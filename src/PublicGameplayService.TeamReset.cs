using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;

namespace ZePractice;

public sealed partial class PublicGameplayService
{
    private long _teamResets;

    private void QueueTeamReset(CCSPlayerController player, PlayerState state, int targetTeam, bool changeTeam = false)
    {
        if (targetTeam is < 1 or > 3 || _plugin.IsPracticeBot(player)) return;
        int revision = state.TeamReset.Begin(targetTeam), generation = _generation;
        int? userId = player.UserId;
        ReleaseMovement(player.PlayerPawn.Value, state);
        if (changeTeam && player.TeamNum != targetTeam)
        {
            if (targetTeam == 1) player.ChangeTeam(CounterStrikeSharp.API.Modules.Utils.CsTeam.Spectator);
            else player.SwitchTeam((CounterStrikeSharp.API.Modules.Utils.CsTeam)targetTeam);
        }
        bool Current() => !_disposed && _active && generation == _generation && Player(player) && player.UserId == userId &&
            _players.TryGetValue(player.Slot, out var current) && ReferenceEquals(state, current) && state.TeamReset.IsCurrent(revision, player.TeamNum);
        Later(() =>
        {
            if (!Current()) return;
            var oldPawn = player.PlayerPawn.Value;
            try
            {
                if (oldPawn is { IsValid: true })
                {
                    RemoveArtifactAttachments(oldPawn);
                    oldPawn.ItemServices?.As<CCSPlayer_ItemServices>().RemoveWeapons();
                    if (oldPawn.Health > 0) oldPawn.CommitSuicide(false, true);
                }
            }
            catch (Exception error) { state.TeamReset.Complete(revision); Report("team-reset-release", error); return; }
            _plugin.AddTimer(Math.Max(1.25f, _plugin.Config.RespawnDelaySeconds), () =>
            {
                if (!Current()) return;
                try
                {
                    ClearArtifactIdentity(oldPawn);
                    player.ResponseContexts.RemoveAll();
                    if (targetTeam is 2 or 3)
                    {
                        player.Respawn();
                        ClearArtifactIdentity(player.PlayerPawn.Value);
                        if (player.PlayerPawn.Value is { IsValid: true } pawn)
                        {
                            pawn.GravityScale = 1; pawn.VelocityModifier = 1;
                            pawn.Render = Color.White;
                            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
                            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
                            SetNativeMoveType(pawn, MoveType_t.MOVETYPE_WALK);
                        }
                    }
                    else state.Team = targetTeam;
                    _teamResets++;
                }
                catch (Exception error) { Report("team-reset", error); }
                finally { state.TeamReset.Complete(revision); }
                if (Alive(player)) InitializePawn(player, state, true);
                _plugin.RefreshMapIdentity(player);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        });
    }

    private static void ClearArtifactIdentity(CCSPlayerPawn? pawn)
    {
        if (pawn is not { IsValid: true }) return;
        pawn.ResponseContexts.RemoveAll();
        if (pawn.Entity is { } identity) identity.Name = "";
        pawn.AcceptInput("SetDamageFilter", pawn, pawn, "");
    }
}
