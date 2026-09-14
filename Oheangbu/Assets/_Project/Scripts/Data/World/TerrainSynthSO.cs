using UnityEngine;

namespace Oheangbu.Data.World
{
    // [SPEC-WORLD-MAP §4 층 2·§7 ④] 지형 합성 파라미터 — TerrainSynth.Generate(sheet, synth) → float[,](순수 C# · System.Random(seed)).
    // 같은 시트·시드 → 같은 하이트맵(§6 A11). 브러시 손질 없음(D8) — 손맛은 Blender 랜드마크·시트 knots로만.
    // 순서: RidgedFbm + 도메인 워프 + valleyProfile → 열 침식(마스크 제외) → 림·경계 띠·valley 벽 → P2 스파이크 시드 스탬프(yaw 0).
    [CreateAssetMenu(menuName = "Oheangbu/World/Terrain Synth", fileName = "TerrainSynth")]
    public sealed class TerrainSynthSO : ScriptableObject
    {
        [Header("결정론")]
        [Tooltip("System.Random 시드 — UnityEngine.Random/Time 금지")]
        [SerializeField] private int seed = 20260906;

        [Header("매크로 Fbm [TEST]")]
        [Tooltip("매크로 기복 진폭 m(floorY 기준 ±)")]
        [SerializeField] private float macroAmplitude = 60f;
        [Tooltip("매크로 기복 기본 파장 m(첫 옥타브) — P1 추가 필드 [TEST](주파수를 코드 상수로 두지 않기 위함)")]
        [SerializeField] private float macroWavelengthM = 400f;
        [Tooltip("Ridged Fbm 옥타브 수")]
        [SerializeField] private int ridgedOctaves = 5;
        [Tooltip("옥타브 주파수 배율")]
        [SerializeField] private float lacunarity = 2f;
        [Tooltip("옥타브 진폭 배율")]
        [SerializeField] private float gain = 0.5f;
        [Tooltip("도메인 워프 세기(파장 대비 비율)")]
        [SerializeField] private float domainWarp = 0.35f;

        [Header("열 침식 [TEST] — 레거시 16 적정·22 과마모")]
        [Tooltip("열 침식 반복 횟수")]
        [SerializeField] private int thermalIterations = 16;
        [Tooltip("안식각 °(초과 경사가 무너진다)")]
        [SerializeField] private float talusDeg = 35f;
        [Tooltip("침식 마스크 사용 — 절벽 예정 셀·경계 띠·림·컨폼 발자국 제외")]
        [SerializeField] private bool erosionMaskEnabled = true;

        [Header("골짜기 프로필")]
        [Tooltip("골짜기 바닥 절대 고도 m(§5 상대 고도 기준 0)")]
        [SerializeField] private float valleyFloorY = 100f;
        [Tooltip("골짜기 반폭 m(축에서 벽까지) — P1 추가 필드 [TEST](스파이크 heightfield x ±80 승계)")]
        [SerializeField] private float valleyHalfWidthM = 80f;
        [Tooltip("골짜기 축 knots(타일 좌표 m · y = 바닥 절대 고도)")]
        [SerializeField] private Vector3[] valleyAxisKnots = new Vector3[0];

        [Header("스파이크 P2 시드 스탬프 (D1)")]
        [Tooltip("SPEC-SPIKE-WORLD-LOOKDEV §5-11 골짜기 heightfield를 MineExit 기준으로 스탬프")]
        [SerializeField] private bool spikeSeedImport = true;
        [Tooltip("스탬프 가장자리 블렌드 m")]
        [SerializeField] private float spikeSeedBlendM = 20f;
        [Tooltip("스탬프 yaw ° — 0 고정(골짜기 축 = +Z 북 · Sun (18,160,0) 역광 관계 보존). 바꾸려면 Sun.y도 같은 문답에서")]
        [SerializeField] private float spikeSeedYawDeg;

        public int Seed => seed;
        public float MacroAmplitude => macroAmplitude;
        public float MacroWavelengthM => macroWavelengthM;
        public int RidgedOctaves => ridgedOctaves;
        public float Lacunarity => lacunarity;
        public float Gain => gain;
        public float DomainWarp => domainWarp;
        public int ThermalIterations => thermalIterations;
        public float TalusDeg => talusDeg;
        public bool ErosionMaskEnabled => erosionMaskEnabled;
        public float ValleyFloorY => valleyFloorY;
        public float ValleyHalfWidthM => valleyHalfWidthM;
        public Vector3[] ValleyAxisKnots => valleyAxisKnots;
        public bool SpikeSeedImport => spikeSeedImport;
        public float SpikeSeedBlendM => spikeSeedBlendM;
        public float SpikeSeedYawDeg => spikeSeedYawDeg;
    }
}
