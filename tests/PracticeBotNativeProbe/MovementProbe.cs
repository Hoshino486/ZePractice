using System.Collections;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;

public sealed partial class PracticeBotNativeProbe
{
    private void RegisterMovementProbe()
    {
        AddCommand("css_movement_refresh_probe", "Isolated: refresh installed HUD resources while empty", (p,c) => {
            if(p!=null)return; Guard();
            if(Utilities.GetPlayers().Count!=0)throw new InvalidOperationException("Must be empty");
            Call(Plugin(),"RefreshMovementResourcesWhenEmpty");
        });
        AddCommand("css_movement_native_probe", "Isolated: native HUD state, not client visual acceptance", (p,c) => {
            if(p!=null)return; Guard(); _results.Clear();
            var service=Field(Plugin(),"_movementHud");
            var players=Utilities.GetPlayers().Where(p=>p.IsBot && p.PawnIsAlive).ToArray();
            Check("fixture-has-both-teams",players.Any(p=>p.TeamNum==2)&&players.Any(p=>p.TeamNum==3));
            using var diagnostic=JsonDocument.Parse((string)Call(service,"Diagnostics",new object[]{null!})!);
            Check("installed-resource-snapshot-ready",diagnostic.RootElement.GetProperty("resources").GetProperty("Ready").GetBoolean(),diagnostic.RootElement.Clone());
            var layouts=Layouts();
            Check("one-private-layout-per-player",layouts.Length==players.Length && layouts.All(h=>!h.Observable));
            Check("engine-layout-path-matches-compiled-workshop-member",layouts.All(h=>h.StrLayout=="panorama/layout/custom_game/zepractice/movement.vxml_c"),layouts.Select(h=>h.StrLayout).ToArray());
            Check("global-hidden-with-owner-explicitly-visible",layouts.All(h=>Visibility(h)==EHudPanelClassStatus_t.k_eHudPanelClassStatus_HasClass &&
                players.Count(p=>PlayerVisible(h,p))==1),layouts.Select(h=>new{h.Index,status=Visibility(h).ToString()}).ToArray());
            Check("persistent-global-text-two-lines",layouts.All(h=>h.GlobalLayoutState.DialogVariableStrings.Count==2));
            Check("hud-does-not-capture-input",layouts.All(h=>!h.GlobalLayoutState.InputCaptureEnabled && players.All(p=>!h.IsInputCaptureEnabled(p))),layouts.Select(h=>new{h.Index,global=h.GlobalLayoutState.InputCaptureEnabled,states=h.PlayerLayoutStates.Count}).ToArray());
            var session=Call(service,"Get",players[0])!;
            var enabled=session.GetType().GetField("Enabled")!;
            var old=(CCSCustomHudLayout)session.GetType().GetField("Hud")!.GetValue(session)!;
            enabled.SetValue(session,false);
            Later(.25f,()=>{
                Check("off-removes-only-owner-hud",!old.IsValid && Layouts().Length==layouts.Length-1);
                enabled.SetValue(session,true);
                Later(.25f,()=>{
                    Check("on-recreates-without-map-restart",Layouts().Length==layouts.Length);
                    using var diag=JsonDocument.Parse((string)Call(service,"Diagnostics",new object[]{null!})!);
                    Check("native-hud-errors-empty",diag.RootElement.GetProperty("errors").GetArrayLength()==0);
                    File.Copy(Path.Combine(ModuleDirectory,"results.json"),Path.Combine(ModuleDirectory,"movement-results.json"),true);
                    Server.PrintToConsole("MOVEMENT_NATIVE_PROBE_COMPLETE (real client rendering not asserted)");
                });
            });
        });
    }
    private static CCSCustomHudLayout[] Layouts()=>Utilities.FindAllEntitiesByDesignerName<CCSCustomHudLayout>("custom_hud_layout").Where(e=>e.Entity?.Name?.StartsWith("ze_movement_hud_")==true).ToArray();
    private static EHudPanelClassStatus_t Visibility(CCSCustomHudLayout h)=>new HUDPanelHasClass_t(NativeAPI.GetNetworkVectorElementAt(h.GlobalLayoutState.HasClasses.Handle,0)).ClassStatus;
    private static bool PlayerVisible(CCSCustomHudLayout h,CCSPlayerController p)
    {
        // GetNetworkVectorElementAt assumes pointer-sized elements, not inline schema states.
        var first=NativeAPI.GetNetworkVectorElementAt(h.PlayerLayoutStates.Handle,0);
        var state=new CCSCustomHudLayoutState(first+p.Slot*CounterStrikeSharp.API.Modules.Memory.Schema.GetClassSize("CCSCustomHudLayoutState"));
        return state.HasClasses.Count==1&&new HUDPanelHasClass_t(NativeAPI.GetNetworkVectorElementAt(state.HasClasses.Handle,0)).ClassStatus==EHudPanelClassStatus_t.k_eHudPanelClassStatus_DoesNotHaveClass;
    }
}
