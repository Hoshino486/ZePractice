using CounterStrikeSharp.API;
using ZePractice;

internal static class NamedMapLoadTests
{
    internal static void Run(Action<string, Action> test)
    {
        const string command = "mm_host_workshop_map 123 ze_exact";
        static string Token() => Server.Commands.Single(c => c.StartsWith("mm_host_workshop_map ")).Split(' ')[3];
        test("named Workshop loading fails closed when native bridge is absent", () =>
        {
            using var f = new Fixture(1); f.Player(0).Owner = true;
            if (f.Service.ForceChange(f.Player(0), command)) throw new Exception("missing bridge accepted");
            f.Advance(1);
            if (Server.Commands.Count != 0 || !f.Service.Diagnostics().Contains("候选=0")) throw new Exception("old map was altered");
        });
        test("named Workshop load keeps exact member and one unique token; double click is rejected", () =>
        {
            using var f = new Fixture(1); f.Player(0).Owner = true; ZePracticePlugin.Cvars["mm_named_map_api"] = "1";
            if (!f.Service.ForceChange(f.Player(0), command) || f.Service.ForceChange(f.Player(0), command)) throw new Exception("request lock broken");
            f.Advance(1);
            if (Token().Length != 32 || Server.Commands[0] != command + " " + Token()) throw new Exception("lost exact member or token");
        });
        test("non-owner cannot invoke named Workshop hosting", () =>
        {
            using var f = new Fixture(1); ZePracticePlugin.Cvars["mm_named_map_api"] = "1";
            if (f.Service.ForceChange(f.Player(0), command)) throw new Exception("non-owner accepted");
            f.Advance(1); if (Server.Commands.Count != 0) throw new Exception("command submitted");
        });
        foreach (string state in new[] { "failed-download", "failed-map-missing", "failed-request", "cancelled" })
            test("native " + state + " unlocks without issuing an unsafe map command", () =>
            {
                using var f = new Fixture(0); ZePracticePlugin.Cvars["mm_named_map_api"] = "1";
                f.Service.ForceChange(null, command); f.Advance(1);
                ZePracticePlugin.Cvars["mm_named_map_status"] = Token() + "|" + state; f.Advance(1);
                if (f.Service.Diagnostics().Contains("引擎加载中") || Server.Commands.Count != 1) throw new Exception("failure still locked or fallback executed");
                if (!f.Service.ForceChange(null, command)) throw new Exception("retry rejected");
            });
        test("stale native response cannot unlock a newer request", () =>
        {
            using var f = new Fixture(0); ZePracticePlugin.Cvars["mm_named_map_api"] = "1";
            f.Service.ForceChange(null, command); f.Advance(1);
            ZePracticePlugin.Cvars["mm_named_map_status"] = new string('f', 32) + "|failed-download"; f.Advance(1);
            if (!f.Service.Diagnostics().Contains("引擎加载中")) throw new Exception("stale token accepted");
        });
        test("named timeout cancels native late load before releasing lock", () =>
        {
            using var f = new Fixture(0); ZePracticePlugin.Cvars["mm_named_map_api"] = "1";
            f.Service.ForceChange(null, command); f.Advance(1); string token = Token();
            f.Advance(f.Plugin.Config.MapVote.LoadTimeoutSeconds);
            if (!Server.Commands.Contains("mm_cancel_workshop_map " + token) || f.Service.Diagnostics().Contains("引擎加载中")) throw new Exception("late load not cancelled");
        });
        test("unload cancels pending native load and stale next-frame callback", () =>
        {
            using var f = new Fixture(0); ZePracticePlugin.Cvars["mm_named_map_api"] = "1";
            f.Service.ForceChange(null, command); f.Service.Dispose(true); Server.Flush();
            if (Server.Commands.Any(c => c.StartsWith("mm_host_workshop_map "))) throw new Exception("stale command executed");
            if (!Server.Commands.Any(c => c.StartsWith("mm_cancel_workshop_map "))) throw new Exception("unload not cancelled");
        });
        test("map end invalidates named load and old timeout cannot affect a new map", () =>
        {
            using var f = new Fixture(0); ZePracticePlugin.Cvars["mm_named_map_api"] = "1";
            f.Service.ForceChange(null, command); f.Advance(1); string token = Token();
            f.Service.OnMapEnd(); Server.MapName = "ze_new"; f.Service.OnMapStart();
            f.Advance(f.Plugin.Config.MapVote.LoadTimeoutSeconds + 1);
            if (Server.Commands.Count(c => c == "mm_cancel_workshop_map " + token) != 1 || f.Service.Diagnostics().Contains("引擎加载中")) throw new Exception("old timer leaked into new map");
        });
        test("explicit owner by-ID load clears cancelled exact-name override first", () =>
        {
            using var f = new Fixture(0); ZePracticePlugin.Cvars["mm_named_map_api"] = "1";
            f.Service.ForceChange(null, "host_workshop_map 123"); f.Advance(1);
            if (!Server.Commands.SequenceEqual(new[] { "mm_allow_workshop_map 123", "host_workshop_map 123" })) throw new Exception("stale named override can block owner by-ID request");
        });
    }
}
