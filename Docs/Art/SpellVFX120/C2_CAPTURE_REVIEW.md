# C2 실제 시전 캡처 검토 — 2026-09-09

판정: **시전 연결은 통과했으나, 전투 가독성·촬영 범위에 잔여 문제가 있어 최종 미술 승인은 보류한다.** 15자 × 3장의 실제 PNG 45장을 모두 열람했다. 촬영 구도 때문에 판단할 수 없는 부분을 효과 자산의 실패로 단정하지 않는다.

## 근거와 촬영 조건

- 원본: [GameplayCapture/20260909-012702-870](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCapture/20260909-012702-870/). 45장 모두 **1280×720**, 카메라 위치 `(0, 2.33, -5.8)`, 회전 `(0, 0, 0)`, FOV 60°, 종횡비 16:9다. 다른 전투 위치·카메라 각도를 촬영하지 않았다.
- [이 실행의 고정 manifest](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCapture/20260909-012702-870/capture_manifest.json)는 `PASS_RECOGNIZED_EVENT_RUNTIME_FIXTURE_ONLY`, 15개 완료·실패 0·오류 0, 실제 피해 26회, 정리 잔여 0을 기록한다. 카메라 렌더 대상 복구는 45장 모두 확인됐다. 이 수치는 미술 품질 통과가 아니다.
- 실제 `Camera.main.Render()`로 최초 생성 관측, 첫 실제 피해 콜백 관측, 수명 중간을 저장했다. 방어는 피해 대신 밝은 창 종료를 관측했다. `Sample` 재생·시간 이동 없이 정상 Update 중 저장했지만, 별도 렌더와 PNG 인코딩이 장당 약 **33.63~75.21ms**를 사용했다. 따라서 이 실행을 성능 측정으로 사용하지 않는다.
- 입력 인식 후의 이벤트 경계를 검증한 fixture다. 표적에는 표시 모델·콜라이더가 없고 카메라 전방 6m의 허공에 배치되어 있다. 실제 적 피부/갑주와의 접촉, 피격 반응, 지면 접지, 이동·락온 전환은 검증하지 않았다. 화면에 크게 보이는 X는 입력 순서를 재현한 두 진단 획이며, 각 글자의 새 VFX 본체가 아니다.
- [검토용 15개 시트와 출처](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCaptureReview/review_sources.json)는 각 글자의 세 원본을 시간순으로 나열한다. 텍스트는 원본 프레임 바깥에만 추가했고, 원본 PNG는 변경하지 않았다. **세 정지 화면은 연속 모션 영상이 아니다.**

## 우선 확인·수정할 문제

