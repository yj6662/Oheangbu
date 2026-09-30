"""Read native deer geometry without saving or modifying the source blend."""
import bpy, json, hashlib
from pathlib import Path

ROOT=Path('C:/Users/yj666/Oheangbu')
SOURCE=ROOT/'Art/SpellVFX120/WoodDeer/WoodDeer_Working.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
objects=[o for o in bpy.context.scene.objects if o.type=='MESH']
rows=[]
for o in objects:
    o.data.calc_loop_triangles()
    rows.append(dict(name=o.name, vertices=len(o.data.vertices), tris=len(o.data.loop_triangles),
                     location=list(o.location), rotation=list(o.rotation_euler), scale=list(o.scale),
                     bounds=[[min(v.co[i] for v in o.data.vertices),max(v.co[i] for v in o.data.vertices)] for i in range(3)],
                     modifiers=[m.type for m in o.modifiers], groups=len(o.vertex_groups)))
body=bpy.data.objects['WoodDeer_Body']; mesh=body.data
sections=[]
for height in [.2,.4,.65,.85,1.0,1.12,1.25,1.4]:
    ids={v.index for v in mesh.vertices if v.co.z<height}
    adj={i:[] for i in ids}
    for e in mesh.edges:
        a,b=e.vertices
        if a in ids and b in ids: adj[a].append(b);adj[b].append(a)
    components=[]
    while ids:
        todo=[ids.pop()]; found=[]
        while todo:
            i=todo.pop();found.append(i)
            for j in adj[i]:
                if j in ids: ids.remove(j);todo.append(j)
        if len(found)<8: continue
        points=[mesh.vertices[i].co for i in found]
        top=[p for p in points if p.z>height-.13]
        components.append(dict(n=len(found), centroid=[sum(p[i] for p in points)/len(points) for i in range(3)],
            topCentroid=[sum(p[i] for p in top)/len(top) for i in range(3)] if top else None,
            bounds=[[min(p[i] for p in points),max(p[i] for p in points)] for i in range(3)]))
    sections.append(dict(height=height,components=sorted(components,key=lambda c:-c['n'])))
out=ROOT/'Art/Demo/Summons/WoodDeer';out.mkdir(parents=True,exist_ok=True)
report=dict(source=str(SOURCE),sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),objects=rows,legSections=sections)
(out/'native_geometry_inspection.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('DEER_NATIVE_INSPECTION '+json.dumps(report),flush=True)
