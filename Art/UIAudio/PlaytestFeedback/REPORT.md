# 플레이테스트 HUD · 효과음 제작 보고서

상태: **생성·변환 자산 준비 완료 / 효과음 서비스 이벤트 PASS_CONTROLLED_SERVICE_TESTS · HUD 현재 상태 렌더 PASS_BOUNDED_RUNTIME · HUD 주입 상태 진단 PASS_BOUNDED_RUNTIME · 5종 소환 등장·소멸 PASS / 사용자 청음·수동 필기·기기 출력 미검증**  
명세: [SPEC-PLAYTEST-UI-AUDIO](../../../Docs/Specs/SPEC-PLAYTEST-UI-AUDIO.md) · [SPEC-WORLD-MACRO-PLAYTEST](../../../Docs/Specs/SPEC-WORLD-MACRO-PLAYTEST.md) · [검토 페이지](REVIEW.html)

런타임 상태는 아래 JSON 보고서에서 읽은 값을 그대로 사용한다. 주입된 인식 완료 글자를 실제 Resolver와 서비스에 전달한 검증은 직접 WAV 재생 시험보다 강한 연결 증거지만, 실제 필기·자연 플레이·사용자 기기 청음 증거는 아니다. 한국적 인상과 최종 음량도 사용자 청음 전에는 확정하지 않는다.

## 제작 결과

- Recraft: 성공 4건과 HTTP 400 무효 요청 1건, 총 시도 5건이다. 성공 응답의 `actual_usage` 합계와 생성 전후 잔량 **1028 → 708**의 차이가 모두 **320 units**다. 무효 요청에는 사용량 필드가 없다.
- ElevenLabs: 공급자 생성 성공 15건, 요청 길이 합계 **17.20초**, 응답 `character-cost` 합계 **189**다. `brush_stroke`는 `actual_usage`가 없으므로 응답 헤더 값 **9**를 사용했다.
- 1차 [`parry.mp3`](Originals/sfx/parry.mp3)는 공급자 호출에는 성공했지만 로컬 분석 RMS -55.7 dBFS로 최종 후보에서 제외했다. 해당 [요청 원장](requests/sfx_parry.json)과 원본은 보존하며, 두 번째 생성 `parry_v2`를 최종 `parry.wav`의 공급자 원본으로 사용한다. 따라서 최종 묶음은 14종이고 공급자 성공 횟수·사용량에는 두 생성이 모두 포함된다.
- Recraft는 잔량 차이까지 확인했다. ElevenLabs의 계정 전체 잔량·청구서는 조회하지 않았으며, 위 수치는 각 응답이 반환한 `character-cost`의 합계다.
- Unity용 결과: 투명 PNG 4개, 44.1kHz mono WAV 14개, WAV 길이 합계 **16.00초**. 무음 0개, 클리핑 표본 0개.
- 모든 공급자 원본 SVG/MP3, 요청 모델·프롬프트·응답 사용량·파일 해시는 `Originals/`와 `requests/`에 보존한다. 비밀키나 인증 헤더는 포함하지 않는다.

## UI 기술 요약

| 자산 | Unity PNG | 투명 픽셀 | SHA-256 | 공급자 원본 |
|---|---:|---:|---|---|
| 체력 먹 획 | 1032×244 | 109,134 | `ecd3271ed262…` | [hp_stroke.svg](Originals/ui/hp_stroke.svg) |
| 먹물병 | 264×476 | 96,426 | `177af8b37773…` | [ink_bottle.svg](Originals/ui/ink_bottle.svg) |
| 일획 락온 원 | 264×257 | 40,924 | `527ecaaba0c6…` | [lock_ring.svg](Originals/ui/lock_ring.svg) |
| 상호작용 한지 | 776×164 | 50,401 | `37e280c31a6b…` | [prompt_paper.svg](Originals/ui/prompt_paper.svg) |

밝은/어두운 미리보기는 같은 투명 PNG에 표시용 먹색/한지색 틴트를 적용한다. 이미지 자체에는 글자가 없으며 상호작용 문구는 런타임 한국어 텍스트가 담당한다.

## 효과음 기술 요약

