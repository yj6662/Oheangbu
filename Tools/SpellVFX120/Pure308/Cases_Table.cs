// SPEC-SPELL-120-308 L2 cases of the foundation (WP-01A): table build, resolve statuses, the 36-glyph regression against the
// pre-#308 resolver (A4), the legacy traits, the unlock policy (A5). Run by spell120_pure308.py.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.EditorTools.WorldMacro;
using Oheangbu.Spellcraft;

namespace Pure308
{
    public static partial class Program
    {
        // A gate for the handler route: which handler ids exist and which finals are open (the App gate uses the same policy).
        sealed class ModelGate : ISpellGate
        {
            public bool Guk, Buffs, Wards, Giyeok, Mum;
            public HashSet<string> Registered = new HashSet<string>();
            public HashSet<SpellFinal> Unlocked = new HashSet<SpellFinal>();
            public SpellResolveStatus Judge(SpellRow row)
            {
                if (row.Feature != SpellLegacyFeature.None) return new LegacySpellGate(Guk, Buffs, Wards, Giyeok, Mum).Judge(row);
                return SpellUnlockPolicy308.JudgeNew(row, Registered.Contains(row.Handler), Unlocked.Contains(row.Final));
            }
        }

        static KeyValuePair<SpellResolveStatus, SpellCast> NewResolve(char letter, ISpellGate gate, SpellRow row, float brush)
        {
            var status = SpellResolveCore308.Resolve(letter, brush, brush, row, gate, out SpellCast cast);
            return new KeyValuePair<SpellResolveStatus, SpellCast>(status, cast);
        }

        static void RunTable(Report r, Context c)
        {
            // ---- A2: the table ----
            var build = SpellTableBuilder308.Build(c.CanonicalCsv, c.Sheets, c.UnlockCsv);
            c.Build = build;
            r.Fact("sheets", string.Join(",", c.Sheets.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal)));
            r.Fact("sourceHash", build.SourceHash); r.Fact("tableHash", build.TableHash);
            r.Check("A2", build.Ok, "table builds without errors", string.Join(" | ", build.Errors.Take(8)));
            if (!build.Ok) return;
            File.WriteAllText(Path.Combine(c.OutDir, "pure308_table.txt"), build.CanonicalText + "\n", new UTF8Encoding(false));
            r.Check("A2", build.Rows.Length == 120, "120 rows");
            int[] expected = { 50, 5, 5, 25, 5, 5, 5, 20 };
            for (int k = 0; k < expected.Length; k++)
                r.Check("A2", build.Rows.Count(x => (int)x.Category == k) == expected[k], "category " + (SpellCategory)k + " = " + expected[k]);
            bool grid = true;
            for (int i = 1; i <= 120; i++)
            {
                var row = build.Rows[i - 1];
                grid &= row.Index == i && row.Is(SpellGrammar308.LetterAt(i)) && SpellGrammar308.IndexOf(SpellGrammar308.LetterAt(i)) == i &&
                    SpellGrammar308.TryDecompose(row.Letter[0], out var glyph) && glyph.Category == row.Category && glyph.Element == row.Element;
            }
            r.Check("A2", grid, "grid order, glyph arithmetic, element and category agree for every row");
            int legacy = build.Rows.Count(x => x.Feature != SpellLegacyFeature.None);
            int registered = build.Rows.Count(x => x.HasHandler && x.Feature == SpellLegacyFeature.None);
            int reserved = build.Rows.Count(x => !string.IsNullOrEmpty(x.Pending));
            int blank = build.Rows.Count(x => x.Category == SpellCategory.Blank);
            int waiting = build.MissingSheet.Count;
            r.Fact("legacyRows", legacy); r.Fact("registeredRows", registered); r.Fact("reservedRows", reserved); r.Fact("blankRows", blank); r.Fact("waitingForSheet", waiting);
            r.Check("A2", legacy == 36, "36 rows keep a legacy feature", legacy.ToString());
            r.Check("A2", blank == 20 && build.Rows.Where(x => x.Category == SpellCategory.Blank).All(x => !x.HasHandler && string.IsNullOrEmpty(x.Pending)), "20 blank rows carry no handler");
            r.Check("A2", legacy + registered + reserved + waiting == 100, "every assigned glyph is legacy, registered, reserved or waiting for its sheet",
                legacy + "+" + registered + "+" + reserved + "+" + waiting);
            r.Check("A2", build.Rows.All(x => string.IsNullOrEmpty(x.Pending) || !x.HasHandler), "a reserved row has no handler");
            r.Check("A2", build.Rows.All(x => !x.HasHandler || !string.IsNullOrEmpty(x.Handler.Trim())), "handler ids are not blank text");

