# AI-Native 게임 개발 문서 운영 체계
## 오행부 프로젝트용 — 설계 철학 + 실전 운영 규약 통합본

> 목적: 개발자의 머릿속에 있는 게임의 의도와 규칙을 **체계화·위계화·시스템화하여 문서화**하고,
> Claude Code·Codex·Unity MCP·Blender MCP 등의 AI 에이전트가 필요한 정본만 탐색하여 구현하도록 만드는 개발 운영 체계를 정의한다.
>
> 이 문서는 개별 게임 시스템의 기획서가 아니다.
> **프로젝트 전체의 문서 구조, 정본 관리, Authority, 라우팅, 상태 전이, AI 작업 규약, 검수 루프를 정의하는 상위 운영 문서**다.

---

# 1. 이 체계가 필요한 이유

생성형 AI를 게임 개발에 투입하면 가장 먼저 드러나는 문제는 코딩 능력이 아니다.

진짜 문제는 다음이다.

> **AI가 프로젝트 전체의 의도를 지속적으로 정확하게 이해할 수 있는가?**

기존의 거대한 GDD에는 흔히 다음 정보가 섞인다.

```text
게임 철학
세계관
게임 규칙
미술 방향
기술 선택
현재 구현
임시 구현
과거 결정
```

사람은 문맥으로 구분할 수 있지만 AI에게는 다음이 모두 같은 “문서 속 문장”으로 보일 수 있다.

```text
먹은 사용할수록 감소한다.
먹이 부족하면 갈필이 발생한다.
획은 검은 Sprite를 Stretch해서 표현한다.
```

앞의 두 문장은 **게임 규칙**이고, 마지막 문장은 **현재 구현 방식**이다.

구현 방식은 바뀔 수 있다.

```text
Sprite Stretch
↓
Runtime Ribbon Mesh
↓
Noise Cutout Shader
```

하지만 게임 규칙까지 같이 바뀌어서는 안 된다.

따라서 핵심 원칙은 다음과 같다.

> **게임의 진실과 구현 방법을 분리하고, 무엇이 더 높은 Authority를 가지는지 문서 구조 자체가 말하게 한다.**

---

# 2. 인간과 AI의 역할 재정의

이 구조에서 개발자는 단순히 “기획자”가 아니고, AI는 단순히 “코더”가 아니다.

## 개발자

```text
Vision
Creative Direction
System Design
Priority
Bible
Approval
Review
Taste
Final Decision
```

개발자의 핵심 역할:

> **아이디어를 체계화하고, 위계화하고, 시스템화한 뒤, AI가 실행할 수 있는 명시적인 게임 지식으로 만드는 것**

## AI Agent

```text
Implementation
Editor Operation
Automation
Refactoring
Testing
Technical Validation
Documentation Assistance
Asset Pipeline Operation
```

AI의 핵심 역할:

> **명시적인 프로젝트 지식을 코드·씬·Shader·Terrain·Rig·Animation·데이터로 변환하는 것**

최종 구조:

```text
Developer
= Game Director / Lead Designer / Reviewer

AI Agents
= Programmers / Technical Artists / Production Operators
```

---

# 3. 전체 문서 계층

오행부 프로젝트의 상위 문서 구조는 다음과 같다.

```text
                    CONSTITUTION
                         │
                    BIBLE_INDEX
                         │
        ┌────────────────┼────────────────┐
        │                │                │
    Spellcraft        Combat          Narrative
        │                │                │
        ├────────────── LDB ──────────────┤
        │                │                │
     Art & Audio      Production        Spatial
                         │
                         ▼
                Implementation Spec
                         │
                         ▼
                       Task
                         │
                         ▼
                     AI Agent
                         │
                         ▼
                 Unity / Blender
                         │
                         ▼
                    Validation
```

보조 정본:

```text
DECISIONS
= 상태 전이와 결정 사유

CSV / SO
= 구조화된 데이터 정본

CLAUDE.md / AGENTS.md
= AI 작업 규약

Reference Docs
= 확정 이전의 원천 자료
```

---

# 4. Constitution의 역할

Constitution은 프로젝트의 최상위 헌법이다.

소유하는 것:

```text
Game Definition
Design Pillars
Absolute Rules
Anti-Vision
Target Experience
State Standard
Bible Authority
Glossary Charter
Scope
```

Constitution은 세부 구현 방법을 소유하지 않는다.

