using System.Text;

namespace ZePractice;

/// <summary>Read the server's actual Workshop VPK, never an authoring/staging copy.
/// Models use this during map precache; hot reload additionally validates all ten
/// glyphs against the engine's currently mounted models before restoring readiness.
/// Resource-only HUD layouts can refresh after hot load or an empty-server remount.</summary>
public static class CommanderResources
{
    public const string WorkshopId = "3818340815";
    public sealed record Availability(bool Ready, string Status, string Path);
    public static readonly string[] Required = Enumerable.Range(0, 10)
        .Select(n => $"models/saibochana/commander/digits/{n}.vmdl_c")
        .Concat(["materials/saibochana/commander/digits/digit.vmat_c",
            "materials/saibochana/commander/digits/white_tga_bbd9c727.vtex_c"]).ToArray();

    public static Availability Installed(string gameDirectory, IEnumerable<string>? requiredMembers = null)
    {
        string root = gameDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Path.GetFileName(root).Equals("csgo", StringComparison.OrdinalIgnoreCase)) root = Directory.GetParent(root)!.FullName;
        return Probe(Path.Combine(root, "bin", "win64", "steamapps", "workshop", "content", "730", WorkshopId, WorkshopId + "_dir.vpk"), requiredMembers);
    }

    public static Availability Probe(string path, IEnumerable<string>? requiredMembers = null)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var input = new BinaryReader(file, Encoding.UTF8, true);
            if (file.Length < 12 || input.ReadUInt32() != 0x55AA1234) return new(false, "invalid-vpk", path);
            uint version = input.ReadUInt32(), treeLength = input.ReadUInt32();
            if (version is not (1 or 2) || treeLength is 0 or > 8 * 1024 * 1024) return new(false, "invalid-vpk-header", path);
            long header = version == 2 ? 28 : 12, dataStart = header + treeLength;
            long dataLength = file.Length - dataStart;
            if (version == 2)
            {
                dataLength = input.ReadUInt32();
                long archives = input.ReadUInt32(), other = input.ReadUInt32(), signature = input.ReadUInt32();
                if (dataStart + dataLength + archives + other + signature != file.Length) return new(false, "incomplete-vpk", path);
            }
            if (dataLength < 0) return new(false, "incomplete-vpk", path);
            byte[] tree = input.ReadBytes(checked((int)treeLength));
            if (tree.Length != treeLength) return new(false, "incomplete-vpk-tree", path);
            using var directory = new MemoryStream(tree, false);
            using var reader = new BinaryReader(directory, Encoding.UTF8, true);
            string CString()
            {
                var bytes = new List<byte>(); byte next;
                while ((next = reader.ReadByte()) != 0)
                { if (bytes.Count >= 1024) throw new InvalidDataException("VPK name too long"); bytes.Add(next); }
                return Encoding.UTF8.GetString(bytes.ToArray());
            }
            var required = (requiredMembers ?? Required).ToHashSet(StringComparer.Ordinal);
            if (required.Count == 0) return new(false, "empty-required-assets", path);
            var found = new HashSet<string>(StringComparer.Ordinal);
            var entries = new HashSet<string>(StringComparer.Ordinal);
            string ext, folder, name;
            while ((ext = CString()) != "")
                while ((folder = CString()) != "")
                    while ((name = CString()) != "")
                    {
                        string member = ((folder == " " ? "" : folder + "/") + name + (ext == " " ? "" : "." + ext)).ToLowerInvariant();
                        if (!entries.Add(member) || member.Split('/').Any(s => s is ".." or ".")) throw new InvalidDataException("Invalid VPK member");
                        uint crc = reader.ReadUInt32(); ushort preload = reader.ReadUInt16(), archive = reader.ReadUInt16();
                        uint offset = reader.ReadUInt32(), length = reader.ReadUInt32();
                        if (reader.ReadUInt16() != ushort.MaxValue || directory.Position + preload > directory.Length) throw new InvalidDataException("Invalid VPK entry");
                        byte[] prefix = reader.ReadBytes(preload);
                        if (!required.Contains(member)) continue;
                        if (archive != 32767 || (long)offset + length > dataLength || length > 16 * 1024 * 1024 || length + preload == 0)
                            return new(false, requiredMembers == null ? "incomplete-countdown-assets" : "incomplete-required-assets", path);
                        file.Position = dataStart + offset;
                        byte[] payload = input.ReadBytes(checked((int)length));
                        uint check = uint.MaxValue;
                        foreach (byte b in prefix.Concat(payload))
                        {
                            check ^= b;
                            for (int bit = 0; bit < 8; bit++) check = (check >> 1) ^ ((check & 1) == 0 ? 0 : 0xEDB88320u);
                        }
                        if (payload.Length != length || ~check != crc) return new(false, requiredMembers == null ? "countdown-crc-mismatch" : "required-assets-crc-mismatch", path);
                        found.Add(member);
                    }
            if (directory.Position != directory.Length) return new(false, "invalid-vpk-tree", path);
            return new(found.Count == required.Count, found.Count == required.Count ? "verified-workshop-assets" :
                requiredMembers == null ? "installed-revision-missing-countdown-assets" : "installed-revision-missing-required-assets", path);
        }
        catch (FileNotFoundException) { return new(false, "workshop-not-installed", path); }
        catch (DirectoryNotFoundException) { return new(false, "workshop-not-installed", path); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or OverflowException or ArgumentException)
        { return new(false, "workshop-read-pending", path); }
    }
}
