from pathlib import Path
import json,hashlib,html,re
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Art/World/Compact/Rebuild';D=OUT/'Arrival262'
checks=(D/'checks.txt').read_text(encoding='utf-8-sig');assert 'FAIL ' not in checks
protected={p:hashlib.sha256(Path(p).read_bytes()).hexdigest()==h for p,h in json.loads((D/'protected-before.json').read_text()).items()}
assert all(protected.values());(D/'protected-after.json').write_text(json.dumps(protected,indent=2))
catalog=json.loads((D/'catalog.json').read_text());count=len(catalog['Entries'])
page=f'''<!doctype html><html lang="ko"><meta charset="utf-8"><title>지역 도착 — 262</title><style>body{{margin:40px auto;max-width:1250px;padding:0 24px;background:#e8e3d7;color:#282d29;font:16px/1.75 sans-serif}}h1{{font-size:34px}}img{{width:100%;margin:12px 0 30px}}pre{{white-space:pre-wrap}}table{{border-collapse:collapse}}td,th{{padding:8px 20px;border-bottom:1px solid #aaa;text-align:left}}</style>
<h1>지역에 닿으면 이름만</h1><p>큰 강토부터 폐광·갱도·주막·사건 장소까지, 화면 상단에 이름 한 줄이 나타났다가 사라진다. 패널·부제·발견 개수·목적지 안내는 없다.</p>
<p>0.65초 진입 확인 → 0.5초 나타남 → 2.7초 유지 → 0.9초 사라짐. 작은 장소 우선, 경계 여유 5m, 재표시 간격 30초. 전투·작도·메뉴·휴식·대화 중에는 현재 장소의 알림만 보류한다.</p>
<p>아래는 실제 UI 위젯과 후보 장면을 오프스크린으로 렌더한 배치 검토다. 예시 이름을 주입한 화면이며 실제 이동 트리거 녹화는 아니다.</p>
<h2>청림 · 1920×1080</h2><img src="Arrival262/name-0.png"><h2>금표 주막 · 1920×1080</h2><img src="Arrival262/name-1.png"><h2>작은 장소 · 1280×720</h2><img src="Arrival262/name-2.png">
<h2>함께 연결한 탐험 기록</h2><p>실제 도착한 장소는 영구 사실과 지도 아이콘으로 함께 저장한다. 저장 실패 시 기록을 확정하지 않고 다시 시도한다. 기존 발견과 장비·진행·저장 슬롯은 유지한다. 발견 자체로 의뢰·보상·보스 진행을 완료하지 않는다. 미제작 지선의 이름과 아이콘, 텔레포트는 추가하지 않았다.</p>
<p>표시 영역 {count}개. 큰 강토 5개의 경계는 오행 방위에 따른 표시용 구획이다. 실제 지형/관문 변경이 아니다.</p>
<table><tr><th>장소</th><th>종류</th></tr>{''.join(f'<tr><td>{html.escape(e["Name"])}</td><td>{"강토" if e["Priority"]==0 else "갱도/작은 장소" if e["Priority"]>=40 else "현장"}</td></tr>' for e in catalog['Entries'])}</table>
<h2>검증 범위</h2><pre>{html.escape(checks)}</pre><p>원본 씬·일반 저장·기존 지형/경로 보호 해시 {len(protected)}개 일치. fixture/원자 파일 저장/오프스크린 UI 검사이며 실제 플레이, 이동 중 표시 타이밍과 최종 미술 판정은 미검증이다. Computer Use·Play·키/커서 조작 없이 진행했다.</p></html>'''
(OUT/'ARRIVAL_REVIEW.html').write_text(page,encoding='utf-8')
block=f'''<!-- arrival-262:start -->
**현재 UI/탐험 #262 — 지역 도착 이름 표시:** 후보 전용 카탈로그 {count}개 영역(강토 5개와 현장/갱도)을 연결했다. 상단 이름 한 줄만 4.1초 동안 나타나고 사라진다. 작은 장소 우선·0.65초 체류·5m 경계 여유·30초 재표시 간격, 전투/작도/대화/메뉴/휴식 보류를 적용했다. 큰 강토 구획은 표시용이며 지형/진행 관문은 바꾸지 않는다.

추가 탐험 기능: 실제 도착한 장소를 영구 사실과 지도 아이콘으로 전체 저장 성공 후 기록한다. 기존 기록을 보존하고 의뢰/보상/전투 완료나 텔레포트는 추가하지 않는다. 현재 자동 계약 검사 {checks.count('PASS ')}개 통과, 실제 UI 위젯의 1080p 2장/720p 1장 캡처. **수동 이동/실제 입력에서의 표시 타이밍·미술 판단은 미검증**이다. 원본 씬/일반 저장 보호 해시 일치. Computer Use 없이 후보를 저장된 Edit 상태로 남겼다.

[화면과 검사](C:/Users/yj666/Oheangbu/Art/World/Compact/Rebuild/ARRIVAL_REVIEW.html) · [사양](C:/Users/yj666/Oheangbu/Docs/Specs/SPEC-LOCATION-ARRIVAL-262.md). #261 숲/암반과 #260 전투의 후속 미검증 항목은 유지한다.
<!-- arrival-262:end -->
'''
for rel in ['Docs/BIBLE_INDEX.md','Docs/PROJECT_STATUS.md','Docs/Specs/SPEC-COMPACT-REBUILD.md','Docs/Plans/PLAN-COMPACT-REBUILD-NEXT.md','Docs/Handoff/HANDOFF-COMPACT-REBUILD.md','Docs/Plans/PLAN-BIBLE-IMPLEMENTATION-TRACKER.md']:
 p=ROOT/rel;t=p.read_text(encoding='utf-8-sig');t=re.sub(r'<!-- arrival-262:start -->.*?<!-- arrival-262:end -->\s*','',t,flags=re.S);p.write_text(block+'\n'+t,encoding='utf-8')
p=ROOT/'Docs/DECISIONS.md';t=p.read_text(encoding='utf-8-sig')
if '## D262 ' not in t:p.write_text(t+'\n\n## D262 — 지역 이름과 도착 기록 (2026-09-24)\n\n사용자 요청으로 큰 강토와 작은 사건 장소의 이름을 상단에 잠깐 표시한다. 이름 이외의 안내 패널은 없다. 작은 장소 우선/높이 범위/체류/경계 여유/재방문/상황별 보류를 적용한다. 도착 영구 사실과 지도 아이콘은 저장 성공 후 반영한다. 텔레포트·새 진행 관문·발견 보상은 넣지 않는다. 후보 전용 opt-in으로 원본 UI/저장을 보존한다.\n',encoding='utf-8')
print('Arrival review and six current documents updated; checks',checks.count('PASS '))
