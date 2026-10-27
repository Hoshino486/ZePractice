using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
namespace ZePractice;
public static class ZeGameplayCatalog { public static readonly Dictionary<string,string> WeaponAliases = new(); }
public sealed class ZePracticePlugin : BasePlugin
{
    public override string ModuleName=>"compile only";
    public override string ModuleVersion=>"0";
    public ZePracticeConfig Config {get;}=new();
    public ZePermissionService Permissions {get;}=null!;
    public bool IsPractice=>true;
    public bool TeamResetPending(CCSPlayerController player)=>false;
    public string FysDirectory=>"";
    public static bool IsRealPlayer(CCSPlayerController? p)=>p is {IsValid:true,IsBot:false,IsHLTV:false};
    public bool CanAdmin(CCSPlayerController? p)=>false;
    public bool NeedPermission(CCSPlayerController? p,string op)=>false;
    public void AddZeCommand(string name,string desc,CommandInfo.CommandCallback callback) { }
    public void RemoveZeCommand(string name,CommandInfo.CommandCallback callback) { }
    public void ExecuteOwnCommand(CCSPlayerController p,string command) { }
    public void OpenMenu(CCSPlayerController p,ChatMenu menu) { }
    public void Reply(CCSPlayerController? p,string text) { }
    public void ShowStatus(CCSPlayerController? p) { }
    public void OpenMapVoteMenu(CCSPlayerController? p,string search="") { }
    public bool ForceMapChange(CCSPlayerController? p,string command)=>false;
    public void TogglePublicNoclip(CCSPlayerController? p) { }
    public bool PublicNoclipEnabled(CCSPlayerController p)=>false;
    public void RestartManualRound(CCSPlayerController? p) { }
    public void SetPracticeMode(bool practice) { }
    public void ReloadMapConfiguration() { }
}
