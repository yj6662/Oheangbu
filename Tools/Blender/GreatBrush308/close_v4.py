"""#308 GreatBrush v4: 붓 스틸(Workbench, 1280x720).  blender -b --factory-startup -P close_v4.py -- <v4 out_dir> [--only 이름,이름] [--res N] [--sub 폴더]
스틸: rest · head_side · head_tip · head_back · splay · press · bend · vs_rest · vs_bend(v3 / v4) · compare(1차 통짜 붓머리 옆에)."""
import bpy, math, os, sys
from mathutils import Vector, Matrix, Quaternion
ARGS = sys.argv[sys.argv.index('--') + 1:]; OUT = ARGS[0]; RES = int(ARGS[ARGS.index('--res') + 1]) if '--res' in ARGS else 1280
ONLY = ARGS[ARGS.index('--only') + 1].split(',') if '--only' in ARGS else None
ST = os.path.join(OUT, 'stills', ARGS[ARGS.index('--sub') + 1]) if '--sub' in ARGS else os.path.join(OUT, 'stills')
V1 = os.path.join(os.path.dirname(OUT.rstrip('/\\')), 'SM_GreatBrush308.blend'); V3 = os.path.join(os.path.dirname(V1), 'v3', 'SM_GreatBrush308.blend')
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, 'SM_GreatBrush308.blend'))
rig = bpy.data.objects['GreatBrush308_Rig']; brush = bpy.data.objects['SM_GreatBrush308']; keys = brush.data.shape_keys.key_blocks
s = bpy.context.scene; s.render.engine = 'BLENDER_WORKBENCH'; s.render.resolution_x = RES; s.render.resolution_y = RES * 9 // 16; s.render.image_settings.file_format = 'PNG'
if s.world is None: s.world = bpy.data.worlds.new('Grey')
s.world.color = (.30, .30, .30)
sh = s.display.shading; sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'; sh.show_shadows = True; sh.shadow_intensity = .35; sh.show_cavity = True; sh.cavity_type = 'BOTH'; sh.show_specular_highlight = True
sh.curvature_ridge_factor = 1.2; sh.curvature_valley_factor = 1.4
s.display.light_direction = (-.45, .35, .82); s.display.render_aa = '8'; s.view_settings.view_transform = 'Standard'
fm = bpy.data.materials.new('Floor'); fm.diffuse_color = (.62, .62, .62, 1)
bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0)); bpy.context.active_object.data.materials.append(fm)
cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); s.collection.objects.link(cam); s.camera = cam; cam.data.sensor_fit = 'VERTICAL'; cam.data.clip_start = .02
RINGS = 'ABCDEF'


def look(pos, target, fov): cam.location = pos; cam.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler(); cam.data.angle_y = math.radians(fov)


def shot(name):
    if ONLY and name not in ONLY: return False
    s.render.filepath = os.path.join(ST, 'brush_%s.png' % name); bpy.ops.render.render(write_still=True); print('CLOSE', name); return True


def rot(r, bone, axis_local, deg):
    """axis_local = 리그 로컬 축. 뼈 쉬는 방향으로 바꿔 돌린다."""
    pb = r.pose.bones[bone]; pb.rotation_mode = 'QUATERNION'
    pb.rotation_quaternion = Quaternion(pb.bone.matrix_local.to_3x3().inverted() @ Vector(axis_local).normalized(), math.radians(deg))


def place(M, press=0, splay=0, bend=0, axis=(1, 0, 0), ring=None, r=None, k=None, power=1.5):
    """M = 리그 월드 행렬. bend = 가운데 사슬 전체 꺾임(도, 리그 로컬 axis 둘레). ring = {'A': 도 …} 고리 사슬을 바깥(+)/안(-)으로."""
    r = r or rig; k = keys if k is None else k
    r.matrix_world = M
    if k: k['BristlePress'].value = press; k['BristleSplay'].value = splay
    for i in range(8): rot(r, 'Bristle_%02d' % (i + 1), axis, bend * (((i + 1) / 8) ** power - (i / 8) ** power))
    for q, n in enumerate(RINGS):
        if 'BristleRing_%s_01' % n not in r.pose.bones: continue
        a = 2 * math.pi * q / 6; tang = (-math.sin(a), math.cos(a), 0); deg = (ring or {}).get(n, 0)
        for j in range(4): rot(r, 'BristleRing_%s_%02d' % (n, j + 1), tang, deg * (.1, .25, .35, .30)[j])
    bpy.context.view_layer.update(); return bpy.data.objects['TipSocket'].matrix_world.translation.copy()


def frame(G, zdir, xdir=(0, 1, 0)):
    """붓끝 방향 zdir, 리그 로컬 X 를 xdir 쪽으로 둔 월드 행렬(원점 G)."""
    z = Vector(zdir).normalized(); x = Vector(xdir); x = (x - z * x.dot(z)).normalized(); y = z.cross(x)
    m = Matrix((x, y, z)).transposed().to_4x4(); m.translation = G; return m


