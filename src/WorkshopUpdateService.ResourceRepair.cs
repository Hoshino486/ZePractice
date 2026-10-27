using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;

namespace ZePractice;

public sealed partial class WorkshopUpdateService
{
    private string? _repairAddons, _repairClientAddons;
    private string _repairPhase="idle";
    private string? _repairError;
    private DateTime _emptySince, _repairStarted, _repairRetry;
    private uint _repairExpected;
    // Only our resource-only addon, and only an empty server. Active map VPKs are never unmounted here.
    private void RepairResourceInstallWhenEmpty(DateTime now)
    {
        if (!_plugin.Config.WorkshopUpdates.Enabled) { RestoreResourceMount(); return; }
        bool empty=Utilities.GetPlayers().Count==0;
        if(!empty)
        {
            _emptySince=default;
            if(_repairAddons!=null) { RestoreResourceMount(); _repairPhase="deferred-player-connected"; _repairRetry=now.AddSeconds(60); }
            return;
        }
        if(_emptySince==default) _emptySince=now;
        try
        {
            if(_repairAddons!=null)
            {
                var install=_backend.Inspect(CommanderResources.WorkshopId);
                if(install.Current(_repairExpected))
                {
                    RestoreResourceMount();
                    _repairPhase="installed-and-remounted"; _repairError=null;
                    _plugin.RefreshMovementResourcesWhenEmpty();
                    _plugin.Logger.LogInformation("RESOURCE_INSTALL_REPAIR installed and remounted {Id}; no changelevel/restart",CommanderResources.WorkshopId);
                }
                else if(now-_repairStarted>TimeSpan.FromSeconds(30))
                {
                    RestoreResourceMount(); _repairPhase="commit-still-pending";
                    _repairError="Steam did not confirm installation after unmount; retry only while empty. No cache files were overwritten.";
                    _repairRetry=now.AddMinutes(5);
                }
                return;
            }
            if(now<_repairRetry || now-_emptySince<TimeSpan.FromSeconds(10)) return;
            var entry=_controller.Entries.FirstOrDefault(e=>e.Id==CommanderResources.WorkshopId && e.Status=="download-complete-awaiting-install");
            if(entry==null)return;
            // Restrict recovery to the exact known MAM resource mount: never reorder/unmount unrelated addons.
            string addons=ConVar.Find("mm_extra_addons")?.StringValue.Trim()??"";
            if(addons!=CommanderResources.WorkshopId) { _repairPhase="deferred-other-addon-mounts";return; }
            _repairExpected=entry.RemoteUpdated; _repairStarted=now; _repairError=null;
            _repairAddons=addons; _repairClientAddons=ConVar.Find("mm_client_extra_addons")?.StringValue??"";
            var client=_repairClientAddons.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Append(CommanderResources.WorkshopId).Distinct();
            // A client connecting during the brief commit window must still receive the resource bundle.
            ZePracticePlugin.SetCvar("mm_client_extra_addons",string.Join(',',client));
            // MAM RefreshAddons' default bReloadMap=false; this only releases its filesystem mount.
            ZePracticePlugin.SetCvar("mm_extra_addons","");
            if(!_backend.Download(CommanderResources.WorkshopId))throw new IOException("Steam rejected resource re-install request.");
            _repairPhase="unmounted-waiting-steam-commit";
            _plugin.Logger.LogInformation("RESOURCE_INSTALL_REPAIR released resource-only mount on empty server; requested Steam commit (no map reload)");
        }
        catch(Exception e)
        {
            RestoreResourceMount(); _repairPhase="retry"; _repairError=e.Message; _repairRetry=now.AddMinutes(5);
            _plugin.Logger.LogWarning(e,"RESOURCE_INSTALL_REPAIR failed; original mounts restored");
        }
    }
    private void RestoreResourceMount()
    {
        if(_repairAddons==null)return;
        string addons=_repairAddons; string client=_repairClientAddons??"";
        _repairAddons=null; _repairClientAddons=null;
        ZePracticePlugin.SetCvar("mm_extra_addons",addons);
        ZePracticePlugin.SetCvar("mm_client_extra_addons",client);
    }
}
