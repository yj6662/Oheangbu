using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Oheangbu.App;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data;
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
    // Editor 전용 유한 재생 검사. 실제 입력 액션→게임플레이→최종 IK를 통과하며 생산 코드 훅은 없다.
    public sealed class DosaInputPlayback
    {
        private const int Fps = 30, Width = 1920, Height = 1080, CaptureEvery = 1;
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DosaInputPlayback _active;
        private static string _lastStatus = "NOT_STARTED";
        private struct DosaReplayInputStage { }
        private struct DosaReplayCaptureStage { }

        [Serializable]
        private sealed class Summary
        {
            public string status, mode, scene, directory, error;
            public bool visuals, capture, restored;
            public string parityStatus = "NOT_RUN";
            public int parityFailures;
            public List<string> parityDetails = new List<string>();
            public int timestampSchedulingDifferences, rawMutationChecks, rawMutations, recognitionChecks, recognitionMismatches, sameRawVisualsOffChecks;
            public float maxRelativeTimeDifference;
            public int frames, plannedFrames, capturedPairs, rawPoints, commits, interruptions, drawingFrames, tipOver2px, unreachableFrames;
            public int inputFps = Fps, videoFps = Fps / CaptureEvery;
            public float maxTipErrorPixels, maxGripErrorMeters, maxBrushLengthMeters, logicalSeconds;
            public double elapsedWallSeconds;
            public string innTraversalStatus = "NOT_RUN";
            public bool innPorchReached, innReturned, innFinalGrounded;
            public int innGroundedFrames, innAirborneFrames, innSupportMisses;
            public float innPorchErrorMeters, innReturnErrorMeters, innPorchRiseMeters, innMaxLateralMeters;
            public Vector3 innApproach, innPorch;
            public string clockNote = "Raw timestamps are actual StrokePoint.Time relative to letter start; unscaledDeltaTime is recorded, not assumed fixed.";
            public string observerNote = "Captured after UpdateAllSkinnedMeshes. Both manual camera renders temporarily force current skin matrices; external review shows the world body, excludes the near rig and screen ink ribbons. Renderer states and matrix-recalculation flags are restored before gameplay continues.";
        }

        [Serializable]
        private sealed class FrameRecord
        {
            public int frame, unityFrame, letter;
            public int pass;
            public bool visuals;
            public string phase;
            public float logicalTime, scaledDeltaTime, unscaledDeltaTime, timeScale, pitch;
            public bool q, leftButton, dodgePressed, drawMode, stroking, hasPointer, shoulder, grounded, isDashing;
            public Vector2 suppliedPoint, pointer, suppliedMove, suppliedKeyboardMove, actualMove;
            public Vector3 playerPosition, playerVelocity, cameraPosition, cameraEuler, brushTip;
            public Vector3 worldRightHand, worldHandGrip, worldBrushGrip, worldIndex3;
            public float worldGripErrorMeters = -1f;
            public float tipErrorPixels, gripErrorMeters, brushLengthMeters, drawingWeight;
            public bool tipReachable, worldHumanoid, nearHumanoid, nearVisible;
        }

        [Serializable]
        private sealed class RawRecord
        {
            public int frame, letter, stroke, point;
            public int pass;
            public Vector2 position;
            public float relativeTime;
        }

        [Serializable]
        private sealed class CommitRecord
        {
            public int frame, letter, strokes, points, splitGroups;
            public int pass;
            public bool success;
            public string character, initial, medial, final;
            public float relativeTime, worstDistance, averageDistance;
        }

        private struct Step
        {
            public string Phase;
            public Vector2 Point, Move, KeyboardMove, LookDelta;
            public bool Q, Stroke, Dodge, Interrupt, SetShoulder, Shoulder, SetPitch;
            public float Pitch;
        }

        private sealed class AssetState
        {
            public InputActionAsset Asset;
            public InputDevice[] Devices;
            public bool WasDirty;
            public readonly List<(InputActionMap map, InputDevice[] devices)> Maps = new List<(InputActionMap, InputDevice[])>();
        }

        private sealed class CameraState
        {
            public Camera Camera;
            public RenderTexture Target;
            public Rect Rect;
            public float Aspect;
        }

        private readonly List<Step> _steps = new List<Step>();
        private readonly List<AssetState> _assets = new List<AssetState>();
        private readonly List<CameraState> _cameras = new List<CameraState>();
        private readonly List<InputDevice> _disabledDevices = new List<InputDevice>();
        private Summary _summary;
        private DrawingInputController _input;
        private PlayerMotor _motor;
        private CharacterController _controller;
        private CameraRigController _cameraRig;
        private DodgeAction _dodge;
        private PlayerVisualDriver _driver;
        private PlayerVisualRig _rig;
        private BrushStrokeFeedAdapter _feed;
        private CombatConfigSO _config;
        private Camera _main, _observer;
        private GameObject _observerObject;
        private Keyboard _keyboard, _oldKeyboard;
        private Mouse _mouse, _oldMouse;
        private Gamepad _gamepad, _oldGamepad;
        private InputAction _move;
        private int _temporaryBinding = -1;
        private bool _moveMapEnabled;
        private InputSettings.UpdateMode _oldUpdateMode;
        private InputSettings.BackgroundBehavior _oldBackgroundBehavior;
        private InputSettings.EditorInputBehaviorInPlayMode _oldEditorInputBehavior;
        private bool _oldRunInBackground;
        private float _oldCaptureDeltaTime, _oldTimeScale, _oldPitch, _oldVerticalVelocity;
        private bool _oldAsyncShaders, _oldDriverEnabled, _oldFeedEnabled, _oldShoulder;
        private Vector3 _oldPosition;
        private Quaternion _oldRotation;
        private CursorLockMode _oldCursorLock;
        private bool _oldCursorVisible;
        private bool _stateCaptured, _finished, _initialised, _subscribed;
        private double _startWall;
        private int _index, _letter, _stroke, _point;
        private int _inputFrame = -1, _capturedFrame = -1;
        private float _letterStart;
        private RenderTexture _mainTarget, _observerTarget;
        private Texture2D _pixels;
        private StreamWriter _frames, _raw, _commits;
        private Renderer[] _worldRenderers, _nearRenderers;
        private SkinnedMeshRenderer[] _captureSkins;
        private Transform _worldRightHand, _worldHandGrip, _worldBrushGrip, _worldIndex3;
        private bool _parity, _visualsActive;
        private bool _innTraversal;
        private int _innStage, _innStageFrames;
        private Vector3 _innApproach, _innPorch;
        private float _innStartGround;
        private int _parityLength;
        private readonly List<RawRecord> _rawOn = new List<RawRecord>(), _rawOff = new List<RawRecord>();
        private readonly List<CommitRecord> _commitsOn = new List<CommitRecord>(), _commitsOff = new List<CommitRecord>();
        private struct RawSnapshot
        {
            public StrokeData Stroke;
            public int Index;
            public StrokePoint Point;
        }
        private sealed class RecognitionProof
        {
            public List<StrokeData> Raw;
            public float ScreenHeight;
            public RecognitionPipeline.Result Before;
        }
        private readonly List<RawSnapshot> _snapshots = new List<RawSnapshot>();
        private readonly List<StrokeData> _letterRaw = new List<StrokeData>();
        private readonly List<RecognitionProof> _pendingProofs = new List<RecognitionProof>();
        private readonly List<RecognitionProof> _onProofs = new List<RecognitionProof>();
        private readonly RecognitionPipeline _recognizerBefore = new RecognitionPipeline(), _recognizerAfter = new RecognitionPipeline();

        public static string Begin() => BeginDraw();
        public static string BeginDraw() => StartPlayback(false, true, true);
        public static string BeginFull() => StartPlayback(true, true, true);
        public static string BeginDrawNoVisuals() => StartPlayback(false, false, false);
        public static string BeginParity() => StartPlayback(false, true, false, true);
        public static string BeginInnTraversal() => StartPlayback(false, true, true, false, true);
        public static string Status() => _active != null ? JsonUtility.ToJson(_active._summary) : _lastStatus;
        public static string Stop()
        {
            if (_active != null) _active.Finish("STOPPED", null);
            return Status();
        }

        private static string StartPlayback(bool full, bool visuals, bool capture, bool parity = false, bool innTraversal = false)
        {
            if (!Application.isPlaying || EditorApplication.isPaused) return "NOT_RUN: active unpaused Play Mode required";
            if (_active != null) return Status();
            if (DosaContextValidation.IsRunning) return "NOT_RUN: stop DosaContextValidation before input playback";
            var scene = SceneManager.GetActiveScene();
            if (scene.name != "C2_PlayerValidation" && scene.name != "C2_CodexWorld")
                return "NOT_RUN: use C2_PlayerValidation or C2_CodexWorld";
            if (innTraversal && scene.path != CodexWorldSceneBuilder.ScenePath) return "NOT_RUN: inn traversal requires C2_CodexWorld";
            var playback = new DosaInputPlayback();
            _active = playback;
            playback._parity = parity;
            playback._innTraversal = innTraversal;
            playback._visualsActive = visuals;
            playback._summary = new Summary { status = "STARTING", mode = innTraversal ? "inn" : parity ? "parity" : full ? "full" : "draw", visuals = visuals, capture = capture, scene = scene.path };
            try
            {
                playback.Initialise(full);
                playback.InstallPlayerLoop();
                playback._summary.status = "RUNNING";
            }
            catch (Exception e) { playback.Finish("ERROR", e.GetBaseException().Message); }
            return Status();
        }

        private void Initialise(bool full)
        {
            _motor = Object.FindFirstObjectByType<PlayerMotor>();
            _driver = Object.FindFirstObjectByType<PlayerVisualDriver>();
            _rig = Object.FindFirstObjectByType<PlayerVisualRig>();
            _input = Object.FindFirstObjectByType<DrawingInputController>();
            _feed = Object.FindFirstObjectByType<BrushStrokeFeedAdapter>();
            _cameraRig = Object.FindFirstObjectByType<CameraRigController>();
            _main = Camera.main;
            if (_motor == null || _driver == null || _rig == null || _input == null || _feed == null || _main == null || _cameraRig == null)
                throw new InvalidOperationException("Dosa player, camera, or drawing component missing");
            if (_input.InDrawMode || !_input.enabled || !_motor.enabled) throw new InvalidOperationException("Begin from idle with player/input enabled");
            _controller = _motor.GetComponent<CharacterController>();
            _dodge = _motor.GetComponent<DodgeAction>();
            _config = Get<CombatConfigSO>(_motor, "_config");
            if (_config == null) throw new InvalidOperationException("Combat config missing");
            var templates = Get<JamoTemplateLibrarySO>(_input, "_templates");
            if (templates == null) throw new InvalidOperationException("Jamo templates missing; recognition parity cannot be tested");
            _recognizerBefore.Initialize(templates); _recognizerAfter.Initialize(templates);
            if (!_recognizerBefore.IsReady) throw new InvalidOperationException("Jamo templates are not ready");

            _oldUpdateMode = InputSystem.settings.updateMode;
            _oldBackgroundBehavior = InputSystem.settings.backgroundBehavior; _oldRunInBackground = Application.runInBackground;
            _oldEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            _oldCaptureDeltaTime = Time.captureDeltaTime; _oldTimeScale = Time.timeScale;
            _oldAsyncShaders = ShaderUtil.allowAsyncCompilation;
            _oldCursorLock = Cursor.lockState; _oldCursorVisible = Cursor.visible;
            _oldKeyboard = Keyboard.current; _oldMouse = Mouse.current; _oldGamepad = Gamepad.current;
            _oldDriverEnabled = _driver.enabled; _oldFeedEnabled = _feed.enabled;
            _oldPosition = _motor.transform.position; _oldRotation = _motor.transform.rotation;
            _oldPitch = Get<float>(_motor, "_pitch"); _oldVerticalVelocity = Get<float>(_motor, "_verticalVelocity");
            _oldShoulder = _cameraRig.IsShoulder;
            _stateCaptured = true;
            _startWall = Time.realtimeSinceStartupAsDouble;
            _summary.directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosa",
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + _summary.mode + (_summary.visuals ? "_visuals" : "_raw")));
            Directory.CreateDirectory(_summary.directory);
            if (_summary.capture)
            {
                Directory.CreateDirectory(Path.Combine(_summary.directory, "play"));
                Directory.CreateDirectory(Path.Combine(_summary.directory, "observer"));
            }
            _frames = Writer("frames.jsonl"); _raw = Writer("raw.jsonl"); _commits = Writer("commits.jsonl");

            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Application.runInBackground = true;
            // 실제 장치는 제거하지 않고 입력만 잠시 차단한다. 종료 때 원래 켜져 있던 장치만 복구한다.
            foreach (var device in InputSystem.devices.ToArray())
                if (device.enabled && (device is Keyboard || device is Mouse || device is Gamepad))
                { _disabledDevices.Add(device); InputSystem.DisableDevice(device); }
            _keyboard = InputSystem.AddDevice<Keyboard>("DosaReplayKeyboard");
            _mouse = InputSystem.AddDevice<Mouse>("DosaReplayMouse");
            _gamepad = InputSystem.AddDevice<Gamepad>("DosaReplayGamepad");
            ScopeAsset(Get<InputActionAsset>(_input, "_actions"));
            ScopeAsset(Get<InputActionAsset>(_motor, "_actions"));
            _move = Get<InputAction>(_motor, "_move");
            if (_move == null) throw new InvalidOperationException("Gameplay Move action missing");
            _moveMapEnabled = _move.actionMap.enabled;
            _move.actionMap.Disable();
            _temporaryBinding = _move.bindings.Count;
            _move.AddBinding("<Gamepad>/leftStick");
            if (_moveMapEnabled) _move.actionMap.Enable();

            Time.captureDeltaTime = 1f / Fps;
            Time.timeScale = 1f;
            ShaderUtil.allowAsyncCompilation = false;
            _mainTarget = NewTarget("DosaReplayMain");
            HoldCamera(_main);
            var data = _main.GetUniversalAdditionalCameraData();
            foreach (var camera in data.cameraStack) if (camera != null) HoldCamera(camera);
            var renderers = _rig.GetComponentsInChildren<Renderer>(true);
            _worldRenderers = renderers.Where(r => r.gameObject.layer != 31).ToArray();
            _nearRenderers = renderers.Where(r => r.gameObject.layer == 31).ToArray();
            _captureSkins = renderers.OfType<SkinnedMeshRenderer>().ToArray();
            var worldBody = _rig.transform.Find("WorldBody");
            var worldBones = worldBody != null ? worldBody.GetComponentsInChildren<Transform>(true) : Array.Empty<Transform>();
            _worldRightHand = worldBones.FirstOrDefault(t => t.name.EndsWith("RightHand", StringComparison.Ordinal));
            _worldHandGrip = worldBones.FirstOrDefault(t => t.name.EndsWith("RightBrushGrip", StringComparison.Ordinal));
            _worldIndex3 = worldBones.FirstOrDefault(t => t.name.EndsWith("RightHandIndex3", StringComparison.Ordinal));
            _worldBrushGrip = _rig.transform.Find("WorldBrush/GripSocket");
            if (_summary.capture)
            {
                _observerTarget = NewTarget("DosaReplayObserver");
                _pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                _observerObject = new GameObject("DosaReplayObserver") { hideFlags = HideFlags.DontSave };
                _observer = _observerObject.AddComponent<Camera>();
                _observer.CopyFrom(_main); _observer.enabled = false; _observer.tag = "Untagged";
                _observer.targetTexture = _observerTarget; _observer.cullingMask &= ~(1 << 31);
                _observer.fieldOfView = 38f; _observer.nearClipPlane = .05f;
                var observerData = _observer.GetUniversalAdditionalCameraData();
                observerData.renderType = CameraRenderType.Base; observerData.cameraStack.Clear();
                observerData.renderPostProcessing = data.renderPostProcessing;
            }
            if (!_summary.visuals) { _driver.enabled = false; _feed.enabled = false; }
            _input.ModeEntered += ModeEntered;
            _input.StrokeStarted += StrokeStarted;
            _input.StrokePointAdded += PointAdded;
            _input.CommitDiagnosed += Diagnosed;
            _input.LetterInterrupted += Interrupted;
            _subscribed = true;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            if (_innTraversal) InitialiseInnTraversal();
            else { PrepareReplayPose(); BuildSequence(full); }
            if (_parity)
            {
                _parityLength = _steps.Count;
                _steps.AddRange(_steps.ToArray());
                _summary.parityStatus = "RUNNING";
            }
            _summary.plannedFrames = _steps.Count;
            _summary.logicalSeconds = _steps.Count / (float)Fps;
            _initialised = true;
        }

        private void ScopeAsset(InputActionAsset asset)
        {
            if (asset == null || _assets.Any(s => s.Asset == asset)) return;
            var state = new AssetState { Asset = asset, Devices = asset.devices?.ToArray(), WasDirty = EditorUtility.IsDirty(asset) };
            _assets.Add(state);
            var devices = new InputDevice[] { _keyboard, _mouse, _gamepad };
            asset.devices = devices;
            foreach (var map in asset.actionMaps)
            {
                state.Maps.Add((map, map.devices?.ToArray()));
                map.devices = devices;
            }
        }

        private void HoldCamera(Camera camera)
        {
            if (_cameras.Any(c => c.Camera == camera)) return;
            _cameras.Add(new CameraState { Camera = camera, Target = camera.targetTexture, Rect = camera.rect, Aspect = camera.aspect });
            camera.targetTexture = _mainTarget; camera.rect = new Rect(0, 0, 1, 1); camera.aspect = Width / (float)Height;
        }

        private static RenderTexture NewTarget(string name)
        {
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = name };
            target.Create();
            return target;
        }

        private StreamWriter Writer(string name) => new StreamWriter(Path.Combine(_summary.directory, name), false, new UTF8Encoding(false));

        private void InstallPlayerLoop()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            RemoveReplayStages(ref loop);
            bool inputAdded = InsertStage(ref loop, typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate),
                new PlayerLoopSystem { type = typeof(DosaReplayInputStage), updateDelegate = Update }, false);
            // Skin matrices must reflect this frame's LateUpdate IK before either camera renders.
            bool captureAdded = InsertStage(ref loop, typeof(UnityEngine.PlayerLoop.PostLateUpdate.UpdateAllSkinnedMeshes),
                new PlayerLoopSystem { type = typeof(DosaReplayCaptureStage), updateDelegate = CaptureLate }, true);
            if (!inputAdded || !captureAdded)
                throw new InvalidOperationException("Required Update/skin-update PlayerLoop stages were not found");
            PlayerLoop.SetPlayerLoop(loop);
        }

        private static bool InsertStage(ref PlayerLoopSystem parent, Type anchor, PlayerLoopSystem stage, bool after)
        {
            var children = parent.subSystemList;
            if (children == null) return false;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].type == anchor)
                {
                    var updated = new List<PlayerLoopSystem>(children);
                    updated.Insert(i + (after ? 1 : 0), stage);
                    parent.subSystemList = updated.ToArray();
                    return true;
                }
                var child = children[i];
                if (!InsertStage(ref child, anchor, stage, after)) continue;
                // Copy the array so a failed installation cannot mutate the live loop in place.
                var replaced = (PlayerLoopSystem[])children.Clone();
                replaced[i] = child;
                parent.subSystemList = replaced;
                return true;
            }
            return false;
        }

        private static bool RemoveReplayStages(ref PlayerLoopSystem parent)
        {
            var children = parent.subSystemList;
            if (children == null) return false;
            var updated = new List<PlayerLoopSystem>(children.Length);
            bool changed = false;
            foreach (var entry in children)
            {
                if (entry.type == typeof(DosaReplayInputStage) || entry.type == typeof(DosaReplayCaptureStage))
                { changed = true; continue; }
                var child = entry;
                changed |= RemoveReplayStages(ref child);
                updated.Add(child);
            }
            if (changed) parent.subSystemList = updated.ToArray();
            return changed;
        }

        private static void UninstallPlayerLoop()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            // Preserve unrelated PlayerLoop changes made while the replay was running.
            if (RemoveReplayStages(ref loop)) PlayerLoop.SetPlayerLoop(loop);
        }

        private void Update()
        {
            if (!_initialised || _finished || _inputFrame == Time.frameCount) return;
            _inputFrame = Time.frameCount;
            try
            {
                if (_index >= _steps.Count) { Finish(_innTraversal ? "FAIL" : "COMPLETE", _innTraversal ? "Inn traversal exceeded 600-frame budget" : null); return; }
                if (_parity && _index == _parityLength)
                {
                    if (_input.InDrawMode) throw new InvalidOperationException("Parity pass ended with drawing mode still active");
                    _driver.enabled = false; _feed.enabled = false; _visualsActive = false;
                    _letter = 0; _stroke = 0; _point = 0;
                    foreach (var proof in _onProofs)
                    {
                        CheckRecognition(proof, _recognizerAfter.Recognize(proof.Raw, proof.ScreenHeight));
                        _summary.sameRawVisualsOffChecks++;
                    }
                    PrepareReplayPose();
                }
                if (_innTraversal) _steps[_index] = BuildInnStep();
                var step = _steps[_index];
                var keys = new List<Key>(7);
                if (step.Q) keys.Add(Key.Q);
                if (step.Dodge) keys.Add(Key.LeftShift);
                if (step.SetShoulder && _cameraRig.IsShoulder != step.Shoulder) keys.Add(Key.V);
                if (step.KeyboardMove.y > 0f) keys.Add(Key.W);
                if (step.KeyboardMove.y < 0f) keys.Add(Key.S);
                if (step.KeyboardMove.x < 0f) keys.Add(Key.A);
                if (step.KeyboardMove.x > 0f) keys.Add(Key.D);
                Vector2 delta = step.SetPitch
                    ? new Vector2(0f, (Get<float>(_motor, "_pitch") - step.Pitch) / Mathf.Max(.001f, _config.LookSensitivity)) : Vector2.zero;
                delta += step.LookDelta;
                Vector2 stick = step.Move;
                if (stick.sqrMagnitude > 0f)
                {
                    float magnitude = Mathf.Lerp(InputSystem.settings.defaultDeadzoneMin, InputSystem.settings.defaultDeadzoneMax, Mathf.Clamp01(stick.magnitude));
                    stick = stick.normalized * magnitude;
                }
                // Move를 먼저 처리해야 같은 입력 업데이트의 회피 콜백도 해당 방향을 읽는다.
                InputSystem.QueueStateEvent(_gamepad, new GamepadState { leftStick = stick });
                InputSystem.QueueStateEvent(_mouse, new MouseState { position = step.Point, delta = delta }.WithButton(MouseButton.Left, step.Stroke));
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys.ToArray()));
                InputSystem.Update();
                _keyboard.MakeCurrent(); _mouse.MakeCurrent(); _gamepad.MakeCurrent();
                if (step.Interrupt) _input.InterruptLetter();
            }
            catch (Exception e) { Finish("ERROR", e.GetBaseException().Message); }
        }

        internal void CaptureLate()
        {
            if (!_initialised || _finished || _index >= _steps.Count || _inputFrame != Time.frameCount || _capturedFrame == Time.frameCount) return;
            _capturedFrame = Time.frameCount;
            try
            {
                var step = _steps[_index];
                var d = _driver.Diagnostics;
                _frames.WriteLine(JsonUtility.ToJson(new FrameRecord
                {
                    frame = _index, unityFrame = Time.frameCount, letter = _letter, phase = step.Phase, pass = Pass, visuals = _visualsActive,
                    logicalTime = _index / (float)Fps, scaledDeltaTime = Time.deltaTime, unscaledDeltaTime = Time.unscaledDeltaTime,
                    timeScale = Time.timeScale, pitch = Get<float>(_motor, "_pitch"), q = step.Q, leftButton = step.Stroke,
                    dodgePressed = step.Dodge, drawMode = _input.InDrawMode, stroking = _input.IsStroking, hasPointer = _input.HasPointer,
                    shoulder = _cameraRig.IsShoulder, grounded = _controller.isGrounded, isDashing = _dodge != null && _dodge.IsDashing,
                    suppliedPoint = step.Point, pointer = _input.PointerScreenPosition, suppliedMove = step.Move,
                    suppliedKeyboardMove = step.KeyboardMove, actualMove = _move.ReadValue<Vector2>(),
                    playerPosition = _motor.transform.position, playerVelocity = _controller.velocity, cameraPosition = _main.transform.position,
                    cameraEuler = _main.transform.eulerAngles, brushTip = d.BrushTipWorld, tipErrorPixels = d.TipScreenErrorPixels,
                    worldRightHand = _worldRightHand != null ? _worldRightHand.position : Vector3.zero,
                    worldHandGrip = _worldHandGrip != null ? _worldHandGrip.position : Vector3.zero,
                    worldBrushGrip = _worldBrushGrip != null ? _worldBrushGrip.position : Vector3.zero,
                    worldIndex3 = _worldIndex3 != null ? _worldIndex3.position : Vector3.zero,
                    worldGripErrorMeters = _worldHandGrip != null && _worldBrushGrip != null
                        ? Vector3.Distance(_worldHandGrip.position, _worldBrushGrip.position) : -1f,
                    gripErrorMeters = d.GripErrorMeters, brushLengthMeters = d.BrushLengthMeters, drawingWeight = d.DrawingWeight,
                    tipReachable = d.TipReachable, worldHumanoid = d.WorldHumanoid, nearHumanoid = d.NearHumanoid, nearVisible = d.NearVisible
                }));
                if (_visualsActive && _input.InDrawMode && _input.HasPointer)
                {
                    _summary.drawingFrames++;
                    _summary.maxTipErrorPixels = Mathf.Max(_summary.maxTipErrorPixels, d.TipScreenErrorPixels);
                    _summary.maxGripErrorMeters = Mathf.Max(_summary.maxGripErrorMeters, d.GripErrorMeters);
                    _summary.maxBrushLengthMeters = Mathf.Max(_summary.maxBrushLengthMeters, d.BrushLengthMeters);
                    if (d.TipScreenErrorPixels > 2f) _summary.tipOver2px++;
                    if (!d.TipReachable) _summary.unreachableFrames++;
                }
                if (_summary.capture && _index % CaptureEvery == 0) CapturePair();
                VerifyRawAfterVisuals();
                foreach (var proof in _pendingProofs) CheckRecognition(proof, _recognizerAfter.Recognize(proof.Raw, proof.ScreenHeight));
                _pendingProofs.Clear();
                _index++;
                _summary.frames = _index;
                _summary.elapsedWallSeconds = Time.realtimeSinceStartupAsDouble - _startWall;
                if (_innTraversal)
                {
                    MeasureInnTraversal();
                    _innStageFrames++;
                    if (_innStage == 4 && _innStageFrames >= 20) { CompleteInnTraversal(); return; }
                }
                if (_index >= _steps.Count) Finish(_innTraversal ? "FAIL" : "COMPLETE", _innTraversal ? "Inn traversal exceeded 600-frame budget" : null);
            }
            catch (Exception e) { Finish("ERROR", e.GetBaseException().Message); }
        }

        private void CapturePair()
        {
            // Manual SRP renders can reuse a skin matrix cache from an earlier camera/visibility state.
            // This is a capture-only refresh of current transforms, never another Animator/IK evaluation.
            var skins = _captureSkins.Where(s => s != null)
                .Select(s => (renderer: s, force: s.forceMatrixRecalculationPerRender)).ToArray();
            try
            {
                foreach (var s in skins) s.renderer.forceMatrixRecalculationPerRender = true;
                CapturePairWithCurrentSkinMatrices();
            }
            finally
            {
                foreach (var s in skins) if (s.renderer != null) s.renderer.forceMatrixRecalculationPerRender = s.force;
            }
        }

        private void CapturePairWithCurrentSkinMatrices()
        {
            string filename = "frame_" + _summary.capturedPairs.ToString("D05") + ".png";
            RenderToFile(_main, _mainTarget, Path.Combine(_summary.directory, "play", filename));
            Vector3 root = _motor.transform.position;
            Vector3 forward = _motor.transform.forward;
            _observer.transform.position = root + _motor.transform.right * 2.4f + forward * 3.0f + Vector3.up * .9f;
            if (_innTraversal)
            {
                // Keep the external review camera outside the porch, independent of the return turn.
                Vector3 route = _innPorch - _innApproach; route.y = 0f; route.Normalize();
                _observer.transform.position = root + Vector3.Cross(Vector3.up, route) * 2.7f - route * 2.8f + Vector3.up * .9f;
            }
            _observer.transform.LookAt(root + Vector3.up * .05f);
            var world = _worldRenderers.Select(r => (renderer: r, enabled: r.enabled, shadow: r.shadowCastingMode)).ToArray();
            var near = _nearRenderers.Select(r => (renderer: r, enabled: r.enabled)).ToArray();
            var ink = Object.FindObjectsByType<BrushStrokeRenderer>(FindObjectsSortMode.None)
                .SelectMany(s => s.GetComponentsInChildren<Renderer>()).Distinct()
                .Select(r => (renderer: r, enabled: r.enabled)).ToArray();
            try
            {
                foreach (var s in world) { s.renderer.enabled = true; s.renderer.shadowCastingMode = ShadowCastingMode.On; }
                foreach (var s in near) s.renderer.enabled = false;
                foreach (var s in ink) s.renderer.enabled = false;
                RenderToFile(_observer, _observerTarget, Path.Combine(_summary.directory, "observer", filename));
            }
            finally
            {
                foreach (var s in world) if (s.renderer != null) { s.renderer.enabled = s.enabled; s.renderer.shadowCastingMode = s.shadow; }
                foreach (var s in near) if (s.renderer != null) s.renderer.enabled = s.enabled;
                foreach (var s in ink) if (s.renderer != null) s.renderer.enabled = s.enabled;
            }
            _summary.capturedPairs++;
        }

        private void RenderToFile(Camera camera, RenderTexture target, string path)
        {
            var active = RenderTexture.active;
            try
            {
                if (GraphicsSettings.currentRenderPipeline != null)
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                else camera.Render();
                RenderTexture.active = target;
                _pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                _pixels.Apply(false, false);
                File.WriteAllBytes(path, _pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = active; }
        }

        private void ModeEntered() { _letter++; _stroke = 0; _point = 0; _letterStart = Time.unscaledTime; _letterRaw.Clear(); }
        private void StrokeStarted()
        {
            _stroke++; _point = 0;
            var raw = Get<StrokeData>(_input, "_currentStroke");
            if (raw != null) _letterRaw.Add(raw);
        }
        private void Interrupted() { _summary.interruptions++; _letter++; _stroke = 0; _point = 0; _letterStart = Time.unscaledTime; _letterRaw.Clear(); }
        private void PointAdded(Vector2 screen)
        {
            var stroke = Get<StrokeData>(_input, "_currentStroke");
            if (stroke == null || stroke.Points.Count == 0) return;
            var raw = stroke.Points[stroke.Points.Count - 1];
            _snapshots.Add(new RawSnapshot { Stroke = stroke, Index = stroke.Points.Count - 1, Point = raw });
            var record = new RawRecord { frame = PassFrame, pass = Pass, letter = _letter, stroke = _stroke, point = _point++, position = raw.Position, relativeTime = raw.Time - _letterStart };
            _raw.WriteLine(JsonUtility.ToJson(record));
            if (_parity) (Pass == 0 ? _rawOn : _rawOff).Add(record);
            _summary.rawPoints++;
        }

        private void Diagnosed(DrawingInputController.CommitDiagnostics diagnostic)
        {
            var record = new CommitRecord
            {
                frame = PassFrame, pass = Pass, letter = _letter, success = diagnostic.Success, character = diagnostic.Success ? diagnostic.Letter.ToString() : "",
                strokes = diagnostic.StrokeCount, points = diagnostic.PointCount, splitGroups = diagnostic.SplitGroupCount,
                initial = diagnostic.InitialName, medial = diagnostic.MedialName, final = diagnostic.FinalName,
                relativeTime = Time.unscaledTime - _letterStart, worstDistance = diagnostic.WorstDistance, averageDistance = diagnostic.AverageDistance
            };
            _commits.WriteLine(JsonUtility.ToJson(record));
            if (_parity) (Pass == 0 ? _commitsOn : _commitsOff).Add(record);
            // 원본 좌표·시각을 그대로 복사한다. 실제 인식기나 게임 데이터에 되써 넣지 않는다.
            var proof = new RecognitionProof { Raw = new List<StrokeData>(), ScreenHeight = Screen.height };
            foreach (var source in _letterRaw)
            {
                var clone = new StrokeData();
                foreach (var point in source.Points) clone.Add(point);
                proof.Raw.Add(clone);
            }
            proof.Before = _recognizerBefore.Recognize(proof.Raw, proof.ScreenHeight);
            _pendingProofs.Add(proof);
            if (_parity && Pass == 0) _onProofs.Add(proof);
            _summary.commits++;
        }

        private void VerifyRawAfterVisuals()
        {
            foreach (var snapshot in _snapshots)
            {
                _summary.rawMutationChecks++;
                if (snapshot.Stroke.Points.Count <= snapshot.Index) { _summary.rawMutations++; continue; }
                var after = snapshot.Stroke.Points[snapshot.Index];
                if (after.Position.x != snapshot.Point.Position.x || after.Position.y != snapshot.Point.Position.y || after.Time != snapshot.Point.Time)
                    _summary.rawMutations++;
            }
            _snapshots.Clear();
        }

        private void CheckRecognition(RecognitionProof proof, RecognitionPipeline.Result after)
        {
            _summary.recognitionChecks++;
            var before = proof.Before;
            if (before.Success != after.Success || before.InitialName != after.InitialName || before.MedialName != after.MedialName
                || before.FinalName != after.FinalName || before.SplitGroupCount != after.SplitGroupCount
                || before.WorstDistance != after.WorstDistance || before.AverageDistance != after.AverageDistance)
                _summary.recognitionMismatches++;
        }

        private int Pass => _parity && _index >= _parityLength ? 1 : 0;
        private int PassFrame => _parity && _index >= _parityLength ? _index - _parityLength : _index;

        private void CompareParity()
        {
            if (_rawOn.Count != _rawOff.Count) ParityFailure("Raw counts " + _rawOn.Count + " / " + _rawOff.Count);
            if (_commitsOn.Count != _commitsOff.Count) ParityFailure("Commit counts " + _commitsOn.Count + " / " + _commitsOff.Count);
            if (_rawOn.Count == 0 || _commitsOn.Count == 0) ParityFailure("Replay produced no raw data or diagnostics");
            for (int i = 0; i < Mathf.Min(_rawOn.Count, _rawOff.Count); i++)
            {
                var a = _rawOn[i]; var b = _rawOff[i];
                if (a.frame != b.frame || a.letter != b.letter || a.stroke != b.stroke || a.point != b.point
                    || a.position.x != b.position.x || a.position.y != b.position.y)
                    ParityFailure("Raw sample " + i + " differs; relative time " + a.relativeTime + " / " + b.relativeTime);
                CompareScheduling(a.relativeTime, b.relativeTime);
            }
            for (int i = 0; i < Mathf.Min(_commitsOn.Count, _commitsOff.Count); i++)
            {
                var a = _commitsOn[i]; var b = _commitsOff[i];
                if (a.frame != b.frame || a.letter != b.letter || a.strokes != b.strokes || a.points != b.points
                    || a.success != b.success || a.character != b.character || a.initial != b.initial || a.medial != b.medial
                    || a.final != b.final || a.splitGroups != b.splitGroups || a.worstDistance != b.worstDistance
                    || a.averageDistance != b.averageDistance)
                    ParityFailure("Commit " + i + " differs; frame " + a.frame + " / " + b.frame + ", letter " + a.character + " / " + b.character);
                CompareScheduling(a.relativeTime, b.relativeTime);
            }
            if (_summary.rawMutations != 0) ParityFailure("Raw changed after visuals: " + _summary.rawMutations);
            if (_summary.recognitionMismatches != 0) ParityFailure("Same Raw recognition changed: " + _summary.recognitionMismatches);
            _summary.parityStatus = _summary.parityFailures == 0 ? "PASS" : "FAIL";
        }

        private void CompareScheduling(float before, float after)
        {
            float delta = Mathf.Abs(before - after);
            _summary.maxRelativeTimeDifference = Mathf.Max(_summary.maxRelativeTimeDifference, delta);
            if (delta > .0001f) _summary.timestampSchedulingDifferences++;
        }

        private void ParityFailure(string detail)
        {
            _summary.parityFailures++;
            if (_summary.parityDetails.Count < 20) _summary.parityDetails.Add(detail);
        }

        private void InitialiseInnTraversal()
        {
            var lockOn = _motor.transform.root.GetComponentInChildren<LockOn>(true);
            if (lockOn != null && lockOn.IsLocked) throw new InvalidOperationException("Begin inn traversal from unlocked player");
            var settings = AssetDatabase.LoadAssetAtPath<CodexWorldSettingsSO>(CodexWorldSceneBuilder.SettingsPath);
            var inn = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .SingleOrDefault(t => t.name == CodexThatchedInn.RootName);
            if (settings == null || inn == null) throw new InvalidOperationException("Inn settings or canonical thatched inn missing");
            _innApproach = inn.TransformPoint(settings.InnApproachLocal); _innPorch = inn.TransformPoint(settings.InnPorchLocal);
            if (!TryInnGround(_innApproach + Vector3.up * 5f, 20f, out RaycastHit ground))
                throw new InvalidOperationException("No scene ground under inn approach");
            _innStartGround = ground.point.y;
            Vector3 route = _innPorch - _innApproach; route.y = 0f;
            if (route.magnitude < .5f) throw new InvalidOperationException("Inn route too short for stairs validation");
            float bottomOffset = (_controller.height * .5f - _controller.center.y) * Mathf.Abs(_motor.transform.lossyScale.y);
            _controller.enabled = false;
            _motor.transform.SetPositionAndRotation(new Vector3(_innApproach.x, ground.point.y + bottomOffset + .05f, _innApproach.z), Quaternion.LookRotation(route));
            _controller.enabled = true;
            Set(_motor, "_pitch", 0f); Set(_motor, "_verticalVelocity", 0f);
            Physics.SyncTransforms();
            _summary.innApproach = _innApproach; _summary.innPorch = _innPorch; _summary.innTraversalStatus = "RUNNING";
            // 최대 20초 예산. 각 프레임의 실제 위치를 보고 입력을 결정하며 CC.Move를 직접 호출하지 않는다.
            Add("inn traversal frame budget", 600);
        }

        private Step BuildInnStep()
        {
            if (_innStage == 0 && _innStageFrames >= 20) { _innStage = 1; _innStageFrames = 0; }
            if (_innStage == 2 && _innStageFrames >= 20) { _innStage = 3; _innStageFrames = 0; }
            var step = new Step { Point = new Vector2(960, 540), SetShoulder = true, Shoulder = true, SetPitch = true, Pitch = 0f };
            if (_innStage == 0) { step.Phase = "inn approach settle"; return step; }
            if (_innStage == 2) { step.Phase = "inn porch arrival hold"; return step; }
            if (_innStage == 4) { step.Phase = "inn returned approach hold"; return step; }
            Vector3 target = _innStage == 1 ? _innPorch : _innApproach;
            Vector3 delta = target - _motor.transform.position; delta.y = 0f;
            float distance = delta.magnitude;
            step.Phase = _innStage == 1 ? "inn climb toward porch" : "inn descend toward approach";
            if (distance <= .08f)
            {
                if (_controller.isGrounded)
                {
                    if (_innStage == 1)
                    {
                        _summary.innPorchReached = true; _summary.innPorchErrorMeters = distance;
                        _summary.innPorchRiseMeters = _controller.bounds.min.y - _innStartGround;
                        _innStage = 2;
                    }
                    else { _summary.innReturned = true; _summary.innReturnErrorMeters = distance; _innStage = 4; }
                    _innStageFrames = 0;
                }
                return step;
            }
            float desiredYaw = Quaternion.LookRotation(delta).eulerAngles.y;
            float yawError = Mathf.DeltaAngle(_motor.transform.eulerAngles.y, desiredYaw);
            float turn = Mathf.Clamp(yawError, -6f, 6f);
            step.LookDelta = new Vector2(turn / Mathf.Max(.001f, _config.LookSensitivity), 0f);
            // 방향 전환도 마우스 입력을 통과한다. 앞으로 바라본 뒤 기존 analog Move로 1.6m/s를 요청한다.
            if (Mathf.Abs(yawError - turn) < .75f)
                step.Move = Vector2.up * Mathf.Clamp01(Mathf.Min(1.6f, distance / Mathf.Max(.001f, Time.deltaTime)) / _config.MoveSpeed);
            return step;
        }

        private bool TryInnGround(Vector3 origin, float distance, out RaycastHit ground)
        {
            foreach (var hit in Physics.RaycastAll(origin, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (hit.collider == null || hit.collider.transform.IsChildOf(_motor.transform.root) || hit.normal.y < .4f) continue;
                ground = hit; return true;
            }
            ground = default; return false;
        }

        private void MeasureInnTraversal()
        {
            if (_innStage == 0) return;
            if (_controller.isGrounded) _summary.innGroundedFrames++; else _summary.innAirborneFrames++;
            Vector3 position = _motor.transform.position;
            Vector3 origin = new Vector3(position.x, _controller.bounds.min.y + .3f, position.z);
            if (!TryInnGround(origin, 2f, out _)) _summary.innSupportMisses++;
            Vector2 start = new Vector2(_innApproach.x, _innApproach.z), end = new Vector2(_innPorch.x, _innPorch.z);
            Vector2 point = new Vector2(position.x, position.z), axis = end - start;
            Vector2 nearest = start + axis * Mathf.Clamp01(Vector2.Dot(point - start, axis) / Mathf.Max(.0001f, axis.sqrMagnitude));
            _summary.innMaxLateralMeters = Mathf.Max(_summary.innMaxLateralMeters, Vector2.Distance(point, nearest));
        }

        private void CompleteInnTraversal()
        {
            Vector3 delta = _motor.transform.position - _innApproach; delta.y = 0f;
            _summary.innReturnErrorMeters = delta.magnitude; _summary.innFinalGrounded = _controller.isGrounded;
            bool passed = _summary.innPorchReached && _summary.innReturned && _summary.innFinalGrounded
                && _summary.innReturnErrorMeters <= .12f && _summary.innSupportMisses == 0;
            _summary.innTraversalStatus = passed ? "PASS" : "FAIL";
            _summary.logicalSeconds = _index / (float)Fps;
            Finish(passed ? "COMPLETE" : "FAIL", passed ? null : "Inn traversal arrival or supporting-ground check failed; see inn metrics");
        }

        private void BuildSequence(bool full)
        {
            Add("settle shoulder", 8, shoulder: true);
            if (full)
            {
                var directions = new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
                foreach (var direction in directions)
                {
                    Add("walk " + direction, 12, move: direction * Mathf.Clamp01(1.6f / _config.MoveSpeed));
                    Add("walk settle", 3);
                }
                var runs = new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right, new Vector2(1, 1).normalized,
                    new Vector2(-1, -1).normalized, new Vector2(-1, 1).normalized, new Vector2(1, -1).normalized };
                foreach (var direction in runs) { Add("run " + direction, 10, move: direction); Add("run settle", 2); }
                // 실제 WASD 합성 바인딩도 통과한다. 대각선 크기를 검사기에서 정규화하지 않는다.
                var keyboardRuns = new[] { ("W+D", new Vector2(1, 1)), ("S+A", new Vector2(-1, -1)),
                    ("W+A", new Vector2(-1, 1)), ("S+D", new Vector2(1, -1)) };
                foreach (var run in keyboardRuns)
                {
                    Add("keyboard diagonal " + run.Item1, 10, keyboardMove: run.Item2);
                    Add("keyboard release", 2);
                }
                int cooldown = Mathf.Max(19, Mathf.CeilToInt(_config.DodgeCooldown * Fps) + 2);
                foreach (var direction in directions)
                {
                    Add("dodge prepare", 1, move: direction);
                    Add("dodge " + direction, 1, move: direction, dodge: true);
                    Add("dodge recover", cooldown);
                }
            }
            // Q와 좌클릭을 같은 프레임에 누르는 진입 및 정상 한글 다획(가).
            Stroke("instant center ㄱ", new[] { new Vector2(580, 760), new Vector2(860, 760), new Vector2(860, 390) }, 6);
            Add("lift", 2, q: true);
            Stroke("center ㅏ vertical", new[] { new Vector2(1080, 800), new Vector2(1080, 340) }, 8);
            Add("lift", 2, q: true);
            Stroke("center ㅏ horizontal", new[] { new Vector2(1080, 590), new Vector2(1330, 590) }, 6);
            Add("commit center", 4);
            Stroke("four corners and edges", new[] { new Vector2(24, 24), new Vector2(1896, 24), new Vector2(1896, 1056), new Vector2(24, 1056), new Vector2(24, 24) }, 5);
            Add("commit border", 4);
            for (int i = 0; i < 24; i++)
            {
                float angle = i / 23f * Mathf.PI * 2f;
                Add("circle while moving", 1, q: true, stroke: true, point: new Vector2(960 + 280 * Mathf.Cos(angle), 540 + 280 * Mathf.Sin(angle)), move: Vector2.up * .25f);
            }
            Add("commit circle", 4);
            Stroke("sharp turns", new[] { new Vector2(400, 800), new Vector2(1450, 350), new Vector2(600, 280), new Vector2(1520, 850) }, 3);
            Add("commit turns", 4);
            Stroke("before interruption", new[] { new Vector2(700, 550), new Vector2(1050, 550) }, 6);
            Add("interrupt held stroke", 1, q: true, stroke: true, point: new Vector2(1050, 550), interrupt: true);
            Stroke("resume without releasing Q", new[] { new Vector2(1050, 550), new Vector2(1300, 750) }, 7);
            Add("commit resumed", 4);
            Add("first person toggle", 3, shoulder: false);
            Add("pitch up clamp", 3, pitch: -80f);
            Stroke("draw at -80 pitch", new[] { new Vector2(750, 600), new Vector2(1150, 600) }, 8);
            Add("commit up", 3);
            Add("pitch down clamp", 3, pitch: 80f);
            Stroke("draw at +80 pitch", new[] { new Vector2(750, 480), new Vector2(1150, 480) }, 8);
            Add("commit down", 3);
            Add("pitch center", 3, pitch: 0f);
            Add("return shoulder", 6, shoulder: true);
            Add("released controls", 3);
        }

        private void Stroke(string label, Vector2[] points, int framesPerEdge)
        {
            for (int edge = 0; edge < points.Length - 1; edge++)
                for (int i = 0; i < framesPerEdge; i++)
                    Add(label, 1, q: true, stroke: true, point: Vector2.Lerp(points[edge], points[edge + 1], i / (float)(framesPerEdge - 1)));
        }

        private void Add(string phase, int count, bool q = false, bool stroke = false, Vector2? point = null,
            Vector2? move = null, bool dodge = false, bool interrupt = false, bool? shoulder = null, float? pitch = null,
            Vector2? keyboardMove = null)
        {
            for (int i = 0; i < count; i++) _steps.Add(new Step
            {
                Phase = phase, Q = q, Stroke = stroke, Point = point ?? new Vector2(960, 540), Move = move ?? Vector2.zero,
                KeyboardMove = keyboardMove ?? Vector2.zero,
                Dodge = dodge, Interrupt = interrupt, SetShoulder = shoulder.HasValue, Shoulder = shoulder.GetValueOrDefault(),
                SetPitch = pitch.HasValue, Pitch = pitch.GetValueOrDefault()
            });
        }

        private void PlayModeChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Finish("PLAY_EXIT", null); }
        private void BeforeReload() => Finish("DOMAIN_RELOAD", null);

        private void Finish(string status, string error)
        {
            if (_finished) return;
            _finished = true;
            _summary.status = status; _summary.error = error;
            if (_innTraversal && _summary.innTraversalStatus == "RUNNING") _summary.innTraversalStatus = status;
            if (_parity && status == "COMPLETE") CompareParity();
            else if (_parity) _summary.parityStatus = "INCOMPLETE";
            try { Restore(); _summary.restored = true; }
            catch (Exception e) { _summary.error = (error ?? "") + " RESTORE: " + e.GetBaseException().Message; }
            finally
            {
                _summary.elapsedWallSeconds = _stateCaptured ? Time.realtimeSinceStartupAsDouble - _startWall : 0;
                try
                {
                    _frames?.Dispose(); _raw?.Dispose(); _commits?.Dispose();
                    _lastStatus = JsonUtility.ToJson(_summary);
                    if (!string.IsNullOrEmpty(_summary.directory)) File.WriteAllText(Path.Combine(_summary.directory, "summary.json"), _lastStatus, new UTF8Encoding(false));
                }
                catch (Exception e)
                {
                    _summary.error = (_summary.error ?? "") + " SAVE: " + e.GetBaseException().Message;
                    _lastStatus = JsonUtility.ToJson(_summary);
                }
                finally
                {
                    _active = null;
                }
            }
        }

        private void Restore()
        {
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            UninstallPlayerLoop();
            if (_subscribed && _input != null)
            {
                _input.ModeEntered -= ModeEntered; _input.StrokeStarted -= StrokeStarted; _input.StrokePointAdded -= PointAdded;
                _input.CommitDiagnosed -= Diagnosed; _input.LetterInterrupted -= Interrupted;
            }
            if (!_stateCaptured) return;
            var failures = new List<Exception>();
            void RestorePart(Action action)
            {
                try { action(); } catch (Exception e) { failures.Add(e); }
            }
            RestorePart(() => { if (_input != null && _input.InDrawMode) { _input.enabled = false; _input.enabled = true; } });
            RestorePart(() =>
            {
                if (_move == null || _temporaryBinding < 0) return;
                try { _move.actionMap.Disable(); _move.ChangeBinding(_temporaryBinding).Erase(); _temporaryBinding = -1; }
                finally { if (_moveMapEnabled) _move.actionMap.Enable(); }
            });
            foreach (var state in _assets)
                RestorePart(() =>
                {
                    if (state.Asset == null) return;
                    state.Asset.devices = state.Devices;
                    foreach (var map in state.Maps) map.map.devices = map.devices;
                });
            foreach (var device in new InputDevice[] { _keyboard, _mouse, _gamepad })
                RestorePart(() => { if (device != null && device.added) InputSystem.RemoveDevice(device); });
            foreach (var device in _disabledDevices) RestorePart(() => { if (device != null && device.added) InputSystem.EnableDevice(device); });
            RestorePart(() => { if (_oldKeyboard != null && _oldKeyboard.added) _oldKeyboard.MakeCurrent(); });
            RestorePart(() => { if (_oldMouse != null && _oldMouse.added) _oldMouse.MakeCurrent(); });
            RestorePart(() => { if (_oldGamepad != null && _oldGamepad.added) _oldGamepad.MakeCurrent(); });
            RestorePart(() => InputSystem.settings.updateMode = _oldUpdateMode);
            RestorePart(() => InputSystem.settings.backgroundBehavior = _oldBackgroundBehavior);
            RestorePart(() => InputSystem.settings.editorInputBehaviorInPlayMode = _oldEditorInputBehavior);
            Application.runInBackground = _oldRunInBackground;
            Time.captureDeltaTime = _oldCaptureDeltaTime; Time.timeScale = _oldTimeScale;
            ShaderUtil.allowAsyncCompilation = _oldAsyncShaders;
            foreach (var camera in _cameras) RestorePart(() =>
            { if (camera.Camera != null) { camera.Camera.targetTexture = camera.Target; camera.Camera.rect = camera.Rect; camera.Camera.aspect = camera.Aspect; } });
            RestorePart(RestorePlayerPose);
            RestorePart(() => { if (_feed != null) _feed.enabled = _oldFeedEnabled; });
            RestorePart(() => { if (_driver != null) _driver.enabled = _oldDriverEnabled; });
            foreach (var state in _assets) RestorePart(() => { if (state.Asset != null && !state.WasDirty) EditorUtility.ClearDirty(state.Asset); });
            Cursor.lockState = _oldCursorLock; Cursor.visible = _oldCursorVisible;
            if (_observerObject != null) Object.Destroy(_observerObject);
            if (_mainTarget != null) { _mainTarget.Release(); Object.Destroy(_mainTarget); }
            if (_observerTarget != null) { _observerTarget.Release(); Object.Destroy(_observerTarget); }
            if (_pixels != null) Object.Destroy(_pixels);
            if (failures.Count != 0) throw new AggregateException("Some replay state could not be restored", failures);
        }

        private void PrepareReplayPose()
        {
            // Only fixture/pass setup uses a deterministic view. Original user pose stays intact for final restoration.
            RestorePlayerPose();
            if (_motor == null) return;
            _motor.transform.rotation = Quaternion.identity;
            Set(_motor, "_pitch", 0f);
        }

        private void RestorePlayerPose()
        {
            if (_motor != null && _controller != null)
            {
                bool enabledController = _controller.enabled;
                _controller.enabled = false;
                _motor.transform.SetPositionAndRotation(_oldPosition, _oldRotation);
                _controller.enabled = enabledController;
                Set(_motor, "_pitch", _oldPitch); Set(_motor, "_verticalVelocity", _oldVerticalVelocity);
            }
            if (_cameraRig != null) Set(_cameraRig, "_shoulder", _oldShoulder);
        }

        private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Fields).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields).SetValue(target, value);
    }

}
