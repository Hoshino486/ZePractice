using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace ZePractice;

public sealed partial class WorkshopUpdateService : IDisposable
{
    private readonly ZePracticePlugin _plugin;
    private readonly WorkshopUpdateBackend _backend;
    private readonly WorkshopUpdateController _controller;
    private readonly string _cache;
    private readonly Dictionary<string, string> _lastStatus = new(StringComparer.Ordinal);
    private DateTime _nextTick;
    private bool _disposed;

    public WorkshopUpdateService(ZePracticePlugin plugin)
    {
        _plugin = plugin;
        string root = Server.GameDirectory;
        if (Path.GetFileName(root).Equals("csgo", StringComparison.OrdinalIgnoreCase)) root = Directory.GetParent(root)!.FullName;
        _cache = Path.Combine(root, "bin", "win64", "steamapps", "workshop", "content", "730");
        _backend = new(root); _controller = new(_backend, plugin.Config.WorkshopUpdates);
    }

    public void Initialize()
    {
        _plugin.RegisterListener<Listeners.OnTick>(Tick);
        _plugin.AddZeCommand("css_ze_workshop_update_status", "服务器工坊自动更新状态", Status);
    }

    private void Tick()
    {
        DateTime now = DateTime.UtcNow;
        if (_disposed || now < _nextTick) return;
        _nextTick = now.AddSeconds(1);
        _controller.Tick(now, () => WorkshopUpdateCatalog.Installed(_cache));
        RepairResourceInstallWhenEmpty(now);
        foreach (var entry in _controller.Entries)
        {
            if (_lastStatus.GetValueOrDefault(entry.Id) == entry.Status) continue;
            _lastStatus[entry.Id] = entry.Status;
            _plugin.Logger.LogInformation("WORKSHOP_UPDATE {Id} status={Status} remote={Remote} installed={Installed} error={Error}",
                entry.Id, entry.Status, entry.RemoteUpdated, entry.InstalledUpdated, entry.Error);
        }
    }

    public string Diagnostics(string? identifier = null) => JsonSerializer.Serialize(new
    {
        enabled = _plugin.Config.WorkshopUpdates.Enabled, checkSeconds = _plugin.Config.WorkshopUpdates.CheckIntervalSeconds,
        cache = _cache, lastCheckedUtc = _controller.LastChecked, nextCheckUtc = _controller.NextCheck,
        queryError = _controller.QueryError, completedDownloads = _controller.CompletedDownloads,
        resourceInstallRepair = new { phase=_repairPhase,error=_repairError,active=_repairAddons!=null },
        tracked = _controller.Entries.Count, current = _controller.Entries.Count(entry => entry.Status == "current"),
        unavailable = _controller.Entries.Count(entry => entry.Status == "unavailable"),
        apply = "Downloaded Workshop content is used on the next authorized map load; no automatic changelevel or round restart.",
        items = _controller.Entries.Where(entry => identifier == null || entry.Id == identifier).Select(entry => new { id = entry.Id, title = entry.Title, status = entry.Status,
            remoteUpdated = entry.RemoteUpdated, installedUpdated = entry.InstalledUpdated, downloadedBytes=entry.Downloaded, totalBytes=entry.Total,
            steamState=entry.SteamState, attempts = entry.Attempts, error = entry.Error }).ToArray()
    });

    private void Status(CCSPlayerController? player, CommandInfo command)
    {
        string? identifier = command.ArgCount == 2 ? command.GetArg(1) : null;
        if (command.ArgCount > 2 || identifier != null && !WorkshopUpdateCatalog.ValidId(identifier))
        { _plugin.Reply(player, "用法：css_ze_workshop_update_status [WorkshopID]"); return; }
        _plugin.Reply(player, Diagnostics(identifier));
    }

    public void Dispose()
    {
        if (_disposed) return;
        RestoreResourceMount();
        _disposed = true; _plugin.RemoveListener<Listeners.OnTick>(Tick);
        _plugin.RemoveZeCommand("css_ze_workshop_update_status", Status);
        _controller.Dispose(); _backend.Dispose();
    }
}
