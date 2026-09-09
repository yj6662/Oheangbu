"""Read-only KTP inventory and local-only source thumbnail audit sheets."""
from pathlib import Path
import re,json,collections,concurrent.futures
from PIL import Image,ImageDraw

ROOT=Path(__file__).resolve().parents[3]
PACK=ROOT/'Oheangbu/Assets/KoreanTraditionalPattern_Effect'
OUT=ROOT/'Docs/Art/SpellVFX120'
PREVIEW=ROOT/'Art/SpellVFX120/AssetAudit'
OUT.mkdir(parents=True,exist_ok=True)
PREVIEW.mkdir(parents=True,exist_ok=True)
def txt(p):return p.read_text(encoding='utf-8-sig',errors='replace')
def guid(p):
    m=re.search(r'^guid: (\w+)',txt(Path(str(p)+'.meta')),re.M)
    return m[1] if m else None
def path(p):return str(p.relative_to(ROOT)).replace('\\','/')
byguid={guid(p):p for p in PACK.rglob('*') if p.is_file() and p.suffix!='.meta' and Path(str(p)+'.meta').exists()}
prefabs=[]
for p in sorted(PACK.rglob('*.prefab')):
    s=txt(p); comps=re.findall(r'^--- !u!(\d+)',s,re.M)
    mats=set(re.findall(r'm_Materials:\s*\n(?:\s*- \{[^\n]+\}\n)+',s))
    mg=set()
    for b in mats: mg.update(re.findall(r'guid: (\w+)',b))
    prefabs.append(dict(path=path(p),guid=guid(p),particle_systems=comps.count('198'),particle_renderers=comps.count('199'),mesh_renderers=comps.count('23'),animators=comps.count('95'),looping_systems=len(re.findall(r'^  looping: 1$',s,re.M)),material_paths=[path(byguid[g]) if g in byguid else g for g in sorted(mg)],mesh_guids=sorted(set(re.findall(r'm_Mesh(?:\d+)?: \{[^}]*guid: (\w+)',s))),particle_caps=[int(n) for n in re.findall(r'^    maxNumParticles: (\d+)',s,re.M)],names=re.findall(r'^  m_Name: (.+)$',s,re.M)))
materials=[]
for p in sorted(PACK.rglob('*.mat')):
    s=txt(p);sh=re.search(r'm_Shader: \{[^}]*guid: (\w+)',s)
    materials.append(dict(path=path(p),guid=guid(p),shader_guid=sh[1] if sh else None,shader_path=path(byguid[sh[1]]) if sh and sh[1] in byguid else None,texture_paths=sorted({path(byguid[g]) for g in re.findall(r'm_Texture: \{[^}]*guid: (\w+)',s) if g in byguid}),render_queue=re.search(r'm_CustomRenderQueue: (-?\d+)',s)[1]))
textures=sorted(PACK.rglob('*.png'),key=lambda p:(str(p.parent),int(re.search(r'\d+',p.stem)[0]) if re.search(r'\d+',p.stem) else 0,p.stem))
def thumbnail(p):
    im=Image.open(p);size=im.size;mode=im.mode
    im.thumbnail((168,168)); im=im.convert('RGBA')
    canvas=Image.new('RGB',(180,202),(48,46,43)); canvas.paste(im,((180-im.width)//2,4),im)
    ImageDraw.Draw(canvas).text((5,179),p.stem,fill=(245,237,220))
    meta=txt(Path(str(p)+'.meta'))
    return canvas,dict(path=path(p),guid=guid(p),size=list(size),mode=mode,maxTextureSize=int(re.search(r'^  maxTextureSize: (\d+)',meta,re.M)[1]),mipmap_enabled=bool(int(re.search(r'^    enableMipMap: (\d+)',meta,re.M)[1])))
records=[]
for group,ps in [('traditional',[p for p in textures if 'TraditionalTexture' in str(p)]),('public',[p for p in textures if 'PublicTexture' in str(p)])]:
    for start in range(0,len(ps),50):
        batch=ps[start:start+50];sheet=Image.new('RGB',(1800,1010),(48,46,43))
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as ex:
            for i,(im,rec) in enumerate(ex.map(thumbnail,batch)):
                sheet.paste(im,((i%10)*180,(i//10)*202));records.append(rec)
        dest=PREVIEW/f'{group}_{start+1:03d}_{start+len(batch):03d}.jpg';sheet.save(dest,quality=91)
        print(path(dest),flush=True)
stats=dict(prefab_count=len(prefabs),material_count=len(materials),texture_count=len(records),mesh_count=len(list(PACK.rglob('*.FBX'))),particle_system_count=sum(p['particle_systems'] for p in prefabs),shader_counts=dict(collections.Counter(m['shader_path'] or m['shader_guid'] for m in materials)))
(OUT/'asset_inventory_raw.json').write_text(json.dumps(dict(stats=stats,prefabs=prefabs,materials=materials,textures=records),ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(stats,ensure_ascii=False))
