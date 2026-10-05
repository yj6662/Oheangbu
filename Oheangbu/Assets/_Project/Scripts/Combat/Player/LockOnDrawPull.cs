using UnityEngine;

namespace Oheangbu.Combat
{
    // 락온 중 작도의 카메라 당김 규칙 [TEST — D308-21 · SPEC-LOCKON-DRAW-STABILITY-308 §4.2].
    // 번호는 직렬화된다 — 0은 권장 모드여야 한다(값이 없는 에셋·0으로 읽힌 값이 권장 모드로 떨어지게).
    public enum LockOnDrawMode
    {
        EdgeOnly = 0,   // 글자를 쓰는 동안 카메라 고정, 대상이 여유 구역을 벗어날 때만 느리게 따라간다 (권장 기본)
        FullPull = 1,   // 이전 동작: 작도 중에도 계속 당긴다
        Hold = 2,       // 작도 중 당김 없음
    }

    // 순수 함수 — 입력(현재 요·피치, 대상 방향, 대상의 화면 위치, dt, 수치)만으로 다음 요·피치를 낸다.
    // 변환·카메라·시간을 읽지 않는다: 읽는 것은 PlayerMotor(호출부)이고 여기는 계산뿐이다(편집 모드 표 검사 대상).
    // 수치는 전부 Settings로 들어온다(CombatConfigSO) — 아래 두 상수는 밸런스가 아니라 기술 한계다.
    // 같은 순서의 파이썬 미러: Tools/Unity/Stage308_lockdraw/sim/lockdraw_rule.py (오프라인 모의·변이 검사).
    public static class LockOnDrawPull
    {
        public const float MaxFollowStep = 0.1f;   // 한 프레임이 길어도(멈칫) 가장자리 추적은 이 시간만큼만 움직인다
        public const float MaxMargin = 0.49f;      // 여유 0.5는 구역이 없다는 뜻이 된다

        public struct Settings
        {
            public LockOnDrawMode Mode;
            public float Pull;               // 초당 — CombatConfigSO.LockOnCameraPull
            public float SettleSeconds;      // 작도 진입: 당김이 0으로 가라앉는 시간
            public float ResumeDelay;        // 작도 종료 뒤 당김이 돌아오기 전의 멈춤
            public float ResumeSeconds;      // 당김이 0에서 온전히 돌아오는 시간
            public float EdgeMarginX, EdgeMarginY;   // 여유 구역 = 뷰포트 가장자리에서 이만큼 안쪽
            public float EdgeGain;           // 초당 — 구역 밖으로 나간 각도에 비례한 추적 속도
            public float EdgeMaxSpeed;       // 도/초 상한
            public float EdgeAcceleration;   // 도/초² — 추적 속도가 변하는 빠르기(시작·멈춤이 보이지 않게)
            public float AimKeepDegrees;     // 몸의 정면과 대상의 수평 각이 이보다 벌어지지 않게(부채꼴 술식). 0=끔
        }

        public struct State
        {
            public bool Started;
            public float Ramp;        // 0..1 — 평시 당김의 세기(1=이전과 같은 당김)
            public float Idle;        // 작도가 끝난 뒤 흐른 시간(멈춤 판정)
            public float YawSpeed;    // 가장자리 추적 각속도(도/초)
            public float PitchSpeed;
        }

        public struct Frame
        {
            public bool Drawing;
            public bool HasYaw;               // 대상이 바로 위·아래가 아니다(수평 방향이 있다)
            public float Yaw, Pitch;          // 현재 값(도) — 피치 +는 아래
            public float TargetYaw, TargetPitch;
            public bool HasViewport;          // 화면 위치를 알 수 있다(카메라 있음)
            public bool InFront;              // 대상이 카메라 앞에 있다
            public float ViewportX, ViewportY;
            public float TanHalfFovX, TanHalfFovY;
            public float DeltaTime;           // unscaled
        }

