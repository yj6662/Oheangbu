using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering.Universal;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.Dressing;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Explicit sequential 1080p stills and short Editor-play measurements, never an automatic traversal.</summary>
    public static class WorldMacroDressingProbe
    {
        static string Output=>WorldMacroBuilder.Output+"/Dressing";
        [Serializable] public sealed class Result
        {
            public string label,utc,status,scope="Unity Editor play, stationary 1920x1080 offscreen target. Not a standalone FPS guarantee.",gpuStatus;
            public bool dressing,poseStable,aerialWashOverride,rebuildEachFrame;public int samples,cpuSamples,gpuSamples,residentCells,residentInstances,drawCalls,drawnInstances,colliders,colliderOverflow;
            public Vector3 position,euler;public float fieldOfView,commitRatio;public double frameMedianMs,frameP95Ms,cpuMedianMs,cpuP95Ms,gpuMedianMs,gpuP95Ms,allocatedMiB;
        }
        static WorldMacroDressingRenderer active;static Camera measuringCamera;static RenderTexture target,previousTarget;
        static bool previousEnabled,forestBefore,previousRebuild;static GameObject baselineForest;static int wanted,warm,lastFrame;static float previousAspect;static double started;
        static Result result;static readonly List<double> frame=new List<double>(),cpu=new List<double>(),gpu=new List<double>();
        static readonly FrameTiming[] timings=new FrameTiming[1];static ulong timestamp;
        static Camera Source()
        {
            if(Application.isPlaying)
            {
                var player=Object.FindFirstObjectByType<WorldMacroCombatWalker>();
                if(player!=null&&player.ViewCamera!=null&&player.ViewCamera.isActiveAndEnabled)return player.ViewCamera;
                var main=Camera.main;
                if(main!=null&&main.isActiveAndEnabled)return main;
            }
            return Object.FindFirstObjectByType<WorldMacroReviewController>()?.GetComponent<Camera>()??Camera.main;
        }
        static GameObject OldForest()=>GameObject.Find("WorldMacro_AuthoredGeography")?.transform.Find("04_ForestExtent_PlaceholderClusters")?.gameObject;
        static void Guard(){if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("No new capture/measurement: system commit >=85%.");}
        static string Safe(string value)
        {if(string.IsNullOrEmpty(value)||value.IndexOfAny(Path.GetInvalidFileNameChars())>=0||value.Contains(".."))throw new ArgumentException("A simple output label is required.");return value;}

        public static string Capture(string label,bool dressing,float x,float y,float z,float tx,float ty,float tz)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Still capture requires Edit mode.");
            Guard();Safe(label);var source=Source();var renderer=Object.FindFirstObjectByType<WorldMacroDressingRenderer>();
            if(source==null||renderer==null)throw new InvalidOperationException("Existing macro camera and dressing are required.");
            var position=new Vector3(x,y,z);var direction=new Vector3(tx,ty,tz)-position;if(direction.sqrMagnitude<.01f)throw new ArgumentException("Capture target coincides with camera.");
            bool enabledBefore=renderer.enabled,previewBefore=renderer.PreviewInEditor,asyncBefore=ShaderUtil.allowAsyncCompilation;
            var oldForest=OldForest();bool oldForestBefore=oldForest!=null&&oldForest.activeSelf;
            GameObject go=null;Camera camera=null;RenderTexture rt=null;Texture2D image=null;var rtBefore=RenderTexture.active;
            var skyBefore=RenderSettings.skybox;Material skyCopy=skyBefore==null?null:new Material(skyBefore){hideFlags=HideFlags.HideAndDontSave};
            bool aerial=position.y-Oheangbu.Data.World.WorldMacroTerrain.SurfaceHeight(renderer.Sheet.Geography,x,z)>150;
            var materials=Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,FindObjectsSortMode.None).SelectMany(r=>r.sharedMaterials)
                .Concat(renderer.Sheet.Prototypes.SelectMany(p=>p.Lods).SelectMany(l=>l.Parts).Select(p=>p.Material)).Where(m=>m!=null&&m.HasProperty("_WashStrength")).Distinct().ToArray();
            var wash=materials.Select(m=>m.GetFloat("_WashStrength")).ToArray();float forestDistance=renderer.Sheet.ForestDistance,aerialBefore=Shader.GetGlobalFloat("_DressingAerial");
            var distant=renderer.Sheet.Prototypes.Where(p=>p.Category==Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Tree).SelectMany(p=>p.Lods[p.Lods.Length-1].Parts).Select(p=>p.Material).Distinct().ToArray();
            var fadeStart=distant.Select(m=>m.GetFloat("_FadeOutStart")).ToArray();var fadeEnd=distant.Select(m=>m.GetFloat("_FadeOutEnd")).ToArray();
            try
            {
                ShaderUtil.allowAsyncCompilation=false;renderer.enabled=dressing;renderer.PreviewInEditor=dressing;if(oldForest!=null)oldForest.SetActive(!dressing);
                if(aerial){foreach(var mat in materials)mat.SetFloat("_WashStrength",.1f);renderer.Sheet.ForestDistance=24000;foreach(var mat in distant){mat.SetFloat("_FadeOutStart",20000);mat.SetFloat("_FadeOutEnd",24000);}Shader.SetGlobalFloat("_DressingAerial",1);}
                if(dressing)renderer.PrepareView(position,4096,Guard);
                go=new GameObject("Temporary_DressingCapture"){hideFlags=HideFlags.HideAndDontSave};camera=go.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;
                camera.aspect=16f/9;camera.useOcclusionCulling=false;camera.layerCullDistances=new float[32];camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(direction,Vector3.up));if(aerial){camera.farClipPlane=40000;camera.nearClipPlane=10;}
                var data=source.GetComponent<UniversalAdditionalCameraData>();if(data!=null)EditorUtility.CopySerialized(data,camera.GetUniversalAdditionalCameraData());
                Object.FindFirstObjectByType<WorldLookDriver>()?.PreviewRegionalSky(position);
                if(dressing)WorldMacroDressingAssets.PrepareNewMaterialPasses(renderer.Sheet.Prototypes.SelectMany(p=>p.Lods).SelectMany(l=>l.Parts).Select(p=>p.Material));Guard();
                rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);image=new Texture2D(1920,1080,TextureFormat.RGB24,false);
                camera.targetTexture=rt;
                // Exceptions from SRP callbacks may only reach the Unity log instead of escaping
                // Camera.Render. Such a frame must not replace an earlier valid PNG or metadata.
                var renderErrors=new List<string>();
                void CollectRenderError(string message,string stack,LogType type)
                {
                    if((type==LogType.Error||type==LogType.Exception||type==LogType.Assert)&&renderErrors.Count<8)
                        renderErrors.Add(type+": "+message+"\n"+stack);
                }
                Application.logMessageReceived+=CollectRenderError;
                try
                {
                    camera.Render();
                    if(renderErrors.Count!=0)throw new InvalidOperationException("Capture rejected; render logged errors. Previous output preserved.\n"+string.Join("\n",renderErrors));
                    RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply(false);
                    if(renderErrors.Count!=0)throw new InvalidOperationException("Capture rejected; readback logged errors. Previous output preserved.\n"+string.Join("\n",renderErrors));
                }
                finally{Application.logMessageReceived-=CollectRenderError;}
                Directory.CreateDirectory(Output);File.WriteAllBytes(Output+"/"+label+".png",image.EncodeToPNG());
                var info=new Result{label=label,utc=DateTime.UtcNow.ToString("o"),status="CAPTURED",scope="1920x1080 still; no video. Does not certify runtime performance or visual approval.",dressing=dressing,aerialWashOverride=aerial,position=position,euler=camera.transform.eulerAngles,fieldOfView=camera.fieldOfView,commitRatio=Prologue.PrologueAudit.CommitRatio(),residentCells=renderer.ResidentCells,residentInstances=renderer.ResidentInstances,drawCalls=renderer.DrawCalls,drawnInstances=renderer.DrawnInstances};
                File.WriteAllText(Output+"/"+label+".json",JsonUtility.ToJson(info,true));return Output+"/"+label+".png";
            }
            finally
            {
                if(camera!=null)camera.targetTexture=null;RenderTexture.active=rtBefore;
                if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(image!=null)Object.DestroyImmediate(image);if(go!=null)Object.DestroyImmediate(go);
                renderer.enabled=enabledBefore;renderer.PreviewInEditor=previewBefore;ShaderUtil.allowAsyncCompilation=asyncBefore;
                if(oldForest!=null)oldForest.SetActive(oldForestBefore);
                for(int i=0;i<materials.Length;i++)materials[i].SetFloat("_WashStrength",wash[i]);for(int i=0;i<distant.Length;i++){distant[i].SetFloat("_FadeOutStart",fadeStart[i]);distant[i].SetFloat("_FadeOutEnd",fadeEnd[i]);}
                renderer.Sheet.ForestDistance=forestDistance;Shader.SetGlobalFloat("_DressingAerial",aerialBefore);if(aerial)renderer.ResetCache();
                if(skyBefore!=null&&skyCopy!=null)skyBefore.CopyPropertiesFromMaterial(skyCopy);RenderSettings.skybox=skyBefore;if(skyCopy!=null)Object.DestroyImmediate(skyCopy);
            }
        }
        public static string BeginMeasure(string label,bool dressing,int sampleFrames=120,bool rebuildEachFrame=false)
        {
            if(active!=null)throw new InvalidOperationException("A measurement is already running; call Poll or Stop.");
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Enter Play mode and hold the review camera still before measuring.");
            Guard();Safe(label);active=Object.FindFirstObjectByType<WorldMacroDressingRenderer>();measuringCamera=Source();
            if(active==null||measuringCamera==null||!measuringCamera.isActiveAndEnabled){active=null;throw new InvalidOperationException("An active game camera and dressing renderer are required. Disabled macro review cameras cannot provide performance evidence.");}
            previousEnabled=active.enabled;previousTarget=measuringCamera.targetTexture;previousAspect=measuringCamera.aspect;previousRebuild=active.DiagnosticRebuildEachFrame;
            result=new Result{label=label,utc=DateTime.UtcNow.ToString("o"),status="RUNNING",dressing=dressing,rebuildEachFrame=rebuildEachFrame,position=measuringCamera.transform.position,euler=measuringCamera.transform.eulerAngles,fieldOfView=measuringCamera.fieldOfView,poseStable=true};
            if(rebuildEachFrame)result.scope+=" Draw packets rebuilt every frame to measure uncached CPU cost. Camera and player remain stationary; this is not traversal measurement.";
            frame.Clear();cpu.Clear();gpu.Clear();timestamp=0;wanted=Mathf.Clamp(sampleFrames,30,600);warm=40;lastFrame=-1;started=EditorApplication.timeSinceStartup;
            try
            {
                baselineForest=OldForest();forestBefore=baselineForest!=null&&baselineForest.activeSelf;if(baselineForest!=null)baselineForest.SetActive(!dressing);
                active.enabled=dressing;active.DiagnosticRebuildEachFrame=rebuildEachFrame;if(dressing)active.PrepareView(measuringCamera.transform.position,4096,Guard);Guard();
                target=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);target.Create();measuringCamera.targetTexture=target;measuringCamera.aspect=16f/9;
                EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=PlayChanged;return "Measurement started: "+label+". Poll after approximately 5 seconds; keep the camera still.";
            }
            catch{Finish("STOPPED_SETUP_FAILURE");throw;}
        }
        static void Tick()
        {
            if(active==null||measuringCamera==null){Finish("STOPPED_MISSING_OBJECT");return;}
            if(!EditorApplication.isPlaying||EditorApplication.timeSinceStartup-started>45){Finish("STOPPED_TIMEOUT_OR_PLAY_EXIT");return;}
            if(Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;
            if(lastFrame%30==0&&Prologue.PrologueAudit.CommitRatio()>=.85f){Finish("STOPPED_COMMIT_85");return;}
            result.poseStable&=Vector3.Distance(result.position,measuringCamera.transform.position)<.02f&&Quaternion.Angle(Quaternion.Euler(result.euler),measuringCamera.transform.rotation)<.05f;
            FrameTimingManager.CaptureFrameTimings();if(warm-->0)return;
            frame.Add(Time.unscaledDeltaTime*1000d);
            if(FrameTimingManager.GetLatestTimings(1,timings)>0&&timings[0].frameStartTimestamp!=timestamp)
            {timestamp=timings[0].frameStartTimestamp;double c=timings[0].cpuFrameTime,g=timings[0].gpuFrameTime;if(c>0&&c<10000)cpu.Add(c);if(g>0&&g<10000)gpu.Add(g);}
            result.colliderOverflow=Mathf.Max(result.colliderOverflow,active.ColliderOverflow);
            if(frame.Count>=wanted)Finish(result.poseStable?"MEASURED":"INVALID_CAMERA_MOVED");
        }
        static void PlayChanged(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingPlayMode)Finish("STOPPED_PLAY_EXIT");}
        static double Percentile(List<double> values,float p){if(values.Count==0)return -1;var copy=values.OrderBy(v=>v).ToArray();return copy[Mathf.Clamp(Mathf.RoundToInt((copy.Length-1)*p),0,copy.Length-1)];}
        static void Finish(string status)
        {
            EditorApplication.update-=Tick;EditorApplication.playModeStateChanged-=PlayChanged;if(result==null)return;
            result.status=status;result.samples=frame.Count;result.cpuSamples=cpu.Count;result.gpuSamples=gpu.Count;result.gpuStatus=gpu.Count>0?"MEASURED":"UNAVAILABLE_NOT_ZERO";
            result.frameMedianMs=Percentile(frame,.5f);result.frameP95Ms=Percentile(frame,.95f);result.cpuMedianMs=Percentile(cpu,.5f);result.cpuP95Ms=Percentile(cpu,.95f);result.gpuMedianMs=Percentile(gpu,.5f);result.gpuP95Ms=Percentile(gpu,.95f);
            result.allocatedMiB=Profiler.GetTotalAllocatedMemoryLong()/1048576d;result.commitRatio=Prologue.PrologueAudit.CommitRatio();
            if(active!=null){result.residentCells=result.dressing?active.ResidentCells:0;result.residentInstances=result.dressing?active.ResidentInstances:0;result.drawCalls=result.dressing?active.ObserverDrawCalls:0;result.drawnInstances=result.dressing?active.ObserverDrawnInstances:0;result.colliders=result.dressing?active.ActiveColliders:0;active.DiagnosticRebuildEachFrame=previousRebuild;active.enabled=previousEnabled;}
            if(measuringCamera!=null){measuringCamera.targetTexture=previousTarget;measuringCamera.aspect=previousAspect;}
            if(baselineForest!=null)baselineForest.SetActive(forestBefore);baselineForest=null;
            if(target!=null){target.Release();Object.DestroyImmediate(target);}target=null;active=null;measuringCamera=null;
            Directory.CreateDirectory(Output);File.WriteAllText(Output+"/"+result.label+"_performance.json",JsonUtility.ToJson(result,true));
        }
        public static bool IsRunning=>active!=null;
        public static string Poll()=>active!=null?"RUNNING "+frame.Count+"/"+wanted:result==null?"No measurement started.":JsonUtility.ToJson(result,true);
        public static string Stop(){if(active!=null)Finish("STOPPED_EXPLICITLY");return Poll();}
    }
}
