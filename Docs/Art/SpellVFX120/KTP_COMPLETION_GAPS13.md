# KTP VFX120 — 완료 요건 차이 점검 13

**전체 목표: NOT COMPLETE.** 2026-09-09 16:59 KST 전후의 디스크 자료를 읽은 점검이다. 진행 중인 13차 빌드·촬영 결과를 미리 포함하지 않는다. Unity 실행, 코드·프로필 변경, 새 검사는 수행하지 않았다.

## 완료의 기준과 현재 수량

[현재 Spec](C:/Users/yj666/Oheangbu/Docs/Specs/SPEC-SPELL-VFX120.md:9)은 **120칸 = 배정100 + 의도적 공백20**, 실행 가능한 게임 술식은 **15개**로 구분한다. 배정된 나머지85개와 공백20개의 게임 규칙 구현은 이번 완료 조건에 추가하지 않는다. 문양이 120개에 연결됐다는 사실도 120개 공격·버프가 실행된다는 뜻이 아니다.

프로필 120개를 직접 읽어 확인한 수량은 `Assigned=100`, NativeCast 120, NativeImpact 51, NativeField 15, NativeBody 2(노·모), SummonPrefab 3(곰·놈·솜)이다. 필드15는 방어막10 + 소환진5다. [카탈로그 원장](C:/Users/yj666/Oheangbu/Art/SpellVFX120/ktp_catalog_mapping.json)과 일치한다. 공통 발동·명중·필드 연결은 글자별 주형상·운동 완성과 별개의 항목이다.

Art Bible의 [가독 우선](C:/Users/yj666/Oheangbu/Docs/오행부_ArtAudio_Bible_v0_1.md:13), [순간 강조 후 먹으로 가라앉음](C:/Users/yj666/Oheangbu/Docs/오행부_ArtAudio_Bible_v0_1.md:31), 실루엣 원칙은 유지한다. 이번 Spec이 허용한 보조 색·텍스처와 KTP 완제품 하위 연출 재사용을 오래된 무텍스처·비재사용 규약으로 다시 금지하지 않는다. 대신 [글자별 동사와 오행 물성](C:/Users/yj666/Oheangbu/Docs/Specs/SPEC-SPELL-VFX120.md:16), 네온·백색 포화·사각 광면의 잔여를 실제 화면으로 판단해야 한다.

## 게임 연결 15행 — 서로 다른 검증 경계

아래 **F**는 [gameplay_diagnostic_camera_audit.json](C:/Users/yj666/Oheangbu/Art/SpellVFX120/gameplay_diagnostic_camera_audit.json)의 과거 15종 PASS다. 임시 채널에 인정된 `DrawnLetter`를 넣고, 임시 CombatLoopWiring/InkPool/표적과 실제 어댑터의 `LateUpdate → SpawnPattern → Update`를 사용했다. 15행 모두 신규 프로필 일치, 목표·AreaPlan 참조/내용, 기존 효과 중복0, 정상 종료를 관측했다. 피해 콜백은 합계26회지만 **임시 EnemyVitals 대상**이며 실제 PlayerRig의 인식·DI·적 AI·패링 결과 검사는 아니다. KTP 재제작 전 자료이므로 최신 표현의 15종 통과로 승계하지 않는다.

**I12**는 [player_input_audit_native12.json](C:/Users/yj666/Oheangbu/Art/SpellVFX120/player_input_audit_native12.json)의 최신 실제 입력3종 PASS다. 등록된 XML 획을 가상 Mouse/Keyboard로 재생해 기존 입력 수집·실제 인식·동일 채널/DI·시전·신규 VFX·종료까지 통과했다. 세 행 모두 `clockMatches=true`, 인식/채널/생성 각1회, 남은 효과0이다. 실제 입력장치나 자유 필기 검사는 아니다.

모든 15프로필은 [최신 Native Edit 계약](C:/Users/yj666/Oheangbu/Art/SpellVFX120/traditional_catalog_audit.json)의 120행 검사에 포함된다. 이것은 진단 시계를 공급한 `Begin/Sample` 검사이며 아래 I12의 빈칸을 채우지 않는다.

