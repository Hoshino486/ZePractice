using System.Globalization;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using CssTimer = CounterStrikeSharp.API.Modules.Timers.Timer;
namespace ZePractice;

/// <summary>Permission-checked administration and practice tools. ze_ commands never shadow CS2Fixes.</summary>
public sealed partial class AdminPracticeService : IDisposable
{
    private readonly ZePracticePlugin _plugin;
    private readonly Dictionary<string, CommandInfo.CommandCallback> _commands = new();
    private readonly Dictionary<int, PracticeState> _practice = new();
    private readonly Dictionary<int, VoiceFlags> _originalVoiceFlags = new();
    private readonly Dictionary<int, DateTimeOffset> _lastRespawn = new();
    private readonly Dictionary<int, (int? UserId, CssTimer Timer)> _pendingRespawns = new();
    private readonly AdminPracticePunishmentStore _punishments;
    private IReadOnlyList<PracticeMapEntry> _maps = Array.Empty<PracticeMapEntry>();
    private CssTimer? _maintenance;
    private bool _disposed, _initialized, _punishmentsHealthy, _mapActive;
    private string _mapName = "";
    private int _roundGeneration;
    public AdminPracticeService(ZePracticePlugin plugin)
    {
        _plugin = plugin;
        _punishments = new(Path.Combine(plugin.ModuleDirectory, "data", "punishments.json"));
    }
    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        try { _punishments.Load(); _punishmentsHealthy = true; }
        catch (Exception e) { _plugin.Logger.LogError(e, "Punishment file could not be read; refusing to overwrite it."); }
        Register("css_zemenu", "中文 ZE / 跑图主菜单", (p, _) => ShowMain(p));
        Register("css_ze", "中文 ZE / 跑图主菜单", (p, _) => ShowMain(p));
        Register("css_zhelp", "显示 ZE 插件帮助", (p, _) => ShowHelp(p));
        Register("css_ze_help", "显示 ZE 插件帮助", (p, _) => ShowHelp(p));
        Register("css_ze_admin", "中文服务器管理菜单", (p, _) => ShowAdmin(p));
        Register("css_zeadmin", "中文服务器管理菜单", (p, _) => ShowAdmin(p));
        Register("css_ze_status", "当前地图和模式", (p, _) => _plugin.ShowStatus(p));
        Register("css_ze_whoami", "自己的 SteamID64 与权限", (p, _) => ShowIdentity(p));
        Register("css_ze_adminhelp", "管理员配置说明", (p, _) => AdminHelp(p));
        Register("css_ze_practice", "个人跑图辅助菜单", (p, _) => ShowPractice(p));
        Register("css_ze_clearcheckpoint", "练习：只清除个人检查点", (p, _) => ClearCheckpoint(p));
        Register("css_ze_god", "练习：个人无敌", (p, _) => ToggleGod(p));
        Register("css_ze_noclip", "所有人：个人穿墙", (p, _) => ToggleNoclip(p));
        Register("css_ze_save", "练习：保存检查点", (p, _) => SaveCheckpoint(p));
        Register("css_ze_tp", "练习：返回检查点", (p, _) => TeleportCheckpoint(p));
        Register("css_ze_reset", "练习：清空个人辅助", (p, _) => ResetPractice(p));
        Register("css_ze_respawn", "复活自己；管理员可指定 #userid / 唯一名称", RespawnCommand);
        Register("css_ze_respawn_other", "管理：复活指定玩家（独立目标权限）", (p, c) => TargetCommand(p, c, "css_ze_respawn_other", (a, t) => Respawn(a, t, true)));
        Register("css_ze_goto", "练习：移动到玩家", GotoCommand);
        Register("css_ze_bring", "管理：拉玩家到自己身边", BringCommand);
        Register("css_ze_slay", "管理：处死指定玩家", (p, c) => TargetCommand(p, c, "css_ze_slay", Slay));
        Register("css_ze_kick", "管理：踢出指定玩家", (p, c) => TargetCommand(p, c, "css_ze_kick", Kick));
        Register("css_ze_ban", "管理：封禁 <玩家> [分钟；0永久] [原因]", (p, c) => PunishCommand(p, c, "ban"));
        Register("css_ze_mute", "管理：语音禁言 <玩家> [分钟；0永久] [原因]", (p, c) => PunishCommand(p, c, "mute"));
        Register("css_ze_gag", "管理：文字禁言 <玩家> [分钟；0永久] [原因]", (p, c) => PunishCommand(p, c, "gag"));
        Register("css_ze_unban", "管理：解除封禁 <SteamID64>", (p, c) => UnpunishCommand(p, c, "ban"));
        Register("css_ze_unmute", "管理：解除语音禁言 <玩家/SteamID64>", (p, c) => UnpunishCommand(p, c, "mute"));
        Register("css_ze_ungag", "管理：解除文字禁言 <玩家/SteamID64>", (p, c) => UnpunishCommand(p, c, "gag"));
        Register("css_ze_punishments", "管理：有效处罚", (p, _) => ListPunishments(p));
        Register("css_ze_team", "管理：换队 <玩家> <ct|t|spec>", TeamCommand);
        Register("css_ze_restart", "所有人：重开回合", (p, _) => RestartRound(p));
        Register("css_ze_mode", "管理：模式 <practice|ze>", ModeCommand);
        Register("css_ze_maps", "所有人：候选地图投票列表", (p, c) => ShowMaps(p, c.ArgCount > 1 ? c.GetArg(1) : ""));
        Register("css_ze_installed_maps", "服主：查看和立即切换已安装地图", (p, c) => ShowInstalledMaps(p, c.ArgCount > 1 ? c.GetArg(1) : ""));
        Register("css_ze_map", "仅服主：指定地图名.vpk 直接切换，无需投票/RTV", MapCommand);
        Register("css_ze_reload", "管理：重载地图配置", (p, _) => Reload(p));
        Register("css_ze_timescale", "仅服主：加速/恢复正常地图时间（所有人）；可带倍数如 2.5", TimescaleCommand);
        _plugin.AddCommandListener("say", OnSay, HookMode.Pre);
        _plugin.AddCommandListener("say_team", OnSayTeam, HookMode.Pre);
        _plugin.RegisterListener<Listeners.OnClientAuthorized>(OnAuthorized);
        _plugin.RegisterListener<Listeners.OnClientDisconnect>(OnDisconnect);
        _plugin.RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
        _plugin.RegisterEventHandler<EventPlayerDeath>(OnDeath, HookMode.Post);
        _maintenance = _plugin.AddTimer(1f, Maintenance, TimerFlags.REPEAT);
        // Engine globals/entities are not guaranteed during native plugin initialization.
        // OnMapStart owns map discovery; timer enforcement is gated by _mapActive.
    }
    private void Register(string name, string description, CommandInfo.CommandCallback handler)
    {
        CommandInfo.CommandCallback guarded = (p, c) =>
        {
            if (_disposed) return;
            try { handler(p, c); }
            catch (Exception e) { _plugin.Logger.LogError(e, "Command {Command} failed", name); _plugin.Reply(p, "操作失败，详情已写入服务器日志；未报告为成功。"); }
        };
        _commands.Add(name, guarded); _plugin.AddZeCommand(name, description, guarded);
    }
    public void OnMapStart(string mapName)
    {
        ClearPracticeOverrides(); _mapActive = true; _mapName = mapName; _roundGeneration++; _lastRespawn.Clear(); RefreshMaps();
    }
    private void OnMapEnd()
    {
        _mapActive = false; CancelRespawns(); _practice.Clear(); _lastRespawn.Clear();
    }
    public void OnRoundStart()
    {
        _roundGeneration++; CancelRespawns();
        foreach (var state in _practice.Values) state.Checkpoint = null;
        if (!_plugin.IsPractice) ClearPracticeOverrides();
    }
    public void OnPlayerSpawn(CCSPlayerController p)
    {
        if (!ZePracticePlugin.IsRealPlayer(p)) return;
        EnforcePunishments(p);
        if (!_plugin.IsPractice) { RestorePractice(p); return; }
        if (_practice.TryGetValue(p.Slot, out var s))
        {
            if (s.UserId != p.UserId) { _practice.Remove(p.Slot); return; }
            RecheckPlayerPermissions(p, s);
            ApplyPractice(p, s);
        }
    }
    public void ClearPracticeOverrides()
    {
        CancelRespawns();
        if (_mapActive && _practice.Count > 0)
            foreach (var p in Utilities.GetPlayers().Where(ZePracticePlugin.IsRealPlayer)) RestorePractice(p);
        _practice.Clear();
    }
    // A missing operation is only for management-menu navigation, never for an action.
    private bool NeedAdmin(CCSPlayerController? p, string? operation = null)
    {
        if (operation != null) return !_disposed && _plugin.NeedPermission(p, operation);
        if (!_disposed && _plugin.Permissions.HasAdminAccess(p)) return true;
        _plugin.Reply(p, "当前没有管理菜单权限；!ze_whoami 查看自己的权限。"); return false;
    }
    private bool NeedPractice(CCSPlayerController? p, string operation)
    {
        if (p == null || !ZePracticePlugin.IsRealPlayer(p)) { _plugin.Reply(p, "此功能需要进入游戏后使用。"); return false; }
        if (!_plugin.IsPractice) { _plugin.Reply(p, "多人 ZE 模式已关闭个人无敌、检查点与管理员自由复活；全员穿墙使用 !ze_noclip。"); return false; }
        return !_disposed && _plugin.NeedPermission(p, operation);
    }
    private bool CanTarget(CCSPlayerController? p, CCSPlayerController t, bool notify = true)
    {
        if (_disposed || !ZePracticePlugin.IsRealPlayer(t) || (p != null && !ZePracticePlugin.IsRealPlayer(p))) return false;
        if (_plugin.Permissions.IsOwner(p) || (p != null && p.Slot == t.Slot && p.UserId == t.UserId)) return true;
        int actorImmunity = _plugin.Permissions.Immunity(p), targetImmunity = _plugin.Permissions.Immunity(t);
        if (_plugin.Permissions.IsOwner(t) || targetImmunity > actorImmunity ||
            ((targetImmunity > 0 || _plugin.Permissions.HasAdminAccess(t)) && targetImmunity >= actorImmunity))
        {
            if (notify) _plugin.Reply(p, "目标拥有更高或同级受保护的管理权限；请由更高等级管理员或服主执行。");
            return false;
        }
        return true;
    }
    private CCSPlayerController? FindTarget(CCSPlayerController? p, string input)
    {
        var all = Utilities.GetPlayers().Where(ZePracticePlugin.IsRealPlayer).ToArray();
        CCSPlayerController[] matches;
        if (input.Equals("@me", StringComparison.OrdinalIgnoreCase)) matches = p == null ? Array.Empty<CCSPlayerController>() : new[] { p };
        else if (input.StartsWith('#') && int.TryParse(input[1..], out int id)) matches = all.Where(x => x.UserId == id).ToArray();
        else if (AdminPracticePunishmentStore.IsSteamId(input)) matches = all.Where(x => x.SteamID.ToString(CultureInfo.InvariantCulture) == input).ToArray();
        else { matches = all.Where(x => x.PlayerName.Equals(input, StringComparison.OrdinalIgnoreCase)).ToArray(); if (matches.Length == 0) matches = all.Where(x => x.PlayerName.Contains(input, StringComparison.OrdinalIgnoreCase)).ToArray(); }
        if (matches.Length == 1) return matches[0];
        _plugin.Reply(p, matches.Length == 0 ? "未找到在线玩家。使用 #userid / SteamID64 / 唯一名称。" : "名称匹配多人，请使用 #userid 或完整 SteamID64。"); return null;
    }
    private void TargetCommand(CCSPlayerController? p, CommandInfo c, string operation, Action<CCSPlayerController?, CCSPlayerController> action)
    {
        if (!NeedAdmin(p, operation)) return;
        if (c.ArgCount < 2) { _plugin.Reply(p, "缺少目标：<命令> <#userid / SteamID64 / 唯一名称>。"); return; }
        var t = FindTarget(p, c.GetArg(1)); if (t != null && CanTarget(p, t)) action(p, t);
    }
    private void Maintenance()
    {
        if (_disposed || !_mapActive) return;
        RecheckPermissions();
        foreach (var p in Utilities.GetPlayers().Where(ZePracticePlugin.IsRealPlayer))
            try { EnforcePunishments(p); } catch (Exception e) { _plugin.Logger.LogDebug(e, "Player state changed during punishment enforcement"); }
    }
    private void OnDisconnect(int slot)
    {
        _practice.Remove(slot); _lastRespawn.Remove(slot); _originalVoiceFlags.Remove(slot);
        if (_pendingRespawns.Remove(slot, out var pending)) pending.Timer.Kill();
    }
    private static string Clean(string text, int length = 100) => new(text.Where(c => !char.IsControl(c)).Take(length).ToArray());
    private void Audit(CCSPlayerController? p, string action, CCSPlayerController? t = null) =>
        _plugin.Logger.LogInformation("ADMIN {Actor} [{SteamId}] {Action} {Target} [{TargetSteamId}]", p == null ? "CONSOLE" : Clean(p.PlayerName), p?.SteamID, action, t == null ? "" : Clean(t.PlayerName), t?.SteamID);
    public void Dispose()
    {
        if (_disposed) return;
        ClearPracticeOverrides(); _disposed = true; _maintenance?.Kill();
        if (_mapActive && _originalVoiceFlags.Count > 0)
            foreach (var p in Utilities.GetPlayers().Where(ZePracticePlugin.IsRealPlayer))
                if (_originalVoiceFlags.TryGetValue(p.Slot, out var original) && (original & VoiceFlags.Muted) == 0) p.VoiceFlags &= ~VoiceFlags.Muted;
        _originalVoiceFlags.Clear();
        if (!_initialized) return;
        foreach (var c in _commands) _plugin.RemoveZeCommand(c.Key, c.Value);
        _plugin.RemoveCommandListener("say", OnSay, HookMode.Pre); _plugin.RemoveCommandListener("say_team", OnSayTeam, HookMode.Pre);
        _plugin.RemoveListener<Listeners.OnClientAuthorized>(OnAuthorized); _plugin.RemoveListener<Listeners.OnClientDisconnect>(OnDisconnect);
        _plugin.RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
        _plugin.DeregisterEventHandler<EventPlayerDeath>(OnDeath, HookMode.Post);
    }
    private sealed class PracticeState
    {
        public int? UserId;
        public bool God, Noclip, GodApplied, NoclipApplied;
        public uint CapturedEntityHandle;
        public IntPtr CapturedPawn;
        public bool OriginalTakesDamage;
        public MoveType_t OriginalMoveType, OriginalActualMoveType;
        public Checkpoint? Checkpoint;
    }
    private sealed record Checkpoint(string Map, int Round, Vector3 Position, Vector3 Angles);
}


