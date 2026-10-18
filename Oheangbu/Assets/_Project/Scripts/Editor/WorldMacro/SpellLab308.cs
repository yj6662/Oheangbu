using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    //   shot:<name>  Game view still into Art/Playtest306/Checks;  side:<name>  the same moment from 9 m off the caster's right,
    //   looking across the firing line (depth along the line is invisible from behind);  top:<name>  from 11 m up behind the
    //   caster looking down the line (ground marks foreshorten to nothing from the low views);  stop  leave Play
    //   rec:<name>:<seconds>  record main + side + top views every frame into Art/Playtest306/Checks/rec_<name>/ (Rec below);
    //   rec:stop  end it early
    public static class SpellLab308
    {
        public const string LabScene = "Assets/_Project/Scenes/Dev/SpellLab308.unity";
        const string LabArt = "Assets/_Project/Art/SpellVFX308/Lab";
        const string GroundMat = LabArt + "/M_SpellLab_Ground.mat";
        const string PostMat = LabArt + "/M_SpellLab_Post.mat";
        const string DummyConfig = "Assets/_Project/Data/Configs/CombatConfig_RangeDummy.asset";
        const string SessionRoot = "WorldMacro_Playtest";
        static readonly string[] KeepRoots = { "Macro_CombatPlayerRig", "Look308", "Macro_WorldLook", "RealmLook308", "Macro_Subtle_Post", "Macro_Sun" };
        // dummies: distance ahead, angle off the facing (SPEC §2.3). Staggered (2026-10-18): with 6 · 10 · 15 m on one line the
        // near dummy stood in the line of sight to the far ones and every visibility-checked cast at the 10 m lock did nothing
        static readonly (float dist, float yaw)[] Dummies = { (6f, -14f), (10f, 0f), (15f, 9f), (10f, -30f), (10f, 30f) };
        // the counterattack dummy (§2.4, D308-111): throws an elemental ranged shot at the player while armed (strike:on)
        const string StrikerDummy = "Dummy_10m_R";
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
                if (c.StartsWith("side:", StringComparison.Ordinal)) return Side(c.Substring(5), false);
                if (c.StartsWith("top:", StringComparison.Ordinal)) return Side(c.Substring(4), true);
                if (c == "rec:stop") return Rec.Stop("rec:stop");
                if (c.StartsWith("lock:", StringComparison.Ordinal)) return Lock(c.Substring(5));
                if (c.StartsWith("strike:", StringComparison.Ordinal)) return Strike(c.Substring(7));
                if (c.StartsWith("rec:", StringComparison.Ordinal)) return Rec.Start(c.Substring(4));
                switch (c)
                {
                    case "build": return Build();
                    case "play": return Play();
                    case "status": return Status();
                    case "stop": if (!EditorApplication.isPlaying) return "stop: not playing"; EditorApplication.isPlaying = false; return "stop: leaving Play";
                    // re-read Packages/manifest.json without window focus (an unfocused editor does not notice a manifest edit)
                    case "resolve": UnityEditor.PackageManager.Client.Resolve(); return "resolve: requested";
                    default: return "SpellLab308: build | play | status | shot:<name> | side:<name> | top:<name> | rec:<name>:<seconds> | rec:stop | lock:<dummy> | strike:on|off|status | stop | resolve";
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
            // §3.1: the ㄱ-final runtimes' profiles live on the session, which the lab drops - read them first
            var macro = session.GetComponent<WorldMacroPlaytestSession>();
            if (macro == null) return Fail("no WorldMacroPlaytestSession on " + SessionRoot);
            var eaBuffs = macro.EABuffProfile; var eaWards = macro.EAWardProfile; var eaGiyeok = macro.EAGiyeokProfile;

            // the walker sits on the session root, which the lab drops: copy it onto the rig (its references all point into the rig)
            var walkerGo = new GameObject("Lab_Walker");
            walkerGo.transform.SetParent(rig.transform, false);
            var walker = walkerGo.AddComponent<WorldMacroCombatWalker>();
            EditorUtility.CopySerialized(source, walker);
            // the kept roots' links to the session walker would go missing with it: point them at the copy. The gesture rig's
            // _walker is how the brush swing (SpellSwing308) and the swing ink (SwingInk308) reach the wiring's swing events -
            // without this they never ran in the lab (fire1 - fire19 all logged clip StanceIdle, bridges 0)
            int repointed = Repoint(keep, source, walker);

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
            var strikerGo = dummyRoot.transform.Find(StrikerDummy);
            if (strikerGo == null) return Fail("no " + StrikerDummy);
            var playerVitals = new SerializedObject(walker.Wiring).FindProperty("_playerVitals").objectReferenceValue as PlayerVitals;
            if (playerVitals == null) return Fail("wiring has no _playerVitals");
            var striker = strikerGo.gameObject.AddComponent<SpellLabStriker308>();
            var kso = new SerializedObject(striker);
            kso.FindProperty("_vitals").objectReferenceValue = strikerGo.GetComponent<EnemyVitals>();
            kso.FindProperty("_player").objectReferenceValue = playerVitals;
            kso.FindProperty("_aim").objectReferenceValue = player;
            kso.ApplyModifiedPropertiesWithoutUndo();
            strikerGo.GetComponent<SpellRangeDummy>().SetCaption("10 m 반격");

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
            var uso = new SerializedObject(unlocks);
            uso.FindProperty("_wiring").objectReferenceValue = walker.Wiring;
            uso.FindProperty("_buffs").objectReferenceValue = eaBuffs;
            uso.FindProperty("_wards").objectReferenceValue = eaWards;
            uso.FindProperty("_giyeok").objectReferenceValue = eaGiyeok;
            uso.ApplyModifiedPropertiesWithoutUndo();
            string ea = "ea buffs " + (eaBuffs != null ? eaBuffs.name : "-") + " wards " + (eaWards != null ? eaWards.name : "-") + " giyeok " + (eaGiyeok != null ? eaGiyeok.name : "-");

            Directory.CreateDirectory(Path.GetDirectoryName(LabScene));
            if (!EditorSceneManager.SaveScene(scene, LabScene, true)) return Fail("save as copy");
            AssetDatabase.SaveAssetIfDirty(ground.GetComponent<Renderer>().sharedMaterial); AssetDatabase.SaveAssetIfDirty(postMat);   // not SaveAssets: it would flush other sessions' dirty assets
            // the report is read before the cut scene closes (its objects die with it)
            string kept = string.Join(",", keep.Select(k => k.name)); int enemies = arr.arraySize;
            EditorSceneManager.OpenScene(LabScene, OpenSceneMode.Single);
            string after = Hash(Harness303.MainScene);
            return "build ok " + LabScene + " | kept " + kept + " | missing " + (missing.Count == 0 ? "-" : string.Join(",", missing)) +
                   " | dropped " + dropped + " roots | pointed-at (dropped) " + (pointed.Count == 0 ? "-" : string.Join(",", pointed)) +
                   " | walker refs repointed " + repointed + " | " + ea +
                   " | dummies " + vitals.Count + " (striker " + StrikerDummy + ") wiring enemies " + enemies + " | rig turned " + (-yaw).ToString("F1") +
                   " | main hash " + before + " -> " + after + (before == after ? " (unchanged)" : " CHANGED");
        }

        // every serialized reference to `from` under the roots now names `to`; the count of fields changed
        static int Repoint(IEnumerable<GameObject> roots, Object from, Object to)
        {
            int n = 0;
            foreach (var root in roots)
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c == to) continue;   // a missing script shows up as null
                var so = new SerializedObject(c);
                var p = so.GetIterator();
                bool changed = false;
                while (p.Next(true))
                {
                    if (p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue != from) continue;
                    p.objectReferenceValue = to; changed = true; n++;
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
            return n;
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

        // a side still: a throw-away copy of the main camera 9 m to the caster's right, aimed at the line 5 m ahead, rendered once
        static string Side(string name, bool top)
        {
            if (!EditorApplication.isPlaying || !InLab) return "refused: Play in the lab only";
            var walker = Object.FindFirstObjectByType<WorldMacroCombatWalker>();
            var main = Camera.main;
            if (walker == null || walker.Body == null || main == null) return "refused: no walker body / main camera";
            var body = walker.Body.transform;
            Vector3 fwd = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized, right = Vector3.Cross(Vector3.up, fwd);
            Vector3 aim = body.position + fwd * 5f + Vector3.up, eye = top ? body.position - fwd * 2f + Vector3.up * 11f : aim + right * 9f + Vector3.up * 1.5f;
            var go = new GameObject("Lab_SideCam") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(1600, 900, 24);
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.CopyFrom(main);
                cam.fieldOfView = 50f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
                cam.targetTexture = rt;
                cam.Render();
                var was = RenderTexture.active; RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
                RenderTexture.active = was;
                string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Playtest306/Checks"));
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, name + (top ? "_top.png" : "_side.png"));
                File.WriteAllBytes(file, tex.EncodeToPNG());
                return (top ? "top " : "side ") + file + " | t " + Time.time.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
        }

        // rec:<name>:<seconds> (Spec 11.34, user 2026-10-16 "다음부터는 영상으로 제공해줘"): every game frame, copies of the main
        // camera render the first-person view (1280 x 720) and the side: / top: views (640 x 360 each, eyes fixed at the start)
        // into one 1920 x 720 jpg f_<n>.jpg under Art/Playtest306/Checks/rec_<name>/, and index.csv gets the frame's clocks and
        // the oldest live spell's age. Stops itself after <seconds> of real time, after MaxFrames, on rec:stop or when Play ends.
        // Tools/Art/SpellVFX308/rec308.py turns the folder into the mp4 (game-time resampling, real speed + slow)
        static class Rec
        {
            const int W = 1280, H = 720, SW = 640, SH = 360, MaxFrames = 1800;
            static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
            static string folder;
            static double until;
            static int frames, lastFrame;
            static Camera main, mainCam, sideCam, topCam;
            static RenderTexture mainRt, sideRt, topRt;
            static Texture2D sheet;
            static StreamWriter index;

            public static string Start(string arg)
            {
                if (!EditorApplication.isPlaying || !InLab) return "refused: Play in the lab only";
                int k = arg.LastIndexOf(':');
                string name = k > 0 ? arg.Substring(0, k) : arg;
                float secs = k > 0 ? float.Parse(arg.Substring(k + 1), Inv) : 20f;
                if (name.Length == 0 || name.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-'))) return "refused: rec name [A-Za-z0-9_-]";
                secs = Mathf.Clamp(secs, 1f, 120f);
                if (index != null) Stop("restarted");
                foreach (var left in UnityEngine.Resources.FindObjectsOfTypeAll<Camera>())   // left over by a crash or a lost domain
                {
                    if (left == null || !left.name.StartsWith("Lab_Rec") || left.hideFlags != HideFlags.HideAndDontSave) continue;
                    var t = left.targetTexture;
                    left.targetTexture = null;
                    if (t != null) { t.Release(); Object.DestroyImmediate(t); }
                    Object.DestroyImmediate(left.gameObject);
                }
                var walker = Object.FindFirstObjectByType<WorldMacroCombatWalker>();
                main = Camera.main;
                if (walker == null || walker.Body == null || main == null) return "refused: no walker body / main camera";

                var body = walker.Body.transform;
                Vector3 fwd = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized, right = Vector3.Cross(Vector3.up, fwd);
                Vector3 aim = body.position + fwd * 5f + Vector3.up;
                Vector3 sideEye = aim + right * 9f + Vector3.up * 1.5f, topEye = body.position - fwd * 2f + Vector3.up * 11f;
                folder = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Playtest306/Checks")), "rec_" + name);
                Directory.CreateDirectory(folder);
                foreach (var old in Directory.GetFiles(folder, "f_*.jpg")) File.Delete(old);   // this recorder's own earlier frames only
                try
                {
                    mainCam = Copy("Lab_RecMain", mainRt = new RenderTexture(W, H, 24), main.transform.position, main.transform.rotation, main.fieldOfView);
                    sideCam = Copy("Lab_RecSide", sideRt = new RenderTexture(SW, SH, 24), sideEye, Quaternion.LookRotation(aim - sideEye), 50f);
                    topCam = Copy("Lab_RecTop", topRt = new RenderTexture(SW, SH, 24), topEye, Quaternion.LookRotation(aim - topEye), 50f);
                    sheet = new Texture2D(W + SW, H, TextureFormat.RGB24, false);
                    index = new StreamWriter(Path.Combine(folder, "index.csv"), false);
                    index.WriteLine("frame,time,real,timescale,age,unityframe");
                }
                catch { Stop("start failed"); throw; }
                until = EditorApplication.timeSinceStartup + secs;
                frames = 0; lastFrame = -1;
                EditorApplication.update += Tick;
                EditorApplication.playModeStateChanged += OnPlayMode;
                AssemblyReloadEvents.beforeAssemblyReload += OnReload;   // a script compile in Play reloads the domain: clean up first
                return "rec " + name + " | " + secs.ToString("F0", Inv) + " s real | " + folder;
            }

            // a disabled throw-away copy of the main camera with its URP settings (post-processing on, like the game), no stack
            static Camera Copy(string name, RenderTexture rt, Vector3 eye, Quaternion look, float fov)
            {
                var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
                var cam = go.AddComponent<Camera>();
                cam.CopyFrom(main);
                cam.enabled = false;
                var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
                EditorUtility.CopySerialized(UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(main), data);
                data.cameraStack?.Clear();
                cam.targetTexture = rt;
                cam.fieldOfView = fov;
                cam.transform.SetPositionAndRotation(eye, look);
                return cam;
            }

            static void OnPlayMode(PlayModeStateChange state)
            {
                if (state == PlayModeStateChange.ExitingPlayMode) Stop("Play ended");
            }

            static void OnReload() => Stop("script reload");

            static void Tick()
            {
                if (index == null) return;
                try
                {
                    if (!EditorApplication.isPlaying || main == null) { Stop("Play or main camera gone"); return; }
                    if (EditorApplication.timeSinceStartup > until) { Stop("time up"); return; }
                    if (frames >= MaxFrames) { Stop("frame cap " + MaxFrames); return; }
                    if (Time.frameCount == lastFrame || EditorApplication.isPaused) return;   // one picture per game frame
                    lastFrame = Time.frameCount;
                    mainCam.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
                    mainCam.fieldOfView = main.fieldOfView;
                    var was = RenderTexture.active;
                    try
                    {
                        Grab(mainCam, mainRt, 0, 0);
                        Grab(sideCam, sideRt, W, SH);   // texture rows count from the bottom: side top-right, top bottom-right
                        Grab(topCam, topRt, W, 0);
                    }
                    finally { RenderTexture.active = was; }
                    File.WriteAllBytes(Path.Combine(folder, "f_" + frames.ToString("D5") + ".jpg"), sheet.EncodeToJPG(88));
                    float age = -1f;
                    foreach (var e in Object.FindObjectsByType<Oheangbu.App.SpellVFX120.Vfx120Effect>(FindObjectsSortMode.None)) age = Mathf.Max(age, e.Age);
                    index.WriteLine(string.Join(",", frames.ToString(Inv), Time.time.ToString("F4", Inv), EditorApplication.timeSinceStartup.ToString("F4", Inv),
                        Time.timeScale.ToString("F3", Inv), age.ToString("F4", Inv), Time.frameCount.ToString(Inv)));
                    frames++;
                }
                catch (Exception e) { Stop("error " + e.GetType().Name + " " + e.Message.Split('\n')[0]); }
            }

            static void Grab(Camera cam, RenderTexture rt, int x, int y)
            {
                cam.Render();
                RenderTexture.active = rt;
                sheet.ReadPixels(new Rect(0, 0, rt.width, rt.height), x, y, false);
            }

            public static string Stop(string why)
            {
                EditorApplication.update -= Tick;
                EditorApplication.playModeStateChanged -= OnPlayMode;
                AssemblyReloadEvents.beforeAssemblyReload -= OnReload;
                if (index == null && mainCam == null) return "rec: not recording";
                int n = frames;
                try { index?.Dispose(); } catch (Exception) { }
                index = null;
                foreach (var cam in new[] { mainCam, sideCam, topCam }) if (cam != null) Object.DestroyImmediate(cam.gameObject);
                foreach (var rt in new[] { mainRt, sideRt, topRt }) if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (sheet != null) Object.DestroyImmediate(sheet);
                mainCam = sideCam = topCam = null; mainRt = sideRt = topRt = null; sheet = null; main = null;
                string line = "rec stop (" + why + ") | " + n + " frames | " + folder;
                Debug.Log("[SpellLab308] " + line);
                return line;
            }
        }

        // lock:<dummy> - lock on to that dummy whatever the camera looks at: the lock picks the candidate nearest the screen
        // centre, so the candidate list is narrowed to the one dummy for the toggle and given back right after
        static string Lock(string name)
        {
            if (!EditorApplication.isPlaying || !InLab) return "refused: Play in the lab only";
            var walker = Object.FindFirstObjectByType<WorldMacroCombatWalker>();
            var lockOn = walker != null && walker.Motor != null ? walker.Motor.GetComponent<LockOn>() : null;
            if (lockOn == null) return "lock: no LockOn on the walker motor";
            var want = Object.FindObjectsByType<EnemyVitals>(FindObjectsSortMode.None).FirstOrDefault(v => v.name == name && v.IsAlive);
            if (want == null) return "lock: no living " + name;
            var field = typeof(LockOn).GetField("_candidates", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return "lock: LockOn._candidates not found";
            var saved = new List<EnemyVitals>((List<EnemyVitals>)field.GetValue(lockOn));
            if (lockOn.IsLocked) lockOn.Toggle();
            lockOn.SetCandidates(new[] { want });
            lockOn.Toggle();
            lockOn.SetCandidates(saved);
            float d = walker.Body != null ? Vector3.Distance(walker.Body.transform.position, want.transform.position) : -1f;
            return lockOn.Target == want ? "lock: " + name + " at " + d.ToString("F1") + " m" : "lock FAIL: " + name + " out of range or view";
        }

        // strike:on | off | status - the counterattack dummy (§2.4)
        static string Strike(string arg)
        {
            if (!EditorApplication.isPlaying || !InLab) return "refused: Play in the lab only";
            var s = Object.FindFirstObjectByType<SpellLabStriker308>();
            if (s == null) return "strike: no striker (rebuild the lab)";
            if (arg == "on") s.Arm(true); else if (arg == "off") s.Arm(false); else if (arg != "status") return "strike: on | off | status";
            var pv = Object.FindFirstObjectByType<PlayerVitals>();
            return "strike " + (s.Armed ? "armed" : "off") + " | " + s.name + " thrown " + s.Thrown + " landed " + s.Landed +
                   (pv != null ? " | player hp " + pv.Hp01.ToString("F2") : "");
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
