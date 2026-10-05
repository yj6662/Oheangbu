using UnityEngine;

namespace Oheangbu.Combat
{
    // #308 락온 중 작도 안정 (SPEC-LOCKON-DRAW-STABILITY-308 §4.2, D308-21): ApplyLockOnPull이 부르는 규칙의 상태와 입력 수집.
    // 규칙 자체는 순수 함수 LockOnDrawPull.Step이고 수치는 CombatConfigSO다. 여기는 변환·카메라를 읽어 넘기기만 한다.
    // 인식 불가침: 이 파일은 작도 입력(DrawingInputController)을 읽지도 쓰지도 않는다 — 아는 것은 _drawing(모드 채널)뿐이다.
    // 정적 필드 없음(도메인 리로드 비활성 대비 — 상태는 전부 인스턴스에 있다).
    public sealed partial class PlayerMotor
    {
        private LockOnDrawPull.State _lockDraw;
        private Camera _lockDrawCamera;

        /// <summary>검사 도구 전용(직렬화하지 않는다): 0 이상이면 설정 에셋의 모드 대신 이 값을 쓴다. -1 = 에셋 값.
        /// Play가 끝나면 씬 인스턴스와 함께 사라진다 — 에셋을 건드리지 않고 세 모드를 견주기 위한 문이다.</summary>
        [System.NonSerialized] public int LockOnDrawModeOverride308 = -1;

        /// <summary>지금 적용되는 작도 중 당김 모드(재정의 포함).</summary>
        public LockOnDrawMode LockOnDrawModeNow => LockOnDrawModeOverride308 >= 0 ? (LockOnDrawMode)LockOnDrawModeOverride308
            : _config != null ? _config.LockOnDrawMode : LockOnDrawMode.EdgeOnly;
        /// <summary>읽기 전용 계측: 평시 당김의 세기 0..1(1 = 이전과 같은 당김).</summary>
        public float LockDrawRamp308 => _lockDraw.Started ? _lockDraw.Ramp : 1f;
        /// <summary>읽기 전용 계측: 가장자리 추적 각속도(도/초, x=요 · y=피치).</summary>
        public Vector2 LockDrawFollowSpeed308 => new Vector2(_lockDraw.YawSpeed, _lockDraw.PitchSpeed);

        // 락온이 풀리면 규칙 상태도 처음으로 — 다음 락온은 Started=false에서 다시 시작한다
        private void ResetLockDraw308() { _lockDraw = default; }

        // 대상의 화면 위치를 재는 카메라 = 피벗 아래의 카메라(리그 구조: Player/CameraPivot/Main Camera). 전역 탐색을 쓰지 않는다.
        // 탑승 중에는 카메라가 피벗에서 떨어지지만 그때는 모터가 꺼져 있다(WorldMacroCombatWalker.Suspend).
        private Camera LockDrawCamera308()
        {
            if (_lockDrawCamera == null || !_lockDrawCamera.transform.IsChildOf(_cameraPivot))
                _lockDrawCamera = _cameraPivot != null ? _cameraPivot.GetComponentInChildren<Camera>(true) : null;
            return _lockDrawCamera;
        }

        // 규칙 한 프레임. to = 피벗에서 조준점(대상 뿌리 + 1.1 m)까지, flatValid = 수평 방향이 있음(이전 코드의 조건 그대로).
        private void StepLockDraw308(Vector3 aimPoint, Vector3 to, bool flatValid, float pull)
        {
            var settings = _config.GetLockOnDrawSettings(pull, LockOnDrawModeNow);
            var frame = new LockOnDrawPull.Frame
            {
                Drawing = _drawing,
                HasYaw = flatValid,
                Yaw = transform.eulerAngles.y,
                Pitch = _pitch,
                TargetYaw = flatValid ? Quaternion.LookRotation(new Vector3(to.x, 0f, to.z)).eulerAngles.y : 0f,
                TargetPitch = -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) * Mathf.Rad2Deg,
                DeltaTime = Time.unscaledDeltaTime,
            };
            // 화면 위치는 가장자리 추적에만 쓴다 — 다른 모드·평시에는 재지 않는다
            if (_drawing && settings.Mode == LockOnDrawMode.EdgeOnly)
            {
                Camera camera = LockDrawCamera308();
                if (camera != null && !camera.orthographic)
                {
                    Vector3 viewport = camera.WorldToViewportPoint(aimPoint);
                    frame.HasViewport = true;
                    frame.InFront = viewport.z > 0f;
                    frame.ViewportX = viewport.x; frame.ViewportY = viewport.y;
                    frame.TanHalfFovY = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    frame.TanHalfFovX = frame.TanHalfFovY * camera.aspect;
                }
            }
            LockOnDrawPull.Step(ref _lockDraw, in settings, in frame, out float yaw, out float pitch);
            if (flatValid) transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            _pitch = ClampPitch(pitch);
        }
    }
}
