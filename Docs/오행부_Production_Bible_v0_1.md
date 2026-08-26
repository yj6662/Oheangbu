# 오행부 — Production Bible v0.1

| 항목 | 값 |
|---|---|
| 지위 | 헌법 하위 바이블(Production). 소유: 개발 파이프라인 프로토콜 · 리깅·구현 규격 · 도구 스택 · AI 에셋 파이프라인 · 스코프 운용 |
| 태그 | 상태어 5종 표준(CONST-STATUS). 본문 기본 LOCKED/TBD, 제안은 PROPOSED 절 한정 |
| 제정 | 2026-08-17 · 창설 — 원천: 참고_codex_리깅애니메이션대화.md · 참고_오행부_맵표현생성가이드.md §26~30 · Combat/Spellcraft에서 이관된 구현 판단 |

---

## 1장 — 헌장 [PROD-CHARTER] [LOCKED]

- 비대칭 투자 원칙(헌법): 주변부는 최소화·자동화, 정체성(작도 손맛·수묵 룩)은 확실하게.
- 역할 분리: AI(Claude/Codex)=배관(로직·반복·자동화), 사람=아트 디렉션·손맛·랜드마크·보스 아레나(맵 가이드 §27과 동형).
- 소유의 삼분법(2026-08-19 개정): **도메인 바이블 = 게임의 의도와 규칙**(무엇이어야 하는가) / **본 문서 = 프로젝트 공통 구현 규격과 제작 파이프라인**(프로젝트 전체가 어떻게 만드는가) / **Implementation Spec = 개별 기능의 구현 방법**(이 특정 기능을 어떻게 구현하는가). 기획 문서에 구현 기법이 침투하면 본 문서로 이관한다.

## 2장 — 개발 파이프라인 프로토콜 [PROD-PIPELINE] [LOCKED — 2026-08-17 채택]

TASK → BIBLE_INDEX(라우팅) → 관련 Bible·의존성 확인 → Implementation Spec → 기존 코드 확인 → Claude/Codex 실행 → Unity/Blender 반영 → Validation → PASS: Commit / FAIL: Bible·Spec·Code 수정 후 재순환.

- Spec 계층: 향후 /Specs 디렉터리로 분리(예고 — BIBLE_INDEX 규약). Spec은 Bible의 Section ID를 인용하고, 수치는 데이터 층(SO/CSV)을 가리킨다.
- Validation의 정의(자동 테스트·수동 체크리스트 구분)는 [TBD].

## 3장 — 리깅·캐릭터 규격 [PROD-RIG] [구조 LOCKED · 세부 원천 참조]

- 인간형: 표준 Humanoid Skeleton 통일 + Retargeting — 애니메이션 재사용의 전제. (현운·무학·병졸·승병 등)
- 용·뱀 계열: Spline Bone Chain — 머리만 키 애니메이션, 몸통은 절차 추종. 청룡·몰락한 황룡·인공 황룡 3체가 이 리그를 공유.
- 짐승형: Generic Rig + 발 IK(Raycast). 범 가족(백호·산군·필드 범)·물든 영물.
- 기계형: 강체 부품 분리 리깅(무스키닝) — 텍스처 늘어짐 원천 차단. 장영실 탑승 기체·메카천수관음·기관술 적.
- 다중 팔(천수관음): 팔 그룹화(Main/Strike/Visual) + 착지=IK Target·Raycast, 타격 위치 0.3~0.5초 전 확정(읽기 윤리의 구현 번역). 지역 불상은 이 골격의 파생.
- 옷: 주요 캐릭터만 Cloth 시뮬(Magica Cloth 2), 나머지는 Cloth Bones+Secondary Motion. 기운(오라): 리깅하지 않음 — Shader+VFX 계층(발광 상한 규칙은 Art & Audio 소관).
- 소환수(SPELL-COL-M): 고정형(토템·포탑형) 우선 구현, 여력 시 이동형 승격 — 3/5 획득 개정으로 사용 구간 2/5 확보, 승격 우선순위 재평가 여지.
- 세부 수치·본 카운트·구현 절차: 참고_codex_리깅애니메이션대화.md 원천 — 착수 시 본 장으로 승격 등재.

## 4장 — 도구 스택 [PROD-TOOLS] [LOCKED]

- Unity 6 / URP · C#. DI=VContainer · 비동기=UniTask · asmdef 모듈 · SO 이벤트 채널 · 데이터 주도(SO/DTO). 전역 싱글턴(The Last One) 폐기 — LEGACY.
- Odin Inspector & Serializer: 에디터/인스펙터 계층 한정, 런타임 코어는 Odin-독립. 퍼블릭 리포 금지.
- 헝겊: Magica Cloth 2. 스트로크 인식: $1 Unistroke Recognizer C# 포트 — 최고 위험 가정, 스파이크 검증 [TBD].
- 붓 획 렌더링: Ribbon Mesh + Noise Cutout(갈필) — 인식 로직 무접촉(좌표 기록 유지, 렌더러만 교체).
- MIT 의존성(VContainer·UniTask) 크레딧 고지 의무.

## 5장 — AI 에셋 파이프라인 [PROD-AIASSET] [LOCKED]

- Meshy(3D 기본 메시) · Recraft(UI/2D/SVG) · Suno(음악 — 저작권 보증 불확실, 릴리스 음원은 human-in-the-loop 필수). 상용이므로 유료 플랜 상업 이용권 확보.
- 맵: Terrain 중심 전환 + Cliff Mesh 하이브리드, 반복 자동화(Codex+MCP)는 §26~30 원천 — 착수 시 승격.
- 폰트: 눈누 사전 검증. 민속 1차 자료: 국립민속박물관 folkency·실록 DB·구비문학대계.

## 6장 — 스코프 운용 [PROD-SCOPE] [LOCKED — 2026-08-17 수정 채택, CONST-SCOPE 개정 완료]

- **베이스 분량 = 25~30시간**(예준 수정 — 상한 완화안 기각). 트라이·탐험·3결말 회차는 별도 체감 가산. 실현 가능성은 EA 실측 게이트가 판정한다.
- 콘텐츠 예산표(1호 안건): 대보스 12+비밀 1 · 신규 골격 ~7 가족(COMBAT-BOSS) · **강토당 공간 예산 확정(LDB 2장 — 개활 1~2·소던전 1~2·주막 2~3·성황당 4~6·필드 보스 1~2, 수치 TEST)** · 작도 글자 100 · 게이트 5종. 잔여 칸: 강토당 잡몹·정예 수.
- EA 실측 게이트: EA(황경+청림) 완성 시 강토 단가 실측 → 정식판 재산정. 조정 대상은 강토 구조가 아니라 강토 두께.

## 7장 — TBD 총람 [PROD-TBD]

- Validation 정의 / Spec 템플릿 / 예산표 잔여 칸 / 스파이크 검증(인식) / 모듈러 건물 키트(2.4~3m 그리드) 착수 / 붓 획 파이프라인 단계 구현 / 소환수 이동형 승격 판단.
