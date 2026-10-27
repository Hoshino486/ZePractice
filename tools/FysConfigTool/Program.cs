using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

internal static class Program
{
    static readonly JsonDocumentOptions ReadOptions = new(){CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true};
    static readonly JsonSerializerOptions WriteOptions = new(){WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    static int Main(string[] args)
    {
        try
        {
            if(args.Length==1 && args[0]=="selftest") { SelfTest(); return 0; }
            if(args.Length < 3) { Console.Error.WriteLine("FysConfigTool import SOURCE DESTINATION [SERVER_NUMBER=2001] [STRIPPER_OUTPUT] [SOURCE_REPOSITORY] | selftest"); return 2; }
            if(args[0]!="import") throw new ArgumentException("Unknown action");
            var source=Path.GetFullPath(args[1]); var destination=Path.GetFullPath(args[2]);
            if(source.Equals(destination,StringComparison.OrdinalIgnoreCase)||destination.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Source and destination must be separate trees.");
            var server=args.Length>3?args[3]:"2001";
            if(!Regex.IsMatch(server,"^[0-9]{1,8}$")) throw new ArgumentException("Invalid server number");
            var stripper=args.Length>4?Path.GetFullPath(args[4]):Path.Combine(destination,"converted-stripper");
            var sourceRepo=args.Length>5?args[5]:"fyscs/cs2";
            if(!Regex.IsMatch(sourceRepo,"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) throw new ArgumentException("Invalid repository identifier");
            var counters=new Dictionary<string,int>(); var files=new List<object>();
            foreach(var rel in new[]{ $"{server}/csgo/cfg/map-configs",$"{server}/sharp/configs/bosses",$"{server}/sharp/configs/translations",$"{server}/sharp/stripper"})
            {
                var folder=Path.Combine(source,rel); if(!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
                var count=0;
                foreach(var file in Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories))
                {
                    if(Path.GetExtension(file) is not (".json" or ".jsonc" or ".cfg")) continue;
                    if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("Symlink is not a config file");
                    if(Path.GetExtension(file)!=".cfg") _=Parse(file); // fails before deployment, never execute config contents
                    var relative=Path.GetRelativePath(source,file);
                    var target=Path.GetFullPath(Path.Combine(destination,relative));
                    if(!target.StartsWith(destination+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path escaped destination");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file,target,true);
                    files.Add(new{path=relative.Replace('\\','/'),sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)))}); count++;
                }
                counters[rel]=count;
            }
            foreach(var rel in new[]{ $"{server}/sharp/configs/maps.json",$"{server}/sharp/configs/mappers.json","README.md","LICENSE","LICENSE.md"})
            {
                var file=Path.Combine(source,rel); if(!File.Exists(file)) continue;
                if(rel.EndsWith(".json")) _=Parse(file);
                var target=Path.Combine(destination,rel); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file,target,true);
            }
            var stRoot=Path.Combine(destination,server,"sharp","stripper");
            Directory.CreateDirectory(stripper);
            int converted=0;
            foreach(var file in Directory.EnumerateFiles(stRoot,"*.jsonc",SearchOption.AllDirectories))
            {
                var rel=Path.GetRelativePath(stRoot,file);
                if(rel=="global.jsonc") rel="global_lump.jsonc";
                else if(rel=="global_default.jsonc") rel="global_map.jsonc";
                else if(!rel.StartsWith("maps"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unrecognized Stripper location: "+rel);
                var convertedData=ConvertStripper(Parse(file).AsObject());
                var target=Path.Combine(stripper,rel); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target,convertedData.ToJsonString(WriteOptions)); converted++;
            }
            var manifest=new{generatedAt=DateTimeOffset.Now,sourceRepository=sourceRepo,server,originalFiles=counters,convertedStripperFiles=converted,rewardsIncluded=false,files};
            File.WriteAllText(Path.Combine(destination,"import-manifest.json"),JsonSerializer.Serialize(manifest,WriteOptions));
            File.WriteAllText(Path.Combine(destination,"ATTRIBUTION.txt"),$"Original configurations: fyscs/cs2, Copyright 2019-2026 FyS. CC BY-NC-SA 4.0.\nOriginal files copied from: https://github.com/{sourceRepo}\nUpstream: https://github.com/fyscs/cs2\nOnly required original configuration files are preserved; derived StripperCS2 config is an adaptation under the same license.\nNo reward configurations are imported.\n");
            Console.WriteLine(JsonSerializer.Serialize(new{success=true,counts=counters,convertedStripper=converted,rewardsIncluded=false},WriteOptions)); return 0;
        }
        catch(Exception e) { Console.Error.WriteLine("IMPORT_FAILED: "+e.Message); return 1; }
    }
    static JsonNode Parse(string file) => JsonNode.Parse(File.ReadAllText(file),documentOptions:ReadOptions) ?? throw new InvalidDataException("null JSON: "+file);
    static JsonArray AsArray(JsonNode node) => node is JsonArray arr ? arr : new JsonArray(node.DeepClone());
    internal static JsonObject ConvertStripper(JsonObject root)
    {
        var result=new JsonObject();
        foreach(var (action,value) in root)
        {
            if(value is null) throw new InvalidDataException("null Stripper action "+action);
            if(action is not ("remove" or "filter" or "add" or "modify")) throw new InvalidDataException("Unknown Stripper action "+action);
            var key=action=="remove"?"filter":action;
            var output=result[key] as JsonArray ?? new JsonArray();
            if(result[key] is null) result[key]=output;
            foreach(var element in AsArray(value))
            {
                var entry=element as JsonObject ?? throw new InvalidDataException("Stripper action must be an object");
                if(action=="modify")
                {
                    var change=new JsonObject();
                    foreach(var (part,body) in entry)
                    {
                        if(part is not ("match" or "delete" or "insert" or "replace")) throw new InvalidDataException("Unknown modify block "+part);
                        // FYS disables 'replace' by default. Refuse it rather than silently changing semantics.
                        if(part=="replace") throw new InvalidDataException("FYS replace is disabled by default; review this config explicitly before converting.");
                        change[part]=ConvertEntity(body?.AsObject()??throw new InvalidDataException("null block"),part is "match" or "delete",part=="delete");
                    }
                    if(change["match"] is null) throw new InvalidDataException("modify requires match");
                    output.Add(change);
                }
                else output.Add(ConvertEntity(entry,action is "remove" or "filter",false));
            }
        }
        return result;
    }
    static JsonObject ConvertEntity(JsonObject entity,bool matching,bool deleting)
    {
        var output=new JsonObject();
        foreach(var (key,value) in entity)
        {
            if(key.Equals("connections",StringComparison.OrdinalIgnoreCase))
            {
                var connections=value as JsonArray ?? throw new InvalidDataException("connections must be an array");
                var io=new JsonArray();
                foreach(var item in connections)
                {
                    var connection=item as JsonObject??throw new InvalidDataException("IO must be object");
                    var translated=new JsonObject();
                    foreach(var (name,data) in connection)
                    {
                        var mapped=name switch {"output"=>"outputname","target"=>"targetname","input"=>"inputname","param"=>"overrideparam","delay"=>"delay","limit"=>"timestofire",_=>throw new InvalidDataException("Unknown FYS IO field "+name)};
                        if(data is null) throw new InvalidDataException("IO values must not be null");
                        if(name is "delay" or "limit") translated[mapped]=data.DeepClone();
                        else translated[mapped]=matching?MatchValue(data.ToString(),name is "output" or "param"):data.ToString();
                    }
                    if(!matching)
                    {
                        foreach(var required in new[]{"outputname","targetname","inputname"}) if(string.IsNullOrEmpty(translated[required]?.ToString())) throw new InvalidDataException("Missing IO "+required);
                        translated["targettype"]=7; translated["overrideparam"]??=""; translated["delay"]??=0f; translated["timestofire"]??=-1;
                    }
                    io.Add(translated);
                }
                output["io"]=io; continue;
            }
            if(value is null) throw new InvalidDataException("Null entity value "+key);
            var text=value is JsonArray or JsonObject ? "<KV3_TYPE_ARRAY>" : value.ToString();
            if(value is JsonValue jv && jv.TryGetValue<bool>(out var b)) text=b?"true":"false";
            var wildcard=deleting||key.Equals("targetname",StringComparison.OrdinalIgnoreCase)||key.Equals("classname",StringComparison.OrdinalIgnoreCase);
            output[key]=matching?MatchValue(text,wildcard):text;
        }
        return output;
    }
    static string MatchValue(string text,bool wildcard)
    {
        if(wildcard&&text.EndsWith('*')) return "/^"+Regex.Escape(text[..^1])+".*$/";
        return "/^"+Regex.Escape(text)+"$/"; // StripperCS2 PCRE2_CASELESS preserves FYS case-insensitive values
    }
    static void SelfTest()
    {
        int assertions=0;
        void Check(bool condition,string name){if(!condition)throw new Exception("SELFTEST FAILED: "+name);assertions++;}
        var sample=JsonNode.Parse("""{"remove":[{"classname":"FOO"}],"modify":[{"match":{"targetname":"boss*","connections":[{"output":"OnHit","input":"Use*"}]},"delete":{"weapon0":"weapon_*"},"insert":{"health":"100"}}],"add":[{"classname":"logic_auto","connections":[{"output":"OnMapSpawn","target":"foo","input":"Enable"}]}]}""")!.AsObject();
        var result=ConvertStripper(sample);
        Check(result["filter"]![0]!["classname"]!.ToString()=="/^FOO$/","remove to filter and insensitive exact match");
        Check(result["modify"]![0]!["match"]!["targetname"]!.ToString()=="/^boss.*$/","trailing wildcard");
        Check(result["modify"]![0]!["match"]!["io"]![0]!["inputname"]!.ToString()=="/^Use\\*$/","input star literal");
        Check(result["modify"]![0]!["delete"]!["weapon0"]!.ToString()=="/^weapon_.*$/","delete wildcard");
        Check(result["modify"]![0]!["insert"]!["health"]!.ToString()=="100","insert not regex");
        Check(result["add"]![0]!["io"]![0]!["targettype"]!.GetValue<int>()==7,"IO target type");
        Check(result["add"]![0]!["io"]![0]!["timestofire"]!.GetValue<int>()==-1,"IO unlimited default");
        Check(!sample.ContainsKey("filter"),"source unmodified");
        var failed=false;try{ConvertStripper(JsonNode.Parse("""{"modify":[{"match":{},"replace":{}}]}""")!.AsObject());}catch(InvalidDataException){failed=true;}
        Check(failed,"unsupported replace fail closed");
        Console.WriteLine($"SELFTEST PASS: {assertions} assertions");
    }
}