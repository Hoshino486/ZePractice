using System.Text.Json;
using System.Reflection;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Plugin.Host;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using ZePractice;

public sealed class MapMarkerNativeProbe : BasePlugin
{
    public override string ModuleName => "MapMarkerNativeProbe - isolated bots only";
    public override string ModuleVersion => "1.1.0";
    private CCSWeaponBase? _weapon;
    private readonly List<object> _results = new();
    private string? _savedMap;
    private uint[]? _usedButtons, _issuedGuns;

    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static object Markers()
    {
        var manager = (IPluginManager)Field(Application.Instance, "_pluginManager");
        var plugin = manager.GetLoadedPlugins().Single(context => context.Plugin.GetType().FullName == "ZePractice.ZePracticePlugin").Plugin;
        return Field(plugin, "_mapMarkers");
    }
    private static void EmptyServerGuard()
    {
        if (Utilities.GetPlayers().Any(player => !player.IsBot && !player.IsHLTV))
            throw new InvalidOperationException("Maintenance requires zero human players.");
    }

    private static void Guard()
    {
        if (ConVar.Find("hostport")?.GetPrimitiveValue<int>() != 27025 || Utilities.GetPlayers().Any(player => !player.IsBot && !player.IsHLTV))
            throw new InvalidOperationException("Probe requires isolated port 27025 and zero real players.");
    }

    private void Record(string stage, bool passed)
    {
        var result = new { stage, passed, owner = _weapon?.OwnerEntity.Raw, glow = _weapon?.Glow.Glowing,
            type = _weapon?.Glow.GlowType, range = _weapon?.Glow.GlowRange };
        _results.Add(result); Server.PrintToConsole("MARKER_PROBE " + JsonSerializer.Serialize(result));
    }

    private void Later(float seconds, Action action) => AddTimer(seconds, () => { Guard(); action(); }, TimerFlags.STOP_ON_MAPCHANGE);

