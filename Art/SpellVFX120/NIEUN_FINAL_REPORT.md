# ㄴ 계열 VFX 제작 결과

배정된 20종의 VFX 제작·개별 촬영·기술 검사를 마쳤다. 비주얼 판정은 사용자 검토 대기다. 눅·눔·눗·눙은 의도적 공백으로 유지했다.

[검토 페이지](http://127.0.0.1:8771/NIEUN_REVIEW.html)

## 구현

- 화염·불티·연기를 Particle System으로 분리했다. KTP 문양은 발동·영역·접촉 피드백에 사용한다.
- 놈의 소환수는 기존 Meshy 7 모델을 재사용했다. 신규 유료 생성은 없다. 정적 모델이며 AI·공격 애니메이션은 추가하지 않았다.
- 마지막 농은 화염 뒤 수증기, 누는 패링 없는 잔존 화벽, 눈은 명시적 경로를 따르는 비전투 연소 표현이다.

## 기술 확인

- 최신 공통 런타임에서 20종을 하나씩 실제 C2 Update/Destroy로 재생했다. 20종 통과, 오류 0. 씬·참조 자산·카메라 복귀 및 효과 계층 제거를 확인했다.
- 변경 프로필 20개가 배정 목록과 정확히 일치한다. 나머지 100개는 초기 해시와 동일하다. 공유 코드 전체의 게임 규칙 회귀를 의미하지 않는다.
- 개별 검토 페이지 20개, 1280×720·24fps 영상 40개. 각 제작 당시 촬영 DLL과 해당 단독 검사 DLL이 일치한다. 이전 영상은 최신 DLL 재촬영본이 아니며 최신 회귀 검사는 별도 파일로 보존했다.

## 미완료·미검증

- 실제 불처럼 보이는지와 색·밀도·문양 크기는 사용자 검토 대상이다.
- 검사 신호는 진단용이다. 기존 SpellBook에 없는 술식의 피해·상태·회복·소환 AI·덩굴 제거와 전체 전투 연결은 완료하지 않았다.
- 실제 필드 상호작용, 다중 동시 시전 GPU 비용, 목표 FPS 달성은 미검증이다. 유체 시뮬레이션이나 열 굴절을 구현한 결과가 아니다.

## 개별 결과

| 술식 | 제작 버전 | 보고서 |
|---|---:|---|
| 나 | 46 | [NA_025_REPORT.md](NA_025_REPORT.md) |
| 낙 | 50 | [NAK_026_REPORT.md](NAK_026_REPORT.md) |
| 난 | 48 | [NAN_027_REPORT.md](NAN_027_REPORT.md) |
| 남 | 49 | [NAM_028_REPORT.md](NAM_028_REPORT.md) |
| 낫 | 52 | [NAT_029_REPORT.md](NAT_029_REPORT.md) |
| 낭 | 53 | [NANG_030_REPORT.md](NANG_030_REPORT.md) |
| 너 | 54 | [NEO_031_REPORT.md](NEO_031_REPORT.md) |
| 넉 | 55 | [NEOK_032_REPORT.md](NEOK_032_REPORT.md) |
| 넌 | 56 | [NEON_033_REPORT.md](NEON_033_REPORT.md) |
| 넘 | 57 | [NEOM_034_REPORT.md](NEOM_034_REPORT.md) |
| 넛 | 58 | [NEOT_035_REPORT.md](NEOT_035_REPORT.md) |
| 넝 | 59 | [NEONG_036_REPORT.md](NEONG_036_REPORT.md) |
| 노 | 60 | [NO_037_REPORT.md](NO_037_REPORT.md) |
| 녹 | 61 | [NOK_038_REPORT.md](NOK_038_REPORT.md) |
| 논 | 62 | [NON_039_REPORT.md](NON_039_REPORT.md) |
| 놈 | 63 | [NOM_040_REPORT.md](NOM_040_REPORT.md) |
| 놋 | 64 | [NOT_041_REPORT.md](NOT_041_REPORT.md) |
| 농 | 65 | [NONG_042_REPORT.md](NONG_042_REPORT.md) |
| 누 | 66 | [NU_043_REPORT.md](NU_043_REPORT.md) |
| 눈 | 67 | [NUN_045_REPORT.md](NUN_045_REPORT.md) |

근거: `nieun_final_delivery.json`, `nieun_final_play_audit.json`, `NIEUN_PROGRESS.json`.
