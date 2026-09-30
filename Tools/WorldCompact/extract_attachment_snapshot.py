"""Read-only original foliage matrices for the repeatable compact final grounding pass."""
from pathlib import Path
import hashlib, json, os, re, yaml
ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'Oheangbu'
OUTPUT = ROOT / 'Art/World/WorldMacro/Compact'
SCENE = PROJECT / 'Assets/_Project/Scenes/World/W_Demo_Campaign.unity'
GUID = 'abab08caa55afb540ac6f3ddc5a22c64'
inventory = json.loads((OUTPUT / 'content_audit.json').read_text(encoding='utf-8'))
source_hash = hashlib.sha256(SCENE.read_bytes()).hexdigest()
assert source_hash == inventory['sceneSha256']
paths = {str(c['fileID']): c['path'] for c in inventory['components'] if c['type'] == 'Oheangbu.App.World.Dressing.EarlyRegionFoliage'}
components = []
text = SCENE.read_text(encoding='utf-8-sig')
for match in re.finditer(r'^--- !u!114 &(-?\d+)[^\n]*\n(.*?)(?=^--- !u!|\Z)', text, re.M | re.S):
    if 'guid: ' + GUID not in match[2]:
        continue
    data = yaml.load(match[2], Loader=yaml.CSafeLoader)['MonoBehaviour']
    packets = []
    def parts(source):
        result = []
        for part in source:
            result.append(dict(meshGuid=part['Mesh'].get('guid',''), meshFileId=part['Mesh']['fileID'], materialGuid=part['Material'].get('guid',''), materialFileId=part['Material']['fileID'], submesh=part['Submesh'], matrices=[dict(values=[m['e'+str(row)+str(col)] for row in range(4) for col in range(4)]) for m in part['Matrices']]))
        return result
    for packet in data['Packets']:
        packets.append(dict(center=packet['Bounds']['m_Center'], extents=packet['Bounds']['m_Extent'], near=parts(packet['Near']), far=parts(packet['Far']), distance=packet['Distance'], count=packet['Count']))
    components.append(dict(componentId=match[1], path=paths[match[1]], packets=packets))
assert len(components) == len(paths)
packet_count = sum(len(c['packets']) for c in components)
matrix_count = sum(len(p['matrices']) for c in components for pack in c['packets'] for p in pack['near']+pack['far'])
result=dict(sourceSceneHash=source_hash, componentCount=len(components), packetCount=packet_count, matrixCount=matrix_count, scope='Exact original YAML affine matrices/bounds; read-only extraction, no Unity execution.', components=components)
target=OUTPUT/'original_attachment_snapshot.json'; temporary=target.with_suffix('.json.tmp')
temporary.write_text(json.dumps(result,ensure_ascii=False,separators=(',',':')),encoding='utf-8');os.replace(temporary,target)
assert hashlib.sha256(SCENE.read_bytes()).hexdigest() == source_hash
print(json.dumps(dict(path=str(target),components=len(components),packets=packet_count,matrices=matrix_count,sha256=hashlib.sha256(target.read_bytes()).hexdigest())))
