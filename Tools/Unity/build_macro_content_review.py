"""Build the content layout review from Unity's exported placements and actual captures."""
import csv
import html
import json
import re
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/WorldMacro/Content'
load = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
placement = load(OUT/'placement.json')
geo = load(OUT.parent/'sheet.json')
entries = placement['entries']
kinds = ['일반 적', 'NPC', '물증·조사', '이벤트', '던전', '보스', '휴식 후보', '재방문 기믹']
regions = {'Cheongrim':'청림','Jeokro':'적로','Cheolong':'철옹','Hyeongang':'현강','Hwanggyeong':'황경'}
shots = {'geumpyo_inn':'금표 주막 주변 · 휴식 후보', 'jeongdam_j1':'역참 · 정담',
         'logging_dungeon':'벌목장 창고 · 외부 구조', 'logging_dungeon_interior':'벌목장 창고 · 1.65m 눈높이',
         'fire_line':'적로 · 화 속성 적 위치', 'smith':'철옹 · 대장장이',
         'refugee_elder':'현강 · 피난민 장로', 'temple_legacy':'현강 사찰 · 던전 구조',
         'south_gate_boss':'황경 남문 · 관문 보스 위치'}

# Compare existing serialized scene records, excluding only the root registry that must gain the new root.
def records(path):
    text = path.read_text(encoding='utf-8-sig')
    parts = re.split(r'(?m)(?=^--- !u!)', text)
    result = {}
    for part in parts:
        match = re.match(r'--- !u!(\d+) &(-?\d+)', part)
        if match: result[match[2]] = (match[1], part)
    return result

before = records(OUT/'BeforeContent.unity')
after = records(ROOT/'Oheangbu/Assets/_Project/Scenes/World/W_WorldMacro_Blockout.unity')
changed = [key for key,(typ,text) in before.items() if typ!='1660057539' and after.get(key)!=(typ,text)]
def without_dressing_counters(text):
    return re.sub(r'(?m)^  (?:ResidentCells|ResidentInstances|DrawCalls|DrawnInstances|LastGenerationMilliseconds): .*$', '', text)
substantive = [key for key in changed if key not in after or without_dressing_counters(before[key][1])!=without_dressing_counters(after[key][1])]
preserved = {'baselineRecords':len(before), 'changedExistingExceptSceneRoots':changed,
             'changedExcludingDressingDiagnosticCounters':substantive,
             'newRecords':len(set(after)-set(before)), 'meaning':'Existing scene records compared to pre-content snapshot. SceneRoots registry excluded. Dressing counters are reset statistics, not placement changes.'}
(OUT/'scene_preservation.json').write_text(json.dumps(preserved,indent=2),encoding='utf-8')

with (OUT/'placements.csv').open('w',encoding='utf-8-sig',newline='') as f:
    writer=csv.writer(f);writer.writerow(['ID','이름','지역','종류','진행단계','기준지점','X','Y','Z','Bible','검토내용'])
    for e in entries:
        p=e['Position'];writer.writerow([e['Id'],e['Label'],regions.get(e['Realm'],e['Realm']),kinds[e['Kind']],e['Stage'],e['Anchor'],*[round(p[a],2) for a in ('x','y','z')],e['Source'],e['Text']])

captures=[]
for key,title in shots.items():
    path=OUT/(key+'.png')
    if path.exists():
        meta=load(OUT/(key+'.json'));captures.append({'id':key,'title':title,'commit':meta['commitRatio']})
