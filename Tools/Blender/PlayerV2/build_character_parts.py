"""Derived V2 surfaces and diagnostic rest rig. Never creates animation actions.

Region boundaries come from the normalized Meshy source inspection. This file
produces a work-in-progress, not a RIG_PASS or a canonical player replacement.
"""
import bpy, bmesh, json, math
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path('C:/Users/yj666/Oheangbu')
OUT = ROOT / 'Art/PlayerV2'
bpy.ops.wm.open_mainfile(filepath=str(OUT / 'DosaV2_Intake.blend'))
s = bpy.context.scene
source = bpy.data.objects['DosaV2_SourceSurface']
components = json.loads((OUT / 'Inspect/character-components.json').read_text())
hat_vertices = set(components[1]['indices'])
source.hide_render = True
source.hide_set(True)
parts = {}
ornament_regions = [
    ('WaistPouch_R', (-.166,-.154,.932), (.069,.081,.124)),
    ('WaistVial_L', (.112,-.204,.842), (.034,.046,.070)),
    ('WaistScroll_L', (.199,-.128,.773), (.049,.089,.148)),
    ('WaistTube_C', (.033,-.184,.750), (.046,.043,.140)),
    ('WaistUpperScroll_L', (.153,-.143,1.025), (.046,.058,.105)),
    ('WaistTubeSide_L', (.227,.005,.890), (.061,.075,.134)),
    ('WaistPouchBack_L', (.162,.139,.908), (.079,.084,.090)),
    ('WaistTubeBack_R', (-.170,.123,.886), (.065,.089,.131)),
    ('WaistVialBack_L', (.219,.133,.782), (.040,.055,.080)),
]

def classify(poly):
    p = sum((source.data.vertices[i].co for i in poly.vertices), Vector()) / len(poly.vertices)
    x, y, z = p
    ax = abs(x)
    side = 'L' if x > 0 else 'R'
    if all(i in hat_vertices for i in poly.vertices):
        if z < 1.588 and ax > .125: return 'HatTassel_' + side
        return 'Hat'
    if ax > .663 and z > 1.18: return 'SourceHands_Removed'
    if z > 1.432:
        if z > 1.632 or (y > -.008 and z > 1.493) or (ax > .066 and z > 1.49): return 'Hair'
        return 'Head'
    if ax > .177 and z > 1.10:
        if ax < .406: return 'SleeveOuter_' + side
        return 'SleeveInner_' + side
    for name, center, radii in ornament_regions:
        if sum(((p[i]-center[i])/radii[i])**2 for i in range(3)) < 1:
            return name
    if .305 < z < .966 and (ax > .153 or abs(y) > .114):
        direction = 'Front' if y < -.037 else ('Back' if y > .037 else 'Side')
        return 'Robe_' + direction + '_' + side
    return 'BodyCore'

groups = {}
for poly in source.data.polygons:
    groups.setdefault(classify(poly), set()).add(poly.index)

def surface(name, face_indices):
    obj = source.copy()
    obj.data = source.data.copy()
    obj.name = 'DosaV2_' + name
    s.collection.objects.link(obj)
    obj.hide_render = False
    obj.hide_set(False)
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in face_indices], context='FACES_ONLY')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bm.to_mesh(obj.data); bm.free()
    for p in obj.data.polygons: p.use_smooth = True
    obj.data.update()
    parts[name] = obj
    return obj

for name, faces in groups.items():
    if name != 'SourceHands_Removed': surface(name, faces)

arm = bpy.data.armatures.new('DosaV2_DeformSkeleton')
rig = bpy.data.objects.new('DosaV2_Rig', arm)
s.collection.objects.link(rig)
bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True); bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
bone_points = {}
def bone(name, head, tail, parent=None, roll=(0,-1,0), deform=True):
    b = arm.edit_bones.new(name)
    b.head, b.tail = head, tail
    b.align_roll(Vector(roll))
    b.use_deform = deform
    if parent: b.parent = arm.edit_bones[parent]
    bone_points[name] = (Vector(head), Vector(tail))
    return b

