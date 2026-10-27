using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ZePractice;

/// <summary>One compiled player model on both teams. Never edits map scripts or weapon/item identity.</summary>
public sealed class ServerPlayerModelService(ZePracticePlugin plugin) : IDisposable
{
    private readonly Dictionary<uint, (CCSPlayerPawn Pawn, string Original)> _originals = new();
    private readonly HashSet<string> _errors = new();
    private float _next;
    private bool _active, _disposed;
    private long _applied;
    public void Initialize()
    {
        plugin.RegisterListener<Listeners.OnServerPrecacheResources>(Precache);
        plugin.RegisterListener<Listeners.OnTick>(Tick);
        plugin.AddZeCommand("css_ze_model_status", "全服人物模型诊断", StatusCommand);
    }
    private void Precache(ResourceManifest manifest)
    { if (plugin.Config.ServerModels.Enabled) manifest.AddResource(plugin.Config.ServerModels.PlayerModel); }
    public void OnMapStart() { _active = plugin.Config.ServerModels.Enabled; _next = 0; _originals.Clear(); _errors.Clear(); _applied = 0; }
    public void OnMapEnd() { _active = false; _originals.Clear(); }
    private static string Model(CCSPlayerPawn pawn) => pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName ?? "";
    public void Apply(CCSPlayerController player)
    {
        if (!_active || _disposed || player is not { IsValid: true, IsHLTV: false, PawnIsAlive: true } || player.TeamNum is not (2 or 3)) return;
        if (player.PlayerPawn.Value is not { IsValid: true } pawn) return;
        string current = Model(pawn), desired = plugin.Config.ServerModels.PlayerModel;
        if (string.Equals(current, desired, StringComparison.OrdinalIgnoreCase)) return;
        _originals.TryAdd(pawn.EntityHandle.Raw, (pawn, current));
        pawn.SetModel(desired); _applied++;
    }
    private void Tick()
    {
        if (!_active || _disposed || Server.CurrentTime < _next) return;
        _next = Server.CurrentTime + 0.25f;
        try
        {
            foreach (var player in Utilities.GetPlayers()) Apply(player);
            foreach (var entry in _originals.ToArray())
                if (!entry.Value.Pawn.IsValid || entry.Value.Pawn.EntityHandle.Raw != entry.Key) _originals.Remove(entry.Key);
        }
        catch (Exception error) { if (_errors.Add(error.GetType().Name)) plugin.Logger.LogError(error, "Server player model apply failed"); }
    }
    public string Diagnostics() => JsonSerializer.Serialize(new { active = _active, model = plugin.Config.ServerModels.PlayerModel, applied = _applied,
        players = Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive && p.PlayerPawn.Value is { IsValid: true }).Select(p => new { slot = p.Slot, team = p.TeamNum, model = Model(p.PlayerPawn.Value!) }),
        errors = _errors.ToArray(), visualAcceptance = "requires-real-client" });
    private void StatusCommand(CCSPlayerController? p, CommandInfo c) => plugin.Reply(p, Diagnostics());
    public void Dispose()
    {
        _disposed = true; _active = false;
        foreach (var entry in _originals)
            if (entry.Value.Pawn.IsValid && entry.Value.Pawn.EntityHandle.Raw == entry.Key && !string.IsNullOrEmpty(entry.Value.Original) && Model(entry.Value.Pawn) == plugin.Config.ServerModels.PlayerModel)
                entry.Value.Pawn.SetModel(entry.Value.Original);
        _originals.Clear(); plugin.RemoveListener<Listeners.OnTick>(Tick); plugin.RemoveListener<Listeners.OnServerPrecacheResources>(Precache);
        plugin.RemoveZeCommand("css_ze_model_status", StatusCommand);
    }
}
