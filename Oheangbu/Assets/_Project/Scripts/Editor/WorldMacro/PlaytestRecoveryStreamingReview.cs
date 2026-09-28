using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.App.World.Dressing;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    [InitializeOnLoad] public static class PlaytestRecoveryStreamingReview
    {
        [Serializable] public struct Sample
        {
            public string phase;public int frame,resident,pending,draws,instances,rebuilds;
            public float delta,generation,packets,submission,collision;public double cpu,gpu,memoryMiB;
            public long generationGc,packetGc,submissionGc;
            public int trees,shrubs,rocks,grass,lowGrass,cover,fixedItems;
        }
        [Serializable] public sealed class Summary
        {
            public string phase;public int count,maxResident,maxPending,maxDraws;
            public double frameP50,frameP95,frameP99,frameMax,cpuP50,cpuP95,gpuP50,gpuP95,generationP95,packetP95,submissionP95,maxMemoryMiB;
            public double cpuP99,gpuP99,collisionP95,generationGcP95,packetGcP95,submissionGcP95;
        }
        [Serializable] public sealed class Report
        {
            public string status,utc,mode,error,scope="Actual Editor Play/render frames, diagnostic camera only: cold stationary, rotation, 224m camera translation and return at14m/s. No player traversal or video; no prewarm. Full world cost is included; camera/input gameplay is not certified.";
            public int width,height;public bool restored;public float commit;public Vector3 start;public Summary[] phases;public Sample[] samples;
            public string unityVersion,gpuDevice,cpuDevice,streamingSourceSha256;
            public int nativeCameraWidth,nativeCameraHeight,quality,vSync;
            public WorldMacroDressingRenderer.RenderCostReport finalRenderCost;
        }
        static readonly List<Sample> data=new List<Sample>(18000);
        static readonly FrameTiming[] timing=new FrameTiming[1];
        static WorldMacroDressingRenderer dressing;static Camera original,probe,observer;
        static RenderTexture target;static bool running,oldEnabled,oldStreaming,oldDiagnostic,oldCameraEnabled,oldRun;
        static double started;static int frame=-1;static Report report;static Vector3 position;static Quaternion rotation;
        static string directory;
        static PlaytestRecoveryStreamingReview()
        {
            AssemblyReloadEvents.beforeAssemblyReload+=()=>{if(running)Finish("INTERRUPTED_RELOAD");};
            EditorApplication.playModeStateChanged+=state=>{if(running&&state==PlayModeStateChange.ExitingPlayMode)Finish("INTERRUPTED_PLAY_EXIT");};
        }
        public static string Execute(string action)
        {
            if(action=="partition")
            {
                var renderer=Object.FindFirstObjectByType<WorldMacroDressingRenderer>();string json=renderer.ValidateStreamingPartition();
                string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/Performance/partition_identity.json"));Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,json);return json;
            }
            if(action=="poll")return running?"RUNNING "+data.Count+" actual frames":report==null?"NOT_RUN":JsonUtility.ToJson(report,true);
            if(action=="abort"){if(running)Finish("ABORTED");return "Stopped";}
            if(running||!Application.isPlaying)throw new InvalidOperationException("Unpaused Play required, no concurrent diagnostic.");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Memory stop85%.");
            string mode=action.Split(':')[0];if(mode!="stream"&&mode!="legacy"&&mode!="off")throw new ArgumentException("stream|legacy|off[:1440] / poll / abort");
            var walker=Object.FindFirstObjectByType<WorldMacroCombatWalker>();dressing=Object.FindFirstObjectByType<WorldMacroDressingRenderer>();
            var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(walker==null||dressing==null||session==null||string.IsNullOrEmpty(session.TestSaveSuffix)||Time.timeScale<.99f)throw new InvalidOperationException("Actual idle walker and isolated test slot required.");
            original=walker.ViewCamera;observer=dressing.Observer;oldCameraEnabled=original.enabled;oldEnabled=dressing.enabled;
            oldStreaming=dressing.BudgetedDenseStreaming;oldDiagnostic=dressing.AllowDiagnosticCameras;oldRun=Application.runInBackground;
            position=original.transform.position;rotation=original.transform.rotation;int h=action.EndsWith(":1440")?1440:1080,w=h*16/9;
            // Fixed material-comparison viewpoint; never teleports or drives the player.
            if(action.Contains(":ink")){position=new Vector3(907,135.2f,208);rotation=Quaternion.LookRotation(new Vector3(888,135,245)-position);}
            report=new Report{status="RUNNING",utc=DateTime.UtcNow.ToString("O"),mode=mode,width=w,height=h,start=position};
            report.unityVersion=Application.unityVersion;report.gpuDevice=SystemInfo.graphicsDeviceName;report.cpuDevice=SystemInfo.processorType;
            report.nativeCameraWidth=original.pixelWidth;report.nativeCameraHeight=original.pixelHeight;report.quality=QualitySettings.GetQualityLevel();report.vSync=QualitySettings.vSyncCount;
            using(var hash=System.Security.Cryptography.SHA256.Create())report.streamingSourceSha256=BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(Application.dataPath+"/_Project/Scripts/App/World/Dressing/WorldMacroDressingStreaming.cs"))).Replace("-","").ToLowerInvariant();
            directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/Performance",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"_"+mode+"_"+h));
            Directory.CreateDirectory(directory);
            try
            {
                var go=new GameObject("RecoveryDiagnosticCamera_NoPlayerMovement"){hideFlags=HideFlags.HideAndDontSave};probe=go.AddComponent<Camera>();probe.CopyFrom(original);
                var urp=original.GetComponent<UniversalAdditionalCameraData>();if(urp!=null)EditorUtility.CopySerialized(urp,probe.GetUniversalAdditionalCameraData());
                probe.transform.SetPositionAndRotation(position,rotation);target=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32);target.Create();probe.targetTexture=target;probe.aspect=16f/9;
                original.enabled=false;probe.enabled=true;dressing.Observer=probe;dressing.AllowDiagnosticCameras=false;dressing.BudgetedDenseStreaming=mode!="legacy";dressing.enabled=mode!="off";
                dressing.ResetCache();data.Clear();frame=-1;Application.runInBackground=true;started=EditorApplication.timeSinceStartup;running=true;EditorApplication.update+=Tick;
                return "RUNNING cold+rotation+translation+return, approximately49seconds; no prewarm / player movement / video";
            }
            catch(Exception e){report.error=e.ToString();Finish("FAILED");throw;}
        }
        static void Tick()
        {
            if(!running||frame==Time.frameCount)return;frame=Time.frameCount;
            if(frame%30==0&&Prologue.PrologueAudit.CommitRatio()>=.85f){Finish("STOPPED_MEMORY_85");return;}
            double t=EditorApplication.timeSinceStartup-started;if(t>=49){Finish("MEASURED_DIAGNOSTIC_CAMERA");return;}
            string phase=t<3?"cold":t<8?"stationary":t<14?"rotation":t<30?"first_visit":t<46?"return_visit":"returned_stationary";
            float travel=t<14?0:t<30?(float)(t-14)*14:t<46?224-(float)(t-30)*14:0;
            Vector3 forward=Vector3.ProjectOnPlane(rotation*Vector3.forward,Vector3.up).normalized;Vector3 p=position+forward*travel;
            if(t>=14&&dressing.TryDiagnosticGroundHeight(p.x,p.z,out float groundHeight))p.y=groundHeight+1.6f;
            probe.transform.SetPositionAndRotation(p,t>=8&&t<14?Quaternion.AngleAxis((float)(t-8)*60,Vector3.up)*rotation:rotation);
            FrameTimingManager.CaptureFrameTimings();double cpu=-1,gpu=-1;if(FrameTimingManager.GetLatestTimings(1,timing)>0){cpu=timing[0].cpuFrameTime>0?timing[0].cpuFrameTime:-1;gpu=timing[0].gpuFrameTime>0?timing[0].gpuFrameTime:-1;}
            data.Add(new Sample{phase=phase,frame=frame,delta=Time.unscaledDeltaTime*1000,cpu=cpu,gpu=gpu,resident=dressing.ResidentInstances,pending=dressing.PendingChunks,
                draws=dressing.ObserverDrawCalls,instances=dressing.ObserverDrawnInstances,generation=dressing.LastGenerationMilliseconds,packets=dressing.LastPacketMilliseconds,
                submission=dressing.LastSubmissionMilliseconds,collision=dressing.LastCollisionMilliseconds,rebuilds=dressing.PacketRebuilds,memoryMiB=Profiler.GetTotalAllocatedMemoryLong()/1048576d,
                generationGc=dressing.GenerationAllocatedBytes,packetGc=dressing.PacketAllocatedBytes,submissionGc=dressing.SubmissionAllocatedBytes,
                trees=dressing.ResidentByLayer[0],shrubs=dressing.ResidentByLayer[1],rocks=dressing.ResidentByLayer[2],grass=dressing.ResidentByLayer[3],lowGrass=dressing.ResidentByLayer[4],cover=dressing.ResidentByLayer[5],fixedItems=dressing.ResidentByLayer[6]});
        }
        static double P(IEnumerable<double> source,double p)
        {var a=source.Where(v=>v>=0&&!double.IsNaN(v)&&!double.IsInfinity(v)).OrderBy(v=>v).ToArray();return a.Length==0?-1:a[(int)Math.Round((a.Length-1)*p)];}
        static void Finish(string status)
        {
            running=false;EditorApplication.update-=Tick;
            if(dressing!=null&&report!=null&&report.mode!="off")report.finalRenderCost=dressing.GetRenderCostSnapshot();
            if(probe!=null){probe.enabled=false;probe.targetTexture=null;Object.DestroyImmediate(probe.gameObject);}if(target!=null){target.Release();Object.DestroyImmediate(target);}
            if(original!=null)original.enabled=oldCameraEnabled;
            if(dressing!=null){dressing.Observer=observer;dressing.BudgetedDenseStreaming=oldStreaming;dressing.AllowDiagnosticCameras=oldDiagnostic;dressing.enabled=oldEnabled;dressing.ResetCache();}
            Application.runInBackground=oldRun;if(report==null)return;report.status=status;report.restored=true;report.commit=Prologue.PrologueAudit.CommitRatio();report.samples=data.ToArray();
            report.phases=data.GroupBy(s=>s.phase).Select(g=>new Summary{phase=g.Key,count=g.Count(),maxResident=g.Max(s=>s.resident),maxPending=g.Max(s=>s.pending),maxDraws=g.Max(s=>s.draws),
                frameP50=P(g.Select(s=>(double)s.delta),.5),frameP95=P(g.Select(s=>(double)s.delta),.95),frameP99=P(g.Select(s=>(double)s.delta),.99),frameMax=P(g.Select(s=>(double)s.delta),1),
                cpuP50=P(g.Select(s=>s.cpu),.5),cpuP95=P(g.Select(s=>s.cpu),.95),gpuP50=P(g.Select(s=>s.gpu),.5),gpuP95=P(g.Select(s=>s.gpu),.95),
                cpuP99=P(g.Select(s=>s.cpu),.99),gpuP99=P(g.Select(s=>s.gpu),.99),collisionP95=P(g.Select(s=>(double)s.collision),.95),
                generationGcP95=P(g.Select(s=>(double)s.generationGc),.95),packetGcP95=P(g.Select(s=>(double)s.packetGc),.95),submissionGcP95=P(g.Select(s=>(double)s.submissionGc),.95),
                generationP95=P(g.Select(s=>(double)s.generation),.95),packetP95=P(g.Select(s=>(double)s.packets),.95),submissionP95=P(g.Select(s=>(double)s.submission),.95),maxMemoryMiB=g.Max(s=>s.memoryMiB)}).ToArray();
            if(directory!=null)File.WriteAllText(directory+"/measurement.json",JsonUtility.ToJson(report,true));
        }
    }
}
