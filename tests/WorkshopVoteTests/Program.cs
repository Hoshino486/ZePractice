using System.Numerics;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using ZePractice;

int passed = 0, failed = 0;
void Check(bool result, string message = "assertion failed") { if (!result) throw new Exception(message); }
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message); }
}
foreach (int count in Enumerable.Range(1, 128))
    Test("strict threshold " + count, () => { int required = WorkshopVoteLedger.RequiredVotes(count); Check(required * 5L > count * 4L); Check((required - 1L) * 5 <= count * 4L); });
Test("zero electorate has no quorum", () => Check(!new WorkshopVoteLedger().Quorum));
Test("maximum integer electorate does not overflow", () => Check(WorkshopVoteLedger.RequiredVotes(int.MaxValue) == 1717986918));
Test("tie picker called only for actual ties", () =>
{
    var ledger = new WorkshopVoteLedger(); ledger.AddCandidate("1", 10); ledger.AddCandidate("2", 10); ledger.SyncVoters([1, 2, 3]);
    Check(ledger.SelectWinner(_ => throw new Exception()) == null); ledger.Vote(1, "1"); Check(ledger.SelectWinner(_ => throw new Exception()) == "1");
    ledger.Vote(2, "2"); Check(ledger.SelectWinner(count => { Check(count == 2); return 1; }) == "2");
    ledger.Vote(3, "1"); Check(ledger.SelectWinner(_ => throw new Exception()) == "1");
});
Test("one ballot per identity with revote and disconnect removal", () =>
{
    var ledger = new WorkshopVoteLedger(); ledger.AddCandidate("1", 5); ledger.AddCandidate("2", 5); ledger.SyncVoters([1, 2]);
    Check(ledger.Vote(1, "1") && ledger.Vote(1, "2")); Check(ledger.VotesFor("1") == 0 && ledger.VotesFor("2") == 1);
    Check(ledger.Support(1) && !ledger.Support(1)); ledger.SyncVoters([2, 3]); Check(ledger.RtvCount == 0 && ledger.VotesFor("2") == 0);
    Check(!ledger.Vote(1, "1") && !ledger.Support(1));
});
Test("candidate validation duplicate cap removal", () =>
{
    var ledger = new WorkshopVoteLedger(); Check(!ledger.AddCandidate("1;quit", 1)); Check(!ledger.AddCandidate("0", 1));
    Check(ledger.AddCandidate("1", 1) && !ledger.AddCandidate("1", 1) && !ledger.AddCandidate("2", 1));
    ledger.SyncVoters([1]); ledger.Vote(1, "1"); Check(ledger.RemoveCandidate("1") && ledger.Choice(1) == null);
});
Test("single candidate skips choice but requires RTV", () =>
{ var ledger = new WorkshopVoteLedger(); ledger.AddCandidate("1", 1); ledger.SyncVoters([1]); Check(ledger.CanSelect && !ledger.Quorum); ledger.Support(1); Check(ledger.Quorum); });
Test("nomination persists without map execution or implicit vote", () =>
{
    using var fixture = new Fixture(1); fixture.Service.Nominate(fixture.Player(0), "+123"); fixture.Advance(20);
    Check(Server.Commands.Count == 0 && fixture.Service.Diagnostics().Contains("RTV=0/1")); Check(File.ReadAllText(fixture.Path).Contains("123"));
});
Test("80 percent is not enough and 100 percent for five switches exactly once", () =>
{
    using var fixture = new Fixture(5); fixture.Service.Nominate(null, "123");
    for (int index = 0; index < 4; index++) fixture.Command("css_rtv", fixture.Player(index));
    fixture.Advance(10); Check(Server.Commands.Count == 0); fixture.Command("css_rtv", fixture.Player(4));
    fixture.Advance(5); Check(Server.Commands.SequenceEqual(["host_workshop_map 123"])); fixture.Advance(10); Check(Server.Commands.Count == 1);
});
Test("bots HLTV connecting and console excluded while spectator counts", () =>
{
    using var fixture = new Fixture(5); fixture.Player(1).IsBot = true; fixture.Player(2).IsHLTV = true; fixture.Player(3).Connected = PlayerConnectedState.Connecting; fixture.Player(4).TeamNum = 1;
    fixture.Service.Nominate(null, "123"); foreach (var player in Utilities.Players) fixture.Command("css_rtv", player); fixture.Command("css_rtv", null);
    Check(fixture.Service.Diagnostics().Contains("RTV=2/2")); fixture.Advance(5); Check(Server.Commands.Count == 1);
});
Test("multi candidates with zero votes never silently randomize", () =>
{
    using var fixture = new Fixture(1); fixture.Service.Nominate(null, "123"); fixture.Service.Nominate(null, "456"); fixture.Command("css_rtv", fixture.Player(0));
    fixture.Advance(10); Check(Server.Commands.Count == 0); fixture.Command("css_ze_vote", fixture.Player(0), "456"); fixture.Advance(5);
    Check(Server.Commands.SequenceEqual(["host_workshop_map 456"]));
});
Test("menu votes do not immediately change map", () =>
{
    using var fixture = new Fixture(1); fixture.Service.Nominate(null, "123"); fixture.Service.Nominate(null, "456"); fixture.Service.ShowMenu(fixture.Player(0));
    var option = fixture.Plugin.Menus[0].Options[1]; option.Callback(fixture.Player(0), option); fixture.Advance(10);
    Check(Server.Commands.Count == 0); fixture.Command("css_rtv", fixture.Player(0)); fixture.Advance(5); Check(Server.Commands.Single().EndsWith("456"));
});
Test("tie winner is selected once and stable during status polls", () =>
{
    using var fixture = new Fixture(2); fixture.Service.Nominate(null, "123"); fixture.Service.Nominate(null, "456");
    fixture.Command("css_ze_vote", fixture.Player(0), "123"); fixture.Command("css_ze_vote", fixture.Player(1), "456");
    fixture.Command("css_rtv", fixture.Player(0)); fixture.Command("css_rtv", fixture.Player(1)); string status = fixture.Service.Diagnostics();
    for (int count = 0; count < 10; count++) Check(fixture.Service.Diagnostics() == status);
    fixture.Advance(5); Check(Server.Commands.Count == 1); Check(status.Contains(Server.Commands[0].Split(' ')[1]));
});
Test("withdraw cancels pending and old timer cannot execute", () =>
{
    using var fixture = new Fixture(1); fixture.Service.Nominate(null, "123"); fixture.Command("css_rtv", fixture.Player(0)); fixture.Command("css_unrtv", fixture.Player(0));
    fixture.Advance(6); Check(Server.Commands.Count == 0); fixture.Command("css_rtv", fixture.Player(0)); fixture.Advance(5); Check(Server.Commands.Count == 1);
});
Test("join raises denominator and cancels pending", () =>
{
    using var fixture = new Fixture(1); fixture.Service.Nominate(null, "123"); fixture.Command("css_rtv", fixture.Player(0));
    Utilities.Players.Add(new() { Slot = 1, EntityHandle = new(25) }); fixture.Advance(5); Check(Server.Commands.Count == 0);
});
Test("disconnect and slot reuse clear previous session votes", () =>
{
    using var fixture = new Fixture(2); fixture.Service.Nominate(null, "123"); fixture.Command("css_rtv", fixture.Player(0));
    ((Listeners.OnClientDisconnect)fixture.Plugin.Listeners[typeof(Listeners.OnClientDisconnect)])(0);
    fixture.Player(0).EntityHandle = new(99); fixture.Advance(5); Check(fixture.Service.Diagnostics().Contains("RTV=0/2")); Check(Server.Commands.Count == 0);
});
Test("map transition clears candidates and invalidates pending callbacks", () =>
{
    using var fixture = new Fixture(1); fixture.Service.Nominate(null, "123"); fixture.Command("css_rtv", fixture.Player(0));
    fixture.Service.OnMapEnd(); fixture.Service.OnMapStart(); fixture.Advance(5); Check(Server.Commands.Count == 0 && fixture.Service.Diagnostics().Contains("候选=0；RTV=0/1"));
});
Test("force immediately submits only for owner and invalidates pending RTV", () =>
{
    using var fixture = new Fixture(1); fixture.Service.Nominate(null, "123"); fixture.Command("css_rtv", fixture.Player(0));
    fixture.Command("css_ze_workshop_now", fixture.Player(0), "456"); Server.Flush(); Check(Server.Commands.Count == 0);
    fixture.Player(0).Owner = true; fixture.Command("css_ze_workshop_now", fixture.Player(0), "456"); Server.Flush();
    Check(Server.Commands.SequenceEqual(["host_workshop_map 456"])); fixture.Advance(5); Check(Server.Commands.Count == 1);
});
Test("force validates identifier and extra argument", () =>
{
    using var fixture = new Fixture(1); fixture.Command("css_ze_workshop_now", null, "1;quit"); fixture.Command("css_ze_workshop_now", null, "1", "quit"); fixture.Advance(5); Check(Server.Commands.Count == 0);
});
Test("timeout unlocks and clears RTV without auto retry", () =>
{
    using var fixture = new Fixture(1); fixture.Service.Nominate(null, "123"); fixture.Command("css_rtv", fixture.Player(0)); fixture.Advance(5); fixture.Advance(181);
    Check(Server.Commands.Count == 1 && fixture.Service.Diagnostics().Contains("RTV=0/1")); fixture.Advance(10); Check(Server.Commands.Count == 1);
});
Test("raw say and say_team handle RTV without touching ordinary text", () =>
{
    using var fixture = new Fixture(1); fixture.Service.Nominate(null, "123");
    Check(fixture.Plugin.Chats["say"](fixture.Player(0), new("say", " Rtv ")) == HookResult.Stop);
    Check(fixture.Plugin.Chats["say_team"](fixture.Player(0), new("say_team", "unrtv")) == HookResult.Stop);
    Check(fixture.Plugin.Chats["say"](fixture.Player(0), new("say", "hello")) == HookResult.Continue); fixture.Advance(5); Check(Server.Commands.Count == 0);
});
Test("native vote manager stays disabled", () =>
{ using var fixture = new Fixture(1); ZePracticePlugin.Cvars["cs2f_votemanager_enable"] = "1"; fixture.Advance(1); Check(ZePracticePlugin.Cvars["cs2f_votemanager_enable"] == "0"); });
Test("malformed candidate store is retained and not overwritten", () =>
{ using var fixture = new Fixture(1, "{broken"); fixture.Service.Nominate(null, "123"); Check(File.ReadAllText(fixture.Path) == "{broken" && fixture.Plugin.Logger.Errors.Count == 1); });
Test("legacy permanent candidates are not restored on a fresh map", () =>
{ using var fixture = new Fixture(1, "[\"123\",\"456\"]"); fixture.Advance(1); Check(fixture.Service.Diagnostics().Contains("候选=0；RTV=0/1") && fixture.StoredCandidates().Length == 0); });
Test("null candidate entry does not fail plugin loading or overwrite input", () =>
{ using var fixture = new Fixture(1, "[null]"); fixture.Service.Nominate(null, "123"); Check(File.ReadAllText(fixture.Path) == "[null]" && fixture.Plugin.Logger.Errors.Count == 1); });
Test("failed atomic save rolls back in-memory nomination", () =>
{ using var fixture = new Fixture(1); Directory.CreateDirectory(fixture.Path + ".tmp"); fixture.Service.Nominate(null, "123"); Check(fixture.Service.Diagnostics().Contains("候选=0") && fixture.Plugin.Logger.Errors.Count == 1); });
Test("candidate removal only owner and removes matching ballots", () =>
{
    using var fixture = new Fixture(1); fixture.Service.Nominate(null, "123"); fixture.Command("css_ze_map_remove", fixture.Player(0), "123"); Check(fixture.Service.Diagnostics().Contains("候选=1"));
    fixture.Command("css_ze_map_remove", null, "123"); Check(fixture.Service.Diagnostics().Contains("候选=0")); Check(fixture.StoredCandidates().Length == 0);
});
Test("nomination spam deduplicated and bounded", () =>
{
    using var fixture = new Fixture(1); fixture.Plugin.Config.MapVote.MaximumCandidates = 2;
    fixture.Service.Nominate(fixture.Player(0), "123"); fixture.Service.Nominate(fixture.Player(0), "456"); Check(fixture.Service.Diagnostics().Contains("候选=1"));
    fixture.Advance(4); fixture.Service.Nominate(fixture.Player(0), "456"); fixture.Service.Nominate(null, "789"); Check(fixture.Service.Diagnostics().Contains("候选=2"));
});
Test("only physical map button entity classes qualify", () =>
{ foreach (string name in new[] { "func_button", "func_rot_button", "momentary_rot_button" }) Check(MapMarkerLogic.IsButton(name)); foreach (string name in new[] { "menu_button", "prop_dynamic", "trigger_multiple", "button" }) Check(!MapMarkerLogic.IsButton(name)); });
Test("button center uses bounds and origin", () =>
{ Check(MapMarkerLogic.ButtonCenter(new(100, 200, 300), new(-10, -20, 0), new(30, 40, 80), Vector3.Zero) == new Vector3(110, 210, 340)); });
foreach (var sample in new[] { (new Vector3(0, 90, 0), new Vector3(0, 10, 0)), (new Vector3(90, 0, 0), new Vector3(0, 0, -10)), (new Vector3(0, 180, 0), new Vector3(-10, 0, 0)) })
    Test("Source rotation " + sample.Item1, () => Check(Vector3.Distance(MapMarkerLogic.Rotate(new(10, 0, 0), sample.Item1), sample.Item2) < 0.0001));
