from pathlib import Path
import json
from original_scope import call
R=Path(__file__).resolve().parents[2];O=R/'Art/SpellVFX120/MetalTiger'
capture=json.loads((O/'capture.json').read_text())
assert capture['status'] not in ['RUNNING'], 'Wait until capture stops before regression checks'
results={name:call(method,None) for name,method in [('WoodDeer','WoodDeerAudit'),('FireHaetae','FireHaetaeAudit')]}
data=dict(status='PASS' if all(value.startswith('PASS ') for value in results.values()) else 'FAIL',results=results)
(O/'regression.json').write_text(json.dumps(data,indent=2));print(data)
