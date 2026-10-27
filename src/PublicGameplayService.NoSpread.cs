using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ZePractice;

public sealed partial class PublicGameplayService
{
    private MemoryFunctionWithReturn<CCSWeaponBase, float>? _spread;
    private MemoryFunctionWithReturn<CCSWeaponBase, IntPtr, IntPtr, float>? _inaccuracy;
    private long _zeroedSpread, _zeroedInaccuracy;
    private long _predictionUpdates, _shotPackets, _zeroSpreadPackets, _unexpectedSpreadPackets;
    private bool _shotObserver;
    private readonly Dictionary<int, object> _lastShot = new();
    private bool NoSpreadReady => _spread != null && _inaccuracy != null;

    private void InitializeNoSpread()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Personal no-spread native data is verified for Windows x64.");
            string game = Server.GameDirectory;
            if (Directory.Exists(Path.Combine(game, "csgo"))) game = Path.Combine(game, "csgo");
            PersonalNoSpreadNativeData.Validate(File.ReadAllBytes(Path.Combine(game, "bin", "win64", "server.dll")));
            PersonalNoSpreadNativeData.ValidateLayout(Schema.GetSchemaOffset("CCSWeaponBase", "m_weaponMode"),
                Schema.GetSchemaOffset("CBaseEntity", "m_nSubclassID"), Schema.GetSchemaOffset("CCSWeaponBaseVData", "m_flSpread"),
                Schema.GetSchemaOffset("CCSWeaponBase", "m_fAccuracyPenalty"));
            _plugin.HookUserMessage(452, ObserveShotPacket, HookMode.Post);
            _shotObserver = true;
            var spread = new MemoryFunctionWithReturn<CCSWeaponBase, float>(PersonalNoSpreadNativeData.SpreadSignature);
            var inaccuracy = new MemoryFunctionWithReturn<CCSWeaponBase, IntPtr, IntPtr, float>(PersonalNoSpreadNativeData.InaccuracySignature);
            spread.Hook(OnGetSpread, HookMode.Pre); _spread = spread;
            inaccuracy.Hook(OnGetInaccuracy, HookMode.Pre); _inaccuracy = inaccuracy;
            _plugin.Logger.LogInformation("PUBLIC_GAMEPLAY personal no-spread hooks ready; CT opt-in only; recoil and shared weapon data unchanged.");
        }
        catch (Exception error) { Report("native-nospread", error); DisposeNoSpread(); }
    }

    private void SyncNoSpreadPrediction(CCSPlayerController player, PlayerState state, bool force = false, bool restore = false)
    {
        if (!Player(player) || player.IsBot) return;
        bool enabled = !restore && NoSpreadReady && PersonalNoSpreadLogic.Applies(player.TeamNum, state.NoSpread, Alive(player), state.ResettingTeam);
        if (!state.Prediction.NeedsUpdate(enabled, player.PlayerPawn.Raw, Server.CurrentTime, force)) return;
        player.ReplicateConVar("weapon_accuracy_nospread", enabled ? "1" : "0");
        state.Prediction.Sent(enabled, player.PlayerPawn.Raw, Server.CurrentTime);
        _predictionUpdates++;
    }

    private void ResetNoSpreadPrediction()
    {
        PluginCleanup.ForPlayers(_active, _players.Count, () => Utilities.GetPlayers().Where(Player), player =>
        {
            if (_players.TryGetValue(player.Slot, out var state) && state.UserId == player.UserId)
                SyncNoSpreadPrediction(player, state, true, true);
        }, "nospread-client-reset", Report);
    }

    private HookResult ObserveShotPacket(UserMessage message)
    {
        if (_disposed || !_active) return HookResult.Continue;
        try
        {
            uint handle = message.ReadUInt("player");
            var pawn = new CHandle<CCSPlayerPawn>(handle).Value;
            var player = pawn?.Controller.Value?.As<CCSPlayerController>();
            if (!Player(player)) return HookResult.Continue;
            var state = State(player!);
            bool enabled = PersonalNoSpreadLogic.Applies(player!.TeamNum, state.NoSpread, Alive(player), state.ResettingTeam);
            float spread = message.ReadFloat("spread"), inaccuracy = message.ReadFloat("inaccuracy");
            _shotPackets++;
            if (enabled)
            {
                if (spread == 0 && inaccuracy == 0) _zeroSpreadPackets++;
                else _unexpectedSpreadPackets++;
            }
            _lastShot[player.Slot] = new { slot = player.Slot, enabled, spread, inaccuracy, weapon = message.ReadInt("item_def_index"), time = Server.CurrentTime };
        }
        catch (Exception error) { Report("nospread-shot-observation", error); }
        return HookResult.Continue;
    }

    private bool NoSpreadFor(CCSWeaponBase weapon)
    {
        if (_disposed || !_active || !NoSpreadReady || !weapon.IsValid || !Firearm(weapon)) return false;
        var owner = weapon.OwnerEntity.Value;
        if (owner is not { IsValid: true, DesignerName: "player" }) return false;
        var pawn = owner.As<CCSPlayerPawn>();
        var player = pawn.Controller.Value?.As<CCSPlayerController>();
        return Player(player) && player!.PlayerPawn.Raw == pawn.EntityHandle.Raw &&
            _players.TryGetValue(player.Slot, out var state) && state.UserId == player.UserId &&
            PersonalNoSpreadLogic.Applies(player.TeamNum, state.NoSpread, Alive(player), state.ResettingTeam);
    }

    private HookResult OnGetSpread(DynamicHook hook)
    {
        if (!NoSpreadFor(hook.GetParam<CCSWeaponBase>(0))) return HookResult.Continue;
        _zeroedSpread++; hook.SetReturn(0f); return HookResult.Stop;
    }

    private HookResult OnGetInaccuracy(DynamicHook hook)
    {
        if (!NoSpreadFor(hook.GetParam<CCSWeaponBase>(0))) return HookResult.Continue;
        for (int parameter = 1; parameter <= 2; parameter++)
        {
            IntPtr output = hook.GetParam<IntPtr>(parameter);
            if (output != IntPtr.Zero) Marshal.WriteInt32(output, 0);
        }
        _zeroedInaccuracy++; hook.SetReturn(0f); return HookResult.Stop;
    }

    private void NoSpreadCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!Player(player)) { _plugin.Reply(player, "游戏内人类可使用 !nospread on/off；省略参数切换。"); return; }
        var state = State(player!);
        if (command.ArgCount > 2 || !PublicGameplayLogic.TryToggle(command.ArgCount == 2 ? command.GetArg(1) : "", state.NoSpread, out bool enabled))
        { _plugin.Reply(player, "用法：!nospread on/off 或 1/0；省略参数切换。"); return; }
        SetNoSpread(player!, enabled);
    }

    private void SetNoSpread(CCSPlayerController player, bool enabled)
    {
        if (enabled && player.TeamNum != 3) { _plugin.Reply(player, "枪械无扩散仅在人类 CT 阵营启用；僵尸仍只使用刀。"); return; }
        if (enabled && !NoSpreadReady) { _plugin.Reply(player, "个人无扩散接口未就绪，请检查 native-nospread 日志。"); return; }
        State(player).NoSpread = enabled;
        SyncNoSpreadPrediction(player, State(player), true);
        _plugin.Reply(player, "个人枪械无扩散：" + (enabled ? "开启" : "关闭") + "；只影响自己，保留正常后坐力，变为僵尸时自动停用。");
    }

    private void VerifyNoSpread(CCSPlayerController? requester, CommandInfo command)
    {
        if (requester != null) { _plugin.Reply(requester, "该诊断仅供服务器控制台使用。"); return; }
        if (!NoSpreadReady) { _plugin.Reply(null, "NO_SPREAD_VERIFY not ready"); return; }
        if (command.ArgCount == 2 && command.GetArg(1) is "bots-on" or "bots-off")
        {
            if (Utilities.GetPlayers().Any(ZePracticePlugin.IsRealPlayer)) { _plugin.Reply(null, "Bot-only verification requires a server with no real players."); return; }
            bool enabled = command.GetArg(1) == "bots-on";
            foreach (var bot in Utilities.GetPlayers().Where(candidate => candidate.IsBot && candidate.TeamNum == 3)) State(bot).NoSpread = enabled;
            _plugin.Reply(null, "NO_SPREAD_VERIFY bot preferences=" + enabled);
            return;
        }
        var player = Utilities.GetPlayers().FirstOrDefault(candidate => Alive(candidate) && candidate.TeamNum == 3 &&
            candidate.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value is { IsValid: true } weapon && Firearm(weapon.As<CCSWeaponBase>()));
        if (player == null) { _plugin.Reply(null, "NO_SPREAD_VERIFY requires an alive CT holding a firearm; no player state changed."); return; }
        var state = State(player);
        var firearm = player.PlayerPawn.Value!.WeaponServices!.ActiveWeapon.Value!.As<CCSWeaponBase>();
        bool original = state.NoSpread;
        long spreadBefore = _zeroedSpread, inaccuracyBefore = _zeroedInaccuracy;
        try
        {
            state.NoSpread = false;
            float ordinarySpread = _spread!.Invoke(firearm);
            float ordinaryInaccuracy = _inaccuracy!.Invoke(firearm, IntPtr.Zero, IntPtr.Zero);
            state.NoSpread = true;
            float personalSpread = _spread.Invoke(firearm);
            float personalInaccuracy = _inaccuracy.Invoke(firearm, IntPtr.Zero, IntPtr.Zero);
            bool passed = personalSpread == 0 && personalInaccuracy == 0 && _zeroedSpread > spreadBefore && _zeroedInaccuracy > inaccuracyBefore;
            _plugin.Reply(null, $"NO_SPREAD_VERIFY passed={passed}; weapon={firearm.DesignerName}; ordinary={ordinarySpread}/{ordinaryInaccuracy}; personal={personalSpread}/{personalInaccuracy}; preferenceRestored={original}; no shots fired.");
        }
        finally { state.NoSpread = original; }
    }

    private void DisposeNoSpread()
    {
        ResetNoSpreadPrediction();
        if (_shotObserver) PluginCleanup.Run("nospread-unhook-observer", () => _plugin.UnhookUserMessage(452, ObserveShotPacket, HookMode.Post), Report);
        _shotObserver = false;
        PluginCleanup.Run("nospread-unhook-spread", () => _spread?.Unhook(OnGetSpread, HookMode.Pre), Report);
        PluginCleanup.Run("nospread-unhook-inaccuracy", () => _inaccuracy?.Unhook(OnGetInaccuracy, HookMode.Pre), Report);
        _spread = null; _inaccuracy = null;
    }
}
