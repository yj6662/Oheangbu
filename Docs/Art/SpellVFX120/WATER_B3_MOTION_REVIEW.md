# 수 계열 B3 동작 표본 검수

상태: **ART_REVIEW_WITH_FINDINGS — 최종 미술 PASS 아님.**

2026-09-09, C2 후방 카메라 배경의 온·옷·옹·엄 원본 PNG를 각 12시점, 합계 48장 실제 열람했다. 온 0035/0043, 옹 0044, 엄 0045는 원본 1280×720 크기로 추가 확인했다. 물결·먹실·그릇은 화면에 나타나고 마지막 표본에서는 사라지지만, 아래 세 가지 가독성 문제가 남는다.

## 큰 잔여 문제

1. **온·옷·옹의 물결 바탕이 직사각형 판처럼 보인다.** 0.71~1.83초에서 긴 직선 하단과 양쪽 경계가 유지된 채 표적 쪽으로 작아지며 전진한다. 특히 옷은 중앙의 틈 때문에 물의 흐름보다 나란한 두 타일로 읽힌다. 위쪽 흰 물결 무늬는 보이지만, 자연스러운 물의 부피·흩어지는 경계는 약하다. 확대나 색 변경만으로 해결될 문제로 보이지 않는다.
2. **온·옷·옹의 실 길이는 구별되지만 방향의 전달력은 차이가 난다.** 온의 검은 먹실은 1.46초의 낮은 왼쪽에서 1.79초의 표적 양옆으로 나타나 상승의 단서가 있다. 다만 여러 짧은 굽은 조각이 따로 놓인 인상도 강해서 이 표본만으로 연속적인 상향 흐름의 명확성을 통과시킬 수 없다. 옷은 짧은 청회색 물결선이 표적 둘레에 균일하게 반복되어 짧은 흐름의 길이는 읽히지만 이동 방향은 약하다. 옹은 어깨 부근부터 바닥까지 긴 실이 이어지고 2.21~2.96초에 하단이 지면 쪽에 남아, 세 종류 중 긴 하향 흐름의 인상이 가장 분명하다. 세 효과의 공통 평판 물결이 서로 다른 동사보다 먼저 눈에 들어온다.
3. **엄은 그릇의 테두리는 생겼지만 내부 부피가 모호하다.** 1.50~3.04초에서 윗부분의 타원형 테두리와 아래로 모이는 형태는 보인다. 그러나 안쪽이 짙은 리본들의 교차와 구멍으로 보여, 물을 담은 그릇보다는 작은 소용돌이 묶음에 가깝다. 이 후방 거리에서는 액면·그릇 안쪽 공간을 명확히 분리해서 읽기 어렵다.

## 술식별 실제 관찰

| 술식 | 관찰한 주요 변화 | 종료 표본 | 판단 |
|---|---|---|---|
| 온 `111_C628` | 0.71초 물결 등장 → 1.46~2.50초 검은 먹실 → 2.88초 먹실 감소 → 3.58초 물결 소멸 직전 | 0095, 3.958초: 잔존 시각 요소 없음 | 상향의 단서는 있으나 방향 가독성 보완 필요. 평판 인상 잔존 |
| 옷 `113_C637` | 두 갈래 물결판 → 1.38~2.42초 짧은 물결선 → 2.75초 아래쪽 짧은 선만 남음 → 3.46초 흐려짐 | 0091, 3.792초: 잔존 시각 요소 없음 | 짧은 실의 길이는 구별됨. 반복 문양처럼 보여 흐름 방향은 약함 |
| 옹 `114_C639` | 폭넓은 물결판 → 1.46~2.96초 긴 세로 실과 바닥 쪽 꺾임 → 3.29초 실 소멸, 물결만 남음 | 0097, 4.042초: 잔존 시각 요소 없음 | 긴 하향 인상은 상대적으로 명확. 공통 물결판은 보완 필요 |
| 엄 `106_C5C4` | 0.38~1.13초 작은 윤곽 → 1.50~3.04초 그릇형 묶음 확대·유지 → 3.42초 축소 → 3.79초 거의 소멸 | 0100, 4.167초: 잔존 시각 요소 없음 | 그릇 테두리 관찰됨. 내부 액면·부피는 불명확 |

효과는 중앙 개방부 안에서 관찰된다. 검사한 표본에서는 효과 본체의 명백한 프레임 절단을 발견하지 않았다. 초반의 옅은 바닥 문양은 프레임 하단까지 닿지만 주효과 판독을 막는 정도는 아니다. 두 캡슐은 검수용 표적이며, 배경의 바위·건물은 실제 C2 환경이다.

