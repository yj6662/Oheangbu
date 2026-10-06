using Oheangbu.Data;
using UnityEngine;

namespace Oheangbu.Presentation
{
    public readonly struct BrushBristlePose
    {
        public readonly bool Valid;
        public readonly Vector3 TipPositionLocal, TipOffsetInGripLocal;
        public readonly Quaternion TipRotationLocal;
        public readonly float BristleArcLength, BendDegrees, Splay;
        public float GripToTipDistance => TipOffsetInGripLocal.magnitude;

        internal BrushBristlePose(Vector3 tip, Quaternion rotation, Vector3 gripOffset,
            float arcLength, float bendDegrees, float splay)
        {
            Valid = true; TipPositionLocal = tip; TipRotationLocal = rotation;
            TipOffsetInGripLocal = gripOffset; BristleArcLength = arcLength;
            BendDegrees = bendDegrees; Splay = splay;
        }
    }

    /// <summary>
    /// EvaluateLocal/EvaluateInFrame → 손 IK/붓 배치 → ApplyPose → EffectTip 순으로 직접 호출한다.
    /// Update/LateUpdate는 없다. 6본 중심선의 호 길이와 붓대/GripSocket 변환을 보존한다.
    /// 속도는 표시 획에서만 전달하며 카메라/손/플레이어의 Transform 이동을 입력으로 추정하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BrushBristleRig : MonoBehaviour
    {
        public const int DeformationBoneCount = 6;
        [SerializeField] private BrushDeformationProfileSO _profile;
        [SerializeField] private Transform _gripSocket;
        [SerializeField] private Transform _tipSocket;
        [SerializeField] private Transform[] _bones = new Transform[DeformationBoneCount];
        [SerializeField] private SkinnedMeshRenderer _bristleRenderer;
        [SerializeField] private string _splayBlendShape = "BristleSplay";

        private readonly Vector3[] _restPositions = new Vector3[DeformationBoneCount];
        private readonly Quaternion[] _restRotations = new Quaternion[DeformationBoneCount];
        private readonly Vector3[] _segments = new Vector3[DeformationBoneCount];
        private readonly float[] _lengths = new float[DeformationBoneCount];
        private readonly Vector3[] _posePositions = new Vector3[DeformationBoneCount];
        private readonly Quaternion[] _poseRotations = new Quaternion[DeformationBoneCount];
        private Vector3 _gripPosition, _restTipPosition, _restAxis, _bend;
        private Quaternion _gripRotation, _restTipRotation;
        private float _arcLength, _splay;
        private int _splayIndex = -1;
        public bool IsBound { get; private set; }
        public string BindingError { get; private set; }
        public bool SplayAvailable => IsBound && _splayIndex >= 0;
        public BrushBristlePose Pose { get; private set; }
        public Transform TipSocket => _tipSocket;
        public Vector3 ActualTipWorld => _tipSocket != null ? _tipSocket.position : transform.position;
        public Vector3 RestTipPositionLocal => _restTipPosition;
        public Quaternion GripRotationLocal => _gripRotation;

        private void Awake() { TryBind(); }
        private void OnDisable() { ResetDeformation(); }

        /// <summary>애니메이터/다른 솔버가 쓰기 전의 모델 bind/rest 상태에서 한 번 호출한다.</summary>
        public bool Configure(BrushDeformationProfileSO profile, Transform gripSocket, Transform tipSocket,
            Transform[] bones, SkinnedMeshRenderer bristleRenderer = null, string splayBlendShape = "BristleSplay")
        {
            // Restore the old chain before replacing any references, even if the new binding is invalid.
            if (IsBound) ResetDeformation();
            IsBound = false;
            _profile = profile; _gripSocket = gripSocket; _tipSocket = tipSocket;
            _bones = bones != null ? (Transform[])bones.Clone() : null;
            _bristleRenderer = bristleRenderer; _splayBlendShape = splayBlendShape;
            return TryBind();
        }

        public bool TryBind()
        {
            // Rebinding an active instance must not turn the currently bent pose into a new rest pose.
            if (IsBound) ResetDeformation();
            IsBound = false; Pose = default; _splayIndex = -1;
            if (_profile == null) return Fail("Brush deformation profile is missing.");
            if (_gripSocket == null || _tipSocket == null || _gripSocket == _tipSocket)
                return Fail("Distinct GripSocket and TipSocket are required.");
            if (!WithinRoot(_gripSocket) || !WithinRoot(_tipSocket))
                return Fail("Both sockets must belong to this brush root.");
            if (_bones == null || _bones.Length != DeformationBoneCount)
                return Fail("Exactly six bristle deformation bones are required.");
            // Local model metres are also world metres. Reject scaled rigs instead of silently stretching.
            if (!UnitScale(transform.lossyScale)) return Fail("Brush root must have positive unit world scale.");
            _arcLength = 0f;
            for (int i = 0; i < DeformationBoneCount; i++)
            {
                Transform bone = _bones[i];
                if (bone == null || bone == transform || !WithinRoot(bone))
                    return Fail("Every deformation bone must be a descendant of the brush root.");
                if (i > 0 && bone.parent != _bones[i - 1])
                    return Fail("Bristle bones must form one direct chain, ordered root to tip.");
                if (!UnitScale(bone.lossyScale)) return Fail("Scaled deformation bones are unsupported.");
                if (_gripSocket == bone || _gripSocket.IsChildOf(bone))
                    return Fail("GripSocket must stay outside the deforming chain.");
                if (_tipSocket == bone || bone.IsChildOf(_tipSocket))
                    return Fail("TipSocket cannot be a deformation bone or its ancestor.");
                _restPositions[i] = transform.InverseTransformPoint(bone.position);
                _restRotations[i] = Quaternion.Inverse(transform.rotation) * bone.rotation;
            }
            _gripPosition = transform.InverseTransformPoint(_gripSocket.position);
            _gripRotation = Quaternion.Inverse(transform.rotation) * _gripSocket.rotation;
            _restTipPosition = transform.InverseTransformPoint(_tipSocket.position);
            _restTipRotation = Quaternion.Inverse(transform.rotation) * _tipSocket.rotation;
            for (int i = 0; i < DeformationBoneCount; i++)
            {
                Vector3 next = i + 1 < DeformationBoneCount ? _restPositions[i + 1] : _restTipPosition;
                _segments[i] = next - _restPositions[i];
                _lengths[i] = _segments[i].magnitude;
                if (!Finite(_lengths[i]) || _lengths[i] < 0.00001f)
                    return Fail("Each bristle bone must own a nonzero segment, including last bone to TipSocket.");
                _arcLength += _lengths[i];
            }
            _restAxis = (_restTipPosition - _restPositions[0]).normalized;
            if (_restAxis.sqrMagnitude < 0.5f) return Fail("Bristle root and rest endpoint must be distinct.");
            if (_bristleRenderer != null && _bristleRenderer.sharedMesh != null && !string.IsNullOrEmpty(_splayBlendShape))
                _splayIndex = _bristleRenderer.sharedMesh.GetBlendShapeIndex(_splayBlendShape);
            BindingError = null; IsBound = true;
            ResetDeformation();
            return true;
        }

        /// <summary>
        /// 근접/월드 양쪽에 사용한다. reference 회전만 전달하므로 카메라 평행 이동은 굽힘을 만들지 않는다.
        /// desiredGripWorldRotation은 이번 프레임 IK에서 사용할 회전이며 이전 프레임 실제 붓 회전이 아니다.
        /// </summary>
        public BrushBristlePose EvaluateInFrame(Vector3 visualVelocityInReferenceSpace,
            Quaternion referenceWorldRotation, Quaternion desiredGripWorldRotation, bool stroking, float deltaTime)
        {
            Quaternion brushRotation = desiredGripWorldRotation * Quaternion.Inverse(_gripRotation);
            Vector3 velocity = Quaternion.Inverse(brushRotation) * (referenceWorldRotation * visualVelocityInReferenceSpace);
            return EvaluateLocal(velocity, stroking, deltaTime);
        }

        /// <summary>
        /// 본/소켓은 아직 쓰지 않고 이번 프레임의 로컬 곡선과 실제 끝점을 먼저 계산한다.
        /// 반환 TipOffsetInGripLocal을 사용해 gripPosition = targetTip - gripRotation * offset으로 IK를 푼다.
        /// 동일 인스턴스는 표시 프레임당 한 번만 평가한다. pen-up은 같은 dt로 중립 복귀한다.
        /// </summary>
        public BrushBristlePose EvaluateLocal(Vector3 visualVelocityInBrushSpace, bool stroking, float deltaTime)
        {
            if (!IsBound) return default;
            if (!Finite(deltaTime) || deltaTime < 0f) deltaTime = 0f;
            if (deltaTime > Safe(_profile.ResetAfterGap, 0.5f, 0.1f))
            {
                ResetDeformation(false);
                return Pose;
            }
            Vector3 lateral = Finite(visualVelocityInBrushSpace)
                ? Vector3.ProjectOnPlane(visualVelocityInBrushSpace, _restAxis) : Vector3.zero;
            float amount = stroking ? Mathf.Clamp01(lateral.magnitude / Safe(_profile.FullBendSpeed, 1.5f, 0.001f)) : 0f;
            float maximum = Mathf.Clamp(Safe(_profile.MaximumBendDegrees, 28f, 0f), 0f, 60f);
            Vector3 target = lateral.sqrMagnitude > 0.00000001f ? -lateral.normalized * (amount * maximum) : Vector3.zero;
            float bendRate = amount > 0f ? Safe(_profile.BendResponse, 22f, 0.01f) : Safe(_profile.RecoveryResponse, 12f, 0.01f);
            float splayRate = amount > 0f ? Safe(_profile.SplayResponse, 18f, 0.01f) : Safe(_profile.SplayRecoveryResponse, 10f, 0.01f);
            _bend = Vector3.LerpUnclamped(_bend, target, 1f - Mathf.Exp(-bendRate * deltaTime));
            float targetSplay = amount * Mathf.Clamp01(Safe(_profile.MaximumSplay, 0.65f, 0f));
            _splay = Mathf.LerpUnclamped(_splay, targetSplay, 1f - Mathf.Exp(-splayRate * deltaTime));
            BuildPose();
            return Pose;
        }

        /// <summary>
        /// #308 juice (SPEC-ANIM-JUICE-308 A-1): 이번 프레임 자세의 벌어짐에만 더한다. 상태(_splay)는 건드리지 않으므로 다음 Evaluate가
        /// 원래 값에서 다시 만든다(쌓이지 않는다). 벌어짐은 중심선 끝점을 보존한다(ApplyPose 주석) — 붓끝은 움직이지 않는다.
        /// Evaluate와 ApplyPose 사이에 부른다.
        /// </summary>
        public void AddPresentationSplay(float add)
        {
            if (!IsBound || !Pose.Valid || !Finite(add) || add <= 0f) return;
            Pose = new BrushBristlePose(Pose.TipPositionLocal, Pose.TipRotationLocal, Pose.TipOffsetInGripLocal,
                Pose.BristleArcLength, Pose.BendDegrees, Mathf.Clamp01(_splay + add));
        }

        /// <summary>IK가 붓 루트를 배치한 뒤 호출한다. root/GripSocket/본 scale은 쓰지 않는다.</summary>
        public bool ApplyPose()
        {
            if (!IsBound || !Pose.Valid || _tipSocket == null || !UnitScale(transform.lossyScale)) return false;
            for (int i = 0; i < DeformationBoneCount; i++) if (_bones[i] == null) return false;
            for (int i = 0; i < DeformationBoneCount; i++)
                if (!SetPoseInBrushFrame(_bones[i], _posePositions[i], _poseRotations[i])) return false;
            if (!SetPoseInBrushFrame(_tipSocket, Pose.TipPositionLocal, Pose.TipRotationLocal)) return false;
            // BristleSplay must only widen radial strands, preserving the root and the centreline endpoint.
            // A blendshape avoids nonuniform ancestor scale changing the six bone segment lengths.
            if (_splayIndex >= 0 && _bristleRenderer != null)
                _bristleRenderer.SetBlendShapeWeight(_splayIndex, Pose.Splay * 100f);
            return true;
        }

        private bool SetPoseInBrushFrame(Transform target, Vector3 position, Quaternion rotation)
        {
            // Avoid TransformPoint -> world SetPosition: at x=2067m a float step is .244mm,
            // which was being written back into the rigid ferrule's local position each pose.
            // Compose the CURRENT parent chain, including every scale, entirely inside the brush.
            Matrix4x4 parentToBrush = Matrix4x4.identity;
            Quaternion parentRotation = Quaternion.identity;
            for (Transform parent = target.parent; parent != transform; parent = parent.parent)
            {
                if (parent == null) return false;
                parentToBrush = Matrix4x4.TRS(parent.localPosition, parent.localRotation, parent.localScale) * parentToBrush;
                parentRotation = parent.localRotation * parentRotation;
            }
            if (!Finite(parentToBrush.determinant) || Mathf.Abs(parentToBrush.determinant) < .000000000001f) return false;
            Vector3 localPosition = parentToBrush.inverse.MultiplyPoint3x4(position);
            if (!Finite(localPosition)) return false;
            target.localPosition = localPosition;
            target.localRotation = Quaternion.Inverse(parentRotation) * rotation;
            return true;
        }

        public void ResetDeformation(bool applyImmediately = true)
        {
            _bend = Vector3.zero; _splay = 0f;
            if (!IsBound) return;
            BuildPose();
            if (applyImmediately) ApplyPose();
        }

        private void BuildPose()
        {
            float angle = _bend.magnitude;
            Vector3 rotationAxis = angle > 0.00001f ? Vector3.Cross(_restAxis, _bend / angle).normalized : Vector3.right;
            Vector3 position = _restPositions[0];
            float distance = 0f;
            Quaternion bend = Quaternion.identity;
            float power = Mathf.Clamp(Safe(_profile.BendDistributionPower, 1.7f, 1f), 1f, 4f);
            for (int i = 0; i < DeformationBoneCount; i++)
            {
                // The first segment stays rigid inside the ferrule. Later tangents bend by authored arc length.
                float fraction = distance / Mathf.Max(0.00001f, _arcLength - _lengths[DeformationBoneCount - 1]);
                bend = Quaternion.AngleAxis(angle * Mathf.Pow(fraction, power), rotationAxis);
                _posePositions[i] = position;
                _poseRotations[i] = bend * _restRotations[i];
                position += bend * _segments[i];
                distance += _lengths[i];
            }
            Pose = new BrushBristlePose(position, bend * _restTipRotation,
                Quaternion.Inverse(_gripRotation) * (position - _gripPosition), _arcLength, angle, _splay);
        }

        private bool Fail(string error) { BindingError = error; return false; }
        private bool WithinRoot(Transform target) => target == transform || target.IsChildOf(transform);
        private static bool UnitScale(Vector3 scale) => Finite(scale) && (scale - Vector3.one).sqrMagnitude < 0.000001f;
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static float Safe(float value, float fallback, float minimum) => Finite(value) ? Mathf.Max(minimum, value) : fallback;
    }
}
