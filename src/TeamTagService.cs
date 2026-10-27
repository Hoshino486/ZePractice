using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace ZePractice;

/// <summary>
/// Forces the engine's own overhead team-ID tag (name + health bar, team colors) to stay
/// visible for every client, matching what an observer sees, instead of a custom worldtext.
/// The server side (sv_teamid_overhead*) is applied in ApplyServerRules.
/// </summary>
public sealed class TeamTagService(ZePracticePlugin plugin) : IDisposable
{
    private static readonly string[] ClientCommands =
    [
        "cl_teamid_overhead_always 1",
        "cl_teamid_overhead_maxdist 0"
    ];

    public void Initialize()
    {
        plugin.RegisterEventHandler<EventPlayerSpawn>(OnSpawn);
        plugin.RegisterListener<Listeners.OnClientPutInServer>(OnPutInServer);
    }

    public void OnMapStart()
    {
        foreach (var player in Utilities.GetPlayers()) Send(player);
    }

    private HookResult OnSpawn(EventPlayerSpawn gameEvent, GameEventInfo info)
    {
        Send(gameEvent.Userid);
        return HookResult.Continue;
    }

    private void OnPutInServer(int slot) => Send(Utilities.GetPlayerFromSlot(slot));

    private static void Send(CCSPlayerController? player)
    {
        if (player is not { IsValid: true } || player.IsBot || player.IsHLTV) return;
        foreach (string command in ClientCommands) player.ExecuteClientCommand(command);
    }

    public void Dispose()
    {
        plugin.DeregisterEventHandler<EventPlayerSpawn>(OnSpawn);
        plugin.RemoveListener<Listeners.OnClientPutInServer>(OnPutInServer);
    }
}
