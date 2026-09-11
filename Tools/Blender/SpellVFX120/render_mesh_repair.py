from pathlib import Path
import bpy,json
from mathutils import Vector

ROOT=Path('C:/Users/yj666/Oheangbu')
exec(compile((ROOT/'Tools/Blender/SpellVFX120/mesh_lab.py').read_text(encoding='utf-8'),'mesh_lab.py','exec'))
OUT=ROOT/'Art/SpellVFX120/Blender/MeshRepair'
assert 'SpellVFX120' in bpy.data.filepath,'Only the separate VFX lab may be used.'

def import_with_normals(path,name):
    obj=obj_read(path);obj.name=name
    normals=[]
    for line in path.read_text(encoding='utf-8-sig').splitlines():
        if line.startswith('vn '):normals.append(UNITY_TO_BLENDER@Vector(tuple(map(float,line.split()[1:4]))))
    if len(normals)==len(obj.data.vertices):obj.data.normals_split_custom_set_from_vertices(normals)
    obj.data.materials.clear();obj.data.materials.append(material('RepairNeutralClay',(.36,.33,.27)))
    return obj

objects={}
for family in ['Ring','Rock']:
    objects[family+'_Before']=import_with_normals(ROOT/'Art/SpellVFX120/MeshSources'/f'{family}.obj',family+'_Before')
    objects[family+'_After']=import_with_normals(OUT/f'{family}_Repaired.obj',family+'_After')

for family in ['Ring','Rock']:
    for version in ['Before','After']:
        name=family+'_'+version
        render(name,[objects[name]],view='front' if family=='Ring' else 'threequarter',ortho=2.1)
        if family=='Rock':render(name+'_Top',[objects[name]],view='top',ortho=2.1)
for ob in objects.values():ob.hide_render=ob.name!='Ring_After';ob.hide_set(ob.name!='Ring_After')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Ring_Rock_Repair_Comparison.blend'))
(OUT/'render_complete.json').write_text(json.dumps({'status':'COMPLETE','images':6,'resolution':[1280,720],'text_labels':False}),encoding='utf-8')
