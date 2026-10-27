using System.Collections;
using System.Numerics;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

public sealed partial class PracticeBotNativeProbe
{
    private int _probeShots;
    private CCSPlayerPawn? _tagVictim;
    private float _observedMinimum = 1;
    private readonly List<object> _hurtSamples = new();
    private CCSPlayerPawn? _fixtureShooter;
    private void RegisterShootingProbe()
    {
        RegisterEventHandler<EventWeaponFire>((ev,info)=>{_probeShots++;return HookResult.Continue;});
        RegisterListener<Listeners.OnTick>(()=>{
            if(_tagVictim is not {IsValid:true} victim)return;
            _observedMinimum=Math.Min(_observedMinimum,victim.VelocityModifier);
            if(_fixtureShooter is not {IsValid:true} shooter || shooter.Bot is not {} bot)return;
            // Aim at the ACTUAL settled chest, not the old unsimulated spawn height. Do not let
            // the AI walk off this controlled shooting lane while counting weapon_fire events.
            var a=shooter.AbsOrigin!;var b=victim.AbsOrigin!;
            float dx=b.X-a.X,dy=b.Y-a.Y,dz=b.Z+40-(a.Z+64);
            float yaw=MathF.Atan2(dy,dx)*180/MathF.PI,pitch=-MathF.Atan2(dz,MathF.Sqrt(dx*dx+dy*dy))*180/MathF.PI;
            shooter.Teleport(angles:new Vector3(pitch,yaw,0));
            bot.LookPitch=pitch;bot.LookYaw=yaw;bot.ForwardSpeed=bot.LeftSpeed=bot.VerticalSpeed=0;
            bot.ButtonFlags=1; // IN_ATTACK; only this isolated fixture shooter
        });
        RegisterEventHandler<EventPlayerHurt>((ev,info)=>{if(_tagVictim is {IsValid:true} && ev.Userid?.PlayerPawn.Raw==_tagVictim.EntityHandle.Raw)_hurtSamples.Add(new{health=_tagVictim.Health,modifier=_tagVictim.VelocityModifier,ev.DmgHealth,ev.Weapon});return HookResult.Continue;});
        AddCommand("css_pb_shoot_probe", "Isolated: real bullets, tagging and regeneration regression", (p,c) => {
            if(p!=null)return; Guard(); _results.Clear(); _probeShots=0;_hurtSamples.Clear();_observedMinimum=1;
            var service=Field(Plugin(),"_practiceBots"); Call(service,"Reset","shoot-fixture");
            var a=Utilities.GetPlayers().First(p=>p.IsBot&&p.TeamNum==3&&p.PawnIsAlive);
            ((IDictionary)Field(service,"_health"))[Id(service,a)]=100000;
            Check("shooting-target-request",(bool)Call(service,"Request",a,false)!);
            Later(1,()=>{
                var target=Targets().Single(); var pawn=target.PlayerPawn.Value!;
                _tagVictim=pawn;
                var shooter=a.PlayerPawn.Value!;
                // The second ordinary fixture bot starts in this same spawn lane. Keep it out
                // of the shooting experiment; otherwise its knife can kill the 100HP shooter.
                foreach(var other in Utilities.GetPlayers().Where(x=>x.IsBot&&x!=a&&!x.PlayerName.StartsWith("ZEPB_")&&x.PawnIsAlive))
                    other.PlayerPawn.Value!.Flags|=1u<<5;
                shooter.Flags&=~(1u<<5);shooter.TakesDamage=false;
                if(Server.MapName!="de_dust2")throw new InvalidOperationException("Shooting lane verified for de_dust2 only.");
                var center=new Vector3(-980,-754,124);
                var aim=new Vector3(-857,-738,124);
                var delta=aim-center; var angles=new Vector3(-MathF.Atan2(delta.Z,MathF.Sqrt(delta.X*delta.X+delta.Y*delta.Y))*180/MathF.PI,MathF.Atan2(delta.Y,delta.X)*180/MathF.PI,0);
                shooter.Teleport(center,angles,Vector3.Zero);
                pawn.Teleport(aim,new Vector3(0,180,0),Vector3.Zero);
                shooter.ItemServices!.As<CCSPlayer_ItemServices>().RemoveWeapons(); a.GiveNamedItem("weapon_m4a1");
                if(shooter.Bot is {} bot){bot.IsAttacking=true;bot.AllowActive=true;bot.Enemy.Raw=pawn.EntityHandle.Raw;bot.IsEnemyVisible=true;}
                _fixtureShooter=shooter;
                Server.ExecuteCommand("bot_stop 0; bot_dont_shoot 0; bot_zombie 0");
                Later(8,()=>{
                    Server.ExecuteCommand("bot_stop 1; bot_dont_shoot 1");
                    _fixtureShooter=null;if(shooter.Bot is {} stop)stop.ButtonFlags=0;
                    if(shooter.IsValid)shooter.TakesDamage=true;
                    var active=shooter.WeaponServices?.ActiveWeapon.Value?.DesignerName;
                    shooter.ItemServices!.As<CCSPlayer_ItemServices>().RemoveWeapons();
                    var stats=Stats(service,target); int health=pawn.Health,armor=pawn.ArmorValue;
                    Check("real-bullets-record-damage-and-hits",health<100000 && (int)stats.GetType().GetProperty("Hits")!.GetValue(stats)!>0,new{health,armor,stats,shots=_probeShots,weapon=active,from=center.ToString(),to=aim.ToString(),actual=Origin(target).ToString()});
                    double measured=(double)stats.GetType().GetProperty("SlowSeconds")!.GetValue(stats)!;
                    Check("tagging-statistics-match-independent-engine-samples",_observedMinimum<.999f ? measured>0 : measured==0,new{independentMinimum=_observedMinimum,stats,hurtSamples=_hurtSamples.ToArray()});
                    Later(12,()=>{
                        Check("no-zombie-health-or-armor-regeneration",target.IsValid&&pawn.Health==health&&pawn.ArmorValue==armor,new{health=pawn.Health,armor=pawn.ArmorValue});
                        Check("tagging-recovery-not-frozen",pawn.VelocityModifier>=.999f,pawn.VelocityModifier);
                        Call(service,"ClearOwner",Id(service,a),"shoot-cleanup",false);
                        _tagVictim=null;
                        Server.ExecuteCommand("bot_dont_shoot 0");
                        File.Copy(Path.Combine(ModuleDirectory,"results.json"),Path.Combine(ModuleDirectory,"shooting-results.json"),true);
                        Server.PrintToConsole("PB_SHOOT_PROBE_COMPLETE");
                    });
                });
            });
        });
    }
}
