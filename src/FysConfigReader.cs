using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZePractice;

public enum FysSettingDisposition { ConVar, Managed, Unsupported }
public enum FysNumericKind { Float, Integer, Boolean }
public sealed record FysSettingSpec(string Name, FysNumericKind Kind, double Minimum, double Maximum,
    double DefaultValue, FysSettingDisposition Disposition, string[] Targets, string Note = "");
public sealed record FysConfigDiagnostic(int Line, string Code, string Message, bool IsError = false);
public sealed class FysCfgParseResult
{
    public Dictionary<string, double> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<FysConfigDiagnostic> Diagnostics { get; } = new();
    public List<string> MetadataMessages { get; } = new();
    public int SettingLines { get; internal set; }
    public bool IsValid => !Diagnostics.Any(x => x.IsError);
}

/// <summary>Strict data reader. Never sends the content of a FYS file to a console/command interpreter.</summary>
public static class FysConfigReader
{
    public const int MaximumFileBytes = 2 * 1024 * 1024;
    private static readonly Regex SafeKey = new("^[a-zA-Z0-9_][a-zA-Z0-9_.-]{0,159}$", RegexOptions.CultureInvariant);
    private static readonly Regex SafeServer = new("^[0-9]{1,8}$", RegexOptions.CultureInvariant);
    private static readonly Regex CommandName = new("^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static IReadOnlyDictionary<string, FysSettingSpec> Specifications { get; } = BuildSpecifications();

    private static IReadOnlyDictionary<string, FysSettingSpec> BuildSpecifications()
    {
        var result = new Dictionary<string, FysSettingSpec>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, FysNumericKind kind, double min, double max, double value,
            FysSettingDisposition disposition, string[]? targets = null, string note = "") =>
            result.Add(name, new(name, kind, min, max, value, disposition, targets ?? Array.Empty<string>(), note));
        void Float(string name, double min, double max, double value, params string[] targets) =>
            Add(name, FysNumericKind.Float, min, max, value, FysSettingDisposition.ConVar, targets);
        void Int(string name, double min, double max, double value, params string[] targets) =>
            Add(name, FysNumericKind.Integer, min, max, value, FysSettingDisposition.ConVar, targets);
        void Bool(string name, bool value, params string[] targets) =>
            Add(name, FysNumericKind.Boolean, 0, 1, value ? 1 : 0, FysSettingDisposition.ConVar, targets);
        Float("mp_timelimit", 1, 70, 45, "mp_timelimit");
        Float("mp_roundtime", 1, 60, 30, "mp_roundtime", "mp_roundtime_defuse", "mp_roundtime_hostage");
        Float("sv_falldamage_scale", 0, 3, 1, "sv_falldamage_scale");
        Int("mcr_map_extend_times", 0, 3, 0, "cs2f_extends");
        Add("vip_map_extend_times", FysNumericKind.Integer, 0, 2, 1, FysSettingDisposition.Unsupported,
            note: "FYS VIP-specific extension privilege has no equivalent in the CS2Fixes map-wide extension counter.");
        Bool("ms_flashlight_enabled", false, "cs2f_flashlight_enable");
        Float("ze_cash_damage_zombie", 0.1, 3, 0.8, "zr_damage_cash_scale");
        Int("ze_infect_mother_ratio", 1, 64, 7, "zr_infect_spawn_mz_ratio");
        Int("ze_infect_mother_spawn_time", 10, 90, 15, "zr_infect_spawn_time_min", "zr_infect_spawn_time_max");
        Bool("ze_infect_teleport_to_spawn", true, "zr_infect_spawn_type");
        Add("ze_infect_sort_by_immunity", FysNumericKind.Boolean, 0, 1, 1, FysSettingDisposition.Unsupported,
            note: "FYS immunity ordering cannot be equated to CS2Fixes zr_mz_immunity_reduction.");
        Float("ze_knockback_scale", 0.1, 6, 1, "zr_knockback_scale");
        Add("ze_spawn_start_health_override", FysNumericKind.Integer, 0, 1000, 0, FysSettingDisposition.Managed,
            note: "Player-spawn health consumer; zero means use the plugin/server default.");
        foreach (var (key, max, value) in new (string, int, int)[] {
            ("hegrenade", 2, 1), ("molotov", 2, 0), ("decoy", 2, 1) })
            Add("ze_weapons_spawn_" + key, FysNumericKind.Integer, 0, max, value, FysSettingDisposition.Managed,
                note: "Per-player round spawn supply; needs the registered spawn/grenade consumer.");
        foreach (var (key, max, value) in new (string, int, int)[] {
            ("hegrenade",25,4), ("molotov",20,2), ("decoy",15,2), ("flash",5,2), ("smoke",3,1), ("adrenaline",15,3) })
            Add("ze_weapons_round_" + key, FysNumericKind.Integer, -1, max, value, FysSettingDisposition.Managed,
                note: "Per-player round purchase limit; -1 is the upstream unlimited sentinel, 0 disables purchases.");
        foreach (var (key, kind, min, max, value) in new (string,FysNumericKind,double,double,double)[] {
            ("hunter_power",FysNumericKind.Float,160,2000,180), ("faster_speed",FysNumericKind.Float,1.05,2,1.4),
            ("blader_damage",FysNumericKind.Float,48,1000,64), ("deimos_amount",FysNumericKind.Integer,3,10,5),
            ("yaksha_damage",FysNumericKind.Integer,1,100,2), ("dancer_speed",FysNumericKind.Float,0.1,1,0.6),
            ("farter_regen",FysNumericKind.Integer,1000,100000,5000), ("cirrus_range",FysNumericKind.Integer,96,256,128),
            ("smoker_range",FysNumericKind.Integer,256,5000,500) })
            Add("ze_skill_" + key, kind, min, max, value, FysSettingDisposition.Unsupported,
                note: "FYS private zombie-skill behavior is not implemented; value is preserved for an explicit future consumer.");
        return new ReadOnlyDictionary<string, FysSettingSpec>(result);
    }

