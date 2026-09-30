using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #299 road edges, any mesh layout (escort roads, forecourts, gate thresholds):
    //   mesh-soften:<town|country>:<path>[|<path>...][:save]
    // 1. the mesh is split until no edge is longer than the class resolution (conforming longest-edge rounds)
    // 2. ground-hugging meshes are laid back onto the walkable surface under them (+ their own median lift); where the
    //    surface is far (bridges, cuts) the interpolated height is kept
    // 3. vertex alpha rises from 0 at the open boundary to 1 at the class band inward, with the edge noise; boundary
    //    edges that meet another listed mesh are joints and do not fade, so consecutive road segments stay continuous
    // Material: the same blended soft copy as road-soften (opaque range 2050, no depth/normals/shadow passes).
    public static partial class GroundFit299
    {
        sealed class SoftWork
        {
            public string path, baseName; public MeshFilter filter; public MeshRenderer renderer;
            public List<Vector3> v; public List<Vector2> uv; public List<int[]> tris; public int subCount;
            public List<(Vector2 a, Vector2 b)> boundary; public bool[] joint; public string note;
        }

        static Material SoftMaterialFor(Material srcMat, string cls, RoadClass rc, string folder)
        {
            string matBase = srcMat.name;
            foreach (var suffix in new[] { "_soft299" }) if (matBase.EndsWith(suffix, StringComparison.Ordinal)) matBase = matBase.Substring(0, matBase.Length - suffix.Length);
            foreach (var suffix in new[] { "_town", "_country" }) if (matBase.EndsWith(suffix, StringComparison.Ordinal)) matBase = matBase.Substring(0, matBase.Length - suffix.Length);
            string matPath = folder + "/" + matBase.Substring(Math.Max(0, matBase.Length - 40)) + "_" + cls + "_soft299.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                if (srcMat.shader.name == "Universal Render Pipeline/Lit")
                {
                    // URP Lit cannot blend in the opaque range: the paving moves to the #297 ink architecture shader
                    var arch = Shader.Find("Oheangbu/Finish297/KoreanArchitecture") ?? throw new Exception("no KoreanArchitecture shader");
                    mat = new Material(arch) { name = Path.GetFileNameWithoutExtension(matPath) };
                    if (srcMat.HasProperty("_BaseMap")) { mat.SetTexture("_BaseMap", srcMat.GetTexture("_BaseMap")); mat.SetTextureScale("_BaseMap", srcMat.GetTextureScale("_BaseMap")); mat.SetTextureOffset("_BaseMap", srcMat.GetTextureOffset("_BaseMap")); }
                    if (srcMat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", srcMat.GetColor("_BaseColor"));
                    if (srcMat.HasProperty("_BumpMap") && srcMat.GetTexture("_BumpMap") != null) { mat.SetTexture("_BumpMap", srcMat.GetTexture("_BumpMap")); if (srcMat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", srcMat.GetFloat("_BumpScale")); }
                }
                else mat = new Material(srcMat) { name = Path.GetFileNameWithoutExtension(matPath) };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            if (mat.HasProperty("_EdgeFade")) mat.SetFloat("_EdgeFade", 1f);
            if (mat.HasProperty("_EdgeFadeWorld")) { mat.SetFloat("_EdgeFadeWorld", 1f); mat.SetVector("_EdgeFadeClump", rc.clump); }
            if (mat.HasProperty("_EdgeBlend"))
            {
                mat.SetFloat("_EdgeBlend", 1f); mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); mat.SetFloat("_ZWrite", 0f);
                mat.renderQueue = 2050;
                foreach (var pass in new[] { "DepthOnly", "DepthNormals", "DepthNormalsOnly", "ShadowCaster" }) mat.SetShaderPassEnabled(pass, false);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        static (long, long, long) Q(Vector3 p) => ((long)Mathf.Round(p.x * 100f), (long)Mathf.Round(p.y * 100f), (long)Mathf.Round(p.z * 100f));
        static string EdgeKey(Vector3 a, Vector3 b)
        {
            var qa = Q(a); var qb = Q(b);
            bool swap = qa.CompareTo(qb) > 0;
            return swap ? qb + "|" + qa : qa + "|" + qb;
        }
        // open boundary by position: an edge is boundary only when no other triangle has an edge at the same place
        static List<(Vector2 a, Vector2 b)> GeometricBoundary(IEnumerable<int[]> tris, Func<int, Vector3> world)
        {
            var count = new Dictionary<string, int>(); var sample = new Dictionary<string, (Vector3, Vector3)>();
            foreach (var t in tris) for (int k = 0; k < 3; k++)
            {
                var a = world(t[k]); var b = world(t[(k + 1) % 3]); string key = EdgeKey(a, b);
                count[key] = count.TryGetValue(key, out int n) ? n + 1 : 1; sample[key] = (a, b);
            }
            var list = new List<(Vector2, Vector2)>();
            foreach (var kv in count) if (kv.Value == 1) { var (a, b) = sample[kv.Key]; list.Add((new Vector2(a.x, a.z), new Vector2(b.x, b.z))); }
            return list;
        }

        static SoftWork Subdivide(Transform tr, MeshFilter filter, float maxEdge, RaycastHit[] hits)
        {
            var src = filter.sharedMesh;
            var w = new SoftWork { filter = filter, renderer = filter.GetComponent<MeshRenderer>() };
            w.v = new List<Vector3>(src.vertices);
            var suv = src.uv; w.uv = suv.Length == w.v.Count ? new List<Vector2>(suv) : Enumerable.Repeat(Vector2.zero, w.v.Count).ToList();
            w.subCount = src.subMeshCount; w.tris = new List<int[]>();
            for (int s = 0; s < w.subCount; s++) { var t = src.GetTriangles(s); for (int i = 0; i + 2 < t.Length; i += 3) w.tris.Add(new[] { t[i], t[i + 1], t[i + 2], s }); }
            Vector3 World(int i) => tr.TransformPoint(w.v[i]);
            int originalCount = w.v.Count, originalTris = w.tris.Count;
            // only the band along the original open boundary needs fine resolution; the interior keeps its triangles
            var rim = GeometricBoundary(w.tris, World);
            float nearBand = maxEdge * 2.5f + 2.2f;              // class band + noise reach, with margin
            bool NearRim(Vector3 a3, Vector3 b3)
            {
                var m = new Vector2((a3.x + b3.x) * .5f, (a3.z + b3.z) * .5f); float half = Vector2.Distance(new Vector2(a3.x, a3.z), new Vector2(b3.x, b3.z)) * .5f;
                foreach (var e in rim) if (SegmentDistance(m, e.a, e.b) < nearBand + half) return true;
                return false;
            }
            for (int round = 0; round < 12; round++)
            {
                var mid = new Dictionary<long, int>(); bool any = false;
                var longCache = new Dictionary<long, bool>();
                bool Long(int i, int j)
                {
                    long key = (long)Mathf.Min(i, j) * 4000000L + Mathf.Max(i, j);
                    if (longCache.TryGetValue(key, out bool r)) return r;
                    var a3 = World(i); var b3 = World(j);
                    r = Vector3.Distance(a3, b3) > maxEdge && NearRim(a3, b3);
                    longCache[key] = r; return r;
                }
                int Mid(int i, int j)
                {
                    long key = (long)Mathf.Min(i, j) * 4000000L + Mathf.Max(i, j);
                    if (mid.TryGetValue(key, out int m)) return m;
                    m = w.v.Count; w.v.Add((w.v[i] + w.v[j]) * .5f); w.uv.Add((w.uv[i] + w.uv[j]) * .5f); mid[key] = m; return m;
                }
                var next = new List<int[]>(w.tris.Count * 2);
                foreach (var t in w.tris)
                {
                    int a = t[0], b = t[1], c = t[2], s = t[3];
                    bool ab = Long(a, b), bc = Long(b, c), ca = Long(c, a);
                    if (!ab && !bc && !ca) { next.Add(t); continue; }
                    any = true;
                    if (ab && bc && ca)
                    {
                        int x = Mid(a, b), y = Mid(b, c), z = Mid(c, a);
                        next.Add(new[] { a, x, z, s }); next.Add(new[] { x, b, y, s }); next.Add(new[] { z, y, c, s }); next.Add(new[] { x, y, z, s });
                    }
                    else if (ab && bc) { int x = Mid(a, b), y = Mid(b, c); next.Add(new[] { x, b, y, s }); next.Add(new[] { a, x, y, s }); next.Add(new[] { a, y, c, s }); }
                    else if (bc && ca) { int y = Mid(b, c), z = Mid(c, a); next.Add(new[] { y, c, z, s }); next.Add(new[] { a, b, y, s }); next.Add(new[] { a, y, z, s }); }
                    else if (ca && ab) { int z = Mid(c, a), x = Mid(a, b); next.Add(new[] { a, x, z, s }); next.Add(new[] { x, b, c, s }); next.Add(new[] { x, c, z, s }); }
                    else if (ab) { int x = Mid(a, b); next.Add(new[] { a, x, c, s }); next.Add(new[] { x, b, c, s }); }
                    else if (bc) { int y = Mid(b, c); next.Add(new[] { a, b, y, s }); next.Add(new[] { a, y, c, s }); }
                    else { int z = Mid(c, a); next.Add(new[] { a, b, z, s }); next.Add(new[] { z, b, c, s }); }
                }
                w.tris = next;
                if (!any || w.v.Count > 600000) break;
            }
            // lay a ground-hugging mesh back on the surface it covers (original vertices tell its own lift)
            var own = new HashSet<Collider>(filter.GetComponents<Collider>());
            var lifts = new List<float>();
            for (int i = 0; i < originalCount; i += Mathf.Max(1, originalCount / 400)) { var p = World(i); float g = Under(p, own, hits); if (!float.IsNaN(g)) lifts.Add(p.y - g); }
            lifts.Sort();
            float lift = lifts.Count > 0 ? lifts[lifts.Count / 2] : 0f;
            int laid = 0;
            if (lifts.Count > 0 && Mathf.Abs(lift) < .3f)
            {
                float useLift = Mathf.Clamp(lift, .01f, .06f);
                for (int i = 0; i < w.v.Count; i++)
                {
                    var p = World(i); float g = Under(p, own, hits);
                    if (float.IsNaN(g) || Mathf.Abs(p.y - g) > .5f) continue;
                    p.y = g + useLift; w.v[i] = tr.InverseTransformPoint(p); laid++;
                }
            }
            // open boundary by position (seams between unshared strips are interior), world XZ
            w.boundary = GeometricBoundary(w.tris, World);
            w.note = $"{originalCount} v/{originalTris} t -> {w.v.Count} v/{w.tris.Count} t, lift {lift:F2} m (laid {laid}), boundary {w.boundary.Count} edges";
            return w;
        }

        static string MeshSoften(string argument)
        {
            bool save = argument.EndsWith(":save", StringComparison.Ordinal);
            if (save) argument = argument.Substring(0, argument.Length - 5);
            int colon = argument.IndexOf(':');
            string cls = colon < 0 ? "" : argument.Substring(0, colon);
            if (colon < 0 || !Classes.TryGetValue(cls, out var rc)) throw new ArgumentException("mesh-soften:<town|country>:<path>[|...][:save]");
            float band = rc.skirt + rc.inner, maxEdge = cls == "town" ? .45f : .8f;
            var scene = SceneManager.GetActiveScene();
            var hits = new RaycastHit[64];
            var sb = new StringBuilder();
            Physics.SyncTransforms();
            string folder = CopyFolder + "/Soft";
            if (!AssetDatabase.IsValidFolder(folder)) { Directory.CreateDirectory(Path.GetFullPath(folder)); AssetDatabase.Refresh(); }
            var works = new List<SoftWork>();
            foreach (string path in argument.Substring(colon + 1).Split('|').Where(s => s.Length > 0))
            {
                var tr = FindPath(scene, path);
                var filter = tr != null ? tr.GetComponent<MeshFilter>() : null;
                if (filter == null || filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null) { sb.AppendLine("MISSING " + path); continue; }
                string baseName = filter.sharedMesh.name;
                foreach (var suffix in new[] { "_town_soft299", "_country_soft299", "_299" }) if (baseName.EndsWith(suffix, StringComparison.Ordinal)) baseName = baseName.Substring(0, baseName.Length - suffix.Length);
                string meshPath = folder + "/" + baseName + "_" + cls + "_soft299.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (existing != null)
                {
                    var mr = filter.GetComponent<MeshRenderer>();
                    filter.sharedMesh = existing; mr.sharedMaterials = mr.sharedMaterials.Select(m => m == null ? null : SoftMaterialFor(m, cls, rc, folder)).ToArray();
                    EditorUtility.SetDirty(filter); EditorUtility.SetDirty(mr);
                    sb.AppendLine($"{path} [{cls}]: REUSE {meshPath}"); continue;
                }
                if (!filter.sharedMesh.isReadable) { sb.AppendLine("SKIP (mesh not readable) " + path); continue; }
                {
                    // only surfaces that face up: a soft edge on walls, kerbs or cut faces would turn them see-through
                    var sv = filter.sharedMesh.vertices; var st = filter.sharedMesh.triangles; var m4 = tr.localToWorldMatrix; double up = 0, area = 0;
                    for (int i = 0; i + 2 < st.Length; i += 3) { var nrm = Vector3.Cross(m4.MultiplyPoint3x4(sv[st[i + 1]]) - m4.MultiplyPoint3x4(sv[st[i]]), m4.MultiplyPoint3x4(sv[st[i + 2]]) - m4.MultiplyPoint3x4(sv[st[i]])); float ar = nrm.magnitude; if (ar < 1e-6f) continue; up += Mathf.Abs(nrm.y); area += ar; }
                    float share = area > 0 ? (float)(up / area) : 0f;
                    if (share < .8f) { sb.AppendLine($"SKIP (not a flat surface, up share {share:F2}) " + path); continue; }
                }
                var work = Subdivide(tr, filter, maxEdge, hits); work.path = path; work.baseName = baseName; works.Add(work);
            }
            // joints: boundary edges lying on another listed mesh's boundary
            foreach (var w in works)
            {
                w.joint = new bool[w.boundary.Count];
                for (int i = 0; i < w.boundary.Count; i++)
                {
                    var m = (w.boundary[i].a + w.boundary[i].b) * .5f;
                    foreach (var o in works) { if (o == w) continue; foreach (var e in o.boundary) if (SegmentDistance(m, e.a, e.b) < 1.0f) { w.joint[i] = true; break; } if (w.joint[i]) break; }
                }
            }
            foreach (var w in works)
            {
                var tr = w.filter.transform;
                // spatial hash of fading boundary segments
                const float cell = 2f;
                var grid = new Dictionary<long, List<int>>();
                long Key(int x, int z) => (long)x * 1000003L + z;
                for (int i = 0; i < w.boundary.Count; i++)
                {
                    if (w.joint[i]) continue;
                    var (a, b) = w.boundary[i];
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / cell), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / cell), z0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) / cell), z1 = Mathf.FloorToInt(Mathf.Max(a.y, b.y) / cell);
                    for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++) { long k = Key(x, z); if (!grid.TryGetValue(k, out var list)) grid[k] = list = new List<int>(); list.Add(i); }
                }
                int reach = Mathf.CeilToInt(band / cell) + 1;
                var colors = new Color[w.v.Count];
                for (int i = 0; i < w.v.Count; i++)
                {
                    var p = tr.TransformPoint(w.v[i]); var q = new Vector2(p.x, p.z);
                    int cx = Mathf.FloorToInt(q.x / cell), cz = Mathf.FloorToInt(q.y / cell);
                    float dist = float.PositiveInfinity;
                    for (int x = cx - reach; x <= cx + reach; x++) for (int z = cz - reach; z <= cz + reach; z++)
                        if (grid.TryGetValue(Key(x, z), out var list)) foreach (int s in list) dist = Mathf.Min(dist, SegmentDistance(q, w.boundary[s].a, w.boundary[s].b));
                    float alpha = 1f;
                    if (dist < band + rc.noise)
                    {
                        float noisy = dist + (EdgeNoise(p.x, p.z) - .5f) * rc.noise * 2f;
                        alpha = dist < .02f ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(noisy / band));
                    }
                    colors[i] = new Color(1, 1, 1, alpha);
                }
                var mesh = new Mesh { name = w.baseName + "_" + cls + "_soft299" };
                if (w.v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(w.v); mesh.SetUVs(0, w.uv); mesh.SetColors(colors);
                mesh.subMeshCount = w.subCount;
                for (int s = 0; s < w.subCount; s++) mesh.SetTriangles(w.tris.Where(t => t[3] == s).SelectMany(t => new[] { t[0], t[1], t[2] }).ToArray(), s);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                string meshPath = folder + "/" + w.baseName + "_" + cls + "_soft299.asset";
                AssetDatabase.CreateAsset(mesh, meshPath);
                w.filter.sharedMesh = mesh;
                w.renderer.sharedMaterials = w.renderer.sharedMaterials.Select(m => m == null ? null : SoftMaterialFor(m, cls, rc, folder)).ToArray();
                EditorUtility.SetDirty(w.filter); EditorUtility.SetDirty(w.renderer);
                sb.AppendLine($"{w.path} [{cls}]: NEW {w.note}, joints {w.joint.Count(j => j)}, faded {colors.Count(c => c.a < .999f)} v -> {meshPath}");
            }
            AssetDatabase.SaveAssets();
            if (save && scene.isDirty) SaveWithBackup(scene, sb);
            return sb.ToString();
        }
    }
}
