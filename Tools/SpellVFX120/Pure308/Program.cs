// SPEC-SPELL-120-308 L2 runner: pure C# rule code executed outside Unity (built and started by spell120_pure308.py).
// Compiled together with: every source whose first line is "// PURE308" (stage first, then Assets), Core/Domain/*.cs,
// Combat/AreaGeometry.cs, Spellcraft/*.cs. References: netstandard.dll + UnityEngine.CoreModule.dll only.
// Usable here: Vector3 / Mathf arithmetic and plain classes. NOT usable (native calls throw): Quaternion.AngleAxis,
// AnimationCurve, ScriptableObject / MonoBehaviour creation, Time, Debug, Physics.
// A work package adds Cases_WPnn.cs with:  static partial void RunWPnn(Report r, Context c) { ... }
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Oheangbu.Spellcraft;

namespace Pure308
{
    public sealed class Report
    {
        public sealed class Item { public string Clause, Text, Detail; public bool Ok; }
        public readonly List<Item> Items = new List<Item>();
        public readonly List<KeyValuePair<string, string>> Facts = new List<KeyValuePair<string, string>>();
        public int Failed;

        // clause = an id of clauses308.csv (glyph-number) or an AC id (A2, A4, W00 ...).
        public bool Check(string clause, bool ok, string text, string detail = "")
        {
            Items.Add(new Item { Clause = clause, Ok = ok, Text = text, Detail = detail ?? "" });
            if (!ok) { Failed++; Console.WriteLine("FAIL " + clause + ": " + text + (string.IsNullOrEmpty(detail) ? "" : " [" + detail + "]")); }
            return ok;
        }

        public void Fact(string key, object value) => Facts.Add(new KeyValuePair<string, string>(key, Convert.ToString(value, CultureInfo.InvariantCulture)));

        public bool Near(float a, float b, float tolerance = 1e-4f) => Math.Abs(a - b) <= tolerance;

        static string Quote(string text)
        {
            var b = new StringBuilder("\"");
            foreach (char c in text ?? "")
            {
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (c == '\n') b.Append("\\n");
                else if (c == '\r') b.Append("\\r");
                else if (c == '\t') b.Append("\\t");
                else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }

        public string ToJson()
        {
            var b = new StringBuilder();
            b.Append("{\n \"status\": ").Append(Quote(Failed == 0 ? "COMPLETE" : "FAILED")).Append(",\n \"checks\": ").Append(Items.Count)
                .Append(",\n \"failed\": ").Append(Failed).Append(",\n \"facts\": {");
            for (int i = 0; i < Facts.Count; i++)
                b.Append(i == 0 ? "\n  " : ",\n  ").Append(Quote(Facts[i].Key)).Append(": ").Append(Quote(Facts[i].Value));
            b.Append("\n },\n \"items\": [");
            for (int i = 0; i < Items.Count; i++)
                b.Append(i == 0 ? "\n  " : ",\n  ").Append("{\"clause\": ").Append(Quote(Items[i].Clause)).Append(", \"ok\": ").Append(Items[i].Ok ? "true" : "false")
                    .Append(", \"text\": ").Append(Quote(Items[i].Text)).Append(", \"detail\": ").Append(Quote(Items[i].Detail)).Append('}');
            b.Append("\n ]\n}\n");
            return b.ToString();
        }
    }

    public sealed class Book
    {
        public string Path = "";
        public readonly List<SpellBookSO.Entry> Entries = new List<SpellBookSO.Entry>();
        public SpellBookSO.Entry[] Array => Entries.ToArray();
    }

    public sealed class Context
    {
        public string Root = "", SheetsDir = "", OutDir = "";
        public string CanonicalCsv = "", UnlockCsv = "";
        public readonly List<SpellSheet308> Sheets = new List<SpellSheet308>();
        public readonly List<Book> Books = new List<Book>();
        public SpellBuildResult308 Build;          // the table built from the sheets (set by RunTable)
        public Book MainBook;                      // the book copy the main scene reads
        public SpellRow Row(char letter) => Build != null && Build.Ok ? Build.Rows[SpellGrammar308.IndexOf(letter) - 1] : null;
        public SpellRow RowAt(int index) => Build.Rows[index - 1];
    }

