namespace CounterStrikeSharp.API.Core { public class BasePluginConfig { public virtual int Version { get; set; } } }
namespace CounterStrikeSharp.API.Modules.Cvars
{
    public sealed class ConVar
    {
        public static readonly Dictionary<string, ConVar> Vars = new(StringComparer.OrdinalIgnoreCase);
        public static readonly List<(string Name, string Value)> Writes = new();
        public static string? ClampName;
        public string Name { get; }
        private string _value;
        public ConVar(string name, string value) { Name=name; _value=value; }
        public string StringValue { get => _value; set { _value = ClampName == Name ? "1" : value; Writes.Add((Name,value)); } }
        public static ConVar? Find(string name) => Vars.GetValueOrDefault(name);
    }
}
namespace ZePractice
{
    public sealed class TestLogger { public readonly List<string> Messages = new(); }
    public sealed class ZePracticePlugin
    {
        public ZePracticeConfig Config { get; } = new();
        public string FysDirectory { get; set; } = "";
        public string CurrentMapKey { get; set; } = "ze_test";
        public bool IsPractice { get; set; } = true;
        public TestLogger Logger { get; } = new();
    }
}
namespace Microsoft.Extensions.Logging
{
    public static class TestLoggingExtensions
    {
        public static void LogInformation(this ZePractice.TestLogger logger, string message, params object?[] args) => logger.Messages.Add(message);
        public static void LogWarning(this ZePractice.TestLogger logger, string message, params object?[] args) => logger.Messages.Add(message);
    }
}