// PURE308
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Oheangbu.Core.Domain;

namespace Oheangbu.Spellcraft
{
    public sealed class SpellSheet308
    {
        public readonly string Name, Text;
        public SpellSheet308(string name, string text) { Name = name ?? ""; Text = text ?? ""; }
    }

    public sealed class SpellBuildResult308
    {
        public SpellRow[] Rows = Array.Empty<SpellRow>();
        public SpellUnlockRule[] Unlocks = Array.Empty<SpellUnlockRule>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public readonly List<string> MissingSheet = new List<string>();   // assigned glyphs no rule sheet covers yet
        public string SourceHash = "", TableHash = "", CanonicalText = "";
        public bool Ok => Errors.Count == 0;
        public SpellTable ToTable() => new SpellTable(Rows, Unlocks, SourceHash, TableHash);
    }

    // CSV + rule sheets -> the 120-row table. Pure: the editor importer (Editor/WorldMacro/SpellTable308.cs), the offline runner
    // and the python mirror (Tools/SpellVFX120/spell120_table308.py) follow the same rules and must produce the same TableHash.
    public static class SpellTableBuilder308
    {
        public const string CanonicalHeader = "글자,초성,속성,중성,프레임,종성,분류,효과,상태,비고";
        public const string BaseSheet = "Rules308_Base";
        static readonly string[] RuleColumns =
        {
            "글자", "Mode", "Handler", "Pending", "Kind", "Gate", "Feature", "Inherit", "BasePower", "SpeedMul", "AreaShape",
            "AreaAngle", "AreaRadius", "AreaLength", "AreaSpeed", "AreaDelay", "Shots", "Interval", "Scatter", "Params"
        };
        static readonly string[] UnlockColumns = { "종성", "LedgerId", "EvidenceId", "GrantedInMain" };
        static readonly string[] CategoryNames = { "공격", "상합 설치", "패링", "버프", "소환", "방벽", "필드", "공백" };
        static readonly int[] CategoryCounts = { 50, 5, 5, 25, 5, 5, 5, 20 };
        const string InitialJamo = "ㄱㄴㅁㅅㅇ", MedialJamo = "ㅏㅓㅗㅜ";

        // Keys the built-in paths read. Handlers declare their own keys (SpellParamSpec); "cost" scales the ink cost of any row.
        public static readonly SpellParamSpec[] CoreKeys =
        {
            new SpellParamSpec("cost", 0f, 10f, false, 1f),
            new SpellParamSpec(SpellGrammar308.SpikesKey, 0f, 1f, false),
            new SpellParamSpec(SpellGrammar308.SeedKey, 0f, 1f, false),
            new SpellParamSpec(SpellGrammar308.PierceKey, 0f, 1f, false),
            new SpellParamSpec("lift.height", .1f, 20f, false),
            new SpellParamSpec("lift.rise", .01f, 20f, false),
            new SpellParamSpec("lift.descent", .01f, 20f, false),
            new SpellParamSpec("lift.hold", 0f, 600f, false),
            new SpellParamSpec("spike.count", 1f, 64f, false),
            new SpellParamSpec("spike.fill", .01f, 1f, false),
            new SpellParamSpec("spike.window", 0f, 10f, false),
            new SpellParamSpec("spike.gap", 0f, 10f, false),
            new SpellParamSpec("spike.inner", 0f, 1f, false),
        };

        sealed class Draft
        {
            public string Letter, Sheet, Mode, Handler, Pending, Feature, Inherit, Gate, Kind, Shape;
            public decimal? Power, Speed, Angle, Radius, Length, AreaSpeed, Delay, Shots, Interval, Scatter;
            public readonly SortedDictionary<string, decimal> Params = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
            public bool Replaced;
        }

