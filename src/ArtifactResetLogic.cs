namespace ZePractice;

public sealed class TeamResetSequence
{
    public int Revision { get; private set; }
    public int TargetTeam { get; private set; }
    public bool Pending { get; private set; }
    public int Begin(int targetTeam)
    {
        if (targetTeam is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(targetTeam));
        TargetTeam = targetTeam; Pending = true;
        return ++Revision;
    }
    public bool IsCurrent(int revision, int team) => Pending && Revision == revision && TargetTeam == team;
    public bool Complete(int revision)
    {
        if (!Pending || revision != Revision) return false;
        Pending = false; return true;
    }
    public void Cancel() { Revision++; Pending = false; }
}

public sealed class ArtifactResetRule
{
    public string WeaponName { get; set; } = "";
    public string[] RemoveEntities { get; set; } = [];
    public string[] DeactivateEntities { get; set; } = [];
    public static bool TrySuffix(string name, string baseName, out string suffix)
    {
        suffix = "";
        if (name == baseName) return true;
        if (!name.StartsWith(baseName + "&", StringComparison.Ordinal)) return false;
        string digits = name[(baseName.Length + 1)..];
        if (digits.Length == 0 || digits.Any(character => character is < '0' or > '9')) return false;
        suffix = "&" + digits; return true;
    }
    public static Dictionary<string, ArtifactResetRule[]> Defaults() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["ze_forhidden_slicoo_cn"] =
        [
            new() { WeaponName = "elves_weapon", RemoveEntities = ["elves_model", "elves_mmfix", "elves_mm"], DeactivateEntities = ["elves_ui"] },
            new() { WeaponName = "luoli_weapon", RemoveEntities = ["luoli_model", "luoli_mmfix", "luoli_mm", "luoli_mmeye", "luoli_mmeye_target"], DeactivateEntities = ["luoli_ui"] },
            new() { WeaponName = "fashi_weapon", RemoveEntities = ["fashi_model", "fashi_mmfix", "fashi_mm"], DeactivateEntities = ["fashi_ui"] },
            new() { WeaponName = "laozhe_weapon", RemoveEntities = ["laozhe_model", "laozhe_mmfix", "laozhe_mm"], DeactivateEntities = ["laozhe_ui"] },
            new() { WeaponName = "heiyan_weapon", RemoveEntities = ["heiyan_model", "heiyan_mmfix", "heiyan_mm"], DeactivateEntities = ["heiyan_ui"] },
            new() { WeaponName = "grzzly_weapon", RemoveEntities = ["grzzly_model", "grzzly_mmfix", "grzzly_mm"], DeactivateEntities = ["grzzly_ui"] },
            new() { WeaponName = "item_flycutter_weapon", RemoveEntities = ["item_flycutter_measure"], DeactivateEntities = ["item_flycutter_ui"] },
            new() { WeaponName = "devilian_weapon", RemoveEntities = ["devilian_button"] }
        ]
    };
    public void Validate()
    {
        static bool Exact(string? name) => !string.IsNullOrWhiteSpace(name) && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
        if (!Exact(WeaponName) || RemoveEntities == null || DeactivateEntities == null ||
            RemoveEntities.Concat(DeactivateEntities).Any(name => !Exact(name)))
            throw new InvalidDataException("Artifact reset rules require exact map entity names, without wildcards.");
    }
}

public sealed class ArtifactOwnership
{
    private readonly Dictionary<string, uint> _owners = new(StringComparer.Ordinal);
    public int Count => _owners.Count;
    public void Pickup(string weaponName, uint pawn) => _owners[weaponName] = pawn;
    public string[] Release(uint pawn)
    {
        string[] names = _owners.Where(pair => pair.Value == pawn).Select(pair => pair.Key).ToArray();
        foreach (string name in names) _owners.Remove(name);
        return names;
    }
    public void Clear() => _owners.Clear();
}
