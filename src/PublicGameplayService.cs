using System.Numerics;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ZePractice;

public sealed partial class PublicGameplayService : IDisposable
{
    private readonly ZePracticePlugin _plugin;
    private readonly Dictionary<int, PlayerState> _players = new();
    private readonly Dictionary<uint, (CCSWeaponBase Weapon, DroppedGunClock Clock)> _guns = new();
    private readonly Dictionary<string, CommandInfo.CommandCallback> _commands = new();
    private readonly Dictionary<string, (string Original, string Last)> _cvars = new();
    private readonly HashSet<string> _reported = new();
    private bool _active, _disposed;
    private int _generation;
    private int? _roundMinutes;
    private float _nextVitals, _nextMaintenance;
    private long _removedGuns;
    private PublicGameplayOptions Options => _plugin.Config.PublicGameplay;
    private sealed class PlayerState
    {
        public int? UserId;
        public uint Pawn;
        public int Team, MaximumHealth;
        public readonly TeamResetSequence TeamReset = new();
        public int TeamRevision => TeamReset.Revision;
        public bool ResettingTeam => TeamReset.Pending;
        public bool InfiniteMagazine, BunnyHop, NoSpread;
        public readonly NoSpreadPredictionState Prediction = new();
        public bool Noclip => Movement.Enabled;
        public readonly PersonalNoclipState Movement = new();
        public IntPtr MovementPawn;
        public float NextKnifeIssue;
        public float MusicVolume = 1;
        public int HideRange;
        public CPointWorldText? NameTag;
        public readonly ZombieRegenerationState Regeneration = new();
        public readonly BunnyHopSequence Jumps = new();
    }

    public PublicGameplayService(ZePracticePlugin plugin) => _plugin = plugin;
    private bool Player(CCSPlayerController? player) => player is { IsValid: true, IsHLTV: false } && !_plugin.IsPracticeBot(player);
    private bool Alive(CCSPlayerController? player) => Player(player) && player!.PawnIsAlive && player.PlayerPawn.Value is { IsValid: true, Health: > 0 };
    private PlayerState State(CCSPlayerController player)
    {
        if (!_players.TryGetValue(player.Slot, out var state) || state.UserId != player.UserId)
        {
            if (state != null) ReleaseMovement(null, state);
            if (state?.NameTag is { IsValid: true } oldTag) oldTag.Remove();
            _players[player.Slot] = state = new() { UserId = player.UserId, InfiniteMagazine = Options.DefaultInfiniteMagazine, BunnyHop = Options.DefaultBunnyHop };
        }
        return state;
    }

    public void Initialize()
    {
        RegisterCommands();
        _plugin.AddCommandListener("say", MenuInputChat, HookMode.Pre);
        _plugin.AddCommandListener("say_team", MenuInputChat, HookMode.Pre);
        _plugin.RegisterListener<Listeners.OnTick>(Tick);
        _plugin.RegisterListener<Listeners.OnClientDisconnect>(Disconnect);
        _plugin.RegisterListener<Listeners.OnEntitySpawned>(EntitySpawned);
        _plugin.RegisterListener<Listeners.CheckTransmit>(CheckTransmit);
        _plugin.RegisterEventHandler<EventPlayerSpawn>(Spawn, HookMode.Post);
        _plugin.RegisterEventHandler<EventPlayerTeam>(TeamChanged, HookMode.Post);
        _plugin.RegisterEventHandler<EventPlayerDeath>(Death, HookMode.Post);
        _plugin.RegisterEventHandler<EventPlayerHurt>(Hurt, HookMode.Post);
        _plugin.RegisterEventHandler<EventPlayerJump>(Jumped, HookMode.Post);
        _plugin.RegisterEventHandler<EventRoundStart>(ResetRoundArtifacts, HookMode.Post);
        _plugin.HookEntityOutput("weapon_knife", "OnPlayerPickup", ArtifactPickup, HookMode.Pre);
        _plugin.AddCommandListener("say", WorkshopChat, HookMode.Pre);
        _plugin.AddCommandListener("say_team", WorkshopChat, HookMode.Pre);
        InitializeSounds();
        InitializeNativeGameplay();
        InitializeNoSpread();
    }

