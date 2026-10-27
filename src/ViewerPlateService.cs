using System.Drawing;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ZePractice;

/// <summary>
/// Per-viewer enemy plate, standing up like the commander marker digits: three worldtext
/// strips (name, HP current/max, armor) rotated 90 degrees and yawed toward that viewer,
/// so each string reads bottom-to-top and always faces the viewer. Teammates keep the
/// native team-ID tag.
/// </summary>
public sealed class ViewerPlateService(ZePracticePlugin plugin) : IDisposable
{
    private sealed class Plate
    {
        public CPointWorldText? Name;
        public CPointWorldText? Health;
        public CPointWorldText? Armor;
        public int Viewer;
        public int Target;
        public int Team;
        public string NameText = "";
        public string HealthText = "";
        public string ArmorText = "";
    }

    private static readonly Color ZombieColor = Color.FromArgb(255, 255, 96, 96);
    private static readonly Color HumanColor = Color.FromArgb(255, 120, 200, 255);
    private const float Height = 104f;
    private const float ColumnSpacing = 16f;
    private const float FontSize = 34f;
    private const float WorldUnitsPerPx = 0.2f;
    private const double UpdateSeconds = 0.2;
    private readonly Dictionary<(int Viewer, int Target), Plate> _plates = new();
    private double _nextUpdate;
    private bool _active, _disposed;

    public void Initialize()
    {
        plugin.RegisterListener<Listeners.OnTick>(Tick);
        plugin.RegisterListener<Listeners.CheckTransmit>(Transmit);
        plugin.RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
    }

    public void OnMapStart() { RemoveAll(); _active = true; _nextUpdate = 0; }

    public void OnMapEnd() { _active = false; RemoveAll(); }

    private void Tick()
    {
        if (_disposed || !_active) return;
        double now = Server.CurrentTime;
        if (now < _nextUpdate) return;
        _nextUpdate = now + UpdateSeconds;
        try
        {
            var alive = new HashSet<(int, int)>();
            var viewers = Utilities.GetPlayers()
                .Where(p => p is { IsValid: true } && !p.IsBot && !p.IsHLTV && p.TeamNum is 2 or 3 && p.PawnIsAlive &&
                            p.PlayerPawn.Value is { IsValid: true, Health: > 0 }).ToArray();
            var targets = Utilities.GetPlayers()
                .Where(p => p is { IsValid: true } && !p.IsHLTV && p.TeamNum is 2 or 3 && p.PawnIsAlive &&
                            p.PlayerPawn.Value is { IsValid: true, Health: > 0 }).ToArray();
            foreach (var viewer in viewers)
            {
                if (viewer.PlayerPawn.Value is not { IsValid: true } eye) continue;
                foreach (var target in targets)
                {
                    if (target.TeamNum == viewer.TeamNum) continue; // teammates use the native tag
                    var pawn = target.PlayerPawn.Value;
                    if (pawn is not { IsValid: true } || pawn.AbsOrigin == null) continue;
                    alive.Add((viewer.Slot, target.Slot));
                    Draw(viewer, target, pawn, eye);
                }
            }
            foreach (var key in _plates.Keys.Where(key => !alive.Contains(key)).ToArray()) Remove(key);
        }
        catch (Exception error)
        {
            plugin.Logger.LogDebug(error, "Viewer plate update failed");
        }
    }

    private void Transmit(CCheckTransmitInfoList list)
    {
        if (_disposed || !_active || _plates.Count == 0) return;
        foreach (var (info, viewer) in list)
        {
            if (viewer is not { IsValid: true }) continue;
            foreach (var plate in _plates.Values)
            {
                foreach (var label in new[] { plate.Name, plate.Health, plate.Armor })
                {
                    if (label is not { IsValid: true }) continue;
                    if (plate.Viewer == viewer.Slot) info.TransmitEntities.Add(label);
                    else info.TransmitEntities.Remove((int)label.Index);
                }
            }
        }
    }

