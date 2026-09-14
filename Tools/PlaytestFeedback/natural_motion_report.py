"""Summarize recorded evidence without turning unrun checks into passes."""
import json,statistics,html
from pathlib import Path
from PIL import Image,ImageStat
R=Path(__file__).resolve().parents[2];O=R/'Art/PlaytestRecovery/NaturalLocomotion'
before=O/'Live/20260914-113624';after=O/'Live/20260914-114809'
old=json.loads((before/'live_input.json').read_text());new=json.loads((after/'live_input.json').read_text())
feet=json.loads((O/'Unity/unity_synthetic_feet.json').read_text());prepare=json.loads((O/'Unity/unity_natural_prepare.json').read_text());retarget=json.loads((O/'Motion/retarget_report.json').read_text())
rows=new['frames'];checks=[]
def check(name,ok,evidence):checks.append({'item':name,'status':'통과' if ok else '실패','evidence':evidence})
widths={}
for phase in ['walk_forward','run_forward']:
 a=[r['ankleWidth'] for r in old['frames'] if r['phase']==phase];b=[r['ankleWidth'] for r in rows if r['phase']==phase]
 widths[phase]={'beforeMeters':statistics.median(a),'afterMeters':statistics.median(b)}
check('전진 시 좌우 발 벌어짐 감소',all(v['afterMeters']<v['beforeMeters']*.8 for v in widths.values()),widths)
for phase in ['walk_forward','walk_back','walk_left','walk_right','run_forward','run_back','run_left','run_right','diagonal','reverse_diagonal']:
 sample=[r for r in rows if r['phase']==phase];speed=max(r['speed']for r in sample)
 check(phase+' 실제 이동 및 상태',any(r['state']=='Locomotion'for r in sample)and speed>(4.8 if phase.startswith('run')else 1.8),{'peakSpeed':speed,'frames':len(sample)})
jumps={}
for phase in ['jump','walk_jump']:
 sample=[r for r in rows if r['phase']==phase];index=rows.index(sample[0]);base=rows[index-1]['position']['y'];peak=max(r['position']['y']for r in sample)-base;states={r['state']for r in sample}
 jumps[phase]={'peakMetersAboveTakeoff':peak,'states':sorted(states)}
 check(phase+' 도약·낙하·실제 접지',{'JumpRise','JumpFall','Land'}<=states and .69<peak<.8 and sample[-1]['grounded'],jumps[phase])
check('웅크림·구르기 연결 유지',any(r['state']=='CrouchRoll'and r['rolling'] for r in rows)and any(r['state']=='Crouch'and r['crouching']for r in rows), '같은 Play 입력 검사에서 Crouch/CrouchRoll 재생')
check('발 보정의 뼈 길이 유지',max(r['legLengthError']for r in rows)<.0001, max(r['legLengthError']for r in rows))
check('합성 지면 30조건',all(c['status']=='PASS_LIMITS'for c in feet['cases']),{'cases':len(feet['cases']),'maximumSlipMeters':max(c['maxStanceSlipAfter']for c in feet['cases']),'maximumFootSkinPenetrationMeters':max(c['maxFootSkinPenetrationAfter']for c in feet['cases'])})
bakes=[json.loads(p.read_text())for p in (O/'Unity').glob('human_bake_*.json')]
check('C02 바인드·소스 자세 변환',len(bakes)==13 and all(d['status']=='EXACT_SOURCE_POSE_PASS'for d in bakes),{'clips':len(bakes),'maximumBindError':max(d['maximumAlignedBindDifference']for d in bakes)})
check('진단 입력·적·위치 복원',new['restored']and not new['error'],new['scope'])
images=[]
for p in after.glob('*.png'):
 im=Image.open(p);images.append({'file':p.name,'size':im.size,'variance':max(ImageStat.Stat(im).var)})
