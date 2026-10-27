using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
namespace ZePractice;

// Only the engine and unrelated gameplay services are faked: command wrappers, access router,
// pending opens, radio and Panorama adapters are the production source files.
public sealed partial class ZePracticePlugin : IDisposable
{
    public readonly StubConfig Config=new();
    public readonly Microsoft.Extensions.Logging.TestLogger Logger=new();
    public readonly List<CounterStrikeSharp.API.Modules.Timers.Timer> Timers=new();
    public readonly Dictionary<Type,Delegate> Listeners=new();
    public readonly Dictionary<string,CommandInfo.CommandCallback> Registered=new();
    public readonly List<string> Commands=new(),Messages=new();
    private readonly AdminPracticeService? _admin=null;
    private readonly PublicGameplayService? _publicGameplay=null;
    private readonly MapMarkerService? _mapMarkers=null;
    private readonly WorkshopVoteService? _mapVote=null;
    private readonly PracticeRoundService? _roundGuard=null;
    public bool IsPractice=true;
    public bool IsCommander(CCSPlayerController p)=>false;
    public bool ThirdPersonEnabled(CCSPlayerController p)=>false;
    public bool DiagnosticsHudEnabled(CCSPlayerController p)=>true;
    public bool MovementHudEnabled(CCSPlayerController p)=>true;
    public static bool IsRealPlayer(CCSPlayerController? p)=>p is {IsValid:true,IsBot:false,IsHLTV:false};
    public void Reply(CCSPlayerController? p,string text)=>Messages.Add(text);
    public CounterStrikeSharp.API.Modules.Timers.Timer AddTimer(float seconds,Action callback,CounterStrikeSharp.API.Modules.Timers.TimerFlags flags)
    {var timer=new CounterStrikeSharp.API.Modules.Timers.Timer(callback);Timers.Add(timer);return timer;}
    public void RegisterListener<T>(T handler) where T:Delegate=>Listeners[typeof(T)]=Delegate.Combine(Listeners.GetValueOrDefault(typeof(T)),handler);
    public void RemoveListener<T>(T handler) where T:Delegate
    {var next=Delegate.Remove(Listeners.GetValueOrDefault(typeof(T)),handler);if(next==null)Listeners.Remove(typeof(T));else Listeners[typeof(T)]=next;}
    public void AddCommand(string name,string help,CommandInfo.CommandCallback callback)=>Registered.Add(name,callback);
    public void RemoveCommand(string name,CommandInfo.CommandCallback callback)=>Registered.Remove(name);
    public void AddCommandListener(string name,Func<CCSPlayerController?,CommandInfo,HookResult> callback,HookMode mode){}
    public void RemoveCommandListener(string name,Func<CCSPlayerController?,CommandInfo,HookResult> callback,HookMode mode){}
    public void Start(ChatMenu menu)
    {InitializeAccess();AddZeCommand("css_ze","main",(p,_)=>{if(p!=null)OpenMenu(p,menu);});}
    public void Command(CCSPlayerController p,string name,params string[] args)
    {Commands.Add(string.Join(' ',new[]{name}.Concat(args)));Registered[name](p,new CommandInfo(new[]{name}.Concat(args).ToArray()));}
    public void Wire(CCSPlayerController p)=>p.ExecuteFromServer=command=>
    {var parts=command.Split(' ');Command(p,parts[0],parts.Skip(1).ToArray());};
    public HookResult Chat(CCSPlayerController p,string text)=>OnFullWidthChat(p,new("say",text));
    public HookResult RadioChat(CCSPlayerController p,params string[] args)=>OnFullWidthChat(p,new(new[]{"say"}.Concat(args).ToArray()));
    public RadioPanelService Radio=>_radioPanel!;
    public ZePanelService Panel=>_panel!;
    public void Disconnect(int slot)=>((Listeners.OnClientDisconnect)Listeners[typeof(Listeners.OnClientDisconnect)])(slot);
    public void MapEnd()=>((Listeners.OnMapEnd)Listeners[typeof(Listeners.OnMapEnd)])();
    public void Dispose()=>DisposeAccess();
}
public sealed class ZePermissionService(ZePracticePlugin plugin) : IDisposable
{
    public readonly HashSet<string> Deny=new();
    public void Initialize(){}
    public bool Allows(CCSPlayerController? p,string command)=>!Deny.Contains(command);
    public bool HasAdminAccess(CCSPlayerController p)=>false;
    public string Describe(CCSPlayerController p)=>"fixture";
    public bool Reload(out string error){error="";return true;}
    public void Dispose(){}
}
public sealed class AdminPracticeService
{public void RecheckPermissions(){} public IReadOnlyDictionary<string,bool> GetPracticeToggles(CCSPlayerController p)=>new Dictionary<string,bool>();}
public sealed class MapMarkerService {public void TrackIssuedGun(CCSWeaponBase weapon){}}
public sealed class WorkshopVoteService
{public void Nominate(CCSPlayerController? p,string id){}public void ShowMenu(CCSPlayerController? p,string search){}public bool ForceChange(CCSPlayerController? p,string cmd)=>false;}
public sealed class PracticeRoundService {public void PermitManualEnd(){}}
