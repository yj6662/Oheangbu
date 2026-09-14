"""Author only the bristle tuft in an isolated derivative. No animations.

The Meshy source, handle, seven-bone rig and socket rest frames are immutable.
New continuous tuft topology and cylindrical UV replace the jagged source hair.
Native Blender procedural fiber material is baked to matching base/normal maps.
"""
import bpy
import math
import json
import hashlib
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion

ROOT = Path(__file__).resolve().parents[3]
ART = ROOT / 'Art/PlayerV2'
SOURCE = ART / 'DosaBrushV2.blend'
OUT = ART / 'Staging/BrushRefined'
OUT.mkdir(parents=True, exist_ok=True)
(OUT / 'Textures').mkdir(exist_ok=True)
(OUT / 'QA').mkdir(exist_ok=True)

def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def mat(m): return [float(x) for row in m for x in row]
def digest(value): return hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()
def preserved():
    h = bpy.data.objects['DosaBrushV2_Handle']; r = bpy.data.objects['DosaBrushV2_Rig']
    return {
        'handle': digest({'v': [list(v.co) for v in h.data.vertices], 'f': [list(p.vertices) for p in h.data.polygons],
                          'uv': [[list(d.uv) for d in layer.data] for layer in h.data.uv_layers],
                          'n': [list(n.vector) for n in h.data.corner_normals],
                          'materialNames': [m.name for m in h.data.materials], 'matrix': mat(h.matrix_local)}),
        'rig': digest({b.name: {'m': mat(b.matrix_local), 'length': b.length, 'parent': b.parent.name if b.parent else None}
                       for b in r.data.bones}),
        'sockets': digest({n: {'m': mat(bpy.data.objects[n].matrix_local), 'parentType': bpy.data.objects[n].parent_type,
                              'bone': bpy.data.objects[n].parent_bone} for n in ['GripSocket', 'TipSocket']}),
    }

bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
source_hash = sha(SOURCE)
source_raw = ART / 'MeshySources/brush-01/source_glb.glb'
raw_hash = sha(source_raw)
before = preserved()
rig = bpy.data.objects['DosaBrushV2_Rig']
handle = bpy.data.objects['DosaBrushV2_Handle']
hair = bpy.data.objects['DosaBrushV2_Bristles']
old_mesh = hair.data
root = rig.data.bones['Bristle_01'].head_local.copy()
tip = rig.data.bones['Bristle_06'].tail_local.copy()
line = tip - root
length = line.length
direction = line.normalized()
axis_x = direction.cross(Vector((0, 1, 0))).normalized()
axis_y = direction.cross(axis_x).normalized()

# Monotone piecewise cubic radius profile: gather into a continuous fine tip.
profile = [(0, .0260), (.08, .0302), (.20, .0350), (.34, .0370), (.48, .0347),
           (.62, .0275), (.76, .0175), (.87, .0092), (.95, .0031), (1, 0)]
def radius(t):
    for i in range(len(profile)-1):
        a, b = profile[i], profile[i+1]
        if t <= b[0]:
            # Cubic Hermite with averaged finite slopes and zero derivatives at extrema.
            d = (b[1]-a[1])/(b[0]-a[0])
            m0 = d if i == 0 else (b[1]-profile[i-1][1])/(b[0]-profile[i-1][0])
            m1 = d if i+2 == len(profile) else (profile[i+2][1]-a[1])/(profile[i+2][0]-a[0])
            if i > 0 and (a[1]-profile[i-1][1])*d <= 0: m0 = 0
            if i+2 < len(profile) and (profile[i+2][1]-b[1])*d <= 0: m1 = 0
            x = (t-a[0])/(b[0]-a[0]); h = b[0]-a[0]
            return max(0, (2*x**3-3*x*x+1)*a[1]+(x**3-2*x*x+x)*h*m0+(-2*x**3+3*x*x)*b[1]+(x**3-x*x)*h*m1)
    return 0

RINGS, SIDES = 48, 36
verts, faces, face_uv = [], [], []
for j in range(RINGS):
    t = j/RINGS
    center = root + line*t
    # Small coherent asymmetry and shallow grooves, never disconnected spikes.
    for i in range(SIDES):
        theta = 2*math.pi*i/SIDES
        corrugation = 1 + .020*math.sin(9*theta+.24*t) + .012*math.sin(13*theta-.18*t)
        oval = 1 + .025*math.cos(2*theta)*math.sin(math.pi*t)
        radial = (axis_x*math.cos(theta)+axis_y*math.sin(theta))*radius(t)*corrugation*oval
        verts.append(tuple(center+radial))
tip_index = len(verts); verts.append(tuple(tip))
base_index = len(verts); verts.append(tuple(root))
for j in range(RINGS-1):
    for i in range(SIDES):
        n = (i+1)%SIDES
        faces.append((j*SIDES+i,j*SIDES+n,(j+1)*SIDES+n,(j+1)*SIDES+i))
        face_uv.append(((i/SIDES,j/RINGS),((i+1)/SIDES,j/RINGS),((i+1)/SIDES,(j+1)/RINGS),(i/SIDES,(j+1)/RINGS)))
