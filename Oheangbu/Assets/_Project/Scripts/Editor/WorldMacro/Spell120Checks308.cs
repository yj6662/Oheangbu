using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 check harness (SPEC-SPELL-120-308 section 11, L3). Edit Mode only: no Play, no dialog, nothing is written into
    /// Assets, the open scene is not touched (fixtures live in a preview scene). Reports go to
    /// Art/SpellVFX120/Spell120_308/Checks/ at the repo root.
    ///   Run("table")              table build + the 36-glyph regression against the pre-#308 resolver, on the real book assets
    ///                             (as they are, and with the freshly built table loaded into an in-memory copy)
    ///   Run("fixtures[:glyphs]")  dispatch, misfire, gate and hook checks on the real wiring; each work package adds its own
    ///                             partial method (Spell120Checks308.WPnn.cs). glyphs = only checks whose clause names one of them.
    ///                             Packages run one by one: a package that throws is reported and the next one still runs.
    ///   Run("fixtures:plan[:glyphs]")  dry: which packages would run, for which glyphs, how many check sites. Builds nothing.
    ///   Run("fixtures:cleanup")   destroys Spell308_* objects a fixture left behind; a dirty scene that lost one is reopened from disk
    ///   Run("status")             what the project carries now: books, table, registered handlers, unlock rules
    ///   Run("unlock[:...]")       the TEST unlock harness of the isolated save (Spell308TestUnlock; its own mode rules)
    /// A check line says "ok" or "FAIL". Passing here is the offline contract only: Play and the user's review come after.
    /// Every command ends with one line of its own: the open scenes (root objects, dirty flags) and the spell books (dirty
    /// flags) are as they were before the command.</summary>
    public static partial class Spell120Checks308
    {
        const string OutFromRepo = "Art/SpellVFX120/Spell120_308/Checks";
        const string SheetFolder = "Assets/_Project/Data/Spells";
        const string CsvFromRepo = "Docs/오행부_작도어휘_v0_1.csv";

        [Serializable]
        public sealed class Report
        {
            public string status = "", command = "", time = "", assembly = "";
            public int checks, failed;
            public List<string> lines = new List<string>(), notes = new List<string>();
            public string[] unverified = Array.Empty<string>();
            public List<PackageRun> packages = new List<PackageRun>();
            [NonSerialized] public string Filter = "";
            [NonSerialized] public bool Plan;                    // fixtures:plan: packages name their glyphs and build nothing
            [NonSerialized] public PackageRun Current;           // the package that is running
            [NonSerialized] public SpellBookSO FixtureBook;      // the book the fixtures clone, found once per command

            // clause = an id of clauses308.csv (glyph-number) or an AC id (A4, A6 ...).
            public bool Check(string clause, bool pass, string text)
            {
                checks++; if (!pass) failed++;
                lines.Add((pass ? "ok   " : "FAIL ") + clause + "  " + text);
                return pass;
            }

            // fixtures:<glyphs> = run a group only when its clause ids name one of the glyphs. The first question of a package
            // names that package's glyphs (it is asked before anything is built), which is all a plan needs: a plan answers no.
            public bool Wants(string glyphs)
            {
                bool wanted = string.IsNullOrEmpty(Filter);
                if (!wanted) foreach (char c in glyphs ?? "") if (Filter.IndexOf(c) >= 0) { wanted = true; break; }
                if (Current != null && Current.status.Length == 0) { Current.glyphs = glyphs ?? ""; Current.status = wanted ? "selected" : "filtered out"; }
                return wanted && !Plan;
            }
        }

        // One fixture package of a fixtures run (or of a plan).
        [Serializable]
        public sealed class PackageRun
        {
            public string name = "", glyphs = "", status = "", error = "";
            public int checks, failed, sites;
            public float seconds;
        }

        public static string Run(string argument)
        {
            string a = (argument ?? "").Trim();
            // the TEST unlock harness has its own mode rules (arm in Edit Mode, on / off in Play Mode)
            if (a == "unlock" || a.StartsWith("unlock:", StringComparison.Ordinal)) return Spell308TestUnlock.Run(a.Length > 7 ? a.Substring(7) : "");
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Spell120Checks308 runs in Edit Mode";
            if (EditorApplication.isCompiling) return "refused: scripts are compiling; call again when done";
            if (a == "table") return Finish("table", Table);
            if (a == "status") return Finish("status", Status);
            if (a == "fixtures:cleanup") return FixturesCleanup();
            if (a == "fixtures:plan" || a.StartsWith("fixtures:plan:", StringComparison.Ordinal))
            {
                string filter = a.Length > 14 ? a.Substring(14) : "";
                return Finish("fixtures_plan", r => { r.Filter = filter; r.Plan = true; Fixtures(r); }, false);
            }
            if (a == "fixtures" || a.StartsWith("fixtures:", StringComparison.Ordinal))
            {
                string filter = a.Length > 9 ? a.Substring(9) : "";
                return Finish(filter.Length > 0 ? "fixtures_filtered" : "fixtures", r => { r.Filter = filter; Fixtures(r); });
            }
            return "refused: expected table | fixtures[:glyphs] | fixtures:plan[:glyphs] | fixtures:cleanup | status | unlock[:status|arm|on|off|disarm]";
        }

        static string OutFolder
        {
            get { string folder = Path.Combine(SpellTable308.RepoRoot, OutFromRepo); Directory.CreateDirectory(folder); return folder; }
        }

        // probeBooks = false for a plan: it must not load an asset.
        static string Finish(string name, Action<Report> body, bool probeBooks = true)
        {
            var report = new Report { command = name, time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                assembly = typeof(CombatLoopWiring).Module.ModuleVersionId.ToString() };
            string before = Untouched(probeBooks);
            try { body(report); }
            catch (Exception exception) { report.failed++; report.lines.Add("FAIL exception  " + exception); }
            string after = Untouched(probeBooks);
            report.Check("A0", before == after, "the open scenes and the spell books are as they were before the command (root objects, dirty flags): " +
                (before == after ? after : before + "  ->  " + after));
            report.status = report.failed == 0 ? "COMPLETE" : "FAILED";
            File.WriteAllText(Path.Combine(OutFolder, name + "308_editor.json"), JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            return name + ": " + report.status + ", " + report.checks + " checks, " + report.failed + " failed\n" +
                string.Join("\n", report.lines.Where(l => l.StartsWith("FAIL", StringComparison.Ordinal)).Take(30)) +
                (report.notes.Count > 0 ? "\n" + string.Join("\n", report.notes.Take(40)) : "");
        }

        // What a check command must leave alone, as text: every open scene's root count and dirty flag, every target book's
        // dirty flag. A fixture object that escaped its preview scene, or a book marked dirty, changes this line.
        static string Untouched(bool books)
        {
            var b = new StringBuilder();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                b.Append(scene.name).Append(' ').Append(scene.isLoaded ? scene.rootCount : -1).Append(scene.isDirty ? " dirty" : " clean").Append("; ");
            }
            b.Append("fixture objects alive ").Append(FixtureStrays().Count).Append("; ");
            if (books)
                foreach (string path in SpellTable308.Chain())
                {
                    var book = AssetDatabase.LoadAssetAtPath<SpellBookSO>(path);
                    b.Append(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)) ?? "")).Append(book != null && EditorUtility.IsDirty(book) ? " book dirty; " : " book clean; ");
                }
            return b.ToString();
        }

        const string FixturePrefix = "Spell308_";

        // Every root object named like a fixture object that is alive anywhere in the editor (an open scene, a preview scene,
        // or hidden outside any scene). Between commands the answer must be none.
        static List<GameObject> FixtureStrays()
        {
            var found = new List<GameObject>();
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && go.transform.parent == null && go.name.StartsWith(FixturePrefix, StringComparison.Ordinal) && !EditorUtility.IsPersistent(go)) found.Add(go);
            return found;
        }

        // fixtures:cleanup - removes what a fixture left behind (2026-10-04: the first fixtures run left 18 objects in the open
        // scene). In every open scene the root objects named Spell308_* are destroyed; hidden ones outside a scene too. A scene
        // that was dirty and lost at least one such object is opened again from its file (single mode, only when it is the one
        // open scene): the unsaved change is discarded, nothing is saved, no dialog. A dirty scene without a fixture object is
        // left exactly as it is. Returns one text.
        static string FixturesCleanup()
        {
            var b = new StringBuilder("fixtures_cleanup:\n");
            string reopen = null; int removedTotal = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) { b.Append("scene ").Append(scene.name).Append(": not loaded, left alone\n"); continue; }
                bool dirty = scene.isDirty; int before = scene.rootCount;
                var mine = scene.GetRootGameObjects().Where(g => g != null && g.name.StartsWith(FixturePrefix, StringComparison.Ordinal)).ToList();
                var names = mine.GroupBy(g => g.name).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + " x" + g.Count()).ToList();
                foreach (var go in mine) Object.DestroyImmediate(go);
                removedTotal += mine.Count;
                b.Append("scene ").Append(scene.name).Append(": roots ").Append(before).Append(" -> ").Append(scene.rootCount).Append(dirty ? ", was dirty" : ", was clean")
                    .Append(", removed ").Append(mine.Count).Append(mine.Count > 0 ? " (" + string.Join(", ", names) + ")" : "").Append('\n');
                if (dirty && mine.Count == 0) b.Append("  dirty and no fixture object in it: nothing was changed, the scene stays dirty\n");
                if (dirty && mine.Count > 0)
                {
                    if (string.IsNullOrEmpty(scene.path)) b.Append("  dirty, but the scene has no file: not reopened\n");
                    else if (SceneManager.sceneCount != 1) b.Append("  dirty, but more than one scene is open: not reopened (a single-mode open would drop the others)\n");
                    else reopen = scene.path;
                }
            }
            var hidden = FixtureStrays();
            if (hidden.Count > 0)
            {
                b.Append("outside the open scenes: removed ").Append(hidden.Count).Append(" (").Append(string.Join(", ", hidden.GroupBy(g => g.name).Select(g => g.Key + " x" + g.Count()))).Append(")\n");
                foreach (var go in hidden) Object.DestroyImmediate(go);
                removedTotal += hidden.Count;
            }
            if (reopen != null)
            {
                var opened = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(reopen, UnityEditor.SceneManagement.OpenSceneMode.Single);
                b.Append("reopened from disk without saving: ").Append(reopen).Append(" -> roots ").Append(opened.rootCount).Append(opened.isDirty ? ", dirty" : ", clean").Append('\n');
            }
            b.Append("removed ").Append(removedTotal).Append(" in all; now: ").Append(Untouched(false));
            return b.ToString();
        }

        static SpellBookSO.Entry[] EntriesOf(SpellBookSO book)
        {
            return (SpellBookSO.Entry[])typeof(SpellBookSO).GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(book) ?? Array.Empty<SpellBookSO.Entry>();
        }

        // An in-memory copy of a book asset carrying the given table (nothing is saved).
        internal static SpellBookSO CloneWithTable(SpellBookSO source, SpellBuildResult308 build)
        {
            var clone = Object.Instantiate(source);
            clone.hideFlags = HideFlags.HideAndDontSave;
            if (build != null) clone.SetTable308(build.Rows, build.Unlocks, build.SourceHash, build.TableHash);
            return clone;
        }

        // The table of the sheets in Assets, optionally with extra in-memory sheets (fixtures add their probe rows this way).
        internal static SpellBuildResult308 BuildTable(IEnumerable<string> onlySheets, params SpellSheet308[] extra)
        {
            var sheets = new List<SpellSheet308>();
            foreach (string path in Directory.Exists(SheetFolder) ? Directory.GetFiles(SheetFolder, "Rules308_*.csv") : Array.Empty<string>())
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (onlySheets == null || onlySheets.Contains(name)) sheets.Add(new SpellSheet308(name, File.ReadAllText(path, Encoding.UTF8)));
            }
            sheets.AddRange(extra);
            string unlock = Path.Combine(SheetFolder, "Unlock308.csv");
            return SpellTableBuilder308.Build(File.ReadAllText(Path.Combine(SpellTable308.RepoRoot, CsvFromRepo), Encoding.UTF8), sheets,
                File.Exists(unlock) ? File.ReadAllText(unlock, Encoding.UTF8) : "");
        }

        // ---- table ----
        static void Table(Report r)
        {
            var build = SpellTable308.Build(false, out var sheetNames);
            r.notes.Add("sheets " + string.Join(", ", sheetNames)); r.notes.Add("source " + build.SourceHash); r.notes.Add("table " + build.TableHash);
            r.Check("A2", build.Ok, "the table builds from the sheets in Assets (with the handlers' declared parameters): " + string.Join(" | ", build.Errors.Take(6)));
            if (!build.Ok) return;
            int legacy = build.Rows.Count(x => x.Feature != SpellLegacyFeature.None), registered = build.Rows.Count(x => x.HasHandler && x.Feature == SpellLegacyFeature.None);
            int reserved = build.Rows.Count(x => !string.IsNullOrEmpty(x.Pending)), blank = build.Rows.Count(x => x.Category == SpellCategory.Blank);
            r.notes.Add("rows: legacy " + legacy + ", registered " + registered + ", reserved " + reserved + ", blank " + blank + ", waiting for a sheet " + build.MissingSheet.Count);
            r.Check("A2", legacy == 36 && blank == 20 && legacy + registered + reserved + build.MissingSheet.Count == 100, "36 legacy rows, 20 blank rows, every assigned glyph accounted for");
            var known = new HashSet<string>(SpellEffectInstaller308.CreateAll().Select(e => e.Id));
            var orphan = build.Rows.Where(x => x.HasHandler && x.Feature == SpellLegacyFeature.None && !known.Contains(x.Handler)).Select(x => x.Letter + ":" + x.Handler).ToList();
            r.Check("A2", orphan.Count == 0, "every row of a registered handler names a handler that exists: " + string.Join(" ", orphan.Take(10)));
            string offline = Path.Combine(SpellTable308.RepoRoot, "Art/SpellVFX120/Spell120_308/table308.txt");
            if (File.Exists(offline))
                r.Check("A2", SpellTableBuilder308.Normalise(File.ReadAllText(offline, Encoding.UTF8)).TrimEnd('\n') == build.CanonicalText,
                    "the editor builds the same table text as the offline importer (table308.txt)");
            else r.notes.Add("offline table308.txt not found: run python Tools/SpellVFX120/spell120_table308.py to compare");

            var targets = SpellTable308.Targets(); var mainGuids = SpellTable308.MainSceneGuids(); var skippedBooks = SpellTable308.ProtectedBooks();
            var total = new SpellEquivalence308.Result(); int books = 0; var sheet = new StringBuilder();
            float[][] measures = { new[] { .6f, 1f }, new[] { 1.2f, 2.5f } };   // (worst jamo distance, stroke seconds): a good and a poor letter
            foreach (string guid in AssetDatabase.FindAssets("t:SpellBookSO").OrderBy(g => AssetDatabase.GUIDToAssetPath(g), StringComparer.Ordinal))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<SpellBookSO>(path);
                if (asset == null) continue;
                books++;
                var entries = EntriesOf(asset);
                var subjects = new List<KeyValuePair<string, SpellBookSO>> { new KeyValuePair<string, SpellBookSO>(path + (asset.HasTable ? " [imported]" : " [no table]"), asset) };
                SpellBookSO clone = null;
                if (targets.Contains(path)) { clone = CloneWithTable(asset, build); subjects.Add(new KeyValuePair<string, SpellBookSO>(path + " [fresh table in memory]", clone)); }
                try
                {
                    foreach (var subject in subjects)
                        foreach (float[] measure in measures)
                        {
                            var resolver = new SpellResolver(subject.Value);
                            float brush = subject.Value.EvaluateBrushPower(measure[0], measure[1]);
                            var result = SpellEquivalence308.Compare(subject.Key, entries, true, brush,
                                letter => resolver.TryRow(letter, out var row) ? row : null,
                                (letter, gate, row) =>
                                {
                                    var drawn = new DrawnLetter(letter, default, default, null, measure[0], measure[0], measure[1], 1f, 3);
                                    var status = resolver.Resolve(drawn, gate, out SpellCast cast, out _);
                                    return new KeyValuePair<SpellResolveStatus, SpellCast>(status, cast);
                                });
                            total.Add(result);
                            // the bool overloads themselves (what the 50-odd editor tools call)
                            for (int set = 0; set < SpellEquivalence308.FlagSets; set++)
                            {
                                SpellEquivalence308.Flags(set, out bool guk, out bool buffs, out bool wards, out bool giyeok, out bool mum);
                                foreach (char letter in SpellEquivalence308.Letters())
                                {
                                    var drawn = new DrawnLetter(letter, default, default, null, measure[0], measure[0], measure[1], 1f, 3);
                                    bool oldOk = SpellOracle308.TryResolve(entries, true, letter, brush, out SpellCast oldCast, out _, guk, buffs, wards, giyeok, mum);
                                    bool newOk = resolver.TryResolve(drawn, out SpellCast newCast, guk, buffs, wards, giyeok, mum);
                                    total.Cases++;
                                    if (oldOk == newOk && (!oldOk || SpellEquivalence308.CastDifference(oldCast, newCast) == null)) continue;
                                    total.Mismatches++;
                                    if (total.Details.Count < 40) total.Details.Add(subject.Key + " overload flags " + set + " U+" + ((int)letter).ToString("X4"));
                                }
                            }
                        }
                    if (mainGuids.Contains(guid))
                    {
                        var resolver = new SpellResolver(clone != null ? clone : asset);
                        var lines = SpellEquivalence308.BeforeAfter(entries, 1f, 31, letter => resolver.TryRow(letter, out var row) ? row : null,
                            (letter, gate, row) => new KeyValuePair<SpellResolveStatus, SpellCast>(SpellResolveCore308.Resolve(letter, 1f, 1f, row, gate, out SpellCast cast), cast), out int same);
                        sheet.Append("# ").Append(path).Append(" (the book W_Demo_Main reads), every legacy flag on, fresh table: ").Append(same).Append(" of 120 same\n");
                        sheet.Append("index\tglyph\tbefore (pre-#308 resolver)\tafter (#308 resolver)\thandler\tverdict\n");
                        foreach (string line in lines) sheet.Append(line).Append('\n');
                        r.Check("A4", same == 120, "before/after sheet of the main-scene book: " + same + " of 120 same");
                    }
                    var traitDetails = new List<string>();
                    var traitResolver = new SpellResolver(clone != null ? clone : asset);
                    int traits = SpellEquivalence308.TraitMismatches(letter => traitResolver.TryRow(letter, out var row) ? row : null, traitDetails);
                    r.Check("A4", traits == 0, path + ": spikes / seed / pierce traits equal the old special cases " + string.Join(" | ", traitDetails.Take(4)));
                }
                finally { if (clone != null) Object.DestroyImmediate(clone); }
            }
            File.WriteAllText(Path.Combine(OutFolder, "table308_editor.tsv"), sheet.ToString(), new UTF8Encoding(false));
            r.notes.Add("books " + books + ", cases " + total.Cases + ", mismatches " + total.Mismatches + ", old casts " + total.OldResolved + ", new casts " + total.NewResolved);
            r.Check("A4", total.Mismatches == 0, "old resolver = new resolver on " + books + " book assets (" + total.Cases + " cases): " + string.Join(" | ", total.Details.Take(8)));
            // 2026-10-04: was "targets.Count >= 4". Two of the three copies sit inside protected trees and are never import
            // targets (SpellImportGuard308); the chain is still four books, and the targets must not include a protected one.
            // Second guard pass, same day: the original book is skipped too (the protected scene W_Demo_Compact reads it), so
            // it must be among the skipped books; the one target left is the copy W_Demo_Main reads (A12).
            r.Check("A4", books >= 4 && targets.Count + skippedBooks.Count >= 4 && targets.Count >= 1 && !targets.Any(SpellImportGuard308.Protected) &&
                skippedBooks.Contains(SpellTable308.OriginalBook),
                "the original book and its three scene copies were found (" + targets.Count + " targets, " + skippedBooks.Count + " skipped: protected); the original book is skipped, never a target");
            r.Check("A12", targets.Any(p => mainGuids.Contains(AssetDatabase.AssetPathToGUID(p))), "one of the import targets is the book W_Demo_Main reads");
            r.unverified = new[] { "anything that needs Play: real brush input, enemy AI, presentation, the save-based unlock flow" };
        }

        // ---- status ----
        static void Status(Report r)
        {
            // the sheets the last import carried (spell308-import :only= / :skip=), else every sheet: "current" means "what was chosen"
            var choice = SpellTable308.Following(null, out string source);
            var build = SpellTable308.Build(false, out var sheetNames, choice);
            r.notes.Add("selection: " + choice.Text + " (" + source + ")");
            r.notes.Add("sheets: " + string.Join(", ", sheetNames));
            r.notes.Add("table: " + (build.Ok ? "builds, " : "DOES NOT BUILD (" + build.Errors.Count + " errors), ") + "table hash " + build.TableHash);
            foreach (string error in build.Errors.Take(10)) r.notes.Add("  error: " + error);
            // the registry exactly as a combat scope builds it: an effect class the container cannot build is left out there
            // and named here. This line is the gate before Play: a missing handler turns its glyphs into misfires.
            var registry = SpellEffectInstaller308.CreateRegistry();
            var effects = registry.All;
            r.notes.Add("registered handlers (" + effects.Count + "): " + string.Join(" ", effects.Select(e => e.Id).OrderBy(id => id, StringComparer.Ordinal)));
            r.notes.Add("handlers that could not be built: " + (registry.Failures.Count == 0 ? "none" : registry.Failures.Count.ToString()));
            foreach (string failure in registry.Failures) r.notes.Add("  NOT BUILT " + failure);
            r.Check("M1", registry.Failures.Count == 0, "every registered effect handler builds (" + effects.Count + " built, " + registry.Failures.Count + " failed): " + string.Join(" | ", registry.Failures.Take(6)));
            if (build.Ok)
            {
                var built = new HashSet<string>(effects.Select(e => e.Id), StringComparer.Ordinal);
                var orphans = build.Rows.Where(x => x.HasHandler && (x.Feature == SpellLegacyFeature.None || SpellTakeover308.Declared(x)) && !built.Contains(x.Handler)).Select(x => x.Letter + ":" + x.Handler).ToList();
                r.Check("M1", orphans.Count == 0, "every row a handler runs names a handler that was built: " + string.Join(" ", orphans.Take(12)));
                var testOnly = build.Rows.Where(x => x.Gate == SpellGateMode.Test).Select(x => x.Letter + ":" + x.Handler).ToList();
                r.notes.Add("TEST-only rows (Gate test, never opened by the main-game grant of their final): " + (testOnly.Count > 0 ? string.Join(" ", testOnly) : "none"));
            }
            if (build.Ok)
            {
                r.notes.Add("rows: legacy " + build.Rows.Count(x => x.Feature != SpellLegacyFeature.None) + ", registered " + build.Rows.Count(x => x.HasHandler && x.Feature == SpellLegacyFeature.None) +
                    ", reserved " + build.Rows.Count(x => !string.IsNullOrEmpty(x.Pending)) + ", blank " + build.Rows.Count(x => x.Category == SpellCategory.Blank) +
                    ", waiting for a sheet " + build.MissingSheet.Count + (build.MissingSheet.Count > 0 ? " (" + string.Join("", build.MissingSheet) + ")" : ""));
                foreach (var rule in build.Unlocks)
                    r.notes.Add("unlock " + rule.Final + ": ledger " + rule.LedgerId + (string.IsNullOrEmpty(rule.EvidenceId) ? "" : " + evidence " + rule.EvidenceId) + (rule.GrantedInMain ? " (granted in the main game)" : " (TEST unlock only)"));
            }
            var mainGuids = SpellTable308.MainSceneGuids();
            foreach (string path in SpellTable308.Targets())
            {
                var book = AssetDatabase.LoadAssetAtPath<SpellBookSO>(path);
                r.notes.Add((mainGuids.Contains(AssetDatabase.AssetPathToGUID(path)) ? "book [W_Demo_Main] " : "book ") + path + ": " +
                    (book != null && book.HasTable ? "table " + book.TableHash + (build.Ok && book.TableHash == build.TableHash ? " (current)" : " (STALE: run spell308-import)") : "no table yet (resolves from _entries, as before #308)"));
            }
            foreach (string path in SpellTable308.ProtectedBooks())
            {
                var book = AssetDatabase.LoadAssetAtPath<SpellBookSO>(path);
                r.notes.Add("book " + SpellImportGuard308.SkipLabel + " " + (mainGuids.Contains(AssetDatabase.AssetPathToGUID(path)) ? "[W_Demo_Main] " : "") + path + ": " +
                    (book != null && book.HasTable ? "table " + book.TableHash + " (NOT EXPECTED: the importer never writes there)" : "no table (resolves from _entries, as before #308; never an import target)"));
            }
            r.Check("status", true, "status listed");
            r.unverified = new[] { "anything that needs Play; this command builds the handlers and the table, it casts nothing" };
        }

        // ---- fixtures ----
        // A fixture book: the main-scene book's own values with a table built from the named sheets plus extra in-memory rows.
        internal static SpellFixture308 NewFixture(Report r, IEnumerable<ISpellEffect> effects, IEnumerable<string> onlySheets, params SpellSheet308[] extra)
        {
            var build = BuildTable(onlySheets, extra);
            if (!build.Ok) throw new InvalidOperationException("fixture table does not build: " + string.Join(" | ", build.Errors.Take(6)));
            return SpellFixture308.Create(CloneWithTable(FixtureBook(r), build), effects);
        }

        // The book whose values the fixtures clone: the copy W_Demo_Main reads. Found once per command and kept on the report.
        // The main scene file is about 50 MB: one pass per command that keeps nothing (SpellTable308.MainSceneBook), instead
        // of a full read with a guid set for every fixture (a fixtures run builds seventeen).
        static SpellBookSO FixtureBook(Report r)
        {
            if (r.FixtureBook != null) return r.FixtureBook;
            var targets = SpellTable308.Targets();
            if (targets.Count == 0) throw new InvalidOperationException("no spell book to clone: the original book and its copies were not found");
            string path = SpellTable308.MainSceneBook(targets) ?? targets[0];
            var book = AssetDatabase.LoadAssetAtPath<SpellBookSO>(path);
            if (book == null) throw new InvalidOperationException("cannot load the fixture book " + path);
            r.notes.Add("fixture book: " + path);
            r.FixtureBook = book;
            return book;
        }

        internal const string RuleHeader = "글자,Mode,Handler,Pending,Kind,Gate,Feature,Inherit,BasePower,SpeedMul,AreaShape,AreaAngle,AreaRadius,AreaLength,AreaSpeed,AreaDelay,Shots,Interval,Scatter,Params\n";
        internal static readonly string[] FoundationSheets = { "Rules308_Base", "Rules308_WP00" };
        internal static bool Near(float a, float b, float tolerance = .001f) => Mathf.Abs(a - b) <= tolerance;

        const string CheckSource = "Assets/_Project/Scripts/Editor/WorldMacro/Spell120Checks308";

        // The packages run one by one. A package that throws is reported under its own name (how many checks it got through,
        // the exception) and the packages after it still run: the first run of fixtures that never ran must not hide the rest.
        // A lambda per package (not a method group): a package whose partial method is absent compiles to nothing.
        static void Fixtures(Report r)
        {
            var packages = new KeyValuePair<string, Action<Report>>[]
            {
                new KeyValuePair<string, Action<Report>>("foundation", x => FixturesFoundation(x)),
                new KeyValuePair<string, Action<Report>>("WP00", x => FixturesWP00(x)), new KeyValuePair<string, Action<Report>>("WP02", x => FixturesWP02(x)),
                new KeyValuePair<string, Action<Report>>("WP03", x => FixturesWP03(x)), new KeyValuePair<string, Action<Report>>("WP04", x => FixturesWP04(x)),
                new KeyValuePair<string, Action<Report>>("WP05", x => FixturesWP05(x)), new KeyValuePair<string, Action<Report>>("WP06", x => FixturesWP06(x)),
                new KeyValuePair<string, Action<Report>>("WP07", x => FixturesWP07(x)), new KeyValuePair<string, Action<Report>>("WP08", x => FixturesWP08(x)),
                new KeyValuePair<string, Action<Report>>("WP09", x => FixturesWP09(x)), new KeyValuePair<string, Action<Report>>("WP10", x => FixturesWP10(x)),
                new KeyValuePair<string, Action<Report>>("WP11", x => FixturesWP11(x)), new KeyValuePair<string, Action<Report>>("WP12", x => FixturesWP12(x)),
                new KeyValuePair<string, Action<Report>>("WP13", x => FixturesWP13(x)), new KeyValuePair<string, Action<Report>>("WP14", x => FixturesWP14(x)),
            };
            foreach (var package in packages)
            {
                var run = new PackageRun { name = package.Key, sites = CheckSites(package.Key) };
                r.packages.Add(run); r.Current = run;
                int checks = r.checks, failed = r.failed; double began = EditorApplication.timeSinceStartup;
                try { package.Value(r); }
                catch (Exception exception)
                {
                    var cause = exception is TargetInvocationException && exception.InnerException != null ? exception.InnerException : exception;
                    r.failed++; run.error = cause.GetType().Name + ": " + cause.Message;
                    r.lines.Add("FAIL exception  " + package.Key + "  " + cause);
                }
                finally
                {
                    r.Current = null;
                    run.checks = r.checks - checks; run.failed = r.failed - failed;
                    run.seconds = (float)(EditorApplication.timeSinceStartup - began);
                    if (run.error.Length > 0) run.status = "EXCEPTION after " + run.checks + " checks";
                    else if (run.status.Length == 0) run.status = r.Plan ? "asks for no glyph" : "ran (asks for no glyph)";
                    else if (run.status == "selected") run.status = r.Plan ? "would run" : run.failed == 0 ? "ok" : "FAILED";
                }
                r.notes.Add((r.Plan ? "plan " : "package ") + run.name + ": " + run.status + " | glyphs " + (run.glyphs.Length > 0 ? run.glyphs : "-") +
                    (r.Plan ? " | check sites in the source " + (run.sites >= 0 ? run.sites.ToString() : "?")
                        : " | checks " + run.checks + ", failed " + run.failed + ", " + run.seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s") +
                    (run.error.Length > 0 ? " | " + run.error : ""));
            }
            if (r.Plan)
            {
                r.notes.Add("plan only: no fixture, no preview scene, no asset was loaded, no check ran" + (r.Filter.Length > 0 ? " (filter " + r.Filter + ")" : ""));
                r.unverified = new[] { "everything: this is a plan" };
                return;
            }
            r.unverified = new[] { "real brush input and recognition", "enemy AI interplay (telegraph interruption, melee retaliation)",
                "presentation (the fixtures use the null presenter)", "main-scene unlock flow and the isolated test save", "first-person legibility and performance" };
        }

        // How many r.Check( sites a package's source file has (-1 = the file is not where it is expected). A loop runs a site
        // more than once, so a run reports more checks than sites.
        static int CheckSites(string package)
        {
            string path = CheckSource + (package == "foundation" ? "" : "." + package) + ".cs";
            if (!File.Exists(path)) return -1;
            string text = File.ReadAllText(path, Encoding.UTF8);
            if (package == "foundation")
            {
                int from = text.IndexOf("static void FixturesFoundation", StringComparison.Ordinal);
                if (from < 0) return -1;
                text = text.Substring(from);
            }
            int count = 0;
            for (int at = text.IndexOf("r.Check(", StringComparison.Ordinal); at >= 0; at = text.IndexOf("r.Check(", at + 1, StringComparison.Ordinal)) count++;
            return count;
        }

        static partial void FixturesWP00(Report r);
        static partial void FixturesWP02(Report r);
        static partial void FixturesWP03(Report r);
        static partial void FixturesWP04(Report r);
        static partial void FixturesWP05(Report r);
        static partial void FixturesWP06(Report r);
        static partial void FixturesWP07(Report r);
        static partial void FixturesWP08(Report r);
        static partial void FixturesWP09(Report r);
        static partial void FixturesWP10(Report r);
        static partial void FixturesWP11(Report r);
        static partial void FixturesWP12(Report r);
        static partial void FixturesWP13(Report r);
        static partial void FixturesWP14(Report r);

        // A handler the foundation checks drive: it records what the host asked of it and can be told to refuse.
        sealed class ProbeEffect : ISpellEffect, ISpellCostHook, ISpellCastHook, ISpellAcceptHook, ISpellHitHook, IPlayerHitHook
        {
            public const string HandlerId = "test.probe";
            static readonly SpellParamSpec[] Specs = { new SpellParamSpec("probe.value", 0f, 100f, false) };
            public string Id => HandlerId;
            public IReadOnlyList<SpellParamSpec> Params => Specs;
            public bool RefusePrepare, RefuseCommit, KeepLetter;
            public float Cost = 1f, PowerScale = 1f;
            public int Prepared, Committed, Ticks, Cleared, Accepts, EnemyHits, PlayerHits;
            public float InkAtPrepare, InkAtCommit, Value;
            public EnemyVitals LastAttacker;
            public bool Prepare(in SpellCastContext ctx) { Prepared++; InkAtPrepare = ctx.Host.Ink.Value; return !RefusePrepare; }
            public bool Commit(in SpellCastContext ctx)
            {
                Committed++; InkAtCommit = ctx.Host.Ink.Value; Value = ctx.Row.F("probe.value", -1f);
                if (RefuseCommit) return false;
                ctx.Host.PlanSingle(ctx.Cast, false);
                return true;
            }
            public void Tick(in SpellTickContext ctx) { Ticks++; }
            public void Clear(SpellClearReason reason) { Cleared++; }
            public float CostScale(in SpellCastInfo cast, float now) => Cost;
            public void ModifyCast(ref SpellCastDraft draft, float now) { draft.Power *= PowerScale; }
            public void OnCastAccepted(in SpellCastContext ctx) { Accepts++; }
            public void OnEnemyHit(in EnemyDamageResult result, in SpellTickContext ctx) { EnemyHits++; }
            public void OnPlayerHit(in PlayerHitInfo hit, in SpellTickContext ctx, ref bool interruptsDrawing)
            { PlayerHits++; LastAttacker = hit.Attacker; if (KeepLetter) interruptsDrawing = false; }
        }

        static void FixturesFoundation(Report r)
        {
            // grid cells used below, by index (no glyph is named): 1 = wood single, 7 = wood parry, 13 = wood circle, 19 = wood ward,
            // 21 = a blank cell, 6 = a reserved cell, 27 / 3 / 5 = cells without a row in the foundation sheets
            char single = SpellGrammar308.LetterAt(1), parry = SpellGrammar308.LetterAt(7), circle = SpellGrammar308.LetterAt(13), ward = SpellGrammar308.LetterAt(19);
            char blank = SpellGrammar308.LetterAt(21), reserved = SpellGrammar308.LetterAt(6);
            char coreRow = SpellGrammar308.LetterAt(27), probeRow = SpellGrammar308.LetterAt(3), ghostRow = SpellGrammar308.LetterAt(5);
            if (!r.Wants(new string(new[] { single, parry, circle, ward, blank, reserved, coreRow, probeRow, ghostRow }))) return;
            var probe = new ProbeEffect();
            var effects = SpellEffectInstaller308.CreateAll().Concat(new ISpellEffect[] { probe }).ToArray();
            var test = new SpellSheet308("Rules308_ZZFixture", RuleHeader +
                coreRow + ",,core.single,,AttackSingle,,,,22,,,,,,,,,,,\n" +
                probeRow + "," + ",test.probe,,AttackSingle,,,,10,,,,,,,,,,,cost=2;probe.value=7\n" +
                ghostRow + ",,test.missing,,AttackSingle,,,,10,,,,,,,,,,,\n");
            using (var f = NewFixture(r, effects, FoundationSheets, test))
            {
                float misfire = f.Config.MisfireInkCost, cost = f.Config.SpellInkCost, brush = f.Brush();
                void Misfire(string clause, char letter, SpellResolveStatus expected, string text)
                {
                    f.RestoreAll(); float before = f.Ink.Value; f.Cast(letter);
                    r.Check(clause, Near(before - f.Ink.Value, misfire) && f.Misfires.Count == 1 && f.Misfires[0].Key == letter && f.Misfires[0].Value == expected &&
                        f.Accepted.Count == 0 && f.Plans.Count == 0 && f.PendingCount == 0,
                        text + ": ink -" + misfire + ", one CastMisfired(" + expected + "), no effect  [got " + (f.Misfires.Count > 0 ? f.Misfires[0].Value.ToString() : "none") + ", ink " + (before - f.Ink.Value) + "]");
                }
                Misfire("A6", blank, SpellResolveStatus.Blank, "blank glyph");
                Misfire("A6", reserved, SpellResolveStatus.Unimplemented, "reserved glyph");
                Misfire("A6", coreRow, SpellResolveStatus.Locked, "glyph behind a locked final");
                Misfire("A6", ghostRow, SpellResolveStatus.Unavailable, "row whose handler is not registered");
                Misfire("A6", ward, SpellResolveStatus.Unavailable, "legacy feature that is switched off (no ward runtime)");
                Misfire("A6", 'A', SpellResolveStatus.NotInVocabulary, "character outside the vocabulary");

                // a new row of a basic shape, final unlocked: today's single-target judgement
                f.RestoreAll(); f.Finals.Open.Add(SpellFinal.Nieun); f.Aim(f.Inside[0]);
                float ink = f.Ink.Value, hp = f.Inside[0].Hp; f.Cast(coreRow);
                r.Check("A6", f.Misfires.Count == 0 && f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 &&
                    f.Plans[0].Hits[0].Target == f.Inside[0] && Near(f.Plans[0].Hits[0].Power, 22f * brush) && f.Plans[0].Hits[0].AttackId > 0,
                    "unlocked new row (core.single): one spell cost, one accepted cast, one scheduled hit of base power x brush with an attack id");
                f.Land();
                r.Check("A6", Near(hp - f.Inside[0].Hp, 22f * brush) && f.Hits.Count == 1 && f.Hits[0].Attack.AttackId == f.Plans[0].Hits[0].AttackId && probe.EnemyHits == 1,
                    "the scheduled hit lands through the wiring's own tick with the planned attack id; the hit hook hears it once");

                // handler route: Prepare -> ink -> Commit -> accept
                f.RestoreAll(); probe.RefusePrepare = true; ink = f.Ink.Value; f.Cast(probeRow);
                r.Check("A6", probe.Prepared == 1 && probe.Committed == 0 && f.Ink.Value == ink && f.Misfires.Count == 0 && f.Accepted.Count == 0,
                    "a refused Prepare is no cast: no ink at all (not even misfire ink), no Commit");
                probe.RefusePrepare = false; probe.RefuseCommit = true; f.Cast(probeRow);
                r.Check("A6", probe.Committed == 1 && Near(probe.InkAtPrepare - probe.InkAtCommit, cost * 2f) && Near(f.Ink.Value, ink) && f.Accepted.Count == 0,
                    "ink is spent between Prepare and Commit (row cost x2) and restored when Commit refuses");
                probe.RefuseCommit = false; int accepts = probe.Accepts; f.Cast(probeRow);
                r.Check("A6", Near(ink - f.Ink.Value, cost * 2f) && f.Accepted.Count == 1 && probe.Accepts == accepts + 1 && probe.Value == 7f && f.Plans.Count == 1,
                    "a committed cast costs the row's ink once, raises CastAccepted and the accept hook once, and reads its parameter from the row");
                f.RestoreAll(); f.Ink.Restore(cost * .5f); ink = f.Ink.Value; int prepared = probe.Prepared; f.Cast(probeRow);
                r.Check("A6", probe.Prepared == prepared + 1 && f.Ink.Value == ink && f.Accepted.Count == 0 && f.Misfires.Count == 0, "not enough ink: no cast, the remaining ink is untouched");

                // the 36 existing glyphs in the same wiring: cost, plan and power as before
                f.RestoreAll(); f.Aim(f.Inside[0]); ink = f.Ink.Value; f.Cast(single);
                f.Book.TryGet(single, out var entry);
                r.Check("A4", Near(ink - f.Ink.Value, cost) && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && Near(f.Plans[0].Hits[0].Power, entry.BasePower * brush) &&
                    f.Accepted.Count == 1 && f.Accepted[0].Kind == SpellKind.AttackSingle, "existing single-target glyph: book base power x brush, one scheduled hit, one spell cost");
                f.RestoreAll(); f.Aim(f.Inside[0]); f.Cast(circle);
                var spikes = AreaSpikeSpec.Legacy;
                r.Check("A4", f.Plans.Count == 1 && f.Plans[0].Area != null && f.Plans[0].Area.Spikes.Count == spikes.Count &&
                    f.Plans[0].Hits.All(h => Near(h.ImpactTime - f.Plans[0].Area.CreatedAt, AreaSpikePlanner.NearestRise(f.Plans[0].Area, h.Target.transform.position))),
                    "existing circle glyph: the staggered spike plan, each enemy hit at its nearest spike's rise time");
                f.RestoreAll(); ink = f.Ink.Value; f.Cast(parry);
                r.Check("A4", Near(ink - f.Ink.Value, f.Config.ParryInkCost) && f.Accepted.Count == 1 && f.Accepted[0].Kind == SpellKind.Parry, "existing parry glyph: parry cost, accepted as a parry");

                // hooks: cost, cast, player hit (no reward), frame tick, clear
                f.RestoreAll(); f.Aim(f.Inside[0]); probe.Cost = .5f; probe.PowerScale = 2f; ink = f.Ink.Value; f.Cast(single);
                r.Check("A10", Near(ink - f.Ink.Value, cost * .5f) && Near(f.Plans[0].Hits[0].Power, entry.BasePower * brush * 2f),
                    "cost hook scales the ink cost and cast hook scales the power of an existing glyph (only while a hook says so)");
                probe.Cost = 1f; probe.PowerScale = 1f;
                float playerHp = f.Player.Hp01 * f.Player.MaxHp; int playerHits = probe.PlayerHits; ink = f.Ink.Value;
                bool struck = EnemyStrike308.Deliver(f.Player, f.Inside[1], 5f, IncomingDamageKind.Melee);
                r.Check("A10", struck && Near(playerHp - f.Player.Hp01 * f.Player.MaxHp, 5f) && f.Player.LastHit.Attacker == f.Inside[1] && f.Ink.Value == ink,
                    "an enemy strike through EnemyStrike308 costs exactly its damage, names its attacker, and gives the player nothing");
                // The wiring subscribes to PlayerVitals.Damaged in OnEnable, which Edit Mode never runs: deliver the same call by hand.
                SpellFixture308.Call(f.Wiring, "OnPlayerDamaged", 5f);
                r.Check("A10", probe.PlayerHits == playerHits + 1 && probe.LastAttacker == f.Inside[1] && f.Ink.Value == ink && Near(playerHp - f.Player.Hp01 * f.Player.MaxHp, 5f),
                    "the player-hit hook hears the hit with its attacker; nothing is healed or refunded");
                int cleared = probe.Cleared; f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                r.Check("A6", probe.Cleared == cleared + 1, "ClearSpells308 reaches every effect");
            }

            // a wiring without the registry (old scenes, old checks): the 36 behave the same and every new row is Unavailable
            using (var f = NewFixture(r, null, FoundationSheets, test))
            {
                f.Finals.Open.Add(SpellFinal.Nieun); f.Aim(f.Inside[0]); f.Cast(coreRow);
                r.Check("A6", f.Misfires.Count == 1 && f.Misfires[0].Value == SpellResolveStatus.Unavailable, "no registry injected: a new row is Unavailable");
                f.RestoreAll(); f.Aim(f.Inside[0]); f.Cast(single);
                r.Check("A4", f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && f.Accepted.Count == 1, "no registry injected: an existing glyph casts as before");
            }
        }
    }
}
