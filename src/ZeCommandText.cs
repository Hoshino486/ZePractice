using System.Text.RegularExpressions;
namespace ZePractice;

/// <summary>Pure command spelling/alias rules shared by binds, chat and access control.</summary>
public static class ZeCommandText
{
    private static readonly Dictionary<string,string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["css_zemenu"]="css_ze", ["css_ze_admin"]="css_zeadmin", ["css_ze_help"]="css_zhelp",
        ["css_zestatus"]="css_ze_status", ["ze_status"]="css_ze_status", ["css_zereload"]="css_ze_reload", ["ze_mode"]="css_ze_mode",
        ["css_zeweapon"]="css_zebuy",
        ["css_ze_select"]="css_ze", ["css_ze_ui_status"]="css_ze", ["css_ze_round_status"]="css_ze_status",
        ["noclip"]="css_ze_noclip", ["god"]="css_ze_god"
    };
    private static readonly HashSet<string> Supplies = new("he hegrenade nade grenade molotov molo inc incendiary incgrenade freeze ice decoy barrier flash flashbang blackhole black smoke smokegrenade adrenaline healthshot heal medic".Split(' '),StringComparer.OrdinalIgnoreCase);
    public static string Canonical(string name)
    {
        name=(name??"").Trim().ToLowerInvariant();
        if(Aliases.TryGetValue(name,out var canonical))return canonical;
        if(name.StartsWith("css_",StringComparison.Ordinal) && (Supplies.Contains(name[4..]) || ZeGameplayCatalog.WeaponAliases.ContainsKey(name[4..])))return "css_zebuy";
        return name;
    }
    public static bool TryParseChat(string text,out string name,out string arguments,out int menuKey)
    {
        name="";arguments="";menuKey=0;text=(text??"").Trim();
        if(text.Length<2 || text.Length>512 || (text[0]!='!'&&text[0]!='！'))return false;
        return TryParseBody(text[1..],out name,out arguments,out menuKey);
    }
    // Separate from TryParseBody: its menuKey=0 is the existing non-menu-command sentinel.
    public static bool TryParseMenuKeyChat(string? text, out int key)
    {
        key = -1; text = (text ?? "").Trim();
        if (text.Length != 2 || text[0] is not ('!' or '！') || text[1] is < '0' or > '9') return false;
        key = text[1] - '0'; return true;
    }
    public static bool TryParseBody(string body,out string name,out string arguments,out int menuKey)
    {
        name="";arguments="";menuKey=0;body=(body??"").Trim();
        // Forward one command, never a command-buffer script. Reasons/names may remain Unicode.
        if(body.Length==0 || body.Length>511 || body.Any(c=>char.IsControl(c)||c==';'))return false;
        int split=body.IndexOfAny(new[]{' ','\t'});
        var token=(split<0?body:body[..split]).ToLowerInvariant();
        arguments=split<0?"":body[(split+1)..].Trim();
        if(token.Length==1 && token[0]>='1'&&token[0]<='9' && arguments.Length==0){menuKey=token[0]-'0';return true;}
        if(!Regex.IsMatch(token,@"\A[a-z][a-z0-9_]{0,63}\z",RegexOptions.CultureInvariant))return false;
        // Unbalanced quotes would change argument grouping in the engine; do not forward them.
        if(arguments.Count(c=>c=='"')%2!=0 || arguments.Contains('\\'))return false;
        name=token.StartsWith("css_",StringComparison.Ordinal)||token.StartsWith("ms_",StringComparison.Ordinal)?token:"css_"+token;
        return true;
    }
}
