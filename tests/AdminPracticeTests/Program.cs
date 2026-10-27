using ZePractice;
using System.Text.Json;

if (args.Length >= 2 && args[0] == "--catalog")
{
    Console.WriteLine(JsonSerializer.Serialize(AdminPracticeMapCatalog.Load(args[1], args.Length > 2 ? args[2] : "",
        Array.Empty<string>(), args.Length > 3 ? args[3] : null), new JsonSerializerOptions { WriteIndented = true }));
    return;
}
int passed = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); passed++; }
string scratchRoot = args.Length > 0 ? Path.GetFullPath(args[0]) : throw new ArgumentException("Pass a test temporary directory");
string root = Path.Combine(scratchRoot, "admin-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Console.WriteLine("Test fixtures: " + root);
string csgo = Path.Combine(root, "game", "csgo");
string maps = Path.Combine(csgo, "maps");
string addon = Path.Combine(root, "game", "csgo_addons", "test_addon", "maps");
Directory.CreateDirectory(maps); Directory.CreateDirectory(addon);
string workshop = Path.Combine(maps, "workshop", "1234567890"); Directory.CreateDirectory(workshop);
foreach (var file in new[] { "de_nuke.vpk", "ze_local.vmap_c", "ze_local_skybox.vmap_c", "pak01_dir.vpk", "pak01_000.vpk" }) File.WriteAllText(Path.Combine(maps, file), "fixture");
File.WriteAllText(Path.Combine(addon, "ze_addon.vpk"), "fixture"); File.WriteAllText(Path.Combine(workshop, "ze_workshop.vpk"), "fixture");
string metadata = Path.Combine(root, "maps.json");
File.WriteAllText(metadata, """
{
 // FYS maps are metadata only. This remote key must not appear in the local catalog.
 "ze_remote_only": { "nomination": true, "WorkshopID": "9876543210" },
 "ze_local": { "displayName": "中文本地图", "nomination": true, "WorkshopID": "999" },
 "ze_workshop": { "name": "Workshop 中文", "cooldown": 60 },
 "evil;quit": { "nomination": true }
}
""");
var catalog = AdminPracticeMapCatalog.Load(csgo, metadata, new[] { "test_addon", "../escape" });
Check(catalog.Count == 4, "only loadable local/addon/workshop map files are listed");
Check(catalog.All(x => x.Name != "ze_remote_only"), "FYS remote-only metadata does not pretend a map is installed");
Check(catalog.All(x => !x.Name.Contains("pak01") && !x.Name.EndsWith("skybox")), "exclude package chunks and skyboxes");
Check(catalog.Single(x => x.Name == "ze_local").DisplayName == "中文本地图", "map display metadata survives JSON comments");
Check(catalog.Single(x => x.Name == "ze_local").WorkshopId == null, "metadata ID alone never changes local loading to Workshop download");
Check(catalog.Single(x => x.Name == "ze_workshop").WorkshopId == "1234567890", "Workshop ID comes from local workshop directory");
Check(catalog.Single(x => x.Name == "ze_addon").LocalFile.StartsWith(addon), "active addon maps available");
Check(!AdminPracticeMapCatalog.Load(csgo, metadata, Array.Empty<string>()).Any(x => x.Name == "ze_addon"), "inactive addon maps are not claimed available");
Check(AdminPracticeMapCatalog.IsValidMapName("ze_test_v1-2"), "normal map name accepted");
foreach (var bad in new[] { "de_nuke;quit", "../de_nuke", "de_nuke\nquit", "de_nuke\"", "-addon evil", "x//y", "C:\\maps\\x" })
    Check(!AdminPracticeMapCatalog.IsValidMapName(bad), "reject command/path injection: " + JsonSerializer.Serialize(bad));
Check(AdminPracticeMapCatalog.IsWorkshopId("1234567890") && !AdminPracticeMapCatalog.IsWorkshopId("0") && !AdminPracticeMapCatalog.IsWorkshopId("12;quit"), "Workshop ID validates numeric nonzero only");
// Explicit server maplist overrides local Workshop-directory discovery, but cannot invent installed maps.
string configuredLocal = Path.Combine(maps, "ze_forhidden_slicoo_cn.vpk");
File.WriteAllText(configuredLocal, "local placeholder VPK fixture");
string maplist = Path.Combine(root, "maplist.jsonc");
File.WriteAllText(maplist, """
{
 // This ID is configured by the local administrator, not inferred from FYS aliases.
 "Groups": {},
 "Maps": {
  "ze_forhidden_slicoo_cn": { "workshop_id": 3817448617 },
  "ze_workshop": { "workshop_id": "3817448618" },
  "ze_addon": { "workshop_id": "0003817448619" },
  "ze_missing_installed_file": { "workshop_id": "3817448620" },
 }
}
""");
var configuredCatalog = AdminPracticeMapCatalog.Load(csgo, metadata, new[] { "test_addon" }, maplist);
Check(configuredCatalog.Single(x => x.Name == "ze_forhidden_slicoo_cn").WorkshopId == "3817448617", "explicit numeric CS2Fixes maplist ID enables Workshop hosting for installed local placeholder");
Check(configuredCatalog.Single(x => x.Name == "ze_forhidden_slicoo_cn").LocalFile == configuredLocal, "Workshop mapping preserves the observed installed local file");
Check(configuredCatalog.Single(x => x.Name == "ze_workshop").WorkshopId == "3817448618", "explicit string maplist ID takes precedence over physical workshop directory ID");
Check(configuredCatalog.Single(x => x.Name == "ze_addon").WorkshopId == "3817448619", "active addon map accepts an explicit ID and normalizes leading zeroes");
Check(configuredCatalog.All(x => x.Name != "ze_missing_installed_file"), "maplist ID alone cannot list a map with no installed local file");
Check(configuredCatalog.Single(x => x.Name == "ze_local").WorkshopId == null, "FYS metadata ID is still ignored when another map has explicit local mapping");
Check(!AdminPracticeMapCatalog.Load(csgo, metadata, Array.Empty<string>(), maplist).Any(x => x.Name == "ze_addon"), "explicit maplist ID cannot activate an unmounted addon");
Check(AdminPracticeMapCatalog.Load(csgo, metadata, Array.Empty<string>()).Single(x => x.Name == "ze_forhidden_slicoo_cn").WorkshopId == null, "optional maplist argument preserves existing local-only behavior");

foreach (string validIdJson in new[] { "18446744073709551615", "\"18446744073709551615\"", "1", "\"1\"" })
{
    File.WriteAllText(maplist, "{\"Maps\":{\"ZE_FORHIDDEN_SLICOO_CN\":{\"workshop_id\":" + validIdJson + "}}}");
    var expected = validIdJson.Trim('"');
    Check(AdminPracticeMapCatalog.Load(csgo, metadata, Array.Empty<string>(), maplist).Single(x => x.Name == "ze_forhidden_slicoo_cn").WorkshopId == expected, "positive uint64 boundary and case-insensitive map key: " + validIdJson);
}
foreach (string invalidIdJson in new[] { "0", "\"0\"", "-1", "1.0", "1e3", "18446744073709551616", "\"18446744073709551616\"", "null", "true", "{}", "[]", "\"\"", "\"+1\"", "\" 1\"", "\"1 \"", "\"1\\n\"", "\"1\\u0000\"", "\"１２\"", "\"12;quit\"" })
{
    File.WriteAllText(maplist, "{\"Maps\":{\"ze_forhidden_slicoo_cn\":{\"workshop_id\":" + invalidIdJson + "},\"ze_workshop\":{\"workshop_id\":" + invalidIdJson + "}}}");
    var invalidCatalog = AdminPracticeMapCatalog.Load(csgo, metadata, Array.Empty<string>(), maplist);
    Check(invalidCatalog.Single(x => x.Name == "ze_forhidden_slicoo_cn").WorkshopId == null, "ignore invalid explicit Workshop ID: " + invalidIdJson);
    Check(invalidCatalog.Single(x => x.Name == "ze_workshop").WorkshopId == "1234567890", "invalid explicit ID preserves verified local workshop-directory fallback: " + invalidIdJson);
}
foreach (var badShape in new[] { "[]", "null", "{\"Maps\":[]}", "{\"Maps\":null}", "{\"Maps\":{\"ze_forhidden_slicoo_cn\":3}}", "{\"Maps\":{\"ze_forhidden_slicoo_cn\":{\"WorkshopID\":\"999\"}}}", "{broken JSON" })
{
    File.WriteAllText(maplist, badShape);
    var invalidCatalog = AdminPracticeMapCatalog.Load(csgo, metadata, Array.Empty<string>(), maplist);
    Check(invalidCatalog.Single(x => x.Name == "ze_forhidden_slicoo_cn").WorkshopId == null, "bad optional maplist cannot hide installed maps or infer unsupported fields: " + badShape);
}
Check(AdminPracticeMapCatalog.Load(csgo, metadata, Array.Empty<string>(), Path.Combine(root, "missing-maplist.jsonc")).Single(x => x.Name == "ze_forhidden_slicoo_cn").WorkshopId == null, "missing optional maplist retains normal local map listing");
Check(AdminPracticePunishmentStore.IsSteamId("76561198000000000") && !AdminPracticePunishmentStore.IsSteamId("BOT") && !AdminPracticePunishmentStore.IsSteamId("0"), "punishment identities must be SteamID64");
var path = Path.Combine(root, "state", "punishments.json");
var store = new AdminPracticePunishmentStore(path); store.Load();
var now = DateTimeOffset.UtcNow;
var record = new PracticePunishment("76561198000000000", "ban", "unit test", "CONSOLE", now, now.AddMinutes(30));
store.Put(record);
Check(store.Find(record.SteamId, "ban", now) != null, "active timed ban lookup");
Check(store.Find(record.SteamId, "ban", now.AddMinutes(31)) == null, "timed ban expires");
store.Put(record with { Reason = "replaced", ExpiresUtc = null });
Check(store.Active(now).Count == 1 && store.Find(record.SteamId, "ban", now.AddYears(10))?.Reason == "replaced", "replacing ban keeps one permanent record");
store.Put(record with { Kind = "mute" });
Check(store.Active(now).Count == 2, "ban and mute are independent");
var restored = new AdminPracticePunishmentStore(path); restored.Load();
Check(restored.Active(now).Count == 2, "punishments survive restart by JSON reload");
Check(restored.Remove(record.SteamId, "ban") && restored.Find(record.SteamId, "mute", now) != null, "unban does not delete mute");
Check(!restored.Remove(record.SteamId, "gag"), "absent removal is a no-op");
try { restored.Put(record with { SteamId = "bad" }); Check(false, "bad identity rejected"); } catch (ArgumentException) { Check(true, "bad identity rejected"); }
Check(!File.Exists(path + ".tmp"), "atomic save commits without temporary residue");
InstalledWorkshopTests.Run(root, Check);
Console.WriteLine($"PASS {passed} checks. No game process started and no server files changed.");
