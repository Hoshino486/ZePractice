using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace ZePractice;

public sealed partial class PublicGameplayService
{
    private readonly ArtifactOwnership _artifacts = new();
    private long _removedArtifactEntities;
    private ArtifactResetRule[] ArtifactRules => Options.ArtifactResetRules.TryGetValue(ZePracticePlugin.NormalizeMapName(Server.MapName), out var rules) ? rules : [];

    private HookResult ArtifactPickup(CEntityIOOutput output, string name, CEntityInstance activator, CEntityInstance caller, CVariant value, float delay)
    {
        if (!_active || _disposed || !activator.IsValid || !caller.IsValid || activator.DesignerName != "player") return HookResult.Continue;
        string? weaponName = caller.Entity?.Name;
        if (weaponName != null && ArtifactRules.Any(rule => ArtifactResetRule.TrySuffix(weaponName, rule.WeaponName, out _)))
            _artifacts.Pickup(weaponName, activator.EntityHandle.Raw);
        return HookResult.Continue;
    }

    private void RemoveArtifactAttachments(CCSPlayerPawn pawn)
    {
        var rules = ArtifactRules;
        foreach (var handle in pawn.WeaponServices?.MyWeapons.ToArray() ?? [])
            if (handle.Value is { IsValid: true } weapon && weapon.Entity?.Name is { } name &&
                rules.Any(rule => ArtifactResetRule.TrySuffix(name, rule.WeaponName, out _)))
                _artifacts.Pickup(name, pawn.EntityHandle.Raw);
        string[] owned = _artifacts.Release(pawn.EntityHandle.Raw);
        if (owned.Length == 0) return;
        var remove = new HashSet<string>(StringComparer.Ordinal);
        var deactivate = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in owned)
            foreach (var rule in rules)
                if (ArtifactResetRule.TrySuffix(name, rule.WeaponName, out string suffix))
                {
                    foreach (string target in rule.RemoveEntities) remove.Add(target + suffix);
                    foreach (string target in rule.DeactivateEntities) deactivate.Add(target + suffix);
                }
        foreach (var entity in Utilities.GetAllEntities().Where(entity => entity.IsValid).ToArray())
        {
            string? name = entity.Entity?.Name;
            if (name == null || entity.DesignerName is "player" or "cs_player_controller" or "point_script" or "worldent") continue;
            if (deactivate.Contains(name)) entity.AcceptInput("Deactivate", pawn, pawn);
            if (!remove.Contains(name)) continue;
            entity.AcceptInput("KillHierarchy", pawn, pawn);
            _removedArtifactEntities++;
        }
    }

    private HookResult ResetRoundArtifacts(EventRoundStart gameEvent, GameEventInfo information)
    {
        _artifacts.Clear();
        foreach (var player in Utilities.GetPlayers().Where(Player))
        {
            var state = State(player);
            state.TeamReset.Cancel();
            ReleaseMovement(player.PlayerPawn.Value, state);
            state.Pawn = 0;
        }
        return HookResult.Continue;
    }
}