        // 이전 코드의 식 그대로 — FullPull과 평시(Ramp 1)는 이 값을 쓴다(부동소수 결과까지 같다).
        public static float LegacyBlend(float pull, float deltaTime) => 1f - Mathf.Exp(-pull * deltaTime);

        // 대상을 여유 구역 가장자리로 되돌리려면 시야가 돌아야 하는 각(도, 부호: 요 +오른쪽 · 피치 +아래). 구역 안이면 0.
        public static void Excess(in Settings c, in Frame f, float yaw, float pitch, out float yawExcess, out float pitchExcess)
        {
            float yawError = f.HasYaw ? Mathf.DeltaAngle(yaw, f.TargetYaw) : 0f;
            float pitchError = Mathf.DeltaAngle(pitch, f.TargetPitch);
            yawExcess = 0f; pitchExcess = 0f;
            if (f.HasViewport && f.TanHalfFovX > 0f && f.TanHalfFovY > 0f)
            {
                if (f.InFront)
                {
                    float mx = Mathf.Clamp(c.EdgeMarginX, 0f, MaxMargin), my = Mathf.Clamp(c.EdgeMarginY, 0f, MaxMargin);
                    float ax = Mathf.Atan((2f * f.ViewportX - 1f) * f.TanHalfFovX) * Mathf.Rad2Deg;
                    float limitX = Mathf.Atan((1f - 2f * mx) * f.TanHalfFovX) * Mathf.Rad2Deg;
                    if (ax > limitX) yawExcess = ax - limitX;
                    else if (ax < -limitX) yawExcess = ax + limitX;
                    float ay = Mathf.Atan((2f * f.ViewportY - 1f) * f.TanHalfFovY) * Mathf.Rad2Deg;
                    float limitY = Mathf.Atan((1f - 2f * my) * f.TanHalfFovY) * Mathf.Rad2Deg;
                    if (ay > limitY) pitchExcess = -(ay - limitY);
                    else if (ay < -limitY) pitchExcess = -(ay + limitY);
                }
                else
                {
                    // 카메라 뒤: 화면 좌표가 뒤집힌다 — 방위 오차 자체를 쓴다
                    yawExcess = yawError; pitchExcess = pitchError;
                }
            }
            if (c.AimKeepDegrees > 0f)
            {
                float over = Mathf.Abs(yawError) - c.AimKeepDegrees;
                if (over > 0f && over > Mathf.Abs(yawExcess)) yawExcess = yawError < 0f ? -over : over;
            }
        }

