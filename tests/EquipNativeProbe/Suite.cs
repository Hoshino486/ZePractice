using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

public sealed partial class EquipNativeProbe
{
    private readonly List<object> checks = [];
    private bool running;
    private int serial;
    private const string VerifiedServer = "2A229DD46C8AF0B52C1F49A3E343B99D9FFE946C102C56102A45B00F6A52349C";
    private static string[] Items(CCSPlayerController p) => p.PlayerPawn.Value?.WeaponServices?.MyWeapons
        .Select(w => w.Value).Where(w => w?.IsValid == true).Select(w => w!.DesignerName).Order().ToArray() ?? [];
    private void Assert(string name, bool pass, object? data = null)
    {
        checks.Add(new { name, pass, data });
        File.WriteAllText(Path.Combine(ModuleDirectory, "suite-results.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        Server.PrintToConsole("EQUIP_CHECK " + JsonSerializer.Serialize(new { name, pass, data }));
        if (!pass) throw new Exception(name);
    }
    private void Create(int flags)
    {
        if (equip?.IsValid == true) equip.Remove();
        equip = Utilities.CreateEntityByName<CBaseEntity>("game_player_equip");
        if (equip?.IsValid != true) throw new Exception("No game_player_equip");
        using var kv = new CEntityKeyValues();
        var id = "ze_equip_regression_" + ++serial;
        kv.SetString("targetname", id); kv.SetString("hammerUniqueId", id);
        kv.SetString("weapon0", "weapon_ak47"); kv.SetString("weapon1", "weapon_deagle");
        kv.SetInt("spawnflags", flags); equip.DispatchSpawn(kv);
    }
    private static void Seed(CCSPlayerController p)
    {
        p.PlayerPawn.Value!.ItemServices!.As<CCSPlayer_ItemServices>().RemoveWeapons();
        foreach (var item in new[] { "weapon_m4a1", "weapon_glock", "weapon_knife", "weapon_hegrenade" }) p.GiveNamedItem(item);
    }
    private void Fire(string input, CCSPlayerController? player, string value = "") =>
        equip!.AcceptInput(input, player?.PlayerPawn.Value, equip, value);
    // Only the isolated fixture uses a verified virtual slot. This invokes actual Use,
    // not an unregistered IO input named "Use". The original Use reads InputData_t.pActivator.
    private unsafe void RealUse(CCSPlayerController p)
    {
        byte* data = stackalloc byte[64]; new Span<byte>(data, 64).Clear();
        *(nint*)data = p.PlayerPawn.Value!.Handle;
        *(nint*)(data + 8) = equip!.Handle;
        var table = Marshal.ReadIntPtr(equip.Handle);
        var fn = (delegate* unmanaged<nint, nint, void>)Marshal.ReadIntPtr(table, 147 * IntPtr.Size);
        fn(equip.Handle, (nint)data);
    }
    private void RegisterSuite()
    {
        AddCommand("css_equip_suite", "Isolated native equipment assertions", (caller, cmd) =>
        {
            if (caller != null) return; Guard();
            if (running) throw new Exception("Suite already running");
            var file = Path.Combine(Server.GameDirectory, "csgo", "bin", "win64", "server.dll");
            if (!File.Exists(file)) file = Path.Combine(Server.GameDirectory, "bin", "win64", "server.dll");
            if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) != VerifiedServer) throw new Exception("Unverified server: no native fixture calls");
            var bots = Utilities.GetPlayers().Where(p => p.IsBot && p.TeamNum == 3 && p.PawnIsAlive).ToArray();
            if (bots.Length < 2) throw new Exception("Need two live CT fixture bots");
            var a = bots[0]; var b = bots[1]; checks.Clear(); running = true;
            Queue<Action> steps = new();
            steps.Enqueue(() => { Create(0); a.PlayerPawn.Value!.ItemServices!.As<CCSPlayer_ItemServices>().RemoveWeapons(); a.GiveNamedItem("weapon_knife"); Fire("TriggerForActivatedPlayer", a); var items = Items(a); Assert("default-list-gives-ak-deagle-and-preserves-knife", items.Contains("weapon_ak47") && items.Contains("weapon_deagle") && items.Contains("weapon_knife"), items); });
            steps.Enqueue(() => { Create(2); Seed(a); Fire("TriggerForActivatedPlayer", a); Assert("stripfirst-empty-param-gives-list", Items(a).SequenceEqual(new[] { "weapon_ak47", "weapon_deagle" }), Items(a)); });
            steps.Enqueue(() => { Seed(a); Fire("TriggerForActivatedPlayer", a, "weapon_aug"); Assert("explicit-string-param-replaces-list", Items(a).SequenceEqual(new[] { "weapon_aug" }), Items(a)); });
            steps.Enqueue(() => { Seed(a); Fire("TriggerForActivatedPlayer", a, "weapon_ak47"); Assert("CT-opposing-team-grant-not-blocked-by-zombie-guard", Items(a).SequenceEqual(new[] { "weapon_ak47" }) && a.TeamNum == 3 && a.PlayerPawn.Value!.TeamNum == 3, Items(a)); });
            steps.Enqueue(() => { Seed(a); Fire("TriggerForActivatedPlayer", a, "(null)"); Assert("null-string-sentinel-uses-list", Items(a).SequenceEqual(new[] { "weapon_ak47", "weapon_deagle" }), Items(a)); });
            steps.Enqueue(() => { Create(4); Seed(a); Fire("TriggerForActivatedPlayer", a); Assert("onlystripsame-replaces-two-slots-keeps-knife-and-grenade", Items(a).SequenceEqual(new[] { "weapon_ak47", "weapon_deagle", "weapon_hegrenade", "weapon_knife" }), Items(a)); });
            steps.Enqueue(() => { Create(2); Seed(a); Seed(b); Fire("TriggerForAllPlayers", a); Assert("allplayers-stripfirst-gives-both-players", Items(a).SequenceEqual(new[] { "weapon_ak47", "weapon_deagle" }) && Items(b).SequenceEqual(Items(a)), new { a = Items(a), b = Items(b) }); });
            steps.Enqueue(() => { Create(4); Seed(a); Seed(b); Fire("TriggerForAllPlayers", a); Assert("allplayers-onlystripsame", Items(a).SequenceEqual(new[] { "weapon_ak47", "weapon_deagle", "weapon_hegrenade", "weapon_knife" }) && Items(b).SequenceEqual(Items(a)), new { a = Items(a), b = Items(b) }); });
            steps.Enqueue(() => { Create(2); Seed(a); RealUse(a); Assert("real-use-virtual-stripfirst", Items(a).SequenceEqual(new[] { "weapon_ak47", "weapon_deagle" }), Items(a)); });
            steps.Enqueue(() => { Create(4); Seed(a); RealUse(a); Assert("real-use-virtual-onlystripsame", Items(a).SequenceEqual(new[] { "weapon_ak47", "weapon_deagle", "weapon_hegrenade", "weapon_knife" }), Items(a)); });
            steps.Enqueue(() => { Create(2); Seed(a); var before = Items(a); Fire("TriggerForActivatedPlayer", null, "weapon_aug"); Assert("null-activator-safe-no-side-effects", Items(a).SequenceEqual(before)); equip!.AcceptInput("TriggerForActivatedPlayer", equip, equip, "weapon_aug"); Assert("non-player-activator-safe", Items(a).SequenceEqual(before)); equip.AcceptInput("TriggerForActivatedPlayer", a, equip, "weapon_aug"); Assert("controller-not-pawn-safe", Items(a).SequenceEqual(before)); });
            steps.Enqueue(() => { var pawn = a.PlayerPawn.Value!; var before = Items(a); byte state = pawn.LifeState; try { pawn.LifeState = 1; Fire("TriggerForActivatedPlayer", a, "weapon_aug"); Assert("dead-lifestate-activator-safe", Items(a).SequenceEqual(before)); } finally { pawn.LifeState = state; } });
            steps.Enqueue(() => { Create(2); for (int i = 0; i < 32; i++) Fire("TriggerForActivatedPlayer", a, "weapon_aug"); Assert("repeated-strip-and-equip-32-times", Items(a).SequenceEqual(new[] { "weapon_aug" }), Items(a)); });
            steps.Enqueue(() => { Seed(a); Fire("tRiGgErFoRaCtIvAtEdPlAyEr", a, "weapon_aug"); Assert("case-insensitive-input-name", Items(a).SequenceEqual(new[] { "weapon_aug" }), Items(a)); });
            steps.Enqueue(() => { Seed(a); equip!.AcceptInput("TriggerForActivatedPlayer", a.PlayerPawn.Value, equip, "weapon_not_a_real_item"); Assert("unknown-weapon-does-not-crash-or-fabricate-item", !Items(a).Contains("weapon_not_a_real_item"), Items(a)); });
            void Next()
            {
                try {
                    Guard();
                    if (steps.Count == 0) { if(equip?.IsValid==true)equip.Remove(); equip=null; running=false; Server.PrintToConsole("EQUIP_SUITE_COMPLETE " + checks.Count); return; }
                    steps.Dequeue()(); AddTimer(.15f, Next, TimerFlags.STOP_ON_MAPCHANGE);
                } catch(Exception e) { running=false; Record("suite-error", e.ToString()); }
            }
            Next();
        });
        RegisterListener<Listeners.OnMapEnd>(() => { running = false; equip = null; });
    }
}
