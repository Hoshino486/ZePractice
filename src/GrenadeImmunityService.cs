using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ZePractice;

public sealed class GrenadeImmunityService(ZePracticePlugin plugin) : IDisposable
{
    private const string SmokeDesignerName = "smokegrenade_projectile";
    private sealed class SmokeState(uint handle)
    {
        public uint Handle { get; } = handle;
        public bool Suppressed, RemovalQueued;
    }

    private readonly Dictionary<uint, SmokeState> _smokes = new();
    private readonly HashSet<string> _errors = new(StringComparer.Ordinal);
    private bool _initialized, _active, _disposed;
    private int _generation, _flashClears, _smokeRemovals;

    public void Initialize()
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        plugin.RegisterListener<Listeners.OnServerPostEntityThink>(MaintainEffects);
        plugin.RegisterListener<Listeners.OnEntitySpawned>(EntitySpawned);
        plugin.RegisterListener<Listeners.OnEntityDeleted>(EntityDeleted);
        plugin.RegisterListener<Listeners.CheckTransmit>(CheckTransmit);
        plugin.RegisterEventHandler<EventPlayerBlind>(Blind, HookMode.Pre);
        plugin.RegisterEventHandler<EventSmokegrenadeDetonate>(SmokeDetonated, HookMode.Pre);
        plugin.RegisterEventHandler<EventPlayerSpawn>(PlayerSpawned, HookMode.Post);
    }

    public void OnMapStart()
    {
        if (_disposed) return;
        _active = true;
        int generation = ++_generation;
        _smokes.Clear();
        Server.NextWorldUpdate(() => Guard("map-start", () =>
        {
            if (!Current(generation)) return;
            foreach (var smoke in Utilities.FindAllEntitiesByDesignerName<CSmokeGrenadeProjectile>(SmokeDesignerName))
                TrackSmoke(smoke);
            MaintainEffects();
        }));
    }

    public void OnMapEnd()
    {
        _active = false;
        _generation++;
        _smokes.Clear();
    }

    private bool Current(int generation) => !_disposed && _active && generation == _generation;

    private SmokeState? TrackSmoke(CEntityInstance? entity)
    {
        if (entity is not { IsValid: true } || entity.DesignerName != SmokeDesignerName) return null;
        uint handle = entity.EntityHandle.Raw;
        if (!_smokes.TryGetValue(handle, out var state)) _smokes[handle] = state = new(handle);
        return state;
    }

    private void EntitySpawned(CEntityInstance entity)
    {
        if (_active && !_disposed) Guard("smoke-spawn", () => TrackSmoke(entity));
    }

    private void EntityDeleted(CEntityInstance entity) => _smokes.Remove(entity.EntityHandle.Raw);

    private void ClearFlash(CCSPlayerController? player)
    {
        if (player is not { IsValid: true, IsHLTV: false } || player.PlayerPawn.Value is not { IsValid: true } pawn) return;
        bool changed = pawn.FlashDuration != 0 || pawn.FlashMaxAlpha != 0 || pawn.BlindUntilTime != 0 || pawn.BlindStartTime != 0;
        if (pawn.FlashDuration != 0)
        {
            pawn.FlashDuration = 0;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_flFlashDuration");
        }
        if (pawn.FlashMaxAlpha != 0)
        {
            pawn.FlashMaxAlpha = 0;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_flFlashMaxAlpha");
        }
        pawn.BlindUntilTime = 0;
        pawn.BlindStartTime = 0;
        if (changed) _flashClears++;
    }

    private HookResult Blind(EventPlayerBlind gameEvent, GameEventInfo information)
    {
        if (_active && !_disposed) Guard("blind-event", () =>
        {
            gameEvent.BlindDuration = 0;
            ClearFlash(gameEvent.Userid);
        });
        return HookResult.Continue;
    }

    private HookResult PlayerSpawned(EventPlayerSpawn gameEvent, GameEventInfo information)
    {
        if (_active && !_disposed) Guard("player-spawn", () => ClearFlash(gameEvent.Userid));
        return HookResult.Continue;
    }

    private HookResult SmokeDetonated(EventSmokegrenadeDetonate gameEvent, GameEventInfo information)
    {
        if (_active && !_disposed && gameEvent.Entityid > 0) Guard("smoke-event", () =>
        {
            var entity = Utilities.GetEntityFromIndex<CEntityInstance>(gameEvent.Entityid);
            if (TrackSmoke(entity) is { } state && new CHandle<CSmokeGrenadeProjectile>(state.Handle).Value is { IsValid: true } smoke)
                SuppressSmoke(smoke, state);
        });
        return HookResult.Continue;
    }

    private void SuppressSmoke(CSmokeGrenadeProjectile smoke, SmokeState state)
    {
        state.Suppressed = true;
        if (smoke.SmokeEffectTickBegin != 0)
        {
            smoke.SmokeEffectTickBegin = 0;
            Utilities.SetStateChanged(smoke, "CSmokeGrenadeProjectile", "m_nSmokeEffectTickBegin");
        }
        if (state.RemovalQueued) return;
        state.RemovalQueued = true;
        int generation = _generation;
        Server.NextWorldUpdate(() => Guard("smoke-remove", () =>
        {
            if (!Current(generation) || !_smokes.TryGetValue(state.Handle, out var current) || !ReferenceEquals(state, current)) return;
            state.RemovalQueued = false;
            var remaining = new CHandle<CSmokeGrenadeProjectile>(state.Handle).Value;
            if (remaining is { IsValid: true } && remaining.DesignerName == SmokeDesignerName)
            {
                remaining.Remove();
                _smokeRemovals++;
            }
            _smokes.Remove(state.Handle);
        }));
    }

    private void MaintainEffects()
    {
        if (!_active || _disposed) return;
        Guard("flash-maintenance", () =>
        {
            foreach (var player in Utilities.GetPlayers()) ClearFlash(player);
        });
        foreach (var state in _smokes.Values.ToArray()) Guard("smoke-maintenance", () =>
        {
            var smoke = new CHandle<CSmokeGrenadeProjectile>(state.Handle).Value;
            if (smoke is not { IsValid: true }) { _smokes.Remove(state.Handle); return; }
            if (state.Suppressed || smoke.DidSmokeEffect || smoke.SmokeEffectTickBegin > 0) SuppressSmoke(smoke, state);
        });
    }

    private void CheckTransmit(CCheckTransmitInfoList list)
    {
        if (!_active || _disposed) return;
        MaintainEffects();
        Guard("transmit", () =>
        {
            foreach (var state in _smokes.Values)
            {
                if (!state.Suppressed || new CHandle<CSmokeGrenadeProjectile>(state.Handle).Value is not { IsValid: true } smoke) continue;
                foreach (var (information, _) in list) information.TransmitEntities.Remove((int)smoke.Index);
            }
        });
    }

    private void Guard(string operation, Action action)
    {
        try { action(); }
        catch (Exception error)
        {
            if (_errors.Add(operation)) plugin.Logger.LogError(error, "Grenade immunity {Operation} failed", operation);
        }
    }

    public string Diagnostics() => $"immunity={(_active && !_disposed ? "on" : "inactive")} flash=blocked smoke=blocked tracked={_smokes.Count} flash_clears={_flashClears} smoke_removed={_smokeRemovals} errors={_errors.Count}";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        OnMapEnd();
        if (!_initialized) return;
        plugin.RemoveListener<Listeners.OnServerPostEntityThink>(MaintainEffects);
        plugin.RemoveListener<Listeners.OnEntitySpawned>(EntitySpawned);
        plugin.RemoveListener<Listeners.OnEntityDeleted>(EntityDeleted);
        plugin.RemoveListener<Listeners.CheckTransmit>(CheckTransmit);
        plugin.DeregisterEventHandler<EventPlayerBlind>(Blind, HookMode.Pre);
        plugin.DeregisterEventHandler<EventSmokegrenadeDetonate>(SmokeDetonated, HookMode.Pre);
        plugin.DeregisterEventHandler<EventPlayerSpawn>(PlayerSpawned, HookMode.Post);
    }
}
