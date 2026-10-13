using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 building assembly audit (edit mode, queue safe, no dialogs). Numbers: Tools/Unity/Stage308_assembly/assembly308.json.
    //   tree[:depth]            hierarchy digest of the play scene and of each realm scene (opened one at a time) -> Out/tree_<scene>.txt   (read only)
    // Other commands live in the partial files.
    public static partial class Assembly308
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        internal static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        internal static string Stage => Path.Combine(Repo, "Tools", "Unity", "Stage308_assembly");
        internal static string OutDir { get { string d = Path.Combine(Stage, "Out"); Directory.CreateDirectory(d); return d; } }
        const string MainPath = "Assets/_Project/Scenes/World/W_Demo_Main.unity";
        const string StreamDir = "Assets/_Project/Scenes/World/Stream";

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only (Play is running)";
                var main = SceneManager.GetSceneByPath(MainPath); if (!main.IsValid() || !main.isLoaded) return "REFUSED open W_Demo_Main first";
                for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) return "REFUSED scene has unsaved changes: " + SceneManager.GetSceneAt(i).path;
                string[] a = c.Split(':');
                if (a[0] == "tree") return Tree(main, a.Length > 1 ? int.Parse(a[1], Inv) : 3);
                return RunMore(main, c, a);
            }
            catch (Exception e) { return "REFUSED " + e.GetType().Name + " " + e.Message + "\n" + e.StackTrace; }
        }

        internal static string[] RealmScenePaths() => Directory.GetFiles(Path.Combine(Application.dataPath, "_Project", "Scenes", "World", "Stream"), "Realm_*.unity").Select(f => StreamDir + "/" + Path.GetFileName(f)).OrderBy(p => p, StringComparer.Ordinal).ToArray();

        /// <summary>Runs the action on the play scene, then on each realm scene opened alone (additive) and closed again without saving.</summary>
        internal static void EachScene(Scene main, Action<Scene> act, bool mainToo = true, string only = null)
        {
            if (mainToo && (only == null || only == "Main")) act(main);
            foreach (string p in RealmScenePaths())
            {
                string id = Path.GetFileNameWithoutExtension(p).Substring(6); if (only != null && only != id) continue;
                var s = SceneManager.GetSceneByPath(p); bool opened = false;
                if (!s.IsValid() || !s.isLoaded) { s = EditorSceneManager.OpenScene(p, OpenSceneMode.Additive); opened = true; SceneManager.SetActiveScene(main); }
                try { Physics.SyncTransforms(); act(s); }
                finally { if (opened) { if (s.isDirty) throw new Exception("realm scene became dirty during a read: " + p); EditorSceneManager.CloseScene(s, true); } }
            }
            EditorUtility.UnloadUnusedAssetsImmediate(true);
        }

        internal static bool Drawn(Renderer r) => r != null && r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer);
        internal static bool BoundsOf(Transform t, out Bounds b)
        {
            b = default; bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>(false)) { if (!Drawn(r)) continue; if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; } }
            return any;
        }
        internal static string F(float v) => v.ToString("0.00", Inv);
        internal static string F1(float v) => v.ToString("0.0", Inv);

        static string Tree(Scene main, int depth)
        {
            var sum = new StringBuilder("Assembly308 tree\n");
            EachScene(main, s =>
            {
                var sb = new StringBuilder();
                foreach (var root in s.GetRootGameObjects().OrderBy(g => g.name, StringComparer.Ordinal))
                {
                    int rn = root.GetComponentsInChildren<Renderer>(true).Length; if (rn == 0) continue;
                    Node(sb, root.transform, 0, depth);
                }
                string file = Path.Combine(OutDir, "tree_" + s.name + ".txt"); File.WriteAllText(file, sb.ToString());
                sum.Append("  ").Append(s.name).Append(" roots ").Append(s.rootCount).Append(" -> ").Append(file).Append('\n');
            });
            return sum.ToString();
        }
        static readonly Regex Tail = new Regex(@"[_\- ]?\(?\d+\)?$", RegexOptions.Compiled);
        static void Node(StringBuilder sb, Transform t, int d, int depth)
        {
            int rn = t.GetComponentsInChildren<Renderer>(false).Count(Drawn); bool has = BoundsOf(t, out var b);
            string src = PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) ? " prefab=" + Path.GetFileName(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject)) : "";
            sb.Append(new string(' ', d * 2)).Append(t.name).Append(t.gameObject.activeInHierarchy ? "" : " [OFF]").Append(" r=").Append(rn).Append(" ch=").Append(t.childCount);
            if (t.GetComponent<LODGroup>() != null) sb.Append(" LOD"); if (t.GetComponent<Renderer>() != null) sb.Append(" R");
            if (has) sb.Append(" c=").Append(F1(b.center.x)).Append(',').Append(F1(b.center.y)).Append(',').Append(F1(b.center.z)).Append(" s=").Append(F1(b.size.x)).Append('x').Append(F1(b.size.y)).Append('x').Append(F1(b.size.z));
            sb.Append(src).Append('\n');
            if (d >= depth || !t.gameObject.activeInHierarchy) return;
            var groups = new Dictionary<string, List<Transform>>(); var order = new List<string>();
            foreach (Transform c in t) { string k = Tail.Replace(c.name, ""); if (!groups.TryGetValue(k, out var l)) { groups[k] = l = new List<Transform>(); order.Add(k); } l.Add(c); }
            foreach (string k in order)
            {
                var l = groups[k];
                if (l.Count <= 3) { foreach (var c in l) Node(sb, c, d + 1, depth); continue; }
                Node(sb, l[0], d + 1, depth); sb.Append(new string(' ', d * 2 + 2)).Append("... ").Append(k).Append(" x").Append(l.Count).Append(" (first shown)\n");
            }
        }
    }
}
