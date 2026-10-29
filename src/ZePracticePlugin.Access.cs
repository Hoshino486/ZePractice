using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using Microsoft.Extensions.Logging;
namespace ZePractice;

public sealed partial class ZePracticePlugin
{
    public ZePermissionService Permissions { get; private set; } = null!;
    private ZePanelService? _panel;
    private RadioPanelService? _radioPanel;
    private readonly Dictionary<string,(string Description,CommandInfo.CommandCallback Original,CommandInfo.CommandCallback Guarded)> _zeCommands=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int,(int? UserId,long At)> _bridgeRate=new();
    private bool _accessDisposed;
    public IReadOnlyDictionary<string,bool> GetPracticeToggles(CCSPlayerController player)=>_admin?.GetPracticeToggles(player)??new Dictionary<string,bool>();
    public void TogglePublicNoclip(CCSPlayerController? player) => _publicGameplay?.ToggleNoclip(player);
    public bool PublicNoclipEnabled(CCSPlayerController player) => _publicGameplay?.NoclipEnabled(player) == true;
    public bool TeamResetPending(CCSPlayerController player) => _publicGameplay?.TeamResetPending(player) == true;
    public void TrackIssuedGun(CCSWeaponBase weapon) { _publicGameplay?.TrackIssuedGun(weapon); _mapMarkers?.TrackIssuedGun(weapon); }
    public void NominateWorkshop(CCSPlayerController? player, string identifier) => _mapVote?.Nominate(player, identifier);
    public void OpenMapVoteMenu(CCSPlayerController? player, string search = "") => _mapVote?.ShowMenu(player, search);
    public bool ForceMapChange(CCSPlayerController? player, string command) => _mapVote?.ForceChange(player, command) == true;
    public bool ForceRtv(CCSPlayerController? player) => _mapVote?.ForceRtv(player) == true;
    public bool ZombieGearEnabled(CCSPlayerController? player) => _publicGameplay?.ZombieGearEnabled(player) == true;
    public void RestartManualRound(CCSPlayerController? player)
    {
        _roundGuard?.PermitManualEnd();
        Server.ExecuteCommand("mp_restartgame 1");
        Reply(player, "已请求 1 秒后重启回合。");
    }
    public void EndManualRound(CCSPlayerController? player)
    {
        var rules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        if (rules == null) { Reply(player, "地图回合尚未初始化。"); return; }
        _roundGuard?.PermitManualEnd();
        rules.TerminateRound(0.5f, CounterStrikeSharp.API.Modules.Entities.Constants.RoundEndReason.RoundDraw);
        Reply(player, "已结束当前回合，将开启下一回合。");
    }
    public bool NeedPermission(CCSPlayerController? player,string command)
    {
        if(Permissions.Allows(player,ZeCommandText.Canonical(command)))return true;
        Reply(player,"没有此操作权限："+ZeCommandText.Canonical(command)+"。!ze_whoami 查看身份。 ");return false;
    }
    public void AddZeCommand(string name,string description,CommandInfo.CommandCallback callback)
    {
        CommandInfo.CommandCallback guarded=(p,c)=>
        {
            if(_accessDisposed||(!IsMenuEscape(p,name,c.ArgCount==2?c.GetArg(1):"")&&!NeedPermission(p,name)))return;
            try{callback(p,c);}catch(Exception e){Logger.LogError(e,"ZE command {Command} failed",name);Reply(p,"操作失败，详情记录在服务端日志。");}
        };
        _zeCommands.Add(name,(description,callback,guarded));AddCommand(name,description,guarded);
    }
    public void RemoveZeCommand(string name,CommandInfo.CommandCallback original)
    {
        if(_zeCommands.Remove(name,out var command))RemoveCommand(name,command.Guarded);
    }
    public void ExecuteOwnCommand(CCSPlayerController player,string commandLine)
    {
        if(!IsRealPlayer(player)||_accessDisposed||!ZeCommandText.TryParseBody(commandLine,out var name,out var args,out var key)||key!=0)return;
        if(!_zeCommands.ContainsKey(name)){Reply(player,"此按钮没有对应的已注册操作。");return;}
        if(!IsMenuEscape(player,name,args)&&!NeedPermission(player,name))return;
        // The native call carries the original player slot: never elevate it to server/RCON.
        player.ExecuteClientCommandFromServer(name+(args.Length>0?" "+args:""));
    }
    public void OpenMenu(CCSPlayerController player,ChatMenu menu)
    {
        // All server command menus remain usable until the player chooses to close or navigate away.
        menu.ExitButton = true;
        menu.PostSelectAction = PostSelectAction.Nothing;
        if (_accessDisposed || !IsRealPlayer(player)) return;
        if (_panel?.PrefersChat(player) == true || Config.MenuUiMode.Equals("chat", StringComparison.OrdinalIgnoreCase))
        { CloseMenus(player); MenuManager.OpenChatMenu(player,menu); }
        else if (Config.MenuUiMode.Equals("radio", StringComparison.OrdinalIgnoreCase) && _radioPanel != null)
        { _panel?.Close(player); _radioPanel.Open(player, menu); }
        else { _radioPanel?.Close(player.Slot); if(_panel!=null)_panel.Open(player,menu);else MenuManager.OpenChatMenu(player,menu); }
    }
    private bool MenuIsOpen(CCSPlayerController p) => _radioPanel?.IsOpenOrPending(p) == true || _panel?.IsOpen(p) == true || MenuManager.GetActiveMenu(p) != null;
    private bool IsMenuEscape(CCSPlayerController? p, string name, string argument) =>
        name is "css_ze_ui_close" or "css_ze_ui_chat" ||
        (name == "css_ze_select" && argument is "0" or "9") ||
        (name == "css_ze_ui" && IsRealPlayer(p) && MenuIsOpen(p!));
    public void CloseMenus(CCSPlayerController p)
    {
        _panel?.Close(p);
        if (_radioPanel?.IsOpenOrPending(p) == true) _radioPanel.Close(p.Slot);
        MenuManager.CloseActiveMenu(p);
    }
    public void UseChatMenus(CCSPlayerController p)
    {
        var menu = _radioPanel?.MenuFor(p) ?? _panel?.MenuFor(p) ?? (MenuManager.GetActiveMenu(p) as BaseMenuInstance)?.Menu as ChatMenu;
        CloseMenus(p); _panel?.SetChatPreference(p, true);
        if (menu != null && Permissions.Allows(p, "css_ze")) MenuManager.OpenChatMenu(p, menu);
        Reply(p,"本次连接已切换聊天菜单；!数字 选择，!0 / !9 / !ze_ui_close 关闭；!ze_ui_ready 恢复图形菜单。");
    }
    public bool RestoreRadioMenus(CCSPlayerController p)
    {
        if (!Config.MenuUiMode.Equals("radio", StringComparison.OrdinalIgnoreCase)) return false;
        var menu = _radioPanel?.MenuFor(p) ?? (MenuManager.GetActiveMenu(p) as BaseMenuInstance)?.Menu as ChatMenu;
        CloseMenus(p); _panel?.SetChatPreference(p, false); _radioPanel?.RefreshResources();
        if (menu != null) OpenMenu(p, menu);
        Reply(p,"已恢复右侧菜单模式；!ze 打开，!0 / !9 / !ze_ui_close 关闭。无线电菜单不捕获鼠标。");
        return true;
    }
    private void SelectMenuKey(CCSPlayerController p, int key)
    {
        if (key is 0 or 9) { CloseMenus(p); return; }
        if (!NeedPermission(p, "css_ze")) return;
        if (_radioPanel?.IsOpenOrPending(p) == true) { _radioPanel.Select(p, key); return; }
        if (MenuManager.GetActiveMenu(p) != null) { MenuManager.OnKeyPress(p, key); return; }
        Reply(p, _panel?.IsOpen(p) == true ? "当前是鼠标面板；!ze_ui_chat 切换数字聊天菜单，!0 关闭。" : "当前功能菜单已关闭；!ze 重新打开。");
    }
    private void InitializeAccess()
    {
        _accessDisposed=false;Permissions=new(this);Permissions.Initialize();
        _panel=new(this);_panel.Initialize();
        _radioPanel = new(this);
        AddCommandListener("say",OnFullWidthChat,HookMode.Pre);AddCommandListener("say_team",OnFullWidthChat,HookMode.Pre);
        AddCommandListener("noclip",OnLegacyNoclip,HookMode.Pre);AddCommandListener("god",OnLegacyGod,HookMode.Pre);
        RegisterListener<Listeners.OnClientDisconnect>(OnAccessDisconnect);
        AddZeCommand("css_ze_permissions_reload","服主：重读自定义权限等级",(p,_)=>{
            if(Permissions.Reload(out var error)){_admin?.RecheckPermissions();_panel?.RefreshPermissions();_radioPanel?.CloseAll();Reply(p,"权限已重载；当前辅助与打开的菜单已按新权限复核。");}
            else Reply(p,"权限未更改，保留上一份有效配置："+error);
        });
        AddZeCommand("css_ze_ui","打开或关闭当前功能菜单",(p,c)=>{
            if(!IsRealPlayer(p)){Reply(p,"需要游戏内玩家。");return;}
            if(MenuIsOpen(p!)){CloseMenus(p!);return;}
            // The main handler ignores arguments. Call its guarded callback with the SAME caller,
            // not a client-command buffer: a subsequent close can cancel the Radio open immediately.
            if(_zeCommands.TryGetValue("css_ze",out var main)) main.Guarded(p,c);
        });
        AddZeCommand("css_ze_ui_close","关闭全部功能菜单并释放鼠标",(p,_)=>{if(IsRealPlayer(p))CloseMenus(p!);});
        AddZeCommand("css_ze_commands","按本人权限显示可用指令",ShowCommandList);
        AddZeCommand("css_ze_select", "无线电选择当前功能菜单的编号", (player, command) =>
        {
            if (!IsRealPlayer(player) || command.ArgCount != 2 || !int.TryParse(command.GetArg(1), out int key) || key < 0 || key > 9) return;
            SelectMenuKey(player!, key);
        });
        AddZeCommand("css_ze_ui_status","图形面板资源与本人的会话诊断",(p,_)=>Reply(p,Config.MenuUiMode.Equals("radio",StringComparison.OrdinalIgnoreCase) ? _radioPanel?.DiagnosticsFor(p)??"UI服务尚未初始化" : _panel?.DiagnosticsFor(p)??"UI服务尚未初始化"));
        AddZeCommand("css_ze_binds","显示现有快捷键和一次性绑定方法",(p,_)=>{
            Reply(p,"快捷键：ALT穿墙；F9菜单；F11无敌；INS存点；HOME回点；END删点。FYS的F5/F7/F8原绑定保留。");
            Reply(p,"本机新增F9菜单、F11无敌；其他电脑在客户端控制台一次执行 exec ze_practice_keys；每次操作仍校验服务端权限。");
        });
    }
    private void ShowCommandList(CCSPlayerController? p,CommandInfo c)
    {
        var search=c.ArgCount>1?c.GetArg(1):"";
        if (p != null && search.Length == 0) { ExecuteOwnCommand(p, "css_ze_public"); return; }
        var rows=_zeCommands.Where(x=>!x.Key.StartsWith("ms_",StringComparison.Ordinal)&&Permissions.Allows(p,ZeCommandText.Canonical(x.Key))&&(search.Length==0||x.Key.Contains(search,StringComparison.OrdinalIgnoreCase)||x.Value.Description.Contains(search,StringComparison.OrdinalIgnoreCase))).OrderBy(x=>x.Key).ToArray();
        if(p==null){foreach(var row in rows)Reply(null,row.Key+" — "+row.Value.Description);return;}
        var menu=new ChatMenu("可用指令（点击查看用法）");
        foreach(var row in rows){var item=row;menu.AddMenuOption(item.Key.Replace("css_","!")+" · "+item.Value.Description,(who,_)=>Reply(who,item.Key+"："+item.Value.Description));}
        OpenMenu(p,menu);
    }
    private HookResult OnFullWidthChat(CCSPlayerController? p,CommandInfo c)
    {
        if(!IsRealPlayer(p)||c.ArgCount<2)return HookResult.Continue;
        // Chat-box text is one quoted argument; Workshop radio's `say !ze_select 8`
        // is unquoted and has several. Do not silently drop its selected key/value.
        string text=string.Join(" ", Enumerable.Range(1,c.ArgCount-1).Select(c.GetArg)).Trim();
        // Remote Workshop clients need no local key binding or global HUD script for these keys.
        if (ZeCommandText.TryParseMenuKeyChat(text, out int chatKey) && (chatKey is 0 or 9 || MenuIsOpen(p!)))
        { SelectMenuKey(p!, chatKey); return HookResult.Stop; }
        // Workshop radio entries may only invoke client-known commands. They use say !ze_...
        // instead of css_... (which the normal Workshop filter rejects as nonexistent).
        // Own this bridge explicitly: CS2Fixes' chat hook can print ASCII !commands before
        // CSS's automatic chat dispatcher sees them. Stop known entries to avoid double actions.
        bool fullWidth = text.StartsWith('！');
        if(!fullWidth && !text.StartsWith('!') && !text.StartsWith('/'))return HookResult.Continue;
        if(!ZeCommandText.TryParseBody(text[1..],out var name,out var args,out var key))
        { if(!fullWidth)return HookResult.Continue; Reply(p,"指令格式无效，请使用 ！命令 参数（单条命令）。");return HookResult.Stop; }
        if(key!=0){SelectMenuKey(p!,key);return HookResult.Stop;}
        if(_zeCommands.ContainsKey(name)){ExecuteOwnCommand(p!,name+(args.Length>0?" "+args:""));return HookResult.Stop;}
        if(!fullWidth)return HookResult.Continue;
        // Native CS2Fixes keeps its own permission checks and original caller identity.
        var native=name[4..];
        if(NativeChatCommands.Contains(native)){p!.ExecuteClientCommandFromServer("c_"+native+(args.Length>0?" "+args:""));return HookResult.Stop;}
        Reply(p,"未识别的全角指令；!ze_commands 查看本服指令。");return HookResult.Stop;
    }
    private static readonly HashSet<string> NativeChatCommands=new("add_dc adminve ban beacons bring bw cancelvote cvarwhitelist_reload debugclientlist disablertv discordbot dispatch_particle eban emitsound enablertv endround entfire entfirecontroller entfirepawn etransfer eunban ew_dump ew_reload extend extendsleft find fl gag getpos getpref getstats give glows goto health help hide hsay http hud hudcolor hudpos hudsize infect info kick leadercolor leaderhelp leaders listdc map mapcooldowns maplist money motherzombies mute myhandle myuid mz nextmap noclip nominate nomlist noshake particle particle_kv pm pullprefs pushprefs rcon reload_discord_bots reload_map_list removeleader resign revive revote rtv say setcollisiongroup setinteraction setkills setnextmap setpos setpref setsolidtype setteam showteam slap slay spec status stopsound strip tdfind tdrank test_target timeleft toggledecals tp tracers uitest unban ungag unmute unrtv unve vc ve vl voicechat who zclass zsounds ztele".Split(' '),StringComparer.OrdinalIgnoreCase);
    private HookResult OnLegacyNoclip(CCSPlayerController? p,CommandInfo c)=>LegacyToggle(p,"css_ze_noclip");
    private HookResult OnLegacyGod(CCSPlayerController? p,CommandInfo c)=>LegacyToggle(p,"css_ze_god");
    private HookResult LegacyToggle(CCSPlayerController? p,string command)
    {
        if(!IsRealPlayer(p))return HookResult.Continue;
        var now=Environment.TickCount64;
        if(_bridgeRate.TryGetValue(p!.Slot,out var last)&&last.UserId==p.UserId&&now-last.At<150)return HookResult.Stop;
        _bridgeRate[p.Slot]=(p.UserId,now);ExecuteOwnCommand(p,command);return HookResult.Stop;
    }
    private void OnAccessDisconnect(int slot)=>_bridgeRate.Remove(slot);
    private void DisposeAccess()
    {
        _accessDisposed=true;_radioPanel?.Dispose();_panel?.Dispose();Permissions?.Dispose();
        RemoveCommandListener("say",OnFullWidthChat,HookMode.Pre);RemoveCommandListener("say_team",OnFullWidthChat,HookMode.Pre);
        RemoveCommandListener("noclip",OnLegacyNoclip,HookMode.Pre);RemoveCommandListener("god",OnLegacyGod,HookMode.Pre);
        RemoveListener<Listeners.OnClientDisconnect>(OnAccessDisconnect);
        foreach(var entry in _zeCommands.ToArray())RemoveCommand(entry.Key,entry.Value.Guarded);
        _zeCommands.Clear();_bridgeRate.Clear();
    }
}
