using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Combat;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // SPEC-SPELL-LAB-308: the spell test scene, cut out of W_Demo_Main (the combat rig + the look) and saved as a copy, so a
    // spell can be tried without the realm streaming, the session, the save slot and the tutorial. The builder is the source;
    // the scene file is generated (not committed) - run build again whenever the main scene's rig changes.
    //   build   open W_Demo_Main, drop every other root, add ground / distance posts / dummies / the unlock stand-in, save as LabScene
    //   play    open LabScene and enter Play directly (no lobby)
    //   status  edit / running | waiting / running | holding
    //   shot:<name>  Game view still into Art/Playtest306/Checks;  stop  leave Play
    public static class SpellLab308
    {
        public const string LabScene = "Assets/_Project/Scenes/Dev/SpellLab308.unity";
        const string LabArt = "Assets/_Project/Art/SpellVFX308/Lab";
        const string GroundMat = LabArt + "/M_SpellLab_Ground.mat";
        const string PostMat = LabArt + "/M_SpellLab_Post.mat";
        const string DummyConfig = "Assets/_Project/Data/Configs/CombatConfig_RangeDummy.asset";
        const string SessionRoot = "WorldMacro_Playtest";
        static readonly string[] KeepRoots = { "Macro_CombatPlayerRig", "Look308", "Macro_WorldLook", "RealmLook308", "Macro_Subtle_Post", "Macro_Sun" };
        // dummies: distance ahead, angle off the facing (SPEC §2.3)
        static readonly (float dist, float yaw)[] Dummies = { (6f, 0f), (10f, 0f), (15f, 0f), (10f, -30f), (10f, 30f) };
        // the lab stands where the main-scene spell runs stood (pq_* hold-teleport 1956,3184 in Hanyang): the realm look and fog
        // pick their colours by world position, and at the origin lab_na1 came out red-brown all over
        static readonly Vector3 Anchor = new Vector3(1956f, 59.8f, 3184f);

        public static bool InLab => SceneManager.GetActiveScene().path == LabScene;

        public static string Run(string command)
        {
            try
            {
                string c = (command ?? "").Trim();
                if (c.StartsWith("shot:", StringComparison.Ordinal)) return Shot(c.Substring(5));
                switch (c)
                {
                    case "build": return Build();
                    case "play": return Play();
                    case "status": return Status();
                    case "stop": if (!EditorApplication.isPlaying) return "stop: not playing"; EditorApplication.isPlaying = false; return "stop: leaving Play";
                    default: return "SpellLab308: build | play | status | shot:<name> | stop";
                }
            }
            catch (Exception e)
            {
                string at = string.Join(" < ", (e.StackTrace ?? "").Split('\n').Where(l => l.Contains("Oheangbu")).Take(3).Select(l => l.Trim()));
                if (!EditorApplication.isPlaying && SceneManager.GetActiveScene().path == Harness303.MainScene && SceneManager.GetActiveScene().isDirty)
                    EditorSceneManager.OpenScene(Harness303.MainScene, OpenSceneMode.Single);   // never leave a half-cut main scene open
                return "SpellLab308 exception: " + e.GetType().Name + " " + e.Message.Split('\n')[0] + " @ " + at;
            }
        }

        static string Hash(string assetPath)
        {
            using var md5 = MD5.Create();
            using var f = File.OpenRead(Path.GetFullPath(assetPath));
            return BitConverter.ToString(md5.ComputeHash(f)).Replace("-", "").Substring(0, 12);
        }

        static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Play is running";
            // OpenScene(Single) would throw away unsaved edits in whatever is open - never decide that here
            // the one exception: the main scene left cut by a build that failed half way (its session root is gone, nobody else
            // removes that) - reopening throws away only this builder's own cuts
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var open = SceneManager.GetSceneAt(i);
                if (!open.isDirty) continue;
                bool ourCut = open.path == Harness303.MainScene && SceneManager.sceneCount == 1 &&
                              open.GetRootGameObjects().Any(r => r.name == "Macro_CombatPlayerRig") && open.GetRootGameObjects().All(r => r.name != SessionRoot);
                if (!ourCut) return "refused: unsaved changes in " + open.path + " (save or discard them first)";
            }
            string before = Hash(Harness303.MainScene);
            var scene = EditorSceneManager.OpenScene(Harness303.MainScene, OpenSceneMode.Single);

            var roots = scene.GetRootGameObjects();
            var keep = new HashSet<GameObject>(roots.Where(r => KeepRoots.Contains(r.name)));
            var missing = KeepRoots.Where(n => roots.All(r => r.name != n)).ToList();
            // other roots the kept ones point at: reported only - the rig's references into the world (enemies, session,
            // streaming) are cut on purpose; anything the lab needs is added below
            var pointed = new HashSet<string>();
            foreach (var dep in EditorUtility.CollectDependencies(keep.Cast<Object>().ToArray()))
            {
                var go = dep is Component c ? c.gameObject : dep as GameObject;
                if (go == null || !go.scene.IsValid() || go.scene != scene) continue;
                var root = go.transform.root.gameObject;
                if (!keep.Contains(root)) pointed.Add(root.name);
            }
            // everything the lab needs is checked before the first cut
            var rig = keep.FirstOrDefault(r => r.name == "Macro_CombatPlayerRig");
            if (rig == null) return Fail("no Macro_CombatPlayerRig in " + Harness303.MainScene);
            var session = roots.FirstOrDefault(r => r.name == SessionRoot);
            var source = session != null ? session.GetComponent<WorldMacroCombatWalker>() : null;
            if (source == null || source.Wiring == null || !source.Wiring.transform.IsChildOf(rig.transform)) return Fail("no walker on " + SessionRoot + " whose wiring is under the rig");
            var scope = rig.GetComponentInChildren<CombatLifetimeScope>(true);
            if (scope == null) return Fail("no CombatLifetimeScope under the rig");
            var config = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(DummyConfig);
            if (config == null) return Fail("dummy config missing " + DummyConfig);

            // the walker sits on the session root, which the lab drops: copy it onto the rig (its references all point into the rig)
            var walkerGo = new GameObject("Lab_Walker");
            walkerGo.transform.SetParent(rig.transform, false);
            var walker = walkerGo.AddComponent<WorldMacroCombatWalker>();
            EditorUtility.CopySerialized(source, walker);

            int dropped = 0;
            foreach (var r in roots) if (r != null && !keep.Contains(r)) { Object.DestroyImmediate(r); dropped++; }   // a root's own teardown may take another with it

            // player to the anchor, facing +Z: move and turn the whole rig about the player's feet
            var player = walker.Body != null ? walker.Body.transform : walker.transform;
            float yaw = player.eulerAngles.y;
            rig.transform.RotateAround(player.position, Vector3.up, -yaw);
            rig.transform.position += Anchor - player.position;

            Directory.CreateDirectory(LabArt);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Lab_Ground"; ground.transform.position = Anchor; ground.transform.localScale = new Vector3(20f, 1f, 20f);   // 200 m
            ground.GetComponent<Renderer>().sharedMaterial = Mat(GroundMat, new Color(.62f, .56f, .47f));
            ground.isStatic = true;

            var posts = new GameObject("Lab_DistancePosts"); posts.transform.position = Anchor;
            var postMat = Mat(PostMat, new Color(.18f, .16f, .14f));
            for (int m = 5; m <= 30; m += 5)
            {
                foreach (float x in new[] { -2.5f, 2.5f })
                {
                    var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    p.name = "Post_" + m + "m" + (x < 0 ? "_L" : "_R"); p.transform.SetParent(posts.transform, false);
                    float h = m % 10 == 0 ? 1.6f : .9f;
                    p.transform.localPosition = new Vector3(x, h * .5f, m); p.transform.localScale = new Vector3(.12f, h, .12f);
                    p.GetComponent<Renderer>().sharedMaterial = postMat;
                    Object.DestroyImmediate(p.GetComponent<Collider>());   // no cover between the caster and the dummies
                }
            }

            var dummyRoot = new GameObject("Lab_Dummies");
            var vitals = new List<EnemyVitals>();
            foreach (var (dist, a) in Dummies)
            {
                var off = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 1f, dist); var pos = Anchor + off;
                var go = DevSceneKit.CreateDummy("Dummy_" + dist + "m" + (a == 0 ? "" : a < 0 ? "_L" : "_R"), pos, dist + " m");
                go.transform.SetParent(dummyRoot.transform, true);
                var face = -off; face.y = 0f; go.transform.rotation = Quaternion.LookRotation(face);
                vitals.Add(go.GetComponent<EnemyVitals>());
            }

            // wiring: only the dummies are enemies; the main scene's single-enemy slots pointed into the dropped world
            var wso = new SerializedObject(walker.Wiring);
            var arr = wso.FindProperty("_enemies"); arr.arraySize = vitals.Count;
            for (int i = 0; i < vitals.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = vitals[i];
            wso.FindProperty("_enemy").objectReferenceValue = null;
            wso.FindProperty("_enemyVitals").objectReferenceValue = null;
            wso.ApplyModifiedPropertiesWithoutUndo();

            // the unlock stand-in (SPEC §3) next to the lifetime scope
            var unlocks = scope.GetComponent<SpellLabUnlocks308>() ?? scope.gameObject.AddComponent<SpellLabUnlocks308>();
            var sso = new SerializedObject(scope);
            sso.FindProperty("_labUnlocks").objectReferenceValue = unlocks;
            sso.FindProperty("_macroPlaytest").objectReferenceValue = null;
            sso.FindProperty("_prologue").objectReferenceValue = null;
            sso.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(LabScene));
            if (!EditorSceneManager.SaveScene(scene, LabScene, true)) return Fail("save as copy");
            AssetDatabase.SaveAssetIfDirty(ground.GetComponent<Renderer>().sharedMaterial); AssetDatabase.SaveAssetIfDirty(postMat);   // not SaveAssets: it would flush other sessions' dirty assets
            // the report is read before the cut scene closes (its objects die with it)
            string kept = string.Join(",", keep.Select(k => k.name)); int enemies = arr.arraySize;
            EditorSceneManager.OpenScene(LabScene, OpenSceneMode.Single);
            string after = Hash(Harness303.MainScene);
            return "build ok " + LabScene + " | kept " + kept + " | missing " + (missing.Count == 0 ? "-" : string.Join(",", missing)) +
                   " | dropped " + dropped + " roots | pointed-at (dropped) " + (pointed.Count == 0 ? "-" : string.Join(",", pointed)) +
                   " | dummies " + vitals.Count + " wiring enemies " + enemies + " | rig turned " + (-yaw).ToString("F1") +
                   " | main hash " + before + " -> " + after + (before == after ? " (unchanged)" : " CHANGED");
        }

        // a failed build never leaves the cut main scene open (a later save there would write it)
        static string Fail(string why)
        {
            if (SceneManager.GetActiveScene().path == Harness303.MainScene) EditorSceneManager.OpenScene(Harness303.MainScene, OpenSceneMode.Single);
            return "build FAIL: " + why + " | main scene reopened from disk";
        }

        static Material Mat(string path, Color color)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", .1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static string Play()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: already playing";
            if (!File.Exists(Path.GetFullPath(LabScene))) return "refused: no lab scene - run build first";
            if (!InLab) EditorSceneManager.OpenScene(LabScene, OpenSceneMode.Single);
            CompactLoadingStartup270.UseCurrent();
            EditorApplication.playModeStateChanged += BackToLobby;
            EditorApplication.isPlaying = true;
            return "play: entering Play in " + LabScene;
        }

        // the editor's normal start is the lobby; give it back once the lab run ends
        static void BackToLobby(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= BackToLobby;
            CompactLoadingStartup270.UseLobby();
        }

        // a still of the Game view into the same folder the held-Play shots use (Map307Capture hold-shot needs a session)
        static string Shot(string name)
        {
            if (!EditorApplication.isPlaying || !InLab) return "refused: Play in the lab only";
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Playtest306/Checks"));
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, name + "_" + Screen.width + "x" + Screen.height + ".png");
            ScreenCapture.CaptureScreenshot(file);
            return "capture requested " + file + " | quality " + QualitySettings.names[QualitySettings.GetQualityLevel()];
        }

        static string Status()
        {
            if (!EditorApplication.isPlaying) return "edit | scene " + SceneManager.GetActiveScene().path;
            var walker = Object.FindFirstObjectByType<WorldMacroCombatWalker>();
            bool ready = InLab && walker != null && walker.Wiring != null && walker.Wiring.isActiveAndEnabled;
            return "running | " + (ready ? "holding" : "waiting") + " | scene " + SceneManager.GetActiveScene().path;
        }
    }
}
