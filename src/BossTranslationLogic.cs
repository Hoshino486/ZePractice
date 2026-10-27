using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZePractice;

// Pure, engine-independent FYS readers and HUD state. These never execute config text.
public enum FysBossKind { Breakable, Counter, Monster }
public sealed record FysBossDefinition(
    FysBossKind Kind, string Display, string Target, string Iterator, string Backup,
    string Counter, string Hitbox, string Identity, int Stages, int Count,
    double? Mass, bool Increase, bool Reverse)
{
    public string PrimaryName => Kind == FysBossKind.Breakable ? Target : Iterator;
}
public sealed record FysBossDocument(IReadOnlyList<FysBossDefinition> Bosses, bool Proxy,
    IReadOnlyList<string> Diagnostics)
{
    public static FysBossDocument Empty { get; } = new(Array.Empty<FysBossDefinition>(), false, Array.Empty<string>());
}
public sealed record FysTranslationRule(string Translation, bool Blocked, bool ClearText,
    bool ClearTimer, int Countdown);

public static class FysBossReader
{
    internal static readonly JsonDocumentOptions JsonOptions = new()
    { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 32 };
    private static readonly HashSet<string> BossFields = new(StringComparer.OrdinalIgnoreCase)
    { "display", "target", "iterator", "backup", "counter", "hitbox", "identity", "stages", "count", "mass", "increase", "reverse" };

    public static FysBossDocument Parse(string json)
    {
        using var doc = JsonDocument.Parse(json.TrimStart('\uFEFF'), JsonOptions);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("Boss JSONC root must be an object.");
        var diagnostics = new List<string>();
        var bosses = new List<FysBossDefinition>();
        bool proxy = false;
        foreach (var section in doc.RootElement.EnumerateObject())
        {
            if (section.Name.Equals("Proxy", StringComparison.OrdinalIgnoreCase))
            {
                proxy = Boolean(section.Value);
                if (proxy) diagnostics.Add("Proxy=true: upstream's private proxy protocol is not specified in its public configuration. Explicit Counter/Breakable/Monster entries still run; no proxy HP is invented.");
                continue;
            }
            FysBossKind? kind = section.Name.ToLowerInvariant() switch
            { "breakables" => FysBossKind.Breakable, "counters" => FysBossKind.Counter, "monsters" => FysBossKind.Monster, _ => null };
            if (kind == null) { diagnostics.Add($"Unknown boss section: {section.Name}"); continue; }
            if (section.Value.ValueKind != JsonValueKind.Array) { diagnostics.Add($"{section.Name} must be an array."); continue; }
            int index = 0;
            foreach (var item in section.Value.EnumerateArray())
            {
                index++;
                if (item.ValueKind != JsonValueKind.Object) { diagnostics.Add($"{section.Name}[{index}] must be an object."); continue; }
                string Get(string key) => String(Property(item, key));
                foreach (var p in item.EnumerateObject())
                    if (!BossFields.Contains(p.Name)) diagnostics.Add($"{section.Name}[{index}]: unknown field {p.Name}.");
                var def = new FysBossDefinition(kind.Value, Get("display"), Get("target"), Get("iterator"),
                    Get("backup"), Get("counter"), Get("hitbox"), Get("identity"),
                    Math.Clamp(Integer(Property(item, "stages"), 1), 1, 1000),
                    Math.Clamp(Integer(Property(item, "count"), 10), 1, 32),
                    Number(Property(item, "mass")), Boolean(Property(item, "increase")), Boolean(Property(item, "reverse")));
                if (string.IsNullOrWhiteSpace(def.PrimaryName) && string.IsNullOrWhiteSpace(def.Identity))
                { diagnostics.Add($"{section.Name}[{index}] has neither an entity name nor identity; skipped."); continue; }
                if (string.IsNullOrWhiteSpace(def.Display)) def = def with { Display = def.PrimaryName.Length != 0 ? def.PrimaryName : def.Identity };
                bosses.Add(def);
            }
        }
        return new(bosses, proxy, diagnostics);
    }

