# KTP 통합 영상 04 — PNG 표본 시각 검토

판정: **관찰 기록 / 최종 미술 승인 아님**. 2026-09-09에 메인 6개·외부 4개, 총 10개 영상에 대응하는 원본 연속 PNG에서 118개 표본을 실제 열람했다. 각 영상의 등장·전개·유지·감쇠·Life 이후 마지막 프레임을 포함했다. 선택 표본에서는 불투명한 격자 흰 판의 재발, 효과 전체의 갑작스러운 위치·크기 점프, 종료 뒤 남은 소환수·문양·광입자가 보이지 않았다. 전체 925장의 모든 중간 프레임을 눈으로 검사한 결과는 아니므로 한 프레임짜리 튐까지 없다고 단정하지 않는다.

## 자료와 검증 범위

- 원본: `KTPIntegratedFrames04` 6개, `KTPExternalFrames04` 4개. 각 PNG는 1280×720이며 `*_capture.json`의 `sampledSeconds`로 시간을 읽었다.
- 대응 영상: `ClipsKTPIntegrated04` 6개, `ClipsKTPExternal04` 4개. 인코딩 보고서의 공통 상태는 `VERIFIED_ENCODING_AND_TIMING_ONLY`다. 이 수치를 미술 통과로 사용하지 않았다.
- 모든 촬영 메타의 실행 어셈블리 MVID는 `78360a6f-5231-421c-8955-254a090925e4`다. C2 배경이지만 `demonstrationCues=true`, `gameplayConnectionVerified=false`, `PRESENTATION_REVIEW_ONLY`다. 따라서 이 자료는 실제 입력 인식·피해 판정·전체 PlayerRig 배선의 검증 자료가 아니다.
- 메인은 `Main Camera`, 외부는 `VFX External Inspection Camera`다. 외부 시점은 검수용 카메라다. 캡슐은 검수용 표적이며 마지막 프레임에 남은 캡슐을 VFX 잔존으로 세지 않았다.
- 이번 작업은 파일 열람과 이 보고서 작성만 수행했다. Unity·Blender 실행, 원본 PNG·MP4·소스 변경은 없다.

## 주요 관찰

1. **구의 촬영 범위가 불충분하다.** 메인 유지 구간에서는 큰 투명 문양막이 화면 하단에서 잘린다. 외부에서는 높이가 있는 둥근 막과 바닥 원형을 확인할 수 있으나 문턱/지형 경계가 하단을 가린다. 이를 격자 흰 판 재발로 보지는 않지만, 이 시점만으로 방어막 전체 경계와 접지를 승인할 수 없다. [메인 1.208초](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTPIntegratedFrames04/019_AD6C/0029.png), [외부 1.208초](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTPExternalFrames04/019_AD6C/0029.png).
2. **사의 밝은 선화는 눈 배경과 섞인다.** 점광과 문양이 보이지만 가의 청록색 타격 문양보다 경계 대비가 약하다. 전체를 덮는 불투명 사각판은 보이지 않는다. [사 0.583초](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTPIntegratedFrames04/073_C0AC/0014.png), [가 0.833초](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTPIntegratedFrames04/001_AC00/0020.png).
3. **곰·놈·솜의 3D 소환수는 정적인 모델이다.** 외부 시점에서는 사슴형·불짐승형·호랑이형 실루엣과 원형 소환진을 구별할 수 있다. 몸체는 같은 자세와 형상을 유지하며 등장/소멸 때 투명도가 변한다. 이것은 걷기·호흡·공격·리깅 동작의 증거가 아니다. 발동 이후 소환진도 상당한 구간 같은 구성으로 유지되어 정적인 인상이 남는다. [곰 유지](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTPExternalFrames04/016_ACF0/0056.png), [놈 유지](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTPExternalFrames04/040_B188/0055.png), [솜 유지](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTPExternalFrames04/088_C19C/0054.png).

## 영상별 관찰 및 종료 표본

아래 ‘종료 잔존 안 보임’은 마지막 PNG에서 보이는 효과를 판단한 것이며 런타임 오브젝트/재질 해제나 입자 수 0의 수치 검사와는 별개다.

