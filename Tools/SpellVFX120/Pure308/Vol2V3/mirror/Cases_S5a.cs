using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Oheangbu.Vol2.Import;
using Oheangbu.Vol2.Rules;
namespace Vol2V3.Tests
{
    // S5-a: the engine-free import layer (MiniJson, Vol2Snapshot, Vol2Golden) that the Unity ScriptableObjects and editor commands sit on.
    public static class Cases_S5a
    {
        public static void Run(Report r, Context c)
        {
            string data = Path.Combine(c.Root, Cases_TableV11.DataFolder), golden = Path.Combine(c.Stage, "Golden");
            byte[] numbers = File.ReadAllBytes(Path.Combine(data, "vol2_numbers_v3.csv")), table = File.ReadAllBytes(Path.Combine(data, "vol2_table120_v11.csv"));
            string source = File.ReadAllText(Path.Combine(golden, "source.json")), events = File.ReadAllText(Path.Combine(golden, "events.json")),
                balance = File.ReadAllText(Path.Combine(golden, "balance.json")), frozen = File.ReadAllText(Path.Combine(golden, "import_manifest_v11.json"));
            Json(r);
            var trusted = Vol2Golden.ReadTrustedManifest(frozen);
            var good = Vol2TableV11Importer.Import(numbers, table, trusted, trusted.DtoHash);
            r.Check("import-against-frozen", good.CanCast, string.Join(" | ", good.Errors)); if (!good.CanCast) return;

            // Snapshot -> Load: the way the SO data comes back.
            var nRows = Vol2Snapshot.FromNumbers(good.Numbers); var sRows = Vol2Snapshot.FromSpells(good.Spells); var stored = Vol2Snapshot.FromManifest(good.Manifest);
            string nSha = Vol2ImportManifestDto.Sha256(numbers), tSha = Vol2ImportManifestDto.Sha256(table);
            var loaded = Vol2Snapshot.Load(nRows, nSha, sRows, tSha, stored, trusted);
            r.Check("snapshot-load", loaded.Ok && loaded.Numbers.IsValidated && loaded.Spells.Count == 120 && loaded.Numbers.Rows.Count == 414, loaded.Error);
            r.Equal("snapshot-hash", trusted.DtoHash, Vol2ImportManifestDto.HashText(Vol2ImportManifestDto.CanonicalDto(loaded.Numbers, loaded.Spells)));
            r.Check("snapshot-null-value", loaded.Numbers.Rows.Count(x => !x.Value.HasValue) == 5);
            r.Check("snapshot-plan", loaded.Spells.All(s => GlyphRulesV3.Build(s.Glyph, loaded.Numbers).Cost == s.Cost));
            // Canonical form survives a second trip through the snapshot and through shuffled row order.
            var again = Vol2Snapshot.Load(nRows.Reverse().ToArray(), nSha, sRows.Reverse().ToArray(), tSha, stored, trusted);
            r.Check("snapshot-order-independent", again.Ok, again.Error);

            // Tamper: every one of these must be refused (the SO is data, the frozen manifest is the authority).
            void Refuse(string id, NumberRowData[] n, SpellRowData[] s, ManifestData m, string ns = null, string ts = null, Vol2ImportManifestDto t = null)
            { var o = Vol2Snapshot.Load(n, ns ?? nSha, s, ts ?? tSha, m, t ?? trusted); r.Check("refuse/" + id, !o.Ok && o.Numbers == null && o.Spells == null, o.Error); }
            NumberRowData[] CopyN() => nRows.Select(x => Clone(x)).ToArray();
            SpellRowData[] CopyS() => sRows.Select(x => Clone(x)).ToArray();
            var tn = CopyN(); tn.Single(x => x.key == "action.instant").value = "0.8"; Refuse("number-value", tn, sRows, stored);
            tn = CopyN(); tn.Single(x => x.key == "combat.parry.refund").hasValue = true; tn.Single(x => x.key == "combat.parry.refund").value = "3"; Refuse("number-refund", tn, sRows, stored);
            tn = CopyN(); tn.Single(x => x.key == "ink.capacity").status = "LOCKED"; Refuse("number-status", tn, sRows, stored);
            tn = CopyN(); tn.Single(x => x.key == "ink.capacity").value = "not-a-number"; Refuse("number-unreadable", tn, sRows, stored);
            Refuse("number-row-missing", nRows.Skip(1).ToArray(), sRows, stored);
            var ts2 = CopyS(); ts2[3].cost = "99"; Refuse("spell-cost", nRows, ts2, stored);
            ts2 = CopyS(); ts2[3].fields[5] += "!"; Refuse("spell-text", nRows, ts2, stored);
            ts2 = CopyS(); ts2[3].raw += "!"; ts2[3].line += 100;   // raw line text and line number are provenance only; the ten fields carry the content and are hashed
            r.Check("provenance-not-hashed", Vol2Snapshot.Load(nRows, nSha, ts2, tSha, stored, trusted).Ok);
            ts2 = CopyS(); int peak = Array.FindIndex(ts2, x => x.fields[0] == "각"); ts2[peak].terms[1].amount = "0.8"; Refuse("spell-term", nRows, ts2, stored);
            ts2 = CopyS(); ts2[peak].terms = ts2[peak].terms.Take(1).ToArray(); Refuse("spell-term-count", nRows, ts2, stored);
            ts2 = CopyS(); ts2[0].fields[0] = "A"; Refuse("spell-illegal-letter", nRows, ts2, stored);
            ts2 = CopyS(); ts2[0].fields = ts2[0].fields.Take(9).ToArray(); Refuse("spell-columns", nRows, ts2, stored);
            Refuse("spell-row-missing", nRows, sRows.Skip(1).ToArray(), stored);
            Refuse("spell-null", nRows, null, stored);
            var tm = Clone(stored); tm.dtoHash = new string('0', 64); Refuse("manifest-dto-hash", nRows, sRows, tm);
            tm = Clone(stored); tm.schema = "old"; Refuse("manifest-schema", nRows, sRows, tm);
            tm = Clone(stored); tm.sourceHashes[1].sha256 = new string('1', 64); Refuse("manifest-source-hash", nRows, sRows, tm, null, tm.sourceHashes[1].sha256);
            Refuse("source-sha-swapped", nRows, sRows, stored, tSha, nSha);
            Refuse("source-sha-stale", nRows, sRows, stored, null, new string('2', 64));
            Refuse("manifest-null", nRows, sRows, null);
            tm = Clone(stored); tm.sourceHashes = tm.sourceHashes.Take(1).ToArray(); Refuse("manifest-missing-source", nRows, sRows, tm);
            Refuse("trusted-other-dto", nRows, sRows, stored, null, null, new Vol2ImportManifestDto(trusted.SchemaVersion, trusted.ImporterVersion, trusted.SourceHashes.ToDictionary(p => p.Key, p => p.Value), new string('3', 64)));

            // Golden replay through the loaded copies: no failure, and a count pinned so a silently shrinking fixture is noticed.
            var g = Vol2Golden.Run(loaded.Numbers, loaded.Spells, source, events, balance);
            r.Check("golden", g.Fail == 0, g.FirstFailure); r.Equal("golden-pass-count", ExpectedGoldenPass, g.Pass);
            // The golden is not a rubber stamp: each mutation below must produce a failure that names the field.
            Mut(r, "cost", Regex.Replace(source, "(\"letter\":\\s*\"가\",\\s*\"cost\":\\s*)11", "${1}12"), events, balance, loaded, "source/가/cost");
            Mut(r, "per-target", Regex.Replace(source, "(\"letter\":\\s*\"각\",\\s*\"cost\":\\s*24,\\s*\"perTarget\":\\s*)0\\.7", "${1}0.8"), events, balance, loaded, "source/각/per-target");
            Mut(r, "reaction-cell", source.Replace("비화 — 덩굴이 굵어짐", "막힘 — 덩굴이 굵어짐"), events, balance, loaded, "cell/0/0/golden");
            Mut(r, "event-expected", source, Regex.Replace(events, "(\"remaining\":\\s*)0\\.9", "${1}0.8"), balance, loaded, "/remaining");
            Mut(r, "efficiency", source, events, Regex.Replace(balance, "(\"ㅗ\":\\s*)0\\.", "${1}0.1"), loaded, "efficiency/");
            Mut(r, "ranking", Regex.Replace(source, "(\"letter\":\\s*\"먼\",\\s*\"score\":\\s*)0\\.19307692307692306", "${1}0.2"), events, balance, loaded, "rank/N1/score/먼");
            // The EO candidate profile (⑨) is a valid number set that must NOT satisfy the golden: second contact 0.25 changes the plans.
            var candidate = loaded.Numbers.CreateEoCandidateProfile(); var cg = Vol2Golden.Run(candidate, loaded.Spells, source, events, balance);
            r.Check("golden-detects-candidate-profile", cg.Fail > 0 && cg.Failures.Any(f => f.StartsWith("source/", StringComparison.Ordinal) || f.Contains("/N")), cg.FirstFailure ?? "no failure");
            var raw = Vol2Golden.Run(new NumbersV3Dto(loaded.Numbers.Rows), loaded.Spells, source, events, balance);
            r.Check("golden-refuses-unvalidated-numbers", raw.Fail == 1 && raw.Failures[0].StartsWith("numbers-validated", StringComparison.Ordinal));
            try { Vol2Golden.Run(loaded.Numbers, loaded.Spells, source.Replace("\"rows\"", "\"rows2\""), events, balance); r.Check("golden-missing-section-throws", false); }
            catch (FormatException) { r.Check("golden-missing-section-throws", true); }

            // Purity: neither assembly the Unity side compiles for the rules and the import layer references an engine assembly.
            foreach (var t in new[] { typeof(GlyphRulesV3), typeof(Vol2Snapshot) })
                r.Check("no-engine-reference/" + t.Name, !t.Assembly.GetReferencedAssemblies().Any(a => a.Name.StartsWith("Unity", StringComparison.Ordinal)));
            var rules = typeof(GlyphRulesV3).Assembly.GetTypes().Where(t => t.Namespace == "Oheangbu.Vol2.Rules" || t.Namespace == "Oheangbu.Vol2.Import").ToArray();
            var statics = rules.SelectMany(t => t.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                .Where(f => !f.IsLiteral && !f.IsInitOnly && !f.DeclaringType.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)).ToArray();
            r.Check("no-mutable-static-state", statics.Length == 0, string.Join(",", statics.Select(f => f.DeclaringType.Name + "." + f.Name)));
        }

