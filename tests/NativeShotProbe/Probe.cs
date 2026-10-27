using System.Numerics;
using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Plugin.Host;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;

public sealed class NativeShotProbe : BasePlugin
{
    public override string ModuleName => "NativeShotProbe - isolated bots only";
    public override string ModuleVersion => "1.0.2";
    private int _shots, _impacts;
    private readonly List<object> _fireSamples = new();
    private MemoryFunctionWithReturn<CCSWeaponBase, float>? _spread;
    private MemoryFunctionWithReturn<CCSWeaponBase, IntPtr, IntPtr, float>? _inaccuracy;

    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    public override void Load(bool hotReload)
    {
        AddCommand("css_shot_probe_players", "Isolated read-only player enumeration diagnostic", (caller, command) =>
        {
            if (caller != null || ConVar.Find("hostport")?.GetPrimitiveValue<int>() != 27025) return;
            command.ReplyToCommand(JsonSerializer.Serialize(new
            {
                cachedMax = Server.MaxPlayers,
                nativeMax = NativeAPI.GetMaxClients(),
                utilityCount = Utilities.GetPlayers().Count,
                controllers = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller")
                    .Select(player => new { player.Index, player.PlayerName, player.Connected, player.IsBot, player.TeamNum, player.PawnIsAlive }).ToArray()
            }));
        });
        AddCommand("css_shot_probe", "Isolated 27025 bot shooting probe", (caller, command) =>
        {
            if (caller != null || ConVar.Find("hostport")?.GetPrimitiveValue<int>() != 27025 || Utilities.GetPlayers().Any(player => !player.IsBot))
                throw new InvalidOperationException("Probe requires port 27025 and bots only.");
            var manager = (IPluginManager)Field(Application.Instance, "_pluginManager");
            var practice = manager.GetLoadedPlugins().Single(context => context.Plugin.GetType().FullName == "ZePractice.ZePracticePlugin").Plugin;
            var gameplay = Field(practice, "_publicGameplay");
            _spread = (MemoryFunctionWithReturn<CCSWeaponBase, float>)Field(gameplay, "_spread");
            _inaccuracy = (MemoryFunctionWithReturn<CCSWeaponBase, IntPtr, IntPtr, float>)Field(gameplay, "_inaccuracy");
            _shots = _impacts = 0; _fireSamples.Clear();
            var shooters = Utilities.GetPlayers().Where(player => player.IsBot && player.TeamNum == 3 && player.PawnIsAlive).ToArray();
            var target = Utilities.GetPlayers().First(player => player.IsBot && player.TeamNum == 2 && player.PawnIsAlive);
            var anchor = shooters[0].PlayerPawn.Value!.AbsOrigin!;
            var center = new Vector3(anchor.X, anchor.Y, anchor.Z + 3);
            string weapon = command.ArgCount == 2 ? command.GetArg(1) : "weapon_m4a1";
            if (weapon is not ("weapon_m4a1" or "weapon_m4a1_silencer" or "weapon_ak47" or "weapon_hkp2000")) throw new InvalidOperationException("Unknown probe weapon.");
            for (int index = 0; index < shooters.Length; index++)
            {
                var shooter = shooters[index]; var pawn = shooter.PlayerPawn.Value!;
                pawn.Teleport(new CounterStrikeSharp.API.Modules.Utils.Vector(center.X, center.Y + index * 80, center.Z), new QAngle(0, 0, 0), new(0, 0, 0));
                pawn.ItemServices!.As<CCSPlayer_ItemServices>().RemoveWeapons();
                shooter.GiveNamedItem(weapon);
                if (pawn.Bot is { } bot) { bot.IsAttacking = true; bot.AllowActive = true; }
            }
            target.PlayerPawn.Value!.Teleport(new CounterStrikeSharp.API.Modules.Utils.Vector(center.X + 150, center.Y, center.Z), new QAngle(0, 180, 0), new(0, 0, 0));
            Server.ExecuteCommand("bot_stop t; bot_zombie 0");
            Server.PrintToConsole($"SHOT_PROBE setup weapon={weapon}; shots={_shots}; impacts={_impacts}; center={center}");
        });
        AddCommand("css_shot_probe_result", "Read real-fire sample results", (caller, command) =>
        {
            if (caller != null || ConVar.Find("hostport")?.GetPrimitiveValue<int>() != 27025) return;
            var report = JsonSerializer.Serialize(new { shots = _shots, impacts = _impacts, fireTimeNativeSamples = _fireSamples });
            File.WriteAllText(Path.Combine(ModuleDirectory, "probe-events.json"), report);
            command.ReplyToCommand(report);
        });
        RegisterEventHandler<EventWeaponFire>((gameEvent, _) =>
        {
            _shots++;
            if (_spread != null && _inaccuracy != null && _fireSamples.Count < 30 &&
                gameEvent.Userid is { IsBot: true, TeamNum: 3 } player &&
                player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value is { IsValid: true } active)
            {
                var weapon = active.As<CCSWeaponBase>();
                _fireSamples.Add(new { shot = _shots, weapon = gameEvent.Weapon, spread = _spread.Invoke(weapon),
                    inaccuracy = _inaccuracy.Invoke(weapon, IntPtr.Zero, IntPtr.Zero) });
            }
            if (_shots <= 4 || _shots % 20 == 0) Server.PrintToConsole($"SHOT_PROBE fired={_shots}; weapon={gameEvent.Weapon}; impacts={_impacts}");
            return HookResult.Continue;
        });
        RegisterEventHandler<EventBulletImpact>((gameEvent, _) => { _impacts++; return HookResult.Continue; });
    }
}
