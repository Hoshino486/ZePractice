using System.Globalization;

namespace ZePractice;

/// <summary>Counts completed engine JS callbacks, not entities, attempted lookups or VProf counters.</summary>
public sealed class ScriptActivityAccumulator
{
    private readonly record struct Frame(nint Context, bool Think, double Started);
    private readonly Stack<Frame> _stack = new();
    private readonly HashSet<nint> _contexts = new();
    private long _calls, _thinks, _errors, _other, _totalCalls, _totalErrors;
    private double _since, _busyMs, _last = double.NaN;
    public int Depth => _stack.Count;
    public sealed record Window(double Seconds, long Callbacks, long ThinkCallbacks, long Errors,
        long OtherResults, int ActiveContexts, double CallbackWallMs, long TotalCallbacks,
        long TotalErrors, double? LastCallbackAgoSeconds)
    {
        public double CallbacksPerSecond => Callbacks / Seconds;
        public double ThinkPerSecond => ThinkCallbacks / Seconds;
    }
    public void Reset(double now)
    {
        _stack.Clear(); _contexts.Clear(); _calls = _thinks = _errors = _other = _totalCalls = _totalErrors = 0;
        _since = now; _busyMs = 0; _last = double.NaN;
    }
    public void Enter(nint context, bool think, double now)
    {
        if (_stack.Count >= 256) throw new InvalidOperationException("Script callback nesting exceeds sampling capacity.");
        _stack.Push(new(context, think, now));
    }
    public void Exit(int result, double now)
    {
        if (!_stack.TryPop(out var frame)) throw new InvalidOperationException("Unpaired script callback return.");
        if (now < frame.Started) throw new InvalidOperationException("Non-monotonic script sample clock.");
        if (_contexts.Count >= 32768 && !_contexts.Contains(frame.Context))
            throw new InvalidOperationException("Script context sampling capacity exceeded.");
        _contexts.Add(frame.Context);
        _calls++; _totalCalls++; if (frame.Think) _thinks++;
        if (result == 2) { _errors++; _totalErrors++; }
        else if (result != 1) _other++; // Not silently counted as successful.
        // Outermost wrapper time only: nested calls must not double-count wall time.
        if (_stack.Count == 0) _busyMs += (now - frame.Started) * 1000;
        _last = now;
    }
    public Window? Sample(double now)
    {
        double seconds = now - _since;
        if (!double.IsFinite(seconds) || seconds < .1) return null;
        if (_stack.Count != 0) throw new InvalidOperationException("Script sample requested during an unfinished callback.");
        var result = new Window(seconds, _calls, _thinks, _errors, _other, _contexts.Count, _busyMs,
            _totalCalls, _totalErrors, double.IsNaN(_last) ? null : Math.Max(0, now - _last));
        _calls = _thinks = _errors = _other = 0; _busyMs = 0; _contexts.Clear(); _since = now;
        return result;
    }
}

public static class ScriptActivityLogic
{
    public sealed record Reading(bool Available, string Status, int Entities,
        ScriptActivityAccumulator.Window? Window, string? Error = null);
    public static string Summary(Reading? r)
    {
        if (r == null) return "脚本采样初始化中";
        string entities = $"脚本实体 {r.Entities}";
        if (!r.Available) return entities + " | 采样接口未就绪（非地图未运行）";
        if (r.Window is not { } w) return entities + " | 采样中";
        return string.Create(CultureInfo.InvariantCulture,
            $"{entities} | 活动上下文 {w.ActiveContexts} | 回调 {w.CallbacksPerSecond:0.0}/s | Think {w.ThinkPerSecond:0.0}/s | 异常 {w.Errors}（累计 {w.TotalErrors}）") +
            (w.OtherResults > 0 ? " | 返回值待核验" : w.Callbacks == 0 ? (r.Entities == 0 ? " | 无脚本实体" : " | 本窗无回调") : "");
    }
    public static string Color(Reading? r) => r is not { Available: true, Window: { } } ? "unknown" :
        r.Window.Errors > 0 ? "critical" : r.Window.TotalErrors > 0 || r.Window.OtherResults > 0 ? "warning" :
        r.Window.Callbacks == 0 && r.Entities > 0 ? "unknown" : "good";
}
