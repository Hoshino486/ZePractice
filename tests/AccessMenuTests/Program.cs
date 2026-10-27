using System.Collections;
using System.Reflection;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using ZePractice;

int passed=0;
void Check(bool ok,string label){if(!ok)throw new Exception("FAIL "+label);passed++;Console.WriteLine("PASS "+label);}
var flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
object Field(object o,string name)=>o.GetType().GetField(name,flags)!.GetValue(o)!;
object Session(object service,CCSPlayerController p)=>((IDictionary)Field(service,"_sessions"))[p.Slot]!;
CCSCustomHudLayout Hud(object service,CCSPlayerController p)=>(CCSCustomHudLayout)Field(Session(service,p),"Layout");
void Unthrottle(RadioPanelService radio,CCSPlayerController p)=>Session(radio,p).GetType().GetField("LastKey",flags)!.SetValue(Session(radio,p),Environment.TickCount64-500);

int actions=0;
var menu=new PersistentChatMenu("远端测试菜单");
for(int i=0;i<16;i++) menu.AddMenuOption("动作 "+i,(_,_)=>actions++);
var p=new CCSPlayerController{Slot=2,UserId=22,IpAddress="198.51.100.4:27005"};
var other=new CCSPlayerController{Slot=3,UserId=23,IpAddress="[2001:db8::5]:27005"};
Utilities.Players.AddRange([p,other]);
using var plugin=new ZePracticePlugin();plugin.Start(menu);plugin.Wire(p);plugin.Wire(other);
var radio=plugin.Radio;
var precache=new ResourceManifest();
radio.GetType().GetMethod("Precache",flags)!.Invoke(radio,[precache]);
Check(precache.Resources.SequenceEqual(new[]{"panorama/layout/custom_game/zepractice/radio.vxml","panorama/styles/custom_game/zepractice/radio.vcss"}),"precache uses logical names so engine does not request _c_c");
Check(RadioPanelService.Layout.EndsWith(".vxml_c")&&RadioPanelService.Required[1].EndsWith(".vcss_c"),"VPK validation and entity layout retain compiled paths");
CommanderResources.Result=new(false,"missing-fixture","fixture");
var missingPrecache=new ResourceManifest();
radio.GetType().GetMethod("Precache",flags)!.Invoke(radio,[missingPrecache]);
Check(missingPrecache.Resources.Count==0,"missing Workshop resources are not precached");
CommanderResources.Result=new(true,"verified-fixture","fixture");radio.RefreshResources();
void Open(CCSPlayerController? who=null){plugin.Command(who??p,"css_ze_ui");Server.Drain();}
void Close(CCSPlayerController? who=null){plugin.Command(who??p,"css_ze_ui_close");Server.Drain();}

// Reproduce production's prior false flag with a real external IP, not the old loopback stub.
plugin.Config.PanoramaAllowRemoteClients=false;Open();
Check(!radio.IsOpen(p)&&MenuManager.GetActiveMenu(p)!=null,"remote-disabled config falls back to an actionable chat menu");
Check(radio.Diagnostics().Contains("allowRemote=False")&&radio.Diagnostics().Contains("fallbacks=1"),"diagnostics expose remote restriction and actual fallback");
int before=actions;plugin.Chat(p,"!1");Server.Drain();
Check(actions==before+1&&MenuManager.GetActiveMenu(p)!=null,"chat fallback works even when global mode is radio and remains open");
plugin.Command(p,"css_ze_select","0");Check(MenuManager.GetActiveMenu(p)==null,"radio key command closes chat fallback instead of ignoring it");
Open();plugin.Command(p,"css_ze_ui");Server.Drain();Check(!radio.IsOpen(p)&&MenuManager.GetActiveMenu(p)==null,"toggle closes the fallback rather than reopening it");

