namespace ZePractice;

// Cleanup also runs after a partial Load, before CS2 has any global/map variables.
internal static class PluginCleanup
{
    public static void Run(string operation, Action action, Action<string, Exception> report)
    {
        try { action(); }
        catch (Exception error) { report(operation, error); }
    }

    public static void ForPlayers<T>(bool mapActive, int trackedCount, Func<IEnumerable<T>> players,
        Action<T> restore, string operation, Action<string, Exception> report)
    {
        if (!mapActive || trackedCount == 0) return;
        Run(operation, () =>
        {
            foreach (var player in players()) Run(operation, () => restore(player), report);
        }, report);
    }
}
