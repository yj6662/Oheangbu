import json,subprocess,hashlib,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/SpellVFX120/Emphasis4'
ff=next((Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries').glob('*.exe'))
rows=[]
for glyph in '가나거너':
    r=json.loads((OUT/(glyph+'_v5')/'report.json').read_text(encoding='utf-8'))
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
(OUT/'offset_guard_scope.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
page=(ROOT/'Tools/SpellVFX120/offset_guard_review.html').read_text(encoding='utf-8')
(OUT.parent/'OFFSET_GUARD_REVIEW.html').write_text(page.replace('__REPORTS__',json.dumps(rows,ensure_ascii=False)),encoding='utf-8')
print(json.dumps(summary,ensure_ascii=False),flush=True)
