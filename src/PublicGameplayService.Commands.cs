using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;

namespace ZePractice;

public sealed partial class PublicGameplayService
{
    private void Register(string name, string description, CommandInfo.CommandCallback callback)
    { _commands.Add(name, callback); _plugin.AddZeCommand(name, description, callback); }

    private void RegisterCommands()
    {
        Register("css_ze_public", "全员生命、弹药、音乐、隐藏与连跳菜单", (player, _) => ShowMenu(player));
        Register("css_ze_hp", "所有人：设置自己的生命 <数值>；T 同时设置个人回血上限", HealthCommand);
        Register("css_ze_armor", "CT：设置自己的护甲 <数值>；T 护甲固定由服务器管理", ArmorCommand);
        Register("css_ze_ammo", "个人免换弹 <on|off>；备弹始终维持 99", (player, command) => ToggleCommand(player, command, false));
        Register("css_nospread", "人类个人枪械无扩散 <on|off>；无需权限，保留后坐力", NoSpreadCommand);
        Register("css_ze_nospread", "人类个人枪械无扩散 <on|off>；无需权限，保留后坐力", NoSpreadCommand);
        Register("css_ze_nospread_verify", "服务器控制台：只读检查个人无扩散原生接口", VerifyNoSpread);
        Register("css_ze_bhop", "个人按住空格连跳 <on|off>；第 5 跳强制降速", (player, command) => ToggleCommand(player, command, true));
        Register("css_ze_music", "个人地图声源音量 <0..1>", MusicCommand);
        Register("css_ze_hide", "仅隐藏范围内队友模型，保留姓名 <0..999>", HideCommand);
        Register("css_ze_zombiegear", "所有人：允许自己的僵尸拾取枪械类神器并购买枪械 <on|off>", ZombieGearCommand);
        Register("css_ze_join", "所有人：切换自己的阵营 <t|ct|spec>", (player, command) => JoinTeam(player, command.ArgCount == 2 ? command.GetArg(1) : ""));
        Register("css_ze_roundtime", "所有人：设置当前地图回合分钟数，立即重设当前倒计时", RoundTimeCommand);
        Register("css_ze_endround", "所有人：结束当前回合并开启下一回合", (player, _) => _plugin.EndManualRound(player));
        Register("css_ze_workshop", "所有人：添加换图候选 <地图ID>，不立即换图", WorkshopCommand);
        Register("css_host_workshop_map", "所有人：添加换图候选 <地图ID>，不立即换图", WorkshopCommand);
        Register("css_ze_public_status", "公共玩法状态（玩家只查看自己，控制台查看全部）", (player, _) => _plugin.Reply(player, Diagnostics(player)));
    }

    private bool NeedAlive(CCSPlayerController? player)
    {
        if (!_active || !Alive(player)) { _plugin.Reply(player, "请先进入 T/CT 阵营并保持存活。"); return false; }
        InitializePawn(player!, State(player!), false);
        return player!.TeamNum is 2 or 3;
    }

