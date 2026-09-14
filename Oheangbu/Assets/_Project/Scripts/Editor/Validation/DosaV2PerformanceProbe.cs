using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // Temporary runtime-only ablation. Never saves scene or asset changes.
    [InitializeOnLoad]
    public static class DosaV2PerformanceProbe
    {
        [Serializable] public sealed class Phase
        {
            public string name;
            public int frames;
            public double frameMs, fps, collisionMs, bodyProxyMs, brushProxyMs, clothBudgetMs;
        }
        [Serializable] public sealed class Report
        {
            public string status, error, scope = "Editor idle same-camera ablation; not build GPU profiling. Restores all runtime toggles.";
            public int vSync, targetFps, clothCount;
            public bool restored;
            public List<Phase> phases = new List<Phase>();
        }
        static Report report;
        static PlayerSecondaryMotionRig[] rigs;
        static PlayerSecondaryCollisionRig[] collisions;
        static bool[] enabledRigs, enabledCloths;
        static Cloth[] cloths;
        static PlayerClothBodyProxyRig[] bodies;
        static PlayerClothBrushProxyRig[] brushes;
        static PlayerClothCollisionBudgetRig[] budgets;
        static double start;
        static bool compareFastPaths;
        static bool remainingCosts;
        static bool compareRemainingOptimizations;
        static bool compareRenderBatches;
        static PlayerModelRenderBatch[] renderBatches;
        static bool[] savedRenderBatches;
        static PlayerVisualRig[] visuals;
        static bool[] savedNearCulling, savedShaftCaches;
        static UniversalRenderPipelineAsset pipeline;
        static bool savedOpaqueTexture;
        static Animator[] nearAnimators;
        static bool[] savedNearEnabled, savedNearKeepState;
        static bool[] savedQueryOptimizations, savedRadiusCaches;
        static int phase, frame;
        static readonly string[] normalNames = { "baseline", "without_ornament_triangle_collision", "baseline_restored", "without_native_cloth", "without_secondary_motion_and_cloth", "final_baseline" };
        static readonly string[] fastNames = { "previous", "optimized", "previous_repeat", "optimized_repeat" };
        static readonly string[] remainingNames = { "baseline", "without_hidden_near_animator", "baseline_restored", "without_native_cloth", "final_baseline" };
        static string[] names;
        static string PathName => Path.GetFullPath("Screenshots/PlayerDosaV2/performance-probe.json");
        static DosaV2PerformanceProbe() { AssemblyReloadEvents.beforeAssemblyReload += Finish; }

        public static string Begin() => BeginInternal(false);
        public static string BeginFastPathComparison() => BeginInternal(true);
        public static string BeginRemainingCostProbe() => BeginInternal(false, true);
        public static string BeginRemainingOptimizationComparison() => BeginInternal(false, false, true);
        public static string BeginRenderBatchComparison() => BeginInternal(false, false, false, true);
        private static string BeginInternal(bool fastPaths, bool remaining = false, bool compareRemaining = false, bool compareBatches = false)
        {
            if (!EditorApplication.isPlaying || report?.status == "RUNNING") return "Requires idle Play Mode and no active probe.";
            compareFastPaths = fastPaths; names = fastPaths ? fastNames : normalNames;
            remainingCosts = remaining;
            compareRemainingOptimizations = compareRemaining;
            compareRenderBatches = compareBatches;
            renderBatches = Object.FindObjectsByType<PlayerModelRenderBatch>(FindObjectsSortMode.None);
            savedRenderBatches = renderBatches.Select(b => b.UseCombined).ToArray();
            if (remaining) names = remainingNames;
            if (compareRemaining) names = fastNames;
            if (compareBatches) names = fastNames;
            visuals = Object.FindObjectsByType<PlayerVisualRig>(FindObjectsSortMode.None);
            savedNearCulling = visuals.Select(v => v.CullHiddenNearAnimation).ToArray();
            nearAnimators = visuals
                .Select(r => new SerializedObject(r).FindProperty("_nearAnimator").objectReferenceValue as Animator)
                .Where(a => a != null).ToArray();
            savedNearEnabled = nearAnimators.Select(a => a.enabled).ToArray();
            savedNearKeepState = nearAnimators.Select(a => a.keepAnimatorStateOnDisable).ToArray();
            rigs = Object.FindObjectsByType<PlayerSecondaryMotionRig>(FindObjectsSortMode.None);
            collisions = rigs.Select(r => r.SecondaryCollision).ToArray();
            enabledRigs = rigs.Select(r => r.enabled).ToArray();
            cloths = Object.FindObjectsByType<Cloth>(FindObjectsSortMode.None);
            enabledCloths = cloths.Select(c => c.enabled).ToArray();
            bodies = Object.FindObjectsByType<PlayerClothBodyProxyRig>(FindObjectsSortMode.None);
            brushes = Object.FindObjectsByType<PlayerClothBrushProxyRig>(FindObjectsSortMode.None);
            savedShaftCaches = brushes.Select(b => b.CacheStaticShaftRadius).ToArray();
            pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            savedOpaqueTexture = pipeline != null && pipeline.supportsCameraOpaqueTexture;
            savedQueryOptimizations = collisions.Select(c => c != null && c.OptimizeRuntimeCapsuleQueries).ToArray();
            savedRadiusCaches = brushes.Select(b => b.CacheUnchangedRadiusFits).ToArray();
            budgets = Object.FindObjectsByType<PlayerClothCollisionBudgetRig>(FindObjectsSortMode.None);
            report = new Report { status = "RUNNING", vSync = QualitySettings.vSyncCount, targetFps = Application.targetFrameRate, clothCount = cloths.Length };
            if (fastPaths) report.scope = "Same-session previous/optimized alternating CPU cache comparison. Cloth, springs, collision envelopes and render settings stay enabled and identical. Restores toggles.";
            if (compareRemaining) report.scope = "Same-session alternating hidden-near Humanoid transform culling + static shaft radius cache + unused opaque texture removal. No cloth, animation clock or render resolution changes. Restores toggles. Proxy timing fields are last-call timings, not amortized frame costs.";
            if (compareBatches) report.scope = "Same-session alternating original/combined world mesh renderers. Identical triangle geometry, skeleton, Cloth, shadow and render settings. Restores renderer mode; Editor idle measurement, not build/GPU profiling.";
            phase = 0; frame = -1; Next();
            EditorApplication.update += Tick;
            return PathName;
        }
        static void Restore()
        {
            for (int i = 0; i < rigs.Length; i++) if (rigs[i])
            {
                if (rigs[i].SecondaryCollision != collisions[i] && !rigs[i].ConfigureCollision(collisions[i]))
                    throw new InvalidOperationException("Collision restoration failed");
                if (rigs[i].enabled != enabledRigs[i]) rigs[i].enabled = enabledRigs[i];
            }
            for (int i = 0; i < cloths.Length; i++) if (cloths[i] && cloths[i].enabled != enabledCloths[i]) cloths[i].enabled = enabledCloths[i];
            for (int i = 0; i < collisions.Length; i++) if (collisions[i]) collisions[i].OptimizeRuntimeCapsuleQueries = savedQueryOptimizations[i];
            for (int i = 0; i < brushes.Length; i++) if (brushes[i]) brushes[i].CacheUnchangedRadiusFits = savedRadiusCaches[i];
            for (int i = 0; i < brushes.Length; i++) if (brushes[i]) brushes[i].CacheStaticShaftRadius = savedShaftCaches[i];
            for (int i = 0; i < visuals.Length; i++) if (visuals[i]) visuals[i].CullHiddenNearAnimation = savedNearCulling[i];
            if (pipeline != null) pipeline.supportsCameraOpaqueTexture = savedOpaqueTexture;
            for (int i = 0; i < renderBatches.Length; i++) if (renderBatches[i]) renderBatches[i].SetCombined(savedRenderBatches[i]);
            for (int i = 0; i < nearAnimators.Length; i++) if (nearAnimators[i])
            {
                nearAnimators[i].enabled = savedNearEnabled[i];
                nearAnimators[i].keepAnimatorStateOnDisable = savedNearKeepState[i];
            }
        }
        static void Next()
        {
            Restore();
            if (compareRenderBatches)
            {
                foreach (var b in renderBatches) b.SetCombined(phase % 2 == 1);
            }
            else if (compareRemainingOptimizations)
            {
                bool optimized = phase % 2 == 1;
                foreach (var v in visuals) v.CullHiddenNearAnimation = optimized;
                foreach (var b in brushes) b.CacheStaticShaftRadius = optimized;
                if (pipeline != null) pipeline.supportsCameraOpaqueTexture = !optimized;
            }
            else if (compareFastPaths)
            {
                foreach (var c in collisions) if (c) { c.OptimizeRuntimeCapsuleQueries = phase % 2 == 1; c.ResetHistory(); }
                foreach (var b in brushes) b.CacheUnchangedRadiusFits = phase % 2 == 1;
            }
            else if (remainingCosts)
            {
                if (phase == 1) foreach (var a in nearAnimators) { a.keepAnimatorStateOnDisable = true; a.enabled = false; }
                if (phase == 3) foreach (var c in cloths) c.enabled = false;
            }
            else
            {
                if (phase == 1) foreach (var r in rigs) r.ConfigureCollision(null);
                if (phase == 3) foreach (var c in cloths) c.enabled = false;
                if (phase == 4) { foreach (var r in rigs) r.enabled = false; foreach (var c in cloths) c.enabled = false; }
            }
            report.phases.Add(new Phase { name = names[phase] });
            start = EditorApplication.timeSinceStartup;
        }
        static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying) { report.error = "Play mode ended"; Finish(); return; }
                if (frame == Time.frameCount) return;
                frame = Time.frameCount;
                double elapsed = EditorApplication.timeSinceStartup - start;
                var p = report.phases[phase];
                if (elapsed > 1.0)
                {
                    p.frames++; p.frameMs += Time.unscaledDeltaTime * 1000.0;
                    if (compareFastPaths || compareRemainingOptimizations || compareRenderBatches || remainingCosts || (phase != 1 && phase != 4)) p.collisionMs += collisions.Where(c => c != null).Sum(c => c.LastFrameMainThreadMilliseconds);
                    p.bodyProxyMs += bodies.Sum(b => b.LastRefreshMilliseconds);
                    p.brushProxyMs += brushes.Sum(b => b.LastRefreshMilliseconds);
                    p.clothBudgetMs += budgets.Sum(b => b.LastRefreshMilliseconds);
                }
                if (elapsed < 4) return;
                if (p.frames > 0)
                {
                    p.frameMs /= p.frames; p.fps = 1000.0 / p.frameMs;
                    p.collisionMs /= p.frames; p.bodyProxyMs /= p.frames; p.brushProxyMs /= p.frames; p.clothBudgetMs /= p.frames;
                }
                phase++;
                if (phase == names.Length) Finish(); else Next();
            }
            catch (Exception e) { report.error = e.ToString(); Finish(); }
        }
        static void Finish()
        {
            EditorApplication.update -= Tick;
            if (report == null || report.status != "RUNNING") return;
            try { Restore(); report.restored = true; }
            catch (Exception e) { report.error += e.ToString(); }
            report.status = string.IsNullOrEmpty(report.error) ? "COMPLETE" : "INCOMPLETE";
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName));
            File.WriteAllText(PathName, JsonUtility.ToJson(report, true));
        }
    }
}
