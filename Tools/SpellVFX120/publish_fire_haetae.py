"""Publish completed captures only, with scoped preservation and delivery evidence."""
from pathlib import Path
import json,hashlib,subprocess,zipfile,shutil
R=Path(__file__).resolve().parents[2];O=R/'Art/SpellVFX120/FireHaetae';A=R/'Oheangbu/Assets/_Project/Art/SpellVFX120/FireHaetae'
model=json.loads((O/'model_report.json').read_text());unity=json.loads((O/'unity_model.json').read_text());audit=json.loads((O/'audit.json').read_text());capture=json.loads((O/'capture.json').read_text())
before=json.loads((O/'scope_before.json').read_text());changed=[p for p,h in before.items() if not (R/p).exists() or hashlib.sha256((R/p).read_bytes()).hexdigest()!=h]
allowed=str(Path('Oheangbu/Assets/_Project/Art/SpellVFX120/Profiles/040_B188.asset'))
scope=dict(status='PASS' if changed==[allowed] else 'FAIL',changed=changed,checkedFiles=len(before),scope='All 120 profile files, WoodDeer assets and MeshySummons source assets before this task')
(O/'scope_after.json').write_text(json.dumps(scope,indent=2));assert scope['status']=='PASS',scope
ff=next((Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries').glob('*.exe'))
clips=[]
for clip in capture['clips']:
 if clip['frames']!=132 or not clip['ended']:continue
 folder=Path(clip['folder']);video=O/(clip['view']+'.mp4')
 subprocess.run([str(ff),'-y','-v','error','-threads','2','-framerate','24','-i',str(folder/'%04d.jpg'),'-frames:v','132','-c:v','libx264','-threads','2','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(video)],check=True)
 subprocess.run([str(ff),'-v','error','-threads','2','-i',str(video),'-f','null','-'],check=True)
 for label,frame in [('formation',18),('hold',54),('dissolve',98)]:shutil.copy2(folder/f'{frame:04d}.jpg',O/f'{clip["view"]}_{label}.jpg')
 clips.append(clip['view'])
complete=len(clips)==2 and capture['status']=='PASS_DIAGNOSTIC_VISUAL_PENDING'
report=f'''# 놈 — 화 속성 해태 외형·등장 연출

2026-09-11. 정적 외형·소환 연출 검토. 리깅·보행·공격·AI·실제 전투는 미연결.

## 변경

기존 Meshy 해태 040_B188의 얼굴·갈기·수염·몸·꼬리를 보존했다. 앞발 하나를 든 원본 자세를 유지한다. Blender 백그라운드에서 별도 작업본을 제작했다. 새 Meshy 생성/비용은 0이다. 푸른 회색 몸은 따뜻한 숯빛으로 바꾸고, 원본 주황 영역에서 불씨 마스크를 베이크했다. 갈기·수염·눈·문양에 제한적으로 주황 발광을 적용한다. 전신 화염이나 크기 애니메이션은 쓰지 않는다. 기존 모델과 곰은 보존했다.

- 실제 FBX {model['triangles']:,} tris / Unity {unity['triangles']:,} tris / 재질 1개.
- Blender 정점 {model['vertices']:,}. 원본 형상 최근접 위치 최대 오차 {model['maxBodyPositionErrorM']}m.
- 크기 {model['dimensions'][0]:.2f} × {model['dimensions'][1]:.2f} × {model['dimensions'][2]:.2f}m (Blender XYZ). 전체 높이 1.55m; 어깨 높이로 표기하지 않는다.
- BaseColor/EmberMask 2048×2048, 기존 Normal 사본. Body 원본 UV 유지.
- 원본 blend SHA256 작업 전후 동일: `{model['sourceHashBefore']}`.

## 연출

시전 위치 전방 4m에서 지면에 고정된다. 0~0.35초 화 KTP Bottom 사본과 발치 불티가 나타난다. 0.2~1.2초 발에서 갈기로 드러나고 형성 경계에 짧은 붉은 빛을 둔다. 1.2~3.8초 외형을 유지하며 갈기 주변에 소량의 불씨를 둔다. 3.8~4.6초 위에서 아래로 재·불티와 함께 소멸한다. 모든 표현은 전달받은 같은 경과 시간을 사용한다. 문양은 월드 지면에 있으며 전역 블룸·노출은 변경하지 않았다.

원본에서 접지한 세 발의 실제 최저 정점을 기준점으로 사용한다. 앞발을 내리는 모션은 만들지 않았다. 하단 35cm만 제한적으로 지면 보정하며 높이 차 15cm 초과나 지면 없음은 표시하지 않는다. 지형 전체에서 자연스러운 자세를 보장하는 시스템은 아니다.

`FireHaetaePresentation`을 놈 프로필만 활성화했다. 원본은 `Baseline_Nom.asset`으로 보존한다. 기존 `Duration`은 그대로이며 4.6초는 검토용 연출 수명이다. 다른 소환수와 게임 판정은 변경하지 않는다.

## 검사 결과

- 통과: Blender 빈 씬 FBX 왕복, 실제 삼각형 수, 원본 형상·원본 blend 보존.
- 통과: Unity 모델 임포트와 셰이더 오류 검사.
- {audit['status']}: 수치 검사 {len(audit['passed'])}개. 30/60/120fps × 1배/0.2배에서 시간·고정 중심·종료 후 표시/입자 정리·정적 몸 유지·게임 콜라이더/Animator 없음·접지점 확인. 최대 앵커 간격 {audit['maxFootGap']*1000:.2f}mm.
- {scope['status']}: 파일 해시 {scope['checkedFiles']}개 비교. 변경 프로필은 놈 하나. 다른 119 프로필, 곰 에셋, 기존 Meshy 에셋 보존.
- C2 촬영 상태: {capture['status']}. 완료 1080p 클립 {len(clips)}개. 진단용 효과를 직접 재생했으며 실제 입력·적 AI 전투 영상이 아니다. 최고 시스템 커밋 {max([c['commitRatio'] for c in capture['clips']] or [0])*100:.1f}%. 한 클립씩 촬영, 85% 이상 중단, 임시 리소스 정리.
- 미검증: 리깅·애니메이션·전투, 전체 지형/카메라 상황, 실게임 동시 시전 성능. 최종 비주얼 합격은 사용자 판단.

## 파일

FireHaetae_Working.blend, Unity/Assets/_Project/Art/SpellVFX120/FireHaetae의 FBX·재질·텍스처·프리팹, 전후 렌더, 영상·정지 이미지, 수치 검사와 원본 보존 보고서를 제공한다.
'''
report+='\n보완 검증: 기존 곰의 48개 수치 검사도 재통과했다. 문양 크기 보정 후 재촬영 중 한 차례 커밋 85.27%에서 자동 중단했다. 임시 리소스와 유휴 임포트 작업자를 정리하고 외부 클립만 재개하여 완료했으며, 중단 기록은 capture_memory_attempt.json에 보존했다.\n'
(O/'REPORT.md').write_text(report,encoding='utf-8')
videos=''.join(f'<section><h2>{"C2 플레이 시점" if v=="play" else "C2 외부 시점"}</h2><video controls loop preload="metadata" poster="FireHaetae/{v}_hold.jpg" src="FireHaetae/{v}.mp4"></video></section>' for v in clips)
stills=''.join(f'<section><h3>{label}</h3><img src="FireHaetae/external_{key}.jpg"></section>' for key,label in [('formation','형성'),('hold','유지'),('dissolve','소멸')]) if 'external' in clips else ''
html=f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>놈 · 불씨 해태</title><style>body{{max-width:1650px;margin:auto;padding:28px;background:#201c19;color:#eee6da;font:17px/1.65 system-ui}}a{{color:#efb879}}.pair,.stills{{display:grid;grid-template-columns:1fr 1fr;gap:18px}}.stills{{grid-template-columns:repeat(3,1fr)}}img,video{{width:100%;background:#333}}.note{{padding:16px;background:#3b3027}}@media(max-width:800px){{.pair,.stills{{grid-template-columns:1fr}}}}</style>
<h1>놈 · 불씨 해태</h1><p>숯빛 몸 · 갈기와 수염의 불씨 · 화 문양 위에서 형성되고 재로 소멸</p>
<p class="note"><b>정적 외형·등장 연출 검토 / 리깅·전투 미연결</b><br>{'C2 1080p 진단 영상 2개. 실제 입력·적 AI 영상이 아닙니다.' if complete else 'C2 촬영 미완료. 확보 자료만 표시합니다.'}</p>
<div class="pair"><section><h2>기존 후보</h2><img src="FireHaetae/model_before.png"></section><section><h2>화 속성 수정판</h2><img src="FireHaetae/model_after.png"></section>{videos}</div><div class="stills">{stills}</div>
<p>{model['triangles']:,}삼각형 · 재질 1개 · 전체 높이 1.55m · 추가 Meshy 비용 0 · 수치 검사 {len(audit['passed'])}개 통과</p>
<p>0~0.35초 바닥 문양 → 0.2~1.2초 불씨 경계로 형성 → 3.8초까지 유지 → 4.6초까지 재·불티로 소멸</p>
<p><a href="FireHaetae/REPORT.md">변경·검사 보고서</a> · <a href="FireHaetae/FireHaetae_Delivery.zip">Blender·FBX·텍스처·Unity 프리팹 다운로드</a> · <a href="WOOD_DEER_REVIEW.html?v=necklace1">곰 보기</a></p></html>'''
(O.parent/'FIRE_HAETAE_REVIEW.html').write_text(html,encoding='utf-8')
with zipfile.ZipFile(O/'FireHaetae_Delivery.zip','w',zipfile.ZIP_DEFLATED,compresslevel=2) as z:
 for p in A.rglob('*'):
  if p.is_file():z.write(p,'Unity/Assets/_Project/Art/SpellVFX120/FireHaetae/'+p.relative_to(A).as_posix())
 for name in ['FireHaetae_Working.blend','REPORT.md','model_report.json','fbx_roundtrip.json','unity_model.json','audit.json','scope_after.json','capture.json','capture_memory_attempt.json','model_before.png','model_after.png']:
  z.write(O/name,name)
with zipfile.ZipFile(O/'FireHaetae_Delivery.zip') as z:assert z.testzip() is None
print(json.dumps(dict(complete=complete,clips=clips,scope=scope['status'],audit=audit['status'])))
