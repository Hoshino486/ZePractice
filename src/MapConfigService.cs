using System.Globalization;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;

namespace ZePractice;

public sealed class MapConfigApplyReport
{
    public string Map { get; set; } = "";
    public bool PracticeOverridesApplied { get; set; }
    public List<string> LoadedFiles { get; } = new();
    public List<string> Errors { get; } = new();
    public List<string> RejectedCommands { get; } = new();
    public Dictionary<string, string> AppliedConVars { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> AppliedManagedSettings { get; } = new();
    public Dictionary<string, string> UnsupportedSettings { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> DeferredSettings { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int DirectSettingCount { get; set; }
}

/// <summary>
/// Applies FYS files as validated data: no exec/say/raw command execution. Direct mappings target only
/// verified CS2/CS2Fixes convars. Unknown and unimplemented private features remain visible in LastReport.
/// </summary>
public sealed class MapConfigService : IDisposable
{
    private readonly ZePracticePlugin _plugin;
    private readonly Dictionary<string, Action<double>> _consumers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Original, string Last)> _owned = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, double> _values = FysConfigReader.CreateDefaults();
    private string _map = "";
    private bool _disposed;
    public MapConfigApplyReport LastReport { get; private set; } = new();
    public IReadOnlyDictionary<string, double> CurrentSettings => _values;
    public int SpawnHealthOverride => GetInt("ze_spawn_start_health_override");

    public MapConfigService(ZePracticePlugin plugin) => _plugin = plugin;

    public void Initialize()
    {
        _plugin.Logger.LogInformation("ZePractice FYS parameter reader: {Count} validated setting definitions; raw console commands are never executed.",
            FysConfigReader.Specifications.Count);
    }

    public void OnMapStart(string map)
    {
        _map = map;
        ApplyCurrentMap();
    }

    public void OnRoundStart()
    {
        if (_disposed) return;
        // Other plugins sometimes reapply infection settings at round start. Reassert only our mode overrides,
        // not the whole map file or spawn supplies, so per-round counters are left to their owning service.
        ApplyModeOverrides(LastReport);
    }

    /// <summary>Register a real implementation before map apply. Merely reading a value does not mark it implemented.</summary>
    public void RegisterManagedSettingHandler(string name, Action<double> consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        if (!FysConfigReader.Specifications.TryGetValue(name, out var spec) || spec.Disposition == FysSettingDisposition.ConVar)
            throw new ArgumentException("A managed or explicitly unsupported FYS setting is required.", nameof(name));
        _consumers[name] = consumer;
    }

    public int GetInt(string key, int fallback = 0) => _values.TryGetValue(key, out var value) ? (int)value : fallback;
    public float GetFloat(string key, float fallback = 0) => _values.TryGetValue(key, out var value) ? (float)value : fallback;
    public bool GetBool(string key, bool fallback = false) => _values.TryGetValue(key, out var value) ? value != 0 : fallback;

    public void ApplyCurrentMap()
    {
        if (_disposed) return;
        RestoreOwnedConVars();
        var report = new MapConfigApplyReport();
        _values = FysConfigReader.CreateDefaults(); // Every map starts fresh; omitted values never leak from the previous map.
        try
        {
            var key = !string.IsNullOrWhiteSpace(_plugin.CurrentMapKey) ? _plugin.CurrentMapKey : _map;
            report.Map = FysConfigReader.NormalizeMapKey(key);
            if (_plugin.Config.ApplyFysMapParameters)
            {
                MergeFile("_template", report, missingIsError: false);
                MergeFile(report.Map, report, missingIsError: true);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        { report.Errors.Add("Map path rejected; using defaults: " + ex.Message); }

        foreach (var pair in _values) report.Values[pair.Key] = pair.Value;
        if (_plugin.Config.ApplyFysMapParameters)
        {
            foreach (var (name, value) in _values)
            {
                var spec = FysConfigReader.Specifications[name];
                if (spec.Disposition == FysSettingDisposition.ConVar)
                {
                    var successes = 0;
                    foreach (var target in spec.Targets)
                    {
                        // FYS uses a relative multiplier where 1.0 is normal. Installed CS2Fixes
                        // cfg defaults zr_knockback_scale to 5.0; do not turn a normal map into 1/5 knockback.
                        var mapped = name.Equals("ze_knockback_scale", StringComparison.OrdinalIgnoreCase)
                            ? value * 5.0 : value;
                        if (SetConVar(target, mapped, report)) successes++;
                    }
                    if (successes == spec.Targets.Length) report.DirectSettingCount++;
                    continue;
                }
                if (_consumers.TryGetValue(name, out var consumer))
                {
                    try { consumer(value); report.AppliedManagedSettings.Add(name); }
                    catch (Exception ex) { report.Errors.Add(name + " consumer failed: " + ex.Message); }
                    continue;
                }
                if (spec.Disposition == FysSettingDisposition.Managed) report.DeferredSettings[name] = spec.Note;
                else report.UnsupportedSettings[name] = spec.Note;
            }
        }
        ApplyModeOverrides(report);
        LastReport = report;
        _plugin.Logger.LogInformation("FYS map {Map}: {Files} files; {Direct} direct settings; {Managed} managed settings; {Unsupported} unsupported; {Deferred} without consumer; {Errors} errors. Practice={Practice}.",
            report.Map, report.LoadedFiles.Count, report.DirectSettingCount, report.AppliedManagedSettings.Count,
            report.UnsupportedSettings.Count, report.DeferredSettings.Count, report.Errors.Count, _plugin.IsPractice);
        foreach (var error in report.Errors) _plugin.Logger.LogWarning("FYS config: {Error}", error);
        if (_plugin.Config.LogUnmappedSettings)
        {
            if (report.UnsupportedSettings.Count != 0) _plugin.Logger.LogWarning("FYS settings not implemented: {Settings}", string.Join(", ", report.UnsupportedSettings.Keys));
            if (report.DeferredSettings.Count != 0) _plugin.Logger.LogWarning("FYS settings need a registered gameplay consumer: {Settings}", string.Join(", ", report.DeferredSettings.Keys));
            foreach (var rejected in report.RejectedCommands) _plugin.Logger.LogWarning("FYS command rejected: {Setting}", rejected);
        }
    }

    private void MergeFile(string map, MapConfigApplyReport report, bool missingIsError)
    {
        string path;
        try { path = FysConfigReader.ResolveMapFile(_plugin.FysDirectory, _plugin.Config.ServerNumber, "csgo/cfg/map-configs", map, ".cfg"); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        { report.Errors.Add(map + ": " + ex.Message); return; }
        if (!File.Exists(path))
        {
            if (missingIsError) report.Errors.Add("No map CFG found (template defaults retained): " + path);
            return;
        }
        try
        {
            var parsed = FysConfigReader.ParseCfg(FysConfigReader.ReadBoundedText(path));
            foreach (var diagnostic in parsed.Diagnostics)
            {
                if (diagnostic.IsError) report.Errors.Add($"{path}:{diagnostic.Line}: {diagnostic.Message}");
                else if (diagnostic.Code == "unmapped") report.RejectedCommands.Add($"{path}:{diagnostic.Line}: {diagnostic.Message}");
            }
            if (!parsed.IsValid)
            {
                report.Errors.Add("Rejected this entire CFG; previous template/defaults retained: " + path);
                return;
            }
            foreach (var value in parsed.Values) _values[value.Key] = value.Value;
            report.LoadedFiles.Add(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
        { report.Errors.Add(path + ": " + ex.Message); }
    }

    private void ApplyModeOverrides(MapConfigApplyReport report)
    {
        // Practice must never infect a lone mapper or end the round simply because one team is empty.
        SetConVar("zr_enable", !_plugin.IsPractice && _plugin.Config.EnableZombieMode ? 1 : 0, report);
        SetConVar("mp_solid_teammates", _plugin.Config.NoBlock ? 0 : 1, report);
        SetConVar("cs2f_infinite_reserve_ammo", _plugin.Config.UnlimitedAmmo ? 1 : 0, report);
        if (!_plugin.IsPractice) return;
        SetConVar("mp_ignore_round_win_conditions", 1, report);
        SetConVar("mp_limitteams", 0, report);
        SetConVar("mp_autoteambalance", 0, report);
        SetConVar("mp_timelimit", 0, report);
        SetConVar("mp_maxrounds", 0, report);
        SetConVar("mp_freezetime", 0, report);
        SetConVar("mp_warmuptime", 5, report); // Current engine clamps to 5; main plugin explicitly ends practice warmup.
        SetConVar("mp_match_can_clinch", 0, report);
        SetConVar("mp_match_end_restart", 0, report);
        SetConVar("mp_match_end_changelevel", 0, report);
        SetConVar("mp_respawn_on_death_ct", _plugin.Config.AutoRespawn ? 1 : 0, report);
        SetConVar("mp_respawn_on_death_t", _plugin.Config.AutoRespawn ? 1 : 0, report);
        report.PracticeOverridesApplied = true;
    }

    private bool SetConVar(string name, double value, MapConfigApplyReport report)
    {
        var text = value.ToString("G17", CultureInfo.InvariantCulture);
        try
        {
            var cvar = ConVar.Find(name);
            if (cvar == null)
            {
                AddUniqueError(report, "ConVar is not present; setting was NOT applied: " + name);
                return false;
            }
            var original = _owned.TryGetValue(name, out var previous) ? previous.Original : cvar.StringValue;
            cvar.StringValue = text; // Native convar setter: no command concatenation, quoting, or exec.
            var actual = cvar.StringValue;
            _owned[name] = (original, actual);
            report.AppliedConVars[name] = actual;
            if (!EquivalentNumber(text, actual))
            {
                AddUniqueError(report, $"ConVar {name} requested {text}, read back {actual}; possible engine clamp/type mismatch.");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            AddUniqueError(report, name + " failed: " + ex.Message);
            return false;
        }
    }

    private static bool EquivalentNumber(string a, string b)
    {
        static bool Number(string text, out double value)
        {
            if (bool.TryParse(text, out var flag)) { value = flag ? 1 : 0; return true; }
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
        }
        return Number(a, out var x) && Number(b, out var y) && Math.Abs(x - y) <= 1e-5 * Math.Max(1, Math.Abs(x));
    }

    private static void AddUniqueError(MapConfigApplyReport report, string message)
    {
        if (!report.Errors.Contains(message, StringComparer.Ordinal)) report.Errors.Add(message);
    }

    private void RestoreOwnedConVars()
    {
        foreach (var (name, state) in _owned)
        {
            try
            {
                var cvar = ConVar.Find(name);
                // Do not overwrite a later intentional admin/other-module change on unload/map transition.
                if (cvar != null && (cvar.StringValue == state.Last || EquivalentNumber(cvar.StringValue, state.Last)))
                    cvar.StringValue = state.Original;
            }
            catch (Exception ex) { _plugin.Logger.LogWarning("Could not restore owned convar {Name}: {Error}", name, ex.Message); }
        }
        _owned.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        RestoreOwnedConVars();
        _consumers.Clear();
        _disposed = true;
    }
}