            // deterministic: the same input builds the same text
            var again = SpellTableBuilder308.Build(c.CanonicalCsv, c.Sheets, c.UnlockCsv);
            r.Check("A2", again.TableHash == build.TableHash && again.SourceHash == build.SourceHash && again.CanonicalText == build.CanonicalText, "second build is identical");

            // builder refusals (section 2), on small sheets of their own (independent of the project's sheets)
            string head = "글자,Mode,Handler,Pending,Kind,Gate,Feature,Inherit,BasePower,SpeedMul,AreaShape,AreaAngle,AreaRadius,AreaLength,AreaSpeed,AreaDelay,Shots,Interval,Scatter,Params\n";
            string g1 = SpellGrammar308.LetterAt(1).ToString(), g2 = SpellGrammar308.LetterAt(2).ToString(), blankGlyph = SpellGrammar308.LetterAt(21).ToString();
            SpellBuildResult308 Try(params string[] texts)
            {
                var sheets = new List<SpellSheet308>();
                for (int i = 0; i < texts.Length; i++) sheets.Add(new SpellSheet308("Rules308_T" + i, texts[i]));
                return SpellTableBuilder308.Build(c.CanonicalCsv, sheets, c.UnlockCsv);
            }
            string good = head + g1 + ",,core.single,,,,,,12,1.3,,,,,,,,,,\n";
            r.Check("A2", Try(good).Ok, "a well-formed new row is accepted");
            r.Check("A2", !Try(good, good).Ok, "a glyph in two sheets is refused (without replace)");
            r.Check("A2", Try(good, head + g1 + ",replace,core.cone,,AttackArea,,,,9,,Cone,,,,,,,,,\n").Ok, "one replace of an existing row is accepted");
            r.Check("A2", !Try(good, head + g1 + ",replace,core.cone,,,,,,,,,,,,,,,,,\n", head + g1 + ",replace,core.path,,,,,,,,,,,,,,,,,\n").Ok, "a second replace of the same glyph is refused");
            r.Check("A2", !Try(head + g1 + ",replace,core.single,,,,,,,,,,,,,,,,,\n").Ok, "replace without an existing row is refused");
            r.Check("A2", !Try(head + blankGlyph + ",,core.single,,,,,,,,,,,,,,,,,\n").Ok, "a handler on a blank glyph is refused");
            r.Check("A2", !Try(head.Replace("Params", "Params,Extra") + g1 + ",,core.single,,,,,,,,,,,,,,,,,,\n").Ok, "an unknown column is refused");
            r.Check("A2", !Try(head + g1 + ",,core.single,,,,,,5000,,,,,,,,,,,\n").Ok, "an out-of-range value is refused");
            r.Check("A2", !Try(head + g1 + ",,core.single,,,,,,1.2345678,,,,,,,,,,,\n").Ok, "a number with more than six significant digits is refused");
            r.Check("A2", !Try(head + g1 + ",,core.single,,,,book,,,,,,,,,,,,,nokey.zz=1\n").Ok, "an unknown parameter on a legacy row is refused");
            r.Check("A2", !Try(head + g1 + ",,core.single,,,,book,,,,,,,,,,,,,cost=1;cost=2\n").Ok, "a repeated parameter is refused");
            r.Check("A2", !Try(head + g1 + ",,,,,,,,,,,,,,,,,,,\n").Ok, "a row with neither handler nor pending is refused");
            r.Check("A2", !Try(head + g1 + ",,core.single,WP-99,,,,,,,,,,,,,,,,\n").Ok, "a row with both handler and pending is refused");
            r.Check("A2", !Try(head + g1 + ",,core.single,,Parry,,,,,,,,,,,,,,,\n").Ok, "a kind that does not fit the category is refused");
            r.Check("A5", !Try(good + g2 + ",,x.test,,,open,,base,,,,,,,,,,,,\n").Ok && Try(head + SpellGrammar308.LetterAt(16) + ",,core.summon,,Summon,open,book,,,,,,,,,,,,,\n").Ok,
                "Gate open is refused on every row except a summon row: a rule sheet cannot open a final consonant by itself");
            var inherit = Try(good + g2 + ",,x.test,,,,,base,,,,,,,,,,,,\n");
            r.Check("A2", inherit.Ok && inherit.Rows[1].BasePower == 12f && inherit.Rows[1].ProjectileSpeedMul == 1.3f && inherit.Rows[1].Kind == SpellKind.AttackSingle,
                "Inherit base copies base power and projectile speed from the row without a final");
            r.Check("A2", !Try(head + g2 + ",,x.test,,,,,base,,,,,,,,,,,,\n").Ok, "Inherit base without a base row is refused");
            var strictBuild = SpellTableBuilder308.Build(c.CanonicalCsv, c.Sheets, c.UnlockCsv, null, true);
            r.Check("A2", strictBuild.Ok == (waiting == 0), "strict build fails exactly while glyphs wait for a sheet", strictBuild.Errors.Count + " errors, waiting " + waiting);
            string shortCsv = string.Join("\n", SpellTableBuilder308.Normalise(c.CanonicalCsv).Split('\n').Take(100));
            r.Check("A2", !SpellTableBuilder308.Build(shortCsv, c.Sheets, c.UnlockCsv).Ok, "a canonical CSV without 120 rows is refused");

