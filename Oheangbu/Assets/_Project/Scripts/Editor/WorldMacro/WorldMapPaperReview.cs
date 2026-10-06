using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Opt-in, sequential actual Game View stills. Never starts Play Mode or moves the player.</summary>
    [InitializeOnLoad]
    public static class WorldMapPaperReview
    {
        [Serializable] sealed class Check { public string id, status, detail; }
        [Serializable] sealed class Preparation
        {
            public string utc, status, texture, failure;
            public string scope = "Edit Mode asset import and actual continuous paper geometry export. OBJ snapshots are geometry deliverables, not runtime animation or Game View evidence.";
            public int width, height, transparentPixels, opaquePixels, vertexCount, triangleCount;
            public float heightOverWidth;
            public string[] models;
        }
        const string PaperPath = "Assets/_Project/Resources/WorldMap/Paper/HanjiWorn.png";
        const float PaperAspect = 1f / 1.38f;
        [Serializable] sealed class Still
        {
            public string id, path, utc, page, evidence;
            public float requestedProgress, progressAtIssue, progressAfterIssue, commitRatioAtIssue, timeScale;
            public int frameAtIssue, frameAfterIssue, width, height;
            public double transitionElapsedSeconds;
            public bool closing, reducedMotion, pauseHeld, inputBlocked, written;
            public long bytes;
        }
        [Serializable] sealed class Report
        {
            public string utc, status, runId, saveIsolationSuffix, scene, failure;
            public string scope = "Actual 1920x1080 uGUI Game View PNGs from sequential ScreenCapture calls. Each still uses a fresh real unscaled-time transition. Separate interaction checks directly dispatch synthetic uGUI PointerEventData; no OS/InputSystem events, arbitrary geometry pose, video, camera render, walking, or teleport. The QA harness holds world timeScale at zero between menus and restores it afterward.";
            public string phaseGuide = "Opening .10: entering folded packet; .30: vertical center fold; .65: horizontal center fold; 1: fully open. Closing .50. Reduced motion uses the presenter API; closing via that API does not test the settings-menu close flow.";
            public string limitations = "Diagnostic setup and technical checks are not native input evidence, human visual approval, or GPU/device performance. PNG issuance follows a rendered game frame; actual issue/next-frame progress is recorded instead of claiming an exact requested pose. Focus-owned input blocking may persist when the application is unfocused.";
            public int startFrame, finishFrame, materialCountBefore, materialCountAfter, renderTextureCountBefore, renderTextureCountAfter;
            public long allocatedBefore, allocatedAfter, paperTickSamples;
            public double paperMeanTickMilliseconds, paperLastTickMilliseconds;
            public Still[] stills = Array.Empty<Still>();
            public Check[] checks = Array.Empty<Check>();
            public string[] failures = Array.Empty<string>();
        }
        sealed class Plan
        {
            public readonly string Id;
            public readonly float Progress;
            public readonly bool Closing, Reduced;
            public Plan(string id, float progress, bool closing = false, bool reduced = false)
            { Id = id; Progress = progress; Closing = closing; Reduced = reduced; }
        }
        sealed class SavedFile
        {
            public string Path;
            public byte[] Bytes;
            public DateTime Modified;
        }
        enum Phase { Start, Opening, Closing, WaitFile, FinishOpen, FinishClose, Finish }
        static readonly Plan[] Plans = {
            new Plan("01_open_packet_010", .10f), new Plan("02_open_vertical_030", .30f),
            new Plan("03_open_horizontal_065", .65f), new Plan("03b_open_printed_085", .85f), new Plan("04_open_flat_100", 1f),
            new Plan("05_close_half_050", .50f, true), new Plan("06_reduced_open", 1f, false, true),
            new Plan("07_reduced_closed", 0f, true, true)
        };
        static readonly List<Still> Stills = new List<Still>();
        static readonly List<Check> Checks = new List<Check>();
        static readonly List<string> Failures = new List<string>();
        static readonly List<SavedFile> SavedFiles = new List<SavedFile>();
        static PlaytestUiRoot root;
        static WorldMacroPlaytestSession session;
        static UserSettingsData originalSettings;
        static EditorWindow originalWindow;
        static Report report;
        static Still still;
        static Phase phase;
        static bool running, restored, closeStarted, openingMeasured, interactionChecked;
        static int planIndex, lastFrame;
        static double deadline, transitionAt;
        static float originalTimeScale;
        static CursorLockMode originalCursorLock;
        static bool originalCursorVisible;
        static string originalProgress, screenshotFolder, reportPath;
        static Vector3 originalFeet;

        static WorldMapPaperReview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Abort;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingPlayMode) Abort(); };
        }

        public static string Execute(string action)
        {
            switch ((action ?? "").Trim().ToLowerInvariant())
            {
                case "begin": return Begin();
                case "prepare": return Prepare();
                case "poll": return Poll();
                case "abort": if (running) Fail("Review aborted by command."); return Poll();
                default: throw new ArgumentException("Expected prepare, begin, poll, or abort.");
            }
        }

        static string Prepare()
        {
            Need(!EditorApplication.isPlayingOrWillChangePlaymode && !running, "paper-prepare is Edit Mode only.");
            Need(Prologue.PrologueAudit.CommitRatio() < .85f, "Memory guard refused paper preparation: system commit must be below 85%.");
            Need(File.Exists(PaperPath), "Missing worn paper source texture: " + PaperPath);
            string run = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/UIAudio/PaperMapReview"));
            string models = Path.Combine(output, "Models", run), validation = Path.Combine(output, "Validation", run);
            Directory.CreateDirectory(models); Directory.CreateDirectory(validation);
            var prepared = new Preparation { utc = DateTime.UtcNow.ToString("O"), status = "FAIL", texture = PaperPath,
                vertexCount = WorldMapPaperGeometry.VertexCount, triangleCount = WorldMapPaperGeometry.TriangleCount, heightOverWidth = PaperAspect };
            var files = new List<string>();
            TextureImporter importer = null;
            try
            {
                AssetDatabase.ImportAsset(PaperPath, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(PaperPath) as TextureImporter;
                Need(importer != null, "Texture importer unavailable for worn paper.");
                importer.textureType = TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048; importer.mipmapEnabled = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None; importer.isReadable = true;
                importer.SaveAndReimport();
                Need(importer.DoesSourceTextureHaveAlpha(), "Worn paper source image has no alpha channel.");
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PaperPath);
                Need(texture != null, "Imported worn paper texture could not be loaded.");
                prepared.width = texture.width; prepared.height = texture.height;
                foreach (Color32 pixel in texture.GetPixels32())
                { if (pixel.a < 250) prepared.transparentPixels++; if (pixel.a > 250) prepared.opaquePixels++; }
                Need(prepared.transparentPixels > 0 && prepared.opaquePixels > 0, "Paper alpha must contain both a transparent perimeter and opaque paper.");
                var uv = new Vector2[WorldMapPaperGeometry.VertexCount];
                var indices = new int[WorldMapPaperGeometry.TriangleCount * 3];
                var positions = new Vector3[uv.Length]; var normals = new Vector3[uv.Length];
                WorldMapPaperGeometry.CreateGrid(uv, indices);
                foreach (float progress in new[] { 0f, .3f, .65f, 1f })
                {
                    WorldMapPaperGeometry.Evaluate(progress, PaperAspect, uv, positions, normals);
                    var obj = new StringBuilder(256000);
                    obj.AppendLine("# Oheangbu continuous four-panel paper; actual WorldMapPaperGeometry output");
                    obj.AppendLine("# width=1; height=" + Number(PaperAspect) + "; positive Z faces viewer; progress=" + Number(progress));
                    obj.AppendLine("o HanjiPaper");
                    foreach (Vector3 p in positions) obj.Append("v ").Append(Number(p.x)).Append(' ').Append(Number(p.y)).Append(' ').Append(Number(p.z)).AppendLine();
                    foreach (Vector2 p in uv) obj.Append("vt ").Append(Number(p.x)).Append(' ').Append(Number(p.y)).AppendLine();
                    foreach (Vector3 n in normals) obj.Append("vn ").Append(Number(n.x)).Append(' ').Append(Number(n.y)).Append(' ').Append(Number(n.z)).AppendLine();
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        obj.Append('f');
                        for (int j = 0; j < 3; j++) { int index = indices[i + j] + 1; obj.Append(' ').Append(index).Append('/').Append(index).Append('/').Append(index); }
                        obj.AppendLine();
                    }
                    string path = Path.Combine(models, "HanjiPaper_p" + Mathf.RoundToInt(progress * 100).ToString("000") + ".obj");
                    File.WriteAllText(path, obj.ToString(), new UTF8Encoding(false)); files.Add(path);
                }
                prepared.status = "PASS";
            }
            catch (Exception exception) { prepared.failure = exception.GetType().Name + ": " + exception.Message; }
            finally
            {
                if (importer != null)
                    try { importer.isReadable = false; importer.SaveAndReimport(); }
                    catch (Exception exception) { prepared.status = "FAIL"; prepared.failure += " Import cleanup: " + exception.Message; }
            }
            prepared.models = files.ToArray();
            string pathReport = Path.Combine(validation, "paper_prepare.json");
            File.WriteAllText(pathReport, JsonUtility.ToJson(prepared, true));
            return prepared.status + " paper-prepare report=" + pathReport + "\n" + JsonUtility.ToJson(prepared, true);
        }

        static string Number(float number) => number.ToString("R", CultureInfo.InvariantCulture);

        static string Begin()
        {
            Need(!running, "A paper review is already running.");
            Need(EditorApplication.isPlaying && !EditorApplication.isPaused, "An active, Editor-unpaused Play Mode session is required.");
            Need(SceneManager.GetActiveScene().path == WorldMacroPlaytestAuthoring.ScenePath, "Paper review requires W_WorldMacro_Playtest.");
            Need(!PlaytestMenuReview.Execute("status").Contains("RUNNING") && !PlaytestMenuAdvancedReview.Execute("poll").StartsWith("RUNNING", StringComparison.Ordinal), "Finish existing menu QA before paper review.");
            root = PlaytestUiRoot.Instance ?? Object.FindFirstObjectByType<PlaytestUiRoot>();
            session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            Need(root != null && root.Map != null && session != null && root.Session == session && session.Progress != null, "Map/menu/session wiring is incomplete.");
            Need(root.Page.Length == 0 && !root.Pause.IsPaused && !root.Map.Folding && !root.Map.Expanded, "Close menus and wait for the map to finish folding before paper-begin.");
            Need(!root.Settings.IsPreviewing && !root.Settings.Current.ReducedMotion, "Begin with reduced motion off and no settings preview; QA previews and restores reduced motion itself.");
            Need(session.Walker != null && session.Walker.Body != null && !session.Walker.Drawing.InDrawMode, "An idle player is required; finish drawing before this review.");
            string suffix = SessionState.GetString(PlaytestMenuReview.ReviewSuffixKey, "");
            Need(!string.IsNullOrWhiteSpace(suffix) && suffix != "_" && session.TestSaveSuffix == suffix, "Paper review requires the active suffixed QA save. Set test-slot:<suffix> before entering Play Mode.");
            NeedResolution();
            Need(Prologue.PrologueAudit.CommitRatio() < .85f, "Memory guard refused paper review: system commit must be below 85%.");
            string runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/UIAudio/PaperMapReview"));
            screenshotFolder = Path.Combine(output, "Screenshots", runId);
            reportPath = Path.Combine(output, "Validation", runId, "paper_review.json");
            Directory.CreateDirectory(screenshotFolder); Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            Stills.Clear(); Checks.Clear(); Failures.Clear(); SavedFiles.Clear();
            originalSettings = root.Settings.Current;
            originalProgress = JsonUtility.ToJson(session.Progress);
            originalFeet = session.Walker.Body.transform.position;
            originalTimeScale = Time.timeScale; originalCursorLock = Cursor.lockState; originalCursorVisible = Cursor.visible;
            originalWindow = EditorWindow.focusedWindow;
            string slot = WorldMacroSaveSlot.GetPrimaryPath(Application.persistentDataPath, session.Content.SaveSlot + suffix);
            foreach (string path in new[] { slot, slot + ".bak", slot + ".tmp" })
                SavedFiles.Add(new SavedFile { Path = path, Bytes = File.Exists(path) ? File.ReadAllBytes(path) : null, Modified = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default });
            report = new Report { utc = DateTime.UtcNow.ToString("O"), status = "RUNNING", runId = runId, saveIsolationSuffix = suffix,
                scene = SceneManager.GetActiveScene().path, startFrame = Time.frameCount,
                materialCountBefore = Resources.FindObjectsOfTypeAll<Material>().Length, renderTextureCountBefore = Resources.FindObjectsOfTypeAll<RenderTexture>().Length,
                allocatedBefore = Profiler.GetTotalAllocatedMemoryLong() };
            restored = false; interactionChecked = false; running = true; planIndex = 0; lastFrame = -1; phase = Phase.Start;
            deadline = EditorApplication.timeSinceStartup + 120d;
            try
            {
                Time.timeScale = 0f;
                Type gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                Need(gameView != null, "Game View type unavailable.");
                EditorWindow.GetWindow(gameView, false, "Game", true).Focus();
                CheckGeometry();
                EditorApplication.update -= Tick; EditorApplication.update += Tick;
                WriteReport();
            }
            catch (Exception exception) { Fail(exception.GetType().Name + ": " + exception.Message); }
            return (running ? "PAPER_REVIEW_STARTED" : "PAPER_REVIEW_FAILED") + " run=" + runId + " poll=paper-poll report=" + reportPath;
        }

        static void Tick()
        {
            if (!running) return;
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) { Fail("Play Mode stopped or was paused in the Editor."); return; }
            if (EditorApplication.timeSinceStartup > deadline) { Fail("Paper review exceeded 120 seconds at " + phase + "."); return; }
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try
            {
                Need(root != null && root.Map != null && session != null, "Runtime objects disappeared during review.");
                Plan plan = Plans[Math.Min(planIndex, Plans.Length - 1)];
                switch (phase)
                {
                    case Phase.Start:
                        if (planIndex >= Plans.Length) { phase = Phase.Finish; break; }
                        Need(root.Page.Length == 0 && !root.Pause.IsPaused && !root.Map.Folding, "Previous map lifecycle did not finish.");
                        closeStarted = false; openingMeasured = false;
                        if (plan.Reduced)
                        {
                            var preview = originalSettings.Clone(); preview.ReducedMotion = true;
                            root.Settings.Preview(preview); root.Map.ReducedMotion = true;
                        }
                        transitionAt = Time.realtimeSinceStartupAsDouble;
                        root.OpenPage("지도"); phase = Phase.Opening;
                        if (plan.Reduced && !plan.Closing)
                        {
                            Add(plan.Id + "-instant", root.Map.FoldProgress >= .999f && !root.Map.Folding, "Reduced motion OpenPage resolves the presenter in the initiating call.");
                        }
                        break;
                    case Phase.Opening:
                        NeedPaused();
                        MeasureOpening(plan);
                        if (plan.Closing)
                        {
                            if (root.Map.Folding || root.Map.FoldProgress < .999f) return;
                            BeginClose(plan);
                        }
                        else if (root.Map.FoldProgress + .0001f >= plan.Progress) Issue(plan);
                        break;
                    case Phase.Closing:
                        if (!plan.Reduced) NeedPaused();
                        if (root.Map.FoldProgress <= plan.Progress + .0001f) Issue(plan);
                        break;
                    case Phase.WaitFile:
                        if (still.frameAfterIssue == 0)
                        { still.frameAfterIssue = Time.frameCount; still.progressAfterIssue = root.Map.FoldProgress; }
                        MeasureOpening(plan);
                        if (Time.frameCount < still.frameAtIssue + 3 || !File.Exists(still.path)) return;
                        var info = new FileInfo(still.path);
                        Need(info.Length > 24, "Screenshot file is missing PNG data.");
                        Need(PngHasExpectedSize(still.path), "Screenshot PNG dimensions differ from 1920x1080.");
                        still.bytes = info.Length; still.written = true;
                        phase = closeStarted ? Phase.FinishClose : Phase.FinishOpen;
                        WriteReport(); break;
                    case Phase.FinishOpen:
                        NeedPaused(); MeasureOpening(plan);
                        if (root.Map.Folding || root.Map.FoldProgress < .999f) return;
                        if (!interactionChecked) { interactionChecked = true; CheckMapInteraction(); }
                        BeginClose(plan); phase = Phase.FinishClose; break;
                    case Phase.FinishClose:
                        if (root.Map.Folding) { NeedPaused(); return; }
                        if (root.Page.Length > 0 || root.Pause.IsPaused) return;
                        Add(plan.Id + "-closed-cleanup", !root.Map.Expanded && root.Map.FoldProgress <= .0001f && !root.Map.FullRoot.gameObject.activeSelf,
                            "page empty; pause depth=" + root.Pause.Depth + "; expanded root inactive; focus=" + Application.isFocused + "; gate release pending=" + root.Gate.ReleasePending);
                        if (!plan.Reduced)
                        {
                            double elapsed = Time.realtimeSinceStartupAsDouble - transitionAt;
                            Add(plan.Id + "-close-timing", elapsed >= WorldMapPresenter.CloseDuration - .08d,
                                "observed close lifecycle=" + elapsed.ToString("0.000") + "s; configured=" + WorldMapPresenter.CloseDuration.ToString("0.000") + "s; PNG wait can extend this observation");
                        }
                        if (root.Settings.IsPreviewing) root.Settings.Revert();
                        root.Map.ReducedMotion = originalSettings.ReducedMotion;
                        planIndex++; phase = Phase.Start; break;
                    case Phase.Finish:
                        Finish(); break;
                }
            }
            catch (Exception exception) { Fail(exception.GetType().Name + ": " + exception.Message); }
        }

        static void BeginClose(Plan plan)
        {
            transitionAt = Time.realtimeSinceStartupAsDouble; closeStarted = true;
            if (plan.Reduced)
            {
                // CloseMenu reverts previews first. This intentionally tests the presenter API separately.
                root.Map.ReducedMotion = true; root.Map.SetExpanded(false);
                Add(plan.Id + "-instant-close", root.Map.FoldProgress <= .0001f && !root.Map.Folding,
                    "Diagnostic presenter SetExpanded(false) completes in its initiating call; unsaved reduced-motion preview.");
            }
            root.CloseMenu(); phase = Phase.Closing;
        }

        static void MeasureOpening(Plan plan)
        {
            if (openingMeasured || closeStarted || root.Map.Folding || root.Map.FoldProgress < .999f) return;
            openingMeasured = true;
            double elapsed = Time.realtimeSinceStartupAsDouble - transitionAt;
            if (!plan.Reduced) Add(plan.Id + "-open-timing", elapsed >= WorldMapPresenter.OpenDuration - .08d && elapsed <= WorldMapPresenter.OpenDuration + .75d,
                "observed natural opening=" + elapsed.ToString("0.000") + "s; configured=" + WorldMapPresenter.OpenDuration.ToString("0.000") + "s; world timeScale=" + Time.timeScale);
        }

        static void CheckMapInteraction()
        {
            RectTransform window = root.Map.FullRoot.Find("TwiceFoldedHanji/PrintedMapWindow") as RectTransform;
            Need(window != null && EventSystem.current != null, "Map interaction QA needs its printed window and EventSystem.");
            GameObject input = window.Find("MapInput").gameObject;
            Material material = window.parent.GetComponent<WorldMapPaperGraphic>().material;
            Canvas canvas = window.GetComponentInParent<Canvas>().rootCanvas;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            Need(canvas.renderMode == RenderMode.ScreenSpaceOverlay, "Interaction QA expects the playtest overlay canvas.");
            Vector4 originalUv = material.GetVector("_WorldUv");
            bool originalWhole = (originalUv - new Vector4(0, 0, 1, 1)).sqrMagnitude < .000001f;
            Vector2 originalWindowSize = window.rect.size;
            float priorScale = canvas.scaleFactor;
            bool scalerEnabled = scaler != null && scaler.enabled;
            string progressBefore = JsonUtility.ToJson(session.Progress);
            try
            {
                WorldMapBakedDataSO data = root.MapData;
                Need(data != null, "Interaction QA needs the bound baked map data.");
                root.Map.ShowWholeWorld();
                Vector4 wholeUv = material.GetVector("_WorldUv");
                bool interior = data.ZoneAt(session.Walker.Body.transform.position) != null;
                if (!interior)
                {
                    float expectedAspect = (data.BoundsMax.x - data.BoundsMin.x) / (data.BoundsMax.y - data.BoundsMin.y);
                    Add("whole-map-aspect-and-world-bounds", Mathf.Abs(window.rect.width / window.rect.height - expectedAspect) < .001f &&
                        (wholeUv - new Vector4(0, 0, 1, 1)).sqrMagnitude < .000001f,
                        "world ratio=" + expectedAspect.ToString("0.000000") + "; printed ratio=" + (window.rect.width / window.rect.height).ToString("0.000000") + "; world UV=" + wholeUv);
                }
                else Add("whole-map-interior-stays-local", (wholeUv - originalUv).sqrMagnitude < .000001f,
                    "Current zone is interior; Whole intentionally stays on the local cave. Outdoor whole-world aspect is not exercised in this run.");
                root.Map.FocusCurrent();
                Vector4 focused = material.GetVector("_WorldUv");
                var feet = session.Walker.Body.transform.position;
                Vector2 here = new WorldMapProjection(data.BoundsMin, data.BoundsMax).WorldToNormalized(new Vector2(feet.x,feet.z));
                // D308-25 (map 5): the wide sheet's default view spans the whole east-west width of the world (uv width 1) and a
                // part of its north-south length, so "local" is the height; the window must have its own shape again (not the world-shaped strip of T)
                Add("current-focus-centers-player", focused.z <= 1.00001f && focused.w < 1 &&
                    Mathf.Abs(window.rect.width / window.rect.height - (data.BoundsMax.x - data.BoundsMin.x) / (data.BoundsMax.y - data.BoundsMin.y)) > .01f &&
                    here.x >= focused.x && here.x <= focused.x + focused.z && here.y >= focused.y && here.y <= focused.y + focused.w,
                    "Current opens a local window containing the player: world UV=" + focused + " (the wide sheet shows the whole east-west span, a part of north-south); printed window=" + window.rect.size + ".");

                if (scaler != null) scaler.enabled = false;
                foreach (float scale in new[] { 1f, 1.2f })
                {
                    canvas.scaleFactor = scale; Canvas.ForceUpdateCanvases();
                    root.Map.FocusCurrent(); Canvas.ForceUpdateCanvases();
                    Vector4 before = material.GetVector("_WorldUv");
                    Vector2 witness = new Vector2(before.x + before.z * .5f, before.y + before.w * .5f);
                    Vector2 start = WorldUvScreen(window, before, witness);
                    Vector2 delta = new Vector2(before.x > .002f ? 48f : -48f, before.y > .002f ? 24f : -24f);
                    var pointer = new PointerEventData(EventSystem.current) { position = start + delta, delta = delta, button = PointerEventData.InputButton.Left };
                    ExecuteEvents.Execute(input, pointer, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(input, pointer, ExecuteEvents.beginDragHandler);
                    bool handled = ExecuteEvents.Execute(input, pointer, ExecuteEvents.dragHandler);
                    Vector4 after = material.GetVector("_WorldUv");
                    Vector2 moved = WorldUvScreen(window, after, witness) - start;
                    bool bounded = after.x >= 0 && after.y >= 0 && after.z > 0 && after.w > 0 && after.x + after.z <= 1.00001f && after.y + after.w <= 1.00001f;
                    // D308-25 (map 5): a view that already spans the whole east-west width cannot move sideways - the drag then moves north-south only
                    bool spansWidth = before.z >= .99999f;
                    Vector2 expected = new Vector2(spansWidth ? 0f : delta.x, delta.y);
                    Add("synthetic-ugui-pan-scale-" + scale.ToString("0.0", CultureInfo.InvariantCulture), handled && bounded && Vector2.Distance(moved, expected) < .75f,
                        "Synthetic PointerEventData OnDrag; canvas scale=" + canvas.scaleFactor.ToString("0.000") + "; requested screen pixels=" + delta + "; expected landmark displacement=" + expected + (spansWidth ? " (the view spans the world's width: no sideways move)" : "")
                        + "; world landmark screen displacement=" + moved + "; UV bounded=" + bounded + ". Not native mouse evidence.");
                }
                CheckInkStrokeShape();
            }
            finally
            {
                canvas.scaleFactor = priorScale;
                if (scaler != null) scaler.enabled = scalerEnabled;
                Canvas.ForceUpdateCanvases();
                if (originalWhole) root.Map.ShowWholeWorld(); else root.Map.FocusCurrent();
                Canvas.ForceUpdateCanvases();
                Add("interaction-fixture-restored", (material.GetVector("_WorldUv") - originalUv).sqrMagnitude < .000001f &&
                    (window.rect.size - originalWindowSize).sqrMagnitude < .01f && Mathf.Abs(canvas.scaleFactor - priorScale) < .001f &&
                    JsonUtility.ToJson(session.Progress) == progressBefore,
                    "Restored the original whole/local view, print layout and canvas scale; progress unchanged. No click, pin, settings persistence or save operation issued.");
            }
        }

        static Vector2 WorldUvScreen(RectTransform window, Vector4 view, Vector2 worldUv)
        {
            Rect rect = window.rect;
            var local = new Vector3(rect.xMin + (worldUv.x - view.x) / view.z * rect.width, rect.yMin + (worldUv.y - view.y) / view.w * rect.height);
            return RectTransformUtility.WorldToScreenPoint(null, window.TransformPoint(local));
        }

        static void CheckInkStrokeShape()
        {
            // Exercise the internal rasterizer through its public methods without adding a runtime test API.
            Type type = typeof(WorldMapPresenter).Assembly.GetType("Oheangbu.App.World.UI.WorldMapPaperInk", true);
            object ink = Activator.CreateInstance(type);
            try
            {
                var lines = new[] {
                    new WorldMapLineSpec { Id = "qa-horizontal", Kind = WorldMapLineKind.Road, PixelWidth = 8, Points = new[] { new Vector2(.2f, .25f), new Vector2(.8f, .25f) } },
                    new WorldMapLineSpec { Id = "qa-vertical", Kind = WorldMapLineKind.Road, PixelWidth = 8, Points = new[] { new Vector2(.25f, .5f), new Vector2(.25f, .9f) } }
                };
                type.GetMethod("Draw").Invoke(ink, new object[] { lines, null, new WorldMapProjection(Vector2.zero, Vector2.one), new Rect(0, 0, 1, 1), new Vector2(512, 768) });
                var texture = (Texture2D)type.GetProperty("Texture").GetValue(ink);
                Color32[] pixels = texture.GetPixels32(); int horizontal = 0, vertical = 0;
                for (int y = 0; y < texture.height; y++) if (pixels[y * texture.width + texture.width / 2].a >= 112) horizontal++;
                int row = Mathf.FloorToInt(texture.height * .7f);
                for (int x = 0; x < texture.width; x++) if (pixels[row * texture.width + x].a >= 112) vertical++;
                float horizontalPixels = horizontal * 768f / texture.height, verticalPixels = vertical * 512f / texture.width;
                Add("ink-width-independent-of-orientation", Mathf.Abs(horizontalPixels - verticalPixels) <= 1f && Mathf.Abs(horizontalPixels - 8f) <= 1.5f,
                    "Isolated actual ink raster at2:3 print aspect; half-alpha stroke thickness horizontal=" + horizontalPixels.ToString("0.00") + "px, vertical=" + verticalPixels.ToString("0.00") + "px; requested8px.");
            }
            finally { ((IDisposable)ink).Dispose(); }
        }

        static void Issue(Plan plan)
        {
            NeedResolution();
            float commit = Prologue.PrologueAudit.CommitRatio();
            Need(commit < .85f, "Capture refused immediately before allocation: system commit=" + commit.ToString("P1") + "; limit 85%.");
            still = new Still { id = plan.Id, path = Path.Combine(screenshotFolder, plan.Id + "_1920x1080.png"), utc = DateTime.UtcNow.ToString("O"),
                page = root.Page, requestedProgress = plan.Progress, progressAtIssue = root.Map.FoldProgress,
                commitRatioAtIssue = commit, timeScale = Time.timeScale, frameAtIssue = Time.frameCount, width = Screen.width, height = Screen.height,
                transitionElapsedSeconds = Time.realtimeSinceStartupAsDouble - transitionAt, closing = plan.Closing, reducedMotion = plan.Reduced,
                pauseHeld = root.Pause.IsPaused, inputBlocked = root.Gate.InputBlocked,
                evidence = plan.Reduced ? "Real Game View after unsaved reduced-motion diagnostic presenter operation." : "Real Game View during a naturally progressing unscaled animation; fresh transition for this target." };
            Need(!File.Exists(still.path), "Refusing to overwrite an existing review screenshot.");
            Stills.Add(still);
            ScreenCapture.CaptureScreenshot(still.path, 1);
            phase = Phase.WaitFile;
        }

        static void CheckGeometry()
        {
            const float aspect = PaperAspect;
            var uv = new Vector2[WorldMapPaperGeometry.VertexCount];
            var triangles = new int[WorldMapPaperGeometry.TriangleCount * 3];
            var positions = new Vector3[uv.Length]; var normals = new Vector3[uv.Length];
            WorldMapPaperGeometry.CreateGrid(uv, triangles);
            Add("mesh-topology-bounded", uv.Length <= 4096 && triangles.Length <= 24576 && triangles.All(i => i >= 0 && i < uv.Length),
                "vertices=" + uv.Length + "; triangles=" + triangles.Length / 3 + "; all indices valid");
            float maxSeamGap = 0f, maxEdgeRatio = 0f;
            bool finite = true, normalsValid = true;
            foreach (float progress in new[] { 0f, .1f, .3f, .5f, .65f, .85f, 1f })
            {
                WorldMapPaperGeometry.Evaluate(progress, aspect, uv, positions, normals);
                for (int i = 0; i < positions.Length; i++)
                {
                    Vector3 p = positions[i]; Vector2 projected = WorldMapPaperGeometry.Project(p);
                    finite &= float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z) && float.IsFinite(projected.x) && float.IsFinite(projected.y);
                    normalsValid &= normals[i].sqrMagnitude > .8f && normals[i].sqrMagnitude < 1.2f;
                }
                for (int j = 0; j <= 10; j++)
                {
                    float along = j / 10f;
                    maxSeamGap = Mathf.Max(maxSeamGap, Vector3.Distance(WorldMapPaperGeometry.EvaluatePoint(new Vector2(.5f - .00001f, along), progress, aspect), WorldMapPaperGeometry.EvaluatePoint(new Vector2(.5f + .00001f, along), progress, aspect)));
                    maxSeamGap = Mathf.Max(maxSeamGap, Vector3.Distance(WorldMapPaperGeometry.EvaluatePoint(new Vector2(along, .5f - .00001f), progress, aspect), WorldMapPaperGeometry.EvaluatePoint(new Vector2(along, .5f + .00001f), progress, aspect)));
                }
                for (int j = 0; j < triangles.Length; j += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = triangles[j + e], b = triangles[j + (e + 1) % 3];
                        float source = Vector2.Scale(uv[a] - uv[b], new Vector2(1f, aspect)).magnitude;
                        if (source > .0001f) maxEdgeRatio = Mathf.Max(maxEdgeRatio, Vector3.Distance(positions[a], positions[b]) / source);
                    }
            }
            Add("geometry-finite-projected-surface", finite && normalsValid, "All sampled vertices project finitely; normals remain unit length.");
            Add("both-centerlines-continuous", maxSeamGap < .003f, "largest near-seam physical gap=" + maxSeamGap.ToString("0.000000") + " normalized widths");
            Add("paper-local-stretch-bounded", maxEdgeRatio < 1.35f, "largest physical/source triangle edge ratio=" + maxEdgeRatio.ToString("0.0000"));
            Vector3 center = WorldMapPaperGeometry.EvaluatePoint(new Vector2(.5f, .5f), 1f, aspect);
            Vector3 left = WorldMapPaperGeometry.EvaluatePoint(new Vector2(0f, .5f), 1f, aspect);
            Vector3 right = WorldMapPaperGeometry.EvaluatePoint(new Vector2(1f, .5f), 1f, aspect);
            Vector3 bottom = WorldMapPaperGeometry.EvaluatePoint(new Vector2(.5f, 0f), 1f, aspect);
            Vector3 top = WorldMapPaperGeometry.EvaluatePoint(new Vector2(.5f, 1f), 1f, aspect);
            Add("expanded-sheet-proportions", Mathf.Abs(Vector3.Distance(left, right) - 1f) < .08f && Mathf.Abs(Vector3.Distance(bottom, top) - aspect) < .1f,
                "expanded width=" + Vector3.Distance(left, right).ToString("0.000") + "; height=" + Vector3.Distance(bottom, top).ToString("0.000") + "; center=" + center);
        }

        static void Finish()
        {
            Add("sequential-stills-complete", Stills.Count == Plans.Length && Stills.All(s => s.written), "written=" + Stills.Count(s => s.written) + "/" + Plans.Length);
            report.materialCountAfter = Resources.FindObjectsOfTypeAll<Material>().Length;
            report.renderTextureCountAfter = Resources.FindObjectsOfTypeAll<RenderTexture>().Length;
            report.allocatedAfter = Profiler.GetTotalAllocatedMemoryLong();
            Add("paper-cycle-resource-growth-bounded", report.materialCountAfter - report.materialCountBefore <= 8 && report.renderTextureCountAfter - report.renderTextureCountBefore <= 1,
                "material delta=" + (report.materialCountAfter - report.materialCountBefore) + "; RenderTexture delta=" + (report.renderTextureCountAfter - report.renderTextureCountBefore) + "; allocated byte delta=" + (report.allocatedAfter - report.allocatedBefore) + "; allocation is diagnostic, not a GPU timing claim");
            Restore(); Stop();
        }

        static void Restore()
        {
            if (restored) return; restored = true;
            RestorePart("menu lifecycle", () =>
            {
                if (root != null)
                {
                    if (root.Settings != null && root.Settings.IsPreviewing) root.Settings.Revert();
                    if (root.Map != null)
                    { root.Map.ReducedMotion = true; root.Map.SetExpanded(false); root.Map.ReducedMotion = originalSettings.ReducedMotion; }
                    if (root.Page.Length > 0) root.CloseMenu();
                    root.Pause?.ForceResume();
                    root.Gate?.ReleaseWhenNeutral();
                }
            });
            RestorePart("runtime progress", () =>
            {
                if (session != null && session.Progress != null)
                {
                    JsonUtility.FromJsonOverwrite(originalProgress, session.Progress);
                    bool saved = session.SaveNow(out string saveError);
                    Add("isolated-runtime-progress-restored", saved, saved ? "Restored original in-memory progress; synchronized save snapshot cache before restoring original file bytes." : saveError);
                    Add("player-not-moved", Vector3.Distance(session.Walker.Body.transform.position, originalFeet) < .002f, "No walk, teleport, or camera motion was requested.");
                }
            });
            foreach (SavedFile file in SavedFiles)
                RestorePart("suffixed save file " + Path.GetFileName(file.Path), () =>
                {
                    if (file.Bytes == null) { if (File.Exists(file.Path)) File.Delete(file.Path); }
                    else { File.WriteAllBytes(file.Path, file.Bytes); File.SetLastWriteTimeUtc(file.Path, file.Modified); }
                });
            RestorePart("restoration verification", () =>
            {
                Add("qa-save-files-restored", SavedFiles.All(file => file.Bytes == null ? !File.Exists(file.Path) : File.Exists(file.Path) && file.Bytes.SequenceEqual(File.ReadAllBytes(file.Path))),
                    "Primary/backup/temporary suffixed-save file bytes and timestamps restored. Production slot never opened for writing.");
                Add("settings-restored", root == null || JsonUtility.ToJson(root.Settings.Current) == JsonUtility.ToJson(originalSettings), "Only unsaved reduced-motion preview was used.");
            });
            RestorePart("time and cursor", () =>
            {
                Time.timeScale = originalTimeScale;
                Cursor.lockState = originalCursorLock; Cursor.visible = originalCursorVisible;
            });
            RestorePart("original Editor window", () =>
            {
                if (originalWindow != null) originalWindow.Focus();
            });
        }

        static void RestorePart(string name, Action restore)
        { try { restore(); } catch (Exception exception) { Failures.Add("Cleanup " + name + ": " + exception.GetType().Name + ": " + exception.Message); } }

        static void Fail(string message) { if (!running) return; Failures.Add(message); report.failure = message; Restore(); Stop(); }
        static void Stop() { running = false; EditorApplication.update -= Tick; report.status = Failures.Count == 0 ? "PASS" : "FAIL"; WriteReport(); }
        static void Abort() { if (running) Fail("Assembly reload or Play Mode exit interrupted paper review."); EditorApplication.update -= Tick; }
        static void NeedPaused() { Need(root.Page == "지도" && root.Pause.IsPaused && root.Gate.InputBlocked && Time.timeScale <= .0001f, "Map animation lost its pause/input guard."); }
        static void NeedResolution() { Need(Screen.width == 1920 && Screen.height == 1080, "Game View must already be exactly 1920x1080; actual=" + Screen.width + "x" + Screen.height + "."); }
        static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Add(string id, bool pass, string detail) { Checks.Add(new Check { id = id, status = pass ? "PASS" : "FAIL", detail = detail }); if (!pass) Failures.Add(id + ": " + detail); }
        static bool PngHasExpectedSize(string path)
        {
            byte[] header = new byte[24];
            using (var stream = File.OpenRead(path)) if (stream.Read(header, 0, header.Length) != header.Length) return false;
            int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            return header[0] == 137 && header[1] == 80 && header[2] == 78 && header[3] == 71 && width == 1920 && height == 1080;
        }
        static void WriteReport()
        {
            if (report == null || string.IsNullOrEmpty(reportPath)) return;
            report.finishFrame = Time.frameCount; report.stills = Stills.ToArray(); report.checks = Checks.ToArray(); report.failures = Failures.ToArray();
            if (root != null && root.Map != null)
            { report.paperTickSamples = root.Map.TickSamples; report.paperMeanTickMilliseconds = root.Map.MeanTickMilliseconds; report.paperLastTickMilliseconds = root.Map.LastTickMilliseconds; }
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
        }
        static string Poll() => running ? "RUNNING plan=" + planIndex + "/" + Plans.Length + " phase=" + phase + " stills=" + Stills.Count(s => s.written) + " failures=" + Failures.Count
            : !string.IsNullOrEmpty(reportPath) && File.Exists(reportPath) ? "COMPLETE report=" + reportPath + "\n" + File.ReadAllText(reportPath) : "IDLE no paper review report in this assembly session";
    }
}