    internal static JsonElement Property(JsonElement obj, string key)
    {
        if (obj.ValueKind == JsonValueKind.Object)
            foreach (var p in obj.EnumerateObject()) if (p.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return default;
    }
    internal static string String(JsonElement e) => e.ValueKind switch
    { JsonValueKind.String => e.GetString() ?? "", JsonValueKind.Number => e.GetRawText(), _ => "" };
    internal static double? Number(JsonElement e) => double.TryParse(String(e), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : null;
    internal static int Integer(JsonElement e, int fallback = 0)
    {
        double? n = Number(e);
        return n is >= int.MinValue and <= int.MaxValue ? (int)n.Value : fallback;
    }
    internal static bool Boolean(JsonElement e) => e.ValueKind == JsonValueKind.True ||
        (e.ValueKind == JsonValueKind.String && bool.TryParse(e.GetString(), out bool b) && b);
}

public static class FysEntityMatcher
{
    // Source2 point_template adds &NNNN to names. Do not treat arbitrary numeric IDs as entity indexes.
    public static string TemplateSuffix(string name)
    {
        int amp = name.LastIndexOf('&');
        if (amp < 0 || amp == name.Length - 1) return "";
        for (int i = amp + 1; i < name.Length; i++) if (!char.IsAsciiDigit(name[i])) return "";
        return name[amp..];
    }
    public static bool NameMatches(string pattern, string actual)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(actual)) return false;
        if (pattern.EndsWith('*')) return actual.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        if (pattern.Equals(actual, StringComparison.OrdinalIgnoreCase)) return true;
        var suffix = TemplateSuffix(actual);
        return suffix.Length > 0 && pattern.Equals(actual[..^suffix.Length], StringComparison.OrdinalIgnoreCase);
    }
    public static bool IdentityMatches(string expected, string actual) =>
        !string.IsNullOrEmpty(expected) && expected.Equals(actual, StringComparison.OrdinalIgnoreCase);
    public static bool IsPrimary(FysBossDefinition d, string name, string identity) =>
        !string.IsNullOrEmpty(d.Identity) ? IdentityMatches(d.Identity, identity) : NameMatches(d.PrimaryName, name);
}

public readonly record struct FysCounterReading(double Value, double Minimum, double Maximum, bool Disabled = false);
public readonly record struct BossGauge(double Current, double? Maximum, bool AscendingWithoutLimit)
{
    public static BossGauge FromCounter(FysCounterReading reading, bool increase, double observedMaximum,
        double? backupMaximum = null)
    {
        if (increase)
        {
            // Without a real upper limit an increasing iterator is progress, not a fabricated HP estimate.
            if (reading.Maximum <= reading.Minimum)
                return new(Math.Max(0, reading.Value), null, true);
            return new(Math.Max(0, reading.Maximum - reading.Value), reading.Maximum - reading.Minimum, false);
        }
        double current = Math.Max(0, reading.Value - reading.Minimum);
        double maximum = backupMaximum is > 0 ? backupMaximum.Value :
            reading.Maximum > reading.Minimum ? reading.Maximum - reading.Minimum : observedMaximum - reading.Minimum;
        if (maximum < current) maximum = current;
        return new(current, maximum > 0 ? maximum : null, false);
    }
    public string Bar(int count)
    {
        count = Math.Clamp(count, 1, 32);
        if (Maximum is not > 0) return "";
        int filled = Math.Clamp((int)Math.Ceiling(Current / Maximum.Value * count), 0, count);
        return "[" + new string('■', filled) + new string('□', count - filled) + "]";
    }
    public string Numbers => AscendingWithoutLimit ? $"进度 {Current:0.##} ↑" :
        Maximum is > 0 ? $"{Current:0}/{Maximum.Value:0}" : $"{Current:0}";
}

public sealed class BossStageTracker
{
    public int Current { get; private set; } = 1;
    private bool _atLimit;
    public void ReachedLimit() => _atLimit = true;
    public void ObserveAlive(bool alive, int total)
    {
        if (_atLimit && alive) { Current = Math.Min(total, Current + 1); _atLimit = false; }
    }
    public void Reset() { Current = 1; _atLimit = false; }
}

public sealed class FysTranslationCatalog
{
    private readonly Dictionary<string, FysTranslationRule> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FysTranslationRule?> _normalized = new(StringComparer.Ordinal);
    public List<string> Diagnostics { get; } = new();
    public int Count => _exact.Count;
    public static FysTranslationCatalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json.TrimStart('\uFEFF'), FysBossReader.JsonOptions);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("Translations root must be an object.");
        var result = new FysTranslationCatalog();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Object) { result.Diagnostics.Add("Non-object translation skipped: " + p.Name); continue; }
            var e = p.Value;
            string text = FysBossReader.String(FysBossReader.Property(e, "translation"));
            var rule = new FysTranslationRule(text, FysBossReader.Boolean(FysBossReader.Property(e, "blocked")),
                FysBossReader.Boolean(FysBossReader.Property(e, "clearText")), FysBossReader.Boolean(FysBossReader.Property(e, "clearTimer")),
                Math.Clamp(FysBossReader.Integer(FysBossReader.Property(e, "countdown")), 0, 86400));
            if (!result._exact.TryAdd(p.Name, rule)) { result._exact[p.Name] = rule; result.Diagnostics.Add("Duplicate translation key; last entry wins: " + p.Name); }
        }
        foreach (var (key, rule) in result._exact)
        {
            string normalized = Normalize(key);
            if (result._normalized.TryGetValue(normalized, out var old) && old != rule)
                result._normalized[normalized] = null; // Never guess between conflicting trimmed/color variants.
            else if (!result._normalized.ContainsKey(normalized)) result._normalized[normalized] = rule;
        }
        return result;
    }
    public bool TryResolve(string original, out FysTranslationRule rule)
    {
        if (_exact.TryGetValue(original, out rule!)) return true;
        if (_normalized.TryGetValue(Normalize(original), out var found) && found != null) { rule = found; return true; }
        rule = null!; return false;
    }
    public static string Normalize(string value)
    {
        value = FysHudText.Plain(value).Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1].Trim();
        return value;
    }
}

