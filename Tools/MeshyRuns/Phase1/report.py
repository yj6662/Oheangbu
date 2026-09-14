from pathlib import Path
import json,shutil,hashlib
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerPhase1'
stats=json.loads((OUT/'mesh_stats.json').read_text());audit=json.loads((OUT/'pose_audit.json').read_text());manifest=json.loads((OUT/'generation_manifest.json').read_text());unity=json.loads((OUT/'unity_import_audit.json').read_text())
audit['method']='Every integer frame (24fps source) of all 5 diagnostic actions, 186 total. Nearest body surface signed distance < -2mm, distance < 100mm. Candidate vertex counts include UV seam duplicates; this is not a full triangle-triangle proof.'
(OUT/'pose_audit.json').write_text(json.dumps(audit,indent=2))
stats['unity_import_triangles']=unity['triangles'];stats['fbx_roundtrip']=json.loads((OUT/'fbx_roundtrip.json').read_text());(OUT/'mesh_stats.json').write_text(json.dumps(stats,indent=2))
lines=[]
for label in ['Walk','Run','ArmsUp','Squat','BrushSwing']:
 rows=[r for r in audit['results'] if r['action']=='DIAG_'+label]
 inner=max(r['parts']['InnerTop']['penetration_candidate_vertices'] for r in rows);robe=max(r['parts']['Durumagi']['penetration_candidate_vertices'] for r in rows)
 depth=max(r['parts']['Durumagi']['max_candidate_depth_m'] for r in rows)*1000
 lines.append(f'| {label} | {len(rows)} | {inner} | {robe} | {depth:.2f}mm | '+('후보 0, 제한된 수치 검사 통과' if inner==robe==0 else '관통 후보 잔존, 실패/추가 수정 필요')+' |')
