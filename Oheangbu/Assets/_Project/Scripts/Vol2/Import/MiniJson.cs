using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
namespace Oheangbu.Vol2.Import
{
    // Small recursive-descent JSON reader for the golden fixtures (no Newtonsoft in the project, JsonUtility cannot read
    // dictionaries/null/mixed arrays). Numbers keep their raw token so decimals are parsed exactly, never through double.
    public sealed class JsonNode
    {
        public enum Type { Null, Bool, Number, String, Array, Object }
        public Type Kind { get; private set; }
        string raw; bool flag;
        List<JsonNode> items; Dictionary<string, JsonNode> props; List<string> keys;
        public static readonly JsonNode Missing = new JsonNode { Kind = Type.Null };

        public bool IsNull => Kind == Type.Null;
        public int Count => items != null ? items.Count : props != null ? props.Count : 0;
        public string Str => Kind == Type.String ? raw : throw new FormatException("JSON: string expected, got " + Kind);
        public decimal Dec => Kind == Type.Number ? decimal.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture) : throw new FormatException("JSON: number expected, got " + Kind);
        public long Long => Kind == Type.Number ? long.Parse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : throw new FormatException("JSON: number expected, got " + Kind);
        public int Int => checked((int)Long);
        public bool Bool => Kind == Type.Bool ? flag : throw new FormatException("JSON: bool expected, got " + Kind);
        public IReadOnlyList<JsonNode> Items => items ?? throw new FormatException("JSON: array expected, got " + Kind);
        public IReadOnlyList<string> Keys => keys ?? throw new FormatException("JSON: object expected, got " + Kind);
        public bool Has(string key) => props != null && props.ContainsKey(key);
        public JsonNode this[string key] => props != null && props.TryGetValue(key, out var v) ? v : throw new FormatException("JSON: missing property '" + key + "'");
        public JsonNode this[int index] => Items[index];
        public JsonNode Opt(string key) => props != null && props.TryGetValue(key, out var v) ? v : Missing;

        public static JsonNode Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            int i = 0; if (text.Length > 0 && text[0] == '﻿') i = 1;
            var node = ParseValue(text, ref i); Skip(text, ref i);
            if (i != text.Length) throw new FormatException("JSON: trailing content at " + i);
            return node;
        }
        public static JsonNode Parse(byte[] utf8) => Parse(new UTF8Encoding(false, true).GetString(utf8));

        static void Skip(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++; }
        static JsonNode ParseValue(string s, ref int i)
        {
            Skip(s, ref i); if (i >= s.Length) throw new FormatException("JSON: unexpected end");
            char c = s[i];
            if (c == '{')
            {
                var n = new JsonNode { Kind = Type.Object, props = new Dictionary<string, JsonNode>(StringComparer.Ordinal), keys = new List<string>() }; i++; Skip(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return n; }
                while (true)
                {
                    Skip(s, ref i); if (i >= s.Length || s[i] != '"') throw new FormatException("JSON: property name expected at " + i);
                    string key = ReadString(s, ref i); Skip(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException("JSON: ':' expected at " + i);
                    i++; var v = ParseValue(s, ref i);
                    if (n.props.ContainsKey(key)) throw new FormatException("JSON: duplicate key '" + key + "'");
                    n.props.Add(key, v); n.keys.Add(key); Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return n; }
                    throw new FormatException("JSON: ',' or '}' expected at " + i);
                }
            }
            if (c == '[')
            {
                var n = new JsonNode { Kind = Type.Array, items = new List<JsonNode>() }; i++; Skip(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return n; }
                while (true)
                {
                    n.items.Add(ParseValue(s, ref i)); Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return n; }
                    throw new FormatException("JSON: ',' or ']' expected at " + i);
                }
            }
            if (c == '"') return new JsonNode { Kind = Type.String, raw = ReadString(s, ref i) };
            if (Literal(s, ref i, "true")) return new JsonNode { Kind = Type.Bool, flag = true };
            if (Literal(s, ref i, "false")) return new JsonNode { Kind = Type.Bool };
            if (Literal(s, ref i, "null")) return new JsonNode { Kind = Type.Null };
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            if (i == start) throw new FormatException("JSON: unexpected '" + c + "' at " + i);
            string token = s.Substring(start, i - start);
            if (!decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) throw new FormatException("JSON: bad number '" + token + "'");
            return new JsonNode { Kind = Type.Number, raw = token };
        }
        static bool Literal(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
            i += word.Length; return true;
        }
        static string ReadString(string s, ref int i)
        {
            var b = new StringBuilder(); i++;
            while (true)
            {
                if (i >= s.Length) throw new FormatException("JSON: unterminated string");
                char c = s[i++];
                if (c == '"') return b.ToString();
                if (c != '\\') { b.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("JSON: unterminated escape");
                char e = s[i++];
                switch (e)
                {
                    case '"': b.Append('"'); break; case '\\': b.Append('\\'); break; case '/': b.Append('/'); break;
                    case 'b': b.Append('\b'); break; case 'f': b.Append('\f'); break; case 'n': b.Append('\n'); break;
                    case 'r': b.Append('\r'); break; case 't': b.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length || !ushort.TryParse(s.Substring(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code)) throw new FormatException("JSON: bad \\u escape at " + i);
                        b.Append((char)code); i += 4; break;
                    default: throw new FormatException("JSON: bad escape \\" + e);
                }
            }
        }
    }
}
