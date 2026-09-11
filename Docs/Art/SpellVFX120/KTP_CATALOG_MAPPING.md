# KTP 120개 프로필 발동·명중·필드 연결

2026-09-09 갱신. `Vfx120TraditionalCatalogBuilder.Build()`의 **제작 매핑·정적 검증과 후속 실행 기록**이다. `ktp_catalog_mapping.json`의 06:16:44 UTC Build 결과는 120개 Native 연결을 기록한다. 이것을 120개 주형상 재제작이나 전수 미술 검수 완료로 해석하지 않는다. 원래 술식별 본체·운동을 유지하면서 발동, 실제 접점의 타격, 소환진, 방어막을 KTP의 완제품 파티클 하위체로 연결한다. 한국적 문양이 실제 화면에서 주역으로 보이는지는 대표 촬영을 검토 중이며 **120개 전체는 미검증**이다.

## 출처와 재사용 기준

- 250개 프리팹 계층·원래 ParticleSystem 설정을 조사한 `KTP_REWORK_SELECTION.json`의 16개 후보에서 출발했다. 최종 선택은 숫자 순환이나 무작위 배치가 아니라 속성·행동 의미에 따른 아래 표다.
- `build_manifest.json`의 120개 이름·의도를 읽었다. 100개 배정, 20개 의도적 공백을 유지한다. CSV, Assigned, Behavior, GameplayConnected, 수명·입력·피해·명중 시각·성능 설정은 변경하지 않는다.
- 실제 Pattern 38/61/147/225/188/125/175/51/8/50/162/250의 알파 도안을 열어 확인했다. Pattern225는 삼태극·괘 도안이며 각형 범용 기호로 취급하지 않는다. Pattern188은 물고기 두 마리, Pattern125는 직각 격자, Pattern147은 가늘고 긴 꽃잎의 방사판이다. Pattern147을 특정 연꽃 종으로 단정하지 않는다.
- Fly는 `Charge` 또는 `Explosion`만 복제한다. 전체 Fly의 Projectile 데모와 Animator는 포함하지 않는다. Bottom의 문양 PS, 실제 메시, 재질 연결, 크기·회전·노이즈·Trail·시간 곡선을 유지한다. 원본 vendor 파일을 수정하지 않는다.
- 문양 이미지만 추출해 새 도형으로 대체하지 않는다. 필요하면 **원본 Pattern 파티클 하위체 자체**를 다른 원형의 정돈된 필드 구조와 결합한다.

## 오행별 실제 원형

아래 경로는 모두 `Assets/KoreanTraditionalPattern_Effect/Prefabs/` 기준이다. `Pattern`은 해당 프리팹의 단일 이름 하위체를 뜻한다.

| 속성 | 발동 주 문양 / 보조 | 실제 명중 접점 | 소환진 | 방어막 | 선택 근거 |
|---|---|---|---|---|---|
| 목 | Bottom03-01/Pattern + Fly06-01/Charge | Fly01-01/Explosion | Bottom12-01 전체 | Bottom04-01 전체 | 꽃판 발동과 굵은 회전 잎의 타격. root가 튜닝한 대표 4프리팹 그대로 재사용 |
| 화 | Bottom05-01/Pattern + Fly05-01/Charge + Fire_01 | Fly03-01/Explosion | Bottom05-01 전체 | Bottom09-01 전체 + Bottom05-01/Fire_01 | 원래 화염 UV 흐름과 팽창 충격면, 겹친 막에 불꽃 지지층 |
| 토 | Bottom12-01/Pattern, Charge 없음 | Fly10-01/Explosion | Bottom12-01 전체 | Bottom14-01 전체 | 방사 꽃판이 지반을 맺고 엮임 도안·이중 충격파로 접점을 강조. 다중 광면 구조를 토벽 후보로 사용 |
| 금 | Bottom09-01/Pattern + Fly08-01/Charge | Fly07-01/Explosion에서 Tech/Light 제외 | Bottom18-01의 Tech/Light를 끈 잔류 기운 + Bottom09-01/Pattern | Bottom14-01의 Tech/Light 제외 + Bottom09-01/Pattern 직립 패널 | 직각 격자 Pattern125 중심. 원래 Pattern225/175와 디지털 사각 채움은 복제본에서 비활성화 |
| 수 | Bottom15-01/Pattern, Charge 없음 | Fly10-01/Explosion | Bottom15-01 전체 | Bottom15-01 전체 | 물고기 두 마리 Pattern188, 원래 두 겹 ShockWave의 파면, 유연한 외곽 광면 |

