using System.Numerics;
using ZePractice;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL " + name);
    passed++; Console.WriteLine("PASS " + name);
}

var movementRules = PublicGameplayLogic.ServerMovementRules;
foreach (string name in new[] { "mp_autokick", "mp_suicide_penalty" })
    Check(PublicGameplayLogic.ServerPlayerProtectionRules[name] == "0", "repeated practice deaths never trigger punishment: " + name);
Check(movementRules["sv_airaccelerate"] == "12", "default air acceleration");
Check(movementRules["sv_air_max_wishspeed"] == "30", "default air wish speed");
Check(movementRules["sv_maxvelocity"] == "3500", "default engine velocity limit");
foreach (string name in new[] { "sv_staminamax", "sv_staminajumpcost", "sv_staminalandcost", "sv_jump_spam_penalty_time" })
    Check(movementRules[name] == "0", "server-wide no jump fatigue: " + name);
Check(movementRules["sv_autobunnyhopping"] == "0" && movementRules["sv_enablebunnyhopping"] == "1", "personal bhop toggle remains independent");
Check(PublicGameplayLogic.HumanSpawnArmor(2500, 0, true, 2500) == 0, "zombie to human clears armor without free armor");
Check(PublicGameplayLogic.HumanSpawnArmor(2500, 2, true, 2500) == 100, "zombie to human uses ordinary free armor");
Check(PublicGameplayLogic.HumanSpawnArmor(75, 0, false, 2500) == 75, "ordinary human armor preserved");
Check(PublicGameplayLogic.HumanSpawnArmor(2500, 2, true, 50) == 50, "human spawn honors lower server cap");
Check(PublicGameplayLogic.HumanSpawnArmor(2500, 2, false, 2500) == 100, "human spawn armor never inherits zombie capacity");
foreach (string value in new[] { "1", "35000", "100" })
    Check(PublicGameplayLogic.TryInteger(value, 1, 35000, out _), "valid health " + value);
foreach (string value in new[] { "0", "35001", "-1", "+2", "1.5", "NaN", "2147483648", "9999999999999999999999", "100;quit", " 100", "1e3" })
    Check(!PublicGameplayLogic.TryInteger(value, 1, 35000, out _), "reject health " + value);
foreach (string value in new[] { "0", "0.5", "1", "0.01" })
    Check(PublicGameplayLogic.TryVolume(value, out _), "valid volume " + value);
foreach (string value in new[] { "NaN", "Infinity", "-0.1", "1.01", "0,5", "1;quit", "1e1" })
    Check(!PublicGameplayLogic.TryVolume(value, out _), "reject volume " + value);
Check(PublicGameplayLogic.TryToggle("on", false, out bool enabled) && enabled, "enable toggle");
Check(PublicGameplayLogic.TryToggle("0", true, out enabled) && !enabled, "disable toggle");
Check(PublicGameplayLogic.TryToggle("", false, out enabled) && enabled, "toggle without argument");
Check(!PublicGameplayLogic.TryToggle("yes;quit", false, out _), "reject toggle injection");
foreach (string value in new[] { "3817448617", "+3817448617", "18446744073709551615" })
    Check(PublicGameplayLogic.TryWorkshopId(value, out _), "valid workshop " + value);
foreach (string value in new[] { "0", "-1", "01", "18446744073709551616", "1;quit", "1\nquit", "1 2", "--1" })
    Check(!PublicGameplayLogic.TryWorkshopId(value, out _), "reject workshop " + value);
