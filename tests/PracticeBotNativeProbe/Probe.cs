using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Plugin.Host;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

public sealed partial class PracticeBotNativeProbe : BasePlugin
{
    public override string ModuleName=>"PracticeBotNativeProbe - isolated 27025 only";
    public override string ModuleVersion=>"1.1";
    private readonly List<object> _results=new();
    private static object Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(o)!;
    private static object? Call(object o,string name,params object[] a)=>o.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance)!.Invoke(o,a);
    private static object Id(object service,CCSPlayerController p)=>Call(service,"Identity",p)!;
    private static object Plugin()=>((IPluginManager)Field(Application.Instance,"_pluginManager")).GetLoadedPlugins().Single(p=>p.Plugin.GetType().FullName=="ZePractice.ZePracticePlugin").Plugin;
    private static void Guard()
    {
        if(ConVar.Find("hostport")?.GetPrimitiveValue<int>()!=27025 || !Server.GameDirectory.Contains("ZePracticeTest-20261016") || Utilities.GetPlayers().Any(p=>!p.IsBot && !p.IsHLTV))
            throw new InvalidOperationException("Isolated test server only; no human players allowed.");
    }
    private void Check(string name,bool pass,object? data=null)
    {
        var result=new{name,pass,data}; _results.Add(result);
        Server.PrintToConsole("PB_PROBE "+JsonSerializer.Serialize(result));
        File.WriteAllText(Path.Combine(ModuleDirectory,"results.json"),JsonSerializer.Serialize(_results,new JsonSerializerOptions{WriteIndented=true}));
        if(!pass)throw new Exception(name);
    }
    private void Later(float seconds,Action action)=>AddTimer(seconds,()=>{Guard();try{action();}catch(Exception e){Check("unexpected-exception",false,e.ToString());}},TimerFlags.STOP_ON_MAPCHANGE);
    private static CCSPlayerController[] Targets()=>Utilities.GetPlayers().Where(p=>p.IsBot && (bool)Call(Field(Plugin(),"_practiceBots"),"IsPracticeBot",p)!).ToArray();
    private static Vector3 Origin(CCSPlayerController p) { var o=p.PlayerPawn.Value!.AbsOrigin!;return new(o.X,o.Y,o.Z); }
    public override void Load(bool hotReload)
    {
        RegisterMovementProbe();
        RegisterShootingProbe();
        RegisterCancelProbe();
        RegisterPoseProbe();
        RegisterTeamProbe();
        AddCommand("css_pb_probe_status","Native practice diagnostic",(p,c)=>{
            if(p!=null)return;Guard();
            Server.PrintToConsole(JsonSerializer.Serialize(new {cachedMax=Server.MaxPlayers,nativeMax=NativeAPI.GetMaxClients(),players=Utilities.GetPlayers().Select(p=>new{p.Slot,p.PlayerName,p.TeamNum,p.PawnIsAlive,hp=p.PlayerPawn.Value?.Health,armor=p.PlayerPawn.Value?.ArmorValue,allow=p.PlayerPawn.Value?.BotAllowActive,ai=p.PlayerPawn.Value?.Bot?.AllowActive})}));
            Server.PrintToConsole((string)Call(Field(Plugin(),"_practiceBots"),"Diagnostics",new object[]{null!})!);
        });
        AddCommand("css_pb_probe","Native Bot lifecycle regression",(p,c)=>{
            if(p!=null)return;Guard();_results.Clear();
            var service=Field(Plugin(),"_practiceBots");Call(service,"Reset","fixture");
            var a=Utilities.GetPlayers().First(p=>p.IsBot && !p.PlayerName.StartsWith("ZEPB_") && p.TeamNum==3 && p.PawnIsAlive);
            var b=Utilities.GetPlayers().First(p=>p.IsBot && !p.PlayerName.StartsWith("ZEPB_") && p.TeamNum==2 && p.PawnIsAlive);
            ((IDictionary)Field(service,"_health"))[Id(service,a)]=12345;
            ((IDictionary)Field(service,"_health"))[Id(service,b)]=1234;
            var start=Origin(a);
            Check("first-request-accepted",(bool)Call(service,"Request",a,false)!);
            a.PlayerPawn.Value!.Teleport(start+new Vector3(100,0,0));
            Later(1,()=>{
                Check("first-bot-registered",Targets().Length==1,new {diagnostics=Call(service,"Diagnostics",new object[]{null!}),players=Utilities.GetPlayers().Select(p=>new{p.PlayerName,p.Slot,p.TeamNum,p.PawnIsAlive}).ToArray()});
                var target=Targets().Single();var pawn=target.PlayerPawn.Value!;
                Check("opposite-zombie-hp-snapshot",target.TeamNum==2 && pawn.Health==12345 && pawn.MaxHealth==12345,new{target.TeamNum,pawn.Health,pawn.MaxHealth});
                Check("position-captured-at-command-not-spawn",Vector3.Distance(Origin(target),start)<5,new{actual=Origin(target).ToString(),expected=start.ToString()});
                Check("zombie-armor-helmet",pawn.ArmorValue==2500 && pawn.ItemServices!.As<CCSPlayer_ItemServices>().HasHelmet);
                Check("simulation-active-without-frozen-damage-immunity",pawn.BotAllowActive && pawn.Bot?.AllowActive==true && (pawn.Flags & (1u << 5)) == 0);
                Check("second-request-accepted",(bool)Call(service,"Request",a,false)!);
                Later(.7f,()=>{
                    Check("third-ordinary-request-rejected",!(bool)Call(service,"Request",a,false)!);
                    Check("other-owner-has-separate-quota",(bool)Call(service,"Request",b,false)!);
                    Later(1,()=>{
                        Check("opposite-human-custom-health",Targets().Any(p=>p.TeamNum==3 && p.PlayerPawn.Value?.Health==1234));
                        var entries=((IEnumerable)Field(service,"_roster").GetType().GetProperty("Entries")!.GetValue(Field(service,"_roster"))!).Cast<object>().ToArray();
                        var enemy=entries.Single(e=>e.GetType().GetProperty("Owner")!.GetValue(e)!.Equals(Id(service,b)));
                        var enemyId=enemy.GetType().GetProperty("Id")!.GetValue(enemy)!.ToString()!;
                        var before=Targets().Select(t=>t.EntityHandle.Raw).ToArray();Call(service,"Kill",a,enemyId);
                        Check("cannot-kill-other-owner-id",Targets().Select(t=>t.EntityHandle.Raw).SequenceEqual(before));
                        Check("server-owner-third-allowed",(bool)Call(service,"Request",a,true)!);
                        Later(1,()=>{
                            Check("owner-bypass-created-third",Targets().Length==4);
                            Call(service,"Move",a,"all");
                            var my=Targets().Where(p=>p.TeamNum==2).ToArray();
                            Check("move-all-own-to-current-position",my.All(t=>Vector3.Distance(Origin(t),Origin(a))<5));
                            var victim=my[0];var handle=victim.EntityHandle.Raw;
                            victim.PlayerPawn.Value!.Teleport(Origin(a)+new Vector3(150,0,0));
                            Damage(victim,a,200);
                            Later(.4f,()=>{
                                Check("real-engine-hurt-reduces-health",victim.IsValid && victim.PlayerPawn.Value!.Health<12345,victim.IsValid?victim.PlayerPawn.Value!.Health:null);
                                var stats=Stats(service,victim);
                                Check("real-hurt-recorded",(long)stats.GetType().GetProperty("EffectiveHealthLoss")!.GetValue(stats)!>0 && (int)stats.GetType().GetProperty("Hits")!.GetValue(stats)!>0);
                                Damage(victim,a,1000000);
                                Later(2,()=>{
                                    Check("death-disconnects-no-respawn-or-zombie-reset",!Targets().Any(t=>t.EntityHandle.Raw==handle));
                                    Check("lethal-damage-kept-in-statistics",(long)stats.GetType().GetProperty("EffectiveHealthLoss")!.GetValue(stats)! == 12345,stats);
                                    var other=Targets().Single(t=>t.TeamNum==3);uint otherHandle=other.EntityHandle.Raw;
                                    Call(service,"Kill",a,"");
                                    Later(2,()=>{
                                        Check("kill-all-removes-only-owner-bots",Targets().Length==1 && Targets()[0].EntityHandle.Raw==otherHandle);
                                        Check("no-quota-replacement-bots",Utilities.GetPlayers().Count==3,Utilities.GetPlayers().Select(t=>t.PlayerName).ToArray());
                                        Call(service,"ClearOwner",Id(service,b),"probe-delete",false);
                                        Later(2,()=>{
                                            Check("clear-all-own-and-restore-quota",Targets().Length==0 && Utilities.GetPlayers().Count==2);
                                            // Immediate cancellation before the command buffer executes.
                                            Check("late-spawn-request",(bool)Call(service,"Request",a,false)!);
                                            Call(service,"ClearOwner",Id(service,a),"probe-cancel",false);
                                            Later(2,()=>{
                                                Check("cancelled-pending-does-not-leak",Targets().Length==0 && Utilities.GetPlayers().Count==2);
                                                Server.PrintToConsole("PB_PROBE_COMPLETE "+_results.Count);
                                            });
                                        });
                                    });
                                });
                            });
                        });
                    });
                });
            });
        });
    }
    private static object Stats(object service,CCSPlayerController target)
    {
        var roster=Field(service,"_roster");var entry=Call(roster,"ByBot",Id(service,target))!;
        long id=(long)entry.GetType().GetProperty("Id")!.GetValue(entry)!;
        var live=((IDictionary)Field(service,"_live"))[id]!;
        return live.GetType().GetField("Stats")!.GetValue(live)!;
    }
    private void Damage(CCSPlayerController victim,CCSPlayerController attacker,int damage)
    {
        var pawn=victim.PlayerPawn.Value!;
        var point=Utilities.CreateEntityByName<CPointHurt>("point_hurt")!;
        // Once gravity works, the feet sit at ground level: use the torso as the radius/LOS
        // origin, not a point that can be fractionally inside the floor.
        using var kv=new CEntityKeyValues();kv.SetInt("Damage",damage);kv.SetInt("DamageType",2);kv.SetFloat("DamageRadius",96);
        point.DispatchSpawn(kv);point.Teleport(Origin(victim)+new Vector3(0,0,32));
        Check("damage-fixture-native-fields",point.Damage==damage && point.Radius==96,new{probeDamage=point.Damage,point.Radius,point.StrTarget,pawn.TakesDamage,pawn.ImmuneToGunGameDamageTime,pawn.TakeDamageFlags,now=Server.CurrentTime});
        try{point.AcceptInput("Hurt",attacker.PlayerPawn.Value,attacker.PlayerPawn.Value);}
        finally{point.Remove();}
    }
}
