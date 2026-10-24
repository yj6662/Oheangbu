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
            /// <summary>S5-b slice 8: the reaction pass runs AFTER the rows above and is counted apart (rows / pass / fail stay the original 125); it still decides the run's status.</summary>
            public int reactionRows, reactionPass, reactionFail;
            /// <summary>S5-b slice 9 (D308-150 V2): the swing pass runs after the reaction pass and is counted apart too - the ㅓ rows again with REAL swings (the swing driver sweeps a scripted brush) and the 갈무리 rows on the real wiring.</summary>
            public int swingRows, swingPass, swingFail;
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
        int _xRows, _xPass, _xFail;           // slice 8: the reaction pass
        int _sRows, _sPass, _sFail;           // slice 9: the swing pass
        bool _swingPass;
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
        bool Commit(int index, Vol2SpellRowDto row, Injected injected, OnceRow once, bool extra = false)
        {
            string verdict = injected.Verdict, note = injected.Note;
            var d = injected.Decision;
            if (once != null && once.Verdict != null && !once.Verdict.Pass) { verdict = "FAIL"; note += (note.Length > 0 ? "; " : "") + once.Verdict.Note; }
            if (once != null && once.Log == null && injected.Decision != null && injected.Decision.Accepted) { verdict = "FAIL"; note += (note.Length > 0 ? "; " : "") + "no cast log for the accepted cast"; }
            int newErrors = _logErrors - injected.LogErrorsBefore;
            if (newErrors > 0) note += (note.Length > 0 ? "; " : "") + "log errors during row: " + newErrors;
            if (extra)
            {
                // slice 8: the reaction pass is counted apart (the original rows / pass / fail stay what they were) and does not touch accepted / rejected
                if (verdict == "PASS") _xPass++; else { _xFail++; if (_problems.Count < 8) _problems.Add(row.Letter + " " + (once != null ? once.Variant + ": " : "") + note); }
                _xRows++;
            }
            else if (_swingPass)
            {
                // slice 9: the swing pass is counted apart as well (the 125 rows and the reaction pass stay what they were)
                if (verdict == "PASS") _sPass++; else { _sFail++; if (_problems.Count < 8) _problems.Add((row != null ? row.Letter + " " : "") + (once != null ? once.Variant + ": " : "") + note); }
                _sRows++;
            }
            else
            {
                if (d != null) { if (d.Accepted) _accepted++; else _rejected++; }
                if (verdict == "PASS") _pass++; else { _fail++; if (_problems.Count < 8) _problems.Add(row.Letter + " " + note); }
                _rows++;
            }
            WriteRow(_request.runId, index, row, d, injected.Exception, verdict, note, once);
            return verdict == "PASS";
        }

        // ---- slice 3: the 20 한 번 glyphs through the real shapes (Play only: the contacts take frames) ----

        /// <summary>Runs one scenario variant of a 한 번 row: dummies in, the letter through the channel, wait for the cast to end, compare, dummies out.
        /// Starts an unfinished row only after BeginRun; always cleans the dummies and the registry flag.</summary>
        async UniTask<bool> ProcessOnceRowAsync(int index, Vol2SpellRowDto row, string variant, System.Threading.CancellationToken token, bool swingPass = false)
        {
            var shapes = _services.Shapes;
            shapes.PoolInk?.Refill();
            float inkBefore = shapes.PoolInk != null ? shapes.PoolInk.InkNow : -1f;
            int linesBefore = shapes.Presenter != null ? shapes.Presenter.Drawn : 0;
            shapes.Registry.ExtrasOnly = true; shapes.Registry.ClearExtras();
            var scenario = Vol2OnceLab.Build(row.Frame, variant, shapes.Caster, shapes.Registry);
            SwingPassRig swingRig = null;
            try
            {
                if (swingPass)
                {
                    if (!scenario.NeedsSwing) return false;             // the swing pass runs the ㅓ rows again with real swings; the others have none
                    swingRig = SwingPassRig.Begin(shapes, _services);
                }
                var injected = Inject(row);
                var once = new OnceRow { Variant = variant, Describe = scenario.Describe, SimulatedSwing = scenario.NeedsSwing && swingRig == null, ScenarioName = swingRig != null ? Vol2SwingLab.Scenario : null };
                if (inkBefore >= 0f) once.InkDelta = (shapes.PoolInk.InkNow - inkBefore).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                Vol2CastLog log = null;
                if (injected.Decision != null && injected.Decision.Accepted) shapes.Coordinator.TryGetLog(injected.Decision.CastId, out log);
                once.Log = log;
                if (log != null)
                {
                    if (scenario.NeedsSwing)
                    {
                        await UniTask.Yield(PlayerLoopTiming.Update, token);
                        Vector3 sleeve = scenario.Origin + Vector3.up * 1.1f, tip = sleeve + scenario.Forward * shapes.Coordinator.Tuning.BrushReach;
                        if (swingRig != null) swingRig.Click(sleeve, tip);       // slice 9: a real click on the swing driver
                        else
                        {
                            long swing = _services.Ids.NextSwing().Value;
                            for (int k = 0; k < 3; k++) shapes.Swings.Raise(new Vol2SwingEvent(swing, sleeve, tip, _services.Clock.NowUs, k));
                        }
                    }
                    float until = Time.unscaledTime + 4f;
                    while (!log.Finished && Time.unscaledTime < until) await UniTask.Yield(PlayerLoopTiming.Update, token);
                    float settle = Time.unscaledTime + 0.45f;       // the earth push and the slow take a moment to show on the body
                    while (Time.unscaledTime < settle) await UniTask.Yield(PlayerLoopTiming.Update, token);
                    once.Verdict = Vol2OnceLab.Evaluate(row, scenario, log, shapes.Coordinator, _services.Rulebook.Numbers, swingRig != null);
                    if (swingRig != null) swingRig.Judge(once.Verdict, 1);
                }
                once.PresenterLines = shapes.Presenter != null ? shapes.Presenter.Drawn - linesBefore : 0;
                Commit(index, row, injected, once);
                return true;
            }
            finally { swingRig?.End(); Vol2OnceLab.Cleanup(scenario, shapes.Registry); shapes.Registry.ExtrasOnly = false; }
        }

        // ---- slice 4: the verbs with a lab provider (Play only: the contacts take frames) ----

        /// <summary>One row of a verb with a lab provider: scenario in, letter through the channel, the scenario's steps on the scope clock, the clock
        /// fast-forwarded to the end of the cast's life, the provider's verdict, everything out. A row starts from nothing (the coordinator's
        /// casts and every material are aborted first) and leaves nothing behind.</summary>
        async UniTask<bool> ProcessVerbRowAsync(int index, Vol2SpellRowDto row, IVol2LabProvider provider, System.Threading.CancellationToken token, string scenarioName = null, bool extra = false, bool swingPass = false)
        {
            var shapes = _services.Shapes;
            shapes.Coordinator.AbortAll();
            shapes.PoolInk?.Refill();
            float inkBefore = shapes.PoolInk != null ? shapes.PoolInk.InkNow : -1f;
            int linesBefore = shapes.Presenter != null ? shapes.Presenter.Drawn : 0;
            shapes.Registry.ExtrasOnly = true; shapes.Registry.ClearExtras();
            var ctx = new Vol2LabContext(shapes, _services) { Kit = _wiring != null ? _wiring.Vol2Defence : null };
            ctx.Cast = letter => { try { _wiring.LetterChannel.Raise(new DrawnLetter(letter, default, default, null, 0.6f, 0.6f, 1f, 1f, 3)); } catch (Exception e) { Debug.LogException(e); } };
            var scenario = provider.Build(row, ctx);
            SwingPassRig swingRig = null;
            try
            {
                if (swingPass)
                {
                    if (!scenario.UsesSwings) return false;             // the swing pass runs the rows that swing; the others have nothing to show again
                    swingRig = SwingPassRig.Begin(shapes, _services);
                    ctx.Driver = swingRig.Driver; ctx.Script = swingRig.Script;
                }
                var injected = Inject(row);
                var once = new OnceRow { Variant = scenario.Variant, Describe = scenario.Describe, SimulatedSwing = scenario.UsesSwings && swingRig == null, ScenarioName = scenarioName ?? Vol2VerbLab.Scenario };
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
                    if (!scenario.SkipFastForward) Vol2VerbLab.FastForward(ctx, log, injected.Decision);
                    float until = Time.unscaledTime + 4f;
                    while (!log.Finished && Time.unscaledTime < until) await UniTask.Yield(PlayerLoopTiming.Update, token);
                    for (int k = 0; k < 3; k++) await UniTask.Yield(PlayerLoopTiming.Update, token);     // the push / slow / marks take a frame to show
                    once.Verdict = provider.Evaluate(row, scenario, log, injected.Decision, ctx);
                    if (swingRig != null && once.Verdict != null) swingRig.Judge(once.Verdict, ctx.SwingsRaised);
                }
                once.PresenterLines = shapes.Presenter != null ? shapes.Presenter.Drawn - linesBefore : 0;
                Commit(index, row, injected, once, extra);
                return true;
            }
            finally { swingRig?.End(); Vol2VerbLab.Cleanup(scenario, shapes.Registry); shapes.Coordinator.AbortAll(); shapes.Registry.ExtrasOnly = false; }
        }

        // ---- slice 8: the reaction pass (real casts, real contacts; counted apart from the 125 rows) ----

        async UniTask ProcessReactionPassAsync(Vol2Rulebook book, System.Threading.CancellationToken token)
        {
            var shapes = _services.Shapes;
            var lab = new Vol2ReactionLab();
            shapes.Coordinator.Reactions.Enabled = true;
            int index = book.Spells.Count;
            foreach (var c in lab.Cases)
            {
                Vol2SpellRowDto row = null;
                foreach (var r in book.Spells) if (r.Letter == c.Letter) { row = r; break; }
                if (row == null) { _xRows++; _xFail++; if (_problems.Count < 8) _problems.Add("reaction case " + c.Variant + ": no table row for " + c.Letter); continue; }
                await ProcessVerbRowAsync(index++, row, lab.ProviderFor(c), token, Vol2ReactionLab.Scenario, true);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
        }

        // ---- slice 9: the swing pass (the ㅓ rows again with real swings through the swing driver; the 갈무리 on the real wiring; counted apart) ----

        async UniTask ProcessSwingPassAsync(Vol2Rulebook book, System.Threading.CancellationToken token)
        {
            var shapes = _services.Shapes;
            int index = book.Spells.Count + _xRows;
            _swingPass = true;
            try
            {
                if (shapes.Swing == null)
                {
                    _sRows++; _sFail++;
                    if (_problems.Count < 8) _problems.Add("swing pass: the scope has no swing driver (Vol2DefenceInstaller.BindSeams)");
                    return;
                }
                shapes.Coordinator.Reactions.Enabled = false;          // verb isolation, as in the 125 rows
                foreach (var row in book.Spells)
                {
                    var provider = Vol2VerbLab.Find(row);
                    if (provider != null) { if (await ProcessVerbRowAsync(index, row, provider, token, Vol2SwingLab.Scenario, false, true)) index++; }
                    else if (row.Verb == Verb.Once && row.Frame == Frame.Attachment)
                        foreach (string variant in Vol2OnceLab.Variants(row.Frame)) { if (await ProcessOnceRowAsync(index, row, variant, token, true)) index++; }
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }
                await ProcessGatherCasesAsync(index, token);
            }
            finally { _swingPass = false; shapes.Coordinator.Reactions.Enabled = true; }
        }

        void CommitCase(int index, string variant, string describe, string failure)
        {
            var injected = new Injected { Verdict = failure.Length == 0 ? "PASS" : "FAIL", Note = failure };
            var once = new OnceRow { Variant = variant, Describe = describe, ScenarioName = "S9-gather" };
            Commit(index, null, injected, once);
        }

        async UniTask WaitSwingClosedAsync(Vol2SwingDriver driver, System.Threading.CancellationToken token)
        {
            float until = Time.unscaledTime + 3f;
            while (driver.InSwing && Time.unscaledTime < until) await UniTask.Yield(PlayerLoopTiming.Update, token);
            for (int k = 0; k < 2; k++) await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        // The 갈무리 on the REAL wiring (the kit's gather service, the rules' ledger and cooldown, real HP of runtime dummies): the swing driver's first-contact report.
        async UniTask ProcessGatherCasesAsync(int index, System.Threading.CancellationToken token)
        {
            var shapes = _services.Shapes; var driver = shapes.Swing;
            var savedSource = driver.Source; var savedGather = driver.Gather; bool savedGate = driver.Tuning.GateByCooldown;
            var script = new Vol2ScriptedSwingSource(_services.Clock); var port = new Vol2WiringGatherPort(_wiring);
            shapes.Coordinator.AbortAll(); shapes.Registry.ExtrasOnly = true; shapes.Registry.ClearExtras();
            var ctx = new Vol2LabContext(shapes, _services) { Kit = _wiring != null ? _wiring.Vol2Defence : null };
            var sc = new Vol2LabScenario { Variant = "gather", Origin = shapes.Caster.Position, Forward = Vol2ShapeGeometry.FlatUnit(shapes.Caster.AimDirection) };
            ctx.Scenario = sc;
            Vector3 sleeve = sc.Origin + Vector3.up * 1.1f, tip = sleeve + sc.Forward * shapes.Coordinator.Tuning.BrushReach;
            try
            {
                driver.Reset(); driver.Source = script; driver.Gather = port; driver.Tuning.GateByCooldown = true;
                // 0. the production source: the player's great brush in this scene (informational when the lab scene's player is not set up for it)
                {
                    var prod = savedSource as Vol2RigSwingSource;
                    string fail = ""; string describe;
                    if (prod != null && prod.TryPose(out var ps, out var pt))
                    {
                        float length = (pt - ps).magnitude;
                        describe = "production source: sleeve-to-tip " + length.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " m from the world brush's sockets";
                        if (length < 1.0f || length > 2.4f) fail = "the capsule the rig gives is " + length.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " m long (a great brush plus a sleeve is 1.0 - 2.4 m)";
                    }
                    else describe = "production source: NA in this scene (" + (prod != null ? prod.Why : "no source") + ")";
                    CommitCase(index++, "source", describe, fail);
                }
                // 0b. the production animation: the click (HarvestAction.Pressed -> the host -> the driver) plays SwingSweep on the player, and the window opens and closes with the REAL clip
                {
                    var prod = savedSource as Vol2RigSwingSource;
                    var swing308 = prod != null ? prod.Swing : null;
                    var harvest = _wiring != null ? _wiring.Harvest : null;
                    string fail = "", describe;
                    if (swing308 == null || !swing308.Active || harvest == null)
                        describe = "production click: NA in this scene (" + (swing308 == null ? "no SpellSwing308 found" : !swing308.Active ? "SpellSwing308 is not active" : "the wiring has no HarvestAction") + ")";
                    else
                    {
                        var rec = new Vol2RecordingGatherPort();
                        driver.Source = prod; driver.Gather = rec; driver.Tuning.GateByCooldown = false;
                        int swings0 = driver.Swings, played0 = swing308.Swings, cancelled0 = driver.Cancelled;
                        harvest.TryBeginPull();
                        bool intercepted = harvest.Intercepted, idle = harvest.State == HarvestState.Idle;
                        await WaitSwingClosedAsync(driver, token);
                        var last = driver.Last;
                        double seconds = (last.ClosedUs - last.OpenedUs) / 1000000.0;
                        describe = "production click: HarvestAction.Pressed -> SpellSwing308." + driver.Tuning.BareSwingState + " -> window [" + driver.WindowStartClip.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ", "
                            + driver.WindowEndClip.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "] s of clip, closed after " + seconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s with " + last.Frames + " swept frames";
                        if (!intercepted || !idle) fail += "the legacy pull was not switched off (Intercepted " + intercepted + ", pull state " + harvest.State + "); ";
                        if (driver.Swings - swings0 != 1 || swing308.Swings - played0 != 1) fail += "swings " + (driver.Swings - swings0) + ", animations " + (swing308.Swings - played0) + ", expected 1 / 1; ";
                        if (driver.Cancelled != cancelled0 || last.Frames < 1 || last.Why != "Window") fail += "the swing did not reach its window (" + last + "); ";
                        if (Mathf.Abs(driver.WindowStartClip - 0.25f) > 0.01f || Mathf.Abs(driver.WindowEndClip - 0.60f) > 0.01f) fail += "window " + driver.WindowStartClip + ".." + driver.WindowEndClip + " is not SwingSweep's [0.25, 0.60]; ";
                        if (seconds < 0.55 || seconds > 0.95) fail += "the window closed " + seconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s after the click (the clip's 0.60 s +- a frame expected); ";
                        if (rec.Misses.Count + rec.Hits.Count != 1) fail += "reports " + (rec.Misses.Count + rec.Hits.Count) + ", expected 1; ";
                        driver.Source = script; driver.Gather = port; driver.Tuning.GateByCooldown = true;
                    }
                    CommitCase(index++, "click", describe, fail);
                }
                // 1. a swing at a body: the first enemy is gathered, once, on the real ledger
                {
                    var a = ctx.Dummy("G1_near", 1.0f, 0f); float hp0 = a.Vitals.Hp;
                    _services.Skew.Seconds += 1.3;
                    int calls0 = port.Calls, hits0 = driver.FirstHits, miss0 = driver.Misses;
                    script.Aim(sleeve, tip); bool began = driver.TryBeginSwing(); await WaitSwingClosedAsync(driver, token);
                    string fail = "";
                    if (!began) fail += "the click was refused; ";
                    if (port.Calls != calls0 + 1 || driver.FirstHits - hits0 != 1 || driver.Misses - miss0 != 0) fail += "expected one first-hit report, got calls " + (port.Calls - calls0) + " hits " + (driver.FirstHits - hits0) + " misses " + (driver.Misses - miss0) + "; ";
                    if (!(port.Last.Accepted && port.Last.Reason == "Gathered")) fail += "gather record: " + port.Last + "; ";
                    if (!(a.Vitals.Hp < hp0 && a.Vitals.Hp >= 1f)) fail += "hp " + hp0 + " -> " + a.Vitals.Hp + " (damaged but never lethal); ";
                    CommitCase(index++, "hit", "a swing at a body 1 m ahead: the brush tip's capsule meets it, ReportVol2SwingFirstHit once, 0.3 u damage with the HP floor, ink gained", fail);
                    Vol2VerbLab.Cleanup(sc, shapes.Registry);
                }
                // 2. a swing at the air: the swing is booked as a miss, once
                {
                    _services.Skew.Seconds += 1.3;
                    int calls0 = port.Calls, hits0 = driver.FirstHits, miss0 = driver.Misses;
                    script.Aim(sleeve, tip); bool began = driver.TryBeginSwing(); await WaitSwingClosedAsync(driver, token);
                    string fail = "";
                    if (!began) fail += "the click was refused; ";
                    if (port.Calls != calls0 + 1 || driver.FirstHits - hits0 != 0 || driver.Misses - miss0 != 1) fail += "expected one miss report, got calls " + (port.Calls - calls0) + " hits " + (driver.FirstHits - hits0) + " misses " + (driver.Misses - miss0) + "; ";
                    if (!(port.Last.Accepted && port.Last.Reason == "Swing")) fail += "gather record: " + port.Last + "; ";
                    CommitCase(index++, "air", "a swing at nothing: ReportVol2SwingMissed once when the window closes (the swing is booked, no damage, no ink)", fail);
                }
                // 3. two bodies in reach: one report, the body nearest the capsule's axis
                {
                    var a = ctx.Dummy("G3_axis", 1.0f, 0.1f); var b = ctx.Dummy("G3_wide", 1.2f, -0.3f); float hpA = a.Vitals.Hp, hpB = b.Vitals.Hp;
                    _services.Skew.Seconds += 1.3;
                    int calls0 = port.Calls;
                    script.Aim(sleeve, tip); bool began = driver.TryBeginSwing(); await WaitSwingClosedAsync(driver, token);
                    string fail = "";
                    if (!began) fail += "the click was refused; ";
                    if (port.Calls != calls0 + 1) fail += "expected ONE report for two bodies, got " + (port.Calls - calls0) + "; ";
                    if (!(a.Vitals.Hp < hpA && b.Vitals.Hp == hpB)) fail += "the body on the axis must be the one gathered (A " + hpA + " -> " + a.Vitals.Hp + ", B " + hpB + " -> " + b.Vitals.Hp + "); ";
                    CommitCase(index++, "first-only", "two bodies in reach, one 0.1 m off the axis and one 0.3 m: the swing reports its FIRST enemy (nearest the axis) once; the other is untouched", fail);
                    Vol2VerbLab.Cleanup(sc, shapes.Registry);
                }
                // 4. the click's gate: a swing in progress and the rules' 1.1 s refuse a second click; after the cooldown it swings again
                {
                    var a = ctx.Dummy("G4_gate", 1.0f, 0f);
                    _services.Skew.Seconds += 1.3;
                    int calls0 = port.Calls, swings0 = driver.Swings, refused0 = driver.Refused;
                    script.Aim(sleeve, tip);
                    bool first = driver.TryBeginSwing();
                    bool during = driver.TryBeginSwing();                        // the first swing is still open
                    await WaitSwingClosedAsync(driver, token);
                    bool cooling = driver.TryBeginSwing();                       // closed, but well inside 1.1 s of its report
                    _services.Skew.Seconds += 1.2;
                    bool later = driver.TryBeginSwing(); await WaitSwingClosedAsync(driver, token);
                    string fail = "";
                    if (!first || during || cooling || !later) fail += "click answers (first, during, cooling, later) = " + first + "/" + during + "/" + cooling + "/" + later + ", expected true/false/false/true; ";
                    if (driver.Swings - swings0 != 2 || driver.Refused - refused0 != 2 || port.Calls - calls0 != 2) fail += "swings " + (driver.Swings - swings0) + " refused " + (driver.Refused - refused0) + " reports " + (port.Calls - calls0) + ", expected 2 / 2 / 2; ";
                    CommitCase(index++, "gate", "a second click during a swing and one inside the rules' 1.1 s are refused (no animation for a swing the rules would refuse); after the cooldown the swing goes again", fail);
                    Vol2VerbLab.Cleanup(sc, shapes.Registry);
                }
            }
            finally
            {
                driver.Reset(); driver.Source = savedSource; driver.Gather = savedGather; driver.Tuning.GateByCooldown = savedGate;
                Vol2VerbLab.Cleanup(sc, shapes.Registry); shapes.Coordinator.AbortAll(); shapes.Registry.ExtrasOnly = false;
            }
        }

        /// <summary>Stops counting, writes the summary and closes the CSV. Returns the status (PASS | FAIL | the forced one).</summary>
        public string EndRun(string forcedStatus = null)
        {
            if (!_running) return "NOT-RUNNING";
            _running = false;
            Application.logMessageReceived -= OnLog;
            _services.Route.Adapter.Decided -= OnDecided;
            if (_kit != null && _kitHooked) { _kit.Posture.Applied -= OnPosture; _kitHooked = false; }
            string status = forcedStatus ?? (_fail == 0 && _xFail == 0 && _sFail == 0 && _exceptions == 0 && _logErrors == 0 && _rows > 0 ? "PASS" : "FAIL");
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
                // slice 8: the rows of the verbs are verb-isolation rows - they run with the reaction engine paused (as before slice 8); the reaction pass below turns it on
                // (a diagnostic run: an empty file Art/SpellVFX308/Vol2Lab/reactions_on.flag keeps the engine ON through the verb rows too - the game configuration - to see what the verbs do beside reactions)
                bool reactionsOnThroughout = File.Exists(Path.Combine(Folder, "reactions_on.flag"));
                if (_services.Shapes != null) _services.Shapes.Coordinator.Reactions.Enabled = reactionsOnThroughout;
                if (reactionsOnThroughout) Debug.Log("[Vol2Lab] reactions stay ON through the verb rows (reactions_on.flag)");
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
                if (book != null && book.Ok && _services.Shapes != null) await ProcessReactionPassAsync(book, token);
                if (book != null && book.Ok && _services.Shapes != null) await ProcessSwingPassAsync(book, token);       // slice 9: the ㅓ rows with real swings + the 갈무리 on the real wiring
                if (_services.Shapes != null) _services.Shapes.Coordinator.Reactions.Enabled = true;      // the scope's normal state
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
                csv = _csvPath ?? "", startedAt = _startedAt, finishedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), scenario = _services != null && _services.Shapes != null ? Scenario + "+" + Vol2OnceLab.Scenario + "+" + Vol2VerbLab.Scenario + "+" + Vol2ReactionLab.Scenario + "+" + Vol2SwingLab.Scenario + (_services.Shapes.Coordinator.Reactions.Enabled && File.Exists(Path.Combine(Folder, "reactions_on.flag")) ? "+reactions-on-throughout" : "") : Scenario,
                dtoHash = book != null ? book.DtoHash : "", numbersSha256 = book != null ? book.NumbersSha256 : "", tableSha256 = book != null ? book.TableSha256 : "",
                rulebookError = book != null ? book.Error ?? "" : "no rulebook",
                rows = _rows, pass = _pass, fail = _fail, accepted = _accepted, rejected = _rejected, exceptions = _exceptions, logErrors = _logErrors, framesWaited = _frames,
                reactionRows = _xRows, reactionPass = _xPass, reactionFail = _xFail,
                swingRows = _sRows, swingPass = _sPass, swingFail = _sFail,
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
