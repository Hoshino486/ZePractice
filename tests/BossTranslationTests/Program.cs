using ZePractice;
using System.Text.Json;

int checks = 0;
void Check(bool condition, string title) { checks++; if (!condition) throw new Exception("FAIL: " + title); }
void Equal<T>(T expected, T actual, string title) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{title} expected={expected} actual={actual}");

var doc = FysBossReader.Parse("""
// upstream JSONC, comments and trailing commas must work
{
  "Proxy": true,
  "Counters": [{"iterator":"hp","backup":"back","counter":"bars","hitbox":"hit","display":"BOSS <x>","stages":2,"mass":174,"increase":true,"reverse":true}],
  "Breakables": [{"target":"box","display":"BREAK","count":8}],
  "Monsters": [{"identity":"4134","display":"NPC"}],
}
""");
Equal(3, doc.Bosses.Count, "all boss sections parsed");
Check(doc.Proxy && doc.Diagnostics.Any(d => d.Contains("Proxy")), "Proxy is explicit unsupported protocol, not silent");
var boss = doc.Bosses[0];
Check(boss.Increase && boss.Reverse && boss.Stages == 2 && boss.Mass == 174 && boss.Backup == "back", "full counter metadata retained");
Equal(8, doc.Bosses[1].Count, "breakable bar divisions");
Equal("4134", doc.Bosses[2].Identity, "Hammer ID remains string not entity index");
Check(FysEntityMatcher.NameMatches("boss_hp", "boss_hp&0027"), "template names match base rule");
Check(!FysEntityMatcher.NameMatches("boss_hp", "boss_hp&evil"), "non-template ampersand not stripped");
Check(FysEntityMatcher.NameMatches("boss_*", "BOSS_big"), "explicit wildcard names");
Check(!FysEntityMatcher.NameMatches("boss", "boss2"), "not accidental prefix matching");
Check(FysEntityMatcher.IdentityMatches("4134", "4134") && !FysEntityMatcher.IdentityMatches("4134", "14134"), "identity exact");
Check(!FysEntityMatcher.IsPrimary(boss with { Identity = "99" }, "hp", "other"), "identity disambiguates same-named entities");
var down = BossGauge.FromCounter(new(70, 0, 100), false, 100);
Equal(70d, down.Current, "decreasing health"); Equal(100d, down.Maximum, "declared maximum");
Equal("[■■■■■■■□□□]", down.Bar(10), "real health bar");
var up = BossGauge.FromCounter(new(30, 0, 100), true, 30);
Equal(70d, up.Current, "increase uses OnHitMax remaining HP");
Equal(100d, up.Maximum, "increase upper bound");
var noBound = BossGauge.FromCounter(new(30, 0, 0), true, 30);
Check(noBound.AscendingWithoutLimit && noBound.Maximum == null, "unknown increasing limit isn't fabricated HP");
Equal(900d, BossGauge.FromCounter(new(400, 0, 0), false, 500, 900).Maximum, "backup captured value supplies max segment");
Equal(40d, BossGauge.FromCounter(new(50, 10, 110), false, 110).Current, "min bound respected");
Check(BossGauge.FromCounter(new(-5, 0, 0), false, 0).Current == 0, "HP nonnegative");
var stage = new BossStageTracker(); stage.ReachedLimit(); stage.ReachedLimit(); stage.ObserveAlive(false, 3);
Equal(1, stage.Current, "limit by itself doesn't advance stage"); stage.ObserveAlive(true, 3); stage.ObserveAlive(true, 3);
Equal(2, stage.Current, "observed refill advances once"); stage.ReachedLimit(); stage.ObserveAlive(true, 2);
Equal(2, stage.Current, "stage clamps configured total"); stage.Reset(); Equal(1, stage.Current, "round clears stages");

