# 기록 메뉴 제거

2026-09-15. 기록첩 탭·페이지, 조사/대화창의 재열람 버튼과 안내문, 메뉴 검토의 기록 페이지·촬영 ID·기록용 데모 상호작용 생성을 제거했다. 조사·대화창 자체는 확인 버튼으로 계속 닫을 수 있다. 지원하지 않는 페이지 요청은 일시정지·저장·현재 페이지를 바꾸기 전에 거절한다. 빈 탭이나 비활성 버튼은 남기지 않는다.

`WorldMacroProgress`, `WorldMacroUiProgress`, `WorldMacroCollectionCatalog`, 입력 액션의 변경 전후 SHA-256이 일치했다. 조사·NPC 완료 ID, 구버전 `ui.records`, 저장 이행과 기존 보상 중복 방지는 삭제하지 않았다. 실제 저장 파일에는 접근하거나 쓰지 않았다.

- 통과: 관련 메뉴 partial 4개와 Editor 검토 파일의 독립 Roslyn 컴파일. 기존 `settingsBuilt` 미사용 경고 1개만 남음.
- 통과: 기록 메뉴/검토 경로 정적 제거 검사와 저장 관련 4개 파일 해시 보존.
- 통과: 현재 메뉴 검토 HTML·보고서 재생성 및 로컬 링크 검사. 과거 기록 스크린샷과 검사 JSON은 보존하되 현재 페이지 목록에서 제외했다. 기존 캡처 사이드바의 기록 탭은 과거 구현임을 페이지에 명시했다.
- 미검증: 실제 Unity 메뉴 개폐·내용창 확인·저장 왕복. Unity 호출·캡처·빌드 없음.

근거는 `Validation/journal_removal.json`, `Validation/compile.log`다. 변경 전 파일은 `Baseline/manifest.json`과 해당 상대 경로에 보존했다. 후속 금속 패찰의 참조 기준은 `Docs/Specs/NOTE-MENU-OHAENGBU-PENDANT.md`에 기록했으며, 유료 이미지/모델 생성이나 모델 교체는 실행하지 않았다.