Test("Source roll and compound transform preserve length", () =>
{ Check(Vector3.Distance(MapMarkerLogic.Rotate(new(0, 10, 0), new(0, 0, 90)), new(0, 0, 10)) < 0.0001); Check(Math.Abs(MapMarkerLogic.Rotate(new(2, 3, 4), new(31, 46, 72)).Length() - new Vector3(2, 3, 4).Length()) < 0.0001); });
Test("map firearm provenance excludes bought guns and knives", () =>
{
    Check(MapMarkerLogic.IsMapGun(true, false, "hammer", "", false, true)); Check(MapMarkerLogic.IsMapGun(true, false, "", "mapgun", false, false));
    Check(MapMarkerLogic.IsMapGun(true, false, "", "", true, false)); Check(!MapMarkerLogic.IsMapGun(true, true, "", "", true, false));
    Check(!MapMarkerLogic.IsMapGun(true, false, "", "", false, false)); Check(!MapMarkerLogic.IsMapGun(false, false, "hammer", "", true, false));
    Check(!MapMarkerLogic.IsMapGun(true, false, "", "", true, true));
});
Test("used button stays hidden through rescans and duplicate outputs", () =>
{
    var state = new ButtonMarkerRoundState();
    Check(state.CanMark(100)); Check(state.Consume("func_button", "OnPressed", 100));
    Check(!state.Consume("func_button", "OnPressed", 100));
    for (int scan = 0; scan < 10; scan++) Check(!state.CanMark(100));
    Check(state.UsedCount == 1 && state.CanMark(101));
});
Test("locked/released/non-button outputs do not consume markers", () =>
{
    var state = new ButtonMarkerRoundState();
    foreach (string output in new[] { "OnUseLocked", "OnDamaged", "OnUnpressed", "OnFullyClosed" })
        Check(!state.Consume("func_button", output, 100));
    Check(!state.Consume("logic_relay", "OnPressed", 100)); Check(state.CanMark(100));
});
Test("rotating buttons and newly reused entity slots are independent", () =>
{
    var state = new ButtonMarkerRoundState();
    Check(state.Consume("func_rot_button", "OnPressed", 100));
    Check(state.Consume("momentary_rot_button", "OnPressed", 101));
    Check(state.CanMark(100 + 0x8000)); Check(state.UsedCount == 2);
    state.Reset(); Check(state.CanMark(100) && state.CanMark(101) && state.UsedCount == 0);
    Check(state.Consume("func_button", "OnPressed", 100));
});
MapSessionTests.Run(Test);
NamedMapLoadTests.Run(Test);
Console.WriteLine($"WorkshopVote/MapMarker: {passed}/{passed + failed}");
return failed == 0 ? 0 : 1;

