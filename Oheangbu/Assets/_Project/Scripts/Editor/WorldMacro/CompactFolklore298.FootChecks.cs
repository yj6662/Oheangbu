using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactFolklore298
    {
        [Serializable] sealed class FootCheckReceipt298
        {
            public string Utc, Scope, Status;
            public bool Passed, Restored, ActualImportedRigPoseVerified;
            public int ChecksPassed, RayQueries, ConfiguredSceneActors;
            public List<string> Checks = new List<string>(), Failures = new List<string>();
            public string[] BoundActorNames;
        }
        public static string FootChecks(string argument)
        {
            string output = Path.Combine(OutputRoot, "Analysis/foot-unity-checks.json");
            if (argument == "status") return File.Exists(output) ? File.ReadAllText(output) : "No #298 foot diagnostics.";
            if (argument != "run") throw new ArgumentException("FootChecks: run or status");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode for the isolated native physics fixture.");
            var active = SceneManager.GetActiveScene(); var dirty = active.isDirty;
            var report = new FootCheckReceipt298 { Utc = DateTime.UtcNow.ToString("O"),
                Scope = "Actual EnemyFootPlacement298 in Unity PreviewScene with native collider raycasts and four synthetic bent leg chains. Explicit synthetic sole overrides plus a separate synthetic weighted-skin coordinate test; no imported weighted-skin calibration or terrain walk/visual certification. Actor root, collider and unmodified animated poses are compared. No scene/prefab/assets saved." };
            var bound = Object.FindObjectsByType<EnemyFootPlacement298>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(x => x.gameObject.scene == active).ToArray();
            report.ConfiguredSceneActors = bound.Count(x => x.Configured); report.BoundActorNames = bound.Select(x => x.name).ToArray();
            Scene preview = default;
            try
            {
                preview = EditorSceneManager.NewPreviewScene();
                FootSkinCoordinates298(report, preview);
                Vector3 origin = new Vector3(10000, 1000, 10000);
                var ground = AudioObject298("Foot298_DiagnosticGround", preview); ground.layer = 30;
                var groundCollider = ground.AddComponent<BoxCollider>(); groundCollider.size = new Vector3(10, .1f, 10);
                ground.transform.position = origin + Vector3.down * .05f;
                var actor = AudioObject298("Foot298_SyntheticFourLegActor", preview); actor.transform.position = origin;
                var vitals = actor.AddComponent<EnemyVitals>(); vitals.Restore();
                var model = AudioObject298("Foot298_Visual", preview); model.transform.SetParent(actor.transform, false);
                var body = AudioObject298("Pelvis", preview); body.transform.SetParent(model.transform, false); body.transform.localPosition = Vector3.up;
                var legs = new EnemyFootPlacement298.LegBinding[4];
                for (int i = 0; i < 4; i++)
                {
                    var upper = AudioObject298("Leg" + i + "_Upper", preview); upper.transform.SetParent(body.transform, false);
                    upper.transform.localPosition = new Vector3((i & 1) == 0 ? -.35f : .35f, 0, i < 2 ? .5f : -.5f);
                    var lower = AudioObject298("Leg" + i + "_Lower", preview); lower.transform.SetParent(upper.transform, false); lower.transform.localPosition = new Vector3(0, -.45f, .15f);
                    var foot = AudioObject298("Leg" + i + "_Foot", preview); foot.transform.SetParent(lower.transform, false); foot.transform.localPosition = new Vector3(0, -.55f, -.15f);
                    legs[i] = new EnemyFootPlacement298.LegBinding { Name="leg"+i, Upper=upper.transform, Lower=lower.transform, Foot=foot.transform,
                        HasSoleOverride=true, SoleLocalOverride=Vector3.zero, SoleNormalLocalOverride=Vector3.up };
                }
                var feet = actor.AddComponent<EnemyFootPlacement298>();
                feet.Configure(actor.transform, model.transform, body.transform, Array.Empty<SkinnedMeshRenderer>(), legs, 1 << 30);
                Physics.SyncTransforms();
                string authored = FootPose298(actor.transform);
                FootCheck298(report, feet.ApplyPose(1f/60), "Flat actual collider supports the four-leg correction: " + feet.LastStatus);
                report.RayQueries = feet.RayQueriesLastSample;
                FootCheck298(report, feet.RayQueriesLastSample == 12 && feet.Diagnostics.All(x => x.Supported && x.AnkleError <= .0031f), "Exactly three native probes per stance foot; bounded solver error.");
                FootCheck298(report, actor.transform.position == origin && ground.transform.position == origin + Vector3.down * .05f,
                    "Gameplay actor root and collider remain unchanged.");
                feet.RestoreAuthoredPose(); FootCheck298(report, FootPose298(actor.transform) == authored, "Restore returns every authored local pose exactly.");
                string stable = FootPose298(actor.transform);
                bool cycle = true;
                for (int i = 0; i < 10; i++) { cycle &= feet.ApplyPose(1f/60); feet.RestoreAuthoredPose(); cycle &= FootPose298(actor.transform) == stable; }
                FootCheck298(report, cycle, "Repeated restore/evaluate boundary has no cumulative drift.");

                ground.transform.position = origin + Vector3.down * .11f; Physics.SyncTransforms();
                FootCheck298(report, feet.ApplyPose(1f/60) && feet.BodyDrop > .04f && feet.BodyDrop <= .12f,
                    "All-feet stance may use a bounded visual body drop on lower support: " + feet.LastStatus);
                feet.RestoreAuthoredPose();
                ground.transform.position = origin + Vector3.down * .05f; ground.transform.rotation = Quaternion.Euler(0, 0, 5); Physics.SyncTransforms();
                FootCheck298(report, feet.ApplyPose(1f/60), "Five-degree native support slope remains solvable: " + feet.LastStatus); feet.RestoreAuthoredPose();
                ground.transform.rotation = Quaternion.identity; Physics.SyncTransforms();
                Vector3 originalUpper = legs[0].Upper.localPosition; legs[0].Upper.localPosition += Vector3.up * .06f;
                Vector3 liftedPosition = legs[0].Foot.position; Quaternion liftedRotation = legs[0].Foot.rotation;
                bool lift = feet.ApplyPose(1f/60);
                FootCheck298(report, lift && !feet.Diagnostics[0].Planted && !feet.Diagnostics[0].Corrected && feet.BodyDrop == 0 &&
                    legs[0].Foot.position == liftedPosition && legs[0].Foot.rotation == liftedRotation,
                    "Authored lifted foot and its parent remain unchanged while other feet may solve.");
                feet.RestoreAuthoredPose(); legs[0].Upper.localPosition = originalUpper;

                actor.transform.position += Vector3.up * 2; Physics.SyncTransforms(); stable = FootPose298(actor.transform);
                FootCheck298(report, !feet.ApplyPose(1f/60) && FootPose298(actor.transform) == stable, "Airborne/no-support pose has zero correction.");
                actor.transform.position = origin; feet.MaximumBodyDrop = 0;
                ground.transform.position = origin + Vector3.down * .18f; Physics.SyncTransforms(); stable = FootPose298(actor.transform);
                FootCheck298(report, !feet.ApplyPose(1f/60) && feet.LastStatus.StartsWith("UNREACHABLE", StringComparison.Ordinal) && FootPose298(actor.transform) == stable,
                    "Unreachable chain rejects the whole correction without stretching or partial edits: " + feet.LastStatus);
                ground.transform.position = origin + Vector3.down * .05f; feet.MaximumBodyDrop = .12f; Physics.SyncTransforms();
                stable = FootPose298(actor.transform);
                FootCheck298(report, !feet.ApplyPose(0) && feet.LastStatus == "PAUSED" && FootPose298(actor.transform) == stable, "Zero-time sample leaves authored pose unchanged.");
                vitals.TakeDamage(vitals.MaxHp + 1); stable = FootPose298(actor.transform);
                FootCheck298(report, !feet.ApplyPose(1f/60) && feet.LastStatus == "DEAD_OR_DISABLED" && FootPose298(actor.transform) == stable, "Actual enemy death disables correction.");
                vitals.Restore(); FootCheck298(report, feet.ApplyPose(1f/60), "Actual Restore allows a later supported correction."); feet.RestoreAuthoredPose();
            }
            catch (Exception error) { report.Failures.Add(error.GetType().Name + ": " + error.Message); }
            finally
            {
                try
                {
                    if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                    report.Restored = SceneManager.GetActiveScene() == active && active.isDirty == dirty;
                    FootCheck298(report, report.Restored, "Preview fixture removed; active scene and dirty state preserved.");
                }
                catch (Exception error) { report.Failures.Add("Cleanup: " + error.Message); }
                report.Passed = report.Failures.Count == 0 && report.Restored; report.ChecksPassed = report.Checks.Count;
                report.Status = report.Passed ? "NATIVE_PHYSICS_SYNTHETIC_RIG_PASS_IMPORTED_RIG_UNVERIFIED" : "FAIL";
                Directory.CreateDirectory(Path.GetDirectoryName(output)); File.WriteAllText(output, JsonUtility.ToJson(report, true));
            }
            return JsonUtility.ToJson(report, true);
        }
        static void FootCheck298(FootCheckReceipt298 r, bool ok, string text) { if (ok) r.Checks.Add(text); else r.Failures.Add(text); }
        static void FootSkinCoordinates298(FootCheckReceipt298 r, Scene preview)
        {
            var root = AudioObject298("Foot298_SyntheticSkin", preview); root.transform.position = new Vector3(50, 20, 30);
            var visual = AudioObject298("ScaledRenderer", preview); visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(.7f, 1.3f, .9f); visual.transform.localRotation = Quaternion.Euler(5, 18, 0);
            var first = AudioObject298("SkinBoneA", preview); first.transform.SetParent(root.transform, false);
            var second = AudioObject298("SkinBoneB", preview); second.transform.SetParent(root.transform, false); second.transform.localPosition = Vector3.up;
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[] { new Vector3(-.2f, 0, -.3f), new Vector3(.3f, .1f, 0), new Vector3(0, .3f, .2f) };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.bindposes = new[] { first.transform.worldToLocalMatrix * visual.transform.localToWorldMatrix,
                    second.transform.worldToLocalMatrix * visual.transform.localToWorldMatrix };
                mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = .25f, boneIndex1 = 1, weight1 = .75f }, 3).ToArray();
                var skin = visual.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh; skin.bones = new[] { first.transform, second.transform }; skin.rootBone = root.transform;
                var world = EnemyFootPlacement298.WorldVertices(skin); var source = mesh.vertices;
                FootCheck298(r, world.Select((p, i) => Vector3.Distance(p, visual.transform.TransformPoint(source[i]))).Max() < .00003f,
                    "Weighted source coordinates preserve renderer scale exactly once at bind pose.");
                root.transform.localScale = new Vector3(2, .8f, 1.4f); root.transform.rotation = Quaternion.Euler(0, 32, 7);
                Vector3 a = new Vector3(.07f, -.04f, 0), b = new Vector3(-.02f, .03f, .05f);
                first.transform.localPosition += a; second.transform.localPosition += b;
                world = EnemyFootPlacement298.WorldVertices(skin);
                Vector3 delta = root.transform.TransformVector(a * .25f + b * .75f);
                FootCheck298(r, world.Select((p, i) => Vector3.Distance(p, visual.transform.TransformPoint(source[i]) + delta)).Max() < .00003f,
                    "Weighted world skin follows animated bones and nonuniform parent scale without duplicate renderer transform.");
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(root); }
        }
        static string FootPose298(Transform root) => string.Join("|", root.GetComponentsInChildren<Transform>(true).Select(x =>
            x.name + ":" + x.localPosition.ToString("R") + ":" + x.localRotation.ToString("R") + ":" + x.localScale.ToString("R")));
    }
}
