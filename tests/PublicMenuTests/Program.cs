using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using ZePractice;

int passed=0;
void Check(bool ok,string name) { if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name); }
var p=new CCSPlayerController { Slot=1, UserId=10 };
var other=new CCSPlayerController { Slot=2, UserId=11 };
Utilities.Players.AddRange([p,other]);
var plugin=new ZePracticePlugin(); var service=new PublicGameplayService(plugin);
ChatMenuOption Option(string text)=>plugin.Current!.MenuOptions.Single(x=>x.Text.Contains(text,StringComparison.Ordinal));
void Click(string text) { var option=Option(text);option.OnSelect(p,option); }
service.Open(p);
Check(plugin.Current!.MenuOptions.Count==45,"45 distinct public/ordinary actions including real script diagnostics");
Check(!plugin.Current.MenuOptions.Any(x=>x.Text.Contains("管理员功能")),"ordinary player never sees management entry");
Check(plugin.Current.MenuOptions.Take(6).Select(x=>x.Text.Split(' ')[0]).SequenceEqual(new[]{"!ze_noclip","!thirdperson","!leader","!ze_ammo","!nospread","!ze_bhop"}),"first page has six direct toggles including thirdperson");
service.Open(p);Click("!ze_diag");Check(plugin.Commands.Last()=="css_ze_diag","diagnostics public toggle");plugin.Commands.Clear();
service.Open(p);Click("!ze_script_status");Check(plugin.Commands.Last()=="css_ze_script_status","real script status directly accessible from public menu");plugin.Commands.Clear();
var stale=Option("!ze_ammo");stale.OnSelect(other,stale);Check(plugin.Commands.Count==0,"menu cannot be clicked by different slot");
p.UserId=99;stale.OnSelect(p,stale);Check(plugin.Commands.Count==0,"slot reuse cannot invoke old action");p.UserId=10;
plugin.Permissions.Deny.Add("css_ze_ammo");stale.OnSelect(p,stale);Check(plugin.Commands.Count==0,"exact permission rechecked on click");plugin.Permissions.Deny.Clear();
Click("!thirdperson");Check(plugin.Commands.Last()=="css_thirdperson","thirdperson uses original player's registered command");
service.Open(p);Click("!ze_speed");Check(plugin.Commands.Last()=="css_ze_speed","movement HUD toggle available in public menu");
service.Open(p);int beforeTeamMenu=plugin.Commands.Count;Click("!ze_bot_add");
Check(plugin.Commands.Count==beforeTeamMenu&&plugin.Current!.Title.Contains("选择阵营"),"Bot add opens team choices without spawning immediately");
var teamMenu=plugin.Current;
foreach(string choice in new[]{"t","ct","ally","enemy"})
{
    Click("!ze_bot_add "+choice+" →");
    Check(plugin.Commands.Last()=="css_ze_bot_add "+choice&&ReferenceEquals(teamMenu,plugin.Current),"public Bot choice keeps submenu: "+choice);
}
var botChoice=Option("添加人类 CT");int beforeBotChoice=plugin.Commands.Count;
botChoice.OnSelect(other,botChoice);Check(plugin.Commands.Count==beforeBotChoice,"another player cannot use Bot team submenu");
plugin.Permissions.Deny.Add("css_ze_bot_add");botChoice.OnSelect(p,botChoice);
Check(plugin.Commands.Count==beforeBotChoice,"Bot team selection rechecks existing public command permission");plugin.Permissions.Deny.Clear();
Click("返回公开指令");Check(plugin.Current!.Title.Contains("公开指令"),"Bot team submenu has an explicit return");
service.Open(p);Click("!ze_bot_hp");Click("自定义输入");service.Chat(p,"23456");Check(plugin.Commands.Last()=="css_ze_bot_hp 23456","Bot HP menu passes validated input");
service.Open(p);Click("!ze_hp");Click("自定义输入");
Check(service.Chat(p,"40000")==HookResult.Stop&&!plugin.Commands.Any(c=>c=="css_ze_hp 40000"),"above health limit is rejected");
Check(service.Chat(p,"hi")==HookResult.Continue,"ordinary chat not swallowed");
Check(service.Chat(p,"!leader")==HookResult.Continue,"explicit command not swallowed");
Check(service.Chat(p,"100;quit")==HookResult.Stop&&!plugin.Commands.Any(c=>c.Contains(";")),"numeric input rejects command chaining");
service.Chat(p,"34567");Check(plugin.Commands.Last()=="css_ze_hp 34567","custom health executes validated value");
service.Open(p);Click("!ze_music");Click("自定义输入");service.Chat(p,"0.35");Check(plugin.Commands.Last()=="css_ze_music 0.35","fractional volume entry works");
service.Open(p);Click("host_workshop_map");Click("自定义输入");service.Chat(p,"+3817448617");Check(plugin.Commands.Last()=="css_ze_workshop 3817448617","workshop input nominates only, never server host_workshop_map");
service.Open(p);Click("!ze_hide");Click("自定义输入");int before=plugin.Commands.Count;Server.CurrentTime=61;Check(service.Chat(p,"100")==HookResult.Continue&&plugin.Commands.Count==before,"chat capture expires");Server.CurrentTime=0;
service.Open(p);Click("!ze_armor");var inputMenu=plugin.Current;Click("自定义输入");
Check(ReferenceEquals(inputMenu,plugin.Current)&&!plugin.Commands.Contains("css_ze_ui_close"),"custom input keeps the existing window open");
service.Chat(p,"取消");Check(ReferenceEquals(inputMenu,plugin.Current),"cancel input keeps current submenu without reopening home");
service.Open(p);Click("!ze_restart");Check(plugin.Current!.Title.Contains("重启全服")&&!plugin.Commands.Contains("css_ze_restart"),"public restart requires menu confirmation");Click("确认执行");Check(plugin.Commands.Last()=="css_ze_restart","ordinary player confirmation does not require admin");
service.Open(p);Click("!ze_join");Click("僵尸 T");Check(plugin.Commands.Last()=="css_ze_join t","own team menu preserves public join command");
p.TeamNum=2;service.Open(p);Click("!ze_armor");Check(plugin.Messages.Last().Contains("服务器管理"),"zombie armor remains server controlled");p.TeamNum=3;
plugin.Permissions.Admin=true;service.Open(p);Check(Option("管理员功能")!=null,"admin keeps separate management entry");
service.Open(p);stale=Option("!nospread");before=plugin.Commands.Count;service.ChangeMap();stale.OnSelect(p,stale);Check(plugin.Commands.Count==before,"map generation invalidates menu callback");
PersistenceCases.Run(Check);
BotMoveMenuCases.Run(Check);
Console.WriteLine($"PASS {passed} public-menu tests (real menu/radio/Panorama sources; native engine stubs)");