        // catalog: handler id -> the keys it declares (null = keys of registered handlers are only checked for syntax).
        // strict: an assigned glyph without a rule row is an error instead of a note (end of the build phase).
        public static SpellBuildResult308 Build(string canonicalCsv, IReadOnlyList<SpellSheet308> ruleSheets, string unlockCsv,
            Func<string, IReadOnlyList<SpellParamSpec>> catalog = null, bool strict = false)
        {
            var result = new SpellBuildResult308();
            var sheets = new List<SpellSheet308>(ruleSheets ?? Array.Empty<SpellSheet308>());
            sheets.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            result.SourceHash = SourceHash(canonicalCsv, sheets, unlockCsv);

            // 1. the canonical vocabulary: 120 rows in grid order, grammar = CSV category
            var glyphs = new SpellGlyph[SpellGrammar308.Count];
            var categories = new SpellCategory[SpellGrammar308.Count];
            var canonical = ParseCsv(canonicalCsv);
            if (canonical.Count == 0 || string.Join(",", canonical[0]) != CanonicalHeader)
                result.Errors.Add("canonical CSV: unexpected header");
            if (canonical.Count != SpellGrammar308.Count + 1)
                result.Errors.Add("canonical CSV: " + (canonical.Count - 1) + " rows, expected 120");
            var counts = new int[CategoryNames.Length];
            for (int i = 0; i < SpellGrammar308.Count; i++)
            {
                char expected = SpellGrammar308.LetterAt(i + 1);
                SpellGrammar308.TryDecompose(expected, out glyphs[i]);
                categories[i] = glyphs[i].Category;
                if (i + 1 >= canonical.Count) continue;
                string[] cells = canonical[i + 1];
                if (cells.Length < 7) { result.Errors.Add("canonical CSV row " + (i + 1) + ": too few cells"); continue; }
                if (cells[0].Length != 1 || cells[0][0] != expected)
                { result.Errors.Add("canonical CSV row " + (i + 1) + ": glyph '" + cells[0] + "' is not grid cell " + (i + 1)); continue; }
                if (cells[1].Length != 1 || InitialJamo.IndexOf(cells[1][0]) != (int)glyphs[i].Initial)
                    result.Errors.Add(cells[0] + ": initial column disagrees with the glyph");
                if (cells[3].Length != 1 || MedialJamo.IndexOf(cells[3][0]) != (int)glyphs[i].Medial - (int)Jamo.A)
                    result.Errors.Add(cells[0] + ": medial column disagrees with the glyph");
                int finalJamo = cells[5].Length == 1 ? InitialJamo.IndexOf(cells[5][0]) : -1;
                if (finalJamo + 1 != (int)glyphs[i].Final)
                    result.Errors.Add(cells[0] + ": final column disagrees with the glyph");
                int category = Array.IndexOf(CategoryNames, cells[6]);
                if (category < 0) result.Errors.Add(cells[0] + ": unknown category '" + cells[6] + "'");
                else
                {
                    counts[category]++;
                    if ((SpellCategory)category != categories[i])
                        result.Errors.Add(cells[0] + ": CSV category " + (SpellCategory)category + " but the grammar grid says " + categories[i]);
                }
            }
            for (int c = 0; c < counts.Length && canonical.Count == SpellGrammar308.Count + 1; c++)
                if (counts[c] != CategoryCounts[c])
                    result.Errors.Add("category " + (SpellCategory)c + ": " + counts[c] + " rows, expected " + CategoryCounts[c]);

            // 2. rule sheets, Base first (ordinal name order): "new" claims a glyph once, "replace" swaps an existing row once
            var drafts = new Draft[SpellGrammar308.Count];
            foreach (var sheet in sheets)
            {
                var rows = ParseCsv(sheet.Text);
                if (rows.Count == 0) { result.Errors.Add(sheet.Name + ": empty sheet"); continue; }
                var column = new Dictionary<string, int>(StringComparer.Ordinal);
                bool header = true;
                for (int c = 0; c < rows[0].Length; c++)
                {
                    if (Array.IndexOf(RuleColumns, rows[0][c]) < 0 || column.ContainsKey(rows[0][c]))
                    { result.Errors.Add(sheet.Name + ": unknown or repeated column '" + rows[0][c] + "'"); header = false; }
                    else column[rows[0][c]] = c;
                }
                if (!column.ContainsKey(RuleColumns[0])) { result.Errors.Add(sheet.Name + ": no glyph column"); header = false; }
                if (!header) continue;
                for (int r = 1; r < rows.Count; r++)
                {
                    string[] cells = rows[r];
                    string Cell(string name) => column.TryGetValue(name, out int at) && at < cells.Length ? cells[at].Trim() : "";
                    string letter = Cell(RuleColumns[0]);
                    string where = sheet.Name + " row " + r + " (" + letter + ")";
                    int index = letter.Length == 1 ? SpellGrammar308.IndexOf(letter[0]) : 0;
                    if (index == 0) { result.Errors.Add(where + ": not a vocabulary glyph"); continue; }
                    if (categories[index - 1] == SpellCategory.Blank) { result.Errors.Add(where + ": a blank glyph cannot have a rule row"); continue; }
                    var draft = new Draft
                    {
                        Letter = letter, Sheet = sheet.Name, Mode = Cell("Mode"), Handler = Cell("Handler"), Pending = Cell("Pending"),
                        Kind = Cell("Kind"), Gate = Cell("Gate"), Feature = Cell("Feature"), Inherit = Cell("Inherit"), Shape = Cell("AreaShape"),
                    };
                    bool fine = true;
                    decimal? Number(string name, decimal min, decimal max, bool whole = false)
                    {
                        string text = Cell(name);
                        if (text.Length == 0) return null;
                        if (!TryDecimal(text, out decimal value) || value < min || value > max || (whole && value != decimal.Truncate(value)))
                        { result.Errors.Add(where + ": " + name + " '" + text + "' is outside " + min + ".." + max); fine = false; return null; }
                        return value;
                    }
                    draft.Power = Number("BasePower", 0, 1000); draft.Speed = Number("SpeedMul", 0, 10);
                    draft.Angle = Number("AreaAngle", 0, 180); draft.Radius = Number("AreaRadius", 0, 100);
                    draft.Length = Number("AreaLength", 0, 200); draft.AreaSpeed = Number("AreaSpeed", 0, 200);
                    draft.Delay = Number("AreaDelay", 0, 30); draft.Shots = Number("Shots", 0, 200, true);
                    draft.Interval = Number("Interval", 0, 10); draft.Scatter = Number("Scatter", 0, 1, true);
                    foreach (string pair in Cell("Params").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int eq = pair.IndexOf('=');
                        string key = eq > 0 ? pair.Substring(0, eq).Trim() : "";
                        if (!ValidKey(key) || !TryDecimal(pair.Substring(eq + 1).Trim(), out decimal value) || draft.Params.ContainsKey(key))
                        { result.Errors.Add(where + ": bad or repeated parameter '" + pair.Trim() + "'"); fine = false; continue; }
                        draft.Params[key] = value;
                    }
                    if (draft.Mode.Length == 0) draft.Mode = "new";
                    if (draft.Mode != "new" && draft.Mode != "replace") { result.Errors.Add(where + ": Mode must be new or replace"); fine = false; }
                    if ((draft.Handler.Length == 0) == (draft.Pending.Length == 0))
                    { result.Errors.Add(where + ": exactly one of Handler and Pending must be filled"); fine = false; }
                    if (draft.Feature.Length > 0 && (FeatureOf(draft.Feature) == SpellLegacyFeature.None || draft.Handler.Length == 0))
                    { result.Errors.Add(where + ": unknown Feature '" + draft.Feature + "' or Feature without Handler"); fine = false; }
                    if (draft.Gate.Length > 0 && draft.Gate != "final" && draft.Gate != "open" && draft.Gate != "test") { result.Errors.Add(where + ": Gate must be final, open or test"); fine = false; }
                    // A row may not open itself: the only rows usable whatever their final are the five summons (the current
                    // exception). Every other final consonant opens through the unlock sheet, so a rule sheet cannot leak one.
                    if (draft.Gate == "open" && categories[index - 1] != SpellCategory.Summon)
                    { result.Errors.Add(where + ": Gate open is the summon exception only (a final consonant opens through Unlock308.csv)"); fine = false; }
                    // Gate test narrows a row: its final's TEST unlock opens it, the main-game grant of that final does not
                    // (SpellUnlockPolicy308.RuleFor). Only a handler row behind a final consonant can be narrowed this way; the
                    // rows that cast before #308 keep their own gates.
                    if (draft.Gate == "test" && (glyphs[index - 1].Final == SpellFinal.None || draft.Handler.Length == 0 || draft.Feature.Length > 0))
                    { result.Errors.Add(where + ": Gate test needs a handler row with a final consonant and without a legacy feature"); fine = false; }
                    if (draft.Inherit.Length > 0 && draft.Inherit != "base") { result.Errors.Add(where + ": Inherit must be empty or base"); fine = false; }
                    if (draft.Kind.Length > 0 && !IsName(typeof(SpellKind), draft.Kind)) { result.Errors.Add(where + ": unknown Kind '" + draft.Kind + "'"); fine = false; }
                    if (draft.Shape.Length > 0 && !IsName(typeof(AreaShape), draft.Shape)) { result.Errors.Add(where + ": unknown AreaShape '" + draft.Shape + "'"); fine = false; }
                    if (!fine) continue;
                    var existing = drafts[index - 1];
                    if (draft.Mode == "new")
                    {
                        if (existing != null) { result.Errors.Add(where + ": the glyph already has a row in " + existing.Sheet + " (use Mode replace)"); continue; }
                    }
                    else
                    {
                        if (existing == null) { result.Errors.Add(where + ": replace needs an existing row"); continue; }
                        if (existing.Replaced) { result.Errors.Add(where + ": the glyph was already replaced by " + existing.Sheet); continue; }
                        draft.Replaced = true;
                        result.Notes.Add(letter + ": " + existing.Sheet + " row replaced by " + sheet.Name);
                    }
                    drafts[index - 1] = draft;
                }
            }

            // 3. rows: inheritance from the same initial + medial without a final, kinds, parameters
            var lines = new List<string>(SpellGrammar308.Count + 5);
            var built = new SpellRow[SpellGrammar308.Count];
            for (int i = 0; i < SpellGrammar308.Count; i++)
            {
                var glyph = glyphs[i]; var draft = drafts[i];
                var row = new SpellRow
                {
                    Letter = glyph.Letter.ToString(), Index = i + 1, Element = glyph.Element, Medial = glyph.Medial, Final = glyph.Final,
                    Category = categories[i], Kind = SpellGrammar308.DefaultKind(categories[i]),
                };
                built[i] = row;
                decimal power = 0, speed = 0, angle = 0, radius = 0, length = 0, areaSpeed = 0, delay = 0, shots = 0, interval = 0, scatter = 0;
                string shape = "None", kind = "";
                if (draft == null)
                {
                    if (categories[i] != SpellCategory.Blank)
                    {
                        result.MissingSheet.Add(row.Letter);
                        if (strict) result.Errors.Add(row.Letter + ": assigned glyph without a rule row");
                    }
                }
                else
                {
                    row.Handler = draft.Handler; row.Pending = draft.Pending;
                    row.Gate = draft.Gate == "open" ? SpellGateMode.Open : draft.Gate == "test" ? SpellGateMode.Test : SpellGateMode.Final;
                    row.Feature = FeatureOf(draft.Feature);
                    Draft parent = null;
                    if (draft.Inherit == "base")
                    {
                        int baseIndex = SpellGrammar308.IndexOf(SpellGrammar308.BaseOf(glyph.Letter));
                        parent = baseIndex > 0 && baseIndex != i + 1 ? drafts[baseIndex - 1] : null;
                        if (parent == null || parent.Handler.Length == 0 || parent.Inherit.Length > 0)
                        { result.Errors.Add(row.Letter + ": Inherit base needs a handled base row that does not inherit itself"); parent = null; }
                    }
                    decimal Pick(decimal? own, decimal? inherited) => own ?? inherited ?? 0;
                    power = Pick(draft.Power, parent?.Power); speed = Pick(draft.Speed, parent?.Speed);
                    angle = Pick(draft.Angle, parent?.Angle); radius = Pick(draft.Radius, parent?.Radius);
                    length = Pick(draft.Length, parent?.Length); areaSpeed = Pick(draft.AreaSpeed, parent?.AreaSpeed);
                    delay = Pick(draft.Delay, parent?.Delay); shots = Pick(draft.Shots, parent?.Shots);
                    interval = Pick(draft.Interval, parent?.Interval); scatter = Pick(draft.Scatter, parent?.Scatter);
                    shape = draft.Shape.Length > 0 ? draft.Shape : parent != null && parent.Shape.Length > 0 ? parent.Shape : "None";
                    kind = draft.Kind.Length > 0 ? draft.Kind : parent != null ? parent.Kind : "";
                    if (kind.Length > 0) row.Kind = (SpellKind)Enum.Parse(typeof(SpellKind), kind);
                    if (!SpellGrammar308.KindFits(categories[i], row.Kind))
                        result.Errors.Add(row.Letter + ": Kind " + row.Kind + " does not fit category " + categories[i]);
                    row.AreaShape = (AreaShape)Enum.Parse(typeof(AreaShape), shape);
                    row.BasePower = (float)power; row.ProjectileSpeedMul = (float)speed;
                    row.AreaAngle = (float)angle; row.AreaRadius = (float)radius; row.AreaLength = (float)length;
                    row.AreaSpeed = (float)areaSpeed; row.AreaImpactDelay = (float)delay; row.VolleyShots = (int)shots;
                    row.VolleyInterval = (float)interval; row.ScatterVolley = scatter != 0;
                    var parameters = new List<SpellParam>(draft.Params.Count);
                    // WP-11: a book row that declares a handover (SpellTakeover308) carries its handler's parameters like a new row.
                    bool handled = row.Feature == SpellLegacyFeature.None || SpellTakeover308.Declared(row);
                    IReadOnlyList<SpellParamSpec> declared = handled && row.HasHandler && catalog != null ? catalog(row.Handler) : null;
                    if (handled && row.HasHandler && catalog != null && declared == null)
                        result.Errors.Add(row.Letter + ": handler '" + row.Handler + "' is not registered");
                    foreach (var pair in draft.Params)
                    {
                        SpellParamSpec? spec = FindSpec(CoreKeys, pair.Key) ?? FindSpec(declared, pair.Key);
                        bool mustKnow = !handled || !row.HasHandler || catalog != null;
                        if (spec == null && mustKnow) result.Errors.Add(row.Letter + ": unknown parameter '" + pair.Key + "'");
                        if (spec != null && !spec.Value.Accepts((float)pair.Value))
                            result.Errors.Add(row.Letter + ": " + pair.Key + "=" + Text(pair.Value) + " is outside " + spec.Value.Min + ".." + spec.Value.Max);
                        parameters.Add(new SpellParam { Key = pair.Key, Value = (float)pair.Value });
                    }
                    if (declared != null)
                        foreach (var spec in declared)
                            if (spec.Required && !draft.Params.ContainsKey(spec.Key))
                                result.Errors.Add(row.Letter + ": required parameter '" + spec.Key + "' of " + row.Handler + " is missing");
                    row.Params = parameters.ToArray();
                }
                var text = new StringBuilder();
                text.Append(row.Index).Append('|').Append(row.Letter).Append('|').Append(row.Element).Append('|').Append(row.Medial).Append('|')
                    .Append(row.Final).Append('|').Append(row.Category).Append('|').Append(row.Kind).Append('|').Append(row.Handler).Append('|')
                    .Append(row.Pending).Append('|').Append(row.Gate).Append('|').Append(row.Feature).Append('|').Append(Text(power)).Append('|')
                    .Append(Text(speed)).Append('|').Append(shape).Append('|').Append(Text(angle)).Append('|').Append(Text(radius)).Append('|')
                    .Append(Text(length)).Append('|').Append(Text(areaSpeed)).Append('|').Append(Text(delay)).Append('|').Append(Text(shots)).Append('|')
                    .Append(Text(interval)).Append('|').Append(Text(scatter)).Append('|');
                if (draft != null)
                {
                    bool first = true;
                    foreach (var pair in draft.Params)
                    { if (!first) text.Append(';'); first = false; text.Append(pair.Key).Append('=').Append(Text(pair.Value)); }
                }
                lines.Add(text.ToString());
            }

            // 4. final-consonant unlock rules: the five finals, each exactly once
            var unlocks = new List<SpellUnlockRule>();
            var unlockRows = ParseCsv(unlockCsv);
            if (unlockRows.Count == 0 || string.Join(",", unlockRows[0]) != string.Join(",", UnlockColumns))
                result.Errors.Add("unlock sheet: unexpected header");
            for (int r = 1; r < unlockRows.Count; r++)
            {
                string[] cells = unlockRows[r];
                int jamo = cells.Length == 4 && cells[0].Trim().Length == 1 ? InitialJamo.IndexOf(cells[0].Trim()[0]) : -1;
                string ledger = cells.Length == 4 ? cells[1].Trim() : "", evidence = cells.Length == 4 ? cells[2].Trim() : "", granted = cells.Length == 4 ? cells[3].Trim() : "";
                if (jamo < 0 || !ValidId(ledger) || (evidence.Length > 0 && !ValidId(evidence)) || (granted != "0" && granted != "1"))
                { result.Errors.Add("unlock sheet row " + r + ": bad final, id or GrantedInMain"); continue; }
                var final = (SpellFinal)(jamo + 1);
                if (unlocks.Exists(u => u.Final == final)) { result.Errors.Add("unlock sheet row " + r + ": final repeated"); continue; }
                unlocks.Add(new SpellUnlockRule { Final = final, LedgerId = ledger, EvidenceId = evidence, GrantedInMain = granted == "1" });
            }
            if (unlocks.Count != 5) result.Errors.Add("unlock sheet: " + unlocks.Count + " finals, expected 5");
            unlocks.Sort((a, b) => ((int)a.Final).CompareTo((int)b.Final));
            foreach (var rule in unlocks)
                lines.Add("U|" + rule.Final + "|" + rule.LedgerId + "|" + rule.EvidenceId + "|" + (rule.GrantedInMain ? "1" : "0"));

            result.Rows = built; result.Unlocks = unlocks.ToArray();
            result.CanonicalText = string.Join("\n", lines);
            result.TableHash = Sha256(result.CanonicalText);
            return result;
        }