        // Pinned after the first clean run (rows, peak terms, ranks, efficiency, events, reaction cells).
        const int ExpectedGoldenPass = 2968;

        static void Mut(Report r, string id, string source, string events, string balance, LoadOutcome loaded, string expectedInFailure)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var g = Vol2Golden.Run(loaded.Numbers, loaded.Spells, source, events, balance);
            r.Check("golden-mutation/" + id, g.Fail > 0 && g.Failures.Any(f => f.Contains(expectedInFailure)), "expected a failure containing '" + expectedInFailure + "' got " + (g.FirstFailure ?? "none"));
        }

        static void Json(Report r)
        {
            var j = JsonNode.Parse("{\"a\":[1,2.50,-3e2,true,false,null,\"x\\u0041\\n\"],\"b\":{\"c\":0.19307692307692306},\"한\":\"글\"}");
            r.Equal("json-count", 3, j.Count); r.Equal("json-int", 1, j["a"][0].Int); r.Equal("json-dec", 2.5m, j["a"][1].Dec); r.Equal("json-exp", -300m, j["a"][2].Dec);
            r.Check("json-bool-null", j["a"][3].Bool && !j["a"][4].Bool && j["a"][5].IsNull); r.Equal("json-escape", "xA\n", j["a"][6].Str);
            r.Equal("json-exact-decimal", 0.19307692307692306m, j["b"]["c"].Dec); r.Equal("json-unicode-key", "글", j["한"].Str);
            r.Check("json-has", j.Has("a") && !j.Has("z") && j.Opt("z").IsNull);
            foreach (string bad in new[] { "{", "[1,]", "{\"a\":}", "{\"a\":1,\"a\":2}", "\"unterminated", "[1 2]", "{\"a\" 1}", "tru", "[1e]", "{} extra", "\"\\q\"", "" })
            {
                bool threw = false; try { JsonNode.Parse(bad); } catch (FormatException) { threw = true; }
                r.Check("json-bad/" + bad, threw);
            }
            bool missing = false; try { var x = j["nope"]; } catch (FormatException) { missing = true; }
            r.Check("json-missing-property-throws", missing);
            bool wrongType = false; try { var x = j["a"][0].Str; } catch (FormatException) { wrongType = true; }
            r.Check("json-wrong-type-throws", wrongType);
        }

        static NumberRowData Clone(NumberRowData x) => new NumberRowData { key = x.key, hasValue = x.hasValue, value = x.value, unit = x.unit, status = x.status, source = x.source, candidate = x.candidate, candidateTrigger = x.candidateTrigger, note = x.note, line = x.line };
        static SpellRowData Clone(SpellRowData x) => new SpellRowData { line = x.line, raw = x.raw, fields = (string[])x.fields.Clone(), cost = x.cost, terms = x.terms.Select(t => new EffectTermData { kind = t.kind, amount = t.amount, count = t.count }).ToArray() };
        static ManifestData Clone(ManifestData x) => new ManifestData { schema = x.schema, importer = x.importer, dtoHash = x.dtoHash, sourceHashes = x.sourceHashes.Select(h => new SourceHashData { name = h.name, sha256 = h.sha256 }).ToArray() };
    }
}
