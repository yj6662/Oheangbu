using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    // (e) delivery stop (D308-22 answer 1, #308 relayout fix 3): "C1 집을 평지 끝으로 옮김". The guesthouse of the cargo delivery
    // stop stood on a 22 degree slope 3.5 m under the packed road end. This op moves it (ONE transform: its 15 MeshCollider parts and
    // its StoneSkirt308 child ride along) onto the rim of that flat so that the plinth top = the packed surface, and re-places the
    // person nodes of the stop on the flat in front of it. No terrain change, no new mesh, no new object, no Light, no text.
    //   delivery:plan:<scene> | delivery:apply:<scene> | delivery:verify:<scene> | delivery:revert:<scene>[:force]
    // Data: Art/World/Compact/Rebuild/CliffBoundary308/small_1b/delivery308.json (XZ, yaw and every limit; Y is always measured).
    // One ledger per scene, Out/cb308-small-delivery-<scene>.json, written BEFORE the saves ("applying") and flipped after them:
    //   1. the house root -> (x, packed surface - plinth top, z), yaw from the data; refused unless it stands on expect_before
    //   2. each listed node -> its XZ, Y = the physical ground under it (+ lift), yaw when the row names one
    //   3. the scene's own content asset: Points[cargo_delivery].Position = Interaction, Points[capital_escort_rest].Position and
    //      Checkpoints[capital_escort_rest].Feet / Yaw = Checkpoint (escort audit A1 compares them within 0.01 m)
    // keep[] (Parking) and the stop root are never touched. Colliders move on walkable ground: apply is BLOCKED unless the data says
    // moves_colliders true AND carries a bake_decision; the NavMesh of the last bake is stale at the old and the new place until
    // the one bake that follows (walk nodes report WAIT in verify till then).
    // After apply: SmallOps308 stops:verify (the F5 seat still holds: same ground rule, same lift), Content308 campaign-apply (stage
    // "delivery" Destination follows the point), then the bake. stops:revert of this scene needs delivery:revert first (XZ moved).
    public static partial class SmallOps308
    {
        const string DeliveryData = Pieces308.DataDir + "/delivery308.json";

        [Serializable] sealed class DelNode { public string node = ""; public Vector3 localPosBefore, worldAfter; public Quaternion localRotBefore, worldRotAfter; public float ground, field; public string support = ""; }
        [Serializable] sealed class DelLedger
        {
            public string format = "cb308.small.delivery.1", scene = "", key = "", state = "none", utc = "", dataSha256 = "", variant = "", bakeDecision = "", content = "", contentShaBefore = "", contentShaAfter = "", sceneShaBefore = "", sceneShaAfter = "", backup = "";
            public string point = "", restPoint = "", checkpoint = "";
            public Vector3 pointAfter, restPointAfter, feetAfter; public float yawAfter, padLevel;
            public int[] beforeBits = new int[0];   // point xyz, rest point xyz, feet xyz, yaw - the exact floats of the content asset before apply
            public List<DelNode> nodes = new List<DelNode>(); public string[] history = new string[0];
        }

        sealed class DelPlan
        {
            public Scene Scene; public WorldMacroPlaytestSO Content; public string ContentPath = "", Variant = "";
            public Transform Root, House; public Vector3 HousePos; public Quaternion HouseRot; public float Pad, HouseYaw; public bool HouseAtTarget, Change;
            public PrologueContentSO.Point Point, RestPoint; public WorldMacroPlaytestSO.CheckpointSpec Check;
            public readonly List<(Transform t, JToken row, Vector3 pos, Quaternion rot, float ground, float field, string support)> Nodes = new List<(Transform, JToken, Vector3, Quaternion, float, float, string)>();
            public readonly List<string> Lines = new List<string>(), Fail = new List<string>(), Refuse = new List<string>();
            public void Row(bool ok, string text) { Lines.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) Fail.Add(text); }
        }

        static Transform DelChild(Transform parent, string name, string what)
        {
            Transform hit = null; int n = 0;
            foreach (Transform c in parent) if (c.name == name) { hit = c; n++; }
            if (n != 1) throw new PostLedger308.Refused(what + ": " + n + " direct children named '" + name + "' under " + PostLedger308.PathOf(parent) + " (expected 1; a name lookup would take the first)");
            return hit;
        }
        static Vector2 DelLocalToWorld(Vector3 pivot, float yawDeg, float lx, float lz)
        {
            float c = Mathf.Cos(yawDeg * Mathf.Deg2Rad), s = Mathf.Sin(yawDeg * Mathf.Deg2Rad);
            return new Vector2(pivot.x + lx * c + lz * s, pivot.z - lx * s + lz * c);
        }
        static Vector2[] DelRect(Vector3 pivot, float yawDeg, JToken r)
            => new[] { DelLocalToWorld(pivot, yawDeg, Pieces308.At(r, 0), Pieces308.At(r, 2)), DelLocalToWorld(pivot, yawDeg, Pieces308.At(r, 1), Pieces308.At(r, 2)), DelLocalToWorld(pivot, yawDeg, Pieces308.At(r, 1), Pieces308.At(r, 3)), DelLocalToWorld(pivot, yawDeg, Pieces308.At(r, 0), Pieces308.At(r, 3)) };
        static float DelSegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var d = b - a; float l2 = d.sqrMagnitude; float t = l2 < 1e-9f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, d) / l2);
            return Vector2.Distance(p, a + d * t);
        }
        static bool DelInside(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < poly[i].x + (p.y - poly[i].y) * (poly[j].x - poly[i].x) / (poly[j].y - poly[i].y)) inside = !inside;
            return inside;
        }
        static bool DelCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float C(Vector2 p, Vector2 q, Vector2 r) => (r.y - p.y) * (q.x - p.x) - (q.y - p.y) * (r.x - p.x);
            return (C(a, c, d) > 0) != (C(b, c, d) > 0) && (C(a, b, c) > 0) != (C(a, b, d) > 0);
        }
        // smallest distance between a polygon (outline or inside) and a polyline
        static float DelPolyLine(Vector2[] poly, Vector2[] line)
        {
            float best = float.PositiveInfinity;
            foreach (var p in line) if (DelInside(p, poly)) return 0f;
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
                for (int j = 0; j + 1 < line.Length; j++)
                {
                    if (DelCross(a, b, line[j], line[j + 1])) return 0f;
                    best = Mathf.Min(best, Mathf.Min(Mathf.Min(DelSegDist(a, line[j], line[j + 1]), DelSegDist(b, line[j], line[j + 1])), Mathf.Min(DelSegDist(line[j], a, b), DelSegDist(line[j + 1], a, b))));
                }
            }
            return best;
        }
        static float DelPolyPoint(Vector2[] poly, Vector2 p)
        {
            if (DelInside(p, poly)) return 0f; float best = float.PositiveInfinity;
            for (int i = 0; i < poly.Length; i++) best = Mathf.Min(best, DelSegDist(p, poly[i], poly[(i + 1) % poly.Length]));
            return best;
        }
        static float DelYaw(Quaternion q) => Mathf.Repeat(q.eulerAngles.y, 360f);
        static string DelV2(Vector2 v) => "(" + Pieces308.F(v.x) + ", " + Pieces308.F(v.y) + ")";

        static DelPlan DeliveryMeasure(JObject ops, JObject d, string scenePath, string key, bool write)
        {
            var k = new DelPlan(); var house = Pieces308.Req(d, "house"); var rules = Pieces308.Req(d, "rules");
            k.ContentPath = Pieces308.Str(Pieces308.Req(Pieces308.Req(ops, "escort"), "content"), key);
            if (PostLedger308.IsProtectedPath(k.ContentPath)) throw new PostLedger308.Refused("protected content path " + k.ContentPath);
            k.Scene = Pieces308.OpenTarget(scenePath, ops["preview_roots"], write);
            var session = CliffCore308.Session(k.Scene); if (session == null) throw new PostLedger308.Refused("no playtest session in " + scenePath);
            k.Content = session.Content;
            if (k.Content == null || AssetDatabase.GetAssetPath(k.Content) != k.ContentPath) throw new PostLedger308.Refused("the session of " + scenePath + " uses " + (k.Content == null ? "no content" : AssetDatabase.GetAssetPath(k.Content)) + ", the data names " + k.ContentPath);
            if (session.MountainLayout == null || session.MountainLayout.FinalSurface == null) throw new PostLedger308.Refused("the session of " + scenePath + " has no layout height field (the witness against a roof / a crate is missing)");
            CompactWorldSurface field;
            try { field = new CompactWorldSurface(session.MountainLayout); } catch (Exception ex) { throw new PostLedger308.Refused("the layout height field does not load: " + ex.Message); }
            k.Root = Pieces308.FindOne(k.Scene, Pieces308.Str(d, "stop"), "delivery stop");
            if (PostLedger308.UnderProtectedTree(k.Root)) throw new PostLedger308.Refused("the stop stands under a protected tree");
            k.House = DelChild(k.Root, Pieces308.Str(house, "node"), "house");
            k.Variant = Pieces308.Str(house, "variant"); var v = Pieces308.Req(Pieces308.Req(house, "variants"), k.Variant);
            if (Pieces308.Opt(v, "blocked").Length > 0) throw new PostLedger308.Refused("house variant '" + k.Variant + "' is blocked in the data: " + Pieces308.Opt(v, "blocked"));
            float poseTol = Pieces308.Num(rules, "pose_tol_m"), angTol = Pieces308.Num(rules, "angle_tol_deg"), window = Pieces308.Num(rules, "support_window_m"), expectTol = Pieces308.Num(rules, "expect_tol_m");
            var rule = new Pieces308.GroundRule(k.Scene, k.Root);

            // ---- the packed surface (measured)
            var ys = new List<float>();
            foreach (var p in Pieces308.Arr(house, "pad_probe_xz"))
            {
                float x = Pieces308.At(p, 0), z = Pieces308.At(p, 1);
                if (!Pieces308.Ground(rule, x, z, window, out var hit)) { k.Refuse.Add("no ground under the pad probe (" + Pieces308.F(x) + ", " + Pieces308.F(z) + ")"); continue; }
                float fy = field.Sample(x, z); ys.Add(hit.point.y);
                if (Mathf.Abs(hit.point.y - fy) > expectTol) k.Refuse.Add("pad probe (" + Pieces308.F(x) + ", " + Pieces308.F(z) + ") answers " + Pieces308.F(hit.point.y, "F3") + " on " + PostLedger308.PathOf(hit.collider.transform) + ", " + Pieces308.F(hit.point.y - fy, "F3") + " m off the height field: not terrain");
            }
            if (ys.Count == 0) throw new PostLedger308.Refused("no pad probe answered");
            ys.Sort(); k.Pad = ys[ys.Count / 2]; float spread = ys[ys.Count - 1] - ys[0];
            k.Lines.Add("packed surface: median " + Pieces308.F(k.Pad, "F3") + " of " + ys.Count + " probes (spread " + Pieces308.F(spread, "F3") + ")");
            k.Row(spread <= Pieces308.Num(house, "pad_spread_max_m"), "the packed surface is one level: probe spread " + Pieces308.F(spread, "F3") + " m (≤ " + Pieces308.F(Pieces308.Num(house, "pad_spread_max_m")) + ")");
            k.Row(Mathf.Abs(k.Pad - Pieces308.Num(house, "pad_level_expect_m")) <= Pieces308.Num(house, "pad_level_tol_m"), "packed surface " + Pieces308.F(k.Pad, "F3") + " = the offline level " + Pieces308.F(Pieces308.Num(house, "pad_level_expect_m")) + " ± " + Pieces308.F(Pieces308.Num(house, "pad_level_tol_m")));

            // ---- the house target
            var plate = DelChild(k.House, Pieces308.Str(house, "plate"), "plinth plate"); var pf = plate.GetComponent<MeshFilter>();
            if (pf == null || pf.sharedMesh == null) throw new PostLedger308.Refused("the plinth plate " + PostLedger308.PathOf(plate) + " has no mesh");
            if (Quaternion.Angle(plate.localRotation, Quaternion.identity) > angTol || (k.House.lossyScale - Vector3.one).magnitude > .001f) throw new PostLedger308.Refused("the house is scaled or its plate is turned: the plinth top cannot be read from the plate bounds");
            float topLocal = plate.localPosition.y + plate.localScale.y * pf.sharedMesh.bounds.max.y;
            k.Row(Mathf.Abs(topLocal - Pieces308.Num(house, "plinth_top_local_m")) <= Pieces308.Num(house, "plinth_top_local_tol_m"), "plinth top over the pivot " + Pieces308.F(topLocal, "F4") + " m = the data " + Pieces308.F(Pieces308.Num(house, "plinth_top_local_m"), "F4") + " ± " + Pieces308.F(Pieces308.Num(house, "plinth_top_local_tol_m"), "F3"));
            k.HouseYaw = Pieces308.Num(v, "yaw"); k.HousePos = new Vector3(Pieces308.At(v["xz"], 0), k.Pad - topLocal, Pieces308.At(v["xz"], 1)); k.HouseRot = Quaternion.Euler(0f, k.HouseYaw, 0f);
            var eb = Pieces308.Req(house, "expect_before");
            bool atBefore = Vector3.Distance(k.House.position, Pieces308.V3(eb["pos"])) <= Pieces308.Num(rules, "before_tol_m") && Mathf.Abs(Mathf.DeltaAngle(DelYaw(k.House.rotation), Pieces308.Num(eb, "yaw"))) <= Pieces308.Num(rules, "before_tol_deg");
            k.HouseAtTarget = Vector3.Distance(k.House.position, k.HousePos) <= Pieces308.Num(rules, "seat_tol_m") && Quaternion.Angle(k.House.rotation, k.HouseRot) <= angTol;
            k.Lines.Add("house " + PostLedger308.PathOf(k.House) + " " + Pieces308.V(k.House.position) + " yaw " + Pieces308.F(DelYaw(k.House.rotation), "F1") + " -> " + Pieces308.V(k.HousePos) + " yaw " + Pieces308.F(k.HouseYaw, "F1") + " [variant " + k.Variant + "]"
                + (k.HouseAtTarget ? " | already there" : " | moves " + Pieces308.F(Vector2.Distance(new Vector2(k.House.position.x, k.House.position.z), new Vector2(k.HousePos.x, k.HousePos.z))) + " m in plan, " + Pieces308.F(k.HousePos.y - k.House.position.y, "F3") + " m up")
                + " | " + k.House.GetComponentsInChildren<Collider>(true).Length + " collider(s) ride along, skirt child " + (k.House.Find(Pieces308.Str(house, "skirt")) != null ? "present" : "MISSING"));
            if (!atBefore && !k.HouseAtTarget) k.Refuse.Add("the house stands at " + Pieces308.V(k.House.position) + " yaw " + Pieces308.F(DelYaw(k.House.rotation), "F1") + ": neither house.expect_before nor the target (moved by another tool)");
            k.Row(k.House.Find(Pieces308.Str(house, "skirt")) != null, "the stone skirt is a child of the house (it moves with it)");

            // roads: the base and the roof stay off the bed of both routes
            var road = Pieces308.Req(house, "road"); var baseP = DelRect(k.HousePos, k.HouseYaw, house["base_rect"]); var roofP = DelRect(k.HousePos, k.HouseYaw, house["roof_rect"]);
            var lines = new List<(string id, Vector2[] pts, float half)>();
            foreach (var rid in Pieces308.Arr(road, "routes"))
            {
                var r = session.MountainLayout.Routes.FirstOrDefault(x => x != null && x.Id == rid.Value<string>());
                if (r == null || r.Bends == null || r.Bends.Length < 2) { k.Refuse.Add("the layout has no route " + rid); continue; }
                lines.Add((r.Id, r.Bends, r.Width * .5f));
                float db = DelPolyLine(baseP, r.Bends) - r.Width * .5f, dr = DelPolyLine(roofP, r.Bends) - r.Width * .5f;
                k.Row(db >= Pieces308.Num(road, "base_clear_of_bed_m"), "base outside the bed of " + r.Id + " (width " + Pieces308.F(r.Width, "F1") + "): " + Pieces308.F(db) + " m (≥ " + Pieces308.F(Pieces308.Num(road, "base_clear_of_bed_m")) + ")");
                k.Row(dr >= Pieces308.Num(road, "roof_clear_of_bed_m"), "eaves outside the bed of " + r.Id + ": " + Pieces308.F(dr) + " m (≥ " + Pieces308.F(Pieces308.Num(road, "roof_clear_of_bed_m")) + ")");
            }

            // footing: no daylight under the base / the skirt along the four edges
            var foot = Pieces308.Req(house, "footing"); var holder = k.House.Find(Pieces308.Str(house, "skirt")); var sm = holder != null && holder.GetComponent<MeshFilter>() != null ? holder.GetComponent<MeshFilter>().sharedMesh : null;
            if (sm != null && sm.isReadable)
            {
                var sv = sm.vertices; float step = Pieces308.Num(foot, "step_m"), band = Pieces308.Num(foot, "band_m"); var br = house["base_rect"];
                float x0 = Pieces308.At(br, 0), x1 = Pieces308.At(br, 1), z0 = Pieces308.At(br, 2), z1 = Pieces308.At(br, 3); float worst = float.NegativeInfinity; string where = ""; int n = 0;
                void Sample(string side, float lx, float lz, bool alongZ)
                {
                    float low = 0f;
                    foreach (var q in sv)
                        if (alongZ ? Mathf.Abs(q.x - lx) <= band && Mathf.Abs(q.z - lz) <= step : Mathf.Abs(q.z - lz) <= band && Mathf.Abs(q.x - lx) <= step) low = Mathf.Min(low, q.y);
                    var w = DelLocalToWorld(k.HousePos, k.HouseYaw, lx, lz);
                    if (!Pieces308.Ground(rule, w.x, w.y, window, out var g)) { k.Refuse.Add("no ground under the base edge " + side + " at " + DelV2(w)); return; }
                    float f = k.HousePos.y + low - g.point.y; n++;
                    if (f > worst) { worst = f; where = side + " " + DelV2(w) + " lowest " + Pieces308.F(k.HousePos.y + low, "F3") + " ground " + Pieces308.F(g.point.y, "F3"); }
                }
                for (float lz = z0; lz <= z1 + 1e-4f; lz += step) { Sample("+x", x1, lz, true); Sample("-x", x0, lz, true); }
                for (float lx = x0; lx <= x1 + 1e-4f; lx += step) { Sample("+z", lx, z1, false); Sample("-z", lx, z0, false); }
                k.Row(worst <= Pieces308.Num(foot, "float_max_m"), "footing: largest daylight under the base / skirt " + Pieces308.F(worst, "F3") + " m over " + n + " edge samples (≤ " + Pieces308.F(Pieces308.Num(foot, "float_max_m")) + "; at " + where + ")");
            }
            else k.Lines.Add("WARN footing: the skirt mesh is " + (sm == null ? "missing" : "not readable") + " - the edge samples are judged offline only (relayout_fix3_dry.py P6)");

            // ---- content rows
            string pointId = "", restId = "", checkId = "";
            foreach (var row in Pieces308.Arr(d, "nodes")) { if (Pieces308.Opt(row, "checkpoint").Length > 0) { checkId = Pieces308.Opt(row, "checkpoint"); restId = Pieces308.Opt(row, "point"); } else if (Pieces308.Opt(row, "point").Length > 0) pointId = Pieces308.Opt(row, "point"); }
            k.Point = (k.Content.Points ?? Array.Empty<PrologueContentSO.Point>()).FirstOrDefault(x => x != null && x.Id == pointId);
            k.RestPoint = (k.Content.Points ?? Array.Empty<PrologueContentSO.Point>()).FirstOrDefault(x => x != null && x.Id == restId);
            k.Check = (k.Content.Checkpoints ?? Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>()).FirstOrDefault(x => x != null && x.Id == checkId);
            if (k.Point == null || k.RestPoint == null || k.Check == null) throw new PostLedger308.Refused("the content lacks point '" + pointId + "', point '" + restId + "' or checkpoint '" + checkId + "'");

            // ---- the nodes
            var keep = new HashSet<string>(Pieces308.Arr(d, "keep").Select(t => t.Value<string>())); var parking = DelChild(k.Root, "Parking", "parking node");
            var car = Pieces308.Req(rules, "car"); float cw = Pieces308.Num(car, "width_m") * .5f, cl = Pieces308.Num(car, "length_m") * .5f;
            var carP = DelRect(parking.position, DelYaw(parking.rotation), new JArray(-cw, cw, -cl, cl));
            var houseBounds = new Bounds(new Vector3(baseP[0].x, 0f, baseP[0].y), Vector3.zero);
            foreach (var p in baseP.Concat(roofP)) houseBounds.Encapsulate(new Vector3(p.x, 0f, p.y));
            var listed = new HashSet<string>(); var at = new Dictionary<string, Vector2>();
            foreach (var row in Pieces308.Arr(d, "nodes"))
            {
                string name = Pieces308.Str(row, "node"); listed.Add(name);
                if (keep.Contains(name)) { k.Refuse.Add("node " + name + " is listed in keep[] and in nodes[]"); continue; }
                var t = DelChild(k.Root, name, "node"); float x = Pieces308.At(row["xz"], 0), z = Pieces308.At(row["xz"], 1); var xz = new Vector2(x, z); at[name] = xz;
                if (!Pieces308.Ground(rule, x, z, window, out var hit)) { k.Refuse.Add("no ground under node " + name + " at " + DelV2(xz)); continue; }
                float fy = field.Sample(x, z), lift = row["lift"] != null ? row["lift"].Value<float>() : 0f; string support = PostLedger308.PathOf(hit.collider.transform);
                var pos = new Vector3(x, hit.point.y + lift, z); var rot = row["yaw"] != null && row["yaw"].Type != JTokenType.Null ? Quaternion.Euler(0f, row["yaw"].Value<float>(), 0f) : t.rotation;
                k.Nodes.Add((t, row, pos, rot, hit.point.y, fy, support));
                float sp = Pieces308.Num(rules, "node_slope_probe_m"); float slope = Vector3.Angle(hit.normal, Vector3.up);
                if (Pieces308.Ground(rule, x + sp, z, window, out var hx1) && Pieces308.Ground(rule, x - sp, z, window, out var hx0) && Pieces308.Ground(rule, x, z + sp, window, out var hz1) && Pieces308.Ground(rule, x, z - sp, window, out var hz0))
                    slope = Mathf.Max(slope, Mathf.Atan(new Vector2((hx1.point.y - hx0.point.y) / (2f * sp), (hz1.point.y - hz0.point.y) / (2f * sp)).magnitude) * Mathf.Rad2Deg);
                float lane = float.PositiveInfinity; string laneId = "";
                foreach (var (id, pts, half) in lines) for (int j = 0; j + 1 < pts.Length; j++) { float dl = DelSegDist(xz, pts[j], pts[j + 1]); if (dl < lane) { lane = dl; laneId = id; } }
                float dCar = DelPolyPoint(carP, xz);
                float dHouse = Mathf.Max(Mathf.Max(houseBounds.min.x - x, x - houseBounds.max.x), Mathf.Max(houseBounds.min.z - z, z - houseBounds.max.z));
                k.Lines.Add("node " + name + ": " + Pieces308.V(t.position) + " -> " + Pieces308.V(pos) + (row["yaw"] != null && row["yaw"].Type != JTokenType.Null ? " yaw " + Pieces308.F(DelYaw(t.rotation), "F1") + " -> " + Pieces308.F(row["yaw"].Value<float>(), "F1") : "") + " | ground " + Pieces308.F(hit.point.y, "F3") + " on " + support + ", height field " + Pieces308.F(fy, "F3") + (lift != 0f ? ", lift " + Pieces308.F(lift) : ""));
                k.Row(Mathf.Abs(hit.point.y - fy) <= expectTol, name + ": ground " + Pieces308.F(hit.point.y - fy, "F3") + " m off the height field (≤ " + Pieces308.F(expectTol) + ": terrain answers, not a roof or a crate)");
                k.Row(Mathf.Abs(hit.point.y - k.Pad) <= Pieces308.Num(rules, "node_pad_tol_m"), name + ": on the flat - ground " + Pieces308.F(hit.point.y - k.Pad, "F3") + " m from the packed surface (± " + Pieces308.F(Pieces308.Num(rules, "node_pad_tol_m")) + ")");
                k.Row(slope <= Pieces308.Num(rules, "node_slope_max_deg"), name + ": slope " + Pieces308.F(slope, "F1") + "° (≤ " + Pieces308.F(Pieces308.Num(rules, "node_slope_max_deg"), "F0") + ")");
                k.Row(lane >= Pieces308.Num(rules, "lane_clear_m"), name + ": " + Pieces308.F(lane) + " m from the centre line of " + laneId + " (≥ " + Pieces308.F(Pieces308.Num(rules, "lane_clear_m")) + ": clear of the lane)");
                k.Row(dCar >= Pieces308.Num(rules, "parking_clear_m"), name + ": " + Pieces308.F(dCar) + " m from the car's footprint at Parking (≥ " + Pieces308.F(Pieces308.Num(rules, "parking_clear_m")) + ")");
                k.Row(dHouse >= Pieces308.Num(rules, "house_clear_m"), name + ": " + Pieces308.F(dHouse) + " m outside the house's bounds (≥ " + Pieces308.F(Pieces308.Num(rules, "house_clear_m")) + "; the F5 stops rule refuses a node inside them)");
            }
            foreach (Transform c in k.Root) if (c != k.House && !keep.Contains(c.name) && !listed.Contains(c.name)) k.Refuse.Add("stop child '" + c.name + "' is neither in keep[] nor in nodes[] nor the house: it would be left behind");
            foreach (var row in Pieces308.Arr(d, "nodes"))
            {
                string same = Pieces308.Opt(row, "same_as"); string name = Pieces308.Str(row, "node");
                if (same.Length > 0 && at.ContainsKey(same) && at.ContainsKey(name)) k.Row(Vector2.Distance(at[same], at[name]) <= poseTol, name + " stands on " + same + " (" + Pieces308.F(Vector2.Distance(at[same], at[name]), "F3") + " m)");
            }
            if (at.ContainsKey("Checkpoint") && at.ContainsKey("capital_escort_rest"))
            { float db = Vector2.Distance(at["Checkpoint"], at["capital_escort_rest"]); k.Row(Mathf.Abs(db - Pieces308.Num(rules, "bench_from_feet_m")) <= Pieces308.Num(rules, "bench_from_feet_tol_m"), "bench " + Pieces308.F(db) + " m from the respawn feet (" + Pieces308.F(Pieces308.Num(rules, "bench_from_feet_m")) + " ± " + Pieces308.F(Pieces308.Num(rules, "bench_from_feet_tol_m")) + ")"); }
            if (at.ContainsKey("CargoClerk252") && at.ContainsKey("InspectionDesk252"))
            { float dd = Vector2.Distance(at["CargoClerk252"], at["InspectionDesk252"]); k.Row(Mathf.Abs(dd - Pieces308.Num(rules, "desk_from_clerk_m")) <= Pieces308.Num(rules, "desk_from_clerk_tol_m"), "desk " + Pieces308.F(dd) + " m from the clerk (" + Pieces308.F(Pieces308.Num(rules, "desk_from_clerk_m")) + " ± " + Pieces308.F(Pieces308.Num(rules, "desk_from_clerk_tol_m")) + ")"); }
            var enemy = Pieces308.Req(rules, "rule54_from")["encounter"]; var ev = new Vector2(Pieces308.At(enemy, 0), Pieces308.At(enemy, 1));
            foreach (var (name, p) in new[] { ("Parking", new Vector2(parking.position.x, parking.position.z)) }.Concat(at.Where(x => x.Key == "Interaction" || x.Key == "Checkpoint").Select(x => (x.Key, x.Value))))
                k.Row(Vector2.Distance(p, ev) >= Pieces308.Num(rules, "rule54_m"), name + ": " + Pieces308.F(Vector2.Distance(p, ev), "F1") + " m from the road enemy at " + DelV2(ev) + " (≥ " + Pieces308.F(Pieces308.Num(rules, "rule54_m"), "F0") + ")");
            k.Change = !k.HouseAtTarget || k.Nodes.Any(n => Vector3.Distance(n.t.position, n.pos) > Pieces308.Num(rules, "seat_tol_m") || Quaternion.Angle(n.t.rotation, n.rot) > angTol);
            return k;
        }

        static string Delivery(JObject ops, string scenePath, string key, string verb, string extra)
        {
            var d = Pieces308.Json(DeliveryData, out string sha); string name = LedgerName("delivery", key);
            if (verb == "revert") return DeliveryRevert(ops, d, scenePath, key, name, extra == "force");
            if (verb == "verify") return DeliveryVerify(ops, d, scenePath, key, name);
            bool dry = verb == "plan"; var k = DeliveryMeasure(ops, d, scenePath, key, !dry); var rules = Pieces308.Req(d, "rules");
            var sb = new StringBuilder("delivery " + verb + " " + scenePath + " (data " + Pieces308.Short(sha) + " " + Pieces308.Opt(d, "version") + ")\n"); foreach (string line in k.Lines) sb.AppendLine("  " + line);
            foreach (string r in k.Refuse) sb.AppendLine("  REFUSE " + r);
            bool decided = Pieces308.Flag(d, "moves_colliders") && !string.IsNullOrWhiteSpace(Pieces308.Opt(d, "bake_decision"));
            if (!decided) k.Refuse.Add("the move takes colliders across walkable ground: the data needs moves_colliders true and a bake_decision");
            if (!k.Change && k.Refuse.Count == 0 && k.Fail.Count == 0) return sb.Append("  변경 없음 (the house and every listed node stand on their targets; nothing written, nothing saved)").ToString();
            if (dry) return sb.Append(k.Refuse.Count == 0 && k.Fail.Count == 0 ? "  apply would move the house and " + k.Nodes.Count + " node(s) and write the content asset and the scene; nothing changed" : "  apply would be REFUSED (" + k.Refuse.Count + " refusal(s), " + k.Fail.Count + " failed rule(s)); nothing changed").ToString();
            if (k.Refuse.Count > 0) throw new PostLedger308.Refused(k.Refuse[0]);
            if (k.Fail.Count > 0) throw new PostLedger308.Refused(k.Fail.Count + " rule(s) fail, first: " + k.Fail[0]);
            if (EditorUtility.IsDirty(k.Content)) throw new PostLedger308.Refused(k.ContentPath + " has unsaved in-memory changes (another session?)");

            string utc = PostLedger308.Utc(); string backup = Pieces308.Backup(utc + "-delivery-" + key, scenePath, k.ContentPath);
            var prior = Pieces308.ReadLedger<DelLedger>(name); string ledgerFile = Pieces308.OutFile(name); string priorText = File.Exists(ledgerFile) ? File.ReadAllText(ledgerFile, Encoding.UTF8) : null;
            var inter = k.Nodes.First(n => n.t.name == "Interaction"); var feet = k.Nodes.First(n => n.t.name == "Checkpoint");
            DelLedger l;
            if (prior != null && (prior.state == "applied" || prior.state == "applying")) l = prior;   // a second apply keeps the FIRST before-values
            else
            {
                l = new DelLedger { scene = scenePath, key = key, content = k.ContentPath, contentShaBefore = Pieces308.ShaAsset(k.ContentPath), sceneShaBefore = Pieces308.ShaAsset(scenePath), backup = backup, point = k.Point.Id, restPoint = k.RestPoint.Id, checkpoint = k.Check.Id };
                l.beforeBits = new[] { Bits(k.Point.Position.x), Bits(k.Point.Position.y), Bits(k.Point.Position.z), Bits(k.RestPoint.Position.x), Bits(k.RestPoint.Position.y), Bits(k.RestPoint.Position.z), Bits(k.Check.Feet.x), Bits(k.Check.Feet.y), Bits(k.Check.Feet.z), Bits(k.Check.Yaw) };
                l.nodes.Add(new DelNode { node = k.House.name, localPosBefore = k.House.localPosition, localRotBefore = k.House.localRotation });
                foreach (var n in k.Nodes) l.nodes.Add(new DelNode { node = n.t.name, localPosBefore = n.t.localPosition, localRotBefore = n.t.localRotation });
            }
            void Put(string node, Vector3 pos, Quaternion rot, float ground, float fy, string support)
            { var r = l.nodes.First(x => x.node == node); r.worldAfter = pos; r.worldRotAfter = rot; r.ground = ground; r.field = fy; r.support = support; }
            Put(k.House.name, k.HousePos, k.HouseRot, k.Pad, k.Pad, "packed surface (median of the pad probes)");
            foreach (var n in k.Nodes) Put(n.t.name, n.pos, n.rot, n.ground, n.field, n.support);
            l.pointAfter = inter.pos; l.restPointAfter = feet.pos; l.feetAfter = feet.pos; l.yawAfter = DelYaw(feet.rot); l.padLevel = k.Pad; l.variant = k.Variant; l.bakeDecision = Pieces308.Opt(d, "bake_decision");
            l.state = "applying"; l.utc = utc; l.dataSha256 = sha; l.history = Add(l.history, "applying variant " + k.Variant + " (backup " + backup + ")");
            Pieces308.WriteLedger(name, l);
            string contentAbs = PostLedger308.Abs(k.ContentPath); var contentBytes = File.ReadAllBytes(contentAbs);
            try
            {
                k.House.SetPositionAndRotation(k.HousePos, k.HouseRot); EditorUtility.SetDirty(k.House);
                foreach (var n in k.Nodes) { n.t.SetPositionAndRotation(n.pos, n.rot); EditorUtility.SetDirty(n.t); }
                k.Point.Position = inter.pos; k.RestPoint.Position = feet.pos; k.Check.Feet = feet.pos; k.Check.Yaw = DelYaw(feet.rot);
                EditorUtility.SetDirty(k.Content); AssetDatabase.SaveAssetIfDirty(k.Content);
                Physics.SyncTransforms();
                Pieces308.SaveOrReload(k.Scene);
            }
            catch
            {
                File.WriteAllBytes(contentAbs, contentBytes); AssetDatabase.ImportAsset(k.ContentPath, ImportAssetOptions.ForceUpdate);
                Pieces308.Reload(k.Scene);
                if (priorText != null) File.WriteAllText(ledgerFile, priorText, new UTF8Encoding(false)); else if (File.Exists(ledgerFile)) File.Delete(ledgerFile);
                throw;
            }
            l.state = "applied"; l.contentShaAfter = Pieces308.ShaAsset(k.ContentPath); l.sceneShaAfter = Pieces308.ShaAsset(scenePath); l.history = Add(l.history, "applied variant " + k.Variant);
            Pieces308.WriteLedger(name, l);
            sb.AppendLine("  wrote " + k.ContentPath + " sha " + Pieces308.Short(l.contentShaBefore) + " -> " + Pieces308.Short(l.contentShaAfter) + " and the scene " + Pieces308.Short(l.sceneShaBefore) + " -> " + Pieces308.Short(l.sceneShaAfter));
            sb.Append("  backup " + backup + " | ledger " + ledgerFile + " | next: delivery:verify:" + key + " -> stops:verify:" + key + " -> (after the three scenes) Content308 campaign-apply -> the ONE bake (" + Pieces308.Opt(d, "decision") + "). Walk nodes report WAIT until that bake");
            return sb.ToString();
        }

        static string DeliveryVerify(JObject ops, JObject d, string scenePath, string key, string name)
        {
            var k = DeliveryMeasure(ops, d, scenePath, key, false); var rules = Pieces308.Req(d, "rules"); var house = Pieces308.Req(d, "house");
            var sb = new StringBuilder("delivery verify " + scenePath + "\n"); int fail = 0, wait = 0;
            void Row(bool ok, string text) { if (!ok) fail++; sb.AppendLine("  " + (ok ? "PASS " : "FAIL ") + text); }
            foreach (string r in k.Refuse) Row(false, "measure: " + r);
            foreach (string line in k.Lines) { if (line.StartsWith("FAIL ", StringComparison.Ordinal)) fail++; sb.AppendLine("  " + line); }
            float seatTol = Pieces308.Num(rules, "seat_tol_m"), angTol = Pieces308.Num(rules, "angle_tol_deg"), audit = Pieces308.Num(rules, "audit_support_tol_m"), nav = Pieces308.Num(rules, "nav_radius_m"), a1 = Pieces308.Num(rules, "pose_tol_m");
            Row(k.HouseAtTarget, "the house stands on its target " + Pieces308.V(k.HousePos) + " yaw " + Pieces308.F(k.HouseYaw, "F1") + " (now " + Pieces308.V(k.House.position) + " yaw " + Pieces308.F(DelYaw(k.House.rotation), "F1") + ")");
            float top = k.House.position.y + Pieces308.Num(house, "plinth_top_local_m");
            Row(Mathf.Abs(top - k.Pad) <= Pieces308.Num(house, "plinth_top_tol_m"), "plinth top " + Pieces308.F(top, "F3") + " = the packed surface " + Pieces308.F(k.Pad, "F3") + " (" + Pieces308.F(top - k.Pad, "F3") + " m, ± " + Pieces308.F(Pieces308.Num(house, "plinth_top_tol_m")) + ")");
            // the data rectangles against the colliders as they stand (a wrong rectangle or scale shows here)
            Physics.SyncTransforms(); var cols = k.House.GetComponentsInChildren<Collider>(false).Where(c => c.enabled && !c.isTrigger).ToArray();
            if (cols.Length > 0)
            {
                var b = cols[0].bounds; foreach (var c in cols) b.Encapsulate(c.bounds);
                var want = new Bounds(k.House.position, Vector3.zero);
                foreach (var p in DelRect(k.House.position, DelYaw(k.House.rotation), house["base_rect"]).Concat(DelRect(k.House.position, DelYaw(k.House.rotation), house["roof_rect"]))) want.Encapsulate(new Vector3(p.x, k.House.position.y, p.y));
                float off = Mathf.Max(Mathf.Max(Mathf.Abs(b.min.x - want.min.x), Mathf.Abs(b.max.x - want.max.x)), Mathf.Max(Mathf.Abs(b.min.z - want.min.z), Mathf.Abs(b.max.z - want.max.z)));
                float rectTol = Pieces308.Num(house, "rect_tol_m") + Pieces308.Num(rules, "collider_rect_slack_m");
                Row(off <= rectTol, "house collider bounds (XZ) = the data rectangles at this pose: largest edge difference " + Pieces308.F(off, "F3") + " m (≤ " + Pieces308.F(rectTol, "F3") + "; " + cols.Length + " colliders)");
            }
            var inter = DelChild(k.Root, "Interaction", "node"); var feet = DelChild(k.Root, "Checkpoint", "node");
            float di = Vector3.Distance(inter.position, k.Point.Position), dc = Vector3.Distance(feet.position, k.Check.Feet), dr = Vector3.Distance(feet.position, k.RestPoint.Position);
            Row(di <= a1, "A1 Interaction " + Pieces308.V(inter.position) + " on the content point " + k.Point.Id + " " + Pieces308.V(k.Point.Position) + " (" + Pieces308.F(di, "F3") + " m, rule " + Pieces308.F(a1) + ")");
            Row(dc <= a1 && Mathf.Abs(Mathf.DeltaAngle(DelYaw(feet.rotation), k.Check.Yaw)) <= angTol, "A1 Checkpoint " + Pieces308.V(feet.position) + " yaw " + Pieces308.F(DelYaw(feet.rotation), "F1") + " on the checkpoint " + k.Check.Id + " feet " + Pieces308.V(k.Check.Feet) + " yaw " + Pieces308.F(k.Check.Yaw, "F1") + " (" + Pieces308.F(dc, "F3") + " m, rule " + Pieces308.F(a1) + ")");
            Row(dr <= a1, "the rest point " + k.RestPoint.Id + " " + Pieces308.V(k.RestPoint.Position) + " stands on the respawn feet (" + Pieces308.F(dr, "F3") + " m)");
            foreach (var n in k.Nodes)
            {
                float gap = Vector3.Distance(n.t.position, n.pos); float ang = Quaternion.Angle(n.t.rotation, n.rot);
                bool sup = Physics.RaycastAll(n.t.position + Vector3.up * .5f, Vector3.down, .8f + Mathf.Abs(n.pos.y - n.ground), ~0, QueryTriggerInteraction.Ignore).Any(h => !(h.collider is CharacterController) && !h.collider.transform.IsChildOf(k.Root) && Mathf.Abs(h.point.y - n.ground) <= audit);
                Row(gap <= seatTol && ang <= angTol && sup, n.t.name + " " + Pieces308.V(n.t.position) + ": on its target (off " + Pieces308.F(gap, "F3") + " m, " + Pieces308.F(ang, "F2") + "°; rule " + Pieces308.F(seatTol) + "), support " + sup);
                if (!Pieces308.Flag(n.row, "walk")) continue;
                bool onNav = NavMesh.SamplePosition(n.t.position, out var nh, nav, NavMesh.AllAreas) && Vector3.Distance(nh.position, n.t.position) <= nav;
                if (onNav) sb.AppendLine("  PASS " + n.t.name + ": NavMesh within " + Pieces308.F(nav, "F1") + " m");
                else { wait++; sb.AppendLine("  WAIT " + n.t.name + ": no NavMesh within " + Pieces308.F(nav, "F1") + " m (expected until the one bake of " + Pieces308.Opt(d, "decision") + ")"); }
            }
            int lights = k.Root.GetComponentsInChildren<Light>(true).Count(x => x.enabled && x.gameObject.activeInHierarchy);
            Row(lights == 0, "lights on under the stop " + lights + " (this op adds none; the stop has none)");
            var l = Pieces308.ReadLedger<DelLedger>(name);
            sb.AppendLine("  INFO ledger " + (l == null ? "none" : l.state + " " + l.utc + " variant " + l.variant + ", content sha now " + Pieces308.Short(Pieces308.ShaAsset(k.ContentPath)) + (l.contentShaAfter == Pieces308.ShaAsset(k.ContentPath) ? " (= this ledger's write)" : " (changed after this ledger)")));
            if (k.Scene.isDirty) { fail++; sb.AppendLine("  FAIL the scene is dirty after a read-only command (bug): reload it"); }
            sb.Insert(0, (fail == 0 ? "GREEN" : "RED") + " (FAIL " + fail + ", WAIT " + wait + ") ");
            return Pieces308.Report("cb308-small-delivery-verify-" + key + ".txt", sb);
        }

        static string DeliveryRevert(JObject ops, JObject d, string scenePath, string key, string name, bool force)
        {
            var l = Pieces308.ReadLedger<DelLedger>(name);
            if (l == null || l.state != "applied" && l.state != "applying") throw new PostLedger308.Refused("no applied ledger " + name);
            if (PostLedger308.IsProtectedPath(l.content)) throw new PostLedger308.Refused("protected content path in the ledger");
            if (l.beforeBits == null || l.beforeBits.Length != 10) throw new PostLedger308.Refused("the ledger " + name + " carries no before-values");
            var rules = Pieces308.Req(d, "rules"); var scene = Pieces308.OpenTarget(scenePath, ops["preview_roots"], true);
            var session = CliffCore308.Session(scene); var content = session != null ? session.Content : null;
            if (content == null || AssetDatabase.GetAssetPath(content) != l.content) throw new PostLedger308.Refused("the scene session does not use " + l.content);
            if (EditorUtility.IsDirty(content)) throw new PostLedger308.Refused(l.content + " has unsaved in-memory changes");
            var root = Pieces308.FindOne(scene, Pieces308.Str(d, "stop"), "delivery stop");
            var point = content.Points.FirstOrDefault(x => x != null && x.Id == l.point); var rest = content.Points.FirstOrDefault(x => x != null && x.Id == l.restPoint); var check = content.Checkpoints.FirstOrDefault(x => x != null && x.Id == l.checkpoint);
            if (point == null || rest == null || check == null) throw new PostLedger308.Refused("the content lost a point / checkpoint of the ledger");
            float seatTol = Pieces308.Num(rules, "seat_tol_m"); var drift = new List<string>(); var rows = new List<(DelNode n, Transform t)>();
            Vector3 B3(int i) => new Vector3(FromBits(l.beforeBits[i]), FromBits(l.beforeBits[i + 1]), FromBits(l.beforeBits[i + 2]));
            foreach (var n in l.nodes)
            {
                var t = DelChild(root, n.node, "ledger node"); rows.Add((n, t));
                if (Vector3.Distance(t.position, n.worldAfter) > seatTol && Vector3.Distance(t.localPosition, n.localPosBefore) > seatTol) drift.Add(n.node + " at " + Pieces308.V(t.position) + " (this op put it at " + Pieces308.V(n.worldAfter) + ")");
            }
            if (Vector3.Distance(point.Position, l.pointAfter) > seatTol && Vector3.Distance(point.Position, B3(0)) > seatTol) drift.Add("point " + l.point + " " + Pieces308.V(point.Position));
            if (Vector3.Distance(check.Feet, l.feetAfter) > seatTol && Vector3.Distance(check.Feet, B3(6)) > seatTol) drift.Add("checkpoint " + l.checkpoint + " feet " + Pieces308.V(check.Feet));
            if (drift.Count > 0 && !force) throw new PostLedger308.Refused("delivery revert " + key + ": " + drift.Count + " value(s) moved after this op wrote them (" + string.Join("; ", drift.Take(4)) + (drift.Count > 4 ? "; ..." : "") + "). Nothing reverted. delivery:revert:" + key + ":force puts the recorded before-values back anyway");
            string utc = PostLedger308.Utc(); string backup = Pieces308.Backup(utc + "-delivery-revert-" + key, scenePath, l.content);
            string abs = PostLedger308.Abs(l.content); var bytes = File.ReadAllBytes(abs);
            try
            {
                foreach (var (n, t) in rows) { t.localPosition = n.localPosBefore; t.localRotation = n.localRotBefore; EditorUtility.SetDirty(t); }
                point.Position = B3(0); rest.Position = B3(3); check.Feet = B3(6); check.Yaw = FromBits(l.beforeBits[9]);
                EditorUtility.SetDirty(content); AssetDatabase.SaveAssetIfDirty(content);
                Physics.SyncTransforms();
                Pieces308.SaveOrReload(scene);
            }
            catch { File.WriteAllBytes(abs, bytes); AssetDatabase.ImportAsset(l.content, ImportAssetOptions.ForceUpdate); Pieces308.Reload(scene); throw; }
            string contentNow = Pieces308.ShaAsset(l.content), sceneNow = Pieces308.ShaAsset(scenePath); string archived = Pieces308.Archive(name);
            return "delivery revert " + scenePath + (l.state == "applying" ? " (the ledger state was 'applying': an apply that did not finish)" : "") + (drift.Count > 0 ? " [FORCED over " + drift.Count + " moved value(s)]" : "") + ": the house and " + (rows.Count - 1) + " node(s) put back, point / rest point / checkpoint feet and yaw restored"
                + "\n  content sha " + Pieces308.Short(contentNow) + (contentNow == l.contentShaBefore ? " (= the bytes before apply)" : " (before apply " + Pieces308.Short(l.contentShaBefore) + ": another writer changed the asset in between)")
                + "\n  scene sha " + Pieces308.Short(sceneNow) + (sceneNow == l.sceneShaBefore ? " (= the bytes before apply)" : " (before apply " + Pieces308.Short(l.sceneShaBefore) + ")")
                + "\n  backup of the state before the revert " + backup + " | ledger archived " + archived + " | the NavMesh keeps the bake it has: a revert AFTER the bake needs the pre-bake NavMesh assets back too (RUN_ORDER_fix3.md section I)";
        }
    }
}
