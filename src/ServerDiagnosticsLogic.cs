using System.Globalization;
using System.Text.Json;

namespace ZePractice;

public static class ServerDiagnosticsLogic
{
    public const uint NetworkEntityLimit = 1u << 14; // Source 2 SDK const.h MAX_EDICT_BITS.
    public const uint TotalEntityLimit = 1u << 15;
    public sealed record NativeSample(int Version, uint Sequence, int Tick, int Samples, double WindowSeconds,
        double SimMeanMs, double SimPeakMs, double Tps, bool ScriptAvailable, ulong ScriptOnTick);
    public sealed record Reading(int Entities, int Networked, int Humans, int Bots, double? MemoryMiB,
        double UptimeSeconds, NativeSample? Native, string NativeStatus, ScriptActivityLogic.Reading? Script = null);
    // Missing numbers must not silently deserialize as 0 and look like a healthy server.
    private static readonly JsonSerializerOptions Json = new()
        { PropertyNameCaseInsensitive=true, RespectRequiredConstructorParameters=true };
    public static NativeSample? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length>2048) return null;
        try
        {
            var s=JsonSerializer.Deserialize<NativeSample>(raw,Json);
            return s is {Version:1, Sequence:>0, Tick:>=0, Samples:>0 and <100000} &&
                double.IsFinite(s.WindowSeconds) && s.WindowSeconds is >=.1 and <=60 &&
                double.IsFinite(s.SimMeanMs) && s.SimMeanMs is >=0 and <=60000 &&
                double.IsFinite(s.SimPeakMs) && s.SimPeakMs>=s.SimMeanMs && s.SimPeakMs<=60000 &&
                double.IsFinite(s.Tps) && s.Tps is >0 and <=10000 ? s:null;
        }
        catch(JsonException) { return null; }
    }
    public static bool IsNetworkedIndex(uint index) => index<NetworkEntityLimit;
    public static string LoadColor(double? milliseconds) => milliseconds is not { } ms || !double.IsFinite(ms) ? "unknown" :
        ms>=15.625 ? "critical" : ms>=7.8125 ? "warning" : "good";
    public static string EntityColor(int count,int limit) => count>=limit*.9 ? "critical" : count>=limit*.75 ? "warning" : "good";
    public static string Ms(double? value) => value.HasValue ? value.Value.ToString("0.00",CultureInfo.InvariantCulture)+"ms" : "--";
    public static string Uptime(double seconds)
    {
        if(!double.IsFinite(seconds)||seconds<0)return "--";
        var time=TimeSpan.FromSeconds(Math.Min(seconds,TimeSpan.MaxValue.TotalSeconds/2));
        return $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
    }
    public static bool TryToggle(string arg,bool current,out bool enabled)
    {
        enabled=arg.ToLowerInvariant() switch {"on" or "1"=>true,"off" or "0"=>false,""=>!current,_=>current};
        return arg.ToLowerInvariant() is "" or "on" or "off" or "1" or "0";
    }
    public static string Summary(Reading r) => string.Create(CultureInfo.InvariantCulture,
        $"[Diagnostics] {r.Entities} Entities ({r.Networked} Networked) | SV(sim) {Ms(r.Native?.SimMeanMs)} (峰值 {Ms(r.Native?.SimPeakMs)}) | TPS {r.Native?.Tps.ToString("0.0",CultureInfo.InvariantCulture)??"--"}/64 | {ScriptActivityLogic.Summary(r.Script)} | RAM {r.MemoryMiB?.ToString("0",CultureInfo.InvariantCulture)??"--"} MiB | 真人 {r.Humans} / Bot {r.Bots} | 运行 {Uptime(r.UptimeSeconds)}");
}
