namespace CounterStrikeSharp.API.Core
{
    public sealed class EntityHandle(uint raw) { public uint Raw { get; } = raw; }
    public sealed class PawnHandle(CCSPlayerPawn? value)
    {
        public CCSPlayerPawn? Value { get; set; } = value;
        public uint Raw => Value?.EntityHandle.Raw ?? 0;
    }
    public class CBaseEntity(uint raw)
    {
        public bool IsValid { get; set; } = true;
        public EntityHandle EntityHandle { get; } = new(raw);
        public readonly List<(string Input, string Value)> Inputs = new();
        public bool FailNextInput;
        public void AcceptInput(string input, CBaseEntity activator, CBaseEntity caller, string value)
        {
            if (FailNextInput) { FailNextInput = false; throw new InvalidOperationException("test input failure"); }
            Inputs.Add((input, value));
        }
    }
    public sealed class CCSPlayerPawn(uint raw) : CBaseEntity(raw);
    public sealed class SteamID(ulong identity) { public ulong SteamId64 { get; } = identity; }
    public sealed class CCSPlayerController(int slot) : CBaseEntity((uint)slot)
    {
        public int Slot { get; } = slot;
        public int? UserId { get; set; } = slot;
        public bool IsBot, IsHLTV, Resetting;
        public ulong SteamID { get; set; }
        public SteamID? AuthorizedSteamID { get; set; }
        public string PlayerName { get; set; } = "test";
        public PawnHandle PlayerPawn { get; set; } = new(new((uint)(100 + slot)));
    }
    public static class Listeners
    {
        public delegate void OnTick();
        public delegate void OnClientDisconnect(int slot);
    }
    public sealed class EventPlayerConnect(bool force)
    {
        public bool Forced { get; } = force;
        public static readonly List<EventPlayerConnect> Fired = new();
        public CCSPlayerController? Userid { get; set; }
        public string Name { get; set; } = "";
        public bool Bot { get; set; }
        public bool ServerOnly;
        public readonly Dictionary<string, string> Values = new();
        public void Set(string key, string value) => Values[key] = value;
        public void FireEvent(bool serverOnly) { ServerOnly = serverOnly; Fired.Add(this); }
    }
}
namespace CounterStrikeSharp.API.Modules.Cvars
{
    public sealed class ConVar(string value)
    {
        public string StringValue { get; set; } = value;
        public static readonly Dictionary<string, ConVar> Values = new();
        public T GetPrimitiveValue<T>() => (T)(object)(StringValue is "1" or "true");
        public static ConVar? Find(string name) => Values.GetValueOrDefault(name);
    }
}
namespace CounterStrikeSharp.API
{
    public static class Server { public static float CurrentTime; }
    public static class Utilities
    {
        public static readonly List<Core.CCSPlayerController> Players = new();
        public static List<Core.CCSPlayerController> GetPlayers() => Players;
    }
}
namespace Microsoft.Extensions.Logging
{
    public static class Extensions
    {
        public static void LogInformation(this object logger, string message, params object[] values) { }
        public static void LogError(this object logger, Exception error, string message) { }
    }
}
namespace ZePractice
{
    using CounterStrikeSharp.API.Core;
    public sealed class ZePracticePlugin
    {
        public object Logger { get; } = new();
        public readonly Dictionary<Type, Delegate> Listeners = new();
        public static bool IsRealPlayer(CCSPlayerController? player) => player is { IsValid: true, IsBot: false, IsHLTV: false };
        public bool IsTeamResetPending(CCSPlayerController player) => player.Resetting;
        public void RegisterListener<T>(T callback) where T : Delegate => Listeners.Add(typeof(T), callback);
        public void RemoveListener<T>(T callback) where T : Delegate => Listeners.Remove(typeof(T));
        public void Tick() { if (Listeners.TryGetValue(typeof(CounterStrikeSharp.API.Core.Listeners.OnTick), out var callback)) callback.DynamicInvoke(); }
    }
}
