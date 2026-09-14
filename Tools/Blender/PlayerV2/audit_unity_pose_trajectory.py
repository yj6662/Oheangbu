"""Validate recorded matrices against separate actual wrist snapshots; no asset edits."""
import hashlib
import json
import sys
from pathlib import Path
import numpy as np

folder = Path(sys.argv[1])
header = json.loads((folder / 'actual-bone-trajectory.json').read_text(encoding='utf-8-sig'))
binary = folder / 'actual-bone-trajectory.bin'
assert header['format'] == 'DOSA_V2_BONE_MATRICES_LE_V1'
assert header['complete'] is True
assert hashlib.sha256(binary.read_bytes()).hexdigest() == header['binarySha256']
nw, nn = len(header['worldBoneNames']), len(header['nearBoneNames'])
dtype = np.dtype([('ordinal', '<i4'), ('world', '<f4', (nw, 4, 4)), ('near', '<f4', (nn, 4, 4))])
assert dtype.itemsize == header['recordBytes']
rows = np.fromfile(binary, dtype=dtype)
assert binary.stat().st_size == dtype.itemsize * len(rows)
assert len(rows) == header['records'] == 464
assert np.array_equal(rows['ordinal'], np.arange(464))
for variant in ('world', 'near'):
    assert np.isfinite(rows[variant]).all()
    assert np.array_equal(rows[variant][:, :, 3, :], np.broadcast_to([0, 0, 0, 1], rows[variant][:, :, 3, :].shape))
comparisons = []
for ordinal, label in [(64, 'rapid_turn'), (153, 'circle')]:
    path = folder / f'{ordinal:03d}_{label}_wrist_snapshot.json'
    snapshot = json.loads(path.read_text(encoding='utf-8-sig'))
    errors = []
    for skin in snapshot['skins']:
        assert skin['assetSha256'] == header['nearModelSha256']
        for bone in skin['bones']:
            i = header['nearBoneNames'].index(bone['name'])
            expected = np.asarray(bone['localToWorld'], dtype=np.float32).reshape(4, 4)
            errors.append(float(np.max(np.abs(rows[ordinal]['near'][i] - expected))))
    assert max(errors) <= 1e-6, (ordinal, max(errors))
    comparisons.append({'ordinal': ordinal, 'boneMatrixComparisons': len(errors), 'maximumElementError': max(errors),
                        'snapshotSha256': hashlib.sha256(path.read_bytes()).hexdigest()})
report = {'status': 'PASS_RECORDED_MATRIX_INTEGRITY_ONLY', 'scope': 'No skin or rig quality assertion. Two independently serialized final snapshots cross-check the near records; world matrices have structural validation only here.',
          'records': len(rows), 'worldBones': nw, 'nearBones': nn, 'binarySha256': header['binarySha256'], 'comparisons': comparisons}
(folder / 'trajectory-integrity.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