play=(OUT/'play_checks.txt').read_text(encoding='utf-8') if (OUT/'play_checks.txt').exists() else '미검증 — Play 검사 기록 없음'
checks='\n'.join('- '+c for c in placement['checks'])
counts=Counter(e['Realm'] for e in entries)
report=f'''# 5강토 콘텐츠 위치 초안

State: TEST · 2026-09-12 · 바이블 및 Spec을 참고한 위치·동선 검토본

## 실제 구현

- 위치 {len(entries)}곳, 적·보스·던전 내부 캡슐 합계 {placement['enemyObjects']}개, NPC {placement['npcs']}명, 조사·이벤트 {placement['events']}곳, 던전 {placement['dungeons']}곳.
- 지역별 지점: {', '.join(regions.get(k,k)+' '+str(v) for k,v in counts.items())}.
- 매크로 씬 `WorldMacro_Content_Layout` 아래에 지역별 실제 오브젝트를 만들었다. 모든 지점은 영속 ID, 실제 좌표, 서사 단계, 기준 장소와 Bible Section ID를 가진다.
- NPC·물증은 보행 중 가까이 가서 F로 설명을 확인한다. 왕소→화물 계약→성저 인도는 세션 안의 선행 확인만 검사하는 미리보기다. 반복 확인은 중복 진행을 만들지 않는다.
- 적은 무속성 또는 단일 속성 캡슐이다. 역할·활동 반경·위치만 저작했으며 기존 전투 AI를 붙이지 않았다. 보스 또한 위치 및 공간 예약이다.
- 던전은 지형을 뚫지 않은 지상 모듈 초안이다. 12m 방 3개, 폭4m 연결 통로, 12m 진입 경사로, 방별 적 후보와 마지막 조사대를 포함한다. 묘역은 천장이 열려 있다. 창고·굴·묘역의 완성 미술이나 지하 레벨은 아니다.
- 근거리 지점 220m, 던전·보스 500m 범위에서 표시한다. 지점별 Update 대신 관리자 하나가 0.2초 간격으로 갱신한다. 신규 지점 주변만 절차 식생 제외 영역으로 추가했다.

## 서사 해석과 보존

- 청림의 폐광 조사, 금표 주막, 벌목장 금속 덩굴 예습, 심부·뿌리굴, 신목 경계 경고와 청룡을 배치했다. 정담은 역참, 왕소는 객주 분소에 둔다.
- 적로는 화재 전장·전쟁 무덤·서로 다른 증언, 철옹은 군수 창고·망명 흔적·백호, 현강은 침수 폐허·현무·사찰·현운, 황경은 성저 인도·남문·도성 기록·왕궁 순서의 후보를 구성했다.
- 장영실 관련 작업 흔적의 철옹 배치는 제작 해석이다. 바이블에서 미정인 지역을 확정 설정으로 승격하지 않는다. 후반 인물과 보스는 단계 메타데이터를 붙인 검토 대상이며 본편에서 동시에 활성화되는 설정이 아니다.
- 청림 본선에 국 요구를 추가하지 않았다. 국·눈·숫·웅·뭄 관련 지점은 재방문 공간의 안내 후보다. 휴식 지점은 서비스 위치 표시이며 본편 회복·저장 기능을 새로 연결하지 않았다.
- 지형·도로·하천·자동차·기존 건축물의 재생성을 실행하지 않았다. 실제 건축물과 물 표면을 먼저 검사했고, 현강 창고가 기존 성벽에 겹치는 문제를 위치 이동으로 수정했다.
- 기존 씬 레코드 보존 비교: SceneRoots 등록 목록 제외 변경 {len(changed)}건 / 기준 레코드 {len(before)}개. 식생 진단 카운터 초기화를 제외한 실질 변경 {len(substantive)}건. 상세는 `scene_preservation.json`. 기존 C2·프롤로그·PlayerRig·SpellBook 등 8파일은 별도 해시 검사에서 모두 일치했다.

## 검사 결과

{checks}

통로 검사는 높이1.75m·지름0.56m 캡슐의 공간 표본이다. 사용자의 실제 보행 완주나 경사로 이동 성공을 대신하지 않는다.

### 실제 Play Mode 호출

```
{play.strip()}
```

### 미검증·미연결

- 실제 F키 입력, 플레이어의 전체 접근·던전 완주, 적 순찰·추격·전투, 보스전.
- 본편 퀘스트·보상·영구 저장·휴식·재방문 능력 판정, 후반 진행 상태별 출현/퇴장.
- 이번 콘텐츠 추가 후 독립 실행 120fps 성능. 과거 식생 측정값을 이번 결과로 재사용하지 않는다.
- 던전 외부에서 먼 가도까지의 완전한 접근로, 최종 건축물 미술과 지하 굴착. 표시 위치는 사용자 보행 검토 후 다듬을 수 있다.

## 검토 자료

- 실제 Unity 정지 이미지 {len(captures)}장, 1920×1080. 영상과 자동 보행 완주는 제작하지 않았다.
- 촬영 응답의 최대 시스템 커밋 {max((c['commit'] for c in captures),default=0):.1%}; 각 촬영 전 85% 중단 기준 적용.
- `REVIEW.html`: 종류·지역·단계 필터가 있는 실제 좌표 지도, 지점별 설명, 스크린샷.
- `placements.csv`, `placement.json`: 파츠 이름이 아닌 실제 콘텐츠 위치와 출처 목록.
- `Content.asset`: 저작 데이터. `BeforeContent.unity`: 새 배치 전 매크로 씬 사본.
'''
(OUT/'REPORT.md').write_text(report,encoding='utf-8')
payload=json.dumps({'entries':entries,'geo':geo,'regions':regions,'kinds':kinds},ensure_ascii=False).replace('</',r'<\/')
gallery=''.join(f'<figure><a href="{c["id"]}.png"><img loading="lazy" src="{c["id"]}.png" alt="{html.escape(c["title"])}"></a><figcaption>{html.escape(c["title"])}</figcaption></figure>' for c in captures)
page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>5강토 · 콘텐츠 위치 검토</title>
<style>*{box-sizing:border-box}body{margin:0;background:#171c1b;color:#e4dfce;font:16px/1.7 system-ui,sans-serif}header,main{max-width:1450px;margin:auto;padding:25px}h1{font-size:30px;margin:0}p{color:#bfbbaa}a{color:#c6decb}nav{display:flex;gap:20px;flex-wrap:wrap}button,select,input{font:inherit;background:#28312d;color:#e4dfce;border:1px solid #586158;border-radius:5px;padding:6px}button{cursor:pointer}.controls{display:flex;gap:16px;flex-wrap:wrap;margin:18px 0}.layout{display:grid;grid-template-columns:2fr 1fr;gap:20px}svg{width:100%;height:720px;background:#252b28;border:1px solid #586158}aside{background:#222a26;padding:20px;border-radius:8px}aside p{white-space:pre-line}circle{cursor:pointer}circle:hover{stroke:white;stroke-width:8px;vector-effect:non-scaling-stroke}table{width:100%;border-collapse:collapse}td,th{padding:9px;text-align:left;border-bottom:1px solid #39413a}.list{max-height:600px;overflow:auto}.gallery{display:grid;grid-template-columns:1fr 1fr;gap:18px}figure{margin:0}img{width:100%;display:block}figcaption{padding:8px;color:#bfbbaa}small{color:#aaa997}.tag{background:#364638;padding:3px 9px}code{overflow-wrap:anywhere}.pointLabel{paint-order:stroke;stroke:#252b28;stroke-width:5px;fill:#e8e3d4;font-size:100px;pointer-events:none}@media(max-width:800px){.layout,.gallery{grid-template-columns:1fr}svg{height:600px}}</style>
<header><span class="tag">TEST · 위치와 던전 구조 검토</span><h1>5강토에 놓인 이야기와 조우</h1><p>83개 지점을 실제 매크로 씬에 배치했습니다. 적은 캡슐, NPC·물증은 F키 확인, 던전은 출입 가능한 모듈 초안입니다. 본편 전투·보상·저장은 미연결입니다.</p><nav><a href="REPORT.md">검증 보고서</a><a href="placements.csv">전체 좌표 CSV</a><a href="placement.json">Unity 내보내기</a><a href="scene_preservation.json">기존 씬 보존</a><a href="../REVIEW.html">전체 지형 검토</a></nav></header><main>
<div class="controls"><select id="realm"><option value="">5강토 전체</option></select><select id="kind"><option value="">모든 종류</option></select><select id="stage"><option value="">모든 서사 단계</option></select><input id="search" aria-label="지점 검색" placeholder="이름·ID 검색"><button id="reset">전체 지도</button><button id="zoomIn">확대</button><button id="zoomOut">축소</button></div>
<div class="layout"><svg id="map" viewBox="-4100 -6100 8200 12200" role="img" aria-label="실제 지형과 콘텐츠 위치 지도"><g id="base"></g><g id="points"></g></svg><aside id="detail"><h2>지도와 목록에서 지점을 선택하세요</h2><p>클릭하면 주변 지형을 확대합니다. 길은 가도·도보망, 파란 선은 수계입니다. 지점의 색은 속성이 아니라 콘텐츠 종류를 나타냅니다.</p><p>검토용 이동: Unity 메뉴 Tools → 오행부 → 전체맵 콘텐츠 위치에서 이름을 검색하고 보행 위치를 누르면 해당 지점 앞으로 이동합니다. 사용자 보행은 기존 WASD·우클릭 시선, 차량 E/V 조작을 유지합니다.</p></aside></div><h2>실제 배치 목록 <small id="count"></small></h2><div class="list"><table><thead><tr><th>지점</th><th>지역</th><th>종류</th><th>단계</th></tr></thead><tbody id="rows"></tbody></table></div><h2>Unity 정지 이미지</h2><p>주변 풍경·설치 위치와 던전의 공간 크기를 확인하는 자료입니다. 모듈 내부는 최종 미술이 아닙니다.</p><div class="gallery">__GALLERY__</div></main>
<script>const data=__DATA__;const $=id=>document.getElementById(id),ns='http://www.w3.org/2000/svg';const colors=['#d39377','#9bd6ad','#dbc989','#e3a7d2','#d5b174','#ed776c','#a9ccd1','#ada9db'];let selected='',vb=[-4100,-6100,8200,12200];function elem(tag,attrs,parent){let n=document.createElementNS(ns,tag);Object.entries(attrs).forEach(([k,v])=>n.setAttribute(k,v));parent.append(n);return n}function xy(p){return p.x+','+(-(p.z??p.y))}elem('polygon',{points:data.geo.Outline.map(xy).join(' '),fill:'#343d33',stroke:'#93957b','stroke-width':15},$('base'));for(const r of data.geo.Regions){elem('polygon',{points:r.Polygon.map(xy).join(' '),fill:'none',stroke:'#657660','stroke-width':10,'stroke-dasharray':'35 35'},$('base'))}for(const r of data.geo.Ridges)elem('polyline',{points:r.Points.map(xy).join(' '),fill:'none',stroke:'#505344','stroke-width':45,'stroke-linecap':'round'},$('base'));for(const r of data.geo.Rivers)elem('polyline',{points:r.Points.map(xy).join(' '),fill:'none',stroke:'#638a98','stroke-width':r.Width},$('base'));for(const r of data.geo.Routes)elem('polyline',{points:r.Points.map(xy).join(' '),fill:'none',stroke:r.Carriage?'#b0a589':'#888e6b','stroke-width':r.Carriage?16:9},$('base'));
Object.entries(data.regions).forEach(([k,v])=>$('realm').add(new Option(v,k)));data.kinds.forEach((v,i)=>$('kind').add(new Option(v,i)));[...new Set(data.entries.map(e=>e.Stage))].forEach(v=>$('stage').add(new Option(v,v)));function visible(){return data.entries.filter(e=>(!$('realm').value||e.Realm===$('realm').value)&&(!$('kind').value||e.Kind===$('kind').value*1)&&(!$('stage').value||e.Stage===$('stage').value)&&(e.Label+e.Id).toLowerCase().includes($('search').value.toLowerCase()))}function zoom(){ $('map').setAttribute('viewBox',vb.join(' '));draw()}function choose(e){selected=e.Id;vb=[e.Position.x-350,-e.Position.z-350,700,700];let a=$('detail');a.replaceChildren();let h=document.createElement('h2');h.textContent=e.Label;a.append(h);for(const t of [data.regions[e.Realm]+' · '+data.kinds[e.Kind]+' · '+e.Stage+' [TEST]',e.Text,'기준 지점: '+e.Anchor,'월드 좌표(m): '+['x','y','z'].map(k=>e.Position[k].toFixed(1)).join(' / '),'근거: '+e.Source,'ID: '+e.Id,'실제 게임 연결은 보고서의 미검증·미연결 항목을 확인하세요.']){let p=document.createElement('p');p.textContent=t;a.append(p)}zoom()}
function draw(){let list=visible();$('count').textContent=list.length+'곳';$('points').replaceChildren();$('rows').replaceChildren();for(const e of list){let c=elem('circle',{cx:e.Position.x,cy:-e.Position.z,r:vb[2]/180,fill:colors[e.Kind],stroke:selected===e.Id?'white':'#1b211c','stroke-width':vb[2]/1100},$('points'));elem('title',{},c).textContent=e.Label;c.onclick=()=>choose(e);let tr=document.createElement('tr');for(const [i,t]of [e.Label,data.regions[e.Realm],data.kinds[e.Kind],e.Stage].entries()){let td=document.createElement('td');if(i===0){let b=document.createElement('button');b.textContent=t;b.onclick=()=>choose(e);td.append(b)}else td.textContent=t;tr.append(td)}$('rows').append(tr)}}['realm','kind','stage','search'].forEach(id=>$(id).addEventListener('input',draw));$('reset').onclick=()=>{vb=[-4100,-6100,8200,12200];zoom()};function scale(s){vb=[vb[0]+vb[2]*(1-s)/2,vb[1]+vb[3]*(1-s)/2,vb[2]*s,vb[3]*s];zoom()}$('zoomIn').onclick=()=>scale(.6);$('zoomOut').onclick=()=>scale(1.6);draw();</script></html>'''
page=page.replace('__GALLERY__',gallery).replace('__DATA__',payload)
(OUT/'REVIEW.html').write_text(page,encoding='utf-8')
print(json.dumps({'entries':len(entries),'captures':len(captures),'scenePreservation':preserved},ensure_ascii=False))
