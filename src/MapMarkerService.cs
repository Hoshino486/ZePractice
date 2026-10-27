using System.Drawing;
using System.Numerics;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ZePractice;

public sealed class MapMarkerService : IDisposable
{
    public const string ChickenModel = "models/chicken/chicken.vmdl";
    private static readonly Color ButtonColor = Color.FromArgb(255, 40, 255, 65);
    private static readonly Color GunColor = Color.FromArgb(255, 0, 255, 185);
    private readonly ZePracticePlugin _plugin;
    private readonly Dictionary<uint, ButtonMarker> _buttons = new();
    private readonly Dictionary<uint, WeaponMarker> _weapons = new();
    private readonly HashSet<uint> _issued = new();
    private readonly HashSet<string> _reported = new();
    private readonly ButtonMarkerRoundState _buttonRound = new();
    private readonly List<(string Class, EntityIO.EntityOutputHandler Handler)> _buttonHooks = new();
    private bool _active, _disposed;
    private int _generation, _errors, _observedButtons;
    private float _nextTick, _nextScan;
    private long _glowRepairs;
    private MapMarkerOptions Options => _plugin.Config.MapMarkers;
    private sealed record ButtonMarker(CBaseModelEntity Button, CDynamicProp Chicken, uint ChickenHandle, Vector3 ModelCenter);
    private sealed class WeaponMarker(CCSWeaponBase weapon)
    {
        public readonly CCSWeaponBase Weapon = weapon;
        public GlowSnapshot? Original;
        public float NextRefresh;
    }
    private sealed record GlowSnapshot(int Type, int Team, int Range, int Minimum, Color Color, Vector3 VectorColor,
        bool Flashing, bool Highlight, bool Glowing, float Time, float Started)
    {
        public static GlowSnapshot Capture(CGlowProperty glow) => new(glow.GlowType, glow.GlowTeam, glow.GlowRange, glow.GlowRangeMin,
            glow.GlowColorOverride, new(glow.GlowColor.X, glow.GlowColor.Y, glow.GlowColor.Z), glow.Flashing,
            glow.EligibleForScreenHighlight, glow.Glowing, glow.GlowTime, glow.GlowStartTime);
        public void Restore(CBaseModelEntity entity)
        {
            var glow = entity.Glow;
            glow.GlowType = Type; glow.GlowTeam = Team; glow.GlowRange = Range; glow.GlowRangeMin = Minimum;
            glow.GlowColorOverride = Color; glow.GlowColor.X = VectorColor.X; glow.GlowColor.Y = VectorColor.Y; glow.GlowColor.Z = VectorColor.Z;
            glow.Flashing = Flashing; glow.EligibleForScreenHighlight = Highlight; glow.Glowing = Glowing;
            glow.GlowTime = Time; glow.GlowStartTime = Started;
            MapGlowReplication.NotifyChanged(entity);
        }
    }

    public MapMarkerService(ZePracticePlugin plugin) => _plugin = plugin;
    public void Initialize()
    {
        _plugin.RegisterListener<Listeners.OnServerPrecacheResources>(Precache);
        _plugin.RegisterListener<Listeners.OnEntitySpawned>(Spawned);
        _plugin.RegisterListener<Listeners.OnTick>(Tick);
        foreach (string classname in new[] { "func_button", "func_rot_button", "momentary_rot_button" })
        {
            EntityIO.EntityOutputHandler handler = (output, name, activator, caller, value, delay) =>
                ButtonPressed(classname, caller);
            _plugin.HookEntityOutput(classname, "OnPressed", handler, HookMode.Post);
            _buttonHooks.Add((classname, handler));
        }
        _plugin.AddZeCommand("css_ze_markers_status", "地图按钮鸡与地图枪轮廓诊断", StatusCommand);
    }

    private void Precache(ResourceManifest manifest)
    { if (Options.ButtonChickens) manifest.AddResource(ChickenModel); }

    public void OnMapStart()
    {
        _generation++; _active = true; _buttons.Clear(); _weapons.Clear(); _issued.Clear(); _reported.Clear();
        _buttonRound.Reset();
        _errors = _observedButtons = 0; _glowRepairs = 0; _nextTick = _nextScan = 0;
        Later(() => Scan(true));
    }

