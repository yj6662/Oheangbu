"""Build a local still-image review from existing artifacts, never render or fabricate results."""
from pathlib import Path
import html, json, re

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/WorldMacro/MagicStoneCar'
def read(path):
    p=OUT/path
    return json.loads(p.read_text(encoding='utf-8-sig')) if p.exists() else None
def esc(value):return html.escape(str(value))
def number(value):return f'{value:,}' if isinstance(value,(int,float)) else '미기록'
def figure(path,title,note=''):
    metadata=Path(path).with_suffix('.json').as_posix()
    record=f' · <a href="{esc(metadata)}">촬영 상태 JSON</a>' if (OUT/metadata).exists() else ''
    if not (OUT/path).exists():
        state=(read(metadata) or {}).get('status','미촬영')
        reason='시스템 커밋 85% 기준에서 중단 · 새 렌더 시작 안 함' if state=='STOPPED_MEMORY' else '미촬영'
        return f'<figure class="missing"><div>{esc(reason)}</div><figcaption>{esc(title)}<small>{esc(state)} · {esc(note)}{record}</small></figcaption></figure>'
    return f'<figure><a href="{esc(path)}"><img src="{esc(path)}" loading="lazy" alt="{esc(title)}"></a><figcaption>{esc(title)}<small>{esc(note)}{record}</small></figcaption></figure>'
def link(path,title):return f'<a href="{esc(path)}">{esc(title)}</a>' if (OUT/path).exists() else f'<span>{esc(title)} — 미작성</span>'

DIAGNOSTICS=[
    ('setup_audit.json','Unity 설치·LOD·재질 연결','정적 구성 검사입니다. 화면 품질·주행·성능 합격을 뜻하지 않습니다.'),
    ('camera_checks.json','실행한 카메라·승하차 API','Play의 실제 API와 임시 콜라이더를 사용했습니다. 실제 E/V 키·전체 경로·승차감 검사는 포함하지 않습니다.'),
    ('drive_state_checks.json','구동·접촉·출발 상태','진단 코드 오류가 있으면 결과는 미완료입니다. 정리 성공을 차량 주행 통과로 계산하지 않습니다.'),
    ('physics_probe.json','한정 주행 진단','파일이 없으면 미검증입니다. 짧은 진단 결과를 전체 경로 주행이나 지정 FPS 검사로 확대하지 않습니다.'),
]

def diagnosis(path):
    d=read(path)
    if not d:return None,'미검증',[]
    checks=d.get('checks',[])
    state=d.get('status')
    if not state:
        states=[c.get('status') for c in checks]
        state='FAIL' if 'FAIL' in states else ('PASS_RECORDED_CHECKS' if states and all(s=='PASS' for s in states) else '미검증')
    return d,state,checks

def diagnostic_panel(path,title,note):
    d,state,checks=diagnosis(path)
    counts=' / '.join(f'{s} {sum(c.get("status")==s for c in checks)}' for s in ('PASS','FAIL')) if checks else '검사 항목 없음'
    details=''.join(f'<tr><td>{esc(c.get("name",""))}</td><td>{esc(c.get("status","미검증"))}</td><td class="wrap">{esc(c.get("detail",""))}</td></tr>' for c in checks)
    severity='error' if state in ('ERROR','FAIL') or any(c.get('status')=='FAIL' for c in checks) else ''
    return f'<article class="diagnostic {severity}"><h3>{esc(title)}</h3><p><strong>{esc(state)}</strong> · {esc(counts)}</p><p>{esc(note)}</p><small>기록 UTC: {esc((d or {}).get("utc","없음"))}</small>'+('<details><summary>실제 검사 항목</summary><div class="table"><table>'+details+'</table></div></details>' if checks else '')+f'<p>{link(path,"원본 JSON")}</p></article>'

