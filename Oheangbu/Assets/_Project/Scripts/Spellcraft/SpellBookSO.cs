using System;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.Spellcraft
{
    // 술식 종류 — 프로토타입 절단면(SPEC-COMBAT-CORE-LOOP §3). 상합·버프 등은 후속 확장.
    public enum SpellKind
    {
        AttackSingle, // 단일 유도 (가 — "단일 대상에 곧게 뻗는 생목 가시")
        AttackArea,   // 영역 즉발 (고 — CSV #190 "지정 영역에서 가시 산발적 솟음(가까운 가시 돌출 완료 시 적별 1회 타격)")
        Parry,        // 상극+ㅓ 받아침 — 판정·보상은 Combat 소유(COMBAT-PARRY). 허공 시전 잔존 없음(CSV)
        Summon,       // 소환 표현 전용 시전 — 이동·공격·피해·어그로 규칙을 부여하지 않는다
        Field,        // Opt-in demo utility; appended to preserve existing serialized kind values.
        Buff,         // Candidate-only temporary combat state; existing serialized values remain stable.
        Ward,         // Fixed survival area; no parry ownership.
        Install       // #308: harmony mark attached to one enemy (no damage of its own); appended, serialized values stay stable.
    }

    // 광역 실판정 형상 [TEST — #137 §9-1 해제 · #141 기하 3종]: None=단일 판정(연출만 광역).
    // Cone=전방 부채꼴 일제(노) / Circle=대상 중심 원형 일제(고) / Path=전방 복도, 전선 도달 시각(오·모) /
    // Volley=전방 부채꼴 후보에 발 단위 순환 배분(소). 의미는 SPEC-SPELL-AREA-SHAPES §2
    public enum AreaShape
    {
        None,
        Cone,
        Circle,
        Path,
        Volley
    }

    // 광역 기하 수치(정규화 완료 — 0은 「미기입」, 소비자가 기본값을 댄다). 판정·연출이 같은 값을 쓴다
    public readonly struct AreaSpec
    {
        public readonly AreaShape Shape;
        public readonly float Angle;       // 부채꼴 반각(도) — Volley 후보
        public readonly float Radius;      // Circle 반경 / Path 반폭
        public readonly float Length;      // Path 길이 / Volley 후보 사거리 / Circle 무대상 조준 거리
        public readonly float Speed;       // Path 전진 속도 / Volley 탄속
        public readonly float ImpactDelay; // 형성 딜레이 — Circle 판정 시각 / Path 차오름 / Volley 기준
        public readonly int Shots;         // Volley 발수
        public readonly float Interval;    // Volley 발사 간격
        public readonly bool ScatterVolley;

        public AreaSpec(AreaShape shape, float angle, float radius, float length, float speed,
            float impactDelay, int shots, float interval, bool scatterVolley = false)
        {
            Shape = shape;
            Angle = angle;
            Radius = radius;
            Length = length;
            Speed = speed;
            ImpactDelay = impactDelay;
            Shots = shots;
            Interval = interval;
            ScatterVolley = scatterVolley;
        }
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
            [Tooltip("광역 실판정 형상 [#137] — None=단일 판정 유지(연출만 광역)")]
            public AreaShape AreaShape;
            [Tooltip("탄속 배율 [SPELL-FIDELITY §4.3] — 0 이하=1 취급. 사=최속·아=느림(CSV 정본 이행). 판정·연출이 같은 시계를 나눠 쓴다")]
            public float ProjectileSpeedMul;

            [Header("광역 기하 [SPELL-AREA-SHAPES §2·§4] — 0=미기입(소비자 기본값). Cone 반각·사거리는 CombatConfig가 정본")]
            [Tooltip("부채꼴 반각(도) — Volley 후보 선정")]
            public float AreaAngle;
            [Tooltip("Circle 반경 / Path 반폭 (m)")]
            public float AreaRadius;
            [Tooltip("Path 길이 / Volley 후보 사거리 / Circle 무대상 조준 거리 (m)")]
            public float AreaLength;
            [Tooltip("Path 전진 속도 / Volley 탄속 (m/s)")]
            public float AreaSpeed;
            [Tooltip("형성 딜레이(s) — Circle 판정 시각 / Path 차오름 / Volley 첫 발 기준. 0=CombatConfig.AreaImpactDelay")]
            public float AreaImpactDelay;
            [Tooltip("Volley 발수 — 위력을 발수로 나눠 후보에 순환 배분")]
            public int VolleyShots;
            [Tooltip("Volley 발사 간격(s)")]
            public float VolleyInterval;
            public bool ScatterVolley;

            public AreaSpec ToAreaSpec()
            {
                return new AreaSpec(AreaShape, AreaAngle, AreaRadius, AreaLength, AreaSpeed, AreaImpactDelay, VolleyShots, VolleyInterval, ScatterVolley);
            }
        }

        [SerializeField] private Entry[] _entries;

        // #308 (SPEC-SPELL-120-308 section 2): the 120-row dispatch table. Written only by the importer
        // (Oheangbu.EditorTools.WorldMacro.SpellTable308 "spell308-import") from the canonical CSV and Data/Spells/Rules308_*.csv,
        // never by hand. Empty = this book resolves from _entries exactly as before #308.
        [Header("#308 술식 표 — 임포터 산출물(손으로 고치지 않는다)")]
        [SerializeField] private SpellRow[] _rows = Array.Empty<SpellRow>();
        [SerializeField] private SpellUnlockRule[] _unlocks = Array.Empty<SpellUnlockRule>();
        [Tooltip("정본 CSV + 규칙 시트 전체의 sha256")]
        [SerializeField] private string _rowsHash = "";
        [Tooltip("임포트된 120행 + 해금 5행의 정규 텍스트 sha256 (오프라인 도구와 대조)")]
        [SerializeField] private string _tableHash = "";

        public bool HasTable => _rows != null && _rows.Length == SpellGrammar308.Count;
        public string RowsHash => _rowsHash ?? "";
        public string TableHash => _tableHash ?? "";
        public System.Collections.Generic.IReadOnlyList<SpellRow> Rows => _rows ?? Array.Empty<SpellRow>();
        public System.Collections.Generic.IReadOnlyList<SpellUnlockRule> Unlocks => _unlocks ?? Array.Empty<SpellUnlockRule>();

        // The row that decides what a glyph does: the imported table when present, otherwise what _entries implied before #308.
        public bool TryGetRow(char letter, out SpellRow row)
        {
            if (!HasTable) return SpellTableBuilder308.TryLegacyRow(letter, _entries, out row);
            int index = SpellGrammar308.IndexOf(letter);
            row = index > 0 ? _rows[index - 1] : null;
            if (row != null && row.Is(letter)) return true;
            row = null;
            return false;
        }

        public bool TryGetUnlock(SpellFinal final, out SpellUnlockRule rule)
        {
            if (_unlocks != null)
                foreach (var candidate in _unlocks)
                    if (candidate.Final == final) { rule = candidate; return true; }
            rule = default;
            return false;
        }

#if UNITY_EDITOR
        // Importer only. rows = 120 rows in grid order, or an empty array to return the book to its pre-#308 state.
        public void SetTable308(SpellRow[] rows, SpellUnlockRule[] unlocks, string rowsHash, string tableHash)
        {
            if (rows != null && rows.Length != 0 && rows.Length != SpellGrammar308.Count)
                throw new ArgumentException("A spell table has 120 rows (or none).");
            _rows = rows ?? Array.Empty<SpellRow>();
            _unlocks = unlocks ?? Array.Empty<SpellUnlockRule>();
            _rowsHash = rowsHash ?? "";
            _tableHash = tableHash ?? "";
        }
#endif

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

        // #308: form x speed before the curve (0..1). Read only: presentation grade, never a second power source.
        public float EvaluateBrush01(float worstJamoDistance, float strokeDuration)
        {
            float form = Mathf.InverseLerp(_formRange.x, _formRange.y, worstJamoDistance);
            float speed = Mathf.InverseLerp(_speedRange.x, _speedRange.y, strokeDuration);
            return Mathf.Clamp01(form * speed);
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
