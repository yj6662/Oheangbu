"""Separate scan-fused waist equipment from the inner forearm sleeve locally.

Only the two observed waist patches are classified. Preserve true sleeve cores,
hand/wrist geometry, original positions, UV, normals, bones and all actions.
The caller owns save/export. Run after fix_waist_arm_weights.py.
"""
import bpy, bmesh, json, math
from pathlib import Path
from mathutils import Vector

ROOT = Path('C:/Users/yj666/Oheangbu')
m = bpy.data.objects['Dosa_Body']
r = bpy.data.objects['Dosa_Rig']
arm_ids = {g.index for g in m.vertex_groups if any(n in g.name for n in ['Arm', 'Hand', 'Shoulder'])}
body_ids = {v for p in m.data.polygons if p.material_index == 0 for v in p.vertices}
wrap_ids = set(range(len(m.data.vertices) - 384, len(m.data.vertices))) if m.get('wrist_wrap_collar') else set()

def classify(point):
    x, y, z = point
    if not .84 < z < 1.25:
        return None
    side = 'Right' if x < 0 else 'Left'
    inside = (-.27 < x < -.065 and -.13 < y < .27) if side == 'Right' else (.14 < x < .36 and -.28 < y < .08)
    if not inside:
        return None
    centers = []
    for first, last in [('Arm', 'ForeArm'), ('ForeArm', 'Hand')]:
        start = r.data.bones[side + first].head_local
        end = r.data.bones[side + last].head_local
        line = end - start
        t = max(0.0, min(1.0, (point - start).dot(line) / line.length_squared))
        centers.append(start + line * t)
    center = min(centers, key=lambda c: (point - c).length_squared)
    radial = point - center
    # Preserve the sleeve tube and its outer loose cloth. The waist equipment is
    # on the torso-facing side, beyond the actual sleeve core.
    lateral_sleeve = x < center.x + .018 if side == 'Right' else ((x > center.x - .035 and y < center.y + .035) or x > center.x + .025)
    sleeve = radial.length < .066 or (radial.length < .135 and lateral_sleeve)
    return side, 'sleeve' if sleeve else 'waist'

