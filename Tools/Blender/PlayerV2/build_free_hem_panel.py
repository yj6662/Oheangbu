"""Author a separate shoulder-attached, free-hem outer-sleeve candidate.

Source-cut coincidence is not a stitch contract. BodyCore's duplicated moving
underarm cloth is transferred, with exact original corners/UV, to OuterR.
Only the remaining garment/body attachment is fixed; the inner-wrap cut is an
open outer hem. No solver threshold, anatomy, hand, rest bone, or action edits.
"""
import bpy, bmesh, json, hashlib, heapq, os
import numpy as np
from pathlib import Path
from mathutils import Matrix
from mathutils.kdtree import KDTree

ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender'
OLD=BASE/'VerifiedAttachments'
VARIANT=os.environ.get('DOSA_PANEL_VARIANT','original31')
OUT=BASE/'FreeHemPanel' if VARIANT=='original31' else BASE/('TorsoSupportedPanel/SeamLineCandidate' if VARIANT=='torso10line' else 'TorsoSupportedPanel/Candidate'); OUT.mkdir(parents=True,exist_ok=True)
SOURCE=OLD/'Inputs/Assembled-d979b9bc.blend'
SHA='d979b9bc9b5002be66b3a9a5e05524a266703ab81dbe386643674e1a53c86bee'
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==SHA
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
rig=bpy.data.objects['DosaV2_Rig']
for o in bpy.context.scene.objects:
    if o.name.startswith('CTRL_') and 'AuthoringMode' in o:
        o['AuthoringMode']=False; o.update_tag()
for b in rig.pose.bones: b.matrix_basis=Matrix.Identity(4)

def snapshot(obj):
    me=obj.data; me.calc_loop_triangles()
    return {'points':[list(v.co) for v in me.vertices],
      'weights':[{obj.vertex_groups[g.group].name:g.weight for g in v.groups} for v in me.vertices],
      'groups':[g.name for g in obj.vertex_groups], 'materials':list(me.materials),
      'faces':[{'vertices':list(p.vertices),'uv':[list(me.uv_layers.active.data[j].uv) for j in p.loop_indices],
       'normals':[list(me.corner_normals[j].vector) for j in p.loop_indices],
       'smooth':p.use_smooth,'material':p.material_index,'sourceMesh':obj.name,'sourceFace':p.index} for p in me.polygons],
      'colors':{a.name:[list(c.color) for c in a.data] for a in me.color_attributes if a.domain=='POINT'},
      'customNormals':me.has_custom_normals}

def install(obj,data):
    me=bpy.data.meshes.new(obj.name+'_FreeHemPanel')
    me.from_pydata(data['points'],[],[f['vertices'] for f in data['faces']]); me.update()
    for m in data['materials']: me.materials.append(m)
    uv=me.uv_layers.new(name='UVMap'); normals=[]
    for p,f in zip(me.polygons,data['faces']):
        p.use_smooth=f['smooth']; p.material_index=f['material']
        for j,v in zip(p.loop_indices,f['uv']): uv.data[j].uv=v
        normals.extend(f['normals'])
    me.normals_split_custom_set(normals)
    for name,values in data['colors'].items():
        a=me.color_attributes.new(name=name,type='FLOAT_COLOR',domain='POINT')
        for c,v in zip(a.data,values): c.color=v
    obj.data=me
    obj.vertex_groups.clear()
    for n in data['groups']: obj.vertex_groups.new(name=n)
    for i,w in enumerate(data['weights']):
        for n,v in w.items(): obj.vertex_groups[n].add([i],v,'REPLACE')
    me.update()
    # Per-corner UVs survive ownership transfer exactly (float32 source).
    observed=np.array([list(c.uv) for c in me.uv_layers['UVMap'].data]); expected=np.array([u for f in data['faces'] for u in f['uv']])
    assert observed.shape==expected.shape, (obj.name,observed.shape,expected.shape)
    assert np.array_equal(observed,expected), (obj.name,'UV changed',float(np.max(np.abs(observed-expected))),np.flatnonzero(np.any(observed!=expected,axis=1))[:8].tolist())