    public static Dictionary<string, double> CreateDefaults() => Specifications.ToDictionary(
        x => x.Key, x => x.Value.DefaultValue, StringComparer.OrdinalIgnoreCase);

    public static string NormalizeMapKey(string map)
    {
        if (string.IsNullOrWhiteSpace(map) || map.IndexOfAny(new[] { '\r', '\n', '\0', '\\', ':' }) >= 0)
            throw new ArgumentException("Invalid map name.", nameof(map));
        // The engine may identify Workshop maps by workshop/<numeric-id>/<map>, never arbitrary paths.
        if (map.StartsWith("workshop/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = map.Split('/');
            if (parts.Length != 3 || parts[1].Length == 0 || parts[1].Any(c => c is < '0' or > '9'))
                throw new ArgumentException("Invalid Workshop map path.", nameof(map));
            map = parts[2];
        }
        if (map.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase)) map = map[..^4];
        if (!SafeKey.IsMatch(map) || map.Contains("..", StringComparison.Ordinal) || map.EndsWith('.'))
            throw new ArgumentException("Map names must be simple basenames, not paths.", nameof(map));
        return map.ToLowerInvariant();
    }

    public static string ResolveMapFile(string root, string serverNumber, string relativeDirectory, string map, string extension)
    {
        if (!SafeServer.IsMatch(serverNumber)) throw new ArgumentException("Invalid FYS server number.", nameof(serverNumber));
        if (extension is not ".cfg" and not ".json" and not ".jsonc") throw new ArgumentException("Unsupported extension.");
        if (Path.IsPathRooted(relativeDirectory) || relativeDirectory.IndexOfAny(new[] { '\r', '\n', '\0', ':' }) >= 0)
            throw new ArgumentException("Invalid relative directory.");
        var segments = relativeDirectory.Replace('\\', '/').Split('/');
        if (segments.Any(x => x.Length == 0 || !SafeKey.IsMatch(x) || x.Contains("..", StringComparison.Ordinal)))
            throw new ArgumentException("Directory traversal rejected.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var relative = Path.Combine(new[] { serverNumber }.Concat(segments).Append(NormalizeMapKey(map) + extension).ToArray());
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Resolved file leaves FYS root.");
        // Do not let a junction/symlink below the selected root turn a harmless map name into an external read.
        var cursor = fullRoot;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            cursor = Path.Combine(cursor, segment);
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Symlink/junction below FYS root is not accepted: " + cursor);
        }
        return full;
    }

