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
        }

        private void OnInterrupted()
        {
            _recoveryState = GestureState.Interrupted;
            _recoveryRemaining = _profile != null ? _profile.InterruptRecoverySeconds : .12f;
            _hasPointerHistory = false;
        }

        private void OnModeExited() { _hasPointerHistory = false; _wasStroking = false; }
        private void OnStrokeStarted() { _recoveryRemaining = 0f; _hasPointerHistory = false; }

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
                    SetNearVisible(false);
                    _cameraRig?.SetDrawingPresentationActive(false);
                    _hasPointerHistory = false;
                }
                return;
            }

            float dt = Mathf.Min(.1f, Mathf.Max(0f, Time.unscaledDeltaTime));
            bool drawing = _drawing != null && _drawing.InDrawMode;
            bool stroking = drawing && _drawing.IsStroking;
            bool harvesting = !drawing && _harvest != null && _harvest.IsExtracting;
            _drawWeight = Follow(_drawWeight, drawing ? 1f : 0f,
                drawing ? _profile.EnterResponse : _profile.ExitResponse, dt);
            _harvestWeight = Follow(_harvestWeight, harvesting ? 1f : 0f, _profile.HarvestResponse, dt);
            _recoveryRemaining = Mathf.Max(0f, _recoveryRemaining - dt);

            Camera camera = null;
            Vector2 screen = default;
            Vector3 ink = default;
            bool hasPointer = drawing && _brushFeed != null && _brushFeed.TryGetVisualPointer(out camera, out screen, out ink);
            Vector2 viewport = hasPointer ? new Vector2(
                (screen.x - camera.pixelRect.x) / Mathf.Max(1f, camera.pixelRect.width),
                (screen.y - camera.pixelRect.y) / Mathf.Max(1f, camera.pixelRect.height)) : new Vector2(.5f, .5f);
            Vector2 centered = (viewport - Vector2.one * .5f) * 2f;
            _bodyPoint = Vector2.Lerp(_bodyPoint, centered, 1f - Mathf.Exp(-_profile.BodyResponse * dt));
            if (stroking && !_wasStroking) _strokeMin = _strokeMax = viewport;
            if (stroking) { _strokeMin = Vector2.Min(_strokeMin, viewport); _strokeMax = Vector2.Max(_strokeMax, viewport); }
            float span = (_strokeMax - _strokeMin).magnitude;
            float bodyParticipation = Mathf.Lerp(_profile.SmallStrokeBodyWeight, 1f,
                Mathf.Clamp01(span / Mathf.Max(.01f, _profile.LargeStrokeViewportSpan))) * _drawWeight;
            Vector3 visualVelocity = hasPointer && stroking && _hasPointerHistory && dt > .00001f
                ? new Vector3((viewport.x - _lastPointer.x) * 2f / dt, (viewport.y - _lastPointer.y) * 2f / dt, 0f)
                : Vector3.zero;
            _lastPointer = viewport; _hasPointerHistory = hasPointer; _wasStroking = stroking;

            SaveAnimatedPose();
            MeasureLengths(_world);
            if (_pendantActive) { ApplyPendantPose(); return; }
            ApplyTorso(bodyParticipation);
            PoseCarry();
            bool tipContact = stroking && _recoveryRemaining <= 0f;
            Vector3 worldTipTarget = DrawingWorldTarget(_bodyPoint, !tipContact);
            // Each optional bristle chain is evaluated once, including pen-up recovery.
            EvaluateBristles(_worldBrush, visualVelocity, _world.Root,
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

            bool nearVisible = drawing && hasPointer && NearIsBound;
            SetNearVisible(nearVisible);
            _cameraRig?.SetDrawingPresentationActive(nearVisible);
            _diagnostics.NearTipErrorPixels = -1f;
            _diagnostics.NearShoulderCorrectionMeters = 0f;
            _diagnostics.TipReachable = !nearVisible;
            if (nearVisible) ApplyNearArm(camera, screen, visualVelocity, tipContact, dt);

            Brush activeBrush = nearVisible ? _nearBrush : _worldBrush;
            _effectTip.SetPositionAndRotation(activeBrush.Tip.position, activeBrush.Tip.rotation);
            _diagnostics.EffectTipWorld = _effectTip.position;
            _diagnostics.GripErrorMeters = Vector3.Distance(_world.Hand.TransformPoint(_profile.RightHandGripPosition), _worldBrush.Grip.position);
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
            return basis.position + basis.up * (_profile.WorldDrawingHeight - (_motor!=null?_motor.Crouch01*.45f:0) + point.y * _profile.WorldDrawingHalfSize.y)
                + basis.right * (point.x * _profile.WorldDrawingHalfSize.x)
                + basis.forward * (_profile.WorldDrawingDistance - (penLift ? _profile.PenLiftDistance : 0f));
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
            Quaternion grip = _profile.OverhandGrip ? DrawingGripRotation(brush.TipOffset,tip-body.Upper.position,basis.up) : GripRotation(brush.TipOffset, tip - body.Upper.position, basis.up, roll);
            Quaternion hand = grip * Quaternion.Inverse(WorldMacroPlayerGestureProfile.SafeRotation(_profile.RightHandGripRotation));
            Vector3 wrist = tip - grip * brush.TipOffset - hand * Vector3.Scale(_profile.RightHandGripPosition, body.Hand.lossyScale);
            Vector3 pole = ArmPole(body,wrist,hand,basis);
            PlayerVisualIK.Solve(body.Upper, body.Forearm, body.Hand, wrist, pole, hand, weight, _profile.MaximumArmExtension);
            AlignForearmToHand(body,weight);
            return wrist;
        }

        private void ApplyNearArm(Camera camera, Vector2 screen, Vector3 visualVelocity, bool stroking, float dt)
        {
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
                    float continuity=continuous ? Vector3.Distance(camera.transform.InverseTransformDirection(elbow-shoulder),_nearLastElbow)*60f+Vector3.Angle(direction,previousAim)*.12f+Mathf.Abs(Vector3.Distance(ray.origin,candidate)-_nearLastDepth)*12f : 0f;
                    float score=error*1000+clipped*20+anatomical+elbowCost+composition+continuity+Mathf.Abs(Vector3.Distance(ray.origin,candidate)-preferred)*.15f+Vector3.Angle(direction,(candidate-shoulder).normalized)*.001f;
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
            Vector3 correction = anchorCorrection + reachCorrection;
            // With an inward edge grip, a downward elbow occupies the same projected diagonal
            // as the shaft. Raise the elbow plane only for that corrective reach so the forearm
            // approaches the hand from above/right and leaves the bristles' diagonal clear.
            float elbowHeight = Mathf.Lerp(-_profile.ElbowDown, .36f, edgeAdjustment);
            Vector3 pole = _profile.OverhandGrip ? ArmPole(_near,wrist,hand,camera.transform) : Vector3.Lerp(_near.Upper.position, wrist, .5f)
                + camera.transform.right * _profile.ElbowOut + camera.transform.up * elbowHeight;
            PlayerVisualIK.Solve(_near.Upper, _near.Forearm, _near.Hand, wrist, pole, hand, 1f, _profile.MaximumArmExtension);
            _nearHasSolution=true;
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
            Quaternion grip = body.Hand.rotation * WorldMacroPlayerGestureProfile.SafeRotation(_profile.RightHandGripRotation);
            brush.Root.rotation = grip * Quaternion.Inverse(brush.RootToGripRotation);
            brush.Root.position += body.Hand.TransformPoint(_profile.RightHandGripPosition) - brush.Grip.position;
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
                Vector3 bend=Vector3.ProjectOnPlane(-basis.up+ basis.right*.32f,axis);
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
            if(!_profile.OverhandGrip)return 0;
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

        private void SetNearVisible(bool visible)
        {
            // The hidden chain is not evaluated. Clear its previous stroke bend once at the
            // visibility boundary so re-entry starts from the authored brush rest shape.
            if (_nearVisible && !visible) _nearBrush?.Bristles?.ResetDeformation();
            if(!visible)_nearHasSolution=false;
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
            _drawWeight = 0f; _harvestWeight = 0f; _recoveryRemaining = 0f;
            _bodyPoint = Vector2.zero; _hasPointerHistory = false; _wasStroking = false;
            _worldBrush?.Bristles?.ResetDeformation(); _nearBrush?.Bristles?.ResetDeformation();
        }

        private void OnDisable()
        {
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
