using System.Globalization;
using System.Net;
using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using CssTimer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace ZePractice;

/// <summary>Pure paging, input and connection checks shared by the native adapter and regression tests.</summary>
public static class ZePanelLogic
{
    public const int CardsPerPage = 10;
    public static int PageCount(int count) => (int)Math.Max(1L, ((long)Math.Max(0, count) + CardsPerPage - 1) / CardsPerPage);
    public static int ClampPage(int page, int count) => Math.Clamp(page, 0, PageCount(count) - 1);
    public static bool TryCard(string id, int page, int count, out int index)
    {
        index = -1;
        if (id.Length != 8 || !id.StartsWith("option_", StringComparison.Ordinal) || id[7] is < '0' or > '9') return false;
        long candidate = (long)page * CardsPerPage + id[7] - '0';
        if (page < 0 || candidate < 0 || candidate >= count) return false;
        index = (int)candidate; return true;
    }
    public static bool IsLoopback(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return false;
        if (IPEndPoint.TryParse(endpoint, out var ep)) return IPAddress.IsLoopback(ep.Address.IsIPv4MappedToIPv6 ? ep.Address.MapToIPv4() : ep.Address);
        return IPAddress.TryParse(endpoint, out var ip) && IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip);
    }
    public static bool SameConnection(int expectedSlot, int? expectedUserId, uint expectedController, int slot, int? userId, uint controller) =>
        expectedSlot == slot && expectedUserId == userId && expectedController == controller;
    public static bool AcceptClick(long expectedEpoch, long currentEpoch, uint expectedLayout, uint suppliedLayout, bool connected, bool captured, bool expired) =>
        expectedEpoch == currentEpoch && expectedLayout == suppliedLayout && connected && captured && !expired;
    public static string Text(string? value, int maxElements = 160)
    {
        if (string.IsNullOrEmpty(value) || maxElements <= 0) return "";
        var output = new StringBuilder();
        var elements = StringInfo.GetTextElementEnumerator(value);
        int count = 0;
        while (elements.MoveNext())
        {
            string item = elements.GetTextElement();
            // CSS chat colours/control characters are not Panorama markup. Dialog variables stay plain text.
            if (item.Any(char.IsControl)) { if (item is "\n" or "\r" or "\t") output.Append(' '); continue; }
            if (count++ >= maxElements) { output.Append('…'); break; }
            output.Append(item);
        }
        return output.ToString();
    }
}

/// <summary>
/// Real server-driven Panorama: compiled client assets + native custom_hud_layout mouse click messages.
/// Each page/navigation gets a new entity/serial; label refreshes retain the current page and layout.
/// This service never changes player movement/freeze flags and never forwards arbitrary button text as a command.
/// </summary>
public sealed class ZePanelService : IDisposable
{
    public const string LayoutResource = "panorama/layout/custom_game/zepractice/main.vxml";
    private const string LayoutFile = "panorama/layout/custom_game/zepractice/main.vxml_c";
    private const string StyleFile = "panorama/styles/custom_game/zepractice/main.vcss_c";
    private static readonly TimeSpan FirstClickTimeout = TimeSpan.FromSeconds(20);
    private readonly ZePracticePlugin _plugin;
    private readonly Dictionary<int, Session> _sessions = new();
    private readonly HashSet<uint> _confirmedClients = new();
    private readonly HashSet<uint> _chatOnlyClients = new();
    private readonly HashSet<uint> _warned = new();
    private readonly Dictionary<int, uint> _connections = new();
    private readonly Dictionary<string, CommandInfo.CommandCallback> _commands = new();
    private CssTimer? _maintenance;
    private long _epoch;
    private long _opened, _nativeClicks, _fallbacks;
    private bool _initialized, _disposed, _mapActive;
    private static readonly Tab[] Tabs =
    [
        new("tab_main", "css_ze", "css_ze"),
        new("tab_practice", "css_ze_practice", "css_ze_practice"),
        new("tab_weapons", "css_zeweapon", "css_zebuy"),
        new("tab_classes", "css_zeclass", "css_zeclass"),
        new("tab_items", "css_items", "css_items"),
        new("tab_maps", "css_ze_maps", "css_ze_maps"),
        new("tab_admin", "css_zeadmin", "css_zeadmin")
    ];
    private sealed record Tab(string Panel, string Command, string Permission);
    private sealed class Session
    {
        public required CCSPlayerController Player;
        public required int Slot;
        public required int? UserId;
        public required uint Controller;
        public required CCSCustomHudLayout Layout;
        public required uint LayoutHandle;
        public required long Epoch;
        public required ChatMenu Menu;
        public required ChatMenuOption[] Options;
        public required int Page;
        public string ActiveTab = "tab_main";
        public string StateSignature = "";
        public bool Captured, Confirmed, Busy;
        public DateTimeOffset Expires;
        public DateTimeOffset LastClick;
    }
    public ZePanelService(ZePracticePlugin plugin) => _plugin = plugin;