        // ---- a row for a book without imported rows (the behaviour before #308, see SpellGrammar308) ----
        public static bool TryLegacyRow(char letter, SpellBookSO.Entry[] entries, out SpellRow row)
        {
            row = null;
            if (!SpellGrammar308.TryDecompose(letter, out var glyph))
                return FindEntry(entries, letter, out var outside) && FromEntry(letter, default, false, outside, SpellLegacyFeature.Book, out row);
            SpellLegacyFeature feature = SpellGrammar308.LegacyFeatureOf(glyph);
            switch (feature)
            {
                case SpellLegacyFeature.Ward: row = Feature(glyph, SpellKind.Ward, feature, "ea.ward"); return true;
                case SpellLegacyFeature.BuffG: row = Feature(glyph, SpellKind.Buff, feature, "ea.buff"); return true;
                case SpellLegacyFeature.Guk: row = Feature(glyph, SpellKind.Field, feature, "field.lift"); return true;
                case SpellLegacyFeature.Mum: row = Feature(glyph, SpellKind.Field, feature, "field.bridge"); return true;
                case SpellLegacyFeature.Giyeok:
                    return FindEntry(entries, SpellGrammar308.BaseOf(letter), out var parent) && FromEntry(letter, glyph, true, parent, feature, out row);
                default:
                    return FindEntry(entries, letter, out var entry) && FromEntry(letter, glyph, true, entry, SpellLegacyFeature.Book, out row);
            }
        }

