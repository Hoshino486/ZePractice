using System.Collections;
using System.Reflection;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using ZePractice;

internal static class PersistenceCases
{
    private static readonly BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance;
    private static object Get(object o,string name)=>o.GetType().GetField(name,Flags)!.GetValue(o)!;
    private static void Set(object o,string name,object value)=>o.GetType().GetField(name,Flags)!.SetValue(o,value);
    private static void Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Flags)!.Invoke(o,args);
    private static object Session(object service,CCSPlayerController p)=>((IDictionary)Get(service,"_sessions"))[p.Slot]!;
    private static CCSCustomHudLayout Hud(object session)=>(CCSCustomHudLayout)Get(session,"Layout");
    public static void Run(Action<bool,string> check)
    {
        var p=new CCSPlayerController{Slot=10,UserId=90};
        var other=new CCSPlayerController{Slot=11,UserId=91};
        Utilities.Players.AddRange([p,other]);
        var plugin=new ZePracticePlugin();
        var publicMenus=new PublicGameplayService(plugin);
        using var radio=new RadioPanelService(plugin);
        plugin.OpenAdapter=radio.Open;
        // Simulate the actual command bridge's asynchronous state update, not an immediate toggle.
        plugin.OnCommand=(_,cmd)=>Server.NextFrame(()=>
        {
            if(cmd=="css_ze_speed")plugin.Movement=!plugin.Movement;
            if(cmd=="css_ze_diag")plugin.Diagnostics=!plugin.Diagnostics;
            if(cmd=="css_thirdperson")plugin.ThirdPerson=!plugin.ThirdPerson;
        });
        void Key(int key,bool drain=true)
        {
            if(radio.IsOpen(p))Set(Session(radio,p),"LastKey",Environment.TickCount64-500);
            radio.Select(p,key);
            if(drain)Server.Drain();
        }
        void Open(){publicMenus.Open(p);Server.Drain();}
        Open();var original=Session(radio,p);var hud=Hud(original);
        check(((ChatMenu)Get(original,"Menu")).PostSelectAction==PostSelectAction.Nothing,"public action has explicit keep-open policy");
        Key(8);check((int)Get(original,"Page")==1,"actual radio service navigates to page 2");
        Key(3);check(ReferenceEquals(Session(radio,p),original)&&hud.IsValid&&hud.Removes==0,"read-only command keeps original radio entity");
        check((int)Get(original,"Page")==1&&plugin.Commands.Last()=="css_ze_script_status","read-only command preserves second page");
        Key(1);check(!plugin.Movement&&hud.Text["radio_line_0"].EndsWith("[关]"),"deferred movement toggle refreshes label in place");
        check(ReferenceEquals(Session(radio,p),original)&&(int)Get(original,"Page")==1,"toggle neither recreates window nor returns home");
        Key(2);check(!plugin.Diagnostics&&hud.Text["radio_line_1"].EndsWith("[关]"),"diagnostics toggle refreshes second row");
        int before=plugin.Commands.Count;Key(4);var teams=Session(radio,p);var teamsHud=Hud(teams);
        check(plugin.Commands.Count==before&&!hud.IsValid&&teamsHud.Text["radio_title"].Contains("选择阵营"),"Bot team child navigation wins over parent refresh without creating a Bot");
        Key(1);Key(2);Key(1);
        check(plugin.Commands.Skip(before).SequenceEqual(["css_ze_bot_add t","css_ze_bot_add ct","css_ze_bot_add t"])&&ReferenceEquals(Session(radio,p),teams)&&teamsHud.IsValid,"repeated mixed-team Bot actions keep submenu without duplicate callbacks");
        Key(5);Key(8);original=Session(radio,p);hud=Hud(original);
        plugin.Movement=true;Call(radio,"Maintain");
        check(hud.Text["radio_line_0"].EndsWith("[开]"),"keyboard or chat state changes refresh an already open radio menu");
        Set(original,"LastKey",Environment.TickCount64-600000);Call(radio,"Maintain");
        check(radio.IsOpen(p)&&ReferenceEquals(Session(radio,p),original),"long idle does not close radio menu");
        check(original.GetType().GetField("Expires",Flags)==null,"old 115-second auto-close deadline removed");
        Key(5);var child=Session(radio,p);var childHud=Hud(child);
        check(!ReferenceEquals(child,original)&&!hud.IsValid&&childHud.Text["radio_title"].Contains("Bot"),"child navigation wins over parent refresh");
        Key(1);check(ReferenceEquals(Session(radio,p),child)&&!plugin.Commands.Contains("css_ze_ui_close"),"custom numeric entry does not close its submenu");
        publicMenus.Chat(p,"23456");Server.Drain();
        check(plugin.Commands.Last()=="css_ze_bot_hp 23456"&&ReferenceEquals(Session(radio,p),child),"accepted custom value keeps the submenu");
        Key(1);publicMenus.Chat(p,"取消");
        check(ReferenceEquals(Session(radio,p),child),"cancel numeric entry leaves current page alone");
        Key(2);check(plugin.Commands.Last()=="css_ze_bot_hp 100"&&ReferenceEquals(Session(radio,p),child),"preset value can be reused without reopening");
        Key(8);Key(1);check(Hud(Session(radio,p)).Text["radio_title"].Contains("公开"),"explicit Back still returns to public menu");
        Key(8);Key(1,false);Server.Frame();radio.Close(p.Slot);Server.Drain();
        check(!radio.IsOpen(p),"manual close wins over delayed command-label refresh");
        Open();before=plugin.Commands.Count;Key(1,false);Key(0,false);Server.Drain();
        check(!radio.IsOpen(p)&&plugin.Commands.Count==before,"zero cancels a selection still pending next frame");
        publicMenus.Open(p);radio.Select(p,9);Server.Drain();
        check(!radio.IsOpen(p),"nine cancels even a pending menu-open request");
        Open();Key(8);Key(5,false);Server.Frame();radio.Select(p,9);Server.Drain();
        check(!radio.IsOpen(p),"close cancels queued child-menu navigation");
        Open();publicMenus.Open(other);Server.Drain();var otherSession=Session(radio,other);Key(9);
        check(!radio.IsOpen(p)&&ReferenceEquals(Session(radio,other),otherSession),"one player's close does not affect another menu");
        Open();p.UserId++;Call(radio,"Maintain");check(!radio.IsOpen(p),"slot reused by another userid retires old menu");p.UserId--;
        Open();uint oldHandle=p.EntityHandle.Raw;p.EntityHandle.Raw++;Call(radio,"Maintain");
        check(!radio.IsOpen(p),"new controller serial cannot inherit an old window");p.EntityHandle.Raw=oldHandle;
        Open();plugin.Permissions.Deny.Add("css_ze");Call(radio,"Maintain");check(!radio.IsOpen(p),"permission revocation still cleans up menu");plugin.Permissions.Deny.Clear();
        Open();publicMenus.Open(p);radio.CloseAll();Server.Drain();
        check(!radio.IsOpen(p)&&!radio.IsOpen(other),"map end clears sessions and pending opens");
        var closeFromCallback=new PersistentChatMenu("close fixture");closeFromCallback.AddMenuOption("close",(who,_)=>radio.Close(who.Slot));
        radio.Open(p,closeFromCallback);Server.Drain();Key(1);
        check(!radio.IsOpen(p),"explicit callback close is not resurrected by Nothing policy");
        var replaced=new PersistentChatMenu("replacement");replaced.AddMenuOption("ok",(_,_)=>{});
        radio.Open(p,closeFromCallback);radio.Open(p,replaced);Server.Drain();
        check(ReferenceEquals(Get(Session(radio,p),"Menu"),replaced),"newest pending open wins");
        Open();Key(8);((ChatMenu)Get(Session(radio,p),"Menu")).MenuOptions[6].Disabled=true;before=plugin.Commands.Count;Key(1);
        check(plugin.Commands.Count==before&&(int)Get(Session(radio,p),"Page")==1,"disabled item never executes or closes");
        Key(8);((Listeners.OnClientDisconnect)plugin.Listeners[typeof(Listeners.OnClientDisconnect)])(p.Slot);
        check(!radio.IsOpen(p),"disconnect still cleans up persistent session");
        Open();publicMenus.Open(p);radio.Dispose();Server.Drain();
        check(!radio.IsOpen(p)&&plugin.Logger.Errors.Count==0,"unload cancels queued work without renderer errors");

        // The fallback also refreshes the same menu instance, without forcing page zero or resurrecting closes.
        plugin.OpenAdapter=MenuManager.OpenChatMenu;
        publicMenus.Open(p);var chat=MenuManager.GetActiveMenu(p)!;chat.Page=1;
        var menu=(PersistentChatMenu)chat.Menu;var diag=menu.MenuOptions[7];bool prior=plugin.Diagnostics;
        diag.OnSelect(p,diag);Server.Drain();
        check(plugin.Diagnostics!=prior&&diag.Text.EndsWith(plugin.Diagnostics?"[开]":"[关]"),"chat fallback updates dynamic label after command");
        check(ReferenceEquals(MenuManager.GetActiveMenu(p),chat)&&chat.Page==1&&chat.Displays==2,"chat fallback refresh retains instance and page");
        diag.OnSelect(p,diag);MenuManager.CloseActiveMenu(p);Server.Drain();
        check(MenuManager.GetActiveMenu(p)==null,"chat close wins over delayed label refresh");
        publicMenus.Open(p);chat=MenuManager.GetActiveMenu(p)!;chat.Menu.MenuOptions[7].OnSelect(p,chat.Menu.MenuOptions[7]);
        var next=new PersistentChatMenu("new submenu");MenuManager.OpenChatMenu(p,next);Server.Drain();
        check(ReferenceEquals(MenuManager.GetActiveMenu(p)!.Menu,next),"chat child menu is not overwritten by stale parent refresh");
        MenuManager.CloseActiveMenu(p);

        // Exercise the actual Panorama adapter with fake native entities, including mouse release.
        var temp=Path.Combine(Path.GetTempPath(),"ze-menu-tests-"+Guid.NewGuid().ToString("N"));
        var layoutFile=Path.Combine(temp,"panorama/layout/custom_game/zepractice/main.vxml_c");
        var styleFile=Path.Combine(temp,"panorama/styles/custom_game/zepractice/main.vcss_c");
        Directory.CreateDirectory(Path.GetDirectoryName(layoutFile)!);Directory.CreateDirectory(Path.GetDirectoryName(styleFile)!);
        File.WriteAllText(layoutFile,"");File.WriteAllText(styleFile,"");Server.GameDirectory=temp;
        try
        {
            var pp=new ZePracticePlugin();pp.Config.MenuUiMode="panorama";
            using var panel=new ZePanelService(pp);panel.Initialize();
            var menus=new PublicGameplayService(pp);pp.OpenAdapter=panel.Open;
            pp.OnCommand=(_,cmd)=>Server.NextFrame(()=>{if(cmd=="css_ze_diag")pp.Diagnostics=!pp.Diagnostics;});
            void Click(string id)
            {
                var s=Session(panel,p);Set(s,"LastClick",DateTimeOffset.UtcNow-TimeSpan.FromSeconds(1));
                Call(panel,"OnClick",p,Hud(s),id);Server.Drain();Call(panel,"Maintenance");
            }
            menus.Open(p);var s=Session(panel,p);var initial=Hud(s);Click("next");s=Session(panel,p);var currentHud=Hud(s);
            check((int)Get(s,"Page")==1&&!initial.IsValid,"Panorama navigation retires old entity serial");
            Click("option_2");check(ReferenceEquals(Session(panel,p),s)&&currentHud.IsValid,"Panorama action keeps page and entity");
            check((DateTimeOffset)Get(s,"Expires")==DateTimeOffset.MaxValue,"confirmed Panorama has no idle auto-close");
            check(currentHud.IsInputCaptureEnabled(p),"Panorama keeps input until explicitly closed");
            Click("previous");s=Session(panel,p);currentHud=Hud(s);Click("option_7");
            check(!pp.Diagnostics&&currentHud.Text["card_text_7"].EndsWith("[关]"),"Panorama dynamic label refreshes after deferred command");
            check(ReferenceEquals(Session(panel,p),s),"Panorama toggle does not recreate layout");
            Click("next");Click("option_0");var submenu=Session(panel,p);
            check(Hud(submenu).Text["heading"].Contains("Bot"),"Panorama child is not overwritten");
            Click("option_0");check(ReferenceEquals(Session(panel,p),submenu),"Panorama numeric entry keeps window");
            Click("close");check(!panel.IsOpen(p)&&!Hud(submenu).IsInputCaptureEnabled(p)&&!Hud(submenu).IsValid,"Panorama explicit close releases capture and destroys entity");
            menus.Open(p);s=Session(panel,p);var staleLayout=Hud(s);Click("next");s=Session(panel,p);int count=pp.Commands.Count;
            Call(panel,"OnClick",p,staleLayout,"option_0");
            check(pp.Commands.Count==count&&ReferenceEquals(Session(panel,p),s),"old page click cannot hit a new page");
            var explicitClose=new PersistentChatMenu("close fixture");explicitClose.AddMenuOption("close",(who,_)=>panel.Close(who));panel.Open(p,explicitClose);Click("option_0");
            check(!panel.IsOpen(p),"Panorama callback close remains closed");
            // Preserve the initial resource-visibility timeout: never trap a client with an unconfirmed HUD.
            panel.OnMapStart("fixture");menus.Open(p);s=Session(panel,p);Set(s,"Expires",DateTimeOffset.UtcNow-TimeSpan.FromSeconds(1));Call(panel,"Maintenance");
            check(!panel.IsOpen(p)&&MenuManager.GetActiveMenu(p)!=null,"unconfirmed invisible HUD still falls back and releases input");
            check(pp.Logger.Errors.Count==0,"Panorama persistence paths have no renderer exceptions");
            MenuManager.CloseActiveMenu(p);
        }
        finally { Directory.Delete(temp,true);Server.GameDirectory=""; }
    }
}
