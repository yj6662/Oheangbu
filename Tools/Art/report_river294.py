"""Local source ledger and before/after review for the candidate river art pass."""
from pathlib import Path
import hashlib, json, re, shutil
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/River294'
SOURCE=ROOT/'Art/World/Compact/Rebuild/Highlands293/sources.json'
assets={'namaqualand_boulder_02','namaqualand_boulder_04','namaqualand_cliff_02','rock_3','mossy_rock','rocks_ground_06','brown_mud_rocks_01'}
records=[]
for record in json.loads(SOURCE.read_text(encoding='utf8')):
    if record['asset'] not in assets:continue
    local=ROOT/record['path']
    assert local.is_file(),local
    assert hashlib.sha256(local.read_bytes()).hexdigest()==record['sha256'],local
    records.append(dict(record,reuse='Existing local CC0 source; no new download. Prior LOD meshes, uniform scale / rotation / partial burial; candidate wet stone material and gravel/mud shoreline.'))
sheet=ROOT/'Oheangbu/Assets/_Project/Art/World/Reworld292/Data/41ed54300d9ef754a8e74defc2ab189a_MountainVegetation.asset'
text=sheet.read_text(encoding='utf8')
plants=[]
for name in ['Hyeongang_SM_PhragmitesAustralis_1','Hyeongang_SM_PhragmitesAustralis_2','Hyeongang_SM_Deparia_1','Hyeongang_SM_Salixpierotii_Summer_1']:
    block=re.search(r'  - Id: '+name+r'\n(.*?)(?=\n  - Id:)',text,re.S)[1]
    source=re.search(r'    SourcePath: (.*)',block)
    plants.append(dict(id=name,source=source[1] if source else 'Existing candidate vegetation sheet',license='Existing project asset; not relabelled CC0',change='Reuse existing meshes, LODs and materials in a separate instanced bank sheet.'))
(OUT/'sources.json').write_text(json.dumps(dict(cc0=records,existing_plants=plants,new_downloads=0),ensure_ascii=False,indent=2),encoding='utf8')

font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',22)
names=['본류 · 강변','본류 · 높은 사선','동쪽 지류 · 강변','동쪽 지류 · 높은 사선','서쪽 지류 · 강변','서쪽 지류 · 높은 사선','본류 · 물가 근접','동쪽 지류 · 물가 근접','서쪽 지류 · 물가 근접']
for group,views in [('near',(0,2,4)),('overview',(1,3,5)),('detail',(6,7,8))]:
    image=Image.new('RGB',(1920,582*3),(238,233,222));draw=ImageDraw.Draw(image)
    for row,i in enumerate(views):
        for col,stage in enumerate(('before','after')):
            im=Image.open(OUT/stage/f'river-{i}.png').convert('RGB')
            image.paste(im.resize((960,540),Image.Resampling.LANCZOS),(col*960,row*582+42))
            draw.text((col*960+12,row*582+8),names[i]+(' — 수정 전' if col==0 else ' — 수정 후'),font=font,fill=(40,42,35))
    image.save(OUT/f'comparison-{group}.jpg',quality=94)
print('Verified local CC0 source records:',len(records),'| existing plant prototypes:',len(plants))

for name in ('sections-checks.txt','walk-fixture.txt'):
    source=OUT.parent/'Highlands293'/name
    content=source.read_text(encoding='utf8')
    assert 'FAIL' not in content,source
    shutil.copy2(source,OUT/name)
