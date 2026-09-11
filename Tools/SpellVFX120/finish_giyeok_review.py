"""Assemble existing single-spell deliveries. Does not generate or grade VFX."""
from pathlib import Path
import json,re,hashlib,html,urllib.request,yaml
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/SpellVFX120'
ASSETS=ROOT/'Oheangbu/Assets/_Project/Art/SpellVFX120'
CONFIG=[
('가','GA_001','36',262),('각','GAK_002','15',16525),('간','GAN_003','37',782),('감','GAM_004','38',994),
('갓','GAT_005','39',240),('강','GANG_006','40',46),('거','GE_007','28',2384),('걱','GEOK_008','30',1032),
('건','GEON_009','31',1088),('검','GEOM_010','15',6826),('것','GEOT_011','32',330),('겅','GEONG_012','33',1284),
('고','GO_013','34',5110),('곡','GOK_014','35',4096),('곤','GON_015','41',9921),('곰','GOM_016','44',12543),
('곳','GOT_017','42',2054),('공','GONG_018','15',19733),('구','GU_019','29',5280),('국','GUK_020','43',1702)]

def read_asset(path):
    s=path.read_text(encoding='utf-8-sig')
    return yaml.safe_load(s[s.index('MonoBehaviour:'):])['MonoBehaviour']
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def guid(path): return re.search(r'^guid: (\w+)',Path(str(path)+'.meta').read_text(),re.M)[1]

