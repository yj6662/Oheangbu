"""Derive exact original top-level component reference bindings for compact repair."""
from pathlib import Path
import json,re,yaml
ROOT=Path(__file__).resolve().parents[2];PROJ=ROOT/'Oheangbu';OUT=ROOT/'Art/World/WorldMacro/Compact'
p=json.loads((OUT/'progress.json').read_text(encoding='utf-8'))
byguid={re.search(r'^guid: (\w+)',(PROJ/(a['source']+'.meta')).read_text(),re.M)[1]:a['target'] for a in p['assets']}
records=[];doc=[];identifier=''
def consume():
    source=''.join(doc)
    if not source.startswith('MonoBehaviour:') or not any(g in source for g in byguid):return
    v=yaml.safe_load(source)['MonoBehaviour']
    for field,reference in v.items():
        if isinstance(reference,dict) and reference.get('guid') in byguid:
            records.append(dict(componentId=identifier,field=field,target=byguid[reference['guid']]))
    # Fail instead of pretending nested matches were covered.
    expected=sum(source.count('guid: '+g) for g in byguid)
    actual=sum(r['componentId']==identifier for r in records)
    if actual!=expected:raise RuntimeError(f'Nested coordinate reference needs explicit remap: {identifier} {actual}/{expected}')
with (PROJ/'Assets/_Project/Scenes/World/W_Demo_Campaign.unity').open(encoding='utf-8-sig') as f:
    for line in f:
        if line.startswith('---'):
            consume();doc=[];identifier=re.search(r'&([0-9]+)',line)[1]
        elif not line.startswith('%'):doc.append(line)
    consume()
(OUT/'scene_reference_bindings.json').write_text(json.dumps(dict(bindings=records),indent=2),encoding='utf-8')
print(json.dumps(records,indent=2))
