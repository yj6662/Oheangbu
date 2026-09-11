# 금 24자 — C2 21번 정적 미술 검수

최우선 수정 대상은 **손(087), 석(080), 섬(082)**이다. 각각 작은 가열 장치가 머리 위 큰 고리로, 편경 조각이 회전 날개로, 가는 궤도 보조선이 큰 교차 칼·리본으로 보인다. KTP 문양을 바꾸는 것만으로 해결되지 않는 본체 크기·배치·운동 문제다.

## 실제 검사 범위

- 현재 Unity Profile의 `Assigned`를 직접 확인: **배정 20개, 미배정 4개(숙·순·숨·숭)**. `build_manifest.json`의 제목·의도·형상·운동 설명과 함께 비교했다. 구 설계의 원본 prefab 재사용 금지 문구는 현재 사용자 지시로 대체된 과거 기록이며, 이번 판정의 금지 기준으로 사용하지 않았다.
- `KTPIntegratedStills21`의 **073~096 전24개 phase 1·2·3 원본 72장**을 각각 `view_image`로 직접 열었다. 추가로 **사·삭·삿·소·속·솟·송의 phase 0·4 원본 14장**을 열었다. 합계 **86장**이며 접촉 시트만 보고 통과시킨 효과는 없다.
- 원본은 모두 1280×720, C2 Main Camera. 24개 메타데이터의 촬영 UTC는 `2026-09-09T10:17:30.8829711Z`~`10:17:40.3986092Z`, 런타임 MVID는 `5710c3e1-f131-449f-af21-e14aaa10eeb9`로 같다.
- 각 phase는 Life의 약 7%·22%·42%·64%·90% 표본이다. phase 4도 Life 이후가 아니므로 완전 소멸·잔존 검사는 아니다. 빠른 침의 phase 1부터는 최초 비행이 끝난 시점이어서, 별도로 phase 0을 읽었더라도 정면 축 단축과 짧은 비행을 완전히 판단하기 어렵다.
- 아래 **통과**는 명시한 정적 형상·단계 구분만을 뜻한다. 미술 최종 승인, 연속 모션, 실제 게임 동작, 성능 통과를 의미하지 않는다. 배정 20개 중 표본 범위 통과 5, 실패 6, 불명 9. 공백 4개는 별도 후보로 남긴다.

## 우선 수정 세 가지

### 1. 손 087 — 가열 표식이 머리 위 큰 고리 묶음

[p1](KTPIntegratedStills21/087_C190_1.png), [p2](KTPIntegratedStills21/087_C190_2.png), [p3](KTPIntegratedStills21/087_C190_3.png)에서 네 개의 완전한 원이 머리 위 넓은 공간을 차지한다. 회청·갈색 고리는 잘 보이지만, ‘발사부의 작은 금속 조각과 열색 틈’보다 큰 고리 장식으로 읽힌다. 시전마다 모인다는 변화도 이 세 표본에서는 식별되지 않는다.

관련 실제 소스는 `Vfx120VariantMotion.WarmMetal()`이다. local Y를 `.8 + u*.3`으로 더하고, 기존 PartScale에 거의 1배만 곱한다. Profile은 Ring 4개, PartScale `.68/.68/.75`다. `Center()`의 Buff 기본 위치에 이 추가 높이가 겹친다.

**최소 방향:** 손 전용 배치를 오른쪽 발사부·전완의 작은 공간으로 옮기고, 완전한 원 4개를 좁은 열린 금속 조각으로 바꾼다. 열색은 조각 사이 짧은 틈에서만 보이게 한다. 현재 소스도 실제 자기 스택 값을 받지 않는다고 명시하므로, 임의 시간 진동을 실제 스택 증가로 보고하거나 새 게임 규칙을 만들지 않는다.

### 2. 석 080 — 작은 편경 대신 큰 회전 날개

