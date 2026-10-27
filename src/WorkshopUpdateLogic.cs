using System.Globalization;
using System.Text.Json;

namespace ZePractice;

public sealed record WorkshopRevision(string Id, uint Updated, string Title);
public sealed record WorkshopInstallation(uint State, uint Updated, ulong Size, ulong Downloaded = 0, ulong Total = 0)
{
    public bool Busy => (State & (16 | 32)) != 0;
    public bool DownloadComplete => Total > 0 && Downloaded >= Total;
    public bool Current(uint expected) => (State & 4) != 0 && (State & 8) == 0 && !Busy && Updated >= expected;
}

public interface IWorkshopUpdateBackend
{
    Task<IReadOnlyList<WorkshopRevision>> QueryAsync(string[] identifiers, CancellationToken cancellation);
    WorkshopInstallation Inspect(string identifier);
    bool Download(string identifier);
}

public static class WorkshopUpdateCatalog
{
    public static bool ValidId(string identifier) => identifier.Length is > 0 and <= 20 &&
        identifier.All(character => character is >= '0' and <= '9') &&
        ulong.TryParse(identifier, NumberStyles.None, CultureInfo.InvariantCulture, out ulong number) && number > 0;

    public static string[] Installed(string directory) => !Directory.Exists(directory) ? [] : Directory.EnumerateDirectories(directory)
        .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
        .Where(path => ValidId(Path.GetFileName(path)) && Directory.EnumerateFiles(path, "*.vpk", SearchOption.TopDirectoryOnly).Any())
        .Select(Path.GetFileName).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(1000).ToArray();

    public static IReadOnlyList<WorkshopRevision> Parse(string json, IEnumerable<string> requested)
    {
        var allowed = requested.ToHashSet(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<string, WorkshopRevision>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.GetProperty("response").GetProperty("publishedfiledetails").EnumerateArray())
        {
            if (!item.TryGetProperty("result", out var status) || status.GetInt32() != 1 ||
                !item.TryGetProperty("consumer_app_id", out var app) || app.GetInt32() != 730 ||
                !item.TryGetProperty("publishedfileid", out var id)) continue;
            if (item.TryGetProperty("file_type", out var type) && type.GetInt32() != 0) continue;
            string identifier = id.GetString() ?? "";
            if (!allowed.Contains(identifier) || !ValidId(identifier) || !item.TryGetProperty("time_updated", out var time) ||
                !time.TryGetUInt32(out uint updated) || updated == 0) continue;
            string title = item.TryGetProperty("title", out var text) ? text.GetString() ?? identifier : identifier;
            result[identifier] = new(identifier, updated, title);
        }
        return result.Values.ToArray();
    }
}

public sealed class WorkshopUpdateEntry(string identifier)
{
    public string Id { get; } = identifier;
    public string Title { get; set; } = identifier;
    public uint RemoteUpdated { get; set; }
    public uint InstalledUpdated { get; set; }
    public string Status { get; set; } = "checking";
    public string? Error { get; set; }
    public bool Requested { get; set; }
    public bool AwaitingCommit { get; set; }
    public int Attempts { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime RetryAt { get; set; }
    public ulong Downloaded { get; set; }
    public ulong Total { get; set; }
    public uint SteamState { get; set; }
}

public sealed class WorkshopUpdateController(IWorkshopUpdateBackend backend, WorkshopUpdateOptions options) : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Dictionary<string, WorkshopUpdateEntry> _entries = new(StringComparer.Ordinal);
    private Task<IReadOnlyList<WorkshopRevision>>? _query;
    private bool _disposed;
    private DateTime _nextCheck;
    public IReadOnlyCollection<WorkshopUpdateEntry> Entries => _entries.Values;
    public string? QueryError { get; private set; }
    public long CompletedDownloads { get; private set; }
    public DateTime? LastChecked { get; private set; }
    public DateTime NextCheck => _nextCheck;