plugin.Config.PanoramaAllowRemoteClients=true;
foreach(var endpoint in new[]{"198.51.100.4:27005","[2001:db8::4]:27005","[::ffff:198.51.100.4]:27005","127.0.0.1:27005","[::1]:27005"})
{
    p.IpAddress=endpoint;Open();Check(radio.IsOpen(p)&&MenuManager.GetActiveMenu(p)==null,"verified assets open right-side menu: "+endpoint);Close();
}
p.IpAddress="198.51.100.4:27005";
Check(plugin.Chat(p,"!ze")==HookResult.Stop,"ASCII plugin chat is explicitly bridged, not printed and lost to other hooks");Server.Drain();
Check(radio.IsOpen(p),"ASCII chat opens the actual Radio service");
Check(plugin.RadioChat(p,"!ze_select","8")==HookResult.Stop,"unquoted Workshop chat bridge claims its entire command");Server.Drain();
Check((int)Field(Session(radio,p),"Page")==1,"unquoted radio key argument survives say parsing");
Unthrottle(radio,p);int radioActions=actions;plugin.RadioChat(p,"!ze_select","1");Server.Drain();
Check(actions==radioActions+1&&radio.IsOpen(p)&&(int)Field(Session(radio,p),"Page")==1,"radio transport invokes exactly once, keeps menu and page");
plugin.RadioChat(p,"!ze_select","9");Server.Drain();Check(!radio.IsOpenOrPending(p),"unquoted close key is not discarded");
plugin.Chat(p,"/ze");Server.Drain();Check(radio.IsOpen(p),"silent slash plugin chat uses the same bridge");Close();
Check(plugin.Chat(p,"!another_plugin")==HookResult.Continue,"unrelated plugin chat is not consumed");
Check(plugin.Chat(p,"hello")==HookResult.Continue,"normal chat is untouched");
plugin.Permissions.Deny.Add("css_ze");plugin.Chat(p,"!ze");Server.Drain();
Check(!radio.IsOpenOrPending(p),"chat transport retains caller permissions");plugin.Permissions.Deny.Clear();
plugin.Command(p,"css_ze_ui");Check(radio.IsOpenOrPending(p)&&!radio.IsOpen(p),"toggle tracks not-yet-rendered menu request");
plugin.Command(p,"css_ze_ui");Server.Drain();Check(!radio.IsOpenOrPending(p),"double toggle before next frame cancels open");
plugin.Command(p,"css_ze_ui");plugin.Command(p,"css_ze_ui_close");Server.Drain();Check(!radio.IsOpenOrPending(p),"explicit close cancels queued toggle");
Open();var initial=Hud(radio,p);plugin.Command(p,"css_ze_ui");Server.Drain();
Check(!radio.IsOpenOrPending(p)&&!initial.IsValid&&initial.Removes==1,"toggle retires existing right-side entity exactly once");

Open();before=actions;plugin.Chat(p,"!1");Server.Drain();
Check(actions==before+1&&radio.IsOpen(p),"ASCII chat number selects actual Radio and keeps it open");
Unthrottle(radio,p);plugin.Chat(p,"！2");Server.Drain();Check(actions==before+2,"full-width chat number routes to Radio, not CSS chat renderer");
Unthrottle(radio,p);plugin.Chat(p,"!8");Server.Drain();Check((int)Field(Session(radio,p),"Page")==1,"chat next-page key uses Radio paging");
Unthrottle(radio,p);plugin.Chat(p,"!1");Server.Drain();Check((int)Field(Session(radio,p),"Page")==1&&actions==before+3,"radio action retains second page");
foreach(var closeText in new[]{"!0","！0","!9","！9"})
{
    plugin.Chat(p,closeText);Server.Drain();Check(!radio.IsOpenOrPending(p),"independent remote close spelling "+closeText);Open();
}
Unthrottle(radio,p);before=actions;plugin.Chat(p,"!1");plugin.Chat(p,"!0");Server.Drain();Check(actions==before&&!radio.IsOpen(p),"chat close cancels queued selection and cannot be undone");

// Escape must work after ACL changes without allowing normal menu actions.
foreach(var closeMethod in new[]{"toggle","select0","select9","close","fullwidth","chat"})
{
    Open();plugin.Permissions.Deny.UnionWith(["css_ze","css_ze_ui","css_ze_ui_close","css_ze_ui_chat"]);
    before=actions;plugin.Command(p,"css_ze_select","1");Server.Drain();Check(actions==before,"revoked permissions block action before "+closeMethod);
    switch(closeMethod)
    {
        case "toggle": plugin.Command(p,"css_ze_ui"); break;
        case "select0": plugin.Command(p,"css_ze_select","0"); break;
        case "select9": plugin.Command(p,"css_ze_select","9"); break;
        case "close": plugin.Command(p,"css_ze_ui_close"); break;
        case "fullwidth": plugin.Chat(p,"！ze_select 0");break;
        case "chat": plugin.Command(p,"css_ze_ui_chat");break;
    }
    Server.Drain();Check(!radio.IsOpenOrPending(p)&&MenuManager.GetActiveMenu(p)==null,"escape bypass is close-only after ACL revocation: "+closeMethod);
    plugin.Command(p,"css_ze_ui");Server.Drain();Check(!radio.IsOpenOrPending(p)&&MenuManager.GetActiveMenu(p)==null,"revoked toggle cannot reopen menu");
    plugin.Permissions.Deny.Clear();plugin.Panel.SetChatPreference(p,false);
}

