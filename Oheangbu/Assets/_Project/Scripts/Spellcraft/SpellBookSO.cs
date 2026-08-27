using System;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.Spellcraft
{
    // 술식 종류 — 프로토타입 절단면(SPEC-COMBAT-CORE-LOOP §3). 상합·버프 등은 후속 확장.
    public enum SpellKind
    {
        AttackSingle, // 단일 유도 (가 — "단일 대상에 곧게 뻗는 생목 가시")
        AttackArea,   // 영역 즉발 (고 — "지정 영역에서 가시 일제 솟음")
        Parry         // 상극+ㅓ 받아침 — 판정·보상은 Combat 소유(COMBAT-PARRY). 허공 시전 잔존 없음(CSV)
    }

    // 어휘 미러 [TEST · Temporary Exception §9-1] — 글자 효과의 정본은 오행부_작도어휘_v0_1.csv다.
    // 프로토 사용 어휘(가·고 + 패링 5자)만 수동 매핑하며, CSV DTO 임포터 도입 시 임포트 산출물로 대체된다.
    [CreateAssetMenu(menuName = "Oheangbu/Spellcraft/Spell Book", fileName = "SpellBook")]
    public sealed class SpellBookSO : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("완성 글자 1자 — CSV 1열 미러")]
            public string Letter;
            public SpellKind Kind;
            public Element Element;
            [Tooltip("기본 위력 — 필세(형×세)가 여기에 곱해진다 [TEST]")]
            public float BasePower;
        }

        [SerializeField] private Entry[] _entries;

        [Header("필세 근사 [TEST] — 정식 수식은 플레이테스트 후 데이터 층(§9-2)")]
        [Tooltip("형(形): 최악 자모 거리 → 0..1 (x=0점 거리, y=1점 거리)")]
        [SerializeField] private Vector2 _formRange = new Vector2(1.6f, 0.6f);
        [Tooltip("세(勢): 총 획 시간(초) → 0..1 (x=느림 0점, y=빠름 1점)")]
        [SerializeField] private Vector2 _speedRange = new Vector2(4f, 1f);
        [Tooltip("필세 0..1 → 위력 배율 — 바닥을 둬서 서툰 글자도 0이 되지는 않게")]
        [SerializeField] private AnimationCurve _powerCurve = AnimationCurve.Linear(0f, 0.3f, 1f, 1f);

        public bool TryGet(char letter, out Entry entry)
        {
            if (_entries != null)
            {
                foreach (var candidate in _entries)
                {
                    if (!string.IsNullOrEmpty(candidate.Letter) && candidate.Letter[0] == letter)
                    {
                        entry = candidate;
                        return true;
                    }
                }
            }
            entry = default;
            return false;
        }

        // 필세(형×세) 근사 — COMBAT-ATTACK "잘 쓴 글씨가 세고, 빨리 쓴 글씨가 세다"
        public float EvaluateBrushPower(float worstJamoDistance, float strokeDuration)
        {
            float form = Mathf.InverseLerp(_formRange.x, _formRange.y, worstJamoDistance);
            float speed = Mathf.InverseLerp(_speedRange.x, _speedRange.y, strokeDuration);
            return Mathf.Max(0f, _powerCurve.Evaluate(Mathf.Clamp01(form * speed)));
        }
    }
}
