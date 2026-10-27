using System.Security.Cryptography;

namespace ZePractice;

public static class PracticeBotNativeData
{
    // Windows x64 CS2 1.41.9.2 / 2000935. CCSBot::Update RVA 0x2EF320.
    // Post-hook only: keep the original update, command simulation and animation intact.
    public const string ServerSha256="2A229DD46C8AF0B52C1F49A3E343B99D9FFE946C102C56102A45B00F6A52349C";
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
