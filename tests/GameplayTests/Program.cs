using System.Numerics;
using ZePractice;
using System.Text.Json;
var tests=new List<(string Name,bool Passed,string? Error)>();
void Check(bool condition,string message="assertion failed") {if(!condition)throw new Exception(message);}
void Test(string name,Action action){try{action();tests.Add((name,true,null));Console.WriteLine("PASS "+name);}catch(Exception ex){tests.Add((name,false,ex.Message));Console.WriteLine("FAIL "+name+": "+ex.Message);}}
Test("six supply classes use FYS matching key names",()=>{Check(ZeGameplayCatalog.Supplies.Count==6);Check(ZeGameplayCatalog.Supplies.All(x=>FysConfigReaderSpec(x.RoundSetting)));Check(ZeGameplayCatalog.Supplies.Count(x=>x.SpawnSetting is not null)==3);});
bool FysConfigReaderSpec(string key)=>key.StartsWith("ze_weapons_round_");
Test("grenade alias coverage",()=>{foreach(var (alias,kind) in new[]{("hegrenade",ZeSupply.He),("weapon_hegrenade",ZeSupply.He),("nade",ZeSupply.He),("incgrenade",ZeSupply.Molotov),("weapon_decoy",ZeSupply.Freeze),("flashbang",ZeSupply.Barrier),("smokegrenade",ZeSupply.BlackHole),("healthshot",ZeSupply.Adrenaline)})Check(ZeGameplayCatalog.FindSupply(alias)?.Kind==kind,alias);});
Test("every listed gun resolves a nonnegative server price",()=>{foreach(var alias in ZeGameplayCatalog.WeaponAliases){Check(ZeGameplayCatalog.GunOffers.TryGetValue(alias.Value,out var offer),alias.Key);Check(offer!.Price>=0 && offer.Price<=16000,alias.Key);}});
Test("weapon catalog refuses unknown names",()=>{Check(ZeGameplayCatalog.FindWeapon("weapon_ak47")=="weapon_ak47");Check(ZeGameplayCatalog.FindWeapon("AK")=="weapon_ak47");Check(ZeGameplayCatalog.FindWeapon("quit")==null);Check(ZeGameplayCatalog.FindWeapon("weapon_../../../etc")==null);});
Test("zero purchase cap forbids",()=>{var ledger=new RoundPurchaseLedger();Check(ledger.TryReserve("a",ZeSupply.Freeze,0)==null);Check(ledger.Used("a",ZeSupply.Freeze)==0);});
Test("single purchase and cap",()=>{var ledger=new RoundPurchaseLedger();using(var reservation=ledger.TryReserve("a",ZeSupply.Freeze,1)){Check(reservation is not null);reservation!.Commit();}Check(ledger.TryReserve("a",ZeSupply.Freeze,1)==null);Check(ledger.Used("a",ZeSupply.Freeze)==1);});
Test("failed transaction rolls back",()=>{var ledger=new RoundPurchaseLedger();using(ledger.TryReserve("a",ZeSupply.He,1))Check(ledger.Used("a",ZeSupply.He)==1);Check(ledger.Used("a",ZeSupply.He)==0);Check(ledger.TryReserve("a",ZeSupply.He,1)!=null);});
Test("unlimited negative-one cap",()=>{var ledger=new RoundPurchaseLedger();for(var i=0;i<40;i++)using(var r=ledger.TryReserve("a",ZeSupply.He,-1)){Check(r is not null);r!.Commit();}Check(ledger.Used("a",ZeSupply.He)==40);Check(ledger.TryReserve("a",ZeSupply.He,-2)==null);});
Test("separate players / supplies",()=>{var ledger=new RoundPurchaseLedger();using(var r=ledger.TryReserve("a",ZeSupply.He,1)){r!.Commit();}Check(ledger.Used("b",ZeSupply.He)==0&&ledger.Used("a",ZeSupply.Freeze)==0);});
Test("old reservation must not revert new round",()=>{var ledger=new RoundPurchaseLedger();var old=ledger.TryReserve("a",ZeSupply.He,1)!;ledger.Reset();using(var newer=ledger.TryReserve("a",ZeSupply.He,1)){newer!.Commit();}old.Dispose();Check(ledger.Used("a",ZeSupply.He)==1);});
Test("spawn supply once per round",()=>{var ledger=new RoundPurchaseLedger();Check(ledger.ClaimSpawnSupply("a"));Check(!ledger.ClaimSpawnSupply("a"));ledger.Reset();Check(ledger.ClaimSpawnSupply("a"));});
Test("skill catalog has nine and weapon aliases cover client buys",()=>{Check(ZeGameplayCatalog.Classes.Count==9);Check(ZeGameplayCatalog.WeaponAliases.Count>=25);});
Test("forward vector cardinality",()=>{var f=ZeSkillMath.Forward(0,0);Check(Vector3.Distance(f,Vector3.UnitX)<.001);f=ZeSkillMath.Forward(0,90);Check(Vector3.Distance(f,Vector3.UnitY)<.001);f=ZeSkillMath.Forward(90,0);Check(Vector3.Distance(f,-Vector3.UnitZ)<.001);});
Test("skill line of sight cone and range",()=>{Check(ZeSkillMath.InCone(Vector3.Zero,Vector3.UnitX,new(100,0,0),128,.75f));Check(!ZeSkillMath.InCone(Vector3.Zero,Vector3.UnitX,new(-10,0,0),128,.75f));Check(!ZeSkillMath.InCone(Vector3.Zero,Vector3.UnitX,new(129,0,0),128,.75f));});
Test("pull velocity cap and zero vector",()=>{Check(ZeSkillMath.PullVelocity(Vector3.Zero,Vector3.Zero,500)==Vector3.Zero);var p=ZeSkillMath.PullVelocity(Vector3.Zero,new(0,0,500),9999);Check(p.Z<=220&&p.Length()<2050);});
Test("heal clamps to maximum",()=>{Check(ZeSkillMath.Heal(50,70,100)==100);Check(ZeSkillMath.Heal(50,-70,100)==50);Check(ZeSkillMath.Heal(5000,1000,100)==5000);});
var result=new{tests=tests.Select(x=>new{name=x.Name,passed=x.Passed,error=x.Error}).ToArray()};
File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"test-results.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
return tests.Any(x=>!x.Passed)?1:0;