| 묶음 | 효과음 | 길이 | 포맷 | RMS dBFS | Peak dBFS | Gain dB | Clipped | SHA-256 |
|---|---|---:|---|---:|---:|---:|---:|---|
| 오행 실제 시전 | 목 시전 | 1.08s | 44100 Hz / 1ch | -21.8 | -3.0 | -6.2 | 0 | `12afc52a6e74…` |
| 오행 실제 시전 | 화 시전 | 1.08s | 44100 Hz / 1ch | -16.4 | -3.0 | -5.8 | 0 | `2ff2498dc225…` |
| 오행 실제 시전 | 토 시전 | 1.20s | 44100 Hz / 1ch | -25.1 | -3.0 | -2.6 | 0 | `572cbb64c3be…` |
| 오행 실제 시전 | 금 시전 | 1.08s | 44100 Hz / 1ch | -24.6 | -3.8 | +3.0 | 0 | `41e30379e52c…` |
| 오행 실제 시전 | 수 시전 | 1.20s | 44100 Hz / 1ch | -31.1 | -3.0 | +3.5 | 0 | `f8801a834a00…` |
| 작도·전투 공통 피드백 | 붓 획 | 0.80s | 44100 Hz / 1ch | -19.3 | -3.0 | +3.6 | 0 | `61944403eafa…` |
| 작도·전투 공통 피드백 | 적중 | 0.72s | 44100 Hz / 1ch | -14.3 | -3.0 | -6.2 | 0 | `d8004156d245…` |
| 작도·전투 공통 피드백 | 플레이어 피격 | 0.68s | 44100 Hz / 1ch | -17.7 | -3.5 | -6.2 | 0 | `bced20a13aa7…` |
| 작도·전투 공통 피드백 | 받아치기 | 1.48s | 44100 Hz / 1ch | -23.6 | -3.0 | -0.8 | 0 | `8001cf706c0e…` |
| 작도·전투 공통 피드백 | 먹 회수 | 1.36s | 44100 Hz / 1ch | -24.9 | -3.0 | -5.7 | 0 | `c6606a0c35d0…` |
| 상호작용·휴식 | 상호작용 | 0.52s | 44100 Hz / 1ch | -22.7 | -3.0 | +5.9 | 0 | `7b5384a561ea…` |
| 상호작용·휴식 | 휴식 | 1.76s | 44100 Hz / 1ch | -32.8 | -13.5 | +6.0 | 0 | `e6e1c63aec08…` |
| 정적 소환 외형 | 소환 등장 | 1.76s | 44100 Hz / 1ch | -21.6 | -3.0 | +2.0 | 0 | `3fbf2275eb58…` |
| 정적 소환 외형 | 소환 소멸 | 1.28s | 44100 Hz / 1ch | -35.8 | -3.0 | -0.8 | 0 | `d6b0abc269f9…` |

변환은 앞쪽 공급자 무음만 정리하고 전체 꼬리를 보존했으며, 최대 피크를 -3 dBFS 방향으로 맞추되 증폭은 +6.02 dB로 제한했다. 최종 `parry_v2`는 Peak -3.0 dBFS / RMS -23.6 dBFS다. `rest`는 Peak -13.5 dBFS, `summon_release`는 RMS -35.8 dBFS여서 실제 믹스에서 체감 음량을 확인한다. 14개 모두 `listening_review=USER_REVIEW_PENDING`이다.

## 화면과 런타임 증거

- [기존 VisualCorridor 정지 이미지](../../World/WorldMacro/Playtest/VisualCorridor/mine_exit.png): UI 적용 전 맥락 참고이며 이번 작업에서 촬영한 새 이미지가 아니다.
- [hud_live_hud_actual_1080p.png](Screenshots/hud_live_hud_actual_1080p.png): 저장된 현재 서비스 상태를 바꾸지 않은 런타임 캡처 · 수동 플레이 장면이나 사용자 시인성 승인 증거가 아님. 판정은 연결된 검증 보고서를 따른다.
- [hud_states_hud_state_all_four_1080p.png](Screenshots/hud_states_hud_state_all_four_1080p.png): 서비스 값을 통제한 UI 진단 상태 이미지 · 자연 플레이 입력 장면 증거가 아님. 판정은 연결된 검증 보고서를 따른다.
- [hud_states_hud_state_low_hp_1080p.png](Screenshots/hud_states_hud_state_low_hp_1080p.png): 서비스 값을 통제한 UI 진단 상태 이미지 · 자연 플레이 입력 장면 증거가 아님. 판정은 연결된 검증 보고서를 따른다.

실제 보고서 판정:

- **효과음 서비스 이벤트: PASS_CONTROLLED_SERVICE_TESTS** (판정 PASS) — 보고서 status=PASS_CONTROLLED_SERVICE_TESTS · 검사 21개 중 PASS 21개 · [runtime_audio.json](runtime_audio.json)
  - 보고서 범위: Live Playtest services with a temporary target and injected recognized letters; simulated focus permission for audio. Not manual input, device output, listening or visual approval.
  - 경계: 주입된 인식 완료 글자와 통제된 전투 서비스 경로 검증이다. 직접 청음·실제 필기·사용자 기기 출력 증거가 아니다.
