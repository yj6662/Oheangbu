using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Oheangbu.App;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // SPEC-PLAYER-DOSA: 실제 입력과 실제 CameraRig/LockOn/Harvest를 통과하는 유한 Play 검사.
    // 검수 fixture 상태만 배치한다. 런타임 Update 호출이나 게임 판정 메서드 직접 호출은 하지 않는다.
    public sealed class DosaContextValidation
    {
        private const int Width = 1920, Height = 1080, Fps = 30;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DosaContextValidation _active;
        private static string _last = "NOT_STARTED";
        private static Report _lastReport;
        private struct InputStage { }
        private struct CaptureStage { }
        private enum Phase { Settle, WallDraw, WallRecover, ReleaseWall, Acquire, LockDraw, ReleaseLock, Harvest, HarvestRelease, HarvestOutside, HarvestDrawing, FinalRelease }
        // 30Hz 입력 궤적 표본 수다. 구간 종료는 Unity 프레임 수가 아닌 실제 unscaled 경과 시간으로 정한다.
        private static readonly int[] Durations = { 30, 40, 80, 4, 20, 60, 4, 60, 30, 20, 24, 45 };

        [Serializable] private sealed class Report
        {
            public string status = "STARTING", phase, error, path;
            public bool restored, wallRecovered, harvestReleased;
            public int frames, failedChecks, actualWallCastHits, wallContractedDrawingFrames, wallRecoveryDrawingFrames, lockDrawingFrames, drawingFrames;
            public int ribbonChecks, singlePointProjectionChecks, ribbonMeshChecks, harvestingFrames, harvestEndpointChecks, harvestGateChecks;
            public float minBoom = 1f, recoveryBoom, maxTipPixels, maxRibbonPixels, maxHarvestEndpointMeters;
            public float maxLockYawChange, harvestedInk, expectedHarvestInk, harvestedDamage, expectedHarvestDamage;
            // D306 click harvest: one press = one pull = one chunk (one damage, one gain); holding never repeats it
            public int harvestPulls, harvestChunks, harvestCancels, gatePulls, pullingFrames;
            public float pullSeconds, expectedPullSeconds, minTargetHp = float.MaxValue, groggyDelta;
            public double elapsedWallSeconds;
            public List<string> failures = new List<string>();
            public List<Frame> samples = new List<Frame>();
            public List<PhaseTiming> phaseTimings = new List<PhaseTiming>();
            public string clock = "captureDeltaTime=0; real unscaled phase duration; 30Hz input trajectory; InputSystem.Update and checks every Unity frame. Gameplay owns timeScale; harvest = one chunk per press (D306), timed by scaled Time.time; natural ink regen is allowed only at its configured rate.";
            public string ribbonContract = "The first point is projected and checked immediately. RibbonMeshBuilder requires at least two points to form a strip, so mesh existence is checked only for 2+ points. A one-point empty mesh is the existing polyline contract, not a delayed first-point projection.";
            public string scope = "Actual virtual Tab/Q/LMB; actual CameraRig sphere cast, LockOn pull, HarvestAction; 1920x1080 projection. No scene transition or art/FPS verdict.";
        }
        [Serializable] private sealed class PhaseTiming
        {
            public string phase;
            public int unityFrames;
            public float plannedSeconds, actualUnscaledSeconds;
        }
        [Serializable] private sealed class Frame
        {
            public int frame, phaseFrame, trajectorySample, ribbonPointCount, ribbonVertexCount;
            public string phase;
            public bool drawing, stroking, locked, extracting, wallEnabled;
            public float boom, tipPixels, ribbonPixels, ink, targetHp, yaw, pitch, scaledDeltaTime, unscaledDeltaTime, phaseElapsedSeconds, harvestEndpointMeters = -1f;
            public Vector3 targetPosition, cameraPosition;
        }
        [Serializable] private sealed class Progress
        {
            public string status, phase, error, path;
            public int frames, failedChecks, actualWallCastHits, wallContractedDrawingFrames, lockDrawingFrames, harvestEndpointChecks;
            public bool restored, wallRecovered, harvestReleased;
            public float maxTipPixels, maxRibbonPixels, maxHarvestEndpointMeters, maxLockYawChange, harvestedInk;
            public double elapsedWallSeconds;
        }
        private sealed class AssetState
        {
            public InputActionAsset Asset;
            public InputDevice[] Devices;
            public readonly List<(InputActionMap map, InputDevice[] devices)> Maps = new List<(InputActionMap, InputDevice[])>();
        }
        private sealed class CameraState
        {
            public Camera Camera;
            public RenderTexture Target;
            public Rect Rect;
            public float Aspect;
        }

        private readonly Report _report = new Report();
        private readonly List<AssetState> _assets = new List<AssetState>();
        private readonly List<CameraState> _cameras = new List<CameraState>();
        private readonly List<InputDevice> _disabled = new List<InputDevice>();
        private readonly List<EnemyVitals> _oldCandidates = new List<EnemyVitals>();
        private PlayerMotor _motor;
        private CharacterController _controller;
        private CameraRigController _cameraRig;
        private DrawingInputController _input;
        private BrushStrokeFeedAdapter _feed;
        private PlayerVisualDriver _driver;
        private PlayerVisualRig _rig;
        private HarvestAction _harvest;
        private HarvestInkStreamEffect _effect;
        private LockOn _lock;
        private InkPool _ink;
        private CombatConfigSO _config;
        private Camera _main;
        private Transform _pivot;
        private GameObject _targetObject, _wallObject;
        private EnemyVitals _target;
        private BoxCollider _wall;
        private RenderTexture _targetTexture;
        private Keyboard _keyboard, _oldKeyboard;
        private Mouse _mouse, _oldMouse;
        private Gamepad _oldGamepad;
        private InputSettings.UpdateMode _oldUpdateMode;
        private InputSettings.BackgroundBehavior _oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode _oldEditorInput;
        private bool _oldRunInBackground, _oldCursorVisible, _oldShoulder, _captured, _finished;
        private CursorLockMode _oldCursor;
        private float _oldCapture, _oldTimeScale, _oldInk, _oldPitch, _oldVertical, _oldBoom, _oldFov;
        private Vector3 _oldPosition, _oldLocalPose, _oldCameraPosition, _targetOrigin, _basisRight, _basisForward;
        private Quaternion _oldRotation, _oldPivotRotation;
        private UnityEngine.Random.State _random;
        private int _phaseIndex, _phaseFrame, _trajectorySample, _inputFrame = -1, _lateFrame = -1;
        private float _beforeInk, _beforeHp, _lockStartYaw, _lastRecoveryBoom, _phaseElapsed, _pullStartedAt, _groggyBefore, _hpAtHarvest;
        private CombatLoopWiring _wiring;
        private double _started;
        private Phase Current => (Phase)_phaseIndex;

        public static string Begin()
        {
            if (_active != null) return Status();
            if (!Application.isPlaying || EditorApplication.isPaused || SceneManager.GetActiveScene().name != "C2_PlayerValidation")
                return "NOT_RUN: unpaused Play in C2_PlayerValidation required";
            if (typeof(DosaInputPlayback).GetField("_active", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) != null)
                return "NOT_RUN: stop DosaInputPlayback before context validation";
            var run = new DosaContextValidation();
            _active = run;
            try { run.Initialise(); run.InstallLoop(); run._report.status = "RUNNING"; }
            catch (Exception e) { run.Finish("ERROR", e.GetBaseException().Message); }
            return Status();
        }
        public static bool IsRunning => _active != null;
        public static string Status()
        {
            var report = _active != null ? _active._report : _lastReport;
            if (report == null) return _last;
            return JsonUtility.ToJson(new Progress { status = report.status, phase = report.phase, error = report.error, path = report.path,
                frames = report.frames, failedChecks = report.failedChecks, actualWallCastHits = report.actualWallCastHits,
                wallContractedDrawingFrames = report.wallContractedDrawingFrames, lockDrawingFrames = report.lockDrawingFrames,
                harvestEndpointChecks = report.harvestEndpointChecks, restored = report.restored, wallRecovered = report.wallRecovered,
                harvestReleased = report.harvestReleased, maxTipPixels = report.maxTipPixels, maxRibbonPixels = report.maxRibbonPixels,
                maxHarvestEndpointMeters = report.maxHarvestEndpointMeters, maxLockYawChange = report.maxLockYawChange,
                harvestedInk = report.harvestedInk, elapsedWallSeconds = report.elapsedWallSeconds });
        }
        public static string Stop() { _active?.Finish("STOPPED", null); return Status(); }

        private void Initialise()
        {
            _motor = Object.FindFirstObjectByType<PlayerMotor>();
            _input = Object.FindFirstObjectByType<DrawingInputController>();
            _feed = Object.FindFirstObjectByType<BrushStrokeFeedAdapter>();
            _driver = Object.FindFirstObjectByType<PlayerVisualDriver>();
            _rig = Object.FindFirstObjectByType<PlayerVisualRig>();
            _cameraRig = Object.FindFirstObjectByType<CameraRigController>();
            _harvest = Object.FindFirstObjectByType<HarvestAction>();
            _effect = Object.FindFirstObjectByType<HarvestInkStreamEffect>();
            _lock = Object.FindFirstObjectByType<LockOn>();
            _main = Camera.main;
            if (_motor == null || _input == null || _feed == null || _driver == null || _rig == null || _cameraRig == null || _harvest == null || _effect == null || _lock == null || _main == null)
                throw new InvalidOperationException("Required player/drawing/harvest components missing");
            if (_input.InDrawMode || _lock.IsLocked || _harvest.IsExtracting || Get<bool>(_effect, "_active") || !_driver.enabled || !_feed.enabled || !_motor.enabled || !_input.enabled)
                throw new InvalidOperationException("Begin from idle, unlocked player with visuals and input enabled");
            if (Get<bool>(_effect, "_debugHold")) throw new InvalidOperationException("Harvest debug hold must be off");
            if (_effect.FlowVisible) throw new InvalidOperationException("Begin with no chunk pull effect visible");
            _wiring = Object.FindFirstObjectByType<CombatLoopWiring>();
            _config = Get<CombatConfigSO>(_motor, "_config");
            _ink = Get<InkPool>(_harvest, "_ink");
            _controller = _motor.GetComponent<CharacterController>();
            _pivot = Get<Transform>(_cameraRig, "_cameraPivot");
            if (_config == null || _ink == null || _controller == null || _pivot == null || _config.LockOnCameraPull <= 0f)
                throw new InvalidOperationException("Config, ink, controller, camera pivot, or lock pull unavailable");
            if (Get<Transform>(_effect, "_sinkAnchor") != _rig.EffectTip || _rig.EffectTip == null)
                throw new InvalidOperationException("Harvest sink does not reference this rig EffectTip");

            _oldUpdateMode = InputSystem.settings.updateMode; _oldBackground = InputSystem.settings.backgroundBehavior;
            _oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            _oldRunInBackground = Application.runInBackground; _oldCapture = Time.captureDeltaTime; _oldTimeScale = Time.timeScale;
            _oldCursor = Cursor.lockState; _oldCursorVisible = Cursor.visible;
            _oldKeyboard = Keyboard.current; _oldMouse = Mouse.current; _oldGamepad = Gamepad.current;
            _oldPosition = _motor.transform.position; _oldRotation = _motor.transform.rotation;
            _oldPitch = Get<float>(_motor, "_pitch"); _oldVertical = Get<float>(_motor, "_verticalVelocity");
            _oldShoulder = _cameraRig.IsShoulder; _oldLocalPose = Get<Vector3>(_cameraRig, "_localPose"); _oldBoom = Get<float>(_cameraRig, "_boom01");
            _oldCameraPosition = _main.transform.localPosition; _oldPivotRotation = _pivot.localRotation; _oldFov = _main.fieldOfView;
            _oldInk = _ink.Value; _random = UnityEngine.Random.state;
            _oldCandidates.AddRange(Get<List<EnemyVitals>>(_lock, "_candidates"));
            _captured = true; _started = Time.realtimeSinceStartupAsDouble;
            _report.path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosa/context-validation.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(_report.path));
            EditorApplication.playModeStateChanged += PlayChanged;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;

            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Application.runInBackground = true; Time.captureDeltaTime = 0f;
            foreach (var device in InputSystem.devices.ToArray())
                if (device.enabled && (device is Keyboard || device is Mouse || device is Gamepad))
                { _disabled.Add(device); InputSystem.DisableDevice(device); }
            _keyboard = InputSystem.AddDevice<Keyboard>("DosaContextKeyboard");
            _mouse = InputSystem.AddDevice<Mouse>("DosaContextMouse");
            Scope(Get<InputActionAsset>(_input, "_actions")); Scope(Get<InputActionAsset>(_motor, "_actions"));
            _targetTexture = new RenderTexture(Width, Height, 24) { name = "DosaContext1080p", hideFlags = HideFlags.DontSave };
            _targetTexture.Create(); HoldCamera(_main);
            foreach (var camera in _main.GetUniversalAdditionalCameraData().cameraStack) if (camera != null) HoldCamera(camera);

            _basisRight = _motor.transform.right; _basisForward = _motor.transform.forward;
            _targetOrigin = _motor.transform.position + _basisForward * Mathf.Min(6f, _config.HarvestRange * .5f);
            _targetOrigin.y = _controller.bounds.min.y;
            _targetObject = new GameObject("DosaContext_Target") { hideFlags = HideFlags.DontSave };
            _targetObject.SetActive(false); _targetObject.transform.position = _targetOrigin;
            _target = _targetObject.AddComponent<EnemyVitals>();
            var serialized = new SerializedObject(_target);
            serialized.FindProperty("_config").objectReferenceValue = _config; serialized.ApplyModifiedPropertiesWithoutUndo();
            _targetObject.SetActive(true);
            _lock.SetCandidates(new[] { _target });
            _harvest.PullStarted += OnPullStarted; _harvest.ChunkExtracted += OnChunkExtracted; _harvest.PullCanceled += OnPullCanceled;
            _wallObject = new GameObject("DosaContext_BoomWall") { hideFlags = HideFlags.DontSave };
            _wall = _wallObject.AddComponent<BoxCollider>(); _wall.size = new Vector3(3f, 3f, .08f); _wall.enabled = false;
            Physics.IgnoreCollision(_controller, _wall, true);
        }

        private void Scope(InputActionAsset asset)
        {
            if (asset == null || _assets.Any(s => s.Asset == asset)) return;
            var state = new AssetState { Asset = asset, Devices = asset.devices?.ToArray() };
            _assets.Add(state); var devices = new InputDevice[] { _keyboard, _mouse }; asset.devices = devices;
            foreach (var map in asset.actionMaps) { state.Maps.Add((map, map.devices?.ToArray())); map.devices = devices; }
        }
        private void HoldCamera(Camera camera)
        {
            if (_cameras.Any(c => c.Camera == camera)) return;
            _cameras.Add(new CameraState { Camera = camera, Target = camera.targetTexture, Rect = camera.rect, Aspect = camera.aspect });
            camera.targetTexture = _targetTexture; camera.rect = new Rect(0, 0, 1, 1); camera.aspect = Width / (float)Height;
        }
        private void InputUpdate()
        {
            if (_finished || _inputFrame == Time.frameCount) return;
            _inputFrame = Time.frameCount;
            try
            {
                if (_motor == null || SceneManager.GetActiveScene().name != "C2_PlayerValidation") throw new InvalidOperationException("Validation scene changed");
                if (Time.realtimeSinceStartupAsDouble - _started > 90d) throw new TimeoutException("Context validation exceeded 90 wall seconds");
                if (_phaseFrame == 0) EnterPhase();
                _trajectorySample = Mathf.Min(Durations[_phaseIndex] - 1, Mathf.FloorToInt(_phaseElapsed * Fps));
                bool draw = Current == Phase.WallDraw || Current == Phase.WallRecover || Current == Phase.LockDraw || Current == Phase.HarvestDrawing;
                bool hold = draw || Current == Phase.Harvest || Current == Phase.HarvestOutside;
                var keys = new List<Key>(3);
                if (draw) keys.Add(Key.Q);
                if (Current == Phase.Settle && !_cameraRig.IsShoulder) keys.Add(Key.V);
                if (Current == Phase.Acquire && _phaseFrame == 0) keys.Add(Key.Tab);
                Vector2 delta = Current == Phase.Settle ? new Vector2(0, Get<float>(_motor, "_pitch") / Mathf.Max(.001f, _config.LookSensitivity)) : Vector2.zero;
                if (Current == Phase.LockDraw)
                    _target.transform.position = _targetOrigin + _basisRight * (Mathf.Sin(_trajectorySample * .06f) * 4f);
                Physics.SyncTransforms();
                var point = new Vector2(600f + (_trajectorySample % 40) * 16f, 540f + Mathf.Sin(_trajectorySample * .17f) * 100f);
                _beforeInk = _ink.Value; _beforeHp = Get<float>(_target, "_hp");
                InputSystem.QueueStateEvent(_mouse, new MouseState { position = point, delta = delta }.WithButton(MouseButton.Left, hold));
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys.ToArray()));
                // 표본 사이에도 매 Unity 프레임 갱신해야 WasPressedThisFrame/Released 플래그가 반복되지 않는다.
                InputSystem.Update(); _keyboard.MakeCurrent(); _mouse.MakeCurrent();
            }
            catch (Exception e) { Finish("ERROR", e.GetBaseException().Message); }
        }
        private void EnterPhase()
        {
            if (Current == Phase.WallDraw)
            {
                var offset = _config.ShoulderDrawOffset;
                var direction = _pivot.TransformDirection(offset.normalized);
                _wall.transform.SetPositionAndRotation(_pivot.position + direction * (offset.magnitude * .9f), Quaternion.LookRotation(direction));
                _wall.enabled = true;
            }
            if (Current == Phase.WallRecover) { _wall.enabled = false; _lastRecoveryBoom = Get<float>(_cameraRig, "_boom01"); }
            if (Current == Phase.LockDraw) _lockStartYaw = _motor.transform.eulerAngles.y;
            if (Current == Phase.Harvest)
            {
                _target.transform.position = _targetOrigin;
                SetInk(.35f); // 가득 찬 먹에서는 수급을 측정할 수 없어 fixture 초기 잔량만 조정한다.
                _groggyBefore = _target.Groggy.Value01; _hpAtHarvest = _target.Hp;
            }
            if (Current == Phase.HarvestOutside)
                _target.transform.position = _motor.transform.position + _basisForward * (_config.HarvestRange + 4f);
            if (Current == Phase.HarvestDrawing) _target.transform.position = _targetOrigin;
        }

        private void CaptureLate()
        {
            if (_finished || _inputFrame != Time.frameCount || _lateFrame == Time.frameCount) return;
            _lateFrame = Time.frameCount;
            try
            {
                float boom = Get<float>(_cameraRig, "_boom01");
                var diagnostic = _driver.Diagnostics;
                var sample = new Frame { frame = _report.frames, phaseFrame = _phaseFrame, trajectorySample = _trajectorySample,
                    phaseElapsedSeconds = _phaseElapsed, phase = Current.ToString(),
                    drawing = _input.InDrawMode, stroking = _input.IsStroking, locked = _lock.Target == _target,
                    extracting = _harvest.IsExtracting, wallEnabled = _wall.enabled, boom = boom,
                    tipPixels = diagnostic.TipScreenErrorPixels, ink = _ink.Value, targetHp = Get<float>(_target, "_hp"),
                    yaw = _motor.transform.eulerAngles.y, pitch = Get<float>(_motor, "_pitch"), targetPosition = _target.transform.position,
                    cameraPosition = _main.transform.position, scaledDeltaTime = Time.deltaTime, unscaledDeltaTime = Time.unscaledDeltaTime };
                if (_input.InDrawMode && _input.HasPointer && _input.IsStroking)
                {
                    _report.drawingFrames++; _report.maxTipPixels = Mathf.Max(_report.maxTipPixels, diagnostic.TipScreenErrorPixels);
                    Check(IsFinite(diagnostic.TipScreenErrorPixels) && diagnostic.TipScreenErrorPixels <= 2f, "drawing tip exceeds 2px");
                    Check(diagnostic.NearVisible, "near drawing visual missing");
                    Check(!Get<Animator>(_rig, "_worldAnimator").GetComponentsInChildren<Renderer>().Any(r => r.enabled && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly), "world body rendered together with near arms");
                    CheckRibbon(sample);
                }
                if (Current == Phase.WallDraw && _input.IsStroking && boom < .98f)
                {
                    _report.wallContractedDrawingFrames++; _report.minBoom = Mathf.Min(_report.minBoom, boom);
                    Vector3 direction = _pivot.TransformPoint(Get<Vector3>(_cameraRig, "_localPose")) - _pivot.position;
                    if (direction.sqrMagnitude > .000001f && Physics.SphereCast(_pivot.position, .25f, direction.normalized, out RaycastHit hit, direction.magnitude) && hit.collider == _wall)
                    {
                        _report.actualWallCastHits++;
                        Check(Vector3.Distance(_main.transform.position, _wall.ClosestPoint(_main.transform.position)) >= .245f, "camera boom sphere penetrates fixture wall");
                    }
                }
                if (Current == Phase.WallRecover && _input.IsStroking)
                {
                    _report.wallRecoveryDrawingFrames++; _report.recoveryBoom = boom;
                    Check(boom + .0001f >= _lastRecoveryBoom, "boom recovery reversed without wall"); _lastRecoveryBoom = boom;
                    if (boom > .99f) _report.wallRecovered = true;
                }
                if (Current == Phase.LockDraw)
                {
                    Check(_lock.Target == _target, "moving target lock lost");
                    if (_input.IsStroking) _report.lockDrawingFrames++;
                    _report.maxLockYawChange = Mathf.Max(_report.maxLockYawChange, Mathf.Abs(Mathf.DeltaAngle(_lockStartYaw, sample.yaw)));
                }
                if (Current == Phase.Harvest)
                {
                    float damage = _beforeHp - sample.targetHp;
                    _report.harvestingFrames++; _report.harvestedDamage += damage; _report.minTargetHp = Mathf.Min(_report.minTargetHp, sample.targetHp);
                    if (_harvest.IsExtracting) _report.pullingFrames++;
                    // D306: HP changes only on the snap frame; the pull itself never damages
                    Check(damage <= .0001f || _harvest.State == HarvestState.Cooldown, "harvest damaged the target outside the snap");
                    Check(sample.targetHp >= 1f - .0001f, "harvest took the target below 1 HP");
                    if (_effect.FlowProfile != null) { if (_effect.FlowVisible) CheckFlowEndpoint(sample); }
                    else if (Get<float>(_effect, "_reveal") >= 1f) CheckHarvestEndpoint(sample);
                }
                if ((Current == Phase.HarvestRelease || Current == Phase.HarvestOutside || Current == Phase.HarvestDrawing) && _phaseFrame > 2)
                {
                    _report.harvestGateChecks++;
                    // natural regen (InkRegenerator) may still fill at its configured rate; harvest must add nothing
                    Check(_ink.Value - _beforeInk <= RegenAllowance() + .0001f && Mathf.Abs(_beforeHp - sample.targetHp) < .0001f, "harvest gate still changes ink/HP");
                    Check(!_harvest.IsExtracting, "harvest signal persists beyond two-frame grace");
                }
                _report.samples.Add(sample); _report.frames++; _report.phase = Current.ToString();
                _report.elapsedWallSeconds = Time.realtimeSinceStartupAsDouble - _started;
                _phaseFrame++; _phaseElapsed += Time.unscaledDeltaTime;
                if (_phaseElapsed >= Durations[_phaseIndex] / (float)Fps)
                {
                    if (Current == Phase.Harvest) CheckChunk();
                    if (Current == Phase.HarvestRelease)
                    { _report.harvestReleased = _effect.FlowProfile != null ? !_effect.FlowVisible : !Get<bool>(_effect, "_active"); Check(_report.harvestReleased, "harvest stream did not fade after release"); }
                    _report.phaseTimings.Add(new PhaseTiming { phase = Current.ToString(), unityFrames = _phaseFrame,
                        plannedSeconds = Durations[_phaseIndex] / (float)Fps, actualUnscaledSeconds = _phaseElapsed });
                    _phaseFrame = 0; _phaseElapsed = 0f;
                    if (++_phaseIndex >= Durations.Length) { Complete(); return; }
                }
            }
            catch (Exception e) { Finish("ERROR", e.GetBaseException().Message); }
        }

        private void CheckRibbon(Frame sample)
        {
            if (!_feed.TryGetVisualPointer(out Camera camera, out Vector2 screen, out Vector3 world)) { Check(false, "projected pointer missing"); return; }
            var strokes = Get<List<BrushStrokeRenderer>>(_feed, "_strokes");
            if (strokes.Count == 0) { Check(false, "visible live ribbon missing"); return; }
            var stroke = strokes[strokes.Count - 1];
            var points = stroke.Data.Points;
            if (points.Count == 0) { Check(false, "live ribbon has no points"); return; }
            sample.ribbonPointCount = points.Count; sample.ribbonVertexCount = stroke.VertexCount;
            var screenActual = (Vector2)camera.WorldToScreenPoint(stroke.transform.TransformPoint(points[points.Count - 1].Position));
            sample.ribbonPixels = Vector2.Distance(screen, screenActual);
            _report.ribbonChecks++; _report.maxRibbonPixels = Mathf.Max(_report.maxRibbonPixels, sample.ribbonPixels);
            Check(IsFinite(sample.ribbonPixels) && sample.ribbonPixels <= 2f, "final-camera live ribbon endpoint projection mismatch");
            // 첫 점은 즉시 투영한다. 띠의 기하는 RibbonMeshBuilder의 기존 최소 2점 계약이다.
            // 따라서 한 점의 빈 메시를 첫 점 처리 지연으로 판정하지 않는다.
            if (points.Count >= 2)
            { _report.ribbonMeshChecks++; Check(stroke.VertexCount > 0, "multi-point live ribbon mesh missing"); }
            else _report.singlePointProjectionChecks++;
        }
        // D306 chunk pull: every visible flow frame aims at the final EffectTip (tendrils start there, the chunk lands there)
        private void CheckFlowEndpoint(Frame sample)
        {
            sample.harvestEndpointMeters = Vector3.Distance(_effect.FlowSink, _rig.EffectTip.position);
            _report.harvestEndpointChecks++; _report.maxHarvestEndpointMeters = Mathf.Max(_report.maxHarvestEndpointMeters, sample.harvestEndpointMeters);
            Check(IsFinite(sample.harvestEndpointMeters) && sample.harvestEndpointMeters <= .001f, "chunk flow sink does not match final EffectTip");
        }
        private void OnPullStarted(EnemyVitals target, Vector3 point)
        {
            if (Current == Phase.Harvest) { _report.harvestPulls++; _pullStartedAt = Time.time; }
            else _report.gatePulls++;
        }
        private void OnChunkExtracted(EnemyVitals target, Vector3 point, float received)
        {
            if (Current != Phase.Harvest) { _report.gatePulls++; return; }
            _report.harvestChunks++; _report.harvestedInk += received; _report.pullSeconds = Time.time - _pullStartedAt;
            _report.expectedHarvestInk = Mathf.Min(_config.HarvestChunkInk / _ink.CapacityMultiplier, 1f - (_ink.Value - received));
        }
        private void OnPullCanceled(HarvestCancelReason reason) { if (Current == Phase.Harvest) _report.harvestCancels++; }
        // one held press over the whole phase: exactly one pull, one chunk, configured damage/ink once, no groggy, cooldown held
        private void CheckChunk()
        {
            _report.expectedHarvestDamage = Mathf.Min(_config.HarvestChunkDamage * _target.DamageMultiplier, Mathf.Max(0f, _hpAtHarvest - 1f));
            _report.expectedPullSeconds = _config.HarvestPullSeconds; _report.groggyDelta = _target.Groggy.Value01 - _groggyBefore;
            Check(_report.harvestPulls == 1 && _report.harvestChunks == 1 && _report.harvestCancels == 0, "held press did not give exactly one pull and one chunk (pulls/chunks/cancels " + _report.harvestPulls + "/" + _report.harvestChunks + "/" + _report.harvestCancels + ")");
            Check(Mathf.Abs(_report.harvestedDamage - _report.expectedHarvestDamage) < .001f, "chunk damage differs from configured single hit");
            Check(Mathf.Abs(_report.harvestedInk - _report.expectedHarvestInk) < .001f, "chunk ink differs from configured single gain");
            Check(Mathf.Abs(_report.pullSeconds - _report.expectedPullSeconds) < .1f, "pull length differs from configured pull seconds");
            Check(Mathf.Abs(_report.groggyDelta) < .0001f, "harvest changed groggy");
        }
        private float RegenAllowance()
        {
            float grade = _wiring != null && _wiring.InkRegen != null ? _wiring.InkRegen.GradeMultiplier : 1f;
            return _config.InkRegenPerSecond * grade * Mathf.Max(1f, _config.InkRegenOutOfCombatMultiplier) * Time.deltaTime / Mathf.Max(1f, _ink.CapacityMultiplier);
        }
        private void CheckHarvestEndpoint(Frame sample)
        {
            var lines = Get<LineRenderer[]>(_effect, "_lines");
            if (lines == null || lines.Length == 0 || lines[0] == null || !lines[0].enabled || lines[0].positionCount == 0)
            { Check(false, "fully grown harvest line missing"); return; }
            var line = lines[0]; var endpoint = line.GetPosition(line.positionCount - 1);
            if (!line.useWorldSpace) endpoint = line.transform.TransformPoint(endpoint);
            sample.harvestEndpointMeters = Vector3.Distance(endpoint, _rig.EffectTip.position);
            _report.harvestEndpointChecks++; _report.maxHarvestEndpointMeters = Mathf.Max(_report.maxHarvestEndpointMeters, sample.harvestEndpointMeters);
            Check(IsFinite(sample.harvestEndpointMeters) && sample.harvestEndpointMeters <= .001f, "harvest line endpoint does not match final EffectTip");
        }
        private void Complete()
        {
            Check(_report.wallContractedDrawingFrames > 5, "insufficient actual contracted-boom drawing samples");
            Check(_report.actualWallCastHits > 5, "fixture wall was not the actual sphere-cast obstruction");
            Check(_report.wallRecovered && _report.wallRecoveryDrawingFrames > 5, "boom did not restore during drawing");
            Check(_report.lockDrawingFrames > 20 && _report.maxLockYawChange > 1f, "actual lock-on camera rotation not exercised");
            Check(_report.harvestingFrames > 20 && _report.harvestChunks == 1 && _report.harvestedInk > 0f && _report.harvestedDamage > 0f, "actual harvest not exercised");
            Check(_report.gatePulls == 0, "out-of-range/drawing press started a pull");
            // the #132 held stream reaches its reveal only after .55 s; the chunk pull ends at .45 s, so only the flow path has endpoint coverage
            Check((_report.harvestEndpointChecks > 5 || _effect.FlowProfile == null) && _report.harvestReleased, "harvest endpoint/release coverage missing");
            Finish(_report.failedChecks == 0 ? "PASS" : "FAIL", null);
        }
        private void Check(bool condition, string detail)
        {
            if (condition) return;
            _report.failedChecks++;
            string message = Current + " frame " + _phaseFrame + ": " + detail;
            if (_report.failures.Count < 30) _report.failures.Add(message);
        }
        private void SetInk(float value)
        { if (_ink.Value > value) _ink.SpendClamped(_ink.Value - value); else _ink.Gain(value - _ink.Value); }

        private void InstallLoop()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop(); RemoveStages(ref loop);
            bool a = Insert(ref loop, typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate), new PlayerLoopSystem { type = typeof(InputStage), updateDelegate = InputUpdate }, false);
            bool b = Insert(ref loop, typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate), new PlayerLoopSystem { type = typeof(CaptureStage), updateDelegate = CaptureLate }, true);
            if (!a || !b) throw new InvalidOperationException("Required PlayerLoop anchors missing");
            PlayerLoop.SetPlayerLoop(loop);
        }
        private static bool Insert(ref PlayerLoopSystem parent, Type anchor, PlayerLoopSystem stage, bool after)
        {
            if (parent.subSystemList == null) return false;
            var children = (PlayerLoopSystem[])parent.subSystemList.Clone();
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].type == anchor)
                { var list = new List<PlayerLoopSystem>(children); list.Insert(i + (after ? 1 : 0), stage); parent.subSystemList = list.ToArray(); return true; }
                if (Insert(ref children[i], anchor, stage, after)) { parent.subSystemList = children; return true; }
            }
            return false;
        }
        private static bool RemoveStages(ref PlayerLoopSystem parent)
        {
            if (parent.subSystemList == null) return false;
            var list = new List<PlayerLoopSystem>(); bool changed = false;
            foreach (var item in parent.subSystemList)
            {
                if (item.type == typeof(InputStage) || item.type == typeof(CaptureStage)) { changed = true; continue; }
                var child = item; changed |= RemoveStages(ref child); list.Add(child);
            }
            if (changed) parent.subSystemList = list.ToArray();
            return changed;
        }
        private void PlayChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Finish("PLAY_EXIT", null); }
        private void BeforeReload() => Finish("DOMAIN_RELOAD", null);
        private void Finish(string status, string error)
        {
            if (_finished) return;
            _finished = true; _report.status = status; _report.error = error;
            if (_captured) _report.elapsedWallSeconds = Time.realtimeSinceStartupAsDouble - _started;
            try { Restore(); _report.restored = true; }
            catch (Exception e) { _report.status = "ERROR"; _report.error = (error ?? "") + " RESTORE: " + e.GetBaseException().Message; }
            finally
            {
                _active = null; _lastReport = _report; _last = JsonUtility.ToJson(_report);
                if (!string.IsNullOrEmpty(_report.path))
                    try { File.WriteAllText(_report.path, _last, new UTF8Encoding(false)); }
                    catch (Exception e) { _report.error = (_report.error ?? "") + " SAVE: " + e.Message; _last = JsonUtility.ToJson(_report); }
            }
        }
        private void Restore()
        {
            EditorApplication.playModeStateChanged -= PlayChanged; AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            var loop = PlayerLoop.GetCurrentPlayerLoop(); if (RemoveStages(ref loop)) PlayerLoop.SetPlayerLoop(loop);
            if (!_captured) return;
            var errors = new List<Exception>();
            void Attempt(Action action) { try { action(); } catch (Exception e) { errors.Add(e); } }
            Attempt(() => { if (_input != null && _input.InDrawMode) { _input.enabled = false; _input.enabled = true; } });
            Attempt(() => { if (_harvest != null) { _harvest.PullStarted -= OnPullStarted; _harvest.ChunkExtracted -= OnChunkExtracted; _harvest.PullCanceled -= OnPullCanceled; } });
            Attempt(() => { if (_lock != null) { if (_lock.IsLocked) _lock.Toggle(); _lock.SetCandidates(_oldCandidates); } });
            Attempt(() => { if (_wallObject != null) Object.Destroy(_wallObject); if (_targetObject != null) Object.Destroy(_targetObject); });
            foreach (var state in _assets) Attempt(() => { if (state.Asset != null) { state.Asset.devices = state.Devices; foreach (var map in state.Maps) map.map.devices = map.devices; } });
            foreach (var device in new InputDevice[] { _keyboard, _mouse }) Attempt(() => { if (device != null && device.added) InputSystem.RemoveDevice(device); });
            foreach (var device in _disabled) Attempt(() => { if (device != null && device.added) InputSystem.EnableDevice(device); });
            Attempt(() => { if (_oldKeyboard != null && _oldKeyboard.added) _oldKeyboard.MakeCurrent(); if (_oldMouse != null && _oldMouse.added) _oldMouse.MakeCurrent(); if (_oldGamepad != null && _oldGamepad.added) _oldGamepad.MakeCurrent(); });
            Attempt(() => { InputSystem.settings.updateMode = _oldUpdateMode; InputSystem.settings.backgroundBehavior = _oldBackground; InputSystem.settings.editorInputBehaviorInPlayMode = _oldEditorInput; });
            Attempt(() => { Application.runInBackground = _oldRunInBackground; Time.captureDeltaTime = _oldCapture; Time.timeScale = _oldTimeScale; });
            foreach (var camera in _cameras) Attempt(() => { if (camera.Camera != null) { camera.Camera.targetTexture = camera.Target; camera.Camera.rect = camera.Rect; camera.Camera.aspect = camera.Aspect; } });
            Attempt(() =>
            {
                if (_motor == null || _controller == null) return;
                bool enabled = _controller.enabled; _controller.enabled = false;
                _motor.transform.SetPositionAndRotation(_oldPosition, _oldRotation); _controller.enabled = enabled;
                Set(_motor, "_pitch", _oldPitch); Set(_motor, "_verticalVelocity", _oldVertical);
            });
            Attempt(() => { if (_cameraRig != null) { Set(_cameraRig, "_shoulder", _oldShoulder); Set(_cameraRig, "_localPose", _oldLocalPose); Set(_cameraRig, "_boom01", _oldBoom); } if (_pivot != null) _pivot.localRotation = _oldPivotRotation; if (_main != null) { _main.transform.localPosition = _oldCameraPosition; _main.fieldOfView = _oldFov; } });
            Attempt(() => { if (_ink != null) SetInk(_oldInk); });
            Attempt(() => { if (_targetTexture != null) { _targetTexture.Release(); Object.Destroy(_targetTexture); } });
            Cursor.lockState = _oldCursor; Cursor.visible = _oldCursorVisible; UnityEngine.Random.state = _random;
            if (errors.Count > 0) throw new AggregateException(errors);
        }
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static T Get<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private).GetValue(instance);
        private static void Set(object instance, string name, object value) => instance.GetType().GetField(name, Private).SetValue(instance, value);
    }
}