    public static partial class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            if (args.Length < 4) { Console.WriteLine("usage: Pure308 <repo root> <sheets dir> <books tsv> <out dir>"); return 2; }
            var c = new Context { Root = args[0], SheetsDir = args[1], OutDir = args[3] };
            c.CanonicalCsv = File.ReadAllText(System.IO.Path.Combine(c.Root, "Docs", CanonicalName), Encoding.UTF8);
            foreach (string path in Directory.GetFiles(c.SheetsDir, "Rules308_*.csv"))
                c.Sheets.Add(new SpellSheet308(System.IO.Path.GetFileNameWithoutExtension(path), File.ReadAllText(path, Encoding.UTF8)));
            string unlock = System.IO.Path.Combine(c.SheetsDir, "Unlock308.csv");
            c.UnlockCsv = File.Exists(unlock) ? File.ReadAllText(unlock, Encoding.UTF8) : "";
            ReadBooks(args[2], c);
            Directory.CreateDirectory(c.OutDir);
            var r = new Report();
            try
            {
                RunTable(r, c);
                RunWP00(r, c); RunWP02(r, c); RunWP03(r, c); RunWP04(r, c); RunWP05(r, c); RunWP06(r, c); RunWP07(r, c);
                RunWP08(r, c); RunWP09(r, c); RunWP10(r, c); RunWP11(r, c); RunWP12(r, c); RunWP13(r, c); RunWP14(r, c);
                RunCore308(r, c);
            }
            catch (Exception exception) { r.Check("runner", false, "unhandled exception", exception.ToString()); }
            Directory.CreateDirectory(c.OutDir);
            File.WriteAllText(System.IO.Path.Combine(c.OutDir, "pure308_report.json"), r.ToJson(), new UTF8Encoding(false));
            Console.WriteLine("pure308: " + r.Items.Count + " checks, " + r.Failed + " failed");
            return r.Failed == 0 ? 0 : 1;
        }

        const string CanonicalName = "오행부_작도어휘_v0_1.csv";

        static partial void RunWP00(Report r, Context c);
        static partial void RunWP02(Report r, Context c);
        static partial void RunWP03(Report r, Context c);
        static partial void RunWP04(Report r, Context c);
        static partial void RunWP05(Report r, Context c);
        static partial void RunWP06(Report r, Context c);
        static partial void RunWP07(Report r, Context c);
        static partial void RunWP08(Report r, Context c);
        static partial void RunWP09(Report r, Context c);
        static partial void RunWP10(Report r, Context c);
        static partial void RunWP11(Report r, Context c);
        static partial void RunWP12(Report r, Context c);
        static partial void RunWP13(Report r, Context c);
        static partial void RunWP14(Report r, Context c);
        static partial void RunCore308(Report r, Context c);   // 2026-10-04 fix pass: TEST-only gate, cast order, cue life (Cases_Core308.cs)

        // books.tsv (written by spell120_pure308.py from the SpellBookSO assets):
        //   book <path> <main 0|1>
        //   entry <letter> <Kind> <Element> <BasePower> <AreaShape> <ProjectileSpeedMul> <AreaAngle> <AreaRadius> <AreaLength>
        //         <AreaSpeed> <AreaImpactDelay> <VolleyShots> <VolleyInterval> <ScatterVolley>
        static void ReadBooks(string path, Context c)
        {
            Book book = null;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string[] cells = line.Split('\t');
                if (cells[0] == "book")
                {
                    book = new Book { Path = cells[1] }; c.Books.Add(book);
                    if (cells.Length > 2 && cells[2] == "1") c.MainBook = book;
                }
                else if (cells[0] == "entry" && book != null)
                {
                    float F(int at) => float.Parse(cells[at], CultureInfo.InvariantCulture);
                    book.Entries.Add(new SpellBookSO.Entry
                    {
                        Letter = cells[1], Kind = (SpellKind)int.Parse(cells[2]), Element = (Oheangbu.Core.Domain.Element)int.Parse(cells[3]),
                        BasePower = F(4), AreaShape = (AreaShape)int.Parse(cells[5]), ProjectileSpeedMul = F(6), AreaAngle = F(7),
                        AreaRadius = F(8), AreaLength = F(9), AreaSpeed = F(10), AreaImpactDelay = F(11), VolleyShots = int.Parse(cells[12]),
                        VolleyInterval = F(13), ScatterVolley = cells[14] == "1",
                    });
                }
            }
        }
    }
}
