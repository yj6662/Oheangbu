"""Read-only audit of candidate authored ribbon meshes; no Unity or asset writes."""
import hashlib
import json
from pathlib import Path
import re
import struct
import yaml

ROOT = Path(__file__).resolve().parents[2]
CANDIDATE = ROOT / 'Oheangbu/Assets/_Project/Art/World/WorldCompact/Rebuild/slice-5e82ecd76d2a'
OUT = ROOT / 'Art/World/Compact/Rebuild/Continuation258/road-surface-audit.json'


def inspect(path):
    raw = path.read_text(encoding='utf-8-sig')
    mesh = yaml.safe_load(re.sub(r'^%.*\n|^--- !u!.*\n', '', raw, flags=re.M)).get('Mesh')
    if not mesh:
        return None
    data = mesh['m_VertexData']
    channels = [c for c in data['m_Channels'] if c['dimension']]
    if any(c['stream'] != 0 or c['format'] != 0 for c in channels):
        raise ValueError(f'Unsupported vertex format: {path}')
    stride = max(c['offset'] + c['dimension'] * 4 for c in channels)
    raw_vertices = bytes.fromhex(str(data['_typelessdata']))
    vertices = [struct.unpack_from('<3f', raw_vertices, i * stride) for i in range(data['m_VertexCount'])]
    normal_offset = data['m_Channels'][1]['offset']
    normals = [struct.unpack_from('<3f', raw_vertices, i * stride + normal_offset) for i in range(data['m_VertexCount'])]
    raw_indices = bytes.fromhex(str(mesh['m_IndexBuffer']))
    index_format = 'I' if mesh.get('m_IndexFormat', 0) == 1 else 'H'
    indices = [i[0] for i in struct.iter_unpack('<' + index_format, raw_indices)]
    down = up = degenerate = 0
    for i in range(0, len(indices), 3):
        a, b, c = [vertices[k] for k in indices[i:i + 3]]
        y = (b[2] - a[2]) * (c[0] - a[0]) - (b[0] - a[0]) * (c[2] - a[2])
        if y < -1e-7:
            down += 1
        elif y > 1e-7:
            up += 1
        else:
            degenerate += 1
    return dict(path=str(path.relative_to(ROOT)), sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                vertices=len(vertices), triangles=len(indices)//3, up=up, down=down, degenerate=degenerate,
                downward_vertex_normals=sum(n[1] < 0 for n in normals))


def run():
    paths = sorted(set((CANDIDATE/'Village245').glob('Yard_*.asset')) |
                   set((CANDIDATE/'Village245').glob('Village_*.asset')) |
                   set((CANDIDATE/'Progression251').glob('*251.asset')))
    rows = [r for p in paths if (r := inspect(p)) is not None]
    report = dict(scope='Stored mesh geometry only; does not prove scene instance use, shading or gameplay collision.', meshes=rows,
                  down=sum(r['down'] for r in rows), up=sum(r['up'] for r in rows))
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k: report[k] for k in ['scope', 'down', 'up']}, ensure_ascii=False))


if __name__ == '__main__':
    run()
