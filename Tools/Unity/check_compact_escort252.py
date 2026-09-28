"""Bounded live-physics escort probe; requires an already-running private Play.

Does not start Unity, overwrite a save, rebuild a scene or change quest state.
The first leg must already have been started with EscortDrive252.
"""
import argparse
import json
import time
from pathlib import Path
from playtest_polish import call

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/Escort252'
TYPE = 'Oheangbu.EditorTools.WorldMacro.CompactRebuildAuthoring'


def runtime(command):
    return call(TYPE, 'EscortRuntime252', command)


def wait_result(path, timeout):
    deadline = time.monotonic() + timeout
    next_notice = 0
    while time.monotonic() < deadline:
        try:
            result = json.loads(path.read_text(encoding='utf-8-sig'))
        except (OSError, ValueError):
            time.sleep(.5)
            continue
        if result['status'] != 'RUNNING':
            print({k: v for k, v in result.items() if k != 'trail'}, flush=True)
            if result['status'] != 'PASS':
                raise RuntimeError(path)
            return result
        if time.monotonic() >= next_notice:
            print({k: v for k, v in result.items() if k != 'trail'}, flush=True)
            next_notice = time.monotonic() + 30
        time.sleep(1)
    raise TimeoutError(path)


def approach(point):
    path = OUT / f'approach-{point}.json'
    if path.exists():
        path.rename(OUT / f'approach-{point}-previous.json') if not (OUT / f'approach-{point}-previous.json').exists() else path.unlink()
    runtime('approach:' + point)
    wait_result(path, 40)
    time.sleep(3)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--through', choices=['checkpoint_1', 'checkpoint_2', 'cargo_delivery'], default='cargo_delivery')
    args = parser.parse_args()
    for i, stop in enumerate(['checkpoint_1', 'checkpoint_2', 'cargo_delivery']):
        if i:
            runtime('board-approach')
            time.sleep(2)
            runtime('board')
            time.sleep(2)
            runtime('board-check')
            call(TYPE, 'EscortDrive252', stop)
            time.sleep(1)
        wait_result(OUT / f'drive-{stop}.json', 440)
        runtime('exit')
        time.sleep(2)
        runtime('exit-check')
        approach(stop)
        call(TYPE, 'VillageRuntime', 'capture:escort252_' + stop)
        runtime('inspect:' + stop)
        if stop == args.through:
            break
    runtime('status')


if __name__ == '__main__':
    main()
