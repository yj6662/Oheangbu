using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 collide (D308-29 answer 2 "소품 충돌을 덜어 냄" + D308-31 "프롭충돌 정교화"; SPEC-NATURE-COLLISION-306 §308).
    // Queue entry Run(string). Refusals are "refused: ..." strings, never a dialog. Edit mode only. AssetDatabase.SaveAssets is never called.
    //   fits                    read only: which sheet prototypes of the ACTIVE scene take a measured stand-in (Profile.Fits308), which keep
    //                           the code rule, which fits are stale (prototype Size differs) or name no prototype
    //   plan:<alias>            read only: the colliders that would be switched off and the lump boxes that would be added
    //   apply:<alias>           backs the scene file up, switches the listed colliders off (a property override on the scene instance:
    //                           no prefab, no pack asset is edited), adds one BoxCollider per assembled prop under the root `Collide308`,
    //                           saves that scene only, appends the ledger
    //   apply-all               plans the three scenes first (any refusal = nothing changes), then applies in order
    //   verify:<alias>          post-conditions, read only; records a digest of the result in the ledger
    //   verify-all              no scene is opened: compares the three recorded digests (the three scenes must hold the same result)
    //   revert:<alias>          undoes the newest un-reverted apply from the ledger values (colliders back on, lumps deleted)
    // alias = architecture296 | folklore298 | main. Data: Art/World/Compact/Rebuild/Collide308/collide308_props.json (deployed by
    // collide_copy.py; the stage copy Tools/Unity/Stage308_collide/Data/ is read when that file is absent). Ledger and scene backups:
    // Art/World/Compact/Rebuild/Collide308/ (LIVE DATA: never clean this folder up while an apply is un-reverted).
    // Rule modes:  off  = every non-trigger collider under each matched object is disabled (small prop / thin member)
    //              lump = the same, plus ONE box around the matched objects of a cluster (objects whose bounds lie within `link` metres;
    //                     whole = each matched object is its own assembled prop). The box frame is the first member's yaw (frame "pca": the members' principal axes, for runs on a slope); its bottom
    //                     goes down to the ground under it (at most ground.reach), so no lump hangs in the air.
    //              orphan (with off) = only matched objects that draw NOTHING: their name is no FixedPlacement Id of any vegetation
    //                     sheet held by a CompactRebuildArtRenderer of the scene, and no tree / rock placement stands within orphanRadius
    //                     (colliders the #261 detail pass left behind where the tree or rock was later taken off the sheet)
    // `leave` rows are never touched and verify fails when one of their colliders is off (the tutorial boss room cover).
    // NavMesh: the bakes of this project collect PhysicsColliders, so the baked NavMesh still has the old holes until the next bake
    // (nothing here bakes). Play stand-ins of the vegetation sheets are separate (CompactNaturalSolids) and are not NavMesh sources.
    public static class Collide308
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const string StageData = "Tools/Unity/Stage308_collide/Data/collide308_props.json";
        const string ProfilePath = "Assets/_Project/Data/World/NaturalSolidProfile306.asset";
        static string Folder => Path.Combine(BuildingAudit308.RepoRoot, "Art", "World", "Compact", "Rebuild", "Collide308");
        static string DataFile { get { string f = Path.Combine(Folder, "collide308_props.json"); return File.Exists(f) ? f : Path.Combine(BuildingAudit308.RepoRoot, StageData); } }
        static string LedgerFile(string alias) => Path.Combine(Folder, "ledger-" + alias + ".json");

        [Serializable] sealed class SceneRow { public string alias = "", path = ""; }
        [Serializable] sealed class Ground { public float reach = 1.5f, probeUp = .6f, sink = .05f; }
        [Serializable] sealed class Rule
        {
            public string id = "", parent = "", mode = "", why = ""; public string[] names = Array.Empty<string>();
            public float link, trim, orphanRadius = .5f; public bool whole, prefix, orphan; public string frame = "";
        }
        [Serializable] sealed class Leave { public string parent = "", why = ""; public string[] names = Array.Empty<string>(); }
        [Serializable] sealed class Data
        {
            public string version = "", lumpRoot = "Collide308"; public SceneRow[] scenes = Array.Empty<SceneRow>(); public string[] protectedTokens = Array.Empty<string>();
            public Ground ground = new Ground(); public float minThickness = .2f, poseTolerance = .002f;
            public Rule[] rules = Array.Empty<Rule>(); public Leave[] leave = Array.Empty<Leave>();
        }
        [Serializable] sealed class Off { public string rule = "", key = "", type = ""; public int index; public string state = ""; }          // state: apply | already
        [Serializable] sealed class Lump
        {
            public string rule = "", name = "", state = "", note = ""; public Vector3 centre, size; public Quaternion rot = Quaternion.identity; public int layer, members; public string[] memberKeys = Array.Empty<string>();
        }
        [Serializable] sealed class Op
        {
            public string utc = "", command = "", alias = "", scene = "", status = "", detail = "", backup = "", shaBefore = "", shaAfter = "", dataSha = "", digest = "", revertedUtc = ""; public bool reverted;
            public List<Off> off = new List<Off>(); public List<Lump> lumps = new List<Lump>();
        }
        [Serializable] sealed class Ledger { public string alias = "", scene = "", verifyUtc = "", verifyDigest = "", verifyStatus = ""; public List<Op> ops = new List<Op>(); }
        sealed class Plan { public List<Off> off = new List<Off>(); public List<Lump> lumps = new List<Lump>(); public List<string> blockers = new List<string>(), notes = new List<string>(); }

        public static string Run(string command)
        {
            command = (command ?? "").Trim();
            try
            {
                var a = command.Split(':');
                switch (a[0])
                {
                    case "fits": return Fits();
                    case "plan": return a.Length > 1 ? PlanCommand(a[1]) : Usage;
                    case "apply": return a.Length > 1 ? Apply(a[1], command) : Usage;
                    case "apply-all": return ApplyAll();
                    case "verify": return a.Length > 1 ? Verify(a[1]) : Usage;
                    case "verify-all": return VerifyAll();
                    case "revert": return a.Length > 1 ? Revert(a[1]) : Usage;
                    default: return Usage;
                }
            }
            catch (Exception e) { Debug.LogException(e); return "FAILED: " + e.GetType().Name + ": " + e.Message; }
        }
        const string Usage = "refused: Collide308 fits | plan:<alias> | apply:<alias> | apply-all | verify:<alias> | verify-all | revert:<alias>   (alias = architecture296 | folklore298 | main)";

        // ------------------------------------------------------------------ data / ledger

        static Data Load(out string error)
        {
            error = null; string f = DataFile;
            if (!File.Exists(f)) { error = "data missing: " + f; return null; }
            var d = JsonUtility.FromJson<Data>(File.ReadAllText(f).TrimStart((char)0xFEFF));
            if (d == null || d.rules == null || d.rules.Length == 0) { error = "no rules in " + f; return null; }
            foreach (var r in d.rules)
            {
                if (string.IsNullOrEmpty(r.id) || string.IsNullOrEmpty(r.parent) || r.names == null || r.names.Length == 0) { error = "rule without id / parent / names in " + f; return null; }
                if (r.mode != "off" && r.mode != "lump") { error = "rule " + r.id + ": mode must be off or lump"; return null; }
                if (r.orphan && r.mode != "off") { error = "rule " + r.id + ": orphan goes with mode off"; return null; }
            }
            if (d.rules.Select(r => r.id).Distinct().Count() != d.rules.Length) { error = "duplicate rule id in " + f; return null; }
            return d;
        }
        static Ledger LoadLedger(string alias)
        {
            string f = LedgerFile(alias);
            return File.Exists(f) ? JsonUtility.FromJson<Ledger>(File.ReadAllText(f)) ?? new Ledger { alias = alias } : new Ledger { alias = alias };
        }
        static void SaveLedger(Ledger l) { Directory.CreateDirectory(Folder); File.WriteAllText(LedgerFile(l.alias), JsonUtility.ToJson(l, true), new UTF8Encoding(false)); }
        static Op Applied(Ledger l) => l.ops.LastOrDefault(o => o.status == "applied" && !o.reverted);

        static string Open(Data d, string alias, out Scene scene, out string previous, out bool opened)
        {
            scene = default; previous = SceneManager.GetActiveScene().path; opened = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only (Play is running)";
            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "refused: scripts are compiling or failed to compile";
            var row = d.scenes.FirstOrDefault(s => s.alias == alias);
            if (row == null) return "refused: unknown scene alias " + alias + " (" + string.Join(", ", d.scenes.Select(s => s.alias)) + ")";
            if (d.protectedTokens.Any(t => t.Length > 0 && row.path.Contains(t)) || row.path.EndsWith("W_Demo_Compact.unity", StringComparison.Ordinal)) return "refused: protected scene " + row.path;
            if (!File.Exists(BuildingAudit308.Abs(row.path))) return "refused: scene missing " + row.path;
            string dirty = BuildingAudit308.DirtyOpenScene();
            if (dirty != null) return "refused: " + dirty + " has unsaved changes";
            var active = SceneManager.GetActiveScene();
            if (active.path == row.path) { scene = active; return null; }
            scene = EditorSceneManager.OpenScene(row.path, OpenSceneMode.Single); opened = true;
            return null;
        }
        static void GoBack(string previous, Scene scene, bool opened, bool force = false)
        {
            if (!opened || string.IsNullOrEmpty(previous) || previous == scene.path || !File.Exists(BuildingAudit308.Abs(previous))) return;
            if (scene.isDirty && !force) return;   // a failed save leaves the target open so nothing is lost
            EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
        }

        // a Play exit can leave a harness save suffix on the session in memory: never write it into the scene file (#307 rule,
        // the same guard as BuildingFix308)
        static void ClearStaleSuffix(Scene scene)
        {
            foreach (var g in scene.GetRootGameObjects())
                foreach (var session in g.GetComponentsInChildren<WorldMacroPlaytestSession>(true))
                    if (WorldMacroPlaytestSession.StaleHarnessSuffix307(session.TestSaveSuffix)) { session.TestSaveSuffix = ""; EditorUtility.SetDirty(session); }
        }

        // ------------------------------------------------------------------ matching

        static Transform Find(Scene scene, string path)
        {
            var segs = path.Split('/');
            var t = scene.GetRootGameObjects().FirstOrDefault(g => g.name == segs[0])?.transform;
            for (int i = 1; i < segs.Length && t != null; i++)
            {
                Transform found = null;
                for (int c = 0; c < t.childCount; c++) if (t.GetChild(c).name == segs[i]) { found = t.GetChild(c); break; }
                t = found;
            }
            return t;
        }
        // direct children of the rule's parent whose name is listed (exact, or a listed prefix when the rule says so), in sibling order
        static List<Transform> Matches(Scene scene, string parent, string[] names, bool prefix)
        {
            var list = new List<Transform>(); var p = Find(scene, parent); if (p == null) return list;
            for (int c = 0; c < p.childCount; c++)
            {
                var ch = p.GetChild(c);
                if (names.Any(n => prefix ? ch.name.StartsWith(n, StringComparison.Ordinal) : ch.name == n)) list.Add(ch);
            }
            return list;
        }
        // what the vegetation sheets of the scene draw: every FixedPlacement Id, and the XZ of the tree / rock placements on a 4 m grid
        sealed class Drawn
        {
            public readonly HashSet<string> ids = new HashSet<string>(); public readonly Dictionary<long, List<Vector2>> grid = new Dictionary<long, List<Vector2>>(); public int sheets;
            static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            public void Add(Vector3 p) { long k = Key(Mathf.FloorToInt(p.x / 4), Mathf.FloorToInt(p.z / 4)); if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<Vector2>(); l.Add(new Vector2(p.x, p.z)); }
            public bool Near(Vector3 p, float r)
            {
                int cx = Mathf.FloorToInt(p.x / 4), cz = Mathf.FloorToInt(p.z / 4); var q = new Vector2(p.x, p.z);
                for (int x = cx - 1; x <= cx + 1; x++) for (int z = cz - 1; z <= cz + 1; z++) if (grid.TryGetValue(Key(x, z), out var l)) foreach (var v in l) if ((v - q).sqrMagnitude <= r * r) return true;
                return false;
            }
        }
        static Drawn ReadDrawn(Scene scene)
        {
            var d = new Drawn(); var seen = new HashSet<WorldMacroDressingSheetSO>();
            foreach (var g in scene.GetRootGameObjects()) foreach (var r in g.GetComponentsInChildren<CompactRebuildArtRenderer>(true))
            {
                var sheet = r.Sheet; if (sheet == null || !seen.Add(sheet)) continue;
                d.sheets++; var kind = new Dictionary<string, WorldMacroDressingSheetSO.Kind>();
                foreach (var p in sheet.Prototypes ?? Array.Empty<WorldMacroDressingSheetSO.Prototype>()) if (p != null && !string.IsNullOrEmpty(p.Id) && !kind.ContainsKey(p.Id)) kind.Add(p.Id, p.Category);
                foreach (var fp in sheet.FixedPlacements ?? Array.Empty<WorldMacroDressingSheetSO.FixedPlacement>())
                {
                    if (fp == null) continue; if (!string.IsNullOrEmpty(fp.Id)) d.ids.Add(fp.Id);
                    if (fp.PrototypeId != null && kind.TryGetValue(fp.PrototypeId, out var k) && (k == WorldMacroDressingSheetSO.Kind.Tree || k == WorldMacroDressingSheetSO.Kind.Rock)) d.Add(fp.Position);
                }
            }
            return d;
        }
        // the objects a rule acts on: its matches, and for an orphan rule only those that draw nothing
        static List<Transform> Targets(Scene scene, Rule rule, ref Drawn drawn, List<string> notes)
        {
            var hits = Matches(scene, rule.parent, rule.names, rule.prefix);
            if (!rule.orphan) return hits;
            if (drawn == null) drawn = ReadDrawn(scene);
            var d = drawn; int all = hits.Count;
            hits = d.sheets == 0 ? new List<Transform>() : hits.Where(h => !d.ids.Contains(h.name) && !d.Near(h.position, rule.orphanRadius)).ToList();
            notes?.Add("rule " + rule.id + ": " + hits.Count + " of " + all + " matched object(s) draw nothing (no sheet Id, no tree / rock placement within " + rule.orphanRadius.ToString("F1", Inv) + " m; " + d.sheets + " sheet(s), " + d.ids.Count + " placement Id(s))"
                + (d.sheets == 0 ? " - NO vegetation sheet found in the scene: nothing is treated as an orphan" : ""));
            return hits;
        }
        static IEnumerable<Collider> Solid(Transform t) => t.GetComponentsInChildren<Collider>(true).Where(c => c != null && !c.isTrigger);
        static int IndexOf(Collider c) => Array.IndexOf(c.GetComponents<Collider>(), c);

        // world points that say where an object is: box collider corners, else the vertices of its renderers' meshes, else collider bounds
        static int boundsOnly;   // meshes Points() could only read the bounds of (reset per plan; a note, not a state)
        static List<Vector3> Points(Transform t)
        {
            var pts = new List<Vector3>();
            foreach (var b in t.GetComponentsInChildren<BoxCollider>(true))
            {
                if (b.isTrigger) continue;
                for (int k = 0; k < 8; k++) pts.Add(b.transform.TransformPoint(b.center + Vector3.Scale(b.size * .5f, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1))));
            }
            if (pts.Count > 0) return pts;
            foreach (var f in t.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = f.GetComponent<MeshRenderer>(); if (f.sharedMesh == null || r == null || !r.enabled) continue;
                // LOD0 only: lower LODs of the same object would only repeat it
                var lod = f.GetComponentInParent<LODGroup>();
                if (lod != null) { var lods = lod.GetLODs(); if (lods.Length > 0 && !lods[0].renderers.Contains(r)) continue; }
                // edit mode reads the vertices of an imported mesh whatever its Read/Write setting; when that gives nothing the mesh
                // bounds stand in (then `trim` cannot shave a ragged heap - the plan line says "bounds only")
                var m = f.sharedMesh; Vector3[] v = null;
                try { v = m.vertices; } catch (Exception) { v = null; }
                if (v != null && v.Length > 0) { int step = Mathf.Max(1, v.Length / 6000); for (int i = 0; i < v.Length; i += step) pts.Add(f.transform.TransformPoint(v[i])); }
                else { boundsOnly++; var b = m.bounds; for (int k = 0; k < 8; k++) pts.Add(f.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1)))); }
            }
            if (pts.Count > 0) return pts;
            foreach (var c in Solid(t)) { var b = c.bounds; for (int k = 0; k < 8; k++) pts.Add(b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1))); }
            return pts;
        }
        static Bounds BoundsOf(List<Vector3> pts) { var b = new Bounds(pts[0], Vector3.zero); foreach (var p in pts) b.Encapsulate(p); return b; }
        static float Gap(Bounds a, Bounds b)
        {
            float dx = Mathf.Max(0, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x)), dy = Mathf.Max(0, Mathf.Max(a.min.y - b.max.y, b.min.y - a.max.y)), dz = Mathf.Max(0, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        // Principal axes of a point set as a rotation: local Y = the axis nearest world up (pointing up), local X = the longest of the
        // other two. Jacobi sweeps on the 3 x 3 covariance; the same points give the same frame in every scene.
        static Quaternion Principal(IEnumerable<Vector3> points)
        {
            var list = points.ToList(); if (list.Count < 3) return Quaternion.identity;
            var mean = Vector3.zero; foreach (var p in list) mean += p; mean /= list.Count;
            var a = new double[3, 3];
            foreach (var p in list) { var q = p - mean; double[] v = { q.x, q.y, q.z }; for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) a[i, j] += v[i] * v[j]; }
            var e = new double[3, 3]; for (int i = 0; i < 3; i++) e[i, i] = 1;
            for (int sweep = 0; sweep < 32; sweep++)
            {
                double offd = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]); if (offd < 1e-12) break;
                for (int i = 0; i < 2; i++) for (int j = i + 1; j < 3; j++)
                {
                    if (Math.Abs(a[i, j]) < 1e-15) continue;
                    double theta = .5 * Math.Atan2(2 * a[i, j], a[j, j] - a[i, i]), c = Math.Cos(theta), s = Math.Sin(theta);
                    for (int k = 0; k < 3; k++) { double aki = a[k, i], akj = a[k, j]; a[k, i] = c * aki - s * akj; a[k, j] = s * aki + c * akj; }
                    for (int k = 0; k < 3; k++) { double aik = a[i, k], ajk = a[j, k]; a[i, k] = c * aik - s * ajk; a[j, k] = s * aik + c * ajk; }
                    for (int k = 0; k < 3; k++) { double eki = e[k, i], ekj = e[k, j]; e[k, i] = c * eki - s * ekj; e[k, j] = s * eki + c * ekj; }
                }
            }
            var axes = new[] { new Vector3((float)e[0, 0], (float)e[1, 0], (float)e[2, 0]), new Vector3((float)e[0, 1], (float)e[1, 1], (float)e[2, 1]), new Vector3((float)e[0, 2], (float)e[1, 2], (float)e[2, 2]) };
            double[] val = { a[0, 0], a[1, 1], a[2, 2] };
            int up = 0; for (int i = 1; i < 3; i++) if (Mathf.Abs(axes[i].y) > Mathf.Abs(axes[up].y)) up = i;
            int o1 = (up + 1) % 3, o2 = (up + 2) % 3, lng = val[o1] >= val[o2] ? o1 : o2;
            var y = axes[up].normalized; if (y.y < 0) y = -y;
            var x = axes[lng] - y * Vector3.Dot(axes[lng], y); if (x.sqrMagnitude < 1e-10f) return Quaternion.identity; x.Normalize();
            // a fixed sign so the frame does not flip between runs: X points towards +x (or +z when it lies along z)
            if (Mathf.Abs(x.x) >= Mathf.Abs(x.z) ? x.x < 0 : x.z < 0) x = -x;
            var z = Vector3.Cross(x, y); return Quaternion.LookRotation(z, y);
        }
        static float Percentile(List<float> sorted, float q) { if (sorted.Count == 0) return 0; float i = Mathf.Clamp01(q) * (sorted.Count - 1); int a = Mathf.FloorToInt(i), b = Mathf.Min(sorted.Count - 1, a + 1); return Mathf.Lerp(sorted[a], sorted[b], i - a); }

        // ------------------------------------------------------------------ plan

        static Plan MakePlan(Data d, Scene scene, Ledger ledger)
        {
            var plan = new Plan(); var prior = Applied(ledger); Physics.SyncTransforms(); Drawn drawn = null;
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == d.lumpRoot);
            if (root != null && prior == null) plan.blockers.Add("a root named " + d.lumpRoot + " exists but the ledger holds no applied op (not ours: rename it or restore the ledger)");
            // every collider any rule takes off: the ground probes ignore all of them, so a plan made before the apply and one made
            // after it (when they are off and the physics scene no longer holds them) find the same ground and the same lumps
            var perRule = new List<List<Transform>>(); var leaving = new HashSet<Collider>();
            foreach (var rule in d.rules) { var t = Targets(scene, rule, ref drawn, plan.notes); perRule.Add(t); foreach (var h in t) foreach (var c in h.GetComponentsInChildren<Collider>(true)) leaving.Add(c); }
            for (int ri = 0; ri < d.rules.Length; ri++)
            {
                var rule = d.rules[ri]; var hits = perRule[ri];
                if (hits.Count == 0) { if (!rule.orphan) plan.blockers.Add("rule " + rule.id + ": nothing matches " + rule.parent + "/{" + string.Join(",", rule.names) + "}"); continue; }
                foreach (var h in hits)
                {
                    string path = BuildingAudit308.PathOf(h);
                    if (d.protectedTokens.Any(t => t.Length > 0 && path.Contains(t))) plan.blockers.Add("rule " + rule.id + ": protected object " + path);
                }
                foreach (var h in hits)
                    foreach (var c in h.GetComponentsInChildren<Collider>(true))
                    {
                        if (c == null || c.isTrigger) continue;
                        string key = BuildingAudit308.KeyOf(c.transform); int index = IndexOf(c); string type = c.GetType().Name;
                        bool listed = prior != null && prior.off.Any(o => o.key == key && o.index == index && o.type == type);
                        if (c.enabled) plan.off.Add(new Off { rule = rule.id, key = key, index = index, type = type, state = "apply" });
                        else if (listed) plan.off.Add(new Off { rule = rule.id, key = key, index = index, type = type, state = "already" });
                        // a collider that was off before this tool ever ran is not ours: it is neither listed nor reverted
                    }
                if (rule.mode != "lump") continue;
                // clusters: whole = one per matched object; else single linkage on the bounds gap
                boundsOnly = 0; var pts = hits.Select(Points).ToList(); if (boundsOnly > 0) plan.notes.Add("rule " + rule.id + ": " + boundsOnly + " mesh(es) gave no vertices - bounds only, trim has no effect there");
                var keep = Enumerable.Range(0, hits.Count).Where(i => pts[i].Count > 0).ToList();
                if (keep.Count < hits.Count) plan.notes.Add("rule " + rule.id + ": " + (hits.Count - keep.Count) + " matched object(s) have no collider or mesh to measure (no lump for them)");
                var bounds = keep.ToDictionary(i => i, i => BoundsOf(pts[i])); var group = keep.ToDictionary(i => i, i => i);
                int Root(int i) { while (group[i] != i) i = group[i]; return i; }
                if (!rule.whole && rule.link > 0)
                    foreach (int i in keep) foreach (int j in keep) if (i < j && Root(i) != Root(j) && Gap(bounds[i], bounds[j]) <= rule.link) group[Root(j)] = Root(i);
                var clusters = keep.GroupBy(Root).Select(g => g.OrderBy(i => i).ToList()).ToList();
                var made = new List<Lump>();
                foreach (var cl in clusters)
                {
                    // frame: the first member's yaw (level furniture), or - rule.frame "pca" - the principal axes of the members' points,
                    // so a fence run or a row of baulks on a slope gets a box that follows the slope instead of a level one full of air
                    var first = hits[cl[0]]; var rot = rule.frame == "pca" ? Principal(cl.SelectMany(i => pts[i])) : Quaternion.Euler(0, first.eulerAngles.y, 0); var inv = Quaternion.Inverse(rot);
                    var xs = new List<float>(); var ys = new List<float>(); var zs = new List<float>();
                    foreach (int i in cl) foreach (var p in pts[i]) { var q = inv * p; xs.Add(q.x); ys.Add(q.y); zs.Add(q.z); }
                    xs.Sort(); ys.Sort(); zs.Sort(); float t = Mathf.Clamp(rule.trim, 0, .4f);
                    var lo = new Vector3(Percentile(xs, t), ys[0], Percentile(zs, t)); var hi = new Vector3(Percentile(xs, 1 - t), Percentile(ys, 1 - t), Percentile(zs, 1 - t));
                    // thin walls are widened to minThickness about their middle (a fence run)
                    if (hi.x - lo.x < d.minThickness) { float m = (hi.x + lo.x) * .5f; lo.x = m - d.minThickness * .5f; hi.x = m + d.minThickness * .5f; }
                    if (hi.z - lo.z < d.minThickness) { float m = (hi.z + lo.z) * .5f; lo.z = m - d.minThickness * .5f; hi.z = m + d.minThickness * .5f; }
                    // ground under the footprint: the lowest surface (not a member, not a lump) within the probe window
                    // how far the bottom hangs above the first surface under its corners and middle (largest gap; lumps and every collider a rule takes off are ignored)
                    float hang = float.NaN; string note = "";
                    foreach (var corner in new[] { new Vector2(lo.x, lo.z), new Vector2(hi.x, lo.z), new Vector2(lo.x, hi.z), new Vector2(hi.x, hi.z), new Vector2((lo.x + hi.x) * .5f, (lo.z + hi.z) * .5f) })
                    {
                        var bottom = rot * new Vector3(corner.x, lo.y, corner.y);
                        foreach (var h in Physics.RaycastAll(bottom + Vector3.up * d.ground.probeUp, Vector3.down, d.ground.probeUp + d.ground.reach, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                        {
                            if (leaving.Contains(h.collider) || (root != null && h.collider.transform.IsChildOf(root.transform))) continue;
                            float gap = bottom.y - h.point.y; if (float.IsNaN(hang) || gap > hang) hang = gap;
                            break;
                        }
                    }
                    if (float.IsNaN(hang)) note = "no ground within " + d.ground.reach.ToString("F1", Inv) + " m: bottom kept";
                    else if (hang + d.ground.sink > 0) { note = "bottom lowered " + (hang + d.ground.sink).ToString("F2", Inv) + " m to the ground"; lo.y -= hang + d.ground.sink; }
                    made.Add(new Lump { rule = rule.id, centre = rot * ((lo + hi) * .5f), size = hi - lo, rot = rot, layer = first.gameObject.layer, members = cl.Count, note = note,
                        memberKeys = cl.Select(i => BuildingAudit308.KeyOf(hits[i])).ToArray() });
                }
                // names in a stable order (x, then z) so the three scenes agree
                made = made.OrderBy(l => Mathf.Round(l.centre.x * 10)).ThenBy(l => Mathf.Round(l.centre.z * 10)).ToList();
                for (int k = 0; k < made.Count; k++)
                {
                    var l = made[k]; l.name = "Lump_" + rule.id + "_" + k.ToString("D2", Inv);
                    var have = root != null ? root.transform.Find(l.name) : null; var box = have != null ? have.GetComponent<BoxCollider>() : null;
                    // after an apply the members are off, so the measured points still describe the same prop: the lump must simply be there
                    l.state = box != null && Near(have.position, l.centre, d.poseTolerance) && Quaternion.Angle(have.rotation, l.rot) <= .05f && Near(Vector3.Scale(box.size, have.lossyScale), l.size, d.poseTolerance) ? "already" : box != null ? "mismatch" : "apply";
                    if (l.state == "mismatch") plan.blockers.Add("lump " + l.name + " exists with another pose (" + BuildingAudit308.V(have.position) + " size " + BuildingAudit308.V(box.size) + " vs planned " + BuildingAudit308.V(l.centre) + " size " + BuildingAudit308.V(l.size) + "): revert first");
                    plan.lumps.Add(l);
                }
            }
            foreach (var lv in d.leave)
            {
                var hits = Matches(scene, lv.parent, lv.names, false);
                int off = hits.Sum(h => Solid(h).Count(c => !c.enabled));
                plan.notes.Add("left alone: " + hits.Count + " object(s) under " + lv.parent + " {" + string.Join(",", lv.names) + "} - " + lv.why + (off > 0 ? "  [" + off + " collider(s) are OFF]" : ""));
                foreach (var h in hits) foreach (var c in Solid(h)) { string key = BuildingAudit308.KeyOf(c.transform); if (plan.off.Any(o => o.key == key)) plan.blockers.Add("a rule would switch off a left-alone object: " + key); }
            }
            return plan;
        }
        static bool Near(Vector3 a, Vector3 b, float tol) => Mathf.Abs(a.x - b.x) <= tol && Mathf.Abs(a.y - b.y) <= tol && Mathf.Abs(a.z - b.z) <= tol;

        static string PlanText(Data d, Plan p)
        {
            var sb = new StringBuilder();
            foreach (var rule in d.rules)
            {
                var off = p.off.Where(o => o.rule == rule.id).ToList(); var lumps = p.lumps.Where(l => l.rule == rule.id).ToList();
                sb.Append(rule.id).Append(" [").Append(rule.mode).Append("]: colliders off ").Append(off.Count(o => o.state == "apply")).Append(" (already ").Append(off.Count(o => o.state == "already")).Append(")");
                if (rule.mode == "lump") sb.Append(", lumps ").Append(lumps.Count(l => l.state == "apply")).Append(" (already ").Append(lumps.Count(l => l.state == "already")).Append(")");
                sb.Append(" - ").Append(rule.why).Append('\n');
                foreach (var l in lumps) sb.Append("   ").Append(l.name).Append(' ').Append(l.state).Append(": centre ").Append(BuildingAudit308.V(l.centre)).Append(" size ").Append(BuildingAudit308.V(l.size)).Append(" euler ").Append(BuildingAudit308.V(l.rot.eulerAngles)).Append(" members ").Append(l.members).Append(l.note.Length > 0 ? " (" + l.note + ")" : "").Append('\n');
            }
            foreach (var n in p.notes) sb.Append("note: ").Append(n).Append('\n');
            foreach (var b in p.blockers) sb.Append("BLOCKER: ").Append(b).Append('\n');
            sb.Append("total: colliders off ").Append(p.off.Count(o => o.state == "apply")).Append(" (already ").Append(p.off.Count(o => o.state == "already")).Append("), lumps ").Append(p.lumps.Count(l => l.state == "apply")).Append(" (already ").Append(p.lumps.Count(l => l.state == "already")).Append("), blockers ").Append(p.blockers.Count);
            return sb.ToString();
        }
        // what the scene holds once the plan is applied: one line per collider off and per lump (0.01 m)
        static string Digest(Plan p)
        {
            var lines = p.off.Select(o => "off|" + o.rule + "|" + o.key + "|" + o.index + "|" + o.type).Concat(p.lumps.Select(l => "lump|" + l.name + "|" + l.centre.x.ToString("F2", Inv) + "," + l.centre.y.ToString("F2", Inv) + "," + l.centre.z.ToString("F2", Inv) + "|" + l.size.x.ToString("F2", Inv) + "," + l.size.y.ToString("F2", Inv) + "," + l.size.z.ToString("F2", Inv) + "|" + l.rot.eulerAngles.x.ToString("F1", Inv) + "," + l.rot.eulerAngles.y.ToString("F1", Inv) + "," + l.rot.eulerAngles.z.ToString("F1", Inv))).OrderBy(s => s, StringComparer.Ordinal);
            return BuildingAudit308.ShaText(string.Join("\n", lines));
        }

        static string PlanCommand(string alias)
        {
            var d = Load(out string err); if (d == null) return "refused: " + err;
            string refuse = Open(d, alias, out var scene, out string previous, out bool opened); if (refuse != null) return refuse;
            try
            {
                var plan = MakePlan(d, scene, LoadLedger(alias));
                Directory.CreateDirectory(Folder); string text = "plan " + alias + " (" + scene.path + ", data " + DataFile + ")\n" + PlanText(d, plan) + "\ndigest " + Digest(plan);
                File.WriteAllText(Path.Combine(Folder, "plan-" + alias + ".txt"), text, new UTF8Encoding(false));
                return text;
            }
            finally { GoBack(previous, scene, opened, true); }
        }

        // ------------------------------------------------------------------ apply / revert

        static Collider Resolve(Scene scene, Off o)
        {
            var t = BuildingAudit308.Resolve(scene, o.key); if (t == null) return null;
            var all = t.GetComponents<Collider>(); return o.index >= 0 && o.index < all.Length && all[o.index].GetType().Name == o.type ? all[o.index] : null;
        }
        static void SetEnabled(Collider c, bool on)
        {
            var so = new SerializedObject(c); var prop = so.FindProperty("m_Enabled");
            if (prop != null) { prop.boolValue = on; so.ApplyModifiedPropertiesWithoutUndo(); } else c.enabled = on;
            if (PrefabUtility.IsPartOfPrefabInstance(c)) PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            EditorUtility.SetDirty(c);
        }

        static string Apply(string alias, string commandText)
        {
            var d = Load(out string err); if (d == null) return "refused: " + err;
            string refuse = Open(d, alias, out var scene, out string previous, out bool opened); if (refuse != null) return refuse;
            var op = new Op { utc = BuildingAudit308.Utc(), command = commandText, alias = alias, scene = scene.path, dataSha = BuildingAudit308.Sha(DataFile) };
            try
            {
                var ledger = LoadLedger(alias); var plan = MakePlan(d, scene, ledger);
                if (plan.blockers.Count > 0) { GoBack(previous, scene, opened, true); return "refused: nothing changed - " + string.Join(" | ", plan.blockers) + "\n" + PlanText(d, plan); }
                var offs = plan.off.Where(o => o.state == "apply").ToList(); var lumps = plan.lumps.Where(l => l.state == "apply").ToList();
                op.shaBefore = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path)); op.digest = Digest(plan);
                if (offs.Count == 0 && lumps.Count == 0) { GoBack(previous, scene, opened, true); return "already: " + alias + " - no change (scene not saved), digest " + op.digest + "\n" + PlanText(d, plan); }
                var prior = Applied(ledger);
                if (prior != null) { GoBack(previous, scene, opened, true); return "refused: " + alias + " holds an applied op (" + prior.utc + ") and the scene differs from it - revert:" + alias + " first, then apply\n" + PlanText(d, plan); }
                string backupDir = Path.Combine(Folder, "SceneBackup"); Directory.CreateDirectory(backupDir);
                op.backup = Path.Combine(backupDir, alias + "-" + op.utc + ".unity"); File.Copy(BuildingAudit308.Abs(scene.path), op.backup, false);
                // resolve every collider BEFORE the first change (keys use sibling order; nothing here reorders, but resolve once)
                var targets = offs.Select(o => (o, c: Resolve(scene, o))).ToList();
                var lost = targets.Where(x => x.c == null).Select(x => x.o.key).ToList();
                if (lost.Count > 0) { GoBack(previous, scene, opened, true); return "refused: nothing changed - cannot resolve " + lost.Count + " collider key(s): " + string.Join(", ", lost.Take(5)); }
                foreach (var (o, c) in targets) SetEnabled(c, false);
                var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == d.lumpRoot);
                if (root == null && lumps.Count > 0) { root = new GameObject(d.lumpRoot); SceneManager.MoveGameObjectToScene(root, scene); root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); root.isStatic = true; }
                foreach (var l in lumps)
                {
                    var go = new GameObject(l.name) { layer = l.layer, isStatic = true }; go.transform.SetParent(root.transform, false);
                    go.transform.SetPositionAndRotation(l.centre, l.rot); var box = go.AddComponent<BoxCollider>(); box.center = Vector3.zero; box.size = l.size;
                }
                op.off = plan.off; op.lumps = plan.lumps; Physics.SyncTransforms();
                ClearStaleSuffix(scene); EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) { op.status = "FAILED"; op.detail = "SaveScene returned false; the scene stays open and dirty; backup " + op.backup; }
                else { op.shaAfter = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path)); op.status = "applied"; op.detail = offs.Count + " collider(s) off, " + lumps.Count + " lump(s) added"; }
                ledger.alias = alias; ledger.scene = scene.path; ledger.ops.Add(op); SaveLedger(ledger);
                GoBack(previous, scene, opened);
                return op.status + ": " + alias + " " + op.detail + ", sha " + op.shaBefore + " -> " + op.shaAfter + ", backup " + op.backup + ", digest " + op.digest
                    + "\nNavMesh NOT re-baked: the baked NavMesh keeps the old holes until the next bake (the bake reads PhysicsColliders)\n" + PlanText(d, plan);
            }
            catch (Exception e)
            {
                // nothing was saved: drop the half-applied in-memory edits by reloading the file, so the next queue call is not refused
                string discarded = "";
                try
                {
                    if (scene.IsValid() && scene.isDirty && string.IsNullOrEmpty(op.shaAfter))
                    {
                        string path = scene.path; EditorSceneManager.OpenScene(path, OpenSceneMode.Single); discarded = "; unsaved edits discarded (scene reloaded from disk)";
                        if (opened && !string.IsNullOrEmpty(previous) && previous != path && File.Exists(BuildingAudit308.Abs(previous))) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                    }
                }
                catch (Exception e2) { discarded = "; reload failed, the scene is still open and dirty: " + e2.Message; }
                Debug.LogException(e);
                return "FAILED: " + alias + " - " + e.Message + discarded + (string.IsNullOrEmpty(op.backup) ? "" : " (backup " + op.backup + ")");
            }
        }

        static string ApplyAll()
        {
            var d = Load(out string err); if (d == null) return "refused: " + err;
            var sb = new StringBuilder();
            foreach (var s in d.scenes)
            {
                string refuse = Open(d, s.alias, out var scene, out string previous, out bool opened); if (refuse != null) return refuse + " (apply-all: nothing changed)";
                Plan plan; try { plan = MakePlan(d, scene, LoadLedger(s.alias)); } finally { GoBack(previous, scene, opened, true); }
                if (plan.blockers.Count > 0) return "refused: apply-all changed nothing - " + s.alias + ": " + string.Join(" | ", plan.blockers);
                sb.Append("planned ").Append(s.alias).Append(": off ").Append(plan.off.Count(o => o.state == "apply")).Append(", lumps ").Append(plan.lumps.Count(l => l.state == "apply")).Append(", digest ").Append(Digest(plan)).Append('\n');
            }
            foreach (var s in d.scenes)
            {
                string r = Apply(s.alias, "apply-all"); sb.Append(r.Split('\n')[0]).Append('\n');
                if (!r.StartsWith("applied", StringComparison.Ordinal) && !r.StartsWith("already", StringComparison.Ordinal)) { sb.Append("STOPPED at ").Append(s.alias).Append(": earlier scenes stay applied (revert:<alias> undoes each)"); break; }
            }
            return sb.ToString().TrimEnd();
        }

        static string Revert(string alias)
        {
            var d = Load(out string err); if (d == null) return "refused: " + err;
            var ledger = LoadLedger(alias); var op = Applied(ledger);
            if (op == null) return "refused: no applied op to revert for " + alias;
            string refuse = Open(d, alias, out var scene, out string previous, out bool opened); if (refuse != null) return refuse;
            try
            {
                int on = 0, gone = 0, missing = 0; string before = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path));
                foreach (var o in op.off)
                {
                    var c = Resolve(scene, o);
                    if (c == null) { missing++; continue; }
                    if (!c.enabled) { SetEnabled(c, true); on++; }
                }
                var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == d.lumpRoot);
                if (root != null)
                {
                    foreach (var l in op.lumps) { var t = root.transform.Find(l.name); if (t != null) { Object.DestroyImmediate(t.gameObject); gone++; } }
                    if (root.transform.childCount == 0) Object.DestroyImmediate(root);
                }
                ClearStaleSuffix(scene); EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) return "FAILED: SaveScene returned false; the scene stays open and dirty";
                op.reverted = true; op.revertedUtc = BuildingAudit308.Utc(); ledger.verifyDigest = ""; ledger.verifyStatus = "";
                ledger.ops.Add(new Op { utc = op.revertedUtc, command = "revert:" + alias, alias = alias, scene = scene.path, status = "reverted", shaBefore = before, shaAfter = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path)), detail = on + " collider(s) back on, " + gone + " lump(s) deleted, " + missing + " key(s) not found" });
                SaveLedger(ledger); GoBack(previous, scene, opened);
                return "reverted: " + alias + " - " + on + " collider(s) back on, " + gone + " lump(s) deleted" + (missing > 0 ? ", " + missing + " ledger key(s) NOT FOUND (the hierarchy changed since the apply: the scene backup is " + op.backup + ")" : "")
                    + "\nNavMesh NOT re-baked";
            }
            catch (Exception e) { Debug.LogException(e); return "FAILED: revert " + alias + " - " + e.Message + " (scene backup " + op.backup + ")"; }
        }

        // ------------------------------------------------------------------ verify

        static string Verify(string alias)
        {
            var d = Load(out string err); if (d == null) return "refused: " + err;
            string refuse = Open(d, alias, out var scene, out string previous, out bool opened); if (refuse != null) return refuse;
            try
            {
                var ledger = LoadLedger(alias); var op = Applied(ledger); var plan = MakePlan(d, scene, ledger); var lines = new List<string>(); int fail = 0;
                void Check(bool ok, string text) { lines.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) fail++; }
                Check(op != null, "ledger holds an applied op" + (op != null ? " (" + op.utc + ", data sha " + (op.dataSha == BuildingAudit308.Sha(DataFile) ? "same" : "CHANGED since the apply") + ")" : ""));
                Check(plan.blockers.Count == 0, "no blocker" + (plan.blockers.Count > 0 ? ": " + string.Join(" | ", plan.blockers) : ""));
                Check(plan.off.All(o => o.state == "already"), "every listed collider is off (" + plan.off.Count(o => o.state == "already") + " of " + plan.off.Count + ")");
                Check(plan.lumps.All(l => l.state == "already"), "every lump is in place (" + plan.lumps.Count(l => l.state == "already") + " of " + plan.lumps.Count + ")");
                Drawn drawn = null;
                foreach (var rule in d.rules)
                {
                    var hits = Targets(scene, rule, ref drawn, null); int live = hits.Sum(h => Solid(h).Count(c => c.enabled));
                    Check(live == 0, "rule " + rule.id + ": " + hits.Count + " object(s), enabled colliders left " + live);
                }
                var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == d.lumpRoot);
                int boxes = root != null ? root.GetComponentsInChildren<BoxCollider>(true).Length : 0;
                Check(boxes == plan.lumps.Count, "root " + d.lumpRoot + " holds " + boxes + " box(es), planned " + plan.lumps.Count + " (one lump per assembled prop)");
                // no lump hangs: its bottom is at or under the ground found below it
                int hang = 0;
                if (root != null) foreach (var box in root.GetComponentsInChildren<BoxCollider>(true))
                {
                    float bottom = box.bounds.min.y; var from = new Vector3(box.bounds.center.x, bottom + d.ground.probeUp, box.bounds.center.z); float ground = float.NaN;
                    foreach (var h in Physics.RaycastAll(from, Vector3.down, d.ground.probeUp + d.ground.reach, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                    { if (h.collider.transform.IsChildOf(root.transform)) continue; ground = h.point.y; break; }
                    if (!float.IsNaN(ground) && bottom > ground + .05f) { hang++; lines.Add("     " + box.name + " bottom " + bottom.ToString("F2", Inv) + " is " + (bottom - ground).ToString("F2", Inv) + " m above the surface under its centre"); }
                }
                Check(hang == 0, "no lump hangs above the surface under its centre (" + hang + ")");
                foreach (var lv in d.leave)
                {
                    var hits = Matches(scene, lv.parent, lv.names, false); int off = hits.Sum(h => Solid(h).Count(c => !c.enabled)), all = hits.Sum(h => Solid(h).Count());
                    Check(hits.Count > 0 && off == 0, "left alone (" + lv.why + "): " + hits.Count + " object(s), " + all + " collider(s), switched off " + off);
                }
                string digest = Digest(plan); ledger.verifyUtc = BuildingAudit308.Utc(); ledger.verifyDigest = fail == 0 ? digest : ""; ledger.verifyStatus = fail == 0 ? "ok" : "FAILED"; ledger.alias = alias; ledger.scene = scene.path; SaveLedger(ledger);
                string head = (fail == 0 ? "verify ok " : "verify FAILED ") + alias + " " + (lines.Count - fail - lines.Count(l => l.StartsWith("     ", StringComparison.Ordinal))) + "/" + lines.Count(l => !l.StartsWith("     ", StringComparison.Ordinal)) + ", digest " + digest;
                string text = head + "\n" + string.Join("\n", lines) + "\n" + string.Join("\n", plan.notes.Select(n => "note: " + n)) + "\nNavMesh: not re-baked by this tool";
                Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "verify-" + alias + ".txt"), text, new UTF8Encoding(false));
                return text;
            }
            finally { GoBack(previous, scene, opened, true); }
        }

        static string VerifyAll()
        {
            var d = Load(out string err); if (d == null) return "refused: " + err;
            var rows = d.scenes.Select(s => (s.alias, l: LoadLedger(s.alias))).ToList(); var sb = new StringBuilder(); bool ok = true;
            foreach (var (alias, l) in rows)
            {
                bool good = l.verifyStatus == "ok" && !string.IsNullOrEmpty(l.verifyDigest) && Applied(l) != null; ok &= good;
                sb.Append(alias).Append(": ").Append(good ? "verify ok " + l.verifyUtc + " digest " + l.verifyDigest : "NOT verified (run verify:" + alias + " after its apply)").Append('\n');
            }
            bool same = ok && rows.Select(r => r.l.verifyDigest).Distinct().Count() == 1;
            return (same ? "verify-all ok: the three scenes hold the same result" : ok ? "verify-all FAILED: the digests differ (a scene's hierarchy or ground differs)" : "verify-all FAILED: a scene is not verified") + "\n" + sb.ToString().TrimEnd();
        }

        // ------------------------------------------------------------------ fits (read only)

        static string Fits()
        {
            var profile = AssetDatabase.LoadAssetAtPath<NaturalSolidProfileSO>(ProfilePath);
            if (profile == null) return "refused: profile missing " + ProfilePath;
            var scene = SceneManager.GetActiveScene(); var sheets = new List<WorldMacroDressingSheetSO>();
            foreach (var g in scene.GetRootGameObjects()) foreach (var r in g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)) if (r.Sheet != null && !sheets.Contains(r.Sheet)) sheets.Add(r.Sheet);
            var fits = profile.Fits308 ?? Array.Empty<NaturalSolidProfileSO.Fit308>(); var listed = new HashSet<string>(fits.Where(f => f?.PrototypeIds != null).SelectMany(f => f.PrototypeIds));
            var seen = new HashSet<string>(); var lines = new List<string>(); int fit = 0, code = 0, stale = 0, none = 0, parts = 0;
            foreach (var sheet in sheets)
            {
                var used = new Dictionary<string, int>();
                foreach (var fp in sheet.FixedPlacements ?? Array.Empty<WorldMacroDressingSheetSO.FixedPlacement>()) if (fp?.PrototypeId != null) { used.TryGetValue(fp.PrototypeId, out int n); used[fp.PrototypeId] = n + 1; }
                foreach (var proto in sheet.Prototypes ?? Array.Empty<WorldMacroDressingSheetSO.Prototype>())
                {
                    if (proto == null || string.IsNullOrEmpty(proto.Id) || !used.TryGetValue(proto.Id, out int n)) continue;
                    if (proto.Category != WorldMacroDressingSheetSO.Kind.Tree && proto.Category != WorldMacroDressingSheetSO.Kind.Rock) continue;
                    seen.Add(proto.Id); var f = profile.FitFor308(proto.Id, proto.Size); string state;
                    if (f != null) { int k = proto.Category == WorldMacroDressingSheetSO.Kind.Tree ? f.Capsules?.Length ?? 0 : f.Boxes?.Length ?? 0; parts += k; if (k > 0) { fit++; state = "fit " + k + " part(s)"; } else { none++; state = "fit: NO stand-in (nothing inside is big enough)"; } }
                    else if (listed.Contains(proto.Id) && profile.UseFits308) { stale++; state = "STALE fit (prototype Size " + BuildingAudit308.V(proto.Size) + " differs): code rule"; }
                    else { code++; state = "code rule"; }
                    lines.Add("  " + sheet.name + " | " + proto.Category + " " + proto.Id + " x" + n + " -> " + state);
                }
            }
            int orphan = listed.Count(id => !seen.Contains(id));
            string head = "fits " + (stale == 0 ? "ok" : "FAILED") + ": profile UseFits308 " + profile.UseFits308 + ", " + fits.Length + " fit(s) naming " + listed.Count + " prototype(s); scene " + scene.name + " sheets " + sheets.Count
                + " -> fitted " + fit + " (parts " + parts + "), no stand-in " + none + ", code rule " + code + ", stale " + stale + ", listed but not in these sheets " + orphan;
            return head + "\n" + string.Join("\n", lines);
        }
    }
}
