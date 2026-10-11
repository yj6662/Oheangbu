using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 look at a model or prefab asset without placing it (read only; nothing is saved, the open scene is not dirtied).
    //   tree:<asset path>            children (two levels) with size, vertices, materials
    //   shot:<asset path>[:<name>]   two stills (three-quarter and front) -> Art/Presentation297/Stills/asset_<name>_iso.png / _front.png
    // The instance lives far below the world with HideAndDontSave and is destroyed before the call returns. Queue-safe (no dialog).
    public static class AssetLook308
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c.StartsWith("tree:", StringComparison.Ordinal)) return Tree(c.Substring(5));
                if (c.StartsWith("shot:", StringComparison.Ordinal)) { var p = c.Substring(5).Split(':'); return Shot(p[0], p.Length > 1 ? p[1] : Path.GetFileNameWithoutExtension(p[0])); }
                return "REFUSED unknown command '" + c + "' (tree:<asset> | shot:<asset>[:<name>])";
            }
            catch (Exception e) { return "REFUSED " + e.Message; }
        }
        static GameObject Make(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (asset == null) throw new Exception("no model / prefab at " + path);
            var g = Object.Instantiate(asset); g.hideFlags = HideFlags.HideAndDontSave; foreach (var t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            g.transform.position = new Vector3(0f, -5000f, 0f); return g;
        }
        static Bounds BoundsOf(GameObject g) { var rs = g.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.zero); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }
        static long Verts(GameObject g) => g.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).Sum(f => (long)f.sharedMesh.vertexCount);
        static string Tree(string path)
        {
            var g = Make(path);
            try
            {
                var b = BoundsOf(g); var sb = new StringBuilder("AssetLook308 tree " + path + "\n  size " + b.size.ToString("F1") + ", vertices " + Verts(g) + ", renderers " + g.GetComponentsInChildren<Renderer>(true).Length + ", colliders " + g.GetComponentsInChildren<Collider>(true).Length
                    + ", materials " + string.Join(", ", g.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name + " [" + m.shader.name + "]").Distinct().Take(14)) + "\n");
                void Row(Transform t, int depth)
                {
                    var tb = BoundsOf(t.gameObject); var local = tb.center - g.transform.position;
                    sb.Append(new string(' ', 2 + depth * 2)).Append(t.name).Append("  children ").Append(t.childCount).Append("  size ").Append(tb.size.ToString("F1")).Append("  at ").Append(local.ToString("F1")).Append("  vertices ").Append(Verts(t.gameObject)).Append('\n');
                }
                int shown = 0;
                foreach (Transform a in g.transform) { if (shown++ > 60) { sb.Append("  ...\n"); break; } Row(a, 0); int k = 0; foreach (Transform bb in a) { if (k++ >= 12) { sb.Append("      ... ").Append(a.childCount - 12).Append(" more\n"); break; } Row(bb, 1); } }
                return sb.ToString();
            }
            finally { Object.DestroyImmediate(g); }
        }
        static string Shot(string path, string name)
        {
            var g = Make(path); var camGo = new GameObject("AssetLook308_Cam") { hideFlags = HideFlags.HideAndDontSave }; var lightGo = new GameObject("AssetLook308_Light") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32); var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            try
            {
                var b = BoundsOf(g); float r = Mathf.Max(b.extents.magnitude, .5f); var cam = camGo.AddComponent<Camera>(); cam.fieldOfView = 35f; cam.nearClipPlane = .1f; cam.farClipPlane = r * 12f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.78f, .76f, .7f); cam.targetTexture = rt;
                var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f; lightGo.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
                string dir = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..")), "Art", "Presentation297", "Stills"); Directory.CreateDirectory(dir); var sb = new StringBuilder("AssetLook308 shot " + path + " size " + b.size.ToString("F1") + " vertices " + Verts(g));
                foreach (var (tag, d) in new[] { ("iso", new Vector3(-.75f, .5f, -1f)), ("front", new Vector3(0f, .12f, -1f)) })
                {
                    float dist = r / Mathf.Sin(cam.fieldOfView * .5f * Mathf.Deg2Rad) * 1.05f; cam.transform.position = b.center + d.normalized * dist; cam.transform.LookAt(b.center); cam.Render();
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply(); RenderTexture.active = null; string file = Path.Combine(dir, "asset_" + name + "_" + tag + ".png"); File.WriteAllBytes(file, tex.EncodeToPNG()); sb.Append("\n  ").Append(file);
                }
                return sb.ToString();
            }
            finally { Object.DestroyImmediate(g); Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); }
        }
    }
}
