using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Spellcraft;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // #308 spell VFX sweep (COVERAGE.md 9.2 item 2): every glyph of a chosen visual set, three beats
    // (cast / impact / just before the end) on a light and a dark backdrop, for contact sheets.
    //
    // Isolation contract:
    //   - the effect, the camera, the neutral targets and the visible floor live in a private PreviewScene;
    //     the open scene is never opened, closed, saved or marked dirty and its hierarchy is checked after every glyph;
    //   - no quality level, pipeline asset, RenderSettings value or shared asset is written. The capture camera picks a
    //     renderer of the ACTIVE pipeline asset by index and switches post-processing, volumes and shadows off on itself;
    //   - lighting comes from the preview scene through the editor's own preview override when that is available.
    // What cannot be isolated is recorded in the report (see SweepEnvironment): the render scale of the active pipeline
    // asset in "game" camera mode, the physics floor when preview colliders are not seen by Physics.Raycast,
    // Camera.main writes made by a runtime effect (restored after each glyph), and the open scene's own
    // RenderPipelineManager.beginCameraRendering subscribers, which also run for a Game-type capture camera.
    // Edit mode never sends OnDestroy to runtime components: the tool runs those hooks itself before it destroys an
    // instance, or the fixed ward's static render callback would stay subscribed until the next assembly reload.
    // A capture proves that pixels were produced. It never approves art, readability or gameplay.
    public static class Spell120Sweep308
    {
        const string CatalogPath = Vfx120Editor.AssetRoot + "/Data/VFX120_Catalog.asset";
        const string OriginalSetPath = Vfx120Editor.AssetRoot + "/Data/SpellVisualSet_120.asset";
        // The visual set the main scene's BrushStrokeFeedAdapter references (Bolt300Build uses the same path).
        const string LiveSetPath = "Assets/_Project/Art/World/Architecture296/Data/9f55ff4897c35bf408434d2fd4cd5dbc_278c3cdf45b3ab24fb93a8f33e2264e2_9ba170fd23c53dc418707872eb7b2056_SpellVisualSet_120.asset";
        const string TrialGlyphs = "가나마사아";
        const int Tolerance = 2;
        static readonly Color LightBackground = new Color(.84f, .84f, .84f, 1);
        static readonly Color DarkBackground = new Color(.055f, .055f, .055f, 1);
        static readonly Color LightFloor = new Color(.76f, .76f, .76f, 1);
        static readonly Color DarkFloor = new Color(.10f, .10f, .10f, 1);
        static readonly string[] BeatNames = { "1cast", "2impact", "3end" };

        [Serializable] public sealed class Request
        {
            public string set = "catalog";        // catalog | live | original | bolt300 (live entries that differ from the catalogue)
            public string visualSetPath = "";     // optional asset path that replaces the default for "live"
            public string cameraMode = "game";    // game (depth/opaque textures, pipeline render scale) | preview (render scale 1, no depth texture)
            public string view = "review";        // review (saved review framing) | first (first-person framing)
            public int rendererIndex = -1;        // -1 = first renderer of the active pipeline asset with full layer masks and the fewest features
            public string glyphs = "";            // explicit glyphs (e.g. "가나마사아"); empty = start/count over the catalogue order
            public int start = 0, count = 120;
            public int width = 1280, height = 720;
            public bool resume = true;            // skip glyphs whose record and six images already exist for the same settings
            public bool force = false;            // ignore the trial gate and the stale-assembly check
            public bool targets = true;           // neutral target figures in the preview scene
            public bool floor = true;             // visible neutral floor in the preview scene
            public int perTick = 1;               // glyphs per editor update
            public int cleanupEvery = 24;         // EditorUtility.UnloadUnusedAssetsImmediate cadence (0 = never)
            public float stageHeight = 4000;      // world Y of the stage: far above any open world so no foreign collider is hit
            public float firstFieldOfView = 60;
            public string folder = "";            // output folder name under Spell120_308/Sweep (default <set>_<cameraMode>_<view>)
        }

        [Serializable] public sealed class PixelMetrics
        {
            public string image;
            public int changedPixels, strongPixels, brightPixels, saturatedPixels, frameEdgePixels, maximumChannelDifference;
            public int minX = -1, minY = -1, maxX = -1, maxY = -1;
            public double coverageFraction, meanChangedPixelDifference;
        }
        [Serializable] public sealed class Beat
        {
            public string name, pixelStatus = "UNVERIFIED";
            public float seconds, fractionOfLife;
            public int triangles, renderers, particleSystems, particles, lineOrTrailPoints, lights;
            public string layers;
            public PixelMetrics light, dark;
        }
        [Serializable] public sealed class GlyphRow
        {
            public int index;
            public string glyph, id, status = "UNVERIFIED", reason, settingsKey;
            public string prefabPath, profilePath, family, behavior, dispatch, areaPlanSource, dependencyHash;
            public bool assigned, sameAsCatalogPrefab, primaryTarget;
            public float life, impactClock;
            public int leakedRootsAdopted, generatedMeshes, maximumTriangles, maximumParticles, destroyHooksRun;
            public bool openSceneCameraTouched, openSceneCameraRestored, hierarchyPreserved, environmentPreserved;
            public string leakedRootNames;
            // new hidden roots that appeared in an open scene during this glyph and were left alone (not this tool's to destroy)
            public string unattributedHiddenRoots;
            // #308 deploy layer (Vfx120Profile.Deploy308): the asset the profile points at, and whether its runtime drew this capture
            public string deploy308Profile;
            public bool deploy308Active, deploy308RetiresLegacyBody;
            public string artReadability = "UNVERIFIED_REQUIRES_VISUAL_REVIEW";
            public Beat[] beats = Array.Empty<Beat>();
        }
        [Serializable] public sealed class SweepEnvironment
        {
            public string unityVersion, graphicsDevice, graphicsApi, openScenes, qualityLevel, pipelineAsset;
            public float pipelineRenderScale;
            public string cameraMode, cameraType, view, rendererName, rendererFeatures, rendererPick;
            public int rendererIndex;
            public bool rendererHasFullLayerMasks, effectiveRenderScaleIsOne;
            public string lightingOverride, physicsFloor, colorSpace;
            public bool openSceneFogEnabled;
            public int effectInstancesInOpenScenes;
            public string loadedRuntimeAssemblyMvid, loadedRuntimeAssemblyLastWriteUtc;
            public Vector3 stageOrigin, cameraPosition, cameraEuler;
            public float cameraFieldOfView;
            // who is subscribed to RenderPipelineManager.beginCameraRendering right now (type x count); in game camera mode
            // every one of them also runs for the capture camera
            public string renderCallbackSubscribers;
            public double baselineRenderMilliseconds;   // mean of the four empty-stage renders (first includes shader warm-up)
            public string notIsolated;
        }
        [Serializable] public sealed class Report
        {
            public string tool = "Spell120Sweep308", status = "RUNNING", reason, startedUtc, finishedUtc, directory, set, settingsKey;
            public int width, height, requested, captured, reused, exceptions, beatsWithoutPixels, glyphsWithoutAnyPixels;
            public int leakedRootsAdopted, openSceneCameraTouches;
            public bool trial, hierarchyPreserved = true, environmentPreserved = true, sceneDirtyFlagsPreserved = true;
            public bool baselinesUniformEnough, cleanupSucceeded = true, gameplayPass = false, artQualityPass = false;
            public double elapsedSeconds;
            public SweepEnvironment environment = new SweepEnvironment();
            public string scope = "Three clock-selected beats per glyph (cast = min(0.12s, half the impact clock); impact = impact clock + 0.08s " +
                "for arriving behaviours, otherwise 42% of Life; end = Life minus 8% (0.1..0.3s)) on a light and a dark neutral backdrop. " +
                "Demonstration cues are presentation-only; no collider, enemy, damage or gameplay event exists. " +
                "Pixel presence is not silhouette, art or gameplay approval. Particle seeds are not fixed: names, clocks and framing are deterministic, particle noise is not.";
            public string metricDefinition = "RGB byte difference against a baseline of the same stage without the effect. changed: max channel difference >2; " +
                "strong: >=24; bright: changed pixel with max channel >=235; saturated: changed pixel with all channels >=250. Descriptive counts only.";
            public GlyphRow[] rows = Array.Empty<GlyphRow>();
        }
        [Serializable] sealed class Progress
        {
            public string status, directory, error, set, cameraMode, view;
            public int index, total, captured, reused, exceptions;
            public bool trial;
        }
        [Serializable] sealed class TrialSummary
        {
            public string status, set, loadedRuntimeAssemblyMvid, capturedUtc, note;
            public string[] directories = Array.Empty<string>();
            public string[] lines = Array.Empty<string>();
        }

        // ---- run state (editor-only; cleared by Finish, by an assembly reload and when Play starts)
        static Request _r;
        static Report _report;
        static Progress _progress;
        static Stopwatch _watch;
        static List<int> _order;            // catalogue indices still to do
        static List<GlyphRow> _rows;
        static Queue<string> _pendingModes; // trial: both camera modes, one after the other
        static List<string> _trialDirectories;
        static bool _trial;
        static Vfx120Catalog _catalog;
        static Dictionary<string, GameObject> _prefabs;
        static Scene _preview;
        static bool _previewCreated;
        static Camera _camera;
        static RenderTexture _target;
        static Texture2D _readback;
        static Color32[] _lightBaseline, _darkBaseline, _pixels;
        static Transform _primary, _secondary;
        static Transform[] _split;
        static GameObject _floorVisual, _hiddenFloorInOpenScene;
        static Material _figureMaterial, _floorMaterial;
        static Vector3 _origin;
        static HashSet<int> _initialMeshIds;
        static Dictionary<int, HashSet<int>> _sceneRoots;
        static Dictionary<int, bool> _sceneDirty;
        static string _environmentStamp, _directory, _settingsKey;
        static int _done;
        static MethodInfo _setLightingOverride, _restoreLightingOverride;
        static string _runQuality, _runPipeline, _runScenes;
        static bool _usesOpenSceneFloor;
        const string OpenFloorName = "Spell120Sweep308 physics floor (hidden, unsaved, removed after the sweep)";

        static string SweepRoot => Path.Combine(Vfx120Editor.Output, "Spell120_308", "Sweep");

        // ------------------------------------------------------------------------------------------ entry points
        // Read-only: what a run would use right now. Renders nothing, creates nothing.
        public static string Describe(string request)
        {
            var r = Parse(request);
            var e = new SweepEnvironment();
            FillEnvironment(e, r, out _);
            return JsonUtility.ToJson(e, true);
        }

        // Five glyphs (one basic attack per element by default) in BOTH camera modes. A full run is refused until this exists.
        public static string Trial(string request)
        {
            if (_r != null) throw new InvalidOperationException("A sweep is already running; poll or cancel it first");
            var r = Parse(request);
            if (string.IsNullOrEmpty(r.glyphs)) r.glyphs = TrialGlyphs;
            if (r.glyphs.Length > 8) throw new ArgumentException("A trial takes at most 8 glyphs");
            r.resume = false;
            _trial = true;
            _pendingModes = new Queue<string>(new[] { "game", "preview" });
            _trialDirectories = new List<string>();
            r.cameraMode = _pendingModes.Dequeue();
            r.folder = "trial_" + r.set + "_" + r.cameraMode + "_" + r.view;
            return Begin(r);
        }

        public static string Start(string request)
        {
            if (_r != null) throw new InvalidOperationException("A sweep is already running; poll or cancel it first");
            var r = Parse(request);
            _trial = false; _pendingModes = null; _trialDirectories = null;
            // "bolt300" is at most the handful of glyphs whose live prefab differs from the catalogue: no gate needed
            bool whole = string.IsNullOrEmpty(r.glyphs) && r.count > 8 && r.set != "bolt300";
            if (whole && !r.force)
            {
                string trialPath = Path.Combine(SweepRoot, "trial_" + r.set + ".json");
                if (!File.Exists(trialPath))
                    throw new InvalidOperationException("Run Sweep308Trial for set '" + r.set + "' first (" + trialPath + " is missing)");
                var trial = JsonUtility.FromJson<TrialSummary>(File.ReadAllText(trialPath));
                // a failed, cancelled or half-finished trial also writes this file: only a completed one opens the gate
                if (trial == null || trial.status == null || !trial.status.StartsWith("TRIAL_CAPTURED", StringComparison.Ordinal))
                    throw new InvalidOperationException("The last trial for set '" + r.set + "' did not complete (" + (trial != null ? trial.status : "unreadable")
                        + "); read " + trialPath + " and run Sweep308Trial again");
                if (trial.loadedRuntimeAssemblyMvid != RuntimeMvid())
                    throw new InvalidOperationException("The trial was captured with another runtime assembly; run Sweep308Trial again");
            }
            return Begin(r);
        }

        public static string Poll()
        {
            if (_progress == null) return "{\"status\":\"IDLE\"}";
            return JsonUtility.ToJson(_progress, true);
        }

        public static string Cancel()
        {
            if (_r != null) Finish("Cancelled; finished glyph records are kept and a later run resumes from them.", "CANCELLED");
            return Poll();
        }

        static Request Parse(string request)
        {
            var r = string.IsNullOrEmpty(request) ? new Request() : JsonUtility.FromJson<Request>(request);
            if (r == null) r = new Request();
            r.start = Mathf.Max(0, r.start); r.count = Mathf.Max(1, r.count);
            r.width = Mathf.Clamp(r.width, 320, 1920); r.height = Mathf.Clamp(r.height, 180, 1080);
            r.perTick = Mathf.Clamp(r.perTick, 1, 4);
            if (r.cameraMode != "game" && r.cameraMode != "preview") throw new ArgumentException("cameraMode must be game or preview");
            if (r.view != "review" && r.view != "first") throw new ArgumentException("view must be review or first");
            if (r.set != "catalog" && r.set != "live" && r.set != "original" && r.set != "bolt300")
                throw new ArgumentException("set must be catalog, live, original or bolt300");
            if (string.IsNullOrEmpty(r.folder)) r.folder = r.set + "_" + r.cameraMode + "_" + r.view;
            foreach (char c in r.folder)
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) throw new ArgumentException("folder may contain letters, digits, _ and - only");
            return r;
        }

        // ------------------------------------------------------------------------------------------ run
        static string Begin(Request r)
        {
            if (_r != null) throw new InvalidOperationException("A sweep is already running; poll or cancel it first");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Wait for edit mode and completed compilation/import");
            string assembly = typeof(Vfx120Effect).Assembly.Location;
            DateTime compiled = File.GetLastWriteTimeUtc(assembly);
            if (!r.force)
                foreach (string source in Directory.GetFiles("Assets/_Project/Scripts/App/SpellVFX120", "*.cs", SearchOption.AllDirectories))
                    if (File.GetLastWriteTimeUtc(source) > compiled)
                        throw new InvalidOperationException("Runtime VFX code is newer than the loaded assembly; refresh/compile first: " + source);
            _r = r;
            _sceneRoots = null; _sceneDirty = null;
            _watch = Stopwatch.StartNew();
            _directory = Path.Combine(SweepRoot, r.folder);
            _report = new Report { startedUtc = DateTime.UtcNow.ToString("O"), directory = _directory, set = r.set, width = r.width, height = r.height, trial = _trial };
            _rows = new List<GlyphRow>(); _done = 0;
            try
            {
                _catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(CatalogPath);
                if (_catalog == null || _catalog.Entries == null || _catalog.Entries.Length != 120)
                    throw new InvalidOperationException("Exactly 120 catalogue entries are required");
                _prefabs = LoadSet(r);
                _order = SelectOrder(r);
                if (_order.Count == 0) throw new InvalidOperationException("No glyph matches the request");
                Directory.CreateDirectory(_directory);
                FillEnvironment(_report.environment, r, out int rendererIndex);
                _runQuality = _report.environment.qualityLevel; _runPipeline = _report.environment.pipelineAsset; _runScenes = OpenSceneKey();
                // everything that changes the pixels of a capture: a record made with another key is never reused
                _settingsKey = r.set + "|" + r.cameraMode + "|" + r.view + "|" + r.width + "x" + r.height + "|" + rendererIndex + ":" + _report.environment.rendererName
                    + "|" + _report.environment.qualityLevel + "|" + _report.environment.pipelineRenderScale.ToString("0.###")
                    + "|targets=" + r.targets + "|floor=" + r.floor + "|y=" + r.stageHeight.ToString("0.##")
                    + (r.view == "first" ? "|fov=" + r.firstFieldOfView.ToString("0.##") : "")
                    + (r.set == "live" || r.set == "bolt300" ? "|" + (string.IsNullOrEmpty(r.visualSetPath) ? "default-live-set" : r.visualSetPath) : "")
                    + "|" + _report.environment.loadedRuntimeAssemblyMvid;
                _report.settingsKey = _settingsKey;
                _report.requested = _order.Count;
                SnapshotOpenScenes();
                BuildStage(rendererIndex);
                _progress = new Progress { status = "RUNNING", directory = _directory, set = r.set, cameraMode = r.cameraMode, view = r.view,
                    total = _order.Count, trial = _trial };
                AssemblyReloadEvents.beforeAssemblyReload += OnInterrupted;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                EditorApplication.update += Tick;
                SaveProgress();
            }
            catch (Exception error)
            {
                Finish(error.ToString(), "FAILED_TO_START");
                throw;
            }
            return "SWEEP308_STARTED " + _directory + " glyphs=" + _order.Count + " renderer=" + _report.environment.rendererName
                + " mode=" + r.cameraMode + " view=" + r.view;
        }

        static void OnInterrupted() { if (_r != null) Finish("Interrupted by an assembly reload; resume with the same request.", "INTERRUPTED"); }
        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (_r != null && change == PlayModeStateChange.ExitingEditMode)
                Finish("Interrupted because Play was requested; resume with the same request.", "INTERRUPTED");
        }

        static void Tick()
        {
            if (_r == null || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                // another session may switch the quality level (and with it the pipeline asset) between editor updates
                var pipeline = GraphicsSettings.currentRenderPipeline;
                if (QualitySettings.names[QualitySettings.GetQualityLevel()] != _runQuality
                    || (pipeline != null ? AssetDatabase.GetAssetPath(pipeline) : "") != _runPipeline)
                { Finish("The quality level or pipeline asset changed during the sweep (not by this tool); resume with the same request.", "INTERRUPTED"); return; }
                // ... or open / close a scene. The hidden physics floor (when used) dies with the scene it was created in, and
                // ground-fitted effects would silently lose their ground for every later glyph.
                if (OpenSceneKey() != _runScenes || (_usesOpenSceneFloor && _hiddenFloorInOpenScene == null))
                { Finish("The set of open scenes changed during the sweep (not by this tool); resume with the same request.", "INTERRUPTED"); return; }
                for (int n = 0; n < _r.perTick && _done < _order.Count; n++)
                {
                    int catalogIndex = _order[_done];
                    var row = TryReuse(catalogIndex);
                    if (row != null) _report.reused++;
                    else
                    {
                        row = CaptureGlyph(catalogIndex);
                        File.WriteAllText(Path.Combine(_directory, row.id + ".json"), JsonUtility.ToJson(row, true));
                        if (row.status == "CAPTURED") _report.captured++; else _report.exceptions++;
                    }
                    _rows.Add(row);
                    _done++;
                    if (_r.cleanupEvery > 0 && _done % _r.cleanupEvery == 0) { GC.Collect(); EditorUtility.UnloadUnusedAssetsImmediate(); }
                }
                _progress.index = _done; _progress.captured = _report.captured; _progress.reused = _report.reused; _progress.exceptions = _report.exceptions;
                SaveProgress();
                if (_done >= _order.Count) Finish(null, null);
            }
            catch (Exception error) { Finish(error.ToString(), "FAILED"); }
        }

        static void Finish(string reason, string status)
        {
            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= OnInterrupted;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            var report = _report ?? new Report();
            // compared with the snapshot of the last glyph, while this tool's own hidden floor (if any) still exists
            bool scenesUnchanged = OpenScenesUnchanged(out string sceneNote);
            var dirtyBefore = _sceneDirty != null ? new Dictionary<int, bool>(_sceneDirty) : new Dictionary<int, bool>();
            Cleanup(report);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (dirtyBefore.TryGetValue(scene.handle, out bool dirty) && dirty != scene.isDirty)
                { scenesUnchanged = false; sceneNote = "dirty flag of " + scene.path + " changed during cleanup"; }
            }
            _sceneRoots = null; _sceneDirty = null;
            if (_rows != null) report.rows = _rows.ToArray();
            foreach (var row in report.rows)
            {
                report.leakedRootsAdopted += row.leakedRootsAdopted;
                if (row.openSceneCameraTouched) report.openSceneCameraTouches++;
                report.hierarchyPreserved &= row.hierarchyPreserved || row.status != "CAPTURED";
                report.environmentPreserved &= row.environmentPreserved || row.status != "CAPTURED";
                int without = 0;
                foreach (var beat in row.beats) if (beat.pixelStatus == "NOT_VISIBLE_AT_THIS_BEAT") without++;
                report.beatsWithoutPixels += without;
                if (row.beats.Length > 0 && without == row.beats.Length) report.glyphsWithoutAnyPixels++;
            }
            report.sceneDirtyFlagsPreserved = scenesUnchanged;
            if (!report.sceneDirtyFlagsPreserved) reason = (reason ?? "") + "\nOpen scene check: " + sceneNote;
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            report.elapsedSeconds = _watch != null ? _watch.Elapsed.TotalSeconds : 0;
            report.reason = reason;
            bool whole = _order != null && _done >= _order.Count && report.exceptions == 0;
            bool preserved = report.cleanupSucceeded && report.hierarchyPreserved && report.environmentPreserved && report.sceneDirtyFlagsPreserved;
            report.status = status ?? (!whole || !preserved ? "INCOMPLETE_OR_UNVERIFIED" : "CAPTURED_ART_UNVERIFIED");
            try
            {
                if (!string.IsNullOrEmpty(_directory))
                {
                    Directory.CreateDirectory(_directory);
                    File.WriteAllText(Path.Combine(_directory, "sweep.json"), JsonUtility.ToJson(report, true));
                }
            }
            catch (Exception error) { UnityEngine.Debug.LogWarning("Spell120Sweep308: report not written: " + error.Message); }
            if (_progress != null) { _progress.status = report.status; _progress.error = reason; SaveProgress(); }
            var request = _r;
            string finishedDirectory = _directory;
            _r = null; _report = null; _rows = null; _order = null; _prefabs = null; _catalog = null; _watch = null;
            if (_trial && request != null)
            {
                if (_trialDirectories == null) _trialDirectories = new List<string>();
                _trialDirectories.Add(finishedDirectory);
                if (status == null && _pendingModes != null && _pendingModes.Count > 0)
                {
                    request.cameraMode = _pendingModes.Dequeue();
                    request.folder = "trial_" + request.set + "_" + request.cameraMode + "_" + request.view;
                    try { Begin(request); return; }
                    catch (Exception error) { UnityEngine.Debug.LogWarning("Spell120Sweep308 trial: second camera mode failed to start: " + error.Message); }
                }
                // Begin's own failure path already wrote the summary and left trial mode
                if (_trial)
                {
                    try { WriteTrialSummary(request); }
                    catch (Exception error) { UnityEngine.Debug.LogWarning("Spell120Sweep308 trial: summary not written: " + error.Message); }
                }
                _trial = false; _pendingModes = null;
            }
        }

        static void WriteTrialSummary(Request request)
        {
            var lines = new List<string>();
            // a trial is both camera modes: one finished folder alone (cancelled, or the second mode never started) is incomplete
            bool ok = _trialDirectories.Count >= 2;
            if (!ok) lines.Add("only " + _trialDirectories.Count + " of 2 camera modes ran");
            foreach (string directory in _trialDirectories)
            {
                string path = Path.Combine(directory, "sweep.json");
                if (!File.Exists(path)) { ok = false; lines.Add(Path.GetFileName(directory) + ": no report"); continue; }
                var report = JsonUtility.FromJson<Report>(File.ReadAllText(path));
                ok &= report.status == "CAPTURED_ART_UNVERIFIED";
                lines.Add(Path.GetFileName(directory) + ": " + report.status + " captured=" + report.captured + " exceptions=" + report.exceptions
                    + " beatsWithoutPixels=" + report.beatsWithoutPixels + " leakedRoots=" + report.leakedRootsAdopted
                    + " cameraTouches=" + report.openSceneCameraTouches + " renderer=" + report.environment.rendererName
                    + " renderScale=" + report.environment.pipelineRenderScale + " lighting=" + report.environment.lightingOverride
                    + " floor=" + report.environment.physicsFloor + " seconds=" + report.elapsedSeconds.ToString("0.0")
                    + " emptyStageRenderMs=" + report.environment.baselineRenderMilliseconds.ToString("0.0")
                    + " renderCallbacks=" + report.environment.renderCallbackSubscribers);
                foreach (var row in report.rows)
                {
                    string beats = "";
                    foreach (var beat in row.beats)
                        beats += " " + beat.name + "=" + (beat.light != null ? beat.light.changedPixels : -1) + "/" + (beat.dark != null ? beat.dark.changedPixels : -1)
                            + "px," + beat.triangles + "tris";
                    lines.Add("  " + row.glyph + " " + row.status + beats + (string.IsNullOrEmpty(row.reason) ? "" : " :: " + row.reason.Split('\n')[0]));
                }
            }
            var summary = new TrialSummary
            {
                status = ok ? "TRIAL_CAPTURED_COMPARE_BOTH_MODES_BEFORE_THE_FULL_RUN" : "TRIAL_INCOMPLETE",
                set = request.set, loadedRuntimeAssemblyMvid = RuntimeMvid(), capturedUtc = DateTime.UtcNow.ToString("O"),
                directories = _trialDirectories.ToArray(), lines = lines.ToArray(),
                note = "Compare the game and preview folders: a glyph that has pixels in one mode only tells which camera mode the full run must use. " +
                       "A world fog or ambient tint on the backdrop means the lighting override did not isolate the open scene."
            };
            Directory.CreateDirectory(SweepRoot);
            File.WriteAllText(Path.Combine(SweepRoot, "trial_" + request.set + ".json"), JsonUtility.ToJson(summary, true));
        }

        static void SaveProgress()
        {
            try
            {
                Directory.CreateDirectory(SweepRoot);
                File.WriteAllText(Path.Combine(SweepRoot, "progress.json"), JsonUtility.ToJson(_progress, true));
            }
            // progress is a convenience for the driver: a locked or unwritable file must never stop Finish half way
            catch (Exception) { }
        }

        // ------------------------------------------------------------------------------------------ selection
        static Dictionary<string, GameObject> LoadSet(Request r)
        {
            var map = new Dictionary<string, GameObject>();
            if (r.set == "catalog")
            {
                foreach (var e in _catalog.Entries) if (!string.IsNullOrEmpty(e.Glyph)) map[e.Glyph] = e.Prefab;
                return map;
            }
            string path = r.set == "original" ? OriginalSetPath : (string.IsNullOrEmpty(r.visualSetPath) ? LiveSetPath : r.visualSetPath);
            var set = AssetDatabase.LoadAssetAtPath<SpellVisualSetSO>(path);
            if (set == null) throw new InvalidOperationException("Visual set not found: " + path);
            var entries = new SerializedObject(set).FindProperty("_entries");
            if (entries == null || !entries.isArray) throw new InvalidOperationException("Visual set has no _entries array");
            for (int i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                string letter = entry.FindPropertyRelative("Letter").stringValue;
                var prefab = entry.FindPropertyRelative("FxPrefab").objectReferenceValue as GameObject;
                if (!string.IsNullOrEmpty(letter)) map[letter.Substring(0, 1)] = prefab;
            }
            if (r.set == "bolt300")
            {
                var differing = new Dictionary<string, GameObject>();
                foreach (var e in _catalog.Entries)
                    if (map.TryGetValue(e.Glyph, out var prefab) && prefab != null && prefab != e.Prefab) differing[e.Glyph] = prefab;
                return differing;
            }
            return map;
        }

        static List<int> SelectOrder(Request r)
        {
            var order = new List<int>();
            if (!string.IsNullOrEmpty(r.glyphs))
            {
                for (int i = 0; i < _catalog.Entries.Length; i++)
                    if (r.glyphs.IndexOf(_catalog.Entries[i].Glyph, StringComparison.Ordinal) >= 0 && _prefabs.ContainsKey(_catalog.Entries[i].Glyph)) order.Add(i);
                return order;
            }
            for (int i = r.start; i < Mathf.Min(_catalog.Entries.Length, r.start + r.count); i++)
                if (_prefabs.ContainsKey(_catalog.Entries[i].Glyph)) order.Add(i);
            return order;
        }

        static string Id(int catalogIndex, string glyph)
        {
            return (catalogIndex + 1).ToString("000") + "_" + (string.IsNullOrEmpty(glyph) ? "0000" : ((int)glyph[0]).ToString("X4"));
        }

        static GlyphRow TryReuse(int catalogIndex)
        {
            if (!_r.resume) return null;
            string id = Id(catalogIndex, _catalog.Entries[catalogIndex].Glyph);
            string path = Path.Combine(_directory, id + ".json");
            if (!File.Exists(path)) return null;
            GlyphRow row;
            try { row = JsonUtility.FromJson<GlyphRow>(File.ReadAllText(path)); } catch (Exception) { return null; }
            if (row == null || row.status != "CAPTURED" || row.settingsKey != _settingsKey || row.beats == null || row.beats.Length != 3) return null;
            _prefabs.TryGetValue(row.glyph, out var prefab);
            if (prefab == null || row.dependencyHash != AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(prefab)).ToString()) return null;
            foreach (var beat in row.beats)
                if (beat.light == null || beat.dark == null || !File.Exists(Path.Combine(_directory, beat.light.image))
                    || !File.Exists(Path.Combine(_directory, beat.dark.image))) return null;
            return row;
        }

        // ------------------------------------------------------------------------------------------ environment
        static string RuntimeMvid() { return typeof(Vfx120Effect).Assembly.ManifestModule.ModuleVersionId.ToString(); }

        static void FillEnvironment(SweepEnvironment e, Request r, out int rendererIndex)
        {
            e.unityVersion = Application.unityVersion;
            e.graphicsDevice = SystemInfo.graphicsDeviceName; e.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
            e.colorSpace = QualitySettings.activeColorSpace.ToString();
            var scenes = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++) scenes.Add(SceneManager.GetSceneAt(i).path + (SceneManager.GetSceneAt(i).isDirty ? " (dirty)" : ""));
            e.openScenes = string.Join(" | ", scenes);
            e.qualityLevel = QualitySettings.names[QualitySettings.GetQualityLevel()];
            e.openSceneFogEnabled = RenderSettings.fog;
            // effects that already live in an open scene are not touched: the stage has its own culling mask and sits far away
            foreach (var effect in Resources.FindObjectsOfTypeAll<Vfx120Effect>())
                if (!EditorUtility.IsPersistent(effect) && effect.gameObject.scene.IsValid()) e.effectInstancesInOpenScenes++;
            e.loadedRuntimeAssemblyMvid = RuntimeMvid();
            e.loadedRuntimeAssemblyLastWriteUtc = File.GetLastWriteTimeUtc(typeof(Vfx120Effect).Assembly.Location).ToString("O");
            e.cameraMode = r.cameraMode; e.view = r.view;
            e.cameraType = r.cameraMode == "preview" ? CameraType.Preview.ToString() : CameraType.Game.ToString();
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) throw new InvalidOperationException("The active render pipeline is not a UniversalRenderPipelineAsset");
            e.pipelineAsset = AssetDatabase.GetAssetPath(asset);
            e.pipelineRenderScale = asset.renderScale;
            e.effectiveRenderScaleIsOne = r.cameraMode == "preview" || Mathf.Abs(1f - asset.renderScale) < .05f;
            var list = asset.rendererDataList;
            rendererIndex = r.rendererIndex;
            if (rendererIndex >= list.Length) throw new ArgumentException("rendererIndex is outside the renderer list of " + e.pipelineAsset);
            if (rendererIndex < 0)
            {
                int best = -1, bestFeatures = int.MaxValue;
                for (int i = 0; i < list.Length; i++)
                {
                    var data = list[i] as UniversalRendererData;
                    if (data == null || data.opaqueLayerMask.value != -1 || data.transparentLayerMask.value != -1) continue;
                    int features = ActiveFeatures(data, null);
                    if (features < bestFeatures) { best = i; bestFeatures = features; }
                }
                if (best < 0) throw new InvalidOperationException("No renderer of the active pipeline asset draws every layer; pass rendererIndex explicitly");
                rendererIndex = best;
                e.rendererPick = "auto: full layer masks, fewest active renderer features";
            }
            else e.rendererPick = "requested index";
            var chosen = list[rendererIndex] as UniversalRendererData;
            if (chosen == null) throw new InvalidOperationException("Renderer " + rendererIndex + " is not a UniversalRendererData");
            var names = new List<string>();
            ActiveFeatures(chosen, names);
            e.rendererIndex = rendererIndex; e.rendererName = chosen.name; e.rendererFeatures = string.Join(", ", names);
            e.rendererHasFullLayerMasks = chosen.opaqueLayerMask.value == -1 && chosen.transparentLayerMask.value == -1;
            e.stageOrigin = new Vector3(0, r.stageHeight, 0);
            e.renderCallbackSubscribers = RenderCallbackSubscribers();
            e.notIsolated = (e.effectiveRenderScaleIsOne ? "" : "render scale " + asset.renderScale.ToString("0.##") + " of the active pipeline asset (quality level '" + e.qualityLevel + "') applies in game camera mode; ")
                + (e.rendererHasFullLayerMasks ? "" : "the chosen renderer hides some layers from its base passes; ")
                + (r.cameraMode == "preview" ? "preview cameras get no depth/opaque texture and skip RenderObjects features (soft particles and depth-reading water differ from the game); "
                    : "beginCameraRendering subscribers of the open scene also run for a Game-type capture camera (" + e.renderCallbackSubscribers
                      + "): world renderers that prepare their view per camera do that work for the stage position on every capture, and an edit-mode preview hook left enabled draws into the captures; ")
                + "the pipeline asset itself (HDR, MSAA and shader variants of the current quality level) is shared with the editor and is only read.";
        }

        // Read-only. The event's delegate list is private; when it cannot be read the report says so instead of guessing.
        static string RenderCallbackSubscribers()
        {
            try
            {
                var field = typeof(RenderPipelineManager).GetField("beginCameraRendering", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (field == null) return "UNAVAILABLE (delegate list not readable)";
                var handlers = field.GetValue(null) as Delegate;
                if (handlers == null) return "none";
                var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var handler in handlers.GetInvocationList())
                {
                    var type = handler.Target != null ? handler.Target.GetType() : handler.Method.DeclaringType;
                    string name = type != null ? type.Name : "?";
                    counts.TryGetValue(name, out int n); counts[name] = n + 1;
                }
                var parts = new List<string>();
                foreach (var pair in counts) parts.Add(pair.Key + " x" + pair.Value);
                return parts.Count == 0 ? "none" : string.Join(", ", parts);
            }
            catch (Exception error) { return "UNAVAILABLE (" + error.GetType().Name + ")"; }
        }

        static string OpenSceneKey()
        {
            var parts = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                parts.Add(scene.handle + ":" + scene.path + ":" + (scene.isLoaded ? "1" : "0"));
            }
            return string.Join("|", parts);
        }

        static int ActiveFeatures(UniversalRendererData data, List<string> names)
        {
            int count = 0;
            foreach (var feature in data.rendererFeatures)
            {
                if (feature == null || !feature.isActive) continue;
                count++;
                if (names != null) names.Add(feature.name);
            }
            return count;
        }

        static string EnvironmentStamp()
        {
            // Read-only stamp of the open scene's shared lighting; this tool never writes these outside the preview override.
            return RenderSettings.fog + "|" + RenderSettings.fogColor + "|" + RenderSettings.fogDensity + "|" + RenderSettings.fogMode + "|"
                + RenderSettings.fogStartDistance + "|" + RenderSettings.fogEndDistance + "|" + RenderSettings.ambientMode + "|"
                + RenderSettings.ambientIntensity + "|" + RenderSettings.ambientLight + "|" + RenderSettings.ambientSkyColor + "|"
                + RenderSettings.ambientEquatorColor + "|" + RenderSettings.ambientGroundColor + "|" + RenderSettings.reflectionIntensity + "|"
                + (RenderSettings.skybox != null ? RenderSettings.skybox.GetInstanceID() : 0) + "|" + QualitySettings.GetQualityLevel();
        }

        // The editor is shared: other sessions may add objects between editor updates. The snapshot is therefore retaken
        // at the start of every glyph, so only objects created during this tool's own synchronous work count as leaks.
        static void SnapshotOpenScenes()
        {
            _sceneRoots = new Dictionary<int, HashSet<int>>(); _sceneDirty = new Dictionary<int, bool>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                var ids = new HashSet<int>();
                foreach (var root in scene.GetRootGameObjects()) ids.Add(root.GetInstanceID());
                _sceneRoots[scene.handle] = ids; _sceneDirty[scene.handle] = scene.isDirty;
            }
            _environmentStamp = EnvironmentStamp();
        }

        static bool OpenScenesUnchanged(out string note)
        {
            note = null;
            if (_sceneRoots == null) return true;
            int seen = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || !_sceneRoots.TryGetValue(scene.handle, out var ids)) continue;
                seen++;
                if (scene.isDirty != _sceneDirty[scene.handle]) { note = "dirty flag of " + scene.path + " changed"; return false; }
                var roots = scene.GetRootGameObjects();
                if (roots.Length != ids.Count) { note = "root count of " + scene.path + " changed (" + ids.Count + " -> " + roots.Length + ")"; return false; }
                foreach (var root in roots)
                    if (!ids.Contains(root.GetInstanceID())) { note = "new root '" + root.name + "' in " + scene.path; return false; }
            }
            if (seen != _sceneRoots.Count) { note = "the set of open scenes changed"; return false; }
            if (_environmentStamp != EnvironmentStamp()) { note = "RenderSettings or the quality level changed"; return false; }
            return true;
        }

        // Objects a runtime effect or fixture created as scene roots outside the preview scene are moved into it at once
        // (that is also what makes them visible to the capture camera). Runtime VFX code creates its objects visible
        // (hideFlags None or DontSave). A new root that is hidden in the hierarchy was not made by an effect: it is
        // reported and left alone, and the open-scene check of this glyph then fails by name instead of the tool
        // destroying something it cannot attribute.
        static int AdoptLeaks(List<GameObject> adopted, List<string> names, List<string> unattributed)
        {
            int count = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || !_sceneRoots.TryGetValue(scene.handle, out var ids)) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (ids.Contains(root.GetInstanceID()) || root == _hiddenFloorInOpenScene) continue;
                    if ((root.hideFlags & HideFlags.HideInHierarchy) != 0)
                    {
                        if (!unattributed.Contains(root.name)) unattributed.Add(root.name);
                        continue;
                    }
                    root.hideFlags = HideFlags.HideAndDontSave;
                    SceneManager.MoveGameObjectToScene(root, _preview);
                    adopted.Add(root); names.Add(root.name); count++;
                }
            }
            return count;
        }

        // ------------------------------------------------------------------------------------------ stage
        static GameObject Make(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(go, _preview);
            return go;
        }

        static void BuildStage(int rendererIndex)
        {
            var e = _report.environment;
            _origin = e.stageOrigin;
            // The stage volume must be free of foreign colliders: effects probe the ground with the global physics scene.
            _usesOpenSceneFloor = false;
            Physics.SyncTransforms();
            // the checked volume covers the whole physics floor (80 x 80 m) plus the height effects probe (16 m up, 48 m down)
            var foreign = Physics.OverlapBox(_origin + new Vector3(0, 0, 6), new Vector3(42, 60, 42), Quaternion.identity, ~0, QueryTriggerInteraction.Collide);
            int strangers = 0; string firstStranger = null;
            foreach (var collider in foreign)
            {
                if (collider == null) continue;
                // a floor an earlier run of this tool could not remove (its cleanup failed): it is ours, take it away
                if (collider.gameObject.name == OpenFloorName && (collider.gameObject.hideFlags & HideFlags.HideInHierarchy) != 0)
                { Object.DestroyImmediate(collider.gameObject); continue; }
                strangers++; if (firstStranger == null) firstStranger = collider.name;
            }
            if (strangers > 0)
                throw new InvalidOperationException("The stage volume at Y=" + _origin.y + " contains " + strangers + " collider(s) of the open scene (first: "
                    + firstStranger + "); pass another stageHeight");
            _initialMeshIds = new HashSet<int>();
            foreach (var mesh in Resources.FindObjectsOfTypeAll<Mesh>()) _initialMeshIds.Add(mesh.GetInstanceID());
            _preview = EditorSceneManager.NewPreviewScene(); _previewCreated = true;

            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit != null)
            {
                _figureMaterial = new Material(unlit) { name = "Sweep308 Figure", hideFlags = HideFlags.HideAndDontSave };
                _floorMaterial = new Material(unlit) { name = "Sweep308 Floor", hideFlags = HideFlags.HideAndDontSave };
                _figureMaterial.SetColor("_BaseColor", new Color(.42f, .40f, .37f, 1));
            }
            // Neutral transform anchors (and, when requested, plain figures) for target conventions of Begin/SetSecondaryTargets.
            _primary = Make("Sweep308 Primary").transform; _primary.position = _origin + new Vector3(0, 0, _r.view == "first" ? 6f : 4f);
            _secondary = Make("Sweep308 Secondary").transform; _secondary.position = _primary.position + new Vector3(2.3f, 0, 1.1f);
            _split = new Transform[6]; _split[0] = _secondary;
            float[] xs = { 0, -2.4f, -1.45f, -.5f, .5f, 1.45f };
            for (int i = 1; i < _split.Length; i++)
            {
                _split[i] = Make("Sweep308 Split " + i).transform;
                _split[i].position = _primary.position + new Vector3(xs[i], 0, 1.55f + (i % 2) * .65f);
            }
            if (_r.targets && _figureMaterial != null) { Figure(_primary); Figure(_secondary); }
            if (_r.floor && _floorMaterial != null)
            {
                _floorVisual = Make("Sweep308 Floor");
                _floorVisual.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                var renderer = _floorVisual.AddComponent<MeshRenderer>(); renderer.sharedMaterial = _floorMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                _floorVisual.transform.SetPositionAndRotation(_origin + new Vector3(0, -.01f, 6), Quaternion.Euler(90, 0, 0));
                _floorVisual.transform.localScale = new Vector3(60, 60, 1);
            }
            var lightRoot = Make("Sweep308 Key Light");
            var key = lightRoot.AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 1f; key.color = Color.white;
            key.shadows = LightShadows.None; lightRoot.transform.rotation = Quaternion.Euler(50, -30, 0);

            // Physics floor: prefer a collider inside the preview scene; fall back to one hidden, unsaved object in the open scene.
            var floor = Make("Sweep308 Physics Floor");
            var box = floor.AddComponent<BoxCollider>(); box.size = new Vector3(80, .5f, 80); box.center = new Vector3(0, -.25f, 0);
            floor.transform.position = _origin + new Vector3(0, 0, 6);
            Physics.SyncTransforms();
            bool previewFloorSeen = Physics.Raycast(_origin + Vector3.up * 3, Vector3.down, out var probe, 6, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && probe.collider == box;
            if (previewFloorSeen) e.physicsFloor = "collider inside the preview scene (seen by Physics.Raycast)";
            else
            {
                Object.DestroyImmediate(floor);
                _hiddenFloorInOpenScene = new GameObject(OpenFloorName) { hideFlags = HideFlags.HideAndDontSave };
                var openBox = _hiddenFloorInOpenScene.AddComponent<BoxCollider>(); openBox.size = new Vector3(80, .5f, 80); openBox.center = new Vector3(0, -.25f, 0);
                _hiddenFloorInOpenScene.transform.position = _origin + new Vector3(0, 0, 6);
                Physics.SyncTransforms();
                bool seen = Physics.Raycast(_origin + Vector3.up * 3, Vector3.down, out probe, 6, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && probe.collider == openBox;
                if (seen)
                {
                    _usesOpenSceneFloor = true;
                    e.physicsFloor = "NOT ISOLATED: one hidden, unsaved BoxCollider object in the open scene's physics at Y=" + _origin.y
                        + " (preview colliders are not seen by Physics.Raycast); removed in cleanup";
                    // the hidden object is a root of the active scene: keep it out of the leak check
                    var active = SceneManager.GetActiveScene();
                    if (_sceneRoots.TryGetValue(active.handle, out var ids)) ids.Add(_hiddenFloorInOpenScene.GetInstanceID());
                }
                else
                {
                    // useless there: do not leave an object in the open scene for nothing
                    Object.DestroyImmediate(_hiddenFloorInOpenScene); _hiddenFloorInOpenScene = null;
                    e.physicsFloor = "NONE: no floor collider is seen by Physics.Raycast; ground-fitted effects fall back or hide";
                }
                e.notIsolated += " physics floor: " + e.physicsFloor + ";";
            }

            var cameraRoot = Make("Sweep308 Camera");
            _camera = cameraRoot.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.cameraType = _r.cameraMode == "preview" ? CameraType.Preview : CameraType.Game;
            _camera.clearFlags = CameraClearFlags.SolidColor; _camera.cullingMask = ~0;
            _camera.scene = _preview;
            _camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(_preview);
            if (_camera.overrideSceneCullingMask == 0) throw new InvalidOperationException("Private preview culling mask unavailable");
            _camera.rect = new Rect(0, 0, 1, 1); _camera.aspect = _r.width / (float)_r.height;
            _camera.nearClipPlane = .05f; _camera.farClipPlane = 200f;
            _camera.allowHDR = false; _camera.allowMSAA = false; _camera.allowDynamicResolution = false; _camera.useOcclusionCulling = false;
            if (_r.view == "first")
            {
                _camera.fieldOfView = _r.firstFieldOfView;
                _camera.transform.SetPositionAndRotation(_origin + new Vector3(0, 1.62f, -1.6f), Quaternion.identity);
            }
            else
            {
                // framing of the saved VFX120 review camera (contrast audit 2026-09-09), relative to the stage origin
                _camera.fieldOfView = 37f;
                _camera.transform.SetPositionAndRotation(_origin + new Vector3(4.7f, 3.1f, -5.7f), Quaternion.Euler(13.798718f, 329.87567f, 0));
            }
            var additional = _camera.GetUniversalAdditionalCameraData();
            additional.SetRenderer(rendererIndex);
            additional.renderPostProcessing = false; additional.renderShadows = false; additional.volumeLayerMask = 0;
            additional.antialiasing = AntialiasingMode.None; additional.dithering = false; additional.stopNaN = false;
            additional.requiresDepthTexture = _r.cameraMode == "game"; additional.requiresColorTexture = _r.cameraMode == "game";
            e.cameraPosition = _camera.transform.position; e.cameraEuler = _camera.transform.eulerAngles; e.cameraFieldOfView = _camera.fieldOfView;

            _setLightingOverride = typeof(Unsupported).GetMethod("SetOverrideLightingSettings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(Scene) }, null);
            _restoreLightingOverride = typeof(Unsupported).GetMethod("RestoreOverrideLightingSettings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            e.lightingOverride = _setLightingOverride != null && _restoreLightingOverride != null
                ? "preview-scene lighting override per render (flat ambient, no fog); the open scene's RenderSettings are checked unchanged after every glyph"
                : "UNAVAILABLE: the open scene's ambient light and fog (" + (RenderSettings.fog ? "fog ON" : "fog off") + ") reach the captures";
            if (_setLightingOverride == null || _restoreLightingOverride == null) e.notIsolated += " lighting: " + e.lightingOverride + ";";

            _target = new RenderTexture(_r.width, _r.height, 24, RenderTextureFormat.ARGB32) { name = "Sweep308 RT", hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
            if (!_target.Create()) throw new InvalidOperationException("RenderTexture creation failed");
            _readback = new Texture2D(_r.width, _r.height, TextureFormat.RGBA32, false) { name = "Sweep308 Readback", hideFlags = HideFlags.HideAndDontSave };
            int n = _r.width * _r.height;
            _lightBaseline = new Color32[n]; _darkBaseline = new Color32[n]; _pixels = new Color32[n];
            // Two renders per backdrop: the first warms shader variants up, the second is the baseline. They must agree, or every
            // later difference against the baseline would be noise of the stage itself (a temporal effect reached the camera).
            var timer = Stopwatch.StartNew();
            Capture(LightBackground, LightFloor, _pixels); Capture(LightBackground, LightFloor, _lightBaseline);
            int lightDrift = Compare(_lightBaseline, _pixels).changedPixels;
            Capture(DarkBackground, DarkFloor, _pixels); Capture(DarkBackground, DarkFloor, _darkBaseline);
            int darkDrift = Compare(_darkBaseline, _pixels).changedPixels;
            e.baselineRenderMilliseconds = timer.Elapsed.TotalMilliseconds / 4.0;
            Save(_lightBaseline, Path.Combine(_directory, "baseline_light.png"));
            Save(_darkBaseline, Path.Combine(_directory, "baseline_dark.png"));
            // ReadPixels rows run bottom-up: the last pixel is the top-right corner, backdrop in both views. The two backdrops
            // must stay neutral and clearly apart, or the open scene's fog or an injected pass tinted them.
            Color32 l = _lightBaseline[n - 1], d = _darkBaseline[n - 1];
            _report.baselinesUniformEnough = l.r - d.r >= 128 && l.g - d.g >= 128 && l.b - d.b >= 128
                && Mathf.Abs(l.r - l.g) <= 6 && Mathf.Abs(l.g - l.b) <= 6 && Mathf.Abs(d.r - d.g) <= 6 && Mathf.Abs(d.g - d.b) <= 6
                && lightDrift == 0 && darkDrift == 0;
            if (!_report.baselinesUniformEnough)
                throw new InvalidOperationException("Backdrops are not neutral/distinct/stable (light " + l + ", dark " + d + ", pixels that differ between two empty renders: light "
                    + lightDrift + ", dark " + darkDrift + "): the open scene's fog, a renderer feature or an edit-mode preview hook reaches the capture camera");
        }

        static void Figure(Transform root)
        {
            var torso = Make("Neutral torso"); torso.transform.SetParent(root, false);
            torso.transform.localPosition = new Vector3(0, .95f, 0); torso.transform.localScale = new Vector3(.4f, .55f, .3f);
            torso.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Capsule.fbx");
            var head = Make("Neutral head"); head.transform.SetParent(root, false);
            head.transform.localPosition = new Vector3(0, 1.62f, 0); head.transform.localScale = Vector3.one * .29f;
            head.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            foreach (var part in new[] { torso, head })
            {
                var renderer = part.AddComponent<MeshRenderer>(); renderer.sharedMaterial = _figureMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            }
        }

        static void Cleanup(Report report)
        {
            void Safe(Action action)
            {
                try { action(); }
                catch (Exception error) { report.cleanupSucceeded = false; report.reason = (report.reason ?? "") + "\nCleanup error: " + error.Message; }
            }
            Safe(() => { if (_camera != null) _camera.targetTexture = null; });
            Safe(() => { if (_previewCreated && _preview.IsValid()) EditorSceneManager.ClosePreviewScene(_preview); });
            _previewCreated = false;
            Safe(() => { if (_hiddenFloorInOpenScene != null) Object.DestroyImmediate(_hiddenFloorInOpenScene); });
            _hiddenFloorInOpenScene = null; _usesOpenSceneFloor = false;
            Safe(() => { if (_target != null) { _target.Release(); Object.DestroyImmediate(_target); } });
            Safe(() => { if (_readback != null) Object.DestroyImmediate(_readback); });
            Safe(() => { if (_figureMaterial != null) Object.DestroyImmediate(_figureMaterial); });
            Safe(() => { if (_floorMaterial != null) Object.DestroyImmediate(_floorMaterial); });
            _target = null; _readback = null; _camera = null; _figureMaterial = null; _floorMaterial = null; _floorVisual = null;
            _primary = null; _secondary = null; _split = null; _lightBaseline = null; _darkBaseline = null; _pixels = null; _initialMeshIds = null;
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------------------------------ one glyph
        static GlyphRow CaptureGlyph(int catalogIndex)
        {
            var entry = _catalog.Entries[catalogIndex];
            var row = new GlyphRow { index = catalogIndex + 1, glyph = entry.Glyph, id = Id(catalogIndex, entry.Glyph), settingsKey = _settingsKey };
            GameObject instance = null;
            var adopted = new List<GameObject>(); var adoptedNames = new List<string>(); var unattributed = new List<string>();
            var owned = new HashSet<Mesh>();
            var randomState = UnityEngine.Random.state;
            SnapshotOpenScenes();
            Camera openMain = Camera.main;
            var openData = openMain != null ? openMain.GetComponent<UniversalAdditionalCameraData>() : null;
            string openDataJson = openData != null ? EditorJsonUtility.ToJson(openData) : null;
            bool openDataDirty = openData != null && EditorUtility.IsDirty(openData);
            var openDepthOption = openData != null ? openData.requiresDepthOption : CameraOverrideOption.UsePipelineSettings;
            var openColorOption = openData != null ? openData.requiresColorOption : CameraOverrideOption.UsePipelineSettings;
            try
            {
                _prefabs.TryGetValue(entry.Glyph, out var prefab);
                if (prefab == null) throw new InvalidOperationException("The selected set has no prefab for this glyph");
                row.prefabPath = AssetDatabase.GetAssetPath(prefab);
                row.sameAsCatalogPrefab = prefab == entry.Prefab;
                row.dependencyHash = AssetDatabase.GetAssetDependencyHash(row.prefabPath).ToString();
                UnityEngine.Random.InitState(308000 + catalogIndex);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _preview);
                instance.hideFlags = HideFlags.HideAndDontSave;
                var effect = instance.GetComponent<Vfx120Effect>();
                if (effect == null) throw new InvalidOperationException("Prefab root has no Vfx120Effect (not drivable by Sample)");
                var profile = effect.Profile;
                if (profile == null) throw new InvalidOperationException("Vfx120Effect has no profile");
                row.profilePath = AssetDatabase.GetAssetPath(profile); row.family = profile.Family; row.behavior = profile.Behavior.ToString();
                row.assigned = profile.Assigned;
                row.dispatch = Dispatch(profile);
                var deployAsset = DeployProfileField != null ? DeployProfileField.GetValue(profile) as Object : null;
                row.deploy308Profile = deployAsset != null ? AssetDatabase.GetAssetPath(deployAsset) : "";
                effect.PreviewControlled = true; effect.DemonstrationCues = true;
                // Same convention as Vfx120Review: self-centred glyphs driven by review fixtures get no target figure.
                bool selfCentred = entry.Glyph == "송" || entry.Glyph == "걱" || entry.Glyph == "건" || entry.Glyph == "것" || entry.Glyph == "겅";
                Transform primary = selfCentred ? null : _primary;
                row.primaryTarget = primary != null;
                Vector3 origin = _origin + new Vector3(0, 1, 0);
                if (_r.view == "first")
                    origin = Vfx120LaunchPoint.Resolve(profile, _camera, _camera.transform.position + _camera.transform.forward * 1.6f + Vector3.down * .35f);
                effect.SetSecondaryTargets(_secondary, entry.Glyph == "안" ? _split : null);
                var plan = DemonstrationPlan(profile, origin, out row.areaPlanSource);
                effect.SetAreaPlan(plan);
                effect.Begin(origin, primary, _primary.position + Vector3.up, Color.white);
                row.leakedRootsAdopted += AdoptLeaks(adopted, adoptedNames, unattributed);
                if (!effect.Begun || !float.IsFinite(effect.Life) || effect.Life <= 0)
                    throw new InvalidOperationException("Effect did not begin with a valid lifetime");
                // The deploy layer draws hostless here (its own mesh, PC tier): the game routes it through a pooled host.
                var deployRuntime = DeployRuntimeProperty != null ? DeployRuntimeProperty.GetValue(effect) as Object : null;
                row.deploy308Active = deployRuntime != null;
                row.deploy308RetiresLegacyBody = row.deploy308Active && DeployRetiresProperty != null && DeployRetiresProperty.GetValue(effect) is bool retires && retires;
                if (row.deploy308Active) row.dispatch = row.deploy308RetiresLegacyBody ? "Deploy308(legacy body retired)" : "Deploy308+" + row.dispatch;
                foreach (var t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
                if (instance.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException("Unexpected collider in the VFX hierarchy; the sweep is render-only");
                RememberMeshes(instance, owned);
                row.life = effect.Life;
                var seconds = new float[3];
                row.impactClock = Beats(effect, profile, seconds);
                row.beats = new Beat[3];
                for (int b = 0; b < 3; b++)
                {
                    var beat = new Beat { name = BeatNames[b], seconds = seconds[b], fractionOfLife = seconds[b] / effect.Life };
                    row.beats[b] = beat;
                    effect.Sample(seconds[b]);
                    row.leakedRootsAdopted += AdoptLeaks(adopted, adoptedNames, unattributed);
                    RememberMeshes(instance, owned);
                    // adopted roots are drawn by the capture camera too: they belong in the counts
                    var layers = new SortedSet<string>();
                    Count(instance, beat, layers);
                    foreach (var go in adopted) if (go != null) Count(go, beat, layers);
                    beat.layers = string.Join(",", layers);
                    row.maximumTriangles = Mathf.Max(row.maximumTriangles, beat.triangles);
                    row.maximumParticles = Mathf.Max(row.maximumParticles, beat.particles);
                    Capture(LightBackground, LightFloor, _pixels);
                    beat.light = Compare(_lightBaseline, _pixels); beat.light.image = row.id + "_" + beat.name + "_light.png";
                    Save(_pixels, Path.Combine(_directory, beat.light.image));
                    Capture(DarkBackground, DarkFloor, _pixels);
                    beat.dark = Compare(_darkBaseline, _pixels); beat.dark.image = row.id + "_" + beat.name + "_dark.png";
                    Save(_pixels, Path.Combine(_directory, beat.dark.image));
                    bool light = beat.light.changedPixels > 0, dark = beat.dark.changedPixels > 0;
                    beat.pixelStatus = light && dark ? "PIXELS_BOTH_BACKDROPS" : light || dark ? "PIXELS_ONE_BACKDROP" : "NOT_VISIBLE_AT_THIS_BEAT";
                    if (effect.Age != seconds[b]) throw new InvalidOperationException("Effect age moved during the backdrop pair");
                }
                row.status = "CAPTURED";
            }
            catch (Exception error) { row.status = "UNVERIFIED_EXCEPTION"; row.reason = error.ToString(); }
            finally
            {
                if (instance != null)
                {
                    RememberMeshes(instance, owned);
                    row.destroyHooksRun += RunDestroyHooks(instance, row);
                    if (instance != null) Object.DestroyImmediate(instance);
                }
                row.leakedRootsAdopted += AdoptLeaks(adopted, adoptedNames, unattributed);
                foreach (var go in adopted)
                {
                    if (go == null) continue;
                    row.destroyHooksRun += RunDestroyHooks(go, row);
                    if (go != null) Object.DestroyImmediate(go);
                }
                foreach (var mesh in owned) if (mesh != null) Object.DestroyImmediate(mesh);
                row.generatedMeshes = owned.Count;
                row.leakedRootNames = string.Join(", ", adoptedNames);
                row.unattributedHiddenRoots = string.Join(", ", unattributed);
                UnityEngine.Random.state = randomState;
                // Vfx120Effect.AreaRemake (water wave) switches depth/colour textures on Camera.main: put the open scene's camera back.
                // The two options the runtime writes are restored through their own setters; the whole-component overwrite is
                // only the fallback for a change this tool does not know about.
                if (openData != null && openDataJson != EditorJsonUtility.ToJson(openData))
                {
                    row.openSceneCameraTouched = true;
                    openData.requiresDepthOption = openDepthOption; openData.requiresColorOption = openColorOption;
                    if (openDataJson != EditorJsonUtility.ToJson(openData)) EditorJsonUtility.FromJsonOverwrite(openDataJson, openData);
                    if (!openDataDirty) EditorUtility.ClearDirty(openData);
                    row.openSceneCameraRestored = openDataJson == EditorJsonUtility.ToJson(openData);
                    if (!row.openSceneCameraRestored) row.reason = (row.reason ?? "") + "\nThe open scene's main camera data could not be put back exactly.";
                }
                // the stage must look exactly like the baseline again, or later glyphs would inherit this one's leftovers
                Capture(DarkBackground, DarkFloor, _pixels);
                row.environmentPreserved = _environmentStamp == EnvironmentStamp();
                row.hierarchyPreserved = OpenScenesUnchanged(out string note);
                if (!row.hierarchyPreserved) row.reason = (row.reason ?? "") + "\nOpen scene check: " + note;
                if (Compare(_darkBaseline, _pixels).changedPixels != 0)
                {
                    row.status = row.status == "CAPTURED" ? "UNVERIFIED_STAGE_NOT_CLEAN_AFTERWARDS" : row.status;
                    row.reason = (row.reason ?? "") + "\nThe empty stage differs from the baseline after this glyph.";
                }
            }
            return row;
        }

        static string Dispatch(Vfx120Profile p)
        {
            if (p.WoodDeerPresentation || p.FireHaetaePresentation || p.MetalTigerPresentation || p.DokkaebiClubPresentation
                || p.WaterTurtlePresentation || p.StoneDokkaebiPresentation) return "SummonPresentation";
            if (p.WardKind != Vfx120WardKind.None) return "FixedWard";
            if (p.AreaRemake != Vfx120AreaRemake.None) return "AreaRemake." + p.AreaRemake;
            if (p.Bolt300Prefab != null) return "Bolt300";
            return "General";
        }

        // Illustration only. Combat builds the real AreaImpactPlan; nothing here is a damage shape.
        static AreaImpactPlan DemonstrationPlan(Vfx120Profile profile, Vector3 origin, out string source)
        {
            source = "none";
            Vector3 forward = Vector3.forward;
            Vector3 ground = _primary.position;
            switch (profile.AreaRemake)
            {
                case Vfx120AreaRemake.BambooField:
                {
                    // the remake accepts a circle with exactly 17 planned spikes
                    var plan = new AreaImpactPlan { Shape = AreaShape.Circle, Point = ground, Direction = forward, Delay = profile.Flight,
                        Radius = Mathf.Max(1.5f, profile.Size), VisualSeed = 308 };
                    for (int i = 0; i < 17; i++)
                    {
                        float radius = plan.Radius * Mathf.Sqrt((i + .5f) / 17f), angle = i * 137.508f * Mathf.Deg2Rad;
                        plan.Spikes.Add(new PlannedSpike { Point = ground + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius),
                            RiseAt = plan.Delay * (.3f + .7f * i / 16f) });
                    }
                    source = "SWEEP_ILLUSTRATION_CIRCLE_17_SPIKES";
                    return plan;
                }
                case Vfx120AreaRemake.NeedleVolley:
                {
                    var plan = new AreaImpactPlan { Shape = AreaShape.Volley, Point = ground + Vector3.up, Direction = forward, Length = 9f, Speed = 22f,
                        Delay = profile.Flight, Radius = 45f, ShotCount = 24, ShotInterval = .045f, VisualSeed = 308 };
                    for (int i = 0; i < plan.ShotCount; i++)
                    {
                        float angle = -45f + 90f * Mathf.Repeat(i * .618034f, 1f);
                        float launch = plan.CreatedAt + plan.Delay + i * plan.ShotInterval;
                        Vector3 end = _origin + Quaternion.Euler(0, angle, 0) * forward * plan.Length;
                        plan.Shots.Add(new PlannedHit { LaunchTime = launch, ImpactTime = launch + plan.Length / plan.Speed, ImpactPoint = end, HasImpactPoint = true });
                    }
                    source = "SWEEP_ILLUSTRATION_VOLLEY_24_FIXED_ENDPOINTS";
                    return plan;
                }
                case Vfx120AreaRemake.SandFront:
                case Vfx120AreaRemake.WaterWave:
                    source = "SWEEP_ILLUSTRATION_PATH";
                    return new AreaImpactPlan { Shape = AreaShape.Path, Point = _origin + new Vector3(0, 0, .8f), Direction = forward, Delay = profile.Flight,
                        Radius = Mathf.Max(2f, profile.Size), Length = 6f, Speed = 3f, VisualSeed = 308 };
                case Vfx120AreaRemake.FlameCone:
                    source = "SWEEP_ILLUSTRATION_CONE";
                    return new AreaImpactPlan { Shape = AreaShape.Cone, Point = origin, Direction = forward, Delay = profile.Flight,
                        Radius = profile.Size, Angle = 32, Length = Mathf.Max(2.5f, profile.Size * 2) };
            }
            // every other glyph: the review illustration, moved from the world origin to the stage
            var review = Vfx120Review.CreateDemonstrationAreaPlan(profile);
            if (review == null) return null;
            review.Point += _origin;
            if (review.Shape == AreaShape.Cone) review.Point = origin;
            source = "REVIEW_PROFILE_ILLUSTRATION_NOT_COMBAT_PLAN";
            return review;
        }

        // cast / impact / just before the end. Returns the impact clock that selected the second beat.
        static float Beats(Vfx120Effect effect, Vfx120Profile profile, float[] seconds)
        {
            float life = effect.Life;
            float impact = effect.ReceivedImpactClock > 0 ? effect.ReceivedImpactClock : profile.Flight;
            var plan = effect.ReceivedAreaPlan;
            if (plan != null)
            {
                impact = Mathf.Max(.01f, plan.Delay);
                if (plan.Shape == AreaShape.Volley) impact = plan.Delay + plan.Length / Mathf.Max(1f, plan.Speed);
            }
            if (!float.IsFinite(impact) || impact <= 0) impact = Mathf.Min(.6f, life * .3f);
            bool arrives = plan != null || profile.Behavior == Vfx120Behavior.Projectile || profile.Behavior == Vfx120Behavior.Bind
                || profile.Behavior == Vfx120Behavior.Zone || profile.Behavior == Vfx120Behavior.Burst || profile.Behavior == Vfx120Behavior.Wave;
            float end = life - Mathf.Clamp(life * .08f, .1f, .3f);
            float second = arrives ? impact + .08f : Mathf.Max(impact + .08f, life * .42f);
            second = Mathf.Clamp(second, .02f, Mathf.Max(.02f, end - .05f));
            float first = Mathf.Clamp(Mathf.Min(.12f, impact * .5f), 0f, Mathf.Max(0f, second - .02f));
            seconds[0] = first; seconds[1] = second; seconds[2] = Mathf.Max(second, end);
            return impact;
        }

        static void Count(GameObject root, Beat beat, SortedSet<string> layers)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled) continue;
                beat.renderers++;
                string layer = LayerMask.LayerToName(renderer.gameObject.layer);
                layers.Add(string.IsNullOrEmpty(layer) ? "Layer" + renderer.gameObject.layer : layer);
                if (renderer is ParticleSystemRenderer particleRenderer)
                {
                    var system = renderer.GetComponent<ParticleSystem>();
                    if (system == null) continue;
                    beat.particleSystems++;
                    int alive = system.particleCount;
                    beat.particles += alive;
                    int per = 2;
                    if (particleRenderer.renderMode == ParticleSystemRenderMode.Mesh && particleRenderer.mesh != null) per = Triangles(particleRenderer.mesh);
                    beat.triangles += alive * per;
                }
                else if (renderer is LineRenderer line) { beat.lineOrTrailPoints += line.positionCount; beat.triangles += Mathf.Max(0, line.positionCount - 1) * 2; }
                else if (renderer is TrailRenderer trail) { beat.lineOrTrailPoints += trail.positionCount; beat.triangles += Mathf.Max(0, trail.positionCount - 1) * 2; }
                else if (renderer is SkinnedMeshRenderer skinned) { if (skinned.sharedMesh != null) beat.triangles += Triangles(skinned.sharedMesh); }
                else
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null) beat.triangles += Triangles(filter.sharedMesh);
                }
            }
            foreach (var light in root.GetComponentsInChildren<Light>(false)) if (light.enabled) beat.lights++;
        }

        // #308 deploy layer members, looked up by name so this tool compiles with or without that layer in the project.
        static readonly FieldInfo DeployProfileField = typeof(Vfx120Profile).GetField("Deploy308", BindingFlags.Instance | BindingFlags.Public);
        static readonly PropertyInfo DeployRuntimeProperty = typeof(Vfx120Effect).GetProperty("Deploy308Runtime", BindingFlags.Instance | BindingFlags.Public);
        static readonly PropertyInfo DeployRetiresProperty = typeof(Vfx120Effect).GetProperty("Deploy308Retires", BindingFlags.Instance | BindingFlags.Public);

        // Edit mode sends OnDestroy only to [ExecuteAlways] components. The runtime VFX components release what they own there
        // (FixedWardVfx unsubscribes from RenderPipelineManager.beginCameraRendering, presentations destroy generated meshes):
        // without this call every ward capture would leave one static render callback behind until the next assembly reload.
        // The hooks are the components' own, are written for edit mode (DestroyImmediate when not playing) and run once.
        static int RunDestroyHooks(GameObject root, GlyphRow row)
        {
            int run = 0;
            var runtimeAssembly = typeof(Vfx120Effect).Assembly;
            // the effect first: its hook disposes and removes the presenters it added
            var behaviours = new List<MonoBehaviour>(root.GetComponents<Vfx120Effect>());
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) if (!behaviours.Contains(behaviour)) behaviours.Add(behaviour);
            foreach (var behaviour in behaviours)
            {
                if (behaviour == null) continue;   // already destroyed by an earlier hook
                var type = behaviour.GetType();
                if (type.Assembly != runtimeAssembly) continue;
                if (type.IsDefined(typeof(ExecuteAlways), true) || type.IsDefined(typeof(ExecuteInEditMode), true)) continue;   // Unity calls these itself
                var hook = type.GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (hook == null) continue;
                try { hook.Invoke(behaviour, null); run++; }
                catch (Exception error)
                {
                    var inner = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                    row.reason = (row.reason ?? "") + "\nDestroy hook of " + type.Name + " failed: " + inner.Message;
                }
            }
            return run;
        }

        static int Triangles(Mesh mesh)
        {
            long indices = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                if (mesh.GetTopology(i) == MeshTopology.Triangles) indices += (long)mesh.GetIndexCount(i);
            return (int)(indices / 3);
        }

        static void RememberMeshes(GameObject root, HashSet<Mesh> owned)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && !EditorUtility.IsPersistent(filter.sharedMesh)
                    && !_initialMeshIds.Contains(filter.sharedMesh.GetInstanceID())) owned.Add(filter.sharedMesh);
        }

        // ------------------------------------------------------------------------------------------ pixels
        static void Capture(Color background, Color floor, Color32[] pixels)
        {
            var oldTarget = _camera.targetTexture; var oldActive = RenderTexture.active;
            bool overridden = false;
            // The open scene's values as they are NOW: the editor is shared, another session may have changed them since
            // the sweep began, and a stale copy written back would undo that session's work.
            bool openFog = RenderSettings.fog; AmbientMode openAmbientMode = RenderSettings.ambientMode; Color openAmbientLight = RenderSettings.ambientLight;
            // The project compiles shader variants asynchronously: a first use would be captured as the cyan placeholder and
            // still count as "pixels". Compile synchronously for this render only (same pattern as the CodexWorld capture tools).
            bool oldAsync = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                if (_floorMaterial != null) _floorMaterial.SetColor("_BaseColor", floor);
                _camera.backgroundColor = background; _camera.targetTexture = _target;
                if (_setLightingOverride != null && _restoreLightingOverride != null)
                {
                    // The editor's own preview isolation (PreviewRenderUtility does the same): while the override is active,
                    // RenderSettings reads and writes address the preview scene. Written only when the call reports success.
                    object applied = _setLightingOverride.Invoke(null, new object[] { _preview });
                    overridden = true;
                    if (!(applied is bool) || (bool)applied)
                    {
                        RenderSettings.fog = false;
                        RenderSettings.ambientMode = AmbientMode.Flat;
                        RenderSettings.ambientLight = new Color(.5f, .5f, .5f, 1);
                    }
                    else if (_report != null && (_report.environment.lightingOverride == null || !_report.environment.lightingOverride.StartsWith("REFUSED", StringComparison.Ordinal)))
                    {
                        // nothing was written, but the open scene's ambient light and fog reach the captures: say so once
                        _report.environment.lightingOverride = "REFUSED: SetOverrideLightingSettings returned false; the open scene's ambient light and fog ("
                            + (openFog ? "fog ON" : "fog off") + ") reach the captures";
                        _report.environment.notIsolated += " lighting: " + _report.environment.lightingOverride + ";";
                    }
                }
                _camera.Render();
                RenderTexture.active = _target;
                _readback.ReadPixels(new Rect(0, 0, _r.width, _r.height), 0, 0, false);
                _readback.GetRawTextureData<Color32>().CopyTo(pixels);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = oldAsync;
                if (overridden) _restoreLightingOverride.Invoke(null, null);
                _camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                if (overridden && (RenderSettings.fog != openFog || RenderSettings.ambientMode != openAmbientMode || RenderSettings.ambientLight != openAmbientLight))
                {
                    // The override did not shield the open scene: put its three values back and stop using the override.
                    RenderSettings.fog = openFog; RenderSettings.ambientMode = openAmbientMode; RenderSettings.ambientLight = openAmbientLight;
                    _setLightingOverride = null; _restoreLightingOverride = null;
                    if (_report != null)
                    {
                        _report.environment.lightingOverride = "LEAKED_ONCE_AND_RESTORED: the override wrote to the open scene's RenderSettings; values were put back and the override is off for the rest of the run";
                        _report.environment.notIsolated += " lighting: " + _report.environment.lightingOverride + ";";
                    }
                }
            }
        }

        static void Save(Color32[] pixels, string path)
        {
            _readback.SetPixels32(pixels);
            File.WriteAllBytes(path, _readback.EncodeToPNG());
        }

        static PixelMetrics Compare(Color32[] baseline, Color32[] pixels)
        {
            var result = new PixelMetrics();
            int width = _r.width, height = _r.height;
            long changedTotal = 0;
            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int i = 0; i < baseline.Length; i++)
            {
                int r = Math.Abs(baseline[i].r - pixels[i].r), g = Math.Abs(baseline[i].g - pixels[i].g), b = Math.Abs(baseline[i].b - pixels[i].b);
                int maximum = Math.Max(r, Math.Max(g, b));
                if (maximum > result.maximumChannelDifference) result.maximumChannelDifference = maximum;
                if (maximum <= Tolerance) continue;
                result.changedPixels++; changedTotal += r + g + b;
                if (maximum >= 24) result.strongPixels++;
                int top = Math.Max(pixels[i].r, Math.Max(pixels[i].g, pixels[i].b));
                if (top >= 235) result.brightPixels++;
                if (pixels[i].r >= 250 && pixels[i].g >= 250 && pixels[i].b >= 250) result.saturatedPixels++;
                int x = i % width, y = i / width;
                if (x < minX) minX = x; if (y < minY) minY = y; if (x > maxX) maxX = x; if (y > maxY) maxY = y;
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1) result.frameEdgePixels++;
            }
            result.coverageFraction = result.changedPixels / (double)baseline.Length;
            if (result.changedPixels > 0)
            {
                result.meanChangedPixelDifference = changedTotal / (result.changedPixels * 3.0);
                result.minX = minX; result.minY = minY; result.maxX = maxX; result.maxY = maxY;
            }
            return result;
        }
    }
}
