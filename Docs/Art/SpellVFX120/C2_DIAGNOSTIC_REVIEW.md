# C2 진단 카메라 실제 시전 화면 검수 — 2026-09-09

**판정: 촬영 조건은 개선됐으나 미술 승인은 보류한다.** 15자 × 실제 프레임 3장, 합계 45장을 모두 열람했다. 이전 자료의 가림과 허공 표적 문제를 줄여 지면형의 높이·표적 위치를 읽기 쉬워졌다. 반면 노의 피해 관측 시점 가시성, 모의 낮은 명도 대비, 근접 방벽의 화면 절단은 남는다. 진단 카메라의 개선을 실제 플레이 카메라나 효과 자산 자체의 수정 완료로 환산하지 않는다.

## 근거와 판정 범위

- 현재 원본: [20260909-021340-911-diagnostic](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCapture/20260909-021340-911-diagnostic). 45장 모두 1280×720, 16:9다. [실행 보고서](C:/Users/yj666/Oheangbu/Art/SpellVFX120/gameplay_diagnostic_camera_audit.json)와 [검토 출처·원본 SHA256](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/review_sources.json)를 함께 남겼다.
- 카메라는 `(0, 3.11139, 1.2)`, 회전 `(17.67968, 0, 0)`, FOV 60°다. 기존 `(0, 2.33, -5.8)`, 회전 0° 화면과 위치·피치·투영 기준이 다르므로 동일 구도의 자산 A/B 검사가 아니다. 방어 효과가 더 크게 보이는 것도 자산 크기 변경의 증거가 아니다.
- 지면 중심은 `(0, 0.11139, 9.2)`, 시전자 fixture 위치는 `(0, 0.04081, 3.2)`다. 세 검은 직육면체는 진단용 표적 표시이며 실제 적 모델·콜라이더·AI가 아니다. 그림자와 바닥 면을 함께 볼 수 있지만 물리 충돌·내비게이션·정밀 접지 오차의 통과는 아니다.
- 실행 JSON은 `PASS_RECOGNIZED_EVENT_RUNTIME_FIXTURE_ONLY`, 15개 완료, 실패·오류 0, 실제 피해 콜백 26회, 정리 잔여 0을 기록한다. **시전 이벤트→생성→피해 시계의 fixture 수치 통과**이며, 인식기·실제 입력·플레이어 DI·피격 반응·미술 통과가 아니다.
- 45장 모두 diagnosticCamera/cameraStateRestored/fixtureStrokeVisibilityRestored가 true다. 진단용 두 획만 촬영 시 가려졌고 플레이어 카메라 자체는 바뀌지 않았다. 이번 자료에는 이전의 밝은 X가 없다.
- 실제 Update 관측 중 저장했으며 시간 이동·Sample 재생으로 대체하지 않았다. PNG당 렌더·인코딩 비용은 약 40.44~152.93ms다. 촬영이 관측 프레임 간격을 늘릴 수 있으므로 이 3장으로 연속 모션·성능·정확한 표시 시작 프레임을 통과 처리하지 않는다.
- 검토 시트는 원본 픽셀을 수정하지 않고 세 장을 시간순으로 세로 배치한 보조 자료다. 설명·라벨·색 보정·임의 대체 프레임을 넣지 않았다. 원본 16:9 파일이 검수의 근거다.

## 우선 남은 세 문제

