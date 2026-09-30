using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #299 ground fit (editor only). Thin ground meshes (road/yard ribbons, pads, paving) must sit on what the player
    // walks on. Reworld292 moved whole place containers (Village245, Village249Frontage, ...) as rigid bodies onto the new
    // surface, so ribbons baked +3.5 cm over the old surface now hover over or sink into the new one.
    //   ground-audit[:<hierarchy prefix>] — active scene, read only. Per ground-like mesh, sampled world vertices vs the
    //   highest static walkable collider under them (own colliders, triggers, capsules/spheres and rigidbodies ignored).
    // Output: <repo>/Art/World/Compact/Rebuild/GroundFit299/audit-<scene>.json and a text summary.
    //   ground-drape:<path>[|<path>...][:save] — active scene. Each object's mesh is copied once to
    //   Assets/_Project/Art/World/Finish297/GroundFit299/<mesh>_299.asset (later scenes reuse the copy), every vertex is
    //   dropped onto the terrain tile under it +3.5 cm (the Village245 ribbon offset) and the MeshFilter points at the
    //   copy. The shared original stays as it is: older worlds (Mountains, Reworld, Watershed295, ...) still use it in
    //   place. :save backs the scene file up under Rebuild/GroundFit299/SceneBackup and saves it.
    public static partial class GroundFit299
    {
        const float Tolerance = .25f;           // |gap| beyond this is a hover/sink sample (m)
        const float NearShare = .30f;           // a mesh counts as ground-level when this share of samples is within tolerance
        const int SamplesPerMesh = 400;
        static readonly string[] GroundWords = { "road", "yard", "path", "trail", "street", "plaza", "pave", "madang", "ground", "apron", "ribbon", "village_", "courtyard", "walk" };
        static readonly string[] NotGround = { "fence", "rail", "post", "wall", "roof", "beam", "stair", "step", "bench", "sign", "lantern", "table" };
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/World/Compact/Rebuild/GroundFit299"));

        public static string Run(string command)
        {
            string[] a = command.Split(new[] { ':' }, 2);
            switch (a[0])
            {
                case "ground-audit": return Audit(a.Length > 1 ? a[1] : "");
                case "ground-drape": return Drape(a.Length > 1 ? a[1] : "");
                case "road-survey": return Survey(a.Length > 1 ? a[1] : "");
                case "road-soften": return Soften(a.Length > 1 ? a[1] : "");
                case "stone-skirt": return StoneSkirt(a.Length > 1 ? a[1] : "");
                case "children": return Children(a[1]);
                case "flat-search": return FlatSearch(a[1]);
                case "relocate": return Relocate(a[1]);
                case "relocate-near": return RelocateNear(a[1]);
                case "preserve-find": return PreserveFind(a[1]);
                case "mesh-soften": return MeshSoften(a[1]);
                case "road-visible": return RoadVisible();
                case "mesh-set":
                {
                    // mesh-set:<mesh asset path>:<path> — point one MeshFilter at a mesh asset (revert a rebuild)
                    int k = a[1].IndexOf(':'); string meshPath = a[1].Substring(0, k);
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) ?? throw new Exception("no mesh " + meshPath);
                    var f = FindPath(SceneManager.GetActiveScene(), a[1].Substring(k + 1))?.GetComponent<MeshFilter>() ?? throw new Exception("no filter " + a[1].Substring(k + 1));
                    f.sharedMesh = mesh; EditorUtility.SetDirty(f); return a[1].Substring(k + 1) + " -> " + mesh.name;
                }
                case "mat-tune":
                {
                    // mat-tune:<material asset path>:<name>=<value>[,<name>=<value>...] — floats; queue=<int>; pass:<LightMode>=0|1
                    int k = a[1].IndexOf(':'); string matPath = a[1].Substring(0, k);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath) ?? throw new Exception("no material " + matPath);
                    var sb = new StringBuilder();
                    foreach (string kv in a[1].Substring(k + 1).Split(','))
                    {
                        int e = kv.LastIndexOf('='); string key = kv.Substring(0, e); string val = kv.Substring(e + 1);
                        if (key == "queue") mat.renderQueue = int.Parse(val, CultureInfo.InvariantCulture);
                        else if (key.StartsWith("pass/", StringComparison.Ordinal)) mat.SetShaderPassEnabled(key.Substring(5), val != "0");
                        else mat.SetFloat(key, float.Parse(val, CultureInfo.InvariantCulture));
                        sb.Append(key + "=" + val + " ");
                    }
                    EditorUtility.SetDirty(mat); AssetDatabase.SaveAssets();
                    return mat.name + ": " + sb + "(queue " + mat.renderQueue + ")";
                }
                case "material-set":
                {
                    // material-set:<material asset path>:<path>[|<path>...] — point renderers at a material (repair or revert)
                    int k = a[1].IndexOf(':'); string matPath = a[1].Substring(0, k);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath) ?? throw new Exception("no material " + matPath);
                    var scene = SceneManager.GetActiveScene(); var sb = new StringBuilder();
                    foreach (string path in a[1].Substring(k + 1).Split('|').Where(s => s.Length > 0))
                    {
                        var r = FindPath(scene, path)?.GetComponent<MeshRenderer>();
                        if (r == null) { sb.AppendLine("MISSING " + path); continue; }
                        r.sharedMaterial = mat; EditorUtility.SetDirty(r); sb.AppendLine(path + " -> " + mat.name);
                    }
                    return sb.ToString();
                }
                case "scene-open":
                {
                    // queue-safe scene switch: refuses while playing or when the open scene has unsaved changes
                    if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: playing";
                    var active = SceneManager.GetActiveScene();
                    // scene-open:<path>:discard reloads without saving (used to drop a trial edit on the open scene)
                    bool discard = a[1].EndsWith(":discard", StringComparison.Ordinal);
                    string target = discard ? a[1].Substring(0, a[1].Length - 8) : a[1];
                    if (active.isDirty && !discard) return "refused: " + active.path + " has unsaved changes";
                    var s = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(target, UnityEditor.SceneManagement.OpenSceneMode.Single);
                    return "opened " + s.path + " roots " + s.rootCount;
                }
                default: throw new ArgumentException("Unknown GroundFit299 command " + command);
            }
        }

        [Serializable] sealed class Row
        {
            public string path, mesh, root, kind;            // kind: ribbon (visual only) | platform (has a collider)
            public bool hasCollider, namedGround, defect;
            public int samples, hover, sink, near, noGround;
            public float maxHover, maxSink, meanAbs, normalUp;
            public Vector3 center, size, worstHover, worstSink;
        }
        [Serializable] sealed class Report
        {
            public string scene, prefix, created;
            public float tolerance = Tolerance;
            public int scanned, groundLike, defects;
            public List<Row> rows = new List<Row>();
        }

        static string PathOf(Transform t) { var names = new List<string>(); for (var p = t; p != null; p = p.parent) names.Add(p.name); names.Reverse(); return string.Join("/", names); }

        static bool Walkable(Collider c, HashSet<Collider> own)
        {
            if (c == null || c.isTrigger || own.Contains(c) || c.attachedRigidbody != null) return false;
            return c is MeshCollider || c is TerrainCollider || c is BoxCollider;
        }

        // highest walkable surface at or below (y + 3 m) under the point; NaN when nothing is under it
        static float Under(Vector3 p, HashSet<Collider> own, RaycastHit[] hits)
        {
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + 40f, p.z), Vector3.down, hits, 120f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NaN;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.normal.y < .3f || h.point.y > p.y + 3f || !Walkable(h.collider, own)) continue;
                if (float.IsNaN(best) || h.point.y > best) best = h.point.y;
            }
            return best;
        }

        const string CopyFolder = "Assets/_Project/Art/World/Finish297/GroundFit299";
        const float RibbonOffset = .035f;

        static float TerrainY(Vector3 p, RaycastHit[] hits)
        {
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + 60f, p.z), Vector3.down, hits, 200f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NaN;
            for (int i = 0; i < n; i++)
                if (hits[i].collider != null && hits[i].collider.transform.root.name == "Reworld292_Terrain" && (float.IsNaN(best) || hits[i].point.y > best)) best = hits[i].point.y;
            return best;
        }

        static string Drape(string argument)
        {
            bool save = argument.EndsWith(":save", StringComparison.Ordinal);
            if (save) argument = argument.Substring(0, argument.Length - 5);
            var scene = SceneManager.GetActiveScene();
            var hits = new RaycastHit[64];
            var sb = new StringBuilder();
            Physics.SyncTransforms();
            if (!AssetDatabase.IsValidFolder(CopyFolder)) { Directory.CreateDirectory(Path.GetFullPath(CopyFolder)); AssetDatabase.Refresh(); }
            foreach (string path in argument.Split('|').Where(s => s.Length > 0))
            {
                var parts = path.Split('/');
                var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == parts[0]);
                var t = root != null ? root.transform : null;
                for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
                var filter = t != null ? t.GetComponent<MeshFilter>() : null;
                if (filter == null || filter.sharedMesh == null) { sb.AppendLine("MISSING " + path); continue; }
                var source = filter.sharedMesh;
                string baseName = source.name.EndsWith("_299", StringComparison.Ordinal) ? source.name.Substring(0, source.name.Length - 4) : source.name;
                string copyPath = CopyFolder + "/" + baseName + "_299.asset";
                var copy = AssetDatabase.LoadAssetAtPath<Mesh>(copyPath);
                int moved = 0, missed = 0; float before = 0, after = 0;
                if (copy == null)
                {
                    copy = Object.Instantiate(source); copy.name = baseName + "_299";
                    var v = copy.vertices;
                    for (int i = 0; i < v.Length; i++)
                    {
                        var w = filter.transform.TransformPoint(v[i]);
                        float y = TerrainY(w, hits);
                        if (float.IsNaN(y)) { missed++; continue; }
                        before = Mathf.Max(before, Mathf.Abs(w.y - (y + RibbonOffset)));
                        w.y = y + RibbonOffset; v[i] = filter.transform.InverseTransformPoint(w); moved++;
                    }
                    copy.vertices = v; copy.RecalculateNormals(); copy.RecalculateBounds();
                    AssetDatabase.CreateAsset(copy, copyPath);
                    sb.Append("NEW ");
                }
                else sb.Append("REUSE ");
                filter.sharedMesh = copy;
                var cv = copy.vertices;
                for (int i = 0; i < cv.Length; i++) { var w = filter.transform.TransformPoint(cv[i]); float y = TerrainY(w, hits); if (!float.IsNaN(y)) after = Mathf.Max(after, Mathf.Abs(w.y - (y + RibbonOffset))); }
                EditorUtility.SetDirty(filter);
                sb.AppendLine($"{path}: {source.name} -> {copyPath}; vertices {cv.Length} (moved {moved}, no terrain {missed}); max |gap-offset| before {before:F2} m, after {after:F3} m");
            }
            AssetDatabase.SaveAssets();
            if (save && scene.isDirty)
            {
                string backup = Path.Combine(Root, "SceneBackup");
                Directory.CreateDirectory(backup);
                string file = Path.GetFullPath(scene.path), copyFile = Path.Combine(backup, Path.GetFileNameWithoutExtension(scene.path) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
                File.Copy(file, copyFile, false);
                if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene)) throw new Exception("save failed " + scene.path);
                sb.AppendLine("saved " + scene.path + " (backup " + copyFile + ")");
            }
            return sb.ToString();
        }

        // road-soften:<class>:<path>[|<path>...][:save] — class town (paved, inside walls) | country (outside walls).
        // Road ribbons (rows of 5 vertices across, CompactRebuildVillage245.Ribbon layout) are rebuilt as a denser strip that
        // widens past the paving by a skirt and carries vertex alpha: 1 in the core, a noisy fall-off to 0 at the outer edge
        // and along both ends. Draped on the terrain +3.5 cm. Material: a copy of the road material with _EdgeFade=1 (the
        // ground shader's own dithered vertex-alpha edge), so the ink wash reads it as a soft, irregular boundary. Mesh and
        // material copies live in Finish297/GroundFit299/Soft; the shared originals are untouched.
        sealed class RoadClass { public float skirt, inner, noise, ends, spacing; public Vector4 clump; }
        static readonly Dictionary<string, RoadClass> Classes = new Dictionary<string, RoadClass>
        {
            ["town"] = new RoadClass { skirt = .35f, inner = .2f, noise = .25f, ends = .5f, spacing = .6f, clump = new Vector4(.08f, .35f, .3f, 1.6f) },
            ["country"] = new RoadClass { skirt = .9f, inner = .5f, noise = .8f, ends = 1.6f, spacing = .6f, clump = new Vector4(.16f, .8f, .45f, 1.8f) },
        };
        static readonly float[] Across = { 0f, .22f, .42f, .58f, .70f, .79f, .86f, .91f, .95f, .98f, 1f };

        static float Hash01(int i, int j)
        {
            uint h = unchecked((uint)(i * 374761393 + j * 668265263));
            h = unchecked((h ^ (h >> 13)) * 1274126177u);
            return (h ^ (h >> 16)) / 4294967295f;
        }
        static float ValueNoise(float x, float z)
        {
            int xi = Mathf.FloorToInt(x), zi = Mathf.FloorToInt(z); float fx = x - xi, fz = z - zi;
            fx = fx * fx * (3 - 2 * fx); fz = fz * fz * (3 - 2 * fz);
            return Mathf.Lerp(Mathf.Lerp(Hash01(xi, zi), Hash01(xi + 1, zi), fx), Mathf.Lerp(Hash01(xi, zi + 1), Hash01(xi + 1, zi + 1), fx), fz);
        }
        static float EdgeNoise(float x, float z) => .55f * ValueNoise(x / 1.1f, z / 1.1f) + .30f * ValueNoise(x / 3.3f + 17f, z / 3.3f - 5f) + .15f * ValueNoise(x / .45f - 3f, z / .45f + 9f);

        static Transform FindPath(Scene scene, string path)
        {
            var parts = path.Split('/');
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == parts[0]);
            var tr = root != null ? root.transform : null;
            for (int i = 1; i < parts.Length && tr != null; i++) tr = tr.Find(parts[i]);
            return tr;
        }

        static void SaveWithBackup(Scene scene, StringBuilder sb)
        {
            string backup = Path.Combine(Root, "SceneBackup");
            Directory.CreateDirectory(backup);
            string file = Path.GetFullPath(scene.path), copyFile = Path.Combine(backup, Path.GetFileNameWithoutExtension(scene.path) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
            File.Copy(file, copyFile, false);
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene)) throw new Exception("save failed " + scene.path);
            sb.AppendLine("saved " + scene.path + " (backup " + copyFile + ")");
        }

        static Mesh SoftRibbon(MeshFilter filter, RoadClass rc, string name, RaycastHit[] hits, out string note, bool fadeStart = true)
        {
            note = null;
            var src = filter.sharedMesh; var v = src.vertices; var uv = src.uv;
            if (v.Length < 10 || v.Length % 5 != 0) { note = "not a 5-wide ribbon"; return null; }
            int rows = v.Length / 5;
            var W = new Vector3[v.Length]; for (int i = 0; i < v.Length; i++) W[i] = filter.transform.TransformPoint(v[i]);
            float worst = 0;
            for (int r = 0; r < rows; r++)
            {
                Vector3 L = W[r * 5], R = W[r * 5 + 4];
                var lr = new Vector2(R.x - L.x, R.z - L.z);
                for (int k = 1; k < 4; k++) { var q = W[r * 5 + k]; var lq = new Vector2(q.x - L.x, q.z - L.z); worst = Mathf.Max(worst, Mathf.Abs(lr.x * lq.y - lr.y * lq.x) / Mathf.Max(1e-4f, lr.magnitude)); }
            }
            if (worst > .1f) { note = $"rows are not straight across ({worst:F2} m)"; return null; }
            var centre = new Vector3[rows]; var across = new Vector3[rows]; var half = new float[rows]; var along = new float[rows]; var vcoord = new float[rows];
            for (int r = 0; r < rows; r++)
            {
                Vector3 L = W[r * 5], R = W[r * 5 + 4];
                centre[r] = (L + R) * .5f; var d = R - L; d.y = 0; half[r] = d.magnitude * .5f; across[r] = d.normalized;
                vcoord[r] = uv.Length == v.Length ? uv[r * 5 + 2].y : 0f;
                along[r] = r == 0 ? 0 : along[r - 1] + Vector2.Distance(new Vector2(centre[r].x, centre[r].z), new Vector2(centre[r - 1].x, centre[r - 1].z));
            }
            float length = along[rows - 1];
            int samples = Mathf.Max(2, Mathf.CeilToInt(length / rc.spacing) + 1);
            int n = Across.Length * 2 - 1;
            var nv = new List<Vector3>(); var nuv = new List<Vector2>(); var ncol = new List<Color>(); var tris = new List<int>();
            for (int s = 0; s < samples; s++)
            {
                float dist = length * s / (samples - 1);
                int r = 0; while (r < rows - 2 && along[r + 1] < dist) r++;
                float f = Mathf.Clamp01((dist - along[r]) / Mathf.Max(1e-4f, along[r + 1] - along[r]));
                var c = Vector3.Lerp(centre[r], centre[r + 1], f); float h = Mathf.Lerp(half[r], half[r + 1], f);
                var a = Vector3.Slerp(across[r], across[r + 1], f).normalized; float vc = Mathf.Lerp(vcoord[r], vcoord[r + 1], f);
                float endFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((fadeStart ? Mathf.Min(dist, length - dist) : length - dist) / rc.ends));
                for (int j = 0; j < n; j++)
                {
                    float tj = j < Across.Length ? -Across[Across.Length - 1 - j] : Across[j - Across.Length + 1];
                    float u = tj * (h + rc.skirt);
                    var p = c + a * u;
                    // the outer skirt wanders so the silhouette is never a parallel offset of the paving
                    float jitter = (EdgeNoise(p.x * .7f + 31f, p.z * .7f - 11f) - .5f) * rc.skirt * .6f * Mathf.Abs(tj);
                    p += a * jitter * (tj < 0 ? -1f : 1f);
                    float y = TerrainY(p, hits);
                    if (!float.IsNaN(y)) p.y = y + RibbonOffset;
                    float e = (h + rc.skirt) - Mathf.Abs(u);              // metres inward from the outer skirt edge
                    float noisy = e + (EdgeNoise(p.x, p.z) - .5f) * rc.noise * 2f;
                    float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(noisy / (rc.skirt + rc.inner))) * endFade;
                    if (Mathf.Abs(tj) >= .999f) alpha = 0f;
                    nv.Add(filter.transform.InverseTransformPoint(p)); nuv.Add(new Vector2(u + h, vc)); ncol.Add(new Color(1, 1, 1, alpha));
                }
                if (s > 0) for (int j = 0; j < n - 1; j++) { int i0 = (s - 1) * n + j, i1 = s * n + j; tris.AddRange(new[] { i0, i1, i1 + 1, i0, i1 + 1, i0 + 1 }); }
            }
            var mesh = new Mesh { name = name };
            if (nv.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(nv); mesh.SetUVs(0, nuv); mesh.SetColors(ncol); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            var mn = mesh.normals; float upSum = 0; for (int i = 0; i < mn.Length; i++) upSum += mn[i].y;
            if (upSum < 0) { for (int i = 0; i < tris.Count; i += 3) { int tmp = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = tmp; } mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); }
            mesh.RecalculateBounds();
            note = $"NEW rows {rows} -> {samples} x {n}, length {length:F1} m, half-width up to {half.Max():F2} m";
            return mesh;
        }

        static string Soften(string argument)
        {
            bool save = argument.EndsWith(":save", StringComparison.Ordinal);
            if (save) argument = argument.Substring(0, argument.Length - 5);
            int colon = argument.IndexOf(':');
            string spec = colon < 0 ? "" : argument.Substring(0, colon);
            bool farOnly = spec.EndsWith("/far", StringComparison.Ordinal);
            string cls = farOnly ? spec.Substring(0, spec.Length - 4) : spec;
            if (colon < 0 || !Classes.TryGetValue(cls, out var rc)) throw new ArgumentException("road-soften:<town|country>[/far]:<path>[|...][:save]");
            var scene = SceneManager.GetActiveScene();
            var hits = new RaycastHit[64];
            var sb = new StringBuilder();
            Physics.SyncTransforms();
            string folder = CopyFolder + "/Soft";
            if (!AssetDatabase.IsValidFolder(folder)) { Directory.CreateDirectory(Path.GetFullPath(folder)); AssetDatabase.Refresh(); }
            foreach (string path in argument.Substring(colon + 1).Split('|').Where(s => s.Length > 0))
            {
                var tr = FindPath(scene, path);
                var filter = tr != null ? tr.GetComponent<MeshFilter>() : null;
                var renderer = tr != null ? tr.GetComponent<MeshRenderer>() : null;
                if (filter == null || renderer == null || filter.sharedMesh == null) { sb.AppendLine("MISSING " + path); continue; }
                string baseName = filter.sharedMesh.name;
                foreach (var suffix in new[] { "_town_soft299", "_country_soft299", "_299" }) if (baseName.EndsWith(suffix, StringComparison.Ordinal)) baseName = baseName.Substring(0, baseName.Length - suffix.Length);
                string meshPath = folder + "/" + baseName + "_" + cls + "_soft299.asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                string note = "REUSE";
                if (mesh == null)
                {
                    mesh = SoftRibbon(filter, rc, baseName + "_" + cls + "_soft299", hits, out note, !farOnly);
                    if (mesh == null) { sb.AppendLine("SKIP " + path + ": " + note); continue; }
                    AssetDatabase.CreateAsset(mesh, meshPath);
                }
                var srcMat = renderer.sharedMaterial;
                if (srcMat == null) { sb.AppendLine("SKIP " + path + ": renderer has no material (repair with material-set first)"); continue; }
                string matBase = srcMat.name.EndsWith("_soft299", StringComparison.Ordinal) ? srcMat.name.Substring(0, srcMat.name.Length - 8) : srcMat.name;
                foreach (var suffix in new[] { "_town", "_country" }) if (matBase.EndsWith(suffix, StringComparison.Ordinal)) matBase = matBase.Substring(0, matBase.Length - suffix.Length);
                string matPath = folder + "/" + matBase.Substring(Math.Max(0, matBase.Length - 40)) + "_" + cls + "_soft299.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null) { mat = new Material(srcMat) { name = Path.GetFileNameWithoutExtension(matPath) }; AssetDatabase.CreateAsset(mat, matPath); }
                if (mat.HasProperty("_EdgeFade")) mat.SetFloat("_EdgeFade", 1f);
                if (mat.HasProperty("_EdgeFadeWorld")) { mat.SetFloat("_EdgeFadeWorld", 1f); mat.SetVector("_EdgeFadeClump", rc.clump); }
                if (mat.HasProperty("_EdgeBlend"))
                {
                    // blended over the already drawn ground, still inside the opaque range so the ink wash processes it
                    mat.SetFloat("_EdgeBlend", 1f); mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); mat.SetFloat("_ZWrite", 0f);
                    mat.renderQueue = 2050;
                    foreach (var pass in new[] { "DepthOnly", "DepthNormalsOnly", "ShadowCaster" }) mat.SetShaderPassEnabled(pass, false);
                }
                EditorUtility.SetDirty(mat);
                filter.sharedMesh = mesh; renderer.sharedMaterial = mat;
                EditorUtility.SetDirty(filter); EditorUtility.SetDirty(renderer);
                sb.AppendLine($"{path} [{cls}]: {note}; mesh {meshPath}; material {matPath} (_EdgeFade {(mat.HasProperty("_EdgeFade") ? mat.GetFloat("_EdgeFade") : -1)})");
            }
            AssetDatabase.SaveAssets();
            if (save && scene.isDirty) SaveWithBackup(scene, sb);
            return sb.ToString();
        }

        // road-survey[:<shader substring>] — every enabled renderer whose material shader matches (default: ground shaders),
        // grouped by root + material + edge-fade state, with mesh colour channel presence and total XZ footprint
        static string Survey(string shaderNeedle)
        {
            if (shaderNeedle.Length == 0) shaderNeedle = "Ground";
            var scene = SceneManager.GetActiveScene();
            var groups = new Dictionary<string, (int count, float area, int colored, string example)>();
            foreach (var r in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(false)))
            {
                if (!r.enabled) continue;
                var f = r.GetComponent<MeshFilter>(); var mesh = f != null ? f.sharedMesh : null;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null || m.shader.name.IndexOf(shaderNeedle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string path = PathOf(r.transform); string root = path.Split('/')[0];
                    if (root == "Reworld292_Terrain") root += " (terrain tiles)";
                    float fade = m.HasProperty("_EdgeFade") ? m.GetFloat("_EdgeFade") : -1f;
                    string key = $"{root} | {m.name} | {m.shader.name} | edgeFade={fade}";
                    groups.TryGetValue(key, out var g);
                    bool colored = mesh != null && mesh.isReadable && mesh.colors32 != null && mesh.colors32.Length > 0;
                    groups[key] = (g.count + 1, g.area + r.bounds.size.x * r.bounds.size.z, g.colored + (colored ? 1 : 0), g.example ?? path);
                }
            }
            var sb = new StringBuilder();
            foreach (var kv in groups.OrderByDescending(k => k.Value.count))
                sb.AppendLine($"{kv.Value.count,5} x  area {kv.Value.area,9:F0} m2  colors {kv.Value.colored,4}  {kv.Key}  e.g. {kv.Value.example}");
            return sb.ToString();
        }

        static string Audit(string prefix)
        {
            var scene = SceneManager.GetActiveScene();
            var report = new Report { scene = scene.path, prefix = prefix, created = DateTime.Now.ToString("s", CultureInfo.InvariantCulture) };
            var hits = new RaycastHit[64];
            Physics.SyncTransforms();
            foreach (var filter in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshFilter>(false)))
            {
                var mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (mesh == null || renderer == null || !renderer.enabled || !mesh.isReadable) continue;
                string path = PathOf(filter.transform);
                if (prefix.Length > 0 && !path.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (path.StartsWith("Reworld292_Terrain", StringComparison.Ordinal) || path.Contains("Water") || path.Contains("River294") || path.Contains("Macro_CombatPlayerRig")) continue;
                report.scanned++;
                var b = renderer.bounds;
                if (Mathf.Max(b.size.x, b.size.z) < 2f || mesh.vertexCount < 3) continue;
                string low = (filter.name + " " + mesh.name).ToLowerInvariant();
                if (NotGround.Any(w => low.Contains(w))) continue;
                // area-weighted upward share of the surface: road/yard ribbons and pads are ~1, walls and trees are not
                var v = mesh.vertices; var tri = mesh.triangles; var m = filter.transform.localToWorldMatrix;
                double up = 0, area = 0;
                for (int i = 0; i + 2 < tri.Length; i += 3)
                {
                    var n = Vector3.Cross(m.MultiplyPoint3x4(v[tri[i + 1]]) - m.MultiplyPoint3x4(v[tri[i]]), m.MultiplyPoint3x4(v[tri[i + 2]]) - m.MultiplyPoint3x4(v[tri[i]]));
                    float a = n.magnitude; if (a < 1e-6f) continue;
                    up += Mathf.Abs(n.y); area += a;
                }
                float normalUp = area > 0 ? (float)(up / area) : 0f;
                bool named = GroundWords.Any(w => filter.name.ToLowerInvariant().Contains(w) || mesh.name.ToLowerInvariant().Contains(w));
                if (normalUp < .85f && !named) continue;
                var own = new HashSet<Collider>(filter.GetComponents<Collider>());
                var row = new Row { path = path, mesh = mesh.name, root = path.Split('/')[0], hasCollider = own.Count > 0, kind = own.Count > 0 ? "platform" : "ribbon", namedGround = named, normalUp = normalUp, center = b.center, size = b.size };
                int stride = Mathf.Max(1, v.Length / SamplesPerMesh);
                double absSum = 0;
                for (int i = 0; i < v.Length; i += stride)
                {
                    var w = m.MultiplyPoint3x4(v[i]);
                    float g = Under(w, own, hits);
                    if (float.IsNaN(g)) { row.noGround++; continue; }
                    float gap = w.y - g; row.samples++; absSum += Mathf.Abs(gap);
                    if (gap > Tolerance) { row.hover++; if (gap > row.maxHover) { row.maxHover = gap; row.worstHover = w; } }
                    else if (gap < -Tolerance) { row.sink++; if (-gap > row.maxSink) { row.maxSink = -gap; row.worstSink = w; } }
                    else row.near++;
                }
                if (row.samples == 0) continue;
                row.meanAbs = (float)(absSum / row.samples);
                if (!named && row.near < row.samples * NearShare) continue;   // raised floors, roofs: not ground-level geometry
                report.groundLike++;
                // a visible defect: a ribbon hovering (a sunk ribbon is mostly hidden) or a platform overhanging its ground
                row.defect = row.kind == "ribbon" ? row.hover > row.samples * .05f || row.maxHover > .35f
                                                  : row.hover > row.samples * .10f && row.maxHover > .6f;
                if (row.defect) report.defects++;
                report.rows.Add(row);
            }
            report.rows = report.rows.OrderByDescending(r => Mathf.Max(r.maxHover, r.maxSink)).ToList();
            Directory.CreateDirectory(Root);
            string file = Path.Combine(Root, "audit-" + Path.GetFileNameWithoutExtension(scene.path) + (prefix.Length > 0 ? "-" + prefix.Replace('/', '_') : "") + ".json");
            File.WriteAllText(file, JsonUtility.ToJson(report, true));
            var sb = new StringBuilder();
            sb.AppendLine($"{scene.name}: scanned {report.scanned}, ground-like {report.groundLike}, defects {report.defects} (|gap| > {Tolerance} m) -> {file}");
            foreach (string kind in new[] { "ribbon", "platform" })
            {
                var bad = report.rows.Where(r => r.defect && r.kind == kind).ToList();
                sb.AppendLine($"[{kind}] {bad.Count} defective of {report.rows.Count(r => r.kind == kind)}");
                foreach (var g in bad.GroupBy(r => r.root).OrderByDescending(g => g.Count())) sb.AppendLine($"  root {g.Key}: {g.Count()}");
                foreach (var r in bad.Take(kind == "ribbon" ? 30 : 15))
                    sb.AppendLine($"  hover {r.hover}/{r.samples} max {r.maxHover:F2}  sink {r.sink} max {r.maxSink:F2}  {r.path}  @({r.center.x:F0},{r.center.y:F1},{r.center.z:F0}) size ({r.size.x:F0}x{r.size.z:F0})");
            }
            return sb.ToString();
        }
    }
}