check('최종 캡처 1080p·유효 픽셀',len(images)>=18 and all(x['size']==(1920,1080) and x['variance']>1 for x in images),{'count':len(images),'singleCapture':True})
unverified=['사용자 직접 키보드·마우스 체감 검수와 최종 외형 승인','실제 경사 지형·계단 전 구간과 전투 피격·사망·차량·메뉴 복원 회귀','30/60/120fps 및 감속의 실제 전체 Play 루프: 이 조합은 합성 지면에서 검사','새 점프의 모든 옷 관통·팔/붓과 얼굴의 충돌. 빠른 동작에서 소매·자락은 후속 비주얼 검토 필요','빌드 검증: 이번에는 빌드를 만들지 않음']
report={'status':'IMPLEMENTED_WITH_RECORDED_LIMITS','before':str(before),'final':str(after),'profile':new['profile'],'checks':checks,'unverified':unverified,'widths':widths,'jumps':jumps,'source_ledger':'source_ledger.json'}
(O/'technical_checks.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
md=['# Mixamo 걷기·달리기·점프 교체 결과','', '새 파생본을 W_WorldMacro_Playtest 씬에 연결하고 저장했다. 손·회피·웅크린 구르기·제자리 회전의 기존 소스는 보존했다. 빌드와 영상은 만들지 않았다.','', '## 변경','', '- Mixamo Locomotion Pack의 걷기·달리기·양옆 이동·점프, 별도 후진 걷기·후진 달리기를 사용했다. 다운로드 원본과 SHA-256은 source_ledger.json에 남겼다.', '- C02의 넓은 휴식 다리 방향이 남던 회전 델타 전사를 다리 관절 방향 재구성으로 보완했다. 뼈 길이·바인드·외형 메시를 바꾸지 않았다.', '- 방향별 왼발 주기를 정렬하고 C02 지지 발 이동으로 재생률을 보정했다. 첫 후진 걷기 후보는 짧은 보폭 때문에 제외했다.', '- 점프 source 46–64프레임 도약 /64–80 낙하 /80–120 착지. 긴 사전 준비는 제외했고 소스의 공중 루트 상승을 제거했다. Motor 수직 속도로 도약·낙하 자세를, 실제 접지로 착지 전환을 제어한다.', '- 새 선 상태 모션에서는 교정된 뒤꿈치→발끝 움직임을 보존하며 발목 고정을 적용하지 않는다. 지면 법선 차이·관통 보정은 유지한다. 도달 범위 클램프가 발목을 과하게 들어올리지 않도록 수평 도달 범위를 해당 높이에서 제한한다.','', '## 실제 Play 결과','', '| 항목 | 이전 | 최종 |','|---|---:|---:|']
for p,label in [('walk_forward','전진 걷기 좌우 발목 간격 중앙값'),('run_forward','전진 달리기 좌우 발목 간격 중앙값')]:
 v=widths[p];md.append(f"|{label}|{v['beforeMeters']*100:.1f}cm|{v['afterMeters']*100:.1f}cm|")
md+=['','간격은 같은 입력 구간 전체의 월드 발목 위치를 플레이어 좌우축으로 투영한 중앙값이다. 발 길이나 보폭 길이를 의미하지 않는다. 이전 실행 후반에는 적이 개입하여 일부 화면을 가렸다. 최종 실행에서는 진단 범위에서 적을 일시 정지하고 복원했다.',f"실제 제자리 점프 상승 {jumps['jump']['peakMetersAboveTakeoff']:.3f}m / 이동 점프 상승 {jumps['walk_jump']['peakMetersAboveTakeoff']:.3f}m. 설정 목표 0.75m 유지.",'','## 검사','', '|상태|항목|','|---|---|']
md += [f"|{c['status']}|{c['item']}|"for c in checks]
md +=['','합성 지면 검사는 전진 걷기·달리기에 30/60/120fps × 1배/0.2배 시간 × 0/12도 경사를 적용했고, 60fps·1배·평지에서는 전후좌우 총8개 동작을 검사했다. 총30조건이다. 실제 월드 경사 주행 통과를 의미하지 않는다.',f"최대 합성 지지 발 미끄러짐 {max(c['maxStanceSlipAfter']for c in feet['cases'])*100:.2f}cm, Foot+Toes 가중치 메시 관통 {max(c['maxFootSkinPenetrationAfter']for c in feet['cases'])*1000:.2f}mm.",'','## 미검증·후속','']+['- 미검증: '+x for x in unverified]+['','## 에셋 대응','', '|Unity 클립|Mixamo 소스|','|---|---|']
md += [f"|{c['name']}|{Path(c['source']).name}|"for c in retarget['clips'] if c['name']!='JumpFull']
md += ['|StartForward|walking.fbx의 정렬된 첫 0.2초|','','Blender: Motion/Player_C02_NaturalLocomotion.blend','FBX: Motion/Player_C02_NaturalLocomotion.fbx','Unity: Assets/_Project/Art/Characters/PlaytestNaturalLocomotion','입력 기록: Live/20260914-114809/live_input.json']
(O/'REPORT.md').write_text('\n'.join(md),encoding='utf-8')
labels={'idle':'정지','walk_forward':'전진 걷기','walk_back':'후진 걷기','walk_left':'왼쪽 걷기','walk_right':'오른쪽 걷기','run_forward':'전진 달리기','run_back':'후진 달리기','run_left':'왼쪽 달리기','run_right':'오른쪽 달리기','diagonal':'대각선','jump_rise':'점프 도약','jump_apex':'점프 정점','jump_fall':'점프 낙하','jump_contact':'착지','walk_jump_apex':'이동 점프','crouch_walk':'기존 웅크림 이동','roll':'기존 웅크린 구르기'}
cards=''.join(f'<figure><a href="Live/{after.name}/{name}.png"><img loading="lazy" src="Live/{after.name}/{name}.png"></a><figcaption>{label}</figcaption></figure>'for name,label in labels.items()if(after/f'{name}.png').exists())
rowsHtml=''.join(f"<tr><td>{c['status']}</td><td>{html.escape(c['item'])}</td></tr>"for c in checks)
page=f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>걷기·달리기·점프 — Mixamo 교체</title><style>body{{margin:0;background:#171816;color:#ece5d4;font-family:system-ui,sans-serif;line-height:1.75}}main{{max-width:1360px;margin:auto;padding:32px}}h1{{font-size:32px}}a{{color:#d1c5a2}}.summary{{background:#262924;padding:20px;border-left:4px solid #9eac81}}.grid{{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:20px}}figure{{margin:0;background:#262924}}img{{width:100%;display:block}}figcaption{{padding:12px}}table{{border-collapse:collapse;width:100%}}td,th{{text-align:left;padding:8px;border-bottom:1px solid #44483d}}@media(max-width:760px){{.grid{{grid-template-columns:1fr}}}}</style><main><h1>걷기·달리기·점프 — Mixamo 교체</h1><p>2026.09.14 · 실제 플레이 씬 적용 · 원본 보존 · 빌드/영상 없음</p><div class="summary"><b>다리의 기본 벌어짐을 줄이고 새 이동 클립을 연결했습니다.</b><br>발목 간격 중앙값: 걷기 {widths['walk_forward']['beforeMeters']*100:.1f} → {widths['walk_forward']['afterMeters']*100:.1f}cm / 달리기 {widths['run_forward']['beforeMeters']*100:.1f} → {widths['run_forward']['afterMeters']*100:.1f}cm.<br>합성 접지 검사 30조건 통과. 실제 입력 경로에서 8방향 이동·제자리/이동 점프·착지와 구르기 연결 확인.</div><p>WASD 걷기 · Ctrl 달리기 · Space 점프 · C 웅크림 · X 착석 · Shift 회피/웅크린 구르기.<br>최종 캡처는 적을 잠시 비활성화한 별도 저장 슬롯의 실제 Play 동작입니다. 적은 검사 후 복원했습니다. 직접 플레이와 전투 검증은 별도입니다.</p><p><a href="REPORT.md">상세 보고서</a> · <a href="technical_checks.json">검사 원장</a> · <a href="source_ledger.json">Mixamo 원본 원장</a> · <a href="../DodgeTurn/REVIEW.html">이전 회피·회전 결과</a></p><h2>최종 동작 — 1920×1080</h2><p>이미지를 누르면 원본 크기로 엽니다. 자연스러움의 최종 판단은 실제 플레이에서 확인해 주세요. 손은 현재 잠정 승인 상태를 유지했습니다.</p><div class="grid">{cards}</div><h2>기술 검사</h2><table>{rowsHtml}</table><h2>미검증·후속</h2><ul>{''.join('<li>'+html.escape(x)+'</li>'for x in unverified)}</ul><p>초기 전후 자료: <a href="Live/{before.name}/walk_forward.png">이전 걷기</a> · <a href="Live/{before.name}/run_forward.png">이전 달리기</a>. 최종 확대 캡처와 카메라 거리가 다릅니다. 이동 간격 수치는 기록된 관절 위치로 비교했습니다.</p></main></html>'''
(O/'REVIEW.html').write_text(page,encoding='utf-8')
print(json.dumps({'report':str(O/'REPORT.md'),'checks':len(checks),'failed':[c['item']for c in checks if c['status']=='실패'],'widths':widths},ensure_ascii=False))
