using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
namespace ZePractice;

public sealed class PracticeRoundService : IDisposable
{
    private readonly ZePracticePlugin _plugin;
    private VirtualFunctionVoid<CCSGameRules, bool>? _intermission;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _retry;
    private bool _disposed;
    private int _attempts, _offset;
    private long _blocked;
    private readonly ManualRoundGate _manual = new();
    private MemoryFunctionVoid<IntPtr, float, CounterStrikeSharp.API.Modules.Entities.Constants.RoundEndReason, IntPtr, byte>? _roundEnd;
    private long _automaticEndsBlocked;
    public PracticeRoundService(ZePracticePlugin plugin)
    {
        _plugin = plugin;
        _plugin.RegisterListener<Listeners.OnTick>(MaintainRound);
    }
    public void OnMapStart()
    {
        _manual.MapStart();
        _retry?.Kill();
        _retry = null;
        _attempts = 0;
        if (_disposed || _intermission != null) return;
        _retry = _plugin.AddTimer(0.25f, EnsureHook, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }
    public void OnMapEnd()
    {
        _retry?.Kill();
        _retry = null;
    }
    private static CCSGameRules? Rules() => Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
    private void EnsureHook()
    {
        if (_disposed || _intermission != null) { OnMapEnd(); return; }
        if (++_attempts > 20) { OnMapEnd(); return; }
        var rules = Rules();
        if (rules == null || rules.Handle == IntPtr.Zero) return;
        try
        {
            string game = Server.GameDirectory;
            if (Directory.Exists(Path.Combine(game, "csgo"))) game = Path.Combine(game, "csgo");
            string gamedata = Path.Combine(game, "addons", "cs2fixes", "gamedata", "cs2fixes.games.txt");
            _offset = PracticeRoundLogic.ReadIntermissionOffset(File.ReadAllText(gamedata), OperatingSystem.IsWindows());
            var hook = new VirtualFunctionVoid<CCSGameRules, bool>(rules, _offset);
            if (hook.Handle == IntPtr.Zero) throw new InvalidOperationException("Intermission virtual function resolution returned zero.");
            hook.Hook(OnIntermission, HookMode.Pre);
            _intermission = hook;
            var terminate = CounterStrikeSharp.API.Modules.Memory.VirtualFunctions.TerminateRoundFuncWindows;
            terminate.Hook(OnRoundEnd, HookMode.Pre);
            _roundEnd = terminate;
            OnMapEnd();
            _plugin.Logger.LogInformation("ZE_ROUND_GUARD installed intermission hook at named CS2Fixes vtable offset {Offset}; practice-only={Enabled}", _offset, _plugin.Config.PreventPracticeIntermission);
        }
        catch (Exception error)
        {
            OnMapEnd();
            _plugin.Logger.LogError(error, "ZE_ROUND_GUARD unavailable; verify installed CS2Fixes gamedata before claiming freeze protection.");
        }
    }
    private HookResult OnIntermission(DynamicHook hook)
    {
        if (!_plugin.Config.PreventPracticeIntermission || _disposed) return HookResult.Continue;
        _blocked++;
        if (_blocked <= 3) _plugin.Logger.LogWarning("ZE_ROUND_GUARD blocked match intermission during practice; map={Map}, count={Count}", Server.MapName, _blocked);
        return HookResult.Stop;
    }
    private HookResult OnRoundEnd(DynamicHook hook)
    {
        if (_disposed || !_manual.Blocks(Server.CurrentTime)) return HookResult.Continue;
        _automaticEndsBlocked++;
        if (_automaticEndsBlocked <= 3) _plugin.Logger.LogWarning("ZE_ROUND_GUARD ignored automatic round end; count={Count}", _automaticEndsBlocked);
        return HookResult.Stop;
    }
    public void RoundStarted() => _manual.RoundStarted();
    public void PermitManualEnd() => _manual.Permit(Server.CurrentTime, 5);
    private void MaintainRound()
    {
        if (_disposed) return;
        var rules = Rules();
        if (rules == null || rules.WarmupPeriod) return;
        if (!_manual.Started) _manual.RoundStarted();
        if (!_manual.Blocks(Server.CurrentTime)) return;
        if (rules.RestartRoundTime > 0) rules.RestartRoundTime = 0;
        if (rules.GameRestart) rules.GameRestart = false;
        if (rules.CompleteReset) rules.CompleteReset = false;
    }
    public string Diagnostics()
    {
        var rules = Rules();
        return JsonSerializer.Serialize(new
        {
            guardInstalled = _intermission != null && _roundEnd != null, offset = _offset, blocked = _blocked,
            automaticEndsBlocked = _automaticEndsBlocked, manualEndPermittedUntil = _manual.AllowedUntil,
            practice = _plugin.IsPractice, enabled = _plugin.Config.PreventPracticeIntermission,
            now = Server.CurrentTime, freeze = rules?.FreezePeriod, warmup = rules?.WarmupPeriod,
            lastThink = rules?.LastThinkTime, intermissionEnd = rules?.IntermissionEndTime,
            roundStart = rules?.RoundStartTime, roundTime = rules?.RoundTime
        });
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _plugin.RemoveListener<Listeners.OnTick>(MaintainRound);
        OnMapEnd();
        _roundEnd?.Unhook(OnRoundEnd, HookMode.Pre);
        _roundEnd = null;
        _intermission?.Unhook(OnIntermission, HookMode.Pre);
        _intermission = null;
    }
}
