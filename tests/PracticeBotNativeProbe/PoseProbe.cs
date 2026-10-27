using System.Numerics;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

public sealed partial class PracticeBotNativeProbe
{
    private int _targetShots;
    private static object Pose(CCSPlayerController p)
    {
        var pawn=p.PlayerPawn.Value!;
        var o=pawn.AbsOrigin!;var v=pawn.AbsVelocity;
        return new {p.Slot,p.TeamNum,p.PawnIsAlive,pawn.Health,pawn.SimulationTime,
            pawn.TakesDamage,pawn.TakeDamageFlags,pawn.GunGameImmunity,pawn.ImmuneToGunGameDamageTime,now=Server.CurrentTime,
            controllerPawn=p.Pawn.Raw,playerPawn=p.PlayerPawn.Raw,pawnController=pawn.Controller.Raw,
            botPawn=pawn.Bot?.Player?.EntityHandle.Raw,botController=pawn.Bot?.Controller?.EntityHandle.Raw,
            pawn.Flags,actualMove=pawn.ActualMoveType.ToString(),pawn.GravityScale,pawn.GravityDisabled,pawn.GravityActuallyDisabled,
            origin=new[]{o.X,o.Y,o.Z},velocity=new[]{v.X,v.Y,v.Z},ground=(pawn.Flags&1)!=0,
            frozen=(pawn.Flags&(1u<<5))!=0,move=pawn.MoveType.ToString(),
            pawn.BotAllowActive,ai=pawn.Bot?.AllowActive,spawned=pawn.Bot?.HasSpawned,
            active=pawn.WeaponServices?.ActiveWeapon.Value?.DesignerName,
            weapons=pawn.WeaponServices?.MyWeapons.Select(w=>w.Value?.DesignerName).ToArray(),
            model=pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName};
    }
    private void RegisterPoseProbe()
    {
        AddCommand("css_pb_damage_inspect","Isolated targeted damage diagnostics",(p,c)=>{
            if(p!=null)return;Guard();foreach(var target in Targets()){
                var attacker=Utilities.GetPlayers().First(x=>x.PawnIsAlive&&x.TeamNum!=target.TeamNum&&!x.PlayerName.StartsWith("ZEPB_"));
                var before=Pose(target);Damage(target,attacker,200);var after=Pose(target);
                File.WriteAllText(Path.Combine(ModuleDirectory,"damage-"+target.TeamNum+".json"),JsonSerializer.Serialize(new{before,after},new JsonSerializerOptions{WriteIndented=true}));
            }
        });
        AddCommand("css_pb_pose_inspect","Isolated pose snapshot",(p,c)=>{if(p!=null)return;Guard();var data=Utilities.GetPlayers().Where(x=>x.PawnIsAlive).Select(Pose).ToArray();File.WriteAllText(Path.Combine(ModuleDirectory,"pose-inspection.json"),JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true}));foreach(var item in data)Server.PrintToConsole(JsonSerializer.Serialize(item));});
        AddCommand("css_pb_pose_owners_away","Isolated separate overlapping owners",(p,c)=>{if(p!=null)return;Guard();foreach(var t in Utilities.GetPlayers().Where(x=>!x.PlayerName.StartsWith("ZEPB_")&&x.PawnIsAlive))t.PlayerPawn.Value!.Teleport(Origin(t)+new Vector3(200,0,0));});
        AddCommand("css_pb_pose_setpawn","Isolated bind player pawn",(p,c)=>{if(p!=null)return;Guard();foreach(var t in Targets())t.SetPawn(t.PlayerPawn.Value!);});
        AddCommand("css_pb_pose_release","Isolated let engine control targets",(p,c)=>{if(p!=null)return;Guard();var service=Field(Plugin(),"_practiceBots");service.GetType().GetField("_active",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(service,false);foreach(var t in Targets())t.PlayerPawn.Value!.Flags&=~(1u<<5);});
        RegisterEventHandler<EventWeaponFire>((ev,info)=>{if(ev.Userid?.PlayerName.StartsWith("ZEPB_")==true)_targetShots++;return HookResult.Continue;});
        AddCommand("css_pb_pose_probe","Isolated real simulation/landing/equip regression",(p,c)=>
        {
            if(p!=null)return;Guard();_results.Clear();_targetShots=0;
            if(Server.MapName!="de_dust2")throw new InvalidOperationException("Pose drop fixture is in the verified outdoor area of de_dust2 only.");
            bool keep=c.GetArg(1)=="keep";
            var service=Field(Plugin(),"_practiceBots");Call(service,"Reset","pose-fixture");
            var owners=Utilities.GetPlayers().Where(x=>x.IsBot&&x.PawnIsAlive&&!x.PlayerName.StartsWith("ZEPB_")).OrderBy(x=>x.TeamNum).Take(2).ToArray();
            Check("pose-fixture-both-teams",owners.Length==2&&owners.Select(x=>x.TeamNum).Distinct().Count()==2);
            var originals=owners.Select(x=>x.PlayerPawn.Value!.Flags).ToArray();
            // Fixture owners do not walk or fire while targets are tested; no bot-wide stop cvar.
            foreach(var owner in owners)owner.PlayerPawn.Value!.Flags|=1u<<5;
            Server.ExecuteCommand("bot_stop 0; bot_dont_shoot 0; bot_zombie 0");
            foreach(var owner in owners)Check("pose-request-"+owner.TeamNum,(bool)Call(service,"Request",owner,false)!);
            Later(1.5f,()=>
            {
                var targets=Targets();Check("pose-two-targets-created",targets.Length==2);
                // The other spawn is under a low roof: lifting there embeds the hull in the ceiling,
                // which is NOT a simulation failure. Use the observed clear outdoor spawn instead.
                var start=targets.Select((t,i)=>(t,position:new Vector3(-980+64*i,-754,120))).ToDictionary(x=>x.t.EntityHandle.Raw,x=>x.position);
                var simulation=targets.ToDictionary(t=>t.EntityHandle.Raw,t=>t.PlayerPawn.Value!.SimulationTime);
                foreach(var target in targets)target.PlayerPawn.Value!.Teleport(start[target.EntityHandle.Raw]+new Vector3(0,0,96),null,Vector3.Zero);
                var before=targets.Select(Pose).ToArray();
                Later(3,()=>
                {
                    var after=targets.Select(Pose).ToArray();
                    // Save BEFORE assertions, including failing old-build comparisons.
                    File.WriteAllText(Path.Combine(ModuleDirectory,"pose-observation.json"),JsonSerializer.Serialize(new{before,after,shots=_targetShots},new JsonSerializerOptions{WriteIndented=true}));
                    if(keep){Server.PrintToConsole("PB_POSE_KEEP "+JsonSerializer.Serialize(after));return;}
                    try
                    {
                        Check("pose-simulation-time-advances",targets.All(t=>t.PlayerPawn.Value!.SimulationTime>simulation[t.EntityHandle.Raw]+1),after);
                        Check("pose-both-teams-rifle-active",targets.All(t=>t.PlayerPawn.Value!.WeaponServices?.ActiveWeapon.Value?.DesignerName==(t.TeamNum==2?"weapon_ak47":"weapon_m4a1")),after);
                        Check("pose-falls-96-units-onto-ground",targets.All(t=>(t.PlayerPawn.Value!.Flags&1)!=0&&Math.Abs(Origin(t).Z-start[t.EntityHandle.Raw].Z)<5),after);
                        Check("pose-no-autonomous-horizontal-walk",targets.All(t=>Vector2.Distance(new(Origin(t).X,Origin(t).Y),new(start[t.EntityHandle.Raw].X,start[t.EntityHandle.Raw].Y))<2),after);
                        Check("pose-no-autonomous-shots",_targetShots==0,_targetShots);
                        Check("pose-model-is-real-player-luna",targets.All(t=>t.PlayerPawn.Value!.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName=="agents/fys/luna/luna.vmdl"));
                        Check("pose-ordinary-bots-still-simulating",owners.All(o=>o.PlayerPawn.Value!.BotAllowActive));
                        var ordinaryZombie=owners.Single(o=>o.TeamNum==2);
                        ordinaryZombie.GiveNamedItem("weapon_ak47");
                        Check("pose-ordinary-zombie-rifle-still-denied",!ordinaryZombie.PlayerPawn.Value!.WeaponServices!.MyWeapons.Any(w=>w.Value?.DesignerName=="weapon_ak47"));
                    }
                    finally
                    {
                        for(int i=0;i<owners.Length;i++)if(owners[i].IsValid)owners[i].PlayerPawn.Value!.Flags=originals[i];
                        Call(service,"Reset","pose-cleanup");
                        Server.ExecuteCommand("bot_stop 1; bot_dont_shoot 1");
                        File.Copy(Path.Combine(ModuleDirectory,"results.json"),Path.Combine(ModuleDirectory,"pose-results.json"),true);
                        Server.PrintToConsole("PB_POSE_PROBE_COMPLETE (animation appearance still requires a real client)");
                    }
                });
            });
        });
    }
}
