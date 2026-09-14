using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Oheangbu.App;
using Oheangbu.Spellcraft;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace Oheangbu.App.World
{
    /// <summary>
    /// Explicit standalone-build smoke probe. Without its two required command-line options this
    /// type creates no object, subscribes to nothing, and has no effect on an ordinary game run.
    /// </summary>
    [Preserve]
    public sealed class WorldMacroBuildSmoke : MonoBehaviour
    {
        private const string ReportOption = "-oheangbu-smoke-report";
        private const string SlotOption = "-oheangbu-smoke-slot";
        private const string ScreenshotOption = "-oheangbu-smoke-screenshot";
        private const int WarmupFrames = 30;
        private const int MeasurementFrames = 180;
        private const double TimeoutSeconds = 90d;
        private const double PerformanceGoalFps = 120d;
        private static readonly char[] SummonLetters = { '곰', '놈', '몸', '솜', '옴' };

        [Serializable]
        public sealed class SemanticReport
        {
            public bool sceneLoadedBeforeStart;
            public bool isolatedSlotApplied;
            public bool progressInitialized;
            public bool playerFound;
            public bool c02Found;
            public bool testBookAccessible;
            public bool fiveSummonsMapped;
            public int summonMappings;
            public string testBook;
            public string loadStatus;
            public bool restartObserved;
            public bool saveSucceeded;
            public string saveError;
            public string saveFile;
            public bool saveFileExists;
            public long saveFileBytes;
            public int renderers;
            public int activeRenderers;
            public int missingMaterialSlots;
            public int missingShaders;
            public int navigationActors;
            public int navigationBound;
            public int patrolPathsComplete;
            public float playerPlanarDisplacement;
            public string[] checks = Array.Empty<string>();
        }

        [Serializable]
        public sealed class PerformanceReport
        {
            public string goal = "Diagnostic stationary standalone sample: uncapped, vSync off, 30 warmup frames, then 180 measured frames; user goal 120 FPS. This does not certify the full route.";
            public int width;
            public int height;
            public string fullScreenMode;
            public int warmupFrames;
            public int measuredFrames;
            public int measuredVSyncCount;
            public int measuredTargetFrameRate;
            public int measuredRenderInterval;
            public double elapsedSeconds;
            public double actualFps;
            public double meanFrameMs;
            public double p95FrameMs;
            public bool frameTimingFeatureEnabled;
            public int cpuTimingSamples;
            public int gpuTimingSamples;
            public double meanCpuFrameMs;
            public double p95CpuFrameMs;
            public double meanGpuFrameMs;
            public double p95GpuFrameMs;
            public double meanCpuMainThreadMs;
            public double meanCpuRenderThreadMs;
            public bool goalMet;
            public bool renderedSampleValid;
            public string renderingStatus;
            public long managedMemoryBytes;
            public long totalAllocatedMemoryBytes;
            public long totalReservedMemoryBytes;
            public long peakAllocatedMemoryBytes;
            public bool settingsRestored;
        }

        [Serializable]
        public sealed class SmokeReport
        {
            public string status = "RUNNING";
            public string utc;
            public string phase = "BOOT";
            public string scope = "Standalone startup, isolated save/reload state, renderer integrity, and stationary frame sampling only. No walking, drawing, casting, interaction, combat input, or visual approval.";
            public string reportPath;
            public string screenshotPath;
            public bool screenshotRequested;
            public bool screenshotWritten;
            public string slot;
            public string isolatedSuffix;
            public string scene;
            public string unity;
            public string platform;
            public string hardware;
            public string graphicsApi;
            public SemanticReport semantic = new SemanticReport();
            public PerformanceReport performance = new PerformanceReport();
            public string[] errors = Array.Empty<string>();
        }

        private enum Stage { WaitingForSession, Warmup, Measure, ScreenshotNextFrame, ScreenshotWait, Finished }

        private readonly List<string> _errors = new List<string>();
        private readonly List<double> _frameMilliseconds = new List<double>(MeasurementFrames);
        private readonly List<double> _cpuMilliseconds = new List<double>(MeasurementFrames);
        private readonly List<double> _gpuMilliseconds = new List<double>(MeasurementFrames);
        private readonly List<double> _cpuMainMilliseconds = new List<double>(MeasurementFrames);
        private readonly List<double> _cpuRenderMilliseconds = new List<double>(MeasurementFrames);
        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private ulong _lastFrameTimingTimestamp;

        private SmokeReport _report;
        private WorldMacroPlaytestSession _session;
        private WorldMacroPlayerAppearance _appearance;
        private Stage _stage;
        private double _deadline;
        private double _measurementStarted;
        private int _stageStartedFrame;
        private int _measuredFrames;
        private long _peakAllocatedMemory;
        private Vector3 _playerStart;
        private int _originalVSync;
        private int _originalTargetFrameRate;
        private int _originalRenderInterval;
        private bool _originalRunInBackground;
        private bool _settingsCaptured;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        [Preserve]
        private static void Bootstrap()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool hasReportOption = Array.IndexOf(args, ReportOption) >= 0;
            bool hasSlotOption = Array.IndexOf(args, SlotOption) >= 0;
            if (!hasReportOption && !hasSlotOption) return;

            string reportPath = ReadOption(args, ReportOption);
            string slot = ReadOption(args, SlotOption);
            bool screenshot = Array.IndexOf(args, ScreenshotOption) >= 0;
            var startupErrors = new List<string>();
            if (string.IsNullOrWhiteSpace(reportPath) || !Path.IsPathRooted(reportPath)
                || !string.Equals(Path.GetExtension(reportPath), ".json", StringComparison.OrdinalIgnoreCase))
                startupErrors.Add(ReportOption + " requires an absolute .json path.");
            if (string.IsNullOrWhiteSpace(slot) || !slot.All(IsAsciiAlphaNumeric))
                startupErrors.Add(SlotOption + " requires a non-empty ASCII alphanumeric value.");

            var owner = new GameObject("WorldMacro_BuildSmoke");
            DontDestroyOnLoad(owner);
            owner.AddComponent<WorldMacroBuildSmoke>().Initialize(reportPath, slot, screenshot, startupErrors);
        }

        private static string ReadOption(string[] args, string option)
        {
            int index = Array.IndexOf(args, option);
            return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("-", StringComparison.Ordinal)
                ? args[index + 1] : null;
        }

        private static bool IsAsciiAlphaNumeric(char value)
        {
            return value >= 'a' && value <= 'z' || value >= 'A' && value <= 'Z' || value >= '0' && value <= '9';
        }

        private void Initialize(string reportPath, string slot, bool screenshot, List<string> startupErrors)
        {
            _report = new SmokeReport
            {
                utc = DateTime.UtcNow.ToString("o"),
                reportPath = reportPath,
                screenshotRequested = screenshot,
                screenshotPath = screenshot && !string.IsNullOrWhiteSpace(reportPath) ? Path.ChangeExtension(reportPath, ".png") : null,
                slot = slot,
                isolatedSuffix = string.IsNullOrWhiteSpace(slot) ? null : "_smoke_" + slot,
                unity = Application.unityVersion,
                platform = Application.platform.ToString(),
                hardware = SystemInfo.processorType + " / " + SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString()
            };
            _errors.AddRange(startupErrors);
            _deadline = Time.realtimeSinceStartupAsDouble + TimeoutSeconds;
            CaptureOriginalSettings();
            Application.runInBackground = true;
            Application.logMessageReceived += OnLogMessage;
            SceneManager.sceneLoaded += OnSceneLoaded;
            _stage = Stage.WaitingForSession;

            if (_errors.Count > 0) Fail("Invalid smoke command line.");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_report == null || _stage == Stage.Finished) return;
            _report.scene = scene.path;
            WorldMacroPlaytestSession candidate = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).FirstOrDefault();
            if (candidate == null) return;

            // sceneLoaded is invoked after Awake/OnEnable and before Start, which keeps the store
            // path isolated before WorldMacroPlaytestSession constructs its AtomicJsonStore.
            candidate.TestSaveSuffix = _report.isolatedSuffix;
            _session = candidate;
            _report.semantic.sceneLoadedBeforeStart = candidate.Progress == null;
            _report.semantic.isolatedSlotApplied = candidate.TestSaveSuffix == _report.isolatedSuffix;
        }

        private void Update()
        {
            if (_report == null || _stage == Stage.Finished) return;
            try
            {
                if (Time.realtimeSinceStartupAsDouble > _deadline)
                {
                    Fail("Smoke timeout exceeded " + TimeoutSeconds.ToString("F0") + " seconds during " + _report.phase + ".");
                    return;
                }

                switch (_stage)
                {
                    case Stage.WaitingForSession: WaitForSession(); break;
                    case Stage.Warmup: Warmup(); break;
                    case Stage.Measure: Measure(); break;
                    case Stage.ScreenshotNextFrame: CaptureScreenshotOnNextFrame(); break;
                    case Stage.ScreenshotWait: WaitForScreenshot(); break;
                }
            }
            catch (Exception exception)
            {
                Fail(exception.ToString());
            }
        }

        private void WaitForSession()
        {
            if (_session == null || _session.Progress == null || _session.Walker == null || _session.Walker.Body == null) return;
            _appearance = FindFirstObjectByType<WorldMacroPlayerAppearance>();
            if (_appearance == null) return;

            _report.semantic.progressInitialized = true;
            _report.semantic.playerFound = _session.Walker.Body != null;
            _report.semantic.c02Found = _appearance.Profile != null && _appearance.Animator != null;
            _report.semantic.loadStatus = _session.LoadStatus;
            _report.semantic.restartObserved = _session.LoadStatus == "primary" || _session.LoadStatus == "backup";
            _playerStart = _session.Walker.Body.transform.position;
            ReadTestBook();
            _stage = Stage.Warmup;
            _stageStartedFrame = Time.frameCount;
            _report.phase = "WARMUP";
        }

        private void Warmup()
        {
            int elapsed = Time.frameCount - _stageStartedFrame;
            _report.performance.warmupFrames = Mathf.Min(elapsed, WarmupFrames);
            if (elapsed < WarmupFrames) return;
            BeginMeasurement();
        }

        private void BeginMeasurement()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            OnDemandRendering.renderFrameInterval = 1;
            Application.runInBackground = true;

            _report.performance.width = Screen.width;
            _report.performance.height = Screen.height;
            _report.performance.fullScreenMode = Screen.fullScreenMode.ToString();
            _report.performance.measuredVSyncCount = QualitySettings.vSyncCount;
            _report.performance.measuredTargetFrameRate = Application.targetFrameRate;
            _report.performance.measuredRenderInterval = OnDemandRendering.renderFrameInterval;
            _report.performance.frameTimingFeatureEnabled = FrameTimingManager.IsFeatureEnabled();
            _report.performance.renderedSampleValid = IsPlayerWindowVisible();
            _report.performance.renderingStatus = _report.performance.renderedSampleValid
                ? "Visible-window stationary sample; not a route benchmark."
                : "UNVERIFIED_RENDERING: hidden/minimized window; update-loop timing is not rendered FPS.";
            _measurementStarted = Time.realtimeSinceStartupAsDouble;
            _peakAllocatedMemory = Profiler.GetTotalAllocatedMemoryLong();
            _stage = Stage.Measure;
            _report.phase = "MEASURE";
        }

        private void Measure()
        {
            _report.semantic.playerPlanarDisplacement = Mathf.Max(_report.semantic.playerPlanarDisplacement,
                Vector3.ProjectOnPlane(_session.Walker.Body.transform.position - _playerStart, Vector3.up).magnitude);
            FrameTimingManager.CaptureFrameTimings();
            _report.performance.renderedSampleValid &= IsPlayerWindowVisible();
            _frameMilliseconds.Add(Time.unscaledDeltaTime * 1000d);
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0 && _timings[0].frameStartTimestamp != _lastFrameTimingTimestamp)
            {
                FrameTiming timing = _timings[0];
                _lastFrameTimingTimestamp = timing.frameStartTimestamp;
                AddPositiveFinite(_cpuMilliseconds, timing.cpuFrameTime);
                AddPositiveFinite(_gpuMilliseconds, timing.gpuFrameTime);
                AddPositiveFinite(_cpuMainMilliseconds, timing.cpuMainThreadFrameTime);
                AddPositiveFinite(_cpuRenderMilliseconds, timing.cpuRenderThreadFrameTime);
            }
            _peakAllocatedMemory = Math.Max(_peakAllocatedMemory, Profiler.GetTotalAllocatedMemoryLong());
            _measuredFrames++;
            if (_measuredFrames < MeasurementFrames) return;

            CompleteMeasurement();
            CompleteSemanticChecks();
            if (_report.screenshotRequested)
            {
                _stage = Stage.ScreenshotNextFrame;
                _stageStartedFrame = Time.frameCount;
                _report.phase = "SCREENSHOT_NEXT_FRAME";
            }
            else Finish();
        }

        private void CompleteMeasurement()
        {
            PerformanceReport performance = _report.performance;
            performance.measuredFrames = _measuredFrames;
            performance.elapsedSeconds = Math.Max(.000001d, Time.realtimeSinceStartupAsDouble - _measurementStarted);
            performance.actualFps = _measuredFrames / performance.elapsedSeconds;
            performance.meanFrameMs = Mean(_frameMilliseconds);
            performance.p95FrameMs = Percentile(_frameMilliseconds, .95d);
            performance.cpuTimingSamples = _cpuMilliseconds.Count;
            performance.gpuTimingSamples = _gpuMilliseconds.Count;
            performance.meanCpuFrameMs = Mean(_cpuMilliseconds);
            performance.p95CpuFrameMs = Percentile(_cpuMilliseconds, .95d);
            performance.meanGpuFrameMs = Mean(_gpuMilliseconds);
            performance.p95GpuFrameMs = Percentile(_gpuMilliseconds, .95d);
            performance.meanCpuMainThreadMs = Mean(_cpuMainMilliseconds);
            performance.meanCpuRenderThreadMs = Mean(_cpuRenderMilliseconds);
            performance.goalMet = performance.renderedSampleValid && performance.actualFps >= PerformanceGoalFps
                && performance.p95FrameMs <= 1000d / PerformanceGoalFps;
            if (!performance.renderedSampleValid)
                performance.renderingStatus = "UNVERIFIED_RENDERING: hidden/minimized window; update-loop timing is not rendered FPS.";
            performance.managedMemoryBytes = GC.GetTotalMemory(false);
            performance.totalAllocatedMemoryBytes = Profiler.GetTotalAllocatedMemoryLong();
            performance.totalReservedMemoryBytes = Profiler.GetTotalReservedMemoryLong();
            performance.peakAllocatedMemoryBytes = _peakAllocatedMemory;
        }

        private void CompleteSemanticChecks()
        {
            _report.semantic.playerPlanarDisplacement = Mathf.Max(_report.semantic.playerPlanarDisplacement, Vector3.ProjectOnPlane(
                _session.Walker.Body.transform.position - _playerStart, Vector3.up).magnitude);
            _report.semantic.saveSucceeded = _session.Save();
            _report.semantic.saveError = _session.SaveError;
            if (_session.Content != null)
            {
                _report.semantic.saveFile = Path.Combine(Application.persistentDataPath,
                    _session.Content.SaveSlot + _report.isolatedSuffix + ".json");
                _report.semantic.saveFileExists = File.Exists(_report.semantic.saveFile);
                if (_report.semantic.saveFileExists) _report.semantic.saveFileBytes = new FileInfo(_report.semantic.saveFile).Length;
            }

            AuditRenderers();
            foreach (var actor in _session.Actors)
            {
                if (actor == null || !actor.gameObject.activeInHierarchy) continue;
                _report.semantic.navigationActors++;
                var agent = actor.GetComponent<NavMeshAgent>();
                if (agent == null || !agent.enabled || !agent.isOnNavMesh) continue;
                _report.semantic.navigationBound++;
                var path = new NavMeshPath();
                if (actor.PatrolPoints != null && actor.PatrolPoints.Length > 0
                    && agent.CalculatePath(actor.PatrolPoints[actor.PatrolPoints.Length - 1], path)
                    && path.status == NavMeshPathStatus.PathComplete) _report.semantic.patrolPathsComplete++;
            }
            var checks = new List<string>
            {
                Check("sceneLoaded hook preceded Session.Start", _report.semantic.sceneLoadedBeforeStart),
                Check("isolated smoke suffix applied", _report.semantic.isolatedSlotApplied),
                Check("progress initialized", _report.semantic.progressInitialized),
                Check("player found", _report.semantic.playerFound),
                Check("C02 appearance found", _report.semantic.c02Found),
                Check("stationary sample stayed within 5cm", _report.semantic.playerPlanarDisplacement <= .05f),
                Check("load status accepted", _report.semantic.loadStatus == "new" || _report.semantic.loadStatus == "primary" || _report.semantic.loadStatus == "backup"),
                Check("isolated save succeeded", _report.semantic.saveSucceeded && string.IsNullOrEmpty(_report.semantic.saveError)
                    && _report.semantic.saveFileExists && _report.semantic.saveFileBytes > 0),
                Check("active renderer material integrity", _report.semantic.missingMaterialSlots == 0 && _report.semantic.missingShaders == 0)
            };
            checks.Add(Check("five TEST summon mappings accessible", _report.semantic.testBookAccessible && _report.semantic.fiveSummonsMapped));
            checks.Add(Check("active enemies bound with complete patrol paths", _report.semantic.navigationActors > 0
                && _report.semantic.navigationActors == _report.semantic.navigationBound
                && _report.semantic.navigationActors == _report.semantic.patrolPathsComplete));
            _report.semantic.checks = checks.ToArray();
        }

        private void ReadTestBook()
        {
            BrushStrokeFeedAdapter adapter = FindFirstObjectByType<BrushStrokeFeedAdapter>();
            FieldInfo field = typeof(BrushStrokeFeedAdapter).GetField("_spellBook", BindingFlags.Instance | BindingFlags.NonPublic);
            SpellBookSO book = adapter != null && field != null ? field.GetValue(adapter) as SpellBookSO : null;
            _report.semantic.testBookAccessible = book != null;
            _report.semantic.testBook = book != null ? book.name : null;
            if (book == null) return;
            _report.semantic.summonMappings = SummonLetters.Count(letter => book.TryGet(letter, out SpellBookSO.Entry entry)
                && entry.Kind == SpellKind.Summon);
            _report.semantic.fiveSummonsMapped = _report.semantic.summonMappings == SummonLetters.Length;
        }

        private void AuditRenderers()
        {
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _report.semantic.renderers = renderers.Length;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                _report.semantic.activeRenderers++;
                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    _report.semantic.missingMaterialSlots++;
                    continue;
                }
                foreach (Material material in materials)
                {
                    if (material == null) _report.semantic.missingMaterialSlots++;
                    else if (material.shader == null || !material.shader.isSupported) _report.semantic.missingShaders++;
                }
            }
        }

        private void CaptureScreenshotOnNextFrame()
        {
            if (Time.frameCount <= _stageStartedFrame) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_report.screenshotPath));
            ScreenCapture.CaptureScreenshot(_report.screenshotPath);
            _stageStartedFrame = Time.frameCount;
            _stage = Stage.ScreenshotWait;
            _report.phase = "SCREENSHOT_WAIT";
        }

        private void WaitForScreenshot()
        {
            if (Time.frameCount - _stageStartedFrame < 3) return;
            _report.screenshotWritten = File.Exists(_report.screenshotPath) && new FileInfo(_report.screenshotPath).Length > 0;
            if (!_report.screenshotWritten) _errors.Add("Requested screenshot was not written after three frames: " + _report.screenshotPath);
            Finish();
        }

        private void Finish()
        {
            if (_stage == Stage.Finished) return;
            RestoreSettings();
            _report.phase = "FINISHED";
            bool semanticPass = _report.semantic.checks.All(check => check.StartsWith("PASS", StringComparison.Ordinal));
            _report.status = _errors.Count > 0 || !semanticPass ? "FAILED" : _report.semantic.restartObserved
                ? "PASS_RESTART_SAVE" : "PASS_STARTUP_SAVE";
            if (_errors.Count == 0 && semanticPass && !_report.performance.renderedSampleValid) _report.status += "_RENDER_PERFORMANCE_UNVERIFIED";
            else if (_errors.Count == 0 && semanticPass && !_report.performance.goalMet) _report.status += "_PERFORMANCE_TARGET_MISSED";
            WriteReportAndQuit(_report.status.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 2);
        }

        private void Fail(string error)
        {
            if (_stage == Stage.Finished) return;
            if (!string.IsNullOrWhiteSpace(error)) _errors.Add(error);
            RestoreSettings();
            if (_report != null)
            {
                _report.status = "FAILED";
                _report.phase = "FAILED";
            }
            WriteReportAndQuit(2);
        }

        private void WriteReportAndQuit(int exitCode)
        {
            _stage = Stage.Finished;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.logMessageReceived -= OnLogMessage;
            if (_report != null)
            {
                _report.errors = _errors.Distinct().ToArray();
                if (!string.IsNullOrWhiteSpace(_report.reportPath) && Path.IsPathRooted(_report.reportPath))
                {
                    try
                    {
                        string directory = Path.GetDirectoryName(_report.reportPath);
                        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                        File.WriteAllText(_report.reportPath, JsonUtility.ToJson(_report, true));
                    }
                    catch { exitCode = 2; }
                }
            }
            Application.Quit(exitCode);
        }

        private void RestoreSettings()
        {
            if (!_settingsCaptured) return;
            QualitySettings.vSyncCount = _originalVSync;
            Application.targetFrameRate = _originalTargetFrameRate;
            OnDemandRendering.renderFrameInterval = _originalRenderInterval;
            Application.runInBackground = _originalRunInBackground;
            if (_report != null)
                _report.performance.settingsRestored = QualitySettings.vSyncCount == _originalVSync
                    && Application.targetFrameRate == _originalTargetFrameRate
                    && OnDemandRendering.renderFrameInterval == _originalRenderInterval
                    && Application.runInBackground == _originalRunInBackground;
            _settingsCaptured = false;
        }

        private void CaptureOriginalSettings()
        {
            if (_settingsCaptured) return;
            _originalVSync = QualitySettings.vSyncCount;
            _originalTargetFrameRate = Application.targetFrameRate;
            _originalRenderInterval = OnDemandRendering.renderFrameInterval;
            _originalRunInBackground = Application.runInBackground;
            _settingsCaptured = true;
        }

        private void OnLogMessage(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (_errors.Count >= 100) return;
            _errors.Add(type + ": " + message + (string.IsNullOrEmpty(stackTrace) ? string.Empty : "\n" + stackTrace));
        }

        private static bool IsPlayerWindowVisible()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                IntPtr window = process.MainWindowHandle;
                return window != IntPtr.Zero && IsWindowVisible(window) && !IsIconic(window);
            }
#else
            return Application.isFocused;
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
#endif

        private void OnDestroy()
        {
            RestoreSettings();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.logMessageReceived -= OnLogMessage;
        }

        private static void AddPositiveFinite(List<double> samples, double value)
        {
            if (!double.IsNaN(value) && !double.IsInfinity(value) && value > 0d) samples.Add(value);
        }

        private static double Mean(List<double> samples)
        {
            return samples.Count == 0 ? 0d : samples.Average();
        }

        private static double Percentile(List<double> samples, double percentile)
        {
            if (samples.Count == 0) return 0d;
            double[] sorted = samples.OrderBy(value => value).ToArray();
            int index = Math.Min(sorted.Length - 1, Math.Max(0, (int)Math.Ceiling(sorted.Length * percentile) - 1));
            return sorted[index];
        }

        private static string Check(string name, bool pass)
        {
            return (pass ? "PASS: " : "FAIL: ") + name;
        }
    }
}
