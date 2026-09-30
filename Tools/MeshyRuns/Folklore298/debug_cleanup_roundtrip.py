import bpy,sys,json
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import rig_creatures as R
p=Path(__file__).resolve().parents[3]/'Art/Characters/Folklore298/Derivatives/fox_spirit/run-06-cleanup'
bpy.ops.wm.open_mainfile(filepath=str(p/'candidate.blend'))
obj=next(o for o in bpy.context.scene.objects if o.type=='MESH')
verts=R.world_vertices(obj); a=R.mesh_corner_signature(obj)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(p/'creature.fbx'))
obj=next(o for o in bpy.context.scene.objects if o.type=='MESH')
b=R.mesh_corner_signature(obj,reference_positions=verts)
diff={'source':sum(a.values()),'fbx':sum(b.values()),'missing':list((a-b).items()),'extra':list((b-a).items())}
(p/'roundtrip-difference.json').write_text(json.dumps(diff,indent=2))
print(json.dumps({k:v[:2] if isinstance(v,list)else v for k,v in diff.items()}))