Check(PublicGameplayLogic.TryWorkshopChat("host_workshop_map +3817448617", out string map) && map == "3817448617", "raw chat workshop");
Check(PublicGameplayLogic.TryWorkshopChat("!host_workshop_map 3817448617", out _), "prefixed chat workshop");
Check(PublicGameplayLogic.TryWorkshopChat("！host_workshop_map 3817448617", out _), "fullwidth workshop");
Check(!PublicGameplayLogic.TryWorkshopChat("host_workshop_map 3817448617;quit", out _), "chat command chaining rejected");
Check(!PublicGameplayLogic.IsWorkshopChat("host_workshop_map_extra 123"), "unknown chat command not intercepted");
Check(PublicGameplayLogic.HideTeammate(0, 1, 3, 3, true, 999, 998001), "hide includes exact range");
Check(!PublicGameplayLogic.HideTeammate(0, 1, 3, 3, true, 999, 998002), "outside range visible");
Check(!PublicGameplayLogic.HideTeammate(0, 1, 3, 3, true, 0, 0), "zero range visible");
Check(!PublicGameplayLogic.HideTeammate(0, 1, 3, 2, true, 999, 0), "opponents never hidden");
Check(!PublicGameplayLogic.HideTeammate(0, 0, 3, 3, true, 999, 0), "self never hidden");
Check(!PublicGameplayLogic.HideTeammate(0, 1, 1, 1, true, 999, 0), "spectator never hidden");
Check(!PublicGameplayLogic.HideTeammate(0, 1, 2, 2, false, 999, 0), "dead teammate not hidden");
Check(!PublicGameplayLogic.HideTeammate(0, 1, 2, 2, true, 999, float.NaN), "nonfinite distance rejected");
var regeneration = new ZombieRegenerationState();
regeneration.Reset(35000, 2500, 0, 5);
var restored = regeneration.Update(2, 30000, 2000, 35000, 2500, 1, 5, 1750, 125);
Check(restored == (30000, 2000), "damage resets cooldown");
restored = regeneration.Update(2, 30000, 2000, 35000, 2500, 5.9f, 5, 1750, 125);
Check(restored == (30000, 2000), "no regeneration before cooldown");
restored = regeneration.Update(2, 30000, 2000, 35000, 2500, 6, 5, 1750, 125);
Check(restored == (31750, 2125), "health and armor restore together");
restored = regeneration.Update(2, restored.Health, restored.Armor, 35000, 2500, 6.1f, 5, 1750, 125);
Check(restored == (31750, 2125), "regeneration runs once per second");
regeneration.Reset(900, 2499, 0, 5);
restored = regeneration.Update(2, 900, 2499, 1000, 2500, 5, 5, 1750, 125);
Check(restored == (1000, 2500), "custom zombie ceiling enforced");
regeneration.Reset(50, 20, 0, 5);
restored = regeneration.Update(3, 50, 20, 35000, 2500, 100, 5, 1750, 125);
Check(restored == (50, 20), "humans do not regenerate");
restored = regeneration.Update(2, 0, 0, 35000, 2500, 1000, 5, 1750, 125);
Check(restored == (0, 0), "regeneration does not resurrect");
regeneration.Reset(500, 500, 0, 5); regeneration.Damage(4, 5);
Check(regeneration.Update(2, 500, 500, 35000, 2500, 8, 5, 1750, 125) == (500, 500), "event damage extends cooldown even if numeric damage zero");
var jumps = new BunnyHopSequence();
for (int number = 1; number <= 4; number++)
{ Check(jumps.Jump(number) && !jumps.SlowCurrentJump, "first four jumps unlimited " + number); jumps.ObserveGround(false, number + 0.1f); jumps.ObserveGround(true, number + 0.7f); }
Check(jumps.Jump(5) && jumps.SlowCurrentJump && jumps.ConsecutiveJumps == 0, "fifth jump capped and counter reset");
Check(!jumps.Jump(5.01f), "duplicate jump event ignored");
var velocity = PublicGameplayLogic.CapHorizontal(new(600, 800, 300), 250);
Check(Math.Abs(new Vector2(velocity.X, velocity.Y).Length() - 250) < 0.001f && velocity.Z == 300, "slowdown only affects horizontal speed");
jumps.ObserveGround(false, 5.1f); jumps.ObserveGround(true, 5.7f);
Check(!jumps.SlowCurrentJump && jumps.Jump(6) && jumps.ConsecutiveJumps == 1, "new sequence after fifth jump");
jumps.ObserveGround(true, 7); jumps.ObserveGround(true, 7.3f);
Check(jumps.ConsecutiveJumps == 0, "standing breaks sequence");
var clock = new DroppedGunClock();
Check(!clock.Update(false, true, 0, 60), "unowned map spawn not deleted");
Check(!clock.Update(true, true, 1, 60), "owned gun tracked");
Check(!clock.Update(false, true, 2, 60), "drop timer begins");
Check(!clock.Update(false, true, 61.9f, 60), "gun kept before sixty seconds");
Check(clock.Update(false, true, 62, 60), "gun deleted at sixty seconds");
clock.Update(true, true, 65, 60); clock.Update(false, true, 70, 60);
Check(!clock.Update(false, true, 100, 60), "pickup resets drop timer");
clock.Protected = true;
Check(!clock.Update(false, true, 1000, 60), "map gun permanently protected");
clock.Protected = false;
Check(!clock.Update(false, false, 1000, 60), "knife and utility not deleted");
var gate = new ManualRoundGate(); gate.MapStart();
Check(!gate.Blocks(0), "bootstrap permitted"); gate.RoundStarted();
Check(gate.Blocks(1), "automatic round ends blocked"); gate.Permit(10, 5);
Check(!gate.Blocks(12), "manual round end permitted");
Check(gate.Blocks(16), "manual permission expires"); gate.Permit(20, 5); gate.RoundStarted();
Check(gate.Blocks(21), "new round closes permission window");
var policy = PermissionPolicy.CreateDefault();
foreach (string command in new[] { "css_ze_hp", "css_ze_armor", "css_ze_noclip", "css_ze_ammo", "css_ze_bhop", "css_ze_hide", "css_ze_music", "css_ze_join", "css_ze_roundtime", "css_ze_restart", "css_ze_endround", "css_ze_workshop", "css_host_workshop_map" })
    Check(policy.Allows(null, false, command), "public without practice role " + command);
