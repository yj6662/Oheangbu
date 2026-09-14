using UnityEngine;

namespace Oheangbu.Data.World
{
    // [SPEC-WORLD-MAP §4 층 2] 강토 시트 — 「강토 영역 데이터 하나」(ArtAudio 강토 경계 = Skybox·씻김 종점·음악·소리 인력 전환점)의 실체.
    // 사람 입력의 유일 창구(§7 인터페이스 = 데이터). 씬 타입 참조 0.
    // 색 저장 0(색 단일 출처 #153): 페이드 종점 틴트는 값이 아니라 「어느 팔레트 항을 쓰는가」의 enum만 둔다 — Driver가 팔레트에서 읽는다(A2).
    // 원경 세트 정책 필드(P1 — §6 A9 리터럴 0의 귀결 · §13-13): 빌더(FarSetBuilder·WorldMapBuilder)는 형상·배치 수치를 여기서만 읽는다.
    [CreateAssetMenu(menuName = "Oheangbu/World/Realm Sheet", fileName = "RealmSheet")]
    public sealed class RealmSheetSO : ScriptableObject
    {
        // 페이드 종점 틴트 — 색 값이 아니라 팔레트 항 선택(A2). 정수 직렬화 — 끝에만 추가한다. 강토 틴트 확장 = SPEC-SKY.
        public enum FadeEndTint
        {
            Paper = 0,
        }

        [Header("정체 [TEST]")]
        [Tooltip("강토 식별자(五方)")]
        [SerializeField] private RealmId realmId = RealmId.Cheongrim;
        [Tooltip("강토 오행 초성(작도어휘 CSV 3열 미러) — 광맥·봉수 색 = 팔레트 GetRawVeinColor(element). 청림 = 'ㄱ'(木)")]
        [SerializeField] private char element = 'ㄱ';
        [Tooltip("강토 스케일 캐논(층 1)")]
        [SerializeField] private WorldScaleSO worldScale;
        [Tooltip("이 강토의 구역 시트들(S2 = 금표의 길 1개)")]
        [SerializeField] private AreaSheetSO[] areas = new AreaSheetSO[0];

        [Header("원경 앵커 · 인력 (층 8·10)")]
        [Tooltip("지배 랜드마크 ID(청림 = 신목 「Sinmok」, LDB 전역 앵커)")]
        [SerializeField] private string dominantLandmarkId = "Sinmok";
        [Tooltip("신목 위치 — 타일 좌표 XZ m [TEST](중심에서 ≈1290 m, 링 2~3 사이, 심부 방위). FarSetBuilder 노치 방위 · §6 D2 앙각 계산 입력")]
        [SerializeField] private Vector2 sinmokPosition = new Vector2(1450f, 1400f);
        [Tooltip("인력 바통 순서(LDB: 광맥 → 신목 → 도성) — ID 문자열 슬롯")]
        [SerializeField] private string[] attractionBaton = { "Vein", "Sinmok", "Doseong" };
        [Tooltip("강토별 게이트 스킨(층 5 — 기하 동일·스킨만 교체)")]
        [SerializeField] private GateSkin gateSkin = GateSkin.Cliff;

        [Header("소리 · 음악 슬롯 (구현 0 — ArtAudio 강토 경계 전환점)")]
        [Tooltip("음악 팔레트 ID 슬롯(문자열, 구현 0)")]
        [SerializeField] private string musicPaletteId = "";
        [Tooltip("소리 인력 ID 슬롯(문자열, 구현 0)")]
        [SerializeField] private string soundAttractionId = "";

        [Header("하늘 · 페이드 종점 (A2)")]
        [Tooltip("페이드 종점 틴트 — 색 저장 0, 청림=Paper(A2), 강토 틴트는 SPEC-SKY")]
        [SerializeField] private FadeEndTint fadeEndTint = FadeEndTint.Paper;

        [Header("원경 세트 결정론")]
        [Tooltip("FarSetBuilder 시드 — 능선 겹별 1D 노이즈(같은 시드 = 같은 실루엣, §6 A11)")]
        [SerializeField] private int farSetSeed = 20260906;

        [Header("원경 세트 정책 (층 8 · 전부 [TEST] — 빌더 리터럴 0의 귀결, §6 A9)")]
        [Tooltip("신목 실루엣 높이 m [TEST](층 8 앵커 Sinmok_Silhouette)")]
        [SerializeField] private float sinmokHeightM = 140f;
        [Tooltip("신목 줄기 폭 / 높이 [TEST]")]
        [SerializeField] private float sinmokTrunkRatio = 0.1f;
        [Tooltip("신목 수관 시작 높이 / 높이 [TEST]")]
        [SerializeField] private float sinmokCrownStartRatio = 0.4f;
        [Tooltip("태양 Euler [TEST] — 스파이크 (18,160,0) 승계(D1 · 승인 컷2 역광 관계)")]
        [SerializeField] private Vector3 sunEuler = new Vector3(18f, 160f, 0f);
        [Tooltip("겹별 능선 재질 _AmbientLevel [TEST](층 8 L1..L4 — B6 인접 ΔL 근거)")]
        [SerializeField] private float[] ridgeAmbientLevels = { 0.10f, 0.15f, 0.20f, 0.25f };
        [Tooltip("신목 실루엣 재질 _AmbientLevel [TEST]")]
        [SerializeField] private float sinmokAmbientLevel = 0.15f;
        [Tooltip("P1 임시 바닥(Terrain 0) 재질 _AmbientLevel [TEST] — M_InkWorld_Rock 승계값(스파이크 §9)")]
        [SerializeField] private float standinAmbientLevel = 0.35f;
        [Tooltip("귀환 포탈 = 스폰 뒤 m [TEST](층 11 TEST-HUB 규약)")]
        [SerializeField] private float returnPortalBehindM = 3f;

        public RealmId RealmId => realmId;
        public char Element => element;
        public WorldScaleSO WorldScale => worldScale;
        public AreaSheetSO[] Areas => areas;
        public string DominantLandmarkId => dominantLandmarkId;
        public Vector2 SinmokPosition => sinmokPosition;
        public string[] AttractionBaton => attractionBaton;
        public GateSkin GateSkin => gateSkin;
        public string MusicPaletteId => musicPaletteId;
        public string SoundAttractionId => soundAttractionId;
        public FadeEndTint FadeEnd => fadeEndTint;
        public int FarSetSeed => farSetSeed;
        public float SinmokHeightM => sinmokHeightM;
        public float SinmokTrunkRatio => sinmokTrunkRatio;
        public float SinmokCrownStartRatio => sinmokCrownStartRatio;
        public Vector3 SunEuler => sunEuler;
        public float[] RidgeAmbientLevels => ridgeAmbientLevels;
        public float SinmokAmbientLevel => sinmokAmbientLevel;
        public float StandinAmbientLevel => standinAmbientLevel;
        public float ReturnPortalBehindM => returnPortalBehindM;

        // 구역 ID로 시트 검색 — 없으면 null(AreaLoader·감사가 FAIL 문자열로 보고).
        public AreaSheetSO FindArea(AreaId id)
        {
            if (areas == null)
            {
                return null;
            }
            foreach (var area in areas)
            {
                if (area != null && area.Id.Equals(id))
                {
                    return area;
                }
            }
            return null;
        }
    }
}
