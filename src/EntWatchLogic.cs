using System.Globalization;
using System.Text.Json;

namespace ZePractice;

// Compatible JSONC schema for the published CS2Fixes EntWatch map files; no native plugin is loaded.
public enum ItemHandlerType { Button, CounterDown, CounterUp, Other }
public enum ItemHandlerMode { None = 1, Cooldown = 2, MaxUses = 3, CooldownAfterUses = 4, CounterValue = 5 }
public sealed record ItemHandlerDefinition(string Name, string HammerId, string Event, ItemHandlerType Type,
    ItemHandlerMode Mode, float Cooldown, int MaxUses, float Offset, float MaxOffset, bool Message,
    bool Ui, bool? Templated);
public sealed record ItemDefinition(string Key, string Name, string ShortName, string HammerId,
    string Color, bool Message, bool Ui, bool? Transfer, bool? Templated, bool? Glow, bool Knife,
    IReadOnlyList<string> Triggers, IReadOnlyList<ItemHandlerDefinition> Handlers);
public sealed record ItemWatchDocument(IReadOnlyList<ItemDefinition> Items, IReadOnlyList<string> Diagnostics)
{
    public static ItemWatchDocument Empty { get; } = new(Array.Empty<ItemDefinition>(), Array.Empty<string>());
}
public static class ItemWatchParser
{
    public static ItemWatchDocument Parse(string json)
    {
        using var doc = JsonDocument.Parse(json.TrimStart('\uFEFF'), FysBossReader.JsonOptions);
        if (doc.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            throw new FormatException("EntWatch maps/<map>.jsonc needs an array or keyed object root.");
        var items = new List<ItemDefinition>(); var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = doc.RootElement.ValueKind == JsonValueKind.Object
            ? doc.RootElement.EnumerateObject().Select(x => (x.Name,x.Value))
            : doc.RootElement.EnumerateArray().Select((x,i) => (Name:$"item{i+1}",Value:x));
        foreach (var entry in entries)
        {
            if (entry.Value.ValueKind != JsonValueKind.Object)
            { warnings.Add($"{entry.Name}: item definition isn't an object, skipped"); continue; }
            var obj = entry.Value; string hid = String(obj,"hammerid");
            if (hid.Length == 0) { warnings.Add($"{entry.Name}: missing hammerid, skipped"); continue; }
            if (!seen.Add(hid)) { warnings.Add($"{entry.Name}: duplicate hammerid={hid}, skipped to avoid ambiguous ownership"); continue; }
            var name = String(obj,"name"); if (string.IsNullOrEmpty(name)) name = entry.Name;
            var shortName = String(obj,"shortname"); if (string.IsNullOrEmpty(shortName)) shortName = name;
            var handlers = new List<ItemHandlerDefinition>();
            var rawHandlers = FysBossReader.Property(obj,"handlers");
            if (rawHandlers.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                var handlerEntries = rawHandlers.ValueKind == JsonValueKind.Object
                    ? rawHandlers.EnumerateObject().Select(x => (x.Name,x.Value))
                    : rawHandlers.EnumerateArray().Select((x,i) => (Name:$"handler{i+1}",Value:x));
                foreach (var handler in handlerEntries)
                {
                    if (handler.Value.ValueKind != JsonValueKind.Object) continue;
                    var e = handler.Value;
                    string type = String(e,"type");
                    var handlerType = type.ToLowerInvariant() switch { "button" => ItemHandlerType.Button, "counterdown" => ItemHandlerType.CounterDown, "counterup" => ItemHandlerType.CounterUp, _ => ItemHandlerType.Other };
                    int mode = FysBossReader.Integer(FysBossReader.Property(e,"mode"),1);
                    if (mode is < 1 or > 5) { warnings.Add($"{entry.Name}.{handler.Name}: unsupported mode={mode}; using None"); mode = 1; }
                    string handlerName = String(e,"name"); if (handlerName.Length == 0) handlerName = handler.Name;
                    string handlerId = String(e,"hammerid");
                    if (handlerId.Length == 0) { warnings.Add($"{entry.Name}.{handler.Name}: no hammerid, skipped"); continue; }
                    var offsets = FysBossReader.Property(e,"offset");
                    float low=0, high=0;
                    if (offsets.ValueKind == JsonValueKind.Array)
                    {
                        var numbers = offsets.EnumerateArray().Take(2).Select(x => (float)(FysBossReader.Number(x) ?? 0)).ToArray();
                        if (numbers.Length>0) {low=numbers[0];high=numbers.Length>1?numbers[1]:low;}
                    }
                    else if (FysBossReader.Number(offsets) is { } number) low=high=(float)number;
                    handlers.Add(new(handlerName,handlerId,String(e,"event"),handlerType,(ItemHandlerMode)mode,
                        Math.Clamp((float)(FysBossReader.Number(FysBossReader.Property(e,"cooldown"))??0),0,86400),
                        Math.Clamp(FysBossReader.Integer(FysBossReader.Property(e,"maxuses")),0,999999),low,high,
                        Bool(e,"message",true),Bool(e,"ui",true),OptionalBool(e,"templated")));
                }
            }
            var triggers = new List<string>();
            var rawTriggers = FysBossReader.Property(obj,"triggers");
            if (rawTriggers.ValueKind == JsonValueKind.Array)
                triggers.AddRange(rawTriggers.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()!).Where(x=>!string.IsNullOrWhiteSpace(x)));
            string legacyTrigger = String(obj,"triggerid");
            if (legacyTrigger.Length>0 && !triggers.Contains(legacyTrigger,StringComparer.OrdinalIgnoreCase)) triggers.Add(legacyTrigger);
            if (obj.TryGetProperty("hud",out var legacyHud) && FysBossReader.Property(obj,"ui").ValueKind==JsonValueKind.Undefined)
                warnings.Add($"{entry.Name}: legacy hud field mapped to ui; official CS2Fixes schema uses ui");
            bool ui = Bool(obj,"ui",Bool(obj,"hud",true));
            bool? glow = OptionalBool(obj,"glow");
            if (glow==true) warnings.Add($"{entry.Name}: glow=true accepted as metadata, but CSS implementation does not apply entity glow");
            var item = new ItemDefinition(entry.Name,name,shortName,hid,String(obj,"color",String(obj,"colour","default")),
                Bool(obj,"message",true),ui,OptionalBool(obj,"transfer"),OptionalBool(obj,"templated"),glow,
                Bool(obj,"knife",false),triggers,handlers);
            if (item.Knife && triggers.Count==0) warnings.Add($"{entry.Name}: knife=true without triggerid/triggers; pickup restriction cannot be matched by a trigger");
            items.Add(item);
        }
        return new(items,warnings);
    }
    private static string String(JsonElement obj,string key,string fallback="") => FysBossReader.String(FysBossReader.Property(obj,key)) is {Length:>0} s ? s : fallback;
    private static bool Bool(JsonElement obj,string key,bool fallback)
    {
        var e=FysBossReader.Property(obj,key);return e.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ?fallback:FysBossReader.Boolean(e);
    }
    private static bool? OptionalBool(JsonElement obj,string key)
    {
        var e=FysBossReader.Property(obj,key);return e.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ?null:FysBossReader.Boolean(e);
    }
}
public sealed class ItemHandlerState
{
    public ItemHandlerDefinition Definition { get; }
    public int Uses { get; private set; }
    public int EffectiveMaxUses { get; private set; }
    public float CounterValue { get; private set; }
    public float CounterMaximum { get; private set; }
    public bool ObservedCounter { get; private set; }
    public double LastUse { get; private set; } = double.NegativeInfinity;
    public ItemHandlerState(ItemHandlerDefinition definition) { Definition = definition; EffectiveMaxUses = definition.MaxUses; }
    private bool IsCounter => Definition.Type is ItemHandlerType.CounterDown or ItemHandlerType.CounterUp;
    private void Refresh(double now)
    {
        // Only buttons synthesize the configured recharge cycle. Counters remain authoritative map state.
        if (!IsCounter && Definition.Mode == ItemHandlerMode.CooldownAfterUses && Uses >= EffectiveMaxUses &&
            EffectiveMaxUses > 0 && now >= LastUse + Definition.Cooldown && !double.IsNegativeInfinity(LastUse))
            Uses = 0;
    }
    public bool CanUse(double now)
    {
        Refresh(now);
        return Definition.Mode switch
        {
            ItemHandlerMode.None or ItemHandlerMode.CounterValue => true,
            ItemHandlerMode.Cooldown => now >= LastUse + Definition.Cooldown,
            ItemHandlerMode.MaxUses => (EffectiveMaxUses <= 0 || Uses < EffectiveMaxUses) && now >= LastUse + Definition.Cooldown,
            ItemHandlerMode.CooldownAfterUses => EffectiveMaxUses <= 0 || Uses < EffectiveMaxUses,
            _ => true
        };
    }
    public bool RecordUse(double now)
    {
        if (!CanUse(now)) return false;
        switch (Definition.Mode)
        {
            case ItemHandlerMode.MaxUses: Uses++; LastUse = now; break;
            case ItemHandlerMode.CooldownAfterUses:
                Uses++;
                if (EffectiveMaxUses > 0 && Uses >= EffectiveMaxUses) LastUse = now;
                break;
            case ItemHandlerMode.Cooldown: LastUse = now; break;
        }
        return true;
    }
    public bool ObserveCounter(float value, float minimum, float maximum, double now)
    {
        if (!float.IsFinite(value) || !float.IsFinite(minimum) || !float.IsFinite(maximum)) return false;
        var d = Definition;
        bool previouslyObserved = ObservedCounter;
        ObservedCounter = true;
        if (d.Mode == ItemHandlerMode.CounterValue)
        {
            CounterValue = Math.Max(0, (d.Type == ItemHandlerType.CounterDown ? value - minimum : maximum - value) + d.Offset);
            CounterMaximum = Math.Max(0, maximum - minimum + d.MaxOffset);
            return false; // Sampling energy is not by itself proof a player used the item.
        }
        if (d.Mode == ItemHandlerMode.Cooldown) { if (previouslyObserved) LastUse = now; return previouslyObserved; }
        int current = Math.Max(0, (int)Math.Round((d.Type == ItemHandlerType.CounterDown ? maximum - value : value - minimum) + d.Offset));
        EffectiveMaxUses = Math.Max(0, (int)Math.Round(maximum - minimum + d.MaxOffset));
        if (d.Mode is ItemHandlerMode.MaxUses or ItemHandlerMode.CooldownAfterUses)
        {
            int old = Uses; Uses = current;
            if (!previouslyObserved || current <= old) return false;
            if (d.Mode == ItemHandlerMode.MaxUses || current >= EffectiveMaxUses) LastUse = now;
            return true;
        }
        return false;
    }
    public string Label(double now)
    {
        Refresh(now);
        if (IsCounter && !ObservedCounter) return "等待输出";
        double cd = double.IsNegativeInfinity(LastUse) ? 0 : Math.Max(0, Math.Ceiling(LastUse + Definition.Cooldown - now));
        return Definition.Mode switch
        {
            ItemHandlerMode.None => "+",
            ItemHandlerMode.Cooldown => cd > 0 ? $"{cd:0}s" : "R",
            ItemHandlerMode.MaxUses => cd > 0 ? $"{cd:0}s" : EffectiveMaxUses > 0 && Uses >= EffectiveMaxUses ? "E" : $"{Uses}/{(EffectiveMaxUses > 0 ? EffectiveMaxUses.ToString(CultureInfo.InvariantCulture) : "?")}",
            ItemHandlerMode.CooldownAfterUses => cd > 0 && EffectiveMaxUses > 0 && Uses >= EffectiveMaxUses ? $"{cd:0}s" : $"{Uses}/{(EffectiveMaxUses > 0 ? EffectiveMaxUses.ToString(CultureInfo.InvariantCulture) : "?")}",
            ItemHandlerMode.CounterValue => CounterValue <= 0 ? "E" : $"{CounterValue:0}/{CounterMaximum:0}",
            _ => "?"
        };
    }
    public void Reset() { Uses = 0; EffectiveMaxUses = Definition.MaxUses; CounterValue = 0; CounterMaximum = 0; ObservedCounter = false; LastUse = double.NegativeInfinity; }
}