"""Serial installed-state captures with disk provenance; never edits Unity assets.

Run after native import. The native command verifies loaded shaders/bindings;
this wrapper additionally fixes the material/include version around each image.
It does not reconstruct provenance for earlier images or claim Play performance.
"""
from pathlib import Path
from datetime import datetime, timezone
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
import re

import compact_ink_painting_performance as bridge

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/WorldMacro/Compact/InkLandscape/InkPaintingStudy/FlowRevision'
ASSETS = ROOT / 'Oheangbu/Assets/_Project'
MATERIALS = ASSETS / 'Art/World/WorldCompact/InkLandscape/InkPaintingStudy/FlowRevision'


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def sources():
    files = list((MATERIALS / 'Materials').glob('*.mat')) + list((MATERIALS / 'FoliageMaterials').glob('*.mat'))
    if len(files) != 205:
        raise ValueError('Expected the205 prepared study materials')
    files += [ASSETS / 'Scenes/World/W_Demo_Compact.unity']
    files += [p for p in (ASSETS / 'Shaders').rglob('*') if p.suffix in ('.shader', '.hlsl') and
              ('InkPaintingStudy' in p.parts or p.name.startswith('CompactInk'))]
    files += [OUT / 'current_geometry_receipt.txt', OUT / 'NavigationUpdate/progress.json']
    return {p.relative_to(ROOT).as_posix(): sha(p) for p in sorted(set(files))}


def commit_percent():
    # GetPerformanceInfo reports system committed pages, not process working set.
    class PerformanceInfo(ctypes.Structure):
        _fields_ = [('cb', wintypes.DWORD)] + [(n, ctypes.c_size_t) for n in (
            'CommitTotal', 'CommitLimit', 'CommitPeak', 'PhysicalTotal', 'PhysicalAvailable',
            'SystemCache', 'KernelTotal', 'KernelPaged', 'KernelNonpaged', 'PageSize')]
        _fields_ += [(n, wintypes.DWORD) for n in ('HandleCount', 'ProcessCount', 'ThreadCount')]
    info = PerformanceInfo()
    info.cb = ctypes.sizeof(info)
    fn = ctypes.WinDLL('psapi', use_last_error=True).GetPerformanceInfo
    fn.argtypes = [ctypes.POINTER(PerformanceInfo), wintypes.DWORD]
    fn.restype = wintypes.BOOL
    if not fn(ctypes.byref(info), info.cb) or not info.CommitLimit:
        raise ctypes.WinError(ctypes.get_last_error())
    return 100 * info.CommitTotal / info.CommitLimit


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--label', required=True)
    parser.add_argument('--views', nargs='+', required=True)
    parser.add_argument('--inspect-only', action='store_true')
    args = parser.parse_args()
    if not re.fullmatch(r'[A-Za-z0-9_-]+', args.label):
        raise ValueError('Unsafe capture label')
    known = {v['id'] for v in json.loads((OUT / 'views.json').read_text(encoding='utf-8-sig'))['views']}
    if set(args.views) - known or len(set(args.views)) != len(args.views):
        raise ValueError('Unknown or duplicate capture views')
    baseline = sources()
    if args.inspect_only:
        print(json.dumps({'files': len(baseline), 'commitPercent': commit_percent(), 'views': args.views})); return
    for view in args.views:
        stem = OUT / f'{args.label}_{view}'
        if stem.with_suffix('.png').exists():
            raise FileExistsError(f'Use a new label; preserving existing capture {stem}')
        memory = commit_percent()
        if memory >= 85:
            raise RuntimeError(f'Capture stopped: system commit {memory:.2f}% >=85%')
        before = sources()
        if before != baseline:
            raise RuntimeError('Disk materials/geometry/shaders changed during this capture set')
        started = datetime.now(timezone.utc).isoformat()
        result = bridge.call(f'ink-study:capture-installed:{args.label}:{view}')
        after = sources()
        check_path = OUT / f'{args.label}_{view}_checks.json'
        checks = json.loads(check_path.read_text(encoding='utf-8-sig')) if check_path.exists() else {}
        passed = before == after and checks.get('status') == 'PASS'
        data = {'status': 'PASS_DISK_VERSION_AND_NATIVE_CAPTURE' if passed else 'FAILED', 'utc': started,
                'label': args.label, 'view': view, 'systemCommitPercentBefore': memory,
                'diskSourcesUnchanged': before == after, 'files': before, 'nativeResult': result,
                'nativeReceipt': check_path.relative_to(ROOT).as_posix(),
                'imageSha256': sha(stem.with_suffix('.png')) if stem.with_suffix('.png').exists() else None,
                'scope': 'Disk source version plus native capture receipt; art approval and gameplay performance excluded.'}
        (OUT / f'{args.label}_{view}_version.json').write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')
        print(json.dumps({'view': view, 'status': data['status'], 'commit': memory}), flush=True)
        if not passed:
            raise RuntimeError(f'Capture validation failed for{view}')


if __name__ == '__main__':
    main()
