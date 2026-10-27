using System.Text;
using ZePractice;

static class CommanderResourceTests
{
    public static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "ze-countdown-resource-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string Write(string name, IEnumerable<string> members, ushort archive = 32767)
        {
            using var tree = new MemoryStream(); using var payload = new MemoryStream();
            using var w = new BinaryWriter(tree, Encoding.UTF8, true);
            void Text(string s) { w.Write(Encoding.UTF8.GetBytes(s)); w.Write((byte)0); }
            foreach (var extensions in members.GroupBy(p => Path.GetExtension(p)))
            {
                Text(extensions.Key.TrimStart('.'));
                foreach (var paths in extensions.GroupBy(p => p[..p.LastIndexOf('/')]))
                {
                    Text(paths.Key);
                    foreach (string p in paths)
                    {
                        Text(Path.GetFileNameWithoutExtension(p));
                        byte[] content = Encoding.UTF8.GetBytes("fixture-content:" + p);
                        uint crc = uint.MaxValue;
                        foreach (byte b in content) { crc ^= b; for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320u); }
                        w.Write(~crc); w.Write((ushort)0); w.Write(archive); w.Write((uint)payload.Position); w.Write((uint)content.Length); w.Write(ushort.MaxValue);
                        payload.Write(content);
                    }
                    Text("");
                }
                Text("");
            }
            Text("");
            string file = Path.Combine(root, name); using var output = File.Create(file); using var header = new BinaryWriter(output);
            header.Write(0x55AA1234u); header.Write(2u); header.Write((uint)tree.Length); header.Write((uint)payload.Length);
            header.Write(0u); header.Write(0u); header.Write(0u); tree.Position = payload.Position = 0; tree.CopyTo(output); payload.CopyTo(output);
            return file;
        }
        check(!CommanderResources.Probe(Path.Combine(root, "missing.vpk")).Ready, "missing Workshop bundle keeps fallback");
        string complete = Write("complete.vpk", CommanderResources.Required);
        check(CommanderResources.Probe(complete).Ready, "complete downloaded glyph and material set enables model countdown");
        check(!CommanderResources.Probe(Write("old.vpk", ["models/saibochana/commander/marker.vmdl_c"])).Ready, "previous approved resource bundle keeps readable fallback");
        check(!CommanderResources.Probe(Write("partial.vpk", CommanderResources.Required.Take(11))).Ready, "missing texture prevents activation");
        check(!CommanderResources.Probe(Write("duplicate.vpk", CommanderResources.Required.Concat(CommanderResources.Required.Take(1)))).Ready, "duplicate VPK entry rejected");
        check(!CommanderResources.Probe(Write("archive.vpk", CommanderResources.Required, 0)).Ready, "incomplete multi-file resource archive is not ready");
        check(CommanderResources.Probe(Write("extra.vpk", CommanderResources.Required.Append("resource/other.txt"))).Ready, "future additive Workshop assets remain compatible");
        byte[] data = File.ReadAllBytes(complete); data[^1] ^= 1; string corrupt = Path.Combine(root, "crc.vpk"); File.WriteAllBytes(corrupt, data);
        check(CommanderResources.Probe(corrupt).Status == "countdown-crc-mismatch", "corrupt payload never activates models");
        File.WriteAllBytes(corrupt, data[..^1]); check(!CommanderResources.Probe(corrupt).Ready, "partial download rejected");
        File.WriteAllText(corrupt, "not a vpk content"); check(!CommanderResources.Probe(corrupt).Ready, "invalid magic rejected without crashing plugin");
    }
}