Open();var old=Hud(radio,p);plugin.Command(p,"css_ze_ui_chat");Server.Drain();
Check(!radio.IsOpen(p)&&!old.IsValid&&MenuManager.GetActiveMenu(p)?.Menu==menu,"chat preference transfers current Radio menu and removes old entity");
Close();Open();Check(MenuManager.GetActiveMenu(p)!=null&&!radio.IsOpen(p),"personal chat preference survives close/reopen in radio server mode");
plugin.Command(p,"css_ze_ui_ready");Server.Drain();Check(radio.IsOpen(p)&&MenuManager.GetActiveMenu(p)==null,"ready switches back from chat to verified Radio");
Close();plugin.Command(p,"css_ze_ui");plugin.Command(p,"css_ze_ui_chat");Server.Drain();
Check(!radio.IsOpenOrPending(p)&&MenuManager.GetActiveMenu(p)?.Menu==menu,"chat preference cancels and transfers a pending Radio open");
plugin.Command(p,"css_ze_ui_ready");Server.Drain();Open(other);
var ownHud=Hud(radio,p);var otherHud=Hud(radio,other);
plugin.Command(p,"css_ze_ui_status");
Check(plugin.Messages.Last().Contains("slot="+p.Slot+",")&&!plugin.Messages.Last().Contains("slot="+other.Slot+","),"player diagnostics do not expose another player's menu session");
var ownInfo=new CCheckTransmitInfo();var otherInfo=new CCheckTransmitInfo();var noneInfo=new CCheckTransmitInfo();
foreach(var info in new[]{ownInfo,otherInfo,noneInfo})info.TransmitEntities.UnionWith([ownHud,otherHud]);
((Listeners.CheckTransmit)plugin.Listeners[typeof(Listeners.CheckTransmit)])(new(){(ownInfo,p),(otherInfo,other),(noneInfo,null)});
Check(ownInfo.TransmitEntities.SetEquals([ownHud])&&otherInfo.TransmitEntities.SetEquals([otherHud]),"CheckTransmit sends only the receiver's menu");
Check(noneInfo.TransmitEntities.Count==0,"unknown receiver gets neither player's menu");
ownInfo.TransmitEntities.Clear();((Listeners.CheckTransmit)plugin.Listeners[typeof(Listeners.CheckTransmit)])(new(){(ownInfo,p)});
Check(ownInfo.TransmitEntities.Contains(ownHud),"owner HUD is forced into transmit set even outside PVS");
Close();Check(radio.IsOpen(other)&&otherHud.IsValid,"one player's close does not hide another player's menu");
Open();plugin.Command(p,"css_ze_ui_chat");plugin.Disconnect(p.Slot);MenuManager.CloseActiveMenu(p);
Check(!plugin.Panel.PrefersChat(p),"disconnect clears personal chat preference even without a Panorama session");
var recycled=new CCSPlayerController{Slot=p.Slot,UserId=122,IpAddress=p.IpAddress};plugin.Wire(recycled);Open(recycled);
radio.Select(p,9);Check(radio.IsOpen(recycled),"stale controller close cannot retire replacement connection");
Close(recycled);
plugin.Command(p,"css_ze_ui");p.UserId++;Server.Drain();Check(!radio.IsOpenOrPending(p),"reconnected slot discards pending open");p.UserId--;
plugin.Command(p,"css_ze_ui");plugin.MapEnd();Server.Drain();Check(!radio.IsOpenOrPending(p)&&!radio.IsOpen(other),"map end cancels both current and pending menus");

