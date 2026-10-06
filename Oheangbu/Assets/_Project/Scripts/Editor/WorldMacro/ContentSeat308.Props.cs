using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 relayout (D308-16) step D8 = relayout op M10, BUILD_BRIEF T6: per-piece re-seat move of a prop set.
    // The set's root goes rigidly to the data place (x, z from the data, y = the physical ground there + root_y_offset_m, yaw from
    // the data); then every piece (each direct child of the root, with its whole subtree) is shot onto the physical ground again
    // so that its own "bounds bottom - ground under the bounds centre" is what it was before the move. A set moved as one block
    // floats and sinks piece by piece, because the ground under each piece is another shape at the new place.
    // A "before" reading outside reseat.offset_range_m is not a seat (the piece already floated or sat sunk): that piece takes its
    // row of the set's authored_offsets, and with no such row the step refuses. Both numbers go into the ledger.
    //   plan:<alias>            lists D8 when contentseat308.json has "reseat" (read only)
    //   apply:<alias>:D8        only when named. Ledger: the root's and every piece's local pose before / after; a piece's note
    //                           starts with "offset=<m>" (the reading verify compares with)
    //   verify:<alias>          root on the data place and on the ground, root to the nearest route centre line, every piece's gap
    //   revert:<alias>:D8       pieces first, then the root, back to the ledger poses
    // Refusals: root neither at expect_before nor at the target; ground at the target not near expect_ground_y; slope; route
    // centre line closer than road_clear_m; a Tree / Shrub sheet row within trunk_clear_m of a piece; a piece whose gap after the
    // height correction exceeds gap_max_m; a protected tree. Pieces keep their rotation and size. Nothing is created, no light,
    // no text.
    // NOT done here: the dressing sheets' PreserveArea / StoryCluster rows of the #264 build stay at the old centre (data
    // reseat.sheet_note), and the NavMesh keeps the old carve until the next bake.
    public static partial class ContentSeat308
    {
        // renderers of the piece's subtree; colliders only when it has no renderer
        static bool PieceBounds(Transform t, out Bounds b)
        {
            b = default; bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (any) return true;
            foreach (var c in t.GetComponentsInChildren<Collider>(false))
            {
                if (!c.enabled || c.isTrigger) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            return any;
        }

        // F8: the solid colliders of the piece (the #264 rule speaks of colliders); the renderer bounds when it has none
        static bool PieceSolidBounds(Transform t, out Bounds b)
        {
            b = default; bool any = false;
            foreach (var c in t.GetComponentsInChildren<Collider>(false))
            {
                if (!c.enabled || c.isTrigger) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            return any || PieceBounds(t, out b);
        }

        static List<Transform> SetPieces(JToken set, Transform root)
        {
            string mode = Str(set, "pieces");
            if (mode != "children") throw new Refuse("data: reseat set " + Str(set, "id") + " pieces '" + mode + "' is not implemented (children)");
            var list = new List<Transform>();
            for (int i = 0; i < root.childCount; i++) list.Add(root.GetChild(i));
            return list;
        }

        // the set's own colliders must not answer the ground ray (a chest would be seated on its own lid)
        static List<Collider> SkipOwn(Ctx k, Transform root)
        {
            var added = new List<Collider>();
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) if (k.Skip.Add(c)) added.Add(c);
            Physics.SyncTransforms();
            return added;
        }
        static void Unskip(Ctx k, List<Collider> added) { foreach (var c in added) k.Skip.Remove(c); }

        // the "bottom - ground" a piece is seated with: its reading before the move, or its authored row when that reading is not a seat
        static bool SeatOffset(JToken rs, JToken set, string name, float before, out float offset, out string source)
        {
            offset = before; source = "kept";
            var rng = rs["offset_range_m"] as JArray; if (rng == null) return true;
            float lo = At(rng, 0), hi = At(rng, 1);
            if (before >= lo && before <= hi) return true;
            var auth = set["authored_offsets"] as JObject; var row = auth != null ? auth[name] : null;
            if (row == null || row.Type == JTokenType.Null) { source = "before " + F(before) + " is outside " + F(lo) + " .. " + F(hi) + " and the set has no authored_offsets row '" + name + "'"; return false; }
            offset = row.Value<float>(); source = "authored " + F(offset) + " (before " + F(before) + " is outside " + F(lo) + " .. " + F(hi) + ")";
            return true;
        }

        static string PieceName(string key) => key.Substring(key.LastIndexOf('/') + 1);

        static bool OffsetOf(Change c, out float offset)
        {
            offset = 0f; string head = (c.note ?? "").Split('|')[0];
            return head.StartsWith("offset=", StringComparison.Ordinal) && float.TryParse(head.Substring(7), System.Globalization.NumberStyles.Float, Inv, out offset);
        }

        static StepPlan PlanD8(Ctx k)
        {
            var sp = new StepPlan { step = "D8", detail = "per-piece re-seat move" };
            var rs = k.Data["reseat"];
            if (rs == null || rs.Type == JTokenType.Null) { sp.status = "blocked"; sp.detail = "contentseat308.json has no 'reseat' section (308.seat.3 data)"; return sp; }
            if (!Flag(rs, "enabled")) { sp.status = "off"; sp.detail = "off in data"; return sp; }
            var blocks = new List<string>();
            foreach (var set in Arr(rs, "sets"))
            {
                string sid = Str(set, "id");
                if (!Flag(set, "enabled")) { sp.notes.Add(sid + ": off in data"); continue; }
                string rootKey = Str(set, "root_key"); var root = Strict(k, rootKey, false);
                if (root == null) { sp.notes.Add("absent " + rootKey + " in this scene (skipped)"); continue; }
                var added = SkipOwn(k, root);
                try { PlanSet(k, sp, rs, set, sid, rootKey, root, blocks); }
                finally { Unskip(k, added); }
            }
            if (blocks.Count > 0) { sp.status = "blocked"; sp.detail = string.Join("; ", blocks); return sp; }
            Settle(sp);
            return sp;
        }

        static void PlanSet(Ctx k, StepPlan sp, JToken rs, JToken set, string sid, string rootKey, Transform root, List<string> blocks)
        {
            var tol = Req(k.Data, "tolerances"); float gapMax = Num(rs, "gap_max_m"), maxShift = Num(rs, "max_piece_shift_m");
            if (ProtectedObject(k, root)) { blocks.Add(sid + ": " + rootKey + " stands under a protected tree"); return; }
            float tx = At(Req(set, "target_xz"), 0), tz = At(Req(set, "target_xz"), 1), yawT = Num(set, "yaw");
            if (!Ground(k, tx, tz, out var g, out float slope)) { blocks.Add(sid + ": no ground under the target (" + F(tx) + ", " + F(tz) + ")"); return; }
            var rootPos = new Vector3(tx, g.y + OptNum(set, "root_y_offset_m"), tz);
            var dq = Quaternion.Euler(0, Mathf.DeltaAngle(root.eulerAngles.y, yawT), 0); var rootRot = dq * root.rotation;
            float road = CentreLine(new Vector2(tx, tz), BuildingAudit308.Corridors(k.Scene, k.Cfg), Num(k.Data, "corridor_run_gap_m"), out string roadId);
            N(k, sp, "D8." + sid + ".root_x", rootPos.x); N(k, sp, "D8." + sid + ".root_y", rootPos.y); N(k, sp, "D8." + sid + ".root_z", rootPos.z); N(k, sp, "D8." + sid + ".slope", slope); N(k, sp, "D8." + sid + ".road", road);
            sp.notes.Add(sid + ": root " + V(root.position) + " yaw " + F(root.eulerAngles.y, "F1") + " -> " + V(rootPos) + " yaw " + F(yawT, "F1") + " | ground at the target " + F(g.y) + " (expected " + F(Num(set, "expect_ground_y")) + "), slope " + F(slope, "F1") + " deg | nearest route centre line " + F(road) + " m (" + roadId + ")");
            var fails = new List<string>();
            if (Mathf.Abs(g.y - Num(set, "expect_ground_y")) > Num(rs, "ground_y_tol_m")) fails.Add("ground " + F(g.y) + " at the target is not within " + F(Num(rs, "ground_y_tol_m")) + " m of expect_ground_y " + F(Num(set, "expect_ground_y")) + " (another collider under the target, or the terrain changed)");
            if (slope > Num(rs, "max_slope_deg")) fails.Add("slope " + F(slope, "F1") + " deg at the target (> " + F(Num(rs, "max_slope_deg"), "F0") + ")");
            if (road < Num(set, "road_clear_m")) fails.Add("root " + F(road) + " m from the centre line of " + roadId + " (< " + F(Num(set, "road_clear_m")) + ")");

            var rootCh = new Change { step = "D8", key = rootKey, kind = "pose", before = PoseText(root.localPosition, root.localRotation), after = LocalPoseFor(root, rootPos, rootRot), at = rootPos };
            var prior = Prior(k.Ledger, "D8", rootKey, "pose");
            bool atTarget = Vector3.Distance(root.position, rootPos) <= k.PoseTol && Quaternion.Angle(root.rotation, rootRot) <= k.YawTol;
            var pieces = SetPieces(set, root);
            if (atTarget)
            {
                // the root stands on the target: with a ledger every piece must still be on its ledger pose
                rootCh.state = "already"; rootCh.before = prior != null ? prior.before : "";
                if (prior == null) rootCh.note = "the root already stands on the target and this ledger has no before value: the pieces' offsets are unknown (nothing is changed)";
                sp.changes.Add(rootCh);
                foreach (var t in pieces)
                {
                    string key = BuildingAudit308.KeyOf(t); var pp = Prior(k.Ledger, "D8", key, "pose");
                    if (pp == null) { if (prior != null) sp.notes.Add(sid + "/" + PieceName(key) + ": no ledger row (it had no bounds, or was added since)"); continue; }
                    bool same = NearPose(k, t, pp.after);
                    sp.changes.Add(new Change { step = "D8", key = key, kind = "pose", before = pp.before, after = pp.after, at = t.position, state = same ? "already" : "mismatch", note = same ? pp.note : "the piece's local pose is " + PoseText(t.localPosition, t.localRotation) + ", not the ledger's after value (moved by another tool since): revert:" + k.Alias + ":D8:force, then apply again" });
                }
                foreach (var f in fails) sp.notes.Add("limit: " + sid + ": " + f);
                return;
            }
            var eb = Req(set, "expect_before");
            bool known = (prior != null && NearPose(k, root, prior.before)) ||
                         (Harness303.Flat(root.position, new Vector3(At(eb, 0), 0, At(eb, 1))) <= Num(tol, "before_xz_m") && Mathf.Abs(Mathf.DeltaAngle(root.eulerAngles.y, At(eb, 2))) <= Num(tol, "before_yaw_deg"));
            if (!known)
            {
                rootCh.state = "mismatch"; rootCh.note = "the root stands at " + V(root.position) + " yaw " + F(root.eulerAngles.y, "F1") + ": neither expect_before (" + F(At(eb, 0)) + ", " + F(At(eb, 1)) + ") yaw " + F(At(eb, 2), "F1") + " nor the target " + V(rootPos);
                sp.changes.Add(rootCh); return;
            }
            rootCh.state = "apply"; rootCh.note = "rigid move " + F(Harness303.Flat(root.position, rootPos), "F1") + " m, y " + F(root.position.y) + " -> " + F(rootPos.y) + ", yaw " + F(root.eulerAngles.y, "F1") + " -> " + F(yawT, "F1");
            sp.changes.Add(rootCh);

            // the root's frame after the move (its parent does not move)
            var parent = root.parent;
            var lp = parent != null ? parent.InverseTransformPoint(rootPos) : rootPos; var lr = parent != null ? Quaternion.Inverse(parent.rotation) * rootRot : rootRot;
            var inv = ((parent != null ? parent.localToWorldMatrix : Matrix4x4.identity) * Matrix4x4.TRS(lp, lr, root.localScale)).inverse;
            Vector3 Map(Vector3 p) => rootPos + dq * (p - root.position);
            float dy = rootPos.y - root.position.y, searchM = Num(rs, "trunk_search_m"), trunkClear = Num(rs, "trunk_clear_m"), worstRigid = 0f;
            foreach (var t in pieces)
            {
                string key = BuildingAudit308.KeyOf(t), name = PieceName(key);
                if (BuildingAudit308.Resolve(k.Scene, key) != t) { fails.Add(name + ": the key " + key + " does not resolve to the piece"); continue; }
                if (!PieceBounds(t, out var b)) { sp.notes.Add(sid + "/" + name + ": no renderer and no collider - it moves with the root, nothing to seat"); continue; }
                var c = b.center;
                if (!Ground(k, c.x, c.z, out var g0, out _)) { fails.Add(name + ": no ground under the piece where it stands now"); continue; }
                float before = b.min.y - g0.y;
                bool seatOk = SeatOffset(rs, set, name, before, out float offset, out string source);
                if (!seatOk) fails.Add(name + ": " + source);
                var cNew = Map(c);
                if (!Ground(k, cNew.x, cNew.z, out var g1, out _)) { fails.Add(name + ": no ground under the piece at the new place " + V(cNew)); continue; }
                // rigidGap = how far off its seat the piece would be after the rigid move alone; shift = the height correction
                float rigidGap = b.min.y + dy - g1.y - offset, shift = Mathf.Clamp(-rigidGap, -maxShift, maxShift), gap = rigidGap + shift;
                worstRigid = Mathf.Max(worstRigid, Mathf.Abs(rigidGap));
                float trunk = float.PositiveInfinity; string trunkId = "-";
                foreach (var (p, id) in Trunks(k, cNew, searchM)) { float d = Harness303.Flat(p, cNew); if (d < trunk) { trunk = d; trunkId = id; } }
                N(k, sp, "D8." + sid + "." + name + ".before", before); N(k, sp, "D8." + sid + "." + name + ".offset", offset); N(k, sp, "D8." + sid + "." + name + ".rigid_gap", rigidGap);
                N(k, sp, "D8." + sid + "." + name + ".shift", shift); N(k, sp, "D8." + sid + "." + name + ".gap", gap);
                if (Mathf.Abs(gap) > gapMax) fails.Add(name + ": gap " + F(gap) + " m after the height correction (> " + F(gapMax) + "; the correction is cut at max_piece_shift_m " + F(maxShift) + ")");
                if (trunk < trunkClear) fails.Add(name + ": trunk " + trunkId + " " + F(trunk) + " m from the piece centre (< " + F(trunkClear) + ")");
                var worldNew = Map(t.position) + Vector3.up * shift; var rotNew = dq * t.rotation;
                sp.changes.Add(new Change
                {
                    step = "D8", key = key, kind = "pose", before = PoseText(t.localPosition, t.localRotation), after = PoseText(inv.MultiplyPoint3x4(worldNew), Quaternion.Inverse(rootRot) * rotNew), at = worldNew, state = "apply",
                    note = "offset=" + offset.ToString("R", Inv) + "|bottom - ground before " + F(before) + " [" + source + "]|bounds " + F(b.size.x) + " x " + F(b.size.y) + " x " + F(b.size.z) + " m|the rigid move alone would leave " + F(rigidGap) + " m|height correction " + F(shift) + " -> gap " + F(gap) + "|ground " + F(g0.y) + " -> " + F(g1.y) + "|nearest trunk " + F(trunk, "F1") + " m (" + trunkId + ")"
                });
            }
            sp.notes.Add(sid + ": " + pieces.Count + " piece(s); without the per-piece correction the worst piece would be " + F(worstRigid) + " m off its seat");
            if (fails.Count > 0) foreach (var f in fails) blocks.Add(sid + ": " + f);
        }

        static void VerifyD8(Ctx k, Action<bool, string> Row, Action<string> Info, StepPlan sink)
        {
            var rs = Req(k.Data, "reseat"); if (!Flag(rs, "enabled")) { Info("D8 off in data"); return; }
            float gapMax = Num(rs, "gap_max_m");
            foreach (var set in Arr(rs, "sets"))
            {
                string sid = Str(set, "id");
                if (!Flag(set, "enabled")) { Info("D8 " + sid + ": off in data"); continue; }
                string rootKey = Str(set, "root_key"); var root = Strict(k, rootKey, false);
                if (root == null) { Info("D8 " + sid + ": " + rootKey + " is not in this scene (skipped)"); continue; }
                var added = SkipOwn(k, root);
                try
                {
                    var live = k.Ledger.ops.Where(o => !o.reverted && o.status == "applied").SelectMany(o => o.changes).Where(c => c.step == "D8" && c.kind == "pose" && c.state == "apply").ToList();
                    if (!live.Any(c => c.key == rootKey)) { Info("D8 " + sid + ": no live apply in the ledger - the rows below describe the set where it stands"); }
                    float tx = At(Req(set, "target_xz"), 0), tz = At(Req(set, "target_xz"), 1), yawT = Num(set, "yaw");
                    float offXz = Harness303.Flat(root.position, new Vector3(tx, 0, tz)), offYaw = Mathf.Abs(Mathf.DeltaAngle(root.eulerAngles.y, yawT));
                    Row(offXz <= k.PoseTol && offYaw <= k.YawTol, "D8 " + sid + " root stands on the data place (" + F(offXz, "F3") + " m, " + F(offYaw, "F2") + " deg off; root " + V(root.position) + ")");
                    N(k, sink, "D8." + sid + ".root_x", root.position.x); N(k, sink, "D8." + sid + ".root_y", root.position.y); N(k, sink, "D8." + sid + ".root_z", root.position.z);
                    if (Ground(k, root.position.x, root.position.z, out var g, out float slope))
                    {
                        float rootGap = root.position.y - g.y - OptNum(set, "root_y_offset_m");
                        N(k, sink, "D8." + sid + ".slope", slope);
                        Row(Mathf.Abs(rootGap) <= Num(rs, "root_gap_max_m"), "D8 " + sid + " root y - ground " + F(rootGap, "F3") + " (|dy| ≤ " + F(Num(rs, "root_gap_max_m")) + "), slope " + F(slope, "F1") + " deg");
                    }
                    else Row(false, "D8 " + sid + " no ground under the root");
                    float road = CentreLine(new Vector2(root.position.x, root.position.z), BuildingAudit308.Corridors(k.Scene, k.Cfg), Num(k.Data, "corridor_run_gap_m"), out string roadId);
                    N(k, sink, "D8." + sid + ".road", road);
                    Row(road >= Num(set, "road_clear_m"), "D8 " + sid + " root " + F(road) + " m from the nearest route centre line (" + roadId + ", need ≥ " + F(Num(set, "road_clear_m")) + ")");
                    int seated = 0;
                    foreach (var ch in live.Where(c => c.key.StartsWith(rootKey + "/", StringComparison.Ordinal)))
                    {
                        string name = PieceName(ch.key); var t = BuildingAudit308.Resolve(k.Scene, ch.key);
                        // #308 fix 3 (D308-22): a piece ContentSeat308.Fix2 re-posed is seated by that op (its seat was taken with the children
                        // attached; a later Fix2 op may carry a child away - the wreck's roof - which changes the bounds read here). Fix2 verify
                        // judges its seat; the road rule below still measures every piece.
                        if (t != null && Fix2Reposed(k, ch.key, out string f2)) { seated++; Info("D8 " + sid + "/" + name + ": re-posed by Fix2 (" + f2 + ") - its seat is judged by ContentSeat308 Fix2 verify"); continue; }
                        if (t == null || !PieceBounds(t, out var b)) { Row(false, "D8 " + sid + "/" + name + ": the piece is gone or has no bounds"); continue; }
                        if (!OffsetOf(ch, out float offset)) { Row(false, "D8 " + sid + "/" + name + ": the ledger note carries no offset"); continue; }
                        if (!Ground(k, b.center.x, b.center.z, out var pg, out _)) { Row(false, "D8 " + sid + "/" + name + ": no ground under the piece"); continue; }
                        float gap = b.min.y - pg.y - offset; seated++;
                        N(k, sink, "D8." + sid + "." + name + ".offset", offset); N(k, sink, "D8." + sid + "." + name + ".gap", gap);
                        Row(Mathf.Abs(gap) <= gapMax, "D8 " + sid + "/" + name + ": bottom - ground " + F(b.min.y - pg.y) + ", seat " + F(offset) + " -> gap " + F(gap) + " (|gap| ≤ " + F(gapMax) + ")");
                    }
                    // D308-16 fix F8: the road rule holds for every piece, not only for the root. piece_road_clear_m = half of the
                    // "central 6 m of every registered route" of SPEC-ROAD-PROPS-264 (완료 조건): no collider of a piece nearer than that
                    // to a route centre line. Measured on the piece's solid colliders (its renderers when it has none), four corners + centre.
                    float pieceRoad = OptNum(set, "piece_road_clear_m");
                    if (pieceRoad > 0f)
                    {
                        var corridors = BuildingAudit308.Corridors(k.Scene, k.Cfg); float runGap = Num(k.Data, "corridor_run_gap_m"); float worst = float.PositiveInfinity; string worstName = "-";
                        foreach (var t in SetPieces(set, root))
                        {
                            if (!PieceSolidBounds(t, out var pb)) { Row(false, "D8 " + sid + "/" + t.name + ": no collider / renderer to measure against the road"); continue; }
                            float near = float.PositiveInfinity; string nearId = "";
                            foreach (var q in new[] { new Vector2(pb.min.x, pb.min.z), new Vector2(pb.min.x, pb.max.z), new Vector2(pb.max.x, pb.min.z), new Vector2(pb.max.x, pb.max.z), new Vector2(pb.center.x, pb.center.z) })
                            { float d = CentreLine(q, corridors, runGap, out string rid); if (d < near) { near = d; nearId = rid; } }
                            N(k, sink, "D8." + sid + "." + t.name + ".road", near);
                            if (near < worst) { worst = near; worstName = t.name; }
                            Row(near >= pieceRoad, "D8 " + sid + "/" + t.name + ": collider bounds " + F(near) + " m from the nearest route centre line (" + nearId + ", need ≥ " + F(pieceRoad) + "; pivot " + F(CentreLine(new Vector2(t.position.x, t.position.z), corridors, runGap, out _)) + " m)");
                        }
                        Info("D8 " + sid + ": nearest piece to a road centre line = " + worstName + " " + (float.IsPositiveInfinity(worst) ? "-" : F(worst) + " m") + " (rule " + F(pieceRoad) + " m = SPEC-ROAD-PROPS-264 'central 6 m'); a deficit is closed by moving target_xz along the road normal by that deficit, never by lowering the rule");
                    }
                    if (live.Any(c => c.key == rootKey)) Row(seated > 0, "D8 " + sid + ": " + seated + " piece(s) seated by the ledger");
                    int lights = root.GetComponentsInChildren<Light>(true).Count(l => l.enabled && l.gameObject.activeInHierarchy);
                    Info("D8 " + sid + ": lights on under the set " + lights + " (this step adds none); the dressing sheets' PreserveArea / StoryCluster rows of the #264 build and the NavMesh carve are not moved here");
                }
                finally { Unskip(k, added); }
            }
        }
    }
}
