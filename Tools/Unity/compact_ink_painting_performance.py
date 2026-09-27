"""Three paired 1080p Play measurements of the temporary ink-painting preview.

Run only after the root has entered an isolated, unpaused compact Play session.
Uses the existing serial Playtest queue and runtime before/after/restore bridge.
Never enters/exits Play, saves a scene, seeds a save, or prepares/prewarms assets.
Each sample is the existing 49-second actual-frame diagnostic, not RenderRequest
wall time. Editor/world overhead is included. Keep Unity source edits and other
queue writers idle for the complete run.
"""
from pathlib import Path
from datetime import datetime, timezone
import argparse
import contextlib
import hashlib
import io
import json
import time
import uuid

import world_macro


ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'Art/World/WorldMacro/Compact/InkLandscape/InkPaintingStudy/FlowRevision/Performance'
SCENE = ROOT / 'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Compact.unity'
PREFIX = 'chapter3:compact:recovery:'
PHASES = ('cold', 'stationary', 'rotation', 'first_visit', 'return_visit', 'returned_stationary')
POPULATION = ('trees', 'shrubs', 'rocks', 'grass', 'lowGrass', 'cover', 'fixedItems')
world_macro.OUT = ROOT / 'Art/World/WorldMacro/Playtest'


def call(command):
    # The transport can time out while Unity is still processing the same UUID.
    # Keep waiting for that exact response; never restart or reissue the command.
    print(f'QUEUE {command}', flush=True)
    try:
        with contextlib.redirect_stdout(io.StringIO()):
            response = world_macro.call('Demo', PREFIX + command)
    except TimeoutError as error:
        response_path = Path(error.args[0])
        while not response_path.exists():
            print(f'WAIT same pending response {response_path.name}; no command reissued', flush=True)
            time.sleep(15)
        response = json.loads(response_path.read_text(encoding='utf-8-sig'))
        if response.get('status') != 'COMPLETE':
            raise RuntimeError(response)
    return response['result']


def scene_hash():
    return hashlib.sha256(SCENE.read_bytes()).hexdigest()


def write_json(path, value):
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + '\n', encoding='utf-8')
    temporary.replace(path)


def measured(value):
    return isinstance(value, (int, float)) and value > 0


def brief(data):
    last = data['samples'][-1]
    return {
        'utc': data['utc'], 'status': data['status'],
        'width': data['width'], 'height': data['height'],
        'restored': data['restored'], 'commit': data.get('commit'),
        'sampleCount': len(data['samples']), 'phases': data['phases'],
        'cpuTiming': 'MEASURED' if all(measured(p.get('cpuP50')) for p in data['phases']) else 'PARTIAL_OR_UNAVAILABLE',
        'gpuTiming': 'MEASURED' if all(measured(p.get('gpuP50')) for p in data['phases']) else 'PARTIAL_OR_UNAVAILABLE',
        'lastResidentPopulation': {key: last.get(key) for key in POPULATION},
        'lastPendingChunks': last.get('pending'),
        'lastDraws': last.get('draws'), 'lastSubmittedInstances': last.get('instances'),
        'finalRenderCost': {key: data.get('finalRenderCost', {}).get(key) for key in
                            ('residentInstances', 'drawCalls', 'submittedInstances', 'visibleTriangles', 'billboardInstances')},
    }


def measure(side, number, folder, state):
    start_utc = datetime.now(timezone.utc)
    started = time.monotonic()
    result = call('natural:performance:stream:ink')
    if not result.startswith('RUNNING'):
        raise RuntimeError(f'Unexpected start response: {result}')
    state['measurement_active'] = True
    print(f'START {side} pair={number}: {result}', flush=True)
    while True:
        time.sleep(5)
        raw = call('natural:performance:poll')
        if raw.startswith('RUNNING'):
            print(f'{side} pair={number} elapsed={time.monotonic() - started:.0f}s {raw}', flush=True)
            continue
        data = json.loads(raw)
        state['measurement_active'] = False
        path = folder / f'performance_{side}_{number}.json'
        write_json(path, data)  # Preserve failed/partial evidence as well as completed runs.
        problems = []
        if data.get('status') != 'MEASURED_DIAGNOSTIC_CAMERA' or data.get('error'):
            problems.append(f"status={data.get('status')} error={data.get('error')}")
        if (data.get('width'), data.get('height')) != (1920, 1080) or data.get('mode') != 'stream':
            problems.append('Not a 1920x1080 streaming measurement')
        if not data.get('restored'):
            problems.append('Diagnostic camera/renderer restoration not confirmed')
        if not data.get('samples') or any(not any(p.get('phase') == name and p.get('count', 0) > 0
                                                 for p in data.get('phases', [])) for name in PHASES):
            problems.append('Missing actual samples or diagnostic phases')
        utc = datetime.fromisoformat(data['utc'].replace('Z', '+00:00'))
        if utc < start_utc:
            problems.append('Stale measurement receipt')
        if problems:
            raise RuntimeError('; '.join(problems) + f'; receipt={path}')
        summary = brief(data)
        print(f"DONE {side} pair={number} frames={summary['sampleCount']} CPU={summary['cpuTiming']} GPU={summary['gpuTiming']} receipt={path}", flush=True)
        for phase in data['phases']:
            cpu = f"{phase['cpuP50']:.3f}" if measured(phase.get('cpuP50')) else 'UNAVAILABLE'
            gpu = f"{phase['gpuP50']:.3f}" if measured(phase.get('gpuP50')) else 'UNAVAILABLE'
            print(f"  {phase['phase']}: CPU p50={cpu} ms GPU p50={gpu} ms frame p95={phase['frameP95']:.3f} ms", flush=True)
        return data, summary


