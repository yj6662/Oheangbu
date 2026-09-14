"""Refresh the selected-A delivery from exact available evidence; no Unity/build/capture."""
from pathlib import Path
import json,html,hashlib
R=Path(__file__).resolve().parents[2];O=R/'Art/PlaytestPolish/Hands'
def load(name):
 p=O/name
 return json.loads(p.read_text(encoding='utf-8-sig'))if p.exists()else None
fit=load('Validation/contact_fit.json');contact=load('Validation/hand_contact.json');surface=load('Validation/hand_surface_sampling.json');preserve=load('Validation/preservation.json');rt=load('Validation/fbx_roundtrip.json');imp=load('Validation/unity_import.json');apply=load('Validation/unity_apply.json')
fbx=O/'Exports/Player_C02_GripA.fbx';sha=hashlib.sha256(fbx.read_bytes()).hexdigest()
checks=[('골격·몸·얼굴·의상 보존',preserve['status'],'54본 bind 행렬 및 오른손 밖 정점 변화 0'),
 ('실제 붓대 정점 접촉',contact['status'],f"최대 관통 {contact['max_penetration_m']*1000:.3f}mm; 국부 수정 {contact['corrected_vertices']}정점, 최대 {contact['max_rest_skin_correction_m']*1000:.3f}mm"),
 ('삼각형 내부 접촉',surface['status']if surface['sourceFbxSha256']==sha else'FAIL_STALE_REPORT',f"0.5mm 이하 샘플 간격, {surface['samples']:,}점, 최대 관통 {surface['maximum_sampled_penetration_m']*1000:.3f}mm"),
 ('FBX 빈 장면 재임포트',rt['status'],f"{rt['bones']}본, 오른손 마커 {rt['right_finger_markers']}개, {rt['samples']:,}개 정점 샘플"),
 ('Unity 새 모델 임포트',imp['status']if imp else'미검증','새 FBX·Humanoid·30손가락·근접 메시'),
 ('현재 플레이 씬 적용',apply['status']if apply else'미검증','메시·파지 프로필만 교체하며 이동 controller/profile은 보존')]
for name,label in [('current_skin_contact.json','현재 Unity 손 표면'),('runtime_gesture_qa.json','현재 Unity 작도 합성 검사')]:
 d=load('Validation/Unity/'+name)
 state='미검증'if d is None else d.get('status','미검증')
 if d and d.get('sourceFbxSha256')!=sha:state='FAIL_STALE_REPORT'
 checks.append((label,state,'새 A 자산 SHA와 bound mesh 출처를 함께 검사; native 입력 검사는 아님'))
lines=['# 선택 A — 손 파지 사본','',
 '선택한 A 이미지에 맞춰 검지 끝마디를 더 굽히고 엄지를 대립시킨 감싸 쥐는 자세를 제작했습니다. 새 손 자세와 붓 파지 기준을 실제 C02 메시에서 보정했습니다. 얼굴·몸·의상·54본 골격은 유지했습니다.','',
 '기존 파지 결과를 이름만 바꾼 것이 아닙니다. 검지 끝마디는 기존 1°에서 약 27°로, 엄지 두 마디는 0°에서 8°·5°로 바꿨으며 나머지 손가락도 다시 최적화했습니다. 오른손의 접촉 정점만 최대 0.541mm 수정했습니다.','',
 '| 검사 | 결과 | 근거 |','|---|---|---|']
