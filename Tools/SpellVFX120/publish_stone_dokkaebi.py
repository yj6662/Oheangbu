"""Publish measured data and completed current diagnostic clips only."""
from pathlib import Path
import json,hashlib,subprocess,shutil,zipfile
R=Path(__file__).resolve().parents[2];O=R/'Art/SpellVFX120/StoneDokkaebi';A=R/'Oheangbu/Assets/_Project/Art/SpellVFX120/StoneDokkaebi'
def read(name):return json.loads((O/name).read_text())
m=read('model_report.json');u=read('unity_model.json');a=read('audit.json');capture=read('capture.json');before=read('scope_before.json');ledger=read('Meshy/ledger.json')
changes=[p for p,h in before.items() if not (R/p).exists() or hashlib.sha256((R/p).read_bytes()).hexdigest()!=h]
scope=dict(status='PASS' if changes==['Oheangbu/Assets/_Project/Art/SpellVFX120/Profiles/064_BAB8.asset'] else 'FAIL',checkedFiles=len(before),changed=changes)
(O/'scope_after.json').write_text(json.dumps(scope,indent=2));assert scope['status']=='PASS',scope
ff=next((Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries').glob('*.exe'))
clips=[]
for c in capture['clips']:
 if c['frames']!=132 or not c['ended']:continue
 v=c['view'];folder=Path(c['folder']);video=O/(v+'.mp4')
 subprocess.run([str(ff),'-y','-v','error','-threads','2','-framerate','24','-i',str(folder/'%04d.jpg'),'-frames:v','132','-c:v','libx264','-threads','2','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(video)],check=True)
 subprocess.run([str(ff),'-v','error','-threads','2','-i',str(video),'-f','null','-'],check=True)
 for label,frame in [('formation',18),('hold',54),('dissolve',98)]:shutil.copy2(folder/f'{frame:04d}.jpg',O/f'{v}_{label}.jpg')
 clips.append(v)
complete=len(clips)==2 and capture['status']=='PASS_DIAGNOSTIC_VISUAL_PENDING'
memory=max([c['commitRatio'] for c in capture['clips']] or [0])*100
reg=read('regression.json') if (O/'regression.json').exists() else {'status':'UNVERIFIED'}
credits=sum(row.get('consumed_credits',0) for row in ledger['requests']);tasks='\n'.join(f"- {r['name']}: `{r['task_id']}`, {r['status']}, {r.get('consumed_credits')}크레딧" for r in ledger['requests'])
report=f'''# 몸 — 토 속성 바위 도깨비

2026-09-11. 정적 외형·등장 연출 검토. 리깅·보행·공격·AI·실제 전투 미연결.

## 새 외형과 출처

사용자가 선택한 “바위 도깨비 — 익살스러운 얼굴, 낮고 넓은 몸, 큰 주먹”을 기준으로 Meshy 7 Ultra에서 새 모델을 생성했다. 이전 반려된 갑옷 병사/장승 원형은 사용하지 않았다. 작성한 텍스트만 전송했으며 KTP 공급자 이미지·에셋은 업로드하지 않았다.

`ai_model=meshy-7`, Ultra preview, triangle target 12,000, 이어서 2K PBR refine. 요청 목표와 결과는 다르다. 실제 Meshy GLB {m['sourceActualTriangles']:,}tris → 최종 FBX {m['triangles']:,}tris → Unity {u['triangles']:,}tris. 재질 1개, Blender 정점 {m['vertices']:,}. 18,000tris 이내.

이번 생성 실제 비용 {credits}크레딧(원형25 + 텍스처10). 별도 기록한 35크레딧 한도 내이며 유료 재시도 없음. 이전 사용량과 분리했다.
{tasks}

Blender 백그라운드 작업본에서 체형 폭을 23% 늘리고, 높이1.72m 이상 뿔 영역의 세로 길이를34%로 압축했다. 손·손목 영역은 부드러운 영향 범위로 약1.58배 확대하고 끝부분을 손바닥 쪽으로 조금 접었다. 손은 완전히 닫힌 복싱 주먹이 아니라 엄지와 손가락 사이 틈이 남은 돌주먹 시안이다. 리깅 또는 실제 파지는 검사하지 않았다.

전체 높이 {m['dimensions'][2]:.2f}m / 폭 {m['dimensions'][0]:.2f}m / 앞뒤 {m['dimensions'][1]:.2f}m. 얼굴·몸을 동일하게 보존했다고 주장하지 않으며, before/after 렌더에 실제 형상 변화를 표시한다. 새 Meshy 원형과 수정 Blender 파일은 분리했다.

원형이 밝은 회백색으로 나와, 새 Meshy BaseColor·Normal을 바탕으로 Blender 절차 노이즈의 회갈색 돌 입자와 미세 요철을 원래 UV에 2K로 베이크했다. 재질 metallic0 / roughness.88, Unity에서는 별도 무광 URP 표면·법선 셰이더를 사용한다. 조명 모델이 다르므로 게임의 밝기는 C2 영상을 기준으로 판단한다. 원본 GLB SHA256 전후 동일: `{m['sourceHashBefore']}`.

## 등장 연출

몸에만 StoneDokkaebiPresentation을 활성화한다. 기존 프로필은 Baseline_Mom.asset에 보존했다. 전방4m 지면에 고정하며, 플레이어 이동에 따라다니지 않는다. 별도의 게임 Duration·SpellBook·전투 규칙은 변경하지 않는다.

- 0~0.35초 토 KTP Bottom 문양과 낮은 흙먼지·자갈.
- 0.2~1.2초 발에서 머리 순서로 표면이 드러난다. 몸을 실제 돌 조각으로 분해해 조립한 물리 모델은 아니며, 높이 기반 표면 표시와 입자를 조합한 시안이다.
- 1.2~3.8초 외형 유지. 가짜 보행·호흡 변형 없음.
- 3.8~4.6초 위부터 부스러지는 표면 소멸과 짧은 돌조각·먼지. 문양이 마지막에 잦아든다.

문양 목표 지름3.4m. 실제 Particle System 입자 크기로 보정했다. 전역 노출·블룸은 그대로 유지한다. 두 발 최저점 기준으로 하단35cm만 제한적으로 접지 보정하며, 지면 높이 차15cm 초과나 지면 없음은 표시하지 않는다. 이는 보행 리그가 아니며 모든 언덕에 대한 해법으로 보장하지 않는다.

## 검사 결과

| 항목 | 결과 | 범위 |
|---|---|---|
| FBX 왕복 임포트 | 통과 | 빈 Blender 장면에 재임포트, 실제 삼각형 수·크기 동일 |
| Unity 임포트·셰이더 | 통과 | 12,547tris, 별도 프리팹·재질 생성, 셰이더 오류 검사 |
| 시간·위치·정리 | {a['status']} | {len(a['passed'])}개 수치 검사, 30/60/120fps × 정상/0.2배 시간. 지면·수명·고정 위치·종료 정리·정적 몸·Collider/Animator 없음 |
| 두 발 앵커 | 통과 | 평지 최대 간격 {a['maxFootGap']*1000:.2f}mm. 실제 발 메시2.5cm 이내도 검사 |
| 기존 에셋 보존 | {scope['status']} | {scope['checkedFiles']}개 해시, 변경은 몸 프로필 하나. 다른119프로필·곰·놈·솜·기존 Meshy 에셋 보존 |
| 곰·놈·솜 회귀 | {reg['status']} | regression.json에 각 검사 결과 |
| C2 영상 | {capture['status']} | 완료{len(clips)}개. 각각1920×1080 /24fps /5.5초. VFX 직접 재생 진단, 실제 입력·적 AI 전투 영상 아님 |
| 최종 외형·한국적인 인상 | 사용자 검토 대기 | 익살스러운 표정·주먹 크기·돌 색감 |
| 리깅·AI·전투·다중 소환 성능 | 미검증 | 이번 범위 제외 |
| 모든 경사·카메라 조합 | 미검증 | 이번 수치 검사는 평지, 영상은 현재 C2 배치 |

촬영 최고 시스템 커밋 {memory:.1f}%. 한 클립씩 처리하고24프레임마다 임시 카메라·RT·모델을 해제한 뒤 동일 시드와 효과 경과 시간으로 복원한다.85% 도달 시 중단한다. 이는 진단 촬영 도구 동작이며 런타임 효과를 반복 생성하는 최적화가 아니다.

## 전달물

StoneDokkaebi_Working.blend(텍스처 포함), FBX, Unity 모델·재질·프리팹, 새 Meshy 원형·텍스처·요청 기록, before/after 및 얼굴 렌더, C2 진단 영상과 정지 이미지, 모델/왕복/시간/보존 검사 JSON. 옴은 이번 작업에 포함하지 않았다.
'''
(O/'REPORT.md').write_text(report,encoding='utf-8')
videos=''.join(f'<section><h2>{"C2 플레이 시점" if v=="play" else "C2 외부 시점"}</h2><video controls loop preload="metadata" poster="StoneDokkaebi/{v}_hold.jpg" src="StoneDokkaebi/{v}.mp4"></video></section>' for v in clips)
stills=''.join(f'<section><h3>{label}</h3><img src="StoneDokkaebi/external_{key}.jpg"></section>' for key,label in [('formation','형성'),('hold','유지'),('dissolve','소멸')]) if 'external' in clips else ''
html=f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>몸 · 바위 도깨비</title><style>body{{max-width:1650px;margin:auto;padding:28px;background:#22201c;color:#e8e4dc;font:17px/1.65 system-ui}}a{{color:#dac59d}}.pair,.stills{{display:grid;grid-template-columns:1fr 1fr;gap:18px}}.stills{{grid-template-columns:repeat(3,1fr)}}img,video{{width:100%;background:#333}}.note{{padding:16px;background:#373128}}@media(max-width:800px){{.pair,.stills{{grid-template-columns:1fr}}}}</style>
<h1>몸 · 바위 도깨비</h1><p>익살스러운 얼굴 · 낮고 넓은 몸 · 큰 돌주먹 · 토 문양과 흙먼지</p><p class="note"><b>정적 외형·등장 연출 검토 / 리깅·전투 미연결</b><br>{'C2 1080p 진단 영상2개. 실제 입력·적 AI 영상이 아닙니다.' if complete else 'C2 촬영 미완료. 확보한 자료만 표시합니다.'}</p>
<div class="pair"><section><h2>새 Meshy 7 원형</h2><img src="StoneDokkaebi/model_before.png"></section><section><h2>Blender 수정판</h2><img src="StoneDokkaebi/model_after.png"></section>{videos}</div><div class="stills">{stills}</div>
<p>{m['triangles']:,}삼각형 · 재질1개 · 높이{m['dimensions'][2]:.2f}m / 폭{m['dimensions'][0]:.2f}m · Meshy7 실제{credits}크레딧</p><p>작은 뿔과 확대된 돌주먹. 손가락 사이 틈은 남겨 둔 시안입니다. 표정·크기·색감은 직접 검토해 주세요.</p>
<p>발부터 형성 → 3.8초까지 유지 → 4.6초까지 돌조각·먼지로 소멸. 수명은 검토용 수치입니다.</p>
<p><a href="StoneDokkaebi/face_detail.png">얼굴 확대</a> · <a href="StoneDokkaebi/REPORT.md">변경·검증 보고서</a> · <a href="StoneDokkaebi/StoneDokkaebi_Delivery.zip">Blender·FBX·텍스처·원본 다운로드</a> · <a href="METAL_TIGER_REVIEW.html">솜</a> · <a href="FIRE_HAETAE_REVIEW.html">놈</a> · <a href="WOOD_DEER_REVIEW.html?v=necklace1">곰</a></p></html>'''
(O.parent/'STONE_DOKKAEBI_REVIEW.html').write_text(html,encoding='utf-8')
with zipfile.ZipFile(O/'StoneDokkaebi_Delivery.zip','w',zipfile.ZIP_DEFLATED,compresslevel=2) as z:
 for p in A.rglob('*'):
  if p.is_file():z.write(p,'Unity/Assets/_Project/Art/SpellVFX120/StoneDokkaebi/'+p.relative_to(A).as_posix())
 for name in ['StoneDokkaebi_Working.blend','REPORT.md','model_report.json','fbx_roundtrip.json','unity_model.json','audit.json','scope_after.json','capture.json','model_before.png','model_after.png','face_detail.png','regression.json']:
  if (O/name).exists():z.write(O/name,name)
 for p in (O/'Meshy').rglob('*'):
  if p.is_file() and '.private' not in p.parts and p.suffix in ['.json','.glb','.fbx','.png']:z.write(p,p.relative_to(O).as_posix())
with zipfile.ZipFile(O/'StoneDokkaebi_Delivery.zip') as z:assert z.testzip() is None
print(json.dumps(dict(complete=complete,clips=clips,scope=scope['status'],audit=a['status'],credits=credits)))
