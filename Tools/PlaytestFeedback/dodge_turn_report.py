from pathlib import Path
import json,math,html,hashlib
R=Path(__file__).resolve().parents[2];O=R/'Art/PlaytestRecovery/DodgeTurn'
p=sorted((O/'Live').glob('*/live_input.json'))[-1];d=json.loads(p.read_text(encoding='utf-8-sig'));rows=d['frames']
def subset(n):return [x for x in rows if x['phase']==n]
def distance(a,b):return math.sqrt(sum((a[k]-b[k])**2 for k in ('x','y','z')))
def foot(n):
 a=subset(n);return max(distance(a[0][side],x[side]) for side in ('leftFoot','rightFoot') for x in a)
checks=[]
def check(name,ok,detail):checks.append(dict(name=name,status='통과' if ok else '실패',detail=detail))
check('실제 Play 입력 루프·정리',d['restored'] and not d['error'],d['scope'])
for phase,angle in [('hold45',45),('hold89',89)]:
 a=subset(phase);movement=foot(phase)
 check(f'{angle}도 시선 / 발 유지',movement<.01 and all(x['turnSerial']==0 for x in a),f'양 발 최대 변위 {movement*1000:.3f}mm, 몸 방향 고정, Turn 미진입')
for phase,state,serial in [('look100','TurnRight',1),('look_left','TurnLeft',2),('crouch_look','CrouchTurnRight',3)]:
 a=subset(phase);check(phase,any(x['state']==state for x in a) and a[-1]['turnSerial']==serial,f'{state}, 회전 누적 {a[-1]["turnSerial"]}회')
for phase in ['dodge_forward','dodge_back','dodge_left','dodge_right']:
 a=subset(phase);check(phase,any(x['dodging'] and not x['rolling'] for x in a) and any(x['state']=='Dodge' for x in a),'새 방향 클립을 사용하는 Dodge 상태 진입')
for phase,after in [('roll_forward','crouch_hold'),('roll_right','crouch_return')]:
 a=subset(phase);b=subset(after);check(phase,any(x['rolling'] and x['state']=='CrouchRoll' for x in a) and all(x['crouching'] and abs(x['height']-1.25)<.001 and not x['dodging'] for x in b),'구르기 → Crouch 복귀, 높이 1.25m 유지')
check('구르기 이후 유령 회피 없음',all(x['state']!='Dodge' and not x['dodging'] for x in subset('stand')),'서기 전환에서 남은 Dodge 트리거 없음')
a=subset('pause_inputs');b=subset('after_menu')
check('메뉴 입력 차단·복원',all(x.get('blocked') and x.get('timeScale')==0 and not x['dodging'] for x in a) and distance(a[0]['position'],a[-1]['position'])<.001 and all(x.get('timeScale')==1 and not x.get('blocked') for x in b),'메뉴 중 W·Shift·C 차단, 닫은 뒤 시간 배율 1 / 입력 복원')
bakes=[json.loads(p.read_text(encoding='utf-8-sig')) for p in (O/'Unity').glob('human_bake_*.json')]
check('C02 리타깃·Unity 클립',len(bakes)==5 and all(b['status']=='EXACT_SOURCE_POSE_PASS' for b in bakes),'5클립 ×121자세,54본 원본 바인드 비교. 실제 지형 관통 합격과 구분')
validation={'live':str(p.relative_to(R)),'checks':checks,'unverified':['사용자 직접 입력의 동작 만족도와 최종 외형 승인','30·60·120fps 강제 조건 및 감속 전체 조합','계단·급경사·낮은 천장 구르기와 전투 피격·탑승 교차 검사','모든 메시 면 단위 의상·손·붓 관통 검사'],'buildCreated':False}
(O/'VALIDATION.json').write_text(json.dumps(validation,ensure_ascii=False,indent=2),encoding='utf-8')
table='\n'.join(f'| {c["name"]} | {c["status"]} | {c["detail"]} |' for c in checks)
text=f'''# 회피·시선 회전·웅크림 구르기 — 2026-09-14

현재 손은 임시 채택 상태로 보존했다. [손 후속 기록](../../../Docs/Plans/PLAYER-HAND-FOLLOWUP-20260914.md).

## 적용
- Mixamo 공식 로그인 세션에서 Standing Dodge Forward / Backward / Left / Right, Stand To Roll을 내려받았다. Without Skin, FBX Binary,60fps, 키프레임 축소 없음. 원본 SHA256은 SOURCE_LEDGER.json.
- 별도 Blender·FBX 및 PlaytestDodgeTurn Unity 에셋. C02의 얼굴·몸·손 메시와 기존 이동 원본을 교체하지 않았다.
- 회피 3m /0.25초 유지. 웅크림 Shift는 3m /0.8초 구르기이며 기존 무적 0.3초 유지. 공중·작도·갈무리·메뉴 중 시작 금지. 완료 후 웅크림 유지.
- Stand To Roll 원본 0.5~1.8초를 사용. 실제 C02 몸 표면이 바닥을 뚫지 않도록 전신 높이 보정(최대 약36.6cm)을 베이크했다. 팔이나 본 길이를 늘리지 않았다. 긴 두루마기의 모든 동적 관통을 해결했다는 뜻은 아니다.
- 선 상태는 Left/Right Turn, 웅크림 제자리 회전은 보유 Crouch Walk 좌우를 회전 스텝으로 활용했다. 몸–시선 차이90도에서 머리·목 분담, 초과 시90도 몸 회전. 기본 Idle의 발 재배치를 없앤 별도 IdleStill 사용.
- 오른쪽 방향으로 구르면 모델이 이동 방향을 향한다. 게임 루트와 카메라는 소스 모션의 루트 회전·이동을 추가로 적용하지 않는다.

## 검사
실제 Unity 씬의 정상 InputSystem·PlayerMotor·Animator에 가상 키보드/마우스 입력을 넣은 짧은 국부 검사다. 사람의 직접 플레이 또는 전체 경로 완주 증거로 취급하지 않는다.

| 항목 | 결과 | 근거 |
|---|---|---|
{table}

초기 검사에서 발견한 기본 Idle의 발 이동과 구르기 뒤 잔여 회피 트리거는 수정 후 재검사했다. 이전 기록은 Live 하위에 보존했다. 외부 스크린샷은 플레이 카메라가 벽 때문에 가까워져 그림자 전용으로 바뀐 월드 모델을 촬영 순간에만 표시한 뒤 원래 상태를 복원한다. 빈 캐릭터 화면은 외형 검증 자료로 사용하지 않는다.

## 미검증·후속
'''+''.join('- '+u+'\n' for u in validation['unverified'])+'''
폐광은 조사·첫 교전·휴식·저장 기능을 가진 플레이테스트 임시 공간이다. 내부 공간 구성과 상세 미술은 완성 던전이 아니며 이번 작업에서 변경하지 않았다.

빌드·영상·자동 전체 보행 없음. 캡처는1920×1080 한 장씩, 커밋85% 이상이면 중단하도록 설정했다. 최종 정리에서 Play 종료·검사 저장 슬롯 해제·씬 저장 완료(dirty=false)를 확인했다. 종료 시 커밋 사용률58.4%.
'''
(O/'REPORT.md').write_text(text,encoding='utf-8')
titles={'hold45':'45도 — 발 유지','hold89':'89도 — 발 유지','look100':'90도 초과 — 오른쪽 몸 회전','look_left':'왼쪽 몸 회전','crouch_look':'웅크림 방향 전환','dodge_forward':'전방 회피','dodge_back':'후방 회피','dodge_left':'좌측 회피','dodge_right':'우측 회피','roll_forward':'웅크림 전방 구르기','roll_right':'웅크림 측방 구르기'}
cards=''
for n,title in titles.items():
 image=p.parent/(n+'.png')
 if image.exists():cards+=f'<section id="{n}"><h2>{title}</h2><a href="{image.relative_to(O).as_posix()}"><img loading="lazy" src="{image.relative_to(O).as_posix()}" alt="{title}"></a></section>'
