# Vol2 V3 — E3 묶음 1–4 스테이징

묶음 3·4의 최신 실행 결과와 결정 우선 변경은 **문서 맨 아래 「묶음 3 · 4」**를 따른다. 아래 묶음 1·2 설명·수치는 2026-10-22 원본 대조 이력이며, 걱·급소창의 현재 실행 규칙은 아래 변경표가 우선한다.

2026-10-22. E3 이식 계약 v3의 입력·문법·예산을 구현했다. 현재 수치는 TEST이며 후보 수치는 자동 적용하지 않는다. Unity 편집기와 Assets에 접근하지 않았고 git은 사용하지 않았다. 모든 작성 파일과 빌드 산출물은 이 디렉터리 아래에 있다.

## 묶음 1·2 최초 실행과 결과 (2026-10-22)

저장소 루트에서 실행한다. 설치된 SDK 9.0.300에 맞춰 콘솔은 net9.0이다. 외부 NuGet 패키지는 없고 NuGet.Config는 패키지 소스를 비운다.

~~~powershell
dotnet run --project Tools/SpellVFX120/Pure308/Vol2V3/Vol2V3.csproj -- --suite import
dotnet run --project Tools/SpellVFX120/Pure308/Vol2V3/Vol2V3.csproj -- --suite grammar-budget
dotnet run --project Tools/SpellVFX120/Pure308/Vol2V3/Vol2V3.csproj -- --suite all
~~~

| suite | PASS | FAIL |
|---|---:|---:|
| import | 80 | 0 |
| grammar-budget | 3654 | 0 |
| all | 3734 | 0 |

all 종료 코드 0. 실패 및 잘못된 suite는 종료 코드 1이다. 음성 입력의 기대 거절은 테스트 PASS에 포함되며, 예외 발생을 성공으로 세지 않는다. 실제 실행 기록은 [Validation/results.json](Validation/results.json)에 있다.

이식할 src만 .NET Standard 2.1 / C# 9로 별도 컴파일했다. 경고 0, 오류 0이다. 이 검사는 Unity 타입·프로젝트 참조 없이 수행한다.

~~~powershell
dotnet build Tools/SpellVFX120/Pure308/Vol2V3/tests/portability/Rules.Portability.csproj
~~~

## 파일 목록과 옮길 위치