def compare_pair(number, before, after):
    keys = ('width', 'height', 'mode', 'start', 'unityVersion', 'gpuDevice', 'cpuDevice',
            'quality', 'vSync', 'streamingSourceSha256')
    differences = [key for key in keys if before.get(key) != after.get(key)]
    phases = []
    for first in before['phases']:
        second = next(p for p in after['phases'] if p['phase'] == first['phase'])
        row = {'phase': first['phase']}
        for metric in ('cpuP50', 'cpuP95', 'gpuP50', 'gpuP95', 'frameP50', 'frameP95'):
            a, b = first.get(metric), second.get(metric)
            row[metric + 'DeltaMs'] = b - a if measured(a) and measured(b) else None
        phases.append(row)
    a, b = before['samples'][-1], after['samples'][-1]
    return {
        'pair': number, 'comparisonContext': 'MATCH' if not differences else 'MISMATCH',
        'contextDifferences': differences, 'phaseDeltas': phases,
        'lastResidentPopulationEqual': all(a.get(key) == b.get(key) for key in POPULATION),
        'bothLastPendingChunksZero': a.get('pending') == 0 and b.get('pending') == 0,
        'populationScope': 'Observed streaming residency, not authored placement/ID proof. Material preview preservation receipts must independently verify unchanged authored populations.',
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pairs', type=int, default=3, help='Sequential before/after pairs; default 3 (about five to six minutes)')
    args = parser.parse_args()
    if args.pairs < 1:
        parser.error('--pairs must be positive')
    stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '_' + uuid.uuid4().hex[:8]
    folder = OUTPUT / stamp
    folder.mkdir(parents=True, exist_ok=False)
    state = {'measurement_active': False, 'comparison_active': False}
    report = {'status': 'RUNNING', 'utc': datetime.now(timezone.utc).isoformat(), 'requestedPairs': args.pairs,
              'scope': 'Actual Editor Play 1920x1080 frame timings; 49 seconds per side, no prewarm, cold/rotation/translation/return. Includes world and Editor costs; not standalone certification. Missing GPU values are unavailable, not zero.',
              'scene': str(SCENE), 'sceneSha256Before': scene_hash(), 'measurements': [], 'pairs': [],
              'restored': False, 'error': None, 'cleanupErrors': []}
    print(f'OUTPUT {folder}', flush=True)
    try:
        # Never adopt, abort or restart another task's live measurement.
        if call('natural:performance:poll').startswith('RUNNING'):
            raise RuntimeError('Another diagnostic is running; poll its existing job before starting this script')
        for number in range(1, args.pairs + 1):
            pair = {}
            for side in ('before', 'after'):
                print(call('ink-study:performance-' + side), flush=True)
                state['comparison_active'] = True
                data, summary = measure(side, number, folder, state)
                report['measurements'].append({'pair': number, 'side': side, **summary})
                pair[side] = data
                write_json(folder / 'summary.json', report)
            comparison = compare_pair(number, pair['before'], pair['after'])
            report['pairs'].append(comparison)
            if comparison['contextDifferences']:
                raise RuntimeError('Before/after measurement settings changed: ' + ', '.join(comparison['contextDifferences']))
        report['status'] = 'MEASURED_PAIRED_DIAGNOSTIC'
    except BaseException as error:
        report['status'] = 'INCOMPLETE'
        report['error'] = f'{type(error).__name__}: {error}'
        raise
    finally:
        if state['measurement_active']:
            try:
                print(call('natural:performance:abort'), flush=True)
                state['measurement_active'] = False
            except BaseException as error:
                report['cleanupErrors'].append(f'Diagnostic abort failed: {error}')
        if state['comparison_active'] and not state['measurement_active']:
            try:
                print(call('ink-study:restore'), flush=True)
                report['restored'] = True
            except BaseException as error:
                report['cleanupErrors'].append(f'Material restoration failed: {error}')
        report['sceneSha256After'] = scene_hash()
        report['sceneFileUnchanged'] = report['sceneSha256After'] == report['sceneSha256Before']
        if report['cleanupErrors'] or not report['sceneFileUnchanged']:
            report['status'] = 'FAILED_RESTORATION_OR_SCENE_CHANGE'
        write_json(folder / 'summary.json', report)
        print(f"FINAL {report['status']} restored={report['restored']} sceneFileUnchanged={report['sceneFileUnchanged']} {folder / 'summary.json'}", flush=True)
    if report['status'] != 'MEASURED_PAIRED_DIAGNOSTIC':
        raise RuntimeError(report['status'])


if __name__ == '__main__':
    main()