tr=''.join(f'<tr><td>{html.escape(c["name"])}</td><td>{c["status"]}</td><td>{html.escape(c["detail"])}</td></tr>' for c in checks)
page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>회피·시선·구르기 검토</title><style>body{margin:0;background:#171914;color:#e8e1d2;font:17px/1.7 system-ui}main{max-width:1250px;margin:auto;padding:40px 24px}h1,h2{font-weight:550}a{color:#bdd9c0}img{width:100%;aspect-ratio:16/9;object-fit:contain;background:#111}section{margin:48px 0}table{border-collapse:collapse;width:100%;font-size:14px}td{border-bottom:1px solid #47493e;padding:10px}.notice{background:#2b3028;padding:20px;border-left:3px solid #91aa7e}</style><main><p>오행부 / 플레이테스트 캐릭터</p><h1>발은 멈추고, 시선부터 돌리기</h1><p>Mixamo 회피4방향 · 웅크림 구르기 · 90도 초과 몸 회전</p><div class="notice">손은 현재 엄지 감싸기 버전을 임시 보존했습니다. 다음 손 작업에서 외형을 더 개선합니다.<br>Shift: 회피 / C: 웅크림 / 웅크림+Shift: 구르기 / X: 바닥 착석.<br>회피 판정은 기존값, 구르기는 이동0.8초·무적0.3초로 시작합니다.</div><p>가상 입력을 실제 Play 루프에 주입한 국부 검사입니다. 외형 만족도는 사용자 검토 대상입니다. 아래는 외부 카메라의 실제 동작 순간 정지 이미지이며 동영상은 만들지 않았습니다.</p><p><a href="REPORT.md">기술 보고서</a> · <a href="SOURCE_LEDGER.json">Mixamo 원본 기록</a> · <a href="VALIDATION.json">검사 결과</a> · <a href="Motion/Player_C02_DodgeRoll.blend">Blender</a> · <a href="Motion/Player_C02_DodgeRoll.fbx">FBX</a></p>'''+cards+'<h2>기술 검사</h2><table>'+tr+'</table><h2>남은 확인</h2><p>'+html.escape(' / '.join(validation['unverified']))+'</p><p>폐광은 기능이 연결된 임시 플레이테스트 공간입니다. 완성된 던전의 내부·상세 미술은 아닙니다. 새 빌드는 만들지 않았습니다.</p></main></html>'
(O/'REVIEW.html').write_text(page,encoding='utf-8')
print(json.dumps({'report':str(O/'REVIEW.html'),'passes':sum(c['status']=='통과' for c in checks),'failures':[c for c in checks if c['status']=='실패']},ensure_ascii=False))
