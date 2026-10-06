using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.App.World.Dressing;
using Oheangbu.Data.World;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-1 building audit (SPEC-WORLD-BUILDING-AUDIT-308 §A). Read only: it never changes or saves a scene or an asset,
    // creates no GameObject and opens no dialog; refusals come back as "refused: …". Queue entry Run(string):
    //   scan[:scene=<architecture296|folklore298|main>][:roots=a,b][:checks=C1,C2]
    //        Edit mode, every open scene clean. scene= opens that scene (Single) and goes back to the previous one afterwards.
    //        -> <repo>/Art/World/Compact/Rebuild/BuildingAudit308/<alias>/<utc>/audit.json, findings.csv, summary.txt
    //   shots[:budget=300][:scene=<alias>]   shot list from the newest scan + config clusters -> BuildingAudit308/Shots/shots.json
    //   wallprobe:<path>                     player CC capsule (h1.75 r.28) swept from 8 directions toward the target (CapsuleCast)
    //   status                               quality level, lodBias per level (read from ProjectSettings/QualitySettings.asset),
    //                                        scene SHA, dirty, newest scan per scene, ledger state
    // Thresholds are TEST values from BuildingAudit308/config.json (nothing is hard-coded here). Nothing is cached between
    // calls: Enter Play Mode domain reload is off, so every call builds its own state.
    public static partial class BuildingAudit308
    {
        internal static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        internal static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        internal static string Folder => Path.Combine(RepoRoot, "Art", "World", "Compact", "Rebuild", "BuildingAudit308");
        internal static string ConfigFile => Path.Combine(Folder, "config.json");
        internal static string Abs(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));

        public static string Run(string command)
        {
            command = (command ?? "").Trim();
            try
            {
                var a = command.Split(':');
                switch (a[0])
                {
                    case "scan": return Scan(a.Skip(1).ToArray());
                    case "shots": return Shots(a.Skip(1).ToArray());
                    case "wallprobe": return a.Length > 1 ? WallProbe(string.Join(":", a.Skip(1))) : "refused: wallprobe:<scene path of the target>";
                    case "status": return Status();
                    default: return "refused: BuildingAudit308 scan[:scene=<architecture296|folklore298|main>][:roots=a,b][:checks=C1,…] | shots[:budget=300][:scene=<alias>] | wallprobe:<path> | status";
                }
            }
            catch (Exception e) { return "FAILED: " + e; }
        }

        // ------------------------------------------------------------------ config (JsonUtility shape; TEST values live in config.json)

        [Serializable] internal sealed class SceneAlias308 { public string alias = "", path = ""; }
        [Serializable] internal sealed class Root308 { public string name = ""; public int depth = 1; public string[] checks = Array.Empty<string>(); public bool readOnly; }
        [Serializable] internal sealed class Thresholds308
        {
            public float minBuildingSize, minBuildingHeight;
            public float c0SiteRadius;
            public float c2HoverFail, c2HoverWarn, c2HoverShareFail, c2SkirtRadius, c2ThresholdRise, c2BuriedDepth, c2BuriedShareFail, c2TerrainWindow, c2AreaStep, c2PlateInset, c2DoorOutset, c2PlateMinSize, c2PlateBottomBand;
            public int c2PerimeterPoints;
            public float c3BaseLift, c3EaveOverlap, c3GridCell;
            public float c4PosTol, c4RotTol, c4CoplanarDist, c4CoplanarArea, c4CoplanarNormalTol;
            public int c4CoplanarMaxTriangles;
            public float c5RoofStep, c5RoofMinHeight, c5InteriorEye, c5InteriorMinElevation, c5InteriorReach, c5Lod1Seam, c5Lod2Seam, c5SeamTouch, c5SeamRegion;
            public int c5InteriorRays, c5RoofNeighbours, c5SeamMaxPairs;
            public float c6SizeSlack, c6CentreTol, c6Fov, c6ScreenHeight;
            public float c7WallMin, c7WallMax, c7ProbeRadius, c7CorridorDistance, c7InvisibleVolume, c7InvisibleRadius, c7NoColliderShare, c7MinRendererSize, c7MinTriangleArea;
            public int c7SamplesPerRenderer, c7MinSamples;
            public float c8FloatHeight;
        }
        [Serializable] internal sealed class Tokens308
        {
            public string[] plate = Array.Empty<string>(), plateExclude = Array.Empty<string>(), door = Array.Empty<string>(), roof = Array.Empty<string>(),
                skirt = Array.Empty<string>(), test = Array.Empty<string>(), building = Array.Empty<string>(), oneSided = Array.Empty<string>();
        }
        [Serializable] internal sealed class Interior308 { public string id = ""; public float[] floor = Array.Empty<float>(); }
        [Serializable] internal sealed class Interiors308 { public bool fromArchitectureSheet; public string architectureSheet = ""; public Interior308[] extra = Array.Empty<Interior308>(); }
        [Serializable] internal sealed class Cluster308 { public string name = ""; public float[] target = Array.Empty<float>(); public float[] eye = Array.Empty<float>(); public int priority = 2; public float distance, height; public string note = ""; }
        [Serializable] internal sealed class Shots308
        {
            public int budget, w, h; public float fov, eyeHeight, eyeDistanceMin, eyeDistanceMax, obliqueDistance, obliqueHeight, coverRadius, clusterMerge;
            public string[] shotChecks = Array.Empty<string>(); public string camerasFile = ""; public int camerasPriority = 2;
            public Cluster308[] clusters = Array.Empty<Cluster308>();
        }
        [Serializable] internal sealed class Site308 { public string name = ""; public float[] centre = Array.Empty<float>(); }
        [Serializable] internal sealed class Corridors308 { public string routesFile = ""; public bool contentPaths; public float defaultWidth; }
        [Serializable] internal sealed class Config308
        {
            public string version = "", status = "";
            public SceneAlias308[] scenes = Array.Empty<SceneAlias308>();
            public Root308[] roots = Array.Empty<Root308>();
            public string[] excludedRoots = Array.Empty<string>(), protectedRootPrefixes = Array.Empty<string>(), protectedAssetFolders = Array.Empty<string>(), protectedAssets = Array.Empty<string>();
            public string[] buildingRoots = Array.Empty<string>(), sheets = Array.Empty<string>(), emissionAllow = Array.Empty<string>(), emissionExcludeRoots = Array.Empty<string>();
            public string attractionRoot = "";
            public Thresholds308 thresholds = new Thresholds308();
            public Tokens308 tokens = new Tokens308();
            public Interiors308 interiors = new Interiors308();
            public Shots308 shots = new Shots308();
            public Site308[] repairSites = Array.Empty<Site308>();
            public Corridors308 corridors = new Corridors308();
            public BuildingFix308.Fix308 fix = new BuildingFix308.Fix308();

            public string ScenePath(string alias) => scenes.FirstOrDefault(s => s.alias == alias)?.path;
            public string AliasOf(string path) => scenes.FirstOrDefault(s => string.Equals(s.path, path, StringComparison.OrdinalIgnoreCase))?.alias;
            public bool ProtectedRoot(string rootName) => protectedRootPrefixes.Any(p => rootName.StartsWith(p, StringComparison.Ordinal));
            public bool ProtectedAsset(string assetPath)
            {
                string p = (assetPath ?? "").Replace('\\', '/');
                return protectedAssets.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)) || protectedAssetFolders.Any(f => p.StartsWith(f.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
            }
        }

        internal static Config308 LoadConfig(out string error)
        {
            error = null;
            if (!File.Exists(ConfigFile)) { error = "config missing: " + ConfigFile; return null; }
            try
            {
                var c = JsonUtility.FromJson<Config308>(File.ReadAllText(ConfigFile).TrimStart('﻿'));
                if (c == null || c.scenes.Length == 0 || c.roots.Length == 0 || c.thresholds == null) { error = "config incomplete (scenes/roots/thresholds): " + ConfigFile; return null; }
                return c;
            }
            catch (Exception e) { error = "config unreadable: " + e.Message; return null; }
        }

        // ------------------------------------------------------------------ small shared helpers (also used by BuildingFix308)

        internal static string Utc() => DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", Inv);
        internal static string V(Vector3 v) => string.Format(Inv, "{0:F2},{1:F2},{2:F2}", v.x, v.y, v.z);
        internal static string F(float v, string f = "F3") => float.IsNaN(v) ? "NaN" : v.ToString(f, Inv);
        internal static Vector3 Vec(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        internal static float[] Arr(Vector3 v) => new[] { v.x, v.y, v.z };
        internal static bool HasAny(string text, string[] tokens) => !string.IsNullOrEmpty(text) && tokens != null && tokens.Any(t => t.Length > 0 && text.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);

        internal static string Sha(string file)
        {
            if (!File.Exists(file)) return "";
            using (var s = File.OpenRead(file)) using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }
        internal static string ShaText(string text)
        {
            using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        internal static string PathOf(Transform t)
        {
            if (t == null) return "";
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        // ledger key: the hierarchy path with "#k" on a segment whose parent holds more than one child of that name (k = order among
        // the same-named siblings), so overlapping same-name siblings (F3) and the four porch lanterns (F5) stay distinguishable
        internal static string KeyOf(Transform t)
        {
            if (t == null) return "";
            var parts = new List<string>();
            for (var x = t; x != null; x = x.parent)
            {
                string seg = x.name;
                if (x.parent != null)
                {
                    int same = 0, order = 0;
                    for (int i = 0; i < x.parent.childCount; i++)
                    {
                        var c = x.parent.GetChild(i);
                        if (c.name != x.name) continue;
                        if (c == x) order = same;
                        same++;
                    }
                    if (same > 1) seg += "#" + order;
                }
                else
                {
                    var roots = x.gameObject.scene.GetRootGameObjects().Where(g => g.name == x.name).ToArray();
                    if (roots.Length > 1) seg += "#" + Array.IndexOf(roots, x.gameObject);
                }
                parts.Add(seg);
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        internal static Transform Resolve(Scene scene, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var segs = key.Split('/');
            (string name, int order) Seg(string s) { int h = s.LastIndexOf('#'); return h > 0 && int.TryParse(s.Substring(h + 1), NumberStyles.Integer, Inv, out int k) ? (s.Substring(0, h), k) : (s, -1); }
            var (rn, ro) = Seg(segs[0]);
            var roots = scene.GetRootGameObjects().Where(g => g.name == rn).ToArray();
            if (roots.Length == 0) return null;
            var t = roots[ro >= 0 && ro < roots.Length ? ro : 0].transform;
            for (int i = 1; i < segs.Length && t != null; i++)
            {
                var (n, o) = Seg(segs[i]);
                Transform found = null; int seen = 0;
                for (int c = 0; c < t.childCount; c++)
                {
                    var ch = t.GetChild(c);
                    if (ch.name != n) continue;
                    if (o < 0 || seen == o) { found = ch; break; }
                    seen++;
                }
                t = found;
            }
            return t;
        }

        internal static bool TerrainLike(Collider c)
        {
            if (c == null) return false;
            if (c is TerrainCollider) return true;
            for (var t = c.transform; t != null; t = t.parent) if (t.name.Contains("Terrain")) return true;
            return false;
        }

        // highest terrain surface within [y - window, y + window] under xz (NaN when none)
        internal static float TerrainNear(Vector3 p, float window)
        {
            float best = float.NaN;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, p.y + window, p.z), Vector3.down, window * 2f, ~0, QueryTriggerInteraction.Ignore))
                if (TerrainLike(h.collider) && (float.IsNaN(best) || h.point.y > best)) best = h.point.y;
            return best;
        }

        // terrain top from far above (any depth of burial)
        internal static float TerrainTop(Vector3 p)
        {
            float best = float.NaN;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, 3000f, p.z), Vector3.down, 6000f, ~0, QueryTriggerInteraction.Ignore))
                if (TerrainLike(h.collider) && (float.IsNaN(best) || h.point.y > best)) best = h.point.y;
            return best;
        }

        internal static Scene? FindScene(string path)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s.path == path) return s; }
            return null;
        }

        internal static string DirtyOpenScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s.isDirty) return s.path; }
            return null;
        }

        internal static string QualityName() { int q = QualitySettings.GetQualityLevel(); var n = QualitySettings.names; return q >= 0 && q < n.Length ? n[q] : q.ToString(Inv); }

        // lodBias per quality level, read from the QualitySettings asset text (no level switch)
        internal static List<(string name, float lodBias)> QualityLevels()
        {
            var list = new List<(string, float)>();
            string file = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ProjectSettings", "QualitySettings.asset"));
            if (!File.Exists(file)) return list;
            string name = null;
            foreach (var raw in File.ReadLines(file))
            {
                string line = raw.Trim();
                if (line.StartsWith("name: ", StringComparison.Ordinal)) name = line.Substring(6).Trim();
                else if (line.StartsWith("lodBias: ", StringComparison.Ordinal) && name != null && float.TryParse(line.Substring(9), NumberStyles.Float, Inv, out float b)) { list.Add((name, b)); name = null; }
            }
            return list;
        }

        internal static IEnumerable<WorldMacroDressingSheetSO> SceneSheets(Scene scene, Config308 cfg)
        {
            var roots = scene.GetRootGameObjects();
            var all = roots.SelectMany(g => g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).Where(r => r.isActiveAndEnabled).Select(r => r.Sheet)
                .Concat(roots.SelectMany(g => g.GetComponentsInChildren<WorldMacroDressingRenderer>(true)).Where(r => r.isActiveAndEnabled).Select(r => r.Sheet))
                .Where(s => s != null).Distinct();
            return all.Where(s => cfg.sheets.Length == 0 || cfg.sheets.Any(n => s.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0 || AssetDatabase.GetAssetPath(s).IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        // ------------------------------------------------------------------ CPU triangles (ray-triangle for C3/C5/C7, no colliders needed)

        // one mesh's triangles; non-readable meshes come through Mesh.AcquireReadOnlyMeshData. Null when it cannot be read.
        internal sealed class MeshTris308 { public Vector3[] v; public int[] t; public int[] sub; }

        internal sealed class MeshCache308
        {
            readonly Dictionary<Mesh, MeshTris308> map = new Dictionary<Mesh, MeshTris308>();
            public int Failed;
            public MeshTris308 Get(Mesh m)
            {
                if (m == null) return null;
                if (map.TryGetValue(m, out var hit)) return hit;
                MeshTris308 r = null;
                try { r = Read(m); } catch (Exception) { r = null; }
                if (r == null) Failed++;
                map[m] = r;
                return r;
            }
            static MeshTris308 Read(Mesh m)
            {
                var tris = new List<int>(); var subs = new List<int>();
                if (m.isReadable)
                {
                    var v = m.vertices; var tmp = new List<int>();
                    for (int s = 0; s < m.subMeshCount; s++)
                    {
                        if (m.GetTopology(s) != MeshTopology.Triangles) continue;
                        tmp.Clear(); m.GetTriangles(tmp, s); tris.AddRange(tmp);
                        for (int i = 0; i < tmp.Count / 3; i++) subs.Add(s);
                    }
                    return new MeshTris308 { v = v, t = tris.ToArray(), sub = subs.ToArray() };
                }
                using (var data = Mesh.AcquireReadOnlyMeshData(m))
                {
                    if (data.Length == 0) return null;
                    var md = data[0];
                    var verts = new NativeArray<Vector3>(md.vertexCount, Allocator.Temp);
                    try
                    {
                        md.GetVertices(verts);
                        var v = verts.ToArray();
                        for (int s = 0; s < md.subMeshCount; s++)
                        {
                            var d = md.GetSubMesh(s);
                            if (d.topology != MeshTopology.Triangles || d.indexCount == 0) continue;
                            var idx = new NativeArray<int>(d.indexCount, Allocator.Temp);
                            try { md.GetIndices(idx, s, true); tris.AddRange(idx); for (int i = 0; i < d.indexCount / 3; i++) subs.Add(s); }
                            finally { idx.Dispose(); }
                        }
                        return new MeshTris308 { v = v, t = tris.ToArray(), sub = subs.ToArray() };
                    }
                    finally { verts.Dispose(); }
                }
            }
        }

        internal static Mesh MeshOf(Renderer r)
        {
            if (r is SkinnedMeshRenderer s) return s.sharedMesh;
            var f = r != null ? r.GetComponent<MeshFilter>() : null;
            return f != null ? f.sharedMesh : null;
        }

        // world-space triangle soup with an XZ bucket grid for vertical rays; a tag per triangle (index into Owners) and a flag
        internal sealed class Soup308
        {
            public readonly List<Vector3> A = new List<Vector3>(), B = new List<Vector3>(), C = new List<Vector3>();
            public readonly List<int> Tag = new List<int>();
            public readonly List<bool> Flag = new List<bool>();
            public readonly List<Renderer> Owners = new List<Renderer>();
            public int Skipped;
            Dictionary<long, List<int>> grid; float cell;
            public Bounds Bounds; bool any;

            public void Add(Renderer r, MeshCache308 cache, Func<Renderer, int, bool> flag = null)
            {
                var mesh = MeshOf(r);
                if (mesh == null) return;   // line/trail/sprite renderers and null meshes (C1 reports those) carry no faces
                var data = cache.Get(mesh);
                if (data == null) { Skipped++; return; }
                int tag = Owners.Count; Owners.Add(r);
                var m = r.localToWorldMatrix;
                var w = new Vector3[data.v.Length];
                for (int i = 0; i < w.Length; i++) w[i] = m.MultiplyPoint3x4(data.v[i]);
                for (int i = 0, k = 0; i + 2 < data.t.Length; i += 3, k++)
                {
                    Vector3 a = w[data.t[i]], b = w[data.t[i + 1]], c = w[data.t[i + 2]];
                    A.Add(a); B.Add(b); C.Add(c); Tag.Add(tag); Flag.Add(flag != null && flag(r, k < data.sub.Length ? data.sub[k] : 0));
                    if (!any) { Bounds = new Bounds(a, Vector3.zero); any = true; }
                    Bounds.Encapsulate(a); Bounds.Encapsulate(b); Bounds.Encapsulate(c);
                }
                grid = null;
            }
            public int Count => A.Count;
            static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            void Build(float size)
            {
                cell = size; grid = new Dictionary<long, List<int>>();
                for (int i = 0; i < A.Count; i++)
                {
                    float x0 = Mathf.Min(A[i].x, Mathf.Min(B[i].x, C[i].x)), x1 = Mathf.Max(A[i].x, Mathf.Max(B[i].x, C[i].x));
                    float z0 = Mathf.Min(A[i].z, Mathf.Min(B[i].z, C[i].z)), z1 = Mathf.Max(A[i].z, Mathf.Max(B[i].z, C[i].z));
                    for (int gx = Mathf.FloorToInt(x0 / cell); gx <= Mathf.FloorToInt(x1 / cell); gx++)
                        for (int gz = Mathf.FloorToInt(z0 / cell); gz <= Mathf.FloorToInt(z1 / cell); gz++)
                        {
                            long k = Key(gx, gz);
                            if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                            l.Add(i);
                        }
                }
            }
            // every crossing of the vertical line at (x, z) with y in [y0, y1]; onlyFlagged limits to flagged triangles
            public void Vertical(float x, float z, float y0, float y1, List<(float y, int tri)> hits, bool onlyFlagged = false)
            {
                hits.Clear();
                if (grid == null) Build(1f);
                if (!grid.TryGetValue(Key(Mathf.FloorToInt(x / cell), Mathf.FloorToInt(z / cell)), out var list)) return;
                foreach (int i in list)
                {
                    if (onlyFlagged && !Flag[i]) continue;
                    if (VerticalHit(A[i], B[i], C[i], x, z, out float y) && y >= y0 && y <= y1) hits.Add((y, i));
                }
            }
            // general ray (brute force with a per-triangle box early-out); few rays only
            public bool Ray(Vector3 o, Vector3 d, float max, out float t, out int tri)
            {
                t = max; tri = -1;
                for (int i = 0; i < A.Count; i++)
                    if (RayTri(o, d, A[i], B[i], C[i], out float h) && h > 1e-4f && h < t) { t = h; tri = i; }
                return tri >= 0;
            }
        }

        // barycentric test in XZ; y on the triangle plane
        internal static bool VerticalHit(Vector3 a, Vector3 b, Vector3 c, float x, float z, out float y)
        {
            y = 0;
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(d) < 1e-9f) return false;
            float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
            float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
            float l3 = 1f - l1 - l2;
            if (l1 < -1e-5f || l2 < -1e-5f || l3 < -1e-5f) return false;
            y = l1 * a.y + l2 * b.y + l3 * c.y;
            return true;
        }

        // Möller–Trumbore, both faces
        internal static bool RayTri(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0; var e1 = b - a; var e2 = c - a; var p = Vector3.Cross(d, e2); float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;
            float inv = 1f / det; var s = o - a; float u = Vector3.Dot(s, p) * inv; if (u < 0 || u > 1) return false;
            var q = Vector3.Cross(s, e1); float v = Vector3.Dot(d, q) * inv; if (v < 0 || u + v > 1) return false;
            t = Vector3.Dot(e2, q) * inv; return t > 0;
        }

        internal static Vector3 ClosestOnTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a; var ac = c - a; var ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap); if (d1 <= 0 && d2 <= 0) return a;
            var bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp); if (d3 >= 0 && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2; if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
            var cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp); if (d6 >= 0 && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6; if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4; if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float den = 1f / (va + vb + vc); return a + ab * (vb * den) + ac * (vc * den);
        }

        // ------------------------------------------------------------------ corridors (routes.json + content main/branch path)

        [Serializable] sealed class RouteFile308 { public Route308[] routes = Array.Empty<Route308>(); }
        [Serializable] sealed class Route308 { public string id = ""; public float width; public bool vehicle; public Vector3[] points = Array.Empty<Vector3>(); }
        internal sealed class Corridor308 { public string id; public Vector3[] pts; public float width; }

        internal static List<Corridor308> Corridors(Scene scene, Config308 cfg)
        {
            var list = new List<Corridor308>();
            string file = string.IsNullOrEmpty(cfg.corridors.routesFile) ? null : Path.Combine(RepoRoot, cfg.corridors.routesFile);
            if (file != null && File.Exists(file))
            {
                var r = JsonUtility.FromJson<RouteFile308>(File.ReadAllText(file).TrimStart('﻿'));
                foreach (var x in r.routes) if (x.points.Length >= 2) list.Add(new Corridor308 { id = x.id, pts = x.points, width = cfg.corridors.defaultWidth > 0 ? cfg.corridors.defaultWidth : x.width });
            }
            if (cfg.corridors.contentPaths)
            {
                var session = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).FirstOrDefault();
                if (session != null && session.Content != null)
                {
                    if (session.Content.MainPath.Length >= 2) list.Add(new Corridor308 { id = "content-main", pts = session.Content.MainPath, width = cfg.corridors.defaultWidth });
                    if (session.Content.BranchPath.Length >= 2) list.Add(new Corridor308 { id = "content-branch", pts = session.Content.BranchPath, width = cfg.corridors.defaultWidth });
                }
            }
            return list;
        }

        internal static float SegDist2(Vector2 p, Vector2 a, Vector2 b)
        {
            var d = b - a; float t = d.sqrMagnitude < 1e-8f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude);
            return Vector2.Distance(p, a + d * t);
        }

        // XZ distance from a point to the nearest corridor edge (centre-line distance minus half its width); +inf when none
        internal static float CorridorClearance(Vector2 p, List<Corridor308> corridors, out string id)
        {
            float best = float.PositiveInfinity; id = "";
            foreach (var c in corridors)
                for (int i = 1; i < c.pts.Length; i++)
                {
                    float d = SegDist2(p, new Vector2(c.pts[i - 1].x, c.pts[i - 1].z), new Vector2(c.pts[i].x, c.pts[i].z)) - c.width * .5f;
                    if (d < best) { best = d; id = c.id; }
                }
            return best;
        }

        // clearance of an XZ rectangle (centre, half extents, yaw) to the corridors: sampled outline
        internal static float CorridorClearance(Vector2 centre, Vector2 half, float yaw, List<Corridor308> corridors, out string id)
        {
            float best = float.PositiveInfinity; id = "";
            var q = Quaternion.Euler(0, yaw, 0);
            for (float u = -1; u <= 1.001f; u += .25f)
                for (float w = -1; w <= 1.001f; w += .25f)
                {
                    if (Mathf.Abs(u) < .99f && Mathf.Abs(w) < .99f && !(u == 0 && w == 0)) continue;
                    var o = q * new Vector3(half.x * u, 0, half.y * w);
                    float d = CorridorClearance(centre + new Vector2(o.x, o.z), corridors, out string cid);
                    if (d < best) { best = d; id = cid; }
                }
            return best;
        }

        // ------------------------------------------------------------------ status

        static string Status()
        {
            var cfg = LoadConfig(out string err);
            var sb = new StringBuilder("BuildingAudit308 status " + DateTime.UtcNow.ToString("O", Inv) + "\n");
            sb.AppendLine("config: " + (cfg != null ? cfg.version + " (" + cfg.status + ")" : err));
            sb.AppendLine("play=" + EditorApplication.isPlaying + " compiling=" + EditorApplication.isCompiling);
            sb.AppendLine("quality level now: " + QualityName() + " (index " + QualitySettings.GetQualityLevel() + ")");
            foreach (var (n, b) in QualityLevels()) sb.AppendLine("  level " + n + ": lodBias " + b.ToString(Inv) + " (QualitySettings.asset)");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                sb.AppendLine("open scene " + s.path + (s.isDirty ? " (DIRTY)" : "") + " sha " + Sha(Abs(s.path)));
            }
            if (cfg != null)
                foreach (var a in cfg.scenes)
                {
                    string dir = Path.Combine(Folder, a.alias);
                    string newest = Directory.Exists(dir) ? Directory.GetDirectories(dir).Select(Path.GetFileName).Where(n => n.EndsWith("Z", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal).LastOrDefault() : null;
                    string ledger = Path.Combine(Folder, "Fix", "ledger-" + a.alias + ".json");
                    sb.AppendLine("scene " + a.alias + " " + a.path + " sha " + Sha(Abs(a.path)) + " | newest scan " + (newest ?? "none") + " | ledger " + (File.Exists(ledger) ? "present" : "none"));
                }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ scan orchestration

        static string Opt(string[] a, string key) { var s = a.FirstOrDefault(x => x.StartsWith(key + "=", StringComparison.Ordinal)); return s?.Substring(key.Length + 1); }

        static string Scan(string[] args)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only (Play is running)";
            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "refused: scripts are compiling or failed to compile";
            var cfg = LoadConfig(out string err);
            if (cfg == null) return "refused: " + err;
            string dirty = DirtyOpenScene();
            if (dirty != null) return "refused: " + dirty + " has unsaved changes";
            string alias = Opt(args, "scene");
            var onlyRoots = (Opt(args, "roots") ?? "").Split(',').Where(s => s.Length > 0).ToArray();
            var onlyChecks = (Opt(args, "checks") ?? "").Split(',').Where(s => s.Length > 0).ToArray();
            var active = SceneManager.GetActiveScene();
            string previous = active.path;
            var scene = active;
            bool opened = false;
            if (!string.IsNullOrEmpty(alias))
            {
                string path = cfg.ScenePath(alias);
                if (path == null) return "refused: unknown scene alias " + alias + " (" + string.Join(", ", cfg.scenes.Select(s => s.alias)) + ")";
                if (active.path != path) { scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single); opened = true; }
            }
            if (string.IsNullOrEmpty(scene.path)) return "refused: the active scene is not saved to a file (open one of " + string.Join(", ", cfg.scenes.Select(s => s.alias)) + " or pass scene=<alias>)";
            alias = cfg.AliasOf(scene.path) ?? Path.GetFileNameWithoutExtension(scene.path);
            string shaBefore = Sha(Abs(scene.path));
            var started = DateTime.UtcNow;
            Report308 report;
            string outDir;
            try
            {
                report = RunChecks(scene, cfg, onlyRoots, onlyChecks);
                report.alias = alias; report.sceneSha = shaBefore; report.command = "scan:" + string.Join(":", args);
                report.openedByScan = opened; report.dirtyAfterScan = scene.isDirty;
                string shaAfter = Sha(Abs(scene.path));
                report.sceneShaAfterScan = shaAfter;
                if (shaAfter != shaBefore) report.notes.Add("ERROR scene file changed during the scan (" + shaBefore + " -> " + shaAfter + ")");
                // AC-B1: no file under Assets may be written while the scan runs (this tool writes none; a hit means another writer)
                report.assetsWrittenDuringScan = AssetsWrittenSince(started, 50);
                if (report.assetsWrittenDuringScan.Count > 0) report.notes.Add("ERROR " + report.assetsWrittenDuringScan.Count + " file(s) under Assets written during the scan (another session or an import?): " + string.Join(", ", report.assetsWrittenDuringScan.Take(5)));
                if (scene.isDirty) report.notes.Add("scene dirty flag raised during the scan by components outside this tool (ExecuteAlways); nothing was saved");
                outDir = Path.Combine(Folder, alias, report.utc);
                Write(report, outDir);
            }
            finally
            {
                if (opened && !string.IsNullOrEmpty(previous) && previous != scene.path && File.Exists(Abs(previous)))
                    EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            }
            return Summary(report) + "\n-> " + outDir;
        }

        // files under Assets whose last write is at or after `since` (relative paths, at most `max`)
        static List<string> AssetsWrittenSince(DateTime since, int max)
        {
            var list = new List<string>();
            var root = new DirectoryInfo(Application.dataPath);
            foreach (var f in root.EnumerateFiles("*", SearchOption.AllDirectories))
                if (f.LastWriteTimeUtc >= since) { list.Add("Assets" + f.FullName.Substring(root.FullName.Length).Replace('\\', '/')); if (list.Count >= max) break; }
            return list;
        }

        // ------------------------------------------------------------------ report shapes

        [Serializable] internal sealed class Finding308
        {
            public string check = "", level = "", root = "", unit = "", path = "", detail = "", extra = "";
            public Vector3 position; public float value;
        }
        [Serializable] internal sealed class RootInfo308
        {
            public string name = "", checks = "", colliderHash = "";
            public bool present, readOnly, protectedRoot;
            public int units, buildings, renderers, colliders, staticColliders, inactiveObjects;
        }
        [Serializable] internal sealed class Count308 { public string check = ""; public int fail, warn, skipped, info, pass; public float seconds; }
        [Serializable] internal sealed class SiteHash308 { public string name = "", hash = ""; public Vector3 centre; public int colliders; }
        [Serializable] internal sealed class UnitMetric308
        {
            public string root = "", unit = "", key = "", plate = "", c2 = "";
            public bool building; public Vector3 centre, size;
            public int perimeter, hovering, warnHover, areaSamples, buried, thresholds;
            public float maxHover, hoverShare, buriedShare, maxThresholdRise, emission;
        }
        [Serializable] internal sealed class Lod308 { public string path = ""; public float size; public float[] transitions = Array.Empty<float>(), pcDistance = Array.Empty<float>(), mobileDistance = Array.Empty<float>(); }
        [Serializable] internal sealed class Report308
        {
            public string scene = "", alias = "", sceneSha = "", sceneShaAfterScan = "", utc = "", quality = "", qualityLevels = "", command = "", configVersion = "";
            public bool openedByScan, dirtyAfterScan;
            public float seconds; public int emissionCount;
            public List<RootInfo308> roots = new List<RootInfo308>();
            public List<Count308> counts = new List<Count308>();
            public List<SiteHash308> sites = new List<SiteHash308>();
            public List<UnitMetric308> units = new List<UnitMetric308>();
            public List<Lod308> lods = new List<Lod308>();
            public List<string> shaderFamilies = new List<string>();
            public List<string> unitIndex = new List<string>();   // root|unit key|B(uilding)/P(rop)|bounds centre — every scanned unit
            public List<string> assetsWrittenDuringScan = new List<string>();   // AC-B1 (must stay empty)
            public List<Finding308> findings = new List<Finding308>();
            public List<string> notes = new List<string>();
        }

        static void Write(Report308 r, string dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "audit.json"), JsonUtility.ToJson(r, true), new UTF8Encoding(false));
            var csv = new StringBuilder("check,level,root,unit,path,x,y,z,value,detail\n");
            string Q(string s) => "\"" + (s ?? "").Replace("\"", "'") + "\"";
            foreach (var c in r.counts)
                csv.Append(c.check).Append(",COUNT,,,,,,,").Append(c.fail).Append(',').Append(Q("fail=" + c.fail + ";warn=" + c.warn + ";skipped=" + c.skipped + ";info=" + c.info + ";pass=" + c.pass + ";seconds=" + F(c.seconds, "F1"))).Append('\n');
            foreach (var f in r.findings)
                csv.Append(f.check).Append(',').Append(f.level).Append(',').Append(Q(f.root)).Append(',').Append(Q(f.unit)).Append(',').Append(Q(f.path)).Append(',')
                   .Append(F(f.position.x, "F2")).Append(',').Append(F(f.position.y, "F2")).Append(',').Append(F(f.position.z, "F2")).Append(',').Append(F(f.value)).Append(',').Append(Q(f.detail)).Append('\n');
            File.WriteAllText(Path.Combine(dir, "findings.csv"), csv.ToString(), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(dir, "summary.txt"), Summary(r), new UTF8Encoding(false));
        }

        static string Summary(Report308 r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BuildingAudit308 scan " + r.alias + " (" + r.scene + ") utc " + r.utc + " quality " + r.quality + " [" + r.qualityLevels + "] config " + r.configVersion);
            sb.AppendLine("scene sha " + r.sceneSha + (r.sceneShaAfterScan == r.sceneSha ? " (unchanged)" : " CHANGED -> " + r.sceneShaAfterScan) + ", seconds " + F(r.seconds, "F1"));
            sb.AppendLine("roots: " + string.Join(", ", r.roots.Select(x => x.name + (x.present ? "(" + x.units + "u/" + x.buildings + "b" + (x.readOnly ? ",read-only" : "") + ")" : "(absent)"))));
            foreach (var c in r.counts) sb.AppendLine(string.Format(Inv, "  {0,-4} FAIL {1,5}  WARN {2,5}  SKIPPED {3,4}  INFO {4,5}  ({5:F1} s)", c.check, c.fail, c.warn, c.skipped, c.info, c.seconds));
            sb.AppendLine("C9 emission count (excluding " + "attraction" + "): " + r.emissionCount);
            foreach (var f in r.findings.Where(f => f.level == "FAIL").Take(40)) sb.AppendLine("  FAIL " + f.check + " " + f.path + " @" + V(f.position) + " " + f.detail);
            foreach (var n in r.notes) sb.AppendLine("note: " + n);
            return sb.ToString();
        }

        // newest scan folder for an alias (null when none)
        internal static string NewestScan(string alias)
        {
            string dir = Path.Combine(Folder, alias);
            if (!Directory.Exists(dir)) return null;
            return Directory.GetDirectories(dir).Where(d => File.Exists(Path.Combine(d, "audit.json"))).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal).LastOrDefault();
        }
        internal static Report308 LoadReport(string scanDir) => JsonUtility.FromJson<Report308>(File.ReadAllText(Path.Combine(scanDir, "audit.json")));
    }
}
