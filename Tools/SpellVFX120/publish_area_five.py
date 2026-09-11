"""Encode sequentially with two threads; preserve prior review pages and supplier assets."""
import json,subprocess,hashlib,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/SpellVFX120/AreaFive'
ff=next((Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries').glob('*.exe'))
rows=[]
for glyph in '고노소모오':
    r=json.loads((OUT/glyph/'report.json').read_text(encoding='utf-8'))
    if r['status']!='PASS_RUNTIME_FIXTURE_VISUAL_PENDING':raise RuntimeError(r)
    cast_at={'고':.28,'노':.25,'소':1.48,'모':.38,'오':.43}[glyph]
    for clip in r['clips']:
        frames=Path(clip['folder']);mp4=frames.with_suffix('.mp4')
        if not mp4.exists() or mp4.stat().st_mtime<max(x.stat().st_mtime for x in frames.glob('*.jpg')):
            subprocess.run([str(ff),'-y','-v','error','-threads','2','-framerate','24','-i',str(frames/'%04d.jpg'),'-c:v','libx264','-threads','2','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(mp4)],check=True)
        subprocess.run([str(ff),'-v','error','-threads','2','-i',str(mp4),'-f','null','-'],check=True)
        for label,index in [('cast',round(cast_at*24)),('contact',round((clip['actualHitAt']+.08)*24))]:
            shutil.copyfile(frames/f'{index:04d}.jpg',frames.parent/(frames.name+'_'+label+'.jpg'))
        print(mp4,flush=True)
    r['castAt']=cast_at;rows.append(r)
hashes=json.loads((OUT/'before_hashes.json').read_text(encoding='utf-8'))
changed=[p.replace('\\','/') for p,h in hashes.items() if hashlib.sha256((ROOT/p).read_bytes()).hexdigest()!=h]
expected={f'Oheangbu/Assets/_Project/Art/SpellVFX120/Profiles/{name}.asset' for name in ['013_ACE0','037_B178','061_BAA8','085_C18C','109_C624']}
if set(changed)!=expected:raise RuntimeError(changed)
summary=dict(status='PASS',changed=changed,vendorUnchanged=True,profilesOutsideFiveUnchanged=True,clips=20,stills=40,maxCommitRatio=max(c['commitRatio'] for r in rows for c in r['clips']),mvids=sorted({r['mvid'] for r in rows}))
(OUT/'scope.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
page=(ROOT/'Tools/SpellVFX120/area_five_review.html').read_text(encoding='utf-8')
(OUT.parent/'AREA_FIVE_REVIEW.html').write_text(page.replace('__REPORTS__',json.dumps(rows,ensure_ascii=False)),encoding='utf-8')
print(json.dumps(summary,ensure_ascii=False),flush=True)
