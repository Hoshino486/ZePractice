using ZePractice;
int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
const string sample = "\"CCSGameRules_GoToIntermission\" { \"windows\" \"128\" \"linux\" \"129\" }";
Check(PracticeRoundLogic.ReadIntermissionOffset(sample, true) == 128, "Windows named offset");
Check(PracticeRoundLogic.ReadIntermissionOffset(sample, false) == 129, "Linux named offset");
foreach (string invalid in new[] { "", sample + sample, sample.Replace("128", "0"), sample.Replace("128", "999999999999"), sample.Replace("windows", "unknown"), sample.Replace("128", "128;quit") })
{
    bool rejected = false;
    try { PracticeRoundLogic.ReadIntermissionOffset(invalid, true); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "Malformed gamedata rejected");
}
foreach (bool practice in new[] { false, true })
foreach (bool enabled in new[] { false, true })
foreach (bool disposed in new[] { false, true })
    Check(PracticeRoundLogic.ShouldBlockIntermission(practice, enabled, disposed) == (practice && enabled && !disposed), "Practice-only lifecycle gate");
if (args.Length > 1) Check(PracticeRoundLogic.ReadIntermissionOffset(File.ReadAllText(args[0]), true) == int.Parse(args[1]), "Installed server gamedata");
Console.WriteLine("TOTAL " + passed + " passed");
