import json,hashlib,subprocess,shutil,zipfile,argparse
from pathlib import Path
R=Path(__file__).resolve().parents[2];p=argparse.ArgumentParser();p.add_argument('name');a=p.parse_args();name=a.name;water=name=='WaterTurtle';glyph='옴' if water else '몸';ident='112_C634' if water else '064_BAB8';title='한국적 거북 영물' if water else '뿔 없는 몽둥이 도깨비';page='WATER_TURTLE_REVIEW.html' if water else 'DOKKAEBI_CLUB_REVIEW.html';O=R/'Art/SpellVFX120'/name;A=R/'Oheangbu/Assets/_Project/Art/SpellVFX120'/name
def read(n):return json.loads((O/n).read_text())
m=read('model_report.json');u=read('unity_model.json');audit=read('audit.json');capture=read('capture.json');before=read('scope_before.json');ledger=read('Meshy/ledger.json')
changes=[p for p,h in before.items() if not (R/p).exists() or hashlib.sha256((R/p).read_bytes()).hexdigest()!=h];expected=f'Oheangbu/Assets/_Project/Art/SpellVFX120/Profiles/{ident}.asset';scope=dict(status='PASS' if changes==[expected] else 'FAIL',checkedFiles=len(before),changed=changes);assert scope['status']=='PASS',scope;(O/'scope_after.json').write_text(json.dumps(scope,indent=2))
ff=next((Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries').glob('*.exe'));clips=[]
for c in capture['clips']:
 if c['frames']!=132 or not c['ended']:continue
 v=c['view'];folder=Path(c['folder']);video=O/(v+'.mp4');subprocess.run([str(ff),'-y','-v','error','-threads','2','-framerate','24','-i',str(folder/'%04d.jpg'),'-frames:v','132','-c:v','libx264','-threads','2','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(video)],check=True);subprocess.run([str(ff),'-v','error','-threads','2','-i',str(video),'-f','null','-'],check=True)
 for label,frame in [('formation',18),('hold',54),('dissolve',98)]:shutil.copy2(folder/f'{frame:04d}.jpg',O/f'{v}_{label}.jpg')
 clips.append(v)
complete=len(clips)==2 and capture['status']=='PASS_DIAGNOSTIC_VISUAL_PENDING';memory=max(c['commitRatio'] for c in capture['clips'])*100;row=ledger['requests'][0];credits=row['consumed_credits']
design=('공식 문화유산 자료의 귀부·청자 거북 조형을 조사하고, 낮고 넓은 거북 체형·육각 갑판·절제된 구름/물결 선을 종합한 독자 콘셉트를 built-in image_gen으로 제작했다. RESEARCH.md에 출처와 실제 도상/게임적 각색을 구분했다.' if water else '사용자 참고의 둥근 눈·굵은 코·넓은 어깨·굽은 팔·문양·허리띠를 살리되, 뿔 없는 머리와 몽둥이를 잡은 손을 built-in image_gen으로 먼저 제작했다. 이 제작 이미지를 Meshy 입력으로 사용했다. 기존 도깨비와 석장승은 보존했다.')
report=f'''# {glyph} — {title}

## 제작

{design}

최종 제작 이미지 Concept.png, 실제 사용 프롬프트 ConceptPrompt.md. Meshy에는 생성된 제작 이미지만 전송했다. ai_model=meshy-7,Ultra,image_enhancement=false,2K PBR,요청16,000poly. 실제 원형 {m['sourceActualTriangles']:,}tris / FBX {m['triangles']:,}tris / Unity {u['triangles']:,}tris. 재질1개, 크기 XYZ(Blender) {m['dimensions']}. 실제35크레딧, 작업ID `{row['task_id']}`. 유료 자동 재시도 없음.

Blender 백그라운드에서 미터 단위/원점 정리, 겹친 위치 정점 용접, 노멀 정리, 부드러운 음영, 원래 UV를 유지한 BaseColor 채도 조정·베이크와 Normal 사본을 적용했다. 원본 GLB SHA256 `{m['sourceHashBefore']}`, 전후 동일. {('전체 앞뒤 길이를3.6m로 정규화했다.' if water else '두 발을 지면 기준으로 세우고, 너무 길었던 몽둥이 하부를 약23cm 줄였다. 손과 손잡이 접촉 영역은 유지한다. 실제 보정량은 model_report.json의 clubShorteningM에 기록한다. 청동색을 억제한 갈색 틴트를 베이크했다.')} 원형·텍스처·Blender 작업본은 별도 보존한다.

## Unity와 검증

{glyph} 한 프로필만 새 정적 외형에 연결했다. 이전 프로필은 Baseline_Previous.asset. 4.6초는 검토용 VFX 수명이며 게임 Duration·SpellBook은 그대로다. 전방4m 지면 고정, {'수 KTP Bottom·물방울/옅은 안개' if water else '토 KTP Bottom·자갈/흙먼지'}와 함께 아래부터 나타나고 위부터 소멸한다. 입자와 표면 표시를 조합한 연출이며 실제 물리 조립/분해는 아니다.

- 통과: FBX 빈 장면 왕복, 실제 삼각형 수 일치, Unity 임포트·셰이더 오류 검사.
- {audit['status']}: {len(audit['passed'])}개 수치 검사.30/60/120fps×정상/0.2배 시간,지면·고정 위치·수명·종료 후 잔존 없음·정적 모델·Collider/Animator 없음.
- {scope['status']}: {scope['checkedFiles']}개 파일 해시. 다른119프로필·기존 소환수/원본 에셋 보존.
- 촬영 {capture['status']}: 완료{len(clips)}개,1920×1080/24fps/5.5초. 실제 C2 배경에서 VFX를 직접 재생한 진단이며 실제 입력·적 AI 전투가 아니다. 최고 시스템 커밋 {memory:.1f}%,85%중단 정책 유지.
- 미검증: 리깅·보행·공격·실제 전투·모든 지형/카메라·다중 소환 성능. 정지 자세의 외형/접지는 확인했지만 실제 몽둥이 휘두르기나 거북 보행을 검사한 것은 아니다.
- 사용자 판단: 최종 한국적인 인상·크기·재질·표정. 자동 검사는 미술 합격 판정이 아니다.

## 파일

Concept.png/ConceptPrompt.md,Meshy원본·텍스처·ledger,{name}_Working.blend(텍스처 포함),FBX,Unity프리팹/재질,외형 전후 렌더,C2영상/정지 이미지,검사JSON. 모든 파트는 이번 정적 표시용이며 생산용 리깅/분리 파츠는 미제작.
''';(O/'REPORT.md').write_text(report,encoding='utf-8')
research_link='<a href="WaterTurtle/RESEARCH.md">거북 자료 조사와 각색</a> · <a href="DOKKAEBI_CLUB_REVIEW.html">몽둥이 도깨비 검토</a>' if water else '<a href="STONE_JANGSEUNG_REVIEW.html">이전 석장승</a>'
videos=''.join(f'<section><h2>{"C2 플레이" if v=="play" else "C2 외부"}</h2><video controls loop preload="metadata" poster="{name}/{v}_hold.jpg" src="{name}/{v}.mp4"></video></section>' for v in clips)
html=f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>{glyph} · {title}</title><style>body{{max-width:1600px;margin:auto;padding:28px;background:#222620;color:#e7e6dc;font:17px/1.65 system-ui}}a{{color:#c6d8bd}}.pair{{display:grid;grid-template-columns:1fr 1fr;gap:18px}}img,video{{width:100%;background:#333}}.note{{background:#363e33;padding:16px}}@media(max-width:800px){{.pair{{grid-template-columns:1fr}}}}</style><h1>{glyph} · {title}</h1><p class="note">정적 외형·등장 연출 검토 / 리깅·실제 전투 미연결</p><div class="pair"><section><h2>제작 콘셉트 · Meshy 입력</h2><img src="{name}/Concept.png"></section><section><h2>실제 Blender 모델</h2><img src="{name}/model_after.png"></section>{videos}</div><p>{m['triangles']:,}삼각형 · 재질1개 · Meshy7 실제{credits}크레딧</p><p><a href="{name}/REPORT.md">제작·검사 보고서</a> · <a href="{name}/ConceptPrompt.md">이미지 프롬프트</a> · <a href="{name}/{name}_Delivery.zip">Blender·FBX·텍스처·원본 다운로드</a> · {research_link}</p></html>''';(O.parent/page).write_text(html,encoding='utf-8')
with zipfile.ZipFile(O/(name+'_Delivery.zip'),'w',zipfile.ZIP_DEFLATED,compresslevel=2) as z:
 for p in A.rglob('*'):
  if p.is_file():z.write(p,'Unity/'+p.relative_to(A).as_posix())
 for n in [name+'_Working.blend','REPORT.md','Concept.png','ConceptPrompt.md','RESEARCH.md','model_report.json','fbx_roundtrip.json','unity_model.json','audit.json','scope_after.json','capture.json','model_before.png','model_after.png']:
  if (O/n).exists():z.write(O/n,n)
 for p in (O/'Meshy').rglob('*'):
  if p.is_file() and '.private' not in p.parts and p.suffix in ['.json','.glb','.fbx','.png']:z.write(p,p.relative_to(O).as_posix())
with zipfile.ZipFile(O/(name+'_Delivery.zip')) as z:assert z.testzip() is None
if complete and audit['status']=='PASS':(O/'DONE.json').write_text(json.dumps(dict(status='COMPLETE_STATIC_REVIEW_DELIVERY',userVisualApproval=False,triangles=m['triangles'],page=page)))
print(dict(complete=complete,triangles=m['triangles'],audit=audit['status'],scope=scope['status'],credits=credits))
