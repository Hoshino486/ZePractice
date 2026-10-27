using System.Text;

namespace ZePractice;

/// <summary>Read map filenames from downloaded Workshop VPK directory trees.
/// Numeric addon archive names and Workshop titles are not map names.</summary>
public static class InstalledWorkshopMapIndex
{
    public sealed record Entry(string Name, string WorkshopId, string Package, DateTime ModifiedUtc);
    public static IEnumerable<Entry> Read(string gameDirectory)
    {
        string[] roots = [
            Path.Combine(gameDirectory, "bin", "win64", "steamapps", "workshop", "content", "730"),
            Path.Combine(gameDirectory, "bin", "linuxsteamrt64", "steamapps", "workshop", "content", "730"),
            Path.GetFullPath(Path.Combine(gameDirectory, "..", "..", "..", "workshop", "content", "730"))];
        var result = new List<Entry>();
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            foreach (string directory in Directory.EnumerateDirectories(root, "*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                string id = Path.GetFileName(directory);
                if (!AdminPracticeMapCatalog.IsWorkshopId(id)) continue;
                string package = Path.Combine(directory, id + "_dir.vpk");
                foreach (string name in ReadMapNames(package)) result.Add(new(name, id, package, File.GetLastWriteTimeUtc(package)));
            }
        }
        return result.OrderByDescending(e => e.ModifiedUtc).ThenBy(e => e.WorkshopId, StringComparer.Ordinal);
    }

    public static IReadOnlyList<string> ReadMapNames(string package)
    {
        try
        {
            using var file = new FileStream(package, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var input = new BinaryReader(file, Encoding.UTF8, true);
            if (file.Length < 12 || input.ReadUInt32() != 0x55AA1234) return [];
            uint version = input.ReadUInt32(), treeSize = input.ReadUInt32();
            if (version is not (1 or 2) || treeSize is 0 or > 16 * 1024 * 1024) return [];
            long header = version == 2 ? 28 : 12, dataStart = header + treeSize;
            long dataLength = file.Length - dataStart;
            if (version == 2)
            {
                dataLength = input.ReadUInt32();
                long archiveMd5 = input.ReadUInt32(), otherMd5 = input.ReadUInt32(), signature = input.ReadUInt32();
                if (dataStart + dataLength + archiveMd5 + otherMd5 + signature != file.Length) return [];
            }
            if (dataLength < 0) return [];
            byte[] tree = input.ReadBytes(checked((int)treeSize));
            if (tree.Length != treeSize) return [];
            using var buffer = new MemoryStream(tree, false);
            using var reader = new BinaryReader(buffer, Encoding.UTF8);
            string ReadString()
            {
                var bytes = new List<byte>(); byte next;
                while ((next = reader.ReadByte()) != 0)
                { if (bytes.Count >= 1024) throw new InvalidDataException("VPK path is too long"); bytes.Add(next); }
                return Encoding.UTF8.GetString(bytes.ToArray());
            }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lengths = new Dictionary<ushort, long>();
            string extension, folder, name;
            while ((extension = ReadString()) != "")
                while ((folder = ReadString()) != "")
                    while ((name = ReadString()) != "")
                    {
                        string member = (folder == " " ? "" : folder + "/") + name + "." + extension;
                        if (!members.Add(member) || member.Split('/').Any(p => p is ".." or ".") || member.Contains('\\')) return [];
                        _ = reader.ReadUInt32(); // CRC is not a map-name field; do not read GB-sized payloads on the game thread.
                        ushort preload = reader.ReadUInt16(), archive = reader.ReadUInt16();
                        uint offset = reader.ReadUInt32(), length = reader.ReadUInt32();
                        if (reader.ReadUInt16() != ushort.MaxValue || buffer.Position + preload > buffer.Length) return [];
                        buffer.Position += preload;
                        if (length > 0)
                        {
                            long available = dataLength;
                            if (archive != 32767 && !lengths.TryGetValue(archive, out available))
                            {
                                string stem = Path.GetFileNameWithoutExtension(package);
                                if (!stem.EndsWith("_dir", StringComparison.OrdinalIgnoreCase)) return [];
                                string chunk = Path.Combine(Path.GetDirectoryName(package)!, stem[..^4] + "_" + archive.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + ".vpk");
                                if (!File.Exists(chunk)) return [];
                                lengths[archive] = available = new FileInfo(chunk).Length;
                            }
                            if ((long)offset + length > available) return []; // Incomplete downloads are not installed maps.
                        }
                        if (folder.Equals("maps", StringComparison.OrdinalIgnoreCase) &&
                            extension.Equals("vpk", StringComparison.OrdinalIgnoreCase) && length + preload > 0 &&
                            AdminPracticeMapCatalog.IsPlayableMapFileName(name)) names.Add(name);
                    }
            return buffer.Position == buffer.Length ? names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray() : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OverflowException)
        { return []; }
    }
}
