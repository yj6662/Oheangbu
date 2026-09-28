"""Three1080p pairs comparing MATERIAL COST ONLY on the same saved compact scene.

Root first finishes smoke/new-navigation validation, then separately starts isolated
unpaused Play (recovery:drive-begin; NOT drive-setup). This runner never starts/stops
Play, moves the player, saves a scene, changes a save slot or prewarms resources.
It reuses the existing49-second diagnostic camera and serial queue transport.
Keep all other Unity queue writers and Assets edits idle for the entire run.
"""
from datetime import datetime, timezone
from pathlib import Path
import argparse
import hashlib
import json
import shutil
import uuid

import compact_ink_painting_performance as shared

ROOT = Path(__file__).resolve().parents[2]
FLOW = ROOT / 'Art/World/WorldMacro/Compact/InkLandscape/InkPaintingStudy/FlowRevision'
OUTPUT = FLOW / 'MaterialCostPerformance'
SWAPS = FLOW / 'SavedMaterialPerformance'
NAV = FLOW / 'NavigationUpdate/progress.json'
SCOPE = ('MATERIAL_COST_ONLY: identical saved geometry/deformation/retention/placement/sky profile; '
         'before=205 receipt-original materials, after=205 current saved study derivatives. '
         'Actual Editor/world cost is included in each49-second1080p run; its paired delta is '
         'a material/renderer-path comparison, not total geometry+look improvement. '
         'No prewarm, player traversal, video or build; GPU unavailable is not zero.')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def navigation_identity():
    gate = json.loads(NAV.read_text(encoding='utf-8-sig'))
    if not (gate.get('installed') and gate.get('sceneSaved') and
            gate.get('status') == 'INSTALLED_SAVED_EDIT_PATHS_VERIFIED' and gate.get('next') == 20):
        raise RuntimeError('Install and validate the new saved navigation generation before this comparison')
    return {'receiptSha256': digest(NAV), 'generation': gate.get('generation'),
            'geometryGeneration': gate.get('geometryGeneration'), 'meshSignature': gate.get('meshSignature')}


def swap_files():
    return set(SWAPS.glob('*.json')) if SWAPS.exists() else set()


