using ZePractice;

int passed = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }
var options = new WorkshopUpdateOptions(); options.Validate();
Check(options.Enabled && options.CheckIntervalSeconds == 60, "automatic update defaults on with 60 second poll");
foreach (string bad in new[] { "", "0", "+1", "1;quit", "../730", " 123", "18446744073709551616" })
    Check(!WorkshopUpdateCatalog.ValidId(bad), "invalid ID rejected: " + bad);
string metadata = """
{"response":{"publishedfiledetails":[
{"publishedfileid":"123","result":1,"consumer_app_id":730,"time_updated":100,"title":"map"},
{"publishedfileid":"456","result":9},
{"publishedfileid":"789","result":1,"consumer_app_id":440,"time_updated":100},
{"publishedfileid":"111","result":1,"consumer_app_id":730,"time_updated":100,"file_type":2},
{"publishedfileid":"999","result":1,"consumer_app_id":730,"time_updated":100}]}}
""";
Check(WorkshopUpdateCatalog.Parse(metadata, ["123", "456", "789", "111"]).Single().Id == "123", "only requested public CS2 community content accepted");
var now = new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc);
var backend = new Backend(); backend.Revisions = [new("123", 100, "one"), new("456", 200, "two"), new("789", 300, "three")];
backend.Installed["123"] = new(4, 100, 1000); backend.Installed["456"] = new(4, 100, 1000); backend.Installed["789"] = new(4, 100, 1000);
options.MaximumConcurrentDownloads = 1;
var controller = new WorkshopUpdateController(backend, options);
string[] Identifiers() => ["123", "456", "789"];
void Tick(int seconds) => controller.Tick(now.AddSeconds(seconds), Identifiers);
Tick(0); Tick(1);
Check(backend.Queries == 1 && backend.Downloads.SequenceEqual(["456"]), "current item skipped and only one stale item starts");
Check(controller.Entries.Single(entry => entry.Id == "789").Status == "queued", "concurrency overflow queued");
Tick(2); Check(backend.Downloads.Count == 1, "pending Steam request not duplicated before flags change");
backend.Installed["456"] = new(4 | 16, 100, 1000); Tick(3);
Check(controller.Entries.Single(entry => entry.Id == "456").Status == "downloading", "download progress recognized");
backend.Installed["456"] = new(4, 200, 2000); Tick(4);
Check(controller.CompletedDownloads == 1 && backend.Downloads.Last() == "789", "only confirmed installation counted, next queued item starts");
backend.Installed["789"] = new(4, 300, 3000); Tick(5);
Check(controller.CompletedDownloads == 2, "second download confirmed");
Tick(30); Check(backend.Queries == 1, "no per-tick metadata polling");
backend.Revisions = [new("123", 150, "updated"), new("456", 200, "two"), new("789", 300, "three")]; Tick(61); Tick(62);
Check(backend.Queries == 2 && backend.Downloads.Last() == "123", "new Workshop revision triggers update without map change");
Check(!controller.Entries.Any(entry => entry.Id == "123" && entry.Status == "current"), "download acceptance is not successful installation");
backend.Installed["123"] = new(4, 150, 1500); Tick(63);
Check(controller.CompletedDownloads == 3, "new revision completes after timestamp catches up");
backend.Installed["456"] = new(4 | 8, 200, 2000); backend.Accept = false; Tick(64);
int attempts = backend.Downloads.Count; Tick(70);
Check(backend.Downloads.Count == attempts && controller.Entries.Single(entry => entry.Id == "456").Error != null, "native rejection backs off and remains visible");
backend.Accept = true; Tick(125);
Check(backend.Downloads.Count == attempts + 1, "native rejection retries automatically after backoff");
Tick(1926);
Check(controller.Entries.Single(entry => entry.Id == "456").Status == "retry", "stalled request times out rather than falsely completing");
backend.ThrowQuery = true; Tick(2000); Tick(2001);
Check(controller.QueryError != null, "network failure reported without deleting installed data");
backend.ThrowQuery = false; Tick(2100); Tick(2101); Check(controller.QueryError == null, "network recovery clears query error");
options.Enabled = false; int queries = backend.Queries; Tick(2200); Check(backend.Queries == queries, "disabled option stops new requests");
options.Enabled = true; controller.Dispose(); Tick(2300); Check(backend.Queries == queries, "unloaded service ignores late ticks");
var pending = new Backend { Delayed = new() }; var pendingController = new WorkshopUpdateController(pending, new());
pendingController.Tick(now, () => ["123"]); pendingController.Dispose();
Check(pending.Token.IsCancellationRequested, "unload cancels in-flight HTTP query");
pending.Delayed.SetResult([new("123", 900, "late")]); pendingController.Tick(now.AddSeconds(10), () => ["123"]);
Check(pending.Downloads.Count == 0, "late query cannot download after unload");
var absent = new Backend(); var absentController = new WorkshopUpdateController(absent, new());
absentController.Tick(now, () => ["456"]); absentController.Tick(now.AddSeconds(1), () => ["456"]);
Check(absentController.Entries.Single().Status == "unavailable" && absent.Downloads.Count == 0, "private/deleted item never downloaded blindly");
absentController.Tick(now.AddSeconds(61), () => []); Check(absentController.Entries.Count == 0, "removed local content stops being watched"); absentController.Dispose();
var root = Path.Combine(Path.GetTempPath(), "ze-workshop-tests", Guid.NewGuid().ToString("N"));
foreach (string directory in new[] { "123", "456", "invalid" }) Directory.CreateDirectory(Path.Combine(root, directory));
File.WriteAllText(Path.Combine(root, "123", "123_dir.vpk"), "fixture"); File.WriteAllText(Path.Combine(root, "invalid", "map.vpk"), "fixture");
Check(WorkshopUpdateCatalog.Installed(root).SequenceEqual(["123"]), "discovery only includes installed numeric VPK folders");
Check(!new WorkshopInstallation(4 | 8, 100, 1).Current(100), "needs-update flag takes precedence over timestamp");
Check(!new WorkshopInstallation(4 | 32, 100, 1).Current(100), "pending download never reported current");
Check(!new WorkshopInstallation(0, 100, 1).Current(100), "uninstalled item never reported current");
var review = new Backend { Revisions = [new("3818340815", 200, "resources"), new("456", 300, "map")] };
review.Installed["3818340815"] = new(4, 100, 1000, 1000, 1000); review.Installed["456"] = new(4, 100, 1000);
using var reviews = new WorkshopUpdateController(review, new() { MaximumConcurrentDownloads = 1 });
void ReviewTick(int second) => reviews.Tick(now.AddSeconds(second), () => ["3818340815", "456"]);
ReviewTick(0); ReviewTick(1); ReviewTick(6);
Check(reviews.Entries.Single(e => e.Id == "3818340815").Status == "awaiting-published-revision" && reviews.CompletedDownloads == 0,
    "successful old approved revision is pending, not current or failed");
