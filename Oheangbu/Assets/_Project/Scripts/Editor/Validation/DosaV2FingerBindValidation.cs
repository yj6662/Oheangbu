using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.Data;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static class DosaV2FingerBindValidation
    {
        [Serializable] private sealed class Case
        {
            public string model, condition;
            public int comparedFingers;
            public bool passed;
            public float maximumRestErrorDegrees, minimumPerturbedDistanceDegrees;
        }
        [Serializable] private sealed class Report
        {
            public string status, error;
            public string scope = "FINGER_BIND_CACHE_ONLY: two actual imported models x unmodified/arbitrarily rotated fingers. Checks mesh bind reference against cached FingerRest, <=0.1 degrees. Does not certify skin/shaft contact or overall rig appearance. Only temporary PreviewScene clones are mutated.";
            public List<Case> cases = new List<Case>();
        }
        private static readonly string[] Names = new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" }
            .SelectMany(f => Enumerable.Range(1, 3).Select(j => "RightHand" + f + j)).ToArray();

        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "NOT_RUN: Edit Mode required";
            var report = new Report();
            try
            {
                foreach (string path in new[] { DosaV2PlayerBuilder.WorldModel, DosaV2PlayerBuilder.ArmsModel })
                    foreach (bool perturb in new[] { false, true }) report.cases.Add(Evaluate(path, perturb));
                report.status = report.cases.All(c => c.passed) ? "FINGER_BIND_CACHE_PASS" : "FINGER_BIND_CACHE_FAIL";
            }
            catch (Exception e) { report.status = "FINGER_BIND_CACHE_ERROR"; report.error = e.GetBaseException().ToString(); }
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2")); Directory.CreateDirectory(directory);
            string json = JsonUtility.ToJson(report, true); File.WriteAllText(Path.Combine(directory, "finger-bind-cache-validation.json"), json);
            return json;
        }

        private static Case Evaluate(string path, bool perturb)
        {
            var scene = EditorSceneManager.NewPreviewScene(); GameObject model = null, owner = null;
            PlayerVisualProfileSO visual = null; DrawingPoseProfileSO drawing = null;
            try
            {
                model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                var animator = model.GetComponent<Animator>(); animator.runtimeAnimatorController = null;
                var all = model.GetComponentsInChildren<Transform>(true);
                var fingers = Names.Select(n => all.Single(t => t.name == n)).ToArray();
                var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                // Independent rotation decomposition of the authored child and parent bind-world bases.
                var expected = fingers.Select(f => ExpectedRest(f, skins)).ToArray();
                visual = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PlayerVisualProfileSO>(DosaV2PlayerBuilder.VisualProfilePath)); visual.Controller = null;
                drawing = Object.Instantiate(AssetDatabase.LoadAssetAtPath<DrawingPoseProfileSO>(DosaV2PlayerBuilder.DrawingProfilePath));
                if (drawing.CalibratedRightFingerOffsets == null || drawing.CalibratedRightFingerOffsets.Length != 15)
                    throw new InvalidOperationException("Actual V2 calibrated fifteen-finger profile is required.");
                owner = new GameObject("TemporaryFingerBindCheck"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(owner, scene);
                model.transform.SetParent(owner.transform, false);
                var rig = owner.AddComponent<PlayerVisualRig>(); rig.Configure(animator, null, null, null, visual, drawing);
                if (perturb)
                    for (int i = 0; i < fingers.Length; i++) fingers[i].localRotation = expected[i] * Quaternion.Euler(43f + i, -31f + i * .7f, 27f - i * .4f);
                float minimumPerturbed = fingers.Select((f, i) => Quaternion.Angle(f.localRotation, expected[i])).Min();
                var bind = typeof(PlayerVisualRig).GetMethod("BindBones", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException("PlayerVisualRig.BindBones");
                object bones = bind.Invoke(rig, new object[] { animator });
                var cached = (Quaternion[])bones.GetType().GetField("FingerRest", BindingFlags.Instance | BindingFlags.Public).GetValue(bones);
                if (cached.Length != 15) throw new InvalidOperationException("Unexpected cached finger count.");
                float maximum = cached.Select((q, i) => Quaternion.Angle(q, expected[i])).Max();
                return new Case { model = path, condition = perturb ? "arbitrary_local_rotations_before_bind" : "fresh_import_reference",
                    comparedFingers = cached.Length, maximumRestErrorDegrees = maximum, minimumPerturbedDistanceDegrees = minimumPerturbed,
                    passed = maximum <= .1f && (!perturb || minimumPerturbed > 20f) };
            }
            finally
            {
                if (owner != null) Object.DestroyImmediate(owner); else if (model != null) Object.DestroyImmediate(model);
                if (visual != null) Object.DestroyImmediate(visual); if (drawing != null) Object.DestroyImmediate(drawing);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        private static Quaternion ExpectedRest(Transform finger, SkinnedMeshRenderer[] skins)
        {
            foreach (var skin in skins)
            {
                int child = Array.IndexOf(skin.bones, finger), parent = Array.IndexOf(skin.bones, finger.parent);
                if (child < 0 || parent < 0) continue;
                var bind = skin.sharedMesh.bindposes;
                Quaternion childBasis = bind[child].inverse.rotation, parentBasis = bind[parent].inverse.rotation;
                return Quaternion.Inverse(parentBasis) * childBasis;
            }
            throw new InvalidOperationException("Missing actual parent/child skin bind basis: " + finger.name);
        }
    }
}