def copy_swap_receipts(folder, earlier, expected_status, scene_hash):
    copied = []
    for path in sorted(swap_files() - earlier):
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        target = folder / 'swap_receipts' / path.name
        target.parent.mkdir(exist_ok=True)
        shutil.copy2(path, target)
        copied.append({'path': str(target.relative_to(folder)), 'status': data.get('status'),
                       'sceneHash': data.get('sceneHash'), 'mappingCount': data.get('mappingCount')})
        if data.get('status') != expected_status or data.get('sceneHash') != scene_hash:
            raise RuntimeError(f'Swap receipt status/scene mismatch: {target}')
        if data.get('mappingCount') != 205 or not data.get('sourceFilesUnchanged') or not data.get('originalSheetDataUnchanged'):
            raise RuntimeError(f'Swap preservation check failed: {target}')
        if expected_status == 'AFTER_SAVED_RESTORED' and not data.get('bindingsRestored'):
            raise RuntimeError(f'Saved bindings not restored: {target}')
    if not copied:
        raise RuntimeError(f'No fresh {expected_status} swap receipt; refuse old-preview or stale results')
    return copied


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pairs', type=int, default=3)
    args = parser.parse_args()
    if args.pairs < 1:
        parser.error('--pairs must be positive')
    nav = navigation_identity()
    stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '_' + uuid.uuid4().hex[:8]
    folder = OUTPUT / stamp
    folder.mkdir(parents=True, exist_ok=False)
    report = {'status': 'RUNNING', 'scope': SCOPE, 'requestedPairs': args.pairs,
              'utc': datetime.now(timezone.utc).isoformat(), 'sceneSha256Before': shared.scene_hash(),
              'navigationIdentity': nav, 'measurements': [], 'pairs': [], 'restored': False,
              'limitations': [
                  'New geometry and retained population are present on both sides; no whole-revision savings claim.',
                  'Original and study shaders share current common includes; this is not an archived old executable.',
                  'Materials remain loaded across repetitions; shader caches and Unity/editor overhead may affect order.',
                  'Streaming residency may differ transiently with frame time despite identical authored population.',
                  'Same sky profile does not freeze wall-clock wind/cloud animation.',
                  'Fixed camera(907,135.2,208) requires initial clearance check on installed terrain.',
                  'drive-begin has a10Hz state logger; keep it identical on both sides.',
                  'The existing35-second smoke must finish before the approximately5-minute paired run.'],
              'error': None, 'cleanupErrors': []}
    state = {'measurement_active': False, 'comparison_active': False}
    print(f'OUTPUT {folder}\nSCOPE {SCOPE}', flush=True)
    try:
        if shared.call('natural:performance:poll').startswith('RUNNING'):
            raise RuntimeError('Another diagnostic is running; leave it untouched')
        for number in range(1, args.pairs + 1):
            pair = {}
            for side, expected in [('before', 'BEFORE_ORIGINAL_BOUND'), ('after', 'AFTER_SAVED_RESTORED')]:
                if shared.scene_hash() != report['sceneSha256Before'] or navigation_identity() != nav:
                    raise RuntimeError('Saved scene/navigation changed during material-only comparison')
                prior = swap_files()
                # Set ownership before dispatch so a partially failed helper can still
                # be restored by finally. No other preview/queue writer may be active.
                state['comparison_active'] = True
                response = shared.call('ink-study:performance-' + side)
                print(response, flush=True)
                if not response.startswith('MATERIAL_COST_ONLY:'):
                    raise RuntimeError('Saved material-only adapter is not installed; do not use old-preview timing')
                receipts = copy_swap_receipts(folder, prior, expected, report['sceneSha256Before'])
                data, brief = shared.measure(side, number, folder, state)
                pair[side] = data
                report['measurements'].append({'pair': number, 'side': side, 'swapReceipts': receipts, **brief})
                shared.write_json(folder / 'summary.json', report)
            comparison = shared.compare_pair(number, pair['before'], pair['after'])
            comparison['populationScope'] = ('Observed residency may vary with frame budget; saved-sheet and packet '
                                             'preservation is checked separately by swap receipts. Compare settled phases as well as cold/streaming.')
            report['pairs'].append(comparison)
            if comparison['contextDifferences']:
                raise RuntimeError('Measurement context changed: ' + ', '.join(comparison['contextDifferences']))
        report['status'] = 'MEASURED_THREE_PAIRED_MATERIAL_COST' if args.pairs == 3 else 'MEASURED_PAIRED_MATERIAL_COST'
    except BaseException as error:
        report['status'] = 'INCOMPLETE'
        report['error'] = f'{type(error).__name__}: {error}'
        raise
    finally:
        if state['measurement_active']:
            try:
                print(shared.call('natural:performance:abort'), flush=True)
                state['measurement_active'] = False
            except BaseException as error:
                report['cleanupErrors'].append(f'Diagnostic abort failed: {error}')
        if state['comparison_active'] and not state['measurement_active']:
            try:
                print(shared.call('ink-study:restore'), flush=True)
                report['restored'] = True
            except BaseException as error:
                report['cleanupErrors'].append(f'Material restoration failed: {error}')
        report['sceneSha256After'] = shared.scene_hash()
        report['sceneFileUnchanged'] = report['sceneSha256After'] == report['sceneSha256Before']
        report['navigationReceiptUnchanged'] = digest(NAV) == nav['receiptSha256']
        if report['cleanupErrors'] or not report['sceneFileUnchanged'] or not report['navigationReceiptUnchanged']:
            report['status'] = 'FAILED_RESTORATION_OR_SAVED_STATE_CHANGE'
        shared.write_json(folder / 'summary.json', report)
        print(f"FINAL {report['status']} restored={report['restored']} {folder / 'summary.json'}", flush=True)
    if not report['status'].startswith('MEASURED_'):
        raise RuntimeError(report['status'])


if __name__ == '__main__':
    main()
