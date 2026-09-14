namespace Oheangbu.Data.World
{
    // [SPEC-WORLD-MAP §4 층 2] 배치 시트가 쓰는 열거형 전량 — 씬 타입 참조 0(Data는 Splines/Terrain을 모른다).
    // 전부 정수 직렬화 — 순서 변경 = 에셋 파손, 끝에만 추가한다.

    // 길 클래스 — S2는 전부 Trail. Gado = 마석 자동차 EA 예약(A1, 구현 0 · §6 C10 기록만).
    public enum PathClass
    {
        Trail = 0,
        Gado = 1,
    }

    // 개울 구간 종류(D6) — Ford = 도보 가능 여울(하상 ≤fordMaxDepth) / Deep = 심연(≥deepMinDepth · 양안 Parapet · Sill 턱).
    public enum StreamSegmentKind
    {
        Ford = 0,
        Deep = 1,
    }

    // POI 종류(§5-1) — 역참(Yeokcham)·객주(Gaekju)는 주막·마을과 별개 항목(A11).
    public enum PoiKind
    {
        MineExit = 0,
        Shrine = 1,
        Inn = 2,
        Stele = 3,
        Yeokcham = 4,
        Village = 5,
        Gaekju = 6,
        LoggingEntry = 7,
        Junction = 8,
        Vista = 9,
        Outcrop = 10,
    }

    // 체크포인트 종류(LDB-CHECKPOINT) — 주막(온기)·성황당(난소 직전).
    public enum CheckpointKind
    {
        Inn = 0,
        Shrine = 1,
    }

    // 게이트 5종(LDB-GATING) — 국·눈·문·숫·웅. 강토별 스킨만 교체(층 5).
    public enum GateKind
    {
        Guk = 0,
        Nun = 1,
        Mum = 2,
        Sut = 3,
        Ung = 4,
    }

    // 게이트 모드 — Preview = 예고(보이되 못 감) / Revisit = 재방문 국 / MainPath = 본편 경로 위(청림 = 0개, §6 A1).
    public enum GateMode
    {
        Preview = 0,
        Revisit = 1,
        MainPath = 2,
    }

    // 강토별 게이트 스킨(층 5) — 청림 국 = 수직 절벽 / 적로 = 덩굴 / 철옹 = 바위.
    public enum GateSkin
    {
        Cliff = 0,
        Vine = 1,
        Rock = 2,
    }

    // 인력 3층(LDB-ATTRACTION) — Mid는 직선 ≤ WorldScaleSO.MidAttractionMaxDistance(§6 D1, 라벨 이동 금지).
    public enum AttractionLayer
    {
        Far = 0,
        Mid = 1,
        Near = 2,
    }

    // 인력원 — 지속 발광 4종(Lantern·Candle·Vein·Beacon, ArtAudio 발광 상한) + 물길 + 실루엣.
    public enum AttractionSource
    {
        Lantern = 0,
        Candle = 1,
        Vein = 2,
        Beacon = 3,
        Water = 4,
        Silhouette = 5,
    }

    // 랜드마크 Terrain 컨폼(층 4·D8) — Raise/Carve = 발자국 안 지면 맞춤 / Hole = 발자국 안 홀(갱 입구) / None.
    public enum ConformMode
    {
        Raise = 0,
        Carve = 1,
        Hole = 2,
        None = 3,
    }

    // 랜드마크 콜라이더 방식(층 4) — UCX_ 볼록 조각 다수 / 단일 메시 ≤2000 tris / 없음.
    public enum ColliderMode
    {
        UCX = 0,
        Mesh = 1,
        None = 2,
    }
}
