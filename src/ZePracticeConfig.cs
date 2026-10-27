using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;
namespace ZePractice;
public sealed class ZePracticeConfig : BasePluginConfig
{
    public override int Version { get; set; } = 1;
    public string MenuUiMode { get; set; } = "chat";
    public bool PreventPracticeIntermission { get; set; } = true;
    public bool MenuAssetsReady { get; set; } = false;
    public bool PanoramaAllowRemoteClients { get; set; } = false;
    public string FysRoot { get; set; } = "Fys";
    public string ServerNumber { get; set; } = "2001";
    public bool PracticeMode { get; set; } = true;
    public bool AllowEveryonePracticeCommands { get; set; } = true;
    public bool TrustLoopbackAdmin { get; set; } = true;
    public List<string> OwnerSteamIds { get; set; } = new();
    public Dictionary<string,string> MapAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool BossHud { get; set; } = true;
    public bool MapTranslations { get; set; } = true;
    public bool TranslationCountdown { get; set; } = true;
    public float HudIntervalSeconds { get; set; } = 0.5f;
    public bool AutoRespawn { get; set; } = true;
    public float RespawnDelaySeconds { get; set; } = 1.0f;
    public int DefaultHealth { get; set; } = 100;
    public string DefaultPrimaryWeapon { get; set; } = "weapon_bizon";
    public bool ApplyFysMapParameters { get; set; } = true;
    public bool EnableZombieMode { get; set; } = false;
    public bool UnlimitedAmmo { get; set; } = true;
    public bool NoBlock { get; set; } = true;
    public bool LogUnmappedSettings { get; set; } = true;
    public bool EnableSpecialGrenades { get; set; } = true;
    public bool EnableItemWatch { get; set; } = true;
    public string ItemWatchDirectory { get; set; } = "";
    public PublicGameplayOptions PublicGameplay { get; set; } = new();
    public MapVoteOptions MapVote { get; set; } = new();
    public MapMarkerOptions MapMarkers { get; set; } = new();
    public WorkshopUpdateOptions WorkshopUpdates { get; set; } = new();
    public CommanderOptions Commander { get; set; } = new();
    public ServerModelOptions ServerModels { get; set; } = new();
    public PracticeBotOptions PracticeBots { get; set; } = new();
}

public sealed class PracticeBotOptions
{
    public int MaximumHealth { get; set; } = 1000000;
    public void Validate()
    {
        if (MaximumHealth is < 1 or > 10000000) throw new InvalidDataException("PracticeBots.MaximumHealth must be 1..10000000.");
    }
}

public sealed class CommanderOptions
{
    // Enable after the matching Workshop bundle has been published and mounted.
    public bool Enabled { get; set; } = false;
}
public sealed class ServerModelOptions
{
    public bool Enabled { get; set; } = false;
    public string PlayerModel { get; set; } = "agents/fys/luna/luna.vmdl";
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PlayerModel) || !PlayerModel.EndsWith(".vmdl", StringComparison.Ordinal) ||
            PlayerModel.Contains("..") || PlayerModel.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '-' or '.')))
            throw new InvalidDataException("ServerModels.PlayerModel must be a relative resource path.");
    }
}

public sealed class WorkshopUpdateOptions
{
    public bool Enabled { get; set; } = true;
    public int CheckIntervalSeconds { get; set; } = 60;
    public int RetrySeconds { get; set; } = 60;
    public int DownloadTimeoutSeconds { get; set; } = 1800;
    public int MaximumConcurrentDownloads { get; set; } = 2;
    public void Validate()
    {
        if (CheckIntervalSeconds is < 30 or > 86400 || RetrySeconds is < 30 or > 3600 ||
            DownloadTimeoutSeconds is < 60 or > 86400 || MaximumConcurrentDownloads is < 1 or > 4)
            throw new InvalidDataException("WorkshopUpdates contains an invalid limit.");
    }
}

