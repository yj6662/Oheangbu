using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-1 seat track (DEFECTS_308: 부유/매몰 · 계단·벽 조립 · 기타): per-object re-seat / structure rows, data in
    // BuildingAudit308/seat308.json (one row = one ledger step "S:<id>"). Own entry (no dispatch line in BuildingFix308.Run):
    //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.BuildingFix308 Seat "<command>"
    //   list                              rows of seat308.json (read only, no scene)
    //   plan:<alias>[:id,…]               dry run -> Fix/seat-plan-<alias>.json (all rows, or the named ones)
    //   apply:<alias>[:id,…]              enabled rows (or exactly the named ones, enabled or not), each planned on the current state
    //                                     right before it runs; scene backup SceneBackup/<alias>-<utc>-seat.unity; Fix/seat-ledger-<alias>.json
    //   apply-all[:id,…]                  plans the three scenes first; a row blocked/mismatched anywhere is left out everywhere
    //   verify:<alias>                    read only -> Fix/seat-verify-<alias>-<utc>.json
    //   revert:<alias>[:id]               ledger "before" values back (generated mesh assets stay on disk, unreferenced)
    // Ops: pose (dy/dx/dz/yaw/level/localPosition/localScale [+ stone skirt]) · skirt (GroundFit299.StoneSkirt308) · footing (wall
    // faces continued down to the ground under floating bottom edges; mode hull = a revetment under a deck outline) · treads (wall-walk
    // slabs narrowed between the curbs / onto the walk, lowered, or removed) · stretch (segment ends overlapped) · piers · lid ·
    // active · dedupe · report (no change). Protected roots and Finish297_Attraction are refused per row. No AssetDatabase.SaveAssets.
    public static partial class BuildingFix308
    {
        static string SeatFile => Path.Combine(BuildingAudit308.Folder, "seat308.json");
        static string SeatLedgerFile(string alias) => Path.Combine(FixDir, "seat-ledger-" + alias + ".json");
        static readonly string[] SeatChildren = { "Footing308", "Piers308", "Lid308" };

        [Serializable] internal sealed class SeatRow308
        {
            public string id = "", op = "", path = "", note = "", expect = "", material = "", plate = "", within = "", prefixA = "", prefixB = "", mode = "", state = "0";
            public string[] stills = Array.Empty<string>(), include = Array.Empty<string>(), exclude = Array.Empty<string>(), extra = Array.Empty<string>();
            public bool disabled, userCheck, level, skirt, noCollider, includeSlabs;
            public float dx, dy, dz, yaw, hover = .25f, sink = .3f, sample = .5f, vmax = .35f, maxGap = 25f, tolerance = .3f;
            public float minSpan = .6f, clearance = .02f, seatGap = .02f, minProud = .04f, overlap, spacing = 6f, width = 1.2f, depth = 1.2f, minHeight = .8f, slabMaxWidth = 1.3f;
            public float[] expectAt = Array.Empty<float>(), localPosition = Array.Empty<float>(), localScale = Array.Empty<float>(), box = Array.Empty<float>();
            public int smallTris = 12, maxTris = 60000;
        }
        [Serializable] internal sealed class SeatCfg308 { public string version = "", meshFolder = "Assets/_Project/Art/World/Finish297/BuildingFix308/Seat"; public SeatRow308[] rows = Array.Empty<SeatRow308>(); }
        [Serializable] internal sealed class SeatRowPlan308
        {
            public string id = "", op = "", path = "", status = "", detail = "", expect = ""; public bool userCheck, enabled; public string[] stills = Array.Empty<string>();
            public List<Change308> changes = new List<Change308>(); public List<string> notes = new List<string>();
        }
        [Serializable] internal sealed class SeatPlan308 { public string alias = "", scene = "", utc = "", sceneSha = "", seatSha = "", quality = ""; public List<SeatRowPlan308> rows = new List<SeatRowPlan308>(); }
        sealed class SeatBuild308 { public Mesh mesh; public Material[] mats = Array.Empty<Material>(); public string stats = ""; public Vector3 at; }
        static readonly Dictionary<string, SeatBuild308> seatBuilt = new Dictionary<string, SeatBuild308>();

        static SeatCfg308 SeatLoad(out string error)
        {
            error = null;
            if (!File.Exists(SeatFile)) { error = "seat rows missing: " + SeatFile; return null; }
            try
            {
                var c = JsonUtility.FromJson<SeatCfg308>(File.ReadAllText(SeatFile).TrimStart('﻿')); if (c == null || c.rows == null || c.rows.Length == 0) { error = "no rows in " + SeatFile; return null; }
                if (string.IsNullOrEmpty(c.meshFolder)) c.meshFolder = "Assets/_Project/Art/World/Finish297/BuildingFix308/Seat";
                foreach (var r in c.rows) SeatNorm(r);
                var dup = c.rows.GroupBy(r => r.id).FirstOrDefault(g => g.Count() > 1 || g.Key.Length == 0 || g.Key.Contains(":") || g.Key.Contains(","));
                if (dup != null) { error = "row id missing, repeated or with ':' ',' — '" + dup.Key + "'"; return null; }
                return c;
            }
            catch (Exception e) { error = "seat rows unreadable: " + e.Message; return null; }
        }
        // defaults for values the JSON leaves out (a deserialised array element may arrive zero-filled)
        static void SeatNorm(SeatRow308 r)
        {
            string S(string v) => v ?? ""; float F(float v, float d) => v > 0 ? v : d;
            r.id = S(r.id); r.op = S(r.op); r.path = S(r.path); r.note = S(r.note); r.expect = S(r.expect); r.material = S(r.material); r.plate = S(r.plate); r.within = S(r.within); r.prefixA = S(r.prefixA); r.prefixB = S(r.prefixB); r.mode = S(r.mode);
            if (string.IsNullOrEmpty(r.state)) r.state = "0";
            r.stills = r.stills ?? Array.Empty<string>(); r.include = r.include ?? Array.Empty<string>(); r.exclude = r.exclude ?? Array.Empty<string>(); r.extra = r.extra ?? Array.Empty<string>();
            r.expectAt = r.expectAt ?? Array.Empty<float>(); r.localPosition = r.localPosition ?? Array.Empty<float>(); r.localScale = r.localScale ?? Array.Empty<float>(); r.box = r.box ?? Array.Empty<float>();
            r.hover = F(r.hover, .25f); r.sink = F(r.sink, .3f); r.sample = F(r.sample, .5f); r.vmax = F(r.vmax, .35f); r.maxGap = F(r.maxGap, 25f); r.tolerance = F(r.tolerance, .3f);
            r.minSpan = F(r.minSpan, .6f); r.clearance = F(r.clearance, .02f); r.seatGap = F(r.seatGap, .02f); r.minProud = F(r.minProud, .04f); r.spacing = F(r.spacing, 6f); r.width = F(r.width, 1.2f); r.depth = F(r.depth, 1.2f); r.minHeight = F(r.minHeight, .8f); r.slabMaxWidth = F(r.slabMaxWidth, 1.3f);
            if (r.smallTris <= 0) r.smallTris = 12; if (r.maxTris <= 0) r.maxTris = 60000;
        }
        static Ledger308 SeatLoadLedger(string alias)
        {
            string f = SeatLedgerFile(alias);
            return File.Exists(f) ? JsonUtility.FromJson<Ledger308>(File.ReadAllText(f)) ?? new Ledger308 { alias = alias } : new Ledger308 { alias = alias };
        }
        static void SeatSaveLedger(Ledger308 l) { Directory.CreateDirectory(FixDir); File.WriteAllText(SeatLedgerFile(l.alias), JsonUtility.ToJson(l, true), new UTF8Encoding(false)); }
        static Change308 SeatPrior(Ledger308 l, string step, string key, string kind)
        {
            for (int i = l.ops.Count - 1; i >= 0; i--)
            {
                var op = l.ops[i]; if (op.reverted) continue;
                var c = op.changes.LastOrDefault(x => x.step == step && x.key == key && x.kind == kind && x.state == "apply");
                if (c != null) return c;
            }
            return null;
        }
        static void SeatDropBuilt() { foreach (var b in seatBuilt.Values) if (b.mesh != null && !AssetDatabase.Contains(b.mesh)) Object.DestroyImmediate(b.mesh); seatBuilt.Clear(); }
        static List<SeatRow308> SeatRows(SeatCfg308 seat, string[] ids, bool all, out string error)
        {
            error = null;
            if (ids == null || ids.Length == 0) return seat.rows.Where(r => all || !r.disabled).ToList();
            var bad = ids.Where(i => seat.rows.All(r => r.id != i)).ToArray();
            if (bad.Length > 0) { error = "unknown row id " + string.Join(",", bad); return null; }
            return seat.rows.Where(r => ids.Contains(r.id)).ToList();
        }

        // ------------------------------------------------------------------ entry

        public static string Seat(string command)
        {
            command = (command ?? "").Trim();
            try
            {
                var a = command.Split(':');
                string[] Ids(int k) => a.Length > k ? a[k].Split(',').Where(s => s.Length > 0).ToArray() : null;
                switch (a[0])
                {
                    case "list": return SeatList();
                    case "plan": return a.Length > 1 ? SeatPlanCommand(a[1], Ids(2)) : "refused: plan:<alias>[:id,…]";
                    case "apply": return a.Length > 1 ? SeatApply(a[1], Ids(2), command, null) : "refused: apply:<alias>[:id,…]";
                    case "apply-all": return SeatApplyAll(Ids(1));
                    case "verify": return a.Length > 1 ? SeatVerify(a[1]) : "refused: verify:<alias>";
                    case "revert": return a.Length > 1 ? SeatRevert(a[1], a.Length > 2 ? a[2] : null) : "refused: revert:<alias>[:id]";
                    default: return "refused: BuildingFix308.Seat list | plan:<alias>[:ids] | apply:<alias>[:ids] | apply-all[:ids] | verify:<alias> | revert:<alias>[:id]";
                }
            }
            catch (Exception e) { SeatDropBuilt(); return "FAILED: " + e; }
        }

        static string SeatList()
        {
            var seat = SeatLoad(out string err); if (seat == null) return "refused: " + err;
            var sb = new StringBuilder("seat308 " + seat.version + ": " + seat.rows.Length + " rows (" + seat.rows.Count(r => !r.disabled && r.op != "report") + " applied by default)\n");
            foreach (var r in seat.rows) sb.AppendLine("  " + (r.op == "report" ? "report  " : !r.disabled ? "enabled " : "proposal") + " " + r.id + " [" + r.op + "] " + r.path + (r.userCheck ? " (user check)" : "") + " — stills " + string.Join(",", r.stills));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ plan

        static string SeatPlanCommand(string alias, string[] ids)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var seat = SeatLoad(out err); if (seat == null) return "refused: " + err;
            var rows = SeatRows(seat, ids, true, out err); if (rows == null) return "refused: " + err;
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            try
            {
                var plan = SeatMakePlan(cfg, seat, alias, scene, rows);
                Directory.CreateDirectory(FixDir);
                string file = Path.Combine(FixDir, "seat-plan-" + alias + ".json");
                File.WriteAllText(file, JsonUtility.ToJson(plan, true), new UTF8Encoding(false));
                return SeatPlanText(plan) + "-> " + file;
            }
            finally { SeatDropBuilt(); GoBack(previous, scene, opened, true); }
        }

        static SeatPlan308 SeatMakePlan(BuildingAudit308.Config308 cfg, SeatCfg308 seat, string alias, Scene scene, List<SeatRow308> rows)
        {
            Physics.SyncTransforms();
            var plan = new SeatPlan308 { alias = alias, scene = scene.path, utc = BuildingAudit308.Utc(), sceneSha = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path)), seatSha = BuildingAudit308.Sha(SeatFile), quality = BuildingAudit308.QualityName() };
            var ledger = SeatLoadLedger(alias);
            foreach (var row in rows) plan.rows.Add(SeatPlanRow(cfg, seat, scene, ledger, row));
            return plan;
        }

        static string SeatPlanText(SeatPlan308 p)
        {
            var sb = new StringBuilder("seat plan " + p.alias + " (" + p.scene + ") sha " + p.sceneSha + " — " + p.rows.Count(r => r.status == "ready") + " ready, " + p.rows.Count(r => r.status == "already") + " already, " + p.rows.Count(r => r.status == "clean") + " clean, " + p.rows.Count(r => r.status == "blocked" || r.status == "mismatch") + " blocked, " + p.rows.Count(r => r.status == "absent") + " absent, " + p.rows.Count(r => r.status == "proposal") + " report\n");
            foreach (var r in p.rows)
            {
                sb.AppendLine("  " + r.id + " [" + r.op + "] " + r.status + (r.enabled ? "" : " (proposal row: applied only when named)") + (r.userCheck ? " (user check)" : "") + ": " + r.detail + " (" + r.changes.Count(c => c.state == "apply") + " to apply, " + r.changes.Count(c => c.state == "already") + " already)");
                foreach (var c in r.changes.Where(c => c.state == "apply" || c.state == "mismatch" || c.state == "blocked").Take(8)) sb.AppendLine("     " + c.state + " " + c.kind + " " + c.key + ": " + c.before + " -> " + c.after + (c.note.Length > 0 ? "  [" + c.note + "]" : ""));
                foreach (var n in r.notes.Take(8)) sb.AppendLine("     · " + n);
            }
            return sb.ToString();
        }

        static void SeatSettle(SeatRowPlan308 rp)
        {
            if (rp.status == "blocked" || rp.status == "absent" || rp.status == "proposal") return;
            if (rp.changes.Any(c => c.state == "blocked")) { rp.status = "blocked"; if (rp.detail.Length == 0) rp.detail = string.Join("; ", rp.changes.Where(c => c.state == "blocked").Select(c => c.note).Take(3)); return; }
            if (rp.changes.Any(c => c.state == "mismatch")) { rp.status = "mismatch"; rp.detail = "current values match neither before nor after: " + string.Join(", ", rp.changes.Where(c => c.state == "mismatch").Select(c => c.key).Take(4)) + " (revert:<alias>:" + rp.id + " or restore first)"; return; }
            if (rp.changes.Any(c => c.state == "apply")) { rp.status = "ready"; if (rp.detail.Length == 0) rp.detail = rp.changes.Count(c => c.state == "apply") + " change(s)"; return; }
            if (rp.changes.Any(c => c.state == "already")) { rp.status = "already"; if (rp.detail.Length == 0) rp.detail = "already applied"; return; }
            rp.status = "clean"; if (rp.detail.Length == 0) rp.detail = "nothing to do";
        }

        static SeatRowPlan308 SeatPlanRow(BuildingAudit308.Config308 cfg, SeatCfg308 seat, Scene scene, Ledger308 ledger, SeatRow308 row)
        {
            var rp = new SeatRowPlan308 { id = row.id, op = row.op, path = row.path, expect = row.expect, userCheck = row.userCheck, enabled = !row.disabled, stills = row.stills };
            if (row.op == "report") { rp.status = "proposal"; rp.detail = row.note; return rp; }
            string refused = SeatProtected(cfg, row.path);
            if (refused != null) { rp.status = "blocked"; rp.detail = "protected tree " + refused + " — report only"; return rp; }
            if (cfg.ProtectedAsset(seat.meshFolder + "/x")) { rp.status = "blocked"; rp.detail = "mesh folder is protected"; return rp; }
            var t = BuildingAudit308.Resolve(scene, row.path);
            if (t == null) { rp.status = "absent"; rp.detail = "absent " + row.path; return rp; }
            refused = SeatProtected(cfg, BuildingAudit308.PathOf(t));
            if (refused != null) { rp.status = "blocked"; rp.detail = "protected tree " + refused + " — report only"; return rp; }
            string step = "S:" + row.id;
            switch (row.op)
            {
                case "pose": SeatPlanPose(scene, ledger, row, t, step, rp); break;
                case "skirt": SeatPlanSkirt(ledger, row, t, step, rp); break;
                case "footing": SeatPlanChild(seat, row, t, step, "Footing308", rp, () => row.mode == "hull" ? SeatHull(scene, row, t) : SeatFooting(scene, row, t)); break;
                case "piers": SeatPlanChild(seat, row, t, step, "Piers308", rp, () => SeatPiers(row, t)); break;
                case "lid": SeatPlanChild(seat, row, t, step, "Lid308", rp, () => SeatLid(row, t)); break;
                case "treads": case "stretch": SeatPlanMeshEdit(seat, ledger, row, t, step, rp); break;
                case "active":
                {
                    string want = row.state == "1" ? "1" : "0", now = t.gameObject.activeSelf ? "1" : "0";
                    rp.changes.Add(new Change308 { step = step, key = BuildingAudit308.KeyOf(t), kind = "active", before = want == "1" ? "0" : "1", after = want, at = t.position, state = now == want ? (SeatPrior(ledger, step, BuildingAudit308.KeyOf(t), "active") != null ? "already" : "none") : "apply" });
                    if (now == want && rp.changes[0].state == "none") rp.notes.Add("already " + (want == "1" ? "active" : "inactive") + " (not by this ledger)");
                    break;
                }
                case "dedupe": SeatPlanDedupe(seat, ledger, row, t, step, rp); break;
                default: rp.status = "blocked"; rp.detail = "unknown op " + row.op; break;
            }
            SeatSettle(rp);
            return rp;
        }

        // a protected tree (Watershed295 / Reworld292 / MountainTrail285) or the attraction block anywhere on the path: the segment, else null
        static string SeatProtected(BuildingAudit308.Config308 cfg, string path)
        {
            foreach (var raw in (path ?? "").Split('/'))
            {
                int h = raw.LastIndexOf('#'); string seg = h > 0 ? raw.Substring(0, h) : raw;
                if (cfg.ProtectedRoot(seg) || (!string.IsNullOrEmpty(cfg.attractionRoot) && seg == cfg.attractionRoot)) return seg;
            }
            return null;
        }

        // ------------------------------------------------------------------ pose / skirt / dedupe

        static string SeatTrs(Vector3 p, Quaternion q, Vector3 s) => Pose(p, q) + "|" + string.Format(Inv, "{0:R},{1:R},{2:R}", s.x, s.y, s.z);
        static string SeatTrs(Transform t) => SeatTrs(t.localPosition, t.localRotation, t.localScale);
        static bool SeatParseTrs(string v, out Vector3 p, out Quaternion q, out Vector3 s)
        {
            p = Vector3.zero; q = Quaternion.identity; s = Vector3.one;
            var parts = (v ?? "").Split('|'); if (parts.Length != 3 || !ParsePose(parts[0] + "|" + parts[1], out p, out q)) return false;
            var a = parts[2].Split(',').Select(x => float.Parse(x, Inv)).ToArray(); if (a.Length != 3) return false;
            s = new Vector3(a[0], a[1], a[2]); return true;
        }
        static bool SeatSameTrs(Transform t, string v) => SeatParseTrs(v, out var p, out var q, out var s) && Vector3.Distance(t.localPosition, p) <= .005f && Quaternion.Angle(t.localRotation, q) <= .05f && Vector3.Distance(t.localScale, s) <= .001f;

        // renderer bounds of `t` when its local TRS is (p, q, s) — nothing moves
        static bool SeatBoundsAt(Transform t, Vector3 p, Quaternion q, Vector3 s, out Bounds b)
        {
            b = default; bool any = false;
            var parent = t.parent != null ? t.parent.localToWorldMatrix : Matrix4x4.identity;
            var m = parent * Matrix4x4.TRS(p, q, s) * t.worldToLocalMatrix;
            foreach (var r in t.GetComponentsInChildren<Renderer>(false))
            {
                var mesh = BuildingAudit308.MeshOf(r); if (mesh == null || !r.enabled) continue;
                var mb = mesh.bounds; var lw = m * r.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var w = lw.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(w, Vector3.zero); any = true; } else b.Encapsulate(w);
                }
            }
            return any;
        }

        static void SeatPlanPose(Scene scene, Ledger308 ledger, SeatRow308 row, Transform t, string step, SeatRowPlan308 rp)
        {
            string key = BuildingAudit308.KeyOf(t);
            var prior = SeatPrior(ledger, step, key, "trs");
            bool moving = false;
            if (prior != null)
            {
                string st = SeatSameTrs(t, prior.after) ? "already" : SeatSameTrs(t, prior.before) ? "apply" : "mismatch";
                rp.changes.Add(new Change308 { step = step, key = key, kind = "trs", before = prior.before, after = prior.after, note = prior.note, at = prior.at, state = st });
                moving = st == "apply";
            }
            else
            {
                var delta = new Vector3(row.dx, row.dy, row.dz);
                bool atFixed = false;
                if (row.expectAt.Length >= 3)
                {
                    var e = BuildingAudit308.Vec(row.expectAt);
                    if (Vector3.Distance(t.position, e) > row.tolerance)
                    {
                        if (delta != Vector3.zero && Vector3.Distance(t.position, e + delta) <= row.tolerance) atFixed = true;
                        else { rp.status = "blocked"; rp.detail = row.path + " is at " + BuildingAudit308.V(t.position) + ", neither at the audited origin " + BuildingAudit308.V(e) + " nor at the fixed pose (moved by F1 or by hand?) — nothing done"; return; }
                    }
                }
                if (atFixed) rp.changes.Add(new Change308 { step = step, key = key, kind = "trs", before = "", after = SeatTrs(t), at = t.position, state = "already", note = "at the fixed pose (no ledger in this scene)" });
                else
                {
                    var wp = t.position + delta; var wr = t.rotation; float tilt = Vector3.Angle(t.up, Vector3.up);
                    if (row.level && tilt > .5f) { var f = t.forward; f.y = 0; if (f.sqrMagnitude < 1e-6f) { f = t.up; f.y = 0; } wr = Quaternion.LookRotation(f.normalized, Vector3.up); }
                    if (row.level) rp.notes.Add("tilt of " + row.path + " now " + tilt.ToString("F2", Inv) + "°" + (tilt > .5f ? " -> yaw only" : " (≤ 0.5°: no rotation change; a lean seen in the still is not in this transform)"));
                    if (row.yaw != 0) wr = Quaternion.Euler(0, row.yaw, 0) * wr;
                    var lp = t.parent != null ? t.parent.InverseTransformPoint(wp) : wp;
                    var lq = t.parent != null ? Quaternion.Inverse(t.parent.rotation) * wr : wr;
                    var ls = t.localScale;
                    if (row.localPosition.Length >= 3) lp = BuildingAudit308.Vec(row.localPosition);
                    if (row.localScale.Length >= 3) ls = BuildingAudit308.Vec(row.localScale);
                    string after = SeatTrs(lp, lq, ls);
                    if (SeatSameTrs(t, after)) rp.notes.Add("pose already equals the target (no change)");
                    else
                    {
                        var ch = new Change308 { step = step, key = key, kind = "trs", before = SeatTrs(t), after = after, at = wp, state = "apply" };
                        SeatBoundsAt(t, t.localPosition, t.localRotation, t.localScale, out var b0); bool okB = SeatBoundsAt(t, lp, lq, ls, out var b1);
                        ch.note = "world " + BuildingAudit308.V(t.position) + " -> " + BuildingAudit308.V(t.parent != null ? t.parent.TransformPoint(lp) : lp) + "; bounds y " + b0.min.y.ToString("F2", Inv) + ".." + b0.max.y.ToString("F2", Inv) + " -> " + b1.min.y.ToString("F2", Inv) + ".." + b1.max.y.ToString("F2", Inv);
                        if (!string.IsNullOrEmpty(row.within))
                        {
                            var host = BuildingAudit308.Resolve(scene, row.within);
                            Bounds hb = default; bool anyH = false;
                            if (host != null) foreach (var r in host.GetComponentsInChildren<Renderer>(false)) { if (!r.enabled || r.transform.IsChildOf(t)) continue; if (!anyH) { hb = r.bounds; anyH = true; } else hb.Encapsulate(r.bounds); }
                            if (!anyH || !okB) { ch.state = "blocked"; ch.note = "within-check: no renderer bounds for " + row.within; }
                            else
                            {
                                hb.Expand(.6f);
                                if (!(hb.Contains(b1.min) && hb.Contains(b1.max))) { ch.state = "blocked"; ch.note = "the new bounds " + BuildingAudit308.V(b1.min) + ".." + BuildingAudit308.V(b1.max) + " leave " + row.within + " " + BuildingAudit308.V(hb.min) + ".." + BuildingAudit308.V(hb.max) + " — not applied (check the door by hand)"; }
                            }
                        }
                        rp.changes.Add(ch); moving = ch.state == "apply";
                    }
                }
            }
            try { foreach (var n in ContentNear(scene, t, 2f).Take(6)) rp.notes.Add("content at the object (moves with it only when it is a child): " + n); }
            catch (Exception e) { rp.notes.Add("content check skipped: " + e.Message); }
            if (!row.skirt) return;
            // the old #299 skirt was built for the old pose
            if (moving) foreach (var old in t.GetComponentsInChildren<Transform>(true).Where(x => x.name == "StoneSkirt299" && x.gameObject.activeSelf))
                rp.changes.Add(new Change308 { step = step, key = BuildingAudit308.KeyOf(old), kind = "active", before = "1", after = "0", state = "apply", note = "old skirt" });
            SeatPlanSkirt(ledger, row, t, step, rp);
        }

        static void SeatPlanSkirt(Ledger308 ledger, SeatRow308 row, Transform t, string step, SeatRowPlan308 rp)
        {
            var plate = string.IsNullOrEmpty(row.plate) ? t : t.Find(row.plate);
            if (plate == null || plate.parent == null || plate.GetComponent<MeshFilter>() == null) { rp.changes.Add(new Change308 { step = step, key = row.path + "/" + row.plate, kind = "skirt", state = "blocked", note = "plate " + row.plate + " not found or has no mesh under " + row.path }); return; }
            string key = BuildingAudit308.KeyOf(plate);
            bool has = plate.parent.Find("StoneSkirt308") != null;
            var prior = SeatPrior(ledger, step, key, "skirt");
            rp.changes.Add(new Change308 { step = step, key = key, kind = "skirt", before = "none", after = prior != null ? prior.after : "pending", at = plate.position, state = has ? "already" : "apply", note = has ? (prior == null ? "StoneSkirt308 present (not by this ledger)" : "") : "GroundFit299.StoneSkirt308 on the plate, evaluated at apply (after the move)" });
        }

        static string SeatTriKey(Vector3 a, Vector3 b, Vector3 c)
        {
            string K(Vector3 p) => Mathf.RoundToInt(p.x * 1000f).ToString(Inv) + "," + Mathf.RoundToInt(p.y * 1000f).ToString(Inv) + "," + Mathf.RoundToInt(p.z * 1000f).ToString(Inv);
            var k = new[] { K(a), K(b), K(c) }; Array.Sort(k, StringComparer.Ordinal);
            return k[0] + "|" + k[1] + "|" + k[2];
        }
        static HashSet<string> SeatTriKeys(Mesh mesh, Transform tr)
        {
            var set = new HashSet<string>(); if (mesh == null || !mesh.isReadable) return set;
            var m = tr.localToWorldMatrix; var w = mesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                var tri = mesh.GetTriangles(s);
                for (int i = 0; i + 2 < tri.Length; i += 3) set.Add(SeatTriKey(w[tri[i]], w[tri[i + 1]], w[tri[i + 2]]));
            }
            return set;
        }
        // a copy of `src` without the triangles whose three world positions (1 mm) repeat a triangle of the twin
        static Mesh SeatEditDedupe(Mesh src, Transform tr, HashSet<string> twin, out string stats, out Vector3 at)
        {
            stats = ""; at = tr.position;
            if (!src.isReadable) { stats = "mesh not readable"; return null; }
            var m = tr.localToWorldMatrix; var w = src.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray();
            var keep = new List<int[]>(); int removed = 0, total = 0; float area = 0; Vector3 sum = Vector3.zero;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                if (src.GetTopology(s) != MeshTopology.Triangles) { keep.Add(null); continue; }
                var tri = src.GetTriangles(s); var k = new List<int>(tri.Length);
                for (int i = 0; i + 2 < tri.Length; i += 3)
                {
                    Vector3 a = w[tri[i]], b = w[tri[i + 1]], c = w[tri[i + 2]]; total++;
                    if (twin.Contains(SeatTriKey(a, b, c))) { removed++; float ar = Vector3.Cross(b - a, c - a).magnitude * .5f; area += ar; sum += (a + b + c) / 3f * ar; continue; }
                    k.Add(tri[i]); k.Add(tri[i + 1]); k.Add(tri[i + 2]);
                }
                keep.Add(k.ToArray());
            }
            if (removed == 0) { stats = "triangles " + total + ": none repeats the twin"; return null; }
            if (removed == total) { stats = "every triangle repeats the twin (bounds differ: check by hand)"; return null; }
            stats = "triangles " + total + ": " + removed + " repeat the twin (" + area.ToString("F1", Inv) + " m2) -> removed from a mesh copy";
            if (area > 0) at = sum / area;
            var copy = Object.Instantiate(src);
            for (int s = 0; s < keep.Count; s++) if (keep[s] != null) copy.SetTriangles(keep[s], s);
            copy.RecalculateBounds();
            return copy;
        }

        // prefixB children against their prefixA twins: the same bounds (tolerance) -> the B object is switched off; only partly the
        // same (two halves that both carry the shared part) -> the triangles of B that repeat a twin triangle leave a copy of B's mesh
        static void SeatPlanDedupe(SeatCfg308 seat, Ledger308 ledger, SeatRow308 row, Transform t, string step, SeatRowPlan308 rp)
        {
            int twins = 0;
            var targets = new List<(Transform tr, string kind, Mesh mesh)>(); var twinKeys = new Dictionary<Transform, HashSet<string>>();
            foreach (Transform c in t)
            {
                if (string.IsNullOrEmpty(row.prefixB) || !c.name.StartsWith(row.prefixB, StringComparison.Ordinal) || (row.include.Length > 0 && !BuildingAudit308.HasAny(c.name, row.include))) continue;
                string key = BuildingAudit308.KeyOf(c);
                if (!c.gameObject.activeSelf) { if (SeatPrior(ledger, step, key, "active") != null) rp.changes.Add(new Change308 { step = step, key = key, kind = "active", before = "1", after = "0", state = "already", at = c.position }); else rp.notes.Add(c.name + " inactive already (not by this ledger)"); continue; }
                var cf = c.GetComponent<MeshFilter>(); var cc = c.GetComponent<MeshCollider>();
                // a mesh copy made by this row earlier: replay the ledger (the twin is not needed)
                if (SeatPrior(ledger, step, key, "mesh") != null || SeatPrior(ledger, step, key, "colmesh") != null)
                {
                    if (cf != null) targets.Add((c, "mesh", cf.sharedMesh));
                    if (cc != null) targets.Add((c, "colmesh", cc.sharedMesh));
                    continue;
                }
                var twin = t.Find(row.prefixA + c.name.Substring(row.prefixB.Length));
                if (twin == null || !twin.gameObject.activeSelf) { rp.notes.Add(c.name + ": no active twin " + row.prefixA + "… (kept)"); continue; }
                if (!RendererBounds(c, out var b1) || !RendererBounds(twin, out var b2)) { rp.notes.Add(c.name + ": no renderer bounds (kept)"); continue; }
                float dc = Vector3.Distance(b1.center, b2.center), ds = Vector3.Distance(b1.size, b2.size);
                if (dc <= row.tolerance && ds <= row.tolerance)
                {
                    twins++;
                    rp.changes.Add(new Change308 { step = step, key = key, kind = "active", before = "1", after = "0", state = "apply", at = b1.center, note = "same bounds as " + twin.name + " (centre Δ " + dc.ToString("F3", Inv) + ", size Δ " + ds.ToString("F3", Inv) + ")" });
                    continue;
                }
                var tf = twin.GetComponent<MeshFilter>();
                if (cf == null || cf.sharedMesh == null || tf == null || tf.sharedMesh == null) { rp.notes.Add(c.name + ": bounds differ from " + twin.name + " and there is no mesh pair to compare (kept)"); continue; }
                var keys = SeatTriKeys(tf.sharedMesh, twin);
                if (keys.Count == 0) { rp.notes.Add(c.name + ": twin mesh not readable (kept)"); continue; }
                twinKeys[c] = keys;
                targets.Add((c, "mesh", cf.sharedMesh));
                if (cc != null && cc.sharedMesh != null)
                {
                    var tc = twin.GetComponent<MeshCollider>();
                    // the collision surface must stay: only when the twin collides with the very mesh the triangles were matched against
                    if (cc.sharedMesh == cf.sharedMesh && tc != null && tc.enabled && tc.sharedMesh == tf.sharedMesh) targets.Add((c, "colmesh", cc.sharedMesh));
                    else rp.notes.Add(c.name + ": collider mesh kept (the twin does not collide with the compared mesh)");
                }
            }
            if (targets.Count > 0)
                SeatPlanMeshList(seat, ledger, row, step, rp, targets, (Mesh mesh, Transform tr, out string stats, out Vector3 at) =>
                {
                    if (!twinKeys.TryGetValue(tr, out var keys)) { stats = "no twin"; at = tr.position; return null; }
                    return SeatEditDedupe(mesh, tr, keys, out stats, out at);
                });
            int meshes = rp.changes.Count(c => c.kind == "mesh" && c.state == "apply");
            if (twins > 0 || meshes > 0) rp.detail = twins + " duplicate object(s) switched off, " + meshes + " mesh(es) lose the triangles their twin repeats";
        }

        // ------------------------------------------------------------------ generated children (footing / piers / lid)

        static string SeatSan(string key) { var sb = new StringBuilder(); foreach (char ch in key) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_'); string s = sb.ToString(); return s.Substring(Math.Max(0, s.Length - 70)); }
        static string SeatHash(SeatRow308 r, string extra) => BuildingAudit308.ShaText(string.Join("|", new[] { r.op, r.mode, r.path, r.material, string.Join(",", r.include), string.Join(",", r.exclude), string.Join(",", r.extra), string.Join(",", r.box.Select(x => x.ToString("R", Inv))), extra }) + "|" + string.Join("|", new[] { r.hover, r.sink, r.sample, r.vmax, r.maxGap, r.minSpan, r.clearance, r.seatGap, r.minProud, r.overlap, r.spacing, r.width, r.depth, r.minHeight, r.slabMaxWidth, r.smallTris, r.includeSlabs ? 1 : 0, r.noCollider ? 1 : 0 }.Select(x => x.ToString("R", Inv)))).Substring(0, 8);

        static void SeatPlanChild(SeatCfg308 seat, SeatRow308 row, Transform t, string step, string child, SeatRowPlan308 rp, Func<SeatBuild308> build)
        {
            string key = BuildingAudit308.KeyOf(t);
            string path = seat.meshFolder + "/" + SeatSan(key) + "_" + child + "_" + SeatHash(row, "") + ".asset";
            var existing = t.Find(child);
            if (existing != null)
            {
                var mf = existing.GetComponent<MeshFilter>(); string now = mf != null && mf.sharedMesh != null ? AssetDatabase.GetAssetPath(mf.sharedMesh) : "";
                rp.changes.Add(new Change308 { step = step, key = key, kind = "child", before = "none", after = path, note = child, at = existing.position, state = now == path ? "already" : "mismatch" });
                if (now != path) rp.notes.Add(child + " exists with " + now + " (row parameters changed since it was built): revert the row first");
                return;
            }
            var b = build();
            if (b == null || b.mesh == null) { rp.notes.Add(child + ": " + (b != null ? b.stats : "nothing to build")); if (b != null && b.stats.StartsWith("blocked", StringComparison.Ordinal)) { rp.status = "blocked"; rp.detail = b.stats; } return; }
            string reuse = SeatReuse(path, b.mesh, out bool differs);
            if (differs) { Object.DestroyImmediate(b.mesh); rp.changes.Add(new Change308 { step = step, key = key, kind = "child", before = "none", after = path, note = reuse, at = b.at, state = "blocked" }); return; }
            seatBuilt[path] = b;
            rp.detail = b.stats;
            rp.changes.Add(new Change308 { step = step, key = key, kind = "child", before = "none", after = path, note = child + "|" + b.stats + reuse, at = b.at, state = "apply" });
        }

        sealed class STri308 { public Vector3 a, b, c, n; public Vector2 ua, ub, uc; public int wa, wb, wc, mat, comp; }
        sealed class SSoup308
        {
            public readonly List<STri308> tris = new List<STri308>(); public readonly List<Material> mats = new List<Material>();
            public readonly Dictionary<Vector3Int, int> weld = new Dictionary<Vector3Int, int>(); public int[] compTris = Array.Empty<int>();
            public int Weld(Vector3 p) { var k = new Vector3Int(Mathf.RoundToInt(p.x * 1000f), Mathf.RoundToInt(p.y * 1000f), Mathf.RoundToInt(p.z * 1000f)); if (!weld.TryGetValue(k, out int id)) { id = weld.Count; weld[k] = id; } return id; }
        }

        static bool SeatLowLod(string n) => n == "LOD1" || n == "LOD2" || n.StartsWith("L1_", StringComparison.Ordinal) || n.StartsWith("L2_", StringComparison.Ordinal) || n.EndsWith("_LOD1", StringComparison.Ordinal) || n.EndsWith("_LOD2", StringComparison.Ordinal) || n.EndsWith("_L1", StringComparison.Ordinal) || n.EndsWith("_L2", StringComparison.Ordinal);
        static IEnumerable<Renderer> SeatLod0(Transform t, SeatRow308 row)
        {
            var lg = t.GetComponent<LODGroup>();
            IEnumerable<Renderer> rs = lg != null && lg.GetLODs().Length > 0 ? lg.GetLODs()[0].renderers.Where(r => r != null) : t.GetComponentsInChildren<Renderer>(false).Where(r => !SeatLowLod(r.name));
            return rs.Where(r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer) && !SeatChildren.Contains(r.name) && !r.name.StartsWith("StoneSkirt", StringComparison.Ordinal)
                                 && (row == null || ((row.include.Length == 0 || BuildingAudit308.HasAny(r.name, row.include)) && !BuildingAudit308.HasAny(r.name, row.exclude))));
        }

        static SSoup308 SeatSoup(IEnumerable<Renderer> rs, bool components)
        {
            var soup = new SSoup308(); var cache = new BuildingAudit308.MeshCache308();
            foreach (var r in rs)
            {
                var mesh = BuildingAudit308.MeshOf(r); var mt = cache.Get(mesh); if (mt == null) continue;
                var uv = mesh.isReadable ? mesh.uv : null; if (uv != null && uv.Length != mt.v.Length) uv = null;
                var m = r.localToWorldMatrix; var rm = r.sharedMaterials;
                for (int i = 0; i < mt.t.Length / 3; i++)
                {
                    int ia = mt.t[i * 3], ib = mt.t[i * 3 + 1], ic = mt.t[i * 3 + 2];
                    var tri = new STri308 { a = m.MultiplyPoint3x4(mt.v[ia]), b = m.MultiplyPoint3x4(mt.v[ib]), c = m.MultiplyPoint3x4(mt.v[ic]) };
                    var n = Vector3.Cross(tri.b - tri.a, tri.c - tri.a); if (n.sqrMagnitude < 1e-12f) continue;
                    tri.n = n.normalized;
                    if (uv != null) { tri.ua = uv[ia]; tri.ub = uv[ib]; tri.uc = uv[ic]; }
                    var mat = mt.sub[i] < rm.Length ? rm[mt.sub[i]] : null;
                    tri.mat = soup.mats.IndexOf(mat); if (tri.mat < 0) { tri.mat = soup.mats.Count; soup.mats.Add(mat); }
                    tri.wa = soup.Weld(tri.a); tri.wb = soup.Weld(tri.b); tri.wc = soup.Weld(tri.c);
                    soup.tris.Add(tri);
                }
            }
            if (!components) return soup;
            var parent = new int[soup.weld.Count]; for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            foreach (var tr in soup.tris) { parent[Find(tr.wb)] = Find(tr.wa); parent[Find(tr.wc)] = Find(tr.wa); }
            soup.compTris = new int[parent.Length];
            foreach (var tr in soup.tris) { tr.comp = Find(tr.wa); soup.compTris[tr.comp]++; }
            return soup;
        }

        static float SeatFlatLen(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
        static bool SeatInBox(SeatRow308 row, Vector3 p) => row.box.Length < 3 || new Vector2(p.x - row.box[0], p.z - row.box[1]).magnitude <= row.box[2];

        // highest face of the soup(s) under (x, z) in [lo, hi]; NaN when none
        static float SeatUnder(List<STri308> tris, float x, float z, float lo, float hi, float minUp)
        {
            float best = float.NaN;
            foreach (var t in tris)
            {
                if (t.n.y < minUp) continue;
                if (x < Mathf.Min(t.a.x, t.b.x, t.c.x) || x > Mathf.Max(t.a.x, t.b.x, t.c.x) || z < Mathf.Min(t.a.z, t.b.z, t.c.z) || z > Mathf.Max(t.a.z, t.b.z, t.c.z)) continue;
                if (BuildingAudit308.VerticalHit(t.a, t.b, t.c, x, z, out float y) && y >= lo && y <= hi && (float.IsNaN(best) || y > best)) best = y;
            }
            return best;
        }

        sealed class SQuads308
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector3> n = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>();
            public readonly Dictionary<int, List<int>> sub = new Dictionary<int, List<int>>();
            public void Quad(int mat, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); n.Add(normal); n.Add(normal); n.Add(normal); n.Add(normal); uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                if (!sub.TryGetValue(mat, out var l)) sub[mat] = l = new List<int>();
                bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0;
                if (flip) l.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 }); else l.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            public Mesh ToMesh(Transform local, List<Material> all, out Material[] mats)
            {
                var mesh = new Mesh(); if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v.Select(p => local.InverseTransformPoint(p)).ToList()); mesh.SetNormals(n.Select(x => local.InverseTransformDirection(x)).ToList()); mesh.SetUVs(0, uv);
                var keys = sub.Keys.OrderBy(k => k).ToList(); mesh.subMeshCount = keys.Count;
                for (int s = 0; s < keys.Count; s++) mesh.SetTriangles(sub[keys[s]], s);
                mats = keys.Select(k => k >= 0 && k < all.Count ? all[k] : null).ToArray();
                mesh.RecalculateTangents(); mesh.RecalculateBounds();
                return mesh;
            }
        }

        // wall faces continued down to the ground: bottom edges (open, or meeting a downward face) of near-vertical faces whose
        // samples float more than `hover` above the terrain / the object's own (and `extra` objects') geometry
        static SeatBuild308 SeatFooting(Scene scene, SeatRow308 row, Transform t)
        {
            var soup = SeatSoup(SeatLod0(t, row), true);
            if (soup.tris.Count == 0) return new SeatBuild308 { stats = "no LOD0 triangles under " + row.path };
            if (soup.tris.Count > row.maxTris) return new SeatBuild308 { stats = "blocked: " + soup.tris.Count + " triangles > maxTris " + row.maxTris + " (narrow with include / use mode hull)" };
            var support = new List<STri308>(soup.tris);
            foreach (var e in row.extra) { var x = BuildingAudit308.Resolve(scene, e); if (x != null) support.AddRange(SeatSoup(SeatLod0(x, null), false).tris); }
            var edges = new Dictionary<long, List<int>>();
            void Use(int a, int b, int ti) { long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a; if (!edges.TryGetValue(k, out var l)) edges[k] = l = new List<int>(); l.Add(ti); }
            for (int i = 0; i < soup.tris.Count; i++) { var tr = soup.tris[i]; Use(tr.wa, tr.wb, i); Use(tr.wb, tr.wc, i); Use(tr.wc, tr.wa, i); }
            var q = new SQuads308(); int used = 0, candidates = 0, held = 0; float worst = 0, area = 0; Vector3 worstAt = t.position;
            for (int ti = 0; ti < soup.tris.Count; ti++)
            {
                var tr = soup.tris[ti];
                if (Mathf.Abs(tr.n.y) > row.vmax || (!row.includeSlabs && soup.compTris[tr.comp] <= row.smallTris)) continue;
                for (int e = 0; e < 3; e++)
                {
                    Vector3 p = e == 0 ? tr.a : e == 1 ? tr.b : tr.c, pq = e == 0 ? tr.b : e == 1 ? tr.c : tr.a, r = e == 0 ? tr.c : e == 1 ? tr.a : tr.b;
                    Vector2 up = e == 0 ? tr.ua : e == 1 ? tr.ub : tr.uc, uq = e == 0 ? tr.ub : e == 1 ? tr.uc : tr.ua, ur = e == 0 ? tr.uc : e == 1 ? tr.ua : tr.ub;
                    int wp = e == 0 ? tr.wa : e == 1 ? tr.wb : tr.wc, wq = e == 0 ? tr.wb : e == 1 ? tr.wc : tr.wa;
                    var users = edges[wp < wq ? ((long)wp << 32) | (uint)wq : ((long)wq << 32) | (uint)wp];
                    bool open = users.Count == 1;
                    if (!open && !(users.Count == 2 && soup.tris[users[0] == ti ? users[1] : users[0]].n.y < -.5f)) continue;
                    float len = SeatFlatLen(p, pq); if (len < .05f) continue;
                    float s = Vector2.Dot(new Vector2(r.x - p.x, r.z - p.z), new Vector2(pq.x - p.x, pq.z - p.z)) / (len * len);
                    if (r.y <= p.y + s * (pq.y - p.y) + .05f) continue;          // the face must rise from this edge
                    if (!SeatInBox(row, (p + pq) * .5f)) continue;
                    candidates++;
                    int k = Mathf.Max(1, Mathf.CeilToInt(len / Mathf.Max(.1f, row.sample)));
                    var pts = new Vector3[k + 1]; for (int i = 0; i <= k; i++) pts[i] = Vector3.Lerp(p, pq, i / (float)k);
                    float edgeLow = Mathf.Min(p.y, pq.y);
                    if (open)
                    {
                        // an open edge that only meets another face of the object (a wall standing on its plinth) is not a bottom
                        int touching = 0;
                        foreach (var x in pts)
                            foreach (var o in soup.tris)
                            {
                                if (o == tr || Mathf.Min(o.a.y, o.b.y, o.c.y) > edgeLow - .05f) continue;
                                if (x.x < Mathf.Min(o.a.x, o.b.x, o.c.x) - .03f || x.x > Mathf.Max(o.a.x, o.b.x, o.c.x) + .03f || x.z < Mathf.Min(o.a.z, o.b.z, o.c.z) - .03f || x.z > Mathf.Max(o.a.z, o.b.z, o.c.z) + .03f) continue;
                                if ((BuildingAudit308.ClosestOnTri(x, o.a, o.b, o.c) - x).sqrMagnitude < .02f * .02f) { touching++; break; }
                            }
                        if (touching > (k + 1) / 2) { held++; continue; }
                    }
                    var nh = new Vector3(tr.n.x, 0, tr.n.z).normalized;
                    var floor = new float[k + 1]; int floating = 0; float edgeWorst = 0;
                    for (int i = 0; i <= k; i++)
                    {
                        float g = BuildingAudit308.TerrainTop(pts[i]);
                        if (float.IsNaN(g)) { floor[i] = pts[i].y; continue; }
                        var o = pts[i] + nh * .05f;
                        float own = SeatUnder(support, o.x, o.z, g - 1f, pts[i].y - .05f, -2f);
                        floor[i] = float.IsNaN(own) ? g : Mathf.Max(g, own);
                        float gap = pts[i].y - floor[i];
                        if (gap > row.hover) floating++;
                        edgeWorst = Mathf.Max(edgeWorst, gap);
                    }
                    if (floating * 2 < k + 1 || edgeWorst > row.maxGap) continue;
                    used++; if (edgeWorst > worst) { worst = edgeWorst; worstAt = (p + pq) * .5f; }
                    // the face's own UV mapping continued downward (per metre of drop)
                    Vector3 e1 = pq - p, e2 = r - p; float a11 = Vector3.Dot(e1, e1), a12 = Vector3.Dot(e1, e2), a22 = Vector3.Dot(e2, e2), det = a11 * a22 - a12 * a12;
                    Vector2 drop = Vector2.zero;
                    if (Mathf.Abs(det) > 1e-9f) { float b1 = Vector3.Dot(e1, Vector3.down), b2 = Vector3.Dot(e2, Vector3.down); float cs = (b1 * a22 - b2 * a12) / det, ct = (a11 * b2 - a12 * b1) / det; drop = cs * (uq - up) + ct * (ur - up); }
                    for (int i = 0; i < k; i++)
                    {
                        float h0 = Mathf.Max(.02f, pts[i].y - floor[i] + row.sink), h1 = Mathf.Max(.02f, pts[i + 1].y - floor[i + 1] + row.sink);
                        var u0 = Vector2.Lerp(up, uq, i / (float)k); var u1 = Vector2.Lerp(up, uq, (i + 1) / (float)k);
                        q.Quad(tr.mat, pts[i], pts[i + 1], pts[i + 1] + Vector3.down * h1, pts[i] + Vector3.down * h0, nh, u0, u1, u1 + drop * h1, u0 + drop * h0);
                        area += (h0 + h1) * .5f * len / k;
                    }
                }
            }
            string stats = "bottom edges " + candidates + " (" + held + " stand on another face), floating > " + row.hover.ToString("F2", Inv) + " m: " + used + ", worst " + worst.ToString("F2", Inv) + " m, +" + (q.v.Count / 2) + " tris, face " + area.ToString("F0", Inv) + " m2";
            if (used == 0) return new SeatBuild308 { stats = stats + " — nothing floats" };
            var mesh = q.ToMesh(t, soup.mats, out var mats);
            return new SeatBuild308 { mesh = mesh, mats = mats, stats = stats, at = worstAt };
        }

        static List<Vector2> SeatConvexHull(List<Vector2> pts)
        {
            var p = pts.Distinct().OrderBy(a => a.x).ThenBy(a => a.y).ToList(); if (p.Count < 3) return p;
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var h = new List<Vector2>();
            foreach (var x in p) { while (h.Count >= 2 && Cross(h[h.Count - 2], h[h.Count - 1], x) <= 0) h.RemoveAt(h.Count - 1); h.Add(x); }
            int lower = h.Count + 1;
            for (int i = p.Count - 2; i >= 0; i--) { var x = p[i]; while (h.Count >= lower && Cross(h[h.Count - 2], h[h.Count - 1], x) <= 0) h.RemoveAt(h.Count - 1); h.Add(x); }
            h.RemoveAt(h.Count - 1); return h;
        }
        static Material SeatMaterial(SeatRow308 row, SSoup308 soup)
        {
            if (!string.IsNullOrEmpty(row.material)) { var m = AssetDatabase.LoadAssetAtPath<Material>(row.material); if (m != null) return m; }
            return soup.tris.GroupBy(x => x.mat).OrderByDescending(g => g.Count()).Select(g => soup.mats[g.Key]).FirstOrDefault(m => m != null);
        }

        // a revetment under the outline (convex hull, inside `box` when given) of a deck that floats: outline sample -> lowest own face -> ground
        static SeatBuild308 SeatHull(Scene scene, SeatRow308 row, Transform t)
        {
            var soup = SeatSoup(SeatLod0(t, row), false);
            var inside = soup.tris.Where(x => SeatInBox(row, x.a) && SeatInBox(row, x.b) && SeatInBox(row, x.c)).ToList();
            if (inside.Count == 0) return new SeatBuild308 { stats = "no LOD0 triangles in the box under " + row.path };
            var hull = SeatConvexHull(inside.SelectMany(x => new[] { new Vector2(x.a.x, x.a.z), new Vector2(x.b.x, x.b.z), new Vector2(x.c.x, x.c.z) }).ToList());
            if (hull.Count < 3) return new SeatBuild308 { stats = "degenerate outline" };
            var centre = new Vector2(hull.Average(p => p.x), hull.Average(p => p.y));
            var mat = SeatMaterial(row, soup); var q = new SQuads308(); float worst = 0, area = 0, run = 0; Vector3 worstAt = t.position; int walls = 0;
            for (int i = 0; i < hull.Count; i++)
            {
                Vector2 a = hull[i], b = hull[(i + 1) % hull.Count]; float len = (b - a).magnitude; if (len < .05f) continue;
                int k = Mathf.Max(1, Mathf.CeilToInt(len / Mathf.Max(.1f, row.sample)));
                var top = new float[k + 1]; var foot = new float[k + 1];
                for (int j = 0; j <= k; j++)
                {
                    var p = Vector2.Lerp(a, b, j / (float)k); top[j] = float.NaN;
                    float g = BuildingAudit308.TerrainTop(new Vector3(p.x, 0, p.y)); if (float.IsNaN(g)) continue;
                    foreach (float inset in new[] { .15f, .5f, 1f })
                    {
                        var o = Vector2.MoveTowards(p, centre, inset); float low = float.NaN;
                        foreach (var x in inside) if (BuildingAudit308.VerticalHit(x.a, x.b, x.c, o.x, o.y, out float y) && y > g - .5f && (float.IsNaN(low) || y < low)) low = y;
                        if (!float.IsNaN(low)) { top[j] = low; break; }
                    }
                    foot[j] = g;
                }
                var dir = (b - a) / len; var nrm = new Vector3(dir.y, 0, -dir.x); if (Vector2.Dot(new Vector2(nrm.x, nrm.z), (a + b) * .5f - centre) < 0) nrm = -nrm;
                for (int j = 0; j < k; j++)
                {
                    if (float.IsNaN(top[j]) || float.IsNaN(top[j + 1])) continue;
                    float g0 = top[j] - foot[j], g1 = top[j + 1] - foot[j + 1];
                    if (Mathf.Max(g0, g1) <= row.hover || Mathf.Max(g0, g1) > row.maxGap) continue;
                    var p0 = Vector2.Lerp(a, b, j / (float)k); var p1 = Vector2.Lerp(a, b, (j + 1) / (float)k);
                    Vector3 t0 = new Vector3(p0.x, top[j] + .02f, p0.y), t1 = new Vector3(p1.x, top[j + 1] + .02f, p1.y), b0 = new Vector3(p0.x, Mathf.Min(top[j], foot[j] - row.sink), p0.y), b1 = new Vector3(p1.x, Mathf.Min(top[j + 1], foot[j + 1] - row.sink), p1.y);
                    float u0 = (run + len * j / k) * .5f, u1 = (run + len * (j + 1) / k) * .5f;
                    q.Quad(0, t0, t1, b1, b0, nrm, new Vector2(u0, t0.y * .5f), new Vector2(u1, t1.y * .5f), new Vector2(u1, b1.y * .5f), new Vector2(u0, b0.y * .5f));
                    walls++; area += (t0.y - b0.y + t1.y - b1.y) * .5f * len / k;
                    if (Mathf.Max(g0, g1) > worst) { worst = Mathf.Max(g0, g1); worstAt = (t0 + t1) * .5f; }
                }
                run += len;
            }
            float hullArea = 0; for (int i = 0; i < hull.Count; i++) { var a = hull[i]; var b = hull[(i + 1) % hull.Count]; hullArea += a.x * b.y - b.x * a.y; }
            string stats = "outline " + hull.Count + " points, " + (Mathf.Abs(hullArea) * .5f).ToString("F0", Inv) + " m2; floating spans " + walls + ", worst " + worst.ToString("F2", Inv) + " m, face " + area.ToString("F0", Inv) + " m2, material " + (mat != null ? mat.name : "none");
            if (walls == 0) return new SeatBuild308 { stats = stats + " — nothing floats" };
            if (mat == null) return new SeatBuild308 { stats = "blocked: no material (row.material)" };
            var mesh = q.ToMesh(t, new List<Material> { mat }, out var mats);
            return new SeatBuild308 { mesh = mesh, mats = mats, stats = stats, at = worstAt };
        }

        static void SeatBox(SQuads308 q, Vector3 centreBottom, Vector3 along, float w, float d, float h)
        {
            var side = Vector3.Cross(Vector3.up, along).normalized; Vector3 x = along * (w * .5f), z = side * (d * .5f), up = Vector3.up * h;
            var c = new[] { centreBottom - x - z, centreBottom + x - z, centreBottom + x + z, centreBottom - x + z };
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = c[i], b = c[(i + 1) % 4]; var n = Vector3.Cross(b - a, Vector3.up).normalized; if (Vector3.Dot(n, (a + b) * .5f - centreBottom) < 0) n = -n;
                float l = (b - a).magnitude * .5f;
                q.Quad(0, a + up, b + up, b, a, n, new Vector2(0, h * .5f), new Vector2(l, h * .5f), new Vector2(l, 0), new Vector2(0, 0));
            }
        }

        // piers under an elevated deck: stations every `spacing` m along its long axis, from the ground to the lowest face above
        static SeatBuild308 SeatPiers(SeatRow308 row, Transform t)
        {
            var soup = SeatSoup(SeatLod0(t, row), false);
            if (soup.tris.Count == 0) return new SeatBuild308 { stats = "no LOD0 triangles under " + row.path };
            var pts = soup.tris.SelectMany(x => new[] { x.a, x.b, x.c }).ToList();
            var mean = new Vector2(pts.Average(p => p.x), pts.Average(p => p.z));
            float sxx = 0, szz = 0, sxz = 0; foreach (var p in pts) { float dx = p.x - mean.x, dz = p.z - mean.y; sxx += dx * dx; szz += dz * dz; sxz += dx * dz; }
            float ang = .5f * Mathf.Atan2(2 * sxz, sxx - szz); var ax = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
            float lo = pts.Min(p => (p.x - mean.x) * ax.x + (p.z - mean.y) * ax.z), hi = pts.Max(p => (p.x - mean.x) * ax.x + (p.z - mean.y) * ax.z);
            var own = new HashSet<Collider>(t.GetComponentsInChildren<Collider>(true));
            var mat = SeatMaterial(row, soup); var q = new SQuads308(); int piers = 0, blocked = 0, low = 0; float tallest = 0; Vector3 at = t.position; var rot = Quaternion.LookRotation(ax, Vector3.up);
            for (float s = lo + row.spacing * .5f; s < hi; s += row.spacing)
            {
                var p = new Vector3(mean.x + ax.x * s, 0, mean.y + ax.z * s);
                float g = BuildingAudit308.TerrainTop(p); if (float.IsNaN(g)) continue;
                float deck = float.NaN;
                foreach (var x in soup.tris) if (BuildingAudit308.VerticalHit(x.a, x.b, x.c, p.x, p.z, out float y) && y > g + .05f && (float.IsNaN(deck) || y < deck)) deck = y;
                if (float.IsNaN(deck) || deck - g < row.minHeight) { low++; continue; }
                float h = deck - g;
                bool hit = Physics.OverlapBox(new Vector3(p.x, g + h * .5f, p.z), new Vector3(row.depth * .5f, Mathf.Max(.05f, h * .5f - .15f), row.width * .5f), rot, ~0, QueryTriggerInteraction.Ignore).Any(c => !own.Contains(c) && !BuildingAudit308.TerrainLike(c));
                if (hit) { blocked++; continue; }
                SeatBox(q, new Vector3(p.x, g - row.sink, p.z), ax, row.width, row.depth, h + row.sink + .05f);
                piers++; if (h > tallest) { tallest = h; at = new Vector3(p.x, g + h * .5f, p.z); }
            }
            string stats = "axis " + (hi - lo).ToString("F1", Inv) + " m, piers " + piers + " every " + row.spacing.ToString("F1", Inv) + " m (tallest " + tallest.ToString("F2", Inv) + " m), stations skipped: " + blocked + " inside other colliders, " + low + " lower than " + row.minHeight.ToString("F1", Inv) + " m, material " + (mat != null ? mat.name : "none");
            if (piers == 0) return new SeatBuild308 { stats = stats + " — nothing to build" };
            if (mat == null) return new SeatBuild308 { stats = "blocked: no material (row.material)" };
            var mesh = q.ToMesh(t, new List<Material> { mat }, out var mats);
            return new SeatBuild308 { mesh = mesh, mats = mats, stats = stats, at = at };
        }

        // a top plate over a hollow masonry box: the open top edges of near-vertical faces inside `box` (x, z, radius[, y]) -> convex outline
        static SeatBuild308 SeatLid(SeatRow308 row, Transform t)
        {
            if (row.box.Length < 3) return new SeatBuild308 { stats = "blocked: lid needs box [x, z, radius(, y)]" };
            var soup = SeatSoup(SeatLod0(t, row), false);
            var edges = new Dictionary<long, int>();
            void Use(int a, int b) { long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a; edges[k] = edges.TryGetValue(k, out int n) ? n + 1 : 1; }
            foreach (var tr in soup.tris) { Use(tr.wa, tr.wb); Use(tr.wb, tr.wc); Use(tr.wc, tr.wa); }
            var rim = new List<Vector3>();
            foreach (var tr in soup.tris)
            {
                if (Mathf.Abs(tr.n.y) > row.vmax) continue;
                for (int e = 0; e < 3; e++)
                {
                    Vector3 p = e == 0 ? tr.a : e == 1 ? tr.b : tr.c, pq = e == 0 ? tr.b : e == 1 ? tr.c : tr.a, r = e == 0 ? tr.c : e == 1 ? tr.a : tr.b;
                    int wp = e == 0 ? tr.wa : e == 1 ? tr.wb : tr.wc, wq = e == 0 ? tr.wb : e == 1 ? tr.wc : tr.wa;
                    if (edges[wp < wq ? ((long)wp << 32) | (uint)wq : ((long)wq << 32) | (uint)wp] != 1) continue;
                    if (r.y >= Mathf.Min(p.y, pq.y) - .05f || Mathf.Abs(p.y - pq.y) > .05f || !SeatInBox(row, p) || !SeatInBox(row, pq)) continue;   // a level, open TOP edge
                    rim.Add(p); rim.Add(pq);
                }
            }
            if (rim.Count < 6) return new SeatBuild308 { stats = "open top edges in the box: " + rim.Count / 2 + " — no hollow box found" };
            float yRef = row.box.Length >= 4 ? row.box[3] : rim.Max(p => p.y);
            float y = rim.OrderBy(p => Mathf.Abs(p.y - yRef)).First().y;
            var level = rim.Where(p => Mathf.Abs(p.y - y) <= .3f).ToList();
            var hull = SeatConvexHull(level.Select(p => new Vector2(p.x, p.z)).ToList());
            if (hull.Count < 3) return new SeatBuild308 { stats = "rim at y " + y.ToString("F2", Inv) + " is degenerate" };
            float areaH = 0; for (int i = 0; i < hull.Count; i++) { var a = hull[i]; var b = hull[(i + 1) % hull.Count]; areaH += a.x * b.y - b.x * a.y; }
            areaH = Mathf.Abs(areaH) * .5f;
            var mat = SeatMaterial(row, soup);
            var mesh = new Mesh();
            float top = y - .02f; var c0 = new Vector2(hull.Average(p => p.x), hull.Average(p => p.y));
            var v = new List<Vector3> { new Vector3(c0.x, top, c0.y) }; v.AddRange(hull.Select(p => new Vector3(p.x, top, p.y)));
            var tris = new List<int>();
            for (int i = 0; i < hull.Count; i++) { int a = 1 + i, b = 1 + (i + 1) % hull.Count; if (Vector3.Cross(v[a] - v[0], v[b] - v[0]).y > 0) tris.AddRange(new[] { 0, a, b }); else tris.AddRange(new[] { 0, b, a }); }
            mesh.SetVertices(v.Select(p => t.InverseTransformPoint(p)).ToList()); mesh.SetUVs(0, v.Select(p => new Vector2(p.x * .5f, p.z * .5f)).ToList()); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            string stats = "open top edges " + rim.Count / 2 + " (" + level.Count / 2 + " at y " + y.ToString("F2", Inv) + "), lid outline " + hull.Count + " points, " + areaH.ToString("F0", Inv) + " m2, material " + (mat != null ? mat.name : "none");
            if (mat == null) { Object.DestroyImmediate(mesh); return new SeatBuild308 { stats = "blocked: no material (row.material)" }; }
            return new SeatBuild308 { mesh = mesh, mats = new[] { mat }, stats = stats, at = new Vector3(c0.x, top, c0.y) };
        }

        // ------------------------------------------------------------------ mesh edits (treads / stretch): a copy of the mesh, the object repointed

        static string SeatMeshRef(Mesh m) => m == null ? "" : AssetDatabase.GetAssetPath(m) + "#" + m.name;
        static Mesh SeatLoadMesh(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return null;
            string path = reference.Split('#')[0];
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault(m => reference.EndsWith("#" + m.name, StringComparison.Ordinal)) ?? AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        delegate Mesh SeatEdit308(Mesh src, Transform tr, out string stats, out Vector3 at);

        // the asset a plan wants to write already exists (an earlier scene of the chain built it): reuse only the same shape
        static string SeatReuse(string path, Mesh fresh, out bool differs)
        {
            differs = false;
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (old == null || fresh == null) return "";
            long io = 0, inew = 0; for (int s = 0; s < old.subMeshCount; s++) io += (long)old.GetIndexCount(s); for (int s = 0; s < fresh.subMeshCount; s++) inew += (long)fresh.GetIndexCount(s);
            differs = old.vertexCount != fresh.vertexCount || old.subMeshCount != fresh.subMeshCount || io != inew || Vector3.Distance(old.bounds.center, fresh.bounds.center) > .02f || Vector3.Distance(old.bounds.size, fresh.bounds.size) > .02f;
            return differs
                ? "the mesh asset " + path + " on disk has another shape (" + old.vertexCount + " verts / " + io + " indices, here " + fresh.vertexCount + " / " + inew + "): this scene differs at the object from the scene it was built in, or the asset is left from a reverted row — delete the asset when no scene references it, then plan again"
                : " (REUSE the mesh asset built in an earlier scene)";
        }

        static void SeatPlanMeshEdit(SeatCfg308 seat, Ledger308 ledger, SeatRow308 row, Transform t, string step, SeatRowPlan308 rp)
        {
            var targets = new List<(Transform tr, string kind, Mesh mesh)>();
            foreach (var f in t.GetComponentsInChildren<MeshFilter>(true)) if (!SeatChildren.Contains(f.name) && !f.name.StartsWith("StoneSkirt", StringComparison.Ordinal)) targets.Add((f.transform, "mesh", f.sharedMesh));
            foreach (var c in t.GetComponentsInChildren<MeshCollider>(true)) if (!SeatChildren.Contains(c.name)) targets.Add((c.transform, "colmesh", c.sharedMesh));
            SeatPlanMeshList(seat, ledger, row, step, rp, targets, (Mesh mesh, Transform tr, out string stats, out Vector3 at) => row.op == "treads" ? SeatEditTreads(mesh, tr, row, out stats, out at) : SeatEditStretch(mesh, tr, row, out stats, out at));
        }

        static void SeatPlanMeshList(SeatCfg308 seat, Ledger308 ledger, SeatRow308 row, string step, SeatRowPlan308 rp, List<(Transform tr, string kind, Mesh mesh)> targets, SeatEdit308 editor)
        {
            var done = new Dictionary<Mesh, string>();   // one copy per source mesh (a collider may share the LOD mesh)
            var summary = new List<string>();
            foreach (var (tr, kind, mesh) in targets)
            {
                string key = BuildingAudit308.KeyOf(tr), now = SeatMeshRef(mesh);
                var prior = SeatPrior(ledger, step, key, kind);
                if (prior != null)
                {
                    string st = now == prior.after ? "already" : now == prior.before ? "apply" : "mismatch";
                    if (st == "apply" && SeatLoadMesh(prior.after) == null) { st = "blocked"; rp.notes.Add("ledger mesh " + prior.after + " is gone: revert the row, then apply again"); }
                    rp.changes.Add(new Change308 { step = step, key = key, kind = kind, before = prior.before, after = prior.after, note = prior.note, at = prior.at, state = st });
                    continue;
                }
                if (mesh == null) continue;
                string src = AssetDatabase.GetAssetPath(mesh);
                if (src.StartsWith(seat.meshFolder + "/", StringComparison.Ordinal)) { rp.notes.Add(key + " already uses a seat mesh " + src + " (no ledger entry in this scene)"); continue; }
                if (done.TryGetValue(mesh, out string shared)) { if (shared != null) rp.changes.Add(new Change308 { step = step, key = key, kind = kind, before = now, after = shared, state = "apply", at = tr.position, note = "same source mesh" }); continue; }
                string name = SeatSan(key) + "_" + row.op + "_" + SeatHash(row, src + "#" + mesh.name + "#" + mesh.vertexCount);
                string path = seat.meshFolder + "/" + name + ".asset";
                string stats; Vector3 at;
                var edit = editor(mesh, tr, out stats, out at);
                summary.Add(tr.name + ": " + stats);
                if (edit == null) { done[mesh] = null; continue; }
                edit.name = name;
                string reuse = SeatReuse(path, edit, out bool differs);
                if (differs) { Object.DestroyImmediate(edit); done[mesh] = null; rp.changes.Add(new Change308 { step = step, key = key, kind = kind, before = now, after = path + "#" + name, state = "blocked", at = at, note = reuse }); continue; }
                seatBuilt[path] = new SeatBuild308 { mesh = edit, stats = stats, at = at };
                done[mesh] = path + "#" + name;
                rp.changes.Add(new Change308 { step = step, key = key, kind = kind, before = now, after = path + "#" + name, state = "apply", at = at, note = stats + reuse });
            }
            rp.notes.AddRange(summary);
            if (summary.Count > 0) rp.detail = summary[0];
        }

        static Mesh SeatEditStretch(Mesh src, Transform tr, SeatRow308 row, out string stats, out Vector3 at)
        {
            stats = ""; at = tr.position;
            if (!src.isReadable) { stats = "mesh not readable"; return null; }
            if (row.overlap <= 0) { stats = "overlap 0"; return null; }
            var m = tr.localToWorldMatrix; var w = src.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray(); if (w.Length == 0) { stats = "empty mesh"; return null; }
            var mean = new Vector2(w.Average(p => p.x), w.Average(p => p.z));
            float sxx = 0, szz = 0, sxz = 0; foreach (var p in w) { float dx = p.x - mean.x, dz = p.z - mean.y; sxx += dx * dx; szz += dz * dz; sxz += dx * dz; }
            float ang = .5f * Mathf.Atan2(2 * sxz, sxx - szz); var ax = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
            float lo = float.MaxValue, hi = float.MinValue; foreach (var p in w) { float s = (p.x - mean.x) * ax.x + (p.z - mean.y) * ax.z; lo = Mathf.Min(lo, s); hi = Mathf.Max(hi, s); }
            float len = hi - lo; if (len < 1f) { stats = "segment shorter than 1 m"; return null; }
            float k = (len + 2 * row.overlap) / len, mid = (lo + hi) * .5f;
            for (int i = 0; i < w.Length; i++) { float s = (w[i].x - mean.x) * ax.x + (w[i].z - mean.y) * ax.z - mid; w[i] += ax * (s * (k - 1)); }
            var inv = tr.worldToLocalMatrix; var copy = Object.Instantiate(src); copy.vertices = w.Select(p => inv.MultiplyPoint3x4(p)).ToArray(); copy.RecalculateBounds();
            at = new Vector3(mean.x + ax.x * hi, w.Average(p => p.y), mean.y + ax.z * hi);
            stats = "length " + len.ToString("F2", Inv) + " m -> " + (len + 2 * row.overlap).ToString("F2", Inv) + " m (each end +" + row.overlap.ToString("F2", Inv) + " m along " + ax.x.ToString("F2", Inv) + "," + ax.z.ToString("F2", Inv) + ")";
            return copy;
        }

        // wall-walk / stair slabs (small box components): ends pulled in to the curb faces and to the edge of the walk under them, the
        // slab lowered until it rests on the walk (its top stays minProud above), removed when no walk is under it or it gets too short
        static Mesh SeatEditTreads(Mesh src, Transform tr, SeatRow308 row, out string stats, out Vector3 at)
        {
            stats = ""; at = tr.position;
            if (!src.isReadable) { stats = "mesh not readable"; return null; }
            var m = tr.localToWorldMatrix; var w = src.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray(); int n = w.Length;
            var map = new Dictionary<Vector3Int, int>(); var weld = new int[n];
            for (int i = 0; i < n; i++) { var k = new Vector3Int(Mathf.RoundToInt(w[i].x * 1000f), Mathf.RoundToInt(w[i].y * 1000f), Mathf.RoundToInt(w[i].z * 1000f)); if (!map.TryGetValue(k, out int id)) { id = map.Count; map[k] = id; } weld[i] = id; }
            var parent = new int[map.Count]; for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            var subs = new List<int[]>(); for (int s = 0; s < src.subMeshCount; s++) subs.Add(src.GetTopology(s) == MeshTopology.Triangles ? src.GetTriangles(s) : Array.Empty<int>());
            foreach (var st in subs) for (int i = 0; i + 2 < st.Length; i += 3) { parent[Find(weld[st[i + 1]])] = Find(weld[st[i]]); parent[Find(weld[st[i + 2]])] = Find(weld[st[i]]); }
            var compTris = new Dictionary<int, List<(int s, int i)>>();
            for (int s = 0; s < subs.Count; s++) for (int i = 0; i + 2 < subs[s].Length; i += 3) { int c = Find(weld[subs[s][i]]); if (!compTris.TryGetValue(c, out var l)) compTris[c] = l = new List<(int, int)>(); l.Add((s, i)); }
            var compVerts = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++) { int c = Find(weld[i]); if (!compTris.ContainsKey(c)) continue; if (!compVerts.TryGetValue(c, out var l)) compVerts[c] = l = new List<int>(); l.Add(i); }
            // slabs: 8..smallTris triangles, thin, long one way
            var slabs = new List<(int comp, Vector3 centre, Vector3 ax, float half, float hw, float yBot, float yTop)>();
            var body = new List<STri308>();
            foreach (var kv in compTris)
            {
                bool slab = false;
                if (kv.Value.Count >= 8 && kv.Value.Count <= row.smallTris)
                {
                    var vs = compVerts[kv.Key]; float yb = vs.Min(i => w[i].y), yt = vs.Max(i => w[i].y);
                    Vector3 longest = Vector3.zero;
                    foreach (var (s, i) in kv.Value) for (int e = 0; e < 3; e++) { var d = w[subs[s][i + (e + 1) % 3]] - w[subs[s][i + e]]; d.y = 0; if (d.sqrMagnitude > longest.sqrMagnitude) longest = d; }
                    // the longest edge of a box side is its length; a diagonal of the top face is longer — take the dominant direction of edges instead
                    var dirs = new List<Vector3>(); foreach (var (s, i) in kv.Value) for (int e = 0; e < 3; e++) { var d = w[subs[s][i + (e + 1) % 3]] - w[subs[s][i + e]]; d.y = 0; if (d.magnitude > .2f) dirs.Add(d); }
                    Vector3 ax = longest.normalized; float bestLen = 0;
                    foreach (var d in dirs) { var u = d.normalized; var p2 = new Vector3(-u.z, 0, u.x); float lu = vs.Max(i => Vector3.Dot(w[i], u)) - vs.Min(i => Vector3.Dot(w[i], u)), lp = vs.Max(i => Vector3.Dot(w[i], p2)) - vs.Min(i => Vector3.Dot(w[i], p2)); if (lu * lp > 0 && (bestLen == 0 || lu * lp < bestLen)) { bestLen = lu * lp; ax = lu >= lp ? u : p2; } }
                    var perp = new Vector3(-ax.z, 0, ax.x);
                    float a0 = vs.Min(i => Vector3.Dot(w[i], ax)), a1 = vs.Max(i => Vector3.Dot(w[i], ax)), p0 = vs.Min(i => Vector3.Dot(w[i], perp)), p1 = vs.Max(i => Vector3.Dot(w[i], perp));
                    if (yt - yb <= .35f && a1 - a0 >= 1f && a1 - a0 <= 6f && p1 - p0 <= row.slabMaxWidth)
                    {
                        slab = true;
                        var centre = ax * ((a0 + a1) * .5f) + perp * ((p0 + p1) * .5f) + Vector3.up * ((yb + yt) * .5f);
                        slabs.Add((kv.Key, centre, ax, (a1 - a0) * .5f, (p1 - p0) * .5f, yb, yt));
                    }
                }
                if (slab) continue;
                foreach (var (s, i) in kv.Value)
                {
                    var t3 = new STri308 { a = w[subs[s][i]], b = w[subs[s][i + 1]], c = w[subs[s][i + 2]] }; var nn = Vector3.Cross(t3.b - t3.a, t3.c - t3.a); if (nn.sqrMagnitude < 1e-12f) continue;
                    t3.n = nn.normalized; body.Add(t3);
                }
            }
            if (slabs.Count == 0) { stats = "no slab components"; return null; }
            var walls = body.Where(x => Mathf.Abs(x.n.y) < .5f).ToList();
            var remove = new HashSet<int>(); int narrowed = 0, lowered = 0; float maxCut = 0, maxDrop = 0;
            foreach (var sl in slabs)
            {
                float Walk(Vector3 p) => SeatUnder(body, p.x, p.z, sl.yBot - 1.5f, sl.yTop + .3f, .5f);
                if (float.IsNaN(Walk(sl.centre))) { remove.Add(sl.comp); at = sl.centre; continue; }
                var lim = new float[2];
                for (int side = 0; side < 2; side++)
                {
                    var d = side == 0 ? sl.ax : -sl.ax; float l = sl.half;
                    foreach (float h in new[] { sl.yBot + .03f, sl.yTop + .05f })
                    {
                        var o = new Vector3(sl.centre.x, h, sl.centre.z);
                        foreach (var x in walls) if (BuildingAudit308.RayTri(o, d, x.a, x.b, x.c, out float tt) && tt < l + .3f) l = Mathf.Min(l, tt - row.clearance);
                    }
                    for (float s = .1f; s <= l + 1e-3f; s += .1f) if (float.IsNaN(Walk(sl.centre + d * s))) { l = Mathf.Min(l, s - .1f); break; }
                    lim[side] = Mathf.Max(0, l);
                }
                if (lim[0] + lim[1] < row.minSpan) { remove.Add(sl.comp); at = sl.centre; continue; }
                var perp = new Vector3(-sl.ax.z, 0, sl.ax.x); float gapMax = float.MinValue, proud = float.MaxValue;
                foreach (float u in new[] { -lim[1], 0f, lim[0] }) foreach (float v in new[] { -sl.hw, sl.hw })
                {
                    float yw = Walk(sl.centre + sl.ax * u + perp * v); if (float.IsNaN(yw)) continue;
                    gapMax = Mathf.Max(gapMax, sl.yBot - yw); proud = Mathf.Min(proud, sl.yTop - yw);
                }
                float dy = gapMax > row.seatGap && proud < float.MaxValue ? -Mathf.Min(gapMax + .01f, Mathf.Max(0, proud - row.minProud)) : 0;
                bool cut = sl.half - lim[0] > 1e-3f || sl.half - lim[1] > 1e-3f;
                if (!cut && dy == 0) continue;
                if (cut) { narrowed++; maxCut = Mathf.Max(maxCut, Mathf.Max(sl.half - lim[0], sl.half - lim[1])); at = sl.centre; }
                if (dy != 0) { lowered++; maxDrop = Mathf.Max(maxDrop, -dy); at = sl.centre; }
                foreach (int i in compVerts[sl.comp])
                {
                    float s = Vector3.Dot(w[i] - sl.centre, sl.ax); float s2 = s > 0 ? s * lim[0] / sl.half : s * lim[1] / sl.half;
                    w[i] += sl.ax * (s2 - s) + Vector3.up * dy;
                }
            }
            stats = "slabs " + slabs.Count + ": narrowed " + narrowed + " (max " + maxCut.ToString("F2", Inv) + " m off one end), lowered " + lowered + " (max " + maxDrop.ToString("F2", Inv) + " m), removed " + remove.Count;
            if (narrowed == 0 && lowered == 0 && remove.Count == 0) return null;
            var inv = tr.worldToLocalMatrix; var copy = Object.Instantiate(src); copy.vertices = w.Select(p => inv.MultiplyPoint3x4(p)).ToArray();
            if (remove.Count > 0)
                for (int s = 0; s < subs.Count; s++)
                {
                    if (subs[s].Length == 0) continue;
                    var keep = new List<int>(subs[s].Length);
                    for (int i = 0; i + 2 < subs[s].Length; i += 3) if (!remove.Contains(Find(weld[subs[s][i]]))) { keep.Add(subs[s][i]); keep.Add(subs[s][i + 1]); keep.Add(subs[s][i + 2]); }
                    copy.SetTriangles(keep, s);
                }
            copy.RecalculateBounds();
            return copy;
        }

        // ------------------------------------------------------------------ execute / undo one change

        static Mesh SeatMeshAsset(string path, List<Object> created)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            if (!seatBuilt.TryGetValue(path, out var b) || b.mesh == null) throw new InvalidOperationException("no built mesh for " + path);
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            b.mesh.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(b.mesh, path); created.Add(b.mesh);
            return b.mesh;
        }

        static void SeatExecute(Scene scene, SeatRow308 row, Change308 c, List<Object> created)
        {
            var t = BuildingAudit308.Resolve(scene, c.key) ?? throw new InvalidOperationException("missing " + c.key);
            switch (c.kind)
            {
                case "trs":
                    if (!SeatParseTrs(c.after, out var p, out var q, out var s)) throw new FormatException(c.after);
                    t.localPosition = p; t.localRotation = q; t.localScale = s; EditorUtility.SetDirty(t); Physics.SyncTransforms();
                    break;
                case "active": t.gameObject.SetActive(c.after == "1"); EditorUtility.SetDirty(t.gameObject); break;
                case "skirt":
                {
                    Physics.SyncTransforms();
                    string mesh = GroundFit299.StoneSkirt308(t, row.material, out string note);
                    if (mesh == null) { c.state = "none"; c.note = "no skirt: " + note; break; }
                    var asset = AssetDatabase.LoadAssetAtPath<Mesh>(mesh); if (asset != null) created.Add(asset);
                    c.after = mesh; c.note = note;
                    break;
                }
                case "child":
                {
                    string child = c.note.Split('|')[0];
                    var mesh = SeatMeshAsset(c.after, created);
                    seatBuilt.TryGetValue(c.after, out var b);
                    var go = new GameObject(child); go.transform.SetParent(t, false);
                    var like = t.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault();
                    if (like != null) { go.layer = like.gameObject.layer; GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(like.gameObject)); }
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = b != null && b.mats.Length > 0 ? b.mats : (like != null ? like.sharedMaterials.Take(mesh.subMeshCount).ToArray() : Array.Empty<Material>());
                    if (!row.noCollider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                    var lg = t.GetComponent<LODGroup>();
                    if (lg != null) { var lods = lg.GetLODs(); for (int i = 0; i < lods.Length; i++) lods[i].renderers = (lods[i].renderers ?? Array.Empty<Renderer>()).Where(r => r != null).Concat(new Renderer[] { mr }).ToArray(); lg.SetLODs(lods); EditorUtility.SetDirty(lg); }
                    EditorUtility.SetDirty(go);
                    break;
                }
                case "mesh": { var f = t.GetComponent<MeshFilter>(); if (f == null) throw new InvalidOperationException("no MeshFilter on " + c.key); f.sharedMesh = SeatMeshAsset(c.after.Split('#')[0], created); EditorUtility.SetDirty(f); break; }
                case "colmesh": { var mc = t.GetComponent<MeshCollider>(); if (mc == null) throw new InvalidOperationException("no MeshCollider on " + c.key); mc.sharedMesh = SeatMeshAsset(c.after.Split('#')[0], created); EditorUtility.SetDirty(mc); break; }
                default: throw new InvalidOperationException("unknown change kind " + c.kind);
            }
        }

        static void SeatUndo(Scene scene, Change308 c)
        {
            var t = BuildingAudit308.Resolve(scene, c.key) ?? throw new InvalidOperationException("missing " + c.key);
            switch (c.kind)
            {
                case "trs": if (!SeatParseTrs(c.before, out var p, out var q, out var s)) throw new FormatException(c.before); t.localPosition = p; t.localRotation = q; t.localScale = s; EditorUtility.SetDirty(t); break;
                case "active": t.gameObject.SetActive(c.before == "1"); EditorUtility.SetDirty(t.gameObject); break;
                case "skirt": { var holder = t.parent != null ? t.parent.Find("StoneSkirt308") : null; if (holder != null) Object.DestroyImmediate(holder.gameObject); EditorUtility.SetDirty(t.gameObject); break; }
                case "child":
                {
                    var child = t.Find(c.note.Split('|')[0]);
                    if (child != null)
                    {
                        var mr = child.GetComponent<Renderer>(); var lg = t.GetComponent<LODGroup>();
                        if (lg != null) { var lods = lg.GetLODs(); for (int i = 0; i < lods.Length; i++) lods[i].renderers = (lods[i].renderers ?? Array.Empty<Renderer>()).Where(r => r != null && r != mr).ToArray(); lg.SetLODs(lods); EditorUtility.SetDirty(lg); }
                        Object.DestroyImmediate(child.gameObject);
                    }
                    EditorUtility.SetDirty(t.gameObject); break;
                }
                case "mesh":
                {
                    var f = t.GetComponent<MeshFilter>(); var back = SeatLoadMesh(c.before);
                    if (f == null || (back == null && !string.IsNullOrEmpty(c.before))) throw new InvalidOperationException("the original mesh " + c.before + " of " + c.key + " is gone");
                    f.sharedMesh = back; EditorUtility.SetDirty(f); break;
                }
                case "colmesh":
                {
                    var mc = t.GetComponent<MeshCollider>(); var back = SeatLoadMesh(c.before);
                    if (mc == null || (back == null && !string.IsNullOrEmpty(c.before))) throw new InvalidOperationException("the original collider mesh " + c.before + " of " + c.key + " is gone");
                    mc.sharedMesh = back; EditorUtility.SetDirty(mc); break;
                }
            }
        }

        // ------------------------------------------------------------------ apply / apply-all / verify / revert

        static string SeatApply(string alias, string[] ids, string commandText, HashSet<string> exclude)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var seat = SeatLoad(out err); if (seat == null) return "refused: " + err;
            var rows = SeatRows(seat, ids, false, out err); if (rows == null) return "refused: " + err;
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            var op = new Op308 { utc = BuildingAudit308.Utc(), command = "seat " + commandText, scene = scene.path, alias = alias, steps = rows.Select(r => "S:" + r.id).ToArray(), quality = BuildingAudit308.QualityName() };
            try
            {
                var ledger = SeatLoadLedger(alias); ledger.scene = scene.path;
                op.attractionBefore = AttractionHash(scene, cfg.attractionRoot); op.shaBefore = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path));
                var created = new List<Object>(); int applied = 0;
                foreach (var row in rows)
                {
                    if (exclude != null && exclude.Contains(row.id)) { op.stepResults.Add(row.id + " skipped: blocked in another scene"); continue; }
                    Physics.SyncTransforms();
                    var rp = SeatPlanRow(cfg, seat, scene, ledger, row);
                    var todo = rp.changes.Where(c => c.state == "apply").ToList();
                    if (rp.status == "blocked" || rp.status == "mismatch" || todo.Count == 0) { op.stepResults.Add(row.id + " " + rp.status + ": " + rp.detail); op.changes.AddRange(rp.changes.Where(c => c.state == "already")); continue; }
                    if (string.IsNullOrEmpty(op.backup))
                    {
                        string backupDir = Path.Combine(BuildingAudit308.Folder, "SceneBackup"); Directory.CreateDirectory(backupDir);
                        op.backup = Path.Combine(backupDir, alias + "-" + op.utc + "-seat.unity"); File.Copy(BuildingAudit308.Abs(scene.path), op.backup, false);
                    }
                    foreach (var c in todo) SeatExecute(scene, row, c, created);
                    op.changes.AddRange(rp.changes.Where(c => c.state == "apply" || c.state == "already"));
                    int did = rp.changes.Count(c => c.state == "apply"); applied += did;
                    op.stepResults.Add(row.id + " applied: " + did + " change(s) — " + rp.detail + (row.userCheck ? " (user check)" : ""));
                }
                SeatDropBuilt();
                if (applied == 0)
                {
                    op.status = "already"; op.detail = "no change (idempotent); scene not saved"; op.shaAfter = op.shaBefore; op.attractionAfter = op.attractionBefore;
                    ledger.ops.Add(op); SeatSaveLedger(ledger);
                    GoBack(previous, scene, opened, true);
                    return "already: " + alias + " — no change, scene SHA " + op.shaBefore + "\n" + string.Join("\n", op.stepResults);
                }
                foreach (var o in created) if (o != null && AssetDatabase.Contains(o)) AssetDatabase.SaveAssetIfDirty(o);
                ClearStaleSuffix(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) { op.status = "FAILED"; op.detail = "SaveScene returned false; the scene stays open and dirty; backup " + op.backup; }
                else
                {
                    op.shaAfter = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path)); op.attractionAfter = AttractionHash(scene, cfg.attractionRoot);
                    op.status = op.attractionAfter == op.attractionBefore ? "applied" : "FAILED";
                    op.detail = applied + " change(s) saved" + (op.attractionAfter == op.attractionBefore ? "" : "; Finish297_Attraction block CHANGED — Seat revert:" + alias);
                }
                ledger.ops.Add(op); SeatSaveLedger(ledger);
                GoBack(previous, scene, opened);
                return op.status + ": " + alias + " sha " + op.shaBefore + " -> " + op.shaAfter + ", backup " + op.backup + "\n" + string.Join("\n", op.stepResults) + "\n" + op.detail;
            }
            catch (Exception e)
            {
                SeatDropBuilt();
                op.status = "FAILED"; op.detail = e.ToString(); string discarded = "";
                try
                {
                    if (scene.IsValid() && scene.isDirty && string.IsNullOrEmpty(op.shaAfter))
                    {
                        string path = scene.path; EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                        discarded = "; unsaved edits discarded (scene reloaded from disk, SHA " + BuildingAudit308.Sha(BuildingAudit308.Abs(path)) + ")";
                        if (opened && !string.IsNullOrEmpty(previous) && previous != path && File.Exists(BuildingAudit308.Abs(previous))) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                    }
                }
                catch (Exception e2) { discarded = "; reload failed, the scene is still open and dirty: " + e2.Message; }
                op.detail += discarded; op.changes.Clear();
                var ledger = SeatLoadLedger(alias); ledger.ops.Add(op); SeatSaveLedger(ledger);
                return "FAILED: " + alias + " — " + e.Message + discarded + (string.IsNullOrEmpty(op.backup) ? "" : " (backup " + op.backup + ")");
            }
        }

        static string SeatApplyAll(string[] ids)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var seat = SeatLoad(out err); if (seat == null) return "refused: " + err;
            var rows = SeatRows(seat, ids, false, out err); if (rows == null) return "refused: " + err;
            var order = new[] { "architecture296", "folklore298", "main" };
            var lines = new List<string>(); var exclude = new HashSet<string>();
            foreach (var alias in order)
            {
                string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
                if (refuse != null) return refuse + (lines.Count > 0 ? "\n" + string.Join("\n", lines) : "");
                try
                {
                    var plan = SeatMakePlan(cfg, seat, alias, scene, rows);
                    foreach (var r in plan.rows.Where(r => r.status == "blocked" || r.status == "mismatch")) { exclude.Add(r.id); lines.Add("left out everywhere: " + r.id + " (" + alias + " " + r.status + ": " + r.detail + ")"); }
                    lines.Add(alias + ": " + plan.rows.Count(r => r.status == "ready") + " ready, " + plan.rows.Count(r => r.status == "already") + " already, " + plan.rows.Count(r => r.status == "clean") + " clean, " + plan.rows.Count(r => r.status == "absent") + " absent");
                }
                finally { SeatDropBuilt(); GoBack(previous, scene, opened, true); }
            }
            foreach (var alias in order)
            {
                string r = SeatApply(alias, ids, "apply-all", exclude);
                lines.Add(r);
                if (r.StartsWith("refused", StringComparison.Ordinal) || r.StartsWith("FAILED", StringComparison.Ordinal)) { lines.Add("stopped at " + alias); break; }
            }
            return string.Join("\n", lines);
        }

        [Serializable] sealed class SeatVerify308 { public string alias = "", scene = "", utc = "", sceneSha = ""; public List<string> rows = new List<string>(); public int fail; }

        static string SeatVerify(string alias)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var seat = SeatLoad(out err); if (seat == null) return "refused: " + err;
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            var v = new SeatVerify308 { alias = alias, scene = scene.path, utc = BuildingAudit308.Utc(), sceneSha = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path)) };
            try
            {
                var plan = SeatMakePlan(cfg, seat, alias, scene, seat.rows.ToList());
                foreach (var r in plan.rows)
                {
                    bool na = r.status == "proposal" || r.status == "absent" || !r.enabled && r.status != "already";
                    bool ok = r.status == "already" || r.status == "clean";
                    bool nullMesh = false;
                    foreach (var c in r.changes.Where(c => c.state == "already" && c.kind == "child"))
                    {
                        var host = BuildingAudit308.Resolve(scene, c.key); var child = host != null ? host.Find(c.note.Split('|')[0]) : null; var mf = child != null ? child.GetComponent<MeshFilter>() : null;
                        if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0) nullMesh = true;
                    }
                    foreach (var c in r.changes.Where(c => c.state == "already" && (c.kind == "mesh" || c.kind == "colmesh"))) if (SeatLoadMesh(c.after) == null) nullMesh = true;
                    string level = na ? "INFO " : ok && !nullMesh ? "PASS " : "FAIL "; if (level == "FAIL ") v.fail++;
                    v.rows.Add(level + r.id + " [" + r.op + "] " + r.status + (nullMesh ? " (generated mesh missing)" : "") + ": " + r.detail + (r.userCheck ? " — user check: " + r.expect : ""));
                }
            }
            finally { SeatDropBuilt(); GoBack(previous, scene, opened, true); }
            Directory.CreateDirectory(FixDir);
            string file = Path.Combine(FixDir, "seat-verify-" + alias + "-" + v.utc + ".json");
            File.WriteAllText(file, JsonUtility.ToJson(v, true), new UTF8Encoding(false));
            return "seat verify " + alias + ": " + (v.fail == 0 ? "PASS" : v.fail + " FAIL") + " -> " + file + "\n" + string.Join("\n", v.rows);
        }

        static string SeatRevert(string alias, string id)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var ledger = SeatLoadLedger(alias);
            string step = id != null ? "S:" + id : null;
            var ops = ledger.ops.Where(o => !o.reverted && (o.status == "applied" || (o.status == "FAILED" && !string.IsNullOrEmpty(o.shaAfter))) && o.changes.Any(c => c.state == "apply" && (step == null || c.step == step))).ToList();
            if (ops.Count == 0) return "refused: nothing to revert in " + SeatLedgerFile(alias) + (id != null ? " for " + id : "");
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            string sha = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path));
            var rev = new Op308 { utc = BuildingAudit308.Utc(), command = "seat revert:" + alias + (id != null ? ":" + id : ""), scene = scene.path, alias = alias, shaBefore = sha, steps = step != null ? new[] { step } : ops.SelectMany(o => o.steps).Distinct().ToArray() };
            string backupDir = Path.Combine(BuildingAudit308.Folder, "SceneBackup"); Directory.CreateDirectory(backupDir);
            rev.backup = Path.Combine(backupDir, alias + "-" + rev.utc + "-seat-prerevert.unity"); File.Copy(BuildingAudit308.Abs(scene.path), rev.backup, false);
            int n = 0;
            try
            {
                foreach (var op in Enumerable.Reverse(ops))
                    foreach (var c in Enumerable.Reverse(op.changes).Where(c => c.state == "apply" && (step == null || c.step == step)).ToList()) { SeatUndo(scene, c); n++; }
            }
            catch (Exception e)
            {
                string path = scene.path; EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                if (opened && !string.IsNullOrEmpty(previous) && previous != path && File.Exists(BuildingAudit308.Abs(previous))) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                return "FAILED: seat revert " + alias + " after " + n + " change(s) — " + e.Message + "; nothing saved (scene reloaded from disk, pre-revert copy " + rev.backup + ")";
            }
            foreach (var op in ops)
            {
                foreach (var c in op.changes.Where(c => c.state == "apply" && (step == null || c.step == step))) c.state = "reverted";
                if (op.changes.All(c => c.state != "apply")) { op.reverted = true; op.revertedUtc = rev.utc; }
            }
            ClearStaleSuffix(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            rev.shaAfter = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path));
            rev.status = saved ? "reverted" : "FAILED"; rev.detail = n + " change(s) set back to their ledger values" + (saved ? "" : "; SaveScene returned false");
            ledger.ops.Add(rev); SeatSaveLedger(ledger);
            GoBack(previous, scene, opened);
            return rev.status + ": " + alias + " sha " + sha + " -> " + rev.shaAfter + " — " + rev.detail + " (pre-revert backup " + rev.backup + ")";
        }
    }
}
