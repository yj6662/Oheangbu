using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-16 look fixes L2 / L3 / L4 (Tools/Unity/Stage308_relayout_fix2/props): data-only re-pose of objects that already stand in
    // the scene. Data = Art/World/Compact/Rebuild/CliffBoundary308/propfix308.json (without it every command refuses).
    //   Fix2 "status"
    //   Fix2 "plan:<alias>"                 read only: every op's state (apply | already | mismatch | absent) and its numbers
    //   Fix2 "apply:<alias>[:<group>,..]"   enabled groups (or the named ones). Ledger first, one scene save, no SaveAssets
    //   Fix2 "verify:<alias>"               post-conditions of every enabled group on the scene as it stands
    //   Fix2 "revert:<alias>[:force]"       the newest live op of this tool's ledger, changes undone in reverse order
    //   Fix2 "veg-plan" | "veg-apply"       L6: the rows of propfix308.json veg that are switched on (none by default) through
    //                                       BuildingFix308.VegRows (the Veg ledger)
    // An op (propfix308.json ops[]) = one object by its exact key:
    //   euler / scale   local rotation (Quaternion.Euler) / localScale; local_xz = local x, z; xz "bounds" = the renderer-bounds centre keeps
    //                   its world XZ through the turn
    //   seat            then shifted along world Y so that (renderer bounds bottom - physical ground under the bounds centre) = seat - the
    //                   D8 rule of ContentSeat308.Props and the Seat rule of Roadside303.Build. The set's own colliders never answer the ray
    //   material        every slot of the object's own renderer -> an EXISTING material asset
    //   check           top_max_m / sunk_frac / axis + tilt_min_deg | tilt_max_deg / road / trunk / apart
    //   world           (#308 fix 3, D308-22 bake) {xz: [x, z], euler: [x, y, z]} = the object's WORLD place and WORLD rotation, set after
    //                   euler / local_xz: a part of a set that is itself turned (the wreck's roof, a wheel) is laid where the data says,
    //                   whatever the parent's roll. Guarded: the group must say world_ops true and name a skip_root above the object, and
    //                   the posed object's bounds stay within checks.navmesh.disc_m of that root - else BLOCKED
    //   seat_once       the seat shift is applied when the op is posed (children still attached); verify then judges the ledger pose
    //                   and MEASURES the piece as it stands: the bounds of its own renderers - without the children that a world op of
    //                   this data carried away - against the ground under their centre (num <id>.own_gap; check.own_gap_max_m = it does
    //                   not hang). The seat number itself is not re-read: those children changed the bounds it was taken from
    // Nothing is created by ops (no object, collider, Light, text). clones[] (default off) copy a colliderless scene object under the
    // tool's own root. A prefab-instance child keeps its change as an instance override. Colliders that sit on a re-posed object turn
    // with it: the NavMesh carve of the last bake is then stale there (data checks.navmesh; reported, never baked here).
    // Guards (review F3 / F4): an op key must start with one of data key_roots[]; a pose op whose object (children included) carries a
    // Collider is BLOCKED unless its group says moves_colliders true AND carries a non-empty bake_decision (who decided, which bake
    // takes it); a scale op on such an object is always BLOCKED (a collider is never resized). The ledger row is written ("applying",
    // with every change) BEFORE the scene is saved and becomes "applied" after it; revert also takes an "applying" row.
    public static partial class ContentSeat308
    {
        static string Fix2File => Path.Combine(Harness303.RepoRoot, "Art", "World", "Compact", "Rebuild", "CliffBoundary308", "propfix308.json");
        const string Fix2Step = "F2X";
        static string Fix2LedgerFile(JObject d, string scenePath) => Path.Combine(Harness303.RepoRoot, Str(d, "ledger_dir").Replace('/', Path.DirectorySeparatorChar), "ledger_fix2_" + SceneName(scenePath) + ".json");
        static Ledger Fix2Load(JObject d, string scenePath)
        {
            string f = Fix2LedgerFile(d, scenePath); var l = File.Exists(f) ? JsonUtility.FromJson<Ledger>(File.ReadAllText(f)) : null;
            return l ?? new Ledger { alias = PostLedger308.Short(scenePath), scene = scenePath };
        }
        static void Fix2Save(JObject d, Ledger l) { string f = Fix2LedgerFile(d, l.scene); Directory.CreateDirectory(Path.GetDirectoryName(f)); File.WriteAllText(f, JsonUtility.ToJson(l, true), new UTF8Encoding(false)); }

        public static string Fix2(string command)
        {
            command = (command ?? "").Trim();
            try
            {
                var a = command.Split(':');
                switch (a[0])
                {
                    case "status": return Fix2Status();
                    case "plan": return a.Length > 1 ? Fix2Pass(a[1], null, false, command) : "refused: plan:<alias>";
                    case "apply": return a.Length > 1 ? Fix2Pass(a[1], a.Length > 2 ? a[2].Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray() : null, true, command) : "refused: apply:<alias>[:<group>,..]";
                    case "verify": return a.Length > 1 ? Fix2Verify(a[1]) : "refused: verify:<alias>";
                    case "revert": return a.Length > 1 ? Fix2Revert(a[1], a.Length > 2 && a[2].Trim() == "force") : "refused: revert:<alias>[:force]";
                    case "veg-plan": return Fix2Veg(false);
                    case "veg-apply": return Fix2Veg(true);
                    default: return "refused: ContentSeat308 Fix2 status | plan:<alias> | apply:<alias>[:<group>,..] | verify:<alias> | revert:<alias>[:force] | veg-plan | veg-apply";
                }
            }
            catch (Refuse r) { return "refused: " + r.Message; }
            catch (Exception e) { return "FAILED: " + e; }
        }

        static JObject Fix2Data() => LoadJson(Fix2File, "prop fix data (copy Tools/Unity/Stage308_relayout_fix2/props/Data/propfix308.json there)");
        static Vector3 V3At(JToken a) => new Vector3(At(a, 0), At(a, 1), At(a, 2));
        static bool Fix2GroupOn(JObject d, string id, string[] only)
        {
            var g = Arr(d, "groups").FirstOrDefault(x => Opt(x, "id") == id); if (g == null) throw new Refuse("data: group '" + id + "' is not listed in groups[]");
            return only != null ? only.Contains(id) : Flag(g, "enabled");
        }
        static JToken Fix2Group(JObject d, string id) => Arr(d, "groups").First(x => Opt(x, "id") == id);

        static string Fix2Status()
        {
            var d = Fix2Data(); var sb = new StringBuilder("ContentSeat308 Fix2 status\n  data " + Fix2File + " sha " + Short(ShaOf(Fix2File)) + " (" + Opt(d, "version") + ")\n");
            foreach (var g in Arr(d, "groups")) sb.AppendLine("  group " + Str(g, "id") + ": " + (Flag(g, "enabled") ? "on" : "off") + (Flag(g, "moves_colliders") ? " [moves colliders: " + (string.IsNullOrWhiteSpace(Opt(g, "bake_decision")) ? "no bake_decision - blocked" : "bake_decision set") + "]" : "") + ", " + Arr(d, "ops").Count(o => Opt(o, "group") == Str(g, "id")) + " op(s), " + Arr(d, "clones").Count(o => Opt(o, "group") == Str(g, "id")) + " clone(s)");
            foreach (var s in PostLedger308.Scenes)
            {
                var l = Fix2Load(d, s); var live = l.ops.Where(o => !o.reverted && o.status == "applied").ToList();
                int half = l.ops.Count(o => !o.reverted && o.status == "applying");
                if (half > 0) sb.AppendLine("  WARNING " + PostLedger308.Short(s) + ": " + half + " op(s) left 'applying' (the editor stopped between the ledger and the scene save) - Fix2 revert:" + PostLedger308.Short(s) + " settles it");
                sb.AppendLine("  " + PostLedger308.Short(s) + ": " + live.Count + " live op(s)" + (live.Count > 0 ? ", newest " + live.Last().utc + " (" + live.Last().detail + ")" : "") + " | ledger " + Fix2LedgerFile(d, s));
            }
            var veg = d["veg"]; sb.AppendLine("  veg (L6): " + (Flag(veg, "enabled") ? "on" : "off") + ", " + Arr(veg, "rows").Count(r => Flag(r, "enabled")) + " row(s) on of " + Arr(veg, "rows").Count());
            return sb.ToString();
        }

        // ------------------------------------------------------------------ one object

        sealed class F2Undo { public Transform t; public Vector3 p, s; public Quaternion q; public Renderer r; public Material[] mats; public GameObject made; }

        static Change F2Prior(Ledger l, string key, string kind)
        {
            for (int i = l.ops.Count - 1; i >= 0; i--)
            {
                var op = l.ops[i]; if (op.reverted || op.status != "applied") continue;
                var c = op.changes.LastOrDefault(x => x.key == key && x.kind == kind && x.state == "apply"); if (c != null) return c;
            }
            return null;
        }

        static bool F2Bounds(Transform t, out Bounds b) => PieceBounds(t, out b);

        // #308 fix 3: D8 verify (ContentSeat308.Props.cs) leaves the seat row of a piece to Fix2 verify when this ledger holds a live
        // pose row for it. No data file or no ledger = false (D8 judges the piece as before).
        static bool Fix2Reposed(Ctx k, string key, out string what)
        {
            what = ""; if (!File.Exists(Fix2File)) return false;
            try { var c = F2Prior(Fix2Load(Fix2Data(), k.ScenePath), key, "pose"); if (c == null) return false; what = (c.note ?? "").Split('|')[0]; return true; }
            catch (Exception) { return false; }
        }

        // #308 fix 3 (review F6): the piece measured without the children another op of this data posed in WORLD terms (the roof and
        // the wheel that were carried away from the wreck's body): bounds bottom of what is left - ground under the bounds centre.
        static bool F2OwnGap(Ctx k, JObject d, JToken op, Transform t, out float gap, out int away)
        {
            gap = float.NaN; away = 0; string under = Str(op, "key") + "/"; var gone = new List<Transform>();
            foreach (var o2 in Arr(d, "ops"))
            {
                string k2 = Opt(o2, "key") ?? ""; if (o2["world"] == null || o2["world"].Type == JTokenType.Null || !k2.StartsWith(under, StringComparison.Ordinal)) continue;
                var c = BuildingAudit308.Resolve(k.Scene, k2); if (c != null && c != t && c.IsChildOf(t)) gone.Add(c);
            }
            away = gone.Count; bool any = false; Bounds b = default;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || gone.Any(g => r.transform.IsChildOf(g))) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any || !Ground(k, b.center.x, b.center.z, out var p, out _)) return false;
            gap = b.min.y - p.y; return true;
        }

        // seat gap of the object where it stands: bounds bottom - ground under the bounds centre (own = the colliders that never answer)
        static bool F2Gap(Ctx k, Transform t, out float gap, out Bounds b, out Vector3 g)
        {
            gap = float.NaN; g = default;
            if (!F2Bounds(t, out b)) return false;
            if (!Ground(k, b.center.x, b.center.z, out g, out _)) return false;
            gap = b.min.y - g.y; return true;
        }

        // the op's target pose, reached by posing the object (the caller restores it for a plan). Returns the fails of the op's checks.
        static List<string> F2Pose(Ctx k, JObject d, JToken op, Transform t, List<string> numbers)
        {
            var fails = new List<string>(); string id = Str(op, "id");
            bool hadBefore = F2Bounds(t, out var b0);
            if (op["euler"] != null) t.localRotation = Quaternion.Euler(V3At(op["euler"]));
            if (op["scale"] != null) t.localScale = V3At(op["scale"]);
            if (op["local_xz"] != null) { var lp = t.localPosition; t.localPosition = new Vector3(At(op["local_xz"], 0), lp.y, At(op["local_xz"], 1)); }
            var wo = op["world"];
            if (wo != null && wo.Type != JTokenType.Null)
            {
                if (wo["euler"] != null) t.rotation = Quaternion.Euler(V3At(wo["euler"]));
                if (wo["xz"] != null) t.position = new Vector3(At(wo["xz"], 0), t.position.y, At(wo["xz"], 1));
            }
            Physics.SyncTransforms();
            if (Opt(op, "xz") == "bounds")
            {
                if (!hadBefore || !F2Bounds(t, out var b1)) fails.Add(id + ": no renderer / collider bounds (xz 'bounds')");
                else { t.position += new Vector3(b0.center.x - b1.center.x, 0f, b0.center.z - b1.center.z); Physics.SyncTransforms(); }
            }
            if (op["seat"] != null && op["seat"].Type != JTokenType.Null)
            {
                float seat = op["seat"].Value<float>();
                if (!F2Gap(k, t, out float gap, out _, out _)) fails.Add(id + ": no bounds or no ground under the object (seat)");
                else { t.position += Vector3.up * (seat - gap); Physics.SyncTransforms(); numbers.Add("num " + id + ".seat_shift " + F(seat - gap, "F3")); }
            }
            F2Checks(k, d, op, t, (ok, text) => { if (!ok) fails.Add(text); }, numbers, true);
            return fails;
        }

        // post-conditions of one op on the object as it stands (plan after posing, verify)
        static void F2Checks(Ctx k, JObject d, JToken op, Transform t, Action<bool, string> row, List<string> numbers, bool posing = false)
        {
            string id = Str(op, "id"); var ck = op["check"]; var tol = Req(d, "tolerances");
            bool has = F2Bounds(t, out var b);
            if (op["seat"] != null && op["seat"].Type != JTokenType.Null && !posing && Flag(op, "seat_once"))
            {
                numbers?.Add("num " + id + ".seat_once 1");
                if (F2OwnGap(k, d, op, t, out float ownGap, out int away))
                {
                    numbers?.Add("num " + id + ".own_gap " + F(ownGap, "F3"));
                    if (ck != null && ck.Type != JTokenType.Null && ck["own_gap_max_m"] != null) row(ownGap <= Num(ck, "own_gap_max_m"), id + ": own bounds bottom - ground " + F(ownGap) + " m, " + away + " carried-away child(ren) left out (≤ " + F(Num(ck, "own_gap_max_m")) + ": the piece does not hang; its seat " + F(op["seat"].Value<float>()) + " was taken once, with them attached)");
                }
                else row(false, id + ": no own renderer bounds or no ground under the piece (seat_once)");
            }
            else if (op["seat"] != null && op["seat"].Type != JTokenType.Null)
            {
                float seat = op["seat"].Value<float>();
                if (F2Gap(k, t, out float gap, out _, out _)) { numbers?.Add("num " + id + ".seat_gap " + F(gap - seat, "F3")); row(Mathf.Abs(gap - seat) <= Num(tol, "seat_m"), id + ": bounds bottom - ground " + F(gap) + " (seat " + F(seat) + ", |gap| ≤ " + F(Num(tol, "seat_m")) + ")"); }
                else row(false, id + ": no bounds or no ground under the object");
            }
            if (ck == null || ck.Type == JTokenType.Null) return;
            if (ck["top_max_m"] != null || ck["sunk_frac"] != null)
            {
                if (!has) { row(false, id + ": no renderer to measure"); return; }
                float low = float.PositiveInfinity; bool all = true; float mid = float.NaN;
                foreach (var q in new[] { new Vector2(b.min.x, b.min.z), new Vector2(b.min.x, b.max.z), new Vector2(b.max.x, b.min.z), new Vector2(b.max.x, b.max.z), new Vector2(b.center.x, b.center.z) })
                { if (Ground(k, q.x, q.y, out var g, out _)) { low = Mathf.Min(low, g.y); mid = g.y; } else all = false; }
                if (ck["top_max_m"] != null)
                {
                    float top = b.max.y - low; numbers?.Add("num " + id + ".top " + F(top, "F3"));
                    row(all && top <= Num(ck, "top_max_m"), id + ": top " + (all ? F(top) : "-") + " m over the lowest ground under it (≤ " + F(Num(ck, "top_max_m")) + "; bounds " + F(b.size.x) + " × " + F(b.size.y) + " × " + F(b.size.z) + ")");
                }
                if (ck["sunk_frac"] != null)
                {
                    float sunk = (mid - b.min.y) / Mathf.Max(.001f, b.size.y); numbers?.Add("num " + id + ".sunk " + F(sunk, "F3"));
                    row(all && sunk >= At(ck["sunk_frac"], 0) && sunk <= At(ck["sunk_frac"], 1), id + ": " + F(sunk * 100f, "F0") + " % of its height in the ground (" + F(At(ck["sunk_frac"], 0) * 100f, "F0") + " – " + F(At(ck["sunk_frac"], 1) * 100f, "F0") + " %)");
                }
            }
            if (ck["axis"] != null)
            {
                float tilt = Vector3.Angle(t.rotation * V3At(ck["axis"]), Vector3.up); if (tilt > 90f) tilt = 180f - tilt;
                numbers?.Add("num " + id + ".tilt " + F(tilt, "F1"));
                if (ck["tilt_min_deg"] != null) row(tilt >= Num(ck, "tilt_min_deg"), id + ": axis " + V(V3At(ck["axis"])) + " " + F(tilt, "F1") + "° off the vertical (≥ " + F(Num(ck, "tilt_min_deg"), "F0") + ")");
                if (ck["tilt_max_deg"] != null) row(tilt <= Num(ck, "tilt_max_deg"), id + ": axis " + V(V3At(ck["axis"])) + " " + F(tilt, "F1") + "° off the vertical (≤ " + F(Num(ck, "tilt_max_deg"), "F0") + ")");
            }
            var wk = Req(Req(d, "checks"), "wreck");
            if (Flag(ck, "road"))
            {
                if (!PieceSolidBounds(t, out var pb)) row(false, id + ": no collider / renderer to measure against the road");
                else
                {
                    var corridors = BuildingAudit308.Corridors(k.Scene, k.Cfg); float runGap = Num(k.Data, "corridor_run_gap_m"); float near = float.PositiveInfinity; string nearId = "";
                    foreach (var q in new[] { new Vector2(pb.min.x, pb.min.z), new Vector2(pb.min.x, pb.max.z), new Vector2(pb.max.x, pb.min.z), new Vector2(pb.max.x, pb.max.z), new Vector2(pb.center.x, pb.center.z) })
                    { float dist = CentreLine(q, corridors, runGap, out string rid); if (dist < near) { near = dist; nearId = rid; } }
                    numbers?.Add("num " + id + ".road " + F(near, "F3"));
                    row(near >= Num(wk, "piece_road_clear_m"), id + ": collider bounds " + F(near) + " m from the nearest route centre line (" + nearId + ", need ≥ " + F(Num(wk, "piece_road_clear_m")) + ")");
                }
            }
            if (Flag(ck, "trunk") && has)
            {
                float trunk = float.PositiveInfinity; string trunkId = "-";
                foreach (var (p, tid) in Trunks(k, b.center, 20f)) { float dist = Harness303.Flat(p, b.center); if (dist < trunk) { trunk = dist; trunkId = tid; } }
                row(trunk >= Num(wk, "trunk_clear_m"), id + ": nearest trunk " + F(trunk, "F1") + " m (" + trunkId + ", need ≥ " + F(Num(wk, "trunk_clear_m"), "F1") + ")");
            }
            foreach (var ap in Arr(ck, "apart"))
            {
                var other = BuildingAudit308.Resolve(k.Scene, Str(ap, "key"));
                if (other == null || !has || !F2Bounds(other, out var ob)) { row(false, id + ": cannot measure against " + Str(ap, "key")); continue; }
                float gap = Mathf.Max(Mathf.Max(ob.min.x - b.max.x, b.min.x - ob.max.x), Mathf.Max(Mathf.Max(ob.min.y - b.max.y, b.min.y - ob.max.y), Mathf.Max(ob.min.z - b.max.z, b.min.z - ob.max.z)));
                row(gap >= Num(ap, "min_m"), id + ": " + F(gap) + " m clear of " + PieceName(Str(ap, "key")) + " (bounds, need ≥ " + F(Num(ap, "min_m")) + ")");
            }
        }

        static bool F2Near(JToken tol, Transform t, Vector3 p, Quaternion q, Vector3 s)
            => Vector3.Distance(t.localPosition, p) <= Num(tol, "pose_m") && Quaternion.Angle(t.localRotation, q) <= Num(tol, "angle_deg") && (t.localScale - s).magnitude <= Num(tol, "scale");

        static string F2Mats(Material[] m) => string.Join("|", m.Select(x => x == null ? "" : AssetDatabase.GetAssetPath(x)));

        // ------------------------------------------------------------------ plan / apply

        static string Fix2Pass(string alias, string[] only, bool write, string commandText)
        {
            var d = Fix2Data(); var tol = Req(d, "tolerances");
            var k = Begin(alias, write, out string previous, out bool opened);
            var ledger = Fix2Load(d, k.ScenePath); var undo = new List<F2Undo>(); var changes = new List<Change>(); var lines = new List<string>(); var blockers = new List<string>();
            var op = new Op { utc = BuildingAudit308.Utc(), command = commandText, scene = k.ScenePath, alias = k.Alias, dataSha = ShaOf(Fix2File), sceneDataSha = k.SceneDataSha, steps = new[] { Fix2Step } };
            bool saved = false;
            try
            {
                foreach (var g in Arr(d, "groups"))
                {
                    string gid = Str(g, "id"); if (!Fix2GroupOn(d, gid, only)) { lines.Add("group " + gid + ": off"); continue; }
                    var gops = Arr(d, "ops").Where(o => Opt(o, "group") == gid).ToList(); var gcl = Arr(d, "clones").Where(o => Opt(o, "group") == gid).ToList();
                    var skipRoot = Opt(g, "skip_root") != null ? Strict(k, Opt(g, "skip_root"), false) : null;
                    var skipped = skipRoot != null ? SkipOwn(k, skipRoot) : new List<Collider>();
                    try
                    {
                        foreach (var o in gops)
                        {
                            string id = Str(o, "id"), key = Str(o, "key"); var t = Strict(k, key, false);
                            if (t == null) { blockers.Add(id + ": " + key + " is not in this scene" + (Opt(g, "needs") != null ? " (" + Opt(g, "needs") + ")" : "")); continue; }
                            if (ProtectedObject(k, t)) { blockers.Add(id + ": " + key + " stands under a protected tree"); continue; }
                            if (!Arr(d, "key_roots").Any(r => key.StartsWith(r.Value<string>(), StringComparison.Ordinal))) { blockers.Add(id + ": " + key + " is outside key_roots[] of the data (this tool poses only the listed sets)"); continue; }
                            if (Flag(o, "keep_rotation") && (o["euler"] != null || o["scale"] != null)) { blockers.Add(id + ": keep_rotation op carries an euler / scale"); continue; }
                            if (o["material"] == null)
                            {
                                int cols = t.GetComponentsInChildren<Collider>(true).Length;
                                if (cols > 0 && o["scale"] != null) { blockers.Add(id + ": " + key + " carries " + cols + " collider(s) - a scale op would resize them (never allowed)"); continue; }
                                if (cols > 0 && !(Flag(g, "moves_colliders") && !string.IsNullOrWhiteSpace(Opt(g, "bake_decision"))))
                                { blockers.Add(id + ": " + key + " carries " + cols + " collider(s) - posing it moves colliders on walkable ground (NavMesh of the last bake). Group " + gid + " needs moves_colliders true and a bake_decision"); continue; }
                            }
                            bool worldOp = o["world"] != null && o["world"].Type != JTokenType.Null;
                            if (worldOp && !(Flag(g, "world_ops") && skipRoot != null && t != skipRoot && t.IsChildOf(skipRoot)))
                            { blockers.Add(id + ": a world op needs its group to say world_ops true and a skip_root above the object (group " + gid + ")"); continue; }
                            var own = skipRoot == null ? SkipOwn(k, t) : new List<Collider>();
                            try
                            {
                                if (o["material"] != null) { Fix2Material(k, o, t, key, changes, undo, lines, blockers); continue; }
                                var eb = Req(o, "expect_before"); var numbers = new List<string>();
                                var p0 = t.localPosition; var q0 = t.localRotation; var s0 = t.localScale;
                                var qT = o["euler"] != null ? Quaternion.Euler(V3At(o["euler"])) : q0; var sT = o["scale"] != null ? V3At(o["scale"]) : s0;
                                var prior = F2Prior(ledger, key, "pose");
                                bool rotOk = Quaternion.Angle(q0, qT) <= Num(tol, "angle_deg") && (s0 - sT).magnitude <= Num(tol, "scale");
                                bool xzOk = o["local_xz"] == null || new Vector2(p0.x - At(o["local_xz"], 0), p0.z - At(o["local_xz"], 1)).magnitude <= Num(tol, "pose_m");
                                if (worldOp && o["world"]["euler"] != null) rotOk = Quaternion.Angle(t.rotation, Quaternion.Euler(V3At(o["world"]["euler"]))) <= Num(tol, "angle_deg") && (s0 - sT).magnitude <= Num(tol, "scale");
                                if (worldOp && o["world"]["xz"] != null) xzOk = xzOk && new Vector2(t.position.x - At(o["world"]["xz"], 0), t.position.z - At(o["world"]["xz"], 1)).magnitude <= Num(tol, "pose_m");
                                bool seatOk = o["seat"] == null || o["seat"].Type == JTokenType.Null || (F2Gap(k, t, out float gapNow, out _, out _) && Mathf.Abs(gapNow - o["seat"].Value<float>()) <= Num(tol, "seat_m"));
                                bool atBefore = Vector3.Distance(p0, V3At(Req(eb, "pos"))) <= Num(tol, "before_m") && Quaternion.Angle(q0, Quaternion.Euler(V3At(Req(eb, "euler")))) <= Num(tol, "before_deg") &&
                                                (eb["scale"] == null || (s0 - V3At(eb["scale"])).magnitude <= Num(tol, "before_scale"));
                                if (prior != null)
                                {
                                    bool same = BuildingFix308.ParsePose(prior.after, out var pa, out var qa) && F2Near(tol, t, pa, qa, sT);
                                    if (same) { lines.Add("already " + id + " (ledger pose)"); continue; }
                                    blockers.Add(id + ": the object's local pose is " + PoseText(p0, q0) + ", not this ledger's after value (moved by another tool since) - revert:" + k.Alias + ":force, then apply again"); continue;
                                }
                                if (rotOk && xzOk && seatOk) { lines.Add("already " + id + " (stands on the target; no ledger row)"); continue; }
                                if (!atBefore) { blockers.Add(id + ": the object stands at " + PoseText(p0, q0) + " scale " + V(s0) + ": neither expect_before nor the target"); continue; }
                                undo.Add(new F2Undo { t = t, p = p0, q = q0, s = s0 });
                                var fails = F2Pose(k, d, o, t, numbers);
                                if (worldOp && PieceBounds(t, out var wb))
                                {
                                    float far = 0f; float disc = Num(Req(Req(d, "checks"), "navmesh"), "disc_m");
                                    foreach (var c in new[] { wb.min, wb.max, new Vector3(wb.min.x, wb.min.y, wb.max.z), new Vector3(wb.max.x, wb.min.y, wb.min.z) }) far = Mathf.Max(far, Harness303.Flat(c, skipRoot.position));
                                    numbers.Add("num " + id + ".disc " + F(far, "F2"));
                                    if (far > disc) fails.Add(id + ": bounds reach " + F(far) + " m from " + skipRoot.name + " (checks.navmesh.disc_m " + F(disc, "F1") + ")");
                                }
                                foreach (var f in fails) blockers.Add(f);
                                changes.Add(new Change { step = Fix2Step, key = key, kind = "pose", before = PoseText(p0, q0), after = PoseText(t.localPosition, t.localRotation), at = t.position, state = "apply", note = id + "|scale " + Vec(s0) + " -> " + Vec(t.localScale) + "|" + string.Join(" ", numbers) });
                                if (o["scale"] != null) changes.Add(new Change { step = Fix2Step, key = key, kind = "scale", before = Vec(s0), after = Vec(t.localScale), at = t.position, state = "apply", note = id });
                                lines.Add("apply " + id + ": " + PoseText(p0, q0) + " -> " + PoseText(t.localPosition, t.localRotation) + (o["scale"] != null ? " scale " + V(s0) + " -> " + V(t.localScale) : "") + " | " + string.Join(" ", numbers));
                            }
                            finally { Unskip(k, own); }
                        }
                        foreach (var c in gcl) Fix2Clone(k, d, c, changes, undo, lines, blockers);
                    }
                    finally { Unskip(k, skipped); }
                }
                string head = "ContentSeat308 Fix2 " + (write ? "apply " : "plan ") + k.Alias + " (data " + Short(op.dataSha) + ")\n" + string.Join("\n", lines.Select(x => "  " + x));
                if (!write || blockers.Count > 0 || changes.Count == 0)
                {
                    Fix2Restore(undo);
                    string tail = blockers.Count > 0 ? "\nBLOCKED (nothing is changed):\n" + string.Join("\n", blockers.Select(x => "  " + x)) : changes.Count == 0 ? "\nno change" : "\n" + changes.Count + " change(s) would be applied";
                    if (write && blockers.Count == 0)
                    {
                        op.status = "already"; op.detail = "no change (idempotent); scene not saved"; op.shaBefore = op.shaAfter = ShaOf(Harness303.Abs(k.ScenePath)); ledger.ops.Add(op); Fix2Save(d, ledger);
                    }
                    GoBack(previous, k.Scene, opened);
                    return (write ? (blockers.Count > 0 ? "refused: " : "already: ") : "") + head + tail;
                }
                op.shaBefore = ShaOf(Harness303.Abs(k.ScenePath)); op.attractionBefore = BuildingFix308.AttractionHash(k.Scene, k.Cfg.attractionRoot);
                string backupDir = Path.Combine(Path.GetDirectoryName(Fix2LedgerFile(d, k.ScenePath)), "Backups", "fix2-" + op.utc); Directory.CreateDirectory(backupDir);
                op.backup = Path.Combine(backupDir, Path.GetFileName(k.ScenePath)); File.Copy(Harness303.Abs(k.ScenePath), op.backup, false);
                op.changes = changes; op.status = "applying"; op.detail = changes.Count + " change(s) - ledger written before the scene save";
                ledger.ops.Add(op); Fix2Save(d, ledger);   // ledger first: a stop between here and the save leaves a row revert can settle
                foreach (var u in undo)
                {
                    if (u.t != null) { EditorUtility.SetDirty(u.t); if (PrefabUtility.IsPartOfPrefabInstance(u.t)) PrefabUtility.RecordPrefabInstancePropertyModifications(u.t); }
                    if (u.r != null) { EditorUtility.SetDirty(u.r); if (PrefabUtility.IsPartOfPrefabInstance(u.r)) PrefabUtility.RecordPrefabInstancePropertyModifications(u.r); }
                }
                EditorSceneManager.MarkSceneDirty(k.Scene);
                if (!EditorSceneManager.SaveScene(k.Scene)) throw new InvalidOperationException("SaveScene returned false");
                saved = true;
                op.shaAfter = ShaOf(Harness303.Abs(k.ScenePath)); op.attractionAfter = BuildingFix308.AttractionHash(k.Scene, k.Cfg.attractionRoot);
                op.status = "applied"; op.detail = changes.Count + " change(s) saved" + (op.attractionAfter == op.attractionBefore ? "" : "; WARNING Finish297_Attraction block hash changed");
                Fix2Save(d, ledger);
                GoBack(previous, k.Scene, opened);
                return "applied: " + head + "\n" + op.detail + ", sha " + Short(op.shaBefore) + " -> " + Short(op.shaAfter) + ", backup " + op.backup + "\nnext: Fix2 verify:" + k.Alias + " -> Content308 scene-check:" + k.Alias + " -> ContentSeat308 verify:" + k.Alias;
            }
            catch (Exception e)
            {
                string after = "";
                try
                {
                    if (saved) { op.changes = changes; op.status = "applied"; op.shaAfter = ShaOf(Harness303.Abs(k.ScenePath)); op.detail = "saved, then failed: " + e.Message; if (!ledger.ops.Contains(op)) ledger.ops.Add(op); Fix2Save(d, ledger); after = "; the scene was saved - the ledger records the op (revert undoes it)"; }
                    else { Fix2Restore(undo); if (ledger.ops.Remove(op)) Fix2Save(d, ledger); after = "; nothing saved"; }
                    GoBack(previous, k.Scene, opened);
                }
                catch (Exception e2) { after += "; clean-up failed: " + e2.Message; }
                return (e is Refuse ? "refused: " : "FAILED: ") + k.Alias + " - " + e.Message + after;
            }
        }

        static void Fix2Restore(List<F2Undo> undo)
        {
            for (int i = undo.Count - 1; i >= 0; i--)
            {
                var u = undo[i];
                if (u.made != null) { Object.DestroyImmediate(u.made); continue; }
                if (u.t != null) { u.t.localPosition = u.p; u.t.localRotation = u.q; u.t.localScale = u.s; }
                if (u.r != null) u.r.sharedMaterials = u.mats;
            }
            Physics.SyncTransforms();
        }

        static void Fix2Material(Ctx k, JToken o, Transform t, string key, List<Change> changes, List<F2Undo> undo, List<string> lines, List<string> blockers)
        {
            string id = Str(o, "id"); var m = Req(o, "material"); string toPath = Str(m, "to"), fromPath = Str(m, "from");
            if (ProtectedAsset(k, toPath)) { blockers.Add(id + ": target material under a protected path " + toPath); return; }
            var to = AssetDatabase.LoadAssetAtPath<Material>(toPath); var from = AssetDatabase.LoadAssetAtPath<Material>(fromPath); var r = t.GetComponent<Renderer>();
            if (to == null || r == null) { blockers.Add(id + ": " + (to == null ? "material missing " + toPath : "no renderer on " + key)); return; }
            if (to.IsKeywordEnabled("_EMISSION")) { blockers.Add(id + ": " + toPath + " has _EMISSION on (발광 상한)"); return; }
            var now = r.sharedMaterials;
            if (now.Length != (int)Num(m, "slots")) { blockers.Add(id + ": the renderer has " + now.Length + " slot(s), the data says " + (int)Num(m, "slots")); return; }
            if (now.All(x => x == to)) { lines.Add("already " + id + " (" + Path.GetFileName(toPath) + ")"); return; }
            if (!now.All(x => x == to || x == from)) { blockers.Add(id + ": slots carry " + F2Mats(now) + " - neither the from-material nor the target"); return; }
            undo.Add(new F2Undo { r = r, mats = now });
            r.sharedMaterials = now.Select(_ => to).ToArray();
            changes.Add(new Change { step = Fix2Step, key = key, kind = "material", before = F2Mats(now), after = F2Mats(r.sharedMaterials), at = t.position, state = "apply", note = id });
            lines.Add("apply " + id + ": " + now.Length + " slot(s) " + Path.GetFileName(fromPath) + " -> " + Path.GetFileName(toPath));
        }

        // an optional copy of a colliderless scene object (default off in data)
        static void Fix2Clone(Ctx k, JObject d, JToken c, List<Change> changes, List<F2Undo> undo, List<string> lines, List<string> blockers)
        {
            string id = Str(c, "id"), rootName = Str(d, "clone_root"); var rules = Req(d, "clone_rules");
            var src = Strict(k, Str(c, "source_key"), false); if (src == null) { blockers.Add(id + ": source " + Str(c, "source_key") + " is not in this scene"); return; }
            var want = AssetDatabase.LoadAssetAtPath<Material>(Str(rules, "source_material"));
            if (src.GetComponentsInChildren<Collider>(true).Length > 0 || src.GetComponentsInChildren<Light>(true).Length > 0 || want == null || src.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(x => x != want)))
            { blockers.Add(id + ": the source must carry only " + Str(rules, "source_material") + ", no collider and no Light"); return; }
            var root = k.Scene.GetRootGameObjects().FirstOrDefault(g => g.name == rootName); var have = root != null ? root.transform.Find(id) : null;
            if (have != null) { lines.Add("already " + id + " (" + rootName + "/" + id + " exists)"); return; }
            float x = At(Req(c, "at_xz"), 0), z = At(Req(c, "at_xz"), 1);
            float road = CentreLine(new Vector2(x, z), BuildingAudit308.Corridors(k.Scene, k.Cfg), Num(k.Data, "corridor_run_gap_m"), out string roadId);
            if (road < Num(rules, "route_clear_m")) { blockers.Add(id + ": " + F(road) + " m from the centre line of " + roadId + " (< " + F(Num(rules, "route_clear_m")) + ")"); return; }
            if (root == null) { root = new GameObject(rootName); SceneManager.MoveGameObjectToScene(root, k.Scene); undo.Add(new F2Undo { made = root }); }
            var go = Object.Instantiate(src.gameObject); SceneManager.MoveGameObjectToScene(go, k.Scene); go.transform.SetParent(root.transform, true); go.name = id; go.hideFlags = HideFlags.None; go.SetActive(true);
            go.transform.localScale = src.lossyScale; go.transform.SetPositionAndRotation(new Vector3(x, src.position.y, z), Quaternion.Euler(V3At(Req(c, "euler"))));
            undo.Add(new F2Undo { made = go }); Physics.SyncTransforms();
            if (!F2Gap(k, go.transform, out float gap, out _, out _)) { blockers.Add(id + ": no ground under (" + F(x) + ", " + F(z) + ")"); return; }
            go.transform.position += Vector3.up * (Num(c, "seat") - gap);
            changes.Add(new Change { step = Fix2Step, key = rootName + "/" + id, kind = "spawn", before = "", after = PoseText(go.transform.position, go.transform.rotation), at = go.transform.position, state = "apply", note = id + "|copy of " + Str(c, "source_key") });
            lines.Add("apply " + id + ": copy of " + Str(c, "source_key") + " at " + V(go.transform.position));
        }

        // ------------------------------------------------------------------ verify / revert

        static string Fix2Verify(string alias)
        {
            var d = Fix2Data(); var tol = Req(d, "tolerances");
            var k = Begin(alias, false, out string previous, out bool opened);
            try
            {
                var ledger = Fix2Load(d, k.ScenePath); int pass = 0, fail = 0; var sb = new StringBuilder(); var numbers = new List<string>();
                void Row(bool ok, string text) { if (ok) pass++; else fail++; sb.AppendLine((ok ? "PASS " : "FAIL ") + text); }
                foreach (var g in Arr(d, "groups"))
                {
                    string gid = Str(g, "id"); if (!Flag(g, "enabled")) { sb.AppendLine("INFO group " + gid + ": off in data"); continue; }
                    var skipRoot = Opt(g, "skip_root") != null ? Strict(k, Opt(g, "skip_root"), false) : null;
                    var skipped = skipRoot != null ? SkipOwn(k, skipRoot) : new List<Collider>();
                    try
                    {
                        foreach (var o in Arr(d, "ops").Where(x => Opt(x, "group") == gid))
                        {
                            string id = Str(o, "id"), key = Str(o, "key"); var t = Strict(k, key, false);
                            if (t == null) { Row(false, id + ": " + key + " is not in this scene"); continue; }
                            if (o["material"] != null)
                            {
                                var to = AssetDatabase.LoadAssetAtPath<Material>(Str(o["material"], "to")); var r = t.GetComponent<Renderer>();
                                Row(r != null && to != null && r.sharedMaterials.All(x => x == to), id + ": every slot = " + Str(o["material"], "to") + " (now " + (r != null ? F2Mats(r.sharedMaterials) : "-") + ")");
                                continue;
                            }
                            var own = skipRoot == null ? SkipOwn(k, t) : new List<Collider>();
                            try
                            {
                                var prior = F2Prior(ledger, key, "pose");
                                if (prior == null) sb.AppendLine("INFO " + id + ": no live ledger row - judged as it stands");
                                else Row(BuildingFix308.ParsePose(prior.after, out var pa, out var qa) && F2Near(tol, t, pa, qa, t.localScale), id + ": stands on the ledger pose " + prior.after + " (now " + PoseText(t.localPosition, t.localRotation) + ")");
                                var qT = o["euler"] != null ? Quaternion.Euler(V3At(o["euler"])) : t.localRotation; var sT = o["scale"] != null ? V3At(o["scale"]) : t.localScale;
                                bool worldRot = o["world"] != null && o["world"].Type != JTokenType.Null && o["world"]["euler"] != null;
                                float rotOff = worldRot ? Quaternion.Angle(t.rotation, Quaternion.Euler(V3At(o["world"]["euler"]))) : Quaternion.Angle(t.localRotation, qT);
                                Row(rotOff <= Num(tol, "angle_deg") && (t.localScale - sT).magnitude <= Num(tol, "scale"), id + ": " + (worldRot ? "world" : "local") + " rotation / scale = the data (" + F(rotOff, "F2") + "° off, scale " + V(t.localScale) + ")");
                                F2Checks(k, d, o, t, Row, numbers);
                                int lights = t.GetComponentsInChildren<Light>(true).Count(l => l.enabled && l.gameObject.activeInHierarchy);
                                Row(lights == 0, id + ": lights on under the object " + lights + " (this tool adds none)");
                            }
                            finally { Unskip(k, own); }
                        }
                    }
                    finally { Unskip(k, skipped); }
                }
                foreach (var n in numbers) sb.AppendLine(n);
                return "ContentSeat308 Fix2 verify " + k.Alias + ": " + (fail == 0 ? "GREEN" : "RED") + " (PASS " + pass + ", FAIL " + fail + ")\n" + sb;
            }
            finally { GoBack(previous, k.Scene, opened); }
        }

        static string Fix2Revert(string alias, bool force)
        {
            var d = Fix2Data(); var tol = Req(d, "tolerances");
            var k = Begin(alias, true, out string previous, out bool opened);
            try
            {
                var ledger = Fix2Load(d, k.ScenePath); var op = ledger.ops.LastOrDefault(o => !o.reverted && (o.status == "applied" || o.status == "applying"));
                if (op == null) { GoBack(previous, k.Scene, opened); return "already: " + k.Alias + " - no live Fix2 op in the ledger"; }
                if (op.status == "applying" && ShaOf(Harness303.Abs(k.ScenePath)) == op.shaBefore)
                {
                    // the editor stopped after the ledger write and before the scene save: the scene never changed
                    op.reverted = true; op.revertedUtc = BuildingAudit308.Utc(); op.detail += "; settled: the scene file still has the sha before the apply (never saved)"; Fix2Save(d, ledger);
                    GoBack(previous, k.Scene, opened); return "reverted: " + k.Alias + " op " + op.utc + " was left 'applying' and the scene was never saved (sha " + Short(op.shaBefore) + ") - ledger row closed, nothing to undo";
                }
                var todo = new List<Action>(); var notes = new List<string>();
                for (int i = op.changes.Count - 1; i >= 0; i--)
                {
                    var c = op.changes[i]; if (c.state != "apply") continue; var t = BuildingAudit308.Resolve(k.Scene, c.key);
                    if (t == null) { notes.Add("lost " + c.key + " (" + c.kind + "): the object is gone"); if (!force) throw new Refuse(c.key + " is gone - revert:" + alias + ":force skips it"); continue; }
                    if (c.kind == "pose")
                    {
                        if (!BuildingFix308.ParsePose(c.before, out var pb, out var qb) || !BuildingFix308.ParsePose(c.after, out var pa, out var qa)) throw new Refuse("unreadable pose in the ledger: " + c.key);
                        if (!force && (Vector3.Distance(t.localPosition, pa) > Num(tol, "pose_m") || Quaternion.Angle(t.localRotation, qa) > Num(tol, "angle_deg"))) throw new Refuse(c.key + " no longer stands on the ledger's after pose (moved since) - revert:" + alias + ":force");
                        todo.Add(() => { t.localPosition = pb; t.localRotation = qb; EditorUtility.SetDirty(t); if (PrefabUtility.IsPartOfPrefabInstance(t)) PrefabUtility.RecordPrefabInstancePropertyModifications(t); });
                    }
                    else if (c.kind == "scale") { if (!ParseVec(c.before, out var sb0)) throw new Refuse("unreadable scale in the ledger: " + c.key); todo.Add(() => { t.localScale = sb0; EditorUtility.SetDirty(t); if (PrefabUtility.IsPartOfPrefabInstance(t)) PrefabUtility.RecordPrefabInstancePropertyModifications(t); }); }
                    else if (c.kind == "material")
                    {
                        var r = t.GetComponent<Renderer>(); var mats = c.before.Split('|').Select(p => string.IsNullOrEmpty(p) ? null : AssetDatabase.LoadAssetAtPath<Material>(p)).ToArray();
                        if (r == null || mats.Any(m => m == null)) throw new Refuse("cannot restore the materials of " + c.key + " (" + c.before + ")");
                        todo.Add(() => { r.sharedMaterials = mats; EditorUtility.SetDirty(r); if (PrefabUtility.IsPartOfPrefabInstance(r)) PrefabUtility.RecordPrefabInstancePropertyModifications(r); });
                    }
                    else if (c.kind == "spawn") todo.Add(() => { var parent = t.parent; Object.DestroyImmediate(t.gameObject); if (parent != null && parent.childCount == 0 && parent.name == Str(d, "clone_root")) Object.DestroyImmediate(parent.gameObject); });
                }
                string backupDir = Path.Combine(Path.GetDirectoryName(Fix2LedgerFile(d, k.ScenePath)), "Backups", "fix2-revert-" + BuildingAudit308.Utc()); Directory.CreateDirectory(backupDir);
                File.Copy(Harness303.Abs(k.ScenePath), Path.Combine(backupDir, Path.GetFileName(k.ScenePath)), false);
                Fix2Save(d, ledger);
                EditorSceneManager.MarkSceneDirty(k.Scene);   // before the first change: a failure half way reloads the scene (GoBack)
                foreach (var a in todo) a();
                if (!EditorSceneManager.SaveScene(k.Scene)) throw new InvalidOperationException("SaveScene returned false");
                op.reverted = true; op.revertedUtc = BuildingAudit308.Utc(); Fix2Save(d, ledger);
                string sha = ShaOf(Harness303.Abs(k.ScenePath));
                GoBack(previous, k.Scene, opened);
                return "reverted: " + k.Alias + " op " + op.utc + " (" + todo.Count + " change(s) undone), scene sha " + Short(sha) + (sha == op.shaBefore ? " = the sha before the apply" : " (before the apply: " + Short(op.shaBefore) + ")") + (notes.Count > 0 ? "\n" + string.Join("\n", notes) : "");
            }
            catch { GoBack(previous, k.Scene, opened); throw; }
        }

        // ------------------------------------------------------------------ L6 (off by default)

        static string Fix2Veg(bool write)
        {
            var d = Fix2Data(); var veg = Req(d, "veg");
            var rows = Arr(veg, "rows").Where(r => Flag(r, "enabled")).ToList();
            if (!Flag(veg, "enabled") || rows.Count == 0) return "refused: propfix308.json veg is off (L6 found no broken row: " + Arr(veg, "rows").Count() + " row(s) listed, none switched on) - nothing to do";
            if (Harness303.IsProtected(Str(veg, "sheet")) || PostLedger308.IsProtectedPath(Str(veg, "sheet"))) return "refused: veg sheet " + Str(veg, "sheet") + " is under a protected path";
            var asks = rows.Select(r => new BuildingFix308.VegRowAsk308 { sheet = Str(veg, "sheet"), id = Str(r, "id"), proto = Str(r, "proto"), at = V3At(Req(r, "at")) }).ToList();
            return write ? BuildingFix308.VegRowsApply(Str(veg, "tag"), asks, Num(veg, "tol_m")) : BuildingFix308.VegRowsPlan(Str(veg, "tag"), asks, Num(veg, "tol_m"));
        }
    }
}
