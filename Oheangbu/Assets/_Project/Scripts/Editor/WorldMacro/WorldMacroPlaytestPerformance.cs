using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEditor;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class WorldMacroPlaytestPerformance
    {
        [Serializable] class Result {
            public string utc,status,scope="Unity Editor Play, fixed 1920x1080 offscreen camera, 30 warmup + 120 samples. Editor overhead included; zero GPU timing means unavailable, not zero cost.";
            public bool combat;public Vector3 cameraPosition,cameraEuler;
            public double medianFrameMs,p95FrameMs,medianCpuMs,medianGpuMs,allocatedMiB,reservedMiB;
            public int frameSamples,cpuSamples,gpuSamples;public float commit;
        }
        static readonly List<double> frame=new List<double>(),cpu=new List<double>(),gpu=new List<double>();static readonly FrameTiming[] timing=new FrameTiming[1];
        static WorldMacroPlaytestSession session;static Camera camera;static RenderTexture target,priorTarget;static bool priorCombat;static float priorAspect;static int lastFrame,warm;static double begun;static ulong timestamp;static Result result;
        public static string Begin(bool combat)
        {
            if(!EditorApplication.isPlaying||session!=null)throw new Exception("Idle measurement in Play required");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new Exception("Measurement stopped at 85% system commit");
            session=UnityEngine.Object.FindFirstObjectByType<WorldMacroPlaytestSession>();camera=session.Walker.ViewCamera;priorCombat=session.CombatActive;session.CombatActive=combat;session.Cull();
            priorTarget=camera.targetTexture;priorAspect=camera.aspect;target=new RenderTexture(1920,1080,24);camera.targetTexture=target;camera.aspect=16f/9;
            frame.Clear();cpu.Clear();gpu.Clear();warm=30;lastFrame=-1;timestamp=0;begun=EditorApplication.timeSinceStartup;
            result=new Result{utc=DateTime.UtcNow.ToString("o"),combat=combat,cameraPosition=camera.transform.position,cameraEuler=camera.transform.eulerAngles};
            EditorApplication.update+=Tick;return "Measurement started "+(combat?"on":"off");
        }
        static void Tick()
        {
            if(session==null||!EditorApplication.isPlaying||EditorApplication.timeSinceStartup-begun>50||Prologue.PrologueAudit.CommitRatio()>=.85f){Finish("INCOMPLETE");return;}
            if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
            FrameTimingManager.CaptureFrameTimings();if(warm-->0)return;
            frame.Add(Time.unscaledDeltaTime*1000);
            if(FrameTimingManager.GetLatestTimings(1,timing)>0&&timing[0].frameStartTimestamp!=timestamp){timestamp=timing[0].frameStartTimestamp;if(timing[0].cpuFrameTime>0)cpu.Add(timing[0].cpuFrameTime);if(timing[0].gpuFrameTime>0)gpu.Add(timing[0].gpuFrameTime);}
            if(frame.Count>=120)Finish("MEASURED");
        }
        static double Percentile(List<double> values,float p){if(values.Count==0)return -1;var sorted=values.OrderBy(v=>v).ToArray();return sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Length*p)-1,0,sorted.Length-1)];}
        static void Finish(string status)
        {
            EditorApplication.update-=Tick;if(result==null)return;
            result.status=status;result.frameSamples=frame.Count;result.cpuSamples=cpu.Count;result.gpuSamples=gpu.Count;result.medianFrameMs=Percentile(frame,.5f);result.p95FrameMs=Percentile(frame,.95f);result.medianCpuMs=Percentile(cpu,.5f);result.medianGpuMs=Percentile(gpu,.5f);result.allocatedMiB=Profiler.GetTotalAllocatedMemoryLong()/1048576d;result.reservedMiB=Profiler.GetTotalReservedMemoryLong()/1048576d;result.commit=Prologue.PrologueAudit.CommitRatio();
            if(camera!=null){camera.targetTexture=priorTarget;camera.aspect=priorAspect;}if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(session!=null){session.CombatActive=priorCombat;session.Cull();}
            File.WriteAllText(WorldMacroPlaytestAuthoring.Output+"/performance_"+(result.combat?"on":"off")+".json",JsonUtility.ToJson(result,true));session=null;
        }
        public static string Poll()=>session!=null?"RUNNING samples="+frame.Count:result==null?"No measurement":JsonUtility.ToJson(result,true);
    }
}
