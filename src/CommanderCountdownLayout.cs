using System.Globalization;
using System.Numerics;

namespace ZePractice;

public static class CommanderCountdownLayout
{
    public const float Height = 48, Spacing = 30, BaselineAboveIcon = 38;
    public static string Digits(int remaining) => Math.Max(0, remaining).ToString(CultureInfo.InvariantCulture);
    public static float FacingYaw(Vector3 marker, Vector3 eye) => MathF.Atan2(eye.Y - marker.Y, eye.X - marker.X) * 180 / MathF.PI;
    // The installed Workshop meshes are NOT in the OBJ authoring basis. ModelDoc imports
    // compiled (x,y,z) = source (z,x,y): compiled +X is up, +Y is front, +Z is text-right.
    // E.g. compiled "8" bounds are (0,-1.5,-12)..(48,1.5,12), not a 48-high Z box.
    // Source QAngles (-90, facing-90, 0) map these to world up, viewer, screen-right.
    // Keep this correction server-side so already downloaded resources work unchanged.
    public static Vector3 ModelAngles(float facingYaw) => new(-90, facingYaw - 90, 0);
    // Bounds of the installed glyphs, in their compiled basis. A nonzero resource
    // handle alone also accepts the engine's error model, so hot reload checks both.
    public static bool MatchesCompiledBounds(int digit, Vector3 min, Vector3 max)
    {
        if (digit is < 0 or > 9) return false;
        var expectedMin = new Vector3(digit is 1 or 4 or 7 ? 4 : 0, -1.5f,
            digit == 1 ? 7 : digit is 3 or 7 ? -8 : -12);
        var expectedMax = new Vector3(digit is 1 or 4 ? 44 : 48, 1.5f, 12);
        return Vector3.Distance(min, expectedMin) < .05f && Vector3.Distance(max, expectedMax) < .05f;
    }
    public static Vector3 Position(Vector3 marker, float yaw, int digit, int length)
    {
        float angle = yaw * MathF.PI / 180;
        var right = new Vector3(-MathF.Sin(angle), MathF.Cos(angle), 0);
        return marker + new Vector3(0, 0, BaselineAboveIcon) + right * ((digit - (length - 1) * .5f) * Spacing);
    }
}
