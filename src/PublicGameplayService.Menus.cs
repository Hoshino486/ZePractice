using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;

namespace ZePractice;

public sealed partial class PublicGameplayService
{
    private readonly PublicMenuInput _menuInput = new();
    private void MenuOption(PersistentChatMenu menu, CCSPlayerController owner, string operation, string label, Action<CCSPlayerController> action, Func<string>? liveLabel = null)
    {
        if (!_plugin.Permissions.Allows(owner, ZeCommandText.Canonical(operation))) return;
        int slot = owner.Slot; int? userId = owner.UserId; int generation = _generation;
        var option = menu.AddMenuOption(label, (who, _) =>
        {
            if (_disposed || !_active || generation != _generation || !ZePracticePlugin.IsRealPlayer(who) || who.Slot != slot || who.UserId != userId) return;
            if (_plugin.NeedPermission(who, operation))
            {
                action(who);
                menu.RefreshChatAfterCommand(who);
            }
        });
        if (liveLabel != null) menu.BindLabel(option, liveLabel);
    }
    private static PersistentChatMenu Menu(string title) => new(title);
    private void ExecuteMenu(CCSPlayerController who, string command) => _plugin.ExecuteOwnCommand(who, command);
    private void ShowMenu(CCSPlayerController? player)
    {
        if (!ZePracticePlugin.IsRealPlayer(player)) { _plugin.Reply(player, "!ze / !ze_public / !ze_commands 打开全部公开快捷操作；菜单显示可直接输入的聊天指令。"); return; }
        var p = player!;
        _menuInput.Remove(p.Slot);
        var menu = Menu("ZE 公开指令 · 选择即用");
        void Run(string command, string text) => MenuOption(menu, p, command.Split(' ')[0], text, who => ExecuteMenu(who, command));
        void Live(string command, Func<string> text) => MenuOption(menu, p, command, text(), who => ExecuteMenu(who, command), text);
        string On(bool enabled) => enabled ? "开" : "关";
        Live("css_ze_noclip", () => $"!ze_noclip 穿墙 [{On(State(p).Noclip)}]");
        Live("css_thirdperson", () => $"!thirdperson 第三人称 [{On(_plugin.ThirdPersonEnabled(p))}]");
        Live("css_leader", () => "!leader 上/下人类指挥 [" + (_plugin.IsCommander(p) ? "已上" : "未上") + "]");
        Live("css_ze_ammo", () => $"!ze_ammo 免换弹 [{On(State(p).InfiniteMagazine)}]");
        Live("css_nospread", () => $"!nospread 人类无扩散 [{On(State(p).NoSpread)}]");
        Live("css_ze_bhop", () => $"!ze_bhop 空格连跳 [{On(State(p).BunnyHop)}]");
        Live("css_ze_speed", () => $"!ze_speed 地速/垂速/跳高 [{On(_plugin.MovementHudEnabled(p))}]");
        Live("css_ze_diag", () => $"!ze_diag 服务器运行诊断 [{On(_plugin.DiagnosticsHudEnabled(p))}]");
        Run("css_ze_script_status", "!ze_script_status 地图脚本活动/异常统计");
        MenuOption(menu, p, "css_ze_bot_add", "!ze_bot_add 选择 Bot 阵营并在脚下添加", BotTeamMenu);
        MenuOption(menu, p, "css_ze_bot_hp", "!ze_bot_hp <数值> 下次放置的 Bot 血量", who => ValueMenu(who, "css_ze_bot_hp"));
        Run("css_ze_bot_move", "!ze_bot_move 选择编号传送自己的 Bot 到脚下");
        Run("css_ze_bot_kill", "!ze_bot_kill [编号] 处死自己的 Bot 并结算");
        Run("css_ze_bot_clear", "!ze_bot_clear 删除自己放置的全部 Bot");
        Run("css_ze_bot_status", "!ze_bot_status 自己的 Bot 编号/统计");

        MenuOption(menu, p, "css_ze_hp", "!ze_hp <数值> 生命/上限", who => ValueMenu(who, "css_ze_hp"));
        MenuOption(menu, p, "css_ze_armor", "!ze_armor <数值> 人类护甲", who => ValueMenu(who, "css_ze_armor"));
        MenuOption(menu, p, "css_ze_music", $"!ze_music <0~1> 音乐 [{State(p).MusicVolume:0.##}]", who => ValueMenu(who, "css_ze_music"), () => $"!ze_music <0~1> 音乐 [{State(p).MusicVolume:0.##}]");
        MenuOption(menu, p, "css_ze_hide", $"!ze_hide <0~999> 隐藏队友 [{State(p).HideRange}]", who => ValueMenu(who, "css_ze_hide"), () => $"!ze_hide <0~999> 隐藏队友 [{State(p).HideRange}]");
        MenuOption(menu, p, "css_ze_join", "!ze_join t/ct/spec 自由换阵营", TeamMenu);
        Run("css_clearpoints", "!clearpoints 指挥清空全部标点");
        Run("css_unleader", "!unleader 下指挥（保留标点）");
        Run("css_ze_maps", "!ze_maps 地图列表 / 选下张图");
        MenuOption(menu, p, "css_ze_workshop", "host_workshop_map <ID> 添加候选", who => ValueMenu(who, "css_ze_workshop"));
        Run("css_rtv", "rtv 赞成换图（严格超过80%）");
        Run("css_unrtv", "unrtv 撤回我的换图票");
        MenuOption(menu, p, "css_ze_roundtime", "!ze_roundtime <分钟> 回合时间", who => ValueMenu(who, "css_ze_roundtime"));
        MenuOption(menu, p, "css_ze_restart", "!ze_restart 重启回合（确认）", who => ConfirmPublic(who, "css_ze_restart", "重启全服当前回合？"));
        MenuOption(menu, p, "css_ze_endround", "!ze_endround 结束回合（确认）", who => ConfirmPublic(who, "css_ze_endround", "结束全服当前回合？"));
        Run("css_ze_save", "!ze_save 保存我的检查点");
        Run("css_ze_tp", "!ze_tp 回到我的检查点");
        Run("css_ze_clearcheckpoint", "!ze_clearcheckpoint 删除我的检查点");
        Run("css_ze_god", "!ze_god 个人无敌（练习）");
        Run("css_ze_respawn", "!ze_respawn 复活自己");
        Run("css_ze_reset", "!ze_reset 清空个人辅助/存点");
        MenuOption(menu, p, "css_ze_goto", "!ze_goto <玩家> 传送到玩家", GotoMenu);
        Run("css_zerefill", "!zerefill 补满生命与出生补给");
        Run("css_zeweapon", "!zeweapon 武器/手雷（别名已合并）");
        Run("css_zeclass", "!zeclass 选择僵尸职业");
        Run("css_zskill", "!zskill 使用职业技能");
        Run("css_items", "!items 神器持有者 / 冷却");
        Run("css_bosshud", "!bosshud 开关 Boss / 翻译 HUD");
        MenuOption(menu, p, "css_ze_public", "查看个人/指挥/投票/模型状态", StatusMenu);
        Run("css_ze_binds", "!ze_binds 快捷键说明");
        Run("css_zhelp", "!zhelp 完整指令说明");
        if (_plugin.Permissions.HasAdminAccess(p)) Run("css_zeadmin", "!zeadmin 管理员功能（仍需权限）");
        _plugin.OpenMenu(p, menu);
    }
    private void BotTeamMenu(CCSPlayerController p)
    {
        var menu = Menu("添加练习 Bot · 选择阵营 · 每次一个");
        foreach (var (team, label) in new[] { ("t", "添加僵尸 T"), ("ct", "添加人类 CT"), ("ally", "添加己方"), ("enemy", "添加敌方（原默认）") })
            MenuOption(menu, p, "css_ze_bot_add", $"!ze_bot_add {team} → {label}", who => ExecuteMenu(who, "css_ze_bot_add " + team));
        Back(menu, p); _plugin.OpenMenu(p, menu);
    }
    private void TeamMenu(CCSPlayerController p)
    {
        var menu = Menu("自己的阵营 · 无限制");
        foreach (var (team, label) in new[] { ("ct", "人类 CT"), ("t", "僵尸 T"), ("spec", "观察者") })
            MenuOption(menu, p, "css_ze_join", $"!ze_join {team} → {label}", who => ExecuteMenu(who, "css_ze_join " + team));
        Back(menu, p); _plugin.OpenMenu(p, menu);
    }
    private void GotoMenu(CCSPlayerController p)
    {
        var menu = Menu("传送到玩家 · 仅移动自己");
        foreach (var target in Utilities.GetPlayers().Where(ZePracticePlugin.IsRealPlayer).Where(t => t.Slot != p.Slot))
        {
            int slot = target.Slot; int? userId = target.UserId;
            MenuOption(menu, p, "css_ze_goto", "!ze_goto #" + userId + " " + target.PlayerName, who =>
            {
                var current = Utilities.GetPlayerFromSlot(slot);
                if (current is { IsValid: true } && current.UserId == userId) ExecuteMenu(who, "css_ze_goto #" + userId);
            });
        }
        Back(menu, p); _plugin.OpenMenu(p, menu);
    }
    private void StatusMenu(CCSPlayerController p)
    {
        var menu = Menu("状态指令 · 只读");
        foreach (var (command, label) in new[] { ("css_ze_public_status", "个人设置"), ("css_ze_commander_status", "指挥与标点"),
            ("css_ze_vote_status", "选图与RTV"), ("css_ze_markers_status", "地图神器与按钮"), ("css_ze_model_status", "人物模型"),
            ("css_ze_movement_status", "个人移动参数"), ("css_ze_status", "地图与插件"), ("css_ze_whoami", "自己的权限") })
            MenuOption(menu, p, command, command.Replace("css_", "!") + " " + label, who => ExecuteMenu(who, command));
        Back(menu, p); _plugin.OpenMenu(p, menu);
    }
    private void ConfirmPublic(CCSPlayerController p, string command, string question)
    {
        var menu = Menu(question);
        MenuOption(menu, p, command, "确认执行 " + command.Replace("css_", "!"), who => ExecuteMenu(who, command));
        Back(menu, p); _plugin.OpenMenu(p, menu);
    }
    private void Back(PersistentChatMenu menu, CCSPlayerController p) => MenuOption(menu, p, "css_ze_public", "返回公开指令", ShowMenu);
    private void ValueMenu(CCSPlayerController p, string command)
    {
        double min = 0, max; bool fraction = false; string title; string[] presets;
        switch (command)
        {
            case "css_ze_bot_hp": min = 1; max = _plugin.Config.PracticeBots.MaximumHealth; title = "下次放置的 Bot 生命/上限"; presets = ["100", "1000", "10000", "35000", max.ToString(CultureInfo.InvariantCulture)]; break;
            case "css_ze_hp": min = 1; max = p.TeamNum == 2 ? Options.ZombieHealthLimit : Options.HumanHealthLimit; title = "生命和个人上限"; presets = ["100", "1000", "10000", "35000", "100000", "1000000", max.ToString(CultureInfo.InvariantCulture)]; break;
            case "css_ze_armor":
                if (p.TeamNum == 2) { _plugin.Reply(p, $"僵尸护甲由服务器管理，上限 {Options.ZombieArmor}。"); return; }
                max = Options.HumanArmorLimit; title = "当前护甲"; presets = ["0", "100", "1000", "2500", "100000", "1000000", max.ToString(CultureInfo.InvariantCulture)]; break;
            case "css_ze_music": max = 1; fraction = true; title = "地图音乐音量（不改语音/枪声）"; presets = ["0", "0.25", "0.5", "0.75", "1"]; break;
            case "css_ze_hide": max = 999; title = "范围内屏蔽队友，0关闭"; presets = ["0", "100", "300", "600", "999"]; break;
            case "css_ze_roundtime": min = 1; max = Options.MaximumRoundMinutes; title = "回合时间（分钟）"; presets = ["10", "30", "60", "120"]; break;
            case "css_ze_workshop": min = 1; max = ulong.MaxValue; title = "提名地图，不立即换图"; presets = []; break;
            default: return;
        }
        var menu = Menu(title);
        MenuOption(menu, p, command, "自定义输入 → 聊天发送数值（60秒）", who =>
        {
            _menuInput.Begin(who.Slot, who.UserId, command, min, max, fraction, Server.CurrentTime);
            _plugin.Reply(who, command == "css_ze_workshop" ? "请在聊天发送创意工坊地图数字ID；只添加候选，不立即换图。60秒内有效，取消 退出。" :
                $"请在聊天发送 {min.ToString(CultureInfo.InvariantCulture)}..{max.ToString(CultureInfo.InvariantCulture)} 内的数值；60秒内有效，取消 退出。也可直接输入 {command.Replace("css_", "!")} 数值。");
        });
        foreach (string value in presets.Distinct().Where(v => double.Parse(v, CultureInfo.InvariantCulture) >= min && double.Parse(v, CultureInfo.InvariantCulture) <= max))
            MenuOption(menu, p, command, command.Replace("css_", "!") + " " + value, who => ExecuteMenu(who, command + " " + value));
        Back(menu, p); _plugin.OpenMenu(p, menu);
    }
    private HookResult MenuInputChat(CCSPlayerController? p, CommandInfo c)
    {
        if (!ZePracticePlugin.IsRealPlayer(p) || c.ArgCount < 2) return HookResult.Continue;
        var result = _menuInput.Consume(p!.Slot, p.UserId, c.GetArg(1), Server.CurrentTime, out var command, out var error);
        switch (result)
        {
            case PublicMenuInput.Result.Accepted: _plugin.ExecuteOwnCommand(p, command); return HookResult.Stop;
            case PublicMenuInput.Result.Rejected: _plugin.Reply(p, error); return HookResult.Stop;
            case PublicMenuInput.Result.Cancelled: _plugin.Reply(p, "已取消数值输入。"); return HookResult.Stop;
            default: return HookResult.Continue;
        }
    }
}