    public void OnMapEnd()
    { _generation++; _active = false; _buttons.Clear(); _weapons.Clear(); _issued.Clear(); _buttonRound.Reset(); }

    public void OnRoundStart()
    {
        if (!_active || _disposed) return;
        _buttonRound.Reset();
        int generation = _generation;
        _plugin.AddTimer(0.2f, () =>
        {
            if (!_disposed && _active && generation == _generation) Scan(false);
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void Later(Action action)
    {
        int generation = _generation;
        Server.NextFrame(() => { if (!_disposed && _active && generation == _generation) action(); });
    }

    private HookResult ButtonPressed(string classname, CEntityInstance caller)
    {
        if (!_active || _disposed || caller is not { IsValid: true }) return HookResult.Continue;
        try
        {
            uint handle = caller.EntityHandle.Raw;
            if (!_buttonRound.Consume(classname, "OnPressed", handle)) return HookResult.Continue;
            if (_buttons.Remove(handle, out var marker) && Valid(marker.Chicken, marker.ChickenHandle)) marker.Chicken.Remove();
        }
        catch (Exception error) { Report("button-pressed", error); }
        return HookResult.Continue;
    }

    private void Spawned(CEntityInstance entity)
    {
        if (!_active || _disposed || !entity.IsValid) return;
        string name = entity.DesignerName;
        if (!MapMarkerLogic.IsButton(name) && !name.StartsWith("weapon_", StringComparison.Ordinal)) return;
        uint handle = entity.EntityHandle.Raw;
        Later(() =>
        {
            if (!entity.IsValid || entity.EntityHandle.Raw != handle) return;
            try
            {
                if (MapMarkerLogic.IsButton(name)) AddButton(entity.As<CBaseModelEntity>());
                else AddWeapon(entity.As<CCSWeaponBase>(), false);
            }
            catch (Exception error) { Report("spawn", error); }
        });
    }

    public void TrackIssuedGun(CCSWeaponBase weapon)
    {
        if (!weapon.IsValid) return;
        if (MapMarkerLogic.HasMapIdentity(weapon.UniqueHammerID, weapon.Entity?.Name)) return;
        uint handle = weapon.EntityHandle.Raw;
        _issued.Add(handle);
        if (_weapons.Remove(handle, out var entry)) entry.Original?.Restore(weapon);
    }

    private void Scan(bool initial)
    {
        try
        {
            _observedButtons = 0;
            foreach (string name in new[] { "func_button", "func_rot_button", "momentary_rot_button" })
                foreach (var button in Utilities.FindAllEntitiesByDesignerName<CBaseModelEntity>(name))
                {
                    _observedButtons++;
                    try { AddButton(button); } catch (Exception error) { Report("button", error); }
                }
            foreach (var weapon in Utilities.FindAllEntitiesByDesignerName<CCSWeaponBase>("weapon_"))
                try { AddWeapon(weapon, initial); } catch (Exception error) { Report("weapon", error); }
            _issued.RemoveWhere(handle => Utilities.GetEntityFromIndex<CEntityInstance>((int)(handle & 0x7FFF)) is not { IsValid: true } entity || entity.EntityHandle.Raw != handle);
        }
        catch (Exception error) { Report("scan", error); }
    }

    private static bool Valid(CEntityInstance entity, uint handle) => entity.IsValid && entity.EntityHandle.Raw == handle;
    private static Vector3 Origin(CBaseEntity entity) => entity.AbsOrigin is { } origin ? new(origin.X, origin.Y, origin.Z) : Vector3.Zero;
    private static Vector3 Angles(CBaseEntity entity) => entity.AbsRotation is { } angles ? new(angles.X, angles.Y, angles.Z) : Vector3.Zero;
    private static Vector3 Center(CBaseModelEntity entity) => MapMarkerLogic.ButtonCenter(Origin(entity),
        new(entity.Collision.Mins.X, entity.Collision.Mins.Y, entity.Collision.Mins.Z),
        new(entity.Collision.Maxs.X, entity.Collision.Maxs.Y, entity.Collision.Maxs.Z), Angles(entity));

    private void AddButton(CBaseModelEntity button)
    {
        if (!Options.ButtonChickens || !button.IsValid || !MapMarkerLogic.IsButton(button.DesignerName) ||
            !_buttonRound.CanMark(button.EntityHandle.Raw) || _buttons.ContainsKey(button.EntityHandle.Raw)) return;
        if (_buttons.Count >= Options.MaximumButtonMarkers) return;
        var chicken = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic_override");
        if (chicken == null) return;
        try
        {
            chicken.Entity!.Name = "ze_button_marker_" + button.EntityHandle.Raw;
            chicken.SetModel(ChickenModel);
            chicken.DispatchSpawn();
            chicken.Collision.SolidType = SolidType_t.SOLID_NONE; chicken.Collision.SolidFlags = 4; chicken.Collision.CollisionGroup = 1;
            chicken.MoveType = chicken.ActualMoveType = MoveType_t.MOVETYPE_NONE;
            chicken.TakeDamageFlags = TakeDamageFlags_t.DFLAG_SUPPRESS_HEALTH_CHANGES | TakeDamageFlags_t.DFLAG_SUPPRESS_PHYSICS_FORCE;
            chicken.Render = Color.FromArgb(255, 120, 255, 120);
            chicken.AcceptInput("DisableCollision");
            Utilities.SetStateChanged(chicken, "CBaseModelEntity", "m_Collision");
            Utilities.SetStateChanged(chicken, "CBaseModelEntity", "m_clrRender");
            var modelCenter = new Vector3((chicken.Collision.Mins.X + chicken.Collision.Maxs.X) * 0.5f,
                (chicken.Collision.Mins.Y + chicken.Collision.Maxs.Y) * 0.5f, (chicken.Collision.Mins.Z + chicken.Collision.Maxs.Z) * 0.5f);
            var entry = new ButtonMarker(button, chicken, chicken.EntityHandle.Raw, modelCenter);
            Follow(entry);
            EnableGlow(chicken, ButtonColor);
            _buttons.Add(button.EntityHandle.Raw, entry);
        }
        catch
        {
            if (chicken.IsValid) chicken.Remove();
            throw;
        }
    }

    private static void Follow(ButtonMarker entry)
    {
        var rotation = new Vector3(0, Angles(entry.Button).Y, 0);
        var position = Center(entry.Button) - MapMarkerLogic.Rotate(entry.ModelCenter, rotation);
        if (Vector3.DistanceSquared(position, Origin(entry.Chicken)) > 0.001f || Vector3.DistanceSquared(rotation, Angles(entry.Chicken)) > 0.001f)
            entry.Chicken.Teleport(position, rotation);
    }

    private void AddWeapon(CCSWeaponBase weapon, bool initial)
    {
        if (!Options.MapWeaponGlow || !weapon.IsValid || _weapons.ContainsKey(weapon.EntityHandle.Raw)) return;
        bool firearm = weapon.VData?.GearSlot is gear_slot_t.GEAR_SLOT_RIFLE or gear_slot_t.GEAR_SLOT_PISTOL;
        if (!MapMarkerLogic.IsMapGun(firearm, _issued.Contains(weapon.EntityHandle.Raw), weapon.UniqueHammerID, weapon.Entity?.Name, initial, weapon.OwnerEntity.IsValid)) return;
        var entry = new WeaponMarker(weapon);
        _weapons.Add(weapon.EntityHandle.Raw, entry);
        UpdateWeapon(entry);
    }

    private void UpdateWeapon(WeaponMarker entry)
    {
        if (entry.Weapon.OwnerEntity.IsValid || !Options.MapWeaponGlow)
        { entry.Original?.Restore(entry.Weapon); entry.Original = null; return; }
        bool first = entry.Original == null;
        entry.Original ??= GlowSnapshot.Capture(entry.Weapon.Glow);
        if (!first && GlowMatches(entry.Weapon, GunColor) && Server.CurrentTime < entry.NextRefresh) return;
        if (!first && !GlowMatches(entry.Weapon, GunColor)) _glowRepairs++;
        EnableGlow(entry.Weapon, GunColor);
        entry.NextRefresh = Server.CurrentTime + 2;
    }

    private bool GlowMatches(CBaseModelEntity entity, Color color)
    {
        var glow = entity.Glow;
        return glow.Glowing && glow.GlowType == 3 && glow.GlowTeam == -1 && glow.GlowRange == Options.GlowRange &&
            glow.GlowRangeMin == 0 && glow.GlowColorOverride.ToArgb() == color.ToArgb() &&
            glow.EligibleForScreenHighlight && !glow.Flashing && glow.GlowTime == 0;
    }

    private void EnableGlow(CBaseModelEntity entity, Color color)
    {
        var glow = entity.Glow;
        glow.GlowType = 3; glow.GlowTeam = -1; glow.GlowRange = Options.GlowRange; glow.GlowRangeMin = 0;
        glow.GlowColorOverride = color; glow.Flashing = false; glow.EligibleForScreenHighlight = true;
        glow.GlowStartTime = Server.CurrentTime; glow.GlowTime = 0; glow.Glowing = true;
        MapGlowReplication.NotifyChanged(entity);
    }

    private void Tick()
    {
        if (!_active || _disposed || Server.CurrentTime < _nextTick) return;
        _nextTick = Server.CurrentTime + 0.1f;
        foreach (var pair in _buttons.ToArray())
        {
            try
            {
                var entry = pair.Value;
                if (!Valid(entry.Button, pair.Key) || !Valid(entry.Chicken, entry.ChickenHandle) || !Options.ButtonChickens)
                { _buttons.Remove(pair.Key); if (Valid(entry.Chicken, entry.ChickenHandle)) entry.Chicken.Remove(); continue; }
                Follow(entry);
            }
            catch (Exception error) { Report("follow", error); }
        }
        foreach (var pair in _weapons.ToArray())
        {
            if (!Valid(pair.Value.Weapon, pair.Key)) { _weapons.Remove(pair.Key); continue; }
            try { UpdateWeapon(pair.Value); } catch (Exception error) { Report("glow", error); }
        }
        if (Server.CurrentTime < _nextScan) return;
        _nextScan = Server.CurrentTime + 2;
        Scan(false);
    }

    private void Report(string area, Exception error)
    {
        _errors++;
        if (_reported.Add(area)) _plugin.Logger.LogError(error, "Map markers failed in {Area}", area);
    }

    public string Diagnostics() => JsonSerializer.Serialize(new
    {
        active = _active, buttons = _observedButtons, chickens = _buttons.Count, usedButtons = _buttonRound.UsedCount, mapGuns = _weapons.Count,
        glowingGuns = _weapons.Values.Count(entry => entry.Original != null && entry.Weapon.IsValid && GlowMatches(entry.Weapon, GunColor)),
        glowRepairs = _glowRepairs, errors = _errors, replication = "glow-members-v2",
        visibility = "server-state-only",
        weapons = _weapons.Values.Where(entry => entry.Weapon.IsValid).Select(entry => new
        {
            handle = entry.Weapon.EntityHandle.Raw, name = entry.Weapon.Entity?.Name, classname = entry.Weapon.DesignerName,
            owned = entry.Weapon.OwnerEntity.IsValid, glowing = entry.Weapon.Glow.Glowing,
            type = entry.Weapon.Glow.GlowType, range = entry.Weapon.Glow.GlowRange
        }).ToArray(),
        range = Options.GlowRange, model = ChickenModel, buttonColor = "40,255,65", weaponColor = "0,255,185",
        buttonLimit = Options.MaximumButtonMarkers
    });
    private void StatusCommand(CCSPlayerController? player, CommandInfo command) => _plugin.Reply(player, Diagnostics());

    public void Dispose()
    {
        _disposed = true;
        foreach (var hook in _buttonHooks) _plugin.UnhookEntityOutput(hook.Class, "OnPressed", hook.Handler, HookMode.Post);
        _buttonHooks.Clear();
        _plugin.RemoveListener<Listeners.OnServerPrecacheResources>(Precache);
        _plugin.RemoveListener<Listeners.OnEntitySpawned>(Spawned); _plugin.RemoveListener<Listeners.OnTick>(Tick);
        _plugin.RemoveZeCommand("css_ze_markers_status", StatusCommand);
        foreach (var entry in _buttons.Values)
            if (Valid(entry.Chicken, entry.ChickenHandle)) entry.Chicken.Remove();
        foreach (var pair in _weapons)
            if (Valid(pair.Value.Weapon, pair.Key)) pair.Value.Original?.Restore(pair.Value.Weapon);
        OnMapEnd();
    }
}