HEADC = Vector((.0, 0, .45)) + Vector((1, 0, .12)).normalized() * .76      # 쉬는 자세에서 붓머리 가운데(그립에서 0.57–0.95 m)
REST = frame(Vector((0, 0, .45)), (1, 0, .12))
# 1) 쉬는 모양 전체
place(REST); look(Vector((.20, -2.05, .95)), Vector((.17, 0, .45)), 34); shot('rest')
# 2) 붓머리 가까이 세 방향: 옆 · 붓끝 쪽 3/4 · 뒤(자루 쪽) 위에서
look(HEADC + Vector((.02, -.62, .10)), HEADC + Vector((.0, 0, .0)), 30); shot('head_side')
look(HEADC + Vector((.52, -.36, .22)), HEADC + Vector((.03, 0, .0)), 30); shot('head_tip')
look(HEADC + Vector((-.34, .42, .40)), HEADC + Vector((.02, 0, .0)), 30); shot('head_back')
# 3) 퍼짐 1.0
place(REST, splay=1); look(HEADC + Vector((.30, -.56, .22)), HEADC + Vector((.03, 0, .0)), 30); shot('splay')
# 4) 눌림 1.0 + 바닥에 끌리듯 꺾임: 60도로 세워 붓끝을 바닥에 대고 +X 로 눕힌다(리그 로컬 X = 월드 Y, 로컬 Y 가 바닥 법선 쪽)
d = Vector((.5, 0, -.866)); bend = -58
M = frame(Vector((0, 0, 1)), d); tip = place(M, press=1, bend=bend, power=1.2)
dg = bpy.context.evaluated_depsgraph_get(); ev = brush.evaluated_get(dg); em = ev.to_mesh(); zs = sorted((ev.matrix_world @ v.co).z for v in em.vertices); ev.to_mesh_clear()
G = Vector((0, 0, 1)) - Vector((tip.x - .55, 0, zs[int(len(zs) * .01)] - .004)); tip = place(frame(G, d), press=1, bend=bend, power=1.2)
look(Vector((.74, -.98, .13)), Vector((.40, 0, .15)), 30); shot('press')
look(Vector((.48, -.05, 1.25)), Vector((.46, 0, .05)), 30); shot('press_top')
# 5) 옆으로 꺾임 + 고리 사슬마다 다른 각도(가닥이 저마다 뻣뻣하게 따로 논다)
place(REST, bend=38, axis=(0, 1, 0), ring={'A': 26, 'B': -10, 'C': 34, 'D': 6, 'E': -14, 'F': 30})
look(HEADC + Vector((.10, -.62, .26)), HEADC + Vector((.02, 0, -.03)), 32); shot('bend')
# 6) 1차 통짜 붓머리와 나란히
def load(path):
    with bpy.data.libraries.load(path, link=False) as (src, dst): dst.objects = [n for n in src.objects]
    new = [o for o in dst.objects if o is not None]
    for o in new: s.collection.objects.link(o)
    r = next(o for o in new if o.type == 'ARMATURE'); b = next(o for o in new if o.type == 'MESH'); k = b.data.shape_keys.key_blocks
    for kk in k[1:]: kk.value = 0
    return new, r, k


def hide(objs):
    for o in objs: o.hide_render = True


UP = Vector((0, 0, .27)); BEND = dict(bend=38, axis=(0, 1, 0), ring={'A': 26, 'B': -10, 'C': 34, 'D': 6, 'E': -14, 'F': 30})
if not ONLY or 'compare' in ONLY:
    new, rig1, k1 = load(V1)
    place(REST); rig1.matrix_world = frame(Vector((0, 0, .72)), (1, 0, .12)); bpy.context.view_layer.update()
    look(HEADC + Vector((.10, -1.02, .30)), HEADC + Vector((.0, 0, .13)), 30); shot('compare'); hide(new)
if not ONLY or 'vs_rest' in ONLY or 'vs_bend' in ONLY:
    new, rig3, k3 = load(V3)
    place(REST); place(frame(Vector((0, 0, .45)) + UP, (1, 0, .12)), r=rig3, k=k3)
    look(HEADC + Vector((.06, -.86, .26)), HEADC + Vector((.0, 0, .135)), 30); shot('vs_rest')
    place(REST, **BEND); place(frame(Vector((0, 0, .45)) + UP, (1, 0, .12)), r=rig3, k=k3, **BEND)
    look(HEADC + Vector((.10, -.88, .34)), HEADC + Vector((.02, 0, .10)), 30); shot('vs_bend'); hide(new)
print('CLOSE DONE')
