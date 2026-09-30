using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Sheet = Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
    //   build   — (Edit mode, candidate / #298 / main scene open) replace the Roadside303 root, solve every site on its route
    //             shoulder, build the props, clone the givers, write the points + commissions into the scene's content and
    //             clear the dressing under each footprint in the shared art sheet. Idempotent; backups under Output/Before.
    public static partial class Roadside303
    {
        const string Root303 = "Roadside303";
        const string AssetFolder = "Assets/_Project/Art/World/Roadside303";
        internal static readonly string[] Scenes303 =
        {
            "Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity",
            CompactFolklore298.ScenePath,
            "Assets/_Project/Scenes/World/W_Demo_Main.unity",
        };

        [Serializable] class SiteSpec { public string id, kind, route, cargo, evidence, theme; public float u; public bool downhill; }
        [Serializable] class NpcSpec { public string id, prompt, site, prop; public float[] feet, local, tint; public float yaw; }
        [Serializable] class PointSpec { public string id, kind, site, prompt, text, locked; public float[] local; public float radius = 2.5f; }
        [Serializable] class CommissionData { public string id, giver, evidence, target, offer, waiting, report, completed; public int reward; }
        [Serializable] class Data303 { public string version; public SiteSpec[] sites; public NpcSpec[] npcs; public PointSpec[] points; public CommissionData[] commissions; }
        [Serializable] public class Placed { public string id, kind, theme, route, status; public Vector3 centre, road; public float yaw, offset, slope, clearance, drop; public int dressingRemoved; }
        [Serializable] class Ledger { public string scene, version, built; public Placed[] sites; public string[] npcs, points, commissions; }

        sealed class RouteLine { public string id; public float width; public Vector2[] pts; public float[] cum; }

        static Data303 LoadData() => JsonUtility.FromJson<Data303>(File.ReadAllText(Path.Combine(AbsOutput, "roadside303.json")));
        static string Abs(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        static Vector3 V3(float[] a) => a == null || a.Length < 3 ? Vector3.zero : new Vector3(a[0], a[1], a[2]);

        static string BuildScene(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Edit mode only");
            if (!Scenes303.Contains(path)) throw new Exception("not a #303 target: " + path);
            if (SceneManager.GetActiveScene().isDirty) throw new Exception("the open scene has unsaved changes");
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            return Build();
        }

        static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Roadside303 build: Edit mode only");
            var scene = SceneManager.GetActiveScene();
            if (!Scenes303.Contains(scene.path)) throw new Exception("Roadside303 build: open one of " + string.Join(", ", Scenes303));
            if (scene.isDirty) throw new Exception("Roadside303 build: the open scene has unsaved changes — save or discard them first (the build saves the scene)");
            var session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>() ?? throw new Exception("no WorldMacroPlaytestSession");
            var content = session.Content ?? throw new Exception("session has no content");
            var layout = session.MountainLayout ?? throw new Exception("session has no world layout");
            var data = LoadData();
            var arts = Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None).Where(a => a.Sheet != null).ToArray();

            // backups: scene, content, every art sheet (copied before anything changes)
            string backup = Path.Combine(AbsOutput, "Before", Path.GetFileNameWithoutExtension(scene.path) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(backup);
            var grass = new GrassClear();
            foreach (var path in new[] { scene.path, AssetDatabase.GetAssetPath(content) }.Concat(arts.Select(a => AssetDatabase.GetAssetPath(a.Sheet))).Concat(grass.Paths).Distinct())
                File.Copy(Abs(path), Path.Combine(backup, Path.GetFileName(path)), true);

            foreach (var g in scene.GetRootGameObjects().Where(g => g.name == Root303).ToArray()) Object.DestroyImmediate(g);
            var root = new GameObject(Root303).transform;
            Physics.SyncTransforms();
            EnsureFolder(AssetFolder); EnsureFolder(AssetFolder + "/Meshes"); EnsureFolder(AssetFolder + "/Materials");

            var routes = Routes(layout);
            var index = new DressingIndex(arts);
            var mats = new Mats303();
            var placed = new List<Placed>(); var sites = new Dictionary<string, Transform>();
            foreach (var s in data.sites)
            {
                var p = Solve(s, routes, index, root, placed);
                if (p == null) { placed.Add(new Placed { id = s.id, kind = s.kind, theme = s.theme, route = s.route, status = "NO SITE" }); continue; }
                var t = new GameObject(s.id).transform; t.SetParent(root, false); t.SetPositionAndRotation(p.centre, Quaternion.Euler(0, p.yaw, 0));
                if (!string.IsNullOrEmpty(s.evidence)) t.gameObject.AddComponent<WorldMacroContentPoint>().Id = s.evidence;
                var rng = new System.Random(StableHash(s.id));
                if (s.kind.StartsWith("cart")) Cart(t, s, mats, rng, root);
                else if (s.kind == "jige") Jige(t, mats, rng, root);
                else House(t, s.kind, mats, rng, root);
                Physics.SyncTransforms();
                p.dressingRemoved = index.ClearUnder(t, 1.0f);
                if (s.kind.StartsWith("house"))
                {
                    grass.Box(t, new Vector3(-.2f, 0, .6f), new Vector2(4.4f + GrassPatchHalf, 4.2f + GrassPatchHalf));    // under the floor
                    grass.Circle(t.TransformPoint(new Vector3(0, 0, 5.8f)), 4.5f + GrassPatchHalf);        // the trodden yard in front
                }
                p.status = "OK"; placed.Add(p); sites[s.id] = t;
            }

            // givers: clones of the inn keeper's point (capsule + content point + C02 stand-in appearance)
            var template = Find("geumpyo_inn/geumpyo_innkeeper") ?? throw new Exception("template geumpyo_inn/geumpyo_innkeeper missing");
            var npcRoot = new GameObject("Givers").transform; npcRoot.SetParent(root, false);
            var npcFeet = new Dictionary<string, Vector3>(); int grassCleared = 0;
            foreach (var n in data.npcs)
            {
                Vector3 feet; float yaw;
                if (!string.IsNullOrEmpty(n.site))
                {
                    if (!sites.TryGetValue(n.site, out var st)) throw new Exception("giver " + n.id + " needs site " + n.site);
                    feet = st.TransformPoint(V3(n.local)); yaw = st.eulerAngles.y + n.yaw;
                }
                else { feet = V3(n.feet); yaw = n.yaw; }
                if (!Ground(feet, root, out var hit)) throw new Exception("no ground for giver " + n.id);
                feet = hit.point;
                var go = Object.Instantiate(template.gameObject, npcRoot); go.name = n.id;
                go.transform.SetPositionAndRotation(feet, Quaternion.Euler(0, yaw, 0));
                var cp = go.GetComponent<WorldMacroContentPoint>(); if (cp == null) cp = go.AddComponent<WorldMacroContentPoint>(); cp.Id = n.id;
                Tint(go, n, mats);
                GiverProp(go, n.prop, mats);
                npcFeet[n.id] = feet;
                grass.Circle(feet, 2.0f + GrassPatchHalf);   // a bare spot of ~2 m where the giver stands
            }

            // points + commissions (ours replace any earlier copies by id; everything else is untouched)
            var ours = new HashSet<string>(data.npcs.Select(n => n.id).Concat(data.points.Select(p => p.id)));
            var points = (content.Points ?? Array.Empty<PrologueContentSO.Point>()).Where(p => p != null && !ours.Contains(p.Id)).ToList();
            foreach (var n in data.npcs)
                points.Add(new PrologueContentSO.Point { Id = n.id, Kind = PrologueInteractionKind.Conversation, Position = npcFeet[n.id], Prompt = n.prompt, Text = "", Radius = 2.5f, LockedText = "" });
            foreach (var p in data.points)
            {
                if (!sites.TryGetValue(p.site, out var st)) throw new Exception("point " + p.id + " needs site " + p.site);
                var at = st.TransformPoint(V3(p.local)); if (Ground(at, null, out var ph)) at = ph.point;
                points.Add(new PrologueContentSO.Point { Id = p.id, Kind = PrologueInteractionKind.Evidence, Position = at, Prompt = p.prompt, Text = p.text, LockedText = p.locked, Radius = p.radius });
            }
            content.Points = points.ToArray();
            var qs = (content.Commissions ?? Array.Empty<WorldMacroPlaytestSO.CommissionSpec>()).Where(q => q != null && !data.commissions.Any(c => c.id == q.Id)).ToList();
            foreach (var c in data.commissions)
                qs.Add(new WorldMacroPlaytestSO.CommissionSpec { Id = c.id, GiverId = c.giver, EvidenceId = c.evidence, TargetEncounterId = c.target, OfferText = c.offer, WaitingText = c.waiting, ReportText = c.report, CompletedText = c.completed, Reward = c.reward });
            content.Commissions = qs.ToArray();
            EditorUtility.SetDirty(content);

            // keep future procedural passes off the footprints
            foreach (var a in arts)
            {
                var keep = a.Sheet.PreservedAreas.Where(x => x == null || !x.Id.StartsWith("roadside303/")).ToList();
                foreach (var kv in sites)
                {
                    var b = BoundsOf(kv.Value); if (b.size == Vector3.zero) continue;
                    keep.Add(new Sheet.PreserveArea { Id = "roadside303/" + kv.Key, Centre = b.center, HalfSize = new Vector2(b.extents.x + 1f, b.extents.z + 1f), Yaw = 0, ExcludeProcedural = true });
                }
                a.Sheet.PreservedAreas = keep.ToArray(); EditorUtility.SetDirty(a.Sheet); a.Invalidate();
            }

            grass.Finish(); grassCleared = grass.Removed;
            EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
            var ledger = new Ledger { scene = scene.path, version = data.version, built = DateTime.Now.ToString("s"), sites = placed.ToArray(), npcs = npcFeet.Keys.ToArray(),
                points = data.points.Select(p => p.id).ToArray(), commissions = data.commissions.Select(c => c.id).ToArray() };
            File.WriteAllText(Path.Combine(AbsOutput, "placements-" + Path.GetFileNameWithoutExtension(scene.path) + ".json"), JsonUtility.ToJson(ledger, true));
            return "built " + placed.Count(p => p.status == "OK") + "/" + data.sites.Length + " sites, " + npcFeet.Count + " givers, " + data.points.Length + " points, " + data.commissions.Length + " commissions in " + scene.path +
                   "; dressing cleared " + placed.Sum(p => p.dressingRemoved) + "; grass seeds cleared " + grassCleared + (placed.Any(p => p.status != "OK") ? "; MISSING " + string.Join(",", placed.Where(p => p.status != "OK").Select(p => p.id)) : "") + "; backup " + backup;
        }

        // ---------- geometry of the world ----------

        static List<RouteLine> Routes(CompactWorldLayoutSO layout)
        {
            var places = layout.Places.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First().XZ);
            var list = new List<RouteLine>();
            foreach (var r in layout.Routes)
            {
                if (!places.ContainsKey(r.From) || !places.ContainsKey(r.To)) continue;
                var pts = new[] { places[r.From] }.Concat(r.Bends ?? Array.Empty<Vector2>()).Concat(new[] { places[r.To] }).ToArray();
                var cum = new float[pts.Length]; for (int i = 1; i < pts.Length; i++) cum[i] = cum[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
                list.Add(new RouteLine { id = r.Id, width = r.Width, pts = pts, cum = cum });
            }
            return list;
        }

        static (Vector2 at, Vector2 tangent) PointAt(RouteLine r, float u, float along = 0)
        {
            float s = Mathf.Clamp(u * r.cum[r.cum.Length - 1] + along, 0, r.cum[r.cum.Length - 1]);
            for (int i = 1; i < r.pts.Length; i++)
                if (r.cum[i] >= s || i == r.pts.Length - 1)
                {
                    float seg = Mathf.Max(1e-4f, r.cum[i] - r.cum[i - 1]), t = Mathf.Clamp01((s - r.cum[i - 1]) / seg);
                    return (Vector2.Lerp(r.pts[i - 1], r.pts[i], t), (r.pts[i] - r.pts[i - 1]).normalized);
                }
            return (r.pts[0], Vector2.up);
        }

        static float DistanceToPolyline(Vector2 p, Vector2[] pts)
        {
            float best = float.MaxValue;
            for (int i = 1; i < pts.Length; i++)
            {
                var a = pts[i - 1]; var d = pts[i] - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / Mathf.Max(1e-6f, d.sqrMagnitude));
                best = Mathf.Min(best, Vector2.Distance(p, a + d * t));
            }
            return best;
        }

        static bool IsTerrain(Collider c)
        {
            if (c is TerrainCollider) return true;
            for (var t = c.transform; t != null; t = t.parent) if (t.name.Contains("Terrain")) return true;
            return false;
        }

        // the terrain surface under xz (buildings, bridges and props above it are skipped)
        internal static bool Ground(Vector3 xz, Transform ignore, out RaycastHit hit)
        {
            foreach (var h in Physics.RaycastAll(new Vector3(xz.x, 1200f, xz.z), Vector3.down, 2400f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (ignore != null && h.transform.IsChildOf(ignore)) continue;
                if (!IsTerrain(h.collider)) continue;
                hit = h; return true;
            }
            hit = default; return false;
        }

        // anything solid standing on the ground inside the footprint (terrain itself excluded)
        static bool Obstructed(Vector3 centre, float radius, float top, Transform ignore)
        {
            var hits = Physics.OverlapBox(new Vector3(centre.x, top + 1.3f, centre.z), new Vector3(radius, 1.1f, radius), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            return hits.Any(c => !IsTerrain(c) && (ignore == null || !c.transform.IsChildOf(ignore)));
        }

        static Placed Solve(SiteSpec s, List<RouteLine> routes, DressingIndex index, Transform ignore, List<Placed> done)
        {
            var route = routes.FirstOrDefault(r => r.id == s.route) ?? throw new Exception("site " + s.id + ": no route " + s.route);
            bool house = s.kind.StartsWith("house"), jige = s.kind == "jige";
            float half = route.width * .5f;
            float[] offsets = house ? new[] { half + 9f, half + 11f, half + 13.5f, half + 16f } : jige ? new[] { half + 1.4f, half + 2.2f, half + 3.0f } : new[] { half + 3.0f, half + 4.0f, half + 5.2f, half + 6.4f };
            float radius = house ? 5.6f : jige ? 1.2f : 2.8f;
            float relief = house ? 1.7f : s.downhill ? 2.6f : 1.1f;
            float minClear = house ? 5f : jige ? .6f : 1.8f;
            Placed best = null; float bestScore = float.MaxValue;
            for (float along = -30f; along <= 30f; along += 3f)
            {
                var (c0, tan) = PointAt(route, s.u, along);
                var normal = new Vector2(tan.y, -tan.x);
                if (!Ground(new Vector3(c0.x, 0, c0.y), ignore, out var roadHit)) continue;
                foreach (float sign in new[] { 1f, -1f })
                    foreach (float off in offsets)
                    {
                        var xz = c0 + normal * sign * off;
                        if (!Ground(new Vector3(xz.x, 0, xz.y), ignore, out var hit) || hit.normal.y < .6f) continue;
                        float clear = routes.Min(r => DistanceToPolyline(xz, r.pts) - r.width * .5f);
                        if (clear < minClear) continue;
                        if (done.Any(d => d.status == "OK" && Vector2.Distance(new Vector2(d.centre.x, d.centre.z), xz) < radius + 6f)) continue;
                        float lo = hit.point.y, hi = hit.point.y; bool ok = true;
                        for (int k = 0; k < 8 && ok; k++)
                        {
                            float a = k * Mathf.PI / 4;
                            if (!Ground(new Vector3(xz.x + Mathf.Cos(a) * radius, 0, xz.y + Mathf.Sin(a) * radius), ignore, out var h2)) ok = false;
                            else { lo = Mathf.Min(lo, h2.point.y); hi = Mathf.Max(hi, h2.point.y); }
                        }
                        if (!ok || hi - lo > relief) continue;
                        if (Obstructed(hit.point, radius, hi, ignore)) continue;
                        int big = index.Count(hit.point, radius + 1f, true);
                        float drop = roadHit.point.y - hit.point.y;   // > 0: the ground falls away from the road
                        float score = Mathf.Abs(along) * .12f + off * .15f + big * 5f + (hi - lo) * 1.5f +
                                      (s.downhill ? (drop < .5f ? 12f : 0f) - Mathf.Clamp(drop, 0, 3) * 1.5f : Mathf.Abs(drop) * 1.2f);
                        if (score >= bestScore) continue;
                        bestScore = score;
                        var toRoad = new Vector3(roadHit.point.x - hit.point.x, 0, roadHit.point.z - hit.point.z);
                        best = new Placed { id = s.id, kind = s.kind, theme = s.theme, route = s.route, centre = hit.point, road = roadHit.point,
                            yaw = Mathf.Atan2(toRoad.x, toRoad.z) * Mathf.Rad2Deg, offset = off * sign, slope = hi - lo, clearance = clear, drop = drop };
                    }
            }
            return best;
        }

        // ---------- dressing (shared art sheet) ----------

        sealed class DressingIndex
        {
            readonly CompactRebuildArtRenderer[] arts;
            readonly Dictionary<long, List<(int art, int index)>> grid = new Dictionary<long, List<(int, int)>>();
            const float Cell = 8f;
            static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            public DressingIndex(CompactRebuildArtRenderer[] arts)
            {
                this.arts = arts;
                for (int a = 0; a < arts.Length; a++)
                {
                    var fp = arts[a].Sheet.FixedPlacements;
                    for (int i = 0; i < fp.Length; i++)
                    {
                        if (fp[i] == null) continue; var p = fp[i].Position;
                        long k = Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
                        if (!grid.TryGetValue(k, out var list)) grid[k] = list = new List<(int, int)>();
                        list.Add((a, i));
                    }
                }
            }
            Sheet.Prototype Proto(int art, Sheet.FixedPlacement f) => arts[art].Sheet.Prototypes.FirstOrDefault(p => p.Id == f.PrototypeId);
            IEnumerable<(int art, int index)> Near(Vector3 c, float r)
            {
                int x0 = Mathf.FloorToInt((c.x - r) / Cell), x1 = Mathf.FloorToInt((c.x + r) / Cell), z0 = Mathf.FloorToInt((c.z - r) / Cell), z1 = Mathf.FloorToInt((c.z + r) / Cell);
                for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++) if (grid.TryGetValue(Key(x, z), out var list)) foreach (var e in list) yield return e;
            }
            public int Count(Vector3 c, float r, bool bigOnly)
            {
                int n = 0;
                foreach (var (a, i) in Near(c, r))
                {
                    var f = arts[a].Sheet.FixedPlacements[i]; if (f == null) continue;
                    if (new Vector2(f.Position.x - c.x, f.Position.z - c.z).magnitude > r) continue;
                    var proto = Proto(a, f); if (bigOnly && proto != null && proto.Category == Sheet.Kind.Grass) continue;
                    n++;
                }
                return n;
            }
            // grass (only) inside a circle: a trodden patch where people stand
            public int ClearGrass(Vector3 c, float r)
            {
                int removed = 0;
                for (int a = 0; a < arts.Length; a++)
                {
                    var sheet = arts[a].Sheet; var drop = new HashSet<int>();
                    foreach (var (art, i) in Near(c, r))
                    {
                        if (art != a) continue; var f = sheet.FixedPlacements[i]; if (f == null) continue;
                        var proto = Proto(a, f); if (proto == null || proto.Category != Sheet.Kind.Grass) continue;
                        if (new Vector2(f.Position.x - c.x, f.Position.z - c.z).magnitude <= r) drop.Add(i);
                    }
                    if (drop.Count == 0) continue;
                    sheet.FixedPlacements = sheet.FixedPlacements.Where((f, i) => !drop.Contains(i)).ToArray(); removed += drop.Count; EditorUtility.SetDirty(sheet);
                    Reindex(a);
                }
                return removed;
            }
            void Reindex(int a)
            {
                foreach (var list in grid.Values) list.RemoveAll(e => e.art == a);
                var fp = arts[a].Sheet.FixedPlacements;
                for (int i = 0; i < fp.Length; i++)
                {
                    if (fp[i] == null) continue; var p = fp[i].Position; long k = Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
                    if (!grid.TryGetValue(k, out var list)) grid[k] = list = new List<(int, int)>(); list.Add((a, i));
                }
            }
            // removes every placement whose base lies inside the prop bounds (padded by its own radius + pad)
            public int ClearUnder(Transform site, float pad)
            {
                var b = BoundsOf(site); if (b.size == Vector3.zero) return 0;
                int removed = 0;
                for (int a = 0; a < arts.Length; a++)
                {
                    var sheet = arts[a].Sheet; var drop = new HashSet<int>();
                    foreach (var (art, i) in Near(b.center, Mathf.Max(b.extents.x, b.extents.z) + 6f))
                    {
                        if (art != a) continue; var f = sheet.FixedPlacements[i]; if (f == null) continue;
                        var proto = Proto(a, f); float rr = (proto != null ? proto.Radius : .5f) * f.Scale + (proto != null && proto.Category == Sheet.Kind.Grass ? .1f : pad);
                        if (f.Position.x < b.min.x - rr || f.Position.x > b.max.x + rr || f.Position.z < b.min.z - rr || f.Position.z > b.max.z + rr) continue;
                        drop.Add(i);
                    }
                    if (drop.Count == 0) continue;
                    sheet.FixedPlacements = sheet.FixedPlacements.Where((f, i) => !drop.Contains(i)).ToArray();
                    removed += drop.Count; EditorUtility.SetDirty(sheet);
                    Reindex(a);   // indices shifted
                }
                return removed;
            }
        }

        // ---------- #266 grass field (seeds per 32 m cell): trodden patches at givers and house yards ----------

        // each #266 seed draws a 3.4 x 3.2 m patch, so a bare radius r needs seeds cleared out to r + half a patch
        const float GrassPatchHalf = 1.7f;
        sealed class GrassClear
        {
            readonly List<Oheangbu.App.World.CompactGrassField266> fields;
            readonly Oheangbu.App.World.CompactGrassRenderer266[] renderers;
            public int Removed;
            public GrassClear()
            {
                renderers = Object.FindObjectsByType<Oheangbu.App.World.CompactGrassRenderer266>(FindObjectsSortMode.None);
                fields = renderers.Select(r => r.Field).Where(f => f != null).Distinct().ToList();
            }
            public IEnumerable<string> Paths => fields.Select(f => AssetDatabase.GetAssetPath(f));
            // removes seeds inside the (yawed) rectangle or circle around c
            public void Circle(Vector3 c, float r) => Remove(c, p => new Vector2(p.x - c.x, p.z - c.z).sqrMagnitude <= r * r, r);
            public void Box(Transform frame, Vector3 localCentre, Vector2 half)
            {
                var c = frame.TransformPoint(localCentre);
                Remove(c, p => { var l = frame.InverseTransformPoint(p) - localCentre; return Mathf.Abs(l.x) <= half.x && Mathf.Abs(l.z) <= half.y; }, Mathf.Max(half.x, half.y) * 1.5f);
            }
            void Remove(Vector3 c, Func<Vector3, bool> inside, float reach)
            {
                foreach (var f in fields)
                {
                    var at = f.Coordinate(c); int cells = Mathf.CeilToInt(reach / Oheangbu.App.World.CompactGrassField266.CellSize) + 1; bool changed = false;
                    for (int z = at.y - cells; z <= at.y + cells; z++)
                        for (int x = at.x - cells; x <= at.x + cells; x++)
                        {
                            int i = f.Index(x, z); if (i < 0 || f.Cells[i] == null || f.Cells[i].Seeds.Length == 0) continue;
                            var seeds = f.Cells[i].Seeds; var keep = seeds.Where(sd => Mathf.Abs(sd.Position.y - c.y) > 3f || !inside(sd.Position)).ToArray();
                            if (keep.Length == seeds.Length) continue;
                            Removed += seeds.Length - keep.Length; f.Count -= seeds.Length - keep.Length; f.Cells[i].Seeds = keep; changed = true;
                        }
                    if (changed) EditorUtility.SetDirty(f);
                }
            }
            public void Finish() { foreach (var r in renderers) r.Invalidate(); }
        }

        // ---------- materials ----------

        internal sealed class Mats303
        {
            public readonly Material Wood, Charred, Hemp, Paper, Lacquer, Ash, Stone, FieldStone, Iron;
            public Mats303()
            {
                var wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/ThatchedInn/Materials/house_Re_Wood_01_Muted.mat");
                Stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/ThatchedInn/Materials/house_Re_Stone_Muted.mat");
                Wood = Copy("WeatheredWood303", wood, new Color(.52f, .49f, .43f));
                FieldStone = Copy("FieldStone303", Stone, new Color(.36f, .35f, .33f));
                Charred = Copy("CharredWood303", wood, new Color(.11f, .10f, .095f));
                Hemp = Lit("Hemp303", new Color(.43f, .39f, .30f));
                Paper = Lit("OiledPaper303", new Color(.62f, .56f, .42f));
                Lacquer = Lit("Lacquer303", new Color(.07f, .045f, .035f), .35f);
                Ash = Lit("Ash303", new Color(.16f, .155f, .15f));
                Iron = Lit("PittedIron303", new Color(.19f, .19f, .18f), .2f);
            }
            static Material Copy(string name, Material source, Color c)
            {
                string path = AssetFolder + "/Materials/" + name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                EditorUtility.SetDirty(m); return m;
            }
            static Material Lit(string name, Color c, float smooth = .08f)
            {
                string path = AssetFolder + "/Materials/" + name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
                m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); EditorUtility.SetDirty(m); return m;
            }
        }

        // ---------- small scene helpers ----------

        internal static Bounds BoundsOf(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static int StableHash(string s) { unchecked { int h = 23; foreach (char c in s) h = h * 31 + c; return h; } }
        static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material m, bool collider)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = m;
            if (collider) { var bc = go.AddComponent<BoxCollider>(); bc.center = mesh.bounds.center; bc.size = mesh.bounds.size; }
            return go;
        }

        static Mesh SaveMesh(MeshBuilder303 mb, string name)
        {
            string path = AssetFolder + "/Meshes/" + name + ".asset";
            var mesh = mb.ToMesh(name);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(mesh, path); return mesh;
        }

        // copies of a prefab's meshes (no scripts), normalised to `size` and seated on the ground
        static Transform PrefabCopy(Transform parent, string name, string path, Vector3 local, float yaw, Vector3 tilt, float size, Material overrideMat = null, bool collider = false)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new Exception("missing prefab " + path);
            var t = new GameObject(name).transform; t.SetParent(parent, false);
            foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = f.GetComponent<MeshRenderer>(); if (f.sharedMesh == null || r == null) continue;
                var g = new GameObject(f.name, typeof(MeshFilter), typeof(MeshRenderer)); g.transform.SetParent(t, false);
                var m = prefab.transform.worldToLocalMatrix * f.transform.localToWorldMatrix;
                g.transform.localPosition = m.GetColumn(3); g.transform.localRotation = m.rotation; g.transform.localScale = m.lossyScale;
                g.GetComponent<MeshFilter>().sharedMesh = f.sharedMesh;
                g.GetComponent<MeshRenderer>().sharedMaterials = overrideMat != null ? r.sharedMaterials.Select(_ => overrideMat).ToArray() : r.sharedMaterials;
                if (collider) g.AddComponent<BoxCollider>();
            }
            var b = BoundsOf(t); float big = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            t.localScale = Vector3.one * (size / Mathf.Max(.01f, big));
            t.localRotation = Quaternion.Euler(tilt.x, yaw, tilt.z);
            Seat(t, parent.TransformPoint(local), .02f);
            return t;
        }

        // moves t so its lowest renderer point rests on the terrain at `at` (minus sink)
        static void Seat(Transform t, Vector3 at, float sink)
        {
            Physics.SyncTransforms();
            var b = BoundsOf(t);
            float ground = Ground(at, t.root, out var hit) ? hit.point.y : at.y;
            t.position += new Vector3(at.x - b.center.x, ground - sink - b.min.y, at.z - b.center.z);
        }
    }
}
