using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-1 mesh track (DEFECTS_308 "찢긴 메시 파편" 0459, "퇴화 삼각형 시트" 0479). Own queue entry so BuildingFix308.cs stays
    // untouched:
    //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.BuildingFix308 Meshes "<command>"
    //   list                          rows of mesh308.json (read only, no scene)
    //   probe:<alias>[:id,…]          read only -> Fix/mesh-probe-<alias>-<utc>.json. Per site: every LOD and collider mesh of the owner
    //                                 (triangles, zero-area, winding flips, rim loops, non-manifold edges, LODn -> LOD0 deviation), the
    //                                 rim against the ground (terrain + other colliders), the still's eye against the shell (triangles
    //                                 and colliders), and a verdict: FALSE-POSITIVE | REAL (open rim) | REAL (broken mesh) | UNEXPLAINED
    //   eye:<alias>:<x,y,z>           read only: is this point inside a one-sided shell (colliders, and the renderers around it)?
    //   reshoot:<alias>[:id,…]        read only: re-seats the replacement eyes of mesh308.json on the ground, drops the ones inside a
    //                                 shell, writes Shots/shots-mesh308-<alias>.json for Tools/Unity/buildingaudit308_sweep.py
    // Finding (2026-10-03, offline, all three scenes): nothing is torn and no LOD is broken. Both stills were shot from inside a
    // hollow one-sided rock shell (CliffPath_jeokro_crag_Face_0; Highlands293 cheolong_0A/Band): the audit eye sits on the terrain
    // under the shell, back-face culling removes the shell and only the inward-facing triangles of its concave folds remain. The
    // rims are buried all the way round, so the player cannot stand there. This track therefore changes no scene and no asset: it
    // has no apply and no revert, and every command here is read only (JSON under BuildingAudit308/Fix and Shots only).
    // InsideShell308 is the test BuildingAudit308.Shots EyeFor lacks (prefer another eye, or mark the shot). Values come from
    // BuildingAudit308/mesh308.json.
    public static partial class BuildingFix308
    {
        static string MeshConfigFile => Path.Combine(BuildingAudit308.Folder, "mesh308.json");
        const int MeshUpDir = 2;
        static readonly Vector3[] MeshDirs =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
            new Vector3(1, 1, 1).normalized, new Vector3(1, 1, -1).normalized, new Vector3(1, -1, 1).normalized, new Vector3(1, -1, -1).normalized,
            new Vector3(-1, 1, 1).normalized, new Vector3(-1, 1, -1).normalized, new Vector3(-1, -1, 1).normalized, new Vector3(-1, -1, -1).normalized,
        };

        // ------------------------------------------------------------------ config (BuildingAudit308/mesh308.json)

        [Serializable] internal sealed class MeshShot308 { public string name = "", note = ""; public float[] eye = Array.Empty<float>(), target = Array.Empty<float>(); }
        [Serializable] internal sealed class MeshSite308
        {
            public string id = "", still = "", defect = "", expect = "";
            public float[] eye = Array.Empty<float>(), target = Array.Empty<float>();
            public string[] owners = Array.Empty<string>(), also = Array.Empty<string>();
            public MeshShot308[] reshoot = Array.Empty<MeshShot308>();
        }
        [Serializable] internal sealed class MeshCfg308
        {
            public string version = "", status = "", finding = "";
            public float eyeHeight, eyeProbeLift, rayMax, insideShare, sealTolerance, enterableGap, rimWindow, flipShareFail, deviationFail, weld, navRadius, protrudeWarn;
            public int deviationSamples;
            public MeshSite308[] sites = Array.Empty<MeshSite308>();
        }

        // ------------------------------------------------------------------ report shapes

        [Serializable] internal sealed class MeshRow308
        {
            public string path = "", mesh = "", role = "";   // role: lod | collider | also
            public int lod = -1, vertices, welded, triangles, zeroArea, flipped, boundaryEdges, loops, nonManifold, rayHits, rayBack;
            public float area, flippedArea, maxEdge, deviationMax = -1f, deviationP95 = -1f, protrudeMax = -1f;
            public int protrudeOver;   // collider rows: LOD0 vertices standing more than protrudeWarn outside the collider
            public bool protectedAsset, upBack, inside;
            public Vector3 boundsMin, boundsMax;
        }
        [Serializable] internal sealed class MeshRim308
        {
            public string path = "", mesh = "", role = "";
            public int vertices, above, enterable, noGround;
            public float gapMin, gapMedian, gapMax;
            public Vector3 worst;
        }
        [Serializable] internal sealed class MeshEye308
        {
            public string name = "", owner = "", command = "", note = "", state = "";   // state: ok | inside | blocked | no-ground
            public Vector3 eye, target;
            public float ground = float.NaN, overGround = float.NaN;
            public int colliderHits, colliderBack;
            public bool insideColliders, insideOwner, overlap, clear, navMesh;
        }
        [Serializable] internal sealed class MeshSiteReport308
        {
            public string id = "", still = "", defect = "", expect = "", verdict = "", detail = "";
            public bool asExpected, protectedOwner;
            public List<MeshRow308> rows = new List<MeshRow308>();
            public List<MeshRim308> rims = new List<MeshRim308>();
            public MeshEye308 stillEye = new MeshEye308();
            public List<MeshEye308> reshoot = new List<MeshEye308>();
            public List<string> notes = new List<string>();
        }
        [Serializable] internal sealed class MeshReport308
        {
            public string alias = "", scene = "", utc = "", sceneSha = "", quality = "", configVersion = "", finding = "";
            public int falsePositive, real, unexplained, unexpected;
            public List<MeshSiteReport308> sites = new List<MeshSiteReport308>();
        }

        sealed class MeshTopo308
        {
            public int vertices, welded, triangles, zeroArea, flipped, boundaryEdges, loops, nonManifold;
            public float area, flippedArea, maxEdge;
            public List<Vector3> rim = new List<Vector3>();   // mesh space, one per welded rim vertex
        }
        struct MeshEdge308 { public int t0, t1, count; public bool d0, d1; }

        static MeshCfg308 MeshLoad(out string error)
        {
            error = null;
            if (!File.Exists(MeshConfigFile)) { error = "mesh config missing: " + MeshConfigFile; return null; }
            try
            {
                var c = JsonUtility.FromJson<MeshCfg308>(File.ReadAllText(MeshConfigFile).TrimStart((char)0xFEFF));
                if (c == null || c.sites.Length == 0 || c.weld <= 0f || c.rayMax <= 0f || c.insideShare <= 0f || c.eyeHeight <= 0f || c.eyeProbeLift <= 0f || c.rimWindow <= 0f || c.enterableGap <= 0f || c.flipShareFail <= 0f || c.deviationFail <= 0f || c.deviationSamples <= 0)
                { error = "mesh config incomplete (sites/weld/rayMax/insideShare/eyeHeight/eyeProbeLift/rimWindow/enterableGap/flipShareFail/deviationFail/deviationSamples): " + MeshConfigFile; return null; }
                foreach (var s in c.sites)
                    if (string.IsNullOrEmpty(s.id) || s.eye.Length < 3 || s.target.Length < 3 || s.owners.Length == 0) { error = "mesh config site needs id, eye, target, owners: " + s.id; return null; }
                return c;
            }
            catch (Exception e) { error = "mesh config unreadable: " + e.Message; return null; }
        }

        static MeshSite308[] MeshSelect(MeshCfg308 mc, string ids, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(ids)) return mc.sites;
            var want = ids.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            var missing = want.Where(w => mc.sites.All(s => s.id != w)).ToArray();
            if (missing.Length > 0) { error = "refused: unknown site id " + string.Join(", ", missing) + " (" + string.Join(", ", mc.sites.Select(s => s.id)) + ")"; return null; }
            return mc.sites.Where(s => want.Contains(s.id)).ToArray();
        }

        // ------------------------------------------------------------------ entry

        public static string Meshes(string command)
        {
            command = (command ?? "").Trim();
            const string usage = "refused: BuildingFix308.Meshes list | probe:<alias>[:id,…] | eye:<alias>:<x,y,z> | reshoot:<alias>[:id,…] (read only — this track has no apply)";
            try
            {
                var a = command.Split(':');
                switch (a[0])
                {
                    case "list": return MeshList();
                    case "probe": return a.Length > 1 ? MeshProbe(a[1], a.Length > 2 ? a[2] : null) : usage;
                    case "eye": return a.Length > 2 ? MeshEyeCommand(a[1], a[2]) : usage;
                    case "reshoot": return a.Length > 1 ? MeshReshoot(a[1], a.Length > 2 ? a[2] : null) : usage;
                    default: return usage;
                }
            }
            catch (Exception e) { return "FAILED: " + e; }
        }

        static string MeshList()
        {
            var mc = MeshLoad(out string err);
            if (mc == null) return "refused: " + err;
            var sb = new StringBuilder("mesh308 " + mc.version + " — " + mc.sites.Length + " site(s)\n");
            foreach (var s in mc.sites)
                sb.AppendLine("  " + s.id + " expect " + s.expect + " | eye " + BuildingAudit308.V(BuildingAudit308.Vec(s.eye)) + " -> " + BuildingAudit308.V(BuildingAudit308.Vec(s.target)) + " | owners " + string.Join(", ", s.owners) + " | reshoot " + s.reshoot.Length + " | " + s.still);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ topology of one mesh (mesh space)

        static void MeshAddEdge(Dictionary<long, MeshEdge308> edges, int u, int v, int tri)
        {
            bool fwd = u < v; long key = fwd ? ((long)u << 32) | (uint)v : ((long)v << 32) | (uint)u;
            edges.TryGetValue(key, out var e);
            if (e.count == 0) { e.t0 = tri; e.d0 = fwd; } else if (e.count == 1) { e.t1 = tri; e.d1 = fwd; }
            e.count++; edges[key] = e;
        }

        // welded (weld m) manifold bookkeeping: rim = edges used once, non-manifold = edges used 3+ times, flipped = the smaller-area
        // side of every winding disagreement across a two-triangle edge (fold-overs of a decimated LOD, or genuinely reversed faces)
        static MeshTopo308 MeshTopo(Vector3[] v, int[] t, float weld)
        {
            var r = new MeshTopo308 { vertices = v.Length, triangles = t.Length / 3 };
            double inv = 1.0 / Math.Max(weld, 1e-6f);
            var ids = new Dictionary<(long, long, long), int>(v.Length);
            var w = new int[v.Length]; var wp = new List<Vector3>(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                var k = ((long)Math.Round(v[i].x * inv), (long)Math.Round(v[i].y * inv), (long)Math.Round(v[i].z * inv));
                if (!ids.TryGetValue(k, out int id)) { id = wp.Count; ids[k] = id; wp.Add(v[i]); }
                w[i] = id;
            }
            r.welded = wp.Count;
            int n = t.Length / 3;
            var keep = new bool[n]; var areas = new float[n];
            var edges = new Dictionary<long, MeshEdge308>(n * 2);
            for (int i = 0; i < n; i++)
            {
                Vector3 pa = v[t[i * 3]], pb = v[t[i * 3 + 1]], pc = v[t[i * 3 + 2]];
                float ar = .5f * Vector3.Cross(pb - pa, pc - pa).magnitude; areas[i] = ar; r.area += ar;
                r.maxEdge = Mathf.Max(r.maxEdge, Mathf.Max((pb - pa).magnitude, Mathf.Max((pc - pb).magnitude, (pa - pc).magnitude)));
                int a = w[t[i * 3]], b = w[t[i * 3 + 1]], c = w[t[i * 3 + 2]];
                bool collapsed = a == b || b == c || a == c;
                if (collapsed || ar < 1e-8f) r.zeroArea++;
                if (collapsed) continue;
                keep[i] = true;
                MeshAddEdge(edges, a, b, i); MeshAddEdge(edges, b, c, i); MeshAddEdge(edges, c, a, i);
            }
            // rim loops (union-find over the rim vertices) and the neighbour table (two-triangle edges only)
            var parent = new Dictionary<int, int>();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            var nb = new int[n * 3]; var same = new bool[n * 3]; var cnt = new byte[n];
            foreach (var kv in edges)
            {
                var e = kv.Value;
                if (e.count == 1)
                {
                    r.boundaryEdges++;
                    int u = (int)(kv.Key >> 32), q = (int)(kv.Key & 0xffffffffL);
                    if (!parent.ContainsKey(u)) parent[u] = u;
                    if (!parent.ContainsKey(q)) parent[q] = q;
                    int ru = Find(u), rq = Find(q); if (ru != rq) parent[ru] = rq;
                }
                else if (e.count == 2)
                {
                    bool s = e.d0 == e.d1;   // both triangles run the shared edge the same way = their windings disagree
                    if (cnt[e.t0] < 3) { nb[e.t0 * 3 + cnt[e.t0]] = e.t1; same[e.t0 * 3 + cnt[e.t0]] = s; cnt[e.t0]++; }
                    if (cnt[e.t1] < 3) { nb[e.t1 * 3 + cnt[e.t1]] = e.t0; same[e.t1 * 3 + cnt[e.t1]] = s; cnt[e.t1]++; }
                }
                else r.nonManifold++;
            }
            var roots = new HashSet<int>();
            foreach (int k in parent.Keys.ToArray()) { roots.Add(Find(k)); r.rim.Add(wp[k]); }
            r.loops = roots.Count;
            // orientation by flood fill; per connected sheet the smaller-area side counts as flipped
            var flip = new bool[n]; var seen = new bool[n]; var queue = new Queue<int>(); var comp = new List<int>();
            for (int s0 = 0; s0 < n; s0++)
            {
                if (!keep[s0] || seen[s0]) continue;
                comp.Clear(); seen[s0] = true; queue.Enqueue(s0); comp.Add(s0);
                while (queue.Count > 0)
                {
                    int x = queue.Dequeue();
                    for (int k = 0; k < cnt[x]; k++)
                    {
                        int y = nb[x * 3 + k];
                        if (seen[y]) continue;
                        seen[y] = true; flip[y] = flip[x] ^ same[x * 3 + k]; queue.Enqueue(y); comp.Add(y);
                    }
                }
                float fa = 0f, na = 0f;
                foreach (int i in comp) { if (flip[i]) fa += areas[i]; else na += areas[i]; }
                bool invert = fa > na;
                foreach (int i in comp) if (flip[i] != invert) { r.flipped++; r.flippedArea += areas[i]; }
            }
            return r;
        }

        // ------------------------------------------------------------------ inside-a-shell tests

        // the renderer's own triangles: of the 14 rays that hit the mesh, how many hit a back face first (culled when seen from the eye)
        static void MeshInsideTriangles(Vector3 eye, Vector3[] world, int[] t, bool mirrored, float max, out int hits, out int back, out bool upBack)
        {
            hits = 0; back = 0; upBack = false;
            for (int k = 0; k < MeshDirs.Length; k++)
            {
                var d = MeshDirs[k]; float best = max; int bi = -1;
                for (int i = 0; i + 2 < t.Length; i += 3)
                    if (BuildingAudit308.RayTri(eye, d, world[t[i]], world[t[i + 1]], world[t[i + 2]], out float h) && h > 1e-4f && h < best) { best = h; bi = i; }
                if (bi < 0) continue;
                hits++;
                // Unity front face = clockwise: cross(b - a, c - a) points to the front side; a ray leaving through it hits the back
                var nrm = Vector3.Cross(world[t[bi + 1]] - world[t[bi]], world[t[bi + 2]] - world[t[bi]]);
                if ((Vector3.Dot(nrm, d) > 0f) != mirrored) { back++; if (k == MeshUpDir) upBack = true; }
            }
        }

        // physics: a ray whose nearest hit exists only (or is nearer) with back faces on leaves a one-sided mesh collider from inside.
        // Downward rays are left out: they end on the front of the ground whatever stands over the eye (9 rays remain).
        static bool MeshInsideColliders(Vector3 eye, float max, float share, out string owner, out int hits, out int back)
        {
            owner = ""; hits = 0; back = 0; bool upBack = false;
            bool old = Physics.queriesHitBackfaces;
            try
            {
                for (int k = 0; k < MeshDirs.Length; k++)
                {
                    var d = MeshDirs[k];
                    if (d.y < -.1f) continue;
                    Physics.queriesHitBackfaces = false;
                    bool f = Physics.Raycast(eye, d, out var hf, max, ~0, QueryTriggerInteraction.Ignore);
                    Physics.queriesHitBackfaces = true;
                    bool b = Physics.Raycast(eye, d, out var hb, max, ~0, QueryTriggerInteraction.Ignore);
                    if (!b) continue;
                    hits++;
                    if (f && hb.distance >= hf.distance - .01f) continue;
                    back++;
                    if (k == MeshUpDir) { upBack = true; owner = BuildingAudit308.PathOf(hb.collider.transform); }
                }
            }
            finally { Physics.queriesHitBackfaces = old; }
            return hits > 0 && upBack && back >= share * hits;
        }

        /// <summary>True when the point lies under/inside a one-sided mesh collider shell: the surface straight overhead is seen
        /// from its back, and so are at least insideShare of the level and upward rays that hit anything (a still shot from there
        /// shows only the shell's inward-facing triangles). It does not say whether the place can be reached — a sealed rock shell
        /// (camera artefact) and a room under a one-sided roof (real defect) both answer true. For BuildingAudit308.Shots EyeFor:
        /// try the other candidate eyes first and, when only such an eye is left, keep it and say so in the shot note.</summary>
        internal static bool InsideShell308(Vector3 eye, out string owner)
        {
            owner = "";
            var stamp = File.Exists(MeshConfigFile) ? File.GetLastWriteTimeUtc(MeshConfigFile) : DateTime.MinValue;
            if (meshShellCfg == null || stamp != meshShellStamp) { meshShellCfg = MeshLoad(out _); meshShellStamp = stamp; }
            // without mesh308.json there is no threshold to judge by: the eye is not rejected
            return meshShellCfg != null && MeshInsideColliders(eye, meshShellCfg.rayMax, meshShellCfg.insideShare, out owner, out _, out _);
        }
        static MeshCfg308 meshShellCfg; static DateTime meshShellStamp;

        static Vector3[] MeshWorld(BuildingAudit308.MeshTris308 data, Matrix4x4 m)
        {
            var world = new Vector3[data.v.Length];
            for (int i = 0; i < world.Length; i++) world[i] = m.MultiplyPoint3x4(data.v[i]);
            return world;
        }

        // highest ground under a rim vertex: terrain from far above, or another collider's upward face up to rimWindow over the vertex
        static float MeshRimGround(Vector3 p, HashSet<Collider> own, float window)
        {
            float g = BuildingAudit308.TerrainTop(p);
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, p.y + window, p.z), Vector3.down, window + 40f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (own.Contains(h.collider) || h.normal.y < .3f) continue;
                if (float.IsNaN(g) || h.point.y > g) g = h.point.y;
            }
            return g;
        }

        static MeshRim308 MeshRim(string path, string mesh, string role, List<Vector3> rimLocal, Matrix4x4 m, HashSet<Collider> own, MeshCfg308 mc)
        {
            var rim = new MeshRim308 { path = path, mesh = mesh, role = role, vertices = rimLocal.Count, gapMin = float.NaN, gapMedian = float.NaN, gapMax = float.NaN };
            var gaps = new List<float>(rimLocal.Count);
            foreach (var l in rimLocal)
            {
                var p = m.MultiplyPoint3x4(l);
                float g = MeshRimGround(p, own, mc.rimWindow);
                if (float.IsNaN(g)) { rim.noGround++; continue; }
                float gap = p.y - g; gaps.Add(gap);
                if (gap > mc.sealTolerance) rim.above++;
                if (gap > mc.enterableGap) rim.enterable++;
                if (float.IsNaN(rim.gapMax) || gap > rim.gapMax) { rim.gapMax = gap; rim.worst = p; }
            }
            if (gaps.Count > 0) { gaps.Sort(); rim.gapMin = gaps[0]; rim.gapMedian = gaps[gaps.Count / 2]; }
            return rim;
        }

        // farthest sampled vertex of a lower LOD from the LOD0 surface (world space)
        static void MeshDeviation(Vector3[] v, Vector3[] v0, int[] t0, int samples, out float max, out float p95)
        {
            max = 0f; p95 = 0f;
            if (v.Length == 0 || t0.Length < 3) return;
            int stride = Mathf.Max(1, v.Length / Mathf.Max(1, samples));
            var d = new List<float>();
            for (int i = 0; i < v.Length; i += stride)
            {
                float best = float.MaxValue;
                for (int k = 0; k + 2 < t0.Length; k += 3)
                {
                    float s = (BuildingAudit308.ClosestOnTri(v[i], v0[t0[k]], v0[t0[k + 1]], v0[t0[k + 2]]) - v[i]).sqrMagnitude;
                    if (s < best) best = s;
                }
                d.Add(Mathf.Sqrt(best));
            }
            d.Sort(); max = d[d.Count - 1]; p95 = d[Mathf.Min(d.Count - 1, Mathf.FloorToInt(d.Count * .95f))];
        }

        // how far the visible LOD0 stands outside (front side of) its collider: a camera pressed against the collider there can dip
        // inside the one-sided visible shell and see through it. Sampled LOD0 vertices against every collider triangle (world space).
        static void MeshProtrusion(Vector3[] v0, Vector3[] cw, int[] ct, bool mirrored, int samples, float warn, out float max, out int over)
        {
            max = 0f; over = 0;
            if (v0 == null || v0.Length == 0 || ct.Length < 3) return;
            int stride = Mathf.Max(1, v0.Length / Mathf.Max(1, samples));
            for (int i = 0; i < v0.Length; i += stride)
            {
                float best = float.MaxValue; int bk = -1; Vector3 bq = default;
                for (int k = 0; k + 2 < ct.Length; k += 3)
                {
                    var q = BuildingAudit308.ClosestOnTri(v0[i], cw[ct[k]], cw[ct[k + 1]], cw[ct[k + 2]]);
                    float s = (q - v0[i]).sqrMagnitude;
                    if (s < best) { best = s; bk = k; bq = q; }
                }
                if (bk < 0) continue;
                var nrm = Vector3.Cross(cw[ct[bk + 1]] - cw[ct[bk]], cw[ct[bk + 2]] - cw[ct[bk]]);
                if ((Vector3.Dot(v0[i] - bq, nrm) > 0f) == mirrored) continue;   // behind the collider surface
                float d = Mathf.Sqrt(best);
                if (d > max) max = d;
                if (warn > 0f && d > warn) over++;
            }
        }

        static MeshRow308 MeshRow(BuildingAudit308.Config308 cfg, string path, Mesh mesh, string role, int lod, MeshTopo308 topo, Bounds worldBounds)
        {
            string asset = mesh != null ? AssetDatabase.GetAssetPath(mesh) : "";
            return new MeshRow308
            {
                path = path, mesh = mesh != null ? asset + "#" + mesh.name : "", role = role, lod = lod, protectedAsset = cfg.ProtectedAsset(asset),
                vertices = topo.vertices, welded = topo.welded, triangles = topo.triangles, zeroArea = topo.zeroArea, flipped = topo.flipped,
                boundaryEdges = topo.boundaryEdges, loops = topo.loops, nonManifold = topo.nonManifold, area = topo.area, flippedArea = topo.flippedArea,
                maxEdge = topo.maxEdge, boundsMin = worldBounds.min, boundsMax = worldBounds.max,
            };
        }

        static Bounds MeshBounds(Vector3[] world)
        {
            if (world.Length == 0) return new Bounds();
            var b = new Bounds(world[0], Vector3.zero);
            for (int i = 1; i < world.Length; i++) b.Encapsulate(world[i]);
            return b;
        }

        // the renderers of an object by LOD index (LODGroup order; without a LODGroup every renderer is LOD0)
        static List<(MeshRenderer r, int lod)> MeshLods(Transform owner)
        {
            var list = new List<(MeshRenderer, int)>();
            var group = owner.GetComponent<LODGroup>();
            if (group != null)
            {
                var lods = group.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers)
                        if (r is MeshRenderer mr && list.All(x => x.Item1 != mr)) list.Add((mr, i));
            }
            foreach (var mr in owner.GetComponentsInChildren<MeshRenderer>(true))
                if (list.All(x => x.Item1 != mr)) list.Add((mr, group != null ? -1 : 0));
            return list;
        }

        // ------------------------------------------------------------------ eyes

        static MeshEye308 MeshEye(string name, Vector3 eye, Vector3 target, bool reseat, MeshCfg308 mc, List<(Vector3[] world, int[] t, bool mirrored)> ownerLod0)
        {
            var e = new MeshEye308 { name = name, target = target };
            // a given eye (the still's) is measured from just above it — a shell may hang low over it; a replacement eye is re-seated,
            // so its ray starts eyeProbeLift higher to forgive an offline height that is a little low
            if (Physics.Raycast(new Vector3(eye.x, eye.y + (reseat ? mc.eyeProbeLift : .2f), eye.z), Vector3.down, out var gh, 60f, ~0, QueryTriggerInteraction.Ignore))
            {
                e.ground = gh.point.y;
                if (reseat) eye.y = gh.point.y + mc.eyeHeight;
            }
            e.eye = eye; e.overGround = float.IsNaN(e.ground) ? float.NaN : eye.y - e.ground;
            e.insideColliders = MeshInsideColliders(eye, mc.rayMax, mc.insideShare, out string owner, out e.colliderHits, out e.colliderBack);
            e.owner = owner;
            foreach (var o in ownerLod0)
            {
                MeshInsideTriangles(eye, o.world, o.t, o.mirrored, mc.rayMax, out int hits, out int back, out bool up);
                if (hits > 0 && up && back >= mc.insideShare * hits) e.insideOwner = true;
            }
            e.overlap = Physics.CheckSphere(eye, .3f, ~0, QueryTriggerInteraction.Ignore);
            var aim = target;
            e.clear = !Physics.Linecast(eye, aim, out var block, ~0, QueryTriggerInteraction.Ignore) || Vector3.Distance(block.point, aim) <= 4f;
            var foot = float.IsNaN(e.ground) ? eye : new Vector3(eye.x, e.ground, eye.z);
            e.navMesh = NavMesh.SamplePosition(foot, out _, Mathf.Max(.1f, mc.navRadius), NavMesh.AllAreas);
            e.state = float.IsNaN(e.ground) ? "no-ground" : (e.insideColliders || e.insideOwner) ? "inside" : e.overlap ? "blocked" : "ok";
            return e;
        }

        static string MeshShotCommand(BuildingAudit308.Config308 cfg, MeshEye308 e)
            => "shot:" + e.name + ":" + BuildingAudit308.V(e.eye) + ":" + BuildingAudit308.V(e.target) + ":fov=" + cfg.shots.fov.ToString("0.#", Inv) + ":w=" + cfg.shots.w + ":h=" + cfg.shots.h + ":hideplayer";

        // ------------------------------------------------------------------ probe

        static MeshSiteReport308 MeshProbeSite(BuildingAudit308.Config308 cfg, MeshCfg308 mc, Scene scene, MeshSite308 site, BuildingAudit308.MeshCache308 cache, bool withRows)
        {
            var rep = new MeshSiteReport308 { id = site.id, still = site.still, defect = site.defect, expect = site.expect };
            Vector3 eye = BuildingAudit308.Vec(site.eye), target = BuildingAudit308.Vec(site.target);
            var lod0 = new List<(Vector3[] world, int[] t, bool mirrored)>();
            bool broken = false, missing = false; float colliderGap = float.NaN, visualGap = float.NaN; int colliderRims = 0;
            var brokenWhy = new List<string>();
            foreach (string key in site.owners.Concat(site.also))
            {
                bool isOwner = site.owners.Contains(key);
                var owner = BuildingAudit308.Resolve(scene, key);
                if (owner == null) { rep.notes.Add("missing object " + key); if (isOwner) missing = true; continue; }
                if (isOwner && cfg.ProtectedRoot(key.Split('/')[0])) rep.protectedOwner = true;
                var own = new HashSet<Collider>(owner.GetComponentsInChildren<Collider>(true));
                Vector3[] w0 = null; int[] t0 = null;
                foreach (var (r, lod) in MeshLods(owner).OrderBy(x => x.lod < 0 ? int.MaxValue : x.lod))
                {
                    var mesh = BuildingAudit308.MeshOf(r);
                    var data = cache.Get(mesh);
                    if (data == null) { rep.notes.Add("unreadable mesh on " + BuildingAudit308.PathOf(r.transform)); continue; }
                    var m = r.localToWorldMatrix; var world = MeshWorld(data, m); bool mirrored = m.determinant < 0f;
                    if (isOwner && cfg.ProtectedAsset(AssetDatabase.GetAssetPath(mesh))) rep.protectedOwner = true;
                    if (lod == 0 && w0 == null) { w0 = world; t0 = data.t; if (isOwner) lod0.Add((world, data.t, mirrored)); }
                    if (!withRows) continue;
                    var topo = MeshTopo(data.v, data.t, mc.weld);
                    var row = MeshRow(cfg, BuildingAudit308.PathOf(r.transform), mesh, isOwner ? "lod" : "also", lod, topo, MeshBounds(world));
                    if (isOwner)
                    {
                        MeshInsideTriangles(eye, world, data.t, mirrored, mc.rayMax, out row.rayHits, out row.rayBack, out row.upBack);
                        row.inside = row.rayHits > 0 && row.upBack && row.rayBack >= mc.insideShare * row.rayHits;
                    }
                    if (lod > 0 && w0 != null) MeshDeviation(world, w0, t0, mc.deviationSamples, out row.deviationMax, out row.deviationP95);
                    rep.rows.Add(row);
                    var why = new List<string>();
                    if (topo.nonManifold > 0) why.Add(topo.nonManifold + " non-manifold edge(s)");
                    if (topo.area > 0f && topo.flippedArea / topo.area > mc.flipShareFail) why.Add("winding flips " + (topo.flippedArea / topo.area).ToString("P1", Inv) + " of the area");
                    if (row.deviationMax > mc.deviationFail) why.Add("LOD" + lod + " leaves LOD0 by " + row.deviationMax.ToString("F2", Inv) + " m");
                    if (why.Count > 0)
                    {
                        if (isOwner) { broken = true; brokenWhy.Add(r.name + ": " + string.Join(", ", why)); }
                        else rep.notes.Add("WARN " + BuildingAudit308.PathOf(r.transform) + ": " + string.Join(", ", why));
                    }
                    if (isOwner && lod == 0)
                    {
                        var rim = MeshRim(row.path, row.mesh, "lod0", topo.rim, m, own, mc);
                        rep.rims.Add(rim);
                        if (!float.IsNaN(rim.gapMax) && (float.IsNaN(visualGap) || rim.gapMax > visualGap)) visualGap = rim.gapMax;
                    }
                }
                if (!withRows || !isOwner) continue;
                foreach (var c in own.OfType<MeshCollider>())
                {
                    // only a collider that really blocks counts as a seal
                    if (c.sharedMesh == null || !c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy) continue;
                    var data = cache.Get(c.sharedMesh);
                    if (data == null) { rep.notes.Add("unreadable collider mesh on " + BuildingAudit308.PathOf(c.transform)); continue; }
                    var m = c.transform.localToWorldMatrix; var world = MeshWorld(data, m);
                    var topo = MeshTopo(data.v, data.t, mc.weld);
                    var row = MeshRow(cfg, BuildingAudit308.PathOf(c.transform), c.sharedMesh, "collider", -1, topo, MeshBounds(world));
                    MeshInsideTriangles(eye, world, data.t, m.determinant < 0f, mc.rayMax, out row.rayHits, out row.rayBack, out row.upBack);
                    row.inside = row.rayHits > 0 && row.upBack && row.rayBack >= mc.insideShare * row.rayHits;
                    MeshProtrusion(w0, world, data.t, m.determinant < 0f, mc.deviationSamples * 10, mc.protrudeWarn, out row.protrudeMax, out row.protrudeOver);
                    if (mc.protrudeWarn > 0f && row.protrudeMax > mc.protrudeWarn)
                        rep.notes.Add("INFO the visible LOD0 stands up to " + row.protrudeMax.ToString("F2", Inv) + " m outside " + c.name + " (" + row.protrudeOver + " sampled vertices over " + mc.protrudeWarn.ToString("F2", Inv) + " m): a camera pressed against the rock there can dip inside the one-sided shell");
                    rep.rows.Add(row);
                    var rim = MeshRim(row.path, row.mesh, "collider", topo.rim, m, own, mc);
                    rep.rims.Add(rim); colliderRims++;
                    if (!float.IsNaN(rim.gapMax) && (float.IsNaN(colliderGap) || rim.gapMax > colliderGap)) colliderGap = rim.gapMax;
                }
            }
            rep.stillEye = MeshEye(site.still, eye, target, false, mc, lod0);
            foreach (var s in site.reshoot)
            {
                var e = MeshEye(s.name, BuildingAudit308.Vec(s.eye), BuildingAudit308.Vec(s.target), true, mc, lod0);
                e.note = s.note; e.command = MeshShotCommand(cfg, e);
                rep.reshoot.Add(e);
            }
            if (!withRows) return rep;
            bool inside = rep.stillEye.insideOwner || rep.rows.Any(q => q.role == "lod" && q.lod == 0 && q.inside);
            bool open = !float.IsNaN(colliderGap) && colliderGap > mc.enterableGap;
            string gaps = "rim gap max: LOD0 " + BuildingAudit308.F(visualGap, "F2") + " m, collider " + BuildingAudit308.F(colliderGap, "F2") + " m (" + colliderRims + " collider mesh(es))";
            if (missing) { rep.verdict = "UNEXPLAINED"; rep.detail = "owner object missing in this scene"; }
            else if (broken) { rep.verdict = "REAL (broken mesh)"; rep.detail = string.Join(" | ", brokenWhy) + "; " + gaps; }
            else if (inside && colliderRims == 0) { rep.verdict = "REAL (open rim)"; rep.detail = "the still's eye is inside the one-sided shell and the owner has no mesh collider — the inside can be walked into; " + gaps; }
            else if (inside && float.IsNaN(colliderGap)) { rep.verdict = "UNEXPLAINED"; rep.detail = "the still's eye is inside the one-sided shell but no collider rim vertex has ground under it — the seal cannot be judged; " + gaps; }
            else if (inside && open) { rep.verdict = "REAL (open rim)"; rep.detail = "the still's eye is inside the one-sided shell and the collider rim stands " + colliderGap.ToString("F2", Inv) + " m over the ground (> " + mc.enterableGap.ToString("F2", Inv) + " m) — the inside can be reached; " + gaps; }
            else if (inside) { rep.verdict = "FALSE-POSITIVE"; rep.detail = "the still's eye is inside the sealed one-sided shell (camera artefact, not reachable on foot); meshes intact; " + gaps; }
            else { rep.verdict = "UNEXPLAINED"; rep.detail = "meshes intact and the still's eye is outside the owner shell — the owner in mesh308.json is wrong or the scene changed; " + gaps; }
            if (!float.IsNaN(visualGap) && visualGap > mc.sealTolerance) rep.notes.Add("WARN the visible rim stands up to " + visualGap.ToString("F2", Inv) + " m over the ground (a slit under the shell)");
            int hanging = rep.rims.Sum(q => q.noGround);
            if (hanging > 0) rep.notes.Add("WARN " + hanging + " rim vertex(es) have no ground under them (gap unknown there)");
            if (rep.protectedOwner) rep.notes.Add("owner object or mesh asset is protected (Watershed295 / Reworld292 / MountainTrail285): report only");
            rep.asExpected = string.IsNullOrEmpty(site.expect) || rep.verdict.StartsWith(site.expect, StringComparison.OrdinalIgnoreCase);
            return rep;
        }

        static string MeshProbe(string alias, string ids)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err);
            if (cfg == null) return "refused: " + err;
            var mc = MeshLoad(out err);
            if (mc == null) return "refused: " + err;
            var sites = MeshSelect(mc, ids, out err);
            if (sites == null) return err;
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            var report = new MeshReport308 { alias = alias, scene = scene.path, utc = BuildingAudit308.Utc(), sceneSha = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path)), quality = BuildingAudit308.QualityName(), configVersion = mc.version, finding = mc.finding };
            try
            {
                Physics.SyncTransforms();
                var cache = new BuildingAudit308.MeshCache308();
                foreach (var site in sites)
                {
                    var rep = MeshProbeSite(cfg, mc, scene, site, cache, true);
                    report.sites.Add(rep);
                    if (rep.verdict.StartsWith("FALSE", StringComparison.Ordinal)) report.falsePositive++; else if (rep.verdict.StartsWith("REAL", StringComparison.Ordinal)) report.real++; else report.unexplained++;
                    if (!rep.asExpected) report.unexpected++;
                }
            }
            finally { GoBack(previous, scene, opened, true); }
            Directory.CreateDirectory(FixDir);
            string file = Path.Combine(FixDir, "mesh-probe-" + alias + "-" + report.utc + ".json");
            File.WriteAllText(file, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            var sb = new StringBuilder("mesh probe " + alias + " sha " + report.sceneSha + ": " + report.falsePositive + " false positive, " + report.real + " real, " + report.unexplained + " unexplained" + (report.unexpected > 0 ? " — " + report.unexpected + " NOT AS EXPECTED" : " (as expected)") + "\n");
            foreach (var s in report.sites)
            {
                sb.AppendLine("  " + s.id + " " + s.verdict + ": " + s.detail);
                foreach (var r in s.rows.Where(q => q.role != "also"))
                    sb.AppendLine("     " + (r.role == "collider" ? "collider" : "LOD" + r.lod) + " " + Path.GetFileName(r.mesh) + ": " + r.triangles + " tris, zero-area " + r.zeroArea + ", flipped " + r.flipped + " (" + r.flippedArea.ToString("F2", Inv) + " of " + r.area.ToString("F0", Inv) + " m2), rim loops " + r.loops + ", non-manifold " + r.nonManifold
                                  + (r.deviationMax >= 0f ? ", off LOD0 max " + r.deviationMax.ToString("F2", Inv) + " m" : "") + (r.role == "collider" && r.protrudeMax >= 0f ? ", LOD0 outside it max " + r.protrudeMax.ToString("F2", Inv) + " m" : "") + "; eye rays " + r.rayBack + "/" + r.rayHits + " back-face first" + (r.upBack ? " (up too)" : "") + (r.protectedAsset ? " [protected asset]" : ""));
                foreach (var m in s.rims)
                    sb.AppendLine("     rim " + m.role + ": " + m.vertices + " vertices, gap min " + BuildingAudit308.F(m.gapMin, "F2") + " median " + BuildingAudit308.F(m.gapMedian, "F2") + " max " + BuildingAudit308.F(m.gapMax, "F2") + " m, " + m.above + " above the ground, " + m.enterable + " enterable" + (m.noGround > 0 ? ", " + m.noGround + " without ground" : ""));
                sb.AppendLine("     still eye " + BuildingAudit308.V(s.stillEye.eye) + ": " + BuildingAudit308.F(s.stillEye.overGround, "F2") + " m over the ground, colliders " + s.stillEye.colliderBack + "/" + s.stillEye.colliderHits + " back-face first" + (s.stillEye.insideColliders ? " = inside " + s.stillEye.owner : "") + ", NavMesh within " + mc.navRadius.ToString("0.#", Inv) + " m: " + (s.stillEye.navMesh ? "yes" : "no"));
                foreach (var e in s.reshoot) sb.AppendLine("     reshoot " + e.name + " " + e.state + (e.clear ? "" : " (line of sight blocked)") + ": " + e.command);
                foreach (var n in s.notes) sb.AppendLine("     · " + n);
            }
            sb.AppendLine("-> " + file);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ eye:<alias>:<x,y,z>

        static string MeshEyeCommand(string alias, string xyz)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err);
            if (cfg == null) return "refused: " + err;
            var mc = MeshLoad(out err);
            if (mc == null) return "refused: " + err;
            var p = xyz.Split(',');
            if (p.Length != 3 || !float.TryParse(p[0], System.Globalization.NumberStyles.Float, Inv, out float x) || !float.TryParse(p[1], System.Globalization.NumberStyles.Float, Inv, out float y) || !float.TryParse(p[2], System.Globalization.NumberStyles.Float, Inv, out float z))
                return "refused: eye:<alias>:<x,y,z> (got " + xyz + ")";
            var eye = new Vector3(x, y, z);
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            try
            {
                Physics.SyncTransforms();
                var sb = new StringBuilder();
                bool inside = MeshInsideColliders(eye, mc.rayMax, mc.insideShare, out string owner, out int hits, out int back);
                sb.AppendLine("eye " + BuildingAudit308.V(eye) + " in " + alias + ": colliders " + back + "/" + hits + " rays back-face first -> " + (inside ? "INSIDE " + owner : "outside"));
                var cache = new BuildingAudit308.MeshCache308(); int shells = 0, tested = 0;
                foreach (var r in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(false)))
                {
                    if (!r.enabled || !r.bounds.Contains(eye)) continue;
                    var data = cache.Get(BuildingAudit308.MeshOf(r));
                    if (data == null) continue;
                    tested++;
                    var m = r.localToWorldMatrix;
                    MeshInsideTriangles(eye, MeshWorld(data, m), data.t, m.determinant < 0f, mc.rayMax, out int h, out int b, out bool up);
                    if (h > 0 && up && b >= mc.insideShare * h) { shells++; sb.AppendLine("  inside renderer shell " + BuildingAudit308.PathOf(r.transform) + " (" + b + "/" + h + " rays back-face first)" + (cfg.ProtectedRoot(r.transform.root.name) || cfg.ProtectedAsset(AssetDatabase.GetAssetPath(BuildingAudit308.MeshOf(r))) ? " [protected]" : "")); }
                }
                sb.AppendLine("  " + tested + " renderer(s) around the point tested, " + shells + " shell(s) contain it");
                return sb.ToString();
            }
            finally { GoBack(previous, scene, opened, true); }
        }

        // ------------------------------------------------------------------ reshoot:<alias>

        static string MeshReshoot(string alias, string ids)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err);
            if (cfg == null) return "refused: " + err;
            var mc = MeshLoad(out err);
            if (mc == null) return "refused: " + err;
            var sites = MeshSelect(mc, ids, out err);
            if (sites == null) return err;
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            var list = new BuildingAudit308.ShotList308 { scene = scene.path, alias = alias, audit = "mesh308 " + mc.version, utc = BuildingAudit308.Utc(), quality = BuildingAudit308.QualityName(), budget = 0 };
            var sb = new StringBuilder();
            try
            {
                Physics.SyncTransforms();
                var cache = new BuildingAudit308.MeshCache308();
                foreach (var site in sites)
                {
                    var rep = MeshProbeSite(cfg, mc, scene, site, cache, false);
                    foreach (var e in rep.reshoot)
                    {
                        if (e.state != "ok") { list.notes.Add(e.name + " skipped: " + e.state + (e.owner.Length > 0 ? " " + e.owner : "")); sb.AppendLine("  skipped " + e.name + ": " + e.state + (e.owner.Length > 0 ? " " + e.owner : "")); continue; }
                        list.shots.Add(new BuildingAudit308.Shot308
                        {
                            name = e.name, group = "mesh308_" + site.id, kind = "reshoot", command = e.command, note = (e.clear ? "" : "line of sight partly blocked; ") + "replaces " + site.still + " (its eye is inside a one-sided shell); " + e.note,
                            priority = 1, eye = e.eye, target = e.target, fov = cfg.shots.fov, w = cfg.shots.w, h = cfg.shots.h, findings = new[] { "mesh308|" + site.id + "|" + string.Join(" | ", site.owners) + "|" + BuildingAudit308.V(e.target) },
                        });
                        sb.AppendLine("  " + e.command);
                    }
                }
            }
            finally { GoBack(previous, scene, opened, true); }
            list.planned = list.shots.Count; list.findingsToCover = sites.Length; list.findingsCovered = sites.Count(s => list.shots.Any(x => x.group == "mesh308_" + s.id));
            string dir = Path.Combine(BuildingAudit308.Folder, "Shots"); Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "shots-mesh308-" + alias + ".json");
            File.WriteAllText(file, JsonUtility.ToJson(list, true), new UTF8Encoding(false));
            return "mesh reshoot " + alias + ": " + list.shots.Count + " shot(s) for " + list.findingsCovered + "/" + sites.Length + " site(s)\n" + sb + "-> " + file + "\nnext (with " + list.scene + " open and saved — the sweep refuses any other open scene): python Tools/Unity/buildingaudit308_sweep.py --run mesh308 --shots " + file;
        }
    }
}
