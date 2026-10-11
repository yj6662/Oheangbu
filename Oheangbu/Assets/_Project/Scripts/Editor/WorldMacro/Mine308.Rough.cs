using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 the first mine, look pass, second half (D308-60): the mine read as ruler-straight and freshly built. Numbers: mine308.json. Ledger: Ledger/rough308.json.
    //     rails     the track is no longer drawn (rails, sleepers, spur, buffer stops)
    //     floor     the floor (drawn chunks and the collider alike) rises and dips a little; what lies on it follows
    //     warp      the rock walls and roof (drawn chunks and their colliders alike) bulge and recede: one displacement that depends on the place only,
    //               so chunks, colliders and what hangs on the wall stay together. It fades out at floor height and at the portal mouth
    //     timber    the timber sets are broken: posts lean, some are snapped to stumps or gone, caps hang from one end or lie on the floor, boards are
    //               missing. Nothing hangs below headroom; what fell lies on the floor. The collider is rebuilt from the posts that still stand
    //     lanterns  no lamp posts: the post and bracket are switched off and each lantern sits on the floor or on a stone; a lit one glows from its
    //               middle (paper panes on a glowing material, the light at the centre of the panes); lit copies sit where the
    //               config lists a spot (lanterns are attraction lights)
    //     falls     roof falls: heaps of stone against the wall at the listed spots (each with one sphere collider inside the heap)
    public static partial class Mine308
    {
        [Serializable] sealed class Spot { public float x, z, side, r = 1.5f, h = 1.1f; public bool stone; }
        [Serializable] sealed class RoughCfg
        {
            public int seed = 308; public string stoneMaterial = "", timberMaterial = ""; public string[] rails = Array.Empty<string>(), floorMeshes = Array.Empty<string>(), floorFollow = Array.Empty<string>(), wallMeshes = Array.Empty<string>(), wallFollow = Array.Empty<string>(), jitter = Array.Empty<string>(), lanternHide = Array.Empty<string>(), wallKeys = Array.Empty<string>();
            public float floorUp = .1f, floorDown = .04f, floorWave = 2.4f, floorFine = .75f, wallAmp = .6f, wallWave = 6f, wallFine = 1.5f, wallFineShare = .22f, wallFadeFrom = 168.6f, wallFadeTo = 169.9f; public float[] wallCalm = Array.Empty<float>(), galleryDir = Array.Empty<float>();
            public float turnDeg = 4f, moveM = .05f, postGone = .14f, postSnapped = .2f, postLean = 9f, capGone = .12f, capDropped = .3f, boardGone = .4f, fallenShare = .65f, headroom = 1.95f;
            public string lanternParent = "", lanternPrefix = "", lampSource = "", lanternPaperMesh = ""; public float[] lanternGlow = Array.Empty<float>(), lanternLight = Array.Empty<float>(), lanternLightBefore = Array.Empty<float>(); public float lanternTilt = 5f, floorGuess = 170f, lampWallGap = .8f, lampIntensity = 2.8f, lampRange = 15f; public int lanternStoneEvery = 2; public Spot[] lampSpots = Array.Empty<Spot>(), falls = Array.Empty<Spot>();
        }
        [Serializable] sealed class MeshSwap { public string path = "", before = ""; public bool collider; }
        [Serializable] sealed class Pose { public string path = ""; public Vector3 pos; public Quaternion rot; }
        [Serializable] sealed class RoughLedger { public List<string> rails = new List<string>(), hidden = new List<string>(); public List<MeshSwap> floor = new List<MeshSwap>(), wall = new List<MeshSwap>(), timber = new List<MeshSwap>(); public List<Pose> poses = new List<Pose>(); public List<MeshSwap> paper = new List<MeshSwap>(); }
        static string RoughLedgerFile => Path.Combine(StageDir, "Ledger", "rough308.json");

        static string Rough(string step, bool revert, Transform root, Ledger308 led)
        {
            var cfg = JsonUtility.FromJson<RoughCfg>(File.ReadAllText(Path.Combine(StageDir, "mine308.json"))); var rl = File.Exists(RoughLedgerFile) ? JsonUtility.FromJson<RoughLedger>(File.ReadAllText(RoughLedgerFile)) : new RoughLedger(); string answer;
            switch (step)
            {
                case "rails": answer = Rails(root, cfg, rl, !revert); break;
                case "floor": answer = revert ? Unswap(root, rl.floor) : Floor(root, cfg, rl); break;
                case "warp": answer = revert ? Unswap(root, rl.wall) : Warp(root, cfg, rl); break;
                case "timber": answer = revert ? TimberRevert(root, led, rl) : Timber(root, cfg, led, rl); break;
                case "lanterns": answer = Lanterns(root, cfg, rl, !revert); break;
                case "falls": answer = Falls(root, cfg, !revert); break;
                case "ore": answer = Ore(root, !revert); break;      // Mine308.Ore.cs
                default: answer = "unknown step"; break;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(RoughLedgerFile)); File.WriteAllText(RoughLedgerFile, JsonUtility.ToJson(rl, true)); return answer;
        }

        // ---------------------------------------------------------------- shared
        static string Safe(string s) { var sb = new StringBuilder(); foreach (char ch in s) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_'); return sb.ToString(); }
        static int StableHash(string s) { unchecked { int h = 17; foreach (char ch in s) h = h * 31 + ch; return h & 0x7FFFFF; } }
        static void CleanMeshes(string prefix)
        {
            if (!AssetDatabase.IsValidFolder(AssetDir + "/Meshes")) return;
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { AssetDir + "/Meshes" })) { string p = AssetDatabase.GUIDToAssetPath(guid); if (Path.GetFileName(p).StartsWith(prefix, StringComparison.Ordinal)) AssetDatabase.DeleteAsset(p); }
        }
        static Mesh SaveMesh(Mesh mesh, string name) { Folder(AssetDir + "/Meshes"); mesh.name = name; string path = AssetDir + "/Meshes/" + name + ".asset"; AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mesh, path); AssetDatabase.SaveAssetIfDirty(mesh); return mesh; }
        static Transform LookHome(Transform root, string name)
        {
            var look = root.Find(LookRoot); if (look == null) { look = new GameObject(LookRoot).transform; look.SetParent(root, false); look.position = Vector3.zero; look.rotation = Quaternion.identity; }
            var old = look.Find(name); if (old != null) Object.DestroyImmediate(old.gameObject); var home = new GameObject(name).transform; home.SetParent(look, false); return home;
        }
        static float Hash3(int x, int y, int z, int s) { unchecked { uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)z * 2246822519u + (uint)s * 3266489917u; h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xFFFFFF) / 8388607.5f - 1f; } }
        static float Noise(Vector3 p, int s)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z); float fx = p.x - x, fy = p.y - y, fz = p.z - z; fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            float a = Mathf.Lerp(Mathf.Lerp(Hash3(x, y, z, s), Hash3(x + 1, y, z, s), fx), Mathf.Lerp(Hash3(x, y + 1, z, s), Hash3(x + 1, y + 1, z, s), fx), fy), b = Mathf.Lerp(Mathf.Lerp(Hash3(x, y, z + 1, s), Hash3(x + 1, y, z + 1, s), fx), Mathf.Lerp(Hash3(x, y + 1, z + 1, s), Hash3(x + 1, y + 1, z + 1, s), fx), fy);
            return Mathf.Lerp(a, b, fz);
        }
        static float Calm(RoughCfg cfg, Vector3 w) => cfg.wallCalm.Length == 4 ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cfg.wallCalm[2], cfg.wallCalm[3], Vector2.Distance(new Vector2(w.x, w.z), new Vector2(cfg.wallCalm[0], cfg.wallCalm[1])))) : 1f;

        // a copy of `src` with every vertex moved by field(world position); normals follow the field to first order (no chunk seams: the field depends on the place only)
        static Mesh Displace(Mesh src, Transform t, Func<Vector3, Vector3> field, bool normals, string name)
        {
            var v = src.vertices; var n = normals ? src.normals : null; var m = t.localToWorldMatrix; var inv = t.worldToLocalMatrix; const float e = .06f; bool withN = n != null && n.Length == v.Length;
            for (int i = 0; i < v.Length; i++)
            {
                var w = m.MultiplyPoint3x4(v[i]); var d = field(w);
                if (withN)
                {
                    var nw = m.MultiplyVector(n[i]).normalized; Vector3 dx = (field(w + Vector3.right * e) - d) / e, dy = (field(w + Vector3.up * e) - d) / e, dz = (field(w + Vector3.forward * e) - d) / e;
                    var bent = nw - new Vector3(Vector3.Dot(dx, nw), Vector3.Dot(dy, nw), Vector3.Dot(dz, nw)); if (bent.sqrMagnitude < 1e-6f) bent = nw;
                    var back = inv.MultiplyVector(bent.normalized); n[i] = back.sqrMagnitude > 1e-10f ? back.normalized : n[i];
                }
                v[i] = inv.MultiplyPoint3x4(w + d);
            }
            var mesh = Object.Instantiate(src); mesh.vertices = v; if (withN) mesh.normals = n; mesh.RecalculateBounds(); return SaveMesh(mesh, name);
        }
        static void SwapMesh(Transform root, List<MeshSwap> ledger, Component c, Mesh mesh)
        {
            bool col = c is MeshCollider; var cur = col ? ((MeshCollider)c).sharedMesh : ((MeshFilter)c).sharedMesh; ledger.Add(new MeshSwap { path = PathOf(c.transform, root), before = AssetDatabase.GetAssetPath(cur), collider = col });
            if (col) ((MeshCollider)c).sharedMesh = mesh; else ((MeshFilter)c).sharedMesh = mesh;
        }
        static string Unswap(Transform root, List<MeshSwap> list)
        {
            var before = new Dictionary<string, string>(); foreach (var s in list) before[s.path + (s.collider ? "|c" : "|f")] = s.before; int n = 0;
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true)) if (before.TryGetValue(PathOf(f.transform, root) + "|f", out string b)) { var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(b); if (mesh != null) { f.sharedMesh = mesh; n++; } }
            foreach (var c in root.GetComponentsInChildren<MeshCollider>(true)) if (before.TryGetValue(PathOf(c.transform, root) + "|c", out string b)) { var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(b); if (mesh != null) { c.sharedMesh = mesh; n++; } }
            list.Clear(); return n + " meshes restored";
        }
        static string SwapAll(Transform root, List<MeshSwap> ledger, string tag, string[] prefixes, Func<Vector3, Vector3> field, bool normals, Dictionary<string, Mesh> made)
        {
            int filters = 0, colliders = 0, skipped = 0; long verts = 0;
            bool Hit(Transform t) { string p = PathOf(t, root); return prefixes.Any(k => p.StartsWith(k, StringComparison.Ordinal)); }
            Mesh For(Mesh src, Transform t, bool nrm)
            {
                string key = src.GetInstanceID() + "|" + t.localToWorldMatrix.ToString("F3") + "|" + nrm;
                if (!made.TryGetValue(key, out var mesh)) { made[key] = mesh = Displace(src, t, field, nrm, tag + "_" + made.Count + "_" + Safe(src.name)); verts += src.vertexCount; }
                return mesh;
            }
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(false).ToList())
            {
                if (f.sharedMesh == null || !Hit(f.transform)) continue; if (AssetDatabase.GetAssetPath(f.sharedMesh).Length == 0) { skipped++; continue; }
                SwapMesh(root, ledger, f, For(f.sharedMesh, f.transform, normals)); filters++;
            }
            foreach (var c in root.GetComponentsInChildren<MeshCollider>(false).ToList())
            {
                if (c.sharedMesh == null || !Hit(c.transform)) continue; if (AssetDatabase.GetAssetPath(c.sharedMesh).Length == 0) { skipped++; continue; }
                SwapMesh(root, ledger, c, For(c.sharedMesh, c.transform, normals)); colliders++;
            }
            return filters + " drawn, " + colliders + " colliders, vertices " + verts + (skipped > 0 ? ", left alone (no asset behind the mesh) " + skipped : "");
        }

        // ---------------------------------------------------------------- rails
        static string Rails(Transform root, RoughCfg cfg, RoughLedger rl, bool hide)
        {
            var back = new HashSet<string>(rl.rails); int n = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                string p = PathOf(r.transform, root);
                if (!hide) { if (back.Contains(p)) { r.enabled = true; n++; } continue; }
                if (r.enabled && cfg.rails.Any(k => p.StartsWith(k, StringComparison.Ordinal))) { r.enabled = false; rl.rails.Add(p); n++; }
            }
            foreach (var c in root.GetComponentsInChildren<Collider>(true))      // what is no longer drawn must not block either (mine carts)
            {
                string p = PathOf(c.transform, root);
                if (!hide) { if (back.Contains(p + "|c")) { c.enabled = true; n++; } continue; }
                if (c.enabled && cfg.rails.Any(k => p.StartsWith(k, StringComparison.Ordinal))) { c.enabled = false; rl.rails.Add(p + "|c"); n++; }
            }
            if (!hide) rl.rails.Clear(); return n + (hide ? " renderers and colliders off" : " renderers and colliders on");
        }

        // ---------------------------------------------------------------- floor
        static float FloorF(RoughCfg cfg, Vector3 w)
        {
            float n = Noise(new Vector3(w.x / cfg.floorWave, 0f, w.z / cfg.floorWave), cfg.seed) * .7f + Noise(new Vector3(w.x / cfg.floorFine, 7f, w.z / cfg.floorFine), cfg.seed + 1) * .3f;
            n = Mathf.Clamp(n * 1.7f, -1f, 1f); return (n > 0f ? n * cfg.floorUp : n * cfg.floorDown) * Calm(cfg, w);
        }
        static string Floor(Transform root, RoughCfg cfg, RoughLedger rl)
        {
            if (rl.floor.Count > 0) return "already applied (floor:revert first)";
            CleanMeshes("Mine308_Floor"); Func<Vector3, Vector3> field = w => new Vector3(0f, FloorF(cfg, w), 0f); var made = new Dictionary<string, Mesh>();
            return "floor " + SwapAll(root, rl.floor, "Mine308_Floor", cfg.floorMeshes, field, true, made) + "; lying on it " + SwapAll(root, rl.floor, "Mine308_FloorOn", cfg.floorFollow, field, false, made);
        }

        // ---------------------------------------------------------------- warp
        static Vector3 WallD(RoughCfg cfg, Vector3 w)
        {
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cfg.wallFadeFrom, cfg.wallFadeTo, w.y)) * Calm(cfg, w); if (fade <= 0f) return Vector3.zero;
            var p = w / cfg.wallWave; var q = w / cfg.wallFine; int s = cfg.seed * 3; float k = cfg.wallFineShare;
            return new Vector3(Noise(p, s) + Noise(q, s + 3) * k, (Noise(p, s + 1) + Noise(q, s + 4) * k) * .5f, Noise(p, s + 2) + Noise(q, s + 5) * k) * (cfg.wallAmp * 1.5f * fade);
        }
        static string Warp(Transform root, RoughCfg cfg, RoughLedger rl)
        {
            if (rl.wall.Count > 0) return "already applied (warp:revert first)";
            CleanMeshes("Mine308_Wall"); Func<Vector3, Vector3> field = w => WallD(cfg, w); var made = new Dictionary<string, Mesh>();
            return "rock " + SwapAll(root, rl.wall, "Mine308_Wall", cfg.wallMeshes, field, true, made) + "; on the rock " + SwapAll(root, rl.wall, "Mine308_WallOn", cfg.wallFollow, field, false, made);
        }

        // ---------------------------------------------------------------- timber
        sealed class Piece
        {
            public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>(); public List<int> tri = new List<int>();
            public Vector3 c, axis, c0; public float len, thick, minY, maxY, minY0; public bool collide, dead; public int state;      // post state: 0 stands, 1 snapped, 2 gone
            public void Measure()
            {
                var mean = Vector3.zero; foreach (var p in v) mean += p; mean /= v.Count; float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
                foreach (var p in v) { var d = p - mean; xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z; }
                var a = new Vector3(.57f, .63f, .52f);
                for (int i = 0; i < 24; i++) { a = new Vector3(xx * a.x + xy * a.y + xz * a.z, xy * a.x + yy * a.y + yz * a.z, xz * a.x + yz * a.y + zz * a.z); float mag = a.magnitude; if (mag < 1e-9f) { a = Vector3.up; break; } a /= mag; }
                axis = a; float lo = float.MaxValue, hi = float.MinValue; thick = 0f; minY = float.MaxValue; maxY = float.MinValue;
                foreach (var p in v) { float s = Vector3.Dot(p - mean, axis); lo = Mathf.Min(lo, s); hi = Mathf.Max(hi, s); thick = Mathf.Max(thick, ((p - mean) - axis * s).magnitude * 2f); minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
                len = hi - lo; c = mean + axis * ((hi + lo) * .5f);
            }
            public void Move(Vector3 pivot, Quaternion q, Vector3 to) { for (int i = 0; i < v.Count; i++) { v[i] = to + q * (v[i] - pivot); n[i] = q * n[i]; } Measure(); }
            public void Lift(float dy) { for (int i = 0; i < v.Count; i++) v[i] += Vector3.up * dy; Measure(); }
            public Piece Copy() { var p = new Piece { c0 = c0, minY0 = minY0 }; p.v.AddRange(v); p.n.AddRange(n); p.uv.AddRange(uv); p.tri.AddRange(tri); p.Measure(); return p; }
        }
        static void Lay(Piece p, float floor, System.Random rng, float spread)
        {
            float R() => (float)rng.NextDouble(); float yaw = R() * Mathf.PI * 2f; var h = new Vector3(Mathf.Cos(yaw), (R() - .5f) * .16f, Mathf.Sin(yaw)).normalized; var q = Quaternion.AngleAxis(R() * 360f, h) * Quaternion.FromToRotation(p.axis, h);
            float a2 = R() * Mathf.PI * 2f, rr = spread * (.4f + R() * .6f); p.Move(p.c, q, new Vector3(p.c.x + Mathf.Cos(a2) * rr, p.c.y, p.c.z + Mathf.Sin(a2) * rr)); p.Lift(floor - .03f - p.minY);
        }
        static Mesh BuildPieces(IEnumerable<Piece> pieces, Matrix4x4 inv, string name)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            foreach (var p in pieces) { int k = v.Count; for (int i = 0; i < p.v.Count; i++) { v.Add(inv.MultiplyPoint3x4(p.v[i])); var nn = inv.MultiplyVector(p.n[i]); n.Add(nn.sqrMagnitude > 1e-10f ? nn.normalized : Vector3.up); uv.Add(p.uv[i]); } foreach (int t in p.tri) tri.Add(k + t); }
            if (v.Count == 0) return null;
            var mesh = new Mesh { indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 }; mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents(); return SaveMesh(mesh, name);
        }
        static string Timber(Transform root, RoughCfg cfg, Ledger308 led, RoughLedger rl)
        {
            if (led.meshes.Count > 0 || rl.timber.Count > 0) return "already applied (timber:revert first)";
            CleanMeshes("Mine308_Timber"); int meshes = 0, nPosts = 0, lean = 0, snapped = 0, postGone = 0, nCaps = 0, hung = 0, capDown = 0, nBoards = 0, boardGone = 0, onFloor = 0, colliders = 0;
            bool Wanted(MeshFilter f) { if (f.sharedMesh == null) return false; string p = PathOf(f.transform, root); return cfg.jitter.Any(k => p.Contains(k) && f.name.EndsWith("LOD0", StringComparison.Ordinal) || p.EndsWith(k, StringComparison.Ordinal)); }
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true).Where(Wanted).OrderBy(f => PathOf(f.transform, root), StringComparer.Ordinal).ToList())
            {
                string path = PathOf(mf.transform, root); if (Resolve(root, path) != mf.transform) continue; var src = mf.sharedMesh; if (AssetDatabase.GetAssetPath(src).Length == 0) continue;
                var sv = src.vertices; var sn = src.normals; var suv = src.uv; var st = src.triangles; var m = mf.transform.localToWorldMatrix; var rng = new System.Random(cfg.seed * 31 + StableHash(path)); float R() => (float)rng.NextDouble(); float S() => (float)rng.NextDouble() * 2f - 1f;
                var weld = new Dictionary<Vector3Int, int>(); var id = new int[sv.Length]; var parent = new List<int>();
                for (int i = 0; i < sv.Length; i++) { var k = Vector3Int.RoundToInt(sv[i] * 500f); if (!weld.TryGetValue(k, out int w)) { w = parent.Count; parent.Add(w); weld[k] = w; } id[i] = w; }
                int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
                for (int t = 0; t < st.Length; t += 3) { int a = Find(id[st[t]]), b = Find(id[st[t + 1]]), c = Find(id[st[t + 2]]); parent[b] = a; parent[Find(c)] = a; }
                var map = new Dictionary<int, Piece>(); var order = new List<Piece>(); var local = new int[sv.Length]; var owner = new Piece[sv.Length];
                for (int i = 0; i < sv.Length; i++)
                {
                    int g = Find(id[i]); if (!map.TryGetValue(g, out var p)) { map[g] = p = new Piece(); order.Add(p); }
                    local[i] = p.v.Count; owner[i] = p; p.v.Add(m.MultiplyPoint3x4(sv[i])); var nn = i < sn.Length ? m.MultiplyVector(sn[i]) : Vector3.up; p.n.Add(nn.sqrMagnitude > 1e-10f ? nn.normalized : Vector3.up); p.uv.Add(i < suv.Length ? suv[i] : Vector2.zero);
                }
                for (int t = 0; t < st.Length; t++) owner[st[t]].tri.Add(local[st[t]]);
                float meshMin = float.MaxValue; foreach (var p in order) { p.Measure(); p.c0 = p.c; p.minY0 = p.minY; meshMin = Mathf.Min(meshMin, p.minY); }
                var posts = order.Where(p => Mathf.Abs(p.axis.y) > .8f && p.len > 1.5f && p.len < 7f).ToList(); var caps = order.Where(p => Mathf.Abs(p.axis.y) <= .8f && p.len > 2.2f && p.len < 7f).ToList(); var boards = order.Where(p => p.len < 7f && !posts.Contains(p) && !caps.Contains(p)).ToList(); var extra = new List<Piece>();
                Piece PostNear(Vector3 at, float within) { Piece best = null; float bd = within * within; foreach (var p in posts) { float d = (p.c0.x - at.x) * (p.c0.x - at.x) + (p.c0.z - at.z) * (p.c0.z - at.z); if (d < bd) { bd = d; best = p; } } return best; }
                float FloorAt(Vector3 at) { var p = PostNear(at, 60f); return p != null ? p.minY0 : meshMin; }
                void Jitter(Piece p, float k) { var rot = Quaternion.Euler(S() * cfg.turnDeg * k, S() * cfg.turnDeg * 1.5f * k, S() * cfg.turnDeg * k); p.Move(p.c, rot, p.c + new Vector3(S(), S() * .4f, S()) * cfg.moveM * k); }
                void Down(Piece p, float floor, float spread) { if (R() < cfg.fallenShare) { Lay(p, floor, rng, spread); onFloor++; } else p.dead = true; }
                void Hang(Piece p, Vector3 pivot, Vector3 free, float drop, float floor)
                {
                    drop = Mathf.Min(drop, free.y - (floor + cfg.headroom)); if (drop <= .05f) { Jitter(p, 1.5f); return; }
                    float ang = Mathf.Asin(Mathf.Clamp01(drop / Mathf.Max(.1f, Vector3.Distance(pivot, free)))) * Mathf.Rad2Deg; var k = Vector3.Cross(free - pivot, Vector3.up); if (k.sqrMagnitude < 1e-6f) { Jitter(p, 1.5f); return; }
                    var q = Quaternion.AngleAxis(ang, k.normalized); if ((q * (free - pivot)).y > (free - pivot).y) q = Quaternion.AngleAxis(-ang, k.normalized); p.Move(pivot, q, pivot); hung++;
                }
                foreach (var p in posts)
                {
                    nPosts++; float r = R(); var foot = new Vector3(p.c.x, p.minY, p.c.z);
                    if (r < cfg.postGone) { p.state = 2; postGone++; Down(p, p.minY0, 1.1f); continue; }
                    if (r < cfg.postGone + cfg.postSnapped)
                    {
                        p.state = 1; snapped++; float keep = .3f + R() * .3f; var top = p.Copy(); var slant = new Vector3(S(), 0f, S()) * 1.2f;
                        for (int i = 0; i < p.v.Count; i++) { var w = p.v[i]; float y = p.minY0 + (w.y - p.minY0) * keep; if (w.y > p.c0.y) y += Vector3.Dot(w - p.c0, slant); p.v[i] = new Vector3(w.x, y, w.z); }
                        p.Measure(); p.Move(foot, Quaternion.AngleAxis(R() * cfg.postLean, new Vector3(S(), 0f, S() + .01f).normalized), foot); p.collide = p.maxY - p.minY > .7f;
                        for (int i = 0; i < top.v.Count; i++) { var w = top.v[i]; top.v[i] = new Vector3(w.x, top.minY0 + (w.y - top.minY0) * (1f - keep), w.z); }
                        top.Measure(); Lay(top, p.minY0, rng, 1.3f); onFloor++; extra.Add(top); continue;
                    }
                    float ang = Mathf.Pow(R(), 1.6f) * cfg.postLean; if (ang > cfg.postLean * .45f) lean++; p.Move(foot, Quaternion.AngleAxis(ang, new Vector3(S(), 0f, S() + .01f).normalized), foot); p.collide = true;
                }
                foreach (var p in caps)
                {
                    nCaps++; Vector3 e0 = p.c - p.axis * (p.len * .5f), e1 = p.c + p.axis * (p.len * .5f); var p0 = PostNear(e0, 1f); var p1 = PostNear(e1, 1f); bool lost0 = p0 != null && p0.state != 0, lost1 = p1 != null && p1.state != 0; float floor = FloorAt(p.c); float r = R();
                    if (lost0 && lost1 || !lost0 && !lost1 && r < cfg.capGone) { capDown++; Down(p, floor, .8f); continue; }
                    if (lost0 || lost1) { Hang(p, lost0 ? e1 : e0, lost0 ? e0 : e1, .35f + R() * .6f, floor); continue; }
                    if (r < cfg.capGone + cfg.capDropped) { bool first = R() < .5f; Hang(p, first ? e1 : e0, first ? e0 : e1, .2f + R() * .45f, floor); continue; }
                    Jitter(p, 1.5f);
                }
                foreach (var p in boards)
                {
                    nBoards++; if (R() < cfg.boardGone) { boardGone++; if (R() < cfg.fallenShare * .5f) { Lay(p, FloorAt(p.c), rng, 1f); onFloor++; } else p.dead = true; continue; }
                    Jitter(p, 3f);
                }
                var alive = order.Concat(extra).Where(p => !p.dead).ToList(); var inv = mf.transform.worldToLocalMatrix; var mesh = BuildPieces(alive, inv, "Mine308_Timber_" + meshes + "_" + Safe(src.name)); if (mesh == null) continue;
                SwapMesh(root, rl.timber, mf, mesh);
                if (mf.transform.parent != null)
                {
                    foreach (var other in mf.transform.parent.GetComponentsInChildren<MeshFilter>(true)) if (other != mf && other.sharedMesh != null && other.name.Contains("_LOD") && other.transform.localToWorldMatrix == mf.transform.localToWorldMatrix && AssetDatabase.GetAssetPath(other.sharedMesh).Length > 0) SwapMesh(root, rl.timber, other, mesh);
                    foreach (var col in mf.transform.parent.GetComponentsInChildren<MeshCollider>(true))
                    {
                        if (!col.name.Contains("Collision") || col.sharedMesh == null || AssetDatabase.GetAssetPath(col.sharedMesh).Length == 0) continue;
                        var solid = BuildPieces(alive.Where(p => p.collide), col.transform.worldToLocalMatrix, "Mine308_TimberCollision_" + meshes + "_" + Safe(src.name)); if (solid == null) continue; SwapMesh(root, rl.timber, col, solid); colliders++;
                    }
                }
                meshes++;
            }
            return meshes + " meshes: posts " + nPosts + " (leaning hard " + lean + ", snapped " + snapped + ", gone " + postGone + "), caps " + nCaps + " (hanging " + hung + ", down " + capDown + "), boards " + nBoards + " (gone " + boardGone + "), pieces on the floor " + onFloor + ", colliders rebuilt " + colliders;
        }
        static string TimberRevert(Transform root, Ledger308 led, RoughLedger rl)
        {
            int n = 0; foreach (var s in led.meshes) { var t = Resolve(root, s.path); var mf = t != null ? t.GetComponent<MeshFilter>() : null; var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(s.before); if (mf != null && mesh != null) { mf.sharedMesh = mesh; n++; } }
            led.meshes.Clear(); return n + " first-pass meshes restored, " + Unswap(root, rl.timber);
        }

        // ---------------------------------------------------------------- lanterns
        static bool GroundUnder(Vector3 from, float reach, Transform skipA, Transform skipB, out RaycastHit best)
        {
            best = default; bool any = false;
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, reach, ~0, QueryTriggerInteraction.Ignore))
            {
                if (skipA != null && hit.transform.IsChildOf(skipA) || skipB != null && hit.transform.IsChildOf(skipB)) continue;
                if (!any || hit.point.y > best.point.y) { best = hit; any = true; }
            }
            return any;
        }
        static float WallDistance(Transform root, RoughCfg cfg, Vector3 from, Vector3 dir, float fallback)
        {
            float best = float.MaxValue; foreach (var hit in Physics.RaycastAll(from, dir, 9f, ~0, QueryTriggerInteraction.Ignore)) { string p = PathOf(hit.transform, root); if (cfg.wallKeys.Any(k => p.Contains(k)) && hit.distance < best) best = hit.distance; }
            return best < float.MaxValue ? best : fallback;
        }
        static Vector3 Across(RoughCfg cfg) { var d = cfg.galleryDir.Length == 2 ? new Vector3(cfg.galleryDir[0], 0f, cfg.galleryDir[1]).normalized : Vector3.forward; return new Vector3(d.z, 0f, -d.x); }
        // a lit lantern glows from its middle: the paper panes take the glowing material and the light sits at the centre of the panes (not at the foot)
        static void Glow(Transform root, Transform group, RoughCfg cfg, Material paper, RoughLedger rl)
        {
            var lights = group.GetComponentsInChildren<Light>(false).Where(l => l.enabled && l.transform != group).ToList(); if (lights.Count == 0) return;
            var panes = group.GetComponentsInChildren<MeshRenderer>(false).Where(r => { var f = r.GetComponent<MeshFilter>(); return f != null && f.sharedMesh != null && f.sharedMesh.name == cfg.lanternPaperMesh; }).ToList(); if (panes.Count == 0) return;
            var box = panes[0].bounds; foreach (var r in panes) box.Encapsulate(r.bounds);
            foreach (var r in panes) { if (rl != null) rl.paper.Add(new MeshSwap { path = PathOf(r.transform, root), before = AssetDatabase.GetAssetPath(r.sharedMaterial) }); r.sharedMaterial = paper; }
            foreach (var l in lights)
            {
                var t = l.transform; var kids = new List<Transform>(); foreach (Transform k in t) kids.Add(k); var at = kids.Select(k => k.position).ToList();
                if (rl != null) { rl.poses.Add(new Pose { path = PathOf(t, root), pos = t.position, rot = t.rotation }); foreach (var k in kids) rl.poses.Add(new Pose { path = PathOf(k, root), pos = k.position, rot = k.rotation }); }
                t.position = box.center; for (int i = 0; i < kids.Count; i++) kids[i].position = at[i]; if (cfg.lanternLight.Length == 3) l.color = new Color(cfg.lanternLight[0], cfg.lanternLight[1], cfg.lanternLight[2], 1f);
            }
        }
        static string Lanterns(Transform root, RoughCfg cfg, RoughLedger rl, bool apply)
        {
            var hidden = new HashSet<string>(rl.hidden); if (hidden.Count > 0) foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (hidden.Contains(PathOf(t, root))) t.gameObject.SetActive(true);
            foreach (var swap in rl.paper) { var t = Resolve(root, swap.path); var r = t != null ? t.GetComponent<MeshRenderer>() : null; var mat = AssetDatabase.LoadAssetAtPath<Material>(swap.before); if (r != null && mat != null) r.sharedMaterial = mat; }
            rl.paper.Clear(); if (cfg.lanternLightBefore.Length == 3) foreach (var pose in rl.poses) { var t = Resolve(root, pose.path); var l = t != null ? t.GetComponent<Light>() : null; if (l != null) l.color = new Color(cfg.lanternLightBefore[0], cfg.lanternLightBefore[1], cfg.lanternLightBefore[2], 1f); }
            foreach (var pose in rl.poses) { var t = Resolve(root, pose.path); if (t != null) { t.position = pose.pos; t.rotation = pose.rot; } }
            rl.hidden.Clear(); rl.poses.Clear(); CleanMeshes("Mine308_LampStones"); var home = LookHome(root, "Lamps");
            if (!apply) { Object.DestroyImmediate(home.gameObject); return "lanterns back on their posts, copies removed"; }
            var stoneMat = AssetDatabase.LoadAssetAtPath<Material>(cfg.stoneMaterial); var parent = Resolve(root, cfg.lanternParent); var source = Resolve(root, cfg.lampSource); if (stoneMat == null || parent == null || source == null) throw new Exception("lantern parent, source or stone material missing");
            Folder(AssetDir + "/Materials"); string paperPath = AssetDir + "/Materials/Mine308_LanternPaper.mat"; var paper = AssetDatabase.LoadAssetAtPath<Material>(paperPath);
            if (paper == null) { paper = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Mine308_LanternPaper" }; AssetDatabase.CreateAsset(paper, paperPath); }
            if (cfg.lanternGlow.Length == 3) paper.SetColor("_BaseColor", new Color(cfg.lanternGlow[0], cfg.lanternGlow[1], cfg.lanternGlow[2], 1f)); EditorUtility.SetDirty(paper); AssetDatabase.SaveAssetIfDirty(paper);
            var rng = new System.Random(cfg.seed * 13 + 5); float R() => (float)rng.NextDouble(); float S() => (float)rng.NextDouble() * 2f - 1f; var across = Across(cfg);
            var sv = new List<Vector3>(); var sn = new List<Vector3>(); var suv = new List<Vector2>(); var stri = new List<int>(); var sb = new StringBuilder(); int placed = 0, onStone = 0, failed = 0;
            var jobs = new List<(Transform group, Vector3 target, bool stone)>();
            for (int i = 0; i < cfg.lampSpots.Length; i++)      // copies first: the source still hangs where it was built
            {
                var spot = cfg.lampSpots[i]; var copy = Object.Instantiate(source.gameObject, home); copy.name = "Lamp_" + i; copy.transform.SetPositionAndRotation(source.position, source.rotation);
                foreach (var child in copy.GetComponentsInChildren<Transform>(true).Where(c => c != null && cfg.lanternHide.Contains(c.name)).ToList()) Object.DestroyImmediate(child.gameObject);
                foreach (var l in copy.GetComponentsInChildren<Light>(true)) { l.enabled = true; l.intensity = cfg.lampIntensity; l.range = cfg.lampRange; }
                var at = new Vector3(spot.x, cfg.floorGuess - .6f, spot.z); if (Mathf.Abs(spot.side) > .01f) { var dir = across * Mathf.Sign(spot.side); at += dir * Mathf.Max(.6f, WallDistance(root, cfg, at, dir, 2.6f) - cfg.lampWallGap); }
                Glow(root, copy.transform, cfg, paper, null); jobs.Add((copy.transform, at, spot.stone));
            }
            int index = 0;
            foreach (Transform group in parent)
            {
                if (!group.name.StartsWith(cfg.lanternPrefix, StringComparison.Ordinal) || !group.gameObject.activeInHierarchy) continue; Transform post = null;
                foreach (Transform child in group) if (cfg.lanternHide.Contains(child.name)) { if (post == null || child.lossyScale.y > post.lossyScale.y) post = child; }
                var parts = group.GetComponentsInChildren<Renderer>(false).Where(r => !cfg.lanternHide.Contains(r.name)).ToList(); if (post == null || parts.Count == 0) continue;
                var box = parts[0].bounds; foreach (var r in parts) box.Encapsulate(r.bounds); var toward = new Vector3(box.center.x - post.position.x, 0f, box.center.z - post.position.z); toward = toward.sqrMagnitude > 1e-4f ? toward.normalized : across;
                var target = new Vector3(post.position.x, 0f, post.position.z) + toward * (.3f + R() * .5f) + new Vector3(toward.z, 0f, -toward.x) * (S() * .35f);
                foreach (Transform child in group) if (cfg.lanternHide.Contains(child.name) && child.gameObject.activeSelf) { child.gameObject.SetActive(false); rl.hidden.Add(PathOf(child, root)); }
                rl.poses.Add(new Pose { path = PathOf(group, root), pos = group.position, rot = group.rotation }); Glow(root, group, cfg, paper, rl); jobs.Add((group, target, cfg.lanternStoneEvery > 0 && index % cfg.lanternStoneEvery == 1)); index++;
            }
            Physics.SyncTransforms();
            foreach (var job in jobs)
            {
                var parts = job.group.GetComponentsInChildren<Renderer>(false).ToList(); if (parts.Count == 0) { failed++; continue; }
                var box = parts[0].bounds; foreach (var r in parts) box.Encapsulate(r.bounds);
                if (!GroundUnder(new Vector3(job.target.x, cfg.floorGuess, job.target.z), 4f, job.group, home, out var hit)) { failed++; sb.Append("\n    no ground under ").Append(job.group.name); if (job.group.parent == home) Object.DestroyImmediate(job.group.gameObject); continue; }
                float top = hit.point.y; string on = hit.transform.name;
                if (job.stone)
                {
                    float size = .6f + R() * .35f; int k0 = sv.Count; Stone(sv, sn, suv, stri, hit.point + Vector3.up * size * .12f, size, rng); var ys = sv.Skip(k0).Select(p => p.y).OrderByDescending(y => y).ToList(); top = Mathf.Max(hit.point.y, ys[2] - .02f); on = "a stone on " + on; onStone++;
                }
                var pivot = new Vector3(box.center.x, box.min.y, box.center.z); var q = Quaternion.Euler(S() * cfg.lanternTilt, R() * 360f, S() * cfg.lanternTilt);
                job.group.position = new Vector3(job.target.x, top, job.target.z) + q * (job.group.position - pivot); job.group.rotation = q * job.group.rotation; placed++;
                sb.Append("\n    ").Append(job.group.name).Append(" (").Append(job.target.x.ToString("F1", Inv)).Append(", ").Append(top.ToString("F2", Inv)).Append(", ").Append(job.target.z.ToString("F1", Inv)).Append(") on ").Append(on);
            }
            if (sv.Count > 0)
            {
                var mesh = new Mesh(); mesh.SetVertices(sv); mesh.SetNormals(sn); mesh.SetUVs(0, suv); mesh.SetTriangles(stri, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents(); SaveMesh(mesh, "Mine308_LampStones");
                var g = new GameObject("Mine308_LampStones"); g.transform.SetParent(home, false); g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = stoneMat;
            }
            return placed + " lanterns set down (" + onStone + " on a stone, copies " + cfg.lampSpots.Length + "), posts switched off " + rl.hidden.Count + ", failed " + failed + sb;
        }

        // ---------------------------------------------------------------- falls
        static string Falls(Transform root, RoughCfg cfg, bool apply)
        {
            CleanMeshes("Mine308_Fall"); var home = LookHome(root, "Falls"); if (!apply) { Object.DestroyImmediate(home.gameObject); return "heaps removed"; }
            var stoneMat = AssetDatabase.LoadAssetAtPath<Material>(cfg.stoneMaterial); var woodMat = AssetDatabase.LoadAssetAtPath<Material>(cfg.timberMaterial); if (stoneMat == null || woodMat == null) throw new Exception("stone or timber material missing");
            Physics.SyncTransforms(); var rng = new System.Random(cfg.seed * 17 + 3); float R() => (float)rng.NextDouble(); var across = Across(cfg); var sb = new StringBuilder(); int stones = 0;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>(); var pv = new List<Vector3>(); var pn = new List<Vector3>(); var puv = new List<Vector2>(); var ptri = new List<int>();
            for (int i = 0; i < cfg.falls.Length; i++)
            {
                var f = cfg.falls[i]; var at = new Vector3(f.x, cfg.floorGuess - .6f, f.z); var dir = across * (f.side < 0f ? -1f : 1f); float wall = WallDistance(root, cfg, at, dir, 2.6f);
                var inner = at + dir * Mathf.Max(.4f, wall - f.r * 1.3f); float floor = GroundUnder(new Vector3(inner.x, cfg.floorGuess, inner.z), 4f, home, null, out var hit) ? hit.point.y : cfg.floorGuess - 1.6f; var c = at + dir * Mathf.Max(.6f, wall - f.r * .45f); c.y = floor;
                int count = Mathf.Clamp(Mathf.RoundToInt(55f * f.r * f.r * f.h), 50, 260);
                for (int k = 0; k < count; k++)
                {
                    float ang = R() * Mathf.PI * 2f, rad = f.r * Mathf.Sqrt(R()), cap = f.h * (1f - Mathf.Pow(rad / f.r, 1.5f)), y = R() * cap, size = Mathf.Lerp(.32f, .95f, Mathf.Pow(R(), 1.6f)) * (1f - .4f * y / Mathf.Max(.1f, f.h));
                    Stone(v, n, uv, tri, new Vector3(c.x + Mathf.Cos(ang) * rad, floor + y + size * .15f, c.z + Mathf.Sin(ang) * rad), size, rng); stones++;
                }
                for (int k = 0; k < 3; k++)
                {
                    float ang = R() * Mathf.PI * 2f; var a = new Vector3(c.x + Mathf.Cos(ang) * f.r * .5f * R(), floor + .1f + R() * f.h * .4f, c.z + Mathf.Sin(ang) * f.r * .5f * R()); float out2 = R() * Mathf.PI * 2f;
                    Log(pv, pn, puv, ptri, a, a + new Vector3(Mathf.Cos(out2) * (1.1f + R()), .35f + R() * .9f, Mathf.Sin(out2) * (1.1f + R())), .08f + R() * .05f, rng);
                }
                float hc = f.h * .8f, ra = f.r * .85f, sphere = (ra * ra + hc * hc) / (2f * hc); var solid = new GameObject("Fall_" + i + "_Collision"); solid.transform.SetParent(home, false); solid.transform.position = new Vector3(c.x, floor + hc - sphere, c.z); solid.AddComponent<SphereCollider>().radius = sphere;
                sb.Append("\n    heap ").Append(i).Append(" (").Append(c.x.ToString("F1", Inv)).Append(", ").Append(floor.ToString("F2", Inv)).Append(", ").Append(c.z.ToString("F1", Inv)).Append(") wall at ").Append(wall.ToString("F1", Inv)).Append(" m, stones ").Append(count);
            }
            void Make(string name, List<Vector3> vv, List<Vector3> nn, List<Vector2> uu, List<int> tt, Material mat)
            {
                if (vv.Count == 0) return; var mesh = new Mesh { indexFormat = vv.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 }; mesh.SetVertices(vv); mesh.SetNormals(nn); mesh.SetUVs(0, uu); mesh.SetTriangles(tt, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents(); SaveMesh(mesh, name);
                var g = new GameObject(name); g.transform.SetParent(home, false); g.AddComponent<MeshFilter>().sharedMesh = mesh; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.On;
            }
            Make("Mine308_Fall_Stones", v, n, uv, tri, stoneMat); Make("Mine308_Fall_Logs", pv, pn, puv, ptri, woodMat);
            return cfg.falls.Length + " heaps, " + stones + " stones" + sb;
        }
    }
}
