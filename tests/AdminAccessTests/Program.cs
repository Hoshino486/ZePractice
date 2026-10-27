using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using ZePractice;

var reportRoot = Path.Combine(@"E:\mapper\chana\临时文件夹\ze-ui-access-20261011\admin-access-tests", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(reportRoot);
var results = new List<object>(); int passed=0, failed=0;
void Test(string name, Action<F> body)
{
    using var f = new F(Path.Combine(reportRoot,"fixture-"+(passed+failed)));
    try { body(f); passed++;results.Add(new{name,passed=true});Console.WriteLine("PASS "+name); }
    catch(Exception e) { failed++;var error=e.GetBaseException().Message;results.Add(new{name,passed=false,error});Console.WriteLine("FAIL "+name+": "+error); }
}
void Check(bool condition,string message="assertion failed") {if(!condition)throw new Exception(message);}
Test("new practice and dedicated targeted-respawn commands registered",f=>Check(f.Plugin.Commands.ContainsKey("css_ze_practice")&&f.Plugin.Commands.ContainsKey("css_ze_clearcheckpoint")&&f.Plugin.Commands.ContainsKey("css_ze_respawn_other")));
Test("kick-only actor cannot slay through direct action",f=>{f.Grant("css_ze_kick");f.Call("Slay",f.A,f.B);Check(f.B.Suicides==0);f.Call("Kick",f.A,f.B);Check(f.B.Disconnects==1);});
Test("kick-only cannot change team",f=>{f.Grant("css_ze_kick");f.Call("SetTeam",f.A,f.B,CsTeam.Terrorist);Check(f.B.TeamChanges==0);});
Test("team-only direct action succeeds",f=>{f.Grant("css_ze_team");f.Call("SetTeam",f.A,f.B,CsTeam.Terrorist);Check(f.B.TeamNum==2);});
Test("restart is public without granting management",f=>{f.Grant();f.Call("RestartRound",f.A);Check(Server.Commands.SequenceEqual(new[]{"mp_restartgame 1"}));Check(!f.Plugin.Permissions.HasAdminAccess(f.A));});
Test("mode and config reload have separate gates",f=>{f.Grant("css_ze_reload");f.Call("SetMode",f.A,false);Check(f.Plugin.IsPractice);f.Call("Reload",f.A);Check(f.Plugin.Reloads==1);});
Test("mute does not permit ban or gag",f=>{f.Grant("css_ze_mute");f.Call("Punish",f.A,f.B,"ban",30,"fixture");f.Call("Punish",f.A,f.B,"gag",30,"fixture");Check(!File.Exists(f.Punishments));f.Call("Punish",f.A,f.B,"mute",30,"fixture");Check((f.B.VoiceFlags&VoiceFlags.Muted)!=0);});
Test("unmute cannot bypass ban-unban gate",f=>{f.Grant("css_ze_ban");f.Call("Punish",f.A,f.B,"ban",30,"fixture");f.Grant("css_ze_unmute");f.Call("UnpunishCommand",f.A,new CommandInfo("css_ze_unban",f.B.SteamID.ToString()),"ban");Check(File.ReadAllText(f.Punishments).Contains("ban"));});
Test("unpunish menu rechecks exact permission",f=>{f.Grant("css_ze_mute");f.Call("Punish",f.A,f.B,"mute",30,"fixture");f.Call("UnpunishFromMenu",f.A,f.B,"mute");Check((f.B.VoiceFlags&VoiceFlags.Muted)!=0);f.Grant("css_ze_unmute");f.Call("UnpunishFromMenu",f.A,f.B,"mute");Check((f.B.VoiceFlags&VoiceFlags.Muted)==0);});
Test("target immunity blocks higher target",f=>{f.SetRoles(new[]{"css_ze_kick"},10,new[]{"css_ze_kick"},20);f.Call("Kick",f.A,f.B);Check(f.B.Disconnects==0);});
Test("target immunity blocks equal protected admin",f=>{f.SetRoles(new[]{"css_ze_kick"},10,new[]{"css_ze_kick"},10);f.Call("Kick",f.A,f.B);Check(f.B.Disconnects==0);});
Test("higher immunity may target lower admin",f=>{f.SetRoles(new[]{"css_ze_kick"},20,new[]{"css_ze_kick"},10);f.Call("Kick",f.A,f.B);Check(f.B.Disconnects==1);});
Test("equal non-admin positive immunity is protected",f=>{f.SetRoles(new[]{"css_ze_kick"},20,Array.Empty<string>(),20);f.Call("Kick",f.A,f.B);Check(f.B.Disconnects==0);});
Test("CSS immunity composes with roles",f=>{f.Grant("css_ze_kick");f.B.CssImmunity=100;f.Call("Kick",f.A,f.B);Check(f.B.Disconnects==0);});
Test("true owner bypasses target protection",f=>{f.Grant();f.B.CssRoot=true;f.Plugin.Config.OwnerSteamIds.Add(f.A.SteamID.ToString());f.Call("Kick",f.A,f.B);Check(f.B.Disconnects==1);});
Test("raw owner SteamID not trusted",f=>{f.Grant("css_ze_kick");f.Plugin.Config.OwnerSteamIds.Add(f.A.SteamID.ToString());f.A.AuthorizedSteamID=null;f.Call("Kick",f.A,f.B);Check(f.B.Disconnects==0);});
Test("public noclip remains separate from god permission",f=>{f.Grant("css_ze_god");f.Call("ToggleGod",f.A);f.Call("ToggleNoclip",f.A);Check(!f.A.PlayerPawn.Value!.TakesDamage&&f.A.PlayerPawn.Value.MoveType==MoveType_t.MOVETYPE_NOCLIP);});
Test("explicit practice role works with everyone toggle off",f=>{f.Grant("css_ze_god");f.Plugin.Config.AllowEveryonePracticeCommands=false;f.Call("ToggleGod",f.A);Check(!f.A.PlayerPawn.Value!.TakesDamage);});
Test("god revocation restores only god once",f=>{f.Grant("css_ze_god","css_ze_noclip");f.Call("ToggleGod",f.A);f.Call("ToggleNoclip",f.A);f.Grant("css_ze_noclip");f.Admin.RecheckPermissions();var pawn=f.A.PlayerPawn.Value!;Check(pawn.TakesDamage&&pawn.MoveType==MoveType_t.MOVETYPE_NOCLIP);int writes=pawn.TakesDamageWrites;f.Admin.RecheckPermissions();Check(pawn.TakesDamageWrites==writes);});
Test("role removal does not revoke public noclip",f=>{f.Grant("css_ze_god","css_ze_noclip");f.Call("ToggleGod",f.A);f.Call("ToggleNoclip",f.A);f.Grant("css_ze_god");f.Admin.RecheckPermissions();Check(!f.A.PlayerPawn.Value!.TakesDamage&&f.A.PlayerPawn.Value.MoveType==MoveType_t.MOVETYPE_NOCLIP);});
Test("public noclip toggle restores captured movement",f=>{f.A.PlayerPawn.Value!.MoveType=MoveType_t.MOVETYPE_LADDER;f.A.PlayerPawn.Value.ActualMoveType=MoveType_t.MOVETYPE_NONE;f.Grant("css_ze_noclip");f.Call("ToggleNoclip",f.A);f.Call("ToggleNoclip",f.A);Check(f.A.PlayerPawn.Value.MoveType==MoveType_t.MOVETYPE_LADDER&&f.A.PlayerPawn.Value.ActualMoveType==MoveType_t.MOVETYPE_NONE);});
Test("checkpoint delete does not reset auxiliary toggles",f=>{f.Grant("css_ze_god","css_ze_noclip","css_ze_save","css_ze_tp","css_ze_clearcheckpoint");f.Call("ToggleGod",f.A);f.Call("ToggleNoclip",f.A);f.Call("SaveCheckpoint",f.A);f.Call("ClearCheckpoint",f.A);var t=f.Admin.GetPracticeToggles(f.A);Check(t["god"]&&t["noclip"]&&!t["checkpoint"]);});
Test("checkpoint revocation clears saved position",f=>{f.Grant("css_ze_save","css_ze_tp");f.Call("SaveCheckpoint",f.A);Check(f.Admin.GetPracticeToggles(f.A)["checkpoint"]);f.Grant("css_ze_tp");f.Admin.RecheckPermissions();Check(!f.Admin.GetPracticeToggles(f.A)["checkpoint"]);});
Test("reset exact permission cannot be obtained from god",f=>{f.Grant("css_ze_god");f.Call("ToggleGod",f.A);f.Call("ResetPractice",f.A);Check(!f.A.PlayerPawn.Value!.TakesDamage);});
Test("mode-off clears god but keeps public noclip",f=>{f.Grant("css_ze_god","css_ze_noclip");f.Call("ToggleGod",f.A);f.Call("ToggleNoclip",f.A);f.Plugin.IsPractice=false;f.Call("Maintenance");Check(f.A.PlayerPawn.Value!.TakesDamage&&f.A.PlayerPawn.Value.MoveType==MoveType_t.MOVETYPE_NOCLIP);});
Test("different pawn handle never receives old restoration",f=>{f.Grant("css_ze_god");f.Call("ToggleGod",f.A);var replacement=F.Pawn(123);replacement.TakesDamage=false;f.A.PlayerPawn.Value=replacement;f.A.PlayerPawn.Raw=123;f.Grant();f.Admin.RecheckPermissions();Check(!replacement.TakesDamage);});
Test("reused slot does not inherit practice toggles",f=>{f.Grant("css_ze_god");f.Call("ToggleGod",f.A);f.A.UserId=99;f.Admin.RecheckPermissions();Check(!f.Admin.GetPracticeToggles(f.A)["god"]);});
Test("spawn permission revocation prevents reapply",f=>{f.Grant("css_ze_god");f.Call("ToggleGod",f.A);f.Grant();f.A.PlayerPawn.Value=F.Pawn(999);f.A.PlayerPawn.Raw=999;f.Admin.OnPlayerSpawn(f.A);Check(f.A.PlayerPawn.Value.TakesDamage);});
Test("spawn duplicate event preserves original damage capture",f=>{f.Grant("css_ze_god");f.Call("ToggleGod",f.A);f.Admin.OnPlayerSpawn(f.A);f.Grant();f.Admin.RecheckPermissions();Check(f.A.PlayerPawn.Value!.TakesDamage);});
Test("self respawn does not grant other respawn",f=>{f.Grant("css_ze_respawn");F.Dead(f.B);f.Call("Respawn",f.A,f.B,true);Server.Tick();Check(f.B.Respawns==0);});
Test("other respawn does not grant self practice respawn",f=>{f.Grant("css_ze_respawn_other");F.Dead(f.A);f.Call("Respawn",f.A,f.A,false);Server.Tick();Check(f.A.Respawns==0);});
Test("explicit other respawn command works without self permission",f=>{f.Grant("css_ze_respawn_other");F.Dead(f.B);f.Execute("css_ze_respawn_other","#2");Server.Tick();Check(f.B.Respawns==1);});
Test("self respawn delayed invocation rechecks permission",f=>{f.Grant("css_ze_respawn");F.Dead(f.A);f.Call("Respawn",f.A,f.A,false);f.Grant();Server.Tick();Check(f.A.Respawns==0);});
Test("target respawn delayed invocation rechecks actor",f=>{f.Grant("css_ze_respawn_other");F.Dead(f.B);f.Call("Respawn",f.A,f.B,true);f.Grant();Server.Tick();Check(f.B.Respawns==0);});
Test("target respawn delayed invocation checks actor reconnect",f=>{f.Grant("css_ze_respawn_other");F.Dead(f.B);f.Call("Respawn",f.A,f.B,true);f.A.UserId=66;Server.Tick();Check(f.B.Respawns==0);});
Test("respawn delayed invocation blocks slot reuse",f=>{f.Grant("css_ze_respawn_other");F.Dead(f.B);f.Call("Respawn",f.A,f.B,true);f.B.UserId=77;Server.Tick();Check(f.B.Respawns==0);});
Test("respawn callback observes increased target immunity",f=>{f.Grant("css_ze_respawn_other");F.Dead(f.B);f.Call("Respawn",f.A,f.B,true);f.B.CssImmunity=100;Server.Tick();Check(f.B.Respawns==0);});
Test("auto respawn requires self permission",f=>{f.Grant("css_ze_kick");F.Dead(f.A);f.Call("OnDeath",new EventPlayerDeath{Userid=f.A},new GameEventInfo());Check(f.Plugin.Timers.Count==1);});
Test("auto respawn timer canceled on revoke",f=>{f.Grant("css_ze_respawn");F.Dead(f.A);f.Call("OnDeath",new EventPlayerDeath{Userid=f.A},new GameEventInfo());var t=f.Plugin.Timers.Last();f.Grant();f.Admin.RecheckPermissions();Check(t.Killed);t.Fire(true);Check(f.A.Respawns==0);});
Test("auto respawn callback independently checks revoked permission",f=>{f.Grant("css_ze_respawn");F.Dead(f.A);f.Call("OnDeath",new EventPlayerDeath{Userid=f.A},new GameEventInfo());var t=f.Plugin.Timers.Last();f.Grant();t.Fire();Check(f.A.Respawns==0);});
Test("auto respawn round generation blocks stale timer",f=>{f.Grant("css_ze_respawn");F.Dead(f.A);f.Call("OnDeath",new EventPlayerDeath{Userid=f.A},new GameEventInfo());var t=f.Plugin.Timers.Last();f.Admin.OnRoundStart();t.Fire(true);Check(f.A.Respawns==0);});
Test("authorized auto respawn fires",f=>{f.Grant("css_ze_respawn");F.Dead(f.A);f.Call("OnDeath",new EventPlayerDeath{Userid=f.A},new GameEventInfo());f.Plugin.Timers.Last().Fire();Check(f.A.Respawns==1);});
Test("team reset prevents competing auto respawn",f=>{f.Grant("css_ze_respawn");F.Dead(f.A);f.Call("OnDeath",new EventPlayerDeath{Userid=f.A},new GameEventInfo());f.Plugin.ResettingSlots.Add(f.A.Slot);f.Plugin.Timers.Last().Fire();Check(f.A.Respawns==0);});
Test("team reset prevents competing manual respawn",f=>{f.Grant("css_ze_respawn");F.Dead(f.A);f.Call("Respawn",f.A,f.A,false);f.Plugin.ResettingSlots.Add(f.A.Slot);Server.Tick();Check(f.A.Respawns==0);});
Test("practice menu disables ungranted independent buttons",f=>{f.Grant("css_ze_practice","css_ze_god");f.Call("ShowPractice",f.A);var m=f.Menu;Check(!f.Option(m,"无敌：").Disabled&&!f.Option(m,"穿墙：").Disabled&&f.Option(m,"只删除").Disabled);});
Test("practice stale menu cannot toggle after revoke",f=>{f.Grant("css_ze_practice","css_ze_god");f.Call("ShowPractice",f.A);var option=f.Option(f.Menu,"无敌：");f.Grant("css_ze_practice");option.Click(f.A,true);Check(f.A.PlayerPawn.Value!.TakesDamage);});
Test("menu owner slot-userid protects callbacks",f=>{f.Grant("css_ze_practice","css_ze_god");f.Call("ShowPractice",f.A);var option=f.Option(f.Menu,"无敌：");f.A.UserId=88;option.Click(f.A,true);Check(f.A.PlayerPawn.Value!.TakesDamage);});
Test("management menu gates exact actions rather than shell",f=>{f.Grant("css_zeadmin","css_ze_kick");f.Call("ShowAdmin",f.A);Check(!f.Option(f.Menu,"重新开始").Disabled&&f.Option(f.Menu,"重载地图").Disabled);});
Test("target menu enables kick while disabling slay ban bring",f=>{f.Grant("css_ze_kick");f.Call("ShowPlayer",f.A,f.B);Check(!f.Option(f.Menu,"踢出").Disabled&&f.Option(f.Menu,"处死").Disabled&&f.Option(f.Menu,"永久封禁").Disabled&&f.Option(f.Menu,"拉到").Disabled);});
Test("target menu stale action cannot kick reconnected target",f=>{f.Grant("css_ze_kick");f.Call("ShowPlayer",f.A,f.B);f.Option(f.Menu,"踢出").Click(f.A);var confirm=f.Option(f.Menu,"确认执行");f.B.UserId=90;confirm.Click(f.A,true);Check(f.B.Disconnects==0);});
Test("confirmation rechecks exact permission",f=>{f.Grant("css_ze_kick");f.Call("ShowPlayer",f.A,f.B);f.Option(f.Menu,"踢出").Click(f.A);var confirm=f.Option(f.Menu,"确认执行");f.Grant("css_ze_slay");confirm.Click(f.A,true);Check(f.B.Disconnects==0);});
Test("unchanged authorized confirmation acts once",f=>{f.Grant("css_ze_kick");f.Call("ShowPlayer",f.A,f.B);f.Option(f.Menu,"踢出").Click(f.A);f.Option(f.Menu,"确认执行").Click(f.A);Check(f.B.Disconnects==1);});
Test("public maps route to voting but never immediate map",f=>{f.MapFile();f.Grant();f.Call("ShowMaps",f.A,"");Check(f.Plugin.Replies.Last().StartsWith("mapvote"));f.Call("MapCommand",f.A,new CommandInfo("css_ze_map","fixture_map"));Check(Server.Commands.Count==0);});
Test("owner map callback rechecks ownership after revocation",f=>{f.MapFile();f.Plugin.Config.OwnerSteamIds.Add(f.A.SteamID.ToString());f.Call("ShowInstalledMaps",f.A,"");f.Menu.MenuOptions[0].Click(f.A);var confirm=f.Option(f.Menu,"确认执行");f.Plugin.Config.OwnerSteamIds.Clear();f.Grant("css_ze_map");confirm.Click(f.A,true);Check(Server.Commands.Count==0);});
Test("admin wildcard cannot instantly change maps",f=>{f.MapFile();f.Grant("*");f.Call("MapCommand",f.A,new CommandInfo("css_ze_map","fixture_map"));Check(Server.Commands.Count==0);});
Test("configured owner can instantly change installed maps",f=>{f.MapFile();f.Plugin.Config.OwnerSteamIds.Add(f.A.SteamID.ToString());f.Call("MapCommand",f.A,new CommandInfo("css_ze_map","fixture_map"));Check(Server.Commands.Single().StartsWith("map fixture_map "));});
Test("owner can directly specify VPK filename",f=>{f.MapFile();f.Plugin.Config.OwnerSteamIds.Add(f.A.SteamID.ToString());f.Call("MapCommand",f.A,new CommandInfo("css_ze_map","fixture_map.vpk"));Check(Server.Commands.Single().StartsWith("map fixture_map "));});
Test("owner menu labels use VPK names not translated titles or IDs",f=>{f.MapFile();f.Plugin.Config.OwnerSteamIds.Add(f.A.SteamID.ToString());f.Call("ShowInstalledMaps",f.A,"");Check(f.Menu.MenuOptions.Single().Text=="fixture_map.vpk");});
Test("fullwidth permitted server command survives gag filter",f=>{f.Grant("css_ze_gag","css_ze_practice");f.Call("Punish",f.A,f.A,"gag",30,"fixture");var result=(HookResult)f.Call("GagFilter",f.A,new CommandInfo("say","！ze_practice"))!;Check(result==HookResult.Continue);});
Test("gag does not block permitted commands owned by other services",f=>{f.Grant("css_ze_gag","css_items");f.Call("Punish",f.A,f.A,"gag",30,"fixture");Check((HookResult)f.Call("GagFilter",f.A,new CommandInfo("say","!items"))! == HookResult.Continue);});
Test("gag still blocks normal text",f=>{f.Grant("css_ze_gag");f.Call("Punish",f.A,f.A,"gag",30,"fixture");Check((HookResult)f.Call("GagFilter",f.A,new CommandInfo("say","普通聊天"))! == HookResult.Handled);});
Test("gag permits numeric menu selection",f=>{f.Grant("css_ze_gag");f.Call("Punish",f.A,f.A,"gag",30,"fixture");Check((HookResult)f.Call("GagFilter",f.A,new CommandInfo("say","！1"))! == HookResult.Continue);});
Test("help identity and owner maps have callback checks",f=>{f.Grant("css_ze_kick");f.Call("ShowHelp",f.A);f.Call("ShowIdentity",f.A);f.Call("ShowInstalledMaps",f.A,"");Check(f.Plugin.Replies.All(x=>x.StartsWith("denied")));});
Test("gag still permits raw RTV and nomination",f=>{f.Grant("css_ze_gag");f.Call("Punish",f.A,f.A,"gag",30,"fixture");foreach(var text in new[]{"rtv","unrtv","host_workshop_map +3817448617"})Check((HookResult)f.Call("GagFilter",f.A,new CommandInfo("say",text))! == HookResult.Continue);});
Test("dispose unregisters commands and restores auxiliaries",f=>{f.Grant("css_ze_god");f.Call("ToggleGod",f.A);f.Admin.Dispose();Check(f.Plugin.Commands.Count==0&&f.A.PlayerPawn.Value!.TakesDamage);});
File.WriteAllText(Path.Combine(reportRoot,"results.json"),JsonSerializer.Serialize(new{passed,failed,results},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"AdminAccess: {passed}/{passed+failed}; results: {reportRoot}");
return failed==0?0:1;

sealed class F : IDisposable
{
    public readonly ZePracticePlugin Plugin;public readonly AdminPracticeService Admin;public readonly CCSPlayerController A,B;
    public string Punishments=>Path.Combine(Plugin.ModuleDirectory,"data","punishments.json");
    public F(string path)
    {
        Directory.CreateDirectory(path);Utilities.Players.Clear();Server.Frames.Clear();Server.Commands.Clear();Utilities.StateChanges=0;
        Server.GameDirectory=Path.Combine(path,"game");Directory.CreateDirectory(Path.Combine(Server.GameDirectory,"csgo","maps"));
        Plugin=new(){ModuleDirectory=path};Plugin.Permissions=new(Plugin);Plugin.Permissions.Initialize();
        A=Player(1,1);B=Player(2,2);Utilities.Players.AddRange(new[]{A,B});
        Admin=new(Plugin);Admin.Initialize();Admin.OnMapStart("fixture_map");Grant();
    }
    public static CCSPlayerPawn Pawn(int handle)=>new(){Handle=new IntPtr(handle)};
    private static CCSPlayerController Player(int slot,int id)
    {
        ulong steam=76561198000000000UL+(ulong)slot;
        return new(){Slot=slot,UserId=id,SteamID=steam,AuthorizedSteamID=new(steam),PlayerName="Fixture"+slot,PlayerPawn=new(){Raw=(uint)slot,Value=Pawn(slot)}};
    }
    public static void Dead(CCSPlayerController p){p.PawnIsAlive=false;p.PlayerPawn.Value!.Health=0;}
    public void Grant(params string[] commands)=>SetRoles(commands,10,Array.Empty<string>(),0);
    public void SetRoles(string[] a,int ai,string[] b,int bi)
    {
        var roles=new Dictionary<string,object>{{"actor",new{Allow=a,Deny=Array.Empty<string>(),Inherits=Array.Empty<string>(),Immunity=ai}}, {"target",new{Allow=b,Deny=Array.Empty<string>(),Inherits=Array.Empty<string>(),Immunity=bi}}};
        var users=new Dictionary<string,string[]>{{A.SteamID.ToString(),new[]{"actor"}},{B.SteamID.ToString(),new[]{"target"}}};
        File.WriteAllText(Plugin.Permissions.ConfigurationPath,JsonSerializer.Serialize(new{Version=1,CssRootBypass=true,DefaultRoles=Array.Empty<string>(),Roles=roles,Users=users}));
        if(!Plugin.Permissions.Reload(out var error))throw new Exception(error);
    }
    public object? Call(string method,params object?[] args)
    {
        var m=typeof(AdminPracticeService).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)??throw new Exception("missing method "+method);
        return m.Invoke(Admin,args);
    }
    public void Execute(string command,params string[] args)=>Plugin.Commands[command](A,new CommandInfo(new[]{command}.Concat(args).ToArray()));
    public ChatMenu Menu=>Plugin.Menus[A.Slot];
    public ChatMenuOption Option(ChatMenu menu,string prefix)=>menu.MenuOptions.Single(x=>x.Text.StartsWith(prefix,StringComparison.Ordinal));
    public void MapFile()=>File.WriteAllText(Path.Combine(Server.GameDirectory,"csgo","maps","fixture_map.vpk"),"fixture");
    public void Dispose(){Admin.Dispose();Plugin.Permissions.Dispose();Utilities.Players.Clear();Server.Frames.Clear();Server.Commands.Clear();}
}
