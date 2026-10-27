using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
namespace ZePractice;
public sealed class RadioPanelService : IDisposable
{
    public const string Layout = "panorama/layout/custom_game/zepractice/radio.vxml_c";
    public static readonly string[] Required = [Layout, "panorama/styles/custom_game/zepractice/radio.vcss_c", "resource/ui/radiopanel.txt"];
    private readonly ZePracticePlugin _plugin;
    private readonly Dictionary<int, Session> _sessions = new();
    private readonly RadioPanelRequests _requests = new();
    private readonly Dictionary<int, Pending> _pending = new();
    private sealed record Pending(int Slot, int? UserId, uint Controller, ChatMenu Menu);
    private CommanderResources.Availability? _resources;
    private readonly CounterStrikeSharp.API.Modules.Timers.Timer _maintenance;
    private bool _disposed;
    private long _opened, _selected, _fallbacks;
    private string _lastFailure = "none";
    private sealed class Session
    {
        public required CCSPlayerController Player;
        public required int Slot;
        public required uint Controller;
        public required int? UserId;
        public required CCSCustomHudLayout Layout;
        public required ChatMenu Menu;
        public required ChatMenuOption[] Options;
        public int Page;
        public long LastKey;
    }
    public RadioPanelService(ZePracticePlugin plugin)
    {
        _plugin = plugin;
        try { ZeHudText.Prepare(); }
        catch (Exception error) { plugin.Logger.LogWarning(error, "ZE dynamic radio initialization deferred until first render"); }
        _maintenance = plugin.AddTimer(1, Maintain, TimerFlags.REPEAT);
        plugin.RegisterListener<Listeners.OnMapEnd>(CloseAll);
        plugin.RegisterListener<Listeners.OnMapStart>(OnMapStart);
        plugin.RegisterListener<Listeners.OnServerPrecacheResources>(Precache);
        plugin.RegisterListener<Listeners.CheckTransmit>(Transmit);
        plugin.RegisterListener<Listeners.OnClientDisconnect>(Close);
    }
    public bool IsOpen(CCSPlayerController player) => !_disposed && _sessions.TryGetValue(player.Slot, out var session) && Owns(session, player);
    public bool IsOpenOrPending(CCSPlayerController player) => IsOpen(player) || PendingFor(player) != null;
    private Pending? PendingFor(CCSPlayerController player) => !_disposed && player.IsValid && _pending.TryGetValue(player.Slot, out var p) &&
        ZePanelLogic.SameConnection(p.Slot, p.UserId, p.Controller, player.Slot, player.UserId, player.EntityHandle.Raw) ? p : null;
    public ChatMenu? MenuFor(CCSPlayerController player) => PendingFor(player)?.Menu ??
        (_sessions.TryGetValue(player.Slot, out var s) && Owns(s, player) ? s.Menu : null);
    private static bool Owns(Session session, CCSPlayerController player) => player.IsValid && session.Player.IsValid &&
        ZePanelLogic.SameConnection(session.Slot, session.UserId, session.Controller, player.Slot, player.UserId, player.EntityHandle.Raw);
    public void RefreshResources() => _resources = CommanderResources.Installed(Server.GameDirectory, Required);
    private void OnMapStart(string map) { CloseAll(); RefreshResources(); }
    private void Precache(ResourceManifest manifest)
    {
        RefreshResources();
        // The engine appends the compiled suffix; manifests use logical names, unlike VPK checks/layouts.
        if (_resources!.Ready) foreach (string resource in Required.Take(2))
            manifest.AddResource(resource.EndsWith("_c", StringComparison.Ordinal) ? resource[..^2] : resource);
    }
    private void Transmit(CCheckTransmitInfoList list)
    {
        if (_disposed) return;
        foreach (var (info, viewer) in list)
            foreach (var session in _sessions.Values)
                if (session.Layout is { IsValid: true } hud)
                {
                    if (viewer != null && Owns(session, viewer)) info.TransmitEntities.Add(hud);
                    else info.TransmitEntities.Remove(hud);
                }
    }
    public void Open(CCSPlayerController player, ChatMenu menu)
    {
        if (_disposed || !ZePracticePlugin.IsRealPlayer(player)) return;
        int slot = player.Slot;
        uint controller = player.EntityHandle.Raw;
        int? userId = player.UserId;
        long request = _requests.Enqueue(slot);
        _pending[slot] = new(slot, userId, controller, menu);
        Server.NextFrame(() =>
        {
            if (_disposed || !_requests.Take(slot, request)) return;
            _pending.Remove(slot);
            if (!ZePracticePlugin.IsRealPlayer(player) ||
                !ZePanelLogic.SameConnection(slot, userId, controller, player.Slot, player.UserId, player.EntityHandle.Raw)) return;
            OpenPage(player, menu, 0);
        });
    }
    private void OpenPage(CCSPlayerController player, ChatMenu menu, int page)
    {
        Close(player.Slot);
        MenuManager.CloseActiveMenu(player);
        if (_disposed || !ZePracticePlugin.IsRealPlayer(player) || !_plugin.Permissions.Allows(player, "css_ze")) return;
        CCSCustomHudLayout? layout = null;
        try
        {
            if (!_plugin.Config.MenuAssetsReady || (!ZePanelLogic.IsLoopback(player.IpAddress) && !_plugin.Config.PanoramaAllowRemoteClients))
            { Fallback(player, menu, "当前配置未开放此客户端的图形菜单"); return; }
            if (_resources == null || !_resources.Ready) RefreshResources();
            if (!_resources!.Ready) { Fallback(player, menu, "工坊菜单资源未就绪：" + _resources.Status); return; }
            layout = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout") ?? throw new InvalidOperationException("custom_hud_layout unavailable");
            using var values = new CEntityKeyValues();
            values.SetString("layout", Layout);
            values.SetString("targetname", "ze_radio_hud_" + player.Slot);
            layout.Observable = false;
            layout.DispatchSpawn(values);
            layout.SetHasClass("radio_window", "hidden", true);
            var session = new Session { Player = player, Slot = player.Slot, Controller = player.EntityHandle.Raw, UserId = player.UserId,
                Layout = layout, Menu = menu, Options = menu.MenuOptions.ToArray(), Page = RadioPanelLogic.ClampPage(page, menu.MenuOptions.Count),
                LastKey = Environment.TickCount64 - 200 };
            _sessions[player.Slot] = session;
            Render(session);
            layout.SetHasClassForPlayer(player, "radio_window", "hidden", false);
            _opened++;
        }
        catch (Exception error)
        {
            _sessions.Remove(player.Slot);
            try { if (layout is { IsValid: true }) layout.Remove(); }
            catch (Exception cleanup) { _plugin.Logger.LogWarning(cleanup, "ZE dynamic radio failed-layout cleanup"); }
            _plugin.Logger.LogError(error, "ZE dynamic radio render failed");
            Fallback(player, menu, "右侧菜单创建失败（" + error.GetType().Name + "）");
        }
    }
    private void Fallback(CCSPlayerController player, ChatMenu menu, string reason)
    {
        Close(player.Slot); _fallbacks++; _lastFailure = reason;
        if (!ZePracticePlugin.IsRealPlayer(player) || !_plugin.Permissions.Allows(player, "css_ze")) return;
        MenuManager.OpenChatMenu(player, menu);
        _plugin.Reply(player, reason + "，已切换可操作的聊天菜单。聊天 !数字 选择，!0 或 !ze_ui_close 关闭；!ze_ui_status 查看诊断。");
    }
    private void Render(Session session)
    {
        if (session.Menu is PersistentChatMenu live) live.RefreshLabels();
        void Text(string panel, string value, int max = 100) => ZeHudText.Set(session.Layout, panel, "text", ZePanelLogic.Text(value, max));
        Text("radio_title", session.Menu.Title, 58);
        for (int row = 0; row < RadioPanelLogic.PageSize; row++)
        {
            int index = session.Page * RadioPanelLogic.PageSize + row;
            bool exists = index < session.Options.Length;
            string label = exists ? $"{row + 1}. {session.Options[index].Text}" : "";
            Text("radio_line_" + row, label);
            session.Layout.SetHasClass("radio_line_" + row, "disabled", exists && session.Options[index].Disabled);
        }
        bool previous = session.Page > 0;
        bool next = session.Page + 1 < RadioPanelLogic.PageCount(session.Options.Length);
        Text("radio_previous", previous ? "7. 上一页" : "7. 上一页（首页）");
        Text("radio_next", next ? "8. 下一页" : "8. 下一页（末页）");
        session.Layout.SetHasClass("radio_previous", "disabled", !previous);
        session.Layout.SetHasClass("radio_next", "disabled", !next);
        Text("radio_page", $"第 {session.Page + 1}/{RadioPanelLogic.PageCount(session.Options.Length)} 页 · 聊天 !数字 操作 · !0 关闭");
    }
    public void Select(CCSPlayerController player, int key)
    {
        if (key is 0 or 9) { if (IsOpenOrPending(player)) Close(player.Slot); return; }
        if (_disposed || !_sessions.TryGetValue(player.Slot, out var queued) || !Owns(queued, player)) return;
        Server.NextFrame(() =>
        {
            if (!_disposed && _sessions.TryGetValue(player.Slot, out var current) && ReferenceEquals(current, queued))
                SelectNow(player, key);
        });
    }
    private void SelectNow(CCSPlayerController player, int key)
    {
        if (!_sessions.TryGetValue(player.Slot, out var session) || !Owns(session, player)) return;
        if (!_plugin.Permissions.Allows(player, "css_ze")) { Close(player.Slot); return; }
        if (key is 0 or 9) { Close(player.Slot); return; }
        long now = Environment.TickCount64;
        if (now - session.LastKey < 120) return;
        session.LastKey = now;
        try
        {
            if (key is 7 or 8)
            {
                int page = session.Page + (key == 7 ? -1 : 1);
                session.Page = RadioPanelLogic.ClampPage(page, session.Options.Length);
                Render(session);
                return;
            }
            if (!RadioPanelLogic.TrySelection(key, session.Page, session.Options.Length, out int index) || session.Options[index].Disabled) return;
            var option = session.Options[index];
            if (session.Menu.PostSelectAction == PostSelectAction.Close) Close(player.Slot);
            _selected++;
            option.OnSelect(player, option);
            // Keep the existing entity and page. A close or a child-menu request must never be undone.
            if (!Current(session)) return;
            if (session.Menu.PostSelectAction == PostSelectAction.Reset) session.Page = 0;
            Render(session);
            Server.NextFrame(() =>
            {
                if (!Current(session)) return;
                try { Render(session); }
                catch (Exception error) { Recover(session, error, "refresh"); }
            });
        }
        catch (Exception error) { Recover(session, error, "selection"); }
    }
    private bool Current(Session session) => !_disposed && Owns(session, session.Player) &&
        _sessions.TryGetValue(session.Player.Slot, out var current) && ReferenceEquals(current, session) &&
        !_requests.Contains(session.Player.Slot);
    public string Diagnostics() => DiagnosticsFor(null);
    public string DiagnosticsFor(CCSPlayerController? viewer) => $"ZE dynamic radio sessions={_sessions.Count}; pending={_requests.Count}; opened={_opened}; selected={_selected}; fallbacks={_fallbacks}; " +
        $"assetsReady={_plugin.Config.MenuAssetsReady}; allowRemote={_plugin.Config.PanoramaAllowRemoteClients}; resources={_resources?.Status ?? "not-checked"}; lastFailure={_lastFailure}; layout={Layout}; " +
        string.Join(" | ", _sessions.Values.Where(s => viewer == null || Owns(s, viewer)).Select(session => $"slot={session.Player.Slot},entity=0x{session.Layout.EntityHandle.Raw:X8},title={ZePanelLogic.Text(session.Menu.Title, 58)},page={session.Page + 1}/{RadioPanelLogic.PageCount(session.Options.Length)},options={session.Options.Length},close=manual"));
    private void Recover(Session session, Exception error, string stage)
    {
        _plugin.Logger.LogError(error, "ZE dynamic radio {Stage} failed", stage);
        // Do not undo a close or replace a child menu if its callback failed after navigating away.
        if (Current(session)) Fallback(session.Player, session.Menu, "右侧菜单刷新失败，已释放旧面板");
    }
    private void Maintain()
    {
        foreach (var entry in _sessions.ToArray())
        {
            try
            {
                var session = entry.Value;
                if (!Owns(session, session.Player) || !_plugin.Permissions.Allows(session.Player, "css_ze")) { Close(entry.Key); continue; }
                if (!session.Layout.IsValid)
                { if (Current(session)) Fallback(session.Player, session.Menu, "右侧菜单实体已失效"); continue; }
                if (Current(session) && session.Menu is PersistentChatMenu live && live.RefreshLabels()) Render(session);
            }
            catch (Exception error) { Recover(entry.Value, error, "maintenance"); }
        }
    }
    public void Close(int slot)
    {
        _requests.Cancel(slot);
        _pending.Remove(slot);
        if (!_sessions.Remove(slot, out var session)) return;
        try { if (session.Layout.IsValid) session.Layout.Remove(); }
        catch (Exception error) { _plugin.Logger.LogWarning(error, "ZE dynamic radio cleanup failed"); }
    }
    public void CloseAll() { _requests.Clear(); _pending.Clear(); foreach (int slot in _sessions.Keys.ToArray()) Close(slot); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _maintenance.Kill();
        CloseAll();
        _plugin.RemoveListener<Listeners.OnMapEnd>(CloseAll);
        _plugin.RemoveListener<Listeners.OnMapStart>(OnMapStart);
        _plugin.RemoveListener<Listeners.OnServerPrecacheResources>(Precache);
        _plugin.RemoveListener<Listeners.CheckTransmit>(Transmit);
        _plugin.RemoveListener<Listeners.OnClientDisconnect>(Close);
    }
}
