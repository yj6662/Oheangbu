using Oheangbu.Core.Events;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Oheangbu.Combat
{
    // [실험 2026-08-27] 숄더뷰 + 작도 클로즈업 카메라 — SPEC-COMBAT-CORE-LOOP §10.1 실증.
    // 결정 3(1인칭)에 대한 실험이지 전이가 아니다 — 채택하려면 DECISIONS 문답이 먼저다.
    //
    // 상태는 2층이다: 베이스 모드(1인칭↔숄더뷰 정중앙 상단, V 토글) + 작도 오버레이(클로즈업 =
    // 살짝 우측·상단 — 추후 이 프레임에 팔·붓이 렌더된다). 작도 중 V는 무시한다 —
    // 종료 시 복귀처가 항상 결정적이어야 한다.
    // 작도면은 카메라 자식(화면 고정)이므로 마우스↔작도 1:1 매핑은 어느 포즈에서든 유지된다 —
    // 포즈 이동만으로는 글자 화면 크기가 변하지 않으므로, 클로즈업 체감은 FOV 스냅이 담당한다.
    public sealed class CameraRigController : MonoBehaviour
    {
        private const float BoomRadius = 0.25f;      // 충돌 붐 구 반경 — 기술 상수(밸런스 아님)
        private const float SnapSqrEpsilon = 1e-6f;  // 지수 수렴 도착 스냅

        [SerializeField] private CombatConfigSO _config;
        [SerializeField] private Transform _cameraPivot;      // 피치 담당(카메라 부모)
        [SerializeField] private Transform _camera;           // Main Camera
        [SerializeField] private BoolEventChannelSO _drawModeChanged;
        [SerializeField] private Renderer[] _bodyRenderers;   // 숄더뷰에서만 보이는 몸

        private Camera _cam;
        private bool _drawing;
        private bool _shoulder;
        private Vector3 _localPose;   // 붐 적용 전의 블렌드 상태(카메라 localPosition과 분리)
        private float _boom01 = 1f;   // 충돌 붐 비율 — 축소는 즉시, 복원은 점진
        private float _baseFov;

        public bool IsShoulder => _shoulder;

        // 숄더뷰는 피치 궤도가 지면·벽을 관통하므로 좁은 클램프를 쓴다 — PlayerMotor가 조회
        public void GetPitchLimits(out float min, out float max)
        {
            if (_shoulder && _config != null)
            {
                min = _config.ShoulderPitchMin;
                max = _config.ShoulderPitchMax;
            }
            else
            {
                min = -80f;
                max = 80f;
            }
        }

        private void Awake()
        {
            _cam = _camera != null ? _camera.GetComponent<Camera>() : null;
            if (_cam != null) _baseFov = _cam.fieldOfView;
        }

        private void OnEnable()
        {
            if (_drawModeChanged != null) _drawModeChanged.Subscribe(OnDrawModeChanged);
            _shoulder = _config != null && _config.ShoulderStart;
            if (_camera != null) _localPose = _camera.localPosition;
        }

        private void OnDisable()
        {
            if (_drawModeChanged != null) _drawModeChanged.Unsubscribe(OnDrawModeChanged);
        }

        private void Update()
        {
            if (_config == null || _camera == null || _cameraPivot == null) return;

            var kb = Keyboard.current; // [실험] 토글 전용 직결 — Spec §9 예외, Exit=Gameplay 액션맵 승격
            if (!_drawing && kb != null && kb.vKey.wasPressedThisFrame)
            {
                _shoulder = !_shoulder;
            }

            Vector3 target = !_shoulder ? Vector3.zero
                : _drawing ? _config.ShoulderDrawOffset
                : _config.ShoulderOffset;

            // 클로즈업 진입은 빠른 블렌드(컷은 어색 — 3차 검수), 평시·복귀는 부드럽게.
            // 감속(작도 timeScale)과 무관하게 같은 속도 — 카메라는 세상 밖의 눈이다.
            float speed = _drawing ? _config.DrawCloseupBlendSpeed : _config.CameraBlendSpeed;
            float blend = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
            _localPose = Vector3.Lerp(_localPose, target, blend);
            if ((target - _localPose).sqrMagnitude < SnapSqrEpsilon) _localPose = target;

            ApplyBoom(blend);

            // 몸 표시는 모드 신호가 아니라 「실제 카메라 오프셋 크기」로 판정한다 —
            // 블렌드·클로즈업·붐 어느 경로로 눈에 접근해도 니어클립 단면 잔상이 없다.
            // 숨김은 ShadowsOnly(그림자 유지) — 몸체는 HUD가 아니므로 화이트리스트 무관.
            float show = _config.BodyShowDistance;
            ApplyBodyVisibility(_camera.localPosition.sqrMagnitude > show * show);
        }

        // 피벗→목표 지점 SphereCast — 지면·벽에 막히면 즉시 당겨 붙이고, 풀리면 점진 복원.
        // 피벗은 플레이어 캡슐 내부라 자기 몸(시작 겹침 콜라이더)은 캐스트에서 제외된다.
        private void ApplyBoom(float blend)
        {
            float boomTarget = 1f;
            Vector3 world = _cameraPivot.TransformPoint(_localPose);
            Vector3 dir = world - _cameraPivot.position;
            float len = dir.magnitude;
            if (len > 0.001f && Physics.SphereCast(_cameraPivot.position, BoomRadius, dir / len, out RaycastHit hit, len))
            {
                boomTarget = Mathf.Clamp01(hit.distance / len);
            }
            _boom01 = boomTarget < _boom01 ? boomTarget : Mathf.Lerp(_boom01, boomTarget, blend);
            _camera.localPosition = _localPose * _boom01;
        }

        private void OnDrawModeChanged(bool drawing)
        {
            _drawing = drawing;

            // FOV는 스냅 — 작도 점이 찍히기 전에 확정돼야 마우스↔작도 1:1이 깨지지 않는다.
            // 포즈는 Update의 빠른 블렌드(진입 속도) — 컷은 어색하다는 3차 검수.
            // 블렌드 중 시작한 획의 미세한 평면 뒤틀림은 수용된 트레이드오프(Spec §12).
            // 1인칭은 대조군으로 무변경.
            if (_cam == null || _config == null) return;
            bool closeup = drawing && _shoulder && _config.DrawCloseupFov > 0f;
            _cam.fieldOfView = closeup ? _config.DrawCloseupFov : _baseFov;
        }

        private void ApplyBodyVisibility(bool show)
        {
            if (_bodyRenderers == null) return;
            foreach (var r in _bodyRenderers)
            {
                if (r != null) r.shadowCastingMode = show ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            }
        }
    }
}
