using UnityEngine;

namespace Oheangbu.Data.World
{
    // [SPEC-WORLD-MAP §4 층 1] 강토 스케일 캐논 — 강토당 1, 전 필드 [TEST](Spatial Placement Bible이 EA 실측 후 승격).
    // 코드에 미감·규모 상수를 두지 않는다(§6 A9): 빌더·carver·Driver·감사가 읽는 숫자는 전부 여기 산다.
    // 파생값(m/분·최원경 반경·페이드 m·능선 반경/높이)은 저장하지 않고 읽기 전용 프로퍼티로 계산한다 — 단일 출처.
    // walkSpeedMps는 CombatConfigSO.MoveSpeed의 값 미러(Data는 Combat을 참조할 수 없다 — 순환) → §6 A9 ScaleMirror 동치 감사.
    [CreateAssetMenu(menuName = "Oheangbu/World/World Scale", fileName = "WorldScale")]
    public sealed class WorldScaleSO : ScriptableObject
    {
        // 단위 환산 상수(미감 아님) — 분 ↔ 초.
        private const float SecondsPerMinute = 60f;

        [Header("보행·시점 [TEST]")]
        [Tooltip("이동 속도 m/s — CombatConfigSO.MoveSpeed 값 미러(§6 A9 동치 감사). MetersPerMinute = ×60")]
        [SerializeField] private float walkSpeedMps = 4.5f;
        [Tooltip("눈높이 m — WorldLookAudit 기준(0.7+1.1)")]
        [SerializeField] private float eyeHeight = 1.8f;

        [Header("far · 페이드 · 원경 (D7 — 비율은 최원경(L4 반경) 기준)")]
        [Tooltip("카메라 far clip m — 강토별(D7, 금표의 길 2500~3000 중앙). WorldScaleDriver가 런타임에만 덮어쓴다")]
        [SerializeField] private float cameraFar = 2800f;
        [Tooltip("최원경 링(L4) 반경 = cameraFar × 이 비율(스파이크 640/1000 승계)")]
        [SerializeField] private float farSetRatio = 0.64f;
        [Tooltip("소지 페이드 시작 = 최원경 반경 × 비율(스파이크 §5-11 15%)")]
        [SerializeField] private float paperFadeStartRatio = 0.15f;
        [Tooltip("소지 페이드 종점 = 최원경 반경 × 비율(스파이크 §5-11 65%)")]
        [SerializeField] private float paperFadeEndRatio = 0.65f;
        [Tooltip("페이드 종점에서 남기는 먹 비율(스파이크 keep 승계)")]
        [SerializeField] private float paperKeep = 0.55f;
        [Tooltip("페이드 세기(스파이크 승계)")]
        [SerializeField] private float paperStrength = 0.9f;
        [Tooltip("법선 먹선 소멸 거리 m(절대 — 근·중경 속성, 강토 규모 무관)")]
        [SerializeField] private float normalEdgeFadeDistance = 140f;

        [Header("원경 능선 링 (층 8 — 가이드 §21 가설)")]
        [Tooltip("능선 겹 수(+Terrain 가장자리 = 5겹)")]
        [SerializeField] private int ridgeLayerCount = 4;
        [Tooltip("겹별 반경 비율(최원경 반경 기준) — L1이 타일 반대각(≈724 m)보다 커야 모서리 관통 0")]
        [SerializeField] private float[] ridgeRadiusRatios = { 0.50f, 0.67f, 0.83f, 1.00f };
        [Tooltip("겹별 상단 앙각 °(구역 중심 기준) — 바깥 겹일수록 높게(심원 문법). h = r·tanθ")]
        [SerializeField] private float[] ridgeElevationDeg = { 6f, 7.5f, 9f, 10.5f };
        [Tooltip("능선 높이 1D 노이즈 진폭(±비율, 층별 시드)")]
        [SerializeField] private float ridgeNoiseAmp = 0.25f;
        [Tooltip("신목 방위 노치 반각 ° — 이 방위 안에서 L2~L4 높이를 낮춘다(ArtAudio 「한 번에 분리」)")]
        [SerializeField] private float sinmokNotchDeg = 8f;
        [Tooltip("노치 안 높이 배율")]
        [SerializeField] private float sinmokNotchScale = 0.75f;
        [Tooltip("타일 4변 림 상단 앙각 상한 °(본선 표본 전부) — 림 높이는 값이 아니라 파생")]
        [SerializeField] private float rimElevationMaxDeg = 4f;

