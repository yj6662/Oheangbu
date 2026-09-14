# 효과음·먹 수급 개선

원본 ElevenLabs 녹음과 기존 Unity WAV·프로필을 보존하고, 플레이테스트 전용 파생본을 제작했다. `before_manifest.json`에는 수정 전 파일의 SHA-256과 백업 경로가 들어 있다. 유료 생성은 실행하지 않았다.

## 확인된 원인과 한계

- 기존 최종 WAV 자체에서는 하드 클리핑을 확인하지 못했다. "깨짐"을 PCM 손상으로 단정하지 않는다.
- 원본 단계부터 6 kHz 이상 에너지 비율이 높았다. 기존 금 발동 약 95.84%, 소환 등장 85.09%, 붓 획 62.61%, 상호작용 60.18%다. 금·소환 계열은 피치와 고역을 별도로 편집하고, 나머지는 역할별 필터와 음량을 조정했다. 런타임 피치는 항상 1이다.
- 기존 타격 재생 규칙의 최악 위치 오프라인 합산 예에서는 +1.81 dBFS가 계산됐다. 이는 **실제 Unity 출력 측정이 아닌** 동시 신호 중첩 위험의 재현이다. 새 간격·3개 동시 타격음·Master 여유를 적용한 동일 형식의 합산에서는 -17.74 dBFS였다.
- MP3 → WAV → Vorbis의 중복 손실 압축과 preload OFF를 제거한다. 새 짧은 효과음은 PCM·Decompress On Load·Preload로 가져온다.
- 프로젝트 로그에서 DSP underrun을 입증하는 메시지를 찾지 못했다. 실제 오디오 장치·OS 향상 기능·버퍼 부족은 별도 확인 대상이다.

## 구현

- Master → SFX → Harvest, Master → UI의 실제 AudioMixer를 추가했다. 기존 전체/게임/UI 옵션 직렬화는 유지하고 노출된 dB 파라미터로 연결했다. 고정 Master 여유는 -6 dB다. 사용자 음량을 소스와 믹서에 중복 곱하지 않는다.
- 게임은 고정 소스 12개(붓 1, 갈무리 1, 일반 10), UI는 고정 소스 4개를 사용한다. 이벤트 간격·효과음별 동시 개수·부드러운 시작과 종료를 적용하고, 소스 교체는 짧게 감쇠한 후 처리한다.
- 갈무리는 실제 `IsExtracting`의 진입·유지·종료를 읽어 시작음, 접합부를 교차 페이드한 반복음, 짧은 종료음으로 재생한다. 매 프레임의 `Extracted`를 매번 완성된 오디오 클립으로 재생하지 않는다.
- 먹 시각 효과는 기존 판정과 수급량을 그대로 읽는다. 주 가닥 1개와 가는 보조 가닥 2개, 같은 경과 시간으로 이동하는 작은 방울을 사용한다. 모든 가닥의 끝은 현재 프레임의 실제 붓 끝 앵커에 일치한다.
- 가닥은 기존 Recraft 먹 획의 알파 텍스처를 재사용한다. 부드러운 가장자리·먹 농도 변화·작은 흔들림을 사용하며, 발광하지 않는다. 1 m 반경의 나선·불규칙 순단·불투명 Spike 재질은 이 파생 프로필에서 사용하지 않는다.
- 흐름은 시전 해제 때 짧게 가늘어지고, 정지·비활성·장면 종료 때 잔여 줄기와 입자를 정리한다.
- `InkPool.Gained`는 실제 증가량만 알린다. 가득 찬 계정·Restore·소모는 수급 강조를 발생시키지 않는다. HUD는 방금 받은 막대 구간만 옅게 강조하고 연속 수급을 한 구간으로 합친다.

## 재현

오프라인:

1. `Tools/PlaytestPolish/AudioInk/prepare_audio.py`
2. `Tools/PlaytestPolish/AudioInk/verify_audio.py`

Unity는 담당 에이전트가 아래 명시적 진입점만 순서대로 실행한다. 도구 자체에는 빌드·자동 재생·씬 재생성·자동 캡처가 없다.

`Oheangbu.EditorTools.WorldMacro.PlaytestAudioInkAuthoring.Execute(action)`

| action | 범위 |
|---|---|
| `prepare` | 독립 믹서·파생 프로필·재질·PCM 가져오기 |
| `contracts` | 실제 증가량 이벤트·페이드·음량 변환·흐름 양 끝 계약 검사 |
| `apply-current` | 저장된 플레이테스트 씬만 백업 후 오디오·흐름·UI 테마 연결 |
| `apply-title` | 제목 씬의 UI 오디오 테마만 연결하고 원래 활성 씬 복원 |
| `validate` | 가져오기·믹서·프로필·재질 연결 확인 |
| `runtime` | 현재 Play 상태의 음성 수·그룹·피치·실제 붓 끝 연결 스냅샷 |

## 검증 상태

