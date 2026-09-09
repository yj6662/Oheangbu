using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using Oheangbu.Drawing;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.LowLevel;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // Actual C2 player/DI/input pipeline. No rig, target, channel, resolver, or spell
    // fixture is made, and no recognition result, Commit, Begin or Sample is invoked.
    // These are replayed registered-template strokes, not a handwriting accuracy study.
    public static class Vfx120PlayerInputAudit
    {
        private const string ScenePath = "Assets/_Project/Scenes/Dev/C2_CodexWorld.unity";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private struct InputStage { }
        private struct ObserveStage { }
        [Serializable] private sealed class CaseRow
        {
            public string glyph, recognized, kind, status = "RUNNING", templates, issue, clockMode;
            public int rawPoints, rawStrokes, diagnostics, channelEvents, plans, spawned, remainingEffects;
            public float inkBefore, inkAfter, impactClock, guardClock, life, commitTime;
            public float plannedAt, expectedImpactClock, expectedFlight, observedFlight;
            public bool recognitionSuccess, newVfxProfile, clockMatches;
        }
        [Serializable] private sealed class Report
        {
            public string status = "RUNNING", phase, scene = ScenePath;
            public string scope = "Virtual Mouse/Keyboard -> existing DrawingInputController.Update -> real RecognitionPipeline -> existing LetterDrawn channel/DI CombatLoopWiring -> existing adapter LateUpdate/SpawnPattern -> native VFX Update/destruction. Registered XML strokes are scaled into screen space; recognition results are never supplied by this audit.";
            public string limitations = "UNVERIFIED: general handwriting accuracy, physical hardware delivery, damage/AI/projectile interception/parry outcomes when C2 has no configured enemies, all 15 glyphs, GPU/CPU performance, art quality. Real casts consume the existing InkPool; ink/gameplay state is not reset or replaced. Camera changes caused by the existing drawing/shoulder logic are observed, not driven by the audit.";
            public string cleanupStatus = "NOT_REQUESTED", fileHashBefore, fileHashAfter, output;
            public bool diObserved, sharedWiringObserved, sceneFileUnchanged, inputFilterRestored;
            public bool virtualDevicesRemoved, loopRemoved, drawingExited, cameraPropertiesRestored;
            public bool inputSettingsRestored, currentDevicesRestored, actionMapStatesRestored;
            public bool drawingBindingsRestored;
            public string drawingBindingsBefore, drawingBindingsAfter;
            public bool cameraPoseReturned, cursorRestored, timeScaleReturned;
            public int remainingInputStages, remainingObserveStages;
            public string recoveryMeaning = "Cleanup requires exact raw/effective nullable device filters (null is not an empty list), Drawing control bindings, virtual device removal, original current devices/settings/map states, and both audit PlayerLoop stages absent from the installed loop. Drawing exit and effect destruction must be observed. Camera pose/FOV and timeScale return are checked separately; no character/camera pose, ink, or game clock is forcibly rewound.";
            public int configuredTargets, errors, failed, completed, residualEffects;
            public float cameraPositionDelta, cameraAngleDelta, cameraFovDelta, inkBefore, inkAfter;
            public List<CaseRow> cases = new List<CaseRow>();
            public List<string> messages = new List<string>();
            public List<FilterRow> deviceFilters = new List<FilterRow>();
        }
        [Serializable] private sealed class FilterRow
        {
            // null and [] are intentionally distinct: null inherits/allows devices;
            // [] explicitly allows none. A map's effective getter can inherit asset.devices.
            public string owner, beforeRaw, beforeEffective, afterRaw, afterEffective;
            public bool rawRestored, effectiveRestored;
        }
        private struct Step { public Vector2 point; public bool q, stroke; }
        private sealed class Filter
        {
            public InputActionMap map;
            public InputDevice[] devices, effectiveDevices;
            public bool enabled;
            public FilterRow row;
        }
        private static Report _report;
        private static DrawingInputController _input;
        private static BrushStrokeFeedAdapter _adapter;
        private static CombatLoopWiring _wiring;
        private static DrawnLetterEventChannelSO _channel;
        private static InputActionAsset _actions;
        private static SpellVisualSetSO _visuals;
        private static SpellBookSO _book;
        private static CombatConfigSO _config;
        private static InkPool _ink;
        private static Camera _camera;
        private static Keyboard _keyboard, _oldKeyboard;
        private static Mouse _mouse, _oldMouse;
        private static InputDevice[] _assetDevices;
        private static FilterRow _assetFilterRow;
        private static readonly List<Filter> Filters = new List<Filter>();
        private static readonly List<Vfx120Effect> Seen = new List<Vfx120Effect>();
        private static readonly HashSet<int> SeenIds = new HashSet<int>();
        private static List<Step> _steps;
        private static CaseRow _row;
        private static Vfx120Profile _expected;
        private static CastPlan _plan;
        private static Vector3 _cameraPosition;
        private static Quaternion _cameraRotation;
        private static float _cameraFov, _timeScale;
        private static int _cameraMask, _index, _stepIndex, _lastObservedFrame, _quietFrames;
        private static RenderTexture _cameraTarget;
        private static CursorLockMode _cursorLock;
        private static bool _cursorVisible, _background, _running, _hooked, _inputCaptured, _cancel;
        private static InputSettings.BackgroundBehavior _backgroundBehavior;
        private static InputSettings.EditorInputBehaviorInPlayMode _editorInput;
        private static double _started, _phaseAt, _nextStep;
        private static Step _held;

        public static string Run() => Start();
        public static string Start()
        {
            if (_running) return Poll();
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                return "BLOCKED: enter unpaused C2 Play first; this audit does not open or save scenes.";
            _report = new Report { output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/SpellVFX120/player_input_audit.json")) };
            Seen.Clear(); SeenIds.Clear(); Filters.Clear(); _index = 0; _cancel = false;
            _hooked = _inputCaptured = false; _row = null; _lastObservedFrame = -1;
            try
            {
                var inputs = Object.FindObjectsByType<DrawingInputController>(FindObjectsSortMode.None)
                    .Where(x => x.isActiveAndEnabled && x.gameObject.scene.path == ScenePath).ToArray();
                Require(inputs.Length == 1, "Expected exactly one active C2 DrawingInputController.");
                _input = inputs[0]; Require(!_input.InDrawMode, "Player is already drawing; do not interrupt user input.");
                _wiring = Object.FindObjectsByType<CombatLoopWiring>(FindObjectsSortMode.None)
                    .SingleOrDefault(x => x.isActiveAndEnabled && x.gameObject.scene == _input.gameObject.scene && Get<DrawingInputController>(x, "_drawingInput") == _input);
                Require(_wiring != null, "Existing player CombatLoopWiring is missing or ambiguous.");
                _adapter = Get<BrushStrokeFeedAdapter>(_wiring, "_brushAdapter");
                _actions = Get<InputActionAsset>(_input, "_actions"); _channel = Get<DrawnLetterEventChannelSO>(_input, "_letterDrawn");
                _ink = Get<InkPool>(_wiring, "_ink"); _config = Get<CombatConfigSO>(_wiring, "_config");
                Require(_adapter != null && _adapter.isActiveAndEnabled && _actions != null && _channel != null, "Input/adapter/actions/channel unavailable.");
                _report.diObserved = Get<object>(_wiring, "_resolver") != null && Get<object>(_wiring, "_judge") != null && _ink != null;
                _report.sharedWiringObserved = Get<DrawingInputController>(_adapter, "_input") == _input
                    && Get<DrawnLetterEventChannelSO>(_adapter, "_letterDrawn") == _channel
                    && Get<DrawnLetterEventChannelSO>(_wiring, "_letterDrawn") == _channel;
                Require(_report.diObserved && _report.sharedWiringObserved && _config != null, "Actual DI/shared channel wiring failed; no substitute is created.");
                _visuals = Get<SpellVisualSetSO>(_adapter, "_visualSet"); _book = Get<SpellBookSO>(_adapter, "_spellBook");
                Require(_visuals != null && _book != null, "Existing visual set or spell book is unassigned.");
                Require(_actions.FindActionMap("Drawing", true).enabled, "Existing Drawing action map is disabled.");
                foreach (var glyph in "가노머")
                {
                    Require(_book.TryGet(glyph, out var entry), "Existing book misses " + glyph);
                    Require(_visuals.TryGet(glyph, out var visual) && visual.FxPrefab.GetComponent<Vfx120Effect>()?.Profile?.Glyph == glyph.ToString(), "New VFX mapping missing: " + glyph);
                }
                Require(_ink.Value >= _config.SpellInkCost * 2 + _config.ParryInkCost, "Insufficient existing ink for three casts; no refill is injected.");
                Require(!Object.FindObjectsByType<Vfx120Effect>(FindObjectsSortMode.None).Any(), "Wait for existing VFX to expire first.");
                _camera = Camera.main; Require(_camera != null && _camera.gameObject.scene == _input.gameObject.scene, "Actual C2 MainCamera unavailable.");
                _report.configuredTargets = (Get<EnemyVitals[]>(_wiring, "_enemies") ?? Array.Empty<EnemyVitals>()).Count(x => x != null)
                    + (Get<EnemyVitals>(_wiring, "_enemyVitals") != null ? 1 : 0);
                _report.fileHashBefore = HashScene(); _report.inkBefore = _ink.Value;
                _cameraPosition = _camera.transform.position; _cameraRotation = _camera.transform.rotation;
                _cameraFov = _camera.fieldOfView; _cameraMask = _camera.cullingMask; _cameraTarget = _camera.targetTexture;
                _timeScale = Time.timeScale; _cursorLock = Cursor.lockState; _cursorVisible = Cursor.visible;
                CaptureInput();
                _input.CommitDiagnosed += Diagnosed; _input.StrokePointAdded += Point; _input.StrokeStarted += Stroke;
                _channel.Subscribe(Letter); _wiring.CastPlanned += Planned; _hooked = true;
                var loop = PlayerLoop.GetCurrentPlayerLoop();
                Require(Insert(ref loop, typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate), typeof(InputStage), InputTick, false)
                    && Insert(ref loop, typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate), typeof(ObserveStage), Observe, true), "Required player-loop anchors missing.");
                PlayerLoop.SetPlayerLoop(loop);
                EditorApplication.playModeStateChanged += PlayChanged; AssemblyReloadEvents.beforeAssemblyReload += Reloading;
                Application.logMessageReceived += Logged;
                _running = true; _started = _phaseAt = EditorApplication.timeSinceStartup;
                _report.phase = "SETTLE"; _held = new Step { point = new Vector2(Screen.width * .5f, Screen.height * .5f) };
                return Poll();
            }
            catch (Exception e) { _report.messages.Add(e.Message); Finish("BLOCKED_PRECONDITION"); return Poll(); }
        }

        public static string Poll() => _report == null ? "NOT_STARTED" : JsonUtility.ToJson(_report, true);
        public static string Cancel()
        {
            if (_running) { _cancel = true; _report.messages.Add("Cancellation requested; clear only the pending letter through InterruptLetter, then release virtual controls and observe exit."); }
            return Poll();
        }
        private static void CaptureInput()
        {
            _assetDevices = SnapshotDevices(_actions.devices);
            _assetFilterRow = new FilterRow { owner = "Asset/" + _actions.name, beforeRaw = DeviceIds(_assetDevices), beforeEffective = DeviceIds(_assetDevices) };
            _report.deviceFilters.Add(_assetFilterRow);
            foreach (var map in _actions.actionMaps)
            {
                // InputActionMap.devices returns m_Devices.Get() ?? asset.devices.
                // Snapshot its own filter separately so restoring an inherited filter
                // does not accidentally turn it into a persistent map-local override.
                var raw = RawMapDevices(map); var effective = SnapshotDevices(map.devices);
                var row = new FilterRow { owner = "Map/" + map.name, beforeRaw = DeviceIds(raw), beforeEffective = DeviceIds(effective) };
                Filters.Add(new Filter { map = map, devices = raw, effectiveDevices = effective, enabled = map.enabled, row = row });
                _report.deviceFilters.Add(row);
            }
            _report.drawingBindingsBefore = DrawingBindingSignature();
            Require(_actions.FindActionMap("Drawing", true).actions.All(a => a.controls.Count > 0),
                "Original Drawing actions have missing controls, possibly a stale empty device filter. Reload/restart C2 before this audit; no pre-existing filter is guessed or cleared.");
            _oldKeyboard = Keyboard.current; _oldMouse = Mouse.current;
            _backgroundBehavior = InputSystem.settings.backgroundBehavior; _editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            _background = Application.runInBackground; _inputCaptured = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Application.runInBackground = true;
            _keyboard = InputSystem.AddDevice<Keyboard>("Vfx120AuditKeyboard"); _mouse = InputSystem.AddDevice<Mouse>("Vfx120AuditMouse");
            var devices = new InputDevice[] { _keyboard, _mouse };
            _actions.devices = devices;
            foreach (var filter in Filters) filter.map.devices = devices;
            // Physical devices stay enabled; only the existing action asset is scoped.
            // No bindings, map-enable flags, character transforms or camera fields change.
        }
        private static void InputTick()
        {
            if (!_running) return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - _started > 90) { _cancel = true; _report.messages.Add("90-second bounded timeout."); }
                if (_cancel && _report.phase != "CANCEL")
                {
                    if (_input.InDrawMode) _input.InterruptLetter();
                    _held.q = _held.stroke = false; _report.phase = "CANCEL"; _phaseAt = now;
                }
                if (_report.phase == "DRAW" && now >= _nextStep)
                {
                    _held = _steps[_stepIndex++]; _nextStep = now + .025;
                    if (_stepIndex == _steps.Count) { _report.phase = "WAIT"; _phaseAt = now; _quietFrames = 0; }
                }
                InputSystem.QueueStateEvent(_keyboard, _held.q ? new KeyboardState(Key.Q) : new KeyboardState());
                InputSystem.QueueStateEvent(_mouse, new MouseState { position = _held.point, delta = Vector2.zero }.WithButton(MouseButton.Left, _held.stroke));
                InputSystem.Update(); _keyboard.MakeCurrent(); _mouse.MakeCurrent();
            }
            catch (Exception e) { _report.messages.Add(e.ToString()); _cancel = true; }
        }
        private static void Observe()
        {
            if (!_running || _lastObservedFrame == Time.frameCount) return;
            _lastObservedFrame = Time.frameCount;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (_report.phase == "SETTLE" && now - _phaseAt > .8) BeginCase();
                foreach (var effect in Object.FindObjectsByType<Vfx120Effect>(FindObjectsSortMode.None))
                {
                    if (!SeenIds.Add(effect.GetInstanceID())) continue;
                    Seen.Add(effect);
                    if (_row == null) { _report.messages.Add("Unattributed concurrent VFX; run may be confounded."); continue; }
                    _row.spawned++; _row.life = effect.Life; _row.impactClock = effect.ReceivedImpactClock; _row.guardClock = effect.ReceivedGuardClock;
                    _row.newVfxProfile = effect.Profile == _expected && effect.Profile.Glyph == _row.glyph
                        && effect.Begun && !effect.PreviewControlled && !effect.DemonstrationCues;
                    _row.clockMatches = ClockMatches(effect);
                }
                int alive = Seen.Count(x => x != null); _report.residualEffects = alive;
                if (_report.phase == "WAIT")
                {
                    _row.remainingEffects = alive; _quietFrames = alive == 0 ? _quietFrames + 1 : 0;
                    if (now - _phaseAt > 1.5 && _quietFrames >= 3)
                    {
                        _row.inkAfter = _ink.Value;
                        bool pass = _row.diagnostics == 1 && _row.recognitionSuccess && _row.recognized == _row.glyph && _row.channelEvents == 1
                            && (_row.kind == SpellKind.Parry.ToString() || _row.plans == 1)
                            && _row.spawned == 1 && _row.newVfxProfile && _row.clockMatches && _row.inkAfter < _row.inkBefore;
                        _row.status = pass ? "PASS_OBSERVED_INPUT_CAST_VFX" : "FAIL";
                        _report.completed++; if (!pass) _report.failed++;
                        _index++; _row = null; _report.phase = _index < 3 ? "SETTLE" : "RESTORE_SETTLE"; _phaseAt = now;
                    }
                    else if (now - _phaseAt > 15) { _row.issue = "Effect lifetime/commit wait exceeded 15 real seconds."; _cancel = true; }
                }
                if (_report.phase == "RESTORE_SETTLE" && now - _phaseAt > 1 && !_input.InDrawMode)
                    Finish(_report.failed == 0 && _report.errors == 0 ? "PASS_OBSERVED_THREE_INPUT_CASTS" : "COMPLETED_WITH_FINDINGS");
                if (_report.phase == "CANCEL" && now - _phaseAt > .2 && !_input.InDrawMode) Finish("CANCELLED");
            }
            catch (Exception e) { _report.messages.Add(e.ToString()); _cancel = true; }
        }
        private static void BeginCase()
        {
            char glyph = "가노머"[_index]; _book.TryGet(glyph, out var spell); _visuals.TryGet(glyph, out var visual);
            _expected = visual.FxPrefab.GetComponent<Vfx120Effect>().Profile; _plan = null;
            _row = new CaseRow { glyph = glyph.ToString(), kind = spell.Kind.ToString(), inkBefore = _ink.Value };
            _report.cases.Add(_row); _steps = new List<Step>();
            _steps.Add(new Step { q = true, point = _held.point });
            var library = Get<JamoTemplateLibrarySO>(_input, "_templates");
            string initial = _index == 0 ? "ㄱ" : _index == 1 ? "ㄴ" : "ㅁ";
            string medial = _index == 0 ? "ㅏ" : _index == 1 ? "ㅗ" : "ㅓ";
            AddJamo(library, "_initials", initial, _index == 1 ? new Rect(.39f, .55f, .22f, .23f) : new Rect(.29f, .34f, .21f, .34f));
            AddJamo(library, "_medials", medial, _index == 1 ? new Rect(.34f, .29f, .33f, .19f) : new Rect(.55f, .31f, .18f, .40f));
            _steps.Add(new Step { point = _steps[_steps.Count - 1].point });
            _stepIndex = 0; _nextStep = 0; _report.phase = "DRAW";
        }
        private static void AddJamo(JamoTemplateLibrarySO library, string field, string name, Rect rect)
        {
            var assets = Get<TextAsset[]>(library, field); XmlDocument xml = null; TextAsset selected = null;
            foreach (var asset in assets ?? Array.Empty<TextAsset>())
            {
                if (asset == null) continue;
                var candidate = new XmlDocument(); candidate.LoadXml(asset.text);
                if (candidate.DocumentElement?.GetAttribute("Name") == name) { xml = candidate; selected = asset; break; }
            }
            Require(xml != null, "No registered template for " + name);
            _row.templates += (string.IsNullOrEmpty(_row.templates) ? "" : "; ") + AssetDatabase.GetAssetPath(selected);
            var strokes = new List<List<Vector2>>();
            foreach (XmlNode stroke in xml.SelectNodes("/Gesture/Stroke"))
            {
                var points = new List<Vector2>();
                foreach (XmlNode point in stroke.SelectNodes("Point")) points.Add(new Vector2(float.Parse(point.Attributes["X"].Value, CultureInfo.InvariantCulture), float.Parse(point.Attributes["Y"].Value, CultureInfo.InvariantCulture)));
                if (points.Count > 1) strokes.Add(points);
            }
            var all = strokes.SelectMany(x => x).ToArray(); Require(all.Length > 1, "Empty template: " + name);
            var min = new Vector2(all.Min(x => x.x), all.Min(x => x.y)); var max = new Vector2(all.Max(x => x.x), all.Max(x => x.y));
            var size = max - min; float scale = Mathf.Min(rect.width * Screen.width / Mathf.Max(1, size.x), rect.height * Screen.height / Mathf.Max(1, size.y));
            Vector2 origin = new Vector2(rect.center.x * Screen.width - size.x * scale * .5f, rect.center.y * Screen.height + size.y * scale * .5f);
            foreach (var stroke in strokes)
            {
                // Arc-length sampling preserves every template stroke and pen-up break.
                var cumulative = new float[stroke.Count];
                for (int i = 1; i < stroke.Count; i++) cumulative[i] = cumulative[i - 1] + Vector2.Distance(stroke[i - 1], stroke[i]);
                int count = Mathf.Clamp(Mathf.CeilToInt(cumulative[cumulative.Length - 1] * scale / 9), 12, 48), segment = 1;
                for (int i = 0; i < count; i++)
                {
                    float distance = cumulative[cumulative.Length - 1] * i / (count - 1f);
                    while (segment < cumulative.Length - 1 && cumulative[segment] < distance) segment++;
                    Vector2 p = Vector2.Lerp(stroke[segment - 1], stroke[segment], Mathf.InverseLerp(cumulative[segment - 1], cumulative[segment], distance)) - min;
                    _steps.Add(new Step { q = true, stroke = true, point = origin + new Vector2(p.x, -p.y) * scale });
                }
                _steps.Add(new Step { q = true, point = _steps[_steps.Count - 1].point });
                _steps.Add(_steps[_steps.Count - 1]);
            }
        }
        private static void Point(Vector2 p) { if (_row != null) _row.rawPoints++; }
        private static void Stroke() { if (_row != null) _row.rawStrokes++; }
        private static void Diagnosed(DrawingInputController.CommitDiagnostics d)
        {
            if (_row == null) return; _row.diagnostics++; _row.recognitionSuccess = d.Success;
            _row.recognized = d.Success ? d.Letter.ToString() : "MISFIRE"; _row.commitTime = Time.time;
        }
        private static void Letter(DrawnLetter letter) { if (_row != null) _row.channelEvents++; }
        private static void Planned(CastPlan plan)
        {
            if (_row == null) return;
            _row.plans++; _row.plannedAt = Time.time; _plan = plan;
        }
        private static bool ClockMatches(Vfx120Effect effect)
        {
            // Read effective flight as well as the received value: an AreaPlan can
            // override flight while ReceivedImpactClock correctly stays at zero.
            _row.observedFlight = Get<float>(effect, "_flight");
            if (!effect.Begun || _expected == null) return false;
            if (_row.kind == SpellKind.Parry.ToString())
            {
                _row.clockMode = "EXISTING_GUARD_DURATION_AND_BRIGHT_WINDOW";
                _row.expectedImpactClock = 0; _row.expectedFlight = _expected.Flight;
                return _plan == null && effect.ReceivedAreaPlan == null && effect.ReceivedTarget == null
                    && CloseClock(effect.ReceivedImpactClock, 0)
                    && CloseClock(effect.ReceivedGuardClock, _config.GuardDuration)
                    && CloseClock(effect.ReceivedGuardBrightWindow, _config.ParryWindow)
                    && CloseClock(effect.Life, _config.GuardDuration)
                    && CloseClock(_row.observedFlight, _row.expectedFlight);
            }
            if (_plan == null || _plan.Cast.Letter.ToString() != _row.glyph
                || !CloseClock(effect.ReceivedGuardClock, 0) || !CloseClock(effect.ReceivedGuardBrightWindow, 0)) return false;
            if (_plan.Area != null)
            {
                // The current three-case suite exercises 노 (Cone). Its adapter gets
                // target=null/duration=0 and the exact authoritative AreaPlan object.
                // Other shapes are not silently claimed by this bounded test.
                _row.clockMode = "AUTHORITATIVE_CONE_PLAN_DELAY";
                _row.expectedImpactClock = 0; _row.expectedFlight = Mathf.Max(.01f, _plan.Area.Delay);
                return _plan.Area.Shape == AreaShape.Cone && ReferenceEquals(effect.ReceivedAreaPlan, _plan.Area)
                    && effect.ReceivedTarget == null && CloseClock(effect.ReceivedImpactClock, 0)
                    && CloseClock(_row.observedFlight, _row.expectedFlight);
            }
            if (effect.ReceivedAreaPlan != null) return false;
            if (_plan.Hits.Count == 0)
            {
                // null==null only confirms the absence of an area plan. It must not
                // accept a stale nonzero impact clock on the ordinary C2 air cast.
                _row.clockMode = "AIR_CAST_ZERO_CLOCK_PROFILE_FLIGHT";
                _row.expectedImpactClock = 0; _row.expectedFlight = _expected.Flight;
                return effect.ReceivedTarget == null && CloseClock(effect.ReceivedImpactClock, 0)
                    && CloseClock(_row.observedFlight, _row.expectedFlight);
            }
            _row.clockMode = "SINGLE_TARGET_SCHEDULED_IMPACT_MINUS_PLAN_TIME";
            if (_plan.Hits.Count != 1 || _plan.Hits[0].Target == null) return false;
            _row.expectedImpactClock = _plan.Hits[0].ImpactTime - _row.plannedAt;
            _row.expectedFlight = _row.expectedImpactClock;
            return _row.expectedImpactClock > 0 && effect.ReceivedTarget == _plan.Hits[0].Target.transform
                && CloseClock(effect.ReceivedImpactClock, _row.expectedImpactClock)
                && CloseClock(_row.observedFlight, _row.expectedFlight);
        }
        private static bool CloseClock(float actual, float expected) =>
            !float.IsNaN(actual) && !float.IsInfinity(actual) && !float.IsNaN(expected)
            && !float.IsInfinity(expected) && Mathf.Abs(actual - expected) <= .001f;
        private static void Logged(string text, string stack, LogType kind)
        {
            if (kind != LogType.Error && kind != LogType.Exception && kind != LogType.Assert) return;
            _report.errors++; if (_report.messages.Count < 25) _report.messages.Add(kind + ": " + text);
        }
        private static void PlayChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Finish("INTERRUPTED_PLAY_EXIT"); }
        private static void Reloading() { if (_running) Finish("INTERRUPTED_ASSEMBLY_RELOAD"); }
        private static void Finish(string status)
        {
            _running = false;
            EditorApplication.playModeStateChanged -= PlayChanged; AssemblyReloadEvents.beforeAssemblyReload -= Reloading; Application.logMessageReceived -= Logged;
            if (_report == null) return;
            _report.status = status; _report.phase = "FINISHED";
            if (_hooked)
            {
                if (_input != null) { _input.CommitDiagnosed -= Diagnosed; _input.StrokePointAdded -= Point; _input.StrokeStarted -= Stroke; }
                if (_channel != null) _channel.Unsubscribe(Letter); if (_wiring != null) _wiring.CastPlanned -= Planned; _hooked = false;
            }
            try
            {
                var loop = PlayerLoop.GetCurrentPlayerLoop(); Remove(ref loop); PlayerLoop.SetPlayerLoop(loop);
                var installedLoop = PlayerLoop.GetCurrentPlayerLoop();
                _report.remainingInputStages = CountStage(installedLoop, typeof(InputStage));
                _report.remainingObserveStages = CountStage(installedLoop, typeof(ObserveStage));
                _report.loopRemoved = _report.remainingInputStages == 0 && _report.remainingObserveStages == 0;
                if (_inputCaptured)
                {
                    // A typed InputDevice[] null implicitly converts to an EMPTY
                    // ReadOnlyArray value, not nullable-null. That would disable every
                    // input binding. Preserve the nullable state explicitly on restore.
                    _actions.devices = NullableDeviceFilter(_assetDevices);
                    foreach (var filter in Filters) filter.map.devices = NullableDeviceFilter(filter.devices);
                    if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
                    if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
                    _report.virtualDevicesRemoved = (_keyboard == null || !_keyboard.added) && (_mouse == null || !_mouse.added);
                    if (_oldKeyboard != null && _oldKeyboard.added) _oldKeyboard.MakeCurrent(); if (_oldMouse != null && _oldMouse.added) _oldMouse.MakeCurrent();
                    InputSystem.settings.backgroundBehavior = _backgroundBehavior; InputSystem.settings.editorInputBehaviorInPlayMode = _editorInput; Application.runInBackground = _background;
                    ObserveFilterRestoration();
                    _report.drawingBindingsAfter = DrawingBindingSignature();
                    _report.drawingBindingsRestored = _report.drawingBindingsBefore == _report.drawingBindingsAfter;
                    _report.currentDevicesRestored = Keyboard.current == _oldKeyboard && Mouse.current == _oldMouse;
                    _report.inputSettingsRestored = InputSystem.settings.backgroundBehavior == _backgroundBehavior
                        && InputSystem.settings.editorInputBehaviorInPlayMode == _editorInput && Application.runInBackground == _background;
                    _report.actionMapStatesRestored = Filters.All(f => f.map.enabled == f.enabled);
                    Cursor.lockState = _cursorLock; Cursor.visible = _cursorVisible;
                    _report.cursorRestored = Cursor.lockState == _cursorLock && Cursor.visible == _cursorVisible;
                    _report.timeScaleReturned = Mathf.Abs(Time.timeScale - _timeScale) < .0001f;
                }
                _report.drawingExited = _input != null && !_input.InDrawMode;
                if (_camera != null)
                {
                    _report.cameraPositionDelta = Vector3.Distance(_cameraPosition, _camera.transform.position);
                    _report.cameraAngleDelta = Quaternion.Angle(_cameraRotation, _camera.transform.rotation);
                    _report.cameraFovDelta = Mathf.Abs(_cameraFov - _camera.fieldOfView);
                    _report.cameraPropertiesRestored = _camera.targetTexture == _cameraTarget && _camera.cullingMask == _cameraMask && _report.cameraFovDelta < .1f;
                    _report.cameraPoseReturned = _report.cameraPositionDelta < .03f && _report.cameraAngleDelta < .2f;
                }
                _report.residualEffects = Seen.Count(x => x != null); _report.inkAfter = _ink != null ? _ink.Value : 0;
                _report.fileHashAfter = HashScene(); _report.sceneFileUnchanged = _report.fileHashBefore == _report.fileHashAfter;
                _report.cleanupStatus = _report.inputFilterRestored && _report.virtualDevicesRemoved && _report.loopRemoved
                    && _report.currentDevicesRestored && _report.inputSettingsRestored && _report.actionMapStatesRestored
                    && _report.drawingBindingsRestored
                    && _report.cursorRestored && _report.drawingExited && _report.residualEffects == 0
                    ? "OBSERVED_INPUT_AND_EFFECT_CLEANUP" : "UNVERIFIED_OR_RESIDUAL_STATE";
                if (_report.status.StartsWith("PASS") && (_report.cleanupStatus != "OBSERVED_INPUT_AND_EFFECT_CLEANUP"
                    || !_report.sceneFileUnchanged || !_report.cameraPropertiesRestored || !_report.cameraPoseReturned || !_report.timeScaleReturned))
                    _report.status = "COMPLETED_WITH_FINDINGS";
            }
            catch (Exception e) { _report.cleanupStatus = "CLEANUP_ERROR"; _report.messages.Add(e.ToString()); _report.status = "COMPLETED_WITH_FINDINGS"; }
            Directory.CreateDirectory(Path.GetDirectoryName(_report.output)); File.WriteAllText(_report.output, JsonUtility.ToJson(_report, true));
        }
        private static InputDevice[] SnapshotDevices(ReadOnlyArray<InputDevice>? devices) => devices.HasValue ? devices.Value.ToArray() : null;
        private static ReadOnlyArray<InputDevice>? NullableDeviceFilter(InputDevice[] devices) =>
            devices == null ? (ReadOnlyArray<InputDevice>?)null : new ReadOnlyArray<InputDevice>(devices);
        private static InputDevice[] RawMapDevices(InputActionMap map)
        {
            // Read-only inspection of the installed package's own nullable DeviceArray.
            // Fail before input takeover if a future package changes this representation;
            // never guess whether a map is inheriting the asset filter.
            var field = typeof(InputActionMap).GetField("m_Devices", Private);
            Require(field != null, "InputSystem map raw device storage is unavailable.");
            var storage = field.GetValue(map);
            var getter = storage.GetType().GetMethod("Get", BindingFlags.Instance | BindingFlags.Public);
            Require(getter != null, "InputSystem map raw device getter is unavailable.");
            var value = getter.Invoke(storage, null);
            return value == null ? null : ((ReadOnlyArray<InputDevice>)value).ToArray();
        }
        private static void ObserveFilterRestoration()
        {
            _assetFilterRow.afterRaw = _assetFilterRow.afterEffective = DeviceIds(SnapshotDevices(_actions.devices));
            _assetFilterRow.rawRestored = _assetFilterRow.beforeRaw == _assetFilterRow.afterRaw;
            _assetFilterRow.effectiveRestored = _assetFilterRow.beforeEffective == _assetFilterRow.afterEffective;
            foreach (var filter in Filters)
            {
                filter.row.afterRaw = DeviceIds(RawMapDevices(filter.map));
                filter.row.afterEffective = DeviceIds(SnapshotDevices(filter.map.devices));
                filter.row.rawRestored = filter.row.beforeRaw == filter.row.afterRaw;
                filter.row.effectiveRestored = filter.row.beforeEffective == filter.row.afterEffective;
            }
            _report.inputFilterRestored = _report.deviceFilters.All(x => x.rawRestored && x.effectiveRestored);
        }
        private static string DrawingBindingSignature() => string.Join(";", _actions.FindActionMap("Drawing", true).actions
            .Select(a => a.name + "=" + string.Join(",", a.controls.Select(c => c.device.deviceId + ":" + c.path).OrderBy(x => x))).OrderBy(x => x));
        private static string DeviceIds(InputDevice[] devices) => devices == null ? "null" : "[" + string.Join(",", devices.Select(x => x.deviceId)) + "]";
        private static T Get<T>(object owner, string field) => owner == null ? default : (T)owner.GetType().GetField(field, Private).GetValue(owner);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static string HashScene() { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(Application.dataPath, "../", ScenePath)))); }
        private static bool Insert(ref PlayerLoopSystem parent, Type anchor, Type type, PlayerLoopSystem.UpdateFunction update, bool after)
        {
            if (parent.subSystemList == null) return false;
            for (int i = 0; i < parent.subSystemList.Length; i++)
            {
                var child = parent.subSystemList[i];
                if (child.type == anchor)
                {
                    var list = parent.subSystemList.ToList(); list.Insert(i + (after ? 1 : 0), new PlayerLoopSystem { type = type, updateDelegate = update }); parent.subSystemList = list.ToArray(); return true;
                }
                if (Insert(ref child, anchor, type, update, after)) { var list = (PlayerLoopSystem[])parent.subSystemList.Clone(); list[i] = child; parent.subSystemList = list; return true; }
            }
            return false;
        }
        private static void Remove(ref PlayerLoopSystem parent)
        {
            if (parent.subSystemList == null) return;
            var list = new List<PlayerLoopSystem>();
            foreach (var entry in parent.subSystemList) { if (entry.type == typeof(InputStage) || entry.type == typeof(ObserveStage)) continue; var child = entry; Remove(ref child); list.Add(child); }
            parent.subSystemList = list.ToArray();
        }
        private static int CountStage(PlayerLoopSystem parent, Type stage)
        {
            int count = parent.type == stage ? 1 : 0;
            if (parent.subSystemList != null) foreach (var child in parent.subSystemList) count += CountStage(child, stage);
            return count;
        }
    }
}