sealed class Fixture : IDisposable
{
    public readonly ZePracticePlugin Plugin = new();
    public WorkshopVoteService Service;
    public string Path => System.IO.Path.Combine(Plugin.ModuleDirectory, "data", "workshop-candidates.json");
    public Fixture(int count, string? stored = null, bool hotReload = false)
    {
        Utilities.Players.Clear(); Server.Commands.Clear(); Server.Messages.Clear(); Server.Frames.Clear(); Server.CurrentTime = 0;
        ZePracticePlugin.Cvars.Clear();
        Server.MapName = "ze_test_a";
        Plugin.ModuleDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ze-vote-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        if (stored != null) File.WriteAllText(Path, stored);
        for (int index = 0; index < count; index++) Utilities.Players.Add(new() { Slot = index, EntityHandle = new((uint)(index + 1)) });
        Service = new(Plugin); Service.Initialize(hotReload); Service.OnMapStart();
    }
    public string[] StoredCandidates() => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path)).GetProperty("Candidates").Deserialize<string[]>()!;
    public void Reload(bool hotReload = true)
    { Service.Dispose(hotReload); Service = new(Plugin); Service.Initialize(hotReload); Service.OnMapStart(); }
    public CCSPlayerController Player(int index) => Utilities.Players[index];
    public void Command(string command, CCSPlayerController? player, params string[] arguments) => Plugin.Commands[command](player, new([command, .. arguments]));
    public void Advance(float seconds)
    {
        Server.CurrentTime += seconds;
        ((Listeners.OnTick)Plugin.Listeners[typeof(Listeners.OnTick)])();
        foreach (var timer in Plugin.Timers.Where(timer => timer.At <= Server.CurrentTime).ToArray()) { Plugin.Timers.Remove(timer); timer.Action(); }
        Server.Flush();
    }
    public void Dispose() => Service.Dispose();
}
