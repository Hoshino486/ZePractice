using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZePractice;

public sealed record PracticeMapEntry(string Name, string DisplayName, string? WorkshopId, string LocalFile, bool IsFysMap)
{
    public string FileLabel => Name + (Path.GetExtension(LocalFile).Equals(".vmap_c", StringComparison.OrdinalIgnoreCase) ? ".vmap_c" : ".vpk");
}

/// <summary>Lists only maps present on this server; the FYS list is metadata, not downloadable map content.</summary>
public static class AdminPracticeMapCatalog
{
    private static readonly Regex MapNamePattern = new(@"^[A-Za-z0-9_][A-Za-z0-9_.\-/]{0,180}$", RegexOptions.CultureInvariant);
    public static bool IsValidMapName(string value) => MapNamePattern.IsMatch(value) && !value.Contains("..", StringComparison.Ordinal) && !value.Contains("//", StringComparison.Ordinal);
    public static bool IsWorkshopId(string value) => !string.IsNullOrEmpty(value) && value.All(c => c is >= '0' and <= '9') && ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0;
    public static bool IsPlayableMapFileName(string name) => IsValidMapName(name) && !name.Contains('/') &&
        !name.EndsWith("_skybox", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("_vanity", StringComparison.OrdinalIgnoreCase) &&
        !name.StartsWith("workshop_preview_", StringComparison.OrdinalIgnoreCase) &&
        name is not ("graphics_settings" or "lobby_mapveto") && !Regex.IsMatch(name, @"_(?:dir|\d{3})$");

    public static string LoadCommand(PracticeMapEntry map)
    {
        if (!IsValidMapName(map.Name) || (map.WorkshopId != null && !IsWorkshopId(map.WorkshopId)))
            throw new ArgumentException("Invalid installed map identity");
        if (map.WorkshopId == null) return "map " + map.Name + " nomapvalidation=true";
        // Use the engine's Workshop downloader/registration path, then pin the
        // exact member name. Plain map+customgamemode can unload the current map
        // before an unregistered cached addon is mountable (CLIENT_NO_MAP).
        if (map.LocalFile.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase))
            return "mm_host_workshop_map " + map.WorkshopId + " " + map.Name;
        return "host_workshop_map " + map.WorkshopId;
    }

