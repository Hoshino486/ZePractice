using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;

namespace ZePractice;

public static class MapGlowReplication
{
    private static readonly string[] Fields =
    [
        "m_iGlowType", "m_iGlowTeam", "m_nGlowRange", "m_nGlowRangeMin",
        "m_glowColorOverride", "m_bFlashing", "m_flGlowTime", "m_flGlowStartTime",
        "m_bEligibleForScreenHighlight"
    ];
    private static readonly Lazy<int[]> Offsets = new(() => Fields.Select(field =>
    {
        if (!Schema.IsSchemaFieldNetworked("CGlowProperty", field))
            throw new InvalidOperationException($"Glow member is no longer networked: {field}");
        return (int)Schema.GetSchemaOffset("CGlowProperty", field);
    }).ToArray());

    public static void NotifyChanged(CBaseModelEntity entity)
    {
        foreach (int offset in Offsets.Value)
            Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_Glow", offset);
    }
}