토·수의 접점은 동일 Fly10 이중 파면을 공유한다. 이는 120개의 독자적인 전체 효과를 새로 만들었다는 주장이 아니다. 토의 바위·충격 동작과 수의 유도탄·파도 본체는 기존 술식별 구현이 계속 담당한다. 연결된 형상의 실제 크기·광면 과다·문양 읽힘은 root의 Unity 촬영에서 판단한다.

## 크기·광량과 보존 정책

- 발동 주 문양은 원본 PS 하위체 캐리어를 `scale ×0.28`, `Euler X=90°`로 둔다. 보조 Charge는 `Y=90°`, 시작색 알파 ×0.30. 화 Fire_01 지지층도 알파 ×0.30이다. 물·토와 Reserved 발동에는 불필요한 Charge를 붙이지 않는다.
- Impact의 `Explosion` 캐리어는 `Y=90°`. 문양 PS를 유지하고 이름이 Light/Aura/Flare인 광면의 시작 알파만 낮춘다. 원래 색 모드와 모든 알파 키의 상대 곡선은 보존한다. 한 PS에 하위 계층 수만큼 중복 감쇠하지 않는다.
- 새 필드의 광면은 소환진 ×0.22, 방어막 ×0.42. Bottom12의 `Light_00/Light_02` 사각 광면은 복제본에서 비활성화한다. 최대 파티클 시스템 수가 기존 runtime 32개를 넘으면 빌드를 거절한다. 상한을 올리거나 임의로 파티클 층을 잘라 통과시키지 않는다.
- **금 계열 04 변경:** 실제03 촬영에서 두꺼운 흰 사각 입자와 발을 가리는 흰 판이 보여, `DisableDigitalFill`을 금 Impact와 Field에 추가했다. 이름에 `Tech`가 포함되거나 `Light`, `Light_*`인 원본 하위체를 **복제본에서 비활성화**한다. 따라서 초기 설계처럼 Bottom18의 Par_Tech 구조가 계속 주연출로 남아 있다고 설명하지 않는다. 금 발동의 별도 Charge는 이 제거 대상이 아니다. 소스 PS 컴포넌트를 삭제하거나 32개 예산을 늘린 것은 아니며, 격자 Pattern과 남은 곡선 및 추가 직립 패널을 유지한다.
- 방어막은 바닥 문양만으로 충분하다고 간주하지 않는다. root의 C2 실제 관찰에서 높이 있는 막이 부족하여, 신규 화/토/금/수 Shield에는 `Vfx120TraditionalBuilder.AddShieldPanels`로 간격이 있는 직립 곡면 패널 4개를 추가한다. 원본 Pattern PS의 곡선·custom data·재질을 사용하는 root 공용 구현이며, 금은 Bottom09 직각 격자를 선택한다. 목은 패널을 추가한 root 대표 출력 재사용. 4개 PS를 추가한 뒤에도 기존 32개 상한으로 검사한다.
- 추가 패널은 원본 평면 메시 그대로라는 뜻이 아니다. root helper의 새 256 tris 곡면 4개가 원본 Pattern의 수명·알파·UV/custom data·재질을 운반한다. 패널의 시작 크기는 1, 속도·시작 회전은 0으로 정렬하며 Shape와 RotationOverLifetime은 끈다. 이것은 **패널 운반체에 한정된 명시적 각색**이다. 나머지 완제품 하위체를 같은 새 메시로 치환하지 않는다.
- NativeScale은 일반 0.48, Summon 0.65, Shield 0.52, Reserved 0.40이며 0.28–0.65로 제한한다. 수명, 본체 Size/PartScale/Count, 기존 파티클 예산은 그대로다.
- **가/곰/구의 기존 NativeCast/Impact/Field 참조는 우선 보존**한다. 값이 없을 때만 대표 프리팹으로 채운다. 대표 4프리팹 자체는 이 빌더에서 다시 저장하지 않는다. 대표 튜닝 내역은 `ktp_rework_build.txt`를 참조한다.
- **곰/놈/솜의 Meshy SummonPrefab/Scale/Yaw와 본체 메시·재질을 수정하지 않는다.** 소환 5자는 NativeReplaceBody=false. 반려된 몸·옴의 모델을 자동 교체하지 않는다.
- **막 예외:** Behavior가 Shield이지만 바위의 비행·착탄 뒤 엄폐 동작이다. caster 위치의 일반 NativeShield로 본체를 숨기지 않고, 기존 바위 유지와 실제 착탄 Impact만 연결한다. 다른 Shield 10자는 NativeFieldRole=Shield, NativeReplaceBody=true로 연결한다. 그 원형이 원하는 방벽으로 읽히는지는 미검증이다.