        // 한 프레임. 피치 한계는 호출부가 건다(리그 소유). Pull이 0이면 아무것도 하지 않는다(이전과 같다).
        public static void Step(ref State state, in Settings c, in Frame f, out float yaw, out float pitch)
        {
            yaw = f.Yaw; pitch = f.Pitch;
            float dt = f.DeltaTime > 0f ? f.DeltaTime : 0f;
            if (c.Pull <= 0f) { state = default; return; }

            bool reduce = f.Drawing && c.Mode != LockOnDrawMode.FullPull;
            if (!state.Started)
            {
                // 작도 도중에 락온을 걸면 당김이 시작되지 않는다(0에서 출발). 평시에 걸면 이전처럼 곧바로 당긴다.
                state = default; state.Started = true; state.Ramp = reduce ? 0f : 1f;
            }

            // 세기(Ramp)는 시간에 선형으로 움직이고 가중은 그 값의 완화 곡선이다. 한 프레임을 [멈춤 wait] → [이동 move] → [나머지]로
            // 나눠 가중의 시간 적분을 정확히 구한다 — 프레임 경계가 어디에 놓여도 같은 시간에 같은 만큼 당긴다.
            float before = state.Ramp, wait = 0f, move = 0f;
            if (reduce)
            {
                state.Idle = 0f;
                if (state.Ramp > 0f)
                {
                    if (c.SettleSeconds > 0f)
                    {
                        float need = state.Ramp * c.SettleSeconds; move = dt < need ? dt : need;
                        state.Ramp = move >= need ? 0f : state.Ramp - move / c.SettleSeconds;
                    }
                    else state.Ramp = 0f;
                }
            }
            else if (state.Ramp < 1f)
            {
                float idleBefore = state.Idle; state.Idle += dt;
                wait = Mathf.Min(Mathf.Max(c.ResumeDelay - idleBefore, 0f), dt);
                float left = dt - wait;
                if (left > 0f)
                {
                    if (c.ResumeSeconds > 0f)
                    {
                        float need = (1f - state.Ramp) * c.ResumeSeconds; move = left < need ? left : need;
                        state.Ramp = move >= need ? 1f : state.Ramp + move / c.ResumeSeconds;
                    }
                    else state.Ramp = 1f;
                }
            }

            float blend;
            if (before >= 1f && state.Ramp >= 1f) blend = LegacyBlend(c.Pull, dt);   // 평시·FullPull: 이전 식 그대로
            else
            {
                float span = reduce ? c.SettleSeconds : c.ResumeSeconds;
                float area = Smooth01(before) * wait + span * Mathf.Abs(Area01(state.Ramp) - Area01(before)) + Smooth01(state.Ramp) * (dt - wait - move);
                blend = area > 0f ? 1f - Mathf.Exp(-c.Pull * area) : 0f;
            }
            if (blend > 0f)
            {
                if (f.HasYaw) yaw = Mathf.LerpAngle(yaw, f.TargetYaw, blend);
                pitch = Mathf.LerpAngle(pitch, f.TargetPitch, blend);
            }

            float wantYaw = 0f, wantPitch = 0f;
            if (f.Drawing && c.Mode == LockOnDrawMode.EdgeOnly)
            {
                Excess(in c, in f, yaw, pitch, out float ex, out float ey);
                // 음수로 적힌 값은 0으로 읽는다 — 대상 반대쪽으로 도는 일은 없다
                float cap = c.EdgeMaxSpeed > 0f ? c.EdgeMaxSpeed : 0f, gain = c.EdgeGain > 0f ? c.EdgeGain : 0f;
                // 가라앉는 당김이 남아 있는 동안에는 그 몫만큼 추적을 덜어 낸다(두 몫의 합 = 1) — 구역 밖에서 쓰기 시작해도
                // 당김과 추적이 겹쳐 이전보다 빨리 도는 일이 없다. 가라앉은 뒤(Ramp 0)와 작도 도중에 건 락온에서는 share = 1.
                float share = 1f - Smooth01(state.Ramp);
                if (ex != 0f) { float s = Mathf.Min(cap, gain * Mathf.Abs(ex)) * share; wantYaw = ex < 0f ? -s : s; }
                if (ey != 0f) { float s = Mathf.Min(cap, gain * Mathf.Abs(ey)) * share; wantPitch = ey < 0f ? -s : s; }
            }
            if (wantYaw != 0f || wantPitch != 0f || state.YawSpeed != 0f || state.PitchSpeed != 0f)
            {
                float step = dt < MaxFollowStep ? dt : MaxFollowStep;
                float change = (c.EdgeAcceleration > 0f ? c.EdgeAcceleration : 0f) * step;
                state.YawSpeed = Mathf.MoveTowards(state.YawSpeed, wantYaw, change);
                state.PitchSpeed = Mathf.MoveTowards(state.PitchSpeed, wantPitch, change);
                if (state.YawSpeed != 0f && f.HasYaw) yaw += state.YawSpeed * step;
                if (state.PitchSpeed != 0f) pitch += state.PitchSpeed * step;
            }
        }

        private static float Smooth01(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3f - 2f * u);
        }

        // Smooth01의 0..u 적분 (u³ − u⁴/2)
        private static float Area01(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * u * (1f - 0.5f * u);
        }
    }
}
