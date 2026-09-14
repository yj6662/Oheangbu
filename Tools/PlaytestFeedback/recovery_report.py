"""Assemble existing measurements and screenshots; never invent a missing acceptance result."""
import json,html,csv,hashlib,shutil,collections,os
from pathlib import Path
R=Path(__file__).resolve().parents[2];O=R/'Art/PlaytestRecovery'
def read(p):return json.loads(Path(p).read_text(encoding='utf-8-sig'))
def rel(p):return os.path.relpath(p,O).replace('\\','/')
def link(p,label):return f'<a href="{html.escape(rel(p))}">{html.escape(label)}</a>'
def table(head,rows):return '<table><thead><tr>'+''.join(f'<th>{html.escape(str(x))}</th>' for x in head)+'</tr></thead><tbody>'+''.join('<tr>'+''.join(f'<td>{html.escape(str(x))}</td>' for x in row)+'</tr>'for row in rows)+'</tbody></table>'
perfPaths=[R/p for p in read(O/'Performance/final_run_set.json')];perf=[read(p) for p in perfPaths]
feet=read(O/'Motion/Unity/unity_synthetic_feet.json');hand=read(O/'Hands/Validation/Unity/runtime_gesture_qa.json');surface=read(O/'Hands/Validation/hand_surface_sampling.json');rt=read(O/'Hands/Validation/fbx_roundtrip.json')
motor=read(R/'Art/PlaytestPolish/Locomotion/Reports/unity_synthetic_motor.json');(O/'Motion/Unity/motor_state_checks.json').write_text(json.dumps(motor,ensure_ascii=False,indent=2))
livePath=max((O/'LiveInput').rglob('live_input.json'),key=lambda p:p.stat().st_mtime);live=read(livePath)
liveRows=[]
for phase in dict.fromkeys(s['phase'] for s in live['frames']):
 rows=[s for s in live['frames'] if s['phase']==phase];tail=rows[-max(1,len(rows)//3):]
 liveRows.append([phase,round(sum(s['speed'] for s in tail)/len(tail),3),round(tail[-1]['crouch'],3),any(s['airborne'] for s in rows),tail[-1]['sitting'],any(s['drawing'] for s in rows)])
(O/'LiveInput/assessment.json').write_text(json.dumps({'scope':live['scope'],'source':rel(livePath),'status':'RECORDED_VIRTUAL_INPUT','phases':liveRows,'unverified':['Native human input','End-to-end occupied vehicle/menu/death restoration','Real ceiling at route locations','Turning animation under mouse look','Live skinned foot displacement on all slopes/stairs']},ensure_ascii=False,indent=2))
cal=read(O/'Motion/Unity/unity_recovery-prepare.json');rates={c['name']:c for c in cal['clips']};retarget=read(O/'Motion/retarget_report.json');motionRows=[]
for c in retarget['clips']:
 n=c['name'];rate=rates.get(n,{}).get('playbackRate',1);motionRows.append([Path(c['source']).name,n,round(c['duration'],3),round(rate,4),'원본 보존 / C02 파생'])
with (O/'Motion/clip_mapping.csv').open('w',encoding='utf-8-sig',newline='') as f:w=csv.writer(f);w.writerow(['source','C02 clip','seconds','rate','status']);w.writerows(motionRows)
retarget['finalPostprocessedFbxSha256']=hashlib.sha256((O/'Motion/Player_C02_AttachedMotions.fbx').read_bytes()).hexdigest();retarget['postprocess']='recovery_crouch_idle.py + recovery_run_contacts.py; see separate reports';(O/'Motion/retarget_report.json').write_text(json.dumps(retarget,ensure_ascii=False,indent=2))
metrics=[]
for run in perf:
 for p in run['phases']:
  metrics.append([run['mode'],run['height'],p['phase'],round(p['frameP50'],2),round(p['frameP95'],2),round(p['frameP99'],2),round(p['cpuP50'],2),round(p['gpuP50'],2),round(p.get('generationP95',0),2),p.get('packetGcP95','미계측'),p.get('submissionGcP95','미계측')])
maxSlip=max(c['maxStanceSlipAfter'] for c in feet['cases']);maxPen=max(c['maxFootSkinPenetrationAfter']for c in feet['cases']);maxTip=max(c['observed']for c in hand['checks']if 'near_tip_pixels' in c['name'])
preserved=[]
for p in (O/'MotionSources').glob('*.fbx'):
 original=Path('C:/Users/yj666/Desktop/새 폴더 (2)')/p.name
 preserved.append({'file':p.name,'originalExists':original.exists(),'sameSha256':original.exists() and hashlib.sha256(original.read_bytes()).digest()==hashlib.sha256(p.read_bytes()).digest()})
(O/'source_preservation.json').write_text(json.dumps(preserved,ensure_ascii=False,indent=2))
buildRoot=R/'Builds/Playtest-20260915';buildFiles=list(buildRoot.glob('*/release_build_report.json'));build=None
if buildFiles:build=read(max(buildFiles,key=lambda p:p.stat().st_mtime))
buildLatest=buildRoot/'build_latest.json'
if buildLatest.exists():build=read(buildLatest)
summary={'status':'IMPLEMENTED_WITH_LIMITATIONS','footCases':len(feet['cases']),'footStatus':feet['status'],'maxStanceSlipMeters':maxSlip,'maxFootSkinPenetrationMeters':maxPen,'motorStatus':motor['status'],'motorCases':len(motor['checks']),'handStatus':hand['status'],'handCases':hand['passed'],'maxTipErrorPixels':maxTip,'roundtrip':rt,'performanceSources':[rel(p)for p in perfPaths],'nativeHumanInput':'UNVERIFIED','120fps':'NOT_ACHIEVED_IN_EDITOR_DIAGNOSTIC','visualApproval':'PENDING_USER_REVIEW','build':build}
(O/'summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2))
sections=[]
sections.append('<header><p>오행부 · 플레이테스트 복구</p><h1>식생 성능 · A 파지 · 이동 모션</h1><p>기존 에셋을 보존한 파생본. 실제 Play 프레임과 합성 검사를 구분했습니다. 영상·자동 전체 보행은 제작하지 않았습니다.</p></header>')
sections.append('<section><h2>확인된 결과와 남은 항목</h2>'+table(['항목','결과'],[
 ['식생','64m 생성 작업과 종류별 상주 범위, 캐시 재사용. 가까운 밀도·확정 위치 유지. 중거리 수목 카드를110m 기준으로 앞당김.'],
 ['성능 목표','Editor 진단에서120fps/8.33ms 미달. 아래 실제 수치를 확인하세요.'],
 ['모션',f'첨부9개 → C02 클립9개 + 웅크림 대기. 합성 발 검사{len(feet["cases"])}개 통과. 최대 미끄러짐{maxSlip*100:.2f}cm / 피부 관통{maxPen*100:.2f}cm.'],
 ['입력',f'격리 Motor{len(motor["checks"])}개 검사 통과. 현재 장면에서 가상 키보드·마우스를 통한 실제 Play 전환 기록. 사람의 직접 조작과는 구분.'],
 ['A 손',f'국부 손·손목 메시/웨이트 수정, 본54개·외부 신체 좌표 유지, FBX 최대4웨이트. 합성 손 검사{hand["passed"]}개, 최대 붓끝 오차{maxTip:.3f}px.'],
 ['외형 검토','중앙/가장자리 손·손바닥·소매와 웅크린 상체 자세는 사용자 검토 대상. 수치 통과로 자연스러움을 승인하지 않았음.'],
 ['미검증','실제 메뉴·차량·사망 왕복 전체, 국 활용 장소 우회, 모든 경사/계단에서의 발 미끄러짐,30/60/120 실제 렌더 fps별 전체 조작.'],
 ])+'</section>')
sections.append('<section><h2>이동 중 성능</h2><p>동일 캐릭터·현재 장면·Editor Play. 실제 플레이 카메라 설정을 복제한 RenderTexture 카메라만 회전/224m 이동/복귀. 사전 전체 생성 없음. 캐릭터 이동 완주나 독립 빌드 FPS의 증거는 아닙니다. Legacy의 초기 전체 생성으로 cold 구간의 샘플이 누락될 수 있으며0ms로 해석하지 않습니다.</p>'+table(['방식','높이','구간','프레임p50 ms','p95','p99','CPU p50','GPU p50','생성p95','패킷GC p95 B','제출GC p95 B'],metrics)+'<p>'+ ' · '.join(link(p,p.parent.name)for p in perfPaths)+'</p><p>CPU/GPU p95·p99, 충돌 갱신, 종류별 상주 수, 메모리와 렌더 제출량은 각 JSON 원자료에 포함됩니다. GC는 계측한 생성·패킷·제출 구간의 현재 스레드 할당이며 전체 게임 GC가 아닙니다.</p></section>')
sections.append('<section><h2>실제 Play 입력과 자세</h2><p>WASD2.2m/s · Ctrl5.5m/s · C웅크림1.2m/s · X착석 · Space점프 · Shift회피. C는 접지 상태에서 전환하며 작도·갈무리 중 무시합니다. 웅크림에서는 점프·달리기·회피 시작을 막습니다.</p>'+table(['단계','종료구간 속도','종료 웅크림','공중 프레임','종료 착석','작도 프레임'],liveRows))
for name in ['walk_forward','run','crouch_forward','crouch_draw','sit_hold']:
 p=livePath.parent/(name+'.png')
 if p.exists():sections.append(f'<figure><img loading="lazy" src="{rel(p)}"><figcaption>{name} — 가상 입력으로 정상 Play 루프를 실행해 촬영. 외부 시점은 별도 검토 카메라.</figcaption></figure>')
sections.append('</section><section><h2>손 중앙과 가장자리</h2><p>상단 모서리에서 손바닥과 소매가 크게 보이는 구도도 포함합니다. 실제 프레임을 보며 외형을 판단해 주세요. 근접 어깨 추가 이동은5cm로 제한했고, 팔·붓 길이와 원시 획을 늘리지 않았습니다.</p>')
for name in ['center','top-right','bottom-left']:
 candidates=list((O/'Hands/Validation/Unity/Screenshots').glob('*_synthetic_'+name+'.png'))
 if candidates:
  p=max(candidates,key=lambda p:p.stat().st_mtime);sections.append(f'<figure><img loading="lazy" src="{rel(p)}"><figcaption>{name} — 현재 Unity 모델의 합성 작도 포즈</figcaption></figure>')
sections.append('</section><section><h2>클립 대응표</h2>'+table(['원본','파생 클립','길이 s','재생률','보존'],motionRows)+'<p>기존 후진·좌우 걷기/달리기, 회피·점프·착석은 보존. Run With Sword에는 검 모델을 추가하지 않았습니다. 시작과90도 회전은 Motor 이동/회전을 지연시키지 않는 표현 전환입니다.</p></section>')
sections.append('<section><h2>원본과 전달물</h2><ul>'+''.join('<li>'+link(p,label)+'</li>'for p,label in [
 (O/'Hands/Work/Player_C02_GripA_Rebuilt.blend','손 수정 Blender'),(O/'Hands/Exports/Player_C02_GripA.fbx','손·캐릭터 FBX'),(O/'Motion/Player_C02_AttachedMotions.blend','모션 Blender'),(O/'Motion/Player_C02_AttachedMotions.fbx','모션 FBX'),(O/'Motion/clip_mapping.csv','클립 대응 CSV'),(O/'Hands/Validation/fbx_roundtrip.json','FBX 왕복 검사'),(O/'Hands/Validation/hand_surface_sampling.json','손·자루 표면 검사'),(O/'Motion/Unity/unity_synthetic_feet.json','발 검사39조건'),(O/'Motion/Unity/motor_state_checks.json','Motor 입력 상태 검사'),(O/'Performance/partition_identity.json','생성 분할·배치 동일성'),(O/'summary.json','통합 결과 JSON')])+'</ul><p>이전 검토 자료: <a href="../PlaytestPolish/REVIEW.html">PlaytestPolish</a>. 예전 자료와의 화면 위치/식생 로딩 상태가 같지 않은 이미지는 정량 전후 비교로 사용하지 않습니다.</p><p>Textures/의 패킹 텍스처와 Unity의 PlaytestRecoveryGripA·PlaytestRecoveryMotion 파생 프리팹/프로필을 함께 사용합니다.</p></section>')
if build:
 sections.append('<section><h2>최종 빌드 후보</h2><p>'+html.escape(build.get('status','상태 미확인'))+'</p><p>'+link(Path(build['executable']),'Windows 실행 파일')+' · '+link(Path(build['outputFolder'])/'release_build_report.json','빌드 원자료')+'</p><p>기존 빌드는 보존했습니다. 이번 통합 후보는 한 번 생성했으며 독립 실행 화면·전체 플레이 QA는 미검증입니다.</p></section>')
style='body{margin:0;background:#eee9dc;color:#272a25;font:16px/1.65 system-ui,sans-serif}header,section{max-width:1280px;margin:auto;padding:32px}header{border-bottom:3px solid #303d32}h1{font-size:38px}h2{font-size:26px}table{width:100%;border-collapse:collapse;font-size:13px}th,td{border-bottom:1px solid #ccc4b2;text-align:left;padding:8px}th{background:#d6d8c9}figure{margin:28px 0}img{width:100%;height:auto;display:block}figcaption{padding:8px;color:#52574f}a{color:#305d48}section{overflow-x:auto}p{max-width:1100px}'
(O/'REVIEW.html').write_text('<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>플레이테스트 복구 검토</title><style>'+style+'</style>'+''.join(sections)+'</html>',encoding='utf-8')
report=['# 식생 성능·A 손·첨부 모션 복구 보고서','',f'발 합성 검사: {len(feet["cases"])} / 최대 미끄러짐 {maxSlip*100:.2f}cm / 피부 관통 {maxPen*100:.2f}cm.',f'Motor 격리 검사: {len(motor["checks"])}개 / {motor["status"]}.',f'손 합성 검사: {hand["passed"]}개 / 붓끝 최대 {maxTip:.3f}px. 외형 승인과 별개.','', '## 성능','', '실제 Editor Play의 진단 카메라 측정이다. 캐릭터가 실제 셀 경계를 걸어 넘는 검사와 독립 빌드의 성능은 미검증. CPU/GPU8.33ms 목표는 미달. 정지 사전 생성 결과로 대체하지 않았다.','', '| 방식 | 해상도 높이 | 구간 | 프레임 p50 | p95 | p99 | CPU p50 | GPU p50 |','|---|---|---|---|---|---|---|---|']
for row in metrics:report.append('| '+' | '.join(map(str,row[:8]))+' |')
report+=['','## 남은 검사와 한계','','- 손/소매와 웅크림 상체의 최종 자연스러움은 사용자 검토 대상.','- 격리 낮은 천장/메뉴 입력 검사는 통과. 실제 장소의 저천장, 탑승·메뉴·사망 왕복 전체는 미검증.','- 발 검사39조건은 명시적 시간간격과 경사 평면의 합성 검사이며 실제 렌더 FPS별 전체 플레이가 아니다.','- 실제 Play 입력은 가상 장치로 주입한27초 로컬 검사다. 사람의 직접 입력/보행 완주로 표기하지 않았다.','- 1ms 생성은 후보별 재개 예산이다. 단일 후보/스케줄 갱신의 초과를 강제로 중단하지 않으므로 상한 초과값도 원자료에 남겼다.','- GC는 생성·패킷·제출 구간의 현재 스레드 계측이며 전체게임 GC는 별도 미검증.','- 중단/경쟁 작업이 있었던 ABORTED 성능 기록은 최종 비교에서 제외했다.','- 원본 FBX9개와 파생 입력 사본의SHA256을비교했다. source_preservation.json 참고.','- 빌드 후보 결과는 BuildRoot의 마지막 보고서와 summary.json을 확인한다. 최종 UI/차량/전체 콘텐츠 승인을 의미하지 않는다.','', '## 제작 경로','','Blender5.0.1 CLI로 C02 파생본을 수정·리타깃·재임포트했다. Unity6000.3.9f1은 직렬 Editor 명령 큐로 에셋 적용·Play 검사를 수행했다. 원본은 덮어쓰지 않았고 영상/자동 완주는 제작하지 않았다.','', '실제 전달물 및 스크린샷: [REVIEW.html](REVIEW.html).']
(O/'REPORT.md').write_text('\n'.join(report),encoding='utf-8')
print('REPORT_READY',O/'REVIEW.html')
