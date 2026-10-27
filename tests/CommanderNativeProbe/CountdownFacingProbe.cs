using System.Collections;
using System.Drawing;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

public sealed partial class CommanderNativeProbe
{
    private void RegisterCountdownFacingProbe()
    {
        AddCommand("css_countdown_hotload_probe", "Isolated real hot reload readiness regression", (caller, command) =>
        {
            if(caller!=null)return;Guard();_results.Clear();
            var service=Field(Plugin(),"_commander");
            bool Ready()=>Prop<bool>(Field(service,"_countdown"),"ModelResourcesReady");
            string Status()=>Prop<string>(Field(service,"_resources"),"Status");
            CDynamicProp[] Probes()=>Utilities.FindAllEntitiesByDesignerName<CDynamicProp>("prop_dynamic")
                .Where(p=>p.Entity?.Name=="ze_commander_hotload_model_check").ToArray();
            Record("real-css-hot-reload-restores-countdown-ready",Ready());
            Record("real-css-hot-reload-verified-currently-mounted-models",Status()=="verified-mounted-models-after-hot-reload");
            Record("hot-reload-leaves-no-validation-entities",Probes().Length==0);
            Call(service,"OnHotReload");
            var transient=Probes();
            Record("validation-entities-hidden-and-nonsolid",transient.All(p=>(p.Effects&32)!=0&&p.Collision.SolidType==SolidType_t.SOLID_NONE));
            Record("repeat-readiness-check-is-idempotent",Ready()&&Status()=="verified-mounted-models-after-hot-reload");
            Later(.25f,()=>{
                Record("repeat-check-cleans-native-entities",Probes().Length==0);
                Record("readiness-survives-following-ticks",Ready());
                Record("hot-reload-no-service-errors",((IEnumerable)Field(service,"_errors")).Cast<object>().Count()==0);
                Server.PrintToConsole("COUNTDOWN_HOTLOAD_PROBE COMPLETE");
            });
        });
        AddCommand("css_countdown_mount_probe", "Isolated mounted-model bounds check", (caller, command) =>
        {
            if(caller!=null)return;Guard();_results.Clear();
            var props=new List<CDynamicProp>();
            try
            {
                for(int digit=0;digit<10;digit++)
                {
                    string model=$"models/saibochana/commander/digits/{digit}.vmdl";
                    var prop=Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic")!;props.Add(prop);
                    using var kv=new CEntityKeyValues();kv.SetString("model",model);kv.SetString("targetname","ze_countdown_mount_probe");
                    kv.SetInt("solid",0);kv.SetInt("spawnflags",256);kv.SetInt("StartDisabled",1);kv.SetInt("rendermode",10);
                    prop.DispatchSpawn(kv);
                    var state=prop.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState;
                    Record("mounted-digit-"+digit,state.Model.Value!=0,new{model=state.ModelName,handle=state.Model.Value,
                        min=prop.Collision.Mins.ToString(),max=prop.Collision.Maxs.ToString(),effects=prop.Effects});
                }
            }
            finally{foreach(var prop in props)if(prop.IsValid)prop.Remove();}
        });
        AddCommand("css_countdown_facing_probe", "Isolated actual countdown geometry/rotation/lifecycle fixture", (caller, command) =>
        {
            if (caller != null) return; Guard(); _results.Clear();
            object? countdown=null;
            var viewers=Utilities.GetPlayers().Where(p=>p.IsBot&&p.PawnIsAlive).Take(2).ToArray();
            var positions=viewers.Select(p=>(Vector3)p.PlayerPawn.Value!.AbsOrigin!).ToArray();
            try
            {
                Record("facing-fixture-two-real-engine-bots",viewers.Length==2);
                var assembly=Plugin().GetType().Assembly;
                var service=Field(Plugin(),"_commander");
                Record("facing-actual-installed-workshop-ready",Prop<bool>(Field(service,"_countdown"),"ModelResourcesReady"));
                var type=assembly.GetType("ZePractice.CommanderCountdown")!;
                countdown=Activator.CreateInstance(type)!;type.GetProperty("ModelResourcesReady")!.SetValue(countdown,true);
                var markerType=assembly.GetType("ZePractice.CommanderMarkerState+Marker")!;
                var markerPos=positions[0]+new Vector3(200,100,20);
                object Marker(int seconds,long id=900001)=>Activator.CreateInstance(markerType,id,0,markerPos,(double)Server.CurrentTime,(double)Server.CurrentTime+seconds)!;
                object Label(CCSPlayerController p,long id=900001)=>((IDictionary)Field(countdown,"_labels"))[(p.EntityHandle.Raw,id)]!;
                CDynamicProp[] Glyphs(CCSPlayerController p)=>((IEnumerable)Label(p).GetType().GetField("Glyphs")!.GetValue(Label(p))!).Cast<CDynamicProp>().ToArray();
                static string Model(CDynamicProp prop)=>prop.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName;
                static Vector3 Rotate(Vector3 v,Vector3 angles)
                {
                    float p=angles.X*MathF.PI/180,y=angles.Y*MathF.PI/180,r=angles.Z*MathF.PI/180;
                    float cp=MathF.Cos(p),sp=MathF.Sin(p),cy=MathF.Cos(y),sy=MathF.Sin(y),cr=MathF.Cos(r),sr=MathF.Sin(r);
                    return v.X*new Vector3(cp*cy,cp*sy,-sp)+v.Y*new Vector3(sr*sp*cy-cr*sy,sr*sp*sy+cr*cy,sr*cp)+v.Z*new Vector3(cr*sp*cy+sr*sy,cr*sp*sy-sr*cy,cr*cp);
                }
                foreach(float yaw in new[]{0f,45,90,135,180,225,270,315})
                {
                    float angle=yaw*MathF.PI/180;var normal=new Vector3(MathF.Cos(angle),MathF.Sin(angle),0);var right=new Vector3(-normal.Y,normal.X,0);
                    viewers[0].PlayerPawn.Value!.Teleport(markerPos+normal*500,new Vector3(0,yaw+180,0),Vector3.Zero);
                    Call(countdown,"UpdateLabel",viewers[0],Marker(10),Color.Red);
                    var props=Glyphs(viewers[0]);
                    Record("two-digit-initial-ten-at-"+yaw,props.Length==2&&Model(props[0]).EndsWith("/1.vmdl")&&Model(props[1]).EndsWith("/0.vmdl"));
                    foreach(var prop in props)
                    {
                        var rotation=(Vector3)prop.AbsRotation!;
                        Record("engine-compiled-up-front-right-"+yaw+"-"+prop.Index,
                            Vector3.Distance(Rotate(Vector3.UnitX,rotation),Vector3.UnitZ)<.001&&
                            Vector3.Distance(Rotate(Vector3.UnitY,rotation),normal)<.001&&
                            Vector3.Distance(Rotate(Vector3.UnitZ,rotation),right)<.001,
                            new{rotation=rotation.ToString(),up=Rotate(Vector3.UnitX,rotation).ToString(),front=Rotate(Vector3.UnitY,rotation).ToString(),right=Rotate(Vector3.UnitZ,rotation).ToString()});
                        Record("resident-and-glowing-"+yaw+"-"+prop.Index,prop.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.Model.Value!=0&&prop.Glow.GlowType==3&&prop.Glow.GlowRange==0&&prop.Glow.Glowing);
                    }
                    var first=(Vector3)props[0].AbsOrigin!;var last=(Vector3)props[1].AbsOrigin!;
                    Record("engine-left-to-right-centered-above-icon-"+yaw,Vector3.Distance(last-first,right*30)<.001&&Vector3.Distance((first+last)/2,markerPos+new Vector3(0,0,38))<.001);
                }
                var before=Glyphs(viewers[0]);var removedLeading=before[1];uint reused=before[0].EntityHandle.Raw;
                Call(countdown,"UpdateLabel",viewers[0],Marker(9),Color.Red);
                Record("engine-10-to-9-recenters-and-reuses-first-entity",Glyphs(viewers[0]).Length==1&&Glyphs(viewers[0])[0].EntityHandle.Raw==reused&&Model(Glyphs(viewers[0])[0]).EndsWith("/9.vmdl")&&Vector3.Distance((Vector3)Glyphs(viewers[0])[0].AbsOrigin!,markerPos+new Vector3(0,0,38))<.001);
                foreach(int digit in Enumerable.Range(0,10))
                {
                    Call(countdown,"UpdateLabel",viewers[0],Marker(digit),Color.Red);
                    var prop=Glyphs(viewers[0]).Single();
                    Record("engine-each-digit-resource-"+digit,Model(prop).EndsWith("/"+digit+".vmdl")&&prop.EntityHandle.Raw==reused&&Math.Abs(prop.AbsRotation!.X+90)<.001);
                }
                Call(countdown,"UpdateLabel",viewers[0],Marker(19),Color.Red);
                Record("engine-extension-9-to-19-adds-second-digit",Glyphs(viewers[0]).Length==2&&Model(Glyphs(viewers[0])[0]).EndsWith("/1.vmdl")&&Model(Glyphs(viewers[0])[1]).EndsWith("/9.vmdl"));
                viewers[1].PlayerPawn.Value!.Teleport(markerPos+new Vector3(500,0,0),Vector3.Zero,Vector3.Zero);
                Call(countdown,"UpdateLabel",viewers[1],Marker(19),Color.Red);
                Record("engine-different-viewers-have-independent-facing-copies",Prop<int>(countdown,"Count")==2&&Glyphs(viewers[0])[0].EntityHandle.Raw!=Glyphs(viewers[1])[0].EntityHandle.Raw&&Math.Abs(Glyphs(viewers[0])[0].AbsRotation!.Y-Glyphs(viewers[1])[0].AbsRotation!.Y)>20);
                var all=Glyphs(viewers[0]).Concat(Glyphs(viewers[1])).Append(removedLeading).ToArray();
                Call(countdown,"Reset",true);
                Record("engine-countdown-reset-clears-bookkeeping",Prop<int>(countdown,"Count")==0&&Prop<int>(countdown,"EntityCount")==0);
                Later(.25f,()=>{
                    Record("engine-countdown-removes-native-props-next-frame",all.All(p=>!p.IsValid));
                    Server.PrintToConsole("COUNTDOWN_FACING_PROBE COMPLETE (native state, not a client screenshot)");
                });
            }
            catch(Exception error)
            {
                File.WriteAllText(Path.Combine(ModuleDirectory,"facing-error.txt"),error.ToString());
                throw;
            }
            finally
            {
                if(countdown!=null)Call(countdown,"Reset",true);
                for(int i=0;i<viewers.Length;i++)if(viewers[i].PlayerPawn.Value is {IsValid:true} pawn)pawn.Teleport(positions[i],null,Vector3.Zero);
            }
        });
    }
}