Check(!policy.HasAdminAccess(null, false), "public controls do not grant admin");
foreach (string command in new[] { "css_nospread", "css_ze_nospread" })
    Check(PermissionPolicy.IsPublicCommand(command) && policy.Allows(null, false, command), "no-spread is public without any role: " + command);
Check(!PermissionPolicy.IsPublicCommand("css_ze_nospread_verify"), "native diagnostics are not a public command");
foreach (int team in new[] { 0, 1, 2, 3 })
foreach (bool optedIn in new[] { false, true })
foreach (bool alive in new[] { false, true })
foreach (bool resetting in new[] { false, true })
    Check(PersonalNoSpreadLogic.Applies(team, optedIn, alive, resetting) == (team == 3 && optedIn && alive && !resetting),
        $"personal spread eligibility: team={team}, optedIn={optedIn}, alive={alive}, resetting={resetting}");
bool badSpreadBinary = false;
try { PersonalNoSpreadNativeData.Validate([0, 1, 2]); } catch (InvalidDataException) { badSpreadBinary = true; }
Check(badSpreadBinary, "unverified native build disables no-spread instead of installing unsafe hooks");
if (args.Length > 0)
{
    PersonalNoSpreadNativeData.Validate(File.ReadAllBytes(args[0]));
    Check(true, "installed server binary hash and both unique signatures verified");
}
foreach (string command in new[] { "css_ze_kick", "css_ze_team", "css_ze_ban", "css_ze_mode", "css_ze_permissions_reload" })
    Check(!policy.Allows(null, false, command), "management remains protected " + command);
foreach (float volume in new[] { 0f, 0.25f, 1f })
    Check(PublicGameplayLogic.TryReadSoundVolume(PublicGameplayLogic.VolumeParameters(volume), out float decoded) && decoded == volume, "SOS volume roundtrip " + volume);