for i in range(SIDES):
    n=(i+1)%SIDES
    faces.append(((RINGS-1)*SIDES+i,(RINGS-1)*SIDES+n,tip_index))
    face_uv.append(((i/SIDES,(RINGS-1)/RINGS),((i+1)/SIDES,(RINGS-1)/RINGS),((i+.5)/SIDES,1)))
    faces.append((n,i,base_index))
    face_uv.append((((i+1)/SIDES,0),(i/SIDES,0),((i+.5)/SIDES,0)))
mesh = bpy.data.meshes.new('DosaBrushV2_ContinuousTuft')
mesh.from_pydata(verts, [], faces); mesh.update()
uv = mesh.uv_layers.new(name='TuftCylindricalUV')
for p, values in zip(mesh.polygons,face_uv):
    p.use_smooth = True
    for li, v in zip(p.loop_indices, values): uv.data[li].uv = v
hair.data = mesh
hair.vertex_groups.clear()
groups = [hair.vertex_groups.new(name=n) for n in ['BrushHandle']+[f'Bristle_{i+1:02d}' for i in range(6)]]
for v in mesh.vertices:
    t=max(0,min(1,(v.co-root).dot(direction)/length))
    blend=min(1,max(0,(t-.02)/.06))
    if blend < 1: groups[0].add([v.index],1-blend,'REPLACE')
    f=min(5,t*6); lo=int(f); hi=min(5,lo+1); alpha=f-lo
    weights={lo:1-alpha}; weights[hi]=weights.get(hi,0)+alpha
    for index,w in weights.items():
        if blend*w > 0: groups[index+1].add([v.index],blend*w,'REPLACE')
hair.shape_key_add(name='Basis'); shape=hair.shape_key_add(name='BristleSplay')
for v,s in zip(mesh.vertices,shape.data):
    t=max(0,min(1,(v.co-root).dot(direction)/length))
    center=root+line*t; fade=math.sin(math.pi*t)**2*min(1,max(0,(t-.04)/.08))
    s.co=v.co+(v.co-center)*(.35*fade)

# Authored fiber source shader. It is baked through Blender, not a modified Meshy atlas.
m=bpy.data.materials.new('M_DosaBrushV2_Bristles');m.use_nodes=True
nodes=m.node_tree.nodes;nodes.clear();links=m.node_tree.links
output=nodes.new('ShaderNodeOutputMaterial');bs=nodes.new('ShaderNodeBsdfPrincipled')
bs.inputs['Metallic'].default_value=0;bs.inputs['Roughness'].default_value=.85
links.new(bs.outputs[0],output.inputs['Surface'])
uvnode=nodes.new('ShaderNodeTexCoord');sep=nodes.new('ShaderNodeSeparateXYZ');links.new(uvnode.outputs['UV'],sep.inputs[0])
def math_node(operation, a, b=None):
    n=nodes.new('ShaderNodeMath');n.operation=operation
    if hasattr(a,'node'):links.new(a,n.inputs[0])
    else:n.inputs[0].default_value=a
    if b is not None:
        if hasattr(b,'node'):links.new(b,n.inputs[1])
        else:n.inputs[1].default_value=b
    return n.outputs[0]
# Periodic cylindrical noise avoids a visible atlas seam; varying fiber spacing
# prevents a regular fluted/plastic read under grazing light.
angle=math_node('MULTIPLY',sep.outputs['X'],2*math.pi)
coord=nodes.new('ShaderNodeCombineXYZ')
links.new(math_node('MULTIPLY',math_node('COSINE',angle),32),coord.inputs['X'])
links.new(math_node('MULTIPLY',math_node('SINE',angle),32),coord.inputs['Y'])
links.new(math_node('MULTIPLY',sep.outputs['Y'],2.7),coord.inputs['Z'])
noise=nodes.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=1;noise.inputs['Detail'].default_value=2;noise.inputs['Roughness'].default_value=.7;links.new(coord.outputs[0],noise.inputs['Vector'])
fine=math_node('MULTIPLY_ADD',math_node('SINE',math_node('MULTIPLY',angle,176)),.5)
fine.node.inputs[2].default_value=.5
fiber=nodes.new('ShaderNodeMixRGB');fiber.blend_type='MULTIPLY';fiber.inputs[0].default_value=.18;links.new(noise.outputs['Fac'],fiber.inputs[1]);links.new(fine,fiber.inputs[2])
bump=nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.35;bump.inputs['Distance'].default_value=.00035;links.new(fiber.outputs[0],bump.inputs['Height']);links.new(bump.outputs['Normal'],bs.inputs['Normal'])
ramp=nodes.new('ShaderNodeValToRGB');ramp.color_ramp.interpolation='EASE'
ramp.color_ramp.elements.remove(ramp.color_ramp.elements[1]);ramp.color_ramp.elements[0].position=0;ramp.color_ramp.elements[0].color=(.48,.445,.38,1)
for position,color in [(.38,(.43,.405,.35,1)),(.62,(.13,.145,.14,1)),(.79,(.035,.045,.043,1)),(1,(.02,.028,.027,1))]:
    e=ramp.color_ramp.elements.new(position);e.color=color