def refresh_report_status():
    p=OUT/'REPORT.md'
    if not p.exists():return
    content=p.read_text(encoding='utf-8-sig')
    start,end='<!-- DIAGNOSTICS:START -->','<!-- DIAGNOSTICS:END -->'
    if start not in content or end not in content:return
    rows=['| 기록 | 상태 | 실제 항목 | 기록 UTC |','|---|---|---|---|']
    errors=[]
    for path,title,note in DIAGNOSTICS:
        d,state,checks=diagnosis(path)
        counts=f'PASS {sum(c.get("status")=="PASS" for c in checks)} / FAIL {sum(c.get("status")=="FAIL" for c in checks)}' if checks else '없음'
        label=f'[{title}]({path})' if d else title
        rows.append(f'| {label} | {state} | {counts} | {(d or {}).get("utc","없음")} |')
        for c in checks:
            if c.get('status')=='FAIL':errors.append(f'- {title}: {c.get("detail","").splitlines()[0]}')
    if errors:rows+=['','현재 실패/중단 기록:',*errors]
    drive=read('drive_state_checks.json') or {}
    if drive.get('status')=='PASS_BOUNDED_DRIVE_STATE':
        rows+=['',f'구동 상태 진단은 실제 앞·뒤 임시 벽 접촉, API 페달 유지·반복·후진, timeScale 일시정지를 검사했다. 최대 위치 변화 {drive.get("maximumDisplacementM",0):.3f}m, 앞/뒤 접촉 콜백 {drive.get("frontContacts",0)} / {drive.get("rearContacts",0)}회, 정지 중 실시간 {drive.get("pausedRealSeconds",0):.3f}초 / 게임시간 {drive.get("pausedGameSeconds",0):.3f}초다. 접촉 콜백 수는 고유 충돌 횟수가 아니다.']
    physics=read('physics_probe.json') or {}
    if physics.get('status')=='PASS_BOUNDED_VEHICLE_DIAGNOSTIC':
        rows+=['',f'한정 물리 진단은 {physics.get("durationGameSeconds",0):.3f}게임초, 시작점에서 최대 {physics.get("maxDisplacementM",0):.3f}m 이동, 최고 {physics.get("peakSpeedMps",0):.3f}m/s, 제동 후 {physics.get("finalBrakeSpeedMps",0):.6f}m/s를 기록했다. 차체 시각 기울기 최대 {physics.get("peakVisualBodyTiltDeg",0):.3f}도, 관측 최대 표본 간격 {physics.get("maximumObservedSampleIntervalSeconds",0):.3f}초다. 이 간격은 고정 FPS 시험을 뜻하지 않는다.']
    rows+=['','상태는 위 JSON에서 가져온 기록이다. `ERROR`는 진단 미완료이며, 정리 성공 항목만으로 구동을 통과 처리하지 않는다. 카메라 API 통과도 실제 키 입력·보행 완주·승차감 합격과 구분한다.']
    before,tail=content.split(start,1);_,after=tail.split(end,1)
    refreshed=before+start+'\n'+'\n'.join(rows)+'\n'+end+after
    camera=read('camera_checks.json') or {}
    if camera.get('status','').startswith('PASS_') and camera.get('checks'):
        refreshed=re.sub(r'(카메라의 (?:최신 )?한정 Play API 검사는 \*\*)\d+(개 항목을 실제 실행\*\*)',lambda m:m.group(1)+str(len(camera['checks']))+m.group(2),refreshed)
    p.write_text(refreshed,encoding='utf-8')

