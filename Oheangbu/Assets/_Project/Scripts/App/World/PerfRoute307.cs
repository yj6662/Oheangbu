using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;

namespace Oheangbu.App.World
{
    // #307 phase 0 (Tools/Unity/Plan307/PERF_DESIGN.md C.0): measurement-only station route. Nothing here is part of ordinary
    // play: the Editor tool Perf307 adds it after arrival, and a player creates it only with the explicit command line
    // "-perf307 <out folder>" (C2StandaloneBenchmark pattern). It teleports through the stations of routes.json, holds fixed-yaw
    // poses through the real camera rig, walks with a virtual keyboard (IPerfInput307) and records FrameTiming, ProfilerRecorder
    // values, the Perf307Markers scopes and the renderers' own counters every frame. It forces the requested quality tier,
    // vSync 0 and the frame cap after arrival and re-checks them every frame (UserSettingsService re-applies the saved settings on
    // an active-scene change); it never calls the settings service, never saves user settings and never commits game progress.
    // Output: <out>/summary.json, frames.csv, segments.csv, spikes.csv (+ top-markers.csv for arrival). IMPLEMENTED, not validated.

    [Serializable] public sealed class PerfTier307 { public string key = "", quality = "", pipeline = ""; }

    [Serializable] public sealed class PerfStation307
    {
        // source: start (Content.StartFeet/StartYaw) | checkpoint (Content.Checkpoints[sourceId]) | point (Content.Points[sourceId])
        //         | fixed (feet) | forest305-edge (placement of the renderer whose sheet or object is sourceId nearest to `near`,
        //         stepped `inset` m toward `near`; base yaw faces that placement)
        public string id = "", source = "fixed", sourceId = "", note = "";
        public Vector3 feet;
        public Vector2 near;
        public float baseYaw, inset = 20f, search = 40f;
        public bool yawRelative = true, walk = true;
        public float[] poseYaws = { 0f, 180f };
        public float walkYaw;
    }

    [Serializable] public sealed class PerfRoutes307
    {
        public string version = "", note = "";
        public float warmupSeconds = 10f, settleTeleportSeconds = 4f, settlePoseSeconds = 1.5f, sampleSeconds = 5f, walkSeconds = 8f;
        public float arrivalSeconds = 12f, abSettleSeconds = 1.5f;
        public int laps = 2, maxFrames = 60000, spikeTopMarkers = 8, spikeLimit = 400, builtinMarkerLimit = 48, builtinMarkerPerFilter = 4, maxTierDriftEvents = 3;
        public float focusMinimum = .9f, repeatTolerance = .05f, spikeFactor = 2f, budgetMs = 8.333f, abNoiseFactor = 2f;
        public bool editorRenderTexture = true;
        public int renderWidth = 1920, renderHeight = 1080;
        public string standaloneWindowMode = "FullScreenWindow";
        public PerfTier307[] tiers = Array.Empty<PerfTier307>();
        public string[] coreRecorders = Array.Empty<string>(), customMarkers = Array.Empty<string>(), builtinMarkers = Array.Empty<string>();
        public string[] builtinMarkerContains = Array.Empty<string>(), gpuMarkers = Array.Empty<string>(), spikeExclude = Array.Empty<string>();
        public PerfStation307[] stations = Array.Empty<PerfStation307>();

        public PerfTier307 Tier(string key)
        {
            if (tiers != null) foreach (var t in tiers) if (t != null && t.key == key) return t;
            return null;
        }
    }

    public sealed class PerfOptions307
    {
        public string Command = "baseline", Tier = "pc", Variant = "full", Mode = "uncapped", Toggle = "", Output = "", RunId = "";
        public bool Standalone;
        public readonly List<string> Meta = new List<string>();
        public bool Uncapped => Mode != "cap120";
        public int TargetFrameRate => Uncapped ? -1 : 120;
    }

    /// <summary>Virtual keyboard for the walk segments: Hold(true) = W held, Hold(false) = neutral, both without mouse motion.</summary>
    public interface IPerfInput307
    {
        string Begin();
        void Hold(bool walk);
        string End(out bool ok);
    }

    [DefaultExecutionOrder(32000)]
    public sealed class PerfRoute307 : MonoBehaviour
    {
        // ------------------------------------------------------------------ wiring (set before Begin)
        public Func<bool> FocusProbe;          // Editor: InternalEditorUtility.isApplicationActive; player: Application.isFocused
        public IPerfInput307 VirtualInput;     // Editor: VirtualInput303 adapter; player: PerfKeyboard307
        public Action<bool> Variant;           // ab: false = A (prior state), true = B (toggle applied)

        public bool Done { get; private set; }
        public string Status { get; private set; } = "idle";
        public string Result { get; private set; } = "";
        public float EstimatedSeconds { get; private set; }
        public string Output => options != null ? options.Output : "";
        public string Progress => Status + " step " + stepIndex + "/" + steps.Count + " frames " + frameCount + (steps.Count > 0 && stepIndex < steps.Count ? " " + steps[stepIndex].Label : "");

        sealed class Step { public int Kind, Station = -1, Segment = -1; public float Seconds, Yaw; public bool Record, Walk, B; public string Label = ""; }
        const int Teleport = 0, Hold = 1, SetVariant = 2;
        sealed class Resolved { public PerfStation307 Spec; public Vector3 Feet; public float BaseYaw; public string How = ""; public bool Ok; }
        sealed class Segment
        {
            public string Label = "", Key = "", Kind = "", Station = ""; public int Lap; public float Yaw;
            public int First = -1, End = -1, Gc0Start, Gc0End; public float HpStart = -1, HpEnd = -1;
        }
        struct Rec { public ProfilerRecorder Recorder; public float Scale; public string Name; public bool Time; }

        PerfRoutes307 routes; PerfOptions307 options; WorldMacroPlaytestSession session; PlaytestUiRoot ui; PlayerVitals vitals;
        PerfTier307 tier; int qualityIndex = -1; RenderPipelineAsset expectedPipeline;
        readonly List<Step> steps = new List<Step>(); readonly List<Segment> segments = new List<Segment>(); readonly List<Resolved> stations = new List<Resolved>();
        readonly List<string> columns = new List<string>(), notes = new List<string>(), missing = new List<string>(), logs = new List<string>();
        readonly List<bool> timeColumn = new List<bool>(); readonly List<int> spikeColumns = new List<int>();
        Rec[] recs = Array.Empty<Rec>();
        CompactRebuildArtRenderer[] arts = Array.Empty<CompactRebuildArtRenderer>();
        CompactGrassRenderer266[] grasses = Array.Empty<CompactGrassRenderer266>();
        CompactNaturalSolids[] solids = Array.Empty<CompactNaturalSolids>();
        long[] solidQueries = Array.Empty<long>();
        readonly List<Behaviour> disabled = new List<Behaviour>();
        Func<double> mapTick;
        Camera view; RenderTexture target; bool viewTargetSet;
        float[] data = Array.Empty<float>(); int width, frameCount, recStart, counterStart; bool capHit;
        readonly FrameTiming[] timing = new FrameTiming[1];
        int stepIndex; bool stepStarted, recordNow, walkNow; int recordSegment = -1; double stepAt, startedAt;
        int gameRenders, sceneRenders, otherRenders, lastGc0;
        bool tierOkNow = true, inDrift, windowOkNow = true, enforceWindow; int driftEvents, driftFrames, windowFrames;
        int exceptions, errors; bool subscribed, inputStarted; string inputBegin = "", inputEnd = "";
        string startedUtc = "";

        // flags column bits
        const int FFocused = 1, FTier = 2, FBlocked = 4, FLoading = 8, FWindow = 16, FWalk = 32;
        const int FixedColumns = 13;   // seg,t,interval,flags,cpuFrame,cpuMain,presentWait,render,gpu,gameRenders,sceneRenders,gc0,hp

