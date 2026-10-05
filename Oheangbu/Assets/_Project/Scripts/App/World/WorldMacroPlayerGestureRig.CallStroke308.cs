using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 D308-8c (SPEC-VEHICLE-UX-308 §3b) [TEST]: 자동차 호출 획 — 손에 든 붓으로 몸 앞 허공에 짧고 결단 있는 획 하나를 긋는다.
    // 표현 전용이다. 세계 팔·몸통·손가락·붓(쌍구 펜 v9 쥐기 그대로, 자루 쪽 0.32 m)만 움직인다. 작도 모드·근접 팔·카메라·입력·
    // 인식기·필세 계산은 읽지도 쓰지도 않는다(DrawingInputController·BrushStrokeFeedAdapter를 부르지 않는다).
    //   · 획 경로는 작도면 좌표(DrawingWorldTarget과 같은 몸 기준 평면, ±1 = WorldDrawingHalfSize)로 받는다. 팔꿈치·손목·붓털은
    //     작도 때와 같은 풀이(PoseTowardTip → VerticalPole 팔꿈치 규칙, BlendedHand 쌍구 손, EvaluateVerticalWorldBristles)를 쓴다.
    //     그래서 붓이 오른쪽이면 팔꿈치도 오른쪽, 왼쪽으로 건너가면 팔꿈치가 내려간다(붓 쥐기 규칙).
    //   · 손과 붓은 함께 오른다: 붓은 매 프레임 손에 다시 놓이고(PlaceBrush), 무게가 0 → 1로 오르는 동안 손이 들고 다니던 쥐기에서
    //     작도 쥐기로 넘어간다(VerticalBlend = 무게). 붓이 손보다 먼저 나타나지 않는다(세계 붓은 늘 손에 있다).
    //   · 획 목표는 작도처럼 컨트롤러 기준(카메라·차를 부르는 앞쪽)이다. 서서 둘러보기(WorldMacroPlayerAppearance 제자리 돌기)로 보이는
    //     몸이 최대 90° 어긋나 있을 수 있으므로, 획 동안 외형이 몸을 컨트롤러 앞으로 BodyTurnSpeed(°/s)로 돌린다(AirStrokeBodyTurnSpeed308).
    //   · 진행 0..1은 호출자(WorldMacroPalanquinSummon)가 넣는다(오행부 손동작과 같은 계약). 끝나면 무게가 작도 퇴장 응답
    //     (ExitResponse)으로 줄어 팔이 들고 다니는 자세로 부드럽게 돌아온다. 중간 취소(피격)도 같은 길로 돌아온다.
    // 정적 가변 필드 없음.
    public sealed partial class WorldMacroPlayerGestureRig
    {
        /// <summary>#308 D308-8c: one presentation-only stroke in the air. Plane coordinates are the world drawing plane's centred units.</summary>
        public struct AirStroke308
        {
            /// <summary>Path start / end on the world drawing plane (x right, y up; ±1 = WorldDrawingHalfSize; beyond 1 is allowed).</summary>
            public Vector2 From, To;
            /// <summary>Bulge as a share of the path length (+ = to the left of the travel direction, sin profile).</summary>
            public float Arc;
            /// <summary>Forward push of the tip toward the far end of the path (m): the stroke thrusts toward the summon side.</summary>
            public float Reach;
            /// <summary>Progress where the raised brush touches the air, and where it leaves it (the call moment).</summary>
            public float RaiseEnd, ContactEnd;
            /// <summary>Contact time 0..1 → path 0..1 (null = smoothstep).</summary>
            public AnimationCurve Ease;
            /// <summary>Recall: the same path drawn backwards (it starts extended and withdraws toward the body).</summary>
            public bool Reverse;
            /// <summary>Torso share (chest yaw / lean, shoulder) while the stroke is held, 0..1.</summary>
            public float BodyParticipation;
            /// <summary>Degrees per second at which the visual body turns to face the controller's forward while the stroke runs
            /// (WorldMacroPlayerAppearance idle look-turn can leave it up to 90° off; the stroke target is in the controller frame).
            /// 0 = no turn.</summary>
            public float BodyTurnSpeed;
            /// <summary>#308 juice C-1: seconds the whole stroke takes as the caller counts its progress (0 = unknown: no overshoot
            /// after the call moment). Presentation only.</summary>
            public float Seconds;
        }

        private bool _airActive308, _airContact308, _airHasLast308;
        private float _airProgress308;
        private AirStroke308 _air308;
        private Vector2 _airLastPoint308;

        /// <summary>#308 D308-8c: a call stroke is being presented (world arm + brush).</summary>
        public bool AirStrokeActive308 => _airActive308;
        /// <summary>#308 D308-8c: the brush tip is on the stroke path this frame (the ink trail records only then).</summary>
        public bool AirStrokeContact308 => _airActive308 && _airContact308;
        public float AirStrokeProgress308 => _airProgress308;
        /// <summary>#308 D308-8c: while a stroke runs, the turn speed (deg/s) the appearance uses to face the visual body along the
        /// controller (where the stroke is drawn and the car is called); 0 when no stroke runs.</summary>
        public float AirStrokeBodyTurnSpeed308 => _airActive308 ? Mathf.Max(0f, _air308.BodyTurnSpeed) : 0f;
        /// <summary>#308 D308-8c: world tip target of the last evaluated stroke frame (diagnostic; the real tip is WorldTip).</summary>
        public Vector3 AirStrokeTarget308 { get; private set; }
        /// <summary>#308 D308-8c: strokes begun (diagnostic).</summary>
        public int AirStrokes308 { get; private set; }
        public bool CanPresentAirStroke308 => IsPresentationReady && _world != null && _world.Valid && _worldBrush != null && _profile != null;

        public bool BeginAirStroke308(AirStroke308 shape)
        {
            if (!CanPresentAirStroke308 || _airActive308 || _pendantActive) return false;
            RestoreAnimatedPose();
            EndCast();   // a spell follow-through still running hands the arm over (the close-up arm is not used here)
            shape.RaiseEnd = Mathf.Clamp(shape.RaiseEnd, .05f, .9f);
            shape.ContactEnd = Mathf.Clamp(shape.ContactEnd, shape.RaiseEnd + .05f, .98f);
            _air308 = shape;
            _airActive308 = true; _airContact308 = false; _airHasLast308 = false; _airProgress308 = 0f;
            AirStrokes308++;
            JuiceCallBegan308();
            SetNearVisible(false);
            return true;
        }

        public void SetAirStrokeProgress308(float progress) { _airProgress308 = Mathf.Clamp01(progress); }

        /// <summary>Ends the stroke. The arm returns to the carry pose over the next frames through the drawing-weight decay.</summary>
        public void EndAirStroke308()
        {
            if (!_airActive308) return;
            RestoreAnimatedPose();
            _airActive308 = false; _airContact308 = false; _airHasLast308 = false;
        }

        // Called from EvaluateGesturePose after the animated pose is saved and the arm lengths are measured (like the pendant).
        private void ApplyAirStrokePose308(float dt)
        {
            float t = _airProgress308, raiseEnd = _air308.RaiseEnd, contactEnd = _air308.ContactEnd;
            // hand + brush rise together to the start, the weight is held through the stroke, then the arm lowers
            float weight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / raiseEnd))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(contactEnd, 1f, t)));
            float s = Mathf.InverseLerp(raiseEnd, contactEnd, t);
            float u = _air308.Ease != null && _air308.Ease.length > 0 ? Mathf.Clamp01(_air308.Ease.Evaluate(s)) : Mathf.SmoothStep(0f, 1f, s);
            bool contact = t >= raiseEnd && t <= contactEnd;
            float path = _air308.Reverse ? 1f - u : u;
            // #308 juice C-1 / C-3 (SPEC-ANIM-JUICE-308): after the call moment the tip runs a little past the stroke's end and the arm
            // stays extended before it lowers; a refused call sinks. Inside the contact span, and without a profile, nothing changes.
            float droop308 = 0f;
            JuiceAirStroke308(t, raiseEnd, contactEnd, ref weight, ref path, ref droop308);
            Vector2 span = _air308.To - _air308.From;
            Vector2 side = span.sqrMagnitude > .000001f ? new Vector2(-span.y, span.x).normalized : Vector2.up;
            Vector2 point = _air308.From + span * path + side * (Mathf.Sin(path * Mathf.PI) * _air308.Arc * span.magnitude);

            // the stroke drives the same state the drawing pose reads: grasp blend (VerticalBlend, plane distance), plane point, sweep lean
            _drawWeight = weight;
            _bodyPoint = point;
            Vector2 velocity2 = _airHasLast308 && dt > .00001f ? (point - _airLastPoint308) / dt : Vector2.zero;
            Vector3 velocity = new Vector3(velocity2.x, velocity2.y, 0f);   // centred plane units / s, as the drawing path feeds the bristles
            _frameSweep = Vector2.ClampMagnitude(velocity2 * .5f, 1f);
            Transform basis = _controller != null ? _controller.transform : _world.Root;
            Vector3 target = DrawingWorldTarget(point, !contact) + basis.forward * (_air308.Reach * path);
            if (droop308 != 0f) target -= basis.up * droop308;
            if (_air308.BodyParticipation > 0f) ApplyTorso(Mathf.Clamp01(_air308.BodyParticipation) * weight);
            PoseCarry();
            if (_profile.ShuanggouGrip) EvaluateVerticalWorldBristles(target, velocity, contact, dt);
            else EvaluateBristles(_worldBrush, velocity, _world.Root, target - _world.Upper.position, contact, dt);
            if (weight > .001f)
                _diagnostics.WorldWristTarget = PoseTowardTip(_world, _worldBrush, target, _world.Root, weight, _profile.DrawingBrushRoll);
            ApplyFingers();
            PlaceBrush(_world, _worldBrush);
            if (_worldBrush.Bristles != null) _worldBrush.Bristles.ApplyPose();

            SetNearVisible(false);
            _cameraRig?.SetDrawingPresentationActive(false);
            _effectTip.SetPositionAndRotation(_worldBrush.Tip.position, _worldBrush.Tip.rotation);
            _diagnostics.EffectTipWorld = _effectTip.position;
            _diagnostics.DrawingWeight = weight;
            _diagnostics.State = GestureState.Suspended;
            AirStrokeTarget308 = target;
            _airContact308 = contact; _airLastPoint308 = point; _airHasLast308 = true;
        }
    }
}
