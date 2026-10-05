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
    // (d) escort stops (D308-16 relayout fix F5). The same grounding the `escort` op gave the escort START, for the other stops of the
    // road: 첫 검문 (checkpoint_1), 둘째 검문 (checkpoint_2), 인도 (cargo_delivery). Their roots were authored on a terrain that has
    // been rebuilt since: Parking / the wait nodes / the inspector / the desk stand 3.2 - 3.8 m UNDER the ground at checkpoint_1 and
    // cargo_delivery (offline, height_p1b) and the escort audit reports them as `A1 ... [pre-existing]` / `A2 support False`.
    // One ledger per scene (all stops of the data list):
    //   1. the scene's own content asset: Y of each stop's interaction point := the measured ground under it; for a stop that owns
    //      its respawn checkpoint (data row "checkpoint") the feet Y too
    //   2. every direct child of the stop root keeps its XZ and takes the Y of its own ground (+ the row's lift for that name);
    //      Interaction gets the point's Y, Checkpoint the feet's Y, so the escort audit A1 (3D distance <= 0.01) holds
    //   3. children named in "keep" are NOT touched (the rest bench and the Checkpoint node the relayout's T5 pass owns; the
    //      guesthouse BuildingFix308 seated). The stop ROOT is not moved either: it is a marker with no component, and leaving it
    //      where it is keeps every local pose other ledgers recorded under it valid (Content308's scene ledger holds the bench and
    //      the Checkpoint node as local poses) - so this op and those ledgers can be reverted in any order.
    // XZ never changes. The witness against "seated on a roof / a crate" is the layout's HEIGHT FIELD (CompactWorldLayoutSO.FinalSurface):
    // a measured ground more than expect_tol_m from it refuses the whole stop (Pieces308.Ground counts every object named *Surface*
    // as terrain - the south gate roof taught that to MainPath).
    //   stops:plan:<scene> | stops:apply:<scene> | stops:verify:<scene> | stops:revert:<scene>      (optional 4th part: one stop id)
    // Review pass (2026-10-05): (1) the ledger is written BEFORE the saves with state "applying" and flipped to "applied" after
    // them - a crash in between leaves a ledger that names the before-values, and revert accepts it; (2) revert refuses when a
    // node or a content Y stands neither where this op put it nor where it was before (another tool moved it since) unless the
    // command ends with :force; (3) plan refuses a node whose XZ lies inside the collider bounds of a kept child (the ground rule
    // ignores everything under the stop root, so such a node would be seated on the terrain UNDER the kept building).
    // After apply: Content308 campaign-apply (the stage destinations of checkpoint_one / checkpoint_two / delivery follow their points),
    // and Content308 content-revert of that scene needs this op reverted first (the content asset changed after its ledger).
    public static partial class SmallOps308
    {
        [Serializable] sealed class StopNode { public string node = "", role = "", support = ""; public Vector3 localBefore, worldAfter; public float ground, field; }
        [Serializable] sealed class StopRow
        {
            public string id = "", point = "", checkpoint = "";
            public float pointYBefore, pointYAfter, feetYBefore, feetYAfter; public int pointYBits, feetYBits; public bool feetOwned;
            public List<StopNode> nodes = new List<StopNode>();
        }
        [Serializable] sealed class StopsLedger
        {
            public string format = "cb308.small.stops.1", scene = "", key = "", state = "none", utc = "", configSha256 = "", content = "", contentShaBefore = "", contentShaAfter = "", sceneShaBefore = "", sceneShaAfter = "", backup = "";
            public List<StopRow> stops = new List<StopRow>(); public string[] history = new string[0];
        }

        sealed class StopPlan
        {
            public JToken Row; public string Id = ""; public Transform Root; public PrologueContentSO.Point Point; public WorldMacroPlaytestSO.CheckpointSpec Check;
            public float PointY, FeetY; public bool Change;
            public readonly List<(Transform t, Vector3 target, float ground, float field, string support, string role)> Nodes = new List<(Transform, Vector3, float, float, string, string)>();
            public readonly List<Transform> Keep = new List<Transform>();
        }
        sealed class StopsPlan
        {
            public Scene Scene; public WorldMacroPlaytestSO Content; public string ContentPath = "";
            public readonly List<StopPlan> Stops = new List<StopPlan>(); public readonly List<string> Lines = new List<string>(), Refuse = new List<string>();
        }

        static StopsPlan StopsMeasure(JObject data, string scenePath, string key, bool write, string only)
        {
            var e = Pieces308.Req(data, "escort_stops"); var k = new StopsPlan();
            k.ContentPath = Pieces308.Str(Pieces308.Req(Pieces308.Req(data, "escort"), "content"), key);
            if (PostLedger308.IsProtectedPath(k.ContentPath)) throw new PostLedger308.Refused("protected content path " + k.ContentPath);
            k.Scene = Pieces308.OpenTarget(scenePath, data["preview_roots"], write);
            var session = CliffCore308.Session(k.Scene); if (session == null) throw new PostLedger308.Refused("no playtest session in " + scenePath);
            k.Content = session.Content;
            if (k.Content == null || AssetDatabase.GetAssetPath(k.Content) != k.ContentPath) throw new PostLedger308.Refused("the session of " + scenePath + " uses " + (k.Content == null ? "no content" : AssetDatabase.GetAssetPath(k.Content)) + ", the data names " + k.ContentPath);
            if (session.MountainLayout == null || session.MountainLayout.FinalSurface == null) throw new PostLedger308.Refused("the session of " + scenePath + " has no layout height field (MountainLayout.FinalSurface): the witness against a roof / a crate is missing");
            CompactWorldSurface field;
            try { field = new CompactWorldSurface(session.MountainLayout); } catch (Exception ex) { throw new PostLedger308.Refused("the layout height field does not load: " + ex.Message); }
            float poseTol = Pieces308.Num(e, "pose_tol_m"), seatTol = Pieces308.Num(e, "seat_tol_m"), window = Pieces308.Num(e, "support_window_m"), expectTol = Pieces308.Num(e, "expect_tol_m"), maxShift = Pieces308.Num(e, "max_shift_m");
            k.Lines.Add("height field " + AssetDatabase.GetAssetPath(session.MountainLayout.FinalSurface) + " sha " + Pieces308.Short(Pieces308.ShaAsset(AssetDatabase.GetAssetPath(session.MountainLayout.FinalSurface))) + " | seat_tol " + Pieces308.F(seatTol) + ", expect_tol " + Pieces308.F(expectTol) + ", max_shift " + Pieces308.F(maxShift, "F1"));
            foreach (var row in Pieces308.Arr(e, "stops"))
            {
                string id = Pieces308.Str(row, "id"); if (!string.IsNullOrEmpty(only) && only != id) continue;
                if (row["enabled"] != null && !Pieces308.Flag(row, "enabled")) { k.Lines.Add("stop " + id + ": off in data"); continue; }
                var s = new StopPlan { Row = row, Id = id };
                var hit = Pieces308.FindAll(k.Scene, Pieces308.Str(row, "stop"));
                if (hit.Count != 1) { k.Refuse.Add("stop " + id + ": " + hit.Count + " objects at " + Pieces308.Str(row, "stop") + " (expected 1)"); continue; }
                s.Root = hit[0];
                if (PostLedger308.UnderProtectedTree(s.Root)) { k.Refuse.Add("stop " + id + " is under a protected tree"); continue; }
                string pointId = Pieces308.Str(row, "point"), checkId = Pieces308.Opt(row, "checkpoint");
                s.Point = (k.Content.Points ?? Array.Empty<PrologueContentSO.Point>()).FirstOrDefault(x => x != null && x.Id == pointId);
                if (s.Point == null) { k.Refuse.Add("stop " + id + ": the content has no point " + pointId); continue; }
                if (checkId.Length > 0)
                {
                    s.Check = (k.Content.Checkpoints ?? Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>()).FirstOrDefault(x => x != null && x.Id == checkId);
                    if (s.Check == null) { k.Refuse.Add("stop " + id + ": the content has no checkpoint " + checkId); continue; }
                }
                var keep = new HashSet<string>(Pieces308.Arr(row, "keep").Select(t => t.Value<string>())); var lifts = row["lifts"] as JObject;
                var interaction = s.Root.Find("Interaction"); var checkpoint = s.Root.Find("Checkpoint");
                if (interaction == null) { k.Refuse.Add("stop " + id + " has no Interaction child"); continue; }
                if (Vector2.Distance(new Vector2(interaction.position.x, interaction.position.z), new Vector2(s.Point.Position.x, s.Point.Position.z)) > poseTol)
                { k.Refuse.Add("stop " + id + ": Interaction " + Pieces308.V(interaction.position) + " is not over the content point " + Pieces308.V(s.Point.Position) + " in XZ (this op never moves XZ)"); continue; }
                if (s.Check != null && (checkpoint == null || keep.Contains("Checkpoint") || Vector2.Distance(new Vector2(checkpoint.position.x, checkpoint.position.z), new Vector2(s.Check.Feet.x, s.Check.Feet.z)) > poseTol))
                { k.Refuse.Add("stop " + id + ": the row names checkpoint " + checkId + " but the Checkpoint node is missing, kept, or not over its feet in XZ"); continue; }
                var rule = new Pieces308.GroundRule(k.Scene, s.Root);
                var keepBounds = new List<(string name, Bounds b)>();
                foreach (Transform kc in s.Root) if (keep.Contains(kc.name)) foreach (var col in kc.GetComponentsInChildren<Collider>(false)) if (col.enabled && !col.isTrigger) keepBounds.Add((kc.name, col.bounds));
                k.Lines.Add("stop " + id + " root " + Pieces308.V(s.Root.position) + " | point " + pointId + " " + Pieces308.V(s.Point.Position) + (s.Check != null ? " | checkpoint " + checkId + " feet " + Pieces308.V(s.Check.Feet) : " | checkpoint node kept (owned by another pass)"));
                bool bad = false;
                foreach (Transform n in s.Root)
                {
                    if (keep.Contains(n.name)) { s.Keep.Add(n); k.Lines.Add("  keep " + n.name + " " + Pieces308.V(n.position) + " (not touched; not this op's)"); continue; }
                    int under = keepBounds.FindIndex(kb => n.position.x >= kb.b.min.x && n.position.x <= kb.b.max.x && n.position.z >= kb.b.min.z && n.position.z <= kb.b.max.z);
                    if (under >= 0) { k.Refuse.Add("stop " + id + " node " + n.name + " " + Pieces308.V(n.position) + " lies inside the collider bounds of the kept child " + keepBounds[under].name + " (XZ): its ground would be measured UNDER that object (the rule ignores the stop's own subtree). This op never moves XZ - the node needs a place decision"); bad = true; continue; }
                    if (!Pieces308.Ground(rule, n.position.x, n.position.z, window, out var h)) { k.Refuse.Add("stop " + id + ": no ground under node " + n.name); bad = true; continue; }
                    float fy = field.Sample(n.position.x, n.position.z); float lift = lifts != null && lifts[n.name] != null ? lifts[n.name].Value<float>() : 0f;
                    var target = new Vector3(n.position.x, h.point.y + lift, n.position.z); string support = PostLedger308.PathOf(h.collider.transform);
                    string role = n == interaction ? "interaction" : n == checkpoint ? "checkpoint" : lift != 0f ? "lift " + Pieces308.F(lift) : "node";
                    s.Nodes.Add((n, target, h.point.y, fy, support, role));
                    k.Lines.Add("  node " + n.name + ": y " + Pieces308.F(n.position.y, "F3") + " -> " + Pieces308.F(target.y, "F3") + " (" + (target.y - n.position.y >= 0 ? "+" : "") + Pieces308.F(target.y - n.position.y, "F3") + ") ground " + Pieces308.F(h.point.y, "F3") + " on " + support + ", height field " + Pieces308.F(fy, "F3") + (lift != 0f ? ", lift " + Pieces308.F(lift) : ""));
                    if (Mathf.Abs(h.point.y - fy) > expectTol) { k.Refuse.Add("stop " + id + " node " + n.name + " would be seated on " + support + " at " + Pieces308.F(h.point.y, "F3") + ", " + Pieces308.F(h.point.y - fy, "F3") + " m off the height field (escort_stops.expect_tol_m " + Pieces308.F(expectTol) + "): something other than terrain answers the ray there"); bad = true; }
                    if (Mathf.Abs(target.y - n.position.y) > maxShift) { k.Refuse.Add("stop " + id + " node " + n.name + " would move " + Pieces308.F(target.y - n.position.y, "F2") + " m (escort_stops.max_shift_m " + Pieces308.F(maxShift, "F1") + ")"); bad = true; }
                }
                if (bad) continue;
                var gi = s.Nodes.First(x => x.t == interaction); s.PointY = gi.ground;
                if (s.Check != null) s.FeetY = s.Nodes.First(x => x.t == checkpoint).ground;
                s.Change = Mathf.Abs(s.Point.Position.y - s.PointY) > seatTol || s.Check != null && Mathf.Abs(s.Check.Feet.y - s.FeetY) > seatTol
                           || s.Nodes.Any(x => Vector3.Distance(x.t.position, x.target) > seatTol);
                k.Lines.Add("  content: point " + pointId + " Y " + Pieces308.F(s.Point.Position.y, "F3") + " -> " + Pieces308.F(s.PointY, "F3") + (s.Check != null ? ", checkpoint " + checkId + " feet Y " + Pieces308.F(s.Check.Feet.y, "F3") + " -> " + Pieces308.F(s.FeetY, "F3") : "")
                    + " | the root stays at " + Pieces308.V(s.Root.position) + " (a marker)" + (s.Change ? "" : " | already seated"));
                k.Stops.Add(s);
            }
            return k;
        }

        static string Stops(JObject data, string dataSha, string scenePath, string key, string verb, string only)
        {
            string name = LedgerName("stops", key);
            if (verb == "revert") return StopsRevert(data, scenePath, key, name, only == "force");
            if (verb == "verify") return StopsVerify(data, scenePath, key, name, only);
            bool dry = verb == "plan";
            var k = StopsMeasure(data, scenePath, key, !dry, only);
            var sb = new StringBuilder("stops " + verb + " " + scenePath + "\n"); foreach (string line in k.Lines) sb.AppendLine("  " + line);
            foreach (string r in k.Refuse) sb.AppendLine("  REFUSE " + r);
            bool change = k.Stops.Any(s => s.Change);
            if (!change && k.Refuse.Count == 0) return sb.Append("  변경 없음 (every listed stop stands on its ground; nothing written, nothing saved)").ToString();
            if (dry) return sb.Append(k.Refuse.Count == 0 ? "  apply would write the content asset and the scene; nothing changed" : "  apply would be REFUSED; nothing changed").ToString();
            if (k.Refuse.Count > 0) throw new PostLedger308.Refused(k.Refuse[0]);
            if (EditorUtility.IsDirty(k.Content)) throw new PostLedger308.Refused(k.ContentPath + " has unsaved in-memory changes (another session?)");

            string utc = PostLedger308.Utc(); string backup = Pieces308.Backup(utc + "-stops-" + key, scenePath, k.ContentPath);
            var prior = Pieces308.ReadLedger<StopsLedger>(name);
            // a second apply keeps the FIRST before-values of a stop (the revert goes back to the state before this op)
            string ledgerFile = Pieces308.OutFile(name); string priorText = File.Exists(ledgerFile) ? File.ReadAllText(ledgerFile, Encoding.UTF8) : null;
            var l = prior != null && (prior.state == "applied" || prior.state == "applying") ? prior : new StopsLedger { scene = scenePath, key = key, content = k.ContentPath, contentShaBefore = Pieces308.ShaAsset(k.ContentPath), sceneShaBefore = Pieces308.ShaAsset(scenePath), backup = backup };
            string contentAbs = PostLedger308.Abs(k.ContentPath); var contentBytes = File.ReadAllBytes(contentAbs);
            // 1. the ledger first (state "applying"): every before-value and every target is on disk before anything is saved
            foreach (var s in k.Stops)
            {
                var row = l.stops.FirstOrDefault(x => x.id == s.Id);
                if (row == null)
                {
                    row = new StopRow { id = s.Id, point = s.Point.Id, checkpoint = s.Check != null ? s.Check.Id : "", feetOwned = s.Check != null,
                        pointYBefore = s.Point.Position.y, pointYBits = Bits(s.Point.Position.y), feetYBefore = s.Check != null ? s.Check.Feet.y : 0f, feetYBits = s.Check != null ? Bits(s.Check.Feet.y) : 0 };
                    foreach (Transform n in s.Root) if (!s.Keep.Contains(n)) row.nodes.Add(new StopNode { node = n.name, localBefore = n.localPosition });
                    l.stops.Add(row);
                }
                foreach (var (t, target, ground, fy, support, role) in s.Nodes)
                {
                    var nr = row.nodes.FirstOrDefault(x => x.node == t.name); if (nr == null) { nr = new StopNode { node = t.name, localBefore = t.localPosition }; row.nodes.Add(nr); }
                    nr.worldAfter = target; nr.ground = ground; nr.field = fy; nr.support = support; nr.role = role;
                }
                row.pointYAfter = s.PointY; row.feetYAfter = s.FeetY;
            }
            l.state = "applying"; l.utc = utc; l.configSha256 = dataSha; l.history = Add(l.history, "applying " + string.Join(", ", k.Stops.Select(s => s.Id)) + " (backup " + backup + ")");
            Pieces308.WriteLedger(name, l);
            // 2. the writes
            try
            {
                foreach (var s in k.Stops)
                {
                    s.Point.Position = new Vector3(s.Point.Position.x, s.PointY, s.Point.Position.z);
                    if (s.Check != null) s.Check.Feet = new Vector3(s.Check.Feet.x, s.FeetY, s.Check.Feet.z);
                    foreach (var (t, target, ground, fy, support, role) in s.Nodes) t.position = target;
                }
                EditorUtility.SetDirty(k.Content); AssetDatabase.SaveAssetIfDirty(k.Content);
                Physics.SyncTransforms();
                Pieces308.SaveOrReload(k.Scene);
            }
            catch
            {
                // the content asset must not stay ahead of a scene that was not saved: put its bytes back; the ledger goes back to
                // what it was before this command (nothing of this apply is on disk any more)
                File.WriteAllBytes(contentAbs, contentBytes); AssetDatabase.ImportAsset(k.ContentPath, ImportAssetOptions.ForceUpdate);
                Pieces308.Reload(k.Scene);
                if (priorText != null) File.WriteAllText(ledgerFile, priorText, new UTF8Encoding(false)); else if (File.Exists(ledgerFile)) File.Delete(ledgerFile);
                throw;
            }
            // 3. the saves are on disk: the ledger says so
            l.state = "applied"; l.contentShaAfter = Pieces308.ShaAsset(k.ContentPath); l.sceneShaAfter = Pieces308.ShaAsset(scenePath); l.history = Add(l.history, "applied " + string.Join(", ", k.Stops.Select(s => s.Id)));
            Pieces308.WriteLedger(name, l);
            sb.AppendLine("  wrote " + k.ContentPath + " sha " + Pieces308.Short(l.contentShaBefore) + " -> " + Pieces308.Short(l.contentShaAfter) + " and the scene " + Pieces308.Short(l.sceneShaBefore) + " -> " + Pieces308.Short(l.sceneShaAfter));
            sb.Append("  backup " + backup + " | ledger " + Pieces308.OutFile(name) + " | next: stops:verify:" + key + ", then Content308 campaign-apply (stage destinations follow the points); Content308 content-revert of this scene now needs stops:revert first");
            return sb.ToString();
        }

        static string StopsVerify(JObject data, string scenePath, string key, string name, string only)
        {
            var e = Pieces308.Req(data, "escort_stops"); var k = StopsMeasure(data, scenePath, key, false, only);
            var sb = new StringBuilder("stops verify " + scenePath + "\n"); int fail = 0, wait = 0;
            void Row(bool ok, string text) { if (!ok) fail++; sb.AppendLine("  " + (ok ? "PASS " : "FAIL ") + text); }
            float seatTol = Pieces308.Num(e, "seat_tol_m"), audit = Pieces308.Num(e, "audit_support_tol_m"), nav = Pieces308.Num(e, "nav_radius_m");
            foreach (string r in k.Refuse) Row(false, "measure: " + r);
            foreach (var s in k.Stops)
            {
                var interaction = s.Root.Find("Interaction"); var checkpoint = s.Root.Find("Checkpoint");
                float di = Vector3.Distance(interaction.position, s.Point.Position);
                Row(di <= .01f, "A1 " + s.Id + ": Interaction " + Pieces308.V(interaction.position) + " on the content point " + Pieces308.V(s.Point.Position) + " (" + Pieces308.F(di, "F3") + " m, rule 0.01)");
                if (s.Check != null) { float dc = Vector3.Distance(checkpoint.position, s.Check.Feet); Row(dc <= .01f, "A1 " + s.Id + ": Checkpoint " + Pieces308.V(checkpoint.position) + " on the checkpoint feet " + Pieces308.V(s.Check.Feet) + " (" + Pieces308.F(dc, "F3") + " m, rule 0.01)"); }
                var walk = new HashSet<string>(Pieces308.Arr(s.Row, "walk_nodes").Select(t => t.Value<string>()));
                foreach (var (t, target, ground, fy, support, role) in s.Nodes)
                {
                    float gap = t.position.y - target.y;
                    bool sup = Physics.RaycastAll(t.position + Vector3.up * .5f, Vector3.down, .8f + Mathf.Abs(target.y - ground), ~0, QueryTriggerInteraction.Ignore).Any(h => !(h.collider is CharacterController) && !h.collider.transform.IsChildOf(s.Root) && Mathf.Abs(h.point.y - ground) <= audit);
                    Row(Mathf.Abs(gap) <= seatTol && sup, s.Id + "/" + t.name + " " + Pieces308.V(t.position) + ": ground " + Pieces308.F(ground, "F3") + (role.StartsWith("lift", StringComparison.Ordinal) ? " + " + role : "") + " (gap " + Pieces308.F(gap, "F3") + ", rule " + Pieces308.F(seatTol) + "), height field " + Pieces308.F(fy, "F3") + ", support " + sup);
                    if (!walk.Contains(t.name)) continue;
                    bool onNav = NavMesh.SamplePosition(t.position, out var nh, nav, NavMesh.AllAreas) && Vector3.Distance(nh.position, t.position) <= nav;
                    if (onNav) sb.AppendLine("  PASS " + s.Id + "/" + t.name + ": NavMesh within " + Pieces308.F(nav, "F1") + " m");
                    else { wait++; sb.AppendLine("  WAIT " + s.Id + "/" + t.name + ": no NavMesh within " + Pieces308.F(nav, "F1") + " m (expected until the one bake after everything; audit A2 / A4 of this stop stay red till then)"); }
                }
                foreach (var t in s.Keep) sb.AppendLine("  INFO " + s.Id + "/" + t.name + " " + Pieces308.V(t.position) + " not touched (owned by another pass)");
            }
            var l = Pieces308.ReadLedger<StopsLedger>(name);
            sb.AppendLine("  INFO ledger " + (l == null ? "none" : l.state + " " + l.utc + ", stops " + string.Join(", ", l.stops.Select(x => x.id)) + ", content sha now " + Pieces308.Short(Pieces308.ShaAsset(k.ContentPath)) + (l.contentShaAfter == Pieces308.ShaAsset(k.ContentPath) ? " (= this ledger's write)" : " (changed after this ledger)")));
            if (k.Scene.isDirty) { fail++; sb.AppendLine("  FAIL the scene is dirty after a read-only command (bug): reload it"); }
            sb.Insert(0, (fail == 0 ? "GREEN" : "RED") + " (FAIL " + fail + ", WAIT " + wait + ") ");
            return Pieces308.Report("cb308-small-stops-verify-" + key + ".txt", sb);
        }

        static string StopsRevert(JObject data, string scenePath, string key, string name, bool force)
        {
            var l = Pieces308.ReadLedger<StopsLedger>(name);
            // "applying" = an apply that did not reach its last line (a crash between the saves and the ledger flip): the before-values are in it
            if (l == null || l.state != "applied" && l.state != "applying") throw new PostLedger308.Refused("no applied ledger " + name);
            if (PostLedger308.IsProtectedPath(l.content)) throw new PostLedger308.Refused("protected content path in the ledger");
            var e = Pieces308.Req(data, "escort_stops"); var scene = Pieces308.OpenTarget(scenePath, data["preview_roots"], true);
            var session = CliffCore308.Session(scene); var content = session != null ? session.Content : null;
            if (content == null || AssetDatabase.GetAssetPath(content) != l.content) throw new PostLedger308.Refused("the scene session does not use " + l.content);
            if (EditorUtility.IsDirty(content)) throw new PostLedger308.Refused(l.content + " has unsaved in-memory changes");
            var rows = new List<(StopRow row, Transform root, PrologueContentSO.Point point, WorldMacroPlaytestSO.CheckpointSpec check)>();
            foreach (var row in l.stops)
            {
                var cfg = Pieces308.Arr(e, "stops").FirstOrDefault(x => Pieces308.Opt(x, "id") == row.id);
                if (cfg == null) throw new PostLedger308.Refused("the data no longer lists the stop " + row.id + " of the ledger (its scene path is needed for the revert)");
                var root = Pieces308.FindOne(scene, Pieces308.Str(cfg, "stop"), "escort stop " + row.id);
                var point = content.Points.FirstOrDefault(x => x != null && x.Id == row.point); var check = row.feetOwned ? content.Checkpoints.FirstOrDefault(x => x != null && x.Id == row.checkpoint) : null;
                if (point == null || row.feetOwned && check == null) throw new PostLedger308.Refused("the content lost the point / checkpoint of " + row.id);
                rows.Add((row, root, point, check));
            }
            // drift: a value this op wrote must still stand where the op put it (or where it was before - an "applying" ledger);
            // anything else was moved by another tool since, and putting the before-value back would undo that silently
            float seatTol = Pieces308.Num(e, "seat_tol_m"); var drift = new List<string>();
            foreach (var (row, root, point, check) in rows)
            {
                if (Mathf.Abs(point.Position.y - row.pointYAfter) > seatTol && Mathf.Abs(point.Position.y - FromBits(row.pointYBits)) > seatTol) drift.Add(row.id + " point " + row.point + " Y " + Pieces308.F(point.Position.y, "F3") + " (this op wrote " + Pieces308.F(row.pointYAfter, "F3") + ")");
                if (check != null && Mathf.Abs(check.Feet.y - row.feetYAfter) > seatTol && Mathf.Abs(check.Feet.y - FromBits(row.feetYBits)) > seatTol) drift.Add(row.id + " checkpoint " + row.checkpoint + " feet Y " + Pieces308.F(check.Feet.y, "F3") + " (this op wrote " + Pieces308.F(row.feetYAfter, "F3") + ")");
                foreach (var n in row.nodes)
                {
                    var t = root.Find(n.node); if (t == null) { drift.Add(row.id + "/" + n.node + " is gone"); continue; }
                    if (Vector3.Distance(t.position, n.worldAfter) > seatTol && Vector3.Distance(t.localPosition, n.localBefore) > seatTol) drift.Add(row.id + "/" + n.node + " at " + Pieces308.V(t.position) + " (this op put it at " + Pieces308.V(n.worldAfter) + ")");
                }
            }
            if (drift.Count > 0 && !force) throw new PostLedger308.Refused("stops revert " + key + ": " + drift.Count + " value(s) moved after this op wrote them (" + string.Join("; ", drift.Take(4)) + (drift.Count > 4 ? "; ..." : "") + "). Nothing reverted. stops:revert:" + key + ":force puts the recorded before-values back anyway");
            string utc = PostLedger308.Utc(); string backup = Pieces308.Backup(utc + "-stops-revert-" + key, scenePath, l.content);
            string abs = PostLedger308.Abs(l.content); var bytes = File.ReadAllBytes(abs);
            try
            {
                foreach (var (row, root, point, check) in rows)
                {
                    point.Position = new Vector3(point.Position.x, FromBits(row.pointYBits), point.Position.z);
                    if (check != null) check.Feet = new Vector3(check.Feet.x, FromBits(row.feetYBits), check.Feet.z);
                    foreach (var n in row.nodes) { var t = root.Find(n.node); if (t != null) t.localPosition = n.localBefore; }
                }
                EditorUtility.SetDirty(content); AssetDatabase.SaveAssetIfDirty(content);
                Pieces308.SaveOrReload(scene);
            }
            catch { File.WriteAllBytes(abs, bytes); AssetDatabase.ImportAsset(l.content, ImportAssetOptions.ForceUpdate); Pieces308.Reload(scene); throw; }
            string contentNow = Pieces308.ShaAsset(l.content), sceneNow = Pieces308.ShaAsset(scenePath); string archived = Pieces308.Archive(name);
            return "stops revert " + scenePath + (l.state == "applying" ? " (the ledger state was 'applying': an apply that did not finish)" : "") + (drift.Count > 0 ? " [FORCED over " + drift.Count + " moved value(s)]" : "") + ": " + rows.Count + " stop(s) - point Y / feet Y / the seated nodes put back (the root and the kept children were never moved)\n  content sha " + Pieces308.Short(contentNow) + (contentNow == l.contentShaBefore ? " (= the bytes before apply)" : " (before apply " + Pieces308.Short(l.contentShaBefore) + ": another writer changed the asset in between)")
                + "\n  scene sha " + Pieces308.Short(sceneNow) + (sceneNow == l.sceneShaBefore ? " (= the bytes before apply)" : " (before apply " + Pieces308.Short(l.sceneShaBefore) + ")") + "\n  backup of the state before the revert " + backup + " | ledger archived " + archived;
        }
    }
}
