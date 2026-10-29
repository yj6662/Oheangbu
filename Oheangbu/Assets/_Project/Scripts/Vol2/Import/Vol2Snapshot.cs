using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oheangbu.Vol2.Rules;
namespace Oheangbu.Vol2.Import
{
    // Engine-free, Unity-serializable mirror of the import result. The ScriptableObjects (Oheangbu.Vol2.Data) hold exactly these
    // classes; decimals are invariant strings because Unity does not serialize decimal. Rebuilding the DTOs from this data and
    // calling Vol2ContractValidator.LoadDto against the frozen manifest is the only way the runtime may obtain numbers or spells.
    [Serializable] public sealed class NumberRowData
    {
        public string key; public bool hasValue; public string value;
        public string unit, status, source, candidate, candidateTrigger, note; public int line;
    }
    [Serializable] public sealed class EffectTermData { public int kind; public string amount; public int count; }
    [Serializable] public sealed class SpellRowData
    {
        public int line; public string raw; public string[] fields; public string cost; public EffectTermData[] terms;
    }
    [Serializable] public sealed class SourceHashData { public string name; public string sha256; }
    [Serializable] public sealed class ManifestData
    {
        public string schema, importer, dtoHash; public SourceHashData[] sourceHashes;
    }

    public sealed class LoadOutcome
    {
        public bool Ok { get; }
        public string Error { get; }
        public NumbersV3Dto Numbers { get; }
        public IReadOnlyList<Vol2SpellRowDto> Spells { get; }
        public LoadOutcome(NumbersV3Dto numbers, IReadOnlyList<Vol2SpellRowDto> spells, string error)
        { Numbers = numbers; Spells = spells; Error = error; Ok = error == null; }
    }

    public static class Vol2Snapshot
    {
        static string Dec(decimal v) => v.ToString("0.############################", CultureInfo.InvariantCulture);
        static decimal Parse(string s) => decimal.Parse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

        public static NumberRowData[] FromNumbers(NumbersV3Dto n) => n.Rows.Select(r => new NumberRowData
        {
            key = r.Key, hasValue = r.Value.HasValue, value = r.Value.HasValue ? Dec(r.Value.Value) : "", unit = r.Unit, status = r.Status,
            source = r.Source, candidate = r.Candidate, candidateTrigger = r.CandidateTrigger, note = r.Note, line = r.Line
        }).ToArray();

        public static SpellRowData[] FromSpells(IEnumerable<Vol2SpellRowDto> spells) => spells.Select(s => new SpellRowData
        {
            line = s.Line, raw = s.RawText, fields = s.Fields.ToArray(), cost = Dec(s.Cost),
            terms = s.EffectTerms.Select(t => new EffectTermData { kind = (int)t.Kind, amount = Dec(t.Amount), count = t.Count }).ToArray()
        }).ToArray();

        public static ManifestData FromManifest(Vol2ImportManifestDto m) => new ManifestData
        {
            schema = m.SchemaVersion, importer = m.ImporterVersion, dtoHash = m.DtoHash,
            sourceHashes = m.SourceHashes.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new SourceHashData { name = p.Key, sha256 = p.Value }).ToArray()
        };

        public static NumbersV3Dto ToNumbers(NumberRowData[] rows) => new NumbersV3Dto((rows ?? new NumberRowData[0]).Select(r =>
            new NumbersV3RowDto(r.key, r.hasValue ? Parse(r.value) : (decimal?)null, r.unit, r.status, r.source, r.candidate, r.candidateTrigger, r.note, r.line)));

        public static Vol2SpellRowDto[] ToSpells(SpellRowData[] rows) => (rows ?? new SpellRowData[0]).Select(r =>
        {
            if (r.fields == null || r.fields.Length != 10 || r.fields[0].Length != 1 || !GlyphRulesV3.TryDecode(r.fields[0][0], out var glyph))
                throw new FormatException("SpellRow line " + r.line + ": 10 fields with a legal syllable required");
            return new Vol2SpellRowDto(r.line, r.raw, r.fields, glyph, Parse(r.cost),
                (r.terms ?? new EffectTermData[0]).Select(t => new EffectTerm((EffectKind)t.kind, Parse(t.amount), t.count)));
        }).ToArray();

        public static Vol2ImportManifestDto ToManifest(ManifestData m)
        {
            if (m == null || m.sourceHashes == null) throw new FormatException("Manifest data missing");
            return new Vol2ImportManifestDto(m.schema, m.importer, m.sourceHashes.ToDictionary(h => h.name, h => h.sha256, StringComparer.Ordinal), m.dtoHash);
        }

        /// <summary>The only load path: DTOs rebuilt from stored data must hash to the trusted (frozen) manifest, and the source hash
        /// stored beside each asset must be the one its own manifest names (a swapped or edited asset is refused).</summary>
        public static LoadOutcome Load(NumberRowData[] numbers, string numbersSourceSha, SpellRowData[] spells, string tableSourceSha, ManifestData stored, Vol2ImportManifestDto trusted)
        {
            if (stored == null || stored.sourceHashes == null || trusted == null) return new LoadOutcome(null, null, "Manifest missing");
            string storedNumbers = stored.sourceHashes.FirstOrDefault(h => h.name == "numbers")?.sha256, storedTable = stored.sourceHashes.FirstOrDefault(h => h.name == "table")?.sha256;
            if (storedNumbers == null || storedTable == null || storedNumbers != numbersSourceSha || storedTable != tableSourceSha) return new LoadOutcome(null, null, "Asset source hash differs from its manifest (swapped or edited asset)");
            try
            {
                var n = ToNumbers(numbers); var s = ToSpells(spells); var m = ToManifest(stored);
                var r = new Vol2ContractValidator().LoadDto(n, s, m, trusted);
                return r.CanCast ? new LoadOutcome(r.Numbers, r.Spells, null) : new LoadOutcome(null, null, string.Join(" | ", r.Errors.Select(e => e.ToString())));
            }
            catch (Exception e) when (e is FormatException || e is OverflowException || e is ArgumentException || e is NullReferenceException)
            { return new LoadOutcome(null, null, "Snapshot unreadable: " + e.Message); }
        }
    }
}
