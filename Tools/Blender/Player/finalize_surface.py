"""Reapply final local cuff weights and edited-hand smooth normals after rebuild."""
import bpy
m=bpy.data.objects['Dosa_Body'];r=bpy.data.objects['Dosa_Rig']
bodyverts={i for p in m.data.polygons if p.material_index==0 for i in p.vertices}
for i in bodyverts:
    v=m.data.vertices[i]
    for side in ['Right','Left']:
        wrist=r.data.bones[side+'Hand'].head_local;elbow=r.data.bones[side+'ForeArm'].head_local;axis=(elbow-wrist).normalized();rel=v.co-wrist;t=rel.dot(axis);rad=(rel-axis*t).length
        if -.015<t<.10 and rad<.055 and v.co.z>wrist.z-.014:
            f=max(0,min(1,(.1-t)/.07));f=f*f*(3-2*f)
            for group in m.vertex_groups:group.remove([i])
            m.vertex_groups[side+'Hand'].add([i],f,'REPLACE');m.vertex_groups[side+'ForeArm'].add([i],1-f,'REPLACE')
handverts={i for p in m.data.polygons if p.material_index==1 for i in p.vertices}
normals=[tuple(n.vector) for n in m.data.corner_normals]
for p in m.data.polygons:
    if p.material_index==1:
        p.use_smooth=True
        for li in p.loop_indices:normals[li]=(0,0,0)
for edge in m.data.edges:
    if all(i in handverts for i in edge.vertices):edge.use_edge_sharp=False
m.data.normals_split_custom_set(normals)
