"""Read C02 in a disposable background Blender process; never save the source."""
import bpy, json, math, hashlib
import numpy as np
from pathlib import Path
from mathutils import Vector
ROOT=Path('C:/Users/yj666/Oheangbu')
OUT=ROOT/'Art/PlayerPhase1/C02_RigFaceLab/Face'
SOURCE=ROOT/'Art/PlayerPhase1/AutoPlayerV1/Candidates/C02/Candidate_C02_Repaired.blend'

def write(name,value):
    (OUT/name).write_text(json.dumps(value,indent=2,ensure_ascii=False),encoding='utf-8')

def stage():
    s=bpy.context.scene
    for o in list(s.objects):
        if o.type in {'LIGHT','CAMERA'}:bpy.data.objects.remove(o,do_unlink=True)
    s.render.engine='CYCLES';s.cycles.samples=12;s.cycles.use_denoising=True
    s.render.resolution_x=1280;s.render.resolution_y=720;s.render.resolution_percentage=100
    s.render.image_settings.file_format='PNG';s.render.fps=30
    s.world=bpy.data.worlds.new('FaceLabWorld');s.world.use_nodes=True
    bg=next((n for n in s.world.node_tree.nodes if n.type=='BACKGROUND'),None)
    if bg is None:
        bg=s.world.node_tree.nodes.new('ShaderNodeBackground')
        output=s.world.node_tree.nodes.new('ShaderNodeOutputWorld');s.world.node_tree.links.new(bg.outputs[0],output.inputs['Surface'])
    bg.inputs[0].default_value=(.18,.18,.18,1);bg.inputs[1].default_value=.5
    target=Vector((0,-.025,1.595))
    for name,loc,power,size in [('FaceKey',(-1,-2,2.5),250,2),('FaceFill',(1,-1,1.8),100,1.8),('FaceRim',(0,1,2.4),130,1)]:
        data=bpy.data.lights.new(name,'AREA');data.energy=power;data.size=size
        o=bpy.data.objects.new(name,data);s.collection.objects.link(o);o.location=loc;o.rotation_euler=(target-o.location).to_track_quat('-Z','Y').to_euler()
    c=bpy.data.objects.new('FaceCamera',bpy.data.cameras.new('FaceCamera'));s.collection.objects.link(c);s.camera=c
    c.data.type='ORTHO';c.data.ortho_scale=.55;s.view_settings.view_transform='AgX'
    return target

def render(label,direction,target=None):
    s=bpy.context.scene;target=Vector(target or (0,-.025,1.595));c=s.camera
    c.location=target+Vector(direction).normalized()*2;c.rotation_euler=(target-c.location).to_track_quat('-Z','Y').to_euler()
    s.render.filepath=str(OUT/'Previews'/f'{label}.png');bpy.ops.render.render(write_still=True)

def inspect():
    OUT.mkdir(parents=True,exist_ok=True);(OUT/'Previews').mkdir(exist_ok=True)
    rigs=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.get('candidate_mesh')]
    if len(meshes)!=1:raise RuntimeError('Expected one integrated candidate mesh')
    for r in rigs:
        r.animation_data_clear();r.data.pose_position='REST'
        for p in r.pose.bones:p.matrix_basis.identity()
    bpy.context.scene.frame_set(1);bpy.context.view_layer.update()
    records=[]
    for o in meshes:
        m=o.data;m.calc_loop_triangles()
        coords=np.array([v.co[:] for v in m.vertices],dtype=np.float64)
        world=np.array([(o.matrix_world@v.co)[:] for v in m.vertices],dtype=np.float64)
        tris=np.array([t.vertices[:] for t in m.loop_triangles],dtype=np.int32)
        groups=[[(g.group,g.weight) for g in v.groups] for v in m.vertices]
        head=np.where(world[:,2]>1.45)[0]
        np.savez_compressed(OUT/'baseline_geometry.npz',local=coords,world=world,triangles=tris,head_ids=head)
        records.append({'name':o.name,'vertices':len(coords),'triangles':len(tris),'matrix_world':[list(r) for r in o.matrix_world],
          'geometry_sha256':hashlib.sha256(coords.tobytes()+tris.tobytes()).hexdigest(),
          'weights_sha256':hashlib.sha256(json.dumps(groups).encode()).hexdigest(),
          'head_vertex_count':len(head),'head_world_bounds':[world[head].min(0).tolist(),world[head].max(0).tolist()],
          'modifiers':[{'name':x.name,'type':x.type} for x in o.modifiers],
          'materials':[x.name for x in m.materials],'shapekeys':list(m.shape_keys.key_blocks.keys()) if m.shape_keys else []})
    textures=[{'name':i.name,'size':list(i.size),'filepath':i.filepath,'packed':bool(i.packed_file)} for i in bpy.data.images if i.type=='IMAGE']
    write('baseline_inspection.json',{'source':str(SOURCE),'blender_version':bpy.app.version_string,'meshes':records,'textures':textures,
      'bones':[{ 'name':p.name,'head_world':list(r.matrix_world@p.bone.head_local),'tail_world':list(r.matrix_world@p.bone.tail_local)} for r in rigs for p in r.pose.bones],
      'face_structure':'PENDING_GEOMETRY_AND_IMAGE_REVIEW','source_untouched':True,'remaining_triangle_budget':60000-sum(x['triangles'] for x in records)})
    stage()
    for label,d in [('Neutral_front',(0,-1,0)),('Neutral_left',(1,0,0)),('Neutral_right',(-1,0,0)),('Neutral_threequarter',(1,-2,0))]:render(label,d)
    # Save only the independent baseline, with original geometry/weights unchanged.
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Face_Baseline.blend'))
    print('FACE_BASELINE_COMPLETE',json.dumps(records))

if __name__=='__main__':inspect()
