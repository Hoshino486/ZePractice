using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ZePractice;

/// <summary>One shared read-only HUD, transmitted only to players who enabled it. Sampling is global, not per player.</summary>
public sealed class ServerDiagnosticsService(ZePracticePlugin plugin) : IDisposable
{
    public const string Layout="panorama/layout/custom_game/zepractice/diagnostics.vxml_c";
    public static readonly string[] Required=[Layout,"panorama/styles/custom_game/zepractice/diagnostics.vcss_c"];
    private sealed class Session(CCSPlayerController p)
    { public CCSPlayerController Player=p; public uint Handle=p.EntityHandle.Raw; public int? UserId=p.UserId; public bool Enabled=true; }
    private readonly Dictionary<int,Session> _sessions=new();
    private readonly Dictionary<string,string> _text=new(),_colors=new();
    private readonly Dictionary<int,(uint Handle,int? UserId,bool Enabled)> _visibility=new();
    private readonly HashSet<string> _errors=new();
    private CCSCustomHudLayout? _hud;
    private CommanderResources.Availability _resources=new(false,"initializing","");
    private ServerDiagnosticsLogic.Reading _reading=new(0,0,0,0,null,0,null,"initializing");
    private bool _active,_disposed;
    private double _nextSample,_nativeSeen,_retryAt,_nextResourceCheck;
    private uint _sequence;
    private int _tick;
    private double? _memory;
    private double _nextMemory;
    private readonly Process _process=Process.GetCurrentProcess();
    private readonly ScriptActivityService _scripts=new(plugin);
    public void Initialize()
    {
        _scripts.Initialize();
        plugin.RegisterListener<Listeners.OnTick>(Tick);
        plugin.RegisterListener<Listeners.CheckTransmit>(Transmit);
        plugin.RegisterListener<Listeners.OnClientDisconnect>(Disconnect);
        plugin.RegisterListener<Listeners.OnServerPrecacheResources>(Precache);
        plugin.AddZeCommand("css_ze_diag","实时服务器运行诊断：!ze_diag [on|off]",Toggle);
        plugin.AddZeCommand("css_ze_diag_status","查看服务器诊断快照及采样状态",Status);
        plugin.AddZeCommand("css_ze_script_status","查看真实地图脚本回调、Think 和异常统计",ScriptStatus);
    }
    private void Precache(ResourceManifest manifest)
    {
        RefreshResources();
        // Keep Required/layout as compiled paths; the precache manifest appends _c itself.
        if (_resources.Ready) foreach (var name in Required)
            manifest.AddResource(name.EndsWith("_c", StringComparison.Ordinal) ? name[..^2] : name);
    }
    public void RefreshResources() => _resources=CommanderResources.Installed(Server.GameDirectory,Required);
    public void OnMapStart()
    {
        _scripts.OnMapStart();
        RemoveHud(); _active=true; _nextSample=_nativeSeen=_retryAt=_nextMemory=0; _sequence=0; _tick=-1;
        _reading=new(0,0,0,0,null,0,null,"initializing"); _errors.Clear();
        RefreshResources(); _nextResourceCheck=Server.EngineTime+5;
    }
    public void OnMapEnd() { _scripts.OnMapEnd(); _active=false; _hud=null; _text.Clear(); _colors.Clear(); _visibility.Clear(); }
    private static bool Owns(Session s,CCSPlayerController p)=>p.IsValid && s.Player.IsValid && p.EntityHandle.Raw==s.Handle && p.UserId==s.UserId;
    private Session Get(CCSPlayerController p)
    {
        if(_sessions.TryGetValue(p.Slot,out var s)&&Owns(s,p))return s;
        return _sessions[p.Slot]=new(p);
    }
    public bool Enabled(CCSPlayerController p)=>!_sessions.TryGetValue(p.Slot,out var s)||!Owns(s,p)||s.Enabled;
    private void Disconnect(int slot) { _sessions.Remove(slot); _visibility.Remove(slot); }
    private void Tick()
    {
        if(_disposed||!_active)return;
        double now=Server.EngineTime;
        if(now<_nextSample)return;
        _nextSample=now+.5;
        try
        {
            var players=Utilities.GetPlayers();
            foreach(var p in players.Where(ZePracticePlugin.IsRealPlayer))Get(p);
            foreach(var pair in _sessions.ToArray())
                if(!pair.Value.Player.IsValid || !Owns(pair.Value,pair.Value.Player))_sessions.Remove(pair.Key);
            // Resource metadata is not read per tick. Retry only when the installed pack lacks this new layout.
            if(!_resources.Ready && now>=_nextResourceCheck) {RefreshResources();_nextResourceCheck=now+5;}
            int all=0,networked=0,scripts=0;
            foreach(var e in Utilities.GetAllEntities())
            {
                if(!e.IsValid)continue;
                if(++all>ServerDiagnosticsLogic.TotalEntityLimit)throw new InvalidDataException("Entity enumeration exceeded Source 2 capacity.");
                if(ServerDiagnosticsLogic.IsNetworkedIndex(e.Index))networked++;
                if(e.DesignerName=="point_script")scripts++;
            }
            var raw=ConVar.Find("ze_diag_snapshot")?.StringValue;
            var native=ServerDiagnosticsLogic.Parse(raw);
            string status=raw==null?"native-plugin-not-loaded":native==null?"awaiting-native-sample":"current";
            if(native!=null)
            {
                if(native.Sequence!=_sequence || native.Tick!=_tick){_nativeSeen=now;_sequence=native.Sequence;_tick=native.Tick;}
                if(now-_nativeSeen>2 || Math.Abs((long)Server.TickCount-native.Tick)>256){native=null;status="stale-native-sample";}
            }
            if(now>=_nextMemory)
            {
                _nextMemory=now+2;
                try {_process.Refresh();_memory=_process.WorkingSet64/1048576d;}
                catch(Exception e){_memory=null;Log(e,"process memory");}
            }
            _reading=new(all,networked,players.Count(ZePracticePlugin.IsRealPlayer),players.Count(p=>p.IsBot&&!p.IsHLTV),_memory,now,native,status,_scripts.Sample(scripts));
            if(!_resources.Ready || !_sessions.Values.Any(s=>s.Enabled)){RemoveHud();return;}
            if(now>=_retryAt)Render();
        }
        catch(Exception e){Log(e,"sampling/render");RemoveHud();_retryAt=now+5;}
    }
    private void Log(Exception e,string area)
    { if(_errors.Count<16 && _errors.Add(area+": "+e.Message))plugin.Logger.LogError(e,"SERVER_DIAGNOSTICS {Area}",area); }
    private void Text(string id,string value)
    { if(_text.GetValueOrDefault(id)==value)return;ZeHudText.Set(_hud!,id,"text",value);_text[id]=value; }
    private void Color(string id,string color)
    {
        if(_colors.GetValueOrDefault(id)==color)return;
        foreach(var name in new[]{"good","warning","critical","unknown"})_hud!.SetHasClass(id,name,name==color);
        _colors[id]=color;
    }
    private void Render()
    {
        if(_hud is not {IsValid:true})
        {
            _hud=Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout")??throw new InvalidOperationException("Diagnostics HUD allocation failed");
            using var kv=new CEntityKeyValues();kv.SetString("layout",Layout);kv.SetString("targetname","ze_server_diagnostics_hud");
            _hud.Observable=false;_hud.DispatchSpawn(kv);_hud.SetHasClass("diagnostics_window","hidden",true);
            _text.Clear();_colors.Clear();
        }
        foreach(var s in _sessions.Values)
        {
            if(!Owns(s,s.Player))continue;
            var state=(s.Handle,s.UserId,s.Enabled);
            if(_visibility.TryGetValue(s.Player.Slot,out var sent)&&sent==state)continue;
            _hud.SetHasClassForPlayer(s.Player,"diagnostics_window","hidden",!s.Enabled);
            _visibility[s.Player.Slot]=state;
        }
        var r=_reading;var n=r.Native;
        Text("diag_entities",r.Entities.ToString(CultureInfo.InvariantCulture));
        Text("diag_networked",r.Networked.ToString(CultureInfo.InvariantCulture));
        Text("diag_script",ScriptActivityLogic.Summary(r.Script));
        Text("diag_mean",ServerDiagnosticsLogic.Ms(n?.SimMeanMs));Text("diag_peak",ServerDiagnosticsLogic.Ms(n?.SimPeakMs));
        Text("diag_detail",string.Create(CultureInfo.InvariantCulture,$"TPS {n?.Tps.ToString("0.0",CultureInfo.InvariantCulture)??"--"}/64  |  RAM {r.MemoryMiB?.ToString("0",CultureInfo.InvariantCulture)??"--"} MiB  |  真人 {r.Humans} / Bot {r.Bots}  |  运行 {ServerDiagnosticsLogic.Uptime(r.UptimeSeconds)}"));
        Color("diag_entities",ServerDiagnosticsLogic.EntityColor(r.Entities,(int)ServerDiagnosticsLogic.TotalEntityLimit));
        Color("diag_networked",ServerDiagnosticsLogic.EntityColor(r.Networked,(int)ServerDiagnosticsLogic.NetworkEntityLimit));
        Color("diag_script",ScriptActivityLogic.Color(r.Script));
        Color("diag_mean",ServerDiagnosticsLogic.LoadColor(n?.SimMeanMs));Color("diag_peak",ServerDiagnosticsLogic.LoadColor(n?.SimPeakMs));
    }
    private void RemoveHud()
    { if(_hud is {IsValid:true})_hud.Remove();_hud=null;_text.Clear();_colors.Clear();_visibility.Clear(); }
    private bool VisibleTo(CCSPlayerController? p) =>
        p is {IsValid:true} && _sessions.TryGetValue(p.Slot,out var s) && Owns(s,p) && s.Enabled;
    private void Transmit(CCheckTransmitInfoList list)
    {
        if(!_active || _disposed || _hud is not {IsValid:true} hud)return;
        foreach(var (info,p) in list)
        {
            if(VisibleTo(p))info.TransmitEntities.Add(hud);
            else info.TransmitEntities.Remove(hud);
        }
    }
    private void Toggle(CCSPlayerController? p,CommandInfo c)
    {
        if(!ZePracticePlugin.IsRealPlayer(p)){plugin.Reply(p,"游戏内输入 !ze_diag [on|off]；!ze_diag_status 查看快照。");return;}
        var s=Get(p!);
        if(c.ArgCount>2 || !ServerDiagnosticsLogic.TryToggle(c.GetArg(1),s.Enabled,out bool enabled)){plugin.Reply(p,"用法：!ze_diag [on|off]");return;}
        s.Enabled=enabled;
        plugin.Reply(p,"服务器诊断面板："+(enabled?"开":"关")+(_resources.Ready?"。":"；诊断布局尚未安装，!ze_diag_status 可查看实时数据。"));
    }
    public string Diagnostics()=>JsonSerializer.Serialize(new {active=_active,resources=_resources,reading=_reading,
        hud=_hud is {IsValid:true}?(uint?)_hud.Index:null,defaultEnabled=true,updateHz=2,enabledViewers=_sessions.Values.Count(s=>s.Enabled),
        sampleScope="IServerGameDLL.GameFrame pre/post wall time; latest half-second mean/peak, not network latency or complete engine frame",
        scriptScope=ScriptActivityService.Scope,
        networkScope="active valid entities allocated in network index range [0,16384); not per-client visible entities",
        errors=_errors.ToArray(),visualAcceptance="requires-real-client"});
    private void Status(CCSPlayerController? p,CommandInfo c)
    { if(p==null)plugin.Reply(null,Diagnostics());else {plugin.Reply(p,ServerDiagnosticsLogic.Summary(_reading));plugin.Reply(p,$"采样：{_reading.NativeStatus}；资源：{_resources.Status}；括号为最近半秒峰值。!ze_diag 开关面板。");} }
    private void ScriptStatus(CCSPlayerController? p,CommandInfo c)
    {
        if(p==null)plugin.Reply(null,JsonSerializer.Serialize(new {script=_reading.Script,scope=ScriptActivityService.Scope}));
        else {
            plugin.Reply(p,ScriptActivityLogic.Summary(_reading.Script));
            plugin.Reply(p,"Think 是回调的子集；0 次可能是等待事件。异常为本次地图/采样启动以来捕获的回调异常，不包含启动导入错误，也不代表地图所有逻辑均正确。");
        }
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;_active=false;_scripts.Dispose();RemoveHud();_sessions.Clear();_process.Dispose();
        plugin.RemoveListener<Listeners.OnTick>(Tick);plugin.RemoveListener<Listeners.CheckTransmit>(Transmit);
        plugin.RemoveListener<Listeners.OnClientDisconnect>(Disconnect);plugin.RemoveListener<Listeners.OnServerPrecacheResources>(Precache);
        plugin.RemoveZeCommand("css_ze_diag",Toggle);plugin.RemoveZeCommand("css_ze_diag_status",Status);
        plugin.RemoveZeCommand("css_ze_script_status",ScriptStatus);
    }
}
