using System.Numerics;

namespace ZePractice;

public static class MapMarkerLogic
{
    public static bool IsButton(string designerName) => designerName is "func_button" or "func_rot_button" or "momentary_rot_button";

    public static bool IsMapGun(bool firearm, bool issued, string? hammerId, string? name, bool initialWorldGun, bool owned)
        => firearm && (HasMapIdentity(hammerId, name) || !issued && initialWorldGun && !owned);

    public static bool HasMapIdentity(string? hammerId, string? name) => !string.IsNullOrWhiteSpace(hammerId) || !string.IsNullOrWhiteSpace(name);

    public static Vector3 Rotate(Vector3 local, Vector3 degrees)
    {
        float pitch = degrees.X * MathF.PI / 180, yaw = degrees.Y * MathF.PI / 180, roll = degrees.Z * MathF.PI / 180;
        float sinPitch = MathF.Sin(pitch), cosPitch = MathF.Cos(pitch), sinYaw = MathF.Sin(yaw), cosYaw = MathF.Cos(yaw);
        float sinRoll = MathF.Sin(roll), cosRoll = MathF.Cos(roll);
        var forward = new Vector3(cosPitch * cosYaw, cosPitch * sinYaw, -sinPitch);
        var left = new Vector3(sinRoll * sinPitch * cosYaw - cosRoll * sinYaw, sinRoll * sinPitch * sinYaw + cosRoll * cosYaw, sinRoll * cosPitch);
        var up = new Vector3(cosRoll * sinPitch * cosYaw + sinRoll * sinYaw, cosRoll * sinPitch * sinYaw - sinRoll * cosYaw, cosRoll * cosPitch);
        return local.X * forward + local.Y * left + local.Z * up;
    }

    public static Vector3 ButtonCenter(Vector3 origin, Vector3 mins, Vector3 maxs, Vector3 rotation)
        => origin + Rotate((mins + maxs) * 0.5f, rotation);
}

public sealed class ButtonMarkerRoundState
{
    private readonly HashSet<uint> _used = new();
    public int UsedCount => _used.Count;
    public bool CanMark(uint handle) => !_used.Contains(handle);
    public bool Consume(string classname, string output, uint handle) =>
        MapMarkerLogic.IsButton(classname) && output == "OnPressed" && _used.Add(handle);
    public void Reset() => _used.Clear();
}
