using System.Numerics;
using System.Xml.Linq;
using ZePractice;

int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
var t = new MovementTelemetryLogic();
MovementTelemetryLogic.Reading Sample(double time, float z, float vz = 0, bool ground = false, bool walk = true, uint pawn = 1, float vx = 0, float vy = 0) =>
    t.Update(new(time, pawn, new(0, 0, z), new(vx, vy, vz), ground, walk));
Check(Sample(0, 1000, 0, true, vx: 3, vy: 4).GroundSpeed == 5, "horizontal magnitude excludes vertical");
Check(Sample(.1, 1000, 10, true).VerticalSpeed == 10, "signed vertical speed");
Check(Sample(.2, 1020, 200).JumpHeight == 20, "jump starts at foot takeoff not sea level");
Check(Sample(.3, 1056, 0).JumpHeight == 56, "peak tracked");
Check(Sample(.4, 1040, -150).JumpHeight == 56, "descending retains peak");
var landed = Sample(.5, 1000, 0, true);
Check(landed.JumpHeight == 56 && !landed.Jumping, "landing stores previous jump");
Check(Sample(.6, 1000, 0, true).JumpHeight == 56, "standing retains jump result");
Check(Sample(.7, 1010, 200).JumpHeight == 10, "new jump replaces old result");
Check(Sample(.8, 3000, 10).JumpHeight == 0, "teleport discards in-progress jump");
Check(Sample(.9, 3000, 0, true).JumpHeight == 0, "teleport result does not leak");
Check(Sample(1, 2995, -100).JumpHeight == 0, "walking off ledge is not a jump");
Check(Sample(1.1, 3000, 100).JumpHeight == 0, "air boost after falling does not invent takeoff");
t.Reset(); Check(Sample(1.2, 100, 100).JumpHeight == 0, "midair first sample is not a jump");
Sample(1.3, 100, 0, true); Sample(1.4, 110, 200);
Check(Sample(1.5, 140, 200, walk: false).JumpHeight == 0, "noclip ladder swimming invalidate jump");
Check(Sample(1.6, 150, 200).JumpHeight == 0, "return from special move midair waits for ground");
Sample(1.7, 100, 0, true); Sample(1.8, 120, 200);
Check(Sample(2.5, 130, 100).JumpHeight == 0, "sampling gap invalidates stale jump");
Sample(2.6, 100, 0, true); Sample(2.7, 120, 200);
Check(Sample(2.8, 130, 100, pawn: 2).JumpHeight == 0, "new pawn cannot inherit old jump");
Sample(2.9, 100, 0, true, pawn: 2); Sample(3, 120, 200, pawn: 2);
Check(Sample(2.99, 130, 100, pawn: 2).JumpHeight == 0, "clock rollback resets");
Check(Sample(double.NaN, 130).GroundSpeed == 0, "nan time rejected");
Check(Sample(4, float.NaN).JumpHeight == 0, "nan position rejected");
Check(Sample(4.1, 0, float.PositiveInfinity).VerticalSpeed == 0, "infinite velocity rejected");
Check(Sample(4.2, 0, vx: float.MaxValue).GroundSpeed == 999999, "finite large velocity cannot overflow HUD");
Check(PermissionPolicy.IsPublicCommand("css_ze_speed"), "HUD toggle public");
Check(PermissionPolicy.IsPublicCommand("css_ze_movement_status"), "HUD diagnostics public");
string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string service = File.ReadAllText(Path.Combine(root, "src/MovementHudService.cs"));
Check(service.Contains("public const string LayoutResource = Layout;"), "spawn uses the same compiled resource that installation validates");
Check(service.Contains("Reset(true); _active = true"), "refresh removes existing layouts instead of leaking them");
var xml = XDocument.Load(Path.Combine(root, "panorama/layout/custom_game/zepractice/movement.xml"));
Check(!xml.Descendants().Any(n => n.Name.LocalName is "scripts" or "script" or "Button"), "no JavaScript or input buttons");
Check(xml.Descendants("Panel").All(n => n.Attributes().All(a => a.Name.LocalName is "id" or "class" or "hittest")), "client Panel attribute whitelist; hittestchildren is not supported");
Check(xml.Descendants().Where(n => n.Attribute("id") != null).Select(n => n.Attribute("id")!.Value).Distinct().Count() == 3, "three unique HUD panels");
Check(xml.Descendants().Single(n => (string?)n.Attribute("id") == "movement_window").Attribute("class")!.Value.Contains("hidden"), "HUD defaults hidden");
Check(File.ReadAllText(Path.Combine(root, "panorama/styles/custom_game/zepractice/movement.css")).Contains("margin-bottom: 86px"), "HUD above bottom team emblem");
string[] required = ["panorama/layout/custom_game/zepractice/movement.vxml_c", "panorama/styles/custom_game/zepractice/movement.vcss_c"];
foreach (string path in args)
{
    var result = CommanderResources.Probe(path, required);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { path, result }));
    Check(result.Ready, "built movement layout and style CRC verified");
    Check(!CommanderResources.Probe(path, ["does/not/exist.vxml_c"]).Ready, "arbitrary missing requirement rejected");
    Check(!CommanderResources.Probe(path, []).Ready, "empty requirement cannot certify resource readiness");
}
Console.WriteLine($"PASS {passed} movement telemetry tests");
