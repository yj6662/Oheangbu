"""Copy actual Unity review evidence; never infer a pass from a file existing."""
from pathlib import Path
import argparse
import json
import shutil

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Art/World/WorldMacro/Playtest'
OUT = ROOT / 'Art/UIAudio/PlaytestFeedback'
parser = argparse.ArgumentParser()
parser.add_argument('kind', choices=['attempt', 'hud-live', 'hud-states', 'summons', 'installation'])
args = parser.parse_args()
validation = OUT / 'Validation'
shots = OUT / 'Screenshots'
validation.mkdir(exist_ok=True)
shots.mkdir(exist_ok=True)

def copy_json(source, name):
    data = json.loads(source.read_text(encoding='utf-8-sig'))
    shutil.copy2(source, validation / name)
    return data

if args.kind == 'attempt':
    attempts = validation / 'Attempts'
    attempts.mkdir(exist_ok=True)
    source = OUT / 'runtime_audio.json'
    target = attempts / 'audio_first_attempt_fixed_delay_finding.json'
    if source.exists() and not target.exists():
        shutil.copy2(source, target)
elif args.kind == 'installation':
    for source in (SOURCE/'HUD').glob('hud_*.json'):
        copy_json(source, source.name)
    # Audio installation/validation uses text reports with explicit checks.
    for source in (SOURCE/'Audio').glob('*'):
        if source.is_file() and source.suffix in ('.txt', '.json'):
            shutil.copy2(source, validation / ('audio_' + source.name))
elif args.kind in ('hud-live', 'hud-states'):
    folder = SOURCE / 'HUD/RuntimeReview'
    prefix = 'hud_live' if args.kind == 'hud-live' else 'hud_states'
    data = copy_json(folder/'runtime_review.json', prefix+'_runtime_review.json')
    candidates = data.get('stills', []) or [data.get('screenshot', '')]
    for name in candidates:
        if not name:
            continue
        source = Path(name)
        if not source.is_absolute():
            source = folder / source.name
        if source.exists():
            shutil.copy2(source, shots / (prefix+'_'+source.name))
    print(json.dumps({'status': data['status'], 'injected': data.get('presentationStateInjected')}, ensure_ascii=False))
elif args.kind == 'summons':
    folder = SOURCE/'SummonCast'
    data = copy_json(folder/'runtime_review.json', 'summon_runtime_review.json')
    # Existing authored summon stills are retained at their original review path.
    print(json.dumps({'status': data['status'], 'rows': len(data.get('rows', []))}, ensure_ascii=False))
