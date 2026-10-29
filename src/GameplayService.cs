using System.Globalization;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using CssTimer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace ZePractice;

/// <summary>Local original gameplay, not FYS private source. Owns weapon routing to make limits authoritative.</summary>
public sealed partial class GameplayService : IDisposable
{
    private readonly ZePracticePlugin _plugin;
    private readonly RoundPurchaseLedger _purchases = new();
    private readonly Dictionary<string,double> _parameters = FysConfigReader.CreateDefaults();
    private readonly Dictionary<string,CommandInfo.CommandCallback> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,CommandInfo.CommandListenerCallback> _listeners = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,(string Original,string Last)> _ownedCvars = new();
    private readonly HashSet<string> _reportedCvarFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,PlayerGameplayState> _players = new(StringComparer.Ordinal);
    private readonly Dictionary<string,float> _lastPurchase = new(StringComparer.Ordinal);
    private readonly List<LocalZone> _zones = new();
    private CssTimer? _tick;
    private bool _initialized,_disposed;
    private int _generation;
    private double _nextRoutingCheck;
    public bool ExclusiveWeaponRouting => true;
    public GameplayService(ZePracticePlugin plugin) => _plugin=plugin;
    private float Setting(string key,float fallback=0) => _parameters.TryGetValue(key,out var value)?(float)value:fallback;
    private static string PlayerKey(CCSPlayerController p) => p.SteamID>0?"steam:"+p.SteamID.ToString(CultureInfo.InvariantCulture):"slot:"+p.Slot+":"+p.UserId;
    private static bool Alive(CCSPlayerController? p) => p is {IsValid:true,IsHLTV:false,PlayerPawn.IsValid:true} && p.PlayerPawn.Value is {IsValid:true,Health:>0} && p.PawnIsAlive;
    private static Vector3 Position(CCSPlayerController p) => p.PlayerPawn.Value?.AbsOrigin is {} v ? new(v.X,v.Y,v.Z):Vector3.Zero;