public sealed class MapVoteOptions
{
    public int MaximumCandidates { get; set; } = 100;
    public float NominationCooldownSeconds { get; set; } = 3;
    public float ChangeDelaySeconds { get; set; } = 5;
    public float LoadTimeoutSeconds { get; set; } = 180;
    public void Validate()
    {
        if (MaximumCandidates is < 1 or > 1000 || !float.IsFinite(NominationCooldownSeconds) || NominationCooldownSeconds is < 0 or > 60 ||
            !float.IsFinite(ChangeDelaySeconds) || ChangeDelaySeconds is < 1 or > 60 ||
            !float.IsFinite(LoadTimeoutSeconds) || LoadTimeoutSeconds is < 30 or > 1800)
            throw new InvalidDataException("MapVote contains an invalid limit.");
    }
}

public sealed class MapMarkerOptions
{
    public bool ButtonChickens { get; set; } = true;
    public bool MapWeaponGlow { get; set; } = true;
    public int GlowRange { get; set; } = 3000;
    public int MaximumButtonMarkers { get; set; } = 1024;
    public void Validate()
    {
        if (GlowRange is < 1 or > 100000 || MaximumButtonMarkers is < 1 or > 4096)
            throw new InvalidDataException("MapMarkers contains an invalid limit.");
    }
}

public sealed class PublicGameplayOptions
{
    public int ZombieHealth { get; set; } = 35000;
    public int ZombieArmor { get; set; } = 2500;
    public float RegenerationDelaySeconds { get; set; } = 5;
    public int HealthRegenerationPerSecond { get; set; } = 1750;
    public int ArmorRegenerationPerSecond { get; set; } = 125;
    public int HumanHealthLimit { get; set; } = 35000;
    public int HumanArmorLimit { get; set; } = 2500;
    public int Money { get; set; } = 99999;
    public int ReserveAmmo { get; set; } = 99;
    public float DroppedGunLifetimeSeconds { get; set; } = 60;
    public int MaximumRoundMinutes { get; set; } = 180;
    public bool DefaultInfiniteMagazine { get; set; } = false;
    public bool DefaultBunnyHop { get; set; } = false;
    public bool AllowZombieMapKnives { get; set; } = true;
    public Dictionary<string, string[]> ZombieKnifeItems { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ze_forhidden_slicoo_cn"] = ["life_tap_weapon", "Circle_of_Winter_weapon", "grzzly_weapon", "item_flycutter_weapon"]
    };
    public Dictionary<string, ArtifactResetRule[]> ArtifactResetRules { get; set; } = ArtifactResetRule.Defaults();

    public void Validate()
    {
        if (ArtifactResetRules == null || ArtifactResetRules.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null))
            throw new InvalidDataException("PublicGameplay.ArtifactResetRules requires map keys and rule arrays.");
        foreach (var rule in ArtifactResetRules.Values.SelectMany(rules => rules))
        {
            if (rule == null) throw new InvalidDataException("Artifact reset rule must not be null.");
            rule.Validate();
        }
        if (ZombieKnifeItems == null || ZombieKnifeItems.Any(pair => string.IsNullOrWhiteSpace(pair.Key) ||
            pair.Value == null || pair.Value.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidDataException("PublicGameplay.ZombieKnifeItems requires map keys and exact entity names.");
        if (ZombieHealth is < 1 or > 1000000 || ZombieArmor is < 0 or > 100000 ||
            HumanHealthLimit is < 1 or > 1000000 || HumanArmorLimit is < 0 or > 100000 ||
            Money is < 0 or > 1000000 || ReserveAmmo is < 1 or > 999 ||
            !float.IsFinite(RegenerationDelaySeconds) || RegenerationDelaySeconds is < 0 or > 300 ||
            HealthRegenerationPerSecond is < 1 or > 1000000 || ArmorRegenerationPerSecond is < 1 or > 100000 ||
            !float.IsFinite(DroppedGunLifetimeSeconds) || DroppedGunLifetimeSeconds is < 1 or > 3600 ||
            MaximumRoundMinutes is < 1 or > 1440)
            throw new InvalidDataException("PublicGameplay contains an invalid server limit.");
    }
}