| 게임 술식·카탈로그 ID | 기존 게임 종류 / 현재 KTP 연결 | F에서 확인한 시계·임시 피격 | I12 실제 PlayerRig 입력 | 아직 없는 최신 실제 경로 증거 |
|---|---|---|---|---|
| 가 / 001_AC00 | 단일 / 발동·명중 | 비행 0.256443초 전달, 1/1회 | **PASS**, 허공 시전. impact=0, profile flight=0.6초 | 실제 대상이 있는 거리 기반 비행·명중 연결 |
| 나 / 025_B098 | 단일 / 발동·명중 | 비행 0.333376초, 1/1회 | 미검증 | 인식부터 시전, 대상·비행 시계 전달 |
| 마 / 049_B9C8 | 단일 / 발동·명중 | 비행 0.333376초, 1/1회 | 미검증 | 인식부터 시전, 대상·비행 시계 전달 |
| 사 / 073_C0AC | 단일 / 발동·명중 | 비행 0.185209초, 1/1회 | 미검증 | 인식부터 시전, 빠른 비행 시계 전달 |
| 아 / 097_C544 | 단일 / 발동·명중 | 비행 0.555626초, 1/1회 | 미검증 | 인식부터 시전, 느린 비행 시계 전달 |
| 고 / 013_ACE0 | 광역 Circle / 발동·명중 | 확정 AreaPlan 동일, 3/3회 | 미검증 | 기존 대상점·Circle 계획을 실제 배선에서 받는 기록 |
| 노 / 037_B178 | 광역 Cone / 발동·명중·FlameCone 본체 | 확정 AreaPlan 동일, 3/3회 | **PASS**, 같은 Cone 계획 참조, delay/flight=0.4초 | 대상이 있는 실제 Cone 시전의 예약 시각·가시 범위 확인 |
| 모 / 061_BAA8 | 광역 Path / 발동·명중·SandFront 본체 | 확정 AreaPlan 동일, 3/3회 | 미검증 | 실제 입력→Path 계획→전진 본체. 별도 Play2의 진단 계획은 이 경계를 대신하지 않음 |
| 소 / 085_C18C | 광역 Volley / 발동·명중 | 확정 AreaPlan 동일, 9/9회 | 미검증 | 실제 입력·표적들·Volley 시각열 전달 |
| 오 / 109_C624 | 광역 Path / 발동·명중 | 확정 AreaPlan 동일, 3/3회 | 미검증 | 실제 입력→Path 계획→파동 본체 |
| 거 / 007_AC70 | Parry / 발동·방어막 본체 교체 | guard=4초, bright=0.9초, 피격0 | 미검증 | 실제 인식·기존 가드 시계·종료 전달 |
| 너 / 031_B108 | Parry / 발동·방어막 본체 교체 | guard=4초, bright=0.9초, 피격0 | 미검증 | 실제 인식·기존 가드 시계·종료 전달 |
| 머 / 055_BA38 | Parry / 발동·방어막 본체 교체 | guard=4초, bright=0.9초, 피격0 | **PASS**, 기존 guard/bright/Life 일치 | 실제 들어오는 공격에 대한 패링 결과는 별도 미검증 |
| 서 / 079_C11C | Parry / 발동·방어막 본체 교체 | guard=4초, bright=0.9초, 피격0 | 미검증 | 실제 인식·기존 가드 시계·종료 전달 |
| 어 / 103_C5B4 | Parry / 발동·방어막 본체 교체 | guard=4초, bright=0.9초, 피격0 | 미검증 | 실제 인식·기존 가드 시계·종료 전달 |

F의 비행시간은 당시 임시 표적 거리에서 나온 값이다. 고정 설정값이나 변경할 목표 수치가 아니다. 광역은 `receivedImpactClock=0`이어도 별도로 받은 AreaPlan이 시계를 정하므로 0만 보고 시계 검증 실패로 판단하지 않는다. I12의 C2 대상 수는 **0**이다. 먹은 1.0→0.6으로 실제 소비했고 강제 복구하지 않았다. 입력 필터·바인딩·현재 장치·감사 PlayerLoop·카메라·시간·커서는 복원 관측됐으나, 이것이 이동/락온/피격 중단 등 전체 전환 검증은 아니다.