            // ---- resolve statuses (section 8) ----
            var allIds = new HashSet<string>(build.Rows.Where(x => x.HasHandler && x.Feature == SpellLegacyFeature.None).Select(x => x.Handler));
            var open = new ModelGate { Guk = true, Buffs = true, Wards = true, Giyeok = true, Mum = true, Registered = allIds,
                Unlocked = new HashSet<SpellFinal> { SpellFinal.Giyeok, SpellFinal.Nieun, SpellFinal.Mieum, SpellFinal.Siot, SpellFinal.Ieung } };
            var shut = new ModelGate { Registered = allIds };
            var counts = new Dictionary<SpellResolveStatus, int>(); var shutCounts = new Dictionary<SpellResolveStatus, int>();
            for (int i = 1; i <= 120; i++)
            {
                char letter = SpellGrammar308.LetterAt(i);
                var status = SpellResolveCore308.Resolve(letter, 1f, 1f, build.Rows[i - 1], open, out _);
                counts[status] = counts.TryGetValue(status, out int n) ? n + 1 : 1;
                status = SpellResolveCore308.Resolve(letter, 1f, 1f, build.Rows[i - 1], shut, out _);
                shutCounts[status] = shutCounts.TryGetValue(status, out n) ? n + 1 : 1;
            }
            int Count(Dictionary<SpellResolveStatus, int> d, SpellResolveStatus s) => d.TryGetValue(s, out int n) ? n : 0;
            r.Fact("statusAllOpen", string.Join(" ", counts.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value)));
            r.Fact("statusAllShut", string.Join(" ", shutCounts.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value)));
            r.Check("A6", Count(counts, SpellResolveStatus.Blank) == 20, "20 glyphs answer Blank");
            r.Check("A6", Count(counts, SpellResolveStatus.Unimplemented) == reserved + waiting, "reserved and waiting glyphs answer Unimplemented", Count(counts, SpellResolveStatus.Unimplemented).ToString());
            r.Check("A6", Count(counts, SpellResolveStatus.Ok) == legacy + registered, "everything else casts when all gates are open");
            r.Check("A6", Count(shutCounts, SpellResolveStatus.Ok) == build.Rows.Count(x => x.Feature == SpellLegacyFeature.Book) +
                build.Rows.Count(x => x.Feature == SpellLegacyFeature.None && x.HasHandler && (x.Final == SpellFinal.None || x.Gate == SpellGateMode.Open)),
                "with every gate shut only book rows and rows without a final (or open rows) cast");
            r.Check("A6", SpellResolveCore308.Resolve('A', 1f, 1f, null, open, out _) == SpellResolveStatus.NotInVocabulary, "no row = NotInVocabulary");
            r.Check("A6", SpellResolveCore308.Resolve(SpellGrammar308.LetterAt(1), 1f, 1f, build.Rows[0], null, out _) == SpellResolveStatus.Unavailable, "no gate = Unavailable");
            var sample = new SpellRow { Params = new[] { new SpellParam { Key = "a.b", Value = 2f } } };
            r.Check("A7", sample.F("a.b", 9f) == 2f && sample.F("a.c", 9f) == 9f && sample.Has("a.b") && !sample.Has("a.c"), "SpellRow.F reads a key or its fallback");

            // ---- A4: the 36 glyphs against the pre-#308 resolver ----
            r.Check("A4", c.Books.Count > 0 && c.MainBook != null, "books parsed from the project", c.Books.Count + " books");
            if (c.MainBook == null) return;
            var total = new SpellEquivalence308.Result(); int shadowed = 0;
            float[] brushes = { 1f, .37f };
            string mainSignature = Signature(c.MainBook);
            foreach (var book in c.Books)
            {
                var entries = book.Array;
                foreach (var entry in entries)
                    if (SpellGrammar308.TryDecompose(entry.Letter[0], out var glyph) && SpellGrammar308.LegacyFeatureOf(glyph) != SpellLegacyFeature.None) shadowed++;
                foreach (float brush in brushes)
                {
                    // (1) a book without imported rows: rows derived from its own entries
                    var legacyResult = SpellEquivalence308.Compare(book.Path + " legacy-rows b" + brush, entries, true, brush,
                        letter => SpellTableBuilder308.TryLegacyRow(letter, entries, out var row) ? row : null,
                        (letter, gate, row) => NewResolve(letter, gate, row, brush));
                    total.Add(legacyResult);
                    // (2) the imported table, for the books of the main copy chain (their entries are what the Base sheet mirrors)
                    if (Signature(book) != mainSignature) continue;
                    var tableResult = SpellEquivalence308.Compare(book.Path + " table b" + brush, entries, true, brush,
                        letter => SpellGrammar308.IndexOf(letter) > 0 ? build.Rows[SpellGrammar308.IndexOf(letter) - 1] : null,
                        (letter, gate, row) => NewResolve(letter, gate, row, brush));
                    total.Add(tableResult);
                }
            }
            // a resolver without a book: everything misfires on both sides
            var noBook = SpellEquivalence308.Compare("no book", null, false, 1f, letter => null, (letter, gate, row) => NewResolve(letter, gate, null, 1f));
            total.Add(noBook);
            r.Fact("equivalenceCases", total.Cases); r.Fact("equivalenceMismatches", total.Mismatches);
            r.Fact("equivalenceOldResolved", total.OldResolved); r.Fact("equivalenceNewResolved", total.NewResolved);
            r.Fact("equivalenceCasesPerBookAndBrush", SpellEquivalence308.FlagSets * SpellEquivalence308.Letters().Length);
            r.Fact("gridCasesPerBookAndBrush", SpellEquivalence308.FlagSets * 120);
            r.Fact("books", c.Books.Count); r.Fact("mainChainBooks", c.Books.Count(b => Signature(b) == mainSignature));
            r.Check("A4", total.Mismatches == 0, "old resolver = new resolver for every book, flag set, glyph and brush (" + total.Cases + " cases)", string.Join(" | ", total.Details.Take(10)));
            r.Check("A4", total.OldResolved == total.NewResolved && total.OldResolved > 0, "the same number of casts on both sides", total.OldResolved + " / " + total.NewResolved);
            r.Check("A4", shadowed == 0, "no book carries its own entry for a glyph a legacy feature owns (the one case where the old code fell through to the book)", shadowed.ToString());

            // import guard (SpellTableBuilder308.LegacyDrift, used by spell308-import): the table changes nothing for the books of
            // the main copy chain; a changed number, a removed handler and a takeover of a glyph that casts today are all named
            int guardDrift = 0, guardBooks = 0;
            foreach (var book in c.Books.Where(b => Signature(b) == mainSignature))
            {
                var drift = new List<string>(); var takeovers = new List<string>();
                SpellTableBuilder308.LegacyDrift(build.Rows, book.Array, drift, takeovers);
                guardBooks++; guardDrift += drift.Count + takeovers.Count;
            }
            r.Fact("importGuardBooks", guardBooks);
            r.Check("A4", guardBooks >= 4 && guardDrift == 0, "import guard: the table changes no glyph that casts today in the " + guardBooks + " books of the main copy chain", guardDrift + " differences");
            {
                SpellBuildResult308 With(string row)
                {
                    var sheets = new List<SpellSheet308>(c.Sheets) { new SpellSheet308("Rules308_ZZGuard", head + row) };
                    return SpellTableBuilder308.Build(c.CanonicalCsv, sheets, c.UnlockCsv);
                }
                var mainRows = c.MainBook.Array;
                // The probe is grid cell 25 (the fire single shot: base power 12, no speed column): a book row no rule sheet
                // replaces. It was cell 1 until the WP-11 sheet replaced that row (a glyph can be replaced only once).
                string guardCell = SpellGrammar308.LetterAt(25).ToString();
                var changed = With(guardCell + ",replace,core.single,,AttackSingle,,book,,13,,,,,,,,,,,\n");
                var d1 = new List<string>(); var t1 = new List<string>(); SpellTableBuilder308.LegacyDrift(changed.Rows, mainRows, d1, t1);
                var taken = With(guardCell + ",replace,x.test,,AttackSingle,,,,12,,,,,,,,,,,\n");
                var d2 = new List<string>(); var t2 = new List<string>(); SpellTableBuilder308.LegacyDrift(taken.Rows, mainRows, d2, t2);
                var d3 = new List<string>(); var t3 = new List<string>(); SpellTableBuilder308.LegacyDrift(build.Rows, new SpellBookSO.Entry[0], d3, t3);
                r.Check("A4", changed.Ok && d1.Count == 2 && t1.Count == 0, "import guard: a changed base power is named for the glyph and for the giyeok glyph that inherits it", string.Join(" | ", d1));
                r.Check("A4", taken.Ok && d2.Count == 0 && t2.Count == 1, "import guard: a row that hands an existing glyph to a handler is a named takeover, not a silent change", string.Join(" | ", t2));
                r.Check("A4", d3.Count >= 20, "import guard: a book without the entries behind the legacy rows is refused", d3.Count + " differences");
            }

            // legacy traits: old glyph special cases = row data = grammar, for all 120
            var traitDetails = new List<string>();
            int traits = SpellEquivalence308.TraitMismatches(letter => build.Rows[SpellGrammar308.IndexOf(letter) - 1], traitDetails);
            var mainEntries = c.MainBook.Array;
            traits += SpellEquivalence308.TraitMismatches(letter => SpellTableBuilder308.TryLegacyRow(letter, mainEntries, out var row) ? row : null, traitDetails);
            r.Check("A4", traits == 0, "spikes / seed / pierce: old special cases = row parameters = grammar fallback", string.Join(" | ", traitDetails.Take(6)));

            // before / after sheet for the main book: every flag on, every flag off, both row sources
            var sheet = new StringBuilder();
            foreach (int set in new[] { 31, 0 })
                foreach (bool table in new[] { false, true })
                {
                    var lines = SpellEquivalence308.BeforeAfter(mainEntries, 1f, set,
                        letter => table ? build.Rows[SpellGrammar308.IndexOf(letter) - 1] : (SpellTableBuilder308.TryLegacyRow(letter, mainEntries, out var row) ? row : null),
                        (letter, gate, row) => NewResolve(letter, gate, row, 1f), out int same);
                    string title = "flags " + (set == 31 ? "all on" : "all off") + " / " + (table ? "imported table" : "book without rows");
                    sheet.Append("# ").Append(title).Append(": ").Append(same).Append(" of 120 same\n");
                    sheet.Append("index\tglyph\tbefore (pre-#308 resolver)\tafter (#308 resolver)\thandler\tverdict\n");
                    foreach (string line in lines) sheet.Append(line).Append('\n');
                    r.Check("A4", same == 120, "before/after sheet: " + title, same + " of 120");
                    r.Fact("beforeAfter " + title, same + "/120");
                }
            File.WriteAllText(Path.Combine(c.OutDir, "equivalence308.tsv"), sheet.ToString(), new UTF8Encoding(false));

            // ---- A5: unlock policy ----
            bool policy = true;
            foreach (bool campaign in new[] { false, true }) foreach (bool granted in new[] { false, true }) foreach (bool ledger in new[] { false, true })
            foreach (bool evidence in new[] { false, true }) foreach (bool test in new[] { false, true }) foreach (bool isolated in new[] { false, true })
            {
                bool got = SpellUnlockPolicy308.Unlocked(campaign, granted, ledger, evidence, test, isolated);
                bool mainGame = campaign && granted && ledger && evidence;   // a final the sheet grants in the main game + the saved ledger (and the named encounter)
                bool testing = test && isolated;                             // the TEST unlock counts only inside the isolated store
                policy &= got == (mainGame || testing);
            }
            r.Check("A5", policy, "unlock policy table: main-game proof of a granted final, or TEST unlock in the isolated store (64 combinations)");
            // a ledger id alone never opens a final the sheet does not grant in the main game (the unlock order is data, not a save file)
            r.Check("A5", build.Unlocks.Where(u => !u.GrantedInMain).All(u => !SpellUnlockPolicy308.Unlocked(true, u.GrantedInMain, true, true, false, false)) &&
                build.Unlocks.Where(u => u.GrantedInMain).All(u => SpellUnlockPolicy308.Unlocked(true, u.GrantedInMain, true, true, false, false)),
                "main game: a complete proof opens only the finals the unlock sheet grants (giyeok); the other four stay locked without the TEST unlock");
            r.Check("A5", !SpellUnlockPolicy308.TestUnlockAccepted(true, true, false) && !SpellUnlockPolicy308.TestUnlockAccepted(true, false, true) &&
                SpellUnlockPolicy308.TestUnlockAccepted(true, true, true) && SpellUnlockPolicy308.TestUnlockAccepted(false, false, false),
                "a TEST unlock request outside the isolated store (or outside Play) is refused; switching it off is always accepted");
            r.Check("A5", build.Unlocks.Length == 5 && build.Unlocks.Count(u => u.GrantedInMain) == 1 && build.Unlocks.Single(u => u.GrantedInMain).Final == SpellFinal.Giyeok,
                "only the giyeok final is granted in the main game");
            var mieum = build.Unlocks.Single(u => u.Final == SpellFinal.Mieum);
            r.Check("A5", mieum.LedgerId == "world_unlock_coda_mieum" && mieum.EvidenceId == "fallen_hwangryong" &&
                build.Unlocks.Single(u => u.Final == SpellFinal.Giyeok).LedgerId == "demo_unlock_coda_giyeok", "ledger ids equal the ids the session already uses");
            var probe = new SpellRow { Handler = "x.y", Final = SpellFinal.Nieun, Gate = SpellGateMode.Final };
            var openRow = new SpellRow { Handler = "x.y", Final = SpellFinal.Mieum, Gate = SpellGateMode.Open };
            var bare = new SpellRow { Handler = "x.y", Final = SpellFinal.None };
            r.Check("A5", SpellUnlockPolicy308.JudgeNew(probe, false, true) == SpellResolveStatus.Unavailable && SpellUnlockPolicy308.JudgeNew(probe, true, false) == SpellResolveStatus.Locked &&
                SpellUnlockPolicy308.JudgeNew(probe, true, true) == SpellResolveStatus.Ok && SpellUnlockPolicy308.JudgeNew(openRow, true, false) == SpellResolveStatus.Ok &&
                SpellUnlockPolicy308.JudgeNew(bare, true, false) == SpellResolveStatus.Ok, "new rows: unregistered = Unavailable, locked final = Locked, open row or no final = Ok");
            r.Check("A5", new LegacySpellGate(true, true, true, true, true).Judge(probe) == SpellResolveStatus.Unavailable, "a new row never opens through the old flag overloads");
            // the five summons keep their open gate (current behaviour, waiting for the user's confirmation)
            r.Check("A5", build.Rows.Where(x => x.Category == SpellCategory.Summon).All(x => x.Gate == SpellGateMode.Open && x.Feature == SpellLegacyFeature.Book), "summon rows are open rows");
            // the earth field glyph (last vocabulary): without its proof it does not cast
            var mumRow = build.Rows.Single(x => x.Feature == SpellLegacyFeature.Mum);
            r.Check("뭄-3", SpellResolveCore308.Resolve(mumRow.Letter[0], 1f, 1f, mumRow, shut, out _) != SpellResolveStatus.Ok &&
                SpellResolveCore308.Resolve(mumRow.Letter[0], 1f, 1f, mumRow, open, out _) == SpellResolveStatus.Ok, "the bridge glyph casts only with its unlock");
        }

        static string Signature(Book book)
        {
            var b = new StringBuilder();
            foreach (var e in book.Entries)
                b.Append(e.Letter).Append((int)e.Kind).Append((int)e.Element).Append(e.BasePower.ToString("R")).Append((int)e.AreaShape).Append(e.ProjectileSpeedMul.ToString("R"))
                    .Append(e.AreaAngle.ToString("R")).Append(e.AreaRadius.ToString("R")).Append(e.AreaLength.ToString("R")).Append(e.AreaSpeed.ToString("R"))
                    .Append(e.AreaImpactDelay.ToString("R")).Append(e.VolleyShots).Append(e.VolleyInterval.ToString("R")).Append(e.ScatterVolley).Append(';');
            return b.ToString();
        }
    }
}
