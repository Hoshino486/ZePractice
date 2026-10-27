using ZePractice;
int checks=0;
void Check(bool pass,string message){checks++;if(!pass)throw new Exception(message);}
void Equal<T>(T expected,T actual,string message)=>Check(EqualityComparer<T>.Default.Equals(expected,actual),$"{message}: expected={expected} actual={actual}");
ItemHandlerDefinition Def(ItemHandlerMode mode,ItemHandlerType type=ItemHandlerType.Button,float cooldown=10,int max=3)=>new("ability","200","OnPressed",type,mode,cooldown,max,0,0,true,true,null);
// This literal is a schema fixture, not copied FYS data and never deployed as a map config.
var officialSchema=ItemWatchParser.Parse("""
[
  {"name":"冰","shortname":"ICE","hammerid":"100","handlers":[
    {"name":"冰冻","type":"button","hammerid":"200","event":"OnPressed","mode":2,"cooldown":30}
  ]}
]
""");
Equal(1,officialSchema.Items.Count,"published CS2Fixes array item root");
Equal(1,officialSchema.Items[0].Handlers.Count,"published CS2Fixes array handler");var parsed=ItemWatchParser.Parse("\uFEFF"+"""
{
 // legacy keyed-object JSONC root; published CS2Fixes uses array of items
 "ice": {"name":"冰","shortname":"ICE","hammerid":"100","colour":"blue","transfer":false,
 "triggers":["900"],"triggerid":"901","templated":true,
 "handlers":{
  "button":{"type":"button","hammerid":"200","event":"OnPressed","mode":2,"cooldown":30},
  "energy":{"type":"counterdown","hammerid":"201","mode":5,"offset":[1,2]},
  "uses":{"type":"counterup","hammerid":"202","mode":3,"maxuses":3,"offset":2},
 }},
 "same":{"hammerid":"100"},
 "missing":{"name":"无ID"},
 "invalid":1,
 "legacy":{"hammerid":"300","hud":false,"glow":true,"knife":true},
}
""");
Equal(2,parsed.Items.Count,"duplicate, missing id and invalid entry skipped");
var ice=parsed.Items[0];Equal("冰",ice.Name,"Unicode");Equal("blue",ice.Color,"colour alias");Equal(false,ice.Transfer,"transfer metadata");Equal(true,ice.Templated,"template metadata");Equal(2,ice.Triggers.Count,"legacy trigger alias merged");Equal(3,ice.Handlers.Count,"all handlers parsed");Equal(30f,ice.Handlers[0].Cooldown,"cooldown");Equal(1f,ice.Handlers[1].Offset,"low offset");Equal(2f,ice.Handlers[1].MaxOffset,"high offset");Equal(2f,ice.Handlers[2].MaxOffset,"scalar offset both");Check(!parsed.Items[1].Ui,"legacy hud maps ui");Check(parsed.Diagnostics.Count>=5,"invalid and unsupported behavior diagnostics");
try{ItemWatchParser.Parse("42");throw new Exception("scalar root accepted");}catch(FormatException){checks++;}
var malformed=ItemWatchParser.Parse("""{"i":{"hammerid":"99","handlers":{"bad":{"type":"button"},"other":{"type":"newtype","hammerid":"23","mode":99,"cooldown":-4}}}}""");
Equal(1,malformed.Items[0].Handlers.Count,"invalid handler skipped");Equal(ItemHandlerMode.None,malformed.Items[0].Handlers[0].Mode,"unknown mode explicit fallback");Equal(0f,malformed.Items[0].Handlers[0].Cooldown,"negative cooldown clamped");
var none=new ItemHandlerState(Def(ItemHandlerMode.None));Check(none.RecordUse(0)&&none.RecordUse(0),"mode1 unlimited");Equal("+",none.Label(0),"mode1 label");
var cd=new ItemHandlerState(Def(ItemHandlerMode.Cooldown));Equal("R",cd.Label(0),"mode2 initial ready");Check(cd.RecordUse(0),"mode2 initial use");Check(!cd.RecordUse(9.9),"mode2 rejects during CD");Equal("1s",cd.Label(9.1),"ceil cooldown");Check(cd.RecordUse(10),"mode2 boundary ready");cd.Reset();Equal("R",cd.Label(0),"reset removes cooldown");
var limited=new ItemHandlerState(Def(ItemHandlerMode.MaxUses));Check(limited.RecordUse(0),"mode3 use1");Check(!limited.RecordUse(1),"mode3 per-use cooldown");Check(limited.RecordUse(10)&&limited.RecordUse(20),"mode3 three uses");Check(!limited.RecordUse(50),"mode3 exhausted");Equal("E",limited.Label(50),"mode3 exhausted label");
var recharge=new ItemHandlerState(Def(ItemHandlerMode.CooldownAfterUses));Check(recharge.RecordUse(0)&&recharge.RecordUse(0)&&recharge.RecordUse(0),"mode4 three quick uses");Check(!recharge.RecordUse(9),"mode4 cooldown after last use");Equal("1s",recharge.Label(9.1),"mode4 countdown");Equal("0/3",recharge.Label(10),"mode4 reset at deadline");Check(recharge.RecordUse(10),"mode4 reused");
var unknown=new ItemHandlerState(Def(ItemHandlerMode.MaxUses,max:0));Check(unknown.RecordUse(0),"missing maximum not fake exhausted");Equal("1/?",unknown.Label(20),"unknown max transparent");
var energy=new ItemHandlerState(Def(ItemHandlerMode.CounterValue,ItemHandlerType.CounterDown) with{Offset=1,MaxOffset=2});Equal("等待输出",energy.Label(0),"counter never guessed before output");Check(!energy.ObserveCounter(8,0,10,0),"energy sample not claimed a use");Equal(9f,energy.CounterValue,"counterdown actualremaining");Equal(12f,energy.CounterMaximum,"maximum offset");energy.ObserveCounter(-5,0,10,1);Equal("E",energy.Label(1),"energy clamped to zero");
var upenergy=new ItemHandlerState(Def(ItemHandlerMode.CounterValue,ItemHandlerType.CounterUp));upenergy.ObserveCounter(3,0,10,0);Equal(7f,upenergy.CounterValue,"counterup remaining");
var counter=new ItemHandlerState(Def(ItemHandlerMode.MaxUses,ItemHandlerType.CounterDown));Check(!counter.ObserveCounter(10,0,10,0),"firstcounter observation isn't use");Equal(10,counter.EffectiveMaxUses,"actual counterbounds override configmax");Check(counter.ObserveCounter(9,0,10,1),"downcounter consumes use");Equal(1,counter.Uses,"counter spent count");Check(!counter.ObserveCounter(10,0,10,2),"counter refill isn't use");Equal(0,counter.Uses,"counter reset authoritative");Check(!counter.ObserveCounter(float.NaN,0,10,3),"nonfinite ignored");Equal(0,counter.Uses,"invalid sample unchanged");
var chargecounter=new ItemHandlerState(Def(ItemHandlerMode.CooldownAfterUses,ItemHandlerType.CounterUp));chargecounter.ObserveCounter(0,0,2,0);chargecounter.ObserveCounter(1,0,2,1);Equal("1/2",chargecounter.Label(1),"counter mode4 no early cooldown");chargecounter.ObserveCounter(2,0,2,2);Equal("10s",chargecounter.Label(2),"counter mode4 cooldown only when full");Equal("2/2",chargecounter.Label(50),"counter never synthetically recharged");chargecounter.ObserveCounter(0,0,2,51);Equal("0/2",chargecounter.Label(51),"counter map recharge acknowledged");
var cdCounter=new ItemHandlerState(Def(ItemHandlerMode.Cooldown,ItemHandlerType.CounterUp));Check(!cdCounter.ObserveCounter(0,0,2,0),"cooldown counter first sample ignored");Equal("R",cdCounter.Label(0),"cooldowncounter initially ready");Check(cdCounter.ObserveCounter(1,0,2,1),"subsequent cooldown counteroutput tracked");Equal("10s",cdCounter.Label(1),"cooldowncounter timer");cdCounter.Reset();Equal("等待输出",cdCounter.Label(2),"reset counters to unknown");
string root=args.FirstOrDefault()??@"E:\mapper\chana\临时文件夹\ze-practice-build-20261011\upstream\cs2-master";
if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
var itemDirs=Directory.EnumerateDirectories(root,"entwatch",SearchOption.AllDirectories).ToArray();
int files=0,items=0;foreach(var dir in itemDirs)foreach(var file in Directory.EnumerateFiles(dir,"*.jsonc",SearchOption.AllDirectories)){var data=ItemWatchParser.Parse(File.ReadAllText(file));files++;items+=data.Items.Count;}
Console.WriteLine($"FYS SNAPSHOT root={root}; public_entwatch_directories={itemDirs.Length} files={files} definitions={items}. Zero means no upstream item configs were available, not an all-maps test success.");
Console.WriteLine($"PASS {checks} EntWatch schema/state checks; fixtures are synthetic. Live HammerID binding, CSS ABI, item pickup/use and HUD need in-game verification.");
