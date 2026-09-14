"""Verify exported layout evidence and local review links without running a traversal."""
import json
import struct
import re
from pathlib import Path

root=Path(__file__).resolve().parents[2]
out=root/'Art/World/WorldMacro/Content'
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
placement=load(out/'placement.json')
geo=load(out.parent/'sheet.json')
assert len(placement['entries'])==83
assert all('Points' in r for r in geo['Ridges'])
assert not any(x.startswith('FAIL') for x in placement['checks'])
assert 'FAIL' not in (out/'play_checks.txt').read_text(encoding='utf-8')
shots=list(out.glob('*.png'))
assert len(shots)==9
assert all(struct.unpack('>II',s.read_bytes()[16:24])==(1920,1080) for s in shots)
page=(out/'REVIEW.html').read_text(encoding='utf-8')
links=re.findall(r'(?:href|src)="([^"]+)"',page)
assert all((out/x).exists() for x in links)
assert not load(out/'scene_preservation.json')['changedExcludingDressingDiagnosticCounters']
assert all(x['unchanged'] for x in load(out.parent/'preservation.json').values())
assert len(set(e['Realm'] for e in placement['entries']))==5
checks=['PASS 83 exported positions across five realms', 'PASS placement and Play check evidence',
        'PASS 9 PNG files at 1920x1080', 'PASS local review links and map schema',
        'PASS existing scene geometry/settings and eight legacy assets preserved',
        'NOT_TESTED browser UI interactions: file URL browser automation restricted; local page delivered']
(out/'delivery_checks.txt').write_text('\n'.join(checks)+'\n',encoding='utf-8')
print('\n'.join(checks))
