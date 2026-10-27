using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using ZePractice;

int passed = 0, failed = 0;
void Check(bool condition, string message = "assertion failed") { if (!condition) throw new Exception(message); }
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
}
(ZePracticePlugin Plugin, GrenadeImmunityService Service) Fresh(bool start = true)
{
    Utilities.Reset(); Server.Updates.Clear();
    var plugin = new ZePracticePlugin();
    var service = new GrenadeImmunityService(plugin);
    service.Initialize();
    if (start) { service.OnMapStart(); Server.Step(); }
    return (plugin, service);
}
CCSPlayerController Player(int team = 3, bool bot = false, uint raw = 1)
{
    var pawn = Utilities.Add(new CCSPlayerPawn(raw + 100));
    var player = Utilities.Add(new CCSPlayerController(raw) { TeamNum = team, IsBot = bot, PlayerPawn = new(pawn.EntityHandle.Raw) });
    Utilities.Players.Add(player);
    Flash(pawn);
    return player;
}
void Flash(CCSPlayerPawn pawn) { pawn.FlashDuration = 5; pawn.FlashMaxAlpha = 255; pawn.BlindStartTime = 10; pawn.BlindUntilTime = 15; }
bool Clear(CCSPlayerPawn pawn) => pawn.FlashDuration == 0 && pawn.FlashMaxAlpha == 0 && pawn.BlindStartTime == 0 && pawn.BlindUntilTime == 0;
CSmokeGrenadeProjectile Smoke(ZePracticePlugin plugin, uint raw = 300, bool exploded = false)
{
    var smoke = Utilities.Add(new CSmokeGrenadeProjectile(raw) { DidSmokeEffect = exploded, SmokeEffectTickBegin = exploded ? 100 : 0 });
    plugin.Spawn(smoke);
    return smoke;
}
CCheckTransmitInfoList Viewers(params int[] entities)
{
    var list = new CCheckTransmitInfoList();
    foreach (var viewer in Utilities.Players.Cast<CCSPlayerController?>().Append(null))
    {
        var info = new CCheckTransmitInfo();
        info.TransmitEntities.UnionWith(entities);
        list.Add((info, viewer));
    }
    return list;
}

foreach (int team in new[] { 1, 2, 3 }) foreach (bool bot in new[] { false, true })
    Test($"default flash immunity team={team} bot={bot}", () =>
    {
        var (plugin, service) = Fresh();
        var player = Player(team, bot);
        var gameEvent = new EventPlayerBlind { Userid = player, BlindDuration = 5 };
        Check(plugin.Fire(gameEvent) == HookResult.Continue);
        Check(Clear(player.PlayerPawn.Value!) && gameEvent.BlindDuration == 0);
        Check(Utilities.Changes.Count == 2);
        plugin.Think(); Check(Utilities.Changes.Count == 2, "unchanged fields must not be resent");
        service.Dispose();
    });
