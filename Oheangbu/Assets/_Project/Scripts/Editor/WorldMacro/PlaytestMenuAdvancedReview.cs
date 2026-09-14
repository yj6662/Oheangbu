using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Vehicle;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Staged opt-in vehicle, settings rollback, and map-cycle review. It never enters Play Mode.</summary>
    [InitializeOnLoad]
    public static class PlaytestMenuAdvancedReview
    {
        const double TimeoutSeconds = 90d;
        const int MapCycles = 20;

        [Serializable] sealed class Check { public string id, status, detail; }
        [Serializable] sealed class Report
        {
            public string utc, status, saveIsolationSuffix;
            public string scope = "Editor Play Mode QA in a suffixed save. Seat boarding/exit and menu opening are diagnostic setup APIs; W/E/V/Space are injected InputSystem events, not native device input.";
            public int elapsedFrames, mapCycles;
            public int objectsBefore, objectsAfter, objectDelta, texturesBefore, texturesAfter, textureDelta;
            public long allocatedBefore, allocatedAfter, allocatedDelta, reservedBefore, reservedAfter, reservedDelta;
            public long mapTickSamplesBefore, mapTickSamplesAfter;
            public double mapLastTickMilliseconds, mapMeanTickMilliseconds;
            public string mapResourceMeasurement;
            public string focusGateInputReleaseEvidence;
            public Check[] checks = Array.Empty<Check>();
            public string[] failures = Array.Empty<string>();
            public string[] unverified = { "Native keyboard/controller device behavior.", "Human visual approval and GPU/device performance." };
        }

        enum Phase { SeatSetup, SeatPause, SeatInput, SeatRelease, SeatExit, SettingsStart, SettingsWait, SettingsClose, MapOpen, MapOpened, MapClose, MapClosed, Finish }
        static bool running;
        static Phase phase;
        static double deadline, phaseAt, previewAt;
        static int startFrame, lastFrame, cycles;
        static PlaytestUiRoot root;
        static WorldMacroPlaytestSession session;
        static WorldMacroPalanquinSeat seat;
        static WorldMacroPalanquinController vehicle;
        static Vector3 playerFeet, vehiclePosition, pausedVehiclePosition;
        static Quaternion vehicleRotation;
        static float playerYaw;
        static Vector3 vehicleVelocity, vehicleAngularVelocity;
        static bool combatActive, seatedView;
        static UserSettingsData originalSettings;
        static byte[] settingsBytes;
        static bool settingsFileExisted;
        static int objectsBefore, texturesBefore;
        static long allocatedBefore, reservedBefore, ticksBefore;
        static bool mapBaselineMeasured;
        static bool applicationUnfocusedObserved;
        static readonly List<Check> Checks = new List<Check>();
        static readonly List<string> Failures = new List<string>();

        static PlaytestMenuAdvancedReview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Abort;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingPlayMode) Abort(); };
        }

        public static string Execute(string action)
        {
            switch ((action ?? "").Trim().ToLowerInvariant())
            {
                case "begin": return Begin();
                case "poll": return Poll();
                default: throw new ArgumentException("Expected begin or poll.");
            }
        }

        static string Begin()
        {
            Need(EditorApplication.isPlaying && !EditorApplication.isPaused, "An unpaused Play Mode session is required.");
            Need(!running, "Advanced review is already running.");
            Need(SceneManager.GetActiveScene().path == WorldMacroPlaytestAuthoring.ScenePath, "Advanced review requires W_WorldMacro_Playtest.");
            root = PlaytestUiRoot.Instance ?? Object.FindFirstObjectByType<PlaytestUiRoot>();
            session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            seat = Object.FindFirstObjectByType<WorldMacroPalanquinSeat>();
            Need(root != null && session != null && root.Session == session && seat != null && seat.Vehicle != null, "Menu/session/vehicle runtime wiring is incomplete.");
            string suffix = SessionState.GetString(PlaytestMenuReview.ReviewSuffixKey, "");
            Need(suffix.Length > 0 && session.TestSaveSuffix == suffix, "Advanced review requires the active suffixed QA save.");
            Need(session.Progress != null && Keyboard.current != null, "Session or Input System keyboard is not ready.");
            vehicle = seat.Vehicle;
            playerFeet = session.Walker.Body.transform.position; playerYaw = session.Walker.Body.transform.eulerAngles.y;
            vehiclePosition = vehicle.Body.position; vehicleRotation = vehicle.Body.rotation;
            vehicleVelocity = vehicle.Body.linearVelocity; vehicleAngularVelocity = vehicle.Body.angularVelocity;
            combatActive = session.CombatActive; originalSettings = root.Settings.Current;
            string settingsPath = Path.Combine(Application.persistentDataPath, UserSettingsService.SettingsFileName);
            settingsFileExisted = File.Exists(settingsPath); settingsBytes = settingsFileExisted ? File.ReadAllBytes(settingsPath) : null;
            Checks.Clear(); Failures.Clear(); cycles = 0; mapBaselineMeasured = false;
            applicationUnfocusedObserved = !Application.isFocused; startFrame = Time.frameCount; lastFrame = -1;
            running = true; phase = Phase.SeatSetup; phaseAt = EditorApplication.timeSinceStartup;
            deadline = phaseAt + TimeoutSeconds; FocusGameView(); Queue();
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            return "ADVANCED_STARTED slot=" + suffix + " scope=diagnostic_setup_plus_synthetic_input poll=advanced-poll";
        }

        static void Tick()
        {
            if (!running) return;
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) { Fail("Play Mode stopped or paused by the Editor."); return; }
            if (EditorApplication.timeSinceStartup > deadline) { Fail("Advanced review exceeded 90 seconds at " + phase + "."); return; }
            if (lastFrame == Time.frameCount) return; lastFrame = Time.frameCount;
            applicationUnfocusedObserved |= !Application.isFocused;
            try
            {
                switch (phase)
                {
                    case Phase.SeatSetup:
                        session.CombatActive = false; session.Cull(); CloseAndRelease();
                        Need(PlaceForBoarding(), "Could not place the player at a safe boardable point.");
                        Need(seat.TryBoard(), "Diagnostic Seat.TryBoard failed: " + seat.LastInteraction);
                        seatedView = seat.SeatedView; Next(Phase.SeatPause); break;
                    case Phase.SeatPause:
                        root.OpenPage("일시정지");
                        pausedVehiclePosition = vehicle.Body.position;
                        Add("occupied-pause-established", seat.Occupied && root.Pause.IsPaused && root.Gate.InputBlocked && Time.timeScale <= .0001f,
                            "boarding=diagnostic Seat.TryBoard; page=" + root.Page + ", occupied=" + seat.Occupied);
                        Queue(Key.W, Key.E, Key.V, Key.Space); Next(Phase.SeatInput); break;
                    case Phase.SeatInput:
                        Queue(); Next(Phase.SeatRelease); break;
                    case Phase.SeatRelease:
                        Add("paused-vehicle-input-is-suppressed", seat.Occupied && seat.SeatedView == seatedView
                            && Vector3.Distance(vehicle.Body.position, pausedVehiclePosition) < .002f && Mathf.Abs(vehicle.RequestedThrottle) < .001f && vehicle.Braking,
                            "input=synthetic W+E+V+Space; distance=" + Vector3.Distance(vehicle.Body.position, pausedVehiclePosition).ToString("0.0000")
                            + ", throttle=" + vehicle.RequestedThrottle.ToString("0.000") + ", braking=" + vehicle.Braking + ", viewUnchanged=" + (seat.SeatedView == seatedView));
                        root.CloseMenu(); Queue(); Next(Phase.SeatExit); break;
                    case Phase.SeatExit:
                        if (root.Page.Length > 0 || root.Pause.IsPaused || Time.timeScale <= .0001f)
                        {
                            Queue();
                            Need(EditorApplication.timeSinceStartup - phaseAt <= 5d,
                                "Seat pause did not complete its page/pause lifecycle within 5 seconds; " + GateDetails());
                            return;
                        }
                        Need(seat.TryExit(), "Diagnostic Seat.TryExit failed: " + seat.LastInteraction);
                        Add("seat-exit-cleanup", !seat.Occupied, "exit=diagnostic Seat.TryExit");
                        RestorePose(); Next(Phase.SettingsStart); break;
                    case Phase.SettingsStart:
                        root.OpenPage("일시정지");
                        var preview = originalSettings.Clone();
                        preview.UiVolume = originalSettings.UiVolume < .6f ? originalSettings.UiVolume + .2f : originalSettings.UiVolume - .2f;
                        int width = Screen.width, height = Screen.height; root.Settings.Preview(preview); previewAt = Time.realtimeSinceStartupAsDouble;
                        Add("settings-preview-begins-without-display-change", root.Settings.IsPreviewing && Screen.width == width && Screen.height == height
                            && !Mathf.Approximately(root.Settings.Current.UiVolume, originalSettings.UiVolume),
                            "preview=diagnostic Settings.Preview; resolution=" + Screen.width + "x" + Screen.height + ", paused=" + root.Pause.IsPaused);
                        Next(Phase.SettingsWait); break;
                    case Phase.SettingsWait:
                        if (Time.realtimeSinceStartupAsDouble - previewAt < UserSettingsService.DisplayPreviewSeconds + .25f) return;
                        string settingsPath = Path.Combine(Application.persistentDataPath, UserSettingsService.SettingsFileName);
                        bool fileSame = settingsFileExisted == File.Exists(settingsPath) && (!settingsFileExisted || settingsBytes.SequenceEqual(File.ReadAllBytes(settingsPath)));
                        Add("settings-preview-auto-rolls-back-while-paused", !root.Settings.IsPreviewing
                            && JsonUtility.ToJson(root.Settings.Current) == JsonUtility.ToJson(originalSettings) && fileSame && root.Pause.IsPaused,
                            "elapsed=" + (Time.realtimeSinceStartupAsDouble - previewAt).ToString("0.00") + "s, fileBytesUnchanged=" + fileSame);
                        Next(Phase.SettingsClose); break;
                    case Phase.SettingsClose:
                        root.CloseMenu(); Queue();
                        if (root.Page.Length > 0 || root.Pause.IsPaused || Time.timeScale <= .0001f)
                        {
                            Need(EditorApplication.timeSinceStartup - phaseAt <= 5d,
                                "Settings page did not complete its page/pause lifecycle within 5 seconds; " + GateDetails());
                            return;
                        }
                        SnapshotMapStart(); Next(Phase.MapOpen); break;
                    case Phase.MapOpen:
                        root.OpenPage("지도"); Next(Phase.MapOpened); break;
                    case Phase.MapOpened:
                        if (EditorApplication.timeSinceStartup - phaseAt < .48d || root.Map.Folding) return;
                        Need(root.Page == "지도" && root.Map.Expanded && root.Pause.IsPaused && root.Gate.InputBlocked && Time.timeScale <= .0001f,
                            "Map cycle " + (cycles + 1) + " did not reach expanded paused state.");
                        Next(Phase.MapClose); break;
                    case Phase.MapClose:
                        root.CloseMenu(); Queue(); Next(Phase.MapClosed); break;
                    case Phase.MapClosed:
                        Queue();
                        double closeElapsed = EditorApplication.timeSinceStartup - phaseAt;
                        if (closeElapsed < .27d) return;
                        if (root.Page.Length > 0 || root.Pause.IsPaused || Time.timeScale <= .0001f || root.Map.Folding)
                        {
                            Need(closeElapsed <= 5d, "Map cycle " + (cycles + 1) + " did not complete close within 5 seconds; " + GateDetails());
                            return;
                        }
                        cycles++; if (cycles < MapCycles) Next(Phase.MapOpen); else Next(Phase.Finish); break;
                    case Phase.Finish: Finish(); break;
                }
            }
            catch (Exception exception) { Fail(exception.ToString()); }
        }

        static void SnapshotMapStart()
        {
            objectsBefore = SceneObjects(); texturesBefore = Resources.FindObjectsOfTypeAll<Texture>().Length;
            allocatedBefore = Profiler.GetTotalAllocatedMemoryLong(); reservedBefore = Profiler.GetTotalReservedMemoryLong();
            ticksBefore = root.Map.TickSamples;
            mapBaselineMeasured = true;
        }

        static void Finish()
        {
            int objectsAfter = SceneObjects(), texturesAfter = Resources.FindObjectsOfTypeAll<Texture>().Length;
            long allocatedAfter = Profiler.GetTotalAllocatedMemoryLong(), reservedAfter = Profiler.GetTotalReservedMemoryLong();
            Add("twenty-map-open-close-lifecycles", cycles == MapCycles,
                "cycles=" + cycles + ", each observed expanded paused state then page-empty, pause-ended, timeScale-restored, folding-finished after 0.45s/0.25s");
            Add("map-cycle-resource-growth-bounded", objectsAfter - objectsBefore <= 8 && texturesAfter - texturesBefore <= 2 && allocatedAfter - allocatedBefore <= 16L * 1024L * 1024L,
                "objectsDelta=" + (objectsAfter - objectsBefore) + ", texturesDelta=" + (texturesAfter - texturesBefore)
                + ", allocatedDelta=" + (allocatedAfter - allocatedBefore) + ", reservedDelta=" + (reservedAfter - reservedBefore));
            WriteReport(objectsAfter, texturesAfter, allocatedAfter, reservedAfter); Cleanup();
        }

        static void Fail(string message) { if (!running) return; Failures.Add(message); WriteReport(SceneObjects(), Resources.FindObjectsOfTypeAll<Texture>().Length, Profiler.GetTotalAllocatedMemoryLong(), Profiler.GetTotalReservedMemoryLong()); Cleanup(); }

        static void WriteReport(int objectsAfter, int texturesAfter, long allocatedAfter, long reservedAfter)
        {
            var report = new Report {
                utc = DateTime.UtcNow.ToString("O"), status = Failures.Count == 0 ? "PASS" : "FAIL",
                saveIsolationSuffix = session != null ? session.TestSaveSuffix : "", elapsedFrames = Time.frameCount - startFrame, mapCycles = cycles,
                objectsBefore = objectsBefore, objectsAfter = mapBaselineMeasured ? objectsAfter : 0, objectDelta = mapBaselineMeasured ? objectsAfter - objectsBefore : 0,
                texturesBefore = texturesBefore, texturesAfter = mapBaselineMeasured ? texturesAfter : 0, textureDelta = mapBaselineMeasured ? texturesAfter - texturesBefore : 0,
                allocatedBefore = allocatedBefore, allocatedAfter = mapBaselineMeasured ? allocatedAfter : 0, allocatedDelta = mapBaselineMeasured ? allocatedAfter - allocatedBefore : 0,
                reservedBefore = reservedBefore, reservedAfter = mapBaselineMeasured ? reservedAfter : 0, reservedDelta = mapBaselineMeasured ? reservedAfter - reservedBefore : 0,
                mapTickSamplesBefore = ticksBefore, mapTickSamplesAfter = mapBaselineMeasured && root != null && root.Map != null ? root.Map.TickSamples : 0,
                mapLastTickMilliseconds = root != null && root.Map != null ? root.Map.LastTickMilliseconds : 0,
                mapMeanTickMilliseconds = root != null && root.Map != null ? root.Map.MeanTickMilliseconds : 0,
                mapResourceMeasurement = mapBaselineMeasured ? "MEASURED after clean settings phase" : "UNMEASURED: run failed before the map resource baseline",
                focusGateInputReleaseEvidence = applicationUnfocusedObserved
                    ? "UNVERIFIED: Application.isFocused was false; focus-owned gameplay input release was intentionally not required for map lifecycle QA."
                    : "Application remained focused; this report still does not claim native input-device QA.",
                checks = Checks.ToArray(), failures = Failures.ToArray()
            };
            if (applicationUnfocusedObserved)
                report.unverified = report.unverified.Concat(new[] { "Focus-dependent gameplay input release: Application.isFocused was false during the run." }).ToArray();
            string folder = Path.Combine(PlaytestMenuAuthoring.Output, "Validation"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "menu_advanced_review.json"), JsonUtility.ToJson(report, true));
        }

        static bool PlaceForBoarding()
        {
            foreach (Vector3 direction in new[] { vehicle.transform.right, -vehicle.transform.right, -vehicle.transform.forward, vehicle.transform.forward })
                foreach (float distance in new[] { .45f, .8f, 1.2f })
                    if (session.TrySafeFeet(seat.SeatSocket.position + direction * distance, out Vector3 feet))
                    { session.Teleport(feet, vehicle.transform.eulerAngles.y); Physics.SyncTransforms(); if (seat.CanBoard) return true; }
            return false;
        }

        static void RestorePose()
        {
            if (vehicle?.Body != null) { vehicle.Body.position = vehiclePosition; vehicle.Body.rotation = vehicleRotation; vehicle.Body.linearVelocity = vehicleVelocity; vehicle.Body.angularVelocity = vehicleAngularVelocity; }
            if (session?.Walker?.Body != null) session.Teleport(playerFeet, playerYaw);
            Physics.SyncTransforms();
        }

        static void Cleanup()
        {
            Queue();
            try {
                if (root != null) { if (root.Settings != null && root.Settings.IsPreviewing) root.Settings.Revert(); CloseAndRelease(); }
                if (seat != null && seat.Occupied) seat.TryExit(); RestorePose();
                if (session != null) { session.CombatActive = combatActive; session.Cull(); }
            } catch (Exception exception) { Debug.LogError("[PlaytestMenuAdvancedReview] Cleanup: " + exception.Message); }
            running = false; EditorApplication.update -= Tick;
        }

        static void CloseAndRelease()
        {
            if (root == null) return;
            if (root.Page.Length > 0) root.CloseMenu();
            if (root.Pause != null && root.Pause.IsPaused) root.Pause.ForceResume();
            root.Gate?.ReleaseImmediately();
        }

        static void Next(Phase next) { phase = next; phaseAt = EditorApplication.timeSinceStartup; }
        static void Add(string id, bool pass, string detail) { Checks.Add(new Check { id = id, status = pass ? "PASS" : "FAIL", detail = detail }); if (!pass) Failures.Add(id + ": " + detail); }
        static int SceneObjects() => Resources.FindObjectsOfTypeAll<GameObject>().Count(go => go.scene.IsValid());
        static void Queue(params Key[] keys) { if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys ?? Array.Empty<Key>())); if (Mouse.current != null) InputSystem.QueueStateEvent(Mouse.current, new MouseState()); }
        static string GateDetails() => "applicationFocused=" + Application.isFocused + ", page=" + (root != null ? root.Page : "<no-root>")
            + ", mapFolding=" + (root != null && root.Map != null && root.Map.Folding) + ", mapExpanded=" + (root != null && root.Map != null && root.Map.Expanded)
            + ", pause=" + (root != null && root.Pause != null && root.Pause.IsPaused) + ", timeScale=" + Time.timeScale.ToString("0.000")
            + ", blocked=" + (root != null && root.Gate != null && root.Gate.InputBlocked) + ", releasePending=" + (root != null && root.Gate != null && root.Gate.ReleasePending)
            + ", focusOwnsBlock=" + (root != null && root.Gate != null && root.Gate.FocusOwnsBlock) + ", neutralFrames=" + (root != null && root.Gate != null ? root.Gate.NeutralFrames : 0);
        static void FocusGameView() { Type type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"); Need(type != null, "Game View type unavailable."); EditorWindow.GetWindow(type, false, "Game", true).Focus(); }
        static string Poll() { string path = Path.Combine(PlaytestMenuAuthoring.Output, "Validation", "menu_advanced_review.json"); return running ? "RUNNING phase=" + phase + " cycles=" + cycles : File.Exists(path) ? "COMPLETE report=" + path + "\n" + File.ReadAllText(path) : "IDLE no advanced report"; }
        static void Abort() { if (running) Fail("Assembly reload or Play Mode exit interrupted advanced review."); EditorApplication.update -= Tick; }
        static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
