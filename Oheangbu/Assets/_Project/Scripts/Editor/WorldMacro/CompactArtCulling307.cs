using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #307 phase 2 checks (Tools/Unity/Plan307/PERF_DESIGN.md 2a/2b/2e, SPEC-ART-RENDERER-CULLING S8.4). Queue entry:
    //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.ArtCull307 Run "<command>"
    // Commands (every refusal is a returned "refused: ..." string; no dialog, no scene or asset change, nothing saved):
    //   artcull                     the existing S3 oracle (3 legacy sheets), unchanged; forced onto the S3 path
    //   artcull2[:sheets=scene|all][:poses=120] S8 optimized vs brute-force multisets per batch and shadow mode, subset/reason proof against the
    //                               S3 full scan, the S3 oracle for the same sheets/poses (2a), worldBounds containment, and
    //                               Camera.Render accounting. scene = sheets of the loaded scenes' renderers (default), all = + legacy 3;
    //                               poses = seeded artcull poses kept per sheet (evenly thinned; station poses always added)
    //   grasscull                   every CompactGrassRenderer266 in the loaded scenes: classified packets vs the per-seed loop
    //   artpixel[:tier=current|pc|mobile|both][:w=1920][:h=1080][:png=0|1]
    //                               fixed poses rendered to a RenderTexture with ContactPreviewClock265/WindPreviewClock267 frozen:
    //                               S3 render, S8 render, S3 again (noise), S3 through the pre-2a DrawMeshInstanced submission (art and
    //                               grass, 2a/2e API identity); byte comparison. Also fails when a shadowed point/spot light's range
    //                               exceeds a sheet's ShadowPunctualReach307 (the S8 caster-margin precondition). Run it in Play (paused is fine) for a
    //                               fixed shader time: in Edit mode URP feeds _Time from the real-time clock, which can show as NOISY.
    //   shadowbounds[:station=<routes id>][:frames=30] | shadowbounds:status | shadowbounds:abort
    //                               Play only (not paused), multi-frame: an extra camera at one station renders tight chunk bounds / whole-sheet
    //                               bounds / S3 path in turn; ProfilerRecorder "Shadow Casters Count" etc. per mode (S8.4.5 experiment).
    // Output: Art/Performance/Perf307/ArtCull/. IMPLEMENTED (offline compile only), not validated.
    public static class ArtCull307
    {
        public static string Run(string arg)
        {
            arg=(arg??"").Trim();
            if(EditorApplication.isCompiling||EditorApplication.isUpdating)return "refused: editor is compiling or importing";
            if(Perf307Active())return "refused: a Perf307 run is active (status/abort it first)";
            string command=arg.Split(':')[0];var options=Options(arg);
            try
            {
                switch(command)
                {
                    case "artcull":return CompactRebuildAuthoring.ArtCullingLegacy307();
                    case "artcull2":return CompactRebuildAuthoring.ArtCulling2_307(Option(options,"sheets","scene"),Math.Max(8,Int(options,"poses",120)));
                    case "grasscull":return GrassCull();
                    case "bandpixel":return BandPixel(Int(options,"w",960),Int(options,"h",540),Int(options,"offsets",3));
                    case "artpixel":foreach(var a in Loaded<CompactRebuildArtRenderer>())a.NativePlaneTest307=Option(options,"native","0")=="1";pixelWhole=Option(options,"whole","0")=="1";pixelSeen=Option(options,"seen","0")=="1"?1:0;{string l=Option(options,"limit","");pixelLimit=l=="inf"?float.PositiveInfinity:float.TryParse(l,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float lv)?lv:-1;}return ArtPixel(Option(options,"tier","current"),Int(options,"w",1920),Int(options,"h",1080),Option(options,"png","0")=="1");
                    case "shadowbounds":return ShadowBounds307.Command(arg.Length>13?arg.Substring(13):"",options);
                    default:return "refused: unknown command '"+command+"' (artcull | artcull2 | grasscull | artpixel | shadowbounds)";
                }
            }
            catch(Exception e){return "refused: "+command+" failed: "+e.GetType().Name+": "+e.Message;}
        }
        internal static bool Perf307Active()
        {
            var json=SessionState.GetString("Perf307.State","");
            return json.Contains("\"Active\":true");
        }
        static Dictionary<string,string> Options(string arg)
        {
            var o=new Dictionary<string,string>();
            foreach(var part in arg.Split(':').Skip(1)){int eq=part.IndexOf('=');if(eq>0)o[part.Substring(0,eq).Trim()]=part.Substring(eq+1).Trim();else if(part.Length>0)o[part.Trim()]="";}
            return o;
        }
        internal static string Option(Dictionary<string,string> o,string key,string fallback)=>o.TryGetValue(key,out var v)&&v.Length>0?v:fallback;
        internal static int Int(Dictionary<string,string> o,string key,int fallback)=>o.TryGetValue(key,out var v)&&int.TryParse(v,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n)?n:fallback;
        internal static string OutFolder
        {
            get{var path=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","Art","Performance","Perf307","ArtCull"));Directory.CreateDirectory(path);return path;}
        }
        internal static string Stamp=>DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'",CultureInfo.InvariantCulture);
        internal static string F(float v,string f="F2")=>v.ToString(f,CultureInfo.InvariantCulture);
        internal static string V(Vector3 v)=>"("+F(v.x,"F1")+","+F(v.y,"F1")+","+F(v.z,"F1")+")";
        internal static List<T> Loaded<T>() where T:Component
        {
            var list=new List<T>();
            for(int s=0;s<SceneManager.sceneCount;s++){var scene=SceneManager.GetSceneAt(s);if(!scene.isLoaded)continue;foreach(var root in scene.GetRootGameObjects())list.AddRange(root.GetComponentsInChildren<T>(true));}
            return list;
        }
        internal static bool AnyDirty(){for(int s=0;s<SceneManager.sceneCount;s++)if(SceneManager.GetSceneAt(s).isDirty)return true;return false;}

        // ---- station poses (Art/Performance/Perf307/routes.json; only the fields these checks read) ----
        [Serializable] sealed class Routes {public Station[] stations=Array.Empty<Station>();}
        [Serializable] sealed class Station {public string id="",source="fixed",sourceId="";public Vector3 feet;public Vector2 near;public float baseYaw,inset=20;public bool yawRelative;public float[] poseYaws={0f,180f};}
        internal struct Pose {public string Label;public Vector3 Eye;public Quaternion Rotation;}
        internal static List<Pose> StationPoses(string only,out string note)
        {
            var poses=new List<Pose>();var path=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","Art","Performance","Perf307","routes.json"));
            if(!File.Exists(path)){note="routes.json missing ("+path+")";return poses;}
            var routes=JsonUtility.FromJson<Routes>(File.ReadAllText(path));var notes=new List<string>();
            var arts=Loaded<CompactRebuildArtRenderer>();
            foreach(var s in routes.stations??Array.Empty<Station>())
            {
                if(!string.IsNullOrEmpty(only)&&s.id!=only)continue;
                Vector3 feet=s.feet;float yaw0=s.baseYaw;
                if(s.source=="forest305-edge")
                {
                    var source=arts.FirstOrDefault(a=>a.Sheet!=null&&(a.name==s.sourceId||a.Sheet.name==s.sourceId));
                    if(source==null||source.Sheet.FixedPlacements.Length==0){notes.Add(s.id+": renderer "+s.sourceId+" missing");continue;}
                    var at=source.Sheet.FixedPlacements.OrderBy(p=>new Vector2(p.Position.x-s.near.x,p.Position.z-s.near.y).sqrMagnitude).First().Position;
                    var dir=new Vector3(s.near.x-at.x,0,s.near.y-at.z);dir=dir.sqrMagnitude>.01f?dir.normalized:Vector3.forward;
                    feet=at+dir*s.inset;yaw0=Quaternion.LookRotation(-dir).eulerAngles.y;
                }
                if(feet==Vector3.zero){notes.Add(s.id+": no feet (source "+s.source+")");continue;}
                if(Physics.Raycast(feet+Vector3.up*3,Vector3.down,out var hit,30,~0,QueryTriggerInteraction.Ignore))feet=hit.point;
                var eye=feet+Vector3.up*1.65f;
                foreach(float y in s.poseYaws??new[]{0f})
                {
                    float yaw=(s.yawRelative||s.source=="forest305-edge"?yaw0:0)+y;
                    poses.Add(new Pose{Label=s.id+" yaw "+F(yaw,"F0"),Eye=eye,Rotation=Quaternion.Euler(0,yaw,0)});
                }
                float first=(s.yawRelative||s.source=="forest305-edge"?yaw0:0)+(s.poseYaws!=null&&s.poseYaws.Length>0?s.poseYaws[0]:0);
                poses.Add(new Pose{Label=s.id+" high",Eye=eye+Vector3.up*25,Rotation=Quaternion.Euler(22,first,0)});
            }
            note=notes.Count==0?"":string.Join("; ",notes);return poses;
        }

        // ---- grasscull ----
        static string GrassCull()
        {
            var renderers=Loaded<CompactGrassRenderer266>().Where(g=>g.Field!=null&&g.Art!=null).ToList();
            if(renderers.Count==0)return "refused: no CompactGrassRenderer266 with a Field and Art in the loaded scenes";
            bool dirtyBefore=AnyDirty();var lines=new List<string>{"#307 grasscull "+DateTime.Now.ToString("s")+" | Unity "+Application.unityVersion+" | play "+Application.isPlaying,
                "Oracle: ComparePackets307 = the classified Collect vs the former per-seed loop, same eye/planes; packets compared in order (near/far, fade, count, matrix bits) plus counters."};
            var go=new GameObject("GrassCull307_camera"){hideFlags=HideFlags.HideAndDontSave};long packets=0;int mismatches=0,poseCount=0;
            try
            {
                var camera=go.AddComponent<Camera>();camera.enabled=false;var stations=StationPoses(null,out string note);
                foreach(var grass in renderers)
                {
                    var field=grass.Field;var observer=grass.Art.Observer;
                    float fov=observer!=null?observer.fieldOfView:60,near=observer!=null?observer.nearClipPlane:.3f,far=observer!=null?observer.farClipPlane:1000;
                    var random=new System.Random(20260930);var seeded=Enumerable.Range(0,field.Cells.Length).Where(i=>field.Cells[i]!=null&&field.Cells[i].Seeds.Length>0).ToArray();
                    var poses=new List<Pose>(stations);
                    float R(float a,float b)=>a+(float)random.NextDouble()*(b-a);
                    for(int i=0;i<200&&seeded.Length>0;i++)
                    {
                        var cell=field.Cells[seeded[random.Next(seeded.Length)]];var seed=cell.Seeds[random.Next(cell.Seeds.Length)].Position;
                        float spread=i<120?30:i<170?field.FarDistance:4;  // near field, the far/limit ring, right on a seed
                        poses.Add(new Pose{Label=i<120?"ground":i<170?"ring":"on-seed",Eye=seed+new Vector3(R(-spread,spread),R(.2f,i<170?6:2),R(-spread,spread)),Rotation=Quaternion.Euler(R(-25,20),R(0,360),0)});
                    }
                    int bad=0;long count=0;var ms=new List<double>();var reference=new List<double>();string first=null;
                    foreach(var pose in poses)
                    {
                        camera.transform.SetPositionAndRotation(pose.Eye,pose.Rotation);camera.fieldOfView=fov;camera.nearClipPlane=near;camera.farClipPlane=far;camera.aspect=16f/9;
                        var r=grass.ComparePackets307(camera);poseCount++;count+=r.Packets;ms.Add(r.Ms);reference.Add(r.ReferenceMs);
                        if(r.Mismatches>0){bad+=r.Mismatches;if(first==null)first=pose.Label+" at "+V(pose.Eye)+": "+r.First;}
                    }
                    mismatches+=bad;packets+=count;
                    lines.Add("");lines.Add("grass "+grass.name+" field "+AssetDatabase.GetAssetPath(field)+" cells "+field.Cells.Length+" near/far "+F(field.NearDistance)+"/"+F(field.FarDistance));
                    lines.Add(" poses="+poses.Count+(note.Length>0?" (stations: "+note+")":"")+" packets compared="+count+" mismatches="+bad+(first!=null?" FIRST: "+first:""));
                    lines.Add(" Collect ms  classified: "+Stats(ms)+" | per-seed: "+Stats(reference));
                }
            }
            finally{Object.DestroyImmediate(go);}
            bool dirtyAfter=AnyDirty();bool pass=mismatches==0&&dirtyAfter==dirtyBefore;
            lines.Insert(2,(pass?"PASS":"FAIL")+" renderers="+renderers.Count+" poses="+poseCount+" packets="+packets+" mismatches="+mismatches+" scene dirty "+dirtyBefore+"->"+dirtyAfter);
            File.WriteAllLines(Path.Combine(OutFolder,"grasscull.txt"),lines);
            return lines[2]+" (report: Art/Performance/Perf307/ArtCull/grasscull.txt)";
        }
        internal static string Stats(List<double> values)
        {
            if(values.Count==0)return "n/a";var v=values.OrderBy(x=>x).ToArray();
            return "median="+v[v.Length/2].ToString("F3",CultureInfo.InvariantCulture)+" p95="+v[(int)((v.Length-1)*.95)].ToString("F3",CultureInfo.InvariantCulture)+" max="+v[v.Length-1].ToString("F3",CultureInfo.InvariantCulture)+" n="+v.Length;
        }

        // ---- artpixel ----
        internal static Camera CloneObserver(string name,List<CompactRebuildArtRenderer> arts,out GameObject go)
        {
            var source=arts.Select(a=>a.Observer).FirstOrDefault(c=>c!=null)??Camera.main;
            go=new GameObject(name){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();
            if(source!=null){camera.CopyFrom(source);EditorUtility.CopySerialized(source.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());}
            // #307: CopyFrom also copies the live layer cull distances, which InteriorSight307 sets while the player stands in a sealed cave cell
            camera.layerCullDistances=new float[32];
            camera.enabled=false;camera.targetTexture=null;
            // Comparison device only: URP dithering picks a new blue-noise offset per render, which would read as a difference.
            camera.GetUniversalAdditionalCameraData().dithering=false;
            return camera;
        }
        static string ArtPixel(string tier,int width,int height,bool png)
        {
            var arts=Loaded<CompactRebuildArtRenderer>().Where(a=>a.isActiveAndEnabled&&a.Sheet!=null).ToList();
            if(arts.Count==0)return "refused: no active CompactRebuildArtRenderer with a sheet in the loaded scenes";
            if(width<64||height<64||width>4096||height>4096)return "refused: w/h out of 64..4096";
            var tiers=new List<string>();
            if(tier=="current")tiers.Add("");else if(tier=="both"){tiers.Add("PC");tiers.Add("Mobile");}
            else if(tier=="pc")tiers.Add("PC");else if(tier=="mobile")tiers.Add("Mobile");else return "refused: tier must be current|pc|mobile|both";
            foreach(var t in tiers)if(t.Length>0&&Array.IndexOf(QualitySettings.names,t)<0)return "refused: quality level "+t+" missing";
            var grasses=Loaded<CompactGrassRenderer266>();bool dirtyBefore=AnyDirty();int priorQuality=QualitySettings.GetQualityLevel();
            string folder=Path.Combine(OutFolder,"artpixel-"+Stamp);Directory.CreateDirectory(folder);
            var lines=new List<string>{"#307 artpixel "+DateTime.Now.ToString("s")+" | Unity "+Application.unityVersion+" | "+SystemInfo.graphicsDeviceName+" | play "+Application.isPlaying+(EditorApplication.isPaused?" (paused)":""),
                "Per pose: S3 render A, S8 render B, S3 render C, S3 through the pre-2a DrawMeshInstanced submission D; byte-compared RGBA ("+width+"x"+height+"). NOISY = A != C (time/random driven, inconclusive), FAIL = A == C but B or D differs."};
            // Saved state: every renderer's observer/path/clocks, the Random state, the quality level.
            var observers=arts.Select(a=>a.Observer).ToArray();var paths=arts.Select(a=>a.CullPath307).ToArray();var clocks=arts.Select(a=>a.ContactPreviewClock265).ToArray();
            var winds=grasses.Select(g=>g.WindPreviewClock267).ToArray();var randomState=UnityEngine.Random.state;var active=RenderTexture.active;
            var artLegacy=arts.Select(a=>a.LegacySubmit307).ToArray();var grassLegacy=grasses.Select(g=>g.LegacySubmit307).ToArray();
            // S8 precondition (CasterMargin >= punctual reach): every active shadowed point/spot light's range within the smallest sheet reach.
            float reach=arts.Min(a=>a.Sheet.ShadowPunctualReach307);var punctual=Loaded<Light>().Where(l=>l.isActiveAndEnabled&&(l.type==LightType.Point||l.type==LightType.Spot)&&l.shadows!=LightShadows.None).ToList();
            var longest=punctual.OrderByDescending(l=>l.range).FirstOrDefault();bool reachOk=longest==null||longest.range<=reach;
            lines.Add("punctual shadow lights="+punctual.Count+" longest range="+(longest!=null?F(longest.range)+" ("+longest.name+")":"n/a")+" vs ShadowPunctualReach307 "+F(reach)+(reachOk?" ok":" EXCEEDED (raise the sheet field)"));
            var camera=CloneObserver("ArtPixel307_camera",arts,out var go);
            camera.useOcclusionCulling=false;   // #307 §9: path D (pre-2a DrawMeshInstanced) has no worldBounds to exempt from Umbra; compare without it
            var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);var tex=new Texture2D(width,height,TextureFormat.RGBA32,false);
            int poses=0,fail=0,noisy=0,saved=0;
            try
            {
                camera.targetTexture=rt;camera.aspect=width/(float)height;
                foreach(var a in arts){a.Observer=camera;a.ContactPreviewClock265=3.25f;}
                foreach(var g in grasses)g.WindPreviewClock267=3.25f;
                var list=PixelPoses(arts,out string note);if(note.Length>0)lines.Add("pose notes: "+note);
                foreach(var t in tiers)
                {
                    if(t.Length>0)QualitySettings.SetQualityLevel(Array.IndexOf(QualitySettings.names,t),true);
                    var pipe=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                    lines.Add("");lines.Add("tier "+QualitySettings.names[QualitySettings.GetQualityLevel()]+" pipeline "+(pipe!=null?pipe.name+" shadowDistance "+F(pipe.shadowDistance,"F0")+" cascades "+pipe.shadowCascadeCount+" additional shadows "+pipe.supportsAdditionalLightShadows:"(not URP)"));
                    foreach(var pose in list)
                    {
                        camera.transform.SetPositionAndRotation(pose.Eye,pose.Rotation);
                        var a=Render(camera,arts,grasses,0,false,rt,tex,out string legacyCounts);var b=Render(camera,arts,grasses,1,false,rt,tex,out string screenCounts);
                        var c=Render(camera,arts,grasses,0,false,rt,tex,out _);var d=Render(camera,arts,grasses,0,true,rt,tex,out string oldCounts);
                        Diff(a,c,out int noise,out _);Diff(a,b,out int differ,out int delta);Diff(a,d,out int api,out int apiDelta);poses++;
                        string status=noise>0?"NOISY":differ>0||api>0?"FAIL":"PASS";if(status=="FAIL")fail++;if(status=="NOISY")noisy++;
                        lines.Add(" "+status+" "+pose.Label+" eye "+V(pose.Eye)+" euler "+V(pose.Rotation.eulerAngles)+" | S3 vs S8 pixels "+differ+" maxDelta "+delta+" | pre-2a submit vs S3 pixels "+api+" maxDelta "+apiDelta+" | S3 repeat pixels "+noise+" | S3 "+legacyCounts+" | S8 "+screenCounts+" | pre-2a "+oldCounts);
                        if((png||status!="PASS")&&saved<(png?999:12)){saved++;string stem=Path.Combine(folder,(t.Length>0?t:"current")+"-"+poses.ToString("D3"));Png(a,width,height,stem+"-A.png");Png(b,width,height,stem+"-B.png");Mask(a,b,width,height,stem+"-diff.png");if(api>0){Png(d,width,height,stem+"-D.png");Mask(a,d,width,height,stem+"-diffD.png");}}
                    }
                }
            }
            finally
            {
                if(QualitySettings.GetQualityLevel()!=priorQuality)QualitySettings.SetQualityLevel(priorQuality,true);
                for(int i=0;i<arts.Count;i++){arts[i].Observer=observers[i];arts[i].CullPath307=paths[i];arts[i].ContactPreviewClock265=clocks[i];arts[i].LegacySubmit307=artLegacy[i];}
                for(int i=0;i<grasses.Count;i++){grasses[i].WindPreviewClock267=winds[i];grasses[i].LegacySubmit307=grassLegacy[i];}
                UnityEngine.Random.state=randomState;RenderTexture.active=active;camera.targetTexture=null;
                rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);
            }
            bool dirtyAfter=AnyDirty();bool pass=fail==0&&noisy==0&&poses>0&&reachOk&&dirtyAfter==dirtyBefore;
            lines.Insert(2,(pass?"PASS":fail>0||!reachOk?"FAIL":"INCONCLUSIVE")+" poses="+poses+" fail="+fail+" noisy="+noisy+" punctual reach "+(reachOk?"ok":"EXCEEDED")+" renderers="+arts.Count+" scene dirty "+dirtyBefore+"->"+dirtyAfter+" quality restored "+(QualitySettings.GetQualityLevel()==priorQuality));
            File.WriteAllLines(Path.Combine(folder,"report.txt"),lines);
            return lines[2]+" (report: Art/Performance/Perf307/ArtCull/"+Path.GetFileName(folder)+"/report.txt)";
        }
        // #307 2c identity: per pixel pose and k offsets, collect with the band at a displaced pose (0.9 x band metres, 0.7 x band degrees
        // of yaw), move back to the pose (the renderer must reuse the band lists), and byte-compare with an exact (band 0) collect there.
        static string BandPixel(int width,int height,int offsets)
        {
            var arts=Loaded<CompactRebuildArtRenderer>().Where(a=>a.isActiveAndEnabled&&a.Sheet!=null).ToList();
            if(arts.Count==0)return "refused: no active CompactRebuildArtRenderer";
            if(!Application.isPlaying)return "refused: bandpixel needs Play (band reuse is Play-only)";
            var grasses=Loaded<CompactGrassRenderer266>();
            string folder=Path.Combine(OutFolder,"bandpixel-"+Stamp);Directory.CreateDirectory(folder);
            var lines=new List<string>{"#307 bandpixel "+DateTime.Now.ToString("s")+" | "+width+"x"+height+" | band "+string.Join(",",arts.Select(a=>a.Sheet.CollectBandMetres307+" m/"+a.Sheet.CollectBandDegrees307+" deg").Distinct()),""};
            var observers=arts.Select(a=>a.Observer).ToArray();var paths=arts.Select(a=>a.CullPath307).ToArray();var clocks=arts.Select(a=>a.ContactPreviewClock265).ToArray();
            var winds=grasses.Select(g=>g.WindPreviewClock267).ToArray();var sheets=arts.Select(a=>a.Sheet).Distinct().ToList();var bands=sheets.Select(s=>s.CollectBandMetres307).ToArray();
            var camera=CloneObserver("BandPixel307_camera",arts,out var go);
            var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);var tex=new Texture2D(width,height,TextureFormat.RGBA32,false);
            int poses=0,fail=0,noReuse=0,saved=0;var rng=new System.Random(307);
            try
            {
                camera.targetTexture=rt;camera.aspect=width/(float)height;
                foreach(var a in arts){a.Observer=camera;a.ContactPreviewClock265=3.25f;a.CullPath307=1;}
                foreach(var g in grasses)g.WindPreviewClock267=3.25f;
                foreach(var pose in PixelPoses(arts,out _))
                {
                    for(int k=0;k<offsets;k++)
                    {
                        float band=sheets.Min(s=>s.CollectBandMetres307),deg=sheets.Min(s=>s.CollectBandDegrees307);
                        double ang=rng.NextDouble()*Math.PI*2;var dir=new Vector3((float)Math.Cos(ang),(float)(rng.NextDouble()-.5)*.6f,(float)Math.Sin(ang)).normalized;
                        var yaw=Quaternion.Euler(0,(float)(rng.NextDouble()*2-1)*deg*.7f,0);
                        foreach(var a in arts)a.ForgetCollect307();
                        camera.transform.SetPositionAndRotation(pose.Eye+dir*band*.9f,yaw*pose.Rotation);Shot(camera,rt,tex);   // band collect here
                        int reuse0=arts.Sum(a=>a.BandReuses307);
                        camera.transform.SetPositionAndRotation(pose.Eye,pose.Rotation);var x=Shot(camera,rt,tex);             // reused lists
                        bool reused=arts.Sum(a=>a.BandReuses307)>reuse0;if(!reused)noReuse++;
                        foreach(var s in sheets)s.CollectBandMetres307=0;foreach(var a in arts)a.ForgetCollect307();
                        var y=Shot(camera,rt,tex);                                                                              // exact lists
                        for(int i=0;i<sheets.Count;i++)sheets[i].CollectBandMetres307=bands[i];
                        Diff(x,y,out int differ,out int delta);poses++;
                        string status=!reused?"NOREUSE":differ>0?"FAIL":"PASS";if(status=="FAIL")fail++;
                        lines.Add(" "+status+" "+pose.Label+" offset "+k+" eye "+V(pose.Eye)+" | reused-vs-exact pixels "+differ+" maxDelta "+delta);
                        if(status!="PASS"&&saved<12){saved++;string stem=Path.Combine(folder,"band-"+poses.ToString("D3"));Png(x,width,height,stem+"-reuse.png");Png(y,width,height,stem+"-exact.png");Mask(x,y,width,height,stem+"-diff.png");}
                    }
                }
            }
            finally
            {
                for(int i=0;i<sheets.Count;i++)sheets[i].CollectBandMetres307=bands[i];
                for(int i=0;i<arts.Count;i++){arts[i].Observer=observers[i];arts[i].CullPath307=paths[i];arts[i].ContactPreviewClock265=clocks[i];arts[i].ForgetCollect307();}
                for(int i=0;i<grasses.Count;i++)grasses[i].WindPreviewClock267=winds[i];
                camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);
            }
            lines[1]=(fail==0&&noReuse==0&&poses>0?"PASS":"FAIL")+" comparisons="+poses+" fail="+fail+" noReuse="+noReuse;
            File.WriteAllLines(Path.Combine(folder,"report.txt"),lines);
            return lines[1]+" (report: Art/Performance/Perf307/ArtCull/"+Path.GetFileName(folder)+"/report.txt)";
        }
        static Color32[] Shot(Camera camera,RenderTexture rt,Texture2D tex)
        {
            UnityEngine.Random.InitState(307);camera.Render();
            var active=RenderTexture.active;RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);RenderTexture.active=active;return tex.GetPixels32();
        }
        // artpixel:whole=1 — the S8 render uses whole-sheet worldBounds (isolates the chunk-bounds shadow culling from the S8 lists)
        static bool pixelWhole;static float pixelLimit=-1;static int pixelSeen;   // artpixel:limit=<m|inf> — S8 render with the shadow caster limit replaced
        static Color32[] Render(Camera camera,List<CompactRebuildArtRenderer> arts,List<CompactGrassRenderer266> grasses,int path,bool legacy,RenderTexture rt,Texture2D tex,out string counts)
        {
            foreach(var a in arts){a.CullPath307=path;a.LegacySubmit307=legacy;a.WholeSheetBounds307=pixelWhole&&path==1;a.ShadowLimitOverride307=path==1?pixelLimit:-1;a.ForceSeen307=path==1?pixelSeen:0;}foreach(var g in grasses)g.LegacySubmit307=legacy;
            UnityEngine.Random.InitState(307);camera.Render();
            RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);var pixels=tex.GetPixels32();
            long triangles=0;int calls=0,visible=0,shadowOnly=0;foreach(var a in arts){calls+=a.DrawCalls;triangles+=a.SubmittedTriangles;visible+=a.VisibleInstances;shadowOnly+=a.ShadowOnlyInstances;}
            counts="calls "+calls+" tris "+triangles+" visible "+visible+" shadowOnly "+shadowOnly;return pixels;
        }
        static void Diff(Color32[] a,Color32[] b,out int pixels,out int delta)
        {
            pixels=0;delta=0;
            for(int i=0;i<a.Length;i++)
            {
                var x=a[i];var y=b[i];if(x.r==y.r&&x.g==y.g&&x.b==y.b&&x.a==y.a)continue;pixels++;
                delta=Math.Max(delta,Math.Max(Math.Max(Math.Abs(x.r-y.r),Math.Abs(x.g-y.g)),Math.Max(Math.Abs(x.b-y.b),Math.Abs(x.a-y.a))));
            }
        }
        static void Png(Color32[] pixels,int w,int h,string path){var t=new Texture2D(w,h,TextureFormat.RGBA32,false);try{t.SetPixels32(pixels);t.Apply(false);File.WriteAllBytes(path,t.EncodeToPNG());}finally{Object.DestroyImmediate(t);}}
        static void Mask(Color32[] a,Color32[] b,int w,int h,string path)
        {
            var m=new Color32[a.Length];for(int i=0;i<a.Length;i++){bool same=a[i].r==b[i].r&&a[i].g==b[i].g&&a[i].b==b[i].b&&a[i].a==b[i].a;m[i]=same?new Color32(0,0,0,255):new Color32(255,40,40,255);}
            Png(m,w,h,path);
        }
        // S8.4.4 poses: the routes stations (eye height and a raised view over the shadow ring), lanterns with shadows, the 80 m ring
        // and the live shadow-limit ring around trees, plus seeded ground poses per sheet.
        static List<Pose> PixelPoses(List<CompactRebuildArtRenderer> arts,out string note)
        {
            var poses=StationPoses(null,out note);var random=new System.Random(307);
            float limit=GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipe?pipe.shadowDistance:150;
            foreach(var light in Loaded<Light>().Where(l=>l.isActiveAndEnabled&&(l.type==LightType.Point||l.type==LightType.Spot)&&l.shadows!=LightShadows.None).Take(4))
            {
                var at=light.transform.position;var dir=Quaternion.Euler(0,(float)random.NextDouble()*360,0)*Vector3.forward;var eye=at+dir*6+Vector3.down*.3f;
                poses.Add(new Pose{Label="lantern "+light.name,Eye=eye,Rotation=Quaternion.LookRotation(at+Vector3.down*1.2f-eye)});
            }
            var trees=new List<Vector3>();
            foreach(var a in arts){var kinds=a.Sheet.Prototypes.Where(p=>p.Category==WorldMacroDressingSheetSO.Kind.Tree).Select(p=>p.Id).ToHashSet();trees.AddRange(a.Sheet.FixedPlacements.Where(p=>kinds.Contains(p.PrototypeId)).Select(p=>p.Position));}
            for(int i=0;i<4&&trees.Count>0;i++)
            {
                var tree=trees[random.Next(trees.Count)];var dir=Quaternion.Euler(0,(float)random.NextDouble()*360,0)*Vector3.forward;
                foreach(float d in new[]{78f,82f})  // the 80 m ring: the tree just beside the view (its shadow can fall in view)
                {var eye=tree+dir*d+Vector3.up*1.65f;poses.Add(new Pose{Label="ring80 "+F(d,"F0"),Eye=eye,Rotation=Quaternion.LookRotation(Vector3.Cross(Vector3.up,dir))});}
                foreach(float d in new[]{limit-10,limit+15,limit+60})  // looking at the tree across the shadow limit
                {var eye=tree+dir*d+Vector3.up*8;poses.Add(new Pose{Label="shadowring "+F(d,"F0"),Eye=eye,Rotation=Quaternion.LookRotation(tree-eye)});}
            }
            foreach(var a in arts.Where(a=>a.Sheet.FixedPlacements.Length>0))for(int i=0;i<3;i++)
            {
                var p=a.Sheet.FixedPlacements[random.Next(a.Sheet.FixedPlacements.Length)].Position;
                poses.Add(new Pose{Label="ground "+a.name,Eye=p+new Vector3((float)random.NextDouble()*40-20,1.8f,(float)random.NextDouble()*40-20),Rotation=Quaternion.Euler((float)random.NextDouble()*20-10,(float)random.NextDouble()*360,0)});
            }
            return poses;
        }
    }

    public static partial class CompactRebuildAuthoring
    {
        internal static string ArtCullingLegacy307()=>ArtCulling();
        // `artcull2` (SPEC-ART-RENDERER-CULLING S8.4.1-2, plus S3 / 2a on the same sheets). A hidden, unsaved renderer and camera.
        internal static string ArtCulling2_307(string which,int maxPoses)
        {
            var sheets=new List<(WorldMacroDressingSheetSO sheet,string origin)>();
            foreach(var r in ArtCull307.Loaded<CompactRebuildArtRenderer>())if(r.Sheet!=null&&!sheets.Any(s=>s.sheet==r.Sheet))sheets.Add((r.Sheet,"scene renderer "+r.name));
            if(which=="all")foreach(var path in CullSheets){var s=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(path);if(s!=null&&!sheets.Any(x=>x.sheet==s))sheets.Add((s,"legacy "+path));}
            else if(which!="scene")return "refused: sheets must be scene|all";
            if(sheets.Count==0)return "refused: no sheets (open W_Demo_Main or pass sheets=all)";
            bool dirtyBefore=ArtCull307.AnyDirty();var template=ArtCull307.Loaded<CompactRebuildArtRenderer>().Select(a=>a.Observer).FirstOrDefault(c=>c!=null)??Camera.main;
            var live=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var limits=new List<float>{float.NaN,50,0};if(live==null||Mathf.Abs(live.shadowDistance-150)>.5f)limits.Insert(1,150);
            var lines=new List<string>{"SPEC-ART-RENDERER-CULLING S8 artcull2 "+DateTime.Now.ToString("s")+" | Unity "+Application.unityVersion+" | "+SystemInfo.processorType+" | pipeline "+(live!=null?live.name+" shadowDistance "+ArtCull307.F(live.shadowDistance,"F0"):"(not URP)"),
                "Per pose and shadow limit (live, 150, 50, off): CompareScreenIdentical (optimized vs brute force, multisets per batch x mode; subset/reason proof against the S3 full scan) and CompareWithFullScan (S3, bit/order)."};
            long compared=0,multiset=0,notIn=0,unexplained=0,legacyMismatch=0,bounds=0,visibleMismatch=0;int renderFailures=0,poseCount=0;
            var root=new GameObject("ArtCulling307_check"){hideFlags=HideFlags.HideAndDontSave};root.SetActive(false);
            try
            {
                var camera=root.AddComponent<Camera>();camera.enabled=false;var art=root.AddComponent<CompactRebuildArtRenderer>();
                var stations=ArtCull307.StationPoses(null,out string note);
                foreach(var (sheet,origin) in sheets)
                {
                    root.SetActive(false);art.Sheet=sheet;art.Observer=null;art.CullPath307=1;art.Invalidate();
                    var all=CullPoses(sheet,template,false).Where(p=>!p.Kind.StartsWith("edge",StringComparison.Ordinal)).ToList();var poses=new List<CullPose>();
                    for(int k=0;k<maxPoses&&k<all.Count;k++)poses.Add(all[(int)((long)k*all.Count/Math.Min(maxPoses,all.Count))]);
                    poses.AddRange(CullPoses(sheet,template,false).Where(p=>p.Kind.StartsWith("edge",StringComparison.Ordinal)));  // every edge case
                    float fov=template!=null?template.fieldOfView:60,near=template!=null?template.nearClipPlane:.3f,far=template!=null?template.farClipPlane:1000;
                    foreach(var s in stations)poses.Add(new CullPose{Kind="station",Position=s.Eye,Rotation=s.Rotation,Fov=fov,Near=near,Far=far});
                    poseCount+=poses.Count;
                    long sMultiset=0,sNotIn=0,sUnexplained=0,sLegacy=0,sBounds=0,sVisible=0,kept=0,toShadow=0,toOff=0,dropNo=0,dropLimit=0,entries=0,legacyEntries=0;string first=null;
                    var optimizedMs=new List<double>();var referenceMs=new List<double>();var legacyMs=new List<double>();
                    foreach(var pose in poses)
                    {
                        Apply(camera,pose);
                        var l=art.CompareWithFullScan(camera);sLegacy+=l.Mismatches+(l.CulledVisible!=l.FullVisible?1:0);sBounds+=l.BoundsViolations;legacyMs.Add(l.CulledMs);
                        if((l.Mismatches>0||l.BoundsViolations>0)&&first==null)first="S3 "+Describe(pose)+" mismatches="+l.Mismatches+" bounds="+l.BoundsViolations;
                        foreach(float limit in limits)
                        {
                            var r=art.CompareScreenIdentical(camera,limit);
                            sMultiset+=r.MultisetMismatches;sNotIn+=r.NotInLegacy;sUnexplained+=r.Unexplained;sBounds+=r.BoundsViolations;
                            if(r.OptimizedVisible!=r.ReferenceVisible||r.OptimizedShadowOnly!=r.ReferenceShadowOnly)sVisible++;
                            kept+=r.Kept;toShadow+=r.ToShadowOnly;toOff+=r.ToOff;dropNo+=r.DroppedOffscreenNoShadow;dropLimit+=r.DroppedOffscreenBeyondLimit;entries+=r.On+r.ShadowOnly+r.Off;legacyEntries+=r.Legacy;
                            if(float.IsNaN(limit)){optimizedMs.Add(r.OptimizedMs);referenceMs.Add(r.ReferenceMs);}
                            if((r.MultisetMismatches>0||r.NotInLegacy>0||r.Unexplained>0||r.BoundsViolations>0)&&first==null)first=Describe(pose)+" limit "+(float.IsNaN(limit)?"live":ArtCull307.F(limit,"F0"))+": "+r.First+" (multiset "+r.MultisetMismatches+" notIn "+r.NotInLegacy+" unexplained "+r.Unexplained+" bounds "+r.BoundsViolations+")";
                        }
                    }
                    compared+=entries;multiset+=sMultiset;notIn+=sNotIn;unexplained+=sUnexplained;legacyMismatch+=sLegacy;bounds+=sBounds;visibleMismatch+=sVisible;
                    lines.Add("");lines.Add("sheet "+AssetDatabase.GetAssetPath(sheet)+" ("+origin+") placements="+sheet.FixedPlacements.Length+" poses="+poses.Count+(note.Length>0?" station notes: "+note:""));
                    lines.Add(" S8 entries="+entries+" multiset mismatches="+sMultiset+" not-in-S3="+sNotIn+" unexplained="+sUnexplained+" visible/shadowOnly count mismatches="+sVisible+" | S3 oracle mismatches="+sLegacy+" | worldBounds violations="+sBounds+(first!=null?" FIRST: "+first:""));
                    lines.Add(" S3 submissions="+legacyEntries+": kept "+kept+", On->ShadowsOnly "+toShadow+", On->Off (beyond limit) "+toOff+", dropped offscreen non-shadow "+dropNo+", dropped offscreen beyond limit "+dropLimit+" (sum over limits)");
                    lines.Add(" list building ms (live limit)  S8: "+ArtCull307.Stats(optimizedMs)+" | S8 brute force: "+ArtCull307.Stats(referenceMs)+" | S3 cells: "+ArtCull307.Stats(legacyMs));
                    // Real Draw() through Camera.Render on the S8 path: submissions must match the optimized lists.
                    var target=new RenderTexture(1280,720,24);int renders=0,sheetFailures=0;string renderFirst=null;var draw=new List<double>();
                    try
                    {
                        camera.targetTexture=target;art.Observer=camera;root.SetActive(true);
                        var chosen=poses.Where(p=>p.Kind=="station"||p.Kind=="ground").ToList();
                        for(int k=0;k<chosen.Count&&renders<12;k+=Math.Max(1,chosen.Count/12))
                        {
                            Apply(camera,chosen[k]);camera.Render();
                            int calls=art.DrawCalls,visible=art.VisibleInstances,shadowOnly=art.ShadowOnlyInstances,shadowCalls=art.ShadowOnlyDrawCalls;long triangles=art.SubmittedTriangles;draw.Add(art.LastCpuMs);
                            var r=art.CompareScreenIdentical(camera);
                            if(calls!=r.ExpectedDrawCalls||triangles!=r.ExpectedTriangles||visible!=r.OptimizedVisible||shadowOnly!=r.OptimizedShadowOnly||shadowCalls!=r.ExpectedShadowOnlyDrawCalls)
                            {sheetFailures++;if(renderFirst==null)renderFirst=Describe(chosen[k])+" calls "+calls+"/"+r.ExpectedDrawCalls+" tris "+triangles+"/"+r.ExpectedTriangles+" visible "+visible+"/"+r.OptimizedVisible+" shadowOnly "+shadowOnly+"/"+r.OptimizedShadowOnly;}
                            renders++;
                        }
                    }
                    finally{root.SetActive(false);art.Observer=null;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);}
                    renderFailures+=sheetFailures;
                    lines.Add(" Camera.Render accounting (S8) poses="+renders+" failures="+sheetFailures+(renderFirst!=null?" FIRST: "+renderFirst:"")+" | Draw() LastCpuMs: "+ArtCull307.Stats(draw));
                }
            }
            finally{Object.DestroyImmediate(root);}
            bool dirtyAfter=ArtCull307.AnyDirty();
            bool pass=multiset==0&&notIn==0&&unexplained==0&&legacyMismatch==0&&bounds==0&&visibleMismatch==0&&renderFailures==0&&dirtyAfter==dirtyBefore;
            lines.Insert(2,(pass?"PASS":"FAIL")+" sheets="+sheets.Count+" poses="+poseCount+" S8 entries="+compared+" multiset="+multiset+" not-in-S3="+notIn+" unexplained="+unexplained+" S3 mismatches="+legacyMismatch+" bounds="+bounds+" counts="+visibleMismatch+" render failures="+renderFailures+" scene dirty "+dirtyBefore+"->"+dirtyAfter);
            File.WriteAllLines(Path.Combine(ArtCull307.OutFolder,"artcull2.txt"),lines);
            return lines[2]+" (report: Art/Performance/Perf307/ArtCull/artcull2.txt)";
        }
    }

    // S8.4.5 experiment: does URP's shadow pass honour RenderParams.worldBounds? Play only, one run at a time, state in memory.
    static class ShadowBounds307
    {
        sealed class Mode {public string Name;public int Path;public bool Whole;}
        static readonly string[] RecorderNames={"Shadow Casters Count","Batches Count","Draw Calls Count","SetPass Calls Count","Triangles Count"};
        static bool running;static string result="",progress="";static int step,frame,lastFrame,settle=5,sample=30;
        static List<Mode> modes;static Dictionary<string,List<long>[]> values;static List<CompactRebuildArtRenderer> arts;static List<CompactGrassRenderer266> grasses;
        static Camera[] observers;static int[] paths;static bool[] wholes;static GameObject go;static Camera camera;static RenderTexture rt;static ProfilerRecorderBox[] recorders;static string where="";
        sealed class ProfilerRecorderBox{public Unity.Profiling.ProfilerRecorder Recorder;public string Name;}
        internal static string Command(string rest,Dictionary<string,string> options)
        {
            if(rest=="status")return running?"running: "+progress:string.IsNullOrEmpty(result)?"idle":result;
            if(rest=="abort"){if(!running)return "idle";Finish("ABORTED by command");return result;}
            if(running)return "refused: shadowbounds already running ("+progress+")";
            if(!Application.isPlaying)return "refused: shadowbounds needs Play mode (enter W_Demo_Main Play first; it renders an extra camera for a few seconds)";
            if(EditorApplication.isPaused)return "refused: shadowbounds counts frames; unpause Play first";
            arts=ArtCull307.Loaded<CompactRebuildArtRenderer>().Where(a=>a.isActiveAndEnabled&&a.Sheet!=null).ToList();
            if(arts.Count==0)return "refused: no active CompactRebuildArtRenderer";
            string station=ArtCull307.Option(options,"station","forest_band");sample=Math.Max(5,ArtCull307.Int(options,"frames",30));
            var poses=ArtCull307.StationPoses(station,out string note);
            if(poses.Count==0)return "refused: station "+station+" unresolved "+note;
            grasses=ArtCull307.Loaded<CompactGrassRenderer266>();
            observers=arts.Select(a=>a.Observer).ToArray();paths=arts.Select(a=>a.CullPath307).ToArray();wholes=arts.Select(a=>a.WholeSheetBounds307).ToArray();
            camera=ArtCull307.CloneObserver("ShadowBounds307_camera",arts,out go);rt=new RenderTexture(1920,1080,24);camera.targetTexture=rt;camera.aspect=16f/9;
            camera.transform.SetPositionAndRotation(poses[0].Eye,poses[0].Rotation);camera.enabled=true;where=poses[0].Label+" eye "+ArtCull307.V(poses[0].Eye);
            foreach(var a in arts)a.Observer=camera;
            modes=new List<Mode>();for(int lap=0;lap<2;lap++){modes.Add(new Mode{Name="tight",Path=1});modes.Add(new Mode{Name="whole-sheet",Path=1,Whole=true});modes.Add(new Mode{Name="S3",Path=0});}
            values=new Dictionary<string,List<long>[]>();foreach(var m in modes)if(!values.ContainsKey(m.Name))values[m.Name]=Enumerable.Range(0,RecorderNames.Length+2).Select(_=>new List<long>()).ToArray();
            recorders=RecorderNames.Select(n=>new ProfilerRecorderBox{Name=n,Recorder=Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render,n)}).ToArray();
            step=0;frame=0;lastFrame=Time.frameCount;running=true;result="";SetMode();
            EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=PlayChanged;
            return "started shadowbounds at "+where+" ("+modes.Count+" phases x "+(settle+sample)+" frames); poll shadowbounds:status";
        }
        // A script reload mid-run drops this class's state: remove the hidden station camera it would otherwise leave rendering.
        // (Renderers keep pointing at it until Play ends, when the scene reverts their Observer.)
        [InitializeOnLoadMethod]
        static void CleanupAfterReload()
        {
            foreach(var c in Resources.FindObjectsOfTypeAll<Camera>())
                if(c!=null&&c.gameObject.name=="ShadowBounds307_camera"&&c.gameObject.hideFlags==HideFlags.HideAndDontSave&&!EditorUtility.IsPersistent(c)){var t=c.targetTexture;c.targetTexture=null;if(t!=null){t.Release();Object.DestroyImmediate(t);}Object.DestroyImmediate(c.gameObject);}
        }
        static void SetMode(){var m=modes[step];foreach(var a in arts){a.CullPath307=m.Path;a.WholeSheetBounds307=m.Whole;}}
        static void PlayChanged(PlayModeStateChange change){if(change==PlayModeStateChange.ExitingPlayMode&&running)Finish("ABORTED: Play is ending");}
        static void Tick()
        {
            if(!running)return;
            try
            {
                if(Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;frame++;
                var m=modes[step];progress=m.Name+" phase "+(step+1)+"/"+modes.Count+" frame "+frame;
                if(frame>settle)
                {
                    var v=values[m.Name];
                    for(int i=0;i<recorders.Length;i++)if(recorders[i].Recorder.Valid)v[i].Add(recorders[i].Recorder.LastValue);
                    long calls=0,shadowCalls=0;foreach(var a in arts){calls+=a.DrawCalls;shadowCalls+=a.ShadowOnlyDrawCalls;}
                    v[recorders.Length].Add(calls);v[recorders.Length+1].Add(shadowCalls);
                }
                if(frame>=settle+sample){step++;frame=0;if(step>=modes.Count){Finish("DONE");return;}SetMode();}
            }
            catch(Exception e){Finish("ABORTED: "+e.GetType().Name+": "+e.Message);}
        }
        static long Median(List<long> v){if(v.Count==0)return -1;var s=v.OrderBy(x=>x).ToList();return s[s.Count/2];}
        static void Finish(string how)
        {
            running=false;EditorApplication.update-=Tick;EditorApplication.playModeStateChanged-=PlayChanged;
            var lines=new List<string>{"#307 shadowbounds "+DateTime.Now.ToString("s")+" | "+how+" | "+where+" | quality "+QualitySettings.names[QualitySettings.GetQualityLevel()],
                "Whole-frame render counters (gameplay camera without vegetation + the station camera), median of "+sample+" frames per phase after "+settle+" settle frames, two laps pooled."};
            try
            {
                for(int i=0;i<arts.Count;i++){if(arts[i]==null)continue;arts[i].Observer=observers[i];arts[i].CullPath307=paths[i];arts[i].WholeSheetBounds307=wholes[i];}
                if(camera!=null){camera.enabled=false;camera.targetTexture=null;}
                if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}
                if(go!=null)Object.DestroyImmediate(go);
                var header=new StringBuilder("mode");foreach(var r in recorders)header.Append(" | "+r.Name+(r.Recorder.Valid?"":" (unavailable)"));header.Append(" | art draw calls | art ShadowsOnly calls");lines.Add(header.ToString());
                foreach(var kv in values){var sb=new StringBuilder(kv.Key);foreach(var list in kv.Value)sb.Append(" | "+Median(list));lines.Add(sb.ToString());}
                if(values.TryGetValue("tight",out var tight)&&values.TryGetValue("whole-sheet",out var whole)&&recorders[0].Recorder.Valid)
                {
                    long a=Median(tight[0]),b=Median(whole[0]);
                    lines.Add(a>=0&&b>0&&a<b*.95?"CONCLUSION: shadow passes honour worldBounds (Shadow Casters "+a+" tight vs "+b+" whole-sheet)":"CONCLUSION: no clear worldBounds effect on Shadow Casters ("+a+" vs "+b+") - S8.4.5 says re-design rule 4 before relying on it");
                }
                else lines.Add("CONCLUSION: unavailable (Shadow Casters Count recorder invalid or no samples)");
            }
            finally
            {
                if(recorders!=null)foreach(var r in recorders)r.Recorder.Dispose();
                recorders=null;camera=null;rt=null;go=null;
            }
            try{File.WriteAllLines(Path.Combine(ArtCull307.OutFolder,"shadowbounds-"+ArtCull307.Stamp+".txt"),lines);}catch(Exception e){lines.Add("report write failed: "+e.Message);}
            result=string.Join("\n",lines);
        }
    }
}
