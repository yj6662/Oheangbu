import json,subprocess,hashlib,html
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/SpellVFX120/Emphasis4'
ff=next((Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries').glob('*.exe'))
rows=[]
for glyph in '가나거너':
    p=OUT/(glyph+'_v3')/'report.json'
    r=json.loads(p.read_text(encoding='utf-8'))
    if r['status']!='PASS_RUNTIME_FIXTURE_VISUAL_PENDING':raise RuntimeError(r)
    for clip in r['clips']:
        frames=Path(clip['folder']);mp4=frames.with_suffix('.mp4')
        if not mp4.exists() or mp4.stat().st_mtime<max(x.stat().st_mtime for x in frames.glob('*.jpg')):
            subprocess.run([str(ff),'-y','-v','error','-threads','2','-framerate','24','-i',str(frames/'%04d.jpg'),'-c:v','libx264','-threads','2','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(mp4)],check=True)
            subprocess.run([str(ff),'-v','error','-threads','2','-i',str(mp4),'-f','null','-'],check=True)
        print(mp4,flush=True)
    rows.append(r)
hashes=json.loads((OUT/'before_hashes.json').read_text(encoding='utf-8'))
changed=[]
for path,previous in hashes.items():
    with (ROOT/path).open('rb') as stream:current=hashlib.file_digest(stream,'sha256').hexdigest()
    if current!=previous:changed.append(path)
expected={f'Oheangbu/Assets/_Project/Art/SpellVFX120/Profiles/{name}.asset' for name in ['001_AC00','025_B098','007_AC70','031_B108']}
if {p.replace('\\','/') for p in changed}!=expected:raise RuntimeError(changed)
(OUT/'scope_check.json').write_text(json.dumps(dict(status='PASS',changed=changed,vendorUnchanged=True,profilesOutsideFourUnchanged=True,clips=16),ensure_ascii=False,indent=2),encoding='utf-8')
page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>가 · 나 · 거 · 너 — KTP 문양 확대 비교</title><style>
body{background:#161b19;color:#eae7dc;font:17px/1.6 system-ui;margin:0;padding:32px;max-width:1700px;margin:auto}h1{font-size:32px;margin:0 0 8px}p{color:#b8c3ba}button,a{color:inherit}button{background:#2a3630;border:1px solid #607569;padding:10px 22px;border-radius:6px;cursor:pointer;margin:4px}button.selected{background:#d5d2ac;color:#151d17}nav{margin:16px 0}.pair{display:grid;grid-template-columns:1fr 1fr;gap:20px}video,img{width:100%;border-radius:8px;background:#101411}h2{font-size:20px}small{color:#b8c3ba}details{margin:24px 0}input{width:60%}@media(max-width:850px){.pair{grid-template-columns:1fr}body{padding:16px}}</style>
<h1>가 · 나 · 거 · 너</h1><p>목의 녹색, 화의 적색을 유지한 KTP 문양 강조 버전. 같은 구도에서 기존판과 확대판을 비교합니다.</p>
<nav id="glyphs"></nav><nav><button onclick="view='play';load()">플레이 카메라 구도</button><button onclick="view='external';load()">외부 구도</button></nav>
<div class="pair"><section><h2>기존판</h2><video id="before" controls playsinline preload="metadata"></video></section><section><h2>문양 강조판</h2><video id="after" controls playsinline preload="metadata"></video></section></div>
<p><button onclick="syncPlay()">함께 재생 / 처음부터</button><button onclick="seek(.25)">발동</button><button onclick="seek(.8)">명중 · 받아치기</button><button onclick="pause()">일시정지</button></p>
<p id="caption"></p><details><summary>검증 범위와 변경 내용</summary><p>1920×1080 · 24fps · 각 6초. C2의 렌더 설정을 사용한 고정 카메라 비교입니다. 임시 표적과 시험용 입사 신호를 사용하며, 실제 CombatLoopWiring의 예약 피해와 ParryJudge 판정·Particle System Update를 실행했습니다. 손글씨 입력·적 AI 전투를 촬영한 영상은 아닙니다. 실제 플레이어 카메라 이동과 성능은 미검증입니다.</p><p>발동 2.5배, 실제 명중·받아치기 2배, 방어 문양 1.5배. 발동은 KTP Bottom04/09의 전체 계층을 세워 배치합니다. 문양 재질 밝기 2.2배, 주변 광원 입자 재질 밝기 0.25배로 선을 구분합니다. 원본 곡선·방출 설정·전역 블룸은 유지합니다. 색감과 전통적인 인상의 최종 판정은 사용자 검토 대상입니다.</p><a href="Emphasis4/REPORT.md">상세 보고서</a></details>
<script>let glyph='가',view='play';const videos=[document.querySelector('#before'),document.querySelector('#after')];for(const g of ['가','나','거','너']){let b=document.createElement('button');b.textContent=g;b.onclick=()=>{glyph=g;load()};document.querySelector('#glyphs').append(b)}function pause(){videos.forEach(v=>v.pause())}function seek(t){pause();videos.forEach(v=>v.currentTime=t)}function syncPlay(){videos.forEach(v=>{v.currentTime=0;v.play()})}function load(){pause();for(let i=0;i<2;i++){const name=view+'_'+(i?'after':'before');videos[i].src='Emphasis4/'+glyph+'_v2/'+name+'.mp4';videos[i].poster='Emphasis4/'+glyph+'_v2/'+name+'/0006.jpg';videos[i].load()}document.querySelectorAll('#glyphs button').forEach(b=>b.classList.toggle('selected',b.textContent===glyph));document.querySelector('#caption').textContent=({'가':'가 — 대나무탄 발동과 적 명중','나':'나 — 화염탄 발동과 적 명중','거':'거 — 대나무 방벽과 받아치기','너':'너 — 화막과 받아치기'})[glyph]}load();</script></html>'''
page=page.replace('_v2','_v3').replace('/0006.jpg','/0030.jpg').replace('seek(.25)','seek(1.25)')
(OUT.parent/'EMPHASIS4_REVIEW.html').write_text(page,encoding='utf-8')
print('PUBLISHED 16 verified clips; four profile scope PASS',flush=True)
