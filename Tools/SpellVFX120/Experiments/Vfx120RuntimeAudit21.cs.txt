using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // A real Play run. Never calls Effect.Sample, Simulate, or Update manually.
    // Start requires the saved review scene already open. No scene/asset is opened or saved.
    [InitializeOnLoad]
    public static class Vfx120RuntimeAudit
    {
        private const string ScenePath = "Assets/_Project/Art/SpellVFX120/Scenes/SpellVFX120_Review.unity";
        private const string CatalogPath = "Assets/_Project/Art/SpellVFX120/Data/VFX120_Catalog.asset";
        private const string StateKey = "Oheangbu.VFX120.RuntimeAudit.ktp21";
        [Serializable] private sealed class LifeRow
        {
            public string glyph, status = "RUNNING", error;
            public float declaredLife, spawnTime, lastObservedAge, destroyedAt, lateBySeconds;
            public int observedFrames, peakParticles, particleSystems, particleBudget;
            public bool rootGone, childrenGone, particleObjectsGone, rendererObjectsGone;
            public bool assigned, configuredRoles = true, noFallback = true, budgets = true, shaderFlags = true;
            public bool expectsCast, expectsImpact, expectsField, expectsBody, expectsModel, expectsBotanical;
            public bool sawCast, sawImpact, sawField, sawBody, sawModel, sawBotanical;
            public int castPeak, impactPeak, fieldPeak, bodyPeak, modelVisibleFrames, botanicalVisibleFrames, materialCloneCount;
            public float castObservedAge, impactObservedAge, fieldObservedAge, bodyObservedAge;
            public float suppliedImpactClock;
            public string diagnosticPlan, nativeDiagnostic, bodyDiagnostic, modelDiagnostic, botanicalDiagnostic;
            public string conditionalScope = "Diagnostic clock/optional spatial plan only. No actual target, secondary target, parry success, environment contact or Release cue is asserted.";
        }
        [Serializable] private sealed class Counter
        {
            public string name, unit; public int samples;
            public double sum, mean, maximum, meanMilliseconds, deltaFromBaselines;
        }
        [Serializable] private sealed class CpuPhase
        {
            public string name, glyph, status;
            public int requestedInstances, frames, minimumLiveInstances = int.MaxValue, maximumLiveInstances;
            public double meanFrameIntervalMs, p95FrameIntervalMs, measuredSeconds;
            public int gen0Collections, gen1Collections, gen2Collections;
            public List<Counter> counters = new List<Counter>();
        }
        [Serializable] private sealed class ResourceRow { public int id; public string kind, name; }
        [Serializable] private sealed class SourceVersion { public string path, before, after; public bool unchanged; }
        [Serializable] private sealed class Report
        {
            public string status, stage, output, unityVersion, hardware, graphicsApi, camera, sceneHashBefore, sceneHashAfter;
            public string scope = "All 120 catalog instances in actual Editor Play Update/native ParticleSystem/Destroy, in lifetime batches <=16. Assigned/reserved counts are catalog metadata, not 120 implemented gameplay spells. Required native impacts receive an explicit diagnostic flight clock; applicable plans come from Vfx120Review. Separate CPU phases use ONE named representative at 1 and 16 instances plus three baselines. No Sample/Simulate/manual Update, recognition, damage or input is invoked.";
            public string limitations = "No actual targets, secondary targets, conditional Hit/Release/Interception branches or physical input test. No forced GC/UnloadUnusedAssets. Dynamic Editor resources can confound native material/mesh growth; any growth remains a finding. CPU markers/frametimes include Editor, scene, engine and observer overhead, not isolated VFX CPU, GPU or a frame-rate PASS. Spawning/first .5s are excluded; rare late-child discovery can occur inside the measured window, but particle counts are not read there. One CPU representative is not an all-120 worst-case performance claim.";
            public long startedUtcTicks;
            public int completed, failed, errors, baselineMaterials, baselineMeshes, finalMaterials, finalMeshes;
            public int assignedEntries, reservedEntries;
            public int wallTimeoutSeconds = 360;
            public string appMvid, auditMvid, catalogDependencyBefore, catalogDependencyAfter, camerasBefore, camerasAfter;
            public string reviewStateBefore, reviewStateAfter;
            public string cpuRepresentative, cpuRepresentativePolicy = "Existing Count*4 + RibbonCount*3 + UseMist*3 score among Duration>=3. Named representative only; not a native/triangle-weighted worst case.";
            public bool sourceVersionUnchanged, observedEditMode, cameraStateRestored;
            public List<SourceVersion> sourceVersions = new List<SourceVersion>();
            public int savedVsync, savedTargetFps; public float savedTimeScale; public bool savedRunInBackground;
            public bool savedSettings, restored, sceneFileUnchanged, resumedAfterReload;
            public string lifetimeStatus, resourceStatus, cpuStatus;
            public string resourceAttributionStatus = "NOT_RUN", resourceAttributionOutput;
            public List<string> messages = new List<string>();
            public List<LifeRow> lifetimes = new List<LifeRow>();
            public List<CpuPhase> cpu = new List<CpuPhase>();
            public List<ResourceRow> newResources = new List<ResourceRow>();
            public List<string> recordedMarkers = new List<string>();
        }
        [Serializable] private sealed class Progress
        {
            public string status, stage, output, lifetimeStatus, resourceStatus, cpuStatus;
            public int completed, failed, errors, cpuPhases;
            public double elapsedSeconds;
            public bool restored;
        }
        private sealed class Tracked
        {
            public GameObject root; public Vfx120Effect effect;
            public readonly List<GameObject> children = new List<GameObject>();
            public readonly List<ParticleSystem> particles = new List<ParticleSystem>();
            public readonly List<Renderer> renderers = new List<Renderer>();
            public readonly HashSet<int> childIds = new HashSet<int>(), particleIds = new HashSet<int>(), rendererIds = new HashSet<int>(), cloneIds = new HashSet<int>();
            public readonly HashSet<int> sourceRendererIds = new HashSet<int>();
            public readonly HashSet<Material> sourceMaterials = new HashSet<Material>();
            public readonly List<MotifTrack> motifs = new List<MotifTrack>();
            public LifeRow row; public bool finished;
        }
        private sealed class MotifTrack
        {
            public Vfx120TraditionalMotif motif; public ParticleSystem[] systems;
            public string role;
        }
        private static Report _report;
        private static Vfx120Catalog _catalog;
        private static Vfx120Profile _representative;
        private static readonly List<Tracked> _live = new List<Tracked>();
        private static readonly List<ProfilerRecorder> _recorders = new List<ProfilerRecorder>();
        private static readonly List<ProfilerRecorderDescription> _descriptions = new List<ProfilerRecorderDescription>();
        private static readonly List<double> _frameTimes = new List<double>(2048);
        private static Dictionary<int, ResourceRow> _resourceBaseline;
        private static int _lastFrame = -1, _waitUntilFrame, _nextEntry, _cpuIndex, _gc0, _gc1, _gc2;
        private static float _stageStarted;
        private static double _cpuSampleStart;
        private static readonly string[] CpuNames = { "baseline_before", "single", "baseline_between", "concurrent_16", "baseline_after" };
        private static string Output => Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "Art/SpellVFX120/runtime_audit_ktp21.json");
        private static bool Active => _report != null && (_report.status == "RUNNING" || _report.stage == "WAIT_EXIT");

        static Vfx120RuntimeAudit()
        {
            string saved = SessionState.GetString(StateKey, "");
            if (!string.IsNullOrEmpty(saved)) _report = JsonUtility.FromJson<Report>(saved);
            EditorApplication.playModeStateChanged += PlayState;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            Application.logMessageReceived += Log;
            if (_report != null && _report.stage == "WAIT_EXIT" && !EditorApplication.isPlaying)
                EditorApplication.delayCall += CompleteExit;
        }

        public static string Start()
        {
            if (Active) return Poll();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return "Requires stopped, compiled Edit mode; this tool owns its Play entry/exit.";
            if (EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0)
                return "Requires Scene Reload enabled for Play so review runtime objects/toggles restore automatically. This tool will not change Editor project settings.";
            if (SceneManager.sceneCount != 1 || SceneManager.GetActiveScene().path != ScenePath || SceneManager.GetActiveScene().isDirty)
                return "Requires only the saved VFX120 Review scene open; no scene will be opened or saved automatically.";
            _report = new Report { status = "RUNNING", stage = "WAIT_ENTER", output = Output,
                unityVersion = Application.unityVersion, startedUtcTicks = DateTime.UtcNow.Ticks,
                sceneHashBefore = SceneHash(), hardware = SystemInfo.processorType + " / " + SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), savedVsync = QualitySettings.vSyncCount,
                savedTargetFps = Application.targetFrameRate, savedTimeScale = Time.timeScale,
                savedRunInBackground = Application.runInBackground, savedSettings = true };
            _report.camerasBefore = CameraState();
            _report.reviewStateBefore = ReviewState();
            _report.catalogDependencyBefore = AssetDatabase.GetAssetDependencyHash(CatalogPath).ToString();
            foreach (var path in VersionPaths()) _report.sourceVersions.Add(new SourceVersion { path = path, before = FileHash(path) });
            Save(); EditorApplication.isPlaying = true; return Poll();
        }
        public static string Poll()
        {
            if (_report == null) return "{\"status\":\"NOT_STARTED\"}";
            return JsonUtility.ToJson(new Progress { status = _report.status, stage = _report.stage, output = _report.output,
                completed = _report.completed, failed = _report.failed, errors = _report.errors, cpuPhases = _report.cpu.Count,
                elapsedSeconds = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - _report.startedUtcTicks).TotalSeconds,
                lifetimeStatus = _report.lifetimeStatus, resourceStatus = _report.resourceStatus, cpuStatus = _report.cpuStatus,
                restored = _report.restored }, true);
        }
        public static string Cancel() { if (Active) Abort("Cancelled by caller."); return Poll(); }

        private static void PlayState(PlayModeStateChange state)
        {
            if (!Active) return;
            if (state == PlayModeStateChange.EnteredPlayMode && _report.stage == "WAIT_ENTER") InitializePlay();
            else if (state == PlayModeStateChange.ExitingPlayMode && _report.stage != "WAIT_EXIT")
            {
                _report.status = "ABORTED"; _report.messages.Add("Play was stopped before audit completion.");
                RestoreSettings(); DisposeRecorders(); _report.stage = "WAIT_EXIT"; Save();
            }
            else if (state == PlayModeStateChange.EnteredEditMode && _report.stage == "WAIT_EXIT") CompleteExit();
        }
        private static void InitializePlay()
        {
            try
            {
                RequireReview();
                _catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(CatalogPath);
                Need(_catalog != null && _catalog.Entries.Length == 120, "Expected 120 catalog entries.");
                var seen = new HashSet<string>();
                int best = -1; _representative = null;
                foreach (var entry in _catalog.Entries)
                {
                    Need(entry.Profile != null && entry.Prefab != null && seen.Add(entry.Glyph), "Missing or duplicate actual catalog entry.");
                    Need(entry.Profile.Glyph == entry.Glyph && entry.Prefab.GetComponent<Vfx120Effect>()?.Profile == entry.Profile, "Catalog/prefab/profile mismatch: " + entry.Glyph);
                    if (entry.Profile.Assigned) _report.assignedEntries++; else _report.reservedEntries++;
                    int score = entry.Profile.Count * 4 + entry.Profile.RibbonCount * 3 + (entry.Profile.UseMist ? 3 : 0);
                    if (entry.Profile.Duration >= 3 && score > best) { _representative = entry.Profile; best = score; }
                }
                Need(_representative != null, "Need one >=3 second representative for equal steady CPU windows.");
                _report.appMvid = typeof(Vfx120Effect).Module.ModuleVersionId.ToString();
                _report.auditMvid = typeof(Vfx120RuntimeAudit).Module.ModuleVersionId.ToString();
                _report.cpuRepresentative = _representative.Glyph;
                foreach (var review in Object.FindObjectsByType<Vfx120Review>(FindObjectsSortMode.None))
                    if (review.gameObject.scene.path == ScenePath) { review.AutoReplay = false; review.enabled = false; }
                foreach (var effect in Object.FindObjectsByType<Vfx120Effect>(FindObjectsSortMode.None))
                    if (effect.gameObject.scene.path == ScenePath) Object.Destroy(effect.gameObject);
                Time.timeScale = 1; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; Application.runInBackground = true;
                var camera = Camera.main;
                _report.camera = camera == null ? "No tagged main camera" : camera.pixelWidth + "x" + camera.pixelHeight + ", FOV=" + camera.fieldOfView + ", position=" + camera.transform.position;
                _report.messages.Add("Play-only conditions: timeScale=1, vSync=0, targetFrameRate=-1, runInBackground=true; saved original values restored before exit.");
                _live.Clear(); _nextEntry = 0; _lastFrame = -1; _waitUntilFrame = Time.frameCount + 3;
                Stage("SETUP_SETTLE");
            }
            catch (Exception e) { Abort(e.Message); }
        }

        private static void Tick()
        {
            if (!Active || _report.stage == "WAIT_EXIT") return;
            if (TimeSpan.FromTicks(DateTime.UtcNow.Ticks - _report.startedUtcTicks).TotalSeconds > _report.wallTimeoutSeconds) { Abort("360-second wall timeout including reload, eight lifetime batches and CPU phases; paused/slow or incomplete conditions are not a PASS."); return; }
            if (_report.stage == "WAIT_ENTER")
            {
                if (EditorApplication.isPlaying && !EditorApplication.isCompiling) InitializePlay();
                return;
            }
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || Time.frameCount == _lastFrame) return;
            _lastFrame = Time.frameCount;
            try
            {
                RequireReview();
                switch (_report.stage)
                {
                    case "SETUP_SETTLE":
                        if (Time.frameCount >= _waitUntilFrame) { Spawn(_representative, 0, false); Stage("WARMUP"); }
                        break;
                    case "WARMUP":
                        if (Observe(true)) { _waitUntilFrame = Time.frameCount + 3; Stage("WARMUP_SETTLE"); }
                        break;
                    case "WARMUP_SETTLE":
                        if (Time.frameCount >= _waitUntilFrame)
                        {
                            _resourceBaseline = ResourcesNow(); CountResources(_resourceBaseline, out _report.baselineMaterials, out _report.baselineMeshes);
                            BeginBatch();
                        }
                        break;
                    case "LIFETIMES": if (Observe(true)) { _waitUntilFrame = Time.frameCount + 2; Stage("BATCH_SETTLE"); } break;
                    case "BATCH_SETTLE":
                        if (Time.frameCount >= _waitUntilFrame)
                        {
                            if (_nextEntry < 120) BeginBatch();
                            else { OpenRecorders(); _cpuIndex = 0; BeginCpu(); }
                        }
                        break;
                    case "CPU": TickCpu(); break;
                    case "CPU_SETTLE":
                        if (Observe(false))
                        {
                            if (++_cpuIndex < CpuNames.Length) BeginCpu();
                            else { DisposeRecorders(); _waitUntilFrame = Time.frameCount + 3; Stage("FINAL_SETTLE"); }
                        }
                        break;
                    case "FINAL_SETTLE": if (Time.frameCount >= _waitUntilFrame) Finish(); break;
                }
            }
            catch (Exception e) { Abort(e.Message); }
        }
        private static void BeginBatch()
        {
            _live.Clear(); int end = Mathf.Min(_nextEntry + 16, 120);
            while (_nextEntry < end) { Spawn(_catalog.Entries[_nextEntry].Profile, _nextEntry % 16, true); _nextEntry++; }
            Stage("LIFETIMES");
        }
        private static void Spawn(Vfx120Profile profile, int slot, bool countForCatalog)
        {
            GameObject prefab = null;
            foreach (var entry in _catalog.Entries) if (entry.Profile == profile) { prefab = entry.Prefab; break; }
            Need(prefab != null, "No actual catalog prefab for " + profile.Glyph);
            var go = Object.Instantiate(prefab); go.name = "RuntimeAudit_" + profile.Glyph + "_" + slot;
            SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            var effect = go.GetComponent<Vfx120Effect>(); Need(effect != null, "Prefab has no Vfx120Effect.");
            Need(effect.Profile == profile, "Actual instance profile mismatch.");
            effect.PreviewControlled = false; effect.DemonstrationCues = false; go.SetActive(true);
            Vector3 origin = new Vector3((slot % 4 - 1.5f) * 1.3f, 1, (slot / 4) * .65f);
            var row = new LifeRow { glyph = profile.Glyph, assigned = profile.Assigned,
                expectsCast = profile.NativeCastPrefab != null, expectsImpact = profile.NativeImpactPrefab != null,
                expectsField = profile.NativeFieldPrefab != null, expectsBody = profile.NativeBodyPrefab != null,
                expectsModel = profile.SummonPrefab != null, expectsBotanical = profile.BotanicalPrefab != null };
            if (row.expectsImpact)
            {
                row.suppliedImpactClock = Mathf.Max(.01f, profile.Flight);
                effect.SetImpactClock(row.suppliedImpactClock); // Diagnostic API, not a recognized cast or hit.
            }
            var plan = Vfx120Review.CreateDemonstrationAreaPlan(profile);
            if (plan != null)
            {
                // Review plans use the canonical origin (0,1,0); translate their
                // world anchor for the audit slot without changing source data.
                plan.Point += origin - new Vector3(0,1,0);
                row.diagnosticPlan = plan.Shape.ToString(); effect.SetAreaPlan(plan);
            }
            else row.diagnosticPlan = "NONE";
            effect.Begin(origin, null, origin + new Vector3(0, -.9f, 3.4f), Color.white);
            Need(effect.Begun && effect.Life > 0 && effect.Life <= 20, "Invalid bounded effect lifetime.");
            row.declaredLife = effect.Life; row.spawnTime = Time.time;
            if (countForCatalog) _report.lifetimes.Add(row);
            var tracked = new Tracked { root = go, effect = effect, row = row };
            _live.Add(tracked); Discover(tracked, true); InspectConfiguration(tracked);
        }
        private static void Discover(Tracked item, bool force = false)
        {
            if (item.root == null || item.effect == null) return;
            var effect = item.effect; var p = effect.Profile;
            bool added = TrackMotif(item, effect.NativeCast, p.NativeCastPrefab, "cast");
            added |= TrackMotif(item, effect.NativeImpact, p.NativeImpactPrefab, "impact");
            added |= TrackMotif(item, effect.NativeField, p.NativeFieldPrefab, "field");
            added |= TrackMotif(item, effect.NativeBody, p.NativeBodyPrefab, "body");
            if (!added && !force) return;
            // All inactive descendants are retained too. Discover again only when
            // a new late native role appears, avoiding a per-frame hierarchy scan.
            foreach (var transform in item.root.GetComponentsInChildren<Transform>(true))
                if (item.childIds.Add(transform.gameObject.GetInstanceID())) item.children.Add(transform.gameObject);
            foreach (var ps in item.root.GetComponentsInChildren<ParticleSystem>(true))
                if (item.particleIds.Add(ps.GetInstanceID())) item.particles.Add(ps);
            foreach (var renderer in item.root.GetComponentsInChildren<Renderer>(true))
                if (item.rendererIds.Add(renderer.GetInstanceID()))
                {
                    item.renderers.Add(renderer);
                    foreach (var material in renderer.sharedMaterials)
                        item.row.shaderFlags &= material != null && material.shader != null && material.shader.isSupported
                            && material.shader.name != "Hidden/InternalErrorShader";
                }
        }
        private static bool TrackMotif(Tracked item, Vfx120TraditionalMotif motif, GameObject source, string role)
        {
            if (motif == null) return false;
            foreach (var existing in item.motifs) if (existing.motif == motif) return false;
            item.motifs.Add(new MotifTrack { motif = motif, systems = motif.GetComponentsInChildren<ParticleSystem>(true), role = role });
            CheckSharedMaterials(item, motif.gameObject, source);
            if (role == "cast") item.row.sawCast = true;
            else if (role == "impact") item.row.sawImpact = true;
            else if (role == "field") item.row.sawField = true;
            else item.row.sawBody = true;
            return true;
        }
        private static void CheckSharedMaterials(Tracked item, GameObject instance, GameObject source)
        {
            if (source == null || instance == null) { item.row.configuredRoles = false; return; }
            var originals = new HashSet<Material>(source.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials));
            item.sourceMaterials.UnionWith(originals);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                item.sourceRendererIds.Add(renderer.GetInstanceID());
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && !originals.Contains(material)) item.cloneIds.Add(material.GetInstanceID());
            }
            item.row.materialCloneCount = item.cloneIds.Count;
        }
        private static void InspectConfiguration(Tracked item)
        {
            var effect = item.effect; if (effect == null) return; var row = item.row; var p = effect.Profile;
            // The owner's hard end intentionally clears role references before
            // deferred Destroy. Evaluate configuration while it is meant to exist.
            if (effect.Age >= effect.Life) return;
            row.nativeDiagnostic = effect.NativeDiagnostic; row.bodyDiagnostic = effect.NativeBodyDiagnostic;
            row.modelDiagnostic = effect.MeshyDiagnostic; row.botanicalDiagnostic = effect.BotanicalDiagnostic;
            row.noFallback &= !Fallback(row.nativeDiagnostic) && !Fallback(row.bodyDiagnostic)
                && !Fallback(row.modelDiagnostic) && !Fallback(row.botanicalDiagnostic);
            row.configuredRoles &= (!row.expectsCast || effect.NativeCast != null)
                && (!row.expectsField || effect.NativeFieldConfigured)
                && (!row.expectsBody || effect.NativeBodyConfigured)
                && (!row.expectsModel || effect.MeshyConfigured)
                && (!row.expectsBotanical || effect.BotanicalConfigured);
            if (row.expectsModel && !row.sawModel && effect.MeshyConfigured && effect.MeshyModel != null)
            {
                row.sawModel = true; CheckSharedMaterials(item, effect.MeshyModel.gameObject, p.SummonPrefab);
            }
            if (row.expectsBotanical && !row.sawBotanical && effect.BotanicalConfigured && effect.BotanicalInstance != null)
            {
                row.sawBotanical = true; CheckSharedMaterials(item, effect.BotanicalInstance, p.BotanicalPrefab);
            }
            if (row.expectsBody || row.expectsModel || row.expectsBotanical || (row.expectsField && p.NativeReplaceBody))
                row.configuredRoles &= effect.PartCount == 0 && effect.AccentCount == 0;
        }
        private static bool Fallback(string value) => !string.IsNullOrEmpty(value) && (value.IndexOf("fallback", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("REQUIRED", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("UNSUPPORTED", StringComparison.OrdinalIgnoreCase) >= 0);
        private static void InspectParticles(Tracked item)
        {
            var row = item.row;
            // Observe temporary clones while alive too; an end-only resource count
            // cannot detect allocations that the owner later destroys correctly.
            foreach (var renderer in item.renderers) if (renderer != null && item.sourceRendererIds.Contains(renderer.GetInstanceID()))
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null && !item.sourceMaterials.Contains(material)) item.cloneIds.Add(material.GetInstanceID());
                    row.shaderFlags &= material != null && material.shader != null && material.shader.isSupported
                        && material.shader.name != "Hidden/InternalErrorShader";
                }
            row.materialCloneCount = item.cloneIds.Count;
            var nativeIds = new HashSet<int>(); int totalLive = 0, totalBudget = 0, totalSystems = 0;
            foreach (var track in item.motifs)
            {
                if (track.motif == null) continue;
                int live = 0, capacity = 0, count = 0;
                foreach (var ps in track.systems) if (ps != null)
                {
                    nativeIds.Add(ps.GetInstanceID()); count++; live += ps.particleCount; capacity += ps.main.maxParticles;
                    row.budgets &= ps.particleCount <= ps.main.maxParticles;
                }
                int maxSystems = track.role == "body" ? 12 : 32, maxParticles = track.role == "body" ? 240 : 400;
                row.budgets &= track.motif.SourceSystemCount <= maxSystems && track.motif.ParticleBudget <= maxParticles
                    && count <= maxSystems && capacity <= maxParticles && live <= capacity;
                if (track.motif.InstanceCount > 0) row.budgets &= count == track.motif.SourceSystemCount && capacity == track.motif.ParticleBudget;
                if (track.role == "cast") { row.castPeak = Math.Max(row.castPeak, live); row.castObservedAge = Mathf.Max(row.castObservedAge, track.motif.Age); }
                else if (track.role == "impact") { row.impactPeak = Math.Max(row.impactPeak, live); row.impactObservedAge = Mathf.Max(row.impactObservedAge, track.motif.Age); }
                else if (track.role == "field") { row.fieldPeak = Math.Max(row.fieldPeak, live); row.fieldObservedAge = Mathf.Max(row.fieldObservedAge, track.motif.Age); }
                else { row.bodyPeak = Math.Max(row.bodyPeak, live); row.bodyObservedAge = Mathf.Max(row.bodyObservedAge, track.motif.Age); }
                totalLive += live; totalBudget += capacity; totalSystems += count;
            }
            int otherLive = 0, otherBudget = 0, otherSystems = 0;
            foreach (var ps in item.particles) if (ps != null && !nativeIds.Contains(ps.GetInstanceID()))
            { otherSystems++; otherLive += ps.particleCount; otherBudget += ps.main.maxParticles; row.budgets &= ps.particleCount <= ps.main.maxParticles; }
            // Only the remaining procedural atmosphere retains the original 3/360 contract.
            row.budgets &= otherSystems <= 3 && otherBudget <= 360 && otherLive <= otherBudget;
            row.peakParticles = Math.Max(row.peakParticles, totalLive + otherLive);
            row.particleBudget = Math.Max(row.particleBudget, totalBudget + otherBudget);
            row.particleSystems = Math.Max(row.particleSystems, totalSystems + otherSystems);
            var effect = item.effect;
            if (row.expectsModel && effect.MeshyModel != null && effect.MeshyModel.GetComponentsInChildren<Renderer>(true)
                .Any(r => r.enabled && r.gameObject.activeInHierarchy)) row.modelVisibleFrames++;
            if (row.expectsBotanical && effect.BotanicalInstance != null && effect.BotanicalInstance.GetComponentsInChildren<Renderer>(true)
                .Any(r => r.enabled && r.gameObject.activeInHierarchy)) row.botanicalVisibleFrames++;
        }
        private static bool Observe(bool inspectParticles)
        {
            bool all = true;
            foreach (var item in _live)
            {
                if (item.finished) continue;
                var row = item.row;
                if (item.root != null && Time.time - row.spawnTime <= row.declaredLife + 1)
                {
                    all = false; row.observedFrames++;
                    if (item.effect != null) row.lastObservedAge = item.effect.Age;
                    // Lifetime observations rescan all descendants, including
                    // unforeseen late children; cost windows use role discovery only.
                    Discover(item, inspectParticles); InspectConfiguration(item);
                    if (inspectParticles)
                        InspectParticles(item);
                    continue;
                }
                row.destroyedAt = Time.time; row.lateBySeconds = Time.time - row.spawnTime - row.declaredLife;
                row.rootGone = item.root == null; row.childrenGone = true; row.particleObjectsGone = true;
                foreach (var child in item.children) if (child != null) row.childrenGone = false;
                foreach (var ps in item.particles) if (ps != null) row.particleObjectsGone = false;
                row.rendererObjectsGone = item.renderers.All(r => r == null);
                if (!row.rootGone || !row.childrenGone || !row.particleObjectsGone || !row.rendererObjectsGone) row.error = "Root, detached child, renderer or native PS survives the lifetime + 1s grace.";
                if (!row.configuredRoles || !row.noFallback || (row.expectsImpact && !row.sawImpact)) row.error = "Assigned native/model/botanical role was not configured/observed, or used fallback.";
                if (!row.budgets || row.materialCloneCount != 0 || !row.shaderFlags) row.error = "Role budget, shader or original shared material reference failed.";
                if (inspectParticles && ((row.expectsCast && row.castPeak == 0) || (row.expectsImpact && row.impactPeak == 0)
                    || (row.expectsField && row.fieldPeak == 0) || (row.expectsBody && row.bodyPeak == 0)
                    || (row.expectsModel && row.modelVisibleFrames == 0) || (row.expectsBotanical && row.botanicalVisibleFrames == 0)))
                    row.error = "Configured role had no observed native particles/model visibility during actual Update.";
                if (inspectParticles && ((row.expectsCast && row.castObservedAge <= 0) || (row.expectsImpact && row.impactObservedAge <= 0)
                    || (row.expectsField && row.fieldObservedAge <= 0) || (row.expectsBody && row.bodyObservedAge <= 0)))
                    row.error = "Configured native role's actual Update age progression was not observed.";
                if (row.lastObservedAge <= 0 || row.observedFrames < 2) row.error = "Actual Update age progression was not observed.";
                if (row.lateBySeconds < -Mathf.Max(.1f, Time.deltaTime * 2)) row.error = "Effect disappeared earlier than its declared lifetime.";
                row.status = row.error == null ? "PASS_ACTUAL_UPDATE_AND_DESTRUCTION" : "FAIL";
                item.finished = true;
                if (_report.lifetimes.Contains(row)) { _report.completed++; if (row.error != null) _report.failed++; }
                // Preserve the failure first, then clean only objects created by this harness.
                if (item.root != null) Object.Destroy(item.root);
                foreach (var child in item.children) if (child != null) Object.Destroy(child);
            }
            return all;
        }

        private static void OpenRecorders()
        {
            var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
            var names = new HashSet<string>();
            // Whole-frame/script/GC counters get priority over numerous nested native markers.
            for (int pass = 0; pass < 2; pass++) foreach (var handle in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(handle); string name = d.Name;
                bool core = name == "PlayerLoop" || name == "Main Thread" || name == "BehaviourUpdate" || name == "GC Allocated In Frame"
                    || name == "GC Used Memory" || name.Contains("Vfx120Effect");
                bool wanted = pass == 0 ? core : !core && name.Contains("ParticleSystem") && name.Contains("Update");
                if (!wanted || !names.Add(name) || _recorders.Count >= 16) continue;
                try { var recorder = new ProfilerRecorder(handle, 1, ProfilerRecorderOptions.Default); recorder.Start(); _recorders.Add(recorder); _descriptions.Add(d); _report.recordedMarkers.Add(name + " [" + d.UnitType + "]"); }
                catch (Exception e) { _report.messages.Add("Marker unavailable: " + name + ": " + e.Message); }
            }
        }
        private static void BeginCpu()
        {
            _live.Clear(); int instances = _cpuIndex == 1 ? 1 : _cpuIndex == 3 ? 16 : 0;
            for (int i = 0; i < instances; i++) Spawn(_representative, i, false);
            var phase = new CpuPhase { name = CpuNames[_cpuIndex], glyph = _representative.Glyph, requestedInstances = instances, status = "RUNNING" };
            foreach (var d in _descriptions) phase.counters.Add(new Counter { name = d.Name, unit = d.UnitType.ToString() });
            _report.cpu.Add(phase); _frameTimes.Clear(); _cpuSampleStart = -1;
            Stage("CPU");
        }
        private static void TickCpu()
        {
            // Track a newly created impact hierarchy once, including in the cost
            // phase. No particleCount reads or explicit simulation occur here.
            foreach (var item in _live) Discover(item);
            float elapsed = Time.time - _stageStarted;
            if (elapsed < .5f) return; // exclude instantiate/initial native setup and previous-frame recorder data
            var phase = _report.cpu[_report.cpu.Count - 1];
            int alive = 0; foreach (var item in _live) if (item.root != null) alive++;
            if (elapsed <= 2f)
            {
                if (_cpuSampleStart < 0) { _cpuSampleStart = Time.realtimeSinceStartupAsDouble; _gc0 = GC.CollectionCount(0); _gc1 = GC.CollectionCount(1); _gc2 = GC.CollectionCount(2); }
                phase.minimumLiveInstances = Math.Min(phase.minimumLiveInstances, alive); phase.maximumLiveInstances = Math.Max(phase.maximumLiveInstances, alive);
                phase.frames++; _frameTimes.Add(Time.unscaledDeltaTime * 1000.0);
                for (int i = 0; i < _recorders.Count; i++)
                {
                    var rec = _recorders[i]; if (!rec.Valid || rec.Count == 0) continue;
                    double value = rec.LastValue; var counter = phase.counters[i]; counter.samples++; counter.sum += value; counter.maximum = Math.Max(counter.maximum, value);
                }
                return;
            }
            phase.measuredSeconds = _cpuSampleStart < 0 ? 0 : Time.realtimeSinceStartupAsDouble - _cpuSampleStart;
            if (phase.frames > 0)
            {
                double sum = 0; foreach (double value in _frameTimes) sum += value; phase.meanFrameIntervalMs = sum / phase.frames;
                _frameTimes.Sort(); phase.p95FrameIntervalMs = _frameTimes[Math.Min(_frameTimes.Count - 1, (int)Math.Ceiling(_frameTimes.Count * .95) - 1)];
            }
            if (_cpuSampleStart >= 0) { phase.gen0Collections = GC.CollectionCount(0) - _gc0; phase.gen1Collections = GC.CollectionCount(1) - _gc1; phase.gen2Collections = GC.CollectionCount(2) - _gc2; }
            foreach (var c in phase.counters) { c.mean = c.samples > 0 ? c.sum / c.samples : 0; if (c.unit.Contains("Nanosecond")) c.meanMilliseconds = c.mean / 1e6; }
            phase.status = phase.frames >= 30 && phase.minimumLiveInstances == phase.requestedInstances && phase.maximumLiveInstances == phase.requestedInstances
                ? "MEASURED_EDITOR_STEADY_WINDOW" : "UNVERIFIED_INSUFFICIENT_FRAMES_OR_CONCURRENCY";
            Stage("CPU_SETTLE");
        }

        private static void Finish()
        {
            CheckVersion();
            var resources = ResourcesNow(); CountResources(resources, out _report.finalMaterials, out _report.finalMeshes);
            foreach (var pair in resources) if (!_resourceBaseline.ContainsKey(pair.Key)) _report.newResources.Add(pair.Value);
            _report.lifetimeStatus = _report.completed == 120 && _report.failed == 0 ? "PASS_120_REAL_UPDATE_DESTRUCTION" : "FAIL_OR_UNVERIFIED";
            _report.resourceStatus = _report.newResources.Count == 0 ? "PASS_NO_NATIVE_MATERIAL_MESH_GROWTH_AFTER_WARMUP" : "GROWTH_OBSERVED_REVIEW_EDITOR_INTERFERENCE_OR_LEAK";
            _report.cpuStatus = _report.cpu.Count == 5 && _report.cpu.TrueForAll(p => p.status == "MEASURED_EDITOR_STEADY_WINDOW") && _recorders.Count == 0 && _report.recordedMarkers.Count > 0
                ? "EDITOR_MEASUREMENTS_REPORTED_NO_PERFORMANCE_PASS" : "UNVERIFIED_OR_PARTIAL";
            foreach (var phase in _report.cpu)
            {
                if (phase.requestedInstances == 0) continue;
                foreach (var counter in phase.counters)
                {
                    double sum = 0; int n = 0;
                    foreach (var baseline in _report.cpu)
                        if (baseline.requestedInstances == 0) foreach (var b in baseline.counters)
                            if (b.name == counter.name && b.samples > 0) { sum += b.mean; n++; }
                    if (n > 0) counter.deltaFromBaselines = counter.mean - sum / n;
                }
            }
            _report.status = _report.failed == 0 && _report.completed == 120 && _report.errors == 0 && _report.newResources.Count == 0 && _report.sourceVersionUnchanged
                ? "COMPLETED_RUNTIME_LIFETIME_CHECK_CPU_EDITOR_ONLY" : "COMPLETED_WITH_FINDINGS";
            InspectNewMaterialReferences();
            ExitPlay();
        }
        private static void InspectNewMaterialReferences()
        {
            // All CPU/resource counters and verdicts are already final. This diagnostic
            // cannot alter them, and an empty ID set must not trigger name-based discovery.
            try
            {
                var newIds = new HashSet<int>();
                foreach (var resource in _report.newResources) if (resource.kind == "Material") newIds.Add(resource.id);
                if (newIds.Count == 0) { _report.resourceAttributionStatus = "SKIPPED_NO_NEW_MATERIAL_IDS"; return; }
                var liveIds = new List<int>();
                foreach (var material in Resources.FindObjectsOfTypeAll<Material>())
                    if (material != null && !EditorUtility.IsPersistent(material) && newIds.Contains(material.GetInstanceID()))
                        liveIds.Add(material.GetInstanceID());
                if (liveIds.Count == 0) { _report.resourceAttributionStatus = "SKIPPED_NEW_MATERIALS_NO_LONGER_LOADED"; return; }
                string json = Vfx120ResourceAttribution.Inspect(liveIds.ToArray());
                var attribution = JsonUtility.FromJson<Vfx120ResourceAttribution.Report>(json);
                if (attribution == null || string.IsNullOrEmpty(attribution.status))
                    throw new InvalidOperationException("Resource attribution returned no status");
                _report.resourceAttributionStatus = attribution.status;
                _report.resourceAttributionOutput = attribution.output;
            }
            catch (Exception error)
            {
                _report.resourceAttributionStatus = "UNVERIFIED_DIAGNOSTIC_CALL_FAILED";
                _report.messages.Add("Post-measurement resource attribution diagnostic: " + error);
            }
        }
        private static Dictionary<int, ResourceRow> ResourcesNow()
        {
            var result = new Dictionary<int, ResourceRow>();
            foreach (var material in Resources.FindObjectsOfTypeAll<Material>())
                if (!EditorUtility.IsPersistent(material)) result[material.GetInstanceID()] = new ResourceRow { id = material.GetInstanceID(), kind = "Material", name = material.name };
            foreach (var mesh in Resources.FindObjectsOfTypeAll<Mesh>())
                if (!EditorUtility.IsPersistent(mesh)) result[mesh.GetInstanceID()] = new ResourceRow { id = mesh.GetInstanceID(), kind = "Mesh", name = mesh.name };
            return result;
        }
        private static void CountResources(Dictionary<int, ResourceRow> resources, out int materials, out int meshes)
        {
            materials = 0; meshes = 0; foreach (var item in resources.Values) { if (item.kind == "Material") materials++; else meshes++; }
        }
        private static void Abort(string reason)
        {
            if (_report == null) return;
            _report.status = "ABORTED"; _report.messages.Add(reason);
            foreach (var item in _live) { if (item.root != null) Object.Destroy(item.root); foreach (var child in item.children) if (child != null) Object.Destroy(child); }
            ExitPlay();
        }
        private static void ExitPlay()
        {
            DisposeRecorders(); RestoreSettings(); _report.stage = "WAIT_EXIT"; Save();
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            else if (!EditorApplication.isPlayingOrWillChangePlaymode) CompleteExit();
        }
        private static void CompleteExit()
        {
            if (_report == null || _report.stage != "WAIT_EXIT" || EditorApplication.isPlayingOrWillChangePlaymode) return;
            RestoreSettings(); _report.sceneHashAfter = SceneHash(); _report.sceneFileUnchanged = _report.sceneHashBefore == _report.sceneHashAfter;
            _report.observedEditMode = !EditorApplication.isPlayingOrWillChangePlaymode;
            _report.camerasAfter = CameraState(); _report.cameraStateRestored = _report.camerasBefore == _report.camerasAfter;
            _report.reviewStateAfter = ReviewState();
            _report.restored = _report.observedEditMode && SceneManager.sceneCount == 1 && SceneManager.GetActiveScene().path == ScenePath
                && !SceneManager.GetActiveScene().isDirty && _report.cameraStateRestored && _report.reviewStateBefore == _report.reviewStateAfter
                && QualitySettings.vSyncCount == _report.savedVsync && Application.targetFrameRate == _report.savedTargetFps
                && Mathf.Approximately(Time.timeScale, _report.savedTimeScale) && Application.runInBackground == _report.savedRunInBackground && _report.sceneFileUnchanged;
            if (!_report.restored) _report.messages.Add("Restore or saved scene hash check failed.");
            if (_report.status != "ABORTED" && (!_report.restored || _report.errors > 0)) _report.status = "COMPLETED_WITH_FINDINGS";
            _report.stage = "FINISHED"; Save();
        }
        private static void RestoreSettings()
        {
            if (_report == null || !_report.savedSettings) return;
            QualitySettings.vSyncCount = _report.savedVsync; Application.targetFrameRate = _report.savedTargetFps;
            Time.timeScale = _report.savedTimeScale; Application.runInBackground = _report.savedRunInBackground;
        }
        private static void DisposeRecorders() { foreach (var rec in _recorders) rec.Dispose(); _recorders.Clear(); _descriptions.Clear(); }
        private static void Stage(string stage) { _report.stage = stage; _stageStarted = Time.time; Save(); }
        private static void Save()
        {
            if (_report == null) return; string json = JsonUtility.ToJson(_report, true); SessionState.SetString(StateKey, json);
            Directory.CreateDirectory(Path.GetDirectoryName(_report.output)); File.WriteAllText(_report.output, json);
        }
        private static void Log(string message, string stack, LogType type)
        {
            if (!Active || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
            _report.errors++; if (_report.messages.Count < 24) _report.messages.Add(type + ": " + message);
        }
        private static void BeforeReload()
        {
            if (!Active) return;
            if (_report.stage == "WAIT_ENTER" || _report.stage == "WAIT_EXIT") { Save(); return; }
            _report.resumedAfterReload = true; Abort("Script/domain reload interrupted the real Play audit; no continuation is inferred.");
        }
        private static IEnumerable<string> VersionPaths()
        {
            string app = Path.Combine(Application.dataPath, "_Project/Scripts/App/SpellVFX120");
            foreach (var path in Directory.GetFiles(app, "*.cs").OrderBy(x => x, StringComparer.Ordinal)) yield return path;
            string shaders = Path.Combine(Application.dataPath, "_Project/Shaders/SpellVFX120");
            foreach (var path in Directory.GetFiles(shaders, "*.shader").OrderBy(x => x, StringComparer.Ordinal)) yield return path;
            yield return Path.Combine(Application.dataPath, "_Project/Scripts/Editor/Vfx120RuntimeAudit.cs");
        }
        private static void CheckVersion()
        {
            _report.catalogDependencyAfter = AssetDatabase.GetAssetDependencyHash(CatalogPath).ToString();
            _report.sourceVersionUnchanged = _report.catalogDependencyBefore == _report.catalogDependencyAfter
                && _report.appMvid == typeof(Vfx120Effect).Module.ModuleVersionId.ToString()
                && _report.auditMvid == typeof(Vfx120RuntimeAudit).Module.ModuleVersionId.ToString();
            foreach (var source in _report.sourceVersions)
            { source.after = FileHash(source.path); source.unchanged = source.before == source.after; _report.sourceVersionUnchanged &= source.unchanged; }
        }
        [Serializable] private struct CameraRecord
        {
            public string path; public Vector3 position, scale; public Quaternion rotation;
            public float fov, near, far, orthoSize; public bool enabled, orthographic; public Rect rect; public int mask;
        }
        private static string CameraState() => string.Join("\n", Resources.FindObjectsOfTypeAll<Camera>()
            .Where(c => c.gameObject.scene.path == ScenePath).OrderBy(c => TransformPath(c.transform), StringComparer.Ordinal)
            .Select(c => JsonUtility.ToJson(new CameraRecord { path = TransformPath(c.transform), position = c.transform.localPosition,
                scale = c.transform.localScale, rotation = c.transform.localRotation, fov = c.fieldOfView, near = c.nearClipPlane,
                far = c.farClipPlane, orthoSize = c.orthographicSize, enabled = c.enabled, orthographic = c.orthographic, rect = c.rect, mask = c.cullingMask })));
        private static string ReviewState() => string.Join("\n", Resources.FindObjectsOfTypeAll<Vfx120Review>()
            .Where(r => r.gameObject.scene.path == ScenePath).OrderBy(r => TransformPath(r.transform), StringComparer.Ordinal)
            .Select(r => TransformPath(r.transform) + ":enabled=" + r.enabled + ":AutoReplay=" + r.AutoReplay));
        private static string TransformPath(Transform t) => t.parent == null ? t.name : TransformPath(t.parent) + "/" + t.name;
        private static string FileHash(string path)
        {
            if (!File.Exists(path)) return "MISSING";
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream));
        }
        private static string SceneHash()
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ScenePath);
            if (!File.Exists(path)) return "MISSING";
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream));
        }
        private static void RequireReview() { Need(SceneManager.sceneCount == 1 && SceneManager.GetActiveScene().path == ScenePath, "Review scene changed; other scenes will not be touched."); }
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
