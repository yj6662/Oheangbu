using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellFx;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    // 2026-10-30 (lane E, PLAN-308-MEMBRANE-SPELLS 2.0 / 3.3 / 6.1 3): which of the 115 membrane glyphs is which - frame (틀) x verb (동사) x body (몸) x peak (극치) - the one table the setup commands read
    // (SpellFxSetupM308 and its Fan / Ring / Attach / Path parts) instead of the key-by-ㄱ `Bites` array of SpellFxSetupG308.
    //
    // WHY: SpellFxSetupG308.Bites is indexed by the vine glyphs of ㄱ, so the 4 wood PEAK 남긴다 glyphs (각 걱 곡 국: cage, self-heal, thicket, ring pillar) were dressed on the 16 ordinary 남긴다
    // glyphs of the other four bodies (낙 막 삭 악 · 넉 먹 석 억 · 녹 목 속 옥 · 눅 묵 숙 욱), whose CSV rows are all "잔존물 한 덩이를 남긴다" / "내 몸에 잔존물로 붙는다" - 넉 먹 석 억 played 걱's self-heal.
    // Here the VERB is the template (6 general per frame, 5 for ㅜ which has no 한 번 = the ward) and ONLY the four wood peak 남긴다 glyphs get a dedicated template.
    //
    // The structure is arithmetic on the Hangul syllable (body = the initial consonant, frame = the vowel, verb = the final consonant) and the peak flag is "the initial consonant IS the final consonant"
    // (ㄱ/ㄱ ㄴ/ㄴ ㅁ/ㅁ ㅅ/ㅅ ㅇ/ㅇ -> 각걱곡국 난넌논눈 맘멈몸뭄 삿섯솟숫 앙엉옹웅). The CSV (Docs/Research/vol2/vol2_table120_v11.csv, the only canon of a glyph's effect) is NOT copied here:
    // <see cref="Validate"/> reads its 초성 / 중성 / 종성 / 구분 columns and reports every row where this table disagrees, so a changed CSV shows up as a failed check, not as a silent drift.

    /// <summary>ㅏ 경로 · ㅓ 부착 · ㅗ 면 · ㅜ 둘레.</summary>
    public enum HexGlyphFrame { Path, Attach, Fan, Ring }

    /// <summary>한 번 · 남긴다 · 번진다 · 심는다 · 가른다 · 돌아온다 (the final consonant: none, ㄱ, ㄴ, ㅁ, ㅅ, ㅇ).</summary>
    public enum HexGlyphVerb { Once, Leave, Spread, Seed, Cut, Return }

    /// <summary>The templates that exist only for the four wood peak 남긴다 glyphs (각 = ㅏ cage, 걱 = ㅓ self-heal, 곡 = ㅗ thicket, 국 = ㅜ ring pillar).</summary>
    public enum HexGlyphDedicated { None, Cage, SelfHeal, Thicket, RingPillar }

    /// <summary>One glyph's row. Immutable.</summary>
    public sealed class HexGlyph308
    {
        public readonly char Letter;
        public readonly HexGlyphFrame Frame;
        public readonly HexGlyphVerb Verb;
        /// <summary>Wood, Fire, Earth, Metal or Water (<see cref="SpellFxBurstBody.Armour"/> is never a glyph's body).</summary>
        public readonly SpellFxBurstBody Body;
        /// <summary>극치: one of the 20 glyphs whose initial consonant is also its final consonant.</summary>
        public readonly bool Peak;
        public readonly HexGlyphDedicated Dedicated;

        public HexGlyph308(char letter, HexGlyphFrame frame, HexGlyphVerb verb, SpellFxBurstBody body, bool peak, HexGlyphDedicated dedicated)
        { Letter = letter; Frame = frame; Verb = verb; Body = body; Peak = peak; Dedicated = dedicated; }

        /// <summary>The template this glyph is drawn with: "&lt;frame&gt;.&lt;verb&gt;" (a general template: path.once ... ring.return) or, for the four wood peak 남긴다 glyphs, "path.cage" / "attach.selfheal" /
        /// "fan.thicket" / "ring.pillar". The other 16 peak glyphs use their frame's general template of THEIR verb (난 = path.spread ...) plus the peak mark.</summary>
        public string TemplateId => Dedicated == HexGlyphDedicated.None
            ? FrameKey(Frame) + "." + VerbKey(Verb)
            : FrameKey(Frame) + "." + (Dedicated == HexGlyphDedicated.Cage ? "cage" : Dedicated == HexGlyphDedicated.SelfHeal ? "selfheal" : Dedicated == HexGlyphDedicated.Thicket ? "thicket" : "pillar");

        /// <summary>The peak mark: cell size, the main pattern in every cell, an always-faint accent seam. A non-peak glyph gets 1 / false / 0.</summary>
        public float CellScale => Peak ? SpellFxHexGlyphs308.PeakCellScale : 1f;
        public bool MainPatternEverywhere => Peak;
        public float SeamAccentIdle => Peak ? SpellFxHexGlyphs308.PeakSeamAccentIdle : 0f;

        public override string ToString() => Letter + " " + TemplateId + " " + Body + (Peak ? " peak" : "");

        public static string FrameKey(HexGlyphFrame f) => f == HexGlyphFrame.Path ? "path" : f == HexGlyphFrame.Attach ? "attach" : f == HexGlyphFrame.Fan ? "fan" : "ring";
        public static string VerbKey(HexGlyphVerb v)
        {
            switch (v)
            {
                case HexGlyphVerb.Once: return "once";
                case HexGlyphVerb.Leave: return "leave";
                case HexGlyphVerb.Spread: return "spread";
                case HexGlyphVerb.Seed: return "seed";
                case HexGlyphVerb.Cut: return "cut";
                default: return "return";
            }
        }
    }

    /// <summary>What <see cref="SpellFxHexGlyphs308.Validate"/> found.</summary>
    public sealed class HexGlyphCheck
    {
        public string CsvPath = "";
        public int CsvRows, TableGlyphs, Peaks, GeneralLeave, WardsSkipped;
        public readonly List<string> Problems = new List<string>();
        public bool Ok => Problems.Count == 0;
        public string Summary => (Ok ? "OK" : "MISMATCH " + Problems.Count) + ": " + CsvRows + " csv rows, " + TableGlyphs + " table glyphs, " + Peaks + " peaks, " + GeneralLeave + " ordinary 남긴다, " + WardsSkipped + " wards (ㅜ 무받침, not in the table)"
            + (Ok ? "" : " | " + string.Join(" | ", Problems.Take(12)));
    }

    /// <summary>The 115 membrane glyphs (the 120 minus the five ㅜ bare wards, which are the dome). Static read-only table; nothing here changes after the type is loaded.</summary>
    public static class SpellFxHexGlyphs308
    {
        // ---- the peak mark (PLAN 2.0 / 6.1 3 (a)): first pass = the body's ordinary template of the glyph's verb + this mark; the dedicated motions of the other 16 come after the 92-glyph re-judging ----
        /// <summary>A peak glyph's cells are this much bigger (x1.25).</summary>
        public const float PeakCellScale = 1.25f;
        /// <summary>Every cell of a peak glyph carries the body's main pattern: slot 1 of its atlas (the other glyphs draw slots 1 2 3 4 5 7 at random).</summary>
        public const int PeakPatternSlot = 1;
        /// <summary>A peak glyph's seams carry the body's one accent colour all the time, faintly (0..1 of the accent seam; TEST - no measured look yet).</summary>
        public const float PeakSeamAccentIdle = 0.15f;

        public const string CsvRelativePath = "Docs/Research/vol2/vol2_table120_v11.csv";

        // Hangul indices: 초성 ㄱ0 ㄴ2 ㅁ6 ㅅ9 ㅇ11; 중성 ㅏ0 ㅓ4 ㅗ8 ㅜ13; 종성 없음0 ㄱ1 ㄴ4 ㅁ16 ㅅ19 ㅇ21 (the same as SpellFxSetupG308)
        static readonly int[] Cho = { 0, 2, 6, 9, 11 };
        static readonly int[] Jung = { 0, 4, 8, 13 };
        static readonly int[] Jong = { 0, 1, 4, 16, 19, 21 };
        static readonly SpellFxBurstBody[] Bodies = { SpellFxBurstBody.Wood, SpellFxBurstBody.Fire, SpellFxBurstBody.Earth, SpellFxBurstBody.Metal, SpellFxBurstBody.Water };

        /// <summary>All 115, in Hangul order (가 각 간 감 갓 강 거 걱 ... 웅).</summary>
        public static readonly IReadOnlyList<HexGlyph308> All = Build();

        static IReadOnlyList<HexGlyph308> Build()
        {
            var list = new List<HexGlyph308>(115);
            for (int b = 0; b < 5; b++)
                for (int f = 0; f < 4; f++)
                    for (int v = 0; v < 6; v++)
                    {
                        var frame = (HexGlyphFrame)f; var verb = (HexGlyphVerb)v;
                        if (frame == HexGlyphFrame.Ring && verb == HexGlyphVerb.Once) continue;           // the bare ㅜ is the ward (the dome), not a membrane glyph
                        char letter = (char)(0xAC00 + (Cho[b] * 21 + Jung[f]) * 28 + Jong[v]);
                        bool peak = v > 0 && Jong[v] == FinalOfInitial(Cho[b]);
                        var ded = HexGlyphDedicated.None;
                        if (peak && b == 0) ded = frame == HexGlyphFrame.Path ? HexGlyphDedicated.Cage : frame == HexGlyphFrame.Attach ? HexGlyphDedicated.SelfHeal : frame == HexGlyphFrame.Fan ? HexGlyphDedicated.Thicket : HexGlyphDedicated.RingPillar;
                        list.Add(new HexGlyph308(letter, frame, verb, Bodies[b], peak, ded));
                    }
            return list.AsReadOnly();
        }

        /// <summary>The final-consonant index that is the same consonant as an initial-consonant index (ㄱ 0 -> 1, ㄴ 2 -> 4, ㅁ 6 -> 16, ㅅ 9 -> 19, ㅇ 11 -> 21).</summary>
        static int FinalOfInitial(int cho)
        {
            switch (cho) { case 0: return 1; case 2: return 4; case 6: return 16; case 9: return 19; default: return 21; }
        }

        /// <summary>The row of one letter, or null (not a membrane glyph: a ㅜ bare ward, or not one of the 120).</summary>
        public static HexGlyph308 Of(char letter)
        {
            foreach (var g in All) if (g.Letter == letter) return g;
            return null;
        }

        public static IEnumerable<HexGlyph308> Where(HexGlyphFrame frame) => All.Where(g => g.Frame == frame);
        public static IEnumerable<HexGlyph308> Where(HexGlyphFrame frame, SpellFxBurstBody body) => All.Where(g => g.Frame == frame && g.Body == body);
        public static IEnumerable<HexGlyph308> Peaks => All.Where(g => g.Peak);

        /// <summary>The 16 ordinary 남긴다 glyphs (the four non-wood bodies x four frames; the wood ones are all peaks): 낙 넉 녹 눅 · 막 먹 목 묵 · 삭 석 속 숙 · 악 억 옥 욱.</summary>
        public static IEnumerable<HexGlyph308> GeneralLeave => All.Where(g => g.Verb == HexGlyphVerb.Leave && !g.Peak);

        /// <summary>A frame's trial set (PLAN 5.2): the wood glyphs of the frame (6, or 5 for ㅜ) + its ordinary 남긴다 glyph of fire (낙 · 넉 · 녹 · 눅) as the 7th, because the wood 남긴다 is a peak and the
        /// ordinary 남긴다 template would not be seen otherwise.</summary>
        public static IEnumerable<HexGlyph308> TrialSet(HexGlyphFrame frame) =>
            Where(frame, SpellFxBurstBody.Wood).Concat(All.Where(g => g.Frame == frame && g.Body == SpellFxBurstBody.Fire && g.Verb == HexGlyphVerb.Leave));

        /// <summary>The template ids a setup command has to build, with the letters each one dresses: 23 general + the 4 dedicated, as "id -> letters".</summary>
        public static SortedDictionary<string, List<char>> ByTemplate()
        {
            var d = new SortedDictionary<string, List<char>>(StringComparer.Ordinal);
            foreach (var g in All)
            {
                if (!d.TryGetValue(g.TemplateId, out var l)) { l = new List<char>(); d[g.TemplateId] = l; }
                l.Add(g.Letter);
            }
            return d;
        }

        // ------------------------------------------------------------ the CSV check

        /// <summary>The CSV of the 120 glyphs next to this project (Docs/Research/vol2/...; Application.dataPath is Oheangbu/Assets).</summary>
        public static string DefaultCsvPath() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", CsvRelativePath));

        /// <summary>Reads the CSV (UTF-8, BOM or not) and compares its 글자 / 초성 / 중성 / 종성 / 구분 columns with the table: for every one of its 120 rows the body, frame and verb must be those the Hangul
        /// syllable decomposes into and the 구분 must be 무받침 (a verb 한 번), 극치 (a peak) or 일반 (the rest); the 115 table glyphs must be exactly the CSV's rows without the five ㅜ bare wards. Pure file work:
        /// no Unity object is touched.</summary>
        public static HexGlyphCheck Validate(string csvPath = null)
        {
            var r = new HexGlyphCheck { TableGlyphs = All.Count, Peaks = All.Count(g => g.Peak), GeneralLeave = GeneralLeave.Count() };
            try
            {
                csvPath = string.IsNullOrEmpty(csvPath) ? DefaultCsvPath() : csvPath;
                r.CsvPath = csvPath;
                if (!File.Exists(csvPath)) { r.Problems.Add("csv not found: " + csvPath); return r; }
                var rows = ParseCsv(File.ReadAllText(csvPath, new UTF8Encoding(false)));
                if (rows.Count == 0) { r.Problems.Add("csv is empty"); return r; }
                int col(string name) => rows[0].IndexOf(name);
                int cLetter = col("글자"), cCho = col("초성"), cJung = col("중성"), cJong = col("종성"), cKind = col("구분");
                if (cLetter < 0 || cCho < 0 || cJung < 0 || cJong < 0 || cKind < 0) { r.Problems.Add("csv header lacks 글자 / 초성 / 중성 / 종성 / 구분: " + string.Join(",", rows[0].Take(6))); return r; }
                var seen = new HashSet<char>();
                for (int i = 1; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row.Count <= cKind || string.IsNullOrWhiteSpace(row[cLetter])) continue;
                    r.CsvRows++;
                    char ch = row[cLetter].Trim()[0];
                    Decompose(ch, out int cho, out int jung, out int jong);
                    string what = "row " + (i + 1) + " " + ch;
                    var body = BodyOfCsv(row[cCho]); var frame = FrameOfCsv(row[cJung]); var verb = VerbOfCsv(row[cJong]);
                    if (!body.HasValue || !frame.HasValue || !verb.HasValue) { r.Problems.Add(what + ": cannot read 초성/중성/종성 \"" + row[cCho] + "\" \"" + row[cJung] + "\" \"" + row[cJong] + "\""); continue; }
                    int bi = Array.IndexOf(Cho, cho), fi = Array.IndexOf(Jung, jung), vi = Array.IndexOf(Jong, jong);
                    if (bi < 0 || fi < 0 || vi < 0) { r.Problems.Add(what + ": not a letter of the 5 x 4 x 6 grid"); continue; }
                    if (Bodies[bi] != body.Value || (HexGlyphFrame)fi != frame.Value || (HexGlyphVerb)vi != verb.Value)
                        r.Problems.Add(what + ": the letter reads as " + Bodies[bi] + "/" + (HexGlyphFrame)fi + "/" + (HexGlyphVerb)vi + " but the csv says " + body.Value + "/" + frame.Value + "/" + verb.Value);
                    string kind = row[cKind].Trim();
                    var g = Of(ch);
                    if (frame.Value == HexGlyphFrame.Ring && verb.Value == HexGlyphVerb.Once)
                    {
                        r.WardsSkipped++;
                        if (g != null) r.Problems.Add(what + ": a ㅜ bare ward must not be a table glyph");
                        if (kind != "무받침") r.Problems.Add(what + ": a ward should be 무받침, csv says " + kind);
                        continue;
                    }
                    if (g == null) { r.Problems.Add(what + ": missing from the table"); continue; }
                    seen.Add(ch);
                    string expect = g.Verb == HexGlyphVerb.Once ? "무받침" : g.Peak ? "극치" : "일반";
                    if (kind != expect) r.Problems.Add(what + ": 구분 is " + kind + " in the csv, " + expect + " in the table");
                }
                foreach (var g in All) if (!seen.Contains(g.Letter)) r.Problems.Add("table glyph " + g.Letter + " is not in the csv");
                if (r.CsvRows != 120) r.Problems.Add("the csv has " + r.CsvRows + " glyph rows, not 120");
                if (r.TableGlyphs != 115) r.Problems.Add("the table has " + r.TableGlyphs + " glyphs, not 115");
                if (r.Peaks != 20) r.Problems.Add("the table has " + r.Peaks + " peaks, not 20");
                if (r.GeneralLeave != 16) r.Problems.Add("the table has " + r.GeneralLeave + " ordinary 남긴다, not 16");
            }
            catch (Exception e) { r.Problems.Add("EXCEPTION " + e.GetType().Name + " " + e.Message); }
            return r;
        }

        static void Decompose(char ch, out int cho, out int jung, out int jong)
        {
            int s = ch - 0xAC00;
            if (s < 0 || s > 11171) { cho = jung = jong = -1; return; }
            cho = s / 588; jung = s % 588 / 28; jong = s % 28;
        }

        // "ㄱ=목" -> wood ...
        static SpellFxBurstBody? BodyOfCsv(string cell)
        {
            switch (After(cell)) { case "목": return SpellFxBurstBody.Wood; case "화": return SpellFxBurstBody.Fire; case "토": return SpellFxBurstBody.Earth; case "금": return SpellFxBurstBody.Metal; case "수": return SpellFxBurstBody.Water; default: return null; }
        }
        static HexGlyphFrame? FrameOfCsv(string cell)
        {
            switch (After(cell)) { case "경로": return HexGlyphFrame.Path; case "부착": return HexGlyphFrame.Attach; case "면": return HexGlyphFrame.Fan; case "둘레": return HexGlyphFrame.Ring; default: return null; }
        }
        static HexGlyphVerb? VerbOfCsv(string cell)
        {
            switch (After(cell))
            {
                case "한 번": return HexGlyphVerb.Once; case "남긴다": return HexGlyphVerb.Leave; case "번진다": return HexGlyphVerb.Spread;
                case "심는다": return HexGlyphVerb.Seed; case "가른다": return HexGlyphVerb.Cut; case "돌아온다": return HexGlyphVerb.Return; default: return null;
            }
        }
        static string After(string cell) { if (cell == null) return ""; int k = cell.IndexOf('='); return (k >= 0 ? cell.Substring(k + 1) : cell).Trim(); }

        /// <summary>RFC 4180 reader: commas, quoted fields (with "" and line breaks inside), CRLF or LF, a leading BOM dropped.</summary>
        static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>(); var sb = new StringBuilder();
            bool quoted = false;
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; } else quoted = false; }
                    else sb.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { row.Add(sb.ToString()); sb.Length = 0; }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(sb.ToString()); sb.Length = 0;
                    if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
                    row = new List<string>();
                }
                else sb.Append(c);
            }
            if (sb.Length > 0 || row.Count > 0) { row.Add(sb.ToString()); rows.Add(row); }
            return rows;
        }
    }
}