        // ---- import guard (SPEC-SPELL-120-308 principle 2: the glyphs that resolve today do not change) ----
        // What importing these rows into a book with these entries would change. drift = a glyph that casts today would cast
        // differently, stop casting, or a legacy row the book cannot back. takeovers = a glyph that casts today and that the
        // table hands to a registered handler (Feature None): a declared behaviour change, never silent. Both empty = the
        // import changes nothing for this book. The importer refuses drift, and refuses takeovers unless told to accept them.
        public static void LegacyDrift(IReadOnlyList<SpellRow> rows, SpellBookSO.Entry[] entries, List<string> drift, List<string> takeovers)
        {
            if (rows == null || rows.Count != SpellGrammar308.Count) { drift.Add("the table does not have 120 rows"); return; }
            var open = new OpenGate308();
            for (int i = 1; i <= SpellGrammar308.Count; i++)
            {
                char letter = SpellGrammar308.LetterAt(i);
                SpellRow now = rows[i - 1];
                bool had = TryLegacyRow(letter, entries, out SpellRow before);
                bool legacyNow = now != null && now.HasHandler && now.Feature != SpellLegacyFeature.None;
                if (!had && !legacyNow) continue;   // a new row, or a misfire on both sides
                if (!had) { drift.Add(letter + ": the table gives it the legacy feature " + now.Feature + " but this book has no entry behind it"); continue; }
                if (!legacyNow)
                {
                    if (now != null && now.HasHandler) takeovers.Add(letter + ": " + before.Feature + " (" + before.Handler + ") -> handler route " + now.Handler);
                    else drift.Add(letter + ": casts today (" + before.Feature + ") but the table gives it no handler");
                    continue;
                }
                if (before.Feature != now.Feature) { drift.Add(letter + ": feature " + before.Feature + " -> " + now.Feature); continue; }
                SpellResolveCore308.Resolve(letter, 1f, 1f, before, open, out SpellCast a);
                SpellResolveCore308.Resolve(letter, 1f, 1f, now, open, out SpellCast b);
                string difference = CastDifference(a, b);
                if (difference == null)
                    foreach (string key in new[] { SpellGrammar308.SpikesKey, SpellGrammar308.SeedKey, SpellGrammar308.PierceKey })
                        if ((before.F(key, 0f) > 0f) != (now.F(key, 0f) > 0f)) difference = key;
                if (difference != null) drift.Add(letter + ": " + difference + " differs from the book's entry");
            }
            if (entries != null)
                foreach (var entry in entries)
                    if (!string.IsNullOrEmpty(entry.Letter) && SpellGrammar308.IndexOf(entry.Letter[0]) == 0)
                        drift.Add(entry.Letter + ": an entry outside the 120-glyph grid would stop resolving once the book carries a table");
        }