[p1](KTPIntegratedStills21/080_C11D_1.png), [p2](KTPIntegratedStills21/080_C11D_2.png), [p3](KTPIntegratedStills21/080_C11D_3.png)의 금속 조각 셋은 상체 폭을 넘어 교차하며, 표본마다 방향이 크게 달라진다. 원래 목표인 ‘손 옆의 작은 조각 셋이 제자리에서 느리게 진자 운동’과 반대 인상이다. 목·얼굴 주변을 가리고, KTP 문양이 빠진 유지 구간에서는 특히 회전 프로펠러처럼 보인다.

Profile은 Shard 3개, PartScale `.65/.45/.75`, Layout Orbit이다. `Vfx120Effect`의 일반 Orbit 배치는 `a += time*.85`를 사용하며, `Vfx120VariantMotion.Apply()`에는 석 전용 배치가 없다.

**최소 방향:** 석만 일반 공전을 벗어나 손 옆의 고정된 서로 다른 부착점 셋에서 작은 각도로 흔들리게 한다. 면 길이 약 15~22cm를 시작점으로 삼고, 서로 겹치지 않는 얇은 각형 조각으로 정리한다. 전체 크기를 줄여도 계속 상체를 공전하면 목표 동작은 해결되지 않는다.

### 3. 섬 082 — 궤도 가독 보조가 전면을 가리는 칼·리본 덩어리

[p1](KTPIntegratedStills21/082_C12C_1.png), [p2](KTPIntegratedStills21/082_C12C_2.png), [p3](KTPIntegratedStills21/082_C12C_3.png)에서 넓고 불투명한 회청색 띠가 가슴 앞을 교차한다. 후반에도 거의 같은 배치가 유지되며, 열린 쌍호·가는 예측선 대신 여러 휘어진 칼 조각으로 읽힌다. 가독성을 돕는 술식이 표적을 가리는 모순이 있다.

Profile은 Ribbon 4개, PartScale `.72/.55/.92`, Layout Fan이며 섬 전용 동작은 현재 Variant switch에 없다. 실제 Ribbon의 넓은 방향은 로컬 Y이므로 X만 줄여서는 띠 폭 문제가 해결되지 않는다.

**최소 방향:** 넓은 불투명 띠를 짧고 가는 열린 쌍호로 정리하고, 예측선은 실제 제공되는 투사체 궤도의 제한된 구간에만 표시한다. 궤도 입력이 없는 검수 상황에서는 작은 열린 표식만 남기며, 임의 궤도나 공격을 생성하지 않는다. 모양을 그대로 두고 투명도만 낮추는 수정은 정보 구조를 고치지 못한다.

## 개별 판정과 실제 열람한 phase

각 행의 링크는 직접 열람한 원본이다. 1·2·3 링크만 있는 행은 다른 phase를 봤다고 간주하지 않는다.

