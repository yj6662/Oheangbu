using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #303 follow-up (CHANGGUI_COMBAT_TEST_PLAN.md): the 창귀 commission played end to end on an isolated save with real
    // input — accept, walk, fight with Q strokes / Tab / Shift / LMB, walk back, report, restart. Queue entry Run(string):
    //   probe[:main|folklore298]              — Edit (or Play): scene, session, 창귀, commission, rest, book rows, NavMesh path → probe.json
    //   stroke-lab[:마|나|가]                  — Edit: registered templates through the real RecognitionPipeline → stroke-lab.json, stroke-recipe.json
    //   start[:main|folklore298][:walk|stage] — Edit, clean scene: isolated Play (_c303fight_<UTC>) runs P0..P14, returns the runId
    //   status | report | abort               — one line / report path / safe stop + cleanup
    // Queue paths never open a dialog; refusals come back as "refused: …". The source of the Commission303Combat*,
    // Input303, UiAdapter303 and Harness303 files must not contain the C8 forbidden tokens (checked by the report).
    [InitializeOnLoad]
    public static partial class Commission303Combat
    {
        const string StateKey = "Commission303Combat.State";
        public const string CommissionId = "commission303_voice", GiverId = "woodbrother303", TargetId = "folklore298/changgui", RestId = "geumpyo_inn";
        const string GiverPath = "Roadside303/Givers/woodbrother303";
        public const string Disclaimer = "자동 입력 검증이며 사람의 조작감·미술 판단이 아니다 (automated real-input validation; not human feel, art or narrative judgement).";
        internal static string Folder => Path.Combine(Harness303.Roadside303Folder, "CombatTest");
        static string RunFolder => Path.Combine(Folder, state != null ? state.runId : "none");
        static string RecipePath => Path.Combine(Folder, "stroke-recipe.json");

        [Serializable] sealed class Criterion { public string id, title, status = "PENDING", detail = ""; }
        [Serializable] sealed class State
        {
            public bool active, finished, restartRequested, interrupted, focusReady, uiTidied, inputStarted;
            public string runId = "", status = "IDLE", target = "main", travel = "walk", plan = "A", phase = "", phaseNote = "", finalStatus = "", interruptReason = "";
            public int boots, lastFrame = -1;
            public double began, phaseAt, enteredAt, focusLossAt = -1;
            public Isolation303 iso = new Isolation303();
            public StrokeRecipe303 recipe = new StrokeRecipe303();
            public int c0 = -1, currencyBeforeKill = -1, currencyAfterKill = -1, currencyBeforeReport = -1, currencyAfterReport = -1, enemyReward = -1;
            public bool restDone, restSkipped, travelStaged, rehearsalPass, planBUsed;
            public float measuredFps, predictedBrush, rehearsalBrushMin = -1, pathLength, travelLimit, enemyMaxHp = 60;
            public int screenW, screenH; public bool screenLabVerified;
            public string qualityLevel = "", unityVersion = "", sceneSha = "", inputSummary = "", inputCleanup = "", perUnitNote = "";
            public List<Criterion> criteria = new List<Criterion>();
            public List<string> findings = new List<string>(), fixtures = new List<string>(), realInputs = new List<string>(), errors = new List<string>(), notes = new List<string>();
            public List<Check303> cleanup = new List<Check303>();
            public int logErrors;
            public List<string> expectedCompleted = new List<string>(); public int expectedCurrency = -1; public bool expectedEvidence;
        }

        static State state;

        static Commission303Combat()
        {
            var json = SessionState.GetString(StateKey, "");
            if (json.Length > 0) { try { state = JsonUtility.FromJson<State>(json); } catch { state = null; } }
            if (state?.active == true) LoadLogs();
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Changed;
            Application.logMessageReceived += Logged;
        }

        public static string Run(string command)
        {
            string[] a = (command ?? "").Split(':');
            string head = a[0];
            switch (head)
            {
                case "probe": return Probe(a.Length > 1 ? a[1] : "main");
                case "stroke-lab": return StrokeLab(a.Length > 1 ? a[1] : "마");
                case "start": return StartRun(a.Length > 1 ? a[1] : "main", a.Length > 2 ? a[2] : "walk");
                case "status": return Status();
                case "report": return state == null ? "none" : Path.Combine(RunFolder, "report.json");
                case "abort": return Abort();
                default: return "refused: Commission303Combat probe[:main|folklore298] | stroke-lab[:마|나|가] | start[:main|folklore298][:walk|stage] | status | report | abort";
            }
        }

        static string Status()
        {
            if (state == null) return "{\"status\":\"IDLE\"}";
            var c = state.criteria;
            return "{\"runId\":\"" + state.runId + "\",\"status\":\"" + state.status + "\",\"phase\":\"" + state.phase + "\",\"note\":\"" + Esc(state.phaseNote) +
                   "\",\"active\":" + (state.active ? "true" : "false") + ",\"final\":\"" + state.finalStatus + "\",\"pass\":" + c.Count(x => x.status == "PASS") +
                   ",\"fail\":" + c.Count(x => x.status == "FAIL") + ",\"conditions\":" + c.Count(x => x.status == "CONDITION") + ",\"boots\":" + state.boots +
                   ",\"folder\":\"" + Esc(RunFolder) + "\"}";
        }
        static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");

        static void Persist()
        {
            if (state == null) return;
            SessionState.SetString(StateKey, JsonUtility.ToJson(state));
        }

        // ------------------------------------------------------------------ start / lifecycle
        static readonly (string id, string title)[] CriteriaTable =
        {
            ("C1", "isolated store path and fresh load"), ("C2", "pre-state: 창귀 alive at full HP, no record, no commission keys"),
            ("C3", "G1 (lab) and G2 (rehearsal) gates"), ("C4", "accept through the real focus loop"), ("C5", "waiting line"),
            ("C6", "travel by real input"), ("C7", "Tab lock-on target is 창귀"), ("C8", "real-fight kill provenance"),
            ("C9", "player survives"), ("C10", "defeat evidence in memory and on disk"), ("C11", "report pays exactly +reward"),
            ("C12", "completed line, no double reward"), ("C13", "no Exception/Error/Assert logs during Play"), ("C14", "restart keeps everything"),
            ("C15", "cleanup: saves, scene, content, protected files, editor state"), ("C16", "artifacts present"),
        };

        static string StartRun(string target, string travel)
        {
            if (state?.active == true) return "refused: run " + state.runId + " is still active (" + state.status + "); status or abort first";
            if (SessionState.GetString("Escort303Regression.State", "").Contains("\"active\":true")) return "refused: an Escort303Regression run is active";
            if (!Harness303.TryTarget(target, out string scenePath, out string slot)) return "refused: target must be main or folklore298";
            if (travel != "walk" && travel != "stage") return "refused: travel must be walk or stage";
            if (!File.Exists(RecipePath)) return "refused: run stroke-lab first (no " + RecipePath + ")";
            StrokeRecipe303 recipe;
            try { recipe = JsonUtility.FromJson<StrokeRecipe303>(File.ReadAllText(RecipePath)); } catch (Exception e) { return "refused: unreadable recipe: " + e.Message; }
            if (recipe == null || string.IsNullOrEmpty(recipe.letter) || recipe.gate == "FAIL") return "refused: the stroke recipe did not pass its gate (" + (recipe?.gate ?? "none") + ")";
            var iso = new Isolation303();
            string refusal = Harness303.Prepare(iso, scenePath, slot, "_c303fight_");
            if (refusal != null) return refusal;
            var s = Harness303.Session;
            if (!s.Actors.Any(x => x != null && x.Id == TargetId)) return "refused: no actor " + TargetId + " in " + scenePath;
            if (s.Content.Commissions == null || !s.Content.Commissions.Any(q => q != null && q.Id == CommissionId)) return "refused: no commission " + CommissionId + " in the scene content";
            if (Harness303.Find(SceneManager.GetActiveScene(), GiverPath) == null) return "refused: no giver object " + GiverPath;
            state = new State
            {
                active = true, runId = Harness303.UtcStamp(), status = "STARTING", target = target, travel = travel, iso = iso, recipe = recipe, plan = recipe.plan,
                began = EditorApplication.timeSinceStartup, unityVersion = Application.unityVersion, sceneSha = iso.sceneSha,
                qualityLevel = QualitySettings.GetQualityLevel() + ":" + QualitySettings.names[QualitySettings.GetQualityLevel()],
            };
            foreach (var (id, title) in CriteriaTable) state.criteria.Add(new Criterion { id = id, title = title });
            ResetLogs();
            Directory.CreateDirectory(RunFolder);
            iso.runFolder = RunFolder;
            Set("C3", recipe.gate == "PASS" || recipe.gate == "PLAN_B" ? "PENDING" : "FAIL", "G1 lab gate " + recipe.gate + " (plan " + recipe.plan + ", 30 fps p10 brush " + Harness303.F(recipe.brush30p10) + ")");
            Harness303.Apply(iso, s);
            state.fixtures.Add("isolated save " + iso.suffix + ": session.TestSaveSuffix set (scene not saved), SessionState PlaytestUiReviewSuffix, direct Play (two lines of CompactLoadingStartup270.UseCurrent)");
            Harness303.FocusGameView();
            state.status = "ENTERING"; state.phaseAt = EditorApplication.timeSinceStartup;
            Persist(); WriteState();
            EditorApplication.EnterPlaymode();
            return "started " + state.runId + " target=" + target + " travel=" + travel + " suffix=" + iso.suffix + " folder=" + RunFolder;
        }

        static void Set(string id, string status, string detail)
        {
            var c = state.criteria.FirstOrDefault(x => x.id == id);
            if (c == null) return;
            // a FAIL is sticky; a later CONDITION never upgrades it
            if (c.status == "FAIL" && status != "FAIL") { c.detail += " | " + detail; return; }
            c.status = status; c.detail = detail;
        }
        static void Pass(string id, string detail) => Set(id, "PASS", detail);
        static void FailC(string id, string detail) => Set(id, "FAIL", detail);
        static bool Failed(string id) => state.criteria.Any(x => x.id == id && x.status == "FAIL");

        static void Tick()
        {
            if (state?.active != true || EditorApplication.isCompiling) return;
            double now = EditorApplication.timeSinceStartup;
            if (state.status == "RESTART_PENDING")
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isUpdating || now - state.phaseAt < .75) return;
                var s = Harness303.Session;
                if (s == null) { Abandon("restart: session missing in Edit mode"); return; }
                Harness303.Apply(state.iso, s);   // the Play-mode scene copy is gone; point the edit instance at the same isolated store again
                Harness303.FocusGameView();
                state.status = "ENTERING"; state.phaseAt = now; Persist();
                EditorApplication.EnterPlaymode();
                return;
            }
            if (state.status == "CLEANUP_PENDING" && !EditorApplication.isPlayingOrWillChangePlaymode) { CleanupAndReport(); return; }
            if (!EditorApplication.isPlaying || state.status != "RUNNING") return;
            if (now - state.began > 900) { Fail("overall deadline 900 s exceeded in phase " + state.phase); Stop(false); return; }
            ObserveFocus(now);
        }

        static void ObserveFocus(double now)
        {
            bool focused = Harness303.Focused;
            if (!state.focusReady)
            {
                if (focused) { state.focusReady = true; state.notes.Add("focus acquired " + Harness303.F((float)(now - state.enteredAt), "F1") + " s after Play boot " + state.boots); Persist(); return; }
                if (now - state.enteredAt < 10) return;
                state.interrupted = true; state.interruptReason = "Game view focus not acquired within 10 s of boot " + state.boots;
                Fail(state.interruptReason); Stop(false); return;
            }
            if (!focused)
            {
                if (state.focusLossAt < 0) { state.focusLossAt = now; state.notes.Add("focus lost in " + state.phase + " (" + (EditorWindow.focusedWindow != null ? EditorWindow.focusedWindow.GetType().Name : "none") + ")"); Persist(); }
                if (now - state.focusLossAt >= 2) { state.interrupted = true; state.interruptReason = "focus lost for 2 s in " + state.phase; Fail(state.interruptReason); Stop(false); }
                return;
            }
            if (state.focusLossAt >= 0) { state.notes.Add("focus returned after " + Harness303.F((float)(now - state.focusLossAt), "F2") + " s (run continues; brief loss < 2 s)"); state.focusLossAt = -1; }
        }

        static void Changed(PlayModeStateChange mode)
        {
            if (state?.active != true) return;
            if (mode == PlayModeStateChange.EnteredPlayMode)
            {
                state.boots++; state.status = "RUNNING"; state.enteredAt = EditorApplication.timeSinceStartup; state.focusReady = false; state.focusLossAt = -1;
                state.uiTidied = false; state.inputStarted = false; state.lastFrame = -1;
                if (state.boots == 1) EnterPhase("P0"); else EnterPhase("P13");
                RuntimeReset();
                var probe = new GameObject("Commission303 probe") { hideFlags = HideFlags.HideAndDontSave };
                probe.AddComponent<Probe303>();
                probeObject = probe;
                Probe303.Late = Late;
                Harness303.FocusGameView();
                Persist();
                return;
            }
            if (mode == PlayModeStateChange.ExitingPlayMode) { Unbind(); EndInput(); if (Probe303.Late == (Action)Late) Probe303.Late = null; if (probeObject != null) Object.DestroyImmediate(probeObject); probeObject = null; return; }
            if (mode != PlayModeStateChange.EnteredEditMode) return;
            if (state.restartRequested && !state.interrupted)
            {
                state.restartRequested = false; state.status = "RESTART_PENDING"; state.phaseAt = EditorApplication.timeSinceStartup; Persist();
                return;
            }
            state.status = "CLEANUP_PENDING"; Persist();
            CleanupAndReport();
        }

        static GameObject probeObject;

        /// <summary>Stop Play (optionally to restart with the same isolated save).</summary>
        static void Stop(bool restart)
        {
            Unbind(); EndInput();
            if (Probe303.Late == (Action)Late) Probe303.Late = null;
            // Stop can run inside the probe's own LateUpdate: destroy deferred while playing
            if (probeObject != null) { if (EditorApplication.isPlaying) Object.Destroy(probeObject); else Object.DestroyImmediate(probeObject); }
            probeObject = null;
            state.restartRequested = restart;
            state.status = restart ? "RESTARTING" : "STOPPING";
            FlushLogs(); Persist();
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            else { state.status = "CLEANUP_PENDING"; Persist(); }
        }

        static string Abort()
        {
            if (state?.active != true) return "no active run";
            Fail("explicit abort in " + state.phase);
            if (EditorApplication.isPlaying) { Stop(false); return "aborting " + state.runId + ": Play stops, then cleanup"; }
            CleanupAndReport();
            return "aborted " + state.runId + " (cleanup done in Edit mode)";
        }

        static void Abandon(string why) { Fail(why); CleanupAndReport(); }

        static void Fail(string why)
        {
            if (state == null) return;
            state.errors.Add("[" + state.phase + "] " + why);
            state.phaseNote = why;
        }

        static void EndInput()
        {
            if (!VirtualInput303.Active) return;
            state.inputCleanup = VirtualInput303.End(out bool ok);
            if (!ok) state.findings.Add("virtual input revert: " + state.inputCleanup);
        }

        static void Logged(string message, string stack, LogType type)
        {
            if (state?.active != true || !EditorApplication.isPlaying || state.status != "RUNNING") return;
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            var s = Harness303.Session;
            if (s == null || !s.InitializationComplete) return;
            state.logErrors++;
            if (state.errors.Count < 40) state.errors.Add("[log " + type + " in " + state.phase + "] " + message);
        }

        static void CleanupAndReport()
        {
            if (state == null) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { state.status = "CLEANUP_PENDING"; Persist(); return; }
            state.status = "CLEANUP";
            FlushLogs();
            state.cleanup = Harness303.Cleanup(state.iso, RunFolder);
            bool clean = state.cleanup.All(c => c.status != "FAIL");
            Set("C15", clean ? "PASS" : "FAIL", string.Join("; ", state.cleanup.Select(c => c.status + " " + c.id + ": " + c.detail)));
            if (!string.IsNullOrEmpty(state.inputCleanup) && state.inputCleanup.Contains("FINDINGS")) state.findings.Add(state.inputCleanup);
            Set("C13", state.logErrors == 0 ? "PASS" : "FAIL", state.logErrors + " Exception/Error/Assert log(s) during Play after initialization");
            ArtifactCheck();
            Grade();
            state.active = false; state.status = "DONE";
            WriteReport(); Persist();
            EditorApplication.delayCall += () => Harness303.FocusPrior(state?.iso);
        }

        static readonly string[] RequiredArtifacts =
        {
            "timeline.json", "commits.json", "damage.json", "player_hits.json", "interactions.json", "save-final.json", "save-after-restart.json",
            "01_spawn_inn.png", "02_rehearsal_commit.png", "04_accept_offer.png", "05_waiting.png", "06_travel_mid.png", "07_engage_lockon.png",
            "10_changgui_death.png", "10b_death_external.png", "11_return_giver.png", "12_report_reward.png", "13_completed.png", "14_restart_completed.png",
        };

        static void ArtifactCheck()
        {
            var missing = RequiredArtifacts.Where(f => !File.Exists(Path.Combine(RunFolder, f))).ToList();
            if (!state.restSkipped && !File.Exists(Path.Combine(RunFolder, "03_rest_done.png"))) missing.Add("03_rest_done.png");
            if (!File.Exists(Path.Combine(RunFolder, "08_first_impact.png")) && !File.Exists(Path.Combine(RunFolder, "09_first_damage.png"))) missing.Add("08/09 first impact/damage");
            Set("C16", missing.Count == 0 ? "PASS" : "FAIL", missing.Count == 0 ? "all artifacts present" : "missing: " + string.Join(", ", missing));
        }

        static void Grade()
        {
            foreach (var c in state.criteria.Where(c => c.status == "PENDING")) { c.status = "FAIL"; c.detail = "not reached" + (string.IsNullOrEmpty(c.detail) ? "" : " (" + c.detail + ")"); }
            var conditions = new List<string>();
            if (state.travelStaged) conditions.Add("C6: travel used the stage fixture (teleport 30 m out, last ~16 m walked)");
            if (state.planBUsed || state.plan == "B") conditions.Add("C3: plan B glyph (" + state.recipe.letter + ") with harvest top-up; plan A (마 ≥ 0.70) not met");
            if (state.restSkipped) conditions.Add("P3: REST_SKIPPED — fought with the ink left after the rehearsal, harvest allowed");
            if (state.interrupted) state.finalStatus = "INTERRUPTED_FOCUS";
            else if (state.criteria.Any(c => c.status == "FAIL")) state.finalStatus = "FAIL";
            else if (conditions.Count > 0 || state.criteria.Any(c => c.status == "CONDITION")) state.finalStatus = "PASS WITH CONDITIONS";
            else state.finalStatus = "PASS (automated real input)";
            state.notes.AddRange(conditions.Select(x => "condition: " + x));
        }

        [Serializable] sealed class Report
        {
            public string runId, finalStatus, disclaimer, target, travel, plan, letter, scene, sceneSha, suffix, savedPath, unityVersion, qualityLevel, inputUpdateMode, inputSummary, inputCleanup;
            public int screenW, screenH, boots, logErrors; public bool screenLabVerified; public float measuredFps, predictedBrush, rehearsalBrushMin, pathLength, degreesPerLookUnit, observedDegreesPerLookUnit;
            public List<Criterion> criteria; public List<string> realInputs, fixtures, findings, errors, notes, uiAdapter; public List<Check303> cleanup;
            public string scope;
        }

        static void WriteReport()
        {
            var r = new Report
            {
                runId = state.runId, finalStatus = state.finalStatus, disclaimer = Disclaimer, target = state.target, travel = state.travel, plan = state.plan, letter = state.recipe.letter,
                scene = state.iso.scenePath, sceneSha = state.sceneSha, suffix = state.iso.suffix, savedPath = state.iso.savedPath, unityVersion = state.unityVersion, qualityLevel = state.qualityLevel,
                inputUpdateMode = VirtualInput303.UpdateMode, inputSummary = state.inputSummary, inputCleanup = state.inputCleanup,
                screenW = state.screenW, screenH = state.screenH, boots = state.boots, logErrors = state.logErrors, screenLabVerified = state.screenLabVerified,
                measuredFps = state.measuredFps, predictedBrush = state.predictedBrush, rehearsalBrushMin = state.rehearsalBrushMin, pathLength = state.pathLength,
                degreesPerLookUnit = Steer303.PerUnit, observedDegreesPerLookUnit = Steer303.ObservedPerUnit,
                criteria = state.criteria, realInputs = state.realInputs, fixtures = state.fixtures, findings = state.findings, errors = state.errors, notes = state.notes,
                uiAdapter = UiAdapter303.Log.ToList(), cleanup = state.cleanup,
                scope = "Real input = virtual keyboard/mouse devices through the existing Input System actions and Keyboard.current reads (WASD, mouse look, Q + LMB strokes replayed from registered templates, Tab, Shift+A/D, LMB harvest, F). Recognition, casting, damage, death records and rewards run unmodified. Fixture interventions are listed in `fixtures`. No human hand, feel, art or performance judgement is implied.",
            };
            Harness303.WriteJson(RunFolder, "report.json", r);
        }

        static void WriteState() { try { Harness303.WriteJson(RunFolder, "state.json", state); } catch { } }

        // ------------------------------------------------------------------ probe
        [Serializable] sealed class ProbeReport
        {
            public string scene, mode, slot, utc; public bool sessionOk, playing;
            public string changgui, encounterSpec, attack, config, commission, rest, giver, book, drawing, wiring, economy;
            public float giverToEngageLength, innToGiverLength; public string giverToEngageStatus = "", innToGiverStatus = "";
            public int corners; public float maxSlopeDeg, nearestOtherEnemy; public string nearestOtherEnemyId = "";
            public Vector3 engagePoint, changguiHome, giverFront; public float engageToHome; public bool engageSight;
            public List<string> otherEnemies = new List<string>(), notes = new List<string>();
        }

        static string Probe(string target)
        {
            if (!Harness303.TryTarget(target, out string scenePath, out string slot)) return "refused: target must be main or folklore298";
            var scene = SceneManager.GetActiveScene();
            if (scene.path != scenePath) return "refused: open " + scenePath + " first (active " + scene.path + ")";
            var s = Harness303.Session;
            var r = new ProbeReport { scene = scene.path, mode = EditorApplication.isPlaying ? "Play" : "Edit", playing = EditorApplication.isPlaying, utc = DateTime.UtcNow.ToString("O"), sessionOk = s != null && s.Content != null };
            if (!r.sessionOk) return "refused: no single playtest session in " + scene.path;
            r.slot = s.Content.SaveSlot;
            if (s.Content.SaveSlot != slot) r.notes.Add("slot " + s.Content.SaveSlot + " differs from expected " + slot);
            var actor = s.Actors.FirstOrDefault(x => x != null && x.Id == TargetId);
            var spec = s.Content.Encounters.FirstOrDefault(e => e.Id == TargetId);
            Vector3 home = actor != null ? actor.transform.position : Vector3.zero;
            if (actor != null)
            {
                var v = actor.GetComponent<EnemyVitals>(); var ec = actor.GetComponent<EnemyController>(); var nav = actor.GetComponent<NavMeshAgent>();
                r.changguiHome = home;
                r.changgui = "pos " + Harness303.V(home) + " maxHp " + Harness303.F(v != null ? v.MaxHp : -1, "F1") + (EditorApplication.isPlaying ? " hp " + Harness303.F(v != null ? v.Hp : -1, "F1") + " alive " + (v != null && v.IsAlive) + " behaviour " + actor.Current : " (hp read in Play only)") +
                             " detection " + actor.DetectionRange + " leash " + actor.Leash + " speed " + actor.Speed + " preferred " + actor.PreferredDistance + " navRadius " + (nav != null ? nav.radius : -1);
                if (ec != null) r.attack = "range " + Harness303.F(ec.AttackRange, "F2") + " telegraph " + Harness303.F(ec.TelegraphDuration, "F2") + " recovery " + Harness303.F(ec.RecoveryDuration, "F2");
            }
            else r.notes.Add("no actor " + TargetId);
            if (spec != null) r.encounterSpec = "feet " + Harness303.V(spec.Feet) + " detection " + spec.Detection + " leash " + spec.Leash + " activation " + spec.Activation + " respawnOnRest " + spec.RespawnOnRest;
            var cfg = Harness303.Field<CombatConfigSO>(s.Walker.Wiring, "_config");
            if (cfg != null) r.config = "playerHp " + cfg.PlayerMaxHp + " move " + cfg.MoveSpeed + " enemyHp " + cfg.EnemyMaxHp + " spellInk " + cfg.SpellInkCost + " misfireInk " + cfg.MisfireInkCost +
                                        " harvest " + cfg.HarvestRange + "m/" + cfg.HarvestInkPerSecond + "ink/s/" + cfg.HarvestDamagePerSecond + "dps projectile " + cfg.SpellProjectileSpeed + " lockOn " + cfg.LockOnRange +
                                        " dodge " + cfg.DodgeDistance + "m/" + cfg.DodgeDuration + "s cd " + cfg.DodgeCooldown + " look " + cfg.LookSensitivity;
            var q = s.Content.Commissions?.FirstOrDefault(x => x != null && x.Id == CommissionId);
            if (q != null) r.commission = "giver " + q.GiverId + " target " + q.TargetEncounterId + " reward " + q.Reward + " offer " + (q.OfferText?.Length ?? 0) + "ch waiting " + (q.WaitingText?.Length ?? 0) + "ch report " + (q.ReportText?.Length ?? 0) + "ch completed " + (q.CompletedText?.Length ?? 0) + "ch icon " + s.IconPresentation;
            var rest = s.Content.Points.FirstOrDefault(p => p.Id == RestId);
            if (rest != null) r.rest = RestId + " kind " + rest.Kind + " at " + Harness303.V(rest.Position) + " radius " + rest.Radius;
            var giverPoint = s.Content.Points.FirstOrDefault(p => p.Id == GiverId);
            var giver = Harness303.Find(scene, GiverPath);
            Vector3 front = giver != null ? giver.position + giver.forward * 1.5f : giverPoint != null ? giverPoint.Position : Vector3.zero;
            r.giverFront = front;
            if (giverPoint != null) r.giver = GiverId + " point " + Harness303.V(giverPoint.Position) + " radius " + giverPoint.Radius + " object " + (giver != null ? Harness303.V(giver.position) + " yaw " + Harness303.F(giver.eulerAngles.y, "F0") : "missing");
            var book = SpellBook(s);
            if (book != null)
            {
                string Row(char c) => book.TryGet(c, out var e) ? c + " " + e.Kind + "/" + e.Element + " base " + e.BasePower + " speed " + e.ProjectileSpeedMul + " area " + e.AreaShape : c + " missing";
                r.book = AssetDatabase.GetAssetPath(book) + " | " + Row('마') + " | " + Row('나') + " | " + Row('가') + " | brush(0.6,1.0)=" + Harness303.F(book.EvaluateBrushPower(.6f, 1f)) + " brush(1.0,1.3)=" + Harness303.F(book.EvaluateBrushPower(1f, 1.3f));
            }
            var drawing = s.Walker.Drawing;
            r.drawing = "templates " + AssetDatabase.GetAssetPath(Harness303.Field<JamoTemplateLibrarySO>(drawing, "_templates")) + " drawTimeScale " + Harness303.FieldValue(drawing, "_drawTimeScale") +
                        " minSamplePx " + Harness303.FieldValue(drawing, "_minSamplePixelDistance") + " minCommitPoints " + Harness303.FieldValue(drawing, "_minCommitPointCount");
            r.wiring = "occlusion " + Harness303.FieldValue(s.Walker.Wiring, "_environmentOcclusion") + " freeAim " + Harness303.FieldValue(s.Walker.Wiring, "_freeAimAngle") + "°/" + Harness303.FieldValue(s.Walker.Wiring, "_freeAimRange") + "m";
            r.economy = "EnemyReward " + (s.Content.TestRules != null ? s.Content.TestRules.EnemyReward : -1);
            // giver front -> engagement point, inn -> giver
            string why = "no 창귀 actor";
            if (actor != null && EngagementPoint(s, actor, front, out var engage, out bool sight, out why))
            {
                r.engagePoint = engage; r.engageToHome = Harness303.Flat(engage, home); r.engageSight = sight;
                bool ok = Walker303.TryPath(front, engage, out var corners, out var reason);
                r.giverToEngageStatus = ok ? "PathComplete" : reason; r.giverToEngageLength = Walker303.Length(corners); r.corners = corners.Length;
                for (int i = 1; i < corners.Length; i++) { float flat = Harness303.Flat(corners[i - 1], corners[i]); if (flat > .3f) r.maxSlopeDeg = Mathf.Max(r.maxSlopeDeg, Mathf.Atan2(Mathf.Abs(corners[i].y - corners[i - 1].y), flat) * Mathf.Rad2Deg); }
                r.nearestOtherEnemy = float.MaxValue;
                foreach (var other in s.Actors.Where(x => x != null && x.Id != TargetId))
                {
                    float d = corners.Length == 0 ? float.MaxValue : corners.Min(c => Vector3.Distance(c, other.transform.position));
                    for (int i = 1; i < corners.Length; i++) d = Mathf.Min(d, SegmentDistance(other.transform.position, corners[i - 1], corners[i]));
                    if (d < 60) r.otherEnemies.Add(other.Id + " " + Harness303.F(d, "F1") + " m");
                    if (d < r.nearestOtherEnemy) { r.nearestOtherEnemy = d; r.nearestOtherEnemyId = other.Id; }
                }
            }
            else r.giverToEngageStatus = "no engagement point: " + why;
            if (rest != null) { bool ok = Walker303.TryPath(rest.Position, front, out var c2, out var reason2); r.innToGiverStatus = ok ? "PathComplete" : reason2; r.innToGiverLength = Walker303.Length(c2); }
            Harness303.WriteJson(Folder, "probe.json", r);
            return JsonUtility.ToJson(r, true);
        }

        static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * t);
        }

        internal static SpellBookSO SpellBook(WorldMacroPlaytestSession s)
        {
            var scope = Object.FindObjectsByType<CombatLifetimeScope>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(x => x.gameObject.scene == s.gameObject.scene);
            return scope != null ? Harness303.Field<SpellBookSO>(scope, "_spellBook") : null;
        }

        /// <summary>A NavMesh point 13 m from the 창귀's home with a clear eye → chest line, within 24 m of home (inside the
        /// 28 m leash), with a complete path from `from`; the shortest such path wins.</summary>
        internal static bool EngagementPoint(WorldMacroPlaytestSession s, PrologueEncounter actor, Vector3 from, out Vector3 point, out bool sight, out string why)
        {
            point = default; sight = false; why = "";
            Vector3 home = actor.transform.position; float best = float.MaxValue; int tried = 0, sightless = 0, pathless = 0;
            foreach (float radius in new[] { 13f, 11f, 15f })
                for (int i = 0; i < 24; i++)
                {
                    var c = home + Quaternion.Euler(0, i * 15f, 0) * Vector3.forward * radius; tried++;
                    if (!NavMesh.SamplePosition(c, out var hit, 3f, NavMesh.AllAreas)) continue;
                    if (Harness303.Flat(hit.position, home) > 24f || Harness303.Flat(hit.position, home) < 9f) continue;
                    if (!Sight(hit.position + Vector3.up * s.Walker.EyeHeight, home + Vector3.up * 1.2f, actor.transform)) { sightless++; continue; }
                    if (!Walker303.TryPath(from, hit.position, out var corners, out _)) { pathless++; continue; }
                    float len = Walker303.Length(corners);
                    if (len < best) { best = len; point = hit.position; sight = true; }
                }
            if (best < float.MaxValue) return true;
            why = tried + " candidates, " + sightless + " without sight, " + pathless + " without a complete path";
            return false;
        }

        internal static bool Sight(Vector3 eye, Vector3 target, Transform ignore)
        {
            var d = target - eye;
            foreach (var h in Physics.RaycastAll(eye, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (ignore == null || !h.transform.IsChildOf(ignore)) return false;
            return true;
        }
    }
}
