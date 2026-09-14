using System;
using System.IO;
using System.Linq;
using Oheangbu.Data;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    public static class DosaV2PerformanceBuilder
    {
        public const string ProfilePath = "Assets/_Project/Data/PlayerV2/SecondaryMotion_DosaV2_Playable.asset";
        const string SourcePath = "Assets/_Project/Data/PlayerV2/SecondaryMotion_DosaV2_World.asset";

        public static string Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            string backup = Path.GetFullPath("../Art/PlayerV2/Backups/Performance_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(backup);
            foreach (string path in new[] { DosaV2PlayableDraftBuilder.VisualPrefabPath, ProfilePath })
                if (File.Exists(path)) File.Copy(path, Path.Combine(backup, Path.GetFileName(path)));
            var root = PrefabUtility.LoadPrefabContents(DosaV2PlayableDraftBuilder.VisualPrefabPath);
            try { ConfigurePlayable(root); PrefabUtility.SaveAsPrefabAsset(root, DosaV2PlayableDraftBuilder.VisualPrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            return "Applied runtime capsules, 60Hz springs/4 substeps, 120Hz cloth. Backup: " + backup;
        }

        public static void ConfigurePlayable(GameObject root)
        {
            var profile = AssetDatabase.LoadAssetAtPath<PlayerSecondaryMotionProfileSO>(ProfilePath);
            if (profile == null)
            {
                profile = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PlayerSecondaryMotionProfileSO>(SourcePath));
                profile.name = "SecondaryMotion_DosaV2_Playable";
                profile.UseCapsuleOrnamentCollision = true;
                profile.CapsuleCollisionIterations = 3;
                profile.SimulationHz = 60;
                profile.MaximumSubsteps = 4;
                profile.MaximumCatchUpSeconds = 4f / 60f;
                profile.Cloth.SolverFrequency = 120;
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            var world = root.GetComponentsInChildren<PlayerSecondaryMotionRig>(true)
                .Single(r => r.GetComponentsInChildren<Cloth>(true).Length > 0);
            var serialized = new SerializedObject(world);
            serialized.FindProperty("_profile").objectReferenceValue = profile;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            profile.ClothColliderSelectionHz = 20;
            foreach (var selector in world.GetComponentsInChildren<PlayerClothCollisionBudgetRig>(true))
                selector.RuntimeSelectionHz = profile.ClothColliderSelectionHz;
            foreach (var cloth in world.GetComponentsInChildren<Cloth>(true)) cloth.clothSolverFrequency = profile.Cloth.SolverFrequency;
            EditorUtility.SetDirty(profile);
        }

        public static string ValidateGeometry()
        {
            var random = new System.Random(180);
            float largestOutside = 0;
            for (int shape = 0; shape < 24; shape++)
            {
                var points = new Vector3[128];
                for (int i = 0; i < points.Length; i++) points[i] = new Vector3(
                    (float)random.NextDouble() * .07f, (float)random.NextDouble() * (shape == 0 ? 0 : .4f), (float)random.NextDouble() * .09f);
                var capsule = PlayerSecondaryCollisionRig.FitRuntimeCapsule(points);
                foreach (var p in points)
                {
                    var nearest = PlayerSecondaryCollisionGeometry.ClosestSegmentPoint(p, capsule.A, capsule.B);
                    largestOutside = Mathf.Max(largestOutside, Vector3.Distance(nearest, p) - capsule.Radius);
                }
            }
            if (largestOutside > .00001f) throw new InvalidOperationException("Runtime proxy does not enclose its fitting points.");
            PlayerSecondaryCollisionGeometry.SegmentSegment(Vector3.zero, Vector3.zero, Vector3.right, Vector3.right,
                out var a, out var b);
            if (!PlayerSecondaryCollisionGeometry.Finite(a) || !PlayerSecondaryCollisionGeometry.Finite(b) || Mathf.Abs(Vector3.Distance(a, b) - 1) > .00001f)
                throw new InvalidOperationException("Degenerate capsule axes failed.");
            return "PASS: 3072 fitting points enclosed; zero-length capsule axes finite. Largest outside metres=" + largestOutside;
        }

        public static string ValidateRuntimeCaches()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode required.");
            var results = new System.Collections.Generic.List<string>();
            foreach (var collision in UnityEngine.Object.FindObjectsByType<PlayerSecondaryCollisionRig>(FindObjectsSortMode.None))
                if (collision.UsesRuntimeCapsules) results.Add(collision.ValidateRuntimeQueryEquivalence());
            foreach (var brush in UnityEngine.Object.FindObjectsByType<PlayerClothBrushProxyRig>(FindObjectsSortMode.None))
            {
                var root = brush.WorldBrush.transform; var rotation = root.rotation;
                try
                {
                    brush.RefreshNow();
                    root.rotation = Quaternion.AngleAxis(47f, Vector3.up) * rotation;
                    if (!brush.RefreshNow()) throw new InvalidOperationException(brush.LastError);
                    var capsules = brush.Capsules; var radii = capsules.Select(c => c.radius).ToArray();
                    if (!brush.RefreshWithForcedBake()) throw new InvalidOperationException(brush.LastError);
                    float largest = 0;
                    for (int i = 0; i < capsules.Length; i++) largest = Mathf.Max(largest, Mathf.Abs(radii[i] - capsules[i].radius));
                    if (largest > .00001f) throw new InvalidOperationException("Cached brush envelope changed after rigid root rotation: " + largest);
                    results.Add("PASS " + capsules.Length + " brush radii after rigid root rotation, maximum local difference=" + largest);
                }
                finally { root.rotation = rotation; brush.RefreshWithForcedBake(); }
            }
            return string.Join("\n", results);
        }

        public static string ValidateRemainingOptimizations()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode required.");
            var results = new System.Collections.Generic.List<string>();
            foreach (var brush in UnityEngine.Object.FindObjectsByType<PlayerClothBrushProxyRig>(FindObjectsSortMode.None))
            {
                var root = brush.WorldBrush.transform;
                var position = root.position; var rotation = root.rotation; var scale = root.localScale;
                bool cache = brush.CacheStaticShaftRadius;
                try
                {
                    for (int sample = 0; sample < 4; sample++)
                    {
                        root.SetPositionAndRotation(position + new Vector3(.17f, .08f, -.12f) * sample,
                            Quaternion.AngleAxis(47f * sample, Vector3.up) * rotation);
                        root.localScale = Vector3.Scale(scale, sample == 3 ? new Vector3(1.1f, .9f, 1.2f) : Vector3.one * (1f + .1f * sample));
                        brush.CacheStaticShaftRadius = true;
                        var audit = brush.AuditAgainstFullSource(1);
                        if (!audit.geometryEquivalent) throw new InvalidOperationException(JsonUtility.ToJson(audit));
                        results.Add("PASS shaft sample " + sample + ": " + audit.checkedRawPoints + " raw points, max radius difference=" + audit.maximumRadiusDifferenceMeters);
                    }
                }
                finally
                {
                    root.localScale = scale; root.SetPositionAndRotation(position, rotation);
                    brush.CacheStaticShaftRadius = cache; brush.RefreshWithForcedBake();
                }
            }
            foreach (var rig in UnityEngine.Object.FindObjectsByType<PlayerVisualRig>(FindObjectsSortMode.None))
            {
                var animator = new SerializedObject(rig).FindProperty("_nearAnimator").objectReferenceValue as Animator;
                if (animator == null) continue;
                var mode = animator.cullingMode; bool cull = rig.CullHiddenNearAnimation;
                try
                {
                    rig.CullHiddenNearAnimation = true;
                    var method = typeof(PlayerVisualRig).GetMethod("SetNearAnimationVisibility", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    method.Invoke(rig, new object[] { false, false });
                    if (animator.cullingMode != AnimatorCullingMode.CullUpdateTransforms) throw new InvalidOperationException("Hidden near transforms still animate.");
                    int before = rig.LateNearPoseRefreshCount;
                    method.Invoke(rig, new object[] { true, true });
                    if (animator.cullingMode != AnimatorCullingMode.AlwaysAnimate || rig.LateNearPoseRefreshCount != before + 1)
                        throw new InvalidOperationException("Final-camera wake did not refresh the near pose immediately.");
                    results.Add("PASS hidden near culling and same-frame final-camera pose refresh.");
                }
                finally { rig.CullHiddenNearAnimation = cull; animator.cullingMode = mode; }
            }
            string output = string.Join("\n", results);
            File.WriteAllText(Path.GetFullPath("Screenshots/PlayerDosaV2/performance-remaining-validation.txt"), output);
            return output;
        }
    }
}
