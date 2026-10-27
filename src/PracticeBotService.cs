using System.Globalization;
using System.Numerics;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using Microsoft.Extensions.Logging;

namespace ZePractice;

public sealed partial class PracticeBotService(ZePracticePlugin plugin) : IDisposable
{
    private readonly PracticeBotRoster _roster = new();
    private readonly Dictionary<PracticeIdentity, int> _health = new();
    private readonly Dictionary<PracticeIdentity, double> _nextAdd = new();
    private readonly Dictionary<long, LiveBot> _live = new();
    private readonly Dictionary<IntPtr, LiveBot> _controlled = new();
    private MemoryFunctionVoid<IntPtr>? _botUpdate;
    private long _inputCorrections;
    private readonly Dictionary<string, CommandInfo.CommandCallback> _commands = new();
    private readonly HashSet<string> _errors = new();
    private readonly Queue<string> _lifecycle = new();
    private void Note(string value) { _lifecycle.Enqueue(value); while(_lifecycle.Count>30)_lifecycle.Dequeue(); }
    private bool _active, _disposed, _quotaLeased;
    private int _menuGeneration;
    private string _originalMode = "normal";
    private int _originalQuota;
    private int _lastQuota;
    private float _nextMaintenance;
    private PracticeBotRoster.Entry? _spawning;
    private HashSet<PracticeIdentity> _spawnBefore = new();
    private readonly Dictionary<int,long> _connections = new();
    private long _connectionSequence;
    private readonly Dictionary<int,(PracticeBotRoster.Entry Entry,long Connection,int ReadyTick)> _pendingSlots=new();
    private string _oldPrefix = "";
    private sealed class LiveBot(CCSPlayerController player)
    {
        public CCSPlayerController Player = player;
        public uint Pawn;
        public PracticeBotStatistics? Stats;
        public bool Finished, Executing;
        public string LastAttacker = "地图/环境";
        public double RespawnAt;
        public double LastDamageAt;
        public double NextRegenAt;
    }
    private PracticeBotOptions Options => plugin.Config.PracticeBots;
    private PracticeIdentity Identity(CCSPlayerController p) => new(p.Slot, p.EntityHandle.Raw, p.UserId,_connections.GetValueOrDefault(p.Slot));
    private bool Matches(CCSPlayerController? p, PracticeIdentity id) => p is { IsValid: true, Connected: PlayerConnectedState.Connected } && Identity(p) == id;
    private CCSPlayerController? Resolve(PracticeIdentity id)
    { var p = Utilities.GetPlayerFromSlot(id.Slot); return Matches(p, id) ? p : null; }
    public bool IsPracticeBot(CCSPlayerController? p) => p is { IsValid: true, IsBot: true } &&
        (_roster.ByBot(Identity(p)) != null || p.PlayerName.StartsWith("ZEPB_", StringComparison.Ordinal) ||
         _pendingSlots.TryGetValue(p.Slot,out var pending) && pending.Connection==_connections.GetValueOrDefault(p.Slot) ||
         _spawning != null && !_spawnBefore.Contains(Identity(p)));
    public void Initialize()
    {
        try
        {
            if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Bot input hook is verified for Windows x64.");
            string game=Server.GameDirectory;
            if(Directory.Exists(Path.Combine(game,"csgo")))game=Path.Combine(game,"csgo");
            PracticeBotNativeData.Validate(File.ReadAllBytes(Path.Combine(game,"bin","win64","server.dll")));
            var update=new MemoryFunctionVoid<IntPtr>(PracticeBotNativeData.UpdateSignature);
            update.Hook(BotDecisionPost,HookMode.Post);_botUpdate=update;
        }
        catch(Exception error){_errors.Add(error.Message);plugin.Logger.LogError(error,"PRACTICE_BOT input hook initialization failed");}
        void Add(string command, string help, CommandInfo.CommandCallback action) { _commands.Add(command, action); plugin.AddZeCommand(command, help, action); }
        Add("css_ze_bot_add", "在当前位置添加 Bot：!ze_bot_add [t|ct|ally|enemy]，省略默认敌方", (p,c) =>
        {
            if (c.ArgCount is < 1 or > 2) { BotTeamUsage(p); return; }
            if (InGame(p, true)) AddBot(p!, c.ArgCount == 2 ? c.GetArg(1) : "");
        });
        Add("css_ze_bot_hp", "设置之后添加的 Bot 生命：!ze_bot_hp <数值>", SetHealth);
        Add("css_ze_bot_move", "选择 Bot 编号传送到脚下：!ze_bot_move [编号|all]，省略打开列表", MoveCommand);
        Add("css_ze_bot_kill", "处死自己的 Bot：!ze_bot_kill [编号]", (p,c) => { if (InGame(p,false)) Kill(p!, c.GetArg(1)); });
        Add("css_ze_bot_clear", "删除自己放置的全部 Bot", (p,c) => { if (InGame(p,false)) { int n = ClearOwner(Identity(p!), "删除", false); plugin.Reply(p,$"已删除自己的 {n} 个 Bot（含待创建），不影响其他玩家。"); } });
        Add("css_ze_bot_status", "列出自己的 Bot 与实时伤害统计", Status);
        plugin.RegisterListener<Listeners.OnTick>(Tick);
        plugin.RegisterListener<Listeners.OnClientDisconnect>(Disconnect);
        plugin.RegisterListener<Listeners.OnClientPutInServer>(Connected);
        plugin.RegisterEventHandler<EventPlayerHurt>(Hurt, HookMode.Post);
        plugin.RegisterEventHandler<EventPlayerDeath>(Death, HookMode.Pre);
        plugin.AddCommand("ze_practice_bot_begin", "Internal serialized Bot creation", BeginSpawn);
        plugin.AddCommand("ze_practice_bot_end", "Internal serialized Bot creation", EndSpawn);
    }
    private bool InGame(CCSPlayerController? p, bool alive)
    {
        if (!ZePracticePlugin.IsRealPlayer(p)) { plugin.Reply(p,"请在游戏内使用个人 Bot 指令。"); return false; }
        if (_disposed || !_active) { plugin.Reply(p,"当前地图尚未就绪。"); return false; }
        if (alive && (!p!.PawnIsAlive || p.TeamNum is not (2 or 3) || plugin.IsTeamResetPending(p) || p.PlayerPawn.Value is not { IsValid: true, Health: > 0 }))
        { plugin.Reply(p,"存活并完成换队后，再在自己的位置放置 Bot。"); return false; }
        return true;
    }
    private int Preset(CCSPlayerController p, int team) => Math.Clamp(
        _health.GetValueOrDefault(Identity(p), team == 2 ? plugin.Config.PublicGameplay.ZombieHealth : plugin.Config.DefaultHealth), 1, Options.MaximumHealth);
    private string PresetLabel(CCSPlayerController p) => $"僵尸 T {Preset(p,2)} / 人类 CT {Preset(p,3)}";
    private PracticePlacement Capture(CCSPlayerController p, int team)
    {
        var pawn = p.PlayerPawn.Value!; var origin = pawn.AbsOrigin ?? throw new InvalidOperationException("Player origin unavailable"); var a = pawn.EyeAngles;
        return new(new(origin.X,origin.Y,origin.Z), new(0,a.Y,0), team, Preset(p,team), team == 2 ? plugin.Config.PublicGameplay.ZombieArmor : 0);
    }
    // Internal entry point also used by the isolated native probe with an explicitly supplied owner role.
    private bool Request(CCSPlayerController p, bool serverOwner) => RequestForTeam(p, serverOwner, PracticeBotRoster.Opponent(p.TeamNum));
    private bool RequestForTeam(CCSPlayerController p, bool serverOwner, int team)
    {
        if (team is not (2 or 3)) { BotTeamUsage(p); return false; }
        if(_botUpdate==null){plugin.Reply(p,"Bot 控制接口尚未就绪；本次未创建 Bot，请检查 !ze_bot_status。");return false;}
        var owner = Identity(p); double now = Server.CurrentTime;
        if (now < _nextAdd.GetValueOrDefault(owner)) { plugin.Reply(p,"添加太快，稍等半秒再放置。"); return false; }
        int slots = NativeAPI.GetMaxClients(); // Server.MaxPlayers can cache zero when loaded before the first map.
        if (slots > 1 && Utilities.GetPlayers().Count + _roster.Entries.Count(e => e.Bot == null) >= slots - 1)
        { plugin.Reply(p,"服务器玩家槽位不足，保留至少一个真人连接位置。"); return false; }
        // Team and loadout are captured with the position. Changing teams while queued must not retarget a Bot.
        // Both teams share the same per-owner quota, including pending requests.
        var entry = _roster.Reserve(owner, Capture(p,team), serverOwner, 2, now);
        if (entry == null) { plugin.Reply(p,"每人最多 2 个自己的 Bot（含待创建）；先删除或处死后再添加。"); return false; }
        _nextAdd[owner] = now + .5;
        // bot_add's name argument is an existing engine PROFILE, not a custom player name.
        // Only queue our guarded dispatcher. A cancel, disconnect, map change or
        // unload before dispatch must not leave an unguarded bot_add in the buffer.
        Server.ExecuteCommand($"ze_practice_bot_begin {entry.Name}");
        plugin.Reply(p,$"正在当前位置放置 Bot #{entry.Id}：{(entry.Placement.Team == 2 ? "僵尸 T" : "人类 CT")}，生命 {entry.Placement.Health}。");
        return true;
    }
    private void BotTeamUsage(CCSPlayerController? p) => plugin.Reply(p,"用法：!ze_bot_add [t|ct|ally|enemy]；t=僵尸，ct=人类，ally=己方，enemy=敌方；省略默认敌方，每次只添加一个，双方合计最多 2 个（服主不限）。");
    private void AddBot(CCSPlayerController p, string choice)
    {
        if (!PracticeBotRoster.TryResolveTeam(p.TeamNum, choice, out int team)) { BotTeamUsage(p); return; }
        RequestForTeam(p, plugin.Permissions.IsServerOwner(p), team);
    }
    private void BeginSpawn(CCSPlayerController? p, CommandInfo c)
    {
        if (p != null || _disposed || !_active || _roster.ByName(c.GetArg(1)) is not { Retired: false } e || Resolve(e.Owner) == null) return;
        if (_spawning != null) { Server.ExecuteCommand($"ze_practice_bot_begin {e.Name}"); return; }
        LeaseQuota();
        _spawning=e; _spawnBefore=Utilities.GetPlayers().Where(p=>p.IsBot).Select(Identity).ToHashSet();
        _oldPrefix=ConVar.Find("bot_prefix")?.StringValue ?? "";
        ZePracticePlugin.SetCvar("bot_prefix",e.Name);
        Server.ExecuteCommand($"bot_add_{(e.Placement.Team == 2 ? "t" : "ct")}; ze_practice_bot_end {e.Name}");
    }
    private void EndSpawn(CCSPlayerController? p, CommandInfo c)
    {
        if (p != null || _spawning is not { } e || c.GetArg(1)!=e.Name) return;
        try
        {
            var created=Utilities.GetPlayers().Where(p=>p.IsBot && !_spawnBefore.Contains(Identity(p))).ToArray();
            if (created.Length!=1) { _roster.Retire(e,Server.CurrentTime); _roster.Drop(e); plugin.Reply(Resolve(e.Owner),$"Bot #{e.Id} 创建未成功（引擎槽位/队伍限制），额度已释放。"); return; }
            var bot=created[0];
            bot.PlayerName=e.Name; Utilities.SetStateChanged(bot,"CBasePlayerController","m_iszPlayerName");
            // On reused engine slots the final userid/controller serial may settle AFTER bot_add returns.
            // Keep the connection epoch as the provisional correlation and bind the final identity two ticks later.
            _pendingSlots[bot.Slot]=(e,_connections.GetValueOrDefault(bot.Slot),Server.TickCount+2);
            Note($"pending #{e.Id}: {Identity(bot)} tick={Server.TickCount}");
        }
        finally { ZePracticePlugin.SetCvar("bot_prefix",_oldPrefix); _spawning=null; _spawnBefore.Clear(); MaintainQuota(); }
    }
    private void SetHealth(CCSPlayerController? p, CommandInfo c)
    {
        if (!InGame(p,false)) return;
        if (c.ArgCount != 2 || !int.TryParse(c.GetArg(1),NumberStyles.None,CultureInfo.InvariantCulture,out int hp) || hp < 1 || hp > Options.MaximumHealth)
        { plugin.Reply(p,$"用法：!ze_bot_hp <1..{Options.MaximumHealth}>；当前预设 {PresetLabel(p!)}，仅影响之后添加的 Bot。"); return; }
        _health[Identity(p!)] = hp; plugin.Reply(p,$"之后放置的 Bot 生命/上限设为 {hp}；已放置的 Bot 不改血、不清空统计。");
    }
    private PracticeBotRoster.Entry[]? Select(CCSPlayerController p, string arg)
    {
        var mine = _roster.Owned(Identity(p)).ToArray();
        if (string.IsNullOrEmpty(arg) || arg == "all") return mine;
        if (!long.TryParse(arg.TrimStart('#'),NumberStyles.None,CultureInfo.InvariantCulture,out long id)) { plugin.Reply(p,"编号必须是 !ze_bot_status 显示的自己的 Bot 编号；all 表示自己的全部 Bot。"); return null; }
        var selected = mine.Where(e => e.Id == id).ToArray();
        if (selected.Length == 0) plugin.Reply(p,"没有这个编号的个人 Bot；其他玩家的 Bot 不可操作。");
        return selected;
    }
    private void Move(CCSPlayerController p, string arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) { ShowMoveMenu(p); return; }
        var selected = Select(p,arg); if (selected == null || selected.Length == 0) return;
        var placement = Capture(p,p.TeamNum);
        foreach (var e in selected)
        {
            e.Placement = e.Placement with { Position = placement.Position, Angles = placement.Angles };
            if (_live.TryGetValue(e.Id,out var live) && !live.Finished && e.Bot is { } id && Matches(live.Player,id) && live.Player.PlayerPawn.Value is { IsValid:true, Health: > 0 } pawn)
                Teleport(pawn,e.Placement);
        }
        plugin.Reply(p,$"已将自己的 {selected.Length} 个 Bot 放到当前位置，保留血量/阵营/伤害统计。");
    }
    private void Kill(CCSPlayerController p, string arg)
    {
        var selected = Select(p,arg); if (selected == null) return;
        foreach (var e in selected)
        {
            if (_live.TryGetValue(e.Id,out var live) && e.Bot is { } id && Matches(live.Player,id) && live.Player.PlayerPawn.Value is { IsValid:true, Health: > 0 } pawn)
            {
                live.Executing = true;
                pawn.CommitSuicide(false,true);
                if (!live.Finished) Finish(e,live,"处死",true);
            }
            else Remove(e,"取消待创建",false);
        }
        plugin.Reply(p,$"已处死/取消自己的 {selected.Length} 个 Bot。");
    }
    private static void Teleport(CCSPlayerPawn pawn, PracticePlacement placement) => pawn.Teleport(placement.Position,placement.Angles,Vector3.Zero);
    private void Regenerate(PracticeBotRoster.Entry e, LiveBot live, CCSPlayerPawn pawn, double now)
    {
        var options = plugin.Config.PublicGameplay;
        if (now < live.LastDamageAt + options.RegenerationDelaySeconds || now < live.NextRegenAt) return;
        live.NextRegenAt = now + 1;
        int health = Math.Min(e.Placement.Health, pawn.Health + options.HealthRegenerationPerSecond);
        int armor = Math.Min(e.Placement.Armor, pawn.ArmorValue + options.ArmorRegenerationPerSecond);
        if (health == pawn.Health && armor == pawn.ArmorValue) return;
        if (health != pawn.Health) pawn.Health = health;
        if (armor != pawn.ArmorValue)
        {
            pawn.ArmorValue = armor;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
        }
        if (live.Player.PawnArmor != armor)
        {
            live.Player.PawnArmor = armor;
            Utilities.SetStateChanged(live.Player, "CCSPlayerController", "m_iPawnArmor");
        }
        live.Stats?.Observe(pawn.Health, pawn.ArmorValue);
    }
    private void InitializePawn(PracticeBotRoster.Entry e, LiveBot live)
    {
        if (live.Player.PlayerPawn.Value is not { IsValid:true, Health: > 0 } pawn || !live.Player.PawnIsAlive) return;
        if (live.Pawn == pawn.EntityHandle.Raw) return;
        if (live.Pawn != 0) { Finish(e,live,"意外复活已清理",true); return; }
        if (live.Player.TeamNum != e.Placement.Team) live.Player.SwitchTeam((CsTeam)e.Placement.Team);
        live.Pawn = pawn.EntityHandle.Raw;
        if (pawn.Bot is { } namedBot) { namedBot.Name=e.Name;_controlled[namedBot.Handle]=live; }
        live.Player.PlayerName=e.Name; Utilities.SetStateChanged(live.Player,"CBasePlayerController","m_iszPlayerName");
        if (live.Player.InGameMoneyServices is { } money)
        { money.Account = 0; Utilities.SetStateChanged(live.Player,"CCSPlayerController","m_pInGameMoneyServices"); }
        pawn.ItemServices?.As<CCSPlayer_ItemServices>().RemoveWeapons();
        // Practice targets are an isolated exception, not a change to real zombie loadouts.
        // Giving just one rifle lets the engine equip/deploy it through the normal weapon path.
        live.Player.GiveNamedItem(e.Placement.Team == 2 ? "weapon_ak47" : "weapon_m4a1");
        pawn.Health = pawn.MaxHealth = e.Placement.Health; pawn.ArmorValue = e.Placement.Armor;
        pawn.TakesDamage = true;
        if (pawn.ItemServices?.As<CCSPlayer_ItemServices>() is { } item) item.HasHelmet = e.Placement.Team == 2;
        live.Player.PawnHasHelmet = e.Placement.Team == 2; live.Player.PawnArmor = e.Placement.Armor;
        // m_iHealth/m_iMaxHealth are not networked; SetStateChanged on them only logs CS# warnings.
        Utilities.SetStateChanged(pawn,"CCSPlayerPawn","m_ArmorValue"); Utilities.SetStateChanged(pawn,"CBasePlayerPawn","m_pItemServices");
        Utilities.SetStateChanged(live.Player,"CCSPlayerController","m_bPawnHasHelmet"); Utilities.SetStateChanged(live.Player,"CCSPlayerController","m_iPawnArmor");
        Teleport(pawn,e.Placement); HoldPosition(pawn);
        live.Stats = new(pawn.Health,pawn.ArmorValue,Server.CurrentTime);
        live.LastDamageAt = Server.CurrentTime; live.NextRegenAt = 0;
        plugin.Reply(Resolve(e.Owner),$"Bot #{e.Id} 已放置；HP {pawn.Health} / 甲 {pawn.ArmorValue}；受伤后停火 {plugin.Config.PublicGameplay.RegenerationDelaySeconds:0.#}s 开始回血回甲。");
    }
    private static void HoldPosition(CCSPlayerPawn pawn)
    {
        // Both AllowActive gates must stay enabled for command simulation and gravity.
        // FL_FROZEN is also intentionally avoided: the engine rejects damage to frozen players.
        // Clear only voluntary input here AND after the bot AI writes its next decision.
        pawn.BotAllowActive = true;
        if (pawn.Bot is { } bot)
        {
            bot.AllowActive = true; bot.IsSleeping = false;
            ClearInput(bot);
        }
    }
    private static void ClearInput(CCSBot bot)
    {
            bot.IsAttacking = false;
            bot.ButtonFlags = 0;
            bot.ForwardSpeed = bot.LeftSpeed = bot.VerticalSpeed = 0;
    }
    private HookResult BotDecisionPost(DynamicHook hook)
    {
        if(_disposed||!_active||_controlled.Count==0)return HookResult.Continue;
        IntPtr pointer=hook.GetParam<IntPtr>(0);
        if(!_controlled.TryGetValue(pointer,out var live)||live.Finished||!live.Player.IsValid)return HookResult.Continue;
        if(live.Player.PlayerPawn.Value is not {IsValid:true,Health:>0} pawn || pawn.EntityHandle.Raw!=live.Pawn || pawn.Bot?.Handle!=pointer)
            return HookResult.Continue;
        ClearInput(pawn.Bot!);_inputCorrections++;
        return HookResult.Continue;
    }
    private void Tick()
    {
        if (_disposed || !_active) return;
        float now = Server.CurrentTime;
        var players = Utilities.GetPlayers();
        foreach (var p in players.Where(p => p.IsValid && p.IsBot && IsPracticeBot(p)).ToArray())
        {
            try
            {
                _pendingSlots.TryGetValue(p.Slot,out var pending);
                if (pending.Entry != null && Server.TickCount < pending.ReadyTick) continue;
                var e = _roster.ByBot(Identity(p)) ?? (pending.Connection==_connections.GetValueOrDefault(p.Slot)?pending.Entry:null) ?? _roster.ByName(p.PlayerName);
                if (e == null || e.Retired || Resolve(e.Owner) == null)
                { _pendingSlots.Remove(p.Slot); DisconnectBot(p,Identity(p)); MaintainQuota(); continue; }
                if (e.Bot == null)
                {
                    if (Server.TickCount < pending.ReadyTick) continue;
                    _roster.Bind(e,Identity(p)); _pendingSlots.Remove(p.Slot);
                    Note($"bound #{e.Id}: {Identity(p)} tick={Server.TickCount}");
                }
                if (e.Bot != Identity(p)) continue;
                if (!_live.TryGetValue(e.Id,out var live)) _live[e.Id] = live = new(p);
                if (live.Finished) { Remove(e,"已结算",false); continue; }
                if (live.Pawn == 0 && !p.PawnIsAlive && now >= live.RespawnAt)
                {
                    live.RespawnAt = now + 1;
                    if (p.TeamNum != e.Placement.Team) p.SwitchTeam((CsTeam)e.Placement.Team);
                    p.Respawn();
                }
                InitializePawn(e,live);
                if (live.Finished) continue;
                if (p.PlayerPawn.Value is { IsValid:true } pawn)
                {
                    if (live.Pawn != 0 && (!p.PawnIsAlive || pawn.Health <= 0)) { Finish(e,live,"死亡",true); continue; }
                    Regenerate(e,live,pawn,now);
                    HoldPosition(pawn); live.Stats?.Sample(now,pawn.VelocityModifier);
                }
            }
            catch (Exception error)
            {
                if (_errors.Add(error.Message)) plugin.Logger.LogError(error,"PRACTICE_BOT update failed");
                var e = _roster.ByBot(Identity(p)) ?? _roster.ByName(p.PlayerName);
                if (e != null) Remove(e,"初始化/更新失败",true);
            }
        }
        if (now < _nextMaintenance) return;
        _nextMaintenance = now + .25f;
        foreach (var e in _roster.Entries.ToArray())
        {
            if (Resolve(e.Owner) == null) { Remove(e,"拥有者离线",false); continue; }
            if (e.Bot is { } id && Resolve(id) == null) { var observed=Utilities.GetPlayerFromSlot(id.Slot); Note($"lost #{e.Id}: expected={id}; observed={(observed is {IsValid:true}?Identity(observed):null)} connected={observed?.Connected}"); Remove(e,"Bot 断开",true); continue; }
            if ((!_live.TryGetValue(e.Id,out var live) || live.Pawn == 0) && now - e.RequestedAt > 10)
                Remove(e,"创建超时（检查空闲槽位/地图）",true);
        }
        MaintainQuota();
        _roster.PruneRetired(now - 30);
    }
    private HookResult Hurt(EventPlayerHurt ev, GameEventInfo info)
    {
        if (ev.Userid is not { IsValid:true } p || _roster.ByBot(Identity(p)) is not { Retired:false } e || !_live.TryGetValue(e.Id,out var live) || live.Finished || live.Executing) return HookResult.Continue;
        var pawn = p.PlayerPawn.Value;
        live.LastDamageAt = Server.CurrentTime;
        live.Stats?.Hurt(pawn?.Health ?? ev.Health,pawn?.ArmorValue ?? ev.Armor,ev.DmgHealth,ev.Hitgroup == 1,Server.CurrentTime,pawn?.VelocityModifier ?? 1);
        live.LastAttacker = ev.Attacker is { IsValid:true } attacker ? ZePanelLogic.Text(attacker.PlayerName,40) : "地图/环境";
        return HookResult.Continue;
    }
    private HookResult Death(EventPlayerDeath ev, GameEventInfo info)
    {
        if (ev.Userid is { IsValid:true } p && _roster.ByBot(Identity(p)) is { Retired:false } e && _live.TryGetValue(e.Id,out var live))
            Finish(e,live,live.Executing ? "处死" : "击杀",true);
        return HookResult.Continue;
    }
    private void Finish(PracticeBotRoster.Entry e, LiveBot live, string reason, bool report)
    {
        if (live.Finished) return;
        live.Stats?.Sample(Server.CurrentTime,1); live.Finished = true;
        if (report) Report(e,live,reason);
        // Retire before the next frame. Other gameplay callbacks still recognize this identity/name.
        _roster.Retire(e,Server.CurrentTime);
        Server.NextFrame(() => { if (!_disposed) Remove(e,reason,false); });
    }
    private void Report(PracticeBotRoster.Entry e, LiveBot? live, string reason)
    {
        var owner = Resolve(e.Owner); if (owner == null) return;
        var s = live?.Stats;
        if (s == null) { plugin.Reply(owner,$"Bot #{e.Id}：{reason}。"); return; }
        plugin.Reply(owner,$"Bot #{e.Id} [{reason}] 初始HP {s.InitialHealth}，有效扣血 {s.EffectiveHealthLoss}，累计伤害 {s.EventDamage}，护甲损失 {s.ArmorLoss}；命中 {s.Hits} / 头部 {s.Headshots}。");
        plugin.Reply(owner,string.Create(CultureInfo.InvariantCulture,$"受击减速累计 {s.SlowSeconds:0.00}s，最大减速 {(1-s.LowestVelocityModifier)*100:0.#}%（速度倍率）；存活 {Math.Max(0,Server.CurrentTime-s.StartedAt):0.0}s，最后伤害：{live!.LastAttacker}。") + (live.Executing ? "处死清血不计射击伤害。" : ""));
    }
    private void Remove(PracticeBotRoster.Entry e, string reason, bool report)
    {
        plugin.Logger.LogInformation("PRACTICE_BOT_REMOVE id={Id} owner={Owner} bot={Bot} reason={Reason}",e.Id,e.Owner,e.Bot,reason);
        _live.Remove(e.Id,out var live);
        if(live!=null)foreach(var key in _controlled.Where(pair=>ReferenceEquals(pair.Value,live)).Select(pair=>pair.Key).ToArray())_controlled.Remove(key);
        if (report && live?.Finished != true) Report(e,live,reason);
        _roster.Retire(e,Server.CurrentTime);
        if (e.Bot is { } id && Resolve(id) is { IsBot:true } p) DisconnectBot(p,id);
        // EndSpawn has already created a controller, but its identity is not bound
        // until two ticks later. Clean this window synchronously as well: unload
        // and map-end remove our Tick listener before a deferred cleanup could run.
        foreach (var pending in _pendingSlots.Where(pair => ReferenceEquals(pair.Value.Entry,e)).ToArray())
        {
            _pendingSlots.Remove(pending.Key);
            if (_connections.GetValueOrDefault(pending.Key) == pending.Value.Connection &&
                Utilities.GetPlayerFromSlot(pending.Key) is { IsValid:true, IsBot:true } created)
                DisconnectBot(created,Identity(created));
        }
        // Free the number so the next placement reuses it (1,2,3...) instead of growing forever.
        _roster.Drop(e);
        MaintainQuota();
    }
    private void DisconnectBot(CCSPlayerController p, PracticeIdentity id)
    {
        if (Matches(p,id) && p.IsBot) p.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
    }
    private int ClearOwner(PracticeIdentity owner, string reason, bool report)
    { var mine = _roster.Owned(owner).ToArray(); foreach (var e in mine) Remove(e,reason,report); return mine.Length; }
    private void Disconnect(int slot)
    {
        Note($"disconnect slot={slot} epoch={_connections.GetValueOrDefault(slot)}");
        // OnClientDisconnect may arrive after the controller is invalid; use captured owner identities.
        foreach (var owner in _roster.Entries.Where(e => e.Owner.Slot == slot).Select(e=>e.Owner).Distinct().ToArray()) ClearOwner(owner,"拥有者离线",false);
        foreach (var id in _health.Keys.Where(id => id.Slot == slot).ToArray()) _health.Remove(id);
        foreach (var id in _nextAdd.Keys.Where(id => id.Slot == slot).ToArray()) _nextAdd.Remove(id);
        _pendingSlots.Remove(slot);
        _connections[slot]=++_connectionSequence;
    }
    private void Connected(int slot) { _connections[slot]=++_connectionSequence; Note($"connected slot={slot} epoch={_connections[slot]}"); }
    private void LeaseQuota()
    {
        if (_quotaLeased) return;
        _originalMode = ConVar.Find("bot_quota_mode")?.StringValue ?? "normal";
        _originalQuota = ConVar.Find("bot_quota")?.GetPrimitiveValue<int>() ?? 0;
        _lastQuota = _originalQuota; _quotaLeased = true;
        ZePracticePlugin.SetCvar("bot_quota_mode","normal");
    }
    private void MaintainQuota()
    {
        if (!_quotaLeased) return;
        int other = Utilities.GetPlayers().Count(p=>p.IsBot && !IsPracticeBot(p));
        int wanted = other + _roster.Entries.Count(e=>e.Bot != null);
        // A bot_add command already adjusts quota while creation is pending. Do not create unnamed replacements.
        if (_pendingSlots.Count != 0 || _roster.Entries.Any(e=>e.Bot == null)) return;
        if (!_roster.Entries.Any())
        {
            ZePracticePlugin.SetCvar("bot_quota",Math.Max(_originalQuota,other == 0 ? 0 : _originalMode == "fill" ? other + Utilities.GetPlayers().Count(p=>!p.IsBot && !p.IsHLTV) : other).ToString(CultureInfo.InvariantCulture));
            if (ConVar.Find("bot_quota_mode")?.StringValue == "normal") ZePracticePlugin.SetCvar("bot_quota_mode",_originalMode);
            _quotaLeased = false; return;
        }
        _lastQuota = wanted; ZePracticePlugin.SetCvar("bot_quota",wanted.ToString(CultureInfo.InvariantCulture));
    }
    public void ReapplyPracticeQuota()
    {
        if (_quotaLeased || _roster.Entries.Any()) { LeaseQuota(); MaintainQuota(); }
        else ZePracticePlugin.SetCvar("bot_quota", "0");
    }
    public string Diagnostics(CCSPlayerController? p = null) => JsonSerializer.Serialize(new
    {
        active=_active, inputHookReady=_botUpdate!=null, inputCorrections=_inputCorrections, limit=2, ownerLimit="engine-slots-only", maximumHealth=Options.MaximumHealth, quotaLeased=_quotaLeased,
        lifecycle=p==null ? _lifecycle.ToArray() : [],
        bots=_roster.Entries.Where(e=>p==null || e.Owner==Identity(p)).Select(e=> new { e.Id,e.Name,owner=e.Owner,bot=e.Bot,e.Placement,
            initialized=_live.TryGetValue(e.Id,out var l) && l.Pawn!=0, statistics=_live.GetValueOrDefault(e.Id)?.Stats }).ToArray(), errors=_errors.ToArray()
    });
    private void Status(CCSPlayerController? p, CommandInfo c)
    {
        if (p == null) { plugin.Reply(p,Diagnostics()); return; }
        if (!InGame(p,false)) return;
        var mine=_roster.Owned(Identity(p)).ToArray();
        plugin.Reply(p,$"个人 Bot {mine.Length}/{(plugin.Permissions.IsServerOwner(p)?"服主不限个人数量":"2，双方合计")}；下次HP：{PresetLabel(p)}。!ze_bot_add t/ct/ally/enemy / !ze_bot_move 打开编号列表（all=全部） / !ze_bot_kill [编号] / !ze_bot_clear");
        foreach(var e in mine) { var s=_live.GetValueOrDefault(e.Id)?.Stats; plugin.Reply(p,$"#{e.Id} {(e.Placement.Team==2?"僵尸T":"人类CT")} HP {s?.RemainingHealth ?? e.Placement.Health}，扣血 {s?.EffectiveHealthLoss ?? 0}，减速 {s?.SlowSeconds ?? 0:0.00}s {(e.Bot==null?"待创建":"")}"); }
    }
    public void OnMapStart() { _active=true; _nextMaintenance=0; _errors.Clear(); }
    public void Reset(string reason)
    {
        foreach(var e in _roster.Entries.ToArray()) Remove(e,reason,false);
        foreach(var e in _live.ToArray())
        {
            var p=e.Value.Player;
            if(p.IsValid && p.IsBot && _roster.ByBot(Identity(p)) is { } entry) Remove(entry,reason,false);
        }
    }
    public void OnMapEnd() { Reset("换图"); _active=false; _menuGeneration++; _health.Clear(); _nextAdd.Clear(); _pendingSlots.Clear(); _roster.Clear(); _live.Clear(); _controlled.Clear(); }
    public void Dispose()
    {
        if (_disposed) return;
        Reset("卸载"); _disposed=true; _active=false;
        _botUpdate?.Unhook(BotDecisionPost,HookMode.Post);_botUpdate=null;_controlled.Clear();
        plugin.RemoveListener<Listeners.OnTick>(Tick); plugin.RemoveListener<Listeners.OnClientDisconnect>(Disconnect);
        plugin.RemoveListener<Listeners.OnClientPutInServer>(Connected);
        plugin.DeregisterEventHandler<EventPlayerHurt>(Hurt,HookMode.Post); plugin.DeregisterEventHandler<EventPlayerDeath>(Death,HookMode.Pre);
        foreach(var c in _commands) plugin.RemoveZeCommand(c.Key,c.Value);
        plugin.RemoveCommand("ze_practice_bot_begin",BeginSpawn); plugin.RemoveCommand("ze_practice_bot_end",EndSpawn);
    }
}
