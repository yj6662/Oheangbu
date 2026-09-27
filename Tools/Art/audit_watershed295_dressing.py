"""Read-only audit of the final candidate's tone, canopy, and lake-bank assets."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import re

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / 'Art/World/Compact/Rebuild/Watershed295'
ART = ROOT / 'Oheangbu/Assets/_Project/Art/World/Watershed295'
GEN = WORK / 'Generated'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def protected_at(protection, xs, zs, radius, outward):
    """Independent summed-area query over protected 4m nodes."""
    low = np.floor if outward else np.ceil
    high = np.ceil if outward else np.floor
    x0 = np.clip(low((xs-radius)/4).astype(int), 0, 1000)
    x1 = np.clip(high((xs+radius)/4).astype(int), 0, 1000)
    z0 = np.clip(low((zs-radius)/4).astype(int), 0, 1500)
    z1 = np.clip(high((zs+radius)/4).astype(int), 0, 1500)
    integral = np.pad(protection.astype(np.int32).cumsum(0).cumsum(1), ((1,0),(1,0)))
    return integral[z1[:,None]+1,x1[None,:]+1] - integral[z0[:,None],x1[None,:]+1] - integral[z1[:,None]+1,x0[None,:]] + integral[z0[:,None],x0[None,:]] > 0


def main():
    protection = np.fromfile(GEN/'protected.bytes', np.uint8).reshape(1501,1001) != 0
    heights = np.fromfile(GEN/'height.bytes', '<f4').reshape(1501,1001)
    tone = np.asarray(Image.open(ART/'Dressing/WatershedToneMask295.png').convert('RGBA'))[::-1]
    canopy = np.asarray(Image.open(ART/'Dressing/Canopy295.png').convert('RGBA'))[::-1]
    source_canopy_path = ROOT/'Oheangbu/Assets/_Project/Art/World/Reworld292/Surface/canopy293.png'
    source_canopy = np.asarray(Image.open(source_canopy_path).convert('RGBA'))[::-1]
    four = protection[:-1,:-1] | protection[:-1,1:] | protection[1:,:-1] | protection[1:,1:]
    tone_guard = protected_at(protection, (np.arange(1000)+.5)*4, (np.arange(1500)+.5)*4, 6, False)
    bank_path = ART/'Dressing/BankMask.asset'
    bank_text = bank_path.read_text(encoding='utf-8-sig')
    width = int(re.search(r'm_Width: (\d+)',bank_text)[1])
    height = int(re.search(r'm_Height: (\d+)',bank_text)[1])
    bank = np.frombuffer(bytes.fromhex(re.search(r'_typelessdata: ([0-9a-fA-F]+)',bank_text)[1][:width*height*8]), np.uint8).reshape(height,width,4)
    bank_guard = protected_at(protection,np.arange(width)*2,np.arange(height)*2,2,True)

    placements=[]
    text=(ART/'Dressing/Banks.asset').read_text(encoding='utf-8-sig')
    pattern=r'  - Id: (watershed295_lakebank_\d+)\s+ClusterId: [^\n]+\s+PrototypeId: ([^\n]+)\s+Position: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}'
    for name,proto,x,y,z in re.findall(pattern,text):
        x,y,z=float(x),float(y),float(z); ix,iz=int(x/4),int(z/4);u,v=x/4-ix,z/4-iz
        a,b,d,e=heights[iz,ix],heights[iz,ix+1],heights[iz+1,ix],heights[iz+1,ix+1]
        ground=float(a+(b-a)*u+(d-a)*v if u+v<=1 else e+(d-e)*(1-u)+(b-e)*(1-v))
        protected=bool(protected_at(protection,np.array([x]),np.array([z]),2,True)[0,0])
        placements.append(dict(id=name,prototype=proto,x=x,y=y,z=z,ground_y=ground,protected=protected,water_hits=[]))
    # Check containment against the exported visible water triangles, independent of the placement helper.
    for path in sorted((GEN/'WaterMeshes').glob('*.json')):
        item=json.loads(path.read_text()); verts=np.array([[v['x'],v['y'],v['z']] for v in item['Vertices']],float)
        tris=verts[np.array(item['Triangles']).reshape(-1,3)];a,b,c=tris[:,0][:,[0,2]],tris[:,1][:,[0,2]],tris[:,2][:,[0,2]]
        lower=verts[:,[0,2]].min(0);upper=verts[:,[0,2]].max(0)
        def cross(q,r):return q[:,0]*r[:,1]-q[:,1]*r[:,0]
        den=cross(b-a,c-a)
        for p in placements:
            q=np.array([p['x'],p['z']])
            if np.any(q<lower) or np.any(q>upper):continue
            u=cross(q-a,c-a)/den;v=cross(b-a,q-a)/den
            inside=(u>=-1e-8)&(v>=-1e-8)&(u+v<=1+1e-8)
            if inside.any():p['water_hits'].append(path.stem)
    source_shader=ROOT/'Oheangbu/Assets/_Project/Art/World/Reworld292/Shaders/KoreanInkGround.shader'
    candidate_shader=ART/'Shaders/KoreanInkGround295.shader'
    source_surface=source_shader.parent/'KoreanSurface292.hlsl'
    candidate_surface=ART/'Shaders/KoreanSurface295.hlsl'
    originals=[(source_shader,'5d12bad3a16abdec41af8847242b93c6b8928d6b837811140ea5ee06f02f6f34'),(source_canopy_path,'8c6fc3dfc223c6af03f4a33fe4dcd4de932f3f785238e482d5b667c81abdf757'),(source_surface,'c357122239e57da6b4f93db80d91da2f3d657b593edbda02797fcebb92abd98c')]
    inserted='''    // Retain subdued existing soil/rock albedo on the candidate's authored lowland mask.
    // No additional material samples or normals; exact baseline resolve where the mask is zero.
    float2 watershedDetailUV295=p.xz/float2(4000,6000);
    float watershedDetailMask295=SAMPLE_TEXTURE2D_LOD(_WatershedToneMask295,sampler_GroundPathMask,saturate(watershedDetailUV295),0).r;
    watershedDetailMask295*=step(0,watershedDetailUV295.x)*step(watershedDetailUV295.x,1)*step(0,watershedDetailUV295.y)*step(watershedDetailUV295.y,1);
    resolve=max(resolve,.30*watershedDetailMask295*(1-smoothstep(500,750,distanceWS)));
'''
    surface_text=candidate_surface.read_text(encoding='utf-8-sig') if candidate_surface.exists() else ''
    include_only_change=surface_text.count(inserted)==1 and surface_text.replace(inserted,'')==source_surface.read_text(encoding='utf-8-sig')
    shader_text=candidate_shader.read_text(encoding='utf-8-sig')
    include_active='#include "Assets/_Project/Art/World/Watershed295/Shaders/KoreanSurface295.hlsl"' in shader_text
    result=dict(checked_utc=datetime.now(timezone.utc).isoformat(),height_sha256=sha(GEN/'height.bytes'),
        mask_pixels=list(tone.shape[:2]),tone_nonzero_protected_pixels=int(np.count_nonzero(tone[:,:,0][four])),
        tone_nonzero_within_six_metre_guard=int(np.count_nonzero(tone[:,:,0][tone_guard])),
        canopy_changed_protected_pixels=int(np.count_nonzero(np.any(canopy!=source_canopy,axis=2)&four)),
        tone_nonzero_pixels=int(np.count_nonzero(tone[:,:,0])),canopy_changed_pixels=int(np.count_nonzero(np.any(canopy!=source_canopy,axis=2))),
        bank_mask_size=[width,height],bank_nonzero_protected_guard_pixels=int(np.count_nonzero(bank[:,:,0][bank_guard])),
        bank_nonzero_pixels=int(np.count_nonzero(bank[:,:,0])),lake_props=len(placements),
        lake_props_protected=sum(p['protected'] for p in placements),lake_props_inside_visible_water=sum(bool(p['water_hits']) for p in placements),
        lake_props_min_ground_above_lake_m=min(p['ground_y']-150 for p in placements),
        source_sampler_declarations=len(re.findall(r'\bSAMPLER\(',source_shader.read_text())),
        candidate_sampler_declarations=len(re.findall(r'\bSAMPLER\(',candidate_shader.read_text())),
        private_surface_include_only_bounded_resolve_change=include_only_change,
        private_surface_include_active=include_active,
        private_detail_mask_extra_texture_taps=1,
        source_assets={str(p.relative_to(ROOT)).replace('\\','/'):dict(sha256=sha(p),baseline_sha256=s,unchanged=sha(p)==s) for p,s in originals},
        asset_hashes={str(p.relative_to(ART)).replace('\\','/'):sha(p) for p in [bank_path,ART/'Dressing/Banks.asset',ART/'Dressing/Canopy295.png',ART/'Dressing/WatershedToneMask295.png',candidate_shader,candidate_surface] if p.exists()},
        scope='Full-resolution raw masks and exported exact terrain/water triangles. Prop anchor y may be sunken for contact; dry support is checked at XZ. Mip filtering, visible prop overhang, and GPU cost require live rendering.')
    result['passed']=all(result[k]==0 for k in ['tone_nonzero_protected_pixels','tone_nonzero_within_six_metre_guard','canopy_changed_protected_pixels','bank_nonzero_protected_guard_pixels','lake_props_protected','lake_props_inside_visible_water']) and all(v['unchanged'] for v in result['source_assets'].values()) and result['lake_props_min_ground_above_lake_m']>=.025 and include_only_change and include_active
    out=WORK/'Analysis/dressing-tone-independent.json'
    out.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
    (WORK/'Analysis/lakebank-props-independent.json').write_text(json.dumps(placements,ensure_ascii=False,indent=2),encoding='utf8')
    print(json.dumps(result,ensure_ascii=False,indent=2))
    if not result['passed']:raise SystemExit(1)


if __name__=='__main__':main()
