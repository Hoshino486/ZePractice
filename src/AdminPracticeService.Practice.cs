using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
namespace ZePractice;
public sealed partial class AdminPracticeService
{
    private static CCSPlayerPawn? AlivePawn(CCSPlayerController p) => p.PlayerPawn.Value is { IsValid: true, Health: > 0 } pawn && p.PawnIsAlive ? pawn : null;
    private PracticeState State(CCSPlayerController p)
    {
        if (!_practice.TryGetValue(p.Slot, out var s) || s.UserId != p.UserId)
            _practice[p.Slot] = s = new() { UserId = p.UserId };
        return s;
    }
    public IReadOnlyDictionary<string, bool> GetPracticeToggles(CCSPlayerController player)
    {
        PracticeState? state = null;
        if (!_disposed && ZePracticePlugin.IsRealPlayer(player) && _practice.TryGetValue(player.Slot, out var found) && found.UserId == player.UserId)
        { RecheckPlayerPermissions(player, found); state = found; }
        return new Dictionary<string, bool>
        {
            ["god"] = state?.God == true,
            ["noclip"] = _plugin.PublicNoclipEnabled(player),
            ["checkpoint"] = state?.Checkpoint is { } cp && cp.Map == _mapName && cp.Round == _roundGeneration
        };
    }
    public void RecheckPermissions()
    {
        if (_disposed) return;
        foreach (var entry in _practice.ToArray())
        {
            var player = Utilities.GetPlayerFromSlot(entry.Key);
            if (!ZePracticePlugin.IsRealPlayer(player) || player!.UserId != entry.Value.UserId) { _practice.Remove(entry.Key); continue; }
            RecheckPlayerPermissions(player, entry.Value);
            var s = entry.Value;
            if (!s.God && !s.Noclip && !s.GodApplied && !s.NoclipApplied && s.Checkpoint == null) _practice.Remove(entry.Key);
        }
        foreach (var entry in _pendingRespawns.ToArray())
        {
            var player = Utilities.GetPlayerFromSlot(entry.Key);
            if (!_plugin.IsPractice || !_plugin.Config.AutoRespawn || !ZePracticePlugin.IsRealPlayer(player) || player!.UserId != entry.Value.UserId || !_plugin.Permissions.Allows(player, "css_ze_respawn"))
            { entry.Value.Timer.Kill(); _pendingRespawns.Remove(entry.Key); }
        }
    }
    private void RecheckPlayerPermissions(CCSPlayerController p, PracticeState s)
    {
        var pawn = p.PlayerPawn.Value;
        bool captured = pawn is { IsValid: true } && pawn.Handle == s.CapturedPawn && p.PlayerPawn.Raw == s.CapturedEntityHandle;
        if (!_plugin.IsPractice || !_plugin.Permissions.Allows(p, "css_ze_god"))
        {
            s.God = false;
            if (s.GodApplied && captured) pawn!.TakesDamage = s.OriginalTakesDamage;
            s.GodApplied = false;
        }
        if (!_plugin.IsPractice || !_plugin.Permissions.Allows(p, "css_ze_noclip"))
        {
            s.Noclip = false;
            if (s.NoclipApplied && captured)
            {
                pawn!.MoveType = s.OriginalMoveType; pawn.ActualMoveType = s.OriginalActualMoveType;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
            }
            s.NoclipApplied = false;
        }
        if (!_plugin.IsPractice || !_plugin.Permissions.Allows(p, "css_ze_save") || !_plugin.Permissions.Allows(p, "css_ze_tp")) s.Checkpoint = null;
    }
    private void ApplyPractice(CCSPlayerController p, PracticeState s)
    {
        RecheckPlayerPermissions(p, s);
        var pawn = AlivePawn(p); if (pawn == null) return;
        if (s.CapturedPawn != pawn.Handle || s.CapturedEntityHandle != p.PlayerPawn.Raw)
        {
            s.CapturedPawn = pawn.Handle; s.CapturedEntityHandle = p.PlayerPawn.Raw;
            s.GodApplied = s.NoclipApplied = false;
        }
        if (s.God)
        {
            if (!s.GodApplied) s.OriginalTakesDamage = pawn.TakesDamage;
            pawn.TakesDamage = false; s.GodApplied = true;
        }
        else if (s.GodApplied) { pawn.TakesDamage = s.OriginalTakesDamage; s.GodApplied = false; }
        if (s.Noclip)
        {
            if (!s.NoclipApplied) { s.OriginalMoveType = pawn.MoveType; s.OriginalActualMoveType = pawn.ActualMoveType; }
            pawn.MoveType = pawn.ActualMoveType = MoveType_t.MOVETYPE_NOCLIP;
            s.NoclipApplied = true; Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
        }
        else if (s.NoclipApplied)
        {
            pawn.MoveType = s.OriginalMoveType; pawn.ActualMoveType = s.OriginalActualMoveType;
            s.NoclipApplied = false; Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
        }
    }
    private void RestorePractice(CCSPlayerController p)
    {
        if (!_practice.Remove(p.Slot, out var s) || s.UserId != p.UserId) return;
        var pawn = p.PlayerPawn.Value;
        if (pawn is not { IsValid: true } || pawn.Handle != s.CapturedPawn || p.PlayerPawn.Raw != s.CapturedEntityHandle) return;
        if (s.GodApplied) pawn.TakesDamage = s.OriginalTakesDamage;
        if (s.NoclipApplied) { pawn.MoveType = s.OriginalMoveType; pawn.ActualMoveType = s.OriginalActualMoveType; Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType"); }
    }
    private void ToggleGod(CCSPlayerController? p)
    {
        if (!NeedPractice(p, "css_ze_god")) return;
        if (AlivePawn(p!) == null) { _plugin.Reply(p, "请先复活再切换无敌。"); return; }
        var s = State(p!); s.God = !s.God; ApplyPractice(p!, s);
        _plugin.Reply(p, $"个人无敌：{(s.God ? "开启" : "关闭")}。仅阻止伤害，不阻止地图直接删除/传送玩家。");
    }
    private void ToggleNoclip(CCSPlayerController? p)
    {
        _plugin.TogglePublicNoclip(p);
    }
    private void SaveCheckpoint(CCSPlayerController? p)
    {
        if (!NeedPractice(p, "css_ze_save")) return;
        var pawn = AlivePawn(p!); var origin = pawn?.AbsOrigin;
        if (pawn == null || origin == null) { _plugin.Reply(p, "存点需要存活角色。"); return; }
        State(p!).Checkpoint = new(_mapName, _roundGeneration, new(origin.X, origin.Y, origin.Z), new(pawn.EyeAngles.X, pawn.EyeAngles.Y, pawn.EyeAngles.Z));
        _plugin.Reply(p, "检查点已保存；!ze_tp 返回。切图或新回合会清空，避免回到失效关卡。");
    }
    private void TeleportCheckpoint(CCSPlayerController? p)
    {
        if (!NeedPractice(p, "css_ze_tp")) return;
        var pawn = AlivePawn(p!);
        if (pawn == null) { _plugin.Reply(p, "请先 !ze_respawn 复活。"); return; }
        var cp = State(p!).Checkpoint;
        if (cp == null || cp.Map != _mapName || cp.Round != _roundGeneration) { _plugin.Reply(p, "没有当前回合检查点，先 !ze_save。"); return; }
        pawn.Teleport(cp.Position, cp.Angles, Vector3.Zero); _plugin.Reply(p, "已返回检查点。");
    }
    private void ClearCheckpoint(CCSPlayerController? p)
    {
        if (!NeedPractice(p, "css_ze_clearcheckpoint")) return;
        if (_practice.TryGetValue(p!.Slot, out var state) && state.UserId == p.UserId) state.Checkpoint = null;
        _plugin.Reply(p, "检查点已清除；无敌和穿墙状态未改变。");
    }
    private void ResetPractice(CCSPlayerController? p)
    {
        if (!NeedPractice(p, "css_ze_reset")) return;
        RestorePractice(p!);
        if (_plugin.PublicNoclipEnabled(p!)) _plugin.TogglePublicNoclip(p);
        _plugin.Reply(p, "个人无敌、穿墙与检查点已清空。");
    }
    private void RespawnCommand(CCSPlayerController? p, CommandInfo c)
    {
        if (c.ArgCount > 1)
        {
            var t = FindTarget(p, c.GetArg(1));
            if (t != null) Respawn(p, t, p == null || p.Slot != t.Slot || p.UserId != t.UserId);
            return;
        }
        if (NeedPractice(p, "css_ze_respawn")) Respawn(p, p!, false);
    }
    private void Respawn(CCSPlayerController? p, CCSPlayerController t, bool admin)
    {
        if (admin && (!NeedAdmin(p, "css_ze_respawn_other") || !CanTarget(p, t))) return;
        if (!admin && !NeedPractice(t, "css_ze_respawn")) return;
        if (AlivePawn(t) != null) { _plugin.Reply(p, "目标已经存活。"); return; }
        if (_lastRespawn.TryGetValue(t.Slot, out var when) && DateTimeOffset.UtcNow - when < TimeSpan.FromSeconds(1)) return;
        _lastRespawn[t.Slot] = DateTimeOffset.UtcNow;
        if (t.TeamNum <= (byte)CsTeam.Spectator) t.ChangeTeam(CsTeam.CounterTerrorist);
        int slot = t.Slot, generation = _roundGeneration; int? userId = t.UserId;
        int? actorSlot = p?.Slot, actorUserId = p?.UserId;
        Server.NextFrame(() =>
        {
            if (_disposed || generation != _roundGeneration || (!admin && !_plugin.IsPractice)) return;
            var current = Utilities.GetPlayerFromSlot(slot);
            if (current == null || !ZePracticePlugin.IsRealPlayer(current) || current.UserId != userId || AlivePawn(current) != null || _plugin.TeamResetPending(current)) return;
            if (admin)
            {
                var actor = actorSlot == null ? null : Utilities.GetPlayerFromSlot(actorSlot.Value);
                if (actorSlot != null && (!ZePracticePlugin.IsRealPlayer(actor) || actor!.UserId != actorUserId)) return;
                if (!_plugin.Permissions.Allows(actor, "css_ze_respawn_other") || !CanTarget(actor, current)) return;
            }
            else if (!_plugin.Permissions.Allows(current, "css_ze_respawn")) return;
            current.Respawn();
        });
        _plugin.Reply(p, $"已请求复活 {Clean(t.PlayerName)}。"); if (admin) Audit(p, "respawn", t);
    }
    private HookResult OnDeath(EventPlayerDeath ev, GameEventInfo info)
    {
        var p = ev.Userid;
        if (_disposed || !_plugin.IsPractice || !_plugin.Config.AutoRespawn || p == null || !ZePracticePlugin.IsRealPlayer(p)) return HookResult.Continue;
        if (!_plugin.Permissions.Allows(p, "css_ze_respawn") || p.TeamNum <= (byte)CsTeam.Spectator) return HookResult.Continue;
        if (_pendingRespawns.Remove(p.Slot, out var previous)) previous.Timer.Kill();
        int slot = p.Slot; int? userId = p.UserId; int generation = _roundGeneration;
        var timer = _plugin.AddTimer(Math.Clamp(_plugin.Config.RespawnDelaySeconds, 0.2f, 60f), () =>
        {
            _pendingRespawns.Remove(slot);
            if (_disposed || !_plugin.IsPractice || !_plugin.Config.AutoRespawn || generation != _roundGeneration) return;
            var current = Utilities.GetPlayerFromSlot(slot);
            if (current == null || !ZePracticePlugin.IsRealPlayer(current) || current.UserId != userId || current.TeamNum <= (byte)CsTeam.Spectator || AlivePawn(current) != null || _plugin.TeamResetPending(current)) return;
            if (!_plugin.Permissions.Allows(current, "css_ze_respawn")) return;
            current.Respawn();
        }, TimerFlags.STOP_ON_MAPCHANGE);
        _pendingRespawns[slot] = (userId, timer); return HookResult.Continue;
    }
    private void CancelRespawns()
    {
        foreach (var p in _pendingRespawns.Values) p.Timer.Kill();
        _pendingRespawns.Clear();
    }
    private void GotoCommand(CCSPlayerController? p, CommandInfo c)
    {
        if (!NeedPractice(p, "css_ze_goto")) return;
        if (c.ArgCount < 2) { _plugin.Reply(p, "用法：!ze_goto <#userid / 唯一名称>。"); return; }
        var t = FindTarget(p, c.GetArg(1)); if (t != null) TeleportToPlayer(p!, t);
    }
    private void BringCommand(CCSPlayerController? p, CommandInfo c) => TargetCommand(p, c, "css_ze_bring", Bring);
    private void Bring(CCSPlayerController? p, CCSPlayerController t)
    {
        if (!NeedAdmin(p, "css_ze_bring") || !CanTarget(p, t)) return;
        if (p == null) { _plugin.Reply(p, "拉人需要管理员本人在游戏中。"); return; }
        if (TeleportToPlayer(t, p)) Audit(p, "bring", t);
    }
    private void GotoMenuTarget(CCSPlayerController? p, CCSPlayerController t)
    {
        if (!NeedAdmin(p, "css_ze_goto") || !CanTarget(p, t)) return;
        if (p == null) { _plugin.Reply(p, "传送需要管理员本人在游戏中。"); return; }
        if (TeleportToPlayer(p, t)) Audit(p, "goto-menu", t);
    }
    private bool TeleportToPlayer(CCSPlayerController moved, CCSPlayerController destination)
    {
        var pawn = AlivePawn(moved); var other = AlivePawn(destination); var origin = other?.AbsOrigin;
        if (pawn == null || other == null || origin == null) { _plugin.Reply(moved, "传送双方都需要存活。"); return false; }
        pawn.Teleport(new Vector3(origin.X, origin.Y, origin.Z + 12f), new Vector3(other.EyeAngles.X, other.EyeAngles.Y, other.EyeAngles.Z), Vector3.Zero);
        _plugin.Reply(moved, $"已传送至 {Clean(destination.PlayerName)} 身边。"); return true;
    }
}