    private void HealthCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!NeedAlive(player)) return;
        int limit = player!.TeamNum == 2 ? Options.ZombieHealthLimit : Options.HumanHealthLimit;
        if (command.ArgCount != 2 || !PublicGameplayLogic.TryInteger(command.GetArg(1), 1, limit, out int health))
        { _plugin.Reply(player, $"用法：!ze_hp <1..{limit}>，超上限、负数和非整数均不执行。"); return; }
        var state = State(player); var pawn = player.PlayerPawn.Value!;
        state.MaximumHealth = health;
        SetHealth(pawn, health, health);
        state.Regeneration.Reset(health, pawn.ArmorValue, Server.CurrentTime, Options.RegenerationDelaySeconds);
        _plugin.Reply(player, $"当前生命和个人生命上限：{health}；服务器上限：{limit}。" + (player.TeamNum == 2 ? "新生命/换队后恢复僵尸默认值。" : "人类不会自动回血或回甲。"));
    }

    private void ArmorCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!NeedAlive(player)) return;
        if (player!.TeamNum == 2) { _plugin.Reply(player, $"僵尸护甲由服务器管理，上限 {Options.ZombieArmor}，受伤后自动恢复。"); return; }
        if (command.ArgCount != 2 || !PublicGameplayLogic.TryInteger(command.GetArg(1), 0, Options.HumanArmorLimit, out int armor))
        { _plugin.Reply(player, $"用法：!ze_armor <0..{Options.HumanArmorLimit}>。"); return; }
        SetArmor(player.PlayerPawn.Value!, armor);
        _plugin.Reply(player, $"当前护甲：{armor}；服务器上限：{Options.HumanArmorLimit}。人类不会自动回甲。");
    }

    public void ToggleNoclip(CCSPlayerController? player)
    {
        if (!NeedAlive(player)) return;
        if (_moveType == null || _groundEntity == null) { _plugin.Reply(player, "移动接口未就绪，请检查服务端 PUBLIC_GAMEPLAY 日志。"); return; }
        var state = State(player!); var pawn = player!.PlayerPawn.Value!;
        if (!state.Noclip)
        {
            state.Movement.Enable((byte)pawn.MoveType); state.MovementPawn = pawn.Handle;
            _movementOverrides[pawn.Handle] = state;
            _groundEntity.Invoke(pawn.Handle, IntPtr.Zero, IntPtr.Zero);
            SetNativeMoveType(pawn, MoveType_t.MOVETYPE_NOCLIP);
        }
        else ReleaseMovement(pawn, state);
        state.Jumps.Reset();
        _plugin.Reply(player, "个人穿墙：" + (state.Noclip ? "开启" : "关闭") + "。此命令对所有玩家开放。");
    }

    private void ZombieGearCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!Player(player)) { _plugin.Reply(player, "该指令只对游戏内真人玩家生效。"); return; }
        var key = GearKey(player!);
        bool current = _zombieGear.Contains(key), enabled = !current;
        if (command.ArgCount > 2 ||
            command.ArgCount == 2 && !PublicGameplayLogic.TryToggle(command.GetArg(1), current, out enabled))
        { _plugin.Reply(player, "用法：!ze_zombiegear [on|off]；省略参数切换开关。"); return; }
        if (enabled) _zombieGear.Add(key); else _zombieGear.Remove(key);
        _plugin.Reply(player, enabled
            ? "已开启：你的僵尸现在可以拾取枪械类神器、购买枪械与补给；换图后保持，直到你关闭。"
            : "已关闭：僵尸恢复只能使用刀，不能拾取神器或购买枪械。");
    }

    private void ToggleCommand(CCSPlayerController? player, CommandInfo command, bool bunnyHop)    {
        if (!Player(player)) { _plugin.Reply(player, "需要游戏内玩家。"); return; }
        var state = State(player!);
        bool current = bunnyHop ? state.BunnyHop : state.InfiniteMagazine;
        if (command.ArgCount > 2 || !PublicGameplayLogic.TryToggle(command.ArgCount == 2 ? command.GetArg(1) : "", current, out bool enabled))
        { _plugin.Reply(player, "参数只接受 on/off 或 1/0；省略参数切换开关。"); return; }
        if (bunnyHop) { state.BunnyHop = enabled; state.Jumps.Reset(); }
        else state.InfiniteMagazine = enabled;
        _plugin.Reply(player, (bunnyHop ? "按住空格连跳" : "免换弹") + "：" + (enabled ? "开启" : "关闭") +
            (bunnyHop ? "；连续第 5 跳降至默认地速，随后重新计数。" + (state.Noclip && enabled ? "当前穿墙开启，关闭穿墙落地后连跳生效。" : "") : $"；备弹始终维持 {Options.ReserveAmmo}，关闭后正常换弹。"));
    }

    private void MusicCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!Player(player)) return;
        if (command.ArgCount != 2 || !PublicGameplayLogic.TryVolume(command.GetArg(1), out float volume))
        { _plugin.Reply(player, "用法：!ze_music <0..1>，0 静音，1 原始音量，例如 0.3。"); return; }
        State(player!).MusicVolume = volume;
        ApplyMusicVolume(player!);
        _plugin.Reply(player, "个人地图声源音量：" + volume.ToString("0.##", CultureInfo.InvariantCulture) + "；不调整枪声和玩家语音。");
    }

    private void HideCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!Player(player)) return;
        if (command.ArgCount != 2 || !PublicGameplayLogic.TryInteger(command.GetArg(1), 0, 999, out int range))
        { _plugin.Reply(player, "用法：!ze_hide <0..999>；0 关闭，非零时仅隐藏该距离以内的队友模型，保留姓名。"); return; }
        State(player!).HideRange = range;
        _plugin.Reply(player, range == 0 ? "队友模型屏蔽已关闭。" : $"隐藏 {range} 单位以内的队友模型与手持武器，保留头顶姓名；范围外正常显示。");
    }

    public void JoinTeam(CCSPlayerController? player, string value)
    {
        if (!_active || !Player(player)) return;
        var team = value.ToLowerInvariant() switch { "t" or "2" => CsTeam.Terrorist, "ct" or "3" => CsTeam.CounterTerrorist, "spec" or "1" => CsTeam.Spectator, _ => CsTeam.None };
        if (team == CsTeam.None) { _plugin.Reply(player, "用法：!ze_join t / ct / spec；仅切换自己的阵营，不要求权限。"); return; }
        var state = State(player!);
        if (player!.TeamNum == (byte)team)
        {
            QueueTeamReset(player, state, (int)team);
            _plugin.Reply(player, "已重置当前阵营的角色，清除神器标签和实体名称后重新出生。");
            return;
        }
        QueueTeamReset(player, state, (int)team, true);
        _plugin.Reply(player, "已请求切换自己的阵营到 " + team + "，清除神器身份并重新出生；不会因此结束或重启回合。");
    }

    private void RoundTimeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount != 2 || !PublicGameplayLogic.TryInteger(command.GetArg(1), 1, Options.MaximumRoundMinutes, out int minutes))
        { _plugin.Reply(player, $"用法：!ze_roundtime <1..{Options.MaximumRoundMinutes}>，单位为分钟。"); return; }
        var proxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        var rules = proxy?.GameRules;
        if (rules == null) { _plugin.Reply(player, "地图回合尚未初始化。"); return; }
        _roundMinutes = minutes;
        rules.RoundTime = checked(minutes * 60); rules.RoundStartTime = Server.CurrentTime;
        Utilities.SetStateChanged(proxy!, "CCSGameRulesProxy", "m_pGameRules");
        _plugin.Reply(player, $"本地图回合时间设置为 {minutes} 分钟，当前倒计时已重设；手动重开保留此设置，换图恢复地图默认；到时不自动结束。");
    }

    public void OnRoundStart()
    {
        if (!_active || _disposed || _roundMinutes is not { } minutes) return;
        Later(() =>
        {
            var proxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
            if (proxy?.GameRules is not { } rules) return;
            rules.RoundTime = checked(minutes * 60);
            Utilities.SetStateChanged(proxy, "CCSGameRulesProxy", "m_pGameRules");
        });
    }

    private void WorkshopCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount != 2 || !PublicGameplayLogic.TryWorkshopId(command.GetArg(1), out string identifier))
        { _plugin.Reply(player, "用法：host_workshop_map 地图ID，或 !ze_workshop 地图ID；只接受一个数字 ID。"); return; }
        _plugin.NominateWorkshop(player, identifier);
    }

    private HookResult WorkshopChat(CCSPlayerController? player, CommandInfo command)
    {
        if (!Player(player) || command.ArgCount < 2 || !PublicGameplayLogic.IsWorkshopChat(command.GetArg(1))) return HookResult.Continue;
        if (!PublicGameplayLogic.TryWorkshopChat(command.GetArg(1), out string identifier))
        { _plugin.Reply(player, "用法：host_workshop_map 地图ID；不接受额外参数或多条命令。"); return HookResult.Stop; }
        _plugin.NominateWorkshop(player, identifier); return HookResult.Stop;
    }

}
