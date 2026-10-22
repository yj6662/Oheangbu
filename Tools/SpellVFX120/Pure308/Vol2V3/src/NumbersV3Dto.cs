using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
namespace Oheangbu.Vol2.Rules
{
    public sealed class NumbersV3Dto
    {
        readonly Dictionary<string, NumbersV3RowDto> byKey;
        public ReadOnlyCollection<NumbersV3RowDto> Rows { get; }
        public bool IsValidated { get; }
        public string Profile { get; }
        public NumbersV3Dto(IEnumerable<NumbersV3RowDto> rows) : this(rows, false, "current") { }
        internal NumbersV3Dto(IEnumerable<NumbersV3RowDto> rows, bool validated, string profile = "current")
        {
            Rows = RuleTypesV3.Freeze(rows); IsValidated = validated; Profile = profile;
            byKey = new Dictionary<string, NumbersV3RowDto>(StringComparer.Ordinal);
            foreach (var row in Rows) if (!byKey.ContainsKey(row.Key)) byKey.Add(row.Key, row);
        }
        public decimal Get(string key) => byKey.TryGetValue(key, out var r) && r.Value.HasValue ? r.Value.Value : throw new InvalidOperationException("Missing value: " + key);
        public NumbersV3RowDto Row(string key) => byKey[key];
        internal bool Has(string key) => byKey.ContainsKey(key);
        internal NumbersV3Dto Validated() => new NumbersV3Dto(Rows, true, Profile);
        public NumbersV3Dto CreateEoCandidateProfile()
        {
            if (!IsValidated) throw new InvalidOperationException("Unvalidated numbers");
            var rows = new List<NumbersV3RowDto>();
            foreach (var r in Rows)
                rows.Add(r.CandidateTrigger.StartsWith("E7 ⑨:", StringComparison.Ordinal)
                    ? r.WithValue(decimal.Parse(r.Candidate, CultureInfo.InvariantCulture)) : r);
            return new NumbersV3Dto(rows, true, "eo-candidate-09");
        }
    }
}
