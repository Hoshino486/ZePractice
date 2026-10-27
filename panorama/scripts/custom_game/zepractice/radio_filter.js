(function () {
    var root = $.GetContextPanel();
    var previous = null;
    var previousInput = false;
    function marked(panel) {
        if (!panel || !panel.IsValid()) return false;
        if (panel.paneltype === "Label" && panel.text.indexOf("__ZE_DYNAMIC_INPUT__") !== -1) return true;
        var children = panel.Children();
        for (var index = 0; index < children.length; index++) {
            if (marked(children[index])) return true;
        }
        return false;
    }
    function update() {
        if (!root || !root.IsValid()) return;
        var menu = root.FindChildTraverse("RadioPanel");
        var background = root.FindChildTraverse("RadioPanelBG");
        var hidden = marked(menu);
        var inputVisible = root.BHasClass("ShowHudRadio");
        if (previousInput && !inputVisible) {
            $.Schedule(0.15, function () {
                if (root && root.IsValid() && !root.BHasClass("ShowHudRadio")) {
                    GameInterfaceAPI.ConsoleCommand("css_ze_ui_close");
                }
            });
        }
        previousInput = hidden && inputVisible;
        if (menu && background && hidden !== previous) {
            menu.style.opacity = hidden ? "0" : "1";
            background.style.opacity = hidden ? "0" : "1";
            previous = hidden;
        }
        $.Schedule(0.05, update);
    }
    update();
})();
