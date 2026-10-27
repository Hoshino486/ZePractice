using System.Globalization;
using ZePractice;

int passed=0;
void Check(bool ok,string name) { if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name); }
void Throws(Action f,string name) { bool thrown=false;try{f();}catch(InvalidOperationException){thrown=true;}Check(thrown,name); }
var a=new ScriptActivityAccumulator();a.Reset(0);
Check(a.Sample(.01)==null,"warming-up is not a zero sample");
a.Enter(1,true,.01);a.Exit(1,.02);
a.Enter(1,true,.03);a.Exit(1,.04);
a.Enter(2,false,.05);a.Exit(2,.06);
var w=a.Sample(.5)!;
Check(w.Callbacks==3&&w.ThinkCallbacks==2,"Think is subset of completed callbacks");
Check(w.CallbacksPerSecond==6&&w.ThinkPerSecond==4,"rates use actual window duration");
Check(w.Errors==1&&w.TotalErrors==1&&w.TotalCallbacks==3,"caught exceptions and totals retained");
Check(w.ActiveContexts==2,"context deduplication not entities or files");
Check(Math.Abs(w.CallbackWallMs-30)<.0001,"outer callback wall time");
Check(Math.Abs(w.LastCallbackAgoSeconds!.Value-.44)<.0001,"last activity age");
var idle=a.Sample(1)!;
Check(idle.Callbacks==0&&idle.ThinkCallbacks==0&&idle.TotalCallbacks==3&&idle.TotalErrors==1,"idle windows zero but map totals retained");
Check(idle.ActiveContexts==0,"active contexts reset every window");
Check(a.Depth==0,"all observed native callbacks paired");
a.Enter(1,true,1.01);a.Enter(2,false,1.02);a.Exit(1,1.04);a.Exit(1,1.06);
var nested=a.Sample(1.5)!;
Check(nested.Callbacks==2&&nested.ThinkCallbacks==1&&nested.ActiveContexts==2,"nested callback identities and names paired");
Check(Math.Abs(nested.CallbackWallMs-50)<.0001,"nested wall time never double-counted");
a.Enter(1,false,1.6);a.Exit(77,1.7);
Check(a.Sample(2)!.OtherResults==1,"unknown return values not silently successful");
a.Reset(5);var reset=a.Sample(5.5)!;
Check(reset.TotalCallbacks==0&&reset.TotalErrors==0&&reset.LastCallbackAgoSeconds==null,"map transition clears all previous activity");
Throws(()=>a.Exit(1,6),"unpaired post fails sampling not map execution");
a.Reset(6);a.Enter(1,false,6.1);
Throws(()=>a.Sample(6.5),"unfinished callback cannot produce healthy sample");
a.Reset(6);for(int i=0;i<256;i++)a.Enter(i,false,6.1);
Throws(()=>a.Enter(257,false,6.1),"recursion memory bounded");
a.Reset(6);a.Enter(1,false,6.2);Throws(()=>a.Exit(1,6.1),"non-monotonic clock rejected");
a.Reset(6);Check(a.Sample(double.NaN)==null&&a.Sample(double.PositiveInfinity)==null,"non-finite sample clock rejected");
CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");
var r=new ScriptActivityLogic.Reading(true,"current",20,w);
Check(ScriptActivityLogic.Summary(r).Contains("回调 6.0/s")&&ScriptActivityLogic.Summary(r).Contains("Think 4.0/s"),"invariant display and clear units");
Check(ScriptActivityLogic.Color(r)=="critical","caught window exception is red");
Check(ScriptActivityLogic.Color(r with {Window=idle})=="warning","earlier map exception remains visible");
Check(ScriptActivityLogic.Summary(r with {Window=idle}).Contains("本窗无回调"),"event-only idle scripts not called broken or assumed loaded");
Check(ScriptActivityLogic.Summary(r with {Entities=0,Window=reset}).Contains("无脚本实体"),"empty map distinguished from unavailable sampler");
Check(ScriptActivityLogic.Color(r with {Entities=0,Window=reset})=="good","valid empty sample is available");
Check(ScriptActivityLogic.Summary(r with {Available=false,Window=null}).Contains("采样接口未就绪")&&ScriptActivityLogic.Color(r with {Available=false})=="unknown","sampler failure distinct from script failure");
Check(!ScriptActivityLogic.Summary(r with {Available=false,Window=null}).Contains("0.0/s"),"failure not fabricated zero rate");
Check(ScriptActivityLogic.Summary(r with {Window=null}).Contains("采样中"),"warmup distinct from idle");
Check(ScriptActivityLogic.Summary(r with {Window=w with {OtherResults=1}}).Contains("返回值待核验"),"unknown native result surfaced");
Check(PermissionPolicy.IsPublicCommand("css_ze_script_status")&&PermissionPolicy.CreateDefault().Allows(null,false,"css_ze_script_status"),"script status is open to all players");
bool invalid=false;try{ScriptActivityNativeData.Validate(new byte[128]);}catch(InvalidDataException){invalid=true;}
Check(invalid,"unverified binary rejected, no guessed hook on future game versions");
foreach(string file in args){ScriptActivityNativeData.Validate(File.ReadAllBytes(file));Check(true,"deployed server binary hash and unique callback signature");}
Console.WriteLine($"PASS {passed} script activity tests");
