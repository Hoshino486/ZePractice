using System.Text.RegularExpressions;

namespace ZePractice;

public static class NativeGameplayData
{
    public static string ReadSignature(string text, string name, bool windows)
    {
        var blocks = Regex.Matches(text, "\"" + Regex.Escape(name) + "\"\\s*\\{([^{}]*)\\}", RegexOptions.CultureInvariant);
        if (blocks.Count != 1) throw new InvalidDataException("Expected one native signature block: " + name);
        string platform = windows ? "windows" : "linux";
        var signature = Regex.Match(blocks[0].Groups[1].Value, "\"" + platform + "\"\\s*\"((?:\\\\x[0-9A-Fa-f]{2})+)\"", RegexOptions.CultureInvariant);
        if (!signature.Success || !Regex.IsMatch(blocks[0].Groups[1].Value, "\"library\"\\s*\"server\""))
            throw new InvalidDataException("Missing server signature for " + name + "/" + platform);
        var bytes = Regex.Matches(signature.Groups[1].Value, @"\\x([0-9A-Fa-f]{2})");
        if (bytes.Count < 8) throw new InvalidDataException("Native signature is too short: " + name);
        return string.Join(" ", bytes.Select(value => value.Groups[1].Value.Equals("2A", StringComparison.OrdinalIgnoreCase) ? "?" : value.Groups[1].Value.ToUpperInvariant()));
    }
}

public sealed class PersonalNoclipState
{
    public const byte Walk = 2, Noclip = 7;
    public bool Enabled { get; private set; }
    public byte RestoreType { get; private set; } = Walk;
    public void Enable(byte current)
    {
        RestoreType = current is 0 or 2 or 3 or 4 ? current : Walk;
        Enabled = true;
    }
    public byte MapRequest(byte requested)
    {
        if (!Enabled) return requested;
        if (requested != Noclip) RestoreType = requested is 0 or 2 or 3 or 4 ? requested : Walk;
        return Noclip;
    }
    public byte Disable() { Enabled = false; return RestoreType; }
}

public static class ZombieWeaponPolicy
{
    // CS2Fixes AWS temporarily changes only the pawn team while granting a CT
    // an opposing-team weapon. The live owning controller remains authoritative.
    public static bool IsZombieTeam(int pawnTeam, int controllerTeam, bool ownsCurrentPawn) =>
        (ownsCurrentPawn && controllerTeam is 2 or 3 ? controllerTeam : pawnTeam) == 2;

    public static bool CanBuy(int team) => team == 3;
    public static bool IsKnifeDefinition(ushort identifier) => identifier is 41 or 42 or 59 or 80 or 500 or 503 or 505 or 506 or 507 or 508 or 509 or 512 or 514 or 515 or 516 or 517 or 518 or 519 or 520 or 521 or 522 or 523 or 525 or 526;
    public static bool IsKnifeClass(string classname) => classname == "weapon_bayonet" || classname == "weapon_knife" || classname.StartsWith("weapon_knife_", StringComparison.Ordinal);
    public static bool IsMapItem(string? hammerId, string? targetName) =>
        !string.IsNullOrWhiteSpace(targetName) || !string.IsNullOrWhiteSpace(hammerId) && hammerId != "0";
    public static bool CanCarry(int team, bool knife, bool mapItem, bool zombieMapKnife, bool allowMapKnives = false) =>
        team != 2 || knife && (allowMapKnives || !mapItem || zombieMapKnife);
    public static bool MatchesMapKnife(string map, string? name, IReadOnlyDictionary<string, string[]> allowed)
    {
        if (string.IsNullOrEmpty(name) || !allowed.TryGetValue(map, out var names)) return false;
        return names.Any(candidate => name.Equals(candidate, StringComparison.Ordinal) ||
            name.StartsWith(candidate + "&", StringComparison.Ordinal) && name.AsSpan(candidate.Length + 1).Length > 0 &&
            name.AsSpan(candidate.Length + 1).IndexOfAnyExceptInRange('0', '9') < 0);
    }
}
