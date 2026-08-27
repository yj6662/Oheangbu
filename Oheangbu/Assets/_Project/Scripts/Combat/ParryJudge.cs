using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.Combat
{
    // 패링 판정 — COMBAT-PARRY의 코드화.
    // 성립: 상극 초성+ㅓ 완성형이 「임팩트 직전의 짧은 창」 안에 완성될 것. 판정점=글자 완성 시점.
    // 3단: 정답(상극)=성공 / 공격을 生하는 글자=완전 실패 / 나머지=반성공. 관계는 ElementRelations로 도출.
    public enum ParryOutcome
    {
        None,    // 판정 없음(창 밖·활성 공격 없음) — 허공 시전 잔존 없음(CSV)
        Success, // 피해 무효 + 그로기 + 먹 순증
        Half,    // 피해 경감
        Fail     // 완전 실패(상생) 또는 너무 늦음
    }

    // 적이 텔레그래프 시작 시 등록하는 「날아오는 속성 공격」 하나.
    // 무속성 공격은 등록하지 않는다 — 무속성=회피만이 답(COMBAT-DEFENSE 이원법).
    public sealed class IncomingAttack
    {
        public Element Element;
        public float ImpactTime;           // scaled time — 감속 중엔 창도 함께 늘어난다(작도할 시간을 주는 감속의 목적)
        public ParryOutcome Resolution;    // 임팩트 처리자가 읽는 판정 결과
    }

    public sealed class ParryJudge
    {
        private readonly float _window;
        private IncomingAttack _active;

        public ParryJudge(CombatConfigSO config)
        {
            _window = config != null ? config.ParryWindow : 0.5f;
        }

        // 일반몹은 콤보가 없으므로(COMBAT-ENEMY) 활성 공격은 동시 1개 — 새 등록이 이전을 대체
        public void Register(IncomingAttack attack)
        {
            _active = attack;
        }

        public void Clear()
        {
            _active = null;
        }

        // 판정점 = now(글자 완성 시점). 창: [임팩트-창, 임팩트]. 이미 판정된 공격은 재판정 없음
        public ParryOutcome Judge(Element parryElement, float now)
        {
            var attack = _active;
            if (attack == null || attack.Resolution != ParryOutcome.None) return ParryOutcome.None;
            if (now < attack.ImpactTime - _window || now > attack.ImpactTime) return ParryOutcome.None;

            ParryOutcome outcome;
            if (ElementRelations.Overcomes(parryElement, attack.Element)) outcome = ParryOutcome.Success;
            else if (ElementRelations.Generates(parryElement, attack.Element)) outcome = ParryOutcome.Fail;
            else outcome = ParryOutcome.Half;

            attack.Resolution = outcome;
            return outcome;
        }
    }
}