Check(!PublicGameplayLogic.TryReadSoundVolume([0xe9, 0x54], out _), "truncated audio parameters rejected");
Check(PublicGameplayLogic.IsMapSound("point_soundevent") && !PublicGameplayLogic.IsMapSound("player") && !PublicGameplayLogic.IsMapSound("weapon_ak47"), "music filter excludes player and weapon sounds");
var noclip = new PersonalNoclipState();
Check(noclip.MapRequest(2) == 2 && noclip.MapRequest(0) == 0, "map owns movement when noclip is off");
noclip.Enable(2);
for (int repeat = 0; repeat < 30; repeat++) Check(noclip.MapRequest(2) == 7 && noclip.Enabled, "elf think cannot cancel noclip " + repeat);
Check(noclip.MapRequest(0) == 7 && noclip.RestoreType == 0, "charging cannot freeze active personal noclip");
Check(noclip.Disable() == 0 && !noclip.Enabled, "off returns current map freeze intent");
Check(noclip.MapRequest(2) == 2, "elf unfreeze works again after off");
noclip.Enable(2); noclip.MapRequest(0); noclip.MapRequest(2);
Check(noclip.Disable() == 2, "restore latest map intent instead of stale saved mode");
noclip.Enable(9); Check(noclip.Disable() == 2, "do not restore stale ladder mode");
Check(!ZombieWeaponPolicy.CanBuy(2) && ZombieWeaponPolicy.CanBuy(3), "purchase gate follows current team without practice exception");
Check(!ZombieWeaponPolicy.IsZombieTeam(2, 3, true), "CT AWS transient pawn team is not a zombie");
Check(ZombieWeaponPolicy.IsZombieTeam(2, 2, true), "actual T controller retains weapon guards");
Check(ZombieWeaponPolicy.IsZombieTeam(3, 2, true), "T controller guards during pawn transition");
Check(ZombieWeaponPolicy.IsZombieTeam(2, 3, false), "stale controller cannot bypass zombie guards");
Check(ZombieWeaponPolicy.IsZombieTeam(2, 0, true), "unassigned controller falls back to pawn team");
Check(!ZombieWeaponPolicy.IsZombieTeam(3, 3, true), "normal CT can carry firearms");
foreach (int team in new[] { 1, 2, 3, 2, 3 })
    Check(ZombieWeaponPolicy.CanCarry(team, false, false, false) == (team != 2), "team switches never permanently blacklist weapons " + team);
Check(ZombieWeaponPolicy.CanCarry(2, true, false, false), "zombies keep ordinary knife");
Check(!ZombieWeaponPolicy.CanCarry(2, true, true, false), "human map knife is not an ordinary knife");
Check(ZombieWeaponPolicy.CanCarry(2, true, true, true), "explicit zombie knife item remains usable");
Check(!ZombieWeaponPolicy.CanCarry(2, false, true, true), "allowlist never permits map guns");
Check(ZombieWeaponPolicy.CanCarry(3, true, true, false), "humans retain their artifacts");
foreach (int team in new[] { 1, 2, 3 })
foreach (bool knife in new[] { false, true })
foreach (bool mapItem in new[] { false, true })
foreach (bool listed in new[] { false, true })
    Check(ZombieWeaponPolicy.CanCarry(team, knife, mapItem, listed, true) == (team != 2 || knife),
        $"map knives allowed, non-knives remain blocked: team={team}, knife={knife}, map={mapItem}, listed={listed}");
foreach (var item in new[] { ("0", "elves_weapon"), ("0", "unlisted_zombie_knife&123"), ("1258", "") })
    Check(ZombieWeaponPolicy.CanCarry(2, true, ZombieWeaponPolicy.IsMapItem(item.Item1, item.Item2), false, true),
        "map knife does not need a whitelist: " + item);
Check(!ZombieWeaponPolicy.CanCarry(2, true, true, false, false), "strict compatibility mode retains map whitelist");
Check(!ZombieWeaponPolicy.CanBuy(2), "map knife allowance does not enable zombie purchases");
Check(!ZombieWeaponPolicy.IsMapItem("0", "") && ZombieWeaponPolicy.IsMapItem("1234", "") && ZombieWeaponPolicy.IsMapItem("", "elves_weapon"), "identify named and hammer map items");
foreach (string classname in new[] { "weapon_knife", "weapon_knife_t", "weapon_knife_kukri", "weapon_bayonet" })
    Check(ZombieWeaponPolicy.IsKnifeClass(classname), "allow normal knife class " + classname);
foreach (string classname in new[] { "weapon_ak47", "weapon_glock", "weapon_c4", "weapon_hegrenade", "weapon_taser", "item_assaultsuit", "weapon_knifegun" })
    Check(!ZombieWeaponPolicy.IsKnifeClass(classname), "reject non-knife grant " + classname);
foreach (ushort identifier in new ushort[] { 42, 59, 500, 507, 516, 526 }) Check(ZombieWeaponPolicy.IsKnifeDefinition(identifier), "knife item definition " + identifier);
foreach (ushort identifier in new ushort[] { 1, 7, 31, 43, 49, 50, 501, 20078 }) Check(!ZombieWeaponPolicy.IsKnifeDefinition(identifier), "non-knife item definition " + identifier);
var allowedKnives = new Dictionary<string, string[]> { ["test_map"] = ["zombie_knife"] };
Check(ZombieWeaponPolicy.MatchesMapKnife("test_map", "zombie_knife", allowedKnives), "map knife exact allowlist");
Check(ZombieWeaponPolicy.MatchesMapKnife("test_map", "zombie_knife&1357", allowedKnives), "template numeric instance matches");
foreach (string name in new[] { "zombie_knife_human", "zombie_knife&", "zombie_knife&1human", "human_knife" })
    Check(!ZombieWeaponPolicy.MatchesMapKnife("test_map", name, allowedKnives), "reject ambiguous item alias " + name);
