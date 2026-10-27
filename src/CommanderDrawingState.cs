using System.Numerics;

namespace ZePractice;

/// <summary>Each segment lives ten seconds from creation; older segments expire first.</summary>
public sealed class CommanderDrawingState
{
    public const double Lifetime = 10;
    public const int MaximumSegments = 384;
    private readonly List<Segment> _segments = new();
    private long _sequence;
    private Vector3? _previous;
    public sealed record Segment(long Id, Vector3 Start, Vector3 End, double ExpiresAt);
    public IReadOnlyList<Segment> Segments => _segments.AsReadOnly();
    public void Clear() { _segments.Clear(); _previous = null; }
    public Segment? Sample(Vector3 position, double now)
    {
        if (!CommanderMarkerState.Finite(position) || !double.IsFinite(now)) { Finish(now); return null; }
        Expire(now);
        if (_previous is not { } previous) { _previous = position; return null; }
        float distance = Vector3.DistanceSquared(previous, position);
        if (distance > 1024 * 1024) { Finish(now); _previous = position; return null; }
        if (distance < 4 * 4) return null;
        _previous = position;
        var segment = new Segment(++_sequence, previous, position, now + Lifetime);
        _segments.Add(segment);
        if (_segments.Count > MaximumSegments) _segments.RemoveAt(0);
        return segment;
    }
    // Releasing R or leaving commander mode breaks the stroke, never resets its timers.
    public void Finish(double now) => _previous = null;
    public void Expire(double now) => _segments.RemoveAll(s => s.ExpiresAt <= now);
    public void Remove(long id) => _segments.RemoveAll(s => s.Id == id);
}
