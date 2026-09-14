"""Remove residual arm weights from waist accessories/robe; preserve sleeves/feet.

Runs after the prior garment and wrist passes. Changes vertex weights only; the
caller owns save/export. The matching near-arm vertices and wrist wrap are kept.
"""
import bpy, json, math
from pathlib import Path

ROOT = Path('C:/Users/yj666/Oheangbu')
m = bpy.data.objects['Dosa_Body']
r = bpy.data.objects['Dosa_Rig']
body_ids = {i for p in m.data.polygons if p.material_index == 0 for i in p.vertices}
hand_ids = {i for p in m.data.polygons if p.material_index == 1 for i in p.vertices}
arm_ids = {g.index for g in m.vertex_groups if any(s in g.name for s in ['Arm', 'Hand', 'Shoulder'])}
lower_spine = min((b for b in r.data.bones if b.name.startswith('Spine')),
                  key=lambda b: b.head_local.z).name

def key(v):
    return tuple(round(float(c), 6) for c in v)

near = bpy.data.objects.get('Dosa_Arms')
near_coords = {key(v.co) for v in near.data.vertices} if near else set()
protected = hand_ids | {v.index for v in m.data.vertices if key(v.co) in near_coords}
if m.get('wrist_wrap_collar'):
    protected.update(range(len(m.data.vertices) - 384, len(m.data.vertices)))

def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)

def sleeve_protection(point):
    keep = 0.0
    for side in ['Left', 'Right']:
        for start_name, end_name, inner, outer in [
                ('Arm', 'ForeArm', .100, .130),
                ('ForeArm', 'Hand', .075, .100)]:
            start = r.data.bones[side + start_name].head_local
            end = r.data.bones[side + end_name].head_local
            line = end - start
            t = max(0.0, min(1.0, (point - start).dot(line) / line.length_squared))
            distance = (point - (start + line * t)).length
            keep = max(keep, 1 - smooth((distance - inner) / (outer - inner)))
    return keep

changes = []
if not m.get('waist_arm_weights_v1'):
    for v in m.data.vertices:
        if v.index not in body_ids or v.index in protected or not .72 < v.co.z < 1.22:
            continue
        old = {g.group: g.weight for g in v.groups if g.weight > .000001}
        arm_weight = sum(w for i, w in old.items() if i in arm_ids)
        if arm_weight < .002:
            continue
        factor = (1 - sleeve_protection(v.co)) * (1 - smooth((v.co.z - 1.10) / .12))
        if factor < .005:
            continue
        removed = arm_weight * factor
        weights = {i: w * (1 - factor if i in arm_ids else 1) for i, w in old.items()}
        torso = {i: w for i, w in old.items() if i not in arm_ids}
        torso_total = sum(torso.values())
        if torso_total < .01:
            upper = smooth((v.co.z - .94) / .22)
            torso = {m.vertex_groups['Hips'].index: 1 - upper,
                     m.vertex_groups[lower_spine].index: upper}
            torso_total = 1.0
        # Transfer the removed weight explicitly. Renormalizing a tapered removal
        # alone can restore a large residual Hand fraction on mostly-arm vertices.
        for i, w in torso.items():
            weights[i] = weights.get(i, 0) + removed * w / torso_total
        weights = dict(sorted(weights.items(), key=lambda p: p[1], reverse=True)[:4])
        total = sum(weights.values())
        for g in m.vertex_groups:
            g.remove([v.index])
        for i, w in weights.items():
            if w / total > .000001:
                m.vertex_groups[i].add([v.index], w / total, 'REPLACE')
        changes.append({'vertex': v.index, 'xyz': list(v.co), 'factor': factor,
                        'before': {m.vertex_groups[i].name: w for i, w in old.items()},
                        'after': {m.vertex_groups[g.group].name: g.weight for g in v.groups}})
    m['waist_arm_weights_v1'] = True

report = {'changed_vertices': len(changes), 'changes': changes,
          'protected_near_arm_and_hand_vertices': len(protected),
          'height_range_m': [.72, 1.22],
          'geometry_uv_bones_actions_changed': False,
          'reason': 'Waist/pouch source auto-rig arm contamination and residual normalization after prior tapered lower-robe cleanup.'}
path = ROOT / 'Art/Player/waist-arm-weight-report.json'
if changes:
    path.write_text(json.dumps(report, indent=2), encoding='utf-8')
print('WAIST_WEIGHT_FIX', len(changes), 'vertices; no save/export performed')