        // ------------------------------------------------------------------ player command line (explicit only)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void LaunchStandalone()
        {
            if (Application.isEditor) return;
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-perf307");
            if (index < 0 || index + 1 >= args.Length) return;
            if (FindFirstObjectByType<PerfRoute307>() != null) return;
            var go = new GameObject("Perf307_Standalone"); DontDestroyOnLoad(go);
            var runner = go.AddComponent<PerfRoute307>();
            // an explicit -perf307 launch never falls through into ordinary play: any setup error quits with a refusal
            try { runner.ConfigureStandalone(args, index); }
            catch (Exception e) { runner.FailBoot("standalone setup failed: " + e.GetType().Name + ": " + e.Message); }
        }

        int boot = -1; double bootAt, bootStable; string bootSuffix = "", routesPath = "";

        static string Arg(string[] args, string key, string fallback)
        {
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        void ConfigureStandalone(string[] args, int index)
        {
            options = new PerfOptions307
            {
                Standalone = true, Output = Path.GetFullPath(args[index + 1]),
                Command = Arg(args, "-perf307-command", "baseline"), Variant = Arg(args, "-perf307-variant", "full"), Mode = Arg(args, "-perf307-mode", "uncapped"),
                RunId = Arg(args, "-perf307-run", DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture))
            };
            options.Tier = options.Command == "arrival" ? "saved" : "pc";   // a player build carries only the PC level
            routesPath = Arg(args, "-perf307-routes", "");
            Directory.CreateDirectory(options.Output);
            Application.runInBackground = true;
            try { routes = JsonUtility.FromJson<PerfRoutes307>(File.ReadAllText(routesPath)); }
            catch (Exception e) { FailBoot("routes unreadable (" + routesPath + "): " + e.Message); return; }
            if (options.Command != "baseline" && options.Command != "arrival") { FailBoot("standalone command must be baseline or arrival"); return; }
            FocusProbe = () => Application.isFocused;
            boot = 0; bootAt = Time.realtimeSinceStartupAsDouble; bootStable = -1;
            File.WriteAllText(Path.Combine(options.Output, "standalone-boot.txt"), "booting " + DateTime.UtcNow.ToString("s", CultureInfo.InvariantCulture) + "Z args: " + string.Join(" ", args));
        }

        void FailBoot(string why)
        {
            Status = "FAIL"; Result = "refused: " + why; Done = true; boot = -1;
            try { if (options != null && !string.IsNullOrEmpty(options.Output)) { Directory.CreateDirectory(options.Output); File.WriteAllText(Path.Combine(options.Output, "summary.json"), JsonUtility.ToJson(new Summary307 { status = "FAIL", scope = Result, standalone = true }, true)); } }
            catch { }
            if (!Application.isEditor) Application.Quit(3);
        }

        void BootTick()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            var root = PlaytestUiRoot.Instance;
            if (boot == 0)
            {
                if (now - bootAt > 180) { FailBoot("lobby title did not appear within 180 s"); return; }
                if (root == null || !root.IsTitle || root.Busy) { bootStable = -1; return; }
                if (bootStable < 0) bootStable = now;
                if (now - bootStable < 2) return;
                bootSuffix = PlaytestUiRoot.DiagnosticSuffix;
                if (!bootSuffix.StartsWith("_perf307", StringComparison.Ordinal)) { FailBoot("-perf307 needs --ui-test-slot=_perf307_<id> (isolated save slot); got '" + bootSuffix + "'"); return; }
                if (!root.ActiveSlotName.EndsWith(bootSuffix, StringComparison.Ordinal)) { FailBoot("active slot " + root.ActiveSlotName + " does not carry the isolated suffix"); return; }
                var start = typeof(PlaytestUiRoot).GetMethod("StartNew", BindingFlags.Instance | BindingFlags.NonPublic);
                if (start == null) { FailBoot("PlaytestUiRoot.StartNew not found"); return; }
                start.Invoke(root, null); boot = 1; bootAt = now; return;
            }
            if (boot == 1)
            {
                if (now - bootAt > 300) { FailBoot("main scene did not finish loading within 300 s"); return; }
                var s = FindFirstObjectByType<WorldMacroPlaytestSession>();
                if (s == null || !s.InitializationComplete || root == null || root.LoadingInProgress) return;
                if (s.TestSaveSuffix != bootSuffix) { FailBoot("session save suffix '" + s.TestSaveSuffix + "' is not the isolated '" + bootSuffix + "'"); return; }
                boot = -1;
                if (options.Command != "arrival") VirtualInput = new PerfKeyboard307(s);
                options.Meta.Add("arrivalSecondsFromStartNew=" + (now - bootAt).ToString("F1", CultureInfo.InvariantCulture));
                string why = Begin(s, routes, options);
                if (why != null) FailBoot(why);
            }
        }

        // ------------------------------------------------------------------ begin
        /// <summary>Starts the route on an arrived session. Returns null when running, else "refused: …" (nothing changed).</summary>
        public string Begin(WorldMacroPlaytestSession s, PerfRoutes307 r, PerfOptions307 o)
        {
            if (Status == "running") return "refused: already running";
            if (s == null || !s.InitializationComplete || s.Content == null || s.Walker == null || s.Walker.Body == null) return "refused: session not ready";
            if (r == null || r.stations == null || r.stations.Length == 0) return "refused: routes have no stations";
            if (o == null || string.IsNullOrEmpty(o.Output)) return "refused: no output folder";
            if (o.Command != "baseline" && o.Command != "arrival" && o.Command != "ab") return "refused: unknown command " + o.Command;
            if (o.Command == "ab" && Variant == null) return "refused: ab needs a Variant toggle";
            if (o.Command != "arrival" && VirtualInput == null) return "refused: no virtual input";
            // tier "saved" keeps the level the settings service applied (no quality switch inside an arrival capture); vSync 0 and
            // the frame cap are still forced
            PerfTier307 t; int q;
            if (o.Tier == "saved") { q = QualitySettings.GetQualityLevel(); t = new PerfTier307 { key = "saved", quality = q >= 0 && q < QualitySettings.names.Length ? QualitySettings.names[q] : "?" }; }
            else
            {
                t = r.Tier(o.Tier); if (t == null) return "refused: tier " + o.Tier + " not in routes.tiers";
                q = Array.IndexOf(QualitySettings.names, t.quality); if (q < 0) return "refused: quality level '" + t.quality + "' not present (" + string.Join(",", QualitySettings.names) + ")";
            }
            session = s; routes = r; options = o; tier = t; qualityIndex = q; ui = PlaytestUiRoot.Instance;
            vitals = s.Walker.Body.GetComponent<PlayerVitals>();
            startedUtc = DateTime.UtcNow.ToString("s", CultureInfo.InvariantCulture) + "Z";
            Directory.CreateDirectory(o.Output);
            // measurement tier after arrival; re-checked every frame
            ApplyTier();
            expectedPipeline = GraphicsSettings.currentRenderPipeline;
            string pipeName = expectedPipeline != null ? expectedPipeline.name : "(built-in)";
            // the measured tier must be the one named in routes.json (PERF_DESIGN C.0: PC level + PC_RPAsset, else stop)
            if (!string.IsNullOrEmpty(t.pipeline) && pipeName != t.pipeline) { string refused = "refused: tier " + t.key + " runs pipeline " + pipeName + ", routes expect " + t.pipeline; Cleanup(); return refused; }
            enforceWindow = o.Standalone && o.Command != "arrival";
            if (enforceWindow) { Screen.SetResolution(r.renderWidth, r.renderHeight, WindowMode(r.standaloneWindowMode)); notes.Add("standalone window requested " + r.renderWidth + "x" + r.renderHeight + " " + r.standaloneWindowMode); }
            view = s.Walker.ViewCamera != null ? s.Walker.ViewCamera : Camera.main;
            if (!o.Standalone && r.editorRenderTexture && view != null)
            {
                target = new RenderTexture(r.renderWidth, r.renderHeight, 24) { name = "Perf307_Observer" }; target.Create();
                view.targetTexture = target; view.aspect = r.renderWidth / (float)r.renderHeight; viewTargetSet = true;
                notes.Add("Editor: gameplay camera " + view.name + " renders to a " + r.renderWidth + "x" + r.renderHeight + " target (MountainTrailProbe285 method); the Game view shows only the UI overlay");
            }
            arts = FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None); Array.Sort(arts, (a, b) => string.CompareOrdinal(a.name, b.name));
            grasses = FindObjectsByType<CompactGrassRenderer266>(FindObjectsSortMode.None); Array.Sort(grasses, (a, b) => string.CompareOrdinal(a.name, b.name));
            solids = FindObjectsByType<CompactNaturalSolids>(FindObjectsSortMode.None); Array.Sort(solids, (a, b) => string.CompareOrdinal(a.name, b.name));
            solidQueries = new long[solids.Length]; for (int i = 0; i < solids.Length; i++) solidQueries[i] = solids[i].Queries;
            foreach (var a in arts) if (view != null && a.Observer != view) notes.Add("INFO art renderer " + a.name + " observer is " + (a.Observer != null ? a.Observer.name : "null") + ", not the gameplay camera");
            // stations resolve with every renderer (and the natural solids it feeds) live, so full and noart use the same feet
            if (o.Command != "arrival") ResolveStations();
            if (o.Variant == "noart")
            {
                foreach (var a in arts) if (a.enabled) { a.enabled = false; disabled.Add(a); }
                foreach (var g in grasses) if (g.enabled) { g.enabled = false; disabled.Add(g); }
                notes.Add("variant noart: " + disabled.Count + " art/grass renderer(s) disabled for this Play only (natural solids release with them)");
            }
            else if (o.Variant != "full") notes.Add("WARN unknown variant " + o.Variant + " treated as full");
            BindMapTick();
            BuildColumns();
            BuildSteps();
            if (o.Command != "arrival" && stations.TrueForAll(x => !x.Ok)) { Cleanup(); return "refused: no station resolved to safe feet (" + string.Join("; ", notes) + ")"; }
            width = columns.Count;
            int maxFrames = Mathf.Clamp(r.maxFrames, 1000, 200000);
            data = new float[(long)maxFrames * width];
            lastGc0 = GC.CollectionCount(0);
            RenderPipelineManager.beginCameraRendering += OnCamera; Application.logMessageReceived += OnLog; subscribed = true;
            if (VirtualInput != null && o.Command != "arrival") { inputBegin = VirtualInput.Begin(); inputStarted = true; VirtualInput.Hold(false); }
            if (o.Command == "ab") Variant(false);
            stepIndex = 0; stepStarted = false; startedAt = Time.realtimeSinceStartupAsDouble; Status = "running";
            File.WriteAllText(Path.Combine(o.Output, "started.txt"), "Perf307 " + o.Command + " tier=" + o.Tier + " variant=" + o.Variant + " mode=" + o.Mode + (o.Toggle.Length > 0 ? " toggle=" + o.Toggle : "") +
                " started " + startedUtc + ", estimated " + EstimatedSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s, " + steps.Count + " steps, " + width + " columns");
            return null;
        }

