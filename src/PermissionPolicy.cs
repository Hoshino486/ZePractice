using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ZePractice;

/// <summary>
/// Immutable, engine-independent command authorization snapshot. Roles have explicit grants;
/// immunity and names never imply permission. Wildcards are terminal-prefix matches, not regex.
/// </summary>
public sealed class PermissionPolicy
{
    public const int MaxJsonBytes = 262144;
    public const int MaxRoles = 128;
    public const int MaxUsers = 4096;
    public const int MaxRulesPerRole = 256;
    public const int MaxCommandLength = 128;
    private const int MaxRoleNameLength = 64;
    private const int MaxInheritanceDepth = 32;
    private const int MaxTotalRules = 8192;
    private static readonly HashSet<string> PublicCommands = new(StringComparer.Ordinal)
    {
        "css_ze_public", "css_ze_hp", "css_ze_armor", "css_ze_noclip", "css_ze_ammo", "css_ze_bhop",
        "css_nospread", "css_ze_nospread", "css_thirdperson", "css_ze_thirdperson",
        "css_ze_speed", "css_ze_movement_status", "css_ze_diag", "css_ze_diag_status", "css_ze_script_status",
        "css_ze_bot_add", "css_ze_bot_hp", "css_ze_bot_move", "css_ze_bot_kill", "css_ze_bot_clear", "css_ze_bot_status",
        "css_ze_music", "css_ze_hide", "css_ze_join", "css_ze_roundtime", "css_ze_restart", "css_ze_endround",
        "css_ze_workshop", "css_host_workshop_map", "css_ze_public_status",
        "css_ze_maps", "css_ze_vote", "css_rtv", "css_unrtv", "css_ze_vote_status", "css_ze_markers_status",
        "css_leader", "css_ze_leader", "css_unleader", "css_clearpoints", "css_ze_clearpoints", "css_ze_commander_status", "css_ze_model_status"
    };
    public static bool IsPublicCommand(string command) => PublicCommands.Contains(NormalizeCommand(command));

    public const string DefaultJson = """
    {
      "Version": 1,
      "CssRootBypass": true,
      "DefaultRoles": ["player", "practicer"],
      "Roles": {
        "player": {
          "Inherits": [],
          "Immunity": 0,
          "Allow": ["css_ze", "css_zhelp", "css_ze_status", "css_ze_whoami", "css_ze_maps", "css_zebuy", "css_zeclass", "css_zskill", "css_bosshud", "css_items", "css_ze_practice", "css_ze_commands", "css_ze_binds", "css_ze_ui", "css_ze_ui_close", "css_ze_ui_chat", "css_ze_ui_ready", "css_ze_public", "css_ze_hp", "css_ze_armor", "css_ze_noclip", "css_ze_ammo", "css_ze_bhop", "css_ze_music", "css_ze_hide", "css_ze_join", "css_ze_roundtime", "css_ze_restart", "css_ze_endround", "css_ze_workshop", "css_host_workshop_map", "css_ze_public_status"],
          "Deny": []
        },
        "practicer": {
          "Inherits": ["player"],
          "Immunity": 0,
          "Allow": ["css_ze_god", "css_ze_save", "css_ze_tp", "css_ze_clearcheckpoint", "css_ze_reset", "css_ze_respawn", "css_ze_goto", "css_zerefill"],
          "Deny": []
        },
        "moderator": {
          "Inherits": ["player"],
          "Immunity": 10,
          "Allow": ["css_zeadmin", "css_ze_adminhelp", "css_ze_kick", "css_ze_mute", "css_ze_gag", "css_ze_unmute", "css_ze_ungag", "css_ze_punishments", "css_ze_team", "css_ze_bring", "css_ze_slay", "css_ze_respawn_other"],
          "Deny": []
        },
        "admin": {
          "Inherits": ["practicer", "moderator"],
          "Immunity": 50,
          "Allow": ["css_ze*", "css_boss*", "css_item*", "css_zskill", "css_zhelp"],
          "Deny": ["css_ze_permissions*", "css_ze_root*"]
        }
      },
      "Users": {}
    }
    """;

    private static readonly HashSet<string> SelfPracticeCommands = new(StringComparer.Ordinal)
    {
        "css_ze_god", "css_ze_save", "css_ze_tp", "css_ze_clearcheckpoint", "css_ze_reset",
        "css_ze_respawn", "css_ze_goto", "css_zerefill"
    };
    // This is a UI/management classification only. Every real operation must still call Allows.
    private static readonly string[] ManagementCommands =
    [
        "css_zeadmin", "css_ze_adminhelp", "css_ze_bring", "css_ze_slay", "css_ze_kick",
        "css_ze_ban", "css_ze_mute", "css_ze_gag", "css_ze_unban", "css_ze_unmute",
        "css_ze_ungag", "css_ze_punishments", "css_ze_team",
        "css_ze_mode", "css_ze_map", "css_ze_reload", "css_ze_respawn_other",
        "css_ze_permissions_reload", "css_ze_timescale", "css_ze_rtv_force", "css_ze_forcertv",
        "css_ze_time", "css_bossstatus", "css_bosshudmode", "css_itemwatch"
    ];