def main():
    cat=read_asset(ASSETS/'Data/VFX120_Catalog.asset')['Entries']
    book=read_asset(ROOT/'Oheangbu/Assets/_Project/Data/Configs/SpellBook_Proto.asset')['_entries']
    registered={x['Letter'] for x in book}
    rows=[];refs=set();scope=[]
    for index,(glyph,key,version,tris) in enumerate(CONFIG,1):
        item=f'{index:03}_{ord(glyph):04X}';profile=ASSETS/'Profiles'/f'{item}.asset';prefab=ASSETS/'Prefabs'/f'{item}.prefab'
        p=read_asset(profile);entry=cat[index-1]
        assert p['Glyph']==entry['Glyph']==glyph and p['Assigned'] and entry['Profile']['guid']==guid(profile)
        assert entry['Prefab']['guid']==guid(prefab)
        prefab_text=prefab.read_text(encoding='utf-8');assert 'guid: '+guid(profile) in prefab_text
        assert 'PreviewControlled: 0' in prefab_text and 'DemonstrationCues: 0' in prefab_text
        audit_name='botanical_play_audit.json' if index in (2,10,18) else f'single_play_{ord(glyph):04X}.json'
        audit=json.loads((OUT/audit_name).read_text(encoding='utf-8'));case=next(x for x in audit['rows'] if x['glyph']==glyph)
        assert audit['status']=='PASS_REAL_UPDATE_LIFETIME_ONLY' and case['status']=='PASS_REAL_UPDATE_LIFETIME_ONLY' and audit['restored'] and audit['errors']==0 and audit['shaderErrors']==0
        for kind in ('Integrated','External'):
            clip=f'ClipsKTP{kind}{version}/{item}.mp4';encoding=json.loads((OUT/clip.replace('.mp4','_encoding.json')).read_text())
            assert encoding['status']=='VERIFIED_ENCODING_AND_TIMING_ONLY' and sha(OUT/clip)==encoding['outputSha256']
            capture=json.loads((OUT/f'KTP{kind}Frames{version}/{item}_capture.json').read_text())
            assert capture['loadedRuntimeAssemblyMvid']==audit['appMvid']
        row=dict(index=index,glyph=glyph,key=key,id=item,title=p['Title'],intent=p['Intent'],version=version,tris=tris,connected=glyph in registered,
                 page=key+'_REVIEW.html',report=key+'_REPORT.md',audit=audit_name,mvid=audit['appMvid'],technical='PASS',art='AWAITING_USER_REVIEW',
                 profileSha256=sha(profile),prefabSha256=sha(prefab),legacyEvidence=index in (2,10,18))
        for kind in ('Integrated','External'):
            row[kind.lower()]=f'ClipsKTP{kind}{version}/{item}.mp4';row[kind.lower()+'Poster']=f'KTP{kind}Stills{version}/{item}_1.png'
        rows.append(row)
        page=(OUT/row['page']).read_text(encoding='utf-8')
        refs.update(re.findall(r'(?:href|src|poster)="([^"]+)"',page));refs.update([row['page'],row['report']])
        scope.append(dict(glyph=glyph,profileReference='PASS',prefabReference='PASS',productionPreviewFlagsOff=True))
    blanks=[]
    for index in range(21,25):
        pfile=next((ASSETS/'Profiles').glob(f'{index:03}_*.asset'));p=read_asset(pfile);assert not p['Assigned'] and p['Glyph'] not in registered
        blanks.append(p['Glyph'])
    assert blanks==list('군굼굿궁') and sum(x['connected'] for x in rows)==3
    evidence=dict(status='PASS_SCOPED_ASSETS_AND_DELIVERY',assigned=20,unassigned=blanks,rows=rows,referenceChecks=scope,
                  scope='VFX authoring and recorded technical playback; game abilities and user visual approval are separate.')
    (OUT/'GIYEOK_MANIFEST.json').write_text(json.dumps(evidence,ensure_ascii=False,indent=2),encoding='utf-8')
    style='''body{margin:0;background:#161b1b;color:#e9e1cf;font:16px/1.6 system-ui,sans-serif}main{max-width:1320px;margin:auto;padding:32px}h1{font-size:34px;margin:6px 0}h2{font-size:24px}p{color:#c2c7bd}a{color:#b8c9a0}button{font:inherit;color:inherit;background:#242e2a;border:1px solid #536254;border-radius:7px;padding:9px 14px;cursor:pointer}button:hover,button.selected{background:#3d5142;border-color:#b8c9a0}.layout{display:grid;grid-template-columns:230px 1fr;gap:24px}.list{display:grid;gap:6px;align-content:start}.list button{text-align:left;padding:9px;font-size:14px}.list b{font-size:20px;margin-right:10px}.media{display:grid;grid-template-columns:1fr 1fr;gap:12px}video{width:100%;aspect-ratio:16/9;background:#0d1010}figure{margin:0}figcaption{color:#9fac9c;font-size:13px}.note{padding:14px;background:#242a26;border-left:3px solid #9faf87}.tag{font-size:13px;letter-spacing:.06em;color:#b8c9a0}.details{border-top:1px solid #39423b;margin-top:24px;padding-top:12px}.blanks{display:flex;gap:18px;color:#a7aaa3}.nav{display:flex;gap:10px;flex-wrap:wrap;margin:16px 0}@media(max-width:850px){main{padding:18px}.layout{display:block}.list{grid-template-columns:repeat(5,1fr);margin:20px 0}.list button{font-size:0;text-align:center}.list b{font-size:22px;margin:0}.media{grid-template-columns:1fr}}'''
    page='<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>오행부 · ㄱ 술식 검토</title><style>'+style+'</style><main>'
    page+='<span class="tag">오행부 · KTP 문양을 활용한 목 술식</span><h1>ㄱ 술식 20종</h1><p>한 종씩 선택해 기본 시점과 외부 시점을 비교할 수 있습니다. 비주얼 최종 판정은 사용자 검토 대기입니다.</p><div class="note">20종은 VFX 시안과 기술 재생 검증 범위입니다. 현재 게임에 연결된 술식은 가·고·거 3종이며, 나머지17종은 카탈로그 표현입니다. 곰은 정적 Meshy 사슴이며 이동·공격 애니메이션은 포함하지 않습니다.</div><div class="layout"><nav class="list" id="list">'
    for row in rows:page+='<button data-index="'+str(row['index']-1)+'"><b>'+row['glyph']+'</b>'+html.escape(row['title'])+'</button>'
    page+='</nav><section><h2 id="title"></h2><p id="intent"></p><div class="media"><figure><video id="main" controls muted loop playsinline preload="metadata"></video><figcaption>C2 기본 카메라</figcaption></figure><figure><video id="external" controls muted loop playsinline preload="metadata"></video><figcaption>외부 카메라</figcaption></figure></div><div class="nav"><button id="play">두 영상 함께 재생</button><button id="prev">이전 술식</button><button id="next">다음 술식</button></div><p id="meta"></p><p><a id="detail">수정 전후·상세 시안</a> · <a id="report">개별 보고서</a> · <a id="audit">기술 검사 원본</a></p></section></div><div class="details"><h2>의도적 공백</h2><div class="blanks"><span>군</span><span>굼</span><span>굿</span><span>궁</span></div><p>미배정 상태를 유지했습니다. 임의의 공격이나 소환물을 추가하지 않았습니다.</p><p><a href="GIYEOK_REPORT.md">ㄱ 범위 최종 보고서</a> · <a href="GIYEOK_MANIFEST.json">자산·영상·검사 목록</a> · <a href="giyeok_outside_scope_check.json">범위 밖96개 프로필 보존</a></p></div></main>'
    page+='<script>const rows='+json.dumps(rows,ensure_ascii=False).replace('</',r'<\/')+''';let selected=0;const main=document.getElementById('main'),ext=document.getElementById('external');function show(i){selected=(i+rows.length)%rows.length;const r=rows[selected];main.pause();ext.pause();main.src=r.integrated;main.poster=r.integratedPoster;ext.src=r.external;ext.poster=r.externalPoster;document.getElementById('title').textContent=r.glyph+' · '+r.title;document.getElementById('intent').textContent=r.intent;document.getElementById('meta').textContent='본체 '+r.tris.toLocaleString()+' tris · '+(r.connected?'기존 게임 경로 연결':'카탈로그 표현')+' · '+(r.legacyEvidence?'채택 버전15 영상/당시 Play 검사, 현재 자산 해시 보존 확인':'개별 제작 버전 '+r.version+' 영상/Play 검사')+' · KTP 파티클·선 렌더러 비용 별도';for(const [id,path] of [['detail',r.page],['report',r.report],['audit',r.audit]])document.getElementById(id).href=path;document.querySelectorAll('[data-index]').forEach(b=>b.classList.toggle('selected',Number(b.dataset.index)===selected));history.replaceState(null,'','#'+r.glyph);}document.querySelectorAll('[data-index]').forEach(b=>b.onclick=()=>show(Number(b.dataset.index)));document.getElementById('play').onclick=()=>[main,ext].forEach(v=>{v.currentTime=0;v.play().catch(()=>{});});document.getElementById('prev').onclick=()=>show(selected-1);document.getElementById('next').onclick=()=>show(selected+1);const initial=rows.findIndex(r=>r.glyph===decodeURIComponent(location.hash.slice(1)));show(initial<0?0:initial);</script></html>'''
    (OUT/'GIYEOK_REVIEW.html').write_text(page,encoding='utf-8')
    # All media were individually encoded/decoded; this checks final files, hashes and links.
    refs.update(['GIYEOK_REVIEW.html','GIYEOK_MANIFEST.json','GIYEOK_REPORT.md','giyeok_outside_scope_check.json'])
    missing=[x for x in sorted(refs) if not (OUT/x).is_file()]
    if missing: raise RuntimeError('Missing final delivery links: '+str(missing))
    for name in ['GIYEOK_REVIEW.html','GIYEOK_MANIFEST.json','GIYEOK_REPORT.md']:
        with urllib.request.urlopen(urllib.request.Request('http://127.0.0.1:8771/'+name,method='HEAD'),timeout=10) as response:assert response.status==200
    check=dict(status='PASS',assigned=20,blankCount=4,verifiedVideos=40,referencedFiles=len(refs),missing=[],runtimeAudits=20,visualReview='AWAITING_USER_REVIEW')
    (OUT/'GIYEOK_DELIVERY_CHECK.json').write_text(json.dumps(check,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(check,ensure_ascii=False))

if __name__=='__main__':main()
