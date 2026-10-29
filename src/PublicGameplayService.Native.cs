using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ZePractice;

public sealed partial class PublicGameplayService
{
    private MemoryFunctionVoid<IntPtr, byte, byte>? _moveType;
    private MemoryFunctionVoid<IntPtr, IntPtr, IntPtr>? _groundEntity;
    private MemoryFunctionVoid<CCSPlayer_WeaponServices, CBasePlayerWeapon>? _equipWeapon;
    private readonly Dictionary<IntPtr, PlayerState> _movementOverrides = new();
    private bool _settingMoveType, _weaponHooks, _canUseHook, _canAcquireHook, _grantHook;
    private long _blockedMoveChanges, _blockedAcquisitions, _blockedPickups, _blockedEquips, _blockedGrants, _zombieInventoryResets;

    private void InitializeNativeGameplay()
    {
        string game = Server.GameDirectory;
        if (Directory.Exists(Path.Combine(game, "csgo"))) game = Path.Combine(game, "csgo");
        string data;
        try { data = File.ReadAllText(Path.Combine(game, "addons", "cs2fixes", "gamedata", "cs2fixes.games.txt")); }
        catch (Exception error) { Report("native-gamedata", error); return; }
        string Signature(string name) => NativeGameplayData.ReadSignature(data, name, OperatingSystem.IsWindows());
        try
        {
            var move = new MemoryFunctionVoid<IntPtr, byte, byte>(Signature("CBaseEntity_SetMoveType"));
            var ground = new MemoryFunctionVoid<IntPtr, IntPtr, IntPtr>(Signature("SetGroundEntity"));
            move.Hook(OnSetMoveType, HookMode.Pre);
            _moveType = move; _groundEntity = ground;
            _plugin.Logger.LogInformation("PUBLIC_GAMEPLAY native movement ready; personal noclip overrides map MoveType only while enabled.");
        }
        catch (Exception error) { Report("native-movement", error); }
        try
        {
            VirtualFunctions.CCSPlayer_WeaponServices_CanUseFunc.Hook(OnCanUseWeapon, HookMode.Pre); _canUseHook = true;
            VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Hook(OnCanAcquireWeapon, HookMode.Pre); _canAcquireHook = true;
            VirtualFunctions.GiveNamedItemFunc.Hook(OnGiveNamedItem, HookMode.Pre); _grantHook = true;
            var equip = new MemoryFunctionVoid<CCSPlayer_WeaponServices, CBasePlayerWeapon>(Signature("CCSPlayer_WeaponServices_EquipWeapon"));
            equip.Hook(OnEquipWeapon, HookMode.Pre); _equipWeapon = equip;
            _weaponHooks = true;
            _plugin.Logger.LogInformation("PUBLIC_GAMEPLAY zombie knife-only guards ready; non-knives blocked; allow map knives={AllowMapKnives}.", Options.AllowZombieMapKnives);
        }
        catch (Exception error) { Report("native-weapons", error); }
    }

    private HookResult OnSetMoveType(DynamicHook hook)
    {
        if (_disposed || !_active || _settingMoveType || _movementOverrides.Count == 0) return HookResult.Continue;
        IntPtr pointer = hook.GetParam<IntPtr>(0);
        if (!_movementOverrides.TryGetValue(pointer, out var state) || !state.Noclip) return HookResult.Continue;
        var pawn = new CBaseEntity(pointer);
        if (!pawn.IsValid || pawn.EntityHandle.Raw != state.Pawn) return HookResult.Continue;
        byte requested = hook.GetParam<byte>(1), effective = state.Movement.MapRequest(requested);
        if (requested == effective) return HookResult.Continue;
        _blockedMoveChanges++;
        hook.SetParam(1, effective);
        return HookResult.Changed;
    }

    private void SetNativeMoveType(CCSPlayerPawn pawn, MoveType_t type)
    {
        if (_moveType == null) return;
        try { _settingMoveType = true; _moveType.Invoke(pawn.Handle, (byte)type, (byte)pawn.MoveCollide); }
        finally { _settingMoveType = false; }
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
    }

    private void ReleaseMovement(CCSPlayerPawn? pawn, PlayerState state)
    {
        _movementOverrides.Remove(state.MovementPawn); state.MovementPawn = IntPtr.Zero;
        if (!state.Noclip) return;
        var restore = (MoveType_t)state.Movement.Disable();
        if (pawn is { IsValid: true } && pawn.EntityHandle.Raw == state.Pawn) SetNativeMoveType(pawn, restore);
        state.Jumps.Reset();
    }

