using System.Numerics;
using ZePractice;
int passed=0;
void Check([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string message) { if(!ok)throw new Exception(message); passed++; Console.WriteLine("PASS "+message); }
var roster=new PracticeBotRoster();
var a=new PracticeIdentity(0,10,1); var b=new PracticeIdentity(1,11,2);
var placement=new PracticePlacement(new(1,2,3),new(0,90,0),2,12345,2500);
var one=roster.Reserve(a,placement,false,2,0)!;
var two=roster.Reserve(a,placement,false,2,.6)!;
Check(one!=null && two!=null,"two pending requests consume two slots");
Check(roster.Reserve(a,placement,false,2,1)==null,"third pending ordinary request rejected");
Check(roster.Reserve(b,placement,false,2,1)!=null,"other connection has independent limit");
Check(roster.Reserve(a,placement,true,2,1)!=null,"explicit server owner bypasses personal quota");
Check(!roster.Owned(new(0,12,1)).Any(),"same slot/userid new serial cannot control old bot");
Check(!roster.Owned(new(0,10,3)).Any(),"same slot/serial new userid cannot control old bot");
Check(!roster.Owned(new(0,10,1,1)).Any(),"same slot/controller/userid reused by CS2 is isolated by connection epoch");
Check(roster.Bind(one,new(4,25,8)),"bot binds exactly once");
Check(!roster.Bind(one,new(5,26,9)),"second bot cannot steal same request");
Check(roster.ByBot(new(4,25,8))==one,"all three identity components match");
Check(roster.ByBot(new(4,28,8))==null,"reused bot slot rejected");
roster.Retire(two);
Check(roster.ByName(two.Name)==two && !roster.Bind(two,new(6,30,11)),"cancelled request tombstone rejects late bot binding");
Check(!roster.Owned(a).Contains(two),"cancelled request releases owner quota");
Check(one.Placement.Health==12345 && one.Placement.Position==new Vector3(1,2,3),"health/placement snapshot retained");
Check(PracticeBotRoster.Opponent(3)==2 && PracticeBotRoster.Opponent(2)==3 && PracticeBotRoster.Opponent(1)==0,"opposing team mapping");
foreach (int ownerTeam in new[]{2,3})
{
    foreach (var (choice,expected) in new[]{("",PracticeBotRoster.Opponent(ownerTeam)),("enemy",PracticeBotRoster.Opponent(ownerTeam)),("ally",ownerTeam),("t",2),("ct",3),(" CT ",3),("T",2)})
        Check(PracticeBotRoster.TryResolveTeam(ownerTeam,choice,out int team)&&team==expected,$"team choice '{choice}' for owner {ownerTeam}");
    Check(PracticeBotRoster.TryResolveTeam(ownerTeam,null,out int defaultTeam)&&defaultTeam==PracticeBotRoster.Opponent(ownerTeam),"missing selection keeps enemy default");
    foreach (string invalid in new[]{"spec","both","0","2","t ct","t;quit","human","t\nct"})
        Check(!PracticeBotRoster.TryResolveTeam(ownerTeam,invalid,out int team)&&team==0,"invalid team rejected: "+invalid);
}
foreach (int ownerTeam in new[]{0,1,4,-1})
    foreach (string choice in new[]{"","t","ct","ally","enemy"})
        Check(!PracticeBotRoster.TryResolveTeam(ownerTeam,choice,out _),"non-playing owner cannot place either team");
var mixed=new PracticeBotRoster();
var zombie=mixed.Reserve(a,placement,false,2,0)!;
var human=mixed.Reserve(a,placement with {Team=3,Health=100,Armor=0},false,2,.6)!;
Check(zombie!=null&&human!=null&&mixed.Owned(a).Select(e=>e.Placement.Team).Order().SequenceEqual(new[]{2,3}),"one owner can reserve both teams at once");
Check(mixed.Reserve(a,placement,false,2,1)==null&&mixed.Reserve(a,placement with {Team=3},false,2,1)==null,"mixed teams share the same two-Bot pending quota");
var otherMixed=mixed.Reserve(b,placement with {Team=3},false,2,1)!;
Check(otherMixed!=null,"mixed-team quotas remain per connection");
Check(mixed.Reserve(a,placement with {Team=3},true,2,1)!=null,"owner can add third Bot on either team");
foreach(var e in mixed.Owned(a).ToArray())mixed.Retire(e);
Check(mixed.Entries.Single()==otherMixed,"clearing mixed teams never removes another owner's Bot");
Check(human.Placement.Team==3&&human.Placement.Health==100&&zombie.Placement.Team==2&&zombie.Placement.Armor==2500,"team health armor snapshots stay independent");
Check(roster.Reserve(b,placement with {Team=1},true,2,1)==null,"spectator target rejected");
Check(roster.Reserve(b,placement with {Position=new(float.NaN,0,0)},true,2,1)==null,"nonfinite origin rejected");
var stats=new PracticeBotStatistics(100,50,0);
stats.Sample(.1,.5f); stats.Sample(.2,.5f);
Check(stats.SlowSeconds==0,"stationary/pre-damage low movement never reports hit slow");
stats.Hurt(70,40,30,true,.3,.6f);
stats.Sample(.4,.6f); stats.Sample(.5,1);
Check(Math.Abs(stats.SlowSeconds-.2)<.00001,"slow time integrates only damaged intervals");
stats.Sample(.5,1); Check(Math.Abs(stats.SlowSeconds-.2)<.00001,"duplicate tick does not count twice");
Check(stats.Hits==1 && stats.Headshots==1 && stats.ArmorLoss==10,"hit/head/armor statistics");
stats.Hurt(0,20,300,false,.6,.5f);
Check(stats.EffectiveHealthLoss==100 && stats.EventDamage==330,"overkill event damage differs from actual health loss");
stats.Sample(5,.5f); Check(Math.Abs(stats.SlowSeconds-.2)<.00001,"long sampling gap not invented as slow seconds");
stats.Sample(double.NaN,0); stats.Sample(5.1,float.NaN);
Check(double.IsFinite(stats.SlowSeconds) && stats.LowestVelocityModifier==.5f,"invalid sample cannot poison statistics");
foreach(string cmd in new[]{"add","hp","move","kill","clear","status"}) Check(PermissionPolicy.IsPublicCommand("css_ze_bot_"+cmd),"public bot command "+cmd);
roster.PruneRetired(30);
Check(roster.ByName(two.Name)==null && roster.ByName(one.Name)==one,"old tombstones pruned without removing living bots");
roster.Clear();Check(!roster.Entries.Any(),"map reset clears roster");
string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
string service=File.ReadAllText(Path.Combine(root,"src/PracticeBotService.cs"));
Check(!service.Contains("BotAllowActive = false") && !service.Contains("bot.AllowActive = false"),"stationary targets must not disable command/physics simulation");
Check(!service.Contains("pawn.Flags |= frozen")&&service.Contains("update.Hook(BotDecisionPost,HookMode.Post)"),"stationary targets suppress AI inputs after decisions without frozen damage immunity");
Check(service.Contains("\"weapon_ak47\" : \"weapon_m4a1\""),"both practice target teams receive a rifle");
Check(!service.Contains("bot_stop ") && !service.Contains("bot_dont_shoot "),"no bot-wide behaviour cvar mutations");
bool rejected=false;try{PracticeBotNativeData.Validate([0,1,2]);}catch(InvalidDataException){rejected=true;}
Check(rejected,"unverified engine cannot install a Bot hook");
foreach(string file in args){PracticeBotNativeData.Validate(File.ReadAllBytes(file));Check(true,"installed engine and unique Bot Update signature verified");}
Console.WriteLine($"PASS {passed} practice-bot tests");
