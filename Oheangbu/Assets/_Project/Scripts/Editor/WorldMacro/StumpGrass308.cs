using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 (user 2026-10-09: "만들어진 나무 밑둥 모델들이 너무 작위적이야. 차라리 풀밭으로 대체를 해버려.")
    // The made tree stumps of Places308 (objects named *_stump_*) are switched off - not deleted - and each gets a clump of grass
    // under ONE root (StumpGrass308) of the main scene. No collider, no script. `revert` switches the stumps back on and removes the root.
    // A later Places308 rebuild makes its objects anew: run `apply` again after it.
    //   apply | revert | status
    public static class StumpGrass308
    {
        const string RootName = "StumpGrass308";
        static readonly string[] Grass = "ABCDEFGHIJK".Select(c => "Assets/YongmeoriCoast/Prefabs/SM_YMC_Grass_01_" + c + ".prefab").ToArray();

        public static string Run(string command)
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED edit mode only";
                var scene = SceneManager.GetActiveScene();
                if (!scene.path.EndsWith("W_Demo_Main.unity", StringComparison.Ordinal)) return "REFUSED open W_Demo_Main first (active scene " + scene.path + ")";
                var places = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Places308"); if (places == null) return "REFUSED no Places308 root";
                var stumps = places.GetComponentsInChildren<Transform>(true).Where(t => t.name.IndexOf("_stump_", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
                if (command == "status") return stumps.Length + " stumps, " + stumps.Count(s => s.gameObject.activeSelf) + " shown | grass root " + (root == null ? "absent" : root.transform.childCount + " clumps");
                if (command == "revert")
                {
                    foreach (var s in stumps) s.gameObject.SetActive(true); if (root != null) Object.DestroyImmediate(root);
                    EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); return "reverted: " + stumps.Length + " stumps shown";
                }
                if (command != "apply") return "REFUSED apply | revert | status";
                var prefabs = Grass.Select(AssetDatabase.LoadAssetAtPath<GameObject>).Where(p => p != null).ToArray(); if (prefabs.Length == 0) return "REFUSED no grass prefab under Assets/YongmeoriCoast/Prefabs";
                if (root != null) Object.DestroyImmediate(root); root = new GameObject(RootName); SceneManager.MoveGameObjectToScene(root, scene);
                var rng = new System.Random(30810); int clumps = 0;
                foreach (var s in stumps)
                {
                    var at = s.position; s.gameObject.SetActive(false);                       // off first, so the ground ray below does not land on the stump
                    for (int k = 0; k < 9; k++)
                    {
                        float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)rng.NextDouble()) * 1.7f; var p = at + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                        if (!Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        var g = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[rng.Next(prefabs.Length)], root.transform);
                        g.transform.SetPositionAndRotation(hit.point, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f)); g.transform.localScale *= 0.9f + (float)rng.NextDouble() * .7f;
                        foreach (var c in g.GetComponentsInChildren<Collider>(true)) c.enabled = false; clumps++;
                    }
                }
                EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) return "REFUSED the scene was not saved";
                return "applied: " + stumps.Length + " stumps off (" + string.Join(", ", stumps.Take(6).Select(s => s.parent.name + "/" + s.name)) + (stumps.Length > 6 ? ", ..." : "") + "), " + clumps + " grass clumps";
            }
            catch (Exception e) { return "REFUSED " + e.GetType().Name + ": " + e.Message; }
        }
    }
}