        sealed class OpenGate308 : ISpellGate { public SpellResolveStatus Judge(SpellRow row) => SpellResolveStatus.Ok; }

        static string CastDifference(in SpellCast a, in SpellCast b)
        {
            if (a.Kind != b.Kind) return "kind " + a.Kind + " / " + b.Kind;
            if (a.Element != b.Element) return "element " + a.Element + " / " + b.Element;
            if (!a.Power.Equals(b.Power)) return "base power " + a.Power.ToString("R", CultureInfo.InvariantCulture) + " / " + b.Power.ToString("R", CultureInfo.InvariantCulture);
            if (!a.SpeedMul.Equals(b.SpeedMul)) return "projectile speed " + a.SpeedMul.ToString("R", CultureInfo.InvariantCulture) + " / " + b.SpeedMul.ToString("R", CultureInfo.InvariantCulture);
            var x = a.Area; var y = b.Area;
            if (x.Shape != y.Shape || !x.Angle.Equals(y.Angle) || !x.Radius.Equals(y.Radius) || !x.Length.Equals(y.Length) || !x.Speed.Equals(y.Speed) ||
                !x.ImpactDelay.Equals(y.ImpactDelay) || x.Shots != y.Shots || !x.Interval.Equals(y.Interval) || x.ScatterVolley != y.ScatterVolley)
                return "area geometry";
            return null;
        }