Check(review.Downloads.SequenceEqual(["3818340815", "456"]), "awaiting approval releases slot for other map updates");
review.Installed["456"] = new(4, 300, 3000); ReviewTick(7); ReviewTick(65);
Check(review.Downloads.Count == 2, "pending approval backs off rather than spamming Steam");
ReviewTick(66); Check(review.Downloads.Count == 3 && review.Downloads.Last() == "3818340815", "pending resource automatically retries after interval");
review.Installed["3818340815"] = new(4, 200, 2000); ReviewTick(67);
Check(reviews.Entries.Single(e => e.Id == "3818340815").Status == "current" && reviews.CompletedDownloads == 2, "approval plus actual download automatically becomes current");
var locked = new Backend { Revisions = [new("3818340815",200,"resources"),new("456",300,"map")] };
locked.Installed["3818340815"] = new(4|8,100,1000,2000,2000);
locked.Installed["456"] = new(4,100,1000);
using var lockController = new WorkshopUpdateController(locked,new(){MaximumConcurrentDownloads=1});
void LockTick(int t)=>lockController.Tick(now.AddSeconds(t),()=>["3818340815","456"]);
LockTick(0); LockTick(1); LockTick(2);
Check(lockController.Entries.Single(e=>e.Id=="3818340815").Status=="download-complete-awaiting-install", "downloaded but locked install is not approval pending");
Check(lockController.CompletedDownloads==0, "complete transfer is not installed success");
Check(locked.Downloads.Contains("456"), "locked resource install releases download queue for other maps");
Check(lockController.Entries.Single(e=>e.Id=="3818340815").Downloaded==2000, "byte progress is exposed accurately");
locked.Installed["3818340815"]=new(4,200,2000,2000,2000); LockTick(3);
Check(lockController.Entries.Single(e=>e.Id=="3818340815").Status=="current", "native install completion clears commit pending");
Check(lockController.CompletedDownloads==1, "deferred install completion counted exactly once");
LockTick(4);
Check(lockController.CompletedDownloads==1, "repeated current observation does not double count deferred commit");
Console.WriteLine($"WorkshopUpdateTests {passed}/{passed}; fixtures: {root}");

sealed class Backend : IWorkshopUpdateBackend
{
    public readonly Dictionary<string, WorkshopInstallation> Installed = new();
    public readonly List<string> Downloads = new();
    public IReadOnlyList<WorkshopRevision> Revisions = [];
    public TaskCompletionSource<IReadOnlyList<WorkshopRevision>>? Delayed;
    public CancellationToken Token;
    public bool Accept = true, ThrowQuery;
    public int Queries;
    public Task<IReadOnlyList<WorkshopRevision>> QueryAsync(string[] identifiers, CancellationToken cancellation)
    {
        Queries++; Token = cancellation;
        return Delayed?.Task ?? (ThrowQuery ? Task.FromException<IReadOnlyList<WorkshopRevision>>(new IOException("network failed")) : Task.FromResult(Revisions));
    }
    public WorkshopInstallation Inspect(string identifier) => Installed.GetValueOrDefault(identifier) ?? new(0, 0, 0);
    public bool Download(string identifier) { Downloads.Add(identifier); return Accept; }
}