var translations = FysTranslationCatalog.Parse("""
{
  "Door opens in 30 seconds": {"translation":"{green}大门30秒后打开","countdown":30},
  "spam": {"translation":"x","blocked":true},
  "clear": {"translation":"重置","clearText":true,"clearTimer":true,"countdown":5},
  " A ": {"translation":"one"}, "A": {"translation":"two"},
  "unsafe": {"translation":"<img src=x onerror=alert(1)>&\"中文{red}红色"}
}
""");
Check(translations.TryResolve("Door opens in 30 seconds", out var rule), "exact source translation");
Check(translations.TryResolve("\u0004\"Door opens in 30 seconds\"", out _), "color and one quoted wrapper normalized");
Check(!translations.TryResolve("  A  ", out _), "ambiguous normalized key refused");
Check(translations.TryResolve(" A ", out var exact) && exact.Translation == "one", "exact still beats ambiguity");
Check(!translations.TryResolve("a", out _), "source keys remain case-sensitive");
Check(translations.TryResolve("spam", out var blocked) && blocked.Blocked, "blocked translation");
var state = new FysTranslationState();
state.Apply(rule, "source", 100, true);
Equal(1, state.TimerCount, "countdown actually starts");
Check(state.Visible(100, true).Any(s => s.Contains("30s")), "countdown uses actual deadline");
Check(state.Visible(109.1, true).Any(s => s.Contains("21s")), "countdown ceil avoids early zero");
state.Apply(rule, "source", 110, true); Equal(2, state.TimerCount, "countdowns independent, not overwritten");
translations.TryResolve("clear", out var clear); state.Apply(clear, "clear", 112, true);
Equal(1, state.TimerCount, "clearTimer then new timer");
Check(!state.Visible(112, true).Any(s => s.Contains("大门")), "clearText removes old subtitles");
state.Visible(118, true); Equal(0, state.TimerCount, "countdown expiry pruned");
state.Apply(blocked, "spam", 120, true); Check(!state.Visible(120, true).Any(s => s == "x"), "blocked text isn't HUD output");
state.Apply(blocked with {Countdown=10}, "spam", 120, true); Equal(1, state.TimerCount, "blocked sentence retains explicit independent countdown");
state.Clear(); Equal(0, state.TimerCount, "round/map clear timers");
for (int i=0;i<100;i++) state.Apply(rule, "source", i, true); Equal(16, state.TimerCount, "bounded memory for map spam");
state.Clear(); state.Apply(rule, "source", 0, false); Equal(0, state.TimerCount, "config switch disables creation");
var html = FysHudText.Html("{red}<img src=x>&\"{green}中文");
Check(html.Contains("&lt;img src=x&gt;") && html.Contains("&amp;") && !html.Contains("<img"), "HTML escaped, no markup injection");
Check(html.Contains("#FF5555") && html.Contains("#55FF55") && html.Contains("中文"), "color tokens rendered without losing CJK");
Check(!FysHudText.Plain("\x01{red}hi").Contains("{red}"), "plain channel strips color markup");
Check(FysHudText.Chat("{green}中文").Contains('\x04'), "chat color encoded");
Check(!FysHudText.Chat("one\0two").Contains('\0'), "NUL stripped");
Equal("123…", FysHudText.Limit("123456", 4), "HUD truncation");
Check(!FysHudText.Limit("123😀X", 5).Contains('\uD83D'), "no dangling high surrogate at truncation");
var invalid = FysBossReader.Parse("{\"Counters\":[{},123,{\"iterator\":\"ok\",\"modes\":3}]}");
Equal(1, invalid.Bosses.Count, "bad entries don't discard valid peers");
Check(invalid.Diagnostics.Any(x => x.Contains("modes")), "unknown modes aren't silently claimed supported");

string root = args.FirstOrDefault() ?? @"E:\mapper\chana\临时文件夹\ze-practice-build-20261011\upstream\cs2-master\2001\sharp\configs";
if (Directory.Exists(root))
{
    int bossFiles=0, defs=0, proxyFiles=0, translationFiles=0, phrases=0;
    foreach (var file in Directory.EnumerateFiles(Path.Combine(root,"bosses"),"*.jsonc"))
    {
        var parsed=FysBossReader.Parse(File.ReadAllText(file)); bossFiles++;defs+=parsed.Bosses.Count;if(parsed.Proxy)proxyFiles++;
        Check(parsed.Bosses.All(x=>x.PrimaryName.Length>0||x.Identity.Length>0),"upstream boss selectors: "+Path.GetFileName(file));
    }
    foreach(var file in Directory.EnumerateFiles(Path.Combine(root,"translations"),"*.jsonc"))
    { var parsed=FysTranslationCatalog.Parse(File.ReadAllText(file)); translationFiles++;phrases+=parsed.Count;Check(parsed.Count>=0,"parse upstream translation "+Path.GetFileName(file)); }
    var current=FysBossReader.Parse(File.ReadAllText(Path.Combine(root,"bosses","ze_forhidden_slicoo_cs2.jsonc")));
    Equal(4,current.Bosses.Count,"Slicoo actual FYS selectors: one counter, three NPC IDs");
    Check(current.Bosses[0].Mass==174&&current.Bosses[0].Reverse&&current.Bosses[0].Iterator=="drg7_hp_1","Slicoo real counter metadata");
    Console.WriteLine($"UPSTREAM PASS boss_files={bossFiles} definitions={defs} proxy_files={proxyFiles} translation_files={translationFiles} phrases={phrases}");
}
Console.WriteLine($"PASS {checks} checks. Pure logic/parser tests only; live CSS entity ABI and in-game HUD not exercised.");