1. **노 — 가로 판으로 읽히는 화염과 화면 절단.** 수명 중간 `f001508`에서 주황색 면이 화면 폭 전체를 가로질러 양 끝이 잘린다. 불꽃의 높낮이·연무보다 두꺼운 평판 띠가 먼저 읽힌다. 밝기나 파티클 수를 일괄 증가시키기보다, 정면에서 불길의 부피와 빈 공간이 보이도록 주형상/높이/배치를 확인해야 한다. 현재 입구 구도가 범위 파악을 방해하므로 열린 지형에서도 같은 문제가 재현되는지 먼저 확인한다. [실제 중간 프레임](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCapture/20260909-012702-870/노_mid_lifetime_observed_f001508.png)
2. **모 — 지면과 낮은 황토색 선이 섞임.** `f001844`, `f001860`에서 새 효과는 먼 곳의 매우 얇은 황토색 가로 띠로 보인다. 회색 지면의 요철·풀과 겹쳐 위치와 진행 폭을 읽기 어렵다. 단순 색상 교체보다 어두운 선행 능선, 높이 변화, 밝고 어두운 면의 분리 등 지면 위 윤곽을 확보할 필요가 있다. 정확한 접지·지면 관통 여부는 이 이미지로 판정하지 않는다. [피해 관측](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCapture/20260909-012702-870/모_first_damage_observed_f001844.png), [중간](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCapture/20260909-012702-870/모_mid_lifetime_observed_f001860.png)
3. **사·서 — 밝은 배경에서 옅은 금속 경계.** 사의 작은 회청색 탄은 밝은 하늘·산 능선과 경계가 약하다. 서의 방벽도 회백색 지형과 비슷한 명도에 반투명 면이 겹쳐, 머의 흙색 몸체/밝은 문양 조합보다 경계가 약하다. 금속 계열의 밝은 면을 유지하면서 짙은 가장자리나 코어로 명도 차를 만들고 실제 전투 거리에서 확인할 대상이다. [사 중간](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCapture/20260909-012702-870/사_mid_lifetime_observed_f000720.png), [서 중간](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCapture/20260909-012702-870/서_mid_lifetime_observed_f005211.png)
4. **촬영 구도 — 캡슐과 입구가 공통 시야를 가림.** 캡슐이 화면 하단 중앙과 모든 방벽의 하단을 가리고, 문틀·바위가 화면 가장자리를 크게 차지한다. 모든 글자를 같은 좁은 창 너머에서 관측하므로 전체 방어 면적이나 광역 끝점을 판단하기 어렵다. 이는 재촬영 조건의 문제이며, 효과 크기를 일괄 줄일 근거가 아니다.
5. **공통 시각 우선순위 — 진단 획이 작은 탄보다 먼저 보임.** 단일탄의 피해 관측 시점에도 크고 밝은 X가 중앙에 남는다. 새 VFX는 그 위의 작은 물체로 보여 비행·도착점보다 획이 먼저 읽힌다. 진단용 X의 형태/면적 영향과 실제 획의 갈무리 전환을 구분해 확인해야 한다. 이번 검수만으로 기존 획 입력·필세·소멸 시간을 바꾸면 안 된다.

## 15자 전체 검토

각 행은 최초 관측·피해/방어 창 관측·중간의 세 장을 모두 확인한 결과다. 아래의 '구별됨'은 이 배경에서 색·형상이 보인다는 뜻이며 최종 아트 PASS가 아니다.

