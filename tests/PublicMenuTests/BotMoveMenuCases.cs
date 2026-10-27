using System.Collections;
using System.Reflection;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using ZePractice;

internal static class BotMoveMenuCases
{
    public static void Run(Action<bool, string> check)
    {
        var p = new CCSPlayerController { Slot = 20, UserId = 120 };
        var other = new CCSPlayerController { Slot = 21, UserId = 121 };
        Utilities.Players.AddRange([p, other]);
        var plugin = new ZePracticePlugin();
        var bots = new PracticeBotService(plugin);
        var publicMenus = new PublicGameplayService(plugin);
        var t = bots.Seed(p, 2, 35000);
        var ct = bots.Seed(p, 3, 777);
        var foreign = bots.Seed(other, 3);
        plugin.OnCommand = (who, cmd) =>
        {
            if (cmd.Split(' ')[0] == "css_ze_bot_move") bots.Invoke(who, cmd.Split(' '));
            if (cmd == "css_ze_public") publicMenus.Open(who);
        };
        void Open() => bots.Invoke(p, "css_ze_bot_move");
        ChatMenuOption Row(long id) => plugin.Current!.MenuOptions.Single(o => o.Text.StartsWith("!ze_bot_move " + id + " →"));
        void Click(ChatMenuOption option, CCSPlayerController? who = null) => option.OnSelect(who ?? p, option);
        ChatMenuOption Named(string text) => plugin.Current!.MenuOptions.Single(o => o.Text.Contains(text));

        publicMenus.Open(p); Click(Named("!ze_bot_move"));
        check(plugin.Current!.Title.Contains("选择编号") && bots.Moves.Count == 0, "public move entry opens a numbered submenu without teleporting all");
        var menu = plugin.Current!;
        check(Row(t.Id).Text.Contains("僵尸 T") && Row(t.Id).Text.Contains("35000") && Row(ct.Id).Text.Contains("人类 CT") && Row(ct.Id).Text.Contains("777"), "move choices show both teams and health with real roster IDs");
        check(!menu.MenuOptions.Any(o => o.Text.StartsWith("!ze_bot_move " + foreign.Id + " →")), "move menu excludes another player's Bot");
        check(menu.PostSelectAction == PostSelectAction.Nothing && menu.ExitButton, "move selector stays open and offers manual close");
        Click(Row(ct.Id));
        check(bots.Moves.SequenceEqual([ct.Id.ToString()]) && plugin.Commands.Last() == "css_ze_bot_move " + ct.Id, "numbered choice sends only that Bot ID");
        check(ReferenceEquals(menu, plugin.Current), "moving one Bot does not close or replace its submenu");
        Click(Row(t.Id));
        check(bots.Moves.Last() == t.Id.ToString() && bots.Moves.Count == 2, "different Bot choice does not reuse previous ID");
        Click(Named("传送我放置的全部"));
        check(bots.Moves.Last() == "all", "moving all requires the explicit all row");

        var stale = Row(t.Id); int before = bots.Moves.Count;
        Click(stale, other); check(bots.Moves.Count == before, "other player cannot activate owner's Bot row");
        p.UserId++; Click(stale); p.UserId--;
        check(bots.Moves.Count == before, "reused slot with new userid cannot move previous owner's Bot");
        p.EntityHandle.Raw++; Click(stale); p.EntityHandle.Raw--;
        check(bots.Moves.Count == before, "controller serial change invalidates Bot row");
        plugin.Permissions.Deny.Add("css_ze_bot_move"); Click(stale); plugin.Permissions.Deny.Clear();
        check(bots.Moves.Count == before, "move permission is rechecked at selection time");
        bots.Alive = false; Click(stale); bots.Alive = true;
        check(bots.Moves.Count == before, "dead or changing-team owner cannot execute stale selection");
        bots.Retire(t); Click(stale);
        check(bots.Moves.Count == before && plugin.Messages.Last().Contains("已失效"), "removed Bot does not fall back to moving all");
        ((PersistentChatMenu)menu).RefreshLabels();
        check(stale.Text.Contains("已移除"), "removed row updates its label without reopening the window");
        Click(Named("刷新我的"));
        check(plugin.Current != menu && !plugin.Current!.MenuOptions.Any(o => o.Text.StartsWith("!ze_bot_move " + t.Id + " →")), "refresh removes stale entries");
        stale = Row(ct.Id); bots.NewConnection(); Click(stale);
        check(bots.Moves.Count == before, "connection epoch prevents inheriting a Bot menu after reconnect");
        Open();
        check(plugin.Current!.MenuOptions.Any(o => o.Disabled && o.Text.Contains("暂无")) && !plugin.Current.MenuOptions.Any(o => o.Text.Contains("传送我放置的全部")), "empty personal roster has an explanation and no all action");
        var pending = bots.Seed(p, 3, 1234, true); Click(Named("刷新我的"));
        check(Row(pending.Id).Text.Contains("待创建"), "pending Bot is clearly labelled rather than silently omitted");
        Click(Row(pending.Id));
        check(bots.Moves.Last() == pending.Id.ToString(), "pending row still sends a specific ID, never all");
        before = bots.Moves.Count; bots.Invoke(p, "css_ze_bot_move", "1", "2");
        check(bots.Moves.Count == before && plugin.Messages.Last().Contains("用法"), "extra arguments are rejected rather than moving several Bots");
        bots.Invoke(p, "css_ze_bot_move", "");
        check(bots.Moves.Count == before && plugin.Current!.Title.Contains("选择编号"), "explicit empty argument also opens selector without moving all");
        stale = Row(pending.Id); bots.NextMap(); Click(stale);
        check(bots.Moves.Count == before, "map generation invalidates old Bot menu actions");
        Open(); Click(Named("返回公开指令"));
        check(plugin.Current!.Title.Contains("公开指令"), "Bot selector returns to public commands explicitly");
        Open(); stale = Named("刷新我的"); bots.Stop(); Click(stale);
        check(bots.Moves.Count == before, "unloaded service ignores an old Bot menu callback");

        // Exercise the real radio adapter and its delayed command bridge as well.
        var rp = new ZePracticePlugin(); var rb = new PracticeBotService(rp);
        var rpub = new PublicGameplayService(rp);
        var r1 = rb.Seed(p, 2, 35000); var r2 = rb.Seed(p, 3);
        using var radio = new RadioPanelService(rp); rp.OpenAdapter = radio.Open;
        rp.OnCommand = (who, cmd) => Server.NextFrame(() =>
        {
            if (cmd.Split(' ')[0] == "css_ze_bot_move") rb.Invoke(who, cmd.Split(' '));
        });
        object Session() => ((IDictionary)typeof(RadioPanelService).GetField("_sessions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(radio)!)[p.Slot]!;
        void Key(int key)
        {
            var session = Session();
            session.GetType().GetField("LastKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(session, Environment.TickCount64 - 500);
            radio.Select(p, key); Server.Drain();
        }
        rpub.Open(p); Server.Drain(); Key(8); Key(6);
        check(rp.Current!.Title.Contains("选择编号") && rb.Moves.Count == 0, "radio parent opens move selector without moving any Bot");
        var rs = Session(); Key(1); Key(2);
        check(rb.Moves.SequenceEqual([r1.Id.ToString(), r2.Id.ToString()]) && ReferenceEquals(Session(), rs), "radio repeated individual moves preserve selector and send exact IDs once");
        Key(3);
        check(rb.Moves.Last() == "all" && ReferenceEquals(Session(), rs), "radio all action is separate and also keeps menu open");
        Key(9); check(!radio.IsOpen(p), "radio explicit close still closes the new Bot selector");
        for (int i = 0; i < 10; i++) rb.Seed(p, i % 2 == 0 ? 2 : 3);
        rb.Invoke(p, "css_ze_bot_move"); Server.Drain(); Key(8); Key(1);
        check(rb.Moves.Last() == "7", "owner with many Bots selects the real ID on later radio pages, not the page's row number");
        Key(8); Key(1);
        check(rb.Moves.Last() == "all", "explicit all remains separate after pagination for an unlimited owner");
        Key(9);
    }
}
