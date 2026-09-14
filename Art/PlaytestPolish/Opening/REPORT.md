# 마을 관청 시작 — 저장·상호작용·UI 연결

2026-09-15. 배치 좌표와 아전 외형은 별도 씬 제작에서 주입한다. 이번 코드는 새 게임이 관청에서 의뢰를 확인하고 마석 자동차로 폐광을 찾아가는 짧은 흐름을 제공한다. 실제 차량 생성은 별도 차량 컴포넌트가 소유한다.

## 연결 계약

- `WorldMacroPlaytestSO.Opening`에 선택적 `WorldMacroOpeningProfileSO`를 연결한다. 미연결/비활성 프로필에서는 기존 폐광 시작을 유지한다. 원래 `Content.StartFeet/StartYaw`는 수정하지 않는다.
- 프로필 `StartFeet/StartYaw`가 관청의 새 게임·부활 지점이다. `Commission.Position/Radius/Prompt/Text`는 아전의 실제 대화 데이터다. ID는 `village_commission`, 종류는 Conversation, 보상은 0으로 검증한다. 프로필의 대화는 기존 Points와 읽기 전용으로 합쳐지며 중복 ID는 한 번만 노출한다. 아전의 `WorldMacroContentPoint.Id`도 같은 값으로 연결한다.
- 실제 새 슬롯에서만 체크포인트 `village_office`, 완료 ID `opening.village_office.started`를 저장한다. 기존 v2/v3 저장을 불러올 때 이 ID를 추가하거나 플레이어를 관청으로 옮기지 않는다. 손상 저장의 임시 플레이는 기존 폐광 fallback이며 새 관청 여정으로 등록하지 않는다.
- 새 관청의 첫 UI 바인딩에서는 기존 상세 내용창·Pause를 사용해 ‘관청 아전에게 폐광 폭파 조사 의뢰를 확인한다 [F]’ 안내를 보여준다. 실제 표시 후 `opening.village_office.intro_seen`을 저장한다. 저장 실패 시 표식을 되돌린다.
- 실제 가까운 거리·가려지지 않은 시선·F 상호작용으로 `village_commission`이 완료된다. 수락/거절 퀘스트 창이나 기록첩을 추가하지 않으며 폭파 배후를 설명하지 않는다. 의뢰 저장이 실패하면 신규 완료 ID를 되돌린다.
- `Session.OpeningCommissionReceived`는 이 관청 흐름에 등록되지 않은 기존 저장에서는 true다. 기존 차량 사용을 막지 않는다. 새 여정에서는 아전과 대화하기 전 false다.
- 차량 컴포넌트는 **실제 소환 성공 후에만** `TryRecordOpeningVehicleSummoned(out error)`를 호출한다. 이 메서드는 차를 만들지 않으며 `opening.village_office.vehicle_summoned`만 저장한다. 중복 표식·보상을 만들지 않고 저장 실패 시 추가 표식을 되돌린다. 기존 저장에서는 변경 없이 true를 반환한다.
- `OpeningObjective`는 아전 의뢰 확인 → 오행부 메뉴에서 자동차 소환 → 폐광 폭파 흔적 조사 순서의 한 줄이다. `EvidenceInteractionId`(기본 `mine_inquiry`) 완료 후 숨긴다. 현재 가까운 F 상호작용과 저장 오류 안내를 우선하며 별도 상시 HUD를 만들지 않는다.
- 관청 체크포인트는 복원·사망 위치·방향과 메뉴 하단 이름에서 처리한다. 이후 주막 휴식은 기존 `geumpyo_inn` 규칙으로 교체한다. 기록·통보·드롭·미회수 보상과 기존 체크포인트 복원은 유지한다.

## 검사 상태

- 통과: Data/App 전체 소스의 독립 Roslyn 컴파일. Unity 실행으로 표시하지 않는다.
- 준비: `Oheangbu.EditorTools.WorldMacro.WorldMacroOpeningStateReview.Execute("policy-check")`. 메모리상의 새 게임/기존 v2·v3/직렬화/비활성 프로필/손상 fallback/체크포인트/제거된 메뉴 경로를 검사한다. 실제 파일·플레이어·물리·장면에는 접근하지 않는다. 결과는 `Validation/opening_policy.json`에 남긴다.
- 미검증: 실제 씬의 관청 바닥·NPC 접근·F 대화·최초 메뉴·소환 성공 연계·사망/재실행 UI. 루트의 순차 Unity 검증 결과를 별도로 기록한다.
- 미실행: Unity 호출, 라이브 씬 수정, 캡처, 빌드, 유료 생성.

변경 전 핵심 파일은 `Baseline/manifest.json`과 해당 상대 경로에 보존했다. 기록 메뉴 제거는 `Art/UIAudio/JournalRemoval/REPORT.md`, 금빛 5소켓 끈 패찰의 후속 제작 기준은 `Docs/Specs/NOTE-MENU-OHAENGBU-PENDANT.md`를 참조한다.