| 시점·ID | 관찰 | Life / 마지막 표본 | 마지막 원본 |
|---|---|---|---|
| 메인 001 가 | 청록 발동 이후 표적 주위로 타격 선·원형 문양이 전개되고 약해진다. 종료 잔존 안 보임. | 1.500 / 1.583초 | [0038.png](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTPIntegratedFrames04/001_AC00/0038.png) |
| 메인 016 곰 | 사슴형 소환수가 투명하게 나타나고 소환진이 생긴다. 검수 표적과 겹쳐 몸통 가독성은 외부보다 낮다. 종료 잔존 안 보임. | 4.700 / 4.792초 | [0115.png](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTPIntegratedFrames04/016_ACF0/0115.png) |
| 메인 019 구 | 아래쪽이 잘린 큰 투명 문양막. 3.417초에는 옅은 선이 남지만 Life 이전이며 마지막에는 안 보임. | 3.600 / 3.667초 | [0088.png](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTPIntegratedFrames04/019_AD6C/0088.png) |
| 메인 040 놈 | 붉은 발동, 짐승형 몸체, 발 주변 불빛을 구분할 수 있다. 감쇠 중 옅은 몸체가 보인 뒤 종료 잔존 안 보임. | 4.600 / 4.667초 | [0112.png](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTPIntegratedFrames04/040_B188/0112.png) |
| 메인 073 사 | 흰 점광·선화의 밝은 배경 대비가 약하다. 짧게 전개되고 종료 잔존 안 보임. | 1.060 / 1.125초 | [0027.png](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTPIntegratedFrames04/073_C0AC/0027.png) |
| 메인 088 솜 | 호랑이형 몸체와 옅은 바닥 문양이 등장한다. 정적 유지 후 투명해지며 종료 잔존 안 보임. | 4.500 / 4.583초 | [0110.png](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTPIntegratedFrames04/088_C19C/0110.png) |
| 외부 016 곰 | 사슴 전신·청록 소환진을 함께 확인. 꽃잎 선화가 유지되며 감쇠 후 종료 잔존 안 보임. | 4.700 / 4.792초 | [0115.png](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTPExternalFrames04/016_ACF0/0115.png) |
| 외부 019 구 | 둥근 높이 있는 문양막이 보이지만 지형 경계가 아래를 가린다. 종료 잔존 안 보임. | 3.600 / 3.667초 | [0088.png](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTPExternalFrames04/019_AD6C/0088.png) |
| 외부 040 놈 | 옆모습·불빛·소환진이 분명하다. 4.583초의 옅은 윤곽은 Life 4.600초 이전이며 마지막에는 안 보임. | 4.600 / 4.667초 | [0112.png](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTPExternalFrames04/040_B188/0112.png) |
| 외부 088 솜 | 호랑이 전신과 옅은 원형 선화를 확인. 4.292초 감쇠 실루엣 후 종료 잔존 안 보임. | 4.500 / 4.583초 | [0110.png](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTPExternalFrames04/088_C19C/0110.png) |

## 실제 열람한 프레임 인덱스

다음 숫자는 원본 PNG 파일명이다. 예를 들어 `29`는 `0029.png`이며 시각은 메타의 0부터 시작하는 표본 인덱스에 대응한다. 메인/외부가 함께 있는 행은 **양쪽을 각각** 열람했다. 축소 배열로 118개를 확인한 뒤 구의 양쪽 유지 프레임, 사의 밝기 표본, 놈 외부 마지막 프레임은 1280×720 원본도 추가 열람했다.

| ID | 시점 | 열람 인덱스 |
|---|---|---|
| 001 | 메인 | 0, 2, 5, 10, 14, 18, 20, 24, 29, 31, 36, 38 |
| 016 | 메인·외부 각각 | 0, 2, 5, 10, 14, 20, 29, 56, 101, 108, 113, 115 |
| 019 | 메인·외부 각각 | 0, 2, 5, 10, 14, 20, 29, 43, 74, 82, 86, 88 |
| 040 | 메인·외부 각각 | 0, 2, 5, 10, 14, 20, 29, 55, 98, 106, 110, 112 |
| 073 | 메인 | 0, 2, 5, 10, 13, 14, 20, 21, 25, 27 |
| 088 | 메인·외부 각각 | 0, 2, 5, 10, 14, 20, 29, 54, 96, 103, 108, 110 |

최종 미술 승인, 모든 중간 프레임의 무결성, 실제 게임 중 임의 카메라/지형의 가독성, 120술식 전체 품질, 소환수 애니메이션, 성능과 실제 시전 판정은 이 보고서의 통과 범위에 포함하지 않는다.