메인이 오프라인 검토 후 **src의 아래 12개 파일만** 저장소 기준 **Oheangbu/Assets/_Project/Scripts/Vol2Rules/**로 옮긴다. 이 작업에서는 실제 이동하지 않았다.

| 파일 | 역할 |
|---|---|
| src/Oheangbu.Vol2.Rules.asmdef | references=[], noEngineReferences=true, autoReferenced=false |
| src/NumbersV3RowDto.cs | 8필드 수치 행·원본 행번호, nullable decimal |
| src/NumbersV3Dto.cs | 불변 수치 묶음, 검증 상태, 별도 ⑨ 후보 프로필 |
| src/Vol2SpellRowDto.cs | 10열 원문·행번호·자모 해석·비용·EffectTerms |
| src/Vol2ImportManifestDto.cs | 원본 바이트 SHA-256 및 정렬된 DTO 정규화·해시 |
| src/Vol2ContractValidator.cs | CSV 임포트, 계약 오류, 해시 검증 DTO 로드 |
| src/ContractSchemaV3.cs | 필수 Key·단위·후보 조건, 인식 가능한 v10 서술 지문 |
| src/CsvV3.cs | UTF-8 BOM, 인용·쉼표·인용 내 개행 처리 |
| src/RuleTypesV3.cs | 자모·접촉·효과 항·접촉 조건 DTO와 공통 enum |
| src/GlyphRulesV3.cs | 120자 Unicode 왕복, 수치로 구성한 불변 Plan |
| src/EffectBudgetV3.cs | 대상별·공유 장부, 접촉·씨앗·이전·반사 상태 전이 |
| src/PeakRulesV3.cs | 같은 접촉 조건의 일반 대비 상한, 세·상극, 급소창 연속 제한 |

다음 파일은 스테이징에 남긴다.

| 파일 | 역할 |
|---|---|
| Vol2V3.csproj, Program.cs, NuGet.Config | 콘솔 빌드·suite 선택·종료 코드 |
| tests/TestSupport.cs | 입력 위치 탐색·집계·오류 출력 |
| tests/Cases_Import.cs | 양성 임포트·13변형·스키마·CSV·해시·SO 재구성 검사 |
| tests/Cases_GrammarBudget.cs | 120자·1–4인·C5·극치20·후보·이벤트 재생 |
| tests/portability/Rules.Portability.csproj | src만 netstandard2.1로 컴파일 |
| golden_v3.py | 원본 검사기의 행·극치항·순위·13변형을 최초 추출 |
| golden_balance_v3.py | 원본 계산의 효율비·⑨ 후보 순위·극치항을 최초 추출 |
| golden_manifest_v3.py | Python으로 독립 계산한 DTO 정규화 해시 |
| Golden/source.json | 원본 6종 해시·120행·극치20·순위·13변형·방향표25 |
| Golden/balance.json | 효율비 및 후보 프로필 계산 골든 |
| Golden/import_manifest.json | schema/importer 버전·원본 해시·독립 DTO 해시 |
| Golden/events.json | E3 §3에서 직접 계산·검토한 12개 이벤트 시나리오 |
| Validation/results.json | 최종 PASS/FAIL 및 이식 컴파일 기록 |
| README.md | 사용·이식·검증 설명 |

bin, obj, tests/portability/bin·obj와 숨김 SDK 캐시·tmp는 실행 산출물이며 이식하지 않는다.

## 입력과 로드 계약

원본은 다음 여섯 파일을 읽기만 한다.

- Docs/Research/e0/numbers_v3.csv
- Docs/Research/debate25/vol2_table120_v10.csv
- Docs/Research/debate25/vol2_v10.md
- Docs/Research/debate25/check_table120_v10.py
- Docs/Research/debate25/e2_sheet_v10.md
- Docs/Research/e3/port_contract_v3.md

수치 414행·Value 409/빈값 5·후보 57·전 행 TEST·Key 유일을 검사한다. 단위·범위·필수 Key·후보 조건·파생식 및 현행 계약을 대조한다. Candidate는 문자열로 보존한다. CreateEoCandidateProfile()만 둘째 0.25/ㄱ 공유 0.85/섯 뒤쪽 0.45/작도 0.6과 그 파생 후보를 새 DTO에 담는다. 원본 DTO는 변하지 않는다.

표는 무받침 20/일반 80/극치 20, 자모와 이름·Unicode의 일치, 비용·대상 상한·둘째 접촉·씨앗·극치 선언과 서술 계산의 일치를 검사한다. 정의·재료·비고를 포함한 v10 서술은 임포트 시에만 해석한다. 알려진 서술 지문 밖의 문구는 UnparsedNarrative로 거절하므로 새 서술을 조용히 무시하지 않는다. 새 표를 채택할 때는 서술 해석과 스키마를 명시적으로 검토해야 한다.

Import 실패 시 CanCast=false, Numbers=null이며 행/Key 오류를 반환한다. GlyphRulesV3.Build는 검증되지 않은 NumbersV3Dto를 거절한다. SO 연결 단계에서는 값을 불변 DTO로 재구성한 뒤 LoadDto(numbers, spells, storedManifest, trustedManifest)를 호출한다. 이 경로는 서술을 다시 파싱하지 않고 **재구성 DTO의 실제 해시**를 계산한다. 원본·버전·DTO 해시 불일치 시 로드를 거절한다.

정규화는 Key/글자 정렬, 고정 필드, invariant decimal, UTF-8 무BOM·LF다. 필드 내 역슬래시·탭·개행은 이스케이프하고 null은 명시한다. CSV 행 순서·행번호·인용 방식은 의미 해시에서 제외하고, CSV 원본 바이트 해시는 별도로 보존한다. Python/C# 독립 DTO 해시:

~~~text
a8c190859d0e18ee563761743e8be16da7e5b43b672f73d6505f272199ec66d3
~~~

## 예산과 이벤트 사용

시간은 외부 long 마이크로초, ID는 long, 수량은 decimal이다. 상태는 시전별 인스턴스이며 전역 정적 가변 상태가 없다. Apply는 입력 상태를 바꾸지 않고 새 State와 Commands를 반환한다. 호출자는 Step.State를 다음 입력에 전달하고 명령 ID를 적용 장부에서 한 번만 소비해야 한다. 같은 EventId를 다시 전달하면 명령을 재발급하지 않는다.

- 효과 상한은 패링 제외 ㅓ 전부 및 ㅏ+ㅁ에만 적용한다. 직접·번짐·씨앗이 먼저 닿거나 심긴 셋을 공유한다. 소진·생명 revision 변경으로 슬롯을 양도하지 않는다. 넷째 글자 효과는 0이며 붓 기본 타격은 별도 장부/후속 어댑터의 몫이다.
- 둘째 접촉은 대상별 첫 접촉 이후 300000µs 이상, 다른 SwingId, 1회다. 299999/300000µs·같은 SwingId·셋째 접촉·다른 시전/대상을 시험한다.
- ㄱ 잔존은 재접촉·틱·이전이 같은 수명 잔액을 소비한다. 걱의 몸 재료 0.9와 뿌리 0.25×3도 MaterialId별 슬롯으로 나눈다. Transfer는 어미 잔액을 차감하고 ChildBudget을 반환한다. 반응 관계·깊이·소유 판정은 후속 묶음 3이다.
- ㄴ 번짐과 ㅅ 벗김은 공유 장부다. 난·넌은 대상당 0.5, 셋까지다. 벗김 환산 0.4는 HpDamage에 더하지 않는다.
- 씨앗은 즉시·둘째 몫 0, 작동 1회다. ㅏ는 다음 내 공격, ㅓ는 실제 피해 수신, ㅗ는 면 진입, ㅜ는 고리 횡단을 받는다. 광역 씨앗의 한 작동에 속하는 대상들은 같은 SwingId로 전달한다. 피해 0·패링·회피는 ㅓ 씨앗을 작동시키지 않는다.
- 수명 경계는 [StartedUs, StartedUs+LifeUs)로 고정했다. ㅏ 씨앗은 심기 이후 별도 공격이어야 한다. 초기 접촉 없이 귀환 구간에서 처음 만난 대상도 귀환 몫만 한 번 받을 수 있다. ㅓ+ㅇ의 귀환은 0이다.
- 엉만 빌린 탄 장부를 쓴다. 원 위력×0.5, 서로 다른 탄 3회, 합≤1.0이며 내 총작용량과 분리한다.

Plan.TotalFor는 검사기의 C5 가격 지표다. 실제 접촉 조건은 ContactProfile로 PeakRulesV3.Evaluate에 전달한다. 구조×세를 같은 조건 일반의 1.5배로 자른 뒤 상극×1.25를 한 번 곱한다. 최대 1.875, 섯 기준 2.1/2.1이다. Commands.HpDamage는 세·상극 적용 전 몸 즉시 피해이며 최종 배율은 이 평가 결과로 연결한다. 수명·면적·벗김 시간은 별도 축이고, 414행 DTO의 대응 수치로 보존한다.

## 골든 근거와 범위

원본 검사기 C1–C5: PASS 93 / FAIL 0 / WARN 1, 음성 13/13. 원본 함수를 메모리에서 실행했으며 원본 검사기·보고서를 수정하지 않았다. Python/C# 산식 오차는 1e-9 이하, 이벤트 상태·경계·decimal은 정확 일치다. C5의 2인 넘·넛 0.2400 동점은 그대로이며 10·11위 교체 시에도 편중 게이트가 유지된다. 3·4인 순위는 참고이고 Play 통과로 해석하지 않는다.

Golden/events.json은 정적 검사기의 출력이 아니라 E3 계약에서 직접 계산한 기대값이다. 순서·수락/거절·대상/공유 잔액·이전·빌린 위력·명령을 재생하고 첫 차이에 caseId/필드/기대/실제를 출력한다. 원본 골든 추출 스크립트 셋은 출력 파일이 이미 있으면 종료하므로 테스트나 빌드가 골든을 자동 갱신하지 않는다.

반응·방어 런타임과 Unity 연결/Play 검증은 이번 묶음 1·2의 범위 밖이다. 방어 관련 수치는 입력 계약과 13개 음성 변형으로만 대조했다.


## 묶음 3 · 4

2026-10-24. workorder_v5 C1(v4 S34). 이 디렉터리 안에서만 구현·실행했다. Assets·Unity·git·원본 CSV·원본 숫자 시트는 수정하지 않았다. 외부 패키지를 설치하지 않았으며 NuGet.Config의 빈 소스도 유지했다. SDK 사용자 폴더·캐시·임시 파일과 빌드 출력은 모두 이 디렉터리 아래로 지정했다.

### 설계와 구현

- 묶음 3: ReactionRulesV3의 불변 Material·State·ContactPair·Step. 실제 접촉에서 작용 방향 선택 → 자격 검사 → 두 재료 갱신과 명령을 한 Step으로 반환한다. 예보는 원본 상태 그대로다. EventId·CastId·MaterialId와 자식의 어미 시전 계보를 보존한다. 같은 시전·중복 접촉·소비된 재료의 재소비를 거절한다.
- 25칸은 Golden/source.json의 원문 방향표와 대조한다. 비화 한 번, 순·역 상생 자식의 물성·접촉 꼴·잔액 절반·10초, 깊이 1 제한, 자식 재상생의 제자리 0.3초 건조, 꺾음 피해/감속, 막힘 절반, 점화자 소유와 진영 면제를 구현했다. 걱은 재료 자격이 없다.
- 기존 EffectBudgetV3.Transfer로 이전한 몫만 Material에 등록한다. 등록 후 접촉·틱은 ReactionRulesV3.Spend, 반응은 Resolve가 **같은 Remaining**을 소비한다. 원래 장부에서는 이미 이전한 몫을 다시 지급하지 않는다. Add는 처리 이력·시간을 보존하면서 새 재료만 등록한다.
- 묶음 4: ParryRulesV3는 먹 0 시작, 성공 0/실패 min(현재 먹,14)의 후불 정산, 탄 제외, 모든 몸의 성공 방어 피해 0, 정답 몸의 자세 40을 반환한다. 0.8초 미만 전조는 그로기 자격을 제거한다. 유효 시간 안 탄 피격은 다음 근접 패링 기회를 소비하지 않으며, 실패 비용은 만료 때 한 번만 정산한다.
- GroggyRulesV3는 TargetId+LifeRevision별 자세·급소창 순번·묶음 합을 관리한다. 자세는 시간으로 감소하지 않는다. 만개 시 자세와 초과분을 소비하고 창 안 축적을 막는다. 삼원 사건은 원천을 바꿔도 같은 창 순번을 쓴다. 새 생명 revision은 별도 자세 장부다.
- NoInkSwingV3는 첫 생존 적 한 명, 휘두르기 시작 기준 1.1초, 비치명 HP 바닥과 대상별 먹 예산을 처리한다. 수급 장부 키는 안정된 조우 ID+대상 ID다. 생명 revision·보스 페이즈·재락온·같은 세션 스트리밍으로 충전하지 않는다. 실제 재생성은 새 조우 ID를 쓴다. RestoreLoaded는 명시적 불러오기에서 복원된 생존 적만 잔량 0으로 등록하며, 처음 만나는 새 조우는 정상 지급한다.
- 갈무리의 씨앗·부착 운반은 원래 CastId/MaterialId와 OwnerAttack·SwingId를 가진 Contact만 발급한다. 자체 피해/먹과 글자 예산을 분리한다. EffectBudgetV3에서 실제로 승인한 씨앗 격발만 능동 상합 사건으로 전달한다. 자동 씨앗·응보·반응·갈무리 자체는 자세 0이다.
- 계약 목록에 없던 GeokRecoveryV3를 D308-145 걱 회복을 실행하기 위해 추가했다. HP 30/3초·먹 26·재사용 20초, 교전 창/조우·페이즈 제한, 비교전 예약 예산, 실제 HP 피해 중단, 휴식 초기화, Save/Restore를 구현했다. JSON 왕복 후에도 지급 진행도·예약 예산·재처리 방지 이력이 보존된다. 회복은 공격 배율과 분리되고 먹 자연 회복을 정지시키지 않는다.
- 공개 GlyphRulesV3.Build의 걱은 공격 항 0·회복 전용 Plan이다. EffectBudgetV3는 걱 공격/공유/이전/귀환 접촉을 거절한다. PeakRulesV3는 회복의 피해 환산을 0으로 돌려주고, TryCritical은 창당 한 번과 창 사이 같은 극치 연속 금지를 검사한다. 일반 글자 사용으로 직전 극치 기록을 지우지 않는다.

### 계약 대비 바뀐 점

수치는 모두 **TEST**다. 규칙 본문에는 밸런스 수치를 직접 박지 않고 NumbersV3Dto.cs의 ReactionNumbersV3Dto·DefenceNumbersV3Dto에서 전달한다. 원본 행을 덮어쓰지 않는다.

| 항목 | 계약/옛 CSV | 이번 실행 규칙 | 출처 |
|---|---|---|---|
| 그로기 삼원 | 원천 사건마다 1·0.6·0.36… | 정답 패링 40·능동 ㅏ+ㅁ 60·ㅓ+ㅅ 근접 상극 10, 자세 100에서 개방 | D308-145, debate30/v04_final.md |
| 반복 감쇠 | 그로기 단위 합 ≤2.5 | 급소창 순번 길이 2초×0.6^(n−1), 초기화 사이 합 ≤5초 | D308-145 |
| 초기화 | 해제 뒤 6초, 원천 사건 n | 직전 창 종료 뒤 6초 새 개방 없음에서 창 순번/길이 합 초기화, 남은 자세 유지 | debate30/v04_final.md v0.4 |
| 성공 방어 | 성립·그로기만 명시, 피해 처리 미정 | 정답·틀린 몸·무속성 모두 피해 전부 무효, 정답만 자세 | D308-145 |
| 무먹/갈무리 | 0.3, 비치명·수급 미정 | HP 1 바닥, 명중당 먹 4, 일반 12/보스 24, 실제 수령 먹만 차감 | D308-145, debate30/v04_final.md |
| 갈무리 기억 | 미정 | 같은 세션 조우·개체 원장, 명시적 로드로 복원된 적만 0 | D308-145 |
| 걱 | 공격 2.65·부착 재료·6초 | 공격 0·HP 30/3초·먹 26·20초 재사용·반응재료 없음 | D308-145 |
| 걱 사용·예약 | 없음 | 교전 급소창·조우당 1회/보스 페이즈당 1회, 비교전 휴식 사이 HP 30을 확정 시 예약, 중단 환급 없음 | debate30/v04_final.md v0.4 |
| 피해 배율 | 구조×세 상한 뒤 상극×1.25, 창 자격·B 결합 불명확 | C=min(R×S,1.5)×K, K는 창 안 술식에만, B=1.5는 모든 피해 | D308-147 |
| 갈무리 창 피해 | 옛 CSV는 급소창 배율 없음 | 창 밖 0.3·창 안 0.45, 최종 HP 1 바닥; 세·상극은 미적용 | D308-147의 모든 피해가 우선 |
| 극치 연속 | 기존 구현은 같은 창 같은 글자만 차단 | 창당 한 번, 창이 바뀌어도 같은 극치 연속 금지·일반 글자로 기록 초기화 금지 | D308-145·147 |

0.5초 패링·0.8초 패링 재사용·1.1초 갈무리·0.3 기본 피해·반응 배율/수명은 numbers_v3.csv에서 읽는다. 새 자세/회복/수급/급소창/B 값은 결정 DTO의 읽기 전용 속성이다. 전조 최소 0.8초는 vol2_v10 B-4의 수치이며 DTO로 명시했다. 그 외 비화·자식·막힘·건조·반응 피해 상한은 CSV reaction.* 및 timing.child.life를 따른다. 후보 수치는 자동 적용하지 않는다.

D308-147의 1.875·1.4375는 B 이전 값이다. DefenceNumbersV3Dto.Damage의 전체 산식은 H×C×B×(1+M)×(1+min(A,10)/100)이고, 상극 극치의 B 포함 최대는 2.8125다. PeakRulesV3.Evaluate에 넘기는 counter는 이미 창 안 술식 자격을 판정한 값이어야 한다. 최종 피해를 계산할 때 Evaluate의 K 적용 결과에 Damage를 또 적용하면 안 된다. Damage에는 배율 전 H/R/S를 넘긴다. 앞잡 입력·황경 콘텐츠 변경은 이번 범위에 포함하지 않는다.

### 원본 검증과 현재 실행의 구분

E9 전이 전의 numbers_v3.csv·vol2_table120_v10.csv·Golden/*는 수정하지 않았다. Import/13음성/C5/후보 프로필은 **원본 스냅샷의 내부 일치**를 검증한다. tests/TestSupport.SourcePlan은 이 대조만을 위해 internal BuildCore를 호출한다. 실행에는 공개 Build를 사용한다. grammar-budget는 별도로 현재 120자 Plan을 대조하여 걱만 회복으로 바뀌고 나머지 119자 구조가 보존됐음을 확인한다. 옛 걱 재료 지급 시험은 삭제하고 회복 전용 거절 시험으로 교체했다. 원본 C5 PASS를 걱 교체 후의 게임 밸런스 승인으로 해석하지 않는다.

C6 13변형은 defence에서도 실제 입력을 각기 변형해 지정 오류를 확인한다. 예외를 성공으로 세지 않는다. ⑦의 옛 2.5 단위 급수 검사는 원본 계약 대조로 남기고, 현재 창 급수는 별도로 40회 반복·합 ≤5초·원천 혼합·6초 경계를 검사한다.

### 명령·시간·연결 계약

호출자는 반환 State를 다음 입력에 넘긴다. 한 상태 스트림을 단일 순서로 처리하거나 외부 CAS/잠금으로 커밋해야 한다. 같은 이전 State로 만든 두 Step을 둘 다 커밋하는 것은 허용하지 않는다. 이 순수 API에는 전역 가변 상태·Unity 타입·스레드 잠금이 없다. 같은 시각의 경쟁은 호출 순번이 먼저인 접촉이 소비한다. 예보는 그 순서를 예약하지 않는다.

CommandKey(반응은 Command.Key)는 적용 장부에서 한 번만 소비한다. 이벤트 ID는 각 규칙 상태 스트림 안에서 유일하게 전달한다. 반응 재료 ID는 소비 후 재사용하지 않는다. 그로기 명령 키에는 대상·생명 revision도 포함한다. 부착/씨앗 명령은 기존 EffectBudgetV3의 CastId 포함 키를 사용한다. 장기 세션의 소비 이력 제거 시점은 연결 계층이 정해야 한다.

반응 DamageBudget은 **산물 전체가 공유하는 HP 상한**이며 대상마다 같은 값을 복제해서 지급하는 값이 아니다. DirectAreaBudget은 동일 먹 직접 광역의 HP 기준을 외부에서 전달한다. 재료 Remaining의 작용량 단위와 HP를 섞지 않는다. 산물 자체는 재료·그로기·기폭 자격이 없고, Child만 새 재료다. 형상·실제 접촉·대상 선정·HP 분배는 후속 Combat 어댑터가 맡는다.

걱 Begin 전에 현재 조우의 급소창·TryCritical 공통 소비를 원자적으로 확인한다. 걱은 별도 HP 상태를 반환하며 피격 씨앗·仁을 호출하지 않는다. 회복 Tick은 실제 피해 이벤트 전까지 누적 지급하고, 같은 시각에는 외부 이벤트 순서를 유지한다. Save/Restore는 논리 마이크로초 시계도 이어지는 것을 전제로 한다. 스트리밍은 현재 State를 유지한다.

### suite 결과와 이식 빌드

~~~powershell
dotnet run --project Tools/SpellVFX120/Pure308/Vol2V3/Vol2V3.csproj -- --suite reaction
dotnet run --project Tools/SpellVFX120/Pure308/Vol2V3/Vol2V3.csproj -- --suite defence
dotnet run --project Tools/SpellVFX120/Pure308/Vol2V3/Vol2V3.csproj -- --suite all
dotnet build Tools/SpellVFX120/Pure308/Vol2V3/tests/portability/Rules.Portability.csproj
~~~

| suite / 검사 | PASS | FAIL | 경고 | 오류 |
|---|---:|---:|---:|---:|
| import (all 내부) | 80 | 0 | — | — |
| grammar-budget (all 내부) | 3768 | 0 | — | — |
| reaction | 248 | 0 | — | — |
| defence (C6 13/13 포함) | 348 | 0 | — | — |
| all | 4444 | 0 | — | — |
| 콘솔 빌드 net9.0 | PASS | 0 | 0 | 0 |
| tests/portability netstandard2.1 / C# 9 | PASS | 0 | 0 | 0 |

개별 reaction·defence와 all, 두 빌드의 종료 코드는 모두 0이다. 실제 실행 기록은 [Validation/results_reaction_defence.json](Validation/results_reaction_defence.json)에 있다. 기존 Validation/results.json은 묶음 1·2의 이력으로 보존한다. Unity EditMode/Play 및 새 결정 이후 E7 밸런스 관문을 통과했다는 뜻은 아니다.

### 바뀐 파일

| 파일 | 변경 |
|---|---|
| src/ReactionRulesV3.cs | 신규, 반응·재료 잔액·소비·자식·소유 |
| src/ParryRulesV3.cs | 신규, 후불 패링 |
| src/GroggyRulesV3.cs | 신규, 삼원 자세·창 감쇠 |
| src/NoInkSwingV3.cs | 신규, 갈무리·세션 원장·진 운반 |
| src/GeokRecoveryV3.cs | 신규, 걱 자기 회복·저장 예산 |
| src/NumbersV3Dto.cs | 반응·결정 우선 방어 숫자 DTO 추가 |
| src/GlyphRulesV3.cs | 현재 걱 회복 Plan |
| src/EffectBudgetV3.cs | 걱의 옛 공격 경로 차단 |
| src/PeakRulesV3.cs | 회복 피해 0·창당 한 번·극치 연속 기록 |
| tests/Cases_Reaction.cs | 신규, 25칸·경쟁·자식·소유·수명·공유 잔액 |
| tests/Cases_Defence.cs | 신규, 후불·삼원·갈무리·회복·13음성 |
| tests/Cases_GrammarBudget.cs | 원본 골든과 실행 Plan 분리·걱/극치 회귀 시험 |
| tests/TestSupport.cs | 원본 대조 전용 SourcePlan |
| Program.cs | reaction·defence·all 연결 |
| README.md | 결정 차이·경계·결과 |
| Validation/results_reaction_defence.json | 신규, 실제 실행 기록 |

### 남은 질문 — 경계 · 미정 규칙

1. 패링의 [시작, 시작+0.5초)와 시작 기준 0.8초는 계약의 제안을 스테이징 경계로 채택했다. E4½/Play에서 확정해야 한다. 유효 시간 중 적 탄이 닿으면 피해만 통과하고 패링은 유지하는 선택도 함께 확정 대상이다. 타임아웃 후 같은 시각의 실제 임팩트는 피해를 통과시키되 비용을 다시 차감하지 않는다.
2. 두 재료가 모두 움직일 때는 생성 시각→큰 CastId로 결정했다. 정지 둘의 규칙을 확장한 선택이며 실제 접촉 어댑터에서 검토가 필요하다. 동일 시각의 서로 다른 접촉은 외부 순번을 따른다.
3. 비화의 어미 기준은 두 입력의 **남은 작용량 합**, 상생은 양쪽 잔액 전부 이전·소비로 해석했다. 꺾음의 작용 입력도 한 접촉에 배정한 몫으로 소비한다. 자식의 상생 재접촉은 두 입력을 소비하되 자식 ID만 건조 신호를 낸다. 계약이 작용 형태의 접촉별 몫·상생 재접촉에서 상대 어미의 잔류를 명시하지 않으므로 이 선택을 연결 Spec에서 확정해야 한다. 되돌아오는 형상은 새 접촉 몫/MaterialId를 쓰되 CastId를 보존하며, 이미 반응한 재료 ID로 돌아오면 거절된다.
4. 급소창 꼬리는 decimal로 합산하되 외부 long 마이크로초로 내림한다. 1µs 미만 창은 길이 0이 되고 자세를 소비한다. 이 정밀도 끝의 최소 창/개방 표시는 미정이다. 임의 최소 길이를 더해 5초 상한을 깨지는 않았다.
5. 걱의 실제 피해와 회복 Tick이 같은 시각이면 외부 순번에 따른다. HP가 이미 최대여도 예약 예산은 소모한다. 저장 복원 시 논리 시계 재기준화, 다른 시전·앞잡과 급소창 한 번을 공유하는 원자적 커밋은 묶음 5 연결 항목이다. 일반 조우는 BossPhaseId=0, 보스만 안정된 페이즈 ID를 넘겨야 한다.
6. 갈무리의 비치명 바닥은 자체 피해에 적용한다. 운반한 기존 씨앗·부착은 원래 글자 장부로 처리한다. 진 피해까지 비치명으로 만드는 규칙은 추가하지 않았다. 휴식 후 실제 재생성과 구역 재로드를 구별할 안정된 EncounterId 제공은 연결 계층의 책임이다.
7. 반응 HP 산물의 개별 대상 배분·화 지속 틱 주기, 적 소유 반응의 0.5초 예고·동시 수 제한, 세계 재생 45초·둘레 1·지속 묶음 3·교체 예고는 실제 세계/시간 연결에서 확인해야 한다. 이번 순수 반응은 총 HP 상한·진영 면제를 명령으로 제공한다.
8. 禮 수치·오상 세부 입력·앞잡 Q 해제·황경 콘텐츠는 이번 범위 밖이다. ㅁ/ㅅ의 늦은 그로기 자격은 순수 defence 시험에서만 검증했으며 첫 구현의 해금 범위를 넓히지 않았다. E9 CSV/SO 전이 및 걱 교체 후 전체 C5/E7 재평가는 별도다.
