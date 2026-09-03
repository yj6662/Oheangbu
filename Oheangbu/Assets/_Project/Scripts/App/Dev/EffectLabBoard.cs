using System;
using System.Text;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // [이펙트 실험실 — SPEC-DEV-TEST-HUB §5] 공격 어휘판: 수치(종류·속성·탄속 배율·cone)는 SpellBook·CombatConfig에서
    // 합성하고, 문법 문구만 손으로 적은 미러다(정본=작도어휘 CSV — §9-4). 시험자가 무엇을 그리고 무엇을 볼지 안다
    public sealed class EffectLabBoard : MonoBehaviour
    {
        [Serializable]
        public struct Row
        {
            public string Letter;
            public string Grammar;
        }

        [SerializeField] private SpellBookSO _spellBook;
        [SerializeField] private CombatConfigSO _config;
        [SerializeField] private DevInfoBoard _board;
        [SerializeField] private Row[] _rows = Array.Empty<Row>();

        private void Start()
        {
            if (_board != null) _board.SetText(Compose());
        }

        private string Compose()
        {
            var single = new StringBuilder();
            var area = new StringBuilder();
            foreach (var row in _rows)
            {
                if (string.IsNullOrEmpty(row.Letter)) continue;
                SpellBookSO.Entry entry = default;
                bool known = _spellBook != null && _spellBook.TryGet(row.Letter[0], out entry);
                if (!known)
                {
                    single.Append(' ').Append(row.Letter).Append(" (어휘 미러 밖)\n");
                    continue;
                }
                string element = DevElement.Name(entry.Element);
                if (entry.Kind == SpellKind.AttackArea)
                {
                    string judge = entry.AreaShape == AreaShape.Cone && _config != null
                        ? $"cone {_config.AreaConeAngle:0}°/{_config.AreaConeRange:0.#}m 판정 +{_config.AreaImpactDelay:0.##}s"
                        : "판정=단일";
                    area.Append(' ').Append(row.Letter).Append('(').Append(element).Append(") ").Append(row.Grammar)
                        .Append(" · ").Append(judge).Append('\n');
                }
                else
                {
                    float mul = entry.ProjectileSpeedMul > 0f ? entry.ProjectileSpeedMul : 1f;
                    single.Append(' ').Append(row.Letter).Append('(').Append(element).Append(") ").Append(row.Grammar)
                        .Append(" · 탄속 ×").Append(mul.ToString("0.#")).Append('\n');
                }
            }

            var sb = new StringBuilder();
            sb.Append("[공격 어휘 — 기대 문법(미러 · 정본=작도어휘 CSV)]\n");
            sb.Append("단일(락온 또는 시선 안 1체 · 착탄에 피해)\n").Append(single);
            sb.Append("광역(전방)\n").Append(area);
            float cost = _config != null ? _config.SpellInkCost : 0f;
            sb.Append("먹 ").Append(cost.ToString("0.##")).Append("/발 · 갈무리(LMB 홀드·락온 L1)로 회복 · R 재시작");
            return sb.ToString();
        }
    }
}
