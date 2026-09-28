using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data.World;
using Oheangbu.Spellcraft;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactRebuildAuthoring
    {
        // Dispatch before Run295's Edit-only guard. The fixture owns both Play transitions.
        static string MumPlay295(string command) => MumBridge295PlayChecks.Run(command);
    }

    /// <summary>Real candidate session lifecycle, isolated save and two actual Play starts.</summary>
    [InitializeOnLoad]
    public static partial class MumBridge295PlayChecks
    {
        const string Key = "Watershed295.MumPlay", UiKey = "PlaytestUiReviewSuffix", DirectKey = "Compact270.DirectPlay", Missing = "__unset__";
        const string ScenePath = "Assets/_Project/Art/World/Watershed295/W_Demo_Compact_Watershed295.unity";
        const string Slot = "world-compact-watershed-295", Suffix = "_mum295_test", BanksName = "Mum295_PlayLifecycleBanks";
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/World/Compact/Rebuild/Watershed295/Mum"));
        static string TerrainPrefix => run?.hyeongangMode == true ? "hyeongang" : "terrain";
        static string Output => Path.Combine(Folder, run?.terrainMode == true ? TerrainPrefix+"-play-checks.json" : "play-checks.json");
        static WorldMacroPlaytestSession Session => Object.FindFirstObjectByType<WorldMacroPlaytestSession>();

        [Serializable] sealed class Stamp { public string path, hash, backup; public bool existed; }
        [Serializable] sealed class Evidence { public string phase, detail; public int frame, bridges; public Vector3 feet; }
        [Serializable] sealed partial class State
        {
            public bool active, stopping, restart, restored, finished, priorDirectPlay, priorDirectPlayPresent;
            public int phase, lastFrame = -1, playStarts, secondPlayRequests;
            public double deadline, phaseAt, enteredAt, secondPlayRequestedAt;
            public string status, priorSuffix, priorUi, priorStartup, savePath, sceneHash, restId, originArea, awayArea, expectedProgress;
            public Vector3 bankA, bankB, checkpoint, approach, safeBeforeBridge, away;
            public List<Stamp> saves = new List<Stamp>(), isolated = new List<Stamp>();
            public List<string> checks = new List<string>(), failures = new List<string>();
            public List<Evidence> evidence = new List<Evidence>();
            public string scope = "Actual candidate Play session, public Interact(rest), failed/successful atomic rest saves, live service Update/realm callback, fatal fall and saved recovery, SaveNow, then a second actual Play boot. Runtime-only static bank pads and direct body placement provide deterministic setup. No native drawing, route traversal, or art/performance certification; those are separate checks.";
        }
        static State run;

        static MumBridge295PlayChecks()
        {
            string saved = SessionState.GetString(Key, "");
            if (!string.IsNullOrEmpty(saved)) run = JsonUtility.FromJson<State>(saved);
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Changed;
            Application.logMessageReceived += Log;
        }

        public static string Run(string command)
        {
            if (command == "status") return run == null ? "No Mum lifecycle run." : Persist();
            if (command == "start") return Start();
            if (command == "start-terrain") return Start(true);
            if (command == "start-hyeongang") return Start(true,true);
            if (command == "abort")
            {
                if (run?.active == true || run?.stopping == true)
                {
                    run.failures.Add("Stopped by explicit abort command.");
                    Stop(false);
                }
                return run == null ? "No Mum lifecycle run." : Persist();
            }
            throw new ArgumentException("Mum lifecycle command: start, start-terrain, start-hyeongang, status or abort.");
        }

        static string Start(bool terrain = false,bool hyeongang = false)
        {
            if (run?.active == true || run?.stopping == true) throw new InvalidOperationException("A Mum lifecycle run is already active.");
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open the saved #295 candidate in Edit mode first.");
            var s = Session;
            if (s == null || s.Content == null || s.Content.SaveSlot != Slot || s.MumBridgeProfile == null)
                throw new InvalidOperationException("The #295 session, isolated content slot and Mum profile must be bound.");
            var terrainSites = terrain ? ReadTerrainSites(s,hyeongang) : null;
            Directory.CreateDirectory(Folder);
            string prefix=hyeongang?"hyeongang":"terrain";
            string previousOutput = Path.Combine(Folder, terrain ? prefix+"-play-checks.json" : "play-checks.json");
            if (File.Exists(previousOutput))
            {
                string archive = Path.Combine(Folder, "history", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
                Directory.CreateDirectory(archive);
                foreach (string name in new[] { Path.GetFileName(previousOutput), "play-save-on-bridge.json", "play-reload-expected.json", "play-reload-actual.json", prefix+"-placement.png", prefix+"-placement.png.json", prefix+"-return.png", prefix+"-return.png.json" })
                    if (File.Exists(Path.Combine(Folder, name))) File.Copy(Path.Combine(Folder, name), Path.Combine(archive, name));
            }
            run = new State {
                active = true, status = "ENTERING_PLAY", priorSuffix = s.TestSaveSuffix, terrainMode = terrain, hyeongangMode = hyeongang, terrainSites = terrainSites,
                priorUi = SessionState.GetString(UiKey, Missing), sceneHash = Hash(ScenePath),
                priorStartup = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
                priorDirectPlay = SessionState.GetBool(DirectKey, false),
                priorDirectPlayPresent = SessionState.GetBool(DirectKey, true) == SessionState.GetBool(DirectKey, false),
                savePath = Path.Combine(Application.persistentDataPath, Slot + Suffix + ".json"),
                deadline = EditorApplication.timeSinceStartup + 360
            };
            if (terrain) run.scope = "Actual candidate riverbank placement under unchanged Mum geometry rules; normal progression lock then isolated editor test unlock; live formation; actual player CharacterController.Move crosses out and back with collision/support samples and two bank-view PNGs; actual rest cleanup; live traversal drowning on the real riverbed and an8m airborne setup over dry terrain followed by actual PlayerMotor gravity/fatal-fall recovery. Body placement is setup only. No native handwriting or player-input certification, synthetic pads, terrain edits, or normal progression grants.";
            Directory.CreateDirectory(Application.persistentDataPath);
            foreach (string path in Directory.GetFiles(Application.persistentDataPath, "*.json*"))
                if (!IsIsolated(path)) run.saves.Add(new Stamp { path = path, existed = true, hash = Hash(path) });
            string backupFolder = Path.Combine(Folder, "slot-backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
            Directory.CreateDirectory(backupFolder);
            // Exactly these three known files belong to the isolated AtomicJsonStore. Ordinary slots are read only.
            foreach (string extension in new[] { "", ".bak", ".tmp" })
            {
                string path = run.savePath + extension;
                var stamp = new Stamp { path = path, existed = File.Exists(path), backup = Path.Combine(backupFolder, Path.GetFileName(path)) };
                if (stamp.existed) { stamp.hash = Hash(path); File.Copy(path, stamp.backup); }
                run.isolated.Add(stamp);
            }
            Persist();
            try
            {
                foreach (var stamp in run.isolated) if (stamp.existed) File.Delete(stamp.path);
                s.TestSaveSuffix = Suffix;
                SessionState.SetString(UiKey, Suffix);
                CompactLoadingStartup270.UseCurrent();
                Persist(); EditorApplication.EnterPlaymode();
            }
            catch (Exception e) { run.failures.Add(e.ToString()); FinishInEdit(); }
            return Persist();
        }

        static bool IsIsolated(string path) => new[] { run.savePath, run.savePath + ".bak", run.savePath + ".tmp" }
            .Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        static string Hash(string path)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }
        static string Persist()
        {
            string json = JsonUtility.ToJson(run, true);
            SessionState.SetString(Key, json); Directory.CreateDirectory(Folder); File.WriteAllText(Output, json);
            return json;
        }
        static void Check(bool value, string detail)
        {
            (value ? run.checks : run.failures).Add(detail); Persist();
            if (!value) throw new InvalidOperationException(detail);
        }
        static void Record(string label, WorldMacroPlaytestSession s, string detail)
        {
            run.evidence.Add(new Evidence { phase = label, detail = detail, frame = Time.frameCount,
                bridges = s.MumBridges?.ActiveCount ?? -1, feet = Feet(s.Walker.Body) });
            Persist();
        }
        static void Log(string text, string stack, LogType type)
        {
            if (run?.active != true || (type != LogType.Error && type != LogType.Exception) || run.failures.Count >= 30) return;
            run.failures.Add(type + ": " + text); Persist();
        }
        static void Advance(int phase, string status)
        { run.phase = phase; run.phaseAt = EditorApplication.timeSinceStartup; run.status = status; Persist(); }
        static bool Settled(double seconds = .35) => EditorApplication.timeSinceStartup - run.phaseAt >= seconds;

        static void Changed(PlayModeStateChange change)
        {
            if (run == null || (!run.active && !run.stopping)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                run.playStarts++; run.enteredAt = EditorApplication.timeSinceStartup;
                run.lastFrame = -1; run.status = run.phase == 100 ? "RELOADING_ISOLATED_SAVE" : "WAITING_FOR_SESSION"; Persist();
            }
            if (change != PlayModeStateChange.EnteredEditMode) return;
            if (run.restart && run.failures.Count == 0)
            {
                run.restart = false; run.stopping = false; run.active = true;
                try
                {
                    Check(Hash(ScenePath) == run.sceneHash, "First Play exit leaves saved candidate scene unchanged.");
                    run.expectedProgress = File.ReadAllText(run.savePath);
                    File.WriteAllText(Path.Combine(Folder, "play-reload-expected.json"), run.expectedProgress);
                    var s = Session;
                    Check(s != null && s.Content.SaveSlot == Slot && SceneManager.GetActiveScene().path == ScenePath, "Edit scene retains the candidate content before second Play start.");
                    s.TestSaveSuffix = Suffix; SessionState.SetString(UiKey, Suffix);
                    Advance(100, "ENTERING_SECOND_PLAY");
                    EditorApplication.delayCall += RequestSecondPlay;
                    return;
                }
                catch (Exception e) { run.failures.Add(e.ToString()); }
            }
            else if (!run.stopping) run.failures.Add("Play mode ended before the lifecycle fixture completed.");
            FinishInEdit();
        }
        static void Stop(bool restart)
        {
            run.active = false; run.stopping = true; run.restart = restart;
            run.status = restart ? "EXITING_TO_RELOAD" : "EXITING_PLAY"; Persist();
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode(); else FinishInEdit();
        }
        static void FinishInEdit()
        {
            run.active = false;
            UnbindTerrainHealth();
            try
            {
                if (Session != null) Session.TestSaveSuffix = run.priorSuffix;
                if (run.priorUi == Missing) SessionState.EraseString(UiKey); else SessionState.SetString(UiKey, run.priorUi);
                RestoreStartup();
                // ApplyDefault also registers a delay callback after domain reload. Restore the prior choice after it.
                EditorApplication.delayCall += RestoreStartup;
                foreach (var stamp in run.isolated)
                {
                    if (stamp.existed) File.Copy(stamp.backup, stamp.path, true);
                    else if (File.Exists(stamp.path)) File.Delete(stamp.path);
                }
                run.restored = true;
                bool intact = run.isolated.All(s => s.existed ? File.Exists(s.path) && Hash(s.path) == s.hash : !File.Exists(s.path));
                (intact ? run.checks : run.failures).Add("Prior isolated primary/backup/tmp files restored byte-for-byte (or removed when originally absent).");
                bool savesIntact = run.saves.All(s => File.Exists(s.path) && Hash(s.path) == s.hash);
                (savesIntact ? run.checks : run.failures).Add("Every pre-existing ordinary save file remains byte-for-byte unchanged.");
                bool noNewOrdinary = Directory.GetFiles(Application.persistentDataPath, "*.json*").Where(p => !IsIsolated(p))
                    .All(p => run.saves.Any(s => string.Equals(s.path, p, StringComparison.OrdinalIgnoreCase)));
                (noNewOrdinary ? run.checks : run.failures).Add("No ordinary save slot was created by the lifecycle fixture.");
                (Hash(ScenePath) == run.sceneHash ? run.checks : run.failures).Add("Saved candidate scene hash unchanged across diagnostic Play sessions.");
                bool settings = Session != null && Session.TestSaveSuffix == run.priorSuffix && SessionState.GetString(UiKey, Missing) == run.priorUi;
                (settings ? run.checks : run.failures).Add("Edit session suffix and prior review suffix restored.");
                bool startup = SessionState.GetBool(DirectKey, false) == run.priorDirectPlay &&
                    AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == run.priorStartup;
                (startup ? run.checks : run.failures).Add("Prior Editor Play start scene and direct-play preference restored.");
            }
            catch (Exception e) { run.failures.Add("Cleanup: " + e); }
            run.active = run.stopping = run.restart = false;
            run.status = run.finished && run.restored && run.failures.Count == 0 ? "PASS" : "FAIL_OR_INCOMPLETE"; Persist();
        }
        static void RestoreStartup()
        {
            if (run == null || run.active || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (run.priorDirectPlayPresent) SessionState.SetBool(DirectKey, run.priorDirectPlay); else SessionState.EraseBool(DirectKey);
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(run.priorStartup) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(run.priorStartup);
        }

        static void RequestSecondPlay()
        {
            if (run?.active != true || run.phase != 100 || run.playStarts != 1 ||
                EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                run.secondPlayRequests > 0 && EditorApplication.timeSinceStartup - run.secondPlayRequestedAt < 2) return;
            try
            {
                if (run.secondPlayRequests >= 3) throw new InvalidOperationException("Editor did not enter the second Play session after three requests.");
                if (SceneManager.GetActiveScene().path != ScenePath || Hash(ScenePath) != run.sceneHash || Session == null ||
                    Session.Content.SaveSlot != Slot || File.ReadAllText(run.savePath) != run.expectedProgress)
                    throw new InvalidOperationException("Candidate scene or isolated saved progress changed before the second Play start.");
                Session.TestSaveSuffix = Suffix; SessionState.SetString(UiKey, Suffix);
                CompactLoadingStartup270.UseCurrent();
                run.secondPlayRequestedAt = EditorApplication.timeSinceStartup;
                if (run.secondPlayRequests++ == 0) run.deadline = Math.Max(run.deadline, run.secondPlayRequestedAt + 120);
                run.status = "SECOND_PLAY_REQUESTED"; Persist();
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e) { run.failures.Add("Second Play start: " + e); Stop(false); }
        }

        static void Tick()
        {
            if (run?.active != true) return;
            // A failed session Start never sets InitializationComplete. Preserve its exception and exit promptly.
            if (run.failures.Count > 0) { Stop(false); return; }
            // EnteredEditMode can precede the point at which EnterPlaymode is accepted. Retry from a stable editor update.
            if (run.phase == 100 && run.playStarts == 1 && !EditorApplication.isPlayingOrWillChangePlaymode)
            { RequestSecondPlay(); if (run?.active != true || EditorApplication.isPlayingOrWillChangePlaymode) return; }
            if (EditorApplication.timeSinceStartup > run.deadline)
            { run.failures.Add("Timed out at phase " + run.phase + ": " + run.status); Stop(false); return; }
            if (!EditorApplication.isPlaying || Time.frameCount == run.lastFrame) return;
            run.lastFrame = Time.frameCount;
            try
            {
                var s = Session;
                if (s == null || !s.InitializationComplete || EditorApplication.timeSinceStartup - run.enteredAt < 2 || PlaytestUiRoot.Instance?.LoadingInProgress == true) return;
                CloseUi();
                if (run.phase == 0 || run.phase == 100)
                {
                    Check(SceneManager.GetActiveScene().path == ScenePath && s.gameObject.scene.path == ScenePath,
                        "Actual Play starts directly in the #295 candidate rather than the default lobby (Play " + run.playStarts + ").");
                    Check(s.Content.SaveSlot == Slot && s.TestSaveSuffix == Suffix && ActualStorePath(s) == run.savePath,
                        "Actual session store opened the exact isolated _mum295_test path (Play " + run.playStarts + ").");
                    Check(s.isActiveAndEnabled && s.Walker.Motor.isActiveAndEnabled && s.MumBridges != null && s.MumBridges.isActiveAndEnabled && s.Traversal != null,
                        "Session, player motor, terrain query and bound Mum service stay active (Play " + run.playStarts + ").");
                    if (run.phase == 100) { VerifyReload(s); return; }
                    Check(s.LoadStatus == "new" && !s.HasMumBridge && !s.MumBridges.IsUnlocked, "Fresh ordinary progression keeps Mum locked before test override.");
                    if (run.terrainMode) { BeginTerrain(s); return; }
                    string proof = JsonUtility.ToJson(s.Progress);
                    s.SetMum295TestUnlock(true);
                    Check(s.MumBridges.IsUnlocked && !s.HasMumBridge && JsonUtility.ToJson(s.Progress) == proof, "Test unlock enables the real service without granting normal proof or changing progress.");
                    s.CombatActive = false; s.Cull();
                    SelectRest(s); SetupBanks(s);
                    PlaceBody(s, run.bankA + Vector3.up * .06f);
                    Advance(1, "WAITING_FOR_REAL_MOTOR_GROUNDING"); return;
                }
                if (run.terrainMode) { TickTerrain(s); return; }
                switch (run.phase)
                {
                    case 1:
                        if (!Settled() || !s.Walker.Motor.IsLocomotionGrounded) return;
                        Generate(s); Advance(2, "FORMING_REST_BRIDGE"); return;
                    case 2:
                        if (!Complete(s)) return;
                        Check(s.MumBridges.ActiveCount == 1, "Bridge forms through normal live Update before rest checks.");
                        PlaceBody(s, run.approach);
                        Record("rest-setup", s, "Direct body setup does not call Session.Teleport or reset the bridge.");
                        Advance(3, "TESTING_REAL_REST_TRANSACTION"); return;
                    case 3:
                        if (!Settled()) return;
                        Check(s.CanInteract(run.restId), "Player passes the actual rest interaction radius and line-of-sight gate: " + run.restId);
                        Check(s.MumBridges.ActiveCount == 1, "Bridge survives movement to the rest before any rest transaction.");
                        string before = JsonUtility.ToJson(s.Progress);
                        using (var locked = new FileStream(run.savePath + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                        {
                            Check(!s.Interact(run.restId), "Actual Rest rejects an exclusive-lock failure of its atomic temporary save.");
                            Check(s.MumBridges.ActiveCount == 1 && s.MumBridges.Bridges[0].Support.enabled && JsonUtility.ToJson(s.Progress) == before,
                                "Failed actual Rest preserves bridge collision, progress and checkpoint.");
                        }
                        CloseUi();
                        Check(s.Interact(run.restId), "Actual Rest succeeds after the same save lock is released.");
                        Check(s.MumBridges.ActiveCount == 0 && s.Progress.ledger.checkpoint == run.restId && ReadSaved().ledger.checkpoint == run.restId,
                            "Successful real rest saves its checkpoint and clears transient bridges.");
                        Record("rest-committed", s, "Failed write preserved the bridge; successful retry removed it.");
                        PlaceBody(s, run.bankA + Vector3.up * .06f); Advance(4, "PREPARING_OCCUPIED_DEATH_BRIDGE"); return;
                    case 4:
                        if (!Settled() || !s.Walker.Motor.IsLocomotionGrounded) return;
                        Generate(s); Advance(5, "FORMING_OCCUPIED_DEATH_BRIDGE"); return;
                    case 5:
                        if (!Complete(s)) return;
                        PlaceBody(s, run.checkpoint); Advance(6, "RECORDING_PERMANENT_CHECKPOINT_SUPPORT"); return;
                    case 6:
                        if (!Settled() || !s.Walker.Motor.IsLocomotionGrounded) return;
                        run.safeBeforeBridge = s.LastSafeFeet;
                        Check(Vector3.Distance(run.safeBeforeBridge, run.checkpoint) < .35f, "Live terrain session records the actual permanent checkpoint as last safe support.");
                        var bridge = s.MumBridges.Bridges[0];
                        PlaceBody(s, (bridge.Plan.Start + bridge.Plan.End) * .5f + Vector3.up * .04f);
                        Advance(7, "CHECKING_TEMPORARY_SUPPORT_SAVE_EXCLUSION"); return;
                    case 7:
                        if (!Settled() || !s.Walker.Motor.IsLocomotionGrounded) return;
                        Check(s.MumBridges.Bridges[0].Supports(s.Walker.Body), "Actual player CharacterController stands on completed bridge collision.");
                        Check(!s.TrySafeFeet(Feet(s.Walker.Body), out _) && Vector3.Distance(s.LastSafeFeet, run.safeBeforeBridge) < .1f,
                            "Actual session refuses generated bridge as permanent support and retains prior dry last-safe position.");
                        s.Progress.ledger.currency = 137;
                        Check(s.SaveNow(out string saveError), "Actual SaveNow succeeds while standing on the bridge: " + saveError);
                        var saved = ReadSaved();
                        Check(Vector3.Distance(saved.ledger.position, run.safeBeforeBridge) < .1f && saved.ledger.currency == 137 && !MumBridgeUnlock295.HasProof(saved),
                            "Durable save stores prior dry support and currency, without serializing the test unlock.");
                        File.Copy(run.savePath, Path.Combine(Folder, "play-save-on-bridge.json"), true);
                        Record("saved-on-bridge", s, "Save pose remains on permanent checkpoint; player is physically on generated support.");
                        s.Walker.Body.GetComponent<PlayerVitals>().ApplyFatalFall();
                        Advance(8, "WAITING_FOR_ACTUAL_FATAL_FALL_RECOVERY"); return;
                    case 8:
                        if (!Settled() || s.Walker.Body.GetComponent<PlayerVitals>().Hp01 <= 0) return;
                        Check(s.MumBridges.ActiveCount == 0, "Actual fatal-fall death clears an occupied bridge through session/vitals lifecycle.");
                        Check(s.Progress.ledger.currency == 0 && s.Progress.ledger.dropCurrency == 137 && Vector3.Distance(s.Progress.ledger.dropPosition, run.safeBeforeBridge) < .1f,
                            "Actual environmental recovery creates one 137-currency drop on prior permanent dry support.");
                        Check(s.Walker.Body.GetComponent<PlayerVitals>().Hp01 == 1 && Vector3.Distance(Feet(s.Walker.Body), s.Progress.ledger.checkpointPosition) < .4f && !s.Walker.Motor.EnvironmentalInputBlocked,
                            "Actual recovery restores living player/input at saved checkpoint.");
                        Check(s.SaveNow(out string recoveryError), "Recovered actual session saves: " + recoveryError);
                        Check(ReadSaved().ledger.dropCurrency == 137, "Recovery currency drop is durable in isolated store.");
                        Record("fatal-fall-recovered", s, "Occupied bridge cleared; normal saved checkpoint/currency recovery completed.");
                        PlaceBody(s, run.bankA + Vector3.up * .06f); Advance(9, "PREPARING_REALM_REENTRY_BRIDGE"); return;
                    case 9:
                        if (!Settled() || !s.Walker.Motor.IsLocomotionGrounded) return;
                        Generate(s); Advance(10, "FORMING_REALM_REENTRY_BRIDGE"); return;
                    case 10:
                        if (!Complete(s)) return;
                        Check(s.MumBridges.CurrentArea() == run.originArea && s.MumBridges.Bridges[0].AreaId == run.originArea,
                            "Bridge owns the real layout realm returned by the unmodified session callback.");
                        PlaceBody(s, run.away); Advance(11, "WAITING_FOR_LIVE_REALM_DEPARTURE"); return;
                    case 11:
                        if (!Settled()) return;
                        Check(s.MumBridges.CurrentArea() == run.awayArea && s.MumBridges.ActiveCount == 1,
                            "Live service Update retains the bridge when player leaves its real layout realm.");
                        Record("realm-departed", s, run.originArea + " -> " + run.awayArea);
                        PlaceBody(s, run.bankA - Vector3.forward * 1.5f + Vector3.up * .06f);
                        Advance(12, "WAITING_FOR_LIVE_REALM_REENTRY"); return;
                    case 12:
                        if (!Settled()) return;
                        Check(s.MumBridges.CurrentArea() == run.originArea && s.MumBridges.ActiveCount == 0,
                            "Live service Update clears origin realm bridges on reentry without calling NotifyArea or ResetForWorldBoundary from the fixture.");
                        Record("realm-reentered", s, run.awayArea + " -> " + run.originArea);
                        PlaceBody(s, run.bankA + Vector3.up * .06f); Advance(13, "PREPARING_RELOAD_EXCLUSION_BRIDGE"); return;
                    case 13:
                        if (!Settled() || !s.Walker.Motor.IsLocomotionGrounded) return;
                        Generate(s); Advance(14, "FORMING_RELOAD_EXCLUSION_BRIDGE"); return;
                    case 14:
                        if (!Complete(s)) return;
                        PlaceBody(s, run.checkpoint); Advance(15, "SAVING_BEFORE_ACTUAL_PLAY_RESTART"); return;
                    case 15:
                        // Location discovery settles after0.65s and retries its ordinary save every5s.
                        // Let the live arrival system record this permanent checkpoint before comparing a later boot.
                        if (!Settled(6) || !s.Walker.Motor.IsLocomotionGrounded) return;
                        Check(s.MumBridges.ActiveCount == 1, "A complete bridge still exists when preparing the real save/restart boundary.");
                        s.Progress.ledger.currency = 29;
                        Check(s.SaveNow(out string reloadError), "Real pre-restart SaveNow succeeds: " + reloadError);
                        Check(Vector3.Distance(ReadSaved().ledger.position, run.checkpoint) < .35f, "Reload snapshot points to existing dry candidate checkpoint, away from runtime-only fixture banks.");
                        Record("before-play-restart", s, "Active bridge=1; saved currency=29; test unlock exists only in session memory.");
                        Stop(true); return;
                }
            }
            catch (Exception e) { run.failures.Add(e.ToString()); Stop(false); }
        }

        static void VerifyReload(WorldMacroPlaytestSession s)
        {
            var expected = JsonUtility.FromJson<WorldMacroProgress>(run.expectedProgress);
            File.WriteAllText(Path.Combine(Folder,"play-reload-actual.json"),JsonUtility.ToJson(s.Progress,true));
            Check(run.playStarts == 2 && s.LoadStatus == "primary", "Second actual Play start loads the saved isolated primary through session Start.");
            Check(s.Progress.ledger.currency == 29 && s.Progress.ledger.checkpoint == expected.ledger.checkpoint && s.Progress.ledger.dropCurrency == expected.ledger.dropCurrency,
                "Actual reload preserves checkpoint, 29-currency sentinel and previous death drop.");
            Check(JsonUtility.ToJson(s.Progress.campaign) == JsonUtility.ToJson(expected.campaign) && JsonUtility.ToJson(s.Progress.equipment) == JsonUtility.ToJson(expected.equipment),
                "Actual reload preserves campaign and equipment snapshots.");
            Check(Vector3.Distance(Feet(s.Walker.Body), expected.ledger.position) < .4f && s.TrySafeFeet(expected.ledger.position, out _),
                "Actual reload restores the player on permanent dry candidate support.");
            Check(s.MumBridges.ActiveCount == 0 && Object.FindObjectsByType<MumBridgeBody>(FindObjectsSortMode.None).Length == 0 && GameObject.Find(BanksName) == null,
                "Actual scene restart restores no generated bridge, collider or runtime fixture bank.");
            Check(!s.MumBridges.IsUnlocked && !s.HasMumBridge && !MumBridgeUnlock295.HasProof(s.Progress),
                "Test unlock does not survive save/reload and normal late progression stays locked.");
            Record("second-play-restored", s, "Real store reload retained progression but no generated support or editor test unlock.");
            run.finished = true; Stop(false);
        }

        static string ActualStorePath(WorldMacroPlaytestSession s)
        {
            object store = typeof(WorldMacroPlaytestSession).GetField("store", Private).GetValue(s);
            return (string)store.GetType().GetField("path", Private).GetValue(store);
        }
        static WorldMacroProgress ReadSaved()
        {
            var state = JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(run.savePath));
            if (!WorldMacroProgress.Valid(state)) throw new InvalidDataException("Isolated durable save is invalid.");
            return state;
        }
        static void CloseUi()
        {
            var ui = PlaytestUiRoot.Instance;
            if (ui != null) { ui.CloseMenu(); ui.Gate.ReleaseImmediately(); }
        }
        static void PlaceBody(WorldMacroPlaytestSession s, Vector3 feet)
        {
            // Setup only: do not invoke Teleport, because its bridge-reset hook would hide a broken rest/reentry test.
            var body = s.Walker.Body; body.enabled = false;
            body.transform.position += feet - Feet(body);
            body.enabled = true; s.Walker.Motor.ResetMotion();
            typeof(WorldMacroPlaytestSession).GetMethod("ResetTraversal", Private).Invoke(s, new object[] { feet });
            Physics.SyncTransforms(); body.Move(Vector3.down * .015f);
        }
        static void Generate(WorldMacroPlaytestSession s)
        {
            Check(s.MumBridges.ActiveCount == 0 && s.MumBridges.IsUnlocked && !s.GameplayInputBlocked,
                "Live session is ready for lifecycle setup bridge (phase " + run.phase + ").");
            var cast = new SpellCast('뭄', SpellKind.Field, Element.Earth, 0, default, 1);
            Check(s.MumBridges.TryPrepareTo(cast, run.bankB, out string reason), "Actual bound service accepts physical bank setup: " + reason);
            Check(s.MumBridges.CommitPrepared(), "Actual bound service commits lifecycle setup bridge.");
        }
        static Vector3 Feet(CharacterController body) => body.transform.TransformPoint(body.center) - Vector3.up * body.height * .5f;
        static bool Complete(WorldMacroPlaytestSession s) => s.MumBridges.ActiveCount == 1 && s.MumBridges.Bridges[0].IsComplete && s.MumBridges.Bridges[0].Support.enabled;
        static void SelectRest(WorldMacroPlaytestSession s)
        {
            var points = s.Content.Points.Where(p => p != null && p.Kind == PrologueInteractionKind.Rest && p.Radius > 0 &&
                (s.InnRestPresentation == null || s.InnRestPresentation.PointId != p.Id) &&
                (s.VillageRestPresentation == null || s.VillageRestPresentation.PointId != p.Id))
                .OrderBy(p => Vector3.SqrMagnitude(p.Position - s.Walker.Body.transform.position));
            foreach (var point in points)
            {
                if (!WorldMacroCheckpointRules.TryResolve(s.Content, s.Progress, point.Id, out var checkpoint) || !s.TrySafeFeet(checkpoint.Feet, out var feet)) continue;
                foreach (float radius in new[] { 0f, .75f, 1.4f })
                    for (int i = 0; i < 8; i++)
                    {
                        var proposed = point.Position + new Vector3(Mathf.Cos(i * Mathf.PI / 4), 0, Mathf.Sin(i * Mathf.PI / 4)) * Mathf.Min(radius, point.Radius * .6f);
                        if (!s.TrySafeFeet(proposed, out var approach)) continue;
                        PlaceBody(s, approach);
                        if (!s.CanInteract(point.Id)) continue;
                        run.restId = point.Id; run.checkpoint = feet; run.approach = approach;
                        Check(true, "Resolved real non-doorway rest, valid permanent checkpoint and interactable approach: " + point.Id); return;
                    }
            }
            throw new InvalidOperationException("No configured non-doorway rest has both safe checkpoint and actual interaction approach.");
        }
        static void SetupBanks(WorldMacroPlaytestSession s)
        {
            if (s.MountainLayout == null || s.MountainLayout.FinalSurface == null) throw new InvalidOperationException("Candidate final surface/realm data is missing.");
            var field = new CompactWorldSurface(s.MountainLayout);
            var area = s.MountainLayout.RealmAt(new Vector2(run.checkpoint.x, run.checkpoint.z));
            if (area == null) throw new InvalidOperationException("Selected rest is outside the authored realms.");
            run.originArea = area.Id;
            var centre = new Vector2(run.checkpoint.x + 24, run.checkpoint.z + 24);
            if (s.MountainLayout.RealmAt(centre)?.Id != area.Id || s.MountainLayout.RealmAt(centre + Vector2.up * 12)?.Id != area.Id)
                centre = area.Centre;
            float top = Mathf.Max(field.Sample(centre.x, centre.y), field.Sample(centre.x, centre.y + 12)) + 40;
            run.bankA = new Vector3(centre.x, top, centre.y); run.bankB = run.bankA + Vector3.forward * 10;
            var other = s.MountainLayout.Realms.FirstOrDefault(r => r.Id != area.Id && s.MountainLayout.RealmAt(r.Centre)?.Id == r.Id);
            if (other == null) throw new InvalidOperationException("A second real authored realm is required.");
            run.awayArea = other.Id;
            run.away = new Vector3(other.Centre.x, field.Sample(other.Centre.x, other.Centre.y) + 40, other.Centre.y) + Vector3.up * .06f;
            var root = new GameObject(BanksName); SceneManager.MoveGameObjectToScene(root, s.gameObject.scene);
            void Pad(string label, Vector3 topCenter, Vector3 size)
            {
                var go = new GameObject(label); go.transform.SetParent(root.transform, false);
                go.transform.position = topCenter - Vector3.up * size.y * .5f; go.AddComponent<BoxCollider>().size = size;
            }
            Pad("NearBank", run.bankA - Vector3.forward * 2.5f, new Vector3(8, 1, 6));
            Pad("FarBank", run.bankB + Vector3.forward * 2.5f, new Vector3(8, 1, 6));
            Pad("OtherRealm", run.away - Vector3.up * .06f, new Vector3(8, 1, 8));
            Physics.SyncTransforms();
            Check(s.MountainLayout.RealmAt(new Vector2(run.bankA.x, run.bankA.z))?.Id == run.originArea,
                "Runtime-only bank fixture is in the same real realm as the checkpoint; other bank uses " + run.awayArea + ".");
        }
    }
}
