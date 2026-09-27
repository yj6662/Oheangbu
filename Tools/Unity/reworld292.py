"""Recovery and serialized Unity editor dispatch for the Korean world rebuild."""
from pathlib import Path
import argparse, hashlib, json, shutil, subprocess, time, uuid, zipfile
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Reworld292'
QUEUE=ROOT/'Art/PlaytestPolish/Unity'

def prepare():
    OUT.mkdir(parents=True,exist_ok=True)
    if (OUT/'recovery.json').exists():
        print('Existing recovery retained'); return
    paths=[]
    for relative in ['Oheangbu/Assets/_Project/Art/World/MountainIntegration290',
                     'Oheangbu/Assets/_Project/Scripts/Editor/WorldMacro',
                     'Oheangbu/Assets/_Project/Scripts/Data/World','Docs']:
        paths.extend(p for p in (ROOT/relative).rglob('*') if p.is_file())
    paths.extend([ROOT/'Oheangbu/ProjectSettings/EditorBuildSettings.asset',
                  ROOT/'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Compact.unity'])
    saves=Path.home()/'AppData/LocalLow'
    # Only this game's save directories, never unrelated applications.
    paths.extend(p for p in saves.glob('*/*/*') if p.is_file() and 'oheangbu' in str(p).lower() and '.json' in p.name)
    manifest={}
    with zipfile.ZipFile(OUT/'Recovery.zip','w',zipfile.ZIP_DEFLATED,compresslevel=1) as z:
        for p in sorted(set(paths)):
            key=str(p.relative_to(ROOT)) if p.is_relative_to(ROOT) else 'Saves/'+p.parent.name+'/'+p.name
            data=p.read_bytes(); manifest[key]=hashlib.sha256(data).hexdigest();z.writestr(key,data)
    (OUT/'git-status.txt').write_bytes(subprocess.check_output(['git','status','--porcelain'],cwd=ROOT))
    (OUT/'recovery.json').write_text(json.dumps(manifest,indent=2),encoding='utf8')
    print('Recovery:',len(manifest),'files; existing uncommitted work preserved')

def call(command,editor=False,wait=50):
    if (QUEUE/'command.json').exists(): raise RuntimeError('Editor queue occupied; do not overwrite')
    ident='reworld292_'+uuid.uuid4().hex[:12]
    request=dict(id=ident,type='editor' if editor else 'Oheangbu.EditorTools.WorldMacro.CompactReworld292',method=command if editor else 'Run',argument='' if editor else command)
    tmp=QUEUE/'reworld292.tmp';tmp.write_text(json.dumps(request),encoding='utf8');tmp.replace(QUEUE/'command.json')
    result=QUEUE/f'response_{ident}.json'
    until=time.monotonic()+wait
    while not result.exists() and time.monotonic()<until:time.sleep(.5)
    print(result.read_text(encoding='utf-8-sig') if result.exists() else 'PENDING '+str(result))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('command');p.add_argument('--editor',action='store_true');p.add_argument('--wait',type=int,default=50);a=p.parse_args()
    if a.command=='prepare-recovery':prepare()
    else:call(a.command,a.editor,a.wait)