def build():
    refresh_report_status()
    ledger=read('Meshy/ledger.json') or {'requests':[]}
    spent=sum(r.get('consumed_credits') or 0 for r in ledger['requests'])
    rows=[]
    for r in ledger['requests']:
        g=next((f.get('geometry') for f in r.get('files',[]) if f.get('geometry')), {})
        values=[r['name'],r.get('status','미기록'),number(r.get('consumed_credits')),number(r.get('config',{}).get('target_polycount')),number(g.get('unique_mesh_triangles')),r.get('task_id','미기록')]
        rows.append('<tr>'+''.join(f'<td>{esc(v)}</td>' for v in values)+'</tr>')
    concepts=''.join(figure('Concepts/'+n+'.png',n,'Meshy 입력용 파츠 이미지 · 최종 게임 모델과 구분') for n in ('Cabin','Roof','Engine','Wheel'))
    views=''.join(figure(n+'.png',title,('임시 물리 정지벽을 둔 진단 촬영 · 실제 모터 토크에 의한 PATTERN 순간. 일반 자유 주행·키보드 입력 검사가 아님' if n=='ignition' else 'Unity 정적 구도 이미지 · 구도·실행 상태는 '+n+'.json 참조. 이미지 존재만으로 실제 조작·시야 합격을 판정하지 않음')) for n,title in [('exterior','새 자동차 외관'),('player','보행 숄더뷰'),('vehicle_shoulder','차량 숄더뷰'),('seated','운전석 구도'),('engine','마석 구동부'),('ignition','구동 상태의 출발 순간 · 진단')])
    diagnostics=''.join(diagnostic_panel(*entry) for entry in DIAGNOSTICS)
    setup=read('setup_audit.json') or {}
    installed=' / '.join(number(v) for v in setup.get('renderedTriangles',[])) or '미기록'
    surface=read('surface_look.json')
    surface_note=''
    if surface:
        surface_note='<p>차량 전용 PBR 재질에 국부 간접광 하한 '+f'{surface.get("localAmbientFloor",0):.2f}'+', 채도 '+f'{surface.get("saturation",0):.2f}'+', 매끄러움 맵 배율 '+f'{surface.get("mappedSmoothnessScale",0):.2f}'+'를 적용했습니다. 전역 환경광·반사 강도, 기존 가마, Meshy 텍스처와 물리 설정은 유지했습니다. 일반 목재·금속의 밝기 보정은 간접 확산광 경로이며 발광은 마석·등롱에만 사용합니다. '+link('surface_look.json','적용 기록')+'</p>'
    reports=[]
    for name,title in [('REPORT.md','기술·미완료 보고서'),('Meshy/ledger.json','요청별 Meshy 원장'),('Blender/REPORT.md','Blender 제작 단계 보고서'),('Blender/Assembly.json','Blender 조립 통계'),('Blender/DELIVERY.json','FBX 전달·왕복 기록'),('setup_audit.json','Unity 실제 배치 검사'),('surface_look.json','차량 전용 PBR·국부 간접광 적용'),('camera_checks.json','실행한 카메라·승하차 API 검사'),('drive_state_checks.json','구동·접촉·출발 상태 진단'),('physics_probe.json','한정 주행 진단'),('ignition.json','출발 효과 촬영 상태'),('engine_collider.json','전방 엔진 충돌체 추가'),('Concepts/PROMPTS.md','파츠 제작 프롬프트 기록'),('References/README.md','시점 참조 출처')]:reports.append('<li>'+link(name,title)+'</li>')
    blends=sorted((OUT/'Blender').glob('*.blend'))
    model_links=''.join('<li>'+link(str(p.relative_to(OUT)).replace('\\','/'),p.name)+'</li>' for p in blends)
    text='''<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>마석 자동차 · 새 제작본</title><style>
    :root{color-scheme:light;--paper:#e7e5dc;--ink:#263731;--line:#b9c1b5}*{box-sizing:border-box}body{margin:0;background:var(--paper);color:var(--ink);font:15px/1.75 system-ui,"Malgun Gothic",sans-serif}main{max-width:1600px;margin:auto;padding:38px}h1{font-size:42px;letter-spacing:-1.5px;font-weight:500;margin:8px 0}h2{font-size:24px;font-weight:500;margin-top:36px}a{color:#2f665d;text-underline-offset:3px}header,footer{border-bottom:1px solid var(--line);padding-bottom:18px}.eyebrow{font-size:11px;letter-spacing:3px}.notice{padding:14px 18px;background:#d4ddd0;border-left:3px solid #648377}.grid{display:grid;grid-template-columns:1fr 1fr;gap:22px}.parts{grid-template-columns:repeat(4,1fr)}figure{margin:0}img{width:100%;aspect-ratio:16/9;object-fit:contain;background:#c1c9bd;display:block}figcaption{font-size:14px;padding-top:7px}small{font-size:12px;display:block;opacity:.72}.missing>div{display:grid;place-content:center;aspect-ratio:16/9;background:#d0d5cb}.table{overflow:auto}table{width:100%;border-collapse:collapse;font-size:12px}td,th{text-align:left;padding:9px;border-bottom:1px solid var(--line);white-space:nowrap}details{border:1px solid var(--line);padding:12px 18px;margin-top:22px}footer{margin-top:38px;padding-top:20px;display:flex;gap:25px;flex-wrap:wrap}@media(max-width:900px){main{padding:22px}.grid,.parts{grid-template-columns:1fr}}
    .diagnostic{border:1px solid var(--line);padding:16px 20px}.diagnostic h3{margin:0}.diagnostic.error{border-left:4px solid #a05239}.wrap{white-space:normal;min-width:240px}.diagnostic details{margin-top:8px}
    </style></head><body><main><header><div class="eyebrow">OHEANGBU / MAGIC STONE CAR</div><h1>마석 자동차 · 새 제작본</h1><p>조선 가마의 기와·목재·단청과 앞 마석 구동부를 조합한 네 파츠 제작본입니다. 기존 가마와 검토 자료는 별도로 보존합니다.</p></header>
    <p class="notice">meshy-7 · Standard · PBR · 4K 요청 · 유료 재생성 없음. 확인된 소비 '''+str(spent)+''' / 사용자 상한120크레딧. 원본 생성·최종 모델·실행 검증은 아래에서 구분합니다.</p>
    <section><h2>설치 수치와 실행 검사</h2><p>실제 Unity LOD0 / LOD1 / LOD2는 <strong>'''+installed+''' tris</strong>, 설치 재질 '''+number(setup.get('materials'))+'''개입니다. 바퀴 4개·보완 프레임·등롱·추종 축을 포함합니다.</p><div class="grid">'''+diagnostics+'''</div></section>
    <section><h2>게임 내 외관과 카메라</h2><p>보행은 높이 1.75m·지름 0.56m·눈높이 1.62m 캡슐의 숄더뷰로 구성했습니다. 차량의 V 전환과 같은 API 경로를 검사했으며, 실제 키 입력·보행 완주·장거리 주행·최종 시야와 승차감은 별도 사용자 검토 항목입니다. 정적 구도 이미지와 Play API 검사는 구분합니다. 영상은 제작하지 않습니다.</p>'''+surface_note+'''<div class="grid">'''+views+'''</div></section>
    <section><h2>파츠 제작 이미지</h2><div class="grid parts">'''+concepts+'''</div><p>''' + link('References/Original_ThreeViews.png','사용자 원본 3뷰')+''' · '''+link('References/README.md','분리 파생 참조와 원본의 구분')+'''</p></section>
    <section><h2>요청과 실제 원본 통계</h2><div class="table"><table><thead><tr><th>파트</th><th>API 상태</th><th>소비 크레딧</th><th>요청 polycount</th><th>원본 GLB tris</th><th>작업 ID</th></tr></thead><tbody>'''+''.join(rows)+'''</tbody></table></div><p><small>원본 GLB 수치는 네 바퀴가 포함된 Unity 최종 조립 합계가 아닙니다. 최종 LOD0 상한50k / LOD1 24k / LOD2 12k 검사는 기술 보고서를 참조합니다.</small></p></section>
    <details open><summary>검사 기록과 편집 파일</summary><ul>'''+''.join(reports)+model_links+'''</ul><p>파일이 존재한다는 사실만으로 검사가 통과한 것으로 취급하지 않습니다. 통과·실패·미검증은 각 보고서의 실제 측정 결과를 따릅니다.</p></details>
    <footer>'''+link('../Dressing/REVIEW.html','5강토 환경 검토')+link('../Dressing/FINAL_STATUS.md','환경·차량 최종 전달 요약')+'''<a href="../Palanquin/REVIEW.html">보존한 이전 가마</a><a href="../REVIEW.html">전체 지도</a></footer></main></body></html>'''
    OUT.mkdir(parents=True,exist_ok=True);(OUT/'REVIEW.html').write_text(text,encoding='utf-8')
    print(OUT/'REVIEW.html')
if __name__=='__main__':build()
