using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    // [SPEC-WORLD-MAP §4 층 2] 구역 배치 시트 — 사람 입력의 유일 창구. §5 배치표는 이 SO의 열람 미러다.
    // 규칙: Data는 Splines/Terrain/씬 오브젝트를 참조하지 않는다 — knots = Vector3[](타일 좌표 m · y = 절대 고도),
    // 씬 SplineContainer·Terrain·프리팹 인스턴스는 전부 빌더(EditorTools) 산출. 랜드마크만 LandmarkMeshSO(Data) 참조.
    // 좌표계(§5): 타일 원점 (0,0)~(1024,1024) · X 동 · Z 북 · 고도 = TerrainSynthSO.valleyFloorY 기준 상대 + floorY.
    // 서사 침묵(LDB): narrativeSlot 기본 공란 — §6 A12 census.
    [CreateAssetMenu(menuName = "Oheangbu/World/Area Sheet", fileName = "AreaSheet")]
    public sealed class AreaSheetSO : ScriptableObject
    {
        // ---- 중첩 레코드(직렬화 클래스 — 인스펙터 「+」가 기본값을 적용하도록 struct 대신 class) ----

        // Terrain 타일 1장. holesResolution = heightmapRes−1(엔진 파생) · alphamap/baseMap = 봉인 상수(시트 필드 아님).
        [Serializable]
        public sealed class TileSpec
        {
            [Tooltip("타일 원점 XZ m(타일 좌표계)")]
            public Vector2 originXZ = Vector2.zero;
            [Tooltip("타일 한 변 m")]
            public float sizeM = 1024f;
            [Tooltip("타일 높이 범위 m")]
            public float heightM = 300f;
            [Tooltip("하이트맵 해상도(2^n+1 · ≈1 m/px)")]
            public int heightmapRes = 1025;

            // 홀 해상도는 엔진 파생값(heightmapRes − 1) — 저장하지 않는다.
            public int HolesResolution => heightmapRes - 1;
        }

        // 길 1개 — 빌더가 Catmull-Rom SplineContainer + PathCarver + 리본 메시로 전개(층 6).
        [Serializable]
        public sealed class PathSpec
        {
            [Tooltip("길 ID(Trail_Main 등)")]
            public string id = "";
            [Tooltip("길 클래스 — S2 전부 Trail(Gado = EA 예약 A1)")]
            public PathClass pathClass = PathClass.Trail;
            [Tooltip("경로 knots(타일 좌표 m · y = 절대 고도) — §6 C1 밖이면 SO가 아니라 여기를 고친다")]
            public Vector3[] knots = new Vector3[0];
            [Tooltip("지선 끝 보상 슬롯 ID(갈래길 끝 보상 — Constitution) · 본선은 공란")]
            public string rewardSlot = "";
        }

        // 개울 구간 — 폴리라인 정규화 t 구간 [t0, t1]에 여울/심연 규칙 적용(층 7).
        [Serializable]
        public sealed class StreamSegment
        {
            [Tooltip("구간 ID(F1·D1 등 — §5-4 이름, 감사·인력 링크가 참조)")]
            public string id = "";
            [Tooltip("구간 시작(폴리라인 정규화 0~1)")]
            public float t0;
            [Tooltip("구간 끝(폴리라인 정규화 0~1)")]
            public float t1;
            [Tooltip("여울(도보 가능) / 심연(기하 차단)")]
            public StreamSegmentKind kind = StreamSegmentKind.Ford;
        }

        // 개울 1개 — 빌더가 StreamCarver + InkRiver 리본으로 전개.
        [Serializable]
        public sealed class StreamSpec
        {
            [Tooltip("개울 ID")]
            public string id = "";
            [Tooltip("물길 knots(상류 → 하류 · y = 하상 절대 고도)")]
            public Vector3[] knots = new Vector3[0];
            [Tooltip("리본 폭 m(3~8)")]
            public float width = 6f;
            [Tooltip("여울/심연 구간")]
            public StreamSegment[] segments = new StreamSegment[0];
        }

        // Blender 랜드마크 배치(층 4) — 컨폼은 LandmarkMeshSO.conformMode.
        [Serializable]
        public sealed class LandmarkPlacement
        {
            [Tooltip("랜드마크 메시 계약 SO")]
            public LandmarkMeshSO landmark;
            [Tooltip("피벗 위치(발자국 XZ 중심 · y = 접지 절대 고도)")]
            public Vector3 position;
            [Tooltip("Y축 회전 °(북 0° 시계)")]
            public float yawDeg;
        }

        // POI(§5-1) — 슬롯 규칙은 kind별 프리팹이 안다. 서사 슬롯 기본 공란.
        [Serializable]
        public sealed class PoiSpec
        {
            [Tooltip("POI ID(MineExit·Inn_Geumpyo …)")]
            public string id = "";
            [Tooltip("종류")]
            public PoiKind kind = PoiKind.Vista;
            [Tooltip("위치(타일 좌표 m · y = 절대 고도)")]
            public Vector3 position;
            [Tooltip("정면 방위 °(북 0° 시계) — 스폰 yaw·시야선 기준")]
            public float yaw;
            [Tooltip("패드 반경 m(식생 제외·평탄 컨폼 참고)")]
            public float padRadius;
            [Tooltip("본선 누적 목표 도보 분(§5-1 열람값 — 검증은 walkTargets)")]
            public float targetWalkMin;
            [Tooltip("서사 슬롯 — 기본 공란(LDB 서사 침묵 · §6 A12)")]
            public string narrativeSlot = "";
        }

        // 체크포인트(LDB-CHECKPOINT) — POI를 가리킨다.
        [Serializable]
        public sealed class CheckpointSpec
        {
            [Tooltip("체크포인트 ID")]
            public string id = "";
            [Tooltip("주막(온기) / 성황당")]
            public CheckpointKind kind = CheckpointKind.Shrine;
            [Tooltip("연결 POI ID")]
            public string poiId = "";
        }

        // 게이트 슬롯(층 5) — 기능 0(Spellcraft/Combat 후속). 청림 본편 경로 위 게이트 0(§6 A1).
        [Serializable]
        public sealed class GateSpec
        {
            [Tooltip("게이트 ID(Guk_CliffTop · Guk_LoggingTower · MinePreview)")]
            public string id = "";
            [Tooltip("게이트 종류 5종")]
            public GateKind kind = GateKind.Guk;
            [Tooltip("예고 / 재방문 / 본편 경로 위")]
            public GateMode mode = GateMode.Preview;
            [Tooltip("위치(타일 좌표 m · y = 절대 고도)")]
            public Vector3 position;
            [Tooltip("국 1회 수직 단차 m(A14 — 배치 규격, 코드 0)")]
            public float stepHeight = 5f;
            [Tooltip("보상 슬롯 ID(예고 게이트의 「보이지만 못 감」 시야 목표)")]
            public string rewardSlot = "";
        }

        // 인력 링크(§5-3) — §6 D1이 시야선·각크기·채도·중경 직선 ≤ MidAttractionMaxDistance를 검증.
        [Serializable]
        public sealed class AttractionLink
        {
            [Tooltip("시점 POI ID")]
            public string fromPoi = "";
            [Tooltip("목표 ID(POI · 개울 구간 · 원경 앵커 오브젝트 이름)")]
            public string toPoi = "";
            [Tooltip("인력 층 — Mid는 직선 상한 검사(라벨 이동 금지)")]
            public AttractionLayer layer = AttractionLayer.Mid;
            [Tooltip("인력원")]
            public AttractionSource source = AttractionSource.Silhouette;
            [Tooltip("시점에서 본 목표 방위 °(북 0° 시계)")]
            public float yaw;
            [Tooltip("허용 직선거리 상한 m(열람값 — Mid의 실제 상한은 WorldScaleSO 파생)")]
            public float maxStraightM;
        }

        // 시야선(§6 D1 입력) — 목표 = 씬 오브젝트 Renderer.bounds(이름으로 검색).
        [Serializable]
        public sealed class SightlineSpec
        {
            [Tooltip("시점 POI ID(눈높이 = WorldScaleSO.eyeHeight)")]
            public string fromPoi = "";
            [Tooltip("목표 씬 오브젝트 이름(POI 프리팹 · 앵커)")]
            public string target = "";
            [Tooltip("시점 yaw °")]
            public float yaw;
            [Tooltip("최소 각크기 °(0 = WorldScaleSO.sightlineMinAngularDeg)")]
            public float minAngularDeg;
        }

        // 도보 목표(§6 C1·C2·C5 입력) — NavMesh 경로 길이 ÷ MetersPerMinute.
        [Serializable]
        public sealed class WalkTarget
        {
            [Tooltip("출발 POI ID")]
            public string from = "";
            [Tooltip("도착 POI ID")]
            public string to = "";
            [Tooltip("목표 도보 분")]
            public float minutes;
            [Tooltip("허용 오차 분(±)")]
            public float tolerance;
        }

        // 컷 포즈 규칙(§6 컷 3장) — 좌표는 빌드 시 WorldBuildStamp.cuts[]에 고정.
        [Serializable]
        public sealed class CutSpec
        {
            [Tooltip("컷 ID(V1·V2·V3)")]
            public string id = "";
            [Tooltip("포즈 규칙 문자열(빌더가 해석 — poi=… · path=from>to@0.5 · yaw=… · pitch=…)")]
            public string rule = "";
        }

        // XZ 다각형 래퍼(Unity가 중첩 배열을 직렬화하지 못하므로).
        [Serializable]
        public sealed class PolygonXZ
        {
            [Tooltip("폐다각형 정점 XZ m(타일 좌표)")]
            public Vector2[] points = new Vector2[0];
        }

        // 식생 존 — 빛의 뼈대(§5-2)는 존별 상대 밀도로 만든다(A15).
        [Serializable]
        public sealed class VegetationZone
        {
            [Tooltip("존 폐다각형 XZ")]
            public Vector2[] poly = new Vector2[0];
            [Tooltip("상대 밀도 0~1(Z1 0.2 → Z4 0.8)")]
            public float density;
        }

        // 식생 산포 입력(층 9).
        [Serializable]
        public sealed class VegetationSpec
        {
            [Tooltip("밀도 존")]
            public VegetationZone[] zones = new VegetationZone[0];
            [Tooltip("제외 다각형(주막·역참·마을·비석 패드)")]
            public PolygonXZ[] exclusionPolys = new PolygonXZ[0];
            [Tooltip("덩굴 없음 다각형(벌목장 입구 — 「쇠 있는 곳 덩굴 없음」)")]
            public PolygonXZ[] noVinePolys = new PolygonXZ[0];
            [Tooltip("산포 시드(§6 A11 결정론)")]
            public int seed;
        }

        // ---- 시트 본문 ----

        [Header("정체")]
        [Tooltip("구역 ID(AreaId — AreaLoader·AreaIdEventChannelSO 페이로드)")]
        [SerializeField] private string areaId = "";
        [Tooltip("구역 씬 경로(Additive 로드 대상, 층 11)")]
        [SerializeField] private string scenePath = "";

        [Header("지형")]
        [Tooltip("Terrain 타일(S2 = 1장 + 예비 미생성)")]
        [SerializeField] private TileSpec[] tiles = new TileSpec[0];
        [Tooltip("합성 파라미터(층 3·§7 ④)")]
        [SerializeField] private TerrainSynthSO synth;
        [Tooltip("절벽/바위 키트 세트 이름(층 5)")]
        [SerializeField] private string cliffKitSet = "";

        [Header("길 · 개울 · 랜드마크")]
        [SerializeField] private PathSpec[] paths = new PathSpec[0];
        [SerializeField] private StreamSpec[] streams = new StreamSpec[0];
        [SerializeField] private LandmarkPlacement[] landmarks = new LandmarkPlacement[0];

        [Header("슬롯 — POI · 체크포인트 · 게이트")]
        [SerializeField] private PoiSpec[] pois = new PoiSpec[0];
        [SerializeField] private CheckpointSpec[] checkpoints = new CheckpointSpec[0];
        [SerializeField] private GateSpec[] gates = new GateSpec[0];

        [Header("인력 · 시야선 · 검증 입력")]
        [SerializeField] private AttractionLink[] attraction = new AttractionLink[0];
        [SerializeField] private SightlineSpec[] sightlines = new SightlineSpec[0];
        [SerializeField] private WalkTarget[] walkTargets = new WalkTarget[0];
        [SerializeField] private CutSpec[] cuts = new CutSpec[0];

        [Header("식생")]
        [SerializeField] private VegetationSpec vegetation = new VegetationSpec();

        public string AreaIdString => areaId;
        public AreaId Id => new AreaId(areaId);
        public string ScenePath => scenePath;
        public TileSpec[] Tiles => tiles;
        public TerrainSynthSO Synth => synth;
        public string CliffKitSet => cliffKitSet;
        public PathSpec[] Paths => paths;
        public StreamSpec[] Streams => streams;
        public LandmarkPlacement[] Landmarks => landmarks;
        public PoiSpec[] Pois => pois;
        public CheckpointSpec[] Checkpoints => checkpoints;
        public GateSpec[] Gates => gates;
        public AttractionLink[] Attraction => attraction;
        public SightlineSpec[] Sightlines => sightlines;
        public WalkTarget[] WalkTargets => walkTargets;
        public CutSpec[] Cuts => cuts;
        public VegetationSpec Vegetation => vegetation;

        // POI ID 검색 — 없으면 null(빌더·감사가 FAIL 문자열로 보고).
        public PoiSpec FindPoi(string id)
        {
            if (pois == null || string.IsNullOrEmpty(id))
            {
                return null;
            }
            foreach (var poi in pois)
            {
                if (poi != null && poi.id == id)
                {
                    return poi;
                }
            }
            return null;
        }

        // 길 ID 검색 — 없으면 null.
        public PathSpec FindPath(string id)
        {
            if (paths == null || string.IsNullOrEmpty(id))
            {
                return null;
            }
            foreach (var path in paths)
            {
                if (path != null && path.id == id)
                {
                    return path;
                }
            }
            return null;
        }
    }
}
