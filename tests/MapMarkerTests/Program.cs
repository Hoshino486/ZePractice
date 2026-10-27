using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using ZePractice;

int passed = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }
void CheckReplication(uint handle, int from, string message)
{
    var writes = Utilities.GlowWrites.Skip(from).Where(write => write.Entity == handle).Select(write => write.Offset).Order().ToArray();
    Check(writes.SequenceEqual(new[] { 48, 52, 56, 60, 64, 68, 72, 76, 80 }), message);
}
CCSWeaponBase Gun(uint handle, string name = "map_artifact")
{
    var weapon = new CCSWeaponBase { EntityHandle = new() { Raw = handle }, DesignerName = "weapon_p250", Entity = new() { Name = name } };
    Utilities.Entities.Add(weapon); return weapon;
}
var original = Color.FromArgb(100, 10, 20, 30);
var gun = Gun(100); gun.Glow.GlowColorOverride = original; gun.Glow.GlowType = 2; gun.Glow.GlowRange = 30;
var plugin = new ZePracticePlugin(); var service = new MapMarkerService(plugin); service.Initialize(); service.OnMapStart();
Check(gun.Glow.Glowing && gun.Glow.GlowType == 3, "map gun glows at discovery");
CheckReplication(100, 0, "initial glow notifies all nine actual network members");
Check(service.Diagnostics().Contains("\"visibility\":\"server-state-only\""), "diagnostics do not claim client visibility");
int writes = Utilities.NetworkWrites; plugin.Advance(0.2f);
Check(Utilities.NetworkWrites == writes, "unchanged glow does not write every tick");
int glowWrites = Utilities.GlowWrites.Count;
gun.Glow.Glowing = false; gun.Glow.GlowType = 0; gun.Glow.GlowRange = 0; plugin.Advance(0.2f);
CheckReplication(100, glowWrites, "overwritten glow retransmits member offsets");
Check(gun.Glow.Glowing && gun.Glow.GlowType == 3 && gun.Glow.GlowRange == 3000, "engine-cleared glow repairs without ownership transition");
Check(service.Diagnostics().Contains("\"glowRepairs\":1"), "repairs counted by actual state mismatch");
gun.Glow.GlowTeam = 2; gun.Glow.GlowTime = 1; gun.Glow.Flashing = true; gun.Glow.GlowColorOverride = Color.Red;
plugin.Advance(0.2f);
Check(gun.Glow.GlowTeam == -1 && gun.Glow.GlowTime == 0 && !gun.Glow.Flashing && gun.Glow.GlowColorOverride.G == 255, "team/color/lifetime overrides repaired");
writes = Utilities.NetworkWrites; glowWrites = Utilities.GlowWrites.Count; plugin.Advance(3);
CheckReplication(100, glowWrites, "periodic refresh transmits actual members even when values are unchanged");
Check(Utilities.NetworkWrites > writes, "periodic network refresh repairs stale client prediction");
glowWrites = Utilities.GlowWrites.Count; gun.OwnerEntity.IsValid = true; plugin.Advance(0.2f);
CheckReplication(100, glowWrites, "pickup restores original values over the network");
Check(!gun.Glow.Glowing && gun.Glow.GlowType == 2 && gun.Glow.GlowRange == 30 && gun.Glow.GlowColorOverride == original, "pickup restores exact original state, not last repaired state");
writes = Utilities.NetworkWrites; plugin.Advance(3);
Check(Utilities.NetworkWrites == writes, "held map gun is not forced glowing");
glowWrites = Utilities.GlowWrites.Count; gun.OwnerEntity.IsValid = false; plugin.Advance(0.2f);
CheckReplication(100, glowWrites, "second drop synchronizes all glow members");
Check(gun.Glow.Glowing, "second drop re-enables glow");
gun.Glow.Glowing = false;
Check(service.Diagnostics().Contains("\"glowingGuns\":0"), "diagnostics count actual glow rather than snapshot existence");
plugin.Advance(0.2f); Check(service.Diagnostics().Contains("\"glowingGuns\":1"), "diagnostics reflect repaired glow");
glowWrites = Utilities.GlowWrites.Count; plugin.Config.MapMarkers.MapWeaponGlow = false; plugin.Advance(0.2f);
CheckReplication(100, glowWrites, "disable synchronizes restored members");
Check(gun.Glow.GlowType == 2 && !gun.Glow.Glowing, "disabling restores original snapshot");
plugin.Config.MapMarkers.MapWeaponGlow = true; plugin.Advance(0.2f); Check(gun.Glow.Glowing, "re-enable restores map glow");
var ordinary = Gun(101, ""); service.TrackIssuedGun(ordinary); plugin.Spawn(ordinary); plugin.Advance(3);
Check(!ordinary.Glow.Glowing, "ordinary granted weapon stays unmarked");
ordinary.Entity!.Name = "late_map_artifact"; plugin.Advance(3);
Check(ordinary.Glow.Glowing, "map identity assigned after grant wins over issued exclusion");
service.TrackIssuedGun(ordinary); Check(ordinary.Glow.Glowing, "named artifact is not discarded by issued tracking");
var delayed = Gun(102); delayed.VData = null; plugin.Spawn(delayed);
Check(!delayed.Glow.Glowing, "not yet initialized weapon is deferred");
delayed.VData = new(); plugin.Advance(3); Check(delayed.Glow.Glowing, "late VData discovered on rescan");
var knife = Gun(103); knife.VData!.GearSlot = gear_slot_t.GEAR_SLOT_KNIFE; plugin.Spawn(knife); plugin.Advance(3);
Check(!knife.Glow.Glowing, "map knives not treated as firearm markers");
gun.Remove(); var replacement = Gun(100 + 0x8000, ""); service.TrackIssuedGun(replacement); plugin.Spawn(replacement); plugin.Advance(3);
Check(!replacement.Glow.Glowing, "entity slot reuse cannot inherit old marker");
glowWrites = Utilities.GlowWrites.Count; service.Dispose();
CheckReplication(102, glowWrites, "unload synchronizes restored members for a live map gun");
Check(!delayed.Glow.Glowing && !ordinary.Glow.Glowing && plugin.Listeners.Count == 0, "unload restores only tracked live entities and listeners");
Console.WriteLine($"MapMarkerTests {passed}/{passed}");