bone('Root', (0,0,0), (0,0,.12), deform=False)
bone('Hips', (0,0,.88), (0,0,.99), 'Root')
bone('Spine', (0,0,.99), (0,0,1.13), 'Hips')
bone('Spine01', (0,0,1.13), (0,0,1.26), 'Spine')
bone('Spine02', (0,0,1.26), (0,0,1.413), 'Spine01')
bone('Neck', (0,0,1.413), (0,0,1.48), 'Spine02')
bone('Head', (0,0,1.48), (0,0,1.666), 'Neck')
for side, sign in [('Left', 1), ('Right', -1)]:
    bone(side+'Shoulder', (sign*.052,0,1.369), (sign*.181,0,1.355), 'Spine02', (0,0,1))
    bone(side+'Arm', (sign*.181,0,1.355), (sign*.442,0,1.336), side+'Shoulder', (0,0,1))
    bone(side+'ForeArm', (sign*.442,0,1.336), (sign*.663,0,1.327), side+'Arm', (0,0,1))
    bone(side+'Hand', (sign*.663,0,1.327), (sign*.731,0,1.327), side+'ForeArm', (0,0,1))
    bone(side+'UpLeg', (sign*.096,.013,.88), (sign*.103,-.006,.485), 'Hips')
    bone(side+'Leg', (sign*.103,-.006,.485), (sign*.111,.013,.105), side+'UpLeg')
    bone(side+'Foot', (sign*.111,.013,.105), (sign*.111,-.096,.040), side+'Leg', (0,0,1))
    bone(side+'ToeBase', (sign*.111,-.096,.040), (sign*.111,-.153,.034), side+'Foot', (0,0,1))
for name, obj in parts.items():
    if name.startswith('Waist'):
        points=[v.co for v in obj.data.vertices]
        center=sum(points,Vector())/len(points)
        top=Vector((center.x,center.y,max(p.z for p in points)))
        bone('J_'+name,top,top+Vector((0,0,-.055)),'Hips',(0,-1,0))
    elif name.startswith('HatTassel_'):
        points=[v.co for v in obj.data.vertices]
        center=sum(points,Vector())/len(points);lo=min(p.z for p in points);hi=max(p.z for p in points)
        for i in range(3):
            top=(center.x,center.y,hi-(hi-lo)*i/3)
            end=(center.x,center.y,hi-(hi-lo)*(i+1)/3)
            bone('J_'+name+'_'+str(i),top,end,'Head' if i==0 else 'J_'+name+'_'+str(i-1))
bpy.ops.object.mode_set(mode='OBJECT')

def weighted_nearest(p, candidates, radius=.10):
    distances = []
    for name in candidates:
        a, b = bone_points[name]
        v = b - a
        t = max(0, min(1, (p-a).dot(v)/v.length_squared))
        d = (p - (a + v*t)).length
        distances.append((name, d))
    distances.sort(key=lambda item: item[1])
    closest = distances[0][1]
    weights = [(name, math.exp(-((dist-closest)/radius)**2*6)) for name, dist in distances[:4]]
    weights = [(name, value) for name, value in weights if value > .004]
    total = sum(w for name,w in weights)
    return {name: w/total for name,w in weights}

def weights_for(name, p):
    side = 'Left' if p.x > 0 else 'Right'
    if name in ('Head', 'Hair', 'Hat'): return {'Head': 1}
    if name.startswith('Waist'): return {'J_'+name:1}
    if name.startswith('HatTassel_'):
        return weighted_nearest(p,['J_'+name+'_'+str(i) for i in range(3)],.014)
    if name.startswith('Robe_'): return {'Hips': 1}
    if name.startswith('Sleeve'):
        return weighted_nearest(p, [side+'Shoulder', side+'Arm', side+'ForeArm', side+'Hand'], .06)
    if p.z < .88:
        return weighted_nearest(p, ['Hips', side+'UpLeg', side+'Leg', side+'Foot', side+'ToeBase'], .045)
    return weighted_nearest(p, ['Hips','Spine','Spine01','Spine02','Neck'], .065)

for name, obj in parts.items():
    obj['surface_role'] = 'cloth' if name.startswith(('Robe_', 'SleeveOuter_')) else 'skin'
    for group in list(obj.vertex_groups): obj.vertex_groups.remove(group)
    for vertex in obj.data.vertices:
        for bn, value in weights_for(name, vertex.co).items():
            vg = obj.vertex_groups.get(bn) or obj.vertex_groups.new(name=bn)
            vg.add([vertex.index], value, 'REPLACE')
    mod = obj.modifiers.new('DosaV2_Skin', 'ARMATURE')
    mod.object = rig
    obj.parent = rig
    # Restore explicit identity; all authored vertex positions already use metres.
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.matrix_basis = Matrix.Identity(4)

report = {'status':'WIP_NOT_RIG_PASS', 'production_actions':0,
    'bone_count':len(arm.bones), 'parts':[],
    'known_remaining':['Replace fused source hands with articulated hands.',
        'Separate waist ornaments from robe regions.', 'Fill body/lining below cloth.',
        'Integrate separately generated backpack.', 'Author cloth particles/pins and auxiliary chains.',
        'Static deformation and Unity roundtrip checks have not run.']}
for name,obj in parts.items():
    obj.data.calc_loop_triangles()
    report['parts'].append({'name':obj.name,'triangles':len(obj.data.loop_triangles),'vertices':len(obj.data.vertices)})
(OUT/'Inspect/character-parts-wip.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'DosaV2_Parts.blend'))
print(json.dumps(report))
