using System.Drawing;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace ZePractice;

/// <summary>Opaque 3D numerals participate in the same outline pass as artifact models.
/// Each viewer gets an upright, correctly facing copy; other viewers' copies are never transmitted.</summary>
public sealed class CommanderCountdown
{
    public const string ModelPrefix = "models/saibochana/commander/digits/";
    private sealed class Label
    {
        public required uint Viewer;
        public required int? UserId;
        public required int Slot;
        public required long Marker;
        public string Value = "";
        public readonly List<CDynamicProp> Glyphs = new();
        public CPointWorldText? Fallback;
        public float RefreshAt;
    }
    private readonly Dictionary<(uint, long), Label> _labels = new();
    public int Count => _labels.Count;
    public int EntityCount => _labels.Values.Sum(x => x.Glyphs.Count);
    public int FallbackCount => _labels.Values.Count(x => x.Fallback is { IsValid: true });
    public bool ModelResourcesReady { get; set; }
    public static void Precache(ResourceManifest manifest)
    { for (int i = 0; i <= 9; i++) manifest.AddResource(ModelPrefix + i + ".vmdl"); }
    private static bool Viewer(CCSPlayerController p) => p is { IsValid: true, IsHLTV: false } && p.Pawn.Value is { IsValid: true };
    private static Vector3 Eye(CCSPlayerController p)
    {
        var pawn = p.Pawn.Value!;
        var target = pawn.ObserverServices?.ObserverTarget.Value as CBasePlayerPawn;
        if (target is { IsValid: true }) pawn = target;
        var origin = pawn.AbsOrigin!;
        return new(origin.X + pawn.ViewOffset.X, origin.Y + pawn.ViewOffset.Y, origin.Z + pawn.ViewOffset.Z);
    }
    public void Update(IReadOnlyList<CommanderMarkerState.Marker> markers, IReadOnlyList<Color> colors)
    {
        var viewers = Utilities.GetPlayers().Where(Viewer).ToArray();
        foreach (var pair in _labels.ToArray())
            if (!markers.Any(m => m.Id == pair.Value.Marker) || !viewers.Any(p => p.EntityHandle.Raw == pair.Value.Viewer && p.UserId == pair.Value.UserId))
            { Remove(pair.Value); _labels.Remove(pair.Key); }
        foreach (var viewer in viewers)
            foreach (var marker in markers) UpdateLabel(viewer, marker, colors[marker.ColorIndex]);
    }
    public void UpdateLabel(CCSPlayerController viewer, CommanderMarkerState.Marker marker, Color color)
    {
        if (!Viewer(viewer)) return;
        var key = (viewer.EntityHandle.Raw, marker.Id);
        if (!_labels.TryGetValue(key, out var label))
        {
            label = new() { Viewer = viewer.EntityHandle.Raw, UserId = viewer.UserId, Slot = viewer.Slot, Marker = marker.Id };
            _labels.Add(key, label);
        }
        string value = CommanderCountdownLayout.Digits(CommanderMarkerState.Remaining(marker, Server.CurrentTime));
        float yaw = CommanderCountdownLayout.FacingYaw(marker.Position, Eye(viewer));
        if (!ModelResourcesReady)
        {
            // Workshop approval can keep serving the previous bundle. Retain a
            // readable timer without precaching/spawning missing model resources.
            // Fullbright text is NOT advertised as an occlusion/glow equivalent.
            if (label.Fallback is not { IsValid: true })
            {
                var text = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext") ?? throw new InvalidOperationException("Countdown text allocation failed");
                label.Fallback = text; text.Entity!.Name = $"ze_commander_pending_{label.Viewer}_{marker.Id}";
                text.FontSize = 48; text.WorldUnitsPerPx = 1; text.FontName = "Arial";
                text.Fullbright = true; text.Enabled = true; text.Color = color;
                text.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
                text.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_BOTTOM;
                text.ReorientMode = (PointWorldTextReorientMode_t)0; // Per-viewer upright orientation below.
                text.MessageText = value; text.DispatchSpawn();
            }
            if (label.Value != value) { label.Fallback.MessageText = value; Utilities.SetStateChanged(label.Fallback, "CPointWorldText", "m_messageText"); }
            label.Fallback.Teleport(marker.Position + new Vector3(0, 0, CommanderCountdownLayout.BaselineAboveIcon), new Vector3(0, yaw + 90, 90));
            label.Value = value; return;
        }
        if (label.Fallback is { IsValid: true }) { label.Fallback.Remove(); label.Fallback = null; label.Value = ""; }
        if (label.Glyphs.Any(x => !x.IsValid)) { Remove(label); label.Value = ""; }
        // Reuse entity handles whenever digit count is unchanged: at most two or three props per ordinary label.
        while (label.Glyphs.Count > value.Length) { var last = label.Glyphs[^1]; if (last.IsValid) last.Remove(); label.Glyphs.RemoveAt(label.Glyphs.Count - 1); }
        for (int n = 0; n < value.Length; n++)
        {
            string model = ModelPrefix + value[n] + ".vmdl";
            var position = CommanderCountdownLayout.Position(marker.Position, yaw, n, value.Length);
            if (n == label.Glyphs.Count)
                label.Glyphs.Add(CommanderVisualStyle.Spawn(model, $"ze_commander_digit_{label.Viewer}_{marker.Id}_{n}", color, position, true));
            else if (n >= label.Value.Length || value[n] != label.Value[n]) label.Glyphs[n].SetModel(model);
            var glyph = label.Glyphs[n];
            glyph.Teleport(position, CommanderCountdownLayout.ModelAngles(yaw)); // Use the compiled mesh basis, not the source OBJ axes.
            if (!CommanderVisualStyle.Matches(glyph, color) || Server.CurrentTime >= label.RefreshAt) CommanderVisualStyle.Apply(glyph, color);
        }
        if (Server.CurrentTime >= label.RefreshAt) label.RefreshAt = Server.CurrentTime + 2;
        label.Value = value;
    }
    public void Transmit(CCheckTransmitInfo info, CCSPlayerController viewer)
    {
        foreach (var label in _labels.Values)
        {
            if (label.Fallback is { IsValid: true } fallback)
            {
                if (label.Viewer == viewer.EntityHandle.Raw && label.UserId == viewer.UserId) info.TransmitEntities.Add(fallback);
                else info.TransmitEntities.Remove(fallback);
            }
            foreach (var glyph in label.Glyphs)
                if (glyph.IsValid)
                {
                    if (label.Viewer == viewer.EntityHandle.Raw && label.UserId == viewer.UserId) info.TransmitEntities.Add(glyph);
                    else info.TransmitEntities.Remove(glyph);
                }
        }
    }
    private static void Remove(Label label)
    {
        foreach (var glyph in label.Glyphs) if (glyph.IsValid) glyph.Remove(); label.Glyphs.Clear();
        if (label.Fallback is { IsValid: true }) label.Fallback.Remove(); label.Fallback = null;
    }
    public void Reset(bool removeEntities)
    { if (removeEntities) foreach (var label in _labels.Values) Remove(label); _labels.Clear(); }
}
