# Meshy 7 소환수 생성 원본 전달

2026-09-09. **형상 5개 생성, 그중 사슴·해태·호랑이 3개만 2K PBR 텍스처까지 완료했다.** 돌거인과 이무기는 실제 미리보기에서 방향과 어긋나 반려했고 원본은 보존했다. 추가 유료 요청은 중지했다.

## 생성 설정·비용

API에 `ai_model="meshy-7"`, `mode="preview"`, `model_type="standard"`, `ultra_mode=true`, `should_remesh=true`, `topology="triangle"`, `target_polycount=12000`, `target_formats=["glb","fbx"]`를 명시했다. 적합한 형상만 기존 preview ID를 받아 `mode="refine"`, `ai_model="meshy-7"`, `texture_resolution="2k"`, `enable_pbr=true`로 진행했다.

[Meshy Text to 3D 공식 규격](https://docs.meshy.ai/en/api/text-to-3d)과 [공식 요금표](https://docs.meshy.ai/en/api/pricing)를 2026-09-09 확인했다. 실제 응답의 사용량은 **preview 25×5 + refine 10×3 = 155크레딧**이다. 이번 작업자가 설정한 보수적인 실행 상한은 175였고, 사용자가 직접 설정한 한도로 표현하지 않는다. API 잔액 관측은 568→413이다.

유료 POST 전에 원장에 예약을 기록했으며 자동 재시도는 없었다. 전송한 것은 직접 작성한 텍스트뿐이다. KTP 등 유료 에셋 이미지·파일은 외부에 전송하지 않았다. 키와 서명 URL은 공개 원장·보고서에 포함하지 않았다. 원응답은 Git 제외된 `../.private`에만 있다.

## 실제 원본 통계와 판정

아래 실제 tris는 다운로드한 **GLB의 primitive index accessor**에서 계산했다. 요청 숫자를 실제 모델 수치로 대신 쓰지 않았다. 정점 수는 UV/노멀 경계 중복을 포함할 수 있다. FBX의 Unity 왕복 임포트 수치는 아직 측정하지 않았다.

| ID·효과 | 형상 | 요청 tris | 실제 GLB tris | 정점 | 결과 |
|---|---|---:|---:|---:|---|
| 016_ACF0 곰 | 뿌리 수호사슴 | 12,000 | 12,543 | 15,147 | 텍스처 완료, 뿌리 표현/재질 보완 필요 |
| 040_B188 놈 | 불 해태 | 12,000 | 12,532 | 15,885 | 텍스처 완료, 자세/광택 보완 필요 |
| 064_BAB8 몸 | 돌 장승 거인 | 12,000 | 12,473 | 15,470 | 형상 반려, preview 보존 |
| 088_C19C 솜 | 철 민화호랑이 | 12,000 | 12,414 | 18,138 | 텍스처 완료, 광택/색조 보완 필요 |
| 112_C634 옴 | 수묵 이무기 | 12,000 | 12,528 | 17,785 | 형상 반려, preview 보존 |

텍스처를 완료한 세 모델은 **총 37,489 tris**, 각각 GLB mesh 1개·material 1개·skin 0개·animation 0개다. 아직 리깅된 모델이나 게임에 적용된 소환수가 아니다. 세 모델의 preview와 refine GLB에서 삼각형 수가 동일함을 확인했다.

## 미리보기에서 실제 확인한 것

### 곰 — 사슴 골격과 뿔은 사용 가능, 뿌리 몸통은 미완성

완전한 사슴 외곽, 가지뿔, 귀, 굽이 있고 대좌가 없다. 목에는 섬유 같은 형상이 있으나 몸통은 얽힌 뿌리보다 매끈한 사슴 몸이다. refine 미리보기와 실제 base-color를 열었으며, 텍스처는 갈색 나무보다 **회백색**으로 나왔다. 따라서 형상 확보와 텍스처 파일 생성은 완료됐지만 뿌리 수호수 미술까지 통과하지 않았다.

![곰 텍스처 미리보기](C:/Users/yj666/Oheangbu/Art/SpellVFX120/MeshySummons/Source/016_ACF0/refine/thumbnail_url.png)

### 놈 — 해태형 얼굴과 불꽃 갈기는 읽힘

넓은 주둥이·굽은 눈썹·두꺼운 발·입체적인 불꽃 갈기가 확인된다. 검은 몸과 주황 갈기가 구분되지만 목표보다 광택과 주황 밝기가 강하다. 앞발 하나를 든 자세이고 가슴의 둥근 장식 형상도 남았다. 숨겨진 뒤쪽 다리와 밑면의 분리, 리깅 적합성은 Blender에서 확인해야 한다.

![놈 텍스처 미리보기](C:/Users/yj666/Oheangbu/Art/SpellVFX120/MeshySummons/Source/040_B188/refine/thumbnail_url.png)

### 솜 — 호랑이와 철재 표현은 읽힘

네 다리·둥근 발·곡선 등·말린 꼬리가 있고 별도 갑옷은 없다. 짙은 철색과 줄무늬 홈이 구분된다. 얼굴은 민화의 강한 과장보다 자연적인 호랑이 비례에 가깝고, 입 주위의 따뜻한 밝은 색과 매끈한 반사가 남는다. 거칠기와 색조를 낮추는 현지 재질 정리가 필요하다.

![솜 텍스처 미리보기](C:/Users/yj666/Oheangbu/Art/SpellVFX120/MeshySummons/Source/088_C19C/refine/thumbnail_url.png)

### 반려한 두 형상

- **몸:** 장방형 얼굴은 있으나 어깨갑옷·완갑·치마형 허리장식·문양 버클이 생겼다. 문자가 없는 자연스러운 석장승 거인 지시와 맞지 않아 refine하지 않았다. 버클 무늬의 실제 문자 종류를 확정한 것은 아니다.
- **옴:** 수평으로 긴 S형 이무기 대신 세로 몸통과 긴 목, 큰 뒷다리의 서양 용에 가까운 형태가 생성됐다. 얇은 가시와 팔처럼 보이는 앞다리도 요청과 어긋나 refine하지 않았다.

반려 모델의 `preview/model_urls_glb.glb`, FBX, thumbnail은 삭제하지 않았다. 새 재시도 요청도 하지 않았다.

## Blender 인계 파일

이 보고서가 있는 `Source`를 기준으로 한다.

- 사용할 1차 후보: `016_ACF0/refine`, `040_B188/refine`, `088_C19C/refine`.
- 각 폴더의 `model_urls_glb.glb`, `model_urls_fbx.fbx`가 원본 모델이다.
- 각 폴더에 `texture_urls_0_base_color.png`, `_normal.png`, `_metallic.png`, `_roughness.png`가 있다. **모두 실제 2048×2048**이다. base-color/normal은 RGB, metallic/roughness는 단일 채널 L이다. Meshy 7 결과에 emission 맵은 없다.
- 각 `manifest.json`에 작업 ID·설정·실제 사용량·파일 크기·SHA-256·GLB 통계·텍스처 크기를 기록했다.
- `delivery_manifest.json`에 다섯 모델 전체의 preview/refine ID, 실제 통계, 반려 이유와 현재 원본 경로를 모았다. 전체 요청 원장은 상위 `ledger.json`이다.
- 요청 재개/상태 확인 코드는 `Tools/MeshyRuns/SpellVFX120/run.py`다. 원장의 `further_paid_requests_stopped=true`로 신규 유료 POST를 막았고 기존 결과 조회는 가능하다.

## 검증 범위

실제 미리보기 5개와 refined 미리보기 3개를 직접 열었다. 사슴 base-color도 추가로 확인했다. GLB/FBX 파일 다운로드·해시 기록·GLB 기하 통계와 텍스처 크기 확인은 완료했다.

**Blender 전체 회전 검수, 숨겨진 면/관통, 리깅, Unity 임포트·재질 연결·실제 효과 적용·게임 성능은 미검증**이다. 이번 실행에서 Blender와 Unity는 조작하지 않았다. 세 후보를 최종 미술 PASS로 선언하지 않으며, 반려 두 모델은 제작 완료로 집계하지 않는다.