## 발동·명중 신호 범위

120개 모두 발동 문양을 배정하되 Reserved 20개는 보조 Charge·Impact·Field가 없는 **비전투 시각 후보**다. 이를 사용 가능한 술식으로 등록하거나 공백의 게임 규칙을 정하지 않는다.

명중 Impact는 적 접점을 갖는 Projectile/Bind/Burst/Wave와 명시 예외 녹·막·곳에만 연결한다. 회복 걱·건, 나무 회복영역 검, 일반 버프와 무기 변신 개시, 일반 방벽, 환경 변화 눈·숫·웅에는 자동 폭발을 붙이지 않는다. 녹은 실제 명중 후 회복존을 만드는 효과라 명중 접점을 갖는다.

빌더는 어떠한 Hit를 발생시키지 않는다. `Vfx120Effect`의 기존 실제 Hit 또는 외부가 넘긴 명중 clock만 사용한다. 예시 발행은 기존 `PreviewControlled && DemonstrationCues`를 모두 만족하는 검수 모드에 한정된다. Profile.Flight만으로 실제 명중을 만들지 않는다. 현재 runtime의 접점 횟수·반복 처리도 변경하지 않는다.

현재 실행 기록의 연결 수: **Cast 120 / Impact 51 / Field 15 (Shield 10 + Summon 5) / Reserved 20**. `Art/SpellVFX120/ktp_catalog_mapping.json`의 2026-09-09 06:16:44 UTC 기록과 아래 의도 매핑이 일치한다. 이 수치는 실제 게임 판정·전수 미술 통과 수가 아니다.

## 출력과 검증 상태

실행 순서: root 소유 `Vfx120TraditionalBuilder.Build()` → 신규 `Vfx120TraditionalCatalogBuilder.Build()`. 대표 4프리팹이 없으면 명시적인 오류로 중단한다. 신규 추가 21프리팹은 `Assets/_Project/Art/SpellVFX120/Traditional/Catalog/`에 저장한다. 대표 4개까지 총 25개 공용 phase 원형을 배정한다.

Build 결과 `Art/SpellVFX120/ktp_catalog_mapping.json`에 실제 프리팹 경로, 원본 경로, 하위체, 캐리어 회전·크기, PS 수, 120프로필 참조·예외·보존 정책이 기록되어 있다. 초기 source 설명에 Par_Tech 재사용 문구가 남아 있더라도 현재 금 변형은 위 `DisableDigitalFill`의 활성 상태를 기준으로 판단한다. 기존 프로필별 본체·이동 연출은 그대로이며 원본 도안에 미술 PASS를 붙이지 않는다.

후속 실제 파일은 Main03 **75장/15프로필**, External03 **40장/8프로필**, Main04 **20장/4프로필**, External04 **15장/3프로필**이다. 각 프로필에 최신 5장+촬영 JSON을 우선 표시하며, 04가 없는 대상은 03을 유지한다. [통합04 실제 검토](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTP_INTEGRATED_REVIEW04.md)는 금 사각 채움 개선과 남은 소환수 가림·정적 자세를 구분한다. 정지 샘플로 보행·타격 애니메이션이나 실제 플레이어 입력 회귀를 통과시켰다고 기록하지 않는다.

