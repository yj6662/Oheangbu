"""#308 D308-127 2단계: 머리 넷을 큰 붓(v6)에 끼운 비교 그림(Workbench, 정면 · 옆).
    blender -b --factory-startup -P sheet_heads308.py -- <heads_dir> <brush_blend>
붓털 칸은 지우고(변신 중 숨기는 것과 같다) 페룰(쥐는 점 +.57 m)에 머리를 붙인다. 왼쪽 끝 = 원래 붓, 눈금 = 1 m."""
import bpy, os, sys, math
from mathutils import Vector

ARGV = sys.argv[sys.argv.index('--') + 1:]
HEADS = ARGV[0]; BRUSH = ARGV[1]
FERRULE = .57
PIG = {'Head_Spear': (0.051, 0.161, 0.098), 'Head_Sword': (0.381, 0.430, 0.418), 'Head_Axe': (0.305, 0.171, 0.056), 'Head_Flail': (0.025, 0.059, 0.102)}   # 목 · 금 · 토 · 수 (선형)
NAMES = {'Head_Spear': '목 장창', 'Head_Axe': '토 부월', 'Head_Sword': '금 쌍수도', 'Head_Flail': '수 편곤'}

bpy.ops.wm.open_mainfile(filepath=BRUSH)
scene = bpy.context.scene
brush = bpy.data.objects['SM_GreatBrush308']
for o in list(scene.objects):
    if o != brush: bpy.data.objects.remove(o, do_unlink=True)
for m in list(brush.modifiers): brush.modifiers.remove(m)
brush.parent = None; brush.matrix_world = brush.matrix_world.Identity(4)

with bpy.data.libraries.load(os.path.join(HEADS, 'SM_WeaponHeads308.blend')) as (src, dst):
    dst.objects = [n for n in src.objects if n.startswith('Head_')]
heads = {o.name: o for o in dst.objects}

order = ['Head_Spear', 'Head_Axe', 'Head_Sword', 'Head_Flail']
gap = .75
ink = bpy.data.materials.new('sheet_ink'); ink.diffuse_color = (.02, .019, .018, 1)
for k, name in enumerate([None] + order):
    x = k * gap
    b = brush if k == 0 else brush.copy()
    if k > 0:
        b.data = brush.data.copy(); scene.collection.objects.link(b)
        # bristle faces out (the form hides that material slot)
        import bmesh
        bm = bmesh.new(); bm.from_mesh(b.data)
        slot = next(i for i, m in enumerate(b.data.materials) if m and 'Bristle' in m.name)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index == slot], context='FACES'); bm.to_mesh(b.data); bm.free()
    b.location = (x, 0, 0)
    if name is None: continue
    root = heads[name]; scene.collection.objects.link(root)
    for c in root.children: scene.collection.objects.link(c)
    root.location = (x, 0, FERRULE)
    pig = bpy.data.materials.new('sheet_pig_' + name); pig.diffuse_color = (*PIG[name], 1)
    for o in [root] + list(root.children):
        if o.type == 'MESH':
            o.data = o.data.copy(); o.data.materials[0] = ink; o.data.materials[1] = pig

# scale bar 1 m and the judged reach marks are not drawn: a plain metre stick beside the line-up
bpy.ops.mesh.primitive_cube_add(size=1, location=(-.45, 0, -.1)); bar = bpy.context.object; bar.scale = (.01, .01, 1.0)
bar.data.materials.append(ink)

scene.render.engine = 'BLENDER_WORKBENCH'
shading = scene.display.shading; shading.light = 'STUDIO'; shading.color_type = 'MATERIAL'; shading.show_object_outline = True
scene.world = bpy.data.worlds.new('w'); scene.world.color = (.78, .75, .69)
shading.background_type = 'WORLD' if hasattr(shading, 'background_type') else shading.background_type
scene.render.resolution_x, scene.render.resolution_y = 1500, 1300; scene.render.film_transparent = False
cam_data = bpy.data.cameras.new('c'); cam_data.type = 'ORTHO'; cam_data.ortho_scale = 3.7
cam = bpy.data.objects.new('cam', cam_data); scene.collection.objects.link(cam); scene.camera = cam
for view, rot, loc in (('front', (math.radians(90), 0, 0), (1.35, -6, 1.05)), ('side', (math.radians(90), 0, math.radians(90)), (6, 0, 1.05))):
    cam.rotation_euler = rot; cam.location = loc
    if view == 'side':
        # side: the line-up runs along the view axis; spread the heads sideways in Y instead
        for o in scene.objects:
            if o.type in ('MESH', 'EMPTY') and o.parent is None and o != cam: o.location = (0, -o.location.x + 1.35, o.location.z)
    scene.render.filepath = os.path.join(HEADS, 'sheet_' + view + '.png')
    bpy.ops.render.render(write_still=True)
    print('SHEET', view, scene.render.filepath)
