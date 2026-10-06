using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.World;
using Sheet308 = Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
    // veg:<scene> / veg-revert:<scene>. Only the sheets the config lists for the stage are touched (1a: DryLandscape). A placement on a
    // changed part of the field is removed when the new ground there is a face (slope >= removeSlopeDeg, the plan's rule) and re-seated by
    // the field's own height difference otherwise. Forest305 is NOT handled here: Enclosure305 build-scene rebuilds it from the cliff
    // mask and would erase any edit. The sheets are shared by the three target scenes (one asset), so the ledger is per stage, not per
    // scene: the first scene's run changes the sheet, the later scenes' runs find it done. Heights come from the stage / base FIELDS
    // (deterministic), so the result does not depend on which scene is open.
    // Stage 1b: a sheet that already carries the veg run of ANOTHER stage stands on that stage's field, not on the base. A plant then moves
    // by (this stage's field - that stage's field) and is removed when this stage's ground under it is a face; the other stage's ledger
    // is marked `superseded` (veg-revert of this stage puts it back to `applied`). Without that, the stage-1b run would lift every plant
    // of the stage-1a run a second time by the full raise.
    public static partial class CliffBoundary308
    {
        // shaBefore / backup = the restore point: the sheet file before the first veg run of the CURRENT series. A series ends with veg-revert
        // or when another tool edits the sheet after veg (then the next veg run takes a new restore point and says so in `earlier`:
        // restoring the older backup would drop that tool's edit). *AtBackup = the counters at the restore point, to which veg-revert returns.
        // pending* = the journal of one sheet write: the ledger is written BEFORE the sheet is saved (pendingFrom = the file sha at that moment,
        // pending counters = the values before the write) and again after it. A run that died in between is settled by the next command from
        // the file itself (SettlePending), so a plant is never lifted twice and a save that never happened is never counted.
        [Serializable] sealed class VegSheet
        {
            public string sheet, guid, shaBefore, shaAfter, backup, removedFile, earlier, pendingFrom; public int total, removed, reseated, removedAtBackup, reseatedAtBackup, keysAtBackup, pendingRemoved, pendingReseated, pendingKeys;
            public float maxLift; public string[] reseatedKeys;
        }

        /// <summary>Settles a journal entry left by a veg run that stopped between "ledger written" and "sheet saved + ledger written".</summary>
        static string SettlePending(VegSheet rec, string shaNow)
        {
            if (string.IsNullOrEmpty(rec.pendingFrom)) return "";
            string said;
            if (shaNow == rec.pendingFrom)
            {
                // the sheet was never saved: forget what the journal planned
                rec.removed = rec.pendingRemoved; rec.reseated = rec.pendingReseated; rec.reseatedKeys = (rec.reseatedKeys ?? new string[0]).Take(rec.pendingKeys).ToArray();
                said = ", an interrupted veg run had not saved this sheet (journal dropped)";
            }
            else { rec.shaAfter = shaNow; said = ", an interrupted veg run had saved this sheet (journal accepted, file sha " + Short(shaNow) + ")"; }
            rec.pendingFrom = "";
            return said;
        }
        [Serializable] sealed class VegLedger
        {
            public string format = "cb308.veg.1", stage, state = "none", utc, heightSha256, baseSha256; public float removeSlopeDeg; public string[] scenes = new string[0]; public VegSheet[] sheets = new VegSheet[0];
            // stage 1b: the stage the sheets stood on when this run moved them ("" = the base), and the stage that took over from this run
            public string fromStage = "", fromHeightSha256 = "", supersededBy = "";
        }

        /// <summary>The applied veg run of another stage: the field the shared sheets stand on now. null = they stand on the base.</summary>
        static VegLedger StandingVeg(CliffCore308.Config cfg, string stageId)
        {
            VegLedger best = null;
            foreach (string other in BuiltStages(cfg))
            {
                if (other == stageId) continue;
                var l = ReadVeg(cfg, other);
                if (l.state != "applied" && l.state != "applying") continue;
                if (best == null || string.CompareOrdinal(l.utc ?? "", best.utc ?? "") > 0) best = l;
            }
            return best;
        }
        [Serializable] sealed class RemovedFile { public string sheet; public int[] index; public Sheet308.FixedPlacement[] placements; }

        static string VegLedgerFile(CliffCore308.Config cfg, string stage) => CliffCore308.OutFile(cfg, "cb308-veg-" + stage + ".json");

        static VegLedger ReadVeg(CliffCore308.Config cfg, string stage)
        {
            string file = VegLedgerFile(cfg, stage);
            if (!File.Exists(file)) return new VegLedger { stage = stage };
            var l = JsonUtility.FromJson<VegLedger>(File.ReadAllText(file, Encoding.UTF8));
            if (l.sheets == null) l.sheets = new VegSheet[0]; if (l.scenes == null) l.scenes = new string[0];
            return l;
        }

        static string Key(Sheet308.FixedPlacement p) => (p.Id ?? "") + "|" + (p.PrototypeId ?? "") + "|" + p.Position.x.ToString("F3", Inv) + "|" + p.Position.z.ToString("F3", Inv);

        static string Veg(CliffCore308.Config cfg, string token, Dictionary<string, string> opt, bool dry)
        {
            string path = CliffCore308.ScenePath(cfg, token);
            PostLedger308.RequireEditable();
            var ledger = ReadLedger(cfg, path);
            if (ledger.state != "applied" || ledger.stage == "") throw new PostLedger308.Refused("tiles are not applied in " + path + " (run tiles:" + ledger.key + " first; plants are re-seated for the stage the scene stands on)");
            string stageId = ledger.stage;
            var spec = (cfg.veg ?? new CliffCore308.VegSpec[0]).FirstOrDefault(v => v.stage == stageId);
            if (spec == null || spec.sheets == null || spec.sheets.Length == 0) return "veg " + stageId + ": the config lists no sheet for this stage - no-op";
            var stage = CliffCore308.LoadStage(cfg, stageId, !dry);
            if (stage.HeightSha != ledger.heightSha256) throw new PostLedger308.Refused("the stage field on disk (sha " + stage.HeightSha.Substring(0, 12) + ") is not the one " + ledger.key + " was applied from (" + Short(ledger.heightSha256) + ")");
            var veg = ReadVeg(cfg, stageId);
            // a run another stage took over from is history: this stage starts a new series (new restore point, no re-seat keys)
            if (veg.state == "superseded") veg = new VegLedger { stage = stageId, scenes = veg.scenes ?? new string[0] };
            if ((veg.state == "applied" || veg.state == "applying") && veg.heightSha256 != "" && veg.heightSha256 != stage.HeightSha) throw new PostLedger308.Refused("veg was applied for another field of stage " + stageId + " (sha " + Short(veg.heightSha256) + "); run veg-revert first");
            // the field the plants stand on now: the applied veg run of another stage, else the base
            var standing = veg.state == "applied" || veg.state == "applying" ? null : StandingVeg(cfg, stageId);
            string fromStage = standing != null ? standing.stage : (veg.state == "applied" || veg.state == "applying" ? veg.fromStage ?? "" : "");
            CliffCore308.Stage prior = null;
            if (fromStage != "")
            {
                prior = CliffCore308.LoadStage(cfg, fromStage, false);
                string want = standing != null ? standing.heightSha256 : veg.fromHeightSha256;
                if (!string.IsNullOrEmpty(want) && prior.HeightSha != want) throw new PostLedger308.Refused("the plants stand on stage " + fromStage + " as its veg run left them (field sha " + Short(want) + "), but that stage's field on disk is now " + Short(prior.HeightSha) + ": the move cannot be computed");
            }
            var reference = prior != null ? prior.Field : stage.BaseField;

            var sb = new StringBuilder(); var work = new List<(Sheet308 sheet, string assetPath, VegSheet rec, List<int> remove, List<int> reseat)>(); bool anything = false;
            foreach (string assetPath in spec.sheets)
            {
                if (PostLedger308.IsProtectedPath(assetPath)) throw new PostLedger308.Refused("protected sheet " + assetPath);
                var sheet = AssetDatabase.LoadAssetAtPath<Sheet308>(assetPath);
                if (sheet == null) throw new PostLedger308.Refused("sheet missing: " + assetPath);
                if (EditorUtility.IsDirty(sheet)) throw new PostLedger308.Refused("sheet " + assetPath + " has unsaved changes from another tool; save or discard them first");
                string sha = CliffCore308.ShaFile(PostLedger308.Abs(assetPath));
                var rec = veg.sheets.FirstOrDefault(s => s.sheet == assetPath) ?? new VegSheet { sheet = assetPath, guid = AssetDatabase.AssetPathToGUID(assetPath), reseatedKeys = new string[0] };
                string settled = SettlePending(rec, sha);
                if (!string.IsNullOrEmpty(rec.shaAfter) && rec.shaAfter == sha) { sb.Append(" | " + Path.GetFileNameWithoutExtension(assetPath) + ": already applied (file sha = ledger) - no-op" + settled); work.Add((sheet, assetPath, rec, null, null)); continue; }
                var done = new HashSet<string>(rec.reseatedKeys ?? new string[0]); var remove = new List<int>(); var reseat = new List<int>(); float maxLift = 0f;
                var list = sheet.FixedPlacements ?? new Sheet308.FixedPlacement[0];
                for (int i = 0; i < list.Length; i++)
                {
                    var p = list[i]; if (p == null) continue;
                    float now = stage.Field.Sample(p.Position.x, p.Position.z), was = reference.Sample(p.Position.x, p.Position.z);
                    if (Mathf.Abs(now - was) <= cfg.changedEps) continue;
                    if (CliffCore308.SlopeDeg(stage.Field, p.Position.x, p.Position.z) >= spec.removeSlopeDeg) remove.Add(i);
                    else if (!done.Contains(Key(p))) { reseat.Add(i); maxLift = Mathf.Max(maxLift, Mathf.Abs(now - was)); }
                }
                rec.total = list.Length; rec.maxLift = Mathf.Max(rec.maxLift, maxLift);
                sb.Append(" | " + Path.GetFileNameWithoutExtension(assetPath) + (prior != null ? " [plants stand on stage " + fromStage + "]" : "") + ": " + list.Length + " placements, on faces (slope >= " + spec.removeSlopeDeg.ToString("0.#", Inv) + " deg) to remove " + remove.Count + ", on moved ground to re-seat " + reseat.Count
                    + " (max " + maxLift.ToString("F1", Inv) + " m)" + (done.Count > 0 ? ", re-seated earlier " + done.Count : "") + settled);
                if (remove.Count + reseat.Count > 0) anything = true;
                work.Add((sheet, assetPath, rec, remove, reseat));
            }
            // the sheets are ONE asset for the three scenes: a scene that still stands on the old ground would show the re-seated plants in
            // the air (up to the stage's max raise) and lose the removed ones. Spec chain: tiles in every scene first, then veg once.
            var behind = cfg.scenes.Where(s => s.path != path).Select(s => ReadLedger(cfg, s.path)).Where(l => !(l.state == "applied" && l.stage == stageId)).Select(l => l.key).ToArray();
            bool partial = opt != null && opt.ContainsKey("partial");
            if (anything && behind.Length > 0)
            {
                string why = "the sheets are shared and [" + string.Join(", ", behind) + "] do not stand on stage " + stageId + " yet: plants would float / be missing there until tiles:<scene>";
                if (!dry && !partial) throw new PostLedger308.Refused(why + ". Run tiles for those scenes first (Spec chain step 5: veg once, after the three scenes), or add :partial to accept it");
                sb.Append(" | WARN " + why + (partial ? " (accepted by :partial)" : " (the write form refuses without :partial)"));
            }
            if (!veg.scenes.Contains(ledger.key)) veg.scenes = veg.scenes.Concat(new[] { ledger.key }).ToArray();
            if (!anything)
            {
                // "applying" here = a run that stopped after its last sheet save: every sheet is on its ledger sha, so the stage is applied
                if (!dry && (veg.state == "applied" || veg.state == "applying"))
                {
                    veg.state = "applied"; CliffCore308.WriteText(VegLedgerFile(cfg, stageId), JsonUtility.ToJson(veg, true));
                    SetStep(ledger, "veg", "applied", stageId, "shared sheets already applied"); WriteLedger(cfg, ledger, "veg " + stageId + ": shared sheets already applied");
                }
                return "veg " + stageId + " (" + ledger.key + ")" + sb + " | no-op";
            }
            if (dry) return "veg-dry " + stageId + " (" + ledger.key + ")" + sb + " | nothing written";

            string utc = PostLedger308.Utc(), backupDir = CliffCore308.OutFile(cfg, "Backup"); var sheets = veg.sheets.ToList();
            foreach (var (sheet, assetPath, rec, remove, reseat) in work)
            {
                if (remove == null || remove.Count + reseat.Count == 0) { if (!sheets.Contains(rec)) sheets.Add(rec); continue; }
                string name = Path.GetFileNameWithoutExtension(assetPath);
                string shaNow = CliffCore308.ShaFile(PostLedger308.Abs(assetPath));
                bool fresh = string.IsNullOrEmpty(rec.shaAfter);        // never applied, or veg-revert restored it
                bool foreign = !fresh && rec.shaAfter != shaNow;         // another tool edited the sheet after our last write
                if (fresh || foreign || string.IsNullOrEmpty(rec.shaBefore) || string.IsNullOrEmpty(rec.backup) || !File.Exists(rec.backup))
                {
                    // the restore point is the file as it is before the first veg run of this series; an older backup is never reused across a
                    // veg-revert or across another tool's edit (restoring it would silently drop what was saved in between)
                    if (foreign) rec.earlier = "an earlier veg run (-" + rec.removed + " ~" + rec.reseated + ", restore point " + rec.backup + ", removed list " + rec.removedFile + ") stays applied: the sheet was edited by another tool after it (file sha "
                        + Short(shaNow) + ", veg had left " + Short(rec.shaAfter) + "), so veg-revert returns to the file of " + utc + " only";
                    else if (fresh) rec.earlier = "";
                    rec.shaBefore = shaNow;
                    rec.backup = PostLedger308.Backup(backupDir, utc + "-veg-" + stageId, assetPath).Replace('\\', '/');
                    rec.removedAtBackup = rec.removed; rec.reseatedAtBackup = rec.reseated; rec.keysAtBackup = rec.reseatedKeys != null ? rec.reseatedKeys.Length : 0;
                }
                var list = sheet.FixedPlacements; var gone = new HashSet<int>(remove); var keys = new List<string>(rec.reseatedKeys ?? new string[0]);
                var removedFile = new RemovedFile { sheet = assetPath, index = remove.ToArray(), placements = remove.Select(i => list[i]).ToArray() };
                rec.removedFile = CliffCore308.OutFile(cfg, "cb308-veg-" + stageId + "-" + name + "-removed-" + utc + ".json");
                CliffCore308.WriteText(rec.removedFile, JsonUtility.ToJson(removedFile));
                // journal first: what this write will have done, and the file sha it starts from
                rec.pendingFrom = shaNow; rec.pendingRemoved = rec.removed; rec.pendingReseated = rec.reseated; rec.pendingKeys = keys.Count;
                foreach (int i in reseat) keys.Add(Key(list[i]));
                rec.removed += remove.Count; rec.reseated += reseat.Count; rec.reseatedKeys = keys.ToArray();
                if (!sheets.Contains(rec)) sheets.Add(rec);
                veg.sheets = sheets.ToArray(); veg.state = "applying"; veg.utc = utc; veg.heightSha256 = stage.HeightSha; veg.baseSha256 = stage.BaseSha; veg.removeSlopeDeg = spec.removeSlopeDeg;
                veg.fromStage = fromStage; veg.fromHeightSha256 = prior != null ? prior.HeightSha : "";
                CliffCore308.WriteText(VegLedgerFile(cfg, stageId), JsonUtility.ToJson(veg, true));
                try
                {
                    foreach (int i in reseat)
                    {
                        var p = list[i]; p.Position.y += stage.Field.Sample(p.Position.x, p.Position.z) - reference.Sample(p.Position.x, p.Position.z);
                    }
                    sheet.FixedPlacements = list.Where((p, i) => !gone.Contains(i)).ToArray();
                    EditorUtility.SetDirty(sheet); AssetDatabase.SaveAssetIfDirty(sheet);
                }
                catch (Exception e)
                {
                    // never leave an edited, unsaved shared sheet in memory (another session's save would flush it): reload it from its file.
                    // Whether the file was written is settled from the journal by the next veg / veg-revert.
                    try { EditorUtility.ClearDirty(sheet); AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate); } catch (Exception e2) { Debug.LogException(e2); }
                    throw new Exception("veg stopped while saving " + assetPath + " (" + e.GetType().Name + ": " + e.Message + "); the sheet was reloaded from its file and the ledger journal settles it on the next veg / veg-revert", e);
                }
                rec.shaAfter = CliffCore308.ShaFile(PostLedger308.Abs(assetPath)); rec.pendingFrom = "";
                CliffCore308.WriteText(VegLedgerFile(cfg, stageId), JsonUtility.ToJson(veg, true));
                // the instanced renderers of the open scene rebuild their chunks from the sheet
                foreach (var r in PostLedger308.All<CompactRebuildArtRenderer>(UnityEngine.SceneManagement.SceneManager.GetActiveScene()).Where(r => r.Sheet == sheet && r.enabled && CliffCore308.Saved(r.gameObject))) { r.enabled = false; r.enabled = true; }
            }
            veg.sheets = sheets.ToArray(); veg.state = "applied"; veg.utc = utc; veg.heightSha256 = stage.HeightSha; veg.baseSha256 = stage.BaseSha; veg.removeSlopeDeg = spec.removeSlopeDeg;
            veg.fromStage = fromStage; veg.fromHeightSha256 = prior != null ? prior.HeightSha : "";
            CliffCore308.WriteText(VegLedgerFile(cfg, stageId), JsonUtility.ToJson(veg, true));
            if (standing != null)
            {
                // the sheets now stand on this stage: the other run is history until veg-revert of this stage brings it back
                standing.state = "superseded"; standing.supersededBy = stageId;
                CliffCore308.WriteText(VegLedgerFile(cfg, standing.stage), JsonUtility.ToJson(standing, true));
            }
            SetStep(ledger, "veg", "applied", stageId, string.Join(", ", veg.sheets.Select(s => Path.GetFileNameWithoutExtension(s.sheet) + " -" + s.removed + " ~" + s.reseated)) + (fromStage != "" ? " (from stage " + fromStage + ")" : ""));
            WriteLedger(cfg, ledger, "veg " + stageId + ": applied");
            return "veg " + stageId + " (" + ledger.key + ")" + sb + " | applied; sheet assets saved one by one (shared by the three scenes, so veg:<other scene> is a no-op); backups " + string.Join(", ", veg.sheets.Select(s => s.backup))
                + "; ledger " + VegLedgerFile(cfg, stageId) + " | Forest305 is rebuilt by Enclosure305 build-scene, not here";
        }

        static string VegRevert(CliffCore308.Config cfg, string token, Dictionary<string, string> opt, bool dry)
        {
            string path = CliffCore308.ScenePath(cfg, token);
            PostLedger308.RequireEditable();
            var ledger = ReadLedger(cfg, path);
            var step = ledger.steps.FirstOrDefault(s => s.name == "veg");
            string stageId = opt != null && opt.TryGetValue("stage", out string so) && so.Length > 0 ? so : step != null && step.stage != "" ? step.stage : ledger.stage;
            if (stageId == "") { var built = BuiltStages(cfg); if (built.Length == 1) stageId = built[0]; else throw new PostLedger308.Refused("no veg step recorded for " + ledger.key); }
            var veg = ReadVeg(cfg, stageId);
            if (veg.state == "superseded") return "veg-revert " + stageId + ": the veg run of stage " + stageId + " was taken over by stage " + veg.supersededBy + " (the sheets stand on that stage) - run veg-revert:" + ledger.key + ":stage=" + veg.supersededBy + " first; no-op";
            if (veg.state != "applied" && veg.state != "applying") return "veg-revert " + stageId + ": the veg ledger is '" + veg.state + "' - no-op";
            var sb = new StringBuilder(); var todo = new List<VegSheet>();
            foreach (var rec in veg.sheets)
            {
                if (PostLedger308.IsProtectedPath(rec.sheet)) throw new PostLedger308.Refused("protected sheet " + rec.sheet);
                string sha = CliffCore308.ShaFile(PostLedger308.Abs(rec.sheet)); string name = Path.GetFileNameWithoutExtension(rec.sheet);
                string settled = SettlePending(rec, sha);
                if (string.IsNullOrEmpty(rec.shaBefore) || sha == rec.shaBefore) { sb.Append(" | " + name + ": already the pre-veg file" + settled); continue; }
                if (sha != rec.shaAfter) throw new PostLedger308.Refused(rec.sheet + " changed after veg (file sha " + Short(sha) + ", veg left " + Short(rec.shaAfter) + "): another tool edited it; restoring the backup would drop that edit. The removed placements are in " + rec.removedFile);
                if (!File.Exists(rec.backup) || CliffCore308.ShaFile(rec.backup) != rec.shaBefore) throw new PostLedger308.Refused("the backup " + rec.backup + " is missing or is not the pre-veg file");
                var loaded = AssetDatabase.LoadAssetAtPath<Sheet308>(rec.sheet);
                if (loaded != null && EditorUtility.IsDirty(loaded)) throw new PostLedger308.Refused("sheet " + rec.sheet + " has unsaved changes");
                sb.Append(" | " + name + ": restore " + (rec.removed - rec.removedAtBackup) + " removed, " + (rec.reseated - rec.reseatedAtBackup) + " re-seated placements from " + rec.backup
                    + (string.IsNullOrEmpty(rec.earlier) ? "" : " (PARTIAL: " + rec.earlier + ")")); todo.Add(rec);
            }
            var applied = cfg.scenes.Select(s => ReadLedger(cfg, s.path)).Where(l => l.state == "applied").Select(l => l.key).ToArray();
            string warn = applied.Length > 0 ? " | NOTE tiles are still applied in [" + string.Join(", ", applied) + "]: plants stand on the old ground there until revert:<scene>" : "";
            if (todo.Count == 0)
            {
                // nothing to restore; a ledger left 'applying' by an interrupted run whose sheets were never saved is closed here
                if (!dry && veg.state == "applying") { veg.state = "reverted"; veg.utc = PostLedger308.Utc(); CliffCore308.WriteText(VegLedgerFile(cfg, stageId), JsonUtility.ToJson(veg, true)); }
                return "veg-revert " + stageId + sb + " | no-op";
            }
            if (dry) return "veg-revert-dry " + stageId + sb + warn + " | nothing written";
            foreach (var rec in todo)
            {
                File.Copy(rec.backup, PostLedger308.Abs(rec.sheet), true);
                AssetDatabase.ImportAsset(rec.sheet, ImportAssetOptions.ForceUpdate);
                if (CliffCore308.ShaFile(PostLedger308.Abs(rec.sheet)) != rec.shaBefore) return "error: " + rec.sheet + " is not the pre-veg file after the restore";
                var sheet = AssetDatabase.LoadAssetAtPath<Sheet308>(rec.sheet);
                foreach (var r in PostLedger308.All<CompactRebuildArtRenderer>(UnityEngine.SceneManagement.SceneManager.GetActiveScene()).Where(r => r.Sheet == sheet && r.enabled && CliffCore308.Saved(r.gameObject))) { r.enabled = false; r.enabled = true; }
                // back to the counters of the restore point (0 unless an earlier series stays applied in that file)
                rec.shaAfter = ""; rec.removed = rec.removedAtBackup; rec.reseated = rec.reseatedAtBackup; rec.reseatedKeys = (rec.reseatedKeys ?? new string[0]).Take(rec.keysAtBackup).ToArray();
                // one ledger write per restored sheet: a stop between two sheets must not leave "re-seated" keys for a file that is restored
                CliffCore308.WriteText(VegLedgerFile(cfg, stageId), JsonUtility.ToJson(veg, true));
            }
            veg.state = "reverted"; veg.utc = PostLedger308.Utc();
            CliffCore308.WriteText(VegLedgerFile(cfg, stageId), JsonUtility.ToJson(veg, true));
            // the sheets are back on the files this run started from: a run of another stage that this one had taken over stands again
            string back = "", standingAgain = "";
            foreach (string other in BuiltStages(cfg))
            {
                if (other == stageId) continue;
                var l = ReadVeg(cfg, other);
                if (l.state != "superseded" || l.supersededBy != stageId) continue;
                bool same = l.sheets.All(s => string.IsNullOrEmpty(s.shaAfter) || CliffCore308.ShaFile(PostLedger308.Abs(s.sheet)) == s.shaAfter);
                if (!same) { back += " | NOTE the veg ledger of stage " + other + " stays superseded: a sheet is not the file that run left"; continue; }
                l.state = "applied"; l.supersededBy = ""; CliffCore308.WriteText(VegLedgerFile(cfg, other), JsonUtility.ToJson(l, true)); back += " | the sheets stand on stage " + other + " again (its veg ledger is applied)"; standingAgain = other;
            }
            foreach (var sc in cfg.scenes)
            {
                var l = ReadLedger(cfg, sc.path); var s = l.steps.FirstOrDefault(x => x.name == "veg");
                if (s == null || s.state != "applied") continue;
                if (standingAgain != "") SetStep(l, "veg", "applied", standingAgain, "sheets back on the stage-" + standingAgain + " veg run after veg-revert " + stageId);
                else SetStep(l, "veg", "reverted", stageId, "sheet backups restored");
                WriteLedger(cfg, l, "veg-revert " + stageId);
            }
            return "veg-revert " + stageId + sb + " | restored byte for byte (file sha = pre-veg)" + back + warn + " | ledger " + VegLedgerFile(cfg, stageId);
        }
    }
}
