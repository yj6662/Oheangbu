# 금표 주막 C2 원형 복원

## 확인한 원형

- 기준 씬: `Assets/_Project/Scenes/Dev/C2_CodexWorld.unity`의 `04_Mountain_Inn/Thatched_Inn`.
- 복원 프리팹: `Assets/_Project/Art/CodexWorld/ThatchedInn/ThatchedInn.prefab`.
- 원천 공급자 모델: `Assets/House_1/house.fbx`. ㄱ자 평면, 높이가 다른 이어진 초가 지붕, 흰 회벽과 어두운 목재, 앞마당·석단·마루 구조다.
- 초기 실제 캡처: `Oheangbu/Screenshots/CodexWorld/cut4_inn.png`.
- 현재 교체 대상은 `Playtest_OwnedAssets/Geumpyo_ThatchedInn`이다. `House_2/house2 .fbx`의 일부 벽·기둥·지붕을 재조립한 직사각 공용실이라 원형과 다르다.

## 구현 API

`Oheangbu.EditorTools.WorldMacro.WorldMacroOriginalInn.Execute`에 `inspect`, `apply`, `validate`를 전달한다. 직접 호출용 `Apply()` / `Validate()`도 공개한다. 이 작업에서는 Unity 명령이나 씬 변경을 실행하지 않았다. 부모 작업자가 직렬로 호출해야 한다.

`apply`는 원본 프리팹 전체를 인스턴스화한다. C2의 0.8/1.2/0.8 배율과 11개 재질 그룹, 석단 5단, 등롱 2개를 보존한다. 마당이 기존 접근 방향인 월드 -Z를 향하도록 180도 회전한다. 기존 휴식·벌목꾼·약초꾼은 앞마당에서 만날 수 있도록 좌표를 그대로 두고, 초가를 그 뒤에 배치한다.

기존 소스 파츠의 렌더러는 Material_Batches 때문에 비활성 상태이므로, 구 주막 전체를 보관 비활성화하고 마루 12타일과 낮은 휴식 탁자만 별도 복제해 활성화한다. 마루는 공급자 `SeyeonjeongPavilion/Prefabs/SM_Maru_2.prefab`에서 기존 작업이 추출해 둔 메시를 그대로 재사용한다. 넓은 구 공용실 바닥 콜라이더는 끄고 좁힌 마루의 실제 bounds로 충돌면을 둔다. 기존 진입 경사면은 남긴다. 원형 씬·프리팹·공유 재질·메시는 수정하지 않는다.

## 확인해야 할 항목

- `apply` 후 `inn_validation.txt`에서 기존 휴식·NPC 접근과 체크포인트 안전복원이 통과하는지 확인한다.
- C2의 원래 계단 중심선은 로컬 `x=-1.5`, `z=4.6→-1.9`로 샘플링한다. 언덕이 원형 콜라이더를 파고들거나 마루·기단 사이 높이가 맞지 않으면 FAIL로 남긴다. 초기 위치는 조정 가능한 출발값이므로 부모가 실제 스크린샷과 물리 샘플로 최종 교정해야 한다.
- 원형의 두 Point Light 설정을 유지했다. 필요하면 인스턴스 오버라이드로만 그림자 비용을 조정하고 원본 재질이나 환경 노출은 변경하지 않는다.
- 기존 건물 뒤로 새 L자 원형이 조금 더 나가므로, 배치 후 식생·지형 관통을 확인한다.
- 여관이 작아졌기 때문에 전 작업에서 바깥에 놓은 `Playtest_EarlyArt/Inn_FuelStore`는 별도 검토 대상이다. 이 helper는 해당 묶음에 간섭하지 않는다.
- 새 빌드, 캐릭터, 게임 수치와 NPC 대사는 변경하지 않는다. 실제 보행 완주는 미검증이다.
