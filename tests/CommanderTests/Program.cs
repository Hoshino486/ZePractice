using System.Numerics;
using ZePractice;

if (args.Length == 2 && args[0] == "--resource-probe")
{ Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(CommanderResources.Probe(args[1]))); return; }
int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS " + name); passed++; }
var state = new CommanderMarkerState();
Check(CommanderMarkerState.Eligible(true, true, 3), "living CT eligible");
foreach (int team in new[] {0, 1, 2}) Check(!CommanderMarkerState.Eligible(true, true, team), "other team rejected " + team);
Check(!CommanderMarkerState.Eligible(true, false, 3) && !CommanderMarkerState.Eligible(false, true, 3), "dead/invalid rejected");
var first = state.Place(new(100, 0, 0), 0);
Check(first.ExpiresAt == 10 && CommanderMarkerState.Remaining(first, 0) == 10, "initial 10 seconds");
Check(CommanderMarkerState.Remaining(first, 0.1) == 10 && CommanderMarkerState.Remaining(first, 1.1) == 9, "ceil countdown");
var extended = state.Extend(first.Id, 2)!;
Check(extended.ExpiresAt == 20 && extended.CreatedAt == 0, "add 10 not restart 10");
extended = state.Extend(first.Id, 3)!;
Check(extended.ExpiresAt == 30, "repeat extension stacks");
for (int n = 1; n < 5; n++) state.Place(new(100 + n * 100, 100, 0), 3);
Check(state.Markers.Count == 5 && state.Markers.Select(m => m.ColorIndex).Distinct().Count() == 5, "five unique colors");
var sixth = state.Place(new(800, 100, 0), 4);
Check(state.Markers.Count == 5 && state.Markers.All(m => m.Id != first.Id) && sixth.ColorIndex == first.ColorIndex, "sixth evicts oldest even if extended and reuses color");
state.Expire(13);
Check(state.Markers.Count == 1 && state.Markers[0].Id == sixth.Id, "expiration retains later point");
Check(state.Extend(sixth.Id, 14) == null, "expired point cannot be resurrected");
var aimed = state.Place(new(90000, 0, 0), 20);
Check(state.AimedAt(Vector3.Zero, Vector3.UnitX, 21)?.Id == aimed.Id, "no range limit on aim/extend");
Check(state.AimedAt(Vector3.Zero, -Vector3.UnitX, 21) == null, "behind-eye not targeted");
Check(state.AimedAt(new(0, 50, 0), Vector3.UnitX, 21) == null, "off-crosshair not targeted");
state.Clear(); Check(state.Markers.Count == 0, "explicit clear or round/map cleanup");
bool invalid = false; try { state.Place(new(float.NaN, 0, 0), 0); } catch (ArgumentOutOfRangeException) { invalid = true; }
Check(invalid, "nonfinite coordinates rejected");
foreach (var command in new[] {"css_leader", "css_ze_leader", "css_unleader", "css_clearpoints", "css_ze_clearpoints"}) Check(PermissionPolicy.IsPublicCommand(command), "public command " + command);
var drawing = new CommanderDrawingState();
Check(drawing.Sample(Vector3.Zero, 0) == null, "first sample only anchors stroke");
var oldest = drawing.Sample(new(10,0,0), .1)!;
var middle = drawing.Sample(new(20,0,0), .2)!;
var newest = drawing.Sample(new(30,0,0), .3)!;
drawing.Finish(1);
Check(oldest.ExpiresAt < middle.ExpiresAt && middle.ExpiresAt < newest.ExpiresAt, "oldest line segment disappears first");
Check(drawing.Segments.Single(s => s.Id == oldest.Id).ExpiresAt == 10.1, "each segment lasts ten seconds from creation, not release");
drawing.Expire(4.5); Check(drawing.Segments.Count == 3, "no segment expires early after release");
drawing.Expire(10.15); Check(drawing.Segments.Count == 2 && drawing.Segments.All(s => s.Id != oldest.Id), "old-to-new sequential erase");
drawing.Expire(10.25); Check(drawing.Segments.Count == 1 && drawing.Segments[0].Id == newest.Id, "newest segment remains last");
drawing.Expire(10.31); Check(drawing.Segments.Count == 0, "stroke completely expires");
drawing.Sample(Vector3.Zero, 12); Check(drawing.Sample(new(5000,0,0), 12.1) == null, "no line across teleport/large aim jump");
drawing.Clear();
for (int i=0;i<1000;i++) drawing.Sample(new(i * 5,0,0), 13+i*.001);
Check(drawing.Segments.Count <= CommanderDrawingState.MaximumSegments, "bounded line entities during long drawing");
drawing.Clear();
drawing.Sample(Vector3.Zero, 100);
var heldOld = drawing.Sample(new(10,0,0), 100.1)!;
var heldNew = drawing.Sample(new(20,0,0), 106)!;
drawing.Sample(new(30,0,0), 110.2);
Check(drawing.Segments.All(s => s.Id != heldOld.Id) && drawing.Segments.Any(s => s.Id == heldNew.Id), "old segments expire while R remains held");
double expiry = drawing.Segments.Single(s => s.Id == heldNew.Id).ExpiresAt;
drawing.Finish(112); drawing.Finish(113);
Check(drawing.Segments.Single(s => s.Id == heldNew.Id).ExpiresAt == expiry, "release and leader departure never extend segment lifetime");
Check(drawing.Sample(new(40,0,0),114) == null, "new press starts a separate stroke");
Check(CommanderCountdownLayout.Height > 48 * .22f * 4, "countdown glyphs over four times old height");
foreach (var eye in new[] {new Vector3(100,0,60),new Vector3(0,100,60),new Vector3(-100,0,60),new Vector3(0,-100,60)})
{
    float yaw=CommanderCountdownLayout.FacingYaw(Vector3.Zero,eye);
    var left=CommanderCountdownLayout.Position(Vector3.Zero,yaw,0,2);
    var right=CommanderCountdownLayout.Position(Vector3.Zero,yaw,1,2);
    Check(left.Z==CommanderCountdownLayout.BaselineAboveIcon&&right.Z==left.Z,"upright baseline above icon " + yaw);
    Check(Math.Abs(Vector3.Distance(left,right)-CommanderCountdownLayout.Spacing)<.01,"fixed readable numeral spacing " + yaw);
    Check((left+right)*.5f==new Vector3(0,0,CommanderCountdownLayout.BaselineAboveIcon),"countdown horizontally centered " + yaw);
}
Check(CommanderCountdownLayout.Digits(int.MaxValue)=="2147483647", "long extension never truncates countdown digits");
CountdownFacingTests.Run(Check);
CountdownHotReloadTests.Run(Check);
foreach(var c in new[]{"css_thirdperson","css_ze_thirdperson"})Check(PermissionPolicy.IsPublicCommand(c), "all-player thirdperson " + c);
CommanderResourceTests.Run(Check);
Console.WriteLine($"PASS {passed} commander tests");