links.new(sep.outputs['Y'],ramp.inputs['Fac'])
detail=nodes.new('ShaderNodeValToRGB');detail.color_ramp.elements[0].position=.28;detail.color_ramp.elements[0].color=(.26,.26,.26,1);detail.color_ramp.elements[1].position=.68;detail.color_ramp.elements[1].color=(1,1,1,1);links.new(fiber.outputs[0],detail.inputs['Fac'])
color=nodes.new('ShaderNodeMixRGB');color.blend_type='MULTIPLY';color.inputs[0].default_value=1;links.new(ramp.outputs[0],color.inputs[1]);links.new(detail.outputs[0],color.inputs[2]);links.new(color.outputs[0],bs.inputs['Base Color'])
mesh.materials.append(m)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=8
bpy.ops.object.select_all(action='DESELECT');hair.select_set(True);bpy.context.view_layer.objects.active=hair
scene.render.bake.use_selected_to_active=False;scene.render.bake.margin=8
images={}
for label,kind in [('BaseColor','EMIT'),('Normal','NORMAL')]:
    image=bpy.data.images.new('T_DosaBrushV2_Bristles_'+label,width=2048,height=2048,alpha=False)
    image.colorspace_settings.name='sRGB' if label=='BaseColor' else 'Non-Color'
    tex=nodes.new('ShaderNodeTexImage');tex.image=image;nodes.active=tex
    if kind=='EMIT':
        emit=nodes.new('ShaderNodeEmission');links.new(color.outputs[0],emit.inputs['Color']);links.new(emit.outputs[0],output.inputs['Surface'])
    else:
        links.new(bs.outputs[0],output.inputs['Surface'])
    bpy.ops.object.bake(type=kind)
    image.filepath_raw=str(OUT/'Textures'/('T_DosaBrushV2_Bristles_'+label+'.png'));image.file_format='PNG';image.save();image.pack();images[label]=image
links.new(bs.outputs[0],output.inputs['Surface'])
# Keep a complete procedural authoring graph in the blend, but render the baked export contract.
base_tex=nodes.new('ShaderNodeTexImage');base_tex.image=images['BaseColor'];links.new(base_tex.outputs['Color'],bs.inputs['Base Color'])
normal_tex=nodes.new('ShaderNodeTexImage');normal_tex.image=images['Normal'];normal_map=nodes.new('ShaderNodeNormalMap');links.new(normal_tex.outputs['Color'],normal_map.inputs['Color']);links.new(normal_map.outputs['Normal'],bs.inputs['Normal'])
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
shape.value=0;bpy.context.view_layer.update()
mesh.calc_loop_triangles();handle.data.calc_loop_triangles()
assert len(mesh.loop_triangles)+len(handle.data.loop_triangles)<=5000
assert preserved()==before
assert len(bpy.data.actions)==0
assert (mesh.vertices[tip_index].co-tip).length < 1e-8
blend=ART/'DosaBrushV2_TuftRefined.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(blend),check_existing=False)
bpy.ops.object.select_all(action='DESELECT')
for ob in [rig,handle,hair,bpy.data.objects['GripSocket'],bpy.data.objects['TipSocket']]:ob.select_set(True)
bpy.context.view_layer.objects.active=rig
fbx=OUT/'SM_DosaBrushV2.fbx'
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},add_leaf_bones=False,use_armature_deform_only=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',use_mesh_modifiers=False,path_mode='COPY',embed_textures=True)
assert sha(SOURCE)==source_hash and sha(source_raw)==raw_hash
report={'source':str(SOURCE),'sourceSha256':source_hash,'meshyRawSha256':raw_hash,'sourcePreserved':True,
        'outputBlend':str(blend),'outputBlendSha256':sha(blend),'outputFbx':str(fbx),'outputFbxSha256':sha(fbx),
        'preserved':before,'onlyModifiedObject':hair.name,'bristleVertices':len(mesh.vertices),'bristleTriangles':len(mesh.loop_triangles),
        'handleTriangles':len(handle.data.loop_triangles),'totalTriangles':len(mesh.loop_triangles)+len(handle.data.loop_triangles),
        'root':list(root),'tip':list(tip),'bristleArcLengthM':length,'tipVertex':tip_index,
        'textureSource':'Native Blender authored cylindrical fiber shader; 2048² emission-color and tangent-normal bakes. No Meshy recoloring or paid generation.',
        'textures':[{ 'path':str(OUT/'Textures'/('T_DosaBrushV2_Bristles_'+k+'.png')), 'sha256':sha(OUT/'Textures'/('T_DosaBrushV2_Bristles_'+k+'.png'))} for k in images],
        'productionAnimations':0,'rigPass':False,'recipe':str(Path(__file__))}
(OUT/'tuft-build-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report))
