using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.Presentation;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // Temporary ablation harness. Changes are restored and never saved.
    public sealed class C2PerformanceAudit
    {
        [Serializable] public sealed class Counter { public string name, unit; public double average; }
        [Serializable] public sealed class Phase
        {
            public string name; public int frames; public double frameMs, p95Ms, fps, gpuMs; public int gpuSamples;
            public List<Counter> counters = new List<Counter>();
        }
        [Serializable] public sealed class Report
        {
            public string status, error, hardware, graphicsApi, scope;
            public int width, height, renderers, meshRenderers, lights, cloths, lodGroups, staticBatched, frustumVisible;
            public bool restored, occlusionCamera; public List<string> shaders, availableMarkers;
            public List<Phase> phases = new List<Phase>();
        }
        public static string Begin()
        {
            if (!Application.isPlaying || active != null) return "Requires idle Play Mode";
            var audit = new C2PerformanceAudit(); active = audit;
            audit.Start();
            return Path.GetFullPath("Screenshots/Performance/C2-ablation.json");
        }
        public static string BeginBaseline()
        {
            if (!Application.isPlaying || active != null) return "Requires idle Play Mode";
            var audit = new C2PerformanceAudit { names = new[] { "baseline" }, warmup = 5, duration = 10 }; active = audit; audit.Start();
            return Path.GetFullPath("Screenshots/Performance/C2-ablation.json");
        }
        static C2PerformanceAudit active;
        Report report; Renderer[] renderers; bool[] rendererEnabled, renderingOff, playerRenderer; Cloth[] cloths; bool[] clothEnabled;
        Behaviour[] physics; bool[] physicsEnabled; Camera cameraMain; UniversalRenderPipelineAsset pipeline;
        float renderScale, shadowDistance; UniversalAdditionalCameraData cameraData; bool post;
        ScriptableRendererFeature[] features; bool[] featureEnabled;
        readonly List<ProfilerRecorder> recorders = new List<ProfilerRecorder>();
        readonly List<ProfilerRecorderDescription> counterDescriptions = new List<ProfilerRecorderDescription>();
        readonly List<double> frameTimes = new List<double>();
        readonly FrameTiming[] timings = new FrameTiming[1];
        int phase = -1, sampledFrame = -1; double start; bool finished;
        double warmup = 2, duration = 5;
        string[] names = { "baseline", "no_world_render", "no_player_render", "no_cloth", "no_secondary_cpu_and_cloth", "half_render_scale", "no_shadows_ssao_post", "final_baseline" };
        void Start()
        {
            if (report != null) return;
            try
            {
                cameraMain = Camera.main; cameraData = cameraMain.GetUniversalAdditionalCameraData(); post = cameraData.renderPostProcessing;
                pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline; renderScale = pipeline.renderScale; shadowDistance = pipeline.shadowDistance;
                features = new SerializedObject(pipeline).FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue is ScriptableRendererData data ? data.rendererFeatures.ToArray() : Array.Empty<ScriptableRendererFeature>();
                featureEnabled = features.Select(f => f.isActive).ToArray();
                renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None); rendererEnabled = renderers.Select(r => r.enabled).ToArray();
                renderingOff = renderers.Select(r => r.forceRenderingOff).ToArray(); playerRenderer = renderers.Select(r => r.GetComponentInParent<PlayerVisualRig>() != null).ToArray();
                cloths = Object.FindObjectsByType<Cloth>(FindObjectsSortMode.None); clothEnabled = cloths.Select(c => c.enabled).ToArray();
                physics = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Where(b => b is PlayerSecondaryMotionRig || b is PlayerClothCollisionBudgetRig || b is PlayerClothBodyProxyRig || b is PlayerClothBrushProxyRig).Cast<Behaviour>().ToArray();
                physicsEnabled = physics.Select(b => b.enabled).ToArray();
                var planes = GeometryUtility.CalculateFrustumPlanes(cameraMain);
                report = new Report { status = "RUNNING", hardware = SystemInfo.processorType + " / " + SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), width = cameraMain.pixelWidth, height = cameraMain.pixelHeight,
                    renderers = renderers.Length, meshRenderers = renderers.Count(r => r is MeshRenderer), lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length,
                    cloths = cloths.Length, lodGroups = Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None).Length, staticBatched = renderers.Count(r => r.isPartOfStaticBatch),
                    frustumVisible = renderers.Count(r => r.enabled && !r.forceRenderingOff && GeometryUtility.TestPlanesAABB(planes, r.bounds)), occlusionCamera = cameraMain.useOcclusionCulling,
                    shaders = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.shader.name).Distinct().ToList(),
                    scope = "Eight temporary 5-second ablations (2-second warmup, 3-second sample). Exclusions are diagnostic only. Frame update timing in Editor; GPU zero means unavailable. All original renderer/physics/pipeline settings restored." };
                var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
                if (names.Length == 1) report.scope = "Current-state Editor measurement, 5s warmup +5s samples. No ablation or quality setting changes. GPU0 means unavailable.";
                var descriptions = handles.Select(h => (h, d: ProfilerRecorderHandle.GetDescription(h))).ToArray();
                report.availableMarkers = descriptions.Where(v => new[] { "PlayerLoop", "Main Thread", "Render Thread", "Cloth", "Skin", "Cull", "Camera.Render", "RenderLoop", "WaitForPresent", "BehaviourUpdate", "Physics.Simulate", "GC Alloc", "Draw Calls", "SetPass", "Triangles Count", "Vertices Count", "Batches Count" }.Any(k => v.d.Name.Contains(k))).Select(v => v.d.Name).Distinct().ToList();
                foreach (var x in descriptions.Where(v => report.availableMarkers.Contains(v.d.Name)).Take(70))
                { var recorder = new ProfilerRecorder(x.h, 1, ProfilerRecorderOptions.Default); recorder.Start(); recorders.Add(recorder); counterDescriptions.Add(x.d); }
                Next(); EditorApplication.update += Update; AssemblyReloadEvents.beforeAssemblyReload += Finish;
            }
            catch (Exception e) { if (report == null) report = new Report(); report.error = e.ToString(); Finish(); }
        }
        void Restore()
        {
            if (renderers != null) for (int i = 0; i < renderers.Length; i++) if (renderers[i]) { renderers[i].enabled = rendererEnabled[i]; renderers[i].forceRenderingOff = renderingOff[i]; }
            if (physics != null) for (int i = 0; i < physics.Length; i++) if (physics[i] && physics[i].enabled != physicsEnabled[i]) physics[i].enabled = physicsEnabled[i];
            if (cloths != null) for (int i = 0; i < cloths.Length; i++) if (cloths[i]) cloths[i].enabled = clothEnabled[i];
            if (pipeline) { pipeline.renderScale = renderScale; pipeline.shadowDistance = shadowDistance; }
            if (cameraData) cameraData.renderPostProcessing = post;
            if (features != null) for (int i = 0; i < features.Length; i++) features[i].SetActive(featureEnabled[i]);
        }
        void Next()
        {
            Restore(); phase++;
            if (phase == names.Length) { Finish(); return; }
            if (phase == 1 || phase == 2) for (int i = 0; i < renderers.Length; i++) if (playerRenderer[i] == (phase == 2)) renderers[i].forceRenderingOff = true;
            if (phase == 3 || phase == 4) foreach (var c in cloths) c.enabled = false;
            if (phase == 4) foreach (var b in physics) b.enabled = false;
            if (phase == 5) pipeline.renderScale = .5f;
            if (phase == 6) { pipeline.shadowDistance = 0; cameraData.renderPostProcessing = false; foreach (var f in features) f.SetActive(false); }
            start = Time.realtimeSinceStartupAsDouble; frameTimes.Clear();
            var p = new Phase { name = names[phase] };
            foreach (var d in counterDescriptions) p.counters.Add(new Counter { name = d.Name, unit = d.UnitType.ToString() });
            report.phases.Add(p);
        }
        void Update()
        {
            if (finished || report == null || phase < 0 || sampledFrame == Time.frameCount) return;
            sampledFrame = Time.frameCount;
            if (!Application.isPlaying) { report.error = "Play Mode ended"; Finish(); return; }
            try
            {
                double elapsed = Time.realtimeSinceStartupAsDouble - start;
                if (elapsed > warmup)
                {
                    var p = report.phases[phase]; p.frames++; frameTimes.Add(Time.unscaledDeltaTime * 1000d);
                    for (int i = 0; i < recorders.Count; i++) if (recorders[i].Valid) p.counters[i].average += recorders[i].LastValue;
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0) { p.gpuMs += timings[0].gpuFrameTime; p.gpuSamples++; }
                }
                if (elapsed < duration) return;
                var result = report.phases[phase];
                if (result.frames > 0) { frameTimes.Sort(); result.frameMs = frameTimes.Average(); result.fps = 1000 / result.frameMs; result.p95Ms = frameTimes[Math.Min(frameTimes.Count - 1, (int)(frameTimes.Count * .95))]; foreach (var c in result.counters) c.average /= result.frames; }
                if (result.gpuSamples > 0) result.gpuMs /= result.gpuSamples;
                Next();
            }
            catch (Exception e) { report.error = e.ToString(); Finish(); }
        }
        void Finish()
        {
            if (finished) return; finished = true; EditorApplication.update -= Update; AssemblyReloadEvents.beforeAssemblyReload -= Finish; active = null;
            try { Restore(); report.restored = true; } catch (Exception e) { report.error += e.ToString(); }
            foreach (var r in recorders) r.Dispose();
            report.status = string.IsNullOrEmpty(report.error) ? "COMPLETE" : "FAILED";
            Directory.CreateDirectory("Screenshots/Performance"); File.WriteAllText("Screenshots/Performance/C2-ablation.json", JsonUtility.ToJson(report, true));
        }
    }
}
