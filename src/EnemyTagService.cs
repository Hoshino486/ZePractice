using System.Drawing;
using System.Globalization;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ZePractice;

/// <summary>
/// Enemy-only overhead tag. The engine's native team-ID tag (forced on by TeamTagService)
/// only covers teammates, so enemies get a matching plate: name on the first line and the
/// current health percentage on the second, in the target team's colour. The plate is
/// removed per-viewer for same-team targets so teammates never show two labels.
/// </summary>
public sealed class EnemyTagService(ZePracticePlugin plugin) : IDisposable
{
    private sealed class Tag
    {
        public CPointWorldText? Entity;
        public int Team;
        public string Text = "";
    }

    private static readonly Color ZombieColor = Color.FromArgb(255, 255, 96, 96);
    private static readonly Color HumanColor = Color.FromArgb(255, 120, 200, 255);
    private const float Height = 92f;
    private const double UpdateSeconds = 0.2;
    private readonly Dictionary<int, Tag> _tags = new();
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
            foreach (var slot in _tags.Keys.Where(slot => !alive.Contains(slot)).ToArray()) Remove(slot);
        }
        catch (Exception error)
        {
            plugin.Logger.LogDebug(error, "Enemy tag update failed");
        }
    }

    private void Transmit(CCheckTransmitInfoList list)
    {
        if (_disposed || !_active || _tags.Count == 0) return;
        foreach (var (info, viewer) in list)
        {
            if (viewer is not { IsValid: true }) continue;
            // Observers already get the native tag for everyone, so never add a duplicate.
            bool observer = viewer.TeamNum is not (2 or 3);
            foreach (var (slot, tag) in _tags)
            {
                if (tag.Entity is not { IsValid: true }) continue;
                if (observer || viewer.TeamNum == tag.Team) info.TransmitEntities.Remove((int)tag.Entity.Index);
            }
        }
    }

    private void Draw(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        if (!_tags.TryGetValue(player.Slot, out var tag)) _tags[player.Slot] = tag = new Tag();
        int team = player.TeamNum;
        var color = team == 2 ? ZombieColor : HumanColor;
        string text = Label(player, pawn);
        if (tag.Entity is not { IsValid: true })
        {
            var label = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
            if (label == null) return;
            label.FontSize = 22;
            label.WorldUnitsPerPx = 0.12f;
            label.Fullbright = true;
            label.Enabled = true;
            label.Color = color;
            label.FontName = "Arial";
            label.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
            label.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_BOTTOM;
            label.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_AROUND_UP;
            label.MessageText = text;
            label.DispatchSpawn();
            tag.Entity = label;
            tag.Team = team;
            tag.Text = text;
        }
        else
        {
            var label = tag.Entity;
            if (tag.Text != text)
            {
                tag.Text = text;
                label.MessageText = text;
                Utilities.SetStateChanged(label, "CPointWorldText", "m_messageText");
            }
            if (tag.Team != team)
            {
                tag.Team = team;
                label.Color = color;
                Utilities.SetStateChanged(label, "CPointWorldText", "m_Color");
            }
        }
        var origin = pawn.AbsOrigin;
        if (origin != null) tag.Entity.Teleport(new Vector3(origin.X, origin.Y, origin.Z + Height));
    }

    private string Label(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        string name = Clean(player.PlayerName);
        if (plugin.IsPracticeBot(player))
        {
            int index = name.LastIndexOf('_');
            name = index >= 0 && int.TryParse(name[(index + 1)..], out int id) ? $"Bot #{id}" : "Bot";
        }
        else if (player.IsBot) name = "[电脑玩家]" + name;
        double percent = 100.0 * pawn.Health / Math.Max(1, pawn.MaxHealth);
        return $"{name}\n{percent.ToString("0.000", CultureInfo.InvariantCulture)}%";
    }

    private void Remove(int slot)
    {
        if (!_tags.Remove(slot, out var tag)) return;
        if (tag.Entity is { IsValid: true }) tag.Entity.Remove();
    }

    private void RemoveAll()
    {
        foreach (var tag in _tags.Values) if (tag.Entity is { IsValid: true }) tag.Entity.Remove();
        _tags.Clear();
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
