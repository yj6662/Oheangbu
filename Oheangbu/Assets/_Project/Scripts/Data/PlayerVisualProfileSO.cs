using UnityEngine;

namespace Oheangbu.Data
{
    [CreateAssetMenu(menuName = "Oheangbu/Presentation/Player Visual Profile")]
    public sealed class PlayerVisualProfileSO : ScriptableObject
    {
        public RuntimeAnimatorController Controller;
        [Min(0.1f)] public float ModelHeight = 1.75f;
        [Min(0.1f)] public float WalkClipSpeed = 1.6f;
        [Min(0.1f)] public float RunClipSpeed = 4.5f;
        // F/B/L/R: 파생 FBX 접지 구간의 실제 발 이동 속도. 원본 보존 후 컨트롤러에서 시간만 보정한다.
        public Vector4 WalkSourceSpeeds = new Vector4(1.3236568f, 1.0345914f, 1.1931836f, 1.3375752f);
        public Vector4 RunSourceSpeeds = new Vector4(4.0614146f, 1.9534202f, 1.9842070f, 2.3030790f);
        [Min(0f)] public float MotionDamping = 12f;
        [Min(0f)] public float FeetWeight = 0.8f;
        [Range(0f, .25f)] public float MaxPelvisLowering = .25f;
        [Min(0.01f)] public float FootRayUp = 0.28f;
        [Min(0.01f)] public float FootRayDown = 0.5f;
        [Min(0f)] public float FootSoleOffset = 0.005f;
        [Min(0f)] public float MaxFootCorrection = 0.14f;
        [Min(0.01f)] public float FootContactHeight = 0.075f;
        public LayerMask GroundMask = ~0;
        public Vector3 NearRootCameraOffset = new Vector3(0f, -1.45f, -0.05f);
        public Vector2 RestTipViewport = new Vector2(0.72f, 0.32f);
        [Min(0.05f)] public float RestTipDepth = 0.5f;
        [Min(0.005f)] public float NearClipPlane = 0.01f;
        public bool RelaxedBrushCarry;
        public Vector3 CarryWristOffset = new Vector3(0.08f, -0.44f, 0.035f);
        public Vector3 CarryBrushDirection = new Vector3(0.16f, -0.72f, 0.68f);
        public Vector3 CarryElbowPole = new Vector3(0.12f, -0.2f, -0.12f);
        [Range(-180f, 180f)] public float CarryBrushRoll = -90f;
        [Range(0f, 1f)] public float CarryGaitInfluence = 0.2f;
        [Min(0f)] public float CarryMaximumSwing = 0.045f;
    }
}
