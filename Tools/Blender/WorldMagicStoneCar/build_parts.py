"""MagicStoneCar authoring. No Meshy/Unity requests; never edits Palanquin.

Run only after root gives memory permission. inspect -> read PNGs -> build with
explicit orientation -> pack_pbr.py -> assemble. All geometry is a rigid part.
FBX importer transforms must be preserved below manifest placement anchors.
"""
from pathlib import Path
import argparse, importlib.util, json, math, sys
import bpy, bmesh, numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT=Path(__file__).resolve().parents[3]
ART=ROOT/'Art/World/WorldMacro/MagicStoneCar'
ASSET=ROOT/'Oheangbu/Assets/_Project/Art/World/WorldMacro/MagicStoneCar'
MODELS=ASSET/'Models'; TEXTURES=ASSET/'Textures'; REPORTS=ART/'Blender'
spec=importlib.util.spec_from_file_location('palanquin_helpers',ROOT/'Tools/Blender/WorldPalanquin/build_parts.py')
base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
# Reuse stateless Blender helpers with this run's explicitly owned destinations.
base.ART=ART;base.ASSET=ASSET;base.MODELS=MODELS;base.TEXTURES=TEXTURES;base.REPORTS=REPORTS
PARTS=('Cabin','Roof','Engine','Wheel')
LIMITS={'Cabin':[16000,8000,4000],'Roof':[8000,4000,2000],'Engine':[8000,4000,2000],'Wheel':[2500,1200,600]}
UNITY_AXLE_TRIANGLES=64  # Two existing runtime Rod8 axles; counted in final Unity placement.
SUPPORT_LIMITS=[4000-UNITY_AXLE_TRIANGLES,3000-UNITY_AXLE_TRIANGLES,1600-UNITY_AXLE_TRIANGLES]
TOTAL_LIMITS=[50000,24000,12000]
# Initial fit envelope; inspect the real model before choosing a fit mode.
SIZES={'Cabin':(1.8,1.90,2.9),'Roof':(2.4,.75,3.4),'Engine':(1.6,1.15,1.45),'Wheel':(.25,1.2,1.2)}
PLACEMENTS={'Cabin':(0,.90,-.45),'Roof':(0,2.75,-.45),'Engine':(0,.65,1.375)}
WHEELS=[('FL',-1.075,1.25),('FR',1.075,1.25),('RL',-1.075,-1.25),('RR',1.075,-1.25)]

def vec(v):return Vector((v[0],-v[2],v[1]))
def dictvec(v):return dict(x=float(v[0]),y=float(v[1]),z=float(v[2]))
def write(p,d):base.write_json(p,d)
def meshes():return [o for o in bpy.context.scene.objects if o.type=='MESH']
def totals(objects):
    rows=[base.stats(o) for o in objects]
    if not rows:raise RuntimeError('No mesh in group')
    low=[min(r['bounds_blender']['minimum'][i] for r in rows) for i in range(3)]
    high=[max(r['bounds_blender']['maximum'][i] for r in rows) for i in range(3)]
    return dict(triangles=sum(r['triangles'] for r in rows),vertices=sum(r['vertices'] for r in rows),
        meshCount=len(rows),materials=sorted(set(m for r in rows for m in r['materials'] if m)),
        uvLayers={o.name:len(o.data.uv_layers) for o in objects},
        nonfinite=sum(r['nonfinite_vertices'] for r in rows),boundsBlender=dict(minimum=low,maximum=high),
        actualBounds=dict(center=base.unity((Vector(low)+Vector(high))*.5),
            size=dict(x=high[0]-low[0],y=high[2]-low[2],z=high[1]-low[1])))

def select(objects):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.hide_set(False);o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]

def export(objects,path):
    path.parent.mkdir(parents=True,exist_ok=True);select(objects)
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH'},global_scale=1,
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',
        bake_space_transform=False,use_mesh_modifiers=True,mesh_smooth_type='OFF',use_triangles=True,
        add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE',embed_textures=False)

