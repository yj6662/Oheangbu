"""Small static geometry/render checks. Never opens or modifies Unity."""
from pathlib import Path
import sys,json,math,importlib.util
import bpy
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[3]
spec=importlib.util.spec_from_file_location('car',Path(__file__).with_name('build_parts.py'));car=importlib.util.module_from_spec(spec);spec.loader.exec_module(car)
base=car.base;out=car.REPORTS/'Images';base.guard()
bpy.ops.wm.open_mainfile(filepath=str(car.REPORTS/'MagicStoneCar_Engine_LOD0.blend'))
core=bpy.data.objects.get('MagicStoneCore');body=bpy.data.objects.get('MagicStoneCar_Engine')
if core is None or body is None:raise RuntimeError('Missing actual core surface renderer')
camera=base.make_camera(bpy.context.scene);camera.data.type='ORTHO';camera.data.ortho_scale=1.4
camera.location=(0,-3,.42);camera.rotation_euler=(Vector((0,-.8,.42))-camera.location).to_track_quat('-Z','Y').to_euler()
body.hide_render=True;bpy.context.scene.render.filepath=str(out/'EngineCore_isolated.png');bpy.ops.render.render(write_still=True)
body.hide_render=False;body.color=(.10,.10,.10,1);core.color=(.05,1,.45,1);bpy.context.scene.display.shading.color_type='OBJECT'
bpy.context.scene.render.filepath=str(out/'EngineCore_region.png');bpy.ops.render.render(write_still=True)
core_stats=base.stats(core)
bpy.ops.wm.open_mainfile(filepath=str(car.REPORTS/'MagicStoneCar_Assembly_LOD0.blend'))
base.guard();scene=bpy.context.scene;camera=base.make_camera(scene);camera.data.type='PERSP';camera.data.lens=45
camera.location=car.vec((6,4.0,7));target=car.vec((0,1.65,0));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(out/'Assembly_threequarter.png');bpy.ops.render.render(write_still=True)
settings=json.loads((car.REPORTS/'assembly_config.json').read_text());camera.location=car.vec(settings['seat']);camera.data.lens=18
target=camera.location+car.vec((0,-math.tan(math.radians(12)),1));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.clip_start=.06
scene.display.shading.show_shadows=False
scene.render.filepath=str(out/'Assembly_seated.png');bpy.ops.render.render(write_still=True)
for obj in car.meshes():obj.hide_render=obj.name.startswith('MagicStoneCar_Cabin')
scene.render.filepath=str(out/'Seated_without_cabin_diagnostic.png');bpy.ops.render.render(write_still=True)
for obj in car.meshes():obj.hide_render=False
from bpy_extras.object_utils import world_to_camera_view
needle_faces=[]
for obj in car.meshes():
    obj.data.calc_loop_triangles()
    for tri in obj.data.loop_triangles:
        projected=[world_to_camera_view(scene,camera,obj.matrix_world@obj.data.vertices[i].co) for i in tri.vertices]
        if not all(v.z>0 for v in projected):continue
        xs=[v.x*1920 for v in projected];ys=[(1-v.y)*1080 for v in projected]
        if max(xs)>790 and min(xs)<820 and min(ys)<740 and max(ys)>650:
            width=max(xs)-min(xs);height=max(ys)-min(ys)
            if height>60 and width<30:needle_faces.append({'object':obj.name,'polygon':tri.polygon_index,'x':xs,'y':ys,'localVertices':[list(obj.data.vertices[i].co) for i in tri.vertices]})
car.write(car.REPORTS/'SeatedNeedles.json',needle_faces)
core_assembly=bpy.data.objects.get('MagicStoneCore');bbox=car.totals([core_assembly])['actualBounds'];socket=settings['engineCore']
centre=bbox['center'];distance=math.dist(socket,[centre[k] for k in ('x','y','z')])
report={'scope':'Static Blender inspection images, no runtime/camera movement/physics verification','coreIsOriginalMeshSurface':True,'coreSourceSurfaceStats':core_stats,'coreAssembledBounds':bbox,'socketDistanceFromSurfaceBoundsCenterM':distance,'socketWithinBounds':all(abs(socket[i]-centre[k])<=bbox['size'][k]*.5+.005 for i,k in enumerate(('x','y','z'))),'images':[str(out/n) for n in ['EngineCore_isolated.png','EngineCore_region.png','Assembly_threequarter.png','Assembly_seated.png']],'visualInspection':'PENDING_READING_IMAGES'}
car.write(car.REPORTS/'StaticInspection.json',report)
print(json.dumps(report))
