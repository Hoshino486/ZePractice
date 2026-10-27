using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using CssTimer=CounterStrikeSharp.API.Modules.Timers.Timer;

namespace ZePractice;

/// <summary>
/// CounterStrikeSharp-managed EntWatch: reads CS2Fixes' JSONC map format but NEVER enables the incompatible native EntWatch.
/// Uses weapon UniqueHammerID + actual inventory handles to distinguish items of the same classname.
/// DOES NOT hook pre-use native +use or trigger StartTouch; cooldown visibility and post-output tracking are reliable,
/// while blocking already-fired map IO requires a native pre-use hook and is explicitly not claimed.
/// </summary>
public sealed class EntWatchService : IDisposable
{
    private readonly ZePracticePlugin _plugin;
    private ItemWatchDocument _document=ItemWatchDocument.Empty;
    private readonly Dictionary<string,ItemDefinition> _byWeaponId=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,List<(ItemDefinition Item,int Index,ItemHandlerDefinition Handler)>> _byHandlerId=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint,LiveItem> _items=new();
    private readonly List<(string Output,EntityIO.EntityOutputHandler Callback)> _hooks=new();
    private readonly Dictionary<uint,uint> _owners=new();
    private readonly Dictionary<uint,double> _warnings=new();
    private CssTimer? _timer;
    private bool _initialized,_disposed,_mapActive,_dirty=true;
    private double _lastScan=-5, _nextScan, _nextHud;
    private string _actualMap="";
    private int _loadedCount;
    public EntWatchService(ZePracticePlugin plugin)=>_plugin=plugin;
    private static double Now=>Server.CurrentTime;
    private static CCSPlayerController? Owner(uint raw)=>new CHandle<CCSPlayerController>(raw).Value;
    private static CBaseEntity? Entity(uint raw)=>new CHandle<CBaseEntity>(raw).Value;
    private static bool Good(CCSPlayerController? p)=>ZePracticePlugin.IsRealPlayer(p);
    public void Initialize()
    {
        if(_initialized)return;_initialized=true;
        _plugin.RegisterListener<Listeners.OnEntitySpawned>(OnSpawned);
        _plugin.RegisterListener<Listeners.OnEntityDeleted>(OnDeleted);
        _plugin.RegisterListener<Listeners.OnMapEnd>(ClearMap);
        _plugin.RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        _plugin.RegisterEventHandler<EventItemPickup>(OnPickup,HookMode.Post);
        _plugin.RegisterEventHandler<EventItemRemove>(OnRemove,HookMode.Post);
        _plugin.RegisterEventHandler<EventPlayerDeath>(OnDeath,HookMode.Post);
        _plugin.AddZeCommand("css_items","List your map items and holder/cooldown",ShowItems);
        _plugin.AddZeCommand("css_itemwatch","Admin: show current item binding status",ShowDiagnostics);
        // Only register output names actually present in the current map; avoids wildcard-per-tick hooks.
        _timer=_plugin.AddTimer(0.5f,Tick,TimerFlags.REPEAT);
    }
    public void OnMapStart(string actualMap)
    {
        ClearMap();
        if(!_plugin.Config.EnableItemWatch){_plugin.Logger.LogInformation("EntWatch is disabled in ZePractice config.");return;}
        if(ConVar.Find("entwatch_enable")?.StringValue=="1")
            _plugin.Logger.LogError("Native EntWatch enabled; CS2Fixes marks it incompatible with CS#. Disable native module.");
        _mapActive=true;
        _actualMap=ZePracticePlugin.NormalizeMapName(actualMap);
        if(_actualMap.Length==0)return;
        string cfgDir=string.IsNullOrWhiteSpace(_plugin.Config.ItemWatchDirectory)
            ?Path.Combine(Path.GetFileName(Server.GameDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Equals("csgo",StringComparison.OrdinalIgnoreCase) ? Server.GameDirectory : Path.Combine(Server.GameDirectory,"csgo"),"addons","cs2fixes","configs","entwatch","maps")
            :Path.GetFullPath(Path.IsPathRooted(_plugin.Config.ItemWatchDirectory)?_plugin.Config.ItemWatchDirectory:Path.Combine(_plugin.ModuleDirectory,_plugin.Config.ItemWatchDirectory));
        string path=Path.Combine(cfgDir,_actualMap+".jsonc");
        if(!File.Exists(path))
        {
            _plugin.Logger.LogWarning("ENTWATCH_NO_CONFIG actual_map={ActualMap} fys_alias={Alias} expected={Path}; FYS 2001 provides no public per-map item config. No HammerIDs are invented.",_actualMap,_plugin.CurrentMapKey,path);
            return;
        }
        try { _document=ItemWatchParser.Parse(File.ReadAllText(path)); }
        catch(Exception e) { _plugin.Logger.LogError(e,"EntWatch config invalid: {Path}; no items loaded.",path);return; }
        foreach(var item in _document.Items)
        {
            _byWeaponId[item.HammerId]=item;
            for(int i=0;i<item.Handlers.Count;i++)
            {
                var h=item.Handlers[i];
                if(!_byHandlerId.TryGetValue(h.HammerId,out var list))_byHandlerId[h.HammerId]=list=new();
                list.Add((item,i,h));
            }
        }
        foreach(var output in _document.Items.SelectMany(i=>i.Handlers).Where(h=>h.Type==ItemHandlerType.Button)
                    .Select(h=>string.IsNullOrWhiteSpace(h.Event)?"OnPressed":h.Event).Distinct(StringComparer.OrdinalIgnoreCase))
            HookOutput(output);
        foreach(var output in new[]{"OutValue","OnGetValue"})
            if(_document.Items.Any(i=>i.Handlers.Any(h=>h.Type is ItemHandlerType.CounterDown or ItemHandlerType.CounterUp)))HookOutput(output);
        _loadedCount=_document.Items.Count;
        foreach(var warning in _document.Diagnostics.Take(20))_plugin.Logger.LogWarning("EntWatch config {Path}: {Warning}",path,warning);
        _plugin.Logger.LogInformation("ENTWATCH_LOADED actual_map={Map} rules={Count} file={Path}; source FYS alias={Alias}; native module remains disabled.",_actualMap,_loadedCount,path,_plugin.CurrentMapKey);
        _dirty=true;_nextScan=Now+0.3;
    }
    private void HookOutput(string output)
    {
        EntityIO.EntityOutputHandler handler=(o,name,activator,caller,value,delay)=>OnOutput(output,activator,caller,value);
        _plugin.HookEntityOutput("*",output,handler,HookMode.Pre);
        _hooks.Add((output,handler));
    }
    public void OnRoundStart()
    {
        if(!_mapActive)return;
        _items.Clear();_owners.Clear();_dirty=true;_nextScan=Now+0.5;_nextHud=Now+1;
    }
    private void ClearMap()
    {
        foreach(var h in _hooks)_plugin.UnhookEntityOutput("*",h.Output,h.Callback,HookMode.Pre);
        _hooks.Clear();_byWeaponId.Clear();_byHandlerId.Clear();_items.Clear();_owners.Clear();
        _document=ItemWatchDocument.Empty;_mapActive=false;_loadedCount=0;_dirty=true;_nextHud=0;
    }
    private void OnSpawned(CEntityInstance entity)
    {
        if(!_mapActive||_loadedCount==0)return;
        if(entity.DesignerName?.StartsWith("weapon_",StringComparison.OrdinalIgnoreCase)==true)
        {_dirty=true;_nextScan=Math.Max(Now+0.25,_lastScan+1);}
    }
    private void OnDeleted(CEntityInstance entity)
    {
        uint raw=entity.EntityHandle.Raw;
        if(_items.Remove(raw,out var instance))
        {
            if(instance.Definition.Message&&Good(instance.Holder)) Server.PrintToChatAll($" \x01[神器] {instance.Definition.Name} 已消失");
            _owners.Remove(raw);_dirty=true;
        }
    }
    private void OnClientDisconnect(int slot)
    {
        foreach(var item in _items.Values)
        {
            var owner=item.Holder;
            if(owner!=null && owner.Slot==slot) {item.Owner=0;item.LastOwner=0;}
        }
    }
    private HookResult OnPickup(EventItemPickup ev,GameEventInfo info){_dirty=true;_nextScan=Math.Max(Now+0.02,_lastScan+0.2);return HookResult.Continue;}
    private HookResult OnRemove(EventItemRemove ev,GameEventInfo info){_dirty=true;_nextScan=Math.Max(Now+0.02,_lastScan+0.2);return HookResult.Continue;}
    private HookResult OnDeath(EventPlayerDeath ev,GameEventInfo info){_dirty=true;_nextScan=Math.Max(Now+0.02,_lastScan+0.2);return HookResult.Continue;}
    private HookResult OnOutput(string output,CEntityInstance activator,CEntityInstance caller,CVariant variant)
    {
        if(!_mapActive||_items.Count==0||caller is not {IsValid:true})return HookResult.Continue;
        try
        {
            var e=caller.As<CBaseEntity>();
            string id=e.UniqueHammerID??"";
            if(!_byHandlerId.TryGetValue(id,out var targets))return HookResult.Continue;
            uint sourceRaw=caller.EntityHandle.Raw;
            foreach(var target in targets)
            {
                foreach(var item in _items.Values.Where(i=>i.Definition==target.Item))
                {
                    if(!TemplateRelated(item.WeaponName,e.Entity?.Name??"",target.Handler.Templated??item.Definition.Templated))continue;
                    var state=item.Handlers[target.Index];
                    if(target.Handler.Type is ItemHandlerType.CounterDown or ItemHandlerType.CounterUp)
                    {
                        if(output is not("OutValue" or "OnGetValue")) continue;
                        float? value=TryFloat(variant);if(value is null)continue;
                        var counter=caller.As<CMathCounter>();
                        bool used=state.ObserveCounter(value.Value,counter.Min,counter.Max,Now);
                        if(used && target.Handler.Message && Good(item.Holder))
                            Server.PrintToChatAll($" \x01[神器] {item.Holder!.PlayerName} 使用 {item.Definition.ShortName} · {target.Handler.Name} {state.Label(Now)}");
                    }
                    else if(target.Handler.Type==ItemHandlerType.Button && output.Equals(string.IsNullOrWhiteSpace(target.Handler.Event)?"OnPressed":target.Handler.Event,StringComparison.OrdinalIgnoreCase))
                    {
                        var who=PlayerFromActivator(activator);
                        if(!Good(who) || item.Owner==0 || who!.EntityHandle.Raw!=item.Owner)continue;
                        // Entity-output prehook is not a native button-use detour. Track *actual* outputs only;
                        // denying this output could break map IO; do not claim to enforce cooldown before use.
                        if(state.RecordUse(Now) && target.Handler.Message)
                            Server.PrintToChatAll($" \x01[神器] {who.PlayerName} 使用 {item.Definition.ShortName} · {target.Handler.Name} {state.Label(Now)}");
                        else if(!state.CanUse(Now))Warn(sourceRaw,$"{item.Definition.Name}: cooldown reached but original map output fired; CSS-only EntWatch cannot retroactively block the button/IO.");
                    }
                }
            }
        }
        catch(Exception error){Warn(caller.EntityHandle.Raw,"EntWatch OnOutput read failed: "+error.Message);}
        return HookResult.Continue;
    }
    private static bool TemplateRelated(string weaponName,string handlerName,bool? templated)
    {
        if(templated==false)return true;
        string a=FysEntityMatcher.TemplateSuffix(weaponName),b=FysEntityMatcher.TemplateSuffix(handlerName);
        return a.Length==0||b.Length==0 ? templated!=true : a.Equals(b,StringComparison.Ordinal);
    }
    private static float? TryFloat(CVariant value)
    {
        var t=value.FieldType.ToString();
        return t.Contains("FLOAT",StringComparison.OrdinalIgnoreCase)?value.Get<float>():
            t.Contains("INT",StringComparison.OrdinalIgnoreCase)?value.Get<int>():
            t.Contains("STRING",StringComparison.OrdinalIgnoreCase) && float.TryParse(value.Get<string>(),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)?n:null;
    }
    private static CCSPlayerController? PlayerFromActivator(CEntityInstance? activator)
    {
        if(activator is not {IsValid:true})return null;
        if(activator.DesignerName?.Equals("player",StringComparison.OrdinalIgnoreCase)==true)
        {
            var pawn=activator.As<CCSPlayerPawn>();
            var player=pawn.Controller.Value?.As<CCSPlayerController>();
            return Good(player)?player:null;
        }
        var direct=Utilities.GetPlayers().FirstOrDefault(p=>Good(p)&&p.EntityHandle.Raw==activator.EntityHandle.Raw);
        return direct;
    }
    private void Tick()
    {
        if(_disposed||!_mapActive||_loadedCount==0)return;
        try
        {
            if((Now>=_nextScan&&_dirty)||Now-_lastScan>=2)Scan();
            // Item overview deliberately uses CHAT only on explicit !items; no center or HTML HUD overwrite.
            // Passive per-item use events are chat; this respects Boss HUD/Map Translation channel ownership.
        }
        catch(Exception error){Warn(0,"EntWatch tick failed: "+error.Message);}
    }
    private void Scan()
    {
        _lastScan=Now;_nextScan=Now+2;_dirty=false;
        var found=new HashSet<uint>();
        foreach(var entity in Utilities.GetAllEntities())
        {
            if(entity is not {IsValid:true} || !entity.DesignerName.StartsWith("weapon_",StringComparison.OrdinalIgnoreCase))continue;
            try
            {
                var weapon=entity.As<CBasePlayerWeapon>();
                string id=weapon.UniqueHammerID??"";
                if(!_byWeaponId.TryGetValue(id,out var definition))continue;
                uint raw=weapon.EntityHandle.Raw;found.Add(raw);
                if(!_items.TryGetValue(raw,out var live))
                {
                    live=new(definition,raw,weapon.Entity?.Name??"");_items[raw]=live;
                    _plugin.Logger.LogInformation("ENTWATCH_BIND map={Map} hammerid={Id} weapon_handle={Handle} name={Name}",_actualMap,id,raw,definition.ShortName);
                }
            }
            catch(Exception){/* entity destroyed during enumeration */}
        }
        foreach(var raw in _items.Keys.Where(x=>!found.Contains(x)).ToArray())_items.Remove(raw);
        foreach(var item in _items.Values)
        {
            uint old=item.Owner;uint newOwner=FindInventoryOwner(item.Weapon);
            if(old==newOwner)continue;
            item.Owner=newOwner;
            CCSPlayerController? previous=Owner(old),current=Owner(newOwner);
            if(current is {IsValid:true})
            {
                item.LastOwner=newOwner;
                if(item.Definition.Message)
                    Server.PrintToChatAll($" \x01[神器] {current.PlayerName} 拾取 {item.Definition.Name}");
                // Native CanUse rejects pickups before acquisition; post-scan can only detect disallowed transfer.
                if(item.Definition.Transfer==false && old!=0 && old!=newOwner)
                    Warn(item.Weapon,$"{item.Definition.Name}: transfer=false but item owner changed. CSS-managed post-scan cannot undo pickup safely or replicate native CanUse prehook.");
            }
            else if(old!=0&&item.Definition.Message)
                Server.PrintToChatAll($" \x01[神器] {previous?.PlayerName??"玩家"} 丢下 {item.Definition.Name}");
        }
    }
    private static uint FindInventoryOwner(uint itemHandle)
    {
        foreach(var player in Utilities.GetPlayers())
        {
            if(!Good(player))continue;
            try
            {
                var weapons=player.PlayerPawn.Value?.WeaponServices?.MyWeapons;
                if(weapons!=null && weapons.Any(w=>w.Raw==itemHandle))return player.EntityHandle.Raw;
            }
            catch(Exception){ /* pawn swapped or left this frame */ }
        }
        return 0;
    }
    private void ShowItems(CCSPlayerController? player,CommandInfo command)
    {
        if(!Good(player))return;
        _plugin.Reply(player,$"本图神器配置{_loadedCount}项，已生成{_items.Count}件；HUD与Boss隔离，以下为实时列表：");
        foreach(var live in _items.Values.Where(i=>i.Definition.Ui).Take(16))
        {
            var statuses=live.Handlers.Select(s=>s.Definition.Ui?$"{s.Definition.Name}:{s.Label(Now)}":"").Where(s=>s.Length>0).ToArray();
            _plugin.Reply(player,$"{live.Definition.ShortName} — {live.Holder?.PlayerName??"无人持有"}{(statuses.Length>0?" · "+string.Join(" ",statuses):"")}");
        }
        if(_loadedCount==0)_plugin.Reply(player,"本图无EntWatch配置；需在 maps/<实际地图名>.jsonc 填写真实武器 HammerID。");
    }
    private void ShowDiagnostics(CCSPlayerController? player,CommandInfo command)
    {
        if(!_plugin.Permissions.Allows(player,"css_itemwatch")){_plugin.Reply(player,"管理员命令。");return;}
        _plugin.Reply(player,$"EntWatch map={_actualMap} 文件规则={_loadedCount} 实时武器={_items.Count} native_entwatch={ConVar.Find("entwatch_enable")?.StringValue??"n/a"}");
        foreach(var d in _document.Items.Take(24))
        {
            int count=_items.Values.Count(x=>x.Definition==d);
            _plugin.Reply(player,$"{d.ShortName}: weapon HammerID={d.HammerId} live={count}, handlers={d.Handlers.Count}, trigger={string.Join(',',d.Triggers)}");
        }
    }
    private void Warn(uint key,string text)
    {
        double monotonic=Environment.TickCount64/1000d;
        if(_warnings.TryGetValue(key,out double last) && monotonic-last<30)return;
        _warnings[key]=monotonic;_plugin.Logger.LogWarning("{Warning}",text);
    }
    public void Dispose()
    {
        if(_disposed)return;
        _disposed=true;_timer?.Kill();_timer=null;ClearMap();
        if(_initialized)
        {
            _plugin.RemoveListener<Listeners.OnEntitySpawned>(OnSpawned);
            _plugin.RemoveListener<Listeners.OnEntityDeleted>(OnDeleted);
            _plugin.RemoveListener<Listeners.OnMapEnd>(ClearMap);
            _plugin.RemoveListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
            _plugin.DeregisterEventHandler<EventItemPickup>(OnPickup,HookMode.Post);
            _plugin.DeregisterEventHandler<EventItemRemove>(OnRemove,HookMode.Post);
            _plugin.DeregisterEventHandler<EventPlayerDeath>(OnDeath,HookMode.Post);
            _plugin.RemoveZeCommand("css_items",ShowItems);
            _plugin.RemoveZeCommand("css_itemwatch",ShowDiagnostics);
        }
    }
    private sealed class LiveItem
    {
        public LiveItem(ItemDefinition definition,uint weapon,string name)
        {Definition=definition;Weapon=weapon;WeaponName=name;Handlers=definition.Handlers.Select(h=>new ItemHandlerState(h)).ToArray();}
        public ItemDefinition Definition {get;}
        public uint Weapon {get;}
        public string WeaponName {get;}
        public ItemHandlerState[] Handlers {get;}
        public uint Owner,LastOwner;
        public CCSPlayerController? Holder=>Owner==0?null:EntWatchService.Owner(Owner);
    }
}