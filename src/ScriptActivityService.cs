using System.Diagnostics;
using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;

namespace ZePractice;

/// <summary>Read-only observer. Never rewrites JS, callback arguments, return values or scheduling.</summary>
public sealed class ScriptActivityService(ZePracticePlugin plugin) : IDisposable
{
    private MemoryFunctionWithReturn<nint,nint,nint,int,nint,nint,int>? _hook;
    private readonly ScriptActivityAccumulator _counts = new();
    private bool _pre, _post, _active, _disposed;
    private int _thread;
    private string? _fault;
    private ScriptActivityAccumulator.Window? _window;
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    public const string Scope = "Completed server JS callbacks in the engine invocation wrapper (map/addon contexts); Think is a subset, not a separate script count. Contexts are window-local distinct identities, not files/entities. Exceptions are caught callback exceptions since this map/sampler start, not startup import errors or proof of map logic correctness. Wall time is outermost wrapper time including native calls and observer overhead, not pure JS CPU. No map scripts are modified.";
    public void Initialize()
    {
        _thread = Environment.CurrentManagedThreadId;
        try
        {
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
                throw new PlatformNotSupportedException("Script observer is verified for Windows x64.");
            string game = Server.GameDirectory;
            if (Directory.Exists(Path.Combine(game,"csgo"))) game = Path.Combine(game,"csgo");
            ScriptActivityNativeData.Validate(File.ReadAllBytes(Path.Combine(game,"bin","win64","server.dll")));
            _hook = new(ScriptActivityNativeData.Signature);
            _hook.Hook(Before,HookMode.Pre); _pre = true;
            _hook.Hook(After,HookMode.Post); _post = true;
        }
        catch (Exception e)
        {
            Fail(e.Message);
            Detach(); // A partial installation must not leave an unmatched observer.
        }
    }
    private void Fail(string reason)
    {
        if (Interlocked.CompareExchange(ref _fault,reason,null) == null)
            plugin.Logger.LogError("SCRIPT_ACTIVITY observer unavailable: {Reason}",reason);
    }
    private bool CanObserve()
    {
        if (_disposed || !_active || _fault != null) return false;
        if (Environment.CurrentManagedThreadId == _thread) return true;
        Fail("Script callbacks observed off the verified server thread.");
        return false;
    }
    private static bool IsThink(nint name)
    {
        // Bounded comparison with early exits: no per-call string allocations or unbounded reads.
        if (name == 0) return false;
        const string expected = "Think";
        for (int i=0;i<expected.Length;i++) if (Marshal.ReadByte(name,i) != expected[i]) return false;
        return Marshal.ReadByte(name,expected.Length) == 0;
    }
    private HookResult Before(DynamicHook h)
    {
        if (CanObserve())
            try { _counts.Enter(h.GetParam<nint>(0),IsThink(h.GetParam<nint>(1)),Now); }
            catch (Exception e) { Fail(e.Message); }
        return HookResult.Continue;
    }
    private HookResult After(DynamicHook h)
    {
        if (CanObserve())
            try { _counts.Exit(h.GetReturn<int>(),Now); }
            catch (Exception e) { Fail(e.Message); }
        return HookResult.Continue;
    }
    public void OnMapStart()
    { _counts.Reset(Now); _window=null; _active=true; }
    public void OnMapEnd()
    { _active=false; _window=null; _counts.Reset(Now); }
    public ScriptActivityLogic.Reading Sample(int entities)
    {
        bool ready = _active && !_disposed && _fault == null && _pre && _post;
        if (ready)
        {
            try { _window = _counts.Sample(Now) ?? _window; }
            catch (Exception e) { Fail(e.Message); ready=false; }
        }
        return new(ready,ready ? (_window == null ? "warming-up" : "current") : "observer-unavailable",
            entities,ready ? _window : null,_fault);
    }
    private void Detach()
    {
        if (_hook == null) return;
        try { if (_pre) { _hook.Unhook(Before,HookMode.Pre); _pre=false; } }
        finally { if (_post) { _hook.Unhook(After,HookMode.Post); _post=false; } }
        _hook=null;
    }
    public void Dispose()
    { if (_disposed) return; _disposed=true; _active=false; Detach(); _counts.Reset(Now); }
}
