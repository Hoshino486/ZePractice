using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;

namespace CounterStrikeSharp.API.Core
{
    public class BasePluginConfig { public virtual int Version { get; set; } }
    public enum PlayerConnectedState { Connected, Connecting, Disconnected }
    public enum HookMode { Pre, Post }
    public enum HookResult { Continue, Handled, Stop }
    public sealed record EntityHandle(uint Raw);
    public sealed class CCSPlayerController
    {
        public bool IsValid { get; set; } = true;
        public bool IsBot { get; set; }
        public bool IsHLTV { get; set; }
        public bool Owner { get; set; }
        public int Slot { get; set; }
        public int TeamNum { get; set; } = 3;
        public PlayerConnectedState Connected { get; set; }
        public EntityHandle EntityHandle { get; set; } = new(1);
    }
    public static class Listeners
    {
        public delegate void OnTick();
        public delegate void OnClientDisconnect(int slot);
    }
}
namespace CounterStrikeSharp.API.Modules.Commands
{
    public sealed class CommandInfo(params string[] arguments)
    {
        public delegate void CommandCallback(CCSPlayerController? player, CommandInfo command);
        public int ArgCount => arguments.Length;
        public string GetArg(int index) => arguments[index];
    }
}
namespace CounterStrikeSharp.API.Modules.Menu
{
    public sealed record ChatMenuOption(string Text, Action<CCSPlayerController, ChatMenuOption> Callback, bool Disabled);
    public sealed class ChatMenu(string title)
    {
        public string Title => title;
        public bool ExitButton { get; set; }
        public List<ChatMenuOption> Options { get; } = new();
        public void AddMenuOption(string title, Action<CCSPlayerController, ChatMenuOption> action, bool disabled = false) => Options.Add(new(title, action, disabled));
    }
}
namespace CounterStrikeSharp.API.Modules.Timers
{
    public enum TimerFlags { STOP_ON_MAPCHANGE }
}
namespace CounterStrikeSharp.API.Modules.Cvars
{
    public sealed class ConVar(string name)
    {
        public string StringValue => ZePractice.ZePracticePlugin.Cvars[name];
        public static ConVar? Find(string name) => ZePractice.ZePracticePlugin.Cvars.ContainsKey(name) ? new(name) : null;
    }
}
namespace CounterStrikeSharp.API
{
    public static class Server
    {
        public static float CurrentTime { get; set; }
        public static string MapName { get; set; } = "ze_test_a";
        public static readonly List<string> Commands = new(), Messages = new();
        public static readonly Queue<Action> Frames = new();
        public static void ExecuteCommand(string command) => Commands.Add(command);
        public static void PrintToChatAll(string message) => Messages.Add(message);
        public static void NextFrame(Action action) => Frames.Enqueue(action);
        public static void Flush() { while (Frames.TryDequeue(out var action)) action(); }
    }
    public static class Utilities
    {
        public static readonly List<CCSPlayerController> Players = new();
        public static List<CCSPlayerController> GetPlayers() => Players;
        public static CCSPlayerController? GetPlayerFromSlot(int slot) => Players.FirstOrDefault(player => player.Slot == slot);
    }
}
namespace Microsoft.Extensions.Logging
{
    public sealed class TestLogger
    {
        public readonly List<Exception> Errors = new();
        public void LogError(Exception error, string message, params object[] arguments) => Errors.Add(error);
    }
}
namespace ZePractice
{
    public sealed class PermissionStub
    { public bool IsServerOwner(CCSPlayerController? player) => player == null || player.Owner; }
    public sealed class ZePracticePlugin
    {
        public string ModuleDirectory { get; set; } = "";
        public ZePracticeConfig Config { get; } = new();
        public PermissionStub Permissions { get; } = new();
        public Microsoft.Extensions.Logging.TestLogger Logger { get; } = new();
        public readonly Dictionary<string, CommandInfo.CommandCallback> Commands = new();
        public readonly Dictionary<string, Func<CCSPlayerController?, CommandInfo, HookResult>> Chats = new();
        public readonly Dictionary<Type, Delegate> Listeners = new();
        public readonly List<(float At, Action Action)> Timers = new();
        public readonly Dictionary<int, ChatMenu> Menus = new();
        public readonly List<string> Replies = new();
        public static readonly Dictionary<string, string> Cvars = new();
        public static bool IsRealPlayer(CCSPlayerController? player) => player is { IsValid: true, IsBot: false, IsHLTV: false };
        public static bool SetCvar(string name, string value) { Cvars[name] = value; return true; }
        public void AddZeCommand(string name, string description, CommandInfo.CommandCallback action) => Commands.Add(name, action);
        public void RemoveZeCommand(string name, CommandInfo.CommandCallback action) => Commands.Remove(name);
        public void AddCommandListener(string name, Func<CCSPlayerController?, CommandInfo, HookResult> action, HookMode mode) => Chats[name] = action;
        public void RemoveCommandListener(string name, Func<CCSPlayerController?, CommandInfo, HookResult> action, HookMode mode) => Chats.Remove(name);
        public void RegisterListener<T>(T action) where T : Delegate => Listeners[typeof(T)] = action;
        public void RemoveListener<T>(T action) where T : Delegate => Listeners.Remove(typeof(T));
        public void AddTimer(float seconds, Action action, CounterStrikeSharp.API.Modules.Timers.TimerFlags flags) => Timers.Add((CounterStrikeSharp.API.Server.CurrentTime + seconds, action));
        public void OpenMenu(CCSPlayerController player, ChatMenu menu) => Menus[player.Slot] = menu;
        public void Reply(CCSPlayerController? player, string message) => Replies.Add(message);
    }
}
