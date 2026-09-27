using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Vehicle;
using Oheangbu.App.World.Dressing;
using Oheangbu.Data.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroCompactAuthoring
    {
        public static string RuntimeSmokeStart() => WorldMacroCompactRuntimeSmoke.Start();
        public static string RuntimeSmokeStatus() => WorldMacroCompactRuntimeSmoke.Status();
        public static string RuntimeSmokeStop() => WorldMacroCompactRuntimeSmoke.Stop();
    }

    /// <summary>Passive isolated-save startup observation; never synthesizes input or advances gameplay.</summary>
    [InitializeOnLoad]
    public static class WorldMacroCompactRuntimeSmoke
    {
        const string StateKey = "WorldMacroCompactRuntimeSmoke.State";
        const string UiSuffixKey = "PlaytestUiReviewSuffix";
        const string MissingKey = "__compact_smoke_key_was_missing__";
        const string ScenePath = "Assets/_Project/Scenes/World/W_Demo_Compact.unity";
        const string SourceScene = "Assets/_Project/Scenes/World/W_Demo_Campaign.unity";
        const string Folder = "Assets/_Project/Art/World/WorldCompact/";
        const double ObserveSeconds = 35, WarmupSeconds = 3;
        static string Output => WorldMacroCompactAuthoring.Output;
        static string ReportPath => Path.Combine(Output, "runtime_lifecycle_smoke.json");
        [Serializable] sealed class Gate
        {
            public string phase, status, stage, sourceHash, sourceSceneHash, mappingHash, gradeHash, reliefHash, fingerprint, fitHash, candidateHash;
            public int next, nextTerrain, publishedCells;
            public string[] terrain;
            public bool published, ready, sourceSceneUnchanged, originalAssetsUnchanged, mapTexturesUnchanged;
            public CorridorMapRow[] roads;
        }
        [Serializable] sealed class CorridorMapRow { public string id; public int mapPoints; }
        [Serializable] sealed class SaveStamp { public string path, hash; public bool existed; }
        [Serializable] sealed class LogRecord { public string type, message, stack; public int frame; }
        [Serializable] sealed class Health
        {
            public string activeScene, content, worldSheet, uiContent, mapData, saveSlot, actualSuffix, loadStatus, saveError, uiError, uiPage, checkpoint, referenceError;
            public bool session, progressValid, saveBlocked, walker, motor, controller, camera, cameraRig, ui, uiSessionMatches, mapUsable, mapPresenter, mapMiniRoot, worldReferenceMatches, compactReferences, suffixMatches;
            public int actors, actorsWithAgent, actorsBoundToNavMesh, nullActors, uniqueActorIds, completedCount, defeatedCount;
            public bool gameplayInputBlocked, bodyGrounded, seated;
            public float timeScale;
            public Vector3 playerFeet;
        }
        [Serializable] sealed class Run
        {
            public string status, phase, stopReason, suffix, savePath, priorSessionSuffix, priorUiSuffix, sourceHash, compactSceneHash, startedUtc, finishedUtc;
            public string scope = "Actual passive Play startup only, a fresh UUID save namespace, and at most 35 seconds of editor-observed unscaled frame durations. No native input, walking, vehicle driving, pickups, combat actions, save/reload interaction, build, screenshots or video are performed.";
            public string performanceScope = "Editor Play observed unscaledDeltaTime samples after a 3-second warmup; includes editor overhead, may omit unobserved frames, and does not measure GPU or standalone-player performance.";
            public bool active, stopping, enteredPlay, suffixRestored, sourceSceneUnchanged, compactSceneFileUnchanged, existingSavesUnchanged, saveNamespaceIsolated, sceneDirtyBefore, sceneDirtyAfter;
            public bool startupVerified, boundedObservationCompleted, nativeInputVerified = false, gpuPerformanceVerified = false, nativeInputObserved, gameplayStateChanged;
            public int lastFrame = -1, observedFrames, skippedFrames, loggedErrors, loggedWarnings, initialCompleted = -1, initialDefeated = -1;
            public double enteringDeadline, playStart, nextPersist, nextGuard, nextHealth;
            public float meanFrameMilliseconds, p95FrameMilliseconds, maximumFrameMilliseconds, maximumPlayerDisplacement;
            public Vector3 firstObservedFeet;
            public Health health;
            public InkPaintingSavedStudyReferenceGuard.Snapshot savedReferenceProof;
            public List<float> frameMilliseconds = new List<float>();
            public List<SaveStamp> protectedSaves = new List<SaveStamp>();
            public List<LogRecord> logs = new List<LogRecord>();
            public List<string> findings = new List<string>();
        }
        static Run run;
        static bool persisting;
        static WorldMacroCompactRuntimeSmoke()
        {
            string state = SessionState.GetString(StateKey, "");
            if (!string.IsNullOrEmpty(state)) run = JsonUtility.FromJson<Run>(state);
            EditorApplication.playModeStateChanged += PlayChanged;
            EditorApplication.quitting += Quitting;
            if (run != null && (run.active || run.stopping)) { Subscribe(); EditorApplication.delayCall += ResumeAfterReload; }
        }
        static void Subscribe()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= Log; Application.logMessageReceived += Log;
        }
        static void Unsubscribe() { EditorApplication.update -= Tick; Application.logMessageReceived -= Log; }
        static string Hash(string path)
        { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        static Gate ReadGate(string file)
        {
            string path = Path.Combine(Output, file); if (!File.Exists(path)) throw new InvalidOperationException("Required completion receipt missing: " + file);
            return JsonUtility.FromJson<Gate>(File.ReadAllText(path));
        }
        static T[] SceneComponents<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
        static string Persist()
        {
            if (run == null) return "NOT_STARTED";
            string json = JsonUtility.ToJson(run, true); SessionState.SetString(StateKey, json);
            if (persisting) return json;
            try
            {
                persisting = true;
                Directory.CreateDirectory(Output);
                string temporary = ReportPath + ".tmp";
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        File.WriteAllText(temporary, json);
                        if (File.Exists(ReportPath)) File.Replace(temporary, ReportPath, null);
                        else File.Move(temporary, ReportPath);
                        break;
                    }
                    catch (IOException) when (attempt < 2)
                    {
                        // A report reader can briefly hold the destination during an atomic replacement.
                        System.Threading.Thread.Sleep(attempt == 0 ? 10 : 25);
                    }
                }
            }
            finally { persisting = false; }
            return json;
        }
        static void Finding(string value) { if (!run.findings.Contains(value)) run.findings.Add(value); }
        public static string Start()
        {
            if (run != null && (run.active || run.stopping)) throw new InvalidOperationException("Compact smoke already active; stop it before starting another.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || SceneManager.GetActiveScene().path != ScenePath || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Start requires the saved compact scene in idle Edit Mode.");
            if (EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0)
                throw new InvalidOperationException("Passive smoke requires Unity scene reload enabled so runtime world state is discarded on exit; no editor settings were changed.");
            if (SceneManager.sceneCount != 1) throw new InvalidOperationException("Passive smoke requires only the compact scene loaded, so runtime discovery cannot bind an additive original scene.");
            if (Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("System commit >=85%; Play startup was not requested.");
            var conversion = ReadGate("progress.json"); var grade = ReadGate("grade_progress.json"); var attachments = ReadGate("final_grade_attachments.json"); var dressing = ReadGate("dressing_progress.json"); var nav = ReadGate("navigation_progress.json");
            string sourceHash = Hash(SourceScene), mappingHash = Hash(Folder + "Compression.asset"), gradeHash = Hash(Folder + "RoadGrade.asset");
            if (conversion.sourceHash != sourceHash || grade.phase != "GRADED_TERRAIN_READY_FOR_PHYSICAL_AUDIT" && grade.phase != "GRADED_GEOMETRY_READY_FOR_PHYSICAL_AUDIT" || grade.terrain == null || grade.nextTerrain != grade.terrain.Length)
                throw new InvalidOperationException("Original identity or completed terrain grade guard failed.");
            if ((attachments.status != "FINAL_GRADE_ATTACHMENTS_REBUILT_FROM_ORIGINAL" && attachments.status != "FINAL_GRADE_ATTACHMENT_FINDINGS") || !attachments.sourceSceneUnchanged || !attachments.originalAssetsUnchanged || attachments.sourceSceneHash != sourceHash || attachments.mappingHash != mappingHash || attachments.gradeHash != gradeHash || (attachments.reliefHash ?? "") != (grade.reliefHash ?? ""))
                throw new InvalidOperationException("Attachments must be finalized from the current source/map/grade before Play.");
            if (!dressing.published || dressing.stage != "Complete" || dressing.publishedCells <= 0 || dressing.fingerprint == null ||
                !dressing.fingerprint.StartsWith("compact-physics-v2-fine2m-check1m:", StringComparison.Ordinal) || nav.status != "BAKED" || nav.next != 20 || nav.mappingHash != mappingHash)
                throw new InvalidOperationException("Fine-v2 dressing publication and all 20 private navigation bakes must finish before Play; the coarse-v1 baseline is preliminary.");
            bool installedStudyNavigation = InkPaintingSavedStudyPlay.HasInstalledNavigationGeneration;
            if (installedStudyNavigation)
                InkPaintingNavigationUpdate.RequireCurrentBake();
            else WorldMacroCompactNavigation.RequireCurrentBake();
            var session = SceneComponents<WorldMacroPlaytestSession>().Single(); var ui = SceneComponents<PlaytestUiRoot>().Single();
            var summon = SceneComponents<WorldMacroPalanquinSummon>().Single(); var renderer = SceneComponents<WorldMacroDressingRenderer>().Single();
            bool Owned(Object o) => o != null && AssetDatabase.GetAssetPath(o).StartsWith(Folder, StringComparison.Ordinal);
            var savedReferenceProof = installedStudyNavigation ? InkPaintingSavedStudyReferenceGuard.Capture(dressing.publishedCells) : null;
            if (!installedStudyNavigation && (!Owned(session.Content) || !Owned(summon.WorldSheet) || ui.Content != session.Content || ui.WorldSheet != summon.WorldSheet || !Owned(ui.MapData) || !ui.MapData.IsUsable ||
                renderer.Sheet == null || !Owned(renderer.Sheet) || renderer.Sheet.Geography != summon.WorldSheet || renderer.Sheet.SourceFingerprint != dressing.fingerprint || renderer.Sheet.Cells.Length != dressing.publishedCells))
                throw new InvalidOperationException("Compact content/world/map/dressing references do not match the completed private outputs.");
            if (File.Exists(Path.Combine(Output, "route_corridor_local_candidate.json")))
            {
                var corridor = ReadGate("corridor_attachments.json");
                if (corridor.status != "APPLIED_REQUIRES_GROUNDING_AND_PHYSICS" || corridor.sourceSceneHash != sourceHash || corridor.mappingHash != mappingHash ||
                    corridor.gradeHash != gradeHash || corridor.fitHash != grade.fitHash || !corridor.sourceSceneUnchanged || !corridor.originalAssetsUnchanged || !corridor.mapTexturesUnchanged ||
                    corridor.candidateHash != Hash(Path.Combine(Output, "route_corridor_local_candidate.json")) || corridor.roads == null || corridor.roads.Length != 2)
                    throw new InvalidOperationException("Local corridor receipt must match current original/map/grade/fit before Play.");
                foreach (var row in corridor.roads)
                {
                    var line = ui.MapData.Lines.Single(l => l.Id == row.id); var route = summon.WorldSheet.Routes.Single(r => r.Id == row.id);
                    if (line.Points.Length != row.mapPoints || line.Points.Length != route.Points.Length ||
                        line.Points.Where((p, i) => Vector2.Distance(p, new Vector2(route.Points[i].x, route.Points[i].z)) > .015f).Any())
                        throw new InvalidOperationException("Local corridor map vectors differ from the current route: " + row.id);
                }
            }
            if (session.Content.SaveSlot == null || !session.Content.SaveSlot.StartsWith("world-demo-compact-", StringComparison.Ordinal) || session.Content.SaveSlot.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidOperationException("A distinct valid compact SaveSlot is required.");
            var surfaces = SceneComponents<NavMeshSurface>();
            // The installed study gate above verifies every exact generation binding,
            // settings and asset hash. Legacy scenes retain their original folder guard.
            if (surfaces.Length != 20 || surfaces.Any(s => s.navMeshData == null) ||
                (!installedStudyNavigation && surfaces.Any(s => !AssetDatabase.GetAssetPath(s.navMeshData).StartsWith(Folder + "Navigation/", StringComparison.Ordinal))))
                throw new InvalidOperationException("All 20 actual surfaces must reference their validated compact navigation generation.");
            string suffix = "_compact_smoke_" + Guid.NewGuid().ToString("N"); string savePath = Path.Combine(Application.persistentDataPath, session.Content.SaveSlot + suffix + ".json");
            if (Directory.Exists(Application.persistentDataPath) && Directory.GetFiles(Application.persistentDataPath).Any(p => p.StartsWith(savePath, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Diagnostic save path unexpectedly exists.");
            run = new Run { active = true, status = "ENTERING_PLAY", phase = "isolated passive startup", suffix = suffix, savePath = savePath, priorSessionSuffix = session.TestSaveSuffix,
                priorUiSuffix = SessionState.GetString(UiSuffixKey, MissingKey), sourceHash = sourceHash, compactSceneHash = Hash(ScenePath), startedUtc = DateTime.UtcNow.ToString("o"), sceneDirtyBefore = false,
                enteringDeadline = EditorApplication.timeSinceStartup + 120, nextGuard = EditorApplication.timeSinceStartup + 1, saveNamespaceIsolated = true, savedReferenceProof = savedReferenceProof };
            if (Directory.Exists(Application.persistentDataPath)) foreach (string path in Directory.GetFiles(Application.persistentDataPath, "*.json*", SearchOption.TopDirectoryOnly)) run.protectedSaves.Add(new SaveStamp { path = path, hash = Hash(path), existed = true });
            session.TestSaveSuffix = suffix; SessionState.SetString(UiSuffixKey, suffix); Subscribe(); Persist();
            try { EditorApplication.EnterPlaymode(); }
            catch { RequestStop("Play entry failed"); throw; }
            return Persist();
        }
        static void ResumeAfterReload()
        {
            if (run == null || !(run.active || run.stopping)) return;
            if (run.stopping && !EditorApplication.isPlayingOrWillChangePlaymode) Cleanup();
            else if (run.active && EditorApplication.isPlaying && !run.enteredPlay) Entered();
        }
        static void Entered()
        {
            if (run.enteredPlay) return;
            run.enteredPlay = true; run.playStart = EditorApplication.timeSinceStartup; run.status = "OBSERVING_PLAY"; run.phase = "passive startup and bounded frame observation"; run.lastFrame = -1; Persist();
        }
        static void PlayChanged(PlayModeStateChange state)
        {
            if (run == null || !(run.active || run.stopping)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) Entered();
            if (state == PlayModeStateChange.ExitingPlayMode) { if (!run.stopping) { run.stopReason = "Play exited externally"; run.stopping = true; run.active = false; } Persist(); }
            if (state == PlayModeStateChange.EnteredEditMode) Cleanup();
        }
        static void Log(string message, string stack, LogType type)
        {
            if (run == null || !run.active || persisting || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert && type != LogType.Warning)) return;
            if (type == LogType.Warning) run.loggedWarnings++; else run.loggedErrors++;
            if (run.logs.Count < 80) run.logs.Add(new LogRecord { type = type.ToString(), message = message.Length > 4000 ? message.Substring(0, 4000) : message, stack = stack != null && stack.Length > 6000 ? stack.Substring(0, 6000) : stack, frame = Time.frameCount });
            SessionState.SetString(StateKey, JsonUtility.ToJson(run));
        }
        static Health ObserveHealth()
        {
            var h = new Health { activeScene = SceneManager.GetActiveScene().path, timeScale = Time.timeScale };
            var session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>(); var ui = PlaytestUiRoot.Instance;
            var summon = Object.FindFirstObjectByType<WorldMacroPalanquinSummon>(); h.session = session != null;
            if (session == null) return h;
            h.content = AssetDatabase.GetAssetPath(session.Content); h.saveSlot = session.Content?.SaveSlot; h.actualSuffix = session.TestSaveSuffix; h.loadStatus = session.LoadStatus; h.saveError = session.SaveError; h.saveBlocked = session.SaveBlocked;
            h.progressValid = WorldMacroProgress.Valid(session.Progress); h.gameplayInputBlocked = session.GameplayInputBlocked;
            h.suffixMatches = session.TestSaveSuffix == run.suffix && SessionState.GetString(UiSuffixKey, MissingKey) == run.suffix && session.Content != null && Path.Combine(Application.persistentDataPath, session.Content.SaveSlot + session.TestSaveSuffix + ".json") == run.savePath;
            if (session.Progress != null) { h.checkpoint = session.Progress.ledger?.checkpoint; h.completedCount = session.Progress.ledger?.completed?.Count ?? 0; h.defeatedCount = session.Progress.defeated?.Count ?? 0; }
            var walker = session.Walker; h.walker = walker != null && walker.isActiveAndEnabled;
            if (walker != null) { h.motor = walker.Motor != null && walker.Motor.isActiveAndEnabled; h.controller = walker.Body != null && walker.Body.enabled; h.camera = walker.ViewCamera != null && walker.ViewCamera.isActiveAndEnabled; h.cameraRig = walker.CameraRig != null && walker.CameraRig.isActiveAndEnabled; h.seated = walker.Seated; if (walker.Body != null) { h.bodyGrounded = walker.Body.isGrounded; h.playerFeet = walker.Body.transform.position; } }
            var actors = session.Actors; h.actors = actors?.Length ?? 0; h.nullActors = actors?.Count(a => a == null) ?? 0; h.uniqueActorIds = actors?.Where(a => a != null).Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() ?? 0;
            if (actors != null) foreach (var actor in actors.Where(a => a != null)) { var agent = actor.GetComponent<NavMeshAgent>(); if (agent != null) { h.actorsWithAgent++; if (agent.enabled && agent.isOnNavMesh) h.actorsBoundToNavMesh++; } }
            h.ui = ui != null && ui.isActiveAndEnabled;
            if (ui != null) { h.uiContent = AssetDatabase.GetAssetPath(ui.Content); h.mapData = AssetDatabase.GetAssetPath(ui.MapData); h.uiSessionMatches = ui.Session == session; h.mapUsable = ui.MapData != null && ui.MapData.IsUsable; h.mapPresenter = ui.Map != null; h.mapMiniRoot = ui.Map != null && ui.Map.MiniRoot != null; h.uiError = ui.LastError; h.uiPage = ui.Page; h.worldSheet = AssetDatabase.GetAssetPath(ui.WorldSheet); h.worldReferenceMatches = summon != null && summon.WorldSheet == ui.WorldSheet; h.compactReferences = ui.Content == session.Content && h.content.StartsWith(Folder, StringComparison.Ordinal) && h.worldSheet.StartsWith(Folder, StringComparison.Ordinal) && h.mapData.StartsWith(Folder, StringComparison.Ordinal); }
            if (run.savedReferenceProof != null)
                h.compactReferences = InkPaintingSavedStudyReferenceGuard.Matches(run.savedReferenceProof, out h.referenceError);
            return h;
        }
        static bool Healthy(Health h) => h != null && h.activeScene == ScenePath && h.session && h.progressValid && h.suffixMatches && !h.saveBlocked && string.IsNullOrEmpty(h.saveError) &&
            h.walker && h.motor && h.controller && h.camera && h.cameraRig && h.ui && h.uiSessionMatches && h.mapUsable && h.mapPresenter && h.mapMiniRoot && h.worldReferenceMatches && h.compactReferences && string.IsNullOrEmpty(h.uiError) && h.actors == 37 && h.uniqueActorIds == 37 && h.nullActors == 0 && h.actorsWithAgent == 37 && h.actorsBoundToNavMesh == 37;
        static void Tick()
        {
            if (run == null) { Unsubscribe(); return; }
            if (run.stopping) { if (!EditorApplication.isPlayingOrWillChangePlaymode) Cleanup(); return; }
            if (!run.active) { Unsubscribe(); return; }
            double now = EditorApplication.timeSinceStartup;
            try
            {
                if (!EditorApplication.isPlaying)
                { if (now >= run.enteringDeadline) { Finding("Play startup did not complete within its 120-second entry guard."); RequestStop("entry timeout"); } return; }
                if (!run.enteredPlay) Entered();
                if (now >= run.nextGuard) { run.nextGuard = now + 1; if (Prologue.PrologueAudit.CommitRatio() >= .85f) { Finding("System commit reached 85% during passive startup."); RequestStop("memory guard"); return; } }
                if (run.health == null || now >= run.nextHealth) { run.health = ObserveHealth(); run.nextHealth = now + .5; }
                if (run.health.session && !run.health.suffixMatches) { Finding("Diagnostic save namespace mismatch observed in actual Play session."); run.saveNamespaceIsolated = false; RequestStop("save namespace mismatch"); return; }
                if (run.loggedErrors > 0) { Finding("Actual Play emitted startup/runtime errors; inspect bounded logs."); RequestStop("runtime error"); return; }
                if (Healthy(run.health)) run.startupVerified = true;
                double elapsed = now - run.playStart;
                if (elapsed >= 12 && !run.startupVerified) { Finding("Actual Play startup health did not become valid within 12 seconds."); RequestStop("startup health timeout"); return; }
                if (elapsed >= WarmupSeconds && Time.frameCount != run.lastFrame)
                {
                    if (run.lastFrame >= 0) run.skippedFrames += Math.Max(0, Time.frameCount - run.lastFrame - 1);
                    run.lastFrame = Time.frameCount; run.observedFrames++;
                    float ms = Time.unscaledDeltaTime * 1000; if (!float.IsNaN(ms) && !float.IsInfinity(ms) && ms > 0 && run.frameMilliseconds.Count < 20000) run.frameMilliseconds.Add(ms);
                    bool input = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame || Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame || Mouse.current.delta.ReadValue().sqrMagnitude > .01f);
                    run.nativeInputObserved |= input;
                    if (run.initialCompleted < 0) { run.initialCompleted = run.health.completedCount; run.initialDefeated = run.health.defeatedCount; run.firstObservedFeet = run.health.playerFeet; }
                    else { run.maximumPlayerDisplacement = Mathf.Max(run.maximumPlayerDisplacement, Vector3.Distance(run.firstObservedFeet, run.health.playerFeet)); run.gameplayStateChanged |= run.initialCompleted != run.health.completedCount || run.initialDefeated != run.health.defeatedCount; }
                }
                if (elapsed >= ObserveSeconds) { run.boundedObservationCompleted = true; RequestStop("35-second passive observation complete"); return; }
                if (now >= run.nextPersist) { run.nextPersist = now + 1; Persist(); }
            }
            catch (Exception error) { Finding("Smoke observer failed: " + error); RequestStop("observer exception"); }
        }
        static void RequestStop(string reason)
        {
            if (run == null) return; run.stopReason = reason; run.active = false; run.stopping = true; run.phase = "exiting Play and restoring temporary suffixes"; run.status = "STOPPING"; Persist();
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode(); else Cleanup();
        }
        static void Cleanup()
        {
            if (run == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (run.priorUiSuffix == MissingKey) SessionState.EraseString(UiSuffixKey); else SessionState.SetString(UiSuffixKey, run.priorUiSuffix ?? "");
            var sessions = SceneComponents<WorldMacroPlaytestSession>();
            if (SceneManager.GetActiveScene().path == ScenePath && sessions.Length == 1) sessions[0].TestSaveSuffix = run.priorSessionSuffix ?? "";
            else Finding("Compact edit session was not available for authored suffix restoration.");
            run.suffixRestored = SessionState.GetString(UiSuffixKey, MissingKey) == run.priorUiSuffix && sessions.Length == 1 && sessions[0].TestSaveSuffix == (run.priorSessionSuffix ?? "");
            run.sourceSceneUnchanged = File.Exists(SourceScene) && Hash(SourceScene) == run.sourceHash; run.compactSceneFileUnchanged = File.Exists(ScenePath) && Hash(ScenePath) == run.compactSceneHash;
            run.existingSavesUnchanged = true;
            foreach (var stamp in run.protectedSaves) if (!File.Exists(stamp.path) || Hash(stamp.path) != stamp.hash) { run.existingSavesUnchanged = false; Finding("Existing save/settings file changed: " + stamp.path); }
            if (Directory.Exists(Application.persistentDataPath)) foreach (string path in Directory.GetFiles(Application.persistentDataPath, "*.json*", SearchOption.TopDirectoryOnly))
                if (!path.StartsWith(run.savePath, StringComparison.OrdinalIgnoreCase) && run.protectedSaves.All(s => !string.Equals(s.path, path, StringComparison.OrdinalIgnoreCase))) { run.existingSavesUnchanged = false; Finding("Unexpected new non-diagnostic save/settings file: " + path); }
            run.sceneDirtyAfter = SceneManager.GetActiveScene().isDirty;
            if (run.sceneDirtyAfter != run.sceneDirtyBefore) Finding("Edit scene dirty state changed; no automatic save or dirty-state clearing performed.");
            if (!run.sourceSceneUnchanged || !run.compactSceneFileUnchanged || !run.suffixRestored) Finding("Source/compact scene hash or temporary suffix restoration check failed.");
            UpdateStatistics();
            if (!run.startupVerified) Finding("Runtime startup health remains unverified.");
            else if (!Healthy(run.health)) Finding("Previously valid startup health was not valid in the last actual Play snapshot.");
            run.active = run.stopping = false; run.phase = "returned to Edit Mode"; run.finishedUtc = DateTime.UtcNow.ToString("o");
            run.status = run.findings.Count == 0 && run.loggedErrors == 0 ? "PASS_PASSIVE_PLAY_STARTUP_NATIVE_INPUT_UNVERIFIED" : "RUNTIME_SMOKE_FINDINGS";
            Persist(); Unsubscribe();
        }
        static void UpdateStatistics()
        {
            if (run?.frameMilliseconds == null || run.frameMilliseconds.Count == 0) return;
            var values = run.frameMilliseconds.OrderBy(v => v).ToArray(); run.meanFrameMilliseconds = values.Average(); run.maximumFrameMilliseconds = values[values.Length - 1]; run.p95FrameMilliseconds = values[Mathf.Clamp(Mathf.CeilToInt(values.Length * .95f) - 1, 0, values.Length - 1)];
        }
        static void Quitting()
        {
            if (run == null || !(run.active || run.stopping)) return;
            Finding("Editor quit before normal compact smoke teardown completed."); run.active = false; run.stopping = true;
            if (run.priorUiSuffix == MissingKey) SessionState.EraseString(UiSuffixKey); else SessionState.SetString(UiSuffixKey, run.priorUiSuffix ?? ""); Persist();
        }
        public static string Status()
        {
            if (run == null) return File.Exists(ReportPath) ? File.ReadAllText(ReportPath) : JsonUtility.ToJson(new Run { status = "NOT_STARTED" }, true);
            if (run.active && EditorApplication.isPlaying) { run.health = ObserveHealth(); UpdateStatistics(); Persist(); }
            return JsonUtility.ToJson(run, true);
        }
        public static string Stop()
        {
            if (run == null || !(run.active || run.stopping)) return Status();
            RequestStop("explicit stop"); return JsonUtility.ToJson(run, true);
        }
    }
}
