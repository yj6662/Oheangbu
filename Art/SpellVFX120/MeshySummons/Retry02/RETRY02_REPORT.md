# Meshy 소환수 2종 재시도 결과

2026-09-09. **장승과 수룡의 새 미리보기 모두 형상 반려**. 실제 썸네일을 각각 열고 기존 실패 결과와 비교했다. 텍스처 생성, 추가 재시도, Blender 정리, Unity 적용은 진행하지 않았다. 이 결과를 승인 모델로 사용하지 않는다.

## 요청과 실제 비용

두 요청은 `meshy-7`, `model_type=standard`, `ultra_mode=true`, `should_remesh=true`, `topology=triangle`, `target_polycount=12000`, `target_formats=[glb,fbx]`였다. 직접 작성한 텍스트만 전송했으며 KTP 이미지·유료 에셋을 업로드하지 않았다. 공식 가격은 Ultra 미리보기 25, 2K/4K refine 10크레딧이다. [Meshy API 요금표](https://docs.meshy.ai/en/api/pricing), [Text to 3D 규격](https://docs.meshy.ai/en/api/text-to-3d).

재시도 배치의 자체 운영 상한은 70, 이전 배치와 합친 자체 상한은 225였다. 이를 사용자가 지정한 예산이라고 기록하지 않는다. 이번 실제 소비는 **25×2=50**, API 관측 잔액은 **413→363**이다. 앞선 실제 소비 155와 합계 **205크레딧**이다. 반려 후 잔여 20을 사용하지 않았다.

| 대상 | 새 작업 ID | 요청 polycount | 실제 GLB tris | GLB 정점 | 실제 소비 |
|---|---|---:|---:|---:|---:|
| 몸 / 064_BAB8 | `01a084bf-fdf3-74ec-9c17-873395d5a900` | 12,000 | 12,391 | 14,605 | 25 |
| 옴 / 112_C634 | `01a084c0-074d-7018-8d9c-3f7fd267702a` | 12,000 | 12,549 | 16,571 | 25 |

실제 수치는 내려받은 GLB의 triangle primitive accessor를 읽은 값이다. 두 파일 모두 메시 1개·텍스처 재질 0개인 미리보기다. Blender의 FBX 재임포트 수치나 Unity 런타임 삼각형 수로 간주하지 않는다.

## 실제 형상 판정

**064 장승 — 실패.** 이전의 중세 갑옷 골렘과 달리 넓고 과장된 귀면은 나타났다. 그러나 긴 장방형 얼굴과 그 아래로 이어지는 단순한 돌기둥 몸이 아니다. 흉부의 단추형 돌기, 허리띠·치마, 손목띠와 장화 같은 복식 구조가 남아 귀면 병사 또는 완구에 가까운 실루엣이다. 팔·손·다리의 분리는 보여도 이번에 제거하려던 복식과 장식이 그대로이므로 refine 승인 조건을 충족하지 못한다. 표면에 문자나 태극을 붙여 장승으로 위장하지 않았다.

![반려된 장승 재시도 미리보기](C:/Users/yj666/Oheangbu/Art/SpellVFX120/MeshySummons/Retry02/064_BAB8/preview/thumbnail_url.png)

**112 수룡 — 실패.** 앞선 세운 목과 두 발 지지 형태가 반복됐다. 머리를 낮게 뻗은 수평 S형 몸 대신 가슴에서 목이 거의 수직으로 올라가고 꼬리는 뒤에서 고리처럼 말린다. 썸네일에서 낮은 네 발을 확인할 수 없다. 보이지 않는 후면에 다리가 없다고 확정하지 않지만, 이미 보이는 목·몸 축만으로도 요구한 낮고 긴 이무기 실루엣에 맞지 않는다.

![반려된 수룡 재시도 미리보기](C:/Users/yj666/Oheangbu/Art/SpellVFX120/MeshySummons/Retry02/112_C634/preview/thumbnail_url.png)

## 보존 파일과 상태

- 신규 실행 도구: `Tools/MeshyRuns/SpellVFX120/run_retry02.py`. 요청 전에 예약을 저장하고, 기존 이름의 작업이 있으면 다시 제출하지 않는다. 두 형상 반려 후 `further_paid_requests_stopped=true`로 닫았다.
- 신규 원장: `ledger02.json`. 요청 프롬프트·설정·예상/실제 비용·작업 ID·관측 잔액·검토 이유를 기록했다.
- 결과: `{064_BAB8,112_C634}/preview/` 각각 GLB·FBX·썸네일·manifest. `delivery_manifest02.json`에 파일 크기와 SHA-256, 실제 GLB 통계를 모았다.
- API 원문 응답은 `.private/`에만 보존했다. 키·서명 다운로드 URL은 공개 원장/보고서에 넣지 않았고 `.gitignore`에서 `.private/`, `*.part`, `*.writing`을 제외했다.
- `preservation_before.json`과 `preservation_after.json`: 기존 `ledger.json`, 기존 3종 `MeshySummons_Source.blend`, 앞선 `Source/delivery_manifest.json`의 작업 전후 SHA-256이 모두 같다. 기존 Blender 파일은 dirty=false 상태임을 읽기 전용으로 확인했으며 수정·저장하지 않았다.

| 검사 | 상태 | 근거 |
|---|---|---|
| Meshy 7 실제 생성·파일 수령 | 통과 | 두 작업 SUCCEEDED, 각 preview GLB/FBX/PNG와 해시 기록 |
| 원본 배치·기존 3종 보존 | 통과 | 기록한 세 원본의 SHA-256 동일 |
| 장승의 기둥형 얼굴·무복식 몸 | 실패 | 실제 썸네일에 복식과 넓은 귀면이 남음 |
| 수룡의 낮은 수평 S 몸 | 실패 | 실제 썸네일에서 목을 세운 몸 축 반복 |
| 새 2종 PBR 생성 | 미실행 | 형상 반려로 유료 refine 요청하지 않음 |
| Blender 정리·크기 정규화·FBX 왕복 | 미검증 | 승인 모델이 없어 실행하지 않음 |
| 리그·애니메이션·Unity 적용 | 미검증 | 이번 재시도에서 실행하지 않음 |

썸네일은 앞 사선 한 시점이다. 후면 토폴로지, 내부 면, 관절 변형을 통과했다고 보고하지 않는다. 현재 완료한 것은 두 번의 제한된 재생성, 실제 형상 반려 판단, 비용과 원본의 보존이다.
