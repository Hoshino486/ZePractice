using System.Drawing;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace ZePractice;

public sealed class CommanderDrawing(ZePracticePlugin plugin)
{
    public const string BeamMaterial = "materials/sprites/laserbeam.vmat";
    private readonly CommanderDrawingState _state = new();
    private readonly Dictionary<long, CBeam> _beams = new();
    private readonly HashSet<string> _errors = new();
    private static readonly TraceOptions TraceMask = new() { InteractsWith = Masks.SolidBrushOnly, InteractsExclude = Contents.Player | Contents.Hitbox | Contents.Trigger };
    private float _next;
    private long _traces, _hits;
    public int Count => _beams.Count;
    public object Diagnostics() => new { segments = _beams.Count, material = BeamMaterial, traceCalls = _traces, traceHits = _hits, errors = _errors.ToArray(),
        expiry = "oldest-to-newest; each segment lives 10 seconds from creation", maxSegments = CommanderDrawingState.MaximumSegments };
    public void Reset(bool remove)
    {
        if (remove) foreach (var beam in _beams.Values) if (beam.IsValid) beam.Remove();
        _beams.Clear(); _state.Clear(); _errors.Clear(); _next = 0; _traces = _hits = 0;
    }
    public void Finish() => _state.Finish(Server.CurrentTime);
    public void Tick(CCSPlayerController? leader)
    {
        if (Server.CurrentTime < _next) return;
        _next = Server.CurrentTime + 0.05f;
        try
        {
            if (leader is { IsValid: true, PawnIsAlive: true, TeamNum: 3 } && (leader.Buttons & PlayerButtons.Reload) != 0)
            {
                if (TraceAim(leader) is { } hit) AddSample(hit);
                else Finish();
            }
            else Finish();
            _state.Expire(Server.CurrentTime); Sync();
        }
        catch (Exception error)
        {
            Finish();
            if (_errors.Add(error.GetType().Name)) plugin.Logger.LogError(error, "Commander drawing failed");
        }
    }
    public Vector3? TraceAim(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is not { IsValid: true } || pawn.AbsOrigin is not { } origin) return null;
        var start = new Vector(origin.X + pawn.ViewOffset.X, origin.Y + pawn.ViewOffset.Y, origin.Z + pawn.ViewOffset.Z);
        float pitch = pawn.EyeAngles.X * MathF.PI / 180, yaw = pawn.EyeAngles.Y * MathF.PI / 180;
        var end = new Vector(start.X + MathF.Cos(pitch) * MathF.Cos(yaw) * 131072,
            start.Y + MathF.Cos(pitch) * MathF.Sin(yaw) * 131072, start.Z - MathF.Sin(pitch) * 131072);
        var trace = Trace.TraceEndShape(start, end, pawn, TraceMask); _traces++;
        if (!trace.DidHit() || trace.IsAllSolid) return null;
        var endpoint = trace.EndPos; var normal = trace.Normal;
        var hit = new Vector3(endpoint.X + normal.X * 2, endpoint.Y + normal.Y * 2, endpoint.Z + normal.Z * 2);
        if (!CommanderMarkerState.Finite(hit)) return null;
        _hits++; return hit;
    }
    public void AddSample(Vector3 point)
    {
        var segment = _state.Sample(point, Server.CurrentTime);
        if (segment == null) return;
        CBeam? beam = null;
        try
        {
            // CBeam alone ignores the texture key and leaves BaseMaterial null.
            // CEnvBeam owns/loads the precached material through normal spawn keys.
            beam = Utilities.CreateEntityByName<CEnvBeam>("env_beam") ?? throw new InvalidOperationException("Beam entity allocation failed");
            string name = "ze_commander_line_" + segment.Id;
            using (var keys = new CEntityKeyValues())
            {
                keys.SetString("targetname", name); keys.SetString("texture", BeamMaterial);
                keys.SetString("LightningStart", name);
                keys.SetVector("origin", new Vector(segment.Start.X, segment.Start.Y, segment.Start.Z));
                keys.SetVector("targetpoint", new Vector(segment.End.X, segment.End.Y, segment.End.Z));
                keys.SetFloat("life", 0); keys.SetFloat("BoltWidth", 4); keys.SetFloat("damage", 0);
                keys.SetInt("spawnflags", 1); keys.SetInt("TouchType", 0); keys.SetInt("ClipStyle", 0);
                keys.SetColor("rendercolor", Color.FromArgb(255, 25, 210, 255)); keys.SetInt("renderamt", 255);
                keys.SetFloat("HDRColorScale", 2);
                beam.DispatchSpawn(keys);
            }
            if (beam.BaseMaterial.Value == 0) throw new InvalidOperationException("Commander beam material not resident: " + BeamMaterial);
            beam.Width = 4; beam.EndWidth = 4; beam.FadeLength = 0; beam.HDRColorScale = 2;
            beam.BeamType = BeamType_t.BEAM_POINTS; beam.Damage = 0; beam.TurnedOff = false;
            beam.Teleport(segment.Start);
            beam.EndPos.X = segment.End.X; beam.EndPos.Y = segment.End.Y; beam.EndPos.Z = segment.End.Z;
            Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");
            Utilities.SetStateChanged(beam, "CBeam", "m_fWidth");
            Utilities.SetStateChanged(beam, "CBeam", "m_nBeamType");
            Utilities.SetStateChanged(beam, "CBaseModelEntity", "m_clrRender");
            _beams.Add(segment.Id, beam); Sync();
        }
        catch { if (beam is { IsValid: true }) beam.Remove(); _state.Remove(segment.Id); throw; }
    }
    private void Sync()
    {
        var ids = _state.Segments.Select(s => s.Id).ToHashSet();
        foreach (var pair in _beams.ToArray())
            if (!ids.Contains(pair.Key) || !pair.Value.IsValid)
            { if (pair.Value.IsValid) pair.Value.Remove(); _beams.Remove(pair.Key); _state.Remove(pair.Key); }
    }
    public void Transmit(CCheckTransmitInfo info)
    { foreach (var beam in _beams.Values) if (beam.IsValid) info.TransmitEntities.Add(beam); }
}
