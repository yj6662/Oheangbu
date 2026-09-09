using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using Oheangbu.Drawing;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // Production component fixture, starting at the accepted-recognition/event boundary.
    // NEVER calls SpawnPattern, LateUpdate, Effect.Begin/Sample/Update or TakeDamage itself.
    // The inactive input object supplies event delegates only: this is NOT a device/recognizer test.
    // Existing player's channels, ink, target list, input actions, camera and scene files are untouched.
    [InitializeOnLoad]
    public static class Vfx120GameplayAudit
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Glyphs = "가나마사아고노모소오거너머서어";
        private const string StateKey = "Oheangbu.VFX120.GameplayAudit.v1";
        private static readonly string[] AllowedScenes =
        {
            "Assets/_Project/Scenes/Dev/C2_CodexWorld.unity",
            "Assets/_Project/Scenes/Dev/C1_SpellRange.unity",
            "Assets/_Project/Scenes/Dev/C1_ParryRange.unity",
            "Assets/_Project/Scenes/Dev/C1_CombatLoop.unity"
        };

        [Serializable] private sealed class HitRow
        {
            public string target; public float plannedTime, actualTime, power, actualDamage, lateSeconds, frameDelta;
            public bool observed;
        }
        [Serializable] private sealed class CaptureRow
        {
            public string glyph, phase, status = "REQUESTED", png, camera, error;
            public int frame, width = 1280, height = 720, observedHitCount;
            public float age, life, time, unscaledTime, timeScale, scheduledReferenceTime, observationLateness;
            public float cameraFieldOfView, cameraAspect, cameraNear, cameraFar;
            public Vector3 cameraPosition, cameraEulerAngles, effectOrigin, originViewport;
            public bool cameraOrthographic, cameraStateRestored;
            public double captureWallMilliseconds;
        }
        [Serializable] private sealed class CaseRow
        {
            public string glyph, kind, shape, prefab, profile, spawnedGlyph, status = "RUNNING", error;
            public float commitTime, timeScale, receivedImpactClock, expectedImpactClock, receivedGuardClock;
            public float receivedBrightWindow, guardStart, inkBefore, inkAfter, expectedInkAfter;
            public float originError, maximumHitLateness, lastObservedEffectAge;
            public float effectDeclaredLife, effectStartedAt, firstMissingTime = -1f, missingFrameAllowance, earlyDestructionSeconds;
            public float scheduledImpactTime, forwardedImpactTime, impactScheduleError;
            public int commitFrame, spawnObservedFrame, plannedEvents, spawnedRoots, authoredComponents, legacyComponents;
            public int expectedHits, actualHits, observedUpdateFrames;
            public bool profileReferenceMatches, areaReferenceMatches, areaContentsUnchanged;
            public bool targetMatches, guardMatches, naturallyDestroyed, actualUpdateObserved, pendingContextCleared;
            public bool lifetimeWithinObservedFrame;
            public string areaAtCast, areaAtFinish;
            public List<HitRow> hits = new List<HitRow>();
            public string captureStatus = "DISABLED";
            public List<CaptureRow> captures = new List<CaptureRow>();
        }
        [Serializable] private sealed class AssetRow
        {
            public string path, before, after, memoryBefore, memoryAfter; public bool unchanged;
        }
        [Serializable] private sealed class Report
        {
            public string status, stage, output, scene, unityVersion, sourceWiring, sourceAdapter, sourceVisualSet;
            public string scope = "15 production spell entries in actual Editor Play. A temporary CombatLoopWiring/SpellResolver/ParryJudge/InkPool and BrushStrokeFeedAdapter reuse the selected live scene's readonly settings. Accepted DrawnLetter plus low-level DrawingInputController events enter real subscribers; normal LateUpdate invokes SpawnPattern and real Update schedules damage and advances/destroys VFX.";
            public string limitations = "UNVERIFIED: physical input, recognizer/Raw samples, existing player's DI/channel wiring, user camera/movement/lock-on state transitions, actual enemy AI, incoming-attack parry outcomes, art/readability and performance. Two synthetic strokes represent event ordering, not the written glyph. Temporary targets have no controller/collider and fixture lock-on selects a camera-centred target. Existing gameplay can continue and may confound global spawn observations. Production stroke noise consumes Unity's global Random just as real drawing does; the harness does not reset unrelated gameplay RNG.";
            public string selection = "Only existing SpellVisualSet references are used; missing/new-profile mappings fail rather than being replaced by the harness.";
            public string sceneHashBefore, sceneHashAfter;
            public long startedUtcTicks;
            public int completed, failed, errors;
            public bool sceneFileUnchanged, assetsUnchanged, isolatedServices, cleanupCompleted;
            public string cleanupStatus = "NOT_REQUESTED";
            public int cleanupTrackedObjects, cleanupResidualObjects;
            public bool captureFrames;
            public string captureDirectory, captureStatus = "DISABLED";
            public string captureScope = "Optional 1280x720 extra render of the actual live Camera.main state. No Sample/Begin, animation seek, time-scale change, camera move or fabricated impact is used. PNGs exclude screen-space overlay UI and are not a continuous video or art PASS. Extra camera render/readback/PNG encoding stalls the Editor: this run is not a CPU, GPU or frame-rate measurement.";
            public List<CaseRow> cases = new List<CaseRow>();
            public List<AssetRow> assets = new List<AssetRow>();
            public List<string> messages = new List<string>();
        }
        [Serializable] private sealed class Progress
        {
            public string status, stage, glyph, output; public int completed, failed, errors;
            public double elapsedSeconds; public bool cleanupCompleted;
        }
        private sealed class ActualHit
        {
            public EnemyVitals target; public float time, damage, frameDelta;
        }
        private sealed class AssetSnapshot { public Object asset; public AssetRow row; }

        private static Report _report;
        private static CombatLoopWiring _source;
        private static BrushStrokeFeedAdapter _sourceAdapter;
        private static SpellBookSO _book;
        private static SpellVisualSetSO _visuals;
        private static CombatConfigSO _config;
        private static Fixture _fixture;
        private static int _next, _lastFrame = -1, _waitFrame;
        private static readonly List<AssetSnapshot> _assets = new List<AssetSnapshot>();
        private static readonly List<Object> _cleanupPending = new List<Object>();
        private static bool _cleanupDiscoveryFailed;
        private static double _cleanupRequestedAt;
        private static bool Active => _report != null && _report.status == "RUNNING";
        private static string Output => Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "Art/SpellVFX120/gameplay_audit.json");

        static Vfx120GameplayAudit()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (Active && state == PlayModeStateChange.ExitingPlayMode) Abort("Play Mode ended before the fixture completed.");
            };
            AssemblyReloadEvents.beforeAssemblyReload += () => { if (Active) Abort("Script reload interrupted the runtime fixture."); };
            string saved = SessionState.GetString(StateKey, "");
            if (!string.IsNullOrEmpty(saved))
            {
                _report = JsonUtility.FromJson<Report>(saved);
                if (Active)
                {
                    _report.status = "ABORTED"; _report.stage = "DOMAIN_RELOAD";
                    _report.messages.Add("State reloaded without live fixture references; no test resumed or passed.");
                    Save();
                }
            }
        }

        public static string Start(int wiringInstanceId = 0, bool captureFrames = false)
        {
            if (Active) return Poll();
            if (!Application.isPlaying || EditorApplication.isPaused) return "{\"status\":\"NOT_RUN_PLAY_REQUIRED\"}";
            Scene scene = SceneManager.GetActiveScene();
            if (Array.IndexOf(AllowedScenes, scene.path) < 0) return "{\"status\":\"NOT_RUN_OPEN_C2_OR_COMBAT_RANGE_PLAY\"}";
            if (Time.timeScale <= 0f || Camera.main == null) return "{\"status\":\"NOT_RUN_RUNNING_TIME_AND_MAIN_CAMERA_REQUIRED\"}";
            _report = new Report
            {
                status = "RUNNING", stage = "PREFLIGHT", output = Output, scene = scene.path,
                unityVersion = Application.unityVersion, startedUtcTicks = DateTime.UtcNow.Ticks,
                sceneHashBefore = FileHash(scene.path), captureFrames = captureFrames,
                captureStatus = captureFrames ? "RUNNING_UNVERIFIED" : "DISABLED",
                captureDirectory = captureFrames ? Path.Combine(Path.GetDirectoryName(Output), "GameplayCapture", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")) : null
            };
            _assets.Clear(); _cleanupPending.Clear(); _cleanupDiscoveryFailed = false; _next = 0; _lastFrame = -1;
            try
            {
                var matches = new List<CombatLoopWiring>();
                foreach (var wiring in Object.FindObjectsByType<CombatLoopWiring>(FindObjectsSortMode.None))
                    if (wiring.isActiveAndEnabled && wiring.gameObject.scene == scene && (wiringInstanceId == 0 || wiring.GetInstanceID() == wiringInstanceId)) matches.Add(wiring);
                Need(matches.Count == 1, "Select exactly one active source CombatLoopWiring via Start(instanceId); found " + matches.Count + ".");
                _source = matches[0]; _sourceAdapter = Get<BrushStrokeFeedAdapter>(_source, "_brushAdapter");
                Need(_sourceAdapter != null && _sourceAdapter.isActiveAndEnabled, "Source brush adapter must be active and wired.");
                _config = Get<CombatConfigSO>(_source, "_config");
                _book = Get<SpellBookSO>(_sourceAdapter, "_spellBook");
                _visuals = Get<SpellVisualSetSO>(_sourceAdapter, "_visualSet");
                var resolver = Get<SpellResolver>(_source, "_resolver");
                Need(_config != null && _book != null && _visuals != null && resolver != null, "Source config/book/visual set or injected resolver is missing.");
                Need(ReferenceEquals(_book, Get<SpellBookSO>(resolver, "_book")), "Source adapter and real resolver read different books.");
                Need(_config == Get<CombatConfigSO>(_sourceAdapter, "_combatConfig"), "Source adapter and combat wiring use different clocks/configs.");
                Need(Get<BrushStyleSO>(_sourceAdapter, "_style") != null, "Source style is missing.");
                _report.sourceWiring = _source.name + "#" + _source.GetInstanceID();
                _report.sourceAdapter = _sourceAdapter.name + "#" + _sourceAdapter.GetInstanceID();
                _report.sourceVisualSet = AssetDatabase.GetAssetPath(_visuals);
                Snapshot(_config); Snapshot(_book); Snapshot(_visuals); Snapshot(Get<BrushStyleSO>(_sourceAdapter, "_style"));
                foreach (char glyph in Glyphs)
                {
                    Need(_book.TryGet(glyph, out _), "Missing gameplay spell " + glyph);
                    Need(_visuals.TryGet(glyph, out var visual), "Missing source visual map " + glyph);
                    var components = visual.FxPrefab.GetComponentsInChildren<SpellSequenceEffect>(true);
                    Need(components.Length == 1 && components[0] is Vfx120Effect, "Map is not exactly one new VFX root: " + glyph);
                    var effect = (Vfx120Effect)components[0];
                    Need(effect.Profile != null && effect.Profile.Glyph == glyph.ToString(), "Profile glyph mismatch: " + glyph);
                    Need(effect.transform == visual.FxPrefab.transform, "Authored effect must own the prefab root: " + glyph);
                    Need(visual.FxPrefab.GetComponentsInChildren<PatternEffectLifetime>(true).Length == 0, "Legacy lifetime on new prefab " + glyph);
                    Snapshot(effect.Profile); Snapshot(visual.FxPrefab);
                }
                Application.logMessageReceived += OnLog;
                _report.stage = "NEXT_CASE";
            }
            catch (Exception e) { Finish("FAIL_PREFLIGHT", e.GetBaseException().Message); }
            Save(); return Poll();
        }

        public static string Poll()
        {
            if (_report == null) return "{\"status\":\"NOT_STARTED\"}";
            return JsonUtility.ToJson(new Progress
            {
                status = _report.status, stage = _report.stage, glyph = _fixture?.row.glyph, output = Output,
                completed = _report.completed, failed = _report.failed, errors = _report.errors,
                cleanupCompleted = _report.cleanupCompleted,
                elapsedSeconds = (DateTime.UtcNow.Ticks - _report.startedUtcTicks) / (double)TimeSpan.TicksPerSecond
            });
        }
        public static string Cancel() { if (Active) Abort("Cancelled by caller."); return Poll(); }

        private static void Tick()
        {
            if (!Active) return;
            if (!Application.isPlaying || SceneManager.GetActiveScene().path != _report.scene) { Abort("Scene/Play state changed."); return; }
            if ((DateTime.UtcNow.Ticks - _report.startedUtcTicks) / (double)TimeSpan.TicksPerSecond > 90) { Abort("90-second wall-clock limit reached; remaining cases are unverified."); return; }
            if (EditorApplication.isPaused || _lastFrame == Time.frameCount) return;
            _lastFrame = Time.frameCount;
            try
            {
                if (_report.stage == "NEXT_CASE")
                {
                    if (_next >= Glyphs.Length) { Finish(_report.failed == 0 && _report.errors == 0 ? "PASS_RECOGNIZED_EVENT_RUNTIME_FIXTURE_ONLY" : "FAIL_RUNTIME_FIXTURE", null); return; }
                    char glyph = Glyphs[_next++];
                    _report.cleanupCompleted = false; _report.cleanupStatus = "ACTIVE_FIXTURE";
                    _fixture = new Fixture(glyph); _report.cases.Add(_fixture.row);
                    _waitFrame = Time.frameCount + 2; _report.stage = "WAIT_START"; Save();
                }
                else if (_report.stage == "WAIT_START" && Time.frameCount >= _waitFrame)
                {
                    _fixture.Commit(); _waitFrame = Time.frameCount + 2; _report.stage = "WAIT_SPAWN";
                }
                else if (_report.stage == "WAIT_SPAWN" && Time.frameCount >= _waitFrame)
                {
                    _fixture.InspectSpawn(); _report.stage = "WAIT_IMPACTS_AND_LIFETIME"; Save();
                }
                else if (_report.stage == "WAIT_IMPACTS_AND_LIFETIME")
                {
                    _fixture.Observe();
                    if (Time.time >= _fixture.finishAt)
                    {
                        _fixture.ValidateFinish(); EndCase();
                    }
                }
                else if (_report.stage == "CLEANUP_FRAME" && Time.frameCount >= _waitFrame)
                {
                    if (ObserveCleanup()) { _cleanupPending.Clear(); _report.stage = "NEXT_CASE"; Save(); }
                    else if (EditorApplication.timeSinceStartup - _cleanupRequestedAt > 2)
                        Abort("Fixture cleanup was not observed complete within two seconds; remaining objects/discovery are unverified.");
                }
            }
            catch (Exception e)
            {
                if (_fixture == null) { Abort(e.GetBaseException().Message); return; }
                _fixture.row.error = e.GetBaseException().Message; _fixture.row.status = "FAIL"; EndCase();
            }
        }

        private static void EndCase()
        {
            if (_fixture.row.status == "RUNNING") _fixture.row.status = "PASS_EVENT_TO_RUNTIME_SPAWN_AND_CLOCKS";
            if (_fixture.row.status != "PASS_EVENT_TO_RUNTIME_SPAWN_AND_CLOCKS") _report.failed++;
            _report.completed++; _fixture.Dispose(); _fixture = null;
            _report.stage = "CLEANUP_FRAME"; _waitFrame = Time.frameCount + 2; Save();
        }

        private sealed class Fixture : IDisposable
        {
            public readonly CaseRow row;
            public float finishAt;
            private readonly GameObject root;
            private readonly DrawingInputController input;
            private readonly BrushStrokeFeedAdapter adapter;
            private readonly CombatLoopWiring wiring;
            private readonly LockOn lockOn;
            private readonly Transform player;
            private readonly EnemyVitals[] targets = new EnemyVitals[3];
            private readonly DrawnLetterEventChannelSO letterChannel;
            private readonly FloatEventChannelSO inkChannel;
            private readonly InkPool ink;
            private readonly ParryJudge judge;
            private readonly SpellBookSO.Entry spell;
            private readonly Vfx120Profile expectedProfile;
            private readonly List<ActualHit> actual = new List<ActualHit>();
            private readonly List<GameObject> ownedEffects = new List<GameObject>();
            private readonly HashSet<int> beforePatterns = new HashSet<int>();
            private CastPlan plan;
            private string planBefore;
            private Vfx120Effect effect;
            private bool disposed, committedEventSubmitted;
            private float lastEffectFrameDelta;
            private readonly bool[] capturedPhases = new bool[3];
            private int lastCaptureFrame = -1;
            private RenderTexture captureTarget;
            private Texture2D captureReadback;

            public Fixture(char glyph)
            {
                _book.TryGet(glyph, out spell); _visuals.TryGet(glyph, out var visual);
                expectedProfile = visual.FxPrefab.GetComponent<Vfx120Effect>().Profile;
                row = new CaseRow { glyph = glyph.ToString(), kind = spell.Kind.ToString(), shape = spell.AreaShape.ToString(), prefab = AssetDatabase.GetAssetPath(visual.FxPrefab), profile = AssetDatabase.GetAssetPath(expectedProfile) };
                if (_report.captureFrames) row.captureStatus = "RUNNING_UNVERIFIED";
                try
                {
                root = new GameObject("Vfx120GameplayAudit_" + glyph) { hideFlags = HideFlags.DontSave };
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, _source.gameObject.scene);
                root.transform.SetPositionAndRotation(_sourceAdapter.transform.position, _sourceAdapter.transform.rotation);
                letterChannel = ScriptableObject.CreateInstance<DrawnLetterEventChannelSO>(); letterChannel.hideFlags = HideFlags.DontSave;
                inkChannel = ScriptableObject.CreateInstance<FloatEventChannelSO>(); inkChannel.hideFlags = HideFlags.DontSave;
                var inputObject = Child("InactiveInputEventFixture"); inputObject.SetActive(false);
                input = inputObject.AddComponent<DrawingInputController>(); // Awake never runs; no device actions are enabled.
                adapter = root.AddComponent<BrushStrokeFeedAdapter>();
                CopySerialized(_sourceAdapter, adapter);
                Set(adapter, "_input", input); Set(adapter, "_letterDrawn", letterChannel);
                lockOn = Child("FixtureLockOn").AddComponent<LockOn>(); Set(lockOn, "_config", _config);
                player = Child("FixturePlayerOrigin").transform;
                var cam = Camera.main;
                Vector3 forward = AreaGeometry.Flat(cam.transform.forward).normalized;
                Need(forward.sqrMagnitude > .5f, "Near-vertical camera cannot define the test corridor.");
                Vector3 first = cam.transform.position + cam.transform.forward * 6f;
                player.SetPositionAndRotation(first - forward * 6f, Quaternion.LookRotation(forward));
                for (int i = 0; i < targets.Length; i++)
                {
                    targets[i] = Child("FixtureTarget" + i).AddComponent<EnemyVitals>();
                    Set(targets[i], "_config", _config);
                    targets[i].transform.position = first + (i == 0 ? Vector3.zero : player.right * (i == 1 ? -.6f : .6f) + forward * .65f);
                }
                var wiringObject = Child("ProductionCombatLoopWiring"); wiring = wiringObject.AddComponent<CombatLoopWiring>();
                Set(wiring, "_letterDrawn", letterChannel); Set(wiring, "_inkChanged", inkChannel);
                Set(wiring, "_drawingInput", input); Set(wiring, "_brushAdapter", adapter); Set(wiring, "_config", _config);
                Set(wiring, "_enemies", targets); Set(wiring, "_playerTransform", player); Set(wiring, "_lockOn", lockOn);
                Set(wiring, "_freeAimAngle", Get<float>(_source, "_freeAimAngle")); Set(wiring, "_freeAimRange", Get<float>(_source, "_freeAimRange"));
                ink = new InkPool(_config, inkChannel); judge = new ParryJudge(_config);
                wiring.Construct(new SpellResolver(_book), judge, new GroggyMeter(_config), ink);
                wiring.CastPlanned += captured =>
                {
                    row.plannedEvents++; plan = captured; planBefore = AreaText(captured.Area);
                    row.areaAtCast = planBefore;
                };
                root.SetActive(true);
                foreach (var target in targets)
                {
                    var tracked = target; float previousHp = tracked.Hp01 * _config.EnemyMaxHp;
                    tracked.HpChanged += () =>
                    {
                        float currentHp = tracked.Hp01 * _config.EnemyMaxHp;
                        actual.Add(new ActualHit { target = tracked, time = Time.time, damage = previousHp - currentHp, frameDelta = Time.deltaTime });
                        previousHp = currentHp;
                    };
                }
                _report.isolatedServices = !ReferenceEquals(ink, Get<InkPool>(_source, "_ink")) && letterChannel != Get<DrawnLetterEventChannelSO>(_source, "_letterDrawn");
                Need(_report.isolatedServices, "Fixture unexpectedly shares live gameplay services.");
                }
                catch { Dispose(); throw; }
            }

            private GameObject Child(string name)
            {
                var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(root.transform, false); return go;
            }

            public void Commit()
            {
                // Public production selector; no private target/health/guard state is forced.
                lockOn.Toggle(); Need(lockOn.Target != null, "Fixture target is outside the current camera/lock-on bounds.");
                foreach (var go in Patterns()) beforePatterns.Add(go.GetInstanceID());
                Rect rect = Camera.main.pixelRect;
                Need(rect.width > 0 && rect.height > 0, "Main camera has no viewport.");
                for (int stroke = 0; stroke < 2; stroke++)
                {
                    RaiseInput("StrokeStarted");
                    for (int p = 0; p < 8; p++)
                    {
                        float u = p / 7f;
                        Vector2 point = new Vector2(rect.x + rect.width * (.42f + .15f * u), rect.y + rect.height * (.43f + .13f * (stroke == 0 ? u : 1f - u)));
                        RaiseInput("StrokePointAdded", point);
                    }
                    RaiseInput("StrokeEnded");
                }
                row.commitTime = Time.time; row.commitFrame = Time.frameCount; row.timeScale = Time.timeScale; row.inkBefore = ink.Value;
                int index = Glyphs.IndexOf(row.glyph, StringComparison.Ordinal);
                Jamo initial = new[] { Jamo.Giyeok, Jamo.Nieun, Jamo.Mieum, Jamo.Siot, Jamo.Ieung }[index % 5];
                Jamo medial = index < 5 ? Jamo.A : index < 10 ? Jamo.O : Jamo.Eo;
                letterChannel.Raise(new DrawnLetter(row.glyph[0], initial, medial, null, .8f, .75f, 1.2f, 1.4f, 2));
                committedEventSubmitted = true;
                RaiseInput("Committed", true);
                RaiseInput("ModeExited"); // same ordering as successful DrawingInputController exit
                row.inkAfter = ink.Value;
                row.expectedInkAfter = Mathf.Clamp01(row.inkBefore - (spell.Kind == SpellKind.Parry ? _config.ParryInkCost : _config.SpellInkCost));
                Need(Mathf.Abs(row.inkAfter - row.expectedInkAfter) < .00001f, "Actual production ink expenditure differs.");
                if (spell.Kind == SpellKind.Parry)
                {
                    row.guardStart = Get<float>(judge, "_guardStart");
                    Need(Get<bool>(judge, "_hasGuard") && row.guardStart == row.commitTime, "Production judge did not raise guard at commit time.");
                }
                else
                {
                    Need(plan != null && row.plannedEvents == 1 && plan.Cast.Letter == row.glyph[0], "CastPlanned was missing, duplicated or mismatched.");
                    row.expectedHits = plan.Hits.Count;
                    Need(row.expectedHits > 0, "No hit plan reached the isolated live targets; this case cannot verify hit timing.");
                }
            }

            private void RaiseInput(string eventName, params object[] args)
            {
                var callback = Get<Delegate>(input, eventName);
                Need(callback != null, "Production adapter did not subscribe to input event " + eventName);
                callback.DynamicInvoke(args);
            }

            public void InspectSpawn()
            {
                Bounds bounds = StrokeBounds(adapter);
                var candidates = new List<GameObject>();
                foreach (var go in Patterns()) if (!beforePatterns.Contains(go.GetInstanceID())) candidates.Add(go);
                row.spawnedRoots = candidates.Count; row.spawnObservedFrame = Time.frameCount;
                var matches = new List<GameObject>();
                foreach (var go in candidates)
                {
                    var probe = go.GetComponent<Vfx120Effect>();
                    // Area identity/temporary targets distinguish the fixture from concurrent
                    // real-player casts; guards additionally require the expected profile/bounds.
                    if (probe != null && HasOwnContext(probe) && (probe.ReceivedOrigin - bounds.center).sqrMagnitude < .000001f) matches.Add(go);
                }
                if (matches.Count == 1) { ownedEffects.Add(matches[0]); effect = matches[0].GetComponent<Vfx120Effect>(); }
                else if (matches.Count > 1) _cleanupDiscoveryFailed = true;
                Need(candidates.Count == 1 && ownedEffects.Count == 1, "Expected one fixture spawn; found " + candidates.Count + ". Concurrent gameplay or missing/duplicate/legacy spawn confounds this case.");
                row.authoredComponents = effect.GetComponentsInChildren<Vfx120Effect>(true).Length;
                row.legacyComponents = effect.GetComponentsInChildren<PatternEffectLifetime>(true).Length;
                foreach (var sequence in effect.GetComponentsInChildren<SpellSequenceEffect>(true)) if (!(sequence is Vfx120Effect)) row.legacyComponents++;
                Need(row.authoredComponents == 1 && row.legacyComponents == 0, "Old effect/lifetime overlaps new root.");
                row.spawnedGlyph = effect.Profile != null ? effect.Profile.Glyph : null;
                row.profileReferenceMatches = effect.Profile == expectedProfile && row.spawnedGlyph == row.glyph;
                Need(row.profileReferenceMatches, "Spawned profile does not match the source visual map.");
                Need(effect.Begun && !effect.PreviewControlled, "Effect did not begin in actual runtime mode.");
                row.originError = Vector3.Distance(effect.ReceivedOrigin, bounds.center);
                Need(row.originError <= .00001f, "Effect origin differs from real committed stroke bounds.");
                row.receivedImpactClock = effect.ReceivedImpactClock;
                // Read the same fixed source geometry/speed used by the schedule. Subtracting
                // absolute float Time.time loses precision in long-running Editor sessions.
                row.expectedImpactClock = spell.Kind == SpellKind.AttackSingle
                    ? Vector3.Distance(player.position, plan.Hits[0].Target.transform.position) / Mathf.Max(1f, _config.SpellProjectileSpeed * plan.Cast.SpeedMul) : 0f;
                Need(Mathf.Abs(row.receivedImpactClock - row.expectedImpactClock) < .00002f, "Authoritative impact duration was changed or invented.");
                if (spell.Kind == SpellKind.AttackSingle)
                {
                    row.scheduledImpactTime = plan.Hits[0].ImpactTime;
                    // Add the relative clock to the original commit time. This compares the
                    // actual scheduled timestamp without precision loss from subtracting two large times.
                    row.forwardedImpactTime = row.commitTime + row.receivedImpactClock;
                    row.impactScheduleError = Mathf.Abs(row.forwardedImpactTime - row.scheduledImpactTime);
                    Need(row.impactScheduleError <= .00002f, "Forwarded VFX clock differs from the actual scheduled impact timestamp.");
                }
                row.areaReferenceMatches = ReferenceEquals(effect.ReceivedAreaPlan, plan?.Area);
                Need(row.areaReferenceMatches, "AreaImpactPlan identity was not forwarded unchanged.");
                Transform expectedTarget = spell.Kind == SpellKind.Parry || spell.AreaShape == AreaShape.Cone ? null
                    : spell.AreaShape == AreaShape.Volley ? plan.Area.Shots[0].Target.transform : lockOn.Target.transform;
                row.targetMatches = effect.ReceivedTarget == expectedTarget;
                Need(row.targetMatches, "Target differs from production cast/area plan.");
                row.receivedGuardClock = effect.ReceivedGuardClock; row.receivedBrightWindow = effect.ReceivedGuardBrightWindow;
                row.guardMatches = row.receivedGuardClock == (spell.Kind == SpellKind.Parry ? _config.GuardDuration : 0f)
                    && row.receivedBrightWindow == (spell.Kind == SpellKind.Parry ? _config.ParryWindow : 0f);
                Need(row.guardMatches, "Guard or bright-window clock changed/leaked across casts.");
                row.pendingContextCleared = Get<Transform>(adapter, "_pendingAttackTarget") == null && Get<float>(adapter, "_pendingAttackDuration") == 0f
                    && Get<AreaImpactPlan>(adapter, "_pendingAreaPlan") == null && Get<GameObject>(adapter, "_pendingFxPrefab") == null;
                Need(row.pendingContextCleared, "Adapter kept consumed cast context.");
                row.effectDeclaredLife = effect.Life;
                row.effectStartedAt = Time.time - effect.Age;
                finishAt = row.effectStartedAt + row.effectDeclaredLife + .15f;
                if (plan != null) foreach (var hit in plan.Hits) finishAt = Mathf.Max(finishAt, hit.ImpactTime + .15f);
                Observe();
            }

            public void Observe()
            {
                if (effect == null)
                {
                    row.naturallyDestroyed = true;
                    if (row.firstMissingTime < 0f)
                    {
                        row.firstMissingTime = Time.time;
                        row.missingFrameAllowance = Mathf.Max(Time.deltaTime, lastEffectFrameDelta) + .001f;
                        row.earlyDestructionSeconds = Mathf.Max(0, row.effectStartedAt + row.effectDeclaredLife - row.firstMissingTime);
                        row.lifetimeWithinObservedFrame = row.earlyDestructionSeconds <= row.missingFrameAllowance;
                    }
                    return;
                }
                row.observedUpdateFrames++;
                if (effect.Age > row.lastObservedEffectAge) row.actualUpdateObserved = true;
                row.lastObservedEffectAge = effect.Age;
                lastEffectFrameDelta = Time.deltaTime;
                ObserveCapture();
            }

            private void ObserveCapture()
            {
                if (!_report.captureFrames || effect == null || lastCaptureFrame == Time.frameCount) return;
                int phase = -1;
                float referenceTime = row.effectStartedAt;
                string phaseName = "spawn_observed";
                if (!capturedPhases[0]) phase = 0;
                else
                {
                    bool guard = spell.Kind == SpellKind.Parry;
                    float eventTime = guard ? row.guardStart + row.receivedBrightWindow : float.PositiveInfinity;
                    if (!guard && plan != null)
                        foreach (var hit in plan.Hits) eventTime = Mathf.Min(eventTime, hit.ImpactTime);
                    // A scheduled attack alone does not count as an observed impact. The real
                    // EnemyVitals callback must already have fired; guards have no hit event.
                    bool eventObserved = guard ? Time.time >= eventTime : actual.Count > 0;
                    bool eventDue = !capturedPhases[1] && eventObserved;
                    bool middleDue = !capturedPhases[2] && effect.Age >= effect.Life * .5f;
                    if (eventDue && (!middleDue || eventTime <= row.effectStartedAt + effect.Life * .5f))
                    {
                        phase = 1; referenceTime = eventTime;
                        phaseName = guard ? "guard_bright_window_end_observed" : "first_damage_observed";
                    }
                    else if (middleDue)
                    {
                        phase = 2; referenceTime = row.effectStartedAt + effect.Life * .5f;
                        phaseName = "mid_lifetime_observed";
                    }
                }
                if (phase < 0) return;
                capturedPhases[phase] = true; lastCaptureFrame = Time.frameCount;
                CaptureLiveCamera(phaseName, referenceTime);
            }

            private void CaptureLiveCamera(string phase, float referenceTime)
            {
                var shot = new CaptureRow
                {
                    glyph = row.glyph, phase = phase, frame = Time.frameCount,
                    age = effect.Age, life = effect.Life, time = Time.time, unscaledTime = Time.unscaledTime,
                    timeScale = Time.timeScale, scheduledReferenceTime = referenceTime,
                    observationLateness = Time.time - referenceTime, observedHitCount = actual.Count,
                    effectOrigin = effect.ReceivedOrigin
                };
                row.captures.Add(shot);
                var timer = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var camera = Camera.main;
                    Need(camera != null, "Live main camera disappeared before capture.");
                    shot.camera = camera.name + "#" + camera.GetInstanceID();
                    shot.cameraPosition = camera.transform.position; shot.cameraEulerAngles = camera.transform.eulerAngles;
                    shot.cameraFieldOfView = camera.fieldOfView; shot.cameraAspect = camera.aspect;
                    shot.cameraNear = camera.nearClipPlane; shot.cameraFar = camera.farClipPlane;
                    shot.cameraOrthographic = camera.orthographic;
                    shot.originViewport = camera.WorldToViewportPoint(shot.effectOrigin);
                    if (captureTarget == null)
                    {
                        captureTarget = new RenderTexture(shot.width, shot.height, 24, RenderTextureFormat.ARGB32)
                            { name = "Vfx120GameplayCaptureRT", hideFlags = HideFlags.DontSave };
                        Need(captureTarget.Create(), "Capture render texture could not be created.");
                        captureReadback = new Texture2D(shot.width, shot.height, TextureFormat.RGB24, false)
                            { name = "Vfx120GameplayCaptureReadback", hideFlags = HideFlags.DontSave };
                    }
                    RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
                    // Retain the live camera projection/transform and restore its render target
                    // even if rendering/readback throws. No presentation clocks are advanced here.
                    try
                    {
                        camera.targetTexture = captureTarget;
                        camera.Render();
                        RenderTexture.active = captureTarget;
                        captureReadback.ReadPixels(new Rect(0, 0, shot.width, shot.height), 0, 0);
                        captureReadback.Apply(false, false);
                    }
                    finally
                    {
                        if (camera != null) camera.targetTexture = previousTarget;
                        RenderTexture.active = previousActive;
                        shot.cameraStateRestored = camera != null && camera.targetTexture == previousTarget && RenderTexture.active == previousActive;
                    }
                    Need(shot.cameraStateRestored, "Camera render-target restoration could not be confirmed.");
                    Directory.CreateDirectory(_report.captureDirectory);
                    shot.png = Path.Combine(_report.captureDirectory, row.glyph + "_" + phase + "_f" + shot.frame.ToString("D6") + ".png");
                    File.WriteAllBytes(shot.png, captureReadback.EncodeToPNG());
                    shot.status = "CAPTURED_VISUAL_REVIEW_REQUIRED";
                }
                catch (Exception e)
                {
                    shot.status = "FAILED_CAPTURE_UNVERIFIED"; shot.error = e.GetBaseException().Message;
                    _report.messages.Add("Capture " + row.glyph + "/" + phase + ": " + shot.error);
                }
                finally
                {
                    timer.Stop(); shot.captureWallMilliseconds = timer.Elapsed.TotalMilliseconds;
                    if (!string.IsNullOrEmpty(shot.png)) File.WriteAllText(Path.ChangeExtension(shot.png, ".json"), JsonUtility.ToJson(shot, true));
                }
            }

            public void ValidateFinish()
            {
                Observe(); Need(row.actualUpdateObserved, "No natural Vfx120Effect.Update progression observed.");
                Need(row.naturallyDestroyed, "Effect outlived its declared scaled-time lifetime.");
                Need(row.lifetimeWithinObservedFrame, "Effect disappeared before its declared lifetime (beyond the observed frame allowance).");
                row.actualHits = actual.Count;
                Need(actual.Count == row.expectedHits, "Actual damage callback count differs from CastPlanned.");
                if (plan != null)
                {
                    var unmatched = new List<ActualHit>(actual);
                    var sorted = new List<PlannedHit>(plan.Hits); sorted.Sort((a, b) => a.ImpactTime.CompareTo(b.ImpactTime));
                    foreach (var hit in sorted)
                    {
                        ActualHit match = null;
                        foreach (var observed in unmatched)
                            if (observed.target == hit.Target && (match == null || observed.time < match.time)) match = observed;
                        var record = new HitRow { target = hit.Target.name, plannedTime = hit.ImpactTime, power = hit.Power, observed = match != null };
                        row.hits.Add(record); Need(match != null, "A planned target never received damage."); unmatched.Remove(match);
                        record.actualTime = match.time; record.actualDamage = match.damage; record.frameDelta = match.frameDelta;
                        record.lateSeconds = match.time - hit.ImpactTime;
                        row.maximumHitLateness = Mathf.Max(row.maximumHitLateness, record.lateSeconds);
                        Need(record.lateSeconds >= -.0001f && record.lateSeconds <= match.frameDelta + .001f, "Impact fired early or later than one actual Update frame.");
                        Need(Mathf.Abs(record.actualDamage - hit.Power) < .0001f, "Damage differs from the authoritative plan.");
                    }
                }
                row.areaAtFinish = AreaText(plan?.Area);
                row.areaContentsUnchanged = row.areaAtFinish == planBefore || (plan == null && row.areaAtFinish == "null");
                Need(row.areaContentsUnchanged, "Presentation changed the authoritative area/shot plan.");
                CompleteCaptureStatus();
            }

            private void CompleteCaptureStatus()
            {
                if (!_report.captureFrames) return;
                bool complete = row.captures.Count == 3;
                foreach (var shot in row.captures) complete &= shot.status == "CAPTURED_VISUAL_REVIEW_REQUIRED";
                row.captureStatus = complete ? "THREE_ACTUAL_FRAMES_VISUAL_REVIEW_REQUIRED" : "INCOMPLETE_OR_FAILED_UNVERIFIED";
            }

            public void Dispose()
            {
                if (disposed) return; disposed = true;
                CompleteCaptureStatus();
                // An abort may happen after the real LateUpdate spawned a detached effect but
                // before InspectSpawn registered it. Recover only this fixture's own context.
                RecoverUnobservedEffect();
                TrackHierarchy(root);
                TrackCleanup(letterChannel); TrackCleanup(inkChannel);
                foreach (var owned in ownedEffects) TrackHierarchy(owned);
                if (adapter != null)
                {
                    TrackStrokes(adapter);
                    var fallback = Get<Material>(adapter, "_fallbackMaterial"); TrackCleanup(fallback);
                    adapter.enabled = false; // production adapter owns detached stroke cleanup
                    if (fallback != null) Object.Destroy(fallback);
                }
                if (root != null) { root.SetActive(false); Object.Destroy(root); }
                foreach (var owned in ownedEffects) if (owned != null) Object.Destroy(owned);
                if (letterChannel != null) Object.Destroy(letterChannel);
                if (inkChannel != null) Object.Destroy(inkChannel);
                if (captureTarget != null) { TrackCleanup(captureTarget); captureTarget.Release(); Object.Destroy(captureTarget); }
                if (captureReadback != null) { TrackCleanup(captureReadback); Object.Destroy(captureReadback); }
                _report.cleanupCompleted = false; _report.cleanupStatus = "REQUESTED_UNVERIFIED";
                _cleanupRequestedAt = EditorApplication.timeSinceStartup;
            }

            private void RecoverUnobservedEffect()
            {
                if (!committedEventSubmitted || ownedEffects.Count > 0) return;
                try
                {
                    var matches = new List<GameObject>();
                    Bounds bounds = default; bool hasBounds = false;
                    if (spell.Kind == SpellKind.Parry)
                    {
                        var groups = adapter != null ? Get<IList>(adapter, "_fading") : null;
                        if (groups != null && groups.Count == 1) { bounds = StrokeBounds(adapter); hasBounds = true; }
                    }
                    foreach (var go in Patterns())
                    {
                        if (beforePatterns.Contains(go.GetInstanceID())) continue;
                        var probe = go.GetComponent<Vfx120Effect>();
                        if (probe == null || !HasOwnContext(probe)) continue;
                        bool own = spell.Kind != SpellKind.Parry || (hasBounds && (probe.ReceivedOrigin - bounds.center).sqrMagnitude < .000001f);
                        if (own) matches.Add(go);
                    }
                    if (matches.Count == 1) ownedEffects.Add(matches[0]);
                    else if (matches.Count > 1)
                    {
                        _cleanupDiscoveryFailed = true;
                        _report.messages.Add("Ambiguous detached FX ownership; no potentially unrelated gameplay object was deleted.");
                    }
                    else if (spell.Kind == SpellKind.Parry && !hasBounds)
                    {
                        _cleanupDiscoveryFailed = true;
                        _report.messages.Add("Interrupted guard fixture has no retained stroke bounds; detached FX cleanup remains unverified.");
                    }
                }
                catch (Exception e)
                {
                    _cleanupDiscoveryFailed = true;
                    _report.messages.Add("Detached FX recovery could not be verified: " + e.GetBaseException().Message);
                }
            }

            private bool HasOwnContext(Vfx120Effect probe)
            {
                if (plan?.Area != null) return ReferenceEquals(probe.ReceivedAreaPlan, plan.Area);
                if (plan != null && plan.Hits.Count > 0) return probe.ReceivedTarget == plan.Hits[0].Target.transform;
                return spell.Kind == SpellKind.Parry && probe.Profile == expectedProfile && probe.ReceivedTarget == null && probe.ReceivedAreaPlan == null;
            }
        }

        private static void TrackCleanup(Object value)
        {
            if (value == null || EditorUtility.IsPersistent(value)) return;
            foreach (var prior in _cleanupPending) if (ReferenceEquals(prior, value)) return;
            _cleanupPending.Add(value); _report.cleanupTrackedObjects++;
        }
        private static void TrackHierarchy(GameObject root)
        {
            if (root == null) return;
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) TrackCleanup(child.gameObject);
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) TrackCleanup(ps);
        }
        private static void TrackStrokes(BrushStrokeFeedAdapter adapter)
        {
            void Track(BrushStrokeRenderer stroke)
            {
                if (stroke == null) return;
                TrackHierarchy(stroke.gameObject); TrackCleanup(stroke.OwnedMaterial);
                var filter = stroke.GetComponent<MeshFilter>(); if (filter != null) TrackCleanup(filter.sharedMesh);
            }
            foreach (var stroke in Get<List<BrushStrokeRenderer>>(adapter, "_strokes")) Track(stroke);
            foreach (var group in Get<IList>(adapter, "_fading"))
                foreach (var stroke in (IEnumerable<BrushStrokeRenderer>)group.GetType().GetField("Strokes").GetValue(group)) Track(stroke);
        }
        private static bool ObserveCleanup()
        {
            int residual = 0;
            foreach (var value in _cleanupPending) if (value != null) residual++;
            _report.cleanupResidualObjects = residual;
            _report.cleanupCompleted = residual == 0 && !_cleanupDiscoveryFailed;
            _report.cleanupStatus = _report.cleanupCompleted ? "OBSERVED_ZERO_TRACKED_RESIDUALS"
                : _cleanupDiscoveryFailed ? "UNVERIFIED_DETACHED_FX_DISCOVERY" : "REQUESTED_UNVERIFIED";
            return _report.cleanupCompleted;
        }

        private static Bounds StrokeBounds(BrushStrokeFeedAdapter adapter)
        {
            var groups = Get<IList>(adapter, "_fading"); Need(groups.Count == 1, "Fixture stroke/commit grouping differs.");
            var strokes = (IEnumerable<BrushStrokeRenderer>)groups[0].GetType().GetField("Strokes").GetValue(groups[0]);
            var bounds = new Bounds(); bool has = false;
            foreach (var stroke in strokes) foreach (var point in stroke.Data.Points)
            {
                Vector3 world = stroke.transform.TransformPoint(point.Position);
                if (!has) { bounds = new Bounds(world, Vector3.zero); has = true; } else bounds.Encapsulate(world);
            }
            Need(has, "No actual committed stroke points."); return bounds;
        }
        private static IEnumerable<GameObject> Patterns()
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && !EditorUtility.IsPersistent(go) && go.scene.IsValid() && go.scene.path == _report.scene && go.name == "SpellPattern") yield return go;
        }
        private static void CopySerialized(object source, object destination)
        {
            foreach (var field in source.GetType().GetFields(Hidden))
                if (field.IsDefined(typeof(SerializeField), true)) field.SetValue(destination, field.GetValue(source));
        }
        private static T Get<T>(object target, string field)
        {
            Need(target != null, "Missing target for " + field);
            var info = target.GetType().GetField(field, Hidden); Need(info != null, "Diagnostic field moved: " + field);
            return (T)info.GetValue(target);
        }
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
        private static string AreaText(AreaImpactPlan area) => area == null ? "null" : JsonUtility.ToJson(area);
        private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static string FileHash(string path)
        {
            if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal))
                path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "MISSING";
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        private static void Snapshot(Object asset)
        {
            if (asset == null) return;
            foreach (var prior in _assets) if (prior.asset == asset) return;
            string path = AssetDatabase.GetAssetPath(asset);
            var row = new AssetRow { path = path, before = FileHash(path), memoryBefore = EditorJsonUtility.ToJson(asset) };
            _assets.Add(new AssetSnapshot { asset = asset, row = row }); _report.assets.Add(row);
        }
        private static void OnLog(string message, string stack, LogType type)
        {
            if (!Active || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
            _report.errors++; if (_report.messages.Count < 40) _report.messages.Add(type + ": " + message);
        }
        private static void Abort(string reason) => Finish("ABORTED", reason);
        private static void Finish(string status, string reason)
        {
            if (_report == null) return;
            if (reason != null) _report.messages.Add(reason);
            if (_fixture != null)
            {
                if (_fixture.row.status == "RUNNING") { _fixture.row.status = "UNVERIFIED_INTERRUPTED"; _fixture.row.error = reason; }
                _fixture.Dispose(); _fixture = null;
            }
            Application.logMessageReceived -= OnLog;
            _report.status = status; _report.stage = "FINISHED";
            if (_report.captureFrames)
            {
                bool complete = _report.cases.Count == Glyphs.Length;
                foreach (var row in _report.cases) complete &= row.captureStatus == "THREE_ACTUAL_FRAMES_VISUAL_REVIEW_REQUIRED";
                _report.captureStatus = complete ? "CAPTURED_45_ACTUAL_FRAMES_VISUAL_REVIEW_REQUIRED" : "INCOMPLETE_OR_FAILED_UNVERIFIED";
            }
            ObserveCleanup(); // Destroy is deferred; a request alone must never mean cleanup PASS.
            _report.sceneHashAfter = FileHash(_report.scene); _report.sceneFileUnchanged = _report.sceneHashBefore == _report.sceneHashAfter;
            _report.assetsUnchanged = true;
            foreach (var item in _assets)
            {
                item.row.after = FileHash(item.row.path); item.row.memoryAfter = item.asset != null ? EditorJsonUtility.ToJson(item.asset) : "DESTROYED";
                item.row.unchanged = item.row.before == item.row.after && item.row.memoryBefore == item.row.memoryAfter;
                if (!item.row.unchanged) _report.assetsUnchanged = false;
            }
            if (!_report.sceneFileUnchanged || !_report.assetsUnchanged)
            {
                _report.messages.Add("Scene or source asset changed during the test; external activity may confound preservation.");
                if (status.StartsWith("PASS", StringComparison.Ordinal)) _report.status = "FAIL_OR_CONFOUNDED_SOURCE_CHANGED";
            }
            Save();
        }
        private static void Save()
        {
            if (_report == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            string json = JsonUtility.ToJson(_report, true); File.WriteAllText(Output, json);
            if (_report.captureFrames && !string.IsNullOrEmpty(_report.captureDirectory))
            {
                Directory.CreateDirectory(_report.captureDirectory);
                File.WriteAllText(Path.Combine(_report.captureDirectory, "capture_manifest.json"), json);
            }
            SessionState.SetString(StateKey, JsonUtility.ToJson(_report));
        }
    }
}
