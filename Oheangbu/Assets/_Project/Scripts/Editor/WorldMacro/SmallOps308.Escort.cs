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
    // (a) escort start (D308-9e). Escort303Fix move-start moves the stop root by the #292 village shift using the CONTENT Y, and the
    // content point lies under the terrain there (0.39 m, offline expectation small_1b/escort308.json): the nodes end up in the ground.
    // This op does the whole grounding in one ledger per scene:
    //   1. the scene's own content asset: Y of the point and of the checkpoint (respawn) feet := the measured ground under them
    //   2. the stop root moves so Interaction stands on the grounded point (XZ move = the village shift, or none when already moved)
    //   3. every child node keeps its XZ and takes the Y of its own ground (physics ray on the scene colliders)
    // The Checkpoint node and the checkpoint feet get the same measured Y, Interaction and the point too, so the escort audit A1
    // (3D distance <= 0.01) holds. Never 03_Content.asset: the content path comes from the data and must be the scene session's own.
    public static partial class SmallOps308
    {
        [Serializable] sealed class EscortNode { public string node = ""; public Vector3 localBefore, worldAfter; public float ground; public string support = ""; }
        [Serializable] sealed class EscortLedger
        {
            public string format = "cb308.small.escort.1", scene = "", key = "", state = "none", utc = "", configSha256 = "", content = "", contentShaBefore = "", contentShaAfter = "", sceneShaBefore = "", sceneShaAfter = "", backup = "";
            public float pointYBefore, pointYAfter, feetYBefore, feetYAfter; public int pointYBits, feetYBits;
            public Vector3 rootLocalBefore, rootWorldAfter;
            public List<EscortNode> nodes = new List<EscortNode>(); public string[] history = new string[0];
        }

        sealed class EscortPlan
        {
            public Scene Scene; public Transform Root; public WorldMacroPlaytestSO Content; public string ContentPath;
            public PrologueContentSO.Point Point; public WorldMacroPlaytestSO.CheckpointSpec Check;
            public Vector3 RootTarget; public float PointY, FeetY; public bool Change;
            public readonly List<(Transform t, Vector3 target, RaycastHit hit)> Nodes = new List<(Transform, Vector3, RaycastHit)>();
            public readonly List<string> Lines = new List<string>(), Refuse = new List<string>();
        }

        static EscortPlan EscortMeasure(JObject data, string scenePath, string key, bool write)
        {
            var e = Pieces308.Req(data, "escort"); var k = new EscortPlan();
            k.ContentPath = Pieces308.Str(Pieces308.Req(e, "content"), key);
            if (PostLedger308.IsProtectedPath(k.ContentPath)) throw new PostLedger308.Refused("protected content path " + k.ContentPath);
            k.Scene = Pieces308.OpenTarget(scenePath, data["preview_roots"], write);
            var session = CliffCore308.Session(k.Scene); if (session == null) throw new PostLedger308.Refused("no playtest session in " + scenePath);
            k.Content = session.Content;
            if (k.Content == null || AssetDatabase.GetAssetPath(k.Content) != k.ContentPath) throw new PostLedger308.Refused("the session of " + scenePath + " uses " + (k.Content == null ? "no content" : AssetDatabase.GetAssetPath(k.Content)) + ", the data names " + k.ContentPath);
            k.Root = Pieces308.FindOne(k.Scene, Pieces308.Str(e, "stop"), "escort stop");
            if (PostLedger308.UnderProtectedTree(k.Root)) throw new PostLedger308.Refused("the escort stop is under a protected tree");
            var interaction = k.Root.Find("Interaction"); var checkpoint = k.Root.Find("Checkpoint");
            if (interaction == null || checkpoint == null) throw new PostLedger308.Refused("the stop has no Interaction / Checkpoint child");
            k.Point = (k.Content.Points ?? Array.Empty<PrologueContentSO.Point>()).FirstOrDefault(x => x != null && x.Id == Pieces308.Str(e, "point"));
            k.Check = (k.Content.Checkpoints ?? Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>()).FirstOrDefault(x => x != null && x.Id == Pieces308.Str(e, "checkpoint"));
            if (k.Point == null || k.Check == null) throw new PostLedger308.Refused("the content has no point / checkpoint " + Pieces308.Str(e, "point"));

            float poseTol = Pieces308.Num(e, "pose_tol_m"), seatTol = Pieces308.Num(e, "seat_tol_m"), window = Pieces308.Num(e, "support_window_m"), expectTol = Pieces308.Num(e, "expect_tol_m");
            var shift = Pieces308.V3(Pieces308.Req(e, "village_shift"));
            var move = new Vector2(k.Point.Position.x - interaction.position.x, k.Point.Position.z - interaction.position.z);
            bool moved = move.magnitude <= poseTol, village = Vector2.Distance(move, new Vector2(shift.x, shift.z)) <= Pieces308.Num(e, "shift_xz_tol_m");
            k.Lines.Add("root " + Pieces308.V(k.Root.position) + ", Interaction " + Pieces308.V(interaction.position) + ", content point " + Pieces308.V(k.Point.Position) + ", checkpoint feet " + Pieces308.V(k.Check.Feet));
            k.Lines.Add("XZ move of the root: (" + Pieces308.F(move.x, "F3") + ", " + Pieces308.F(move.y, "F3") + ") = " + (moved ? "none (already on the point)" : village ? "the #292 village shift" : "NEITHER none nor the village shift"));
            if (!moved && !village) k.Refuse.Add("the root is neither on the content point nor one village shift (" + Pieces308.F(shift.x) + ", " + Pieces308.F(shift.z) + ") away from it: this op never moves the stop anywhere else");

            // expectation of the offline run (small_1b/escort308.json), when it is there
            var expect = new Dictionary<string, float>();
            string expFile = PostLedger308.RepoPath(Pieces308.DataDir + "/escort308.json");
            if (File.Exists(expFile))
                foreach (var s in Pieces308.Arr(JObject.Parse(File.ReadAllText(expFile)), "scenes")) if (Pieces308.Opt(s, "scene") == key) foreach (var n in Pieces308.Arr(s, "nodes")) expect[Pieces308.Str(n, "node")] = Pieces308.Num(n, "new_y");

            var rule = new Pieces308.GroundRule(k.Scene, k.Root);
            foreach (Transform n in k.Root)
            {
                var target = new Vector3(n.position.x + move.x, 0f, n.position.z + move.y);
                if (!Pieces308.Ground(rule, target.x, target.z, window, out var hit)) { k.Refuse.Add("no ground under node " + n.name + " at (" + Pieces308.F(target.x) + ", " + Pieces308.F(target.z) + ")"); continue; }
                target.y = hit.point.y; k.Nodes.Add((n, target, hit));
                string exp = expect.TryGetValue(n.name, out float ey) ? ", height field " + Pieces308.F(ey, "F3") : " (no offline expectation: run small308.py escort)";
                k.Lines.Add("  node " + n.name + ": " + Pieces308.V(n.position) + " -> " + Pieces308.V(target) + " on " + PostLedger308.PathOf(hit.collider.transform) + exp);
                // a prop top (a cart, a crate, a deck) within the support window would pass as ground: the height field is the witness
                if (expect.ContainsKey(n.name) && Mathf.Abs(ey - hit.point.y) > expectTol)
                    k.Refuse.Add("node " + n.name + " would be seated on " + PostLedger308.PathOf(hit.collider.transform) + " at " + Pieces308.F(hit.point.y, "F3") + ", " + Pieces308.F(hit.point.y - ey, "F3") + " m off the height field " + Pieces308.F(ey, "F3")
                        + " (escort.expect_tol_m " + Pieces308.F(expectTol) + "): check what stands there; raise the tolerance in the data only if that support is the village ground");
            }
            var gi = k.Nodes.FirstOrDefault(x => x.t == interaction); var gc = k.Nodes.FirstOrDefault(x => x.t == checkpoint);
            if (gi.t == null || gc.t == null) return k;
            if (Vector2.Distance(new Vector2(gc.target.x, gc.target.z), new Vector2(k.Check.Feet.x, k.Check.Feet.z)) > poseTol) k.Refuse.Add("the Checkpoint node would stand at (" + Pieces308.F(gc.target.x) + ", " + Pieces308.F(gc.target.z) + "), the checkpoint feet are at (" + Pieces308.F(k.Check.Feet.x) + ", " + Pieces308.F(k.Check.Feet.z) + ")");
            k.PointY = gi.target.y; k.FeetY = gc.target.y;
            k.RootTarget = new Vector3(k.Root.position.x + move.x, gi.target.y - (interaction.position.y - k.Root.position.y), k.Root.position.z + move.y);
            float dy = k.RootTarget.y - k.Root.position.y, yWindow = Pieces308.Num(e, "shift_y_window_m");
            if (Mathf.Abs(dy - (moved ? 0f : shift.y)) > yWindow) k.Refuse.Add("the root would move " + Pieces308.F(dy, "F3") + " m in Y, more than " + Pieces308.F(yWindow) + " m off " + (moved ? "its place" : "the village shift " + Pieces308.F(shift.y, "F3")));
            k.Change = Mathf.Abs(k.Point.Position.y - k.PointY) > seatTol || Mathf.Abs(k.Check.Feet.y - k.FeetY) > seatTol || Vector3.Distance(k.Root.position, k.RootTarget) > poseTol && !moved
                       || k.Nodes.Any(x => Vector3.Distance(x.t.position, x.target) > seatTol);
            k.Lines.Add("content " + k.ContentPath + ": point Y " + Pieces308.F(k.Point.Position.y, "F3") + " -> " + Pieces308.F(k.PointY, "F3") + " (" + Pieces308.F(k.PointY - k.Point.Position.y, "F3") + "), checkpoint feet Y " + Pieces308.F(k.Check.Feet.y, "F3") + " -> " + Pieces308.F(k.FeetY, "F3") + " (" + Pieces308.F(k.FeetY - k.Check.Feet.y, "F3") + ")");
            k.Lines.Add("root -> " + Pieces308.V(k.RootTarget) + " (" + Pieces308.F(Vector3.Distance(k.Root.position, k.RootTarget), "F3") + " m)");
            return k;
        }

        static string Escort(JObject data, string dataSha, string scenePath, string key, string verb)
        {
            string name = LedgerName("escort", key);
            if (verb == "revert") return EscortRevert(data, scenePath, key, name);
            if (verb == "verify") return EscortVerify(data, scenePath, key, name);
            bool dry = verb == "plan";
            var k = EscortMeasure(data, scenePath, key, !dry);
            var sb = new StringBuilder("escort " + verb + " " + scenePath + "\n"); foreach (string line in k.Lines) sb.AppendLine("  " + line);
            foreach (string r in k.Refuse) sb.AppendLine("  REFUSE " + r);
            if (!k.Change && k.Refuse.Count == 0) return sb.Append("  변경 없음 (point, feet and nodes stand on their ground; nothing written, nothing saved)").ToString();
            if (dry) return sb.Append(k.Refuse.Count == 0 ? "  apply would write the content asset and the scene; nothing changed" : "  apply would be REFUSED; nothing changed").ToString();
            if (k.Refuse.Count > 0) throw new PostLedger308.Refused(k.Refuse[0]);
            if (EditorUtility.IsDirty(k.Content)) throw new PostLedger308.Refused(k.ContentPath + " has unsaved in-memory changes (another session?)");

            string utc = PostLedger308.Utc(); string backup = Pieces308.Backup(utc + "-escort-" + key, scenePath, k.ContentPath);
            var prior = Pieces308.ReadLedger<EscortLedger>(name);
            // a second apply after a ground change keeps the FIRST before-values (the revert goes back to the state before #308)
            var l = prior != null && prior.state == "applied" ? prior : new EscortLedger
            {
                scene = scenePath, key = key, content = k.ContentPath, contentShaBefore = Pieces308.ShaAsset(k.ContentPath), sceneShaBefore = Pieces308.ShaAsset(scenePath), backup = backup,
                pointYBefore = k.Point.Position.y, pointYBits = Bits(k.Point.Position.y), feetYBefore = k.Check.Feet.y, feetYBits = Bits(k.Check.Feet.y), rootLocalBefore = k.Root.localPosition,
                nodes = k.Nodes.Select(x => new EscortNode { node = x.t.name, localBefore = x.t.localPosition }).ToList(),
            };
            string contentAbs = PostLedger308.Abs(k.ContentPath); var contentBytes = File.ReadAllBytes(contentAbs);
            try
            {
                k.Point.Position = new Vector3(k.Point.Position.x, k.PointY, k.Point.Position.z); k.Check.Feet = new Vector3(k.Check.Feet.x, k.FeetY, k.Check.Feet.z);
                EditorUtility.SetDirty(k.Content); AssetDatabase.SaveAssetIfDirty(k.Content);
                k.Root.position = k.RootTarget;
                foreach (var (t, target, hit) in k.Nodes)
                {
                    t.position = target;
                    var row = l.nodes.FirstOrDefault(x => x.node == t.name); if (row == null) { row = new EscortNode { node = t.name, localBefore = t.localPosition }; l.nodes.Add(row); }
                    row.worldAfter = target; row.ground = hit.point.y; row.support = PostLedger308.PathOf(hit.collider.transform);
                }
                Physics.SyncTransforms();
                Pieces308.SaveOrReload(k.Scene);
            }
            catch
            {
                // the content asset must not stay ahead of a scene that was not saved: put its bytes back
                File.WriteAllBytes(contentAbs, contentBytes); AssetDatabase.ImportAsset(k.ContentPath, ImportAssetOptions.ForceUpdate);
                Pieces308.Reload(k.Scene);   // unconditional: transform edits by script do not mark the scene dirty
                throw;
            }
            l.state = "applied"; l.utc = utc; l.configSha256 = dataSha; l.pointYAfter = k.PointY; l.feetYAfter = k.FeetY; l.rootWorldAfter = k.RootTarget;
            l.contentShaAfter = Pieces308.ShaAsset(k.ContentPath); l.sceneShaAfter = Pieces308.ShaAsset(scenePath); l.history = Add(l.history, "apply (backup " + backup + ")");
            Pieces308.WriteLedger(name, l);
            sb.AppendLine("  wrote " + k.ContentPath + " sha " + Pieces308.Short(l.contentShaBefore) + " -> " + Pieces308.Short(l.contentShaAfter) + " and the scene " + Pieces308.Short(l.sceneShaBefore) + " -> " + Pieces308.Short(l.sceneShaAfter));
            sb.Append("  backup " + backup + " | ledger " + Pieces308.OutFile(name) + " | next: escort:verify:" + key + " (Content308 content-revert of this scene now needs this op reverted first)");
            return sb.ToString();
        }

        static string EscortVerify(JObject data, string scenePath, string key, string name)
        {
            var e = Pieces308.Req(data, "escort"); var k = EscortMeasure(data, scenePath, key, false);
            var sb = new StringBuilder("escort verify " + scenePath + "\n"); int fail = 0, wait = 0;
            void Row(bool ok, string text) { if (!ok) fail++; sb.AppendLine("  " + (ok ? "PASS " : "FAIL ") + text); }
            float seatTol = Pieces308.Num(e, "seat_tol_m"), audit = Pieces308.Num(e, "audit_support_tol_m"), nav = Pieces308.Num(e, "nav_radius_m");
            var interaction = k.Root.Find("Interaction"); var checkpoint = k.Root.Find("Checkpoint");
            Row(Vector3.Distance(interaction.position, k.Point.Position) <= .01f, "A1 escort_start: Interaction " + Pieces308.V(interaction.position) + " on the content point " + Pieces308.V(k.Point.Position) + " (" + Pieces308.F(Vector3.Distance(interaction.position, k.Point.Position), "F3") + " m, rule 0.01)");
            Row(Vector3.Distance(checkpoint.position, k.Check.Feet) <= .01f, "A1 escort_start: Checkpoint " + Pieces308.V(checkpoint.position) + " on the checkpoint feet " + Pieces308.V(k.Check.Feet) + " (" + Pieces308.F(Vector3.Distance(checkpoint.position, k.Check.Feet), "F3") + " m, rule 0.01)");
            var session = CliffCore308.Session(k.Scene); var place = session.MountainLayout != null ? session.MountainLayout.Places.FirstOrDefault(p => p.Id == "escort_departure") : null;
            if (place != null) Row(Vector2.Distance(new Vector2(interaction.position.x, interaction.position.z), place.XZ) < 12f, "A1 escort_departure: layout place (" + Pieces308.F(place.XZ.x) + ", " + Pieces308.F(place.XZ.y) + ") within 12 m of the stop");
            var walk = new HashSet<string>(Pieces308.Arr(e, "walk_nodes").Select(t => t.Value<string>()));
            foreach (var (t, target, hit) in k.Nodes)
            {
                float gap = t.position.y - hit.point.y;
                bool support = Physics.RaycastAll(t.position + Vector3.up * .5f, Vector3.down, .8f, ~0, QueryTriggerInteraction.Ignore).Any(h => !(h.collider is CharacterController) && Mathf.Abs(h.point.y - t.position.y) <= audit);
                Row(Mathf.Abs(gap) <= seatTol && support, "node " + t.name + " " + Pieces308.V(t.position) + ": ground " + Pieces308.F(hit.point.y, "F3") + " (gap " + Pieces308.F(gap, "F3") + ", rule " + Pieces308.F(seatTol) + "), audit A2 support " + support);
                if (!walk.Contains(t.name)) continue;
                bool onNav = NavMesh.SamplePosition(t.position, out var nh, nav, NavMesh.AllAreas) && Vector3.Distance(nh.position, t.position) <= nav;
                if (onNav) sb.AppendLine("  PASS node " + t.name + ": NavMesh within " + Pieces308.F(nav, "F1") + " m");
                else { wait++; sb.AppendLine("  WAIT node " + t.name + ": no NavMesh within " + Pieces308.F(nav, "F1") + " m (expected until the shared 1b bake; audit A2 stays red till then)"); }
            }
            var l = Pieces308.ReadLedger<EscortLedger>(name);
            sb.AppendLine("  INFO ledger " + (l == null ? "none" : l.state + " " + l.utc + ", content sha now " + Pieces308.Short(Pieces308.ShaAsset(k.ContentPath)) + (l.contentShaAfter == Pieces308.ShaAsset(k.ContentPath) ? " (= this ledger's write)" : " (changed after this ledger)")));
            sb.AppendLine("  INFO not this op (stay red in `Escort303Regression audit`): A1 / A2 of checkpoint_1, checkpoint_2, cargo_delivery; A3-A6 need the NavMesh bake");
            if (k.Scene.isDirty) { fail++; sb.AppendLine("  FAIL the scene is dirty after a read-only command (bug): reload it"); }
            sb.Insert(0, (fail == 0 ? "GREEN" : "RED") + " (FAIL " + fail + ", WAIT " + wait + ") ");
            return Pieces308.Report("cb308-small-escort-verify-" + key + ".txt", sb);
        }

        static string EscortRevert(JObject data, string scenePath, string key, string name)
        {
            var l = Pieces308.ReadLedger<EscortLedger>(name);
            if (l == null || l.state != "applied") throw new PostLedger308.Refused("no applied ledger " + name);
            if (PostLedger308.IsProtectedPath(l.content)) throw new PostLedger308.Refused("protected content path in the ledger");
            var e = Pieces308.Req(data, "escort"); var scene = Pieces308.OpenTarget(scenePath, data["preview_roots"], true);
            var session = CliffCore308.Session(scene); var content = session != null ? session.Content : null;
            if (content == null || AssetDatabase.GetAssetPath(content) != l.content) throw new PostLedger308.Refused("the scene session does not use " + l.content);
            if (EditorUtility.IsDirty(content)) throw new PostLedger308.Refused(l.content + " has unsaved in-memory changes");
            var root = Pieces308.FindOne(scene, Pieces308.Str(e, "stop"), "escort stop");
            var point = content.Points.FirstOrDefault(x => x != null && x.Id == Pieces308.Str(e, "point")); var check = content.Checkpoints.FirstOrDefault(x => x != null && x.Id == Pieces308.Str(e, "checkpoint"));
            if (point == null || check == null) throw new PostLedger308.Refused("the content lost the point / checkpoint");
            string utc = PostLedger308.Utc(); string backup = Pieces308.Backup(utc + "-escort-revert-" + key, scenePath, l.content);
            string abs = PostLedger308.Abs(l.content); var bytes = File.ReadAllBytes(abs);
            try
            {
                point.Position = new Vector3(point.Position.x, FromBits(l.pointYBits), point.Position.z); check.Feet = new Vector3(check.Feet.x, FromBits(l.feetYBits), check.Feet.z);
                EditorUtility.SetDirty(content); AssetDatabase.SaveAssetIfDirty(content);
                root.localPosition = l.rootLocalBefore;
                foreach (var n in l.nodes) { var t = root.Find(n.node); if (t != null) t.localPosition = n.localBefore; }
                Pieces308.SaveOrReload(scene);
            }
            catch { File.WriteAllBytes(abs, bytes); AssetDatabase.ImportAsset(l.content, ImportAssetOptions.ForceUpdate); Pieces308.Reload(scene); throw; }
            string contentNow = Pieces308.ShaAsset(l.content), sceneNow = Pieces308.ShaAsset(scenePath); string archived = Pieces308.Archive(name);
            return "escort revert " + scenePath + ": point Y / feet Y / root / " + l.nodes.Count + " nodes put back\n  content sha " + Pieces308.Short(contentNow) + (contentNow == l.contentShaBefore ? " (= the bytes before apply)" : " (before apply " + Pieces308.Short(l.contentShaBefore) + ": another writer changed the asset in between)")
                + "\n  scene sha " + Pieces308.Short(sceneNow) + (sceneNow == l.sceneShaBefore ? " (= the bytes before apply)" : " (before apply " + Pieces308.Short(l.sceneShaBefore) + ")") + "\n  backup of the state before the revert " + backup + " | ledger archived " + archived;
        }
    }
}
