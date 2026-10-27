using System.Globalization;
using System.Net;
using System.Text;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using Microsoft.Extensions.Logging;

namespace ZePractice;

/// <summary>
/// Runtime adapter. Call Allows for every command/alias/menu action at invocation time.
/// Reload swaps a fully validated immutable snapshot; malformed edits keep the last good policy.
/// </summary>
public sealed class ZePermissionService : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly ZePracticePlugin _plugin;
    private readonly object _gate = new();
    private PermissionPolicy _policy = PermissionPolicy.CreateDefault();
    private volatile bool _disposed;
    public string ConfigurationPath { get; }

    public ZePermissionService(ZePracticePlugin plugin)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        ConfigurationPath = Path.Combine(Path.GetFullPath(plugin.ModuleDirectory), "data", "permissions.json");
    }

    public void Initialize()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                if (!File.Exists(ConfigurationPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ConfigurationPath)!);
                    // No example IDs and no automatic admin enrollment; do not overwrite an existing edit.
                    try
                    {
                        using var stream = new FileStream(ConfigurationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        var bytes = Utf8.GetBytes(PermissionPolicy.DefaultJson + Environment.NewLine);
                        stream.Write(bytes);
                    }
                    catch (IOException) when (File.Exists(ConfigurationPath)) { }
                }
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                _plugin.Logger.LogWarning("Permission defaults could not be written; using the last valid/default policy. Error type: {Type}", ex.GetType().Name);
            }
            if (!Reload(out var error))
                _plugin.Logger.LogWarning("Permission configuration was not loaded: {Reason}", error);
        }
    }

    public bool Reload(out string error)
    {
        lock (_gate)
        {
            if (_disposed) { error = "权限服务已停止。"; return false; }
            try
            {
                using var stream = new FileStream(ConfigurationPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > PermissionPolicy.MaxJsonBytes) { error = "权限文件超过大小限制，保留上一份有效配置。"; return false; }
                // A hard read bound also covers a file being enlarged concurrently by an editor.
                var bytes = new byte[PermissionPolicy.MaxJsonBytes + 1];
                int length = 0, read;
                while (length < bytes.Length && (read = stream.Read(bytes, length, bytes.Length - length)) > 0) length += read;
                if (length > PermissionPolicy.MaxJsonBytes) { error = "权限文件超过大小限制，保留上一份有效配置。"; return false; }
                var json = Utf8.GetString(bytes, 0, length);
                if (!PermissionPolicy.TryParse(json, out var next, out var reason))
                { error = "权限配置无效，保留上一份有效配置。" + reason; return false; }
                Volatile.Write(ref _policy, next!);
                _plugin.Logger.LogInformation("Permissions reloaded: {RoleCount} roles, {UserCount} explicitly assigned users. No automatic admin enrollment.", next!.RoleCount, next.UserCount);
                error = "";
                return true;
            }
            catch (Exception ex) when (IsFileError(ex))
            { error = "读取权限文件失败，保留上一份有效配置（" + ex.GetType().Name + "）。"; return false; }
        }
    }

    public bool Allows(CCSPlayerController? player, string command)
    {
        if (_disposed || PermissionPolicy.NormalizeCommand(command).Length == 0) return false;
        if (PermissionPolicy.NormalizeCommand(command) is "css_ze_map" or "css_ze_workshop_now" or "css_ze_installed_maps" or "css_ze_map_remove")
            return IsServerOwner(player);
        var policy = Volatile.Read(ref _policy);
        var subject = Identity(player, policy);
        return subject.Valid && (subject.Owner || policy.Allows(subject.AuthorizedSteamId, _plugin.Config.AllowEveryonePracticeCommands, command));
    }

    /// <summary>UI/management classification, never a replacement for per-command Allows.</summary>
    public bool HasAdminAccess(CCSPlayerController? player)
    {
        if (_disposed) return false;
        var policy = Volatile.Read(ref _policy);
        var subject = Identity(player, policy);
        return subject.Valid && (subject.Owner || policy.HasAdminAccess(subject.AuthorizedSteamId, _plugin.Config.AllowEveryonePracticeCommands));
    }

    /// <summary>Only server/RCON, configured authenticated owners, trusted loopback, or enabled CSS-root bypass.</summary>
    public bool IsOwner(CCSPlayerController? player)
        => !_disposed && Identity(player, Volatile.Read(ref _policy)).Owner;

    public bool IsServerOwner(CCSPlayerController? player)
    {
        if (_disposed) return false;
        var subject = Identity(player, Volatile.Read(ref _policy));
        return subject.Valid && subject.Owner && (player == null || subject.OwnerSource == "OwnerSteamIds");
    }

    public int Immunity(CCSPlayerController? player)
    {
        if (_disposed) return 0;
        var policy = Volatile.Read(ref _policy);
        var subject = Identity(player, policy);
        if (!subject.Valid) return 0;
        if (subject.Owner) return int.MaxValue;
        return Math.Max(subject.CssImmunity, policy.Immunity(subject.AuthorizedSteamId, _plugin.Config.AllowEveryonePracticeCommands));
    }

    public string Describe(CCSPlayerController? player)
    {
        if (_disposed) return "权限服务已停止";
        var policy = Volatile.Read(ref _policy);
        var subject = Identity(player, policy);
        if (!subject.Valid) return "无有效玩家身份";
        if (subject.Owner) return "最高权限（" + subject.OwnerSource + "）";
        var access = policy.Resolve(subject.AuthorizedSteamId, _plugin.Config.AllowEveryonePracticeCommands);
        var roles = access.Roles.Count == 0 ? "无" : string.Join(", ", access.Roles);
        var auth = subject.AuthorizedSteamId == null ? "Steam身份未认证；仅默认角色" : "Steam身份已认证";
        var practice = _plugin.Config.AllowEveryonePracticeCommands ? "" : "；默认自助练习已关闭，显式角色可授权";
        return auth + "；角色=" + roles + "；豁免=" + Math.Max(subject.CssImmunity, access.Immunity).ToString(CultureInfo.InvariantCulture) + practice;
    }

    private readonly record struct Subject(bool Valid, bool Owner, string OwnerSource, string? AuthorizedSteamId, int CssImmunity);
    private Subject Identity(CCSPlayerController? player, PermissionPolicy policy)
    {
        if (player == null) return new(true, true, "服务端控制台/RCON", null, 0);
        if (!ZePracticePlugin.IsRealPlayer(player)) return default;
        try
        {
            // Never substitute player.SteamID for this authenticated identity.
            var authenticated = player.AuthorizedSteamID?.SteamId64.ToString(CultureInfo.InvariantCulture);
            if (!PermissionPolicy.IsSteamId64(authenticated)) authenticated = null;
            if (authenticated != null && _plugin.Config.OwnerSteamIds.Contains(authenticated, StringComparer.Ordinal))
                return new(true, true, "OwnerSteamIds", authenticated, 0);
            if (_plugin.Config.TrustLoopbackAdmin && IsNumericLoopback(player.IpAddress))
                return new(true, true, "可信本机连接", authenticated, 0);
            if (authenticated != null && policy.CssRootBypass && AdminManager.PlayerHasPermissions(player, "@css/root"))
                return new(true, true, "CSS root", authenticated, 0);
            int cssImmunity = authenticated == null ? 0 : (int)Math.Min((uint)(int.MaxValue - 1), AdminManager.GetPlayerImmunity(player));
            return new(true, false, "", authenticated, cssImmunity);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Disconnect/reconnect races fail closed instead of consulting raw, reusable slot identities.
            return default;
        }
    }

    public static bool IsNumericLoopback(string? endpoint)
    {
        if (string.IsNullOrEmpty(endpoint)) return false;
        if (IPEndPoint.TryParse(endpoint, out var parsed)) return IPAddress.IsLoopback(parsed.Address);
        return IPAddress.TryParse(endpoint, out var address) && IPAddress.IsLoopback(address);
    }
    private static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException or DecoderFallbackException or System.Security.SecurityException;
    public void Dispose() { lock (_gate) { _disposed = true; } }
}