### 현재 배선과 감사 소스 근거

- [PlayerRig.prefab:956](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Prefabs/Rig/PlayerRig.prefab:956)은 `SpellVisualSet_120` GUID `9ba170fd23c53dc418707872eb7b2056`을 참조한다. 같은 프리팹의 [CombatSystems 연결](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Prefabs/Rig/PlayerRig.prefab:440)과 어댑터는 같은 LetterDrawn 채널 및 기존 입력 컴포넌트를 사용한다. [SpellBook_Proto](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Data/Configs/SpellBook_Proto.asset:16)는 여전히 위15개다.
- [어댑터 구독](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/BrushStrokeFeedAdapter.cs:146) → [같은 프레임 이벤트 순서 소비](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/BrushStrokeFeedAdapter.cs:223) → [Vfx120 생성 분기](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/BrushStrokeFeedAdapter.cs:564)가 실제 교체 접점이다. 기존 확정 목표·시계·계획을 전달하고 PatternEffectLifetime의 구 틴트 경로를 건너뛴다.
- [CombatLoopWiring:303](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/CombatLoopWiring.cs:303)의 거리 기반 단일 공격, [Cone:318](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/CombatLoopWiring.cs:318), [Path:368](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/App/CombatLoopWiring.cs:368)가 권위 있는 계획을 만든다. VFX 완료를 기다려 피해를 정하는 구조로 바꾸는 일은 남은 요건이 아니다.
- F는 [별도 fixture 생성](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/Editor/Vfx120GameplayAudit.cs:320) 및 [인정된 글자 주입](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/Editor/Vfx120GameplayAudit.cs:502)을 사용한다. I12는 [실제 C2 배선 검증](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/Editor/Vfx120PlayerInputAudit.cs:123)을 통과해야 하고 [현재 대상 문자열은 가노머](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/Editor/Vfx120PlayerInputAudit.cs:274)로 한정돼 있다. 나머지12개의 실행 결과 파일은 이 도구로 아직 만들어지지 않았다.

## 요건 단위 판정과 정확히 부족한 전달물

