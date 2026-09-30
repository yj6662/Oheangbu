"""Inspect actual Meshy GLB joint motion; never edits source geometry or animation."""
import json
import struct
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Art/Characters/Folklore298'


def inspect(path):
    data = path.read_bytes()
    length = struct.unpack_from('<I', data, 12)[0]
    doc = json.loads(data[20:20 + length])
    blob = data[28 + length:]
    nodes = doc['nodes']
    parents = {child: i for i, node in enumerate(nodes) for child in node.get('children', [])}
    names = {node.get('name', ''): i for i, node in enumerate(nodes)}
    def accessor(index):
        item = doc['accessors'][index]
        view = doc['bufferViews'][item['bufferView']]
        sizes = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}
        width = sizes[item['type']]
        types = {5126: '<f4', 5125: '<u4', 5123: '<u2', 5121: 'u1'}
        dtype = np.dtype(types[item['componentType']])
        return np.ndarray((item['count'], width), dtype, blob,
            view.get('byteOffset', 0) + item.get('byteOffset', 0),
            strides=(view.get('byteStride', width * dtype.itemsize), dtype.itemsize)).copy()
    def matrix(t, q, s):
        x, y, z, w = q / max(1e-12, np.linalg.norm(q))
        value = np.eye(4)
        value[:3, :3] = np.array([[1-2*y*y-2*z*z, 2*x*y-2*z*w, 2*x*z+2*y*w],
            [2*x*y+2*z*w, 1-2*x*x-2*z*z, 2*y*z-2*x*w],
            [2*x*z-2*y*w, 2*y*z+2*x*w, 1-2*x*x-2*y*y]]) @ np.diag(s)
        value[:3, 3] = t
        return value
    records = []
    for animation in doc.get('animations', []):
        samplers = [(accessor(s['input']).ravel(), accessor(s['output']), s.get('interpolation', 'LINEAR')) for s in animation['samplers']]
        duration = max(float(s[0][-1]) for s in samplers)
        samples = []
        for time in np.linspace(0, duration, 121):
            poses = [{key: np.array(node.get(key, default), dtype=float) for key, default in
                      [('translation',[0,0,0]), ('rotation',[0,0,0,1]), ('scale',[1,1,1])]} for node in nodes]
            for channel in animation['channels']:
                times, values, interpolation = samplers[channel['sampler']]
                if interpolation not in ('LINEAR', 'STEP'):
                    raise ValueError('Unsupported interpolation: ' + interpolation)
                index = int(np.clip(np.searchsorted(times, time) - 1, 0, len(times) - 2))
                alpha = float(np.clip((time - times[index]) / max(1e-9, times[index+1]-times[index]), 0, 1))
                a, b = values[index], values[index+1]
                if interpolation == 'STEP':
                    alpha = 0 if time < times[index+1] else 1
                key = channel['target']['path']
                if key == 'rotation' and np.dot(a,b) < 0:
                    b = -b
                poses[channel['target']['node']][key] = a * (1-alpha) + b * alpha
            world = {}
            def transform(i):
                if i not in world:
                    pose = poses[i]
                    local = matrix(pose['translation'], pose['rotation'], pose['scale'])
                    world[i] = transform(parents[i]) @ local if i in parents else local
                return world[i]
            hip = transform(names['Hips'])[:3, 3]
            hands = [transform(names[n])[:3, 3] - hip for n in ['LeftHand', 'RightHand']]
            samples.append({'phase': float(time/duration), 'left':hands[0].tolist(), 'right':hands[1].tolist()})
        left = max(samples, key=lambda s: s['left'][2])
        right = max(samples, key=lambda s: s['right'][2])
        records.append({'name':animation['name'], 'seconds':duration,
            'forward_axis':'+Z relative to Hips (inspect visually before assigning contact)',
            'left_max_forward_phase':left['phase'], 'right_max_forward_phase':right['phase'],
            'samples':samples})
    return records


if __name__ == '__main__':
    for path in sorted((OUT/'Source').glob('*-motion/result_animation_glb_url.glb')):
        records = inspect(path)
        name = path.parent.name
        (OUT/'Analysis'/f'{name}-joint-samples.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
        print(name, [(r['name'], round(r['left_max_forward_phase'],3),round(r['right_max_forward_phase'],3)) for r in records])
