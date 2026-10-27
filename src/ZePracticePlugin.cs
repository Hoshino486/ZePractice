using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
namespace ZePractice;

public sealed partial class ZePracticePlugin : BasePlugin, IPluginConfig<ZePracticeConfig>
{
    public override string ModuleName => "ZE Practice · FYS Config Adapter";
    public override string ModuleVersion => "1.7.26";
    public override string ModuleAuthor => "Local ZE Server";
    public override string ModuleDescription => "无奖励的 ZE 跑图、管理、Boss、地图翻译与 FYS 配置兼容";
    public ZePracticeConfig Config { get; set; } = new();
    private MapConfigService? _map;
    private BossTranslationService? _boss;
    private AdminPracticeService? _admin;
    private GameplayService? _gameplay;
    private EntWatchService? _items;
    private PracticeRoundService? _roundGuard;
    private PublicGameplayService? _publicGameplay;
    private WorkshopVoteService? _mapVote;
    private MapMarkerService? _mapMarkers;
    private GrenadeImmunityService? _grenadeImmunity;
    private MapIdentityService? _mapIdentity;
    private WorkshopUpdateService? _workshopUpdates;
    private ThirdPersonService? _thirdPerson;
    public bool ThirdPersonEnabled(CCSPlayerController p) => _thirdPerson?.Enabled(p) == true;
    private CommanderService? _commander;
    private ServerPlayerModelService? _serverModels;
    private MovementHudService? _movementHud;
    private ServerDiagnosticsService? _serverDiagnostics;
    private ViewerPlateService? _plates;
    private TeammateTagService? _teammateTags;
    public bool DiagnosticsHudEnabled(CCSPlayerController p) => _serverDiagnostics?.Enabled(p) ?? true;
    private PracticeBotService? _practiceBots;
    public bool IsPracticeBot(CCSPlayerController? p) => _practiceBots?.IsPracticeBot(p) == true;
    public void RefreshMovementResourcesWhenEmpty()
    { if (Utilities.GetPlayers().Count == 0) { _movementHud?.OnMapStart(); _serverDiagnostics?.RefreshResources(); _radioPanel?.RefreshResources(); } }
    public bool MovementHudEnabled(CCSPlayerController player) => _movementHud?.Enabled(player) ?? true;
    public bool IsCommander(CCSPlayerController player) => _commander?.IsCommander(player) == true;
    public bool IsTeamResetPending(CCSPlayerController player) => _publicGameplay?.TeamResetPending(player) == true;
    public void RefreshMapIdentity(CCSPlayerController player) => _mapIdentity?.Invalidate(player.Slot);
    private int _mapGeneration;
    private string _currentMap = "";
    public bool IsPractice => Config.PracticeMode;
    public MapConfigService MapSettings => _map!;
    public string FysDirectory => Path.GetFullPath(Path.IsPathRooted(Config.FysRoot) ? Config.FysRoot : Path.Combine(ModuleDirectory, Config.FysRoot));
    public string CurrentMapKey
    {
        get
        {
            var name = NormalizeMapName(_currentMap.Length > 0 ? _currentMap : Server.MapName);
            // One explicit alias only: a typo/cycle cannot redirect outside the selected FYS tree.
            foreach (var entry in Config.MapAliases)
                if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
                    return NormalizeMapName(entry.Value);
            return name;
        }
    }
    public static string NormalizeMapName(string name)
    {
        name = (name ?? "").Replace('\\', '/').Split('/').Last().ToLowerInvariant();
        if (name.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return Regex.IsMatch(name, "^[a-z0-9_][a-z0-9_-]{0,127}$") ? name : "";
    }
    public static bool IsRealPlayer(CCSPlayerController? player) => player is { IsValid: true, IsBot: false, IsHLTV: false };
    public bool CanAdmin(CCSPlayerController? player) => Permissions is not null && Permissions.HasAdminAccess(player);
    public void Reply(CCSPlayerController? player, string message)
    {
        if (IsRealPlayer(player)) player!.PrintToChat($" \x04[ZE练习]\x01 {message}");
        else Server.PrintToConsole("[ZePractice] " + message);
    }
    public void OnConfigParsed(ZePracticeConfig config)
    {
        if (!Regex.IsMatch(config.ServerNumber, "^[0-9]{1,8}$")) throw new InvalidDataException("ServerNumber must be numeric.");
        config.HudIntervalSeconds = Math.Clamp(config.HudIntervalSeconds, 0.2f, 5f);
        config.RespawnDelaySeconds = Math.Clamp(config.RespawnDelaySeconds, 0.1f, 30f);
        config.DefaultHealth = Math.Clamp(config.DefaultHealth, 1, 1000000);
        config.PublicGameplay ??= new();
        config.PublicGameplay.Validate();
        config.MapVote ??= new(); config.MapVote.Validate();
        config.MapMarkers ??= new(); config.MapMarkers.Validate();
        config.WorkshopUpdates ??= new(); config.WorkshopUpdates.Validate();
        config.Commander ??= new(); config.ServerModels ??= new(); config.ServerModels.Validate();
        config.PracticeBots ??= new(); config.PracticeBots.Validate();
        config.MapAliases ??= new(); config.OwnerSteamIds ??= new();
        config.MenuUiMode = (config.MenuUiMode ?? "chat").Trim().ToLowerInvariant();
        if (config.MenuUiMode is not ("panorama" or "chat" or "radio")) throw new InvalidDataException("MenuUiMode must be panorama, radio or chat.");
        Config = config;
    }
    public override void Load(bool hotReload)
    {
        InitializeAccess();
        _roundGuard = new(this);
        _practiceBots = new(this); _practiceBots.Initialize();
        AddZeCommand("css_ze_round_status", "回合冻结保护与实时状态", (player, _) => Reply(player, _roundGuard.Diagnostics()));
        _grenadeImmunity = new(this);
        _grenadeImmunity.Initialize();
        AddZeCommand("css_ze_immunity_status", "全服闪光与烟雾免疫状态", (player, _) => Reply(player, _grenadeImmunity.Diagnostics()));
        _publicGameplay = new(this);
        _mapVote = new(this); _mapMarkers = new(this);
        _map = new(this); _boss = new(this); _admin = new(this); _gameplay = new(this); _items = new(this);
        _map.Initialize(); _boss.Initialize(); _admin.Initialize(); _gameplay.Initialize(); _items.Initialize();
        _publicGameplay.Initialize();
        _mapIdentity = new(this); _mapIdentity.Initialize();
        AddZeCommand("css_ze_identity_status", "地图身份传递诊断", (player, _) => Reply(player, _mapIdentity.Diagnostics(player)));
        _mapVote.Initialize(hotReload); _mapMarkers.Initialize();
        _thirdPerson = new(this); _thirdPerson.Initialize();
        _commander = new(this); _commander.Initialize();
        _serverModels = new(this); _serverModels.Initialize();
        _movementHud = new(this); _movementHud.Initialize();
        _serverDiagnostics = new(this); _serverDiagnostics.Initialize();
        _plates = new ViewerPlateService(this); _plates.Initialize();
        _teammateTags = new(this); _teammateTags.Initialize();
        _workshopUpdates = new(this); _workshopUpdates.Initialize();
        RegisterListener<Listeners.OnMapStart>(StartMap);
        RegisterListener<Listeners.OnMapEnd>(() => { _mapGeneration++; _practiceBots?.OnMapEnd(); _movementHud?.OnMapEnd(); _serverDiagnostics?.OnMapEnd(); _plates?.OnMapEnd(); _thirdPerson?.Reset(false); _commander?.OnMapEnd(); _serverModels?.OnMapEnd(); _mapIdentity?.OnMapEnd(); _roundGuard?.OnMapEnd(); _publicGameplay?.OnMapEnd(); _mapVote?.OnMapEnd(); _mapMarkers?.OnMapEnd(); _grenadeImmunity?.OnMapEnd(); });
        RegisterEventHandler<EventRoundStart>((ev, info) =>
        {
            _map.OnRoundStart(); _boss.OnRoundStart(); _admin.OnRoundStart(); _gameplay.OnRoundStart(); _items.OnRoundStart();
            _roundGuard?.RoundStarted();
            _practiceBots?.Reset("重开回合");
            _publicGameplay?.OnRoundStart();
            _mapMarkers?.OnRoundStart();
            _commander?.OnRoundStart();
            return HookResult.Continue;
        });
        RegisterEventHandler<EventPlayerSpawn>((ev, info) =>
        {
            if (IsRealPlayer(ev.Userid)) { _admin.OnPlayerSpawn(ev.Userid!); _gameplay.OnPlayerSpawn(ev.Userid!); }
            return HookResult.Continue;
        });
        AddZeCommand("css_zestatus", "Show map adapter status", (p,c) => ShowStatus(p));
        AddZeCommand("css_zereload", "Reload original FYS configurations", (p,c) =>
        {
            if (!NeedPermission(p,"css_ze_reload")) return;
            ReloadMapConfiguration(); Reply(p,"配置已重载；地图实体修补需要重新加载地图。");
        });
        AddZeCommand("ze_status", "Server status", (p,c) => { if (NeedPermission(p,"css_ze_status")) ShowStatus(p); });
        AddZeCommand("ze_mode", "practice|ze", (p,c) =>
        {
            if (!NeedPermission(p,"css_ze_mode")) return;
            var mode = c.GetArg(1).ToLowerInvariant();
            if (mode is not ("practice" or "ze")) { Reply(p,"ze_mode practice / ze"); return; }
            SetPracticeMode(mode == "practice");
        });
        Logger.LogInformation("ZePractice {Version} loaded. FYS root={Root}; no reward/store module.", ModuleVersion,FysDirectory);
        if (hotReload) Server.NextFrame(() => { StartMap(Server.MapName); _commander?.OnHotReload(); });
    }
    private void StartMap(string map)
    {
        _currentMap = map; var generation = ++_mapGeneration;
        _practiceBots?.OnMapStart();
        _grenadeImmunity?.OnMapStart();
        _roundGuard?.OnMapStart();
        _map?.OnMapStart(map); _boss?.OnMapStart(map); _admin?.OnMapStart(map); _gameplay?.OnMapStart(map); _items?.OnMapStart(map);
        _publicGameplay?.OnMapStart();
        _mapIdentity?.OnMapStart();
        _mapVote?.OnMapStart(); _mapMarkers?.OnMapStart();
        _thirdPerson?.Reset(false);
        _commander?.OnMapStart(); _serverModels?.OnMapStart();
        _movementHud?.OnMapStart(); _serverDiagnostics?.OnMapStart(); _plates?.OnMapStart(); _teammateTags?.OnMapStart();
        // Engine/CS2Fixes map configs may execute after OnMapStart. Apply our chosen profile last.
        AddTimer(3f, () => { if (_mapGeneration == generation) { ApplyBaseFeatures(); _map?.ApplyCurrentMap(); _publicGameplay?.ReapplyRules(); if (IsPractice) Server.ExecuteCommand("mp_warmup_end"); ShowStatus(null); } }, TimerFlags.STOP_ON_MAPCHANGE);
    }
    public void ReloadMapConfiguration()
    {
        _map?.OnMapStart(_currentMap); _boss?.OnMapStart(_currentMap); _gameplay?.OnMapStart(_currentMap); _items?.OnMapStart(_currentMap);
        ApplyBaseFeatures(); _map?.ApplyCurrentMap(); _publicGameplay?.ReapplyRules();
    }
    public void SetPracticeMode(bool value)
    {
        Config.PracticeMode = value;
        Config.EnableZombieMode = !value;
        ApplyBaseFeatures(); _map?.ApplyCurrentMap();
        // Respawns/round restart reset god/noclip/checkpoints via the practice service.
        _admin?.OnMapStart(_currentMap);
        _publicGameplay?.ReapplyRules();
        Server.PrintToChatAll(value ? " [ZE练习] 已切换单人/合作跑图模式；如需重开使用 !ze_restart。" : " [ZE练习] 已切换多人 ZE 模式；如需重开使用 !ze_restart。");
    }
    public static bool SetCvar(string name, string value)
    {
        var cvar = ConVar.Find(name);
        if (cvar is null) return false;
        cvar.StringValue = value;
        return true;
    }
    private void ApplyBaseFeatures()
    {
        foreach (var pair in PublicGameplayLogic.ServerPlayerProtectionRules) SetCvar(pair.Key, pair.Value);
        foreach (var name in new[]{"cs2f_commands_enable","cs2f_admin_commands_enable","cs2f_stopsound_enable","cs2f_hide_enable","cs2f_noshake_enable","cs2f_enable_button_watch","cs2f_drop_map_weapons","cs2f_map_steamids_enable","cs2f_free_armor"}) SetCvar(name,"1");
        SetCvar("cs2f_leader_enable", Config.Commander.Enabled ? "0" : "1"); // one owner for pings/glow, never both modules
        SetCvar("cs2f_votemanager_enable","0");
        SetCvar("cs2f_weapons_enable","0"); // ZePractice handles weapon purchase limits.
        SetCvar("entwatch_enable","0"); // CS2Fixes declares EntWatch incompatible with CounterStrikeSharp.
        SetCvar("cs2f_noblock_enable","1");
        SetCvar("cs2f_infinite_reserve_ammo","0");
        SetCvar("cs2f_trigger_timer_enable","0"); // our translated countdown owns this channel
        SetCvar("cs2f_full_alltalk","1");
        // Teammates keep the engine's native team-ID tag; the plugin plate only serves enemies.
        SetCvar("sv_teamid_overhead","1"); SetCvar("sv_teamid_overhead_always_prohibit","0");
        SetCvar("sv_teamid_overhead_maxdist","0"); SetCvar("sv_teamid_overhead_maxdist_spec","0");
        SetCvar("mp_autoteambalance","0"); SetCvar("mp_limitteams","0");
        SetCvar("mp_freezetime","0"); SetCvar("mp_warmuptime","5");
        // Personal bots own quota while active; never kick them during a rules refresh.
        if (IsPractice) _practiceBots?.ReapplyPracticeQuota();
        SetCvar("mp_buy_anywhere","1"); SetCvar("mp_buytime","9999");
        SetCvar("mp_maxmoney",Config.PublicGameplay.Money.ToString());
        SetCvar("mp_startmoney",Config.PublicGameplay.Money.ToString());
        SetCvar("zr_enable",Config.EnableZombieMode && !IsPractice?"1":"0");
        SetCvar("mp_ignore_round_win_conditions","1");
        SetCvar("mp_respawn_on_death_ct",IsPractice?"1":"0");
        SetCvar("mp_respawn_on_death_t",IsPractice?"1":"0");
        SetCvar("cs2f_vote_bypass_cd_max_count",IsPractice?"64":"0");
    }
    public void ShowStatus(CCSPlayerController? player)
    {
        Reply(player,$"模式={(IsPractice?"跑图练习":"多人ZE")}，地图={_currentMap}，FYS配置名={CurrentMapKey}");
        var serverRoot = Path.Combine(FysDirectory, Config.ServerNumber);
        foreach(var (label,path) in new[]{("地图参数",Path.Combine(serverRoot,"csgo","cfg","map-configs",CurrentMapKey+".cfg")),("Boss",Path.Combine(serverRoot,"sharp","configs","bosses",CurrentMapKey+".jsonc")),("翻译",Path.Combine(serverRoot,"sharp","configs","translations",CurrentMapKey+".jsonc"))})
            Reply(player,$"{label}：{(File.Exists(path)?"配置文件存在（加载详情看服务端日志）":"本图无配置，使用基础行为")}");
        Reply(player,"主菜单 !zemenu / 管理 !zeadmin / 帮助 !zhelp；奖励与商店未安装。");
        if (player is null)
            Logger.LogInformation("ZE_STATUS {Json}", JsonSerializer.Serialize(new{mode=IsPractice?"practice":"ze",map=_currentMap,config=CurrentMapKey,fysRoot=FysDirectory,cs2fixes=ConVar.Find("cs2f_commands_enable") is not null,boss=Config.BossHud,translations=Config.MapTranslations}));
    }
    public override void Unload(bool hotReload)
    {
        _mapGeneration++;
        // A failed Load must not strand another module's native hooks/callbacks.
        void Cleanup(string name, Action action) => PluginCleanup.Run(name, action,
            (operation, error) => Logger.LogError(error, "UNLOAD {Operation} failed; continuing cleanup", operation));
        Cleanup("practice-bots", () => _practiceBots?.Dispose());
        Cleanup("workshop", () => _workshopUpdates?.Dispose());
        Cleanup("third-person", () => _thirdPerson?.Dispose());
        Cleanup("movement-hud", () => _movementHud?.Dispose());
        Cleanup("server-diagnostics", () => _serverDiagnostics?.Dispose());
        Cleanup("player-plates", () => _plates?.Dispose());
        Cleanup("teammate-tags", () => _teammateTags?.Dispose());
        Cleanup("commander", () => _commander?.Dispose()); Cleanup("server-models", () => _serverModels?.Dispose());
        Cleanup("identity", () => _mapIdentity?.Dispose());
        Cleanup("immunity", () => _grenadeImmunity?.Dispose());
        Cleanup("votes", () => _mapVote?.Dispose(hotReload)); Cleanup("markers", () => _mapMarkers?.Dispose());
        Cleanup("public-gameplay", () => _publicGameplay?.Dispose());
        Cleanup("round-guard", () => _roundGuard?.Dispose());
        Cleanup("panel", () => _panel?.Dispose()); Cleanup("admin", () => _admin?.Dispose());
        Cleanup("items", () => _items?.Dispose()); Cleanup("gameplay", () => _gameplay?.Dispose());
        Cleanup("boss", () => _boss?.Dispose()); Cleanup("map", () => _map?.Dispose()); Cleanup("access", DisposeAccess);
    }
}
