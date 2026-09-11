"""Only publish complete current clips and measured verification results."""
from pathlib import Path
import json,hashlib,subprocess,shutil,zipfile
R=Path(__file__).resolve().parents[2];O=R/'Art/SpellVFX120/MetalTiger';A=R/'Oheangbu/Assets/_Project/Art/SpellVFX120/MetalTiger'
def read(name):return json.loads((O/name).read_text())
m=read('model_report.json');u=read('unity_model.json');a=read('audit.json');capture=read('capture.json');before=read('scope_before.json')
changes=[p for p,h in before.items() if not (R/p).exists() or hashlib.sha256((R/p).read_bytes()).hexdigest()!=h]
scope=dict(status='PASS' if changes==['Oheangbu/Assets/_Project/Art/SpellVFX120/Profiles/088_C19C.asset'] else 'FAIL',checkedFiles=len(before),changed=changes)
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
report=f'''# 솜 — 금 속성 은철 호랑이

2026-09-11. 정적 외형·등장 연출 검토. 리깅·보행·공격·AI·실제 전투 미연결.

## 제작

기존 Meshy 088_C19C 호랑이의 얼굴·줄무늬·몸·꼬리·자세를 보존했다. Blender 백그라운드에서 별도 작업본에 재질을 만들었다. 추가 Meshy 생성과 비용은 0이다. 채도를 낮춘 BaseColor를 원본 UV에 베이크하고 기존 Normal을 사본으로 사용한다. Blender에서는 metallic .65 / roughness .43의 은철 재질을 사용한다. Unity에서는 별도의 표면 셰이더로 법선·시점·주광원에 따른 제한된 은빛 하이라이트를 표현한다. 두 렌더러의 조명 모델은 같지 않으므로 게임 내 결과는 C2 영상을 기준으로 판단한다.

- 실제 FBX {m['triangles']:,} tris, Unity {u['triangles']:,} tris, 재질 1개, Blender 정점 {m['vertices']:,}.
- 전체 높이 {m['dimensions'][2]:.2f}m, 폭 {m['dimensions'][0]:.2f}m, 꼬리 포함 앞뒤 길이 {m['dimensions'][1]:.2f}m. 어깨 높이 수치가 아니다.
- 정규화 원본 대비 몸 정점 위치 최대 오차 {m['maxBodyPositionErrorM']}m. 위치 중복 정점만 용접하고 노멀을 정리했다.
- 원본 blend SHA256 전후 동일: `{m['sourceHashBefore']}`.
- 기존 곰·놈과 원본 Meshy 파일은 수정하지 않았다.

## 표현

솜 프로필에만 MetalTigerPresentation을 활성화한다. Baseline_Som.asset에 기존 프로필을 보존했다. 게임 Duration과 SpellBook은 변경하지 않으며, 4.6초는 시각 검토용 수명이다.

플레이어 전방 4m 지면에 고정한다. 0~0.35초 금 KTP Bottom 문양과 소량의 금속 입자가 나타난다. 0.2~1.2초 발부터 은빛 윤곽을 따라 형성한다. 1.2~3.8초 정적 외형을 유지한다. 3.8~4.6초 위에서 아래로 얇은 금속 조각과 짧은 섬광으로 소멸한다. 문양의 목표 지름은 3.8m이며 실제 Particle System 입자 크기를 기준으로 맞춘다. 전신 확대·가짜 보행·상시 불티·전역 노출/블룸 변경은 없다.

실제 접지한 네 발의 최저 정점을 기준으로 하단 35cm를 제한적으로 지면 보정한다. 높이 차 15cm 초과나 지면 없음은 표시하지 않는다. 본격적인 경사 보행 또는 모든 지형을 처리하는 리그는 아니다.

## 검증

- 통과: Blender 빈 씬 FBX 왕복 삼각형 수, 몸 위치와 원본 blend 보존, Unity 임포트·셰이더 컴파일.
- {a['status']}: {len(a['passed'])}개 수치 검사. 30/60/120fps × 정상/0.2배 시간에서 지면·수명·고정 중심·종료 정리·정적 몸 유지·Collider/Animator 없음·네 발 접지. 최대 앵커 간격 {a['maxFootGap']*1000:.2f}mm.
- {scope['status']}: {scope['checkedFiles']}개 파일 해시 비교, 변경 프로필은 솜 하나. 다른 119 프로필·곰·놈·기존 Meshy 에셋 보존.
- 기존 소환수 회귀: {reg['status']}. 상세 regression.json 참고.
- C2 촬영: {capture['status']}. 완료 클립 {len(clips)}개, 각각 1920×1080/24fps/5.5초. 실제 C2 배경에서 효과를 직접 재생한 진단 영상이며 실제 입력·적 AI 전투 영상이 아니다.
- 성공/현재 촬영 기록의 최고 시스템 커밋 {memory:.1f}%. 플레이와 외부 클립을 분리한다. 초기 촬영은85% 한도로 중단되어 capture_memory_attempt.json에 보존했다. 촬영 도구에서24프레임마다 임시 카메라·RT·모델을 해제하고 같은 시드·경과 시간으로 복원하여 이어 찍는다. 이 변경은 촬영 도구에만 적용하며 게임 런타임에는 영향을 주지 않는다. 85% 도달 시 중단한다.
- 미검증: 리깅·애니메이션·전투, 모든 경사/카메라 상황, 다중 소환 실게임 성능. 최종 크기·광택·한국적인 인상은 사용자 판단.

## 전달

MetalTiger_Working.blend, FBX, 텍스처·Unity 재질/프리팹, 외형 전후 렌더, 완료된 진단 영상과 정지 이미지, 실제 메시 통계·수치 검사·원본 보존 보고서. 다음 몸·옴은 이번 작업에 포함하지 않았다.
'''
(O/'REPORT.md').write_text(report,encoding='utf-8')
videos=''.join(f'<section><h2>{"C2 플레이 시점" if v=="play" else "C2 외부 시점"}</h2><video controls loop preload="metadata" poster="MetalTiger/{v}_hold.jpg" src="MetalTiger/{v}.mp4"></video></section>' for v in clips)
stills=''.join(f'<section><h3>{label}</h3><img src="MetalTiger/external_{key}.jpg"></section>' for key,label in [('formation','형성'),('hold','유지'),('dissolve','소멸')]) if 'external' in clips else ''
html=f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>솜 · 은철 호랑이</title><style>body{{max-width:1650px;margin:auto;padding:28px;background:#1c2022;color:#e5e8e8;font:17px/1.65 system-ui}}a{{color:#b8d0d9}}.pair,.stills{{display:grid;grid-template-columns:1fr 1fr;gap:18px}}.stills{{grid-template-columns:repeat(3,1fr)}}img,video{{width:100%;background:#333}}.note{{padding:16px;background:#2e373c}}@media(max-width:800px){{.pair,.stills{{grid-template-columns:1fr}}}}</style>
<h1>솜 · 은철 호랑이</h1><p>검은 줄무늬 · 절제된 은빛 광택 · 금 문양과 얇은 금속 조각</p><p class="note"><b>정적 외형·등장 연출 검토 / 리깅·전투 미연결</b><br>{'C2 1080p 진단 영상 2개. 실제 입력·적 AI 영상이 아닙니다.' if complete else 'C2 촬영 미완료. 완료된 자료만 표시합니다.'}</p>
<div class="pair"><section><h2>기존 후보</h2><img src="MetalTiger/model_before.png"></section><section><h2>은철 외형 수정판</h2><img src="MetalTiger/model_after.png"></section>{videos}</div><div class="stills">{stills}</div>
<p>{m['triangles']:,}삼각형 · 재질 1개 · 전체 높이 1.40m · Meshy 추가 비용 0 · 수치 검사 {len(a['passed'])}개 통과</p>
<p>0.2~1.2초 형성 → 3.8초까지 유지 → 4.6초까지 금속 조각과 짧은 섬광으로 소멸</p>
<p><a href="MetalTiger/REPORT.md">변경·검증 보고서</a> · <a href="MetalTiger/MetalTiger_Delivery.zip">Blender·FBX·텍스처·프리팹 다운로드</a> · <a href="FIRE_HAETAE_REVIEW.html">놈</a> · <a href="WOOD_DEER_REVIEW.html?v=necklace1">곰</a></p></html>'''
(O.parent/'METAL_TIGER_REVIEW.html').write_text(html,encoding='utf-8')
with zipfile.ZipFile(O/'MetalTiger_Delivery.zip','w',zipfile.ZIP_DEFLATED,compresslevel=2) as z:
 for p in A.rglob('*'):
  if p.is_file():z.write(p,'Unity/Assets/_Project/Art/SpellVFX120/MetalTiger/'+p.relative_to(A).as_posix())
 for name in ['MetalTiger_Working.blend','REPORT.md','model_report.json','fbx_roundtrip.json','unity_model.json','audit.json','scope_after.json','capture.json','capture_memory_attempt.json','model_before.png','model_after.png','regression.json']:
  if (O/name).exists():z.write(O/name,name)
with zipfile.ZipFile(O/'MetalTiger_Delivery.zip') as z:assert z.testzip() is None
print(json.dumps(dict(complete=complete,clips=clips,scope=scope['status'],audit=a['status'])))