    public static IReadOnlyList<PracticeMapEntry> Load(string serverCsgoDirectory, string fysMapsPath, IEnumerable<string> activeAddons, string? maplistPath = null)
    {
        var metadata = ReadMetadata(fysMapsPath);
        var configuredWorkshopIds = ReadLocalWorkshopIds(maplistPath);
        var maps = new Dictionary<string, PracticeMapEntry>(StringComparer.OrdinalIgnoreCase);
        Scan(Path.Combine(serverCsgoDirectory, "maps"), metadata, configuredWorkshopIds, maps);
        var gameDirectory = Directory.GetParent(serverCsgoDirectory)?.FullName;
        if (gameDirectory != null)
        {
            foreach (var addon in activeAddons.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Regex.IsMatch(addon, @"^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)) continue;
                Scan(Path.Combine(gameDirectory, "csgo_addons", addon, "maps"), metadata, configuredWorkshopIds, maps);
            }
            // Workshop downloads remain in numeric cache directories and are not
            // necessarily copied into csgo/maps. Read their real maps/<name>.vpk entries.
            foreach (var entry in InstalledWorkshopMapIndex.Read(gameDirectory))
            {
                bool explicitChoice = configuredWorkshopIds.TryGetValue(entry.Name, out string? selected) && selected == entry.WorkshopId;
                if (maps.ContainsKey(entry.Name) && !explicitChoice) continue;
                bool found = metadata.TryGetValue(entry.Name, out var display);
                maps[entry.Name] = new(entry.Name, found ? display! : entry.Name, entry.WorkshopId, entry.Package, found);
            }
        }
        return maps.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Dictionary<string, string> ReadMetadata(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return result;
        using var json = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (json.RootElement.ValueKind != JsonValueKind.Object) return result;
        foreach (var entry in json.RootElement.EnumerateObject())
        {
            if (!IsValidMapName(entry.Name) || entry.Value.ValueKind != JsonValueKind.Object) continue;
            string display = entry.Name;
            foreach (var field in entry.Value.EnumerateObject())
            {
                var key = field.Name.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
                if (key is "displayname" or "chinesename" or "name")
                {
                    if (field.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(field.Value.GetString())) display = field.Value.GetString()!;
                }
            }
            // Upstream IDs may refer to another revision or alias. They are never a local hosting instruction.
            result[entry.Name] = display;
        }
        return result;
    }

    private static Dictionary<string, string> ReadLocalWorkshopIds(string? path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return result;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("Maps", out var maps) || maps.ValueKind != JsonValueKind.Object) return result;
            foreach (var entry in maps.EnumerateObject())
            {
                if (!IsValidMapName(entry.Name) || entry.Value.ValueKind != JsonValueKind.Object || !entry.Value.TryGetProperty("workshop_id", out var id)) continue;
                // Permit only a positive uint64 JSON integer or an ASCII digit string. No floats, signs, or whitespace.
                string? candidate = id.ValueKind switch
                {
                    JsonValueKind.Number => id.GetRawText(),
                    JsonValueKind.String => id.GetString(),
                    _ => null
                };
                if (candidate != null && IsWorkshopId(candidate)) result[entry.Name] = ulong.Parse(candidate, NumberStyles.None, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            }
        }
        // An optional bad/unreadable maplist must not hide otherwise installed maps.
        catch (JsonException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return result;
    }

    private static void Scan(string root, Dictionary<string, string> metadata, Dictionary<string, string> configuredWorkshopIds, Dictionary<string, PracticeMapEntry> maps)
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            var ext = Path.GetExtension(file);
            if (!ext.Equals(".vpk", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".vmap_c", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileNameWithoutExtension(file);
            if (!IsPlayableMapFileName(name)) continue;
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Split('/')[0].ToLowerInvariant() is "prefabs" or "ui" or "templates" or "editor") continue;
            string? workshop = null;
            if (configuredWorkshopIds.TryGetValue(name, out var configured)) workshop = configured;
            else if (relative.StartsWith("workshop/", StringComparison.OrdinalIgnoreCase))
            {
                var segments = relative.Split('/');
                if (segments.Length >= 3 && IsWorkshopId(segments[1])) workshop = segments[1];
            }
            var found = metadata.TryGetValue(name, out var display);
            // Entries still originate exclusively from installed files, never from the configured/remote map list.
            maps[name] = new PracticeMapEntry(name, found ? display! : name, workshop, file, found);
        }
    }
}
public sealed record PracticePunishment(string SteamId, string Kind, string Reason, string Administrator, DateTimeOffset CreatedUtc, DateTimeOffset? ExpiresUtc)
{
    public bool IsActive(DateTimeOffset now) => ExpiresUtc == null || ExpiresUtc > now;
}

/// <summary>Plugin-local bans/mutes/gags. Does not overwrite CS2Fixes or CSS administrator data.</summary>
public sealed class AdminPracticePunishmentStore
{
    private readonly string _path;
    private List<PracticePunishment> _records = new();
    public AdminPracticePunishmentStore(string path) { _path = path; }
    public void Load()
    {
        if (!File.Exists(_path)) return;
        var records = JsonSerializer.Deserialize<List<PracticePunishment>>(File.ReadAllText(_path)) ?? new();
        _records = records.Where(x => IsSteamId(x.SteamId) && x.Kind is "ban" or "mute" or "gag").ToList();
    }
    public static bool IsSteamId(string value) => value.Length == 17 && ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 76561197960265728UL;
    public PracticePunishment? Find(string steamId, string kind, DateTimeOffset now) => _records.LastOrDefault(x => x.SteamId == steamId && x.Kind == kind && x.IsActive(now));
    public IReadOnlyList<PracticePunishment> Active(DateTimeOffset now) => _records.Where(x => x.IsActive(now)).ToArray();
    public void Put(PracticePunishment record)
    {
        if (!IsSteamId(record.SteamId) || record.Kind is not ("ban" or "mute" or "gag")) throw new ArgumentException("Invalid punishment");
        var next = _records.Where(x => !(x.SteamId == record.SteamId && x.Kind == record.Kind) && x.IsActive(DateTimeOffset.UtcNow)).ToList();
        next.Add(record);
        Save(next);
        _records = next;
    }
    public bool Remove(string steamId, string kind)
    {
        var next = _records.Where(x => x.SteamId != steamId || x.Kind != kind).ToList();
        if (next.Count == _records.Count) return false;
        Save(next);
        _records = next;
        return true;
    }
    private void Save(List<PracticePunishment> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _path, overwrite: true);
    }
}
