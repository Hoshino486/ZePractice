using System.Collections;
using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Plugin.Host;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Timers;

// Console-only fixture. Never installed on the production server.
public sealed class ServerDiagnosticsNativeProbe : BasePlugin
{
    public override string ModuleName=>"ServerDiagnosticsNativeProbe - isolated 27025 only";
    public override string ModuleVersion=>"1.0";
    private readonly List<object> _results=[];
    private static FieldInfo Info(object o,string name)=>o.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance)!;
    private static object Field(object o,string name)=>Info(o,name).GetValue(o)!;
    private static object? Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance)!.Invoke(o,args);
    private static object Plugin()=>((IPluginManager)Field(Application.Instance,"_pluginManager")).GetLoadedPlugins().Select(p=>p.Plugin)
        .Single(p=>p?.GetType().FullName=="ZePractice.ZePracticePlugin"&&Info(p,"_serverDiagnostics").GetValue(p) is {} service&&!(bool)Field(service,"_disposed"))!;
    private static object Service()=>Field(Plugin(),"_serverDiagnostics");
    private static void Guard()
    {
        if(ConVar.Find("hostport")?.GetPrimitiveValue<int>()!=27025||!Server.GameDirectory.Contains("ZePracticeTest-20261016")||Utilities.GetPlayers().Any(p=>!p.IsBot&&!p.IsHLTV))
            throw new InvalidOperationException("Isolated test server only; no human players allowed.");
    }
    private void Check(string name,bool pass,object? data=null)
    {
        var item=new{name,pass,data};_results.Add(item);
        Server.PrintToConsole("DIAG_PROBE "+JsonSerializer.Serialize(item));
        File.WriteAllText(Path.Combine(ModuleDirectory,"results.json"),JsonSerializer.Serialize(_results,new JsonSerializerOptions{WriteIndented=true}));
        if(!pass)throw new Exception(name);
    }
    private void Later(float seconds,Action action)=>AddTimer(seconds,()=>{Guard();try{action();}catch(Exception e){Check("unexpected-exception",false,e.ToString());}},TimerFlags.STOP_ON_MAPCHANGE);
    private static void Sample(object service) {Info(service,"_nextSample").SetValue(service,0d);Call(service,"Tick");}
    private static JsonElement Snapshot(object service) {using var doc=JsonDocument.Parse((string)Call(service,"Diagnostics")!);return doc.RootElement.Clone();}
    private static CCSCustomHudLayout[] Layouts()=>Utilities.FindAllEntitiesByDesignerName<CCSCustomHudLayout>("custom_hud_layout").Where(h=>h.Entity?.Name=="ze_server_diagnostics_hud").ToArray();
    private static EHudPanelClassStatus_t Hidden(CCSCustomHudLayout h)=>new HUDPanelHasClass_t(NativeAPI.GetNetworkVectorElementAt(h.GlobalLayoutState.HasClasses.Handle,0)).ClassStatus;
    private static bool PlayerVisible(CCSCustomHudLayout h,CCSPlayerController p)
    {
        var first=NativeAPI.GetNetworkVectorElementAt(h.PlayerLayoutStates.Handle,0);
        var state=new CCSCustomHudLayoutState(first+p.Slot*CounterStrikeSharp.API.Modules.Memory.Schema.GetClassSize("CCSCustomHudLayoutState"));
        return state.HasClasses.Count==1&&new HUDPanelHasClass_t(NativeAPI.GetNetworkVectorElementAt(state.HasClasses.Handle,0)).ClassStatus==EHudPanelClassStatus_t.k_eHudPanelClassStatus_DoesNotHaveClass;
    }
    public override void Load(bool hotReload)
    {
        AddCommand("css_diag_candidate_resources","Isolated native-state fixture only; does NOT certify Workshop delivery",(p,c)=>{
            if(p!=null)return;Guard();var service=Service();
            var type=service.GetType().Assembly.GetType("ZePractice.CommanderResources")!;
            var required=service.GetType().GetField("Required",BindingFlags.Public|BindingFlags.Static)!.GetValue(null)!;
            var candidate=type.GetMethod("Probe")!.Invoke(null,new[]{c.GetArg(1),required})!;
            if(!(bool)candidate.GetType().GetProperty("Ready")!.GetValue(candidate)!)throw new InvalidOperationException("Candidate resources failed CRC validation");
            Info(service,"_resources").SetValue(service,candidate);
            Server.PrintToConsole("DIAG_CANDIDATE_ONLY "+JsonSerializer.Serialize(candidate)+"; no installed file changed; not client rendering or Workshop download acceptance");
        });
        AddCommand("css_diag_installed_resources","Restore actual installed-resource probe after candidate testing",(p,c)=>{
            if(p!=null)return;Guard();Call(Service(),"RefreshResources");Server.PrintToConsole((string)Call(Service(),"Diagnostics")!);
        });
        AddCommand("css_diag_inventory","Isolated HUD inventory without requiring managed plugin",(p,c)=>{
            if(p!=null)return;Guard();Server.PrintToConsole(JsonSerializer.Serialize(new{map=Server.MapName,huds=Layouts().Select(h=>new{h.Index,h.StrLayout}).ToArray()}));
        });
        AddCommand("css_diag_expect","Check actual native availability after unload/reload",(p,c)=>{
            if(p!=null)return;Guard();var service=Service();Sample(service);var snapshot=Snapshot(service);var reading=snapshot.GetProperty("reading");
            string expected=c.GetArg(1);
            Check("native-state-"+expected,reading.GetProperty("NativeStatus").GetString()==expected,snapshot);
            Check("native-state-timing-"+expected,(reading.GetProperty("Native").ValueKind==JsonValueKind.Null)==(expected!="current"));
            if(expected!="current"&&Layouts().Length>0)
                Check("no-stale-timing-in-HUD",((IDictionary)Field(service,"_text"))["diag_mean"]?.ToString()=="--");
        });
        AddCommand("css_diag_probe","Run native diagnostics regression, not visual client acceptance",(p,c)=>{
            if(p!=null)return;Guard();_results.Clear();
            try {Run();}catch(Exception e){Check("unexpected-exception",false,e.ToString());}
        });
    }
    private void Run()
    {
        var service=Service();
        var players=Utilities.GetPlayers().Where(p=>p.IsBot&&p.PawnIsAlive).Take(2).ToArray();
        Check("two-bot-fixture-no-human-clients",players.Length==2);
        var sessions=players.Select(p=>Call(service,"Get",p)!).ToArray();
        foreach(var session in sessions)Info(session,"Enabled").SetValue(session,true);
        Sample(service);Sample(service);
        var snapshot=Snapshot(service);var reading=snapshot.GetProperty("reading");
        Check("crc-verified-diagnostics-resources-ready-see-provenance",snapshot.GetProperty("resources").GetProperty("Ready").GetBoolean(),snapshot);
        Check("native-snapshot-current",reading.GetProperty("NativeStatus").GetString()=="current");
        var native=reading.GetProperty("Native");
        double mean=native.GetProperty("SimMeanMs").GetDouble(),peak=native.GetProperty("SimPeakMs").GetDouble();
        Check("native-gameframe-mean-peak-finite",double.IsFinite(mean)&&double.IsFinite(peak)&&mean>=0&&peak>=mean,native);
        var script=reading.GetProperty("Script");
        Check("script-real-observer-not-legacy-vprof",script.GetProperty("Available").GetBoolean()&&script.GetProperty("Window").ValueKind==JsonValueKind.Object,script);
        Check("script-HUD-not-missing-counter",((IDictionary)Field(service,"_text"))["diag_script"]?.ToString()?.Contains("回调")==true);
        var entities=Utilities.GetAllEntities().Where(e=>e.IsValid).ToArray();
        Check("entity-count-independent-enumeration",reading.GetProperty("Entities").GetInt32()==entities.Length,new{reported=reading.GetProperty("Entities").GetInt32(),actual=entities.Length});
        Check("network-index-count-independent",reading.GetProperty("Networked").GetInt32()==entities.Count(e=>e.Index<16384));
        Check("actual-bot-human-counts",reading.GetProperty("Bots").GetInt32()==Utilities.GetPlayers().Count(p=>p.IsBot&&!p.IsHLTV)&&reading.GetProperty("Humans").GetInt32()==0);
        Check("memory-reading-positive",reading.GetProperty("MemoryMiB").GetDouble()>0);
        var huds=Layouts();Check("one-shared-layout-for-two-viewers",huds.Length==1);
        var hud=huds.Single();uint handle=hud.EntityHandle.Raw;
        Check("correct-compiled-engine-layout",hud.StrLayout=="panorama/layout/custom_game/zepractice/diagnostics.vxml_c"&&!hud.Observable);
        Check("global-hidden-and-both-viewer-overrides-visible",Hidden(hud)==EHudPanelClassStatus_t.k_eHudPanelClassStatus_HasClass&&players.All(p=>PlayerVisible(hud,p)));
        Check("six-persistent-text-variables",hud.GlobalLayoutState.DialogVariableStrings.Count==6,hud.GlobalLayoutState.DialogVariableStrings.Count);
        Check("no-input-capture",!hud.GlobalLayoutState.InputCaptureEnabled&&players.All(p=>!hud.IsInputCaptureEnabled(p)));
        Check("both-viewers-transmit-predicate-true",players.All(p=>(bool)Call(service,"VisibleTo",p)!));
        Info(sessions[0],"Enabled").SetValue(sessions[0],false);Sample(service);
        Check("one-off-other-on-filter-isolated",!(bool)Call(service,"VisibleTo",players[0])!&&(bool)Call(service,"VisibleTo",players[1])!&&!PlayerVisible(hud,players[0])&&PlayerVisible(hud,players[1]));
        Check("one-off-keeps-shared-layout",Layouts().Length==1&&Layouts()[0].EntityHandle.Raw==handle);
        Info(sessions[1],"Enabled").SetValue(sessions[1],false);Sample(service);
        // Remove() schedules engine entity destruction; validate after a frame, not inside the same command callback.
        Later(.2f,()=>AfterDisabled(service,players,sessions,hud,handle,native.GetProperty("Sequence").GetUInt32()));
    }
    private void AfterDisabled(object service,CCSPlayerController[] players,object[] sessions,CCSCustomHudLayout hud,uint handle,uint sequence)
    {
        Check("all-off-removes-shared-entity",!hud.IsValid&&Layouts().Length==0);
        Info(sessions[0],"Enabled").SetValue(sessions[0],true);Sample(service);Sample(service);
        Check("on-recreates-without-restart",Layouts().Length==1&&Layouts()[0].EntityHandle.Raw!=handle);
        Check("missing-viewer-not-transmitted",!(bool)Call(service,"VisibleTo",new object[]{null!})!);
        var sessionHandle=Field(sessions[0],"Handle");Info(sessions[0],"Handle").SetValue(sessions[0],0u);
        Check("reused-slot-invalid-handle-is-not-transmitted",!(bool)Call(service,"VisibleTo",players[0])!);
        Info(sessions[0],"Handle").SetValue(sessions[0],sessionHandle);
        int before=Snapshot(service).GetProperty("reading").GetProperty("Entities").GetInt32();
        var fixtures=new List<CBaseEntity>();
        try
        {
            for(int i=0;i<3;i++){var e=Utilities.CreateEntityByName<CBaseEntity>("info_target")!;e.DispatchSpawn();fixtures.Add(e);}
            Sample(service);
            Check("created-entities-counted-exactly",Snapshot(service).GetProperty("reading").GetProperty("Entities").GetInt32()==before+3);
        }
        finally{foreach(var e in fixtures)if(e.IsValid)e.Remove();}
        Sample(service);
        Info(service,"_nativeSeen").SetValue(service,(double)Server.EngineTime-3);
        Sample(service);
        var stale=Snapshot(service).GetProperty("reading");
        Check("frozen-sample-timeout-rejected",stale.GetProperty("NativeStatus").GetString()=="stale-native-sample"&&stale.GetProperty("Native").ValueKind==JsonValueKind.Null);
        Check("frozen-sample-not-rendered-as-healthy",((IDictionary)Field(service,"_text"))["diag_mean"]?.ToString()=="--");
        Later(1.2f,()=>{
            Sample(service);var latest=Snapshot(service);
            Check("removed-entities-no-longer-counted",latest.GetProperty("reading").GetProperty("Entities").GetInt32()==Utilities.GetAllEntities().Count(e=>e.IsValid));
            Check("native-sequence-advances",latest.GetProperty("reading").GetProperty("Native").GetProperty("Sequence").GetUInt32()!=sequence);
            Check("service-errors-empty",latest.GetProperty("errors").GetArrayLength()==0,latest);
            Check("visual-limit-explicit",latest.GetProperty("visualAcceptance").GetString()=="requires-real-client");
            Server.PrintToConsole("DIAG_NATIVE_PROBE_COMPLETE "+_results.Count+" (no client rendering asserted)");
        });
    }
}
