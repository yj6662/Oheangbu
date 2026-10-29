using System;
using System.Collections.Generic;
using System.Text;
namespace Oheangbu.Vol2.Rules
{
    internal static class CsvV3
    {
        internal sealed class Record
        {
            internal string[] Fields;
            internal int Line;
            internal string Raw;
        }
        internal static List<Record> Parse(byte[] bytes)
        {
            string text = new UTF8Encoding(false, true).GetString(bytes);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            var rows = new List<Record>(); var fields = new List<string>(); var cell = new StringBuilder();
            bool quoted = false, closed = false; int line = 1, startLine = 1, start = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                bool eof = i == text.Length; char c = eof ? '\0' : text[i];
                if (quoted)
                {
                    if (eof) throw new FormatException("CSV line " + startLine + ": unclosed quote");
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else { quoted = false; closed = true; } }
                    else { cell.Append(c); if (c == '\n') line++; }
                    continue;
                }
                if (closed && !eof && c != ',' && c != '\r' && c != '\n') throw new FormatException("CSV line " + line + ": trailing quoted text");
                if (c == '"')
                {
                    if (cell.Length != 0 || closed) throw new FormatException("CSV line " + line + ": unexpected quote");
                    quoted = true; continue;
                }
                if (eof || c == ',' || c == '\r' || c == '\n')
                {
                    if (eof && start == text.Length && fields.Count == 0 && cell.Length == 0) break;
                    fields.Add(cell.ToString()); cell.Clear(); closed = false;
                    if (c == ',') continue;
                    int end = i;
                    rows.Add(new Record { Fields = fields.ToArray(), Line = startLine, Raw = text.Substring(start, end - start) }); fields.Clear();
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    if (!eof) line++;
                    start = i + 1; startLine = line;
                }
                else cell.Append(c);
            }
            return rows;
        }
    }
}