def views(objects,prefix):
    camera=base.make_camera(bpy.context.scene);st=totals(objects);lo=Vector(st['boundsBlender']['minimum']);hi=Vector(st['boundsBlender']['maximum']);c=(lo+hi)*.5
    files=[]
    for name,direction in [('front',(0,-1,.10)),('back',(0,1,.10)),('side',(1,0,.10)),('top',(0,0,1))]:
        base.guard();camera.location=c+Vector(direction).normalized()*max((hi-lo).length*2,1)
        camera.rotation_euler=(c-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO'
        camera.data.ortho_scale=max(hi.x-lo.x,hi.y-lo.y,(hi.z-lo.z)*16/9)*1.18
        path=REPORTS/'Images'/f'{prefix}_{name}.png';path.parent.mkdir(parents=True,exist_ok=True)
        bpy.context.scene.render.filepath=str(path);bpy.ops.render.render(write_still=True);files.append(str(path))
    bpy.data.objects.remove(camera,do_unlink=True)
    return files

def inspect(args):
    obj,report=base.import_raw(args.part);obj.name=f'MagicStoneCar_{args.part}_Raw'
    report['views']=views([obj],args.part+'_raw');report['status']='INSPECT_ONLY'
    write(REPORTS/(args.part+'_raw.json'),report)
    print(json.dumps({'part':args.part,'raw':report['raw'],'views':report['views']}))

def fit(obj,args):
    target=args.size or SIZES[args.part]
    obj.rotation_mode='XYZ';obj.rotation_euler=(0,0,math.radians(args.yaw_deg));base.active(obj)
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=False)
    if args.part=='Wheel':
        co=np.array([v.co[:] for v in obj.data.vertices]);eigen,axes=np.linalg.eigh(np.cov(co.T));axle=Vector(axes[:,int(np.argmin(eigen))])
        outer=Vector(args.outer_vector)
        if axle.dot(outer)<0:axle.negate()
        rotation=axle.rotation_difference(Vector((1,0,0)))
        for v in obj.data.vertices:v.co=rotation@v.co
        obj.data.update();low,high=base.bbox(obj);centre=(low+high)*.5
        for v in obj.data.vertices:v.co-=centre
        obj.data.update();low,high=base.bbox(obj);size=high-low
        factors=(target[0]/size.x,target[2]/size.y,target[1]/size.z)
    else:
        base.centre_bottom(obj);low,high=base.bbox(obj);size=high-low
        factors=(target[0]/size.x,target[2]/size.y,target[1]/size.z)
        if args.fit_mode=='uniform':factors=(min(factors),)*3
    for v in obj.data.vertices:
        for i in range(3):v.co[i]*=factors[i]
    obj.data.update()
    if args.part=='Wheel':
        # Preserve hub/spokes; only clamp the outer surface to the physical radius.
        for v in obj.data.vertices:
            r=math.hypot(v.co.y,v.co.z)
            if r>.6:v.co.y*=.6/r;v.co.z*=.6/r
        obj.data.update()
    else:base.centre_bottom(obj)
    return dict(yawBlenderDegrees=args.yaw_deg,fitMode=args.fit_mode,axisFactors=list(factors),targetEnvelopeUnity=list(target),actual=base.stats(obj))

