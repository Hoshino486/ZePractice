using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
namespace ZePractice;
// Compiles the actual native adapter against CSS v1.0.376. Tests never call native engine APIs.
public sealed class ZePracticePlugin : BasePlugin
{
    public override string ModuleName => "PurePanelTests";
    public override string ModuleVersion => "1.0.0";
    public bool IsPractice => true;
    public TestConfig Config { get; } = new();
    public TestPermissions Permissions { get; } = new();
    public static bool IsRealPlayer(CCSPlayerController? player) => player is { IsValid: true, IsBot: false };
    public void AddZeCommand(string name, string description, CommandInfo.CommandCallback callback) { }
    public void RemoveZeCommand(string name, CommandInfo.CommandCallback callback) { }
    public void Reply(CCSPlayerController? player, string text) { }
    public void ExecuteOwnCommand(CCSPlayerController player, string commandLine) { }
    public void UseChatMenus(CCSPlayerController player) { }
    public bool RestoreRadioMenus(CCSPlayerController player) => false;
    public IReadOnlyDictionary<string, bool> GetPracticeToggles(CCSPlayerController player) => new Dictionary<string, bool>();
}
public sealed class TestConfig { public string MenuUiMode { get; set; } = "panorama"; public bool MenuAssetsReady { get; set; } public bool PanoramaAllowRemoteClients { get; set; } }
public sealed class TestPermissions { public bool Allows(CCSPlayerController p, string command) => true; public bool HasAdminAccess(CCSPlayerController p) => false; public string Describe(CCSPlayerController p) => "测试"; }
