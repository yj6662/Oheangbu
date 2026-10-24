using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEngine;
using VolSpellKind = Oheangbu.Vol2.Rules.SpellKind;

namespace Oheangbu.App
{
    /// <summary>S5-b slice 1, design section 12 steps 1 to 4 and 8: the spell lab walks the 120 rows of the vol.2 table (not an executor
    /// list), injects each letter into the recognition-result channel (the same DrawnLetterEventChannelSO the drawing input raises),
    /// lets the scope's route decide it, and writes one CSV row per glyph. Slice 1 knows: glyph, body, frame, verb, peak, TemplateId,
    /// accepted / refused + reason, CastId, the plan's cost / life / budget terms, the decision time. The remaining columns of the
    /// section 12 field groups exist and are empty until slices 2 to 8 fill them. The injection does NOT prove the recognizer
    /// (a separate stroke replay does); it proves the route from the channel to the adapter.
    /// Started only by the scope, only in Vol2 mode, only after the editor command left a request. Does nothing else: no effects.</summary>
    public sealed class Vol2LabRunner : MonoBehaviour
    {
        public const string Scenario = "S1-inject-decode";

        [Serializable] sealed class Summary
        {
            public string status, runId, commit, mode, csv, startedAt, finishedAt, dtoHash, numbersSha256, tableSha256, scenario, rulebookError;
            public int rows, pass, fail, accepted, rejected, exceptions, logErrors, framesWaited;
            public string[] firstProblems = new string[0];
        }

        static readonly string[] Columns =
        {
            "RunId","Commit","ManifestHash","NumbersHash","Seed","Scenario","Index",
            "Letter","Body","Frame","Verb","Peak","TemplateId",
            "CastId","AttackId","SwingId","TargetId","LifeRevision","MaterialId",
            "Decision","Reason","Detail","InkDelta","HpDelta","Posture","WindowUs","InkCost","LifeUs","DecidedUs",
            "BudgetPerTarget","BudgetShared","BudgetBalance","ReflectLedger",
            "MaterialCreated","MaterialConsumed","MaterialDepth","OriginalOwner","Igniter",
            "SpawnUs","HitUs","EndUs","VfxMissing","VideoTimecode",
            "Expected","Actual","Verdict","Note"
        };

        CombatLoopWiring _wiring;
        Vol2Services _services;
        Vol2LabRequest _request;
        StreamWriter _csv;
        string _csvPath, _summaryPath;
        readonly List<Vol2CastDecision> _decisions = new List<Vol2CastDecision>();
        readonly List<string> _problems = new List<string>();
        int _logErrors, _rows, _pass, _fail, _exceptions, _accepted, _rejected, _frames;
        bool _finished, _started, _running;
        string _startedAt = "";

        public bool Finished => _finished;
        /// <summary>Where the run files go; null = Art/SpellVFX308/Vol2Lab (fixtures point it at a temp folder).</summary>
        public string OutputFolder { get; set; }
        public string CsvPath => _csvPath;

        public static Vol2LabRunner Create(CombatLoopWiring wiring, Vol2Services services, Vol2LabRequest request)
        {
            if (services == null || !services.Route.IsVol2) throw new InvalidOperationException("Vol2LabRunner needs a Vol2 route");
            var go = new GameObject("Vol2LabRunner");
            var runner = go.AddComponent<Vol2LabRunner>();
            runner._wiring = wiring; runner._services = services; runner._request = request ?? new Vol2LabRequest();
            return runner;
        }

        void Start()
        {
            if (_started) return;
            _started = true;
            RunAsync().Forget();
        }

        void OnDestroy()
        {
            if (_running) EndRun("INTERRUPTED");   // Play stopped mid-run: the rows so far stay in the CSV, the summary says so
            else { Application.logMessageReceived -= OnLog; CloseCsv(); }
        }

        void OnLog(string condition, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (condition != null && condition.StartsWith("[Vol2Lab]", StringComparison.Ordinal)) return;
            _logErrors++;
            if (_problems.Count < 8) _problems.Add("log " + type + ": " + (condition != null && condition.Length > 160 ? condition.Substring(0, 160) : condition));
        }

        // ---- the run, in three synchronous steps (BeginRun / ProcessRow / EndRun) so an edit-mode fixture can drive the same code that Play drives ----

        string Folder => string.IsNullOrEmpty(OutputFolder) ? Vol2LabRequestFile.Folder : OutputFolder;

        /// <summary>Opens run_&lt;runId&gt;.csv (UTF-8 with BOM) and the summary path, and starts counting Decided events and logged errors.</summary>
        public void BeginRun()
        {
            if (_running) throw new InvalidOperationException("run already begun");
            string runId = string.IsNullOrEmpty(_request.runId) ? DateTime.Now.ToString("yyyyMMdd_HHmmss") : _request.runId;
            _request.runId = runId;
            Directory.CreateDirectory(Folder);
            _csvPath = Path.Combine(Folder, "run_" + runId + ".csv");
            _summaryPath = Path.Combine(Folder, "run_" + runId + ".summary.json");
            _startedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _csv = new StreamWriter(_csvPath, false, new UTF8Encoding(true)) { AutoFlush = true };
            _csv.WriteLine(string.Join(",", Columns));
            Application.logMessageReceived += OnLog;
            _services.Route.Adapter.Decided += OnDecided;
            _running = true;
        }

