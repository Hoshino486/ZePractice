using System.Collections;
using System.Drawing;
using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

public sealed partial class CommanderNativeProbe
{
    private void RegisterGameplay151Probe()
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(m => m.AddResource("characters/models/ctm_sas/ctm_sas.vmdl"));
        AddCommand("css_gameplay151_probe", "Isolated helmet, leader proxy and movement HUD regression", (caller, command) =>
        {
            if (caller != null) return; Guard(); _results.Clear();
            var plugin = Plugin(); var gameplay = Field(plugin, "_publicGameplay"); var commander = Field(plugin, "_commander");
            var hud = Field(plugin, "_movementHud");
            var a = Utilities.GetPlayers().First(p => p.IsBot && p.TeamNum == 3 && p.PawnIsAlive);
            var zombie = Utilities.GetPlayers().First(p => p.IsBot && p.TeamNum == 2 && p.PawnIsAlive);
            var zp = zombie.PlayerPawn.Value!; var hp = a.PlayerPawn.Value!;
            var zi = zp.ItemServices!.As<CCSPlayer_ItemServices>();
            var hi = hp.ItemServices!.As<CCSPlayer_ItemServices>();
            Record("zombie-spawn-helmet-and-2500-armor", zi.HasHelmet && zombie.PawnHasHelmet && zp.ArmorValue == 2500,
                new { helmet=zi.HasHelmet, summary=zombie.PawnHasHelmet, armor=zp.ArmorValue });
            zi.HasHelmet = false; zombie.PawnHasHelmet = false; hi.HasHelmet = false; a.PawnHasHelmet = false;
            Later(.3f, () => {
                Record("zombie-helmet-repaired-after-overwrite", zi.HasHelmet && zombie.PawnHasHelmet);
                Record("human-helmet-not-forced", !hi.HasHelmet && !a.PawnHasHelmet);
                Record("zombie-controller-armor-synchronized", zombie.PawnArmor == zp.ArmorValue);
            });

            Call(commander, "Release", false); Record("leader-acquires-for-proxy-test", (bool)Call(commander,"TryAcquire",a)!);
            var proxy = Field(commander, "_leaderGlow"); var originalOutline = Prop<CDynamicProp>(proxy,"Outline");
            string originalModel = hp.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName;
            hp.SetModel("characters/models/ctm_sas/ctm_sas.vmdl"); Call(commander, "ApplyLeaderGlow");
            var changed = Prop<CDynamicProp>(proxy,"Outline");
            Record("leader-model-change-rebuilds-outline", changed.EntityHandle.Raw != originalOutline.EntityHandle.Raw && changed.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName == "characters/models/ctm_sas/ctm_sas.vmdl");
            hp.SetModel(originalModel); Call(commander, "ApplyLeaderGlow");
            var restored = Prop<CDynamicProp>(proxy,"Outline");
            Record("leader-model-restored-follow-chain", (bool)Call(proxy,"Linked",hp)! && restored.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName == originalModel);
            restored.Remove(); Later(.35f, () => {
                var replacement = Prop<CDynamicProp>(proxy,"Outline");
                Record("leader-deleted-proxy-repaired", replacement.IsValid && replacement.EntityHandle.Raw != restored.EntityHandle.Raw && (bool)Call(proxy,"Linked",hp)!);
                Record("leader-old-model-proxies-deleted", !changed.IsValid && !originalOutline.IsValid);
            });

            // Explicit staged-asset run only. Never label this as a Workshop/client download.
            if (command.GetArg(1) == "candidate")
            {
                string vpk = Path.Combine(ModuleDirectory, "movement-candidate.vpk");
                var resourcesType = plugin.GetType().Assembly.GetType("ZePractice.CommanderResources")!;
                string[] required = (string[])hud.GetType().GetField("Required")!.GetValue(null)!;
                var availability = resourcesType.GetMethod("Probe")!.Invoke(null, [vpk, required])!;
                Record("isolated-candidate-vpk-crc", Prop<bool>(availability,"Ready"));
                SetField(hud, "_resources", availability);
            }
            Later(.5f, () => {
                using var diag = JsonDocument.Parse((string)Call(hud,"Diagnostics",new object[]{null!})!);
                bool ready = diag.RootElement.GetProperty("resources").GetProperty("Ready").GetBoolean();
                var sessions = diag.RootElement.GetProperty("sessions").EnumerateArray().ToArray();
                Record("both-teams-telemetry-sampled", sessions.Length >= 3 && sessions.All(s=>s.GetProperty("reading").GetProperty("GroundSpeed").GetSingle() >= 0), diag.RootElement.Clone());
                if (ready)
                {
                    var layouts = Utilities.FindAllEntitiesByDesignerName<CCSCustomHudLayout>("custom_hud_layout").Where(e=>e.Entity?.Name?.StartsWith("ze_movement_hud_")==true).ToArray();
                    Record("personal-hud-one-per-player", layouts.Length == sessions.Length && layouts.All(e=>!e.Observable));
                    Record("personal-hud-text-updated", sessions.All(s=>s.GetProperty("speed").GetString()!.Contains("u/s") && s.GetProperty("jump").GetString()!.Contains("跳高")));
                    var session = Call(hud,"Get",a)!;
                    session.GetType().GetField("Enabled")!.SetValue(session,false);
                    Later(.2f,()=>{
                        Record("personal-hud-off-removes-only-owner", session.GetType().GetField("Hud")!.GetValue(session)==null && Utilities.FindAllEntitiesByDesignerName<CCSCustomHudLayout>("custom_hud_layout").Count(e=>e.Entity?.Name?.StartsWith("ze_movement_hud_")==true)==layouts.Length-1);
                        session.GetType().GetField("Enabled")!.SetValue(session,true);
                    });
                }
                else Record("old-workshop-never-spawns-missing-hud", sessions.All(s=>s.GetProperty("entity").ValueKind==JsonValueKind.Null));
                Record("movement-hud-no-native-errors", diag.RootElement.GetProperty("errors").GetArrayLength()==0);
            });
            Later(1.2f,()=>{
                var state=Call(gameplay,"State",a)!;
                Call(gameplay,"QueueTeamReset",a,state,2,true);
                Later(2,()=>{
                    var pawn=a.PlayerPawn.Value!;
                    Record("ct-to-zombie-reset-has-helmet-armor-health",a.TeamNum==2 && pawn.Health==35000 && pawn.ArmorValue==2500 && pawn.ItemServices!.As<CCSPlayer_ItemServices>().HasHelmet && a.PawnHasHelmet);
                    Record("team-switch-cleans-commander-proxies",Prop<CDynamicProp?>(proxy,"Outline")==null && !Utilities.FindAllEntitiesByDesignerName<CDynamicProp>("prop_dynamic").Any(e=>e.Entity?.Name?.StartsWith("ze_commander_leader_")==true));
                    Call(gameplay,"QueueTeamReset",a,Call(gameplay,"State",a)!,3,true);
                    Later(2,()=>{
                        Record("zombie-to-human-unrestricted",a.TeamNum==3 && a.PawnIsAlive);
                        var human=a.PlayerPawn.Value!;
                        human.ItemServices!.As<CCSPlayer_ItemServices>().HasHelmet=false;
                        human.CommitSuicide(false,true);
                        Later(2,()=>{
                            var infected=a.PlayerPawn.Value!;
                            Record("death-converts-to-zombie-with-helmet",a.TeamNum==2 && a.PawnIsAlive && infected.ArmorValue==2500 && infected.ItemServices!.As<CCSPlayer_ItemServices>().HasHelmet);
                            Call(gameplay,"QueueTeamReset",a,Call(gameplay,"State",a)!,3,true);
                            Later(2,()=>{
                                Record("test-human-restored",a.TeamNum==3 && a.PawnIsAlive);
                                Server.PrintToConsole("GAMEPLAY151_PROBE COMPLETE");
                            });
                        });
                    });
                });
            });
        });
    }
}
