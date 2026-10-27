using System.Drawing;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ZePractice;

/// <summary>
/// Three stacked overhead labels for every human and bot: name, "HP current/max" and
/// "armor value", tinted by the target team. Native team-ID tags are disabled server-side
/// so teammates do not show a second label.
/// </summary>
public sealed class PlayerPlateService(ZePracticePlugin plugin) : IDisposable
{
    private sealed class Plate
    {
        public CPointWorldText? Name;
        public CPointWorldText? Health;
        public CPointWorldText? Armor;
        public int Team;
        public string NameText = "";
        public string HealthText = "";
        public string ArmorText = "";
    }

    private static readonly Color ZombieColor = Color.FromArgb(255, 255, 96, 96);
    private static readonly Color HumanColor = Color.FromArgb(255, 120, 200, 255);
    private const float NameHeight = 116f;
    private const float HealthHeight = 100f;
    private const float ArmorHeight = 84f;
    private const float FontSize = 34f;
    private const float WorldUnitsPerPx = 0.2f;
    private const double UpdateSeconds = 0.2;
    private readonly Dictionary<int, Plate> _plates = new();
    private double _nextUpdate;
    private bool _active, _disposed;

    public void Initialize()
    {
        plugin.RegisterListener<Listeners.OnTick>(Tick);
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
            var alive = new HashSet<int>();
            foreach (var player in Utilities.GetPlayers())
            {
                if (player is not { IsValid: true } || player.IsHLTV) continue;
                if (player.TeamNum is not (2 or 3)) { Remove(player.Slot); continue; }
                var pawn = player.PlayerPawn.Value;
                if (pawn is not { IsValid: true } || !player.PawnIsAlive || pawn.Health <= 0) { Remove(player.Slot); continue; }
                alive.Add(player.Slot);
                Draw(player, pawn);
            }
            foreach (var slot in _plates.Keys.Where(slot => !alive.Contains(slot)).ToArray()) Remove(slot);
        }
        catch (Exception error)
        {
            plugin.Logger.LogDebug(error, "Player plate update failed");
        }
    }

    private void Draw(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        if (!_plates.TryGetValue(player.Slot, out var plate)) _plates[player.Slot] = plate = new Plate();
        int team = player.TeamNum;
        var color = team == 2 ? ZombieColor : HumanColor;
        string name = NameOf(player);
        string health = $"HP {pawn.Health}/{Math.Max(pawn.Health, pawn.MaxHealth)}";
        string armor = $"甲 {pawn.ArmorValue}";
        if (plate.Name is not { IsValid: true }) plate.Name = Create(name, color);
        if (plate.Health is not { IsValid: true }) plate.Health = Create(health, color);
        if (plate.Armor is not { IsValid: true }) plate.Armor = Create(armor, color);
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
        var origin = pawn.AbsOrigin;
        if (origin == null) return;
        plate.Name.Teleport(new Vector3(origin.X, origin.Y, origin.Z + NameHeight));
        plate.Health.Teleport(new Vector3(origin.X, origin.Y, origin.Z + HealthHeight));
        plate.Armor.Teleport(new Vector3(origin.X, origin.Y, origin.Z + ArmorHeight));
    }

    private string NameOf(CCSPlayerController player)
    {
        string name = Clean(player.PlayerName);
        if (!plugin.IsPracticeBot(player)) return name;
        int index = name.LastIndexOf('_');
        return index >= 0 && int.TryParse(name[(index + 1)..], out int id) ? $"Bot #{id}" : "Bot";
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
        label.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_AROUND_UP;
        label.MessageText = text;
        label.DispatchSpawn();
        return label;
    }

    private void Remove(int slot)
    {
        if (!_plates.Remove(slot, out var plate)) return;
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
        plugin.RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
    }
}