def split_core(obj,box):
    if not box:return [obj],dict(status='MASK_FALLBACK_REQUIRED',reason='No inspected core surface region supplied; no floating substitute geometry')
    # Explicit box must be chosen from fitted model inspection, in part-local Unity units.
    lo=Vector((box[0],box[2],box[4]));hi=Vector((box[1],box[3],box[5]));selected=[]
    shader=obj.data.materials[0].node_tree.nodes.get('Principled BSDF')
    image=shader.inputs['Base Color'].links[0].from_node.image
    w,h=image.size;pixels=np.empty(w*h*4,dtype=np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape(h,w,4)
    uv=obj.data.uv_layers.active.data
    for p in obj.data.polygons:
        c=base.unity(p.center)
        tex=sum((uv[i].uv for i in p.loop_indices),Vector((0,0)))/len(p.loop_indices)
        r,g,b=pixels[min(h-1,max(0,int(tex.y*h))),min(w-1,max(0,int(tex.x*w))),:3]
        if all(lo[i]<=c[k]<=hi[i] for i,k in enumerate(('x','y','z'))) and min(g,b)>r*1.22 and g>.12 and b>.12:selected.append(p.index)
    del pixels
    if not selected or len(selected)==len(obj.data.polygons):raise RuntimeError('Core region empty or whole engine; inspect and correct box')
    group=obj.vertex_groups.new(name='CoreSelection')
    for i in selected:
        for v in obj.data.polygons[i].vertices:group.add([v],1,'REPLACE')
    chosen=set(selected);base.active(obj)
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='DESELECT');bpy.ops.object.mode_set(mode='OBJECT')
    for p in obj.data.polygons:p.select=p.index in chosen
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_mode(type='FACE');bpy.ops.mesh.separate(type='SELECTED');bpy.ops.object.mode_set(mode='OBJECT')
    core=next(o for o in bpy.context.selected_objects if o!=obj);core.name='MagicStoneCore';obj.name='MagicStoneCar_Engine'
    return [obj,core],dict(status='SEPARATED_FOR_VISUAL_REVIEW',sourceFaces=len(selected),regionUnity=box,core=base.stats(core),verifiedSemanticCore=False)

def reduce_group(objects,target):
    before=totals(objects);remaining=target;weights=[base.stats(o)['triangles'] for o in objects];total=sum(weights)
    for i,o in enumerate(objects):
        allowance=remaining if i==len(objects)-1 else max(32,int(target*weights[i]/total))
        allowance=min(allowance,remaining);remaining-=allowance
        if weights[i]>allowance:
            base.active(o);m=o.modifiers.new('Budget','DECIMATE');m.ratio=allowance*.985/weights[i];m.use_collapse_triangulate=True
            bpy.ops.object.modifier_apply(modifier=m.name)
        # Collapse can leave zero-area faces that FBX silently omits. Remove
        # them explicitly so editable mesh and actual export have equal counts.
        bm=bmesh.new();bm.from_mesh(o.data)
        bmesh.ops.triangulate(bm,faces=list(bm.faces))
        bm.verts.index_update();seen=set();bad=[]
        for f in bm.faces:
            key=tuple(sorted(v.index for v in f.verts))
            if f.calc_area()<1e-10 or key in seen:bad.append(f)
            seen.add(key)
        if bad:bmesh.ops.delete(bm,geom=bad,context='FACES_ONLY')
        bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=1e-7)
        loose=[e for e in bm.edges if not e.link_faces]
        if loose:bmesh.ops.delete(bm,geom=loose,context='EDGES')
        isolated=[v for v in bm.verts if not v.link_edges]
        if isolated:bmesh.ops.delete(bm,geom=isolated,context='VERTS')
        bm.to_mesh(o.data);bm.free();o.data.update();bpy.context.view_layer.update()
        # All exact export counts include triangulation; no hidden thickness modifiers.
    after=totals(objects);drift=max(abs(after['boundsBlender'][k][i]-before['boundsBlender'][k][i]) for k in ('minimum','maximum') for i in range(3))
    if after['triangles']>target or after['nonfinite']:raise RuntimeError('Part budget or finite geometry failure')
    if drift>.08:raise RuntimeError(f'LOD bounds changed {drift:.3f}m; inspect instead of exporting a damaged silhouette')
    return dict(before=before['triangles'],target=target,actual=after['triangles'],maxBoundsDriftM=drift)

def roundtrip(path,before):
    bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(path),use_image_search=False)
    loaded=meshes();after=totals(loaded)
    error=max(abs(after['boundsBlender'][k][i]-before['boundsBlender'][k][i]) for k in ('minimum','maximum') for i in range(3))
    ok=after['triangles']==before['triangles'] and after['materials']==before['materials'] and after['meshCount']==before['meshCount'] and after['nonfinite']==0 and error<.001
    uvok=all(len(o.data.uv_layers)>0 for o in loaded)
    return dict(status='PASS_GEOMETRY' if ok and uvok else 'FAIL_GEOMETRY',boundsErrorM=error,uvPresent=uvok,actual=after,
        importedObjects=[dict(name=o.name,location=list(o.location),rotationEulerDegrees=[math.degrees(a) for a in o.rotation_euler],scale=list(o.scale)) for o in loaded])

