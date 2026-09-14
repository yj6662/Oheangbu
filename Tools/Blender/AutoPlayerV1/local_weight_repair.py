"""Single bounded repair of numerically located discontinuities; no mesh edits."""
import bpy, json, hashlib
import numpy as np
from pathlib import Path

def repair_local_weights(candidate='C01', shared_palette=False, audit_folder='Repair1Audit', seed_edge_ids=None):
    root=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/AutoPlayerV1/Candidates')/candidate
    obj=bpy.data.objects[candidate+'_Mesh_0']; rig=next(m.object for m in obj.modifiers if m.type=='ARMATURE')
    result=json.loads((root/'candidate_result.json').read_text())
    if result.get('corrections_used',0)>=2: raise RuntimeError('Repair limit reached')
    index=result.get('corrections_used',0)+1
    bpy.ops.wm.save_as_mainfile(filepath=str(root/f'BeforeRepair{index}.blend'),copy=True)
    verts=np.array([tuple(v.co) for v in obj.data.vertices]); pos={}; vi=[]
    for i,p in enumerate(verts):
        key=tuple(np.round(p,5)); pos.setdefault(key,[]).append(i); vi.append(key)
    names=list(rig.data.bones.keys()); weights={}; neighbors={p:set() for p in pos}
    for p,ids in pos.items():
        w=np.zeros(len(names))
        for g in obj.data.vertices[ids[0]].groups:
            n=obj.vertex_groups[g.group].name
            if n in names:w[names.index(n)]=g.weight
        weights[p]=w
    for e in obj.data.edges:
        a,b=[vi[i] for i in e.vertices]
        if a!=b:neighbors[a].add(b);neighbors[b].add(a)
    warnings=json.loads((root/'warning_edge_vertices.json').read_text())['over4']
    if shared_palette:
        edge_ids=set()
        for f in (root/audit_folder).glob('*_audit.json'):
            edge_ids.update(json.loads(f.read_text())['meshes'][obj.name]['warn_edges_over_4_unique'])
        warnings=[{'edge_index':e,'vertices':[{'id':i} for i in obj.data.edges[e].vertices]} for e in edge_ids]
    if seed_edge_ids is not None:
        selected={vi[i] for e in seed_edge_ids for i in obj.data.edges[e].vertices}
        while True:
            enlarged=selected|{vi[v['id']] for e in warnings if any(vi[v['id']] in selected for v in e['vertices']) for v in e['vertices']}
            if enlarged==selected:break
            selected=enlarged
        warnings=[e for e in warnings if all(vi[v['id']] in selected for v in e['vertices'])]
    targets={vi[v['id']] for e in warnings for v in e['vertices']}
    if len(targets)>100:raise RuntimeError('More than 100 distinct positions: not a small local repair')
    original={p:w.copy() for p,w in weights.items()}; changes=[]
    # Smooth only flagged locations against their immediate connected neighbours.
    # Duplicate render vertices at the same position receive the same result.
    for p in targets:
        nearby=list(neighbors[p]); sample=[p]+nearby
        avg=np.mean([original[q] for q in sample],axis=0)
        w=.2*original[p]+.8*avg
        keep=np.argsort(w)[-4:]; w[np.setdiff1d(np.arange(len(w)),keep)]=0; w/=w.sum()
        weights[p]=w
    if shared_palette:
        links={p:set() for p in targets}
        for e in warnings:
            a,b=[vi[v['id']] for v in e['vertices']];links[a].add(b);links[b].add(a)
        todo=set(targets)
        while todo:
            group=set();stack=[todo.pop()]
            while stack:
                p=stack.pop()
                if p in group:continue
                group.add(p);todo.discard(p);stack.extend(links[p]-group)
            w=np.mean([original[p] for p in group],axis=0);keep=np.argsort(w)[-4:]
            w[np.setdiff1d(np.arange(len(w)),keep)]=0;w/=w.sum()
            for p in group:weights[p]=w.copy()
    for p in targets:
        ids=pos[p]; before=original[p]; after=weights[p]
        changes.append({'vertex_ids':ids,'local_position':list(p),'before':{n:float(v) for n,v in zip(names,before) if v>0},'after':{n:float(v) for n,v in zip(names,after) if v>0}})
        for g in obj.vertex_groups:g.remove(ids)
        for n,w in zip(names,after):
            if w>0:obj.vertex_groups[n].add(ids,float(w),'REPLACE')
    record={'repair_index':index,'cause':'Localized abrupt bone-influence boundaries at >4x warning edges',
        'method':('Shared constant max4 normalized palette per tiny connected unresolved warning group' if shared_palette else '80% mean of immediate connected rest-position neighbours, only flagged locations; max4 normalized'),
        'unique_positions_changed':len(targets),'vertex_ids_changed':sum(len(pos[p]) for p in targets),
        'geometry_uv_rest_bones_changed':False,'global_cross_leg_removal':False,'selected_seed_edge_ids':seed_edge_ids,'changes':changes,'validation':'PENDING'}
    (root/f'repair_{index}.json').write_text(json.dumps(record,indent=2),encoding='utf8')
    result['corrections_used']=index;result['status']='REPAIR_REVALIDATION';(root/'candidate_result.json').write_text(json.dumps(result,indent=2),encoding='utf8')
    bpy.ops.wm.save_as_mainfile(filepath=str(root/f'Repair{index}.blend'))
    return {k:v for k,v in record.items() if k!='changes'}
