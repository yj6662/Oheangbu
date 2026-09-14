using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using Oheangbu.App.World.UI;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace Oheangbu.App
{
    /// <summary>Explicit standalone title-only diagnostic. Absent its CLI flag, creates nothing.</summary>
    public sealed class PlaytestStartupSmoke : MonoBehaviour
    {
        const string Flag = "--oheangbu-startup-smoke";
        const string ImageName = "startup_title_1920x1080.png";
        const string ReportName = "startup_smoke.json";
        const double TimeoutSeconds = 30d;
        readonly object logLock = new object();
        readonly List<LogEntry> logs = new List<LogEntry>();
        string directory, imagePath, reportPath, temporaryReportPath, startedUtc;
        double started, stableSince = -1;
        int stableSceneHandle, firstRenderedFrame, observedFrames, errorCount, warningCount;
        long peakUnityAllocatedBytes, peakManagedBytes;
        bool configured, captureRequested, resolutionRequested, finished, subscribed;
        bool captureCommitKnown;
        double captureCommitRatio = -1d;
        string captureMemoryError;

        [Serializable] sealed class LogEntry { public string type, message, stack; }
        [Serializable] sealed class Report
        {
            public string status, reason, startedUtc, finishedUtc, scope, unityVersion, platform,
                operatingSystem, graphicsDevice, graphicsVendor, graphicsVersion, graphicsApi,
                expectedScene, actualScene, screenshot, outputDirectory, captureMemoryError;
            public bool titleUiReady, pngComplete, resolutionChangedForDiagnostic, nativeInputExecuted,
                gameplayExecuted, saveOperationsRequested, finalPlaytestQaComplete, captureCommitKnown;
            public int screenWidth, screenHeight, pngWidth, pngHeight, renderedTitleFrames,
                logErrorCount, logWarningCount, storedErrorCount, systemMemoryMegabytes;
            public double elapsedSeconds, stableTitleSeconds, captureCommitRatio, captureCommitStopRatio = .85d;
            public long screenshotBytes, peakUnityAllocatedBytes, peakManagedBytes;
            public LogEntry[] logErrors;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void StartOnlyWhenRequested()
        {
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args, Flag);
            if (flag < 0) return;
            if (flag + 1 >= args.Length || Array.LastIndexOf(args, Flag) != flag)
            { Debug.LogError("[StartupSmoke] One flag followed by one absolute output directory is required."); Application.Quit(2); return; }
            GameObject holder = null;
            try
            {
                string output = ValidateDirectory(args[flag + 1]);
                Directory.CreateDirectory(output);
                // A stale PNG must never make a new run appear to have rendered successfully.
                foreach (string name in new[] { ImageName, ReportName, ReportName + ".tmp" })
                    if (File.Exists(ConfinedFile(output, name))) throw new IOException("Use a fresh evidence directory; file already exists: " + name);
                holder = new GameObject("Explicit_Standalone_StartupSmoke") { hideFlags = HideFlags.HideInHierarchy };
                DontDestroyOnLoad(holder);
                holder.AddComponent<PlaytestStartupSmoke>().Configure(output);
            }
            catch (Exception error)
            {
                if (holder != null) Destroy(holder);
                Debug.LogError("[StartupSmoke] Initialization failed: " + error.Message);
                Application.Quit(2);
            }
        }

        static string ValidateDirectory(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || !Path.IsPathRooted(input)) throw new ArgumentException("Output directory must be absolute.");
            string root = Path.GetPathRoot(input);
            if (Path.DirectorySeparatorChar == '\\' && (string.IsNullOrEmpty(root) || root.Length < 3))
                throw new ArgumentException("Drive-relative output directories are not accepted.");
            return Path.GetFullPath(input);
        }
        static string ConfinedFile(string output, string name)
        {
            string basePath = Path.GetFullPath(output);
            string prefix = basePath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? basePath : basePath + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(basePath, name));
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Evidence path escaped its explicit output directory.");
            return path;
        }
        void Configure(string output)
        {
            directory = output; imagePath = ConfinedFile(output, ImageName);
            reportPath = ConfinedFile(output, ReportName); temporaryReportPath = ConfinedFile(output, ReportName + ".tmp");
            started = Time.realtimeSinceStartupAsDouble; startedUtc = DateTime.UtcNow.ToString("O");
            Application.logMessageReceivedThreaded += RecordLog; subscribed = true; configured = true;
        }
        void RecordLog(string condition, string trace, LogType type)
        {
            lock (logLock)
            {
                if (type == LogType.Warning) { warningCount++; return; }
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                errorCount++;
                if (logs.Count < 64) logs.Add(new LogEntry { type = type.ToString(), message = Limit(condition, 4096), stack = Limit(trace, 4096) });
            }
        }
        static string Limit(string value, int maximum) => string.IsNullOrEmpty(value) || value.Length <= maximum ? value : value.Substring(0, maximum);
        bool TitleReady()
        {
            var ui = PlaytestUiRoot.Instance;
            return SceneManager.GetActiveScene().name == PlaytestUiRoot.TitleScene
                && ui != null && ui.isActiveAndEnabled && ui.IsTitle && !ui.Busy && string.IsNullOrEmpty(ui.Page)
                && ui.Theme != null && ui.Theme.TitleBackdrop != null && ui.Theme.Font != null && Camera.allCamerasCount > 0;
        }
        void LateUpdate()
        {
            if (!configured || finished) return;
            try
            {
                double now = Time.realtimeSinceStartupAsDouble;
                peakUnityAllocatedBytes = Math.Max(peakUnityAllocatedBytes, Profiler.GetTotalAllocatedMemoryLong());
                peakManagedBytes = Math.Max(peakManagedBytes, GC.GetTotalMemory(false));
                if (now - started >= TimeoutSeconds) { Complete(false, "Timed out before a stable title and complete screenshot within 30 seconds."); return; }
                if (!TitleReady())
                {
                    if (captureRequested) { Complete(false, "Title became unavailable after screenshot request."); return; }
                    stableSince = -1; observedFrames = 0; return;
                }
                // A saved display option can override CLI dimensions during title Awake. This
                // diagnostic only changes the current window; it never invokes settings/save APIs.
                if (Screen.width != 1920 || Screen.height != 1080)
                {
                    if (!resolutionRequested) { Screen.SetResolution(1920, 1080, FullScreenMode.Windowed); resolutionRequested = true; }
                    stableSince = -1; observedFrames = 0; return;
                }
                int handle = SceneManager.GetActiveScene().handle;
                if (stableSince < 0 || stableSceneHandle != handle)
                { stableSince = now; stableSceneHandle = handle; firstRenderedFrame = Time.renderedFrameCount; }
                observedFrames = Math.Max(0, Time.renderedFrameCount - firstRenderedFrame);
                if (!captureRequested)
                {
                    if (now - stableSince < 3d || observedFrames < 60) return;
                    captureCommitKnown = TryCommitRatio(out captureCommitRatio, out captureMemoryError);
                    if (!captureCommitKnown)
                    { Complete(false, "Screenshot skipped: system commit could not be measured; capture remains unverified.", "UNVERIFIED_TITLE_CAPTURE_MEMORY_UNKNOWN"); return; }
                    if (captureCommitRatio >= .85d)
                    { Complete(false, "Screenshot skipped: system commit is at or above 85%.", "UNVERIFIED_TITLE_CAPTURE_MEMORY_LIMIT"); return; }
                    ScreenCapture.CaptureScreenshot(imagePath, 1); captureRequested = true; return;
                }
                if (ReadCompletePng(out int width, out int height, out _))
                {
                    int errors; lock (logLock) errors = errorCount;
                    Complete(width == 1920 && height == 1080 && errors == 0,
                        errors > 0 ? "Title rendered, but runtime error/exception/assert logs were observed."
                        : width != 1920 || height != 1080 ? "Captured PNG dimensions were not 1920x1080."
                        : "Expected title stayed ready for at least 3 seconds and 60 rendered frames; one actual screenshot completed.");
                }
            }
            catch (Exception error) { Complete(false, "Startup smoke exception: " + error); }
        }
        bool ReadCompletePng(out int width, out int height, out long bytes)
        {
            width = height = 0; bytes = 0;
            if (!File.Exists(imagePath)) return false;
            try
            {
                using (var file = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    bytes = file.Length; if (bytes < 44) return false;
                    byte[] header = new byte[24]; if (file.Read(header, 0, header.Length) != header.Length) return false;
                    byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
                    for (int i = 0; i < signature.Length; i++) if (header[i] != signature[i]) return false;
                    width = BigEndian(header, 16); height = BigEndian(header, 20);
                    byte[] ending = new byte[12]; file.Seek(-12, SeekOrigin.End); if (file.Read(ending, 0, 12) != 12) return false;
                    byte[] iend = { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 };
                    for (int i = 0; i < 12; i++) if (ending[i] != iend[i]) return false;
                    return width > 0 && height > 0;
                }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        static int BigEndian(byte[] bytes, int start) => (bytes[start] << 24) | (bytes[start + 1] << 16) | (bytes[start + 2] << 8) | bytes[start + 3];
        [StructLayout(LayoutKind.Sequential)] struct PerformanceInfo
        {
            public uint Size;
            public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable,
                SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
            public uint Handles, Processes, Threads;
        }
        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetPerformanceInfo(out PerformanceInfo info, uint size);
        static bool TryCommitRatio(out double ratio, out string error)
        {
            ratio = -1d; error = null;
            if (Application.platform != RuntimePlatform.WindowsPlayer) { error = "System commit query is supported for this Windows player only."; return false; }
            try
            {
                if (!GetPerformanceInfo(out var info, (uint)Marshal.SizeOf<PerformanceInfo>()))
                { error = "GetPerformanceInfo failed: " + Marshal.GetLastWin32Error(); return false; }
                ulong limit = info.CommitLimit.ToUInt64();
                if (limit == 0) { error = "GetPerformanceInfo returned a zero commit limit."; return false; }
                ratio = (double)info.CommitTotal.ToUInt64() / limit; return true;
            }
            catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException || exception is BadImageFormatException)
            { error = exception.Message; return false; }
        }
        void Complete(bool success, string reason, string explicitStatus = null)
        {
            if (finished) return; finished = true;
            bool png = ReadCompletePng(out int width, out int height, out long bytes);
            var report = new Report {
                status = explicitStatus ?? (success ? "PASS_STANDALONE_TITLE_STARTUP_ONLY" : "FAIL_STANDALONE_TITLE_STARTUP"),
                reason = reason, startedUtc = startedUtc, finishedUtc = DateTime.UtcNow.ToString("O"),
                scope = "Explicit standalone title-startup smoke only. No input injection, menu click, gameplay, save request, walking or video. Does not certify player controls, combat, saves, performance target or whole playtest QA.",
                unityVersion = Application.unityVersion, platform = Application.platform.ToString(), operatingSystem = SystemInfo.operatingSystem,
                graphicsDevice = SystemInfo.graphicsDeviceName, graphicsVendor = SystemInfo.graphicsDeviceVendor,
                graphicsVersion = SystemInfo.graphicsDeviceVersion, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                expectedScene = PlaytestUiRoot.TitleScene, actualScene = SceneManager.GetActiveScene().name,
                screenshot = png ? imagePath : null, outputDirectory = directory, titleUiReady = TitleReady(), pngComplete = png,
                resolutionChangedForDiagnostic = resolutionRequested, screenWidth = Screen.width, screenHeight = Screen.height,
                pngWidth = width, pngHeight = height, renderedTitleFrames = observedFrames, screenshotBytes = bytes,
                elapsedSeconds = Time.realtimeSinceStartupAsDouble - started,
                stableTitleSeconds = stableSince < 0 ? 0 : Time.realtimeSinceStartupAsDouble - stableSince,
                peakUnityAllocatedBytes = peakUnityAllocatedBytes, peakManagedBytes = peakManagedBytes,
                systemMemoryMegabytes = SystemInfo.systemMemorySize, captureCommitKnown = captureCommitKnown,
                captureCommitRatio = captureCommitRatio, captureMemoryError = captureMemoryError
            };
            lock (logLock) { report.logErrors = logs.ToArray(); report.storedErrorCount = logs.Count; report.logErrorCount = errorCount; report.logWarningCount = warningCount; }
            Unsubscribe();
            try
            {
                File.WriteAllText(temporaryReportPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
                File.Move(temporaryReportPath, reportPath);
                Application.Quit(success ? 0 : 1);
            }
            catch (Exception error) { Debug.LogError("[StartupSmoke] Could not write evidence report: " + error.Message); Application.Quit(2); }
        }
        void Unsubscribe() { if (subscribed) { Application.logMessageReceivedThreaded -= RecordLog; subscribed = false; } }
        void OnDestroy() { Unsubscribe(); }
        void OnApplicationQuit() { Unsubscribe(); }
    }
}
