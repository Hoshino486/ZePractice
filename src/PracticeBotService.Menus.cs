using System.Globalization;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace ZePractice;

public sealed partial class PracticeBotService
{
    private void MoveCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount is < 1 or > 2)
        {
            plugin.Reply(player, "用法：!ze_bot_move 打开编号列表；!ze_bot_move <编号> 传送单只；!ze_bot_move all 明确传送全部。");
            return;
        }
        if (!InGame(player, true)) return;
        string target = command.ArgCount == 2 ? command.GetArg(1) : "";
        if (string.IsNullOrWhiteSpace(target)) ShowMoveMenu(player!);
        else Move(player!, target);
    }

    private void ShowMoveMenu(CCSPlayerController player)
    {
        var owner = Identity(player);
        int generation = _menuGeneration;
        var menu = new PersistentChatMenu("传送自己的 Bot 到脚下 · 选择编号");
        var mine = _roster.Owned(owner).OrderBy(e => e.Id).ToArray();
        bool Valid(CCSPlayerController who) => !_disposed && _active && generation == _menuGeneration &&
            Matches(who, owner) && InGame(who, true) && plugin.NeedPermission(who, "css_ze_bot_move");
        void Action(string text, Action<CCSPlayerController> execute)
        {
            menu.AddMenuOption(text, (who, _) =>
            {
                if (!Valid(who)) return;
                execute(who);
                menu.RefreshChatAfterCommand(who);
            });
        }
        if (mine.Length == 0)
            menu.AddMenuOption("暂无自己的 Bot，请先添加", (_, _) => { }, true);
        foreach (var entry in mine)
        {
            string number = entry.Id.ToString(CultureInfo.InvariantCulture);
            string Label()
            {
                if (entry.Retired) return $"Bot #{number} 已移除（可刷新列表）";
                int health = _live.GetValueOrDefault(entry.Id)?.Stats?.RemainingHealth ?? entry.Placement.Health;
                return $"!ze_bot_move {number} → {(entry.Placement.Team == 2 ? "僵尸 T" : "人类 CT")} · HP {health}" +
                    (entry.Bot == null ? " · 待创建" : "");
            }
            var option = menu.AddMenuOption(Label(), (who, _) =>
            {
                if (!Valid(who)) return;
                // Recheck the owner's live roster. A removed/stale row must never fall back to all.
                if (entry.Retired || !_roster.Owned(owner).Any(e => ReferenceEquals(e, entry)))
                    plugin.Reply(who, $"Bot #{number} 已失效，没有移动任何 Bot；可刷新列表。");
                else
                    plugin.ExecuteOwnCommand(who, "css_ze_bot_move " + number);
                menu.RefreshChatAfterCommand(who);
            });
            menu.BindLabel(option, Label);
        }
        if (mine.Length != 0)
            Action("传送我放置的全部 Bot（!ze_bot_move all）", who => plugin.ExecuteOwnCommand(who, "css_ze_bot_move all"));
        Action("刷新我的 Bot 编号列表", ShowMoveMenu);
        Action("返回公开指令", who => plugin.ExecuteOwnCommand(who, "css_ze_public"));
        plugin.OpenMenu(player, menu);
    }
}