Check(!ZombieWeaponPolicy.MatchesMapKnife("other_map", "zombie_knife", allowedKnives), "item allowlist is map-scoped");
string nativeData = "\"SetMove\" { \"library\" \"server\" \"windows\" \"\\x48\\x89\\x5C\\x24\\x2A\\x55\\x56\\x57\" }";
Check(NativeGameplayData.ReadSignature(nativeData, "SetMove", true) == "48 89 5C 24 ? 55 56 57", "installed gamedata signature conversion");
foreach (string invalid in new[] { "", nativeData + nativeData, nativeData.Replace("server", "client"), nativeData.Replace("\\x48", "ZZ") })
{
    bool rejected = false;
    try { NativeGameplayData.ReadSignature(invalid, "SetMove", true); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "native gamedata fails closed");
}
var reset = new TeamResetSequence();
int firstReset = reset.Begin(2);
Check(reset.Pending && reset.IsCurrent(firstReset, 2), "team reset pending on requested target");
Check(!reset.IsCurrent(firstReset, 3), "external team change invalidates pending callback");
int newestReset = reset.Begin(3);
Check(!reset.IsCurrent(firstReset, 2) && !reset.Complete(firstReset) && reset.Pending, "stale reset cannot complete newer request");
Check(reset.IsCurrent(newestReset, 3) && reset.Complete(newestReset) && !reset.Pending, "last requested team wins");
for (int repeat = 0; repeat < 30; repeat++)
{
    int request = reset.Begin(repeat % 3 + 1);
    Check(reset.IsCurrent(request, repeat % 3 + 1) && !reset.Complete(request - 1), "rapid team sequence " + repeat);
}
int beforeRound = reset.Revision; reset.Cancel();
Check(!reset.Pending && !reset.Complete(beforeRound), "round/map disconnect cancels old reset");
int sameTeam = reset.Begin(3); int sameTeamAgain = reset.Begin(3);
Check(!reset.IsCurrent(sameTeam, 3) && reset.IsCurrent(sameTeamAgain, 3), "same-team reset is a new request");
bool invalidTeam = false;
try { reset.Begin(0); } catch (ArgumentOutOfRangeException) { invalidTeam = true; }
Check(invalidTeam, "unassigned is not a respawn target");
var ownership = new ArtifactOwnership();
ownership.Pickup("elves_weapon", 100); ownership.Pickup("fashi_weapon", 200);
ownership.Pickup("elves_weapon", 200);
Check(ownership.Release(100).Length == 0 && ownership.Count == 2, "previous owner cannot remove transferred artifact");
Check(ownership.Release(200).Length == 2 && ownership.Count == 0, "release only artifacts owned by resetting pawn");
ownership.Pickup("elves_weapon&17", 100); ownership.Pickup("elves_weapon&19", 200);
Check(ownership.Release(100).SequenceEqual(["elves_weapon&17"]) && ownership.Count == 1, "template instance ownership isolated");
ownership.Clear(); Check(ownership.Count == 0, "map/round resets clear ownership ledger");
Check(ArtifactResetRule.TrySuffix("elves_weapon", "elves_weapon", out var noSuffix) && noSuffix == "", "exact map artifact instance");
Check(ArtifactResetRule.TrySuffix("elves_weapon&17", "elves_weapon", out var suffix) && suffix == "&17", "template suffix follows cleanup targets");
foreach (string invalid in new[] { "elves_weapon&", "elves_weapon&-1", "elves_weapon_human", "elves_weapon&1x" })
    Check(!ArtifactResetRule.TrySuffix(invalid, "elves_weapon", out _), "no broad cleanup alias " + invalid);
