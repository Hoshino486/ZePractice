using System.Drawing;
using System.Numerics;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ZePractice;

public sealed class CommanderService(ZePracticePlugin plugin) : IDisposable
{
    public const string MarkerModel = "models/saibochana/commander/marker.vmdl";
    // Engine-documented unlimited glow range (base.fgd).
    public const int FullWorldGlowRange = CommanderVisualStyle.UnlimitedRange;
    private static readonly Color LeaderColor = Color.FromArgb(255, 255, 210, 20);
    private static readonly Color[] Colors = [Color.FromArgb(255, 255, 65, 65), Color.FromArgb(255, 60, 255, 85),
        Color.FromArgb(255, 50, 150, 255), Color.FromArgb(255, 255, 215, 30), Color.FromArgb(255, 215, 65, 255)];
    private static readonly string[] ColorNames = ["红", "绿", "蓝", "黄", "紫"];
    private readonly CommanderMarkerState _state = new();
    private readonly CommanderCountdown _countdown = new();
    private CommanderResources.Availability _resources = new(false, "awaiting-map-precache", "");
    private readonly CommanderDrawing _drawing = new(plugin);
    private readonly Dictionary<long, Visual> _visuals = new();
    private readonly HashSet<string> _errors = new();
    private CCSPlayerController? _leader;
    private uint _leaderHandle, _pawnHandle;
    private CCSPlayerPawn? _pawn;
    private readonly CommanderLeaderGlow _leaderGlow = new();
    private bool _active, _disposed;
    private float _nextTick;
    private int _generation;
    private long _pings, _extensions, _transmitted;
    private sealed record Visual(CDynamicProp Icon) { public float RefreshAt; }
    public void Initialize()
    {
        plugin.RegisterListener<Listeners.OnServerPrecacheResources>(Precache);
        plugin.RegisterListener<Listeners.OnTick>(Tick);
        plugin.RegisterListener<Listeners.CheckTransmit>(Transmit);
        plugin.RegisterEventHandler<EventPlayerPing>(Ping, HookMode.Pre);
        plugin.RegisterEventHandler<EventPlayerDeath>(Died);
        plugin.RegisterEventHandler<EventPlayerTeam>(TeamChanged);
        plugin.RegisterListener<Listeners.OnClientDisconnect>(Disconnected);
        plugin.AddZeCommand("css_leader", "人类上/下指挥：!leader [on|off]", LeaderCommand);
        plugin.AddZeCommand("css_ze_leader", "人类上/下指挥", LeaderCommand);
        plugin.AddZeCommand("css_unleader", "下指挥，保留现有标点直到到期", LeaveCommand);
        plugin.AddZeCommand("css_clearpoints", "指挥清除全部标点", ClearCommand);
        plugin.AddZeCommand("css_ze_clearpoints", "指挥清除全部标点", ClearCommand);
        plugin.AddZeCommand("css_ze_commander_status", "指挥与标点诊断", StatusCommand);
    }
    private void Precache(ResourceManifest manifest)
    {
        if (!plugin.Config.Commander.Enabled) return;
        manifest.AddResource(MarkerModel);
        _resources = CommanderResources.Installed(Server.GameDirectory);
        _countdown.ModelResourcesReady = _resources.Ready;
        if (_resources.Ready) CommanderCountdown.Precache(manifest);
        plugin.Logger.LogInformation("COMMANDER_RESOURCES ready={Ready} status={Status}; downloaded updates activate on next map load, never mid-round", _resources.Ready, _resources.Status);
        manifest.AddResource(CommanderDrawing.BeamMaterial);
    }
    public void OnMapStart()
    {
        _generation++; _active = plugin.Config.Commander.Enabled; _nextTick = 0;
        _leader = null; _pawn = null; _pawnHandle = _leaderHandle = 0; _leaderGlow.Clear(false); _state.Clear(); _visuals.Clear(); _errors.Clear();
        _pings = _extensions = _transmitted = 0;
        _drawing.Reset(false); _countdown.Reset(false);
    }
    public void OnHotReload()
    {
        if (!_active || _disposed) return;
        _resources = CommanderResources.Installed(Server.GameDirectory);
        _countdown.ModelResourcesReady = false;
        try
        {
            if (_resources.Ready)
                _resources = _resources with { Ready = CommanderMountedModels.Verify(), Status = "mounted-countdown-models-unavailable" };
            if (_resources.Ready) _resources = _resources with { Status = "verified-mounted-models-after-hot-reload" };
            _countdown.ModelResourcesReady = _resources.Ready;
            plugin.Logger.LogInformation("COMMANDER_HOTLOAD_RESOURCES ready={Ready} status={Status}; no map reload or addon remount", _resources.Ready, _resources.Status);
        }
        catch (Exception error)
        {
            _resources = _resources with { Ready = false, Status = "hot-reload-model-check-failed" };
            Report("hot-reload-resources", error);
        }
    }
    public void OnMapEnd()
    { _active = false; _generation++; _leader = null; _pawn = null; _leaderGlow.Clear(false); _state.Clear(); _visuals.Clear(); _drawing.Reset(false); _countdown.Reset(false); }
    public void OnRoundStart() { Release(false); ClearMarkers(); _drawing.Reset(true); }
    private static bool Eligible(CCSPlayerController? p) => CommanderMarkerState.Eligible(p is { IsValid: true, IsHLTV: false }, p?.PawnIsAlive == true, p?.TeamNum ?? 0);
    public bool IsCommander(CCSPlayerController player) => _active && _leader is { IsValid: true } &&
        player.IsValid && player.EntityHandle.Raw == _leaderHandle && Eligible(player);
    private void LeaderCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true }) { plugin.Reply(player, "在游戏聊天输入 !leader；仅存活人类可上指挥。"); return; }
        string arg = command.GetArg(1).ToLowerInvariant();
        if (arg is not ("" or "on" or "off" or "1" or "0")) { plugin.Reply(player, "用法：!leader [on|off]"); return; }
        if (arg is "off" or "0" || (arg == "" && IsCommander(player))) { LeaveCommand(player, command); return; }
        TryAcquire(player);
    }
    public bool TryAcquire(CCSPlayerController player)
    {
        if (!_active || _disposed) { plugin.Reply(player, "指挥资源尚未启用。"); return false; }
        if (!Eligible(player)) { plugin.Reply(player, "只有存活的人类（CT）才能上指挥。"); return false; }
        if (_leader != null && !IsCommander(_leader)) Release(false);
        if (_leader != null) { plugin.Reply(player, IsCommander(player) ? "已经是指挥，中键标点 / !clearpoints 清空。" : "已有一名指挥，待其下指挥后即可接任。"); return false; }
        _leader = player; _leaderHandle = player.EntityHandle.Raw;
        try { ApplyLeaderGlow(); }
        catch (Exception error) { Report("leader-glow", error); Release(false); plugin.Reply(player, "指挥发光初始化失败，已撤销；请查看服务器日志。"); return false; }
        Server.PrintToChatAll($" \x04[指挥]\x01 {CleanName(player.PlayerName)} 已上指挥；中键标点/续时，按住R画线。!clearpoints 清点，!unleader 下指挥（保留标点计时）。");
        return true;
    }
    private void LeaveCommand(CCSPlayerController? p, CommandInfo command)
    { if (p != null && IsCommander(p)) Release(true); else plugin.Reply(p, "当前不是指挥。"); }
    private void ClearCommand(CCSPlayerController? p, CommandInfo command)
    { if (p != null && IsCommander(p)) { ClearMarkers(); plugin.Reply(p, "已清除全部指挥标点。"); } else plugin.Reply(p, "只有当前指挥可以清空标点。"); }
    private HookResult Died(EventPlayerDeath ev, GameEventInfo info)
    { if (ev.Userid is { IsValid: true } p && p.EntityHandle.Raw == _leaderHandle) Release(true); return HookResult.Continue; }
    private HookResult TeamChanged(EventPlayerTeam ev, GameEventInfo info)
    { if (ev.Userid is { IsValid: true } p && p.EntityHandle.Raw == _leaderHandle) Release(true); return HookResult.Continue; }
    private void Disconnected(int slot) { if (_leader?.Slot == slot) Release(false); }
    private void Release(bool announce)
    {
        bool existed = _leader != null;
        try { _leaderGlow.Clear(true); }
        catch (Exception error) { Report("restore-glow", error); }
        _pawn = null; _leader = null; _leaderHandle = _pawnHandle = 0;
        _drawing.Finish();
        if (announce && existed) Server.PrintToChatAll(" \x04[指挥]\x01 指挥已离任，自身发光取消；已有标点保留到期，下一任指挥接着使用。!leader 接任。");
    }
    private HookResult Ping(EventPlayerPing ev, GameEventInfo info)
    {
        if (ev.Userid is not { IsValid: true } player || !IsCommander(player)) return HookResult.Continue;
        info.DontBroadcast = true; // Our marker replaces the stock six-second ping, not other players' pings.
        try
        {
            var pawn = player.PlayerPawn.Value!;
            pawn.PingServices?.PlayerPingTokens.Clear();
            ProcessPing(player, new Vector3(ev.X, ev.Y, ev.Z));
            // Remove only the engine ping owned by this player; entityid is not assumed to be a ping.
            var ping = pawn.PingServices?.PlayerPing.Value;
            if (ping is { IsValid: true })
            {
                uint handle = ping.EntityHandle.Raw; int generation = _generation;
                Server.NextFrame(() => { if (!_disposed && generation == _generation && ping.IsValid && ping.EntityHandle.Raw == handle) ping.Remove(); });
            }
        }
        catch (Exception error) { Report("ping", error); }
        return HookResult.Continue;
    }
    public void ProcessPing(CCSPlayerController player, Vector3 hit)
    {
        if (!IsCommander(player) || !CommanderMarkerState.Finite(hit)) return;
        var pawn = player.PlayerPawn.Value!; var origin = pawn.AbsOrigin;
        if (origin == null) return;
        float pitch = pawn.EyeAngles.X * MathF.PI / 180, yaw = pawn.EyeAngles.Y * MathF.PI / 180;
        var direction = new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch));
        var eye = new Vector3(origin.X + pawn.ViewOffset.X, origin.Y + pawn.ViewOffset.Y, origin.Z + pawn.ViewOffset.Z);
        double now = Server.CurrentTime;
        _state.Expire(now); SyncRemoved();
        var aimed = _state.AimedAt(eye, direction, now);
        _pings++;
        if (aimed != null)
        {
            var updated = _state.Extend(aimed.Id, now)!; _extensions++;
            UpdateLabel(updated); plugin.Reply(player, $"{ColorNames[updated.ColorIndex]}色标点 +10秒，剩余 {CommanderMarkerState.Remaining(updated, now)} 秒。");
            return;
        }
        var marker = _state.Place(hit + new Vector3(0, 0, 22), now);
        SyncRemoved();
        try { CreateVisual(marker); }
        catch { _state.Remove(marker.Id); SyncRemoved(); throw; }
    }
    private void CreateVisual(CommanderMarkerState.Marker marker)
    {
        var icon = CommanderVisualStyle.Spawn(MarkerModel, "ze_commander_marker_" + marker.Id, Colors[marker.ColorIndex], marker.Position, false);
        _visuals.Add(marker.Id, new(icon));
        _countdown.Update(_state.Markers, Colors);
    }
    private static void SetGlow(CBaseModelEntity entity, Color color) => CommanderVisualStyle.Apply(entity, color);
    private void ApplyLeaderGlow()
    {
        var pawn = _leader?.PlayerPawn.Value;
        if (pawn is not { IsValid: true }) return;
        _leaderGlow.Update(pawn, LeaderColor);
        _pawn = pawn; _pawnHandle = pawn.EntityHandle.Raw;
    }
    private void UpdateLabel(CommanderMarkerState.Marker marker) => _countdown.Update(_state.Markers, Colors);
    private void SyncRemoved()
    {
        foreach (var pair in _visuals.ToArray())
        {
            if (_state.Markers.Any(marker => marker.Id == pair.Key) && pair.Value.Icon.IsValid) continue;
            _state.Remove(pair.Key);
            if (pair.Value.Icon.IsValid) pair.Value.Icon.Remove();
            _visuals.Remove(pair.Key);
        }
    }
    private void ClearMarkers() { _state.Clear(); SyncRemoved(); _countdown.Reset(true); }
    private void Tick()
    {
        if (!_active || _disposed) return;
        _drawing.Tick(_leader != null && IsCommander(_leader) ? _leader : null);
        if (Server.CurrentTime < _nextTick) return;
        _nextTick = Server.CurrentTime + 0.1f;
        try
        {
            if (_leader != null && !IsCommander(_leader)) { Release(true); return; }
            if (_leader != null) { ApplyLeaderGlow(); _leader.PlayerPawn.Value?.PingServices?.PlayerPingTokens.Clear(); }
            _state.Expire(Server.CurrentTime); SyncRemoved();
            foreach (var marker in _state.Markers)
                if (_visuals.TryGetValue(marker.Id, out var visual) && (!CommanderVisualStyle.Matches(visual.Icon, Colors[marker.ColorIndex]) || Server.CurrentTime >= visual.RefreshAt))
                { SetGlow(visual.Icon, Colors[marker.ColorIndex]); visual.RefreshAt = Server.CurrentTime + 2; }
            _countdown.Update(_state.Markers, Colors);
        }
        catch (Exception error) { Report("tick", error); }
    }
    private void Transmit(CCheckTransmitInfoList list)
    {
        if (!_active || _disposed) return;
        try
        {
            foreach (var (info, viewer) in list)
            {
                if (viewer is not { IsValid: true }) continue;
                if (_leader != null && _pawn is { IsValid: true } && _pawn.EntityHandle.Raw == _pawnHandle && IsCommander(_leader))
                { info.TransmitEntities.Add(_pawn); info.TransmitEntities.Add(_leader); _leaderGlow.Transmit(info); }
                foreach (var visual in _visuals.Values)
                {
                    if (visual.Icon.IsValid) info.TransmitEntities.Add(visual.Icon);
                }
                _countdown.Transmit(info, viewer);
                _drawing.Transmit(info);
                _transmitted++;
            }
        }
        catch (Exception error) { Report("transmit", error); }
    }
    public string Diagnostics() => JsonSerializer.Serialize(new
    {
        active = _active, leaderSlot = _leader?.Slot, leaderPawn = _pawnHandle, maxLeaders = 1,
        leaderGlow = _leaderGlow.Diagnostics(),
        limit = CommanderMarkerState.Limit, lifetime = CommanderMarkerState.Lifetime, glowRange = FullWorldGlowRange,
        pingEvents = _pings, extensions = _extensions, transmitPasses = _transmitted,
        markers = _state.Markers.Select(m => new { id = m.Id, color = ColorNames[m.ColorIndex], remaining = CommanderMarkerState.Remaining(m, Server.CurrentTime),
            icon = _visuals.GetValueOrDefault(m.Id)?.Icon.Index }),
        countdown = new { kind = _countdown.ModelResourcesReady ? "opaque-model-glow" : "large-text-pending-workshop", height = CommanderCountdownLayout.Height,
            upright = true, perViewer = true, compiledUpAxis = "+X", compiledFrontAxis = "+Y", compiledRightAxis = "+Z",
            modelAngles = "pitch=-90,yaw=facing-90,roll=0", labels = _countdown.Count, entities = _countdown.EntityCount, textFallbacks = _countdown.FallbackCount,
            resources = _resources, apply = "Next normal map load after approved Workshop assets are downloaded; no forced restart." },
        drawing = _drawing.Diagnostics(), errors = _errors.ToArray(), visualAcceptance = "requires-real-client"
    });
    private void StatusCommand(CCSPlayerController? player, CommandInfo command) => plugin.Reply(player, Diagnostics());
    private void Report(string area, Exception error) { if (_errors.Add(area)) plugin.Logger.LogError(error, "Commander failure in {Area}", area); }
    private static string CleanName(string text) => new(text.Where(c => !char.IsControl(c)).Take(48).ToArray());
    public void Dispose()
    {
        Release(false); ClearMarkers(); _drawing.Reset(true); _disposed = true; _active = false;
        plugin.RemoveListener<Listeners.OnServerPrecacheResources>(Precache); plugin.RemoveListener<Listeners.OnTick>(Tick);
        plugin.RemoveListener<Listeners.CheckTransmit>(Transmit); plugin.RemoveListener<Listeners.OnClientDisconnect>(Disconnected);
        plugin.DeregisterEventHandler<EventPlayerPing>(Ping, HookMode.Pre); plugin.DeregisterEventHandler<EventPlayerDeath>(Died);
        plugin.DeregisterEventHandler<EventPlayerTeam>(TeamChanged);
        plugin.RemoveZeCommand("css_leader", LeaderCommand); plugin.RemoveZeCommand("css_ze_leader", LeaderCommand);
        plugin.RemoveZeCommand("css_unleader", LeaveCommand); plugin.RemoveZeCommand("css_clearpoints", ClearCommand);
        plugin.RemoveZeCommand("css_ze_clearpoints", ClearCommand); plugin.RemoveZeCommand("css_ze_commander_status", StatusCommand);
    }
}
