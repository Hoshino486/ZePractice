using System.Globalization;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using Microsoft.Extensions.Logging;
namespace ZePractice;
public sealed partial class AdminPracticeService
{
    private void Slay(CCSPlayerController? p, CCSPlayerController t)
    {
        if (!NeedAdmin(p, "css_ze_slay") || !CanTarget(p, t)) return;
        if (AlivePawn(t) == null) { _plugin.Reply(p, "目标已经死亡。"); return; }
        t.CommitSuicide(false, true); Audit(p, "slay", t); _plugin.Reply(p, $"已处死 {Clean(t.PlayerName)}。");
    }
    private void Kick(CCSPlayerController? p, CCSPlayerController t)
    {
        if (!NeedAdmin(p, "css_ze_kick") || !CanTarget(p, t)) return;
        string name = Clean(t.PlayerName); Audit(p, "kick", t);
        t.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED); _plugin.Reply(p, $"已踢出 {name}。");
    }
    private void TeamCommand(CCSPlayerController? p, CommandInfo c)
    {
        if (!NeedAdmin(p, "css_ze_team")) return;
        if (c.ArgCount < 3) { _plugin.Reply(p, "用法：!ze_team <玩家> <ct|t|spec>。"); return; }
        var team = c.GetArg(2).ToLowerInvariant() switch { "ct" or "3" => CsTeam.CounterTerrorist, "t" or "2" => CsTeam.Terrorist, "spec" or "1" => CsTeam.Spectator, _ => CsTeam.None };
        if (team == CsTeam.None) { _plugin.Reply(p, "队伍只支持 ct、t、spec。"); return; }
        TargetCommand(p, c, "css_ze_team", (a, t) => SetTeam(a, t, team));
    }
    private void SetTeam(CCSPlayerController? p, CCSPlayerController t, CsTeam team)
    {
        if (!NeedAdmin(p, "css_ze_team") || !CanTarget(p, t)) return;
        t.ChangeTeam(team); Audit(p, "team:" + team, t); _plugin.Reply(p, $"已将 {Clean(t.PlayerName)} 切换到 {team}。");
    }
    private void RestartRound(CCSPlayerController? p)
    {
        _plugin.RestartManualRound(p);
    }
    private void ModeCommand(CCSPlayerController? p, CommandInfo c)
    {
        if (!NeedAdmin(p, "css_ze_mode")) return;
        if (c.ArgCount < 2) { _plugin.Reply(p, "用法：!ze_mode practice 或 !ze_mode ze。模式变化清除练习辅助，不自动重启回合。"); return; }
        var mode = c.GetArg(1).ToLowerInvariant();
        if (mode is not ("practice" or "ze")) { _plugin.Reply(p, "模式只支持 practice 和 ze。"); return; }
        SetMode(p, mode == "practice");
    }
    private void SetMode(CCSPlayerController? p, bool practice)
    {
        if (!NeedAdmin(p, "css_ze_mode")) return;
        ClearPracticeOverrides(); _plugin.SetPracticeMode(practice);
        Audit(p, practice ? "mode:practice" : "mode:ze");
        _plugin.Reply(p, practice ? "已切换练习模式，并请求重启回合。" : "已切换多人 ZE；跑图辅助已撤销，并请求重启回合。");
    }
    private void Reload(CCSPlayerController? p)
    {
        if (!NeedAdmin(p, "css_ze_reload")) return;
        _plugin.ReloadMapConfiguration(); RefreshMaps(); Audit(p, "reload-map-config");
        _plugin.Reply(p, "已重载当前地图配置与本地地图列表；修改主配置 JSON 后需重新加载插件。");
    }
    private void PunishCommand(CCSPlayerController? p, CommandInfo c, string kind)
    {
        if (!NeedAdmin(p, "css_ze_" + kind)) return;
        if (c.ArgCount < 2) { _plugin.Reply(p, $"用法：!ze_{kind} <玩家> [分钟；0永久] [原因]。"); return; }
        int minutes = kind == "ban" ? 60 : 30;
        if (c.ArgCount > 2 && (!int.TryParse(c.GetArg(2), NumberStyles.None, CultureInfo.InvariantCulture, out minutes) || minutes > 525600))
        { _plugin.Reply(p, "分钟必须是 0～525600 的整数；0 表示永久。"); return; }
        string reason = c.ArgCount > 3 ? string.Join(" ", Enumerable.Range(3, c.ArgCount - 3).Select(c.GetArg)) : "管理员操作";
        var t = FindTarget(p, c.GetArg(1)); if (t != null) Punish(p, t, kind, minutes, reason);
    }
    private void Punish(CCSPlayerController? p, CCSPlayerController t, string kind, int minutes, string reason)
    {
        if (!NeedAdmin(p, "css_ze_" + kind) || !CanTarget(p, t)) return;
        if (!_punishmentsHealthy) { _plugin.Reply(p, "处罚存档读取失败；为保护原存档不覆盖，请检查日志。"); return; }
        string id = t.AuthorizedSteamID?.SteamId64.ToString(CultureInfo.InvariantCulture) ?? "";
        if (!AdminPracticePunishmentStore.IsSteamId(id)) { _plugin.Reply(p, "目标 Steam 身份尚未验证；可先踢出，验证完成后再封禁/禁言。"); return; }
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _punishments.Put(new(id, kind, Clean(reason, 180), p?.SteamID.ToString(CultureInfo.InvariantCulture) ?? "CONSOLE", now, minutes == 0 ? null : now.AddMinutes(minutes)));
        Audit(p, kind + ":" + minutes, t);
        _plugin.Reply(p, $"已对 {Clean(t.PlayerName)} 设置 {kind}，期限 {(minutes == 0 ? "永久" : minutes + " 分钟")}。"); EnforcePunishments(t);
    }
    private void UnpunishCommand(CCSPlayerController? p, CommandInfo c, string kind)
    {
        if (!NeedAdmin(p, "css_ze_un" + kind)) return;
        if (c.ArgCount < 2) { _plugin.Reply(p, $"用法：!ze_un{kind} <SteamID64 / 在线玩家>。"); return; }
        if (!_punishmentsHealthy) { _plugin.Reply(p, "处罚存档读取失败，请先检查日志。"); return; }
        string id = c.GetArg(1); CCSPlayerController? t;
        if (!AdminPracticePunishmentStore.IsSteamId(id)) { t = FindTarget(p, id); if (t == null || !CanTarget(p, t)) return; id = t.AuthorizedSteamID?.SteamId64.ToString(CultureInfo.InvariantCulture) ?? ""; if (!AdminPracticePunishmentStore.IsSteamId(id)) { _plugin.Reply(p, "目标 Steam 身份尚未验证。"); return; } }
        else { t = Utilities.GetPlayers().FirstOrDefault(x => ZePracticePlugin.IsRealPlayer(x) && x.AuthorizedSteamID?.SteamId64.ToString(CultureInfo.InvariantCulture) == id); if (t != null && !CanTarget(p, t)) return; }
        bool removed = _punishments.Remove(id, kind); if (t != null) EnforcePunishments(t);
        Audit(p, "un" + kind + ":" + id); _plugin.Reply(p, removed ? "已删除本插件对应记录；其他插件仍有处罚时需分别解除。" : "本插件没有对应处罚记录。");
    }
    private void ListPunishments(CCSPlayerController? p)
    {
        if (!NeedAdmin(p, "css_ze_punishments")) return;
        var records = _punishments.Active(DateTimeOffset.UtcNow);
        _plugin.Reply(p, $"有效处罚：{records.Count} 条；完整记录在插件 data/punishments.json。");
        foreach (var r in records.Take(20)) _plugin.Reply(p, $"{r.SteamId} | {r.Kind} | {(r.ExpiresUtc == null ? "永久" : r.ExpiresUtc.Value.ToString("u"))} | {r.Reason}");
    }
    private void OnAuthorized(int slot, SteamID steamId)
    {
        Server.NextFrame(() => { if (!_disposed) { var p = Utilities.GetPlayerFromSlot(slot); if (p != null && ZePracticePlugin.IsRealPlayer(p) && p.SteamID == steamId.SteamId64) EnforcePunishments(p); } });
    }
    private void EnforcePunishments(CCSPlayerController p)
    {
        if (!_punishmentsHealthy) return;
        string id = p.AuthorizedSteamID?.SteamId64.ToString(CultureInfo.InvariantCulture) ?? "";
        if (!AdminPracticePunishmentStore.IsSteamId(id)) return;
        var now = DateTimeOffset.UtcNow;
        if (_punishments.Find(id, "ban", now) != null) { p.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKBANADDED); return; }
        if (_punishments.Find(id, "mute", now) != null)
        {
            if (!_originalVoiceFlags.ContainsKey(p.Slot)) _originalVoiceFlags[p.Slot] = p.VoiceFlags;
            p.VoiceFlags |= VoiceFlags.Muted;
        }
        else if (_originalVoiceFlags.Remove(p.Slot, out var original) && (original & VoiceFlags.Muted) == 0) p.VoiceFlags &= ~VoiceFlags.Muted;
    }
    private HookResult OnSay(CCSPlayerController? p, CommandInfo c) => GagFilter(p, c);
    private HookResult OnSayTeam(CCSPlayerController? p, CommandInfo c) => GagFilter(p, c);
    private HookResult GagFilter(CCSPlayerController? p, CommandInfo c)
    {
        if (p == null || !ZePracticePlugin.IsRealPlayer(p) || !_punishmentsHealthy) return HookResult.Continue;
        var id = p.AuthorizedSteamID?.SteamId64.ToString(CultureInfo.InvariantCulture) ?? "";
        if (_punishments.Find(id, "gag", DateTimeOffset.UtcNow) == null) return HookResult.Continue;
        if (c.ArgCount > 1 && WorkshopVoteLedger.IsPublicChat(c.GetArg(1))) return HookResult.Continue;
        // The central full-width bridge is registered first and handles its own permission checks.
        // Ordinary service commands/menu keys remain usable while normal chat stays gagged.
        if (c.ArgCount > 1 && ZeCommandText.TryParseChat(c.GetArg(1), out var name, out _, out int key) &&
            (key != 0 || _plugin.Permissions.Allows(p, ZeCommandText.Canonical(name)))) return HookResult.Continue;
        _plugin.Reply(p, "当前处于文字禁言状态；仍可使用有权限的服务器命令和菜单。"); return HookResult.Handled;
    }
    private void RefreshMaps()
    {
        try
        {
            var root = Server.GameDirectory;
            if (Directory.Exists(Path.Combine(root, "csgo"))) root = Path.Combine(root, "csgo");
            // Restrict addon map listings to the addons actually mounted by this server process.
            var active = new List<string>();
            var match = Regex.Match(Environment.CommandLine, "(?:^|\\s)-addon\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success) active.AddRange((match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            string path = Path.Combine(_plugin.FysDirectory, "maps.json");
            if (!File.Exists(path)) path = Path.Combine(_plugin.FysDirectory, "sharp", "configs", "maps.json");
            if (!File.Exists(path)) path = Path.Combine(_plugin.FysDirectory, _plugin.Config.ServerNumber, "sharp", "configs", "maps.json");
            _maps = AdminPracticeMapCatalog.Load(root, path, active, Path.Combine(root, "addons", "cs2fixes", "configs", "maplist.jsonc"));
        }
        catch (Exception e) { _plugin.Logger.LogWarning(e, "Local map catalog refresh failed"); }
    }
    private void MapCommand(CCSPlayerController? p, CommandInfo c)
    {
        if (!NeedAdmin(p, "css_ze_map")) return;
        if (c.ArgCount != 2) { _plugin.Reply(p, "仅服主：!ze_map <地图名.vpk> 直接切换（无需投票/RTV，可省略.vpk）；!ze_installed_maps 查看已下载地图。"); return; }
        string name = c.GetArg(1);
        if (name.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        RefreshMaps(); var map = _maps.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (map == null) { _plugin.Reply(p, "地图未在服务器已安装且可见的地图中找到。FYS 配置不含地图本体，先安装地图。"); return; }
        ChangeMap(p, map);
    }
    private void ChangeMap(CCSPlayerController? p, PracticeMapEntry map)
    {
        if (!NeedAdmin(p, "css_ze_map")) return;
        RefreshMaps(); var current = _maps.FirstOrDefault(x => x.Name.Equals(map.Name, StringComparison.OrdinalIgnoreCase) && x.LocalFile == map.LocalFile);
        if (current == null || !File.Exists(current.LocalFile) || !AdminPracticeMapCatalog.IsValidMapName(current.Name)) { _plugin.Reply(p, "地图文件已变化，请刷新菜单。"); return; }
        string command = AdminPracticeMapCatalog.LoadCommand(current);
        if (_plugin.ForceMapChange(p, command)) Audit(p, "map:" + current.Name);
    }
}