    public static string ReadBoundedText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("Configuration file exceeds 2 MiB.");
        using var reader = new StreamReader(stream, StrictUtf8, detectEncodingFromByteOrderMarks: true);
        var builder = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) != 0)
        {
            builder.Append(buffer, 0, read);
            if (builder.Length > MaximumFileBytes) throw new InvalidDataException("Configuration grew beyond 2 MiB.");
        }
        var text = builder.ToString();
        if (StrictUtf8.GetByteCount(text) > MaximumFileBytes) throw new InvalidDataException("Configuration grew beyond 2 MiB.");
        return text;
    }

    public static JsonDocument ReadJsonc(string path) => ParseJsonc(ReadBoundedText(path));
    public static JsonDocument ParseJsonc(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumFileBytes) throw new InvalidDataException("JSONC exceeds 2 MiB.");
        return JsonDocument.Parse(text.TrimStart('\uFEFF'), new JsonDocumentOptions {
            CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 64 });
    }
    public static bool TryReadJsonc(string path, out JsonDocument? document, out string? error)
    {
        try { document = ReadJsonc(path); error = null; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or DecoderFallbackException)
        { document = null; error = ex.Message; return false; }
    }

    public static FysCfgParseResult ParseCfg(string text)
    {
        var result = new FysCfgParseResult();
        if (Encoding.UTF8.GetByteCount(text) > MaximumFileBytes)
        { result.Diagnostics.Add(new(0, "size", "CFG exceeds 2 MiB.", true)); return result; }
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        string? line; var lineNumber = 0;
        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            if (!TryTokenizeLine(line, out var tokens, out var error))
            { result.Diagnostics.Add(new(lineNumber, "syntax", error!, true)); continue; }
            if (tokens.Count == 0) continue;
            var name = tokens[0];
            if (!CommandName.IsMatch(name))
            { result.Diagnostics.Add(new(lineNumber, "command", "Invalid setting name.", true)); continue; }
            if (name.Equals("echo", StringComparison.OrdinalIgnoreCase) || name.Equals("say", StringComparison.OrdinalIgnoreCase))
            {
                result.MetadataMessages.Add(string.Join(' ', tokens.Skip(1)));
                result.Diagnostics.Add(new(lineNumber, "metadata", name + " kept as metadata, not executed."));
                continue;
            }
            if (!Specifications.TryGetValue(name, out var spec))
            {
                result.Diagnostics.Add(new(lineNumber, "unmapped", "Unrecognized command/setting rejected: " + name));
                continue;
            }
            if (tokens.Count != 2)
            { result.Diagnostics.Add(new(lineNumber, "arity", name + " requires exactly one value.", true)); continue; }
            if (!TryParseValue(spec, tokens[1], out var value, out error))
            { result.Diagnostics.Add(new(lineNumber, "value", name + ": " + error, true)); continue; }
            if (result.Values.ContainsKey(name)) result.Diagnostics.Add(new(lineNumber, "duplicate", name + ": last value wins."));
            result.Values[name] = value;
            result.SettingLines++;
        }
        return result;
    }

    public static bool TryParseValue(FysSettingSpec spec, string text, out double value, out string? error)
    {
        value = 0; error = null;
        if (spec.Kind == FysNumericKind.Boolean && bool.TryParse(text, out var flag)) value = flag ? 1 : 0;
        else if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !double.IsFinite(value))
        { error = "Expected a finite invariant-culture number or boolean."; return false; }
        if (value < spec.Minimum || value > spec.Maximum ||
            (spec.Kind is FysNumericKind.Integer or FysNumericKind.Boolean && value != Math.Truncate(value)))
        { error = $"Expected {spec.Kind} in [{spec.Minimum.ToString(CultureInfo.InvariantCulture)}, {spec.Maximum.ToString(CultureInfo.InvariantCulture)}]."; return false; }
        return true;
    }

    private static bool TryTokenizeLine(string line, out List<string> tokens, out string? error)
    {
        tokens = new(); error = null; var token = new StringBuilder(); var quoted = false; var hasToken = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\0' || c == ';' || (char.IsControl(c) && c != '\t'))
            { error = "Control characters and command separators are not accepted."; return false; }
            if (!quoted && c == '/' && i + 1 < line.Length && line[i + 1] == '/') break;
            if (c == '"') { quoted = !quoted; hasToken = true; continue; }
            if (quoted && c == '\\' && i + 1 < line.Length && line[i + 1] is '"' or '\\')
            { token.Append(line[++i]); hasToken = true; continue; }
            if (!quoted && char.IsWhiteSpace(c))
            { if (hasToken) { tokens.Add(token.ToString()); token.Clear(); hasToken = false; } continue; }
            token.Append(c); hasToken = true;
        }
        if (quoted) { error = "Unterminated quoted value."; return false; }
        if (hasToken) tokens.Add(token.ToString());
        return true;
    }
}