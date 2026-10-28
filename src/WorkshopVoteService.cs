using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;

namespace ZePractice;

public sealed class WorkshopVoteService : IDisposable
{
    private readonly ZePracticePlugin _plugin;
    private readonly WorkshopVoteLedger _ledger = new();
    private readonly Dictionary<string, CommandInfo.CommandCallback> _commands = new();
    private readonly Dictionary<uint, float> _lastNomination = new();
    private readonly string _path;
    private bool _active, _disposed, _pending, _storeHealthy = true, _executing;
    private int _generation, _request;
    private string? _winner;
    private float _nextRefresh;
    private bool _resumeCandidatesOnce;
    private string _mapName = "";
    private string? _namedMapToken;
    private static readonly string ProcessSession = ProcessIdentity();
    private sealed record CandidateSnapshot(int Version, string Map, string Process, bool ResumeAfterHotReload, string[] Candidates);
    private static string ProcessIdentity()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return $"{process.Id}:{process.StartTime.ToUniversalTime().Ticks}";
    }
    private MapVoteOptions Options => _plugin.Config.MapVote;

    public WorkshopVoteService(ZePracticePlugin plugin)
    { _plugin = plugin; _path = Path.Combine(plugin.ModuleDirectory, "data", "workshop-candidates.json"); }

    private void Register(string name, string description, CommandInfo.CommandCallback callback)
    { _commands.Add(name, callback); _plugin.AddZeCommand(name, description, callback); }

    public void Initialize(bool hotReload = false)
    {
        LoadCandidates(hotReload);
        Register("css_rtv", "支持换图，在线真人严格超过 80% 才换图", (player, _) => Rtv(player, false));
        Register("css_unrtv", "撤回本次 RTV 支持", (player, _) => Rtv(player, true));
        Register("css_ze_vote", "选下一张图 <WorkshopID>；每人一票，可改票", VoteCommand);
        Register("css_ze_vote_status", "候选选图和 RTV 进度", (player, _) => _plugin.Reply(player, Diagnostics()));
        Register("css_ze_workshop_now", "仅服主：立即加载 Workshop 地图 <ID>", ForceCommand);
        Register("css_ze_rtv_force", "仅服主：强制通过 RTV 并换到当前票数最高的地图", (player, _) => ForceRtv(player));
        Register("css_ze_forcertv", "仅服主：强制通过 RTV 并换图（别名）", (player, _) => ForceRtv(player));
        Register("css_ze_map_remove", "仅服主：从候选列表删除 <WorkshopID>", RemoveCommand);
        _plugin.AddCommandListener("say", Chat, HookMode.Pre);
        _plugin.AddCommandListener("say_team", Chat, HookMode.Pre);
        _plugin.RegisterListener<Listeners.OnTick>(Tick);
        _plugin.RegisterListener<Listeners.OnClientDisconnect>(Disconnect);
    }

    public void OnMapStart()
    {
        CancelNamedMap();
        _mapName = Server.MapName;
        _generation++; _request++; _active = true; _pending = _executing = false; _winner = null;
        if (_resumeCandidatesOnce) _ledger.ResetBallots(); else _ledger.ResetForMap();
        _resumeCandidatesOnce = false; _lastNomination.Clear(); _nextRefresh = 0;
        // A nomination is for this map only. Consume a same-map hot-reload receipt
        // once; a cold start or a real map load always starts with an empty list.
        if (_storeHealthy && _mapName.Length > 0) SaveCandidates();
        ReapplyRules();
    }

    public void OnMapEnd()
    {
        StopMapSession();
        if (_storeHealthy) SaveCandidates();
    }

    private void StopMapSession()
    {
        CancelNamedMap();
        _generation++; _request++; _active = false; _pending = _executing = false; _winner = null;
        _resumeCandidatesOnce = false; _ledger.ResetForMap(); _lastNomination.Clear();
    }

    public void ReapplyRules() => ZePracticePlugin.SetCvar("cs2f_votemanager_enable", "0");
    private static uint Key(CCSPlayerController player) => player.EntityHandle.Raw;
    private static bool Eligible(CCSPlayerController? player) => ZePracticePlugin.IsRealPlayer(player) && player!.Connected == PlayerConnectedState.Connected;
    private void Refresh(int excludeSlot = -1) => _ledger.SyncVoters(Utilities.GetPlayers().Where(player => Eligible(player) && player.Slot != excludeSlot).Select(Key));

    private void Tick()
    {
        if (!_active || _disposed || Server.CurrentTime < _nextRefresh) return;
        _nextRefresh = Server.CurrentTime + 1;
        if (_executing && _namedMapToken != null)
        {
            string status = ConVar.Find("mm_named_map_status")?.StringValue ?? "";
            if (status.StartsWith(_namedMapToken + "|failed-", StringComparison.Ordinal) || status == _namedMapToken + "|cancelled")
            {
                _namedMapToken = null; _executing = _pending = false; _winner = null; _ledger.ClearRtv(); _request++;
                Broadcast("工坊地图准备失败，已保留当前地图、解除换图锁；请检查下载状态或刷新已安装地图列表后重试。");
            }
        }
        ReapplyRules(); Refresh(); CheckProgress();
    }

    private void Disconnect(int slot)
    {
        if (!_active || _disposed) return;
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player is { IsValid: true }) _lastNomination.Remove(Key(player));
        Refresh(slot); CheckProgress();
    }

    public void Nominate(CCSPlayerController? player, string identifier)
    {
        if (!_active || _disposed || !PublicGameplayLogic.TryWorkshopId(identifier, out identifier)) return;
        if (_pending) { _plugin.Reply(player, "换图已进入倒计时，暂不修改候选。撤回 RTV 可在执行前取消。 "); return; }
        if (!_storeHealthy) { _plugin.Reply(player, "候选文件无效，已保留原文件；请服主检查 data/workshop-candidates.json。 "); return; }
        if (_ledger.Candidates.Contains(identifier)) { _plugin.Reply(player, "该地图已在列表中；!ze_maps 选图，rtv 支持换图。"); return; }
        if (_ledger.Candidates.Count >= Options.MaximumCandidates) { _plugin.Reply(player, $"候选列表已达 {Options.MaximumCandidates} 张，请服主移除旧候选。"); return; }
        if (player != null)
        {
            if (!Eligible(player)) return;
            if (_lastNomination.TryGetValue(Key(player), out float last) && Server.CurrentTime - last < Options.NominationCooldownSeconds)
            { _plugin.Reply(player, "提交过快，请稍后添加下一张候选。"); return; }
            _lastNomination[Key(player)] = Server.CurrentTime;
        }
        if (!_ledger.AddCandidate(identifier, Options.MaximumCandidates)) return;
        if (!SaveCandidates()) { _ledger.RemoveCandidate(identifier); _plugin.Reply(player, "候选保存失败，本次未添加；查看服务端日志。"); return; }
        Broadcast($"已添加 Workshop {identifier} 到换图列表，不会立即切图。!ze_maps 投票选图；rtv 支持换图。");
    }

    public void ShowMenu(CCSPlayerController? player, string search = "")
    {
        if (!_active || _disposed) return;
        Refresh();
        _plugin.Reply(player, Diagnostics());
        bool empty = _ledger.Candidates.Count == 0;
        if (empty) _plugin.Reply(player, "本图尚未预定地图。聊天输入 host_workshop_map +地图ID 添加候选；每次换图后需重新预定，添加不会换图。");
        if (player == null)
        { foreach (string identifier in _ledger.Candidates) _plugin.Reply(null, identifier + "：" + _ledger.VotesFor(identifier) + "票"); return; }
        if (!Eligible(player)) return;
        int generation = _generation;
        var menu = new ChatMenu("下一张地图投票（换图后清空预定）") { ExitButton = true };
        if (empty) menu.AddMenuOption("本图尚未预定地图，请先在聊天添加", (_, _) => { }, true);
        foreach (string identifier in _ledger.Candidates.Where(identifier => identifier.Contains(search, StringComparison.OrdinalIgnoreCase)))
        {
            string captured = identifier;
            string chosen = _ledger.Choice(Key(player)) == identifier ? " ✓" : "";
            menu.AddMenuOption($"Workshop {identifier} · {_ledger.VotesFor(identifier)}票{chosen}",
                (who, _) => { if (generation == _generation) Vote(who, captured); }, _pending);
        }
        menu.AddMenuOption("RTV：支持换图（严格超过80%）", (who, _) => { if (generation == _generation) Rtv(who, false); }, empty);
        menu.AddMenuOption("撤回我的 RTV", (who, _) => { if (generation == _generation) Rtv(who, true); }, empty);
        _plugin.OpenMenu(player, menu);
    }

    private void VoteCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount != 2 || !PublicGameplayLogic.TryWorkshopId(command.GetArg(1), out string identifier))
        { ShowMenu(player); return; }
        Vote(player, identifier);
    }

    private void Vote(CCSPlayerController? player, string identifier)
    {
        if (!_active || _disposed || !Eligible(player)) return;
        if (_pending) { _plugin.Reply(player, "换图倒计时中，选图票已锁定。"); return; }
        Refresh();
        if (!_ledger.Vote(Key(player!), identifier)) { _plugin.Reply(player, "该候选已移除；!ze_maps 刷新列表。"); return; }
        _plugin.Reply(player, $"已投票 Workshop {identifier}，当前 {_ledger.VotesFor(identifier)} 票；每人一票，可改票。另需 rtv 支持才会换图。");
        CheckProgress();
    }

    private void Rtv(CCSPlayerController? player, bool withdraw)
    {
        if (!_active || _disposed || !Eligible(player)) { _plugin.Reply(player, "RTV 只统计在线真人（含观察者），控制台和机器人不计票。"); return; }
        if (_executing) { _plugin.Reply(player, "换图指令已提交，正在等待引擎加载。"); return; }
        Refresh();
        if (withdraw)
        {
            if (!_ledger.Withdraw(Key(player!))) { _plugin.Reply(player, "当前没有提交 RTV。"); return; }
        }
        else
        {
            if (_ledger.Candidates.Count == 0) { ShowMenu(player); return; }
            if (!_ledger.Support(Key(player!))) { _plugin.Reply(player, "已计入 RTV，请勿重复。" + Diagnostics()); return; }
        }
        Broadcast((withdraw ? "一名玩家撤回 RTV。" : "一名玩家支持 RTV。") + Diagnostics());
        CheckProgress();
        if (!_ledger.CanSelect && !withdraw) ShowMenu(player);
    }

    private HookResult Chat(CCSPlayerController? player, CommandInfo command)
    {
        if (!Eligible(player) || command.ArgCount < 2) return HookResult.Continue;
        string text = command.GetArg(1).Trim();
        if (!text.Equals("rtv", StringComparison.OrdinalIgnoreCase) && !text.Equals("unrtv", StringComparison.OrdinalIgnoreCase)) return HookResult.Continue;
        Rtv(player, text.Equals("unrtv", StringComparison.OrdinalIgnoreCase));
        return HookResult.Stop;
    }

    private void CheckProgress()
    {
        if (!_active || _disposed || _executing) return;
        if (_pending)
        {
            if (!_ledger.Quorum)
            { _pending = false; _winner = null; _request++; Broadcast("在线人数或 RTV 已变化，未严格超过80%，取消换图倒计时；选图票保留。"); }
            return;
        }
        if (!_ledger.Quorum || !_ledger.CanSelect) return;
        _winner = _ledger.SelectWinner(Random.Shared.Next);
        if (_winner == null) return;
        _pending = true;
        int generation = _generation, request = ++_request;
        Broadcast($"RTV {_ledger.RtvCount}/{_ledger.PlayerCount}，严格超过80%；下一张 Workshop {_winner}（最高票并列随机）。{Options.ChangeDelaySeconds} 秒后换图。");
        _plugin.AddTimer(Options.ChangeDelaySeconds, () =>
        {
            if (_disposed || !_active || generation != _generation || request != _request || !_pending) return;
            Refresh(); CheckProgress();
            if (!_pending || _winner == null) return;
            ExecuteChange("host_workshop_map " + _winner);
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void ForceCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_plugin.Permissions.IsServerOwner(player)) { _plugin.Reply(player, "立即换图仅服主可用；其他玩家请添加候选并 RTV。"); return; }
        if (command.ArgCount != 2 || !PublicGameplayLogic.TryWorkshopId(command.GetArg(1), out string identifier))
        { _plugin.Reply(player, "用法：!ze_workshop_now 地图ID（仅服主）。"); return; }
        ForceChange(player, "host_workshop_map " + identifier);
    }

    public bool ForceRtv(CCSPlayerController? player)
    {
        if (!_plugin.Permissions.IsServerOwner(player)) { _plugin.Reply(player, "强制 RTV 仅服主可用。"); return false; }
        if (!_active || _disposed) { _plugin.Reply(player, "当前没有可用的换图投票。"); return false; }
        if (_executing) { _plugin.Reply(player, "换图指令已提交，正在等待引擎加载。"); return false; }
        Refresh();
        if (_ledger.Candidates.Count == 0) { _plugin.Reply(player, "换图列表为空：先 !ze_maps 添加候选，或 !ze_map <地图> 直接切图。"); return false; }
        if (!_ledger.CanSelect) { _plugin.Reply(player, "还没有选图票：先 !ze_maps 选一张（只有一张候选时可直接强制）。"); return false; }
        if (_pending) { _plugin.Reply(player, "换图倒计时已在进行中，稍等即可。"); return true; }
        _ledger.ForceQuorum();
        Broadcast($"服主强制 RTV（{_ledger.RtvCount}/{_ledger.PlayerCount}）。" + Diagnostics());
        CheckProgress();
        if (!_pending) { _plugin.Reply(player, "强制 RTV 未启动：当前没有在线真人，无法统计 RTV。"); return false; }
        return true;
    }
    public bool ForceChange(CCSPlayerController? player, string command)
    {
        if (!_active || _disposed || !_plugin.Permissions.IsServerOwner(player)) return false;
        if (_executing) { _plugin.Reply(player, "已有换图请求正在加载，请等待结果。"); return false; }
        if (command.StartsWith("mm_host_workshop_map ", StringComparison.Ordinal) &&
            ConVar.Find("mm_named_map_api")?.StringValue != "1")
        {
            _plugin.Reply(player, "工坊地图加载桥未就绪，当前地图保持运行；请加载配套的 MultiAddonManager 修复版后重试。");
            return false;
        }
        _request++; _pending = true; _winner = null;
        Broadcast("服主已请求立即换图。");
        ExecuteChange(command);
        return true;
    }

    private void ExecuteChange(string command)
    {
        _executing = true;
        bool named = command.StartsWith("mm_host_workshop_map ", StringComparison.Ordinal);
        if (named) { _namedMapToken = Guid.NewGuid().ToString("N"); command += " " + _namedMapToken; }
        int generation = _generation, request = _request;
        _plugin.AddTimer(Options.LoadTimeoutSeconds, () =>
        {
            if (_disposed || !_active || generation != _generation || request != _request || !_executing) return;
            CancelNamedMap();
            _executing = _pending = false; _winner = null; _ledger.ClearRtv(); _request++;
            Broadcast(named ? "工坊地图准备超时，已取消本次直切并保留当前地图；下载可继续，但完成后不会执行已取消的切图。请稍后重试。" :
                "等待引擎换图超时，已解除投票锁；候选保留，需重新 RTV。引擎下载可能仍在继续，详情见控制台。");
        }, TimerFlags.STOP_ON_MAPCHANGE);
        Server.NextFrame(() =>
        {
            if (_disposed || !_active || generation != _generation || request != _request || !_executing) return;
            // A fresh explicit default-map request (RTV / owner by ID) must not
            // inherit a cancelled exact-name override for the same Workshop ID.
            const string prefix = "host_workshop_map ";
            if (command.StartsWith(prefix, StringComparison.Ordinal) &&
                PublicGameplayLogic.TryWorkshopId(command[prefix.Length..], out string id) && ConVar.Find("mm_named_map_api")?.StringValue == "1")
                Server.ExecuteCommand("mm_allow_workshop_map " + id);
            Server.ExecuteCommand(command);
        });
    }

    private void CancelNamedMap()
    {
        if (_namedMapToken == null) return;
        Server.ExecuteCommand("mm_cancel_workshop_map " + _namedMapToken);
        _namedMapToken = null;
    }

    private void RemoveCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_plugin.Permissions.IsServerOwner(player) || !_active || _pending) { _plugin.Reply(player, "仅服主可编辑候选，换图期间锁定。"); return; }
        if (!_storeHealthy || command.ArgCount != 2 || !PublicGameplayLogic.TryWorkshopId(command.GetArg(1), out string identifier))
        { _plugin.Reply(player, "用法：!ze_map_remove 地图ID；候选文件须有效。"); return; }
        var remaining = _ledger.Candidates.Where(candidate => candidate != identifier).ToArray();
        if (remaining.Length == _ledger.Candidates.Count) { _plugin.Reply(player, "列表中没有此地图。"); return; }
        if (!SaveCandidates(remaining)) { _plugin.Reply(player, "保存失败，候选未移除。"); return; }
        _ledger.RemoveCandidate(identifier);
        _plugin.Reply(player, "已移除候选 " + identifier + "；该图选票清除。");
    }

    private void LoadCandidates(bool hotReload)
    {
        try
        {
            if (!File.Exists(_path)) return;
            if (new FileInfo(_path).Length > 131072) throw new InvalidDataException("Candidate file too large.");
            string json = File.ReadAllText(_path);
            using var document = JsonDocument.Parse(json);
            CandidateSnapshot? snapshot = document.RootElement.ValueKind == JsonValueKind.Array ? null :
                JsonSerializer.Deserialize<CandidateSnapshot>(json) ?? throw new InvalidDataException("Expected candidate snapshot.");
            if (snapshot != null && (snapshot.Version != 1 || string.IsNullOrWhiteSpace(snapshot.Map) || string.IsNullOrWhiteSpace(snapshot.Process)))
                throw new InvalidDataException("Invalid candidate snapshot scope.");
            var entries = (snapshot == null ? JsonSerializer.Deserialize<string[]>(json) : snapshot.Candidates)
                ?? throw new InvalidDataException("Expected candidate array.");
            var staged = new WorkshopVoteLedger();
            foreach (string entry in entries)
                if (!staged.AddCandidate(entry, Options.MaximumCandidates)) throw new InvalidDataException("Invalid or duplicate candidate.");
            // Legacy arrays were a permanent map catalogue, not this map's nominations.
            // Only an explicit unload(true) receipt from this process and map may resume.
            if (hotReload && snapshot is { ResumeAfterHotReload: true } && snapshot.Process == ProcessSession && snapshot.Map == Server.MapName)
            {
                foreach (string entry in entries) _ledger.AddCandidate(entry, Options.MaximumCandidates);
                _resumeCandidatesOnce = true;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { _storeHealthy = false; _plugin.Logger.LogError(error, "Workshop candidates not loaded; original file preserved."); }
    }

    private bool SaveCandidates(IEnumerable<string>? entries = null, bool resumeAfterHotReload = false)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var snapshot = new CandidateSnapshot(1, _mapName, ProcessSession, resumeAfterHotReload, (entries ?? _ledger.Candidates).ToArray());
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(_path + ".tmp", _path, true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { _plugin.Logger.LogError(error, "Workshop candidates save failed."); return false; }
    }

    public string Diagnostics() => $"候选={_ledger.Candidates.Count}；RTV={_ledger.RtvCount}/{_ledger.PlayerCount}，需要{_ledger.RequiredRtv}票（>80%，真人含观察者）；" +
        (_executing ? "引擎加载中" : _pending ? "下一张=" + _winner : _ledger.Candidates.Count == 1 ? "单候选免选图，仍需RTV" : "!ze_maps 选图，rtv 支持换图");
    private static void Broadcast(string message) => Server.PrintToChatAll(" \x04[换图投票]\x01 " + message);

    public void Dispose() => Dispose(false);
    public void Dispose(bool hotReload)
    {
        if (_disposed) return;
        if (_storeHealthy && _mapName.Length > 0)
        {
            if (hotReload && _active) SaveCandidates(resumeAfterHotReload: true);
            else SaveCandidates(Array.Empty<string>());
        }
        _disposed = true; StopMapSession();
        _plugin.RemoveCommandListener("say", Chat, HookMode.Pre); _plugin.RemoveCommandListener("say_team", Chat, HookMode.Pre);
        _plugin.RemoveListener<Listeners.OnTick>(Tick); _plugin.RemoveListener<Listeners.OnClientDisconnect>(Disconnect);
        foreach (var command in _commands) _plugin.RemoveZeCommand(command.Key, command.Value);
    }
}
