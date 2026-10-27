namespace ZePractice;
public static class RadioPanelLogic
{
    public const int PageSize = 6;
    public static int PageCount(int count) => (int)Math.Max(1L, ((long)Math.Max(0, count) + PageSize - 1) / PageSize);
    public static int ClampPage(int page, int count) => Math.Clamp(page, 0, PageCount(count) - 1);
    public static bool TrySelection(int key, int page, int count, out int index)
    {
        long candidate = (long)page * PageSize + key - 1;
        index = -1;
        if (key is < 1 or > PageSize || page < 0 || candidate < 0 || candidate >= count) return false;
        index = (int)candidate;
        return true;
    }
}
public sealed class RadioPanelRequests
{
    private readonly Dictionary<int, long> _pending = new();
    private long _sequence;
    public int Count => _pending.Count;
    public long Enqueue(int slot) => _pending[slot] = ++_sequence;
    public bool Contains(int slot) => _pending.ContainsKey(slot);
    public bool Take(int slot, long request)
    {
        if (!_pending.TryGetValue(slot, out long latest) || latest != request) return false;
        return _pending.Remove(slot);
    }
    public void Cancel(int slot) => _pending.Remove(slot);
    public void Clear() => _pending.Clear();
}
