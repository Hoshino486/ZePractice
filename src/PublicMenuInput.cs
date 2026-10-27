using System.Globalization;

namespace ZePractice;

/// <summary>One bounded numeric chat entry, tied to the original connection, never a console script.</summary>
public sealed class PublicMenuInput
{
    public sealed record Request(int? UserId, string Command, double Minimum, double Maximum, bool Fractional, double Expires);
    public enum Result { Ignored, Accepted, Rejected, Cancelled, Expired }
    private readonly Dictionary<int, Request> _pending = new();
    private static readonly HashSet<string> Commands = ["css_ze_hp", "css_ze_armor", "css_ze_bot_hp", "css_ze_music", "css_ze_hide", "css_ze_roundtime", "css_ze_workshop"];
    public void Begin(int slot, int? userId, string command, double minimum, double maximum, bool fractional, double now)
    {
        if (!Commands.Contains(command) || !double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum)
            throw new ArgumentException("Unsupported public numeric input");
        _pending[slot] = new(userId, command, minimum, maximum, fractional, now + 60);
    }
    public void Remove(int slot) => _pending.Remove(slot);
    public void Clear() => _pending.Clear();
    public Result Consume(int slot, int? userId, string text, double now, out string commandLine, out string error)
    {
        commandLine = error = "";
        if (!_pending.TryGetValue(slot, out var request)) return Result.Ignored;
        if (request.UserId != userId) { _pending.Remove(slot); return Result.Ignored; }
        text = text.Trim();
        if (now >= request.Expires) { _pending.Remove(slot); return Result.Expired; }
        if (text.Equals("cancel", StringComparison.OrdinalIgnoreCase) || text == "取消")
        { _pending.Remove(slot); return Result.Cancelled; }
        // An ordinary message or another explicit command is never swallowed by a menu.
        if (text.Length == 0 || !(char.IsAsciiDigit(text[0]) || text[0] is '+' or '-' or '.')) return Result.Ignored;
        bool valid;
        string value = "";
        if (request.Command == "css_ze_workshop")
            valid = PublicGameplayLogic.TryWorkshopId(text, out value);
        else if (request.Fractional)
        {
            valid = PublicGameplayLogic.TryVolume(text, out float number);
            value = number.ToString("0.########", CultureInfo.InvariantCulture);
        }
        else
        {
            valid = PublicGameplayLogic.TryInteger(text, checked((int)request.Minimum), checked((int)request.Maximum), out int number);
            value = number.ToString(CultureInfo.InvariantCulture);
        }
        if (!valid) { error = $"请输入 {request.Minimum.ToString(CultureInfo.InvariantCulture)}..{request.Maximum.ToString(CultureInfo.InvariantCulture)} 内的" + (request.Fractional ? "数值" : "整数") + "；输入 取消 退出。"; return Result.Rejected; }
        _pending.Remove(slot); commandLine = request.Command + " " + value;
        return Result.Accepted;
    }
}