changes, removed = [], []
labels = {v.index: classify(v.co) for v in m.data.vertices if v.index in body_ids and v.index not in wrap_ids}
if not m.get('waist_sleeve_separated_v2'):
    for v in m.data.vertices:
        label = labels.get(v.index)
        if label is None:
            continue
        old = {g.group: g.weight for g in v.groups if g.weight > .000001}
        arm_total = sum(w for i, w in old.items() if i in arm_ids)
        if label[1] == 'waist':
            if arm_total < .001:
                continue
            weights = {i: w for i, w in old.items() if i not in arm_ids}
        else:
            if arm_total < .15 or 1 - arm_total < .001:
                continue
            weights = {i: w for i, w in old.items() if i in arm_ids}
        if sum(weights.values()) < .01:
            lower_spine = min((b for b in r.data.bones if b.name in ['Spine', 'Spine01', 'Spine02']), key=lambda b: b.head_local.z).name
            weights = {m.vertex_groups['Hips'].index: .8, m.vertex_groups[lower_spine].index: .2}
        weights = dict(sorted(weights.items(), key=lambda p: p[1], reverse=True)[:4])
        total = sum(weights.values())
        if label[1] == 'sleeve':
            # Keep original shoulder/upper-sleeve blend at the outer patch edge.
            x,y,z=v.co
            bounds=(-.27,-.065,-.13,.27) if x<0 else (.14,.36,-.28,.08)
            fade=min(1.,max(0.,min(x-bounds[0],bounds[1]-x,y-bounds[2],bounds[3]-y,z-.84,1.25-z)/.04))
            fade=fade*fade*(3.-2.*fade)
            weights={i:old.get(i,0.)*(1.-fade)+weights.get(i,0.)/total*fade for i in set(old)|set(weights)}
            weights=dict(sorted(weights.items(),key=lambda p:p[1],reverse=True)[:4]);total=sum(weights.values())
        for g in m.vertex_groups:
            g.remove([v.index])
        for i, w in weights.items():
            m.vertex_groups[i].add([v.index], w / total, 'REPLACE')
        changes.append({'vertex': v.index, 'xyz': list(v.co), 'part': label[1],
                        'before': {m.vertex_groups[i].name: w for i, w in old.items()},
                        'after': {m.vertex_groups[g.group].name: g.weight for g in v.groups}})
    for p in m.data.polygons:
        if p.material_index != 0:continue
        values=[labels.get(i) for i in p.vertices]
        for side in ['Right','Left']:
            outside_arm=any(labels.get(i) is None and sum(g.weight for g in m.data.vertices[i].groups if g.group in arm_ids)>.10 for i in p.vertices)
            if (side,'waist') in values and ((side,'sleeve') in values or outside_arm):
                removed.append(p.index);break
    # Retain all untouched per-corner normals; remove only the scan-fused bridge.
    normals={tuple(sorted(p.vertices)):{m.data.loops[li].vertex_index:tuple(m.data.corner_normals[li].vector) for li in p.loop_indices} for p in m.data.polygons}
    removed_vertices=[list(m.data.polygons[i].vertices) for i in removed]
    # Original fabric supplies lining UVs by closest-surface projection.
    from mathutils.bvhtree import BVHTree
    from mathutils.geometry import barycentric_transform
    m.data.calc_loop_triangles()
    source_tri=[(list(t.vertices),list(t.loops)) for t in m.data.loop_triangles if t.material_index==0]
    source_points=[v.co.copy() for v in m.data.vertices]
    source_uv=[v.uv.copy() for v in m.data.uv_layers['uv'].data]
    tree=BVHTree.FromPolygons(source_points,[x[0] for x in source_tri],all_triangles=True)
    original_count=len(m.data.vertices)
    bm=bmesh.new();bm.from_mesh(m.data);bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm,geom=[bm.faces[i] for i in removed],context='FACES_ONLY')
    bm.to_mesh(m.data);bm.free()
    restored=[]
    for p in m.data.polygons:
        saved=normals[tuple(sorted(p.vertices))]
        restored.extend(saved[m.data.loops[li].vertex_index] for li in p.loop_indices)
    m.data.normals_split_custom_set(restored)
    # A small continuous inner sleeve covers the newly separated inside opening.
    # It follows only Arm/ForeArm and stops before the original hand/wrist wrap.
    verts=[];faces=[];weights=[];segments=24
    for side in ['Right','Left']:
        shoulder=r.data.bones[side+'Arm'].head_local.copy()
        elbow=r.data.bones[side+'ForeArm'].head_local.copy()
        wrist=r.data.bones[side+'Hand'].head_local.copy()
        centers=[]
        for t,rad,w in [(.35,.042,0.),(.55,.046,0.),(.75,.049,.12),(.90,.048,.32)]:centers.append((shoulder.lerp(elbow,t),rad,w))
        centers.append((elbow,.047,.5))
        for t,rad,w in [(.10,.046,.68),(.25,.044,.86),(.45,.042,1.),(.65,.040,1.),(.80,.036,1.)]:centers.append((elbow.lerp(wrist,t),rad,w))
        offset=len(verts)
        for k,(center,rad,forearm) in enumerate(centers):
            axis=(centers[min(k+1,len(centers)-1)][0]-centers[max(k-1,0)][0]).normalized()
            right=Vector((1,0,0));right=(right-axis*right.dot(axis)).normalized();front=axis.cross(right).normalized()
            for j in range(segments):
                ang=j*math.tau/segments
                verts.append(tuple(center+right*(rad*math.cos(ang))+front*(rad*.92*math.sin(ang))))
                weights.append({side+'Arm':1.-forearm,side+'ForeArm':forearm})
        for k in range(len(centers)-1):
            for j in range(segments):
                face=[offset+k*segments+j,offset+k*segments+(j+1)%segments,offset+(k+1)*segments+(j+1)%segments,offset+(k+1)*segments+j]
                points=[Vector(verts[i]) for i in face]
                normal=(points[1]-points[0]).cross(points[2]-points[0])
                radial=(sum(points,Vector())/4)-(centers[k][0]+centers[k+1][0])/2
                if normal.dot(radial)<0:face.reverse()
                faces.append(face)
    mesh=bpy.data.meshes.new('Dosa_InnerSleeves');mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new('Dosa_InnerSleeves',mesh);bpy.context.scene.collection.objects.link(obj);mesh.materials.append(m.data.materials[0]);uv=mesh.uv_layers.new(name='uv')
    for p in mesh.polygons:
        p.use_smooth=True
        for li in p.loop_indices:
            position=mesh.vertices[mesh.loops[li].vertex_index].co
            nearest,normal,index,distance=tree.find_nearest(position)
            vi,loops=source_tri[index]
            uv.data[li].uv=barycentric_transform(nearest,*[source_points[i] for i in vi],*[Vector((*source_uv[i],0)) for i in loops]).xy
    for name in ['LeftArm','LeftForeArm','RightArm','RightForeArm']:
        group=obj.vertex_groups.new(name=name)
        for i,row in enumerate(weights):
            if row.get(name,0)>0:group.add([i],row[name],'REPLACE')
    bpy.ops.object.select_all(action='DESELECT');m.select_set(True);obj.select_set(True);bpy.context.view_layer.objects.active=m;bpy.ops.object.join()
    m['waist_sleeve_separated_v2']=True
    m['wrist_wrap_vertex_range']=[original_count-384,original_count]
    report={'changed_vertices':len(changes),'removed_scan_bridge_faces':len(removed),'removed_face_original_vertices':removed_vertices,
            'added_lining_vertices':len(verts),'added_lining_triangles':len(faces)*2,'lining_vertex_range':[original_count,len(m.data.vertices)],'changes':changes,
            'scope':'Two torso-facing inner sleeve/waist intersections, rest height .84..1.25m; original hands/wrist wraps excluded. Source-UV inner sleeve added along real arms.',
            'original_vertex_positions_bones_actions_unchanged':True,'remaining_face_uv_normals_preserved':True}
    (ROOT/'Art/Player/waist-sleeve-separation-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('WAIST_SLEEVE_SEPARATION',len(changes),'weights;',len(removed),'bridge faces removed;',len(verts),'lining vertices;',len(faces)*2,'lining tris')
