using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;

namespace ZePractice;

public sealed partial class GameplayService
{
    private sealed class PlayerGameplayState
    {
        public string SelectedClass="hunter";
        public float SkillReadyAt;
        public int MaxHealthSeen;
        public IntPtr SlowedPawn;
        public float OriginalVelocityModifier=1f, SlowedUntil;
        public IntPtr BoostPawn;
        public float OriginalBoost=1f, BoostUntil;
        public float AdrenalineUntil;
    }
    private sealed record LocalZone(ZeSupply Kind,Vector3 Origin,int OwnerSlot,float EndsAt,int Generation);
    private PlayerGameplayState GetState(CCSPlayerController p)
    {var key=PlayerKey(p);if(!_players.TryGetValue(key,out var s))_players[key]=s=new();return s;}
    private void ResetEffects()
    {
        foreach(var p in Utilities.GetPlayers().Where(ZePracticePlugin.IsRealPlayer))
        {
            if(_players.TryGetValue(PlayerKey(p),out var state)&&p.PlayerPawn.Value is {IsValid:true} pawn)RestoreEffects(pawn,state);
        }
        _zones.Clear();
    }
    private static void RestoreEffects(CCSPlayerPawn pawn,PlayerGameplayState state)
    {
        if(state.SlowedPawn==pawn.Handle) pawn.VelocityModifier=state.OriginalVelocityModifier;
        if(state.BoostPawn==pawn.Handle && state.SlowedPawn!=pawn.Handle) pawn.VelocityModifier=state.OriginalBoost;
        state.SlowedPawn=IntPtr.Zero;state.BoostPawn=IntPtr.Zero;state.SlowedUntil=state.BoostUntil=state.AdrenalineUntil=0;
    }
    private bool UseAdrenaline(CCSPlayerController p)
    {
        if(p.PlayerPawn.Value is not {IsValid:true} pawn)return false;
        var state=GetState(p);var health=Math.Min(100,Math.Max(state.MaxHealthSeen,pawn.MaxHealth));
        pawn.Health=ZeSkillMath.Heal(pawn.Health,35,health);Utilities.SetStateChanged(pawn,"CBaseEntity","m_iHealth");
        state.AdrenalineUntil=Server.CurrentTime+8;
        _plugin.Reply(p,"肾上腺素：回血并临时加速 8 秒。");return true;
    }
    private HookResult OnDecoy(EventDecoyStarted ev,GameEventInfo info)
    {
        if(!_plugin.Config.EnableSpecialGrenades||_plugin.IsPractice||ev.Userid is not {TeamNum:3})return HookResult.Continue;
        _zones.Add(new(ZeSupply.Freeze,new(ev.X,ev.Y,ev.Z),ev.Userid.Slot,Server.CurrentTime+5,_generation));return HookResult.Continue;
    }
    private HookResult OnBarrier(EventFlashbangDetonate ev,GameEventInfo info)
    {
        if(!_plugin.Config.EnableSpecialGrenades||_plugin.IsPractice||ev.Userid is not {TeamNum:3})return HookResult.Continue;
        _zones.Add(new(ZeSupply.Barrier,new(ev.X,ev.Y,ev.Z),ev.Userid.Slot,Server.CurrentTime+4,_generation));return HookResult.Continue;
    }
    private HookResult OnBlackHole(EventSmokegrenadeDetonate ev,GameEventInfo info)
    {
        if(!_plugin.Config.EnableSpecialGrenades||_plugin.IsPractice||ev.Userid is not {TeamNum:3})return HookResult.Continue;
        _zones.Add(new(ZeSupply.BlackHole,new(ev.X,ev.Y,ev.Z),ev.Userid.Slot,Server.CurrentTime+9,_generation));return HookResult.Continue;
    }
    private void Tick()
    {
        if(_disposed)return;
        var now=Server.CurrentTime;
        if(now>=_nextRoutingCheck){ClaimWeaponRouting();_nextRoutingCheck=now+2;}
        _zones.RemoveAll(x=>x.Generation!=_generation||x.EndsAt<now);
        foreach(var p in Utilities.GetPlayers().Where(ZePracticePlugin.IsRealPlayer))
        {
            if(!Alive(p)||p.PlayerPawn.Value is not {IsValid:true} pawn)continue;
            var state=GetState(p);
            if(state.SlowedPawn!=IntPtr.Zero && (state.SlowedPawn!=pawn.Handle||state.SlowedUntil<now))
            {if(state.SlowedPawn==pawn.Handle)pawn.VelocityModifier=state.OriginalVelocityModifier;state.SlowedPawn=IntPtr.Zero;}
            if(state.BoostPawn!=IntPtr.Zero && (state.BoostPawn!=pawn.Handle||state.BoostUntil<now))
            {if(state.BoostPawn==pawn.Handle && state.SlowedPawn==IntPtr.Zero)pawn.VelocityModifier=state.OriginalBoost;state.BoostPawn=IntPtr.Zero;}
            if(state.AdrenalineUntil>now && p.TeamNum==3)Boost(p,1.15f,now+0.25f);
            if(_plugin.IsPractice||p.TeamNum!=2)continue;
            foreach(var z in _zones)
            {
                var pos=Position(p);if(Vector3.DistanceSquared(pos,z.Origin)>40000)continue;
                if(z.Kind==ZeSupply.Freeze)Slow(p,0.28f,now+0.35f);
                else if(z.Kind==ZeSupply.BlackHole) Pull(p,z.Origin,350);
                else if(z.Kind==ZeSupply.Barrier)Push(p,z.Origin,600);
            }
        }
    }
    private void Slow(CCSPlayerController p,float multiplier,float until)
    {
        var pawn=p.PlayerPawn.Value;if(pawn is not {IsValid:true})return;
        var state=GetState(p);
        if(state.SlowedPawn!=pawn.Handle) {state.SlowedPawn=pawn.Handle;state.OriginalVelocityModifier=pawn.VelocityModifier;}
        state.SlowedUntil=Math.Max(state.SlowedUntil,until);pawn.VelocityModifier=Math.Min(state.OriginalVelocityModifier,multiplier);
    }
    private void Boost(CCSPlayerController p,float multiplier,float until)
    {
        var pawn=p.PlayerPawn.Value;if(pawn is not {IsValid:true})return;
        var state=GetState(p);
        if(state.BoostPawn!=pawn.Handle) {state.BoostPawn=pawn.Handle;state.OriginalBoost=pawn.VelocityModifier;}
        state.BoostUntil=Math.Max(state.BoostUntil,until);
        if(state.SlowedPawn!=pawn.Handle)pawn.VelocityModifier=Math.Max(state.OriginalBoost,multiplier);
    }
    private static void Pull(CCSPlayerController p,Vector3 center,float speed)
    {var pawn=p.PlayerPawn.Value;if(pawn is null)return;pawn.Teleport(velocity:ZeSkillMath.PullVelocity(Position(p),center,speed));}
    private static void Push(CCSPlayerController p,Vector3 center,float speed)
    {var pawn=p.PlayerPawn.Value;if(pawn is null)return;pawn.Teleport(velocity:ZeSkillMath.PullVelocity(center,Position(p),speed));}
    private void ShowClasses(CCSPlayerController? p)
    {
        if(!_plugin.NeedPermission(p,"css_zeclass"))return;
        if(!ZePracticePlugin.IsRealPlayer(p)){_plugin.Reply(p,"进入游戏后使用 !zeclass。 ");return;}
        var menu=new CounterStrikeSharp.API.Modules.Menu.ChatMenu("本地僵尸职业（非FYS源码）");
        foreach(var (key,label) in ZeGameplayCatalog.Classes){var selected=key;menu.AddMenuOption(label,(player,_)=>ChooseClass(player,selected));}
        _plugin.OpenMenu(p!,menu);
    }
    private void ChooseClass(CCSPlayerController? p,string name)
    {
        if(!_plugin.NeedPermission(p,"css_zeclass"))return;
        if(!ZePracticePlugin.IsRealPlayer(p))return;
        if(!ZeGameplayCatalog.Classes.ContainsKey(name)){_plugin.Reply(p,"未知职业；!zeclass 查看列表。");return;}
        var state=GetState(p!);state.SelectedClass=name;state.SkillReadyAt=0;
        _plugin.Reply(p,"已选职业："+ZeGameplayCatalog.Classes[name]+"；T队可使用 !zskill (冷却30秒)。");
    }
    private void UseSkill(CCSPlayerController? p)
    {
        if(!_plugin.NeedPermission(p,"css_zskill"))return;
        if(_plugin.IsPractice||!Alive(p)||p!.TeamNum!=2){_plugin.Reply(p,"僵尸职业技能仅在多人ZE僵尸队生效。");return;}
        var state=GetState(p);var now=Server.CurrentTime;
        if(now<state.SkillReadyAt){_plugin.Reply(p,$"技能冷却还需 {Math.Ceiling(state.SkillReadyAt-now)} 秒。");return;}
        var pawn=p.PlayerPawn.Value!;var origin=Position(p);
        var angle=pawn.EyeAngles;var forward=ZeSkillMath.Forward(angle.X,angle.Y);
        var humans=Utilities.GetPlayers().Where(x=>Alive(x)&&x.TeamNum==3).ToArray();
        bool used=false;
        switch(state.SelectedClass)
        {
            case "hunter": pawn.Teleport(velocity:forward*Setting("ze_skill_hunter_power",180));used=true;break;
            case "faster": Boost(p,Setting("ze_skill_faster_speed",1.4f),now+6);used=true;break;
            case "blader":
                foreach(var h in humans.Where(x=>ZeSkillMath.InCone(origin,forward,Position(x),110,0.5f)))used|=Damage(h,(int)Setting("ze_skill_blader_damage",64));break;
            case "deimos":
                foreach(var h in humans.Where(x=>ZeSkillMath.InCone(origin,forward,Position(x),600,0.5f)).OrderBy(x=>Vector3.DistanceSquared(origin,Position(x))).Take((int)Setting("ze_skill_deimos_amount",5)))
                {Slow(h,0.55f,now+3);used|=Damage(h,12);}break;
            case "yaksha":
                foreach(var h in humans.Where(x=>Vector3.DistanceSquared(origin,Position(x))<=22500))used|=Damage(h,(int)Setting("ze_skill_yaksha_damage",2)*8);break;
            case "dancer":
                foreach(var h in humans.Where(x=>Vector3.DistanceSquared(origin,Position(x))<=90000)) {Slow(h,Setting("ze_skill_dancer_speed",0.6f),now+5);used=true;}break;
            case "farter":pawn.Health=ZeSkillMath.Heal(pawn.Health,(int)Setting("ze_skill_farter_regen",5000),Math.Max(1,pawn.MaxHealth));Utilities.SetStateChanged(pawn,"CBaseEntity","m_iHealth");used=true;break;
            case "cirrus":
                foreach(var h in humans.Where(x=>ZeSkillMath.InCone(origin,forward,Position(x),Setting("ze_skill_cirrus_range",128),0.75f)).Take(1))used|=Damage(h,30);break;
            case "smoker":
                foreach(var h in humans.Where(x=>ZeSkillMath.InCone(origin,forward,Position(x),Setting("ze_skill_smoker_range",500),0.91f)).Take(1)) {Pull(h,origin,650);used=true;}break;
        }
        if(!used){_plugin.Reply(p,"技能未命中有效目标，未计入冷却。");return;}
        state.SkillReadyAt=now+30;
        _plugin.Reply(p,"技能已施放："+state.SelectedClass);
    }
    private static bool Damage(CCSPlayerController p,int damage)
    {
        if(p.PlayerPawn.Value is not {IsValid:true} pawn||damage<=0)return false;
        if(damage>=pawn.Health) pawn.CommitSuicide(false,true); // Locally implemented damage: no FYS kill attribution.
        else {pawn.Health-=damage;Utilities.SetStateChanged(pawn,"CBaseEntity","m_iHealth");}
        return true;
    }
}
