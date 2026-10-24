using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using Oheangbu.Combat;
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
            "Expected","Actual","Verdict","Note",
            // slice 3 (S3-once): empty on the rows the slice-1 scenario writes
            "Variant","ShapeKind","ContactCount","PaidContacts","HitTargets","ExpectedTargets","UnexpectedTargets","DamageUnits","HpApplied","BodyAction","BodyObserved",
            "ParryWindowUs","SwingReports","SwingDuplicates","InkPaid","LabSimulated","ScenarioNote","PresenterLines",
            // slice 4 (S4-verb): the share ledger (kind:body:amount of every paid contact) and the peak rule checks that held
            "ShareLedger","PeakChecks"
        };

        /// <summary>The slice 3 part of a CSV row: what the cast did to the scenario's dummies.</summary>
        sealed class OnceRow
        {
            public string Variant, Describe, ScenarioName; public Vol2CastLog Log; public Vol2OnceVerdict Verdict; public bool SimulatedSwing;
            /// <summary>Measured: the pool before the letter and right after it (empty without a pool). The cast's requested cost is InkPaid.</summary>
            public string InkDelta = ""; public int PresenterLines;
        }

        // slice 2 (defence): when the wiring has a Vol2 defence kit, each row starts from a clean defence state (ink full, no parry cooldown, no
        // recovery, no posture; the session ledger is not touched), and InkDelta / HpDelta / Posture / WindowUs are filled where the cast produced
        // them: InkDelta = the ink the cast's payment took (negative; checked against the plan's cost), HpDelta = the HP a 걱 reserves (not yet
        // given), Posture / WindowUs only when a posture event happened during the row. A row the slice 3 shapes ran (a 한 번 row) keeps slice 3's
        // InkDelta / HpDelta (the dummies' side).
        Vol2DefenceKit _kit;
        bool _kitHooked;
        Vol2PostureRecord? _rowPosture;
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
            var injected = Inject(row);
            return Commit(index, row, injected, null);
        }

        /// <summary>What one injected letter did at the channel: the decisions, an exception of a subscriber, and the slice 1 comparison.</summary>
        sealed class Injected
        {
            public Vol2CastDecision Decision; public string Exception = "", Verdict, Note = ""; public int LogErrorsBefore, Decisions;
        }

        Injected Inject(Vol2SpellRowDto row)
        {
            _decisions.Clear();
            var r = new Injected { LogErrorsBefore = _logErrors };
            _rowPosture = null;
            _kit = _wiring != null && _wiring.Vol2Defence != null ? _wiring.Vol2Defence : _kit;   // bound by the installer at build, or by the wiring on its first frame
            if (_kit != null && !_kitHooked) { _kit.Posture.Applied += OnPosture; _kitHooked = true; }
            if (_kit != null) _kit.ResetForRow();
            try { _wiring.LetterChannel.Raise(new DrawnLetter(row.Letter, default, default, null, 0.6f, 0.6f, 1f, 1f, 3)); }
            catch (Exception e) { r.Exception = e.GetType().Name + ": " + e.Message; _exceptions++; if (_problems.Count < 8) _problems.Add(row.Letter + " " + r.Exception); }
            r.Decisions = _decisions.Count;
            r.Decision = _decisions.Count == 1 ? _decisions[0] : null;
            if (r.Exception.Length > 0) { r.Verdict = "FAIL"; r.Note = "exception " + r.Exception; }
            else if (_decisions.Count != 1) { r.Verdict = "FAIL"; r.Note = _decisions.Count + " decisions for one injected letter"; }
            else
            {
                string mismatch = Compare(row, r.Decision);
                if (mismatch.Length == 0 && _kit != null) mismatch = CompareDefence(r.Decision, _kit.LastPayment);
                r.Verdict = mismatch.Length == 0 ? "PASS" : "FAIL"; r.Note = mismatch;
            }
            return r;
        }

        /// <summary>Counts the row and writes it. <paramref name="once"/> (slice 3) carries what the cast did and can turn the verdict to FAIL.</summary>
        bool Commit(int index, Vol2SpellRowDto row, Injected injected, OnceRow once)
        {
            string verdict = injected.Verdict, note = injected.Note;
            var d = injected.Decision;
            if (once != null && once.Verdict != null && !once.Verdict.Pass) { verdict = "FAIL"; note += (note.Length > 0 ? "; " : "") + once.Verdict.Note; }
            if (once != null && once.Log == null && injected.Decision != null && injected.Decision.Accepted) { verdict = "FAIL"; note += (note.Length > 0 ? "; " : "") + "no cast log for the accepted cast"; }
            int newErrors = _logErrors - injected.LogErrorsBefore;
            if (newErrors > 0) note += (note.Length > 0 ? "; " : "") + "log errors during row: " + newErrors;
            if (d != null) { if (d.Accepted) _accepted++; else _rejected++; }
            if (verdict == "PASS") _pass++; else { _fail++; if (_problems.Count < 8) _problems.Add(row.Letter + " " + note); }
            _rows++;
            WriteRow(_request.runId, index, row, d, injected.Exception, verdict, note, once);
            return verdict == "PASS";
        }

        // ---- slice 3: the 20 한 번 glyphs through the real shapes (Play only: the contacts take frames) ----

        /// <summary>Runs one scenario variant of a 한 번 row: dummies in, the letter through the channel, wait for the cast to end, compare, dummies out.
        /// Starts an unfinished row only after BeginRun; always cleans the dummies and the registry flag.</summary>
        async UniTask ProcessOnceRowAsync(int index, Vol2SpellRowDto row, string variant, System.Threading.CancellationToken token)
        {
            var shapes = _services.Shapes;
            shapes.PoolInk?.Refill();
            float inkBefore = shapes.PoolInk != null ? shapes.PoolInk.InkNow : -1f;
            int linesBefore = shapes.Presenter != null ? shapes.Presenter.Drawn : 0;
            shapes.Registry.ExtrasOnly = true; shapes.Registry.ClearExtras();
            var scenario = Vol2OnceLab.Build(row.Frame, variant, shapes.Caster, shapes.Registry);
            try
            {
                var injected = Inject(row);
                var once = new OnceRow { Variant = variant, Describe = scenario.Describe, SimulatedSwing = scenario.NeedsSwing };
                if (inkBefore >= 0f) once.InkDelta = (shapes.PoolInk.InkNow - inkBefore).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                Vol2CastLog log = null;
                if (injected.Decision != null && injected.Decision.Accepted) shapes.Coordinator.TryGetLog(injected.Decision.CastId, out log);
                once.Log = log;
                if (log != null)
                {
                    if (scenario.NeedsSwing)
                    {
                        await UniTask.Yield(PlayerLoopTiming.Update, token);
                        long swing = _services.Ids.NextSwing().Value;
                        Vector3 sleeve = scenario.Origin + Vector3.up * 1.1f, tip = sleeve + scenario.Forward * shapes.Coordinator.Tuning.BrushReach;
                        for (int k = 0; k < 3; k++) shapes.Swings.Raise(new Vol2SwingEvent(swing, sleeve, tip, _services.Clock.NowUs, k));
                    }
                    float until = Time.unscaledTime + 4f;
                    while (!log.Finished && Time.unscaledTime < until) await UniTask.Yield(PlayerLoopTiming.Update, token);
                    float settle = Time.unscaledTime + 0.45f;       // the earth push and the slow take a moment to show on the body
                    while (Time.unscaledTime < settle) await UniTask.Yield(PlayerLoopTiming.Update, token);
                    once.Verdict = Vol2OnceLab.Evaluate(row, scenario, log, shapes.Coordinator, _services.Rulebook.Numbers);
                }
                once.PresenterLines = shapes.Presenter != null ? shapes.Presenter.Drawn - linesBefore : 0;
                Commit(index, row, injected, once);
            }
            finally { Vol2OnceLab.Cleanup(scenario, shapes.Registry); shapes.Registry.ExtrasOnly = false; }
        }

        // ---- slice 4: the verbs with a lab provider (Play only: the contacts take frames) ----

        /// <summary>One row of a verb with a lab provider: scenario in, letter through the channel, the scenario's steps on the scope clock, the clock
        /// fast-forwarded to the end of the cast's life, the provider's verdict, everything out. A row starts from nothing (the coordinator's
        /// casts and every material are aborted first) and leaves nothing behind.</summary>
        async UniTask ProcessVerbRowAsync(int index, Vol2SpellRowDto row, IVol2LabProvider provider, System.Threading.CancellationToken token)
        {
            var shapes = _services.Shapes;
            shapes.Coordinator.AbortAll();
            shapes.PoolInk?.Refill();
            float inkBefore = shapes.PoolInk != null ? shapes.PoolInk.InkNow : -1f;
            int linesBefore = shapes.Presenter != null ? shapes.Presenter.Drawn : 0;
            shapes.Registry.ExtrasOnly = true; shapes.Registry.ClearExtras();
            var ctx = new Vol2LabContext(shapes, _services);
            var scenario = provider.Build(row, ctx);
            try
            {
                var injected = Inject(row);
                var once = new OnceRow { Variant = scenario.Variant, Describe = scenario.Describe, SimulatedSwing = scenario.UsesSwings, ScenarioName = Vol2VerbLab.Scenario };
                if (inkBefore >= 0f) once.InkDelta = (shapes.PoolInk.InkNow - inkBefore).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                Vol2CastLog log = null;
                if (injected.Decision != null && injected.Decision.Accepted) shapes.Coordinator.TryGetLog(injected.Decision.CastId, out log);
                once.Log = log;
                if (log != null)
                {
                    long t0 = _services.Clock.NowUs;
                    float guard = Time.unscaledTime + 30f;
                    scenario.Steps.Sort((a, b) => a.AtSeconds.CompareTo(b.AtSeconds));
                    foreach (var step in scenario.Steps)
                    {
                        while ((_services.Clock.NowUs - t0) / 1000000.0 < step.AtSeconds && Time.unscaledTime < guard) await UniTask.Yield(PlayerLoopTiming.Update, token);
                        step.Do(ctx);
                    }
                    double last = scenario.Steps.Count > 0 ? scenario.Steps[scenario.Steps.Count - 1].AtSeconds : 0.0;
                    double settle = Math.Max(last + scenario.SettleSeconds, scenario.MinSeconds);
                    while ((_services.Clock.NowUs - t0) / 1000000.0 < settle && Time.unscaledTime < guard) await UniTask.Yield(PlayerLoopTiming.Update, token);
                    Vol2VerbLab.FastForward(ctx, log, injected.Decision);
                    float until = Time.unscaledTime + 4f;
                    while (!log.Finished && Time.unscaledTime < until) await UniTask.Yield(PlayerLoopTiming.Update, token);
                    for (int k = 0; k < 3; k++) await UniTask.Yield(PlayerLoopTiming.Update, token);     // the push / slow / marks take a frame to show
                    once.Verdict = provider.Evaluate(row, scenario, log, injected.Decision, ctx);
                }
                once.PresenterLines = shapes.Presenter != null ? shapes.Presenter.Drawn - linesBefore : 0;
                Commit(index, row, injected, once);
            }
            finally { Vol2VerbLab.Cleanup(scenario, shapes.Registry); shapes.Coordinator.AbortAll(); shapes.Registry.ExtrasOnly = false; }
        }

        /// <summary>Stops counting, writes the summary and closes the CSV. Returns the status (PASS | FAIL | the forced one).</summary>
        public string EndRun(string forcedStatus = null)
        {
            if (!_running) return "NOT-RUNNING";
            _running = false;
            Application.logMessageReceived -= OnLog;
            _services.Route.Adapter.Decided -= OnDecided;
            if (_kit != null && _kitHooked) { _kit.Posture.Applied -= OnPosture; _kitHooked = false; }
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
                        var row = book.Spells[i];
                        IVol2LabProvider provider = _services.Shapes != null ? Vol2VerbLab.Find(row) : null;
                        if (provider != null) await ProcessVerbRowAsync(i, row, provider, token);       // slice 4: 남긴다 / 번진다 (and later verbs) through their lab provider
                        else if (_services.Shapes != null && row.Verb == Verb.Once)
                        {
                            // slice 3: the 20 한 번 glyphs run the real shapes against runtime dummies (path: line + wall; the others: one scenario)
                            foreach (string variant in Vol2OnceLab.Variants(row.Frame)) await ProcessOnceRowAsync(i, row, variant, token);
                        }
                        else ProcessRow(i, row);
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

        void WriteRow(string runId, int index, Vol2SpellRowDto row, Vol2CastDecision d, string exception, string verdict, string note, OnceRow once = null)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var book = _services.Rulebook;
            var defence = DefenceColumns(d);
            string decision = d == null ? "NONE" : d.Accepted ? "ACCEPTED" : "REJECTED";
            string expected = row == null ? "" : Describe(row.Letter, row.Body, row.Frame, row.Verb, Vol2TemplateId.Of(row.Glyph).Name, row.Kind == VolSpellKind.Peak, row.Cost);
            string actual = d == null ? "" : d.Decoded
                ? Describe(d.Letter, d.Glyph.Body, d.Glyph.Frame, d.Glyph.Verb, d.TemplateName, d.IsPeak, d.Plan != null ? d.Plan.Cost : 0m)
                : d.Letter + " (not decoded)";
            var f = new string[]
            {
                runId, _request.commit ?? "", book != null ? book.DtoHash : "", book != null ? book.NumbersSha256 : "", "0", once != null ? (once.ScenarioName ?? Vol2OnceLab.Scenario) : Scenario, index.ToString(inv),
                row != null ? row.Letter.ToString() : "", row != null ? row.Body.ToString() : "", row != null ? row.Frame.ToString() : "", row != null ? row.Verb.ToString() : "",
                row != null && row.Kind == VolSpellKind.Peak ? row.Letter.ToString() : "", row != null ? Vol2TemplateId.Of(row.Glyph).Name : "",
                d != null && d.CastId.IsValid ? d.CastId.Value.ToString(inv) : "", Join(once, c => c.Attack.Value.ToString(inv)), Join(once, c => c.SwingId > 0 ? c.SwingId.ToString(inv) : ""),
                Join(once, c => c.TargetId.ToString(inv)), Join(once, c => c.LifeRevision.ToString(inv)), once != null && once.Verdict != null ? once.Verdict.MaterialIds : "",
                decision, d != null ? d.Reason.ToString() : exception.Length > 0 ? "Exception" : "NoDecision", d != null ? d.Detail : exception,
                once != null ? once.InkDelta : defence[0], once != null && once.Log != null ? (-once.Log.TotalHp).ToString("0.###", inv) : defence[1], defence[2], defence[3],
                d != null && d.Plan != null ? d.Plan.Cost.ToString(inv) : "", d != null && d.Plan != null ? d.Plan.LifeUs.ToString(inv) : "", d != null ? d.DecidedUs.ToString(inv) : "",
                d != null && d.Plan != null ? d.Plan.PerTarget.ToString(inv) : "", d != null && d.Plan != null ? d.Plan.Shared.ToString(inv) : "", once != null && once.Verdict != null ? once.Verdict.Balance : "", "",
                once != null && once.Verdict != null ? once.Verdict.Created : "", once != null && once.Verdict != null ? once.Verdict.Consumed : "", once != null && once.Verdict != null ? once.Verdict.Depth : "", once != null && once.Verdict != null ? once.Verdict.Owner : "", "",
                once != null && once.Log != null ? once.Log.StartUs.ToString(inv) : "", once != null && once.Log != null && once.Log.Contacts.Count > 0 ? once.Log.Contacts[0].NowUs.ToString(inv) : "",
                once != null && once.Log != null ? once.Log.EndUs.ToString(inv) : "", "", "",
                expected, actual, verdict, note,
                once != null ? once.Variant : "", once != null && once.Log != null ? once.Log.Shape.ToString() : "", once != null && once.Log != null ? once.Log.ContactCount.ToString(inv) : "",
                once != null && once.Log != null ? once.Log.PaidContacts.ToString(inv) : "", once != null && once.Verdict != null ? once.Verdict.Hit : "", once != null && once.Verdict != null ? once.Verdict.Expected : "",
                once != null && once.Verdict != null ? once.Verdict.Unexpected : "", once != null && once.Verdict != null ? once.Verdict.Units.ToString("0.#####", inv) : "",
                once != null && once.Verdict != null ? once.Verdict.Hp.ToString("0.###", inv) : "", once != null && once.Log != null ? once.Log.BodyAction.ToString() : "",
                once != null && once.Verdict != null ? once.Verdict.BodyObserved : "",
                once != null && once.Log != null && once.Log.ParryWindowOpened ? (once.Log.WindowEndUs - once.Log.WindowStartUs).ToString(inv) : "",
                once != null && once.Log != null ? once.Log.SwingReports.ToString(inv) : "", once != null && once.Log != null ? once.Log.SwingDuplicates.ToString(inv) : "",
                once != null && once.Log != null ? once.Log.InkPaid.ToString(inv) : "", once != null ? (once.SimulatedSwing ? "swing" : "") : "", once != null ? once.Describe : "", once != null ? once.PresenterLines.ToString(inv) : "",
                once != null && once.Verdict != null ? once.Verdict.Ledger : "", once != null && once.Verdict != null ? once.Verdict.Checks : ""
            };
            if (f.Length != Columns.Length) throw new InvalidOperationException("CSV row has " + f.Length + " fields, header has " + Columns.Length);
            var sb = new StringBuilder();
            for (int i = 0; i < f.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Field(f[i])); }
            _csv.WriteLine(sb.ToString());
        }

        void OnPosture(EnemyVitals target, Vol2PostureRecord record) { _rowPosture = record; }

        // slice 2: the ink the cast took must be the plan's cost (a parry 0, 걱 26), paid by the kit's gate for this very letter. A kit that leaves
        // ordinary casts to the shape coordinator ("Deferred") has nothing to compare here.
        static string CompareDefence(Vol2CastDecision d, Vol2CastPayment payment)
        {
            if (d == null || !d.Accepted || d.Plan == null) return "";
            if (!payment.Paid || payment.Letter != d.Letter) return "payment";
            if (payment.Kind == "Deferred") return "";
            decimal diff = payment.InkSpent - d.Plan.Cost;
            return diff > 0.05m || diff < -0.05m ? "ink spent " + payment.InkSpent.ToString(System.Globalization.CultureInfo.InvariantCulture) + " != cost " + d.Plan.Cost.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        }

        // InkDelta, HpDelta, Posture, WindowUs (blank without a kit / a refused cast / a deferred payment / no posture event in the row)
        string[] DefenceColumns(Vol2CastDecision d)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string ink = "", hp = "", posture = "", window = "";
            if (_kit != null && d != null && d.Accepted && _kit.LastPayment.Paid && _kit.LastPayment.Letter == d.Letter && _kit.LastPayment.Kind != "Deferred")
            {
                ink = (-_kit.LastPayment.InkSpent).ToString(inv);
                hp = _kit.LastPayment.HpReserved > 0m ? _kit.LastPayment.HpReserved.ToString(inv) : "0";
            }
            if (_rowPosture.HasValue && _rowPosture.Value.Accepted)
            { posture = _rowPosture.Value.PostureAfter.ToString(inv); window = _rowPosture.Value.WindowUs > 0 ? _rowPosture.Value.WindowUs.ToString(inv) : "0"; }
            return new[] { ink, hp, posture, window };
        }

        static string Join(OnceRow once, Func<Vol2ContactRecord, string> pick)
        {
            if (once == null || once.Log == null || once.Log.Contacts.Count == 0) return "";
            var parts = new List<string>();
            foreach (var c in once.Log.Contacts) parts.Add(pick(c));
            return string.Join("|", parts);
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
                csv = _csvPath ?? "", startedAt = _startedAt, finishedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), scenario = _services != null && _services.Shapes != null ? Scenario + "+" + Vol2OnceLab.Scenario + "+" + Vol2VerbLab.Scenario : Scenario,
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
