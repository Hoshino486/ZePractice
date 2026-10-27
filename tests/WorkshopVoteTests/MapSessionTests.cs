using System.Text.Json;
using System.Text.Json.Nodes;
using CounterStrikeSharp.API;
using ZePractice;

internal static class MapSessionTests
{
    public static void Run(Action<string, Action> test)
    {
        static void Check(bool ok) { if (!ok) throw new Exception("map session assertion failed"); }
        test("reset clears all candidates votes electorate and RTV", () =>
        {
            var ledger = new WorkshopVoteLedger(); ledger.AddCandidate("123", 10); ledger.AddCandidate("456", 10);
            ledger.SyncVoters([1]); ledger.Vote(1, "123"); ledger.Support(1); ledger.ResetForMap();
            Check(ledger.Candidates.Count == 0 && ledger.PlayerCount == 0 && ledger.RtvCount == 0 && ledger.Choice(1) == null && !ledger.CanSelect);
        });
        test("all old nominations removed not only elected winner", () =>
        {
            using var f = new Fixture(1); f.Service.Nominate(null, "123"); f.Service.Nominate(null, "456");
            f.Command("css_ze_vote", f.Player(0), "456"); f.Command("css_rtv", f.Player(0)); f.Advance(5);
            Check(Server.Commands.SequenceEqual(["host_workshop_map 456"])); f.Service.OnMapEnd();
            Check(f.StoredCandidates().Length == 0); Server.MapName = "ze_test_b"; f.Service.OnMapStart(); f.Advance(6);
            Check(f.StoredCandidates().Length == 0 && Server.Commands.Count == 1 && f.Service.Diagnostics().Contains("候选=0；RTV=0/1"));
        });
        test("owner direct map change also clears every nomination", () =>
        {
            using var f = new Fixture(1); f.Service.Nominate(null, "123"); f.Service.Nominate(null, "456");
            f.Command("css_ze_workshop_now", null, "789"); Server.Flush(); Check(Server.Commands.Single() == "host_workshop_map 789");
            f.Service.OnMapEnd(); Server.MapName = "ze_test_b"; f.Service.OnMapStart(); Check(f.StoredCandidates().Length == 0);
        });
        test("same map reload counts as new map and clears nominations", () =>
        {
            using var f = new Fixture(1); f.Service.Nominate(null, "123");
            f.Service.OnMapEnd(); f.Service.OnMapStart(); Check(f.StoredCandidates().Length == 0);
        });
        test("new map start clears even when map end callback was missed", () =>
        {
            using var f = new Fixture(1); f.Service.Nominate(null, "123"); Server.MapName = "ze_test_b";
            f.Service.OnMapStart(); Check(f.StoredCandidates().Length == 0);
        });
        test("new map can immediately renominate same ID and cooldown resets", () =>
        {
            using var f = new Fixture(1); f.Service.Nominate(f.Player(0), "123"); f.Service.OnMapEnd(); f.Service.OnMapStart();
            f.Service.Nominate(f.Player(0), "123"); Check(f.StoredCandidates().SequenceEqual(["123"]));
        });
        test("empty map menu replaces old list with explicit empty state", () =>
        {
            using var f = new Fixture(1); f.Service.Nominate(null, "123"); f.Service.ShowMenu(f.Player(0));
            f.Service.OnMapEnd(); f.Service.OnMapStart(); f.Service.ShowMenu(f.Player(0));
            var options = f.Plugin.Menus[0].Options; Check(options.Count == 3 && options.All(o => o.Disabled) && options.All(o => !o.Text.Contains("123")));
        });
        test("old map menu callback cannot vote or RTV into a new map", () =>
        {
            using var f = new Fixture(1); f.Service.Nominate(null, "123"); f.Service.ShowMenu(f.Player(0));
            var old = f.Plugin.Menus[0]; f.Service.OnMapEnd(); f.Service.OnMapStart(); f.Service.Nominate(null, "123");
            foreach (var option in old.Options) option.Callback(f.Player(0), option);
            f.Advance(10); Check(Server.Commands.Count == 0 && f.Service.Diagnostics().Contains("RTV=0/1"));
        });
        test("genuine same-map hot reload preserves nominations but resets ballots", () =>
        {
            using var f = new Fixture(1); f.Service.Nominate(null, "123"); f.Service.Nominate(null, "456");
            f.Command("css_ze_vote", f.Player(0), "456"); f.Command("css_rtv", f.Player(0));
            f.Reload(); f.Advance(6); Check(f.StoredCandidates().SequenceEqual(["123", "456"]) && Server.Commands.Count == 0);
            Check(f.Service.Diagnostics().Contains("候选=2；RTV=0/1"));
            Check(!JsonNode.Parse(File.ReadAllText(f.Path))!["ResumeAfterHotReload"]!.GetValue<bool>());
        });
        test("resumed list cleared at the next real map change", () =>
        {
            using var f = new Fixture(1); f.Service.Nominate(null, "123"); f.Reload(); f.Service.OnMapEnd(); f.Service.OnMapStart();
            Check(f.StoredCandidates().Length == 0); f.Reload(); Check(f.StoredCandidates().Length == 0);
        });
        test("normal unload and cold load do not restore nominations", () =>
        { using var f = new Fixture(1); f.Service.Nominate(null, "123"); f.Reload(false); Check(f.StoredCandidates().Length == 0); });
        test("legacy array discarded even on first hot deployment", () =>
        { using var f = new Fixture(1, "[\"123\",\"456\"]", true); Check(f.StoredCandidates().Length == 0); });
        foreach (string changed in new[] { "Map", "Process", "ResumeAfterHotReload" })
            test("stale hot-reload snapshot rejected when " + changed + " differs", () =>
            {
                using var f = new Fixture(1); f.Service.Nominate(null, "123"); f.Service.Dispose(true);
                var node = JsonNode.Parse(File.ReadAllText(f.Path))!;
                node[changed] = changed == "ResumeAfterHotReload" ? JsonValue.Create(false) : JsonValue.Create("different-session");
                File.WriteAllText(f.Path, node.ToJsonString()); f.Service = new(f.Plugin); f.Service.Initialize(true); f.Service.OnMapStart();
                Check(f.StoredCandidates().Length == 0);
            });
        test("failed change timeout retains current map nominations", () =>
        {
            using var f = new Fixture(1); f.Service.Nominate(null, "123"); f.Command("css_rtv", f.Player(0)); f.Advance(5); f.Advance(181);
            Check(f.StoredCandidates().SequenceEqual(["123"]) && f.Service.Diagnostics().Contains("RTV=0/1"));
        });
        test("map boundary write failure cannot retain stale in-memory nominations", () =>
        {
            using var f = new Fixture(1); f.Service.Nominate(null, "123"); Directory.CreateDirectory(f.Path + ".tmp");
            f.Service.OnMapEnd(); f.Service.OnMapStart(); f.Advance(5);
            Check(f.Service.Diagnostics().Contains("候选=0；RTV=0/1") && Server.Commands.Count == 0);
        });
    }
}