    public void OnMapStart()
    {
        _menuInput.Clear(); ResetNoSpreadPrediction(); _lastShot.Clear();
        _active = true; _generation++; _players.Clear(); _guns.Clear(); _sounds.Clear(); _movementOverrides.Clear(); _artifacts.Clear();
        _nextVitals = _nextMaintenance = 0; _roundMinutes = null;
        int generation = _generation;
        Server.NextFrame(() =>
        {
            if (_disposed || !_active || generation != _generation) return;
            ApplyServerRules();
            foreach (var weapon in Utilities.FindAllEntitiesByDesignerName<CCSWeaponBase>("weapon_")) TrackGun(weapon, true);
        });
    }

    public void OnMapEnd()
    { _menuInput.Clear(); ResetNoSpreadPrediction(); _lastShot.Clear(); _active = false; _generation++; _players.Clear(); _guns.Clear(); _sounds.Clear(); _movementOverrides.Clear(); _artifacts.Clear(); }

    private void Later(Action action)
    {
        int generation = _generation;
        Server.NextFrame(() => { if (!_disposed && _active && generation == _generation) action(); });
    }

    private HookResult Spawn(EventPlayerSpawn gameEvent, GameEventInfo information)
    {
        var player = gameEvent.Userid;
        if (Player(player)) Later(() =>
        {
            if (!Player(player)) return;
            var state = State(player!);
            if (state.ResettingTeam) return;
            if (state.Team != 0 && state.Team != player!.TeamNum) QueueTeamReset(player, state, player.TeamNum);
            else if (Alive(player)) InitializePawn(player!, state, true);
        });
        return HookResult.Continue;
    }

    private HookResult TeamChanged(EventPlayerTeam gameEvent, GameEventInfo information)
    {
        var player = gameEvent.Userid;
        if (Player(player))
        {
            var state = State(player!);
            if (!gameEvent.Disconnect && player!.TeamNum == gameEvent.Team &&
                !(state.ResettingTeam && state.TeamReset.TargetTeam == gameEvent.Team))
                QueueTeamReset(player, state, gameEvent.Team);
        }
        return HookResult.Continue;
    }

    private HookResult Death(EventPlayerDeath gameEvent, GameEventInfo information)
    {
        var player = gameEvent.Userid;
        if (!Player(player) || player!.TeamNum is not (2 or 3)) return HookResult.Continue;
        var state = State(player); int revision = state.TeamRevision, team = player.TeamNum;
        ReleaseMovement(player.PlayerPawn.Value, state);
        if (state.ResettingTeam) return HookResult.Continue;
        int? userId = player.UserId;
        Later(() =>
        {
            if (!Player(player) || player.UserId != userId || player.TeamNum != team || state.TeamRevision != revision) return;
            QueueTeamReset(player, state, 2, true);
        });
        return HookResult.Continue;
    }

    private HookResult Hurt(EventPlayerHurt gameEvent, GameEventInfo information)
    {
        if (Player(gameEvent.Userid)) State(gameEvent.Userid!).Regeneration.Damage(Server.CurrentTime, Options.RegenerationDelaySeconds);
        return HookResult.Continue;
    }

    private HookResult Jumped(EventPlayerJump gameEvent, GameEventInfo information)
    {
        if (Alive(gameEvent.Userid))
        {
            var state = State(gameEvent.Userid!);
            if (state.BunnyHop && !state.Noclip) state.Jumps.Jump(Server.CurrentTime);
        }
        return HookResult.Continue;
    }

