using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Core.Domain;
using Oheangbu.Data.Spell;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 forms2 (Tools/Unity/Stage308_forms2/DESIGN.md 5-1): the data side of the stage - the two assets of the deploy layer.
    // Queue: Oheangbu.EditorTools.WorldMacro.Forms308 Run "<command>"   (no modal dialog: a refusal comes back as "refused: ...")
    //   forms308-status          the live map's legacy-body policy per category, the profile's forms2 tier numbers, the backups
    //   forms308-map:dry         what forms308-map would change (row by row count), nothing written
    //   forms308-map             Tools/Unity/Stage308_forms2/_Generated/deploy_map308.json -> SpellDeploy308Map.asset (backup first),
    //                            by BUNDLE: without an option only the ward rows (5) and the install rows (5) become Retire.
    //     :parry                 + the parry rows (5). Only after the HUD whitelist question about the guard stroke is answered
    //                            and recorded in Docs/DECISIONS.md (the stroke follows the camera's heading for up to 4 s).
    //     :buff                  + the five EA buff rows (the Giyeok finals). Only after the user has seen buffwalk / buffend /
    //                            aura and decided that marks are enough: the stage's own map keeps every buff row Keep.
    //                            A bundle that is not named keeps the legacy body the asset has today.
    //   forms308-profile[:dry]   the profile asset's forms2 tier numbers (an asset saved before this stage has the PC numbers on
    //                            both tiers) + writes the new sets into the asset as they are in memory (backup first)
    //   forms308-revert          the OLDEST backup of each asset is put back (the asset files are replaced and re-imported); the
    //                            profile's LayerEnabled / EnabledLetters of today are kept. Refused while either asset has unsaved
    //                            changes in memory.
    //   forms308-unit            U-F1..U-F9 in memory (nothing saved)
    // Saving: AssetDatabase.SaveAssetIfDirty on the named asset only - never SaveAssets. Backups go outside Assets
    // (Art/SpellVFX120/Deploy308/Backup_forms2/<utc>/). The layer's master switch and EnabledLetters are not touched here.
    public static class Forms308
    {
        const string MapJson = "Tools/Unity/Stage308_forms2/_Generated/deploy_map308.json";
        const string BackupDir = "Art/SpellVFX120/Deploy308/Backup_forms2";

        public static string Run(string command)
        {
            string[] a = (command ?? "").Split(':');
            bool dry = a.Skip(1).Any(p => string.Equals(p.Trim(), "dry", StringComparison.OrdinalIgnoreCase));
            bool parry = a.Skip(1).Any(p => string.Equals(p.Trim(), "parry", StringComparison.OrdinalIgnoreCase));
            bool buff = a.Skip(1).Any(p => string.Equals(p.Trim(), "buff", StringComparison.OrdinalIgnoreCase));
            try
            {
                switch (a[0])
                {
                    case "forms308-status": return Status();
                    case "forms308-map": return Map(dry, parry, buff);
                    case "forms308-profile": return Profile(dry);
                    case "forms308-revert": return Revert();
                    case "forms308-unit": return Unit();
                    default: throw new PostLedger308.Refused("unknown command '" + command + "' (forms308-status | forms308-map[:dry][:parry][:buff] | forms308-profile[:dry] | forms308-revert | forms308-unit)");
                }
            }
            catch (PostLedger308.Refused r) { return "refused: " + r.Message; }
        }

        static void RequireIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new PostLedger308.Refused("the editor is compiling or importing");
        }

        // ---------------------------------------------------------------- policy

        /// <summary>The stage's map policy (DESIGN 5-1). null = the row is as it must be.</summary>
        internal static string PolicyError(in SpellDeploy308MapSO.Row row)
        {
            DeployLegacyBody308 want;
            switch (row.Category)
            {
                case DeployCategory308.Summon: want = DeployLegacyBody308.Wrap; break;       // the model is the summon; a static animal's lifetime times its sound
                case DeployCategory308.Field: want = DeployLegacyBody308.Keep; break;        // the tree and the bridge are things of the world
                case DeployCategory308.Buff: want = DeployLegacyBody308.Keep; break;         // nothing of a running buff is in the forward view without its body (REVIEW.md F11): the user decides
                default: want = DeployLegacyBody308.Retire; break;
            }
            // the tree lift is the wood field row: its body is read by the rule (a refused plan fails the cast)
            if (row.Category == DeployCategory308.Field && row.Element == Element.Wood && row.LegacyBody != DeployLegacyBody308.Keep) return row.Letter + ": the tree lift must stay Keep (its body is read by the rule)";
            return row.LegacyBody == want ? null : row.Letter + " (" + row.Category + "): " + row.LegacyBody + ", the stage wants " + want;
        }

        static string Policy(SpellDeploy308MapSO.Row[] rows)
        {
            var sb = new StringBuilder();
            foreach (DeployCategory308 category in Enum.GetValues(typeof(DeployCategory308)))
            {
                var of = rows.Where(r => r.Category == category).ToArray();
                sb.Append(category).Append(' ').Append(of.Length).Append(" = ")
                    .Append(string.Join("+", of.GroupBy(r => r.LegacyBody).OrderBy(g => g.Key).Select(g => g.Key + " " + g.Count()))).Append(" | ");
            }
            return sb.ToString().TrimEnd(' ', '|');
        }

        static SpellDeploy308MapSO.Row[] ReadRows(out string csvSha)
        {
            string path = PostLedger308.RepoPath(MapJson);
            if (!File.Exists(path)) throw new PostLedger308.Refused("no " + MapJson + " (run python Tools/Unity/Stage308_forms2/_Tools/deploy308_map.py)");
            var file = JsonUtility.FromJson<Deploy308.MapFile>(File.ReadAllText(path, Encoding.UTF8));
            if (file == null || file.rows == null || file.rows.Length != 120) throw new PostLedger308.Refused(MapJson + " must hold 120 rows, has " + (file != null && file.rows != null ? file.rows.Length : 0));
            csvSha = file.csvSha256 ?? "";
            var rows = new SpellDeploy308MapSO.Row[file.rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                var r = file.rows[i];
                try
                {
                    rows[i] = new SpellDeploy308MapSO.Row
                    {
                        Letter = r.letter, Category = (DeployCategory308)Enum.Parse(typeof(DeployCategory308), r.category), Frame = (DeployFrame308)Enum.Parse(typeof(DeployFrame308), r.frame),
                        Element = (Element)Enum.Parse(typeof(Element), r.element), Final = (DeployFinal308)Enum.Parse(typeof(DeployFinal308), r.final), ImpactFrame = r.impactFrame,
                        ImpactOnTrigger = r.impactOnTrigger, LegacyBody = (DeployLegacyBody308)Enum.Parse(typeof(DeployLegacyBody308), r.legacyBody), FormOverride = r.formOverride ?? "",
                    };
                }
                catch (ArgumentException) { throw new PostLedger308.Refused("row " + i + " (" + r.letter + "): an enum name is not known"); }
                if (string.IsNullOrEmpty(rows[i].Letter) || rows[i].Letter.Length != 1) throw new PostLedger308.Refused("row " + i + ": bad letter");
            }
            return rows;
        }

        static string Sha(string repoPath) => Deploy308.Sha256(File.ReadAllBytes(PostLedger308.RepoPath(repoPath)));

        // ---------------------------------------------------------------- status

        static string Status()
        {
            var sb = new StringBuilder("forms308-status\n");
            var map = AssetDatabase.LoadAssetAtPath<SpellDeploy308MapSO>(Deploy308.MapPath);
            sb.AppendLine(map == null ? "map asset: missing (" + Deploy308.MapPath + ")" : "map asset: " + map.Count + " rows | " + Policy(map.Rows));
            var profile = AssetDatabase.LoadAssetAtPath<SpellDeploy308ProfileSO>(Deploy308.ProfilePath);
            if (profile == null) sb.AppendLine("profile asset: missing (" + Deploy308.ProfilePath + ")");
            else
            {
                sb.AppendLine("profile asset: LayerEnabled=" + profile.LayerEnabled + " EnabledLetters='" + profile.EnabledLetters + "' | guard=" + profile.Guard.Enabled + " fence=" + profile.Fence.Enabled
                    + " comet=" + profile.Comet.Enabled + " (per frame " + profile.Comet.PerFrame + ") summon entrance=" + profile.Summon.OwnEntrance + " foot marks=" + profile.Buff.FootMarks + " brush band=" + profile.Buff.BrushBand);
                for (int i = 0; i < 2; i++) sb.AppendLine("  " + TierText(profile.Tier((DeployTier308)i)));
                sb.AppendLine("  " + TierDiff(profile));
                sb.AppendLine("  " + SetDiff(profile));
            }
            string json = PostLedger308.RepoPath(MapJson);
            if (File.Exists(json))
            {
                try { var rows = ReadRows(out string sha); sb.AppendLine("stage map: " + Policy(rows) + " | csv " + (sha.Length >= 12 ? sha.Substring(0, 12) : sha) + (sha.Equals(Sha(Deploy308.CsvFile), StringComparison.OrdinalIgnoreCase) ? " (the CSV as it is now)" : " (THE CSV HAS CHANGED)")); }
                catch (PostLedger308.Refused r) { sb.AppendLine("stage map: " + r.Message); }
            }
            else sb.AppendLine("stage map: " + MapJson + " missing");
            string dir = PostLedger308.RepoPath(BackupDir);
            sb.Append("backups: " + (Directory.Exists(dir) ? string.Join(" ", Directory.GetDirectories(dir).Select(Path.GetFileName).OrderBy(n => n)) : "none"));
            return sb.ToString();
        }

        static string TierText(SpellDeploy308ProfileSO.TierSet t) =>
            t.Name + ": standing " + t.StandingBursts + " x " + t.VertsPerStanding + " verts, fence posts " + t.FencePosts + " rails " + t.FenceRails + " ring drops " + t.FenceRingDrops + ", guard needles " + t.GuardNeedles
            + " dry ticks " + t.GuardDryTicks + ", comet tails " + t.CometTails + " drops " + t.CometDrops + ", foot marks " + t.FootMarksPerStep + "/step " + Deploy308.F(t.FootMarksPerSecond) + "/s, aura stamps " + t.AuraStamps;

        static readonly string[] TierFields = { "StandingBursts", "VertsPerStanding", "FencePosts", "FenceRails", "FenceRingDrops", "GuardNeedles", "GuardDryTicks", "CometTails", "CometDrops", "FootMarksPerStep", "FootMarksPerSecond", "AuraStamps", "Forms2Saved" };

        // look2: the forms2 sets whose numbers the look rounds tune. The asset keeps its own copy of every serialised set, so a
        // number changed in the code reaches the asset (and the previews, which instantiate the asset) only through this list.
        // (forms3 builder 3: AirTail - the airborne tailed drop, D10; an asset saved before it has no such set and reads the code defaults)
        static readonly string[] SetFields = { "Guard", "GuardElements", "Fence", "FenceElements", "Comet", "Comets", "Summon", "Loose", "StandingGlow", "AirTail", "Cover", "Trigger" };   // forms3 fix pass: + Cover, Trigger

        static string SetText(object value)
        {
            if (value == null) return "null";
            if (value is System.Array array)
            {
                var parts = new List<string>();
                foreach (object item in array) parts.Add(SetText(item));
                return "[" + string.Join(",", parts) + "]";
            }
            return value.GetType().IsPrimitive ? System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) : JsonUtility.ToJson(value);
        }

        static string SetDiff(SpellDeploy308ProfileSO profile)
        {
            var defaults = ScriptableObject.CreateInstance<SpellDeploy308ProfileSO>();
            try
            {
                var differs = new List<string>();
                foreach (string name in SetFields)
                {
                    var field = typeof(SpellDeploy308ProfileSO).GetField(name);
                    if (SetText(field.GetValue(profile)) != SetText(field.GetValue(defaults))) differs.Add(name);
                }
                // forms3 (D5): of the Buff set only the mark cells are the stage's (the rest of the set is not a look number)
                if (SetText(profile.Buff.MarkCells) != SetText(defaults.Buff.MarkCells)) differs.Add("Buff.MarkCells");
                return differs.Count == 0 ? "forms2 sets = the stage's" : "forms2 sets differ from the stage's: " + string.Join(", ", differs);
            }
            finally { Object.DestroyImmediate(defaults); }
        }

        static string TierDiff(SpellDeploy308ProfileSO profile)
        {
            var defaults = ScriptableObject.CreateInstance<SpellDeploy308ProfileSO>();
            try
            {
                var differs = new List<string>();
                for (int i = 0; i < 2 && profile.Tiers != null && i < profile.Tiers.Length; i++)
                    foreach (string name in TierFields)
                    {
                        var field = typeof(SpellDeploy308ProfileSO.TierSet).GetField(name);
                        if (!Equals(field.GetValue(profile.Tiers[i]), field.GetValue(defaults.Tiers[i]))) differs.Add(profile.Tiers[i].Name + "." + name + "=" + field.GetValue(profile.Tiers[i]) + " (stage " + field.GetValue(defaults.Tiers[i]) + ")");
                    }
                return differs.Count == 0 ? "forms2 tier numbers = the stage's" : "forms2 tier numbers differ from the stage's: " + string.Join(", ", differs);
            }
            finally { Object.DestroyImmediate(defaults); }
        }

        // ---------------------------------------------------------------- backup

        static string Backup(string utc, string assetPath)
        {
            string from = PostLedger308.Abs(assetPath);
            if (!File.Exists(from)) throw new PostLedger308.Refused("no asset file to back up: " + assetPath);
            string dir = Path.Combine(PostLedger308.RepoPath(BackupDir), utc);
            Directory.CreateDirectory(dir);
            string to = Path.Combine(dir, Path.GetFileName(assetPath));
            File.Copy(from, to, false);
            return BackupDir + "/" + utc + "/" + Path.GetFileName(assetPath);
        }

        // ---------------------------------------------------------------- map

        /// <summary>The legacy body a row gets from forms308-map: the stage's for a bundle that was named (ward and install
        /// always, parry / the five EA buffs only on request), the asset's own for every other parry and buff row.</summary>
        internal static DeployLegacyBody308 BundleBody(in SpellDeploy308MapSO.Row stage, DeployLegacyBody308 assetBody, bool parry, bool buff)
        {
            switch (stage.Category)
            {
                case DeployCategory308.Parry: return parry ? stage.LegacyBody : assetBody;
                case DeployCategory308.Buff: return buff && stage.Final == DeployFinal308.Giyeok ? DeployLegacyBody308.Retire : assetBody;
                default: return stage.LegacyBody;
            }
        }

        static string Map(bool dry, bool parry, bool buff)
        {
            RequireIdle();
            var rows = ReadRows(out string csvSha);
            string csvNow = Sha(Deploy308.CsvFile);
            if (!string.Equals(csvNow, csvSha, StringComparison.OrdinalIgnoreCase))
                throw new PostLedger308.Refused("the glyph CSV changed after the stage map was generated: rerun python Tools/Unity/Stage308_forms2/_Tools/deploy308_map.py");
            if (!SpellDeploy308MapSO.CountsMatch(Rows(rows), out string counts)) throw new PostLedger308.Refused("category counts are off: " + counts);
            var errors = rows.Select(r => PolicyError(r)).Where(e => e != null).ToArray();
            if (errors.Length > 0) throw new PostLedger308.Refused("the stage map breaks the stage's own policy: " + string.Join("; ", errors.Take(6)));
            var map = AssetDatabase.LoadAssetAtPath<SpellDeploy308MapSO>(Deploy308.MapPath);
            if (map == null) throw new PostLedger308.Refused("no map asset at " + Deploy308.MapPath + " (deploy308-map makes it)");
            var now = map.Rows.ToDictionary(r => r.Char);
            if (EditorUtility.IsDirty(map)) throw new PostLedger308.Refused("the map asset has unsaved changes in memory: save or discard them first");
            for (int i = 0; i < rows.Length; i++)
                if (now.TryGetValue(rows[i].Char, out var asset)) rows[i].LegacyBody = BundleBody(rows[i], asset.LegacyBody, parry, buff);
            var changed = rows.Where(r => !now.TryGetValue(r.Char, out var old) || old.LegacyBody != r.LegacyBody).ToArray();
            var other = rows.Where(r => now.TryGetValue(r.Char, out var old) && (old.Category != r.Category || old.Element != r.Element || old.Frame != r.Frame || old.Final != r.Final || old.ImpactFrame != r.ImpactFrame || old.ImpactOnTrigger != r.ImpactOnTrigger)).ToArray();
            var sb = new StringBuilder("forms308-map" + (dry ? " (dry)" : "") + ": csv sha matches | " + counts + "\n");
            sb.AppendLine("bundles: ward + install" + (parry ? " + parry" : "") + (buff ? " + the five EA buffs" : "")
                + (parry ? "" : " | parry rows are left as they are (add :parry only after the HUD whitelist question about the guard stroke is recorded in Docs/DECISIONS.md)")
                + (buff ? "" : " | buff rows are left as they are (add :buff only after the user has decided that marks are enough for a running buff)"));
            sb.AppendLine("now:   " + Policy(map.Rows));
            sb.AppendLine("after: " + Policy(rows));
            sb.AppendLine("legacy body changes: " + changed.Length + " rows (" + string.Join(" ", changed.GroupBy(r => r.Category).Select(g => g.Key + " " + g.Count() + "->" + g.First().LegacyBody)) + ")"
                + (other.Length > 0 ? " | OTHER columns differ on " + other.Length + " rows (" + string.Concat(other.Take(12).Select(r => r.Letter)) + ")" : " | no other column changes"));
            if (other.Length > 0) throw new PostLedger308.Refused(sb + "the stage map differs from the asset in more than the legacy body: regenerate both from the same CSV");
            if (dry) { sb.Append("would rewrite " + Deploy308.MapPath + " after a backup"); return sb.ToString(); }
            if (changed.Length == 0) { sb.Append("nothing to change"); return sb.ToString(); }
            string backup = Backup(PostLedger308.Utc(), Deploy308.MapPath);
            map.Rows = rows; map.SourceCsvSha256 = csvSha;
            EditorUtility.SetDirty(map); AssetDatabase.SaveAssetIfDirty(map);
            sb.Append("wrote " + rows.Length + " rows | backup " + backup);
            return sb.ToString();
        }

        static SpellDeploy308MapSO Rows(SpellDeploy308MapSO.Row[] rows)
        {
            var map = ScriptableObject.CreateInstance<SpellDeploy308MapSO>();
            map.hideFlags = HideFlags.HideAndDontSave; map.Rows = rows;
            EditorApplication.delayCall += () => { if (map != null) Object.DestroyImmediate(map); };
            return map;
        }

        // ---------------------------------------------------------------- profile

        static string Profile(bool dry)
        {
            RequireIdle();
            var profile = AssetDatabase.LoadAssetAtPath<SpellDeploy308ProfileSO>(Deploy308.ProfilePath);
            if (profile == null) throw new PostLedger308.Refused("no profile asset at " + Deploy308.ProfilePath + " (deploy308-assets makes it)");
            if (profile.Tiers == null || profile.Tiers.Length != 2) throw new PostLedger308.Refused("the profile's Tiers array must have two entries (PC, Mobile)");
            var sb = new StringBuilder("forms308-profile" + (dry ? " (dry)" : "") + "\n");
            sb.AppendLine("now:   " + TierDiff(profile) + " | " + SetDiff(profile));
            sb.AppendLine("until this is saved the layer already draws with the stage's numbers on both tiers (Tier() answers with the code's own forms2 numbers for a tier whose Forms2Saved is off)");
            if (EditorUtility.IsDirty(profile)) throw new PostLedger308.Refused("the profile asset has unsaved changes in memory: save or discard them first");
            if (dry) { sb.Append("would set the forms2 tier numbers to the stage's and save the look sets (" + string.Join(" / ", SetFields) + "; each is written whole, the forms3 fields inside them included: Guard.Head / BreakHead and the guard numbers that were code constants, GuardElements[].HeadShareMul / HeadEndMul / Lines; Summon.Head / RootWidth / RootSink / OuterWidthShare / OuterCurveStep / DepthStep / TopJitter / HeadRoom / FootJitter / BirthStep / NeedleHeightShare / Segments; Fence.Flame* / Wave* / Spindle* / Stone* / LowRailChordsPerRail; FenceElements[].RailSpindles / SpindleRows) and Buff.MarkCells (fire 15 -> 10; nothing else of Buff) and the new AirTail set (D10: Cell / Base / PerSpeed / Max / MinSpeed / Width) and the fix pass's new sets Cover (the presenter's standing cover wall: rising strokes) and Trigger (the combo burst: Own / Bold / LengthMul / Open / Head) and its new fields inside the sets above (every BrushHeadSet.Swell / SwellSpan, GuardElements[].DryWidthMul, Fence.FlameBaseShare / FlameBaseMax / FlameCurlMin / FlameWave / FlameSideFrom / FlameSideEvery / FlameStain* / FlameSegmentsCoarse / PostHead / SpindleSegmentsCoarse / StoneLift / WaveDrop*, FenceElements[].SpindleSagMul) into the asset. The presenter sheet has no asset: its new numbers (Ring.Body / Arcs / ArcShare / ArcMaxDeg / Half* / Head / Segments / Lift / Wobble / MaxSeconds, Fling.Tailed / SizeStep / Size / Spacing / Drops, Splash.Drip* / DripStagger, Ring.BodiesMax, Sword.Blade* / Slash*) are read from the code. Written, after a backup. LayerEnabled / EnabledLetters stay as they are."); return sb.ToString(); }
            string backup = Backup(PostLedger308.Utc(), Deploy308.ProfilePath);
            var defaults = ScriptableObject.CreateInstance<SpellDeploy308ProfileSO>();
            try
            {
                for (int i = 0; i < 2; i++)
                    foreach (string name in TierFields)
                    {
                        var field = typeof(SpellDeploy308ProfileSO.TierSet).GetField(name);
                        field.SetValue(profile.Tiers[i], field.GetValue(defaults.Tiers[i]));
                    }
                // look2: the look sets are written whole (plain data objects: the code's own instances are handed over)
                foreach (string name in SetFields)
                {
                    var field = typeof(SpellDeploy308ProfileSO).GetField(name);
                    field.SetValue(profile, field.GetValue(defaults));
                }
                // forms3 (D5): the buff mark cells (fire 15 -> 10); nothing else of the Buff set is touched
                profile.Buff.MarkCells = (int[])defaults.Buff.MarkCells.Clone();
            }
            finally { Object.DestroyImmediate(defaults); }
            profile.ResetForms2Tiers();
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            sb.AppendLine("after: " + TierDiff(profile) + " | " + SetDiff(profile));
            sb.Append("saved " + Deploy308.ProfilePath + " | backup " + backup);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- revert

        static string Revert()
        {
            RequireIdle();
            string dir = PostLedger308.RepoPath(BackupDir);
            if (!Directory.Exists(dir)) throw new PostLedger308.Refused("no backups (" + BackupDir + ")");
            var sb = new StringBuilder("forms308-revert\n");
            int restored = 0;
            // the file on disk is replaced: an asset that is dirty in memory would be written over the restored file by the next
            // save of anybody (another session's SaveAssets), so nothing is restored while either is dirty
            foreach (string assetPath in new[] { Deploy308.MapPath, Deploy308.ProfilePath })
            {
                var loaded = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (loaded != null && EditorUtility.IsDirty(loaded)) throw new PostLedger308.Refused(assetPath + " has unsaved changes in memory: save or discard them first (nothing was restored)");
            }
            // the layer's switches are not this stage's: what they are now is put back after the profile file is replaced
            var before = AssetDatabase.LoadAssetAtPath<SpellDeploy308ProfileSO>(Deploy308.ProfilePath);
            bool hadProfile = before != null, layerEnabled = hadProfile && before.LayerEnabled;
            string enabledLetters = hadProfile ? before.EnabledLetters ?? "" : "";
            if (hadProfile) sb.AppendLine("profile now: LayerEnabled=" + layerEnabled + " EnabledLetters='" + enabledLetters + "' (kept)");
            foreach (string assetPath in new[] { Deploy308.MapPath, Deploy308.ProfilePath })
            {
                // the OLDEST backup is the state before this stage touched the asset
                string name = Path.GetFileName(assetPath);
                string source = Directory.GetDirectories(dir).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal).Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
                if (source == null) { sb.AppendLine(name + ": no backup, left as it is"); continue; }
                File.Copy(source, PostLedger308.Abs(assetPath), true);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                sb.AppendLine(name + ": restored from " + source.Substring(PostLedger308.RepoPath("").Length).Replace('\\', '/').TrimStart('/') + " (the WHOLE file: every value changed after that backup is gone)");
                restored++;
            }
            var after = AssetDatabase.LoadAssetAtPath<SpellDeploy308ProfileSO>(Deploy308.ProfilePath);
            if (hadProfile && after != null && (after.LayerEnabled != layerEnabled || (after.EnabledLetters ?? "") != enabledLetters))
            {
                sb.AppendLine("profile: the backup had LayerEnabled=" + after.LayerEnabled + " EnabledLetters='" + after.EnabledLetters + "' - today's values written back");
                after.LayerEnabled = layerEnabled; after.EnabledLetters = enabledLetters;
                EditorUtility.SetDirty(after); AssetDatabase.SaveAssetIfDirty(after);
            }
            var map = AssetDatabase.LoadAssetAtPath<SpellDeploy308MapSO>(Deploy308.MapPath);
            sb.Append(restored + " asset(s) restored" + (map != null ? " | map now: " + Policy(map.Rows) : "") + " | the backups are kept");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- unit (in memory)

        sealed class CountingHost : ISpellDeployHost308
        {
            public DeployTier308 TierValue; public int Rents, Returns, Stamps, Held, Released, Air, Began;
            public InkBurstBuffer308 Buffer; public Mesh Mesh; public bool SawStanding;
            public DeployTier308 Tier => TierValue;
            public float CutPause => 0f;
            public Camera ViewCamera => null;
            public bool RentBurst(InkDeployRuntime308 owner, out InkBurstBuffer308 buffer, out Mesh mesh) { Rents++; SawStanding = owner.Standing; buffer = Buffer; mesh = Mesh; return true; }
            public void ReturnBurst(InkDeployRuntime308 owner, InkBurstBuffer308 buffer, Mesh mesh) { Returns++; }
            public void StampResidue(in ResidueStamp308 stamp) { Stamps++; if (stamp.Held) Held++; }
            public void ReleaseHeld(int owner) { Released++; }
            public void SpawnAir(Vector3 worldPoint, Vector3 velocity, float size, int cell, float life, float gravityScale, float landY) { Air++; }
            public void RequestImpact(in ImpactRequest308 request) { }
            public void NotifyDeployBegan(char letter) { Began++; }
            public bool TargetGroggy(Transform target, AreaImpactPlan plan) => false;
        }

        static string Unit()
        {
            RequireIdle();
            var sb = new StringBuilder("forms308-unit\n");
            int failed = 0;
            void Check(string id, bool ok, string detail) { if (!ok) failed++; sb.AppendLine(id + " " + (ok ? "ok" : "FAIL") + ": " + detail); }
            var made = new List<Object>();
            try
            {
                var asset = AssetDatabase.LoadAssetAtPath<SpellDeploy308ProfileSO>(Deploy308.ProfilePath);
                var profile = asset != null ? Object.Instantiate(asset) : ScriptableObject.CreateInstance<SpellDeploy308ProfileSO>();
                profile.hideFlags = HideFlags.HideAndDontSave; profile.LayerEnabled = true; made.Add(profile);
                var rows = ReadRows(out _);
                var map = ScriptableObject.CreateInstance<SpellDeploy308MapSO>(); map.hideFlags = HideFlags.HideAndDontSave; map.Rows = rows; made.Add(map);
                profile.Map = map;
                SpellDeploy308MapSO.Row Row(DeployCategory308 category, Element element) => rows.First(r => r.Category == category && r.Element == element && r.Final == (category == DeployCategory308.AttackSingle ? DeployFinal308.None : r.Final));
                SpellDeploy308MapSO.Row Retired(SpellDeploy308MapSO.Row row) { row.LegacyBody = DeployLegacyBody308.Retire; return row; }

                // U-F1: ordinary bursts never push a standing body out (the rule the director applies), on both tiers of this profile
                foreach (DeployTier308 tier in new[] { DeployTier308.PC, DeployTier308.Mobile })
                {
                    var t = profile.Tier(tier);
                    int general = Mathf.Clamp(t.Bursts, 1, 8), standing = Mathf.Clamp(t.StandingBursts, 0, 6), serial = 1, pushed = 0;
                    var used = new bool[general + standing]; var order = new int[general + standing];
                    InkForms2Rules308.SlotRange(true, general, standing, out int s0, out int sc);
                    int fence = InkForms2Rules308.PickSlot(used, order, s0, sc, out _); used[fence] = true; order[fence] = serial++;
                    for (int i = 0; i < 9; i++)
                    {
                        InkForms2Rules308.SlotRange(false, general, standing, out int g0, out int gc);
                        int slot = InkForms2Rules308.PickSlot(used, order, g0, gc, out _);
                        if (slot >= general) pushed++;
                        used[slot] = true; order[slot] = serial++;
                    }
                    Check("U-F1 " + tier, standing > 0 && fence >= general && pushed == 0, "9 bursts on " + general + " + " + standing + " slots: standing slot taken " + pushed + " time(s)");
                }

                // U-F2 / U-F3: a guard stroke run through its whole life on the runtime component: phases follow the given clock, no glow after the birth cels
                {
                    var holder = new GameObject("Forms308_Unit") { hideFlags = HideFlags.HideAndDontSave }; made.Add(holder);
                    var runtime = holder.AddComponent<InkDeployRuntime308>();
                    float window = .9f, life = 4f;
                    var cast = new DeployCast308 { Profile = profile, Row = Retired(Row(DeployCategory308.Parry, Element.Wood)), Origin = Vector3.up, FallbackPoint = Vector3.up, Grade01 = .5f, Seed = 308,
                        CameraPosition = Vector3.up * 1.6f, HasCameraPosition = true, HoldSeconds = life, GuardWindow = window, GuardLife = life, NewForms = true };
                    bool configured = runtime.Configure(cast, null);
                    var line = runtime.GuardTimeline;
                    float cel = runtime.CelSeconds; bool phases = configured && runtime.Form == DeployForm308.Guard && runtime.Standing; float lateGlow = 0f; int wrong = 0;
                    for (float t = 0f; configured && t < life + 1f && runtime.Configured; t += cel * .5f)
                    {
                        runtime.Sample(t);
                        if (!runtime.Configured) break;
                        int k = runtime.CurrentCel;
                        var want = k < line.WetEndCel ? GuardPhase308.Window : GuardPhase308.Dry;
                        if (runtime.GuardPhase != want) wrong++;
                        if (k >= line.GlowEndCel) lateGlow = Mathf.Max(lateGlow, runtime.GlowNow);
                    }
                    Check("U-F2", phases && wrong == 0 && line.WetEndCel == Mathf.Max(1, Mathf.FloorToInt(window / cel + .0001f)) && line.WetEndCel * cel <= window + 1e-4f && runtime.MeshRebuilds == 1 && !runtime.Configured,
                        "guard: wet until cel " + line.WetEndCel + " (window " + window + " s / cel " + Deploy308.F(cel) + "), wrong phase on " + wrong + " samples, mesh rebuilt " + runtime.MeshRebuilds + "x, released at the end " + !runtime.Configured);
                    Check("U-F3", lateGlow == 0f, "guard: largest glow amount from cel " + line.GlowEndCel + " to the end of its " + life + " s = " + Deploy308.F(lateGlow));
                    runtime.Dispose();
                }

                // U-F4: a hosted fence (what an EA runtime gets through HostEffect) rents as a standing body, asks for its ring marks held, and lets them go at the end
                {
                    var holder = new GameObject("Forms308_Unit") { hideFlags = HideFlags.HideAndDontSave }; made.Add(holder);
                    var runtime = holder.AddComponent<InkDeployRuntime308>();
                    var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave }; made.Add(mesh);
                    var host = new CountingHost { TierValue = DeployTier308.Mobile, Buffer = new InkBurstBuffer308(profile.Tier(DeployTier308.Mobile).VertsPerStanding), Mesh = mesh };
                    var cast = new DeployCast308 { Profile = profile, Row = Retired(Row(DeployCategory308.Ward, Element.Metal)), Origin = Vector3.zero, FallbackPoint = Vector3.forward, Grade01 = .5f, Seed = 308,
                        HoldSeconds = 6f, WardRadius = 3f, WardHeight = 2.2f, FadeSeconds = .5f, NewForms = true };
                    bool configured = runtime.Configure(cast, host);
                    float lateGlow = 0f;
                    for (float t = 0f; configured && t < 7f && runtime.Configured; t += runtime.CelSeconds * .5f) { runtime.Sample(t); if (runtime.Configured && t > runtime.GlowEnd) lateGlow = Mathf.Max(lateGlow, runtime.GlowNow); }
                    Check("U-F4", configured && host.SawStanding && host.Began == 1 && host.Held > 0 && host.Released >= 1 && host.Returns == 1 && runtime.DroppedPrimitives == 0 && lateGlow == 0f,
                        "hosted fence (Mobile, the densest element): standing=" + host.SawStanding + ", held ring marks " + host.Held + ", released " + host.Released + ", buffer returned " + host.Returns + ", primitives that did not fit "
                        + runtime.DroppedPrimitives + ", glow after the rise " + Deploy308.F(lateGlow));
                    runtime.Dispose();
                }

                // U-F5: the map of the stage - parry / ward / install rows are Retire, summon Wrap, field and buff Keep (and so the tree lift)
                {
                    var errors = rows.Select(r => PolicyError(r)).Where(e => e != null).ToArray();
                    Check("U-F5", errors.Length == 0, "stage map policy: " + (errors.Length == 0 ? Policy(rows) : string.Join("; ", errors.Take(4))));
                }

                // U-F6: a row that says Retire is not retired while the effect runs on somebody else's clock in Play (N3)
                {
                    var retire = rows.First(r => r.LegacyBody == DeployLegacyBody308.Retire);
                    Check("U-F6", !InkForms2Rules308.RetireAllowed(retire, true) && InkForms2Rules308.RetireAllowed(retire, false), "RetireAllowed(Retire row, external clock in Play) = " + InkForms2Rules308.RetireAllowed(retire, true));
                }

                // U-F7: the comet on the runtime component - the advance it writes mid-flight is the flight clock's share, not a cel step
                {
                    var holder = new GameObject("Forms308_Unit") { hideFlags = HideFlags.HideAndDontSave }; made.Add(holder);
                    var runtime = holder.AddComponent<InkDeployRuntime308>();
                    float flight = .56f;
                    var cast = new DeployCast308 { Profile = profile, Row = Row(DeployCategory308.AttackSingle, Element.Fire), Origin = new Vector3(0f, 1.3f, 0f), FallbackPoint = new Vector3(0f, 1.1f, 10f), ImpactClock = flight,
                        Grade01 = .5f, Seed = 308, CameraPosition = Vector3.up * 1.6f, HasCameraPosition = true, PathEase = 1f, NewForms = true };
                    bool configured = runtime.Configure(cast, null);
                    float worst = 0f; int samples = 0;
                    for (float t = .01f; configured && t < flight; t += .013f) { runtime.Sample(t); worst = Mathf.Max(worst, Mathf.Abs(runtime.FlightAdvance - t / flight)); samples++; }
                    Check("U-F7", configured && runtime.Form == DeployForm308.Comet && runtime.Stats.Comet && worst < 1e-4f, "comet: " + samples + " samples through a " + flight + " s flight, largest |advance - t / flight| = " + worst.ToString("0.######"));
                    runtime.Dispose();
                }

                // U-F8: the numbers the layer draws with on Mobile are the stage's Mobile numbers, whether or not forms308-profile has
                // been run on the asset (a tier saved before the stage deserialises with the PC class defaults)
                {
                    var defaults = ScriptableObject.CreateInstance<SpellDeploy308ProfileSO>(); defaults.hideFlags = HideFlags.HideAndDontSave; made.Add(defaults);
                    var differs = new List<string>();
                    for (int i = 0; i < 2; i++)
                        foreach (string name in TierFields)
                        {
                            if (name == "Forms2Saved") continue;
                            var field = typeof(SpellDeploy308ProfileSO.TierSet).GetField(name);
                            if (!Equals(field.GetValue(profile.Tier((DeployTier308)i)), field.GetValue(defaults.Tiers[i]))) differs.Add((DeployTier308)i + "." + name + "=" + field.GetValue(profile.Tier((DeployTier308)i)));
                        }
                    bool saved = profile.Tiers != null && profile.Tiers.Length == 2 && profile.Tiers[0].Forms2Saved && profile.Tiers[1].Forms2Saved;
                    float ceiling = InkForms2Rules308.BuffAliveCeiling(profile, DeployTier308.Mobile), ring = profile.Tier(DeployTier308.Mobile).SpellResidue;
                    Check("U-F8", differs.Count == 0 && ceiling <= .45f * ring, "forms2 tier numbers in effect = the stage's (asset tiers saved with them: " + saved + ")" + (differs.Count == 0 ? "" : " - DIFFER: " + string.Join(", ", differs))
                        + " | Mobile: most marks alive at once " + Deploy308.F(ceiling) + " of " + ring + " (limit 45 %)");
                }

                // U-F9: a cast that does not ask for the new forms (the spell presenter's stage: its cover wall is an attack row drawn as
                // a Ward) keeps the category form and still stands in a standing slot; an install aimed at a target is never the loose mark
                {
                    var cover = Retired(Row(DeployCategory308.AttackSingle, Element.Earth)); cover.Category = DeployCategory308.Ward;
                    var holder = new GameObject("Forms308_Unit") { hideFlags = HideFlags.HideAndDontSave }; made.Add(holder);
                    var runtime = holder.AddComponent<InkDeployRuntime308>();
                    bool configured = runtime.Configure(new DeployCast308 { Profile = profile, Row = cover, Origin = Vector3.zero, FallbackPoint = Vector3.forward, Grade01 = .5f, Seed = 308, HoldSeconds = 12f, WardRadius = 2f }, null);
                    bool wall = configured && runtime.Form == DeployForm308.Category && runtime.Standing;
                    runtime.Dispose();
                    var install = Retired(Row(DeployCategory308.ComboInstall, Element.Wood));
                    bool aimed = InkForms2Rules308.FormOf(profile, install, false, false, true, true) == DeployForm308.Category && InkForms2Rules308.FormOf(profile, install, false, false, true, false) == DeployForm308.Loose
                        && InkForms2Rules308.FormOf(profile, install, false, false, false, false) == DeployForm308.Category;
                    Check("U-F9", wall && aimed, "a presenter cover wall (attack row as Ward, NewForms off): category form + standing slot=" + wall + " | install: with a target = category form, without = loose mark, NewForms off = category form: " + aimed);
                }
            }
            catch (PostLedger308.Refused) { throw; }
            catch (Exception e) { failed++; sb.AppendLine("EXCEPTION: " + e.GetType().Name + ": " + e.Message); }
            finally { foreach (var o in made) if (o != null) Object.DestroyImmediate(o); }
            sb.Append("forms308-unit: " + (failed == 0 ? "0 failed" : failed + " FAILED") + " (in memory, nothing saved; NOT covered: the director's slots on a live scene, the wiring seams, Signal on a retired effect - Play)");
            return sb.ToString();
        }
    }
}
