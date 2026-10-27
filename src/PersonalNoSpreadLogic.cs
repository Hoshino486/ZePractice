using System.Security.Cryptography;

namespace ZePractice;

public static class PersonalNoSpreadLogic
{
    public static bool Applies(int team, bool enabled, bool alive, bool resetting) => team == 3 && enabled && alive && !resetting;
}

public sealed class NoSpreadPredictionState
{
    public bool? LastSent { get; private set; }
    private uint _pawn;
    private float _nextRefresh;
    public bool NeedsUpdate(bool enabled, uint pawn, float now, bool force = false) =>
        force || LastSent != enabled || _pawn != pawn || now >= _nextRefresh;
    public void Sent(bool enabled, uint pawn, float now)
    { LastSent = enabled; _pawn = pawn; _nextRefresh = now + 2; }
}

public static class PersonalNoSpreadNativeData
{
    // CS2 1.41.9.2 / 2000935, Windows x64. Spread RVA 0xA08050, inaccuracy 0xA06AD0.
    public const string ServerSha256 = "2A229DD46C8AF0B52C1F49A3E343B99D9FFE946C102C56102A45B00F6A52349C";
    public const string SpreadSignature = "48 63 91 70 0F 00 00 48 8B 81 20 03 00 00 85 D2 78 10 48 83 FA 02 73 0A F3 0F 10 84 90 50 07 00 00 C3";
    public const string InaccuracySignature = "48 89 5C 24 10 55 56 57 48 81 EC B0 00 00 00 44 0F 29 84 24 80 00 00 00 49 8B F0 48 8B EA 48 8B F9";

    public static void Validate(ReadOnlySpan<byte> binary)
    {
        string actualHash = Convert.ToHexString(SHA256.HashData(binary));
        if (!actualHash.Equals(ServerSha256, StringComparison.Ordinal))
            throw new InvalidDataException($"Personal no-spread requires the verified server build; actual SHA256={actualHash}, expected={ServerSha256}. Update native data after a game update; other gameplay remains available.");
        foreach (string signature in new[] { SpreadSignature, InaccuracySignature })
        {
            var pattern = Convert.FromHexString(signature.Replace(" ", ""));
            int first = binary.IndexOf(pattern);
            if (first < 0 || binary[(first + 1)..].IndexOf(pattern) >= 0)
                throw new InvalidDataException("Personal no-spread signature is missing or ambiguous.");
        }
    }

    public static void ValidateLayout(int weaponMode, int subclassId, int spread, int accuracyPenalty)
    {
        if (weaponMode != 0xF70 || subclassId + 4 != 0x320 || spread != 0x750 || accuracyPenalty != 0xF88)
            throw new InvalidDataException($"Personal no-spread schema layout mismatch: weaponMode=0x{weaponMode:X}, subclassId=0x{subclassId:X}, spread=0x{spread:X}, accuracyPenalty=0x{accuracyPenalty:X}.");
    }
}
