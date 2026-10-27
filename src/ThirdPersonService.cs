using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ZePractice;

/// <summary>Per-player server camera. No sv_cheats, client-wide cvar, or map-camera takeover.</summary>
public sealed class ThirdPersonService(ZePracticePlugin plugin) : IDisposable
{
    private sealed class Session
    {
        public required CCSPlayerController Player;
        public uint Controller;
        public int? UserId;
        public CCSCustomPlayerCamera? Camera;
        public CCSPlayerPawn? Pawn;
        public uint PawnHandle;
    }
    private readonly Dictionary<int, Session> _sessions = new();
    private float _next;
    private bool _disposed;
    private readonly HashSet<string> _errors = new();
    public bool Enabled(CCSPlayerController p) => _sessions.TryGetValue(p.Slot, out var s) && Owns(s, p);
    private static bool Owns(Session s, CCSPlayerController p) => p.IsValid && p.EntityHandle.Raw == s.Controller && p.UserId == s.UserId;
    public void Initialize()
    {
        plugin.AddZeCommand("css_thirdperson", "所有人：第三人称 [on|off]，省略切换", Command);
        plugin.AddZeCommand("css_ze_thirdperson", "所有人：第三人称 [on|off]，省略切换", Command);
        plugin.RegisterListener<Listeners.OnTick>(Tick);
        plugin.RegisterListener<Listeners.OnClientDisconnect>(Disconnect);
        plugin.RegisterListener<Listeners.CheckTransmit>(Transmit);
    }
    private void Command(CCSPlayerController? p, CommandInfo c)
    {
        if (p is not { IsValid: true, IsHLTV: false }) { plugin.Reply(p, "聊天输入 !thirdperson [on|off]；人类和僵尸均可使用。"); return; }
        if (c.ArgCount > 2 || !PublicGameplayLogic.TryToggle(c.GetArg(1), Enabled(p), out bool enabled))
        { plugin.Reply(p, "用法：!thirdperson [on|off] 或 !ze_thirdperson；省略参数切换。"); return; }
        SetEnabled(p, enabled);
    }
    public void SetEnabled(CCSPlayerController p, bool enabled)
    {
        if (_disposed || !p.IsValid || p.IsHLTV) return;
        if (!enabled) { Disconnect(p.Slot); plugin.Reply(p, "第三人称：关闭，恢复第一人称；地图专用镜头保持原样。"); return; }
        if (!Enabled(p)) { Disconnect(p.Slot); _sessions[p.Slot] = new() { Player = p, Controller = p.EntityHandle.Raw, UserId = p.UserId }; }
        try { Apply(_sessions[p.Slot]); }
        catch { Disconnect(p.Slot); throw; }
        plugin.Reply(p, "第三人称：开启；存活时生效，地图剧情镜头优先。!thirdperson off 关闭。");
    }
    private void Apply(Session s)
    {
        var p = s.Player; var pawn = p.PlayerPawn.Value;
        if (!p.PawnIsAlive || p.TeamNum is not (2 or 3) || pawn is not { IsValid: true } || pawn.CameraServices == null)
        { ReleaseCamera(s); return; }
        if (s.PawnHandle != pawn.EntityHandle.Raw) ReleaseCamera(s);
        var view = pawn.CameraServices.ViewEntity;
        if (view.IsValid && (s.Camera is not { IsValid: true } || view.Raw != s.Camera.EntityHandle.Raw)) return;
        if (s.Camera is not { IsValid: true })
        {
            var camera = Utilities.CreateEntityByName<CCSCustomPlayerCamera>("custom_player_camera") ?? throw new InvalidOperationException("custom_player_camera allocation failed");
            s.Camera = camera; s.Pawn = pawn; s.PawnHandle = pawn.EntityHandle.Raw;
            using var keys = new CEntityKeyValues(); keys.SetString("targetname", "ze_personal_camera_" + s.Controller); camera.DispatchSpawn(keys);
            camera.Pawn.Raw = pawn.EntityHandle.Raw; camera.FollowEntity.Raw = pawn.EntityHandle.Raw;
            camera.CameraMode = CustomCameraMode_t.CUSTOM_CAMERA_MODE_FOLLOW_POSITION;
            camera.FollowEyes = true; camera.FollowOffset.X = camera.FollowOffset.Y = camera.FollowOffset.Z = 0;
            camera.CameraOffset.X = -100; camera.CameraOffset.Y = 0; camera.CameraOffset.Z = 10;
            camera.ClipCameraOffset = true; camera.CameraOffsetReturnStrength = 1;
            foreach (string field in new[] { "m_hPawn", "m_hFollowEntity", "m_nCameraMode", "m_bFollowEyes", "m_vecFollowOffset", "m_vecCameraOffset", "m_bClipCameraOffset", "m_flCameraOffsetReturnStrength" })
                Utilities.SetStateChanged(camera, "CCSCustomPlayerCamera", field);
        }
        if (view.Raw != s.Camera.EntityHandle.Raw)
        {
            view.Raw = s.Camera.EntityHandle.Raw;
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");
        }
    }
    private static void ReleaseCamera(Session s)
    {
        if (s.Camera is { IsValid: true } camera)
        {
            if (s.Pawn is { IsValid: true } pawn && pawn.EntityHandle.Raw == s.PawnHandle && pawn.CameraServices?.ViewEntity.Raw == camera.EntityHandle.Raw)
            { pawn.CameraServices.ViewEntity.Raw = uint.MaxValue; Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices"); }
            camera.CameraMode = CustomCameraMode_t.CUSTOM_CAMERA_MODE_DISABLED;
            camera.Remove();
        }
        s.Camera = null; s.Pawn = null; s.PawnHandle = 0;
    }
    private void Tick()
    {
        if (_disposed || Server.CurrentTime < _next) return;
        _next = Server.CurrentTime + .2f;
        foreach (var (slot, session) in _sessions.ToArray())
        {
            try { if (!Owns(session, session.Player)) Disconnect(slot); else Apply(session); }
            catch (Exception error) { if (_errors.Add(error.Message)) plugin.Logger.LogError(error, "Third-person camera update failed"); Disconnect(slot); }
        }
    }
    private void Transmit(CCheckTransmitInfoList list)
    {
        foreach (var (info, viewer) in list)
        {
            if (viewer is not { IsValid: true }) continue;
            foreach (var session in _sessions.Values)
            {
                if (session.Camera is not { IsValid: true } camera) continue;
                if (Owns(session, viewer)) info.TransmitEntities.Add(camera); else info.TransmitEntities.Remove(camera);
            }
        }
    }
    private void Disconnect(int slot) { if (_sessions.Remove(slot, out var s)) ReleaseCamera(s); }
    public void Reset(bool removeEntities)
    {
        if (removeEntities) foreach (var s in _sessions.Values) ReleaseCamera(s);
        _sessions.Clear(); _next = 0;
    }
    public string Diagnostics() => JsonSerializer.Serialize(new { sessions = _sessions.Select(x => new { slot = x.Key, pawn = x.Value.PawnHandle, camera = x.Value.Camera?.Index }), errors = _errors });
    public void Dispose()
    {
        Reset(true); _disposed = true;
        plugin.RemoveListener<Listeners.OnTick>(Tick); plugin.RemoveListener<Listeners.OnClientDisconnect>(Disconnect); plugin.RemoveListener<Listeners.CheckTransmit>(Transmit);
        plugin.RemoveZeCommand("css_thirdperson", Command); plugin.RemoveZeCommand("css_ze_thirdperson", Command);
    }
}
