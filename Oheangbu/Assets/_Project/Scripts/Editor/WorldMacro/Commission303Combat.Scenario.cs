using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using Oheangbu.Drawing;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Commission303Combat phase machine (plan §6): P0 boot · P1 recipe · P2 rehearsal · P3 rest · P4 accept · P5 waiting ·
    // P6 travel · P7 fight (Fight.cs) · P8 evidence · P9 return · P10 report · P11 completed · P12 save · P13 restart ·
    // P14 cleanup (Commission303Combat.cs). Runs from Probe303.LateUpdate (order 32000), one step per frame.
    public static partial class Commission303Combat
    {
        // ------------------------------------------------------------------ logs (flushed to the run folder)
        [Serializable] sealed class TimelineRow
        {
            public float tReal, tGame, fps; public int frame; public string phase = "", behaviour = "", lockTarget = "", keys = "";
            public Vector3 player, enemy; public float yaw, hp, ink, enemyHp, telegraphProgress, distance;
            public bool drawing, grounded, telegraph, recovering, sight, lmb, blocked;
        }
        [Serializable] sealed class HitRow { public float impactTime, power; public string target = ""; }
        [Serializable] sealed class CommitRow
        {
            public int index, startFrame, endFrame, points, penUps, scriptFrames; public float startReal, endReal, fps, inkBefore, inkAfter;
            public string phase = "", outcome = "", letter = "", initial = "", medial = "", kind = "";
            public bool success, planned, diagnosed, interrupted; public int split, strokes, recordedPoints; public float worst, avg, elapsedMs;
            public float strokeDuration, holdDuration, evaluatedBrush, measuredBrush, predictedBrush, holdPower = 1, power, damageScale = 1;
            public float distance; public string enemyState = "";
            public List<HitRow> hits = new List<HitRow>();
        }
        [Serializable] sealed class DamageRow { public float t, applied, hpAfter; public string source = "", element = "", phase = ""; public long attackId; public bool killed, wiringPaired; }
        [Serializable] sealed class PlayerHitRow { public float t, amount, hp01; public string kind = "", detail = "", phase = ""; }
        [Serializable] sealed class Logs
        {
            public List<TimelineRow> timeline = new List<TimelineRow>();
            public List<CommitRow> commits = new List<CommitRow>();
            public List<DamageRow> damage = new List<DamageRow>();
            public List<DamageRow> wiringDamage = new List<DamageRow>();
            public List<PlayerHitRow> playerHits = new List<PlayerHitRow>();
            public List<InteractionRow303> interactions = new List<InteractionRow303>();
        }
        [Serializable] sealed class ListFile<T> { public List<T> rows; }
        static Logs logs = new Logs();

        static void ResetLogs() { logs = new Logs(); UiAdapter303.Reset(); }
        static void LoadLogs()
        {
            logs = new Logs();
            try
            {
                string f = Path.Combine(RunFolder, "logs-all.json");
                if (File.Exists(f)) logs = JsonUtility.FromJson<Logs>(File.ReadAllText(f)) ?? new Logs();
            }
            catch { logs = new Logs(); }
        }
        static void FlushLogs()
        {
            if (state == null || string.IsNullOrEmpty(state.runId)) return;
            try
            {
                Directory.CreateDirectory(RunFolder);
                Harness303.WriteJson(RunFolder, "timeline.json", new ListFile<TimelineRow> { rows = logs.timeline });
                Harness303.WriteJson(RunFolder, "commits.json", new ListFile<CommitRow> { rows = logs.commits });
                Harness303.WriteJson(RunFolder, "damage.json", new DamageFile { rows = logs.damage, wiring = logs.wiringDamage });
                Harness303.WriteJson(RunFolder, "player_hits.json", new ListFile<PlayerHitRow> { rows = logs.playerHits });
                Harness303.WriteJson(RunFolder, "interactions.json", new ListFile<InteractionRow303> { rows = logs.interactions });
                File.WriteAllText(Path.Combine(RunFolder, "logs-all.json"), JsonUtility.ToJson(logs));
                WriteState();
            }
            catch (Exception e) { state.errors.Add("log flush: " + e.Message); }
        }
        [Serializable] sealed class DamageFile { public List<DamageRow> rows, wiring; }

        // ------------------------------------------------------------------ runtime (per boot, not persisted)
        static int sub; static double subAt; static float lastSample = -1; static bool talkBegun;
        static readonly Walker303 walker = new Walker303();
        static readonly Talk303 talk = new Talk303();
        static PrologueEncounter actor; static EnemyVitals vitals; static EnemyController enemy; static LockOn lockOn;
        static DrawingInputController drawing; static CombatLoopWiring wiring; static PlayerVitals playerVitals;
        static DrawnLetterEventChannelSO letterChannel; static CombatConfigSO config; static SpellBookSO book; static JamoTemplateLibrarySO library;
        static bool bound; static float offlineWorst = -1; static int offlinePoints, offlinePenUps;
        static double deathAt = -1; static bool capturedMid, captured10, captured10b, capturedReturn;
        static Vector2 Center => new Vector2(Screen.width * .5f, Screen.height * .5f);
        static double Now => EditorApplication.timeSinceStartup;
        static double Elapsed => Now - state.phaseAt;

        static void RuntimeReset()
        {
            sub = 0; subAt = Now; lastSample = -1; bound = false; offlineWorst = -1; talkBegun = false;
            actor = null; vitals = null; enemy = null; lockOn = null; drawing = null; wiring = null; playerVitals = null; letterChannel = null; config = null; book = null; library = null;
            letterActive = false; currentCommit = null; fight = null; deathAt = -1; capturedMid = captured10 = captured10b = capturedReturn = false;
            talk.End(); walker.Status = "idle";
        }

        static void EnterPhase(string phase)
        {
            if (state.phase != phase) state.notes.Add(phase + " at " + Harness303.F((float)(Now - state.began), "F1") + " s");
            state.phase = phase; state.phaseNote = ""; state.phaseAt = Now; sub = 0; subAt = Now; talkBegun = false; talk.End();
            FlushLogs(); Persist();
        }

        static float Limit(string phase)
        {
            switch (phase)
            {
                case "P0": return 90; case "P2": return 20; case "P3": return 25; case "P4": return 30; case "P5": return 10;
                case "P6": return Mathf.Max(30, state.travelLimit + 15); case "P7": return 150; case "P8": return 5;
                case "P9": return Mathf.Max(30, state.travelLimit + 15); case "P10": return 10; case "P11": return 10; case "P12": return 5; case "P13": return 120;
                default: return 0;
            }
        }

        /// <summary>A phase failure ends the run (cleanup follows in Edit mode).</summary>
        static void FailPhase(string criterion, string why)
        {
            if (!string.IsNullOrEmpty(criterion)) FailC(criterion, why);
            Fail(why); Stop(false);
        }

        static void Late()
        {
            if (state?.active != true || state.status != "RUNNING" || !EditorApplication.isPlaying) return;
            if (state.lastFrame == Time.frameCount) return;
            state.lastFrame = Time.frameCount;
            VirtualInput303.Hold = InputFrame303.Neutral(Center);
            try
            {
                float limit = Limit(state.phase);
                if (limit > 0 && Elapsed > limit) { FailPhase(PhaseCriterion(state.phase), state.phase + " time limit " + limit + " s exceeded (" + state.phaseNote + ")"); return; }
                var s = Harness303.Session;
                if (s == null) return;
                if (s.TestSaveSuffix != state.iso.suffix) { FailPhase("C1", "isolated suffix lost ('" + s.TestSaveSuffix + "') — refusing to continue on a non-isolated save"); return; }
                if (!Boot(s)) return;
                ObserveLook(s);
                Sample(s);
                switch (state.phase)
                {
                    case "P0": P0(s); break; case "P1": P1(s); break; case "P2": P2(s); break; case "P3": P3(s); break;
                    case "P4": P4(s); break; case "P5": P5(s); break; case "P6": P6(s); break; case "P7": FightTick(s); break;
                    case "P8": P8(s); break; case "P9": P9(s); break; case "P10": P10(s); break; case "P11": P11(s); break;
                    case "P12": P12(s); break; case "P13": P13(s); break;
                }
            }
            catch (Exception e) { Fail("harness exception in " + state.phase + ": " + e); Stop(false); }
        }

        static string PhaseCriterion(string phase)
        {
            switch (phase)
            {
                case "P0": return "C2"; case "P1": case "P2": return "C3"; case "P4": return "C4"; case "P5": return "C5"; case "P6": return "C6";
                case "P7": return "C8"; case "P8": return "C10"; case "P9": return "C11"; case "P10": return "C11"; case "P11": return "C12"; case "P12": case "P13": return "C14";
                default: return null;
            }
        }

        /// <summary>Per boot: session ready, loading finished, focus, one UI tidy (fixture), virtual input, event binding.</summary>
        static bool Boot(WorldMacroPlaytestSession s)
        {
            if (!s.InitializationComplete) { state.phaseNote = "waiting for session init"; return false; }
            if (UiAdapter303.LoadingInProgress == true) { state.phaseNote = "waiting for loading"; return false; }
            if (!state.focusReady) { state.phaseNote = "waiting for Game view focus"; return false; }
            if (!state.uiTidied)
            {
                string a = UiAdapter303.CloseMenu(), b = UiAdapter303.ReleaseGate();
                state.fixtures.Add("boot " + state.boots + ": one UI tidy before input (298 precedent) — CloseMenu " + a + ", ReleaseGate " + b + " [" + UiAdapter303.Describe() + "]");
                state.uiTidied = true; return false;
            }
            if (!state.inputStarted)
            {
                var assets = new List<InputActionAsset> { Harness303.Field<InputActionAsset>(s.Walker.Motor, "_actions"), Harness303.Field<InputActionAsset>(s.Walker.Drawing, "_actions") };
                state.inputSummary = VirtualInput303.Begin(assets);
                var cfg = Harness303.Field<CombatConfigSO>(s.Walker.Motor, "_config");
                float preference = s.Walker.Motor.RuntimeState != null ? s.Walker.Motor.RuntimeState.LookSensitivity : 1f;
                Steer303.Reset(cfg != null ? cfg.LookSensitivity * preference : Steer303.MeasuredPerUnit);
                state.perUnitNote = "look " + Harness303.F(Steer303.PerUnit, "F4") + "°/unit (config × preference; plan measured 0.1214)";
                state.inputStarted = true;
                Bind(s);
                if (state.boots == 1) state.realInputs.Add("virtual keyboard/mouse through the existing actions: " + state.inputSummary);
                return false;
            }
            return true;
        }

        static void Bind(WorldMacroPlaytestSession s)
        {
            if (bound) return;
            actor = s.Actors.FirstOrDefault(a => a != null && a.Id == TargetId);
            vitals = actor != null ? actor.GetComponent<EnemyVitals>() : null;
            enemy = actor != null ? actor.GetComponent<EnemyController>() : null;
            drawing = s.Walker.Drawing; wiring = s.Walker.Wiring;
            lockOn = Harness303.Or(s.Walker.Motor.GetComponent<LockOn>(), Harness303.Field<LockOn>(wiring, "_lockOn"));
            playerVitals = s.Walker.Body.GetComponent<PlayerVitals>();
            letterChannel = Harness303.Field<DrawnLetterEventChannelSO>(drawing, "_letterDrawn");
            config = Harness303.Field<CombatConfigSO>(wiring, "_config");
            book = SpellBook(s); library = Harness303.Field<JamoTemplateLibrarySO>(drawing, "_templates");
            drawing.CommitDiagnosed += OnDiagnosed; drawing.LetterInterrupted += OnInterrupted;
            letterChannel?.Subscribe(OnLetter);
            wiring.CastPlanned += OnPlanned; wiring.EnemyDamageResolved += OnWiringDamage;
            if (vitals != null) vitals.DamageResolved += OnDamage;
            if (playerVitals != null) playerVitals.Damaged += OnPlayerDamaged;
            if (enemy != null) { enemy.AttackTelegraphed += OnTelegraph; enemy.AttackImpactResolved += OnImpact; }
            bound = true;
        }

        static void Unbind()
        {
            talk.End();
            if (!bound) return;
            if (drawing != null) { drawing.CommitDiagnosed -= OnDiagnosed; drawing.LetterInterrupted -= OnInterrupted; }
            letterChannel?.Unsubscribe(OnLetter);
            if (wiring != null) { wiring.CastPlanned -= OnPlanned; wiring.EnemyDamageResolved -= OnWiringDamage; }
            if (vitals != null) vitals.DamageResolved -= OnDamage;
            if (playerVitals != null) playerVitals.Damaged -= OnPlayerDamaged;
            if (enemy != null) { enemy.AttackTelegraphed -= OnTelegraph; enemy.AttackImpactResolved -= OnImpact; }
            bound = false;
        }

        static float Ink(WorldMacroPlaytestSession s) { var pool = Harness303.Field<InkPool>(wiring, "_ink"); return pool != null ? pool.Value : -1; }
        static float SpellCost => config != null ? config.SpellInkCost : .15f;

        static void ObserveLook(WorldMacroPlaytestSession s)
        {
            bool valid = VirtualInput303.LastFedFrame == Time.frameCount && (lockOn == null || lockOn.Target == null) && !s.Walker.Motor.IsDrawing && !s.GameplayInputBlocked;
            Steer303.Observe(Steer303.CameraYaw(s), VirtualInput303.LastFed.Delta.x, valid);
        }

        static void Sample(WorldMacroPlaytestSession s)
        {
            if (Time.realtimeSinceStartup - lastSample < .1f) return;
            lastSample = Time.realtimeSinceStartup;
            var body = s.Walker.Body.transform;
            var row = new TimelineRow
            {
                tReal = (float)(Now - state.began), tGame = Time.time, frame = Time.frameCount, phase = state.phase, fps = Time.unscaledDeltaTime > 0 ? 1f / Time.unscaledDeltaTime : 0,
                player = body.position, yaw = Steer303.CameraYaw(s), hp = playerVitals != null ? playerVitals.Hp01 : -1, ink = Ink(s),
                drawing = drawing != null && drawing.InDrawMode, grounded = s.Walker.Motor.IsLocomotionGrounded, blocked = s.GameplayInputBlocked,
                keys = VirtualInput303.LastFed.KeyText, lmb = VirtualInput303.LastFed.Lmb,
                lockTarget = lockOn != null && lockOn.Target != null ? lockOn.Target.name : "",
            };
            if (actor != null)
            {
                row.enemy = actor.transform.position; row.enemyHp = vitals != null ? vitals.Hp : -1; row.behaviour = actor.Current.ToString();
                row.telegraph = enemy != null && enemy.IsTelegraphing; row.telegraphProgress = enemy != null ? enemy.TelegraphProgress : 0; row.recovering = enemy != null && enemy.IsRecovering;
                row.distance = Harness303.Flat(body.position, actor.transform.position); row.sight = Visible(s);
            }
            logs.timeline.Add(row);
        }

        // ------------------------------------------------------------------ P0 boot
        static void P0(WorldMacroPlaytestSession s)
        {
            if (sub == 0)
            {
                string store = Harness303.StorePath(s);
                bool pathOk = store != null && Path.GetFullPath(store) == Path.GetFullPath(state.iso.savedPath);
                if (pathOk && s.LoadStatus == "new") Pass("C1", "store " + store + ", LoadStatus new");
                else { FailPhase("C1", "store " + store + " (expected " + state.iso.savedPath + "), LoadStatus " + s.LoadStatus); return; }
                state.c0 = s.Progress.ledger.currency;
                var (feet, yaw, label) = CompactRebuildAuthoring.PlaceReviewSpot297(s, RestId);
                s.Teleport(feet, yaw);
                state.fixtures.Add("P0 teleport to the " + label + " review spot " + Harness303.V(feet) + " (CompactRebuildAuthoring.PlaceReviewSpot297)");
                sub = 1; subAt = Now; return;
            }
            if (sub == 1)
            {
                if (Now - subAt < 1.2 || !s.Walker.Motor.IsLocomotionGrounded) return;
                var q = s.Content.Commissions.First(x => x != null && x.Id == CommissionId);
                bool alive = vitals != null && vitals.IsAlive, full = vitals != null && Mathf.Abs(vitals.Hp - vitals.MaxHp) < .001f;
                bool clean = !s.Progress.defeated.Contains(TargetId) && !s.Progress.campaign.EncounterEvidence.Contains(TargetId) && !s.CommissionAccepted(q.Id) && !s.CommissionReported(q.Id);
                state.enemyMaxHp = vitals != null ? vitals.MaxHp : 60; state.enemyReward = s.Content.TestRules.EnemyReward;
                string detail = "alive " + alive + " hp " + Harness303.F(vitals != null ? vitals.Hp : -1, "F1") + "/" + Harness303.F(state.enemyMaxHp, "F1") + " clean " + clean + " c0 " + state.c0 + " EnemyReward " + state.enemyReward;
                if (!(alive && full && clean)) { FailPhase("C2", detail); return; }
                Pass("C2", detail);
                Harness303.Capture(RunFolder, "01_spawn_inn");
                EnterPhase("P1");
            }
        }

        // ------------------------------------------------------------------ P1 recipe at the actual screen
        static void P1(WorldMacroPlaytestSession s)
        {
            var r = state.recipe;
            state.screenW = Screen.width; state.screenH = Screen.height;
            state.screenLabVerified = StrokeLabScreens.Any(x => x.x == Screen.width && x.y == Screen.height);
            if (library == null || book == null) { FailPhase("C3", "live template library or spell book missing"); return; }
            var glyph = Strokes303.Glyph(r, library, Screen.width, Screen.height);
            var pipeline = new RecognitionPipeline(); pipeline.Initialize(library);
            float minPx = Harness303.FieldValue(drawing, "_minSamplePixelDistance") is float m ? m : 2f;
            var strokes = Strokes303.Simulate(glyph, r.penUpFrames, 60f, minPx, null, 0, 0, out float duration, out int recorded);
            var result = pipeline.Recognize(strokes, Screen.height);
            bool composed = result.Success && HangulComposer.TryCompose(result.InitialName, result.MedialName, result.FinalName, out char letter, out _, out _, out _) && letter.ToString() == r.letter;
            Strokes303.Frames(glyph, r.penUpFrames, out offlinePoints, out offlinePenUps);
            offlineWorst = result.WorstDistance;
            state.notes.Add("P1 screen " + Screen.width + "x" + Screen.height + (state.screenLabVerified ? " (lab-verified size)" : " (NOT a lab size; offline check at this size)") +
                            ": offline recognition " + (composed ? r.letter : "FAILED") + " worst " + Harness303.F(offlineWorst) + " points " + recorded);
            if (!composed) { FailPhase("C3", "recipe glyph is not recognized as " + r.letter + " at " + Screen.width + "x" + Screen.height); return; }
            EnterPhase("P2");
        }

        // ------------------------------------------------------------------ P2 rehearsal (two casts into empty air)
        static float emptyYaw;
        static void P2(WorldMacroPlaytestSession s)
        {
            var feet = s.Walker.Body.transform.position;
            if (sub == 0)
            {
                // the yaw with no content point / living actor within 25 m in a ±25° cone
                float best = 0, bestScore = float.MinValue;
                for (int i = 0; i < 24; i++)
                {
                    float yaw = i * 15f; var f = Quaternion.Euler(0, yaw, 0) * Vector3.forward; float score = 0;
                    foreach (var p in s.Content.Points) { var d = p.Position - feet; if (d.magnitude < 25 && Vector3.Angle(f, Vector3.ProjectOnPlane(d, Vector3.up)) < 25) score -= 10; }
                    foreach (var a in s.Actors) { if (a == null) continue; var d = a.transform.position - feet; if (d.magnitude < 40 && Vector3.Angle(f, Vector3.ProjectOnPlane(d, Vector3.up)) < 30) score -= 100; }
                    if (!Physics.Raycast(feet + Vector3.up * 1.5f, f, 8f, ~0, QueryTriggerInteraction.Ignore)) score += 1;
                    if (score > bestScore) { bestScore = score; best = yaw; }
                }
                emptyYaw = best; sub = 1; subAt = Now; return;
            }
            if (sub == 1 || sub == 3)
            {
                float err = Mathf.DeltaAngle(Steer303.CameraYaw(s), emptyYaw);
                VirtualInput303.Hold.Delta = Steer303.Level(s, err, 0f);
                if (Mathf.Abs(err) < 3f && Now - subAt > .3 && !drawing.InDrawMode && (drawing.EntryAllowed == null || drawing.EntryAllowed())) { BeginLetter(s, "P2"); sub++; subAt = Now; }
                return;
            }
            if (sub == 2 || sub == 4)
            {
                if (letterActive) { LetterTick(s); return; }
                if (Now - subAt < .4) return;
                if (sub == 2) { Harness303.Capture(RunFolder, "02_rehearsal_commit"); sub = 3; subAt = Now; return; }
                EvaluateRehearsal(s); return;
            }
        }

        static void EvaluateRehearsal(WorldMacroPlaytestSession s)
        {
            var rows = logs.commits.Where(c => c.phase == "P2").ToList();
            float threshold = state.plan == "A" ? .70f : Mathf.Max(.3f, state.recipe.minimumBrush);
            bool letters = rows.Count == 2 && rows.All(c => c.success && c.letter == state.recipe.letter && c.planned);
            float maxGap = rows.Count == 0 ? 1 : rows.Max(c => Mathf.Abs(c.measuredBrush - c.predictedBrush));
            float minBrush = rows.Count == 0 ? 0 : rows.Min(c => c.measuredBrush);
            state.rehearsalBrushMin = minBrush; state.measuredFps = rows.Count == 0 ? 0 : rows.Average(c => c.fps); state.predictedBrush = rows.Count == 0 ? 0 : rows.Average(c => c.predictedBrush);
            string detail = "G1 " + state.recipe.gate + " plan " + state.plan + "; G2 letters " + string.Join(",", rows.Select(c => c.letter + (c.success ? "" : "(misfire)"))) +
                            " measured brush " + string.Join("/", rows.Select(c => Harness303.F(c.measuredBrush))) + " predicted " + string.Join("/", rows.Select(c => Harness303.F(c.predictedBrush))) +
                            " fps " + string.Join("/", rows.Select(c => Harness303.F(c.fps, "F1"))) + " threshold " + Harness303.F(threshold, "F2");
            state.rehearsalPass = letters && maxGap <= .10f && minBrush >= threshold;
            if (!state.rehearsalPass) { FailPhase("C3", detail); return; }
            if (!Failed("C3")) Set("C3", state.plan == "A" ? "PASS" : "CONDITION", detail);
            EnterPhase("P3");
        }

        // ------------------------------------------------------------------ P3 rest at the inn
        static void P3(WorldMacroPlaytestSession s)
        {
            if (sub == 0) { talk.WaitRestPresentation = true; talk.MenuPick = null; talk.Begin(s, "P3", RestId, null, null, RunFolder, null); sub = 1; return; }
            if (sub == 1)
            {
                VirtualInput303.Hold = talk.Tick(Center);
                if (talk.Status == "running") { state.phaseNote = "rest: " + talk.Row.detail; if (Elapsed > 23) RestSkipped(s, "rest not finished in time"); return; }
                state.fixtures.AddRange(talk.Fixtures); logs.interactions.Add(talk.Row);
                if (talk.Status != "done") { RestSkipped(s, talk.Row.detail); return; }
                sub = 2; subAt = Now; return;
            }
            if (sub == 2)
            {
                if (Elapsed > 23) { RestSkipped(s, "rest presentation still active at 23 s"); return; }
                if (s.RestPresentationActive && Now - subAt < 12) return;
                float ink = Ink(s), hp = playerVitals.Hp01;
                if (ink < .999f || hp < .999f) { RestSkipped(s, "after rest ink " + Harness303.F(ink) + " hp " + Harness303.F(hp)); return; }
                state.restDone = true; state.realInputs.Add("P3 walked to " + RestId + " and pressed F (rest)");
                Harness303.Capture(RunFolder, "03_rest_done");
                EnterPhase("P4");
            }
        }

        static void RestSkipped(WorldMacroPlaytestSession s, string why)
        {
            talk.End(); state.restSkipped = true;
            state.findings.Add("REST_SKIPPED: " + why + " — continuing with ink " + Harness303.F(Ink(s)) + " and harvest allowed");
            EnterPhase("P4");
        }

        // ------------------------------------------------------------------ P4 / P5 accept + waiting
        static Oheangbu.Data.World.WorldMacroPlaytestSO.CommissionSpec Q(WorldMacroPlaytestSession s) => s.Content.Commissions.First(x => x != null && x.Id == CommissionId);
        static Transform Giver(WorldMacroPlaytestSession s) => Harness303.Find(s.gameObject.scene, GiverPath);

        // `expected` is read only on the first call (Begin), so callers build it only while !talkBegun. menuPick = the talk-menu row
        // the close picks once (#306 view; null = 떠나기, so only P4 accepts)
        static bool TalkStep(WorldMacroPlaytestSession s, string phase, string expected, string capture, out InteractionRow303 row, Oheangbu.App.World.UI.DialogueServiceKind306? menuPick = null)
        {
            row = null;
            if (!talkBegun) { talk.WaitRestPresentation = false; talk.MenuPick = menuPick; talk.Begin(s, phase, GiverId, Giver(s), expected, RunFolder, capture); talkBegun = true; return false; }
            VirtualInput303.Hold = talk.Tick(Center);
            if (talk.Status == "running") { state.phaseNote = phase + " talk"; return false; }
            state.fixtures.AddRange(talk.Fixtures); logs.interactions.Add(talk.Row); row = talk.Row;
            if (talk.Row.details > 1 || talk.Row.resolved > 1) state.findings.Add(phase + ": duplicate interaction events (details " + talk.Row.details + ", resolved " + talk.Row.resolved + ")");
            return true;
        }

        /// <summary>The giver's line as the talk records it. On the #306 dialogue surface (DialogueSurfaceBound) that is the pages of
        /// InteractCommission306 → Begin306 joined with "\n": speaker = the commission's, else the point's, else a leading "X: " in the
        /// text (stripped), else the prompt's name; pages = the authored lines, else the text paged by the session's own public rules
        /// (SplitSpeaker306 / PromptName306 / Pages306, Content.DialoguePageMaxChars). Without the surface: the text Present shows.</summary>
        static string Expected306(WorldMacroPlaytestSession s, string[] lines, string text)
        {
            if (!s.DialogueSurfaceBound) return text;
            var q = Q(s); var giver = Harness303.InteractionPoint(s, GiverId);
            string name = !string.IsNullOrWhiteSpace(q.Speaker) ? q.Speaker.Trim() : giver != null && !string.IsNullOrWhiteSpace(giver.Speaker) ? giver.Speaker.Trim() : null;
            string body = text ?? "";
            if (WorldMacroPlaytestSession.SplitSpeaker306(body, out var prefix, out var stripped)) { body = stripped; if (name == null) name = prefix; }
            if (name == null) name = WorldMacroPlaytestSession.PromptName306(giver?.Prompt);
            int max = s.Content != null ? Mathf.Max(8, s.Content.DialoguePageMaxChars) : 72;
            return string.Join("\n", WorldMacroPlaytestSession.Pages306(lines ?? Array.Empty<string>(), body, name, max));
        }

        static void P4(WorldMacroPlaytestSession s)
        {
            var q = Q(s);
            // #306 surface: the offer ends on [맡는다 / 거절한다 / 떠나기]; the close picks 맡는다 with arrows + F (Talk303.MenuPick),
            // and the waiting pages that answer it in place are a follow-up of this talk, not a second interaction
            if (!TalkStep(s, "P4", talkBegun ? null : Expected306(s, q.OfferLines, q.OfferText), "04_accept_offer", out var row, Oheangbu.App.World.UI.DialogueServiceKind306.CommissionAccept)) return;
            var disk = Harness303.ReadDisk(state.iso.savedPath);
            bool diskKey = disk != null && disk.ledger.completed.Contains(q.Id + ":accepted");
            string detail = "status " + row.status + " focus@F '" + row.focusedAtPress + "' body match " + row.bodyMatches + " memory " + s.CommissionAccepted(q.Id) + " disk " + diskKey + " close " + row.closeMethod +
                (row.menu.Length > 0 ? " menu " + row.menu : "") + (row.followUps > 0 ? " follow-ups " + row.followUps : "");
            if (row.status == "done" && row.focusMatched && row.bodyMatches && s.CommissionAccepted(q.Id) && diskKey) { Pass("C4", detail); state.realInputs.Add("P4 walked to " + GiverId + ", F through the real focus loop (accept)"); EnterPhase("P5"); }
            else FailPhase("C4", detail + " " + row.detail);
        }

        static void P5(WorldMacroPlaytestSession s)
        {
            var q = Q(s);
            if (!TalkStep(s, "P5", talkBegun ? null : Expected306(s, q.WaitingLines, q.WaitingText), "05_waiting", out var row)) return;
            string detail = "status " + row.status + " body match " + row.bodyMatches + " reported " + s.CommissionReported(q.Id) + " currency " + row.currencyBefore + "->" + row.currencyAfter;
            if (row.status == "done" && row.bodyMatches && !s.CommissionReported(q.Id) && row.currencyAfter == row.currencyBefore) { Pass("C5", detail); state.realInputs.Add("P5 F waiting line"); EnterPhase("P6"); }
            else FailPhase("C5", detail + " " + row.detail);
        }

        // ------------------------------------------------------------------ P6 travel
        static Vector3 engage;
        static void P6(WorldMacroPlaytestSession s)
        {
            var feet = s.Walker.Body.transform.position;
            if (sub == 0)
            {
                if (!EngagementPoint(s, actor, feet, out engage, out _, out string why))
                {
                    if (state.travel == "walk") { FailPhase("C6", "walk: no complete NavMesh path to an engagement point (" + why + ")"); return; }
                    if (!StageTeleport(s, why)) return;
                    return;
                }
                StartTravel(s, feet); return;
            }
            if (sub == 1)
            {
                var f = walker.Tick(s, Center);
                VirtualInput303.Hold = f;
                if (!capturedMid && walker.Walked >= walker.PathLength * .5f) { capturedMid = true; Harness303.Capture(RunFolder, "06_travel_mid"); }
                float d = Harness303.Flat(feet, actor.transform.position);
                bool early = d <= 14f && Sight(feet + Vector3.up * s.Walker.EyeHeight, actor.transform.position + Vector3.up * 1.2f, actor.transform) || actor.Current == PrologueEncounter.Behaviour.Chase;
                if (walker.Status == "arrived" || early)
                {
                    if (!capturedMid) { capturedMid = true; Harness303.Capture(RunFolder, "06_travel_mid"); }
                    string how = walker.Status == "arrived" ? "engagement point reached" : "stopped early (" + (actor.Current == PrologueEncounter.Behaviour.Chase ? "창귀 chasing" : "14 m with sight") + ")";
                    string detail = how + " after " + Harness303.F(walker.Walked, "F1") + " m / path " + Harness303.F(walker.PathLength, "F1") + " m, repaths " + walker.Repaths + ", recoveries " + walker.Recoveries;
                    if (state.travelStaged) Set("C6", "CONDITION", "stage fixture used; last leg " + detail);
                    else { Pass("C6", "travelMode walk: " + detail); state.realInputs.Add("P6 walked giver → 창귀 with WASD + mouse look (" + Harness303.F(walker.Walked, "F0") + " m)"); }
                    VirtualInput303.Hold = InputFrame303.Neutral(Center);
                    EnterPhase("P7"); return;
                }
                if (walker.Status == "failed")
                {
                    if (state.travel == "stage" && !state.travelStaged) { sub = 0; StageTeleport(s, walker.Detail + " " + walker.LastBlock); return; }
                    FailPhase("C6", "walk failed: " + walker.Detail + " " + walker.LastBlock + " at " + Harness303.V(feet));
                }
            }
        }

        static void StartTravel(WorldMacroPlaytestSession s, Vector3 feet)
        {
            Walker303.TryPath(feet, engage, out var corners, out _);
            state.pathLength = Walker303.Length(corners);
            state.travelLimit = Mathf.Min(240f, 2.5f * state.pathLength / 4.5f);
            walker.AllowSprint = true;
            if (!walker.Begin(s, engage, 1.0f, Mathf.Max(20f, state.travelLimit), "engage")) { FailPhase("C6", "walker: " + walker.Detail); return; }
            state.notes.Add("P6 engagement " + Harness303.V(engage) + " path " + Harness303.F(state.pathLength, "F1") + " m limit " + Harness303.F(state.travelLimit, "F0") + " s");
            sub = 1; subAt = Now; Persist();
        }

        /// <summary>stage mode only: fixture teleport to a NavMesh point 30 m from the 창귀's home (outside detection) that has a
        /// complete path to an engagement point; the last ~16 m are walked.</summary>
        static bool StageTeleport(WorldMacroPlaytestSession s, string why)
        {
            Vector3 home = actor.transform.position;
            for (int i = 0; i < 24; i++)
            {
                var c = home + Quaternion.Euler(0, i * 15f, 0) * Vector3.forward * 30f;
                if (!NavMesh.SamplePosition(c, out var hit, 4f, NavMesh.AllAreas)) continue;
                if (!EngagementPoint(s, actor, hit.position, out var e, out _, out _)) continue;
                if (!s.TrySafeFeet(hit.position, out var feet)) continue;
                s.Teleport(feet, Harness303.YawTo(feet, home)); Physics.SyncTransforms();
                state.travelStaged = true; engage = e;
                state.fixtures.Add("P6 stage fixture: teleport to " + Harness303.V(feet) + " (30 m from 창귀 home) because: " + why);
                StartTravel(s, feet); return true;
            }
            FailPhase("C6", "stage: no NavMesh point 30 m out with a path to an engagement point (" + why + ")");
            return false;
        }

        // ------------------------------------------------------------------ P8 evidence
        static void P8(WorldMacroPlaytestSession s)
        {
            if (deathAt < 0) deathAt = Now;
            if (letterActive) LetterTick(s);
            if (!captured10 && Now - deathAt >= .5 && !drawing.InDrawMode) { captured10 = true; Harness303.Capture(RunFolder, "10_changgui_death"); }
            if (captured10 && !captured10b) { captured10b = true; try { ExternalCapture(s, actor.transform, "10b_death_external"); } catch (Exception e) { state.findings.Add("10b external capture: " + e.Message); } }
            var p = s.Progress;
            bool memory = p.campaign.EncounterEvidence.Contains(TargetId) && p.defeated.Contains(TargetId) && p.campaign.Facts.Contains("defeated:" + TargetId);
            var disk = Harness303.ReadDisk(state.iso.savedPath);
            bool onDisk = disk != null && disk.campaign.EncounterEvidence.Contains(TargetId) && disk.defeated.Contains(TargetId) && disk.campaign.Facts.Contains("defeated:" + TargetId);
            state.currencyAfterKill = p.ledger.currency;
            bool paid = state.currencyBeforeKill >= 0 && state.currencyAfterKill == state.currencyBeforeKill + state.enemyReward && disk != null && disk.ledger.currency == state.currencyAfterKill;
            if (memory && onDisk && paid && captured10b)
            {
                Pass("C10", "EncounterEvidence/defeated/Facts in memory and on disk, currency " + state.currencyBeforeKill + " → " + state.currencyAfterKill + " (+" + state.enemyReward + ") after " + Harness303.F((float)(Now - deathAt), "F2") + " s");
                EnterPhase("P9"); return;
            }
            if (Now - deathAt > 3) FailPhase("C10", "after 3 s memory " + memory + " disk " + onDisk + " currency " + state.currencyBeforeKill + " → " + state.currencyAfterKill + " expected +" + state.enemyReward);
        }

        // ------------------------------------------------------------------ P9 return, P10 report, P11 completed
        static void P9(WorldMacroPlaytestSession s)
        {
            var giver = Giver(s);
            var front = giver.position + giver.forward * 1.5f;
            if (NavMesh.SamplePosition(front, out var hit, 1.2f, NavMesh.AllAreas)) front = hit.position;
            var feet = s.Walker.Body.transform.position;
            if (sub == 0)
            {
                Walker303.TryPath(feet, front, out var corners, out _);
                float len = Walker303.Length(corners);
                state.travelLimit = Mathf.Min(240f, 2.5f * Mathf.Max(20f, len) / 4.5f);
                if (!walker.Begin(s, front, .5f, Mathf.Max(20f, state.travelLimit), "return")) { FailPhase("C11", "return walk: " + walker.Detail); return; }
                sub = 1; return;
            }
            VirtualInput303.Hold = walker.Tick(s, Center);
            if (!capturedReturn && Harness303.Flat(feet, giver.position) < 6f) { capturedReturn = true; Harness303.Capture(RunFolder, "11_return_giver"); }
            if (walker.Status == "arrived") { if (!capturedReturn) { capturedReturn = true; Harness303.Capture(RunFolder, "11_return_giver"); } state.realInputs.Add("P9 walked back to the giver (" + Harness303.F(walker.Walked, "F0") + " m)"); EnterPhase("P10"); return; }
            if (walker.Status == "failed") FailPhase("C11", "return walk failed: " + walker.Detail + " " + walker.LastBlock);
        }

        static string Receipt303(WorldMacroPlaytestSession s, Oheangbu.Data.World.WorldMacroPlaytestSO.CommissionSpec q) => q.Reward > 0 && !s.IconPresentation ? "조선통보 +" + q.Reward : "";

        static void P10(WorldMacroPlaytestSession s)
        {
            var q = Q(s);
            // the receipt: without the surface the last line of the page; on the #306 surface never a page but a notice raised when
            // the conversation closes (Raise306 / DialogueClosed306), so it is read from LastFeedback after the close
            string expected = null, receipt;
            if (!talkBegun)
            {
                state.currencyBeforeReport = s.Progress.ledger.currency; receipt = Receipt303(s, q);
                expected = s.DialogueSurfaceBound ? Expected306(s, q.ReportLines, q.ReportText) : q.ReportText + (receipt.Length > 0 ? "\n" + receipt : "");
            }
            if (!TalkStep(s, "P10", expected, "12_report_reward", out var row)) return;
            receipt = Receipt303(s, q);
            state.currencyAfterReport = s.Progress.ledger.currency;
            var disk = Harness303.ReadDisk(state.iso.savedPath);
            bool diskOk = disk != null && disk.ledger.completed.Contains(q.Id + ":reported") && disk.ledger.currency == state.currencyAfterReport;
            bool receiptOk = !s.DialogueSurfaceBound || receipt.Length == 0 || s.LastFeedback == receipt;
            string detail = "body match " + row.bodyMatches + " reported " + s.CommissionReported(q.Id) + " currency " + state.currencyBeforeReport + " → " + state.currencyAfterReport + " (+" + q.Reward + " expected) disk " + diskOk +
                (s.DialogueSurfaceBound && receipt.Length > 0 ? " receipt notice '" + s.LastFeedback + "' ok " + receiptOk : "");
            if (row.status == "done" && row.bodyMatches && receiptOk && s.CommissionReported(q.Id) && state.currencyAfterReport == state.currencyBeforeReport + q.Reward && diskOk) { Pass("C11", detail); state.realInputs.Add("P10 F report"); EnterPhase("P11"); }
            else FailPhase("C11", detail + " " + row.detail);
        }

        static void P11(WorldMacroPlaytestSession s)
        {
            var q = Q(s);
            if (!TalkStep(s, "P11", talkBegun ? null : Expected306(s, q.CompletedLines, q.CompletedText), "13_completed", out var row)) return;
            string detail = "body match " + row.bodyMatches + " currency " + row.currencyBefore + " → " + row.currencyAfter;
            if (row.status == "done" && row.bodyMatches && row.currencyAfter == row.currencyBefore) { Pass("C12", detail); state.realInputs.Add("P11 F completed line"); EnterPhase("P12"); }
            else FailPhase("C12", detail + " " + row.detail);
        }

        // ------------------------------------------------------------------ P12 save, P13 restart
        static void P12(WorldMacroPlaytestSession s)
        {
            if (!s.SaveNow(out string error)) { FailPhase("C14", "SaveNow failed: " + error); return; }
            File.Copy(state.iso.savedPath, Path.Combine(RunFolder, "save-final.json"), true);
            var disk = Harness303.ReadDisk(state.iso.savedPath);
            var q = Q(s);
            state.expectedCompleted = new List<string> { q.Id + ":accepted", q.Id + ":reported" };
            state.expectedCurrency = s.Progress.ledger.currency; state.expectedEvidence = true;
            bool keys = disk != null && state.expectedCompleted.All(k => disk.ledger.completed.Contains(k)) && disk.campaign.EncounterEvidence.Contains(TargetId) && disk.ledger.currency == state.expectedCurrency;
            if (!keys) { FailPhase("C14", "save-final.json keys/currency mismatch"); return; }
            state.notes.Add("P12 saved; restart next (same isolated suffix)");
            FlushLogs();
            Stop(true);
        }

        static void P13(WorldMacroPlaytestSession s)
        {
            if (sub == 0)
            {
                var p = s.Progress;
                bool ok = s.LoadStatus == "primary" && state.expectedCompleted.All(k => p.ledger.completed.Contains(k)) && p.campaign.EncounterEvidence.Contains(TargetId) && p.ledger.currency == state.expectedCurrency;
                state.notes.Add("P13 boot " + state.boots + ": LoadStatus " + s.LoadStatus + " currency " + p.ledger.currency + " 창귀 " + (vitals != null && vitals.IsAlive ? "alive" : "defeated") + " defeated-listed " + p.defeated.Contains(TargetId));
                if (!ok) { FailPhase("C14", "restart: LoadStatus " + s.LoadStatus + " keys/evidence/currency not kept (" + p.ledger.currency + " vs " + state.expectedCurrency + ")"); return; }
                sub = 1; return;
            }
            if (sub == 1)
            {
                var q = Q(s);
                if (!TalkStep(s, "P13", talkBegun ? null : Expected306(s, q.CompletedLines, q.CompletedText), "14_restart_completed", out var row)) return;
                File.Copy(state.iso.savedPath, Path.Combine(RunFolder, "save-after-restart.json"), true);
                string detail = "LoadStatus primary, keys/evidence/currency kept; completed line after restart match " + row.bodyMatches + " currency " + row.currencyBefore + " → " + row.currencyAfter;
                if (row.status == "done" && row.bodyMatches && row.currencyAfter == state.expectedCurrency) { Pass("C14", detail); state.realInputs.Add("P13 F after restart"); }
                else { FailC("C14", detail + " " + row.detail); }
                state.finished = true; FlushLogs(); Stop(false);
            }
        }

        // ------------------------------------------------------------------ external proof camera (298 Capture method)
        static void ExternalCapture(WorldMacroPlaytestSession s, Transform subject, string name)
        {
            var source = s.Walker.ViewCamera != null ? s.Walker.ViewCamera : Camera.main;
            var go = new GameObject("Commission303 proof camera") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(go, s.gameObject.scene);
            var camera = go.AddComponent<Camera>(); camera.CopyFrom(source); camera.enabled = false; camera.aspect = 16f / 9f;
            var data = source.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().FullName == "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
            if (data != null) { var copy = go.AddComponent(data.GetType()); EditorUtility.CopySerialized(data, copy); if (data.GetType().GetProperty("cameraStack")?.GetValue(copy) is System.Collections.IList stack) stack.Clear(); }
            var target = subject.position + Vector3.up * .4f;
            var eye = target + subject.forward * 5f + subject.right * 3f + Vector3.up * 1.4f;
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            var arts = Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None); var observers = arts.Select(a => a.Observer).ToArray();
            var dress = Object.FindObjectsByType<Oheangbu.App.World.Dressing.WorldMacroDressingRenderer>(FindObjectsSortMode.None); var dressObservers = dress.Select(d => d.Observer).ToArray();
            var rt = new RenderTexture(1280, 720, 24); var old = RenderTexture.active; Texture2D texture = null;
            try
            {
                foreach (var a in arts) a.Observer = camera; foreach (var d in dress) d.Observer = camera;
                rt.Create(); camera.targetTexture = rt; camera.Render(); camera.Render();
                RenderTexture.active = rt; texture = new Texture2D(1280, 720, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
                Directory.CreateDirectory(RunFolder); File.WriteAllBytes(Path.Combine(RunFolder, name + ".png"), texture.EncodeToPNG());
            }
            finally
            {
                for (int i = 0; i < arts.Length; i++) arts[i].Observer = observers[i]; for (int i = 0; i < dress.Length; i++) dress[i].Observer = dressObservers[i];
                RenderTexture.active = old; camera.targetTexture = null; Object.DestroyImmediate(go); rt.Release(); Object.DestroyImmediate(rt); if (texture != null) Object.DestroyImmediate(texture);
            }
        }
    }
}
