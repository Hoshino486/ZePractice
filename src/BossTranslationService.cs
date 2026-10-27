using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using CssTimer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace ZePractice;

/// <summary>
/// Original FYS JSONC -> live entity output/health readings and translated server chat.
/// Never writes entity HP, fires GetValue/OnGetValue, executes translated commands, or patches native offsets.
/// Counter values are observed via outputs; until observed the HUD explicitly says waiting, not fake HP.
/// Default HUD is TextMsg center, not the survival HTML event used by many item HUDs.
/// IMPORTANT: upstream CS2Fixes labels entwatch_enable INCOMPATIBLE WITH CS#; channel separation is NOT a compatibility fix.
/// </summary>
public sealed class BossTranslationService : IDisposable
{
    private readonly ZePracticePlugin _plugin;
    private FysBossDocument _document = FysBossDocument.Empty;
    private FysTranslationCatalog _translations = new();
    private readonly FysTranslationState _translationState = new();
    private readonly Dictionary<uint, EntityMeta> _entities = new();
    private readonly Dictionary<uint, CounterObservation> _counters = new();
    private readonly Dictionary<(int, uint), BoundBoss> _bosses = new();
    private readonly Dictionary<uint, double> _lastHit = new();
    private readonly Dictionary<uint, double> _deleted = new();
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _wildcards = new();
    private readonly HashSet<string> _identities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, double> _foreignHudUntil = new();
    private readonly HashSet<uint> _muted = new();
    private readonly HashSet<uint> _ownHud = new();
    private readonly List<(string Class, string Output, EntityIO.EntityOutputHandler Handler)> _outputHooks = new();
    private readonly List<(int Id, UserMessage.UserMessageHandler Handler)> _messageHooks = new();
    private readonly Dictionary<string, double> _warningTimes = new();
    private CssTimer? _timer;
    private bool _initialized, _disposed, _sendingChat, _sendingHud, _mapActive;
    private double _nextScan, _lastScan, _roundReadyAt, _reportAt, _lastSnapshotLog;
    private int _generation, _lastBoundCount = -1;
    private bool _dirty;
    private string _map = "";
    /// <summary>center (default, separate from HTML item HUD), html (explicit opt-in), off.</summary>
    public string HudChannel { get; private set; } = "center";

