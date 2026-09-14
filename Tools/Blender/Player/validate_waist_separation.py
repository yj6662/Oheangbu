"""Read saved Animated source, preview local fix, render frozen independent QA poses.

This script never saves a blend or exports Unity assets. FACES_ONLY wire edges
are excluded from rendered-surface strain checks. Clear active animation before
applying QA rotations, because render frame evaluation otherwise overwrites them.
"""
import bpy, json, hashlib
from pathlib import Path
from mathutils import Vector, Quaternion

ROOT = Path('C:/Users/yj666/Oheangbu')
bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'Art/Player/Dosa_Animated.blend'))
if not globals().get('PREVIEW_ORIGINAL', False):
    exec(compile((ROOT / 'Tools/Blender/Player/separate_waist_sleeve_bridges.py').read_text(), 'separate_waist_sleeve_bridges.py', 'exec'))
suffix = 'before' if globals().get('PREVIEW_ORIGINAL', False) else 'final'
s = bpy.context.scene
r = bpy.data.objects['Dosa_Rig']; m = bpy.data.objects['Dosa_Body']
qa = ROOT / 'Art/Player/MotionQA'
report = json.loads((ROOT / 'Art/Player/waist-sleeve-separation-report.json').read_text())
changed = {x['vertex'] for x in report['changes']} | set(range(*report.get('lining_vertex_range',[0,0])))
rendered_edges = {tuple(sorted(e)) for p in m.data.polygons for e in p.edge_keys}
patch_edges = [e for e in rendered_edges if any(i in changed for i in e)]
poses = {}
for name, action in [('DrawHold', 'Dosa_DrawHold'), ('Idle', 'Dosa_Idle')]:
    r.animation_data_create(); r.animation_data.action = bpy.data.actions[action]
    s.frame_set(1); bpy.context.view_layer.update()
    poses[name] = {p.name: p.matrix_basis.copy() for p in r.pose.bones}
r.animation_data_clear()
s.render.resolution_x = 720; s.render.resolution_y = 1000; s.render.resolution_percentage = 100
s.cycles.samples = 12
cam = s.camera
def camera(pos):
    cam.location = pos
    cam.rotation_euler = (Vector((0,0,.92)) - cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.lens = 60
results = []
for name in ['DrawHold', 'DrawHigh', 'DrawAcross', 'Idle']:
    for p in r.pose.bones: p.matrix_basis = poses['Idle' if name == 'Idle' else 'DrawHold'][p.name]
    if name == 'DrawHigh':
        for n, v in [('RightArm', .75), ('LeftArm', -.65)]:
            p = r.pose.bones[n]; p.rotation_mode = 'QUATERNION'; p.rotation_quaternion = p.rotation_quaternion @ Quaternion((1,0,0), v)
    if name == 'DrawAcross':
        for n, v in [('RightForeArm', .85), ('LeftForeArm', -.85)]:
            p = r.pose.bones[n]; p.rotation_mode = 'QUATERNION'; p.rotation_quaternion = p.rotation_quaternion @ Quaternion((0,0,1), v)
    bpy.context.view_layer.update()
    em = m.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
    signature = hashlib.sha256(b''.join(__import__('struct').pack('<3f', *v.co) for v in em.vertices)).hexdigest()
    edges = []
    for i,j in patch_edges:
        a = (m.data.vertices[i].co - m.data.vertices[j].co).length
        b = (em.vertices[i].co - em.vertices[j].co).length
        if a > .001 and b/a > 2:
            edges.append({'verts': [i,j], 'ratio': b/a, 'rest_m': a, 'posed_m': b, 'xyz':[list(m.data.vertices[x].co) for x in [i,j]]})
    edges.sort(key=lambda x: x['posed_m']-x['rest_m'], reverse=True)
    original_count=report.get('lining_vertex_range',[len(m.data.vertices)])[0]
    results.append({'pose':name,'evaluated_vertex_sha256':signature,'evaluated_rendered_patch_edges':len(patch_edges),'over2_edges':len(edges),
                    'original_patch_edges_over2':sum(max(e['verts'])<original_count for e in edges),
                    'lining_edges_over2':sum(max(e['verts'])>=original_count for e in edges),
                    'maximum_over2_edge_length_m':max([e['posed_m'] for e in edges],default=0.),'largest':edges[:40]})
    camera((0,-4,1.7)); s.render.filepath=str(qa / ('WaistSeparate-'+name+'-'+suffix+'.png')); bpy.ops.render.render(write_still=True)
    if name == 'DrawHold':
        camera((-2,4,1.7)); s.render.filepath=str(qa/('WaistSeparate-DrawBack-'+suffix+'.png')); bpy.ops.render.render(write_still=True)
assert len({x['evaluated_vertex_sha256'] for x in results}) == 4, 'Independent pose evaluation failed'
(ROOT/('Art/Player/waist-separation-preview-validation'+('-before' if suffix=='before' else '')+'.json')).write_text(json.dumps(results,indent=2))
print('QA_DISTINCT_POSES',json.dumps([{k:x[k] for k in ['pose','evaluated_vertex_sha256','over2_edges']} for x in results]))
