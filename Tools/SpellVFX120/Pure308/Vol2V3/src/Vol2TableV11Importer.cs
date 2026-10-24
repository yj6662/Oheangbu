using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace Oheangbu.Vol2.Rules
{
    // S5-a (D308-149). Imports the integrated vol.2 table v11 (vol2_table120_v11.csv, same 10 columns as v10) together with the
    // E0 numbers CSV. The v10 importer (Vol2ContractValidator.Import) stays the gate for the v10 dialect and is not changed in logic;
    // v11 rewrote 비고 in all 120 rows, so its per-column narrative fingerprints cannot apply. What stays strict:
    //   - the numbers go through the same ReadNumbers/ValidateNumbers (414 rows, derived formulas, E0 current contract),
    //   - every row is decoded from its Unicode syllable and must agree with the jamo/name/kind columns, the cost in 비고 and the plan,
    //   - peak rows (19, without 걱) must declare "총 작용량 X(상한 Y) = 항 + 항" equal to the plan built from the numbers,
    //   - 걱 is the D308-145 self-heal exception: its text must carry the DefenceNumbersV3Dto values and its plan has no attack terms,
    //   - the whole table is pinned by the SHA-256 of its source bytes in the manifest (any text change is a new hash, never silent).
    // Rules never read table text at runtime; they are built from the numbers DTO. EffectTerms of every row come from Build().
    public static class Vol2TableV11Importer
    {
        public const string TableHeader = "글자,초성,중성,종성,구분,한줄정의,자국윤곽,몸거동,반응재료,비고";
        public static readonly string[] SourceNames = { "numbers", "table" };
        static readonly string[] TermNames = { "즉시", "둘째 접촉", "몸 재료", "뿌리 재료", "재료", "번짐", "되번짐", "작동", "벗김", "뒤쪽 가름", "귀환" };

        public static Vol2ContractValidator.Result Import(byte[] numbersCsv, byte[] tableCsv, Vol2ImportManifestDto expected = null, string roundTripDtoHash = null)
        {
            var errors = new List<Vol2ContractValidator.Issue>(); var rows = new List<Vol2SpellRowDto>();
            NumbersV3Dto numbers = null; Vol2ImportManifestDto manifest = null;
            void Error(string code, string where, string detail) => errors.Add(new Vol2ContractValidator.Issue(code, where, detail));
            if (numbersCsv == null || numbersCsv.Length == 0) Error("MissingSource", "numbers", "Required source bytes");
            if (tableCsv == null || tableCsv.Length == 0) Error("MissingSource", "table", "Required source bytes");
            if (errors.Count != 0) return new Vol2ContractValidator.Result(null, rows, null, errors);
            try
            {
                numbers = Vol2ContractValidator.ReadNumbers(numbersCsv, Error);
                Vol2ContractValidator.ValidateNumbers(numbers, Error);
                if (errors.Count == 0)
                {
                    rows = ReadSpells(tableCsv, numbers.Validated(), Error);
                    manifest = Vol2ImportManifestDto.Create(new Dictionary<string, byte[]> { ["numbers"] = numbersCsv, ["table"] = tableCsv }, numbers, rows);
                    if (expected != null && !expected.Matches(manifest, roundTripDtoHash)) Error("ManifestMismatch", "manifest", "Schema, importer, source or DTO/SO hash differs");
                }
            }
            catch (FormatException e) { Error("Format", "input", e.Message); }
            catch (OverflowException e) { Error("NumericRange", "numbers", e.Message); }
            catch (DivideByZeroException e) { Error("NumericRange", "numbers", e.Message); }
            catch (DecoderFallbackException e) { Error("Utf8", "input", e.Message); }
            return new Vol2ContractValidator.Result(numbers, rows, manifest, errors);
        }

        static decimal D(string s) => decimal.Parse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        static decimal? Num(string s, string pattern) { var m = Regex.Match(s, pattern); return m.Success ? D(m.Groups[1].Value) : (decimal?)null; }
        static string N(decimal v) => Vol2ImportManifestDto.Number(v);

        static List<Vol2SpellRowDto> ReadSpells(byte[] bytes, NumbersV3Dto n, Action<string, string, string> error)
        {
            var parsed = CsvV3.Parse(bytes); var result = new List<Vol2SpellRowDto>();
            if (parsed.Count == 0 || string.Join(",", parsed[0].Fields) != TableHeader) { error("TableHeader", "table:1", "Expected the 10-column vol.2 table header"); return result; }
            var d = new DefenceNumbersV3Dto(n);
            foreach (var r in parsed.Skip(1))
            {
                var f = r.Fields; string loc = "table:" + r.Line;
                if (f.Length != 10) { error("Columns", loc, "Expected 10 columns"); continue; }
                loc = f[0] + ":" + r.Line;
                if (f.Any(string.IsNullOrWhiteSpace)) { error("Blank", loc, "No blank fields"); continue; }
                if (f.Any(s => s.Contains("…") || s.Contains("..."))) error("Ellipsis", loc, "No omitted terms");
                if (f[0].Length != 1 || !GlyphRulesV3.TryDecode(f[0][0], out var g)) { error("Glyph", loc, "Illegal syllable"); continue; }
                string cho = "ㄱㄴㅁㅅㅇ"[(int)g.Body] + "=" + "목화토금수"[(int)g.Body];
                string jung = "ㅏㅓㅗㅜ"[(int)g.Frame] + "=" + new[] { "경로", "부착", "면", "둘레" }[(int)g.Frame];
                string jong = new[] { "없음", "ㄱ", "ㄴ", "ㅁ", "ㅅ", "ㅇ" }[(int)g.Verb] + "=" + new[] { "한 번", "남긴다", "번진다", "심는다", "가른다", "돌아온다" }[(int)g.Verb];
                if (f[1] != cho || f[2] != jung || f[3] != jong) error("JamoName", loc, "Unicode and jamo/name pairs disagree");
                if (f[4] != new[] { "무받침", "일반", "극치" }[(int)g.Kind]) error("Kind", loc, "Initial/final identity defines peak");
                var p = GlyphRulesV3.Build(g, n);
                decimal cost = Num(f[9], @"먹\s*(\d+)\s*\(TEST\)") ?? 0;
                if (cost != p.Cost) error("Cost", loc, "Cost differs from E0");
                if (!f[9].Contains("수치 TEST")) error("Status", loc, "Numbers must stay marked TEST");
                if (g.IsParry && (!f[9].Contains("먹 후불") || !f[9].Contains("환급 없음") || Num(f[9], @"실패\s*(\d+)\s*고정") != n.Get("combat.parry.failureCost")))
                    error("ParryPayment", loc, "Postpaid success 0/failure 14; refund 0");
                if (g.Letter != '엉' && string.Join(" ", f).Contains("빌린 위력")) error("BorrowedScope", loc, "Only 엉 may borrow projectile power");
                if (g.Letter == '걱')
                {
                    // D308-145: self-heal exception. Values come from DefenceNumbersV3Dto, never from this text.
                    string heal = "HP " + N(d.HealingHp) + " / " + N(d.HealingLifeUs / 1000000m) + " s";
                    if (!p.IsRecovery || p.EffectTerms.Count != 0 || p.Total != 0) error("GeokRecovery", loc, "걱 must be a heal-only plan without attack terms");
                    if (!f[5].Contains(heal) || !f[7].Contains(heal)) error("GeokRecovery", loc, "Definition/behaviour must carry '" + heal + "'");
                    if (!f[9].Contains("재사용 " + N(d.HealingCooldownUs / 1000000m) + " s(TEST)")) error("GeokRecovery", loc, "Cooldown differs from DefenceNumbers");
                    if (!f[8].Contains("반응재료 없음") || !f[9].Contains("반응재료 없음")) error("GeokRecovery", loc, "걱 has no reaction material");
                    if (f[5].Contains("총 작용량") || f[9].Contains("총 작용량")) error("GeokRecovery", loc, "걱 declares no attack total");
                }
                else
                {
                    if (p.Limited && Num(f[9], @"시전당 효과 대상 상한\s*(\d+)") != n.Get("action.effectTargetCap")) error("EffectTargetCap", loc, "One common set of first three targets");
                    if (g.Frame == Frame.Attachment && g.Verb != Verb.Once && g.Verb != Verb.Seed && (!f[9].Contains("대상 ID)마다 최대 1회") || !f[9].Contains("0.3 s")))
                        error("SecondContact", loc, "Per cast/target once after 0.3s on separate swing");
                    if (g.Frame == Frame.Attachment && g.Verb == Verb.Seed && (!f[9].Contains("둘째 접촉 없음") || !f[5].Contains("피해를 받을 때")))
                        error("SeedTrigger", loc, "Damage received, no second contact");
                    if (g.Kind == SpellKind.Peak) PeakDeclaration(g, p, f, n, error, loc);
                }
                result.Add(new Vol2SpellRowDto(r.Line, r.Raw, f, g, cost, p.EffectTerms));
            }
            if (result.Count != 120) error("TableCount", "table", "Expected 120 rows");
            foreach (var group in result.GroupBy(r => r.Letter)) if (group.Count() != 1) error("DuplicateLetter", group.Key.ToString(), "Repeated glyph");
            foreach (var group in result.GroupBy(r => Regex.Replace(r.Definition, @"\s+", " "))) if (group.Count() != 1) error("DuplicateDefinition", string.Join(",", group.Select(r => r.Letter)), "Repeated definition");
            foreach (var k in new[] { SpellKind.Bare, SpellKind.Normal, SpellKind.Peak }) if (result.Count(r => r.Kind == k) != (k == SpellKind.Normal ? 80 : 20)) error("KindCount", k.ToString(), "Expected 20/80/20");
            return result;
        }

        static void PeakDeclaration(Glyph g, GlyphRulesV3.Plan p, string[] f, NumbersV3Dto n, Action<string, string, string> error, string loc)
        {
            var m = Regex.Match(f[9], @"총 작용량\s*(\d+(?:\.\d+)?)\s*\(상한\s*(\d+(?:\.\d+)?)\)\s*=\s*([^·]+)");
            if (!m.Success || D(m.Groups[1].Value) != p.Total || D(m.Groups[2].Value) != n.Get("peak." + g.Letter + ".cap")) { error("PeakDeclaration", loc, "Declaration/cap differs from the plan built from E0"); return; }
            var declared = new List<decimal>();
            foreach (string part in m.Groups[3].Value.Trim().Split(new[] { " + " }, StringSplitOptions.None))
            {
                var t = Regex.Match(part.Trim(), @"^(.+?)\s*(\d+(?:\.\d+)?)(?:×(\d+))?$");
                if (!t.Success) { error("UnparsedTerm", loc, part); continue; }
                string name = t.Groups[1].Value.Trim();
                if (!TermNames.Contains(name)) error("UnparsedTerm", loc, name);
                declared.Add(D(t.Groups[2].Value) * (t.Groups[3].Success ? D(t.Groups[3].Value) : 1));
            }
            if (!declared.OrderBy(x => x).SequenceEqual(p.EffectTerms.Select(t => t.Total).OrderBy(x => x))) error("PeakTerms", loc, "Formula terms differ from the plan terms");
        }
    }
}