Test("late native or script flash writes cleared after simulation and before transmit", () =>
{
    var (plugin, service) = Fresh(); var player = Player();
    plugin.Think(); Check(Clear(player.PlayerPawn.Value!));
    Flash(player.PlayerPawn.Value!); plugin.Transmit(Viewers()); Check(Clear(player.PlayerPawn.Value!));
    service.Dispose();
});
Test("new join respawn team switch and dead pawns have no permission or mode dependency", () =>
{
    var (plugin, service) = Fresh();
    var player = Player(); plugin.Fire(new EventPlayerSpawn { Userid = player }, HookMode.Post); Check(Clear(player.PlayerPawn.Value!));
    player.TeamNum = 2; var replacement = Utilities.Add(new CCSPlayerPawn(200)); player.PlayerPawn = new(200); Flash(replacement);
    plugin.Think(); Check(Clear(replacement));
    Player(1, false, 2); plugin.Think(); Check(Utilities.Players.All(player => Clear(player.PlayerPawn.Value!)));
    service.Dispose();
});
Test("null invalid disconnected and HLTV targets are ignored", () =>
{
    var (plugin, service) = Fresh(); var player = Player(); player.IsHLTV = true;
    plugin.Fire(new EventPlayerBlind()); plugin.Think(); Check(!Clear(player.PlayerPawn.Value!));
    player.IsHLTV = false; player.IsValid = false; plugin.Think(); Check(!Clear(player.PlayerPawn.Value!));
    player.IsValid = true; player.PlayerPawn.Value!.IsValid = false; plugin.Think();
    Check(plugin.Logger.Errors.Count == 0); service.Dispose();
});
Test("unexploded grenade remains visible and throwable", () =>
{
    var (plugin, service) = Fresh(); var smoke = Smoke(plugin); var viewers = Viewers(300, 400);
    plugin.Think(); plugin.Transmit(viewers); Server.Step();
    Check(smoke.IsValid && smoke.RemoveCalls == 0 && viewers.All(view => view.Information.TransmitEntities.Contains(300)));
    service.Dispose();
});
Test("detonation preserves special-skill event and defers safe removal", () =>
{
    var (plugin, service) = Fresh(); var smoke = Smoke(plugin);
    int postCalls = 0;
    plugin.RegisterEventHandler<EventSmokegrenadeDetonate>((gameEvent, information) => { Check(smoke.IsValid); postCalls++; return HookResult.Continue; }, HookMode.Post);
    var gameEvent = new EventSmokegrenadeDetonate { Entityid = 300 };
    Check(plugin.Fire(gameEvent) == HookResult.Continue); plugin.Fire(gameEvent, HookMode.Post);
    Check(postCalls == 1 && smoke.IsValid && Server.Updates.Count == 1);
    smoke.SmokeEffectTickBegin = 999; smoke.DidSmokeEffect = true;
    Player(2); Player(3, false, 2); Player(1, false, 3);
    var viewers = Viewers(300, 400); plugin.Transmit(viewers);
    Check(smoke.SmokeEffectTickBegin == 0 && smoke.DidSmokeEffect);
    Check(viewers.All(view => !view.Information.TransmitEntities.Contains(300) && view.Information.TransmitEntities.Contains(400)));
    plugin.Think(); Check(Server.Updates.Count == 1);
    Server.Step(); Check(!smoke.IsValid && smoke.RemoveCalls == 1);
    Check(service.Diagnostics().Contains("smoke_removed=1") && service.Diagnostics().Contains("errors=0")); service.Dispose();
});
Test("did-smoke flag alone catches clouds without an event", () =>
{
    var (plugin, service) = Fresh(); var smoke = Smoke(plugin); smoke.DidSmokeEffect = true;
    plugin.Think(); Server.Step(); Check(!smoke.IsValid); service.Dispose();
});
Test("effect-tick alone catches clouds before their next transmission", () =>
{
    var (plugin, service) = Fresh(); var smoke = Smoke(plugin); smoke.SmokeEffectTickBegin = 8;
    var viewers = Viewers(300); plugin.Transmit(viewers);
    Check(smoke.SmokeEffectTickBegin == 0 && !viewers[0].Information.TransmitEntities.Contains(300));
    Server.Step(); Check(!smoke.IsValid); service.Dispose();
});
Test("hot reload recovers existing clouds without deleting flying grenades", () =>
{
    var (plugin, service) = Fresh(false);
    var cloud = Utilities.Add(new CSmokeGrenadeProjectile(300) { DidSmokeEffect = true, SmokeEffectTickBegin = 1 });
    var flying = Utilities.Add(new CSmokeGrenadeProjectile(301)); Player();
    service.OnMapStart(); Server.Step(); Check(cloud.IsValid && cloud.SmokeEffectTickBegin == 0);
    Server.Step(); Check(!cloud.IsValid && flying.IsValid && Clear(Utilities.Players[0].PlayerPawn.Value!)); service.Dispose();
});
Test("wrong entity ids and map particle smoke remain untouched", () =>
{
    var (plugin, service) = Fresh();
    var others = new[] { "info_particle_system", "env_smokestack", "flashbang_projectile", "weapon_smokegrenade" }
        .Select((name, index) => Utilities.Add(new CBaseEntity((uint)(400 + index), name))).ToArray();
    foreach (var entity in others) { plugin.Spawn(entity); plugin.Fire(new EventSmokegrenadeDetonate { Entityid = (int)entity.Index }); }
    foreach (int index in new[] { -1, 0, 900 }) plugin.Fire(new EventSmokegrenadeDetonate { Entityid = index });
    plugin.Think(); Server.Step(); Check(others.All(entity => entity.IsValid) && service.Diagnostics().Contains("tracked=0")); service.Dispose();
});
Test("reused entity index is protected by handle serial", () =>
{
    var (plugin, service) = Fresh(); var old = Smoke(plugin, exploded: true); plugin.Think();
    old.Remove(); plugin.Delete(old); var replacement = Smoke(plugin, 300 + 32768);
    Server.Step(); Check(replacement.IsValid && replacement.RemoveCalls == 0); service.Dispose();
});
Test("map generation invalidates pending removal even when raw handle is reused", () =>
{
    var (plugin, service) = Fresh(); var old = Smoke(plugin, exploded: true); plugin.Think();
    service.OnMapEnd(); Utilities.Entities.Clear(); service.OnMapStart(); var replacement = Smoke(plugin);
    Server.Step(); Check(replacement.IsValid && replacement.RemoveCalls == 0); service.Dispose();
});
Test("entity removed by another plugin does not leave a pending callback", () =>
{
    var (plugin, service) = Fresh(); var smoke = Smoke(plugin, exploded: true); plugin.Think();
    smoke.Remove(); plugin.Delete(smoke); Server.Step(); Check(service.Diagnostics().Contains("tracked=0"));
    Check(plugin.Logger.Errors.Count == 0); service.Dispose();
});
Test("removal failure is logged and retried instead of losing cloud tracking", () =>
{
    var (plugin, service) = Fresh(); var smoke = Smoke(plugin, exploded: true); smoke.FailNextRemove = true;
    plugin.Think(); Server.Step(); Check(smoke.IsValid && plugin.Logger.Errors.Count == 1);
    plugin.Think(); Server.Step(); Check(!smoke.IsValid && smoke.RemoveCalls == 1); service.Dispose();
});
Test("initialize and dispose are idempotent and cancel delayed work", () =>
{
    var (plugin, service) = Fresh(); service.Initialize(); var smoke = Smoke(plugin, exploded: true); plugin.Think();
    service.Dispose(); service.Dispose(); service.Initialize(); service.OnMapStart(); Server.Step();
    Check(smoke.IsValid && plugin.Listeners.Count == 0 && plugin.Events.Count == 0 && service.Diagnostics().Contains("inactive"));
});
Test("inactive map does not mutate entities", () =>
{
    var (plugin, service) = Fresh(false); var player = Player(); var smoke = Smoke(plugin, exploded: true);
    var gameEvent = new EventPlayerBlind { Userid = player, BlindDuration = 5 }; plugin.Fire(gameEvent);
    plugin.Think(); plugin.Transmit(Viewers(300)); Check(!Clear(player.PlayerPawn.Value!) && smoke.SmokeEffectTickBegin == 100 && gameEvent.BlindDuration == 5);
    service.Dispose();
});
Test("multiple simultaneous clouds filter all viewers and remove exactly once", () =>
{
    var (plugin, service) = Fresh();
    for (uint slot = 1; slot <= 16; slot++) Player(slot % 2 == 0 ? 2 : 3, false, slot);
    var smokes = Enumerable.Range(1000, 64).Select(index => Smoke(plugin, (uint)index, true)).ToArray();
    var viewers = Viewers(smokes.Select(smoke => (int)smoke.Index).Append(5000).ToArray()); plugin.Transmit(viewers);
    Check(viewers.All(view => view.Information.TransmitEntities.SetEquals(new[] { 5000 })));
    Server.Step(); Check(smokes.All(smoke => smoke.RemoveCalls == 1)); Check(service.Diagnostics().Contains("errors=0")); service.Dispose();
});
Console.WriteLine($"GrenadeImmunity: {passed} passed, {failed} failed");
Environment.ExitCode = failed == 0 ? 0 : 1;