1. **노: 피해 관측과 눈에 보이는 화염 사이의 간극, 이후 근접 절단.** [첫 피해 관측 f001817](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCapture/20260909-021340-911-diagnostic/노_first_damage_observed_f001817.png)은 효과 age 0.3950초, 이미 피해 3회가 관측된 프레임인데 화면에서 화염 본체를 뚜렷하게 찾을 수 없다. [중간 f001907](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCapture/20260909-021340-911-diagnostic/노_mid_lifetime_observed_f001907.png) age 0.9001초에는 전방으로 벌어진 큰 주황 불잎 군집이 나타나며 시작부가 화면 하단 밖으로 잘린다. 기존의 가로 평판 하나로 보이던 인상은 이 시점에서는 완화됐지만, 얇고 반복되는 잎/판 형태가 여전히 먼저 읽힌다. 이 두 이미지로 정확한 지연 시간이나 코드 원인을 단정하지 않는다. 피해 시점 전후 실제 영상과 옆 시점으로 표시 위치·크기·전개 시각을 확인할 대상이다. [노 전후](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/compare_노_old_above_diagnostic_below.png).
2. **모: 형상은 보이지만 밝은 지면에 약한 능선.** [피해 관측 f002313](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCapture/20260909-021340-911-diagnostic/모_first_damage_observed_f002313.png)과 [중간 f002325](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCapture/20260909-021340-911-diagnostic/모_mid_lifetime_observed_f002325.png)에서 이전의 얇은 가로 선보다 높이와 좌우 폭이 분명해졌다. 그러나 옅은 황토색 반투명 능선의 밑선과 옆면이 회색빛 지면에 섞이며, 중앙은 검은 표적에 가려져 앞뒤 범위가 끊겨 읽힌다. 진행 전선과 단순 먼지 덩어리를 구별하는 짙은 선행 윤곽/면 대비가 부족하다. 이것은 관찰한 미술 잔여 문제이며, 관통·비접지나 실제 범위 계산 실패라는 판정은 아니다. [모 전후](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/compare_모_old_above_diagnostic_below.png).
3. **근접 방벽: 시야 대부분을 점유하고 위쪽이 잘려 전체 면적 검수 불가.** 거·너·머·어의 방어 창 종료 및 중간 프레임에서 본체/문양이 화면 중앙을 크게 차지하고 일부 위쪽 끝은 프레임 밖으로 나간다. 특히 머는 불투명 황토 몸체와 굵은 밝은 문양 때문에 세 표적의 대부분이 가려진다. 기존의 캡슐 가림은 사라졌지만 이 진단 시점만으로 전체 방어 실루엣과 적의 공격 방향 가독성을 통과시킬 수 없다. 이는 현재 촬영 거리·투영을 포함한 확인된 화면 문제다. 게임 방벽 크기를 바로 줄일 근거로 쓰지 말고 실제 숄더뷰와 별도 전체 실루엣 시점을 함께 확인해야 한다. [머 중간](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCapture/20260909-021340-911-diagnostic/머_mid_lifetime_observed_f005931.png), [어 중간](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCapture/20260909-021340-911-diagnostic/어_mid_lifetime_observed_f007712.png).

## 이전 검수와 달라진 점

| 항목 | 이번 실제 화면 | 판정 |
|---|---|---|
| 노 판형 화염 | 가로 통판에서 세워진 불잎 군집으로 읽히며 좌우 바깥 조각이 분리된다. 새 화면에서는 하단이 잘린다. | **부분 개선 / 잔여 실패.** 촬영 기준이 달라져 자산 수정 효과를 분리 입증하지 못함. 피해 관측 때 미표시와 얇은 판 인상은 재검수 필요. |
| 모의 낮은 대비 | 표적 아래 약한 황토 능선의 높이·폭이 눈에 들어온다. 하단·옆선의 명도 차는 여전히 작다. | **부분 개선 / 대비 보류.** 다른 지면과 옆 시점에서 진행 윤곽 확인 필요. |
| 사의 밝은 배경 경계 | 피해/중간의 작은 회백색 탄은 검은 중앙 표적 위에서 찾을 수 있다. 중간에는 매우 얇은 밝은 선처럼 보인다. | **어두운 표적 위 존재 확인, 밝은 배경 대비 미검증.** 현재 표본은 배경이 달라졌고 비행 중의 또렷한 실루엣이 부족하므로 기존 문제 해결로 선언하지 않음. [사 전후](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/compare_사_old_above_diagnostic_below.png) |
| 서의 금속 방벽 경계 | 커진 화면상 형상에서 명암 면·외곽·네 개의 뾰족한 몸체를 구별할 수 있다. 밝은 문양의 아래쪽은 지면과 명도가 가깝다. | **이번 시점의 본체 경계는 개선되어 보임.** 근접 확대와 피치 변경의 영향이 있어 정상 전투 거리·밝고 어두운 배경 검사는 여전히 미검증. [서 전후](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/compare_서_old_above_diagnostic_below.png) |
| 표적 접지·가림 | 표적 하단과 그림자가 지면에 이어져 보이고, 고·모·오의 높이 비교가 가능하다. 입구 문틀·캡슐·X가 중앙에서 제거됐다. | **검수 조건 개선.** 표적 모델 접촉·지형 오차·충돌은 미검증. 모·오의 중앙 진행부는 불투명 표적에 가려진다. |

## 15자 × 3장 전체 관찰

각 행의 시트에서 위→아래는 실제 관측 시각 순서다. 소는 중간 관측이 첫 피해 관측보다 먼저 기록되어 그 순서를 유지했다. 생성 직후 약 0.005~0.011초의 희미한/빈 화면은 단독으로 생성 실패라고 판정하지 않았다.