names=['DosaV2_BodyCore','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']
before={n:snapshot(bpy.data.objects[n]) for n in names}
data={n:snapshot(bpy.data.objects[n]) for n in names}
body=data[names[0]]; right=data[names[2]]
panel=json.loads((OLD/'body-panel-transfer-selection.json' if VARIANT=='original31' else BASE/'TorsoSupportedPanel/panel-10.json').read_text())
assert panel['sourceSha256']==SHA
face_ids=set(panel['selectedBodyFaces']); selected=[f for f in body['faces'] if f['sourceFace'] in face_ids]
assert len(selected)==len(face_ids) and all(len(f['vertices'])==3 for f in selected)
body_to_outer={}; transfer_rows=[]
for f in selected:
    newface={k:v for k,v in f.items()}; newface['vertices']=[]
    for i in f['vertices']:
        if i not in body_to_outer:
            p=np.array(body['points'][i]); distances=np.linalg.norm(np.array(right['points'])-p,axis=1)
            exact=np.flatnonzero(distances<1e-7)
            if len(exact):
                j=int(exact[0]); a=right['weights'][j]; b=body['weights'][i]
                delta=max(abs(a.get(n,0)-b.get(n,0)) for n in set(a)|set(b))
                assert delta<1e-5, ('Shared cloth weights differ',i,j,delta)
            else:
                j=len(right['points']); right['points'].append(body['points'][i]); right['weights'].append(body['weights'][i])
                for name,colors in right['colors'].items():
                    colors.append([0.,0.,0.,1.] if name=='ClothMobility' else [0.,0.,0.,1.])
            body_to_outer[i]=j
        newface['vertices'].append(body_to_outer[i])
    material=body['materials'][f['material']]
    if material not in right['materials']: right['materials'].append(material)
    newface['material']=right['materials'].index(material)
    assert not any(set(t['vertices'])==set(newface['vertices']) for t in right['faces']), ('Duplicate transferred face',f['sourceFace'])
    right['faces'].append(newface)
    transfer_rows.append({'bodySourceFace':f['sourceFace'],'bodySourceVertices':f['vertices'],
                         'outerDestinationFace':len(right['faces'])-1,'outerVertices':newface['vertices'],'UVExact':True})
body['faces']=[f for f in body['faces'] if f['sourceFace'] not in face_ids]
used=sorted({i for f in body['faces'] for i in f['vertices']}); remap={i:j for j,i in enumerate(used)}
for f in body['faces']: f['vertices']=[remap[i] for i in f['vertices']]
body['points']=[body['points'][i] for i in used]; body['weights']=[body['weights'][i] for i in used]
for name,colors in body['colors'].items(): body['colors'][name]=[colors[i] for i in used]
for n in names: install(bpy.data.objects[n],data[n])

def topology(obj):
    me=obj.data; me.calc_loop_triangles(); tri=np.array([list(t.vertices) for t in me.loop_triangles])
    counts={}
    for t in tri:
        for a,b in zip(t,np.roll(t,-1)):
            e=tuple(sorted((int(a),int(b)))); counts[e]=counts.get(e,0)+1
    boundary=sorted({i for e,c in counts.items() if c==1 for i in e})
    return tri,np.array(sorted(counts)),boundary