        static bool FindEntry(SpellBookSO.Entry[] entries, char letter, out SpellBookSO.Entry entry)
        {
            if (entries != null)
                foreach (var candidate in entries)
                    if (!string.IsNullOrEmpty(candidate.Letter) && candidate.Letter[0] == letter) { entry = candidate; return true; }
            entry = default; return false;
        }

        static SpellRow Feature(in SpellGlyph glyph, SpellKind kind, SpellLegacyFeature feature, string handler)
        {
            return new SpellRow
            {
                Letter = glyph.Letter.ToString(), Index = glyph.Index, Element = glyph.Element, Medial = glyph.Medial, Final = glyph.Final,
                Category = glyph.Category, Kind = kind, Handler = handler, Feature = feature,
            };
        }

        static bool FromEntry(char letter, in SpellGlyph glyph, bool inGrid, in SpellBookSO.Entry entry, SpellLegacyFeature feature, out SpellRow row)
        {
            row = new SpellRow
            {
                Letter = letter.ToString(), Index = inGrid ? glyph.Index : 0, Element = entry.Element, Kind = entry.Kind, Feature = feature,
                Medial = inGrid ? glyph.Medial : default, Final = inGrid ? glyph.Final : SpellFinal.None,
                Category = inGrid ? glyph.Category : SpellCategory.Attack, Gate = SpellGateMode.Open,
                Handler = feature == SpellLegacyFeature.Giyeok ? "ea.giyeok" : CoreHandler(entry.Kind, entry.AreaShape),
                BasePower = entry.BasePower, ProjectileSpeedMul = entry.ProjectileSpeedMul, AreaShape = entry.AreaShape,
                AreaAngle = entry.AreaAngle, AreaRadius = entry.AreaRadius, AreaLength = entry.AreaLength, AreaSpeed = entry.AreaSpeed,
                AreaImpactDelay = entry.AreaImpactDelay, VolleyShots = entry.VolleyShots, VolleyInterval = entry.VolleyInterval,
                ScatterVolley = entry.ScatterVolley,
            };
            var traits = new List<SpellParam>(1);
            foreach (string key in new[] { SpellGrammar308.SpikesKey, SpellGrammar308.SeedKey, SpellGrammar308.PierceKey })
            {
                float value = SpellGrammar308.LegacyTrait(letter, key);
                if (value != 0f) traits.Add(new SpellParam { Key = key, Value = value });
            }
            row.Params = traits.ToArray();
            return true;
        }