        static FullScreenMode WindowMode(string mode)
        {
            switch (mode)
            {
                case "ExclusiveFullScreen": return FullScreenMode.ExclusiveFullScreen;
                case "MaximizedWindow": return FullScreenMode.MaximizedWindow;
                case "Windowed": return FullScreenMode.Windowed;
                default: return FullScreenMode.FullScreenWindow;
            }
        }

        void ApplyTier()
        {
            if (QualitySettings.GetQualityLevel() != qualityIndex) QualitySettings.SetQualityLevel(qualityIndex, true);
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = options.TargetFrameRate;
        }

        bool TierOk() => QualitySettings.GetQualityLevel() == qualityIndex && QualitySettings.vSyncCount == 0 &&
                         Application.targetFrameRate == options.TargetFrameRate && GraphicsSettings.currentRenderPipeline == expectedPipeline;

        void BindMapTick()
        {
            // WorldMapPresenter belongs to the minimap track: read its existing counter by name, no compile-time coupling
            var map = ui != null ? ui.Map : null;
            var getter = map != null ? map.GetType().GetProperty("LastTickMilliseconds", BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod() : null;
            if (getter != null && getter.ReturnType == typeof(double)) mapTick = (Func<double>)Delegate.CreateDelegate(typeof(Func<double>), map, getter);
            else notes.Add("INFO map tick counter unavailable (WorldMapPresenter.LastTickMilliseconds)");
        }

        // ------------------------------------------------------------------ recorders and columns
        void BuildColumns()
        {
            columns.Clear(); timeColumn.Clear(); spikeColumns.Clear();
            foreach (var c in new[] { "seg", "t_s", "interval_ms", "flags", "cpuFrame_ms", "cpuMain_ms", "presentWait_ms", "renderThread_ms", "gpu_ms", "gameCameraRenders", "sceneViewRenders", "gc0Collections", "hp01" })
            { columns.Add(c); timeColumn.Add(false); }
            // touch the markers so they are registered before discovery
            var touch = new[] { Perf307Markers.ArtCollect, Perf307Markers.ArtSubmit, Perf307Markers.Grass, Perf307Markers.SessionFocus, Perf307Markers.SessionSafeFeet,
                Perf307Markers.SessionSave, Perf307Markers.SessionCull, Perf307Markers.RigEvaluate, Perf307Markers.Gesture, Perf307Markers.FootPlacement,
                Perf307Markers.ColliderSync, Perf307Markers.NaturalSolidsQuery, Perf307Markers.NpcJob };
            if (touch.Length == 0) notes.Add("no markers");
            var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
            var byName = new Dictionary<string, ProfilerRecorderHandle>(StringComparer.Ordinal);
            var names = new List<string>();
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h); string n = d.Name;
                if (string.IsNullOrEmpty(n) || byName.ContainsKey(n)) continue;
                byName.Add(n, h); names.Add(n);
            }
            names.Sort(StringComparer.Ordinal);
            var list = new List<Rec>(); var taken = new HashSet<string>(StringComparer.Ordinal);
            void Add(string name, string prefix, bool gpu)
            {
                if (string.IsNullOrEmpty(name) || !taken.Add(prefix + name)) return;
                if (!byName.TryGetValue(name, out var h)) { missing.Add(prefix + name); return; }
                var d = ProfilerRecorderHandle.GetDescription(h);
                var opts = ProfilerRecorderOptions.Default | ProfilerRecorderOptions.StartImmediately | (gpu ? ProfilerRecorderOptions.GpuRecorder : 0);
                var rec = new ProfilerRecorder(h, 1, opts); if (!rec.IsRunning) rec.Start();
                bool isTime = d.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds;
                list.Add(new Rec { Recorder = rec, Scale = isTime ? 1e-6f : 1f, Name = prefix + name, Time = isTime });
            }
            foreach (var n in routes.coreRecorders ?? Array.Empty<string>()) Add(n, "", false);
            foreach (var n in routes.customMarkers ?? Array.Empty<string>()) Add(n, "", false);
            foreach (var n in routes.builtinMarkers ?? Array.Empty<string>()) Add(n, "", false);
            int limit = Mathf.Max(0, routes.builtinMarkerLimit), added = 0;
            foreach (var f in routes.builtinMarkerContains ?? Array.Empty<string>())
            {
                int perFilter = 0;
                foreach (var n in names)
                {
                    if (added >= limit || perFilter >= Mathf.Max(1, routes.builtinMarkerPerFilter)) break;
                    if (string.IsNullOrEmpty(f) || n.IndexOf(f, StringComparison.Ordinal) < 0 || taken.Contains(n)) continue;
                    Add(n, "", false); added++; perFilter++;
                }
            }
            if (SystemInfo.supportsGpuRecorder) foreach (var n in routes.gpuMarkers ?? Array.Empty<string>()) Add(n, "gpu:", true);
            else notes.Add("INFO GPU recorder unsupported on this platform: per-pass GPU comes from ab: toggles");
            recs = list.ToArray(); recStart = columns.Count;
            var exclude = new HashSet<string>(routes.spikeExclude ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (var r in recs) { if (r.Time && !exclude.Contains(r.Name) && !r.Name.StartsWith("gpu:", StringComparison.Ordinal)) spikeColumns.Add(columns.Count); columns.Add(r.Name + (r.Time ? "_ms" : "")); timeColumn.Add(r.Time); }
            counterStart = columns.Count;
            foreach (var a in arts)
            {
                string n = "art:" + a.name + (a.Sheet != null ? "[" + a.Sheet.name + "]" : "");
                spikeColumns.Add(columns.Count); columns.Add(n + ".cpu_ms"); timeColumn.Add(true);
                columns.Add(n + ".drawCalls"); timeColumn.Add(false); columns.Add(n + ".visible"); timeColumn.Add(false); columns.Add(n + ".triangles"); timeColumn.Add(false);
            }
            foreach (var g in grasses)
            {
                string n = "grass:" + g.name; spikeColumns.Add(columns.Count); columns.Add(n + ".cpu_ms"); timeColumn.Add(true);
                columns.Add(n + ".drawCalls"); timeColumn.Add(false); columns.Add(n + ".instances"); timeColumn.Add(false);
            }
            foreach (var n0 in solids)
            {
                string n = "solids:" + n0.name; spikeColumns.Add(columns.Count); columns.Add(n + ".ms"); timeColumn.Add(true);
                columns.Add(n + ".queries"); timeColumn.Add(false); columns.Add(n + ".active"); timeColumn.Add(false);
            }
            columns.Add("map.tick_ms"); timeColumn.Add(true);
        }

