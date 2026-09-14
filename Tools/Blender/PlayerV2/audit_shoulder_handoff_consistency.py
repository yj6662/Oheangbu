"""Read-only source/derivative consistency and real export inventory."""
import bpy,json,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderComplete'
def digest(value):return hashlib.sha256(json.dumps(value,sort_keys=True,separators=(',',':')).encode()).hexdigest()
def read(path):
    bpy.ops.wm.open_mainfile(filepath=str(path));rig=bpy.data.objects['DosaV2_Rig'];meshes={};inventory=[]
    for o in bpy.context.scene.objects:
        if o.type!='MESH':continue
        m=o.data;m.calc_loop_triangles()
        data={'vertices':[v.co[:] for v in m.vertices],'faces':[list(p.vertices) for p in m.polygons],'edges':[list(e.vertices) for e in m.edges],
          'weights':[[[o.vertex_groups[g.group].name,g.weight] for g in v.groups] for v in m.vertices],
          'groups':[g.name for g in o.vertex_groups],'uvs':{layer.name:[p.uv[:] for p in layer.data] for layer in m.uv_layers},
          'colors':{layer.name:[p.color[:] for p in layer.data] for layer in m.color_attributes},
          'materialNames':[mat.name for mat in m.materials],'materialIndices':[p.material_index for p in m.polygons],
          'shapeKeys':{key.name:[p.co[:] for p in key.data] for key in m.shape_keys.key_blocks} if m.shape_keys else None,
          'transform':list(sum((list(row) for row in o.matrix_world),[]))}
        meshes[o.name]={key:digest(value) for key,value in data.items()}
        if o.name.startswith(('DosaV2_','DosaPackV2_')) and o.name!='DosaV2_SourceSurface':inventory.append({'name':o.name,'vertices':len(m.vertices),'triangles':len(m.loop_triangles)})
    bones=[{'name':b.name,'parent':b.parent.name if b.parent else None,'matrix':[list(row) for row in b.matrix_local],'deform':b.use_deform} for b in rig.data.bones]
    return {'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'meshes':meshes,'bones':digest(bones),'boneCount':len(bones),'actions':len(bpy.data.actions),'inventory':inventory}
source=read(ROOT/'Art/PlayerV2/Inspect/ClothBlender/GlobalEnvelope/DosaV2_GlobalClothEnvelope.blend')
world=read(OUT/'DosaV2_ShoulderComplete_Candidate.blend');near=read(OUT/'NearOverrides/DosaV2_NearArmLining18.blend')
added=set(world['meshes'])-set(source['meshes']);assert added=={'DosaV2_ShoulderLining_Left','DosaV2_ShoulderLining_Right'}
assert not(set(source['meshes'])-set(world['meshes']))
changes=[]
for name,row in source['meshes'].items():
    fields=[key for key,value in row.items() if world['meshes'][name][key]!=value]
    expected={'DosaV2_BodyCore':['vertices'],'DosaV2_SleeveOuter_L':['vertices','edges'],'DosaV2_SleeveOuter_R':['edges']}.get(name,[])
    assert sorted(fields)==sorted(expected),(name,fields,expected)
    if fields:changes.append({'mesh':name,'changedFieldsOnly':fields})
near_changes=[]
for name,row in world['meshes'].items():
    fields=[key for key,value in row.items() if near['meshes'][name][key]!=value]
    if name in ['DosaV2_ArmLining_Left','DosaV2_ArmLining_Right']:
        assert 'groups' not in fields and 'materialNames' not in fields and 'transform' not in fields
        near_changes.append({'mesh':name,'changedFieldsOnly':fields})
    else:assert not fields,(name,fields)
assert source['bones']==world['bones']==near['bones'] and source['boneCount']==98
assert source['actions']==world['actions']==near['actions']==0
near_names=lambda n:n=='DosaV2_Hands' or n.startswith(('DosaV2_Sleeve','DosaV2_ArmLining','DosaV2_ShoulderLining'))
report={'status':'DERIVATIVE_SOURCE_CONSISTENCY_VERIFIED_NOT_RIG_PASS','sourceSha256':source['sha256'],'worldSha256':world['sha256'],'nearSha256':near['sha256'],
 'existingWorldChanges':changes,'newWorldMeshes':sorted(added),'nearOnlyChanges':near_changes,'all98BoneRestParentsDeformFlagsExact':True,'productionActions':0,
 'worldTriangles':sum(r['triangles'] for r in world['inventory']),'nearTriangles':sum(r['triangles'] for r in near['inventory'] if near_names(r['name'])),
 'worldInventory':world['inventory'],'nearInventory':[r for r in near['inventory'] if near_names(r['name'])]}
assert report['worldTriangles']<=75000 and report['nearTriangles']<=26000
(OUT/'consistency-audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2),flush=True)
