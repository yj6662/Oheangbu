using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using Oheangbu.Core.Domain;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.SpellVFX120
{
    // Runtime-component review fixture. Targets and incoming impacts are diagnostic;
    // particles, scheduled damage and guard outcomes use production Update/ParryJudge.
    [InitializeOnLoad] public static class KtpBasicSixCapture
    {
        const string Key="KtpBasicSixCaptureState";
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        [StructLayout(LayoutKind.Sequential)] struct PerformanceInfo
        {
            public uint Size;public UIntPtr CommitTotal,CommitLimit,CommitPeak,PhysicalTotal,PhysicalAvailable,SystemCache,KernelTotal,KernelPaged,KernelNonpaged,PageSize;public uint Handles,Processes,Threads;
        }
        [DllImport("psapi.dll",SetLastError=true)] static extern bool GetPerformanceInfo(out PerformanceInfo info,uint size);
        static float CommitRatio(){PerformanceInfo p; if(!GetPerformanceInfo(out p,(uint)Marshal.SizeOf<PerformanceInfo>()))throw new Exception("Cannot read commit headroom");return (float)((double)p.CommitTotal.ToUInt64()/p.CommitLimit.ToUInt64());}
        [Serializable] class Row
        {
            public string variant,view,folder,status,eventSource;
            public int frames,damageEvents,contactCount,peakParticles;
            public float contactScale,commitRatio,actualHitAt=-1,configuredCastScale,configuredContactScale;
            public Vector3 cameraPosition,cameraEuler,origin,target;
            public bool ended,duplicateImpact,quickCastObserved,quickCastEnded,patternShieldNoContinuousFire=true;
            public int burnBursts;
            public float castOriginError;
            public Vector3 launchViewport;
            public bool guardCastAbsent=true,elementContactOnly=true;
        }
        [Serializable] class Report
        {
            public string glyph,status,mvid,scene,limitation="C2 runtime component fixture with a temporary target and timed incoming guard impact. Not handwriting input or enemy AI combat. Camera is frozen for before/after comparison. Capture is not a performance benchmark.";
            public List<Row> clips=new List<Row>();public List<string> errors=new List<string>();
        }
        static Report report;static Row row;static bool ready,background;static float oldCapture,oldScale,start;static int clip,lastFrame=-1,frame;
        static Camera source,camera;static Vector3 cameraPosition,forward,origin,target;static Quaternion cameraRotation;
        static RenderTexture rt;static Texture2D tex;static GameObject rig,targetRoot,incoming;static Material markerMaterial;
        static Vfx120Effect effect;static Vfx120Profile profile;static KtpContactProfile contactProfile;static CombatConfigSO config;static CombatLoopWiring wiring;static ParryJudge judge;static EnemyVitals enemy;
        static bool guard,impact;static string folder;static readonly List<GameObject> owned=new List<GameObject>();
        static AsyncOperation releaseUnused;
        static KtpBasicSixCapture(){EditorApplication.update+=Tick;Application.logMessageReceived+=(m,s,t)=>{if(SessionState.GetBool(Key,false)&&report!=null&&(t==LogType.Error||t==LogType.Exception))report.errors.Add(m);};}
        public static string Start(string glyph)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||!new[]{"사","마","아","서","머","어"}.Contains(glyph))throw new Exception("Stopped C2 and one selected glyph required");
            if(SceneManager.GetActiveScene().path!="Assets/_Project/Scenes/Dev/C2_CodexWorld.unity")throw new Exception("C2 required; scene was not switched");
            if(CommitRatio()>=.85f)throw new Exception("Commit >=85%; capture not started");
            SessionState.SetString(Key+"Glyph",glyph);SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;return "PLAY_CAPTURE_REQUESTED";
        }
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling||EditorApplication.isPaused)return;
            if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
            try
            {
                if(!ready){Setup();ready=true;return;}
                if(releaseUnused!=null){if(!releaseUnused.isDone)return;releaseUnused=null;GC.Collect();}
                if(row==null){if(clip==4){Finish();return;}BeginClip();return;}
                float age=Time.time-start;
                if(profile.KtpQuickCast&&effect!=null)
                {
                    var cast=effect.NativeCast;
                    if(cast!=null){row.castOriginError=Vector3.Distance(cast.transform.position,origin);if(age<.32f&&cast.LiveParticleCount>0)row.quickCastObserved=true;if(age>=.7f&&cast.LiveParticleCount==0)row.quickCastEnded=true;}
                }
                if(profile.KtpPatternShield&&effect!=null)
                {
                    if(!impact&&effect.FireGuardParticles!=0)row.patternShieldNoContinuousFire=false;
                    row.burnBursts=Math.Max(row.burnBursts,effect.GetComponentsInChildren<Transform>().Count(t=>t.name=="ConfirmedInterceptBurn"));
                }
                var contacts=Object.FindObjectsByType<KtpContactEffect>(FindObjectsSortMode.None).Where(x=>x.gameObject.scene==rig.scene).ToArray();
                row.contactCount=Math.Max(row.contactCount,contacts.Length);
                foreach(var c in contacts){row.contactScale=c.transform.localScale.x;row.peakParticles=Math.Max(row.peakParticles,c.LiveParticles);if(profile.GuardContactPrefab!=null){int patterns=c.Content.GetComponentsInChildren<Renderer>().Count(t=>t.enabled&&t.name.IndexOf("pattern",StringComparison.OrdinalIgnoreCase)>=0);if(c.Source!=profile.GuardContactPrefab||patterns!=(profile.KtpRectShield?1:0))row.elementContactOnly=false;}}
                if(profile.GuardContactPrefab!=null&&effect!=null&&effect.NativeCast!=null)row.guardCastAbsent=false;
                float guardHitTime=Mathf.Min(.65f,config.ParryWindow*.5f);
                if(guard&&!impact&&age>=guardHitTime)
                {
                    impact=true;var element=ElementFor(report.glyph);
                    var attack=Countered(element);
                    var point=origin+forward*(profile.KtpPatternShield?.7f:2.1f);
                    var outcome=judge.ResolveImpact(attack,Time.time,point);
                    if(outcome!=ParryOutcome.Success)throw new Exception("Expected confirmed parry, got "+outcome);
                    row.actualHitAt=age;Object.Destroy(incoming);incoming=null;
                }
                if(incoming!=null) incoming.transform.position=Vector3.Lerp(origin+forward*6,origin+forward*(profile.KtpPatternShield?.7f:2.1f),Mathf.Clamp01(age/guardHitTime));
                if(effect!=null&&effect.NativeImpact!=null)row.duplicateImpact=true;
                if(frame<144){Capture();frame++;row.frames=frame;}
                if(age<13)return;
                row.ended=effect==null&&contacts.Length==0&&!Object.FindObjectsByType<Vfx120TraditionalMotif>(FindObjectsSortMode.None).Any(x=>x.DestroyHostOnCompletion);
                if(row.contactCount!=1||row.duplicateImpact||!row.ended||(!guard&&row.damageEvents!=1))throw new Exception("Contact/lifetime check failed "+JsonUtility.ToJson(row));
                if(profile.KtpQuickCast&&(!row.quickCastObserved||!row.quickCastEnded||row.castOriginError>.001f))throw new Exception("Quick cast timing/origin failed "+JsonUtility.ToJson(row));
                if(profile.KtpPatternShield&&(!row.patternShieldNoContinuousFire||(profile.GuardContactPrefab==null&&row.burnBursts!=1&&report.glyph=="너")))throw new Exception("Pattern shield burn failed "+JsonUtility.ToJson(row));
                if(profile.GuardContactPrefab!=null&&(!row.guardCastAbsent||!row.elementContactOnly||row.burnBursts!=0||row.peakParticles==0))throw new Exception("Element-only guard contact failed "+JsonUtility.ToJson(row));
                if(profile.CameraOffsetLaunch&&Vector2.Distance(row.launchViewport,profile.LaunchViewport)>.001f)throw new Exception("Launch viewport mismatch");
                row.status="PASS_RUNTIME_FIXTURE_VISUAL_PENDING";CleanupClip();clip++;Save();GC.Collect();releaseUnused=Resources.UnloadUnusedAssets();
            }
            catch(Exception e){if(report==null)report=new Report();report.errors.Add(e.ToString());Finish();}
        }
        static void Setup()
        {
            report=new Report{glyph=SessionState.GetString(Key+"Glyph",""),mvid=typeof(Vfx120Effect).Module.ModuleVersionId.ToString(),scene=SceneManager.GetActiveScene().path,status="RUNNING"};
            folder=Path.Combine(Vfx120Editor.Output,"BasicSix",report.glyph);Directory.CreateDirectory(folder);
            source=Camera.main;if(source==null)throw new Exception("Main camera missing");
            cameraPosition=source.transform.position;cameraRotation=source.transform.rotation;
            forward=Vector3.ProjectOnPlane(source.transform.forward,Vector3.up).normalized;
            cameraPosition+=forward*18f;
            if(Physics.Raycast(cameraPosition+Vector3.up*6,Vector3.down,out var cameraGround,30,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))cameraPosition.y=cameraGround.point.y+2.34f;
            origin=cameraPosition+forward*2f;origin.y=cameraPosition.y-.25f;
            target=origin+forward*6f;
            if(Physics.Raycast(target+Vector3.up*4,Vector3.down,out var hit,25,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))target.y=hit.point.y;
            else target.y=origin.y-1.1f;
            background=Application.runInBackground;oldCapture=Time.captureDeltaTime;oldScale=Time.timeScale;
            Application.runInBackground=true;Time.timeScale=1;Time.captureDeltaTime=1f/24;
            clip=0;Save();
        }
        static void BeginClip()
        {
            float ratio=CommitRatio();if(ratio>=.85f)throw new Exception("Commit >=85%; no new clip");
            bool after=clip%2==1,external=clip>=2;
            row=new Row{variant=after?"after":"before",view=external?"external":"play",commitRatio=ratio,eventSource="Production scheduled damage / ParryJudge; diagnostic target and timed incoming impact",status="RUNNING",origin=origin,target=target};
            row.folder=Path.Combine(folder,row.view+"_"+row.variant);Directory.CreateDirectory(row.folder);report.clips.Add(row);
            var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");
            var entry=catalog.Entries.Single(x=>x.Glyph==report.glyph);
            var selected=after?entry.Profile:AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/KtpEmphasis/BaselineBasicSix_"+report.glyph+".asset");
            if(selected==null)throw new Exception("Basic six baseline missing");profile=Object.Instantiate(selected);
            guard=profile.Behavior==Vfx120Behavior.Shield;impact=false;frame=0;
            var cameraGo=new GameObject("EmphasisReviewCamera");owned.Add(cameraGo);camera=cameraGo.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;camera.targetTexture=null;camera.aspect=16f/9;
            var srcData=source.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();if(srcData!=null){var data=cameraGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();EditorUtility.CopySerialized(srcData,data);data.cameraStack.Clear();}
            camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
            origin=cameraPosition+forward*2;origin.y=cameraPosition.y-.25f;
            origin=Vfx120LaunchPoint.Resolve(profile,camera,origin);row.origin=origin;row.launchViewport=camera.WorldToViewportPoint(origin);
            if(external){camera.transform.position=origin-forward*3+Vector3.Cross(Vector3.up,forward)*5+Vector3.up*2;camera.transform.LookAt(origin+forward*2);}
            row.cameraPosition=camera.transform.position;row.cameraEuler=camera.transform.eulerAngles;
            rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);rt.Create();tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            rig=new GameObject("EmphasisRuntimeFixture");owned.Add(rig);rig.SetActive(false);
            config=ScriptableObject.CreateInstance<CombatConfigSO>();contactProfile=Object.Instantiate(Resources.Load<KtpContactProfile>(KtpContactProfile.ResourcePath));contactProfile.SpellProfiles=new[]{profile};
            wiring=rig.AddComponent<CombatLoopWiring>();Set(wiring,"_config",config);Set(wiring,"_contactVfx",contactProfile);
            var live=Object.FindObjectsByType<CombatLoopWiring>(FindObjectsSortMode.None).FirstOrDefault(x=>x!=wiring);
            if(live!=null)Set(wiring,"_palette",Get(live,"_palette"));
            judge=new ParryJudge(config);wiring.Construct(null,judge,new GroggyMeter(config),new InkPool(config,null));rig.SetActive(true);
            targetRoot=new GameObject("DiagnosticEnemy");owned.Add(targetRoot);targetRoot.transform.position=target;targetRoot.SetActive(false);
            enemy=targetRoot.AddComponent<EnemyVitals>();Set(enemy,"_config",config);targetRoot.SetActive(true);
            var marker=GameObject.CreatePrimitive(PrimitiveType.Capsule);marker.transform.SetParent(targetRoot.transform,false);marker.transform.localPosition=Vector3.up*.9f;marker.transform.localScale=new Vector3(.6f,.9f,.6f);Object.Destroy(marker.GetComponent<Collider>());
            markerMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));markerMaterial.color=new Color(.25f,.20f,.16f);marker.GetComponent<Renderer>().sharedMaterial=markerMaterial;
            enemy.HpChanged+=()=>{row.damageEvents++;row.actualHitAt=Time.time-start;};
            var go=Object.Instantiate(entry.Prefab);owned.Add(go);effect=go.GetComponent<Vfx120Effect>();effect.Profile=profile;effect.PreviewControlled=false;effect.DemonstrationCues=false;
            float flight=profile.Flight;effect.SetImpactClock(flight);effect.Begin(origin,guard?null:enemy.transform,target+Vector3.up*1.1f,Color.white);
            start=Time.time;
            Element element=ElementFor(report.glyph);
            if(guard)
            {
                Invoke(wiring,"ResolveParry",new SpellCast(report.glyph[0],SpellKind.Parry,element,1,default,1));
                // These three guards use the common native-field and contact path.
                incoming=GameObject.CreatePrimitive(PrimitiveType.Sphere);owned.Add(incoming);Object.Destroy(incoming.GetComponent<Collider>());incoming.transform.localScale=Vector3.one*.13f;incoming.GetComponent<Renderer>().sharedMaterial=markerMaterial;
            }
            else AddPending(report.glyph[0],element,5,Time.time+flight);
            row.configuredCastScale=profile.NativeScale*profile.KtpCastMultiplier;
            row.configuredContactScale=contactProfile.ParryScale*profile.KtpContactMultiplier;
            Save();
        }
        static Element ElementFor(string glyph)=>glyph=="사"||glyph=="서"?Element.Metal:glyph=="마"||glyph=="머"?Element.Earth:Element.Water;
        static Element Countered(Element element)=>element==Element.Metal?Element.Wood:element==Element.Earth?Element.Water:Element.Fire;
        static void AddPending(char glyph,Element element,float power,float time)
        {
            var type=typeof(CombatLoopWiring).GetNestedType("PendingCast",BindingFlags.NonPublic);var item=Activator.CreateInstance(type);
            foreach(var pair in new Dictionary<string,object>{{"Target",enemy},{"Letter",glyph},{"Element",element},{"Power",power},{"ImpactTime",time}})type.GetField(pair.Key).SetValue(item,pair.Value);
            ((IList)Get(wiring,"_pendingCasts")).Add(item);
        }
        static void Capture()
        {
            var old=RenderTexture.active;var previous=camera.targetTexture;
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(row.folder,frame.ToString("D4")+".jpg"),tex.EncodeToJPG(92));}
            finally{camera.targetTexture=previous;RenderTexture.active=old;}
        }
        static object Get(object o,string name)=>o.GetType().GetField(name,Hidden).GetValue(o);
        static void Set(object o,string name,object value)=>o.GetType().GetField(name,Hidden).SetValue(o,value);
        static void Invoke(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Hidden).Invoke(o,args);
        static void CleanupClip()
        {
            if(rig!=null)rig.SetActive(false);foreach(var go in owned)if(go!=null)Object.DestroyImmediate(go);owned.Clear();
            if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(tex!=null)Object.DestroyImmediate(tex);
            foreach(var asset in new Object[]{profile,contactProfile,config,markerMaterial})if(asset!=null)Object.DestroyImmediate(asset);
            row=null;
        }
        static void Save(){if(report!=null&&folder!=null)File.WriteAllText(Path.Combine(folder,"report.json"),JsonUtility.ToJson(report,true));}
        static void Finish()
        {
            report.status=report.errors.Count==0?"PASS_RUNTIME_FIXTURE_VISUAL_PENDING":"FAIL";Save();CleanupClip();
            if(ready){Time.captureDeltaTime=oldCapture;Time.timeScale=oldScale;Application.runInBackground=background;}
            ready=false;SessionState.SetBool(Key,false);EditorApplication.isPlaying=false;
        }
    }
}