checks=(OUT/'checks.txt').read_text(encoding='utf8')
assert 'FAIL' not in checks
protection=json.loads((OUT/'protected-after.json').read_text())
assert not protection['changed'] and not protection['added']
manifest=[]
for i,name in enumerate(names):
    for stage in ('before','after'):
        path=OUT/stage/f'river-{i}.png'
        with Image.open(path) as im: assert im.size==(1920,1080)
        manifest.append(dict(view=i,label=name,stage=stage,path=str(path.relative_to(ROOT)),sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
(OUT/'captures.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')

html='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>강과 물가 — #294 검토</title>
<style>
*{box-sizing:border-box}body{margin:0;background:#ede9df;color:#29302b;font:16px/1.65 "Malgun Gothic",sans-serif}main{max-width:1480px;margin:auto;padding:32px 28px 64px}h1{font-size:36px;margin:0 0 10px}h2{font-size:22px;margin-top:36px}.eyebrow{letter-spacing:.1em;font-size:12px}p{max-width:960px}a{color:#355d4c}nav{display:flex;gap:8px;flex-wrap:wrap;margin:22px 0 14px}button{font:inherit;border:1px solid #8e978d;color:inherit;background:transparent;padding:8px 14px;cursor:pointer}button[aria-pressed=true]{background:#334c42;color:#fff;border-color:#334c42}.viewer{position:relative;aspect-ratio:16/9;overflow:hidden;background:#c1c6bd}.viewer img{width:100%;height:100%;position:absolute;inset:0;object-fit:contain}.before{clip-path:inset(0 50% 0 0)}.divider{position:absolute;left:50%;top:0;bottom:0;width:2px;background:#fff8}.tag{position:absolute;top:12px;padding:3px 9px;background:#23352de6;color:white;font-size:13px}.left{left:12px}.right{right:12px}.controls{display:flex;gap:16px;align-items:center;margin:10px 0}.controls input{flex:1;accent-color:#355d4c}.facts{display:flex;gap:28px;flex-wrap:wrap;border-block:1px solid #a5ad9f;padding:16px 0;margin:24px 0}.facts strong{font-size:25px;display:block}.facts span{font-size:14px}details{border-top:1px solid #a5ad9f;padding:12px 0}summary{cursor:pointer}table{border-collapse:collapse}td,th{text-align:left;padding:8px 16px 8px 0;border-bottom:1px solid #bdc3b9}.note{border-left:3px solid #758574;padding-left:15px}.links{display:flex;gap:18px;flex-wrap:wrap}pre{white-space:pre-wrap;background:#e3e2d9;padding:18px;font-size:13px}
</style><main>
<div class="eyebrow">오행부 · 월드맵 후보 · 2026.09.27 · TEST</div>
<h1>강과 물가</h1>
<p>기존 CC0 암석·자갈·진흙과 보유 갈대·고사리를 활용했다. 지형 단면에 맞춰 강의 폭을 바꾸고, 얕은 물과 젖은 둔치가 이어지도록 정리했다.</p>
<div class="facts"><div><strong>3개 강</strong><span>본류 · 동쪽 지류 · 서쪽 지류</span></div><div><strong>9쌍</strong><span>같은 카메라의 수정 전후 화면</span></div><div><strong>7,972개</strong><span>LOD를 사용하는 강변 돌·식생 배치</span></div></div>
<nav id="views" aria-label="비교 시점"></nav>
<div class="viewer"><img id="after" alt="수정 후"><img id="before" class="before" alt="수정 전"><div id="divider" class="divider"></div><span class="tag left">수정 전</span><span class="tag right">수정 후</span></div>
<div class="controls"><label for="split">전후 비교</label><input id="split" type="range" min="0" max="100" value="50" aria-label="수정 전 화면 비율"></div>
<div class="links"><a id="originalBefore" target="_blank">수정 전 원본</a><a id="originalAfter" target="_blank">수정 후 원본</a><a href="comparison-near.jpg">강변 비교 모음</a><a href="comparison-overview.jpg">원경 비교 모음</a><a href="comparison-detail.jpg">물가 근접 비교 모음</a></div>
<h2>이번에 바뀐 것</h2><ul><li>일정한 폭의 리본을 3m 간격의 지형 단면과 부드러운 굽이에 맞춘 수면으로 교체했다.</li><li>물 깊이에 따른 바닥색, 저채도 하늘 반사, 국소 경사에 따른 물결·여울을 적용했다.</li><li>젖은 자갈과 진흙을 지형 재질에 직접 섞어 겹친 표면의 줄무늬를 없앴다.</li><li>돌 군집·갈대·고사리·드문 버드나무를 배치했다. 새 수면에 잠긴 기존 식생 736개는 별도 배치 사본에서 제외했다.</li></ul>
<h2>확인 결과</h2><table><tr><th>검사</th><th>결과</th></tr><tr><td>강 메시·물속 판정·장식 접지·컬링</td><td>11 PASS · 수면 삼각형 55,168개 일치</td></tr><tr><td>강변 / 기존 식생 사본 컬링</td><td>각 12시점 · 전체 순회와 불일치 0</td></tr><tr><td>고산 구조 / Edit 자동 보행</td><td>111 PASS / 70 PASS</td></tr><tr><td>기존 원본 / 후보 지형·기존 배치</td><td>422파일 / 3,827파일 해시 동일</td></tr></table>
<p class="note"><strong>남은 문제:</strong> 기존 수계가 일부 언덕을 가로질러 오르내린다. 이번에는 고산·지형·경로를 보존했으므로 전역 배수와 모든 합류부는 해결되지 않았다. 급경사 수면 형태, 합류부, 여울 통행과 이동 중 물결은 후속 검토가 필요하다. 새 Play 성능·수동 플레이·사용자 미술 승인은 미검증이다.</p>
<details><summary>출처와 검증 원문</summary><p>새 다운로드 0건. 기존 CC0 원장 24개 파일의 SHA256을 확인했다. 보유 식생은 CC0로 재표기하지 않았다.</p><div class="links"><a href="sources.json">에셋 출처</a><a href="checks.txt">강 검사</a><a href="sections-checks.txt">고산 검사</a><a href="walk-fixture.txt">자동 보행</a><a href="protected-after.json">후보 보존 해시</a><a href="captures.json">캡처 원장</a></div></details>
<p><a href="../Highlands293/ToneReview/REVIEW.html">앞선 강토 명도·컬링 검토</a></p>
</main><script>
const names=__NAMES__;
const before=document.getElementById('before'),after=document.getElementById('after'),views=document.getElementById('views');
function select(i){before.src=`before/river-${i}.png`;after.src=`after/river-${i}.png`;before.alt=names[i]+' 수정 전';after.alt=names[i]+' 수정 후';document.getElementById('originalBefore').href=before.src;document.getElementById('originalAfter').href=after.src;[...views.children].forEach((b,k)=>b.setAttribute('aria-pressed',k===i));}
names.forEach((name,i)=>{const b=document.createElement('button');b.textContent=name;b.onclick=()=>select(i);views.appendChild(b)});
document.getElementById('split').oninput=e=>{before.style.clipPath=`inset(0 ${100-e.target.value}% 0 0)`;document.getElementById('divider').style.left=e.target.value+'%'};select(7);
</script></html>'''.replace('__NAMES__',json.dumps(names,ensure_ascii=False))
(OUT/'REVIEW.html').write_text(html,encoding='utf8')
print('Review:',OUT/'REVIEW.html')