- **HUD 현재 상태 렌더: PASS_BOUNDED_RUNTIME** (판정 PASS) — 보고서 상태 필드 기준 · [hud_live_runtime_review.json](Validation/hud_live_runtime_review.json)
  - 보고서 범위: Actual W_WorldMacro_Playtest current HUD state and one 1920x1080 still. No input synthesis, gameplay-state mutation, automatic walking, audio judgment, or user visual approval.
  - 경계: 저장된 현재 서비스 상태를 바꾸지 않고 캡처한 런타임 렌더다. 수동 이동·전투 장면이나 사용자 시인성 승인 증거가 아니다.
- **HUD 주입 상태 진단: PASS_BOUNDED_RUNTIME** (판정 PASS) — 보고서 상태 필드 기준 · [hud_states_runtime_review.json](Validation/hud_states_runtime_review.json)
  - 보고서 범위: Actual W_WorldMacro_Playtest Canvas with two explicitly injected presentation-only states. Live gameplay values are sampled first and restored after capture; images are UI composition evidence, not gameplay-event evidence or user visual approval.
  - 경계: HP·먹·락온·그로기·받아치기 값을 통제해 네 표시와 저체력 상태·복원을 확인한 진단이다. 자연 플레이 장면 증거가 아니다.
- **5종 소환 등장·소멸: PASS** (판정 PASS) — Five accepted casts spawned, remained static, rendered once each and self-cleaned. · [summon_runtime_review.json](Validation/summon_runtime_review.json)
  - 보고서 범위: Editor Play: injected already-recognized DrawnLetter plus normal subscribed stroke/Commit events -> live SpellResolver/CombatLoopWiring/BrushStrokeFeedAdapter -> authored VFX Update/destruction. This is not raw handwriting, pen input, user visual approval, movement, attack, damage, defence or progression evidence.
  - 경계: 주입된 인식 완료 글자를 실제 Resolver/먹 승인/표현 수명 경로에 전달한 검증이다. 실제 펜 입력·수동 필기 증거가 아니다.

검증 보고서:

- [runtime_audio.json](runtime_audio.json): 현재 판정 · 효과음 서비스 이벤트
- [audio_first_attempt_fixed_delay_finding.json](Validation/Attempts/audio_first_attempt_fixed_delay_finding.json): 과거 시도 기록 · 현재 판정 아님
- [hud_first_capture_listener_finding.json](Validation/Attempts/hud_first_capture_listener_finding.json): 과거 시도 기록 · 현재 판정 아님
- [audio_diagnostics.json](Validation/audio_diagnostics.json): 보조 검증 자료
- [audio_listeners.json](Validation/audio_listeners.json): 보조 검증 자료
- [build_candidate.json](Validation/build_candidate.json): 보조 검증 자료
- [extended_events.json](Validation/extended_events.json): 보조 검증 자료
- [hud_install.json](Validation/hud_install.json): 보조 검증 자료
- [hud_live_runtime_review.json](Validation/hud_live_runtime_review.json): 현재 판정 · HUD 현재 상태 렌더
- [hud_states_runtime_review.json](Validation/hud_states_runtime_review.json): 현재 판정 · HUD 주입 상태 진단
- [hud_validate.json](Validation/hud_validate.json): 보조 검증 자료
- [package_report.json](Validation/package_report.json): 보조 검증 자료
- [post_build_preservation.json](Validation/post_build_preservation.json): 보조 검증 자료
- [preservation.json](Validation/preservation.json): 보조 검증 자료
- [smoke_restart.json](Validation/smoke_restart.json): 보조 검증 자료
- [smoke_start.json](Validation/smoke_start.json): 보조 검증 자료
- [summon_runtime_review.json](Validation/summon_runtime_review.json): 현재 판정 · 5종 소환 등장·소멸

## 남은 판정

1. 사용자가 실제 필기로 인식·시전 흐름을 확인한다. 주입된 인식 완료 이벤트는 수동 필기 증거가 아니다.
2. 사용자 기기 출력으로 14개를 직접 청음해 한국적 인상, 오행 구분과 최종 음량을 판정한다.
3. 자연 플레이 화면에서 HUD 시인성과 중앙 시야 비침범을 확인한다. 진단 상태 이미지는 자연 플레이 장면이 아니다.

## 재생성

```powershell
python Tools/PlaytestFeedback/build_review.py
```

외부 CDN, 자동 재생, 공급자 호출, Unity 실행 없이 현재 로컬 원장과 산출물만 읽어 `REVIEW.html`과 이 보고서를 다시 만든다.

## UI·효과음 포함 빌드

[Windows 테스트 빌드 ZIP](../../../Builds/Playtest-20260915/Oheangbu_Playtest_20260913T170336Z.zip) · 1.13 GB. 빌드 오류 0건, 기존 경고 581건. 별도 슬롯 시작·저장·재실행 PASS, 렌더 성능·기기 청음은 미검증. ZIP CRC 확인.

링크 검사: 로컬 참조 **142개**, 누락 **0개**. REVIEW.html 오디오 컨트롤 **14개**, autoplay **0개**.