def build(args):
    if not args.confirm_orientation or args.yaw_deg is None:raise RuntimeError('Inspect and explicitly confirm yaw before build')
    source=base.source_path(args.part);source_hash=base.sha(source);levels=[];materials=None;core_record=None
    for lod,target in enumerate(LIMITS[args.part]):
        base.guard();obj,raw=base.import_raw(args.part);obj.name=f'MagicStoneCar_{args.part}'
        for material in obj.data.materials:
            if material:material.name=f'{args.part}_Surface'
        base.weld(obj);normalization=fit(obj,args);objects=[obj]
        if args.part=='Cabin':
            bm=bmesh.new();bm.from_mesh(obj.data)
            opening=[f for f in bm.faces if abs(f.calc_center_median().x)<.72 and .63<f.calc_center_median().z<1.72 and f.calc_center_median().y<-.55]
            removed=len(opening)
            bmesh.ops.delete(bm,geom=opening,context='FACES')
            eye_crossing=[f for f in bm.faces if any(abs(v.co.x)<.60 and 1.0<v.co.z<1.66 and v.co.y<-.45 for v in f.verts)]
            if eye_crossing:bmesh.ops.delete(bm,geom=eye_crossing,context='FACES')
            needles=[f for f in bm.faces if f.calc_center_median().y<-.9 and .4<f.calc_center_median().z<1.8 and max(e.calc_length() for e in f.edges)>.25 and f.calc_area()/max(e.calc_length() for e in f.edges)**2<.001]
            needle_count=len(needles)
            if needles:bmesh.ops.delete(bm,geom=needles,context='FACES')
            bm.to_mesh(obj.data);bm.free();obj.data.update()
            normalization['localEdit']={'frontOpeningFacesRemoved':removed,'cutEdgeNeedleFacesRemoved':needle_count,'reason':'Open actual forward seated sightline; preserve side lattice, sill, top beam and outer uprights'}
        if args.part=='Engine':objects,core_record=split_core(obj,args.core_box)
        simplification=reduce_group(objects,target)
        if lod==0:
            materials=base.material_textures(args.part,obj)
            for other in objects[1:]:base.material_textures(args.part,other)
        else:
            # Keep copied texture paths in every editable LOD blend.
            base.material_textures(args.part,obj)
        before=totals(objects);fbx=MODELS/f'MagicStoneCar_{args.part}_LOD{lod}.fbx';export(objects,fbx)
        blend=REPORTS/f'MagicStoneCar_{args.part}_LOD{lod}.blend';blend.parent.mkdir(parents=True,exist_ok=True)
        bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(blend),check_existing=False)
        images=views(objects,f'{args.part}_LOD{lod}') if args.render and lod in (0,2) else []
        record=dict(lod=lod,targetTriangles=target,fbx=str(fbx),fbxSha256=base.sha(fbx),blend=str(blend),normalization=normalization,simplification=simplification,beforeExport=before,images=images)
        record['roundtrip']=roundtrip(fbx,before);levels.append(record)
        write(REPORTS/f'{args.part}_LOD{lod}_roundtrip.json',record)
        if record['roundtrip']['status']!='PASS_GEOMETRY':raise RuntimeError('FBX roundtrip failed: '+str(fbx))
    preserved=base.sha(source)==source_hash
    report=dict(part=args.part,source=str(source),sourceSha256=source_hash,sourcePreserved=preserved,materials=materials,levels=levels,core=core_record,
        status='PASS_GEOMETRY' if preserved else 'FAIL_SOURCE_CHANGED',visualQuality='UNVERIFIED',runtime='UNVERIFIED')
    write(REPORTS/(args.part+'.json'),report)
    print(json.dumps(dict(part=args.part,status=report['status'],lodTriangles=[x['roundtrip']['actual']['triangles'] for x in levels])))

