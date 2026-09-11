from pathlib import Path
import json,html
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/SpellVFX120'
d=json.loads((OUT/'NIEUN_PROGRESS.json').read_text(encoding='utf-8'))
rows=[x for x in d['rows'] if 'review' in x]
for x in rows:
    for k in ['Integrated','External']:
        path=f'ClipsKTP{k}{x["version"]}/{x["id"]}.mp4'
        assert (OUT/path).is_file(),path
        x[k.lower()]=path
page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>ㄴ 술식 검토</title><style>body{background:#1c1a18;color:#eee5d7;font:16px/1.7 system-ui;margin:0}main{max-width:1180px;padding:30px;margin:auto}button,a{color:#e8ad72}button{background:#332720;border:1px solid #806046;border-radius:5px;padding:10px 18px;cursor:pointer;margin:4px}video{width:100%;background:#111}.media{display:grid;grid-template-columns:1fr 1fr;gap:18px}h1{font-size:32px}p{color:#c7bdb0}@media(max-width:760px){.media{grid-template-columns:1fr}}</style><main><h1>ㄴ · 화염과 문양</h1>'''
page+=f'<p>배정20종 중 {len(rows)}종 제작·개별 기술 검사 완료. 비주얼 판정은 사용자 검토 대기입니다. 게임 기능 연결과 VFX 자산 제작은 별도입니다.</p><nav>'
for i,x in enumerate(rows):page+=f'<button onclick="show({i})">{x["glyph"]}</button>'
page+='</nav><h2 id="name"></h2><p id="intent"></p><div class="media"><div><video id="a" controls muted loop playsinline preload="metadata"></video><p>기본 시점</p></div><div><video id="b" controls muted loop playsinline preload="metadata"></video><p>외부 시점</p></div></div><button onclick="playBoth()">함께 재생</button><p><a id="detail">전후 비교</a> · <a id="report">개별 보고서</a></p><h2>남은 술식</h2><p>'
page+=' · '.join(x['glyph'] for x in d['rows'] if x['assigned'] and 'review' not in x)
page+='</p><p>눅·눔·눗·눙은 의도적 미배정입니다.</p><p><a href="NIEUN_PROGRESS.json">진행 기록</a></p></main><script>const rows='+json.dumps(rows,ensure_ascii=False)+''';function show(i){let r=rows[i];document.getElementById('name').textContent=r.glyph;document.getElementById('intent').textContent=r.intent;for(let [id,key] of [['a','integrated'],['b','external']]){let v=document.getElementById(id);v.pause();v.src=r[key];}document.getElementById('detail').href=r.review;document.getElementById('report').href=r.report;}function playBoth(){for(let id of ['a','b']){let v=document.getElementById(id);v.currentTime=0;v.play().catch(()=>{});}}show(rows.length-1);</script></html>'''
(OUT/'NIEUN_REVIEW.html').write_text(page,encoding='utf-8')
print(json.dumps(dict(status='UPDATED',completed=len(rows),remaining=20-len(rows)),ensure_ascii=False))
