"""Single-editor, screenshot-only macro-world authoring transport."""
import argparse
import hashlib
import json
import time
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/WorldMacro'
PRESERVED = [
    'Oheangbu/Assets/_Project/Scenes/Dev/C2_CodexWorld.unity',
    'Oheangbu/Assets/_Project/Scenes/World/W_Cheongrim_Prologue.unity',
    'Oheangbu/Assets/_Project/Data/World/CodexWorld_Settings.asset',
    'Oheangbu/Assets/_Project/Art/CodexWorld/Codex_NoBloom.asset',
    'Oheangbu/Assets/_Project/Art/World/Prologue/Post.asset',
    'Oheangbu/Assets/_Project/Art/World/Prologue/Content.asset',
    'Oheangbu/Assets/_Project/Prefabs/Rig/PlayerRig.prefab',
    'Oheangbu/Assets/_Project/Data/Configs/SpellBook_Proto.asset',
]

def preservation(baseline=False):
    OUT.mkdir(parents=True, exist_ok=True)
    current = {p: hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in PRESERVED}
    path = OUT/'baseline.json'
    if baseline:
        if path.exists():
            raise RuntimeError('Preservation baseline already exists; refusing to overwrite')
        path.write_text(json.dumps(current, indent=2), encoding='utf-8')
    else:
        original = json.loads(path.read_text(encoding='utf-8'))
        result = {p: dict(unchanged=original[p] == digest, sha256=digest) for p, digest in current.items()}
        (OUT/'preservation.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
        print(json.dumps(result, indent=2))

def call(method, argument=''):
    OUT.mkdir(parents=True, exist_ok=True)
    command = OUT/'command.json'
    if command.exists():
        raise RuntimeError('Unconsumed macro command exists; inspect before retrying')
    token = uuid.uuid4().hex
    pending = OUT/(token+'.pending')
    pending.write_text(json.dumps(dict(id=token, method=method, argument=argument)), encoding='utf-8')
    pending.replace(command)
    response = OUT/('response_'+token+'.json')
    print(str(response), flush=True)
    # A player build may spend several minutes compiling shaders. Keep its single request alive;
    # the caller can yield/poll this process without submitting a duplicate Unity build.
    deadline = time.monotonic()+(3600 if (method == 'Release' and argument == 'build') or (method == 'Game' and argument == 'BuildJourney') else 300)
    # Unity can create a response before its JSON has finished writing. Keep
    # reading this exact request's response; never resubmit a consumed command.
    while True:
        try:
            result = json.loads(response.read_text(encoding='utf-8-sig'))
            break
        except (FileNotFoundError, json.JSONDecodeError, PermissionError):
            if time.monotonic()>deadline:
                raise TimeoutError(response)
            time.sleep(.5)
    print(json.dumps(result, ensure_ascii=False), flush=True)
    if result['status']!='COMPLETE':
        raise RuntimeError(result)
    return result

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('method')
    parser.add_argument('argument', nargs='?', default='')
    args=parser.parse_args()
    if args.method=='Baseline': preservation(True)
    elif args.method=='Preservation': preservation()
    else: call(args.method,args.argument)
