using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
namespace CounterStrikeSharp.API
{
    public static class Server
    {
        public static float CurrentTime;
        public static string GameDirectory = "", MapName = "fixture";
        private static readonly Queue<Action> Frames = new();
        public static void NextFrame(Action action) => Frames.Enqueue(action);
        public static readonly List<string> Executed=new();
        public static void ExecuteCommand(string command)=>Executed.Add(command);
        public static void Frame() { int n=Frames.Count; while(n-->0) Frames.Dequeue()(); }
        public static void Drain() { for(int i=0;Frames.Count>0&&i<20;i++) Frame(); if(Frames.Count>0)throw new Exception("unbounded frame queue"); }
    }
    public static class Utilities
    {
        public static readonly List<CCSPlayerController> Players = new();
        public static List<CCSPlayerController> GetPlayers() => Players;
        public static CCSPlayerController? GetPlayerFromSlot(int slot) => Players.FirstOrDefault(p => p.Slot == slot);
        public static IEnumerable<T> FindAllEntitiesByDesignerName<T>(string name)=>Array.Empty<T>();
        public static bool FailCreate;
        public static T? CreateEntityByName<T>(string name) where T:class,new() => FailCreate ? null : new();
    }
}
namespace CounterStrikeSharp.API.Core
{
    public enum HookResult { Continue, Stop }
    public sealed class EntityHandle { private static uint _serial; public uint Raw=++_serial; }
    public sealed class CCSPlayerController
    {
        public bool IsValid=true, IsBot, IsHLTV;
        public int Slot; public int? UserId; public int TeamNum=3; public string PlayerName="fixture", IpAddress="127.0.0.1:27005";
        public EntityHandle EntityHandle=new();
        public IntPtr Handle => (IntPtr)EntityHandle.Raw;
        public Action<string>? ExecuteFromServer;
        public void ExecuteClientCommandFromServer(string command)=>ExecuteFromServer?.Invoke(command);
    }
    public sealed class CCSWeaponBase { }
    public sealed class CCSGameRules { public void TerminateRound(float seconds,CounterStrikeSharp.API.Modules.Entities.Constants.RoundEndReason reason){} }
    public sealed class CCSGameRulesProxy { public CCSGameRules? GameRules; }
    public sealed class CEntityKeyValues : IDisposable { public readonly Dictionary<string,string> Values=new(); public void SetString(string k,string v)=>Values[k]=v; public void Dispose(){} }
    public sealed class CCSCustomHudLayout
    {
        public bool IsValid=true, Observable;
        public int Removes; public string StrLayout="";
        public EntityHandle EntityHandle=new();
        public readonly Dictionary<string,string> Text=new();
        public readonly Dictionary<string,bool> Classes=new();
        private readonly HashSet<int> _captured=new();
        public void DispatchSpawn(CEntityKeyValues? kv=null) { if(kv!=null) StrLayout=kv.Values["layout"]; }
        public void SetHasClass(string panel,string cls,bool value)=>Classes[panel+"/"+cls]=value;
        public void SetHasClassForPlayer(CCSPlayerController p,string panel,string cls,bool value)=>SetHasClass(panel,cls,value);
        public void SetDialogVariableStringForPlayer(CCSPlayerController p,string panel,string variable,string value)=>Text[panel]=value;
        public void SetInputCaptureEnabled(CCSPlayerController p,bool on){if(on)_captured.Add(p.Slot);else _captured.Remove(p.Slot);}
        public bool IsInputCaptureEnabled(CCSPlayerController p)=>_captured.Contains(p.Slot);
        public void Remove(){Removes++;IsValid=false;}
    }
    public static class Listeners
    {
        public delegate void OnMapStart(string map);
        public delegate void OnMapEnd();
        public delegate void OnClientDisconnect(int slot);
        public delegate void OnServerPrecacheResources(ResourceManifest manifest);
        public delegate void CheckTransmit(CCheckTransmitInfoList list);
        public delegate void OnCustomHudClicked(CCSPlayerController player,CCSCustomHudLayout layout,string buttonId);
    }
    public sealed class ResourceManifest { public readonly List<string> Resources=new(); public void AddResource(string r)=>Resources.Add(r); }
    public sealed class CCheckTransmitInfo { public readonly HashSet<CCSCustomHudLayout> TransmitEntities=new(); }
    public sealed class CCheckTransmitInfoList : List<(CCheckTransmitInfo,CCSPlayerController?)> { }
}
namespace CounterStrikeSharp.API.Modules.Commands
{
    public enum HookMode { Pre, Post }
    public sealed class CommandInfo(params string[] args)
    {
        public delegate void CommandCallback(CCSPlayerController? p,CommandInfo c);
        public int ArgCount=>args.Length; public string GetArg(int n)=>n<args.Length?args[n]:"";
    }
}
namespace CounterStrikeSharp.API.Modules.Menu
{
    public enum PostSelectAction { Close, Reset, Nothing }
    public sealed class ChatMenuOption(string text,Action<CCSPlayerController,ChatMenuOption> callback,bool disabled=false)
    { public string Text=text; public Action<CCSPlayerController,ChatMenuOption> OnSelect=callback; public bool Disabled=disabled; }
    public class ChatMenu(string title)
    {
        public string Title=title;
        public bool ExitButton;
        public PostSelectAction PostSelectAction=PostSelectAction.Reset;
        public List<ChatMenuOption> MenuOptions=new();
        public ChatMenuOption AddMenuOption(string text,Action<CCSPlayerController,ChatMenuOption> callback,bool disabled=false)
        {var option=new ChatMenuOption(text,callback,disabled);MenuOptions.Add(option);return option;}
    }
    public class BaseMenuInstance(ChatMenu menu) { public ChatMenu Menu=menu; public int Page, Displays; public void Display()=>Displays++; }
    public static class MenuManager
    {
        private static readonly Dictionary<uint,BaseMenuInstance> Active=new();
        public static BaseMenuInstance? GetActiveMenu(CCSPlayerController p)=>Active.GetValueOrDefault(p.EntityHandle.Raw);
        public static void CloseActiveMenu(CCSPlayerController p)=>Active.Remove(p.EntityHandle.Raw);
        public static void OpenChatMenu(CCSPlayerController p,ChatMenu menu){Active[p.EntityHandle.Raw]=new(menu);Active[p.EntityHandle.Raw].Display();}
        public static readonly List<int> Keys=new();
        public static void OnKeyPress(CCSPlayerController p,int key)
        {
            Keys.Add(key); var current=GetActiveMenu(p); if(current==null)return;
            if(key is 0 or 9){CloseActiveMenu(p);return;}
            if(key==7){current.Page=Math.Max(0,current.Page-1);return;}
            if(key==8){current.Page++;return;}
            int index=current.Page*6+key-1;
            if(index>=0&&index<current.Menu.MenuOptions.Count&&!current.Menu.MenuOptions[index].Disabled)
                current.Menu.MenuOptions[index].OnSelect(p,current.Menu.MenuOptions[index]);
        }
    }
}
namespace CounterStrikeSharp.API.Modules.Entities.Constants { public enum RoundEndReason { RoundDraw } }
namespace CounterStrikeSharp.API.Modules.Extensions { public static class Placeholder{} }
namespace CounterStrikeSharp.API.Modules.Utils { public static class Placeholder{} }
namespace CounterStrikeSharp.API.Modules.Timers
{
    [Flags] public enum TimerFlags{ REPEAT=1, STOP_ON_MAPCHANGE=2 }
    public sealed class Timer(Action callback) { public bool Killed; public void Kill()=>Killed=true; public void Run(){if(!Killed)callback();} }
}
namespace Microsoft.Extensions.Logging
{
    public sealed class TestLogger
    {
        public readonly List<string> Errors=new();
        public void LogWarning(Exception e,string s,params object[] args)=>Errors.Add(s+e);
        public void LogError(Exception e,string s,params object[] args)=>Errors.Add(s+e);
        public void LogInformation(string s){}
    }
}
namespace ZePractice
{
    public static class ZeHudText
    {
        public static void Prepare(){}
        public static void Set(CCSCustomHudLayout hud,string panel,string variable,string value)=>hud.Text[panel]=value;
    }
    public sealed class MenuPermissions
    {
        public bool Admin; public readonly HashSet<string> Deny=new();
        public bool Allows(CCSPlayerController p,string command)=>!Deny.Contains(command)&&(command!="css_zeadmin"||Admin);
        public bool HasAdminAccess(CCSPlayerController p)=>Admin;
        public string Describe(CCSPlayerController p)=>"fixture";
    }
#if !ACCESS_MENU_TESTS
    public sealed class ZePracticePlugin
    {
        public readonly StubConfig Config=new();
        public readonly MenuPermissions Permissions=new();
        public readonly Microsoft.Extensions.Logging.TestLogger Logger=new();
        public readonly List<CounterStrikeSharp.API.Modules.Timers.Timer> Timers=new();
        public readonly Dictionary<Type,Delegate> Listeners=new();
        public ChatMenu? Current;
        public Action<CCSPlayerController,ChatMenu>? OpenAdapter;
        public Action<CCSPlayerController,string>? OnCommand;
        public readonly List<string> Commands=new(), Messages=new();
        public bool Leader, ThirdPerson, Diagnostics=true, Movement=true, IsPractice=true;
        public bool IsCommander(CCSPlayerController p)=>Leader;
        public bool ThirdPersonEnabled(CCSPlayerController p)=>ThirdPerson;
        public bool DiagnosticsHudEnabled(CCSPlayerController p)=>Diagnostics;
        public bool MovementHudEnabled(CCSPlayerController p)=>Movement;
        public static bool IsRealPlayer(CCSPlayerController? p)=>p is { IsValid:true, IsBot:false, IsHLTV:false };
        public bool NeedPermission(CCSPlayerController p,string command)=>Permissions.Allows(p,ZeCommandText.Canonical(command));
        public void ExecuteOwnCommand(CCSPlayerController p,string command)
        {if(NeedPermission(p,command.Split(' ')[0])){Commands.Add(command);OnCommand?.Invoke(p,command);}}
        public void UseChatMenus(CCSPlayerController p) { }
        public bool RestoreRadioMenus(CCSPlayerController p)=>false;
        public void Reply(CCSPlayerController? p,string text)=>Messages.Add(text);
        public void OpenMenu(CCSPlayerController p,ChatMenu menu){Current=menu;OpenAdapter?.Invoke(p,menu);}
        public CounterStrikeSharp.API.Modules.Timers.Timer AddTimer(float seconds,Action callback,CounterStrikeSharp.API.Modules.Timers.TimerFlags flags)
        {var timer=new CounterStrikeSharp.API.Modules.Timers.Timer(callback);Timers.Add(timer);return timer;}
        public void RegisterListener<T>(T handler) where T:Delegate=>Listeners[typeof(T)]=handler;
        public void RemoveListener<T>(T handler) where T:Delegate=>Listeners.Remove(typeof(T));
        public void AddZeCommand(string name,string help,CommandInfo.CommandCallback callback){}
        public void RemoveZeCommand(string name,CommandInfo.CommandCallback callback){}
        public IReadOnlyDictionary<string,bool> GetPracticeToggles(CCSPlayerController p)=>new Dictionary<string,bool>();
    }
#endif
    public sealed class StubConfig
    {
        public StubBotOptions PracticeBots=new();
        public string MenuUiMode="radio";
        public bool MenuAssetsReady=true, PanoramaAllowRemoteClients=true;
    }
    public static class CommanderResources
    {
        public sealed record Availability(bool Ready,string Status,string Path);
        public static Availability Result=new(true,"verified-fixture", "fixture.vpk");
        public static Availability Installed(string root,IEnumerable<string> members)=>Result;
    }
    public sealed class StubBotOptions { public int MaximumHealth=1000000; }
#if !ACCESS_MENU_TESTS
    public static class ZeCommandText { public static string Canonical(string s)=>s=="css_zeweapon"?"css_zebuy":s; }
#endif
    public sealed class MenuOptions { public int ZombieHealth=35000, HumanHealthLimit=35000, HumanArmorLimit=2500, ZombieArmor=2500, MaximumRoundMinutes=1440; }
    public sealed partial class PublicGameplayService(ZePracticePlugin plugin)
    {
        private readonly ZePracticePlugin _plugin=plugin;
        private bool _disposed;
        private bool _active=true;
        private int _generation;
        private readonly MenuOptions Options=new();
        private sealed class PlayerState { public bool Noclip=false, InfiniteMagazine=false, NoSpread=false, BunnyHop=false; public float MusicVolume=1; public int HideRange=0; }
        private PlayerState State(CCSPlayerController p)=>new();
        public void Open(CCSPlayerController p)=>ShowMenu(p);
        public void ChangeMap()=>_generation++;
        public void Stop()=>_disposed=true;
        public void ToggleNoclip(CCSPlayerController? p){}
        public bool NoclipEnabled(CCSPlayerController p)=>false;
        public bool TeamResetPending(CCSPlayerController p)=>false;
        public void TrackIssuedGun(CCSWeaponBase weapon){}
        public HookResult Chat(CCSPlayerController p,string text)=>MenuInputChat(p,new("say",text));
    }
}