예:

```text
Constitution
"작도는 직접 획을 긋는 입력으로만 한다."

O

Constitution
"Ribbon Mesh를 사용한다."

X
```

두 번째는 Production 또는 Implementation Spec의 영역이다.

---

# 5. Domain Bible의 역할

Domain Bible은 **게임이 무엇이어야 하는가**를 소유한다.

오행부의 기본 구조:

```text
Spellcraft
= 작도 문법 · 필세 · 인식 철학 · 글자 설계 사유

Combat
= 전투 문법 · 적 · 보스 · 성장 · 경제

Narrative
= 세계 진실 · 연표 · 세력 · 인물 · 결말 · 시선

Level Design
= 공간 문법 · 게이팅 · 인력 · 체크포인트 · 아레나

Art & Audio
= 수묵 룩 · 색 어휘 · VFX · UI · 국악 · 소리

Spatial
= 실제 거리 · 스케일 · 배치 수치

Production
= 프로젝트 공통 구현 규격 · 제작 파이프라인
```

---

# 6. 소유권 분리

같은 게임 개념이 여러 문서와 관계될 수 있다.

하지만 **정의의 소유자는 하나여야 한다.**

예:

```text
보스

Combat
= 전투 기믹

Narrative
= 정체와 서사

LDB
= 아레나와 위치

Production
= 공통 제작 규격
```

또 다른 예:

```text
색

Constitution
= "채색은 의미를 가진다"

Art & Audio
= 어떤 색이 어떤 의미인가

LDB
= 그 색을 공간에서 어디에 사용하는가
```

원칙:

> **정의는 소유 문서에 한 번만 존재하고, 다른 문서는 참조만 한다.**

---

# 7. Authority

문서 충돌 시 우선순위:

```text
CONSTITUTION
      >
Domain Bible
      >
Production Bible
      >
Structured Data / Implementation Spec
      >
Existing Code
```

단, 같은 층의 Bible끼리 충돌할 경우 Constitution의 소유권 표에서 지정된 문서가 우선한다.

```text
Bible != Existing Code

→ Existing Code가 수정 대상
```

기존 코드가 존재한다는 이유로 정답으로 간주하지 않는다.

---

# 8. BIBLE_INDEX — 모든 작업의 진입점

AI는 모든 Bible을 처음부터 읽지 않는다.

항상 다음으로 시작한다.

```text
Task
↓
BIBLE_INDEX
↓
Relevant Bible
↓
Relevant Section ID
↓
필요한 Cross Reference
↓
Spec
↓
Code
```

BIBLE_INDEX가 소유하는 것:

```text
현재 정본 버전
파일 생명주기
문서 소유 영역
핵심 Section ID
데이터 정본 규칙
라우팅 정보
```

Constitution에는 역할과 권한만 두고, 현재 버전·생성 여부 등 동적 상태는 Index가 소유한다.

---

# 9. 버전 원칙

버전은 다음처럼 분리한다.

```text
BIBLE_INDEX
= 현재 프로젝트가 사용하는 정본 버전

각 파일의 Version
= 해당 파일 자신의 식별자
```

예:

```text
Combat v0.1
Combat v0.2
```

두 파일이 동시에 존재하더라도:

```text
BIBLE_INDEX
→ Combat v0.2
```

라면 v0.2만 현재 정본이다.

---

# 10. Section ID

장 번호는 개정 과정에서 바뀔 수 있다.

따라서 참조의 정본은 **Section ID**다.

예:

```text
COMBAT-PARRY
COMBAT-GROGGY
SPELL-BUFF
SPELL-BRUSH
LDB-GATING
NARR-LENSES
ART-INK
PROD-RIG
```

초기 가독성을 위해:

```text
COMBAT-PARRY (Combat 3장)
```

처럼 병기할 수 있다.

하지만 Authority는 ID에 있다.

원칙:

> **장 번호는 사람용 보조 정보, Section ID는 기계 친화적 안정 참조다.**

---

# 11. 상태어와 Lifecycle

내용의 상태와 파일의 존재 상태를 섞지 않는다.

## State — 내용 상태

```text
LOCKED
TEST
PROPOSED
TBD
LEGACY
```

### LOCKED
확정. 변경 시 DECISIONS 기록과 정본 개정이 필요하다.

### TEST
규칙의 방향은 정했으나 실제 구현·실측 검증이 필요한 상태.

