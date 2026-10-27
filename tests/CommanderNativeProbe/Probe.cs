using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Plugin.Host;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

public sealed partial class CommanderNativeProbe : BasePlugin
{
    public override string ModuleName => "CommanderNativeProbe - isolated port only";
    public override string ModuleVersion => "1.0.0";
    private readonly List<object> _results = new();
    private static object Field(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void SetField(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(target, args);
    private static object Plugin()
    {
        var manager = (IPluginManager)Field(Application.Instance, "_pluginManager");
        return manager.GetLoadedPlugins().Single(p => p.Plugin.GetType().FullName == "ZePractice.ZePracticePlugin").Plugin;
    }
    private static void Guard()
    {
        if (ConVar.Find("hostport")?.GetPrimitiveValue<int>() != 27025 || Utilities.GetPlayers().Any(p => !p.IsBot && !p.IsHLTV))
            throw new InvalidOperationException("Probe is restricted to port 27025 with zero human players.");
    }
    private static object[] Markers(object service) => ((IEnumerable)Field(service, "_state").GetType().GetProperty("Markers")!.GetValue(Field(service, "_state"))!).Cast<object>().ToArray();
    private static T Prop<T>(object value, string property) => (T)value.GetType().GetProperty(property)!.GetValue(value)!;
    private static long Id(object value) => Prop<long>(value, "Id");
    private void Record(string name, bool passed, object? data = null)
    {
        var result = new { name, passed, data }; _results.Add(result);
        Server.PrintToConsole("COMMANDER_PROBE " + JsonSerializer.Serialize(result));
        File.WriteAllText(Path.Combine(ModuleDirectory, "results.json"), JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true }));
        if (!passed) throw new InvalidOperationException("Probe failed: " + name);
    }
    private void Later(float seconds, Action work) => AddTimer(seconds, () => { Guard(); work(); }, TimerFlags.STOP_ON_MAPCHANGE);
    public override void Load(bool hotReload)
    {
        RegisterCountdownFacingProbe();
        RegisterGameplay151Probe();
        AddCommand("css_commander_bot_status", "Isolated bot diagnostics", (caller, command) =>
        {
            if (caller != null) return; Guard();
            foreach (var p in Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller"))
                Server.PrintToConsole(JsonSerializer.Serialize(new { index=p.Index, connected=p.Connected, isBot=p.IsBot, name=p.PlayerName, team=p.TeamNum, alive=p.PawnIsAlive,
                    pawn=p.PlayerPawn.Raw, flags=p.Flags }));
        });
        AddCommand("css_commander_beam_probe", "Isolated beam material spawn check", (caller, command) =>
        {
            if (caller != null) return; Guard();
            File.WriteAllText(Path.Combine(ModuleDirectory, "beam-material.jsonl"), "");
            var origin = Utilities.GetPlayers().FirstOrDefault(p => p.IsBot && p.PawnIsAlive)?.PlayerPawn.Value?.AbsOrigin;
            var start = origin == null ? new Vector3(0, 0, 100) : new Vector3(origin.X, origin.Y, origin.Z + 70);
            foreach (string designer in new[] { "beam", "env_beam" })
            {
                var beam = Utilities.CreateEntityByName<CBeam>(designer)!;
                using var kv = new CEntityKeyValues();
                string name = "commander_material_probe_" + designer;
                kv.SetString("targetname", name);
                kv.SetString("texture", "materials/sprites/laserbeam.vmat");
                kv.SetString("LightningStart", name);
                kv.SetVector("origin", new CounterStrikeSharp.API.Modules.Utils.Vector(start.X, start.Y, start.Z));
                kv.SetVector("targetpoint", new CounterStrikeSharp.API.Modules.Utils.Vector(100, 0, 0));
                kv.SetFloat("BoltWidth", 4); kv.SetFloat("life", 0); kv.SetFloat("damage", 0);
                kv.SetInt("spawnflags", 1); kv.SetColor("rendercolor", System.Drawing.Color.FromArgb(255,25,210,255));
                kv.SetInt("renderamt", 255); kv.SetFloat("HDRColorScale", 2);
                beam.DispatchSpawn(kv);
                Server.NextFrame(() => {
                    Guard();
                    var data = new { designer, valid=beam.IsValid, material=beam.BaseMaterial.Value,
                        start=beam.AbsOrigin?.ToString(), end=beam.EndPos.ToString(), width=beam.Width,
                        sprite=designer=="env_beam"?beam.As<CEnvBeam>().SpriteName:null,
                        spriteTexture=designer=="env_beam"?beam.As<CEnvBeam>().SpriteTexture.Value:0,
                        turnedOff=beam.TurnedOff, type=beam.BeamType, damage=beam.Damage };
                    Server.PrintToConsole("BEAM_MATERIAL_PROBE " + JsonSerializer.Serialize(data));
                    File.AppendAllText(Path.Combine(ModuleDirectory, "beam-material.jsonl"), JsonSerializer.Serialize(data) + Environment.NewLine);
                    if (beam.IsValid) beam.Remove();
                });
            }
        });
        AddCommand("css_commander_probe", "Full isolated commander/model/trace regression", (caller, command) =>
        {
            if (caller != null) return; Guard(); _results.Clear();
            var service = Field(Plugin(), "_commander"); var models = Field(Plugin(), "_serverModels");
            var bots = Utilities.GetPlayers().Where(p => p.IsBot && p.TeamNum == 3 && p.PawnIsAlive).Take(2).ToArray();
            var zombie = Utilities.GetPlayers().First(p => p.IsBot && p.TeamNum == 2 && p.PawnIsAlive);
            if (bots.Length != 2) throw new InvalidOperationException("Need two live CT bots and one T bot");
            var a = bots[0]; var b = bots[1];
            Call(service, "OnRoundStart");
            Record("zombie-rejected", !(bool)Call(service, "TryAcquire", zombie)!);
            Record("human-acquired", (bool)Call(service, "TryAcquire", a)!);
            Record("second-leader-rejected", !(bool)Call(service, "TryAcquire", b)!);
            var leaderGlow = Field(service, "_leaderGlow");
            var outline = Prop<CDynamicProp>(leaderGlow, "Outline");
            var relay = Prop<CDynamicProp>(leaderGlow, "Relay");
            Record("commander-full-world-proxy-glow", outline.Glow.GlowType == 3 && outline.Glow.Glowing && outline.Glow.GlowRange == 0 && outline.Glow.GlowTeam == -1);
            Record("commander-real-follow-chain", (bool)Call(leaderGlow, "Linked", a.PlayerPawn.Value!)!);
            Record("commander-proxies-have-resident-player-model", new[]{outline, relay}.All(e => e.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.Model.Value != 0 && e.CBodyComponent.SceneNode.GetSkeletonInstance().ModelState.ModelName == a.PlayerPawn.Value!.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName));
            Record("commander-proxies-nonsolid", outline.Collision.SolidType == SolidType_t.SOLID_NONE && relay.Collision.SolidType == SolidType_t.SOLID_NONE);
            outline.Glow.GlowType = 0; Call(service, "ApplyLeaderGlow");
            Record("commander-overwritten-glow-restored", outline.Glow.GlowType == 3);
            var third = Field(Plugin(), "_thirdPerson");
            Call(third, "SetEnabled", a, true);
            var camera = a.PlayerPawn.Value!.CameraServices!.ViewEntity.Value!.As<CCSCustomPlayerCamera>();
            Record("thirdperson-human-camera-linked", camera.Pawn.Raw == a.PlayerPawn.Raw && camera.CameraMode == CustomCameraMode_t.CUSTOM_CAMERA_MODE_FOLLOW_POSITION && camera.ClipCameraOffset);
            Call(third, "SetEnabled", a, true);
            Record("thirdperson-on-is-idempotent", a.PlayerPawn.Value.CameraServices.ViewEntity.Raw == camera.EntityHandle.Raw);
            Record("thirdperson-personal-not-global", !b.PlayerPawn.Value!.CameraServices!.ViewEntity.IsValid);
            Call(third, "SetEnabled", a, false);
            Record("thirdperson-off-restores-first-person", !a.PlayerPawn.Value.CameraServices.ViewEntity.IsValid && camera.CameraMode == CustomCameraMode_t.CUSTOM_CAMERA_MODE_DISABLED, new { view=a.PlayerPawn.Value.CameraServices.ViewEntity.Raw, cameraValid=camera.IsValid });
            Later(.15f, () => Record("thirdperson-disabled-camera-removed-next-tick", !camera.IsValid));
            Call(third, "SetEnabled", zombie, true);
            Record("thirdperson-zombie-also-supported", zombie.PlayerPawn.Value!.CameraServices!.ViewEntity.IsValid);
            Call(third, "SetEnabled", zombie, false);
            var mapCamera = Utilities.CreateEntityByName<CCSCustomPlayerCamera>("custom_player_camera")!;
            mapCamera.DispatchSpawn(); a.PlayerPawn.Value.CameraServices.ViewEntity.Raw = mapCamera.EntityHandle.Raw;
            Call(third, "SetEnabled", a, true);
            Record("thirdperson-does-not-steal-map-camera", a.PlayerPawn.Value.CameraServices.ViewEntity.Raw == mapCamera.EntityHandle.Raw);
            Call(third, "SetEnabled", a, false);
            Record("thirdperson-off-does-not-delete-map-camera", mapCamera.IsValid && a.PlayerPawn.Value.CameraServices.ViewEntity.Raw == mapCamera.EntityHandle.Raw);
            a.PlayerPawn.Value.CameraServices.ViewEntity.Raw = uint.MaxValue; mapCamera.Remove();
            var origin = a.PlayerPawn.Value.AbsOrigin!;
            var anchor = new Vector3(origin.X, origin.Y, origin.Z);
            for (int i = 0; i < 5; i++)
            {
                a.PlayerPawn.Value.EyeAngles.X = -89;
                Call(service, "ProcessPing", a, anchor + new Vector3(100 + i * 90, 200 + i * 30, 0));
            }
            var markers = Markers(service); long oldest = Id(markers[0]);
            var icons = Utilities.FindAllEntitiesByDesignerName<CDynamicProp>("prop_dynamic").Where(e => e.Entity?.Name?.StartsWith("ze_commander_marker_") == true).ToArray();
            var digits = Utilities.FindAllEntitiesByDesignerName<CDynamicProp>("prop_dynamic").Where(e => e.Entity?.Name?.StartsWith("ze_commander_digit_") == true).ToArray();
            Record("all-five-markers-unlimited-glow", icons.Length == 5 && icons.All(e => e.Glow.GlowType == 3 && e.Glow.GlowRange == 0 && e.Glow.GlowTeam == -1 && e.Glow.Glowing));
            if (Prop<bool>(Field(service,"_countdown"),"ModelResourcesReady"))
            {
            Record("countdown-uses-resident-glowing-models", digits.Length == 30 && digits.All(e => e.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.Model.Value != 0 && e.Glow.GlowType == 3 && e.Glow.Glowing && e.Glow.GlowRange == 0), new { digits = digits.Length });
            Record("countdown-compiled-X-is-up-above-points", digits.All(e => Math.Abs(e.AbsRotation!.X + 90) < .01 && Math.Abs(e.AbsRotation.Z) < .01 && Math.Abs(e.AbsOrigin!.Z - (anchor.Z + 22 + 38)) < .01));
            Record("five-exact-countdown-colors", digits.Select(e => e.Glow.GlowColorOverride.ToArgb()).Distinct().Count() == 5);
            Record("old-flat-worldtext-removed", !Utilities.FindAllEntitiesByDesignerName<CPointWorldText>("point_worldtext").Any(e => e.Entity?.Name?.StartsWith("ze_commander_countdown_") == true));
            icons[1].Glow.GlowType = 0; digits[4].Glow.GlowType = 0;
            var repairIcon = icons[1]; var repairDigit = digits[4];
            Later(.3f, () => Record("marker-and-number-overwritten-glow-repaired", repairIcon.IsValid && repairIcon.Glow.GlowType == 3 && repairDigit.IsValid && repairDigit.Glow.GlowType == 3));
            }
            else
            {
                var texts = Utilities.FindAllEntitiesByDesignerName<CPointWorldText>("point_worldtext").Where(e=>e.Entity?.Name?.StartsWith("ze_commander_pending_")==true).ToArray();
                Record("unapproved-bundle-never-spawns-missing-digit-models", digits.Length == 0);
                Record("pending-countdown-retains-large-readable-text", texts.Length == 15 && texts.All(e=>e.FontSize==48 && e.WorldUnitsPerPx==1 && e.Fullbright));
                Record("pending-countdown-standing-above-points", texts.All(e=>Math.Abs(e.AbsRotation!.Z-90)<.01 && Math.Abs(e.AbsOrigin!.Z-(anchor.Z+22+38))<.01));
                Record("pending-countdown-five-colors", texts.Select(e=>e.Color.ToArgb()).Distinct().Count()==5);
                icons[1].Glow.GlowType=0; var repairIcon=icons[1];
                Later(.3f,()=>Record("pending-marker-overwritten-glow-repaired",repairIcon.IsValid&&repairIcon.Glow.GlowType==3));
            }

            Record("five-markers-five-colors", markers.Length == 5 && markers.Select(m => Prop<int>(m,"ColorIndex")).Distinct().Count() == 5);
            Call(service, "Release", false);
            Record("leave-keeps-markers-and-removes-glow", Markers(service).Length == 5 && Prop<CDynamicProp?>(leaderGlow, "Outline") == null && Prop<CDynamicProp?>(leaderGlow, "Relay") == null);
            Later(.15f, () => Record("leave-proxy-entities-actually-deleted", !outline.IsValid && !relay.IsValid));
            Record("successor-acquired", (bool)Call(service, "TryAcquire", b)!);
            b.PlayerPawn.Value!.EyeAngles.X = -89;
            Call(service, "ProcessPing", b, anchor + new Vector3(-200, -200, 0));
            markers = Markers(service);
            Record("successor-sixth-evicts-original-oldest", markers.Length == 5 && markers.All(m => Id(m) != oldest));
            var chosen = markers[^1]; var pos = Prop<Vector3>(chosen,"Position");
            var pawn = b.PlayerPawn.Value;
            var eye = new Vector3(pawn.AbsOrigin!.X + pawn.ViewOffset.X, pawn.AbsOrigin.Y + pawn.ViewOffset.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
            var delta = pos-eye;
            pawn.EyeAngles.X = -MathF.Atan2(delta.Z, MathF.Sqrt(delta.X*delta.X + delta.Y*delta.Y))*180/MathF.PI;
            pawn.EyeAngles.Y = MathF.Atan2(delta.Y,delta.X)*180/MathF.PI;
            double before = markers.Sum(m => Prop<double>(m,"ExpiresAt"));
            var ping = new EventPlayerPing(false); ping.Userid=b; ping.X=anchor.X; ping.Y=anchor.Y; ping.Z=anchor.Z; ping.Entityid=0; ping.FireEvent(false);
            Record("engine-ping-extends-existing-ten-seconds", Markers(service).Length == 5 && Math.Abs(Markers(service).Sum(m => Prop<double>(m,"ExpiresAt"))-before-10)<.1);
            var drawing = Field(service, "_drawing");
            pawn.EyeAngles.X = 65;
            var hit = (Vector3?)Call(drawing, "TraceAim", b);
            Record("native-crosshair-ray-hits-world", hit != null, hit?.ToString());
            for (int i = 0; i < 4; i++) Call(drawing, "AddSample", hit!.Value + new Vector3(i * 12, 0, 0));
            Call(drawing, "Finish");
            var beams = Utilities.FindAllEntitiesByDesignerName<CBeam>("env_beam").Where(e => e.Entity?.Name?.StartsWith("ze_commander_line_") == true).ToArray();
            Record("three-cyan-beams-with-loaded-material", beams.Length == 3 && beams.All(e => e.Width == 4 && e.Render.B == 255 && e.EndPos != null && e.BaseMaterial.Value != 0 && e.Damage == 0),
                beams.Select(e => new { index=e.Index, width=e.Width, type=e.BeamType, materialValue=e.BaseMaterial.Value, hdr=e.HDRColorScale }).ToArray());
            var beamState = beams.Select(e => new { beam=e, start=e.AbsOrigin!.ToString(), end=e.EndPos.ToString() }).ToArray();
            Call(service, "Release", false);
            Record("leave-keeps-lines-and-points", Markers(service).Length == 5 && Prop<int>(drawing,"Count") == 3);
            var players = Utilities.GetPlayers().Where(p => p.IsBot && p.PawnIsAlive).ToArray();
            Record("both-teams-use-luna", players.All(p => p.PlayerPawn.Value?.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName == "agents/fys/luna/luna.vmdl"), Call(models,"Diagnostics"));
            Record("luna-model-handles-resident", players.All(p => p.PlayerPawn.Value!.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.Model.Value != 0));
            Later(1, () => Record("beam-material-endpoints-stable-after-engine-tick", beamState.All(e => e.beam.IsValid && e.beam.BaseMaterial.Value != 0 &&
                !e.beam.TurnedOff && e.beam.Damage == 0 && e.beam.BeamType == BeamType_t.BEAM_POINTS && e.beam.AbsOrigin!.ToString() == e.start && e.beam.EndPos.ToString() == e.end)));
            Later(4, () => Record("lines-retain-full-ten-second-lifetime", Prop<int>(drawing,"Count") == 3 && beams.All(e => e.IsValid)));
            Later(10.5f, () => {
                Record("lines-expired-after-ten-seconds", Prop<int>(drawing,"Count") == 0);
                Record("unextended-points-expired", Markers(service).Length == 1);
            });
            Later(20.5f, () => {
                Record("extended-points-expired-without-leader", Markers(service).Length == 0);
                Record("countdown-models-cleaned-after-expiry", !Utilities.FindAllEntitiesByDesignerName<CDynamicProp>("prop_dynamic").Any(e => e.Entity?.Name?.StartsWith("ze_commander_digit_") == true));
                Record("pending-countdown-text-cleaned-after-expiry", !Utilities.FindAllEntitiesByDesignerName<CPointWorldText>("point_worldtext").Any(e=>e.Entity?.Name?.StartsWith("ze_commander_pending_")==true));

                Record("no-commander-errors", !Call(service,"Diagnostics")!.ToString()!.Contains("\"errors\":[\""), Call(service,"Diagnostics"));
                Record("reacquire-for-reload-button-test", (bool)Call(service,"TryAcquire",b)!);
                var input=pawn.MovementServices!.Buttons;
                ulong previous=input.ButtonStates[0];
                try
                {
                    input.ButtonStates[0]=0;
                    SetField(drawing,"_next",0f); Call(drawing,"Tick",b);
                    Record("no-reload-button-no-line", Prop<int>(drawing,"Count")==0);
                    input.ButtonStates[0]=(ulong)PlayerButtons.Reload;
                    pawn.EyeAngles.X=65;
                    for(int i=0;i<4;i++)
                    {
                        pawn.EyeAngles.Y=i*15;
                        SetField(drawing,"_next",0f); Call(drawing,"Tick",b);
                    }
                    Record("native-reload-button-path-draws", Prop<int>(drawing,"Count")>0, Call(drawing,"Diagnostics"));
                    input.ButtonStates[0]=0;
                    SetField(drawing,"_next",0f); Call(drawing,"Tick",b);
                    Record("reload-segments-have-finite-expiry", ((IEnumerable)Field(drawing,"_state").GetType().GetProperty("Segments")!.GetValue(Field(drawing,"_state"))!).Cast<object>().All(s=>double.IsFinite(Prop<double>(s,"ExpiresAt"))));
                }
                finally { input.ButtonStates[0]=previous; Call(service,"OnRoundStart"); }
                // Stagger actual server-time segment creation to verify the corrected fade direction.
                void Segment(float offset)
                {
                    Call(drawing,"AddSample",hit!.Value+new Vector3(offset,0,0));
                    Call(drawing,"AddSample",hit!.Value+new Vector3(offset+12,0,0));
                    Call(drawing,"Finish");
                }
                Segment(0);
                var firstTrail=Utilities.FindAllEntitiesByDesignerName<CBeam>("env_beam").Single(e=>e.Entity?.Name?.StartsWith("ze_commander_line_")==true);
                Later(1,()=>Segment(24));
                Later(2,()=>Segment(48));
                Later(3,()=>Record("staggered-lines-all-present-before-expiry",Prop<int>(drawing,"Count")==3));
                Later(10.5f,()=>Record("oldest-line-expires-first",Prop<int>(drawing,"Count")==2 && !firstTrail.IsValid));
                Later(11.5f,()=>Record("newest-line-remains-last",Prop<int>(drawing,"Count")==1));
                Later(12.5f,()=>{
                    Record("staggered-trail-completely-expired",Prop<int>(drawing,"Count")==0);
                    Server.PrintToConsole("COMMANDER_PROBE COMPLETE");
                });
            });
        });
    }
}
