# 5강토 콘텐츠 위치 초안

State: TEST · 2026-09-12 · 바이블 및 Spec을 참고한 위치·동선 검토본

## 실제 구현

- 위치 83곳, 적·보스·던전 내부 캡슐 합계 79개, NPC 12명, 조사·이벤트 15곳, 던전 11곳.
- 지역별 지점: 청림 26, 적로 11, 철옹 12, 현강 17, 황경 17.
- 매크로 씬 `WorldMacro_Content_Layout` 아래에 지역별 실제 오브젝트를 만들었다. 모든 지점은 영속 ID, 실제 좌표, 서사 단계, 기준 장소와 Bible Section ID를 가진다.
- NPC·물증은 보행 중 가까이 가서 F로 설명을 확인한다. 왕소→화물 계약→성저 인도는 세션 안의 선행 확인만 검사하는 미리보기다. 반복 확인은 중복 진행을 만들지 않는다.
- 적은 무속성 또는 단일 속성 캡슐이다. 역할·활동 반경·위치만 저작했으며 기존 전투 AI를 붙이지 않았다. 보스 또한 위치 및 공간 예약이다.
- 던전은 지형을 뚫지 않은 지상 모듈 초안이다. 12m 방 3개, 폭4m 연결 통로, 12m 진입 경사로, 방별 적 후보와 마지막 조사대를 포함한다. 묘역은 천장이 열려 있다. 창고·굴·묘역의 완성 미술이나 지하 레벨은 아니다.
- 근거리 지점 220m, 던전·보스 500m 범위에서 표시한다. 지점별 Update 대신 관리자 하나가 0.2초 간격으로 갱신한다. 신규 지점 주변만 절차 식생 제외 영역으로 추가했다.

## 서사 해석과 보존

- 청림의 폐광 조사, 금표 주막, 벌목장 금속 덩굴 예습, 심부·뿌리굴, 신목 경계 경고와 청룡을 배치했다. 정담은 역참, 왕소는 객주 분소에 둔다.
- 적로는 화재 전장·전쟁 무덤·서로 다른 증언, 철옹은 군수 창고·망명 흔적·백호, 현강은 침수 폐허·현무·사찰·현운, 황경은 성저 인도·남문·도성 기록·왕궁 순서의 후보를 구성했다.
- 장영실 관련 작업 흔적의 철옹 배치는 제작 해석이다. 바이블에서 미정인 지역을 확정 설정으로 승격하지 않는다. 후반 인물과 보스는 단계 메타데이터를 붙인 검토 대상이며 본편에서 동시에 활성화되는 설정이 아니다.
- 청림 본선에 국 요구를 추가하지 않았다. 국·눈·숫·웅·뭄 관련 지점은 재방문 공간의 안내 후보다. 휴식 지점은 서비스 위치 표시이며 본편 회복·저장 기능을 새로 연결하지 않았다.
- 지형·도로·하천·자동차·기존 건축물의 재생성을 실행하지 않았다. 실제 건축물과 물 표면을 먼저 검사했고, 현강 창고가 기존 성벽에 겹치는 문제를 위치 이동으로 수정했다.
- 기존 씬 레코드 보존 비교: SceneRoots 등록 목록 제외 변경 1건 / 기준 레코드 6183개. 식생 진단 카운터 초기화를 제외한 실질 변경 0건. 상세는 `scene_preservation.json`. 기존 C2·프롤로그·PlayerRig·SpellBook 등 8파일은 별도 해시 검사에서 모두 일치했다.

## 검사 결과

- PASS unique persistent content IDs
- PASS every content entry has an actual scene object
- PASS resolved finite positions within world outline
- PASS event prerequisite references exist
- PASS Bible section provenance on all entries
- PASS layout preview does not alter existing combat/save state
- PASS content approach capsules outside existing structures
- PASS dungeon centre-route capsule clearance: 0 blocked / 1408 samples (not user traversal)

통로 검사는 높이1.75m·지름0.56m 캡슐의 공간 표본이다. 사용자의 실제 보행 완주나 경사로 이동 성공을 대신하지 않는다.

### 실제 Play Mode 호출

```
PASS unknown ID rejected
PASS delivery blocked before contract
PASS contract blocked before Wangso
PASS Wangso -> contract -> delivery preview sequence
PASS repeated inspection is idempotent
PASS evidence feedback available
PASS distant content culled by live Update
PASS disable restores edit visibility and clears focus
NOT_TESTED physical F key and user traversal; Inspect API exercised in Play.
```

### 미검증·미연결

- 실제 F키 입력, 플레이어의 전체 접근·던전 완주, 적 순찰·추격·전투, 보스전.
- 본편 퀘스트·보상·영구 저장·휴식·재방문 능력 판정, 후반 진행 상태별 출현/퇴장.
- 이번 콘텐츠 추가 후 독립 실행 120fps 성능. 과거 식생 측정값을 이번 결과로 재사용하지 않는다.
- 던전 외부에서 먼 가도까지의 완전한 접근로, 최종 건축물 미술과 지하 굴착. 표시 위치는 사용자 보행 검토 후 다듬을 수 있다.

## 검토 자료

- 실제 Unity 정지 이미지 9장, 1920×1080. 영상과 자동 보행 완주는 제작하지 않았다.
- 촬영 응답의 최대 시스템 커밋 64.5%; 각 촬영 전 85% 중단 기준 적용.
- `REVIEW.html`: 종류·지역·단계 필터가 있는 실제 좌표 지도, 지점별 설명, 스크린샷.
- `placements.csv`, `placement.json`: 파츠 이름이 아닌 실제 콘텐츠 위치와 출처 목록.
- `Content.asset`: 저작 데이터. `BeforeContent.unity`: 새 배치 전 매크로 씬 사본.
