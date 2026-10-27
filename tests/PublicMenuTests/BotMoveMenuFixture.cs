using System.Numerics;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace ZePractice;

// Only engine/lifecycle seams are stubbed. Menu building, argument dispatch and all
// menu callbacks come directly from the production PracticeBotService.Menus.cs.
public sealed partial class PracticeBotService(ZePracticePlugin plugin)
{
    private readonly PracticeBotRoster _roster = new();
    private sealed class TestLive(PracticeBotStatistics stats) { public PracticeBotStatistics Stats = stats; }
    private readonly Dictionary<long, TestLive> _live = new();
    private bool _active = true, _disposed;
    private int _menuGeneration;
    private long _connection = 1;
    public bool Alive = true;
    public readonly List<string> Moves = new();
    private PracticeIdentity Identity(CCSPlayerController p) => new(p.Slot, p.EntityHandle.Raw, p.UserId, _connection);
    private bool Matches(CCSPlayerController p, PracticeIdentity owner) => p.IsValid && Identity(p) == owner;
    private bool InGame(CCSPlayerController? p, bool alive) => !_disposed && _active &&
        ZePracticePlugin.IsRealPlayer(p) && (!alive || Alive && p!.TeamNum is 2 or 3);
    private void Move(CCSPlayerController p, string arg) => Moves.Add(arg);
    public void Invoke(CCSPlayerController p, params string[] args) => MoveCommand(p, new CommandInfo(args));
    public PracticeBotRoster.Entry Seed(CCSPlayerController p, int team, int health = 100, bool pending = false)
    {
        var entry = _roster.Reserve(Identity(p), new(Vector3.Zero, Vector3.Zero, team, health, 0), true, 2, 0)!;
        if (!pending) _roster.Bind(entry, new(50, (uint)(1000 + entry.Id), (int)(1000 + entry.Id)));
        _live[entry.Id] = new(new(health, 0, 0));
        return entry;
    }
    public void Retire(PracticeBotRoster.Entry entry) => _roster.Retire(entry);
    public void NewConnection() => _connection++;
    public void NextMap() { _menuGeneration++; _roster.Clear(); }
    public void Stop() { _disposed = true; _active = false; }
}