def material(name,color,metal=0):
    m=bpy.data.materials.new(name);m.use_nodes=True;m.diffuse_color=(*color,1)
    bsdf=m.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Base Color'].default_value=(*color,1);bsdf.inputs['Metallic'].default_value=metal;bsdf.inputs['Roughness'].default_value=.55
    return m

def finish_primitive(obj,name,mat):
    obj.name=name;obj.data.materials.append(mat);base.active(obj);bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    return obj

def box(name,centre,size,mat):
    bpy.ops.mesh.primitive_cube_add(size=1,location=vec(centre));o=bpy.context.object;o.scale=(size[0],size[2],size[1]);return finish_primitive(o,name,mat)

def cylinder(name,centre,radius,depth,mat,axis='y',segments=12):
    bpy.ops.mesh.primitive_cylinder_add(vertices=segments,radius=radius,depth=depth,location=vec(centre));o=bpy.context.object
    direction=vec((1,0,0) if axis=='x' else (0,1,0) if axis=='y' else (0,0,1));o.rotation_euler=Vector((0,0,1)).rotation_difference(direction).to_euler()
    return finish_primitive(o,name,mat)

def support(lod):
    brass=material('Support_DarkBrass',(.18,.12,.058),.65);glass=material('MagicStoneLanternGlass',(.09,.28,.29),.1)
    parts=[];segments=[12,8,6][lod]
    for x in (-.68,.68):parts.append(box('Frame_Longitudinal',(x,.69,-.08),(.13,.15,4.30),brass))
    for z in (-1.75,-.4,1.25):parts.append(box('Frame_Crossmember',(0,.69,z),(1.55,.15,.13),brass))
    for x in (-.68,.68):
        for z in (-1.25,1.25):parts.append(box('Frame_Support',(x,.79,z),(.12,.25,.15),brass))
    # Runtime Unity front/rear axle rods remain independently tracked (64 tris); do not duplicate them here.
    # Four actual rigid lanterns, no camera-attached floating lights.
    lamps=[]
    for label,x,z in [('FL',-1.02,.88),('FR',1.02,.88),('RL',-1.02,-1.78),('RR',1.02,-1.78)]:
        centre=(x,2.48,z);lamps.append(dict(id=label,localPosition=dictvec(centre)))
        parts.append(cylinder(f'Lantern{label}_Hanger',(x,2.72,z),.018,.20,brass,'y',segments))
        for y in (2.31,2.65):parts.append(cylinder(f'Lantern{label}_Cap',(x,y,z),.105,.04,brass,'y',segments))
        parts.append(cylinder(f'MagicStoneLanternGlass_{label}',centre,.075,.30,glass,'y',segments))
        for dx,dz in ((.075,.075),(-.075,.075),(.075,-.075),(-.075,-.075)):
            parts.append(box(f'Lantern{label}_Frame',(x+dx,2.48,z+dz),(.017,.34,.017),brass))
    parts.append(cylinder('Steering_Column',(0,1.52,.77),.035,.5,brass,'y',segments))
    bpy.ops.mesh.primitive_torus_add(major_segments=[24,16,12][lod],minor_segments=[6,5,4][lod],location=vec((0,1.78,.77)),major_radius=.23,minor_radius=.02)
    o=bpy.context.object;o.rotation_euler.x=math.radians(30);parts.append(finish_primitive(o,'Steering_Rim',brass))
    parts.append(box('Steering_Crossbar',(0,1.78,.77),(.40,.025,.025),brass))
    # Merge connected rigid pieces while retaining four independently addressable lamps.
    grouped=[]
    def join_members(members,name,pivot):
        select(members);bpy.ops.object.join();obj=bpy.context.object;obj.name=name
        centre=vec(pivot)
        for v in obj.data.vertices:v.co-=centre
        obj.location=centre;obj.data.update();grouped.append(obj)
    buckets=[([o for o in parts if o.name.startswith('Frame_')],'Frame_Rigid',(0,.69,-.08)),
        ([o for o in parts if o.name.startswith('Steering_')],'Steering_Rigid',(0,1.78,.77))]
    glass_objects=[o for o in parts if o.name.startswith('MagicStoneLanternGlass_')]
    for lamp in lamps:
        label=lamp['id'];pos=lamp['localPosition'];buckets.append(([o for o in parts if o.name.startswith('Lantern'+label+'_')],f'Lantern{label}_Frame',(pos['x'],pos['y'],pos['z'])))
    for members,name,pivot in buckets:join_members(members,name,pivot)
    grouped.extend(glass_objects)
    actual=totals(grouped)
    if actual['triangles']>SUPPORT_LIMITS[lod]:raise RuntimeError('Support instance budget exceeded')
    return grouped,lamps

