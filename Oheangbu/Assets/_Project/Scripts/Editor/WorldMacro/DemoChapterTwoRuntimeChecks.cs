using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.App.World;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Oheangbu.App.World.UI;
using Oheangbu.Core.Domain;
using Oheangbu.Data.Demo;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Opt-in API diagnostic. Only a new UUID save is written; no input or walking is synthesized.</summary>
    [InitializeOnLoad]
    public static class DemoChapterTwoRuntimeChecks
    {
        const string StateKey = "DemoChapterTwoRuntimeChecks.State";
        const string SuffixKey = "PlaytestUiReviewSuffix";
        const string Missing = "__missing_demo_check_session_key__";
        const string ExpectedScene = "Assets/_Project/Scenes/World/W_Demo_Campaign.unity";
        [Serializable] sealed class Check { public string name, status, detail; }
        [Serializable] sealed class ProtectedFile { public string path, sha256; }
        [Serializable] sealed class Run
        {
            public string status, phase, suffix, savePath, priorSuffix;
            public string scope = "Actual Play Session.Interact, shop callbacks, EnemyVitals.TakeDamage and PrologueEncounter.Defeated using bounded safe teleports. No direct progress/defeated injection. API diagnostic, not native combat input, walking, app restart, or full campaign completion.";
            public bool active, holdingPlay, stopping, suffixRestored, oldRunInBackground;
            public int stage;
            public int currencyAfterInquiry, currencyAfterCache, campaignCountAfterInquiry;
            public string inquiryRewardDetail;
            public double readyAt, deadline;
            public float pausedTime;
            public Vector3 pausedFeet;
            public List<Check> checks = new List<Check>();
            public List<ProtectedFile> protectedFiles = new List<ProtectedFile>();
            public List<string> failures = new List<string>();
            public string[] unverified = { "Native keyboard/mouse interaction", "App restart and scene re-entry", "Bosses and reserved campaign stages", "Visual quality", "Vehicle or combat pause during active input" };
        }
        static Run run;
        static WorldMacroPlaytestSession session;
        static PlaytestUiRoot ui;
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Demo/Chapter2/runtime_tests.json"));

        static DemoChapterTwoRuntimeChecks()
        {
            string pending = SessionState.GetString(StateKey, "");
            if (!string.IsNullOrEmpty(pending)) run = JsonUtility.FromJson<Run>(pending);
            EditorApplication.playModeStateChanged += PlayChanged;
            EditorApplication.quitting += EditorQuitting;
            if (run != null && (run.active || run.holdingPlay || run.stopping))
                EditorApplication.update += Tick;
        }

        public static string Execute(string command)
        {
            if (command == "poll")
                return run != null ? JsonUtility.ToJson(run, true) : File.Exists(Output) ? File.ReadAllText(Output) : "NOT_RUN";
            if (command == "stop" || command == "abort")
            {
                if (run == null || !(run.active || run.holdingPlay || run.stopping)) return "NOT_RUNNING";
                if (run.active) { run.failures.Add(command == "abort" ? "Explicit abort" : "Stopped before completion"); run.status = "ABORTED"; }
                run.active = false; run.holdingPlay = false; run.stopping = true; run.phase = "waiting for Edit mode cleanup";
                Persist();
                if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
                else Cleanup();
                return "STOPPING; previous diagnostic suffix restored after Play exits";
            }
            if (command != "begin") throw new ArgumentException("Use begin, poll, stop or abort.");
            if (run != null && (run.active || run.holdingPlay || run.stopping)) throw new InvalidOperationException("A diagnostic run is already active; stop it first.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Begin requires idle Edit mode after compilation.");
            if (SceneManager.GetActiveScene().path != ExpectedScene)
                throw new InvalidOperationException("Open W_Demo_Campaign before begin. This diagnostic never opens or modifies scenes.");
            var authored = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            var authoredUi = Object.FindFirstObjectByType<PlaytestUiRoot>();
            if (authored == null || authored.Content == null || authored.Content.Campaign == null || authored.Content.Economy == null ||
                authored.Content.SaveSlot != "demo-campaign-foundation-v1" || authoredUi == null || !authoredUi.enabled)
                throw new InvalidOperationException("An isolated demo content asset and active UI are required.");
            string suffix = "_chapter2_" + Guid.NewGuid().ToString("N");
            string savePath = Path.Combine(Application.persistentDataPath, authored.Content.SaveSlot + suffix + ".json");
            if (File.Exists(savePath) || File.Exists(savePath + ".bak") || File.Exists(savePath + ".tmp"))
                throw new IOException("Refusing to reuse an existing diagnostic save.");
            run = new Run
            {
                status = "RUNNING", phase = "entering Play", stage = -1, active = true,
                suffix = suffix, savePath = savePath, priorSuffix = SessionState.GetString(SuffixKey, Missing),
                oldRunInBackground = Application.runInBackground, deadline = EditorApplication.timeSinceStartup + 240
            };
            foreach (string path in ExistingSaveFiles()) run.protectedFiles.Add(new ProtectedFile { path = path, sha256 = Hash(path) });
            SessionState.SetString(SuffixKey, suffix);
            Application.runInBackground = true;
            session = null; ui = null; Persist();
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
            return "RUNNING; fresh UUID save, actual API interactions, leaves Play at the logging shelter shop for review. Use stop when finished.";
        }

        static IEnumerable<string> ExistingSaveFiles()
        {
            if (!Directory.Exists(Application.persistentDataPath)) return Array.Empty<string>();
            // Include primary, backup, interrupted writes and settings; no existing file may change.
            return Directory.GetFiles(Application.persistentDataPath, "*.json*", SearchOption.TopDirectoryOnly)
                .Where(p => run == null || !p.StartsWith(run.savePath, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p).ToArray();
        }
        static string Hash(string path)
        {
            using (var algorithm = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
        }
        static void Persist()
        {
            if (run == null) return;
            string json = JsonUtility.ToJson(run, true);
            SessionState.SetString(StateKey, json);
            Directory.CreateDirectory(Path.GetDirectoryName(Output)); File.WriteAllText(Output, json);
        }
        static void CheckThat(bool ok, string name, string detail)
        {
            run.checks.Add(new Check { name = name, status = ok ? "PASS" : "FAIL", detail = detail });
            if (!ok) throw new InvalidOperationException(name + ": " + detail);
        }
        static void Next(int stage, string phase, double delay = .15)
        {
            run.stage = stage; run.phase = phase; run.readyAt = EditorApplication.timeSinceStartup + delay; Persist();
        }
        static bool InputReady => ui != null && ui.Pause != null && !ui.Pause.IsPaused && !ui.Pause.Gate.InputBlocked && Time.timeScale > .0001f;
        static bool HasStage(string id) => session.Progress.campaign.Completed.Contains(id);
        static int Currency => session.Progress.ledger.currency;
        static void Approach(string id)
        {
            var point = id == "village_commission" ? session.Content.Opening.Commission : Array.Find(session.Content.Points, p => p.Id == id);
            CheckThat(point != null, "authored point " + id, "Uses actual content ID and scene-space position.");
            bool found = false;
            // Search reachable sides of the real prop; LOS and capsule safety are never bypassed.
            foreach (float fraction in new[] { .65f, .85f, .4f })
            {
                for (int i = 0; i < 24; i++)
                {
                    float angle = i * Mathf.PI / 12;
                    Vector3 candidate = point.Position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * Mathf.Min(2.1f, point.Radius * fraction);
                    if (!session.TrySafeFeet(candidate, out Vector3 feet)) continue;
                    Vector3 direction = Vector3.ProjectOnPlane(point.Position - feet, Vector3.up);
                    session.Teleport(feet, direction.sqrMagnitude > .001f ? Quaternion.LookRotation(direction).eulerAngles.y : 0);
                    Physics.SyncTransforms();
                    if (!session.CanInteract(id)) continue;
                    found = true; break;
                }
                if (found) break;
            }
            CheckThat(found, "safe approach " + id, "Session.TrySafeFeet, Teleport, and CanInteract with actual distance and line of sight; no input synthesis.");
        }

        static void Tick()
        {
            if (run == null) return;
            if (run.stopping)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) Cleanup();
                return;
            }
            if (!run.active) return;
            try
            {
                if (EditorApplication.timeSinceStartup > run.deadline) throw new TimeoutException("Play API diagnostic timed out; input must remain neutral.");
                if (!EditorApplication.isPlaying || EditorApplication.isPaused || EditorApplication.timeSinceStartup < run.readyAt) return;
                if (session == null) session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
                if (ui == null) ui = PlaytestUiRoot.Instance;
                if (session == null || session.Progress == null || ui == null || ui.Pause == null || ui.Session != session || ui.Busy) return;
                CheckSuffix();
                switch (run.stage)
                {
                    case -1:
                        CheckThat(session.DemoCampaignActive && session.DemoEconomy != null, "demo runtime binding", "Campaign and shop economy instantiated by actual Session.Start.");
                        CheckThat(session.LoadStatus == "new" && Currency == 0 && session.Progress.campaign.Completed.Count == 0, "fresh UUID slot", session.LoadStatus + "; zero currency and no completed campaign stages.");
                        CheckThat(session.TestSaveSuffix == run.suffix, "diagnostic suffix applied before Session.Start", session.TestSaveSuffix);
                        ui.CloseMenu(); Next(0, "approach office"); break;
                    case 0:
                        if (!InputReady) return;
                        Approach("village_commission");
                        ui.OpenPage("일시정지"); run.pausedFeet = session.Walker.Body.transform.position; run.pausedTime = Time.time;
                        CheckThat(ui.Page == "일시정지" && ui.Pause.IsPaused && ui.Pause.Gate.InputBlocked && Time.timeScale == 0, "menu owns pause and input gate", "Actual PlaytestUiRoot.OpenPage, no time-scale override by diagnostic.");
                        CheckThat(!session.Interact("village_commission") && !HasStage("commission"), "paused interaction rejected", "A real Session.Interact request cannot advance while menu is open.");
                        Next(1, "paused frame hold", .3); break;
                    case 1:
                        CheckThat(Mathf.Abs(Time.time - run.pausedTime) < .0001f && Vector3.Distance(run.pausedFeet, session.Walker.Body.transform.position) < .02f, "pause holds world and player", "0.3 seconds of editor time with unchanged scaled time and player feet.");
                        ui.CloseMenu(); Next(2, "accept commission"); break;
                    case 2:
                        if (!InputReady) return;
                        CheckThat(session.Interact("village_commission") && HasStage("commission") && Currency == 0, "accept commission", "Actual Session.Interact(village_commission); no currency granted.");
                        ui.CloseMenu(); Next(3, "approach mine inquiry"); break;
                    case 3:
                        if (!InputReady) return;
                        Approach("mine_inquiry"); Next(4, "investigate mine"); break;
                    case 4:
                        if (!InputReady) return;
                        CheckThat(session.Interact("mine_inquiry") && HasStage("mine_evidence") && Currency == 0, "mine inquiry advances objective", "Actual Session.Interact(mine_inquiry); evidence alone does not grant report reward.");
                        ui.CloseMenu(); Next(5, "return to office"); break;
                    case 5:
                        if (!InputReady) return;
                        Approach("village_commission"); Next(6, "report evidence"); break;
                    case 6:
                        if (!InputReady) return;
                        CheckThat(session.Interact("village_commission") && HasStage("office_report") && Currency == 60, "report grants 60 exactly once", "Same office ID reused with actual stage-dependent dialogue/reward.");
                        ui.CloseMenu(); Next(7, "repeat report request"); break;
                    case 7:
                        if (!InputReady) return;
                        CheckThat(!session.Interact("village_commission") && Currency == 60 && session.Progress.campaign.Completed.Count == 3, "duplicate report cannot mint currency", "Second actual report request is rejected; objective remains inn rest.");
                        ui.CloseMenu(); Next(8, "approach inn"); break;
                    case 8:
                        if (!InputReady) return;
                        Approach("geumpyo_inn"); Next(9, "inn rest"); break;
                    case 9:
                        if (!InputReady) return;
                        CheckThat(session.Interact("geumpyo_inn") && HasStage("inn_rest") && session.Progress.ledger.checkpoint == "geumpyo_inn", "actual inn rest and checkpoint", "Session.Interact invokes Rest, campaign event, recovery and checkpoint saving.");
                        CheckThat(session.AtDemoShop && ui.Page == "정비" && ui.Pause.IsPaused, "rest opens real shop UI", "InteractionResolved binds to the actual shop page; game remains paused.");
                        Next(10, "purchase Wood tier one"); break;
                    case 10:
                        var button = ui.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Buy_Wood");
                        CheckThat(button != null && button.interactable, "foundation Wood purchase available", "Report reward funds a real 60-tongbo purchase before chapter two.");
                        button.onClick.Invoke();
                        CheckThat(Currency == 0 && session.Progress.economy.Level(DemoUpgradeTrack.Wood) == 1, "foundation upgrade stored", "Actual Buy_Wood callback; later rest must preserve this upgrade.");
                        ui.CloseMenu(); Next(11, "approach Jeongdam"); break;
                    case 11:
                        if (!InputReady) return;
                        Approach("jeongdam_j1"); Next(12, "Jeongdam relay dialogue"); break;
                    case 12:
                        if (!InputReady) return;
                        CheckThat(session.Interact("jeongdam_j1") && HasStage("relay"), "actual Jeongdam interaction", "Session.Interact(jeongdam_j1) advances relay, not a preview visitation marker.");
                        ui.CloseMenu(); Next(13, "approach Wangso"); break;
                    case 13:
                        if (!InputReady) return;
                        Approach("wangso_w1"); Next(14, "Wangso cargo contract"); break;
                    case 14:
                        if (!InputReady) return;
                        CheckThat(session.Interact("wangso_w1") && HasStage("cargo_contract"), "actual Wangso interaction", "Session.Interact(wangso_w1) records the cargo contract stage.");
                        ui.CloseMenu(); Next(15, "approach logging inquiry"); break;
                    case 15:
                        if (!InputReady) return;
                        Approach("logging_inquiry"); session.Cull();
                        var loggingStage = Array.Find(session.Content.Campaign.Stages, x => x.Id == "logging");
                        CheckThat(loggingStage != null && loggingStage.Implemented && loggingStage.RequiredDefeatedIds != null &&
                            loggingStage.RequiredDefeatedIds.OrderBy(x => x).SequenceEqual(ThreatIds.OrderBy(x => x)), "logging threat gate authored", "RequiredDefeatedIds exactly references demo_logging_01, 02 and 03.");
                        foreach (string id in ThreatIds)
                        {
                            var actor = Threat(id);
                            CheckThat(actor != null && actor.GetComponent<EnemyVitals>().IsAlive && !session.Progress.defeated.Contains(id), "live required threat " + id, "Actual instantiated encounter starts alive with no saved defeat marker.");
                        }
                        Next(16, "blocked investigation while threats alive"); break;
                    case 16:
                        if (!InputReady) return;
                        int beforeBlocked = Currency;
                        CheckThat(!session.Interact("logging_inquiry") && !HasStage("logging") && Currency == beforeBlocked, "living threats block inquiry", "Actual investigation attempt fails before any threat is defeated; no reward or campaign advance.");
                        ui.CloseMenu(); Next(17, "first actual defeat callback"); break;
                    case 17:
                        if (!InputReady) return;
                        DefeatThreat(ThreatIds[0]); Next(18, "second defeat and partial gate"); break;
                    case 18:
                        if (!InputReady) return;
                        DefeatThreat(ThreatIds[1]);
                        beforeBlocked = Currency;
                        CheckThat(!session.Interact("logging_inquiry") && !HasStage("logging") && Currency == beforeBlocked, "partial threat defeat still blocks inquiry", "Two real defeat callbacks have fired, but demo_logging_03 is still alive.");
                        ui.CloseMenu(); Next(19, "last actual defeat callback"); break;
                    case 19:
                        if (!InputReady) return;
                        DefeatThreat(ThreatIds[2]);
                        CheckThat(ThreatIds.All(id => session.Progress.defeated.Contains(id)), "all required deaths reached session ledger", "No test code writes Progress.defeated; records come from actual enemy death events.");
                        Next(20, "complete logging inquiry"); break;
                    case 20:
                        if (!InputReady) return;
                        Approach("logging_inquiry");
                        var point = Point("logging_inquiry");
                        loggingStage = Array.Find(session.Content.Campaign.Stages, x => x.Id == "logging");
                        int expected = checked(Currency + loggingStage.TongboReward + Math.Max(0, point.Currency));
                        CheckThat(session.Interact("logging_inquiry") && HasStage("logging") && Currency == expected, "cleared investigation grants exactly authored reward", "Actual Session.Interact after three EnemyVitals death events; expected balance " + expected + ".");
                        run.currencyAfterInquiry = Currency; run.campaignCountAfterInquiry = session.Progress.campaign.Completed.Count;
                        run.inquiryRewardDetail = "Campaign " + loggingStage.TongboReward + ", point " + point.Currency;
                        ui.CloseMenu(); Next(21, "repeat cleared investigation"); break;
                    case 21:
                        if (!InputReady) return;
                        CheckThat(!session.Interact("logging_inquiry") && Currency == run.currencyAfterInquiry && session.Progress.campaign.Completed.Count == run.campaignCountAfterInquiry, "logging inquiry cannot pay twice", "Repeated actual interaction is rejected after the completed campaign stage.");
                        ui.CloseMenu(); Next(22, "approach optional cache"); break;
                    case 22:
                        if (!InputReady) return;
                        Approach("demo_logging_cache"); Next(23, "claim optional cache"); break;
                    case 23:
                        if (!InputReady) return;
                        point = Point("demo_logging_cache");
                        CheckThat(point.Kind == PrologueInteractionKind.Currency && point.Currency > 0, "cache is a real currency interaction", "Uses authored side-path reward, not a preview-only visit.");
                        expected = checked(Currency + point.Currency);
                        CheckThat(session.Interact("demo_logging_cache") && Currency == expected && session.Progress.ledger.completed.Contains("demo_logging_cache"), "optional cache grants once", "Actual Session.Interact writes the one-time completion ID and " + point.Currency + " tongbo.");
                        run.currencyAfterCache = Currency;
                        CheckThat(session.Interact("demo_logging_cache") && Currency == run.currencyAfterCache, "cache repeat cannot duplicate reward", "Repeated cache interaction preserves the balance.");
                        ui.CloseMenu(); Next(24, "approach logging shelter"); break;
                    case 24:
                        if (!InputReady) return;
                        Approach("logging_rest"); Next(25, "rest at authored logging checkpoint"); break;
                    case 25:
                        if (!InputReady) return;
                        CheckThat(session.Interact("logging_rest"), "actual logging shelter rest", "Session.Interact(logging_rest), not the historical hardcoded inn path.");
                        CheckThat(session.Progress.ledger.checkpoint == "logging_rest" && WorldMacroCheckpointRules.TryResolve(session.Content, session.Progress, "logging_rest", out var checkpoint) &&
                            Vector3.Distance(session.Progress.ledger.checkpointPosition, checkpoint.Feet) < .6f, "new checkpoint ID and safe position persist", "Authored logging_rest respawn feet; safety probe may add a small floor offset.");
                        CheckThat(session.AtDemoShop && ui.Page == "정비" && ui.Pause.IsPaused, "logging shelter opens real shop", "Configured Shop flag and actual interaction distance are honored.");
                        CheckThat(Currency == run.currencyAfterCache && HasStage("logging") && session.Progress.ledger.completed.Contains("demo_logging_cache") && session.Progress.economy.Level(DemoUpgradeTrack.Wood) == 1, "rest preserves chapter rewards and upgrade", "Completed inquiry, claimed cache, tongbo and Wood tier one remain unchanged.");
                        foreach (string id in ThreatIds)
                        {
                            var spec = Array.Find(session.Content.Encounters, e => e.Id == id);
                            var actor = Threat(id);
                            CheckThat(spec != null && spec.RespawnOnRest && actor.GetComponent<EnemyVitals>().IsAlive && !session.Progress.defeated.Contains(id), "normal enemy reset " + id, "Actual Rest reset revives the encounter and clears its transient defeat marker.");
                        }
                        Next(26, "read checkpoint JSON"); break;
                    case 26:
                        CheckThat(session.SaveNow(out string error), "chapter save succeeds", error ?? "SaveNow succeeded.");
                        CheckThat(File.Exists(run.savePath), "UUID save file exists", run.savePath);
                        var disk = JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(run.savePath));
                        CheckThat(WorldMacroProgress.Valid(disk) && disk.ledger.currency == run.currencyAfterCache && disk.ledger.checkpoint == "logging_rest" && disk.economy.Level(DemoUpgradeTrack.Wood) == 1 &&
                            disk.campaign.Completed.Contains("relay") && disk.campaign.Completed.Contains("cargo_contract") && disk.campaign.Completed.Contains("logging") &&
                            disk.ledger.completed.Contains("demo_logging_cache") && ThreatIds.All(id => !disk.defeated.Contains(id)), "disk JSON preserves chapter two rest state", "Reloaded checkpoint, reward, contract, cleared logging stage, cache, upgrade and respawned regular enemies.");
                        var store = new AtomicJsonStore<WorldMacroProgress>(run.savePath, WorldMacroProgress.Valid);
                        var loaded = store.Load();
                        CheckThat(loaded != null && store.LoadStatus == "primary" && loaded.economy.SameAs(disk.economy) && loaded.ledger.currency == disk.ledger.currency && loaded.ledger.checkpoint == "logging_rest", "fresh AtomicJsonStore reload", "Read primary UUID save independently. App restart is not claimed.");
                        float scale = session.Walker.Wiring.PlayerDamageScale != null ? session.Walker.Wiring.PlayerDamageScale(Element.Wood) : 0;
                        CheckThat(Mathf.Abs(scale - 1.1f) < .0001f, "runtime Wood upgrade survives rest reset", "CombatLoopWiring damage scale remains 1.1 after resetting encounters.");
                        VerifyProtectedFiles("existing saves unchanged after chapter two diagnostics");
                        run.active = false; run.holdingPlay = true; run.status = "PASS_API_INTEGRATION";
                        run.phase = "finished; Play held at logging shelter shop for review; stop restores prior suffix";
                        Persist(); EditorApplication.update -= Tick; break;
                }
            }
            catch (Exception e)
            {
                run.failures.Add(e.ToString()); run.active = false; run.holdingPlay = EditorApplication.isPlaying;
                run.status = "FINDINGS"; run.phase = "failed; inspect then stop";
                Persist(); EditorApplication.update -= Tick;
                if (!EditorApplication.isPlayingOrWillChangePlaymode) Cleanup();
            }
        }
        static readonly string[] ThreatIds = { "demo_logging_01", "demo_logging_02", "demo_logging_03" };
        static PrologueContentSO.Point Point(string id) => Array.Find(session.Content.Points, p => p != null && p.Id == id);
        static PrologueEncounter Threat(string id) => Array.Find(session.Actors, actor => actor != null && actor.Id == id);
        static void DefeatThreat(string id)
        {
            session.Cull();
            var actor = Threat(id);
            CheckThat(actor != null && actor.enabled && actor.gameObject.activeInHierarchy, "actual death subscription active " + id,
                "Session.Cull activates the nearby encounter; the diagnostic does not write progress or directly call Session.EnemyDefeated.");
            var vitals = actor.GetComponent<EnemyVitals>();
            CheckThat(vitals != null && vitals.IsAlive, "live target before diagnostic damage " + id, "Actual EnemyVitals component.");
            int before = Currency;
            vitals.TakeDamage(float.MaxValue);
            CheckThat(!vitals.IsAlive && actor.Current == PrologueEncounter.Behaviour.Dead && session.Progress.defeated.Contains(id) && Currency == before + session.Content.TestRules.EnemyReward,
                "EnemyVitals death reaches encounter and session " + id, "TakeDamage -> Died -> PrologueEncounter.Defeated -> saved Session.EnemyDefeated. No simulated combat input is claimed.");
            vitals.TakeDamage(float.MaxValue);
            CheckThat(Currency == before + session.Content.TestRules.EnemyReward, "dead enemy cannot pay twice " + id, "Second damage call emits no repeated reward.");
        }
        static void CheckSuffix()
        {
            if (session.TestSaveSuffix != run.suffix || SessionState.GetString(SuffixKey, Missing) != run.suffix)
                throw new InvalidOperationException("Diagnostic suffix changed unexpectedly; no further interaction/save calls are allowed.");
            if (!string.Equals(Path.Combine(Application.persistentDataPath, session.Content.SaveSlot + session.TestSaveSuffix + ".json"), run.savePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Session save path differs from the allocated UUID slot.");
        }
        static void VerifyProtectedFiles(string name)
        {
            foreach (var prior in run.protectedFiles)
                CheckThat(File.Exists(prior.path) && Hash(prior.path) == prior.sha256, name, Path.GetFileName(prior.path));
            var known = new HashSet<string>(run.protectedFiles.Select(f => f.path), StringComparer.OrdinalIgnoreCase);
            CheckThat(ExistingSaveFiles().All(known.Contains), "no unexpected non-test save created", "Only the allocated UUID primary/backup/temporary names are excluded from the audit.");
        }
        static void PlayChanged(PlayModeStateChange state)
        {
            if (run == null || !(run.active || run.holdingPlay || run.stopping)) return;
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                if (run.active) { run.failures.Add("Play ended before checks completed."); run.status = "ABORTED"; }
                Cleanup();
            }
        }
        static void EditorQuitting()
        {
            if (run == null || run.suffixRestored) return;
            if (run.active) { run.failures.Add("Editor quit before completion."); run.status = "ABORTED"; }
            Cleanup();
        }
        static void Cleanup()
        {
            if (run == null || run.suffixRestored) return;
            try { VerifyProtectedFiles("existing saves unchanged after Play exit"); }
            catch (Exception e) { run.failures.Add("Save isolation: " + e.Message); run.status = "FINDINGS"; }
            if (run.priorSuffix == Missing) SessionState.EraseString(SuffixKey); else SessionState.SetString(SuffixKey, run.priorSuffix);
            Application.runInBackground = run.oldRunInBackground;
            run.suffixRestored = SessionState.GetString(SuffixKey, Missing) == run.priorSuffix;
            run.active = false; run.holdingPlay = false; run.stopping = false;
            run.phase = "finished; previous SessionState suffix and background setting restored";
            EditorApplication.update -= Tick; Persist();
        }
    }
}
