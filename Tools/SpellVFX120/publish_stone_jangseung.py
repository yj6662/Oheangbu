from pathlib import Path
import json,hashlib,subprocess,shutil,zipfile
R=Path(__file__).resolve().parents[2];O=R/'Art/SpellVFX120/StoneJangseung';A=R/'Oheangbu/Assets/_Project/Art/SpellVFX120/StoneJangseung'
def read(name):return json.loads((O/name).read_text())
m=read('model_report.json');u=read('unity_model.json');audit=read('audit.json');capture=read('capture.json');before=read('scope_before.json');ledger=read('Meshy/ledger.json')
changes=[p for p,h in before.items() if not (R/p).exists() or hashlib.sha256((R/p).read_bytes()).hexdigest()!=h]
scope=dict(status='PASS' if changes==['Oheangbu/Assets/_Project/Art/SpellVFX120/Profiles/064_BAB8.asset'] else 'FAIL',checkedFiles=len(before),changed=changes);assert scope['status']=='PASS',scope
(O/'scope_after.json').write_text(json.dumps(scope,indent=2))
ff=next((Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries').glob('*.exe'))
clips=[]
for c in capture['clips']:
 if c['frames']!=132 or not c['ended']:continue
 v=c['view'];folder=Path(c['folder']);video=O/(v+'.mp4')
 subprocess.run([str(ff),'-y','-v','error','-threads','2','-framerate','24','-i',str(folder/'%04d.jpg'),'-frames:v','132','-c:v','libx264','-threads','2','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(video)],check=True)
 subprocess.run([str(ff),'-v','error','-threads','2','-i',str(video),'-f','null','-'],check=True)
 for label,frame in [('formation',18),('hold',54),('dissolve',98)]:shutil.copy2(folder/f'{frame:04d}.jpg',O/f'{v}_{label}.jpg')
 clips.append(v)
complete=len(clips)==2 and capture['status']=='PASS_DIAGNOSTIC_VISUAL_PENDING';memory=max(c['commitRatio'] for c in capture['clips'])*100
credits=sum(r.get('consumed_credits',0) for r in ledger['requests']);tasks='\n'.join(f"- `{r['task_id']}` / {r['mode']} / {r['status']} / {r.get('consumed_credits')}크레딧" for r in ledger['requests'])
report=f'''# 몸 — 석장승 재해석 시안

## 제작 기준과 결과

제공 사진의 돌기둥·둥근 눈·굵은 코를 해석한 작성 텍스트로 Meshy7 Ultra 신규 원형을 생성했다. 사진 자체를 Meshy에 업로드하지 않았으며 명문을 복제하지 않았다. 팔·다리 없는 독립 석주, 강한 얼굴 실루엣과 낮은 채도의 무광 돌로 게임적 각색했다. 이전 바위 도깨비의 모델·텍스처·프리팹·영상은 보존했다.

원형의 상부 장식·받침과 몸통 구멍을 확인했다. Blender에서 기존 얼굴 조각을 남기고 장식 머리끝과 건축형 몸통을 잘라냈다. 별도로 만든 모서리가 둥근 불규칙 통돌과 두 개의 둥근 돌눈을 결합하고,12mm voxel union으로 하나의 돌덩이로 합친 뒤 완화·게임용 면수 정리를 했다. 원형의 강조된 이빨과 일부 조각면은 유지했다. 원형 텍스처는 보존하되, 새 토폴로지는 UV를 다시 펼쳐 별도 회갈색 광물 노이즈·미세 요철을2K BaseColor/Normal로 베이크했다. 완전한 사진 재현이나 사용자 외형 승인으로 처리하지 않는다.

Meshy `ai_model=meshy-7`, Ultra, 요청12,000 triangle target,2K PBR. 실제 원형 {m['sourceActualTriangles']:,}tris / FBX {m['triangles']:,}tris / Unity {u['triangles']:,}tris. 재질1개, 전체 높이 {m['dimensions'][2]:.2f}m, 폭 {m['dimensions'][0]:.2f}m. 최종 재질 metallic0,roughness.88. 추가 생성 실제 {credits}크레딧, 자동 유료 재시도 없음.

{tasks}

원본 GLB SHA256 전후 동일: `{m['sourceHashBefore']}`. 원본 사진·Meshy 파일과 편집용 Blender 파일은 별도로 남겼다.

## Unity 표시

몸의 기존 정적 소환 표현 경로에 새 석장승 프리팹을 연결한다. Baseline_Dokkaebi.asset에 직전 프로필을 보존한다. KTP 토 Bottom과 흙먼지·자갈, 발밑부터 형성하고 위부터 돌가루로 사라지는4.6초 검토용 연출을 재사용한다. 중심은 시전 전방4m의 지면에 고정한다. 게임 Duration·SpellBook·전투 기능은 변경하지 않았다.

정적 석주 하단의 두 지지점을 기존 지면 보정에 연결했다. 하단35cm에만 제한적인 높이 보정, 편차15cm 이상/지면 없음은 숨긴다. 석장승의 보행·AI나 이동형 소환수로 구현한 것은 아니다.

## 검증

- 통과: 빈 Blender 씬 FBX 재임포트 삼각형 수·크기 동일, 원형 보존.
- {audit['status']}: {len(audit['passed'])}개 검사.30/60/120fps×정상/0.2배 시간, 지면·수명·위치 고정·종료 정리·정적 형상·Collider/Animator 없음. 하단 앵커 최대 간격 {audit['maxFootGap']*1000:.2f}mm. 감사 코드의 foot/paw는 재사용 경로의 하단 지지점을 뜻하며 이 모델에 발이 있다는 의미가 아니다.
- {scope['status']}: {scope['checkedFiles']}개 파일 해시. 몸 프로필만 변경, 나머지119개·기존 도깨비/사슴/해태/호랑이/원본 에셋 보존.
- 촬영 {capture['status']}: 완료 {len(clips)}개, C2 1920×1080/24fps/5.5초. 직접 VFX를 재생한 진단이며 실제 입력·적 AI 전투는 아니다. 최고 시스템 커밋 {memory:.1f}%,85%중단 기준 유지.
- 미검증: 리깅·AI·실제 전투·모든 경사/카메라·다중 소환 성능. 한국적인 인상·세련됨·최종 크기는 사용자 검토 대기.

## 파일

StoneJangseung_Working.blend(텍스처 포함),FBX,Unity 프리팹/재질,원본사진과 Meshy 원형/텍스처/작업 기록,전후 렌더·얼굴 확대·C2 두 시점 영상·정지 이미지·검사 보고서. 이전 도깨비 페이지는 유지하며 옴은 진행하지 않았다.
'''
(O/'REPORT.md').write_text(report,encoding='utf-8')
videos=''.join(f'<section><h2>{"C2 플레이 시점" if v=="play" else "C2 외부 시점"}</h2><video controls loop preload="metadata" poster="StoneJangseung/{v}_hold.jpg" src="StoneJangseung/{v}.mp4"></video></section>' for v in clips)
html=f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>몸 · 석장승</title><style>body{{max-width:1600px;margin:auto;padding:28px;background:#25241f;color:#ebe7df;font:17px/1.65 system-ui}}a{{color:#d6c49c}}.pair{{display:grid;grid-template-columns:1fr 1fr;gap:18px}}img,video{{width:100%;background:#333}}.note{{background:#39362e;padding:16px}}@media(max-width:800px){{.pair{{grid-template-columns:1fr}}}}</style><h1>몸 · 석장승</h1><p>돌기둥 몸체 · 강조된 돌눈과 코 · 정돈한 조각면 · 무광 회갈색 석재</p><p class="note"><b>정적 외형·등장 연출 시안 / 리깅·전투 미연결</b><br>참고 사진의 특징을 해석한 신규 Meshy7 모델입니다. 기존 도깨비는 보존했습니다.</p>
<div class="pair"><section><h2>새 원형</h2><img src="StoneJangseung/model_before.png"></section><section><h2>Blender 각색판</h2><img src="StoneJangseung/model_after.png"></section>{videos}</div>
<p>{m['triangles']:,}삼각형 · 재질1개 · 높이{m['dimensions'][2]:.2f}m · 실제{credits}크레딧</p><p>소환·유지·소멸은 검토용4.6초. C2 영상은 진단 재생이며 실제 전투 영상이 아닙니다.</p>
<p><a href="StoneJangseung/face_detail.png">얼굴 확대</a> · <a href="StoneJangseung/Reference_StoneJangseung.png">참고 사진</a> · <a href="StoneJangseung/REPORT.md">변경·검사 보고서</a> · <a href="StoneJangseung/StoneJangseung_Delivery.zip">Blender·FBX·텍스처 다운로드</a> · <a href="STONE_DOKKAEBI_REVIEW.html">이전 바위 도깨비</a></p></html>'''
(O.parent/'STONE_JANGSEUNG_REVIEW.html').write_text(html,encoding='utf-8')
with zipfile.ZipFile(O/'StoneJangseung_Delivery.zip','w',zipfile.ZIP_DEFLATED,compresslevel=2) as z:
 for p in A.rglob('*'):
  if p.is_file():z.write(p,'Unity/'+p.relative_to(A).as_posix())
 for name in ['StoneJangseung_Working.blend','REPORT.md','model_report.json','fbx_roundtrip.json','unity_model.json','audit.json','scope_after.json','capture.json','model_before.png','model_after.png','face_detail.png','Reference_StoneJangseung.png','REFERENCE_INTERPRETATION.md']:
  if (O/name).exists():z.write(O/name,name)
 for p in (O/'Meshy').rglob('*'):
  if p.is_file() and '.private' not in p.parts and p.suffix in ['.json','.glb','.fbx','.png']:z.write(p,p.relative_to(O).as_posix())
with zipfile.ZipFile(O/'StoneJangseung_Delivery.zip') as z:assert z.testzip() is None
print(dict(complete=complete,triangles=m['triangles'],audit=audit['status'],scope=scope['status'],credits=credits))