def seated_check(objects,eye):
    results=[];deps=bpy.context.evaluated_depsgraph_get();trees=[(o.name,BVHTree.FromObject(o,deps),o.matrix_world.inverted()) for o in objects]
    origin=vec(eye)
    for pitch in (-10,0,10):
        for yaw in (-20,-10,0,10,20):
            a=math.radians(yaw);b=math.radians(pitch);direction=Vector((math.sin(a)*math.cos(b),-math.cos(a)*math.cos(b),math.sin(b)))
            hits=[]
            for name,tree,inv in trees:
                localdir=(inv.to_3x3()@direction).normalized();hit=tree.ray_cast(inv@origin,localdir,6)
                if hit[0] is not None:hits.append(dict(object=name,distance=float(hit[3])))
            results.append(dict(yaw=yaw,pitch=pitch,clear=not hits,hits=hits))
    return dict(scope='Static rays, no motion or Unity camera comfort validation',eyeUnity=dictvec(eye),clear=sum(r['clear'] for r in results),count=len(results),rays=results)

def assemble(args):
    reports={p:json.loads((REPORTS/(p+'.json')).read_text(encoding='utf-8')) for p in PARTS}
    if any(r['status']!='PASS_GEOMETRY' for r in reports.values()):raise RuntimeError('All four part roundtrips must pass')
    settings=json.loads(Path(args.assembly_config).read_text(encoding='utf-8')) if args.assembly_config else {}
    placements={**PLACEMENTS,**settings.get('placements',{})};seat=settings.get('seat',[0,2.18,.20]);alllevels=[];support_rows=[];lamps=[]
    for lod in range(3):
        base.guard();bpy.ops.wm.read_factory_settings(use_empty=True);objects=[]
        for part in PARTS:
            report=reports[part]['levels'][lod]
            with bpy.data.libraries.load(report['blend'],link=False) as (available,requested):requested.objects=list(available.objects)
            loaded=[o for o in requested.objects if o and o.type=='MESH']
            for o in loaded:bpy.context.scene.collection.objects.link(o)
            if part=='Wheel':
                original=loaded[0]
                for i,(label,x,z) in enumerate(WHEELS):
                    o=original if i==0 else original.copy()
                    if i:o.data=original.data;bpy.context.scene.collection.objects.link(o)
                    o.name=f'Wheel_{label}';o.location=vec((x,.6,z));o.rotation_euler=(0,0,math.pi if x<0 else 0);objects.append(o)
            else:
                for o in loaded:o.location=vec(placements[part]);objects.append(o)
        extra,lamps=support(lod);support_stats=totals(extra);support_fbx=MODELS/f'MagicStoneCar_Support_LOD{lod}.fbx';export(extra,support_fbx)
        support_rows.append(dict(lod=lod,fbx=str(support_fbx),triangles=support_stats['triangles'],materials=support_stats['materials']))
        objects+=extra;actual=totals(objects)
        if actual['triangles']+UNITY_AXLE_TRIANGLES>TOTAL_LIMITS[lod]:raise RuntimeError('Repeated-wheel assembly budget exceeded')
        eye=seated_check(objects,seat) if lod==0 else None
        markers={'SeatSocket':seat,'EngineCoreSocket':settings.get('engineCore',[0,1.22,2.1]),'PatternSocket':settings.get('pattern',[0,1.02,2.11])}
        for name,position in markers.items():
            o=bpy.data.objects.new(name,None);bpy.context.scene.collection.objects.link(o);o.location=vec(position)
        blend=REPORTS/f'MagicStoneCar_Assembly_LOD{lod}.blend';bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(blend),check_existing=False)
        images=views(objects,f'Assembly_LOD{lod}') if args.render and lod in (0,2) else []
        alllevels.append(dict(lod=lod,actual=actual,withRuntimeAxleTriangles=actual['triangles']+UNITY_AXLE_TRIANGLES,blend=str(blend),views=images,seatRays=eye))
        support_rows[-1]['roundtrip']=roundtrip(support_fbx,support_stats)
        if support_rows[-1]['roundtrip']['status']!='PASS_GEOMETRY':raise RuntimeError('Support FBX roundtrip failed')
    manifest=dict(schemaVersion=1,coordinateConvention='Unity +Y up,+Z front,scale1; wheel axle X. Preserve imported FBX child transforms below placement anchors.',
        parts=[dict(part=p,localPosition=dictvec(pos),localEuler=dictvec((0,0,0))) for p,pos in placements.items()]+[dict(part=p,localPosition=dictvec((0,0,0)),localEuler=dictvec((0,0,0))) for p in ('Wheel','Support')],
        seatLocalPosition=dictvec(seat),seatLocalEuler=dictvec(settings.get('seatEuler',[12,0,0])),engineCoreLocalPosition=dictvec(settings.get('engineCore',[0,1.22,2.1])),patternLocalPosition=dictvec(settings.get('pattern',[0,1.02,2.11])),
        ventLocalPositions=[dictvec(v) for v in settings.get('vents',[[-.55,1.55,1.45],[.55,1.55,1.45]])],lanterns=lamps,
        wheelColliderCenters=[dict(id=n,localPosition=dictvec((x,.74,z))) for n,x,z in WHEELS],wheelRadius=.6,
        leftWheelVisualEuler=dictvec((0,180,0)),rightWheelVisualEuler=dictvec((0,0,0)),actualBounds=alllevels[0]['actual']['actualBounds'],
        lodTriangles=[r['withRuntimeAxleTriangles'] for r in alllevels],blenderOnlyLodTriangles=[r['actual']['triangles'] for r in alllevels],runtimeAxleTriangles=UNITY_AXLE_TRIANGLES,support=support_rows,scope='Static Blender assembly; no Unity drive, physics, boarding or image quality pass')
    write(ASSET/'SocketManifest.json',manifest);write(REPORTS/'Assembly.json',dict(manifest=manifest,levels=alllevels))
    print(json.dumps(dict(status='ASSEMBLED_FOR_REVIEW',lodTriangles=manifest['lodTriangles'],seatRays=alllevels[0]['seatRays']['clear'])))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--stage',choices=['inspect','build','assemble'],required=True);parser.add_argument('--part',choices=PARTS)
    parser.add_argument('--memory-permit',action='store_true',help='Only set after root has explicitly allowed this Blender process')
    parser.add_argument('--confirm-orientation',action='store_true');parser.add_argument('--yaw-deg',type=float)
    parser.add_argument('--outer-vector',type=float,nargs=3,default=[0,-1,0]);parser.add_argument('--size',type=float,nargs=3);parser.add_argument('--fit-mode',choices=['uniform','axis'],default='uniform')
    parser.add_argument('--core-box',type=float,nargs=6);parser.add_argument('--assembly-config');parser.add_argument('--render',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    if not args.memory_permit:parser.error('Root memory permission is required; no Blender work started')
    if args.stage!='assemble' and not args.part:parser.error('--part required')
    base.guard();REPORTS.mkdir(parents=True,exist_ok=True)
    {'inspect':inspect,'build':build,'assemble':assemble}[args.stage](args)
