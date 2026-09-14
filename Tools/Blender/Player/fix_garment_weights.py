"""Remove scan auto-rig hand influence from lower robe, preserving authored hands."""
import bpy,json
ROOT='C:/Users/yj666/Oheangbu'
m=bpy.data.objects['Dosa_Body'];r=bpy.data.objects['Dosa_Rig']
bodyverts={i for p in m.data.polygons if p.material_index==0 for i in p.vertices}
armids={g.index for g in m.vertex_groups if any(x in g.name for x in ['Arm','Hand','Shoulder'])}
changed=[]
for v in m.data.vertices:
    if v.index not in bodyverts or v.co.z>=.88:continue
    # The source mesh had robe triangles around z=.72-.80 driven 20-50% by RightHand.
    wristdist=min((v.co-r.data.bones[s+'Hand'].head_local).length for s in ['Left','Right'])
    factor=max(0,min(1,(.88-v.co.z)/.055))*max(0,min(1,(wristdist-.045)/.025))
    old={g.group:g.weight for g in v.groups};amount=sum(w for i,w in old.items() if i in armids)*factor
    if amount<.002:continue
    weights={i:w*(1-factor if i in armids else 1) for i,w in old.items()}
    total=sum(weights.values())
    if total<.01:weights={m.vertex_groups['RightUpLeg' if v.co.x<0 else 'LeftUpLeg'].index:1};total=1
    weights=dict(sorted(weights.items(),key=lambda x:x[1],reverse=True)[:4]);total=sum(weights.values())
    for g in m.vertex_groups:g.remove([v.index])
    for i,w in weights.items():
        if w/total>.0001:m.vertex_groups[i].add([v.index],w/total,'REPLACE')
    changed.append(v.index)
open(ROOT+'/Art/Player/garment-weight-report.json','w',encoding='utf-8').write(json.dumps({'changed_vertices':len(changed),'indices':changed,'reason':'lower robe auto-rig RightHand contamination; hand materials excluded'},indent=2))
print('garment weights corrected',len(changed))