        /// <summary>The rulebook could not be loaded: nothing to iterate, one FAIL row says why.</summary>
        public void ProcessUnavailable()
        {
            var book = _services.Rulebook;
            WriteRow(_request.runId, 0, null, null, "", "FAIL", "rulebook unavailable: " + (book != null ? book.Error : "no rulebook"));
            _fail++; _rows++;
        }

        /// <summary>Injects one table row's letter into the recognition-result channel and records the outcome. Never throws: an exception
        /// of a subscriber becomes a FAIL row. Returns true for PASS.</summary>
        public bool ProcessRow(int index, Vol2SpellRowDto row)
        {
            if (!_running) throw new InvalidOperationException("BeginRun first");
            _decisions.Clear();
            int logErrorsBefore = _logErrors;
            string exception = "";
            try { _wiring.LetterChannel.Raise(new DrawnLetter(row.Letter, default, default, null, 0.6f, 0.6f, 1f, 1f, 3)); }
            catch (Exception e) { exception = e.GetType().Name + ": " + e.Message; _exceptions++; if (_problems.Count < 8) _problems.Add(row.Letter + " " + exception); }

            string verdict, note = "";
            Vol2CastDecision d = _decisions.Count == 1 ? _decisions[0] : null;
            if (exception.Length > 0) { verdict = "FAIL"; note = "exception " + exception; }
            else if (_decisions.Count != 1) { verdict = "FAIL"; note = _decisions.Count + " decisions for one injected letter"; }
            else
            {
                string mismatch = Compare(row, d);
                verdict = mismatch.Length == 0 ? "PASS" : "FAIL"; note = mismatch;
            }
            int newErrors = _logErrors - logErrorsBefore;
            if (newErrors > 0) note += (note.Length > 0 ? "; " : "") + "log errors during row: " + newErrors;
            if (d != null) { if (d.Accepted) _accepted++; else _rejected++; }
            if (verdict == "PASS") _pass++; else { _fail++; if (_problems.Count < 8) _problems.Add(row.Letter + " " + note); }
            _rows++;
            WriteRow(_request.runId, index, row, d, exception, verdict, note);
            return verdict == "PASS";
        }

        /// <summary>Stops counting, writes the summary and closes the CSV. Returns the status (PASS | FAIL | the forced one).</summary>
        public string EndRun(string forcedStatus = null)
        {
            if (!_running) return "NOT-RUNNING";
            _running = false;
            Application.logMessageReceived -= OnLog;
            _services.Route.Adapter.Decided -= OnDecided;
            string status = forcedStatus ?? (_fail == 0 && _exceptions == 0 && _logErrors == 0 && _rows > 0 ? "PASS" : "FAIL");
            try { WriteSummary(status); } catch (Exception e) { Debug.LogWarning("[Vol2Lab] summary failed: " + e.Message); }
            _finished = true;
            CloseCsv();
            return status;
        }

        async UniTaskVoid RunAsync()
        {
            var token = this.GetCancellationTokenOnDestroy();
            try
            {
                // wait for the wiring to be live (it hooks its services over the first frames), then a few frames more
                while (_frames < 600 && (_wiring == null || !_wiring.isActiveAndEnabled || _wiring.LetterChannel == null))
                { await UniTask.Yield(PlayerLoopTiming.Update, token); _frames++; }
                if (_wiring == null || _wiring.LetterChannel == null) { BeginRun(); EndRun("ABORTED-NO-CHANNEL"); return; }
                for (int i = 0; i < 3; i++) { await UniTask.Yield(PlayerLoopTiming.Update, token); _frames++; }

                var book = _services.Rulebook;
                BeginRun();
                if (book == null || !book.Ok) ProcessUnavailable();
                else
                    for (int i = 0; i < book.Spells.Count; i++)
                    {
                        ProcessRow(i, book.Spells[i]);
                        await UniTask.Yield(PlayerLoopTiming.Update, token);   // one letter per frame: the scene and the presenters live between rows
                    }
                string status = EndRun();
                Debug.Log("[Vol2Lab] done " + status + " rows=" + _rows + " pass=" + _pass + " fail=" + _fail + " accepted=" + _accepted + " rejected=" + _rejected + " exceptions=" + _exceptions + " logErrors=" + _logErrors + " csv=" + _csvPath);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                _exceptions++;
                Debug.LogError("[Vol2Lab] runner failed: " + e);
                EndRun("RUNNER-EXCEPTION");
            }
        }

