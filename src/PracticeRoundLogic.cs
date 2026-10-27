using System.Globalization;
using System.Text.RegularExpressions;
namespace ZePractice;

public static class PracticeRoundLogic
{
    public static int ReadIntermissionOffset(string text, bool windows)
    {
        var blocks = Regex.Matches(text, "\"CCSGameRules_GoToIntermission\"\\s*\\{([^{}]*)\\}", RegexOptions.CultureInvariant);
        if (blocks.Count != 1) throw new InvalidDataException("Expected one CCSGameRules_GoToIntermission offset block.");
        string platform = windows ? "windows" : "linux";
        var values = Regex.Matches(blocks[0].Groups[1].Value, "\"" + platform + "\"\\s*\"([0-9]+)\"", RegexOptions.CultureInvariant);
        if (values.Count != 1 || !int.TryParse(values[0].Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int offset) || offset < 16 || offset > 512)
            throw new InvalidDataException("Invalid intermission vtable offset for " + platform + ".");
        return offset;
    }
    public static bool ShouldBlockIntermission(bool practice, bool enabled, bool disposed) => practice && enabled && !disposed;
}