### PROPOSED
후보·파킹·검수 중.

### TBD
결정 자체가 아직 없음.

### LEGACY
폐기된 설계. 다시 제안되는 낭비를 막기 위해 보존.

## Lifecycle — 파일 상태

```text
ACTIVE
PLANNED
ARCHIVED
```

State와 Lifecycle을 혼용하지 않는다.

---

# 12. DECISIONS — 왜 이렇게 되었는가

Bible은 **현재의 진실**을 말한다.

DECISIONS는:

> **현재의 진실이 어떻게 만들어졌는가**

를 기록한다.

```text
Bible
= What is true now

DECISIONS
= Why it became true
```

기록 대상:

```text
TBD → LOCKED
PROPOSED → LOCKED
LOCKED → 개정 LOCKED
기능 삭제 → LEGACY
정본 변경
문서 구조 변경
```

AI가 다음 질문을 할 때 사용한다.

> “왜 이 시스템이 이렇게 되어 있지?”

DECISIONS는 과거 폐기안을 다시 제안하는 것을 막는 역할도 한다.

---

# 13. 데이터 정본

구조화 데이터는 사람이 읽는 Bible과 별도 정본을 가질 수 있다.

예:

```text
Spellcraft Bible
= 작도 문법 · 설계 사유

작도어휘 CSV
= 글자별 효과의 유일 정본
```

동일한 값을 두 개의 편집 가능한 정본으로 두지 않는다.

Bible 안에 데이터 표를 넣는다면:

```text
READ-ONLY MIRROR
```

로 취급한다.

충돌 시 데이터 정본을 따른다.

---

# 14. Production Bible과 Spec의 차이

이 구분은 반드시 유지한다.

```text
Domain Bible
= 게임의 의도와 규칙
= 무엇이어야 하는가

Production Bible
= 프로젝트 공통 구현 규격과 제작 파이프라인
= 프로젝트 전체에서 어떻게 만드는가

Implementation Spec
= 특정 기능의 구현 방법
= 이 기능을 어떻게 구현하는가
```

예:

```text
Art & Audio
"갈필은 획 내부가 마른 붓처럼 끊겨 보여야 한다."

↓

Production
"붓 렌더링 공통 규격은 Ribbon Mesh 계열을 사용한다."

↓

SPEC-BRUSH-DRY
"Vertex Ink + World-space Noise Cutout으로 구현한다."
```

---

# 15. Implementation Spec

Bible이 충분히 안정된 뒤 작성한다.

Template 예시:

```md
# SPEC-BRUSH-DRY

## Authority
- CONST-RULES
- SPELL-BRUSH
- ART-INK
- PROD-TOOLS

## Goal
갈필을 인식 로직과 분리된 순수 비주얼로 구현한다.

## Requirements
- Runtime Ribbon Mesh
- World-space Noise Cutout
- 카메라 이동에 Noise 고정 금지
- 입력 좌표 기록 로직 변경 금지

## Affected Code
- BrushStrokeRenderer.cs
- BrushStrokeMesh.cs
- InkStroke.shader

## Acceptance Criteria
- 인식 결과 불변
- 갈필 시각 표현 정상
- 화면 공간 Noise swimming 없음
```

---

# 16. Task

Spec을 작은 실행 단위로 나눈다.

예:

```text
BRUSH-001
Ribbon Mesh Prototype

BRUSH-002
Vertex Ink Data

BRUSH-003
Noise Cutout

BRUSH-004
Stroke Taper

BRUSH-005
Validation
```

권장:

```text
One Task
→ One Logical Change
→ One Validation
→ One Commit
```

---

# 17. AI 작업 프로토콜

AI가 구현 요청을 받으면:

```text
1. BIBLE_INDEX를 읽는다.
2. 관련 Section ID를 식별한다.
3. 해당 Section을 먼저 읽는다.
4. 필요한 Cross Reference만 확장한다.
5. LOCKED와 충돌 여부를 확인한다.
6. Spec이 없으면 구현 전에 Spec을 작성한다.
7. 기존 코드를 확인한다.
8. 구현한다.
9. Validation한다.
10. PASS면 Commit, FAIL이면 원인 계층을 판정한다.
```

금지:

```text
전체 GDD를 매번 읽기
기존 코드를 정본으로 가정하기
LOCKED를 구현 편의를 위해 우회하기
설계 변경을 코드에서 몰래 수행하기
```