| 검사 | 상태 | 근거 / 한계 |
|---|---|---|
| 120개 manifest 의도와 배정 상태 읽기 | 통과 | 100배정 / 20공백, 코드의 명중 예외 명시 |
| 선택한 원본 경로와 Pattern/Fire_01 하위체 | 통과 | 로컬 파일과 250프리팹 계층 조사 JSON 대조 |
| 실제 도안 12종 판별 | 통과 | RGBA 알파 도안을 열어 관찰. 완제품 모션 촬영 통과와는 별개 |
| C#9 정적 컴파일 | 통과 | 초기 빌더·패널 helper 기준. Unity 6000.3.9f1 관리 DLL과 App/EditorTools DLL 참조, 경고0/오류0 |
| Unity Build·프로필 배정 기록 | 통과 | 후속 실제 `ktp_catalog_mapping.json`: total120 / Cast120 / Impact51 / Field15 / Reserved20. 이 문서 갱신 작업은 Unity를 실행하지 않고 결과를 읽음 |
| 120프로필 Edit preview 연결 계약 | 통과 | `traditional_catalog_audit.json`, 06:32:21 UTC: profileCount120 / passed120 / failed0 / errorCount0. 진단용 clock을 공급한 연결 계약이며 게임 판정·미술·성능 통과 아님 |
| 실제 Play 5종 생성~수명 종료 | 통과 | `traditional_play_audit.json`: 가·곰·구·놈·솜 completed5 / failed0 / errors0 / shaderErrors0, 원본·씬 불변과 설정 복원·정리 확인. 진단용 명중 시각을 공급한 객체 생성·종료 범위 |
| 120개 한국적 문양 주역성·가시성·성능 | 미검증 | root가 실제 촬영·프로파일링 후 판정 |
| 게임 판정·입력·수명 회귀 | 미검증 | 빌더는 관련 값을 수정하지 않지만 실제 런타임 회귀 시험은 별도 |

최신 영상은 `ClipsKTPIntegrated04` **Main 6개**, `ClipsKTPExternal04` **External 4개**다. 각각 `encoding_report.json`과 `<id>_encoding.json`에 실제 전체 디코드·프레임·타이밍 검증을 남겼다. 24fps/1280×720이며 Life 뒤까지 포함한다. **C2 Editor 카메라의 진단 재생 영상**으로, 실제 사용자 입력 플레이 영상이나 120종 전수 영상이 아니다. 검수 페이지는 MP4·촬영 메타데이터 해시가 해당 기록과 맞는 파일만 연결한다.

## 120개 배정

아래 표의 ‘공백’은 비전투 시각 후보이며 ‘명중’은 기존 외부 신호가 있을 때만 재생되는 연결이다.