    public void Initialize()
    {
        if (_initialized) return;_initialized=true;
        foreach (var spec in FysConfigReader.Specifications.Values.Where(x=>x.Disposition==FysSettingDisposition.Managed || x.Name.StartsWith("ze_skill_",StringComparison.Ordinal)))
        {
            var key=spec.Name;
            _plugin.MapSettings.RegisterManagedSettingHandler(key,value=> {
                _parameters[key]=value; // Consumed by spawn, purchase, and skill mechanics below.
                // MapConfigService applies validated parameters only after a map is live.
            });
        }
        Register("css_zeweapon","中文枪械/手雷补给菜单",(p,c)=> { if(c.ArgCount>1) BuyByName(p,c.GetArg(1));else ShowWeapons(p); });
        Register("css_zebuy","购买/补给 <he|molotov|freeze|barrier|blackhole|adrenaline|枪名>",(p,c)=> { if(c.ArgCount>1)BuyByName(p,c.GetArg(1));else ShowWeapons(p); });
        Register("css_zerefill","练习：补充生命和出生补给",(p,_)=>Refill(p));
        Register("css_zeclass","选择本地僵尸职业（不是FYS私有实现）",(p,c)=> { if(c.ArgCount>1)ChooseClass(p,c.GetArg(1));else ShowClasses(p); });
        Register("css_zskill","使用选定职业技能",(p,_)=>UseSkill(p));
        var aliases=ZeGameplayCatalog.WeaponAliases.Keys.Concat(new[]{"he","hegrenade","nade","grenade","molotov","molo","inc","incendiary","incgrenade","freeze","ice","decoy","barrier","flash","flashbang","blackhole","black","smoke","smokegrenade","adrenaline","healthshot","heal","medic"}).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach(var alias in aliases)
        {
            var captured=alias;
            Register("css_"+captured,"ZE 武器/补给："+captured,(p,_)=>BuyByName(p,captured));
            Listen("c_"+captured,(p,_)=>{BuyByName(p,captured);return HookResult.Stop;});
        }
        Listen("buy",OnBuy);
        Listen("autobuy",BlockBulkBuy); Listen("rebuy",BlockBulkBuy);
        Listen("say",OnSay); Listen("say_team",OnSay);
        _plugin.RegisterEventHandler<EventDecoyStarted>(OnDecoy,HookMode.Post);
        _plugin.RegisterEventHandler<EventFlashbangDetonate>(OnBarrier,HookMode.Post);
        _plugin.RegisterEventHandler<EventSmokegrenadeDetonate>(OnBlackHole,HookMode.Post);
        // Do not create timers or read/write native ConVars while BasePlugin.Load may run before map globals exist.
        _plugin.Logger.LogInformation("ZePractice gameplay owns weapon commands/buy/autobuy/rebuy; only CS2Fixes weapon commands are disabled to prevent quota bypass. Other CS2Fixes modules remain active. Nine zombie classes are LOCAL implementations, not FYS source.");
    }
    private void Register(string command,string description,CommandInfo.CommandCallback callback)
    { _commands.Add(command,callback);_plugin.AddZeCommand(command,description,callback); }
    private void Listen(string command,CommandInfo.CommandListenerCallback callback)
    { _listeners.Add(command,callback);_plugin.AddCommandListener(command,callback,HookMode.Pre); }
    private void ClaimWeaponRouting()
    {
        SetOwnedCvar("cs2f_weapons_enable","0");
        if(_plugin.Config.EnableSpecialGrenades) SetOwnedCvar("zr_napalm_enable","1");
    }
    private bool SetOwnedCvar(string name,string value)
    {
        try
        {
            var c=ConVar.Find(name);
            if(c is null)
            {
                if(_reportedCvarFailures.Add(name)) _plugin.Logger.LogWarning("Gameplay ConVar is absent; setting NOT applied: {Name}",name);
                return false;
            }
            var before=c.StringValue;
            var original=_ownedCvars.TryGetValue(name,out var old)?old.Original:before;
            if(!EquivalentCvar(before,value))c.StringValue=value;
            var actual=c.StringValue;
            if(!EquivalentCvar(actual,value))
            {
                if(_reportedCvarFailures.Add(name)) _plugin.Logger.LogWarning("Gameplay ConVar {Name}: requested {Value}, read back {Actual}; setting NOT applied.",name,value,actual);
                return false;
            }
            _reportedCvarFailures.Remove(name);
            _ownedCvars[name]=(original,actual);
            return true;
        }
        catch(Exception ex)
        {
            if(_reportedCvarFailures.Add(name)) _plugin.Logger.LogWarning(ex,"Gameplay ConVar {Name} native get/set failed; setting NOT applied.",name);
            return false;
        }
    }
    private static bool EquivalentCvar(string actual,string requested)
    {
        if (string.Equals(actual,requested,StringComparison.OrdinalIgnoreCase)) return true;
        if (requested is "0" or "1" && bool.TryParse(actual,out var enabled)) return enabled == (requested == "1");
        return false;
    }    public void OnMapStart(string map)
    {
        // An OnMapStart callback may be earlier than a usable player list; old pawns are gone.
        _zones.Clear();_generation++;_purchases.Reset();_players.Clear();_lastPurchase.Clear();_reportedCvarFailures.Clear();_nextRoutingCheck=0;
        if(_tick is null) _tick=_plugin.AddTimer(0.1f,Tick,TimerFlags.REPEAT);
        // Native ConVar writes are deferred until the first simulated frame.
    }
    public void OnRoundStart()
    {
        ResetEffects();_generation++;_purchases.Reset();_lastPurchase.Clear();
        foreach(var state in _players.Values){state.SkillReadyAt=0;state.MaxHealthSeen=0;}
        ClaimWeaponRouting();
    }
    public void OnPlayerSpawn(CCSPlayerController player)
    {
        if(!ZePracticePlugin.IsRealPlayer(player))return;
        var generation=_generation;var user=player.UserId;
        _plugin.AddTimer(0.25f,()=> {
            if(_disposed || generation!=_generation || !Alive(player) || player.UserId!=user)return;
            try
            {
                var pawn=player.PlayerPawn.Value!;var state=GetState(player);state.MaxHealthSeen=Math.Max(pawn.Health,pawn.MaxHealth);
                if(player.TeamNum==2)return;
                if(!_plugin.IsPractice && player.TeamNum!=3)return;
                if(_purchases.ClaimSpawnSupply(PlayerKey(player)))SupplySpawnGrenades(player);
                if(_plugin.IsPractice && !HasPrimary(player))
                {
                    var weapon=ZeGameplayCatalog.FindWeapon(_plugin.Config.DefaultPrimaryWeapon);
                    if(weapon is not null)player.GiveNamedItem<CCSWeaponBase>(weapon);
                }
            }
            catch(Exception ex){_plugin.Logger.LogWarning(ex,"Could not finish delayed ZE spawn supply");}
        },TimerFlags.STOP_ON_MAPCHANGE);
    }
    private void SupplySpawnGrenades(CCSPlayerController player)
    {
        foreach(var definition in ZeGameplayCatalog.Supplies.Where(x=>x.SpawnSetting is not null))
        {
            var desired=(int)Setting(definition.SpawnSetting!);
            while(CountGrenade(player,definition)<desired)
                if(!GrantGrenade(player,definition))break;
        }
    }
    private void Refill(CCSPlayerController? p)
    {
        if(!_plugin.NeedPermission(p,"css_zerefill"))return;
        if(!NeedHuman(p))return;
        if(!_plugin.IsPractice)
        {_plugin.Reply(p,"自由补给仅在允许跑图辅助的练习模式使用。");return;}
        if(!Throttle(p!))return;
        var pawn=p!.PlayerPawn.Value!;var health=p.TeamNum==2?pawn.MaxHealth:Math.Min(pawn.MaxHealth,_plugin.Config.PublicGameplay.HumanHealthLimit);
        pawn.Health=health;Utilities.SetStateChanged(pawn,"CBaseEntity","m_iHealth");SupplySpawnGrenades(p);
        _plugin.Reply(p,"生命及本图出生补给已补满；普通购买次数仍按本回合记录。");
    }
    public void ShowWeapons(CCSPlayerController? p)
    {
        if(!_plugin.NeedPermission(p,"css_zebuy"))return;
        if(!NeedHuman(p))return;
        var menu=new ChatMenu("ZE枪械/补给（练习免费 / 多人游戏金钱）");
        foreach(var def in ZeGameplayCatalog.Supplies)
        { var d=def;var limit=(int)Setting(d.RoundSetting);var used=_purchases.Used(PlayerKey(p!),d.Kind);menu.AddMenuOption($"{d.Label} {(limit<0?"不限":used+"/"+limit)} ${(_plugin.IsPractice?0:d.Price)}",(player,_)=>BuySupply(player,d),limit==0); }
        foreach(var alias in new[]{"bizon","p90","ak","m4","awp","negev","deagle"})
        {var a=alias;menu.AddMenuOption("枪械："+a,(player,_)=>BuyByName(player,a));}
        menu.AddMenuOption("更多枪械：!zebuy 枪名",(player,_)=>_plugin.Reply(player,"例如 !zebuy mp9 / aug / scar20 / usp。使用当前游戏武器实体，不自动删除地图神器。"));
        _plugin.OpenMenu(p!,menu);
    }
    private bool NeedHuman(CCSPlayerController? p)
    {
        if(!ZePracticePlugin.IsRealPlayer(p)||!Alive(p)){_plugin.Reply(p,"需要存活玩家在游戏内使用。");return false;}
        if(!ZombieWeaponPolicy.CanBuy(p!.TeamNum) && !_plugin.ZombieGearEnabled(p)){_plugin.Reply(p,"僵尸只能使用刀，禁止购买枪械和补给；可先用 !ze_zombiegear on 允许自己的僵尸购枪，或切换 CT。");return false;}
        return true;
    }
    private bool Throttle(CCSPlayerController p)
    {
        var key=PlayerKey(p);var now=Server.CurrentTime;
        if(_lastPurchase.TryGetValue(key,out var before)&&now-before<0.2f)return false;
        _lastPurchase[key]=now;return true;
    }
    private void BuyByName(CCSPlayerController? p,string input)
    {
        if(!_plugin.NeedPermission(p,"css_zebuy"))return;
        if(!NeedHuman(p))return;
        if(ZeGameplayCatalog.FindSupply(input) is {} supply){BuySupply(p!,supply);return;}
        var weapon=ZeGameplayCatalog.FindWeapon(input);if(weapon is null){_plugin.Reply(p,"未知武器/补给。输入 !zeweapon 查看菜单。");return;}
        if(!Throttle(p!))return;
        BuyGun(p!,weapon);
    }
    private void BuySupply(CCSPlayerController p,ZeSupplyDefinition definition)
    {
        if(!_plugin.NeedPermission(p,"css_zebuy"))return;
        if(!NeedHuman(p)||!Throttle(p))return;
        var key=PlayerKey(p);var limit=(int)Setting(definition.RoundSetting);
        using var reservation=_purchases.TryReserve(key,definition.Kind,limit);
        if(reservation is null){_plugin.Reply(p,limit==0?"本图禁止购买此补给。":"本回合该补给购买次数已用完。");return;}
        var price=_plugin.IsPractice?0:definition.Price;
        if(!CanPay(p,price)){_plugin.Reply(p,$"游戏金钱不足，需要 ${price}。");return;}
        try
        {
            var granted=definition.Kind==ZeSupply.Adrenaline?UseAdrenaline(p):GrantGrenade(p,definition);
            if(!granted){_plugin.Reply(p,"补给未发放（已达携带上限或实体创建失败），没有扣款或消耗次数。");return;}
            Pay(p,price);reservation.Commit();
            _plugin.Reply(p,$"{definition.Label}已发放/生效，本回合已购 {_purchases.Used(key,definition.Kind)}{(limit<0?"":"/"+limit)}。");
        }
        catch(Exception ex){_plugin.Logger.LogWarning(ex,"Supply grant failed");_plugin.Reply(p,"发放失败；购买计数已回退。");}
    }
    private static bool CanPay(CCSPlayerController p,int amount)=>amount==0||(p.InGameMoneyServices is {} money&&money.Account>=amount);
    private static void Pay(CCSPlayerController p,int amount)
    { if(amount==0)return;var money=p.InGameMoneyServices??throw new InvalidOperationException("Player money service disappeared.");money.Account=Math.Max(0,money.Account-amount);Utilities.SetStateChanged(p,"CCSPlayerController","m_pInGameMoneyServices"); }
    private static IEnumerable<CCSWeaponBase> Weapons(CCSPlayerController p)
    {var services=p.PlayerPawn.Value?.WeaponServices;if(services is null)yield break;foreach(var handle in services.MyWeapons){if(handle.Value is {IsValid:true} weapon)yield return weapon.As<CCSWeaponBase>();}}
    private static bool HasPrimary(CCSPlayerController p)=>Weapons(p).Any(w=>w.VData?.GearSlot==gear_slot_t.GEAR_SLOT_RIFLE);
    private void BuyGun(CCSPlayerController p,string entityName)
    {
        try
        {
            if(!ZeGameplayCatalog.GunOffers.TryGetValue(entityName,out var offer)) {_plugin.Reply(p,"此枪不在本服白名单。");return;}
            var price=_plugin.IsPractice?0:offer.Price;
            var targetSlot=offer.Primary?gear_slot_t.GEAR_SLOT_RIFLE:gear_slot_t.GEAR_SLOT_PISTOL;
            if(Weapons(p).Any(x=>x.VData?.GearSlot==targetSlot)) {_plugin.Reply(p,"该武器槽已有武器。请先丢出旧枪；插件不会删除可能属于地图神器的武器。");return;}
            if(!CanPay(p,price)){_plugin.Reply(p,$"游戏金钱不足，需要 ${price}。");return;}
            var granted=p.GiveNamedItem<CCSWeaponBase>(entityName);
            if(granted is not {IsValid:true}){_plugin.Reply(p,"武器实体发放失败，没有扣款。");return;}
            _plugin.TrackIssuedGun(granted);
            Pay(p,price);_plugin.Reply(p,$"已获得 {entityName[7..]}，花费 ${price} 游戏金钱。");
        }
        catch(Exception ex){_plugin.Logger.LogWarning(ex,"Weapon grant failed");_plugin.Reply(p,"枪械发放失败，详情见服务端日志。");}
    }
    private static CCSWeaponBase? FindGrenade(CCSPlayerController p,ZeSupplyDefinition d)=>Weapons(p).FirstOrDefault(w=>w.DesignerName==d.EntityName||(d.Kind==ZeSupply.Molotov&&w.DesignerName=="weapon_incgrenade"));
    private static int CountGrenade(CCSPlayerController p,ZeSupplyDefinition d)
    {var w=FindGrenade(p,d);var services=p.PlayerPawn.Value?.WeaponServices;var ammo=w?.VData?.PrimaryAmmoType;return services is not null&&ammo.HasValue&&ammo.Value<services.Ammo.Length?services.Ammo[ammo.Value]:0;}
    private static bool GrantGrenade(CCSPlayerController p,ZeSupplyDefinition d)
    {
        var before=CountGrenade(p,d);if(before>=2)return false;
        var weapon=FindGrenade(p,d)??p.GiveNamedItem<CCSWeaponBase>(d.EntityName);
        var pawn=p.PlayerPawn.Value;var services=pawn?.WeaponServices;var ammo=weapon?.VData?.PrimaryAmmoType;
        if(weapon is not {IsValid:true}||services is null||!ammo.HasValue||ammo.Value>=services.Ammo.Length)return false;
        services.Ammo[ammo.Value]=(ushort)(before+1);Utilities.SetStateChanged(pawn!,"CBasePlayerPawn","m_pWeaponServices");
        return CountGrenade(p,d)>before;
    }
    private HookResult OnBuy(CCSPlayerController? p,CommandInfo c)
    {
        if(p is { IsValid: true, TeamNum: 2 } && !_plugin.ZombieGearEnabled(p)){_plugin.Reply(p,"僵尸只能使用刀，禁止购买枪械和补给；可先用 !ze_zombiegear on 允许自己的僵尸购枪。");return HookResult.Stop;}
        if(c.ArgCount<2||ZeGameplayCatalog.FindSupply(c.GetArg(1)) is null)return HookResult.Continue;
        BuyByName(p,c.GetArg(1));return HookResult.Stop;
    }
    private HookResult BlockBulkBuy(CCSPlayerController? p,CommandInfo c)
    {_plugin.Reply(p,"为保持地图购买限额一致，自动购买/重新购买请改用 !zeweapon 或单件购买。");return HookResult.Stop;}
    private HookResult OnSay(CCSPlayerController? p,CommandInfo c)
    {
        var text=c.GetArg(1).Trim();if(text.Length<2||text[0] is not ('!' or '/'))return HookResult.Continue;
        var name=text[1..].Split(' ',StringSplitOptions.RemoveEmptyEntries)[0];
        if(ZeGameplayCatalog.FindSupply(name) is null&&ZeGameplayCatalog.FindWeapon(name) is null)return HookResult.Continue;
        BuyByName(p,name);return HookResult.Stop;
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;_generation++;ResetEffects();_tick?.Kill();
        foreach(var (key,handler) in _commands)_plugin.RemoveZeCommand(key,handler);
        foreach(var (key,handler) in _listeners)_plugin.RemoveCommandListener(key,handler,HookMode.Pre);
        if(_initialized){_plugin.DeregisterEventHandler<EventDecoyStarted>(OnDecoy,HookMode.Post);_plugin.DeregisterEventHandler<EventFlashbangDetonate>(OnBarrier,HookMode.Post);_plugin.DeregisterEventHandler<EventSmokegrenadeDetonate>(OnBlackHole,HookMode.Post);}
        foreach(var (key,state) in _ownedCvars)
        {
            try
            {
                var c=ConVar.Find(key);
                if(c is not null&&c.StringValue==state.Last)c.StringValue=state.Original;
            }
            catch(Exception ex){_plugin.Logger.LogWarning(ex,"Could not restore owned gameplay ConVar {Name}",key);}
        }
        _commands.Clear();_listeners.Clear();_ownedCvars.Clear();_reportedCvarFailures.Clear();_players.Clear();_zones.Clear();
    }
}
