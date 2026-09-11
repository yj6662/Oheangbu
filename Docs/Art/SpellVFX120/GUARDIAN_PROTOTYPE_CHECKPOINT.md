# 장승 부품·동작 시제품 체크포인트

상태: **형상 왕복 검사 통과 / 타격 자세 실패 / 제작 중**.

기존 `StoneGuardian_Jangseung_B1`의 44개 연결 요소를 골반·몸통·머리·좌우 팔·주먹·다리·발 11개 강체 부품으로 나눴다. 원본 형상과 총 2,720 tris를 유지했으며 새 Meshy 요청은 없다. 단일 정적 메시도 보존했다.

## 형상과 연결

- Blender 빈 장면의 FBX 재임포트: 11개 메시·2,720 tris, 정지 형상 최대 양방향 오차 `6.9992e-08 m`. [결과](../../../Art/SpellVFX120/Blender/GuardianParts/blender_roundtrip.json).
- Unity 임포트: 11개 부품의 조립 형상 오차 `6.82857e-08 m`, native 정점 버퍼 모두 1. [기록](../../../Art/SpellVFX120/Blender/GuardianParts/unity_import.txt).
- `064_BAB8` 프로필에 부품 메시·피벗·우측 주먹 접점을 연결했다. `Vfx120GuardianMotion`은 수치로 부품 자세를 계산하며 피해·물리 규칙을 추가하지 않는다.
- 재생성 스크립트는 `Tools/SpellVFX120/build_guardian_parts.py`, Unity 재임포트는 `Vfx120Editor.ImportGuardianParts()`다. `Art/SpellVFX120/Blender/GuardianParts`의 manifest와 B2 FBX가 필요하다.
- 편집 작업본 `GuardianParts_Working.blend`는 해당 로컬 폴더에 보존했다. 이 체크포인트에는 FBX·부품 메시·제작 스크립트·manifest를 포함한다.

## 실제 화면과 미완료

- [시제품 영상](../../../Art/SpellVFX120/GuardianClips/064_BAB8.mp4): 1280×720·24fps·116프레임. 파일 단위 인코딩·디코딩·시간 검증 통과이며 최종 미술 승인이 아니다.
- `GuardianFrames/064_BAB8`의 0010·0044·0062·0090 표본에서 조립·접근·타격·복귀를 확인했다. 타격 표본에서는 **머리 위로 뜨는 블록과 어색한 팔 자세가 남아 있다**.
- 데모는 수명 55% 부근의 예시 타격 시계를 사용한다. 실제 게임 타격 이벤트와의 연결·주먹 접촉·피해 판정은 미검증이다.
- `Vfx120GuardianAudit.Run()`은 실제 부품 좌표와 접점 오차를 기록하도록 작성했고 컴파일만 확인했다. 이 체크포인트에서는 미실행이다.
- 관절 교차·표면 접촉, UV/색상 왕복 동일성, 최종 런타임·성능은 미검증이다.
- 전체 카탈로그용 인코더를 이 단일 영상 폴더에 사용한 실행은 119개 누락과 종합 보고서 저장 오류로 실패했다. 위 단일 파일의 검증 결과와 구분하며, 잘못된 종합 보고서는 PR에 포함하지 않는다.

[단일 영상 검증](../../../Art/SpellVFX120/GuardianClips/064_BAB8_encoding.json), [촬영 메타데이터](../../../Art/SpellVFX120/GuardianFrames/064_BAB8_capture.json).
