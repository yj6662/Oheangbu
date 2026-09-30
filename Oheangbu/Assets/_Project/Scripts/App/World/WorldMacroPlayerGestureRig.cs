using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using Oheangbu.Presentation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.World
{
    /// <summary>
    /// C02 presentation only. The final brush feed is read after its LateUpdate; gameplay input,
    /// recognizer samples, movement, camera transforms and resource rules are never written here.
    /// The optional close-up arm has a copied bone hierarchy and a stripped skin, not another Animator.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed partial class WorldMacroPlayerGestureRig : MonoBehaviour
    {
        public enum GestureState { Carry, Drawing, PenLift, Commit, Interrupted, Harvest, Suspended }

        [Serializable]
        public struct GestureDiagnostics
        {
            public bool Bound, NearBound, NearVisible, TipReachable, Paused, Seated, ShaftMeasurementAvailable;
            public int CalibratedFingers, MissingFingers;
            public GestureState State;
            public float DrawingWeight, HarvestWeight, GripErrorMeters, NearTipErrorPixels;
            public float UpperArmLengthErrorMeters, ForearmLengthErrorMeters, ShaftLengthErrorMeters;
            public float UpperArmWorldCoordinateDeltaMeters, ForearmWorldCoordinateDeltaMeters;
            public float ShaftSpanMeters, BrushWorldScaleError;
            public float NearShoulderCorrectionMeters, NearEndpointCorrectionMeters, NearTipErrorBeforeCorrectionPixels;
            public Vector3 EffectTipWorld, WorldWristTarget, NearWristTarget;
            public bool OverhandActive;
            public float NearBrushThumbSideDot;
            public float NearElbowHeight, WorldElbowHeight, NearElbowFlexion, WorldElbowFlexion;
            public float NearWristBendDegrees, NearWristTwistDegrees, NearDorsalUp, NearForearmTwistDegrees;
            public float StrokeArmShare;
            public Vector2 StrokeSweep;
            public bool VerticalActive;
            public float VerticalBlend, NearDorsalRight, NearVerticalAxisErrorDegrees, NearVerticalConeDegrees;
            public int StretchedHandles, HandleStretchRejected;
        }

        [SerializeField] private WorldMacroPlayerGestureProfile _profile;
        [SerializeField] private Animator _animator;
        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private CharacterController _controller;
        [SerializeField] private DrawingInputController _drawing;
        [SerializeField] private BrushStrokeFeedAdapter _brushFeed;
        [SerializeField] private CameraRigController _cameraRig;
        [SerializeField] private WorldMacroCombatWalker _walker;
        [SerializeField] private HarvestAction _harvest;
        [SerializeField] private HarvestInkStreamEffect _harvestEffect;
        [SerializeField] private Transform _worldBrushRoot, _worldGripSocket, _worldTipSocket;
        [SerializeField] private Transform _nearRoot, _nearBrushRoot, _nearGripSocket, _nearTipSocket;
        [SerializeField] private Renderer[] _nearRenderers = Array.Empty<Renderer>();
        [SerializeField] private Transform _effectTip;

        private sealed class Body
        {
            public Transform Root, Upper, Forearm, Hand, Shoulder, Chest;
            public Transform LeftUpper, LeftForearm, LeftHand;
            public float UpperLength, ForearmLength;
            public float UpperWorldLength, ForearmWorldLength;
            public bool Valid => Upper != null && Forearm != null && Hand != null;
        }

        private sealed class Brush
        {
            public Transform Root, Grip, Tip, ShaftEnd;
            public Quaternion RootToGripRotation;
            public Vector3 RestTipOffset;
            public float ShaftSpan;
            public Renderer[] Renderers;
            public BrushBristleRig Bristles;
            public Vector3 TipOffset => Bristles != null && Bristles.IsBound && Bristles.Pose.Valid
                ? Bristles.Pose.TipOffsetInGripLocal : RestTipOffset;
        }

        private struct FingerBinding
        {
            public Transform World, Near;
            public WorldMacroPlayerGestureProfile.FingerPose Pose;
            public Quaternion Rest;
        }

        private struct PoseRestore
        {
            public Transform Bone;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        private struct PoseCopy { public Transform Source, Target; }

        private Body _world, _near;
        private Brush _worldBrush, _nearBrush;
        private float _nearRaise;
        private FingerBinding[] _fingers = Array.Empty<FingerBinding>();
        private PoseRestore[] _restores = Array.Empty<PoseRestore>();
        private PoseCopy[] _nearCopies = Array.Empty<PoseCopy>();
        private DrawingInputController _subscribedDrawing;
        private WorldMacroPlayerSkinningLease _skinningLease;
        private Transform _previousHarvestSink;
        private bool _hasAppliedPose, _bound, _hasPointerHistory, _wasStroking, _nearVisible, _wasSuspended;
        private float _drawWeight, _harvestWeight, _recoveryRemaining;
        private GestureState _recoveryState;
        private Vector2 _bodyPoint, _lastPointer, _strokeMin, _strokeMax;
        private GestureDiagnostics _diagnostics;
        private bool _nearHasSolution;
        private Vector3 _nearLastAim, _nearLastElbow;
        private float _nearLastDepth;
        private Vector2 _nearLastScreen;
        private BrushStrokeMotion _strokeMotion;
        private Vector3 _nearStrokeWrist, _nearStrokeGrip, _worldStrokeGrip;
        private bool _strokeAnchorReady;
        // #300 cast follow-through (presentation only): after a successful commit the close-up arm flicks past the
        // launch point, then arm and brush lower out of frame together; the world arm keeps a short forward reach.
        private bool _castActive;
        private float _castTime, _castReach;
        private Vector2 _castFrom, _castTo, _lastNearViewport = new Vector2(.5f, .5f);
        private Camera _castCamera;
        public bool IsCasting => _castActive;

        // Optional vertical double-hook grip state. Follow targets live in the camera (close-up) or actor root
        // (world) frame, so walking or turning never reads as lag.
        private struct VerticalFollow
        {
            public bool Valid, PoleValid;
            public Vector3 Grip, Pole, Shoulder;
            public float Depth, Lift;
            public Vector2 Screen;
        }

        private struct HandleStretch { public Transform Target; public Vector3 Position, Scale; }

        private VerticalFollow _vNear, _vWorld;
        private List<HandleStretch> _stretched = new List<HandleStretch>(4);
        private readonly float[] _depthCosts = new float[VerticalDepthSamples + 1];
        private float _frameDt;
        private Vector2 _frameSweep;
        private const int VerticalDepthSamples = 24;

        public WorldMacroPlayerGestureProfile Profile => _profile;
        public GestureDiagnostics Diagnostics => _diagnostics;
        public bool IsPresentationReady => isActiveAndEnabled && _bound;
        public string BindingError { get; private set; }
        public Transform EffectTip => _effectTip;
        public Transform WorldTip => _worldTipSocket;

        public void Configure(WorldMacroPlayerGestureProfile profile, Animator animator,
            PlayerMotor motor, CharacterController controller, DrawingInputController drawing,
            BrushStrokeFeedAdapter brushFeed, CameraRigController cameraRig,
            WorldMacroCombatWalker walker, HarvestAction harvest, HarvestInkStreamEffect harvestEffect = null)
        {
            RestoreAnimatedPose();
            Unsubscribe();
            ReleaseHarvestSink();
            _profile = profile; _animator = animator; _motor = motor; _controller = controller;
            _drawing = drawing; _brushFeed = brushFeed; _cameraRig = cameraRig;
            _walker = walker; _harvest = harvest; _harvestEffect = harvestEffect;
            TryBind();
            if (isActiveAndEnabled) Subscribe();
        }

        public void ConfigureWorldBrush(Transform root, Transform gripSocket = null, Transform tipSocket = null)
        {
            RestoreAnimatedPose();
            _worldBrushRoot = root;
            _worldGripSocket = gripSocket != null ? gripSocket : Find(root, "GripSocket");
            _worldTipSocket = tipSocket != null ? tipSocket : Find(root, "TipSocket");
            TryBind();
        }

        public void ConfigureNearArm(Transform root, Transform brushRoot, Transform gripSocket,
            Transform tipSocket, Renderer[] armRenderers)
        {
            SetNearVisible(false);
            _nearRoot = root; _nearBrushRoot = brushRoot;
            _nearGripSocket = gripSocket != null ? gripSocket : Find(brushRoot, "GripSocket");
            _nearTipSocket = tipSocket != null ? tipSocket : Find(brushRoot, "TipSocket");
            _nearRenderers = armRenderers ?? Array.Empty<Renderer>();
            TryBind();
        }

        private void Awake() { TryBind(); }
        private void OnEnable() { TryBind(); Subscribe(); }

        public bool TryBind()
        {
            RestoreHandleStretch();
            SetSkinningBound(false);
            RestoreAnimatedPose();
            SetNearVisible(false);
            _cameraRig?.SetDrawingPresentationActive(false);
            ReleaseHarvestSink();
            _worldBrush?.Bristles?.ResetDeformation();
            _nearBrush?.Bristles?.ResetDeformation();
            _bound = false;
            _diagnostics.Bound = false;
            _diagnostics.NearBound = false;
            BindingError = null;
            if (_profile == null || _animator == null)
            {
                BindingError = "Gesture profile and world Animator are required.";
                return false;
            }
            _world = BindBody(_animator.transform, true);
            ApplyBrushSize(_worldBrushRoot); ApplyBrushSize(_nearBrushRoot);
            _worldBrush = BindBrush(_worldBrushRoot, _worldGripSocket, _worldTipSocket);
            _near = _nearRoot != null ? BindBody(_nearRoot, false) : null;
            _nearBrush = BindBrush(_nearBrushRoot, _nearGripSocket, _nearTipSocket);
            if (!_world.Valid || _worldBrush == null)
            {
                BindingError = !_world.Valid ? "Right upper arm, forearm and hand could not be bound."
                    : "World brush requires distinct descendant grip/tip sockets and positive unit world scale.";
                return false;
            }

            var restores = new List<PoseRestore>(40);
            AddRestore(restores, _world.Chest); AddRestore(restores, _world.Shoulder);
            AddRestore(restores, _world.Upper); AddRestore(restores, _world.Forearm); AddRestore(restores, _world.Hand);
            AddRestore(restores, _world.LeftUpper); AddRestore(restores, _world.LeftForearm); AddRestore(restores, _world.LeftHand);
            var bindings = new List<FingerBinding>(30);
            int missing = 0;
            foreach (var pose in _profile.Fingers ?? Array.Empty<WorldMacroPlayerGestureProfile.FingerPose>())
            {
                if (pose == null || string.IsNullOrEmpty(pose.BoneName)) { missing++; continue; }
                var world = Find(_world.Root, pose.BoneName);
                if (world == null) { missing++; continue; }
                bindings.Add(new FingerBinding
                {
                    World = world, Near = Find(_nearRoot, pose.BoneName), Pose = pose,
                    Rest = pose.UseExplicitRestPose
                        ? WorldMacroPlayerGestureProfile.SafeRotation(pose.RestLocalRotation) : world.localRotation
                });
                AddRestore(restores, world);
            }
            _fingers = bindings.ToArray();
            _restores = restores.ToArray();
            BindNearCopies();
            if (_effectTip == null)
            {
                var tip = new GameObject("C02_GestureEffectTip");
                tip.transform.SetParent(transform, false);
                _effectTip = tip.transform;
            }
            _effectTip.SetPositionAndRotation(_worldBrush.Tip.position, _worldBrush.Tip.rotation);
            _bound = true;
            _wasSuspended = false;
            _diagnostics = new GestureDiagnostics
            {
                Bound = true, NearBound = NearIsBound, CalibratedFingers = _fingers.Length,
                MissingFingers = missing, NearTipErrorPixels = -1f, State = GestureState.Carry
            };
            ApplyHandleStretch(_worldBrush);
            ApplyHandleStretch(_nearBrush);
            if (Application.isPlaying && isActiveAndEnabled) BindHarvestSink();
            SetSkinningBound(isActiveAndEnabled);
            SetNearVisible(false);
            return true;
        }

        private bool NearIsBound => _near != null && _near.Valid && _nearBrush != null;

        private Body BindBody(Transform root, bool human)
        {
            return new Body
            {
                Root = root,
                Upper = Bone(root, human, HumanBodyBones.RightUpperArm, _profile.RightUpperArmName),
                Forearm = Bone(root, human, HumanBodyBones.RightLowerArm, _profile.RightForearmName),
                Hand = Bone(root, human, HumanBodyBones.RightHand, _profile.RightHandName),
                Shoulder = Bone(root, human, HumanBodyBones.RightShoulder, _profile.RightShoulderName),
                Chest = Bone(root, human, HumanBodyBones.Chest, _profile.ChestName),
                LeftUpper = Bone(root, human, HumanBodyBones.LeftUpperArm, _profile.LeftUpperArmName),
                LeftForearm = Bone(root, human, HumanBodyBones.LeftLowerArm, _profile.LeftForearmName),
                LeftHand = Bone(root, human, HumanBodyBones.LeftHand, _profile.LeftHandName)
            };
        }

        private Transform Bone(Transform root, bool human, HumanBodyBones id, string fallback)
        {
            Transform bone = human && _animator != null && _animator.isHuman ? _animator.GetBoneTransform(id) : null;
            return bone != null ? bone : Find(root, fallback);
        }

        private static Brush BindBrush(Transform root, Transform grip, Transform tip)
        {
            if (root == null || grip == null || tip == null || grip == tip) return null;
            if ((grip != root && !grip.IsChildOf(root)) || (tip != root && !tip.IsChildOf(root))) return null;
            if ((root.lossyScale - Vector3.one).sqrMagnitude > .000001f) return null;
            // The first bristle bone stays fixed inside the ferrule. Root and GripSocket can
            // coincide, so they are not a valid rigid-shaft span for deformation diagnostics.
            Transform shaftEnd = Find(root, "ShaftEndSocket") ?? Find(root, "Bristle_01");
            return new Brush
            {
                Root = root, Grip = grip, Tip = tip,
                RootToGripRotation = Quaternion.Inverse(root.rotation) * grip.rotation,
                RestTipOffset = Quaternion.Inverse(grip.rotation) * (tip.position - grip.position),
                ShaftEnd = shaftEnd,
                ShaftSpan = shaftEnd != null ? HierarchyDistance(grip, shaftEnd) : 0f,
                Renderers = root.GetComponentsInChildren<Renderer>(true),
                Bristles = root.GetComponent<BrushBristleRig>()
            };
        }

        private static void AddRestore(List<PoseRestore> entries, Transform bone)
        {
            if (bone == null) return;
            for (int i = 0; i < entries.Count; i++) if (entries[i].Bone == bone) return;
            entries.Add(new PoseRestore { Bone = bone });
        }

        private void BindNearCopies()
        {
            var copies = new List<PoseCopy>(64);
            if (_nearRoot != null)
                foreach (var target in _nearRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (target == _nearRoot || (_nearBrushRoot != null && target.IsChildOf(_nearBrushRoot))) continue;
                    Transform source = Find(_world.Root, target.name);
                    if (source != null) copies.Add(new PoseCopy { Source = source, Target = target });
                }
            _nearCopies = copies.ToArray();
        }

        private void Subscribe()
        {
            if (_drawing == null || _subscribedDrawing == _drawing) return;
            Unsubscribe();
            _subscribedDrawing = _drawing;
            _drawing.Committed += OnCommitted;
            _drawing.LetterInterrupted += OnInterrupted;
            _drawing.ModeExited += OnModeExited;
            _drawing.StrokeStarted += OnStrokeStarted;
        }

        private void Unsubscribe()
        {
            if (_subscribedDrawing == null) return;
            _subscribedDrawing.Committed -= OnCommitted;
            _subscribedDrawing.LetterInterrupted -= OnInterrupted;
            _subscribedDrawing.ModeExited -= OnModeExited;
            _subscribedDrawing.StrokeStarted -= OnStrokeStarted;
            _subscribedDrawing = null;
        }

        private void OnCommitted(bool success)
        {
            _recoveryState = success ? GestureState.Commit : GestureState.Interrupted;
            _recoveryRemaining = _profile != null
                ? success ? _profile.CommitRecoverySeconds : _profile.InterruptRecoverySeconds : .15f;
            _hasPointerHistory = false;
            if (success && _profile != null && _profile.CastFollowThrough && _profile.CastHoldSeconds > 0f
                && _nearVisible && _castCamera != null && NearIsBound) BeginCast();
            else EndCast();
        }

        private void BeginCast()
        {
            _castActive = true; _castTime = 0f;
            _castFrom = _lastNearViewport;
            Vector2 via = _profile.CastFlickViewport, direction = via - _castFrom;
            // a stroke that ended on the launch point still throws: down and to the right, away from the glyph
            if (direction.sqrMagnitude < .0025f) direction = new Vector2(.4f, -.6f);
            _castTo = via + direction.normalized * _profile.CastFlickOvershoot;
            _cameraRig?.HoldDrawCloseup(_profile.CastHoldSeconds + .1f);
        }

        private void EndCast()
        {
            if (!_castActive) return;
            _castActive = false;
            _cameraRig?.HoldDrawCloseup(0f);
        }

        private void OnInterrupted()
        {
            _recoveryState = GestureState.Interrupted;
            _recoveryRemaining = _profile != null ? _profile.InterruptRecoverySeconds : .12f;
            _hasPointerHistory = false;
        }

        private void OnModeExited() { _hasPointerHistory = false; _wasStroking = false; }
        private void OnStrokeStarted() { _recoveryRemaining = 0f; _hasPointerHistory = false; _strokeMotion.BeginStroke(); }

        private void Update()
        {
            // Restore our last presentation edits before the Animator samples the next gait.
            // While paused, keep the evaluated pose; restoring without an Animator evaluation would snap it open.
            if (Time.timeScale > 0f || (_walker != null && _walker.Seated)) RestoreAnimatedPose();
        }

        private void LateUpdate()
        {
            if (!_bound) return;
            bool seated = _walker != null && _walker.Seated;
            bool paused = Time.timeScale <= 0f;
            _diagnostics.Seated = seated; _diagnostics.Paused = paused;
            if (seated || (_motor != null && !_motor.enabled) || (_controller != null && !_controller.enabled))
            {
                RestoreAnimatedPose();
                EndCast();
                SetNearVisible(false);
                _cameraRig?.SetDrawingPresentationActive(false);
                if (!_wasSuspended)
                {
                    ResetTransitions();
                    SetSkinningBound(false);
                    _wasSuspended = true;
                }
                _diagnostics.State = GestureState.Suspended;
                return;
            }
            if (_wasSuspended)
            {
                _wasSuspended = false;
                SetSkinningBound(true);
            }
            if (paused)
            {
                // UI cancellation has already happened in the input owner. Retain the sampled
                // skeletal pose, but do not leave a cancelled close-up arm over the menu.
                if (_drawing == null || !_drawing.InDrawMode)
                {
                    EndCast();
                    SetNearVisible(false);
                    _cameraRig?.SetDrawingPresentationActive(false);
                    _hasPointerHistory = false;
                }
                return;
            }

            // real time (the draw slow-down does not slow the hand), frame-exact during fixed-rate captures
            float step = Time.captureDeltaTime > 0f ? Time.captureDeltaTime : Time.unscaledDeltaTime;
            EvaluateGesturePose(Mathf.Min(.1f, Mathf.Max(0f, step)));
        }

        private void EvaluateGesturePose(float dt)
        {
            bool drawing = _drawing != null && _drawing.InDrawMode;
            if (drawing) EndCast();   // a new draw mode supersedes the follow-through
            bool casting = _castActive;
            bool stroking = drawing && _drawing.IsStroking;
            bool harvesting = !drawing && _harvest != null && _harvest.IsExtracting;
            _drawWeight = Follow(_drawWeight, drawing || casting ? 1f : 0f,
                drawing || casting ? _profile.EnterResponse : _profile.ExitResponse, dt);
            _harvestWeight = Follow(_harvestWeight, harvesting ? 1f : 0f, _profile.HarvestResponse, dt);
            _recoveryRemaining = Mathf.Max(0f, _recoveryRemaining - dt);

            Camera camera = null;
            Vector2 screen = default;
            Vector3 ink = default;
            bool hasPointer = drawing && _brushFeed != null && _brushFeed.TryGetVisualPointer(out camera, out screen, out ink);
            float castDrop = 0f;
            if (casting)
            {
                _castTime += dt;
                float u = _castTime / Mathf.Max(.01f, _profile.CastHoldSeconds);
                if (u >= 1f || _castCamera == null) { EndCast(); casting = false; }
                else
                {
                    float share = _profile.CastFlickShare;
                    float flick = Mathf.Clamp01(u / share);
                    flick = 1f - (1f - flick) * (1f - flick) * (1f - flick);   // fast out: the throw
                    Vector2 point = Vector2.Lerp(_castFrom, _castTo, flick);
                    castDrop = u <= share ? 0f : Mathf.SmoothStep(0f, 1f, (u - share) / (1f - share));
                    camera = _castCamera;
                    Rect rect = camera.pixelRect;
                    screen = new Vector2(rect.x + point.x * rect.width, rect.y + point.y * rect.height);
                    hasPointer = true;
                    _castReach = Mathf.Max(_castReach, flick);
                }
            }
            if (!casting) _castReach = Follow(_castReach, 0f, _profile.ExitResponse, dt);
            Vector2 viewport = hasPointer ? new Vector2(
                (screen.x - camera.pixelRect.x) / Mathf.Max(1f, camera.pixelRect.width),
                (screen.y - camera.pixelRect.y) / Mathf.Max(1f, camera.pixelRect.height)) : new Vector2(.5f, .5f);
            Vector2 centered = (viewport - Vector2.one * .5f) * 2f;
            if (drawing && hasPointer) { _lastNearViewport = viewport; _castCamera = camera; }
            if (_profile.ArticulatedStrokes)
            {
                _strokeMotion.Step(viewport, drawing && hasPointer, stroking, dt, _profile.WristStrokeSpan, _profile.ArmStrokeSpan);
                if (_strokeMotion.StartedStroke)
                {
                    _strokeAnchorReady = _nearHasSolution;
                    if (_nearHasSolution)
                    {
                        _nearStrokeWrist = camera.transform.InverseTransformPoint(_near.Hand.position);
                        _nearStrokeGrip = camera.transform.InverseTransformPoint(_nearBrush.Grip.position);
                    }
                    _worldStrokeGrip = _world.Root.InverseTransformPoint(_worldBrush.Grip.position);
                }
                _diagnostics.StrokeArmShare = _strokeMotion.ArmShare;
                _diagnostics.StrokeSweep = _strokeMotion.Sweep;
            }
            _bodyPoint = Vector2.Lerp(_bodyPoint, centered, 1f - Mathf.Exp(-_profile.BodyResponse * dt));
            if (stroking && !_wasStroking) _strokeMin = _strokeMax = viewport;
            if (stroking) { _strokeMin = Vector2.Min(_strokeMin, viewport); _strokeMax = Vector2.Max(_strokeMax, viewport); }
            float span = (_strokeMax - _strokeMin).magnitude;
            float bodyParticipation = Mathf.Lerp(_profile.SmallStrokeBodyWeight, 1f,
                Mathf.Clamp01(span / Mathf.Max(.01f, _profile.LargeStrokeViewportSpan))) * _drawWeight;
            Vector3 visualVelocity = hasPointer && (stroking || casting) && _hasPointerHistory && dt > .00001f
                ? new Vector3((viewport.x - _lastPointer.x) * 2f / dt, (viewport.y - _lastPointer.y) * 2f / dt, 0f)
                : Vector3.zero;
            _lastPointer = viewport; _hasPointerHistory = hasPointer; _wasStroking = stroking;
            _frameDt = dt;
            _frameSweep = _profile.ArticulatedStrokes ? _strokeMotion.Sweep
                : Vector2.ClampMagnitude(new Vector2(visualVelocity.x, visualVelocity.y) * .5f, 1f);
            if (_drawWeight <= .001f) _vWorld.Valid = _vWorld.PoleValid = false;

            SaveAnimatedPose();
            MeasureLengths(_world);
            if (_pendantActive) { ApplyPendantPose(); return; }
            ApplyTorso(bodyParticipation);
            PoseCarry();
            bool tipContact = stroking && _recoveryRemaining <= 0f;
            Vector3 worldTipTarget = DrawingWorldTarget(_bodyPoint, !tipContact);
            if (_castReach > .001f)
                worldTipTarget += (_controller != null ? _controller.transform : _world.Root).forward * (_profile.CastWorldReach * _castReach);
            // Each optional bristle chain is evaluated once, including pen-up recovery.
            if (_profile.ShuanggouGrip) EvaluateVerticalWorldBristles(worldTipTarget, visualVelocity, tipContact, dt);
            else EvaluateBristles(_worldBrush, visualVelocity, _world.Root,
                worldTipTarget - _world.Upper.position, tipContact, dt);
            if (_drawWeight > .001f)
            {
                _diagnostics.WorldWristTarget = PoseTowardTip(_world, _worldBrush, worldTipTarget,
                    _world.Root, _drawWeight, _profile.DrawingBrushRoll);
            }
            if (_harvestWeight > .001f) PoseHarvest();
            ApplyFingers();
            PlaceBrush(_world, _worldBrush);
            if (_worldBrush.Bristles != null) _worldBrush.Bristles.ApplyPose();

            bool nearVisible = (drawing || casting) && hasPointer && NearIsBound;
            SetNearVisible(nearVisible);
            _cameraRig?.SetDrawingPresentationActive(nearVisible);
            _diagnostics.NearTipErrorPixels = -1f;
            _diagnostics.NearShoulderCorrectionMeters = 0f;
            _diagnostics.TipReachable = !nearVisible;
            if (nearVisible)
            {
                ApplyNearArm(camera, screen, visualVelocity, tipContact, dt);
                // entering draw mode: the close-up arm and the brush in its hand rise together from below the frame
                _nearRaise = Mathf.MoveTowards(_nearRaise, 1f, dt / Mathf.Max(.05f, _profile.NearRaiseSeconds));
                // leaving after a cast: the same path back down, arm and brush together
                float lower = Mathf.Max(1f - Mathf.SmoothStep(0f, 1f, _nearRaise), castDrop);
                if (lower > .0001f)
                {
                    Vector3 offset = camera.transform.TransformDirection(_profile.NearRaiseOffset) * lower;
                    _nearRoot.position += offset;
                    if (_nearBrush.Root != _nearRoot && !_nearBrush.Root.IsChildOf(_nearRoot)) _nearBrush.Root.position += offset;
                }
            }
            else _nearRaise = 0f;

            Brush activeBrush = nearVisible ? _nearBrush : _worldBrush;
            _effectTip.SetPositionAndRotation(activeBrush.Tip.position, activeBrush.Tip.rotation);
            _diagnostics.EffectTipWorld = _effectTip.position;
            _diagnostics.VerticalBlend = VerticalBlend;
            if (!nearVisible) _diagnostics.VerticalActive = _diagnostics.VerticalBlend > .99f;
            _diagnostics.GripErrorMeters = Vector3.Distance(_world.Hand.TransformPoint(GripPositionLocal(VerticalBlend)), _worldBrush.Grip.position);
            _diagnostics.UpperArmLengthErrorMeters = Mathf.Abs(HierarchyDistance(_world.Upper, _world.Forearm) - _world.UpperLength);
            _diagnostics.ForearmLengthErrorMeters = Mathf.Abs(HierarchyDistance(_world.Forearm, _world.Hand) - _world.ForearmLength);
            _diagnostics.UpperArmWorldCoordinateDeltaMeters = Mathf.Abs(Vector3.Distance(_world.Upper.position, _world.Forearm.position) - _world.UpperWorldLength);
            _diagnostics.ForearmWorldCoordinateDeltaMeters = Mathf.Abs(Vector3.Distance(_world.Forearm.position, _world.Hand.position) - _world.ForearmWorldLength);
            _diagnostics.ShaftMeasurementAvailable = _worldBrush.ShaftEnd != null && _worldBrush.ShaftSpan > .0001f;
            _diagnostics.ShaftSpanMeters = _diagnostics.ShaftMeasurementAvailable
                ? HierarchyDistance(_worldBrush.Grip, _worldBrush.ShaftEnd) : 0f;
            _diagnostics.ShaftLengthErrorMeters = _diagnostics.ShaftMeasurementAvailable
                ? Mathf.Abs(_diagnostics.ShaftSpanMeters - _worldBrush.ShaftSpan) : -1f;
            _diagnostics.BrushWorldScaleError = (_worldBrush.Root.lossyScale - Vector3.one).magnitude;
            _diagnostics.DrawingWeight = _drawWeight; _diagnostics.HarvestWeight = _harvestWeight;
            _diagnostics.WorldElbowHeight = Vector3.Dot(_world.Forearm.position-_world.Upper.position,_world.Root.up);
            _diagnostics.WorldElbowFlexion = Vector3.Angle(_world.Forearm.position-_world.Upper.position,_world.Hand.position-_world.Forearm.position);
            _diagnostics.State = _recoveryRemaining > 0f ? _recoveryState
                : drawing ? stroking ? GestureState.Drawing : GestureState.PenLift
                : harvesting || _harvestWeight > .05f ? GestureState.Harvest : GestureState.Carry;
        }

        private void SaveAnimatedPose()
        {
            for (int i = 0; i < _restores.Length; i++)
            {
                if (_restores[i].Bone == null) continue;
                _restores[i].Position = _restores[i].Bone.localPosition;
                _restores[i].Rotation = _restores[i].Bone.localRotation;
            }
            _hasAppliedPose = true;
        }

        private void RestoreAnimatedPose()
        {
            if (!_hasAppliedPose) return;
            for (int i = 0; i < _restores.Length; i++)
            {
                if (_restores[i].Bone == null) continue;
                _restores[i].Bone.localPosition = _restores[i].Position;
                _restores[i].Bone.localRotation = _restores[i].Rotation;
            }
            _hasAppliedPose = false;
        }

        private static void MeasureLengths(Body body)
        {
            body.UpperLength = HierarchyDistance(body.Upper, body.Forearm);
            body.ForearmLength = HierarchyDistance(body.Forearm, body.Hand);
            body.UpperWorldLength = Vector3.Distance(body.Upper.position, body.Forearm.position);
            body.ForearmWorldLength = Vector3.Distance(body.Forearm.position, body.Hand.position);
        }

        // Measure the actual current hierarchy, including its scales, before adding the world
        // origin. At x=3400 m one float position step is 0.244 mm; subtracting two rounded world
        // positions cannot verify a 0.2 mm segment invariant. No authored length is substituted.
        public static float HierarchyDistance(Transform a, Transform b)
        {
            if (a == null || b == null) return float.NaN;
            Transform ancestor = a;
            while (ancestor != null && b != ancestor && !b.IsChildOf(ancestor)) ancestor = ancestor.parent;
            if (ancestor == null) return Vector3.Distance(a.position, b.position);
            Vector3 displacement = PointInAncestor(b, ancestor) - PointInAncestor(a, ancestor);
            return ancestor.TransformVector(displacement).magnitude;
        }

        private static Vector3 PointInAncestor(Transform point, Transform ancestor)
        {
            Vector3 result = Vector3.zero;
            for (Transform current = point; current != ancestor; current = current.parent)
                result = current.localPosition + current.localRotation * Vector3.Scale(current.localScale, result);
            return result;
        }

        private void PoseCarry()
        {
            var basis = _world.Root;
            float sitting = _profile.UseSeatedCarry && _motor != null ? Mathf.Clamp01(_motor.Posture01) : 0f;
            if (_profile.PreserveAnimatedCarry && sitting < .001f)
            {
                // Keep the source upper arm and forearm motion. A briefcase/sword source
                // needs a bounded wrist-only adjustment to carry the longer brush clear of the torso.
                Quaternion carryGrip = GripRotation(_worldBrush.TipOffset, basis.TransformDirection(_profile.CarryBrushDirection), basis.up, _profile.CarryBrushRoll);
                Quaternion desiredHand = carryGrip * Quaternion.Inverse(WorldMacroPlayerGestureProfile.SafeRotation(_profile.RightHandGripRotation));
                Quaternion limited = Quaternion.RotateTowards(_world.Hand.rotation, desiredHand, 45f);
                _world.Hand.rotation = Quaternion.Slerp(_world.Hand.rotation, limited, (1f-_drawWeight)*(1f-_harvestWeight));
                return;
            }
            float swing = Mathf.Clamp(Vector3.Dot(_world.Hand.position - _world.Upper.position, basis.forward)
                * _profile.CarryGaitInfluence, -_profile.CarryMaximumSwing, _profile.CarryMaximumSwing);
            Vector3 wristOffset = Vector3.Lerp(_profile.CarryWristOffset, _profile.SeatedWristOffset, sitting);
            Vector3 brushDirection = Vector3.Slerp(_profile.CarryBrushDirection, _profile.SeatedBrushDirection, sitting);
            Vector3 elbow = Vector3.Lerp(_profile.CarryElbowPole, _profile.SeatedElbowPole, sitting);
            Vector3 wrist = _world.Upper.position + basis.TransformDirection(wristOffset) + basis.forward * (swing * (1f - sitting));
            Quaternion grip = GripRotation(_worldBrush.TipOffset, basis.TransformDirection(brushDirection), basis.up, _profile.CarryBrushRoll);
            Quaternion hand = grip * Quaternion.Inverse(WorldMacroPlayerGestureProfile.SafeRotation(_profile.RightHandGripRotation));
            PlayerVisualIK.Solve(_world.Upper, _world.Forearm, _world.Hand, wrist,
                _world.Upper.position + basis.TransformDirection(elbow), hand,
                _profile.CarryArmWeight * (1f - _drawWeight) * (1f - _harvestWeight), _profile.MaximumArmExtension);
        }

        private void ApplyTorso(float participation)
        {
            if (_world.Chest != null)
                _world.Chest.rotation = Quaternion.AngleAxis(_bodyPoint.x * _profile.ChestYaw * participation, _world.Root.up)
                    * Quaternion.AngleAxis(-_bodyPoint.y * _profile.ChestLean * participation, _world.Root.right) * _world.Chest.rotation;
            if (_world.Shoulder != null)
                _world.Shoulder.rotation = Quaternion.AngleAxis(-_bodyPoint.y * _profile.ShoulderLift * participation, _world.Root.forward) * _world.Shoulder.rotation;
            if (_profile.ShuanggouGrip && _world.Shoulder != null && participation > 0f)
            {
                // Vertical grip: the clavicle protracts across the body and lifts for high/outer strokes.
                float yaw = _bodyPoint.x * _profile.ShoulderYaw * participation;
                float raise = (Mathf.Max(0f, _bodyPoint.y) + .5f * Mathf.Max(0f, _bodyPoint.x))
                    * _profile.ShoulderRaise * _profile.ShoulderFollow * participation;
                _world.Shoulder.rotation = Quaternion.AngleAxis(yaw, _world.Root.up)
                    * Quaternion.AngleAxis(raise, _world.Root.forward) * _world.Shoulder.rotation;
            }
            if (_world.LeftUpper == null || _world.LeftForearm == null || _world.LeftHand == null) return;
            Vector3 target = _world.LeftHand.position
                - _world.Root.right * (_bodyPoint.x * _profile.OffHandCounter * participation)
                + _world.Root.up * (_bodyPoint.y * _profile.OffHandCounter * participation * .3f);
            PlayerVisualIK.Solve(_world.LeftUpper, _world.LeftForearm, _world.LeftHand, target,
                _world.LeftUpper.position - _world.Root.right * .22f - _world.Root.up * .25f,
                _world.LeftHand.rotation, participation, _profile.MaximumArmExtension);
        }

        private Vector3 DrawingWorldTarget(Vector2 point, bool penLift)
        {
            Transform basis = _controller != null ? _controller.transform : _world.Root;
            float distance = _profile.ShuanggouGrip
                ? Mathf.Lerp(_profile.WorldDrawingDistance, _profile.ShuanggouDrawingDistance, VerticalBlend)
                : _profile.WorldDrawingDistance;
            return basis.position + basis.up * (_profile.WorldDrawingHeight - (_motor!=null?_motor.Crouch01*.45f:0) + point.y * _profile.WorldDrawingHalfSize.y)
                + basis.right * (point.x * _profile.WorldDrawingHalfSize.x)
                + basis.forward * (distance - (penLift ? _profile.PenLiftDistance : 0f));
        }

        private void PoseHarvest()
        {
            Transform basis = _world.Root;
            Vector3 wrist = _world.Upper.position + basis.TransformDirection(_profile.HarvestWristOffset);
            Vector3 target = _harvest != null ? _harvest.ExtractSourcePosition : wrist + basis.forward;
            Quaternion grip = _profile.OverhandGrip ? DrawingGripRotation(_worldBrush.TipOffset,target-wrist,basis.up) : GripRotation(_worldBrush.TipOffset, target - wrist, basis.up, _profile.HarvestBrushRoll);
            Quaternion hand = grip * Quaternion.Inverse(WorldMacroPlayerGestureProfile.SafeRotation(_profile.RightHandGripRotation));
            PlayerVisualIK.Solve(_world.Upper, _world.Forearm, _world.Hand, wrist,
                ArmPole(_world,wrist,hand,basis),
                hand, _harvestWeight, _profile.MaximumArmExtension);
            AlignForearmToHand(_world,_harvestWeight);
        }

        private Vector3 PoseTowardTip(Body body, Brush brush, Vector3 tip, Transform basis, float weight, float roll)
        {
            if (_profile.ShuanggouGrip) return PoseTowardTipVertical(body, brush, tip, basis, weight);
            Vector3 aim = tip-body.Upper.position;
            if (_profile.ArticulatedStrokes && _strokeMotion.Contact && _strokeAnchorReady)
                aim = Vector3.Slerp(aim.normalized, (tip-basis.TransformPoint(_worldStrokeGrip)).normalized, 1-_strokeMotion.ArmShare);
            Quaternion grip = _profile.OverhandGrip ? DrawingGripRotation(brush.TipOffset,aim,basis.up) : GripRotation(brush.TipOffset, aim, basis.up, roll);
            Quaternion hand = grip * Quaternion.Inverse(WorldMacroPlayerGestureProfile.SafeRotation(_profile.RightHandGripRotation));
            Vector3 wrist = tip - grip * brush.TipOffset - hand * Vector3.Scale(_profile.RightHandGripPosition, body.Hand.lossyScale);
            Vector3 pole = ArmPole(body,wrist,hand,basis);
            PlayerVisualIK.Solve(body.Upper, body.Forearm, body.Hand, wrist, pole, hand, weight, _profile.MaximumArmExtension);
            AlignForearmToHand(body,weight);
            return wrist;
        }

        private void ApplyNearArm(Camera camera, Vector2 screen, Vector3 visualVelocity, bool stroking, float dt)
        {
            if (_profile.ShuanggouGrip) { ApplyNearArmVertical(camera, screen, visualVelocity, stroking, dt); return; }
            foreach (var pair in _nearCopies)
            {
                pair.Target.localPosition = pair.Source.localPosition;
                pair.Target.localRotation = pair.Source.localRotation;
            }
            _nearRoot.rotation = camera.transform.rotation;
            _nearRoot.position += camera.transform.TransformPoint(_profile.NearShoulderOffset) - _near.Upper.position;
            MeasureLengths(_near);
            Ray ray = camera.ScreenPointToRay(screen);
            float preferred = Mathf.Max(camera.nearClipPlane + .08f, _profile.NearTipDepth);
            float minimum = Mathf.Max(camera.nearClipPlane + .04f, _profile.NearMinimumDepth);
            float maximum = Mathf.Max(minimum, _profile.NearMaximumDepth);
            PlayerVisualIK.FindTip(ray, _near.Upper.position, _near.UpperLength + _near.ForearmLength,
                _nearBrush.TipOffset.magnitude, minimum, maximum, preferred, out Vector3 tip);
            if (!stroking) tip -= ray.direction * Mathf.Min(_profile.PenLiftDistance, Mathf.Max(0f, preferred - minimum));
            Vector3 aim = NearAimDirection(camera, screen, tip, tip - _near.Upper.position,
                _nearBrush.TipOffset.magnitude, out float edgeAdjustment);
            EvaluateBristles(_nearBrush, visualVelocity, camera.transform, aim, stroking, dt);
            if (_profile.AnatomicalNearReach)
            {
                // Search depth and brush orientation using the already deformed bristle endpoint.
                // The arm's anchor and all bone lengths remain unchanged during this search.
                float best=float.PositiveInfinity;Vector3 bestTip=tip,bestAim=aim;
                Vector2 normalizedScreen=new Vector2(screen.x/Mathf.Max(1,camera.pixelWidth),screen.y/Mathf.Max(1,camera.pixelHeight));
                bool continuous=_profile.StableDrawingElbow&&_nearHasSolution&&Vector2.Distance(normalizedScreen,_nearLastScreen)<.20f;
                Vector3 previousAim=camera.transform.TransformDirection(_nearLastAim);
                Vector3 shoulder=_near.Upper.position;
                void Consider(Vector3 candidate,Vector3 direction)
                {
                    Quaternion g=DrawingGripRotation(_nearBrush.TipOffset,direction,camera.transform.up);
                    Quaternion h=g*Quaternion.Inverse(WorldMacroPlayerGestureProfile.SafeRotation(_profile.RightHandGripRotation));
                    Vector3 w=candidate-g*_nearBrush.TipOffset-h*Vector3.Scale(_profile.RightHandGripPosition,_near.Hand.lossyScale);
                    Vector3 reachable=PlayerVisualIK.ClampReach(shoulder,w,_near.UpperLength,_near.ForearmLength,_profile.MaximumArmExtension);
                    float error=Vector3.Distance(w,reachable);
                    Vector3 view=camera.WorldToViewportPoint(w);
                    float clipped=Mathf.Max(0,(_profile.OverhandGrip?.40f:.26f)-view.z)*30+Mathf.Max(0,.14f-view.y)+Mathf.Max(0,view.y-.88f)+Mathf.Max(0,.08f-view.x)+Mathf.Max(0,view.x-.94f);
                    Vector3 testPole=ArmPole(_near,w,h,camera.transform);
                    Vector3 elbow=PlayerVisualIK.ElbowPosition(shoulder,w,testPole,_near.UpperLength,_near.ForearmLength,_profile.MaximumArmExtension);
                    float bend=Vector3.Angle(w-elbow,h*_profile.HandForwardLocal);
                    float elbowRise=Vector3.Dot(elbow-shoulder,camera.transform.up);
                    float flexion=Vector3.Angle(elbow-shoulder,w-elbow);
                    float elbowCost=_profile.StableDrawingElbow ? Mathf.Max(0,elbowRise+.025f)*180f+Mathf.Max(0,flexion-135f)*.12f : 0f;
                    float anatomical=_profile.OverhandGrip ? Mathf.Max(0,bend-40)*5f+bend*.002f : 0;
                    float composition=_profile.OverhandGrip ? Vector2.Distance(new Vector2(view.x,view.y),new Vector2(.62f,.24f))*6f : 0;
                    float armShare=_profile.ArticulatedStrokes?_strokeMotion.ArmShare:0;
                    float continuity=continuous ? Vector3.Distance(camera.transform.InverseTransformDirection(elbow-shoulder),_nearLastElbow)*Mathf.Lerp(60f,18f,armShare)+Vector3.Angle(direction,previousAim)*.12f+Mathf.Abs(Vector3.Distance(ray.origin,candidate)-_nearLastDepth)*12f : 0f;
                    float pivot=_profile.ArticulatedStrokes&&stroking&&_strokeAnchorReady
                        ? Vector3.Distance(w,camera.transform.TransformPoint(_nearStrokeWrist))*_profile.WristAnchorWeight*(1-armShare) : 0;
                    float score=error*1000+clipped*20+anatomical+elbowCost+composition+continuity+pivot+Mathf.Abs(Vector3.Distance(ray.origin,candidate)-preferred)*.15f+Vector3.Angle(direction,(candidate-shoulder).normalized)*.001f;
                    if(score<best){best=score;bestTip=candidate;bestAim=direction;}
                }
                for(int d=0;d<=16;d++)
                {
                    Vector3 candidate=ray.GetPoint(Mathf.Lerp(minimum,maximum,d/16f));
                    Vector3 radial=(candidate-shoulder).normalized;
                    for(int a=0;a<=(_profile.OverhandGrip?63:5);a++)
                    {
                        Vector3 direction=_profile.OverhandGrip && a>0
                            ? Quaternion.AngleAxis(((a-1)%9-4)*25f,camera.transform.up)*Quaternion.AngleAxis(((a-1)/9-3)*15f,camera.transform.right)*radial
                            : Vector3.Slerp(aim.normalized,radial,_profile.OverhandGrip?0:a/5f).normalized;
                        Consider(candidate,direction);
                    }
                }
                if(continuous)
                    for(int d=-2;d<=2;d++)
                    {
                        Vector3 candidate=ray.GetPoint(Mathf.Clamp(_nearLastDepth+d*.018f,minimum,maximum));
                        for(int yaw=-1;yaw<=1;yaw++)for(int pitch=-1;pitch<=1;pitch++)
                            Consider(candidate,Quaternion.AngleAxis(yaw*5f,camera.transform.up)*Quaternion.AngleAxis(pitch*5f,camera.transform.right)*previousAim);
                    }
                if (_profile.ArticulatedStrokes && stroking && _strokeAnchorReady)
                {
                    // Exact ray/sphere candidates allow a small stroke to pivot about the grasp.
                    // Depth remains free, while the rendered endpoint remains on the input ray.
                    Vector3 pivot=camera.transform.TransformPoint(_nearStrokeGrip);
                    Vector3 offset=ray.origin-pivot;
                    float b=Vector3.Dot(offset,ray.direction);
                    float discriminant=b*b-offset.sqrMagnitude+_nearBrush.TipOffset.sqrMagnitude;
                    if(discriminant>=0)
                        foreach(float depth in new[]{-b-Mathf.Sqrt(discriminant),-b+Mathf.Sqrt(discriminant)})
                            if(depth>=minimum&&depth<=maximum)
                            {
                                Vector3 candidate=ray.GetPoint(depth);
                                Consider(candidate,(candidate-pivot).normalized);
                            }
                }
                _nearLastAim=camera.transform.InverseTransformDirection(bestAim);
                _nearLastDepth=Vector3.Distance(ray.origin,bestTip);_nearLastScreen=normalizedScreen;
                tip=bestTip;aim=bestAim;edgeAdjustment=0;
            }
            Quaternion grip = DrawingGripRotation(_nearBrush.TipOffset, aim, camera.transform.up);
            Quaternion hand = grip * Quaternion.Inverse(WorldMacroPlayerGestureProfile.SafeRotation(_profile.RightHandGripRotation));
            Vector3 wrist = tip - grip * _nearBrush.TipOffset - hand * Vector3.Scale(_profile.RightHandGripPosition, _near.Hand.lossyScale);
            Vector3 anchorCorrection = Vector3.zero;
            if (edgeAdjustment > 0f)
            {
                // The trimmed upper sleeve must enter from the side above this corrective
                // grip, rather than from the same bottom corner as the bristles. Move only the
                // presentation shoulder, then solve the unchanged two-bone arm back to its wrist.
                Vector3 edgeShoulder = wrist + camera.transform.right * .36f
                    + camera.transform.up * .18f - camera.transform.forward * .25f;
                edgeShoulder = PlayerVisualIK.ClampReach(wrist, edgeShoulder,
                    _near.UpperLength, _near.ForearmLength, _profile.MaximumArmExtension);
                anchorCorrection = Vector3.ClampMagnitude((edgeShoulder - _near.Upper.position) * edgeAdjustment,
                    _profile.MaximumNearShoulderCorrection);
                _nearRoot.position += anchorCorrection;
            }
            Vector3 clamped = PlayerVisualIK.ClampReach(_near.Upper.position, wrist, _near.UpperLength, _near.ForearmLength, _profile.MaximumArmExtension);
            Vector3 reachCorrection = Vector3.ClampMagnitude(wrist - clamped,
                Mathf.Max(0f, _profile.MaximumNearShoulderCorrection - anchorCorrection.magnitude));
            _nearRoot.position += reachCorrection;
            // With an inward edge grip, a downward elbow occupies the same projected diagonal
            // as the shaft. Raise the elbow plane only for that corrective reach so the forearm
            // approaches the hand from above/right and leaves the bristles' diagonal clear.
            float elbowHeight = Mathf.Lerp(-_profile.ElbowDown, .36f, edgeAdjustment);
            Vector3 pole = _profile.OverhandGrip ? ArmPole(_near,wrist,hand,camera.transform) : Vector3.Lerp(_near.Upper.position, wrist, .5f)
                + camera.transform.right * _profile.ElbowOut + camera.transform.up * elbowHeight;
            PlayerVisualIK.Solve(_near.Upper, _near.Forearm, _near.Hand, wrist, pole, hand, 1f, _profile.MaximumArmExtension);
            _nearHasSolution=true;
            if (_profile.ArticulatedStrokes && stroking && !_strokeAnchorReady)
            {
                _nearStrokeWrist=camera.transform.InverseTransformPoint(wrist);
                _nearStrokeGrip=camera.transform.InverseTransformPoint(tip-grip*_nearBrush.TipOffset);
                _worldStrokeGrip=_world.Root.InverseTransformPoint(_worldBrush.Grip.position);
                _strokeAnchorReady=true;
            }
            _nearLastElbow=camera.transform.InverseTransformDirection(_near.Forearm.position-_near.Upper.position);
            _diagnostics.NearElbowHeight=Vector3.Dot(_near.Forearm.position-_near.Upper.position,camera.transform.up);
            _diagnostics.NearElbowFlexion=Vector3.Angle(_near.Forearm.position-_near.Upper.position,_near.Hand.position-_near.Forearm.position);
            _diagnostics.OverhandActive=_profile.OverhandGrip;
            _diagnostics.NearForearmTwistDegrees = AlignForearmToHand(_near, 1f);
            Vector3 forearmAxis=(_near.Hand.position-_near.Forearm.position).normalized;
            Vector3 dorsal=_near.Hand.rotation*_profile.HandDorsalLocal;
            _diagnostics.NearWristBendDegrees=Vector3.Angle(forearmAxis,_near.Hand.rotation*_profile.HandForwardLocal);
            _diagnostics.NearWristTwistDegrees=Vector3.SignedAngle(Vector3.ProjectOnPlane(_near.Forearm.rotation*_profile.HandRestInForearm*_profile.HandDorsalLocal,forearmAxis),Vector3.ProjectOnPlane(dorsal,forearmAxis),forearmAxis);
            _diagnostics.NearDorsalUp=Vector3.Dot(dorsal.normalized,camera.transform.up);
            _diagnostics.NearBrushThumbSideDot=Vector3.Dot((grip*_nearBrush.RestTipOffset).normalized,(_near.Hand.rotation*_profile.HandThumbSideLocal).normalized);
            FinishNearArm(camera, screen, wrist, anchorCorrection, reachCorrection);
        }

        private void FinishNearArm(Camera camera, Vector2 screen, Vector3 wrist, Vector3 anchorCorrection, Vector3 reachCorrection)
        {
            Vector3 correction = anchorCorrection + reachCorrection;
            // Finger local rotations were copied from the world pose above; a second blend would
            // give the near hand a different grasp when FingerPoseWeight is below one.
            PlaceBrush(_near, _nearBrush);
            if (_nearBrush.Bristles != null) _nearBrush.Bristles.ApplyPose();
            Vector3 actualScreen = camera.WorldToScreenPoint(_nearBrush.Tip.position);
            _diagnostics.NearTipErrorBeforeCorrectionPixels = actualScreen.z > camera.nearClipPlane
                ? Vector2.Distance(screen, new Vector2(actualScreen.x, actualScreen.y)) : float.PositiveInfinity;
            // The close-up arm is a presentation-only chain. Reconcile the ACTUAL deformed tip
            // after the bone/brush transforms have incurred large-world float rounding. Translate
            // the complete arm and brush together, never the tip alone, and never scale a segment.
            // This uses the remaining authored shoulder allowance and at most 2 cm of refinement;
            // a reach/configuration error larger than that remains visible in diagnostics.
            Vector3 endpointCorrection = Vector3.zero;
            float budget = Mathf.Min(.02f, Mathf.Max(0f, _profile.MaximumNearShoulderCorrection
                - anchorCorrection.magnitude - reachCorrection.magnitude));
            for (int pass = 0; pass < 3 && actualScreen.z > camera.nearClipPlane; pass++)
            {
                if (Vector2.Distance(screen, new Vector2(actualScreen.x, actualScreen.y)) < .35f || budget <= 0f) break;
                Vector3 residual = Vector3.ClampMagnitude(ProjectionResidual(camera, _nearBrush.Tip.position,
                    actualScreen, screen), budget);
                if (residual.sqrMagnitude < 1e-12f) break;
                _nearRoot.position += residual;
                if (_nearBrush.Root != _nearRoot && !_nearBrush.Root.IsChildOf(_nearRoot))
                    _nearBrush.Root.position += residual;
                endpointCorrection += residual;
                budget -= residual.magnitude;
                actualScreen = camera.WorldToScreenPoint(_nearBrush.Tip.position);
            }
            _diagnostics.NearTipErrorPixels = actualScreen.z > camera.nearClipPlane
                ? Vector2.Distance(screen, new Vector2(actualScreen.x, actualScreen.y)) : float.PositiveInfinity;
            _diagnostics.TipReachable = _diagnostics.NearTipErrorPixels <= 2f;
            _diagnostics.NearWristTarget = wrist + endpointCorrection;
            _diagnostics.NearEndpointCorrectionMeters = endpointCorrection.magnitude;
            _diagnostics.NearShoulderCorrectionMeters = (correction + endpointCorrection).magnitude;
        }

        private static Vector3 NearAimDirection(Camera camera, Vector2 screen, Vector3 tip, Vector3 shoulderAim,
            float gripToTip, out float edgeAdjustment)
        {
            edgeAdjustment = 0f;
            Rect rect = camera.pixelRect;
            Vector2 viewport = new Vector2((screen.x - rect.x) / Mathf.Max(1f, rect.width),
                (screen.y - rect.y) / Mathf.Max(1f, rect.height));
            float edge = Mathf.Max(Mathf.Abs(viewport.x - .5f), Mathf.Abs(viewport.y - .5f)) * 2f;
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.60f, .94f, edge));
            if (blend <= 0f) return shoulderAim;

            Vector3 originalAxis = shoulderAim.normalized;
            // Preserve the original reach whenever the grip or a substantial part of the
            // shaft already projects into the image. In particular the original upper-left
            // reach is good; turning every viewport edge exposes the trimmed shoulder skin.
            Vector3 originalGrip = camera.WorldToScreenPoint(tip - originalAxis * gripToTip);
            Vector3 originalShaft = camera.WorldToScreenPoint(tip - originalAxis * Mathf.Min(.25f, gripToTip * .45f));
            float visibleMargin = Mathf.Max(ViewportMargin(camera, rect, originalGrip), ViewportMargin(camera, rect, originalShaft));
            blend *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .035f, visibleMargin));
            if (blend <= 0f) return shoulderAim;

            // Near the viewport boundary, pointing out from the camera-adjacent shoulder puts
            // the rigid handle even farther off screen than the tip. Rotate the complete grasp
            // towards an interior ray at a shallower depth, keeping the brush pointing forward.
            // The central gesture is unchanged; this is orientation only, never a shorter brush.
            Vector2 inner = new Vector2(Mathf.Clamp(viewport.x, .22f, .78f), Mathf.Clamp(viewport.y, .22f, .78f));
            Vector2 innerScreen = new Vector2(rect.x + inner.x * rect.width, rect.y + inner.y * rect.height);
            Vector3 projected = camera.WorldToScreenPoint(tip);
            Vector3 inward = ProjectionResidual(camera, tip, projected, innerScreen);
            Vector3 relativeTip = tip - camera.transform.position;
            float tipDepth = Vector3.Dot(relativeTip, camera.transform.forward);
            float handleDepth = Mathf.Max(camera.nearClipPlane + .10f, tipDepth - .24f);
            Vector3 interiorHandle = (relativeTip + inward) * (handleDepth / Mathf.Max(.001f, tipDepth));
            Vector3 edgeAim = relativeTip - interiorHandle;
            if (edgeAim.sqrMagnitude < .000001f) return shoulderAim;
            edgeAdjustment = blend;
            return Vector3.Slerp(shoulderAim.normalized, edgeAim.normalized, blend);
        }

        private static float ViewportMargin(Camera camera, Rect rect, Vector3 point)
        {
            if (point.z <= camera.nearClipPlane) return -1f;
            float x = (point.x - rect.x) / Mathf.Max(1f, rect.width);
            float y = (point.y - rect.y) / Mathf.Max(1f, rect.height);
            return Mathf.Min(Mathf.Min(x, 1f - x), Mathf.Min(y, 1f - y));
        }

        private static Vector3 ProjectionResidual(Camera camera, Vector3 tip, Vector3 projected, Vector2 target)
        {
            // ScreenToWorldPoint forms an inverse view-projection with the kilometre-scale
            // translation embedded in it; it is not a sufficiently accurate inverse of the
            // final projection here. Measure a local screen Jacobian instead. Its two samples
            // are only 2 cm apart and the solved correction uses camera-plane directions only.
            // This also respects off-centre projection/pixel rectangles without assuming a FOV.
            const float probe = .02f;
            Vector3 horizontal = camera.WorldToScreenPoint(tip + camera.transform.right * probe);
            Vector3 vertical = camera.WorldToScreenPoint(tip + camera.transform.up * probe);
            Vector2 dx = new Vector2(horizontal.x - projected.x, horizontal.y - projected.y) / probe;
            Vector2 dy = new Vector2(vertical.x - projected.x, vertical.y - projected.y) / probe;
            Vector2 error = target - new Vector2(projected.x, projected.y);
            float determinant = dx.x * dy.y - dy.x * dx.y;
            if (Mathf.Abs(determinant) < .0001f || float.IsNaN(determinant) || float.IsInfinity(determinant))
                return Vector3.zero;
            float right = (error.x * dy.y - dy.x * error.y) / determinant;
            float up = (dx.x * error.y - error.x * dx.y) / determinant;
            return camera.transform.right * right + camera.transform.up * up;
        }

        private void ApplyFingers()
        {
            for (int i = 0; i < _fingers.Length; i++)
            {
                var binding = _fingers[i];
                Transform bone = binding.World;
                if (bone == null) continue;
                Quaternion target = FingerLocalRotation(binding.Rest, binding.Pose, _drawWeight, _harvestWeight);
                bone.localRotation = Quaternion.Slerp(bone.localRotation, target, _profile.FingerPoseWeight);
            }
        }

        public static Quaternion FingerLocalRotation(Quaternion rest,
            WorldMacroPlayerGestureProfile.FingerPose pose, float drawingWeight, float harvestWeight)
        {
            Quaternion basis = WorldMacroPlayerGestureProfile.SafeRotation(rest);
            if (pose == null) return basis;
            Quaternion offset = Quaternion.Slerp(WorldMacroPlayerGestureProfile.SafeRotation(pose.CarryOffset),
                WorldMacroPlayerGestureProfile.SafeRotation(pose.DrawingOffset), drawingWeight);
            offset = Quaternion.Slerp(offset, WorldMacroPlayerGestureProfile.SafeRotation(pose.HarvestOffset), harvestWeight);
            return basis * offset;
        }

        private void PlaceBrush(Body body, Brush brush)
        {
            float blend = body == _near ? NearBlend : VerticalBlend;
            Quaternion grip = body.Hand.rotation * GripRotationLocal(blend);
            brush.Root.rotation = grip * Quaternion.Inverse(brush.RootToGripRotation);
            brush.Root.position += body.Hand.TransformPoint(GripPositionLocal(blend)) - brush.Grip.position;
        }

        private void EvaluateBristles(Brush brush, Vector3 velocity, Transform basis, Vector3 axis, bool stroking, float dt)
        {
            if (brush.Bristles == null || !brush.Bristles.IsBound) return;
            Quaternion grip = DrawingGripRotation(brush.TipOffset, axis, basis.up);
            brush.Bristles.EvaluateInFrame(velocity, basis.rotation, grip, stroking, dt);
        }

        private Vector3 ArmPole(Body body,Vector3 wrist,Quaternion hand,Transform basis)
        {
            if(_profile.StableDrawingElbow)
            {
                // A hand-relative pole rolls the whole bend plane when the brush rotates.
                // Prefer gravity/down and a small outward bias, independent of wrist pronation.
                Vector3 axis=(wrist-body.Upper.position).normalized;
                Vector3 direction=-basis.up+ basis.right*.32f;
                if (_profile.ArticulatedStrokes && _drawWeight > .01f)
                {
                    float share=_strokeMotion.ArmShare*_profile.DirectionalElbowWeight;
                    direction += basis.right * (_strokeMotion.Sweep.x*.55f*share)
                        + basis.forward * (-_strokeMotion.Sweep.y*.65f*share)
                        + basis.up * (Mathf.Abs(_strokeMotion.Sweep.y)*.3f*share);
                }
                Vector3 bend=Vector3.ProjectOnPlane(direction,axis);
                if(bend.sqrMagnitude<.015f)
                    bend=Vector3.ProjectOnPlane(basis.right-basis.forward*.2f,axis);
                return body.Upper.position+bend.normalized*Mathf.Max(.2f,body.UpperLength);
            }
            if(_profile.OverhandGrip)
                return wrist-hand*_profile.HandForwardLocal*body.ForearmLength+basis.right*.04f-basis.up*.04f;
            return Vector3.Lerp(body.Upper.position,wrist,.5f)+basis.right*_profile.ElbowOut-basis.up*_profile.ElbowDown;
        }

        private Quaternion DrawingGripRotation(Vector3 offset, Vector3 aim, Vector3 up)
        {
            Quaternion g=GripRotation(offset,aim,up,_profile.DrawingBrushRoll);
            if(!_profile.OverhandGrip)return g;
            Vector3 axis=aim.normalized;
            Quaternion h=g*Quaternion.Inverse(WorldMacroPlayerGestureProfile.SafeRotation(_profile.RightHandGripRotation));
            Vector3 dorsal=Vector3.ProjectOnPlane(h*_profile.HandDorsalLocal,axis);
            Vector3 target=Vector3.ProjectOnPlane(up,axis);
            if(dorsal.sqrMagnitude<1e-6f||target.sqrMagnitude<1e-6f)return g;
            return Quaternion.AngleAxis(Vector3.SignedAngle(dorsal,target,axis)+(_profile.DrawingBrushRoll-18f),axis)*g;
        }

        private float AlignForearmToHand(Body body,float weight)
        {
            if(!_profile.OverhandGrip&&!_profile.ShuanggouGrip)return 0;
            Quaternion hand=body.Hand.rotation;
            Vector3 axis=(body.Hand.position-body.Forearm.position).normalized;
            Vector3 neutral=Vector3.ProjectOnPlane(body.Forearm.rotation*_profile.HandRestInForearm*_profile.HandDorsalLocal,axis);
            Vector3 desired=Vector3.ProjectOnPlane(hand*_profile.HandDorsalLocal,axis);
            float twist=Vector3.SignedAngle(neutral,desired,axis)*weight;
            // Axial pronation belongs to the forearm, not a 180-degree wrist seam.
            body.Forearm.rotation=Quaternion.AngleAxis(twist,axis)*body.Forearm.rotation;
            body.Hand.rotation=hand;
            return twist;
        }

        public static Quaternion GripRotation(Vector3 tipOffsetInGrip, Vector3 desiredAxis, Vector3 up, float roll)
        {
            Vector3 axis = desiredAxis.sqrMagnitude > .000001f ? desiredAxis.normalized : Vector3.forward;
            Vector3 dorsal = Vector3.ProjectOnPlane(-up, axis);
            if (dorsal.sqrMagnitude < .000001f) dorsal = Vector3.ProjectOnPlane(Vector3.forward, axis);
            if (dorsal.sqrMagnitude < .000001f) dorsal = Vector3.ProjectOnPlane(Vector3.right, axis);
            Vector3 localAxis = tipOffsetInGrip.sqrMagnitude > .000001f ? tipOffsetInGrip.normalized : Vector3.up;
            return Quaternion.AngleAxis(roll, axis) * Quaternion.LookRotation(dorsal.normalized, axis)
                * Quaternion.FromToRotation(localAxis, Vector3.up);
        }

        // ---- Optional vertical double-hook grip (쌍구법) --------------------------------------------------
        // Presentation only. The tip is still placed on the same input ray / world target; only the brush
        // orientation, grasp frame and arm/shoulder follow differ. Drawing input and recognition are untouched.

        private float VerticalBlend => _profile != null && _profile.ShuanggouGrip
            ? Mathf.Clamp01(_drawWeight * (1f - _harvestWeight)) : 0f;

        // The close-up arm exists only while drawing: it holds the full drawing grasp from its first frame and
        // rises into view with the brush already in the hand (no re-grip while it enters).
        private float NearBlend => _profile != null && _profile.ShuanggouGrip ? 1f - Mathf.Clamp01(_harvestWeight) : 0f;

        private Quaternion GripRotationLocal(float blend)
        {
            Quaternion a = WorldMacroPlayerGestureProfile.SafeRotation(_profile.RightHandGripRotation);
            return blend <= 0f ? a : Quaternion.Slerp(a,
                WorldMacroPlayerGestureProfile.SafeRotation(_profile.ShuanggouGripRotation), Mathf.Clamp01(blend));
        }

        private Vector3 GripPositionLocal(float blend) => blend <= 0f ? _profile.RightHandGripPosition
            : Vector3.Lerp(_profile.RightHandGripPosition, _profile.ShuanggouGripPosition, Mathf.Clamp01(blend));

        private static float Lag(float seconds, float dt) => seconds <= .0001f ? 1f : 1f - Mathf.Exp(-Mathf.Max(0f, dt) / seconds);

        private Vector3 VerticalShaftLocal(Brush brush)
        {
            Vector3 rest = brush.RestTipOffset.sqrMagnitude > .000001f ? brush.RestTipOffset.normalized : Vector3.up;
            return WorldMacroPlayerGestureProfile.SafeRotation(_profile.ShuanggouGripRotation) * rest;
        }

        // Drawing-plane normal tilted so the butt leans toward the authored direction, plus a small lean with
        // the smoothed stroke direction. The grasp is therefore never directly between the eye and the tip.
        private Vector3 VerticalAxis(Vector3 normal, Transform frame, Vector2 centered)
        {
            Vector3 n = normal.sqrMagnitude > .000001f ? normal.normalized : frame.forward;
            Vector2 lean = _profile.ShuanggouLeanDirection - Vector2.Scale(_profile.ShuanggouEdgeLean,
                new Vector2(Mathf.Max(0f, centered.x), Mathf.Max(0f, centered.y)));
            Vector3 butt = Vector3.ProjectOnPlane(frame.right * lean.x + frame.up * lean.y, n);
            float tilt = _profile.ShuanggouViewTilt * Mathf.Deg2Rad * Mathf.Clamp01(butt.magnitude / .3f);
            Vector3 axis = butt.sqrMagnitude > .000001f ? n * Mathf.Cos(tilt) - butt.normalized * Mathf.Sin(tilt) : n;
            float amount = Mathf.Clamp01(_frameSweep.magnitude);
            Vector3 sweep = Vector3.ProjectOnPlane(frame.right * _frameSweep.x + frame.up * _frameSweep.y, axis);
            if (amount > 0f && sweep.sqrMagnitude > .000001f)
                axis -= sweep.normalized * Mathf.Tan(_profile.ShuanggouTilt * amount * Mathf.Deg2Rad);
            return axis.normalized;
        }

        private static Vector3 ClampCone(Vector3 direction, Vector3 centre, float degrees)
        {
            Vector3 c = centre.normalized;
            if (direction.sqrMagnitude < .00000001f) return c;
            Vector3 d = direction.normalized;
            float angle = Vector3.Angle(c, d);
            float limit = Mathf.Max(0f, degrees);
            return angle <= limit ? d : Vector3.Slerp(c, d, limit / angle).normalized;
        }

        // The fitted shaft runs along the hand's radial→ulnar axis (bristles below the little finger). The
        // hand is rolled about the shaft so the knuckles point up-left, blended toward the solved forearm.
        private Quaternion VerticalHand(Brush brush, Vector3 axis, Vector3 up, Vector3 forearm)
        {
            if (_profile.ShuanggouFist) return FistHand(brush, axis, up, forearm);
            Vector3 shaft = VerticalShaftLocal(brush);
            // ShuanggouDorsalFacing: the back of the hand (not the finger direction) is aligned to the view-up reference,
            // so the palm and finger pads face the look direction instead of the screen
            Vector3 forward = Vector3.ProjectOnPlane(_profile.ShuanggouDorsalFacing ? _profile.HandDorsalLocal : _profile.HandForwardLocal, shaft);
            if (forward.sqrMagnitude < .000001f) forward = Vector3.ProjectOnPlane(_profile.HandDorsalLocal, shaft);
            if (forward.sqrMagnitude < .000001f) forward = Vector3.ProjectOnPlane(Vector3.forward, shaft);
            Vector3 a = axis.sqrMagnitude > .000001f ? axis.normalized : Vector3.forward;
            Vector3 knuckles = Quaternion.AngleAxis(_profile.ShuanggouKnuckleRoll, a) * Vector3.ProjectOnPlane(up, a);
            if (knuckles.sqrMagnitude < .000001f) knuckles = Vector3.ProjectOnPlane(Vector3.up, a);
            if (knuckles.sqrMagnitude < .000001f) knuckles = Vector3.ProjectOnPlane(Vector3.right, a);
            knuckles.Normalize();
            Vector3 follow = Vector3.ProjectOnPlane(forearm, a);
            if (!_profile.ShuanggouDorsalFacing && _profile.ShuanggouForearmFollow > 0f && follow.sqrMagnitude > .000001f
                && Vector3.Dot(follow.normalized, knuckles) > -.5f)
                knuckles = Vector3.Slerp(knuckles, follow.normalized, _profile.ShuanggouForearmFollow);
            return Quaternion.LookRotation(a, knuckles) * Quaternion.Inverse(Quaternion.LookRotation(shaft, forward));
        }

        // Hand rotation and world grip rotation for the blended grasp (A while carrying → vertical while drawing).
        private Quaternion BlendedHand(Brush brush, Vector3 axis, Vector3 up, Vector3 forearm, float blend, out Quaternion grip)
        {
            Quaternion vertical = VerticalHand(brush, axis, up, forearm);
            if (blend >= .9999f) { grip = vertical * GripRotationLocal(1f); return vertical; }
            Quaternion a = _profile.OverhandGrip ? DrawingGripRotation(brush.TipOffset, axis, up)
                : GripRotation(brush.TipOffset, axis, up, _profile.DrawingBrushRoll);
            Quaternion hand = Quaternion.Slerp(a * Quaternion.Inverse(GripRotationLocal(0f)), vertical, Mathf.Clamp01(blend));
            grip = hand * GripRotationLocal(blend);
            return hand;
        }

        // Elbow bend direction on the two-bone circle: prefer a forearm lying across the shaft (the raised,
        // perpendicular double-hook forearm), then the authored outward/down bias that grows with stroke x/height.
        private Vector3 VerticalBend(Body body, Vector3 wrist, Vector3 axis, Transform frame, Vector2 point, int samples, out float perpendicular)
        {
            Vector3 shoulder = body.Upper.position;
            Vector3 reach = wrist - shoulder;
            Vector3 along = reach.sqrMagnitude > .00000001f ? reach.normalized : frame.forward;
            Vector3 e1 = Vector3.ProjectOnPlane(frame.right, along);
            if (e1.sqrMagnitude < .000001f) e1 = Vector3.ProjectOnPlane(frame.up, along);
            e1.Normalize();
            Vector3 e2 = Vector3.Cross(along, e1);
            // Pen-grip elbow rule: the elbow sits out to the right, follows the brush to the right, and drops as the brush
            // crosses to the left (the forearm swings under the stroke instead of the elbow hanging straight down).
            Vector3 desired = _profile.ShuanggouElbowRule
                ? Vector3.ProjectOnPlane(frame.right * Mathf.Max(.05f, _profile.ShuanggouElbowRight + _profile.ShuanggouElbowRightPerX * point.x)
                    - frame.up * Mathf.Max(.04f, _profile.ShuanggouElbowDown + _profile.ShuanggouElbowDownPerLeft * Mathf.Max(0f, -point.x)), along)
                : Vector3.ProjectOnPlane(frame.right * (_profile.ElbowOutward + .12f * Mathf.Max(0f, point.x) + .10f * Mathf.Max(0f, point.y))
                - frame.up * Mathf.Max(.04f, _profile.ElbowDown - .16f * Mathf.Max(0f, point.y)), along);
            desired = desired.sqrMagnitude > .000001f ? desired.normalized : e1;
            float step = Mathf.PI * 2f / Mathf.Max(3, samples);
            float best = float.PositiveInfinity, previous = 0f, next = 0f, first = 0f, last = 0f;
            int bestIndex = 0;
            perpendicular = 1f;
            for (int i = 0; i < samples; i++)
            {
                float cost = BendCost(body, shoulder, wrist, axis, frame, desired, e1 * Mathf.Cos(i * step) + e2 * Mathf.Sin(i * step), out float perp);
                if (i == 0) first = cost;
                if (i == samples - 1) last = cost;
                if (cost < best) { best = cost; bestIndex = i; perpendicular = perp; }
            }
            // Parabolic refinement between the neighbouring samples keeps the bend plane continuous.
            previous = bestIndex > 0 ? BendCost(body, shoulder, wrist, axis, frame, desired, e1 * Mathf.Cos((bestIndex - 1) * step) + e2 * Mathf.Sin((bestIndex - 1) * step), out _) : last;
            next = bestIndex < samples - 1 ? BendCost(body, shoulder, wrist, axis, frame, desired, e1 * Mathf.Cos((bestIndex + 1) * step) + e2 * Mathf.Sin((bestIndex + 1) * step), out _) : first;
            float denominator = previous - 2f * best + next;
            float offset = denominator > .000001f ? Mathf.Clamp(.5f * (previous - next) / denominator, -.5f, .5f) : 0f;
            float angle = (bestIndex + offset) * step;
            return e1 * Mathf.Cos(angle) + e2 * Mathf.Sin(angle);
        }

        private float BendCost(Body body, Vector3 shoulder, Vector3 wrist, Vector3 axis, Transform frame, Vector3 desired, Vector3 direction, out float perpendicular)
        {
            Vector3 elbow = PlayerVisualIK.ElbowPosition(shoulder, wrist, shoulder + direction,
                body.UpperLength, body.ForearmLength, _profile.MaximumArmExtension);
            Vector3 forearm = wrist - elbow;
            perpendicular = forearm.sqrMagnitude > .00000001f ? Mathf.Abs(Vector3.Dot(forearm.normalized, axis)) : 1f;
            float rise = Mathf.Max(0f, Vector3.Dot(elbow - shoulder, frame.up) + .02f);
            return _profile.ShuanggouElbowRule
                ? perpendicular * _profile.ShuanggouBendPerpendicularWeight + (1f - Vector3.Dot(direction, desired)) * _profile.ShuanggouBendDesiredWeight + rise * 4f
                : perpendicular + (1f - Vector3.Dot(direction, desired)) * .35f + rise * 4f;
        }

        private Vector3 VerticalPole(Body body, Vector3 wrist, Vector3 axis, Transform frame, Vector2 point, ref VerticalFollow state, float dt)
        {
            Vector3 bend = VerticalBend(body, wrist, axis, frame, point, 36, out _);
            Vector3 local = frame.InverseTransformDirection(bend);
            state.Pole = state.PoleValid ? Vector3.Slerp(state.Pole, local, Lag(_profile.ElbowLag, dt)).normalized : local;
            state.PoleValid = true;
            Vector3 reach = wrist - body.Upper.position;
            Vector3 smoothed = reach.sqrMagnitude > .00000001f
                ? Vector3.ProjectOnPlane(frame.TransformDirection(state.Pole), reach.normalized) : bend;
            if (smoothed.sqrMagnitude < .015f) smoothed = bend;
            return body.Upper.position + smoothed.normalized * Mathf.Max(.2f, body.UpperLength);
        }

        private void EvaluateVerticalWorldBristles(Vector3 tip, Vector3 velocity, bool stroking, float dt)
        {
            Transform basis = _world.Root;
            Vector3 axis = _profile.ShuanggouFist ? FistAxis(basis, _bodyPoint.x, FistElbowDirection(basis))
                : VerticalAxis(basis.forward, basis, _bodyPoint);
            BlendedHand(_worldBrush, axis, basis.up, tip - axis * _worldBrush.TipOffset.magnitude - _world.Upper.position,
                VerticalBlend, out Quaternion grip);
            EvaluateBristlesGrip(_worldBrush, velocity, basis, grip, stroking, dt);
        }

        private static void EvaluateBristlesGrip(Brush brush, Vector3 velocity, Transform basis, Quaternion grip, bool stroking, float dt)
        {
            if (brush.Bristles == null || !brush.Bristles.IsBound) return;
            brush.Bristles.EvaluateInFrame(velocity, basis.rotation, grip, stroking, dt);
        }

        private Vector3 PoseTowardTipVertical(Body body, Brush brush, Vector3 tip, Transform basis, float weight)
        {
            if (_profile.ShuanggouFist) return PoseTowardTipFist(body, brush, tip, basis, weight);
            float blend = VerticalBlend, dt = _frameDt;
            Vector3 shoulder = body.Upper.position;
            float length = brush.TipOffset.magnitude;
            float share = _profile.ArticulatedStrokes ? _strokeMotion.ArmShare : 1f;
            Vector3 axis0 = VerticalAxis(basis.forward, basis, _bodyPoint);
            Vector3 ideal = tip - axis0 * length;
            // Small strokes pivot about the stroke-start grasp (fingers/wrist); large strokes carry the grasp.
            Vector3 target = _profile.ArticulatedStrokes && _strokeMotion.Contact && _strokeAnchorReady
                ? Vector3.Lerp(basis.TransformPoint(_worldStrokeGrip), ideal, share) : ideal;
            Vector3 local = basis.InverseTransformPoint(target);
            _vWorld.Grip = _vWorld.Valid ? Vector3.Lerp(_vWorld.Grip, local, Lag(_profile.WristLag, dt)) : local;
            _vWorld.Valid = true;
            float cone = _profile.ShuanggouCone * (_profile.ArticulatedStrokes ? 1f - share : 1f);
            Vector3 axis = ClampCone(tip - basis.TransformPoint(_vWorld.Grip), axis0, cone);
            Vector3 gripPosition = Vector3.Scale(GripPositionLocal(blend), body.Hand.lossyScale);
            Quaternion hand = BlendedHand(brush, axis, basis.up, tip - axis * length - shoulder, blend, out Quaternion grip);
            Vector3 wrist = tip - grip * brush.TipOffset - hand * gripPosition;
            Vector3 pole = VerticalPole(body, wrist, axis, basis, _bodyPoint, ref _vWorld, dt);
            Vector3 elbow = PlayerVisualIK.ElbowPosition(shoulder, wrist, pole, body.UpperLength, body.ForearmLength, _profile.MaximumArmExtension);
            hand = BlendedHand(brush, axis, basis.up, wrist - elbow, blend, out grip);
            wrist = tip - grip * brush.TipOffset - hand * gripPosition;
            PlayerVisualIK.Solve(body.Upper, body.Forearm, body.Hand, wrist, pole, hand, weight, _profile.MaximumArmExtension);
            AlignForearmToHand(body, weight);
            return wrist;
        }

        private void ApplyNearArmVertical(Camera camera, Vector2 screen, Vector3 visualVelocity, bool stroking, float dt)
        {
            if (_profile.ShuanggouFist) { ApplyNearArmFist(camera, screen, visualVelocity, stroking, dt); return; }
            Transform view = camera.transform;
            float blend = NearBlend;
            foreach (var pair in _nearCopies)
            {
                pair.Target.localPosition = pair.Source.localPosition;
                pair.Target.localRotation = pair.Source.localRotation;
            }
            foreach (var finger in _fingers)
                if (finger.Near != null)
                    finger.Near.localRotation = FingerLocalRotation(finger.Rest, finger.Pose, 1f, _harvestWeight);
            Rect rect = camera.pixelRect;
            Vector2 centered = new Vector2((screen.x - rect.x) / Mathf.Max(1f, rect.width) - .5f,
                (screen.y - rect.y) / Mathf.Max(1f, rect.height) - .5f) * 2f;
            if (_vNear.Valid && Vector2.Distance(centered, _vNear.Screen) > .40f) _vNear.Valid = _vNear.PoleValid = false;
            bool continuous = _vNear.Valid;
            // Shoulder follow: at most 5 cm, damped, counted inside the authored shoulder-correction budget.
            float followLimit = Mathf.Min(_profile.NearShoulderFollowMaximum, _profile.MaximumNearShoulderCorrection);
            Vector3 followTarget = Vector3.ClampMagnitude(new Vector3(centered.x * .035f, centered.y * .025f, .015f)
                * _profile.ShoulderFollow, followLimit);
            _vNear.Shoulder = continuous ? Vector3.Lerp(_vNear.Shoulder, followTarget, Lag(_profile.ShoulderLag, dt)) : followTarget;
            _nearRoot.rotation = view.rotation;
            _nearRoot.position += view.TransformPoint(_profile.NearShoulderOffset + _vNear.Shoulder) - _near.Upper.position;
            Vector3 follow = view.TransformVector(_vNear.Shoulder);
            MeasureLengths(_near);
            Ray ray = camera.ScreenPointToRay(screen);
            float preferred = Mathf.Max(camera.nearClipPlane + .08f, _profile.NearTipDepth);
            float minimum = Mathf.Max(camera.nearClipPlane + .04f, _profile.NearMinimumDepth);
            float maximum = Mathf.Max(minimum, _profile.NearMaximumDepth);
            Vector3 shoulder = _near.Upper.position;
            float share = _profile.ArticulatedStrokes ? _strokeMotion.ArmShare : 1f;
            float length = _nearBrush.TipOffset.magnitude;
            float reach = Mathf.Max(.01f, _near.UpperLength + _near.ForearmLength);
            Vector3 gripPosition = Vector3.Scale(GripPositionLocal(blend), _near.Hand.lossyScale);
            Vector3 axis0 = VerticalAxis(ray.direction, view, centered);

            // Depth only: the tip stays on the input ray. Prefer a reachable grasp whose forearm can lie across
            // the shaft (the double-hook posture) rather than along it.
            Quaternion searchHand = BlendedHand(_nearBrush, axis0, view.up, Vector3.zero, blend, out Quaternion searchGrip);
            Vector3 handOffset = searchGrip * _nearBrush.TipOffset + searchHand * gripPosition;
            int bestIndex = 0;
            float best = float.PositiveInfinity;
            for (int d = 0; d <= VerticalDepthSamples; d++)
            {
                float depth = Mathf.Lerp(minimum, maximum, d / (float)VerticalDepthSamples);
                Vector3 w = ray.GetPoint(depth) - handOffset;
                float ratio = Vector3.Distance(w, shoulder) / reach;
                VerticalBend(_near, w, axis0, view, centered, 12, out float perpendicular);
                float viewDepth = Vector3.Dot(w - view.position, view.forward);
                float cost = Mathf.Max(0f, ratio - _profile.MaximumArmExtension) * 400f
                    + Mathf.Abs(ratio - _profile.ShuanggouReach) * 2f + perpendicular * 3f * (_profile.ShuanggouElbowRule ? _profile.ShuanggouBendPerpendicularWeight : 1f)
                    + Mathf.Max(0f, .25f - viewDepth) * 30f + Mathf.Abs(depth - preferred) * .15f
                    + (continuous ? Mathf.Abs(depth - _vNear.Depth) * 1.5f : 0f);
                _depthCosts[d] = cost;
                if (cost < best) { best = cost; bestIndex = d; }
            }
            float refined = bestIndex;
            if (bestIndex > 0 && bestIndex < VerticalDepthSamples)
            {
                float a = _depthCosts[bestIndex - 1], c = _depthCosts[bestIndex + 1], denominator = a - 2f * best + c;
                if (denominator > .000001f) refined += Mathf.Clamp(.5f * (a - c) / denominator, -.5f, .5f);
            }
            float chosen = Mathf.Lerp(minimum, maximum, refined / VerticalDepthSamples);
            if (_profile.ArticulatedStrokes && stroking && _strokeAnchorReady)
            {
                // Exact ray/sphere depth about the stroke-start grasp: small strokes pivot in the fingers.
                Vector3 pivot = view.TransformPoint(_nearStrokeGrip);
                Vector3 offset = ray.origin - pivot;
                float b = Vector3.Dot(offset, ray.direction);
                float discriminant = b * b - offset.sqrMagnitude + length * length;
                if (discriminant >= 0f)
                {
                    float root = Mathf.Sqrt(discriminant), far = -b + root, near = -b - root;
                    bool farOk = far >= minimum && far <= maximum, nearOk = near >= minimum && near <= maximum;
                    float reference = continuous ? _vNear.Depth : chosen;
                    if (farOk || nearOk)
                    {
                        float pivotDepth = farOk && (!nearOk || Mathf.Abs(far - reference) <= Mathf.Abs(near - reference)) ? far : near;
                        chosen = Mathf.Lerp(pivotDepth, chosen, share);
                    }
                }
            }
            float depthNow = continuous ? Mathf.Lerp(_vNear.Depth, chosen, Lag(.06f, dt)) : chosen;
            float liftTarget = stroking ? 0f : _profile.PenLiftDistance;
            _vNear.Lift = continuous ? Mathf.Lerp(_vNear.Lift, liftTarget, Lag(.05f, dt)) : liftTarget;
            Vector3 tip = ray.GetPoint(depthNow - Mathf.Min(_vNear.Lift, Mathf.Max(0f, depthNow - minimum)));

            Vector3 ideal = tip - axis0 * length;
            Vector3 target = _profile.ArticulatedStrokes && stroking && _strokeAnchorReady
                ? Vector3.Lerp(view.TransformPoint(_nearStrokeGrip), ideal, share) : ideal;
            Vector3 targetLocal = view.InverseTransformPoint(target);
            _vNear.Grip = continuous ? Vector3.Lerp(_vNear.Grip, targetLocal, Lag(_profile.WristLag, dt)) : targetLocal;
            float cone = _profile.ShuanggouCone * (_profile.ArticulatedStrokes ? 1f - share : 1f);
            Vector3 axis = ClampCone(tip - view.TransformPoint(_vNear.Grip), axis0, cone);

            Quaternion hand = BlendedHand(_nearBrush, axis, view.up, tip - axis * length - shoulder, blend, out Quaternion grip);
            EvaluateBristlesGrip(_nearBrush, visualVelocity, view, grip, stroking, dt);
            Vector3 wrist = tip - grip * _nearBrush.TipOffset - hand * gripPosition;
            Vector3 pole = VerticalPole(_near, wrist, axis, view, centered, ref _vNear, dt);
            Vector3 elbow = PlayerVisualIK.ElbowPosition(shoulder, wrist, pole, _near.UpperLength, _near.ForearmLength, _profile.MaximumArmExtension);
            hand = BlendedHand(_nearBrush, axis, view.up, wrist - elbow, blend, out grip);
            wrist = tip - grip * _nearBrush.TipOffset - hand * gripPosition;
            Vector3 clamped = PlayerVisualIK.ClampReach(_near.Upper.position, wrist, _near.UpperLength, _near.ForearmLength, _profile.MaximumArmExtension);
            Vector3 reachCorrection = Vector3.ClampMagnitude(wrist - clamped,
                Mathf.Max(0f, _profile.MaximumNearShoulderCorrection - follow.magnitude));
            _nearRoot.position += reachCorrection;
            PlayerVisualIK.Solve(_near.Upper, _near.Forearm, _near.Hand, wrist, pole + reachCorrection, hand, 1f, _profile.MaximumArmExtension);
            _nearHasSolution = true;
            if (_profile.ArticulatedStrokes && stroking && !_strokeAnchorReady)
            {
                _nearStrokeWrist = view.InverseTransformPoint(wrist);
                _nearStrokeGrip = view.InverseTransformPoint(tip - grip * _nearBrush.TipOffset);
                _worldStrokeGrip = _world.Root.InverseTransformPoint(_worldBrush.Grip.position);
                _strokeAnchorReady = true;
            }
            _vNear.Valid = true; _vNear.Depth = depthNow; _vNear.Screen = centered;
            _nearLastAim = view.InverseTransformDirection(axis);
            _nearLastDepth = Vector3.Distance(ray.origin, tip);
            _nearLastScreen = new Vector2(screen.x / Mathf.Max(1, camera.pixelWidth), screen.y / Mathf.Max(1, camera.pixelHeight));
            _nearLastElbow = view.InverseTransformDirection(_near.Forearm.position - _near.Upper.position);
            _diagnostics.NearElbowHeight = Vector3.Dot(_near.Forearm.position - _near.Upper.position, view.up);
            _diagnostics.NearElbowFlexion = Vector3.Angle(_near.Forearm.position - _near.Upper.position, _near.Hand.position - _near.Forearm.position);
            _diagnostics.OverhandActive = false;
            _diagnostics.VerticalActive = blend > .99f;
            _diagnostics.NearForearmTwistDegrees = AlignForearmToHand(_near, 1f);
            Vector3 forearmAxis = (_near.Hand.position - _near.Forearm.position).normalized;
            Vector3 dorsal = _near.Hand.rotation * _profile.HandDorsalLocal;
            _diagnostics.NearWristBendDegrees = Vector3.Angle(forearmAxis, _near.Hand.rotation * _profile.HandForwardLocal);
            _diagnostics.NearWristTwistDegrees = Vector3.SignedAngle(Vector3.ProjectOnPlane(_near.Forearm.rotation * _profile.HandRestInForearm * _profile.HandDorsalLocal, forearmAxis),
                Vector3.ProjectOnPlane(dorsal, forearmAxis), forearmAxis);
            _diagnostics.NearDorsalUp = Vector3.Dot(dorsal.normalized, view.up);
            _diagnostics.NearDorsalRight = Vector3.Dot(dorsal.normalized, view.right);
            _diagnostics.NearBrushThumbSideDot = Vector3.Dot((grip * _nearBrush.RestTipOffset).normalized,
                (_near.Hand.rotation * _profile.HandThumbSideLocal).normalized);
            _diagnostics.NearVerticalAxisErrorDegrees = Vector3.Angle(grip * _nearBrush.RestTipOffset, axis0);
            _diagnostics.NearVerticalConeDegrees = cone;
            FinishNearArm(camera, screen, wrist, follow, reachCorrection);
        }

        // ---------------------------------------------------------------- fist grip (#297)
        // A big brush held in the fist: the elbow stays near an anchor out to the right (it moves only when the tip
        // would be out of reach), strokes up/down are made by bending the elbow (the wrist moves on the forearm sphere
        // around the elbow), and strokes left/right by the wrist: the brush turns from parallel with the upper arm
        // (right) to perpendicular to it (left).

        private Vector3 FistElbowDirection(Transform frame)
        {
            Vector3 d = frame.TransformDirection(_profile.FistElbowDirection);
            return d.sqrMagnitude > .00000001f ? d.normalized : frame.right;
        }

        private Vector3 FistAxis(Transform frame, float x, Vector3 upper)
        {
            Vector3 u = upper.sqrMagnitude > .00000001f ? upper.normalized : frame.right;
            Vector3 right = u;
            float into = Vector3.Dot(right, frame.forward);
            if (into < .25f) right = (right + frame.forward * (.25f - into)).normalized;
            Vector3 left = Vector3.Cross(frame.up, u);
            if (left.sqrMagnitude < .000001f) left = -frame.right;
            if (Vector3.Dot(left, frame.forward) < 0f) left = -left;
            left = (left.normalized + frame.forward * _profile.FistLeftForward).normalized;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_profile.FistSweepRange.x, _profile.FistSweepRange.y, x));
            return (Vector3.Slerp(left, right, t).normalized + frame.up * _profile.FistRise).normalized;
        }

        // Fist hand: the wrist-neutral hand (fingers along the forearm, back of the hand up) turned by the smallest
        // rotation that points the brush along the drawing axis — the wrist, not the elbow, aims the brush.
        private Quaternion FistHand(Brush brush, Vector3 axis, Vector3 up, Vector3 forearm)
        {
            Vector3 shaft = VerticalShaftLocal(brush);
            Vector3 a = axis.sqrMagnitude > .000001f ? axis.normalized : Vector3.forward;
            Vector3 f = forearm.sqrMagnitude > .00000001f ? forearm.normalized : a;
            Vector3 d = Vector3.ProjectOnPlane(up, f);
            if (d.sqrMagnitude < .000001f) d = Vector3.ProjectOnPlane(Vector3.up, f);
            if (d.sqrMagnitude < .000001f) d = Vector3.ProjectOnPlane(Vector3.forward, f);
            Vector3 fl = _profile.HandForwardLocal.normalized;
            Vector3 dl = Vector3.ProjectOnPlane(_profile.HandDorsalLocal, fl);
            if (dl.sqrMagnitude < .000001f) dl = Vector3.ProjectOnPlane(Vector3.forward, fl);
            Quaternion upright = Quaternion.LookRotation(f, d.normalized) * Quaternion.Inverse(Quaternion.LookRotation(fl, dl.normalized));
            // forearm roll first (pronation/supination, limited around back-of-hand-up), then the wrist bends the rest
            Vector3 s0 = Vector3.ProjectOnPlane(upright * shaft, f), across = Vector3.ProjectOnPlane(a, f);
            float roll = 0f;
            if (s0.sqrMagnitude > .000001f && across.sqrMagnitude > .000001f)
                roll = Mathf.Clamp(Vector3.SignedAngle(s0, across, f), -_profile.FistRollLimit, _profile.FistRollLimit)
                    * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.1f, .45f, across.magnitude));
            Quaternion neutral = Quaternion.AngleAxis(roll + _profile.FistKnuckleRoll, f) * upright;
            return Quaternion.FromToRotation(neutral * shaft, a) * neutral;
        }

        private Vector3 PoseTowardTipFist(Body body, Brush brush, Vector3 tip, Transform basis, float weight)
        {
            float blend = VerticalBlend;
            Vector3 shoulder = body.Upper.position;
            Vector3 upper = FistElbowDirection(basis);
            Vector3 elbow = shoulder + upper * body.UpperLength;
            Vector3 axis = FistAxis(basis, _bodyPoint.x, upper);
            Vector3 gripPosition = Vector3.Scale(GripPositionLocal(blend), body.Hand.lossyScale);
            Quaternion hand = BlendedHand(brush, axis, basis.up, tip - axis * brush.TipOffset.magnitude - elbow, blend, out Quaternion grip);
            Vector3 wrist = tip - grip * brush.TipOffset - hand * gripPosition;
            hand = BlendedHand(brush, axis, basis.up, wrist - elbow, blend, out grip);
            wrist = tip - grip * brush.TipOffset - hand * gripPosition;
            PlayerVisualIK.Solve(body.Upper, body.Forearm, body.Hand, wrist, elbow + (elbow - (shoulder + wrist) * .5f) * .5f, hand, weight, _profile.MaximumArmExtension);
            AlignForearmToHand(body, weight);
            return wrist;
        }

        private void ApplyNearArmFist(Camera camera, Vector2 screen, Vector3 visualVelocity, bool stroking, float dt)
        {
            Transform view = camera.transform;
            float blend = NearBlend;
            foreach (var pair in _nearCopies)
            {
                pair.Target.localPosition = pair.Source.localPosition;
                pair.Target.localRotation = pair.Source.localRotation;
            }
            foreach (var finger in _fingers)
                if (finger.Near != null)
                    finger.Near.localRotation = FingerLocalRotation(finger.Rest, finger.Pose, 1f, _harvestWeight);
            Rect rect = camera.pixelRect;
            Vector2 centered = new Vector2((screen.x - rect.x) / Mathf.Max(1f, rect.width) - .5f,
                (screen.y - rect.y) / Mathf.Max(1f, rect.height) - .5f) * 2f;
            if (_vNear.Valid && Vector2.Distance(centered, _vNear.Screen) > .40f) _vNear.Valid = _vNear.PoleValid = false;
            bool continuous = _vNear.Valid;
            float followLimit = Mathf.Min(_profile.NearShoulderFollowMaximum, _profile.MaximumNearShoulderCorrection);
            Vector3 followTarget = Vector3.ClampMagnitude(new Vector3(centered.x * .035f, centered.y * .025f, .015f)
                * _profile.ShoulderFollow, followLimit);
            _vNear.Shoulder = continuous ? Vector3.Lerp(_vNear.Shoulder, followTarget, Lag(_profile.ShoulderLag, dt)) : followTarget;
            _nearRoot.rotation = view.rotation;
            _nearRoot.position += view.TransformPoint(_profile.NearShoulderOffset + _vNear.Shoulder) - _near.Upper.position;
            Vector3 follow = view.TransformVector(_vNear.Shoulder);
            MeasureLengths(_near);
            Ray ray = camera.ScreenPointToRay(screen);
            float preferred = Mathf.Max(camera.nearClipPlane + .08f, _profile.NearTipDepth);
            float minimum = Mathf.Max(camera.nearClipPlane + .04f, _profile.NearMinimumDepth);
            float maximum = Mathf.Max(minimum, _profile.NearMaximumDepth);
            Vector3 shoulder = _near.Upper.position;
            float upperLength = _near.UpperLength, forearmLength = _near.ForearmLength * .995f;
            Vector3 upper = FistElbowDirection(view);
            Vector3 anchor = shoulder + upper * upperLength;
            // the elbow drifts back to its anchor; it only leaves it by what reach demands
            Vector3 elbow = continuous && _vNear.PoleValid
                ? Vector3.Lerp(view.TransformPoint(_vNear.Pole), anchor, Lag(_profile.FistElbowReturn, dt)) : anchor;
            elbow = shoulder + (elbow - shoulder).normalized * upperLength;
            Vector3 axis = FistAxis(view, centered.x, upper);
            Vector3 gripPosition = Vector3.Scale(GripPositionLocal(blend), _near.Hand.lossyScale);
            float reference = continuous ? _vNear.Depth : preferred, depth = reference;
            Vector3 forearmGuess = ray.GetPoint(depth) - axis * _nearBrush.TipOffset.magnitude - elbow;
            Quaternion hand = Quaternion.identity, grip = Quaternion.identity;
            Vector3 reachVector = Vector3.zero;
            for (int pass = 0; pass < 4; pass++)
            {
                hand = BlendedHand(_nearBrush, axis, view.up, forearmGuess, blend, out grip);
                reachVector = grip * _nearBrush.TipOffset + hand * gripPosition;   // wrist -> tip
                // depth where the wrist lies on the forearm sphere around the elbow (up/down = elbow bend)
                Vector3 oc = ray.origin - (elbow + reachVector);
                float b = Vector3.Dot(oc, ray.direction), disc = b * b - oc.sqrMagnitude + forearmLength * forearmLength;
                if (disc >= 0f)
                {
                    float root = Mathf.Sqrt(disc), near = -b - root, far = -b + root;
                    bool nearOk = near >= minimum && near <= maximum, farOk = far >= minimum && far <= maximum;
                    depth = nearOk && farOk ? (Mathf.Abs(near - reference) <= Mathf.Abs(far - reference) ? near : far)
                        : nearOk ? near : farOk ? far : Mathf.Clamp(-b, minimum, maximum);
                }
                else depth = Mathf.Clamp(-b, minimum, maximum);
                Vector3 w = ray.GetPoint(depth) - reachVector;
                // out of reach at every depth: the elbow moves (back onto the shoulder sphere) by the remaining gap
                float gap = Vector3.Distance(w, elbow) - forearmLength;
                if (Mathf.Abs(gap) > .001f)
                {
                    elbow += (w - elbow).normalized * gap;
                    elbow = shoulder + (elbow - shoulder).normalized * upperLength;
                }
                forearmGuess = w - elbow;
            }
            float depthNow = continuous ? Mathf.Lerp(_vNear.Depth, depth, Lag(.06f, dt)) : depth;
            float liftTarget = stroking ? 0f : _profile.PenLiftDistance;
            _vNear.Lift = continuous ? Mathf.Lerp(_vNear.Lift, liftTarget, Lag(.05f, dt)) : liftTarget;
            Vector3 tip = ray.GetPoint(depthNow - Mathf.Min(_vNear.Lift, Mathf.Max(0f, depthNow - minimum)));
            hand = BlendedHand(_nearBrush, axis, view.up, forearmGuess, blend, out grip);
            EvaluateBristlesGrip(_nearBrush, visualVelocity, view, grip, stroking, dt);
            Vector3 wrist = tip - grip * _nearBrush.TipOffset - hand * gripPosition;
            Vector3 pole = elbow + (elbow - (shoulder + wrist) * .5f) * .5f;
            Vector3 clamped = PlayerVisualIK.ClampReach(shoulder, wrist, upperLength, _near.ForearmLength, _profile.MaximumArmExtension);
            Vector3 reachCorrection = Vector3.ClampMagnitude(wrist - clamped,
                Mathf.Max(0f, _profile.MaximumNearShoulderCorrection - follow.magnitude));
            _nearRoot.position += reachCorrection;
            PlayerVisualIK.Solve(_near.Upper, _near.Forearm, _near.Hand, wrist, pole + reachCorrection, hand, 1f, _profile.MaximumArmExtension);
            _nearHasSolution = true;
            if (_profile.ArticulatedStrokes && stroking && !_strokeAnchorReady)
            {
                _nearStrokeWrist = view.InverseTransformPoint(wrist);
                _nearStrokeGrip = view.InverseTransformPoint(tip - grip * _nearBrush.TipOffset);
                _worldStrokeGrip = _world.Root.InverseTransformPoint(_worldBrush.Grip.position);
                _strokeAnchorReady = true;
            }
            _vNear.Valid = true; _vNear.Depth = depthNow; _vNear.Screen = centered;
            _vNear.Pole = view.InverseTransformPoint(elbow); _vNear.PoleValid = true;
            _nearLastAim = view.InverseTransformDirection(axis);
            _nearLastDepth = Vector3.Distance(ray.origin, tip);
            _nearLastScreen = new Vector2(screen.x / Mathf.Max(1, camera.pixelWidth), screen.y / Mathf.Max(1, camera.pixelHeight));
            _nearLastElbow = view.InverseTransformDirection(_near.Forearm.position - _near.Upper.position);
            _diagnostics.NearElbowHeight = Vector3.Dot(_near.Forearm.position - _near.Upper.position, view.up);
            _diagnostics.NearElbowFlexion = Vector3.Angle(_near.Forearm.position - _near.Upper.position, _near.Hand.position - _near.Forearm.position);
            _diagnostics.OverhandActive = false;
            _diagnostics.VerticalActive = blend > .99f;
            _diagnostics.NearForearmTwistDegrees = AlignForearmToHand(_near, 1f);
            Vector3 forearmAxis = (_near.Hand.position - _near.Forearm.position).normalized;
            Vector3 dorsal = _near.Hand.rotation * _profile.HandDorsalLocal;
            _diagnostics.NearWristBendDegrees = Vector3.Angle(forearmAxis, _near.Hand.rotation * _profile.HandForwardLocal);
            _diagnostics.NearDorsalUp = Vector3.Dot(dorsal.normalized, view.up);
            _diagnostics.NearDorsalRight = Vector3.Dot(dorsal.normalized, view.right);
            _diagnostics.NearVerticalAxisErrorDegrees = Vector3.Angle(grip * _nearBrush.RestTipOffset, axis);
            _diagnostics.NearVerticalConeDegrees = 0f;
            FinishNearArm(camera, screen, wrist, follow, reachCorrection);
        }

        // Uniform brush size: the brush's children are moved under one scaled child so the root keeps unit scale
        // (BindBrush contract) and every bone keeps its local pose; lengths/offsets are measured after scaling.
        private void ApplyBrushSize(Transform root)
        {
            if (!Application.isPlaying || root == null || _profile == null) return;
            float size = Mathf.Clamp(_profile.BrushSize, .5f, 3f);
            Transform sizer = root.Find(BrushSizeNode);
            if (sizer == null)
            {
                if (Mathf.Abs(size - 1f) < .001f) return;
                sizer = new GameObject(BrushSizeNode).transform;
                sizer.SetParent(root, false);
                var children = new List<Transform>();
                foreach (Transform child in root) if (child != sizer) children.Add(child);
                foreach (var child in children) child.SetParent(sizer, true);
            }
            sizer.localScale = Vector3.one * size;
        }

        private const string BrushSizeNode = "BrushSize297";

        // Handle-only length stretch pinned at the ferrule (Bristle_01). The brush root, GripSocket, TipSocket and
        // six bristle bones keep unit scale, so BindBrush/BrushBristleRig contracts and the tip offset are unchanged.
        private void ApplyHandleStretch(Brush brush)
        {
            if (!Application.isPlaying || brush == null || brush.ShaftEnd == null || _profile == null || !_profile.ShuanggouGrip) return;
            float scale = Mathf.Clamp(_profile.BrushScale, 1f, 1.6f);
            if (scale - 1f < .001f) return;
            Vector3 axis = brush.Grip.rotation * brush.RestTipOffset;
            if (axis.sqrMagnitude < .00000001f) return;
            float aligned = Mathf.Cos(5f * Mathf.Deg2Rad);
            foreach (var renderer in brush.Renderers)
            {
                if (!(renderer is MeshRenderer) || renderer == null) continue;
                Transform handle = renderer.transform;
                if (handle == brush.Root || handle.childCount > 0 || handle == brush.ShaftEnd || handle.IsChildOf(brush.ShaftEnd)) continue;
                Vector3 local = handle.InverseTransformDirection(axis).normalized;
                int k = Mathf.Abs(local.x) >= Mathf.Abs(local.y) && Mathf.Abs(local.x) >= Mathf.Abs(local.z) ? 0
                    : Mathf.Abs(local.y) >= Mathf.Abs(local.z) ? 1 : 2;
                if (Mathf.Abs(local[k]) < aligned) { _diagnostics.HandleStretchRejected++; continue; }
                Vector3 pin = handle.InverseTransformPoint(brush.ShaftEnd.position);
                Vector3 before = handle.localScale, after = before;
                after[k] = before[k] * scale;
                _stretched.Add(new HandleStretch { Target = handle, Position = handle.localPosition, Scale = before });
                handle.localPosition += handle.localRotation * (Vector3.Scale(before, pin) - Vector3.Scale(after, pin));
                handle.localScale = after;
                _diagnostics.StretchedHandles++;
            }
        }

        private void RestoreHandleStretch()
        {
            if (_stretched == null) { _stretched = new List<HandleStretch>(4); return; }
            for (int i = _stretched.Count - 1; i >= 0; i--)
            {
                var entry = _stretched[i];
                if (entry.Target == null) continue;
                entry.Target.localPosition = entry.Position;
                entry.Target.localScale = entry.Scale;
            }
            _stretched.Clear();
        }

        private void SetNearVisible(bool visible)
        {
            // The hidden chain is not evaluated. Clear its previous stroke bend once at the
            // visibility boundary so re-entry starts from the authored brush rest shape.
            if (_nearVisible && !visible) _nearBrush?.Bristles?.ResetDeformation();
            if(!visible){_nearHasSolution=false;_vNear.Valid=_vNear.PoleValid=false;}
            _nearVisible = visible;
            _diagnostics.NearVisible = visible;
            if (_nearRenderers != null)
                foreach (var renderer in _nearRenderers)
                    if (renderer != null) { renderer.enabled = visible; renderer.shadowCastingMode = ShadowCastingMode.Off; }
            if (_nearBrush != null)
                foreach (var renderer in _nearBrush.Renderers)
                    if (renderer != null) { renderer.enabled = visible; renderer.shadowCastingMode = ShadowCastingMode.Off; }
        }

        private void BindHarvestSink()
        {
            if (_harvestEffect == null || _effectTip == null || _harvestEffect.SinkAnchor == _effectTip) return;
            _previousHarvestSink = _harvestEffect.SinkAnchor;
            _harvestEffect.ConfigureSinkAnchor(_effectTip);
        }

        private void ReleaseHarvestSink()
        {
            if (_harvestEffect != null && _harvestEffect.SinkAnchor == _effectTip)
                _harvestEffect.ConfigureSinkAnchor(_previousHarvestSink);
            _previousHarvestSink = null;
        }

        private void ResetTransitions()
        {
            EndCast(); _castReach = 0f;
            _drawWeight = 0f; _harvestWeight = 0f; _recoveryRemaining = 0f;
            _bodyPoint = Vector2.zero; _hasPointerHistory = false; _wasStroking = false;
            _strokeMotion.Reset(); _strokeAnchorReady=false;
            _vNear = default; _vWorld = default;
            _worldBrush?.Bristles?.ResetDeformation(); _nearBrush?.Bristles?.ResetDeformation();
        }

        private void OnDisable()
        {
            RestoreHandleStretch();
            EndPendant();
            SetSkinningBound(false);
            Unsubscribe();
            RestoreAnimatedPose();
            SetNearVisible(false);
            _cameraRig?.SetDrawingPresentationActive(false);
            ReleaseHarvestSink();
            ResetTransitions();
        }

        private void SetSkinningBound(bool bound)
        {
            if (_skinningLease == null) _skinningLease = GetComponent<WorldMacroPlayerSkinningLease>();
            if (_skinningLease == null && bound && Application.isPlaying && isActiveAndEnabled)
                _skinningLease = gameObject.AddComponent<WorldMacroPlayerSkinningLease>();
            if (_skinningLease != null) _skinningLease.SetBound(bound);
        }

        private static float Follow(float current, float target, float rate, float dt)
        {
            float result = Mathf.Lerp(current, target, 1f - Mathf.Exp(-Mathf.Max(.01f, rate) * dt));
            return Mathf.Abs(result - target) < .0001f ? target : result;
        }

        private static Transform Find(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
