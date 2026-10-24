using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Oheangbu.Vol2.Rules;
namespace Vol2V3.Tests
{
    // S5-a: the vol.2 table v11 importer (Vol2TableV11Importer) against the Assets byte copies and the independent Python manifest.
    public static class Cases_TableV11
    {
        public const string DataFolder = "Oheangbu/Assets/_Project/Data/Spells/Vol2";
        public static void Run(Report r, Context c)
        {
            string dir = Path.Combine(c.Root, DataFolder);
            byte[] numbers = File.ReadAllBytes(Path.Combine(dir, "vol2_numbers_v3.csv")), table = File.ReadAllBytes(Path.Combine(dir, "vol2_table120_v11.csv"));
            var good = Vol2TableV11Importer.Import(numbers, table);
            r.Check("import", good.CanCast, string.Join(" | ", good.Errors)); if (!good.CanCast) return;
            r.Equal("numbers-rows", 414, good.Numbers.Rows.Count); r.Equal("spells", 120, good.Spells.Count);
            r.Check("numbers-validated", good.Numbers.IsValidated);
            r.Equal("kinds-bare", 20, good.Spells.Count(x => x.Kind == SpellKind.Bare)); r.Equal("kinds-normal", 80, good.Spells.Count(x => x.Kind == SpellKind.Normal)); r.Equal("kinds-peak", 20, good.Spells.Count(x => x.Kind == SpellKind.Peak));
            r.Check("manifest-keys", good.Manifest.SourceHashes.Count == 2 && good.Manifest.SourceHashes.ContainsKey("numbers") && good.Manifest.SourceHashes.ContainsKey("table"));
            r.Equal("numbers-hash-is-source", c.Golden.RootElement.GetProperty("hashes").GetProperty("numbers").GetString(), good.Manifest.SourceHashes["numbers"]);
            // 걱: D308-145 heal exception. Terms come from Build(), cost from DefenceNumbers.
            var geok = good.Spells.Single(x => x.Letter == '걱');
            r.Check("geok-recovery", geok.EffectTerms.Count == 0 && geok.Cost == 26 && c.Plan('걱', good.Numbers).IsRecovery);
            foreach (var row in good.Spells.Where(x => x.Letter != '걱'))
            {
                var p = GlyphRulesV3.Build(row.Glyph, good.Numbers);
                r.Check("terms/" + row.Letter, row.Cost == p.Cost && row.EffectTerms.Select(t => t.Kind + ":" + t.Amount + ":" + t.Count).SequenceEqual(p.EffectTerms.Select(t => t.Kind + ":" + t.Amount + ":" + t.Count)));
            }
            foreach (var row in good.Spells.Where(x => x.Kind == SpellKind.Peak && x.Letter != '걱'))
                r.Check("peak-declared/" + row.Letter, Regex.IsMatch(row.Note, @"총 작용량\s*" + Regex.Escape(Vol2ImportManifestDto.Number(row.EffectTerms.Sum(t => t.Total))) + @"\(상한"));
            // The independent Python hash and the pinned source hashes.
            using (var frozen = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(c.Stage, "Golden/import_manifest_v11.json"))))
            {
                var f = frozen.RootElement;
                r.Equal("python-dto-hash", f.GetProperty("dtoHash").GetString(), good.Manifest.DtoHash);
                r.Equal("source-hash/numbers", f.GetProperty("sourceHashes").GetProperty("numbers").GetString(), good.Manifest.SourceHashes["numbers"]);
                r.Equal("source-hash/table", f.GetProperty("sourceHashes").GetProperty("table").GetString(), good.Manifest.SourceHashes["table"]);
                var trusted = new Vol2ImportManifestDto(f.GetProperty("schema").GetString(), f.GetProperty("importer").GetString(),
                    f.GetProperty("sourceHashes").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()), f.GetProperty("dtoHash").GetString());
                r.Check("frozen-manifest-load", Vol2TableV11Importer.Import(numbers, table, trusted, trusted.DtoHash).CanCast);
                var changed = table.Concat(new byte[] { 10 }).ToArray();
                Reject(r, "table-byte-change", Vol2TableV11Importer.Import(numbers, changed, trusted, trusted.DtoHash), "ManifestMismatch");
                Reject(r, "manifest-hash-required", Vol2TableV11Importer.Import(numbers, table, trusted), "ManifestMismatch");
                var otherRows = CsvV3.Parse(numbers).Select(x => x.Fields.ToArray()).ToList(); otherRows[3][7] += "x"; var otherNumbers = Context.Csv(otherRows);
                Reject(r, "numbers-byte-change", Vol2TableV11Importer.Import(otherNumbers, table, trusted, trusted.DtoHash), "ManifestMismatch");
                // SO side: DTO rebuilt from stored data must hash to the frozen DTO hash (LoadDto).
                var roundTrip = new NumbersV3Dto(good.Numbers.Rows.Reverse());
                var copied = good.Spells.Select(x => new Vol2SpellRowDto(x.Line, x.RawText, x.Fields, x.Glyph, x.Cost, x.EffectTerms.Select(t => new EffectTerm(t.Kind, t.Amount, t.Count)))).ToArray();
                var loaded = new Vol2ContractValidator().LoadDto(roundTrip, copied, good.Manifest, trusted);
                r.Check("so-load", loaded.CanCast && loaded.Numbers.IsValidated && !roundTrip.IsValidated, string.Join(" | ", loaded.Errors));
                r.Check("so-number-tamper", !new Vol2ContractValidator().LoadDto(new NumbersV3Dto(roundTrip.Rows.Select(x => x.Key == "action.instant" ? x.WithValue(0.8m) : x)), copied, good.Manifest, trusted).CanCast);
                var cost = copied.ToArray(); cost[5] = new Vol2SpellRowDto(cost[5].Line, cost[5].RawText, cost[5].Fields, cost[5].Glyph, cost[5].Cost + 1, cost[5].EffectTerms);
                r.Check("so-cost-tamper", !new Vol2ContractValidator().LoadDto(roundTrip, cost, good.Manifest, trusted).CanCast);
                int peak = Array.FindIndex(copied, x => x.Letter == '각'); var terms = copied.ToArray();
                terms[peak] = new Vol2SpellRowDto(terms[peak].Line, terms[peak].RawText, terms[peak].Fields, terms[peak].Glyph, terms[peak].Cost, new[] { new EffectTerm(EffectKind.Instant, 0.8m, 1), new EffectTerm(EffectKind.Residue, 0.8m, 1) });
                r.Check("so-terms-tamper", !new Vol2ContractValidator().LoadDto(roundTrip, terms, good.Manifest, trusted).CanCast);
                var text = copied.ToArray(); var fld = text[3].Fields.ToArray(); fld[5] += "!";
                text[3] = new Vol2SpellRowDto(text[3].Line, text[3].RawText, fld, text[3].Glyph, text[3].Cost, text[3].EffectTerms);
                r.Check("so-text-tamper", !new Vol2ContractValidator().LoadDto(roundTrip, text, good.Manifest, trusted).CanCast);
                r.Check("so-missing-row", !new Vol2ContractValidator().LoadDto(roundTrip, copied.Skip(1), good.Manifest, trusted).CanCast);
            }
            r.Equal("deterministic", good.Manifest.DtoHash, Vol2TableV11Importer.Import(numbers, table).Manifest.DtoHash);
            r.Equal("order-independent", good.Manifest.DtoHash, Vol2ImportManifestDto.HashText(Vol2ImportManifestDto.CanonicalDto(new NumbersV3Dto(good.Numbers.Rows.Reverse()), good.Spells.Reverse())));
            // Docs originals, when present, are the same bytes (the Assets copies are copies, not forks).
            string docNumbers = Path.Combine(c.Root, "Docs/Research/e0/numbers_v3.csv"), docTable = Path.Combine(c.Root, "Docs/Research/vol2/vol2_table120_v11.csv");
            if (File.Exists(docNumbers)) r.Equal("docs-numbers-same", Vol2ImportManifestDto.Sha256(File.ReadAllBytes(docNumbers)), good.Manifest.SourceHashes["numbers"]);
            if (File.Exists(docTable)) r.Equal("docs-table-same", Vol2ImportManifestDto.Sha256(File.ReadAllBytes(docTable)), good.Manifest.SourceHashes["table"]);
            // The v10 gate is not the v11 gate: the v10 importer still refuses v11 text, the v11 importer refuses the v10 table (걱 still an attack).
            var v10 = c.Copy(); v10["table"] = table;
            Reject(r, "v10-gate-refuses-v11", new Vol2ContractValidator().Import(v10), "UnparsedNarrative");
            string v10Table = Path.Combine(c.Root, "Docs/Research/debate25/vol2_table120_v10.csv");
            if (File.Exists(v10Table)) Reject(r, "v11-gate-refuses-v10-table", Vol2TableV11Importer.Import(numbers, File.ReadAllBytes(v10Table)), "GeokRecovery");

            var rows = CsvV3.Parse(table).Select(x => x.Fields.ToArray()).ToList();
            void Mutate(string id, char letter, int col, Func<string, string> edit, string code)
            {
                var copy = rows.Select(x => x.ToArray()).ToList(); var row = copy.Single(x => x[0] == letter.ToString());
                string old = row[col]; row[col] = edit(old); r.Check("mutation-applied/" + id, row[col] != old);
                Reject(r, id, Vol2TableV11Importer.Import(numbers, Context.Csv(copy)), code);
            }
            void Table(string id, Action<List<string[]>> edit, string code) { var copy = rows.Select(x => x.ToArray()).ToList(); edit(copy); Reject(r, id, Vol2TableV11Importer.Import(numbers, Context.Csv(copy)), code); }
            Table("header", x => x[0][0] = "Index", "TableHeader");
            Table("columns", x => x[1] = x[1].Take(9).ToArray(), "Columns");
            Table("duplicate-glyph", x => x[2] = x[1].ToArray(), "DuplicateLetter");
            Table("duplicate-definition", x => x[2][5] = x[1][5], "DuplicateDefinition");
            Table("table-count", x => x.RemoveAt(x.Count - 1), "TableCount");
            Table("blank", x => x[1][5] = "", "Blank");
            Table("illegal-glyph", x => x[1][0] = "A", "Glyph");
            Mutate("ellipsis", '가', 5, s => s + "…", "Ellipsis");
            Mutate("jamo-name", '가', 1, s => "ㄴ=화", "JamoName");
            Mutate("kind", '가', 4, s => "일반", "Kind");
            Mutate("cost", '가', 9, s => s.Replace("먹 11(TEST)", "먹 12(TEST)"), "Cost");
            Mutate("status", '가', 9, s => s.Replace("수치 TEST", "수치 LOCKED"), "Status");
            Mutate("parry-refund", '거', 9, s => s.Replace("환급 없음", "환급 있음"), "ParryPayment");
            Mutate("parry-failure", '거', 9, s => s.Replace("실패 14 고정", "실패 13 고정"), "ParryPayment");
            Mutate("target-cap", '넉', 9, s => s.Replace("시전당 효과 대상 상한 3", "시전당 효과 대상 상한 4"), "EffectTargetCap");
            Mutate("second-contact", '넉', 9, s => s.Replace("대상 ID)마다 최대 1회", "대상 ID)마다 여러 번"), "SecondContact");
            Mutate("second-delay", '넉', 9, s => s.Replace("0.3 s", "0.2 s"), "SecondContact");
            Mutate("seed-trigger", '멈', 9, s => s.Replace("둘째 접촉 없음", "둘째 접촉 있음"), "SeedTrigger");
            Mutate("borrowed-scope", '감', 9, s => s + " · 빌린 위력", "BorrowedScope");
            Mutate("peak-total", '각', 9, s => s.Replace("총 작용량 1.6(", "총 작용량 1.7("), "PeakDeclaration");
            Mutate("peak-cap", '각', 9, s => s.Replace("(상한 2.4)", "(상한 2.5)"), "PeakDeclaration");
            Mutate("peak-terms", '각', 9, s => s.Replace("즉시 0.7 + 재료 0.9", "즉시 0.8 + 재료 0.8"), "PeakTerms");
            Mutate("peak-term-name", '각', 9, s => s.Replace("즉시 0.7 + 재료 0.9", "즉시 0.7 + 폭발 0.9"), "UnparsedTerm");
            Mutate("geok-cooldown", '걱', 9, s => s.Replace("재사용 20 s(TEST)", "재사용 25 s(TEST)"), "GeokRecovery");
            Mutate("geok-heal-amount", '걱', 5, s => s.Replace("HP 30 / 3 s", "HP 40 / 3 s"), "GeokRecovery");
            Mutate("geok-material", '걱', 8, s => s.Replace("반응재료 없음", "목 재료"), "GeokRecovery");
            Mutate("geok-attack-total", '걱', 9, s => s + " · 총 작용량 2.65(상한 3.3) = 즉시 0.7", "GeokRecovery");
            // Numbers go through the unchanged v10 numbers gate.
            var numberRows = CsvV3.Parse(numbers).Select(x => x.Fields.ToArray()).ToList();
            var tamper = numberRows.Select(x => x.ToArray()).ToList(); tamper.Single(x => x[0] == "action.instant")[1] = "0.8";
            Reject(r, "numbers-gate", Vol2TableV11Importer.Import(Context.Csv(tamper), table), "CurrentContract");
            Reject(r, "missing-numbers", Vol2TableV11Importer.Import(null, table), "MissingSource");
            Reject(r, "missing-table", Vol2TableV11Importer.Import(numbers, new byte[0]), "MissingSource");
            Reject(r, "invalid-utf8", Vol2TableV11Importer.Import(numbers, new byte[] { 0xff, 0xfe }), "Utf8");
            Reject(r, "malformed-csv", Vol2TableV11Importer.Import(numbers, Encoding.UTF8.GetBytes("글자,초성\n\"unterminated")), "Format");
        }
        static void Reject(Report r, string id, Vol2ContractValidator.Result result, string code) =>
            r.Check(id, !result.CanCast && result.Numbers == null && result.Errors.Any(x => x.Code == code), "expected=" + code + " actual=" + string.Join(" | ", result.Errors.Take(6)));
    }
}