---

# 18. Local Task와 Global Task

모든 작업에서 같은 양의 문서를 읽지 않는다.

## Local Task

예:

```text
"갈필 Noise가 카메라에 붙어 움직인다."
```

라우팅:

```text
BIBLE_INDEX
↓
ART-INK
↓
SPELL-BRUSH
↓
PROD-TOOLS
↓
SPEC-BRUSH-DRY
↓
InkStroke.shader
```

## Global Task

예:

```text
"전투 시스템을 전면 개편한다."
```

필요 범위:

```text
CONSTITUTION
COMBAT
SPELLCRAFT
LDB
ART
PRODUCTION
관련 데이터
관련 Spec
```

원칙:

> **최소 Context에서 시작하고 의존성을 따라 필요한 만큼만 확장한다.**

---

# 19. 기존 코드 Migration

새 Bible이 생겼다고 기존 코드를 모두 버리지 않는다.

기존 구현은 세 가지로 분류한다.

```text
KEEP
= Bible과 일치

REFACTOR
= 개념은 맞지만 구현이 현재 규격과 다름

REMOVE
= Bible과 충돌
```

예:

```text
붓으로 직접 작도
→ KEEP

Sprite Stretch Stroke
→ REFACTOR

자동 즉발 술식
→ REMOVE
```

---

# 20. 결과가 잘못됐을 때 원인 분류

AI 결과가 마음에 들지 않는다고 무조건 다시 생성하지 않는다.

```text
Result Failure
      │
      ├─ Vision 문제
      │    → Constitution / Domain Bible
      │
      ├─ Rule 문제
      │    → Domain Bible
      │
      ├─ Production 규격 문제
      │    → Production Bible
      │
      ├─ 기술 설계 문제
      │    → Implementation Spec
      │
      └─ 구현 오류
           → Code
```

이렇게 해야 실패가 프로젝트 지식으로 축적된다.

---

# 21. Validation

최종 구현 검수는 단순히 “작동하는가?”로 끝나지 않는다.

## Functional
- 기능이 동작하는가
- Error가 없는가
- 기존 시스템과 충돌하지 않는가

## Bible
- 관련 LOCKED를 지켰는가
- 소유권을 침범하지 않았는가

## Gameplay
- 목표 플레이 리듬이 나오는가
- 의도하지 않은 전략이 생기지 않았는가

## Visual / Audio
- Art & Audio Bible과 일치하는가
- 가독성을 해치지 않는가

## Performance
- CPU
- GPU
- Memory
- GC
- Draw Call
- LOD / Culling

## Experience
Constitution의 Target Experience 질문을 통과하는가.

---

# 22. 문서 Drift Audit

이 체계에서 가장 중요한 운영 규칙 중 하나다.

문서 구조가 좋아도 **부분 롤백·미전파·옛 스냅샷 혼입**은 발생할 수 있다.

따라서 정기적으로 다음을 검사한다.

```text
1. Constitution ↔ 모든 Bible 충돌
2. Bible ↔ Bible 소유권 충돌
3. 같은 Bible 내부 LOCKED ↔ TBD 충돌
4. BIBLE_INDEX Owns ↔ 실제 헤더 충돌
5. DECISIONS 최신 LOCKED ↔ 현재 정본 반영 여부
6. 데이터 정본 ↔ Bible Mirror 충돌
7. LEGACY 상태어·과거 용어 잔존
8. Cross Reference가 장 번호만 사용하고 있지 않은지
9. Appendix 이관 완료분이 Authority를 다시 갖고 있지 않은지
```

특히:

```text
DECISIONS
"변경 완료"

↓

현재 Bible
"옛 문구 유지"
```

가 발견되면 **현재 정본의 미전파 문제**로 판정한다.

---

# 23. Appendix / Migration Buffer

상위 문서의 임시 이관 대기 영역은 **버퍼**일 뿐 정본이 아니다.

원칙:

```text
이관 전
Appendix = 임시 소유

이관 완료
Appendix Authority = 0
Target Bible = 정본
```

이관 완료 후에는 원문을 복제해 두지 않는다.

```text
이관 완료 → NARR-TRUTH
```

처럼 참조만 남긴다.

---

# 24. Reference Docs

Reference 문서는 정본이 아니다.

예:

