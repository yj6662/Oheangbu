using System;
using Oheangbu.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.Presentation
{
    public struct PlayerVisualFrame
    {
        public Vector3 LocalVelocity;
        public bool Grounded, Combat, Drawing, Stroking, Dodging, WorldBodyVisible, HasPointer;
        public Vector2 PointerScreen, DodgeDirection;
        public float DodgeProgress;
        public Camera Camera;
        public Vector3 InkWorldPoint;
    }

    public struct PlayerVisualDiagnostics
    {
        public bool WorldHumanoid, NearHumanoid, TipReachable, NearVisible;
        public float TipScreenErrorPixels, GripErrorMeters, BrushLengthMeters, DrawingWeight;
        public bool FlexibleBristles;
        public float BristleArcLengthMeters, BristleBendDegrees;
        public Vector3 BrushTipWorld, WristTargetWorld;
    }

    public struct PlayerFootDiagnostics
    {
        public bool Locked;
        public float SourceSoleGap, TargetReachRatio;
        public float PelvisLowering;
        public Vector3 AnchorWorld, ContactWorld;
    }

    /// <summary>Animator 평가 뒤 표시 골격만 움직인다. 입력·카메라·이동·판정에는 쓰지 않는다.</summary>
    public sealed class PlayerVisualRig : MonoBehaviour
    {
        [SerializeField] private Animator _worldAnimator;
        [SerializeField] private Animator _nearAnimator;
        [SerializeField] private Transform _worldBrush;
        [SerializeField] private Transform _nearBrush;
        [SerializeField] private PlayerVisualProfileSO _visual;
        [SerializeField] private DrawingPoseProfileSO _drawing;
        [SerializeField] private Transform _effectTip;
        [SerializeField] private Camera _viewCamera;
        [SerializeField] private PlayerSecondaryMotionRig _worldSecondaryMotion;
        [SerializeField] private PlayerSecondaryMotionRig _nearSecondaryMotion;
        private PlayerLodClothController _worldLod;
        public bool CullHiddenNearAnimation = true;
        public int LateNearPoseRefreshCount { get; private set; }

        private sealed class FootContact
        {
            public Vector3 LastLocal, LockedPosition, SoleLocal, GroundPoint, GroundNormal, LocalHeading;
            public float SoleHeight;
            public float SourceSoleGap, TargetReachRatio;
            public Quaternion LockedRotation;
            public bool HasLast, Locked, HasGround;
            public SurfaceSample[] Samples;
            public int ContactIndex = -1;
        }

        private struct SurfaceSample
        {
            public Transform B0, B1, B2, B3;
            public Vector3 P0, P1, P2, P3;
            public BoneWeight Weights;
            public Vector3 Position => (B0 != null ? B0.TransformPoint(P0) * Weights.weight0 : Vector3.zero)
                + (B1 != null ? B1.TransformPoint(P1) * Weights.weight1 : Vector3.zero)
                + (B2 != null ? B2.TransformPoint(P2) * Weights.weight2 : Vector3.zero)
                + (B3 != null ? B3.TransformPoint(P3) * Weights.weight3 : Vector3.zero);
        }

        private sealed class Bones
        {
            public Animator Animator;
            public Transform Root, Hips, Chest, Spine, Shoulder, Upper, Forearm, Hand, Grip;
            public Transform LeftUpper, LeftForearm, LeftHand;
            public Transform LeftThigh, LeftShin, LeftFoot, RightThigh, RightShin, RightFoot;
            public Renderer[] Renderers;
            public PlayerModelRenderBatch RenderBatch;
            public PlayerHandGripCorrectives HandCorrectives;
            public PlayerArmRollSolver RollSolver;
            public readonly Transform[] Fingers = new Transform[15];
            public readonly Quaternion[] FingerRest = new Quaternion[15];
            public Vector3 GripOffset;
            public Quaternion GripRotation;
            public Vector3 HandForwardLocal = Vector3.up;
            public float PelvisLowering;
            public readonly FootContact LeftContact = new FootContact();
            public readonly FootContact RightContact = new FootContact();
            public bool Valid => Upper != null && Forearm != null && Hand != null;
        }

        private sealed class Brush
        {
            public Transform Root, Grip, Tip;
            public Vector3 AxisInGrip;
            public float Length;
            public Renderer[] Renderers;
            public BrushBristleRig Bristles;
            public BrushBristlePose DeformedPose;
        }

        private static readonly int MoveXId = Animator.StringToHash("MoveX");
        private static readonly int MoveYId = Animator.StringToHash("MoveY");
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int LocomotionPlaybackRateId = Animator.StringToHash("LocomotionPlaybackRate");
        private static readonly int CombatId = Animator.StringToHash("Combat");
        private static readonly int DrawingId = Animator.StringToHash("Drawing");
        private static readonly int DrawPlaybackRateId = Animator.StringToHash("DrawPlaybackRate");
        private static readonly int DodgeId = Animator.StringToHash("Dodge");
        private static readonly int DodgeXId = Animator.StringToHash("DodgeX");
        private static readonly int DodgeYId = Animator.StringToHash("DodgeY");
        private static readonly int DodgeProgressId = Animator.StringToHash("DodgeProgress");
        private Bones _world, _near;
        private Brush _worldProp, _nearProp;
        private float _drawWeight;
        private Vector2 _bodyPoint;
        private Vector3 _smoothedVelocity;
        private Vector2 _strokeMin, _strokeMax;
        private float _strokeEngagement;
        private bool _wasStroking;
        private Vector2 _lastVisualPointer;
        private bool _bound;
        private readonly RaycastHit[] _groundHits = new RaycastHit[16];
        public PlayerVisualDiagnostics Diagnostics { get; private set; }
        public PlayerFootDiagnostics FootDiagnostics(bool left)
        {
            var state = _world == null ? null : left ? _world.LeftContact : _world.RightContact;
            return state == null ? default : new PlayerFootDiagnostics { Locked = state.Locked,
                SourceSoleGap = state.SourceSoleGap, TargetReachRatio = state.TargetReachRatio,
                PelvisLowering = _world.PelvisLowering, AnchorWorld = state.LockedPosition,
                ContactWorld = state.ContactIndex >= 0 ? state.Samples[state.ContactIndex].Position : Vector3.zero };
        }
        public bool IsArmRollBound(bool near)
        {
            var bones = near ? _near : _world;
            return bones != null && bones.RollSolver != null;
        }
        public PlayerArmRollSolver.Result ArmRollDiagnostics(bool near)
        {
            var bones = near ? _near : _world;
            return bones != null && bones.RollSolver != null ? bones.RollSolver.Diagnostics : default;
        }
        public Transform EffectTip => _effectTip;
        public void ConfigureViewCamera(Camera camera) { _viewCamera = camera; }

        public void Configure(Animator worldAnimator, Animator nearAnimator, Transform worldBrush,
            Transform nearBrush, PlayerVisualProfileSO visual, DrawingPoseProfileSO drawing)
        {
            _worldAnimator = worldAnimator; _nearAnimator = nearAnimator;
            _worldBrush = worldBrush; _nearBrush = nearBrush; _visual = visual; _drawing = drawing;
            Bind();
        }

        private void Awake() { Bind(); }
        private void OnEnable() { ResetArmRollHistory(); }
        private void OnDisable() { ResetArmRollHistory(); }

        private void ResetArmRollHistory()
        {
            _world?.RollSolver?.Reset();
            _near?.RollSolver?.Reset();
        }

        private void Bind()
        {
            ResetArmRollHistory();
            _bound = false;
            if (_visual == null || _drawing == null) return;
            _world = BindBones(_worldAnimator);
            _worldLod = _worldAnimator != null ? _worldAnimator.GetComponent<PlayerLodClothController>() : null;
            _near = BindBones(_nearAnimator);
            _worldProp = BindBrush(_worldBrush);
            _nearProp = BindBrush(_nearBrush);
            if (_worldSecondaryMotion == null && _worldAnimator != null)
                _worldSecondaryMotion = _worldAnimator.GetComponentInChildren<PlayerSecondaryMotionRig>(true);
            if (_nearSecondaryMotion == null && _nearAnimator != null)
                _nearSecondaryMotion = _nearAnimator.GetComponentInChildren<PlayerSecondaryMotionRig>(true);
            if (_effectTip == null)
            {
                _effectTip = new GameObject("EffectTip").transform;
                _effectTip.SetParent(transform, false);
            }
            if (_near != null)
                foreach (var r in _near.Renderers) r.shadowCastingMode = ShadowCastingMode.Off;
            _bound = _world != null && _world.Valid;
        }

        private Bones BindBones(Animator animator)
        {
            if (animator == null) return null;
            if (_visual.Controller != null) animator.runtimeAnimatorController = _visual.Controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // 이동 클립은 게임 시간, 작도 레이어는 별도 속도 계수, IK는 비스케일 시간으로 갱신한다.
            animator.updateMode = AnimatorUpdateMode.Normal;
            var b = new Bones { Animator = animator, Root = animator.transform,
                Renderers = animator.GetComponentsInChildren<Renderer>(true), RenderBatch = animator.GetComponent<PlayerModelRenderBatch>() };
            b.HandCorrectives = new PlayerHandGripCorrectives(b.Renderers);
            b.Hips = Bone(animator, HumanBodyBones.Hips, "Hips");
            b.Spine = Bone(animator, HumanBodyBones.Spine, "Spine");
            b.Chest = Bone(animator, HumanBodyBones.Chest, "Spine01");
            b.Shoulder = Bone(animator, HumanBodyBones.RightShoulder, "RightShoulder");
            b.Upper = Bone(animator, HumanBodyBones.RightUpperArm, "RightArm");
            b.Forearm = Bone(animator, HumanBodyBones.RightLowerArm, "RightForeArm");
            b.Hand = Bone(animator, HumanBodyBones.RightHand, "RightHand");
            b.LeftUpper = Bone(animator, HumanBodyBones.LeftUpperArm, "LeftArm");
            b.LeftForearm = Bone(animator, HumanBodyBones.LeftLowerArm, "LeftForeArm");
            b.LeftHand = Bone(animator, HumanBodyBones.LeftHand, "LeftHand");
            b.LeftThigh = Bone(animator, HumanBodyBones.LeftUpperLeg, "LeftUpLeg");
            b.LeftShin = Bone(animator, HumanBodyBones.LeftLowerLeg, "LeftLeg");
            b.LeftFoot = Bone(animator, HumanBodyBones.LeftFoot, "LeftFoot");
            b.RightThigh = Bone(animator, HumanBodyBones.RightUpperLeg, "RightUpLeg");
            b.RightShin = Bone(animator, HumanBodyBones.RightLowerLeg, "RightLeg");
            b.RightFoot = Bone(animator, HumanBodyBones.RightFoot, "RightFoot");
            BindFoot(b, b.LeftFoot, b.LeftContact);
            BindFoot(b, b.RightFoot, b.RightContact);
            b.Grip = Find(animator.transform, "RightBrushGrip");
            if (b.Hand != null)
            {
                b.GripOffset = b.Grip != null ? b.Hand.InverseTransformPoint(b.Grip.position) : _drawing.FallbackHandGripOffset;
                b.GripRotation = b.Grip != null ? Quaternion.Inverse(b.Hand.rotation) * b.Grip.rotation
                    : Quaternion.Euler(_drawing.FallbackHandGripEuler);
                if (_drawing.OverrideHandGrip)
                {
                    b.GripOffset = _drawing.HandGripLocalPosition;
                    b.GripRotation = _drawing.HandGripLocalRotation.normalized;
                }
                Transform middle = Find(animator.transform, "RightHandMiddle1");
                if (middle != null) b.HandForwardLocal = b.Hand.InverseTransformDirection(middle.position - b.Hand.position).normalized;
            }
            string[] names = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
            for (int f = 0; f < 5; f++)
                for (int j = 0; j < 3; j++)
                {
                    int i = f * 3 + j;
                    b.Fingers[i] = Find(animator.transform, "RightHand" + names[f] + (j + 1));
                    if (b.Fingers[i] != null)
                    {
                        b.FingerRest[i] = b.Fingers[i].localRotation;
                        // Calibrated offsets are authored relative to mesh bind rest, never an Animator's first sampled pose.
                        if (_drawing.CalibratedRightFingerOffsets != null && _drawing.CalibratedRightFingerOffsets.Length == 15
                            && TryBindLocalRotation(b.Fingers[i], b.Renderers, out Quaternion rest))
                            b.FingerRest[i] = rest;
                    }
                }
            if (_drawing.EnableArmRollRedistribution)
            {
                var skins = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (!PlayerArmRollSolver.TryBind(b.Upper, b.Forearm, b.Hand, skins, out b.RollSolver))
                    Debug.LogWarning("V2 arm roll requested but bind-pose chain is unavailable: " + animator.name, animator);
            }
            return b;
        }

        private static bool TryBindLocalRotation(Transform bone, Renderer[] renderers, out Quaternion rotation)
        {
            foreach (var renderer in renderers)
            {
                if (!(renderer is SkinnedMeshRenderer skin) || skin.sharedMesh == null) continue;
                var bones = skin.bones; var bindposes = skin.sharedMesh.bindposes;
                if (bones.Length != bindposes.Length) continue;
                int child = System.Array.IndexOf(bones, bone), parent = System.Array.IndexOf(bones, bone.parent);
                if (child < 0 || parent < 0) continue;
                rotation = (bindposes[parent] * bindposes[child].inverse).rotation;
                return true;
            }
            rotation = Quaternion.identity;
            return false;
        }

        private void BindFoot(Bones b, Transform foot, FootContact contact)
        {
            if (foot == null) return;
            contact.LocalHeading = foot.InverseTransformDirection(b.Root.forward);
            contact.SoleHeight = Mathf.Max(0f, Vector3.Dot(foot.position - b.Root.position, b.Root.up)) + _visual.FootSoleOffset;
            contact.Samples = BuildSoleSamples(b, foot, out contact.SoleLocal);
        }

        private static SurfaceSample[] BuildSoleSamples(Bones body, Transform foot, out Vector3 soleLocal)
        {
            soleLocal = Vector3.zero;
            // 발바닥 4x6 구역의 최저 정점을 한 번만 뽑는다. 실행 중에는 소수의 본 변환만 계산한다.
            foreach (var renderer in body.Renderers)
            {
                if (!(renderer is SkinnedMeshRenderer skin) || skin.sharedMesh == null || !skin.sharedMesh.isReadable) continue;
                var mesh = skin.sharedMesh; var weights = mesh.boneWeights; var vertices = mesh.vertices;
                var bones = skin.bones; var bind = mesh.bindposes;
                if (weights.Length != vertices.Length) continue;
                bool[] belongs = new bool[bones.Length];
                for (int i = 0; i < bones.Length; i++) belongs[i] = bones[i] == foot || bones[i].IsChildOf(foot);
                var candidates = new System.Collections.Generic.List<int>();
                Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var w = weights[i];
                    float weight = (belongs[w.boneIndex0] ? w.weight0 : 0f) + (belongs[w.boneIndex1] ? w.weight1 : 0f)
                        + (belongs[w.boneIndex2] ? w.weight2 : 0f) + (belongs[w.boneIndex3] ? w.weight3 : 0f);
                    if (weight < .35f) continue;
                    candidates.Add(i);
                    Vector3 p = body.Root.InverseTransformPoint(skin.transform.TransformPoint(vertices[i]));
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                }
                if (candidates.Count == 0) continue;
                Vector3 soleSum = Vector3.zero; int soleCount = 0;
                foreach (int i in candidates)
                {
                    Vector3 world = skin.transform.TransformPoint(vertices[i]);
                    if (body.Root.InverseTransformPoint(world).y > min.y + .02f) continue;
                    soleSum += world; soleCount++;
                }
                if (soleCount > 0) soleLocal = foot.InverseTransformPoint(soleSum / soleCount);
                int[] chosen = new int[24]; float[] lowest = new float[24];
                for (int i = 0; i < chosen.Length; i++) { chosen[i] = -1; lowest[i] = float.MaxValue; }
                foreach (int i in candidates)
                {
                    Vector3 p = body.Root.InverseTransformPoint(skin.transform.TransformPoint(vertices[i]));
                    if (p.y > min.y + .07f) continue;
                    int x = Mathf.Clamp((int)(4f * (p.x - min.x) / Mathf.Max(.001f, max.x - min.x)), 0, 3);
                    int z = Mathf.Clamp((int)(6f * (p.z - min.z) / Mathf.Max(.001f, max.z - min.z)), 0, 5);
                    int cell = z * 4 + x;
                    if (p.y >= lowest[cell]) continue;
                    lowest[cell] = p.y; chosen[cell] = i;
                }
                var samples = new System.Collections.Generic.List<SurfaceSample>(24);
                foreach (int i in chosen)
                {
                    if (i < 0) continue;
                    var w = weights[i]; Vector3 v = vertices[i];
                    samples.Add(new SurfaceSample { Weights = w,
                        B0 = bones[w.boneIndex0], B1 = bones[w.boneIndex1], B2 = bones[w.boneIndex2], B3 = bones[w.boneIndex3],
                        P0 = bind[w.boneIndex0].MultiplyPoint3x4(v), P1 = bind[w.boneIndex1].MultiplyPoint3x4(v),
                        P2 = bind[w.boneIndex2].MultiplyPoint3x4(v), P3 = bind[w.boneIndex3].MultiplyPoint3x4(v) });
                }
                return samples.ToArray();
            }
            return Array.Empty<SurfaceSample>();
        }

        private static float LowestSurface(FootContact foot, Vector3 planePoint, Vector3 normal, out int index)
        {
            float minimum = float.MaxValue; index = -1;
            if (foot.Samples == null) return minimum;
            for (int i = 0; i < foot.Samples.Length; i++)
            {
                float distance = Vector3.Dot(foot.Samples[i].Position - planePoint, normal);
                if (distance >= minimum) continue;
                minimum = distance; index = i;
            }
            return minimum;
        }

        private Brush BindBrush(Transform root)
        {
            if (root == null) return null;
            Transform grip = Find(root, "GripSocket") ?? root;
            Transform tip = Find(root, "TipSocket");
            Vector3 delta = tip != null ? tip.position - grip.position : grip.up * _drawing.BrushLength;
            return new Brush { Root = root, Grip = grip, Tip = tip, Length = delta.magnitude,
                AxisInGrip = Quaternion.Inverse(grip.rotation) * delta.normalized,
                Bristles = root.GetComponent<BrushBristleRig>(),
                Renderers = root.GetComponentsInChildren<Renderer>(true) };
        }

        private static Transform Bone(Animator a, HumanBodyBones bone, string fallback)
        {
            Transform t = a.isHuman && a.avatar != null && a.avatar.isValid ? a.GetBoneTransform(bone) : null;
            return t != null ? t : Find(a.transform, fallback);
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name || root.name.EndsWith(":" + name, StringComparison.Ordinal)) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = Find(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        public void UpdateMotion(PlayerVisualFrame frame)
        {
            UpdateMotion(frame, Time.unscaledDeltaTime);
        }

        public void UpdateMotion(PlayerVisualFrame frame, float unscaledDeltaTime)
        {
            if (!_bound) return;
            float k = 1f - Mathf.Exp(-_visual.MotionDamping * Mathf.Max(0f, unscaledDeltaTime));
            _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, frame.LocalVelocity, k);
            SetAnimator(_worldAnimator, frame);
            SetNearAnimationVisibility(!frame.WorldBodyVisible || frame.Drawing, false);
            SetAnimator(_nearAnimator, frame);
        }

        private void SetNearAnimationVisibility(bool visible, bool finalCamera)
        {
            if (_nearAnimator == null) return;
            bool wasCulled = _nearAnimator.cullingMode != AnimatorCullingMode.AlwaysAnimate;
            // Keep the controller clock/transitions running. Only skip Humanoid retargeting
            // and bone writes for arms that are hidden; the world body always animates.
            _nearAnimator.cullingMode = CullHiddenNearAnimation && !visible
                ? AnimatorCullingMode.CullUpdateTransforms : AnimatorCullingMode.AlwaysAnimate;
            // Camera contraction can reveal the arms in LateUpdate, after normal Animator
            // evaluation. Evaluate this pose now without advancing animation/game time.
            if (visible && finalCamera && wasCulled && _nearAnimator.isActiveAndEnabled)
            {
                _nearAnimator.Update(0f);
                LateNearPoseRefreshCount++;
            }
        }

        private void SetAnimator(Animator animator, PlayerVisualFrame frame)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            // 빈 Override 상태도 이전 상체 포즈를 남길 수 있어 실제 작도 가중치로 레이어를 끈다.
            if (animator.layerCount > 1) animator.SetLayerWeight(1, _drawWeight);
            animator.SetFloat(MoveXId, _smoothedVelocity.x);
            animator.SetFloat(MoveYId, _smoothedVelocity.z);
            animator.SetFloat(SpeedId, new Vector2(_smoothedVelocity.x, _smoothedVelocity.z).magnitude);
            float actualPlanarSpeed = new Vector2(frame.LocalVelocity.x, frame.LocalVelocity.z).magnitude;
            float directionCompensation = actualPlanarSpeed > .01f
                ? (Mathf.Abs(frame.LocalVelocity.x) + Mathf.Abs(frame.LocalVelocity.z)) / actualPlanarSpeed : 1f;
            animator.SetFloat(LocomotionPlaybackRateId, directionCompensation
                * Mathf.Max(1f, actualPlanarSpeed / Mathf.Max(.1f, _visual.RunClipSpeed)));
            animator.SetBool(CombatId, frame.Combat || frame.Drawing);
            animator.SetBool(DrawingId, frame.Drawing);
            animator.SetFloat(DrawPlaybackRateId, 1f / Mathf.Max(Time.timeScale, 0.01f));
            animator.SetBool(DodgeId, frame.Dodging);
            animator.SetFloat(DodgeXId, frame.DodgeDirection.x);
            animator.SetFloat(DodgeYId, frame.DodgeDirection.y);
            animator.SetFloat(DodgeProgressId, frame.DodgeProgress);
        }

        public void ApplyFrame(PlayerVisualFrame frame)
        {
            ApplyFrame(frame, Time.unscaledDeltaTime, Time.deltaTime);
        }

        public void ApplyFrame(PlayerVisualFrame frame, float unscaledDeltaTime)
        {
            ApplyFrame(frame, unscaledDeltaTime, unscaledDeltaTime * Time.timeScale);
        }

        public void ApplyFrame(PlayerVisualFrame frame, float unscaledDeltaTime, float scaledDeltaTime)
        {
            if (!_bound || frame.Camera == null) return;
            float dt = Mathf.Clamp(unscaledDeltaTime, 0f, 0.1f);
            _drawWeight = Mathf.MoveTowards(_drawWeight, frame.Drawing ? 1f : 0f,
                (frame.Drawing ? _drawing.EnterSpeed : _drawing.ExitSpeed) * dt);
            Vector3 viewport = frame.Camera.ScreenToViewportPoint(frame.PointerScreen);
            Vector2 normalized = frame.HasPointer ? new Vector2(viewport.x * 2f - 1f, viewport.y * 2f - 1f) : Vector2.zero;
            normalized.x = Mathf.Clamp(normalized.x, -1f, 1f);
            normalized.y = Mathf.Clamp(normalized.y, -1f, 1f);
            _bodyPoint = Vector2.Lerp(_bodyPoint, normalized, 1f - Mathf.Exp(-_drawing.BodyFollowSpeed * dt));
            if (frame.Stroking && !_wasStroking) _strokeMin = _strokeMax = normalized;
            if (frame.Stroking)
            {
                _strokeMin = Vector2.Min(_strokeMin, normalized);
                _strokeMax = Vector2.Max(_strokeMax, normalized);
            }
            float span = Mathf.Max(_strokeMax.x - _strokeMin.x, _strokeMax.y - _strokeMin.y);
            float engagement = frame.Stroking ? Mathf.Lerp(_drawing.SmallStrokeBodyWeight, 1f,
                Mathf.Clamp01(span / _drawing.FullBodyStrokeSpan)) : _drawing.SmallStrokeBodyWeight;
            _strokeEngagement = Mathf.Lerp(_strokeEngagement, engagement, 1f - Mathf.Exp(-_drawing.BodyFollowSpeed * dt));
            Vector2 visualVelocity = frame.Stroking && _wasStroking && dt > 0.000001f
                ? (normalized - _lastVisualPointer) / dt : Vector2.zero;
            _lastVisualPointer = normalized;
            _wasStroking = frame.Stroking;

            bool worldVisible = frame.WorldBodyVisible && !frame.Drawing;
            if (_worldLod != null && _worldLod.ViewCamera != frame.Camera) _worldLod.SetViewCamera(frame.Camera);
            bool nearVisible = !worldVisible && _near != null && _near.Valid;
            SetNearAnimationVisibility(nearVisible, true);
            if (!nearVisible) _near?.RollSolver?.Reset();
            if (_viewCamera != null)
            {
                _viewCamera.transform.SetPositionAndRotation(frame.Camera.transform.position, frame.Camera.transform.rotation);
                _viewCamera.orthographic = frame.Camera.orthographic;
                _viewCamera.orthographicSize = frame.Camera.orthographicSize;
                _viewCamera.fieldOfView = frame.Camera.fieldOfView;
                _viewCamera.aspect = frame.Camera.aspect;
                _viewCamera.rect = frame.Camera.rect;
                _viewCamera.nearClipPlane = _visual.NearClipPlane;
                _viewCamera.farClipPlane = frame.Camera.farClipPlane;
                _viewCamera.enabled = nearVisible;
            }
            SetVisibility(_world, worldVisible, false);
            SetVisibility(_near, nearVisible, true);
            if (_worldProp != null) SetBrushVisibility(_worldProp, worldVisible, true);
            if (_nearProp != null) SetBrushVisibility(_nearProp, nearVisible, false);

            ApplyBody(_world, _bodyPoint, _drawWeight * _strokeEngagement);
            if (frame.Grounded && !frame.Dodging) ApplyFeet(_world, frame.LocalVelocity, scaledDeltaTime);
            else { _world.LeftContact.Locked = false; _world.RightContact.Locked = false; }
            if (_visual.RelaxedBrushCarry && !frame.Dodging)
                PoseCarryArm(_world, 1f - _drawWeight);
            Vector3 worldPoint = _world.Root.position + _world.Root.up * _drawing.WorldPlaneHeight
                + _world.Root.forward * _drawing.WorldPlaneDistance
                + _world.Root.right * (_bodyPoint.x * _drawing.WorldPlaneHalfSize.x)
                + _world.Root.up * (_bodyPoint.y * _drawing.WorldPlaneHalfSize.y);
            PrepareBristles(_world, _worldProp, worldPoint, _world.Root,
                new Vector3(visualVelocity.x * _drawing.WorldPlaneHalfSize.x,
                    visualVelocity.y * _drawing.WorldPlaneHalfSize.y, 0f), frame.Stroking, dt);
            if (_drawWeight > 0.001f) PoseBrushArm(_world, _worldProp, worldPoint, _world.Root, _drawWeight, false, dt, out _);
            else
            {
                _world?.RollSolver?.Reset();
                PlaceBrushOnHand(_world, _worldProp);
            }
            ApplyGrip(_world, frame.Drawing && !frame.Stroking);
            _worldProp?.Bristles?.ApplyPose();

            var diagnostics = new PlayerVisualDiagnostics { WorldHumanoid = _worldAnimator.isHuman,
                NearHumanoid = _nearAnimator != null && _nearAnimator.isHuman, DrawingWeight = _drawWeight,
                NearVisible = nearVisible, TipScreenErrorPixels = -1f };
            if (nearVisible)
            {
                _near.Root.SetPositionAndRotation(frame.Camera.transform.TransformPoint(_visual.NearRootCameraOffset), frame.Camera.transform.rotation);
                ApplyBody(_near, _bodyPoint, _drawWeight * _strokeEngagement * 0.3f);
                Vector2 screen = frame.Drawing && frame.HasPointer ? frame.PointerScreen
                    : (Vector2)frame.Camera.ViewportToScreenPoint(_visual.RestTipViewport);
                Ray ray = frame.Camera.ScreenPointToRay(screen);
                if (!frame.Camera.orthographic) ray = new Ray(frame.Camera.transform.position, ray.direction);
                float a = Vector3.Distance(_near.Upper.position, _near.Forearm.position);
                float b = Vector3.Distance(_near.Forearm.position, _near.Hand.position);
                float visualDepth = frame.Drawing ? _drawing.PreferredTipDepth : _visual.RestTipDepth;
                float halfHeight = frame.Camera.orthographic ? frame.Camera.orthographicSize
                    : Mathf.Tan(frame.Camera.fieldOfView * Mathf.Deg2Rad * .5f) * visualDepth;
                PrepareBristles(_near, _nearProp, ray.GetPoint(visualDepth), frame.Camera.transform,
                    new Vector3(visualVelocity.x * halfHeight * frame.Camera.aspect,
                        visualVelocity.y * halfHeight, 0f), frame.Stroking, dt);
                float length = _nearProp != null ? _nearProp.Length : _drawing.BrushLength;
                float gripMargin = Vector3.Scale(_near.GripOffset, _near.Hand.lossyScale).magnitude;
                bool reachable = PlayerVisualIK.FindTip(ray, _near.Upper.position,
                    (a + b) * _drawing.MaxArmExtension - gripMargin, length,
                    Mathf.Max(_drawing.MinTipDepth, _visual.NearClipPlane + 0.04f), _drawing.MaxTipDepth,
                    frame.Drawing ? _drawing.PreferredTipDepth - (frame.Stroking ? 0f : _drawing.PenLiftDistance)
                        : _visual.RestTipDepth, out Vector3 tip);
                PoseBrushArm(_near, _nearProp, tip, frame.Camera.transform, 1f, true, dt, out Vector3 wrist);
                ApplyGrip(_near, frame.Drawing && !frame.Stroking);
                _nearProp?.Bristles?.ApplyPose();
                Vector3 actual = BrushTip(_nearProp);
                Vector3 projected = frame.Camera.WorldToScreenPoint(actual);
                diagnostics.TipReachable = reachable;
                diagnostics.BrushTipWorld = actual;
                diagnostics.WristTargetWorld = wrist;
                diagnostics.TipScreenErrorPixels = Vector2.Distance(projected, screen);
                diagnostics.BrushLengthMeters = _nearProp != null ? _nearProp.Length : 0f;
                diagnostics.GripErrorMeters = _nearProp != null ? Vector3.Distance(_nearProp.Grip.position,
                    _near.Hand.TransformPoint(_near.GripOffset)) : 0f;
            }
            // Both systems own only auxiliary transforms/cloth, after the final body/arm pose.
            _worldSecondaryMotion?.Evaluate(scaledDeltaTime, Vector3.zero);
            if (_nearSecondaryMotion != null)
            {
                _nearSecondaryMotion.SetCameraReference(frame.Camera);
                _nearSecondaryMotion.SetRepresentationActive(nearVisible);
                if (nearVisible) _nearSecondaryMotion.Evaluate(scaledDeltaTime, Vector3.zero);
            }
            Brush activeProp = nearVisible ? _nearProp : _worldProp;
            if (activeProp != null && activeProp.DeformedPose.Valid)
            {
                diagnostics.FlexibleBristles = true;
                diagnostics.BristleArcLengthMeters = activeProp.DeformedPose.BristleArcLength;
                diagnostics.BristleBendDegrees = activeProp.DeformedPose.BendDegrees;
            }
            if (_effectTip != null && activeProp != null)
                _effectTip.SetPositionAndRotation(BrushTip(activeProp),
                    activeProp.DeformedPose.Valid && activeProp.Tip != null ? activeProp.Tip.rotation : activeProp.Grip.rotation);
            Diagnostics = diagnostics;
        }

        private void PrepareBristles(Bones body, Brush prop, Vector3 approximateTip, Transform basis,
            Vector3 visualVelocity, bool stroking, float deltaTime)
        {
            if (prop == null || prop.Bristles == null || !prop.Bristles.IsBound) return;
            Vector3 axis = (approximateTip - body.Upper.position).normalized;
            if (axis.sqrMagnitude < .0001f) axis = basis.forward;
            Vector3 dorsal = Vector3.ProjectOnPlane(-basis.up, axis).normalized;
            if (dorsal.sqrMagnitude < .0001f) dorsal = basis.forward;
            Quaternion handGrip = Quaternion.AngleAxis(_drawing.BrushRollDegrees, axis)
                * Quaternion.LookRotation(dorsal, axis);
            Quaternion brushGrip = handGrip * Quaternion.FromToRotation(prop.AxisInGrip, Vector3.up);
            prop.DeformedPose = prop.Bristles.EvaluateInFrame(visualVelocity, basis.rotation,
                brushGrip, stroking, deltaTime);
            if (prop.DeformedPose.Valid) prop.Length = prop.DeformedPose.GripToTipDistance;
        }

        private void PoseCarryArm(Bones body, float weight)
        {
            if (body == null || !body.Valid || weight <= .001f) return;
            Transform basis = body.Root;
            float swing = Mathf.Clamp(Vector3.Dot(body.Hand.position - body.Upper.position, basis.forward)
                * _visual.CarryGaitInfluence, -_visual.CarryMaximumSwing, _visual.CarryMaximumSwing);
            Vector3 wrist = body.Upper.position + basis.TransformDirection(_visual.CarryWristOffset)
                + basis.forward * swing;
            Vector3 axis = basis.TransformDirection(_visual.CarryBrushDirection).normalized;
            if (axis.sqrMagnitude < .001f) return;
            Vector3 dorsal = Vector3.ProjectOnPlane(-basis.up, axis).normalized;
            if (dorsal.sqrMagnitude < .001f) dorsal = basis.forward;
            Quaternion grip = Quaternion.AngleAxis(_visual.CarryBrushRoll, axis) * Quaternion.LookRotation(dorsal, axis);
            PlayerVisualIK.Solve(body.Upper, body.Forearm, body.Hand, wrist,
                body.Upper.position + basis.TransformDirection(_visual.CarryElbowPole),
                grip * Quaternion.Inverse(body.GripRotation), weight, .985f);
        }

        private void ApplyBody(Bones b, Vector2 point, float weight)
        {
            if (b == null || weight <= 0f) return;
            if (b.Hips != null)
            {
                b.Hips.rotation = Quaternion.AngleAxis(point.x * _drawing.PelvisYaw * weight, b.Root.up) * b.Hips.rotation;
                b.Hips.position += b.Root.right * (point.x * _drawing.PelvisSway * weight);
            }
            if (b.Chest != null)
                b.Chest.rotation = Quaternion.AngleAxis(point.x * _drawing.ChestYaw * weight, b.Root.up)
                    * Quaternion.AngleAxis(-point.y * _drawing.ChestLean * weight, b.Root.right) * b.Chest.rotation;
            if (b.Shoulder != null)
                b.Shoulder.rotation = Quaternion.AngleAxis(-point.y * _drawing.ShoulderLift * weight, b.Root.forward)
                    * Quaternion.AngleAxis(point.x * _drawing.ShoulderYaw * weight, b.Root.up) * b.Shoulder.rotation;
            if (b.LeftHand != null)
            {
                Vector3 target = b.LeftHand.position - b.Root.right * (point.x * _drawing.OffHandCounter * weight)
                    + b.Root.up * (point.y * _drawing.OffHandCounter * 0.4f * weight);
                Vector3 pole = b.LeftUpper.position - b.Root.right * _drawing.ElbowOut - b.Root.up * _drawing.ElbowDown;
                PlayerVisualIK.Solve(b.LeftUpper, b.LeftForearm, b.LeftHand, target, pole, b.LeftHand.rotation, weight);
            }
        }

        private void PoseBrushArm(Bones b, Brush prop, Vector3 tip, Transform basis, float weight,
            bool exactTip, float unscaledDeltaTime, out Vector3 wristGoal)
        {
            Vector3 axis = (tip - b.Upper.position).normalized;
            if (axis.sqrMagnitude < 0.0001f) axis = basis.forward;
            Vector3 dorsal = Vector3.ProjectOnPlane(-basis.up, axis).normalized;
            if (dorsal.sqrMagnitude < 0.0001f) dorsal = basis.forward;
            Quaternion gripRotation = Quaternion.AngleAxis(_drawing.BrushRollDegrees, axis) * Quaternion.LookRotation(dorsal, axis);
            if (prop != null && prop.DeformedPose.Valid)
            {
                // The hand and rigid handle rotate together. Align the already deformed
                // grip-to-tip vector without stretching the shaft, hair, or arm bones.
                Vector3 tipInHandGrip = Quaternion.FromToRotation(prop.AxisInGrip, Vector3.up)
                    * prop.DeformedPose.TipOffsetInGripLocal;
                gripRotation *= Quaternion.FromToRotation(tipInHandGrip.normalized, Vector3.up);
            }
            Quaternion handRotation = gripRotation * Quaternion.Inverse(b.GripRotation);
            float length = prop != null ? prop.Length : _drawing.BrushLength;
            Vector3 gripPosition = tip - axis * length;
            wristGoal = gripPosition - handRotation * Vector3.Scale(b.GripOffset, b.Hand.lossyScale);
            Vector3 pole = Vector3.Lerp(b.Upper.position, wristGoal, 0.5f)
                + basis.right * _drawing.ElbowOut - basis.up * _drawing.ElbowDown;
            // 가로로 쥔 붓에 전완을 평행하게 강제하지 않는다. 손목 장축의 뒤쪽을 팔꿈치 후보로 삼는다.
            Vector3 alignedElbow = wristGoal - (handRotation * b.HandForwardLocal)
                * Vector3.Distance(b.Forearm.position, b.Hand.position);
            pole = Vector3.Lerp(pole, alignedElbow, _drawing.GripAlignedElbowWeight);
            PlayerVisualIK.Solve(b.Upper, b.Forearm, b.Hand, wristGoal, pole, handRotation, weight,
                exactTip ? 0.999f : _drawing.MaxArmExtension);
            if (_drawing.EnableArmRollRedistribution && b.RollSolver != null)
            {
                var roll = PlayerArmRollSolver.Settings.CandidateDefault;
                roll.Enabled = true;
                roll.MaximumRollDegreesPerSecond = _drawing.ArmRollDegreesPerSecond;
                roll.TrackingWindowDegrees = _drawing.ArmRollTrackingWindowDegrees;
                b.RollSolver.Apply(roll, weight, unscaledDeltaTime);
            }
            else b.RollSolver?.Reset();
            PlaceBrushOnHand(b, prop);
        }

        private void ApplyGrip(Bones b, bool penUp)
        {
            if (b == null) return;
            b.HandCorrectives?.Apply(_drawing.GripWeight, 0f);
            for (int i = 0; i < b.Fingers.Length; i++)
            {
                Transform finger = b.Fingers[i];
                if (finger == null) continue;
                if (_drawing.CalibratedRightFingerOffsets != null && _drawing.CalibratedRightFingerOffsets.Length == 15)
                {
                    Quaternion offset = _drawing.CalibratedRightFingerOffsets[i];
                    if (penUp && i >= 9 && !_drawing.KeepGripClosedOnPenLift) offset = Quaternion.Slerp(Quaternion.identity, offset, .9f);
                    finger.localRotation = Quaternion.Slerp(finger.localRotation, b.FingerRest[i] * offset, _drawing.GripWeight);
                    continue;
                }
                Vector3 angles = _drawing.CurlDegrees(i / 3);
                float angle = i % 3 == 0 ? angles.x : i % 3 == 1 ? angles.y : angles.z;
                if (penUp && i >= 9) angle *= 0.9f; // 파지하는 엄지·검지는 유지하고 보조 손가락만 가볍게 편다.
                Quaternion opposition = i == 0
                    ? Quaternion.AngleAxis(_drawing.ThumbOppositionDegrees.x, Vector3.up)
                        * Quaternion.AngleAxis(_drawing.ThumbOppositionDegrees.y, Vector3.forward)
                    : Quaternion.identity;
                Quaternion target = b.FingerRest[i] * opposition * Quaternion.AngleAxis(angle, _drawing.FingerCurlAxis);
                finger.localRotation = Quaternion.Slerp(finger.localRotation, target, _drawing.GripWeight);
            }
        }

        private static void PlaceBrushOnHand(Bones b, Brush prop)
        {
            if (b == null || b.Hand == null || prop == null) return;
            Quaternion handGrip = b.Hand.rotation * b.GripRotation;
            // 붓 축이 +Y가 아니어도 고정 소켓으로 보정한다. 배율은 변경하지 않는다.
            Quaternion desiredGrip = handGrip * Quaternion.FromToRotation(prop.AxisInGrip, Vector3.up);
            Quaternion rootToGrip = Quaternion.Inverse(prop.Root.rotation) * prop.Grip.rotation;
            prop.Root.rotation = desiredGrip * Quaternion.Inverse(rootToGrip);
            prop.Root.position += b.Hand.TransformPoint(b.GripOffset) - prop.Grip.position;
        }

        private static Vector3 BrushTip(Brush prop)
        {
            if (prop == null) return Vector3.zero;
            return prop.Tip != null ? prop.Tip.position : prop.Grip.position + prop.Grip.TransformDirection(prop.AxisInGrip) * prop.Length;
        }

        private void ApplyFeet(Bones b, Vector3 velocity, float deltaTime)
        {
            // 접지 앵커를 유지할 수 있을 때까지 시각 골반만 낮춘다. 이동 루트와 다리 길이는 유지한다.
            float lower = Mathf.Max(RequiredPelvisLowering(b.LeftThigh, b.LeftShin, b.LeftFoot, b.LeftContact),
                RequiredPelvisLowering(b.RightThigh, b.RightShin, b.RightFoot, b.RightContact));
            b.PelvisLowering = Mathf.Min(_visual.MaxPelvisLowering,
                Mathf.Max(lower, b.PelvisLowering * Mathf.Exp(-8f * Mathf.Max(0f, deltaTime))));
            if (b.Hips != null) b.Hips.position -= Vector3.up * b.PelvisLowering;
            FitFoot(b, b.LeftThigh, b.LeftShin, b.LeftFoot, b.LeftContact, velocity, deltaTime);
            FitFoot(b, b.RightThigh, b.RightShin, b.RightFoot, b.RightContact, velocity, deltaTime);
        }

        private float RequiredPelvisLowering(Transform thigh, Transform shin, Transform foot, FootContact state)
        {
            if (thigh == null || shin == null || foot == null) return 0f;
            Vector3 target;
            if (state.Locked && state.ContactIndex >= 0)
                target = state.LockedPosition - (FootRotation(foot, state, state.GroundNormal) * Quaternion.Inverse(foot.rotation))
                    * (state.Samples[state.ContactIndex].Position - foot.position);
            else
            {
                if (!state.HasGround) return 0f;
                float gap = LowestSurface(state, state.GroundPoint, state.GroundNormal, out int lowest);
                if (lowest < 0 || gap > _visual.FootContactHeight + .03f) return 0f;
                Vector3 anchor = state.Samples[lowest].Position + state.GroundNormal * (_visual.FootSoleOffset - gap);
                target = anchor - (FootRotation(foot, state, state.GroundNormal) * Quaternion.Inverse(foot.rotation))
                    * (state.Samples[lowest].Position - foot.position);
            }
            Vector3 delta = thigh.position - target;
            float length = (Vector3.Distance(thigh.position, shin.position) + Vector3.Distance(shin.position, foot.position)) * .98f;
            float horizontal = delta.x * delta.x + delta.z * delta.z;
            float vertical = Mathf.Sqrt(Mathf.Max(.01f, length * length - horizontal));
            return Mathf.Max(0f, delta.y - vertical);
        }

        private static Quaternion FootRotation(Transform foot, FootContact state, Vector3 normal)
        {
            // 평지에서는 원본 heel/toe 굴림을 유지하고, 지지 중 수평 방향만 고정한다.
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal) * foot.rotation;
            if (!state.Locked) return rotation;
            Vector3 from = Vector3.ProjectOnPlane(rotation * state.LocalHeading, normal);
            Vector3 to = Vector3.ProjectOnPlane(state.LockedRotation * state.LocalHeading, normal);
            return from.sqrMagnitude > .0001f && to.sqrMagnitude > .0001f
                ? Quaternion.FromToRotation(from, to) * rotation : rotation;
        }

        private void FitFoot(Bones b, Transform thigh, Transform shin, Transform foot, FootContact state, Vector3 velocity, float deltaTime)
        {
            if (thigh == null || shin == null || foot == null || _visual.FeetWeight <= 0f) return;
            Vector3 origin = foot.position + Vector3.up * _visual.FootRayUp;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits,
                _visual.FootRayUp + _visual.FootRayDown, _visual.GroundMask, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            RaycastHit ground = default;
            for (int i = 0; i < count; i++)
            {
                var hit = _groundHits[i];
                if (hit.collider == null || hit.transform.IsChildOf(transform.root) || hit.distance >= nearest) continue;
                ground = hit; nearest = hit.distance;
            }
            if (float.IsPositiveInfinity(nearest)) { state.Locked = false; state.HasLast = false; state.HasGround = false; return; }
            state.GroundPoint = ground.point; state.GroundNormal = ground.normal; state.HasGround = true;
            float surfaceGap = LowestSurface(state, ground.point, ground.normal, out int lowestIndex);
            float gap = lowestIndex >= 0 ? surfaceGap - _visual.FootSoleOffset : foot.position.y - ground.point.y - state.SoleHeight;
            // 골반을 낮춘 결과로 스윙 발까지 지지로 오인하지 않도록 원본 높이에서 접지 위상을 판정한다.
            float sourceContactGap = gap + b.PelvisLowering * ground.normal.y;
            state.SourceSoleGap = sourceContactGap;
            Vector3 local = b.Root.InverseTransformPoint(foot.TransformPoint(state.SoleLocal));
            Vector3 footVelocity = state.HasLast ? (local - state.LastLocal) / Mathf.Max(deltaTime, 0.0001f) : Vector3.zero;
            state.LastLocal = local; state.HasLast = true;
            float directionalVelocity = Vector3.Dot(footVelocity, velocity.normalized);
            bool planted = velocity.sqrMagnitude < .04f ? sourceContactGap < _visual.FootContactHeight : (state.Locked
                ? sourceContactGap < _visual.FootContactHeight + .025f && directionalVelocity < .25f
                : sourceContactGap < _visual.FootContactHeight && directionalVelocity < -.05f);
            if (!planted) state.Locked = false;
            if (state.Locked && state.ContactIndex >= 0 && lowestIndex >= 0
                && Vector3.Dot(state.Samples[state.ContactIndex].Position - ground.point, ground.normal) > surfaceGap + .02f)
            {
                // 접촉면이 바뀌어도 이전 수평 보정량은 유지한다. 새 원본 위치로 재고정하면 발이 튄다.
                state.LockedPosition += (FootRotation(foot, state, ground.normal) * Quaternion.Inverse(foot.rotation))
                    * (state.Samples[lowestIndex].Position - state.Samples[state.ContactIndex].Position);
                state.LockedPosition += ground.normal * (_visual.FootSoleOffset - Vector3.Dot(state.LockedPosition - ground.point, ground.normal));
                state.ContactIndex = lowestIndex;
            }
            if (planted && !state.Locked)
            {
                state.ContactIndex = lowestIndex;
                state.LockedPosition = lowestIndex >= 0 ? state.Samples[lowestIndex].Position : foot.position;
                if (lowestIndex >= 0) state.LockedPosition += ground.normal * (_visual.FootSoleOffset - surfaceGap);
                state.Locked = true;
                state.LockedRotation = Quaternion.FromToRotation(Vector3.up, ground.normal) * foot.rotation;
            }
            // 공중에 든 발을 바닥으로 끌어내리지 않고 접지 부근과 관통만 보정한다.
            float contact = gap > 0f ? 1f - Mathf.Clamp01(gap / Mathf.Max(0.001f, _visual.MaxFootCorrection)) : 1f;
            Quaternion rot = FootRotation(foot, state, ground.normal);
            Vector3 target = foot.position;
            if (state.Locked && state.ContactIndex >= 0)
                target = state.LockedPosition - (rot * Quaternion.Inverse(foot.rotation)) * (state.Samples[state.ContactIndex].Position - foot.position);
            else if (lowestIndex >= 0)
            {
                Vector3 anchor = state.Samples[lowestIndex].Position + ground.normal * Mathf.Clamp(-gap, -_visual.MaxFootCorrection, _visual.MaxFootCorrection);
                target = anchor - (rot * Quaternion.Inverse(foot.rotation)) * (state.Samples[lowestIndex].Position - foot.position);
            }
            else target.y += Mathf.Clamp(-gap, -_visual.MaxFootCorrection, _visual.MaxFootCorrection);
            state.TargetReachRatio = Vector3.Distance(thigh.position, target)
                / Mathf.Max(.001f, Vector3.Distance(thigh.position, shin.position) + Vector3.Distance(shin.position, foot.position));
            Vector3 pole = shin.position + b.Root.forward * 0.3f;
            float weight = _visual.FeetWeight * (state.Locked ? 1f : contact);
            PlayerVisualIK.Solve(thigh, shin, foot, target, pole, rot, weight);
            // 회전한 발가락까지 확인한다. 발목 높이만 맞추면 발끝이 바닥 아래로 내려갈 수 있다.
            for (int iteration = 0; iteration < 2 && lowestIndex >= 0; iteration++)
            {
                float actualGap = LowestSurface(state, ground.point, ground.normal, out _);
                Vector3 correction = state.Locked && state.ContactIndex >= 0
                    ? state.LockedPosition - state.Samples[state.ContactIndex].Position : Vector3.zero;
                float up = Mathf.Max(0f, _visual.FootSoleOffset - actualGap);
                correction += ground.normal * Mathf.Max(0f, up - Vector3.Dot(correction, ground.normal));
                if (correction.sqrMagnitude < .0000001f) break;
                PlayerVisualIK.Solve(thigh, shin, foot, foot.position + correction, pole, rot, weight);
            }
        }

        private static void SetVisibility(Bones b, bool show, bool near)
        {
            if (b == null) return;
            foreach (var r in b.RenderBatch != null ? b.RenderBatch.ActiveRenderers : b.Renderers)
            {
                if (r == null) continue;
                r.enabled = near ? show : true;
                r.shadowCastingMode = near ? ShadowCastingMode.Off : show ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            }
        }

        private static void SetBrushVisibility(Brush prop, bool show, bool keepShadow)
        {
            // 비활성화하지 않는다. 비표시 중에도 소켓 자세와 그림자가 갱신된다.
            foreach (var r in prop.Renderers)
            {
                if (r == null) continue;
                r.enabled = show || keepShadow;
                r.shadowCastingMode = keepShadow ? show ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly : ShadowCastingMode.Off;
            }
        }

        public void ReleasePresentation()
        {
            ResetArmRollHistory();
            _wasStroking = false;
            _worldProp?.Bristles?.ResetDeformation();
            _nearProp?.Bristles?.ResetDeformation();
            _worldSecondaryMotion?.ResetMotion(SecondaryMotionResetReason.RepresentationChanged);
            _nearSecondaryMotion?.ResetMotion(SecondaryMotionResetReason.RepresentationChanged);
            SetVisibility(_near, false, true);
            if (_nearProp != null) SetBrushVisibility(_nearProp, false, false);
            if (_viewCamera != null) _viewCamera.enabled = false;
        }
    }
}
