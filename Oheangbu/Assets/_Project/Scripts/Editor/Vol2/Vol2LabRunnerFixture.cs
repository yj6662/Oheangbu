using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-b slice 1 edit-mode fixture of the lab runner: the SAME BeginRun / ProcessRow / EndRun code that Play drives (one letter
    /// per frame there), driven here row by row on a wiring + recognition channel built in memory (HideAndDontSave, no scene opened or saved,
    /// no Play). Files go to a temp folder that is deleted afterwards. Queue:
    /// python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2LabRunnerFixture Run "all"
    ///   F1  120 rows through the channel -> wiring -> Vol2 route -> adapter: 120 PASS, status PASS, summary + CSV written
    ///   F2  the CSV: UTF-8 BOM, header + 120 rows, every row has the header's column count, CastIds 1..120, 24 templates, 20 peaks, 120 ACCEPTED
    ///   F3  an unavailable rulebook: one FAIL row, status FAIL (the run cannot silently pass)
    ///   F4  a closed glyph (걱): 119 PASS + 1 FAIL(Locked), status FAIL
    ///   F5  a throwing channel subscriber becomes a FAIL row, counted in the summary
    ///   F6  CSV quoting
    /// Not covered (Play): the real scope building, one letter per frame, the lab scene, the other channel subscribers of the lab.</summary>
    public static class Vol2LabRunnerFixture
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }
        [Serializable] sealed class SummaryRead { public string status, runId, csv, mode; public int rows, pass, fail, accepted, rejected, exceptions, logErrors; }

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-runner-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-runner-fixture: editor busy (play / compile / import)";
            var r = new Report();
            var made = new List<Object>();
            string temp = Path.Combine(Path.GetTempPath(), "vol2lab_fixture_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try { Body(r, made, temp); } catch (Exception e) { Check(r, "EXCEPTION", false, e.ToString()); }
            finally
            {
                foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch (Exception) { }
            }
            return JsonUtility.ToJson(r, true);
        }

        static void Check(Report r, string id, bool ok, string detail)
        {
            if (ok) r.pass++; else r.fail++;
            r.lines.Add((ok ? "PASS " : "FAIL ") + id + " " + detail);
        }

        sealed class Rig
        {
            public Vol2LabRunner Runner; public Vol2Services Services; public CombatLoopWiring Wiring; public DrawnLetterEventChannelSO Channel;
        }

        static Rig NewRig(List<Object> made, string name, Vol2Rulebook book, Func<Oheangbu.Vol2.Rules.Glyph, bool> isOpen, string temp, string runId)
        {
            var go = new GameObject("Vol2Runner_" + name) { hideFlags = HideFlags.HideAndDontSave }; made.Add(go);
            var wiring = go.AddComponent<CombatLoopWiring>();
            var channel = ScriptableObject.CreateInstance<DrawnLetterEventChannelSO>(); channel.hideFlags = HideFlags.HideAndDontSave; made.Add(channel);
            var config = ScriptableObject.CreateInstance<CombatConfigSO>(); config.hideFlags = HideFlags.HideAndDontSave; made.Add(config);
            Set(wiring, "_letterDrawn", channel); Set(wiring, "_resolver", new SpellResolver(null)); Set(wiring, "_config", config);
            // OnEnable does not run in edit mode: subscribe the wiring's handler the way OnEnable would
            var handler = (Action<DrawnLetter>)Delegate.CreateDelegate(typeof(Action<DrawnLetter>), wiring, typeof(CombatLoopWiring).GetMethod("OnLetterDrawn", BindingFlags.Instance | BindingFlags.NonPublic));
            channel.Subscribe(handler);
            double now = 0;
            var services = Vol2Installer.Build(SpellRuntimeMode.Vol2, book, isOpen, () => now += 0.001);
            wiring.ConstructVol2(services.Route);
            var runner = Vol2LabRunner.Create(wiring, services, new Vol2LabRequest { runId = runId, commit = "fixture" });
            runner.gameObject.hideFlags = HideFlags.HideAndDontSave; made.Add(runner.gameObject);
            runner.OutputFolder = temp;
            return new Rig { Runner = runner, Services = services, Wiring = wiring, Channel = channel };
        }

        static void Set(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new MissingFieldException(target.GetType().Name, field);
            f.SetValue(target, value);
        }

        static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>(); var sb = new StringBuilder(); bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else quoted = false; } else sb.Append(c); }
                else if (c == '"') quoted = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Length = 0; }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields;
        }

        static void Body(Report r, List<Object> made, string temp)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "F0", book.Ok, book.Ok ? "rulebook loaded (" + book.Spells.Count + " spells)" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;

            // F1 + F2: the full 120-row run
            var rig = NewRig(made, "Full", book, _ => true, temp, "fixture_full");
            rig.Runner.BeginRun();
            int passRows = 0;
            for (int i = 0; i < book.Spells.Count; i++) if (rig.Runner.ProcessRow(i, book.Spells[i])) passRows++;
            string status = rig.Runner.EndRun();
            var summary = JsonUtility.FromJson<SummaryRead>(File.ReadAllText(Path.Combine(temp, "run_fixture_full.summary.json")));
            Check(r, "F1", passRows == 120 && status == "PASS" && summary.status == "PASS" && summary.rows == 120 && summary.pass == 120 && summary.fail == 0 && summary.accepted == 120 && summary.rejected == 0 && summary.exceptions == 0 && summary.logErrors == 0 && summary.mode == "Vol2",
                "120 rows: " + passRows + " PASS, status " + status + ", summary rows " + summary.rows + " accepted " + summary.accepted + " exceptions " + summary.exceptions + " logErrors " + summary.logErrors);

            string csvPath = Path.Combine(temp, "run_fixture_full.csv");
            byte[] bytes = File.ReadAllBytes(csvPath);
            var lines = File.ReadAllLines(csvPath, Encoding.UTF8).Where(l => l.Length > 0).ToList();
            var header = ParseCsvLine(lines[0]);
            var rows = lines.Skip(1).Select(ParseCsvLine).ToList();
            int Col(string name) => header.IndexOf(name);
            bool bom = bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            string[] required = { "RunId", "Commit", "ManifestHash", "NumbersHash", "Seed", "Scenario", "Letter", "Body", "Frame", "Verb", "Peak", "TemplateId", "CastId", "AttackId", "SwingId", "TargetId", "LifeRevision", "MaterialId", "Decision", "Reason", "Verdict", "Expected", "Actual" };
            Check(r, "F2a", bom && lines.Count == 121 && rows.All(x => x.Count == header.Count) && required.All(c => header.Contains(c)) && header.Count == header.Distinct().Count(), "UTF-8 BOM, header + 120 rows, " + header.Count + " columns on every row, the section 12 column groups present");
            Check(r, "F2b", rows.Select(x => x[Col("Letter")]).SequenceEqual(book.Spells.Select(s => s.Letter.ToString())), "rows follow the 120 table rows in order");
            Check(r, "F2c", rows.Select(x => x[Col("CastId")]).SequenceEqual(Enumerable.Range(1, 120).Select(i => i.ToString())) && rows.All(x => x[Col("Decision")] == "ACCEPTED" && x[Col("Verdict")] == "PASS"), "CastIds 1..120, 120 ACCEPTED, 120 PASS");
            Check(r, "F2d", rows.Select(x => x[Col("TemplateId")]).Distinct().Count() == 24 && rows.Count(x => x[Col("Peak")].Length > 0) == 20 && rows.All(x => x[Col("RunId")] == "fixture_full" && x[Col("Commit")] == "fixture" && x[Col("ManifestHash")] == book.DtoHash && x[Col("NumbersHash")] == book.NumbersSha256),
                "24 templates, 20 peak rows, run id / commit / manifest hash / numbers hash on every row");
            Check(r, "F2e", rig.Services.Ids.CastsIssued == 120 && rig.Services.Route.Adapter.Received == 120 && rig.Wiring.SpellMode == SpellRuntimeMode.Vol2, "the adapter received each letter exactly once through the channel and the wiring");

            // F3: unavailable rulebook
            var down = NewRig(made, "Down", Vol2Rulebook.Unavailable("fixture: no data"), null, temp, "fixture_down");
            down.Runner.BeginRun(); down.Runner.ProcessUnavailable(); string downStatus = down.Runner.EndRun();
            var downLines = File.ReadAllLines(Path.Combine(temp, "run_fixture_down.csv"), Encoding.UTF8).Where(l => l.Length > 0).ToList();
            var downRow = ParseCsvLine(downLines[1]);
            Check(r, "F3", downStatus == "FAIL" && downLines.Count == 2 && downRow[Col("Verdict")] == "FAIL" && downRow[Col("Note")].Contains("fixture: no data"), "an unavailable rulebook gives one FAIL row and status " + downStatus);

            // F4: a closed glyph
            var locked = NewRig(made, "Locked", book, g => g.Letter != '걱', temp, "fixture_locked");
            locked.Runner.BeginRun();
            int lockedPass = 0; for (int i = 0; i < book.Spells.Count; i++) if (locked.Runner.ProcessRow(i, book.Spells[i])) lockedPass++;
            string lockedStatus = locked.Runner.EndRun();
            var lockedRows = File.ReadAllLines(Path.Combine(temp, "run_fixture_locked.csv"), Encoding.UTF8).Where(l => l.Length > 0).Skip(1).Select(ParseCsvLine).ToList();
            var fail = lockedRows.Where(x => x[Col("Verdict")] == "FAIL").ToList();
            Check(r, "F4", lockedPass == 119 && lockedStatus == "FAIL" && fail.Count == 1 && fail[0][Col("Letter")] == "걱" && fail[0][Col("Reason")] == "Locked" && fail[0][Col("Decision")] == "REJECTED", "119 PASS + 1 FAIL(Locked, 걱), status " + lockedStatus);

            // F5: a throwing subscriber
            var thrower = NewRig(made, "Throw", book, null, temp, "fixture_throw");
            thrower.Channel.Subscribe(_ => throw new InvalidOperationException("fixture subscriber failure"));
            thrower.Runner.BeginRun();
            bool thrown = thrower.Runner.ProcessRow(0, book.Spells[0]);
            string thrownStatus = thrower.Runner.EndRun();
            var thrownSummary = JsonUtility.FromJson<SummaryRead>(File.ReadAllText(Path.Combine(temp, "run_fixture_throw.summary.json")));
            var thrownRow = ParseCsvLine(File.ReadAllLines(Path.Combine(temp, "run_fixture_throw.csv"), Encoding.UTF8).Where(l => l.Length > 0).ToList()[1]);
            Check(r, "F5", !thrown && thrownStatus == "FAIL" && thrownSummary.exceptions == 1 && thrownRow[Col("Verdict")] == "FAIL" && thrownRow[Col("Note")].Contains("fixture subscriber failure"), "a throwing subscriber is a FAIL row (exceptions " + thrownSummary.exceptions + "), the run goes on");

            // F6: CSV quoting
            Check(r, "F6", Vol2LabRunner.Field("a,b") == "\"a,b\"" && Vol2LabRunner.Field("say \"x\"") == "\"say \"\"x\"\"\"" && Vol2LabRunner.Field("plain") == "plain" && Vol2LabRunner.Field(null) == "" && ParseCsvLine("\"a,b\",c").SequenceEqual(new[] { "a,b", "c" }), "fields with comma / quote / newline are quoted");
        }
    }
}
