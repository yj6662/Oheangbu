using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.Combat
{
    // 패링 판정 — COMBAT-PARRY의 코드화 + 작도 잔존 방어막 [TEST · 2차 플레이 검수 2026-08-28].
    //
    // 전이 실험(§10.1): 창의 기준이 「임팩트 직전(판정점=글자 완성)」에서 「완성 후 잔존」으로 바뀐다.
    //   패링 글자 완성 = 방어막 생성 → 임팩트가 앞 구간(ParryWindow) 안이면 기존 3단 판정
    //   (상극=성공 · 상생=완전실패 · 나머지=반성공 — 정본표는 ElementRelations로 유지),
    //   그 뒤 구간(GuardDuration까지)은 일반 방어(경감·그로기/환급 없음). 판정점 = 임팩트 시각.
    // 채택 시 DECISIONS 문답 + 어휘 CSV(허공 시전 잔존 없음) 정본 반영이 선행돼야 한다.
    //
    // 시간축은 scaled(Time.time) — 감속 중엔 방어막도 함께 늘어난다(작도할 시간을 주는 감속의 목적).
    public enum ParryOutcome
    {
        None,    // 방어막 없음/만료 — 그대로 맞는다
        Success, // 앞 구간 상극 = 피해 무효 + 그로기 + 먹 순증. 방어막을 소비한다
        Half,    // 앞 구간 비상극·비상생 = 피해 경감
        Fail,    // 앞 구간 상생 = 완전 실패(공격을 生하는 글자)
        Block    // 뒤 구간 = 일반 방어(경감만 — 속성 무관)
    }

    public sealed class ParryJudge
    {
        private readonly CombatConfigSO _config;
        private Element _guardElement;
        private float _guardStart = float.NegativeInfinity;
        private bool _hasGuard;

        // 임팩트가 방어막에 닿았을 때(None 제외) — 배선부가 성공 보상(그로기·먹·이펙트)을 이행한다.
        // 방어막 속성·접점은 표현(접점 버스트)에 필요한 문맥 — 판정 규칙에는 불개입
        public event System.Action<ParryOutcome, Element, Vector3> ImpactResolved;

        public ParryJudge(CombatConfigSO config)
        {
            _config = config;
        }

        // 글자 완성 = 방어막 생성. 새 방어막은 이전 것을 대체한다(동시 1장) [TEST]
        public void RaiseGuard(Element element, float now)
        {
            _guardElement = element;
            _guardStart = now;
            _hasGuard = true;
        }

        // 판정점 = 임팩트 시각. 무속성 공격은 호출하지 않는다 — 무속성=회피만이 답(COMBAT-DEFENSE 이원법).
        // impactPoint = 투사체가 방어막과 만나는 지점(호출자가 계산) — 이벤트로 표현 계층에 전달만 한다
        public ParryOutcome ResolveImpact(Element attackElement, float now, Vector3 impactPoint)
        {
            float parryWindow = _config != null ? _config.ParryWindow : 0.9f;
            float duration = _config != null ? _config.GuardDuration : 4f;

            ParryOutcome outcome = ParryOutcome.None;
            if (_hasGuard && now <= _guardStart + duration)
            {
                if (now <= _guardStart + parryWindow)
                {
                    if (ElementRelations.Overcomes(_guardElement, attackElement)) outcome = ParryOutcome.Success;
                    else if (ElementRelations.Generates(_guardElement, attackElement)) outcome = ParryOutcome.Fail;
                    else outcome = ParryOutcome.Half;

                    if (outcome == ParryOutcome.Success) _hasGuard = false; // 성공은 방어막을 소비한다
                }
                else
                {
                    outcome = ParryOutcome.Block; // 일반 방어 — 여러 번 받아낼 수 있다(지속이 곧 가치)
                }
            }
            else
            {
                _hasGuard = false; // 만료 정리
            }

            if (outcome != ParryOutcome.None) ImpactResolved?.Invoke(outcome, _guardElement, impactPoint);
            return outcome;
        }
    }
}
