using System.Text;
using ZePractice;

internal static class InstalledWorkshopTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        string game = Path.Combine(root, "download-fixture", "game");
        string csgo = Path.Combine(game, "csgo"); Directory.CreateDirectory(csgo);
        string cache = Path.Combine(game, "bin", "win64", "steamapps", "workshop", "content", "730");
        string Write(string id, string[] names, bool external = false, string folder = "maps")
        {
            string dir = Path.Combine(cache, id); Directory.CreateDirectory(dir);
            using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer, Encoding.UTF8, true);
            void Text(string text) { writer.Write(Encoding.UTF8.GetBytes(text)); writer.Write((byte)0); }
            Text("vpk"); Text(folder);
            for (int i=0; i<names.Length; i++)
            {
                Text(names[i]); writer.Write(0u); writer.Write((ushort)0); writer.Write((ushort)(external ? 0 : 32767));
                writer.Write((uint)(i*4)); writer.Write(4u); writer.Write(ushort.MaxValue);
            }
            Text(""); Text(""); Text(""); writer.Flush();
            string path = Path.Combine(dir, id + "_dir.vpk");
            using (var output = new BinaryWriter(File.Create(path)))
            {
                output.Write(0x55AA1234u); output.Write(2u); output.Write((uint)buffer.Length);
                output.Write((uint)(external ? 0 : names.Length * 4)); output.Write(0u); output.Write(0u); output.Write(0u);
                output.Write(buffer.ToArray()); if (!external) output.Write(new byte[names.Length * 4]);
            }
            if (external) File.WriteAllBytes(Path.Combine(dir, id + "_000.vpk"), new byte[names.Length * 4]);
            return path;
        }
        var a = Write("123", ["ze_downloaded", "ze_downloaded_skybox"]);
        var b = Write("456", ["ze_split"], true);
        var resource = Write("789", ["resources_only"], false, "materials");
        var maps = AdminPracticeMapCatalog.Load(csgo, "", []);
        check(maps.Count == 2 && maps.Select(x=>x.Name).Order().SequenceEqual(new[]{"ze_downloaded","ze_split"}), "Workshop cache maps found without extracted csgo/maps files");
        check(maps.All(x=>x.FileLabel.EndsWith(".vpk") && !x.FileLabel.Contains(x.WorkshopId!)), "display uses actual VPK map name not numeric addon archive");
        check(maps.Single(x=>x.Name=="ze_downloaded").LocalFile==a && maps.Single(x=>x.Name=="ze_split").WorkshopId=="456", "actual package and hosting ID retained separately from label");
        check(AdminPracticeMapCatalog.LoadCommand(maps.Single(x=>x.Name=="ze_split"))=="mm_host_workshop_map 456 ze_split", "direct Workshop map command prepares official Workshop load and selects exact VPK name and addon");
        check(AdminPracticeMapCatalog.LoadCommand(new("de_nuke","title",null,"de_nuke.vpk",false))=="map de_nuke nomapvalidation=true", "direct stock map command needs no addon");
        check(AdminPracticeMapCatalog.LoadCommand(new("ze_local","title","123","ze_local.vpk",false))=="host_workshop_map 123", "explicit local placeholder mapping retains existing Workshop hosting flow");
        bool rejected=false;try{AdminPracticeMapCatalog.LoadCommand(new("ze_split","title","123;quit",a,false));}catch(ArgumentException){rejected=true;}
        check(rejected,"direct map command rejects malformed addon ID");
        check(InstalledWorkshopMapIndex.ReadMapNames(resource).Count==0, "resource-only addon is not a playable map");
        check(InstalledWorkshopMapIndex.ReadMapNames(a).SequenceEqual(["ze_downloaded"]), "skybox is not a selectable map");
        string chunk = Path.Combine(Path.GetDirectoryName(b)!, "456_000.vpk");
        File.WriteAllBytes(chunk, [1]); check(InstalledWorkshopMapIndex.ReadMapNames(b).Count==0, "partial external chunk rejected");
        File.Delete(chunk); check(InstalledWorkshopMapIndex.ReadMapNames(b).Count==0, "missing external chunk rejected");
        b = Write("456", ["ze_split"], true);
        check(InstalledWorkshopMapIndex.ReadMapNames(b).SequenceEqual(["ze_split"]), "completed download discovered on refresh");
        string bad = Write("800", ["../escape"]); check(InstalledWorkshopMapIndex.ReadMapNames(bad).Count==0, "unsafe map member rejected");
        bad = Write("801", ["ze_a","ze_a"]); check(InstalledWorkshopMapIndex.ReadMapNames(bad).Count==0, "duplicate VPK directory member rejected");
        bad = Write("802", ["ze_a"]); using(var stream=File.OpenWrite(bad)) stream.SetLength(stream.Length-1);
        check(InstalledWorkshopMapIndex.ReadMapNames(bad).Count==0, "incomplete VPK rejected");
        bad = Write("803", ["ze_a"]); File.WriteAllText(bad, "not a vpk");
        check(InstalledWorkshopMapIndex.ReadMapNames(bad).Count==0, "invalid VPK signature rejected");
        bad = Write("804", ["ze_a"]); var bytes=File.ReadAllBytes(bad); BitConverter.GetBytes(uint.MaxValue).CopyTo(bytes,8);File.WriteAllBytes(bad,bytes);
        check(InstalledWorkshopMapIndex.ReadMapNames(bad).Count==0, "unbounded VPK directory allocation rejected");
        string newer = Write("999", ["ze_downloaded"]);File.SetLastWriteTimeUtc(a,DateTime.UtcNow.AddHours(-2));File.SetLastWriteTimeUtc(newer,DateTime.UtcNow);
        maps=AdminPracticeMapCatalog.Load(csgo,"",[]);
        check(maps.Count(x=>x.Name=="ze_downloaded")==1 && maps.Single(x=>x.Name=="ze_downloaded").WorkshopId=="999", "duplicate map names use newest download when no explicit mapping exists");
        string mapping=Path.Combine(root,"download-maplist.json");File.WriteAllText(mapping,"{\"Maps\":{\"ze_downloaded\":{\"workshop_id\":\"123\"}}}");
        check(AdminPracticeMapCatalog.Load(csgo,"",[],mapping).Single(x=>x.Name=="ze_downloaded").WorkshopId=="123", "local explicit Workshop mapping wins over download recency");
        check(!AdminPracticeMapCatalog.IsPlayableMapFileName("ze_x_skybox")&&!AdminPracticeMapCatalog.IsPlayableMapFileName("de_nuke_vanity")&&
            !AdminPracticeMapCatalog.IsPlayableMapFileName("workshop_preview_dust2")&&!AdminPracticeMapCatalog.IsPlayableMapFileName("123_000"), "skyboxes vanity previews and archive chunks excluded");
        check(!AdminPracticeMapCatalog.IsPlayableMapFileName("graphics_settings") && !AdminPracticeMapCatalog.IsPlayableMapFileName("lobby_mapveto"), "UI utility worlds are not playable maps");
        foreach (string folder in new[] {"prefabs/misc", "ui", "templates", "editor"})
        {
            string directory = Path.Combine(csgo,"maps",folder); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"fixture_scene.vpk"),"not a selectable world");
        }
        check(AdminPracticeMapCatalog.Load(csgo,"",[]).All(x=>x.Name!="fixture_scene"), "UI prefab template and editor subdirectories excluded from installed maps");
    }
}
