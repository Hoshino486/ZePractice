using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
namespace ZePractice;
public sealed partial class AdminPracticeService
{
    private void Option(ChatMenu menu, CCSPlayerController owner, string? operation, string text, Action<CCSPlayerController> action, bool disabled = false)
    {
        bool allowed = operation == null ? _plugin.Permissions.HasAdminAccess(owner) : _plugin.Permissions.Allows(owner, ZeCommandText.Canonical(operation));
        int ownerSlot = owner.Slot; int? ownerUserId = owner.UserId;
        menu.AddMenuOption(text, (who, _) =>
        {
            if (_disposed || !ZePracticePlugin.IsRealPlayer(who) || who.Slot != ownerSlot || who.UserId != ownerUserId) return;
            if (operation == null ? !NeedAdmin(who) : !_plugin.NeedPermission(who, operation)) return;
            try { action(who); }
            catch (Exception e) { _plugin.Logger.LogError(e, "Menu action failed: {MenuAction}", text); _plugin.Reply(who, "操作失败，详情已写入服务器日志。"); }
        }, disabled || !allowed);
    }
    private void RunOwnCommand(CCSPlayerController player, string command)
    {
        if (command is not ("css_zeweapon" or "css_zeclass" or "css_zskill" or "css_items" or "css_itemwatch" or "css_ze_commands" or "css_ze_binds" or "css_ze_public"))
            throw new ArgumentOutOfRangeException(nameof(command));
        if (_plugin.NeedPermission(player, command)) _plugin.ExecuteOwnCommand(player, command);
    }
    private void ShowHelp(CCSPlayerController? p)
    {
        if (!_plugin.NeedPermission(p, "css_zhelp")) return;
        _plugin.Reply(p, "!zemenu 主菜单 | !ze_status 地图状态 | !ze_maps 换图投票 | !ze_whoami 身份。");
        _plugin.Reply(p, "补给 !zeweapon / !zebuy；职业 !zeclass；职业技能 !zskill；神器持有与冷却 !items（需本地图实际规则）。");
        _plugin.Reply(p, "全员：!ze_public 菜单；!ze_hp / !ze_armor；!ze_noclip 穿墙；!ze_ammo / !ze_bhop；!ze_music / !ze_hide；!ze_join t/ct/spec；!ze_roundtime；!ze_restart / !ze_endround；host_workshop_map 地图ID。");
        _plugin.Reply(p, "人类指挥：!leader 上/下指挥，中键标点10秒，对准旧点中键+10秒；最多5色，超限替换最早点。!unleader 仅取消自身发光，旧点保留计时；!clearpoints 清点。");
        _plugin.Reply(p, "指挥画线：按住R沿准星画青蓝线，每段保留10秒，由旧到新消退；持续画线时旧线也会到期，不改原换弹绑定。");
        _plugin.Reply(p, "练习：!ze_practice 菜单、!ze_god 无敌、!ze_save 存点、!ze_tp 回点、!ze_clearcheckpoint 删点、!ze_reset 清除全部、!ze_respawn 复活、!ze_goto 玩家。");
        if (_plugin.Permissions.HasAdminAccess(p)) _plugin.Reply(p, "管理：!ze_admin | !ze_mode practice/ze | !ze_reload | !ze_map 名称 | !ze_team 玩家 ct/t/spec | !ze_bring / !ze_slay / !ze_kick 玩家。管理他人仍需权限。");
        _plugin.Reply(p, "host_workshop_map +ID 只添加候选；!ze_maps 选图；rtv 严格超过80%才换图。仅服主可 !ze_workshop_now ID 或 !ze_map 名称。原生投票已关闭。");
    }
    private void ShowIdentity(CCSPlayerController? p)
    {
        if (!_plugin.NeedPermission(p, "css_ze_whoami")) return;
        var id = p?.AuthorizedSteamID?.SteamId64.ToString(CultureInfo.InvariantCulture);
        _plugin.Reply(p, p == null ? "服务器控制台：最高管理权限。" : $"SteamID64: {id ?? "尚未认证"}；{_plugin.Permissions.Describe(p)}。");
    }
    private void AdminHelp(CCSPlayerController? p)
    {
        if (!NeedAdmin(p, "css_ze_adminhelp")) return;
        _plugin.Reply(p, "自定义角色与逐指令权限：插件 data/permissions.json；Users 使用认证后的 SteamID64，Roles 允许自定义名称/继承/Allow/Deny/Immunity。");
        _plugin.Reply(p, "修改后由有 css_ze_permissions_reload 权限的人执行 !ze_permissions_reload；失败保留上一份有效配置。菜单、快捷键与命令同权，Deny 优先。");
        _plugin.Reply(p, "OwnerSteamIds、配置允许的 CSS @css/root 与纯 loopback 保留最高权限；等级数字只用于目标豁免，不会自动开全部指令。CS2Fixes 管理员另行配置。");
    }
    private void ShowMain(CCSPlayerController? p)
    {
        if (!_plugin.NeedPermission(p, "css_ze")) return;
        if (p == null) { ShowHelp(null); return; }
        _plugin.ExecuteOwnCommand(p, "css_ze_public");
    }
    private void ShowPractice(CCSPlayerController? p)
    {
        if (!NeedPractice(p, "css_ze_practice")) return;
        var state = State(p!); RecheckPlayerPermissions(p!, state);
        var menu = new ChatMenu("个人跑图辅助（仅练习模式）") { ExitButton = true };
        Option(menu, p!, "css_ze_god", $"无敌：{(state.God ? "开" : "关")}", ToggleGod);
        Option(menu, p!, "css_ze_noclip", $"穿墙：{(_plugin.PublicNoclipEnabled(p!) ? "开" : "关")}", ToggleNoclip);
        Option(menu, p!, "css_ze_save", "保存检查点", SaveCheckpoint);
        Option(menu, p!, "css_ze_tp", "返回检查点", TeleportCheckpoint);
        Option(menu, p!, "css_ze_clearcheckpoint", "只删除检查点", ClearCheckpoint);
        Option(menu, p!, "css_ze_respawn", "复活自己", who => Respawn(who, who, false));
        Option(menu, p!, "css_ze_reset", "清空自己的辅助与存点", ResetPractice);
        Option(menu, p!, "css_zerefill", "补满生命与出生补给", who => _plugin.ExecuteOwnCommand(who, "css_zerefill"));
        Option(menu, p!, "css_ze", "回到主菜单", ShowMain);
        _plugin.OpenMenu(p!, menu);
    }
    private void ShowAdmin(CCSPlayerController? p)
    {
        if (!NeedAdmin(p, "css_zeadmin")) return;
        if (p == null) { ShowHelp(null); return; }
        var menu = new ChatMenu("ZE 服务器管理") { ExitButton = true };
        Option(menu, p, "css_ze_public", "全部公开指令 / 直接操作", who => RunOwnCommand(who, "css_ze_public"));
        Option(menu, p, null, "玩家管理", ShowPlayers);
        Option(menu, p, "css_ze_maps", "换图候选 / 投票", who => ShowMaps(who, ""));
        Option(menu, p, "css_ze_installed_maps", "服主：直接切换已下载地图", who => ShowInstalledMaps(who, ""));
        Option(menu, p, "css_ze_mode", "切换为练习模式", who => Confirm(who, "css_ze_mode", "切换练习模式（不重启回合）？", a => SetMode(a, true)));
        Option(menu, p, "css_ze_mode", "切换为多人 ZE", who => Confirm(who, "css_ze_mode", "切换多人 ZE（不重启回合）？", a => SetMode(a, false)));
        Option(menu, p, "css_ze_restart", "重新开始回合", who => Confirm(who, "css_ze_restart", "重启当前回合？", RestartRound));
        Option(menu, p, "css_ze_reload", "重载地图配置", Reload);
        Option(menu, p, "css_itemwatch", "神器绑定诊断", who => RunOwnCommand(who, "css_itemwatch"));
        Option(menu, p, "css_ze_punishments", "查看有效封禁 / 禁言", ListPunishments);
        Option(menu, p, "css_ze_adminhelp", "管理员设置帮助", AdminHelp);
        _plugin.OpenMenu(p, menu);
    }
    private void ShowPlayers(CCSPlayerController p)
    {
        if (!NeedAdmin(p)) return;
        var menu = new ChatMenu("选择玩家（含管理员豁免检查）") { ExitButton = true };
        foreach (var t in Utilities.GetPlayers().Where(ZePracticePlugin.IsRealPlayer))
        {
            int slot = t.Slot; int? userId = t.UserId;
            Option(menu, p, null, $"#{userId} {Clean(t.PlayerName, 40)}", who => { var target = ResolveMenuTarget(who, slot, userId); if (target != null) ShowPlayer(who, target); }, !CanTarget(p, t, false));
        }
        if (menu.MenuOptions.Count == 0) { _plugin.Reply(p, "当前没有可管理的真人玩家。"); return; }
        _plugin.OpenMenu(p, menu);
    }
    private CCSPlayerController? ResolveMenuTarget(CCSPlayerController p, int slot, int? userId)
    {
        if (!NeedAdmin(p)) return null;
        var t = Utilities.GetPlayerFromSlot(slot);
        if (t == null || !ZePracticePlugin.IsRealPlayer(t) || t.UserId != userId) { _plugin.Reply(p, "目标已离线或重连，请刷新菜单。"); return null; }
        return CanTarget(p, t) ? t : null;
    }
    private void ShowPlayer(CCSPlayerController p, CCSPlayerController t)
    {
        if (!NeedAdmin(p) || !CanTarget(p, t)) return;
        int slot = t.Slot; int? userId = t.UserId;
        var menu = new ChatMenu($"管理 {Clean(t.PlayerName, 35)}") { ExitButton = true };
        void Add(string operation, string label, Action<CCSPlayerController, CCSPlayerController> action, bool confirm = false)
        {
            Option(menu, p, operation, label, who =>
            {
                void Run(CCSPlayerController? admin)
                {
                    if (admin == null || !NeedAdmin(admin, operation)) return;
                    var target = ResolveMenuTarget(admin, slot, userId);
                    if (target != null) action(admin, target);
                }
                if (confirm) Confirm(who, operation, label + "？", Run); else Run(who);
            });
        }
        Add("css_ze_respawn_other", "复活", (a, b) => Respawn(a, b, true));
        Add("css_ze_bring", "拉到我身边", Bring);
        Add("css_ze_team", "切到 CT", (a, b) => SetTeam(a, b, CsTeam.CounterTerrorist));
        Add("css_ze_team", "切到 T", (a, b) => SetTeam(a, b, CsTeam.Terrorist));
        Add("css_ze_team", "切到观察者", (a, b) => SetTeam(a, b, CsTeam.Spectator));
        Add("css_ze_slay", "处死", Slay, true); Add("css_ze_kick", "踢出", Kick, true);
        Add("css_ze_mute", "语音禁言 30 分钟", (a, b) => Punish(a, b, "mute", 30, "管理员菜单"));
        Add("css_ze_gag", "文字禁言 30 分钟", (a, b) => Punish(a, b, "gag", 30, "管理员菜单"));
        Add("css_ze_unmute", "解除本插件语音禁言", (a, b) => UnpunishFromMenu(a, b, "mute"));
        Add("css_ze_ungag", "解除本插件文字禁言", (a, b) => UnpunishFromMenu(a, b, "gag"));
        Add("css_ze_ban", "封禁 60 分钟", (a, b) => Punish(a, b, "ban", 60, "管理员菜单"), true);
        Add("css_ze_ban", "封禁 24 小时", (a, b) => Punish(a, b, "ban", 1440, "管理员菜单"), true);
        Add("css_ze_ban", "永久封禁", (a, b) => Punish(a, b, "ban", 0, "管理员菜单"), true);
        _plugin.OpenMenu(p, menu);
    }
    private void UnpunishFromMenu(CCSPlayerController p, CCSPlayerController t, string kind)
    {
        if (!NeedAdmin(p, "css_ze_un" + kind) || !CanTarget(p, t) || !_punishmentsHealthy) return;
        var id = t.AuthorizedSteamID?.SteamId64.ToString(CultureInfo.InvariantCulture) ?? "";
        if (!AdminPracticePunishmentStore.IsSteamId(id)) { _plugin.Reply(p, "目标 Steam 身份尚未验证。"); return; }
        _punishments.Remove(id, kind); EnforcePunishments(t);
        Audit(p, "un" + kind, t); _plugin.Reply(p, "已解除本插件对应禁言。");
    }
    private void Confirm(CCSPlayerController p, string operation, string title, Action<CCSPlayerController?> action)
    {
        if (!NeedAdmin(p, operation)) return;
        var menu = new ChatMenu(title) { ExitButton = true };
        Option(menu, p, "css_zeadmin", "取消", ShowAdmin);
        Option(menu, p, operation, "确认执行", who => { if (NeedAdmin(who, operation)) action(who); });
        _plugin.OpenMenu(p, menu);
    }
    private void ShowMaps(CCSPlayerController? p, string search)
    {
        if (!_plugin.NeedPermission(p, "css_ze_maps")) return;
        _plugin.OpenMapVoteMenu(p, search);
    }
    private void ShowInstalledMaps(CCSPlayerController? p, string search)
    {
        if (!_plugin.NeedPermission(p, "css_ze_installed_maps")) return;
        RefreshMaps();
        var matches = _maps.Where(x => string.IsNullOrWhiteSpace(search) || x.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || x.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        _plugin.Reply(p, $"已安装地图 {matches.Length} 张，按地图VPK文件名显示；服主可直接切换，无需投票/RTV。普通玩家使用 !ze_maps 预定投票。");
        if (p == null) { foreach (var map in matches) _plugin.Reply(null, map.FileLabel); return; }
        if (matches.Length == 0) return;
        var menu = new ChatMenu("服主已下载地图（直接切换）") { ExitButton = true };
        foreach (var map in matches)
        {
            var captured = map;
            Option(menu, p, "css_ze_map", Clean(map.FileLabel, 80), who => Confirm(who, "css_ze_map", "直接切换 " + Clean(captured.FileLabel, 65) + "？", a => ChangeMap(a, captured)));
        }
        _plugin.OpenMenu(p, menu);
    }
}