    public override void Load(bool hotReload)
    {
        AddCommand("css_marker_network_inspect", "Read-only glow replication diagnostic", (caller, command) =>
        {
            if (caller != null) return;
            string[] fields = ["m_fGlowColor", "m_iGlowType", "m_iGlowTeam", "m_nGlowRange", "m_nGlowRangeMin", "m_glowColorOverride", "m_bFlashing", "m_flGlowTime", "m_flGlowStartTime", "m_bEligibleForScreenHighlight", "m_bGlowing"];
            var report = JsonSerializer.Serialize(new
            {
                glowOffset = Schema.GetSchemaOffset("CBaseModelEntity", "m_Glow"),
                glowNetworked = Schema.IsSchemaFieldNetworked("CBaseModelEntity", "m_Glow"),
                fields = fields.Select(field => new { field, offset = Schema.GetSchemaOffset("CGlowProperty", field), networked = Schema.IsSchemaFieldNetworked("CGlowProperty", field) }),
                weapons = Utilities.FindAllEntitiesByDesignerName<CCSWeaponBase>("weapon_").Where(weapon => weapon.IsValid && weapon.Glow.Glowing).Select(weapon => new
                {
                    handle = weapon.EntityHandle.Raw, index = weapon.Index, classname = weapon.DesignerName, name = weapon.Entity?.Name,
                    owned = weapon.OwnerEntity.IsValid, glow = weapon.Glow.Glowing, type = weapon.Glow.GlowType, team = weapon.Glow.GlowTeam,
                    color = weapon.Glow.GlowColorOverride.ToArgb(), vector = new[] { weapon.Glow.GlowColor.X, weapon.Glow.GlowColor.Y, weapon.Glow.GlowColor.Z },
                    model = weapon.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName,
                    effects = weapon.Effects, render = weapon.RenderMode.ToString(), alpha = weapon.Render.A,
                    parent = weapon.CBodyComponent?.SceneNode?.PParent?.Owner?.EntityHandle.Raw
                }).ToArray()
            });
            File.WriteAllText(Path.Combine(ModuleDirectory, "network-inspect.json"), report);
            command.ReplyToCommand(report);
        });
        AddCommand("css_marker_refresh_fields", "Resend existing glow fields without changing gameplay", (caller, command) =>
        {
            if (caller != null) return;
            int count = 0;
            foreach (var weapon in Utilities.FindAllEntitiesByDesignerName<CCSWeaponBase>("weapon_"))
            {
                if (!weapon.IsValid || weapon.OwnerEntity.IsValid || !weapon.Glow.Glowing) continue;
                MapGlowReplication.NotifyChanged(weapon);
                count++;
            }
            command.ReplyToCommand($"Resent individual glow members for {count} existing dropped weapons; no weapon or map state changed.");
        });
        AddCommand("css_marker_save_round", "Save marker-only round state before empty-server hot reload", (caller, command) =>
        {
            if (caller != null) return; EmptyServerGuard();
            var markers = Markers();
            _savedMap = Server.MapName;
            _usedButtons = ((HashSet<uint>)Field(Field(markers, "_buttonRound"), "_used")).ToArray();
            _issuedGuns = ((HashSet<uint>)Field(markers, "_issued")).ToArray();
            File.WriteAllText(Path.Combine(ModuleDirectory, "round-snapshot.json"), JsonSerializer.Serialize(new { map = _savedMap, usedButtons = _usedButtons, issuedGuns = _issuedGuns }));
            command.ReplyToCommand($"Saved marker round state: {_usedButtons.Length} consumed buttons, {_issuedGuns.Length} issued guns.");
        });
        AddCommand("css_marker_restore_round", "Restore marker-only state without restarting the round", (caller, command) =>
        {
            if (caller != null) return; EmptyServerGuard();
            if (_savedMap != Server.MapName || _usedButtons == null || _issuedGuns == null)
                throw new InvalidOperationException("No matching in-memory marker snapshot.");
            var markers = Markers();
            var press = markers.GetType().GetMethod("ButtonPressed", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (uint handle in _usedButtons)
            {
                var button = Utilities.GetEntityFromIndex<CEntityInstance>((int)(handle & 0x7FFF));
                if (button is { IsValid: true } && button.EntityHandle.Raw == handle)
                    press.Invoke(markers, [button.DesignerName, button]);
            }
            ((HashSet<uint>)Field(Field(markers, "_buttonRound"), "_used")).UnionWith(_usedButtons);
            var track = markers.GetType().GetMethod("TrackIssuedGun")!;
            foreach (uint handle in _issuedGuns)
            {
                var gun = Utilities.GetEntityFromIndex<CCSWeaponBase>((int)(handle & 0x7FFF));
                if (gun is { IsValid: true } && gun.EntityHandle.Raw == handle) track.Invoke(markers, [gun]);
            }
            command.ReplyToCommand($"Restored {_usedButtons.Length} consumed buttons and live issued-gun exclusions; no map inputs fired.");
        });
        AddCommand("css_marker_probe", "Isolated pickup/drop/glow-repair test", (caller, command) =>
        {
            if (caller != null) return; Guard(); _results.Clear();
            var bot = Utilities.GetPlayers().First(player => player.IsBot && player.TeamNum == 3 && player.PawnIsAlive);
            bot.PlayerPawn.Value!.ItemServices!.As<CCSPlayer_ItemServices>().RemoveWeapons();
            _weapon = bot.GiveNamedItem<CCSWeaponBase>("weapon_p250") ?? throw new InvalidOperationException("Weapon grant failed.");
            _weapon.Entity!.Name = "ze_probe_map_artifact";
            Later(3, () =>
            {
                Record("held", _weapon.OwnerEntity.IsValid && !_weapon.Glow.Glowing);
                bot.PlayerPawn.Value!.ItemServices!.As<CCSPlayer_ItemServices>().DropActivePlayerWeapon(_weapon);
                Later(0.4f, () =>
                {
                    Record("dropped", !_weapon.OwnerEntity.IsValid && _weapon.Glow.Glowing && _weapon.Glow.GlowType == 3);
                    _weapon.Glow.Glowing = false; _weapon.Glow.GlowType = 0; _weapon.Glow.GlowRange = 0;
                    MapGlowReplication.NotifyChanged(_weapon);
                    Later(0.4f, () =>
                    {
                        Record("overwritten-glow-repaired", _weapon.Glow.Glowing && _weapon.Glow.GlowType == 3 && _weapon.Glow.GlowRange == 3000);
                        _weapon.AcceptInput("Use", bot.PlayerPawn.Value, bot.PlayerPawn.Value);
                        Later(2.0f, () =>
                        {
                            Record("picked-up", _weapon.OwnerEntity.IsValid && !_weapon.Glow.Glowing);
                            bot.PlayerPawn.Value!.WeaponServices!.ActiveWeapon.Raw = _weapon.EntityHandle.Raw;
                            bot.PlayerPawn.Value!.ItemServices!.As<CCSPlayer_ItemServices>().DropActivePlayerWeapon(_weapon);
                            var origin = bot.PlayerPawn.Value.AbsOrigin!;
                            bot.PlayerPawn.Value.Teleport(new System.Numerics.Vector3(origin.X + 256, origin.Y, origin.Z), null, System.Numerics.Vector3.Zero);
                            Later(0.4f, () =>
                            {
                                Record("dropped-again", !_weapon.OwnerEntity.IsValid && _weapon.Glow.Glowing);
                                File.WriteAllText(Path.Combine(ModuleDirectory, "results.json"), JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true }));
                                if (_weapon.IsValid) _weapon.Remove();
                                Server.PrintToConsole("MARKER_PROBE completed; results.json written.");
                            });
                        });
                    });
                });
            });
        });
    }
}
