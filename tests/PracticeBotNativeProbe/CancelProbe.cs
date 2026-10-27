using System.Collections;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

public sealed partial class PracticeBotNativeProbe
{
    private void RegisterCancelProbe()
    {
        AddCommand("css_pb_cancel_window_probe", "Isolated: cancel an already created, not-yet-bound Bot", (p,c) =>
        {
            if (p != null) return;
            Guard(); _results.Clear();
            var service=Field(Plugin(),"_practiceBots");
            var a=Utilities.GetPlayers().First(p=>p.IsBot && p.TeamNum==3 && p.PawnIsAlive);
            int originalPlayers=Utilities.GetPlayers().Count;
            bool observed=false;
            Listeners.OnTick? watcher=null;
            watcher=()=>
            {
                if (((IDictionary)Field(service,"_pendingSlots")).Count==0) return;
                RemoveListener(watcher!); observed=true;
                Check("cancel-window-observed-created-pending",Utilities.GetPlayers().Count==originalPlayers+1);
                Call(service,"OnMapEnd");
                Later(2,()=>
                {
                    Check("map-end-before-bind-leaves-no-bot-or-quota-replacement",Utilities.GetPlayers().Count==originalPlayers,
                        Utilities.GetPlayers().Select(p=>p.PlayerName).ToArray());
                    Call(service,"OnMapStart");
                    File.Copy(Path.Combine(ModuleDirectory,"results.json"),Path.Combine(ModuleDirectory,"cancel-results.json"),true);
                    Server.PrintToConsole("PB_CANCEL_WINDOW_PROBE_COMPLETE");
                });
            };
            RegisterListener(watcher);
            Check("cancel-window-request-accepted",(bool)Call(service,"Request",a,false)!);
            Later(5,()=> { RemoveListener(watcher); if(!observed) Check("cancel-window-observed",false); });
        });
    }
}
