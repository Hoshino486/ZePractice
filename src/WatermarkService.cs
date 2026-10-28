using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace ZePractice;

/// <summary>
/// 插件署名：进图与出生时在屏幕中央提示「跑图插件制作：赛博刹那」。
/// 只要服务器加载了本插件就会显示，不依赖玩家主动敲命令。
/// </summary>
public sealed class WatermarkService
{
    private readonly ZePracticePlugin _plugin;
    private readonly HashSet<int> _announced = new();

    public WatermarkService(ZePracticePlugin plugin) => _plugin = plugin;

    private WatermarkOptions Options => _plugin.Config.Watermark;

    public void Initialize() { }

    public void OnMapStart()
    {
        _announced.Clear();
        var options = Options;
        if (!options.Enabled) return;
        // 等地图真正进入 active 再显示，避免开局被引擎的居中提示盖掉
        _plugin.AddTimer(Math.Clamp(options.MapStartDelaySeconds, 0f, 30f), () =>
        {
            if (!Options.Enabled) return;
            foreach (var player in Utilities.GetPlayers().Where(ZePracticePlugin.IsRealPlayer))
                Announce(player, options.MapStartSeconds);
        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
    }

    public void OnMapEnd() => _announced.Clear();

    public void OnPlayerSpawn(CCSPlayerController? player)
    {
        var options = Options;
        if (!options.Enabled || player is null || !ZePracticePlugin.IsRealPlayer(player)) return;
        if (!options.OnEverySpawn && !_announced.Add(player.Slot)) return;
        Announce(player, options.SpawnSeconds);
    }

    /// <summary>管理员随时手动再放一次，方便确认署名还在。</summary>
    public void Replay(CCSPlayerController? player)
    {
        var options = Options;
        var targets = player is null || !ZePracticePlugin.IsRealPlayer(player)
            ? Utilities.GetPlayers().Where(ZePracticePlugin.IsRealPlayer).ToList()
            : [player];
        foreach (var target in targets) Announce(target, options.MapStartSeconds);
    }

    private void Announce(CCSPlayerController player, float seconds)
    {
        var options = Options;
        var author = string.IsNullOrWhiteSpace(options.Author) ? "赛博刹那" : options.Author.Trim();
        var headline = string.IsNullOrWhiteSpace(options.Headline) ? "跑图插件制作" : options.Headline.Trim();

        var html =
            $"<font class='fontSize-m' color='#FFFFFF'>{Escape(headline)}</font><br>" +
            $"<font class='fontSize-xl' color='#FFD700'>{Escape(author)}</font><br>" +
            $"<font class='fontSize-s' color='#7FD4FF'>ZePractice v{_plugin.ModuleVersion}</font>";

        // 大号居中提示：这一层是最醒目的，任何玩家进服都会看到
        player.PrintToCenterHtml(html, (int)Math.Clamp(seconds, 1f, 20f));

        if (options.ShowInChat)
            player.PrintToChat($" \x04[{Escape(headline)}]\x01 {Escape(author)} · ZePractice v{_plugin.ModuleVersion}");
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");
}
