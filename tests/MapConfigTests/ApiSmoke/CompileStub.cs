using CounterStrikeSharp.API.Core;
namespace ZePractice;
public sealed class ZePracticePlugin : BasePlugin
{
 public override string ModuleName => "compile-only";
 public override string ModuleVersion => "0";
 public ZePracticeConfig Config { get; } = new();
 public string FysDirectory { get; set; } = "";
 public string CurrentMapKey { get; set; } = "";
 public bool IsPractice { get; set; }
}