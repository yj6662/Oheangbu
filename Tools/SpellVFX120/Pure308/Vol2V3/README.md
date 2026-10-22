# Vol2 V3 — E3 묶음 1·2 스테이징

2026-10-22. E3 이식 계약 v3의 입력·문법·예산을 구현했다. 현재 수치는 TEST이며 후보 수치는 자동 적용하지 않는다. Unity 편집기와 Assets에 접근하지 않았고 git은 사용하지 않았다. 모든 작성 파일과 빌드 산출물은 이 디렉터리 아래에 있다.

## 실행과 결과

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