    public BossTranslationService(ZePracticePlugin plugin) => _plugin = plugin;
    private static double Now => Server.CurrentTime;
    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        foreach (string name in new[] { "OutValue", "OnGetValue", "OnHitMin", "OnHitMax" }) HookOutput("math_counter", name);
        foreach (string name in new[] { "OnHealthChanged", "OnTakeDamage", "OnBreak", "OnKilled" }) HookOutput("*", name);
        _plugin.RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawned);
        _plugin.RegisterListener<Listeners.OnEntityDeleted>(OnEntityDeleted);
        _plugin.RegisterListener<Listeners.OnEntityTakeDamagePost>(OnEntityDamage);
        _plugin.RegisterListener<Listeners.OnClientDisconnect>(OnDisconnect);
        _plugin.RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
        _plugin.AddCommandListener("say", OnServerSay, HookMode.Pre);
        _plugin.AddZeCommand("css_bosshud", "Toggle your Boss/translation HUD: on|off", OnHudCommand);
        _plugin.AddZeCommand("css_bossstatus", "Show live Boss/config bindings", OnStatusCommand);
        _plugin.AddZeCommand("css_bosshudmode", "Admin: center|html|off", OnModeCommand);
        _timer = _plugin.AddTimer(Math.Clamp(_plugin.Config.HudIntervalSeconds, 0.2f, 2f), Tick, TimerFlags.REPEAT);
    }
    private void HookOutput(string classname, string output)
    {
        // CSS stores hooks by Delegate. Each registration needs its OWN closure, including identical callback bodies.
        EntityIO.EntityOutputHandler handler = (o, name, activator, caller, value, delay) =>
            OnOutput(classname, output, caller, value);
        _plugin.HookEntityOutput(classname, output, handler, HookMode.Pre);
        _outputHooks.Add((classname, output, handler));
    }
    private void HookMessage(string partialName)
    {
        try
        {
            int id = UserMessage.FindIdByName(partialName);
            if (id < 0) { Warn("message-" + partialName, $"Usermessage {partialName} not registered; server say listener remains active."); return; }
            UserMessage.UserMessageHandler handler = message => OnUserMessage(message, partialName);
            _plugin.HookUserMessage(id, handler, HookMode.Pre);
            _messageHooks.Add((id, handler));
        }
        catch (Exception e) { Warn("message-" + partialName, $"Cannot attach {partialName}: {e.Message}"); }
    }
    public void OnMapStart(string map)
    {
        if (_messageHooks.Count == 0) { HookMessage("SayText2"); HookMessage("TextMsg"); }
        var entwatch = ConVar.Find("entwatch_enable");
        if (entwatch?.StringValue == "1")
            _plugin.Logger.LogWarning("Native CS2Fixes entwatch_enable is enabled, but CS2Fixes marks it incompatible with CS#.");
        _generation++; _map = _plugin.CurrentMapKey; _mapActive = true;
        ResetLiveState();
        _document = FysBossDocument.Empty; _translations = new();
        if (string.IsNullOrEmpty(_map)) return;
        string configRoot = Path.Combine(_plugin.FysDirectory, _plugin.Config.ServerNumber, "sharp", "configs");
        string bossFile = Path.Combine(configRoot, "bosses", _map + ".jsonc");
        string translationFile = Path.Combine(configRoot, "translations", _map + ".jsonc");
        try { if (File.Exists(bossFile)) _document = FysBossReader.Parse(File.ReadAllText(bossFile)); }
        catch (Exception e) { _plugin.Logger.LogError(e, "Boss file failed: {Path}; Boss module disabled for this map, translations remain independent.", bossFile); }
        try { if (File.Exists(translationFile)) _translations = FysTranslationCatalog.Parse(File.ReadAllText(translationFile)); }
        catch (Exception e) { _plugin.Logger.LogError(e, "Translation file failed: {Path}; original messages will pass through.", translationFile); }
        _names.Clear(); _identities.Clear(); _wildcards.Clear();
        foreach (var d in _document.Bosses)
        {
            foreach (var name in new[] { d.PrimaryName, d.Backup, d.Counter, d.Hitbox })
                if (name.EndsWith('*')) _wildcards.Add(name); else if (name.Length > 0) _names.Add(name);
            if (d.Identity.Length > 0) _identities.Add(d.Identity);
        }
        foreach (var message in _document.Diagnostics.Concat(_translations.Diagnostics).Take(12))
            _plugin.Logger.LogWarning("FYS {Map}: {Detail}", _map, message);
        _plugin.Logger.LogInformation("FYS_BOSS_TRANSLATION map={Map} runtime_map={RuntimeMap} boss_rules={Count} translations={Translations} proxy={Proxy} channel={Channel}. mass is retained as metadata, not an undocumented total-HP multiplier.",
            _map, map, _document.Bosses.Count, _translations.Count, _document.Proxy, HudChannel);
        _dirty = true; _roundReadyAt = Now + 2.5; _nextScan = Now + 0.3; _reportAt = Now + 5;
    }
    public void OnRoundStart()
    {
        if (!_mapActive) return;
        _generation++;
        ResetLiveState(); _dirty = true; _roundReadyAt = Now + 2.5; _nextScan = Now + 0.3; _reportAt = Now + 5;
    }
    private void ResetLiveState()
    {
        _entities.Clear(); _counters.Clear(); _bosses.Clear(); _lastHit.Clear(); _deleted.Clear();
        _translationState.Clear(); _foreignHudUntil.Clear(); _ownHud.Clear();
        _roundReadyAt = 0; _lastScan = -10; _lastBoundCount = -1; _lastSnapshotLog = 0;
    }
    private void OnMapEnd() { _generation++; _mapActive = false; ResetLiveState(); _muted.Clear(); }
    private void OnEntitySpawned(CEntityInstance entity)
    {
        if (_mapActive && _document.Bosses.Count > 0) { _dirty = true; _nextScan = Math.Max(_lastScan + 1, Now + 0.25); }
    }
    private void OnEntityDeleted(CEntityInstance entity)
    {
        uint raw = entity.EntityHandle.Raw;
        if (_entities.ContainsKey(raw)) { _deleted[raw] = Now; _dirty = true; _nextScan = Math.Max(_lastScan + 1, Now + 0.25); }
    }
    private void OnEntityDamage(CBaseEntity entity, CTakeDamageInfo info, CTakeDamageResult result)
    {
        if (!_mapActive || !_plugin.Config.BossHud || entity is not { IsValid: true }) return;
        if (_entities.ContainsKey(entity.EntityHandle.Raw)) _lastHit[entity.EntityHandle.Raw] = Now;
    }
    private void OnDisconnect(int slot)
    {
        // Stored IDs include serial numbers; additionally release all per-slot state immediately.
        foreach (var raw in _muted.Concat(_ownHud).Concat(_foreignHudUntil.Keys).Distinct().ToArray())
            if (new CHandle<CCSPlayerController>(raw).Index == slot + 1)
            { _muted.Remove(raw); _ownHud.Remove(raw); _foreignHudUntil.Remove(raw); }
    }
    private bool Wanted(string name, string identity)
    {
        if (_identities.Contains(identity) || _names.Contains(name)) return true;
        string suffix = FysEntityMatcher.TemplateSuffix(name);
        if (suffix.Length > 0 && _names.Contains(name[..^suffix.Length])) return true;
        return _wildcards.Any(p => FysEntityMatcher.NameMatches(p, name));
    }
    private static CBaseEntity? Resolve(uint raw)
    {
        var entity = new CHandle<CBaseEntity>(raw).Value;
        return entity is { IsValid: true } ? entity : null;
    }
    private EntityMeta? Describe(CEntityInstance entity)
    {
        if (!entity.IsValid) return null;
        var e = entity.As<CBaseEntity>();
        string name = e.Entity?.Name ?? "", identity = e.UniqueHammerID ?? "";
        if (!Wanted(name, identity)) return null;
        return new(e.EntityHandle.Raw, name, identity, e.DesignerName == "math_counter");
    }
    private HookResult OnOutput(string classname, string output, CEntityInstance caller, CVariant value)
    {
        if (_disposed || !_mapActive || !_plugin.Config.BossHud || _document.Bosses.Count == 0 || caller is not { IsValid: true }) return HookResult.Continue;
        try
        {
            uint raw = caller.EntityHandle.Raw;
            if (!_entities.TryGetValue(raw, out var meta))
            {
                meta = Describe(caller);
                if (meta == null) return HookResult.Continue;
                _entities[raw] = meta; _dirty = true;
            }
            if (meta.IsCounter)
            {
                var counter = caller.As<CMathCounter>();
                if (!_counters.TryGetValue(raw, out var sample)) _counters[raw] = sample = new();
                double? numeric = output switch
                {
                    "OnHitMin" => counter.Min, "OnHitMax" => counter.Max,
                    "OutValue" or "OnGetValue" => VariantNumber(value), _ => null
                };
                if (numeric is { } n && double.IsFinite(n))
                {
                    bool changed = sample.HasValue && Math.Abs(sample.Reading.Value - n) > 0.0001;
                    sample.Reading = new(n, counter.Min, counter.Max, counter.Disabled);
                    sample.HasValue = true; sample.Peak = Math.Max(sample.Peak, n);
                    if (Now >= _roundReadyAt && (changed || output == "OutValue")) sample.LastActivity = Now;
                }
                if (output is "OnHitMin" or "OnHitMax")
                    foreach (var b in _bosses.Values)
                    {
                        bool hasBars = b.Bars.HasValue;
                        bool correctEntity = hasBars ? b.Bars == raw : b.Primary == raw;
                        bool ascending = hasBars ? b.Definition.Reverse : b.Definition.Increase;
                        if (correctEntity && output == (ascending ? "OnHitMax" : "OnHitMin")) b.Stage.ReachedLimit();
                    }
            }
            else if (output is "OnTakeDamage" or "OnHealthChanged" or "OnBreak" or "OnKilled")
            {
                _lastHit[raw] = Now;
                if (output is "OnBreak" or "OnKilled")
                {
                    _deleted[raw] = Now;
                    foreach (var b in _bosses.Values.Where(b => b.Primary == raw)) b.Stage.ReachedLimit();
                }
            }
        }
        catch (Exception e) { Warn("boss-output", "Entity output read failed (stale/deleted handle or schema): " + e.Message); }
        return HookResult.Continue;
    }
    private static double? VariantNumber(CVariant value)
    {
        string type = value.FieldType.ToString();
        if (type.Contains("FLOAT", StringComparison.OrdinalIgnoreCase)) return value.Get<float>();
        if (type.Contains("INTEGER", StringComparison.OrdinalIgnoreCase) || type == "FIELD_INT32") return value.Get<int>();
        if (type.Contains("STRING", StringComparison.OrdinalIgnoreCase) && double.TryParse(value.Get<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return n;
        return null;
    }
    private void ScanEntities()
    {
        _lastScan = Now; _dirty = false; _nextScan = Now + 3;
        var found = new Dictionary<uint, EntityMeta>();
        foreach (var entity in Utilities.GetAllEntities())
        {
            try { var m = Describe(entity); if (m != null) found[m.Raw] = m; }
            catch (Exception) { /* not a readable CBaseEntity / being deleted */ }
        }
        _entities.Clear(); foreach (var pair in found) _entities.Add(pair.Key, pair.Value);
        var validKeys = new HashSet<(int, uint)>();
        for (int index = 0; index < _document.Bosses.Count; index++)
        {
            var d = _document.Bosses[index];
            foreach (var meta in found.Values.Where(e => FysEntityMatcher.IsPrimary(d, e.Name, e.Identity)))
            {
                if (d.Kind == FysBossKind.Counter && !meta.IsCounter) continue;
                var key = (index, meta.Raw); validKeys.Add(key);
                if (!_bosses.TryGetValue(key, out var boss)) _bosses[key] = boss = new(d, meta.Raw, meta.IsCounter);
                boss.Backup = FindAux(found.Values, d.Backup, meta.Name, true);
                boss.Bars = FindAux(found.Values, d.Counter, meta.Name, true);
                boss.Hitbox = FindAux(found.Values, d.Hitbox, meta.Name, false);
            }
        }
        foreach (var key in _bosses.Keys.Where(k => !validKeys.Contains(k)).ToArray())
            if (!_deleted.TryGetValue(key.Item2, out double deletedAt) || Now - deletedAt > 3) _bosses.Remove(key);
        foreach (uint raw in _counters.Keys.Where(k => !found.ContainsKey(k)).ToArray()) _counters.Remove(raw);
        foreach (uint raw in _lastHit.Keys.Where(k => !found.ContainsKey(k)).ToArray()) _lastHit.Remove(raw);
        foreach (uint raw in _deleted.Keys.Where(k => Now - _deleted[k] > 4).ToArray()) _deleted.Remove(raw);
        if (_lastBoundCount != _bosses.Count)
        {
            _lastBoundCount = _bosses.Count;
            _plugin.Logger.LogInformation("FYS_BOSS_BIND map={Map} definitions={Rules} live_instances={Bound} named_entities={Entities}", _map, _document.Bosses.Count, _bosses.Count, found.Count);
        }
    }
    private static uint? FindAux(IEnumerable<EntityMeta> entities, string name, string primaryName, bool requireCounter)
    {
        if (name.Length == 0) return null;
        var matches = entities.Where(e => (!requireCounter || e.IsCounter) && FysEntityMatcher.NameMatches(name, e.Name)).ToArray();
        var suffix = FysEntityMatcher.TemplateSuffix(primaryName);
        var sameInstance = matches.Where(e => FysEntityMatcher.TemplateSuffix(e.Name) == suffix).ToArray();
        if (sameInstance.Length == 1) return sameInstance[0].Raw;
        // A deliberately global, unsuffixed backup can be shared; never bind a different template clone arbitrarily.
        var exact = matches.Where(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return exact.Length == 1 ? exact[0].Raw : null;
    }
    private void Tick()
    {
        if (_disposed || !_mapActive) return;
        try
        {
            if (_plugin.Config.BossHud && _document.Bosses.Count > 0 &&
                ((_dirty && Now >= _nextScan) || Now - _lastScan >= 3)) ScanEntities();
            if (_reportAt > 0 && Now >= _reportAt)
            {
                _reportAt = 0;
                var missing = _document.Bosses.Where(d => !_bosses.Values.Any(b => b.Definition == d)).Select(d => $"{d.Display}({(d.Identity.Length > 0 ? "id=" + d.Identity : d.PrimaryName)})").Take(12).ToArray();
                if (missing.Length > 0) _plugin.Logger.LogInformation("FYS_BOSS_WAIT map={Map}: currently unbound (later-stage/template entities may spawn later): {Names}", _map, string.Join(", ", missing));
            }
            var lines = new List<string>();
            if (_plugin.Config.BossHud)
                foreach (var b in _bosses.Values.OrderByDescending(b => b.ActiveUntil).ToArray())
                {
                    var line = ReadBossLine(b);
                    if (line != null && lines.Count < 3) lines.Add(line);
                }
            if (_plugin.Config.MapTranslations) lines.AddRange(_translationState.Visible(Now, _plugin.Config.TranslationCountdown).Take(3));
            Render(lines);
        }
        catch (Exception e) { Warn("boss-timer", "Boss/translation tick: " + e.Message); }
    }
    private string? ReadBossLine(BoundBoss boss)
    {
        var primary = Resolve(boss.Primary);
        if (primary == null) return null;
        var d = boss.Definition;
        foreach (uint raw in new[] { boss.Primary, boss.Hitbox ?? boss.Primary })
            if (_lastHit.TryGetValue(raw, out double hit)) boss.ActiveUntil = Math.Max(boss.ActiveUntil, hit + 8);
        BossGauge gauge;
        string extra = "";
        if (boss.IsCounter)
        {
            if (!_counters.TryGetValue(boss.Primary, out var sample) || !sample.HasValue)
                return boss.ActiveUntil >= Now ? $"{d.Display} · HP等待计数器输出" : null;
            var c = primary.As<CMathCounter>();
            sample.Reading = sample.Reading with { Minimum = c.Min, Maximum = c.Max, Disabled = c.Disabled };
            if (sample.Reading.Disabled) return null;
            boss.ActiveUntil = Math.Max(boss.ActiveUntil, sample.LastActivity + 8);
            double? backup = boss.Backup is { } rawBackup && _counters.TryGetValue(rawBackup, out var backupSample) && backupSample.HasValue ? backupSample.Reading.Value : null;
            gauge = BossGauge.FromCounter(sample.Reading, d.Increase, sample.Peak, backup);
            if (boss.Bars is { } rawBars && _counters.TryGetValue(rawBars, out var bars) && bars.HasValue)
            {
                var cBars = Resolve(rawBars)?.As<CMathCounter>();
                if (cBars != null) bars.Reading = bars.Reading with { Minimum = cBars.Min, Maximum = cBars.Max };
                var barGauge = BossGauge.FromCounter(bars.Reading, d.Reverse, bars.Peak);
                extra = $" · 段计数 {barGauge.Numbers}{(d.Reverse ? " ↑" : " ↓")}";
                boss.Stage.ObserveAlive(barGauge.Current > 0 && gauge.Current > 0, d.Stages);
            }
            else if (d.Counter.Length > 0) extra = " · 段计数待采集";
            else boss.Stage.ObserveAlive(gauge.Current > 0, d.Stages);
            // mass's undocumented backup algorithm is NOT a known full-HP formula. Show segment values only.
            if (d.Mass is not null && d.Counter.Length > 0) extra += " · 分段HP";
        }
        else
        {
            int hp = primary.Health;
            if (boss.LastHealth is { } previous && hp < previous) boss.ActiveUntil = Now + 8;
            boss.LastHealth = hp; boss.ObservedMaximum = Math.Max(boss.ObservedMaximum, Math.Max(hp, primary.MaxHealth));
            gauge = new(Math.Max(0, hp), boss.ObservedMaximum > 0 ? boss.ObservedMaximum : null, false);
            boss.Stage.ObserveAlive(hp > 0, d.Stages);
        }
        if (Now > boss.ActiveUntil) return null;
        if (d.Stages > 1) extra += $" · 阶段 {boss.Stage.Current}/{d.Stages}";
        string line = $"{d.Display} {gauge.Bar(d.Count)} {gauge.Numbers}{extra}";
        if (Now - _lastSnapshotLog > 5)
        {
            _lastSnapshotLog = Now;
            _plugin.Logger.LogDebug("FYS_BOSS_LIVE map={Map} primary={Handle} line={Line}", _map, boss.Primary, FysHudText.Plain(line));
        }
        return line;
    }
    private HookResult OnServerSay(CCSPlayerController? player, CommandInfo command)
    {
        // Never translate player chat, even if a player types a known map phrase.
        if (player != null || _sendingChat || !_mapActive || !_plugin.Config.MapTranslations) return HookResult.Continue;
        string original = command.ArgString;
        if (!_translations.TryResolve(original, out var rule)) return HookResult.Continue;
        ApplyTranslation(original, rule, null);
        return HookResult.Handled;
    }
    private HookResult OnUserMessage(UserMessage message, string kind)
    {
        if (_disposed || !_mapActive || _sendingChat || _sendingHud) return HookResult.Continue;
        try
        {
            if (kind == "TextMsg")
            {
                int destination = message.ReadInt("dest");
                if (destination is 4 or 6)
                {
                    foreach (var p in message.Recipients.Where(p => p is { IsValid: true })) _foreignHudUntil[p.EntityHandle.Raw] = Now + 2;
                    return HookResult.Continue;
                }
                if (destination != 3 || !_plugin.Config.MapTranslations) return HookResult.Continue;
                int count = message.GetRepeatedFieldCount("param");
                for (int i = 0; i < Math.Min(count, 5); i++)
                {
                    string original = message.ReadString("param", i);
                    if (!_translations.TryResolve(original, out var rule)) continue;
                    ApplyTranslation(original, rule, RecipientHandles(message));
                    return HookResult.Stop;
                }
            }
            else if (_plugin.Config.MapTranslations)
            {
                // SayText2 player entity indices are nonzero. Restrict this fallback to server announcements.
                if (message.ReadInt("entityindex") != 0) return HookResult.Continue;
                foreach (string field in new[] { "param2", "param1", "messagename" })
                {
                    if (!message.HasField(field)) continue;
                    string original = message.ReadString(field);
                    if (!_translations.TryResolve(original, out var rule)) continue;
                    ApplyTranslation(original, rule, RecipientHandles(message));
                    return HookResult.Stop;
                }
            }
        }
        catch (Exception e) { Warn("translation-message", "Translation usermessage read failed; original output retained: " + e.Message); }
        return HookResult.Continue;
    }
    private static uint[] RecipientHandles(UserMessage message) => message.Recipients.Where(p => p is { IsValid: true, IsBot: false }).Select(p => p.EntityHandle.Raw).ToArray();
    private void ApplyTranslation(string source, FysTranslationRule rule, uint[]? recipients)
    {
        _translationState.Apply(rule, FysTranslationCatalog.Normalize(source), Now, _plugin.Config.TranslationCountdown);
        if (rule.Blocked) return;
        string translated = string.IsNullOrEmpty(rule.Translation) ? FysTranslationCatalog.Normalize(source) : rule.Translation;
        int generation = _generation;
        // Avoid native reentrancy and message invalidation while inside a command/usermessage pre-hook.
        Server.NextFrame(() =>
        {
            if (_disposed || !_mapActive || generation != _generation) return;
            try
            {
                _sendingChat = true;
                var targets = recipients == null ? Utilities.GetPlayers() : recipients.Select(raw => new CHandle<CCSPlayerController>(raw).Value).Where(p => p != null).Cast<CCSPlayerController>().ToList();
                foreach (var p in targets)
                    if (ZePracticePlugin.IsRealPlayer(p)) p.PrintToChat(FysHudText.Chat(FysHudText.Limit(translated, 700)));
            }
            finally { _sendingChat = false; }
        });
    }
    private void Render(IReadOnlyList<string> lines)
    {
        if (HudChannel == "off") return;
        string plain = string.Join("\n", lines.Select(x => FysHudText.Limit(FysHudText.Plain(x), 140)));
        string html = HudChannel == "html" ? string.Join("<br>", lines.Select(x => FysHudText.Html(FysHudText.Limit(x, 140)))) : "";
        try
        {
            _sendingHud = true;
            foreach (var player in Utilities.GetPlayers())
            {
                if (!ZePracticePlugin.IsRealPlayer(player)) continue;
                uint raw = player.EntityHandle.Raw;
                if (_muted.Contains(raw) || MenuManager.GetActiveMenu(player) != null ||
                    (_foreignHudUntil.TryGetValue(raw, out double busy) && Now < busy)) continue;
                if (lines.Count == 0)
                {
                    if (_ownHud.Remove(raw))
                    {
                        if (HudChannel == "html") player.PrintToCenterHtml(" ", 1); else player.PrintToCenter(" ");
                    }
                    continue;
                }
                if (HudChannel == "html") player.PrintToCenterHtml(html, 1); else player.PrintToCenter(plain);
                _ownHud.Add(raw);
            }
        }
        finally { _sendingHud = false; }
    }
    private void OnHudCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!ZePracticePlugin.IsRealPlayer(player)) { _plugin.Reply(player, "玩家使用 !bosshud on/off；服务器选择通道用 css_bosshudmode。"); return; }
        uint raw = player!.EntityHandle.Raw;
        string arg = command.GetArg(1).ToLowerInvariant();
        bool turnOff = arg == "off" || (arg != "on" && !_muted.Contains(raw));
        if (turnOff) _muted.Add(raw); else _muted.Remove(raw);
        _plugin.Reply(player, turnOff ? "Boss/地图字幕HUD已关闭；聊天翻译仍开启。" : "Boss/地图字幕HUD已开启。");
    }
    private void OnModeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_plugin.Permissions.Allows(player,"css_bosshudmode")) { _plugin.Reply(player, "只有管理员能修改HUD通道。"); return; }
        string mode = command.GetArg(1).ToLowerInvariant();
        if (mode is not ("center" or "html" or "off")) { _plugin.Reply(player, "css_bosshudmode center|html|off；默认center，HTML需关闭其他插件相同HUD。"); return; }
        HudChannel = mode; _ownHud.Clear();
        _plugin.Reply(player, "HUD通道=" + mode + "。center避免使用生存模式HTML通道；html需自行停用竞争HUD。");
    }
    private void OnStatusCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_plugin.Permissions.Allows(player,"css_bossstatus")) { _plugin.Reply(player, "只有管理员可以查看实体绑定诊断。"); return; }
        _plugin.Reply(player, $"FYS={_map}；Boss规则={_document.Bosses.Count}，已绑定实例={_bosses.Count}，翻译={_translations.Count}，倒计时={_translationState.TimerCount}，HUD={HudChannel}");
        foreach (var d in _document.Bosses.Take(20))
        {
            var bindings = _bosses.Values.Where(b => b.Definition == d).ToArray();
            _plugin.Reply(player, $"{d.Display}: {(bindings.Length == 0 ? "等待实体/名称或identity不匹配" : string.Join(",", bindings.Select(b => $"handle={b.Primary},value={(_counters.TryGetValue(b.Primary, out var v) && v.HasValue ? v.Reading.Value.ToString(CultureInfo.InvariantCulture) : b.IsCounter ? "waiting-output" : "health-schema")}")))}");
        }
        if (_document.Proxy) _plugin.Reply(player, "Proxy=true私有协议未公开；目前仅运行明确配置的实体，不伪造Proxy状态。");
    }
    private void Warn(string key, string detail)
    {
        double monotonic = Environment.TickCount64 / 1000d; // No engine call: warning is safe before map start.
        if (_warningTimes.TryGetValue(key, out double previous) && monotonic - previous < 30) return;
        _warningTimes[key] = monotonic; _plugin.Logger.LogWarning("{Detail}", detail);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _generation++; _timer?.Kill(); _timer = null;
        foreach (var h in _outputHooks) _plugin.UnhookEntityOutput(h.Class, h.Output, h.Handler, HookMode.Pre);
        foreach (var h in _messageHooks) _plugin.UnhookUserMessage(h.Id, h.Handler, HookMode.Pre);
        if (_initialized)
        {
            _plugin.RemoveCommandListener("say", OnServerSay, HookMode.Pre);
            _plugin.RemoveListener<Listeners.OnEntitySpawned>(OnEntitySpawned);
            _plugin.RemoveListener<Listeners.OnEntityDeleted>(OnEntityDeleted);
            _plugin.RemoveListener<Listeners.OnEntityTakeDamagePost>(OnEntityDamage);
            _plugin.RemoveListener<Listeners.OnClientDisconnect>(OnDisconnect);
            _plugin.RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
            _plugin.RemoveZeCommand("css_bosshud", OnHudCommand); _plugin.RemoveZeCommand("css_bossstatus", OnStatusCommand); _plugin.RemoveZeCommand("css_bosshudmode", OnModeCommand);
        }
        _outputHooks.Clear(); _messageHooks.Clear(); ResetLiveState(); _muted.Clear();
    }
    private sealed record EntityMeta(uint Raw, string Name, string Identity, bool IsCounter);
    private sealed class CounterObservation
    {
        public bool HasValue; public FysCounterReading Reading; public double Peak, LastActivity = -100;
    }
    private sealed class BoundBoss(FysBossDefinition definition, uint primary, bool counter)
    {
        public FysBossDefinition Definition { get; } = definition;
        public uint Primary { get; } = primary;
        public bool IsCounter { get; } = counter;
        public uint? Backup, Bars, Hitbox;
        public double ActiveUntil = -100, ObservedMaximum;
        public int? LastHealth;
        public BossStageTracker Stage { get; } = new();
    }
}