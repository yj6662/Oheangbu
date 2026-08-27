using System;
using UnityEngine;

namespace Oheangbu.Combat
{
    // 그로기 — COMBAT-GROGGY: 「그로기는 상극 응수의 보상이다」.
    // 증가 경로는 정답 패링 하나뿐(프로토 절단면 — 상합·상극 검격은 후속). 시간 감쇠 코드는 없다:
    // 「무감쇠 — 소멸은 판 단위」. Reset은 판이 끝날 때(적 처치·플레이어 사망)와 만개 소진 시[TEST]만.
    public sealed class GroggyMeter
    {
        private readonly int _parriesToBlossom;
        private int _count;

        public event Action Changed;
        public event Action Blossomed; // 만개 = 급소창 개방(스턴+피해 증폭)

        public float Value01 => _parriesToBlossom > 0 ? Mathf.Clamp01(_count / (float)_parriesToBlossom) : 0f;

        public GroggyMeter(CombatConfigSO config)
        {
            _parriesToBlossom = config != null ? config.ParriesToBlossom : 3;
        }

        // 유일한 증가 경로 — 정답 패링(호출자는 배선부 한 곳뿐이어야 한다)
        public void AddFromParry()
        {
            _count++;
            Changed?.Invoke();
            if (_count >= _parriesToBlossom)
            {
                Blossomed?.Invoke();
            }
        }

        public void Reset()
        {
            _count = 0;
            Changed?.Invoke();
        }
    }
}
