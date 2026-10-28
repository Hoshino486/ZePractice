using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace ZePractice;

/// <summary>
/// Keeps the engine's native team-ID tag (name + health bar) visible for teammates on every
/// client, matching the observer/teammate look. Enemies are covered by PlayerPlateService
/// because the engine never draws the native tag for the opposing team.
/// </summary>
public sealed class TeammateTagService(ZePracticePlugin plugin) : IDisposable
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