    private sealed record Role(string[] Inherits, string[] Allow, string[] Deny, int Immunity);
    private readonly Dictionary<string, Role> _roles;
    private readonly Dictionary<string, string[]> _users;
    private readonly string[] _defaultRoles;
    private readonly Dictionary<string, string[]> _closures;
    public bool CssRootBypass { get; }
    public int RoleCount => _roles.Count;
    public int UserCount => _users.Count;
    public IReadOnlyList<string> DefaultRoles => Array.AsReadOnly(_defaultRoles);

    private PermissionPolicy(Dictionary<string, Role> roles, Dictionary<string, string[]> users,
        string[] defaults, bool cssRootBypass, Dictionary<string, string[]> closures)
    { _roles = roles; _users = users; _defaultRoles = defaults; CssRootBypass = cssRootBypass; _closures = closures; }

    public static PermissionPolicy CreateDefault() => Parse(DefaultJson);

    public static PermissionPolicy Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
            throw Invalid("Permission JSON is empty or exceeds the size limit.");
        if (json[0] == '\ufeff') json = json[1..];
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 32 });
        var root = Properties(document.RootElement, "Version", "CssRootBypass", "Roles", "Users", "DefaultRoles");
        if (root.TryGetValue("Version", out var version) && (!version.TryGetInt32(out var v) || v != 1))
            throw Invalid("Unsupported permission configuration version.");
        bool cssRoot = true;
        if (root.TryGetValue("CssRootBypass", out var bypass))
        {
            if (bypass.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid("CssRootBypass must be boolean.");
            cssRoot = bypass.GetBoolean();
        }
        if (!root.TryGetValue("Roles", out var roleObject) || roleObject.ValueKind != JsonValueKind.Object)
            throw Invalid("Roles must be an object.");
        var roles = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);
        int totalRules = 0;
        foreach (var property in roleObject.EnumerateObject())
        {
            ValidateRoleName(property.Name);
            if (roles.Count >= MaxRoles || roles.ContainsKey(property.Name)) throw Invalid("Too many roles or duplicate role name.");
            var data = Properties(property.Value, "Allow", "Deny", "Inherits", "Immunity");
            var allow = Strings(data, "Allow", MaxRulesPerRole, ValidatePattern);
            var deny = Strings(data, "Deny", MaxRulesPerRole, ValidatePattern);
            var inherits = Strings(data, "Inherits", MaxRoles, ValidateRoleName);
            totalRules += allow.Length + deny.Length;
            if (totalRules > MaxTotalRules) throw Invalid("Too many permission rules.");
            int immunity = 0;
            if (data.TryGetValue("Immunity", out var immunityValue) &&
                (!immunityValue.TryGetInt32(out immunity) || immunity < 0 || immunity > 1000000))
                throw Invalid("Immunity must be an integer from 0 to 1000000.");
            roles.Add(property.Name, new(inherits, allow.Select(NormalizePattern).ToArray(), deny.Select(NormalizePattern).ToArray(), immunity));
        }
        if (roles.Count == 0) throw Invalid("At least one role is required.");
        var defaults = Strings(root, "DefaultRoles", MaxRoles, ValidateRoleName);
        var users = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (root.TryGetValue("Users", out var userObject))
        {
            if (userObject.ValueKind != JsonValueKind.Object) throw Invalid("Users must be an object.");
            foreach (var property in userObject.EnumerateObject())
            {
                if (users.Count >= MaxUsers || !IsSteamId64(property.Name) || users.ContainsKey(property.Name))
                    throw Invalid("Invalid, duplicate, or excessive Users entries.");
                users.Add(property.Name, StringArray(property.Value, MaxRoles, ValidateRoleName));
            }
        }
        foreach (var name in defaults.Concat(users.Values.SelectMany(x => x)).Concat(roles.Values.SelectMany(x => x.Inherits)))
            if (!roles.ContainsKey(name)) throw Invalid("Unknown role referenced by inheritance, defaults, or Users.");
        var closures = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in roles.Keys)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Visit(string next, int depth)
            {
                if (depth > MaxInheritanceDepth) throw Invalid("Role inheritance exceeds the depth limit.");
                if (visiting.Contains(next)) throw Invalid("Role inheritance contains a cycle.");
                if (!visited.Add(next)) return;
                visiting.Add(next);
                foreach (var parent in roles[next].Inherits) Visit(parent, depth + 1);
                visiting.Remove(next);
            }
            Visit(name, 1);
            closures.Add(name, visited.ToArray());
        }
        return new(roles, users, defaults, cssRoot, closures);
    }

    public static bool TryParse(string json, out PermissionPolicy? policy, out string error)
    {
        try { policy = Parse(json); error = ""; return true; }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            policy = null;
            // Do not echo malformed JSON, user IDs, role names, or parser excerpts into game chat/logs.
            error = ex is InvalidDataException ? ex.Message : "Permission JSON has an invalid type, syntax, or nesting depth.";
            return false;
        }
    }

    public bool Allows(string? authorizedSteamId, bool allowEveryonePractice, string command)
        => Resolve(authorizedSteamId, allowEveryonePractice).Allows(command);
    public bool HasAdminAccess(string? authorizedSteamId, bool allowEveryonePractice)
    {
        var access = Resolve(authorizedSteamId, allowEveryonePractice);
        return ManagementCommands.Any(access.Allows);
    }
    public int Immunity(string? authorizedSteamId, bool allowEveryonePractice)
        => Resolve(authorizedSteamId, allowEveryonePractice).Immunity;
    public IReadOnlyList<string> EffectiveRoles(string? authorizedSteamId, bool allowEveryonePractice)
        => Resolve(authorizedSteamId, allowEveryonePractice).Roles;

    public PermissionAccess Resolve(string? authorizedSteamId, bool allowEveryonePractice)
    {
        var defaults = Expand(_defaultRoles.Where(x => allowEveryonePractice || !x.Equals("practicer", StringComparison.OrdinalIgnoreCase)));
        var assigned = Expand(authorizedSteamId != null && IsSteamId64(authorizedSteamId) && _users.TryGetValue(authorizedSteamId, out var names) ? names : []);
        var combined = defaults.Concat(assigned).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var defaultAllow = defaults.SelectMany(x => _roles[x].Allow).Distinct(StringComparer.Ordinal).ToArray();
        var assignedAllow = assigned.SelectMany(x => _roles[x].Allow).Distinct(StringComparer.Ordinal).ToArray();
        var denies = combined.SelectMany(x => _roles[x].Deny).Distinct(StringComparer.Ordinal).ToArray();
        int immunity = combined.Length == 0 ? 0 : combined.Max(x => _roles[x].Immunity);
        return new(defaultAllow, assignedAllow, denies, combined, immunity, allowEveryonePractice);
    }
    private string[] Expand(IEnumerable<string> roots)
        => roots.SelectMany(x => _closures[x]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool IsSteamId64(string? value) => value is { Length: 17 } && value.All(c => c is >= '0' and <= '9') &&
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 76561197960265728UL;
    public static string NormalizeCommand(string? command)
    {
        if (command == null || command.Length is < 5 or > MaxCommandLength || !command.StartsWith("css_", StringComparison.OrdinalIgnoreCase)) return "";
        if (command.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))) return "";
        return command.ToLowerInvariant();
    }
    private static string NormalizePattern(string value) => value.ToLowerInvariant();
    private static void ValidatePattern(string value)
    {
        if (value == "*") return;
        var prefix = value.EndsWith('*') ? value[..^1] : value;
        if (value.Length > MaxCommandLength || (NormalizeCommand(prefix).Length == 0 && !(value.EndsWith('*') && prefix.Equals("css_", StringComparison.OrdinalIgnoreCase))) || prefix.Contains('*'))
            throw Invalid("Rules must be css_ commands, terminal prefix*, or *; regex and command arguments are not accepted.");
    }
    private static void ValidateRoleName(string value)
    {
        if (value.Length is < 1 or > MaxRoleNameLength || value != value.Trim() || value.Any(char.IsControl))
            throw Invalid("Role names must be non-empty, bounded, and without surrounding whitespace/control characters.");
    }
    private static Dictionary<string, JsonElement> Properties(JsonElement element, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid("Expected a JSON object.");
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw Invalid("Unknown or duplicate configuration property.");
            map.Add(property.Name, property.Value);
        }
        return map;
    }
    private static string[] Strings(Dictionary<string, JsonElement> data, string key, int limit, Action<string> validate)
        => data.TryGetValue(key, out var value) ? StringArray(value, limit, validate) : [];
    private static string[] StringArray(JsonElement element, int limit, Action<string> validate)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > limit) throw Invalid("Expected a bounded array.");
        var result = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) throw Invalid("Role and command lists must contain strings.");
            var text = item.GetString()!; validate(text); result.Add(text);
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static InvalidDataException Invalid(string message) => new(message);
    private static bool Matches(string pattern, string command) => pattern == "*" ||
        (pattern.EndsWith('*') ? command.AsSpan().StartsWith(pattern.AsSpan(0, pattern.Length - 1), StringComparison.Ordinal) : command == pattern);

    public sealed class PermissionAccess
    {
        private readonly string[] _defaults, _assigned, _denies;
        private readonly bool _allowDefaultPractice;
        public IReadOnlyList<string> Roles { get; }
        public int Immunity { get; }
        internal PermissionAccess(string[] defaults, string[] assigned, string[] denies, string[] roles, int immunity, bool allowDefaultPractice)
        {
            _defaults = defaults; _assigned = assigned; _denies = denies;
            Roles = Array.AsReadOnly(roles); Immunity = immunity; _allowDefaultPractice = allowDefaultPractice;
        }
        public bool Allows(string command)
        {
            var canonical = NormalizeCommand(command);
            if (IsPublicCommand(canonical)) return true;
            if (canonical.Length == 0 || _denies.Any(x => Matches(x, canonical))) return false;
            if (_assigned.Any(x => Matches(x, canonical))) return true;
            if (!_allowDefaultPractice && SelfPracticeCommands.Contains(canonical)) return false;
            return _defaults.Any(x => Matches(x, canonical));
        }
    }
}
