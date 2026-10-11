using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 the first mine, look pass (SPEC-PLAYTEST-306 mine + D308-60). The data the mine was generated from is gone (2026-10-06), so
    // nothing here re-runs a mine generator: the tool reads what the scene holds and adds to it or swaps what is drawn.
    //   dump        every renderer, light and collider under the `mine` root -> Tools/Unity/Stage308_mine/dump.json (read only)
    // Queue-safe: Run(string) never opens a dialog; refusals come back as "REFUSED ...".
    public static partial class Mine308
    {
        const string RootName = "mine", SceneName = "W_Demo_Main";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string StageDir => Path.Combine(RepoRoot, "Tools", "Unity", "Stage308_mine");

        [Serializable] sealed class DumpRow
        {
            public string path = "", mesh = "", meshAsset = "", materials = "", shaders = "", light = ""; public bool active, renderer, collider;
            public float[] centre = Array.Empty<float>(), size = Array.Empty<float>(), pos = Array.Empty<float>(), euler = Array.Empty<float>(), scale = Array.Empty<float>(), localCentre = Array.Empty<float>(), localSize = Array.Empty<float>(); public int vertices;
        }
        [Serializable] sealed class Dump { public List<DumpRow> rows = new List<DumpRow>(); }

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c == "dump") return DumpScene();
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only (Play is running)";
                if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "REFUSED scripts are compiling or failed to compile";
                return Extra(c) ?? "REFUSED unknown command '" + c + "'";
            }
            catch (Exception e) { return "REFUSED " + e.Message; }
        }

        static partial void ExtraCommand(string c, ref string answer);
        static string Extra(string c) { string answer = null; ExtraCommand(c, ref answer); return answer; }

        static Scene PlayScene(bool clean)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name != SceneName) throw new Exception("open " + SceneName + " first (active scene is " + scene.name + ")");
            if (clean && scene.isDirty) throw new Exception("the play scene has unsaved changes");
            return scene;
        }
        static GameObject MineRoot(Scene scene) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName) ?? throw new Exception("no `mine` root in " + scene.name);
        static string PathOf(Transform t, Transform root) { var parts = new List<string>(); for (; t != null && t != root; t = t.parent) parts.Add(t.name); parts.Reverse(); return string.Join("/", parts); }
        static float[] A(Vector3 v) => new[] { v.x, v.y, v.z };

        static string DumpScene()
        {
            var scene = PlayScene(false); var root = MineRoot(scene); var dump = new Dump();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var r = t.GetComponent<Renderer>(); var l = t.GetComponent<Light>(); var col = t.GetComponent<Collider>(); var mf = t.GetComponent<MeshFilter>();
                if (r == null && l == null && col == null) continue;
                var row = new DumpRow { path = PathOf(t, root.transform), active = t.gameObject.activeInHierarchy, renderer = r != null && r.enabled, collider = col != null, pos = A(t.position), euler = A(t.eulerAngles), scale = A(t.lossyScale) };
                if (mf != null && mf.sharedMesh != null) { row.mesh = mf.sharedMesh.name; row.meshAsset = AssetDatabase.GetAssetPath(mf.sharedMesh); row.vertices = mf.sharedMesh.vertexCount; row.localCentre = A(mf.sharedMesh.bounds.center); row.localSize = A(mf.sharedMesh.bounds.size); }
                if (r != null) { row.centre = A(r.bounds.center); row.size = A(r.bounds.size); row.materials = string.Join("|", r.sharedMaterials.Select(m => m != null ? AssetDatabase.GetAssetPath(m) : "null")); row.shaders = string.Join("|", r.sharedMaterials.Select(m => m != null && m.shader != null ? m.shader.name : "null").Distinct()); }
                else if (col != null) { row.centre = A(col.bounds.center); row.size = A(col.bounds.size); }
                if (l != null) row.light = l.type + " range " + l.range.ToString("F1", Inv) + " intensity " + l.intensity.ToString("F2", Inv) + " colour " + ColorUtility.ToHtmlStringRGB(l.color) + " enabled " + l.enabled + " shadows " + l.shadows;
                dump.rows.Add(row);
            }
            Directory.CreateDirectory(StageDir); string file = Path.Combine(StageDir, "dump.json"); File.WriteAllText(file, JsonUtility.ToJson(dump));
            return "Mine308 dump: " + dump.rows.Count + " rows (renderers " + dump.rows.Count(x => x.materials.Length > 0) + ", lights " + dump.rows.Count(x => x.light.Length > 0) + ") -> " + file;
        }
    }
}
