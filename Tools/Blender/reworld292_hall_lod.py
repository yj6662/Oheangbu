import bpy, json
from pathlib import Path
root=Path(__file__).resolve().parents[2]
folder=root/'Art/World/Compact/Rebuild/Reworld292/HallLOD'
output=root/'Oheangbu/Assets/_Project/Art/World/Reworld292/Architecture/HallLOD.obj'
output.parent.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=str(folder/'hall.obj'),forward_axis='NEGATIVE_Z',up_axis='Y')
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
before=sum(len(o.data.polygons) for o in meshes)
for o in meshes:
    if len(o.data.polygons)<60:continue
    bpy.context.view_layer.objects.active=o;o.select_set(True)
    dec=o.modifiers.new('Preserve architectural proportions','DECIMATE');dec.ratio=.12;dec.use_collapse_triangulate=True
    bpy.ops.object.modifier_apply(modifier=dec.name);o.select_set(False)
after=sum(len(o.data.polygons) for o in meshes)
bpy.ops.wm.obj_export(filepath=str(output),forward_axis='NEGATIVE_Z',up_axis='Y',export_materials=True,export_triangulated_mesh=True)
(folder/'reduction.json').write_text(json.dumps(dict(beforeTriangles=before,afterTriangles=after,method='Blender edge-collapse; 12 percent target; source unchanged'),indent=2))
print('LOD',before,'->',after)
