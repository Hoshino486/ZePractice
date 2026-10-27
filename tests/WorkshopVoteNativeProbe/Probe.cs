using System.Collections;
using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Plugin.Host;
using CounterStrikeSharp.API.Modules.Cvars;

public sealed class WorkshopVoteNativeProbe : BasePlugin
{
    public override string ModuleName => "WorkshopVoteNativeProbe - isolated 27025 only";
    public override string ModuleVersion => "1.0.0";
    private static object Field(object value, string name) => value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value)!;
    private static object Plugin()
    {
        var manager = (IPluginManager)Field(Application.Instance, "_pluginManager");
        return manager.GetLoadedPlugins().Single(p=>p.Plugin.GetType().FullName=="ZePractice.ZePracticePlugin").Plugin;
    }
    private static void Guard()
    {
        if (ConVar.Find("hostport")?.GetPrimitiveValue<int>() != 27025 || Utilities.GetPlayers().Any(p=>!p.IsBot && !p.IsHLTV))
            throw new InvalidOperationException("Only isolated port 27025 with no humans is permitted");
    }
    public override void Load(bool hotReload)
    {
        AddCommand("css_vote_session_probe", "Isolated nomination lifecycle check", (caller, command) =>
        {
            if (caller != null) return; Guard();
            object plugin = Plugin(), service = Field(plugin, "_mapVote");
            string action = command.GetArg(1);
            if (action == "inspect-install")
            {
                var backend = Field(Field(plugin, "_workshopUpdates"), "_backend");
                var result = backend.GetType().GetMethod("Inspect")!.Invoke(backend, [command.GetArg(2)]);
                string inspection = JsonSerializer.Serialize(new { action, id=command.GetArg(2), result, time=DateTimeOffset.Now });
                File.AppendAllText(Path.Combine(ModuleDirectory,"installation-results.jsonl"),inspection+Environment.NewLine);
                Server.PrintToConsole("WORKSHOP_INSTALL_PROBE " + inspection);
                return;
            }
            if (action == "seed")
                foreach (string id in new[] { "3815144923", "3521710695" })
                    service.GetType().GetMethod("Nominate")!.Invoke(service, [null, id]);
            var ledger = Field(service, "_ledger");
            var candidates = ((IEnumerable)ledger.GetType().GetProperty("Candidates")!.GetValue(ledger)!).Cast<string>().ToArray();
            var path = (string)Field(service, "_path");
            using var file = JsonDocument.Parse(File.ReadAllText(path));
            var persisted = file.RootElement.GetProperty("Candidates").EnumerateArray().Select(e=>e.GetString()).ToArray();
            bool pending = (bool)Field(service, "_pending"), executing = (bool)Field(service, "_executing");
            bool expected = action is "seed" or "expect-resumed" ? candidates.Length == 2 : action == "expect-empty" && candidates.Length == 0;
            bool passed = expected && candidates.SequenceEqual(persisted) && !pending && !executing &&
                !file.RootElement.GetProperty("ResumeAfterHotReload").GetBoolean() && file.RootElement.GetProperty("Map").GetString() == Server.MapName;
            var data = new { action, passed, map = Server.MapName, candidates, pending, executing,
                version = ((BasePlugin)plugin).ModuleVersion, time = DateTimeOffset.Now };
            string json = JsonSerializer.Serialize(data);
            File.AppendAllText(Path.Combine(ModuleDirectory, "results.jsonl"), json + Environment.NewLine);
            Server.PrintToConsole("VOTE_SESSION_PROBE " + json);
            if (!passed) throw new InvalidOperationException("Nomination lifecycle check failed: " + action);
        });
    }
}
