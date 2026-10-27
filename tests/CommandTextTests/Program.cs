using ZePractice;
int passed=0;void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);passed++;}
foreach(var prefix in new[]{"!","！"}){
 Check(ZeCommandText.TryParseChat(prefix+"ZE_noclip",out var n,out var a,out var k)&&n=="css_ze_noclip"&&a==""&&k==0,"punctuation "+prefix);
 Check(ZeCommandText.TryParseChat(prefix+"ze_kick \"中文 Name\"",out n,out a,out k)&&n=="css_ze_kick"&&a=="\"中文 Name\"","quoted Unicode target "+prefix);
 foreach(var key in Enumerable.Range(1,9))Check(ZeCommandText.TryParseChat(prefix+key,out n,out a,out k)&&k==key,"menu key "+prefix+key);
}
foreach(var invalid in new[]{"hello","", "!", "！！ze", "！ze\nquit", "！ze;quit", "！ze_kick \"bad", "！ze_kick bad\\name", "！<script>", "！9 more", "！ze " + new string('a',512), "！ze\0x"})Check(!ZeCommandText.TryParseChat(invalid,out _,out _,out _),"reject invalid "+passed);
foreach(var kv in new Dictionary<string,string>{{"css_zemenu","css_ze"},{"css_ze_admin","css_zeadmin"},{"css_ze_help","css_zhelp"},{"css_zereload","css_ze_reload"},{"css_zestatus","css_ze_status"},{"noclip","css_ze_noclip"},{"god","css_ze_god"},{"css_zeweapon","css_zebuy"}})Check(ZeCommandText.Canonical(kv.Key)==kv.Value,"permission alias "+kv.Key);
foreach(var key in ZeGameplayCatalog.WeaponAliases.Keys)Check(ZeCommandText.Canonical("css_"+key)=="css_zebuy","weapon alias "+key);
foreach(var key in new[]{"he","smoke","flash","ice","black","healthshot","medic"})Check(ZeCommandText.Canonical("css_"+key)=="css_zebuy","supply alias "+key);
Check(ZeCommandText.Canonical("css_ze_kick")=="css_ze_kick","nonalias stable");
Check(ZeCommandText.Canonical("css_ze_select")=="css_ze","radio selection requires menu permission");
Check(ZeCommandText.Canonical("css_ze_ui_status")=="css_ze","personal UI diagnostics share public menu permission");
Check(ZeCommandText.Canonical("css_ze_round_status")=="css_ze_status","round diagnostics require status permission");
Console.WriteLine($"COMMAND_TEXT_TESTS {passed}/{passed} passed");
