using System;
using UnityEngine;

namespace Oheangbu.BrushRender
{
    // 오행 속성 → 술식 플래시 색 팔레트(표현 전용).
    // 초성→속성 매핑의 정본은 작도어휘 CSV 3열이다 — 여기 값은 수동 미러
    // (SPEC-SPIKE-INK-LOOKDEV §10 예외 1: CSV DTO 임포터 도입 시 임포트 산출물로 대체).
    // 색 실수치는 ART-COLOR가 [TEST](실측 트랙)이므로 여기 값도 전부 TEST placeholder다.
    [CreateAssetMenu(menuName = "Oheangbu/BrushRender/Element Palette", fileName = "ElementPalette")]
    public sealed class ElementPaletteSO : ScriptableObject
    {
        [Serializable]
        private struct Entry
        {
            [Tooltip("속성명(표시용) — 목/화/토/금/수")]
            public string Name;
            [Tooltip("이 속성에 속하는 초성들(CSV 3열 미러) — 예: \"ㄱ\"")]
            public string Initials;
            [Tooltip("술식 플래시 색 [TEST] — 담채 기본색(상태 변형은 규칙으로 파생)")]
            [ColorUsage(false, true)] public Color Color;
            [Tooltip("술식 모티프 [TEST] — 커밋 시 글자 주변에 피는 한국화풍 소재(목=매난국죽, 화=불꽃 등). " +
                "흰 바탕 수묵담채 그림 — 셰이더가 밝기로 알파를 만든다(InkMotif)")]
            public Texture2D Motif;
        }

        // 기본값 = CSV 실측 매핑(2026-08-27: ㄱ목 ㄴ화 ㅁ토 ㅅ금 ㅇ수) + 담채 5색(SPEC-ART-INK-LOOK §4.1 승인):
        // 목=#4E8069 / 화=#B85C38 / 토=#B08D4A / 금=#AEB4BA(광택질은 별도 트랙) / 수=#33475C
        [SerializeField] private Entry[] _entries =
        {
            new Entry { Name = "목", Initials = "ㄱ", Color = new Color(0.306f, 0.502f, 0.412f) },
            new Entry { Name = "화", Initials = "ㄴ", Color = new Color(0.722f, 0.361f, 0.220f) },
            new Entry { Name = "토", Initials = "ㅁ", Color = new Color(0.690f, 0.553f, 0.290f) },
            new Entry { Name = "금", Initials = "ㅅ", Color = new Color(0.682f, 0.706f, 0.729f) },
            new Entry { Name = "수", Initials = "ㅇ", Color = new Color(0.200f, 0.278f, 0.361f) },
        };

        [Tooltip("매핑에 없는 초성일 때의 색 — 무채(먹) 폴백")]
        [SerializeField, ColorUsage(false, true)] private Color _fallback = new Color(0.16f, 0.15f, 0.13f);

        // ---- 상태 변환 규칙 [TEST] — SPEC-ART-INK-LOOK §4.1 ----
        // 정본은 「기본색 × 변환 규칙」이다. 상태별 색을 베이크하지 않는다(규칙과 어긋날 위험).
        [Header("상태 변환 규칙 [TEST] — 파생색은 계산으로(베이크 금지)")]
        [Tooltip("술식(플래시): 명도만 올린다 — 채도·색상 불변(담채 유지)")]
        [SerializeField, Range(0f, 0.5f)] private float _flashValueLift = 0.12f;
        [Tooltip("오염 = 검보라 먹과 혼합 — 「속성 오염은 해당 오행색의 탁화」(ART-COLOR LOCKED)")]
        [SerializeField, ColorUsage(false, false)] private Color _corruptInk = new Color(0.200f, 0.169f, 0.212f); // #332B36
        [SerializeField, Range(0f, 1f)] private float _corruptMix = 0.55f;
        [Tooltip("정화 = 한지 소지와 혼합 — 역번짐: 「어둠이 물러나 소지가 드러난다」(ART-INK LOCKED)")]
        [SerializeField, ColorUsage(false, false)] private Color _purityPaper = new Color(0.969f, 0.945f, 0.894f); // #F7F1E4
        [SerializeField, Range(0f, 1f)] private float _purityMix = 0.35f;
        [Tooltip("갈색 — ART-COLOR 기본 팔레트(#8A5A33). 「재질의 색」 슬롯(목질 줄기 등)이며 오행 강조색과는 별개다 — 속성 식별은 강조색이 계속 담당한다(색+형태 이중 채널)")]
        [SerializeField, ColorUsage(false, false)] private Color _woodBrown = new Color(0.541f, 0.353f, 0.200f); // #8A5A33

        public Color GetBaseColor(char initial)
        {
            foreach (var entry in _entries)
            {
                if (!string.IsNullOrEmpty(entry.Initials) && entry.Initials.IndexOf(initial) >= 0)
                {
                    return entry.Color;
                }
            }
            return _fallback;
        }

        // 술식 모티프 — 없으면 null(모티프 생략, 플래시만)
        public Texture2D GetMotif(char initial)
        {
            foreach (var entry in _entries)
            {
                if (!string.IsNullOrEmpty(entry.Initials) && entry.Initials.IndexOf(initial) >= 0)
                {
                    return entry.Motif;
                }
            }
            return null;
        }

        // 술식 플래시색 — 기본색의 명도 상승(채도 불변). 판독 기준: 피크에서도 속성이 색으로 읽혀야 한다
        public Color GetFlashColor(char initial)
        {
            Color c = GetBaseColor(initial) * (1f + _flashValueLift);
            c.a = 1f;
            return c;
        }

        // 오염(탁화) — 항상 기본보다 어둡고 탁해야 한다(빛=안전/번짐=위험 읽기 보존, §5 기준 1b)
        public Color GetCorruptColor(char initial)
        {
            return Color.Lerp(GetBaseColor(initial), _corruptInk, _corruptMix);
        }

        // 한지 소지(素地) — 역번짐 발광의 색 슬롯(ART-INK LOCKED: 「빛 퍼짐 = 먹의 물러남, 어둠이 밀려난
        // 자리로 소지가 드러난다」). 술식의 밝기는 새 색을 더하는 것이 아니라 이 소지를 드러내는 것이므로,
        // 표현 계층은 lerp(속성색, 소지, k)로만 명도를 올린다 — 상한이 소지색으로 하드 클램프되어
        // 백색 포화가 산술적으로 불가능하고 색 단일 출처(팔레트)도 무손상이다.
        public Color PaperColor => _purityPaper;

        // 목질·흙 등 「재질의 색」 — 속성 강조색이 아니다(ART-COLOR 기본 팔레트 갈색)
        public Color WoodColor => _woodBrown;

        // 정화 — 한지 소지 쪽으로 물러난 맑은 색
        public Color GetPurifiedColor(char initial)
        {
            return Color.Lerp(GetBaseColor(initial), _purityPaper, _purityMix);
        }
    }
}
