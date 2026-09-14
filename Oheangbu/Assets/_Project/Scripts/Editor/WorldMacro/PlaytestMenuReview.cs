using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Staged, opt-in review of the playtest menus. It never starts Play Mode.</summary>
    [InitializeOnLoad]
    public static class PlaytestMenuReview
    {
        public const string ReviewSuffixKey = "PlaytestUiReviewSuffix";
        private const string CheckReportName = "menu_runtime_checks.json";
        private const double OperationTimeoutSeconds = 90d;
        private const long Megabyte = 1024L * 1024L;

        [Serializable]
        private sealed class CheckEntry
        {
            public string id;
            public string status;
            public string detail;
        }

        [Serializable]
        private sealed class RuntimeStats
        {
            public int frame;
            public int width;
            public int height;
            public long totalAllocatedBytes;
            public long reservedBytes;
            public long monoHeapBytes;
            public int systemMemoryMb;
            public string scene;
            public string page;
            public bool gateBlocked;
            public bool applicationFocused;
            public bool gateReleasePending;
            public bool gateFocusOwnsBlock;
            public int gateNeutralFrames;
            public bool pauseActive;
            public bool drawing;
            public bool vehicleOccupied;
            public float timeScale;
            public float playerHp01;
            public float hudInk01;
            public int collectedBundles;
            public int collectedFragments;
        }

        [Serializable]
        private sealed class CheckReport
        {
            public string utc;
            public string status;
            public string scope = "Editor Play Mode technical QA using injected Input System keyboard state. It is not native handwriting, natural walking, controller/device, listening, or user approval evidence.";
            public string inputEvidence = "synthetic InputSystem.QueueStateEvent keyboard events";
            public string saveIsolationSuffix;
            public int elapsedFrames;
            public int collectionEvents;
            public int uniqueBundlesCollected;
            public int fragmentItemsCollected;
            public int knownSpellLetters;
            public bool productionSlotUntouchedByContract;
            public RuntimeStats before;
            public RuntimeStats after;
            public CheckEntry[] checks = Array.Empty<CheckEntry>();
            public string[] failures = Array.Empty<string>();
            public string[] unverified = Array.Empty<string>();
        }

        [Serializable]
        private sealed class CaptureReport
        {
            public string utc;
            public string status;
            public string id;
            public string scope = "Actual uGUI Game View capture staged through EditorApplication.update. Page setup is diagnostic and is not natural gameplay input evidence.";
            public string setup;
            public string scene;
            public string page;
            public int width;
            public int height;
            public long totalAllocatedBytes;
            public long systemMemoryBytes;
            public float systemCommitRatio;
            public float systemCommitRatioAtIssue;
            public bool memoryBelow85Percent;
            public bool screenshotWritten;
            public string screenshot;
            public long screenshotBytes;
            public string screenshotUtc;
            public string failure;
        }

        [Serializable]
        private sealed class SeedReport
        {
            public string utc;
            public string status;
            public string scope = "Curated collection fixture created only in the active suffixed review save through WorldMacroPlaytestSession.TryCollectFragmentBundle. Investigation/NPC completion and legacy record IDs are preserved; no journal fixture is generated.";
            public string saveIsolationSuffix;
            public int bundleCount;
            public int fragmentItemCount;
            public int knownSpellCount;
            public int preservedLegacyRecordCount;
            public int collectionEvents;
            public string[] dataSources = Array.Empty<string>();
            public string[] failures = Array.Empty<string>();
        }

        [Serializable]
        private sealed class LayoutSample
        {
            public string resolution;
            public float logicalWidth;
            public float logicalHeight;
            public float runtimeFrameScale;
            public float runtimeTitleScale;
            public bool fixedPanelsFit;
        }

        [Serializable]
        private sealed class LayoutReport
        {
            public string utc;
            public string status;
            public string scope = "Analytical CanvasScaler and runtime adaptive-panel fit at four target resolutions plus actual active uGUI Text preferred-size checks for each diagnostic page. No Game View resolution was changed.";
            public string mode;
            public float uiScale;
            public float textScale;
            public string actualCanvasRect;
            public LayoutSample[] samples = Array.Empty<LayoutSample>();
            public string[] inspectedPages = Array.Empty<string>();
            public string[] textOverflows = Array.Empty<string>();
            public string[] failures = Array.Empty<string>();
        }

        private enum CheckPhase
        {
            Prepare, DrawPress, DrawObserved, PauseObserved, BlockedInputObserved,
            CloseObserved, NeutralAfterClose, MapObserved, MapExpanded, MapClosing,
            MapFoldGuard, MapClosed, FragmentFocus, FragmentCollected, FragmentDuplicate,
            FragmentDetailClose, FragmentDetailNeutral, Finish
        }

        private enum CapturePhase { Prepare, WaitPage, WaitFoldSample, Issue, WaitFile }

        private static bool _checksRunning;
        private static CheckPhase _checkPhase;
        private static double _deadline;
        private static double _phaseStarted;
        private static int _startFrame;
        private static int _phaseFrame;
        private static int _lastCheckFrame;
        private static int _neutralFrames;
        private static int _gateRecoveryFrames;
        private static int _fragmentIndex;
        private static int _collectionEvents;
        private static int _committedEvents;
        private static int _diagnosedEvents;
        private static int _fragmentEventsBeforeDuplicate;
        private static double _mapCloseElapsed;
        private static bool _cleanupReported;
        private static string _progressBeforeBlockedInput;
        private static Vector3 _positionBeforeBlockedInput;
        private static Vector3 _originalFeet;
        private static float _originalYaw;
        private static float _originalHp;
        private static float _originalInk;
        private static bool _originalCombatActive;
        private static UiProgress _originalUi;
        private static RuntimeStats _beforeStats;
        private static PlaytestUiRoot _root;
        private static WorldMacroPlaytestSession _session;
        private static PlayerVitals _vitals;
        private static HudController _hud;
        private static WorldMacroFragmentPickup[] _pickups;
        private static Vector3 _fragmentReviewFeet;
        private static readonly List<CheckEntry> CheckEntries = new List<CheckEntry>();
        private static readonly List<string> CheckFailures = new List<string>();

        private static bool _captureRunning;
        private static CapturePhase _capturePhase;
        private static string _captureId;
        private static string _capturePath;
        private static double _captureReadyAt;
        private static int _captureIssuedFrame;
        private static int _lastCaptureFrame;
        private static CaptureReport _captureReport;

        static PlaytestMenuReview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Abort;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) Abort();
            };
        }

        public static string Execute(string argument)
        {
            string command = (argument ?? string.Empty).Trim();
            string lower = command.ToLowerInvariant();
            if (lower == "status") return Status();
            if (lower == "checks-begin") return BeginChecks();
            if (lower == "checks-poll") return PollChecks();
            if (lower == "capture-poll") return PollCapture();
            if (lower == "clear-test-slot") return ClearTestSlot();
            if (lower == "seed-review") return SeedReview();
            if (lower == "layout-check") return RunLayoutCheck();
            if (lower == "layout-large") return RunLayoutCheck(true);
            if (lower.StartsWith("capture:", StringComparison.Ordinal)) return BeginCapture(lower.Substring(8));
            if (lower.StartsWith("page:", StringComparison.Ordinal)) return SetPage(command.Substring(5));
            if (lower.StartsWith("test-slot:", StringComparison.Ordinal)) return SetTestSlot(command.Substring(10));
            if (lower.StartsWith("button:", StringComparison.Ordinal)) return InvokeButton(command.Substring(7));
            if (lower.StartsWith("options-tab:", StringComparison.Ordinal)) return SelectOptionsTab(lower.Substring(12));
            throw new ArgumentException("Expected status, checks-begin/checks-poll, capture:<id>/capture-poll, page:<name>, button:<name>, options-tab:<name>, seed-review, layout-check/layout-large, test-slot:<suffix>, or clear-test-slot.");
        }

        private static string ClearTestSlot()
        {
            Need(!EditorApplication.isPlayingOrWillChangePlaymode, "clear-test-slot is Edit Mode only.");
            SessionState.EraseString(ReviewSuffixKey);
            return "REVIEW_SLOT_CLEARED key=" + ReviewSuffixKey + "; set a new test-slot before entering review Play Mode";
        }

        private static string InvokeButton(string raw)
        {
            RequirePlaying();
            Need(!_checksRunning && !_captureRunning, "Wait for the current review operation to finish.");
            string requested = (raw ?? string.Empty).Trim();
            Need(requested.Length > 0, "Button name or visible label is required.");
            PlaytestUiRoot root = FindRoot();
            Button[] active = root.GetComponentsInChildren<Button>(false).Where(button => button != null && button.gameObject.activeInHierarchy).ToArray();
            Button[] matches = active.Where(button => string.Equals(button.name, requested, StringComparison.OrdinalIgnoreCase)
                || button.GetComponentsInChildren<Text>(true).Any(text => string.Equals((text.text ?? string.Empty).Trim(), requested, StringComparison.OrdinalIgnoreCase))).ToArray();
            Need(matches.Length == 1, "Expected one active button matching '" + requested + "'; found " + matches.Length + ". Active buttons=" + string.Join(",", active.Select(button => button.name)));
            Need(matches[0].interactable, "Matched button is not interactable: " + matches[0].name);
            matches[0].onClick.Invoke();
            return "BUTTON_INVOKED name=" + matches[0].name + " evidence=diagnostic_uGUI_Button.onClick actualPage=" + root.Page;
        }

        private static string SelectOptionsTab(string id)
        {
            RequirePlaying();
            string label;
            switch ((id ?? string.Empty).Trim())
            {
                case "display": label = "화면"; break;
                case "audio": label = "소리"; break;
                case "input": label = "조작"; break;
                case "access": case "accessibility": label = "접근성"; break;
                default: throw new ArgumentException("Expected options-tab:display, audio, input, or access.");
            }
            PlaytestUiRoot root = FindRoot();
            if (root.Page != "옵션") root.OpenPage("옵션");
            return InvokeButton("OptionTab_" + label);
        }

        private static string SeedReview()
        {
            RequirePlaying();
            Need(!_checksRunning && !_captureRunning, "Wait for the current review operation to finish.");
            Need(SceneManager.GetActiveScene().path == WorldMacroPlaytestAuthoring.ScenePath, "seed-review requires W_WorldMacro_Playtest.");
            BindRuntime();
            string suffix = SessionState.GetString(ReviewSuffixKey, string.Empty);
            Need(!string.IsNullOrWhiteSpace(suffix) && _session.TestSaveSuffix == suffix,
                "seed-review requires a running suffixed review slot. Exit Play, run test-slot:<suffix>, and re-enter Play.");
            Need(_session.Progress != null && _session.Progress.ui != null, "The playtest session is not initialized yet.");
            Need(_root.Page.Length == 0 && !_root.Pause.IsPaused, "Close menus before seed-review.");
            bool restoreBlockedGate = _root.Gate.InputBlocked;

            Directory.CreateDirectory(ValidationFolder);
            var failures = new List<string>();
            var sources = new List<string>();
            Vector3 originalFeet = _session.Walker.Body.transform.position;
            float originalYaw = _session.Walker.Body.transform.eulerAngles.y;
            bool originalCombat = _session.CombatActive;
            PlayerVitals seedVitals = _session.Walker.Body.GetComponent<PlayerVitals>();
            float originalHp = seedVitals != null ? seedVitals.Hp01 : -1f;
            int collectionEvents = 0;
            Action<WorldMacroCollectionNotice> collected = notice => { if (notice != null) collectionEvents++; };
            _session.CollectionChanged += collected;
            try
            {
                // A synchronous, isolated screenshot fixture; never counted as input acceptance QA.
                _root.Gate.ReleaseImmediately();
                sources.Add("fixture setup: temporarily released background focus gate for synchronous diagnostic collection; native input acceptance is unverified");
                _session.CombatActive = false;
                _session.Cull();
                WorldMacroFragmentPickup[] pickups = Object.FindObjectsByType<WorldMacroFragmentPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .OrderBy(item => item.BundleId, StringComparer.Ordinal).ToArray();
                foreach (WorldMacroFragmentPickup pickup in pickups)
                {
                    if (!WorldMacroCollectionCatalog.TryGetBundle(pickup.BundleId, out FragmentBundleDefinition bundle))
                    { failures.Add("Unknown fragment bundle: " + pickup.BundleId); continue; }
                    if (WorldMacroInventoryService.IsBundleCollected(_session.Progress.ui, bundle))
                    { sources.Add("bundle " + bundle.Id + ": already present in review save"); continue; }
                    pickup.RefreshCollectedState(false);
                    if (!TryPlaceNear(pickup.InteractionPosition, pickup.Radius, out Vector3 feet))
                    { failures.Add("No safe reachable review feet near fragment bundle " + bundle.Id + "."); continue; }
                    _session.Teleport(feet, originalYaw);
                    Physics.SyncTransforms();
                    bool accepted = _session.TryCollectFragmentBundle(bundle.Id, out WorldMacroCollectionNotice notice);
                    if (!accepted || notice == null || !WorldMacroInventoryService.IsBundleCollected(_session.Progress.ui, bundle))
                        failures.Add("TryCollectFragmentBundle rejected " + bundle.Id + "; " + DescribePickupFocus(pickup));
                    else sources.Add("bundle " + bundle.Id + ": WorldMacroPlaytestSession.TryCollectFragmentBundle after diagnostic teleport");
                }

            }
            catch (Exception exception)
            {
                failures.Add(exception.GetType().Name + ": " + exception.Message);
            }
            finally
            {
                _session.CollectionChanged -= collected;
                _session.Teleport(originalFeet, originalYaw);
                if (seedVitals != null && originalHp >= 0f) seedVitals.Restore(originalHp);
                _session.CombatActive = originalCombat;
                _session.Cull();
                if (!_session.SaveNow(out string error)) failures.Add("Could not persist curated review fixture: " + error);
                if (restoreBlockedGate) { _root.Gate.Block(); _root.Gate.ReleaseWhenNeutral(); }
            }

            UiProgress ui = _session.Progress.ui;
            int bundles = WorldMacroCollectionCatalog.AllBundles.Count(bundle => WorldMacroInventoryService.IsBundleCollected(ui, bundle));
            int fragments = ui.items.Sum(item => item != null ? item.count : 0);
            if (bundles != 5 || fragments != 20 || ui.knownSpellLetters.Count != 20)
                failures.Add("Fixture totals incomplete: bundles=" + bundles + ", fragments=" + fragments + ", letters=" + ui.knownSpellLetters.Count + ".");
            var report = new SeedReport
            {
                utc = DateTime.UtcNow.ToString("O"), status = failures.Count == 0 ? "PASS" : "FAIL",
                saveIsolationSuffix = suffix, bundleCount = bundles, fragmentItemCount = fragments,
                knownSpellCount = ui.knownSpellLetters.Count, preservedLegacyRecordCount = ui.records.Count,
                collectionEvents = collectionEvents,
                dataSources = sources.ToArray(), failures = failures.ToArray()
            };
            string path = Path.Combine(ValidationFolder, "seed_review.json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            return report.status + " seed-review bundles=" + bundles + " fragments=" + fragments + " letters=" + ui.knownSpellLetters.Count
                + " report=" + path;
        }

        private static string RunLayoutCheck(bool large = false)
        {
            RequirePlaying();
            Need(!_checksRunning && !_captureRunning, "Wait for the current review operation to finish.");
            PlaytestUiRoot root = FindRoot();
            Need(root.Settings != null, "Playtest settings are unavailable.");
            Need(!large || !root.Settings.IsPreviewing, "layout-large requires no existing settings preview so its rollback is unambiguous.");
            UserSettingsData original = root.Settings.Current;
            if (large)
            {
                UserSettingsData preview = original.Clone(); preview.UiScale = 1.2f; preview.TextScale = 1.25f;
                root.Settings.Preview(preview);
            }
            try { return RunLayoutCheckCore(root, large); }
            finally { if (large && root.Settings.IsPreviewing) root.Settings.Revert(); }
        }

        private static string RunLayoutCheckCore(PlaytestUiRoot root, bool large)
        {
            Canvas menuCanvas = root.GetComponentsInChildren<Canvas>(true).FirstOrDefault(candidate => candidate.sortingOrder == 80);
            Need(menuCanvas != null, "Playtest menu canvas is unavailable.");
            CanvasScaler canvasScaler = menuCanvas.GetComponent<CanvasScaler>();
            Need(canvasScaler != null && canvasScaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize,
                "Layout review requires the playtest ScaleWithScreenSize canvas.");

            Directory.CreateDirectory(ValidationFolder);
            UserSettingsData settings = root.Settings.Current;
            Vector2 reference = canvasScaler.referenceResolution;
            var samples = new List<LayoutSample>();
            var failures = new List<string>();
            foreach ((int width, int height, string label) in new[]
            {
                (1280, 720, "1280x720"), (1920, 1080, "1920x1080"),
                (2560, 1440, "2560x1440"), (3440, 1440, "3440x1440")
            })
            {
                float sx = width / reference.x, sy = height / reference.y;
                float scale = Mathf.Exp(Mathf.Lerp(Mathf.Log(sx), Mathf.Log(sy), canvasScaler.matchWidthOrHeight));
                float logicalWidth = width / scale, logicalHeight = height / scale;
                float frameScale = Mathf.Min(1f, (logicalWidth - 64f) / 1560f, (logicalHeight - 64f) / 900f);
                float titleScale = Mathf.Min(1f, (logicalHeight - 70f) / 904f);
                bool fits = frameScale > 0f && titleScale > 0f
                    && 1560f * frameScale <= logicalWidth - 64f + .1f && 900f * frameScale <= logicalHeight - 64f + .1f
                    && 904f * titleScale <= logicalHeight - 70f + .1f;
                samples.Add(new LayoutSample { resolution = label, logicalWidth = logicalWidth, logicalHeight = logicalHeight,
                    runtimeFrameScale = frameScale, runtimeTitleScale = titleScale, fixedPanelsFit = fits });
                if (!fits) failures.Add(label + " adaptive runtime panel formula does not fit logical canvas "
                    + logicalWidth.ToString("0.0") + "x" + logicalHeight.ToString("0.0") + ".");
            }

            string originalPage = root.Page;
            var inspected = new List<string>();
            var overflows = new List<string>();
            string[] pageNames = root.IsTitle ? new[] { "title", "옵션" } : new[] { "일시정지", "소지품", "술식 도감", "차패", "옵션" };
            try
            {
                foreach (string page in pageNames)
                {
                    if (page != "title") root.OpenPage(page);
                    Canvas.ForceUpdateCanvases();
                    if (page == "옵션")
                    {
                        foreach (string tab in new[] { "화면", "소리", "조작", "접근성" })
                        {
                            InvokeButton("OptionTab_" + tab); Canvas.ForceUpdateCanvases();
                            InspectActiveText(root, page + "/" + tab, inspected, overflows);
                        }
                    }
                    else InspectActiveText(root, page, inspected, overflows);
                }
            }
            finally
            {
                if (string.IsNullOrEmpty(originalPage) && root.Page.Length > 0) root.CloseMenu();
                else if (root.Page != originalPage) root.OpenPage(originalPage);
            }
            if (overflows.Count > 0) failures.Add(overflows.Count + " active Text element(s) overflow at the current Game View and text scale.");
            var report = new LayoutReport
            {
                utc = DateTime.UtcNow.ToString("O"), status = failures.Count == 0 ? "PASS" : "FAIL",
                mode = large ? "UNPERSISTED_PREVIEW UiScale=1.20 TextScale=1.25; reverted in finally" : "CURRENT_CONFIRMED_SETTINGS",
                uiScale = settings.UiScale, textScale = settings.TextScale,
                actualCanvasRect = ((RectTransform)menuCanvas.transform).rect.ToString(), samples = samples.ToArray(),
                inspectedPages = inspected.ToArray(), textOverflows = overflows.ToArray(), failures = failures.ToArray()
            };
            string path = Path.Combine(ValidationFolder, large ? "layout_large_review.json" : "layout_review.json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            return report.status + " layout-check samples=" + samples.Count + " overflows=" + overflows.Count + " report=" + path;
        }

        private static string SetTestSlot(string raw)
        {
            Need(!_checksRunning && !_captureRunning, "Wait for the current review operation to finish.");
            string token = (raw ?? string.Empty).Trim();
            Need(token.Length > 0 && token.Length <= 32, "Review suffix must contain 1-32 characters.");
            Need(token.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'), "Review suffix may contain only letters, digits, underscore, and hyphen.");
            string suffix = token.StartsWith("_", StringComparison.Ordinal) ? token : "_" + token;
            Need(suffix != "_", "A production-empty suffix is forbidden for review.");
            SessionState.SetString(ReviewSuffixKey, suffix);
            return "REVIEW_SLOT_SET key=" + ReviewSuffixKey + " suffix=" + suffix + " restartPlayRequired=true";
        }

        private static string SetPage(string raw)
        {
            RequirePlaying();
            Need(!_checksRunning && !_captureRunning, "Wait for the current review operation to finish.");
            PlaytestUiRoot root = FindRoot();
            string id = NormalizePageId(raw);
            if (id == "title") Need(root.IsTitle, "The title page requires W_Playtest_Title.");
            else
            {
                Need(!root.IsTitle, "Gameplay menu pages require W_WorldMacro_Playtest.");
                if (id == "hud" || id == "empty") root.CloseMenu();
                else root.OpenPage(PageName(id));
            }
            return "PAGE_SET id=" + id + " actual=" + root.Page + " setup=directDiagnosticPageCommand";
        }

        private static string BeginChecks()
        {
            RequirePlaying();
            Need(!_checksRunning && !_captureRunning, "Another menu review operation is running.");
            Need(SceneManager.GetActiveScene().path == WorldMacroPlaytestAuthoring.ScenePath, "Checks require W_WorldMacro_Playtest.");
            BindRuntime();
            string suffix = SessionState.GetString(ReviewSuffixKey, string.Empty);
            Need(!string.IsNullOrWhiteSpace(suffix), "Set a non-empty review slot before entering Play Mode: test-slot:<suffix>.");
            Need(_session.TestSaveSuffix == suffix, "The running session did not start with the review suffix. Exit Play, set the suffix, and re-enter Play.");
            Need(_session.Progress != null && _session.Progress.ui != null, "The playtest session is not initialized yet.");
            Need(Keyboard.current != null, "No Input System keyboard is available for synthetic review input.");
            FocusGameView();

            Directory.CreateDirectory(ValidationFolder);
            CheckEntries.Clear(); CheckFailures.Clear();
            _checksRunning = true; _checkPhase = CheckPhase.Prepare;
            _deadline = EditorApplication.timeSinceStartup + OperationTimeoutSeconds;
            _startFrame = Time.frameCount; _phaseFrame = Time.frameCount; _lastCheckFrame = -1; _phaseStarted = EditorApplication.timeSinceStartup;
            _neutralFrames = _gateRecoveryFrames = _fragmentIndex = _collectionEvents = _committedEvents = _diagnosedEvents = 0;
            _cleanupReported = false;
            _originalFeet = _session.Walker.Body.transform.position;
            _originalYaw = _session.Walker.Body.transform.eulerAngles.y;
            _originalUi = _session.Progress.ui.Copy();
            _originalCombatActive = _session.CombatActive;
            _vitals = _session.Walker.Body.GetComponent<PlayerVitals>();
            _hud = Object.FindFirstObjectByType<HudController>();
            _originalHp = _vitals != null ? _vitals.Hp01 : -1f;
            _originalInk = _hud != null ? _hud.Ink01 : -1f;
            _beforeStats = BuildStats(_originalHp, _originalInk);
            _pickups = Object.FindObjectsByType<WorldMacroFragmentPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .OrderBy(item => item.BundleId, StringComparer.Ordinal).ToArray();
            _session.CollectionChanged += OnCollection;
            _session.Walker.Drawing.Committed += OnCommitted;
            _session.Walker.Drawing.CommitDiagnosed += OnDiagnosed;
            EditorApplication.update -= TickChecks;
            EditorApplication.update += TickChecks;
            return "CHECKS_STARTED input=synthetic_InputSystem_QueueStateEvent slot=" + suffix + " poll=checks-poll";
        }

        private static void TickChecks()
        {
            if (!_checksRunning || !EditorApplication.isPlaying || EditorApplication.isPaused) { FailChecks("Play Mode stopped or paused during checks."); return; }
            if (EditorApplication.timeSinceStartup > _deadline) { FailChecks("Checks exceeded the 90 second timeout at " + _checkPhase + "."); return; }
            if (_lastCheckFrame == Time.frameCount) return;
            _lastCheckFrame = Time.frameCount;
            try
            {
                switch (_checkPhase)
                {
                    case CheckPhase.Prepare:
                        _session.CombatActive = false; _session.Cull();
                        if (_root.Page.Length > 0 || _root.Pause.IsPaused) { _root.CloseMenu(); QueueKeys(); }
                        if (_root.Gate.InputBlocked)
                        {
                            if (_gateRecoveryFrames == 0) FocusGameView();
                            QueueKeys(); _gateRecoveryFrames++;
                            Need(EditorApplication.timeSinceStartup - _phaseStarted <= 5d,
                                "Prepare could not reach neutral gameplay within 5 seconds; " + GateDiagnostics());
                            return;
                        }
                        _gateRecoveryFrames = 0;
                        Add("isolated-save-slot", true, "running suffix=" + _session.TestSaveSuffix);
                        RunSaveSlotChecks();
                        _session.Progress.ui.CopyFrom(new UiProgress());
                        foreach (WorldMacroFragmentPickup pickup in _pickups) pickup.RefreshCollectedState(false);
                        QueueKeys(Key.Q); Next(CheckPhase.DrawPress); break;

                    case CheckPhase.DrawPress:
                        Need(_session.Walker.Drawing.InDrawMode, "Synthetic Q did not enter drawing mode.");
                        QueueKeys(Key.Q, Key.Escape); Next(CheckPhase.DrawObserved); break;

                    case CheckPhase.DrawObserved:
                        Add("pause-opens-through-menu-action", _root.Page == "일시정지" && _root.Pause.IsPaused, "page=" + _root.Page);
                        Add("unfinished-draw-cancelled", !_session.Walker.Drawing.InDrawMode && _committedEvents == 0 && _diagnosedEvents == 0,
                            "committedEvents=" + _committedEvents + ", diagnosedEvents=" + _diagnosedEvents);
                        Add("pause-blocks-gameplay", _root.Gate.InputBlocked && Time.timeScale <= .0001f,
                            "gate=" + _root.Gate.InputBlocked + ", timeScale=" + Time.timeScale);
                        _positionBeforeBlockedInput = _session.Walker.Body.transform.position;
                        _progressBeforeBlockedInput = JsonUtility.ToJson(_session.Progress.ledger);
                        QueueKeys(); Next(CheckPhase.PauseObserved); break;

                    case CheckPhase.PauseObserved:
                        QueueKeys(Key.W, Key.F, Key.Q); Next(CheckPhase.BlockedInputObserved); break;

                    case CheckPhase.BlockedInputObserved:
                        Add("blocked-w-f-q-have-no-gameplay-effect",
                            !_session.Walker.Drawing.InDrawMode
                            && Vector3.Distance(_positionBeforeBlockedInput, _session.Walker.Body.transform.position) < .001f
                            && _progressBeforeBlockedInput == JsonUtility.ToJson(_session.Progress.ledger)
                            && _collectionEvents == 0,
                            "drawing=" + _session.Walker.Drawing.InDrawMode + ", distance=" + Vector3.Distance(_positionBeforeBlockedInput, _session.Walker.Body.transform.position));
                        QueueKeys(); Next(CheckPhase.CloseObserved); break;

                    case CheckPhase.CloseObserved:
                        if (FramesInPhase < 2) return;
                        QueueKeys(Key.Escape); Next(CheckPhase.NeutralAfterClose); break;

                    case CheckPhase.NeutralAfterClose:
                        if (_root.Page.Length > 0) return;
                        if (FramesInPhase == 1)
                        {
                            Add("closing-key-does-not-resume-same-frame", _root.Gate.InputBlocked && !_root.Pause.IsPaused,
                                "gate=" + _root.Gate.InputBlocked + ", releasePending=" + _root.Gate.ReleasePending);
                            QueueKeys(); return;
                        }
                        if (_root.Gate.InputBlocked) return;
                        QueueKeys(Key.M); Next(CheckPhase.MapObserved); break;

                    case CheckPhase.MapObserved:
                        Need(_root.Page == "지도" && _root.Pause.IsPaused && _root.Gate.InputBlocked, "Synthetic M did not open the paused map.");
                        QueueKeys(); Next(CheckPhase.MapExpanded); break;

                    case CheckPhase.MapExpanded:
                        if (EditorApplication.timeSinceStartup - _phaseStarted < .45d || (_root.Map != null && _root.Map.Folding)) return;
                        Add("map-open-fold-duration", _root.Map != null && _root.Map.Expanded, "waited>=0.45s unscaled");
                        QueueKeys(Key.M); Next(CheckPhase.MapClosing); break;

                    case CheckPhase.MapClosing:
                        Need(_root.Page == "지도" && _root.Pause.IsPaused, "Map closed before its fold transition was retained.");
                        QueueKeys(); _neutralFrames = 0; Next(CheckPhase.MapFoldGuard); break;

                    case CheckPhase.MapFoldGuard:
                        double foldElapsed = EditorApplication.timeSinceStartup - _phaseStarted;
                        if (foldElapsed < .12d) return;
                        if (_neutralFrames == 0)
                        {
                            Add("map-close-keeps-pause-during-fold", _root.Page == "지도" && _root.Gate.InputBlocked && _root.Pause.IsPaused,
                                "sampleAt=" + foldElapsed.ToString("0.000") + "s");
                            _neutralFrames = 1;
                        }
                        if (_root.Page.Length > 0) return;
                        _mapCloseElapsed = foldElapsed;
                        Next(CheckPhase.MapClosed); break;

                    case CheckPhase.MapClosed:
                        if (_root.Gate.InputBlocked) return;
                        Add("map-close-finishes-before-input-resume", !_root.Pause.IsPaused && _mapCloseElapsed >= .24d,
                            "foldElapsed=" + _mapCloseElapsed.ToString("0.000") + "s; neutralComplete=true");
                        _fragmentIndex = 0; _fragmentReviewFeet = new Vector3(float.NaN, float.NaN, float.NaN); Next(CheckPhase.FragmentFocus); break;

                    case CheckPhase.FragmentFocus:
                        if (_fragmentIndex >= _pickups.Length) { Next(CheckPhase.Finish); break; }
                        WorldMacroFragmentPickup focusPickup = _pickups[_fragmentIndex];
                        if (_root.Gate.InputBlocked)
                        {
                            Need(_root.Page.Length == 0 && !_root.Pause.IsPaused && Time.timeScale > .0001f,
                                "Gameplay gate remained blocked by an active menu/pause before bundle " + focusPickup.BundleId + ".");
                            if (_gateRecoveryFrames == 0) FocusGameView();
                            QueueKeys();
                            _gateRecoveryFrames++;
                            Need(_gateRecoveryFrames <= 120, "Gameplay gate did not complete its normal two-neutral-frame focus recovery before bundle " + focusPickup.BundleId + ".");
                            return;
                        }
                        _gateRecoveryFrames = 0;
                        if (!float.IsFinite(_fragmentReviewFeet.x))
                        {
                            focusPickup.RefreshCollectedState(false);
                            Need(TryPlaceNear(focusPickup.InteractionPosition, focusPickup.Radius, out _fragmentReviewFeet),
                                "No safe reachable review feet near fragment bundle " + focusPickup.BundleId + ".");
                            _session.Teleport(_fragmentReviewFeet, _originalYaw);
                            Physics.SyncTransforms();
                            return;
                        }
                        if (_session.FocusedCollectionBundleId != focusPickup.BundleId)
                        {
                            Need(FramesInPhase < 60, "Could not focus fragment bundle " + focusPickup.BundleId + " after teleport; " + DescribePickupFocus(focusPickup));
                            return;
                        }
                        QueueKeys(Key.F); Next(CheckPhase.FragmentCollected); break;

                    case CheckPhase.FragmentCollected:
                        WorldMacroFragmentPickup collected = _pickups[_fragmentIndex];
                        Need(WorldMacroCollectionCatalog.TryGetBundle(collected.BundleId, out FragmentBundleDefinition definition), "Unknown bundle " + collected.BundleId);
                        Need(WorldMacroInventoryService.IsBundleCollected(_session.Progress.ui, definition), "Synthetic F did not collect " + collected.BundleId);
                        _fragmentEventsBeforeDuplicate = _collectionEvents;
                        QueueKeys(); Next(CheckPhase.FragmentDuplicate); break;

                    case CheckPhase.FragmentDuplicate:
                        if (FramesInPhase == 1) { QueueKeys(Key.F); return; }
                        if (FramesInPhase == 2) { QueueKeys(); return; }
                        Add("bundle-" + _pickups[_fragmentIndex].BundleId + "-fires-once", _collectionEvents == _fragmentEventsBeforeDuplicate,
                            "collectionEvents=" + _collectionEvents);
                        if (_root.Page.Length > 0)
                        {
                            Add("duplicate-f-near-" + _pickups[_fragmentIndex].BundleId + "-opens-legitimate-interaction",
                                _pickups[_fragmentIndex].BundleId == "inn" && _root.Page == "상세" && _root.Pause.IsPaused,
                                "page=" + _root.Page + ", interaction is separate from the already-collected bundle");
                            QueueKeys(Key.Escape); Next(CheckPhase.FragmentDetailClose); return;
                        }
                        _fragmentIndex++; _fragmentReviewFeet = new Vector3(float.NaN, float.NaN, float.NaN); Next(CheckPhase.FragmentFocus); break;

                    case CheckPhase.FragmentDetailClose:
                        if (_root.Page.Length > 0) return;
                        Add("legitimate-interaction-closes-through-escape", !_root.Pause.IsPaused && _root.Gate.InputBlocked,
                            "page cleared; closing Escape remains gated until neutral");
                        QueueKeys(); Next(CheckPhase.FragmentDetailNeutral); break;

                    case CheckPhase.FragmentDetailNeutral:
                        QueueKeys();
                        if (_root.Gate.InputBlocked) return;
                        _fragmentIndex++; _fragmentReviewFeet = new Vector3(float.NaN, float.NaN, float.NaN); Next(CheckPhase.FragmentFocus); break;

                    case CheckPhase.Finish:
                        FinishChecks(); break;
                }
            }
            catch (Exception exception) { FailChecks(exception.GetType().Name + ": " + exception.Message); }
        }

        private static void RunSaveSlotChecks()
        {
            string directory = Path.Combine(Path.GetTempPath(), "OheangbuMenuReview-" + Guid.NewGuid().ToString("N"));
            const string slot = "review-slot";
            try
            {
                Directory.CreateDirectory(directory);
                string primary = WorldMacroSaveSlot.GetPrimaryPath(directory, slot);
                string valid = JsonUtility.ToJson(_session.Progress, true);
                File.WriteAllText(primary, valid);
                Add("save-slot-primary-inspection", WorldMacroSaveSlot.Inspect(directory, slot).Status == WorldMacroSaveSlotStatus.Primary, primary);
                File.WriteAllText(primary + ".bak", valid);
                File.WriteAllText(primary, "{ corrupt review primary");
                WorldMacroSaveSlotInfo recovered = WorldMacroSaveSlot.Inspect(directory, slot);
                Add("save-slot-corrupt-primary-recovers-backup", recovered.Status == WorldMacroSaveSlotStatus.Backup && recovered.LoadedPath == primary + ".bak", recovered.Error ?? "backup");
                File.WriteAllText(primary + ".tmp", "temporary review artifact");
                bool archived = WorldMacroSaveSlot.TryArchiveForNew(directory, slot, DateTime.UtcNow, out string archive, out string error);
                bool allMoved = archived && Directory.Exists(archive) && Directory.GetFiles(archive).Length == 3 && WorldMacroSaveSlot.Inspect(directory, slot).Status == WorldMacroSaveSlotStatus.New;
                Add("save-slot-new-game-archives-all-artifacts", allMoved, error ?? archive);
            }
            finally
            {
                try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
                catch (Exception exception) { CheckFailures.Add("Temporary save fixture cleanup failed: " + exception.Message); }
            }
        }

        private static void FinishChecks()
        {
            int fragments = _session.Progress.ui.items.Sum(item => item != null ? item.count : 0);
            int unique = WorldMacroCollectionCatalog.AllBundles.Count(bundle => WorldMacroInventoryService.IsBundleCollected(_session.Progress.ui, bundle));
            int letters = _session.Progress.ui.knownSpellLetters.Count;
            Add("five-bundles-and-twenty-fragments", _collectionEvents == 5 && unique == 5 && fragments == 20 && _session.Progress.ui.knownSpellLetters.Count == 20,
                "events=" + _collectionEvents + ", bundles=" + unique + ", fragments=" + fragments + ", letters=" + _session.Progress.ui.knownSpellLetters.Count);
            Add("player-hp-and-ink-unchanged", (_vitals == null || Mathf.Approximately(_vitals.Hp01, _originalHp)) && (_hud == null || Mathf.Approximately(_hud.Ink01, _originalInk)),
                "hp=" + (_vitals != null ? _vitals.Hp01.ToString("0.000") : "unavailable") + ", ink=" + (_hud != null ? _hud.Ink01.ToString("0.000") : "unavailable"));

            CleanupCheckState();
            RuntimeStats after = BuildStats(_vitals != null ? _vitals.Hp01 : -1f, _hud != null ? _hud.Ink01 : -1f);
            var report = new CheckReport
            {
                utc = DateTime.UtcNow.ToString("O"), status = CheckFailures.Count == 0 ? "PASS" : "FAIL",
                saveIsolationSuffix = _session != null ? _session.TestSaveSuffix : SessionState.GetString(ReviewSuffixKey, string.Empty),
                elapsedFrames = Time.frameCount - _startFrame, collectionEvents = _collectionEvents,
                uniqueBundlesCollected = unique, fragmentItemsCollected = fragments, knownSpellLetters = letters,
                productionSlotUntouchedByContract = !string.IsNullOrWhiteSpace(SessionState.GetString(ReviewSuffixKey, string.Empty)),
                before = _beforeStats, after = after, checks = CheckEntries.ToArray(), failures = CheckFailures.ToArray(),
                unverified = new[] { "Vehicle occupancy pause behavior: no occupied vehicle fixture was forced.", "Native keyboard/mouse/controller device input.", "Manual handwriting and human visual/audio approval." }
            };
            string path = Path.Combine(ValidationFolder, CheckReportName);
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            _checksRunning = false; EditorApplication.update -= TickChecks;
        }

        private static void FailChecks(string failure)
        {
            if (!_checksRunning) return;
            CheckFailures.Add(failure);
            int partialBundles = CurrentBundleCount();
            int partialFragments = CurrentFragmentCount();
            int partialLetters = CurrentLetterCount();
            try { CleanupCheckState(); } catch (Exception cleanup) { CheckFailures.Add("Cleanup failed: " + cleanup.Message); }
            var report = new CheckReport
            {
                utc = DateTime.UtcNow.ToString("O"), status = "FAIL", saveIsolationSuffix = SessionState.GetString(ReviewSuffixKey, string.Empty),
                elapsedFrames = EditorApplication.isPlaying ? Time.frameCount - _startFrame : 0,
                collectionEvents = _collectionEvents,
                uniqueBundlesCollected = partialBundles, fragmentItemsCollected = partialFragments, knownSpellLetters = partialLetters,
                productionSlotUntouchedByContract = _session != null && !string.IsNullOrWhiteSpace(_session.TestSaveSuffix)
                    && _session.TestSaveSuffix == SessionState.GetString(ReviewSuffixKey, string.Empty),
                before = _beforeStats, checks = CheckEntries.ToArray(), failures = CheckFailures.ToArray(),
                after = EditorApplication.isPlaying ? BuildStats(_vitals != null ? _vitals.Hp01 : -1f, _hud != null ? _hud.Ink01 : -1f) : null,
                unverified = new[] { "Checks aborted before all target states were measured. Counts in this report may be partial.", "Native keyboard/mouse/controller device input and manual handwriting remain unverified." }
            };
            Directory.CreateDirectory(ValidationFolder);
            File.WriteAllText(Path.Combine(ValidationFolder, CheckReportName), JsonUtility.ToJson(report, true));
            _checksRunning = false; EditorApplication.update -= TickChecks;
        }

        private static void CleanupCheckState()
        {
            QueueKeys();
            if (_session != null)
            {
                _session.CollectionChanged -= OnCollection;
                if (_session.Walker != null && _session.Walker.Drawing != null)
                {
                    _session.Walker.Drawing.Committed -= OnCommitted;
                    _session.Walker.Drawing.CommitDiagnosed -= OnDiagnosed;
                    _session.Walker.Drawing.CancelForUi();
                }
                if (_root != null && _root.Page.Length > 0) _root.CloseMenu();
                if (_root != null && _root.Pause != null && _root.Pause.IsPaused) _root.Pause.ForceResume();
                if (_originalUi != null && _session.Progress != null)
                {
                    _session.Progress.ui.CopyFrom(_originalUi);
                    if (_pickups != null) foreach (WorldMacroFragmentPickup pickup in _pickups)
                        if (pickup != null && WorldMacroCollectionCatalog.TryGetBundle(pickup.BundleId, out FragmentBundleDefinition bundle))
                            pickup.RefreshCollectedState(WorldMacroInventoryService.IsBundleCollected(_originalUi, bundle));
                }
                _session.CombatActive = _originalCombatActive;
                if (_session.Walker != null && _session.Walker.Body != null) _session.Teleport(_originalFeet, _originalYaw);
                if (_vitals != null && _originalHp >= 0f) _vitals.Restore(_originalHp);
                _session.Cull();
                bool saved = _session.SaveNow(out string saveError);
                bool uiRestored = _originalUi == null || JsonUtility.ToJson(_session.Progress.ui) == JsonUtility.ToJson(_originalUi);
                bool poseRestored = _session.Walker == null || _session.Walker.Body == null
                    || Vector3.Distance(_session.Walker.Body.transform.position, _originalFeet) < .001f;
                if (!_cleanupReported)
                {
                    bool pageCleared = _root == null || _root.Page.Length == 0;
                    Add("review-state-restored-to-isolated-slot", saved && uiRestored && poseRestored && pageCleared,
                        "saved=" + saved + ", ui=" + uiRestored + ", pose=" + poseRestored + ", pageCleared=" + pageCleared
                        + (saved ? string.Empty : ", error=" + saveError));
                    _cleanupReported = true;
                }
            }
        }

        private static string BeginCapture(string rawId)
        {
            RequirePlaying();
            Need(!_checksRunning && !_captureRunning, "Another menu review operation is running.");
            string id = NormalizePageId(rawId);
            Need(new[] { "title", "hud", "empty", "pause", "inventory", "codex", "chapae", "options", "map", "map_fold" }.Contains(id), "Unsupported capture id: " + id);
            _root = FindRoot();
            Need(Screen.width == 1920 && Screen.height == 1080, "Game View must be exactly 1920x1080; current=" + Screen.width + "x" + Screen.height + ".");
            long total = Profiler.GetTotalAllocatedMemoryLong();
            long system = Math.Max(0L, (long)SystemInfo.systemMemorySize * Megabyte);
            float commitRatio = Prologue.PrologueAudit.CommitRatio();
            Need(commitRatio < .85f, "Capture memory guard refused: system commit=" + commitRatio.ToString("P1") + " (limit 85%).");
            FocusGameView();
            Directory.CreateDirectory(ScreenshotFolder); Directory.CreateDirectory(ValidationFolder);
            _captureId = id; _capturePath = Path.Combine(ScreenshotFolder, id + "_1920x1080.png");
            if (File.Exists(_capturePath)) File.Delete(_capturePath);
            _captureReport = new CaptureReport
            {
                utc = DateTime.UtcNow.ToString("O"), status = "RUNNING", id = id,
                setup = "direct diagnostic page setup; no synthetic input used for this still",
                scene = SceneManager.GetActiveScene().path, width = Screen.width, height = Screen.height,
                totalAllocatedBytes = total, systemMemoryBytes = system, systemCommitRatio = commitRatio,
                memoryBelow85Percent = commitRatio < .85f,
                screenshot = _capturePath
            };
            _captureRunning = true; _capturePhase = CapturePhase.Prepare;
            _deadline = EditorApplication.timeSinceStartup + OperationTimeoutSeconds;
            _captureReadyAt = EditorApplication.timeSinceStartup; _lastCaptureFrame = -1;
            EditorApplication.update -= TickCapture; EditorApplication.update += TickCapture;
            return "CAPTURE_STARTED id=" + id + " path=" + _capturePath + " poll=capture-poll";
        }

        private static void TickCapture()
        {
            if (!_captureRunning) return;
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) { FailCapture("Play Mode stopped or paused during capture."); return; }
            if (EditorApplication.timeSinceStartup > _deadline) { FailCapture("Capture exceeded the 90 second timeout at " + _capturePhase + "."); return; }
            if (_lastCaptureFrame == Time.frameCount) return;
            _lastCaptureFrame = Time.frameCount;
            try
            {
                switch (_capturePhase)
                {
                    case CapturePhase.Prepare:
                        if (_captureId == "title")
                        {
                            Need(_root.IsTitle, "title capture requires W_Playtest_Title.");
                            if (_root.Page.Length > 0) _root.CloseMenu();
                        }
                        else
                        {
                            Need(!_root.IsTitle, _captureId + " capture requires W_WorldMacro_Playtest.");
                            if (_captureId == "hud" || _captureId == "empty") _root.CloseMenu();
                            else _root.OpenPage(PageName(_captureId == "map_fold" ? "map" : _captureId));
                        }
                        _captureReadyAt = EditorApplication.timeSinceStartup + (_captureId == "map" || _captureId == "map_fold" ? .45d : .10d);
                        _capturePhase = CapturePhase.WaitPage; break;

                    case CapturePhase.WaitPage:
                        if (EditorApplication.timeSinceStartup < _captureReadyAt) return;
                        if (_captureId == "title" && _root.Page.Length > 0) return;
                        if ((_captureId == "hud" || _captureId == "empty") && (_root.Page.Length > 0 || _root.Pause.IsPaused)) return;
                        if ((_captureId == "map" || _captureId == "map_fold") && _root.Map != null && _root.Map.Folding) return;
                        if (_captureId != "title" && _captureId != "hud" && _captureId != "empty"
                            && _root.Page != PageName(_captureId == "map_fold" ? "map" : _captureId))
                            throw new InvalidOperationException("Requested page was not active; actual=" + _root.Page + ".");
                        if (_captureId == "map_fold") { _root.CloseMenu(); _captureReadyAt = EditorApplication.timeSinceStartup + .12d; _capturePhase = CapturePhase.WaitFoldSample; return; }
                        _capturePhase = CapturePhase.Issue; break;

                    case CapturePhase.WaitFoldSample:
                        if (EditorApplication.timeSinceStartup < _captureReadyAt) return;
                        Need(_root.Page == "지도" && _root.Map != null && _root.Map.Folding, "Map fold sample was not active at the capture point.");
                        _capturePhase = CapturePhase.Issue; break;

                    case CapturePhase.Issue:
                        _captureReport.systemCommitRatioAtIssue = Prologue.PrologueAudit.CommitRatio();
                        Need(_captureReport.systemCommitRatioAtIssue < .85f,
                            "Capture stopped before screenshot allocation: system commit=" + _captureReport.systemCommitRatioAtIssue.ToString("P1") + " (limit 85%).");
                        _captureReport.page = _root.Page;
                        ScreenCapture.CaptureScreenshot(_capturePath, 1);
                        _captureIssuedFrame = Time.frameCount;
                        _capturePhase = CapturePhase.WaitFile; break;

                    case CapturePhase.WaitFile:
                        if (Time.frameCount < _captureIssuedFrame + 3 || !File.Exists(_capturePath)) return;
                        var info = new FileInfo(_capturePath);
                        Need(info.Length > 0, "Screenshot file is empty.");
                        _captureReport.status = "PASS"; _captureReport.screenshotWritten = true;
                        _captureReport.screenshotBytes = info.Length; _captureReport.screenshotUtc = info.LastWriteTimeUtc.ToString("O");
                        SaveCaptureReport(); _captureRunning = false; EditorApplication.update -= TickCapture; break;
                }
            }
            catch (Exception exception) { FailCapture(exception.GetType().Name + ": " + exception.Message); }
        }

        private static void FailCapture(string failure)
        {
            if (!_captureRunning) return;
            _captureReport.status = "FAIL"; _captureReport.failure = failure;
            _captureReport.screenshotWritten = File.Exists(_capturePath) && new FileInfo(_capturePath).Length > 0;
            if (_captureReport.screenshotWritten) _captureReport.screenshotBytes = new FileInfo(_capturePath).Length;
            SaveCaptureReport(); _captureRunning = false; EditorApplication.update -= TickCapture;
        }

        private static void SaveCaptureReport()
        {
            Directory.CreateDirectory(ValidationFolder);
            File.WriteAllText(Path.Combine(ValidationFolder, "capture_" + _captureId + ".json"), JsonUtility.ToJson(_captureReport, true));
        }

        private static string PollChecks()
        {
            string path = Path.Combine(ValidationFolder, CheckReportName);
            if (_checksRunning) return "RUNNING phase=" + _checkPhase + " frame=" + Time.frameCount + " checks=" + CheckEntries.Count + " failures=" + CheckFailures.Count;
            return File.Exists(path) ? "COMPLETE report=" + path + "\n" + File.ReadAllText(path) : "IDLE no check report";
        }

        private static string PollCapture()
        {
            if (_captureRunning) return "RUNNING id=" + _captureId + " phase=" + _capturePhase + " frame=" + Time.frameCount;
            if (string.IsNullOrEmpty(_captureId)) return "IDLE no capture requested";
            string path = Path.Combine(ValidationFolder, "capture_" + _captureId + ".json");
            return File.Exists(path) ? "COMPLETE report=" + path + "\n" + File.ReadAllText(path) : "IDLE no capture report";
        }

        private static string Status()
        {
            return "checks=" + (_checksRunning ? "RUNNING:" + _checkPhase : "idle")
                + "; capture=" + (_captureRunning ? "RUNNING:" + _captureId + ":" + _capturePhase : "idle")
                + "; reviewSuffix=" + SessionState.GetString(ReviewSuffixKey, "unset")
                + "; playing=" + EditorApplication.isPlaying + "; scene=" + SceneManager.GetActiveScene().path
                + "; resolution=" + Screen.width + "x" + Screen.height + "; captureRequirement=1920x1080_actualGameView";
        }

        private static void BindRuntime()
        {
            _root = FindRoot();
            _session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            Need(_session != null && _root.Session == _session, "The persistent menu root is not bound to the active playtest session.");
            Need(_session.Walker != null && _session.Walker.Body != null && _session.Walker.Drawing != null, "Player/session wiring is incomplete.");
        }

        private static PlaytestUiRoot FindRoot()
        {
            PlaytestUiRoot root = PlaytestUiRoot.Instance != null ? PlaytestUiRoot.Instance : Object.FindFirstObjectByType<PlaytestUiRoot>();
            Need(root != null, "PlaytestUiRoot is not active.");
            return root;
        }

        private static RuntimeStats BuildStats(float hp, float ink)
        {
            UiProgress ui = _session != null && _session.Progress != null ? _session.Progress.ui : null;
            int bundles = ui == null ? 0 : WorldMacroCollectionCatalog.AllBundles.Count(bundle => WorldMacroInventoryService.IsBundleCollected(ui, bundle));
            return new RuntimeStats
            {
                frame = Time.frameCount, width = Screen.width, height = Screen.height,
                totalAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong(), reservedBytes = Profiler.GetTotalReservedMemoryLong(),
                monoHeapBytes = Profiler.GetMonoHeapSizeLong(), systemMemoryMb = SystemInfo.systemMemorySize,
                scene = SceneManager.GetActiveScene().path, page = _root != null ? _root.Page : string.Empty,
                gateBlocked = _root != null && _root.Gate != null && _root.Gate.InputBlocked,
                applicationFocused = Application.isFocused,
                gateReleasePending = _root != null && _root.Gate != null && _root.Gate.ReleasePending,
                gateFocusOwnsBlock = _root != null && _root.Gate != null && _root.Gate.FocusOwnsBlock,
                gateNeutralFrames = _root != null && _root.Gate != null ? _root.Gate.NeutralFrames : 0,
                pauseActive = _root != null && _root.Pause != null && _root.Pause.IsPaused,
                drawing = _session != null && _session.Walker != null && _session.Walker.Drawing != null && _session.Walker.Drawing.InDrawMode,
                vehicleOccupied = Object.FindFirstObjectByType<WorldMacroPalanquinSeat>()?.Occupied ?? false,
                timeScale = Time.timeScale, playerHp01 = hp, hudInk01 = ink,
                collectedBundles = bundles, collectedFragments = ui != null ? ui.items.Sum(item => item != null ? item.count : 0) : 0
            };
        }

        private static bool TryPlaceNear(Vector3 target, float interactionRadius, out Vector3 feet)
        {
            float offset = Mathf.Min(.35f, Mathf.Max(.15f, interactionRadius * .2f));
            foreach (Vector3 candidate in new[]
            {
                target + Vector3.right * offset, target - Vector3.right * offset,
                target + Vector3.forward * offset, target - Vector3.forward * offset,
                target + (Vector3.right + Vector3.forward).normalized * offset,
                target + (Vector3.left + Vector3.back).normalized * offset
            })
            {
                if (_session.TrySafeFeet(candidate, out feet) && Vector3.Distance(feet, target) <= interactionRadius * .8f)
                    return true;
            }
            feet = default;
            return false;
        }

        private static string DescribePickupFocus(WorldMacroFragmentPickup pickup)
        {
            if (_session == null || _session.Walker == null || _session.Walker.Body == null || pickup == null) return "runtime references unavailable";
            Vector3 player = _session.Walker.Body.transform.position;
            Vector3 target = pickup.InteractionPosition;
            Vector3 eye = player + Vector3.up * _session.Walker.EyeHeight;
            Vector3 aim = target + Vector3.up * .5f;
            Vector3 ray = aim - eye;
            string blockers = ray.sqrMagnitude < .0001f ? "none(ray-zero)" : string.Join(",",
                Physics.RaycastAll(eye, ray.normalized, ray.magnitude, ~0, QueryTriggerInteraction.Ignore)
                    .Where(hit => !hit.transform.IsChildOf(_session.Walker.Body.transform) && !hit.transform.IsChildOf(pickup.transform))
                    .Select(hit => hit.transform.name + "@" + hit.distance.ToString("0.00")).ToArray());
            if (string.IsNullOrEmpty(blockers)) blockers = "none";
            return "player=" + player.ToString("F3") + ", reviewFeet=" + _fragmentReviewFeet.ToString("F3")
                + ", pickup=" + target.ToString("F3") + ", distance=" + Vector3.Distance(player, target).ToString("0.000")
                + ", radius=" + pickup.Radius.ToString("0.000") + ", focusedBundle=" + (_session.FocusedCollectionBundleId ?? "<null>")
                + ", focusedId=" + (_session.FocusedId ?? "<null>") + ", active=" + pickup.gameObject.activeInHierarchy
                + ", enabled=" + pickup.enabled + ", gate=" + _session.GameplayInputBlocked + ", seated=" + _session.Walker.Seated
                + ", motor=" + _session.Walker.Motor.enabled + ", drawing=" + _session.Walker.Drawing.InDrawMode
                + ", blockers=" + blockers;
        }

        private static int CurrentBundleCount()
        {
            return _session == null || _session.Progress == null || _session.Progress.ui == null ? 0
                : WorldMacroCollectionCatalog.AllBundles.Count(bundle => WorldMacroInventoryService.IsBundleCollected(_session.Progress.ui, bundle));
        }

        private static int CurrentFragmentCount()
        {
            return _session == null || _session.Progress == null || _session.Progress.ui == null ? 0
                : _session.Progress.ui.items.Sum(item => item != null ? item.count : 0);
        }

        private static int CurrentLetterCount()
        {
            return _session == null || _session.Progress == null || _session.Progress.ui == null ? 0
                : _session.Progress.ui.knownSpellLetters.Count;
        }

        private static void InspectActiveText(PlaytestUiRoot root, string page, List<string> inspected, List<string> overflows)
        {
            inspected.Add(page);
            foreach (Text label in root.GetComponentsInChildren<Text>(false))
            {
                Rect rect = label.rectTransform.rect;
                if (!label.gameObject.activeInHierarchy || rect.width <= 1f || rect.height <= 1f || label.resizeTextForBestFit) continue;
                bool vertical = label.verticalOverflow == VerticalWrapMode.Truncate && label.preferredHeight > rect.height + .1f;
                bool horizontal = label.horizontalOverflow == HorizontalWrapMode.Overflow && label.preferredWidth > rect.width + .1f;
                if (vertical || horizontal)
                    overflows.Add(page + "/" + TransformPath(label.transform, root.transform) + " preferred="
                        + label.preferredWidth.ToString("0.0") + "x" + label.preferredHeight.ToString("0.0")
                        + " rect=" + rect.width.ToString("0.0") + "x" + rect.height.ToString("0.0"));
            }
        }

        private static string TransformPath(Transform leaf, Transform root)
        {
            var names = new Stack<string>();
            Transform current = leaf;
            while (current != null && current != root) { names.Push(current.name); current = current.parent; }
            return string.Join("/", names);
        }

        private static string GateDiagnostics()
        {
            if (_root == null || _root.Gate == null) return "gate unavailable";
            return "applicationFocused=" + Application.isFocused + ", page=" + (_root.Page ?? "<null>")
                + ", pause=" + (_root.Pause != null && _root.Pause.IsPaused) + ", timeScale=" + Time.timeScale.ToString("0.000")
                + ", blocked=" + _root.Gate.InputBlocked + ", releasePending=" + _root.Gate.ReleasePending
                + ", focusOwnsBlock=" + _root.Gate.FocusOwnsBlock + ", neutralFrames=" + _root.Gate.NeutralFrames;
        }

        private static void Add(string id, bool pass, string detail)
        {
            CheckEntries.Add(new CheckEntry { id = id, status = pass ? "PASS" : "FAIL", detail = detail });
            if (!pass) CheckFailures.Add(id + ": " + detail);
        }

        private static void Next(CheckPhase next)
        {
            _checkPhase = next; _phaseFrame = Time.frameCount; _phaseStarted = EditorApplication.timeSinceStartup;
        }

        private static int FramesInPhase => Time.frameCount - _phaseFrame;
        private static void OnCollection(WorldMacroCollectionNotice notice) { if (notice != null) _collectionEvents++; }
        private static void OnCommitted(bool _) { _committedEvents++; }
        private static void OnDiagnosed(Oheangbu.Drawing.DrawingInputController.CommitDiagnostics _) { _diagnosedEvents++; }

        private static void QueueKeys(params Key[] keys)
        {
            if (Keyboard.current == null) return;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys ?? Array.Empty<Key>()));
            if (Mouse.current != null) InputSystem.QueueStateEvent(Mouse.current, new MouseState());
        }

        private static string NormalizePageId(string value)
        {
            string id = (value ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_');
            Need(id.Length > 0, "Page/capture id is required.");
            return id;
        }

        private static string PageName(string id)
        {
            switch (id)
            {
                case "pause": return "일시정지";
                case "inventory": return "소지품";
                case "codex": return "술식 도감";
                case "chapae": return "차패";
                case "options": return "옵션";
                case "map": return "지도";
                default: throw new ArgumentException("Unknown gameplay page: " + id);
            }
        }

        private static void FocusGameView()
        {
            Type type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            Need(type != null, "UnityEditor.GameView type is unavailable.");
            EditorWindow window = EditorWindow.GetWindow(type, false, "Game", true);
            Need(window != null, "Game View could not be opened.");
            window.Focus();
        }

        private static void RequirePlaying()
        {
            Need(EditorApplication.isPlaying && !EditorApplication.isPaused, "An unpaused Play Mode session is required. This command does not enter Play Mode.");
        }

        private static void Abort()
        {
            if (_checksRunning) FailChecks("Assembly reload or Play Mode exit interrupted checks.");
            if (_captureRunning) FailCapture("Assembly reload or Play Mode exit interrupted capture.");
            EditorApplication.update -= TickChecks; EditorApplication.update -= TickCapture;
        }

        private static string ValidationFolder => Path.Combine(PlaytestMenuAuthoring.Output, "Validation");
        private static string ScreenshotFolder => Path.Combine(PlaytestMenuAuthoring.Output, "Screenshots");
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
