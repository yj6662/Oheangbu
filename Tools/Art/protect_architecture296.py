"""Preserve original worlds and the complete #295 candidate while building #296."""
from pathlib import Path
import hashlib, json, sys, subprocess, zipfile
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Architecture296'
TREES=['Oheangbu/Assets/_Project/Art/World/Watershed295',
       'Oheangbu/Assets/_Project/Art/World/Reworld292',
       'Oheangbu/Assets/_Project/Art/World/MountainTrail285']
FILES=['Oheangbu/Assets/_Project/Scenes/World/W_Demo_Compact.unity',
       'Oheangbu/Assets/_Project/Art/World/WorldCompact/Data/03_Content.asset',
       'Oheangbu/ProjectSettings/EditorBuildSettings.asset']
def sha(p):
    h=hashlib.sha256()
    with p.open('rb') as f:
        for block in iter(lambda:f.read(1<<20),b''):h.update(block)
    return h.hexdigest()
def snapshot():
    paths=[p for d in TREES for p in (ROOT/d).rglob('*') if p.is_file()]
    paths += [ROOT/p for p in FILES if (ROOT/p).exists()]
    return {p.relative_to(ROOT).as_posix():sha(p) for p in sorted(set(paths))}
if __name__=='__main__':
    OUT.mkdir(parents=True,exist_ok=True)
    base=OUT/'Baseline/protected.json'
    if len(sys.argv)>1 and sys.argv[1]=='record':
        if base.exists():raise SystemExit('Existing baseline retained')
        base.parent.mkdir(parents=True,exist_ok=True)
        data=snapshot();base.write_text(json.dumps(data,indent=1),encoding='utf8')
        (base.parent/'git-status.txt').write_bytes(subprocess.check_output(['git','status','--porcelain'],cwd=ROOT))
        with zipfile.ZipFile(base.parent/'Candidate295.zip','w',zipfile.ZIP_DEFLATED,compresslevel=1) as z:
            for p in (ROOT/TREES[0]).rglob('*'):
                if p.is_file():z.write(p,p.relative_to(ROOT).as_posix())
        print(json.dumps({'recorded':len(data),'backup':str(base.parent/'Candidate295.zip')}))
    else:
        data=json.loads(base.read_text(encoding='utf8'));now=snapshot()
        changed=[p for p,h in data.items() if now.get(p)!=h]
        result={'files':len(data),'changed':changed,'missing':[p for p in changed if p not in now],'passed':not changed}
        (OUT/'protection-check.json').write_text(json.dumps(result,indent=1),encoding='utf8')
        print(json.dumps(result,ensure_ascii=False));sys.exit(bool(changed))
