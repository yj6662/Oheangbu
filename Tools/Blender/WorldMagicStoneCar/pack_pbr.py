"""Lossless channel packing for the authorized MagicStoneCar PBR pipeline.

No generation, upscaling, repainting, or external requests. Source glTF pixels
and UVs remain unchanged. One 4k part at a time outside Blender.
"""
from pathlib import Path
import argparse, hashlib, io, json, struct
from PIL import Image, ImageChops
ROOT=Path(__file__).resolve().parents[3]
ART=ROOT/'Art/World/WorldMacro/MagicStoneCar'
ASSET=ROOT/'Oheangbu/Assets/_Project/Art/World/WorldMacro/MagicStoneCar'
PARTS=('Cabin','Roof','Engine','Wheel')

def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def asset(p):return 'Assets/'+p.relative_to(ROOT/'Oheangbu/Assets').as_posix()
def write(p,d):p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(d,ensure_ascii=False,indent=2),encoding='utf-8')
def glb(path):
    data=path.read_bytes()
    if data[:4]!=b'glTF':raise ValueError('Expected binary glTF')
    offset=12;j=None;binary=None
    while offset<len(data):
        length,kind=struct.unpack_from('<II',data,offset);chunk=data[offset+8:offset+8+length];offset+=8+length
        if kind==0x4e4f534a:j=json.loads(chunk)
        elif kind==0x004e4942:binary=chunk
    if j is None or binary is None:raise ValueError('GLB missing JSON or binary chunk')
    return j,binary

def pack(part):
    source=ART/'Meshy'/part/'image/model_urls_glb.glb';before=sha(source);j,data=glb(source)
    if len(j.get('materials',[]))!=1:raise RuntimeError('Explicit mapping required for multi-material source')
    material=j['materials'][0];pbr=material.get('pbrMetallicRoughness',{});images={}
    def tex(binding):
        if not binding:return None
        index=j['textures'][binding['index']]['source']
        if index not in images:
            src=j['images'][index]
            if 'bufferView' not in src:raise RuntimeError('External image URI requires explicit local source validation')
            v=j['bufferViews'][src['bufferView']];start=v.get('byteOffset',0)
            images[index]=Image.open(io.BytesIO(data[start:start+v['byteLength']])).convert('RGBA')
        return images[index]
    base=tex(pbr.get('baseColorTexture'));normal=tex(material.get('normalTexture'));mr=tex(pbr.get('metallicRoughnessTexture'));ao=tex(material.get('occlusionTexture'))
    if base is None or normal is None or mr is None:raise RuntimeError('Source lacks requested BaseColor/Normal/packed MR maps')
    folder=ASSET/'Textures';folder.mkdir(parents=True,exist_ok=True)
    paths={};records=[]
    def save(key,image):
        p=folder/f'MagicStoneCar_{part}_{key}.png';image.save(p);paths[key]=asset(p);records.append(dict(role=key,path=asset(p),size=list(image.size),sha256=sha(p)))
    save('BaseColor',base);save('Normal',normal)
    metallic_factor=float(pbr.get('metallicFactor',1));roughness_factor=float(pbr.get('roughnessFactor',1))
    metal=mr.getchannel('B').point(lambda x:round(x*metallic_factor))
    smooth=mr.getchannel('G').point(lambda x:round(255*(1-(x/255)*roughness_factor)))
    zero=Image.new('L',mr.size,0);packed=Image.merge('RGBA',(metal,zero,zero,smooth));save('MetallicSmoothness',packed)
    if ao is not None:
        channel=ao.getchannel('R');strength=float(material.get('occlusionTexture',{}).get('strength',1))
        channel=channel.point(lambda x:round(255*(1-strength)+x*strength));occlusion=Image.merge('RGBA',(channel,channel,channel,Image.new('L',ao.size,255)))
    else:occlusion=Image.new('RGBA',mr.size,(255,255,255,255))
    save('Occlusion',occlusion)
    # Fallback engine mask refers only to color-coded source surfaces; visual region validation remains separate.
    emission=''
    if part=='Engine':
        r,g,b=base.split()[:3]
        candidate=ImageChops.subtract(ImageChops.darker(g,b),r)
        mask=candidate.point(lambda x:0 if x<16 else min(255,(x-16)*4));save('EmissionCandidateMask',mask)
        emission=paths['EmissionCandidateMask']
    row=dict(part=part,material=f'{part}_Surface',baseColor=paths['BaseColor'],normal=paths['Normal'],metallicSmoothness=paths['MetallicSmoothness'],occlusion=paths['Occlusion'],
        engineEmissionMask=emission,emissionMaskVerified=False,useConstantMaterial=False,baseColorFactor=pbr.get('baseColorFactor',[1,1,1,1]),normalScale=material.get('normalTexture',{}).get('scale',1),
        sourceUvSet=pbr.get('baseColorTexture',{}).get('texCoord',0),baseColorSpace='sRGB',dataMapSpace='Linear',metallicChannel='R',smoothnessChannel='A')
    report=dict(part=part,source=str(source),sourceSha256=before,sourcePreserved=sha(source)==before,conversion='glTF B=metallic,G=roughness -> Unity R=metallic,A=1-roughness; source factors applied; AO in G',
        maps=records,sourceRequested4K=True,actualSourceSizes={k:list(v.size) for k,v in [('baseColor',base),('normal',normal),('metallicRoughness',mr)]},
        emissionMaskStatus='UNVERIFIED_CANDIDATE_ONLY' if part=='Engine' else 'NOT_APPLICABLE',entry=row)
    write(ART/'Blender'/f'{part}_PBR.json',report)
    entries=[]
    for name in PARTS:
        p=ART/'Blender'/f'{name}_PBR.json'
        if p.exists():entries.append(json.loads(p.read_text(encoding='utf-8'))['entry'])
    entries.extend([dict(part='Support',material='Support_DarkBrass',useConstantMaterial=True,constantBaseColor=dict(r=.18,g=.12,b=.058,a=1),metallic=.65,smoothness=.45),
        dict(part='Support',material='MagicStoneLanternGlass',useConstantMaterial=True,constantBaseColor=dict(r=.09,g=.28,b=.29,a=1),metallic=.1,smoothness=.45)])
    write(ASSET/'TextureManifest.json',dict(schemaVersion=1,entries=entries))
    print(json.dumps(dict(part=part,sourcePreserved=report['sourcePreserved'],actualSizes=report['actualSourceSizes'],emissionMaskStatus=report['emissionMaskStatus'])))
    for image in images.values():image.close()

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('part',choices=PARTS);args=parser.parse_args();pack(args.part)
