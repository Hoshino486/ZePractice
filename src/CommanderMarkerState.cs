using System.Numerics;

namespace ZePractice;

/// <summary>Engine-independent lifetime, FIFO and colour ownership. All times are game time.</summary>
public sealed class CommanderMarkerState
{
    public const int Limit = 5;
    public const double Lifetime = 10;
    public sealed record Marker(long Id, int ColorIndex, Vector3 Position, double CreatedAt, double ExpiresAt);
    private readonly List<Marker> _markers = new();
    private long _sequence;
    public IReadOnlyList<Marker> Markers => _markers.AsReadOnly();
    public static bool Eligible(bool valid, bool alive, int team) => valid && alive && team == 3;
    public void Clear() => _markers.Clear();
    public void Remove(long id) => _markers.RemoveAll(marker => marker.Id == id);
    public void Expire(double now) => _markers.RemoveAll(marker => marker.ExpiresAt <= now);
    public Marker Place(Vector3 position, double now)
    {
        if (!double.IsFinite(now) || !Finite(position)) throw new ArgumentOutOfRangeException(nameof(position));
        Expire(now);
        if (_markers.Count == Limit) _markers.RemoveAt(0); // extending a marker never changes placement order
        int color = Enumerable.Range(0, Limit).First(index => _markers.All(marker => marker.ColorIndex != index));
        var result = new Marker(++_sequence, color, position, now, now + Lifetime);
        _markers.Add(result);
        return result;
    }
    public Marker? Extend(long id, double now)
    {
        Expire(now);
        int index = _markers.FindIndex(marker => marker.Id == id);
        if (index < 0) return null;
        return _markers[index] = _markers[index] with { ExpiresAt = _markers[index].ExpiresAt + Lifetime };
    }
    public static int Remaining(Marker marker, double now) => (int)Math.Clamp(Math.Ceiling(marker.ExpiresAt - now), 0, int.MaxValue);
    public Marker? AimedAt(Vector3 eye, Vector3 direction, double now)
    {
        if (!Finite(eye) || !Finite(direction) || direction.LengthSquared() < 0.9f) return null;
        direction = Vector3.Normalize(direction);
        return _markers.Where(marker => marker.ExpiresAt > now)
            .Select(marker => (marker, along: Vector3.Dot(marker.Position - eye, direction)))
            .Where(hit => hit.along > 0 && Vector3.DistanceSquared(eye + direction * hit.along, hit.marker.Position) <= 28 * 28)
            .OrderBy(hit => hit.along).Select(hit => hit.marker).FirstOrDefault();
    }
    public static bool Finite(Vector3 vector) => float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);
}
