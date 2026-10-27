using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;

namespace ZePractice;

/// <summary>Refresh labels in the existing menu; never open a new session to update a toggle.</summary>
public sealed class PersistentChatMenu : ChatMenu
{
    private readonly List<(ChatMenuOption Option, Func<string> Text)> _labels = new();
    public PersistentChatMenu(string title) : base(title)
    {
        ExitButton = true;
        PostSelectAction = PostSelectAction.Nothing;
    }
    public void BindLabel(ChatMenuOption option, Func<string> text) => _labels.Add((option, text));
    public bool RefreshLabels()
    {
        bool changed = false;
        foreach (var (option, text) in _labels)
        {
            string value = text();
            if (option.Text == value) continue;
            option.Text = value;
            changed = true;
        }
        return changed;
    }
    public void RefreshChatAfterCommand(CCSPlayerController player)
    {
        // Only the fallback chat renderer needs Display(). Native panels refresh their existing layout.
        if (MenuManager.GetActiveMenu(player) is not BaseMenuInstance current || !ReferenceEquals(current.Menu, this)) return;
        int? userId = player.UserId;
        uint controller = player.EntityHandle.Raw;
        Server.NextFrame(() =>
        {
            if (!player.IsValid || player.UserId != userId || player.EntityHandle.Raw != controller ||
                !ReferenceEquals(MenuManager.GetActiveMenu(player), current)) return;
            if (RefreshLabels()) current.Display(); // Keeps the same page; close/replace always wins.
        });
    }
}