| ID·글자 / 실제 제목 | 배정 | 열람 원본 | 판정 / 보이는 결과 |
|---|---|---|---|
| 073 사 / 은침의 일획 | 배정 | [0](KTPIntegratedStills21/073_C0AC_0.png) [1](KTPIntegratedStills21/073_C0AC_1.png) [2](KTPIntegratedStills21/073_C0AC_2.png) [3](KTPIntegratedStills21/073_C0AC_3.png) [4](KTPIntegratedStills21/073_C0AC_4.png) | **불명.** 초기에는 작은 밝은 점·짧은 흔적, 이후 공통 타격 원문양이 주로 보인다. 직선 은침의 길이는 정면 단축 때문에 판정하기 어렵다. |
| 074 삭 / 뒤까지 꿰는 은선 | 배정 | [0](KTPIntegratedStills21/074_C0AD_0.png) [1](KTPIntegratedStills21/074_C0AD_1.png) [2](KTPIntegratedStills21/074_C0AD_2.png) [3](KTPIntegratedStills21/074_C0AD_3.png) [4](KTPIntegratedStills21/074_C0AD_4.png) | **불명.** 사와 거의 같은 점·문양으로 읽힌다. 첫 표적 뒤까지 관통하는 길이를 이 정면 표본으로 확인하지 못했다. |
| 075 산 / 새겨지는 쇠 낙인 | 배정 | [1](KTPIntegratedStills21/075_C0B0_1.png) [2](KTPIntegratedStills21/075_C0B0_2.png) [3](KTPIntegratedStills21/075_C0B0_3.png) | **통과: 유지 낙인 형상.** 작은 열린 각형 띠가 가슴 표면에 남고 공통 문양이 사라져도 식별된다. 실제 피해 증가 미검증. |
| 076 삼 / 걸린 쇠 봉함 | 배정 | [1](KTPIntegratedStills21/076_C0BC_1.png) [2](KTPIntegratedStills21/076_C0BC_2.png) [3](KTPIntegratedStills21/076_C0BC_3.png) | **통과: 부착→해방 단계 구분.** 교차 못이 가슴에 유지되다 네 방향으로 풀어진 조각이 보인다. 실제 C4 공격 입력이 아닌 예시 cue 범위다. |
| 077 삿 / 끊기지 않는 쇠끝 | 배정 | [0](KTPIntegratedStills21/077_C0BF_0.png) [1](KTPIntegratedStills21/077_C0BF_1.png) [2](KTPIntegratedStills21/077_C0BF_2.png) [3](KTPIntegratedStills21/077_C0BF_3.png) [4](KTPIntegratedStills21/077_C0BF_4.png) | **불명.** 짧은 점과 공통 문양은 보이나 사와 구별되는 끊기지 않는 궤도는 읽기 어렵다. 방어 무시는 정적 검사 대상이 아니다. |
| 078 상 / 허공을 묶는 냉침 | 배정 | [1](KTPIntegratedStills21/078_C0C1_1.png) [2](KTPIntegratedStills21/078_C0C1_2.png) [3](KTPIntegratedStills21/078_C0C1_3.png) | **실패: 서리 형상.** 표적 앞의 두꺼운 톱니형 고리가 먼저 보인다. p2에서 고리가 낮아지고 p3에서 사라지지만 작은 투사체를 얼리는 서리끈으로는 잘 읽히지 않는다. |
| 079 서 / 반듯한 금강판 | 배정 | [1](KTPIntegratedStills21/079_C11C_1.png) [2](KTPIntegratedStills21/079_C11C_2.png) [3](KTPIntegratedStills21/079_C11C_3.png) | **불명.** 방벽 규모와 문양 면은 잘 보인다. 넓은 반복 선화가 전면을 덮고 수와 비슷하며, 짧은 받아침→잔존막 차이는 정적 표본으로 판단하지 못했다. |
| 080 석 / 천천히 돌아가는 편경 | 배정 | [1](KTPIntegratedStills21/080_C11D_1.png) [2](KTPIntegratedStills21/080_C11D_2.png) [3](KTPIntegratedStills21/080_C11D_3.png) | **실패·우선2.** 상체 크기의 판 셋이 크게 교차·공전한다. 작은 진자 편경과 다르다. |
| 081 선 / 앞을 여는 은깃 | 배정 | [1](KTPIntegratedStills21/081_C120_1.png) [2](KTPIntegratedStills21/081_C120_2.png) [3](KTPIntegratedStills21/081_C120_3.png) | **실패.** 허리 아래에 두꺼운 판이 겹친 덩어리가 남는다. 좁고 평행한 금속 깃·출발 압축의 인상이 없다. 실제 속도 증가 미검증. |
| 082 섬 / 읽히는 쇠눈 | 배정 | [1](KTPIntegratedStills21/082_C12C_1.png) [2](KTPIntegratedStills21/082_C12C_2.png) [3](KTPIntegratedStills21/082_C12C_3.png) | **실패·우선3.** 넓은 리본·칼 조각이 상체를 가린다. 열린 쌍호·가늘게 이어지는 궤도 보조와 다르다. |
| 083 섯 / 벼린 은검 | 배정 | [1](KTPIntegratedStills21/083_C12F_1.png) [2](KTPIntegratedStills21/083_C12F_2.png) [3](KTPIntegratedStills21/083_C12F_3.png) | **통과: 검 형상 식별.** 작은 은백 칼날과 자루가 명확하고 과한 광면이 없다. 떠 있는 검수 프롭이므로 실제 손 파지·검 변신 동작은 미검증. |
| 084 성 / 곁침 잔탄 | 배정 | [1](KTPIntegratedStills21/084_C131_1.png) [2](KTPIntegratedStills21/084_C131_2.png) [3](KTPIntegratedStills21/084_C131_3.png) | **불명.** 길쭉한 침 셋은 읽힌다. 세 표본의 거의 같은 배치만으로 주탄 옆 순차 발사를 판정할 수 없다. |
| 085 소 / 은살의 속사 | 배정 | [0](KTPIntegratedStills21/085_C18C_0.png) [1](KTPIntegratedStills21/085_C18C_1.png) [2](KTPIntegratedStills21/085_C18C_2.png) [3](KTPIntegratedStills21/085_C18C_3.png) [4](KTPIntegratedStills21/085_C18C_4.png) | **불명.** 정면에서는 길이가 단축된 여러 넓은 조각과 타격 문양이 보인다. 조각 수·위치 차이는 있으나 즉발 속사 리듬과 선명한 침은 연속·측면 확인이 필요하다. |
| 086 속 / 되튄 은가지 | 배정 | [0](KTPIntegratedStills21/086_C18D_0.png) [1](KTPIntegratedStills21/086_C18D_1.png) [2](KTPIntegratedStills21/086_C18D_2.png) [3](KTPIntegratedStills21/086_C18D_3.png) [4](KTPIntegratedStills21/086_C18D_4.png) | **불명.** p1에서 옆으로 꺾인 긴 선분은 보인다. 실제 다음 표적까지 잇는 도탄보다 가슴 높이의 넓은 가로 배열로 읽히며, 표적 연결 여부는 표본만으로 미확정이다. |
| 087 손 / 달아오른 침틀 | 배정 | [1](KTPIntegratedStills21/087_C190_1.png) [2](KTPIntegratedStills21/087_C190_2.png) [3](KTPIntegratedStills21/087_C190_3.png) | **실패·우선1.** 머리 위 대형 고리 네 개. 작은 발사부 가열 틀과 위치·크기 모두 다르다. |
| 088 솜 / 강철 짐승의 갈비 | 배정 | [1](KTPIntegratedStills21/088_C19C_1.png) [2](KTPIntegratedStills21/088_C19C_2.png) [3](KTPIntegratedStills21/088_C19C_3.png) | **통과: 정적 강철 짐승 식별.** 네발 몸체·넓은 발·층진 금속 갑판이 보이고 소환진이 본체를 덮지 않는다. 고정 포즈이므로 리깅·걷기·판 조립 모션 통과가 아니다. |
| 089 솟 / 갈수록 얇아지는 살 | 배정 | [0](KTPIntegratedStills21/089_C19F_0.png) [1](KTPIntegratedStills21/089_C19F_1.png) [2](KTPIntegratedStills21/089_C19F_2.png) [3](KTPIntegratedStills21/089_C19F_3.png) [4](KTPIntegratedStills21/089_C19F_4.png) | **불명.** p1의 가로 작은 조각들은 보인다. 뒤 탄이 더 가늘고 날카롭다는 순서는 정면 희소 표본에서 읽지 못했다. |
| 090 송 / 별처럼 꺾인 요격막 | 배정 | [0](KTPIntegratedStills21/090_C1A1_0.png) [1](KTPIntegratedStills21/090_C1A1_1.png) [2](KTPIntegratedStills21/090_C1A1_2.png) [3](KTPIntegratedStills21/090_C1A1_3.png) [4](KTPIntegratedStills21/090_C1A1_4.png) | **실패: 요격 형상.** p1은 표적 위를 두른 넓은 금속 조각 반원으로 보인다. 개별 적 투사체로 뻗는 은선·서리 매듭이 식별되지 않는다. 실제 광역 격추는 미검증. |
| 091 수 / 둘러선 금강살 | 배정 | [1](KTPIntegratedStills21/091_C218_1.png) [2](KTPIntegratedStills21/091_C218_2.png) [3](KTPIntegratedStills21/091_C218_3.png) | **불명.** 높이가 있는 문양 장벽은 유지된다. 정면에 같은 패턴이 크게 겹치고 서와 구별되는 광역 외곽·금속 살 구조는 이 시점에서 불명확하다. |
| 092 숙 / 은결의 매듭 후보 | 미배정 | [1](KTPIntegratedStills21/092_C219_1.png) [2](KTPIntegratedStills21/092_C219_2.png) [3](KTPIntegratedStills21/092_C219_3.png) | **공백·불명.** 낮은 위치의 구부러진 띠 후보. 배정 효과나 완성 연출로 세지 않는다. |
| 093 순 / 회청 깃살 후보 | 미배정 | [1](KTPIntegratedStills21/093_C21C_1.png) [2](KTPIntegratedStills21/093_C21C_2.png) [3](KTPIntegratedStills21/093_C21C_3.png) | **공백·불명.** 큰 깃 조각의 한쪽 펼침은 보인다. 배정·실제 기능 없음. |
| 094 숨 / 속이 빈 금함 후보 | 미배정 | [1](KTPIntegratedStills21/094_C228_1.png) [2](KTPIntegratedStills21/094_C228_2.png) [3](KTPIntegratedStills21/094_C228_3.png) | **공백·불명.** 속이 빈 함보다 채워진 직사각 면 두 장으로 보인다. 후보 형태의 한계이며 이번 배정 효과 우선 수정 목록에서는 제외한다. |
| 095 숫 / 바위를 가르는 은획 | 배정·비전투 | [1](KTPIntegratedStills21/095_C22B_1.png) [2](KTPIntegratedStills21/095_C22B_2.png) [3](KTPIntegratedStills21/095_C22B_3.png) | **통과: 검수 프록시 절단 단계.** 중앙 은선 뒤 검은 프록시 두 면 사이에 통로가 열린다. 바위 실물 재질·지형 변화가 아닌 명시적 예시 프록시다. |
| 096 숭 / 뒤집힌 은방울 후보 | 미배정 | [1](KTPIntegratedStills21/096_C22D_1.png) [2](KTPIntegratedStills21/096_C22D_2.png) [3](KTPIntegratedStills21/096_C22D_3.png) | **공백·불명.** 원형 고리 두 개의 변화·감쇠가 보인다. 껍질 있는 방울로는 읽히지 않으며 기능 배정 없음. |

## 추가 관찰과 한계

상의 톱니 고리, 송의 반원 금속 조각도 개선 후보지만, 먼저 손·석·섬의 지속 구간을 수정하는 편이 표적 가독성과 술식 의미에 더 큰 영향을 준다. 사·삭·삿의 공통 타격 문양은 주 형상보다 크게 보이고 서·수의 반복 문양 면도 비슷하다. 다만 빠른 침 3개를 정면 정적 표본만으로 ‘본체가 전혀 없다’고 단정하지 않는다.

이번 검사에서 소스 읽기는 눈으로 확인한 결과의 수정 위치를 찾는 보조 근거다. 현재 코드·변수 이름·검수 JSON 상태를 미술 통과의 대신으로 사용하지 않았다. 실제 입력·인식·피해·패링, 적 투사체와의 상호작용, 모든 프레임의 동적 궤도, 카메라 전환, Life 이후 잔존, FPS는 **미검증**이다. Unity 명령, 코드, Profile, 원본 이미지 수정은 하지 않았다.
