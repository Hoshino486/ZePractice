using System.Collections;
using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Plugin.Host;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;

// Does not simulate a connected remote human. Checks actual installed resources and the
// production native renderer/visibility state/destruction using two bot-backed sessions.
public sealed class RemoteMenuNativeProbe : BasePlugin
{
    public override string ModuleName=>"RemoteMenuNativeProbe - isolated 27025 only";
    public override string ModuleVersion=>"1.0";
    private readonly List<object> _results=[];
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static object Field(object o,string n)=>o.GetType().GetField(n,Fields)!.GetValue(o)!;
    private static void Set(object o,string n,object? v)=>o.GetType().GetField(n,Fields)!.SetValue(o,v);
    private static object? Call(object o,string n,params object[] args)=>o.GetType().GetMethod(n,Fields)!.Invoke(o,args);
    private static BasePlugin Plugin()=>(BasePlugin)((IPluginManager)Field(Application.Instance,"_pluginManager")).GetLoadedPlugins().Select(p=>p.Plugin)
        .Single(p=>p?.GetType().FullName=="ZePractice.ZePracticePlugin"&&!(bool)Field(p,"_accessDisposed"))!;
    private static void Guard()
    {
        if(ConVar.Find("hostport")?.GetPrimitiveValue<int>()!=27025||!Server.GameDirectory.Contains("ZePracticeTest-20261016")||Utilities.GetPlayers().Any(p=>!p.IsBot&&!p.IsHLTV))
            throw new InvalidOperationException("Isolated 27025 only, without human clients");
    }
    private void Check(string name,bool passed,object? data=null)
    {
        var item=new{name,passed,data};_results.Add(item);
        Server.PrintToConsole("REMOTE_MENU_PROBE "+JsonSerializer.Serialize(item));
        File.WriteAllText(Path.Combine(ModuleDirectory,"results.json"),JsonSerializer.Serialize(_results,new JsonSerializerOptions{WriteIndented=true}));
        if(!passed)throw new InvalidOperationException(name);
    }
    private static CCSCustomHudLayout[] Huds()=>Utilities.FindAllEntitiesByDesignerName<CCSCustomHudLayout>("custom_hud_layout")
        .Where(h=>h.Entity?.Name?.StartsWith("ze_radio_probe_")==true).ToArray();
    private static EHudPanelClassStatus_t Status(CCSCustomHudLayoutState state)=>
        new HUDPanelHasClass_t(NativeAPI.GetNetworkVectorElementAt(state.HasClasses.Handle,0)).ClassStatus;
    private static CCSCustomHudLayoutState State(CCSCustomHudLayout h,CCSPlayerController p)=>new(
        NativeAPI.GetNetworkVectorElementAt(h.PlayerLayoutStates.Handle,0)+p.Slot*CounterStrikeSharp.API.Modules.Memory.Schema.GetClassSize("CCSCustomHudLayoutState"));
    private static CCSCustomHudLayout Create(object service,CCSPlayerController p,string title)
    {
        var hud=Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout")!;
        using var kv=new CEntityKeyValues();kv.SetString("layout","panorama/layout/custom_game/zepractice/radio.vxml_c");kv.SetString("targetname","ze_radio_probe_"+p.Slot);
        hud.Observable=false;hud.DispatchSpawn(kv);hud.SetHasClass("radio_window","hidden",true);
        var menu=new ChatMenu(title){ExitButton=true,PostSelectAction=PostSelectAction.Nothing};
        for(int i=0;i<10;i++)menu.AddMenuOption("测试选项 "+i,(_,_)=>{});
        var type=service.GetType().GetNestedType("Session",BindingFlags.NonPublic)!;
        var session=Activator.CreateInstance(type,true)!;
        Set(session,"Player",p);Set(session,"Slot",p.Slot);Set(session,"Controller",p.EntityHandle.Raw);Set(session,"UserId",p.UserId);
        Set(session,"Layout",hud);Set(session,"Menu",menu);Set(session,"Options",menu.MenuOptions.ToArray());Set(session,"Page",0);Set(session,"LastKey",Environment.TickCount64-500);
        ((IDictionary)Field(service,"_sessions"))[p.Slot]=session;
        Call(service,"Render",session);hud.SetHasClassForPlayer(p,"radio_window","hidden",false);
        return hud;
    }
    public override void Load(bool hotReload)
    {
        AddCommand("css_remote_menu_probe","Isolated native radio checks (not visual client acceptance)",(p,c)=>
        {
            if(p!=null)return;Guard();_results.Clear();
            var plugin=Plugin();var service=Field(plugin,"_radioPanel");
            try{Run(plugin,service);}catch(Exception e){Call(service,"CloseAll");Check("exception",false,e.ToString());}
        });
    }
    private void Run(BasePlugin plugin,object service)
    {
        Call(service,"CloseAll");Call(service,"RefreshResources");
        var availability=Field(service,"_resources");
        Check("installed-workshop-radio-crc-verified",(bool)availability.GetType().GetProperty("Ready")!.GetValue(availability)!,availability);
        var config=plugin.GetType().GetProperty("Config")!.GetValue(plugin)!;
        Check("remote-radio-config-enabled",(string)config.GetType().GetProperty("MenuUiMode")!.GetValue(config)! == "radio"&&
            (bool)config.GetType().GetProperty("MenuAssetsReady")!.GetValue(config)!&&(bool)config.GetType().GetProperty("PanoramaAllowRemoteClients")!.GetValue(config)!);
        Check("expected-plugin-version",plugin.ModuleVersion=="1.7.7",plugin.ModuleVersion);
        var players=Utilities.GetPlayers().Where(p=>p.IsBot&&!p.IsHLTV).Take(2).ToArray();Check("two-bots-no-human-render-claim",players.Length==2);
        var first=Create(service,players[0],"菜单甲"); var second=Create(service,players[1],"菜单乙");
        Check("two-distinct-native-layouts",Huds().Length==2&&first.EntityHandle.Raw!=second.EntityHandle.Raw);
        foreach(var pair in new[]{(first,players[0],players[1]),(second,players[1],players[0])})
        {
            var (hud,owner,other)=pair;
            Check("compiled-layout-slot-"+owner.Slot,hud.StrLayout=="panorama/layout/custom_game/zepractice/radio.vxml_c"&&!hud.Observable);
            Check("hidden-globally-slot-"+owner.Slot,Status(hud.GlobalLayoutState)==EHudPanelClassStatus_t.k_eHudPanelClassStatus_HasClass);
            Check("visible-owner-only-state-slot-"+owner.Slot,Status(State(hud,owner))==EHudPanelClassStatus_t.k_eHudPanelClassStatus_DoesNotHaveClass&&State(hud,other).HasClasses.Count==0);
            Check("ten-native-text-variables-slot-"+owner.Slot,hud.GlobalLayoutState.DialogVariableStrings.Count==10,hud.GlobalLayoutState.DialogVariableStrings.Count);
            Check("no-mouse-capture-slot-"+owner.Slot,!hud.IsInputCaptureEnabled(owner)&&!hud.GlobalLayoutState.InputCaptureEnabled);
        }
        var sessions=(IDictionary)Field(service,"_sessions");var s=sessions[players[0].Slot]!;uint handle=first.EntityHandle.Raw;
        Set(s,"Page",1);Call(service,"Render",s);Check("same-entity-page-refresh",first.EntityHandle.Raw==handle&&(int)Field(s,"Page")==1);
        Call(plugin,"CloseMenus",players[0]);
        Check("central-close-only-one-session",!(bool)Call(service,"IsOpen",players[0])!&&(bool)Call(service,"IsOpen",players[1])!);
        Call(plugin,"CloseMenus",players[0]);Check("repeated-close-idempotent",sessions.Count==1);
        Call(plugin,"CloseMenus",players[1]);Check("all-menus-logically-closed",sessions.Count==0);
        AddTimer(.3f,()=>{
            Guard();
            Check("native-entities-actually-removed-after-frame",!first.IsValid&&!second.IsValid&&Huds().Length==0);
            Check("no-pending-requests",((string)Call(service,"Diagnostics")!).Contains("pending=0"),Call(service,"Diagnostics"));
            Check("limits-explicit",true,"Native entity/state verification only. No real remote human renderer/visual assertion.");
            Server.PrintToConsole("REMOTE_MENU_PROBE_COMPLETE "+_results.Count);
        },TimerFlags.STOP_ON_MAPCHANGE);
    }
}
