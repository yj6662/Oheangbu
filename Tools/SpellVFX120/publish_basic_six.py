import json,subprocess,hashlib,shutil,re
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/SpellVFX120/BasicSix'
ff=next((Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries').glob('*.exe'))
rows=[]
for glyph in '사마아서머어':
    r=json.loads((OUT/glyph/'report.json').read_text(encoding='utf-8'))
    if r['status']!='PASS_RUNTIME_FIXTURE_VISUAL_PENDING':raise RuntimeError(r)
    for clip in r['clips']:
        frames=Path(clip['folder']);mp4=frames.with_suffix('.mp4')
        if not mp4.exists() or mp4.stat().st_mtime<max(x.stat().st_mtime for x in frames.glob('*.jpg')):
            subprocess.run([str(ff),'-y','-v','error','-threads','2','-framerate','24','-i',str(frames/'%04d.jpg'),'-c:v','libx264','-threads','2','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(mp4)],check=True)
        subprocess.run([str(ff),'-v','error','-threads','2','-i',str(mp4),'-f','null','-'],check=True)
        for label,index in [('cast',2),('contact',round((clip['actualHitAt']+.125)*24))]:
            shutil.copyfile(frames/f'{index:04d}.jpg',frames.parent/(frames.name+'_'+label+'.jpg'))
        print(mp4,flush=True)
    rows.append(r)
hashes=json.loads((OUT/'before_hashes.json').read_text(encoding='utf-8'))
changed=[p for p,h in hashes.items() if hashlib.sha256((ROOT/p).read_bytes()).hexdigest()!=h]
expected={f'Oheangbu/Assets/_Project/Art/SpellVFX120/Profiles/{name}.asset' for name in ['073_C0AC','049_B9C8','097_C544','079_C11C','055_BA38','103_C5B4']}
if {p.replace('\\','/') for p in changed}!=expected:raise RuntimeError(changed)
summary=dict(status='PASS',changed=changed,vendorUnchanged=True,profilesOutsideSixUnchanged=True,clips=24,maxCommitRatio=max(c['commitRatio'] for r in rows for c in r['clips']),mvids=sorted({r['mvid'] for r in rows}))
preserved=[]
for glyph,name in [('사','073_C0AC'),('마','049_B9C8'),('아','097_C544'),('서','079_C11C'),('머','055_BA38'),('어','103_C5B4')]:
    base=ROOT/'Oheangbu/Assets/_Project/Art/SpellVFX120'
    before=(base/'KtpEmphasis'/f'BaselineBasicSix_{glyph}.asset').read_text(encoding='utf-8')
    after=(base/'Profiles'/f'{name}.asset').read_text(encoding='utf-8')
    for field in ['BodyMesh','BodyMaterial','Flight','Lift','Count','Size','PartScale','Duration','Pigment']:
        get=lambda text:re.search(r'^  '+field+r': (.+)$',text,re.M).group(1)
        if get(before)!=get(after):raise RuntimeError(f'{glyph}: unexpected change to {field}')
        preserved.append(glyph+': '+field)
summary['preservedShapeMotionPaletteChecks']=preserved
(OUT/'scope.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
page=(ROOT/'Tools/SpellVFX120/basic_six_review.html').read_text(encoding='utf-8')
(OUT.parent/'BASIC_SIX_REVIEW.html').write_text(page.replace('__REPORTS__',json.dumps(rows,ensure_ascii=False)),encoding='utf-8')
print(json.dumps(summary,ensure_ascii=False),flush=True)
