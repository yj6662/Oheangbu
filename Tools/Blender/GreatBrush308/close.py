"""#308 GreatBrush: 붓만 가까이(쉬는 모양 · 눌림 + 꺾임).  blender -b --factory-startup -P close.py -- <out_dir> [fbx 무시] [--res N]
Workbench. 눌림 컷: 붓을 바닥에 60도로 세워 대고 BristlePress 1 + 붓털 사슬을 바닥을 따라 눕힌다."""
import bpy, math, os, sys
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


def look(pos, target, fov): cam.location = pos; cam.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler(); cam.data.angle_y = math.radians(fov)
def shot(name): s.render.filepath = os.path.join(OUT, 'stills', name); bpy.ops.render.render(write_still=True); print('CLOSE', name)


def place(G, d, press=0, splay=0, bend=0, axis=(1, 0, 0)):
    d = Vector(d).normalized(); rig.matrix_world = Matrix.Translation(G) @ d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    keys['BristlePress'].value = press; keys['BristleSplay'].value = splay; bpy.context.view_layer.update(); rw = rig.matrix_world.to_3x3(); ax = Vector(axis).normalized()
    for i in range(8):
        pb = rig.pose.bones['Bristle_%02d' % (i + 1)]; pb.rotation_mode = 'QUATERNION'; w = ((i + 1) / 8) ** 1.5 - (i / 8) ** 1.5
        pb.rotation_quaternion = Quaternion((rw @ pb.bone.matrix_local.to_3x3()).inverted() @ ax, math.radians(bend * w))
    bpy.context.view_layer.update(); return bpy.data.objects['TipSocket'].matrix_world.translation.copy()


# 1) 쉬는 모양: 받침 없이 살짝 띄워 비스듬히, 전체가 보이게
place(Vector((0, 0, .45)), (1, 0, .12)); look(Vector((.20, -2.05, .95)), Vector((.17, 0, .45)), 34); shot('brush_rest.png')
look(Vector((1.05, -.62, .78)), Vector((.80, 0, .55)), 30); shot('brush_head.png')
look(Vector((-.95, -.50, .62)), Vector((-.62, 0, .38)), 26); shot('brush_tail.png')
# 2) 눌림 + 꺾임: 60도로 세워 붓끝을 바닥에 대고 누른다(붓털이 +X 쪽으로 눕는다)
d = Vector((.5, 0, -.866)); bend = 52; ax = Vector((0, -1, 0))
tip = place(Vector((0, 0, 1)), d, press=1, splay=.35, bend=bend, axis=ax)
G = Vector((0, 0, 1)) - Vector((tip.x - .55, 0, tip.z - .012)); tip = place(G, d, press=1, splay=.35, bend=bend, axis=ax)
look(Vector((.62, -1.05, .30)), Vector((.43, 0, .17)), 30); shot('brush_press.png')
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'brush_press_scene.blend'), copy=True)
print('CLOSE tip', tuple(round(x, 3) for x in tip)); print('CLOSE DONE')
