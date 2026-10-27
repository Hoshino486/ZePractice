using System.Numerics;
namespace ZePractice;

public enum ZeSupply { He, Molotov, Freeze, Barrier, BlackHole, Adrenaline }
public sealed record ZeSupplyDefinition(ZeSupply Kind, string Alias, string Label, string EntityName, string RoundSetting, string? SpawnSetting, int Price);
public static class ZeGameplayCatalog
{
    public static readonly IReadOnlyList<ZeSupplyDefinition> Supplies = new[] {
        new ZeSupplyDefinition(ZeSupply.He,"he","高爆/燃烧雷","weapon_hegrenade","ze_weapons_round_hegrenade","ze_weapons_spawn_hegrenade",300),
        new ZeSupplyDefinition(ZeSupply.Molotov,"molotov","燃烧瓶","weapon_molotov","ze_weapons_round_molotov","ze_weapons_spawn_molotov",400),
        new ZeSupplyDefinition(ZeSupply.Freeze,"freeze","冰冻雷","weapon_decoy","ze_weapons_round_decoy","ze_weapons_spawn_decoy",300),
        new ZeSupplyDefinition(ZeSupply.Barrier,"barrier","排斥屏障雷","weapon_flashbang","ze_weapons_round_flash",null,400),
        new ZeSupplyDefinition(ZeSupply.BlackHole,"blackhole","牵引黑洞雷","weapon_smokegrenade","ze_weapons_round_smoke",null,500),
        new ZeSupplyDefinition(ZeSupply.Adrenaline,"adrenaline","肾上腺素","","ze_weapons_round_adrenaline",null,500)
    };
    public static readonly IReadOnlyDictionary<string, string> WeaponAliases = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
        ["ak"]="weapon_ak47",["ak47"]="weapon_ak47",["m4"]="weapon_m4a1",["m4a1"]="weapon_m4a1",["m4s"]="weapon_m4a1_silencer",["m4a1_silencer"]="weapon_m4a1_silencer",
        ["bizon"]="weapon_bizon",["p90"]="weapon_p90",["negev"]="weapon_negev",["m249"]="weapon_m249",["awp"]="weapon_awp",["scout"]="weapon_ssg08",["ssg08"]="weapon_ssg08",
        ["aug"]="weapon_aug",["sg553"]="weapon_sg556",["sg556"]="weapon_sg556",["g3sg1"]="weapon_g3sg1",["scar20"]="weapon_scar20",["mp9"]="weapon_mp9",["mac10"]="weapon_mac10",["ump45"]="weapon_ump45",["mp7"]="weapon_mp7",["mp5sd"]="weapon_mp5sd",
        ["galil"]="weapon_galilar",["galilar"]="weapon_galilar",["famas"]="weapon_famas",["nova"]="weapon_nova",["xm1014"]="weapon_xm1014",["mag7"]="weapon_mag7",["sawedoff"]="weapon_sawedoff",
        ["glock"]="weapon_glock",["usp"]="weapon_usp_silencer",["usp_silencer"]="weapon_usp_silencer",["p2000"]="weapon_hkp2000",["hkp2000"]="weapon_hkp2000",["p250"]="weapon_p250",["tec9"]="weapon_tec9",["fiveseven"]="weapon_fiveseven",
        ["deagle"]="weapon_deagle",["revolver"]="weapon_revolver",["elite"]="weapon_elite",["cz75a"]="weapon_cz75a"
    };
    public static ZeSupplyDefinition? FindSupply(string input)
    {
        var s = input.Trim().ToLowerInvariant();
        if (s.StartsWith("weapon_")) s=s[7..];
        s = s switch { "hegrenade" or "nade" or "grenade" => "he", "molo" or "inc" or "incendiary" or "incgrenade" => "molotov",
            "decoy" or "ice" => "freeze", "flashbang" or "flash" => "barrier", "smokegrenade" or "smoke" or "black" => "blackhole",
            "healthshot" or "heal" or "medic" => "adrenaline", _ => s };
        return Supplies.FirstOrDefault(x=>x.Alias.Equals(s,StringComparison.OrdinalIgnoreCase));
    }
    public static string? FindWeapon(string input)
    {
        if (input.StartsWith("weapon_",StringComparison.OrdinalIgnoreCase)) input=input[7..];
        return WeaponAliases.GetValueOrDefault(input);
    }
    // Local server pricing, not a claim about a constantly changing CS2 retail economy.
    // Every allowed gun is enumerated: unrecognized entities cannot be requested via chat/console input.
    public sealed record GunOffer(int Price,bool Primary);
    public static readonly IReadOnlyDictionary<string,GunOffer> GunOffers = new Dictionary<string,GunOffer>(StringComparer.OrdinalIgnoreCase) {
        ["weapon_ak47"]=new(2700,true),["weapon_m4a1"]=new(3100,true),["weapon_m4a1_silencer"]=new(2900,true),
        ["weapon_bizon"]=new(1400,true),["weapon_p90"]=new(2350,true),["weapon_negev"]=new(1700,true),["weapon_m249"]=new(5200,true),
        ["weapon_awp"]=new(4750,true),["weapon_ssg08"]=new(1700,true),["weapon_aug"]=new(3300,true),["weapon_sg556"]=new(3000,true),
        ["weapon_g3sg1"]=new(5000,true),["weapon_scar20"]=new(5000,true),["weapon_mp9"]=new(1250,true),["weapon_mac10"]=new(1050,true),
        ["weapon_ump45"]=new(1200,true),["weapon_mp7"]=new(1500,true),["weapon_mp5sd"]=new(1500,true),["weapon_galilar"]=new(1800,true),
        ["weapon_famas"]=new(2050,true),["weapon_nova"]=new(1050,true),["weapon_xm1014"]=new(2000,true),["weapon_mag7"]=new(1300,true),
        ["weapon_sawedoff"]=new(1100,true),["weapon_glock"]=new(200,false),["weapon_usp_silencer"]=new(200,false),
        ["weapon_hkp2000"]=new(200,false),["weapon_p250"]=new(300,false),["weapon_tec9"]=new(500,false),
        ["weapon_fiveseven"]=new(500,false),["weapon_deagle"]=new(700,false),["weapon_revolver"]=new(600,false),
        ["weapon_elite"]=new(300,false),["weapon_cz75a"]=new(500,false)
    };
    public static readonly IReadOnlyDictionary<string, string> Classes = new Dictionary<string,string> {
        ["hunter"]="猎手：向准星方向冲刺",["faster"]="迅捷：短时加速",["blader"]="刀锋：近距离斩击",["deimos"]="恶魔：连锁减速伤害",
        ["yaksha"]="赤焰：近身火焰区域",["dancer"]="舞王：减速区域",["farter"]="再生：恢复生命",["cirrus"]="长手：延伸距离攻击",["smoker"]="钩子：向自己牵引目标"
    };
}

