using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.App.World.Dressing;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 F1–F5 planners (read only: they compute before/after values), the F2 sheet step, the F5 cap mesh and verify.
    public static partial class BuildingFix308
    {
        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        static float Median(List<float> v) { if (v.Count == 0) return 0; var s = v.OrderBy(x => x).ToList(); return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) * .5f; }

        static bool RendererBounds(Transform t, out Bounds b)
        {
            b = default; bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        // replay of this scene's ledger for one step: after = already, before = apply again, anything else = mismatch
        static List<Change308> Replay(Scene scene, Ledger308 ledger, string step, Func<Change308, bool> filter = null)
        {
            var list = new List<Change308>(); var seen = new HashSet<string>();
            for (int i = ledger.ops.Count - 1; i >= 0; i--)
            {
                var op = ledger.ops[i]; if (op.reverted) continue;
                foreach (var c in op.changes.Where(c => c.step == step && (c.state == "apply" || c.state == "already") && (filter == null || filter(c))))
                {
                    if (!seen.Add(c.kind + "|" + c.key)) continue;
                    var t = BuildingAudit308.Resolve(scene, c.key);
                    var r = new Change308 { step = c.step, key = c.key, kind = c.kind, before = c.before, after = c.after, note = c.note, at = c.at };
                    if (t == null) { r.state = "mismatch"; r.note += " (missing)"; }
                    else if (c.kind == "pose") r.state = SamePose(t, c.after) ? "already" : SamePose(t, c.before) ? "apply" : "mismatch";
                    else if (c.kind == "active") r.state = (t.gameObject.activeSelf ? "1" : "0") == c.after ? "already" : "apply";
                    else if (c.kind == "collider") { var f = t.GetComponent<MeshFilter>(); string now = ColliderState(t, f != null ? f.sharedMesh : null); r.state = now == c.after ? "already" : now == c.before ? "apply" : "mismatch"; }
                    else if (c.kind == "mesh") { string now = MeshState(t.GetComponent<MeshFilter>()); r.state = now == c.after ? "already" : now == c.before ? "apply" : "mismatch"; }
                    // a missing skirt is not re-applied from the ledger: SkirtAfterMove re-evaluates it on the moved plate
                    else if (c.kind == "skirt") r.state = t.Find("StoneSkirt308") != null ? "already" : "redo";
                    else r.state = "already";
                    list.Add(r);
                }
            }
            list.Reverse();
            return list;
        }

        static void Settle(StepPlan308 sp)
        {
            if (sp.status == "blocked" || sp.status == "absent") return;
            if (sp.changes.Any(c => c.state == "mismatch")) { sp.status = "mismatch"; sp.detail = "current values match neither before nor after: " + string.Join(", ", sp.changes.Where(c => c.state == "mismatch").Select(c => c.key).Take(6)); return; }
            if (sp.changes.Any(c => c.state == "apply")) { sp.status = "ready"; if (string.IsNullOrEmpty(sp.detail)) sp.detail = sp.changes.Count(c => c.state == "apply") + " change(s)"; return; }
            if (sp.changes.Any(c => c.state == "already")) { sp.status = "already"; if (string.IsNullOrEmpty(sp.detail)) sp.detail = "already applied"; return; }
            sp.status = "clean"; if (string.IsNullOrEmpty(sp.detail)) sp.detail = "nothing to do";
        }

        // ------------------------------------------------------------------ F1 buried village houses

        [Serializable] internal sealed class Decision308 { public string path = "", chosenBy = "", scene = "", utc = ""; public float[] origin = Array.Empty<float>(), target = Array.Empty<float>(); public float yaw; public List<Candidate308> candidates = new List<Candidate308>(); }
        [Serializable] internal sealed class Decisions308 { public List<Decision308> houses = new List<Decision308>(); }
        static string DecisionsFile => Path.Combine(FixDir, "decisions-F1.json");
        static Decisions308 LoadDecisions() => File.Exists(DecisionsFile) ? JsonUtility.FromJson<Decisions308>(File.ReadAllText(DecisionsFile)) ?? new Decisions308() : new Decisions308();
        static void SaveDecisions(Decisions308 d) { Directory.CreateDirectory(FixDir); File.WriteAllText(DecisionsFile, JsonUtility.ToJson(d, true), new UTF8Encoding(false)); }

        static StepPlan308 PlanF1(BuildingAudit308.Config308 cfg, Scene scene, Ledger308 ledger)
        {
            var f1 = cfg.fix.f1;
            var sp = new StepPlan308 { step = "F1", target = string.Join(", ", f1.houses.Select(h => h.path)) };
            if (f1.houses.Length == 0) { sp.status = "clean"; sp.detail = "no houses configured"; return sp; }
            var decisions = LoadDecisions();
            var corridors = BuildingAudit308.Corridors(scene, cfg);
            var blockers = new List<string>();
            foreach (var house in f1.houses)
            {
                var t = BuildingAudit308.Resolve(scene, house.path);
                if (t == null) { sp.notes.Add("absent " + house.path); continue; }
                string key = BuildingAudit308.KeyOf(t);
                // 1. this scene's ledger decides (idempotent replay)
                var replay = Replay(scene, ledger, "F1", c => c.note.EndsWith(":" + key, StringComparison.Ordinal));
                if (replay.Count > 0) { sp.changes.AddRange(replay); sp.notes.Add(house.path + ": ledger replay " + replay.Count(c => c.state == "already") + " already, " + replay.Count(c => c.state == "apply") + " to re-apply"); continue; }
                var d = decisions.houses.FirstOrDefault(x => x.path == house.path);
                var pos = t.position;
                if (d != null && d.target.Length >= 3)
                {
                    var tgt = BuildingAudit308.Vec(d.target); var org = BuildingAudit308.Vec(d.origin);
                    if (Flat(pos, tgt) <= .05f) { sp.changes.Add(new Change308 { step = "F1", key = key, kind = "pose", before = "", after = Pose(t), state = "already", at = pos, note = "house:" + key }); sp.notes.Add(house.path + " already at the decided site (no ledger here)"); continue; }
                    if (Flat(pos, org) > .05f) { blockers.Add(house.path + " is neither at its origin " + BuildingAudit308.V(org) + " nor at the decided site " + BuildingAudit308.V(tgt) + " (now " + BuildingAudit308.V(pos) + ")"); continue; }
                }
                // 2. content within the footprint + contentRadius stops the move (ask first)
                var near = ContentNear(scene, t, f1.contentRadius);
                if (near.Count > 0 && !f1.allowContentNear.Contains(house.path)) { blockers.Add(house.path + ": content within " + f1.contentRadius + " m — " + string.Join("; ", near.Take(6)) + " (ask; then list the path in fix.f1.allowContentNear)"); continue; }
                if (near.Count > 0) sp.notes.Add(house.path + " content near (allowed by config): " + string.Join("; ", near.Take(6)));
                // 3. a before still of this house must exist (Spec F1 order 1)
                if (f1.requireBeforeStill && !BeforeStill(t.name)) { blockers.Add(house.path + ": no capture of the current state yet (BuildingAudit308/Shots/**/*" + t.name + "*.png) — run the sweep first"); continue; }
                sp.notes.AddRange(GroundFit299.Children308(t).Take(20).Select(x => "child " + x));
                // 4. site: shared decision (same target in all three scenes) or a new flat search
                if (d == null)
                {
                    d = Decide(cfg, scene, t, house, corridors, out string why);
                    if (d == null) { blockers.Add(house.path + ": " + why); continue; }
                    decisions.houses.Add(d); SaveDecisions(decisions);
                    sp.notes.Add(house.path + " decided " + d.chosenBy + " -> (" + d.target[0].ToString("F1", Inv) + ", " + d.target[2].ToString("F1", Inv) + ") recorded in " + DecisionsFile);
                }
                sp.candidates.AddRange(d.candidates);
                // the decision may come from another scene (the first plan writes it): re-check the site in THIS scene — colliders,
                // sheet trunks, terrain range and content points (main carries #306/#307 placements the candidates lack)
                {
                    var here = new Vector3(d.target[0], d.target[1], d.target[2]);
                    var spot = GroundFit299.FlatSearch308(t, 0f, Mathf.Max(.5f, f1.step), here).FirstOrDefault();
                    string bad = spot == null ? "terrain range over .8 m" : !string.IsNullOrEmpty(spot.blocker) ? spot.blocker : spot.range > f1.rangeMax ? "range " + spot.range.ToString("F2", Inv) + " m" : null;
                    var atSite = ContentNear(scene, t, f1.contentRadius, new Vector3(here.x - pos.x, 0, here.z - pos.z));
                    if (bad == null && atSite.Count > 0 && !f1.allowContentNear.Contains(house.path)) bad = "content " + string.Join("; ", atSite.Take(4));
                    if (bad != null) { blockers.Add(house.path + ": the decided site (" + here.x.ToString("F1", Inv) + ", " + here.z.ToString("F1", Inv) + ", " + d.chosenBy + " in " + d.scene + ") is not clear in " + scene.path + " — " + bad); continue; }
                }
                var target = GroundFit299.RelocateTarget308(t, d.target[0], d.target[2], out float range, out float lift);
                if (float.IsNaN(target.x)) { blockers.Add(house.path + ": no terrain at the decided site"); continue; }
                var newLocal = t.parent != null ? t.parent.InverseTransformPoint(target) : target;
                sp.changes.Add(new Change308 { step = "F1", key = key, kind = "pose", before = Pose(t), after = Pose(newLocal, t.localRotation), state = "apply", at = target, note = "house:" + key });
                sp.notes.Add(house.path + ": " + BuildingAudit308.V(pos) + " -> " + BuildingAudit308.V(target) + " (ground range " + range.ToString("F2", Inv) + " m, old root-ground " + lift.ToString("F2", Inv) + " m)");
                foreach (var skirt in t.GetComponentsInChildren<Transform>(true).Where(x => x.name == "StoneSkirt299" && x.gameObject.activeSelf))
                    sp.changes.Add(new Change308 { step = "F1", key = BuildingAudit308.KeyOf(skirt), kind = "active", before = "1", after = "0", state = "apply", note = "oldskirt:" + key });
                // 5. its fence / timber props move by the same offset and sit on the new ground
                var delta = target - pos; delta.y = 0;
                var props = PropMoves(cfg, scene, t, delta, out var propNotes);
                foreach (var c in props) { c.note = "prop:" + key; sp.changes.Add(c); }
                sp.notes.AddRange(propNotes.Select(n => house.path + " " + n));
            }
            if (blockers.Count > 0) { sp.status = "blocked"; sp.detail = string.Join(" | ", blockers); return sp; }
            Settle(sp);
            return sp;
        }

        static bool BeforeStill(string name)
        {
            string dir = Path.Combine(BuildingAudit308.Folder, "Shots");
            return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*" + name + "*.png", SearchOption.AllDirectories).Any();
        }

        // content within the footprint + radius; `shift` evaluates the same footprint moved by that XZ offset (a candidate site)
        static List<string> ContentNear(Scene scene, Transform t, float radius, Vector3 shift = default)
        {
            var list = new List<string>();
            GroundFit299.Footprint308(t, out var half, out var off, out float yaw);
            var rot = Quaternion.Euler(0, yaw, 0); var inv = Quaternion.Inverse(rot);
            var centre = t.position + shift + rot * new Vector3(off.x, 0, off.y);
            bool Inside(Vector3 p) { var l = inv * new Vector3(p.x - centre.x, 0, p.z - centre.z); return Mathf.Abs(l.x) <= half.x + radius && Mathf.Abs(l.z) <= half.y + radius; }
            var session = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).FirstOrDefault();
            if (session != null && session.Content != null)
            {
                foreach (var p in session.Content.Points) if (p != null && Inside(p.Position)) list.Add("point " + p.Id + " @" + BuildingAudit308.V(p.Position));
                foreach (var c in session.Content.Checkpoints) if (c != null && Inside(c.Feet)) list.Add("checkpoint " + c.Id + " @" + BuildingAudit308.V(c.Feet));
            }
            foreach (var g in scene.GetRootGameObjects())
            {
                foreach (var a in g.GetComponentsInChildren<NpcJobActor>(true)) if (Inside(a.transform.position)) list.Add("npc job " + BuildingAudit308.PathOf(a.transform));
                foreach (var a in g.GetComponentsInChildren<PrologueInteraction>(true)) if (!a.transform.IsChildOf(t) && Inside(a.transform.position)) list.Add("interaction " + BuildingAudit308.PathOf(a.transform));
            }
            foreach (var a in t.GetComponentsInChildren<PrologueInteraction>(true)) list.Add("interaction on the house " + BuildingAudit308.PathOf(a.transform));
            return list;
        }

        static Decision308 Decide(BuildingAudit308.Config308 cfg, Scene scene, Transform t, House308 house, List<BuildingAudit308.Corridor308> corridors, out string why)
        {
            var f1 = cfg.fix.f1; why = "";
            Vector3? centre = house.searchCentre.Length >= 2 ? new Vector3(house.searchCentre[0], t.position.y, house.searchCentre[house.searchCentre.Length >= 3 ? 2 : 1]) : (Vector3?)null;
            var spots = GroundFit299.FlatSearch308(t, f1.radius, f1.step, centre);
            GroundFit299.Footprint308(t, out var half, out var off, out float yaw);
            var rot = Quaternion.Euler(0, yaw, 0);
            var others = BuildingAudit308.UnitsOf(scene, cfg, f1.buildingRoots).Where(u => u.building && u.t != t && !u.t.IsChildOf(t) && !t.IsChildOf(u.t)).ToList();
            var apron = half + new Vector2(f1.apron, f1.apron);
            var cands = new List<Candidate308>();
            foreach (var s in spots)
            {
                var c = new Candidate308 { x = s.at.x, z = s.at.z, ground = s.at.y, range = s.range, dist = s.dist, blocker = s.blocker ?? "" };
                var fc = s.at + rot * new Vector3(off.x, 0, off.y);
                c.corridorClearance = BuildingAudit308.CorridorClearance(new Vector2(fc.x, fc.z), apron, yaw, corridors, out string cid);
                // nearest other building (footprint AABB to bounds XZ)
                var e =new Vector2(Mathf.Abs(Mathf.Cos(yaw * Mathf.Deg2Rad)) * half.x + Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) * half.y, Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) * half.x + Mathf.Abs(Mathf.Cos(yaw * Mathf.Deg2Rad)) * half.y);
                float gap = float.PositiveInfinity;
                foreach (var u in others)
                {
                    float dx = Mathf.Max(0, Mathf.Abs(fc.x - u.bounds.center.x) - e.x - u.bounds.extents.x), dz = Mathf.Max(0, Mathf.Abs(fc.z - u.bounds.center.z) - e.y - u.bounds.extents.z);
                    gap = Mathf.Min(gap, Mathf.Sqrt(dx * dx + dz * dz));
                }
                c.buildingGap = gap;
                if (c.blocker.Length == 0 && c.range > f1.rangeMax) c.blocker = "range " + c.range.ToString("F2", Inv) + " > " + f1.rangeMax.ToString("F2", Inv);
                if (c.blocker.Length == 0 && c.corridorClearance < 0) c.blocker = "route corridor " + cid;
                if (c.blocker.Length == 0 && c.buildingGap < f1.buildingGap) c.blocker = "building gap " + c.buildingGap.ToString("F1", Inv) + " m";
                c.ok = c.blocker.Length == 0;
                cands.Add(c);
            }
            var ok = cands.Where(c => c.ok).OrderBy(c => c.dist).ToList();
            var choice = f1.choices.FirstOrDefault(c => c.path == house.path && c.xz.Length >= 2);
            Candidate308 pick;
            string by;
            if (choice != null)
            {
                pick = ok.OrderBy(c => Vector2.Distance(new Vector2(c.x, c.z), new Vector2(choice.xz[0], choice.xz[1]))).FirstOrDefault();
                if (pick == null || Vector2.Distance(new Vector2(pick.x, pick.z), new Vector2(choice.xz[0], choice.xz[1])) > f1.step) { why = "the configured choice (" + choice.xz[0] + ", " + choice.xz[1] + ") is not a clear candidate within " + f1.step + " m"; return null; }
                by = "config choice";
            }
            else { pick = ok.FirstOrDefault(); by = "nearest clear candidate"; }
            if (pick == null) { why = "no flat candidate within " + f1.radius + " m (range ≤ " + f1.rangeMax + " m, outside corridors, ≥ " + f1.buildingGap + " m from buildings) — move the search centre (fix.f1.houses[].searchCentre) or ask; blocked best: " + string.Join("; ", cands.Where(c => !c.ok).OrderBy(c => c.range).Take(3).Select(c => "(" + c.x.ToString("F0", Inv) + "," + c.z.ToString("F0", Inv) + ") " + c.blocker)); return null; }
            return new Decision308
            {
                path = house.path, chosenBy = by, scene = scene.path, utc = BuildingAudit308.Utc(), yaw = yaw,
                origin = BuildingAudit308.Arr(t.position), target = new[] { pick.x, pick.ground, pick.z },
                candidates = ok.Take(Mathf.Max(1, f1.maxCandidates)).Concat(cands.Where(c => !c.ok).OrderBy(c => c.dist).Take(3)).ToList()
            };
        }

        // Village245 children near the house (fence / timber tokens): moved by the house offset; heights per config seat mode
        //   group     — rows (single link propGroupLink) keep their shape; the median lowest member sits at ground - propSeatSink
        //   difference — GroundFit299 relocate-near rule: + (terrain at the new XZ - terrain at the old XZ) per child
        static List<Change308> PropMoves(BuildingAudit308.Config308 cfg, Scene scene, Transform house, Vector3 delta, out List<string> notes)
        {
            var f1 = cfg.fix.f1; notes = new List<string>(); var list = new List<Change308>();
            var parent = BuildingAudit308.Resolve(scene, f1.parent);
            if (parent == null) { notes.Add("parent " + f1.parent + " absent"); return list; }
            var buildings = new HashSet<Transform>(BuildingAudit308.UnitsOf(scene, cfg, new[] { parent.name }).Where(u => u.building).Select(u => u.t));
            var items = new List<(Transform t, Bounds b)>();
            foreach (Transform c in parent)
            {
                if (c == house || buildings.Contains(c) || !c.gameObject.activeInHierarchy) continue;
                if (!BuildingAudit308.HasAny(c.name, f1.propTokens)) continue;
                if (Flat(c.position, house.position) > f1.propRadius) continue;
                if (!RendererBounds(c, out var b)) continue;
                items.Add((c, b));
            }
            if (items.Count == 0) { notes.Add("no fence/timber props within " + f1.propRadius + " m"); return list; }
            // cross-check with the Floating307 'no ground' list (baseline)
            string fl = string.IsNullOrEmpty(f1.floatingList) ? null : Path.Combine(BuildingAudit308.RepoRoot, f1.floatingList);
            if (fl != null && File.Exists(fl))
            {
                var lines = File.ReadAllLines(fl).Where(l => l.Contains("no ground")).ToList();
                int listed = items.Count(i => lines.Any(l => l.TrimStart().StartsWith(i.t.name + " [", StringComparison.Ordinal) && l.Contains(Mathf.RoundToInt(i.b.center.x).ToString(Inv))));
                notes.Add("props selected " + items.Count + ", of them in the Floating307 'no ground' list " + listed);
            }
            // groups by single link on bounds centres
            var group = new int[items.Count]; for (int i = 0; i < group.Length; i++) group[i] = i;
            int Find(int i) { while (group[i] != i) i = group[i] = group[group[i]]; return i; }
            for (int i = 0; i < items.Count; i++) for (int j = i + 1; j < items.Count; j++)
                if (Flat(items[i].b.center, items[j].b.center) <= f1.propGroupLink + Mathf.Max(items[i].b.extents.x, items[i].b.extents.z) + Mathf.Max(items[j].b.extents.x, items[j].b.extents.z)) group[Find(i)] = Find(j);
            foreach (var g in Enumerable.Range(0, items.Count).GroupBy(Find))
            {
                var members = g.Select(i => items[i]).ToList();
                float lowest = members.Min(m => m.b.min.y);
                var offsets = new List<float>();
                foreach (var m in members.Where(m => m.b.min.y <= lowest + .3f))
                {
                    float gNew = BuildingAudit308.TerrainTop(m.b.center + delta);
                    if (!float.IsNaN(gNew)) offsets.Add(gNew - f1.propSeatSink - m.b.min.y);
                }
                float groupOff = Median(offsets);
                foreach (var m in members)
                {
                    float dy = groupOff;
                    if (f1.propSeat == "difference") { float g0 = BuildingAudit308.TerrainTop(m.b.center), g1 = BuildingAudit308.TerrainTop(m.b.center + delta); dy = float.IsNaN(g0) || float.IsNaN(g1) ? 0 : g1 - g0; }
                    var world = m.t.position + delta + Vector3.up * dy;
                    var local = parent.InverseTransformPoint(world);
                    list.Add(new Change308 { step = "F1", key = BuildingAudit308.KeyOf(m.t), kind = "pose", before = Pose(m.t), after = Pose(local, m.t.localRotation), state = "apply", at = world });
                }
                notes.Add("prop group of " + members.Count + " (" + string.Join(",", members.Select(m => m.t.name).Distinct().Take(4)) + ") vertical " + groupOff.ToString("F2", Inv) + " m (" + f1.propSeat + ")");
            }
            return list;
        }

        // after the move: a plate that still floats more than skirtHover gets the #299 dry stone skirt (StoneSkirt308)
        static List<Change308> SkirtAfterMove(BuildingAudit308.Config308 cfg, Scene scene, List<Change308> houses, List<Object> created)
        {
            var list = new List<Change308>();
            Physics.SyncTransforms();
            foreach (var hc in houses)
            {
                var t = BuildingAudit308.Resolve(scene, hc.key); if (t == null) continue;
                var rc = cfg.roots.FirstOrDefault(r => r.name == t.root.name) ?? new BuildingAudit308.Root308 { name = t.root.name, depth = 1 };
                var u = BuildingAudit308.MakeUnit(t, rc, false); u.building = true;
                var plates = BuildingAudit308.Plates(cfg.thresholds, cfg.tokens, u, out _);
                if (plates.Length == 0) continue;
                var rows = new List<(string level, Vector3 at, float value, string detail)>();
                var m = BuildingAudit308.Landing(u, plates, cfg.thresholds, cfg.tokens, p => false, rows);
                if (m.maxHover <= cfg.fix.f1.skirtHover) continue;
                var plate = plates.OrderByDescending(c => c.bounds.size.x * c.bounds.size.z).First().transform;
                string holderKey = BuildingAudit308.KeyOf(plate.parent);
                if (plate.parent.Find("StoneSkirt308") != null) { list.Add(new Change308 { step = "F1", key = holderKey, kind = "skirt", before = "none", after = "present", state = "already", note = "skirt:" + hc.key, at = plate.position }); continue; }
                string mesh = GroundFit299.StoneSkirt308(plate, cfg.fix.f1.skirtMaterial, out string note);
                if (mesh == null) continue;
                var asset = AssetDatabase.LoadAssetAtPath<Mesh>(mesh); if (asset != null) created.Add(asset);
                list.Add(new Change308 { step = "F1", key = holderKey, kind = "skirt", before = "none", after = mesh, state = "apply", note = "skirt:" + hc.key + " " + note, at = plate.position });
            }
            return list;
        }

        // ------------------------------------------------------------------ F3 overlapping same-name siblings

        [Serializable] internal sealed class OffsetPair308 { public string parent = "", name = "", status = "", note = ""; public float[] current = Array.Empty<float>(), a = Array.Empty<float>(), b = Array.Empty<float>(), delta = Array.Empty<float>(); public float currentYaw, aYaw, bYaw, dyaw; public int candidates;
            // 2026-10-04: the dragged member ("Name#k") goes back to its own baseline place (home, homeRot = local pose in the baseline)
            public string mover = "", stayer = ""; public float[] home = Array.Empty<float>(), homeRot = Array.Empty<float>();
            // home height from the neighbours that did not move (0 neighbours = seat on the terrain)
            public float homeY; public int homeYNeighbours; }
        [Serializable] internal sealed class OffsetFile308 { public string utc = "", baseline = "", scene = "", parent = ""; public OffsetPair308[] pairs = Array.Empty<OffsetPair308>(); }

        static StepPlan308 PlanF3(BuildingAudit308.Config308 cfg, Scene scene, Ledger308 ledger)
        {
            var f3 = cfg.fix.f3; var th = cfg.thresholds;
            var sp = new StepPlan308 { step = "F3", target = f3.parent };
            var parent = BuildingAudit308.Resolve(scene, f3.parent);
            if (parent == null) { sp.status = "absent"; sp.detail = f3.parent + " absent"; return sp; }
            var replay = Replay(scene, ledger, "F3");
            sp.changes.AddRange(replay);
            var handled = new HashSet<string>(replay.Select(c => c.key));
            var pairs = BuildingAudit308.SiblingDuplicates(parent, th.c4PosTol, th.c4RotTol).Where(p => p.a.parent == parent).ToList();
            if (pairs.Count == 0) { Settle(sp); if (sp.status == "clean") sp.detail = "no duplicate siblings under " + f3.parent; return sp; }
            string file = Path.Combine(BuildingAudit308.Folder, f3.offsetsFile);
            if (!File.Exists(file)) { sp.status = "blocked"; sp.detail = pairs.Count + " duplicate pair(s) but no " + file + " (python Tools/Art/buildingaudit308_offline.py offsets)"; return sp; }
            var offsets = JsonUtility.FromJson<OffsetFile308>(File.ReadAllText(file).TrimStart('﻿'));
            var corridors = BuildingAudit308.Corridors(scene, cfg);
            var count = new Dictionary<Transform, int>();
            foreach (var (a, b) in pairs) { count[a] = count.TryGetValue(a, out int x) ? x + 1 : 1; count[b] = count.TryGetValue(b, out int y) ? y + 1 : 1; }
            foreach (var (pa, pb) in pairs)
            {
                // which member moves: the offsets file names the dragged one (its own baseline place is its home); without a name the
                // older rule stands (the second member goes to the next baseline place, within maxShift)
                var e = offsets.pairs.FirstOrDefault(o => o.parent == f3.parent && o.name == pa.name && o.current.Length >= 3 && Vector2.Distance(new Vector2(o.current[0], o.current[2]), new Vector2(pa.localPosition.x, pa.localPosition.z)) <= f3.matchTolerance);
                bool named = e != null && !string.IsNullOrEmpty(e.mover) && e.home.Length >= 3;
                string ka = BuildingAudit308.KeyOf(pa), kb = BuildingAudit308.KeyOf(pb);
                bool moveA = named && ka.EndsWith("/" + e.mover, StringComparison.Ordinal);
                if (named && !moveA && !kb.EndsWith("/" + e.mover, StringComparison.Ordinal))
                {
                    if (!handled.Contains(kb)) sp.changes.Add(new Change308 { step = "F3", key = kb, kind = "pose", before = Pose(pb), note = "pair:" + ka, at = pb.position, state = "blocked", after = "the offsets file names " + e.mover + ", which is neither member of this pair — run the offline offsets again" });
                    continue;
                }
                var a = moveA ? pb : pa; var b = moveA ? pa : pb;   // a stays, b moves
                string bk = BuildingAudit308.KeyOf(b);
                if (handled.Contains(bk)) continue;
                var ch = new Change308 { step = "F3", key = bk, kind = "pose", before = Pose(b), note = "pair:" + BuildingAudit308.KeyOf(a), at = b.position };
                if (count[a] > 1 || count[b] > 1) { ch.state = "blocked"; ch.after = "three or more identical siblings — capture review"; sp.changes.Add(ch); continue; }
                if (e == null) { ch.state = "blocked"; ch.after = "no baseline offset for this pair in " + Path.GetFileName(file); sp.changes.Add(ch); continue; }
                if (e.status != "ok" || e.delta.Length < 2) { ch.state = "blocked"; ch.after = "baseline " + e.status + " (" + e.candidates + " same-name siblings within the radius) " + e.note; sp.changes.Add(ch); continue; }
                float shift = new Vector2(e.delta[0], e.delta[1]).magnitude;
                if (!named && f3.maxShift > 0 && shift > f3.maxShift) { ch.state = "blocked"; ch.after = "baseline partner " + shift.ToString("F1", Inv) + " m away (> fix.f3.maxShift " + f3.maxShift.ToString("F0", Inv) + " m) — capture review; raise maxShift to move it"; sp.changes.Add(ch); continue; }
                bool hung = named && e.homeYNeighbours > 0;   // an assembly part: its height follows the neighbours, not the ground
                if (hung) ch.note += ";height:neighbours";
                var local = named ? new Vector3(e.home[0], hung ? e.homeY : b.localPosition.y, e.home[2]) : a.localPosition + new Vector3(e.delta[0], 0, e.delta[1]);
                var rot = named ? (e.homeRot.Length >= 4 ? new Quaternion(e.homeRot[0], e.homeRot[1], e.homeRot[2], e.homeRot[3]) : b.localRotation) : Quaternion.Euler(0, e.dyaw, 0) * a.localRotation;
                if (named)
                {
                    // the home place must be free of the same name (a second pile is not a repair)
                    Transform holder = null;
                    foreach (Transform c in parent)
                        if (c != b && c.name == b.name && Vector2.Distance(new Vector2(c.localPosition.x, c.localPosition.z), new Vector2(local.x, local.z)) <= .25f) { holder = c; break; }
                    if (holder != null) { ch.state = "blocked"; ch.after = "the home place already holds " + BuildingAudit308.KeyOf(holder) + " — capture review"; sp.changes.Add(ch); continue; }
                }
                // seat: lowest renderer bottom = terrain - seatSink (hypothetical pose, nothing moves here)
                var mNew = (parent.localToWorldMatrix * Matrix4x4.TRS(local, rot, b.localScale)) * b.worldToLocalMatrix;
                float bottom = float.PositiveInfinity; var centre = Vector3.zero; int nc = 0;
                foreach (var r in b.GetComponentsInChildren<Renderer>(false))
                {
                    var mesh = BuildingAudit308.MeshOf(r); if (mesh == null || !r.enabled) continue;
                    var mb = mesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var w = mNew.MultiplyPoint3x4(r.localToWorldMatrix.MultiplyPoint3x4(c));
                        bottom = Mathf.Min(bottom, w.y); centre += w; nc++;
                    }
                }
                if (nc == 0) { ch.state = "blocked"; ch.after = "no renderer to seat"; sp.changes.Add(ch); continue; }
                centre /= nc;
                // a home place can lie far from where the object was dragged to, at another height: read the terrain top there
                float ground = named ? BuildingAudit308.TerrainTop(new Vector3(centre.x, 0f, centre.z)) : BuildingAudit308.TerrainNear(new Vector3(centre.x, bottom, centre.z), th.c2TerrainWindow);
                if (float.IsNaN(ground)) { ch.state = "blocked"; ch.after = "no terrain under the new place"; sp.changes.Add(ch); continue; }
                float dy = hung ? 0f : f3.seat == "mirror-a" && RendererBounds(a, out var ab) ? (ab.min.y - BuildingAudit308.TerrainNear(ab.center, th.c2TerrainWindow)) + ground - bottom : ground - f3.seatSink - bottom;
                var world = parent.TransformPoint(local) + Vector3.up * dy;
                local = parent.InverseTransformPoint(world);
                float clear = BuildingAudit308.CorridorClearance(new Vector2(centre.x, centre.z), corridors, out string cid);
                if (clear < (named ? -f3.homeCorridorSlack : 0f)) { ch.state = "blocked"; ch.after = "the baseline place lies in route corridor " + cid + " (" + (-clear).ToString("F2", Inv) + " m inside)"; sp.changes.Add(ch); continue; }
                ch.after = Pose(local, rot); ch.state = "apply"; ch.at = world;
                sp.changes.Add(ch);
                sp.notes.Add(bk + " -> local " + BuildingAudit308.V(local) + " (Δxz " + e.delta[0].ToString("F2", Inv) + "," + e.delta[1].ToString("F2", Inv) + " Δyaw " + e.dyaw.ToString("F1", Inv) + ", seat " + dy.ToString("F2", Inv) + " m" + (named ? ", own baseline place" : "") + (hung ? ", height from " + e.homeYNeighbours + " neighbour(s), bottom " + (bottom - ground).ToString("F2", Inv) + " m above the ground" : "") + (clear < 0 ? ", " + (-clear).ToString("F2", Inv) + " m inside corridor " + cid + " (within homeCorridorSlack)" : "") + ")");
            }
            Settle(sp);
            int held = sp.changes.Count(c => c.state == "blocked");
            if (held > 0) sp.detail += "; " + held + " pair(s) held for capture review (not moved)";
            return sp;
        }

        // ------------------------------------------------------------------ F4 Roadside303 thatched walls / door colliders

        static StepPlan308 PlanF4(BuildingAudit308.Config308 cfg, Scene scene, Ledger308 ledger)
        {
            var f4 = cfg.fix.f4;
            var sp = new StepPlan308 { step = "F4", target = f4.root + "/{" + string.Join(",", f4.houses) + "}/" + f4.housePart };
            foreach (var h in f4.houses)
            {
                var house = BuildingAudit308.Resolve(scene, f4.root + "/" + h + "/" + f4.housePart);
                if (house == null) { sp.notes.Add("absent " + f4.root + "/" + h); continue; }
                foreach (var part in f4.parts)
                {
                    var c = house.Find(part);
                    if (c == null) { sp.notes.Add(h + "/" + part + " absent (a door only exists on the lived-in house)"); continue; }
                    var f = c.GetComponent<MeshFilter>();
                    if (f == null || f.sharedMesh == null) { sp.notes.Add(h + "/" + part + " has no mesh"); continue; }
                    string now = ColliderState(c, f.sharedMesh), want = "MeshCollider:" + AssetDatabase.GetAssetPath(f.sharedMesh) + "#" + f.sharedMesh.name + ":convex=0";
                    sp.changes.Add(new Change308 { step = "F4", key = BuildingAudit308.KeyOf(c), kind = "collider", before = "none", after = want, at = c.position, note = "house:" + h, state = now == "none" ? "apply" : now == want ? "already" : "mismatch" });
                }
            }
            Settle(sp);
            return sp;
        }

        // ------------------------------------------------------------------ F5 geumpyo inn porch lantern caps

        static StepPlan308 PlanF5(BuildingAudit308.Config308 cfg, Scene scene, Ledger308 ledger)
        {
            var f5 = cfg.fix.f5;
            var sp = new StepPlan308 { step = "F5", target = f5.root + "/…/" + f5.support + "/" + f5.holder + "/" + f5.surfacePrefix + "*" };
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == f5.root);
            if (root == null) { sp.status = "absent"; sp.detail = f5.root + " absent"; return sp; }
            var moved = File.Exists(f5.quarantineList) ? File.ReadAllLines(f5.quarantineList) : Array.Empty<string>();
            foreach (var holder in root.GetComponentsInChildren<Transform>(true).Where(x => x.name == f5.holder && x.parent != null && x.parent.name == f5.support))
            {
                var old = holder.parent;
                foreach (Transform s in holder)
                {
                    if (!s.name.StartsWith(f5.surfacePrefix, StringComparison.Ordinal) || !int.TryParse(s.name.Substring(f5.surfacePrefix.Length), System.Globalization.NumberStyles.Integer, Inv, out int index)) continue;
                    var f = s.GetComponent<MeshFilter>(); var r = s.GetComponent<MeshRenderer>();
                    if (f == null || r == null) continue;
                    string key = BuildingAudit308.KeyOf(s);
                    var prior = Replay(scene, ledger, "F5", c => c.key == key || c.note == "for:" + key);
                    if (prior.Count > 0) { sp.changes.AddRange(prior); continue; }
                    if (f.sharedMesh != null) continue;                 // has geometry: not a target
                    if (!r.enabled) continue;                           // retired original (CompactKcisaReplacement.Retire): not a target
                    // 1. the generator's own key (CompactKcisaReplacement.Surfaces Clad)
                    string genKey = Hash128.Compute(BuildingAudit308.PathOf(old) + old.GetSiblingIndex() + old.position.ToString("R") + index).ToString();
                    string kcisa = f5.kcisaMeshFolder + "/" + genKey + ".asset";
                    var found = AssetDatabase.LoadAssetAtPath<Mesh>(kcisa);
                    if (found != null && found.vertexCount > 0)
                    {
                        sp.changes.Add(new Change308 { step = "F5", key = key, kind = "mesh", before = "", after = kcisa + "#" + found.name, state = "apply", at = s.position, note = "generator key" });
                        continue;
                    }
                    string q = moved.FirstOrDefault(l => l.EndsWith("/" + genKey + ".asset", StringComparison.Ordinal));
                    if (q != null)
                    {
                        string rel = q.Substring(q.IndexOf('\t') + 1);
                        string src = Path.Combine(f5.quarantineRoot, rel);
                        if (File.Exists(src))
                        {
                            sp.changes.Add(new Change308 { step = "F5", key = BuildingAudit308.KeyOf(old), kind = "restore-asset", before = "", after = src + "|" + rel, state = "apply", note = "for:" + key });
                            sp.changes.Add(new Change308 { step = "F5", key = key, kind = "mesh", before = "", after = rel + "#" + f5.holder, state = "apply", at = s.position, note = "quarantine #307" });
                            continue;
                        }
                    }
                    // 2. rebuild with the generator's timber rule into the #308 folder (shared by the three scenes when identical)
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(f5.sourcePrefab) == null) { sp.status = "blocked"; sp.detail = "source prefab missing " + f5.sourcePrefab + " — ask"; return sp; }
                    string name = "Lantern308_" + Hash128.Compute(BuildingAudit308.PathOf(old) + "|" + old.GetSiblingIndex() + "|" + old.lossyScale.ToString("R") + "|" + index);
                    string path = f5.meshFolder + "/" + name + ".asset";
                    sp.changes.Add(new Change308 { step = "F5", key = BuildingAudit308.KeyOf(old), kind = "create-mesh", before = "", after = path, state = AssetDatabase.LoadAssetAtPath<Mesh>(path) != null ? "already" : "apply", note = index.ToString(Inv) });
                    sp.changes.Add(new Change308 { step = "F5", key = key, kind = "mesh", before = "", after = path + "#" + name, state = "apply", at = s.position, note = "rebuilt (timber " + f5.sourcePrefab + ", support " + old.lossyScale.ToString("F2") + ")" });
                }
            }
            Settle(sp);
            return sp;
        }

        // CompactKcisaReplacement.Clad(wood) for one support volume: SM_Floor_001 tiles (≤ tileMax m) over |lossyScale|, in the
        // holder's frame (holder = support position/rotation, world scale 1); only the parts of material #index (Dictionary order)
        internal static Mesh BuildCapMesh(BuildingAudit308.Config308 cfg, Transform old, string indexText)
        {
            var f5 = cfg.fix.f5;
            int index = int.Parse(string.IsNullOrEmpty(indexText) ? "0" : indexText, Inv);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(f5.sourcePrefab) ?? throw new FileNotFoundException(f5.sourcePrefab);
            var rootM = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, prefab.transform.localScale) * prefab.transform.worldToLocalMatrix;
            var parts = new List<(Mesh mesh, int slot, Material mat, Matrix4x4 m)>(); bool first = true; var bounds = new Bounds();
            foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = f.GetComponent<Renderer>(); if (f.sharedMesh == null || r == null) continue;
                var m = rootM * f.transform.localToWorldMatrix;
                var mb = f.sharedMesh.bounds;
                for (int i = 0; i < 8; i++) { var v = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))); if (first) { bounds = new Bounds(v, Vector3.zero); first = false; } else bounds.Encapsulate(v); }
                for (int i = 0; i < f.sharedMesh.subMeshCount; i++) if (i < r.sharedMaterials.Length && r.sharedMaterials[i] != null) parts.Add((f.sharedMesh, i, r.sharedMaterials[i], m));
            }
            var mats = new List<Material>(); foreach (var p in parts) if (!mats.Contains(p.mat)) mats.Add(p.mat);
            if (index >= mats.Count) throw new InvalidOperationException("source has " + mats.Count + " material batch(es), surface index " + index);
            var size = new Vector3(Mathf.Abs(old.lossyScale.x), Mathf.Abs(old.lossyScale.y), Mathf.Abs(old.lossyScale.z));
            float max = f5.tileMax > 0 ? f5.tileMax : 3f;
            int nx = Mathf.Max(1, Mathf.CeilToInt(size.x / max)), nz = Mathf.Max(1, Mathf.CeilToInt(size.z / max));
            var tile = new Vector3(size.x / nx, size.y, size.z / nz);
            var combine = new List<CombineInstance>();
            for (int x = 0; x < nx; x++)
                for (int z = 0; z < nz; z++)
                {
                    var offset = new Vector3(-size.x / 2 + tile.x * (x + .5f), 0, -size.z / 2 + tile.z * (z + .5f));
                    var scale = new Vector3(tile.x / bounds.size.x, tile.y / bounds.size.y, tile.z / bounds.size.z);
                    var matrix = Matrix4x4.TRS(offset, Quaternion.identity, scale) * Matrix4x4.Translate(-bounds.center);
                    foreach (var p in parts.Where(p => p.mat == mats[index])) combine.Add(new CombineInstance { mesh = p.mesh, subMeshIndex = p.slot, transform = matrix * p.m });
                }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(combine.ToArray(), true, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------ F2 the shared DryLandscape sheet (once, not per scene)

        [Serializable] internal sealed class SheetRow308 { public string id = "", proto = "", kind = "", building = "", root = "", sheet = "", reason = "", placementJson = "", utc = "", backup = ""; public int index; public Vector3 position; public float hitHeight; public bool reverted; }
        [Serializable] internal sealed class SheetPlan308 { public string utc = "", scene = "", sheet = "", sheetSha = "", quality = ""; public List<SheetRow308> remove = new List<SheetRow308>(), review = new List<SheetRow308>(); public List<string> notes = new List<string>(); }
        [Serializable] internal sealed class SheetLedger308 { public List<SheetRow308> rows = new List<SheetRow308>(); public List<string> backups = new List<string>(); public List<string> events = new List<string>(); }
        static string SheetPlanFile => Path.Combine(FixDir, "sheet-plan.json");
        static string SheetLedgerFile => Path.Combine(FixDir, "sheet-removals.json");

        static List<SheetRow308> Intrusions(BuildingAudit308.Config308 cfg, Scene scene, WorldMacroDressingSheetSO sheet)
        {
            var rows = new List<SheetRow308>();
            var units = BuildingAudit308.UnitsOf(scene, cfg, cfg.fix.f2.buildingRoots).Where(u => u.building).ToList();
            var places = BuildingAudit308.Placements(scene, cfg).Where(p => p.sheet == sheet).ToList();
            var grid = new BuildingAudit308.PlaceGrid308(places, cfg.thresholds.c3GridCell);
            var cache = new BuildingAudit308.MeshCache308(); var hits = new List<(float y, int tri)>();
            var taken = new HashSet<int>();   // one row per placement even when two units' bounds overlap it
            foreach (var u in units)
            {
                var near = grid.In(u.bounds.min.x, u.bounds.min.z, u.bounds.max.x, u.bounds.max.z).ToList();
                if (near.Count == 0) continue;
                var soup = new BuildingAudit308.Soup308(); foreach (var r in u.lod0) soup.Add(r, cache);
                foreach (var p in near)
                {
                    float y = BuildingAudit308.Intrusion(soup, p, cfg.thresholds.c3BaseLift, hits);
                    if (float.IsNaN(y) || !taken.Add(p.index)) continue;
                    var fp = sheet.FixedPlacements[p.index];
                    rows.Add(new SheetRow308 { id = p.id, index = p.index, proto = p.proto, kind = p.kind, building = u.name, root = u.root, sheet = AssetDatabase.GetAssetPath(sheet), position = p.pos, hitHeight = y - p.pos.y, reason = cfg.fix.f2.reason, placementJson = JsonUtility.ToJson(fp) });
                }
            }
            return rows;
        }

        static string SheetScene(BuildingAudit308.Config308 cfg, out Scene scene, out string previous, out bool opened)
        {
            var active = SceneManager.GetActiveScene();
            string alias = cfg.AliasOf(active.path) ?? "main";
            return OpenTarget(cfg, alias, out scene, out previous, out opened);
        }

        static string SheetPlan()
        {
            var cfg = BuildingAudit308.LoadConfig(out string err);
            if (cfg == null) return "refused: " + err;
            var f2 = cfg.fix.f2;
            if (cfg.ProtectedAsset(f2.sheet)) return "refused: protected sheet " + f2.sheet;
            var sheet = AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(f2.sheet);
            if (sheet == null) return "refused: no sheet " + f2.sheet;
            string refuse = SheetScene(cfg, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            var plan = new SheetPlan308 { utc = BuildingAudit308.Utc(), scene = scene.path, sheet = f2.sheet, sheetSha = BuildingAudit308.Sha(BuildingAudit308.Abs(f2.sheet)), quality = BuildingAudit308.QualityName() };
            try
            {
                Physics.SyncTransforms();
                var rows = Intrusions(cfg, scene, sheet);
                if (f2.ids.Length > 0)
                {
                    plan.remove = rows.Where(r => f2.ids.Contains(r.id)).ToList();
                    plan.review = rows.Where(r => !f2.ids.Contains(r.id)).ToList();
                    foreach (var id in f2.ids.Where(i => rows.All(r => r.id != i))) plan.notes.Add("configured id " + id + " is not under a building face now (not removed)");
                }
                else
                {
                    foreach (var g in rows.GroupBy(r => (r.building, r.kind)))
                    {
                        var exp = f2.expected.FirstOrDefault(e => e.building == g.Key.building && e.kind == g.Key.kind);
                        int n = g.Count();
                        if (exp == null) { plan.review.AddRange(g); plan.notes.Add("outside the decided 11+1: " + g.Key.building + " " + g.Key.kind + " ×" + n + " (review, not removed)"); }
                        else if (n > exp.count) { plan.review.AddRange(g); plan.notes.Add(g.Key.building + " " + g.Key.kind + ": found " + n + " > decided " + exp.count + " (review, none removed)"); }
                        else { plan.remove.AddRange(g); if (n < exp.count) plan.notes.Add(g.Key.building + " " + g.Key.kind + ": found " + n + " < decided " + exp.count); }
                    }
                    foreach (var e in f2.expected.Where(e => rows.All(r => r.building != e.building || r.kind != e.kind))) plan.notes.Add("decided " + e.building + " " + e.kind + " ×" + e.count + " not found now");
                }
                Directory.CreateDirectory(FixDir);
                File.WriteAllText(SheetPlanFile, JsonUtility.ToJson(plan, true), new UTF8Encoding(false));
            }
            finally { GoBack(previous, scene, opened, true); }
            var sb = new StringBuilder("sheet-plan " + f2.sheet + " sha " + plan.sheetSha + ": remove " + plan.remove.Count + ", review " + plan.review.Count + " -> " + SheetPlanFile + "\n");
            foreach (var r in plan.remove) sb.AppendLine("  remove " + r.kind + " " + r.id + " #" + r.index + " " + r.proto + " under " + r.root + "/" + r.building + " @" + BuildingAudit308.V(r.position) + " face +" + r.hitHeight.ToString("F2", Inv) + " m");
            foreach (var r in plan.review) sb.AppendLine("  review " + r.kind + " " + r.id + " under " + r.root + "/" + r.building);
            foreach (var n in plan.notes) sb.AppendLine("  note " + n);
            return sb.ToString();
        }

        static string SheetApply()
        {
            var cfg = BuildingAudit308.LoadConfig(out string err);
            if (cfg == null) return "refused: " + err;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only";
            var f2 = cfg.fix.f2;
            if (!File.Exists(SheetPlanFile)) return "refused: run sheet-plan first";
            var plan = JsonUtility.FromJson<SheetPlan308>(File.ReadAllText(SheetPlanFile));
            if (plan.sheet != f2.sheet) return "refused: the plan is for " + plan.sheet;
            if (cfg.ProtectedAsset(plan.sheet)) return "refused: protected sheet";
            if (plan.remove.Count > f2.expected.Sum(e => e.count) + f2.ids.Length) return "refused: plan removes " + plan.remove.Count + " > the decided " + f2.expected.Sum(e => e.count);
            var sheet = AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(plan.sheet);
            if (sheet == null) return "refused: no sheet " + plan.sheet;
            if (EditorUtility.IsDirty(sheet)) return "refused: the sheet has unsaved changes in memory (another session?)";
            var fps = sheet.FixedPlacements ?? Array.Empty<WorldMacroDressingSheetSO.FixedPlacement>();
            bool Match(WorldMacroDressingSheetSO.FixedPlacement p, SheetRow308 r) => p != null && p.Id == r.id && Vector3.Distance(p.Position, r.position) <= f2.positionTolerance;
            var present = plan.remove.Where(r => fps.Any(p => Match(p, r))).ToList();
            if (present.Count == 0) return "already: none of the " + plan.remove.Count + " planned placements remain in " + plan.sheet + " (no change)";
            string sha = BuildingAudit308.Sha(BuildingAudit308.Abs(plan.sheet));
            if (sha != plan.sheetSha) return "refused: " + plan.sheet + " changed since sheet-plan (" + plan.sheetSha + " -> " + sha + "); run sheet-plan again";
            // re-check each row in the open scene: the upward ray must still meet a building face
            string refuse = SheetScene(cfg, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            List<SheetRow308> still;
            try { Physics.SyncTransforms(); var now = Intrusions(cfg, scene, sheet); still = present.Where(r => now.Any(n => n.id == r.id && n.index == r.index)).ToList(); }
            finally { GoBack(previous, scene, opened, true); }
            var dropped = present.Where(r => !still.Contains(r)).ToList();
            if (still.Count == 0) return "refused: no planned placement is under a building face any more (" + string.Join(", ", dropped.Select(r => r.id)) + ")";
            // Id + position must name exactly one placement each (an empty or repeated Id at one spot would take extra rows with it)
            var keep = fps.Where(p => !still.Any(r => Match(p, r))).ToArray();
            int removed = fps.Length - keep.Length;
            if (still.Any(r => string.IsNullOrEmpty(r.id))) return "refused: a planned placement has no Id (" + string.Join(", ", still.Where(r => string.IsNullOrEmpty(r.id)).Select(r => "#" + r.index)) + "); nothing changed";
            if (removed != still.Count) return "refused: " + still.Count + " planned row(s) match " + removed + " placement(s) by Id+position; nothing changed";
            int cap = f2.expected.Sum(e => e.count) + f2.ids.Length;
            if (removed > cap) return "refused: " + removed + " removals > the decided " + cap + "; nothing changed";
            string utc = BuildingAudit308.Utc();
            string backupDir = Path.Combine(BuildingAudit308.Folder, "Backup", "Sheets", utc); Directory.CreateDirectory(backupDir);
            string file = BuildingAudit308.Abs(plan.sheet), backup = Path.Combine(backupDir, Path.GetFileName(file));
            File.Copy(file, backup, false); if (File.Exists(file + ".meta")) File.Copy(file + ".meta", backup + ".meta", false);
            File.WriteAllText(Path.Combine(backupDir, "sha256.txt"), sha + "  " + Path.GetFileName(file) + "\n", new UTF8Encoding(false));
            var csv = new StringBuilder("id,index,prototype,kind,building,x,y,z,face_height\n");
            foreach (var r in still) csv.Append(r.id).Append(',').Append(r.index).Append(',').Append(r.proto).Append(',').Append(r.kind).Append(',').Append(r.root + "/" + r.building).Append(',')
                .Append(r.position.x.ToString("F3", Inv)).Append(',').Append(r.position.y.ToString("F3", Inv)).Append(',').Append(r.position.z.ToString("F3", Inv)).Append(',').Append(r.hitHeight.ToString("F2", Inv)).Append('\n');
            File.WriteAllText(Path.Combine(FixDir, "sheet-removals-" + utc + ".csv"), csv.ToString(), new UTF8Encoding(true));
            sheet.FixedPlacements = keep;
            EditorUtility.SetDirty(sheet);
            AssetDatabase.SaveAssetIfDirty(sheet);
            foreach (var a in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (a.Sheet == sheet) a.Invalidate();
            var ledger = File.Exists(SheetLedgerFile) ? JsonUtility.FromJson<SheetLedger308>(File.ReadAllText(SheetLedgerFile)) : new SheetLedger308();
            foreach (var r in still) { r.utc = utc; r.backup = backup; ledger.rows.Add(r); }
            ledger.backups.Add(backup + " sha " + sha);
            ledger.events.Add(utc + " sheet-apply removed " + removed + " from " + plan.sheet + " sha " + sha + " -> " + BuildingAudit308.Sha(file) + (dropped.Count > 0 ? "; dropped (no longer under a face) " + string.Join(",", dropped.Select(r => r.id)) : ""));
            File.WriteAllText(SheetLedgerFile, JsonUtility.ToJson(ledger, true), new UTF8Encoding(false));
            return "applied: removed " + removed + " placement(s) from " + plan.sheet + " (sha " + sha + " -> " + BuildingAudit308.Sha(file) + "), backup " + backup + ", ledger " + SheetLedgerFile + (dropped.Count > 0 ? "; not removed (no longer under a face): " + string.Join(", ", dropped.Select(r => r.id)) : "");
        }

        static string SheetRevert()
        {
            var cfg = BuildingAudit308.LoadConfig(out string err);
            if (cfg == null) return "refused: " + err;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only";
            if (!File.Exists(SheetLedgerFile)) return "refused: no " + SheetLedgerFile;
            var ledger = JsonUtility.FromJson<SheetLedger308>(File.ReadAllText(SheetLedgerFile));
            var rows = ledger.rows.Where(r => !r.reverted).ToList();
            if (rows.Count == 0) return "refused: nothing to revert";
            string backup = rows.OrderBy(r => r.utc, StringComparer.Ordinal).First().backup;   // the oldest backup = the sheet before #308
            if (!File.Exists(backup)) return "refused: backup missing " + backup;
            string path = rows[0].sheet; string file = BuildingAudit308.Abs(path);
            var sheet = AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(path);
            if (sheet != null && EditorUtility.IsDirty(sheet)) return "refused: the sheet has unsaved changes in memory";
            string before = BuildingAudit308.Sha(file);
            File.Copy(backup, file, true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            sheet = AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(path);
            foreach (var a in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (a.Sheet == sheet) a.Invalidate();
            foreach (var r in rows) r.reverted = true;
            ledger.events.Add(BuildingAudit308.Utc() + " revert:sheet restored " + backup + " (" + before + " -> " + BuildingAudit308.Sha(file) + ")");
            File.WriteAllText(SheetLedgerFile, JsonUtility.ToJson(ledger, true), new UTF8Encoding(false));
            return "reverted: " + path + " restored from " + backup + " (sha " + before + " -> " + BuildingAudit308.Sha(file) + ")";
        }

        // ------------------------------------------------------------------ verify rows

        static void VerifySteps(BuildingAudit308.Config308 cfg, Scene scene, Ledger308 ledger, Action<bool, string> Row, Action<string> Info)
        {
            var th = cfg.thresholds; var tok = cfg.tokens;
            var places = BuildingAudit308.Placements(scene, cfg);
            // F1: both houses land (C2), no sheet tree on the new site, their props have ground
            foreach (var house in cfg.fix.f1.houses)
            {
                var t = BuildingAudit308.Resolve(scene, house.path); if (t == null) { Info("F1 " + house.path + " absent"); continue; }
                var rc = cfg.roots.FirstOrDefault(r => r.name == t.root.name) ?? new BuildingAudit308.Root308 { name = t.root.name, depth = 1 };
                var u = BuildingAudit308.MakeUnit(t, rc, false);
                var plates = BuildingAudit308.Plates(th, tok, u, out string how);
                if (plates.Length == 0) Row(false, "AC-B4 " + house.path + " has no plate collider (C2 not measurable)");
                else
                {
                    var rows = new List<(string level, Vector3 at, float value, string detail)>();
                    var skirts = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(false)).Where(r => BuildingAudit308.HasAny(r.name, tok.skirt) || (r.transform.parent != null && BuildingAudit308.HasAny(r.transform.parent.name, tok.skirt))).ToList();
                    var m = BuildingAudit308.Landing(u, plates, th, tok, p => skirts.Any(s => s.bounds.SqrDistance(p) <= th.c2SkirtRadius * th.c2SkirtRadius), rows);
                    Row(m.c2 == "PASS" || m.c2 == "WARN", "AC-B4 " + house.path + " C2 " + m.c2 + " (hover share " + m.hoverShare.ToString("F2", Inv) + ", max " + m.maxHover.ToString("F2", Inv) + " m, buried " + m.buriedShare.ToString("F2", Inv) + ", threshold rise " + m.maxThresholdRise.ToString("F2", Inv) + " m)" + (rows.Count > 0 ? " — " + string.Join("; ", rows.Select(r => r.detail)) : ""));
                }
                GroundFit299.Footprint308(t, out var half, out var off, out float yaw);
                var rot = Quaternion.Euler(0, yaw, 0); var inv = Quaternion.Inverse(rot); var centre = t.position + rot * new Vector3(off.x, 0, off.y);
                int trees = places.Count(p => p.kind == "Tree" && Mathf.Abs((inv * (p.pos - centre)).x) <= half.x + cfg.fix.f1.apron && Mathf.Abs((inv * (p.pos - centre)).z) <= half.y + cfg.fix.f1.apron);
                Row(trees == 0, "AC-B4 " + house.path + " sheet trees inside the site (+" + cfg.fix.f1.apron + " m): " + trees);
                var parent = BuildingAudit308.Resolve(scene, cfg.fix.f1.parent);
                if (parent != null)
                {
                    int noGround = 0, n = 0; var names = new List<string>();
                    foreach (Transform c in parent)
                    {
                        if (!BuildingAudit308.HasAny(c.name, cfg.fix.f1.propTokens) || Flat(c.position, t.position) > cfg.fix.f1.propRadius) continue;
                        foreach (var r in c.GetComponentsInChildren<Renderer>(false))
                        {
                            if (!r.enabled) continue; n++;
                            var b = r.bounds; var own = r.GetComponentsInChildren<Collider>(true).Concat(r.GetComponentsInParent<Collider>(true)).ToArray();
                            bool ground = Physics.RaycastAll(new Vector3(b.center.x, b.max.y + 300f, b.center.z), Vector3.down, 800f, ~0, QueryTriggerInteraction.Ignore).Any(h => !own.Contains(h.collider) && h.point.y <= b.min.y + .5f);
                            if (!ground) { noGround++; if (names.Count < 6) names.Add(BuildingAudit308.KeyOf(r.transform)); }
                        }
                    }
                    Row(noGround == 0, "AC-B4 fence/timber renderers within " + cfg.fix.f1.propRadius + " m of " + house.path + " with no ground (Floating307 rule, .5 m): " + noGround + "/" + n + (names.Count > 0 ? " e.g. " + string.Join(", ", names) : ""));
                }
                Info("NavMesh around " + house.path + ": " + NavRing(t.position, Mathf.Max(half.x, half.y) + 2f) + "/8 samples within 1 m");
            }
            // F3: no duplicates left under the parent; reseated objects sit on the ground, clear of colliders, trees and routes
            var f3p = BuildingAudit308.Resolve(scene, cfg.fix.f3.parent);
            if (f3p != null)
            {
                var dups = BuildingAudit308.SiblingDuplicates(f3p, th.c4PosTol, th.c4RotTol).Where(p => p.a.parent == f3p).ToList();
                Row(dups.Count == 0, "AC-B6 " + cfg.fix.f3.parent + " duplicate sibling pairs: " + dups.Count + (dups.Count > 0 ? " (" + string.Join(", ", dups.Take(5).Select(d => BuildingAudit308.KeyOf(d.b))) + ")" : ""));
                var f3done = Replay(scene, ledger, "F3").Where(c => c.state == "already").ToList();
                var hungSet = new HashSet<Transform>(f3done.Where(c => c.note.Contains("height:neighbours")).Select(c => BuildingAudit308.Resolve(scene, c.key)).Where(x => x != null));
                var hungGaps = new List<string>();
                var moved = f3done.Select(c => BuildingAudit308.Resolve(scene, c.key)).Where(x => x != null).ToList();
                var gaps = new List<float>(); int overlaps = 0, piles = 0, contacts = 0, inTrees = 0, inRoutes = 0; var touching = new List<string>();
                var corridors = BuildingAudit308.Corridors(scene, cfg);
                foreach (var b in moved)
                {
                    if (!RendererBounds(b, out var bb)) continue;
                    float g = BuildingAudit308.TerrainNear(new Vector3(bb.center.x, bb.min.y, bb.center.z), th.c2TerrainWindow);
                    if (!float.IsNaN(g)) { if (hungSet.Contains(b)) hungGaps.Add(b.name + " " + (bb.min.y - g).ToString("F2", Inv)); else gaps.Add(Mathf.Abs(bb.min.y - g)); }
                    var own = new HashSet<Collider>(b.GetComponentsInChildren<Collider>(true));
                    foreach (var col in b.GetComponentsInChildren<Collider>(false))
                    {
                        var others = Physics.OverlapBox(col.bounds.center, col.bounds.extents * .95f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Where(o => !own.Contains(o) && !BuildingAudit308.TerrainLike(o)).ToList();
                        if (others.Count == 0) continue;
                        // parts of one assembly under the same parent touch by design (post and rail, rail and rail, wheel and cart bed):
                        // those are listed, not failed. A failure is something outside the parent, or a pile (below).
                        if (others.Any(o => !o.transform.IsChildOf(f3p))) overlaps++; else contacts++;
                        touching.Add(BuildingAudit308.KeyOf(b) + " ~ " + string.Join(" + ", others.Select(o => BuildingAudit308.KeyOf(o.transform)).Distinct().Take(4)));
                    }
                    // a pile: a same-name sibling standing on the same spot (the height may differ after seating)
                    foreach (Transform c in f3p)
                        if (c != b && c.name == b.name && Vector2.Distance(new Vector2(c.localPosition.x, c.localPosition.z), new Vector2(b.localPosition.x, b.localPosition.z)) <= .25f) { piles++; touching.Add(BuildingAudit308.KeyOf(b) + " piled on " + BuildingAudit308.KeyOf(c)); break; }
                    inTrees += places.Count(p => p.kind == "Tree" && Mathf.Abs(p.pos.x - bb.center.x) < bb.extents.x + .35f && Mathf.Abs(p.pos.z - bb.center.z) < bb.extents.z + .35f);
                    if (BuildingAudit308.CorridorClearance(new Vector2(bb.center.x, bb.center.z), corridors, out _) < -cfg.fix.f3.homeCorridorSlack) inRoutes++;
                }
                if (moved.Count > 0)
                {
                    if (gaps.Count > 0) Row(Median(gaps) <= .10f, "AC-B6 reseated objects bottom-ground |difference| median " + Median(gaps).ToString("F3", Inv) + " m (n " + gaps.Count + ")");
                    if (hungGaps.Count > 0) Info("AC-B6 objects whose height follows their unmoved neighbours (assembly parts), bottom above the ground: " + string.Join(", ", hungGaps) + " m");
                    Row(overlaps == 0 && piles == 0 && inTrees == 0 && inRoutes == 0, "AC-B6 reseated objects: piled on a same-name sibling " + piles + ", overlaps with colliders outside " + cfg.fix.f3.parent + " " + overlaps + ", tree trunks " + inTrees + ", in route corridors " + inRoutes + " (contacts inside the parent " + contacts + ")" + (touching.Count > 0 ? " — " + string.Join("; ", touching.Take(10)) : ""));
                }
            }
            // F4: no walk-through on the thatched houses; no interaction point inside the new colliders
            var f4 = cfg.fix.f4;
            var session = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).FirstOrDefault();
            foreach (var h in f4.houses)
            {
                var house = BuildingAudit308.Resolve(scene, f4.root + "/" + h + "/" + f4.housePart); if (house == null) { Info("F4 " + h + " absent"); continue; }
                var probe = BuildingAudit308.Probe(house, f4.probeHeight, f4.probeRadius, new BuildingAudit308.MeshCache308(), BuildingAudit308.FloorTop(house, cfg));
                Row(probe.passThrough == 0, "AC-B7 wallprobe " + h + ": pass-through " + probe.passThrough + "/" + probe.directions + " (blocked " + probe.blocked + ")");
                var cols = f4.parts.Select(p => house.Find(p)).Where(x => x != null).SelectMany(x => x.GetComponents<MeshCollider>()).ToList();
                int inside = 0;
                if (session != null && session.Content != null)
                    foreach (var p in session.Content.Points.Where(p => p != null && Flat(p.Position, house.position) <= f4.interactionRadius))
                        if (Physics.OverlapCapsule(p.Position + Vector3.up * f4.probeRadius, p.Position + Vector3.up * (f4.probeHeight - f4.probeRadius), f4.probeRadius * .9f, ~0, QueryTriggerInteraction.Ignore).Any(c => cols.Contains(c))) inside++;
                foreach (var a in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<NpcJobActor>(true)).Where(a => Flat(a.transform.position, house.position) <= f4.interactionRadius))
                    if (Physics.OverlapCapsule(a.transform.position + Vector3.up * f4.probeRadius, a.transform.position + Vector3.up * (f4.probeHeight - f4.probeRadius), f4.probeRadius * .9f, ~0, QueryTriggerInteraction.Ignore).Any(c => cols.Contains(c))) inside++;
                Row(inside == 0, "AC-B7 interaction points / NPC job places inside the new " + h + " colliders: " + inside);
                Info("NavMesh around " + h + ": " + NavRing(house.position, 6f) + "/8 samples within 1 m");
            }
            // F5: the porch lantern caps have geometry with an enabled renderer
            var f5Changes = Replay(scene, ledger, "F5").Where(c => c.kind == "mesh").ToList();
            foreach (var c in f5Changes)
            {
                var s = BuildingAudit308.Resolve(scene, c.key);
                var f = s != null ? s.GetComponent<MeshFilter>() : null; var r = s != null ? s.GetComponent<MeshRenderer>() : null;
                Row(f != null && r != null && r.enabled && f.sharedMesh != null && f.sharedMesh.vertexCount > 0, "AC-B3 " + c.key + " mesh " + (f != null && f.sharedMesh != null ? f.sharedMesh.name + " " + f.sharedMesh.vertexCount + " vertices" : "null") + ", renderer " + (r != null && r.enabled ? "on" : "OFF"));
            }
            var root5 = scene.GetRootGameObjects().FirstOrDefault(g => g.name == cfg.fix.f5.root);
            if (root5 != null)
            {
                int empty = root5.GetComponentsInChildren<MeshRenderer>(false).Count(r => r.enabled && r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh == null);
                Row(empty == 0, "AC-B3 enabled renderers with a null mesh under " + cfg.fix.f5.root + ": " + empty);
            }
        }

        static int NavRing(Vector3 c, float r)
        {
            int n = 0;
            for (int k = 0; k < 8; k++) { var p = c + Quaternion.Euler(0, k * 45f, 0) * Vector3.forward * r; if (NavMesh.SamplePosition(p, out var hit, 1f, NavMesh.AllAreas) && Flat(hit.position, p) <= 1f) n++; }
            return n;
        }
    }
}
