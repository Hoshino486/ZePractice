using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

public sealed partial class EquipNativeProbe : BasePlugin
{
    public override string ModuleName => "EquipNativeProbe - isolated 27025 only";
    public override string ModuleVersion => "1.1";
    private readonly List<object> results = [];
    private CBaseEntity? equip;
    private static void Guard() {
        if (ConVar.Find("hostport")?.GetPrimitiveValue<int>() != 27025 ||
            !Server.GameDirectory.Contains("ZePracticeTest-20261016") ||
            Utilities.GetPlayers().Any(p => !p.IsBot && !p.IsHLTV)) throw new Exception("Isolated server without humans only");
    }
    private void Record(string step, object data) {
        results.Add(new { step, data });
        File.WriteAllText(Path.Combine(ModuleDirectory,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions {WriteIndented=true}));
        Server.PrintToConsole("EQUIP_PROBE " + JsonSerializer.Serialize(new {step,data}));
    }
    public override void Load(bool hotReload) {
        RegisterSuite();
        AddCommand("css_equip_probe", "Equipment ABI reproduction", (p,c) => {
            if (p != null) return; Guard();
            var action=c.GetArg(1);
            if (action=="create") {
                results.Clear(); if(equip?.IsValid==true)equip.Remove();
                string cls=c.ArgCount>2?c.GetArg(2):"game_player_equip";
                if(cls is not("game_player_equip" or "game_weapon_equip"))throw new Exception("Unexpected class");
                Record("before-create",new {cls});
                equip=Utilities.CreateEntityByName<CBaseEntity>(cls);
                if(equip?.IsValid!=true){equip=null;Record("unknown-class",new {cls});return;}
                using var kv=new CEntityKeyValues();
                kv.SetString("targetname","ze_equip_regression");kv.SetString("hammerUniqueId","ze_equip_regression");
                kv.SetString("weapon0","weapon_ak47");kv.SetString("weapon1","weapon_deagle");
                kv.SetInt("spawnflags",c.ArgCount>3?int.Parse(c.GetArg(3)):0);
                Record("before-spawn",new {equip.Index});
                equip.DispatchSpawn(kv);Record("spawned",new {equip.Index,equip.DesignerName});
            } else if(action=="status") {
                Record("status",Utilities.GetPlayers().Where(p=>p.IsBot).Select(p=>new {p.Slot,p.TeamNum,p.PawnIsAlive,items=p.PlayerPawn.Value?.WeaponServices?.MyWeapons.Select(w=>w.Value?.DesignerName).ToArray()}));
            } else {
                if(equip?.IsValid!=true)throw new Exception("Create equip first");
                if(action is not("Use" or "TriggerForActivatedPlayer" or "TriggerForAllPlayers"))throw new Exception("Unexpected input");
                var pawn=Utilities.GetPlayers().FirstOrDefault(p=>p.IsBot && p.TeamNum==3 && p.PawnIsAlive)?.PlayerPawn.Value;
                if(pawn==null)throw new Exception("Need a live CT bot");
                string value=c.ArgCount>2?c.GetArg(2):"";
                Record("before-input",new {action,value,pawn= pawn.Index});
                equip.AcceptInput(action,pawn,pawn,value);
                Record("after-input",new {action,items=pawn.WeaponServices?.MyWeapons.Select(w=>w.Value?.DesignerName).ToArray()});
            }
        });
    }
}
