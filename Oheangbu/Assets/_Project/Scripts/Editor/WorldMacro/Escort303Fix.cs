using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // F1 (plan §12): the CapitalEscort252/escort_start stop root was left at its pre-#292 place when the village moved rigidly
    // by (+60, +173.766338, -40) (a renderer-less marker; CompactReworld292 Move skips transforms without renderers). This
    // idempotent builder moves only that root so its Interaction / Checkpoint children land on the content point and
    // checkpoint again. Queue entry Run(string):
    //   plan:<main|folklore298|architecture296>                     — read-only preview of the move and the node checks
    //   move-start:<main|folklore298|architecture296>[:allow-offmesh] — back up the scene, move the root, save, verify
    //   apply-all                                                   — Architecture296 → Folklore298 → W_Demo_Main, only when
    //                                                                 EscortTest/escort-audit.json confirms the shift mismatch
    // No NavMesh bake (on hold). Nodes that do not sit on the existing NavMesh refuse the move unless :allow-offmesh is given
    // (ask the user first). Protected scenes (W_Demo_Compact.unity) and 03_Content.asset are never opened for writing.
    public static class Escort303Fix
    {
        static string Folder => Path.Combine(Harness303.Roadside303Folder, "EscortFix");
        static string BeforeFolder => Path.Combine(Harness303.Roadside303Folder, "Before");
        static readonly string[] Order = { "architecture296", "folklore298", "main" };

        static string ScenePath(string target) =>
            target == "main" ? Harness303.MainScene : target == "folklore298" ? Harness303.FolkloreScene : target == "architecture296" ? Harness303.CandidateScene : null;

        [Serializable] sealed class NodeRow { public string node = ""; public Vector3 before, after; public bool support, navmesh, walk; }
        [Serializable] sealed class Result
        {
            public string scene = "", action = "", status = "", detail = "", backup = "", utc = "", sceneShaBefore = "", sceneShaAfter = "";
            public Vector3 rootBefore, rootAfter, delta; public float interactionError, checkpointError;
            public List<NodeRow> nodes = new List<NodeRow>();
        }

        public static string Run(string command)
        {
            var a = (command ?? "").Split(':');
            switch (a[0])
            {
                case "plan": return Move(a.Length > 1 ? a[1] : "main", false, false);
                case "move-start": return Move(a.Length > 1 ? a[1] : "", true, a.Length > 2 && a[2] == "allow-offmesh");
                case "apply-all": return ApplyAll(a.Length > 1 && a[1] == "allow-offmesh");
                default: return "refused: Escort303Fix plan:<main|folklore298|architecture296> | move-start:<target>[:allow-offmesh] | apply-all";
            }
        }

        static string ApplyAll(bool allowOffmesh)
        {
            string auditFile = Path.Combine(Harness303.Roadside303Folder, "EscortTest", "escort-audit.json");
            if (!File.Exists(auditFile)) return "refused: run Escort303Regression audit:main first — apply-all only runs when the audit confirms the mismatch";
            var audit = JsonUtility.FromJson<Escort303Regression.AuditReport>(File.ReadAllText(auditFile));
            var row = audit?.rows?.FirstOrDefault(r => r.id == "A1" && r.target == "escort_start");
            if (row == null || row.status != "FAIL" || !audit.escortStartShiftMismatch) return "refused: the audit does not show the escort_start village-shift mismatch (" + (row != null ? row.status + " " + row.cause : "no row") + "); nothing applied";
            var lines = new List<string>();
            foreach (var t in Order)
            {
                string r = Move(t, true, allowOffmesh);
                lines.Add(t + ": " + r.Split('\n')[0]);
                if (r.StartsWith("refused", StringComparison.Ordinal) || r.StartsWith("FAILED", StringComparison.Ordinal)) { lines.Add("stopped at " + t); break; }
            }
            return string.Join("\n", lines);
        }

        static string Move(string target, bool apply, bool allowOffmesh)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only";
            string path = ScenePath(target);
            if (path == null) return "refused: target must be main, folklore298 or architecture296";
            if (Harness303.IsProtected(path)) return "refused: protected scene";
            var active = SceneManager.GetActiveScene();
            if (active.isDirty) return "refused: the open scene has unsaved changes";
            string previous = active.path;
            var scene = active.path == path ? active : EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var result = new Result { scene = path, action = apply ? "move-start" : "plan", utc = DateTime.UtcNow.ToString("O"), sceneShaBefore = Harness303.Sha(Harness303.Abs(path)) };
            try
            {
                var s = Harness303.Session;
                var route = Escort303Regression.Route(scene);
                var stop = Escort303Regression.Stop(route, "escort_start");
                if (s == null || route == null || stop?.Interaction == null || stop.Checkpoint == null) return Done(result, "refused", "escort_start stop or session missing", previous, scene);
                var root = stop.Interaction.parent;
                if (root == null || root.name != "escort_start" || root.parent == null || root.parent.name != Escort303Regression.RouteRoot) return Done(result, "refused", "unexpected hierarchy " + Harness303.PathOf(stop.Interaction), previous, scene);
                var point = s.Content.Points.FirstOrDefault(p => p.Id == stop.Id);
                var check = s.Content.Checkpoints.FirstOrDefault(c => c.Id == stop.CheckpointId);
                if (point == null || check == null) return Done(result, "refused", "content escort_start point or checkpoint missing", previous, scene);
                var target0 = point.Position - (stop.Interaction.position - root.position);
                var delta = target0 - root.position;
                result.rootBefore = root.position; result.delta = delta;
                var nodes = new[] { stop.Interaction, stop.Parking, stop.CompanionWait, stop.CargoWait, stop.Checkpoint, stop.StagingApproach }.Where(n => n != null).ToArray();
                if (delta.magnitude <= .01f)
                {
                    result.rootAfter = root.position; Nodes(result, nodes, Vector3.zero, stop);
                    return Done(result, "already applied", "root already at " + Harness303.V(root.position) + " (idempotent no-op; scene not saved)", previous, scene);
                }
                if (Vector3.Distance(delta, Escort303Regression.VillageShift) > .05f)
                    return Done(result, "refused", "mismatch " + Harness303.V(delta) + " is not the #292 village shift " + Harness303.V(Escort303Regression.VillageShift) + "; nothing moved", previous, scene);
                result.checkpointError = Vector3.Distance(stop.Checkpoint.position + delta, check.Feet);
                result.interactionError = Vector3.Distance(stop.Interaction.position + delta, point.Position);
                bool nodesOk = Nodes(result, nodes, delta, stop);
                if (result.checkpointError > .01f) return Done(result, "refused", "after the move the checkpoint would miss the content checkpoint by " + Harness303.F(result.checkpointError), previous, scene);
                if (!apply) { result.rootAfter = root.position + delta; return Done(result, "plan", "would move " + Harness303.V(root.position) + " → " + Harness303.V(result.rootAfter) + "; nodes " + (nodesOk ? "all supported" : "NOT all on NavMesh/support"), previous, scene); }
                if (!nodesOk && !allowOffmesh) return Done(result, "refused", "some moved nodes are not on the existing NavMesh / ground (NavMesh re-bake is on hold) — confirm with the user, then pass :allow-offmesh", previous, scene);
                // back up, move, save
                string backup = Path.Combine(BeforeFolder, Path.GetFileNameWithoutExtension(path) + "-escortfix-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(backup);
                File.Copy(Harness303.Abs(path), Path.Combine(backup, Path.GetFileName(path)), true);
                result.backup = backup;
                Undo.RecordObject(root, "Escort303Fix move escort_start");
                root.position = target0;
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) return Done(result, "FAILED", "SaveScene returned false (backup " + backup + ")", previous, scene);
                Physics.SyncTransforms();
                result.rootAfter = root.position;
                result.interactionError = Vector3.Distance(stop.Interaction.position, point.Position);
                result.checkpointError = Vector3.Distance(stop.Checkpoint.position, check.Feet);
                result.sceneShaAfter = Harness303.Sha(Harness303.Abs(path));
                bool ok = result.interactionError <= .01f && result.checkpointError <= .01f;
                return Done(result, ok ? "applied" : "FAILED", "moved " + Harness303.V(result.rootBefore) + " → " + Harness303.V(result.rootAfter) + ", interaction Δ " + Harness303.F(result.interactionError) + ", checkpoint Δ " + Harness303.F(result.checkpointError) + ", backup " + backup, previous, scene);
            }
            catch (Exception e) { return Done(result, "FAILED", e.ToString(), previous, scene); }
        }

        static bool Nodes(Result r, Transform[] nodes, Vector3 delta, DemoEscortStop stop)
        {
            bool all = true;
            foreach (var n in nodes)
            {
                var p = n.position + delta; bool walk = n != stop.Parking && n != stop.Interaction;
                bool support = Physics.RaycastAll(p + Vector3.up * .5f, Vector3.down, .8f, ~0, QueryTriggerInteraction.Ignore).Any(h => Mathf.Abs(h.point.y - p.y) <= .3f);
                bool nav = !walk || NavMesh.SamplePosition(p, out var hit, .5f, NavMesh.AllAreas) && Vector3.Distance(hit.position, p) <= .5f;
                r.nodes.Add(new NodeRow { node = n.name, before = n.position, after = p, support = support, navmesh = nav, walk = walk });
                if (n != stop.Interaction && !(support && nav)) all = false;
            }
            return all;
        }

        static string Done(Result r, string status, string detail, string previous, Scene opened)
        {
            r.status = status; r.detail = detail;
            try { Harness303.WriteJson(Folder, r.action + "-" + Path.GetFileNameWithoutExtension(r.scene) + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + ".json", r); } catch { }
            // put the editor back on the scene it had open (only when it is clean — a failed save leaves the target open)
            if (!string.IsNullOrEmpty(previous) && previous != opened.path && !opened.isDirty && File.Exists(Harness303.Abs(previous)))
                EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            string nodes = string.Join("; ", r.nodes.Select(n => n.node + " " + Harness303.V(n.after) + (n.walk ? " nav " + n.navmesh : "") + " support " + n.support));
            return (status == "refused" ? "refused: " : status == "FAILED" ? "FAILED: " : status + ": ") + r.scene + " — " + detail + (nodes.Length > 0 ? "\nnodes: " + nodes : "");
        }
    }
}