cost=sum(t.get('consumed_credits',0) for t in manifest['tasks'])
report='''# 오행부 플레이어 Phase1 검증 보고서

## 판정

**제작·내보내기·동작 검사 실행 완료. 품질 검수 최종 판정은 실패/보류이며 RIG_PASS가 아니다.**
몸·속상의·두루마기만 제작했다. 추가 의복·장식·붓 본체는 제작하지 않았다. 기존 C2와 공용 PlayerRig의 캡슐은 유지했다.
걷기·달리기 자락 관통과 천용 메시 구조 정리가 남아 있으므로 현재 결과를 최종 플레이어로 승격하지 않는다.

## 실제 산출물

- [편집 원본](Dosa_Phase1.blend): 별도 메시 3개, 공통 변형 골격 66본(기본24+손가락30+자락12), 검수용 액션5개.
- [FBX](Dosa_Phase1.fbx): 텍스처 포함, 검수용 액션5개. 생산용 이동/작도 애니메이션 승인본 아님.
- [동작 영상](Previews/Phase1_motion_validation.mp4): Walk → Run → ArmsUp → Squat → BrushSwing. 각 클립도 같은 폴더에 제공. 960×540, 12fps 영상. 검사 자체는 24fps의 모든 정수 프레임.
- Previews/Final_front.png, Final_back.png, Final_side.png, Final_Face.png, Final_Hand.png, Final_InnerTop.png.
- Textures/: 모델별 원래 UV에 대응하는 텍스처. Hyper3D/이전 모델 텍스처 혼용 없음.
- Source/Original, Source/Views, Source/Meshy: 원본 참조, 분리 이미지, 생성 원본/리메시 전 GLB/응답/리깅 원본 보존.
- generation_manifest.json, mesh_stats.json, fbx_roundtrip.json, unity_import_audit.json, topology_audit.json, pose_audit.json.
- Scripts/: 이번 작업 스크립트. Blender MCP의 단계별 피팅 보정도 아래에 기록. 원클릭 무상태 재빌드 보증은 하지 않는다.

## 폴리곤 요청과 실측

| 파츠 | Meshy 요청 | 최종 Blender/FBX 삼각형 |
|---|---:|---:|
| Body | quad 13,000 faces | 24,464 |
| InnerTop | quad 5,000 faces | 9,097 |
| Durumagi | 처음 quad 7,000 → 실패, 재생성 triangle 14,000 | 13,035 |
| 합계 | 요청값 합계를 최종 삼각형으로 간주하지 않음 | **46,596** |

60,000 상한 통과. 목표50,000보다 3,404개 적다. 장식 포함 향후80,000 목표까지 남은 예산33,404개. 현재 3개 재질 슬롯이며 실제 draw call 측정은 미검증.
FBX 재수입에서도 46,596개, 메시3개/골격1개. Unity 임포트 후46,589개로 7개 감소했으며 두 측정값을 별도로 기록한다. 제거된 7개 면의 개별 원인 추적은 미검증.

## 생성 및 수정 이력

모든 형상 생성은 `ai_model="meshy-7"`, 일반 품질, 2K PBR, 자동 이미지 스타일 변경 OFF로 실행했다.
몸은 정면·양 측면·후면 네 장. 의복 측면의 이웃 시점 겹침 때문에 옷은 정면·후면 두 장을 사용했다. 속상의 시트의 바지는 제외했다. 이미지 왜곡/좌우 반전 없음.

두루마기의 첫 quad 결과는821개 분리 조각으로 깨져 채택하지 않았다. 원래 고밀도 표면은 연속임을 비교한 뒤 triangle 방식으로 한 번 재생성했다. 이 결과의 UV/텍스처가 줄무늬로 실패해 fresh UV retexture 한 번을 실행했다. 재텍스처 결과의 정규화 스케일을 되돌려 정점 대응으로 피팅 좌표와 웨이트를 옮겼다.

Blender에서 몸1.75m·발바닥0·단위1m로 정리했다. 자동 리그의 과도한 본 표시 길이를 바로잡고 스케일을 적용했다. 양손 손가락 본30개와 자락 본12개를 추가했다. 속상의 소매 중심을 실제 팔에 맞추고, 두 옷에 공통 골격의 최대4 웨이트를 연결했다. 두루마기 하단에 팔/손/다리 웨이트가 없는 것을 수치 확인했다.

쪼그리기 자락 회전을 보완하고, 신체 표면 거리와 역 스키닝 변환을 이용해 옷의 국소 여유를 두 차례 보정했다. 전체 프레임에서 옷을 반복 밀어낸 후보는 자락 형태가 지나치게 변해 되돌렸다. 걷기·달리기에는 다리 자세에 반응하는 검수용 자락 회전을 추가했지만 잔존 관통이 있다. 이는 실시간 물리 시뮬레이션이 아니다.

## 실행한 검사

| 검사 | 결과 |
|---|---|
| 키1.75m/공통 골격/분리 메시 | 통과 |
| FBX 재수입 메시 수/삼각형 상한 | 통과 |
| 최대4 웨이트, 미할당0 | 통과. 최대 정규화 오차 약5.3e-8 |
| 유한 좌표 | 통과 |
| Unity Humanoid | 유효 Avatar/isHuman 확인, 5개 클립 확인 |
| Body 연결성 | 위치 용접 진단상 연결 성분1개. 46개 경계 에지의 개별 용도 분류는 미검증 |
| 의복 토폴로지 | 천용 정리 미완료. 위치 용접 진단에서 두루마기 비다양체 에지2,713개(경계 포함). 찢김/겹친 양면/잘못된 연결의 개별 분류 미완료 |
| 붓 모의 동작 | 빈손 휘두르기/손가락 본 회전 실행. 실제 붓 파지와 손가락 접촉 정밀도는 미검증 |

### 동작별 신체 관통 후보

아래는 부호 있는 최근접 거리 검사이며 완전한 삼각형 충돌 검사는 아니다. UV 경계의 중복 정점도 집계된다. 후보0도 옷끼리 관통·자가 교차·모든 관절 품질을 보증하지 않는다.

| 동작 | 검사 프레임 | 속상의 최대 후보 정점 | 두루마기 최대 후보 정점 | 두루마기 최대 후보 깊이 | 판정 |
|---|---:|---:|---:|---:|---|
'''+ '\n'.join(lines)+'''

## 미검증 및 남은 결함

- 걷기·달리기의 두루마기 관통, 쪼그리기의 소수 관통 후보: 실패/추가 수정 필요. 영상은 이를 숨기지 않은 현 상태다.
- 두루마기의 넓은 소매·자락 토폴로지는 전용 천 시뮬레이션에 적합하다고 승인할 수 없다. 자연스러운 두께·안감·모든 개구부 및 겹친 면 정리 미완료.
- Unity Cloth, 충돌 프록시, 중력 정착·폭발·프레임률별 안정성: 구현/검사하지 않았으므로 **미검증**.
- 옷끼리·자가 교차의 완전한 검사, 손목/손가락의 정밀 파지, 세부 관절 보정 및 모든 좌우 정지 포즈: **미검증**.
- 플레이 모드 발 미끄러짐·실시간 지면 접촉·실제 CPU/GPU 비용·카메라·작도 규칙 회귀: **미검증**. C2 적용 범위가 아니며 테스트 동작을 게임 모션으로 승인하지 않았다.

## 비용

이번 신규 작업 실제 합계 **'''+str(cost)+'''크레딧**. 이전 세션 비용과 분리했다. 요청별 예상/실제 비용·작업ID는 generation_manifest.json에 기록했다. 최초 형상3×30, 새 몸 리깅5, 두루마기 형상 재시도30, UV/텍스처 복구10.

API 규격: [Multi-image](https://docs.meshy.ai/en/api/multi-image-to-3d), [Rigging](https://docs.meshy.ai/en/api/rigging), [Retexture](https://docs.meshy.ai/en/api/retexture).
'''
(OUT/'validation_report.md').write_text(report,encoding='utf-8')
for category,folder in [('Blender',ROOT/'Tools/Blender/PlayerPhase1'),('Meshy',ROOT/'Tools/MeshyRuns/Phase1')]:
 dest=OUT/'Scripts'/category;dest.mkdir(parents=True,exist_ok=True)
 for p in folder.glob('*.py'):shutil.copy2(p,dest/p.name)
manifest['final_actual_credits']=cost;manifest['selected_tasks']={'Body':'Body','InnerTop':'InnerTop','Durumagi':'DurumagiRetry','DurumagiTexture':'DurumagiTextureRepair','Rig':'BodyRig'};manifest['quality_status']='FAILED_VALIDATION_NOT_RIG_PASS'
(OUT/'generation_manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
files=['Dosa_Phase1.blend','Dosa_Phase1.fbx','validation_report.md','mesh_stats.json','Previews/Phase1_motion_validation.mp4']
(OUT/'delivery_manifest.json').write_text(json.dumps([{'path':p,'bytes':(OUT/p).stat().st_size,'sha256':hashlib.sha256((OUT/p).read_bytes()).hexdigest()} for p in files],indent=2))
print('Report and delivery manifest written; validation NOT passed.')
