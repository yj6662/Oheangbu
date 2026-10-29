using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
namespace Oheangbu.Vol2.Rules
{
    public sealed class Vol2ImportManifestDto
    {
        public const string Schema = "vol2-v3/1";
        public const string Importer = "pure-v3/1";
        public string SchemaVersion { get; }
        public string ImporterVersion { get; }
        public ReadOnlyDictionary<string, string> SourceHashes { get; }
        public string DtoHash { get; }
        public Vol2ImportManifestDto(string schemaVersion, string importerVersion, IDictionary<string, string> hashes, string dtoHash)
        { SchemaVersion = schemaVersion; ImporterVersion = importerVersion; SourceHashes = new ReadOnlyDictionary<string,string>(new Dictionary<string,string>(hashes, StringComparer.Ordinal)); DtoHash = dtoHash; }
        public static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        public static string HashText(string text) => Sha256(new UTF8Encoding(false).GetBytes(text));
        static string Escape(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n");
        internal static string Number(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
        public static string CanonicalDto(NumbersV3Dto numbers, IEnumerable<Vol2SpellRowDto> spells)
        {
            var b = new StringBuilder(Schema + "\n");
            foreach (var r in numbers.Rows.OrderBy(r => r.Key, StringComparer.Ordinal))
            {
                var f = new[] { r.Key, r.Value.HasValue ? Number(r.Value.Value) : "null", r.Unit, r.Status, r.Source, r.Candidate, r.CandidateTrigger, r.Note };
                b.Append("N\t").Append(string.Join("\t", f.Select(Escape))).Append('\n');
            }
            foreach (var r in spells.OrderBy(r => r.Letter))
            {
                b.Append("S\t").Append(string.Join("\t", r.Fields.Select(Escape))).Append('\t').Append((int)r.Body).Append('\t').Append((int)r.Frame).Append('\t').Append((int)r.Verb).Append('\t').Append((int)r.Kind).Append('\t').Append(Number(r.Cost));
                foreach (var t in r.EffectTerms) b.Append('\t').Append((int)t.Kind).Append(':').Append(Number(t.Amount)).Append(':').Append(t.Count);
                b.Append('\n');
            }
            return b.ToString();
        }
        public static Vol2ImportManifestDto Create(IDictionary<string, byte[]> sources, NumbersV3Dto numbers, IEnumerable<Vol2SpellRowDto> spells)
        {
            var hashes = new Dictionary<string,string>(StringComparer.Ordinal);
            foreach (var p in sources) hashes.Add(p.Key, Sha256(p.Value));
            return new Vol2ImportManifestDto(Schema, Importer, hashes, HashText(CanonicalDto(numbers, spells)));
        }
        public bool Matches(Vol2ImportManifestDto other, string roundTripDtoHash) =>
            other != null && SchemaVersion == Schema && other.SchemaVersion == SchemaVersion && ImporterVersion == Importer && other.ImporterVersion == ImporterVersion &&
            DtoHash == other.DtoHash && DtoHash == roundTripDtoHash && SourceHashes.Count == other.SourceHashes.Count &&
            SourceHashes.All(p => other.SourceHashes.TryGetValue(p.Key, out string value) && value == p.Value);
    }
}
