using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;

public sealed class PushSafetyNativeProbe : BasePlugin
{
    public override string ModuleName => "PushSafetyNativeProbe - isolated only";
    public override string ModuleVersion => "1.0";
    private readonly List<object> _results = [];
    private const string Signature = "48 89 5C 24 ? 48 89 6C 24 ? 48 89 74 24 ? 57 48 83 EC ? 44 8B 89 ? ? ? ? 33 DB 49 8B F8 48 8B EA 48 8B F1";
    private void Check(string name, bool passed, object? data = null)
    {
        _results.Add(new { name, passed, data });
        File.WriteAllText(Path.Combine(ModuleDirectory, "results.json"), JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true }));
        Server.PrintToConsole("PUSH_SAFETY_PROBE " + JsonSerializer.Serialize(new { name, passed, data }));
        if (!passed) throw new InvalidOperationException(name);
    }
    public override void Load(bool hotReload)
    {
        AddCommand("css_push_safety_probe", "Explicit native regression; isolated 27025, no humans", (p, c) =>
        {
            if (p != null || ConVar.Find("hostport")?.GetPrimitiveValue<int>() != 27025 || !Server.GameDirectory.Contains("ZePracticeTest-20261016") || Utilities.GetPlayers().Any(x => !x.IsBot && !x.IsHLTV)) return;
            _results.Clear();
            // This invokes the real engine helper through the installed native guard.
            // The same invalid-path case crashes unguarded build 2000932.
            var helper = new MemoryFunctionVoid<CTriggerPush, CBaseEntity, Vector>(Signature);
            Check("verified-path-handle-offset", Schema.GetSchemaOffset("CTriggerPush", "m_PathSimple") == 0x9f0);
            var trigger = Utilities.CreateEntityByName<CTriggerPush>("trigger_push")!;
            var other = Utilities.CreateEntityByName<CBaseEntity>("info_target")!;
            trigger.DispatchSpawn(); other.DispatchSpawn();
            trigger.UsePathSimple = true; trigger.PathSimpleName = "ze_missing_path_probe";
            trigger.PathSimple.Raw = uint.MaxValue;
            var direction = new Vector(99, 88, 77);
            helper.Invoke(trigger, other, direction);
            Check("invalid-path-returns-with-zero-impulse", direction.X == 0 && direction.Y == 0 && direction.Z == 0);
            Check("map-path-mode-not-mutated", trigger.UsePathSimple && trigger.PathSimpleName == "ze_missing_path_probe");
            var path = Utilities.CreateEntityByName<CPathSimple>("path_simple")!;
            path.DispatchSpawn();
            trigger.PathSimple.Raw = path.EntityHandle.Raw;
            Check("path-assigned", trigger.PathSimple.Value?.IsValid == true);
            path.Remove();
            AddTimer(.2f, () =>
            {
                try
                {
                    Check("path-deleted-handle-now-stale", trigger.IsValid && !path.IsValid && trigger.PathSimple.Value == null);
                    for (int i = 0; i < 10000; i++)
                    {
                        direction.X = 10; direction.Y = 20; direction.Z = 30;
                        helper.Invoke(trigger, other, direction);
                        if (direction.X != 0 || direction.Y != 0 || direction.Z != 0) throw new InvalidOperationException("nonzero stale-path impulse");
                    }
                    Check("10000-stale-path-calls-no-crash", true);
                    Check("no-persistent-mode-change", trigger.UsePathSimple);
                    Server.ExecuteCommand("cs2f_push_safety_status");
                    Server.PrintToConsole("PUSH_SAFETY_PROBE_COMPLETE " + _results.Count);
                }
                catch (Exception e) { Check("exception", false, e.ToString()); }
                finally { if (trigger.IsValid) trigger.Remove(); if (other.IsValid) other.Remove(); }
            });
        });
    }
}