| 글자 | 세 프레임 실제 관찰 | 미술 판단 / 남은 검사 | 출처 |
|---|---|---|---|
| 가 | 최초 작은 녹색 점/선, 피해 때 검은 표적 가슴 부근 녹색 덩어리와 큰 옅은 문양, 중간에는 얇은 녹색 선. | 존재 확인. 가시 세부·비행 방향은 작은 크기와 표적 가림 때문에 미검증. | [가](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/01_가_three_actual_frames.png) |
| 나 | 최초 거의 비어 있고 피해 때 주황색 작은 면과 문양, 중간에 작은 주황 조각들이 표적 주변에 남음. | 색은 식별됨. 불꽃 부피·연속 운동·실제 피부 접점 미검증. | [나](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/02_나_three_actual_frames.png) |
| 마 | 최초 거의 비어 있고 피해 때 황토 덩어리·꼬리와 큰 원형 문양, 중간에도 황토 덩어리가 표적을 일부 가림. | 색 구별됨. 바위 물성·꼬리 소멸·표적 접촉은 정지 표본만으로 통과 불가. | [마](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/03_마_three_actual_frames.png) |
| 사 | 최초 극소 점, 피해/중간은 중앙 검은 표적 위 회백색 작은 면/선. | 표적과의 대비는 확보되지만 밝은 지형 앞 비행 경계는 미검증. | [사](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/04_사_three_actual_frames.png) |
| 아 | 최초 거의 비어 있고 피해 때 파란 작은 탄, 중간에 짧은 밝은 흔적과 표적 옆 파란 면. | 물 계열 색 존재 확인. 물방울 변형·전후 이동은 미검증. | [아](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/05_아_three_actual_frames.png) |
| 고 | 피해/중간에 지면 주변으로 솟은 녹색 가시 군집. 표적과 높이·반경을 비교할 수 있음. | 존재·바닥 주변 배치 확인. 동일한 단순 쐐기 반복과 평평한 면이 강해 자연물 미술은 보류. | [고](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/06_고_three_actual_frames.png) |
| 노 | 최초/첫 피해 때 뚜렷한 화염을 찾기 어렵고, 중간에 화면 아래에서 큰 불잎 군집이 벌어짐. | 우선 잔여 문제 1. 실제 피해 근처 영상과 측면 촬영 필요. | [노](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/07_노_three_actual_frames.png) |
| 모 | 피해/중간에 표적 발 주변 옅은 황토 능선. 중앙은 표적에 가려짐. | 우선 잔여 문제 2. 실제 진행 전선과 저명도 대비 보류. | [모](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/08_모_three_actual_frames.png) |
| 소 | 중간·첫 피해 프레임에서 크기가 다른 은회색 다각형 탄 여러 개가 서로 다른 위치에 보임. | 복수탄 존재 확인. 9회 발사/피해의 시각 순서와 속도는 이 두 근접 표본으로 미검증. | [소](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/09_소_three_actual_frames.png) |
| 오 | 피해 때 청회색 낮은 파도와 흰 마루가 표적 앞, 중간에는 뒤편 양옆으로 보임. | 모보다 마루/몸체 구별이 쉽다. 중앙 가림 때문에 연속 통과·지면 접점은 미검증. | [오](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/10_오_three_actual_frames.png) |
| 거 | 녹색 굽은 띠·잎과 밝은 연화형 문양이 크게 펼쳐지고 중앙에 짙은 파란 잎 모양 조각이 보임. | 목 실루엣은 구별됨. 상단 절단·반복 띠의 평판성·표적 가림으로 최종 미술 보류. | [거](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/11_거_three_actual_frames.png) |
| 너 | 주황색 불잎 띠와 밝은 원형 문양이 넓게 유지됨. 윗부분이 잘리고 중앙 표적이 크게 가려짐. | 화 계열은 구별됨. 판 형태의 반복·전체 면적·피격 접점 보류. | [너](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/12_너_three_actual_frames.png) |
| 머 | 큰 황토 덩어리와 밝은 굵은 문양이 겹쳐 중앙을 거의 덮음. | 명도 차는 뚜렷하지만 방벽 뒤 표적 가독성이 낮음. 전체 실루엣/정상 카메라 검수 필요. | [머](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/13_머_three_actual_frames.png) |
| 서 | 길고 뾰족한 회청색 몸체와 밝은 문양, 명암 면을 구별 가능. 중앙 아래에 파란 작은 조각과 밝은 흔적이 보임. | 현재 본체 경계는 이전보다 읽기 쉬움. 정상 거리·배경 대비와 재질/색 일관성 최종 미검증. | [서](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/14_서_three_actual_frames.png) |
| 어 | 짙은 청색 곡선과 밝은 물결 문양이 크게 펼쳐짐. 하단 곡선은 확인되지만 상단이 잘림. | 색·문양 구별됨. 표적 시야와 상단 실루엣 검수 보류. | [어](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayDiagnosticReview/15_어_three_actual_frames.png) |

## 이번에 하지 않은 판정

물리 입력·문자 인식 정확도, 실제 캐릭터 손/붓, 원래 카메라 조작과 락온, 적 AI·실제 피격·패링, 30/60/120fps, 성능, 120개 전체 효과, 연속 동작·정밀 접지·지면 관통은 이 자료로 통과시키지 않았다. 미표시 원인을 셰이더·카메라·타이밍 중 하나로 단정하지도 않았다.

이 작업은 파일 열람, 보조 시트와 이 문서 작성만 수행했다. Unity·코드·원본 PNG는 변경하지 않았다.