    private void Draw(CCSPlayerController viewer, CCSPlayerController target, CCSPlayerPawn pawn, CCSPlayerPawn eye)
    {
        var key = (viewer.Slot, target.Slot);
        if (!_plates.TryGetValue(key, out var plate)) _plates[key] = plate = new Plate { Viewer = viewer.Slot, Target = target.Slot };
        int team = target.TeamNum;
        var color = team == 2 ? ZombieColor : HumanColor;
        string name = NameOf(target);
        string health = $"HP {pawn.Health}/{Math.Max(pawn.Health, pawn.MaxHealth)}";
        string armor = $"甲 {pawn.ArmorValue}";
        if (plate.Name is not { IsValid: true }) { plate.Name = Create(name, color); plate.NameText = name; }
        if (plate.Health is not { IsValid: true }) { plate.Health = Create(health, color); plate.HealthText = health; }
        if (plate.Armor is not { IsValid: true }) { plate.Armor = Create(armor, color); plate.ArmorText = armor; }
        if (plate.Name is not { IsValid: true } || plate.Health is not { IsValid: true } || plate.Armor is not { IsValid: true }) return;
        if (plate.Team != team)
        {
            plate.Team = team;
            foreach (var label in new[] { plate.Name, plate.Health, plate.Armor })
            {
                label.Color = color;
                Utilities.SetStateChanged(label, "CPointWorldText", "m_Color");
            }
        }
        Update(plate.Name, ref plate.NameText, name);
        Update(plate.Health, ref plate.HealthText, health);
        Update(plate.Armor, ref plate.ArmorText, armor);
        var origin = pawn.AbsOrigin!;
        var eyePosition = eye.AbsOrigin ?? origin;
        var basePosition = new Vector3(origin.X, origin.Y, origin.Z + Height);
        float yaw = MathF.Atan2(eyePosition.Y - basePosition.Y, eyePosition.X - basePosition.X) * 180 / MathF.PI;
        float radians = yaw * MathF.PI / 180;
        var right = new Vector3(-MathF.Sin(radians), MathF.Cos(radians), 0);
        var angles = new Vector3(0, yaw + 90, 90);
        plate.Name.Teleport(basePosition + right * -ColumnSpacing, angles);
        plate.Health.Teleport(basePosition, angles);
        plate.Armor.Teleport(basePosition + right * ColumnSpacing, angles);
    }

    private string NameOf(CCSPlayerController target)
    {
        string name = Clean(target.PlayerName);
        if (plugin.IsPracticeBot(target))
        {
            int index = name.LastIndexOf('_');
            name = index >= 0 && int.TryParse(name[(index + 1)..], out int id) ? $"Bot #{id}" : "Bot";
        }
        else if (target.IsBot) name = "[电脑玩家]" + name;
        return name;
    }

    private static void Update(CPointWorldText label, ref string cached, string text)
    {
        if (cached == text) return;
        cached = text;
        label.MessageText = text;
        Utilities.SetStateChanged(label, "CPointWorldText", "m_messageText");
    }

    private static CPointWorldText? Create(string text, Color color)
    {
        var label = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
        if (label == null) return null;
        label.FontSize = FontSize;
        label.WorldUnitsPerPx = WorldUnitsPerPx;
        label.Fullbright = true;
        label.Enabled = true;
        label.Color = color;
        label.FontName = "Arial";
        label.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
        label.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_BOTTOM;
        label.ReorientMode = (PointWorldTextReorientMode_t)0; // per-viewer angles below
        label.MessageText = text;
        label.DispatchSpawn();
        return label;
    }

    private void Remove((int, int) key)
    {
        if (!_plates.Remove(key, out var plate)) return;
        foreach (var label in new[] { plate.Name, plate.Health, plate.Armor })
            if (label is { IsValid: true }) label.Remove();
    }

    private void RemoveAll()
    {
        foreach (var plate in _plates.Values)
            foreach (var label in new[] { plate.Name, plate.Health, plate.Armor })
                if (label is { IsValid: true }) label.Remove();
        _plates.Clear();
    }

    private static string Clean(string value) => new(value.Where(c => !char.IsControl(c)).Take(32).ToArray());

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _active = false;
        RemoveAll();
        plugin.RemoveListener<Listeners.OnTick>(Tick);
        plugin.RemoveListener<Listeners.CheckTransmit>(Transmit);
        plugin.RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
    }
}
