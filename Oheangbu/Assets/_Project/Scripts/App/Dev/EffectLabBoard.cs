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
                    string judge = DescribeJudge(entry);
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
            AppendBody(sb, single, area);
            return sb.ToString();
        }

        // 판정 문구 — 형상별 수치는 SpellBook, cone만 CombatConfig(SPELL-AREA-SHAPES §2)
        private string DescribeJudge(SpellBookSO.Entry entry)
        {
            switch (entry.AreaShape)
            {
                case AreaShape.Cone:
                    return _config != null
                        ? $"cone {_config.AreaConeAngle:0}°/{_config.AreaConeRange:0.#}m 판정 +{_config.AreaImpactDelay:0.##}s"
                        : "cone";
                case AreaShape.Circle:
                    return $"원형 반경 {entry.AreaRadius:0.#}m 판정 +{entry.AreaImpactDelay:0.##}s";
                case AreaShape.Path:
                    return $"경로 반폭 {entry.AreaRadius:0.#}m·{entry.AreaLength:0.#}m·{entry.AreaSpeed:0.#}m/s(전선 도달 시각)";
                case AreaShape.Volley:
                    return $"다연발 {entry.VolleyShots}발 반각 {entry.AreaAngle:0}°/{entry.AreaLength:0.#}m(발당 위력÷{entry.VolleyShots})";
                default:
                    return "판정=단일";
            }
        }

        private void AppendBody(StringBuilder sb, StringBuilder single, StringBuilder area)
        {
            sb.Append("단일(락온 또는 시선 안 1체 · 착탄에 피해)\n").Append(single);
            sb.Append("광역(전방)\n").Append(area);
            float cost = _config != null ? _config.SpellInkCost : 0f;
            sb.Append("먹 ").Append(cost.ToString("0.##")).Append("/발 · 갈무리(LMB 홀드·락온 L1)로 회복 · R 재시작");
        }
    }
}
