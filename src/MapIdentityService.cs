using System.Globalization;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;

namespace ZePractice;

public sealed class MapIdentityService(ZePracticePlugin plugin) : IDisposable
{
    private readonly Dictionary<int, MapIdentityBinding> _bindings = new();
    private readonly HashSet<string> _errors = new();
    private bool _active, _disposed;
    private float _nextCheck;
    private long _published;

    public void Initialize()
    {
        plugin.RegisterListener<Listeners.OnTick>(Tick);
        plugin.RegisterListener<Listeners.OnClientDisconnect>(Invalidate);
    }

    public void OnMapStart() { _active = true; _bindings.Clear(); _nextCheck = 0; }
    public void OnMapEnd() { _active = false; _bindings.Clear(); }
    public void Invalidate(int slot) { _bindings.Remove(slot); _nextCheck = 0; }
    private static bool Enabled() => ConVar.Find("cs2f_map_steamids_enable")?.GetPrimitiveValue<bool>() == true;

    private void Tick()
    {
        if (_disposed || !_active || Server.CurrentTime < _nextCheck) return;
        _nextCheck = Server.CurrentTime + 0.5f;
        if (!Enabled()) { _bindings.Clear(); return; }
        foreach (var player in Utilities.GetPlayers().Where(ZePracticePlugin.IsRealPlayer))
        {
            try
            {
                ulong? identity = MapIdentityLogic.Verified(player.SteamID, player.AuthorizedSteamID?.SteamId64, player.IsBot);
                if (identity == null) { _bindings.Remove(player.Slot); continue; }
                var pawn = player.PlayerPawn.Value;
                if (pawn is not { IsValid: true } || plugin.IsTeamResetPending(player)) continue;
                if (_bindings.TryGetValue(player.Slot, out var binding) && binding.Matches(player.UserId, pawn.EntityHandle.Raw, identity.Value)) continue;
                string value = identity.Value.ToString(CultureInfo.InvariantCulture);
                pawn.AcceptInput("AddAttribute", pawn, pawn, value);
                player.AcceptInput("AddAttribute", player, player, value);
                var wakeup = new EventPlayerConnect(true) { Userid = player, Name = player.PlayerName, Bot = false };
                wakeup.Set("steam_id", value);
                wakeup.FireEvent(true);
                binding ??= new();
                binding.Published(player.UserId, pawn.EntityHandle.Raw, identity.Value);
                _bindings[player.Slot] = binding;
                _published++;
                plugin.Logger.LogInformation("MAP_IDENTITY authenticated identity forwarded for slot {Slot}, pawn {Pawn}.", player.Slot, pawn.EntityHandle.Raw);
            }
            catch (Exception error)
            {
                if (_errors.Add(error.GetType().Name + ":" + error.Message)) plugin.Logger.LogError(error, "MAP_IDENTITY publish failed");
            }
        }
    }

    public string Diagnostics(CCSPlayerController? viewer) => JsonSerializer.Serialize(new
    {
        active = _active, enabled = Enabled(), lan = ConVar.Find("sv_lan")?.StringValue,
        published = _published, errors = _errors,
        players = Utilities.GetPlayers().Where(ZePracticePlugin.IsRealPlayer).Where(player => viewer == null || player.Slot == viewer.Slot).Select(player =>
        {
            ulong? authorized = player.AuthorizedSteamID?.SteamId64;
            ulong? verified = MapIdentityLogic.Verified(player.SteamID, authorized, player.IsBot);
            return new { slot = player.Slot, player.PlayerName, reported = player.SteamID.ToString(CultureInfo.InvariantCulture),
                authorized = authorized?.ToString(CultureInfo.InvariantCulture), eligible = verified != null,
                forwarded = verified != null && _bindings.TryGetValue(player.Slot, out var binding) && binding.Matches(player.UserId, player.PlayerPawn.Raw, verified.Value) };
        }).ToArray()
    });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _active = false; _bindings.Clear();
        plugin.RemoveListener<Listeners.OnTick>(Tick);
        plugin.RemoveListener<Listeners.OnClientDisconnect>(Invalidate);
    }
}
