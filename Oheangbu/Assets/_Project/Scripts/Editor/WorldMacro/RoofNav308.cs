using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 world bundle 4 (D308-24 answer 28): NavMesh that the bake left on building ROOFS. SPEC-CONTENT-PACING-308 "월드 묶음 4", AC-N1..N8.
    // Data = Art/World/Compact/Rebuild/CliffBoundary308/roofnav308.json (every number; without it every command refuses).
    //   status
    //   roof-scan:<alias>[:label=<name>]   READ ONLY. The loaded NavMesh over every building unit: the pieces that stand scan.high_m or more
    //                                      over the terrain, each with area, heights, slope, "is it reachable from the ground", what stands on
    //                                      it, whether it is the top of its building's drawn mesh, and the verdict. Report + JSON under ledger_dir.
    //   roof-plan:<alias>                  READ ONLY for the scene. The same scan + the boxes (one per REMOVE piece, cut into cells when
    //                                      BLOCKED), recorded as ledger_dir/roofplan-<alias>.json. This is the ONE heavy pass of a run.
    //   roof-apply:<alias>                 writes root data.root with one NavMeshModifierVolume (area = Not Walkable) per box of the RECORDED
    //                                      plan - it does not scan again. Refuses when the plan is stale (data / NavMesh / approval file /
    //                                      scene changed since), when a must_match row matched nothing, and after a bake. Scene backup +
    //                                      ledger BEFORE the save. A scene that is not in data.scenes[] answers "변경 없음" without being opened.
    //   roof-check:<alias>                 READ ONLY, after the bake, in a scene of data.scenes[]: AC-N1 (no NavMesh over the Play point; no
    //                                      high NavMesh of ANY verdict left on a must_match unit), AC-N6 (no NavMesh inside any written box),
    //                                      AC-N6b (BLOCKED / candidates listed as OPEN), AC-N7 (KEEP pieces still there, nothing new),
    //                                      AC-N8 (ground NavMesh of the treated units unchanged) against the plan the apply wrote.
    //   roof-revert:<alias>                takes the root out again (only when everything under it is this tool's), saves, closes the row.
    // <alias> = arch296 | folk298 | main. This tool never bakes: the one bake is CompactArchitecture296 Run "nav" (RUN_ORDER_world4.md).
    // What is REMOVE: a decisions[] row of the data, or - only for a piece the rule itself calls a roof CANDIDATE - a unit the main agent
    // listed in the approval file of this NavMesh (roof.approval_file). roof.auto_remove (shipped false) would remove every candidate.
    // Queue-safe: no dialog; refusals come back as "REFUSED ...". Only the target scene is saved (never AssetDatabase.SaveAssets).
    // Nothing here is a collider, a renderer or a light: a NavMeshModifierVolume only re-labels walkable spans inside its box at bake time.
    public static partial class RoofNav308
    {
        const string Usage = "status | roof-scan:<alias>[:label=<name>] | roof-plan:<alias> | roof-apply:<alias> | roof-check:<alias> | roof-revert:<alias>";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string DataFile => PostLedger308.RepoPath("Art/World/Compact/Rebuild/CliffBoundary308/roofnav308.json");
        sealed class Refuse : Exception { public Refuse(string m) : base(m) { } }

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c == "status") return Status();
                var a = c.Split(':');
                if (a.Length < 2) return "REFUSED unknown RoofNav308 command '" + c + "' (" + Usage + ")";
                string alias = a[1].Trim();
                string label = a.Skip(2).Select(x => x.Trim()).Where(x => x.StartsWith("label=", StringComparison.Ordinal)).Select(x => x.Substring(6)).FirstOrDefault();
                switch (a[0].Trim())
                {
                    case "roof-scan": return ScanCommand(alias, label);
                    case "roof-plan": return PlanCommand(alias);
                    case "roof-apply": return ApplyCommand(alias);
                    case "roof-check": return CheckCommand(alias);
                    case "roof-revert": return Revert(alias);
                    default: return "REFUSED unknown RoofNav308 command '" + c + "' (" + Usage + ")";
                }
            }
            catch (Refuse r) { return "REFUSED " + r.Message; }
            catch (PostLedger308.Refused r) { return "REFUSED " + r.Message; }
            catch (Exception e) { return "FAILED " + e; }
        }

        // ------------------------------------------------------------------ data

        static JObject Load(out string sha)
        {
            string f = DataFile;
            if (!File.Exists(f)) throw new Refuse("data missing: " + f + " (python Tools/Unity/Stage308_world4/world4_copy.py --apply)");
            JObject j;
            try { j = JObject.Parse(File.ReadAllText(f)); } catch (Exception e) { throw new Refuse("data does not parse: " + f + " (" + e.Message + ")"); }
            foreach (string k in new[] { "root", "ledger_dir", "scenes", "scan", "roof", "evidence", "volume", "check" }) Req(j, k);
            foreach (string k in new[] { "high_m", "unit_margin_m", "weld_m", "min_piece_m2", "ring_m", "ring_snap_m", "ring_ground_slack_m", "piece_snap_m", "ground_band_m", "max_triangles_per_unit" }) Num(j["scan"], k);
            foreach (string k in new[] { "share_min", "top_above_m", "top_below_m" }) Num(j["roof"], k);
            Req(j["roof"], "approval_file"); Req(j["roof"], "geo");
            foreach (string k in new[] { "pitch_min_deg", "share_min" }) Num(j["roof"]["geo"], k);
            foreach (string k in new[] { "xz_m", "y_m", "corridor_step_m", "surface_probe_up_m", "surface_probe_len_m" }) Num(j["evidence"], k);
            foreach (string k in new[] { "area", "xz_margin_m", "below_m", "above_m", "min_bottom_over_terrain_m", "surface_probe_up_m", "surface_probe_down_m", "surface_slack_m", "pose_tol_m", "yaw_tol_deg" }) Num(j["volume"], k);
            foreach (string k in new[] { "remove_left_m2", "keep_area_tol", "keep_area_tol_m2", "ground_area_tol_m2", "ground_area_tol_frac", "must_rows_min" }) Num(j["check"], k);
            if ((int)Num(j["volume"], "area") != 1) throw new Refuse("volume.area must be 1 (Not Walkable): this tool only takes NavMesh away");
            sha = BuildingAudit308.Sha(f);
            return j;
        }
        static JToken Req(JToken o, string key) { var t = o?[key]; if (t == null || t.Type == JTokenType.Null) throw new Refuse("roofnav308.json: missing '" + key + "'"); return t; }
        static float Num(JToken o, string key) => Req(o, key).Value<float>();
        static float OptNum(JToken o, string key, float fallback) { var t = o?[key]; return t == null || t.Type == JTokenType.Null ? fallback : t.Value<float>(); }
        static string Str(JToken o, string key) => Req(o, key).Value<string>();
        static string Opt(JToken o, string key) { var t = o?[key]; return t == null || t.Type == JTokenType.Null ? null : t.Value<string>(); }
        static bool Flag(JToken o, string key, bool fallback = false) { var t = o?[key]; return t != null && t.Type == JTokenType.Boolean ? t.Value<bool>() : fallback; }
        static IEnumerable<JToken> Arr(JToken o, string key) { var t = o?[key] as JArray; return t ?? (IEnumerable<JToken>)Array.Empty<JToken>(); }
        static string F(float v, string f = "F2") => float.IsNaN(v) ? "NaN" : v.ToString(f, Inv);
        static string V(Vector3 v) => string.Format(Inv, "({0:F2}, {1:F2}, {2:F2})", v.x, v.y, v.z);
        static string Short(string sha) => string.IsNullOrEmpty(sha) ? "-" : sha.Substring(0, Math.Min(12, sha.Length)).ToLowerInvariant();
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        static string ScenePath(string alias)
        {
            foreach (var s in PostLedger308.Scenes) if (PostLedger308.Short(s) == alias) return s;
            throw new Refuse("alias '" + alias + "' (arch296 | folk298 | main)");
        }
        static bool Listed(JObject d, string alias) => Arr(d, "scenes").Any(x => x.Value<string>() == alias);
        static string ScenesText(JObject d) => string.Join(", ", Arr(d, "scenes").Select(x => x.Value<string>()));
        static string LedgerDir(JObject d) => PostLedger308.RepoPath(Str(d, "ledger_dir"));
        static string LedgerFile(JObject d, string alias) => Path.Combine(LedgerDir(d), "ledger_roofnav308_" + alias + ".json");
        static string PlanFile(JObject d, string alias) => Path.Combine(LedgerDir(d), "roofplan-" + alias + ".json");
        static string AppliedPlanFile(JObject d, string alias) => Path.Combine(LedgerDir(d), "roofplan-" + alias + "-applied.json");
        static string ApprovalFile(JObject d, string alias) => PostLedger308.RepoPath(Str(d["roof"], "approval_file").Replace("{alias}", alias));
        static string NavSha(JObject d) { string nav = Opt(d, "nav_asset"); return nav != null && File.Exists(PostLedger308.Abs(nav)) ? BuildingAudit308.Sha(PostLedger308.Abs(nav)) : ""; }
        static JObject LedgerLoad(JObject d, string alias)
        {
            string f = LedgerFile(d, alias);
            if (!File.Exists(f)) return new JObject { ["alias"] = alias, ["ops"] = new JArray() };
            try { var j = JObject.Parse(File.ReadAllText(f)); if (!(j["ops"] is JArray)) j["ops"] = new JArray(); return j; }
            catch (Exception e) { throw new Refuse("ledger does not parse: " + f + " (" + e.Message + ") - nothing is written while it is unreadable"); }
        }
        static void LedgerSave(JObject d, string alias, JObject ledger)
        {
            Directory.CreateDirectory(LedgerDir(d));
            File.WriteAllText(LedgerFile(d, alias), ledger.ToString(Newtonsoft.Json.Formatting.Indented), Utf8);
        }
        static JObject LiveOp(JObject ledger) => ((JArray)ledger["ops"]).OfType<JObject>().LastOrDefault(o => !Flag(o, "reverted") && (Opt(o, "status") == "applied" || Opt(o, "status") == "applying"));

        // The main agent's approval of roof CANDIDATES (review R1): a file beside the ledger, bound to the NavMesh asset it was written for.
        // { "navSha": "<sha256 of the NavMesh asset, 12 or more hex>", "by": "...", "utc": "...", "units": ["<unit key>", ..],
        //   "careful": ["<unit key>", ..] }   careful = the second opt-in a unit under roof.careful_roots (or a read-only root) needs.
        // It can only switch on what the rule already calls a candidate; anything else needs a decisions[] row of the data.
        sealed class Approval
        {
            public string file = "", sha = "", navSha = "", by = "", utc = ""; public bool exists, live;
            public HashSet<string> units = new HashSet<string>(StringComparer.Ordinal), careful = new HashSet<string>(StringComparer.Ordinal), used = new HashSet<string>(StringComparer.Ordinal);
            public string Line => !exists ? "none (" + file + " absent: candidates are listed, not removed)"
                : file + " sha " + Short(sha) + " by '" + by + "' " + utc + ", " + units.Count + " unit(s), " + careful.Count + " careful - " + (live ? "LIVE for this NavMesh" : "IGNORED: written for NavMesh sha '" + navSha + "', not for the one loaded");
        }
        static Approval LoadApproval(JObject d, string alias, string navSha)
        {
            var a = new Approval { file = ApprovalFile(d, alias) };
            if (!File.Exists(a.file)) return a;
            a.exists = true; a.sha = BuildingAudit308.Sha(a.file);
            JObject j;
            try { j = JObject.Parse(File.ReadAllText(a.file)); } catch (Exception e) { throw new Refuse("approval file does not parse: " + a.file + " (" + e.Message + ") - fix it or take it away; nothing is read past it"); }
            a.navSha = (Opt(j, "navSha") ?? "").Trim().ToLowerInvariant(); a.by = Opt(j, "by") ?? ""; a.utc = Opt(j, "utc") ?? "";
            foreach (var u in Arr(j, "units")) a.units.Add(u.Value<string>());
            foreach (var u in Arr(j, "careful")) a.careful.Add(u.Value<string>());
            a.live = a.navSha.Length >= 12 && !string.IsNullOrEmpty(navSha) && navSha.StartsWith(a.navSha, StringComparison.OrdinalIgnoreCase);
            return a;
        }

        static bool Preview(Transform t) { for (var p = t; p != null; p = p.parent) if ((p.gameObject.hideFlags & HideFlags.DontSave) != 0) return true; return false; }
        static Transform Root(JObject d, Scene scene)
        {
            string name = Str(d, "root");
            var hit = scene.GetRootGameObjects().Where(g => g.name == name && !Preview(g.transform)).ToArray();
            if (hit.Length > 1) throw new Refuse("the scene has " + hit.Length + " roots named " + name);
            return hit.Length == 1 ? hit[0].transform : null;
        }
        // everything under the root is this tool's: children named volume.name_prefix* that carry exactly a NavMeshModifierVolume
        static bool OursOnly(JObject d, Transform root, out string why)
        {
            why = null; string prefix = Opt(d["volume"], "name_prefix") ?? "RoofVol_";
            if (root.GetComponents<Component>().Length != 1) { why = "the root carries a component"; return false; }
            foreach (Transform c in root)
            {
                if (!c.name.StartsWith(prefix, StringComparison.Ordinal) || c.childCount != 0) { why = "child " + c.name + " is not a " + prefix + " box"; return false; }
                var comps = c.GetComponents<Component>();
                if (comps.Length != 2 || c.GetComponent<NavMeshModifierVolume>() == null) { why = "child " + c.name + " carries something else than one NavMeshModifierVolume"; return false; }
            }
            return true;
        }
        static Vector3 Vec(JToken a) => new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>());
        // how many of the recorded boxes stand in the scene exactly as recorded (name, pose, size, area)
        static int BoxesInScene(JObject d, Transform root, List<JToken> boxes)
        {
            if (root == null) return 0;
            float tol = Num(d["volume"], "pose_tol_m"), yawTol = Num(d["volume"], "yaw_tol_deg"); int area = (int)Num(d["volume"], "area"), n = 0;
            foreach (var b in boxes)
            {
                var c = root.Find(Str(b, "name")); var v = c != null ? c.GetComponent<NavMeshModifierVolume>() : null;
                if (v != null && Vector3.Distance(c.position, Vec(b["centre"])) <= tol && Mathf.Abs(Mathf.DeltaAngle(c.eulerAngles.y, Num(b, "yaw"))) <= yawTol && (v.size - Vec(b["size"])).magnitude <= tol && v.center.magnitude <= tol && v.area == area) n++;
            }
            return n;
        }

        static string Status()
        {
            var d = Load(out string sha); var sb = new StringBuilder("RoofNav308 status\n  data " + DataFile + " sha " + Short(sha) + " (" + Opt(d, "version") + ")\n");
            sb.AppendLine("  scenes that take the volumes: " + ScenesText(d) + "; decisions " + Arr(d, "decisions").Count(x => Flag(x, "enabled", true)) + " on of " + Arr(d, "decisions").Count() + "; roof.auto_remove " + (Flag(d["roof"], "auto_remove") ? "ON (every candidate is removed)" : "off (candidates need the approval file)"));
            string navSha = NavSha(d);
            if (navSha.Length > 0) sb.AppendLine("  NavMesh asset " + Opt(d, "nav_asset") + " sha " + Short(navSha));
            foreach (var s in PostLedger308.Scenes)
            {
                string alias = PostLedger308.Short(s); var l = LedgerLoad(d, alias); var op = LiveOp(l);
                sb.AppendLine("  " + alias + ": " + (op == null ? "no live op" : Opt(op, "status") + " " + Opt(op, "utc") + " (" + Opt(op, "detail") + ")") + " | ledger " + LedgerFile(d, alias));
                if (!Listed(d, alias)) continue;
                string plan = PlanFile(d, alias);
                sb.AppendLine("     plan: " + (File.Exists(plan) ? "recorded " + File.GetLastWriteTimeUtc(plan).ToString("yyyyMMdd'T'HHmmss'Z'", Inv) + " sha " + Short(BuildingAudit308.Sha(plan)) : "none (roof-plan:" + alias + ")")
                    + "; applied plan: " + (File.Exists(AppliedPlanFile(d, alias)) ? "kept" : "none"));
                sb.AppendLine("     approval: " + LoadApproval(d, alias, navSha).Line);
            }
            var active = SceneManager.GetActiveScene(); var root = active.IsValid() ? Root(d, active) : null;
            sb.Append("  active scene " + active.path + (active.isDirty ? " (dirty)" : "") + ": " + Str(d, "root") + " " + (root == null ? "absent" : "present, " + root.childCount + " box(es)"));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ plan / apply / revert

        static string PlanCommand(string alias)
        {
            var d = Load(out string dataSha); string path = ScenePath(alias);
            PostLedger308.RequireEditable();
            var scene = PostLedger308.Open(path);
            var scan = Scan(d, scene, alias); var boxes = Plan(d, scan);
            var sb = new StringBuilder("RoofNav308 roof-plan " + alias + " (data " + Short(dataSha) + ", NavMesh " + scan.triangles + " triangle(s), " + scan.units + " building unit(s))\n");
            AppendPlan(d, sb, scan, boxes);
            var j = ScanJson(d, scan, dataSha, "plan"); j["boxes"] = BoxesJson(d, boxes);
            Directory.CreateDirectory(LedgerDir(d));
            File.WriteAllText(PlanFile(d, alias), j.ToString(Newtonsoft.Json.Formatting.Indented), Utf8);
            File.WriteAllText(Path.ChangeExtension(PlanFile(d, alias), ".txt"), sb.ToString(), Utf8);
            if (scene.isDirty) sb.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
            return sb.Append("plan only: nothing written to the scene; plan recorded " + PlanFile(d, alias)
                + (Listed(d, alias) ? " - roof-apply:" + alias + " writes exactly this plan (it does not scan again)" : " - this scene is NOT in data scenes[] (" + ScenesText(d) + "): roof-apply writes nothing here")).ToString();
        }

        static string ApplyCommand(string alias)
        {
            var d = Load(out string dataSha); string path = ScenePath(alias);
            PostLedger308.RequireEditable();
            // review R7: a scene the bake never reads is answered before it is opened (no scene load, no scan)
            if (!Listed(d, alias)) return "변경 없음 (" + alias + " is not listed in roofnav308.json scenes[]: the bake reads " + ScenesText(d) + " only; the scene was not opened, nothing written)";
            string planFile = PlanFile(d, alias);
            if (!File.Exists(planFile)) throw new Refuse("no recorded plan " + planFile + " (roof-plan:" + alias + " first: roof-apply writes a recorded plan, it does not scan)");
            JObject plan;
            try { plan = JObject.Parse(File.ReadAllText(planFile)); } catch (Exception e) { throw new Refuse("the plan does not parse: " + planFile + " (" + e.Message + ")"); }
            string navNow = NavSha(d); var approval = LoadApproval(d, alias, navNow);
            var all = Arr(plan, "boxes").ToList(); var want = all.Where(b => !Flag(b, "blocked")).ToList(); int blockedCount = all.Count - want.Count;
            var sb = new StringBuilder("RoofNav308 roof-apply " + alias + " (data " + Short(dataSha) + "; plan " + Opt(plan, "utc") + " sha " + Short(BuildingAudit308.Sha(planFile)) + ": " + want.Count + " box(es), " + blockedCount + " BLOCKED)\n");
            foreach (var b in all) sb.AppendLine("  " + (Flag(b, "blocked") ? "BLOCKED " : "box     ") + Opt(b, "unit") + " " + F(OptNum(b, "area", 0f), "F1") + " m2: centre " + V(Vec(b["centre"])) + " size " + V(Vec(b["size"])) + " yaw " + F(Num(b, "yaw"), "F1") + (Flag(b, "blocked") ? " - " + Opt(b, "why") : ""));
            var ledger = LedgerLoad(d, alias); var liveBefore = LiveOp(ledger);
            var scene = PostLedger308.Open(path); var root = Root(d, scene);
            // after a bake the NavMesh no longer shows the roofs the boxes keep out: a plan made from it would be EMPTY and an apply would
            // take the boxes away (the next bake would bring the roofs back). So once the NavMesh asset differs from the one the live
            // op was planned on, roof-apply writes nothing - roof-revert is the only way to take the boxes out.
            if (liveBefore != null && Opt(liveBefore, "status") == "applied" && root != null && (Opt(liveBefore, "navSha") ?? "") != navNow)
                return sb.Append("REFUSED the NavMesh asset changed since roof-apply " + Opt(liveBefore, "utc") + " (sha " + Short(Opt(liveBefore, "navSha")) + " -> " + Short(navNow)
                    + ": a bake ran). The " + root.childCount + " box(es) in the scene are what keeps those roofs out; a plan made from the baked NavMesh no longer sees them. Nothing written. To take the boxes out: roof-revert:" + alias
                    + ". To add boxes for what roof-check still lists: a decisions[] row / an approval is the main agent's call before the NEXT bake (roof-revert, roof-plan, roof-apply, bake).").ToString();
            if (root != null && root.childCount == want.Count && BoxesInScene(d, root, want) == want.Count)
                return sb.Append("변경 없음 (the scene already holds exactly these " + want.Count + " box(es); nothing written, scene not saved)").ToString();
            // the plan must be the last reading of what it was made from (review R1: what the main agent read is what gets written)
            var stale = new List<string>(); string sceneSha = BuildingAudit308.Sha(PostLedger308.Abs(path));
            if ((Opt(plan, "dataSha") ?? "") != dataSha) stale.Add("the data file changed (" + Short(Opt(plan, "dataSha")) + " -> " + Short(dataSha) + ")");
            if ((Opt(plan, "navSha") ?? "") != navNow) stale.Add("the NavMesh asset changed (" + Short(Opt(plan, "navSha")) + " -> " + Short(navNow) + ")");
            if ((Opt(plan, "approvalSha") ?? "") != approval.sha) stale.Add("the approval file changed (" + Short(Opt(plan, "approvalSha")) + " -> " + Short(approval.sha) + ")");
            if ((Opt(plan, "sceneSha") ?? "") != sceneSha) stale.Add("the scene file changed (" + Short(Opt(plan, "sceneSha")) + " -> " + Short(sceneSha) + ")");
            if (stale.Count > 0) throw new Refuse("the recorded plan is stale: " + string.Join("; ", stale) + " - roof-plan:" + alias + " again, nothing written");
            float leftTol = Num(d["check"], "remove_left_m2");
            foreach (var row in Arr(plan, "rows"))
                if (Flag(row, "mustMatch") && OptNum(row, "matchedM2", 0f) <= leftTol)
                    throw new Refuse("decisions[" + Opt(row, "index") + "] '" + Opt(row, "unit") + "' is must_match and the plan removes nothing there (" + F(OptNum(row, "highM2", 0f), "F1") + " m2 of high NavMesh on it, 0 decided by the row): the tool does not see what the row names - nothing written (read the plan's lines of that unit)");
            if (want.Count == 0 && root == null) return sb.Append("변경 없음 (no box to write and no root in the scene; scene not saved)").ToString();
            if (root != null && !OursOnly(d, root, out string why)) throw new Refuse(Str(d, "root") + " in " + path + " holds something that is not this tool's (" + why + ") - nothing written");
            string utc = PostLedger308.Utc();
            string backup = PostLedger308.Backup(Path.Combine(LedgerDir(d), "Backups"), "roofnav308-" + utc, path);
            var blockedM2 = new JObject();
            foreach (var g in all.Where(b => Flag(b, "blocked")).GroupBy(b => Opt(b, "unit") ?? "")) blockedM2[g.Key] = g.Sum(b => OptNum(b, "area", 0f));
            var op = new JObject
            {
                ["utc"] = utc, ["command"] = "roof-apply:" + alias, ["scene"] = path, ["dataSha"] = dataSha, ["status"] = "applying", ["reverted"] = false,
                ["shaBefore"] = sceneSha, ["navSha"] = navNow, ["planSha"] = BuildingAudit308.Sha(planFile), ["approvalSha"] = approval.sha, ["backup"] = backup, ["hadRoot"] = root != null,
                ["boxes"] = want.Count, ["blocked"] = blockedCount, ["blockedM2"] = blockedM2, ["boxList"] = new JArray(want.Select(b => b.DeepClone()).ToArray()),
                ["treatedUnits"] = new JArray(want.Select(b => Opt(b, "unit") ?? "").Distinct().ToArray()), ["detail"] = want.Count + " box(es) - ledger written before the scene save"
            };
            ((JArray)ledger["ops"]).Add(op); LedgerSave(d, alias, ledger);
            // what roof-check compares with = the plan these boxes came from: kept beside the ledger, swapped in only after the save
            string applied = AppliedPlanFile(d, alias), pending = applied + ".pending";
            try
            {
                if (root != null) Object.DestroyImmediate(root.gameObject);
                var go = new GameObject(Str(d, "root")); SceneManager.MoveGameObjectToScene(go, scene); go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                int layer = (int)OptNum(d["volume"], "layer", 0f), area = (int)Num(d["volume"], "area");
                foreach (var b in want)
                {
                    var c = new GameObject(Str(b, "name")); c.layer = layer; c.transform.SetParent(go.transform, false);
                    c.transform.SetPositionAndRotation(Vec(b["centre"]), Quaternion.Euler(0f, Num(b, "yaw"), 0f));
                    var v = c.AddComponent<NavMeshModifierVolume>(); v.center = Vector3.zero; v.size = Vec(b["size"]); v.area = area;
                }
                File.Copy(planFile, pending, true);
                PostLedger308.SaveScene(scene);
            }
            catch (Exception e)
            {
                ((JArray)ledger["ops"]).Remove(op); LedgerSave(d, alias, ledger);
                try { if (File.Exists(pending)) File.Delete(pending); } catch (Exception) { }
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);   // a half-built root must not stay in memory
                return "FAILED " + alias + " - " + e.Message + " (scene reloaded from disk, nothing saved, the ledger row was taken out again; backup " + backup + ")";
            }
            string planNote = "";
            try { File.Copy(pending, applied, true); File.Delete(pending); }
            catch (Exception e) { planNote = "\nWARN the scene is saved but the applied-plan copy could not be written (" + e.Message + "): copy " + pending + " to " + applied + " by hand before roof-check"; }
            op["status"] = "applied"; op["shaAfter"] = BuildingAudit308.Sha(PostLedger308.Abs(path)); op["detail"] = want.Count + " box(es) saved, " + blockedCount + " BLOCKED";
            // an earlier live row of this scene is closed: its root was replaced by this one (one live row = the boxes that stand in the scene)
            foreach (var old in ((JArray)ledger["ops"]).OfType<JObject>().Where(o => o != op && !Flag(o, "reverted") && (Opt(o, "status") == "applied" || Opt(o, "status") == "applying")))
            { old["reverted"] = true; old["supersededBy"] = utc; old["detail"] = Opt(old, "detail") + "; superseded by roof-apply " + utc; }
            LedgerSave(d, alias, ledger);
            return sb.Append("applied: " + want.Count + " box(es) under " + Str(d, "root") + ", " + blockedCount + " BLOCKED; scene sha " + Short(Opt(op, "shaBefore")) + " -> " + Short(Opt(op, "shaAfter")) + ", backup " + backup + planNote
                + "\nnext: the ONE bake (CompactArchitecture296 Run \"nav\", RUN_ORDER_world4.md G), then roof-check:" + alias).ToString();
        }

        static string Revert(string alias)
        {
            var d = Load(out _); string path = ScenePath(alias);
            PostLedger308.RequireEditable();
            var ledger = LedgerLoad(d, alias); var op = LiveOp(ledger);
            if (op == null) return "변경 없음 (" + alias + ": no live RoofNav308 op in the ledger; nothing written)";
            var scene = PostLedger308.Open(path); var root = Root(d, scene);
            if (Opt(op, "status") == "applying" && BuildingAudit308.Sha(PostLedger308.Abs(path)) == Opt(op, "shaBefore"))
            {
                op["reverted"] = true; op["revertedUtc"] = PostLedger308.Utc(); op["detail"] = Opt(op, "detail") + "; settled: the scene was never saved"; LedgerSave(d, alias, ledger);
                return "reverted: " + alias + " op " + Opt(op, "utc") + " was left 'applying' and the scene was never saved - ledger row closed, nothing to undo";
            }
            if (root == null)
            {
                op["reverted"] = true; op["revertedUtc"] = PostLedger308.Utc(); op["detail"] = Opt(op, "detail") + "; closed: the root is not in the scene any more"; LedgerSave(d, alias, ledger);
                return "reverted: " + alias + " - " + Str(d, "root") + " is not in the scene (taken out by hand?); ledger row closed, scene not saved";
            }
            if (!OursOnly(d, root, out string why)) throw new Refuse(Str(d, "root") + " holds something that is not this tool's (" + why + ") - nothing written");
            int n = root.childCount;
            string backup = PostLedger308.Backup(Path.Combine(LedgerDir(d), "Backups"), "roofnav308-revert-" + PostLedger308.Utc(), path);
            Object.DestroyImmediate(root.gameObject);
            try { PostLedger308.SaveScene(scene); }
            catch (Exception e) { EditorSceneManager.OpenScene(path, OpenSceneMode.Single); return "FAILED " + alias + " - " + e.Message + " (scene reloaded from disk, nothing saved; the ledger row is still live; backup " + backup + ")"; }
            op["reverted"] = true; op["revertedUtc"] = PostLedger308.Utc(); LedgerSave(d, alias, ledger);
            string sha = BuildingAudit308.Sha(PostLedger308.Abs(path));
            return "reverted: " + alias + " op " + Opt(op, "utc") + " (" + n + " box(es) and the root taken out), scene sha " + Short(sha) + (sha == Opt(op, "shaBefore") ? " = the sha before the apply" : " (before the apply: " + Short(Opt(op, "shaBefore")) + ")")
                + ", backup " + backup + "\nif a bake ran with the boxes, the NavMesh still has those roofs cut out until the next bake: bake again (or put the NavMesh asset of the insurance copy back) - RUN_ORDER_world4.md, 되돌리기";
        }
    }
}
