using System;
using System.Collections.Generic;
using Oheangbu.Presentation;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App.World
{
    /// <summary>
    /// Opt-in C02 derivative foot clearance, after Animator and before the upper-body gesture pass.
    /// Legacy mode only raises penetrating soles. The opt-in locomotion motor enables bounded
    /// stance planting, sole-normal alignment and a bounded visual pelvis correction; no gameplay root or cloth solve.
    /// </summary>
    [DefaultExecutionOrder(900)]
    [DisallowMultipleComponent]
    public sealed class WorldMacroPlayerFootPlacement : MonoBehaviour
    {
        [Serializable]
        public struct FootDiagnostics
        {
            public bool Bound, Grounded, Seated, Paused, LeftPlanted, RightPlanted;
            public int LeftSoleSamples, RightSoleSamples, Raycasts, SaturatedRaycasts, DiagnosticPlaneProbes;
            public float LeftRequestedLift, RightRequestedLift, LeftAppliedLift, RightAppliedLift;
            public float MaximumLocalLengthError;
            public float PelvisOffset;
        }

        [SerializeField] private Animator _animator;
        [SerializeField] private CharacterController _controller;
        [SerializeField] private WorldMacroCombatWalker _walker;
        [SerializeField] private SkinnedMeshRenderer[] _worldSkins = Array.Empty<SkinnedMeshRenderer>();
        [SerializeField] private LayerMask _groundLayers = ~0;
        [SerializeField, Range(0f, .01f)] private float _soleClearance = .003f;
        [SerializeField, Range(.01f, .12f)] private float _maximumLift = .075f;
        [SerializeField, Range(.1f, .6f)] private float _rayHeight = .3f;
        [SerializeField, Range(0f, 65f)] private float _maximumGroundSlope = 55f;

        private struct Influence { public Transform Bone; public Vector3 Point; public float Weight; public int Slot; }
        private struct Support { public Vector3 Point, Normal; }
        private sealed class SoleSample
        {
            public Influence[] Influences;
            public Vector3 RestWorld, World;
            public int Half;
        }
        private sealed class Leg
        {
            public Transform Upper, Lower, Foot;
            public SoleSample[] Sole, Clearance;
            public Quaternion UpperAnimation, LowerAnimation, FootAnimation;
            public bool Applied;
            public Vector3 FallbackBend;
            public bool Planted, HasPrevious;
            public Vector3 PlantAnkle, PreviousAnkle, PreviousRoot, SoleUpLocal, FilteredNormal = Vector3.up;
            public Quaternion PlantRotation;
            public float SmoothedLift;
            // #308 read-only footstep output (SPEC-SPELL-DEPLOY-308 section 10); the solve never reads these
            public bool Support308; public int SupportSerial308, SupportFrame308; public Vector3 SupportPoint308, SupportNormal308 = Vector3.up;
            // #307 phase 1 item 11: the distinct bones of Sole + Clearance and their matrices, read once per skinning pass.
            public Transform[] Bones = Array.Empty<Transform>();
            public Matrix4x4[] Matrices = Array.Empty<Matrix4x4>();
            public bool Valid => Upper != null && Lower != null && Foot != null && Sole != null && Sole.Length > 1;
        }

        private readonly RaycastHit[] _hits = new RaycastHit[32];
        /// <summary>#307: identical Clearance samples dropped at bind (both legs).</summary>
        public int ClearanceDuplicates307 { get; private set; }
        private Leg _left, _right;
        private PlayerMotor _motor;
        private Transform _hips;
        private Vector3 _hipsAnimation;
        private float _pelvisOffset;
        private float _evaluationDelta;
        private bool _pelvisApplied;
        private FootDiagnostics _diagnostics;
        private bool _diagnosticPlane;
        private Vector3 _planePoint, _planeNormal;
        public FootDiagnostics Diagnostics => _diagnostics;
        public string BindingError { get; private set; }
        public bool IsBound => _left != null && _right != null && _left.Valid && _right.Valid;

        public void Configure(Animator animator, CharacterController controller,
            WorldMacroCombatWalker walker, SkinnedMeshRenderer[] worldSkins)
        {
            RestoreAnimatedPose();
            _animator = animator; _controller = controller; _walker = walker;
            _worldSkins = worldSkins ?? Array.Empty<SkinnedMeshRenderer>();
            TryBind();
        }

        private void Awake() { TryBind(); }
        private void OnEnable() { TryBind(); }
        private void OnDisable() { RestoreAnimatedPose(); }

        public bool TryBind()
        {
            RestoreAnimatedPose();
            _left = _right = null; BindingError = null;
            _diagnostics = default;
            if (_animator == null) _animator = GetComponent<Animator>();
            _motor = _controller != null ? _controller.GetComponent<PlayerMotor>() : null;
            if (_animator == null || _animator.avatar == null || !_animator.isHuman)
            { BindingError = "A valid derivative Humanoid Animator is required."; return false; }
            if (_worldSkins == null || _worldSkins.Length == 0)
                _worldSkins = _animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            try
            {
                _left = BindLeg(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
                _right = BindLeg(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
                _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
                _diagnostics.Bound = IsBound;
                _diagnostics.LeftSoleSamples = _left.Sole.Length; _diagnostics.RightSoleSamples = _right.Sole.Length;
                if (!IsBound) BindingError = "Actual weighted sole vertices or leg bones could not be calibrated.";
                return IsBound;
            }
            catch (Exception exception)
            {
                BindingError = "Sole calibration failed: " + exception.Message;
                _left = _right = null; return false;
            }
        }

        private Leg BindLeg(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones foot)
        {
            var leg = new Leg { Upper = _animator.GetBoneTransform(upper), Lower = _animator.GetBoneTransform(lower),
                Foot = _animator.GetBoneTransform(foot), Sole = Array.Empty<SoleSample>(), Clearance = Array.Empty<SoleSample>() };
            if (leg.Upper == null || leg.Lower == null || leg.Foot == null) return leg;
            var candidates = new List<SoleSample>(); var clearance = new List<SoleSample>();
            foreach (var skin in _worldSkins)
            {
                if (skin == null || skin.sharedMesh == null) continue;
                Mesh mesh = skin.sharedMesh;
                if (!mesh.isReadable) throw new InvalidOperationException("The derivative mesh must be readable during sole calibration.");
                Transform[] bones = skin.bones; Matrix4x4[] bind = mesh.bindposes; Vector3[] vertices = mesh.vertices;
                var counts = mesh.GetBonesPerVertex(); var weights = mesh.GetAllBoneWeights();
                try
                {
                    int offset = 0;
                    for (int vertex = 0; vertex < counts.Length; vertex++)
                    {
                        int start = offset; float footWeight = 0f, clearanceWeight = 0f;
                        for (int j = 0; j < counts[vertex]; j++)
                        {
                            var weight = weights[offset++];
                            if (weight.boneIndex < bones.Length && bones[weight.boneIndex] == leg.Foot) footWeight += weight.weight;
                            if (weight.boneIndex < bones.Length && bones[weight.boneIndex] != null
                                && (bones[weight.boneIndex] == leg.Foot || bones[weight.boneIndex].IsChildOf(leg.Foot))) clearanceWeight += weight.weight;
                        }
                        if (footWeight < .7f && clearanceWeight < .5f) continue;
                        var influences = new List<Influence>(counts[vertex]);
                        for (int j = 0; j < counts[vertex]; j++)
                        {
                            var weight = weights[start + j]; int index = weight.boneIndex;
                            if (index >= bones.Length || index >= bind.Length || bones[index] == null || weight.weight <= 0f) continue;
                            influences.Add(new Influence { Bone = bones[index], Point = bind[index].MultiplyPoint3x4(vertices[vertex]), Weight = weight.weight });
                        }
                        if (influences.Count > 0)
                        {
                            var sample = new SoleSample { Influences = influences.ToArray(), RestWorld = skin.transform.TransformPoint(vertices[vertex]) };
                            if (footWeight >= .7f) candidates.Add(sample);
                            if (clearanceWeight >= .5f) clearance.Add(sample);
                        }
                    }
                }
                finally { counts.Dispose(); weights.Dispose(); }
            }
            if (candidates.Count < 2) return leg;
            candidates.Sort((a, b) => a.RestWorld.y.CompareTo(b.RestWorld.y));
            float cutoff = candidates[Mathf.Min(candidates.Count - 1, Mathf.CeilToInt(candidates.Count * .15f))].RestWorld.y;
            candidates.RemoveAll(sample => sample.RestWorld.y > cutoff);
            // Split the actual sole along its longest horizontal rest extent: two local ground probes.
            Bounds soleBounds = new Bounds(candidates[0].RestWorld, Vector3.zero);
            foreach (var sample in candidates) soleBounds.Encapsulate(sample.RestWorld);
            bool useX = soleBounds.size.x > soleBounds.size.z;
            candidates.Sort((a, b) => (useX ? a.RestWorld.x : a.RestWorld.z).CompareTo(useX ? b.RestWorld.x : b.RestWorld.z));
            for (int i = 0; i < candidates.Count; i++) candidates[i].Half = i < candidates.Count / 2 ? 0 : 1;
            leg.Sole = candidates.ToArray();
            foreach (var sample in clearance) sample.Half = (useX ? sample.RestWorld.x < soleBounds.center.x : sample.RestWorld.z < soleBounds.center.z) ? 0 : 1;
            // #307: vertices split at UV/normal seams repeat the same bones, bind points and weights, so they skin to the same point;
            // Clearance feeds only a maximum, so one copy each gives the identical lift (Sole keeps its copies: it feeds centroids)
            var seen = new HashSet<string>(); var unique = new List<SoleSample>(clearance.Count);
            foreach (var sample in clearance)
            {
                var key = new System.Text.StringBuilder();
                foreach (var influence in sample.Influences)
                    key.Append(influence.Bone.GetInstanceID()).Append(':').Append(BitConverter.SingleToInt32Bits(influence.Point.x)).Append(',')
                       .Append(BitConverter.SingleToInt32Bits(influence.Point.y)).Append(',').Append(BitConverter.SingleToInt32Bits(influence.Point.z)).Append(',')
                       .Append(BitConverter.SingleToInt32Bits(influence.Weight)).Append(';');
                key.Append('h').Append(sample.Half);
                if (seen.Add(key.ToString())) unique.Add(sample);
            }
            ClearanceDuplicates307 += clearance.Count - unique.Count;
            leg.Clearance = unique.ToArray();
            Vector3 limb = leg.Foot.position - leg.Upper.position;
            Vector3 bend = Vector3.ProjectOnPlane(leg.Lower.position - leg.Upper.position, limb);
            // The selected C02 native clip/mesh faces -localZ; only used for a completely straight rest knee.
            if (bend.sqrMagnitude < .000001f) bend = Vector3.ProjectOnPlane(-_animator.transform.forward, limb);
            leg.FallbackBend = _animator.transform.InverseTransformDirection(bend.normalized);
            leg.SoleUpLocal = Quaternion.Inverse(leg.Foot.rotation) * Vector3.up;
            BindSkinSlots(leg);
            return leg;
        }

        private void Update()
        {
            // Animator must start from its own prior pose, never the accumulated previous correction.
            // A paused pose remains exactly where it was; seating still releases the visual override.
            using (Perf307Markers.FootPlacement.Auto()) { if (Time.timeScale > 0f || (_walker != null && _walker.Seated)) RestoreAnimatedPose(); }
        }

        private void LateUpdate()
        {
            using (Perf307Markers.FootPlacement.Auto()) { bool seated = _walker != null && _walker.Seated;
            bool grounded = _controller != null && _controller.enabled && _controller.isGrounded;
            if (_motor != null && _motor.HasLocomotion) grounded = _motor.IsLocomotionGrounded && !_motor.IsSitting && !_motor.IsDodging;
            EvaluatePoseCore(grounded, seated, Time.timeScale <= 0f, Time.deltaTime, true); }
        }

        /// <summary>
        /// Explicit diagnostic entry after sampling the Animator. In a manual loop call
        /// RestoreAnimatedPose BEFORE evaluating the next animation sample, then this method.
        /// This method never changes gameplay grounding, controller position or camera state.
        /// </summary>
        public void EvaluatePose(bool grounded, bool seated, bool paused)
            => EvaluatePoseAtStep(grounded, seated, paused, Time.deltaTime);

        /// <summary>Same solver with an explicit scaled step for isolated animation diagnostics.</summary>
        public void EvaluatePoseAtStep(bool grounded, bool seated, bool paused, float scaledDelta)
            => EvaluatePoseCore(grounded, seated, paused, scaledDelta, false);

        // #307 phase 1 item 11: only the live LateUpdate pass treats a body with no drawn world skin as airborne; the explicit
        // entry points (EvaluatePose / EvaluatePoseAtStep / EvaluatePoseAgainstPlane*) keep solving hidden QA models (ReRigGait).
        private void EvaluatePoseCore(bool grounded, bool seated, bool paused, float scaledDelta, bool skipUndrawn)
        {
            if (!Finite(scaledDelta) || scaledDelta < 0f) throw new ArgumentOutOfRangeException(nameof(scaledDelta));
            _evaluationDelta = scaledDelta;
            _diagnostics.Bound = IsBound; _diagnostics.Grounded = grounded;
            _diagnostics.Seated = seated; _diagnostics.Paused = paused;
            if (seated) { RestoreAnimatedPose(); ReleasePlant(_left); ReleasePlant(_right); _pelvisOffset = 0f;
                _diagnostics.LeftPlanted = _diagnostics.RightPlanted = false; _diagnostics.PelvisOffset = 0f; return; }
            if (paused) return;
            _diagnostics.Raycasts = _diagnostics.SaturatedRaycasts = _diagnostics.DiagnosticPlaneProbes = 0;
            _diagnostics.LeftRequestedLift = _diagnostics.RightRequestedLift = 0f;
            _diagnostics.LeftAppliedLift = _diagnostics.RightAppliedLift = 0f;
            _diagnostics.MaximumLocalLengthError = 0f;
            // #307 phase 1 item 11 (live pass only): a body none of whose world skins is drawn this frame (all disabled or inactive)
            // is treated like an airborne one; a skin merely outside the view still evaluates (isVisible lags a frame, so it is not used).
            if (!grounded || !IsBound || skipUndrawn && !AnySkinDrawn()) { ReleasePlant(_left); ReleasePlant(_right); _pelvisOffset = 0f;
                _diagnostics.LeftPlanted = _diagnostics.RightPlanted = false; _diagnostics.PelvisOffset = 0f; return; }
            if (_motor != null && _motor.HasLocomotion) ApplyPelvisReach();
            Apply(_left, true); Apply(_right, false);
            _diagnostics.LeftPlanted = _left.Planted; _diagnostics.RightPlanted = _right.Planted;
            _diagnostics.PelvisOffset = _pelvisOffset;
        }

        /// <summary>
        /// Isolated pose diagnostic only: evaluates an explicit infinite ground plane without adding
        /// colliders to the live world. Not called by LateUpdate and not evidence of terrain-ray support.
        /// RestoreAnimatedPose must precede each new manual Animator sample, as with EvaluatePose.
        /// </summary>
        public void EvaluatePoseAgainstPlane(Vector3 point, Vector3 normal, bool grounded, bool seated, bool paused)
            => EvaluatePoseAgainstPlaneAtStep(point, normal, grounded, seated, paused, Time.deltaTime);

        public void EvaluatePoseAgainstPlaneAtStep(Vector3 point, Vector3 normal, bool grounded, bool seated, bool paused, float scaledDelta)
        {
            if (!Finite(point) || !Finite(normal) || normal.sqrMagnitude < .000001f || normal.normalized.y <= .1f)
                throw new ArgumentException("A finite upward-facing diagnostic plane is required.");
            bool previous = _diagnosticPlane; Vector3 previousPoint = _planePoint, previousNormal = _planeNormal;
            try
            {
                _diagnosticPlane = true; _planePoint = point; _planeNormal = normal.normalized;
                EvaluatePoseAtStep(grounded, seated, paused, scaledDelta);
            }
            finally { _diagnosticPlane = previous; _planePoint = previousPoint; _planeNormal = previousNormal; }
        }

        private void Apply(Leg leg, bool left)
        {
            Vector3 first = Vector3.zero, second = Vector3.zero; int firstCount = 0, secondCount = 0;
            ReadBones(leg);
            foreach (var sample in leg.Sole)
            {
                Vector3 point = Skin(leg, sample);
                if (!Finite(point)) return;
                sample.World = point;
                if (sample.Half == 0) { first += point; firstCount++; } else { second += point; secondCount++; }
            }
            if (firstCount == 0 || secondCount == 0) return;
            if (!Ground(first / firstCount, out Support firstHit) || !Ground(second / secondCount, out Support secondHit))
            { ReleasePlant(leg); return; }
            float lift = 0f;
            foreach (var sample in leg.Sole)
            {
                Support hit = sample.Half == 0 ? firstHit : secondHit;
                float groundY = hit.Point.y - ((sample.World.x - hit.Point.x) * hit.Normal.x
                    + (sample.World.z - hit.Point.z) * hit.Normal.z) / hit.Normal.y;
                lift = Mathf.Max(lift, groundY + _soleClearance - sample.World.y);
            }
            if (left) _diagnostics.LeftRequestedLift = lift; else _diagnostics.RightRequestedLift = lift;
            if (_motor != null && _motor.HasLocomotion)
            {
                lift = Mathf.Max(lift, SoleLift(leg, firstHit, secondHit, true));
                if (left) _diagnostics.LeftRequestedLift = lift; else _diagnostics.RightRequestedLift = lift;
                ApplyLocomotionFoot(leg, lift, firstHit, secondHit, left);
                return;
            }
            if (!TryGetPenetrationTarget(leg.Upper.position, leg.Lower.position, leg.Foot.position, lift, _maximumLift, out Vector3 target)) return;
            float upperLength = leg.Lower.localPosition.magnitude, lowerLength = leg.Foot.localPosition.magnitude;
            leg.UpperAnimation = leg.Upper.localRotation; leg.LowerAnimation = leg.Lower.localRotation;
            leg.FootAnimation = leg.Foot.localRotation; leg.Applied = true;
            Vector3 oldFoot = leg.Foot.position;
            Vector3 limb = oldFoot - leg.Upper.position;
            Vector3 bend = Vector3.ProjectOnPlane(leg.Lower.position - leg.Upper.position, limb);
            if (bend.sqrMagnitude < .000001f) bend = _animator.transform.TransformDirection(leg.FallbackBend) * .1f;
            Vector3 pole = leg.Lower.position + bend;
            PlayerVisualIK.Solve(leg.Upper, leg.Lower, leg.Foot, target, pole, leg.Foot.rotation, 1f, .9999f);
            float applied = leg.Foot.position.y - oldFoot.y;
            if (left) _diagnostics.LeftAppliedLift = applied; else _diagnostics.RightAppliedLift = applied;
            _diagnostics.MaximumLocalLengthError = Mathf.Max(_diagnostics.MaximumLocalLengthError,
                Mathf.Max(Mathf.Abs(upperLength - leg.Lower.localPosition.magnitude), Mathf.Abs(lowerLength - leg.Foot.localPosition.magnitude)));
        }

        private bool Ground(Vector3 sole, out Support nearest)
        {
            nearest = default;
            if (_diagnosticPlane)
            {
                _diagnostics.DiagnosticPlaneProbes++;
                float y = _planePoint.y - ((sole.x - _planePoint.x) * _planeNormal.x
                    + (sole.z - _planePoint.z) * _planeNormal.z) / _planeNormal.y;
                float distance = sole.y + _rayHeight - y;
                if (distance < 0f || distance > _rayHeight + _maximumLift + .15f
                    || _planeNormal.y < Mathf.Cos(_maximumGroundSlope * Mathf.Deg2Rad)) return false;
                nearest = new Support { Point = new Vector3(sole.x, y, sole.z), Normal = _planeNormal };
                return true;
            }
            int count = Physics.RaycastNonAlloc(sole + Vector3.up * _rayHeight, Vector3.down, _hits,
                _rayHeight + _maximumLift + .15f, _groundLayers, QueryTriggerInteraction.Ignore);
            _diagnostics.Raycasts++;
            // A full unordered buffer may omit the real support. Skip rather than choose an arbitrary hit.
            if (count == _hits.Length) { _diagnostics.SaturatedRaycasts++; return false; }
            Transform self = _controller != null ? _controller.transform : _animator.transform;
            float slope = _motor != null && _motor.HasLocomotion && _controller != null ? _controller.slopeLimit : _maximumGroundSlope;
            float best = float.PositiveInfinity, normalMinimum = Mathf.Cos(slope * Mathf.Deg2Rad);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                if (hit.collider == null || hit.collider.isTrigger || hit.transform == self || hit.transform.IsChildOf(self)
                    || hit.normal.y < normalMinimum || hit.distance >= best) continue;
                nearest = new Support { Point = hit.point, Normal = hit.normal }; best = hit.distance;
            }
            return !float.IsPositiveInfinity(best);
        }

        /// <summary>Pure, bounded ankle target. A nonpenetrating/lifted foot is returned unchanged.</summary>
        public static bool TryGetPenetrationTarget(Vector3 hip, Vector3 knee, Vector3 ankle,
            float requiredLift, float maximumLift, out Vector3 target)
        {
            target = ankle;
            if (!Finite(hip) || !Finite(knee) || !Finite(ankle) || !Finite(requiredLift) || !Finite(maximumLift)
                || requiredLift <= .0001f || maximumLift <= 0f) return false;
            float upper = Vector3.Distance(hip, knee), lower = Vector3.Distance(knee, ankle);
            if (upper <= .001f || lower <= .001f) return false;
            Vector3 requested = ankle + Vector3.up * Mathf.Min(requiredLift, maximumLift);
            target = PlayerVisualIK.ClampReach(hip, requested, upper, lower, .9999f);
            if (!Finite(target) || target.y <= ankle.y) { target = ankle; return false; }
            return true;
        }

        public void RestoreAnimatedPose()
        {
            Restore(_left); Restore(_right);
            if (_pelvisApplied && _hips != null) _hips.localPosition = _hipsAnimation;
            _pelvisApplied = false;
        }

        /// <summary>#308 read-only footstep output: `serial` rises once each time this leg's support phase begins (not Planted, which
        /// never latches on authored-roll walks). False when the legs were not evaluated this frame (body not drawn, airborne, seated).</summary>
        public bool TryGetSupport(bool left, out int serial, out Vector3 point, out Vector3 normal)
        {
            var leg = left ? _left : _right;
            serial = 0; point = default; normal = Vector3.up;
            if (leg == null || Time.frameCount - leg.SupportFrame308 > 1) return false;
            serial = leg.SupportSerial308; point = leg.SupportPoint308; normal = leg.SupportNormal308;
            return true;
        }

        private static void ReleasePlant(Leg leg)
        {
            if (leg == null) return;
            leg.Planted = leg.HasPrevious = false; leg.SmoothedLift = 0f;
            leg.Support308 = false;   // #308: the next support phase after a release is a new step
        }

        private static float PelvisReachRequest(Leg leg)
        {
            if (leg == null || !leg.Planted) return 0f;
            float length = Vector3.Distance(leg.Upper.position, leg.Lower.position) + Vector3.Distance(leg.Lower.position, leg.Foot.position);
            return Mathf.Min(0f, -(Vector3.Distance(leg.Upper.position, leg.PlantAnkle) - length * .985f));
        }

        private void ApplyPelvisReach()
        {
            if (_hips == null) return;
            float requiredLift = Mathf.Max(PredictLift(_left), PredictLift(_right));
            // A forward foot on an uphill plane can require more than the ankle's 75mm
            // allowance. Lift the visual pelvis by at most40mm before solving either leg;
            // do not push the controller/camera or relax the measured penetration limit.
            float rise = Mathf.Clamp(requiredLift - _maximumLift + .004f, 0f, .04f);
            float requested = rise > 0f ? rise : Mathf.Clamp(Mathf.Min(PelvisReachRequest(_left), PelvisReachRequest(_right)), -.035f, 0f);
            _pelvisOffset = requested > _pelvisOffset ? requested
                : Mathf.Lerp(_pelvisOffset, requested, 1f - Mathf.Exp(-18f * _evaluationDelta));
            _hipsAnimation = _hips.localPosition; _pelvisApplied = true;
            _hips.position += Vector3.up * _pelvisOffset;
        }

        private float PredictLift(Leg leg)
        {
            Vector3 first = Vector3.zero, second = Vector3.zero; int firstCount = 0, secondCount = 0;
            ReadBones(leg);
            foreach (var sample in leg.Sole)
            {
                Vector3 point = Skin(leg, sample);
                sample.World = point;
                if (sample.Half == 0) { first += point; firstCount++; } else { second += point; secondCount++; }
            }
            if (firstCount == 0 || secondCount == 0 || !Ground(first / firstCount, out Support a) || !Ground(second / secondCount, out Support b)) return 0f;
            return SoleLift(leg, a, b, true);
        }

        private float SoleLift(Leg leg, Support first, Support second, bool resample)
        {
            float lift = 0f;
            if (resample) ReadBones(leg);
            foreach (var sample in leg.Clearance)
            {
                Vector3 point = sample.World;
                if (resample) point = Skin(leg, sample);
                Support hit = sample.Half == 0 ? first : second;
                float groundY = hit.Point.y - ((point.x - hit.Point.x) * hit.Normal.x + (point.z - hit.Point.z) * hit.Normal.z) / hit.Normal.y;
                lift = Mathf.Max(lift, groundY + _soleClearance - point.y);
            }
            return lift;
        }

        private void ApplyLocomotionFoot(Leg leg, float lift, Support firstHit, Support secondHit, bool left)
        {
            float dt = Mathf.Max(.0001f, _evaluationDelta);
            Vector3 ankle = leg.Foot.position, root = _controller.transform.position;
            Vector3 relativeVelocity = leg.HasPrevious ? ((ankle - leg.PreviousAnkle) - (root - leg.PreviousRoot)) / dt : Vector3.zero;
            float lowest = float.PositiveInfinity;
            foreach (var sample in leg.Sole) lowest = Mathf.Min(lowest, sample.World.y);
            leg.PreviousAnkle = ankle; leg.PreviousRoot = root; leg.HasPrevious = true;
            Vector3 normal = (firstHit.Normal + secondHit.Normal).normalized;
            // Contact is inferred from the evaluated skin height and directional foot
            // trajectory, not a hardcoded phase shared by unrelated source clips.
            bool nearGround = lift > -.0001f && lowest - _controller.bounds.min.y < .045f;
            float travel = new Vector2(_motor.ActualLocalVelocity.x, _motor.ActualLocalVelocity.z).magnitude;
            Vector3 travelDirection = _controller.transform.TransformDirection(new Vector3(_motor.ActualLocalVelocity.x, 0f, _motor.ActualLocalVelocity.z)).normalized;
            // A low returning swing foot can have almost no vertical velocity. It must
            // not plant while moving with actor travel instead of opposing it.
            bool opposingTravel = travel < .08f || Vector3.Dot(relativeVelocity, travelDirection) < -.05f;
            // Heel/toe rollover may lift the ankle and even the lowest weighted sample
            // before toe-off. Opposing horizontal travel is the reliable release signal;
            // a returning swing moves with travel and cannot plant merely because it is low.
            bool supportPhase = nearGround && opposingTravel;
            if (supportPhase && !leg.Support308) { leg.SupportSerial308++; leg.SupportPoint308 = new Vector3(ankle.x, lowest, ankle.z); leg.SupportNormal308 = normal; }
            leg.Support308 = supportPhase; leg.SupportFrame308 = Time.frameCount;   // #308: the rising edge of the support phase is a footstep
            // Calibrated natural clips already carry heel-to-toe support travel.
            // Locking their ankle flattens toe-off and fights the shorter C02 shins.
            bool authoredRoll = _motor.LocomotionProfile.PreserveAuthoredFootRoll && !_motor.IsCrouching;
            bool canPlant = supportPhase && !authoredRoll;
            if (leg.Planted && (!canPlant || Vector3.ProjectOnPlane(leg.PlantAnkle - ankle, normal).magnitude > .35f || travel < .08f)) leg.Planted = false;
            float bounded = Mathf.Clamp(lift, 0f, _maximumLift);
            // Rise quickly to avoid penetration; release smoothly to avoid ankle popping on seams.
            leg.SmoothedLift = bounded > leg.SmoothedLift ? bounded : Mathf.Lerp(leg.SmoothedLift, bounded, 1f - Mathf.Exp(-20f * dt));
            Vector3 target = ankle + Vector3.up * leg.SmoothedLift;
            if (!leg.Planted && canPlant && travel >= .08f) { leg.Planted = true; leg.PlantAnkle = target; leg.PlantRotation = leg.Foot.rotation; }
            if (leg.Planted) target = leg.PlantAnkle + Vector3.up * Mathf.Max(0f, target.y - leg.PlantAnkle.y);
            target.y = Mathf.Min(target.y, ankle.y + _maximumLift);
            leg.FilteredNormal = Vector3.Slerp(leg.FilteredNormal, normal, 1f - Mathf.Exp(-16f * dt));
            Quaternion rotation = leg.Planted ? leg.PlantRotation : leg.Foot.rotation;
            if (supportPhase || lift > .003f)
                rotation = authoredRoll ? Quaternion.FromToRotation(Vector3.up, leg.FilteredNormal) * rotation
                    : Quaternion.FromToRotation(rotation * leg.SoleUpLocal, leg.FilteredNormal) * rotation;
            leg.UpperAnimation = leg.Upper.localRotation; leg.LowerAnimation = leg.Lower.localRotation;
            leg.FootAnimation = leg.Foot.localRotation; leg.Applied = true;
            float upper = leg.Lower.localPosition.magnitude, lower = leg.Foot.localPosition.magnitude;
            Vector3 limb = ankle - leg.Upper.position;
            Vector3 bend = Vector3.ProjectOnPlane(leg.Lower.position - leg.Upper.position, limb);
            if (bend.sqrMagnitude < .000001f) bend = _animator.transform.TransformDirection(leg.FallbackBend) * .1f;
            Vector3 pole = leg.Lower.position + bend;
            // Clamp horizontal planting distance at the requested height. A spherical
            // reach clamp alone can raise a trailing ankle above its 75mm allowance.
            target = ReachAtHeight(leg, target);
            PlayerVisualIK.Solve(leg.Upper, leg.Lower, leg.Foot, target, pole, rotation, 1f, .9999f);
            // Tilting the foot to the plane can lower a heel/toe after the initial lift
            // was measured. Re-evaluate the same actual weighted skin against the two
            // support planes, using only the remaining original75mm ankle allowance.
            float remaining = Mathf.Max(0f, _maximumLift - (leg.Foot.position.y - ankle.y));
            float correction = Mathf.Min(SoleLift(leg, firstHit, secondHit, true), remaining);
            if (correction > .0001f)
                PlayerVisualIK.Solve(leg.Upper, leg.Lower, leg.Foot, ReachAtHeight(leg, leg.Foot.position + Vector3.up * correction), pole, rotation, 1f, .9999f);
            if (left) _diagnostics.LeftAppliedLift = leg.Foot.position.y - ankle.y;
            else _diagnostics.RightAppliedLift = leg.Foot.position.y - ankle.y;
            _diagnostics.MaximumLocalLengthError = Mathf.Max(_diagnostics.MaximumLocalLengthError,
                Mathf.Max(Mathf.Abs(upper - leg.Lower.localPosition.magnitude), Mathf.Abs(lower - leg.Foot.localPosition.magnitude)));
        }

        private static Vector3 ReachAtHeight(Leg leg, Vector3 target)
        {
            Vector3 hip = leg.Upper.position;
            float reach = (Vector3.Distance(hip, leg.Lower.position) + Vector3.Distance(leg.Lower.position, leg.Foot.position)) * .9998f;
            float dy = target.y - hip.y;
            float radius = Mathf.Sqrt(Mathf.Max(0f, reach * reach - dy * dy));
            Vector2 offset = Vector2.ClampMagnitude(new Vector2(target.x - hip.x, target.z - hip.z), radius);
            return new Vector3(hip.x + offset.x, target.y, hip.z + offset.y);
        }

        // #307 phase 1 item 11: each weighted sole vertex from one matrix read per bone per pass instead of a TransformPoint per
        // influence. Same weights and bind points; the result differs from TransformPoint only by float round-off (a few ulps of
        // the world coordinate; MeasureSkinningDelta307 reports it).
        private static void BindSkinSlots(Leg leg)
        {
            var bones = new List<Transform>();
            void Slot(SoleSample[] samples)
            {
                foreach (var sample in samples)
                    for (int k = 0; k < sample.Influences.Length; k++)
                    {
                        Transform bone = sample.Influences[k].Bone; int at = bones.IndexOf(bone);
                        if (at < 0) { at = bones.Count; bones.Add(bone); }
                        sample.Influences[k].Slot = at;
                    }
            }
            Slot(leg.Sole); Slot(leg.Clearance);
            leg.Bones = bones.ToArray(); leg.Matrices = new Matrix4x4[leg.Bones.Length];
        }
        private static void ReadBones(Leg leg)
        {
            for (int i = 0; i < leg.Bones.Length; i++) leg.Matrices[i] = leg.Bones[i].localToWorldMatrix;
        }
        private static Vector3 Skin(Leg leg, SoleSample sample)
        {
            Vector3 point = Vector3.zero;
            foreach (var influence in sample.Influences) point += leg.Matrices[influence.Slot].MultiplyPoint3x4(influence.Point) * influence.Weight;
            return point;
        }
        private bool AnySkinDrawn()
        {
            if (_worldSkins == null) return false;
            foreach (var skin in _worldSkins) if (skin != null && skin.enabled && skin.gameObject.activeInHierarchy) return true;
            return false;
        }
        /// <summary>#307 diagnostic (Perf307Checks feet-identity): the largest distance between the former per-influence
        /// TransformPoint sole points and the matrix path, over every Sole and Clearance sample of both legs in the current pose;
        /// magnitude = the largest world coordinate involved (float spacing there bounds the round-off). NaN when unbound.</summary>
        public float MeasureSkinningDelta307(out float magnitude)
        {
            magnitude = 0f;
            if (!IsBound) return float.NaN;
            float delta = 0f;
            foreach (var leg in new[] { _left, _right })
            {
                ReadBones(leg);
                foreach (var samples in new[] { leg.Sole, leg.Clearance })
                    foreach (var sample in samples)
                    {
                        Vector3 before = Vector3.zero;
                        foreach (var influence in sample.Influences) before += influence.Bone.TransformPoint(influence.Point) * influence.Weight;
                        Vector3 after = Skin(leg, sample);
                        delta = Mathf.Max(delta, (after - before).magnitude);
                        magnitude = Mathf.Max(magnitude, Mathf.Abs(before.x), Mathf.Abs(before.y), Mathf.Abs(before.z));
                    }
            }
            return delta;
        }

        private static void Restore(Leg leg)
        {
            if (leg == null || !leg.Applied) return;
            if (leg.Upper != null) leg.Upper.localRotation = leg.UpperAnimation;
            if (leg.Lower != null) leg.Lower.localRotation = leg.LowerAnimation;
            if (leg.Foot != null) leg.Foot.localRotation = leg.FootAnimation;
            leg.Applied = false;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
