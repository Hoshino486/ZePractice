using System.Security.Cryptography;

namespace ZePractice;

public static class PracticeBotNativeData
{
    // Windows x64 CS2 1.41.9.3 / 2000938. CCSBot::Update located by signature.
    // Post-hook only: keep the original update, command simulation and animation intact.
    public const string ServerSha256="3788AFAA2DBCE98A7B2FA1E15B6B1A304759DD5B0F05802268952341B40777C4";
    public const string UpdateSignature="40 55 56 57 41 54 48 8D AC 24 08 FF FF FF 48 81 EC F8 01 00 00";
    public static void Validate(ReadOnlySpan<byte> binary)
    {
        if(Convert.ToHexString(SHA256.HashData(binary))!=ServerSha256)
            throw new InvalidDataException("Practice Bot input hook needs revalidation for this server build.");
        var pattern=Convert.FromHexString(UpdateSignature.Replace(" ",""));
        int offset=binary.IndexOf(pattern);
        if(offset<0 || binary[(offset+1)..].IndexOf(pattern)>=0)
            throw new InvalidDataException("Practice Bot Update signature is missing or ambiguous.");
    }
}
