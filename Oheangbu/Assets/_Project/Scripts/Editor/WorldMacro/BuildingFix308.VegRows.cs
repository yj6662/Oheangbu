using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Dress308 = Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Named-row removals through the BuildingFix308.Veg ledger (Fix/veg-ledger.json, Fix/veg-mask.json), for callers that already know
    // WHICH placements have to go (Amneung308: the 14 trees under the 금표 암릉 band, D308-16c). The Veg plan / apply pair is not used:
    // a plan re-tests every building of the world and would mix those rows into this op. What is shared with Veg:
    //   - the same row / op shapes and the same ledger and mask files, so "Veg replay" brings these removals back after a re-bake and
    //     "Veg status" lists the op; "Veg revert" (newest op first) also undoes it while it is the newest applied op;
    //   - the same guards: protected sheets never written, a dirty sheet refuses, a byte backup of each sheet before the write
    //     (BuildingAudit308/Backup/Sheets/<utc>-veg-<tag>/), SaveAssetIfDirty on the edited sheets only, never SaveAssets.
    // A row is found by Id + position (tolerance from the caller's data); a row that is not there is "already" (idempotent).
    // The sheets are shared by the three target scenes: one apply serves all of them. No scene is opened or saved.
    public static partial class BuildingFix308
    {
        internal sealed class VegRowAsk308 { public string sheet = "", id = "", proto = ""; public Vector3 at; }

        static string VegRowsCommand(string tag) => "rows:" + tag;

        static string VegRowsGuard(string path, BuildingAudit308.Config308 cfg)
        {
            if (cfg.ProtectedAsset(path) || PostLedger308.IsProtectedPath(path) || Harness303.IsProtected(path)) return "protected sheet " + path;
            return null;
        }

        /// <summary>Read only: where each asked row stands in its sheet.</summary>
        internal static string VegRowsPlan(string tag, List<VegRowAsk308> asks, float tol)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var sb = new StringBuilder("BuildingFix308.VegRows plan " + tag + " (nothing is changed)\n");
            foreach (var g in asks.GroupBy(a => a.sheet))
            {
                string bad = VegRowsGuard(g.Key, cfg); if (bad != null) return "refused: " + bad;
                var sheet = AssetDatabase.LoadAssetAtPath<Dress308>(g.Key); if (sheet == null) return "refused: no sheet " + g.Key;
                var fps = sheet.FixedPlacements ?? Array.Empty<Dress308.FixedPlacement>(); int found = 0, gone = 0, twice = 0;
                foreach (var a in g)
                {
                    int n = fps.Count(p => VegAt(p, a.id, a.at, tol));
                    if (n == 1) found++; else if (n == 0) gone++; else twice++;
                    sb.AppendLine("  " + (n == 1 ? "remove " : n == 0 ? "already" : "AMBIGUOUS x" + n) + " " + a.id + " (" + a.proto + ") at " + BuildingAudit308.V(a.at));
                }
                sb.AppendLine("  " + g.Key + ": " + fps.Length + " placements, sha " + BuildingAudit308.Sha(BuildingAudit308.Abs(g.Key)).Substring(0, 12) + " -> to remove " + found + ", already gone " + gone + (twice > 0 ? ", AMBIGUOUS " + twice + " (apply refuses)" : "") + (EditorUtility.IsDirty(sheet) ? " [the sheet has unsaved changes: apply refuses]" : ""));
            }
            var op = VegLoadLedger().ops.LastOrDefault(o => o.command == VegRowsCommand(tag) && o.status == "applied" && !o.reverted);
            sb.AppendLine("  ledger " + VegLedgerFile + ": " + (op == null ? "no applied op of this tag" : "applied " + op.utc + " (" + op.detail + ")"));
            return sb.ToString();
        }

        internal static string VegRowsApply(string tag, List<VegRowAsk308> asks, float tol)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var v = VegLoadConfig(out err); if (v == null) return "refused: " + err;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only (Play is running)";
            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "refused: scripts are compiling or failed to compile";
            // ---- verify everything first: nothing is changed unless every sheet passes
            var work = new List<(string path, Dress308 sheet, List<Dress308.FixedPlacement> keep, VegSheetOp308 op)>();
            foreach (var g in asks.GroupBy(a => a.sheet).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                string bad = VegRowsGuard(g.Key, cfg); if (bad != null) return "refused: " + bad + "; nothing changed";
                var sheet = AssetDatabase.LoadAssetAtPath<Dress308>(g.Key); if (sheet == null) return "refused: no sheet " + g.Key + "; nothing changed";
                if (EditorUtility.IsDirty(sheet)) return "refused: " + g.Key + " has unsaved changes in memory (another session?); nothing changed";
                var fps = (sheet.FixedPlacements ?? Array.Empty<Dress308.FixedPlacement>()).ToList(); var drop = new HashSet<int>();
                var op = new VegSheetOp308 { path = g.Key, shaBefore = BuildingAudit308.Sha(BuildingAudit308.Abs(g.Key)) };
                foreach (var a in g)
                {
                    var hits = Enumerable.Range(0, fps.Count).Where(i => VegAt(fps[i], a.id, a.at, tol)).ToList();
                    if (hits.Count > 1) return "refused: " + hits.Count + " placements match " + a.id + " at " + BuildingAudit308.V(a.at) + " in " + g.Key + "; nothing changed";
                    var row = new VegRow308 { sheet = g.Key, id = a.id, proto = a.proto, kind = "Tree", action = "remove", reason = tag, from = a.at, index = -1 };
                    if (hits.Count == 1 && drop.Add(hits[0])) { row.index = hits[0]; row.from = fps[hits[0]].Position; row.state = "applied"; row.placementJson = JsonUtility.ToJson(fps[hits[0]]); op.removed++; }
                    else { row.state = "already"; op.already++; }
                    op.rows.Add(row);
                }
                var keep = new List<Dress308.FixedPlacement>(fps.Count);
                for (int i = 0; i < fps.Count; i++) if (!drop.Contains(i)) keep.Add(fps[i]);
                work.Add((g.Key, sheet, keep, op));
            }
            string utc = BuildingAudit308.Utc();
            if (work.All(w => w.op.removed == 0))
                return "already: veg " + VegRowsCommand(tag) + " — no change (" + work.Sum(w => w.op.already) + " row(s) already gone); no sheet saved, no ledger row";
            string backupDir = Path.Combine(BuildingAudit308.Folder, "Backup", "Sheets", utc + "-veg-" + tag); Directory.CreateDirectory(backupDir);
            var vop = new VegOp308 { utc = utc, command = VegRowsCommand(tag), planUtc = "" }; var shaLines = new StringBuilder();
            // journal first (review 2, finding 6): the op with every row (index + placement json) is in the ledger as "applying" before a
            // sheet is saved; VegRowsRevert also takes an "applying" op, so a crash between save and ledger can still be undone.
            var ledger = VegLoadLedger();
            foreach (var w in work) { if (w.op.removed == 0) w.op.shaAfter = w.op.shaBefore; vop.sheets.Add(w.op); }
            vop.status = "applying"; vop.detail = "removing " + work.Sum(w => w.op.removed) + ", already " + work.Sum(w => w.op.already) + " (" + tag + ")";
            ledger.ops.Add(vop); VegWrite(VegLedgerFile, ledger);
            foreach (var w in work)
            {
                if (w.op.removed == 0) continue;
                string file = BuildingAudit308.Abs(w.path), backup = Path.Combine(backupDir, Path.GetFileName(file));
                File.Copy(file, backup, false); if (File.Exists(file + ".meta")) File.Copy(file + ".meta", backup + ".meta", false);
                shaLines.Append(w.op.shaBefore).Append("  ").Append(Path.GetFileName(file)).Append('\n');
                w.sheet.FixedPlacements = w.keep.ToArray();
                EditorUtility.SetDirty(w.sheet); AssetDatabase.SaveAssetIfDirty(w.sheet);
                w.op.backup = backup; w.op.shaAfter = BuildingAudit308.Sha(file);
            }
            File.WriteAllText(Path.Combine(backupDir, "sha256.txt"), shaLines.ToString(), new UTF8Encoding(false));
            foreach (var a in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (a.Sheet != null && work.Any(w => w.op.removed > 0 && w.sheet == a.Sheet)) a.Invalidate();
            vop.status = "applied"; vop.detail = "removed " + work.Sum(w => w.op.removed) + ", already " + work.Sum(w => w.op.already) + " (" + tag + ")";
            VegWrite(VegLedgerFile, ledger);
            VegRowsMask(v, ledger);
            var sb = new StringBuilder("applied: veg " + vop.command + " — " + vop.detail + "\n");
            foreach (var s in vop.sheets) sb.AppendLine("  " + s.path + ": removed " + s.removed + " already " + s.already + ", sha " + s.shaBefore.Substring(0, 12) + " -> " + s.shaAfter.Substring(0, 12) + (s.backup.Length > 0 ? ", backup " + s.backup : " (unchanged)"));
            sb.AppendLine("-> " + VegLedgerFile); sb.AppendLine("-> " + VegMaskFile);
            return sb.ToString();
        }

        // the mask keeps the footprint areas of the newest Veg plan; only the decision list is rebuilt from the ledger
        static void VegRowsMask(VegCfg308 v, VegLedger308 ledger)
        {
            VegMask308 old = File.Exists(VegMaskFile) ? JsonUtility.FromJson<VegMask308>(File.ReadAllText(VegMaskFile)) : null;
            VegWriteMask(v, ledger, old != null ? old.areas : new List<VegArea308>(), old != null ? old.areaSheet : "", old != null ? old.scene : "");
        }

        /// <summary>Puts the rows of the newest applied op of this tag back at their old index (any later Veg op stays as it is).</summary>
        internal static string VegRowsRevert(string tag, float tol)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var v = VegLoadConfig(out err); if (v == null) return "refused: " + err;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only (Play is running)";
            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "refused: scripts are compiling or failed to compile";
            var ledger = VegLoadLedger();
            var op = ledger.ops.LastOrDefault(o => o.command == VegRowsCommand(tag) && (o.status == "applied" || o.status == "applying") && !o.reverted);
            if (op == null) return "refused: no applied op '" + VegRowsCommand(tag) + "' in " + VegLedgerFile + "; nothing changed";
            var changed = op.sheets.Where(s => s.shaAfter != s.shaBefore).ToList();
            foreach (var s in changed)
            {
                string bad = VegRowsGuard(s.path, cfg); if (bad != null) return "refused: " + bad;
                var sheet = AssetDatabase.LoadAssetAtPath<Dress308>(s.path); if (sheet == null) return "refused: no sheet " + s.path + "; nothing changed";
                if (EditorUtility.IsDirty(sheet)) return "refused: " + s.path + " has unsaved changes in memory; nothing changed";
            }
            string utc = BuildingAudit308.Utc(); string backupDir = Path.Combine(BuildingAudit308.Folder, "Backup", "Sheets", utc + "-veg-" + tag + "-revert"); Directory.CreateDirectory(backupDir);
            var sb = new StringBuilder();
            foreach (var s in changed)
            {
                var sheet = AssetDatabase.LoadAssetAtPath<Dress308>(s.path); string file = BuildingAudit308.Abs(s.path); string before = BuildingAudit308.Sha(file);
                File.Copy(file, Path.Combine(backupDir, Path.GetFileName(file)), false);
                var fps = (sheet.FixedPlacements ?? Array.Empty<Dress308.FixedPlacement>()).ToList(); int inserted = 0, there = 0, missing = 0;
                foreach (var r in s.rows.Where(r => r.state == "applied" && r.action == "remove").OrderBy(r => r.index))
                {
                    if (fps.Any(q => VegAt(q, r.id, r.from, tol))) { there++; continue; }
                    var p = string.IsNullOrEmpty(r.placementJson) ? null : JsonUtility.FromJson<Dress308.FixedPlacement>(r.placementJson);
                    if (p == null) { missing++; continue; }
                    fps.Insert(Mathf.Clamp(r.index, 0, fps.Count), p); inserted++;
                }
                sheet.FixedPlacements = fps.ToArray();
                EditorUtility.SetDirty(sheet); AssetDatabase.SaveAssetIfDirty(sheet);
                string after = BuildingAudit308.Sha(file);
                sb.AppendLine("  " + s.path + ": re-inserted " + inserted + (there > 0 ? ", already back " + there : "") + (missing > 0 ? ", NOT RESTORABLE " + missing : "") + ", sha " + before.Substring(0, 12) + " -> " + after.Substring(0, 12)
                    + (after == s.shaBefore ? " (= the SHA before the apply)" : " (differs from the SHA before the apply " + s.shaBefore.Substring(0, 12) + ": other edits were made since)"));
                foreach (var a in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (a.Sheet == sheet) a.Invalidate();
            }
            op.reverted = true; op.revertedUtc = utc;
            ledger.ops.Add(new VegOp308 { utc = utc, command = "revert", status = "reverted", detail = "op " + op.utc + " (" + op.command + ")", planUtc = op.planUtc });
            VegWrite(VegLedgerFile, ledger);
            VegRowsMask(v, ledger);
            return "reverted: veg op " + op.utc + " (" + op.command + ")\n" + sb + "  backup of the state before this revert: " + backupDir + "\n-> " + VegLedgerFile;
        }

        /// <summary>How many of the asked rows are still in their sheets (read only; -1 = a sheet is missing).</summary>
        internal static int VegRowsLeft(List<VegRowAsk308> asks, float tol)
        {
            int left = 0;
            foreach (var g in asks.GroupBy(a => a.sheet))
            {
                var sheet = AssetDatabase.LoadAssetAtPath<Dress308>(g.Key); if (sheet == null) return -1;
                var fps = sheet.FixedPlacements ?? Array.Empty<Dress308.FixedPlacement>();
                left += g.Count(a => fps.Any(p => VegAt(p, a.id, a.at, tol)));
            }
            return left;
        }
    }
}