foreach(var fault in new[]{"assets-disabled","missing-vpk","crc-mismatch","create-entity"})
{
    plugin.Config.MenuAssetsReady=fault!="assets-disabled";
    CommanderResources.Result=new(fault is not ("missing-vpk" or "crc-mismatch"),fault,"fixture");radio.RefreshResources();
    Utilities.FailCreate=fault=="create-entity";Open();
    Check(!radio.IsOpen(p)&&MenuManager.GetActiveMenu(p)?.Menu==menu,"actionable fallback on "+fault);
    before=actions;plugin.Chat(p,"!2");Server.Drain();Check(actions==before+1,"fallback callback still usable on "+fault);
    Close();Utilities.FailCreate=false;
}
plugin.Config.MenuAssetsReady=true;CommanderResources.Result=new(true,"verified-fixture","fixture");radio.RefreshResources();
Open();Hud(radio,p).Remove();radio.GetType().GetMethod("Maintain",flags)!.Invoke(radio,null);
Check(!radio.IsOpen(p)&&MenuManager.GetActiveMenu(p)?.Menu==menu,"unexpectedly removed HUD recovers as actionable chat instead of blank panel");Close();
var failing=new PersistentChatMenu("failed action");failing.AddMenuOption("fail",(_,_)=>throw new InvalidOperationException("fixture"));
plugin.OpenMenu(p,failing);Server.Drain();plugin.Chat(p,"!1");Server.Drain();
Check(!radio.IsOpen(p)&&MenuManager.GetActiveMenu(p)?.Menu==failing,"renderer or action exception returns to chat menu");Close();
var closedFailure=new PersistentChatMenu("close then fail");closedFailure.AddMenuOption("close",(who,_)=>{plugin.CloseMenus(who);throw new InvalidOperationException("fixture");});
plugin.OpenMenu(p,closedFailure);Server.Drain();plugin.Chat(p,"!1");Server.Drain();
Check(!radio.IsOpen(p)&&MenuManager.GetActiveMenu(p)==null,"exception after explicit close does not resurrect fallback");

// Actual Panorama service plus central close routing: all routes release its input capture.
string root=Path.Combine(Path.GetTempPath(),"ze-access-menu-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root,"panorama/layout/custom_game/zepractice"));Directory.CreateDirectory(Path.Combine(root,"panorama/styles/custom_game/zepractice"));
File.WriteAllText(Path.Combine(root,"panorama/layout/custom_game/zepractice/main.vxml_c"),"");
File.WriteAllText(Path.Combine(root,"panorama/styles/custom_game/zepractice/main.vcss_c"),"");Server.GameDirectory=root;
try
{
    plugin.Config.MenuUiMode="panorama";p.IpAddress="127.0.0.1:27005";
    foreach(var key in new[]{"toggle","select0","select9","chat0","close"})
    {
        Open();var hud=Hud(plugin.Panel,p);Check(hud.IsInputCaptureEnabled(p),"Panorama captures visible local fixture for "+key);
        if(key=="toggle")plugin.Command(p,"css_ze_ui");
        else if(key.StartsWith("select"))plugin.Command(p,"css_ze_select",key[^1].ToString());
        else if(key=="chat0")plugin.Chat(p,"!0");else Close();Server.Drain();
        Check(!plugin.Panel.IsOpen(p)&&!hud.IsInputCaptureEnabled(p)&&!hud.IsValid,"central close releases Panorama capture: "+key);
    }
    plugin.Config.MenuUiMode="chat";Open();before=actions;plugin.Chat(p,"！1");
    Check(actions==before+1&&MenuManager.GetActiveMenu(p)!=null,"chat mode routes number without graphical resource dependency");Close();
}
finally {Directory.Delete(root,true);Server.GameDirectory="";}

foreach(var prefix in new[]{"!","！"})foreach(int n in Enumerable.Range(0,10))
    Check(ZeCommandText.TryParseMenuKeyChat(prefix+n,out int parsed)&&parsed==n,"strict chat digit "+prefix+n);
foreach(var invalid in new[]{"!10","!０","!0 quit","!0;quit","！0\nquit","hello","0","!","！","/1"})
    Check(!ZeCommandText.TryParseMenuKeyChat(invalid,out _),"reject non-single numeric chat: "+invalid.Replace('\n',' '));
Check(!ZeCommandText.TryParseBody("0",out _,out _,out _),"old zero sentinel in command parser is not changed");
Check(ZeCommandText.Canonical("css_ze_ui_status")=="css_ze","ordinary menu users can inspect their own UI resources/status");
plugin.Config.MenuUiMode="radio";plugin.Command(p,"css_ze_ui");plugin.Dispose();Server.Drain();
Check(!radio.IsOpenOrPending(p)&&plugin.Registered.Count==0&&plugin.Listeners.Count==0,"unload cancels queued menu and unregisters all callbacks");
Console.WriteLine($"PASS {passed} access-menu tests (actual production Access/Radio/Panorama sources; engine stubs, not visual acceptance)");
