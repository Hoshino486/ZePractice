using CounterStrikeSharp.API.Core;
namespace ZePractice;
public sealed class ZePracticePlugin : BasePlugin
{
    public override string ModuleName => "Compile-only";
    public override string ModuleVersion => "0";
    public ZePracticeConfig Config { get; } = new();
    public static bool IsRealPlayer(CCSPlayerController? player) => player is { IsValid:true, IsBot:false, IsHLTV:false };
}