| 요건 | 확인한 증거 / 판정 범위 | 완료에 부족한 것 |
|---|---|---|
| 120칸 데이터·원본 보존 | **부분 PASS.** 직접 읽은120프로필 및 100/20 구분, Native 매핑. 07:42:19 UTC의 traditional_catalog_audit는 120행·실패0·원본불변·정리 확인. | 13차 이후 새로 바뀐 자산/소스는 해당 버전 검사와 묶어야 한다. 감사의 `unassigned=0`은 Native 참조가 모두 없어서 건너뛴 행0이라는 뜻이며 CSV 공백20을 없앤 수치가 아니다([판정 코드](C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Scripts/Editor/SpellVFX120/Vfx120TraditionalAudit.cs:199)). |
| 배정100개의 주형상·동사·오행·KTP 미술 | **미완료.** 공통 문양만 붙인 결과로 주형상까지 완성했다고 세지 않는다. [10차 실제 검토](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTP_BODY_REVIEW10.md)에서 오의 하단 광면, 모의 약한 토사 양감, 노의 지형 가림이 남았다. | 각 배정 ID의 채택 버전에 대응하는 주형상/운동·동사·색/실루엣 판정. 13차 교정이 과거 실패를 해소했는지는 새 실제 렌더 근거가 필요하다. 비배정20은 공백 상태로 별도 표시한다. |
| Meshy 소환수5종 | **미완료.** [실제 임포트](C:/Users/yj666/Oheangbu/Art/SpellVFX120/meshy_unity_import.json)는 사슴·해태·호랑이3종만, 합37,489 tris. 장승·수룡은 재시도 반려, [장승 무비용 구제](C:/Users/yj666/Oheangbu/Art/SpellVFX120/MeshySummons/Blender/JangseungSalvage01/SALVAGE_REVIEW.md)도 최종 미술 반려다. | 몸/옴에 채택 가능한 장승/수룡 모델 및 그 모델의 Blender 정리·Unity 임포트·실제 화면 근거. 기존 절차형 본체나 반려 형상을 완료 모델로 세지 않는다. 현재3종은 정적 출현/소멸이며 리깅·보행·타격은 미제작이다. 이 자료로 소환 AI 완료를 주장하지 않는다. 추가 유료 요청은 닫힌 원장을 따른다. |
| 게임15종 실제 시전 교체 | **3종 부분 PASS / 종합 미검증.** I12는 실제 경로3종, F는 과거 fixture15종. | 위 표의 나머지12행을 기존 C2 PlayerRig 입력/인식/DI에서 실행한 결과와, 단일 공격의 실제 대상/비행 연결 증거. 현재 적0 자료에는 그 경로가 없다. 모든 적 AI나 새85개 게임 규칙을 제작하라는 요건은 아니다. |
| 전체 목록·최신 단계별 영상 | **부분 완료.** 120항목 선택기와 과거 전수120영상은 존재. 최신 KTP 영상은04의10파일+10의4파일=14파일이며, **고유 술식8개/최대120칸**의 두 시점 자료다. [영상10 검토](C:/Users/yj666/Oheangbu/Art/SpellVFX120/KTP_VIDEO_REVIEW10.md)는 원본32표본의 관찰이다. | KTP 재제작의 채택 버전에 대응하는 나머지 항목의 개시·진행·도달·소멸 영상 및 ID별 실제 검토. 전체1280×720 디코드 성공을 미술 PASS로 바꾸지 않는다. 정지 화면12와 영상10의 픽셀 동일성도 검사하지 않았다. |
| 밝음·어두움·전투 배경의 가독 | **부분 검토 / 최신 전수 미검증.** C2 대표 자료는 있고 기존 contrast_audit는 KTP 이전 자료다. | 최신 프로필의 명암 두 배경과 실제 전투 배경에서의 실루엣/동사 판정. 지형에 가려 안 보이는 구간은 렌더 생성 수나 수치 대비로 통과시키지 않는다. |
| 최신 자원 수명·동시 비용 | **한정 기술 PASS.** KTP5종 실제 Update(06:30, 구 MVID), 노·모2종 최신 실제 Update(07:43, 새 MVID), 120 Edit 계약 통과. | 최신 KTP Native 구성 전체의 실제 Update·동시 사용 종료/잔존·비용 근거. `runtime_audit.json`의120수명·1/16비용은 KTP 이전 버전이며 재질+1의 Editor 폰트 캐시 관찰도 누수 없음 확정이 아니다. 현재 C2 전체 프레임 수치는 VFX 단독 CPU나 GPU/목표FPS 인증을 대신하지 않는다. |

최신 한정 기술 검사는 [element_wash_audit.json](C:/Users/yj666/Oheangbu/Art/SpellVFX120/element_wash_audit.json)의11행 및 [element_wash_play_audit.json](C:/Users/yj666/Oheangbu/Art/SpellVFX120/element_wash_play_audit.json)의 노·모2행이다. [traditional_play_audit.json](C:/Users/yj666/Oheangbu/Art/SpellVFX120/traditional_play_audit.json)은 가·곰·구·놈·솜5종만 관측했고, 그중 **가 이외 네 개는 SpellBook15 밖의 카탈로그**다. 따라서 Play5+Play2+입력3을 합산해 서로 다른10개 게임 술식의 통과로 세지 않는다.

원시 획의 전후 비트 동일성, 자유 필기, 실제 하드웨어, 이동/락온/피격 중단 전체 전환, 실제 적 패링 결과는 현재 자료에서 미검증이다. 이를 이번 표현 교체 완료를 위해 입력 좌표·가드/피해 시계를 고쳐야 한다는 요구로 해석하지 않는다. 기존 규칙의 권위를 유지한 채 부족한 경로·버전별 증거를 명시하는 것이 이 점검의 범위다.
