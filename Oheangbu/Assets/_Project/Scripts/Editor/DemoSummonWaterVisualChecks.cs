using System;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    /// <summary>Inspects real water presentation geometry using the authored materials; no campaign mutation.</summary>
    public static class DemoSummonWaterVisualChecks
    {
        const string ProfilePath = "Assets/_Project/Art/Demo/Summons/WaterTurtle/Combat_Om.asset";
        const string OwnedMeshName = "TurtleWater_OwnedSurface";
        const float Epsilon = .0002f;
        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Shader compilation and rendered appearance", "Authored mouth alignment in gameplay",
                "GPU performance and player visual approval" };
        }
        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Edit-mode presentation diagnostic only");
            var report = new Report(); Scene scene = default; GameObject root = null;
            var baseline = OwnedMeshIds();
            void Check(bool pass, string name) => (pass ? report.passed : report.failed).Add(name);
            try
            {
                var profile = AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(ProfilePath);
                if (profile == null) throw new InvalidOperationException("Authored water profile missing: " + ProfilePath);
                Check(profile.WaterAttackEnabled && profile.WaterJetMaterial != null && profile.WaterFoamMaterial != null,
                    "Actual water profile enables combat and supplies jet/foam materials");
                if (profile.WaterJetMaterial == null || profile.WaterFoamMaterial == null)
                    throw new InvalidOperationException("Both authored water materials are required for geometry and particle diagnostics");
                scene = EditorSceneManager.NewPreviewScene(); root = new GameObject("Isolated water presentation diagnostic");
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.SetPositionAndRotation(new Vector3(5, 2, -4), Quaternion.Euler(0, 31, 0));
                var presentation = root.AddComponent<DemoSummonWaterPresentation>();
                var plan = Plan(profile); float travel = plan.Range / plan.TravelSpeed;
                presentation.Sample(profile, plan, plan.StartedAt, true);
                Check(presentation.VisibleLength == 0 && presentation.LiveDrops == 0 && root.GetComponentsInChildren<MeshFilter>(true).Length == 0,
                    "Windup creates no visible tube or particles");

                float[] times = { plan.ReleaseAt, plan.ReleaseAt + travel * .008f, plan.ReleaseAt + travel * .025f,
                    plan.ReleaseAt + travel * .25f, plan.ReleaseAt + travel * .6f, plan.ReleaseAt + travel,
                    plan.StreamEndAt - .001f, plan.StreamEndAt + travel * .1f, plan.StreamEndAt + travel * .6f,
                    plan.StreamEndAt + travel * .975f };
                bool geometryBounded = true, particlesBounded = true, endpointMatch = true; int inspectedVertices = 0, inspectedParticles = 0;
                foreach (float time in times)
                {
                    plan.AdvanceTo(time); presentation.Sample(profile, plan, time, true);
                    InspectBounds(root, plan, ref geometryBounded, ref particlesBounded, ref inspectedVertices, ref inspectedParticles);
                    if (presentation.VisibleLength > 0)
                        endpointMatch &= Near(presentation.VisibleStart, plan.VisibleStart) && Near(presentation.VisibleEnd, plan.VisibleEnd) &&
                            Math.Abs(presentation.VisibleLength - (plan.FrontDistance - plan.TailDistance)) < Epsilon;
                }
                Check(inspectedVertices > 0 && geometryBounded, "Every sampled tube vertex stays inside corridor radius and tail/front planes (" + inspectedVertices + " vertices)");
                Check(inspectedParticles > 0 && particlesBounded,
                    "Every sampled particle including startSize/2 stays inside radius and tail/front planes (" + inspectedParticles + " particles)");
                Check(endpointMatch, "Presentation endpoints and lengths match immutable plan samples through travel and recovery");
                Check(!plan.IsStreaming && plan.IsVisible && presentation.VisibleLength < plan.Range * .03f,
                    "Tail drains after mouth emission stops instead of discarding the full stream");

                plan = Plan(profile); float pausedAt = plan.ReleaseAt + travel * .7f; plan.AdvanceTo(pausedAt);
                presentation.Sample(profile, plan, pausedAt, true);
                var meshFilter = root.GetComponentInChildren<MeshFilter>(true);
                var particleSystem = root.GetComponentInChildren<ParticleSystem>(true);
                if (meshFilter == null || particleSystem == null) throw new InvalidOperationException("Actual tube or foam system was not created");
                Mesh ownedMesh = meshFilter.sharedMesh;
                Check(meshFilter.GetComponent<MeshRenderer>().sharedMaterial == profile.WaterJetMaterial &&
                    particleSystem.GetComponent<ParticleSystemRenderer>().sharedMaterial == profile.WaterFoamMaterial,
                    "Actual presentation uses authored jet and foam materials");
                var initialVertices = ownedMesh.vertices; var initialParticles = Particles(particleSystem);
                Vector3 initialStart = presentation.VisibleStart, initialEnd = presentation.VisibleEnd;
                bool unchanged = true;
                for (int i = 0; i < 20; i++)
                {
                    presentation.Sample(profile, plan, pausedAt, true);
                    unchanged &= Same(initialVertices, ownedMesh.vertices) && Same(initialParticles, Particles(particleSystem)) &&
                        Near(initialStart, presentation.VisibleStart) && Near(initialEnd, presentation.VisibleEnd);
                }
                Check(unchanged && ownedMesh == meshFilter.sharedMesh, "Twenty equal-time samples preserve mesh, particles and endpoints without allocation of another mesh");
                Check(!particleSystem.main.playOnAwake && !particleSystem.emission.enabled && !particleSystem.isPlaying,
                    "Foam has no autonomous emission or running simulation clock");

                plan.AdvanceTo(plan.EndAt + .01f); presentation.Sample(profile, plan, plan.SampleTime, true);
                Check(Hidden(root, presentation), "Completed trailing edge hides tube and clears particles");
                plan = Plan(profile); plan.AdvanceTo(plan.ReleaseAt + travel * .7f);
                Clip(plan, plan.Range * .2f); float clipped = plan.ClearDistance; Clip(plan, plan.Range);
                presentation.Sample(profile, plan, plan.SampleTime, true);
                bool clipGeometry = true, clipParticles = true; int cv = 0, cp = 0;
                InspectBounds(root, plan, ref clipGeometry, ref clipParticles, ref cv, ref cp);
                Check(Math.Abs(plan.ClearDistance - clipped) < Epsilon && Math.Abs(presentation.VisibleLength - clipped) < Epsilon &&
                    clipGeometry && clipParticles && cv > 0 && cp > 0, "Monotonic wall clipping bounds complete tube and particle extents and cannot reopen");
                plan.AdvanceTo(plan.StreamEndAt + clipped / plan.TravelSpeed + .005f);
                presentation.Sample(profile, plan, plan.SampleTime, true);
                Check(Hidden(root, presentation), "Clipped stream drains at its shortened endpoint");

                plan = Plan(profile); plan.AdvanceTo(plan.ReleaseAt + travel * .7f); presentation.Sample(profile, plan, plan.SampleTime, true);
                plan.Cancel(); presentation.Sample(profile, plan, plan.SampleTime, true);
                Check(Hidden(root, presentation), "Cancellation hides owned geometry and clears all live particles");
                plan = Plan(profile); plan.AdvanceTo(plan.ReleaseAt + travel * .7f); presentation.Sample(profile, plan, plan.SampleTime, true);
                presentation.Sample(profile, plan, plan.SampleTime, false);
                Check(Hidden(root, presentation), "Inactive combat hides water immediately");
                presentation.Sample(profile, plan, plan.SampleTime, true); presentation.enabled = false;
                Check(Hidden(root, presentation), "Component disable hides geometry and clears particles");
                presentation.enabled = true; presentation.Sample(profile, plan, plan.SampleTime, true);
                Check(presentation.VisibleLength > 0 && presentation.LiveDrops > 0 && meshFilter.sharedMesh == ownedMesh,
                    "Re-enable reuses the single owned mesh and deterministic foam");
                UnityEngine.Object.DestroyImmediate(presentation);
                Check(ownedMesh == null && root.GetComponentsInChildren<MeshFilter>(true).Length == 0 &&
                    root.GetComponentsInChildren<ParticleSystem>(true).Length == 0 && SameIds(baseline, OwnedMeshIds()),
                    "Destroy removes tube children, foam systems and every newly owned mesh");
            }
            catch (Exception exception) { report.failed.Add(exception.ToString()); }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                Check(SameIds(baseline, OwnedMeshIds()), "Diagnostic cleanup leaves no additional owned water meshes");
            }
            report.status = report.failed.Count == 0 ? "PASS" : "FAIL"; return JsonUtility.ToJson(report, true);
        }
        static SummonWaterAttackPlan Plan(SummonCombatProfile profile) => new SummonWaterAttackPlan(
            new Vector3(12, 4, 7), new Vector3(.37f, .16f, .91f), profile.WaterRange, profile.WaterRadius, profile.WaterTravelSpeed,
            10, profile.WaterWindupSeconds, profile.WaterStreamSeconds, profile.WaterRecoverySeconds);
        static void Clip(SummonWaterAttackPlan plan, float distance) => typeof(SummonWaterAttackPlan)
            .GetMethod("ClipTo", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(plan, new object[] { distance });
        static void InspectBounds(GameObject root, SummonWaterAttackPlan plan, ref bool geometry, ref bool particles, ref int vertices, ref int drops)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(false))
            {
                if (filter.sharedMesh == null) continue;
                foreach (Vector3 local in filter.sharedMesh.vertices)
                { vertices++; geometry &= Within(plan, filter.transform.TransformPoint(local), 0); }
            }
            foreach (var system in root.GetComponentsInChildren<ParticleSystem>(false))
                foreach (var particle in Particles(system))
                {
                    drops++;
                    // Billboard half-size is included in radial AND axial bounds, rather than checking only centres.
                    float scale = Mathf.Max(Mathf.Abs(system.transform.lossyScale.x), Mathf.Abs(system.transform.lossyScale.y), Mathf.Abs(system.transform.lossyScale.z));
                    particles &= Within(plan, system.transform.TransformPoint(particle.position), particle.startSize * .5f * scale);
                }
        }
        static bool Within(SummonWaterAttackPlan plan, Vector3 world, float extent)
        {
            Vector3 delta = world - plan.Origin; float along = Vector3.Dot(delta, plan.Direction);
            float radial = (delta - plan.Direction * along).magnitude;
            return float.IsFinite(along) && float.IsFinite(radial) && along - extent >= plan.TailDistance - Epsilon &&
                along + extent <= plan.FrontDistance + Epsilon && radial + extent <= plan.Radius + Epsilon;
        }
        static bool Hidden(GameObject root, DemoSummonWaterPresentation presentation)
        {
            if (presentation.VisibleLength != 0 || presentation.LiveDrops != 0) return false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) if (renderer.gameObject.activeInHierarchy) return false;
            foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true)) if (system.particleCount != 0) return false;
            return true;
        }
        static ParticleSystem.Particle[] Particles(ParticleSystem system)
        {
            var particles = new ParticleSystem.Particle[system.main.maxParticles]; int count = system.GetParticles(particles);
            Array.Resize(ref particles, count); return particles;
        }
        static bool Same(Vector3[] a, Vector3[] b)
        { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (!Near(a[i], b[i])) return false; return true; }
        static bool Same(ParticleSystem.Particle[] a, ParticleSystem.Particle[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!Near(a[i].position, b[i].position) || !Near(a[i].velocity, b[i].velocity) ||
                a[i].startSize != b[i].startSize || a[i].remainingLifetime != b[i].remainingLifetime ||
                a[i].startLifetime != b[i].startLifetime || a[i].rotation != b[i].rotation || !a[i].startColor.Equals(b[i].startColor)) return false;
            return true;
        }
        static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < Epsilon * Epsilon;
        static HashSet<int> OwnedMeshIds()
        { var result = new HashSet<int>(); foreach (var mesh in Resources.FindObjectsOfTypeAll<Mesh>()) if (mesh.name == OwnedMeshName) result.Add(mesh.GetInstanceID()); return result; }
        static bool SameIds(HashSet<int> a, HashSet<int> b) => a.SetEquals(b);
    }
}