    public void Initialize()
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        _plugin.RegisterListener<Listeners.OnCustomHudClicked>(OnClick);
        _plugin.RegisterListener<Listeners.OnMapStart>(OnMapStart);
        _plugin.RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
        _plugin.RegisterListener<Listeners.OnClientDisconnect>(OnDisconnect);
        Register("css_ze_ui_chat", "Use chat menus for this connection and release mouse capture", (p, _) =>
        {
            if (!ZePracticePlugin.IsRealPlayer(p)) return;
            _plugin.UseChatMenus(p!);
        });
        Register("css_ze_ui_ready", "Confirm that the custom ZE panel is visible on this client", (p, _) =>
        {
            if (!ZePracticePlugin.IsRealPlayer(p)) return;
            SetChatPreference(p!, false);
            if (_plugin.RestoreRadioMenus(p!)) return;
            if (!_sessions.TryGetValue(p!.Slot, out var s) || !Owns(s, p))
            { _plugin.Reply(p, "先 !zemenu 显示面板；看到完整图形界面后，再 !ze_ui_ready 确认。"); return; }
            if (!AssetsAllowed(p, out var reason)) { Fallback(p, s.Menu, reason); return; }
            try { Confirm(s); _plugin.Reply(p, "已确认界面可见，鼠标点击已启用；使用后保持打开，点击关闭或 !ze_ui_close 退出。"); }
            catch (Exception e) { Fail(p, s.Menu, e); }
        });
        _plugin.Logger.LogInformation("ZE Panorama adapter registered. Compiled client assets and MenuAssetsReady are required; no player movement flags are modified.");
    }
    private void Register(string name, string help, CommandInfo.CommandCallback callback)
    { _commands.Add(name, callback); _plugin.AddZeCommand(name, help, callback); }

    public void OnMapStart(string map)
    {
        CloseAll(); _epoch++; _mapActive = true;
        _confirmedClients.Clear(); _chatOnlyClients.Clear(); _warned.Clear(); _connections.Clear();
        StartMaintenance();
    }
    public void OnMapEnd()
    {
        CloseAll(); _epoch++; _mapActive = false;
        _confirmedClients.Clear(); _chatOnlyClients.Clear(); _warned.Clear(); _connections.Clear();
        _maintenance?.Kill(); _maintenance = null;
    }
    private void StartMaintenance()
    {
        if (_maintenance == null && !_disposed && _mapActive)
            _maintenance = _plugin.AddTimer(0.5f, Maintenance, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }
    public string Diagnostics() => DiagnosticsFor(null);
    public string DiagnosticsFor(CCSPlayerController? viewer) => $"ZE Panorama mode={_plugin.Config.MenuUiMode}; assetsReady={_plugin.Config.MenuAssetsReady}; allowRemote={_plugin.Config.PanoramaAllowRemoteClients}; mapActive={_mapActive}; epoch={_epoch}; sessions={_sessions.Count}; opened={_opened}; nativeClicks={_nativeClicks}; fallbacks={_fallbacks}; layout={LayoutResource}; " +
        string.Join(" | ", _sessions.Values.Where(s => viewer == null || Owns(s, viewer)).Select(s => $"slot={s.Slot},user={s.UserId},entity=0x{s.LayoutHandle:X8},page={s.Page + 1},capture={s.Captured},confirmed={s.Confirmed},expires={s.Expires:o}"));
    public void RefreshPermissions() => CloseAll(); // Never leave stale enabled callbacks after a permission reload.
    public bool IsOpen(CCSPlayerController player) => !_disposed && _sessions.TryGetValue(player.Slot, out var s) && Owns(s, player);
    public bool PrefersChat(CCSPlayerController player) => _chatOnlyClients.Contains(player.EntityHandle.Raw);
    public ChatMenu? MenuFor(CCSPlayerController player) => _sessions.TryGetValue(player.Slot, out var s) && Owns(s, player) ? s.Menu : null;
    public void SetChatPreference(CCSPlayerController player, bool enabled)
    {
        _connections[player.Slot] = player.EntityHandle.Raw;
        if (enabled) _chatOnlyClients.Add(player.EntityHandle.Raw); else _chatOnlyClients.Remove(player.EntityHandle.Raw);
    }
    public void Open(CCSPlayerController player, ChatMenu menu)
    {
        if (_disposed || !ZePracticePlugin.IsRealPlayer(player)) return;
        _connections[player.Slot] = player.EntityHandle.Raw;
        if (!_plugin.Permissions.Allows(player, "css_ze")) { _plugin.Reply(player, "当前权限不允许打开菜单。"); return; }
        if (!AssetsAllowed(player, out var reason)) { Fallback(player, menu, reason); return; }
        // Also supports a plugin hot reload after the current map's OnMapStart already ran.
        _mapActive = true; StartMaintenance();
        OpenPage(player, menu, 0, GuessTab(menu.Title));
    }
    private bool AssetsAllowed(CCSPlayerController player, out string reason)
    {
        reason = "";
        if (!string.Equals(_plugin.Config.MenuUiMode, "panorama", StringComparison.OrdinalIgnoreCase) || _chatOnlyClients.Contains(player.EntityHandle.Raw)) return false;
        if (!_plugin.Config.MenuAssetsReady) { reason = "图形菜单资源尚未完成部署验证，已回退聊天菜单，不捕获鼠标。"; return false; }
        if (!ZePanelLogic.IsLoopback(player.IpAddress) && !_plugin.Config.PanoramaAllowRemoteClients)
        { reason = "远程玩家尚未启用图形资源分发。需要地图/资源Workshop携带UI后开启远程支持，当前使用聊天菜单。"; return false; }
        var csgo = Server.GameDirectory;
        if (Directory.Exists(Path.Combine(csgo, "csgo"))) csgo = Path.Combine(csgo, "csgo");
        if (!File.Exists(Path.Combine(csgo, LayoutFile)) || !File.Exists(Path.Combine(csgo, StyleFile)))
        { reason = "服务端缺少已编译的ZE面板资源，已回退聊天菜单，不捕获鼠标。"; return false; }
        return true;
    }
    private void Fallback(CCSPlayerController player, ChatMenu menu, string reason)
    {
        _fallbacks++; Close(player);
        if (!ZePracticePlugin.IsRealPlayer(player)) return;
        if (!string.IsNullOrEmpty(reason) && _warned.Add(player.EntityHandle.Raw)) _plugin.Reply(player, reason);
        MenuManager.OpenChatMenu(player, menu);
    }
    private void OpenPage(CCSPlayerController player, ChatMenu menu, int page, string activeTab)
    {
        Close(player); MenuManager.CloseActiveMenu(player);
        CCSCustomHudLayout? created = null;
        try
        {
            created = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout") ?? throw new InvalidOperationException("custom_hud_layout unavailable");
            created.StrLayout = LayoutResource; created.Observable = false;
            created.DispatchSpawn();
            // Hidden is declared in the asset too: other clients never see another player's newly spawned menu.
            created.SetHasClass("window", "hidden", true);
            var options = menu.MenuOptions.ToArray();
            var session = new Session
            {
                Player = player, Slot = player.Slot, UserId = player.UserId, Controller = player.EntityHandle.Raw,
                Layout = created, LayoutHandle = created.EntityHandle.Raw, Epoch = _epoch,
                Menu = menu, Options = options, Page = ZePanelLogic.ClampPage(page, options.Length), ActiveTab = activeTab,
                Confirmed = _confirmedClients.Contains(player.EntityHandle.Raw), Expires = DateTimeOffset.UtcNow + FirstClickTimeout
            };
            _sessions[player.Slot] = session; _opened++;
            Render(session);
            created.SetHasClassForPlayer(player, "window", "hidden", false);
            // Local permission is enabled only after parent deployment validates both client and server assets.
            // Remote clients need an explicit per-connection visibility confirmation; server assets are not proof.
            if (session.Confirmed || ZePanelLogic.IsLoopback(player.IpAddress))
            {
                Capture(session);
                if (session.Confirmed) session.Expires = DateTimeOffset.MaxValue;
            }
            else _plugin.Reply(player, "图形面板已请求显示但未捕获鼠标。看到完整面板后输入 !ze_ui_ready；若未显示，20秒后自动回退聊天菜单。");
        }
        catch (Exception e)
        {
            if (created != null && (!_sessions.TryGetValue(player.Slot, out var owned) || owned.Layout != created))
                try { if (created.IsValid) created.Remove(); } catch { }
            Fail(player, menu, e);
        }
    }
    private void Render(Session s)
    {
        if (s.Menu is PersistentChatMenu live) live.RefreshLabels();
        var player = s.Player; var hud = s.Layout;
        void Text(string panel, string value, int max = 160) => hud.SetDialogVariableStringForPlayer(player, panel, "text", ZePanelLogic.Text(value, max));
        void Class(string panel, string name, bool value) => hud.SetHasClassForPlayer(player, panel, name, value);
        Text("heading", s.Menu.Title, 55);
        Text("identity", player.PlayerName + "  ·  " + _plugin.Permissions.Describe(player), 95);
        Text("status", (_plugin.IsPractice ? "跑图练习" : "多人 ZE") + "  /  " + Server.MapName, 95);
        Text("page", $"{s.Page + 1} / {ZePanelLogic.PageCount(s.Options.Length)}    ·    {s.Options.Length} 项");
        Text("hint", s.Confirmed ? "执行后保留当前页 · 点击关闭或 !ze_ui_close 退出" : "首次点击确认界面 · 20秒未点击自动回退聊天菜单");
        Class("previous", "disabled", s.Page == 0);
        Class("next", "disabled", s.Page + 1 >= ZePanelLogic.PageCount(s.Options.Length));
        var toggles = _plugin.GetPracticeToggles(player);
        s.StateSignature = StateSignature(toggles);
        foreach (var key in new[] { "god", "noclip", "checkpoint" })
        {
            bool enabled = toggles.TryGetValue(key, out var state) && state;
            Class("state_" + key, "on", enabled);
            if (key is "god" or "noclip") Class("state_" + key, "disabled", !_plugin.IsPractice || !_plugin.Permissions.Allows(player, "css_ze_" + key));
            Text("text_" + key, (key == "god" ? "无敌" : key == "noclip" ? "穿墙" : "检查点") + (enabled ? "  开启" : "  关闭"));
        }
        foreach (var tab in Tabs)
        {
            bool allowed = _plugin.Permissions.Allows(player, tab.Permission) && (tab.Panel != "tab_admin" || _plugin.Permissions.HasAdminAccess(player));
            Class(tab.Panel, "disabled", !allowed);
            Class(tab.Panel, "active", tab.Panel == s.ActiveTab);
        }
        for (int i = 0; i < ZePanelLogic.CardsPerPage; i++)
        {
            int index = s.Page * ZePanelLogic.CardsPerPage + i;
            bool exists = index < s.Options.Length;
            Class("option_" + i, "empty", !exists);
            Class("option_" + i, "disabled", !exists || s.Options[index].Disabled);
            Text("card_text_" + i, exists ? s.Options[index].Text : "", 115);
            Text("card_num_" + i, exists ? (index + 1).ToString("00", CultureInfo.InvariantCulture) : "");
            Text("card_hint_" + i, !exists ? "" : s.Options[index].Disabled ? "当前不可用 / 权限不足" : "点击执行  →", 40);
        }
    }
    private string StateSignature(IReadOnlyDictionary<string, bool> toggles) =>
        (_plugin.IsPractice ? "P" : "Z") + string.Concat(new[] { "god", "noclip", "checkpoint" }.Select(k => toggles.TryGetValue(k, out var enabled) && enabled ? "1" : "0"));
    private static string GuessTab(string title)
    {
        if (title.Contains("管理") || title.Contains("处罚") || title.Contains("确认")) return "tab_admin";
        if (title.Contains("地图")) return "tab_maps";
        if (title.Contains("神器")) return "tab_items";
        if (title.Contains("职业")) return "tab_classes";
        if (title.Contains("补给") || title.Contains("枪械") || title.Contains("武器")) return "tab_weapons";
        if (title.Contains("个人") || title.Contains("辅助")) return "tab_practice";
        return "tab_main";
    }
    private bool Owns(Session s, CCSPlayerController p) => s.Epoch == _epoch && p.IsValid &&
        ZePanelLogic.SameConnection(s.Slot, s.UserId, s.Controller, p.Slot, p.UserId, p.EntityHandle.Raw);
    private static void Capture(Session s)
    {
        s.Layout.SetInputCaptureEnabled(s.Player, true);
        if (!s.Layout.IsInputCaptureEnabled(s.Player)) throw new InvalidOperationException("Custom HUD capture readback failed");
        s.Captured = true;
    }
    private void Confirm(Session s)
    {
        s.Confirmed = true; _confirmedClients.Add(s.Controller); s.Expires = DateTimeOffset.MaxValue;
        Capture(s); Render(s);
    }
    private void OnClick(CCSPlayerController player, CCSCustomHudLayout layout, string buttonId)
    {
        if (_disposed || !ZePracticePlugin.IsRealPlayer(player) || layout is not { IsValid: true } || !_sessions.TryGetValue(player.Slot, out var s) || s.Busy) return;
        if (!ZePanelLogic.AcceptClick(s.Epoch, _epoch, s.LayoutHandle, layout.EntityHandle.Raw, Owns(s, player), s.Captured, DateTimeOffset.UtcNow >= s.Expires)) return;
        if (DateTimeOffset.UtcNow - s.LastClick < TimeSpan.FromMilliseconds(120)) return;
        s.LastClick = DateTimeOffset.UtcNow; _nativeClicks++;
        try
        {
            if (!AssetsAllowed(player, out var reason)) { Fallback(player, s.Menu, reason); return; }
            if (!_plugin.Permissions.Allows(player, "css_ze")) { Close(player); return; }
            if (buttonId == "close") { _confirmedClients.Add(s.Controller); Close(player); return; }
            if (buttonId == "previous" || buttonId == "next")
            {
                int page = s.Page + (buttonId == "next" ? 1 : -1);
                if (page < 0 || page >= ZePanelLogic.PageCount(s.Options.Length)) return;
                _confirmedClients.Add(s.Controller); OpenPage(player, s.Menu, page, s.ActiveTab); return;
            }
            if (buttonId is "state_god" or "state_noclip")
            {
                string command = buttonId == "state_god" ? "css_ze_god" : "css_ze_noclip";
                if (!_plugin.IsPractice || !_plugin.Permissions.Allows(player, command)) return;
                Confirm(s); s.Busy = true;
                _plugin.ExecuteOwnCommand(player, command);
                if (Current(s)) { s.Busy = false; Render(s); }
                return;
            }
            var tab = Tabs.FirstOrDefault(x => x.Panel == buttonId);
            if (tab != null)
            {
                if (!_plugin.Permissions.Allows(player, tab.Permission) || (tab.Panel == "tab_admin" && !_plugin.Permissions.HasAdminAccess(player))) return;
                _confirmedClients.Add(s.Controller); s.Busy = true; Close(player);
                _plugin.ExecuteOwnCommand(player, tab.Command); return;
            }
            if (!ZePanelLogic.TryCard(buttonId, s.Page, s.Options.Length, out var index)) return;
            var option = s.Options[index];
            if (option.Disabled) return;
            Confirm(s); s.Busy = true;
            if (s.Menu.PostSelectAction == PostSelectAction.Close) Close(player);
            option.OnSelect(player, option); // Existing callback retains its own target/permission/confirmation checks.
            if (!Current(s)) return; // Explicit close or child navigation wins; never resurrect the old layout.
            s.Busy = false;
            if (s.Menu.PostSelectAction == PostSelectAction.Reset) { OpenPage(player, s.Menu, 0, s.ActiveTab); return; }
            Render(s);
        }
        catch (Exception e) { Fail(player, s.Menu, e); }
    }
    private bool Current(Session s) => !_disposed && Owns(s, s.Player) &&
        _sessions.TryGetValue(s.Slot, out var current) && ReferenceEquals(current, s);
    private void Maintenance()
    {
        if (_disposed || !_mapActive) return;
        foreach (var s in _sessions.Values.ToArray())
        {
            try
            {
                if (!s.Player.IsValid || !Owns(s, s.Player)) { Retire(s); continue; }
                if (DateTimeOffset.UtcNow < s.Expires)
                {
                    // Client commands may be consumed next frame; keyboard toggles must update an already open panel too.
                    bool labelsChanged = s.Menu is PersistentChatMenu live && live.RefreshLabels();
                    if (labelsChanged || s.StateSignature != StateSignature(_plugin.GetPracticeToggles(s.Player))) Render(s);
                    continue;
                }
                bool confirmed = s.Confirmed || _confirmedClients.Contains(s.Controller);
                Close(s.Player);
                if (!confirmed)
                {
                    _chatOnlyClients.Add(s.Controller); MenuManager.OpenChatMenu(s.Player, s.Menu);
                    _plugin.Reply(s.Player, "图形面板未收到可见性确认，已自动释放鼠标并回退聊天菜单。资源就绪后 !ze_ui_ready 再 !zemenu 重试。");
                }
                else _plugin.Reply(s.Player, "图形菜单超时已关闭，鼠标已释放；!zemenu 可重新打开。");
            }
            catch (Exception e)
            {
                _plugin.Logger.LogWarning(e, "ZE panel maintenance failed for slot {Slot}; releasing owned layout", s.Slot);
                Retire(s);
            }
        }
    }
    public void Close(CCSPlayerController player)
    {
        if (_sessions.TryGetValue(player.Slot, out var s) && Owns(s, player)) Retire(s);
    }
    private void Retire(Session s)
    {
        if (_sessions.TryGetValue(s.Slot, out var current) && ReferenceEquals(current, s)) _sessions.Remove(s.Slot);
        try
        {
            if (s.Layout.IsValid && s.Player.IsValid && Owns(s, s.Player))
            { s.Layout.SetInputCaptureEnabled(s.Player, false); s.Layout.SetHasClassForPlayer(s.Player, "window", "hidden", true); }
        }
        catch (Exception e) { _plugin.Logger.LogWarning(e, "ZE panel input release failed for slot {Slot}; removing owned layout", s.Slot); }
        finally
        {
            s.Captured = false;
            try { if (s.Layout.IsValid) s.Layout.Remove(); }
            catch (Exception e) { _plugin.Logger.LogWarning(e, "ZE panel entity removal failed for slot {Slot}", s.Slot); }
        }
    }
    private void CloseAll() { foreach (var s in _sessions.Values.ToArray()) Retire(s); _sessions.Clear(); }
    private void OnDisconnect(int slot)
    {
        if (_connections.Remove(slot, out var controller))
        { _confirmedClients.Remove(controller); _chatOnlyClients.Remove(controller); _warned.Remove(controller); }
        if (_sessions.TryGetValue(slot, out var s)) Retire(s);
    }
    private void Fail(CCSPlayerController player, ChatMenu menu, Exception error)
    {
        _plugin.Logger.LogError(error, "ZE Panorama operation failed; releasing input and using chat menu");
        Fallback(player, menu, "图形面板发生错误，已释放鼠标并回退聊天菜单。详情见服务端日志。");
    }
    public void Dispose()
    {
        if (_disposed) return;
        CloseAll(); _disposed = true; _maintenance?.Kill(); _maintenance = null;
        _confirmedClients.Clear(); _chatOnlyClients.Clear(); _warned.Clear(); _connections.Clear();
        if (!_initialized) return;
        _plugin.RemoveListener<Listeners.OnCustomHudClicked>(OnClick);
        _plugin.RemoveListener<Listeners.OnMapStart>(OnMapStart);
        _plugin.RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
        _plugin.RemoveListener<Listeners.OnClientDisconnect>(OnDisconnect);
        foreach (var c in _commands) _plugin.RemoveZeCommand(c.Key, c.Value);
        _commands.Clear();
    }
}