네 효과 모두 마지막 PNG의 SHA-256이 각 첫 빈 장면 PNG와 동일하다. 이는 **해당 종료 표본에 화면 잔존이 없음**을 뒷받침한다. 오브젝트 파괴, 실제 플레이의 입자 누수 또는 원래 적에게 발생한 상태 종료를 증명하지는 않는다.

## 표본과 자료

| 술식 | 확인한 프레임 번호, 각 24fps | 타임라인 시트 | 영상 |
|---|---|---|---|
| 온 | 0000, 0009, 0017, 0026, 0035, 0043, 0052, 0060, 0069, 0078, 0086, 0095 | [앞](C:/Users/yj666/Oheangbu/Art/SpellVFX120/VideoReview/Water_B3/111_C628_timeline_1.jpg), [뒤](C:/Users/yj666/Oheangbu/Art/SpellVFX120/VideoReview/Water_B3/111_C628_timeline_2.jpg) | [온 MP4](C:/Users/yj666/Oheangbu/Art/SpellVFX120/CueMotionClipsB3/111_C628.mp4) |
| 옷 | 0000, 0008, 0017, 0025, 0033, 0041, 0050, 0058, 0066, 0074, 0083, 0091 | [앞](C:/Users/yj666/Oheangbu/Art/SpellVFX120/VideoReview/Water_B3/113_C637_timeline_1.jpg), [뒤](C:/Users/yj666/Oheangbu/Art/SpellVFX120/VideoReview/Water_B3/113_C637_timeline_2.jpg) | [옷 MP4](C:/Users/yj666/Oheangbu/Art/SpellVFX120/CueMotionClipsB3/113_C637.mp4) |
| 옹 | 0000, 0009, 0018, 0026, 0035, 0044, 0053, 0062, 0071, 0079, 0088, 0097 | [앞](C:/Users/yj666/Oheangbu/Art/SpellVFX120/VideoReview/Water_B3/114_C639_timeline_1.jpg), [뒤](C:/Users/yj666/Oheangbu/Art/SpellVFX120/VideoReview/Water_B3/114_C639_timeline_2.jpg) | [옹 MP4](C:/Users/yj666/Oheangbu/Art/SpellVFX120/CueMotionClipsB3/114_C639.mp4) |
| 엄 | 0000, 0009, 0018, 0027, 0036, 0045, 0055, 0064, 0073, 0082, 0091, 0100 | [앞](C:/Users/yj666/Oheangbu/Art/SpellVFX120/VideoReview/Water_B3/106_C5C4_timeline_1.jpg), [뒤](C:/Users/yj666/Oheangbu/Art/SpellVFX120/VideoReview/Water_B3/106_C5C4_timeline_2.jpg) | [엄 MP4](C:/Users/yj666/Oheangbu/Art/SpellVFX120/CueMotionClipsB3/106_C5C4.mp4) |

대표 원본: [온 0035](C:/Users/yj666/Oheangbu/Art/SpellVFX120/CueMotionFramesB3/111_C628/0035.png), [온 0043](C:/Users/yj666/Oheangbu/Art/SpellVFX120/CueMotionFramesB3/111_C628/0043.png), [옷 0041](C:/Users/yj666/Oheangbu/Art/SpellVFX120/CueMotionFramesB3/113_C637/0041.png), [옹 0044](C:/Users/yj666/Oheangbu/Art/SpellVFX120/CueMotionFramesB3/114_C639/0044.png), [엄 0045](C:/Users/yj666/Oheangbu/Art/SpellVFX120/CueMotionFramesB3/106_C5C4/0045.png).

## 검증 범위

- 원본 1280×720 PNG의 12시점 표본 시각 검수다. 연속 영상 전 프레임의 미술 판정을 수행했다고 주장하지 않는다.
- 각 MP4와 `_capture.json`의 SHA가 대응 `_encoding.json`에 기록된 SHA와 일치한다. 기존 인코딩 판정은 `VERIFIED_ENCODING_AND_TIMING_ONLY`이며 미술 판정을 대신하지 않는다.
- 공통 로드 App MVID: `a05edc37-50ee-4210-bb0e-2d0e7fd6ff48`.
- 모든 캡처는 `demonstrationCues=true`, `gameplayConnectionVerified=false`, `PRESENTATION_REVIEW_ONLY`다. `Review-only Primary/Secondary` 표적을 사용하며, 시연용 hit/release 시계는 실제 피격·해제 이벤트의 증거가 아니다.
- 이번 검수에서 Unity·소스·원본 프레임·기존 영상을 변경하지 않았다. 시트의 시간·술식 라벨은 원본 밖에만 추가했다.
- 원본 경로·시각·해시 목록: [review_sources.json](C:/Users/yj666/Oheangbu/Art/SpellVFX120/VideoReview/Water_B3/review_sources.json).