lines += [f'| {label} | {state} | {detail} |'for label,state,detail in checks]
lines += ['', '**범위 제한:** 삼각형 검사는 유한한 정지 자세 샘플이며 모든 연속 표면·모든 모션의 무관통 증명이 아닙니다. 천 물리, 좌식 전환과 지형 접촉, 실제 키 입력, 프레임별 시전 연속성은 실행된 별도 증거가 없으면 미검증입니다. 생성 참조 이미지와 실제 런타임 결과의 외형 합격은 사용자가 판단합니다.','',
 '실제 예산: 몸 59,246 tris, 근접 오른팔 6,900 tris, 기존 붓 4,680 tris. 근접 팔과 월드 몸은 동일 골격 자세를 사용하며 두 번째 Animator를 추가하지 않습니다.','',
 '새 A 전용 프로필의 DrawingBrushRoll은 -72°이며, 일반 carry는 기존 값을 유지합니다. 앉을 때는 무릎 위를 향한 wrist/brush/pole 값으로 Posture01에 따라 보간합니다. 기존 프로필의 좌식 보정 기본값은 비활성이므로 영향을 주지 않습니다.','',
 '## 파일','',
 '- `Work/Player_C02_GripA.blend` — 중립 골격과 FBX용 파지 마커',
 '- `Work/Player_C02_GripA_Assembly.blend` — 실제 붓과 연결한 편집용 사본',
 '- `Exports/Player_C02_GripA.fbx` — 중립 모델·스킨·마커, 새 생산용 모션 없음',
 '- `Textures/` — 기존 모델/붓의 원본 텍스처 사본; `Validation/textures.json`에 출처 해시',
 '- `Validation/Unity/` — 새 A 런타임 검사/순차 스크린샷(확보된 파일만 존재)',
 '',f'내보낸 FBX SHA256: `{sha}`','',
 'Unity: `WorldMacroPlayerReRigAuthoring.ExecuteGripA("import" | "apply")`; 런타임 `ExecuteGripARuntime("check" | "contact" | "capture:center" | "capture:top-left" | "capture:bottom-right" | "capture:sit-carry")`. 작업 중 빌드와 영상은 제작하지 않았습니다.']
(O/'REPORT.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
rows=''.join(f'<tr><td>{html.escape(l)}</td><td>{html.escape(s)}</td><td>{html.escape(d)}</td></tr>'for l,s,d in checks)
shots=[]
for p in sorted((O/'Validation/Unity').glob('runtime_capture_*.json')):
 d=json.loads(p.read_text(encoding='utf-8-sig'));image=d.get('image');status=d.get('status','미검증')
 if image and Path(image).exists():
  rel=Path(image).relative_to(O).as_posix();shots.append(f'<figure><img src="{html.escape(rel)}"><figcaption>{html.escape(p.stem)} — {html.escape(status)} · 합성 자세 검수, native 입력 아님</figcaption></figure>')
page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>선택 A 파지 검수</title><style>body{max-width:1200px;margin:40px auto;padding:0 20px;background:#e9e1d1;color:#26231f;font:17px/1.7 system-ui,sans-serif}h1{font-size:30px}table{width:100%;border-collapse:collapse}td,th{padding:10px;border:1px solid #b0a594;text-align:left}img{width:100%;height:auto}figure{margin:30px 0}a{color:#75421f}.notice{padding:18px;background:#d7cebc}</style><h1>선택 A — 손 파지 검수</h1><p>원본 C02의 몸·얼굴·의상·54본 bind를 유지하면서 감싸 쥐는 오른손과 엄지 대립을 새로 제작했습니다.</p><p class="notice">아래 결과는 실제 확보된 검사만 표시합니다. 손·상체 검증과 전체 천·보행 검증을 구분합니다. 외형 최종 합격은 사용자 검토 대상이며 빌드·영상은 만들지 않았습니다.</p>'''
page+='<table><tr><th>검사</th><th>결과</th><th>근거</th></tr>'+rows+'</table>'
page+='<p><a href="Work/Player_C02_GripA.blend">중립 Blender</a> · <a href="Work/Player_C02_GripA_Assembly.blend">붓 포함 편집 사본</a> · <a href="Exports/Player_C02_GripA.fbx">FBX</a> · <a href="REPORT.md">검증 보고서</a> · <a href="Validation/textures.json">텍스처 목록</a></p>'
page+='<h2>선택한 외형 참조</h2><figure><img src="Grip_A.png"><figcaption>선택한 A 참조 이미지 · 실제 Unity 캡처가 아님</figcaption></figure>'
page+='<h2>현재 A 실물 검수</h2>'+(''.join(shots)if shots else'<p>새 A 런타임 스크린샷은 아직 확보되지 않았습니다.</p>')
(O/'REVIEW.html').write_text(page+'</html>',encoding='utf-8')
print('A review refreshed; runtime evidence available:',len(shots))
