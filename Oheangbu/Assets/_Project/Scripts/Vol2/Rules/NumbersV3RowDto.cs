using System;
namespace Oheangbu.Vol2.Rules
{
    public sealed class NumbersV3RowDto
    {
        public string Key { get; }
        public decimal? Value { get; }
        public string Unit { get; }
        public string Status { get; }
        public string Source { get; }
        public string Candidate { get; }
        public string CandidateTrigger { get; }
        public string Note { get; }
        public int Line { get; }
        public NumbersV3RowDto(string key, decimal? value, string unit, string status, string source,
            string candidate, string candidateTrigger, string note, int line)
        { Key = key; Value = value; Unit = unit; Status = status; Source = source; Candidate = candidate;
          CandidateTrigger = candidateTrigger; Note = note; Line = line; }
        internal NumbersV3RowDto WithValue(decimal? value) => new NumbersV3RowDto(Key, value, Unit, Status, Source, Candidate, CandidateTrigger, Note, Line);
    }
}
