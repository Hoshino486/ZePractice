using System.Numerics;

namespace ZePractice;

/// <summary>Samples foot positions, not camera height. A jump is an upward grounded-to-air
/// transition; walking off a ledge, noclip, ladders, water and teleports never create records.</summary>
public sealed class MovementTelemetryLogic
{
    public readonly record struct Sample(double Time, uint Pawn, Vector3 Position, Vector3 Velocity, bool Grounded, bool Walking);
    public readonly record struct Reading(float GroundSpeed, float VerticalSpeed, float JumpHeight, bool Jumping, string State);
    private Sample? _previous;
    private bool _jumping;
    private float _takeoff, _peak, _lastHeight;
    public Reading Current { get; private set; }
    public void Reset() { _previous = null; _jumping = false; _takeoff = _peak = _lastHeight = 0; Current = default; }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    public Reading Update(Sample s)
    {
        if (!double.IsFinite(s.Time) || !Finite(s.Position) || !Finite(s.Velocity)) { Reset(); return Current; }
        bool discontinuity = false;
        if (_previous is { } p)
        {
            double dt = s.Time - p.Time;
            // A generous velocity-aware residual accepts normal high-speed movement but rejects teleport offsets.
            discontinuity = p.Pawn != s.Pawn || dt <= 0 || dt > .25 ||
                Vector3.Distance(s.Position, p.Position) > Math.Max(s.Velocity.Length(), p.Velocity.Length()) * dt + 64;
            if (discontinuity) { _jumping = false; _lastHeight = _peak = 0; }
            else if (s.Walking && p.Walking)
            {
                if (p.Grounded && !s.Grounded && s.Velocity.Z > 0)
                { _takeoff = p.Position.Z; _peak = Math.Max(0, s.Position.Z - _takeoff); _jumping = true; }
                if (_jumping)
                {
                    _peak = Math.Max(_peak, s.Position.Z - _takeoff);
                    if (s.Grounded) { _lastHeight = _peak; _jumping = false; }
                }
            }
        }
        if (!s.Walking) { _jumping = false; _peak = _lastHeight = 0; }
        _previous = s;
        // Double intermediates keep even malformed finite velocities from overflowing the display.
        float speed = (float)Math.Min(999999, Math.Sqrt((double)s.Velocity.X * s.Velocity.X + (double)s.Velocity.Y * s.Velocity.Y));
        Current = new(speed, Math.Clamp(s.Velocity.Z, -999999, 999999), Math.Clamp(_jumping ? _peak : _lastHeight, 0, 999999), _jumping,
            !s.Walking ? "特殊移动" : discontinuity ? "已重置" : s.Grounded ? "地面" : _jumping ? "跳跃" : "空中");
        return Current;
    }
}