    private void InitializePawn(CCSPlayerController player, PlayerState state, bool force)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is not { IsValid: true, Health: > 0 } || player.TeamNum is not (2 or 3)) return;
        if (!force && state.Pawn == player.PlayerPawn.Raw && state.Team == player.TeamNum) return;
        ReleaseMovement(state.Pawn == player.PlayerPawn.Raw ? pawn : null, state);
        bool fromZombie = state.Team == 2 && state.Pawn == player.PlayerPawn.Raw;
        state.Pawn = player.PlayerPawn.Raw; state.Team = player.TeamNum;
        state.Jumps.Reset(); state.NextKnifeIssue = 0;
        state.MaximumHealth = player.TeamNum == 2 ? Options.ZombieHealth : Math.Min(_plugin.Config.DefaultHealth, Options.HumanHealthLimit);
        SetHealth(pawn, state.MaximumHealth, state.MaximumHealth);
        int freeArmor = Math.Max(ConVar.Find("mp_free_armor")?.GetPrimitiveValue<int>() ?? 0,
            ConVar.Find("cs2f_free_armor")?.GetPrimitiveValue<int>() ?? 0);
        SetArmor(pawn, player.TeamNum == 2 ? Options.ZombieArmor :
            PublicGameplayLogic.HumanSpawnArmor(pawn.ArmorValue, freeArmor, fromZombie, Options.HumanArmorLimit));
        MaintainZombieHelmet(player, pawn);
        state.Regeneration.Reset(pawn.Health, pawn.ArmorValue, Server.CurrentTime, Options.RegenerationDelaySeconds);
        EnforceZombieInventory(player, state);
    }

    private static void SetHealth(CCSPlayerPawn pawn, int health, int maximum)
    {
        if (pawn.MaxHealth != maximum) { pawn.MaxHealth = maximum; Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth"); }
        if (pawn.Health != health) { pawn.Health = health; Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth"); }
    }

    private static void SetArmor(CCSPlayerPawn pawn, int armor)
    { if (pawn.ArmorValue != armor) { pawn.ArmorValue = armor; Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue"); } }

    private static void MaintainZombieHelmet(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        // ArmorValue alone is a vest: the server's head-hit calculation checks ItemServices.
        // Keep the controller summary in sync too, without changing human equipment rules.
        if (player.TeamNum != 2) return;
        if (pawn.ItemServices?.As<CCSPlayer_ItemServices>() is { } items && !items.HasHelmet)
        {
            items.HasHelmet = true;
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pItemServices");
        }
        if (!player.PawnHasHelmet)
        {
            player.PawnHasHelmet = true;
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_bPawnHasHelmet");
        }
        if (player.PawnArmor != pawn.ArmorValue)
        {
            player.PawnArmor = pawn.ArmorValue;
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_iPawnArmor");
        }
    }

    private void Tick()
    {
        if (_disposed || !_active) return;
        float now = Server.CurrentTime;
        bool maintain = now >= _nextVitals;
        if (maintain) _nextVitals = now + 0.1f;
        foreach (var player in Utilities.GetPlayers().Where(Player))
        {
            try
            {
                var state = State(player);
                if (maintain && player.InGameMoneyServices is { } money && money.Account != Options.Money)
                { money.Account = Options.Money; Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices"); }
                if (state.ResettingTeam && state.TeamReset.TargetTeam != player.TeamNum ||
                    !state.ResettingTeam && player.TeamNum is 1 or 2 or 3 && state.Team != player.TeamNum)
                    QueueTeamReset(player, state, player.TeamNum);
                SyncNoSpreadPrediction(player, state);
                if (!Alive(player)) continue;
                if (state.ResettingTeam) continue;
                InitializePawn(player, state, false);
                MaintainMovement(player, state, now);
                MaintainAmmunition(player, state);
                if (maintain)
                {
                    EnforceZombieInventory(player, state);
                    var pawn = player.PlayerPawn.Value!;
                    int ceiling = player.TeamNum == 2 ? state.MaximumHealth : Options.HumanHealthLimit;
                    int armorCeiling = player.TeamNum == 2 ? Options.ZombieArmor : Options.HumanArmorLimit;
                    int health = Math.Min(pawn.Health, ceiling), armor = Math.Min(pawn.ArmorValue, armorCeiling);
                    var restored = state.Regeneration.Update(player.TeamNum, health, armor, ceiling, armorCeiling,
                        now, Options.RegenerationDelaySeconds, Options.HealthRegenerationPerSecond, Options.ArmorRegenerationPerSecond);
                    SetHealth(pawn, restored.Health, player.TeamNum == 2 ? ceiling : Math.Clamp(pawn.MaxHealth, 1, ceiling));
                    SetArmor(pawn, restored.Armor);
                    MaintainZombieHelmet(player, pawn);
                    MaintainNameTag(player, state);
                }
            }
            catch (Exception error) { Report("player-tick", error); }
        }
        if (now >= _nextMaintenance)
        {
            _nextMaintenance = now + 1;
            try { ApplyServerRules(); CleanupGuns(now); } catch (Exception error) { Report("maintenance", error); }
        }
    }

    private void MaintainAmmunition(CCSPlayerController player, PlayerState state)
    {
        var pawn = player.PlayerPawn.Value!;
        var services = pawn.WeaponServices;
        if (services == null) return;
        foreach (var handle in services.MyWeapons)
        {
            if (handle.Value is not { IsValid: true } entity) continue;
            var weapon = entity.As<CCSWeaponBase>();
            if (!Firearm(weapon)) continue;
            TrackGun(weapon, false);
            for (int index = 0; index < weapon.ReserveAmmo.Length; index++)
                if (weapon.ReserveAmmo[index] != Options.ReserveAmmo)
                { weapon.ReserveAmmo[index] = Options.ReserveAmmo; Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_pReserveAmmo"); }
            int ammoType = weapon.VData!.PrimaryAmmoType;
            if (ammoType >= 0 && ammoType < services.Ammo.Length && services.Ammo[ammoType] != Options.ReserveAmmo)
            { services.Ammo[ammoType] = (ushort)Options.ReserveAmmo; Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pWeaponServices"); }
            int magazine = weapon.VData.MaxClip1;
            if (state.InfiniteMagazine && magazine > 0 && weapon.Clip1 != magazine)
            { weapon.Clip1 = magazine; Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip1"); }
        }
    }

    private static bool Firearm(CCSWeaponBase weapon) => weapon.IsValid && weapon.VData?.GearSlot is gear_slot_t.GEAR_SLOT_RIFLE or gear_slot_t.GEAR_SLOT_PISTOL;
    private static bool MapGun(CCSWeaponBase weapon) => !string.IsNullOrWhiteSpace(weapon.UniqueHammerID) || !string.IsNullOrWhiteSpace(weapon.Entity?.Name);
    public void TrackIssuedGun(CCSWeaponBase weapon) => TrackGun(weapon, false, true);
    private void TrackGun(CCSWeaponBase weapon, bool existingWorldGun, bool issued = false)
    {
        if (!Firearm(weapon)) return;
        uint handle = weapon.EntityHandle.Raw;
        bool owned = weapon.OwnerEntity.IsValid;
        if (!_guns.TryGetValue(handle, out var entry))
        {
            entry = (weapon, new() { Protected = MapGun(weapon) || existingWorldGun && !owned });
            _guns[handle] = entry;
        }
        if (MapGun(weapon)) entry.Clock.Protected = true;
        if (issued || owned) entry.Clock.Update(true, true, Server.CurrentTime, Options.DroppedGunLifetimeSeconds);
    }

    private void EntitySpawned(CEntityInstance entity)
    {
        if (!_active || !entity.IsValid || !entity.DesignerName.StartsWith("weapon_", StringComparison.Ordinal)) return;
        var handle = entity.EntityHandle.Raw;
        Later(() =>
        {
            if (entity.IsValid && entity.EntityHandle.Raw == handle) TrackGun(entity.As<CCSWeaponBase>(), true);
        });
    }

    private void CleanupGuns(float now)
    {
        foreach (var entry in _guns.ToArray())
        {
            var (weapon, clock) = entry.Value;
            if (!weapon.IsValid || weapon.EntityHandle.Raw != entry.Key) { _guns.Remove(entry.Key); continue; }
            if (MapGun(weapon)) clock.Protected = true;
            if (!clock.Update(weapon.OwnerEntity.IsValid, Firearm(weapon), now, Options.DroppedGunLifetimeSeconds)) continue;
            weapon.Remove(); _guns.Remove(entry.Key); _removedGuns++;
        }
    }

    private void ApplyServerRules()
    {
        foreach (var pair in new Dictionary<string, string>
        {
            ["mp_maxmoney"] = Options.Money.ToString(), ["mp_startmoney"] = Options.Money.ToString(),
            ["mp_autoteambalance"] = "0", ["mp_limitteams"] = "0", ["mp_ignore_round_win_conditions"] = "1",
            ["mp_timelimit"] = "0", ["mp_maxrounds"] = "0", ["mp_match_end_restart"] = "0",
            ["mp_match_end_changelevel"] = "0", ["cs2f_noblock_enable"] = "1", ["cs2f_hide_enable"] = "0",
            ["mp_respawn_on_death_ct"] = "0", ["mp_respawn_on_death_t"] = "0",
            ["cs2f_infinite_reserve_ammo"] = "0", ["sv_infinite_ammo"] = "0"
        }.Concat(PublicGameplayLogic.ServerMovementRules).Concat(PublicGameplayLogic.ServerPlayerProtectionRules))
        {
            var variable = ConVar.Find(pair.Key);
            if (variable == null) { if (_reported.Add("cvar:" + pair.Key)) _plugin.Logger.LogWarning("PUBLIC_GAMEPLAY missing ConVar {Name}", pair.Key); continue; }
            string before = variable.StringValue;
            string original = _cvars.TryGetValue(pair.Key, out var previous) ? previous.Original : before;
            if (before != pair.Value && !(bool.TryParse(before, out bool boolean) && pair.Value == (boolean ? "1" : "0"))) variable.StringValue = pair.Value;
            _cvars[pair.Key] = (original, variable.StringValue);
        }
    }
    public void ReapplyRules() { if (_active && !_disposed) ApplyServerRules(); }

    private void Disconnect(int slot)
    {
        _menuInput.Remove(slot);
        _lastShot.Remove(slot);
        if (!_players.Remove(slot, out var state)) return;
        _artifacts.Release(state.Pawn);
        ReleaseMovement(null, state);
        if (state.NameTag is { IsValid: true } tag) tag.Remove();
    }

    private void Report(string operation, Exception error)
    { if (_reported.Add(operation)) _plugin.Logger.LogError(error, "PUBLIC_GAMEPLAY {Operation} failed", operation); }

    public bool NoclipEnabled(CCSPlayerController player) => _players.TryGetValue(player.Slot, out var state) && state.UserId == player.UserId && state.Noclip;
    public bool TeamResetPending(CCSPlayerController player) => _players.TryGetValue(player.Slot, out var state) && state.UserId == player.UserId && state.ResettingTeam;

    public string Diagnostics(CCSPlayerController? viewer)
    {
        return JsonSerializer.Serialize(new
        {
            active = _active,
            options = new { Options.ZombieHealth, Options.ZombieHealthLimit, Options.ZombieArmor, Options.RegenerationDelaySeconds, Options.HumanHealthLimit,
                Options.HumanArmorLimit, Options.Money, Options.ReserveAmmo, Options.DefaultInfiniteMagazine, Options.DefaultBunnyHop,
                Options.AllowZombieMapKnives },
            artifactResetMaps = Options.ArtifactResetRules.Keys, trackedGuns = _guns.Count, removedGuns = _removedGuns, mapSounds = _sounds.Count,
            movementHooks = _moveType != null && _groundEntity != null, weaponHooks = _weaponHooks,
            noSpreadHooks = NoSpreadReady, zeroedSpread = _zeroedSpread, zeroedInaccuracy = _zeroedInaccuracy,
            predictionUpdates = _predictionUpdates, shotPackets = _shotPackets, zeroSpreadPackets = _zeroSpreadPackets,
            unexpectedSpreadPackets = _unexpectedSpreadPackets,
            lastShots = _lastShot.Where(entry => viewer == null || entry.Key == viewer.Slot).Select(entry => entry.Value).ToArray(),
            blockedMoveChanges = _blockedMoveChanges, blockedAcquisitions = _blockedAcquisitions, blockedPickups = _blockedPickups,
            blockedEquips = _blockedEquips, blockedGrants = _blockedGrants, zombieInventoryResets = _zombieInventoryResets, teamResets = _teamResets,
            artifactOwners = _artifacts.Count, removedArtifactEntities = _removedArtifactEntities,
            errors = _reported, players = Utilities.GetPlayers().Where(Player).Where(player => viewer == null || player.Slot == viewer.Slot).Select(player =>
            {
                var pawn = player.PlayerPawn.Value; var state = State(player);
                return new { slot = player.Slot, team = player.TeamNum, alive = Alive(player), health = pawn?.Health, maxHealth = pawn?.MaxHealth,
                    armor = pawn?.ArmorValue, money = player.InGameMoneyServices?.Account, state.ResettingTeam, state.InfiniteMagazine, state.BunnyHop,
                    state.NoSpread, predictionSent = state.Prediction.LastSent, noSpreadActive = NoSpreadReady && PersonalNoSpreadLogic.Applies(player.TeamNum, state.NoSpread, Alive(player), state.ResettingTeam),
                    state.Noclip, moveType = pawn?.MoveType.ToString(), actualMoveType = pawn?.ActualMoveType.ToString(),
                    state.MusicVolume, state.HideRange, jumps = state.Jumps.ConsecutiveJumps, slowedJump = state.Jumps.SlowCurrentJump };
            }).ToArray()
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        PluginCleanup.ForPlayers(_active, _players.Count, () => Utilities.GetPlayers().Where(Player), player =>
        {
            if (!_players.TryGetValue(player.Slot, out var state) || state.UserId != player.UserId) return;
            ReleaseMovement(player.PlayerPawn.Raw == state.Pawn ? player.PlayerPawn.Value : null, state);
            if (state.NameTag is { IsValid: true } tag) tag.Remove();
        }, "dispose-player-state", Report);
        _menuInput.Clear();
        _plugin.RemoveCommandListener("say", MenuInputChat, HookMode.Pre);
        _plugin.RemoveCommandListener("say_team", MenuInputChat, HookMode.Pre);
        _disposed = true;
        DisposeNoSpread(); DisposeNativeGameplay(); DisposeSounds(); _active = false; _generation++;
        _plugin.RemoveListener<Listeners.OnTick>(Tick);
        _plugin.RemoveListener<Listeners.OnClientDisconnect>(Disconnect);
        _plugin.RemoveListener<Listeners.OnEntitySpawned>(EntitySpawned);
        _plugin.RemoveListener<Listeners.CheckTransmit>(CheckTransmit);
        _plugin.DeregisterEventHandler<EventPlayerSpawn>(Spawn, HookMode.Post);
        _plugin.DeregisterEventHandler<EventPlayerTeam>(TeamChanged, HookMode.Post);
        _plugin.DeregisterEventHandler<EventPlayerDeath>(Death, HookMode.Post);
        _plugin.DeregisterEventHandler<EventPlayerHurt>(Hurt, HookMode.Post);
        _plugin.DeregisterEventHandler<EventPlayerJump>(Jumped, HookMode.Post);
        _plugin.DeregisterEventHandler<EventRoundStart>(ResetRoundArtifacts, HookMode.Post);
        _plugin.UnhookEntityOutput("weapon_knife", "OnPlayerPickup", ArtifactPickup, HookMode.Pre);
        _plugin.RemoveCommandListener("say", WorkshopChat, HookMode.Pre);
        _plugin.RemoveCommandListener("say_team", WorkshopChat, HookMode.Pre);
        foreach (var command in _commands) _plugin.RemoveZeCommand(command.Key, command.Value);
        foreach (var entry in _cvars)
        {
            var variable = ConVar.Find(entry.Key);
            if (variable != null && variable.StringValue == entry.Value.Last) variable.StringValue = entry.Value.Original;
        }
        _players.Clear(); _guns.Clear();
    }
}
