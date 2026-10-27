using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ZePractice;

int passed=0;
void Check(bool ok,string name) { if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name); }
const string sample="""{"version":1,"sequence":123,"tick":1000,"samples":32,"windowSeconds":0.5,"simMeanMs":2.5,"simPeakMs":7.5,"tps":64,"scriptAvailable":false,"scriptOnTick":0}""";
string With(string name,JsonNode? value) {var node=JsonNode.Parse(sample)!;node[name]=value;return node.ToJsonString();}
var parsed=ServerDiagnosticsLogic.Parse(sample)!;
Check(parsed is {Sequence:123,SimMeanMs:2.5,SimPeakMs:7.5,Tps:64},"valid native measurements preserved");
foreach(string? invalid in new string?[]{null,""," ","{}","null","[]","{broken",new('x',2049)})
    Check(ServerDiagnosticsLogic.Parse(invalid)==null,"invalid or absent snapshot rejected: "+(invalid?.Length.ToString()??"null"));
foreach(var property in JsonNode.Parse(sample)!.AsObject().Select(x=>x.Key).ToArray())
{
    var node=JsonNode.Parse(sample)!.AsObject();node.Remove(property);
    Check(ServerDiagnosticsLogic.Parse(node.ToJsonString())==null,"missing "+property+" rejected, not defaulted");
}
foreach(var (field,value) in new (string,JsonNode?)[]{("version",2),("sequence",0),("tick",-1),("samples",0),("samples",100000),
    ("windowSeconds",0.09),("windowSeconds",61),("simMeanMs",-1),("simPeakMs",2),("simPeakMs",60001),("tps",0),("tps",10001),
    ("simMeanMs","NaN"),("simPeakMs","Infinity"),("scriptAvailable","false"),("scriptOnTick",-1)})
    Check(ServerDiagnosticsLogic.Parse(With(field,value))==null,"out-of-range/wrong-type "+field+" rejected");
Check(ServerDiagnosticsLogic.Parse(sample.Replace("2.5","1e999"))==null,"overflowing float rejected");
Check(ServerDiagnosticsLogic.IsNetworkedIndex(0)&&ServerDiagnosticsLogic.IsNetworkedIndex(16383)&&!ServerDiagnosticsLogic.IsNetworkedIndex(16384),"network index boundary 16384, not client-visible count");
Check(ServerDiagnosticsLogic.TotalEntityLimit==32768,"total entity capacity");
Check(ServerDiagnosticsLogic.LoadColor(null)=="unknown"&&ServerDiagnosticsLogic.LoadColor(double.NaN)=="unknown","unavailable timing grey");
Check(ServerDiagnosticsLogic.LoadColor(7.8124)=="good"&&ServerDiagnosticsLogic.LoadColor(7.8125)=="warning","half tick warning boundary");
Check(ServerDiagnosticsLogic.LoadColor(15.6249)=="warning"&&ServerDiagnosticsLogic.LoadColor(15.625)=="critical","full tick critical boundary");
Check(ServerDiagnosticsLogic.EntityColor(74,100)=="good"&&ServerDiagnosticsLogic.EntityColor(75,100)=="warning"&&ServerDiagnosticsLogic.EntityColor(90,100)=="critical","capacity thresholds");
Check(ServerDiagnosticsLogic.TryToggle("",true,out bool state)&&!state,"toggle defaults off when on");
Check(ServerDiagnosticsLogic.TryToggle("ON",false,out state)&&state,"explicit on case insensitive");
Check(ServerDiagnosticsLogic.TryToggle("0",true,out state)&&!state,"explicit numeric off");
Check(!ServerDiagnosticsLogic.TryToggle("on;quit",false,out state)&&!state,"invalid toggle has no side effects");
Check(ServerDiagnosticsLogic.Uptime(90061)=="25:01:01"&&ServerDiagnosticsLogic.Uptime(-1)=="--","uptime retains days as hours and rejects negatives");
Check(ServerDiagnosticsLogic.Uptime(double.PositiveInfinity)=="--","infinite uptime rejected");
CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");
Check(ServerDiagnosticsLogic.Ms(1.25)=="1.25ms"&&ServerDiagnosticsLogic.Ms(null)=="--","invariant milliseconds, absent stays absent");
var reading=new ServerDiagnosticsLogic.Reading(2000,1700,2,3,1024,60,parsed,"current");
Check(ServerDiagnosticsLogic.Summary(reading).Contains("脚本采样初始化中"),"missing script observer is initializing, never a fake zero");
Check(!ServerDiagnosticsLogic.Summary(reading with {Native=parsed with {ScriptAvailable=true,ScriptOnTick=623268}}).Contains("623268"),"legacy raw counter is not presented as script activity");
Check(ServerDiagnosticsLogic.Summary(reading with {Native=null}).Contains("SV(sim) -- (峰值 --)"),"missing native plugin leaves timing unknown");
var policy=PermissionPolicy.CreateDefault();
foreach(var command in new[]{"css_ze_diag","css_ze_diag_status","css_ze_script_status"})
    Check(PermissionPolicy.IsPublicCommand(command)&&policy.Allows(null,false,command),command+" available even when practice permission is disabled");