        public static string CoreHandler(SpellKind kind, AreaShape shape)
        {
            switch (kind)
            {
                case SpellKind.Parry: return "core.parry";
                case SpellKind.Summon: return "core.summon";
                case SpellKind.Field: return "core.field";
                case SpellKind.Buff: return "core.buff";
                case SpellKind.Ward: return "core.ward";
                case SpellKind.Install: return "core.install";
            }
            switch (shape)
            {
                case AreaShape.Cone: return "core.cone";
                case AreaShape.Circle: return "core.circle";
                case AreaShape.Path: return "core.path";
                case AreaShape.Volley: return "core.volley";
                default: return "core.single";
            }
        }

        // ---- shared helpers ----
        public static SpellLegacyFeature FeatureOf(string text)
        {
            switch (text)
            {
                case "book": return SpellLegacyFeature.Book;
                case "ward": return SpellLegacyFeature.Ward;
                case "buff.g": return SpellLegacyFeature.BuffG;
                case "guk": return SpellLegacyFeature.Guk;
                case "giyeok": return SpellLegacyFeature.Giyeok;
                case "mum": return SpellLegacyFeature.Mum;
                default: return SpellLegacyFeature.None;
            }
        }

        static bool IsName(Type type, string text) => Array.IndexOf(Enum.GetNames(type), text) >= 0;

