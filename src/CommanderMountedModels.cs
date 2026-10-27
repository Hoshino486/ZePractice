using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace ZePractice;

/// <summary>Recover model readiness after CSS reload without replaying map precache.
/// Validate what the engine actually resolves in its current mounted search paths;
/// this never downloads, remounts an addon, or invokes a stale resource manifest.</summary>
public static class CommanderMountedModels
{
    public const string ProbeName = "ze_commander_hotload_model_check";
    public static bool Verify()
    {
        for (int digit = 0; digit < 10; digit++)
        {
            string model = CommanderCountdown.ModelPrefix + digit + ".vmdl";
            var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic")
                ?? throw new InvalidOperationException("Countdown model validation allocation failed");
            try
            {
                using var kv = new CEntityKeyValues();
                kv.SetString("targetname", ProbeName); kv.SetString("model", model);
                kv.SetInt("spawnflags", 256); kv.SetInt("solid", 0);
                kv.SetInt("StartDisabled", 1); kv.SetInt("rendermode", 10);
                prop.DispatchSpawn(kv);
                // StartDisabled sets EF_NODRAW. Remove in the same callback, before
                // a network snapshot; no visible marker or persistent test entity.
                prop.Collision.SolidType = SolidType_t.SOLID_NONE;
                prop.MoveType = prop.ActualMoveType = MoveType_t.MOVETYPE_NONE;
                var state = prop.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState;
                if (state == null || state.Model.Value == 0 || state.ModelName != model ||
                    !CommanderCountdownLayout.MatchesCompiledBounds(digit,
                        (Vector3)prop.Collision.Mins, (Vector3)prop.Collision.Maxs)) return false;
            }
            finally { if (prop.IsValid) prop.Remove(); }
        }
        return true;
    }
}
