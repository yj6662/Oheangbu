"""Repair static garment seams and inner coverage without touching hand calibration."""
import bpy,bmesh,json,math
from pathlib import Path
from mathutils import Vector,Matrix
from mathutils.kdtree import KDTree
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/PlayerV2'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'DosaV2_Materials.blend'))
rig=bpy.data.objects['DosaV2_Rig'];s=bpy.context.scene
def nearest(p,names,radius):
    pairs=[]
    for name in names:
        b=rig.data.bones[name];d=b.tail_local-b.head_local;t=max(0,min(1,(p-b.head_local).dot(d)/d.length_squared))
        pairs.append((name,(p-b.head_local-d*t).length))
    pairs.sort(key=lambda v:v[1]);minimum=pairs[0][1]
    weights=[(n,math.exp(-((d-minimum)/radius)**2*6)) for n,d in pairs[:4]]
    total=sum(w for n,w in weights);return {n:w/total for n,w in weights}
def assign(obj,index,values):
    for g in obj.vertex_groups:g.remove([index])
    values=sorted(((n,w) for n,w in values.items() if w>1e-7),key=lambda v:v[1],reverse=True)[:4]
    total=sum(w for n,w in values)
    for name,w in values:
        group=obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name);group.add([index],w/total,'REPLACE')
def base_arm(p):
    side='Left' if p.x>0 else 'Right'
    torso=nearest(p,['Spine','Spine01','Spine02','Neck'],.075)
    arm=nearest(p,[side+'Shoulder',side+'Arm',side+'ForeArm',side+'Hand'],.09)
    t=max(0,min(1,(abs(p.x)-.085)/.18));t=t*t*(3-2*t)
    result={n:w*(1-t) for n,w in torso.items()}
    for n,w in arm.items():result[n]=result.get(n,0)+w*t
    return result
affected=[o for o in s.objects if o.name in ('DosaV2_BodyCore','DosaV2_Head') or o.name.startswith(('DosaV2_SleeveOuter','DosaV2_SleeveInner','DosaV2_ArmLining'))]
boundaries=[]
for obj in affected:
    bm=bmesh.new();bm.from_mesh(obj.data);bm.verts.ensure_lookup_table()
    indices={v.index for e in bm.edges if e.is_boundary for v in e.verts}
    for index in indices:boundaries.append((obj.name,index,obj.data.vertices[index].co.copy()))
    bm.free()
kd=KDTree(len(boundaries))
for i,(_,_,p) in enumerate(boundaries):kd.insert(p,i)
kd.balance();pins=0;changed=0
for obj in affected:
    color=obj.data.color_attributes.get('ClothMobility')
    for v in obj.data.vertices:
        p=v.co
        if p.z<1.10:continue
        if obj.name=='DosaV2_Head' and (p.z>1.48 or abs(p.x)<.095):continue
        values=base_arm(p)
        if color:
            # Shared source-cut seams stay sewn. Broad lower hems retain independent motion.
            seam=any(boundaries[i][0]!=obj.name for co,i,d in kd.find_range(p,.006))
            if seam:
                color.data[v.index].color=(0,0,0,1);pins+=1
            elif color.data[v.index].color[0]>0:
                t=min(1,color.data[v.index].color[0]/.14)*.8
                values={n:w*(1-t) for n,w in values.items()}
                label='L' if p.x>0 else 'R';f=max(0,min(2.99,(1.32-p.z)/.055));a=min(2,int(f));b=min(2,a+1)
                for index,w in [(a,1-(f-a)),(b,f-a)]:
                    n='J_SleeveHem_'+label+'_'+str(index);values[n]=values.get(n,0)+w*t
        assign(obj,v.index,values);changed+=1
# Inner boot coverage should stay inside the authored boot silhouette.
for side,sign in [('Left',1),('Right',-1)]:
    obj=bpy.data.objects['DosaV2_LegLining_'+side]
    for v in obj.data.vertices:
        if v.co.z<.48:
            t=max(0,min(1,(.48-v.co.z)/.18));factor=1-.40*t
            v.co.x=sign*.096+(v.co.x-sign*.096)*factor
            v.co.y=.008+(v.co.y-.008)*factor
    obj.data.update()
# Keep exact calibrated socket orientation, including roll, in the rest skeleton.
calibration=json.loads((OUT/'Calibration/hand-brush-contact-report.json').read_text())
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.object.mode_set(mode='EDIT')
for side,entry in calibration['sides'].items():
    b=rig.data.edit_bones[side+'BrushGrip'];b.matrix=Matrix(entry['grip_matrix_rig_local']);b.length=.035
bpy.ops.object.mode_set(mode='OBJECT')
# Remove unused material slots before FBX; its importer omits them by design.
for obj in [o for o in s.objects if o.type=='MESH' and o.name!='DosaV2_SourceSurface']:
    used=sorted({p.material_index for p in obj.data.polygons});mapping={old:i for i,old in enumerate(used)}
    mats=[obj.data.materials[i] for i in used]
    indices=[mapping[p.material_index] for p in obj.data.polygons]
    obj.data.materials.clear()
    for mat in mats:obj.data.materials.append(mat)
    for p,index in zip(obj.data.polygons,indices):p.material_index=index
report={'status':'REPAIRED_PENDING_POSE_REVIEW','changed_upper_vertices':changed,'pinned_seam_vertices':pins,
        'hand_geometry_and_weights_changed':False,'production_actions':len(bpy.data.actions)}
(OUT/'Inspect/weight-seam-repair.json').write_text(json.dumps(report,indent=2))
bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'DosaV2_Refined.blend'))
print(json.dumps(report))
