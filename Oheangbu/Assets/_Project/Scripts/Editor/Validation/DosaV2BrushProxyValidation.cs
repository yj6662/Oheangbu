using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static class DosaV2BrushProxyValidation
    {
        [Serializable] private sealed class Sample
        { public int direction; public Vector3 rootPosition; public Quaternion rootRotation; public float bend, splay; public PlayerClothBrushProxyRig.BrushProxyAudit audit; }
        [Serializable] private sealed class Report
        { public string status, error, path; public bool restored; public List<Sample> samples = new List<Sample>(); }
        public static string Run()
        {
            if (Application.isPlaying) return "WAIT: Edit Mode disposable preview fixture required.";
            var report = new Report();
            report.path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2/brush-proxy-validation.json"));
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = new GameObject("ActualBrushProxyReview"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                var brush = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DosaV2PlayerBuilder.BrushModel), scene);
                brush.transform.SetParent(root.transform, false);
                Transform Find(string name) => brush.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
                var skin = brush.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
                var bristles = brush.AddComponent<BrushBristleRig>();
                var profile = AssetDatabase.LoadAssetAtPath<Oheangbu.Data.BrushDeformationProfileSO>(DosaV2PlayerBuilder.BrushProfilePath);
                if (!bristles.Configure(profile, Find("GripSocket"), Find("TipSocket"), Enumerable.Range(1, 6).Select(i => Find("Bristle_" + i.ToString("00"))).ToArray(), skin))
                    throw new InvalidOperationException(bristles.BindingError);
                var proxy = root.AddComponent<PlayerClothBrushProxyRig>();
                if (!proxy.Configure(root.transform, brush, out string reason)) throw new InvalidOperationException(reason);
                for (int direction = 0; direction < 9; direction++)
                {
                    float angle = direction * Mathf.PI / 4f;
                    root.transform.position = new Vector3(direction * .03f, .2f, -.12f);
                    root.transform.rotation = Quaternion.Euler(direction * 7, direction * 19, direction * -5);
                    bristles.ResetDeformation();
                    for (int i = 0; i < 40; i++)
                    { bristles.EvaluateLocal(direction == 8 ? Vector3.zero : new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2f, direction != 8, 1f / 60); bristles.ApplyPose(); }
                    var audit = proxy.AuditAgainstFullSource(64);
                    report.samples.Add(new Sample { direction = direction, rootPosition = root.transform.position, rootRotation = root.transform.rotation,
                        bend = bristles.Pose.BendDegrees, splay = bristles.Pose.Splay, audit = audit });
                }
                report.status = "COMPLETE_MEASURED";
            }
            catch (Exception e) { report.status = "FAIL"; report.error = e.ToString(); }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene); report.restored = true;
                Directory.CreateDirectory(Path.GetDirectoryName(report.path)); File.WriteAllText(report.path, JsonUtility.ToJson(report, true));
            }
            return JsonUtility.ToJson(report, true);
        }
    }
}
