using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // RoofNav308 - the read-only half: scan (pieces + verdicts), plan (boxes), check (after the bake). The arithmetic of Verdict / Plan /
    // TriInBox / the check rows is the one Tools/Unity/Stage308_world4/roofnav308_offline.py runs on the NavMesh asset's own polygons and
    // on a synthetic world (its selftest + mutations). What only the editor has, and where the two differ (README "거울과 다른 곳"):
    //   terrain      physical (BuildingAudit308.TerrainTop)            | offline: height_p1b, 4 m grid
    //   unit frames  the unit's yaw + renderer bounds                  | offline: axis-aligned boxes of the audit scan
    //   reach        NavMesh.CalculatePath to ground NavMesh on a ring | offline: the piece's connected component holds a ground polygon
    //   evidence     scene components + NavMeshAgents + routes         | offline: content asset points / feet + routes
    //   roof         top of the unit's DRAWN mesh, vertex by vertex    | offline: pitch only (real meshes for the consignee house alone)
    //   box depth    lowered to the physical surface under the piece   | offline: NavMesh heights (real meshes for the consignee house)
    public static partial class RoofNav308
    {
        sealed class Frame
        {
            public float ox, oz, cos, sin, x0, x1, z0, z1, yaw;
            public void Local(float x, float z, out float lx, out float lz) { float dx = x - ox, dz = z - oz; lx = dx * cos - dz * sin; lz = dx * sin + dz * cos; }
            public Vector3 World(float lx, float y, float lz) => new Vector3(ox + lx * cos + lz * sin, y, oz - lx * sin + lz * cos);
            public bool Holds(float x, float z, float m) { Local(x, z, out float lx, out float lz); return lx >= x0 - m && lx <= x1 + m && lz >= z0 - m && lz <= z1 + m; }
            public float Area => (x1 - x0) * (z1 - z0);
        }
        sealed class UnitRef { public BuildingAudit308.Unit308 u; public Frame f; public string key; public List<Vector3> ring; public bool careful; }
        sealed class Piece
        {
            public UnitRef unit; public List<int> tris = new List<int>();
            public float area, y0 = float.PositiveInfinity, y1 = float.NegativeInfinity, above0 = float.PositiveInfinity, above1 = float.NegativeInfinity, slope, pitch, tokenShare = float.NaN, geoShare = float.NaN;
            public Vector3 centre; public bool reachable, reachJudged, warn, candidate; public int row = -1; public string reachNote = "";
            public List<string> evidence = new List<string>(); public string verdict = "KEEP", why = "";
        }
        sealed class Box
        {
            public string name, unit, cell, why = ""; public Piece piece; public Frame f; public float lx0, lx1, lz0, lz1, y0, y1, yaw, area, surfaceLow = float.NaN; public Vector3 centre, size; public bool blocked; public List<int> tris;
        }
        sealed class ScanResult
        {
            public string alias, scenePath, sceneSha, navSha, dataSha; public int triangles, units, unmeasured; public Approval approval;
            public Vector3[] v; public int[] idx; public List<Piece> pieces = new List<Piece>(); public Dictionary<string, float> ground = new Dictionary<string, float>(StringComparer.Ordinal);
            public Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>(); public List<string> notes = new List<string>();
            public Vector3 A(int t) => v[idx[t * 3]]; public Vector3 B(int t) => v[idx[t * 3 + 1]]; public Vector3 C(int t) => v[idx[t * 3 + 2]];
            public float Slope(int t) { var n = Vector3.Cross(B(t) - A(t), C(t) - A(t)); return n.sqrMagnitude > 1e-12f ? Vector3.Angle(n.y < 0 ? -n : n, Vector3.up) : 0f; }
        }
        const float GridCell = 16f;
        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
        static float AreaXZ(Vector3 a, Vector3 b, Vector3 c) => Mathf.Abs((b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)) * .5f;

        static Frame FrameOf(BuildingAudit308.Unit308 u)
        {
            float yaw = u.t.eulerAngles.y, r = yaw * Mathf.Deg2Rad;
            var f = new Frame { ox = u.bounds.center.x, oz = u.bounds.center.z, cos = Mathf.Cos(r), sin = Mathf.Sin(r), yaw = yaw, x0 = float.PositiveInfinity, x1 = float.NegativeInfinity, z0 = float.PositiveInfinity, z1 = float.NegativeInfinity };
            var boxes = u.renderers.Length > 0 ? u.renderers.Select(x => x.bounds) : u.colliders.Select(x => x.bounds);
            foreach (var b in boxes)
                for (int i = 0; i < 4; i++)
                {
                    f.Local(i % 2 == 0 ? b.min.x : b.max.x, i < 2 ? b.min.z : b.max.z, out float lx, out float lz);
                    f.x0 = Mathf.Min(f.x0, lx); f.x1 = Mathf.Max(f.x1, lx); f.z0 = Mathf.Min(f.z0, lz); f.z1 = Mathf.Max(f.z1, lz);
                }
            if (float.IsInfinity(f.x0)) { f.x0 = f.z0 = -u.bounds.extents.x; f.x1 = f.z1 = u.bounds.extents.x; }
            return f;
        }

        // ------------------------------------------------------------------ scan

        static ScanResult Scan(JObject d, Scene scene, string alias)
        {
            Physics.SyncTransforms();
            var S = d["scan"]; float high = Num(S, "high_m"), margin = Num(S, "unit_margin_m"), weld = Mathf.Max(1e-3f, Num(S, "weld_m")), band = Num(S, "ground_band_m");
            var tri = NavMesh.CalculateTriangulation();
            if (tri.indices == null || tri.indices.Length < 3) throw new Refuse("no NavMesh is loaded in " + scene.path + " (NavMesh.CalculateTriangulation is empty) - is Architecture296_Navigation in the scene and enabled?");
            var r = new ScanResult { alias = alias, scenePath = scene.path, sceneSha = BuildingAudit308.Sha(PostLedger308.Abs(scene.path)), v = tri.vertices, idx = tri.indices, triangles = tri.indices.Length / 3 };
            r.navSha = NavSha(d); r.approval = LoadApproval(d, alias, r.navSha);
            // building units: the audit's roots and rule, plus the extra roots of the data
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) throw new Refuse("BuildingAudit308 config: " + err);
            var extra = Arr(S, "extra_roots").Select(x => new BuildingAudit308.Root308 { name = Str(x, "name"), depth = Mathf.Max(1, (int)Num(x, "depth")) }).Where(x => cfg.roots.All(k => k.name != x.name)).ToArray();
            cfg.roots = cfg.roots.Concat(extra).ToArray();
            var names = cfg.roots.Select(k => k.name).Where(n => !cfg.excludedRoots.Contains(n)).Distinct().ToArray();
            var carefulRoots = new HashSet<string>(Arr(d["roof"], "careful_roots").Select(x => x.Value<string>()), StringComparer.Ordinal);
            var units = BuildingAudit308.UnitsOf(scene, cfg, names).Where(u => u.building && u.hasBounds && u.t != null && !Preview(u.t))
                .Select(u => new UnitRef { u = u, key = u.key, f = FrameOf(u), careful = u.readOnly || carefulRoots.Contains(u.root) }).ToList();
            r.units = units.Count;
            var ugrid = new Dictionary<long, List<UnitRef>>();
            foreach (var u in units)
            {
                var b = u.u.bounds; b.Expand(new Vector3(2f * margin + 2f, 0f, 2f * margin + 2f));
                for (int gx = Mathf.FloorToInt(b.min.x / 32f); gx <= Mathf.FloorToInt(b.max.x / 32f); gx++)
                    for (int gz = Mathf.FloorToInt(b.min.z / 32f); gz <= Mathf.FloorToInt(b.max.z / 32f); gz++)
                    { long k = Key(gx, gz); if (!ugrid.TryGetValue(k, out var l)) ugrid[k] = l = new List<UnitRef>(); l.Add(u); }
            }
            UnitRef UnitAt(float x, float z)
            {
                if (!ugrid.TryGetValue(Key(Mathf.FloorToInt(x / 32f), Mathf.FloorToInt(z / 32f)), out var l)) return null;
                UnitRef best = null; foreach (var u in l) if (u.f.Holds(x, z, margin) && (best == null || u.f.Area < best.f.Area)) best = u;
                return best;
            }
            // every triangle into the 16 m grid (plan / check: what else reaches into a box); the unit of each triangle; heights over the terrain
            int nt = r.triangles; var unitOf = new UnitRef[nt]; var isHigh = new bool[nt]; var terrain = new Dictionary<int, float>();
            float Over(int vi) { if (!terrain.TryGetValue(vi, out float t)) terrain[vi] = t = BuildingAudit308.TerrainTop(r.v[vi]); return r.v[vi].y - t; }
            for (int t = 0; t < nt; t++)
            {
                Vector3 a = r.A(t), b = r.B(t), c = r.C(t);
                float minx = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxx = Mathf.Max(a.x, Mathf.Max(b.x, c.x)), minz = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), maxz = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                for (int gx = Mathf.FloorToInt(minx / GridCell); gx <= Mathf.FloorToInt(maxx / GridCell); gx++)
                    for (int gz = Mathf.FloorToInt(minz / GridCell); gz <= Mathf.FloorToInt(maxz / GridCell); gz++)
                    { long k = Key(gx, gz); if (!r.grid.TryGetValue(k, out var l)) r.grid[k] = l = new List<int>(); l.Add(t); }
                var u = UnitAt((a.x + b.x + c.x) / 3f, (a.z + b.z + c.z) / 3f); unitOf[t] = u; if (u == null) continue;
                float oa = Over(r.idx[t * 3]), ob = Over(r.idx[t * 3 + 1]), oc = Over(r.idx[t * 3 + 2]);
                if (float.IsNaN(oa) || float.IsNaN(ob) || float.IsNaN(oc)) { r.unmeasured++; continue; }
                if (oa >= high && ob >= high && oc >= high) isHigh[t] = true;
                else if (oa < high && ob < high && oc < high) { r.ground.TryGetValue(u.key, out float g); r.ground[u.key] = g + AreaXZ(a, b, c); }
            }
            if (r.unmeasured > 0) r.notes.Add(r.unmeasured + " triangle(s) over a building unit have no terrain under a vertex (not judged)");
            // pieces: high triangles of one unit joined through welded vertices
            var parent = new int[nt]; for (int t = 0; t < nt; t++) parent[t] = t;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            var seen = new Dictionary<(long, long, long, UnitRef), int>();
            for (int t = 0; t < nt; t++)
            {
                if (!isHigh[t]) continue;
                for (int k = 0; k < 3; k++)
                {
                    var p = r.v[r.idx[t * 3 + k]]; var key = ((long)Mathf.Round(p.x / weld), (long)Mathf.Round(p.y / weld), (long)Mathf.Round(p.z / weld), unitOf[t]);
                    if (seen.TryGetValue(key, out int other)) parent[Find(t)] = Find(other); else seen[key] = t;
                }
            }
            var byRoot = new Dictionary<int, Piece>();
            for (int t = 0; t < nt; t++)
            {
                if (!isHigh[t]) continue;
                int root = Find(t); if (!byRoot.TryGetValue(root, out var p)) byRoot[root] = p = new Piece { unit = unitOf[t] };
                p.tris.Add(t);
            }
            float minPiece = Num(S, "min_piece_m2"), pitchMin = Num(d["roof"]["geo"], "pitch_min_deg");
            foreach (var p in byRoot.Values)
            {
                Vector3 sum = Vector3.zero; float slopeSum = 0f, pitched = 0f;
                foreach (int t in p.tris)
                {
                    Vector3 a = r.A(t), b = r.B(t), c = r.C(t); float ar = AreaXZ(a, b, c), sl = r.Slope(t); p.area += ar; sum += (a + b + c) / 3f * ar; slopeSum += sl * ar; if (sl >= pitchMin) pitched += ar;
                    for (int k = 0; k < 3; k++) { int vi = r.idx[t * 3 + k]; float y = r.v[vi].y, o = Over(vi); p.y0 = Mathf.Min(p.y0, y); p.y1 = Mathf.Max(p.y1, y); p.above0 = Mathf.Min(p.above0, o); p.above1 = Mathf.Max(p.above1, o); }
                }
                if (p.area < minPiece) continue;
                p.centre = sum / p.area; p.slope = slopeSum / p.area; p.pitch = pitched / p.area; r.pieces.Add(p);
            }
            r.pieces = r.pieces.OrderBy(p => p.unit.key, StringComparer.Ordinal).ThenByDescending(p => p.area).ToList();
            Evidence(d, scene, cfg, r, UnitAt);
            Reach(d, r, band);
            RoofShares(d, cfg, r);
            foreach (var p in r.pieces) Verdict(d, p, r.approval);
            if (r.approval.exists && !r.approval.live) r.notes.Add("approval file IGNORED (written for NavMesh sha '" + r.approval.navSha + "', loaded " + Short(r.navSha) + ")");
            if (r.approval.live) foreach (var u in r.approval.units.Where(x => !r.approval.used.Contains(x))) r.notes.Add("approval names " + u + ": no approvable roof candidate there (ignored - an approval only switches on what the rule calls a candidate; a careful root also needs the 'careful' list)");
            return r;
        }

        // what stands or walks on a piece: route / path samples on the physical surface, NavMeshAgents, the listed component types
        static void Evidence(JObject d, Scene scene, BuildingAudit308.Config308 cfg, ScanResult r, Func<float, float, UnitRef> unitAt)
        {
            var E = d["evidence"]; float xz = Num(E, "xz_m"), ym = Num(E, "y_m"), step = Mathf.Max(.25f, Num(E, "corridor_step_m")), dropUp = Num(E, "surface_probe_up_m"), dropLen = Num(E, "surface_probe_len_m"); var pts = new List<(string id, Vector3 p)>();
            if (Flag(E, "corridors", true))
                foreach (var c in BuildingAudit308.Corridors(scene, cfg))
                    for (int i = 1; i < c.pts.Length; i++)
                    {
                        int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(c.pts[i - 1], c.pts[i]) / step));
                        for (int k = 0; k <= n; k++)
                        {
                            var p = Vector3.Lerp(c.pts[i - 1], c.pts[i], k / (float)n); if (unitAt(p.x, p.z) == null) continue;
                            if (Physics.Raycast(p + Vector3.up * dropUp, Vector3.down, out var hit, dropLen, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
                            pts.Add(("route " + c.id, p));
                        }
                    }
            var types = new HashSet<string>(Arr(E, "component_types").Select(x => x.Value<string>()), StringComparer.Ordinal);
            foreach (var go in scene.GetRootGameObjects())
            {
                if (Preview(go.transform)) continue;
                if (Flag(E, "agents", true)) foreach (var a in go.GetComponentsInChildren<NavMeshAgent>(false)) pts.Add(("agent " + BuildingAudit308.KeyOf(a.transform), a.transform.position - Vector3.up * a.baseOffset));
                if (types.Count > 0) foreach (var m in go.GetComponentsInChildren<MonoBehaviour>(false)) if (m != null && types.Contains(m.GetType().Name)) pts.Add((m.GetType().Name + " " + BuildingAudit308.KeyOf(m.transform), m.transform.position));
            }
            foreach (var p in r.pieces)
            {
                var b = new Bounds(r.A(p.tris[0]), Vector3.zero); foreach (int t in p.tris) { b.Encapsulate(r.A(t)); b.Encapsulate(r.B(t)); b.Encapsulate(r.C(t)); }
                b.Expand(new Vector3(2f * xz, 2f * ym, 2f * xz));
                foreach (var (id, q) in pts)
                {
                    if (!b.Contains(q) || p.evidence.Contains(id)) continue;
                    foreach (int t in p.tris)
                    {
                        Vector3 a = r.A(t), bb = r.B(t), c = r.C(t); var cl = BuildingAudit308.ClosestOnTri(new Vector3(q.x, 0f, q.z), new Vector3(a.x, 0f, a.z), new Vector3(bb.x, 0f, bb.z), new Vector3(c.x, 0f, c.z));
                        if (new Vector2(cl.x - q.x, cl.z - q.z).magnitude > xz) continue;
                        float lo = Mathf.Min(a.y, Mathf.Min(bb.y, c.y)) - ym, hi = Mathf.Max(a.y, Mathf.Max(bb.y, c.y)) + ym;
                        if (q.y >= lo && q.y <= hi) { p.evidence.Add(id); break; }
                    }
                }
            }
            r.notes.Add("evidence samples over building units: " + pts.Count);
        }

        // is the piece joined to ground NavMesh outside its unit by a complete path. Review R8: when the question cannot be asked (no ground
        // NavMesh on the ring round the unit, or no NavMesh under the piece's own sample) the piece is "reach not judged" = KEEP, never an island.
        static void Reach(JObject d, ScanResult r, float band)
        {
            var S = d["scan"]; float ring = Num(S, "ring_m"), snap = Num(S, "ring_snap_m"), slackUp = Num(S, "ring_ground_slack_m"), pieceSnap = Num(S, "piece_snap_m");
            foreach (var p in r.pieces)
            {
                var u = p.unit;
                if (u.ring == null)
                {
                    u.ring = new List<Vector3>(); var f = u.f; float mx = (f.x0 + f.x1) * .5f, mz = (f.z0 + f.z1) * .5f;
                    foreach (var (lx, lz) in new[] { (f.x0 - ring, f.z0 - ring), (mx, f.z0 - ring), (f.x1 + ring, f.z0 - ring), (f.x1 + ring, mz), (f.x1 + ring, f.z1 + ring), (mx, f.z1 + ring), (f.x0 - ring, f.z1 + ring), (f.x0 - ring, mz) })
                    {
                        var w = f.World(lx, 0f, lz); float ty = BuildingAudit308.TerrainTop(w); if (float.IsNaN(ty)) continue; w.y = ty;
                        if (!NavMesh.SamplePosition(w, out var hit, snap, NavMesh.AllAreas)) continue;
                        float hy = BuildingAudit308.TerrainTop(hit.position); if (float.IsNaN(hy) || hit.position.y - hy > band + slackUp) continue;
                        u.ring.Add(hit.position);
                    }
                }
                if (u.ring.Count == 0) { p.reachJudged = false; p.reachNote = "no ground NavMesh on the ring " + F(ring, "F1") + " m round the unit"; continue; }
                int big = p.tris.OrderByDescending(t => AreaXZ(r.A(t), r.B(t), r.C(t))).First(); var src = (r.A(big) + r.B(big) + r.C(big)) / 3f;
                if (!NavMesh.SamplePosition(src, out var h0, pieceSnap, NavMesh.AllAreas)) { p.reachJudged = false; p.reachNote = "NavMesh.SamplePosition found nothing within " + F(pieceSnap, "F1") + " m of the piece's own sample"; continue; }
                p.reachJudged = true;
                foreach (var target in u.ring)
                {
                    var path = new NavMeshPath();
                    if (NavMesh.CalculatePath(h0.position, target, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) { p.reachable = true; break; }
                }
            }
        }

        // Is the piece the TOP of its building. Measured on the consignee house [O real meshes, Stage308_world4/Dry/roofmesh_guesthouse.txt]: the
        // NavMesh polygons float 0.03 .. 0.93 m OVER the roof they were built from (the chord of a concave tiled roof, the climb at the
        // ridge), and at every vertex the nearest drawn face is the topmost one. So a triangle CENTRE within a small snap of a drawn face
        // (the first rule of this tool) saw only 26 % of that roof. The law now, vertex by vertex: the HIGHEST drawn LOD0 face of the unit on
        // the vertical lies between roof.top_below_m under and roof.top_above_m over... the NavMesh vertex, i.e. nothing of the building is
        // drawn above it. tokenShare = such vertices whose top face also carries a roof token (name / material, building audit C5);
        // geoShare = such vertices of triangles pitched roof.geo.pitch_min_deg or more (review R5: the kit houses' materials are hash-named).
        // Only for islands nothing stands on - reachable pieces and evidence pieces are KEEP whatever their faces (memory: one unit at a time).
        static void RoofShares(JObject d, BuildingAudit308.Config308 cfg, ScanResult r)
        {
            var R = d["roof"]; float up = Num(R, "top_above_m"), dn = Num(R, "top_below_m"), pitchMin = Num(R["geo"], "pitch_min_deg");
            int cap = (int)Num(d["scan"], "max_triangles_per_unit"); var tokens = cfg.tokens.roof; var hits = new List<(float y, int tri)>(); int judged = 0;
            foreach (var g in r.pieces.Where(x => x.evidence.Count == 0 && x.reachJudged && !x.reachable).GroupBy(x => x.unit))
            {
                var u = g.Key; long count = 0;
                foreach (var x in u.u.lod0) { var m = BuildingAudit308.MeshOf(x); if (m == null) continue; for (int s = 0; s < m.subMeshCount; s++) count += m.GetIndexCount(s) / 3; }
                if (count == 0) { r.notes.Add(u.key + ": no drawn LOD0 mesh - roof not judged"); continue; }
                if (count > cap) { r.notes.Add(u.key + ": " + count + " drawn triangles (> scan.max_triangles_per_unit " + cap + ") - roof not judged"); continue; }
                var cache = new BuildingAudit308.MeshCache308();   // per unit: its mesh copies do not outlive its turn
                var mats = new Dictionary<Renderer, Material[]>(); var soup = new BuildingAudit308.Soup308();
                foreach (var x in u.u.lod0)
                    soup.Add(x, cache, (rr, sub) =>
                    {
                        if (BuildingAudit308.HasAny(rr.name, tokens)) return true;
                        if (!mats.TryGetValue(rr, out var mm)) mats[rr] = mm = rr.sharedMaterials;
                        return sub < mm.Length && mm[sub] != null && BuildingAudit308.HasAny(mm[sub].name, tokens);
                    });
                if (soup.Count == 0) { r.notes.Add(u.key + ": drawn meshes unreadable (" + soup.Skipped + ") - roof not judged"); continue; }
                float yLo = u.u.bounds.min.y - 2f, yHi = u.u.bounds.max.y + up + 2f; var top = new Dictionary<int, (bool on, bool tok)>();
                foreach (var p in g)
                {
                    float onTok = 0f, onGeo = 0f;
                    foreach (int t in p.tris)
                    {
                        float ar = AreaXZ(r.A(t), r.B(t), r.C(t)); int k = 0, kt = 0;
                        for (int c = 0; c < 3; c++)
                        {
                            int vi = r.idx[t * 3 + c];
                            if (!top.TryGetValue(vi, out var s))
                            {
                                var q = r.v[vi]; soup.Vertical(q.x, q.z, yLo, yHi, hits); bool on = false, tk = false;
                                if (hits.Count > 0)
                                {
                                    float best = float.NegativeInfinity; int bt = -1;
                                    foreach (var h in hits) if (h.y > best) { best = h.y; bt = h.tri; }
                                    float dy = q.y - best; on = dy >= -dn && dy <= up; tk = on && soup.Flag[bt];
                                }
                                top[vi] = s = (on, tk);
                            }
                            if (s.on) k++; if (s.tok) kt++;
                        }
                        onTok += ar * kt / 3f; if (r.Slope(t) >= pitchMin) onGeo += ar * k / 3f;
                    }
                    p.tokenShare = p.area > 0f ? onTok / p.area : float.NaN; p.geoShare = p.area > 0f ? onGeo / p.area : float.NaN; judged++;
                }
            }
            r.notes.Add("roof judged (drawn LOD0 meshes read) for " + judged + " island piece(s)");
        }

        static bool Roofish(JObject d, Piece p, out string how)
        {
            var R = d["roof"]; float min = Num(R, "share_min"); var geo = R["geo"];
            bool tok = !float.IsNaN(p.tokenShare) && p.tokenShare >= min, g = Flag(geo, "enabled") && !float.IsNaN(p.geoShare) && p.geoShare >= Num(geo, "share_min");
            how = tok ? "roof-token faces on top (share " + F(p.tokenShare) + ")" : g ? "the pitched top of its building's drawn mesh (share " + F(p.geoShare) + ")" : "";
            return tok || g;
        }

        static void Verdict(JObject d, Piece p, Approval approval)
        {
            bool roofish = Roofish(d, p, out string how); JToken dec = null; int decIndex = -1, i = -1; string key = p.unit.key;
            foreach (var row in Arr(d, "decisions"))
            {
                i++;
                if (!Flag(row, "enabled", true)) continue;
                bool hit = key == Str(row, "unit") || (Opt(row, "match") == "prefix" && key.StartsWith(Str(row, "unit"), StringComparison.Ordinal));
                if (!hit || (Flag(row, "only_islands") && (!p.reachJudged || p.reachable)) || (Flag(row, "requires_roof") && !roofish) || p.above0 < OptNum(row, "above_min_m", float.NegativeInfinity)) continue;
                dec = row; decIndex = i; break;
            }
            string last = key.Split('/').Last(); string guard = Arr(d["roof"], "never_auto_tokens").Select(x => x.Value<string>()).FirstOrDefault(tok => last.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0);
            if (p.evidence.Count > 0) { p.verdict = "KEEP"; p.why = "evidence: " + string.Join(", ", p.evidence.Take(4)); }
            else if (dec != null)
            {
                string v = Str(dec, "decision").ToUpperInvariant(); if (v != "KEEP" && v != "REMOVE") throw new Refuse("decisions[]: decision '" + Str(dec, "decision") + "' (keep | remove)");
                p.verdict = v; p.row = decIndex; p.why = "data decisions[" + decIndex + "]: " + (Opt(dec, "reason") ?? ""); if (p.why.Length > 110) p.why = p.why.Substring(0, 110);
            }
            else if (!p.reachJudged) { p.verdict = "KEEP"; p.why = "reach NOT judged (" + p.reachNote + ") - kept, listed"; }
            else if (p.reachable) { p.verdict = "KEEP"; p.warn = p.pitch >= Num(d["roof"], "share_min"); p.why = "reachable from the ground" + (p.warn ? " - PITCHED like a roof (WARN: needs a look and a decisions[] row; its faces were not read)" : ""); }
            else if (roofish && guard != null) { p.verdict = "KEEP"; p.why = "island, " + how + ", but the unit name carries \"" + guard + "\" (roof.never_auto_tokens: removed only by a decisions[] row)"; }
            else if (roofish)
            {
                bool approved = approval != null && approval.live && approval.units.Contains(key) && (!p.unit.careful || approval.careful.Contains(key));
                if (approved) approval.used.Add(key);
                if (Flag(d["roof"], "auto_remove") && !p.unit.careful) { p.verdict = "REMOVE"; p.why = "island, " + how + " (roof.auto_remove)"; }
                else if (approved) { p.verdict = "REMOVE"; p.why = "island, " + how + " - approved by '" + approval.by + "' " + approval.utc; }
                else { p.verdict = "KEEP"; p.candidate = true; p.why = "island, " + how + " - CANDIDATE" + (p.unit.careful ? " in a CAREFUL root (needs 'units' AND 'careful' of the approval file)" : " (needs the approval file or a decisions[] row)"); }
            }
            else { p.verdict = "KEEP"; p.why = "island, not a roof by the rule (token " + F(p.tokenShare) + ", top-and-pitched " + F(p.geoShare) + ", pitched " + F(p.pitch) + ") - undecided, listed"; }
        }

        // ------------------------------------------------------------------ plan

        // the convex triangle against the oriented box: separating axes in XZ (the box's two axes + the triangle's edge normals) and the heights
        static bool TriInBox(Vector3 a, Vector3 b, Vector3 c, Box box)
        {
            if (Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < box.y0 || Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > box.y1) return false;
            var lx = new float[3]; var lz = new float[3]; box.f.Local(a.x, a.z, out lx[0], out lz[0]); box.f.Local(b.x, b.z, out lx[1], out lz[1]); box.f.Local(c.x, c.z, out lx[2], out lz[2]);
            if (lx.Max() < box.lx0 || lx.Min() > box.lx1 || lz.Max() < box.lz0 || lz.Min() > box.lz1) return false;
            for (int i = 0; i < 3; i++)
            {
                float nx = -(lz[(i + 1) % 3] - lz[i]), nz = lx[(i + 1) % 3] - lx[i]; if (Mathf.Abs(nx) + Mathf.Abs(nz) < 1e-9f) continue;
                float p0 = float.PositiveInfinity, p1 = float.NegativeInfinity, b0 = float.PositiveInfinity, b1 = float.NegativeInfinity;
                for (int k = 0; k < 3; k++) { float q = lx[k] * nx + lz[k] * nz; p0 = Mathf.Min(p0, q); p1 = Mathf.Max(p1, q); }
                foreach (var (qx, qz) in new[] { (box.lx0, box.lz0), (box.lx1, box.lz0), (box.lx1, box.lz1), (box.lx0, box.lz1) }) { float q = qx * nx + qz * nz; b0 = Mathf.Min(b0, q); b1 = Mathf.Max(b1, q); }
                if (p1 < b0 || p0 > b1) return false;
            }
            return true;
        }
        static void WorldSpan(Box box, out float wx0, out float wx1, out float wz0, out float wz1)
        {
            var f = box.f; var c0 = f.World(box.lx0, 0f, box.lz0); var c1 = f.World(box.lx1, 0f, box.lz0); var c2 = f.World(box.lx1, 0f, box.lz1); var c3 = f.World(box.lx0, 0f, box.lz1);
            wx0 = Mathf.Min(Mathf.Min(c0.x, c1.x), Mathf.Min(c2.x, c3.x)); wx1 = Mathf.Max(Mathf.Max(c0.x, c1.x), Mathf.Max(c2.x, c3.x)); wz0 = Mathf.Min(Mathf.Min(c0.z, c1.z), Mathf.Min(c2.z, c3.z)); wz1 = Mathf.Max(Mathf.Max(c0.z, c1.z), Mathf.Max(c2.z, c3.z));
        }

        static List<Box> Plan(JObject d, ScanResult r)
        {
            var vol = d["volume"]; var boxes = new List<Box>(); var remove = new HashSet<int>(r.pieces.Where(x => x.verdict == "REMOVE").SelectMany(x => x.tris));
            var margins = new List<(float xz, float below)> { (Num(vol, "xz_margin_m"), Num(vol, "below_m")) };
            foreach (var m in Arr(vol, "retry_margins")) { var a = m as JArray; if (a != null && a.Count >= 2) margins.Add((a[0].Value<float>(), a[1].Value<float>())); }
            float above = Num(vol, "above_m"), minBottom = Num(vol, "min_bottom_over_terrain_m"), cell = OptNum(vol, "split_cell_m", 0f);
            float probeUp = Num(vol, "surface_probe_up_m"), probeDown = Num(vol, "surface_probe_down_m"), slack = Num(vol, "surface_slack_m");
            Box Make(Piece p, List<int> tris, string cellName)
            {
                var f = p.unit.f; Box box = null;
                float x0 = float.PositiveInfinity, x1 = float.NegativeInfinity, z0 = float.PositiveInfinity, z1 = float.NegativeInfinity, y0 = float.PositiveInfinity, y1 = float.NegativeInfinity, area = 0f, surf = float.PositiveInfinity;
                foreach (int t in tris)
                {
                    Vector3 a = r.A(t), b = r.B(t), c = r.C(t); area += AreaXZ(a, b, c);
                    foreach (var q in new[] { a, b, c }) { f.Local(q.x, q.z, out float lx, out float lz); x0 = Mathf.Min(x0, lx); x1 = Mathf.Max(x1, lx); z0 = Mathf.Min(z0, lz); z1 = Mathf.Max(z1, lz); y0 = Mathf.Min(y0, q.y); y1 = Mathf.Max(y1, q.y); }
                    // the bake re-labels walkable SPANS (the collider surface) inside the box, and the NavMesh polygons float up to about
                    // 0.9 m over that surface [O consignee house]: the box must hold the physical surface under the piece, not only the polygons
                    foreach (var q in new[] { a, b, c, (a + b) * .5f, (b + c) * .5f, (c + a) * .5f, (a + b + c) / 3f })
                        if (Physics.Raycast(q + Vector3.up * probeUp, Vector3.down, out var hit, probeUp + probeDown, ~0, QueryTriggerInteraction.Ignore)) surf = Mathf.Min(surf, hit.point.y);
                }
                foreach (var (mxz, mdn) in margins)
                {
                    float by0 = y0 - mdn; if (!float.IsInfinity(surf) && surf - slack < by0) by0 = surf - slack;
                    box = new Box { unit = p.unit.key, piece = p, f = f, cell = cellName, tris = tris, area = area, yaw = f.yaw, lx0 = x0 - mxz, lx1 = x1 + mxz, lz0 = z0 - mxz, lz1 = z1 + mxz, y0 = by0, y1 = y1 + above, surfaceLow = float.IsInfinity(surf) ? float.NaN : surf };
                    box.size = new Vector3(box.lx1 - box.lx0, box.y1 - box.y0, box.lz1 - box.lz0); box.centre = f.World((box.lx0 + box.lx1) * .5f, (box.y0 + box.y1) * .5f, (box.lz0 + box.lz1) * .5f);
                    float low = float.PositiveInfinity;
                    foreach (var (lx, lz) in new[] { (box.lx0, box.lz0), (box.lx1, box.lz0), (box.lx1, box.lz1), (box.lx0, box.lz1) }) { float ty = BuildingAudit308.TerrainTop(f.World(lx, 0f, lz)); if (!float.IsNaN(ty)) low = Mathf.Min(low, box.y0 - ty); }
                    if (low < minBottom) { box.blocked = true; box.why = "underside " + F(low) + " m over the terrain under a corner (< " + F(minBottom) + ")"; continue; }
                    int hits = 0; Vector3 first = Vector3.zero; var done = new HashSet<int>();
                    WorldSpan(box, out float wx0, out float wx1, out float wz0, out float wz1);
                    for (int gx = Mathf.FloorToInt(wx0 / GridCell); gx <= Mathf.FloorToInt(wx1 / GridCell); gx++)
                        for (int gz = Mathf.FloorToInt(wz0 / GridCell); gz <= Mathf.FloorToInt(wz1 / GridCell); gz++)
                        {
                            if (!r.grid.TryGetValue(Key(gx, gz), out var l)) continue;
                            foreach (int t in l) { if (remove.Contains(t) || !done.Add(t) || !TriInBox(r.A(t), r.B(t), r.C(t), box)) continue; if (hits++ == 0) first = (r.A(t) + r.B(t) + r.C(t)) / 3f; }
                        }
                    if (hits > 0) { box.blocked = true; box.why = hits + " NavMesh triangle(s) that are NOT being removed reach into the box (first at " + V(first) + ")"; continue; }
                    box.blocked = false; box.why = ""; break;
                }
                return box;
            }
            foreach (var p in r.pieces.Where(x => x.verdict == "REMOVE"))
            {
                var whole = Make(p, p.tris, null);
                if (!whole.blocked || cell <= 0f || p.tris.Count < 2) { boxes.Add(whole); continue; }
                var groups = new SortedDictionary<(int, int), List<int>>();
                foreach (int t in p.tris)
                {
                    var c = (r.A(t) + r.B(t) + r.C(t)) / 3f; p.unit.f.Local(c.x, c.z, out float lx, out float lz); var k = (Mathf.FloorToInt(lx / cell), Mathf.FloorToInt(lz / cell));
                    if (!groups.TryGetValue(k, out var l)) groups[k] = l = new List<int>(); l.Add(t);
                }
                foreach (var kv in groups) boxes.Add(Make(p, kv.Value, kv.Key.Item1 + "," + kv.Key.Item2));
            }
            // names: the unblocked boxes in a fixed order (unit, then where they stand)
            int index = 0; string prefix = Opt(vol, "name_prefix") ?? "RoofVol_";
            foreach (var b in boxes.Where(x => !x.blocked).OrderBy(x => x.unit, StringComparer.Ordinal).ThenBy(x => x.centre.x).ThenBy(x => x.centre.z))
            {
                var sb = new StringBuilder(); foreach (char ch in b.unit.Split('/').Last()) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
                b.name = prefix + (index++).ToString("000", Inv) + "_" + sb;
            }
            return boxes;
        }

        static JArray BoxesJson(JObject d, List<Box> boxes)
        {
            var a = new JArray();
            foreach (var b in boxes.OrderBy(x => x.blocked).ThenBy(x => x.name ?? "", StringComparer.Ordinal))
                a.Add(new JObject { ["name"] = b.name ?? "", ["unit"] = b.unit, ["cell"] = b.cell ?? "", ["blocked"] = b.blocked, ["why"] = b.why, ["area"] = b.area, ["centre"] = new JArray(b.centre.x, b.centre.y, b.centre.z),
                    ["size"] = new JArray(b.size.x, b.size.y, b.size.z), ["yaw"] = b.yaw, ["underside"] = b.y0, ["surfaceLow"] = float.IsNaN(b.surfaceLow) ? JValue.CreateNull() : new JValue(b.surfaceLow), ["row"] = b.piece != null ? b.piece.row : -1 });
            return a;
        }
        // a recorded box back as an oriented box in its own frame (centre, yaw, half sizes)
        static Box BoxOf(JToken b)
        {
            var c = Vec(b["centre"]); var s = Vec(b["size"]); float yaw = Num(b, "yaw"), rad = yaw * Mathf.Deg2Rad;
            var f = new Frame { ox = c.x, oz = c.z, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad), yaw = yaw };
            return new Box { name = Opt(b, "name") ?? "", unit = Opt(b, "unit") ?? "", f = f, lx0 = -s.x * .5f, lx1 = s.x * .5f, lz0 = -s.z * .5f, lz1 = s.z * .5f, y0 = c.y - s.y * .5f, y1 = c.y + s.y * .5f, yaw = yaw, centre = c, size = s, area = OptNum(b, "area", 0f) };
        }
        // area of the loaded NavMesh (ANY triangle: high or low, any unit, any verdict) whose centre lies inside the box
        static float InsideArea(ScanResult r, Box box)
        {
            WorldSpan(box, out float wx0, out float wx1, out float wz0, out float wz1); var done = new HashSet<int>(); float sum = 0f;
            for (int gx = Mathf.FloorToInt(wx0 / GridCell); gx <= Mathf.FloorToInt(wx1 / GridCell); gx++)
                for (int gz = Mathf.FloorToInt(wz0 / GridCell); gz <= Mathf.FloorToInt(wz1 / GridCell); gz++)
                {
                    if (!r.grid.TryGetValue(Key(gx, gz), out var l)) continue;
                    foreach (int t in l)
                    {
                        if (!done.Add(t)) continue;
                        var c = (r.A(t) + r.B(t) + r.C(t)) / 3f; box.f.Local(c.x, c.z, out float lx, out float lz);
                        if (lx >= box.lx0 && lx <= box.lx1 && lz >= box.lz0 && lz <= box.lz1 && c.y >= box.y0 && c.y <= box.y1) sum += AreaXZ(r.A(t), r.B(t), r.C(t));
                    }
                }
            return sum;
        }
        // heights of every NavMesh triangle over the point (x, z)
        static List<float> NavOver(ScanResult r, float x, float z)
        {
            var ys = new List<float>();
            if (!r.grid.TryGetValue(Key(Mathf.FloorToInt(x / GridCell), Mathf.FloorToInt(z / GridCell)), out var l)) return ys;
            foreach (int t in l) if (BuildingAudit308.VerticalHit(r.A(t), r.B(t), r.C(t), x, z, out float y)) ys.Add(y);
            ys.Sort(); return ys;
        }

        static void AppendScan(JObject d, StringBuilder sb, ScanResult r)
        {
            float high = Num(d["scan"], "high_m");
            sb.AppendLine("  scene " + r.scenePath + " sha " + Short(r.sceneSha) + "; NavMesh asset sha " + Short(r.navSha) + "; " + r.triangles + " triangle(s); " + r.units + " building unit(s); pieces >= " + F(high, "F1") + " m over the terrain: " + r.pieces.Count + ", " + F(r.pieces.Sum(p => p.area), "F0") + " m2");
            sb.AppendLine("  approval: " + r.approval.Line);
            foreach (var n in r.notes) sb.AppendLine("  note " + n);
            string Line(Piece p) => p.unit.key + " " + F(p.area, "F1") + " m2 at " + V(p.centre) + " y " + F(p.y0, "F1") + " .. " + F(p.y1, "F1") + " (" + F(p.above0, "F1") + " .. " + F(p.above1, "F1") + " over the terrain) slope " + F(p.slope, "F1")
                + " pitched " + F(p.pitch) + " top " + F(p.geoShare) + " token " + F(p.tokenShare) + (!p.reachJudged ? " reach?" : p.reachable ? " reachable" : " island") + " | " + p.why;
            foreach (var p in r.pieces.Where(x => x.verdict == "REMOVE")) sb.AppendLine("  REMOVE " + Line(p));
            foreach (var p in r.pieces.Where(x => x.candidate)) sb.AppendLine("  CAND   " + Line(p));
            foreach (var p in r.pieces.Where(x => x.warn)) sb.AppendLine("  WARN   " + Line(p));
            foreach (var p in r.pieces.Where(x => x.verdict == "KEEP" && !x.candidate && x.evidence.Count == 0 && (!x.reachJudged || !x.reachable))) sb.AppendLine("  KEEP?  " + Line(p));
            foreach (var p in r.pieces.Where(x => x.evidence.Count > 0)) sb.AppendLine("  KEEP   " + Line(p));
            foreach (var g in r.pieces.Where(p => p.verdict == "KEEP" && p.reachJudged && p.reachable && p.evidence.Count == 0 && !p.warn).GroupBy(p => p.unit.key))
                sb.AppendLine("  KEEP   " + g.Key + ": " + g.Count() + " reachable piece(s), " + F(g.Sum(p => p.area), "F1") + " m2, highest " + F(g.Max(p => p.above1), "F1") + " m over the terrain");
            int i = -1;
            foreach (var row in Arr(d, "decisions"))
            {
                i++; if (!Flag(row, "enabled", true)) continue; int at = i;
                sb.AppendLine("  row    decisions[" + at + "] " + Str(row, "decision") + " '" + Str(row, "unit") + "'" + (Flag(row, "must_match") ? " MUST MATCH" : "") + ": decided " + F(r.pieces.Where(p => p.row == at).Sum(p => p.area), "F1") + " m2 in " + r.pieces.Count(p => p.row == at)
                    + " piece(s); high NavMesh of any verdict on its unit(s) " + F(r.pieces.Where(p => RowUnit(row, p.unit.key)).Sum(p => p.area), "F1") + " m2");
            }
            var cand = r.pieces.Where(p => p.candidate).ToList();
            sb.AppendLine("  totals: REMOVE " + r.pieces.Count(p => p.verdict == "REMOVE") + " (" + F(r.pieces.Where(p => p.verdict == "REMOVE").Sum(p => p.area), "F1") + " m2), CANDIDATE " + cand.Count + " (" + F(cand.Sum(p => p.area), "F1") + " m2 in " + cand.Select(p => p.unit.key).Distinct().Count() + " unit(s), "
                + cand.Count(p => p.unit.careful) + " in careful roots), WARN " + r.pieces.Count(p => p.warn) + ", KEEP undecided island " + r.pieces.Count(p => p.verdict == "KEEP" && !p.candidate && p.reachJudged && !p.reachable && p.evidence.Count == 0)
                + ", reach not judged " + r.pieces.Count(p => !p.reachJudged && p.evidence.Count == 0 && p.row < 0) + ", KEEP reachable " + r.pieces.Count(p => p.verdict == "KEEP" && p.reachJudged && p.reachable) + ", KEEP evidence " + r.pieces.Count(p => p.evidence.Count > 0));
            if (cand.Count > 0) sb.AppendLine("  to approve candidates: the MAIN agent writes " + r.approval.file + " = {\"navSha\": \"" + Short(r.navSha) + "\", \"by\": \"main\", \"utc\": \"...\", \"units\": [..unit keys of CAND lines..], \"careful\": [..]} and runs roof-plan again; an operator never writes it");
        }
        static bool RowUnit(JToken row, string key) => key == Str(row, "unit") || (Opt(row, "match") == "prefix" && key.StartsWith(Str(row, "unit"), StringComparison.Ordinal));

        static void AppendPlan(JObject d, StringBuilder sb, ScanResult r, List<Box> boxes)
        {
            AppendScan(d, sb, r);
            foreach (var b in boxes)
                sb.AppendLine("  " + (b.blocked ? "BLOCKED " : "box     ") + b.unit + (b.cell != null ? " cell " + b.cell : "") + " " + F(b.area, "F1") + " m2: centre " + V(b.centre) + " size " + V(b.size) + " yaw " + F(b.yaw, "F1")
                    + (float.IsNaN(b.surfaceLow) ? " (no collider surface found under it)" : " underside " + F(b.y0) + " = " + F(b.surfaceLow - b.y0) + " m under the lowest collider surface of the piece") + (b.blocked ? " - " + b.why : " -> " + b.name));
            sb.AppendLine("  plan: " + boxes.Count(b => !b.blocked) + " box(es) in " + boxes.Where(b => !b.blocked).Select(b => b.unit).Distinct().Count() + " unit(s); BLOCKED " + boxes.Count(b => b.blocked) + " (" + F(boxes.Where(b => b.blocked).Sum(b => b.area), "F1") + " m2 of " + F(boxes.Sum(b => b.area), "F1") + " m2)");
        }

        static JObject ScanJson(JObject d, ScanResult r, string dataSha, string label)
        {
            var j = new JObject { ["tool"] = "RoofNav308", ["utc"] = PostLedger308.Utc(), ["label"] = label ?? "", ["alias"] = r.alias, ["scene"] = r.scenePath, ["sceneSha"] = r.sceneSha, ["navSha"] = r.navSha ?? "", ["dataSha"] = dataSha,
                ["approvalSha"] = r.approval.sha, ["approval"] = r.approval.Line, ["triangles"] = r.triangles, ["units"] = r.units };
            var pieces = new JArray();
            foreach (var p in r.pieces)
                pieces.Add(new JObject { ["unit"] = p.unit.key, ["verdict"] = p.verdict, ["why"] = p.why, ["row"] = p.row, ["candidate"] = p.candidate, ["careful"] = p.unit.careful, ["area"] = p.area, ["centre"] = new JArray(p.centre.x, p.centre.y, p.centre.z), ["y0"] = p.y0, ["y1"] = p.y1,
                    ["above0"] = p.above0, ["above1"] = p.above1, ["slope"] = p.slope, ["pitch"] = p.pitch, ["tokenShare"] = float.IsNaN(p.tokenShare) ? -1f : p.tokenShare, ["geoShare"] = float.IsNaN(p.geoShare) ? -1f : p.geoShare,
                    ["reachJudged"] = p.reachJudged, ["reachable"] = p.reachable, ["warn"] = p.warn, ["evidence"] = new JArray(p.evidence.ToArray()) });
            j["pieces"] = pieces; var rows = new JArray(); int i = -1;
            foreach (var row in Arr(d, "decisions"))
            {
                i++; int at = i;
                rows.Add(new JObject { ["index"] = at, ["unit"] = Str(row, "unit"), ["decision"] = Str(row, "decision"), ["enabled"] = Flag(row, "enabled", true), ["mustMatch"] = Flag(row, "enabled", true) && Flag(row, "must_match"),
                    ["matchedM2"] = r.pieces.Where(p => p.row == at).Sum(p => p.area), ["highM2"] = r.pieces.Where(p => RowUnit(row, p.unit.key)).Sum(p => p.area) });
            }
            j["rows"] = rows; var g = new JObject(); foreach (var kv in r.ground) g[kv.Key] = kv.Value; j["groundM2"] = g; j["notes"] = new JArray(r.notes.ToArray());
            return j;
        }

        static string ScanCommand(string alias, string label)
        {
            var d = Load(out string dataSha); string path = ScenePath(alias);
            PostLedger308.RequireEditable();
            var scene = PostLedger308.Open(path); var r = Scan(d, scene, alias);
            var sb = new StringBuilder("RoofNav308 roof-scan " + alias + (label != null ? " [" + label + "]" : "") + " (data " + Short(dataSha) + ")\n"); AppendScan(d, sb, r);
            Directory.CreateDirectory(LedgerDir(d)); string name = "roofscan-" + alias + "-" + (string.IsNullOrEmpty(label) ? PostLedger308.Utc() : label);
            File.WriteAllText(Path.Combine(LedgerDir(d), name + ".json"), ScanJson(d, r, dataSha, label).ToString(Newtonsoft.Json.Formatting.Indented), Utf8);
            File.WriteAllText(Path.Combine(LedgerDir(d), name + ".txt"), sb.ToString(), Utf8);
            if (scene.isDirty) sb.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
            return sb.Append("read only: nothing written to the scene; report " + Path.Combine(LedgerDir(d), name + ".json")).ToString();
        }

        // ------------------------------------------------------------------ check (after the bake)

        static string CheckCommand(string alias)
        {
            var d = Load(out string dataSha); string path = ScenePath(alias);
            PostLedger308.RequireEditable();
            if (!Listed(d, alias)) throw new Refuse("roof-check runs in a scene of data scenes[] (" + ScenesText(d) + "): the boxes and the bake live there, and the three scenes share the NavMesh asset");
            var op = LiveOp(LedgerLoad(d, alias));
            if (op == null || Opt(op, "status") != "applied") throw new Refuse("no live roof-apply op for " + alias + " (roof-plan, roof-apply and the bake come first)");
            string refFile = AppliedPlanFile(d, alias);
            if (!File.Exists(refFile)) throw new Refuse("the plan the apply wrote is missing: " + refFile);
            var before = JObject.Parse(File.ReadAllText(refFile));
            var scene = PostLedger308.Open(path); var r = Scan(d, scene, alias); var C = d["check"];
            int pass = 0, fail = 0, open = 0; var sb = new StringBuilder();
            void Row(bool ok, string text) { if (ok) pass++; else fail++; sb.AppendLine((ok ? "PASS " : "FAIL ") + text); }
            void OpenRow(string text) { open++; sb.AppendLine("OPEN " + text); }
            float leftTol = Num(C, "remove_left_m2"), keepTol = Num(C, "keep_area_tol"), keepM2 = Num(C, "keep_area_tol_m2");
            sb.AppendLine("INFO compared with " + refFile + " (plan " + Opt(before, "utc") + ", NavMesh sha then " + Short(Opt(before, "navSha")) + ", now " + Short(r.navSha) + "); applied op " + Opt(op, "utc") + ": " + Opt(op, "detail"));
            Row((Opt(before, "navSha") ?? "") != (r.navSha ?? ""), "the NavMesh asset changed since the plan (the bake has run)");
            var boxList = Arr(op, "boxList").ToList(); var root = Root(d, scene); int inScene = BoxesInScene(d, root, boxList);
            Row(inScene == boxList.Count && (root == null ? 0 : root.childCount) == boxList.Count, "the " + boxList.Count + " box(es) of the ledger op stand in the scene as written (" + inScene + " found, root holds " + (root == null ? 0 : root.childCount) + ")");
            // AC-N1 (a): the finding itself, by geometry - no unit key, no verdict, no row
            foreach (var pt in Arr(C, "probe_points"))
            {
                float x = Num(pt, "x"), z = Num(pt, "z"), max = Num(pt, "max_over_ground_m"), ground = BuildingAudit308.TerrainTop(new Vector3(x, 0f, z)); var ys = NavOver(r, x, z);
                if (float.IsNaN(ground)) { Row(false, "AC-N1 probe " + Opt(pt, "id") + " (" + F(x) + ", " + F(z) + "): no terrain under the point"); continue; }
                var bad = ys.Where(y => y - ground >= max).ToList();
                Row(bad.Count == 0, "AC-N1 probe " + Opt(pt, "id") + " (" + F(x) + ", " + F(z) + "): no NavMesh " + F(max, "F1") + " m or more over the terrain " + F(ground) + " (NavMesh over the point: " + (ys.Count == 0 ? "none" : string.Join(", ", ys.Select(y => "y " + F(y)))) + ")");
            }
            // AC-N1 (b): the must_match rows (the consignee house) - ALL high NavMesh on the unit, whatever its verdict now (review R2)
            var blocked = new Dictionary<string, float>(StringComparer.Ordinal);
            if (op["blockedM2"] is JObject bm) foreach (var kv in bm) blocked[kv.Key] = kv.Value.Value<float>();
            int must = 0, ri = -1;
            foreach (var row in Arr(d, "decisions"))
            {
                ri++; if (!Flag(row, "enabled", true) || Str(row, "decision") != "remove") continue;
                string unit = Str(row, "unit"); var was = Arr(before, "rows").FirstOrDefault(x => (int)OptNum(x, "index", -1f) == ri && Opt(x, "unit") == unit);
                float wasM2 = OptNum(was, "matchedM2", 0f), nowAll = r.pieces.Where(p => RowUnit(row, p.unit.key)).Sum(p => p.area), bl = blocked.Where(kv => RowUnit(row, kv.Key)).Sum(kv => kv.Value);
                if (!Flag(row, "must_match")) { sb.AppendLine("INFO decisions[" + ri + "] " + unit + ": decided " + F(wasM2, "F1") + " m2 at plan time; high NavMesh on its unit(s) now " + F(nowAll, "F1") + " m2 (not a must_match row: judged by AC-N6)"); continue; }
                must++;
                if (wasM2 <= leftTol) Row(false, "AC-N1 decisions[" + ri + "] " + unit + ": the row decided NOTHING at plan time (" + F(wasM2, "F1") + " m2) - the unit key did not match or the piece was not an island; high NavMesh on the unit now " + F(nowAll, "F1") + " m2");
                else if (nowAll > leftTol && bl > 0f) OpenRow("AC-N1 decisions[" + ri + "] " + unit + ": " + F(nowAll, "F1") + " m2 of high NavMesh left (the row decided " + F(wasM2, "F1") + "), " + F(bl, "F1") + " m2 of its boxes were BLOCKED at plan time");
                else Row(nowAll <= leftTol, "AC-N1 decisions[" + ri + "] " + unit + ": high NavMesh of ANY verdict left on the unit " + F(nowAll, "F1") + " m2 (the row decided " + F(wasM2, "F1") + " m2 before the bake)");
            }
            int mustMin = (int)Num(C, "must_rows_min");
            Row(must >= mustMin, "AC-N1 the data names " + must + " enabled must_match remove row(s) (>= " + mustMin + ": the consignee house is checked by a row, not by chance)");
            // AC-N6: nothing of the NavMesh inside any box the bake read
            var leftIn = new List<string>(); float leftSum = 0f;
            foreach (var b in boxList) { float a = InsideArea(r, BoxOf(b)); if (a > leftTol) { leftSum += a; leftIn.Add(Opt(b, "name") + " " + F(a, "F1") + " m2"); } }
            Row(leftIn.Count == 0, "AC-N6 no NavMesh is left inside any of the " + boxList.Count + " box(es) (triangle centres, any height, any verdict)" + (leftIn.Count > 0 ? ": " + F(leftSum, "F1") + " m2 in " + leftIn.Count + " box(es) - " + string.Join("; ", leftIn.Take(8)) : ""));
            var cand = r.pieces.Where(p => p.candidate || p.verdict == "REMOVE").ToList();
            if (blocked.Count > 0) OpenRow("AC-N6b BLOCKED at plan time (no box was written): " + F(blocked.Values.Sum(), "F1") + " m2 in " + blocked.Count + " unit(s) (" + string.Join(", ", blocked.Take(8).Select(kv => kv.Key + " " + F(kv.Value, "F1"))) + ") - each needs a finer box or a row; not a PASS");
            if (cand.Count > 0) OpenRow("AC-N6b roof candidates still on buildings: " + cand.Count + " piece(s), " + F(cand.Sum(p => p.area), "F1") + " m2 in " + cand.Select(p => p.unit.key).Distinct().Count() + " unit(s) (" + string.Join(", ", cand.GroupBy(p => p.unit.key).Take(8).Select(g => g.Key + " " + F(g.Sum(p => p.area), "F1"))) + ") - not approved for this bake; not a PASS");
            if (blocked.Count == 0 && cand.Count == 0) Row(true, "AC-N6b no BLOCKED box and no roof candidate is left on any building unit");
            // AC-N7: every unit carries the high NavMesh it was meant to keep - the pieces kept at plan time + the REMOVE pieces whose box was
            // BLOCKED (they are expected to stand, AC-N6b lists them) - no less and no more. Counted as ALL high NavMesh now, whatever its label.
            var kb = Arr(before, "pieces").Where(p => Opt(p, "verdict") == "KEEP").GroupBy(p => Opt(p, "unit")).ToDictionary(g => g.Key, g => g.Sum(p => Num(p, "area")), StringComparer.Ordinal);
            var ha = r.pieces.GroupBy(p => p.unit.key).ToDictionary(g => g.Key, g => g.Sum(p => p.area), StringComparer.Ordinal);
            var units7 = new HashSet<string>(kb.Keys, StringComparer.Ordinal); units7.UnionWith(ha.Keys); units7.UnionWith(blocked.Keys); var lost = new List<string>();
            foreach (var u in units7.OrderBy(x => x, StringComparer.Ordinal))
            {
                kb.TryGetValue(u, out float kept); blocked.TryGetValue(u, out float bl7); ha.TryGetValue(u, out float now); float expect = kept + bl7;
                if (Mathf.Abs(now - expect) > Mathf.Max(keepTol * expect, keepM2)) lost.Add(u + " expected " + F(expect, "F1") + " (kept " + F(kept, "F1") + " + BLOCKED " + F(bl7, "F1") + "), now " + F(now, "F1") + " m2");
            }
            Row(lost.Count == 0, "AC-N7 every unit carries the high NavMesh it was meant to keep (kept pieces + pieces whose box was BLOCKED), no less and no more: " + units7.Count + " unit(s) compared, tol " + F(keepTol * 100f, "F0") + " % or " + F(keepM2, "F1") + " m2"
                + (lost.Count > 0 ? ": " + string.Join("; ", lost.Take(6)) : ""));
            // AC-N8: the ground under eaves and in doorways of the treated units
            var bad8 = new List<string>(); int compared = 0; var gb = before["groundM2"] as JObject;
            foreach (var u in Arr(op, "treatedUnits").Select(x => x.Value<string>()))
            {
                float a = gb != null && gb[u] != null ? gb[u].Value<float>() : 0f; r.ground.TryGetValue(u, out float b); compared++;
                if (Mathf.Abs(a - b) > Mathf.Max(Num(C, "ground_area_tol_m2"), Num(C, "ground_area_tol_frac") * a)) bad8.Add(u + " " + F(a, "F1") + " -> " + F(b, "F1") + " m2");
            }
            Row(bad8.Count == 0 && compared > 0, "AC-N8 ground-level NavMesh inside every treated unit keeps its area (" + compared + " unit(s); tol " + F(Num(C, "ground_area_tol_m2"), "F1") + " m2 or " + F(Num(C, "ground_area_tol_frac") * 100f, "F0") + " %)" + (bad8.Count > 0 ? ": " + string.Join("; ", bad8.Take(6)) : ""));
            foreach (var p in r.pieces.Where(x => x.warn)) sb.AppendLine("WARN reachable piece pitched like a roof (kept): " + p.unit.key + " " + F(p.area, "F1") + " m2 at " + V(p.centre));
            Directory.CreateDirectory(LedgerDir(d));
            File.WriteAllText(Path.Combine(LedgerDir(d), "roofscan-" + alias + "-after.json"), ScanJson(d, r, dataSha, "after").ToString(Newtonsoft.Json.Formatting.Indented), Utf8);
            string head = "RoofNav308 roof-check " + alias + ": " + (fail == 0 ? (open == 0 ? "GREEN" : "GREEN with OPEN") : "RED") + " (PASS " + pass + ", FAIL " + fail + ", OPEN " + open + ")\n";
            File.WriteAllText(Path.Combine(LedgerDir(d), "roofcheck-" + alias + ".txt"), head + sb, Utf8);
            if (scene.isDirty) sb.AppendLine("WARN the scene is dirty after a read-only pass (bug): reload it");
            return head + sb + "read only: nothing written to the scene";
        }
    }
}
