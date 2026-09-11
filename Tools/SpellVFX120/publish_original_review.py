from pathlib import Path
import json,html,urllib.request,urllib.parse
o=Path('Art/SpellVFX120');families=[('나','화'),('가','목'),('마','토'),('사','금'),('아','수')];labels=['발동','명중 접점','소환진','방어 연출'];rows=[]
for glyph,element in families:
 d=json.loads((o/'OriginalReview'/glyph/'report.json').read_text(encoding='utf-8'));assert d['status']=='PASS_PREVIEW_ONLY_USER_VISUAL_PENDING'
 for role in range(4):
  path=f'OriginalReview/{glyph}/role_{role}.mp4';assert (o/path).is_file()
  with urllib.request.urlopen('http://127.0.0.1:8771/'+urllib.parse.quote(path)) as response:assert response.status==200
 rows.append(dict(glyph=glyph,element=element,roles=d['roles']))
page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>KTP 원본 연출 검토</title><style>body{background:#191816;color:#eee6d8;font:16px/1.7 system-ui;margin:0}main{max-width:1150px;margin:auto;padding:32px}button{padding:10px 24px;margin:4px;background:#403125;color:#f2d3a8;border:1px solid #897158;border-radius:6px;cursor:pointer}.grid{display:grid;grid-template-columns:1fr 1fr;gap:20px}video{width:100%;background:#0c0c0c}a{color:#e3b47d}p{color:#c9c0b3}@media(max-width:750px){.grid{grid-template-columns:1fr}}</style><main><h1>KTP 원본 연출</h1><p>본체는 유지하고 속성색만 변경했습니다. 원본 문양·입자 계층·재생 곡선을 확인하는 분리 촬영입니다. 실제 게임 입력 시전 영상이나 비주얼 합격 판정은 아닙니다.</p><nav>'''
page+=''.join(f'<button onclick="show({i})">{element}</button>' for i,(glyph,element) in enumerate(families))
page+='</nav><h2 id="element"></h2><div class="grid">'+''.join(f'<section><h3>{label}</h3><video id="v{i}" controls muted loop playsinline preload="metadata"></video><p id="s{i}"></p></section>' for i,label in enumerate(labels))+'</div><button onclick="playAll()">함께 재생</button><p>받아치기는 확정 접점에서 속성별 Explosion 원본을 사용합니다. 방어 연출 영상은 지속 결계의 Bottom 원본입니다. 실제 피해 연결은 기존 구현된 술식에 적용되며, 미구현 술식의 게임 규칙은 추가하지 않았습니다.</p><p><a href="ORIGINAL_KTP_REPORT.md">변경·검증 보고서</a> · <a href="ORIGINAL_KTP_PROGRESS.json">100개 설정 기록</a></p></main><script>const rows='+json.dumps(rows,ensure_ascii=False)+''';function show(i){const r=rows[i];document.getElementById('element').textContent=r.element;for(let k=0;k<4;k++){let v=document.getElementById('v'+k);v.pause();v.src='OriginalReview/'+r.glyph+'/role_'+k+'.mp4';let m=r.roles[k];document.getElementById('s'+k).textContent='입자 시스템 '+m.systems+'개 · 촬영 중 최대 활성 입자 '+m.peak+'개';}}function playAll(){for(let k=0;k<4;k++){let v=document.getElementById('v'+k);v.currentTime=0;v.play().catch(()=>{});}}show(0);</script></html>'''
(o/'ORIGINAL_KTP_REVIEW.html').write_text(page,encoding='utf-8')
print('20 videos linked and HTTP checked')