string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
string service=File.ReadAllText(Path.Combine(root,"src/ServerDiagnosticsService.cs"));
Check(service.Contains("Layout=\"panorama/layout/custom_game/zepractice/diagnostics.vxml_c\""),"spawn requests compiled layout matching radio and VPK");
Check(service.Contains("Required=[Layout,"),"precache and installed-asset validation use actual spawn path");
var xml=XDocument.Load(Path.Combine(root,"panorama/layout/custom_game/zepractice/diagnostics.xml"));
Check(!xml.Descendants().Any(n=>n.Name.LocalName is "scripts" or "script" or "Button"),"no script or clickable controls");
var panels=xml.Descendants().Where(n=>n.Name.LocalName=="Panel"&&n.Attribute("class")?.Value.Contains("ze-diagnostics")==true).ToArray();
Check(panels.Length==2&&panels.All(n=>(string?)n.Attribute("hittest")=="false"),"both HUD roots use the supported non-hit-test attribute");
Check(xml.Descendants("Panel").All(n=>n.Attributes().All(a=>a.Name.LocalName is "id" or "class" or "hittest")),"client Panel attribute whitelist; hittestchildren rejects the entire layout");
Check(!xml.ToString().Contains("ScriptOnTick")&&xml.Descendants("Panel").Count(n=>(string?)n.Attribute("class")=="diag-row")==3,"real script activity has its own row; obsolete counter label removed");
var ids=xml.Descendants().Where(n=>n.Attribute("id")!=null).Select(n=>n.Attribute("id")!.Value).ToArray();
Check(ids.Length==7&&ids.Distinct().Count()==7&&new[]{"diagnostics_window","diag_entities","diag_networked","diag_script","diag_mean","diag_peak","diag_detail"}.All(ids.Contains),"exact seven unique update panels");
Check(xml.Descendants().Single(n=>(string?)n.Attribute("id")=="diagnostics_window").Attribute("class")!.Value.Contains("hidden"),"hidden until valid engine state");
string css=File.ReadAllText(Path.Combine(root,"panorama/styles/custom_game/zepractice/diagnostics.css"));
Check(new[]{".good",".warning",".critical",".unknown"}.All(css.Contains),"four severity colors exist");
string[] required=["panorama/layout/custom_game/zepractice/diagnostics.vxml_c","panorama/styles/custom_game/zepractice/diagnostics.vcss_c"];
foreach(string path in args)
{
    var available=CommanderResources.Probe(path,required);
    Console.WriteLine(JsonSerializer.Serialize(new{path,available}));
    Check(available.Ready,"VPK contains both CRC-validated diagnostics assets");
}
Console.WriteLine($"PASS {passed} server-diagnostics tests");