        [Header("도보 캐논 (D3·D4·A7)")]
        [Tooltip("인접 체크포인트 도보 상한 분(회수 런 상한, A7)")]
        [SerializeField] private float checkpointMaxWalkMinutes = 3.5f;
        [Tooltip("난소 직전 성황당 → 난소 입구 도보 상한 초(LDB-CHECKPOINT)")]
        [SerializeField] private float bossShrineSeconds = 30f;
        [Tooltip("폐광 출구 → 첫 주막 목표 도보 분(D4 시드, 범위는 시트 walkTargets tolerance)")]
        [SerializeField] private float innTargetWalkMinutes = 2.5f;
        [Tooltip("구역 총 도보 분 하한(본선+지선 왕복, D3)")]
        [SerializeField] private float areaWalkMinutesMin = 10f;
        [Tooltip("구역 총 도보 분 상한(D3)")]
        [SerializeField] private float areaWalkMinutesMax = 15f;

        [Header("길 규격 (층 6 — CC 45°의 2/3)")]
        [Tooltip("길 코리도 폭 m(중앙 평탄 + 어깨)")]
        [SerializeField] private float trailCorridorWidth = 6f;
        [Tooltip("길 종단 경사 상한 °")]
        [SerializeField] private float trailMaxGradeDeg = 15f;
        [Tooltip("길 횡단 경사 상한 °")]
        [SerializeField] private float trailCrossGradeDeg = 5f;
        [Tooltip("램프 종단 경사 상한 °")]
        [SerializeField] private float rampMaxGradeDeg = 30f;
        [Tooltip("램프 최대 길이 m")]
        [SerializeField] private float rampMaxLengthM = 20f;

        [Header("가도 예약 (A1 — 마석 자동차, 구현 0)")]
        [Tooltip("가도 최소 폭 m")]
        [SerializeField] private float gadoMinWidth = 6f;
        [Tooltip("가도 종단 경사 상한 °")]
        [SerializeField] private float gadoMaxGradeDeg = 12f;
        [Tooltip("가도 최소 회전 반경 m")]
        [SerializeField] private float gadoMinTurnRadius = 12f;

        [Header("낙차 · 절벽 · 경계 (A6 · 가이드 §10 3분류 가설)")]
        [Tooltip("코리도 안 자유 낙하 상한 m(실수 낙사 0 — 낙사 임계는 Combat #166)")]
        [SerializeField] private float maxWalkableDropM = 2f;
        [Tooltip("일방 낙차턱 최소 높이 m(CC step 0.3 초과 = 못 오름)")]
        [SerializeField] private float oneWayDropMinM = 0.35f;
        [Tooltip("절벽 셀 판정 경사 °(≥ = 키트 매입)")]
        [SerializeField] private float cliffSlopeDeg = 55f;
        [Tooltip("타일 경계 띠 경사 °(보이지 않는 벽 대신 경사 띠 — 림 안쪽 사면)")]
        [SerializeField] private float boundarySlopeDeg = 60f;
        [Tooltip("경계 띠 폭 m")]
        [SerializeField] private float boundaryBandM = 40f;

        [Header("개울 (D6 — 층 7)")]
        [Tooltip("여울 하상 깊이 상한 m(CC step 0.3)")]
        [SerializeField] private float fordMaxDepth = 0.3f;
        [Tooltip("심연 하상 깊이 하한 m")]
        [SerializeField] private float deepMinDepth = 1.5f;
        [Tooltip("여울↔심연 경계 Sill 턱 최소 높이 m(수직면)")]
        [SerializeField] private float sillMinHeight = 0.6f;
        [Tooltip("심연 양안 Parapet 최소 높이 m(연속, 틈 0)")]
        [SerializeField] private float parapetMinHeight = 0.8f;

        [Header("인력 시야선 · 앵커 (층 8·10)")]
        [Tooltip("인력 목표 최소 각크기 °(≈15 px @1080p·FOV 60)")]
        [SerializeField] private float sightlineMinAngularDeg = 0.8f;
        [Tooltip("원경 봉수 판 크기 m(최원경 반경에서 sightlineMinAngularDeg 전제)")]
        [SerializeField] private float beaconFarPlateM = 25f;
        [Tooltip("벌목장 적재탑 높이 m — 배치값 [TEST](인간 구조물 높이 규칙 보류 A16)")]
        [SerializeField] private float loggingTowerHeightM = 8f;

        [Header("식생 (A4)")]
        [Tooltip("희소 식생 인스턴스 상한(밀식은 SPEC-VEG)")]
        [SerializeField] private int vegetationMaxInstances = 1500;

