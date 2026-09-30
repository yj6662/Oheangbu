"""Read the approved turtle, preserving the source; inventory before combat rigging."""
import bpy, json, hashlib
from pathlib import Path
from mathutils import Vector

root=Path('C:/Users/yj666/Oheangbu')
source=root/'Art/SpellVFX120/WaterTurtle/WaterTurtle_Working.blend'
out=root/'Art/Demo/Summons/WaterTurtle'
out.mkdir(parents=True, exist_ok=True)
digest=hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(source))
rows=[]
for obj in bpy.context.scene.objects:
    row={'name':obj.name,'type':obj.type,'location':list(obj.location),'rotation':list(obj.rotation_euler),'scale':list(obj.scale)}
    if obj.type=='MESH':
        obj.data.calc_loop_triangles()
        points=[obj.matrix_world@v.co for v in obj.data.vertices]
        row.update(vertices=len(points), triangles=len(obj.data.loop_triangles),
                   bounds=[[min(p[i] for p in points),max(p[i] for p in points)] for i in range(3)],
                   materials=[m.name if m else None for m in obj.data.materials],
                   modifiers=[m.type for m in obj.modifiers], groups=[g.name for g in obj.vertex_groups])
        # Connected low slices identify toes/legs without assuming a mammal's proportions.
        slices=[]
        for height in (.08,.18,.3,.45,.6,.8,1):
            ids={i for i,p in enumerate(points) if p.z<height}
            adj={i:[] for i in ids}
            for edge in obj.data.edges:
                a,b=edge.vertices
                if a in ids and b in ids: adj[a].append(b);adj[b].append(a)
            parts=[]
            while ids:
                stack=[ids.pop()];found=[]
                while stack:
                    i=stack.pop();found.append(i)
                    for j in adj[i]:
                        if j in ids:ids.remove(j);stack.append(j)
                if len(found)<8:continue
                ps=[points[i] for i in found]
                parts.append({'count':len(ps),'centre':list(sum(ps,Vector())/len(ps)),
                              'bounds':[[min(p[i] for p in ps),max(p[i] for p in ps)] for i in range(3)]})
            slices.append({'height':height,'parts':sorted(parts,key=lambda x:-x['count'])})
        row['lowSlices']=slices
    rows.append(row)
after=hashlib.sha256(source.read_bytes()).hexdigest()
assert after==digest
(out/'source_inventory.json').write_text(json.dumps({'status':'READ_ONLY_INVENTORY','source':str(source),
    'sha256Before':digest,'sha256After':after,'objects':rows,'unverified':['Combat rig','Animation','Water attack','Actual runtime']},indent=2),encoding='utf-8')
print('TURTLE_INVENTORY_COMPLETE',out/'source_inventory.json')
