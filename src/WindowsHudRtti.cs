using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;
namespace ZePractice;
public static class WindowsHudRtti
{
    public static int FindVtable(byte[] image, string typeName)
    {
        using var stream = new MemoryStream(image, false);
        using var reader = new PEReader(stream);
        var headers = reader.PEHeaders;
        if (headers.PEHeader?.Magic != PEMagic.PE32Plus) throw new InvalidDataException("Expected a 64-bit PE image.");
        var runtime = headers.SectionHeaders.Single(section => section.Name == ".data");
        var readOnly = headers.SectionHeaders.Single(section => section.Name == ".rdata");
        var code = headers.SectionHeaders.Single(section => section.Name == ".text");
        ReadOnlySpan<byte> data = image.AsSpan(runtime.PointerToRawData, runtime.SizeOfRawData);
        ReadOnlySpan<byte> constants = image.AsSpan(readOnly.PointerToRawData, readOnly.SizeOfRawData);
        var name = Encoding.ASCII.GetBytes(".?AU" + typeName + "@@\0");
        int nameOffset = data.IndexOf(name);
        if (nameOffset < 16 || data[(nameOffset + name.Length)..].IndexOf(name) >= 0) throw new InvalidDataException("Expected a unique struct RTTI descriptor.");
        uint descriptor = checked((uint)(runtime.VirtualAddress + nameOffset - 16));
        var results = new HashSet<int>();
        for (int locatorOffset = 0; locatorOffset + 24 <= constants.Length; locatorOffset += 4)
        {
            var locator = constants[locatorOffset..];
            uint locatorRva = checked((uint)(readOnly.VirtualAddress + locatorOffset));
            if (BinaryPrimitives.ReadUInt32LittleEndian(locator) != 1 || BinaryPrimitives.ReadUInt32LittleEndian(locator[4..]) != 0 ||
                BinaryPrimitives.ReadUInt32LittleEndian(locator[12..]) != descriptor || BinaryPrimitives.ReadUInt32LittleEndian(locator[20..]) != locatorRva) continue;
            ulong absoluteLocator = headers.PEHeader.ImageBase + locatorRva;
            for (int reference = 0; reference + 16 <= constants.Length; reference += 8)
            {
                if (BinaryPrimitives.ReadUInt64LittleEndian(constants[reference..]) != absoluteLocator) continue;
                ulong firstFunction = BinaryPrimitives.ReadUInt64LittleEndian(constants[(reference + 8)..]);
                ulong codeStart = headers.PEHeader.ImageBase + (uint)code.VirtualAddress;
                if (firstFunction < codeStart || firstFunction >= codeStart + (uint)code.VirtualSize) continue;
                results.Add(readOnly.VirtualAddress + reference + 8);
            }
        }
        return results.Count == 1 ? results.Single() : throw new InvalidDataException("Expected a unique validated HUD string vtable.");
    }
}
