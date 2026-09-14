# 효과음 재점검 — 프레임에 묶인 페이드 제거

현재 씬은 이미 편집된 PCM 음원과 전용 믹서를 사용하고 있었다. 원본이 다시 연결되거나 손실 압축이 재적용된 상태는 아니었다. 기존 실제 Unity 출력 기록은 최고 -11.66 dBFS, full-scale 초과 샘플 0개였다. 따라서 모든 소리가 깨지는 원인을 디지털 클리핑 하나로 단정하지 않았다.

## 실제로 확인한 문제와 수정

`WorldMacroAudioVoicePool.Tick()`은 DSP 시간을 읽었지만 **음량 변경 자체는 렌더 Update에서만** 실행했다. 18 ms attack, 25 ms release를 10–20 fps에서 평가하면 음량이 부드럽게 연결되지 않고 한 프레임에 크게 뛰거나 바로 끊길 수 있었다. 과거의 정지·정상 프레임 출력 검사로 이 조건을 확인하지 못했다.

이를 음성별 `WorldMacroAudioEnvelope`의 `OnAudioFilterRead` 처리로 옮겼다. attack·자연 종료·중도 release·교체 시 fade는 오디오 샘플 시간으로 진행하며, 메인 스레드는 완전히 조용해진 voice만 정리한다. UI와 게임 소리가 같은 경로를 사용한다. 12개 게임 voice·4개 UI voice, 역할별 중복 억제, 갈무리 시작/루프/종료, 사용자 믹서 옵션과 원본 음원은 유지했다.

오디오 콜백에서는 파일 접근·로그·Unity API·lock·동적 배열 생성 없이 미리 만든 버퍼를 처리한다. 프레임이 지연되어도 진행 중인 페이드가 0→최대 음량으로 뛰지 않는다. 이 변경은 OS 드라이버 버퍼 부족이나 실제 스피커/헤드셋의 문제를 판정하거나 해결했다고 뜻하지 않는다.

## 검사

| 항목 | 상태 | 근거 |
|---|---|---|
| 현재 씬의 편집 PCM 프로필 참조 | 통과 | AudioFeedback.asset GUID와 씬 참조 일치 |
| 20개 PCM 형식·4배 오버샘플 peak·끝점·루프 이음 | 통과 | `pcm_verification.json` |
| 원본 45개 해시 보존 | 통과 | 같은 파일 |
| 실제 새 DSP processor의 8개 계약 검사 | 통과 | `envelope_contracts.json` |
| 256/1024 frame 오디오 버퍼에서 동일한 엔벨로프 | 통과 | 샘플 차이 0 |
| 메인 Tick 없이 25 ms release 종료 | 통과 | 약 100 ms짜리 버퍼 안에서 종료, 남은 부분 0 |
| 프레임식 attack과 비교 | 통과 | 테스트 신호의 종전 최대 단차 0.75, 새 단차 약 0.0013 미만. 실제 음원 청취 측정 아님 |
| processor의 GC 할당 | 통과 | 16 voice × 1024 stereo frame 반복에서 0 byte; 오프라인 계산 비용만 계측 |
| 변경 후 Unity 소스·importer·믹서 연결 | 통과 | 활성 listener 1개, DSP envelope 소스 16개, 48kHz·1024×4 버퍼, PCM preload 및 믹서 연결. `runtime_audit.json` |
| 변경 후 Unity 믹서 합산, 10 Hz pool housekeeping | 통과 | 실제 listener PCM 9.493초/48kHz stereo, 최고 -11.6629 dBFS, full-scale 초과 0, mixer mute 교정 및 종료 정리 통과. `UnityOutput/listener_output.json` |
| 실제 전투 입력·장치 출력·최종 음색 | 미검증 | 사용자 장치 청취가 필요하며 이전 출력 검사로 대신하지 않음 |

## 재현 도구

- `dotnet run -c Release --project Tools/PlaytestRecovery/AudioRepair/AudioEnvelopeChecks.csproj` — production processor를 그대로 연결한 오프라인 검사.
- `Oheangbu.EditorTools.WorldMacro.PlaytestAudioRepairReview.Execute("audit")` — 현재 씬, 믹서, importer, Play voice 필터 읽기 전용 검사.
- `Oheangbu.EditorTools.WorldMacro.PlaytestAudioInkOutputReview.Execute("begin-lowfps")`, 이후 `poll` — 격리된 테스트 저장 슬롯의 idle Play에서 실행. 실제 voice pool의 메인 정리 Tick을 10 Hz로 제한하고 Unity listener PCM을 기록한다. 실제 장치 루프백이나 OS 오디오 스레드 정지 재현은 아니다.

기존 검토 자료와 소스는 `Before/` 및 이전 `Art/PlaytestPolish/AudioInk/`에 보존했다. 이번 수정으로 빌드를 생성하지 않았다.
