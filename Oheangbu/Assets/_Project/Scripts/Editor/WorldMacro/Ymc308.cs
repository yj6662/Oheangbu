using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 light copies of the Yongmeori coast rock pack (Assets/YongmeoriCoast, KHS scans: 1 - 5 million triangles a piece, no LOD).
    // The pack itself is never changed. A copy is the source mesh clustered on a grid (one position per cell, so the surface stays
    // closed; uv kept per texture block so islands do not smear into each other), written to Assets/_Project/Art/World/Ymc308.
    // Used by CliffWay308 dress (벼랑 잔도) and MumSite308 rock (뭄 자리).
    //   lite:<prefab>[:<cell0 m>[:<cell1 m>]]    one piece, two levels (L0 near, L1 far)
    //   lite-set[:<cell0>[:<cell1>]]             the cliff pieces CliffWay308 uses
    //   status
    //   show:<x>,<y>,<z>[:<spacing>] | hide     the copies in a row in the active scene (root Ymc308_Show), for stills
    public static class Ymc308
    {
        public const string Dir = "Assets/_Project/Art/World/Ymc308";
        public static readonly string[] CliffSet = { "SM_YMC_Cliff02C", "SM_YMC_Cliff04C", "SM_YMC_Cliff03B", "SM_YMC_Cliff02B", "SM_YMC_Cliff06B", "SM_YMC_Cliff04B", "SM_YMC_Cliff01A", "SM_YMC_Cliff02C_1" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Run(string command)
        {
            var a = (command ?? "").Split(':');
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED edit mode only";
                switch (a[0])
                {
                    case "lite": return Lite(a[1], a.Length > 2 ? float.Parse(a[2], Inv) : .7f, a.Length > 3 ? float.Parse(a[3], Inv) : 2.5f);
                    case "lite-set":
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (string p in CliffSet) { sb.AppendLine(Lite(p, a.Length > 1 ? float.Parse(a[1], Inv) : .7f, a.Length > 2 ? float.Parse(a[2], Inv) : 2.5f)); EditorUtility.UnloadUnusedAssetsImmediate(); }
                        return sb.ToString().TrimEnd();
                    }
                    case "status": return AssetDatabase.IsValidFolder(Dir) ? string.Join("\n", AssetDatabase.FindAssets("t:Mesh", new[] { Dir }).Select(AssetDatabase.GUIDToAssetPath).Select(p => { var m = AssetDatabase.LoadAssetAtPath<Mesh>(p); return m.name + " vertices " + m.vertexCount + " triangles " + Triangles(m) + " size " + m.bounds.size.ToString("F1"); })) : "no copies";
                    case "show": return Show(a);
                    case "hide": { var r = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "Ymc308_Show"); if (r != null) Object.DestroyImmediate(r); return "hidden"; }
                }
            }
            catch (Exception e) { return "REFUSED " + e.GetType().Name + ": " + e.Message; }
            return "REFUSED lite:<prefab>[:cell0[:cell1]] | lite-set | status | show:<x>,<y>,<z>[:spacing] | hide";
        }

        static long Triangles(Mesh m) { long n = 0; for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3; return n; }

        public static GameObject Prefab(string name)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/YongmeoriCoast/Prefabs/" + name + ".prefab");
            return p != null ? p : throw new Exception("no prefab Assets/YongmeoriCoast/Prefabs/" + name);
        }

        public static Material[] Materials(string name) => Prefab(name).GetComponentsInChildren<MeshRenderer>(true).OrderByDescending(r => r.GetComponent<MeshFilter>()?.sharedMesh?.vertexCount ?? 0).First().sharedMaterials;

        public static Mesh Load(string name, int level)
        {
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(Dir + "/" + name + "_L" + level + ".asset");
            return m != null ? m : throw new Exception("no light copy of " + name + " (run Ymc308 lite:" + name + ")");
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        static string Lite(string name, float cell0, float cell1)
        {
            var filter = Prefab(name).GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).OrderByDescending(f => f.sharedMesh.vertexCount).First();
            var src = filter.sharedMesh; var pos = src.vertices; var uv = src.uv;
            // the copy is in the prefab root's space (rotation and scale of the model child baked in), so an instance needs no child offset
            var toRoot = Prefab(name).transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int i = 0; i < pos.Length; i++) pos[i] = toRoot.MultiplyPoint3x4(pos[i]);
            bool flip = toRoot.determinant < 0f;
            var tris = new int[src.subMeshCount][]; for (int s = 0; s < tris.Length; s++) tris[s] = src.GetTriangles(s);
            Folder(Dir); var report = name + " source vertices " + pos.Length + " triangles " + tris.Sum(t => t.Length / 3);
            foreach (var (level, cell) in new[] { (0, cell0), (1, cell1) })
            {
                var lite = Cluster(pos, uv, tris, cell, name + "_L" + level, flip);
                string path = Dir + "/" + lite.name + ".asset"; AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(lite, path); AssetDatabase.SaveAssetIfDirty(lite);
                report += " | L" + level + " cell " + cell.ToString("0.00", Inv) + " vertices " + lite.vertexCount + " triangles " + Triangles(lite);
            }
            return report + " | size " + Load(name, 0).bounds.size.ToString("F1") + " materials " + src.subMeshCount;
        }

        // One position per grid cell (the mean of what falls in it); one vertex per (cell, texture block); triangles that lose a corner go.
        public static Mesh Cluster(Vector3[] pos, Vector2[] uv, int[][] tris, float cell, string name, bool flip = false)
        {
            var b = new Bounds(pos[0], Vector3.zero); foreach (var p in pos) b.Encapsulate(p);
            Vector3 min = b.min; float inv = 1f / cell; bool hasUv = uv != null && uv.Length == pos.Length;
            var cellOf = new Dictionary<long, int>(); var sum = new List<Vector3>(); var count = new List<int>();
            var vertOf = new Dictionary<long, int>(); var vCell = new List<int>(); var vUv = new List<Vector2>();
            var map = new int[pos.Length];
            for (int i = 0; i < pos.Length; i++)
            {
                var p = pos[i]; long key = (long)((p.x - min.x) * inv) | ((long)((p.y - min.y) * inv) << 20) | ((long)((p.z - min.z) * inv) << 40);
                if (!cellOf.TryGetValue(key, out int c)) { c = sum.Count; cellOf[key] = c; sum.Add(Vector3.zero); count.Add(0); }
                sum[c] += p; count[c]++;
                long block = hasUv ? (long)(Mathf.FloorToInt(uv[i].x * 8f) & 1023) | ((long)(Mathf.FloorToInt(uv[i].y * 8f) & 1023) << 10) : 0;
                long vk = ((long)c << 20) | block;
                if (!vertOf.TryGetValue(vk, out int v)) { v = vCell.Count; vertOf[vk] = v; vCell.Add(c); vUv.Add(hasUv ? uv[i] : Vector2.zero); }
                map[i] = v;
            }
            var cp = new Vector3[sum.Count]; for (int c = 0; c < cp.Length; c++) cp[c] = sum[c] / count[c];
            var cn = new Vector3[cp.Length]; var outTris = new List<int>[tris.Length];
            for (int s = 0; s < tris.Length; s++)
            {
                var t = tris[s]; var o = new List<int>(t.Length / 20); var seen = new HashSet<(int, int, int)>();
                for (int i = 0; i < t.Length; i += 3)
                {
                    int x = map[t[i]], y = map[t[i + 1]], z = map[t[i + 2]]; int cx = vCell[x], cy = vCell[y], cz = vCell[z];
                    if (cx == cy || cy == cz || cx == cz) continue;
                    if (flip) { (y, z) = (z, y); (cy, cz) = (cz, cy); }
                    int lo = Math.Min(cx, Math.Min(cy, cz)); var id = lo == cx ? (cx, cy, cz) : lo == cy ? (cy, cz, cx) : (cz, cx, cy);
                    if (!seen.Add(id)) continue;
                    o.Add(x); o.Add(y); o.Add(z);
                    var n = Vector3.Cross(cp[cy] - cp[cx], cp[cz] - cp[cx]); cn[cx] += n; cn[cy] += n; cn[cz] += n;
                }
                outTris[s] = o;
            }
            // only the vertices a kept triangle uses
            var slot = new int[vCell.Count]; for (int i = 0; i < slot.Length; i++) slot[i] = -1; var keep = new List<int>();
            foreach (var o in outTris) for (int i = 0; i < o.Count; i++) { int v = o[i]; if (slot[v] < 0) { slot[v] = keep.Count; keep.Add(v); } o[i] = slot[v]; }
            return Make(name, keep.Select(v => cp[vCell[v]]).ToArray(), keep.Select(v => Unit(cn[vCell[v]])).ToArray(), keep.Select(v => vUv[v]).ToArray(), outTris.Select(o => o.ToArray()).ToArray());
        }

        // Vector3.normalized gives zero for short vectors (zero normals bloom into NaN): scale first, fall back to up.
        public static Vector3 Unit(Vector3 n)
        {
            float m = Mathf.Max(Mathf.Abs(n.x), Mathf.Max(Mathf.Abs(n.y), Mathf.Abs(n.z))); if (m < 1e-20f) return Vector3.up;
            n /= m; return n / n.magnitude;
        }

        public static Mesh Make(string name, Vector3[] v, Vector3[] n, Vector2[] uv, int[][] tris)
        {
            var mesh = new Mesh { name = name, indexFormat = v.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.subMeshCount = tris.Length;
            for (int s = 0; s < tris.Length; s++) mesh.SetTriangles(tris[s], s, false);
            mesh.RecalculateBounds(); mesh.RecalculateTangents(); return mesh;
        }

        // Normals again after vertices were moved: summed over vertices that share a position (the copies split vertices by texture block).
        public static Vector3[] Normals(Vector3[] v, int[][] tris)
        {
            var at = new Dictionary<Vector3, int>(); var id = new int[v.Length];
            for (int i = 0; i < v.Length; i++) { if (!at.TryGetValue(v[i], out int k)) { k = at.Count; at[v[i]] = k; } id[i] = k; }
            var acc = new Vector3[at.Count];
            foreach (var t in tris) for (int i = 0; i < t.Length; i += 3) { var n = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]); acc[id[t[i]]] += n; acc[id[t[i + 1]]] += n; acc[id[t[i + 2]]] += n; }
            var o = new Vector3[v.Length]; for (int i = 0; i < o.Length; i++) o[i] = Unit(acc[id[i]]); return o;
        }

        static string Show(string[] a)
        {
            var c = a[1].Split(','); var at = new Vector3(float.Parse(c[0], Inv), float.Parse(c[1], Inv), float.Parse(c[2], Inv)); float gap = a.Length > 2 ? float.Parse(a[2], Inv) : 100f;
            var scene = SceneManager.GetActiveScene(); var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Ymc308_Show"); if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("Ymc308_Show") { hideFlags = HideFlags.DontSave }; SceneManager.MoveGameObjectToScene(root, scene);
            var names = AssetDatabase.FindAssets("_L0 t:Mesh", new[] { Dir }).Select(AssetDatabase.GUIDToAssetPath).Select(p => System.IO.Path.GetFileNameWithoutExtension(p)).Where(n => n.EndsWith("_L0")).Select(n => n.Substring(0, n.Length - 3)).OrderBy(n => n).ToArray();
            var sb = new System.Text.StringBuilder("show " + names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                var g = new GameObject(names[i]) { hideFlags = HideFlags.DontSave }; g.transform.SetParent(root.transform, false); g.transform.position = at + Vector3.right * gap * i;
                g.AddComponent<MeshFilter>().sharedMesh = Load(names[i], 0); g.AddComponent<MeshRenderer>().sharedMaterials = Materials(names[i]);
                sb.Append('\n').Append(names[i]).Append(" at ").Append(g.transform.position.ToString("F0"));
            }
            return sb.ToString();
        }
    }
}