        // ------------------------------------------------------------------ stations
        void ResolveStations()
        {
            stations.Clear();
            foreach (var spec in routes.stations)
            {
                var r = new Resolved { Spec = spec };
                try { r.Ok = Resolve(spec, out r.Feet, out r.BaseYaw, out r.How); }
                catch (Exception e) { r.Ok = false; r.How = "error " + e.Message; }
                notes.Add((r.Ok ? "station " : "WARN station skipped ") + spec.id + ": " + r.How + (r.Ok ? " feet " + V(r.Feet) + " base yaw " + r.BaseYaw.ToString("F1", CultureInfo.InvariantCulture) : ""));
                stations.Add(r);
            }
        }

        bool Resolve(PerfStation307 s, out Vector3 feet, out float baseYaw, out string how)
        {
            feet = default; baseYaw = s.baseYaw; how = s.source;
            var content = session.Content; Vector3 candidate; bool faceTarget = false; Vector3 face = default;
            switch (s.source)
            {
                case "start": candidate = content.StartFeet; baseYaw = content.StartYaw; break;
                case "checkpoint":
                {
                    var c = Array.Find(content.Checkpoints, x => x != null && x.Id == s.sourceId);
                    if (c == null) { how = "checkpoint " + s.sourceId + " missing"; return false; }
                    candidate = c.Feet; baseYaw = c.Yaw; break;
                }
                case "point":
                {
                    var p = Array.Find(content.Points, x => x != null && x.Id == s.sourceId);
                    if (p == null) { how = "point " + s.sourceId + " missing"; return false; }
                    candidate = p.Position; break;
                }
                case "forest305-edge":
                {
                    CompactRebuildArtRenderer source = null;
                    foreach (var a in arts) if (a != null && (a.name == s.sourceId || a.Sheet != null && a.Sheet.name == s.sourceId)) { source = a; break; }
                    if (source == null || source.Sheet == null || source.Sheet.FixedPlacements == null || source.Sheet.FixedPlacements.Length == 0) { how = "renderer " + s.sourceId + " missing"; return false; }
                    float best = float.MaxValue; Vector3 at = default;
                    foreach (var fp in source.Sheet.FixedPlacements)
                    {
                        float d = new Vector2(fp.Position.x - s.near.x, fp.Position.z - s.near.y).sqrMagnitude;
                        if (d < best) { best = d; at = fp.Position; }
                    }
                    var dir = new Vector3(s.near.x - at.x, 0, s.near.y - at.z); dir = dir.sqrMagnitude > .01f ? dir.normalized : Vector3.forward;
                    candidate = at + dir * s.inset; face = at; faceTarget = true;
                    how = "forest305-edge nearest placement " + V(at) + " inset " + s.inset.ToString("F0", CultureInfo.InvariantCulture) + " m";
                    break;
                }
                case "fixed": candidate = s.feet; break;
                default: how = "unknown source " + s.source; return false;
            }
            if (!Ground(candidate, s.search, out feet, out string ground)) { how += "; no safe feet within " + s.search.ToString("F0", CultureInfo.InvariantCulture) + " m of " + V(candidate); return false; }
            how += "; " + ground;
            if (faceTarget) { var d = face - feet; baseYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }
            return true;
        }

        bool Ground(Vector3 candidate, float search, out Vector3 feet, out string how)
        {
            if (session.TrySafeFeet(candidate, out feet)) { how = "exact"; return true; }
            if (Vertical(candidate, out feet)) { how = "vertical probe"; return true; }
            for (float r = 4f; r <= search + .01f; r += 4f)
                for (int k = 0; k < 12; k++)
                {
                    var p = candidate + Quaternion.Euler(0, k * 30f, 0) * Vector3.forward * r;
                    if (session.TrySafeFeet(p, out feet) || Vertical(p, out feet)) { how = "ring " + r.ToString("F0", CultureInfo.InvariantCulture) + " m bearing " + (k * 30) + " deg"; return true; }
                }
            how = "none"; feet = default; return false;
        }