| 글자 | 실제 화면 관찰 | 판정 / 남은 검사 | 세 원본 시트 |
|---|---|---|---|
| 가 | 진단 획 위의 작은 짙은 녹색 뾰족 탄이 하늘과 분리된다. 중간에는 방향이 바뀐 작은 쐐기처럼 보인다. | 색은 구별됨. 가지/가시의 세부와 비행 연속성은 작고 성긴 표본이라 미검증. | [가](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCaptureReview/01_가_actual_three_frames.png) |
| 나 | 주황 탄과 바깥으로 벌어지는 면이 밝은 하늘 앞에 보인다. 중앙 X보다 작다. | 화 계열 색은 구별됨. 연속 상승 불꽃·연무의 물성과 실제 접점은 미검증. | [나](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCaptureReview/02_나_actual_three_frames.png) |
| 마 | 황토색 덩어리와 가느다란 꼬리가 보인다. 밝은 하늘과는 구별된다. | 단일 흙 탄의 존재는 확인. 바위 배경에서 같은 대비인지, 꼬리가 부자연스럽게 남는지는 연속 영상 필요. | [마](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCaptureReview/03_마_actual_three_frames.png) |
| 사 | 작은 회청색 쐐기와 소수 점이 산 능선 위에 보인다. 흰 진단 획이 훨씬 두드러진다. | 대비 보완 대상. 밝은 배경에서 탄의 경계·이동 방향 가독성이 약함. | [사](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCaptureReview/04_사_actual_three_frames.png) |
| 아 | 짙은 청색 작은 탄과 밝은 청백색 잔무늬가 하늘과 분리된다. 중간에는 진단 획이 없어 탄을 더 쉽게 찾는다. | 색 구별됨. 물방울/물탄의 물성 및 궤적은 세 장만으로 통과 불가. | [아](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCaptureReview/05_아_actual_three_frames.png) |
| 고 | 넓은 녹색 가시 군집이 크게 보이고 아래 문양 일부가 보인다. 평평한 면과 직선 꼭짓점이 뚜렷하다. | 범위의 존재는 구별됨. 근접 캡슐 가림, 실제 지면에서의 가시 뿌리·접지 및 나무 질감은 재검수. | [고](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCaptureReview/06_고_actual_three_frames.png) |
| 노 | 첫 피해 관측은 거의 진단 획만 보이며, 중간에 화면 전체를 가로지르는 주황 평판 띠가 나타난다. | 우선 수정/재촬영. 원뿔형 불길의 폭·부피·화염 실루엣을 읽기 어렵고 양 끝이 잘림. | [노](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCaptureReview/07_노_actual_three_frames.png) |
| 모 | 피해/중간에 지면과 가까운 얇은 황토색 가로 선만 희미하게 보인다. | 우선 대비·형상 보완 대상. 진행부/폭/높이를 지형과 구분하기 어려움. | [모](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCaptureReview/08_모_actual_three_frames.png) |
| 소 | 은회색 작은 파편과 파란 점들이 보인다. 중간과 첫 피해는 서로 인접한 프레임이다. | 형상 존재는 확인. 여러 발의 발사 순서·9회 타격은 이 두 근접 표본으로 시각 검증 불가. 수치 기록과 구분. | [소](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCaptureReview/09_소_actual_three_frames.png) |
| 오 | 청색 바닥과 흰 물마루가 넓은 물결로 보이고, 중간에는 규모가 작아진다. | 이 배경에서 물결·색이 비교적 잘 구별됨. 실제 지면 접점·이동 거리·물성 연속성은 미검증. | [오](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCaptureReview/10_오_actual_three_frames.png) |
| 거 | 녹색 굽은 가지/잎 띠와 밝은 전통 문양이 보인다. 캡슐이 방벽 하단을 가린다. | 목 방벽과 문양은 구별됨. 전체 방어 면적·피격 접점은 재촬영 필요. | [거](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCaptureReview/11_거_actual_three_frames.png) |
| 너 | 세로 주황 불잎 모양의 띠와 옅은 원형 문양이 보인다. 중간에도 유지된다. | 색과 방벽 유지 상태는 구별됨. 평면 불잎 인상이 강하며 불길의 연속 물성·피격은 미검증. | [너](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCaptureReview/12_너_actual_three_frames.png) |
| 머 | 황토색 판/돌 덩어리와 밝은 둥근 문양의 명도 차가 뚜렷하다. | 방벽 구별은 양호. 캡슐에 가린 하단과 전체 실루엣, 실제 방어 방향·피격은 미검증. | [머](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCaptureReview/13_머_actual_three_frames.png) |
| 서 | 회청색 반투명 뾰족 면과 밝은 원형 문양이 겹친다. 밝은 지형이 뒤로 비쳐 가장자리가 약하다. | 대비 보완 대상. 더 짙은 경계와 밝은 면의 구별을 실제 전투 거리에서 확인. | [서](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCaptureReview/14_서_actual_three_frames.png) |
| 어 | 짙은 청색 곡선들과 밝은 물결 문양이 대칭으로 펼쳐진다. | 이 배경에서 색·문양이 구별됨. 하단은 캡슐에 가리고 실제 방어 충돌은 미검증. | [어](C:/Users/yj666/Oheangbu/Art/SpellVFX120/GameplayCaptureReview/15_어_actual_three_frames.png) |

## 재검수 조건

입구 밖의 열린 C2 전투 구역에서 지면 위 시전자와 보이는 표적을 사용해 다시 촬영한다. 실제 플레이 카메라의 정상 조작/위치를 기준으로 하고, 가림 확인을 위한 별도 검수 시점은 플레이 화면과 구분한다. 지면형은 실제 지면 좌표와 충돌 판정을 확인하고, 방벽은 하단과 좌우 끝이 보이는 시점을 추가한다. 실제 시전 경로·피해 시계는 그대로 둔다.

노·모·사·서는 같은 배경과 더 어두운 바위/건물 배경을 함께 확인할 우선 항목이다. 소의 연속 발사와 모든 효과의 출발→전개→피해→소멸은 세 정지 표본 대신 짧은 실제 Update 영상으로 확인해야 한다. 보이지 않는 순간을 건너뛰거나 예시 `Sample` 시점으로 대체한 자료를 실전 연속 검증으로 표시하지 않는다.

이 검수는 파일 읽기와 검토용 시트/문서 작성만 수행했다. Unity·게임 코드·원본 캡처를 변경하지 않았다.