foreach (var rule in ArtifactResetRule.Defaults().Values.SelectMany(rules => rules))
{
    rule.Validate();
    Check(!rule.RemoveEntities.Contains("functions") && !rule.RemoveEntities.Contains("map_brush"), "shared map scripts retained for " + rule.WeaponName);
}
bool invalidRule = false;
try { new ArtifactResetRule { WeaponName = "elves_weapon", RemoveEntities = ["*"] }.Validate(); } catch (InvalidDataException) { invalidRule = true; }
Check(invalidRule, "cleanup wildcard rejected");
var prediction = new NoSpreadPredictionState();
Check(prediction.NeedsUpdate(false, 101, 0), "new client receives explicit prediction default");
prediction.Sent(false, 101, 0);
Check(!prediction.NeedsUpdate(false, 101, 1), "unchanged prediction does not spam network");
Check(prediction.NeedsUpdate(true, 101, 1), "toggle immediately synchronizes prediction");
prediction.Sent(true, 101, 1);
Check(prediction.LastSent == true, "prediction tracks sent value, not presumed acknowledgement");
Check(prediction.NeedsUpdate(false, 101, 1), "infection death or reset restores client prediction");
Check(prediction.NeedsUpdate(true, 102, 1), "new pawn resynchronizes prediction");
Check(prediction.NeedsUpdate(true, 101, 3), "periodic refresh repairs engine re-replication");
Check(prediction.NeedsUpdate(true, 101, 1, true), "explicit repair bypasses refresh delay");
var otherPrediction = new NoSpreadPredictionState(); otherPrediction.Sent(false, 201, 1);
Check(otherPrediction.LastSent == false && prediction.LastSent == true, "prediction state remains per-player");

int enumerations = 0, restores = 0;
var cleanupErrors = new List<(string Operation, Exception Error)>();
void ReportCleanup(string operation, Exception error) => cleanupErrors.Add((operation, error));
IEnumerable<int> UninitializedPlayers() { enumerations++; throw new InvalidOperationException("Global Variables not initialized yet."); }
PluginCleanup.ForPlayers(false, 0, UninitializedPlayers, _ => restores++, "cold", ReportCleanup);
PluginCleanup.ForPlayers(false, 2, UninitializedPlayers, _ => restores++, "map-ended", ReportCleanup);
PluginCleanup.ForPlayers(true, 0, UninitializedPlayers, _ => restores++, "empty", ReportCleanup);
Check(enumerations == 0 && restores == 0 && cleanupErrors.Count == 0, "cold/empty/map-ended cleanup never touches engine globals");
PluginCleanup.ForPlayers(true, 1, UninitializedPlayers, _ => restores++, "lost-globals", ReportCleanup);
Check(cleanupErrors.Count == 1 && cleanupErrors[0].Error.Message.Contains("Global Variables"), "lost globals reported without escaping cleanup");
PluginCleanup.ForPlayers(true, 3, () => new[] { 1, 2, 3 }, player =>
{ if (player == 2) throw new InvalidOperationException("disconnected"); restores++; }, "restore", ReportCleanup);
Check(restores == 2 && cleanupErrors.Count == 2, "one disconnected player does not prevent other clients being restored");
IEnumerable<int> BrokenEnumeration() { yield return 1; throw new InvalidOperationException("world torn down"); }
PluginCleanup.ForPlayers(true, 2, BrokenEnumeration, _ => restores++, "iterator", ReportCleanup);
Check(restores == 3 && cleanupErrors.Count == 3, "lazy enumeration failure is contained");
int unhooks = 0;
PluginCleanup.Run("observer", () => throw new InvalidOperationException("already removed"), ReportCleanup);
PluginCleanup.Run("spread", () => unhooks++, ReportCleanup);
PluginCleanup.Run("inaccuracy", () => unhooks++, ReportCleanup);
Check(unhooks == 2 && cleanupErrors.Last().Operation == "observer", "native unhooks continue after an independent cleanup failure");
PersonalNoSpreadNativeData.ValidateLayout(0xF70, 0x31C, 0x750, 0xF88);
Check(true, "verified native schema matches new build");
foreach (int field in Enumerable.Range(0, 4))
{
    int[] offsets = [0xF70, 0x31C, 0x750, 0xF88]; offsets[field] += 8;
    bool rejected = false;
    try { PersonalNoSpreadNativeData.ValidateLayout(offsets[0], offsets[1], offsets[2], offsets[3]); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "native layout rejects changed field " + field);
}
Console.WriteLine($"PUBLIC_GAMEPLAY_TESTS {passed}/{passed} passed");
