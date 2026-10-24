using System;
using UnityEngine;

namespace Oheangbu.Combat
{
    // #308: groggy has exactly three sources (COMBAT-GROGGY): a correct parry, a harmony detonation, a counter-element sword strike.
    public enum GroggySource { Parry, Harmony, SwordCounter }

    // 그로기 — COMBAT-GROGGY: 「그로기는 상극 응수의 보상이다」.
    // 증가 경로는 정답 패링 하나뿐(프로토 절단면 — 상합·상극 검격은 후속). 시간 감쇠 코드는 없다:
    // 「무감쇠 — 소멸은 판 단위」. Reset은 판이 끝날 때(적 처치·플레이어 사망)와 만개 소진 시[TEST]만.
    public sealed class GroggyMeter
    {
        private readonly int _parriesToBlossom;
        private int _count;
        public bool IsBlossomed => _count >= _parriesToBlossom;

        public event Action Changed;
        public event Action Blossomed; // 만개 = 급소창 개방(스턴+피해 증폭)
        // Legacy DI consumers reset this service at encounter boundaries.
        public event Action ResetRequested;

        // S5-b slice 2 (D308-149): in a vol.2 scope the posture (GroggyRulesV3: 40 / 60 / 10 toward 100) is the rules' own state, kept by
        // Vol2PostureBridge. It is shown through this meter (the HUD reads Value01) but NEVER fed into the step counter below:
        // ProjectExternal replaces what Value01 answers until Reset (the end of the window, a new encounter) puts the meter back.
        private float _external = -1f;
        public bool HasExternal => _external >= 0f;
        public void ProjectExternal(float value01)
        {
            float v = float.IsNaN(value01) ? 0f : Mathf.Clamp01(value01);
            if (Mathf.Approximately(v, _external)) return;
            _external = v;
            Changed?.Invoke();
        }

        public float Value01 => _external >= 0f ? _external : _parriesToBlossom > 0 ? Mathf.Clamp01(_count / (float)_parriesToBlossom) : 0f;

        public GroggyMeter(CombatConfigSO config)
        {
            _parriesToBlossom = Mathf.Max(1, config != null ? config.ParriesToBlossom : 3);
        }

        // 유일한 증가 경로 — 정답 패링(호출자는 배선부 한 곳뿐이어야 한다)
        public void AddFromParry()
        {
            if (IsBlossomed) return;
            _count++;
            Changed?.Invoke();
            if (_count >= _parriesToBlossom)
            {
                Blossomed?.Invoke();
            }
        }

        // #308: the same meter for the other two sources. steps = how many parry-sized steps the source is worth (data).
        public void Add(GroggySource source, int steps = 1)
        {
            if (steps <= 0 || IsBlossomed) return;
            _count = Mathf.Min(_parriesToBlossom, _count + steps);
            Changed?.Invoke();
            if (_count >= _parriesToBlossom)
            {
                Blossomed?.Invoke();
            }
        }

        public void Reset()
        {
            _count = 0; _external = -1f;
            Changed?.Invoke();
            ResetRequested?.Invoke();
        }
    }
}