    // Personal practice targets have their own stationary rifle loadout. Do not expand this
    // exemption to ordinary bots or human players: real zombies retain all four knife guards.
    private bool ZombiePawn(CBasePlayerPawn? pawn)
    {
        if (pawn is not { IsValid: true }) return false;
        var controller = pawn.Controller.Value?.As<CCSPlayerController>();
        bool ownsCurrentPawn = controller is { IsValid: true } && controller.PlayerPawn.Raw == pawn.EntityHandle.Raw;
        return ZombieWeaponPolicy.IsZombieTeam(pawn.TeamNum, controller?.TeamNum ?? 0, ownsCurrentPawn) &&
            !_plugin.IsPracticeBot(controller);
    }
    private bool CanCarry(CBasePlayerWeapon weapon)
    {
        if (!weapon.IsValid) return false;
        bool knife = ZombieWeaponPolicy.IsKnifeClass(weapon.DesignerName);
        bool mapItem = ZombieWeaponPolicy.IsMapItem(weapon.UniqueHammerID, weapon.Entity?.Name);
        return ZombieWeaponPolicy.CanCarry(2, knife, mapItem,
            ZombieWeaponPolicy.MatchesMapKnife(ZePracticePlugin.NormalizeMapName(Server.MapName), weapon.Entity?.Name, Options.ZombieKnifeItems),
            Options.AllowZombieMapKnives);
    }

    private HookResult OnCanUseWeapon(DynamicHook hook)
    {
        if (_disposed || !_active) return HookResult.Continue;
        var services = hook.GetParam<CCSPlayer_WeaponServices>(0);
        if (!ZombiePawn(services.Pawn.Value) || ZombieGear(services.Pawn.Value) || CanCarry(hook.GetParam<CBasePlayerWeapon>(1))) return HookResult.Continue;
        _blockedPickups++; hook.SetReturn(false); return HookResult.Stop;
    }

    private HookResult OnCanAcquireWeapon(DynamicHook hook)
    {
        if (_disposed || !_active) return HookResult.Continue;
        var services = hook.GetParam<CCSPlayer_ItemServices>(0);
        if (!ZombiePawn(services.Pawn.Value) || ZombieGear(services.Pawn.Value)) return HookResult.Continue;
        var method = hook.GetParam<AcquireMethod>(2);
        var item = hook.GetParam<CEconItemView>(1);
        if (method == AcquireMethod.PickUp && ZombieWeaponPolicy.IsKnifeDefinition(item.ItemDefinitionIndex)) return HookResult.Continue;
        _blockedAcquisitions++; hook.SetReturn(AcquireResult.NotAllowedByTeam); return HookResult.Stop;
    }

    private HookResult OnGiveNamedItem(DynamicHook hook)
    {
        if (_disposed || !_active) return HookResult.Continue;
        var services = new CCSPlayer_ItemServices(hook.GetParam<IntPtr>(0));
        if (!ZombiePawn(services.Pawn.Value) || ZombieGear(services.Pawn.Value) || ZombieWeaponPolicy.IsKnifeClass(hook.GetParam<string>(1))) return HookResult.Continue;
        _blockedGrants++; hook.SetReturn(IntPtr.Zero); return HookResult.Stop;
    }

    private HookResult OnEquipWeapon(DynamicHook hook)
    {
        if (_disposed || !_active) return HookResult.Continue;
        var services = hook.GetParam<CCSPlayer_WeaponServices>(0);
        if (!ZombiePawn(services.Pawn.Value) || ZombieGear(services.Pawn.Value) || CanCarry(hook.GetParam<CBasePlayerWeapon>(1))) return HookResult.Continue;
        _blockedEquips++; return HookResult.Stop;
    }

    private void EnforceZombieInventory(CCSPlayerController player, PlayerState state)
    {
        if (player.TeamNum != 2 || !Alive(player) || state.ResettingTeam || ZombieGearEnabled(player)) return;
        var pawn = player.PlayerPawn.Value!;
        if (pawn.WeaponServices == null || pawn.ItemServices == null) return;
        bool hasKnife = false;
        foreach (var handle in pawn.WeaponServices.MyWeapons.ToArray())
        {
            if (handle.Value is not { IsValid: true } weapon) continue;
            if (CanCarry(weapon)) { hasKnife = true; continue; }
            _zombieInventoryResets++;
            QueueTeamReset(player, state, player.TeamNum);
            return;
        }
        if (hasKnife || Server.CurrentTime < state.NextKnifeIssue) return;
        state.NextKnifeIssue = Server.CurrentTime + 1;
        player.GiveNamedItem("weapon_knife");
    }

    private void DisposeNativeGameplay()
    {
        PluginCleanup.Run("unhook-move", () => _moveType?.Unhook(OnSetMoveType, HookMode.Pre), Report);
        PluginCleanup.Run("unhook-equip", () => _equipWeapon?.Unhook(OnEquipWeapon, HookMode.Pre), Report);
        if (_canUseHook) PluginCleanup.Run("unhook-can-use", () => VirtualFunctions.CCSPlayer_WeaponServices_CanUseFunc.Unhook(OnCanUseWeapon, HookMode.Pre), Report);
        if (_canAcquireHook) PluginCleanup.Run("unhook-can-acquire", () => VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Unhook(OnCanAcquireWeapon, HookMode.Pre), Report);
        if (_grantHook) PluginCleanup.Run("unhook-grant", () => VirtualFunctions.GiveNamedItemFunc.Unhook(OnGiveNamedItem, HookMode.Pre), Report);
        _canUseHook = _canAcquireHook = _grantHook = false;
        _moveType = null; _groundEntity = null; _equipWeapon = null; _movementOverrides.Clear(); _weaponHooks = false;
    }
}
