# 오행부의 가마 호출

실제 기존 가마 한 대를 근처 큰길의 안전한 빈자리로 옮기는 표현·편의 기능이다. 새 차량을 생성하거나 프로필·운전 설정을 초기화하지 않는다. 큰길 밖 운전을 제한하는 규칙은 추가하지 않았다.

## 연결

씬의 기존 차량 또는 세션 루트에 `WorldMacroPalanquinSummon` 하나를 추가하고 `Vehicle`, `Seat`, `Walker`, `WorldSheet`를 지정한다. UI는 관청 의뢰/기존 저장의 접근 조건을 확인한 뒤 다음을 호출한다.

```csharp
bool moved = summon.TrySummonFromMenu(Pause, out string message);
// message는 실제 텍스트 UI로 표시.
// moved == true일 때만 Session.TryRecordOpeningVehicleSummoned(out error) 호출.
```

소환은 정상적인 메뉴 정지와 공통 입력 차단 상태에서만 실행한다. 차량 탑승·이동·회전·구동 토크, 플레이어 공중·회피·갈무리·사망 중에는 거절한다. 성공 후 1.5초 동안 재입력을 억제한다. 이 기능은 UI를 닫거나 사용자의 시간을 재개하지 않는다.

## 배치 조건

- `WorldMacroSheetSO.RouteSpec.Carriage == true`인 실제 가도 데이터만 사용한다.
- 초기 검색 반경 80m, 플레이어와 6m 이상 거리, 높이 차 18m 이하. 넓이는 차체 및 출입 공간을 감당해야 한다.
- 실제 scene Collider에서 4바퀴 접점과 차체 밑 9개 지점을 찾는다. 14도 이하 경사와 제한된 좌우·전후 지지 높이 차를 요구한다.
- 차체 Box, 바퀴 Sphere, 출입 Capsule 공간을 확인한다. 플레이어·NPC·다른 건물·바위는 장애물이며, 기존 차량 자신의 콜라이더만 제외한다.
- 물길의 수면 높이 아래인 지지면은 거절한다. 실제 교량 바닥이 수면 위라면 가능하다.
- 플레이어에서 출입 위치까지 벽·산자락 등으로 가려지는 후보는 거절한다.
- 모든 확인을 마친 뒤 `Rigidbody.position/rotation`을 한 번 변경하고 속도를 0으로 한다. 실패 경로에서는 위치·프로필·저장에 쓰지 않는다.

## 검증 상태

- Unity 6000.3.9 관리 어셈블리를 참조한 해당 Runtime 클래스 컴파일: 통과(0오류).
- 실제 씬 지지·공간 검사: `WorldMacroPalanquinSummonReview.Execute("audit")` 실행 결과 `vehicle_call_query.json`으로 판단한다. 가상 query origin만 사용하며 플레이어/차량을 움직이지 않는다.
- 실제 메뉴 호출 성공·실패·중복 방지·탑승 복원·저장 이력: 미검증. 부모 통합 후 별도 확인 대상이다.

이 문서는 씬 연결 전에 작성했으며, 확인하지 않은 항목을 기능 검증 완료로 취급하지 않는다.