```text
맵 표현 생성 가이드
리깅·애니메이션 대화 기록
과거 GDD
아이디어 메모
```

사용 방식:

```text
Reference
↓
검토
↓
채택 결정
↓
DECISIONS
↓
Bible / Production 승격
```

확정되지 않은 Reference의 내용이 정본 Bible을 덮을 수 없다.

---

# 25. 문서 과잉 설계 방지

문서 시스템은 게임을 만들기 위한 수단이다.

다음은 하지 않는다.

```text
모든 문장에 ID
모든 Section에 Requires/Affects
매 작업마다 모든 Bible 읽기
Spec 작성 전에 Bible이 완벽해질 때까지 기다리기
문서 구조를 계속 재설계하기
```

대신:

```text
의미 있는 결정에만 ID
고의존 Section에만 Dependency
필요한 Context만 읽기
Vertical Slice 기준으로 필요한 TBD부터 해소
```

---

# 26. Bible 작성과 구현의 전환점

모든 TBD가 사라질 필요는 없다.

첫 구현 대상에 필요한 정본 경로만 끊기지 않으면 된다.

예:

```text
CONSTITUTION
↓
SPELL-BRUSH
↓
ART-INK
↓
PROD-TOOLS
↓
모두 구현에 필요한 규칙 결정됨
────────────────
→ Spec 작성 가능
```

Narrative 엔딩 TBD가 남아 있어도 붓 렌더링 구현에는 문제가 없다.

---

# 27. Spatial Bible의 특수성

Spatial은 다른 Bible과 달리 실제 플레이 측정값에 크게 의존한다.

따라서:

```text
LDB
= 배치 문법

EA Prototype
= 실제 거리감

Spatial
= m 단위 실배치 정본
```

순서가 가능하다.

예:

```text
FOV
Movement Speed
Terrain Scale
Landmark Visibility
Checkpoint Distance
Road Width
Arena Size
```

이런 수치는 책상에서 추측해 LOCKED하기보다 EA 실측을 거쳐 승격하는 것이 낫다.

---

# 28. 실제 개발 루프

최종 운영 루프:

```text
IDEA
 ↓
SYSTEMIZE
 ↓
BIBLE
 ↓
SPEC
 ↓
TASK
 ↓
AGENT
 ↓
IMPLEMENT
 ↓
PLAY / TEST
 ↓
VALIDATE
 ↓
ROOT CAUSE
 ↓
UPDATE
 ↓
COMMIT
 ↓
NEXT
```

조금 더 실제적으로:

```text
Developer Intent
      ↓
Bible Knowledge
      ↓
BIBLE_INDEX Routing
      ↓
Implementation Spec
      ↓
AI Execution
      ↓
Playable Result
      ↓
Human Review
      ↓
Knowledge Improvement
```

---

# 29. 이 체계의 궁극적인 목표

목표는 “AI에게 더 많은 문서를 읽히는 것”이 아니다.

목표는:

> **AI가 무엇이 중요한 정보인지 스스로 판단할 수 있도록 정보의 위계와 소유권을 명시하는 것**

이다.

잘못된 구조:

```text
200-page GDD
↓
매번 전체 입력
↓
Context Noise
```

목표 구조:

```text
Task
↓
Index
↓
Section ID
↓
Dependencies
↓
Spec
↓
Code
```

---

# 30. 한 문장 정의

> **아이디어는 사람이 결정하고, Constitution과 Bible이 의도를 보존하며, Index가 정본을 라우팅하고, Production과 Spec이 구현 방법을 정의하고, AI가 실행하며, 사람은 결과를 검수해 다시 프로젝트 지식으로 축적한다.**

---

# 31. 최종 역할 구조

```text
Developer
= Game Director
= System Designer
= Documentation Architect
= Final Reviewer

AI Agents
= Implementation Engineers
= Technical Artists
= Production Operators

Constitution
= Project Constitution

Bibles
= Shared Studio Knowledge

BIBLE_INDEX
= Knowledge Router

DECISIONS
= Decision History

Production
= Studio Technical Standard

Specs
= Feature Technical Design

Tasks
= Production Orders

Validation
= Direction Review
```

이 구조가 충분히 성숙하면, 1인 개발이라도 실제 작업 방식은 **한 명의 디렉터가 작은 개발 조직을 운영하는 형태**에 가까워진다.

차이는 실행 조직의 상당 부분이 AI 에이전트라는 점이다.
