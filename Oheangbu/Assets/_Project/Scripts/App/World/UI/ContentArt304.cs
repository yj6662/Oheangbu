using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 content screens (소지품 · 술식 도감 · 차패 · 상점/강화 · 정비 · 여정의 끝): the area's own sprites and the
    /// 작도어휘 rows imported from Docs/오행부_작도어휘_v0_1.csv (the CSV stays the only source; this is its DTO import).
    /// Asset: Assets/_Project/Resources/UI304/content/ContentArt304.asset, created / refreshed by the editor command
    /// Oheangbu.EditorTools.WorldMacro.ContentSetup304 Execute("content304-setup"). Every screen still works without it:
    /// missing sprites fall back to foundation sprites or plain frames, missing rows fall back to WorldMacroCollectionCatalog.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/UI/UI304 Content Art")]
    public sealed class ContentArt304 : ScriptableObject
    {
        public const string ResourcePath = "UI304/content/ContentArt304";

        [Header("sprites (Textures/content, Art/UI304/gen/content_assets.py)")]
        [Tooltip("D04 조선통보 symbol: circle, rim 6.5 %, square hole 24 %, thin hole rim; ink monochrome mask")] public Sprite Coin;
        [Tooltip("D16 묵등: brush-edged ink block of a glyph not found yet")] public Sprite MukDeung;
        [Tooltip("D17 광곽 사주쌍변 (Sliced 24): outer 5 px + gap 2 px + inner 1.5 px")] public Sprite GwangGwak;
        [Tooltip("D21 새 내용 삐침 (short ink flick)")] public Sprite Flick;
        [Tooltip("D18 관변 seal border (rough square, ~4.7 % of the side)")] public Sprite SealFrame;

        [Header("작도어휘 import (read only at runtime)")]
        public List<ContentVocabRow304> Vocab = new List<ContentVocabRow304>();
        [Tooltip("source path relative to the repo root")] public string VocabSource;
        [Tooltip("SHA-1 of the source bytes at import time")] public string VocabHash;

        static ContentArt304 cached;
        static bool loaded;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cached = null; loaded = false; }

        /// <summary>The Resources asset (null when content304-setup has not run). Loaded once per Play.</summary>
        public static ContentArt304 Load()
        {
            if (!loaded) { loaded = true; cached = Resources.Load<ContentArt304>(ResourcePath); }
            return cached;
        }

        /// <summary>The CSV row of a complete syllable (글자), or null.</summary>
        public ContentVocabRow304 Row(string letter)
        {
            if (string.IsNullOrEmpty(letter) || Vocab == null) return null;
            foreach (var r in Vocab) if (r != null && r.Letter == letter) return r;
            return null;
        }

        /// <summary>CSV rows sharing 초성 + 중성 that carry a 종성 (받침 table), in CSV order.</summary>
        public List<ContentVocabRow304> FinalsOf(string initial, string medial)
        {
            var list = new List<ContentVocabRow304>();
            if (Vocab == null) return list;
            foreach (var r in Vocab)
                if (r != null && r.Initial == initial && r.Medial == medial && r.HasFinal) list.Add(r);
            return list;
        }
    }

    /// <summary>One 작도어휘 CSV row (columns 글자,초성,속성,중성,프레임,종성,분류,효과,상태,비고).</summary>
    [Serializable]
    public sealed class ContentVocabRow304
    {
        public string Letter, Initial, ElementName, Medial, Frame, Final, Category, Effect, State, Note;

        public bool HasFinal => !string.IsNullOrEmpty(Final) && Final != "무";
        /// <summary>분류 공백 = 의도적 공백 (미배정): shown in the sealed state (D22).</summary>
        public bool Blank => Category == "공백";

        /// <summary>The player-facing head of 효과: text before the first " — " / sentence end, split into the effect and its
        /// trailing parenthesis (사거리 etc.). Parentheses that are design notes (=, ASCII letters or digits: "재질=초성",
        /// "C4식", "임시처리 TEST") are dropped.</summary>
        public void SplitEffect(out string effect, out string range)
        {
            effect = ContentVocab304.Head(Effect);
            range = "";
            int open = effect.IndexOf('(');
            if (open >= 0)
            {
                int close = effect.IndexOf(')', open + 1);
                string inner = close > open ? effect.Substring(open + 1, close - open - 1).Trim() : "";
                effect = effect.Substring(0, open).Trim();
                if (ContentVocab304.PlayerFacing(inner)) range = inner;
            }
        }

        /// <summary>A short form for the 받침 table: the effect head without any parenthesis.</summary>
        public string ShortEffect()
        {
            SplitEffect(out string e, out _);
            return e;
        }
    }

    /// <summary>CSV → rows (RFC 4180 quotes, UTF-8 BOM tolerant) and the small text rules the content screens share.</summary>
    public static class ContentVocab304
    {
        public static List<ContentVocabRow304> Parse(string csv)
        {
            var rows = new List<ContentVocabRow304>();
            if (string.IsNullOrEmpty(csv)) return rows;
            var records = Records(csv.TrimStart('﻿'));
            if (records.Count == 0) return rows;
            var head = records[0];
            int Col(string name) { for (int i = 0; i < head.Count; i++) if (head[i].Trim() == name) return i; return -1; }
            int cL = Col("글자"), cI = Col("초성"), cE = Col("속성"), cM = Col("중성"), cF = Col("프레임"), cJ = Col("종성"),
                cC = Col("분류"), cX = Col("효과"), cS = Col("상태"), cN = Col("비고");
            string Get(List<string> r, int c) => c >= 0 && c < r.Count ? r[c].Trim() : "";
            for (int i = 1; i < records.Count; i++)
            {
                var r = records[i];
                string letter = Get(r, cL);
                if (letter.Length == 0) continue;
                rows.Add(new ContentVocabRow304
                {
                    Letter = letter, Initial = Get(r, cI), ElementName = Get(r, cE), Medial = Get(r, cM), Frame = Get(r, cF), Final = Get(r, cJ),
                    Category = Get(r, cC), Effect = Get(r, cX), State = Get(r, cS), Note = Get(r, cN),
                });
            }
            return rows;
        }

        static List<List<string>> Records(string text)
        {
            var records = new List<List<string>>();
            var record = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else quoted = false; }
                    else field.Append(c);
                    continue;
                }
                if (c == '"') quoted = true;
                else if (c == ',') { record.Add(field.ToString()); field.Clear(); }
                else if (c == '\r') { }
                else if (c == '\n') { record.Add(field.ToString()); field.Clear(); records.Add(record); record = new List<string>(); }
                else field.Append(c);
            }
            if (field.Length > 0 || record.Count > 0) { record.Add(field.ToString()); records.Add(record); }
            return records;
        }

        /// <summary>Text before the first " — " (or "—") and before the first sentence end.</summary>
        public static string Head(string effect)
        {
            if (string.IsNullOrEmpty(effect)) return "";
            string e = effect;
            int dash = e.IndexOf('—'); if (dash >= 0) e = e.Substring(0, dash);
            int stop = e.IndexOf(". ", StringComparison.Ordinal); if (stop >= 0) e = e.Substring(0, stop);
            return e.Trim().TrimEnd('.').Trim();
        }

        /// <summary>False for parentheses that are design notes (contain '=', ASCII letters or digits).</summary>
        public static bool PlayerFacing(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text) if (c == '=' || (c < 128 && char.IsLetterOrDigit(c))) return false;
            return true;
        }

        /// <summary>Codex cell name from the catalog's dry noun phrase ("곧게 뻗는 생목 가시." → "생목 가시"): the words after the
        /// last adnominal word (last syllable with the ㄴ final: 뻗는, 느린, 보내는 ...). No new names are written here.</summary>
        public static string CellName(string description) => CellName(description, 0);

        /// <summary>CellName keeping `extraWords` more of the catalog's words before the cut ("빠른 금속 송곳" next to 소's
        /// "금속 송곳"): used only to tell two cells apart that would otherwise read the same.</summary>
        public static string CellName(string description, int extraWords)
        {
            if (string.IsNullOrEmpty(description)) return "";
            string d = description.Trim().TrimEnd('.').Trim();
            var words = d.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 1) return d;
            int cut = -1;
            for (int i = 0; i < words.Length - 1; i++) if (EndsWithNieun(words[i])) cut = i;
            int start = cut < 0 ? words.Length - 2 : cut + 1;
            start = Math.Max(0, start - Math.Max(0, extraWords));
            return string.Join(" ", words, start, words.Length - start);
        }

        static bool EndsWithNieun(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            int code = word[word.Length - 1] - 0xAC00;
            return code >= 0 && code < 11172 && code % 28 == 4;   // jongseong index 4 = ㄴ
        }

        static readonly string[] Initials = { "ㄱ", "ㄲ", "ㄴ", "ㄷ", "ㄸ", "ㄹ", "ㅁ", "ㅂ", "ㅃ", "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅉ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ" };
        static readonly string[] Medials = { "ㅏ", "ㅐ", "ㅑ", "ㅒ", "ㅓ", "ㅔ", "ㅕ", "ㅖ", "ㅗ", "ㅘ", "ㅙ", "ㅚ", "ㅛ", "ㅜ", "ㅝ", "ㅞ", "ㅟ", "ㅠ", "ㅡ", "ㅢ", "ㅣ" };
        static readonly string[] Finals = { "", "ㄱ", "ㄲ", "ㄳ", "ㄴ", "ㄵ", "ㄶ", "ㄷ", "ㄹ", "ㄺ", "ㄻ", "ㄼ", "ㄽ", "ㄾ", "ㄿ", "ㅀ", "ㅁ", "ㅂ", "ㅄ", "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ" };

        /// <summary>Hangul syllable → compatibility jamo (초성, 중성, 종성 "" when none). False for anything else.</summary>
        public static bool Decompose(string letter, out string initial, out string medial, out string final)
        {
            initial = medial = final = "";
            if (string.IsNullOrEmpty(letter) || letter.Length != 1) return false;
            int code = letter[0] - 0xAC00;
            if (code < 0 || code >= 11172) return false;
            initial = Initials[code / (21 * 28)]; medial = Medials[(code / 28) % 21]; final = Finals[code % 28];
            return true;
        }

        /// <summary>Canonical 중성 order of the codex rows (ㅏ ㅓ ㅗ ㅜ, then the rest in Unicode order).</summary>
        public static int MedialRank(string medial)
        {
            switch (medial) { case "ㅏ": return 0; case "ㅓ": return 1; case "ㅗ": return 2; case "ㅜ": return 3; }
            int i = Array.IndexOf(Medials, medial);
            return i < 0 ? 99 : 10 + i;
        }

        public static int FinalRank(string final) { int i = Array.IndexOf(Finals, final ?? ""); return i < 0 ? 99 : i; }
    }
}