    public void Tick(DateTime now, Func<string[]> discover)
    {
        if (_disposed || !options.Enabled) return;
        if (_query is { IsCompleted: true })
        {
            try
            {
                var revisions = _query.GetAwaiter().GetResult();
                var returned = revisions.Select(revision => revision.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var revision in revisions)
                    if (_entries.TryGetValue(revision.Id, out var entry))
                    { entry.RemoteUpdated = revision.Updated; entry.Title = revision.Title; entry.Error = null; }
                foreach (var entry in _entries.Values.Where(entry => !returned.Contains(entry.Id) && entry.RemoteUpdated == 0))
                { entry.Status = "unavailable"; entry.Error = "Workshop did not return a public CS2 item."; }
                LastChecked = now; QueryError = null;
            }
            catch (Exception error) { QueryError = error.Message; }
            _query = null;
        }
        if (_query == null && now >= _nextCheck)
        {
            _nextCheck = now.AddSeconds(options.CheckIntervalSeconds);
            try
            {
                string[] identifiers = discover().Where(WorkshopUpdateCatalog.ValidId).Distinct(StringComparer.Ordinal).Take(1000).ToArray();
                foreach (string identifier in identifiers) _entries.TryAdd(identifier, new(identifier));
                foreach (string identifier in _entries.Keys.Except(identifiers).ToArray()) _entries.Remove(identifier);
                if (identifiers.Length > 0) _query = backend.QueryAsync(identifiers, _cancellation.Token);
            }
            catch (Exception error) { QueryError = error.Message; }
        }
        int busy = 0;
        var inspected = new List<(WorkshopUpdateEntry Entry, WorkshopInstallation Installation)>();
        foreach (var entry in _entries.Values.Where(entry => entry.RemoteUpdated > 0))
        {
            try
            {
                var installed = backend.Inspect(entry.Id); entry.InstalledUpdated = installed.Updated;
                entry.Downloaded=installed.Downloaded; entry.Total=installed.Total; entry.SteamState=installed.State;
                if (installed.Current(entry.RemoteUpdated))
                {
                    if (entry.Requested || entry.AwaitingCommit) CompletedDownloads++;
                    entry.Requested = entry.AwaitingCommit = false; entry.Status = "current"; entry.Error = null; entry.RetryAt = default;
                }
                else if (installed.DownloadComplete && (installed.Busy || (installed.State & 8) != 0) && installed.Updated < entry.RemoteUpdated)
                {
                    // Bytes downloaded is NOT installation. In-use VPKs on Windows can fail commit with Steam result 33.
                    // Release our queue slot; report this phase explicitly, and retry at a bounded interval.
                    entry.Status = "download-complete-awaiting-install";
                    entry.AwaitingCommit = true;
                    if (entry.Requested) { entry.Requested=false; entry.RetryAt=now.AddSeconds(options.RetrySeconds); }
                    if (!installed.Busy) inspected.Add((entry,installed));
                }
                else if (installed.Busy)
                { busy++; entry.Status = "downloading"; }
                else if (entry.Requested && (installed.State & (4u | 8u)) == 4u && installed.Updated > 0 && installed.Updated < entry.RemoteUpdated &&
                         now >= entry.RequestedAt.AddSeconds(5))
                {
                    // Steam may successfully install the last approved revision
                    // while owner metadata already describes a pending submission.
                    // Back off without occupying a download slot for 30 minutes.
                    entry.Requested = false; entry.Status = "awaiting-published-revision"; entry.Error = null;
                    entry.RetryAt = now.AddSeconds(options.RetrySeconds);
                }
                else if (entry.Requested && now < entry.RequestedAt.AddSeconds(options.DownloadTimeoutSeconds))
                { busy++; entry.Status = "waiting-for-steam"; }
                else
                {
                    if (entry.Requested) Fail(entry, now, "Steam download did not finish before the timeout.");
                    inspected.Add((entry, installed));
                }
            }
            catch (Exception error) { Fail(entry, now, error.Message); }
        }
        foreach (var (entry, _) in inspected)
        {
            if (now < entry.RetryAt) continue;
            if (busy >= options.MaximumConcurrentDownloads) { entry.Status = "queued"; continue; }
            try
            {
                entry.Attempts++;
                if (!backend.Download(entry.Id)) { Fail(entry, now, "Steam rejected the download request; will retry."); continue; }
                entry.Requested = true; entry.RequestedAt = now; entry.Status = "requested"; entry.Error = null; busy++;
            }
            catch (Exception error) { Fail(entry, now, error.Message); }
        }
    }

    private void Fail(WorkshopUpdateEntry entry, DateTime now, string error)
    { entry.Requested = false; entry.Status = "retry"; entry.Error = error; entry.RetryAt = now.AddSeconds(options.RetrySeconds); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _cancellation.Cancel();
        if (_query != null) _ = _query.ContinueWith(task => _ = task.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _cancellation.Dispose();
    }
}
