using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Plugin.Host;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

// Isolated fixture only. No additional callback hook; no changes to existing map scripts.
public sealed class ScriptActivityNativeProbe:BasePlugin
{
    public override string ModuleName=>"Script activity isolated probe";
    public override string ModuleVersion=>"0.2.0";
    private static FieldInfo Info(object o,string name)=>o.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance)!;
    private static object Field(object o,string name)=>Info(o,name).GetValue(o)!;
    private static object Service()=>Field(((IPluginManager)Field(Application.Instance,"_pluginManager")).GetLoadedPlugins().Select(p=>p.Plugin)
        .Single(p=>p?.GetType().FullName=="ZePractice.ZePracticePlugin"&&Info(p,"_serverDiagnostics").GetValue(p) is {} service&&!(bool)Field(service,"_disposed"))!,"_serverDiagnostics");
    private static void Guard()
    {
        if(ConVar.Find("hostport")?.GetPrimitiveValue<int>()!=27025||!Server.GameDirectory.Contains("ZePracticeTest-20261016",StringComparison.OrdinalIgnoreCase)||Utilities.GetPlayers().Any(p=>!p.IsBot&&!p.IsHLTV))
            throw new InvalidOperationException("This probe is restricted to the isolated test server without real clients.");
    }
    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest=>manifest.AddResource("maps/scripts/ze_activity_fixture.vjs"));
        // No player/global-variable access before initial map exists.
        AddCommand("css_script_probe","Save actual production sampler reading",(p,c)=>{
            if(p!=null)return;Guard();
            string label=c.GetArg(1);if(!System.Text.RegularExpressions.Regex.IsMatch(label,"^[a-z0-9-]{1,64}$"))throw new Exception("Supply fixture evidence label");
            var service=Service();
            var json=(string)service.GetType().GetMethod("Diagnostics")!.Invoke(service,null)!;
            File.WriteAllText(Path.Combine(ModuleDirectory,label+".json"),json);
            Server.PrintToConsole("SCRIPT_PROBE "+label+" "+json);
        });
        AddCommand("css_script_fixture","Spawn independent test script on empty de_dust2",(p,c)=>{
            if(p!=null)return;Guard();if(Server.MapName!="de_dust2")throw new Exception("Fixture only on de_dust2");
            if(Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("point_script").Any())throw new Exception("Fixture needs a script-free map");
            var entity=Utilities.CreateEntityByName<CBaseEntity>("point_script")!;
            using var kv=new CEntityKeyValues();kv.SetString("targetname","ze_script_fixture");kv.SetString("cs_script","maps/scripts/ze_activity_fixture.vjs");
            entity.DispatchSpawn(kv);
            entity.AcceptInput("RunScriptInput",null,null,"Start");
            Server.PrintToConsole("SCRIPT_FIXTURE spawned "+entity.Index);
        });
        AddCommand("css_script_fault_fixture","Fail only observer, never game execution",(p,c)=>{
            if(p!=null)return;Guard();
            var observer=Field(Service(),"_scripts");Info(observer,"_fault").SetValue(observer,"isolated-test-unavailable");
        });
        AddCommand("css_script_fixture_input","Trigger only an isolated test callback",(p,c)=>{
            if(p!=null)return;Guard();
            if(c.GetArg(1) is not ("Start" or "Event" or "Throw" or "Missing"))throw new Exception("Invalid test input");
            var entity=Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("point_script").Single(e=>e.Entity?.Name=="ze_script_fixture");
            entity.AcceptInput("RunScriptInput",null,null,c.GetArg(1));
        });
    }
}
