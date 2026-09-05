using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    // [P4 — 오(水)·모(土) 공용 전진 연출] 커밋 문양 아래 지면에서 형성돼(솟음) 목표 방향으로
    // 느리게 전진하고, 사거리 끝에서 지면으로 가라앉는다(潤下 — 잔존·발광 없음).
    // 기점=시전자 전방(문답 확정 2026-08-29 — §3.1 개정: 소환감 문법 통일).
    // 모(土) 전용 — 오는 WaterWaveEffect(전용 구현, SPEC-SPELL-FX-REWORK §3.3)로 이관. _crest 경로는 LEGACY(참조 0).
    // 피해는 배선의 단일 착탄 시계 그대로 — 연출≠실판정(SPEC-SPELL-FX-ASSETS §9-1).
    public sealed class GroundWaveEffect : SpellSequenceEffect
    {
        [SerializeField] private Transform _crest;        // 실체 메시 자식(모=비움) — 높이+Z·전진+Y 규약(§4.2-6)
        [SerializeField] private Vector3 _crestExtraEuler; // 반입 메시 축 미세 보정(검수용 — 리컴파일 없이)
        [SerializeField] private float _groundDrop = 1.2f; // 문양(가슴 높이)→지면 근사 낙차 [TEST]
        [SerializeField] private float _riseTime = 0.45f;  // 형성 — 물이 느긋하게 차오른다
        [SerializeField] private float _buriedDepth = 1.8f; // 형성 시작 매장 깊이(m)
        [SerializeField] private float _speed = 6f;        // 전진 속도 — 「느린 파도」
        [SerializeField] private float _overshoot = 2.5f;  // 목표 지점을 지나쳐 훑는 여분 — 경로 타격의 읽기
        [SerializeField] private float _maxRange = 14f;
        [SerializeField] private float _sinkTime = 0.5f;   // 끝 — 지면으로 스며 사라진다
        [SerializeField] private float _bobAmplitude = 0.07f;
        [SerializeField] private float _bobFrequency = 2.6f;

        private enum Phase { Rise, Advance, Sink, Done }

        private readonly List<Material> _ownedMaterials = new List<Material>();
        private Phase _phase = Phase.Done;
        private float _phaseStart;
        private Vector3 _dir;
        private float _travelDistance;
        private float _traveled;
        private Quaternion _crestBaseRot;
        private bool _emissionStopped;
        private AreaImpactPlan _plan;

        // 판정 계획 [SPELL-AREA-SHAPES §3] — 시작점·방향·길이·속도·차오름을 판정과 공유한다(직렬 값은 폴백)
        public override void SetAreaPlan(AreaImpactPlan plan)
        {
            _plan = plan;
        }

        public override void Begin(Vector3 origin, Transform target, Vector3 fallbackPoint, Color tint)
        {
            transform.SetParent(null, true);
            transform.localScale = Vector3.one; // 글자 크기 비의존 — 파도 실크기는 프리팹 자식 스케일이 정본(소 문법)

            // 기점 = 시전자(문양) 아래 지면 근사 — 파도는 내 앞에서 태어나 목표를 향해 밀려간다
            Vector3 goal = target != null ? target.position : fallbackPoint;
            Vector3 start = origin;
            start.y = target != null ? target.position.y : origin.y - _groundDrop;
            transform.position = start;

            Vector3 flat = goal - start;
            flat.y = 0f;
            float distToGoal = flat.magnitude;
            _dir = flat.sqrMagnitude > 0.001f ? flat.normalized : Vector3.forward;
            _travelDistance = Mathf.Min(_maxRange, distToGoal + _overshoot);

            if (_plan != null)
            {
                // 판정이 정한 복도 그대로 — 전선이 닿는 시각(차오름 + 거리/속도)이 피해 시각과 같아진다
                Vector3 planDir = new Vector3(_plan.Direction.x, 0f, _plan.Direction.z);
                if (planDir.sqrMagnitude > 0.001f) _dir = planDir.normalized;
                if (_plan.Length > 0f) _travelDistance = _plan.Length;
                if (_plan.Speed > 0f) _speed = _plan.Speed;
                if (_plan.Delay > 0f) _riseTime = _plan.Delay;
                transform.position = new Vector3(_plan.Point.x, start.y, _plan.Point.z);
            }

            // crest: 높이+Z·전진+Y 메시를 세워 진행 방향으로 — LookRotation(up, dir)이 +Z→up·+Y→dir
            _crestBaseRot = Quaternion.LookRotation(Vector3.up, _dir) * Quaternion.Euler(_crestExtraEuler);
            if (_crest != null)
            {
                _crest.rotation = _crestBaseRot;
                _crest.position = transform.position - Vector3.up * _buriedDepth;
            }

            PatternEffectLifetime.TintHierarchy(gameObject, tint, _ownedMaterials);
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Simulate(0f, true, true);
                ps.Play(true);
            }

            _phase = Phase.Rise;
            _phaseStart = Time.time;
        }

        private void Update()
        {
            if (_phase == Phase.Done) return;
            float t = Time.time - _phaseStart;

            switch (_phase)
            {
                case Phase.Rise: // 지면 아래에서 차오른다 — 潤下의 느긋한 형성
                {
                    float k = Mathf.Clamp01(t / Mathf.Max(0.01f, _riseTime));
                    float ease = Mathf.SmoothStep(0f, 1f, k);
                    SetCrestPos(Vector3.up * (_buriedDepth * (ease - 1f)));
                    if (k >= 1f) { _phase = Phase.Advance; _phaseStart = Time.time; }
                    break;
                }
                case Phase.Advance: // 느리게 밀려간다 — 낮은 봅으로 물결의 숨
                {
                    float step = _speed * Time.deltaTime;
                    transform.position += _dir * step;
                    _traveled += step;
                    SetCrestPos(Vector3.up * (Mathf.Sin(t * _bobFrequency) * _bobAmplitude));
                    if (_traveled >= _travelDistance) { _phase = Phase.Sink; _phaseStart = Time.time; }
                    break;
                }
                case Phase.Sink: // 끝에서 스며 사라진다 — 남는 것 없음(잔존·발광 금지)
                {
                    float k = Mathf.Clamp01(t / Mathf.Max(0.01f, _sinkTime));
                    SetCrestPos(-Vector3.up * (_buriedDepth * k * k));
                    if (!_emissionStopped)
                    {
                        _emissionStopped = true;
                        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
                        {
                            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); // 잔여 입자 자연 소멸
                        }
                    }
                    if (k >= 1f)
                    {
                        _phase = Phase.Done;
                        Destroy(gameObject, 1.2f); // 파티클 여운 뒤 정리
                    }
                    break;
                }
            }
        }

        private void SetCrestPos(Vector3 offset)
        {
            if (_crest == null) return;
            _crest.rotation = _crestBaseRot;
            _crest.position = transform.position + offset;
        }

        private void OnDestroy()
        {
            foreach (var material in _ownedMaterials)
            {
                if (material != null) Destroy(material);
            }
        }
    }
}
