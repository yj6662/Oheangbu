using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 seat fix: the scene steps (plan + post-conditions). D1 inn, D2 keeper, D3 lantern caps, D5 firewood, D6 old road ribbons.
    // D308-16 relayout: D5 reads a piece's "before" place from its prop row in content308_scene.json when the data says
    // "expect_before": "scene_row" (the anchor content point + the row offset), and is refused while the logger point is not near
    // the stack (firewood.anchor_max_m). D7 / D8 live in ContentSeat308.Hinge.cs / ContentSeat308.Props.cs.
    // A plan never changes anything; every measured number is printed as "num <name> <value>" with the names the offline dry run
    // (Tools/Art/contentseat308_dry.py) uses, so the two can be laid side by side.
    public static partial class ContentSeat308
    {
        // ------------------------------------------------------------------ shared measurements

        // the rest compound frame exactly as Content308.RestOps308 builds it: origin = the ground under the content point, front = feet
        static void RestFrame(Ctx k, string restId, out Vector3 g, out float yaw, out WorldMacroPlaytestSO.CheckpointSpec cp)
        {
            var point = Array.Find(k.Content.Points ?? Array.Empty<PrologueContentSO.Point>(), p => p != null && p.Id == restId);
            cp = Array.Find(k.Content.Checkpoints ?? Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>(), p => p != null && p.Id == restId);
            if (point == null || cp == null) throw new Refuse("rest " + restId + ": point / checkpoint not in " + AssetDatabase.GetAssetPath(k.Content) + " (Content308 content-apply first)");
            if (!Ground(k, point.Position.x, point.Position.z, out g, out _)) throw new Refuse("rest " + restId + ": no ground under " + V(point.Position));
            yaw = Mathf.Repeat(Harness303.YawTo(g, cp.Feet), 360f);
        }

        // the inn part row of content308_scene.json (the row Content308 scene-apply enforces)
        static JToken InnPart(Ctx k, JToken inn)
        {
            string restId = Str(inn, "rest_id");
            var rest = Arr(k.SceneData, "rests").FirstOrDefault(r => Opt(r, "id") == restId) ?? throw new Refuse("scene data: rest " + restId + " missing");
            return Arr(rest, "parts").FirstOrDefault() ?? throw new Refuse("scene data: rest " + restId + " has no part");
        }

        // world pose Content308.Place308 gives the inn for a rest-frame offset / part yaw: at = g + R(yaw) * offset, y = the ground under
        // the part + offsetY, rotation = Euler(0, yaw + partYaw - source_front_yaw, 0) * source rotation
        static bool InnTarget(Ctx k, JToken inn, float ox, float oz, float partYaw, float offsetY, out Vector3 pos, out Quaternion rot, out float groundAtPivot, out string why)
        {
            pos = default; rot = Quaternion.identity; groundAtPivot = float.NaN; why = null;
            RestFrame(k, Str(inn, "rest_id"), out var g, out float yaw, out _);
            var part = InnPart(k, inn);
            string sourceKey = Str(Req(k.Data, "lanterns"), "source_inn_key");
            if (Opt(part, "source_scene") != sourceKey) { why = "scene data source_scene '" + Opt(part, "source_scene") + "' is not lanterns.source_inn_key '" + sourceKey + "'"; return false; }
            var src = Strict(k, sourceKey);
            float front = part["source_front_yaw"] != null ? part["source_front_yaw"].Value<float>() : src.eulerAngles.y;
            rot = Quaternion.Euler(0, yaw + partYaw - front, 0) * src.rotation;
            var at = g + Quaternion.Euler(0, yaw, 0) * new Vector3(ox, 0, oz);
            if (!Ground(k, at.x, at.z, out var pg, out _)) { why = "no ground under the part at " + V(at); return false; }
            groundAtPivot = pg.y; pos = new Vector3(at.x, pg.y + offsetY, at.z);
            return true;
        }

        sealed class HouseShape
        {
            public float[] Plinth, Roof; public float TopRel, Measured = float.NaN; public string TopNote = "", Suspect;
            public readonly List<(string name, float topRel)> Steps = new List<(string, float)>();
        }

        // what moves rigidly with the house, read where it stands now: plinth top above the pivot (plate collider), entry step tops.
        // The plinth top is the median of four corner probes (inset from the plinth corners). The middle of the plate is not probed:
        // the plate's renderer bounds are 3.1 m tall, so a ray there may meet a higher face. A reading that is not near the data value
        // (plinth_top_m ± plinth_top_tol_m), or corner readings that disagree by more than that, make the measurement "suspect": D1 then
        // refuses to move the house until someone has looked (correct plinth_top_m, or set inn.plinth_top_use_data).
        static HouseShape MeasureHouse(Ctx k, JToken inn, Transform house, Transform plate)
        {
            var m = new HouseShape { Plinth = Rect(inn, "plinth_rect"), Roof = Rect(inn, "roof_rect") };
            float inset = Num(inn, "plinth_probe_inset_m"), up = Num(inn, "plinth_ray_up_m"), len = Num(inn, "plinth_ray_len_m"), minNy = Num(inn, "plinth_normal_y_min");
            float data = Num(inn, "plinth_top_m"), tolTop = Num(inn, "plinth_top_tol_m"); float hyaw = house.eulerAngles.y; var tops = new List<float>();
            var cols = plate.GetComponents<Collider>().Where(c => c.enabled).ToArray();
            foreach (var (lx, lz) in new[] { (m.Plinth[0] + inset, m.Plinth[2] + inset), (m.Plinth[1] - inset, m.Plinth[2] + inset), (m.Plinth[1] - inset, m.Plinth[3] - inset), (m.Plinth[0] + inset, m.Plinth[3] - inset) })
            {
                var o = house.position + Local(hyaw, lx, lz); var ray = new Ray(new Vector3(o.x, house.position.y + up, o.z), Vector3.down);
                float best = float.NaN;
                foreach (var c in cols) if (c.Raycast(ray, out var hit, len) && hit.normal.y > minNy && (float.IsNaN(best) || hit.point.y > best)) best = hit.point.y;
                if (!float.IsNaN(best)) tops.Add(best - house.position.y);
            }
            if (tops.Count > 0)
            {
                tops.Sort(); int h = tops.Count / 2;
                m.Measured = tops.Count % 2 == 1 ? tops[h] : (tops[h - 1] + tops[h]) * .5f;
                m.TopNote = "measured on the plate collider at " + tops.Count + " corner probe(s), " + F(tops[0]) + " .. " + F(tops[tops.Count - 1]) + ", median " + F(m.Measured) + "; data plinth_top_m " + F(data);
                bool off = Mathf.Abs(m.Measured - data) > tolTop || tops[tops.Count - 1] - tops[0] > tolTop;
                if (Flag(inn, "plinth_top_use_data")) { m.TopRel = data; m.TopNote += " - the DATA value is used (inn.plinth_top_use_data)"; }
                else
                {
                    m.TopRel = m.Measured;
                    if (off) m.Suspect = "the plinth top measured on the plate collider (" + F(tops[0]) + " .. " + F(tops[tops.Count - 1]) + ", median " + F(m.Measured) + ") is not within " + F(tolTop) + " m of the data value " + F(data) + ": the burial check would mean nothing - look at the plate, then correct inn.plinth_top_m or set inn.plinth_top_use_data true";
                }
            }
            else { m.TopRel = data; m.TopNote = "NOT measured (no plate collider answered): data plinth_top_m " + F(m.TopRel) + " [I]"; }
            foreach (var sk in Arr(inn, "step_keys"))
            {
                var st = Strict(k, sk.Value<string>(), false); var r = st != null ? st.GetComponent<Renderer>() : null;
                if (r != null) m.Steps.Add((st.name, r.bounds.max.y - house.position.y));
            }
            return m;
        }

        sealed class HouseEval
        {
            public Vector3 Pivot; public float Yaw, Ground, Trunk = float.PositiveInfinity, Bury, Foot, FirstRise = float.NaN, Corridor, FeetOut;
            public string TrunkId = "", CorridorId = "", Blocker = ""; public float[] Corner = new float[4];   // FL FR BL BR: ground - pivot
            public List<string> Fails = new List<string>();
        }

        // the limits of D1 for the house standing at (pivot, yaw): trunks, plinth burial, first step rise, route centre lines, other colliders
        static HouseEval EvalHouse(Ctx k, JToken inn, HouseShape m, Transform house, Vector3 pivot, float hyaw, WorldMacroPlaytestSO.CheckpointSpec cp)
        {
            var e = new HouseEval { Pivot = pivot, Yaw = hyaw, Ground = GroundY(k, pivot.x, pivot.z) };
            foreach (var (p, id) in Trunks(k, pivot, Num(inn, "trunk_search_m")))
            {
                var l = ToLocal(hyaw, pivot, p); float d = Mathf.Min(RectOut(m.Plinth, l), RectOut(m.Roof, l));
                if (d < e.Trunk) { e.Trunk = d; e.TrunkId = id; }
            }
            if (e.Trunk < Num(inn, "trunk_margin_m")) e.Fails.Add("trunk " + e.TrunkId + " " + F(e.Trunk) + " m from the outline (< " + F(Num(inn, "trunk_margin_m")) + ")");
            var corners = new[] { (m.Plinth[0], m.Plinth[3]), (m.Plinth[1], m.Plinth[3]), (m.Plinth[0], m.Plinth[2]), (m.Plinth[1], m.Plinth[2]) };
            e.Bury = float.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                var w = pivot + Local(hyaw, corners[i].Item1, corners[i].Item2); float gy = GroundY(k, w.x, w.z);
                e.Corner[i] = gy - pivot.y; if (float.IsNaN(gy)) e.Fails.Add("no ground under a plinth corner");
                e.Bury = Mathf.Max(e.Bury, e.Corner[i] - m.TopRel);
            }
            if (e.Bury > Num(inn, "bury_max_m")) e.Fails.Add("the ground stands " + F(e.Bury) + " m over the plinth top at a corner (> " + F(Num(inn, "bury_max_m")) + ")");
            var foot = pivot + Local(hyaw, At(Req(inn, "step_foot_local"), 0), At(Req(inn, "step_foot_local"), 1));
            e.Foot = GroundY(k, foot.x, foot.z) - pivot.y;
            foreach (var s in m.Steps) { float rise = s.topRel - e.Foot; if (rise > 0 && (float.IsNaN(e.FirstRise) || rise < e.FirstRise)) e.FirstRise = rise; }
            if (float.IsNaN(e.FirstRise)) e.Fails.Add("every entry step is under the ground at the step foot");
            else if (e.FirstRise > Num(inn, "step_limit_m")) e.Fails.Add("first step " + F(e.FirstRise) + " m above the ground (> " + F(Num(inn, "step_limit_m")) + ")");
            var corridors = BuildingAudit308.Corridors(k.Scene, k.Cfg); float gap = Num(k.Data, "corridor_run_gap_m");
            e.Corridor = float.PositiveInfinity;
            for (float u = -1; u <= 1.001f; u += .5f)
                for (float w = -1; w <= 1.001f; w += .5f)
                {
                    if (Mathf.Abs(u) < .99f && Mathf.Abs(w) < .99f) continue;
                    var o = pivot + Local(hyaw, (m.Plinth[0] + m.Plinth[1]) * .5f + (m.Plinth[1] - m.Plinth[0]) * .5f * u, (m.Plinth[2] + m.Plinth[3]) * .5f + (m.Plinth[3] - m.Plinth[2]) * .5f * w);
                    float d = CentreLine(new Vector2(o.x, o.z), corridors, gap, out string cid);
                    if (d < e.Corridor) { e.Corridor = d; e.CorridorId = cid; }
                }
            if (e.Corridor < Num(inn, "corridor_clear_m")) e.Fails.Add("outline " + F(e.Corridor) + " m from the centre line of " + e.CorridorId + " (< " + F(Num(inn, "corridor_clear_m")) + ")");
            // other colliders inside the plinth + apron, above the plinth top (the house's own, the terrain, actors and previews do not count)
            float apron = Num(inn, "overlap_apron_m"), h = Num(inn, "overlap_height_m");
            var centre = pivot + Local(hyaw, (m.Plinth[0] + m.Plinth[1]) * .5f, (m.Plinth[2] + m.Plinth[3]) * .5f) + Vector3.up * (m.TopRel + h + Num(inn, "overlap_lift_m"));
            var half = new Vector3((m.Plinth[1] - m.Plinth[0]) * .5f + apron, h, (m.Plinth[3] - m.Plinth[2]) * .5f + apron);
            // the keeper's own body is placed by D2 (1.35 m in front of the plinth edge, inside the apron on purpose)
            var keeperBody = BuildingAudit308.Resolve(k.Scene, Str(Req(k.Data, "keeper"), "body_key"));
            foreach (var c in Physics.OverlapBox(centre, half, Quaternion.Euler(0, hyaw, 0), ~0, QueryTriggerInteraction.Ignore))
            {
                if (c == null || c.transform.IsChildOf(house) || BuildingAudit308.TerrainLike(c) || k.Skip.Contains(c)) continue;
                if (keeperBody != null && c.transform.IsChildOf(keeperBody)) continue;
                bool preview = false; for (var t = c.transform; t != null; t = t.parent) if ((t.gameObject.hideFlags & HideFlags.DontSave) != 0) preview = true;
                if (preview) continue;
                e.Blocker = BuildingAudit308.KeyOf(c.transform); break;
            }
            if (e.Blocker.Length > 0) e.Fails.Add("another collider stands inside the plinth + " + F(apron, "F1") + " m: " + e.Blocker);
            e.FeetOut = cp != null ? RectOut(m.Plinth, ToLocal(hyaw, pivot, cp.Feet)) : float.PositiveInfinity;
            if (e.FeetOut < Num(Req(k.Data, "keeper"), "feet_clear_m")) e.Fails.Add("the respawn feet are " + F(e.FeetOut) + " m from the plinth outline");
            return e;
        }
        static string EvalText(HouseEval e, HouseShape m)
            => "pivot " + V(e.Pivot) + " yaw " + F(e.Yaw, "F1") + ", ground at the pivot " + F(e.Ground) + " | trunk clearance " + F(e.Trunk) + " m (" + e.TrunkId + ") | corners ground - pivot FL " + F(e.Corner[0]) + " FR " + F(e.Corner[1]) + " BL " + F(e.Corner[2]) + " BR " + F(e.Corner[3]) +
               ", plinth top " + F(m.TopRel) + " -> deepest bury " + F(e.Bury) + " | step foot ground - pivot " + F(e.Foot) + ", first rise " + F(e.FirstRise) + " | route centre line " + F(e.Corridor) + " m (" + e.CorridorId + ") | feet " + F(e.FeetOut) + " m outside the plinth";

        static void HouseNumbers(Ctx k, StepPlan sp, HouseEval e)
        {
            N(k, sp, "D1.pivot_x", e.Pivot.x); N(k, sp, "D1.pivot_y", e.Pivot.y); N(k, sp, "D1.pivot_z", e.Pivot.z); N(k, sp, "D1.yaw", e.Yaw);
            N(k, sp, "D1.trunk_clearance", e.Trunk); N(k, sp, "D1.corner_FL", e.Corner[0]); N(k, sp, "D1.corner_FR", e.Corner[1]); N(k, sp, "D1.corner_BL", e.Corner[2]); N(k, sp, "D1.corner_BR", e.Corner[3]);
            N(k, sp, "D1.step_foot", e.Foot); N(k, sp, "D1.first_rise", e.FirstRise); N(k, sp, "D1.corridor", e.Corridor);
        }

        static string SkirtMeshPath(JToken inn, Transform plate)
        {
            // the name GroundFit299.StoneSkirt308 gives the mesh (its hierarchy path, '/' -> '_', last 90 characters)
            string key = BuildingAudit308.PathOf(plate).Replace('/', '_');
            return Str(inn, "skirt_folder") + "/" + key.Substring(Math.Max(0, key.Length - 90)) + "_skirt308.asset";
        }
        static string SkirtPoseHash(Vector3 pos, Quaternion rot, Vector3 scale) => string.Format(Inv, "{0:F3},{1:F3},{2:F3};{3:F2};{4:F3},{5:F3},{6:F3}", pos.x, pos.y, pos.z, rot.eulerAngles.y, scale.x, scale.y, scale.z);

        // ------------------------------------------------------------------ D1 hunter inn: move, lower, new stone skirt

        static StepPlan PlanD1(Ctx k, int candidate, string dataState)
        {
            var inn = Req(k.Data, "inn"); var tol = Req(k.Data, "tolerances");
            var sp = new StepPlan { step = "D1", detail = "hunter inn (" + Str(inn, "mode") + ")" };
            if (!Flag(inn, "enabled")) { sp.status = "off"; sp.detail = "off in data"; return sp; }
            sp.notes.Add("scene data: " + dataState);
            var cands = Candidates(inn);
            if (candidate < 0) { sp.status = "blocked"; sp.detail = "data mismatch - content308_scene.json does not name a candidate; run: python Tools/Art/contentseat308_dry.py patch-data --write"; return sp; }
            var holder = Strict(k, Str(inn, "holder_key")); var house = Strict(k, Str(inn, "inn_key")); var plate = Strict(k, Str(inn, "plate_key"));
            var oldSkirt = Strict(k, Str(inn, "old_skirt_key"), false);
            if (!UnderOwnRoot(k, house)) { sp.status = "blocked"; sp.detail = house.name + " is not under " + Str(k.Data, "root"); return sp; }
            RestFrame(k, Str(inn, "rest_id"), out var g, out float yaw, out var cp);
            if (Vector3.Distance(holder.position, g) > k.PoseTol || Mathf.Abs(Mathf.DeltaAngle(holder.eulerAngles.y, yaw)) > k.YawTol)
                sp.notes.Add("the rest holder " + V(holder.position) + " yaw " + F(holder.eulerAngles.y, "F1") + " is not on the rest frame " + V(g) + " yaw " + F(yaw, "F1") + ": Content308 scene-apply will re-seat the holder (children keep their world pose)");
            float embed = cands[candidate].embed; var shape = MeasureHouse(k, inn, house, plate);
            sp.notes.Add("plinth top above the pivot " + F(shape.TopRel) + " m (" + shape.TopNote + "); entry step tops " + string.Join(", ", shape.Steps.Select(s => s.name + " " + F(s.topRel))));
            if (shape.Suspect != null) sp.notes.Add("SUSPECT: " + shape.Suspect);
            // lower_in_place stays a data switch: it is refused while a trunk stands inside the outline + margin
            var (cx, cz, cyaw, _) = cands[candidate];
            if (!InnTarget(k, inn, cx, cz, cyaw, -embed, out var pos, out var rot, out float groundAtPivot, out string why)) { sp.status = "blocked"; sp.detail = why; return sp; }
            float hyaw = rot.eulerAngles.y;
            var eval = EvalHouse(k, inn, shape, house, pos, hyaw, cp);
            sp.notes.Add("candidate " + candidate + " (in the scene data): " + EvalText(eval, shape));
            HouseNumbers(k, sp, eval); N(k, sp, "D1.plinth_top", shape.TopRel);
            for (int i = 0; i < cands.Count; i++)
            {
                if (i == candidate) continue;
                if (!InnTarget(k, inn, cands[i].x, cands[i].z, cands[i].yaw, -cands[i].embed, out var p2, out var r2, out _, out string w2)) { sp.notes.Add("candidate " + i + ": " + w2); continue; }
                var e2 = EvalHouse(k, inn, shape, house, p2, r2.eulerAngles.y, cp);
                sp.notes.Add("candidate " + i + " (embed " + F(cands[i].embed) + ", not chosen): " + (e2.Fails.Count == 0 ? "passes" : "fails - " + string.Join("; ", e2.Fails)) + " | " + EvalText(e2, shape));
            }
            // ---- pose
            bool atTarget = Vector3.Distance(house.position, pos) <= k.PoseTol && Quaternion.Angle(house.rotation, rot) <= k.YawTol;
            var prior = Prior(k.Ledger, "D1", BuildingAudit308.KeyOf(house), "pose");
            var poseCh = new Change { step = "D1", key = BuildingAudit308.KeyOf(house), kind = "pose", before = PoseText(house.localPosition, house.localRotation), after = LocalPoseFor(house, pos, rot), at = pos };
            if (atTarget)
            {
                poseCh.state = "already";
                if (prior != null) poseCh.before = prior.before;
                else { poseCh.before = ""; poseCh.note = "the inn already stands on the target and this ledger has no before value (Content308 scene-apply moved it from the patched scene data?): revert = restore content308_scene.json + Content308 scene-apply"; }
            }
            else
            {
                var eb = Req(inn, "expect_before"); bool known = prior != null && SamePose(k, house, prior.before);
                if (!known && InnTarget(k, inn, At(Req(eb, "offset"), 0), At(Req(eb, "offset"), 1), Num(eb, "yaw"), 0f, out var bp, out var br, out _, out _))
                    known = Harness303.Flat(house.position, bp) <= Num(tol, "before_xz_m") && Quaternion.Angle(house.rotation, br) <= Num(tol, "before_yaw_deg");
                poseCh.state = known ? "apply" : "mismatch";
                poseCh.note = known ? "move " + F(Harness303.Flat(house.position, pos)) + " m, yaw " + F(house.eulerAngles.y, "F1") + " -> " + F(hyaw, "F1") + ", pivot y " + F(house.position.y) + " -> " + F(pos.y) + " (ground " + F(groundAtPivot) + " - " + F(embed) + ")"
                                    : "the inn stands at " + V(house.position) + " yaw " + F(house.eulerAngles.y, "F1") + ": neither the content-pass place (expect_before) nor the target " + V(pos);
                if (eval.Fails.Count > 0 && known) { poseCh.state = "blocked"; sp.status = "blocked"; sp.detail = "candidate " + candidate + " fails: " + string.Join("; ", eval.Fails) + " - choose another candidate (patch-data --candidate N --write) or edit contentseat308.json"; }
                else if (shape.Suspect != null && known) { poseCh.state = "blocked"; sp.status = "blocked"; sp.detail = shape.Suspect; }
            }
            sp.changes.Add(poseCh);
            // ---- old skirt (the mesh was draped on the original's ground): renderer off
            if (oldSkirt == null) sp.notes.Add("no " + Str(inn, "old_skirt_key") + " in this scene");
            else
            {
                var r = oldSkirt.GetComponent<MeshRenderer>();
                if (r == null) sp.notes.Add("the old skirt has no MeshRenderer");
                else sp.changes.Add(new Change { step = "D1", key = BuildingAudit308.KeyOf(oldSkirt), kind = "enable", before = "1", after = "0", at = oldSkirt.position, state = r.enabled ? "apply" : "already", note = "StoneSkirt299 renderer (mesh shared with the original, never edited)" });
            }
            // ---- new skirt on the new ground
            string material = Str(inn, "skirt_material"); var mat = AssetDatabase.LoadAssetAtPath<Material>(material);
            string meshPath = SkirtMeshPath(inn, plate); string poseHash = SkirtPoseHash(pos, rot, house.lossyScale);
            var skirt = new Change { step = "D1", key = BuildingAudit308.KeyOf(plate), kind = "skirt", before = "", after = meshPath, note = material + "|" + poseHash, at = pos };
            var skirtHolder = house.Find(Str(inn, "skirt_holder")); var shared = LoadShared(); bool meshExists = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null;
            bool recordOk = shared.skirtMesh == meshPath && shared.skirtPose == poseHash;
            if (mat == null) { skirt.state = "blocked"; sp.status = "blocked"; sp.detail += "; skirt material missing " + material; }
            else if (mat.IsKeywordEnabled("_EMISSION")) { skirt.state = "blocked"; sp.status = "blocked"; sp.detail += "; skirt material is emissive (ArtAudio light rule)"; }
            else if (skirtHolder != null)
            {
                var f = skirtHolder.GetComponent<MeshFilter>(); string have = f != null && f.sharedMesh != null ? AssetDatabase.GetAssetPath(f.sharedMesh) : "";
                skirt.state = have == meshPath && recordOk ? "already" : "mismatch";
                if (skirt.state == "mismatch") skirt.note += "|a StoneSkirt308 holder exists with " + (have == meshPath ? "a mesh made for another pose (" + shared.skirtPose + ")" : "another mesh " + have) + ": revert D1 in the three scenes, then skirt-reset";
            }
            else if (SkirtRecord(k.Ledger, skirt.key) is Change none && none.state == "info" && none.note.Split('|').Length > 1 && none.note.Split('|')[1] == poseHash)
            { skirt.state = "already"; skirt.after = ""; skirt.note += "|no skirt was needed at this pose (ledger: the plate edge sits on the ground all round)"; }
            else if (meshExists && !recordOk) { skirt.state = "blocked"; sp.status = "blocked"; sp.detail += "; the skirt mesh " + meshPath + " exists but was made for " + (string.IsNullOrEmpty(shared.skirtPose) ? "an unrecorded pose" : "pose " + shared.skirtPose) + " (now " + poseHash + "): StoneSkirt308 would reuse it - revert D1 in the scenes that use it, then skirt-reset"; }
            else { skirt.state = "apply"; skirt.note += meshExists ? "|reuse (same pose as the scene that made it)" : "|new mesh"; }
            sp.changes.Add(skirt);
            Settle(sp);
            return sp;
        }

        // ------------------------------------------------------------------ D2 keeper: content point + body in front of the moved house

        static bool KeeperTarget(Ctx k, int candidate, out Vector3 g, out Quaternion rot, out float yaw, out WorldMacroPlaytestSO.CheckpointSpec cp, out string why)
        {
            g = default; rot = Quaternion.identity; yaw = 0; cp = null;
            var inn = Req(k.Data, "inn"); var kp = Req(k.Data, "keeper"); var cands = Candidates(inn);
            if (!InnTarget(k, inn, cands[candidate].x, cands[candidate].z, cands[candidate].yaw, -cands[candidate].embed, out var pos, out var hrot, out _, out why)) return false;
            var xz = pos + Local(hrot.eulerAngles.y, At(Req(kp, "inn_local"), 0), At(Req(kp, "inn_local"), 1));
            if (!Ground(k, xz.x, xz.z, out g, out _)) { why = "no ground under the keeper point " + V(xz); return false; }
            string face = Str(kp, "face_checkpoint");
            cp = Array.Find(k.Content.Checkpoints ?? Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>(), p => p != null && p.Id == face);
            if (cp == null) { why = "checkpoint " + face + " not in the content"; return false; }
            yaw = Mathf.Repeat(Harness303.YawTo(g, cp.Feet), 360f);
            // Content308.Place308: no source_front_yaw on an npc row = the source's own yaw is its front
            string id = Str(kp, "id");
            var row = Arr(k.SceneData["npcs"], "clones").FirstOrDefault(n => Opt(n, "id") == id);
            var src = row != null && Opt(row, "source_scene") != null ? Strict(k, Opt(row, "source_scene"), false) : null;
            float front = row != null && row["source_front_yaw"] != null ? row["source_front_yaw"].Value<float>() : src != null ? src.eulerAngles.y : 0f;
            rot = src != null ? Quaternion.Euler(0, yaw - front, 0) * src.rotation : Quaternion.Euler(0, yaw, 0);
            return true;
        }

        // d1InRun = D1 is one of the steps of this apply (the plan command lists every step)
        static StepPlan PlanD2(Ctx k, int candidate, bool d1InRun)
        {
            var kp = Req(k.Data, "keeper"); var tol = Req(k.Data, "tolerances"); var inn = Req(k.Data, "inn");
            var sp = new StepPlan { step = "D2", detail = "inn keeper point + body" };
            if (!Flag(kp, "enabled")) { sp.status = "off"; sp.detail = "off in data"; return sp; }
            if (candidate < 0) { sp.status = "blocked"; sp.detail = "data mismatch - the keeper point is measured from the inn candidate (see D1)"; return sp; }
            string id = Str(kp, "id");
            var point = Array.Find(k.Content.Points ?? Array.Empty<PrologueContentSO.Point>(), p => p != null && p.Id == id);
            if (point == null) { sp.status = "blocked"; sp.detail = "content point " + id + " missing (Content308 content-apply first)"; return sp; }
            var body = Strict(k, Str(kp, "body_key"));
            if (!KeeperTarget(k, candidate, out var g, out var rot, out float yaw, out var cp, out string why)) { sp.status = "blocked"; sp.detail = why; return sp; }
            // the keeper point is measured from the candidate TARGET pose of the house, not from where the house stands: without D1 the
            // keeper would be put in front of a place the house is not at
            var house = Strict(k, Str(inn, "inn_key")); var hc = Candidates(inn)[candidate];
            if (!InnTarget(k, inn, hc.x, hc.z, hc.yaw, -hc.embed, out var hpos, out var hrot, out _, out string hwhy)) { sp.status = "blocked"; sp.detail = hwhy; return sp; }
            bool houseThere = Vector3.Distance(house.position, hpos) <= k.PoseTol && Quaternion.Angle(house.rotation, hrot) <= k.YawTol;
            if (!houseThere && !d1InRun) { sp.status = "blocked"; sp.detail = "the inn is " + F(Vector3.Distance(house.position, hpos)) + " m off its target pose and D1 is not part of this apply: the keeper point is measured from the target pose - apply D1 first or together (apply:" + k.Alias + ":D1,D2)"; return sp; }
            float feetClear = Num(kp, "feet_clear_m"); var fails = new List<string>();
            foreach (var spot in k.Content.Checkpoints ?? Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>())
                if (spot != null && spot.IsConfigured && Harness303.Flat(spot.Feet, g) < feetClear) fails.Add(F(Harness303.Flat(spot.Feet, g)) + " m from the respawn feet of " + spot.Id);
            var spec = new Vector3(At(Req(kp, "spec_xz"), 0), 0, At(Req(kp, "spec_xz"), 1));
            if (Harness303.Flat(g, spec) > Num(kp, "keep_near_m")) fails.Add(F(Harness303.Flat(g, spec)) + " m from the spec place (> keep_near_m: content-apply would write the spec place again)");
            float outPlinth = RectOut(Rect(inn, "plinth_rect"), new Vector2(At(Req(kp, "inn_local"), 0), At(Req(kp, "inn_local"), 1)));
            if (outPlinth < Num(kp, "plinth_clear_m")) fails.Add("only " + F(outPlinth) + " m outside the plinth outline");
            sp.notes.Add("new point " + V(g) + " yaw " + F(yaw, "F1") + " | to the respawn feet " + F(Harness303.Flat(g, cp.Feet)) + " m | from the spec place " + F(Harness303.Flat(g, spec)) + " m | outside the plinth " + F(outPlinth) + " m | point now " + V(point.Position) + ", body now " + V(body.position) + " yaw " + F(body.eulerAngles.y, "F1"));
            N(k, sp, "D2.x", g.x); N(k, sp, "D2.y", g.y); N(k, sp, "D2.z", g.z); N(k, sp, "D2.yaw", yaw); N(k, sp, "D2.feet_gap", Harness303.Flat(g, cp.Feet));
            if (fails.Count > 0) { sp.status = "blocked"; sp.detail = string.Join("; ", fails); return sp; }
            var before = new Vector3(At(Req(kp, "expect_before_xz"), 0), 0, At(Req(kp, "expect_before_xz"), 1)); float bxz = Num(tol, "before_xz_m"), bodyTol = Num(kp, "body_tol_m");
            // ---- content point (this scene's own content asset)
            string pkey = AssetDatabase.GetAssetPath(k.Content) + "|" + id;
            var pc = new Change { step = "D2", key = pkey, kind = "point", before = Vec(point.Position), after = Vec(g), at = g };
            var pprior = Prior(k.Ledger, "D2", pkey, "point");
            if (Vector3.Distance(point.Position, g) <= bodyTol) { pc.state = "already"; pc.before = pprior != null ? pprior.before : ""; }
            else if (Harness303.Flat(point.Position, before) <= bxz || (pprior != null && ParseVec(pprior.before, out var pb) && Vector3.Distance(point.Position, pb) <= bodyTol)) { pc.state = "apply"; pc.note = "move " + F(Harness303.Flat(point.Position, g)) + " m"; }
            else { pc.state = "mismatch"; pc.note = "the point is at " + V(point.Position) + ": neither the content-pass place nor the target"; }
            sp.changes.Add(pc);
            // ---- body (Content308 keeps the body on its point; the yaw rule is the same face_checkpoint rule)
            var bc = new Change { step = "D2", key = BuildingAudit308.KeyOf(body), kind = "pose", before = PoseText(body.localPosition, body.localRotation), after = LocalPoseFor(body, g, rot), at = g };
            var bprior = Prior(k.Ledger, "D2", bc.key, "pose");
            if (Vector3.Distance(body.position, g) <= k.PoseTol && Quaternion.Angle(body.rotation, rot) <= k.YawTol) { bc.state = "already"; bc.before = bprior != null ? bprior.before : ""; }
            else if (Harness303.Flat(body.position, before) <= bxz || (bprior != null && SamePose(k, body, bprior.before))) { bc.state = "apply"; bc.note = "yaw " + F(body.eulerAngles.y, "F1") + " -> " + F(yaw, "F1"); }
            else { bc.state = "mismatch"; bc.note = "the body is at " + V(body.position) + ": neither the content-pass place nor the target"; }
            sp.changes.Add(bc);
            Settle(sp);
            return sp;
        }

        // ------------------------------------------------------------------ D3 porch lantern caps (the BuildingFix308 F5 rule on the clone)

        static void LanternCount(Ctx k, out int nullEnabled, out int nullDisabled, List<string> enabledKeys)
        {
            nullEnabled = nullDisabled = 0;
            var inn = Req(k.Data, "inn"); var ln = Req(k.Data, "lanterns");
            foreach (var lrel in Arr(ln, "lantern_rel"))
            {
                var lantern = Strict(k, Str(inn, "inn_key") + "/" + lrel.Value<string>(), false); if (lantern == null) continue;
                foreach (var f in lantern.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (f.sharedMesh != null) continue;
                    var r = f.GetComponent<MeshRenderer>();
                    if (r != null && r.enabled) { nullEnabled++; enabledKeys?.Add(BuildingAudit308.KeyOf(f.transform)); } else nullDisabled++;
                }
            }
        }

        static StepPlan PlanD3(Ctx k)
        {
            var inn = Req(k.Data, "inn"); var ln = Req(k.Data, "lanterns");
            var sp = new StepPlan { step = "D3", detail = "porch lantern caps" };
            if (!Flag(ln, "enabled")) { sp.status = "off"; sp.detail = "off in data"; return sp; }
            string innKey = Str(inn, "inn_key"), srcKey = Str(ln, "source_inn_key"); int up = Mathf.RoundToInt(Num(ln, "support_levels_up")); float sizeTol = Num(ln, "size_tol_m");
            var listed = new HashSet<string>();
            foreach (var lrel in Arr(ln, "lantern_rel"))
                foreach (var srel in Arr(ln, "surface_rel"))
                {
                    string rel = lrel.Value<string>() + "/" + srel.Value<string>(); string key = innKey + "/" + rel; listed.Add(key);
                    var t = Strict(k, key, false);
                    if (t == null) { sp.notes.Add("absent " + key); continue; }
                    var f = t.GetComponent<MeshFilter>(); var r = t.GetComponent<MeshRenderer>();
                    if (f == null || r == null) { sp.notes.Add(key + ": no MeshFilter / MeshRenderer"); continue; }
                    var prior = Prior(k.Ledger, "D3", key, "mesh"); string now = MeshState(f);
                    if (prior != null)
                    {
                        sp.changes.Add(new Change { step = "D3", key = key, kind = "mesh", before = prior.before, after = prior.after, at = t.position, state = now == prior.after ? "already" : now == prior.before ? "apply" : "mismatch", note = now == prior.after || now == prior.before ? "ledger" : "mesh now " + now });
                        continue;
                    }
                    if (f.sharedMesh != null) { sp.notes.Add(rel + ": has a mesh (" + now + ") - not a target"); continue; }
                    if (!r.enabled) { sp.notes.Add(rel + ": renderer off - not a defect"); continue; }
                    var support = t; for (int i = 0; i < up && support.parent != null; i++) support = support.parent;
                    var origin = Strict(k, srcKey + "/" + rel, false); var of = origin != null ? origin.GetComponent<MeshFilter>() : null;
                    var osupport = origin; for (int i = 0; i < up && osupport != null && osupport.parent != null; i++) osupport = osupport.parent;
                    bool sameSize = osupport != null && (osupport.lossyScale - support.lossyScale).magnitude < sizeTol;
                    if (of != null && of.sharedMesh != null && of.sharedMesh.vertexCount > 0 && sameSize)
                    {
                        sp.changes.Add(new Change { step = "D3", key = key, kind = "mesh", before = "", after = MeshState(of), at = t.position, state = "apply", note = "the mesh the original's same cap uses after F5 (support " + support.lossyScale.ToString("F3") + ")" });
                        continue;
                    }
                    // fallback: the F5 generator rule on this support (the original has no mesh here, or another size)
                    string name = Str(ln, "fallback_prefix") + Hash128.Compute(BuildingAudit308.KeyOf(support) + "|" + support.lossyScale.ToString("R") + "|" + Str(ln, "fallback_index"));
                    string path = k.Cfg.fix.f5.meshFolder + "/" + name + ".asset";
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(k.Cfg.fix.f5.sourcePrefab) == null) { sp.status = "blocked"; sp.detail = "the original has no usable mesh for " + rel + " and the F5 source prefab is missing " + k.Cfg.fix.f5.sourcePrefab; return sp; }
                    sp.changes.Add(new Change { step = "D3", key = BuildingAudit308.KeyOf(support), kind = "create-mesh", before = "", after = path, note = Str(ln, "fallback_index"), state = AssetDatabase.LoadAssetAtPath<Mesh>(path) != null ? "already" : "apply" });
                    sp.changes.Add(new Change { step = "D3", key = key, kind = "mesh", before = "", after = path + "#" + name, at = t.position, state = "apply", note = "rebuilt with the F5 rule (" + (of == null || of.sharedMesh == null ? "the original has no mesh: BuildingFix308 F5 not applied in this scene?" : "support size differs") + ")" });
                }
            var enabledKeys = new List<string>();
            LanternCount(k, out int nullOn, out int nullOff, enabledKeys);
            sp.notes.Add("MeshFilters with no mesh under the two lanterns: renderer off " + nullOff + " (not a defect: the retired primitives, same as the original), renderer on " + nullOn);
            foreach (var key in enabledKeys.Where(x => !listed.Contains(x))) sp.notes.Add("renderer on + no mesh but not listed in lanterns.surface_rel (left alone): " + key);
            N(k, sp, "D3.null_disabled", nullOff);
            Settle(sp);
            return sp;
        }

        // ------------------------------------------------------------------ D5 firewood: a stack on the slope
        // visual "boxes"   = the three content-pass cubes laid as one stack (2 below + 1 above), each with its own along_m / twist_deg
        //                    (the end faces no longer share one plane)
        // visual "bundles" = the same three cubes shrunk to the pile and kept as its collider (renderer off), plus rows of the existing
        //                    split-firewood prefab (NpcJobs306's PF_NpcTool306_Firewood: no new asset, no collider, no light)

        sealed class Stack { public Vector3 Centre, Normal, Tangent, Right; public Quaternion Rot; public float Slope; }

        static Stack StackFrame(Ctx k, JToken fw, out string why)
        {
            why = null; float cx = At(Req(fw, "centre_xz"), 0), cz = At(Req(fw, "centre_xz"), 1), d = Num(fw, "normal_sample_m");
            if (!Ground(k, cx, cz, out var g, out _)) { why = "no ground under the stack centre"; return null; }
            float xp = BuildingAudit308.TerrainTop(new Vector3(cx + d, 0, cz)), xm = BuildingAudit308.TerrainTop(new Vector3(cx - d, 0, cz)), zp = BuildingAudit308.TerrainTop(new Vector3(cx, 0, cz + d)), zm = BuildingAudit308.TerrainTop(new Vector3(cx, 0, cz - d));
            if (float.IsNaN(xp) || float.IsNaN(xm) || float.IsNaN(zp) || float.IsNaN(zm)) { why = "no terrain under the normal samples"; return null; }
            var n = new Vector3(-(xp - xm) / (2 * d), 1f, -(zp - zm) / (2 * d)).normalized;
            var s = new Stack { Centre = g, Normal = n, Slope = Vector3.Angle(n, Vector3.up) };
            var down = new Vector3(n.x, 0, n.z);
            if (down.sqrMagnitude < 1e-8f) { s.Tangent = Vector3.forward; s.Right = Vector3.right; }
            else
            {
                down.Normalize(); var contour = Vector3.Cross(Vector3.up, down);
                s.Tangent = (down - n * Vector3.Dot(down, n)).normalized;      // in-plane downhill
                s.Right = (contour - n * Vector3.Dot(contour, n)).normalized;  // the long axis lies along the contour
            }
            s.Rot = Quaternion.LookRotation(Vector3.Cross(s.Right, n), n);
            return s;
        }

        // underside - ground at the two ends of a piece as it stands (its own right / up axes)
        static void EndGaps(Ctx k, Vector3 p, Vector3 right, Vector3 up, Vector3 scale, out float a, out float b)
        {
            var ua = p - right * (scale.x * .5f) - up * (scale.y * .5f); var ub = p + right * (scale.x * .5f) - up * (scale.y * .5f);
            a = ua.y - GroundY(k, ua.x, ua.z); b = ub.y - GroundY(k, ub.x, ub.z);
        }

        static float PathDistance(Ctx k, Vector3 at, out string id)
            => CentreLine(new Vector2(at.x, at.z), BuildingAudit308.Corridors(k.Scene, k.Cfg), Num(k.Data, "corridor_run_gap_m"), out id);

        static bool Bundles(JToken fw) => Str(fw, "visual") == "bundles";

        // where a cube stands in the stack; scale = its lossy size there. boxes: the piece's own along / twist; bundles: the cubes are
        // the hidden collider, laid flush with the collider layout numbers
        static void PieceTarget(JToken fw, JToken pc, Stack s, Vector3 scale, out Vector3 p, out Quaternion rot)
        {
            bool bundles = Bundles(fw); var lay = bundles ? Req(Req(fw, "bundle"), "collider") : fw;
            int layer = Mathf.RoundToInt(Num(pc, "layer"));
            float along = bundles ? 0f : OptNum(pc, "along_m"), twist = bundles ? 0f : OptNum(pc, "twist_deg");
            rot = Quaternion.AngleAxis(twist, s.Normal) * s.Rot;
            p = s.Centre + s.Right * along + s.Tangent * (Num(pc, "side") * Num(lay, "side_m")) + s.Normal * (scale.y * .5f - Num(fw, "sink_m") + (layer > 0 ? Num(lay, "top_rise_m") : 0f));
        }

        // every mesh of a prefab as one box in the prefab root's frame (measured from the asset: nothing is instantiated)
        static Bounds PrefabBounds(GameObject prefab)
        {
            bool any = false; var b = new Bounds(); var root = prefab.transform;
            foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                var mb = f.sharedMesh.bounds; var m = root.worldToLocalMatrix * f.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        // a bundle row: c = the bottom centre of its box on the stack, origin = where the prefab root goes. The prefab's +Z (the length
        // of the splits) lies along the contour, its +Y along the ground normal; twist_deg turns it about the normal.
        static void BundleTarget(JToken bd, JToken row, Stack s, Bounds b, Vector3 scale, out Vector3 c, out Quaternion rot, out Vector3 origin)
        {
            float up = OptNum(row, "up_m");
            rot = Quaternion.AngleAxis(OptNum(row, "twist_deg"), s.Normal) * Quaternion.LookRotation(s.Right, s.Normal);
            c = s.Centre + s.Right * OptNum(row, "along_m") + s.Tangent * OptNum(row, "side_m") + s.Normal * (up - (up <= 0f ? Num(bd, "sink_m") : 0f));
            origin = c - rot * Vector3.Scale(scale, new Vector3(b.center.x, b.min.y, b.center.z));
        }
        // underside - ground at the four bottom corners of a bundle's box
        static void BundleGaps(Ctx k, Vector3 c, Quaternion rot, Bounds b, Vector3 scale, out float lo, out float hi)
        {
            lo = float.PositiveInfinity; hi = float.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                var w = c + rot * new Vector3(((i & 1) == 0 ? -1 : 1) * b.extents.x * scale.x, 0, ((i & 2) == 0 ? -1 : 1) * b.extents.z * scale.z);
                float g = w.y - GroundY(k, w.x, w.z); lo = Mathf.Min(lo, g); hi = Mathf.Max(hi, g);
            }
        }
        static string BundleDefect(GameObject prefab)
        {
            if (prefab.GetComponentsInChildren<Collider>(true).Length > 0) return "carries a collider (the pile's collider is the three cubes)";
            if (prefab.GetComponentsInChildren<Light>(true).Length > 0) return "carries a light";
            if (prefab.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m != null && m.IsKeywordEnabled("_EMISSION")))) return "has an emissive material (ArtAudio light rule)";
            return null;
        }

        // where a piece stands before D5. A pinned [x, z, yaw] in the data, or ("scene_row" / no value) what Content308 scene-apply
        // writes: the content point of the prop row's anchor + the row's offset (world axes), the row's yaw. Read at plan time, so
        // the place follows the anchor point when Content308 content-apply moves it.
        static bool ExpectBefore(Ctx k, JToken pc, out Vector3 at, out float yaw, out string source)
        {
            at = default; yaw = 0f;
            if (pc["expect_before"] is JArray a) { at = new Vector3(At(a, 0), 0, At(a, 1)); yaw = At(a, 2); source = "pinned in the data"; return true; }
            string mode = Opt(pc, "expect_before");
            if (mode != null && mode != "scene_row") throw new Refuse("data: firewood piece " + Str(pc, "id") + " expect_before '" + mode + "' (a [x, z, yaw] array or \"scene_row\")");
            string id = Str(pc, "id"); var row = Arr(k.SceneData, "props").FirstOrDefault(p => Opt(p, "id") == id);
            if (row == null) { source = "no prop row " + id + " in the scene data"; return false; }
            string anchor = Opt(row, "anchor");
            var point = Array.Find(k.Content.Points ?? Array.Empty<PrologueContentSO.Point>(), p => p != null && p.Id == anchor);
            if (point == null) { source = "the prop row's anchor point '" + anchor + "' is not in the content"; return false; }
            var off = Req(row, "offset");
            at = new Vector3(point.Position.x + At(off, 0), 0, point.Position.z + At(off, 1)); yaw = Num(row, "yaw");
            source = "scene row: anchor " + anchor + " " + V(point.Position) + " + offset (" + F(At(off, 0)) + ", " + F(At(off, 1)) + ")";
            return true;
        }

        static StepPlan PlanD5(Ctx k, bool firewoodOff, string dataState)
        {
            var fw = Req(k.Data, "firewood"); var tol = Req(k.Data, "tolerances"); string visual = Str(fw, "visual"); bool bundles = visual == "bundles";
            var sp = new StepPlan { step = "D5", detail = "firewood stack (" + visual + ")" };
            if (!Flag(fw, "enabled")) { sp.status = "off"; sp.detail = "off in data"; return sp; }
            if (visual != "boxes" && !bundles) { sp.status = "blocked"; sp.detail = "firewood.visual '" + visual + "' is not implemented (boxes | bundles)"; return sp; }
            if (!firewoodOff) { sp.status = "blocked"; sp.detail = "data mismatch - the firewood rows are still enabled in content308_scene.json (the next scene-apply would lay them level again); run: python Tools/Art/contentseat308_dry.py patch-data --write"; return sp; }
            var s = StackFrame(k, fw, out string why);
            if (s == null) { sp.status = "blocked"; sp.detail = why; return sp; }
            sp.notes.Add("stack centre " + V(s.Centre) + " normal (" + F(s.Normal.x, "F3") + ", " + F(s.Normal.y, "F3") + ", " + F(s.Normal.z, "F3") + ") slope " + F(s.Slope, "F1") + " deg, long axis bearing " + F(Mathf.Repeat(Mathf.Atan2(s.Right.x, s.Right.z) * Mathf.Rad2Deg, 360f), "F0"));
            N(k, sp, "D5.normal_x", s.Normal.x); N(k, sp, "D5.normal_y", s.Normal.y); N(k, sp, "D5.normal_z", s.Normal.z); N(k, sp, "D5.ground", s.Centre.y);
            if (s.Slope > Num(fw, "max_slope_deg")) { sp.status = "blocked"; sp.detail = "slope " + F(s.Slope, "F1") + " deg at the stack centre (> " + F(Num(fw, "max_slope_deg"), "F0") + ")"; return sp; }
            float lo = At(Req(fw, "end_gap_m"), 0), hi = At(Req(fw, "end_gap_m"), 1);
            var logger = Array.Find(k.Content.Points ?? Array.Empty<PrologueContentSO.Point>(), p => p != null && p.Id == Str(fw, "logger_id"));
            // the stack is laid where the data says; it must not run before the content pass has brought the logger there
            float anchorMax = OptNum(fw, "anchor_max_m");
            if (anchorMax > 0f && logger != null && Harness303.Flat(s.Centre, logger.Position) > anchorMax)
            {
                sp.status = "blocked";
                sp.detail = "the " + Str(fw, "logger_id") + " content point " + V(logger.Position) + " is " + F(Harness303.Flat(s.Centre, logger.Position), "F1") + " m from the stack centre (> firewood.anchor_max_m " + F(anchorMax, "F1") + "): Content308 content-apply and scene-apply have to move the logger and its firewood first";
                return sp;
            }
            var fails = new List<string>();
            foreach (var pc in Arr(fw, "pieces"))
            {
                string key = Str(pc, "key"), id = Str(pc, "id"); var t = Strict(k, key);
                if (!UnderOwnRoot(k, t)) { sp.status = "blocked"; sp.detail = key + " is not under " + Str(k.Data, "root"); return sp; }
                var scale = bundles ? Vec3(Req(Req(fw, "bundle"), "collider"), "scale") : t.lossyScale; int layer = Mathf.RoundToInt(Num(pc, "layer"));
                PieceTarget(fw, pc, s, scale, out var p, out var rot);
                EndGaps(k, p, rot * Vector3.right, rot * Vector3.up, scale, out float ea, out float eb);
                float cd = PathDistance(k, p, out string cid); float ld = logger != null ? Harness303.Flat(p, logger.Position) : float.PositiveInfinity;
                N(k, sp, "D5." + id + ".x", p.x); N(k, sp, "D5." + id + ".y", p.y); N(k, sp, "D5." + id + ".z", p.z); N(k, sp, "D5." + id + ".end_a", ea); N(k, sp, "D5." + id + ".end_b", eb);
                if (layer == 0 && (ea < lo || ea > hi || eb < lo || eb > hi)) fails.Add(id + " end underside - ground " + F(ea) + " / " + F(eb) + " outside " + F(lo) + " .. " + F(hi));
                if (cd < Num(fw, "corridor_clear_m")) fails.Add(id + " " + F(cd) + " m from the centre line of " + cid + " (< " + F(Num(fw, "corridor_clear_m")) + ")");
                if (ld < Num(fw, "logger_clear_m")) fails.Add(id + " " + F(ld) + " m from the logger point (< " + F(Num(fw, "logger_clear_m")) + ")");
                var ch = new Change { step = "D5", key = key, kind = "pose", before = PoseText(t.localPosition, t.localRotation), after = LocalPoseFor(t, p, rot), at = p };
                var prior = Prior(k.Ledger, "D5", key, "pose"); bool ebOk = ExpectBefore(k, pc, out var ebAt, out float ebYaw, out string ebSource);
                if (SamePose(k, t, ch.after)) { ch.state = "already"; ch.before = prior != null ? prior.before : ""; }
                else if ((prior != null && SamePose(k, t, prior.before)) || (ebOk && Harness303.Flat(t.position, ebAt) <= Num(tol, "before_xz_m") && Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, ebYaw)) <= Num(tol, "before_yaw_deg")))
                { ch.state = "apply"; ch.note = V(t.position) + " yaw " + F(t.eulerAngles.y, "F0") + " -> " + V(p) + ", ends " + F(ea) + " / " + F(eb) + ", route " + F(cd, "F1") + " m, logger " + F(ld, "F1") + " m"; }
                else { ch.state = "mismatch"; ch.note = "the piece is at " + V(t.position) + " yaw " + F(t.eulerAngles.y, "F0") + ": neither the content-pass place " + (ebOk ? "(" + F(ebAt.x) + ", " + F(ebAt.z) + ") yaw " + F(ebYaw, "F0") + " [" + ebSource + "]" : "[" + ebSource + "]") + " nor the target " + V(p) + " (after a change of firewood.visual or of the layout numbers: revert:" + k.Alias + ":D5 first; after a move of the anchor point: Content308 scene-apply first)"; }
                sp.changes.Add(ch);
                if (!bundles) continue;
                // the cube becomes the pile's collider: sized to the pile, renderer off
                var local = LocalScaleUnder(t.parent, scale); var sprior = Prior(k.Ledger, "D5", key, "scale");
                var sc = new Change { step = "D5", key = key, kind = "scale", before = Vec(t.localScale), after = Vec(local), at = p, note = "the cube stays as the pile's collider (" + F(scale.x) + " x " + F(scale.y) + " x " + F(scale.z) + " m)" };
                if ((t.localScale - local).magnitude <= Num(tol, "scale")) { sc.state = "already"; sc.before = sprior != null ? sprior.before : ""; } else sc.state = "apply";
                sp.changes.Add(sc);
                var cr = t.GetComponent<MeshRenderer>();
                if (cr != null) sp.changes.Add(new Change { step = "D5", key = key, kind = "enable", before = "1", after = "0", at = p, state = cr.enabled ? "apply" : "already", note = "cube renderer off: the bundles are the visible pile" });
            }
            if (bundles)
            {
                string bwhy = PlanBundles(k, sp, fw, s, logger, fails);
                if (bwhy != null) { sp.status = "blocked"; sp.detail = bwhy; return sp; }
            }
            if (fails.Count > 0 && sp.changes.Any(c => c.state == "apply")) { sp.status = "blocked"; sp.detail = string.Join("; ", fails); return sp; }
            foreach (var f in fails) sp.notes.Add("limit: " + f);
            Settle(sp);
            return sp;
        }

        // null = planned; else why the step is blocked
        static string PlanBundles(Ctx k, StepPlan sp, JToken fw, Stack s, PrologueContentSO.Point logger, List<string> fails)
        {
            var bd = Req(fw, "bundle"); var tol = Req(k.Data, "tolerances"); string prefabPath = Str(bd, "prefab"), parentKey = Str(bd, "parent_key");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return "bundle prefab missing " + prefabPath + " (NpcJobs306 'profiles' builds it) - or set firewood.visual to boxes";
            string defect = BundleDefect(prefab); if (defect != null) return "the bundle prefab " + defect;
            var parent = Strict(k, parentKey); if (!UnderOwnRoot(k, parent)) return parentKey + " is not under " + Str(k.Data, "root");
            var b = PrefabBounds(prefab); var scale = Vec3(bd, "scale"); float lo = At(Req(bd, "end_gap_m"), 0), hi = At(Req(bd, "end_gap_m"), 1);
            if (b.size.z <= 0f) return "the bundle prefab has no mesh";
            sp.notes.Add("bundle prefab box (root frame) min " + V(b.min) + " max " + V(b.max) + " x scale " + V(scale) + " -> " + F(b.size.z * scale.z) + " m long, " + F(b.size.x * scale.x) + " m wide, " + F(b.size.y * scale.y) + " m high");
            N(k, sp, "D5.bundle_len", b.size.z * scale.z); N(k, sp, "D5.bundle_width", b.size.x * scale.x); N(k, sp, "D5.bundle_height", b.size.y * scale.y);
            var names = new HashSet<string>(); var localScale = LocalScaleUnder(parent, scale);
            foreach (var row in Arr(bd, "rows"))
            {
                string name = Str(row, "name"); if (!names.Add(name)) return "bundle row name twice: " + name;
                string key = parentKey + "/" + name;
                BundleTarget(bd, row, s, b, scale, out var c, out var rot, out var origin);
                BundleGaps(k, c, rot, b, scale, out float glo, out float ghi);
                float cd = PathDistance(k, c, out string cid); float ld = logger != null ? Harness303.Flat(c, logger.Position) : float.PositiveInfinity;
                N(k, sp, "D5." + name + ".x", c.x); N(k, sp, "D5." + name + ".y", c.y); N(k, sp, "D5." + name + ".z", c.z); N(k, sp, "D5." + name + ".gap_lo", glo); N(k, sp, "D5." + name + ".gap_hi", ghi);
                if (OptNum(row, "up_m") <= 0f && (glo < lo || ghi > hi)) fails.Add(name + " underside - ground " + F(glo) + " .. " + F(ghi) + " outside " + F(lo) + " .. " + F(hi));
                if (cd < Num(fw, "corridor_clear_m")) fails.Add(name + " " + F(cd) + " m from the centre line of " + cid + " (< " + F(Num(fw, "corridor_clear_m")) + ")");
                if (ld < Num(fw, "logger_clear_m")) fails.Add(name + " " + F(ld) + " m from the logger point (< " + F(Num(fw, "logger_clear_m")) + ")");
                string pose = LocalPoseUnder(parent, origin, rot);
                var ch = new Change { step = "D5", key = key, kind = "spawn", before = "", after = prefabPath + ";" + pose + ";" + Vec(localScale), at = c };
                var have = Strict(k, key, false);
                if (have == null) { ch.state = "apply"; ch.note = "new prefab instance, bottom centre " + V(c) + ", underside - ground " + F(glo) + " .. " + F(ghi) + ", route " + F(cd, "F1") + " m"; }
                else
                {
                    var src = PrefabUtility.GetCorrespondingObjectFromSource(have.gameObject); string srcPath = src != null ? AssetDatabase.GetAssetPath(src) : "";
                    bool same = srcPath == prefabPath && NearPose(k, have, pose) && (have.localScale - localScale).magnitude <= Num(tol, "scale");
                    ch.state = same ? "already" : "mismatch";
                    if (!same) ch.note = "an object of this name exists but " + (srcPath != prefabPath ? "is not an instance of the bundle prefab (" + (srcPath.Length == 0 ? "no prefab" : srcPath) + ")" : "stands at " + V(have.position) + ", not the data place") + ": revert:" + k.Alias + ":D5 first";
                }
                sp.changes.Add(ch);
            }
            if (names.Count == 0) return "firewood.bundle.rows is empty";
            return null;
        }

        // ------------------------------------------------------------------ D6 old road ribbons: renderer off (never deleted)

        static StepPlan PlanD6(Ctx k)
        {
            var rb = Req(k.Data, "ribbons");
            var sp = new StepPlan { step = "D6", detail = "old road ribbons - renderer off" };
            if (!Flag(rb, "enabled")) { sp.status = "off"; sp.detail = "off in data"; return sp; }
            var root = Strict(k, Str(rb, "root_key"), false); var touched = new HashSet<Transform>();
            foreach (var row in Arr(rb, "renderers"))
            {
                string key = Str(row, "key");
                if (!Flag(row, "enabled")) { sp.notes.Add("listed only: " + key); continue; }
                var t = Strict(k, key, false);
                if (t == null) { sp.notes.Add("absent " + key); continue; }
                var r = t.GetComponent<MeshRenderer>();
                if (r == null) { sp.notes.Add(key + ": no MeshRenderer"); continue; }
                touched.Add(t);
                var ch = new Change { step = "D6", key = key, kind = "enable", before = "1", after = "0", at = r.bounds.center, state = r.enabled ? "apply" : "already", note = Opt(row, "note") ?? "" };
                if (Flag(rb, "require_no_collider") && t.GetComponentsInChildren<Collider>(true).Length > 0) { ch.state = "blocked"; ch.note = "has a collider: something may stand on it - not switched off (decide by hand)"; }
                if (t.childCount > 0) ch.note += " (" + t.childCount + " child object(s) stay as they are)";
                sp.changes.Add(ch);
            }
            if (root != null)
                sp.changes.Add(new Change { step = "D6", key = BuildingAudit308.KeyOf(root), kind = "hash", state = "info", after = SubtreeHash(root, touched), note = "every other object under " + root.name + " (must be equal after the apply)" });
            Settle(sp);
            return sp;
        }

        // ------------------------------------------------------------------ verify (read only)

        [Serializable] sealed class VerifyDoc { public string alias = "", scene = "", utc = "", sceneSha = "", dataSha = "", sceneDataSha = "", attraction = ""; public int fail; public List<string> rows = new List<string>(); }

        static string Verify(string alias)
        {
            var k = Begin(alias, false, out string previous, out bool opened);
            var v = new VerifyDoc { alias = k.Alias, scene = k.ScenePath, utc = BuildingAudit308.Utc(), sceneSha = ShaOf(Harness303.Abs(k.ScenePath)), dataSha = k.DataSha, sceneDataSha = k.SceneDataSha };
            void Row(bool ok, string text) { v.rows.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) v.fail++; }
            void Info(string text) => v.rows.Add("INFO " + text);
            var sink = new StepPlan();   // collects the "num" rows
            try
            {
                Physics.SyncTransforms();
                v.attraction = BuildingFix308.AttractionHash(k.Scene, k.Cfg.attractionRoot);
                string state = SceneDataState(k.Data, k.SceneData, out int candidate, out bool firewoodOff);
                Row(candidate >= 0 && firewoodOff, "data: " + state);
                foreach (var st in StepsInData(k.Data)) Info(st + " ledger: " + (StepLive(k.Ledger, st) ? "applied" : "no live apply in " + Path.GetFileName(LedgerFile(k.ScenePath))));
                try { VerifyD1(k, candidate, Row, Info, sink); } catch (Refuse r) { Row(false, "D1 " + r.Message); }
                try { VerifyD2(k, candidate, Row, Info, sink); } catch (Refuse r) { Row(false, "D2 " + r.Message); }
                try { VerifyD3(k, Row, Info, sink); } catch (Refuse r) { Row(false, "D3 " + r.Message); }
                try { VerifyD5(k, Row, Info, sink); } catch (Refuse r) { Row(false, "D5 " + r.Message); }
                try { VerifyD6(k, Row, Info, sink); } catch (Refuse r) { Row(false, "D6 " + r.Message); }
                if (k.Data["hinges"] != null) { try { VerifyD7(k, Row, Info, sink); } catch (Refuse r) { Row(false, "D7 " + r.Message); } }
                if (k.Data["reseat"] != null) { try { VerifyD8(k, Row, Info, sink); } catch (Refuse r) { Row(false, "D8 " + r.Message); } }
                var last = k.Ledger.ops.LastOrDefault(o => o.status == "applied" && !o.reverted);
                if (last != null) Row(last.attractionAfter == last.attractionBefore && v.attraction == last.attractionAfter, "Finish297_Attraction block hash unchanged (" + Short(v.attraction) + ")");
                foreach (var n in sink.numbers) v.rows.Add(n);
                foreach (var d in CompareDry(k)) v.rows.Add(d);
            }
            finally { GoBack(previous, k.Scene, opened); }
            Directory.CreateDirectory(OutDir);
            string file = Path.Combine(OutDir, "seat308_verify_" + k.Alias + "_" + v.utc + ".json");
            File.WriteAllText(file, JsonUtility.ToJson(v, true), new UTF8Encoding(false));
            return "ContentSeat308 verify " + k.Alias + ": " + (v.fail == 0 ? "no FAIL" : v.fail + " FAIL") + " -> " + file + "\n" + string.Join("\n", v.rows);
        }

        static void VerifyD1(Ctx k, int candidate, Action<bool, string> Row, Action<string> Info, StepPlan sink)
        {
            var inn = Req(k.Data, "inn"); if (!Flag(inn, "enabled")) { Info("D1 off in data"); return; }
            var holder = Strict(k, Str(inn, "holder_key")); var house = Strict(k, Str(inn, "inn_key")); var plate = Strict(k, Str(inn, "plate_key"));
            RestFrame(k, Str(inn, "rest_id"), out _, out _, out var cp);
            float embed = candidate >= 0 ? Candidates(inn)[candidate].embed : Num(inn, "embed_m"); var shape = MeasureHouse(k, inn, house, plate); var vf = Req(k.Data, "verify");
            Row(shape.Suspect == null, "D1 plinth top above the pivot " + F(shape.TopRel) + " (" + shape.TopNote + ")" + (shape.Suspect != null ? " - SUSPECT: the burial row below means nothing" : ""));
            if (candidate >= 0)
            {
                var c = Candidates(inn)[candidate];
                if (InnTarget(k, inn, c.x, c.z, c.yaw, -embed, out var pos, out var rot, out _, out string why))
                    Row(Vector3.Distance(house.position, pos) <= k.PoseTol && Quaternion.Angle(house.rotation, rot) <= k.YawTol, "D1 the inn stands where content308_scene.json puts it (" + F(Vector3.Distance(house.position, pos), "F3") + " m, " + F(Quaternion.Angle(house.rotation, rot), "F2") + " deg off; Content308 scene-dry then says 'ok', no re-seat)");
                else Row(false, "D1 target: " + why);
            }
            var e = EvalHouse(k, inn, shape, house, house.position, house.eulerAngles.y, cp);
            HouseNumbers(k, sink, e); N(k, sink, "D1.plinth_top", shape.TopRel);
            Row(Mathf.Abs(house.position.y - e.Ground + embed) <= Num(inn, "embed_tol_m"), "D1 pivot y - ground = " + F(house.position.y - e.Ground, "F3") + " (want " + F(-embed) + " ± " + F(Num(inn, "embed_tol_m")) + ")");
            Row(e.Bury <= Num(inn, "bury_max_m"), "D1 plinth corners: ground - plinth top ≤ " + F(Num(inn, "bury_max_m")) + " (deepest " + F(e.Bury) + "; exposed FL " + F(shape.TopRel - e.Corner[0]) + " FR " + F(shape.TopRel - e.Corner[1]) + " BL " + F(shape.TopRel - e.Corner[2]) + " BR " + F(shape.TopRel - e.Corner[3]) + "; " + shape.TopNote + ")");
            Row(!float.IsNaN(e.FirstRise) && e.FirstRise <= Num(inn, "step_limit_m"), "D1 first step above the ground " + F(e.FirstRise) + " m ≤ " + F(Num(inn, "step_limit_m")));
            Row(e.Trunk >= Num(inn, "trunk_margin_m"), "D1 no trunk inside the outline + " + F(Num(inn, "trunk_margin_m")) + " m (nearest " + e.TrunkId + " " + F(e.Trunk) + " m)");
            Row(e.Corridor >= Num(inn, "corridor_clear_m"), "D1 outline " + F(e.Corridor) + " m from the nearest route centre line (" + e.CorridorId + ")");
            Row(e.Blocker.Length == 0, "D1 no other collider inside the plinth + apron" + (e.Blocker.Length > 0 ? " (" + e.Blocker + ")" : ""));
            var old = Strict(k, Str(inn, "old_skirt_key"), false); var oldR = old != null ? old.GetComponent<MeshRenderer>() : null;
            Row(oldR == null || !oldR.enabled, "D1 StoneSkirt299 renderer off");
            var sh = house.Find(Str(inn, "skirt_holder")); var sf = sh != null ? sh.GetComponent<MeshFilter>() : null; var sr = sh != null ? sh.GetComponent<MeshRenderer>() : null;
            var skirtCh = SkirtRecord(k.Ledger, BuildingAudit308.KeyOf(plate));
            if (skirtCh != null && skirtCh.state == "info") { Info("D1 no new skirt was needed: " + skirtCh.note); Row(sh == null, "D1 no StoneSkirt308 holder (the ledger says none was made)"); }
            else
            {
                Row(sf != null && sf.sharedMesh != null && sf.sharedMesh.vertexCount > 0, "D1 StoneSkirt308 mesh has vertices (" + (sf != null && sf.sharedMesh != null ? sf.sharedMesh.vertexCount + ", " + AssetDatabase.GetAssetPath(sf.sharedMesh) : "none") + ")");
                Row(sr != null && sr.sharedMaterial != null && !sr.sharedMaterial.IsKeywordEnabled("_EMISSION"), "D1 skirt material is not emissive");
                if (skirtCh != null) Info("D1 skirt ledger note: " + skirtCh.note);
            }
            var gd = Req(k.SceneData, "ground"); float pr = Num(gd, "feet_probe_radius_m"), ph = Num(gd, "feet_probe_height_m");
            var inside = Physics.OverlapCapsule(cp.Feet + Vector3.up * (pr + Num(vf, "feet_probe_lift_m")), cp.Feet + Vector3.up * (ph - pr), pr, ~0, QueryTriggerInteraction.Ignore).Where(c => UnderOwnRoot(k, c.transform)).Select(c => BuildingAudit308.KeyOf(c.transform)).Distinct().ToArray();
            Row(inside.Length == 0, "D1/D2 respawn feet " + V(cp.Feet) + " clear of the Content308 colliders" + (inside.Length > 0 ? " (inside: " + string.Join(", ", inside) + ")" : ""));
            int lights = holder.GetComponentsInChildren<Light>(true).Count(l => l.enabled && l.gameObject.activeInHierarchy);
            Row(lights == Mathf.RoundToInt(Num(inn, "lights_on_expected")), "D1 lights on under the rest compound: " + lights + " (want " + F(Num(inn, "lights_on_expected"), "F0") + ")");
        }

        static void VerifyD2(Ctx k, int candidate, Action<bool, string> Row, Action<string> Info, StepPlan sink)
        {
            var kp = Req(k.Data, "keeper"); if (!Flag(kp, "enabled")) { Info("D2 off in data"); return; }
            var vf = Req(k.Data, "verify");
            string id = Str(kp, "id"); var body = Strict(k, Str(kp, "body_key"));
            var point = Array.Find(k.Content.Points ?? Array.Empty<PrologueContentSO.Point>(), p => p != null && p.Id == id);
            if (point == null) { Row(false, "D2 content point " + id + " missing"); return; }
            float gy = GroundY(k, point.Position.x, point.Position.z);
            var cp = Array.Find(k.Content.Checkpoints ?? Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>(), p => p != null && p.Id == Str(kp, "face_checkpoint"));
            N(k, sink, "D2.x", point.Position.x); N(k, sink, "D2.y", point.Position.y); N(k, sink, "D2.z", point.Position.z); N(k, sink, "D2.yaw", body.eulerAngles.y);
            Row(Harness303.Flat(body.position, point.Position) <= k.PoseTol, "D2 (AC-C8) body on its content point (" + F(Harness303.Flat(body.position, point.Position), "F3") + " m)");
            Row(Mathf.Abs(point.Position.y - gy) <= Num(kp, "max_dy_m"), "D2 point y - ground " + F(point.Position.y - gy, "F3") + " (|dy| ≤ " + F(Num(kp, "max_dy_m")) + ")");
            float nearest = float.PositiveInfinity; string who = "";
            foreach (var spot in k.Content.Checkpoints ?? Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>())
                if (spot != null && spot.IsConfigured && Harness303.Flat(spot.Feet, point.Position) < nearest) { nearest = Harness303.Flat(spot.Feet, point.Position); who = spot.Id; }
            if (cp != null) N(k, sink, "D2.feet_gap", Harness303.Flat(cp.Feet, point.Position));
            Row(nearest >= Num(kp, "feet_clear_m"), "D2 point " + F(nearest) + " m from the nearest respawn feet (" + who + ", need ≥ " + F(Num(kp, "feet_clear_m")) + ")");
            if (cp != null) Row(Mathf.Abs(Mathf.DeltaAngle(body.eulerAngles.y, Harness303.YawTo(body.position, cp.Feet))) <= Num(vf, "d2_face_deg"), "D2 the body faces the respawn feet (yaw " + F(body.eulerAngles.y, "F1") + ", toward the feet " + F(Mathf.Repeat(Harness303.YawTo(body.position, cp.Feet), 360f), "F1") + ")");
            if (candidate >= 0 && KeeperTarget(k, candidate, out var g, out _, out _, out _, out _))
                Row(Harness303.Flat(point.Position, g) <= Num(kp, "body_tol_m"), "D2 the point is the data place in front of the house (" + F(Harness303.Flat(point.Position, g), "F3") + " m off)");
            var house = Strict(k, Str(Req(k.Data, "inn"), "inn_key"), false); var cap = body.GetComponent<CapsuleCollider>();
            if (house != null && cap != null)
            {
                var c = body.TransformPoint(cap.center); float r = cap.radius * Mathf.Max(body.lossyScale.x, body.lossyScale.z), h = Mathf.Max(cap.height * body.lossyScale.y, r * 2);
                var hits = Physics.OverlapCapsule(c + Vector3.up * (h * .5f - r), c - Vector3.up * (h * .5f - r - Num(vf, "capsule_lift_m")), r, ~0, QueryTriggerInteraction.Ignore).Where(x => x.transform.IsChildOf(house)).Select(x => x.name).Distinct().ToArray();
                Row(hits.Length == 0, "D2 the keeper capsule does not touch the house colliders" + (hits.Length > 0 ? " (" + string.Join(", ", hits) + ")" : ""));
            }
            Info("D2 after this: NpcJobs306 author:" + k.ScenePath + " (job places follow the point), then its report must still show 15 actors / 9 hand tools");
        }

        static void VerifyD3(Ctx k, Action<bool, string> Row, Action<string> Info, StepPlan sink)
        {
            var ln = Req(k.Data, "lanterns"); if (!Flag(ln, "enabled")) { Info("D3 off in data"); return; }
            var keys = new List<string>(); LanternCount(k, out int on, out int off, keys);
            N(k, sink, "D3.null_disabled", off); N(k, sink, "D3.null_enabled_after", on);
            Row(on == 0, "D3 enabled renderer + no mesh under the two lanterns: " + on + (on > 0 ? " (" + string.Join(", ", keys) + ")" : ""));
            Row(off == Mathf.RoundToInt(Num(ln, "expected_null_disabled")), "D3 no mesh + renderer off (retired primitives, not a defect): " + off + " (want " + F(Num(ln, "expected_null_disabled"), "F0") + ")");
            foreach (var ch in k.Ledger.ops.Where(o => !o.reverted && o.status == "applied").SelectMany(o => o.changes).Where(c => c.step == "D3" && c.kind == "mesh" && c.state == "apply"))
            {
                var t = BuildingAudit308.Resolve(k.Scene, ch.key); var f = t != null ? t.GetComponent<MeshFilter>() : null; var r = t != null ? t.GetComponent<MeshRenderer>() : null;
                Row(f != null && MeshState(f) == ch.after, "D3 " + ch.key.Substring(Math.Max(0, ch.key.Length - 70)) + " uses " + Path.GetFileName(ch.after.Split('#')[0]));
                if (r != null) Row(r.sharedMaterials.All(m => m == null || !m.IsKeywordEnabled("_EMISSION")), "D3 that cap's materials are not emissive (this tool adds no material)");
            }
        }

        static void VerifyD5(Ctx k, Action<bool, string> Row, Action<string> Info, StepPlan sink)
        {
            var fw = Req(k.Data, "firewood"); if (!Flag(fw, "enabled")) { Info("D5 off in data"); return; }
            var vf = Req(k.Data, "verify"); bool bundles = Bundles(fw);
            float lo = At(Req(fw, "end_gap_m"), 0), hi = At(Req(fw, "end_gap_m"), 1);
            var logger = Array.Find(k.Content.Points ?? Array.Empty<PrologueContentSO.Point>(), p => p != null && p.Id == Str(fw, "logger_id"));
            var s = StackFrame(k, fw, out _);
            if (s != null) { N(k, sink, "D5.normal_x", s.Normal.x); N(k, sink, "D5.normal_y", s.Normal.y); N(k, sink, "D5.normal_z", s.Normal.z); N(k, sink, "D5.ground", s.Centre.y); }
            Info("D5 visual " + Str(fw, "visual"));
            foreach (var pc in Arr(fw, "pieces"))
            {
                string id = Str(pc, "id"); var t = Strict(k, Str(pc, "key"));
                EndGaps(k, t.position, t.right, t.up, t.lossyScale, out float ea, out float eb);
                N(k, sink, "D5." + id + ".x", t.position.x); N(k, sink, "D5." + id + ".y", t.position.y); N(k, sink, "D5." + id + ".z", t.position.z); N(k, sink, "D5." + id + ".end_a", ea); N(k, sink, "D5." + id + ".end_b", eb);
                if (Mathf.RoundToInt(Num(pc, "layer")) == 0) Row(ea >= lo && ea <= hi && eb >= lo && eb <= hi, "D5 " + id + " ends: underside - ground " + F(ea) + " / " + F(eb) + " (limit " + F(lo) + " .. " + F(hi) + ")");
                else Info("D5 " + id + " (top piece) underside - ground " + F(ea) + " / " + F(eb));
                if (s != null)
                {
                    Row(Vector3.Angle(t.up, s.Normal) <= Num(vf, "d5_normal_deg"), "D5 " + id + " lies on the slope (its up axis " + F(Vector3.Angle(t.up, s.Normal), "F2") + " deg off the ground normal)");
                    PieceTarget(fw, pc, s, t.lossyScale, out var p, out var rot);
                    Row(Vector3.Distance(t.position, p) <= k.PoseTol && Quaternion.Angle(t.rotation, rot) <= Num(vf, "d5_rot_deg"), "D5 " + id + " stands on its data place in the stack (" + F(Vector3.Distance(t.position, p), "F3") + " m, " + F(Quaternion.Angle(t.rotation, rot), "F2") + " deg off)");
                }
                float cd = PathDistance(k, t.position, out string cid);
                Row(cd >= Num(fw, "corridor_clear_m"), "D5 " + id + " " + F(cd, "F1") + " m from the nearest route centre line (" + cid + ", need ≥ " + F(Num(fw, "corridor_clear_m"), "F1") + ")");
                if (logger != null) Row(Harness303.Flat(t.position, logger.Position) >= Num(fw, "logger_clear_m"), "D5 " + id + " " + F(Harness303.Flat(t.position, logger.Position), "F1") + " m from the logger point (need ≥ " + F(Num(fw, "logger_clear_m"), "F1") + ")");
                if (bundles)
                {
                    var cr = t.GetComponent<MeshRenderer>(); var col = t.GetComponent<Collider>();
                    Row((cr == null || !cr.enabled) && col != null && col.enabled, "D5 " + id + " is the pile's collider only (renderer off, collider on, size " + V(t.lossyScale) + ")");
                }
            }
            if (!bundles || s == null) return;
            var bd = Req(fw, "bundle"); string prefabPath = Str(bd, "prefab"), parentKey = Str(bd, "parent_key");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { Row(false, "D5 bundle prefab missing " + prefabPath); return; }
            Row(BundleDefect(prefab) == null, "D5 the bundle prefab has no collider, no light and no emissive material" + (BundleDefect(prefab) != null ? " (" + BundleDefect(prefab) + ")" : ""));
            var b = PrefabBounds(prefab); float blo = At(Req(bd, "end_gap_m"), 0), bhi = At(Req(bd, "end_gap_m"), 1); var scale = Vec3(bd, "scale");
            N(k, sink, "D5.bundle_len", b.size.z * scale.z); N(k, sink, "D5.bundle_width", b.size.x * scale.x); N(k, sink, "D5.bundle_height", b.size.y * scale.y);
            foreach (var row in Arr(bd, "rows"))
            {
                string name = Str(row, "name"); var t = Strict(k, parentKey + "/" + name, false);
                Row(t != null, "D5 bundle " + name + " exists under " + parentKey);
                if (t == null) continue;
                BundleTarget(bd, row, s, b, scale, out var c, out var rot, out var origin);
                var have = t.position + t.rotation * Vector3.Scale(t.lossyScale, new Vector3(b.center.x, b.min.y, b.center.z));
                BundleGaps(k, have, t.rotation, b, t.lossyScale, out float glo, out float ghi);
                N(k, sink, "D5." + name + ".x", have.x); N(k, sink, "D5." + name + ".y", have.y); N(k, sink, "D5." + name + ".z", have.z); N(k, sink, "D5." + name + ".gap_lo", glo); N(k, sink, "D5." + name + ".gap_hi", ghi);
                Row(Vector3.Distance(t.position, origin) <= k.PoseTol && Quaternion.Angle(t.rotation, rot) <= k.YawTol, "D5 bundle " + name + " stands on its data place (" + F(Vector3.Distance(t.position, origin), "F3") + " m, " + F(Quaternion.Angle(t.rotation, rot), "F2") + " deg off)");
                if (OptNum(row, "up_m") <= 0f) Row(glo >= blo && ghi <= bhi, "D5 bundle " + name + " underside - ground " + F(glo) + " .. " + F(ghi) + " (limit " + F(blo) + " .. " + F(bhi) + ")");
                else Info("D5 bundle " + name + " (upper row) underside - ground " + F(glo) + " .. " + F(ghi));
                var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                Row(src != null && AssetDatabase.GetAssetPath(src) == prefabPath, "D5 bundle " + name + " is an instance of " + Path.GetFileName(prefabPath));
            }
        }

        static void VerifyD6(Ctx k, Action<bool, string> Row, Action<string> Info, StepPlan sink)
        {
            var rb = Req(k.Data, "ribbons"); if (!Flag(rb, "enabled")) { Info("D6 off in data"); return; }
            int on = 0; var touched = new HashSet<Transform>();
            foreach (var row in Arr(rb, "renderers"))
            {
                if (!Flag(row, "enabled")) continue;
                string key = Str(row, "key"); var t = Strict(k, key, false);
                if (t == null) { Info("D6 absent " + key); continue; }
                touched.Add(t); var r = t.GetComponent<MeshRenderer>();
                if (r != null && r.enabled) on++;
                Row(r != null && !r.enabled && t.gameObject.activeSelf, "D6 " + key + ": renderer off, object still active, MeshFilter " + (t.GetComponent<MeshFilter>() != null && t.GetComponent<MeshFilter>().sharedMesh != null ? "keeps its mesh" : "HAS NO MESH"));
            }
            N(k, sink, "D6.renderers_on_after", on);
            var root = Strict(k, Str(rb, "root_key"), false);
            var rec = k.Ledger.ops.Where(o => !o.reverted && o.status == "applied").SelectMany(o => o.changes).LastOrDefault(c => c.step == "D6" && c.kind == "hash");
            if (root != null && rec != null) Row(SubtreeHash(root, touched) == rec.after, "D6 every other object under " + root.name + " is unchanged since the apply (hash " + Short(rec.after) + ")");
            else Info("D6 no hash of the other children in the ledger (not applied by this tool yet)");
            Info("D6 not checked here: the map road line in Play, and whether a map bake photographs these renderers (check before the next map bake)");
        }
    }
}
