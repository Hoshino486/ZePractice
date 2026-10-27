using ZePractice;
int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; }
Check(RadioPanelLogic.PageCount(0) == 1, "empty page");
Check(RadioPanelLogic.PageCount(6) == 1, "six options");
Check(RadioPanelLogic.PageCount(7) == 2, "seventh option");
Check(RadioPanelLogic.PageCount(int.MaxValue) == 357913942, "overflow-safe page count");
Check(RadioPanelLogic.ClampPage(-1, 16) == 0, "negative page");
Check(RadioPanelLogic.ClampPage(999, 16) == 2, "last page clamp");
for (int key = 1; key <= 6; key++) Check(RadioPanelLogic.TrySelection(key, 1, 12, out int index) && index == key + 5, "correct second-page index");
Check(!RadioPanelLogic.TrySelection(0, 0, 12, out _), "zero not an option");
Check(!RadioPanelLogic.TrySelection(7, 0, 12, out _), "previous not an option");
Check(!RadioPanelLogic.TrySelection(8, 0, 12, out _), "next not an option");
Check(!RadioPanelLogic.TrySelection(9, 0, 12, out _), "back not an option");
Check(!RadioPanelLogic.TrySelection(6, 1, 7, out _), "empty row rejected");
Check(!RadioPanelLogic.TrySelection(1, int.MaxValue, 12, out _), "overflow rejected");
Check(!RadioPanelLogic.TrySelection(1, -1, 12, out _), "negative selection page");
if (args.Length > 0)
{
    byte[] image = File.ReadAllBytes(args[0]);
    int address = WindowsHudRtti.FindVtable(image, "HUDPanelDialogVariableString_t");
    Check(address > 0, "actual installed server struct vtable found");
    bool rejected = false;
    try { WindowsHudRtti.FindVtable(image, "ZE_NONEXISTENT_STRUCT"); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "missing RTTI is not guessed");
}
var requests = new RadioPanelRequests();
long first = requests.Enqueue(1);
long latest = requests.Enqueue(1);
Check(!requests.Take(1, first), "obsolete menu request rejected");
Check(requests.Contains(1), "obsolete request preserves replacement");
Check(requests.Take(1, latest), "latest request rendered once");
Check(!requests.Take(1, latest), "request cannot render twice");
long closed = requests.Enqueue(1);
requests.Cancel(1);
Check(!requests.Take(1, closed), "close cancels pending open");
long previousMap = requests.Enqueue(1);
requests.Clear();
long nextMap = requests.Enqueue(1);
Check(!requests.Take(1, previousMap), "map transition rejects stale menu");
Check(requests.Take(1, nextMap), "new map request survives stale callback");
long other = requests.Enqueue(2);
requests.Cancel(1);
Check(requests.Take(2, other), "closing one slot preserves other players");
Check(requests.Count == 0, "pending menu requests drained");
Console.WriteLine($"RADIO_LOGIC_TESTS {passed}/{passed} passed");