/// <summary>Main-thread transactional accounting; failed entity grants never consume the purchase quota.</summary>
public sealed class RoundPurchaseLedger
{
    private readonly Dictionary<(string Player,ZeSupply Kind), int> _counts = new();
    private readonly HashSet<string> _spawned = new(StringComparer.Ordinal);
    private int _generation;
    public int Used(string player, ZeSupply kind) => _counts.GetValueOrDefault((player,kind));
    public void Reset() { _generation++; _counts.Clear(); _spawned.Clear(); }
    public bool ClaimSpawnSupply(string player) => _spawned.Add(player);
    public PurchaseReservation? TryReserve(string player, ZeSupply kind, int limit)
    {
        if (limit < -1 || limit == 0 || (limit > 0 && Used(player,kind) >= limit)) return null;
        var key=(player,kind); _counts[key]=checked(Used(player,kind)+1);
        return new PurchaseReservation(this,key,_generation);
    }
    public sealed class PurchaseReservation : IDisposable
    {
        private RoundPurchaseLedger? _owner; private readonly (string Player,ZeSupply Kind) _key; private readonly int _generation; private bool _committed;
        internal PurchaseReservation(RoundPurchaseLedger owner,(string Player,ZeSupply Kind) key,int generation) { _owner=owner;_key=key;_generation=generation; }
        public void Commit() => _committed=true;
        public void Dispose() { if (_owner is {} owner && !_committed && owner._generation==_generation) owner._counts[_key]=Math.Max(0,owner._counts.GetValueOrDefault(_key)-1); _owner=null; }
    }
}

public static class ZeSkillMath
{
    public static Vector3 Forward(float pitchDegrees, float yawDegrees)
    {
        var p=pitchDegrees*MathF.PI/180;var y=yawDegrees*MathF.PI/180;
        return new(MathF.Cos(p)*MathF.Cos(y),MathF.Cos(p)*MathF.Sin(y),-MathF.Sin(p));
    }
    public static bool InCone(Vector3 origin,Vector3 forward,Vector3 target,float range,float cosine)
    {
        var delta=target-origin; var length=delta.Length();
        return length > 0.01f && length <= range && Vector3.Dot(delta/length,Vector3.Normalize(forward)) >= cosine;
    }
    public static Vector3 PullVelocity(Vector3 from,Vector3 to,float strength,float maxVertical=220)
    {
        var d=to-from;if(d.LengthSquared()<1)return Vector3.Zero;
        var v=Vector3.Normalize(d)*Math.Clamp(strength,0,2000);v.Z=Math.Clamp(v.Z+80,-maxVertical,maxVertical);return v;
    }
    public static int Heal(int current,int amount,int maximum) => (int)Math.Clamp((long)current+Math.Max(0,amount),0,Math.Max(current,maximum));
}