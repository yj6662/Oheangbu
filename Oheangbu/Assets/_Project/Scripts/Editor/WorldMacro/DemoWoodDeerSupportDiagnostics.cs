using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Explicitly invoked diagnostics only. No InitializeOnLoad, scene edits, asset edits or refresh.
    public static class DemoWoodDeerSupportDiagnostics
    {
        const string ProfilePath = "Assets/_Project/Art/Demo/Summons/WoodDeer/Combat_Gom.asset";
        static readonly string[] Feet = { "Fore_L", "Fore_R", "Hind_L", "Hind_R" };
        [Serializable] public class TransformRow
        { public string path; public Vector3 localPosition, localEuler, localScale, worldPosition, lossyScale; }
        [Serializable] public class HitRow
        {
            public string path, colliderType, layer, mesh; public Vector3 point, normal;
            public float distance, slope; public bool ignoredCharacter, slopeRejected, rendererEnabled;
        }
        [Serializable] public class GroundRow
        {
            public string label; public Vector3 queriedPoint; public bool chosen, saturated;
            public float rawGap, gapToManagerFeet; public HitRow chosenHit; public List<HitRow> hits = new List<HitRow>();
        }
        [Serializable] public class FootRow
        { public string name; public int candidates; public Vector3 bonePosition, lowestMeshPoint, lowestActorLocal; public GroundRow support; }
        [Serializable] public class SkinRow
        { public string path, rootBone; public Bounds rendererBounds, localBounds, bakedWorldBounds; public List<FootRow> feet = new List<FootRow>(); }
        [Serializable] public class PoseRow
        {
            public string source, phase; public float elapsed, phaseElapsed, attackTime, idleWeight, walkWeight, attackWeight, rootAttackWeight;
            public string footIkStatus; public float footBodyDrop; public int footRayQueries; public bool footSupported;
            public DemoWoodDeerFootPlacement.FootResult[] footCorrections;
            public Vector3 actorPosition; public List<TransformRow> transforms = new List<TransformRow>();
            public List<SkinRow> skins = new List<SkinRow>(); public GroundRow actorGround;
        }
        [Serializable] public class Report
        {
            public string status = "MEASURED_NOT_A_GAMEPLAY_PASS", scope;
            public float configuredFootTolerance, configuredBodyHeight, configuredFootprintWidth, configuredFootprintLength;
            public List<PoseRow> poses = new List<PoseRow>();
        }
        static string PathOf(Transform t)
        { if (t == null) return null; var names = new List<string>(); while (t != null) { names.Add(t.name); t = t.parent; } names.Reverse(); return string.Join("/", names); }
        static string Save(string filename, Report report)
        {
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Demo/Summons/WoodDeer"));
            Directory.CreateDirectory(directory); string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(directory, filename), json); return json;
        }
        static Report NewReport(SummonCombatProfile p, string scope) => new Report
        { scope = scope, configuredFootTolerance = p.FootHeightTolerance, configuredBodyHeight = p.BodyHeight,
          configuredFootprintWidth = p.FootprintWidth, configuredFootprintLength = p.FootprintLength };

        // Snapshot the existing actor/pose without changing time, graph, transforms, physics or any asset.
        public static string CaptureActive()
        {
            var manager = Object.FindFirstObjectByType<DemoSummonCombatManager>();
            if (manager == null || manager.Active == null) throw new InvalidOperationException("An existing active summon is required; this diagnostic does not spawn one.");
            var actor = manager.Active; var p = manager.Profiles.First(x => x != null && x.Letter == "곰");
            var clock = manager.ActiveClock;
            var report = NewReport(p, "Read-only snapshot of existing runtime pose using BakeMesh and owned PhysicsScene rays. No pose/time modification.");
            var row = CapturePose(actor, "active runtime", p, manager, true);
            row.phase = clock.Phase.ToString(); row.elapsed = clock.Elapsed; row.phaseElapsed = clock.PhaseElapsed;
            var visual = actor.GetComponent<DemoSummonPresentation>();
            if (visual != null)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                float start = (float)typeof(DemoSummonPresentation).GetField("attackStarted", flags).GetValue(visual);
                row.attackTime = float.IsFinite(start) ? clock.Elapsed - start : -1;
                var mix = (AnimationMixerPlayable)typeof(DemoSummonPresentation).GetField("mixer", flags).GetValue(visual);
                if (mix.IsValid()) { row.idleWeight = mix.GetInputWeight(0); row.walkWeight = mix.GetInputWeight(1); row.attackWeight = mix.GetInputWeight(2); row.rootAttackWeight = mix.GetInputCount()>3 ? mix.GetInputWeight(3) : 0; }
            }
            var feet = actor.GetComponent<DemoWoodDeerFootPlacement>();
            if(feet!=null){row.footIkStatus=feet.LastStatus;row.footBodyDrop=feet.BodyDrop;row.footRayQueries=feet.RayQueriesLastSample;row.footSupported=feet.SupportedLastSample;row.footCorrections=feet.Diagnostics;}
            report.poses.Add(row); return Save("unity_active_support_diagnostic.json", report);
        }

        // Separate temporary preview scene; never changes an existing scene/actor/profile or saves a scene.
        public static string InspectImportedPoses()
        {
            var p = AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(ProfilePath);
            if (p == null || p.PresentationPrefab == null) throw new InvalidOperationException("Imported combat prefab/profile required.");
            var report = NewReport(p, "Actual imported Generic animation via the same manual three-input mixer in an isolated preview scene. No terrain/active gameplay scene involved.");
            Scene preview = EditorSceneManager.NewPreviewScene(); PlayableGraph graph = default;
            try
            {
                var actor = (GameObject)PrefabUtility.InstantiatePrefab(p.PresentationPrefab, preview);
                actor.name = "Diagnostic_ImportedDeer";
                var animator = actor.GetComponent<Animator>(); if (animator == null) animator = actor.AddComponent<Animator>();
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.runtimeAnimatorController = null;
                graph = PlayableGraph.Create("Independent hoof support diagnostic"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var mixer = AnimationMixerPlayable.Create(graph, 3);
                var idle = AnimationClipPlayable.Create(graph, p.IdleClip); var walk = AnimationClipPlayable.Create(graph, p.WalkClip); var attack = AnimationClipPlayable.Create(graph, p.AttackClip);
                graph.Connect(idle, 0, mixer, 0); graph.Connect(walk, 0, mixer, 1); graph.Connect(attack, 0, mixer, 2);
                idle.SetSpeed(0); walk.SetSpeed(0); attack.SetSpeed(0);
                var output = AnimationPlayableOutput.Create(graph, "Rig", animator); output.SetSourcePlayable(mixer); graph.Play();
                foreach (float t in new[] { 0f, .40f, .70f, .75f, .80f, .85f, .90f, .95f, 1.2f, 1.4f })
                {
                    idle.SetTime(0); walk.SetTime(0); attack.SetTime(t);
                    mixer.SetInputWeight(0, 0); mixer.SetInputWeight(1, 0); mixer.SetInputWeight(2, 1); graph.Evaluate(0);
                    var row = CapturePose(actor, "isolated imported Horn clip", p, null, false); row.attackTime = t; row.attackWeight = 1;
                    report.poses.Add(row);
                }
            }
            finally { if (graph.IsValid()) graph.Destroy(); EditorSceneManager.ClosePreviewScene(preview); }
            return Save("unity_imported_support_diagnostic.json", report);
        }
        static PoseRow CapturePose(GameObject actor, string source, SummonCombatProfile p, DemoSummonCombatManager manager, bool ground)
        {
            var row = new PoseRow { source = source, actorPosition = actor.transform.position };
            foreach (var t in actor.GetComponentsInChildren<Transform>(true))
                if (t == actor.transform || t.name == "ApprovedAppearance_CombatDerivative" || t.name == "WoodDeer_CombatRig" || t.name == "Root" || t.name.EndsWith("_Hoof", StringComparison.Ordinal))
                    row.transforms.Add(new TransformRow { path = PathOf(t), localPosition = t.localPosition, localEuler = t.localEulerAngles, localScale = t.localScale, worldPosition = t.position, lossyScale = t.lossyScale });
            if (ground) row.actorGround = Probe("actor origin", actor.transform.position, actor, p, manager);
            foreach (var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var baked = new Mesh();
                try
                {
                    skin.BakeMesh(baked, false); var v = baked.vertices;
                    var r = new SkinRow { path = PathOf(skin.transform), rootBone = PathOf(skin.rootBone), rendererBounds = skin.bounds, localBounds = skin.localBounds };
                    if (v.Length == 0) { row.skins.Add(r); continue; }
                    var world = new Vector3[v.Length]; for (int i = 0; i < v.Length; i++) world[i] = skin.transform.TransformPoint(v[i]);
                    r.bakedWorldBounds = new Bounds(world[0], Vector3.zero); foreach (var point in world) r.bakedWorldBounds.Encapsulate(point);
                    if (skin.name.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var candidates = Feet.ToDictionary(n => n, n => new List<int>());
                        var counts = skin.sharedMesh.GetBonesPerVertex(); var weights = skin.sharedMesh.GetAllBoneWeights();
                        try
                        {
                            int cursor = 0;
                            for (int vertex = 0; vertex < counts.Length; vertex++)
                                for (int j = 0; j < counts[vertex]; j++)
                                {
                                    var weight = weights[cursor++]; if (weight.weight <= .5f || weight.boneIndex >= skin.bones.Length) continue;
                                    var bone = skin.bones[weight.boneIndex]; if (bone == null) continue;
                                    foreach (string name in Feet) if (bone.name == name + "_Hoof") candidates[name].Add(vertex);
                                }
                        }
                        finally { counts.Dispose(); weights.Dispose(); }
                        foreach (string name in Feet)
                        {
                            var ids = candidates[name]; if (ids.Count == 0) continue;
                            Vector3 minimum = world[ids[0]]; foreach (int index in ids) if (world[index].y < minimum.y) minimum = world[index];
                            var bone = skin.bones.FirstOrDefault(b => b != null && b.name == name + "_Hoof");
                            r.feet.Add(new FootRow { name = name, candidates = ids.Count, bonePosition = bone != null ? bone.position : Vector3.zero,
                                lowestMeshPoint = minimum, lowestActorLocal = actor.transform.InverseTransformPoint(minimum),
                                support = ground ? Probe(name, minimum, actor, p, manager) : null });
                        }
                    }
                    row.skins.Add(r);
                }
                finally { Object.DestroyImmediate(baked); }
            }
            return row;
        }
        static GroundRow Probe(string label, Vector3 point, GameObject actor, SummonCombatProfile p, DemoSummonCombatManager manager)
        {
            var row = new GroundRow { label = label, queriedPoint = point };
            var hits = new RaycastHit[128]; var physics = actor.scene.GetPhysicsScene();
            int count = physics.Raycast(point + Vector3.up * p.GroundProbeUp, Vector3.down, hits, p.GroundProbeUp + p.GroundProbeDown, p.GroundMask, QueryTriggerInteraction.Ignore);
            row.saturated = count == hits.Length; Array.Sort(hits, 0, count, Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance)));
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i]; var c = hit.collider;
                bool ignored = c == null || c.transform.IsChildOf(actor.transform) || c.GetComponentInParent<EnemyVitals>() != null ||
                    manager != null && manager.Wiring != null && manager.Wiring.SummonPlayer != null && c.transform.IsChildOf(manager.Wiring.SummonPlayer);
                var r = c != null ? c.GetComponent<Renderer>() : null;
                var item = new HitRow { path = c != null ? PathOf(c.transform) : null, colliderType = c != null ? c.GetType().Name : null,
                    layer = c != null ? LayerMask.LayerToName(c.gameObject.layer) : null, mesh = c is MeshCollider mc && mc.sharedMesh != null ? mc.sharedMesh.name : null,
                    point = hit.point, normal = hit.normal, distance = hit.distance, slope = Vector3.Angle(hit.normal, Vector3.up),
                    ignoredCharacter = ignored, slopeRejected = Vector3.Angle(hit.normal, Vector3.up) > p.MaxSlope, rendererEnabled = r != null && r.enabled && r.gameObject.activeInHierarchy };
                row.hits.Add(item);
                if (!row.chosen && !item.ignoredCharacter && !item.slopeRejected)
                { row.chosen = true; row.chosenHit = item; row.rawGap = point.y - hit.point.y; row.gapToManagerFeet = point.y - (hit.point.y + .035f); }
            }
            return row;
        }
    }
}
