using System.Drawing;
using System.Globalization;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

namespace ZePractice;

public sealed partial class PublicGameplayService
{
    private void MaintainMovement(CCSPlayerController player, PlayerState state, float now)
    {
        var pawn = player.PlayerPawn.Value!;
        if (pawn.MovementServices is { } services)
        {
            var movement = services.As<CCSPlayer_MovementServices>();
            if (movement.Stamina != 0)
            {
                movement.Stamina = 0;
                Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pMovementServices");
            }
        }
        if (state.Noclip)
        {
            if (pawn.MoveType != MoveType_t.MOVETYPE_NOCLIP || pawn.ActualMoveType != MoveType_t.MOVETYPE_NOCLIP)
                SetNativeMoveType(pawn, MoveType_t.MOVETYPE_NOCLIP);
            state.Jumps.Reset(); return;
        }
        if (!state.BunnyHop || pawn.MoveType != MoveType_t.MOVETYPE_WALK || (pawn.Flags & 64) != 0 || _groundEntity == null)
        { state.Jumps.Reset(); return; }
        bool grounded = (pawn.Flags & 1) != 0;
        state.Jumps.ObserveGround(grounded, now);
        if (grounded && (player.Buttons & PlayerButtons.Jump) != 0 && now - state.Jumps.LastJumpAt >= 0.1f)
        {
            float impulse = 301.993f;
            if (float.TryParse(ConVar.Find("sv_jump_impulse")?.StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float configured) && configured is > 0 and < 2000)
                impulse = configured;
            state.Jumps.Jump(now);
            _groundEntity.Invoke(pawn.Handle, IntPtr.Zero, IntPtr.Zero);
            pawn.Teleport(velocity: new Vector3(pawn.AbsVelocity.X, pawn.AbsVelocity.Y, impulse));
        }
        if (state.Jumps.SlowCurrentJump)
        {
            float speed = Math.Clamp(pawn.MovementServices?.Maxspeed ?? 250, 1, 250);
            var current = new Vector3(pawn.AbsVelocity.X, pawn.AbsVelocity.Y, pawn.AbsVelocity.Z);
            var capped = PublicGameplayLogic.CapHorizontal(current, speed);
            if (current != capped) pawn.Teleport(velocity: capped);
        }
    }

    private void MaintainNameTag(CCSPlayerController player, PlayerState state)
    {
        if (!_players.Values.Any(value => value.HideRange > 0)) return;
        var pawn = player.PlayerPawn.Value!;
        if (pawn.AbsOrigin == null) return;
        if (state.NameTag is not { IsValid: true })
        {
            var label = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
            if (label == null) return;
            label.FontSize = 24; label.WorldUnitsPerPx = 0.15f; label.Fullbright = true; label.Enabled = true;
            label.Color = Color.White; label.FontName = "Arial";
            label.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
            label.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_BOTTOM;
            label.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_AROUND_UP;
            label.MessageText = CleanName(player.PlayerName);
            label.DispatchSpawn(); state.NameTag = label;
        }
        var tag = state.NameTag;
        string name = CleanName(player.PlayerName);
        if (tag.MessageText != name) { tag.MessageText = name; Utilities.SetStateChanged(tag, "CPointWorldText", "m_messageText"); }
        tag.Teleport(new Vector3(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + 85));
    }

    private static string CleanName(string value) => new(value.Where(character => !char.IsControl(character)).Take(48).ToArray());

    private void CheckTransmit(CCheckTransmitInfoList list)
    {
        if (!_active || _disposed) return;
        try
        {
            var players = Utilities.GetPlayers().Where(Player).ToArray();
            foreach (var (information, viewer) in list)
            {
                if (!Player(viewer)) continue;
                var viewerState = State(viewer!);
                var viewerOrigin = Alive(viewer) ? viewer!.PlayerPawn.Value?.AbsOrigin : null;
                foreach (var target in players)
                {
                    var targetState = State(target);
                    var pawn = target.PlayerPawn.Value;
                    var origin = pawn?.AbsOrigin;
                    float distanceSquared = viewerOrigin == null || origin == null ? float.PositiveInfinity :
                        Vector3.DistanceSquared(new(viewerOrigin.X, viewerOrigin.Y, viewerOrigin.Z), new(origin.X, origin.Y, origin.Z));
                    bool hide = !_plugin.IsCommander(target) && PublicGameplayLogic.HideTeammate(viewer!.Slot, target.Slot, viewer.TeamNum, target.TeamNum,
                        Alive(target), viewerState.HideRange, distanceSquared);
                    if (targetState.NameTag is { IsValid: true } tag && !hide) information.TransmitEntities.Remove((int)tag.Index);
                    if (!hide || pawn is not { IsValid: true }) continue;
                    information.TransmitEntities.Remove((int)pawn.Index);
                    if (pawn.WeaponServices is not { } weapons) continue;
                    foreach (var handle in weapons.MyWeapons)
                        if (handle.Value is { IsValid: true } weapon) information.TransmitEntities.Remove((int)weapon.Index);
                }
            }
        }
        catch (Exception error) { Report("transmit", error); }
    }
}