| 항목 | 상태 | 근거/범위 |
|---|---|---|
| 기존 녹음·이전 오디오 에셋 보존 | 통과 | 45개 원본 관련 파일 SHA-256 일치 |
| 파생 PCM 포맷과 클리핑 | 통과 | 20개 44.1 kHz/16-bit/mono, 4배 오버샘플링에서도 0 dBFS 미만 |
| 효과음 시작·종료 및 루프 연결 | 통과 | 일반 클립 0 샘플 끝점, 루프 이음 차이 허용치 .001 full-scale 이내 |
| 수정본 고역·중첩 오프라인 비교 | 통과 | `audio_analysis.json`, `pcm_verification.json` |
| Unity 컴파일·새 믹서 작성·프로필 연결 | 통과 | 루트 실행 `technical_validation.json`, `apply_current.json`, 수정 후 `runtime_snapshot.json`. 첫 Play의 Unity fake-null AudioSource 생성 결함은 명시적 null 검사로 수정 |
| 실제 증가량·Restore 제외·연속 수급량 보존 | 통과 | `contract_tests.json`의 서비스 검사. 실제 HUD 시각·실제 입력 전환과 구분 |
| 실제 먹 이벤트·HUD 구간·현재 붓끝 연결 | 통과 — 합성 연출 + 실제 서비스 이벤트 | `InkFeedback/ink_feedback.json`: 양수 이벤트 16회, 합계 0.159999847(약 0.16), Restore 이벤트 0회. 실제 GestureRig.EffectTip과 흐름 끝 오차 0m, HUD 0.34→약 0.50 구간 일치, 해제 정리·원상 복원 true, failures 빈 배열 |
| 실제 입력의 갈무리 시작·유지·해제 | 미검증 | 현재 상태 스냅샷만으로 전체 전환을 통과 처리하지 않음 |
| 정지·탑승·비활성·장면 종료 잔존 | 미검증 | 실제 전환 순회 필요 |
| 실제 Unity 믹서 출력 | 통과 | `UnityOutput/listener_output.json`: 48 kHz stereo 9.493초, 최대 게임 12+UI 4개, 피크 -11.663 dBFS, 클리핑 0, 음소거 교정·정리 통과 |
| 갈무리 반복음과 종료 출력 | 통과 | 실제 출력의 유지 구간에 10 ms 이상 무음 없음; 해제 약 0.279초 후 정리, 최종 무음 정확히 0. 진단 신호이며 실제 수급 입력과 구분 |
| 실제 장치 출력·DSP underrun·청취 품질 | 미검증 | Unity 믹서 출력 통과와 OS/스피커·청취는 구분 |
| 먹의 질감·가시성·HUD 색감 | 사용자 검토 | 실제 화면 캡처 후 판단 |

검토 페이지의 오디오는 직접 눌렀을 때만 재생한다. `REVIEW.html`은 편집 전후 오디오를 제공하며, 실제 게임 출력과 동일하다고 주장하지 않는다. 영상이나 새 빌드는 제작하지 않았다.

## 먹 흐름·HUD 실제 연결 진단

`InkFeedback/ink_feedback.json`의 최종 재검사인 2026-09-14 02:21:40 UTC 기록은 `PASS_SYNTHETIC_PRESENTATION_AND_REAL_GAIN_EVENTS`다. 붓털 고정점 수정 후 진단용 발생점을 사용해 현재 실제 `GestureRig.EffectTip`, 생산용 `HarvestInkFlowRenderer`와 프로필, 실제 `InkPool.Gain` 및 기존 HUD 이벤트 구독을 함께 실행했다. 16개의 양수 증가 이벤트가 총 약 0.16을 더했고, HUD는 기존 먹 0.34에서 약 0.50까지 받은 구간만 표시했다. Restore는 증가 이벤트를 보내지 않았다. 실제 붓끝과 줄기 끝의 최대 거리 오차는 0m였고 `actualSinkBinding`, `receivedSegmentCorrect`, `releaseCleared`, `restored`가 모두 true다. 오류 배열은 비어 있다.

1920×1080 수급 전·수급 중·해제 후 스크린샷은 [먹 흐름 검토 페이지](InkFeedback/REVIEW.html)에 있다. 이 검사는 실제 적·명중·피해 이벤트를 발생시키지 않았다. 실제 키 입력의 갈무리 시작/유지/해제, 적에게서 먹을 얻는 전체 경로, 수급 경제 밸런스, 플레이어 갈무리 애니메이션 및 최종 −162° 손 프로필 접촉 검증은 이 진단의 합격 범위에 포함하지 않는다.

추가 출력 검수는 `Oheangbu.EditorTools.WorldMacro.PlaytestAudioInkOutputReview.Execute("begin" / "poll" / "abort")`로 명시적으로 실행한다. 격리 저장 슬롯에서 게임을 정지하고 현재 실제 AudioMixer와 같은 VoicePool을 이용해 음소거 교정, 금 효과음 단독, 최대 12개 게임+4개 UI 음성, 갈무리 유지·종료를 순차적으로 재생한다. `OnAudioFilterRead`는 현재 리스너의 PCM을 변경 없이 복사한다. 음소거/복원 교정을 통과해야 믹서 이후 출력으로 인정하며, 콜백이 없을 때 소스 PCM이나 프레임별 샘플을 실제 믹서 출력으로 대체하지 않는다. 출력 WAV와 피크·샘플 변화·무음·콜백 간격은 `UnityOutput`에 기록한다. 이 검사는 소리 연출 신호이며 실제 적 피해나 먹 수급을 실행하지 않는다.

루트 에이전트가 실행한 실제 출력은 445개의 1024프레임 콜백으로 9.493333초를 기록했으며, DSP 경과 시간과 샘플 길이가 일치했다. 최대 콜백 간격 40.047 ms는 벽시계의 스케줄링 간격이다. 이 수치를 곧바로 오디오 드롭이나 장치 underrun으로 해석하지 않는다. 유지음의 조용한 영점 구간은 최대 0.958 ms였으며 10 ms 이상 중단 구간은 없었다. 실제 출력의 4배 오버샘플링 피크도 -11.663 dBFS로 확인됐다. `Tools/PlaytestPolish/AudioInk/summarize_output.py`로 재분석할 수 있다.