public sealed record TranslationHudTimer(long Id, string Text, double Deadline);
public sealed class FysTranslationState
{
    private long _sequence;
    private readonly List<(string Text, double Expires)> _texts = new();
    private readonly List<TranslationHudTimer> _timers = new();
    public int TimerCount => _timers.Count;
    public void Clear() { _texts.Clear(); _timers.Clear(); _sequence = 0; }
    public void Apply(FysTranslationRule rule, string source, double now, bool enableCountdown)
    {
        if (rule.ClearText) _texts.Clear();
        if (rule.ClearTimer) _timers.Clear();
        string text = string.IsNullOrEmpty(rule.Translation) ? source : rule.Translation;
        if (!rule.Blocked && !string.IsNullOrWhiteSpace(text))
        {
            _texts.Add((text, now + 8));
            if (_texts.Count > 3) _texts.RemoveAt(0);
        }
        // blocked hides the sentence, not its explicit independent countdown or clear actions.
        if (enableCountdown && rule.Countdown > 0)
        {
            _timers.Add(new(++_sequence, text, now + rule.Countdown));
            if (_timers.Count > 16) _timers.RemoveAt(0);
        }
    }
    public IReadOnlyList<string> Visible(double now, bool countdown)
    {
        _texts.RemoveAll(x => x.Expires <= now);
        _timers.RemoveAll(x => x.Deadline <= now);
        var result = new List<string>();
        foreach (var text in _texts.TakeLast(1)) result.Add(text.Text);
        if (countdown)
            foreach (var timer in _timers.OrderBy(t => t.Deadline).Take(2))
                result.Add($"⏱ {Math.Max(0, (int)Math.Ceiling(timer.Deadline - now))}s · {timer.Text}");
        return result;
    }
}

public static class FysHudText
{
    private static readonly Regex ColorToken = new(@"\{([A-Za-z]+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Dictionary<string, (char Chat, string Html)> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = ('\x01', "#FFFFFF"), ["white"] = ('\x01', "#FFFFFF"), ["red"] = ('\x07', "#FF5555"),
        ["green"] = ('\x04', "#55FF55"), ["lightgreen"] = ('\x05', "#A8FF80"), ["lime"] = ('\x06', "#BFFF00"),
        ["blue"] = ('\x0B', "#6699FF"), ["lightblue"] = ('\x0B', "#6699FF"), ["darkblue"] = ('\x0C', "#4466DD"),
        ["purple"] = ('\x03', "#CC88FF"), ["pink"] = ('\x0E', "#FF99DD"), ["lightred"] = ('\x0F', "#FFAAAA"),
        ["orange"] = ('\x10', "#FFBB55"), ["yellow"] = ('\x09', "#FFFF66"), ["gold"] = ('\x09', "#FFD966"),
        ["grey"] = ('\x08', "#AAAAAA"), ["gray"] = ('\x08', "#AAAAAA"), ["silver"] = ('\x0A', "#DDDDDD"),
        ["olive"] = ('\x05', "#A8FF80"), ["team"] = ('\x03', "#CC88FF")
    };
    public static string Plain(string text)
    {
        text = ColorToken.Replace(text, m => Colors.ContainsKey(m.Groups[1].Value) ? "" : m.Value);
        var s = new StringBuilder();
        foreach (char c in text) if (!char.IsControl(c) || c == '\n' || c == '\t') s.Append(c);
        return s.ToString();
    }
    public static string Chat(string text) => " " + '\x01' + ColorToken.Replace(Sanitize(text), m =>
        Colors.TryGetValue(m.Groups[1].Value, out var color) ? color.Chat.ToString() : m.Value);
    public static string Html(string text)
    {
        var result = new StringBuilder();
        int cursor = 0; bool span = false;
        foreach (Match match in ColorToken.Matches(text))
        {
            if (!Colors.TryGetValue(match.Groups[1].Value, out var color)) continue;
            result.Append(WebUtility.HtmlEncode(Plain(text[cursor..match.Index])));
            if (span) result.Append("</font>");
            result.Append("<font color=\"").Append(color.Html).Append("\">");
            span = true; cursor = match.Index + match.Length;
        }
        result.Append(WebUtility.HtmlEncode(Plain(text[cursor..])));
        if (span) result.Append("</font>");
        return result.ToString().Replace("\n", "<br>");
    }
    // No command execution. NUL and dangerous control characters are removed; accepted CS chat colors are retained.
    private static string Sanitize(string text) => new(text.Where(c => !char.IsControl(c) || (c >= '\x01' && c <= '\x10') || c == '\n').ToArray());
    public static string Limit(string text, int maximum)
    {
        if (text.Length <= maximum) return text;
        int length = maximum - 1;
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length] + "…";
    }
}