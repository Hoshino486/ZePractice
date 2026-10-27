namespace CounterStrikeSharp.API.Core
{
    public sealed class TestSteamId { public ulong SteamId64 { get; set; } }
    public sealed class CCSPlayerController
    {
        public bool IsValid { get; set; } = true;
        public bool IsBot { get; set; }
        public bool IsHLTV { get; set; }
        public ulong SteamID { get; set; }
        public TestSteamId? AuthorizedSteamID { get; set; }
        public string? IpAddress { get; set; } = "192.0.2.20:27005";
        public bool CssRoot { get; set; }
        public uint CssImmunity { get; set; }
    }
}
namespace CounterStrikeSharp.API.Modules.Admin
{
    public static class AdminManager
    {
        public static bool PlayerHasPermissions(CounterStrikeSharp.API.Core.CCSPlayerController? p, params string[] flags) => p?.CssRoot ?? true;
        public static uint GetPlayerImmunity(CounterStrikeSharp.API.Core.CCSPlayerController? p) => p?.CssImmunity ?? 0;
    }
}
namespace ZePractice
{
    public sealed class TestConfig
    {
        public bool AllowEveryonePracticeCommands { get; set; } = true;
        public bool TrustLoopbackAdmin { get; set; }
        public List<string> OwnerSteamIds { get; set; } = new();
    }
    public sealed class TestLogger { public List<string> Messages { get; } = new(); }
    public sealed class ZePracticePlugin
    {
        public string ModuleDirectory { get; set; } = "";
        public TestConfig Config { get; } = new();
        public TestLogger Logger { get; } = new();
        public static bool IsRealPlayer(CounterStrikeSharp.API.Core.CCSPlayerController? p) => p is { IsValid:true, IsBot:false, IsHLTV:false };
    }
}
namespace Microsoft.Extensions.Logging
{
    public static class LoggingExtensions
    {
        public static void LogWarning(this ZePractice.TestLogger logger, string text, params object?[] values) => logger.Messages.Add(text);
        public static void LogInformation(this ZePractice.TestLogger logger, string text, params object?[] values) => logger.Messages.Add(text);
    }
}