        bool Vertical(Vector3 p, out Vector3 feet)
        {
            feet = default;
            var hits = Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 90f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => Mathf.Abs(a.point.y - p.y).CompareTo(Mathf.Abs(b.point.y - p.y)));
            foreach (var h in hits) if (session.TrySafeFeet(h.point + Vector3.up * .05f, out feet)) return true;
            return false;
        }

        // ------------------------------------------------------------------ steps
        void AddStep(int kind, float seconds, bool record, bool walk, string label, int station, float yaw, bool b = false, string segKind = "", int lap = 0)
        {
            var st = new Step { Kind = kind, Seconds = seconds, Record = record, Walk = walk, Label = label, Station = station, Yaw = yaw, B = b };
            if (record)
            {
                string key = label; int slash = label.IndexOf('/'); if (lap > 0 && slash >= 0) key = label.Substring(slash + 1);
                segments.Add(new Segment { Label = label, Key = key, Kind = segKind, Lap = lap, Yaw = yaw, Station = station >= 0 ? stations[station].Spec.id : "" });
                st.Segment = segments.Count - 1;
            }
            if (kind == Hold) EstimatedSeconds += seconds;
            steps.Add(st);
        }

        float Yaw(Resolved r, float value) => Mathf.Repeat(r.Spec.yawRelative ? r.BaseYaw + value : value, 360f);

        void BuildSteps()
        {
            steps.Clear(); segments.Clear(); EstimatedSeconds = 0;
            if (options.Command == "arrival") { AddStep(Hold, routes.arrivalSeconds, true, false, "arrival", -1, 0, false, "arrival"); return; }
            bool ab = options.Command == "ab"; bool warmed = false;
            for (int lap = 1; lap <= Mathf.Max(1, routes.laps); lap++)
                for (int i = 0; i < stations.Count; i++)
                {
                    var r = stations[i]; if (!r.Ok) continue;
                    float[] poses = r.Spec.poseYaws != null && r.Spec.poseYaws.Length > 0 ? r.Spec.poseYaws : new[] { 0f };
                    string id = "L" + lap + "/" + r.Spec.id;
                    AddStep(Teleport, 0, false, false, id + "/teleport", i, Yaw(r, poses[0]));
                    if (!warmed) { AddStep(Hold, routes.warmupSeconds, false, false, "warmup (not recorded)", i, 0); warmed = true; }
                    AddStep(Hold, routes.settleTeleportSeconds, false, false, id + "/settle", i, 0);
                    for (int p = 0; p < poses.Length; p++)
                    {
                        float yaw = Yaw(r, poses[p]); string pose = id + "/p" + p + "@" + yaw.ToString("F0", CultureInfo.InvariantCulture);
                        if (p > 0) { AddStep(Teleport, 0, false, false, pose + "/yaw", i, yaw); AddStep(Hold, routes.settlePoseSeconds, false, false, pose + "/settle", i, 0); }
                        if (!ab) { AddStep(Hold, routes.sampleSeconds, true, false, pose, i, yaw, false, "pose", lap); continue; }
                        AddStep(SetVariant, 0, false, false, pose + "/A-state", i, yaw, false);
                        AddStep(Hold, routes.sampleSeconds, true, false, pose + "/A", i, yaw, false, "A", lap);
                        AddStep(SetVariant, 0, false, false, pose + "/B-state", i, yaw, true);
                        AddStep(Hold, routes.abSettleSeconds, false, false, pose + "/B-settle", i, 0);
                        AddStep(Hold, routes.sampleSeconds, true, false, pose + "/B", i, yaw, true, "B", lap);
                        AddStep(SetVariant, 0, false, false, pose + "/A-restore", i, yaw, false);
                    }
                    if (ab || !r.Spec.walk || routes.walkSeconds <= 0) continue;
                    float walkYaw = Yaw(r, r.Spec.walkYaw); string walk = id + "/walk@" + walkYaw.ToString("F0", CultureInfo.InvariantCulture);
                    AddStep(Teleport, 0, false, false, walk + "/yaw", i, walkYaw);
                    AddStep(Hold, routes.settlePoseSeconds, false, false, walk + "/settle", i, 0);
                    AddStep(Hold, routes.walkSeconds, true, true, walk, i, walkYaw, false, "walk", lap);
                }
        }

        // ------------------------------------------------------------------ frame loop
        void Update()
        {
            if (boot >= 0) { BootTick(); return; }
            if (Status != "running") return;
            // #308: a new journey opens the #306 tutorial card, which blocks gameplay input — every frame then carried FBlocked and
            // no frame was valid (2026-10-03: 0/29982). The measurement route is not the tutorial: close the card.
            if (ui != null && ui.SkipTutorial306ForHarness()) notes.Add("tutorial card (#306) closed for the measurement");
            recordNow = false;
            try
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (TierOk()) { tierOkNow = true; inDrift = false; }
                else
                {
                    tierOkNow = false; driftFrames++;
                    if (!inDrift) { inDrift = true; driftEvents++; }
                    ApplyTier();
                    if (driftEvents > routes.maxTierDriftEvents) { Finish("FAIL tier override drifted " + driftEvents + " times (UserSettingsService or another tool re-applied settings)"); return; }
                }
                windowOkNow = !enforceWindow || now - startedAt < 1.5 || Screen.width == routes.renderWidth && Screen.height == routes.renderHeight;
                if (!windowOkNow) windowFrames++;
                if (stepIndex >= steps.Count) { Finish("COMPLETE"); return; }
                var st = steps[stepIndex];
                if (!stepStarted)
                {
                    stepStarted = true; stepAt = now;
                    if (st.Kind == Teleport)
                    {
                        var r = stations[st.Station]; session.Teleport(r.Feet, st.Yaw);
                        if (inputStarted) VirtualInput.Hold(false);
                        NextStep(); return;
                    }
                    if (st.Kind == SetVariant) { Variant?.Invoke(st.B); NextStep(); return; }
                    if (st.Segment >= 0) BeginSegment(segments[st.Segment]);
                }
                if (inputStarted) VirtualInput.Hold(st.Walk);
                if (st.Record) { recordNow = true; recordSegment = st.Segment; walkNow = st.Walk; }
                if (now - stepAt >= st.Seconds)
                {
                    if (st.Segment >= 0) pendingEnd = st.Segment;
                    NextStep();
                }
            }
            catch (Exception e) { Finish("FAIL " + e.GetType().Name + ": " + e.Message); }
        }

        int pendingEnd = -1;
        void NextStep() { stepIndex++; stepStarted = false; }

        void BeginSegment(Segment s)
        {
            s.First = frameCount; s.Gc0Start = GC.CollectionCount(0); s.HpStart = vitals != null ? vitals.Hp01 : -1;
        }

        void EndSegment(Segment s)
        {
            s.End = frameCount; s.Gc0End = GC.CollectionCount(0); s.HpEnd = vitals != null ? vitals.Hp01 : -1;
        }

        void LateUpdate()
        {
            if (Status != "running") return;
            FrameTimingManager.CaptureFrameTimings();
            int gc0 = GC.CollectionCount(0);
            if (recordNow && !capHit)
            {
                if ((long)(frameCount + 1) * width > data.Length) { capHit = true; notes.Add("WARN frame cap reached at " + frameCount + " frames; later frames not recorded"); }
                else Sample(gc0);
            }
            if (pendingEnd >= 0) { EndSegment(segments[pendingEnd]); pendingEnd = -1; }
            gameRenders = sceneRenders = otherRenders = 0; lastGc0 = gc0;
            for (int i = 0; i < solids.Length; i++) if (solids[i] != null) solidQueries[i] = solids[i].Queries;
        }

        void Sample(int gc0)
        {
            long o = (long)frameCount * width; var a = data;
            bool focused = FocusProbe != null ? FocusProbe() : Application.isFocused;
            bool blocked = session != null && session.GameplayInputBlocked, loading = ui != null && ui.LoadingInProgress;
            int flags = (focused ? FFocused : 0) | (tierOkNow ? FTier : 0) | (blocked ? FBlocked : 0) | (loading ? FLoading : 0) | (windowOkNow ? FWindow : 0) |
                        (walkNow ? FWalk : 0);
            a[o] = recordSegment; a[o + 1] = (float)(Time.realtimeSinceStartupAsDouble - startedAt); a[o + 2] = Time.unscaledDeltaTime * 1000f; a[o + 3] = flags;
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {
                a[o + 4] = Ms(timing[0].cpuFrameTime); a[o + 5] = Ms(timing[0].cpuMainThreadFrameTime);
                // a present wait of exactly 0 is a real value (not GPU/vSync bound), unlike the other timings where 0 = unavailable
                a[o + 6] = a[o + 5] > 0 ? MsOrZero(timing[0].cpuMainThreadPresentWaitTime) : float.NaN;
                a[o + 7] = Ms(timing[0].cpuRenderThreadFrameTime); a[o + 8] = Ms(timing[0].gpuFrameTime);
            }
            else for (int k = 4; k <= 8; k++) a[o + k] = float.NaN;
            a[o + 9] = gameRenders; a[o + 10] = sceneRenders; a[o + 11] = gc0 - lastGc0; a[o + 12] = vitals != null ? vitals.Hp01 : float.NaN;
            long c = o + recStart;
            for (int i = 0; i < recs.Length; i++, c++) a[c] = recs[i].Recorder.Valid ? (float)(recs[i].Recorder.LastValue * (double)recs[i].Scale) : float.NaN;
            for (int i = 0; i < arts.Length; i++)
            {
                var r = arts[i]; bool live = r != null && r.isActiveAndEnabled;
                a[c++] = live ? (float)r.LastCpuMs : float.NaN; a[c++] = live ? r.DrawCalls : float.NaN; a[c++] = live ? r.VisibleInstances : float.NaN; a[c++] = live ? r.SubmittedTriangles : float.NaN;
            }
            for (int i = 0; i < grasses.Length; i++)
            {
                var g = grasses[i]; bool live = g != null && g.isActiveAndEnabled;
                a[c++] = live ? (float)g.LastCpuMs : float.NaN; a[c++] = live ? g.DrawCalls : float.NaN; a[c++] = live ? g.SubmittedInstances : float.NaN;
            }
            for (int i = 0; i < solids.Length; i++)
            {
                var n = solids[i]; bool live = n != null;
                a[c++] = live ? (float)n.LastMs : float.NaN; a[c++] = live ? n.Queries - solidQueries[i] : float.NaN; a[c++] = live ? n.Active : float.NaN;
            }
            a[c] = mapTick != null ? (float)mapTick() : float.NaN;
            frameCount++;
        }

        static float Ms(double v) => v > 0 && !double.IsNaN(v) ? (float)v : float.NaN;
        static float MsOrZero(double v) => v >= 0 && !double.IsNaN(v) && !double.IsInfinity(v) ? (float)v : float.NaN;

        void OnCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null) return;
            if (camera.cameraType == CameraType.Game) gameRenders++;
            else if (camera.cameraType == CameraType.SceneView) sceneRenders++;
            else otherRenders++;
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception) { exceptions++; if (logs.Count < 40) logs.Add("EXC " + message); }
            else if (type == LogType.Error || type == LogType.Assert) { errors++; if (logs.Count < 40) logs.Add("ERR " + message); }
        }

        // ------------------------------------------------------------------ finish
        /// <summary>Stops the route now (abort) and writes whatever was recorded.</summary>
        public void Abort(string why) { if (Status == "running") Finish("ABORTED " + why); }

        void Cleanup()
        {
            if (subscribed) { RenderPipelineManager.beginCameraRendering -= OnCamera; Application.logMessageReceived -= OnLog; subscribed = false; }
            if (options != null && options.Command == "ab" && Variant != null) { try { Variant(false); } catch (Exception e) { notes.Add("WARN variant restore: " + e.Message); } }
            if (inputStarted) { try { inputEnd = VirtualInput.End(out bool ok); if (!ok) notes.Add("WARN " + inputEnd); } catch (Exception e) { notes.Add("WARN input end: " + e.Message); } inputStarted = false; }
            if (viewTargetSet) { if (view != null) { view.targetTexture = null; view.ResetAspect(); } viewTargetSet = false; }
            if (target != null) { target.Release(); Destroy(target); target = null; }
            foreach (var b in disabled) if (b != null) b.enabled = true;
            disabled.Clear();
            for (int i = 0; i < recs.Length; i++) recs[i].Recorder.Dispose();
            recs = Array.Empty<Rec>();
        }

        void Finish(string status)
        {
            if (Status != "running") return;
            Status = "finishing";
            if (pendingEnd >= 0) { EndSegment(segments[pendingEnd]); pendingEnd = -1; }
            foreach (var s in segments) if (s.First >= 0 && s.End < 0) EndSegment(s);
            Cleanup();
            try { Result = Write(status); }
            catch (Exception e) { Result = "FAIL writing output: " + e; try { File.WriteAllText(Path.Combine(options.Output, "error.txt"), Result); } catch { } }
            Status = Result.StartsWith("PASS", StringComparison.Ordinal) ? "PASS" : Result.StartsWith("INVALID", StringComparison.Ordinal) ? "INVALID" : "FAIL";
            Done = true;
            if (options.Standalone) Application.Quit(Status == "PASS" ? 0 : 2);
        }

        void OnDestroy()
        {
            if (Status == "running") Finish("ABORTED runner destroyed (Play stopped)");
            else Cleanup();
        }

        // ------------------------------------------------------------------ report
        [Serializable] public sealed class SegmentStats307
        {
            public string label = "", key = "", kind = "", station = ""; public int lap; public float yaw;
            public int frames, valid, focused, blocked, tierBad, gcCollections, spikes; public float focusShare, hpStart, hpEnd;
            public float intervalMedian = -1, intervalP95 = -1, intervalP99 = -1, intervalMax = -1, intervalMean = -1, overBudgetShare = -1;
            public float busyMedian = -1, busyP95 = -1, cpuMainMedian = -1, presentWaitMedian = -1, renderMedian = -1, gpuMedian = -1, gpuP95 = -1;
            public float[] means = Array.Empty<float>();
        }
        [Serializable] public sealed class Repeat307 { public string key = "", metric = ""; public float busyLap1, busyLap2, busyDelta, gpuLap1, gpuLap2, gpuDelta, intervalLap1, intervalLap2, intervalDelta; public bool compared, within; }
        [Serializable] public sealed class Ab307 { public string key = ""; public int lap; public float busyA, busyB, busyDelta, gpuA, gpuB, gpuDelta, intervalA, intervalB, noiseBusy, noiseGpu; public bool busyEffect, gpuEffect; }
        [Serializable] public sealed class Spike307 { public int frame; public string segment = ""; public float intervalMs, medianMs; public string[] top = Array.Empty<string>(); }
        [Serializable] public sealed class Summary307
        {
            public string status = "", command = "", tier = "", variant = "", mode = "", toggle = "", runId = "", output = "", scope = "", startedUtc = "", finishedUtc = "";
            public string unity = "", device = "", cpu = "", gpuDevice = "", graphicsApi = "", os = "", quality = "", pipeline = "", repeatability = "", inputBegin = "", inputEnd = "";
            public bool standalone, frameTimingEnabled, gpuRecorderSupported, frameCapHit;
            public int screenWidth, screenHeight, frames, validFrames, focusedFrames, tierDriftEvents, tierDriftFrames, windowDriftFrames, exceptions, errors, spikeCount, vSyncCount, targetFrameRate;
            public float focusShare, estimatedSeconds;
            public List<string> notes = new List<string>(), meta = new List<string>(), missingRecorders = new List<string>(), log = new List<string>();
            public string[] columns = Array.Empty<string>();
            public List<SegmentStats307> segments = new List<SegmentStats307>();
            public List<Repeat307> repeat = new List<Repeat307>();
            public List<Ab307> ab = new List<Ab307>();
            public List<Spike307> spikes = new List<Spike307>();
        }

        bool Valid(int flags) => (flags & FFocused) != 0 && (flags & FTier) != 0 && (flags & FWindow) != 0 && (flags & (FBlocked | FLoading)) == 0;
        float At(int frame, int column) => data[(long)frame * width + column];

        static float Percentile(List<float> sorted, double q) => sorted.Count == 0 ? -1 : sorted[Math.Min(sorted.Count - 1, (int)Math.Floor((sorted.Count - 1) * q + .5))];

        List<float> Column(Segment s, int column, bool validOnly, bool busy = false)
        {
            var list = new List<float>();
            if (s.First < 0) return list;
            for (int f = s.First; f < s.End; f++)
            {
                if (validOnly && !Valid((int)At(f, 3))) continue;
                float v = busy ? At(f, 5) - At(f, 6) : At(f, column);
                if (!float.IsNaN(v) && !float.IsInfinity(v)) list.Add(v);
            }
            list.Sort(); return list;
        }

        string Write(string status)
        {
            var sum = new Summary307
            {
                command = options.Command, tier = options.Tier, variant = options.Variant, mode = options.Mode, toggle = options.Toggle, runId = options.RunId, output = options.Output,
                standalone = options.Standalone, startedUtc = startedUtc, finishedUtc = DateTime.UtcNow.ToString("s", CultureInfo.InvariantCulture) + "Z",
                unity = Application.unityVersion, device = SystemInfo.deviceModel, cpu = SystemInfo.processorType, gpuDevice = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), os = SystemInfo.operatingSystem,
                quality = qualityIndex >= 0 && qualityIndex < QualitySettings.names.Length ? QualitySettings.names[qualityIndex] : "?",
                pipeline = expectedPipeline != null ? expectedPipeline.name : "(built-in)",
                frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(), gpuRecorderSupported = SystemInfo.supportsGpuRecorder, frameCapHit = capHit,
                screenWidth = Screen.width, screenHeight = Screen.height, frames = frameCount, tierDriftEvents = driftEvents, tierDriftFrames = driftFrames, windowDriftFrames = windowFrames,
                exceptions = exceptions, errors = errors, vSyncCount = QualitySettings.vSyncCount, targetFrameRate = options.TargetFrameRate, estimatedSeconds = EstimatedSeconds,
                inputBegin = inputBegin, inputEnd = inputEnd, columns = columns.ToArray(),
                scope = "tier " + (tier != null ? tier.key + " -> " + tier.quality : "?") + "; " + (options.Standalone ? "Standalone player" : "Unity Editor Play (includes Editor overhead; reference only)") + ", " + options.Command + ", tier " + options.Tier +
                        ", variant " + options.Variant + ", " + (options.Uncapped ? "uncapped (vSync 0, targetFrameRate -1)" : "capped 120 (vSync 0, targetFrameRate 120)") +
                        ". Automated teleport + fixed-yaw poses + virtual-keyboard walk, not manual play. Recorder values are the previous completed frame; FrameTiming is the latest available (GPU lags 1-4 frames). Frames count only when focused, on the forced tier, not input-blocked and not loading."
            };
            sum.meta.AddRange(options.Meta); sum.missingRecorders.AddRange(missing); sum.log.AddRange(logs);
            // validity
            int valid = 0, focusedCount = 0;
            for (int f = 0; f < frameCount; f++) { int fl = (int)At(f, 3); if ((fl & FFocused) != 0) focusedCount++; if (Valid(fl)) valid++; }
            sum.validFrames = valid; sum.focusedFrames = focusedCount; sum.focusShare = frameCount > 0 ? focusedCount / (float)frameCount : 0;
            // segments
            int means = columns.Count;
            var stats = new List<SegmentStats307>();
            var spikeRows = new List<Spike307>();
            var topBuffer = new List<KeyValuePair<float, int>>();
            foreach (var s in segments)
            {
                var st = new SegmentStats307 { label = s.Label, key = s.Key, kind = s.Kind, station = s.Station, lap = s.Lap, yaw = s.Yaw, hpStart = s.HpStart, hpEnd = s.HpEnd, gcCollections = s.Gc0End - s.Gc0Start };
                if (s.First >= 0)
                {
                    st.frames = s.End - s.First;
                    for (int f = s.First; f < s.End; f++) { int fl = (int)At(f, 3); if (Valid(fl)) st.valid++; if ((fl & FFocused) != 0) st.focused++; if ((fl & FBlocked) != 0) st.blocked++; if ((fl & FTier) == 0) st.tierBad++; }
                    st.focusShare = st.frames > 0 ? st.focused / (float)st.frames : 0;
                    var interval = Column(s, 2, true);
                    if (interval.Count > 0)
                    {
                        double total = 0; int over = 0; foreach (var v in interval) { total += v; if (v > routes.budgetMs) over++; }
                        st.intervalMedian = Percentile(interval, .5); st.intervalP95 = Percentile(interval, .95); st.intervalP99 = Percentile(interval, .99);
                        st.intervalMax = interval[interval.Count - 1]; st.intervalMean = (float)(total / interval.Count); st.overBudgetShare = over / (float)interval.Count;
                    }
                    var busy = Column(s, 0, true, true); st.busyMedian = Percentile(busy, .5); st.busyP95 = Percentile(busy, .95);
                    st.cpuMainMedian = Percentile(Column(s, 5, true), .5); st.presentWaitMedian = Percentile(Column(s, 6, true), .5);
                    st.renderMedian = Percentile(Column(s, 7, true), .5);
                    var gpu = Column(s, 8, true); st.gpuMedian = Percentile(gpu, .5); st.gpuP95 = Percentile(gpu, .95);
                    st.means = new float[means];
                    for (int c = 0; c < means; c++)
                    {
                        double total = 0; int n = 0;
                        for (int f = s.First; f < s.End; f++) { if (!Valid((int)At(f, 3))) continue; float v = At(f, c); if (float.IsNaN(v)) continue; total += v; n++; }
                        st.means[c] = n > 0 ? (float)(total / n) : float.NaN;
                    }
                    // spikes: interval above spikeFactor x the segment median
                    if (st.intervalMedian > 0)
                        for (int f = s.First; f < s.End; f++)
                        {
                            if (!Valid((int)At(f, 3))) continue; float v = At(f, 2);
                            if (v <= routes.spikeFactor * st.intervalMedian) continue;
                            st.spikes++;
                            if (spikeRows.Count < Mathf.Max(0, routes.spikeLimit)) spikeRows.Add(new Spike307 { frame = f, segment = s.Label, intervalMs = v, medianMs = st.intervalMedian, top = Top(f, topBuffer) });
                        }
                }
                stats.Add(st);
            }
            sum.segments = stats; sum.spikes = spikeRows; sum.spikeCount = 0; foreach (var st in stats) sum.spikeCount += st.spikes;
            // repeatability: lap 1 vs lap 2 per key
            // main-thread busy (main - present wait) is the design metric; without FrameTiming the frame interval stands in and the
            // row says so; a pair with neither metric is listed but never counted as repeatable
            int unstable = 0, compared = 0, pairs = 0, fallback = 0;
            foreach (var a in stats)
            {
                if (a.lap != 1) continue;
                var b = stats.Find(x => x.lap == 2 && x.key == a.key);
                if (b == null) continue;
                pairs++;
                var row = new Repeat307 { key = a.key, busyLap1 = a.busyMedian, busyLap2 = b.busyMedian, gpuLap1 = a.gpuMedian, gpuLap2 = b.gpuMedian, intervalLap1 = a.intervalMedian, intervalLap2 = b.intervalMedian };
                row.busyDelta = Rel(a.busyMedian, b.busyMedian); row.gpuDelta = Rel(a.gpuMedian, b.gpuMedian); row.intervalDelta = Rel(a.intervalMedian, b.intervalMedian);
                float primary = row.busyDelta >= 0 ? row.busyDelta : row.intervalDelta;
                row.metric = row.busyDelta >= 0 ? "busy" : row.intervalDelta >= 0 ? "interval (no FrameTiming busy)" : "none";
                row.compared = primary >= 0;
                row.within = row.compared && primary <= routes.repeatTolerance && (row.gpuDelta < 0 || row.gpuDelta <= routes.repeatTolerance);
                sum.repeat.Add(row);
                if (!row.compared) continue;
                compared++; if (row.busyDelta < 0) fallback++; if (!row.within) unstable++;
            }
            string tolerance = (routes.repeatTolerance * 100).ToString("F0", CultureInfo.InvariantCulture);
            string basis = fallback > 0 ? " (" + fallback + " on frame interval: FrameTiming busy unavailable)" : "";
            sum.repeatability = pairs == 0 ? "not compared (one lap)" : compared == 0 ? "not compared (" + pairs + " pair(s) without busy/interval data)" :
                unstable == 0 ? "REPEATABLE: " + compared + "/" + pairs + " segment(s) within +/-" + tolerance + "%" + basis :
                "UNSTABLE: " + unstable + "/" + compared + " segment(s) outside +/-" + tolerance + "%" + basis + " (re-measure once; if still unstable report the spread and accept only A/B effects >= 2x noise)";
            // ab: B - A per pose, noise = lap1/lap2 spread of A
            if (options.Command == "ab")
                foreach (var a in stats)
                {
                    if (a.kind != "A") continue;
                    string bkey = a.key.Substring(0, a.key.Length - 1) + "B";
                    var b = stats.Find(x => x.kind == "B" && x.lap == a.lap && x.key == bkey);
                    if (b == null) continue;
                    var other = stats.Find(x => x.kind == "A" && x.lap != a.lap && x.key == a.key);
                    var row = new Ab307 { key = a.key.Substring(0, a.key.Length - 2), lap = a.lap, busyA = a.busyMedian, busyB = b.busyMedian, gpuA = a.gpuMedian, gpuB = b.gpuMedian, intervalA = a.intervalMedian, intervalB = b.intervalMedian };
                    row.busyDelta = b.busyMedian - a.busyMedian; row.gpuDelta = b.gpuMedian - a.gpuMedian;
                    row.noiseBusy = other != null ? Mathf.Abs(other.busyMedian - a.busyMedian) : -1; row.noiseGpu = other != null ? Mathf.Abs(other.gpuMedian - a.gpuMedian) : -1;
                    row.busyEffect = row.noiseBusy >= 0 && Mathf.Abs(row.busyDelta) >= routes.abNoiseFactor * row.noiseBusy;
                    row.gpuEffect = row.noiseGpu >= 0 && Mathf.Abs(row.gpuDelta) >= routes.abNoiseFactor * row.noiseGpu;
                    sum.ab.Add(row);
                }
            sum.notes.AddRange(notes);
            // verdict: PASS = route complete and valid (not a performance pass: the budget is judged from the numbers)
            string verdict;
            if (!status.StartsWith("COMPLETE", StringComparison.Ordinal)) verdict = status.StartsWith("ABORTED", StringComparison.Ordinal) ? "FAIL " + status : status;
            else if (frameCount == 0) verdict = "FAIL no frames recorded";
            else if (sum.focusShare < routes.focusMinimum) verdict = "INVALID focus " + (sum.focusShare * 100).ToString("F1", CultureInfo.InvariantCulture) + "% < " + (routes.focusMinimum * 100).ToString("F0", CultureInfo.InvariantCulture) + "% (keep the Unity window in front)";
            else if (driftEvents > 0) verdict = "INVALID tier override drifted " + driftEvents + " time(s)";
            else verdict = "PASS route complete, focus " + (sum.focusShare * 100).ToString("F1", CultureInfo.InvariantCulture) + "%, " + valid + "/" + frameCount + " valid frames; " + sum.repeatability;
            sum.status = verdict;
            string folder = options.Output; Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "summary.json"), JsonUtility.ToJson(sum, true));
            WriteFrames(Path.Combine(folder, "frames.csv"));
            WriteSegments(Path.Combine(folder, "segments.csv"), stats);
            WriteSpikes(Path.Combine(folder, "spikes.csv"), spikeRows);
            if (options.Command == "arrival") WriteTop(Path.Combine(folder, "top-markers.csv"), topBuffer);
            return verdict;
        }

        static float Rel(float a, float b) => a > 0 && b > 0 ? Mathf.Abs(a - b) / ((a + b) * .5f) : -1;

        string[] Top(int frame, List<KeyValuePair<float, int>> buffer)
        {
            buffer.Clear();
            foreach (int c in spikeColumns) { float v = At(frame, c); if (!float.IsNaN(v) && v > 0) buffer.Add(new KeyValuePair<float, int>(v, c)); }
            buffer.Sort((x, y) => y.Key.CompareTo(x.Key));
            int n = Math.Min(Mathf.Max(1, routes.spikeTopMarkers), buffer.Count); var top = new string[n];
            for (int i = 0; i < n; i++) top[i] = columns[buffer[i].Value] + "=" + buffer[i].Key.ToString("F3", CultureInfo.InvariantCulture);
            return top;
        }

        static string Csv(string s) => s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        static string F(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "" : v.ToString("0.####", CultureInfo.InvariantCulture);

        void WriteFrames(string path)
        {
            using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                var header = new StringBuilder(); for (int c = 0; c < columns.Count; c++) { if (c > 0) header.Append(','); header.Append(Csv(columns[c])); }
                w.WriteLine(header.ToString());
                var line = new StringBuilder();
                for (int f = 0; f < frameCount; f++)
                {
                    line.Clear();
                    for (int c = 0; c < width; c++) { if (c > 0) line.Append(','); line.Append(F(At(f, c))); }
                    w.WriteLine(line.ToString());
                }
            }
        }

        void WriteSegments(string path, List<SegmentStats307> stats)
        {
            using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                var sb = new StringBuilder("label,lap,kind,station,yaw,frames,valid,focusShare,blocked,tierBad,gc0,hpStart,hpEnd,spikes,intervalMedian,intervalP95,intervalP99,intervalMax,overBudgetShare,busyMedian,busyP95,cpuMainMedian,presentWaitMedian,renderMedian,gpuMedian,gpuP95");
                for (int c = 0; c < columns.Count; c++) sb.Append(",mean:").Append(Csv(columns[c]));
                w.WriteLine(sb.ToString());
                foreach (var s in stats)
                {
                    sb.Clear();
                    sb.Append(Csv(s.label)).Append(',').Append(s.lap).Append(',').Append(s.kind).Append(',').Append(Csv(s.station)).Append(',').Append(F(s.yaw)).Append(',')
                      .Append(s.frames).Append(',').Append(s.valid).Append(',').Append(F(s.focusShare)).Append(',').Append(s.blocked).Append(',').Append(s.tierBad).Append(',')
                      .Append(s.gcCollections).Append(',').Append(F(s.hpStart)).Append(',').Append(F(s.hpEnd)).Append(',').Append(s.spikes).Append(',')
                      .Append(F(s.intervalMedian)).Append(',').Append(F(s.intervalP95)).Append(',').Append(F(s.intervalP99)).Append(',').Append(F(s.intervalMax)).Append(',').Append(F(s.overBudgetShare)).Append(',')
                      .Append(F(s.busyMedian)).Append(',').Append(F(s.busyP95)).Append(',').Append(F(s.cpuMainMedian)).Append(',').Append(F(s.presentWaitMedian)).Append(',')
                      .Append(F(s.renderMedian)).Append(',').Append(F(s.gpuMedian)).Append(',').Append(F(s.gpuP95));
                    for (int c = 0; c < columns.Count; c++) sb.Append(',').Append(c < s.means.Length ? F(s.means[c]) : "");
                    w.WriteLine(sb.ToString());
                }
            }
        }

        static void WriteSpikes(string path, List<Spike307> rows)
        {
            using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                w.WriteLine("frame,segment,interval_ms,segment_median_ms,top_markers");
                foreach (var r in rows) w.WriteLine(r.frame + "," + Csv(r.segment) + "," + F(r.intervalMs) + "," + F(r.medianMs) + "," + Csv(string.Join(" | ", r.top)));
            }
        }

        void WriteTop(string path, List<KeyValuePair<float, int>> buffer)
        {
            using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                w.WriteLine("frame,t_s,interval_ms,cpuMain_ms,gpu_ms,gc0Collections,top_markers");
                for (int f = 0; f < frameCount; f++)
                    w.WriteLine(f + "," + F(At(f, 1)) + "," + F(At(f, 2)) + "," + F(At(f, 5)) + "," + F(At(f, 8)) + "," + F(At(f, 11)) + "," + Csv(string.Join(" | ", Top(f, buffer))));
            }
        }

        static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:F2}, {1:F2}, {2:F2})", v.x, v.y, v.z);
    }

    /// <summary>Player-side virtual keyboard for -perf307 (C2StandaloneBenchmark method): one added keyboard + mouse, the motor's and
    /// drawing controller's action assets scoped to them, IgnoreFocus while it runs; everything restored in End. Never used in the
    /// Editor (VirtualInput303 is used there) and never created during ordinary play.</summary>
    public sealed class PerfKeyboard307 : IPerfInput307
    {
        readonly WorldMacroPlaytestSession session;
        Keyboard keyboard; Mouse mouse; bool walk, active;
        readonly KeyboardState walkState = new KeyboardState(Key.W), idleState = new KeyboardState();
        readonly List<InputActionAsset> assets = new List<InputActionAsset>();
        readonly List<InputDevice[]> prior = new List<InputDevice[]>();
        InputSettings.BackgroundBehavior priorBackground;

        public PerfKeyboard307(WorldMacroPlaytestSession session) { this.session = session; }

        public string Begin()
        {
            priorBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard = InputSystem.AddDevice<Keyboard>("Perf307Keyboard"); mouse = InputSystem.AddDevice<Mouse>("Perf307Mouse");
            foreach (var owner in new object[] { session.Walker.Motor, session.Walker.Drawing })
            {
                var field = owner != null ? owner.GetType().GetField("_actions", BindingFlags.Instance | BindingFlags.NonPublic) : null;
                if (field?.GetValue(owner) is InputActionAsset asset && !assets.Contains(asset))
                {
                    assets.Add(asset); prior.Add(asset.devices.HasValue ? asset.devices.Value.ToArray() : null);
                    asset.devices = new InputDevice[] { keyboard, mouse };
                }
            }
            InputSystem.onBeforeUpdate -= Feed; InputSystem.onBeforeUpdate += Feed; active = true;
            return "player virtual keyboard " + keyboard.deviceId + ", " + assets.Count + " action asset(s) scoped";
        }

        public void Hold(bool value) { walk = value; }

        void Feed()
        {
            if (!active || keyboard == null || !keyboard.added) return;
            var type = InputState.currentUpdateType;
            if (type != InputUpdateType.Dynamic && type != InputUpdateType.Fixed) return;
            InputSystem.QueueStateEvent(keyboard, walk ? walkState : idleState);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(Screen.width * .5f, Screen.height * .5f) });
            keyboard.MakeCurrent(); mouse.MakeCurrent();
        }

        public string End(out bool ok)
        {
            ok = true; InputSystem.onBeforeUpdate -= Feed; active = false;
            for (int i = 0; i < assets.Count; i++) if (assets[i] != null) { if (prior[i] == null) assets[i].devices = null; else assets[i].devices = prior[i]; }
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior = priorBackground;
            assets.Clear(); prior.Clear();
            return "player virtual keyboard removed";
        }
    }
}