        void OnDecided(Vol2CastDecision d) { _decisions.Add(d); }
        static string Compare(Vol2SpellRowDto row, Vol2CastDecision d)
        {
            var bad = new List<string>();
            var g = row.Glyph;
            if (!d.Accepted) return "refused " + d.Reason + " " + d.Detail;
            if (d.Letter != row.Letter) bad.Add("letter");
            if (!d.Decoded || d.Glyph.Body != g.Body || d.Glyph.Frame != g.Frame || d.Glyph.Verb != g.Verb) bad.Add("glyph");
            if (d.Template != Vol2TemplateId.Of(g)) bad.Add("template");
            if (d.IsPeak != (g.Kind == VolSpellKind.Peak)) bad.Add("peak");
            if (d.IsRecovery != (g.Letter == '걱')) bad.Add("recovery");
            if (d.IsParry != g.IsParry) bad.Add("parry");
            if (!d.CastId.IsValid) bad.Add("castId");
            if (d.Plan == null || d.Plan.Cost != row.Cost) bad.Add("cost");
            return string.Join(",", bad);
        }

        static string Describe(char letter, Body b, Frame f, Verb v, string template, bool peak, decimal cost)
            => letter + " " + b + "/" + f + "/" + v + " " + template + " peak=" + (peak ? "Y" : "N") + " cost=" + cost.ToString(System.Globalization.CultureInfo.InvariantCulture);

        void WriteRow(string runId, int index, Vol2SpellRowDto row, Vol2CastDecision d, string exception, string verdict, string note)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var book = _services.Rulebook;
            string decision = d == null ? "NONE" : d.Accepted ? "ACCEPTED" : "REJECTED";
            string expected = row == null ? "" : Describe(row.Letter, row.Body, row.Frame, row.Verb, Vol2TemplateId.Of(row.Glyph).Name, row.Kind == VolSpellKind.Peak, row.Cost);
            string actual = d == null ? "" : d.Decoded
                ? Describe(d.Letter, d.Glyph.Body, d.Glyph.Frame, d.Glyph.Verb, d.TemplateName, d.IsPeak, d.Plan != null ? d.Plan.Cost : 0m)
                : d.Letter + " (not decoded)";
            var f = new string[]
            {
                runId, _request.commit ?? "", book != null ? book.DtoHash : "", book != null ? book.NumbersSha256 : "", "0", Scenario, index.ToString(inv),
                row != null ? row.Letter.ToString() : "", row != null ? row.Body.ToString() : "", row != null ? row.Frame.ToString() : "", row != null ? row.Verb.ToString() : "",
                row != null && row.Kind == VolSpellKind.Peak ? row.Letter.ToString() : "", row != null ? Vol2TemplateId.Of(row.Glyph).Name : "",
                d != null && d.CastId.IsValid ? d.CastId.Value.ToString(inv) : "", "", "", "", "", "",
                decision, d != null ? d.Reason.ToString() : exception.Length > 0 ? "Exception" : "NoDecision", d != null ? d.Detail : exception, "", "", "", "",
                d != null && d.Plan != null ? d.Plan.Cost.ToString(inv) : "", d != null && d.Plan != null ? d.Plan.LifeUs.ToString(inv) : "", d != null ? d.DecidedUs.ToString(inv) : "",
                d != null && d.Plan != null ? d.Plan.PerTarget.ToString(inv) : "", d != null && d.Plan != null ? d.Plan.Shared.ToString(inv) : "", "", "",
                "", "", "", "", "",
                "", "", "", "", "",
                expected, actual, verdict, note
            };
            if (f.Length != Columns.Length) throw new InvalidOperationException("CSV row has " + f.Length + " fields, header has " + Columns.Length);
            var sb = new StringBuilder();
            for (int i = 0; i < f.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Field(f[i])); }
            _csv.WriteLine(sb.ToString());
        }

        public static string Field(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            bool quote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            return quote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        void WriteSummary(string status)
        {
            if (_summaryPath == null) return;
            var book = _services != null ? _services.Rulebook : null;
            var s = new Summary
            {
                status = status, runId = _request != null ? _request.runId : "", commit = _request != null ? _request.commit : "", mode = _services != null ? _services.Mode.ToString() : "",
                csv = _csvPath ?? "", startedAt = _startedAt, finishedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), scenario = Scenario,
                dtoHash = book != null ? book.DtoHash : "", numbersSha256 = book != null ? book.NumbersSha256 : "", tableSha256 = book != null ? book.TableSha256 : "",
                rulebookError = book != null ? book.Error ?? "" : "no rulebook",
                rows = _rows, pass = _pass, fail = _fail, accepted = _accepted, rejected = _rejected, exceptions = _exceptions, logErrors = _logErrors, framesWaited = _frames,
                firstProblems = _problems.ToArray()
            };
            File.WriteAllText(_summaryPath, JsonUtility.ToJson(s, true), new UTF8Encoding(false));
        }

        void CloseCsv()
        {
            if (_csv == null) return;
            try { _csv.Flush(); _csv.Dispose(); } catch { }
            _csv = null;
        }
    }
}
