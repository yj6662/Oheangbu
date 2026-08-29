using System;
using UnityEngine;

namespace Oheangbu.App
{
    // [SPEC-SPELL-FX-ASSETS §4.4] 글자→커밋 이펙트 프리팹 매핑 — 표현 전용 SO.
    // 규칙 계층(SpellBookSO)과 분리한다: CSV DTO 임포터가 규칙 미러를 통째 대체해도
    // 시각 매핑은 살아남는다(ElementPaletteSO와 같은 결의 표현 데이터).
    // 미등재 글자는 어댑터의 기본 슬롯(_commitPatternPrefab)으로 폴백 — 기존 동작 비파괴.
    [CreateAssetMenu(menuName = "Oheangbu/Spell Visual Set", fileName = "SpellVisualSet")]
    public sealed class SpellVisualSetSO : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string Letter;      // 완성형 글자 — SpellBook 미러와 같은 키
            public GameObject FxPrefab; // 결합 프리팹(문양±생성 모델) — Projectile/Explosion 자식명 규약
            public float ScaleMul;      // 글자 배율에 곱하는 보정(0 이하=1 취급)
            public float ArcHeight;     // 연출 포물선 높이(m·0=직선) — 마의 §3.1 B안. 비행시간 불변
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        public bool TryGet(char letter, out Entry entry)
        {
            foreach (var e in _entries)
            {
                if (!string.IsNullOrEmpty(e.Letter) && e.Letter[0] == letter && e.FxPrefab != null)
                {
                    entry = e;
                    return true;
                }
            }
            entry = default;
            return false;
        }
    }
}
