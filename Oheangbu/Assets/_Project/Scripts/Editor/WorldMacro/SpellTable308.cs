using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.Demo;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 spell table importer (SPEC-SPELL-120-308 section 6). Edit Mode only, never a dialog (queue safe).
    /// Reads  Docs/오행부_작도어휘_v0_1.csv (canonical, never written) and Assets/_Project/Data/Spells/Rules308_*.csv, Unlock308.csv,
    ///        SummonPower308.csv.
    /// Writes only its own fields: SpellBookSO._rows / _unlocks / _rowsHash / _tableHash of the books of the copy chain that
    ///        SpellImportGuard308 leaves open. The chain = the main spell book and every scene copy of it (file name
    ///        607113b4..._SpellBook_WorldMacroSummon_TEST.asset, or ending in _607113b4..._SpellBook_WorldMacroSummon_TEST.asset).
    ///        One book of the four is open: the copy under Art/World/Architecture296/Data, the one W_Demo_Main reads. An import
    ///        whose targets do not include the book W_Demo_Main references writes nothing.
    ///        _entries is never touched. Each target is saved with AssetDatabase.SaveAssetIfDirty (never SaveAssets).
    ///        A protected book or summon profile (SpellImportGuard308: the trees Watershed295*, Reworld292*, MountainTrail285*;
    ///        the files W_Demo_Compact.unity, 03_Content.asset; and what the protected scene W_Demo_Compact.unity reads, which
    ///        is the original book in Data/World and the profiles under Art/Demo/Summons) is never a target: dry, apply, verify
    ///        and revert list it as "SKIPPED (protected)" and it keeps resolving from its own _entries, as before #308.
    ///        Nothing is written unless every target loads and every target's own _entries resolve exactly as the table does for
    ///        the glyphs that cast today (SpellTableBuilder308.LegacyDrift): an import must not change an existing spell.
    /// Commands (Execute argument):
    ///   spell308-import[:dry][:strict][:takeover][:only=<packages>][:skip=<glyphs>][:all]
    ///                                    build the table and write it to every target book (dry = report only;
    ///                                    strict = fail while assigned glyphs still wait for a rule sheet; takeover = accept rows
    ///                                    that hand a glyph that casts today to a registered handler: a declared behaviour change,
    ///                                    which includes the WP-11 handover rows that keep their legacy feature)
    ///        the deployment switch       only=WP00+WP03+... = Rules308_Base plus the named package sheets and nothing else
    ///                                    (only=base = the legacy 36 alone); skip=<glyphs> = those glyphs keep their Base row
    ///                                    even when their package is chosen; all = every sheet in the folder. A glyph left out
    ///                                    stays the misfire it was before #308. The selection of the last import is remembered
    ///                                    (Import/selection308.json): verify, status and catalog follow it, and an import
    ///                                    without a selection is refused while a narrower one is in force (say it again, or :all).
    ///        the dry run                 names the glyphs whose behaviour changes in a NORMAL save (no TEST unlock): handed
    ///                                    over, new from the start, new after a main-game unlock, gone, changed numbers.
    ///   spell308-revert[:dry]            put back what the last import replaced (first import: back to "no table")
    ///   spell308-report                  the books of the project and what they carry
    ///   spell308-verify[:strict][:only=..][:skip=..][:all]
    ///                                    the target books carry exactly the table the selected sheets build now, and no
    ///                                    protected book of the chain carries a table
    ///   spell308-summon-power[:dry]      WP-00: SummonPower308.csv -> BasePower of every SummonCombatProfile the guard leaves open
    ///   spell308-catalog[:dry]           WP-00: VFX120_Catalog.GameplayConnected = "the glyph has a handler" (stale flags)
    /// Reports: Art/SpellVFX120/Spell120_308/Import/ (repo root).</summary>
    public static class SpellTable308
    {
        const string CsvFromRepo = "Docs/오행부_작도어휘_v0_1.csv";
        const string SheetFolder = "Assets/_Project/Data/Spells";
        // The head of the copy chain. Never a target (SpellImportGuard308.ProtectedPaths): the protected scene W_Demo_Compact reads it.
        internal const string OriginalBook = "Assets/_Project/Data/World/SpellBook_WorldMacroSummon_TEST.asset";
        // A scene copy keeps the copied asset's guid in its file name: "<guid of the original>_SpellBook_...asset" for the first
        // copy (Reworld292), "<guid of that copy>_<guid of the original>_SpellBook_...asset" for a copy of a copy.
        const string CopyName = "607113b45769a524983ba10b2d5e1a71_SpellBook_WorldMacroSummon_TEST.asset";
        const string MainScene = "Assets/_Project/Scenes/World/W_Demo_Main.unity";
        const string CatalogPath = "Assets/_Project/Art/SpellVFX120/Data/VFX120_Catalog.asset";
        const string OutFromRepo = "Art/SpellVFX120/Spell120_308/Import";
        const string BaseSheet = "Rules308_Base", SheetPrefix = "Rules308_", SelectionFile = "selection308.json";

        // Which rule sheets and rows a table carries: the deployment switch. Rules308_Base (the 36 glyphs that cast before
        // #308, and the reserved rows) is always in. A glyph that is left out keeps what Base says about it: a misfire.
        [Serializable]
        internal sealed class Selection
        {
            public bool all = true;                                  // every Rules308_*.csv of the folder
            public List<string> only = new List<string>();           // package sheets, full names (Rules308_WP03)
            public string skip = "";                                 // glyphs whose package rows are left out
            public string time = "", tableHash = "";
            [NonSerialized] public string Problem;

            public bool IsDefault => all && string.IsNullOrEmpty(skip);
            public bool Takes(string sheet) => all || sheet == BaseSheet || only.Contains(sheet);
            public string Text => (all ? "every sheet" : only.Count == 0 ? "only base (the legacy 36)" : "only " + string.Join("+", only.Select(n => n.Substring(SheetPrefix.Length)))) +
                (string.IsNullOrEmpty(skip) ? "" : ", skip " + skip);
            public string Argument => (all ? ":all" : ":only=" + (only.Count == 0 ? "base" : string.Join("+", only.Select(n => n.Substring(SheetPrefix.Length))))) +
                (string.IsNullOrEmpty(skip) ? "" : ":skip=" + skip);

            // given = the argument names a selection (only=, skip= or all).
            public static Selection Parse(string[] parts, out bool given)
            {
                var choice = new Selection(); given = false;
                foreach (string raw in parts)
                {
                    string part = raw.Trim();
                    if (part == "all") { given = true; choice.all = true; choice.only.Clear(); }
                    else if (part.StartsWith("only=", StringComparison.Ordinal))
                    {
                        given = true; choice.all = false;
                        foreach (string token in part.Substring(5).Split(new[] { '+', ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string name = token.Trim();
                            if (name.Equals("base", StringComparison.OrdinalIgnoreCase) || name == BaseSheet) continue;
                            if (name.StartsWith(SheetPrefix, StringComparison.Ordinal)) name = name.Substring(SheetPrefix.Length);
                            if (name.Length > 0 && char.IsDigit(name[0])) name = "WP" + name;
                            name = SheetPrefix + name.ToUpperInvariant();
                            if (!File.Exists(Path.Combine(SheetFolder, name + ".csv"))) { choice.Problem = "no such rule sheet: " + SheetFolder + "/" + name + ".csv"; return choice; }
                            if (!choice.only.Contains(name)) choice.only.Add(name);
                        }
                        choice.only.Sort(StringComparer.Ordinal);
                    }
                    else if (part.StartsWith("skip=", StringComparison.Ordinal))
                    {
                        given = true;
                        foreach (char glyph in part.Substring(5))
                        {
                            if (char.IsWhiteSpace(glyph) || glyph == '+' || glyph == ',') continue;
                            if (SpellGrammar308.IndexOf(glyph) == 0) { choice.Problem = "skip names a character outside the 120-glyph grid: U+" + ((int)glyph).ToString("X4"); return choice; }
                            if (choice.skip.IndexOf(glyph) < 0) choice.skip += glyph;
                        }
                    }
                }
                return choice;
            }
        }

        // The selection the last import carried (null = no import recorded one).
        internal static Selection Recorded()
        {
            string file = Path.Combine(RepoRoot, OutFromRepo, SelectionFile);
            if (!File.Exists(file)) return null;
            try { return JsonUtility.FromJson<Selection>(File.ReadAllText(file, Encoding.UTF8)); }
            catch (Exception) { return null; }
        }

        // The selection a read-only command follows: the one in its argument, else the recorded one, else every sheet.
        internal static Selection Following(string[] parts, out string source)
        {
            var choice = Selection.Parse(parts ?? Array.Empty<string>(), out bool given);
            if (given) { source = "argument"; return choice; }
            var recorded = Recorded();
            if (recorded != null) { source = "last import"; return recorded; }
            source = "default"; return choice;
        }

        // A package sheet without the rows of the skipped glyphs (a rule row starts with its glyph and a comma).
        static string WithoutRows(string sheet, string skip)
        {
            if (string.IsNullOrEmpty(skip)) return sheet;
            var b = new StringBuilder(sheet.Length);
            int at = 0;
            while (at < sheet.Length)
            {
                int end = sheet.IndexOf('\n', at);
                int next = end < 0 ? sheet.Length : end + 1;
                int first = at < sheet.Length && sheet[at] == '\uFEFF' ? at + 1 : at;
                bool drop = first + 1 < next && sheet[first + 1] == ',' && skip.IndexOf(sheet[first]) >= 0;
                if (!drop) b.Append(sheet, at, next - at);
                at = next;
            }
            return b.ToString();
        }

        [Serializable] sealed class BookState
        {
            public string path = "", rowsHash = "", tableHash = "";
            public SpellRow[] rows = Array.Empty<SpellRow>();
            public SpellUnlockRule[] unlocks = Array.Empty<SpellUnlockRule>();
        }
        [Serializable] sealed class Backup
        {
            public string time = "";
            public List<BookState> books = new List<BookState>();
            public bool hadSelection; public Selection selection = new Selection();   // the selection in force before this import
        }

        public static string Execute(string argument)
        {
            string[] parts = (argument ?? "").Trim().Split(':');
            bool dry = parts.Contains("dry"), strict = parts.Contains("strict"), takeover = parts.Contains("takeover");
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: " + parts[0] + " is Edit Mode only";
            if (EditorApplication.isCompiling) return "refused: scripts are compiling; call again when done";
            switch (parts[0])
            {
                case "spell308-import": return Import(dry, strict, takeover, parts);
                case "spell308-revert": return Revert(dry);
                case "spell308-report": return Report();
                case "spell308-verify": return Verify(strict, parts);
                case "spell308-summon-power": return SummonPower(dry);
                case "spell308-catalog": return Catalog(dry);
            }
            return "refused: expected spell308-import[:dry][:strict][:takeover][:only=<packages>][:skip=<glyphs>][:all] | spell308-revert[:dry] | spell308-report | " +
                "spell308-verify[:strict][:only=..][:skip=..][:all] | spell308-summon-power[:dry] | spell308-catalog[:dry]";
        }

        // ---- shared ----
        internal static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        static string OutFolder { get { string folder = Path.Combine(RepoRoot, OutFromRepo); Directory.CreateDirectory(folder); return folder; } }

        static string Write(string name, List<string> lines)
        {
            string text = string.Join("\n", lines);
            File.WriteAllText(Path.Combine(OutFolder, name), text + "\n", new UTF8Encoding(false));
            return text;
        }

        // The table the sheets build right now. catalog = parameter keys of the registered effect handlers.
        // choice = which sheets and rows (null = every sheet of the folder).
        internal static SpellBuildResult308 Build(bool strict, out List<string> sheetNames, Selection choice = null)
        {
            string csv = Path.Combine(RepoRoot, CsvFromRepo);
            if (!File.Exists(csv)) throw new FileNotFoundException("Missing " + csv);
            var sheets = new List<SpellSheet308>();
            if (Directory.Exists(SheetFolder))
                foreach (string path in Directory.GetFiles(SheetFolder, "Rules308_*.csv"))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (choice != null && !choice.Takes(name)) continue;
                    string text = File.ReadAllText(path, Encoding.UTF8);
                    sheets.Add(new SpellSheet308(name, choice != null && name != BaseSheet ? WithoutRows(text, choice.skip) : text));
                }
            sheetNames = sheets.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            string unlock = Path.Combine(SheetFolder, "Unlock308.csv");
            var specs = new Dictionary<string, IReadOnlyList<SpellParamSpec>>(StringComparer.Ordinal);
            foreach (var effect in SpellEffectInstaller308.CreateAll()) specs[effect.Id] = effect.Params ?? Array.Empty<SpellParamSpec>();
            return SpellTableBuilder308.Build(File.ReadAllText(csv, Encoding.UTF8), sheets, File.Exists(unlock) ? File.ReadAllText(unlock, Encoding.UTF8) : "",
                id => specs.TryGetValue(id, out var list) ? list : null, strict);
        }

        // The books an import writes: the books of the copy chain (the original and every scene copy of the main spell book,
        // original first) WITHOUT the protected ones. Since the second guard pass the original itself is protected, so this is
        // the Architecture296 copy alone. Every write of this file goes through this list (and SpellImportGuard308.Demand).
        internal static List<string> Targets() => SpellImportGuard308.Writable(Chain(), null);

        // The books of the copy chain an import leaves alone because they are protected (a protected tree, or the original
        // book, which the protected scene W_Demo_Compact reads).
        internal static List<string> ProtectedBooks()
        {
            var skipped = new List<string>();
            SpellImportGuard308.Writable(Chain(), skipped);
            return skipped;
        }

        // The original and every scene copy of the main spell book, original first (protected or not: read-only uses).
        internal static List<string> Chain()
        {
            var list = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:SpellBookSO"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == OriginalBook || IsCopyOfOriginal(path)) list.Add(path);
            }
            list.Sort((a, b) => a == b ? 0 : a == OriginalBook ? -1 : b == OriginalBook ? 1 : string.CompareOrdinal(a, b));
            return list;
        }

        // The file name is CopyName itself (the first copy) or ends with "_" + CopyName (a copy of a copy). Nothing else is a target.
        internal static bool IsCopyOfOriginal(string path)
        {
            string name = Path.GetFileName(path ?? "");
            return name == CopyName || name.EndsWith("_" + CopyName, StringComparison.Ordinal);
        }

        internal static SpellBookSO.Entry[] EntriesOf(SpellBookSO book)
        {
            var field = typeof(SpellBookSO).GetField("_entries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return (SpellBookSO.Entry[])field?.GetValue(book) ?? Array.Empty<SpellBookSO.Entry>();
        }

        // Asset guids the main scene file references (one pass over the scene text).
        internal static HashSet<string> MainSceneGuids()
        {
            var guids = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(MainScene)) return guids;
            const string marker = "guid: ";
            foreach (string line in File.ReadLines(MainScene))
            {
                int at = line.IndexOf(marker, StringComparison.Ordinal);
                if (at >= 0 && line.Length >= at + marker.Length + 32) guids.Add(line.Substring(at + marker.Length, 32));
            }
            return guids;
        }

        // The target book W_Demo_Main references: the first of the targets (in their own order, as the importer names it)
        // whose guid the scene text carries. One pass over the scene file that keeps nothing and stops once every target was
        // seen (null = none of them, or no scene file).
        internal static string MainSceneBook(IReadOnlyList<string> targets)
        {
            if (targets == null || targets.Count == 0 || !File.Exists(MainScene)) return null;
            var guids = new string[targets.Count]; var seen = new bool[targets.Count];
            for (int i = 0; i < targets.Count; i++) guids[i] = AssetDatabase.AssetPathToGUID(targets[i]) ?? "";
            const string marker = "guid: ";
            int left = targets.Count;
            foreach (string line in File.ReadLines(MainScene))
            {
                int at = line.IndexOf(marker, StringComparison.Ordinal);
                if (at < 0 || line.Length < at + marker.Length + 32) continue;
                for (int i = 0; i < guids.Length; i++)
                {
                    if (seen[i] || guids[i].Length != 32 || string.CompareOrdinal(line, at + marker.Length, guids[i], 0, 32) != 0) continue;
                    seen[i] = true; left--;
                }
                if (left == 0 || seen[0]) break;   // the first target wins whatever else the scene names
            }
            for (int i = 0; i < seen.Length; i++) if (seen[i]) return targets[i];
            return null;
        }

        static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        internal static string RowText(SpellRow row)
        {
            if (row == null) return "";
            var b = new StringBuilder();
            b.Append(row.Index).Append('|').Append(row.Letter).Append('|').Append(row.Element).Append('|').Append(row.Medial).Append('|').Append(row.Final).Append('|')
                .Append(row.Category).Append('|').Append(row.Kind).Append('|').Append(row.Handler).Append('|').Append(row.Pending).Append('|').Append(row.Gate).Append('|')
                .Append(row.Feature).Append('|').Append(F(row.BasePower)).Append('|').Append(F(row.ProjectileSpeedMul)).Append('|').Append(row.AreaShape).Append('|')
                .Append(F(row.AreaAngle)).Append('|').Append(F(row.AreaRadius)).Append('|').Append(F(row.AreaLength)).Append('|').Append(F(row.AreaSpeed)).Append('|')
                .Append(F(row.AreaImpactDelay)).Append('|').Append(row.VolleyShots).Append('|').Append(F(row.VolleyInterval)).Append('|').Append(row.ScatterVolley ? 1 : 0).Append('|');
            if (row.Params != null) foreach (var p in row.Params) b.Append(p.Key).Append('=').Append(F(p.Value)).Append(';');
            return b.ToString();
        }

        static string UnlockText(SpellUnlockRule rule) => rule.Final + "|" + rule.LedgerId + "|" + rule.EvidenceId + "|" + (rule.GrantedInMain ? 1 : 0);

        // _entries as serialized text: the importer proves it leaves them alone.
        static string EntriesText(SpellBookSO book)
        {
            string json = JsonUtility.ToJson(book);
            int from = json.IndexOf("\"_entries\":", StringComparison.Ordinal), to = json.IndexOf(",\"_rows\":", StringComparison.Ordinal);
            return from >= 0 && to > from ? json.Substring(from, to - from) : json;
        }

        static BookState StateOf(string path, SpellBookSO book)
        {
            // JSON round trip = a private deep copy (rows are classes)
            var state = new BookState { path = path, rowsHash = book.RowsHash, tableHash = book.TableHash, rows = book.Rows.ToArray(), unlocks = book.Unlocks.ToArray() };
            return JsonUtility.FromJson<BookState>(JsonUtility.ToJson(state));
        }

        // ---- spell308-import ----
        static string Import(bool dry, bool strict, bool acceptTakeovers, string[] parts)
        {
            var log = new List<string>();
            var choice = Selection.Parse(parts, out bool given);
            var recorded = Recorded();
            log.Add("spell308-import" + (dry ? ":dry" : "") + (strict ? ":strict" : "") + (acceptTakeovers ? ":takeover" : "") + (given ? choice.Argument : ""));
            if (choice.Problem != null) { log.Add("refused: " + choice.Problem + "; nothing was written"); return string.Join("\n", log); }
            // the import is the switch: a narrower selection in force is never widened by an import that does not say so
            if (!given && recorded != null && !recorded.IsDefault)
            {
                log.Add("refused: the last import carried '" + recorded.Text + "'. Say that selection again (" + recorded.Argument + ") or pass :all for every sheet; nothing was written");
                return string.Join("\n", log);
            }
            var build = Build(strict, out var sheetNames, choice);
            log.Add("SELECTION " + choice.Text + (given ? "" : " (no selection in the argument)"));
            log.Add("SHEETS " + string.Join(", ", sheetNames));
            log.Add("SOURCE " + build.SourceHash); log.Add("TABLE  " + build.TableHash);
            log.Add("ROWS legacy " + build.Rows.Count(r => r.Feature != SpellLegacyFeature.None) + " / registered " + build.Rows.Count(r => r.HasHandler && r.Feature == SpellLegacyFeature.None) +
                " / reserved " + build.Rows.Count(r => !string.IsNullOrEmpty(r.Pending)) + " / blank " + build.Rows.Count(r => r.Category == SpellCategory.Blank) +
                " / waiting for a sheet " + build.MissingSheet.Count);
            foreach (string note in build.Notes) log.Add("NOTE " + note);
            if (!build.Ok)
            {
                foreach (string error in build.Errors) log.Add("ERROR " + error);
                log.Add("FAILED: the table does not build; nothing was written");
                return Write("import_failed.txt", log);
            }
            var targets = Targets(); var mainGuids = MainSceneGuids(); var skippedBooks = ProtectedBooks();
            foreach (string path in skippedBooks)
            {
                var book = AssetDatabase.LoadAssetAtPath<SpellBookSO>(path);
                log.Add(SpellImportGuard308.SkipLabel + " " + (mainGuids.Contains(AssetDatabase.AssetPathToGUID(path)) ? "[W_Demo_Main references it] " : "") + path +
                    ": never a target; " + (book != null && book.HasTable ? "carries table " + book.TableHash + " (not written by this importer)" : "no table, resolves from _entries as before #308"));
            }
            // The chain starts at the original book: it must be there, as a target or (since the second guard pass) as a
            // skipped book. An import without any target has nothing to do.
            if (!targets.Contains(OriginalBook) && !skippedBooks.Contains(OriginalBook)) { log.Add("FAILED: the original book " + OriginalBook + " was not found; nothing was written"); return Write("import_failed.txt", log); }
            if (targets.Count == 0) { log.Add("FAILED: none of the " + skippedBooks.Count + " books of the copy chain is an import target (every one is protected); nothing was written"); return Write("import_failed.txt", log); }

            // Pass 1 (reads only): every target must load, and the table must not change a glyph that casts from that book today.
            var books = new List<SpellBookSO>(); var refusals = new List<string>();
            // A handover row keeps its legacy feature (so LegacyDrift below sees no difference), but with the effect registry
            // present its judgement moves to the registered handler: the same declared behaviour change as a takeover.
            var known = new HashSet<string>(SpellEffectInstaller308.CreateAll().Select(e => e.Id), StringComparer.Ordinal);
            foreach (var row in build.Rows)
            {
                if (!SpellTakeover308.Declared(row) || !known.Contains(row.Handler)) continue;
                log.Add("TAKEOVER " + row.Letter + ": casts today as a " + row.Feature + " spell (" + SpellTableBuilder308.CoreHandler(row.Kind, row.AreaShape) + ") -> judged by the registered handler " + row.Handler +
                    " in every scene whose wiring carries the effect registry");
                if (!acceptTakeovers) refusals.Add(row.Letter + ": handover of a glyph that casts today to " + row.Handler + "; pass :takeover to accept this declared change, or leave its sheet / row out (:only=, :skip=)");
            }
            foreach (string path in targets)
            {
                var book = AssetDatabase.LoadAssetAtPath<SpellBookSO>(path);
                books.Add(book);
                if (book == null) { refusals.Add("cannot load " + path); continue; }
                var drift = new List<string>(); var takeovers = new List<string>();
                SpellTableBuilder308.LegacyDrift(build.Rows, EntriesOf(book), drift, takeovers);
                foreach (string line in drift) refusals.Add(path + ": " + line);
                foreach (string line in takeovers)
                {
                    log.Add("TAKEOVER " + path + ": " + line);
                    if (!acceptTakeovers) refusals.Add(path + ": takeover of a glyph that casts today (" + line + "); pass :takeover to accept this declared change");
                }
            }
            if (!targets.Any(p => mainGuids.Contains(AssetDatabase.AssetPathToGUID(p))))
                refusals.Add("W_Demo_Main references none of the " + targets.Count + " target books: the import would not reach the game" +
                    (skippedBooks.Any(p => mainGuids.Contains(AssetDatabase.AssetPathToGUID(p))) ? " (the book it reads is protected and is never written)" : ""));
            // What a NORMAL save (the campaign, no TEST unlock) would feel: against the book W_Demo_Main reads, as it is now.
            {
                int main = targets.FindIndex(p => mainGuids.Contains(AssetDatabase.AssetPathToGUID(p)));
                var reference = books[main >= 0 ? main : 0];
                if (reference != null) NormalSaveChanges(log, build, reference, targets[main >= 0 ? main : 0], known);
            }
            if (refusals.Count > 0)
            {
                foreach (string refusal in refusals.Take(60)) log.Add("REFUSED " + refusal);
                if (!dry) { log.Add("FAILED: " + refusals.Count + " refusal(s); nothing was written"); return Write("import_failed.txt", log); }
            }

            // Pass 2: report, and write unless dry.
            var backup = new Backup { time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), hadSelection = recorded != null };
            if (recorded != null) backup.selection = recorded;
            int written = 0;
            for (int t = 0; t < targets.Count; t++)
            {
                string path = targets[t]; var book = books[t];
                if (book == null) continue;   // only reachable in a dry run (listed above as REFUSED)
                bool main = mainGuids.Contains(AssetDatabase.AssetPathToGUID(path));
                var changed = new List<string>();
                for (int i = 0; i < build.Rows.Length; i++)
                {
                    string before = book.HasTable ? RowText(book.Rows[i]) : "";
                    if (before != RowText(build.Rows[i])) changed.Add(build.Rows[i].Letter);
                }
                bool same = book.HasTable && changed.Count == 0 && book.RowsHash == build.SourceHash && book.TableHash == build.TableHash &&
                    book.Unlocks.Select(UnlockText).SequenceEqual(build.Unlocks.Select(UnlockText));
                log.Add((main ? "BOOK [the book W_Demo_Main reads] " : "BOOK ") + path);
                log.Add("  before: " + (book.HasTable ? "table " + book.TableHash : "no table (resolves from _entries)") + "  ->  after: table " + build.TableHash);
                log.Add("  rows 120, changed " + changed.Count + (changed.Count > 0 && changed.Count <= 40 ? " (" + string.Join("", changed) + ")" : "") + (same ? "  [already up to date]" : ""));
                backup.books.Add(StateOf(path, book));
                if (dry || same) continue;
                SpellImportGuard308.Demand(path);
                string entriesBefore = EntriesText(book);
                // each book gets its own row objects (rows are classes): a plain field-by-field copy, no text round trip
                book.SetTable308(build.Rows.Select(CloneRow).ToArray(), (SpellUnlockRule[])build.Unlocks.Clone(), build.SourceHash, build.TableHash);
                EditorUtility.SetDirty(book);
                AssetDatabase.SaveAssetIfDirty(book);
                if (EntriesText(book) != entriesBefore) throw new InvalidOperationException("_entries changed while importing " + path);
                written++;
                log.Add("  written (SaveAssetIfDirty), _entries unchanged");
            }
            if (!dry && written > 0) File.WriteAllText(Path.Combine(OutFolder, "backup308.json"), JsonUtility.ToJson(backup, true), new UTF8Encoding(false));
            if (!dry)
            {
                // remembered so that verify, status and catalog compare against the same sheets, and so that the next import
                // cannot widen the selection without saying so
                choice.time = backup.time; choice.tableHash = build.TableHash;
                File.WriteAllText(Path.Combine(OutFolder, SelectionFile), JsonUtility.ToJson(choice, true), new UTF8Encoding(false));
                log.Add("SELECTION recorded in " + OutFromRepo + "/" + SelectionFile + ": " + choice.Text);
            }
            string skippedText = skippedBooks.Count > 0 ? ", " + skippedBooks.Count + " skipped (protected)" : "";
            log.Add(dry ? "DRY: nothing was written (" + targets.Count + " books listed" + skippedText + (refusals.Count > 0 ? ", " + refusals.Count + " refusal(s): the real import would write nothing" : "") + ")"
                : "DONE: " + written + " of " + targets.Count + " books written" + skippedText + (written > 0 ? "; previous state kept in " + OutFromRepo + "/backup308.json (spell308-revert)" : ""));
            return Write(dry ? "import_dry.txt" : "import_apply.txt", log);
        }

        // ---- what an import changes for a normal save ----
        // The gate of a normal save that has earned every unlock the main game grants: the real policy functions, with no
        // TEST unlock and no isolated store. start = before any unlock.
        static string ReachOf(SpellRow row, IReadOnlyList<SpellUnlockRule> unlocks, HashSet<string> known, out bool casts)
        {
            casts = false;
            if (row == null || !row.HasHandler) return "misfire";
            if (row.Feature != SpellLegacyFeature.None)
            {
                casts = true;   // behind its own pre-#308 gate, which an import does not move
                return SpellTakeover308.Declared(row) && known.Contains(row.Handler) ? "handler " + row.Handler : "legacy " + row.Feature;
            }
            bool registered = known.Contains(row.Handler);
            if (SpellUnlockPolicy308.JudgeNew(row, registered, false) == SpellResolveStatus.Ok) { casts = true; return "from the start, handler " + row.Handler; }
            foreach (var rule in unlocks)
            {
                if (rule.Final != row.Final) continue;
                // the rule the wiring judges this row with: a TEST-only row (Gate test) is not opened by the main-game
                // grant of its final (SpellUnlockPolicy308.RuleFor), so it stays a misfire in a normal save
                bool earned = SpellUnlockPolicy308.Unlocked(true, SpellUnlockPolicy308.RuleFor(row, rule).GrantedInMain, true, true, false, false);
                if (SpellUnlockPolicy308.JudgeNew(row, registered, earned) != SpellResolveStatus.Ok) break;
                casts = true;
                return "after the " + rule.Final + " unlock (" + rule.LedgerId + "), handler " + row.Handler;
            }
            return "misfire";
        }

        static void NormalSaveChanges(List<string> log, SpellBuildResult308 build, SpellBookSO book, string path, HashSet<string> known)
        {
            var entries = EntriesOf(book);
            var handover = new List<string>(); var opened = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            var gone = new List<string>(); var numbers = new List<string>(); var locked = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var testOnly = new List<string>();   // rows the data keeps behind the TEST unlock although the main game grants their final
            int same = 0, misfire = 0;
            for (int i = 1; i <= SpellGrammar308.Count; i++)
            {
                char letter = SpellGrammar308.LetterAt(i);
                SpellRow now = build.Rows[i - 1], before = null;
                if (book.HasTable) before = book.Rows[i - 1]; else SpellTableBuilder308.TryLegacyRow(letter, entries, out before);
                string was = ReachOf(before, book.HasTable ? book.Unlocks : build.Unlocks, known, out bool cast), will = ReachOf(now, build.Unlocks, known, out bool casts);
                if (!cast && !casts)
                {
                    misfire++;
                    if (now != null && now.HasHandler && now.Feature == SpellLegacyFeature.None)
                    {
                        if (now.Gate == SpellGateMode.Test) testOnly.Add(letter + "(" + now.Handler + ")");
                        else { string final = now.Final.ToString(); locked.TryGetValue(final, out int n); locked[final] = n + 1; }
                    }
                    continue;
                }
                if (cast && !casts) { gone.Add(letter + "(" + was + ")"); continue; }
                if (!cast)
                {
                    string when = will.Substring(0, will.IndexOf(", handler", StringComparison.Ordinal));
                    if (!opened.TryGetValue(when, out var list)) opened[when] = list = new List<string>();
                    list.Add(letter + "(" + now.Handler + ")");
                    continue;
                }
                if (was != will && will.StartsWith("handler ", StringComparison.Ordinal)) { handover.Add(letter + "(" + now.Handler + ")"); continue; }
                if (was != will) { numbers.Add(letter + "(" + was + " -> " + will + ")"); continue; }
                // the same route: for a row a handler runs, its numbers are the row itself (legacy numbers are guarded by the drift check)
                if (book.HasTable && now.Feature == SpellLegacyFeature.None && RowText(before) != RowText(now)) { numbers.Add(letter + "(row values)"); continue; }
                same++;
            }
            log.Add("NORMAL SAVE (no TEST unlock), against " + path + (book.HasTable ? " [table " + book.TableHash.Substring(0, Math.Min(8, book.TableHash.Length)) + "]" : " [no table]") + ":");
            log.Add("  HANDED OVER (casts today, judged by a registered handler after the import): " + (handover.Count > 0 ? string.Join(" ", handover) : "none"));
            if (opened.Count == 0) log.Add("  NEW (a misfire today, casts after the import): none");
            foreach (var pair in opened) log.Add("  NEW " + pair.Key + ": " + string.Join(" ", pair.Value));
            log.Add("  GONE (casts today, a misfire after the import): " + (gone.Count > 0 ? string.Join(" ", gone) : "none"));
            log.Add("  CHANGED (casts before and after, by another route or with other row values): " + (numbers.Count > 0 ? string.Join(" ", numbers) : "none"));
            log.Add("  TEST-ONLY rows (Gate test: the main-game grant of their final does not open them): " + (testOnly.Count > 0 ? string.Join(" ", testOnly) : "none"));
            log.Add("  unchanged: " + same + " cast as before, " + misfire + " stay a misfire" +
                (locked.Count > 0 ? " (of those, behind a final the main game does not grant: " + string.Join(", ", locked.Select(p => p.Key + " " + p.Value)) + ")" : ""));
        }

        static SpellRow CloneRow(SpellRow row)
        {
            return new SpellRow
            {
                Letter = row.Letter, Index = row.Index, Element = row.Element, Medial = row.Medial, Final = row.Final, Category = row.Category, Kind = row.Kind,
                Handler = row.Handler, Pending = row.Pending, Gate = row.Gate, Feature = row.Feature, BasePower = row.BasePower,
                ProjectileSpeedMul = row.ProjectileSpeedMul, AreaShape = row.AreaShape, AreaAngle = row.AreaAngle, AreaRadius = row.AreaRadius,
                AreaLength = row.AreaLength, AreaSpeed = row.AreaSpeed, AreaImpactDelay = row.AreaImpactDelay, VolleyShots = row.VolleyShots,
                VolleyInterval = row.VolleyInterval, ScatterVolley = row.ScatterVolley,
                Params = row.Params != null ? (SpellParam[])row.Params.Clone() : Array.Empty<SpellParam>(),
            };
        }

        // ---- spell308-revert ----
        static string Revert(bool dry)
        {
            var log = new List<string> { "spell308-revert" + (dry ? ":dry" : "") };
            string file = Path.Combine(OutFolder, "backup308.json");
            if (!File.Exists(file)) { log.Add("refused: no backup (" + OutFromRepo + "/backup308.json); nothing to revert"); return string.Join("\n", log); }
            var backup = JsonUtility.FromJson<Backup>(File.ReadAllText(file, Encoding.UTF8));
            log.Add("BACKUP of " + backup.time + ", " + backup.books.Count + " books");
            int written = 0;
            foreach (var state in backup.books)
            {
                if (SpellImportGuard308.Protected(state.path)) { log.Add(SpellImportGuard308.SkipLabel + " " + state.path + ": never written, not reverted"); continue; }
                var book = AssetDatabase.LoadAssetAtPath<SpellBookSO>(state.path);
                if (book == null) { log.Add("SKIP missing " + state.path); continue; }
                bool table = state.rows != null && state.rows.Length == SpellGrammar308.Count;
                log.Add("BOOK " + state.path + "  now: " + (book.HasTable ? "table " + book.TableHash : "no table") + "  ->  back to: " + (table ? "table " + state.tableHash : "no table"));
                if (dry) continue;
                SpellImportGuard308.Demand(state.path);
                string entriesBefore = EntriesText(book);
                book.SetTable308(table ? state.rows : Array.Empty<SpellRow>(), table ? state.unlocks : Array.Empty<SpellUnlockRule>(), table ? state.rowsHash : "", table ? state.tableHash : "");
                EditorUtility.SetDirty(book);
                AssetDatabase.SaveAssetIfDirty(book);
                if (EntriesText(book) != entriesBefore) throw new InvalidOperationException("_entries changed while reverting " + state.path);
                written++;
            }
            if (!dry)
            {
                // the selection goes back with the books (no selection before = every sheet)
                var previous = backup.hadSelection && backup.selection != null ? backup.selection : new Selection();
                File.WriteAllText(Path.Combine(OutFolder, SelectionFile), JsonUtility.ToJson(previous, true), new UTF8Encoding(false));
                log.Add("SELECTION back to: " + previous.Text);
            }
            log.Add(dry ? "DRY: nothing was written" : "DONE: " + written + " books put back");
            return Write(dry ? "revert_dry.txt" : "revert_apply.txt", log);
        }

        // ---- spell308-report ----
        static string Report()
        {
            var log = new List<string> { "spell308-report" };
            var targets = Targets(); var mainGuids = MainSceneGuids(); var skippedBooks = ProtectedBooks();
            foreach (string guid in AssetDatabase.FindAssets("t:SpellBookSO").OrderBy(g => AssetDatabase.GUIDToAssetPath(g), StringComparer.Ordinal))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var book = AssetDatabase.LoadAssetAtPath<SpellBookSO>(path);
                if (book == null) continue;
                int resolvable = 0;
                for (int i = 1; i <= SpellGrammar308.Count; i++) if (book.TryGetRow(SpellGrammar308.LetterAt(i), out var row) && row.HasHandler) resolvable++;
                log.Add((targets.Contains(path) ? "TARGET " : skippedBooks.Contains(path) ? SpellImportGuard308.SkipLabel + " " : "other  ") + (mainGuids.Contains(guid) ? "[W_Demo_Main] " : "") + path);
                log.Add("  guid " + guid + ", " + (book.HasTable ? "table " + book.TableHash + " source " + book.RowsHash : "no table (resolves from _entries)") + ", glyphs with a handler " + resolvable);
            }
            return Write("report.txt", log);
        }

        // ---- spell308-verify ----
        static string Verify(bool strict, string[] parts)
        {
            var log = new List<string> { "spell308-verify" + (strict ? ":strict" : "") };
            var choice = Following(parts, out string source);
            if (choice.Problem != null) { log.Add("refused: " + choice.Problem); return string.Join("\n", log); }
            log.Add("SELECTION " + choice.Text + " (" + source + ")");
            var build = Build(strict, out _, choice);
            foreach (string error in build.Errors) log.Add("ERROR " + error);
            int bad = build.Ok ? 0 : 1;
            var targets = Targets(); var mainGuids = MainSceneGuids();
            foreach (string path in targets)
            {
                var book = AssetDatabase.LoadAssetAtPath<SpellBookSO>(path);
                var problems = new List<string>();
                if (book == null || !book.HasTable) problems.Add("no table");
                else if (build.Ok)
                {
                    if (book.RowsHash != build.SourceHash) problems.Add("source hash differs");
                    if (book.TableHash != build.TableHash) problems.Add("table hash differs");
                    for (int i = 0; i < build.Rows.Length; i++)
                        if (RowText(book.Rows[i]) != RowText(build.Rows[i])) problems.Add("row " + build.Rows[i].Letter);
                    if (!book.Unlocks.Select(UnlockText).SequenceEqual(build.Unlocks.Select(UnlockText))) problems.Add("unlock rules differ");
                }
                if (problems.Count > 0) bad++;
                log.Add((problems.Count == 0 ? "ok   " : "FAIL ") + (mainGuids.Contains(AssetDatabase.AssetPathToGUID(path)) ? "[W_Demo_Main] " : "") + path +
                    (problems.Count > 0 ? "  : " + string.Join(", ", problems.Take(12)) : ""));
            }
            // a protected book (a protected tree, or the original book the protected scene W_Demo_Compact reads) is never
            // imported: it is listed, not judged against the table, and must not carry one
            var skippedBooks = ProtectedBooks();
            foreach (string path in skippedBooks)
            {
                var book = AssetDatabase.LoadAssetAtPath<SpellBookSO>(path);
                bool carries = book != null && book.HasTable;
                if (carries) bad++;
                log.Add((carries ? "FAIL " : "") + SpellImportGuard308.SkipLabel + " " + (mainGuids.Contains(AssetDatabase.AssetPathToGUID(path)) ? "[W_Demo_Main] " : "") + path +
                    (carries ? "  : carries table " + book.TableHash + " although the importer never writes there" : "  : no table, resolves from _entries"));
            }
            if (targets.Count + skippedBooks.Count < 4) { bad++; log.Add("FAIL expected the original and three copies, found " + (targets.Count + skippedBooks.Count)); }
            if (!targets.Any(p => mainGuids.Contains(AssetDatabase.AssetPathToGUID(p)))) { bad++; log.Add("FAIL none of the " + targets.Count + " target books is the book W_Demo_Main reads"); }
            log.Add(bad == 0 ? "VERIFY ok: " + targets.Count + " books carry table " + build.TableHash + " (" + choice.Text + ")" +
                (skippedBooks.Count > 0 ? "; " + skippedBooks.Count + " skipped (protected)" : "") : "VERIFY FAILED: " + bad + " problem(s)");
            return Write("verify.txt", log);
        }

        // ---- spell308-summon-power (WP-00) ----
        static string SummonPower(bool dry)
        {
            var log = new List<string> { "spell308-summon-power" + (dry ? ":dry" : "") };
            string sheet = Path.Combine(SheetFolder, "SummonPower308.csv");
            if (!File.Exists(sheet)) { log.Add("refused: missing " + sheet); return string.Join("\n", log); }
            var power = new Dictionary<string, float>(StringComparer.Ordinal);
            var rows = SpellTableBuilder308.ParseCsv(File.ReadAllText(sheet, Encoding.UTF8));
            for (int r = 1; r < rows.Count; r++)
            {
                if (rows[r].Length < 2 || !float.TryParse(rows[r][1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value) || value < 0f ||
                    !SpellGrammar308.TryDecompose(rows[r][0].Trim().Length == 1 ? rows[r][0].Trim()[0] : '\0', out var glyph) || glyph.Category != SpellCategory.Summon)
                { log.Add("refused: bad row " + r + " in " + sheet); return string.Join("\n", log); }
                power[rows[r][0].Trim()] = value;
            }
            if (power.Count != 5) { log.Add("refused: " + sheet + " must list the five summon glyphs, found " + power.Count); return string.Join("\n", log); }
            var mainGuids = MainSceneGuids(); int written = 0, total = 0, skipped = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:SummonCombatProfile").OrderBy(g => AssetDatabase.GUIDToAssetPath(g), StringComparer.Ordinal))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(path);
                if (profile == null || profile.Letter == null || !power.TryGetValue(profile.Letter, out float value)) continue;
                if (SpellImportGuard308.Protected(path))
                {
                    skipped++;
                    log.Add(SpellImportGuard308.SkipLabel + " " + (mainGuids.Contains(guid) ? "PROFILE [W_Demo_Main] " : "PROFILE ") + path + "  " + profile.Letter + "  keeps " +
                        (File.ReadAllText(path).Contains("\n  BasePower:") ? F(profile.BasePower) : "the code default " + F(profile.BasePower)));
                    continue;
                }
                total++;
                bool serialized = File.ReadAllText(path).Contains("\n  BasePower:");
                bool change = !serialized || profile.BasePower != value;
                log.Add((mainGuids.Contains(guid) ? "PROFILE [W_Demo_Main] " : "PROFILE ") + path + "  " + profile.Letter + "  " +
                    (serialized ? F(profile.BasePower) : "(not in the file: code default " + F(profile.BasePower) + ")") + " -> " + F(value) + (change ? "" : "  [already up to date]"));
                if (dry || !change) continue;
                SpellImportGuard308.Demand(path);
                profile.BasePower = value;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
                written++;
            }
            string skippedText = skipped > 0 ? ", " + skipped + " skipped (protected)" : "";
            log.Add(dry ? "DRY: nothing was written (" + total + " profiles listed" + skippedText + ")" : "DONE: " + written + " of " + total + " profiles written" + skippedText);
            return Write(dry ? "summon_power_dry.txt" : "summon_power_apply.txt", log);
        }

        // ---- spell308-catalog (WP-00) ----
        static string Catalog(bool dry)
        {
            var log = new List<string> { "spell308-catalog" + (dry ? ":dry" : "") };
            var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(CatalogPath);
            if (catalog == null) { log.Add("refused: missing " + CatalogPath); return string.Join("\n", log); }
            if (SpellImportGuard308.Protected(CatalogPath)) { log.Add(SpellImportGuard308.SkipLabel + " " + CatalogPath + ": nothing was written"); return string.Join("\n", log); }
            var choice = Following(null, out string source);
            log.Add("SELECTION " + choice.Text + " (" + source + ")");
            var build = Build(false, out _, choice);
            if (!build.Ok) { log.Add("refused: the table does not build (" + build.Errors.Count + " errors)"); return string.Join("\n", log); }
            int changed = 0, connected = 0;
            for (int i = 0; i < catalog.Entries.Length; i++)
            {
                var entry = catalog.Entries[i];
                int index = !string.IsNullOrEmpty(entry.Glyph) ? SpellGrammar308.IndexOf(entry.Glyph[0]) : 0;
                if (index == 0) continue;
                var row = build.Rows[index - 1];
                // connected = game code presents this glyph with its catalogue prefab; the bridge glyph draws its own mesh
                bool now = row.HasHandler && row.Feature != SpellLegacyFeature.Mum;
                if (now) connected++;
                if (entry.GameplayConnected == now) continue;
                changed++;
                log.Add("ENTRY " + entry.Glyph + "  " + entry.GameplayConnected + " -> " + now);
                if (!dry) { entry.GameplayConnected = now; catalog.Entries[i] = entry; }
            }
            if (!dry && changed > 0) { SpellImportGuard308.Demand(CatalogPath); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog); }
            log.Add((dry ? "DRY: " : "DONE: ") + changed + " flags " + (dry ? "would change" : "changed") + "; connected glyphs " + connected + " of " + catalog.Entries.Length);
            return Write(dry ? "catalog_dry.txt" : "catalog_apply.txt", log);
        }
    }
}
