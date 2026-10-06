using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    // check:<scene> - read-only on the scene and on every asset. It measures on the physics scene (terrain tile colliders + the wall's
    // own colliders) with the cliff ledger's capsule walker (the player's CharacterController and the motor's rules, D308-9d jump rule).
    // Two things are switched for the probes and restored in a finally block, never saved: the gate's probe pose (closed / open) and the
    // shell colliders of the forest seam runs the wall replaces (so the capsule reaches the wall instead of the forest in front of it).
    // Automated Edit-mode physics, not manual play. The last line answers AC-B11f: "seam shells may be removed: yes / no".
    public static partial class Jangseong308
    {
        [Serializable] sealed class ModuleRow { public string id; public bool gateSpan, aboveDeck; public float top, eaGround, topOverEa, grade, a1, a1Top, late16; }
        [Serializable] sealed class ProbeRow { public string at, kind, variant, verdict, end, by; public float x, z, maxPast, endY, endX, endZ, startY, maxWallPast; public int jumps, lifts; }
        [Serializable] sealed class CheckReport
        {
            public string format = "cb308.wall.check.1", utc, scene, key, buildKey, layoutSha256, shells, verdict; public bool sceneDirty;
            public int pass, fail, leaks, onTopFromEa, inconclusive, gaps, deckReached, deckCrossed, bastionDecksReached;
            public Counts counts; public ModuleRow[] modules; public ProbeRow[] probes; public string[] lines; public string[] gapsAt;
        }

        /// <summary>Poses the gate closed or open for a probe without reading the ledger; Dispose restores every pose and flag.</summary>
        sealed class GateProbe : IDisposable
        {
            readonly WorldSealGate308 g; readonly bool enabled; readonly Quaternion l, r; readonly bool[] blockers, nav, open;
            public GateProbe(WorldSealGate308 gate, bool openPose)
            {
                g = gate; enabled = g.enabled; l = g.LeftLeaf.localRotation; r = g.RightLeaf.localRotation;
                blockers = (g.ClosedBlockers ?? new Collider[0]).Select(c => c != null && c.enabled).ToArray(); nav = (g.ClosedNavigation ?? new NavMeshObstacle[0]).Select(o => o != null && o.enabled).ToArray();
                open = (g.OpenColliders ?? new Collider[0]).Select(c => c != null && c.enabled).ToArray();
                g.enabled = false; g.SetProbePose(openPose); Physics.SyncTransforms();
            }
            public void Dispose()
            {
                g.LeftLeaf.localRotation = l; g.RightLeaf.localRotation = r;
                for (int i = 0; i < blockers.Length; i++) if (g.ClosedBlockers[i] != null) g.ClosedBlockers[i].enabled = blockers[i];
                for (int i = 0; i < nav.Length; i++) if (g.ClosedNavigation[i] != null) g.ClosedNavigation[i].enabled = nav[i];
                for (int i = 0; i < open.Length; i++) if (g.OpenColliders[i] != null) g.OpenColliders[i].enabled = open[i];
                g.ResyncAfterProbe(); g.enabled = enabled; Physics.SyncTransforms();
            }
        }

        /// <summary>Switches off the shell colliders of the forest runs the wall replaces (their chunks by name; joint strips and plugs
        /// by place: centre within `jointRadius` of the wall centreline); Dispose switches them back on.</summary>
        sealed class ShellsOff : IDisposable
        {
            readonly List<Collider> off = new List<Collider>(); public int Count => off.Count; public int Joints;
            public ShellsOff(Scene scene, string[] runs, bool keep, List<Seated> modules, float jointRadius)
            {
                if (keep || runs == null || runs.Length == 0) return;
                foreach (var root in scene.GetRootGameObjects().Where(g => g.name == ForestRoot && CliffCore308.Saved(g)))
                    foreach (var c in root.GetComponentsInChildren<Collider>(true))
                    {
                        if (!c.enabled) continue;
                        bool chunk = runs.Any(id => c.name.StartsWith(id + "_", StringComparison.Ordinal)), joint = false;
                        if (!chunk && jointRadius > 0f && (c.name.StartsWith("E305_J", StringComparison.Ordinal) || c.name.StartsWith("E305_P", StringComparison.Ordinal)))
                        {
                            var q = new Vector2(c.bounds.center.x, c.bounds.center.z);
                            foreach (var s in modules)
                            {
                                var ab = s.B - s.A; float t = Mathf.Clamp01(Vector2.Dot(q - s.A, ab) / ab.sqrMagnitude);
                                if (Vector2.Distance(q, s.A + ab * t) <= jointRadius) { joint = true; break; }
                            }
                        }
                        if (!chunk && !joint) continue;
                        c.enabled = false; off.Add(c); if (joint) Joints++;
                    }
                Physics.SyncTransforms();
            }
            public void Dispose() { foreach (var c in off) if (c != null) c.enabled = true; off.Clear(); Physics.SyncTransforms(); }
        }

        static Bounds FrameBounds(Transform frame, MeshFilter f)
        {
            var b = f.sharedMesh.bounds; var c = b.center; var e = b.extents; var first = frame.InverseTransformPoint(f.transform.TransformPoint(c)); var result = new Bounds(first, Vector3.zero);
            for (int i = 0; i < 8; i++) result.Encapsulate(frame.InverseTransformPoint(f.transform.TransformPoint(c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)))));
            return result;
        }

        static bool Inside(Bounds inner, Bounds outer, float tol) { outer.Expand(tol * 2f); return outer.Contains(inner.min) && outer.Contains(inner.max); }

        static string Check(string token, Dictionary<string, string> opt)
        {
            var cfg = CliffCore308.LoadConfig(); string path = CliffCore308.ScenePath(cfg, token); string key = CliffCore308.SceneKey(cfg, path);
            var lay = LoadLayout(); var scene = OpenForRead(path);
            bool withShells = opt != null && opt.ContainsKey("withshells");
            int max = opt != null && opt.TryGetValue("max", out string mo) && int.TryParse(mo, out int mv) ? mv : int.MaxValue;
            var report = new CheckReport { utc = PostLedger308.Utc(), scene = path, key = key, layoutSha256 = lay.Sha256, sceneDirty = scene.isDirty };
            var lines = new List<string>(); var rows = new List<ProbeRow>();
            void C(bool ok, string what) { lines.Add((ok ? "PASS " : "FAIL ") + what); if (ok) report.pass++; else report.fail++; }
            void I(string what) => lines.Add("INFO " + what);
            string Finish(string shells)
            {
                report.shells = shells; report.verdict = (report.fail == 0 ? "OK" : "NOT OK") + ", PASS " + report.pass + " / FAIL " + report.fail;
                lines.Add("seam shells may be removed: " + shells);
                report.lines = lines.ToArray(); report.probes = rows.ToArray();
                CliffCore308.WriteText(CliffCore308.OutFile(cfg, "cb308-wall-check-" + key + ".json"), JsonUtility.ToJson(report, true));
                string text = "Jangseong308 check " + key + " (" + path + ") " + report.utc + ": " + report.verdict + "\n" + string.Join("\n", lines);
                CliffCore308.WriteText(CliffCore308.OutFile(cfg, "cb308-wall-check-" + key + ".txt"), text);
                return text;
            }

            var roots = Roots(scene, lay);
            C(roots.Length == 1, "one " + lay.root + " root in the scene (found " + roots.Length + ")");
            if (roots.Length != 1) return Finish("no (the wall is not built in this scene)");
            var rootGo = roots[0]; var root = rootGo.transform; var session = CliffCore308.Session(scene); var ledger = ReadLedger(cfg, path);
            var ground = (CliffCore308.Ground)CliffCore308.TerrainRoot(scene, cfg); var p = cfg.probe; var lim = lay.limits; float hd = lay.moduleHalfDepth;
            var stale = StaleInputs(lay);
            C(stale.Count == 0, "the layout inputs match the files on disk" + (stale.Count > 0 ? " - STALE: " + string.Join(", ", stale) : ""));
            var plan = Compute(lay, ground); report.buildKey = Marker(rootGo, "key");
            C(report.buildKey == plan.Key, "the built seat is the seat measured now (build " + report.buildKey + ", measured " + plan.Key + ")" + (report.buildKey == plan.Key ? "" : " - the ground or the layout changed after apply: run apply again"));
            C(ledger.state == "applied" && ledger.buildKey == report.buildKey, "ledger: " + ledger.state + (ledger.buildKey != "" ? " build " + ledger.buildKey : "") + " = the root in the scene");
            C(Mathf.Abs(plan.GateFloor - lay.gate.floorY) <= lim.floorTolerance, "gate floor " + F(plan.GateFloor) + " m = the user-fixed " + F(lay.gate.floorY) + " (door-plane ground " + F(plan.DoorLow) + ".." + F(plan.DoorHigh) + ", leaf bottoms "
                + F(plan.DoorLow - (plan.GateFloor + lay.gate.doorBottom)) + ".." + F(plan.DoorHigh - (plan.GateFloor + lay.gate.doorBottom)) + " m in the ground: door gap 0)");

            // ---- gate wiring and authored state
            var gates = rootGo.GetComponentsInChildren<WorldSealGate308>(true); var gate = gates.FirstOrDefault();
            C(gates.Length == 1, "one WorldSealGate308 (found " + gates.Length + ")");
            bool gateOk = gate != null && gate.IsConfigured && gate.ClosedNavigation != null && gate.ClosedNavigation.Length > 0 && gate.OpenColliders != null && gate.OpenColliders.Length == 2
                && gate.ClosedBlockers.All(c => c != null) && gate.ClosedNavigation.All(o => o != null) && gate.OpenColliders.All(c => c != null);
            C(gateOk, "gate fields wired (leaves, blocker, carve, two open-leaf colliders)");
            if (gate != null)
            {
                C(gate.Session == session && session != null, "gate Session = the scene session");
                C(gate.RequiredCompleted == WorldMacroPlaytestSession.SouthGateOpenedId, "AC-B11e the gate opens only on " + WorldMacroPlaytestSession.SouthGateOpenedId + " (RequiredCompleted '" + gate.RequiredCompleted + "'), no interaction component on it");
            }
            if (gateOk)
            {
                bool authored = gate.ClosedBlockers.All(c => !c.enabled) && gate.ClosedNavigation.All(o => !o.enabled) && gate.OpenColliders.All(c => !c.enabled);
                C(authored, "authored state: blocker, carve and open-leaf colliders disabled (the edit-time NavMesh bake sees the pass open; the component closes it in Play)");
                C(Quaternion.Angle(gate.LeftLeaf.localRotation, Quaternion.identity) < .5f && Quaternion.Angle(gate.RightLeaf.localRotation, Quaternion.identity) < .5f, "leaves saved closed");
            }

            // ---- per module on the physics scene: top over the EA ground, grade, A1 (recorded exception), hill ground over the deck
            float capZ = 0f, capY = float.NegativeInfinity; for (int i = 0; i + 1 < lay.wallKit.colliderProfileZY.Length; i += 2) if (lay.wallKit.colliderProfileZY[i + 1] > capY) { capY = lay.wallKit.colliderProfileZY[i + 1]; capZ = lay.wallKit.colliderProfileZY[i]; }
            var wallT = root.Find("Wall"); var colliderOf = new Dictionary<string, MeshCollider>();
            if (wallT != null) foreach (var mcol in wallT.GetComponentsInChildren<MeshCollider>(true)) if (mcol.convex) colliderOf[mcol.name] = mcol;
            var table = new List<ModuleRow>(); var lattice = new Dictionary<long, float>(); float walkable = Mathf.Cos(p.capsuleSlope * Mathf.Deg2Rad);
            int missingTop = 0, underMin = 0; float worstTop = float.PositiveInfinity, worstGrade = 0f; string worstTopAt = "", worstGradeAt = "";
            foreach (var s in plan.Modules.Where(x => !x.Hidden))
            {
                var row = new ModuleRow { id = s.M.id, gateSpan = s.M.gateSpan, topOverEa = float.PositiveInfinity, eaGround = float.NegativeInfinity, top = float.NaN };
                if (!colliderOf.TryGetValue(s.M.id + "_col", out var mc)) { missingTop++; table.Add(row); continue; }
                float firstTop = float.NaN, lastTop = float.NaN;
                for (int k = 0; k < 5; k++)
                {
                    float u = (k + .5f) / 5f; var q = Vector2.Lerp(s.A, s.B, u) + s.Late * (capZ + .05f);
                    if (!mc.Raycast(new Ray(new Vector3(q.x, 3000f, q.y), Vector3.down), out var hit, 6000f)) { missingTop++; continue; }
                    if (k == 0) firstTop = hit.point.y; if (k == 4) lastTop = hit.point.y; if (k == 2) row.top = hit.point.y;
                    float strip = float.NegativeInfinity;
                    for (float o = lim.eaStripFrom; o <= lim.eaStripTo + 1e-3f; o += lim.eaStripStep)
                    {
                        var e = Vector2.Lerp(s.A, s.B, u) - s.Late * (hd + o);
                        if (CliffCore308.TerrainY(ground, e.x, e.y, out float gy, out var normal) && normal.y >= walkable && gy > strip) strip = gy;     // a rock face in front of the wall is the barrier itself
                    }
                    if (float.IsNegativeInfinity(strip)) continue;
                    row.eaGround = Mathf.Max(row.eaGround, strip); row.topOverEa = Mathf.Min(row.topOverEa, hit.point.y - strip);
                }
                if (!float.IsNaN(firstTop) && !float.IsNaN(lastTop)) row.grade = (lastTop - firstTop) / (s.M.length * .8f);
                if (!float.IsInfinity(row.topOverEa)) { if (row.topOverEa < lim.topOverEaMin) underMin++; if (row.topOverEa < worstTop) { worstTop = row.topOverEa; worstTopAt = s.M.id; } }
                if (Mathf.Abs(row.grade) > worstGrade) { worstGrade = Mathf.Abs(row.grade); worstGradeAt = s.M.id; }
                // A1 and the hill ground near the deck on the 4 m lattice of the terrain colliders (the look gate's measure)
                var c = s.Centre; float high = float.NegativeInfinity, near = float.NegativeInfinity;
                int x0 = Mathf.FloorToInt((c.x - lim.a1Radius) / 4f), x1 = Mathf.CeilToInt((c.x + lim.a1Radius) / 4f), z0 = Mathf.FloorToInt((c.y - lim.a1Radius) / 4f), z1 = Mathf.CeilToInt((c.y + lim.a1Radius) / 4f);
                for (int iz = z0; iz <= z1; iz++)
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        var d = new Vector2(ix * 4f, iz * 4f) - c; float dist = d.magnitude;
                        if (dist > lim.a1Radius || Vector2.Dot(d, s.Late) <= lim.a1SideMin) continue;
                        long cell = ((long)ix << 32) ^ (uint)iz;
                        if (!lattice.TryGetValue(cell, out float y)) { y = CliffCore308.TerrainY(ground, ix * 4f + .02f, iz * 4f + .02f, out float ly) ? ly : float.NaN; lattice[cell] = y; }
                        if (float.IsNaN(y)) continue;
                        if (y > high) high = y; if (dist <= lim.deckRadius && y > near) near = y;
                    }
                float under = CliffCore308.TerrainY(ground, c.x, c.y, out float gc) ? gc : float.NaN;
                row.a1 = high - under; row.a1Top = high - (s.BaseCentre + lay.parapetTop); row.late16 = near - s.BaseCentre; row.aboveDeck = row.late16 > lay.moduleHeight;
                table.Add(row);
            }
            report.modules = table.ToArray();
            C(missingTop == 0, "every built module has its collider and a top under the probe ray (" + missingTop + " missing)");
            C(underMin == 0 && !float.IsInfinity(worstTop), "AC-B11a wall top - EA-side walkable ground >= " + F(lim.topOverEaMin, "F1") + " m on physics: min " + F(worstTop) + " m at " + worstTopAt + " (" + underMin + " modules under)");
            C(worstGrade <= lay.seat.gradeCap + .01f, "AC-B11a grade along the wall top <= " + F(lay.seat.gradeCap) + ": max " + F(worstGrade, "F3") + " at " + worstGradeAt);
            foreach (string wing in lay.modules.Select(m => m.wing).Distinct())
            {
                var mine = table.Where(r => r.id[0] == char.ToUpperInvariant(wing[0]) && !r.gateSpan && !float.IsNaN(r.a1) && !float.IsInfinity(r.a1)).ToList();
                var open = mine.Where(r => !plan.Of(r.id).M.buried).OrderBy(r => r.a1).ToList();
                if (open.Count == 0) continue;
                I("AC-B11d A1 [RECORDED EXCEPTION D308-9d] " + wing + ": min / median / max " + F(open[0].a1, "F1") + " / " + F(open[open.Count / 2].a1, "F1") + " / " + F(open[open.Count - 1].a1, "F1") + " m over " + open.Count
                    + " visible wing modules, " + open.Count(r => r.a1 > lim.a1Limit) + " over the " + F(lim.a1Limit, "F0") + " m limit; hill ground within " + F(lim.deckRadius, "F0") + " m above the deck on " + open.Count(r => r.aboveDeck) + " (per module: the JSON report)");
            }

            // ---- joints: nothing may pass between two modules, or between a module and the gate block
            int gapRays = 0, gaps = 0; var gapAt = new List<string>(); var gapAll = new List<string>();
            foreach (string wing in lay.modules.Select(m => m.wing).Distinct())
            {
                var built = plan.Modules.Where(s => s.M.wing == wing && !s.Hidden).ToList();
                for (int i = 0; i < built.Count; i++)
                    foreach (bool end in new[] { false, true })
                    {
                        if (end && i < built.Count - 1) continue;                    // the far end of the last built module runs into rock: probed by the capsule below
                        var s = built[i]; var j = end ? s.B : s.A; float baseY = end ? s.BaseB : s.BaseA;
                        foreach (float h in new[] { .3f, 1f, 1.6f, p.variants.Max(v => v.nominalRise) })
                        {
                            var a = new Vector3(j.x - s.Late.x * (hd + 3f), baseY + 1.5f + h, j.y - s.Late.y * (hd + 3f)); var b = new Vector3(j.x + s.Late.x * (hd + 3f), baseY + 1.5f + h, j.y + s.Late.y * (hd + 3f)); gapRays++;
                            var dir = b - a; float len = dir.magnitude;
                            bool hit = Physics.RaycastAll(a, dir / len, len, ~0, QueryTriggerInteraction.Ignore).Any(x => x.collider.transform.IsChildOf(root) || ground.Is(x.collider));
                            if (!hit) { gaps++; if (gapAt.Count < 8) gapAt.Add(s.M.id + (end ? ".b" : ".a") + " h " + F(h)); var gAlong = (s.B - s.A).normalized; var gOpen = new List<string>(); foreach (float gOff in new[] { -.28f, -.14f, -.05f, .05f, .14f, .28f }) { var gShift = new Vector3(gAlong.x * gOff, 0f, gAlong.y * gOff); if (!Physics.RaycastAll(a + gShift, dir / len, len, ~0, QueryTriggerInteraction.Ignore).Any(x => x.collider.transform.IsChildOf(root) || ground.Is(x.collider))) gOpen.Add(F(gOff)); } gapAll.Add(s.M.id + (end ? ".b" : ".a") + " h " + F(h) + " (" + F(j.x, "F1") + "," + F(j.y, "F1") + ") y " + F(baseY + 1.5f + h, "F1") + " | also open at along-wall offsets [" + string.Join(" ", gOpen) + "] of -0.28 -0.14 -0.05 0.05 0.14 0.28 m"); }
                        }
                    }
            }
            report.gaps = gaps; report.gapsAt = gapAll.ToArray();
            C(gaps == 0, "no gap between module colliders (" + gapRays + " rays across every joint at walk / jump / guk+jump heights)" + (gaps > 0 ? " - open: " + string.Join("; ", gapAt) : ""));

            // ---- capsule probes
            int stations = 0, deckRuns = 0;
            using (var shells = new ShellsOff(scene, lay.replaces, withShells, plan.Modules, lay.probes.shellJointRadius))
            using (var w = new CliffCore308.Walker(scene, cfg))
            {
                I("capsule = " + w.Source + ", speed " + F(w.Speed) + ", jump " + F(w.JumpHeight) + ", jump rule: " + (p.jumpNeedsWalkable ? "walkable support only (D308-9d)" : "any contact below")
                    + " | forest shells of [" + string.Join(", ", lay.replaces) + "]: " + (withShells ? "left on (:withshells)" : shells.Count + " colliders switched off for the probes (" + (shells.Count - shells.Joints) + " chunks of those runs + " + shells.Joints + " joint strips / plugs within "
                        + F(lay.probes.shellJointRadius, "F0") + " m of the wall), restored after"));
                ProbeRow Run(string at, string kind, CliffCore308.Variant variant, Vector2 origin, Vector2 normal, float[] backs, float push, float limit, Func<CliffCore308.RunResult, Vector2, string> judge)
                {
                    CliffCore308.RunResult run = null; float wallPast = float.NegativeInfinity;   // report only: furthest signed distance past the nearest module's centre line (hill side +)
                    float Side(Vector3 wf)
                    {
                        float wBest = float.PositiveInfinity, wSide = 0f; var wq = new Vector2(wf.x, wf.z);
                        foreach (var wm in plan.Modules) { var wab = wm.B - wm.A; float wt = Mathf.Clamp01(Vector2.Dot(wq - wm.A, wab) / wab.sqrMagnitude); var wc = wm.A + wab * wt; float wd = Vector2.Distance(wq, wc); if (wd < wBest) { wBest = wd; wSide = Vector2.Dot(wq - wc, wm.Late); } }
                        return wSide;
                    }
                    foreach (float back in backs)
                    {
                        var from = origin - normal * back; wallPast = float.NegativeInfinity;
                        run = CliffCore308.RunWalker(w, cfg, variant, ground, new Vector3(from.x, 0f, from.y), false, new[] { origin + normal * push }, f => { wallPast = Mathf.Max(wallPast, Side(f)); return (f.x - origin.x) * normal.x + (f.z - origin.y) * normal.y; }, 0f, p.climbSeconds);
                        if (run.Ground && run.Start == "") break;
                    }
                    string verdict = !run.Ground || run.Start != "" ? "INCONCLUSIVE" : judge != null ? judge(run, origin) : run.MaxPast > limit ? "LEAK" : "BLOCKED";
                    // stopped by a forest shell that is still on (a joint strip or a plug of a replaced run keeps its own name): the wall was not what held
                    if (verdict == "BLOCKED" && kind == "wall" && run.By.Contains("@Shell")) verdict = "FOREST";
                    var row = new ProbeRow { at = at, kind = kind, variant = variant.name, verdict = verdict, end = run.End + run.Start, by = run.By, x = origin.x, z = origin.y, maxPast = run.MaxPast, endY = run.Last.y, endX = run.Last.x, endZ = run.Last.z, startY = run.StartY, maxWallPast = float.IsInfinity(wallPast) ? 0f : wallPast, jumps = run.Jumps, lifts = run.Lifts };
                    rows.Add(row); return row;
                }
                void Tally(ProbeRow row) { if (row.verdict == "LEAK") report.leaks++; else if (row.verdict == "ON TOP") report.onTopFromEa++; else if (row.verdict == "INCONCLUSIVE" || row.verdict == "FOREST") report.inconclusive++; }
                string FromEa(CliffCore308.RunResult run, Vector2 origin, Seated s)
                {
                    if (run.MaxPast > lay.probes.leakPast) return "LEAK";
                    // on the wall = it started below the deck and came to rest at deck height within the wall's thickness (a start on cliff rock is not that)
                    return s != null && run.StartY < s.BaseCentre + lay.moduleHeight - 1f && run.Last.y >= s.BaseCentre + lay.moduleHeight - .5f && Mathf.Abs(run.MaxPast) <= hd + 1f ? "ON TOP" : "BLOCKED";
                }

                WorldSealGate308 probeGate = gateOk ? gate : null;
                using (probeGate != null ? new GateProbe(probeGate, false) : null)
                {
                    // (a) every `every` m along both wings, from the EA side through the wall
                    foreach (string wing in lay.modules.Select(m => m.wing).Distinct())
                    {
                        float arc = 0f, next = lay.probes.every * .5f;
                        foreach (var s in plan.Modules.Where(x => x.M.wing == wing))
                        {
                            while (next <= arc + s.M.length && stations < max)
                            {
                                var origin = Vector2.Lerp(s.A, s.B, (next - arc) / s.M.length); stations++;
                                foreach (var variant in p.variants) Tally(Run(s.M.id + " s=" + F(next, "F0"), "wall", variant, origin, s.Late, p.startBack, p.push, lay.probes.leakPast, (r, o) => FromEa(r, o, s.Hidden || s.M.buried ? null : s)));
                                next += lay.probes.every;
                            }
                            arc += s.M.length;
                        }
                    }
                    // (b) the named points: Spec section 6 lattice cells, wing ends, cliff joints, gate joints (three headings each)
                    foreach (var pt in lay.probes.points)
                    {
                        var q = new Vector2(pt.x, pt.z); Seated nearest = null; float best = float.PositiveInfinity; var foot = q;
                        foreach (var s in plan.Modules)
                        {
                            var ab = s.B - s.A; float t = Mathf.Clamp01(Vector2.Dot(q - s.A, ab) / ab.sqrMagnitude); var c = s.A + ab * t; float d = Vector2.Distance(q, c);
                            if (d < best) { best = d; nearest = s; foot = c; }
                        }
                        bool onWall = best <= 6f; var origin = onWall ? foot : q; float limit = onWall ? lay.probes.leakPast : p.failPast;
                        foreach (float turn in p.markTurns)
                        {
                            var n3 = Quaternion.Euler(0f, turn, 0f) * new Vector3(nearest.Late.x, 0f, nearest.Late.y); var normal = new Vector2(n3.x, n3.z);
                            foreach (var variant in p.variants)
                                Tally(Run(pt.id + (turn != 0f ? " " + turn.ToString("+0;-0", Inv) + " deg" : "") + (onWall ? "" : " (" + F(best, "F0") + " m off the wall line)"), onWall ? "wall" : "point", variant, origin, normal, p.startBack, p.push, limit, (r, o) => FromEa(r, o, onWall && !nearest.Hidden && !nearest.M.buried ? nearest : null)));
                        }
                    }
                    // (c) the closed gate: lanes through the passage from the EA side
                    if (probeGate != null)
                    {
                        var centre = V2(lay.gate.centre); var late = V2(lay.gate.late).normalized; var right = new Vector2(late.y, -late.x); int closedLeaks = 0, closedRuns = 0;
                        foreach (float lane in lay.probes.gateLanes)
                            foreach (var variant in p.variants)
                            {
                                var row = Run("gate closed lane " + lane.ToString("+0.0;-0.0", Inv), "gate", variant, centre + right * lane, late, new[] { 12f, 16f }, 12f, 0f, (r, o) => r.MaxPast > 0f ? "LEAK" : "BLOCKED");
                                closedRuns++; if (row.verdict == "LEAK") closedLeaks++; Tally(row);
                            }
                        C(closedLeaks == 0, "AC-B11e closed gate: " + closedRuns + " capsule runs (walk / jump / guk+jump) through the passage stop at the leaves" + (closedLeaks > 0 ? " - " + closedLeaks + " passed" : ""));
                    }
                }
                C(report.leaks == 0, "EA side -> hill side: " + rows.Count(r => r.kind != "deck" && r.kind != "gate open") + " capsule runs at " + stations + " stations every " + F(lay.probes.every, "F0") + " m + " + lay.probes.points.Length
                    + " named points, LEAK " + report.leaks + (report.leaks > 0 ? " - " + string.Join("; ", rows.Where(r => r.verdict == "LEAK").Take(6).Select(r => r.at + " " + r.variant + " past " + F(r.maxPast, "F1"))) : ""));
                C(report.onTopFromEa == 0, "nobody gets onto the wall from the EA side (ON TOP " + report.onTopFromEa + ")");
                if (report.inconclusive > 0) I(report.inconclusive + " runs not conclusive (INCONCLUSIVE = no terrain at the start or started inside a collider; FOREST = stopped by a forest shell that is still on, not by the wall): "
                    + string.Join("; ", rows.Where(r => r.verdict == "INCONCLUSIVE" || r.verdict == "FOREST").Take(8).Select(r => r.at + " " + r.variant + " " + r.verdict + " " + (r.verdict == "FOREST" ? r.by : r.end))));

                // (d) the open gate leaves the road clear
                if (probeGate != null)
                    using (new GateProbe(probeGate, true))
                    {
                        var centre = V2(lay.gate.centre); var late = V2(lay.gate.late).normalized; var right = new Vector2(late.y, -late.x); int blocked = 0, runs = 0; var walk = p.variants.First(v => !v.jump && v.guk <= 0f);
                        foreach (float lane in lay.probes.gateLanes)
                        {
                            var row = Run("gate open lane " + lane.ToString("+0.0;-0.0", Inv), "gate open", walk, centre + right * lane, late, new[] { 12f }, 12f, 0f, (r, o) => r.Reached || r.MaxPast > 10f ? "THROUGH" : "BLOCKED");
                            runs++; if (row.verdict != "THROUGH") blocked++;
                        }
                        float clear = lay.gate.leaves.clearHalfWidthOpen;
                        C(blocked == 0 && clear >= lay.probes.roadHalfWidth, "open gate: " + runs + " walking runs pass on the lanes " + string.Join(" / ", lay.probes.gateLanes.Select(x => F(x, "F1"))) + " m (blocked " + blocked + "); clear half width between the open leaves "
                            + F(clear) + " m >= road half width " + F(lay.probes.roadHalfWidth));
                    }

                // (e) AC-B11c deck access from the hill (state S2: the gate is open, 국 is known): start behind the wall, push toward the EA side
                foreach (var s in plan.Modules.Where(x => !x.Hidden && !x.M.buried))
                {
                    var rowM = table.FirstOrDefault(r => r.id == s.M.id);
                    if (rowM == null || !rowM.aboveDeck || deckRuns >= max * 3) continue;
                    foreach (var variant in p.variants)
                    {
                        var row = Run(s.M.id + " hill", "deck", variant, s.Centre, -s.Late, lay.probes.hillBack, p.push, lay.probes.leakPast, (r, o) =>
                            r.MaxPast > lay.probes.leakPast ? "CROSSED" : r.Last.y >= s.BaseCentre + lay.moduleHeight - .5f && Mathf.Abs((r.Last.x - o.x) * s.Late.x + (r.Last.z - o.y) * s.Late.y) <= hd + .6f ? "ON TOP" : "BLOCKED");
                        deckRuns++; if (row.verdict == "CROSSED") report.deckCrossed++; else if (row.verdict == "ON TOP") report.deckReached++;
                    }
                }
                foreach (var b in plan.Bastions.Where(x => !x.Hidden))
                {
                    var late = V2(b.B.late).normalized; var rect = lay.bastionKit.deckRect; var parts = lay.bastionKit.colliderParts[0]; var front = V2(b.B.at) + late * parts.b[3];
                    foreach (var variant in p.variants)
                    {
                        var row = Run(b.B.id + " hill", "deck", variant, front, -late, lay.probes.hillBack, parts.b[3] + 4f, parts.b[3] + lay.probes.leakPast, (r, o) =>
                        {
                            float z = (r.Last.x - b.B.at[0]) * late.x + (r.Last.z - b.B.at[1]) * late.y;
                            if (r.MaxPast > parts.b[3] + lay.probes.leakPast) return "CROSSED";
                            return r.Last.y >= b.BaseY + lay.bastionKit.deckY - .5f && z >= rect[1] - .5f && z <= rect[3] + 2f ? "ON DECK" : "BLOCKED";
                        });
                        if (row.verdict == "CROSSED") report.deckCrossed++; else if (row.verdict == "ON DECK") report.bastionDecksReached++;
                    }
                }
            }
            C(report.deckCrossed == 0 && report.deckReached == 0, "AC-B11c from the hill (S2): " + deckRuns + " runs on the modules whose hill ground within " + F(lim.deckRadius, "F0") + " m is above the deck (" + table.Count(r => r.aboveDeck)
                + " modules) + the bastions - ON TOP " + report.deckReached + ", CROSSED to the EA side " + report.deckCrossed + (report.deckCrossed + report.deckReached > 0
                    ? " (" + string.Join("; ", rows.Where(r => r.kind == "deck" && (r.verdict == "CROSSED" || r.verdict == "ON TOP")).Take(6).Select(r => r.at + " " + r.variant + " " + r.verdict)) + ") - build the hill-side rock slots there (layout rocks.build) or raise the profile" : ""));
            I("bastion decks reached from the hill: " + report.bastionDecksReached + " runs (a standable authored piece inside its parapets, state S2 only - list it in the AC-B10b ledger)");

            // ---- colliders inside the visible mass, naming
            var bare = rootGo.GetComponentsInChildren<Collider>(true).Where(c => c.GetComponent<Renderer>() == null && !c.name.EndsWith("_col", StringComparison.Ordinal) && !PrefabUtility.IsPartOfPrefabInstance(c)).Select(c => c.name).ToArray();
            C(bare.Length == 0, "renderer-less colliders are only *_col (" + bare.Length + " others" + (bare.Length > 0 ? ": " + string.Join(", ", bare.Take(6)) : "") + ")");
            var outside = new List<string>(); const float tol = .1f;
            foreach (var s in plan.Modules.Where(x => !x.Hidden))
            {
                if (!colliderOf.TryGetValue(s.M.id + "_col", out var mc) || mc.sharedMesh == null) continue;
                var b = mc.sharedMesh.bounds; float h = s.M.length * .5f, rise = Mathf.Abs(s.Grade) * h;
                if (b.min.x < -h - tol || b.max.x > h + tol || b.min.z < -hd - tol || b.max.z > hd + tol || b.max.y > lay.parapetTop + rise + tol || b.min.y < -lay.wallKit.colliderSink - rise - tol) outside.Add(mc.name);
            }
            var kit = lay.bastionKit; var kitBounds = new Bounds(); bool any = false;
            foreach (var part in kit.colliderParts)
                foreach (var corner in new[] { new Vector3(part.b[0], part.y0, part.b[1]), new Vector3(part.b[2], part.y1, part.b[3]) }) { if (!any) { kitBounds = new Bounds(corner, Vector3.zero); any = true; } else kitBounds.Encapsulate(corner); }
            foreach (var mc in rootGo.GetComponentsInChildren<MeshCollider>(true).Where(c => !c.convex && c.sharedMesh != null && c.transform.parent != null && c.transform.parent.name == "Colliders"))
                if (!Inside(mc.sharedMesh.bounds, kitBounds, tol)) outside.Add(mc.name);
            var gateT = root.Find("Gate"); var masonry = gateT != null ? gateT.Find("Masonry") : null; var masonryFilter = masonry != null ? masonry.GetComponent<MeshFilter>() : null;
            if (gateT == null || masonryFilter == null || masonryFilter.sharedMesh == null) outside.Add("Gate/Masonry missing");
            else
            {
                var visible = masonryFilter.sharedMesh.bounds;
                foreach (var box in gateT.GetComponents<BoxCollider>().Concat(gateT.Cast<Transform>().SelectMany(t => t.GetComponents<BoxCollider>())).Where(x => x.transform.parent == gateT))
                    if (!Inside(new Bounds(box.center, box.size), visible, tol)) outside.Add(box.name);
                if (gateOk)
                {
                    var leavesT = gate.transform; var lf = gate.LeftLeaf.GetComponentInChildren<MeshFilter>(true); var rf = gate.RightLeaf.GetComponentInChildren<MeshFilter>(true);
                    if (lf != null && rf != null)
                    {
                        var both = FrameBounds(leavesT, lf); both.Encapsulate(FrameBounds(leavesT, rf));
                        foreach (var c in gate.ClosedBlockers.OfType<BoxCollider>()) if (!Inside(new Bounds(c.center, c.size), both, tol)) outside.Add(c.name);
                        foreach (var (leaf, filter) in new[] { (gate.LeftLeaf, lf), (gate.RightLeaf, rf) })
                            foreach (var c in gate.OpenColliders.OfType<BoxCollider>().Where(x => x.transform.parent == leaf)) if (!Inside(new Bounds(c.center, c.size), FrameBounds(leaf, filter), tol)) outside.Add(c.name);
                    }
                    else outside.Add("leaf meshes missing");
                }
            }
            C(outside.Count == 0, "AC-B10 every collider lies inside its visible mass (module profile box, bastion kit, gate masonry, leaves; " + F(tol) + " m)" + (outside.Count > 0 ? " - outside: " + string.Join(", ", outside.Take(8)) : ""));

            // ---- AC-B17: nothing glows, lights or prompts; AC-B22: pack geometry only by reference or under the git-ignored derived folders
            C(rootGo.GetComponentsInChildren<Light>(true).Length == 0, "AC-B17 no light under " + lay.root);
            var glowing = rootGo.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(Glows).Select(m => m.name).Distinct().ToArray();
            C(glowing.Length == 0, "AC-B17 no emissive material" + (glowing.Length > 0 ? " (" + string.Join(", ", glowing) + ")" : ""));
            var prompts = rootGo.GetComponentsInChildren<Component>(true).Where(c => c != null && (c is WorldMacroContentPoint || c is ParticleSystem || c is AudioSource || c.GetType().Name == "Canvas" || c.GetType().Name.StartsWith("TextMeshPro", StringComparison.Ordinal))).ToArray();
            C(prompts.Length == 0, "AC-B17 no interaction point, prompt, text, particle or sound under " + lay.root + " (" + prompts.Length + ")");
            string packRoot = lay.packPrefabDir.Substring(0, lay.packPrefabDir.TrimEnd('/').LastIndexOf('/') + 1); int inline = 0, stray = 0; var strays = new List<string>();
            var rockDirs = lay.rocks.slots.Select(s => s.path.Substring(0, s.path.IndexOf('/', "Assets/".Length) + 1)).Distinct().ToArray();
            void Asset(UnityEngine.Object o)
            {
                if (o == null) return;
                if (!EditorUtility.IsPersistent(o)) { inline++; return; }
                string ap = AssetDatabase.GetAssetPath(o);
                if (!ap.StartsWith(cfg.assetRoot + "/", StringComparison.Ordinal) && !ap.StartsWith(packRoot, StringComparison.Ordinal) && !rockDirs.Any(d => ap.StartsWith(d, StringComparison.Ordinal))) { stray++; if (strays.Count < 6) strays.Add(ap); }
            }
            foreach (var f in rootGo.GetComponentsInChildren<MeshFilter>(true)) Asset(f.sharedMesh);
            foreach (var mcol in rootGo.GetComponentsInChildren<MeshCollider>(true)) Asset(mcol.sharedMesh);
            foreach (var m in rootGo.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct()) Asset(m);
            C(inline == 0 && stray == 0, "AC-B22 every mesh and material under " + lay.root + " is an asset under " + cfg.assetRoot + " (git-ignored, derived) or the owned pack itself (by GUID): in-scene objects " + inline + ", elsewhere " + stray
                + (strays.Count > 0 ? " (" + string.Join(", ", strays) + ")" : ""));

            // ---- counts against the wall's share of the #307 budget
            var counts = Measure(rootGo, cfg.assetRoot + MeshSub); counts.modules = lay.modules.Length; counts.bastions = plan.Bastions.Count(x => !x.Hidden); report.counts = counts; var bd = lay.counts.budget;
            C(counts.triangles <= bd.triangles && counts.batches <= bd.batches && counts.colliders <= bd.colliders && counts.meshMemoryMB <= bd.meshMemoryMB, "AC-B15 share: triangles " + counts.triangles + " <= " + bd.triangles + ", batches " + counts.batches + " <= " + bd.batches
                + ", colliders " + counts.colliders + " <= " + bd.colliders + ", derived mesh memory " + F(counts.meshMemoryMB) + " MB <= " + F(bd.meshMemoryMB, "F1") + "; renderers " + counts.renderers + ", shadow casters " + counts.shadowCasters + " (no LOD in the pack: LOD0 at every distance)");

            // ---- save relocation (AC-B20) and the encounter distance
            var profile = session != null ? session.SealProfile : null;
            C(profile != null && AssetDatabase.GetAssetPath(profile) == cfg.assetRoot + ProfileSub && profile.IsConfigured && profile.RequiredFact == WorldMacroPlaytestSession.SouthGateOpenedId,
                "AC-B20 the session links WorldSealProfile308 (" + (profile == null ? "none" : AssetDatabase.GetAssetPath(profile) + ", rings " + profile.BeyondRings.Length + ", EA rests " + profile.EaRestIds.Length) + ")");
            if (profile != null)
            {
                var centre = V2(lay.gate.centre); var late = V2(lay.gate.late).normalized; var beyond = centre + late * 30f; var ea = centre - late * 30f;
                bool b1 = WorldSealRules308.InsideRings(profile, new Vector3(beyond.x, 0f, beyond.y)), b2 = WorldSealRules308.InsideRings(profile, new Vector3(ea.x, 0f, ea.y));
                C(b1 && !b2, "AC-B20 the rings follow the wall: 30 m behind the gate is beyond (" + b1 + "), 30 m in front of it is not (" + b2 + ")");
                C(profile.SourceHash == CliffCore308.ShaFile(PostLedger308.RepoPath(BeyondFile)), "AC-B20 the profile was made from the present " + BeyondFile);
                var content = session.Content;
                if (content != null)
                {
                    var progress = WorldMacroProgress.CreateNew(content.TerrainRevision, content.StartFeet, content.StartYaw);
                    int eaBad = profile.EaRestIds.Count(id => WorldMacroCheckpointRules.TryResolve(content, progress, id, out var cp) && WorldSealRules308.InsideRings(profile, cp.Feet));
                    C(eaBad == 0, "AC-B20 no EA rest lies beyond the wall (" + eaBad + ")");
                    C(profile.FallbackRestIds.Any(id => WorldMacroCheckpointRules.TryResolve(content, progress, id, out _)), "AC-B20 a fallback rest resolves [" + string.Join(",", profile.FallbackRestIds) + "]");
                }
            }
            var agwi = new Vector2(lim.agwiX, lim.agwiZ); float agwiDist = plan.Modules.Min(s => Vector2.Distance(agwi, s.Centre)) - lay.moduleLength * .5f;
            C(agwiDist >= lim.agwiMin, "the encounter agwi (" + F(lim.agwiX, "F0") + ", " + F(lim.agwiZ, "F0") + ") stays " + F(agwiDist, "F0") + " m from the wall (>= " + F(lim.agwiMin, "F0") + ")");
            I("NOT measured here: NavMesh closed / open (bake + Play: carving is runtime only), the fact in Play, the save relocation at load, the look, the GPU cost");

            bool shellsOk = report.leaks == 0 && report.onTopFromEa == 0 && report.inconclusive == 0 && gaps == 0 && underMin == 0 && missingTop == 0 && !float.IsInfinity(worstTop) && gateOk && stale.Count == 0 && report.buildKey == plan.Key && !withShells;
            var why = new List<string>();
            if (report.leaks > 0) why.Add("LEAK " + report.leaks); if (report.onTopFromEa > 0) why.Add("ON TOP from the EA side " + report.onTopFromEa); if (report.inconclusive > 0) why.Add("not conclusive " + report.inconclusive + " (INCONCLUSIVE / FOREST rows)");
            if (gaps > 0) why.Add("joint gaps " + gaps); if (underMin > 0 || missingTop > 0 || float.IsInfinity(worstTop)) why.Add("wall top under the EA floor / not measured"); if (!gateOk) why.Add("gate not wired");
            if (stale.Count > 0) why.Add("stale layout"); if (report.buildKey != plan.Key) why.Add("the seat changed since apply"); if (withShells) why.Add("probed with the shells on (:withshells): run check without it");
            return Finish(shellsOk ? "yes (EA-side probes, joints, gate and top heights hold without the shells of " + string.Join(", ", lay.replaces) + ")" : "no (" + string.Join("; ", why) + ")");
        }
    }
}
