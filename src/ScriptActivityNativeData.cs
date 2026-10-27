using System.Security.Cryptography;

namespace ZePractice;

public static class ScriptActivityNativeData
{
    // Windows x64 CS2 1.41.9.2 / 2000935. Actual JS invoke wrapper at server.dll RVA 0x1CBB30.
    // rdx is the callback's C-string name. Return 1=completed, 2=V8 TryCatch caught an exception.
    // Do NOT hook the name lookup: it also runs for unregistered callbacks.
    public const string ServerSha256 = "2A229DD46C8AF0B52C1F49A3E343B99D9FFE946C102C56102A45B00F6A52349C";
    public const string Signature = "48 89 5C 24 10 48 89 74 24 20 55 57 41 54 41 56 41 57 48 8B EC 48 81 EC 80 00 00 00 33 DB 45 8B F1";
    public static void Validate(ReadOnlySpan<byte> binary)
    {
        if (Convert.ToHexString(SHA256.HashData(binary)) != ServerSha256)
            throw new InvalidDataException("Script callback interface needs validation for this server build.");
        var pattern = Convert.FromHexString(Signature.Replace(" ", ""));
        int offset = binary.IndexOf(pattern);
        if (offset < 0 || binary[(offset + 1)..].IndexOf(pattern) >= 0)
            throw new InvalidDataException("Script callback signature is absent or ambiguous.");
    }
}