        static SpellParamSpec? FindSpec(IReadOnlyList<SpellParamSpec> specs, string key)
        {
            if (specs != null)
                for (int i = 0; i < specs.Count; i++)
                    if (specs[i].Key == key) return specs[i];
            return null;
        }

        // lower-case words joined by dots: "return.pause"
        static bool ValidKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key[0] == '.' || key[key.Length - 1] == '.') return false;
            bool dot = true;
            foreach (char c in key)
            {
                if (c == '.') { if (dot) return false; dot = true; continue; }
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9')) return false;
                if (dot && !(c >= 'a' && c <= 'z')) return false;
                dot = false;
            }
            return true;
        }

        static bool ValidId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (char c in id) if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_') return false;
            return true;
        }

        // Plain decimals only, at most six significant digits (a float keeps them, so the asset and the sheet print the same).
        static bool TryDecimal(string text, out decimal value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;
            int digits = 0; bool leading = true;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '-' && i == 0) continue;
                if (c == '.') continue;
                if (c < '0' || c > '9') return false;
                if (leading && c == '0') continue;
                leading = false; digits++;
            }
            if (digits > 6) return false;
            return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
        }

        // "0.30" -> "0.3", "12.0" -> "12", "-0" -> "0"
        public static string Text(decimal value)
        {
            string text = value.ToString(CultureInfo.InvariantCulture);
            if (text.IndexOf('.') >= 0) text = text.TrimEnd('0').TrimEnd('.');
            return text == "-0" || text.Length == 0 ? "0" : text;
        }

        // BOM removed, line ends normalised: the same bytes on every machine.
        public static string Normalise(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text[0] == '﻿') text = text.Substring(1);
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        public static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        static string SourceHash(string canonicalCsv, List<SpellSheet308> sortedSheets, string unlockCsv)
        {
            var text = new StringBuilder();
            text.Append("csv\n").Append(Sha256(Normalise(canonicalCsv))).Append('\n');
            foreach (var sheet in sortedSheets) text.Append(sheet.Name).Append('\n').Append(Sha256(Normalise(sheet.Text))).Append('\n');
            text.Append("unlock\n").Append(Sha256(Normalise(unlockCsv)));
            return Sha256(text.ToString());
        }

        // Quoted cells may hold commas, quotes ("") and line breaks. Empty lines and lines whose first cell starts with # are skipped.
        public static List<string[]> ParseCsv(string text)
        {
            var rows = new List<string[]>();
            text = Normalise(text);
            var cells = new List<string>(); var cell = new StringBuilder(); bool quoted = false, any = false;
            void EndRow()
            {
                cells.Add(cell.ToString()); cell.Length = 0;
                bool blank = cells.Count == 1 && cells[0].Length == 0 && !any;
                if (!blank && !(cells[0].Length > 0 && cells[0][0] == '#')) rows.Add(cells.ToArray());
                cells.Clear(); any = false;
            }
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else quoted = false; }
                    else cell.Append(c);
                }
                else if (c == '"') { quoted = true; any = true; }
                else if (c == ',') { cells.Add(cell.ToString()); cell.Length = 0; any = true; }
                else if (c == '\n') EndRow();
                else cell.Append(c);
            }
            if (cell.Length > 0 || cells.Count > 0) EndRow();
            return rows;
        }
    }
}