ledger=json.loads((OLD/'attachment-role-ledger.json').read_text())
tri,edges,body_boundary=topology(bpy.data.objects[names[0]])
bodypoints=np.array(body['points']); tree=KDTree(len(body_boundary))
for j,i in enumerate(body_boundary): tree.insert(bodypoints[i],j)
tree.balance()
reports=[]
for side in ['L','R']:
    name='DosaV2_SleeveOuter_'+side; obj=bpy.data.objects[name]; m=data[name]
    points=np.array(m['points']); triangles,edges,boundary=topology(obj)
    old=next(s for s in ledger['surfaces'] if s['mesh']==name)
    historic={r['vertex']:r for r in old['vertices']}
    seeds={}; rows=[]
    for i in boundary:
        q,j,d=tree.find(points[i])
        # The tree indexes the post-transfer BodyCore boundary, not the sleeve.
        target=body_boundary[j]
        h=historic.get(i,{}).get('oldAuthoredSeed')
        historic_body=bool(h and h['otherMesh']=='DosaV2_BodyCore')
        if d<.0003 or (historic_body and d<=.006):
            seeds[i]={'bodyVertex':target,'bodySourceVertex':used[target], 'restGapMeters':float(d),
             'evidence':'Remaining connected body-garment cut; same source surface, inspected shoulder/torso attachment',
             'historicalBodyCut':historic_body}
    assert seeds, name
    adjacency=[[] for p in points]
    for a,b in edges:
        length=float(np.linalg.norm(points[a]-points[b])); adjacency[a].append((int(b),length)); adjacency[b].append((int(a),length))
    dist=np.full(len(points),np.inf); donor=np.full(len(points),-1,dtype=int); queue=[]
    for i in seeds: dist[i]=0; donor[i]=i; heapq.heappush(queue,(0,i))
    while queue:
        d,i=heapq.heappop(queue)
        if d>dist[i]: continue
        for j,length in adjacency[i]:
            x=d+length
            if x<dist[j]: dist[j]=x; donor[j]=donor[i]; heapq.heappush(queue,(x,j))
    assert np.all(np.isfinite(dist)), ('Disconnected unattached fabric',name,np.flatnonzero(~np.isfinite(dist)).tolist())
    colors=obj.data.color_attributes['ClothMobility']; oldmob=np.array([c.color[0] for c in colors.data]); changed_weights=[]
    for i,d in enumerate(dist):
        h=historic.get(i,{}); seed=h.get('oldAuthoredSeed')
        is_inner=bool(seed and seed['otherMesh']=='DosaV2_SleeveInner_'+side)
        reinforced=VARIANT!='torso10line' and i not in seeds and d<=.006 and oldmob[i]==0 and not is_inner
        if i in seeds:
            new=0.; role='fixed_actual_body_shoulder_attachment'
        elif reinforced:
            new=0.; role='fixed_six_mm_reinforced_attachment_strip'
            source=m['weights'][int(donor[i])]; previous=m['weights'][i]
            if VARIANT=='original31' and source!=previous:
                changed_weights.append({'vertex':i,'donorSewingVertex':int(donor[i]),'before':previous,'after':source.copy()})
                m['weights'][i]=source.copy()
                for g in obj.vertex_groups:
                    if any(x.group==g.index for x in obj.data.vertices[i].groups): g.remove([i])
                for n,w in source.items(): obj.vertex_groups[n].add([i],w,'REPLACE')
        else:
            seam_width=0. if VARIANT=='torso10line' else .006
            t=float(np.clip((d-seam_width)/(.05-seam_width),0,1)); allowance=.18*t*t*(3-2*t)
            new=max(oldmob[i],allowance)
            # No finite positive vertex is silently promoted to a pin.
            if new==0: new=.00001
            role='free_overlapping_outer_hem' if is_inner else 'free_outer_fabric'
        colors.data[i].color=(float(np.float32(new)),*colors.data[i].color[1:])
        rows.append({'vertex':i,'sourceIdentity':{'mesh':name,'vertex':i} if i<len(before[name]['points']) else
          {'mesh':names[0],'vertex':next(k for k,v in body_to_outer.items() if v==i)},
          'role':role,'oldMaxDistanceMeters':float(oldmob[i]) if i<len(before[name]['points']) else None,
          'previousClothState':'ExistingOuterCloth' if i<len(before[name]['points']) else 'BodyCore_LBS_NoCloth',
          'newMaxDistanceMeters':float(np.float32(new)),
          'geodesicDistanceToActualAttachmentMeters':float(d),'nearestSewingVertex':int(donor[i]),
          'attachment':seeds.get(i),'historicalInnerCutNowFree':is_inner})
    reports.append({'mesh':name,'vertices':len(points),'triangles':len(triangles),'oldExactPins':int(np.sum(np.array(before[name]['colors']['ClothMobility'])[:,0]==0)),
      'newlyTransferredVerticesWithoutPriorCloth':len(points)-len(before[name]['points']),
      'newExactPins':sum(r['newMaxDistanceMeters']==0 for r in rows),'sewingSeeds':seeds,
      'coherentReinforcedWeightChanges':changed_weights,'verticesLedger':rows})

DEST=OUT/'DosaV2_FreeHemPanel_Candidate.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(DEST))
counts={}
for n in names:
    obj=bpy.data.objects[n];obj.data.calc_loop_triangles()
    counts[n]={'vertices':len(obj.data.vertices),'triangles':len(obj.data.loop_triangles)}
report={'status':'CANDIDATE_AUTHORED_NEEDS_GEOMETRY_AND_NATIVE_CLOTH_VALIDATION',
 'sourceSha256':SHA,'outputSha256':hashlib.sha256(DEST.read_bytes()).hexdigest(),
 'policy':'Only inspected connected shoulder/body garment attachment stays sewn. The broad ragged outer hem overlaps inner wraps freely. Same cloth underarm faces previously owned by rigid BodyCore move into OuterR, preserving every source corner UV and position. '+('The actual shared joining line is pinned; adjacent fabric seam allowance moves continuously from that line rather than becoming a six-millimeter rigid skin strip. Original skin weights remain exact.' if VARIANT=='torso10line' else 'An existing six-millimeter seam margin remains fixed for the explicit comparison baseline.')+' All other existing cloth gets a smooth distance-based allowance capped at 0.18m; no acceptance threshold changes.',
 'transferredFaces':transfer_rows,'bodySourceToOuterVertexMap':body_to_outer,'bodyRetainedSourceVertexMap':used,
 'counts':counts,'productionActions':len(bpy.data.actions),'bones':len(rig.data.bones),'surfaces':reports}
(OUT/'authoring-ledger.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({'status':report['status'],'outputSha256':report['outputSha256'],'counts':counts,
 'surfaces':[{k:v for k,v in r.items() if k not in ['verticesLedger','sewingSeeds','coherentReinforcedWeightChanges']} for r in reports]},indent=2),flush=True)
