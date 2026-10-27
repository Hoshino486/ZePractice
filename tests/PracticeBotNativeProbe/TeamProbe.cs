using System.Collections;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

public sealed partial class PracticeBotNativeProbe
{
    private void RegisterTeamProbe()
    {
        AddCommand("css_pb_team_probe", "Isolated mixed-team Bot creation, quotas and ownership", (p,c) =>
        {
            if (p != null) return;
            Guard(); _results.Clear();
            if(Server.MapName!="de_dust2")throw new InvalidOperationException("Team fixture uses verified flat outdoor coordinates on de_dust2 only.");
            var service=Field(Plugin(),"_practiceBots");
            Call(service,"Reset","team-fixture");
            var owners=Utilities.GetPlayers().Where(x=>x.IsBot&&x.PawnIsAlive&&!x.PlayerName.StartsWith("ZEPB_")).ToArray();
            Check("team-fixture-two-owners",owners.Length==2&&owners.Select(x=>x.TeamNum).Distinct().Count()==2);
            var a=owners.Single(x=>x.TeamNum==3);var b=owners.Single(x=>x.TeamNum==2);
            var flags=owners.Select(x=>x.PlayerPawn.Value!.Flags).ToArray();
            var positions=owners.Select(Origin).ToArray();
            // Ordinary fixture Bots may still have movement queued when frozen. Use flat,
            // settled test positions with zero velocity; compare against the request snapshot,
            // not against a potentially moving owner after asynchronous creation.
            for(int i=0;i<owners.Length;i++)
            {
                owners[i].PlayerPawn.Value!.Flags|=1u<<5;
                owners[i].PlayerPawn.Value!.Teleport(new Vector3(-980+128*i,-754,120),null,Vector3.Zero);
            }
            var health=(IDictionary)Field(service,"_health");health.Remove(Id(service,a));health.Remove(Id(service,b));
            var roster=Field(service,"_roster");
            object Entry(CCSPlayerController bot)=>Call(roster,"ByBot",Id(service,bot))!;
            CCSPlayerController[] Mine(CCSPlayerController owner)=>Targets().Where(bot=>Entry(bot).GetType().GetProperty("Owner")!.GetValue(Entry(bot))!.Equals(Id(service,owner))).ToArray();
            string Number(CCSPlayerController bot)=>Entry(bot).GetType().GetProperty("Id")!.GetValue(Entry(bot))!.ToString()!;
            Check("spectator-target-rejected",!(bool)Call(service,"RequestForTeam",a,false,1)!);
            var requestedOrigin=Origin(a);
            Call(service,"AddBot",a,"CT");
            Later(1.2f,()=>
            {
                var human=Mine(a).Single();var hp=human.PlayerPawn.Value!;
                Check("ct-owner-can-create-ct-ally",human.TeamNum==3);
                Check("chosen-human-default-health-not-owner-enemy-default",hp.Health==100&&hp.MaxHealth==100&&hp.ArmorValue==0);
                Check("chosen-human-holds-rifle",hp.WeaponServices?.ActiveWeapon.Value?.DesignerName=="weapon_m4a1");
                Check("chosen-human-created-at-request-position",Vector3.Distance(Origin(human),requestedOrigin)<5,
                    new{expected=requestedOrigin.ToString(),actual=Origin(human).ToString(),ownerNow=Origin(a).ToString()});
                Call(service,"AddBot",a,"t");
                Later(1.2f,()=>
                {
                    Check("both-teams-coexist-for-one-owner",Mine(a).Length==2&&Mine(a).Select(x=>x.TeamNum).Order().SequenceEqual(new byte[]{2,3}));
                    var zombie=Mine(a).Single(x=>x.TeamNum==2);var zp=zombie.PlayerPawn.Value!;
                    Check("chosen-zombie-default-health-and-armor",zp.Health==35000&&zp.MaxHealth==35000&&zp.ArmorValue==2500);
                    Check("chosen-zombie-has-helmet-and-rifle",zp.ItemServices!.As<CCSPlayer_ItemServices>().HasHelmet&&zp.WeaponServices?.ActiveWeapon.Value?.DesignerName=="weapon_ak47");
                    Check("mixed-third-human-request-rejected",!(bool)Call(service,"RequestForTeam",a,false,3)!);
                    Check("mixed-third-zombie-request-rejected",!(bool)Call(service,"RequestForTeam",a,false,2)!);
                    Call(service,"AddBot",b,"ally");
                    Later(1.2f,()=>
                    {
                        var other=Mine(b).Single();uint otherHandle=other.EntityHandle.Raw;
                        Check("t-owner-can-create-t-ally-independent-quota",other.TeamNum==2&&Mine(a).Length==2);
                        Call(service,"Kill",a,Number(other));
                        Check("cannot-kill-other-owner-same-or-opposite-team",other.IsValid&&other.PawnIsAlive&&Targets().Length==3);
                        health[Id(service,a)]=777;
                        Check("owner-override-allows-third-chosen-team",(bool)Call(service,"RequestForTeam",a,true,3)!);
                        Later(1.2f,()=>
                        {
                            var custom=Mine(a).Single(x=>x.PlayerPawn.Value!.Health==777);
                            Check("custom-health-applies-to-selected-team",custom.TeamNum==3&&custom.PlayerPawn.Value!.MaxHealth==777&&Mine(a).Length==3);
                            var snapshot=Mine(a).ToDictionary(x=>x.EntityHandle.Raw,x=>(x.TeamNum,x.PlayerPawn.Value!.Health,x.PlayerPawn.Value.ArmorValue));
                            var beforeMove=Mine(a).ToDictionary(x=>x.EntityHandle.Raw,Origin);
                            a.PlayerPawn.Value!.Teleport(Origin(a)+new Vector3(64,0,0));
                            Call(service,"Move",a,"");
                            Check("empty-move-opens-selector-without-teleporting",Mine(a).All(x=>Vector3.Distance(Origin(x),beforeMove[x.EntityHandle.Raw])<.01f));
                            Call(service,"Move",a,Number(other));
                            Check("foreign-bot-number-cannot-fall-back-to-all",Mine(a).All(x=>Vector3.Distance(Origin(x),beforeMove[x.EntityHandle.Raw])<.01f));
                            Call(service,"Move",a,"invalid");
                            Check("invalid-bot-number-never-moves-all",Mine(a).All(x=>Vector3.Distance(Origin(x),beforeMove[x.EntityHandle.Raw])<.01f));
                            Call(service,"Move",a,Number(custom));
                            Check("numbered-move-teleports-only-selected-bot",Vector3.Distance(Origin(custom),Origin(a))<5&&
                                Mine(a).Where(x=>x.EntityHandle.Raw!=custom.EntityHandle.Raw).All(x=>Vector3.Distance(Origin(x),beforeMove[x.EntityHandle.Raw])<.01f));
                            Call(service,"Move",a,"all");
                            Check("move-both-teams-to-owner",Mine(a).All(x=>Vector3.Distance(Origin(x),Origin(a))<5));
                            Check("move-preserves-selected-team-health-armor",Mine(a).All(x=>snapshot[x.EntityHandle.Raw]==(x.TeamNum,x.PlayerPawn.Value!.Health,x.PlayerPawn.Value.ArmorValue)));
                            uint killed=custom.EntityHandle.Raw;Call(service,"Kill",a,Number(custom));
                            Later(1.5f,()=>
                            {
                                Check("chosen-ally-death-removes-without-respawn",!Targets().Any(x=>x.EntityHandle.Raw==killed)&&Mine(a).Length==2);
                                Call(service,"ClearOwner",Id(service,a),"team-delete",false);
                                Later(1.5f,()=>
                                {
                                    Check("clear-removes-both-owned-teams-only",Targets().Length==1&&Targets()[0].EntityHandle.Raw==otherHandle);
                                    Call(service,"ClearOwner",Id(service,b),"team-delete",false);
                                    Later(1.5f,()=>
                                    {
                                        Check("mixed-team-cleanup-no-replacement-bots",Targets().Length==0&&Utilities.GetPlayers().Count==2);
                                        for(int i=0;i<owners.Length;i++)if(owners[i].PlayerPawn.Value is {IsValid:true} pawn)
                                        {pawn.Teleport(positions[i],null,Vector3.Zero);pawn.Flags=flags[i];}
                                        health.Remove(Id(service,a));health.Remove(Id(service,b));
                                        File.Copy(Path.Combine(ModuleDirectory,"results.json"),Path.Combine(ModuleDirectory,"team-results.json"),true);
                                        Server.PrintToConsole("PB_TEAM_PROBE_COMPLETE "+_results.Count);
                                    });
                                });
                            });
                        });
                    });
                });
            });
        });
    }
}