        // ---- 원시 필드 ----
        public float WalkSpeedMps => walkSpeedMps;
        public float EyeHeight => eyeHeight;
        public float CameraFar => cameraFar;
        public float FarSetRatio => farSetRatio;
        public float PaperFadeStartRatio => paperFadeStartRatio;
        public float PaperFadeEndRatio => paperFadeEndRatio;
        public float PaperKeep => paperKeep;
        public float PaperStrength => paperStrength;
        public float NormalEdgeFadeDistance => normalEdgeFadeDistance;
        public int RidgeLayerCount => ridgeLayerCount;
        public float[] RidgeRadiusRatios => ridgeRadiusRatios;
        public float[] RidgeElevationDeg => ridgeElevationDeg;
        public float RidgeNoiseAmp => ridgeNoiseAmp;
        public float SinmokNotchDeg => sinmokNotchDeg;
        public float SinmokNotchScale => sinmokNotchScale;
        public float RimElevationMaxDeg => rimElevationMaxDeg;
        public float CheckpointMaxWalkMinutes => checkpointMaxWalkMinutes;
        public float BossShrineSeconds => bossShrineSeconds;
        public float InnTargetWalkMinutes => innTargetWalkMinutes;
        public float AreaWalkMinutesMin => areaWalkMinutesMin;
        public float AreaWalkMinutesMax => areaWalkMinutesMax;
        public float TrailCorridorWidth => trailCorridorWidth;
        public float TrailMaxGradeDeg => trailMaxGradeDeg;
        public float TrailCrossGradeDeg => trailCrossGradeDeg;
        public float RampMaxGradeDeg => rampMaxGradeDeg;
        public float RampMaxLengthM => rampMaxLengthM;
        public float GadoMinWidth => gadoMinWidth;
        public float GadoMaxGradeDeg => gadoMaxGradeDeg;
        public float GadoMinTurnRadius => gadoMinTurnRadius;
        public float MaxWalkableDropM => maxWalkableDropM;
        public float OneWayDropMinM => oneWayDropMinM;
        public float CliffSlopeDeg => cliffSlopeDeg;
        public float BoundarySlopeDeg => boundarySlopeDeg;
        public float BoundaryBandM => boundaryBandM;
        public float FordMaxDepth => fordMaxDepth;
        public float DeepMinDepth => deepMinDepth;
        public float SillMinHeight => sillMinHeight;
        public float ParapetMinHeight => parapetMinHeight;
        public float SightlineMinAngularDeg => sightlineMinAngularDeg;
        public float BeaconFarPlateM => beaconFarPlateM;
        public float LoggingTowerHeightM => loggingTowerHeightM;
        public int VegetationMaxInstances => vegetationMaxInstances;

        // ---- 파생(읽기 전용 — 저장 0) ----

        // 분당 도보 m = 4.5 × 60 = 270. 도보 분 = 경로 길이 ÷ 이 값.
        public float MetersPerMinute => walkSpeedMps * SecondsPerMinute;

        // 최원경(L4) 링 반경 m = far × 비율(≈1800).
        public float FarSetOuterRadius => cameraFar * farSetRatio;

        // 소지 페이드 시작 m(≈270) — 최원경 기준(D7).
        public float PaperFadeStartM => FarSetOuterRadius * paperFadeStartRatio;

        // 소지 페이드 종점 m(≈1170).
        public float PaperFadeEndM => FarSetOuterRadius * paperFadeEndRatio;

        // 중경 인력 직선 상한 = 페이드 시작(#151-i · §6 D1 「layer==Mid ⇒ 직선 ≤ 이 값」).
        public float MidAttractionMaxDistance => PaperFadeStartM;

        // 체크포인트 간격 상한 m(≈945).
        public float CheckpointMaxWalkM => checkpointMaxWalkMinutes * MetersPerMinute;

        // 난소 직전 성황당 거리 상한 m(≈135).
        public float BossShrineM => bossShrineSeconds * walkSpeedMps;

        // 겹별 능선 반경 m(ridgeRadiusRatios × 최원경 반경 → ≈900/1200/1500/1800). 새 배열 반환(호출자 변경 안전).
        public float[] RidgeRadii
        {
            get
            {
                int n = LayerCountClamped();
                var radii = new float[n];
                float outer = FarSetOuterRadius;
                for (int i = 0; i < n; i++)
                {
                    radii[i] = ridgeRadiusRatios[i] * outer;
                }
                return radii;
            }
        }

        // 겹별 능선 상단 높이 m = r·tan(앙각) → ≈95/158/238/334.
        public float[] RidgeHeights
        {
            get
            {
                float[] radii = RidgeRadii;
                var heights = new float[radii.Length];
                for (int i = 0; i < radii.Length; i++)
                {
                    float deg = i < ridgeElevationDeg.Length ? ridgeElevationDeg[i] : 0f;
                    heights[i] = radii[i] * Mathf.Tan(deg * Mathf.Deg2Rad);
                }
                return heights;
            }
        }

        // 겹 수는 ridgeLayerCount와 비율 배열 길이 중 작은 쪽 — 배열이 짧으면 조용히 잘린다(감사 A9가 길이 불일치를 보고).
        private int LayerCountClamped()
        {
            int byRatios = ridgeRadiusRatios != null ? ridgeRadiusRatios.Length : 0;
            return Mathf.Max(0, Mathf.Min(ridgeLayerCount, byRatios));
        }
    }
}
