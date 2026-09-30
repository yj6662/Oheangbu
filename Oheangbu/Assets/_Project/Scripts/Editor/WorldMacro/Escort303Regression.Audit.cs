using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // E0 static audit (plan §10.1), read-only in the open scene (Edit mode): A1 stop ↔ content, A2 node support,
    // A3 vehicle corridor vs Roadside303 colliders, A4 companion walks at each stop (presenter capsule sweep), A5 walk-by
    // corridors past the #303 wrecks, A6 front summon probe. Causes: "#303" when a Roadside303/… collider is involved,
    // "pre-existing" otherwise. Gate GE0 = every A1 and A2 row passes.
    public static partial class Escort303Regression
    {
        [Serializable] internal sealed class AuditRow { public string id = "", target = "", status = "", cause = "", detail = ""; }
        [Serializable] internal sealed class AuditReport
        {
            public string scene = "", sceneSha = "", utc = ""; public bool gateGE0;
            public int pass, fail, skip;
            public List<AuditRow> rows = new List<AuditRow>();
            public bool escortStartShiftMismatch; public Vector3 escortStartRoot, escortStartExpectedRoot;
        }

        static string Audit(string target)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only";
            string path = target == "main" ? Harness303.MainScene : target == "folklore298" ? Harness303.FolkloreScene : target == "architecture296" ? Harness303.CandidateScene : null;
            if (path == null) return "refused: target must be main, folklore298 or architecture296";
            var scene = SceneManager.GetActiveScene();
            if (scene.path != path) return "refused: open " + path + " first (active " + scene.path + ")";
            var s = Harness303.Session;
            if (s == null) return "refused: no single playtest session";
            var route = Route(scene);
            var r = new AuditReport { scene = path, sceneSha = Harness303.Sha(Harness303.Abs(path)), utc = DateTime.UtcNow.ToString("O") };
            void Row(string id, string tgt, string status, string cause, string detail) => r.rows.Add(new AuditRow { id = id, target = tgt, status = status, cause = cause, detail = detail });
            if (route == null) { Row("A1", RouteRoot, "FAIL", "pre-existing", "no DemoEscortSceneRoute"); return Finish(r, target); }
            Physics.SyncTransforms();
            var ignore = Ignorer(s);
            var roadside = RoadsideColliders(scene);
            // A1 -----------------------------------------------------------------------------------------------
            Row("A1", "stops", route.Stops.Length == 4 && StopIds.All(id => route.Stops.Count(x => x != null && x.Id == id) == 1) ? "PASS" : "FAIL", "", "stops " + string.Join(",", route.Stops.Where(x => x != null).Select(x => x.Id)));
            foreach (var stop in route.Stops.Where(x => x != null))
            {
                var point = s.Content.Points.FirstOrDefault(p => p.Id == stop.Id);
                var check = s.Content.Checkpoints.FirstOrDefault(c => c.Id == stop.CheckpointId);
                float di = point != null && stop.Interaction != null ? Vector3.Distance(point.Position, stop.Interaction.position) : float.MaxValue;
                float dc = check != null && stop.Checkpoint != null ? Vector3.Distance(check.Feet, stop.Checkpoint.position) : float.MaxValue;
                string cause = "";
                if (di > .01f && point != null && stop.Interaction != null)
                {
                    var delta = point.Position - stop.Interaction.position;
                    bool shift = Vector3.Distance(delta, VillageShift) < .05f;
                    cause = shift ? "pre-existing (#292 village shift (+60, +173.766, -40) not applied to the renderer-less stop root)" : "pre-existing";
                    if (stop.Id == "escort_start")
                    {
                        r.escortStartShiftMismatch = shift;
                        var root = stop.Interaction.parent;
                        r.escortStartRoot = root != null ? root.position : stop.Interaction.position;
                        r.escortStartExpectedRoot = r.escortStartRoot + delta;
                    }
                }
                Row("A1", stop.Id, di <= .01f && dc <= .01f ? "PASS" : "FAIL", cause,
                    "interaction " + (stop.Interaction != null ? Harness303.V(stop.Interaction.position) : "missing") + " vs content " + (point != null ? Harness303.V(point.Position) : "missing") + " Δ " + Harness303.F(di) +
                    "; checkpoint " + (stop.Checkpoint != null ? Harness303.V(stop.Checkpoint.position) : "missing") + " vs " + (check != null ? Harness303.V(check.Feet) : "missing") + " Δ " + Harness303.F(dc));
            }
            var departure = s.MountainLayout != null ? s.MountainLayout.Places.FirstOrDefault(p => p.Id == "escort_departure") : null;
            var start = Stop(route, "escort_start");
            if (departure != null && start?.Interaction != null)
                Row("A1", "escort_departure", Harness303.Flat(start.Interaction.position, new Vector3(departure.XZ.x, 0, departure.XZ.y)) < 12f ? "PASS" : "FAIL", "pre-existing",
                    "layout escort_departure (" + departure.XZ.x + ", " + departure.XZ.y + ") vs stop " + Harness303.V(start.Interaction.position));
            // A2 -----------------------------------------------------------------------------------------------
            foreach (var stop in route.Stops.Where(x => x != null))
                foreach (var (node, walk) in new[] { (stop.Parking, false), (stop.CompanionWait, true), (stop.CargoWait, true), (stop.Checkpoint, true), (stop.StagingApproach, true) })
                {
                    if (node == null) continue;
                    bool support = Physics.RaycastAll(node.position + Vector3.up * .5f, Vector3.down, .8f, ~0, QueryTriggerInteraction.Ignore).Any(h => !ignore(h.collider) && Mathf.Abs(h.point.y - node.position.y) <= .3f);
                    bool nav = !walk || NavMesh.SamplePosition(node.position, out var hit, .5f, NavMesh.AllAreas) && Vector3.Distance(hit.position, node.position) <= .5f;
                    Row("A2", stop.Id + "/" + node.name, support && nav ? "PASS" : "FAIL", "pre-existing", "at " + Harness303.V(node.position) + " support " + support + (walk ? " navmesh≤0.5 " + nav : " (vehicle node)"));
                }
            // A3 -----------------------------------------------------------------------------------------------
            var hull = s.DemoEscortSeat != null && s.DemoEscortSeat.Vehicle != null ? s.DemoEscortSeat.Vehicle.Hull : null;
            Vector3 size = hull != null ? Vector3.Scale(hull.size, hull.transform.lossyScale) : new Vector3(2.2f, 1.6f, 4f);
            float halfWidth = size.x * .5f + .4f;
            for (int i = 0; i < LegTable.Length; i++)
            {
                if (!TryLeg(s, route, i, out var leg, out var why)) { Row("A3", LegTable[i].id, "SKIP", "", why); continue; }
                float min = float.MaxValue; string nearest = ""; var hits = new HashSet<string>(); int samples = 0;
                foreach (var (p, forward) in Samples(leg.points, 2f))
                {
                    samples++;
                    var rot = Quaternion.LookRotation(forward.sqrMagnitude > 1e-4f ? forward : Vector3.forward);
                    foreach (var c in Physics.OverlapBox(p + Vector3.up * (size.y * .5f + .2f), new Vector3(halfWidth, size.y * .5f, 1f), rot, ~0, QueryTriggerInteraction.Ignore))
                        if (UnderRoadside(c.transform)) hits.Add(Harness303.PathOf(c.transform));
                    float d = Clearance(roadside, p, halfWidth, out var near);
                    if (d < min) { min = d; nearest = near != null ? Harness303.PathOf(near.transform) : ""; }
                }
                bool ok = hits.Count == 0 && min >= .5f;
                Row("A3", leg.id, ok ? "PASS" : "FAIL", ok ? "" : "#303", samples + " samples every 2 m, vehicle half width " + Harness303.F(halfWidth, "F2") + " m; min clearance to Roadside303 " + (min < float.MaxValue ? Harness303.F(min, "F2") : "n/a") + " m (" + nearest + ")" + (hits.Count > 0 ? "; overlaps " + string.Join(", ", hits.Take(6)) : ""));
            }
            // A4 -----------------------------------------------------------------------------------------------
            foreach (var stop in route.Stops.Where(x => x != null))
            {
                var walks = new List<(string label, Vector3 a, Vector3 b)>();
                if (stop.Parking != null && stop.Interaction != null)
                {
                    var right = Vector3.ProjectOnPlane(stop.Parking.right, Vector3.up).normalized;
                    var exit = new[] { stop.Parking.position + right * 2.5f, stop.Parking.position - right * 2.5f }.FirstOrDefault(p => NavMesh.SamplePosition(p, out _, 1f, NavMesh.AllAreas));
                    if (exit != default) walks.Add(("exit→interaction", exit, stop.Interaction.position));
                    if (stop.CompanionWait != null) { walks.Add(("exit→companion wait", exit != default ? exit : stop.Parking.position, stop.CompanionWait.position)); walks.Add(("companion wait→exit", stop.CompanionWait.position, exit != default ? exit : stop.Parking.position)); }
                }
                if (stop.StagingApproach != null && stop.CompanionWait != null) walks.Add(("staging approach→companion wait", stop.StagingApproach.position, stop.CompanionWait.position));
                if (stop.CargoWait != null && stop.CompanionWait != null) walks.Add(("cargo→companion wait", stop.CargoWait.position, stop.CompanionWait.position));
                foreach (var w in walks)
                {
                    if (!Walker303.TryPath(w.a, w.b, out var corners, out var reason)) { Row("A4", stop.Id + " " + w.label, "FAIL", "pre-existing", "no NavMesh path: " + reason); continue; }
                    Collider block = null; Vector3 at = default;
                    for (int i = 1; i < corners.Length && block == null; i++)
                    {
                        int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(corners[i - 1], corners[i]) / .5f));
                        for (int j = 0; j < n && block == null; j++) block = Sweep(Vector3.Lerp(corners[i - 1], corners[i], j / (float)n), Vector3.Lerp(corners[i - 1], corners[i], (j + 1) / (float)n), ignore, out at);
                    }
                    Row("A4", stop.Id + " " + w.label, block == null ? "PASS" : "FAIL", block == null ? "" : Cause(block),
                        "path " + Harness303.F(Walker303.Length(corners), "F1") + " m" + (block != null ? "; blocked by " + Harness303.PathOf(block.transform) + " at " + Harness303.V(at) : ""));
                }
            }
            // A5 -----------------------------------------------------------------------------------------------
            var legs = new List<Leg>();
            for (int i = 0; i < LegTable.Length; i++) if (TryLeg(s, route, i, out var leg, out _)) legs.Add(leg);
            var rootRoadside = Harness303.Find(scene, Roadside);
            foreach (var name in new[] { "inspection_seized303", "inspection_burnt303", "hamlet_home303", "hamlet_empty303", "hamlet_ruin303", "hamlet_cart303", "hamlet_elder303" })
            {
                var t = rootRoadside != null ? rootRoadside.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name) : null;
                if (t == null) { Row("A5", name, "SKIP", "", "not in Roadside303"); continue; }
                if (!WalkBy(s, legs, t.position, ignore, roadside, out var detail, out var blockCause)) Row("A5", name, "FAIL", blockCause, detail);
                else Row("A5", name, "PASS", "", detail);
            }
            // A6 -----------------------------------------------------------------------------------------------
            foreach (var stop in route.Stops.Where(x => x != null && x.Parking != null))
            {
                try
                {
                    var call = s.DemoEscortSummon;
                    var forward = Vector3.ProjectOnPlane(stop.Parking.forward, Vector3.up).normalized;
                    var feet = stop.Parking.position - forward * call.MinimumPlayerDistance;
                    if (s.TrySafeFeet(feet, out var safe)) feet = safe;
                    bool ok = call.TryFindPlacement(feet, forward, out var pose, out var reason);
                    Row("A6", stop.Id, ok ? "PASS" : "FAIL", ok ? "" : "pre-existing", ok ? "placement " + Harness303.V(pose.Position) + " (" + Harness303.F(Vector3.Distance(pose.Position, stop.Parking.position), "F2") + " m from parking)" : reason + " / " + call.LastPlacementDiagnostic);
                }
                catch (Exception e) { Row("A6", stop.Id, "SKIP", "", "probe threw in Edit mode: " + e.GetType().Name + " " + e.Message); }
            }
            return Finish(r, target);
        }

        static string Finish(AuditReport r, string target)
        {
            r.pass = r.rows.Count(x => x.status == "PASS"); r.fail = r.rows.Count(x => x.status == "FAIL"); r.skip = r.rows.Count(x => x.status == "SKIP");
            r.gateGE0 = r.rows.Any(x => x.id == "A1") && r.rows.Where(x => x.id == "A1" || x.id == "A2").All(x => x.status == "PASS");
            Directory.CreateDirectory(Folder);
            Harness303.WriteJson(Folder, "escort-audit-" + Path.GetFileNameWithoutExtension(r.scene) + ".json", r);
            if (target == "main") Harness303.WriteJson(Folder, "escort-audit.json", r);
            return "audit " + r.scene + ": " + r.pass + " pass / " + r.fail + " fail / " + r.skip + " skip, GE0 " + (r.gateGE0 ? "OPEN" : "CLOSED") +
                   (r.escortStartShiftMismatch ? " — escort_start carries the #292 village shift (F1 applies)" : "") + "\n" +
                   string.Join("\n", r.rows.Where(x => x.status != "PASS").Take(30).Select(x => x.status + " " + x.id + " " + x.target + " [" + x.cause + "] " + x.detail));
        }

        internal static IEnumerable<(Vector3 p, Vector3 forward)> Samples(Vector3[] pts, float step)
        {
            for (int i = 1; i < pts.Length; i++)
            {
                var a = pts[i - 1]; var b = pts[i]; var f = Vector3.ProjectOnPlane(b - a, Vector3.up).normalized;
                int n = Mathf.Max(1, Mathf.CeilToInt(Harness303.Flat(a, b) / step));
                for (int j = 0; j < n; j++)
                {
                    var q = Vector3.Lerp(a, b, j / (float)n);
                    if (Harness303.Ground(q, out var hit)) q = hit.point;
                    yield return (q, f);
                }
            }
        }

        /// <summary>Follower points (road centre − forward × 2.2 ± right × 1.5) along ±30 m of road nearest `site`, swept pairwise.</summary>
        internal static bool WalkBy(WorldMacroPlaytestSession s, List<Leg> legs, Vector3 site, Func<Collider, bool> ignore, List<Collider> roadside, out string detail, out string cause)
        {
            detail = ""; cause = "";
            Leg bestLeg = null; int bestIndex = -1; float bestD = float.MaxValue;
            foreach (var leg in legs) for (int i = 0; i < leg.points.Length; i++) { float d = Harness303.Flat(leg.points[i], site); if (d < bestD) { bestD = d; bestLeg = leg; bestIndex = i; } }
            if (bestLeg == null) { detail = "no leg"; return true; }
            var centre = Samples(bestLeg.points, 1f).Where(x => Harness303.Flat(x.p, site) <= 30f + bestD).ToList();
            if (centre.Count < 2) { detail = "road not within reach (" + Harness303.F(bestD, "F1") + " m)"; return true; }
            float min = float.MaxValue; string nearest = "";
            foreach (float side in new[] { -1.5f, 1.5f })
            {
                Vector3? prev = null;
                foreach (var (p, f) in centre)
                {
                    var right = Vector3.Cross(Vector3.up, f).normalized;
                    var q = p - f * 2.2f + right * side;
                    if (Harness303.Ground(q, out var hit)) q = hit.point;
                    float d = Clearance(roadside, q, .27f, out var near);
                    if (d < min) { min = d; nearest = near != null ? Harness303.PathOf(near.transform) : ""; }
                    if (prev.HasValue)
                    {
                        var block = Sweep(prev.Value, q, ignore, out var at);
                        if (block != null && UnderRoadside(block.transform)) { cause = "#303"; detail = "lane " + side + " blocked by " + Harness303.PathOf(block.transform) + " at " + Harness303.V(at) + " (road " + Harness303.F(bestD, "F1") + " m from site)"; return false; }
                    }
                    prev = q;
                }
            }
            detail = "road " + Harness303.F(bestD, "F1") + " m from site on " + bestLeg.id + ", min follower clearance " + (min < float.MaxValue ? Harness303.F(min, "F2") : "n/a") + " m (" + nearest + ")";
            return true;
        }
    }
}
