"""#308 GreatBrush 2차: 붓만 가까이.  blender -b --factory-startup -P close_v2.py -- <v2 out_dir> [무시] [--res N]
쉬는 모양 · BristleSplay 단독 · BristlePress + 바닥을 따라 눕는 꺾임 · 1차/2차 비례 비교. Workbench."""
import bpy, math, os, sys
import numpy as np
from mathutils import Vector, Matrix, Quaternion
ARGS = sys.argv[sys.argv.index('--') + 1:]; OUT = ARGS[0]; RES = int(ARGS[ARGS.index('--res') + 1]) if '--res' in ARGS else 1280
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, 'SM_GreatBrush308.blend'))
rig = bpy.data.objects['GreatBrush308_Rig']; brush = bpy.data.objects['SM_GreatBrush308']; keys = brush.data.shape_keys.key_blocks
s = bpy.context.scene; s.render.engine = 'BLENDER_WORKBENCH'; s.render.resolution_x = RES; s.render.resolution_y = RES * 9 // 16; s.render.image_settings.file_format = 'PNG'
if s.world is None: s.world = bpy.data.worlds.new('Grey')
s.world.color = (.30, .30, .30)
sh = s.display.shading; sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'; sh.show_shadows = True; sh.shadow_intensity = .35; sh.show_cavity = True; sh.cavity_type = 'BOTH'; sh.show_specular_highlight = True
s.display.light_direction = (-.45, .35, .82); s.display.render_aa = '8'; s.view_settings.view_transform = 'Standard'
fm = bpy.data.materials.new('Floor'); fm.diffuse_color = (.62, .62, .62, 1)
bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0)); bpy.context.active_object.data.materials.append(fm)
cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); s.collection.objects.link(cam); s.camera = cam; cam.data.sensor_fit = 'VERTICAL'; cam.data.clip_start = .02
TIP = .77; TAIL = .48


def look(pos, target, fov): cam.location = pos; cam.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler(); cam.data.angle_y = math.radians(fov)
def shot(name): s.render.filepath = os.path.join(OUT, 'stills', name); bpy.ops.render.render(write_still=True); print('CLOSE', name)


def place(G, d, press=0, splay=0, bend=0, axis=(1, 0, 0), power=1.5):
    d = Vector(d).normalized(); rig.matrix_world = Matrix.Translation(G) @ d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    keys['BristlePress'].value = press; keys['BristleSplay'].value = splay; bpy.context.view_layer.update(); rw = rig.matrix_world.to_3x3(); ax = Vector(axis).normalized()
    for i in range(8):
        pb = rig.pose.bones['Bristle_%02d' % (i + 1)]; pb.rotation_mode = 'QUATERNION'; w = ((i + 1) / 8) ** power - (i / 8) ** power
        pb.rotation_quaternion = Quaternion((rw @ pb.bone.matrix_local.to_3x3()).inverted() @ ax, math.radians(bend * w))
    bpy.context.view_layer.update()


def low_point():
    dg = bpy.context.evaluated_depsgraph_get(); ev = brush.evaluated_get(dg); me = ev.to_mesh(); co = np.empty(len(me.vertices) * 3, dtype=np.float32); me.vertices.foreach_get('co', co); ev.to_mesh_clear()
    M = np.array(brush.matrix_world, dtype=np.float32); P = co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]; i = int(P[:, 2].argmin()); return Vector(P[i].tolist())


place(Vector((0, 0, .45)), (1, 0, .12)); look(Vector((.15, -1.75, .90)), Vector((.14, 0, .46)), 34); shot('brush_rest.png')
place(Vector((0, 0, .45)), (1, 0, .12), splay=1); look(Vector((.72, -.80, .78)), Vector((.58, 0, .52)), 32); shot('brush_splay.png')
# 눌림 + 꺾임: 55도로 세워 누른다. 붓털 사슬이 고르게 휘어 끝 쪽 절반이 바닥을 따라 눕는다(마지막 뼈가 바닥과 나란하도록 휨 = 세운 각)
d = Vector((.574, 0, -.819)); ang = math.degrees(math.asin(.819))
place(Vector((0, 0, 1)), d, press=1, splay=.45, bend=ang + 8, axis=(0, -1, 0), power=.9)
lp = low_point(); G = Vector((-lp.x + .55, 0, 1 - lp.z + .004)); place(G, d, press=1, splay=.45, bend=ang + 8, axis=(0, -1, 0), power=.9)
look(Vector((.50, -1.15, .34)), Vector((.36, 0, .19)), 30); shot('brush_press.png'); print('CLOSE press low point z %.3f' % low_point().z)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'brush_press_scene.blend'), copy=True)
# 비례 비교: 1차 붓을 불러 위에 놓는다
place(Vector((0, 0, .30)), (1, 0, 0), press=0, splay=0); v1 = os.path.join(os.path.dirname(OUT.rstrip('/\\')), 'SM_GreatBrush308.blend')
with bpy.data.libraries.load(v1) as (src, dst): dst.objects = [n for n in src.objects if n in ('GreatBrush308_Rig', 'SM_GreatBrush308')]
for o in dst.objects: s.collection.objects.link(o)
for o in dst.objects:
    if o.type == 'MESH':
        for kb in o.data.shape_keys.key_blocks[1:]: kb.value = 0
r1 = next(o for o in dst.objects if o.type == 'ARMATURE'); r1.matrix_world = Matrix.Translation(Vector((0 - .12, 0, .62))) @ Vector((1, 0, 0)).to_track_quat('Z', 'Y').to_matrix().to_4x4()
bpy.context.view_layer.update(); look(Vector((.15, -2.3, .55)), Vector((.15, 0, .45)), 34); shot('brush_compare.png')
print('CLOSE DONE')
