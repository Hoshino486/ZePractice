using System.Drawing;
using System.Globalization;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace ZePractice;

public static class CommanderVisualStyle
{
    // base.fgd Glow: 0 = always visible. Do not send int.MaxValue through range quantization.
    public const int UnlimitedRange = 0;
    public static CDynamicProp Spawn(string model, string name, Color color, Vector3 position, bool tint)
    {
        var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic") ?? throw new InvalidOperationException("Commander prop allocation failed");
        try
        {
            using var kv = new CEntityKeyValues();
            kv.SetString("targetname", name); kv.SetString("model", model);
            kv.SetInt("spawnflags", 256); kv.SetInt("solid", 0);
            kv.SetInt("glowstate", 3); kv.SetInt("glowrange", UnlimitedRange); kv.SetInt("glowrangemin", 0);
            kv.SetInt("glowteam", -1); kv.SetColor("glowcolor", color);
            kv.SetInt("renderamt", 255); kv.SetColor("rendercolor", tint ? color : Color.White);
            kv.SetFloat("fademindist", -1); kv.SetFloat("fademaxdist", 0); kv.SetInt("object_culling", 3);
            prop.DispatchSpawn(kv);
            prop.Collision.SolidType = SolidType_t.SOLID_NONE; prop.Collision.SolidFlags = 4; prop.Collision.CollisionGroup = 1;
            prop.MoveType = prop.ActualMoveType = MoveType_t.MOVETYPE_NONE;
            prop.AcceptInput("DisableCollision"); prop.AcceptInput("StartGlowing");
            prop.Teleport(position); Apply(prop, color);
            return prop;
        }
        catch { if (prop.IsValid) prop.Remove(); throw; }
    }
    public static bool Matches(CBaseModelEntity entity, Color color)
    {
        var g = entity.Glow;
        return g.Glowing && g.GlowType == 3 && g.GlowTeam == -1 && g.GlowRange == UnlimitedRange && g.GlowRangeMin == 0 &&
            g.GlowColorOverride.ToArgb() == color.ToArgb() && g.EligibleForScreenHighlight && !g.Flashing && g.GlowTime == 0;
    }
    public static void Apply(CBaseModelEntity entity, Color color)
    {
        var g = entity.Glow;
        g.GlowType = 3; g.GlowTeam = -1; g.GlowRange = UnlimitedRange; g.GlowRangeMin = 0;
        g.GlowColorOverride = color; g.GlowColor.X = color.R / 255f; g.GlowColor.Y = color.G / 255f; g.GlowColor.Z = color.B / 255f;
        g.Flashing = false; g.EligibleForScreenHighlight = true; g.GlowTime = 0; g.GlowStartTime = Server.CurrentTime; g.Glowing = true;
        MapGlowReplication.NotifyChanged(entity);
    }
}
