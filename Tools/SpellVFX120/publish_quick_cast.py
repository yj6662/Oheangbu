import json,subprocess,hashlib,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/SpellVFX120/Emphasis4'
ff=next((Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries').glob('*.exe'))
rows=[]
for glyph in '가나거너':
    r=json.loads((OUT/(glyph+'_v4')/'report.json').read_text(encoding='utf-8'))
    if r['status']!='PASS_RUNTIME_FIXTURE_VISUAL_PENDING':raise RuntimeError(r)
    for clip in r['clips']:
        frames=Path(clip['folder']);mp4=frames.with_suffix('.mp4')
        if not mp4.exists() or mp4.stat().st_mtime<max(x.stat().st_mtime for x in frames.glob('*.jpg')):
            subprocess.run([str(ff),'-y','-v','error','-threads','2','-framerate','24','-i',str(frames/'%04d.jpg'),'-c:v','libx264','-threads','2','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(mp4)],check=True)
            subprocess.run([str(ff),'-v','error','-threads','2','-i',str(mp4),'-f','null','-'],check=True)
        for label,index in [('cast',1),('contact',round((clip['actualHitAt']+.125)*24))]:
            shutil.copyfile(frames/f'{index:04d}.jpg',frames.parent/(frames.name+'_'+label+'.jpg'))
        print(mp4,flush=True)
    rows.append(r)
hashes=json.loads((OUT/'before_hashes.json').read_text(encoding='utf-8'))
changed=[]
for path,previous in hashes.items():
    with (ROOT/path).open('rb') as stream:current=hashlib.file_digest(stream,'sha256').hexdigest()
    if current!=previous:changed.append(path)
expected={f'Oheangbu/Assets/_Project/Art/SpellVFX120/Profiles/{name}.asset' for name in ['001_AC00','025_B098','007_AC70','031_B108']}
if {p.replace('\\','/') for p in changed}!=expected:raise RuntimeError(changed)
summary=dict(status='PASS',changed=changed,vendorUnchanged=True,profilesOutsideFourUnchanged=True,clips=16,maxCommitRatio=max(c['commitRatio'] for r in rows for c in r['clips']),mvids=sorted({r['mvid'] for r in rows}))
(OUT/'quick_cast_scope.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>빠른 발동 · 문양 방어막</title><style>
body{background:#171b19;color:#eceadd;font:17px/1.6 system-ui;margin:0;padding:30px;max-width:1700px;margin:auto}h1{font-size:30px;margin:0}p,small{color:#bdc6bf}button,a{color:inherit}button{background:#2c3831;border:1px solid #677a6a;padding:9px 20px;border-radius:6px;cursor:pointer;margin:4px}button.selected{background:#dad4ae;color:#172019}nav{margin:15px 0}.pair{display:grid;grid-template-columns:1fr 1fr;gap:20px}video,img{width:100%;border-radius:8px;background:#111}h2{font-size:20px}details{margin:24px 0}@media(max-width:850px){.pair{grid-template-columns:1fr}body{padding:16px}}</style>
<h1>빠른 발동 · 문양 방어막</h1><p>가 · 나 · 거 · 너의 발동을 짧고 투명하게. 너는 KTP Bottom 문양으로만 방어하고, 받아친 접점에서만 불꽃이 사라집니다.</p>
<nav id="glyphs"></nav><nav><button onclick="view='play';load()">플레이 구도</button><button onclick="view='external';load()">외부 구도</button></nav>
<div class="pair"><section><h2>이전 확대판 v3</h2><video id="before" controls playsinline preload="metadata"></video></section><section><h2>빠른 발동 · 문양 방어막 v4</h2><video id="after" controls playsinline preload="metadata"></video></section></div>
<p><button onclick="play(1)">함께 재생</button><button onclick="play(.25)">느리게 비교</button><button onclick="seek(.05)">발동</button><button onclick="seek(.5)">발동 소멸 확인</button><button onclick="seek(hit())">명중 · 받아치기</button><button onclick="pause()">일시정지</button></p><p id="caption"></p>
<details><summary>수정 사항 · 기술 검증 범위</summary><p>발동: 0.32초, 입자 알파 최대 0.35, 기존 대비 월드 크기 44%. 문양 중심을 실제 투사체 출발점으로 1.8m 당겼습니다. 카메라와 가까워져 화면에서는 월드 크기 감소만큼 작아지지는 않습니다. 원본 KTP Bottom04/09 문양 텍스처를 사용한 별도 프리팹이며, 원본은 보존했습니다.</p><p>너: 지속 화염·연기와 기존 막을 제거하고, 알파 0.26의 KTP Bottom09 문양만 세워 배치했습니다. 받아치기 성공 지점에서만 짧은 불꽃·불티·연기가 재생됩니다. 나의 화염탄은 유지했습니다. 나머지 ㄴ 전체에 일괄 적용하지 않았습니다.</p><p>1920×1080 · 24fps · 각 6초. C2의 같은 배경·카메라에서 임시 표적과 시험용 입사 신호로 촬영했습니다. 예약 피해와 ParryJudge 및 Particle System은 실제 런타임 경로입니다. 손글씨 입력·적 AI 전투 영상은 아닙니다. 영상은 외형 검토용이며 프레임 성능 검사가 아닙니다.</p><a href="Emphasis4/QUICK_CAST_REPORT.md">수치와 잔여 검증</a> · <a href="EMPHASIS4_REVIEW.html">이전 확대판 검토</a></details>
<script>const reports=__REPORTS__;let glyph=decodeURIComponent(location.hash.slice(1))||'가',view='play';if(!reports.some(r=>r.glyph===glyph))glyph='가';const videos=[document.querySelector('#before'),document.querySelector('#after')];for(const g of ['가','나','거','너']){let b=document.createElement('button');b.textContent=g;b.onclick=()=>{glyph=g;history.replaceState(null,'','#'+g);load()};document.querySelector('#glyphs').append(b)}function pause(){videos.forEach(v=>v.pause())}function seek(t){pause();videos.forEach(v=>v.currentTime=t)}function play(rate){videos.forEach(v=>{v.currentTime=0;v.playbackRate=rate;v.play()})}function hit(){return reports.find(r=>r.glyph===glyph).clips.find(c=>c.view===view&&c.variant==='after').actualHitAt+.1}function load(){pause();for(let i=0;i<2;i++){const name=view+'_'+(i?'after':'before');videos[i].src='Emphasis4/'+glyph+'_v4/'+name+'.mp4';videos[i].poster='Emphasis4/'+glyph+'_v4/'+name+'_cast.jpg';videos[i].load()}document.querySelectorAll('#glyphs button').forEach(b=>b.classList.toggle('selected',b.textContent===glyph));document.querySelector('#caption').textContent=({'가':'가 — 빠른 목 문양에서 출발하는 대나무탄','나':'나 — 빠른 화 문양에서 출발하는 화염탄','거':'거 — 빠른 발동과 기존 대나무 방벽','너':'너 — 투명한 Bottom 문양 방어막과 접점 소각'})[glyph]}load();</script></html>'''
(OUT.parent/'QUICK_CAST_REVIEW.html').write_text(page.replace('__REPORTS__',json.dumps(rows,ensure_ascii=False)),encoding='utf-8')
print(json.dumps(summary,ensure_ascii=False),flush=True)
