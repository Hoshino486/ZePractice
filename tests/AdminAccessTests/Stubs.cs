using System.Numerics;
namespace CounterStrikeSharp.API.Modules.Entities
{
    public sealed class SteamID { public ulong SteamId64 { get; set; } public SteamID(ulong id) { SteamId64 = id; } }
}
namespace CounterStrikeSharp.API.Modules.Utils
{
    public enum CsTeam : byte { None=0, Spectator=1, Terrorist=2, CounterTerrorist=3 }
    [Flags] public enum VoiceFlags { Normal=0, Muted=1 }
}
namespace CounterStrikeSharp.API.Modules.Commands
{
    public sealed class CommandInfo
    {
        public delegate void CommandCallback(CounterStrikeSharp.API.Core.CCSPlayerController? player, CommandInfo info);
        private readonly string[] _args;
        public CommandInfo(params string[] args) { _args = args; }
        public int ArgCount => _args.Length;
        public string GetArg(int i) => _args[i];
    }
}
namespace CounterStrikeSharp.API.Modules.Timers
{
    [Flags] public enum TimerFlags { REPEAT=1, STOP_ON_MAPCHANGE=2 }
    public sealed class Timer
    {
        public Action Callback { get; } public TimerFlags Flags { get; } public bool Killed { get; private set; }
        public Timer(Action callback, TimerFlags flags) { Callback=callback;Flags=flags; }
        public void Kill() => Killed=true;
        public void Fire(bool force=false) { if(!Killed||force) Callback(); }
    }
}
namespace CounterStrikeSharp.API.Core
{
    public enum MoveType_t { MOVETYPE_NONE, MOVETYPE_WALK, MOVETYPE_NOCLIP, MOVETYPE_LADDER }
    public enum HookMode { Pre, Post } public enum HookResult { Continue, Handled, Stop }
    public sealed class GameEventInfo { }
    public sealed class EventPlayerDeath { public CCSPlayerController? Userid { get; set; } }
    public static class Listeners
    {
        public delegate void OnClientAuthorized(int slot, CounterStrikeSharp.API.Modules.Entities.SteamID steamId);
        public delegate void OnClientDisconnect(int slot);
        public delegate void OnMapEnd();
    }
    public sealed class TestVector { public float X {get;set;} public float Y {get;set;} public float Z {get;set;} }
    public sealed class CCSPlayerPawn
    {
        public bool IsValid {get;set;}=true; public int Health {get;set;}=100; public IntPtr Handle {get;set;}
        private bool _takesDamage=true;
        public int TakesDamageWrites {get;private set;}
        public bool TakesDamage { get=>_takesDamage;set {_takesDamage=value;TakesDamageWrites++;} }
        public MoveType_t MoveType {get;set;}=MoveType_t.MOVETYPE_WALK;
        public MoveType_t ActualMoveType {get;set;}=MoveType_t.MOVETYPE_WALK;
        public TestVector? AbsOrigin {get;set;}=new(); public TestVector EyeAngles {get;set;}=new();
        public int Teleports {get;private set;}
        public void Teleport(Vector3 pos,Vector3 angles,Vector3 velocity) { Teleports++;AbsOrigin=new(){X=pos.X,Y=pos.Y,Z=pos.Z}; }
    }
    public sealed class PawnReference { public CCSPlayerPawn? Value {get;set;} public uint Raw {get;set;} }
    public sealed class CCSPlayerController
    {
        public int Slot {get;set;} public int? UserId {get;set;} public bool IsValid {get;set;}=true;
        public bool IsBot {get;set;} public bool IsHLTV {get;set;} public ulong SteamID {get;set;}
        public CounterStrikeSharp.API.Modules.Entities.SteamID? AuthorizedSteamID {get;set;}
        public string? IpAddress {get;set;}="192.0.2.20:27005"; public bool CssRoot {get;set;} public uint CssImmunity {get;set;}
        public string PlayerName {get;set;}="Fixture player"; public byte TeamNum {get;set;}=3;
        public PawnReference PlayerPawn {get;set;}=new(); public bool PawnIsAlive {get;set;}=true;
        public CounterStrikeSharp.API.Modules.Utils.VoiceFlags VoiceFlags {get;set;}
        public int Respawns {get;private set;} public int Suicides {get;private set;} public int Disconnects {get;private set;}
        public int TeamChanges {get;private set;} public List<string> ClientCommands {get;}=new();
        public void ChangeTeam(CounterStrikeSharp.API.Modules.Utils.CsTeam team) { TeamChanges++;TeamNum=(byte)team; }
        public void Respawn() { Respawns++;PawnIsAlive=true;if(PlayerPawn.Value!=null)PlayerPawn.Value.Health=100; }
        public void CommitSuicide(bool a,bool b) { Suicides++;PawnIsAlive=false;if(PlayerPawn.Value!=null)PlayerPawn.Value.Health=0; }
        public void Disconnect(CounterStrikeSharp.API.ValveConstants.Protobuf.NetworkDisconnectionReason reason) => Disconnects++;
        public void ExecuteClientCommandFromServer(string command) => ClientCommands.Add(command);
    }
}
namespace CounterStrikeSharp.API.ValveConstants.Protobuf { public enum NetworkDisconnectionReason { NETWORK_DISCONNECT_KICKED, NETWORK_DISCONNECT_KICKBANADDED } }
namespace CounterStrikeSharp.API.Modules.Admin
{
    public static class AdminManager
    {
        public static bool PlayerHasPermissions(CounterStrikeSharp.API.Core.CCSPlayerController? p,params string[] flags)=>p?.CssRoot??true;
        public static uint GetPlayerImmunity(CounterStrikeSharp.API.Core.CCSPlayerController? p)=>p?.CssImmunity??0;
    }
}
namespace CounterStrikeSharp.API.Modules.Menu
{
    public sealed class ChatMenuOption
    {
        public string Text {get;} public bool Disabled {get;} public Action<CounterStrikeSharp.API.Core.CCSPlayerController,ChatMenuOption> Callback {get;}
        public ChatMenuOption(string text,Action<CounterStrikeSharp.API.Core.CCSPlayerController,ChatMenuOption> callback,bool disabled){Text=text;Callback=callback;Disabled=disabled;}
        public void Click(CounterStrikeSharp.API.Core.CCSPlayerController player,bool force=false) {if(!Disabled||force)Callback(player,this);}
    }
    public sealed class ChatMenu
    {
        public string Title {get;} public bool ExitButton {get;set;} public List<ChatMenuOption> MenuOptions {get;}=new();
        public ChatMenu(string title){Title=title;}
        public void AddMenuOption(string text,Action<CounterStrikeSharp.API.Core.CCSPlayerController,ChatMenuOption> callback,bool disabled=false)=>MenuOptions.Add(new(text,callback,disabled));
    }
}
namespace CounterStrikeSharp.API
{
    public static class Utilities
    {
        public static List<CounterStrikeSharp.API.Core.CCSPlayerController> Players {get;}=new();public static int StateChanges {get;set;}
        public static List<CounterStrikeSharp.API.Core.CCSPlayerController> GetPlayers()=>Players.ToList();
        public static CounterStrikeSharp.API.Core.CCSPlayerController? GetPlayerFromSlot(int slot)=>Players.FirstOrDefault(x=>x.Slot==slot);
        public static void SetStateChanged(CounterStrikeSharp.API.Core.CCSPlayerPawn pawn,string schema,string field)=>StateChanges++;
    }
    public static class Server
    {
        public static string GameDirectory {get;set;}=""; public static Queue<Action> Frames {get;}=new();public static List<string> Commands {get;}=new();
        public static void NextFrame(Action action)=>Frames.Enqueue(action);
        public static void ExecuteCommand(string command)=>Commands.Add(command);
        public static void Tick(){while(Frames.TryDequeue(out var frame))frame();}
    }
}
namespace ZePractice
{
    public sealed class TestConfig
    {
        public bool AllowEveryonePracticeCommands {get;set;}=true;public bool TrustLoopbackAdmin {get;set;}
        public List<string> OwnerSteamIds {get;set;}=new();public bool AutoRespawn {get;set;}=true;
        public float RespawnDelaySeconds {get;set;}=1f;public string ServerNumber {get;set;}="2001";
    }
    public sealed class TestLogger {public List<string> Messages {get;}=new();}
    public static class ZeGameplayCatalog {public static readonly Dictionary<string,string> WeaponAliases=new(){["ak"]="weapon_ak47"};}
    public sealed class ZePracticePlugin
    {
        public string ModuleDirectory {get;set;}="";public string FysDirectory=>Path.Combine(ModuleDirectory,"Fys");
        public TestConfig Config {get;}=new();public TestLogger Logger {get;}=new();public bool IsPractice {get;set;}=true;
        public ZePermissionService Permissions {get;set;}=null!;public List<string> Replies {get;}=new();public int Reloads {get;private set;}
        public Dictionary<string,CounterStrikeSharp.API.Modules.Commands.CommandInfo.CommandCallback> Commands {get;}=new();
        public Dictionary<int,CounterStrikeSharp.API.Modules.Menu.ChatMenu> Menus {get;}=new();
        public List<CounterStrikeSharp.API.Modules.Timers.Timer> Timers {get;}=new();
        public HashSet<int> ResettingSlots {get;}=new();
        public bool TeamResetPending(CounterStrikeSharp.API.Core.CCSPlayerController player)=>ResettingSlots.Contains(player.Slot);
        public static bool IsRealPlayer(CounterStrikeSharp.API.Core.CCSPlayerController? p)=>p is {IsValid:true,IsBot:false,IsHLTV:false};
        public void Reply(CounterStrikeSharp.API.Core.CCSPlayerController? p,string text)=>Replies.Add(text);
        public bool CanAdmin(CounterStrikeSharp.API.Core.CCSPlayerController? p)=>Permissions.HasAdminAccess(p);
        public bool NeedPermission(CounterStrikeSharp.API.Core.CCSPlayerController? p,string command)
        {if(Permissions.Allows(p,ZeCommandText.Canonical(command)))return true;Reply(p,"denied "+command);return false;}
        public void AddZeCommand(string name,string description,CounterStrikeSharp.API.Modules.Commands.CommandInfo.CommandCallback callback)=>Commands.Add(name,(p,c)=>{if(NeedPermission(p,name))callback(p,c);});
        public void RemoveZeCommand(string name,CounterStrikeSharp.API.Modules.Commands.CommandInfo.CommandCallback callback)=>Commands.Remove(name);
        public void ExecuteOwnCommand(CounterStrikeSharp.API.Core.CCSPlayerController p,string command){if(NeedPermission(p,command))p.ExecuteClientCommandFromServer(command);}
        public void OpenMenu(CounterStrikeSharp.API.Core.CCSPlayerController p,CounterStrikeSharp.API.Modules.Menu.ChatMenu menu)=>Menus[p.Slot]=menu;
        public void ShowStatus(CounterStrikeSharp.API.Core.CCSPlayerController? p)=>Reply(p,"status");
        public void OpenMapVoteMenu(CounterStrikeSharp.API.Core.CCSPlayerController? p,string search="")=>Reply(p,"mapvote "+search);
        public bool ForceMapChange(CounterStrikeSharp.API.Core.CCSPlayerController? p,string command)
        { if(!Permissions.IsServerOwner(p))return false;CounterStrikeSharp.API.Server.ExecuteCommand(command);return true; }
        public void SetPracticeMode(bool practice)=>IsPractice=practice;
        private readonly Dictionary<int,(CounterStrikeSharp.API.Core.MoveType_t Move,CounterStrikeSharp.API.Core.MoveType_t Actual)> _publicNoclip=new();
        public bool PublicNoclipEnabled(CounterStrikeSharp.API.Core.CCSPlayerController player)=>_publicNoclip.ContainsKey(player.Slot);
        public void TogglePublicNoclip(CounterStrikeSharp.API.Core.CCSPlayerController? player)
        {
            if(!IsRealPlayer(player)||!player!.PawnIsAlive||player.PlayerPawn.Value is not {Health:>0} pawn)return;
            if(_publicNoclip.Remove(player.Slot,out var original)){pawn.MoveType=original.Move;pawn.ActualMoveType=original.Actual;}
            else{_publicNoclip[player.Slot]=(pawn.MoveType,pawn.ActualMoveType);pawn.MoveType=pawn.ActualMoveType=CounterStrikeSharp.API.Core.MoveType_t.MOVETYPE_NOCLIP;}
        }
        public void RestartManualRound(CounterStrikeSharp.API.Core.CCSPlayerController? player)=>CounterStrikeSharp.API.Server.ExecuteCommand("mp_restartgame 1");
        public void ReloadMapConfiguration()=>Reloads++;
        public CounterStrikeSharp.API.Modules.Timers.Timer AddTimer(float seconds,Action callback,CounterStrikeSharp.API.Modules.Timers.TimerFlags flags=0)
        {var timer=new CounterStrikeSharp.API.Modules.Timers.Timer(callback,flags);Timers.Add(timer);return timer;}
        public void AddCommandListener(string n,CounterStrikeSharp.API.Modules.Commands.CommandInfo.CommandCallback c,CounterStrikeSharp.API.Core.HookMode m){}
        public void AddCommandListener(string n,Func<CounterStrikeSharp.API.Core.CCSPlayerController?,CounterStrikeSharp.API.Modules.Commands.CommandInfo,CounterStrikeSharp.API.Core.HookResult> c,CounterStrikeSharp.API.Core.HookMode m){}
        public void RemoveCommandListener(string n,Func<CounterStrikeSharp.API.Core.CCSPlayerController?,CounterStrikeSharp.API.Modules.Commands.CommandInfo,CounterStrikeSharp.API.Core.HookResult> c,CounterStrikeSharp.API.Core.HookMode m){}
        public void RegisterListener<T>(T callback) where T:Delegate {}
        public void RemoveListener<T>(T callback) where T:Delegate {}
        public void RegisterEventHandler<T>(Func<T,CounterStrikeSharp.API.Core.GameEventInfo,CounterStrikeSharp.API.Core.HookResult> callback,CounterStrikeSharp.API.Core.HookMode mode){}
        public void DeregisterEventHandler<T>(Func<T,CounterStrikeSharp.API.Core.GameEventInfo,CounterStrikeSharp.API.Core.HookResult> callback,CounterStrikeSharp.API.Core.HookMode mode){}
    }
}
namespace Microsoft.Extensions.Logging
{
    public static class LoggingExtensions
    {
        public static void LogInformation(this ZePractice.TestLogger logger,string text,params object?[] values)=>logger.Messages.Add(text);
        public static void LogWarning(this ZePractice.TestLogger logger,string text,params object?[] values)=>logger.Messages.Add(text);
        public static void LogWarning(this ZePractice.TestLogger logger,Exception e,string text,params object?[] values)=>logger.Messages.Add(text);
        public static void LogError(this ZePractice.TestLogger logger,Exception e,string text,params object?[] values)=>logger.Messages.Add(text);
        public static void LogDebug(this ZePractice.TestLogger logger,Exception e,string text,params object?[] values)=>logger.Messages.Add(text);
    }
}