| 술식 | 속성 | 기존 분류 | 발동 | 명중 | 필드 |
|---|---|---|---|---|---|
| 가 | 목 | Projectile | 목 원형 | 외부 명중 시 | 없음 |
| 각 | 목 | Bind | 목 원형 | 외부 명중 시 | 없음 |
| 간 | 목 | Projectile | 목 원형 | 외부 명중 시 | 없음 |
| 감 | 목 | Bind | 목 원형 | 외부 명중 시 | 없음 |
| 갓 | 목 | Projectile | 목 원형 | 외부 명중 시 | 없음 |
| 강 | 목 | Projectile | 목 원형 | 외부 명중 시 | 없음 |
| 거 | 목 | Shield | 목 원형 | 없음 | 방어 패널 |
| 걱 | 목 | Heal | 목 원형 | 없음 | 없음 |
| 건 | 목 | Heal | 목 원형 | 없음 | 없음 |
| 검 | 목 | Zone | 목 원형 | 없음 | 없음 |
| 것 | 목 | Weapon | 목 원형 | 없음 | 없음 |
| 겅 | 목 | Buff | 목 원형 | 없음 | 없음 |
| 고 | 목 | Burst | 목 원형 | 외부 명중 시 | 없음 |
| 곡 | 목 | Zone | 목 원형 | 없음 | 없음 |
| 곤 | 목 | Burst | 목 원형 | 외부 명중 시 | 없음 |
| 곰 | 목 | Summon | 목 원형 | 없음 | 소환진·본체 보존 |
| 곳 | 목 | Zone | 목 원형 | 외부 명중 시 | 없음 |
| 공 | 목 | Wave | 목 원형 | 외부 명중 시 | 없음 |
| 구 | 목 | Shield | 목 원형 | 없음 | 방어 패널 |
| 국 | 목 | Zone | 목 원형 | 없음 | 없음 |
| 군 | 목 | Reserve | 공백 문양만 | 없음 | 없음 |
| 굼 | 목 | Reserve | 공백 문양만 | 없음 | 없음 |
| 굿 | 목 | Reserve | 공백 문양만 | 없음 | 없음 |
| 궁 | 목 | Reserve | 공백 문양만 | 없음 | 없음 |
| 나 | 화 | Projectile | 화 원형 | 외부 명중 시 | 없음 |
| 낙 | 화 | Bind | 화 원형 | 외부 명중 시 | 없음 |
| 난 | 화 | Projectile | 화 원형 | 외부 명중 시 | 없음 |
| 남 | 화 | Bind | 화 원형 | 외부 명중 시 | 없음 |
| 낫 | 화 | Projectile | 화 원형 | 외부 명중 시 | 없음 |
| 낭 | 화 | Projectile | 화 원형 | 외부 명중 시 | 없음 |
| 너 | 화 | Shield | 화 원형 | 없음 | 방어 패널 |
| 넉 | 화 | Zone | 화 원형 | 없음 | 없음 |
| 넌 | 화 | Buff | 화 원형 | 없음 | 없음 |
| 넘 | 화 | Buff | 화 원형 | 없음 | 없음 |
| 넛 | 화 | Weapon | 화 원형 | 없음 | 없음 |
| 넝 | 화 | Buff | 화 원형 | 없음 | 없음 |
| 노 | 화 | Burst | 화 원형 | 외부 명중 시 | 없음 |
| 녹 | 화 | Heal | 화 원형 | 외부 명중 시 | 없음 |
| 논 | 화 | Burst | 화 원형 | 외부 명중 시 | 없음 |
| 놈 | 화 | Summon | 화 원형 | 없음 | 소환진·본체 보존 |
| 놋 | 화 | Burst | 화 원형 | 외부 명중 시 | 없음 |
| 농 | 화 | Zone | 화 원형 | 없음 | 없음 |
| 누 | 화 | Shield | 화 원형 | 없음 | 방어 패널 |
| 눅 | 화 | Reserve | 공백 문양만 | 없음 | 없음 |
| 눈 | 화 | Zone | 화 원형 | 없음 | 없음 |
| 눔 | 화 | Reserve | 공백 문양만 | 없음 | 없음 |
| 눗 | 화 | Reserve | 공백 문양만 | 없음 | 없음 |
| 눙 | 화 | Reserve | 공백 문양만 | 없음 | 없음 |
| 마 | 토 | Projectile | 토 원형 | 외부 명중 시 | 없음 |
| 막 | 토 | Shield | 토 원형 | 외부 명중 시 | 기존 착탄 바위 보존 |
| 만 | 토 | Projectile | 토 원형 | 외부 명중 시 | 없음 |
| 맘 | 토 | Bind | 토 원형 | 외부 명중 시 | 없음 |
| 맛 | 토 | Projectile | 토 원형 | 외부 명중 시 | 없음 |
| 망 | 토 | Projectile | 토 원형 | 외부 명중 시 | 없음 |
| 머 | 토 | Shield | 토 원형 | 없음 | 방어 패널 |
| 먹 | 토 | Buff | 토 원형 | 없음 | 없음 |
| 먼 | 토 | Buff | 토 원형 | 없음 | 없음 |
| 멈 | 토 | Buff | 토 원형 | 없음 | 없음 |
| 멋 | 토 | Weapon | 토 원형 | 없음 | 없음 |
| 멍 | 토 | Buff | 토 원형 | 없음 | 없음 |
| 모 | 토 | Wave | 토 원형 | 외부 명중 시 | 없음 |
| 목 | 토 | Wave | 토 원형 | 외부 명중 시 | 없음 |
| 몬 | 토 | Burst | 토 원형 | 외부 명중 시 | 없음 |
| 몸 | 토 | Summon | 토 원형 | 없음 | 소환진·본체 보존 |
| 못 | 토 | Burst | 토 원형 | 외부 명중 시 | 없음 |
| 몽 | 토 | Zone | 토 원형 | 없음 | 없음 |
| 무 | 토 | Shield | 토 원형 | 없음 | 방어 패널 |
| 묵 | 토 | Reserve | 공백 문양만 | 없음 | 없음 |
| 문 | 토 | Reserve | 공백 문양만 | 없음 | 없음 |
| 뭄 | 토 | Zone | 토 원형 | 없음 | 없음 |
| 뭇 | 토 | Reserve | 공백 문양만 | 없음 | 없음 |
| 뭉 | 토 | Reserve | 공백 문양만 | 없음 | 없음 |
| 사 | 금 | Projectile | 금 원형 | 외부 명중 시 | 없음 |
| 삭 | 금 | Projectile | 금 원형 | 외부 명중 시 | 없음 |
| 산 | 금 | Bind | 금 원형 | 외부 명중 시 | 없음 |
| 삼 | 금 | Bind | 금 원형 | 외부 명중 시 | 없음 |
| 삿 | 금 | Projectile | 금 원형 | 외부 명중 시 | 없음 |
| 상 | 금 | Projectile | 금 원형 | 외부 명중 시 | 없음 |
| 서 | 금 | Shield | 금 원형 | 없음 | 방어 패널 |
| 석 | 금 | Buff | 금 원형 | 없음 | 없음 |
| 선 | 금 | Buff | 금 원형 | 없음 | 없음 |
| 섬 | 금 | Buff | 금 원형 | 없음 | 없음 |
| 섯 | 금 | Weapon | 금 원형 | 없음 | 없음 |
| 성 | 금 | Buff | 금 원형 | 없음 | 없음 |
| 소 | 금 | Projectile | 금 원형 | 외부 명중 시 | 없음 |
| 속 | 금 | Projectile | 금 원형 | 외부 명중 시 | 없음 |
| 손 | 금 | Buff | 금 원형 | 없음 | 없음 |
| 솜 | 금 | Summon | 금 원형 | 없음 | 소환진·본체 보존 |
| 솟 | 금 | Projectile | 금 원형 | 외부 명중 시 | 없음 |
| 송 | 금 | Projectile | 금 원형 | 외부 명중 시 | 없음 |
| 수 | 금 | Shield | 금 원형 | 없음 | 방어 패널 |
| 숙 | 금 | Reserve | 공백 문양만 | 없음 | 없음 |
| 순 | 금 | Reserve | 공백 문양만 | 없음 | 없음 |
| 숨 | 금 | Reserve | 공백 문양만 | 없음 | 없음 |
| 숫 | 금 | Zone | 금 원형 | 없음 | 없음 |
| 숭 | 금 | Reserve | 공백 문양만 | 없음 | 없음 |
| 아 | 수 | Projectile | 수 원형 | 외부 명중 시 | 없음 |
| 악 | 수 | Projectile | 수 원형 | 외부 명중 시 | 없음 |
| 안 | 수 | Projectile | 수 원형 | 외부 명중 시 | 없음 |
| 암 | 수 | Bind | 수 원형 | 외부 명중 시 | 없음 |
| 앗 | 수 | Bind | 수 원형 | 외부 명중 시 | 없음 |
| 앙 | 수 | Projectile | 수 원형 | 외부 명중 시 | 없음 |
| 어 | 수 | Shield | 수 원형 | 없음 | 방어 패널 |
| 억 | 수 | Buff | 수 원형 | 없음 | 없음 |
| 언 | 수 | Buff | 수 원형 | 없음 | 없음 |
| 엄 | 수 | Buff | 수 원형 | 없음 | 없음 |
| 엇 | 수 | Weapon | 수 원형 | 없음 | 없음 |
| 엉 | 수 | Buff | 수 원형 | 없음 | 없음 |
| 오 | 수 | Wave | 수 원형 | 외부 명중 시 | 없음 |
| 옥 | 수 | Wave | 수 원형 | 외부 명중 시 | 없음 |
| 온 | 수 | Wave | 수 원형 | 외부 명중 시 | 없음 |
| 옴 | 수 | Summon | 수 원형 | 없음 | 소환진·본체 보존 |
| 옷 | 수 | Wave | 수 원형 | 외부 명중 시 | 없음 |
| 옹 | 수 | Wave | 수 원형 | 외부 명중 시 | 없음 |
| 우 | 수 | Shield | 수 원형 | 없음 | 방어 패널 |
| 욱 | 수 | Reserve | 공백 문양만 | 없음 | 없음 |
| 운 | 수 | Reserve | 공백 문양만 | 없음 | 없음 |
| 움 | 수 | Reserve | 공백 문양만 | 없음 | 없음 |
| 웃 | 수 | Reserve | 공백 문양만 | 없음 | 없음 |
| 웅 | 수 | Zone | 수 원형 | 없음 | 없음 |
