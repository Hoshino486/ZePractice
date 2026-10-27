using System.Globalization;
using System.Numerics;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ZePractice;

/// <summary>Read-only personal telemetry. No native HUD overrides, JS, key binds or central print messages.</summary>
public sealed class MovementHudService(ZePracticePlugin plugin) : IDisposable
{
    public const string Layout = "panorama/layout/custom_game/zepractice/movement.vxml_c";
    // custom_hud_layout takes the compiled resource name, just like the working radio panel.
    // Keeping a separate uncompiled name silently creates a network entity with no client panel.
    public const string LayoutResource = Layout;
    public static readonly string[] Required = [Layout, "panorama/styles/custom_game/zepractice/movement.vcss_c"];
    private readonly Dictionary<int, Session> _sessions = new();
    private readonly HashSet<string> _errors = new();
    private CommanderResources.Availability _resources = new(false, "awaiting-map-precache", "");
    private bool _active, _disposed;
    private sealed class Session
    {
        public required CCSPlayerController Player;
        public required uint Controller;
        public required int? UserId;
        public readonly MovementTelemetryLogic Telemetry = new();
        public CCSCustomHudLayout? Hud;
        public bool Enabled = true;
        public float NextDisplay, RetryAt;
        public string Speed = "", Jump = "";
    }
    public void Initialize()
    {
        plugin.RegisterListener<Listeners.OnServerPrecacheResources>(Precache);
        plugin.RegisterListener<Listeners.OnTick>(Tick);
        plugin.RegisterListener<Listeners.CheckTransmit>(Transmit);
        plugin.RegisterListener<Listeners.OnClientDisconnect>(Disconnect);
        plugin.AddZeCommand("css_ze_speed", "个人地速/垂速/跳高 HUD：!ze_speed [on|off]", Toggle);
        plugin.AddZeCommand("css_ze_movement_status", "个人移动 HUD 与工坊资源诊断", Status);
    }
    private void Precache(ResourceManifest manifest)
    {
        _resources = CommanderResources.Installed(Server.GameDirectory, Required);
        // AddResource expects the logical resource name, not the compiled VPK entry.
        if (_resources.Ready) foreach (string resource in Required)
            manifest.AddResource(resource.EndsWith("_c", StringComparison.Ordinal) ? resource[..^2] : resource);
        plugin.Logger.LogInformation("MOVEMENT_HUD_RESOURCES ready={Ready} status={Status}; map precache snapshot", _resources.Ready, _resources.Status);
    }
    public void OnMapStart()
    {
        Reset(true); _active = true; _errors.Clear();
        // Hot reload does not receive OnServerPrecacheResources. HUD layouts have no model/physics precache;
        // initialize the availability snapshot on both map start and hot reload, never from downloads/staging.
        _resources = CommanderResources.Installed(Server.GameDirectory, Required);
        plugin.Logger.LogInformation("MOVEMENT_HUD_INIT ready={Ready} status={Status} path={Path}", _resources.Ready,_resources.Status,_resources.Path);
    }
    public void OnMapEnd() { _active = false; Reset(false); }
    private static bool Owns(Session s, CCSPlayerController p) => p.IsValid && s.Player.IsValid &&
        p.EntityHandle.Raw == s.Controller && p.UserId == s.UserId;
    private Session Get(CCSPlayerController p)
    {
        if (_sessions.TryGetValue(p.Slot, out var s) && Owns(s, p)) return s;
        Disconnect(p.Slot);
        return _sessions[p.Slot] = new() { Player = p, Controller = p.EntityHandle.Raw, UserId = p.UserId };
    }
    public bool Enabled(CCSPlayerController p) => !_sessions.TryGetValue(p.Slot, out var s) || !Owns(s, p) || s.Enabled;
    private static void RemoveHud(Session s)
    {
        if (s.Hud is { IsValid: true }) s.Hud.Remove();
        s.Hud = null; s.Speed = s.Jump = "";
    }
    private void Tick()
    {
        if (_disposed || !_active) return;
        float now = Server.CurrentTime;
        foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsHLTV && !plugin.IsPracticeBot(p)))
        {
            var s = Get(player);
            try
            {
                if (!player.PawnIsAlive || player.TeamNum is not (2 or 3) || plugin.IsTeamResetPending(player) || player.PlayerPawn.Value is not { IsValid: true, Health: > 0 } pawn || pawn.AbsOrigin is not { } origin)
                { s.Telemetry.Reset(); RemoveHud(s); continue; }
                var v = pawn.AbsVelocity;
                bool walking = pawn.MoveType == MoveType_t.MOVETYPE_WALK && pawn.WaterLevel < .5f && !plugin.PublicNoclipEnabled(player);
                var data = s.Telemetry.Update(new(now, pawn.EntityHandle.Raw, new(origin.X, origin.Y, origin.Z), new(v.X, v.Y, v.Z), (pawn.Flags & 1) != 0, walking));
                if (!s.Enabled || !_resources.Ready) { RemoveHud(s); continue; }
                if (now < s.NextDisplay || now < s.RetryAt) continue;
                s.NextDisplay = now + .1f;
                Render(s, data);
            }
            catch (Exception e)
            {
                s.RetryAt = now + 5;
                RemoveHud(s);
                if (_errors.Add(e.GetType().Name + ":" + e.Message)) plugin.Logger.LogError(e, "Movement HUD update failed");
            }
        }
    }
    private static void Render(Session s, MovementTelemetryLogic.Reading data)
    {
        if (s.Hud is not { IsValid: true })
        {
            s.Hud = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout") ?? throw new InvalidOperationException("Movement HUD allocation failed");
            using var kv = new CEntityKeyValues(); kv.SetString("layout", LayoutResource); kv.SetString("targetname", "ze_movement_hud_" + s.Player.Slot);
            s.Hud.Observable = false; s.Hud.DispatchSpawn(kv);
            // Same replicated per-player visibility path as the working radio menu.
            // CheckTransmit is an additional isolation layer, not a substitute for client HUD state.
            s.Hud.SetHasClass("movement_window", "hidden", true);
            s.Hud.SetHasClassForPlayer(s.Player, "movement_window", "hidden", false);
            s.Speed = s.Jump = "";
        }
        string speed = string.Create(CultureInfo.InvariantCulture, $"地速 {data.GroundSpeed:0} u/s    垂速 {data.VerticalSpeed:+0;-0;0} u/s");
        string jump = string.Create(CultureInfo.InvariantCulture, $"跳高 {data.JumpHeight:0.0} u · {data.State}");
        if (speed != s.Speed) { ZeHudText.Set(s.Hud, "movement_speed", "text", speed); s.Speed = speed; }
        if (jump != s.Jump) { ZeHudText.Set(s.Hud, "movement_jump", "text", jump); s.Jump = jump; }
    }
    private void Transmit(CCheckTransmitInfoList list)
    {
        if (_disposed || !_active) return;
        foreach (var (info, viewer) in list)
            foreach (var s in _sessions.Values)
                if (s.Hud is { IsValid: true } hud)
                {
                    if (viewer != null && Owns(s, viewer) && s.Enabled) info.TransmitEntities.Add(hud);
                    else info.TransmitEntities.Remove(hud);
                }
    }
    private void Toggle(CCSPlayerController? p, CommandInfo c)
    {
        if (p is not { IsValid: true }) { plugin.Reply(p, "游戏内输入 !ze_speed [on|off]。"); return; }
        string arg = c.GetArg(1).ToLowerInvariant();
        if (arg is not ("" or "on" or "off" or "1" or "0")) { plugin.Reply(p, "用法：!ze_speed [on|off]"); return; }
        var s = Get(p); s.Enabled = arg is "on" or "1" || (arg == "" && !s.Enabled);
        if (!s.Enabled) RemoveHud(s);
        plugin.Reply(p, "个人移动参数：" + (s.Enabled ? "开" : "关") + (_resources.Ready ? "。" :
            "；服务器当前安装快照缺少移动面板，!ze_movement_status 查看安装/面板状态。"));
    }
    public string Diagnostics(CCSPlayerController? viewer) => JsonSerializer.Serialize(new
    {
        active = _active, resources = _resources, defaultEnabled = true, updateHz = 10,
        placement = "bottom-center above team emblem", jumpHeight = "maximum foot rise from takeoff; last jump retained on landing",
        sessions = _sessions.Values.Where(s => viewer == null || Owns(s, viewer)).Select(s => new { slot = s.Player.Slot, enabled = s.Enabled,
            entity = s.Hud is { IsValid: true } ? (uint?)s.Hud.Index : null, reading = s.Telemetry.Current, speed = s.Speed, jump = s.Jump }),
        errors = _errors.ToArray(), visualAcceptance = "requires-real-client"
    });
    private void Status(CCSPlayerController? p, CommandInfo c) => plugin.Reply(p, Diagnostics(p));
    private void Disconnect(int slot) { if (_sessions.Remove(slot, out var s)) RemoveHud(s); }
    private void Reset(bool remove)
    { if (remove) foreach (var s in _sessions.Values) RemoveHud(s); _sessions.Clear(); }
    public void Dispose()
    {
        _disposed = true; _active = false; Reset(true);
        plugin.RemoveListener<Listeners.OnServerPrecacheResources>(Precache); plugin.RemoveListener<Listeners.OnTick>(Tick);
        plugin.RemoveListener<Listeners.CheckTransmit>(Transmit); plugin.RemoveListener<Listeners.OnClientDisconnect>(Disconnect);
        plugin.RemoveZeCommand("css_ze_speed", Toggle); plugin.RemoveZeCommand("css_ze_movement_status", Status);
    }
}
