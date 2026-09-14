"""Inspect, fit and package the three authorized Meshy palanquin parts.

Run one part in one hidden Blender process. This tool never submits Meshy jobs.
Inspect renders must be read before --confirm-orientation is supplied to build.
Unity contract: +Z forward, +Y up. Blender working equivalent: -Y forward, +Z up.
Cabin/Roof origins are bottom-centre; Wheel origin is the X axle centre.
"""
from pathlib import Path
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
import math
import sys

import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[3]
ART = ROOT / "Art/World/WorldMacro/Palanquin"
ASSET = ROOT / "Oheangbu/Assets/_Project/Art/World/WorldMacro/Palanquin"
MODELS = ASSET / "Models"
TEXTURES = ASSET / "Textures"
REPORTS = ART / "Blender"
LIMITS = {"Cabin": 18000, "Roof": 8000, "Wheel": 3000}
FOOTPRINTS = {"Cabin": (1.8, 3.0), "Roof": (2.3, 3.4)}


def commit_ratio():
    class Info(ctypes.Structure):
        _fields_ = [("cb", wintypes.DWORD)] + [(x, ctypes.c_size_t) for x in (
            "CommitTotal", "CommitLimit", "CommitPeak", "PhysicalTotal", "PhysicalAvailable",
            "SystemCache", "KernelTotal", "KernelPaged", "KernelNonpaged", "PageSize")] + [
            (x, wintypes.DWORD) for x in ("HandleCount", "ProcessCount", "ThreadCount")]
    data = Info(); data.cb = ctypes.sizeof(data)
    if not ctypes.windll.psapi.GetPerformanceInfo(ctypes.byref(data), data.cb):
        raise RuntimeError("Cannot measure system commit; refusing Blender production")
    return data.CommitTotal / data.CommitLimit


def guard():
    value = commit_ratio()
    if value >= .85: raise RuntimeError(f"System commit {value:.2%} >=85%; no new work started")
    return value


def sha(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""): h.update(block)
    return h.hexdigest()


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def unity(vector):
    return {"x": float(vector[0]), "y": float(vector[2]), "z": float(-vector[1])}


def bbox(obj):
    bpy.context.view_layer.update()
    points = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    low = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    high = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    return low, high


def stats(obj):
    obj.data.calc_loop_triangles(); low, high = bbox(obj)
    return {"vertices": len(obj.data.vertices), "triangles": len(obj.data.loop_triangles),
            "size_unity_m": {"x": high.x-low.x, "y": high.z-low.z, "z": high.y-low.y},
            "bounds_blender": {"minimum": list(low), "maximum": list(high)},
            "uv_layers": [uv.name for uv in obj.data.uv_layers],
            "materials": [m.name if m else None for m in obj.data.materials],
            "nonfinite_vertices": sum(not all(math.isfinite(c) for c in v.co) for v in obj.data.vertices)}


def active(obj):
    bpy.ops.object.select_all(action="DESELECT"); obj.hide_set(False); obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def source_path(part):
    path = ART / "Meshy" / part / "image/model_urls_glb.glb"
    if not path.exists(): raise FileNotFoundError(f"Wait for the existing Meshy task download: {path}")
    return path


def import_raw(part):
    source = source_path(part)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1
    bpy.ops.import_scene.gltf(filepath=str(source))
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes: raise RuntimeError("GLB contained no mesh")
    for obj in meshes:
        world = obj.matrix_world.copy(); obj.parent = None; obj.matrix_world = world
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes: obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1: bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.name = "Palanquin_" + part
    return obj, {"part": part, "source": str(source), "source_sha256": sha(source),
                 "raw": stats(obj), "runtime_verification": "UNVERIFIED"}


def make_camera(scene):
    data = bpy.data.cameras.new("InspectionCamera"); camera = bpy.data.objects.new("InspectionCamera", data)
    scene.collection.objects.link(camera); scene.camera = camera
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"; scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.show_shadows = True; scene.display.shading.show_cavity = True
    scene.display.shading.background_type = "WORLD"
    if scene.world is None: scene.world = bpy.data.worlds.new("InspectionWorld")
    scene.world.color = (.32, .34, .36)
    scene.render.resolution_x = 1920; scene.render.resolution_y = 1080
    scene.render.resolution_percentage = 100; scene.render.image_settings.file_format = "PNG"
    return camera


def inspect_views(obj, prefix):
    scene = bpy.context.scene; camera = make_camera(scene)
    low, high = bbox(obj); centre = (low + high) * .5; size = high-low
    distance = max(size.length * 2, 1)
    views = {"negative_y": (0, -1, .04), "positive_y": (0, 1, .04),
             "positive_x": (1, 0, .04), "top": (0, 0, 1)}
    if prefix.startswith("Roof"):
        views={key:views[key] for key in ("negative_y","top")}
    elif prefix.startswith("Wheel"):
        views={key:views[key] for key in ("negative_y","positive_x")}
    files = []
    for label, direction in views.items():
        guard()
        camera.location = centre + Vector(direction).normalized() * distance
        camera.rotation_euler = (centre-camera.location).to_track_quat("-Z", "Y").to_euler()
        camera.data.type = "ORTHO"
        # The vertical fit includes horizontal coverage at 16:9.
        camera.data.ortho_scale = max(size.x, size.y, size.z) * 2.05
        path = REPORTS / "Images" / f"{prefix}_{label}.png"; path.parent.mkdir(parents=True, exist_ok=True)
        scene.render.filepath = str(path); bpy.ops.render.render(write_still=True); files.append(str(path))
    return files


def weld(obj):
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.00001)
    bmesh.ops.dissolve_degenerate(bm, edges=list(bm.edges), dist=.000001)
    bm.to_mesh(obj.data); bm.free(); obj.data.update()


def centre_bottom(obj):
    low, high = bbox(obj); offset = Vector(((low.x+high.x)*.5, (low.y+high.y)*.5, low.z))
    for vertex in obj.data.vertices: vertex.co -= offset
    obj.data.update(); bpy.context.view_layer.update()


def normalize_part(obj, part, yaw):
    obj.rotation_mode="XYZ";obj.rotation_euler=(0,0,math.radians(yaw)); active(obj)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    centre_bottom(obj)
    low, high = bbox(obj); size = high-low
    width, length = FOOTPRINTS[part]
    scale = min(width/max(size.x,.0001), length/max(size.y,.0001))
    obj.scale = (scale,scale,scale); bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    transverse_fit=1.0
    if part=="Roof":
        low,high=bbox(obj);transverse_fit=width/max(high.x-low.x,.0001)
        for vertex in obj.data.vertices:vertex.co.x*=transverse_fit
        obj.data.update()
    centre_bottom(obj)
    return {"yaw_blender_degrees": yaw, "uniform_scale": scale,
            "height_not_forced": True, "footprint_limit_unity_m": [width,length],
            "roof_transverse_fit":transverse_fit,
            "fit_note":"Roof width fitted locally to cover the cabin; original roof length and height proportions preserved" if part=="Roof" else "Uniform footprint fit"}


def normalize_wheel(obj):
    coordinates = np.array([tuple(v.co) for v in obj.data.vertices], dtype=np.float64)
    cov = np.cov(coordinates.T); values, vectors = np.linalg.eigh(cov)
    axle = Vector(vectors[:, int(np.argmin(values))])
    # Raw inspection confirms a decorated hub on -Y; both faces carry jade.
    # Map that inspected outer face to +X for an unambiguous mounting contract.
    if axle.dot(Vector((0,-1,0))) < 0: axle.negate()
    rotation = axle.rotation_difference(Vector((1,0,0)))
    for vertex in obj.data.vertices: vertex.co = rotation @ vertex.co
    obj.data.update(); low, high = bbox(obj); centre=(low+high)*.5
    for vertex in obj.data.vertices: vertex.co -= centre
    obj.data.update(); low, high=bbox(obj); size=high-low
    original_span=list(size)
    sy=1.2/max(size.y,.0001); sz=1.2/max(size.z,.0001)
    sx=min((sy+sz)*.5,.26/max(size.x,.0001))
    for vertex in obj.data.vertices:
        vertex.co.x*=sx; vertex.co.y*=sy; vertex.co.z*=sz
    # Fit the outer rim to a circular radial envelope. Keep hub/spoke relief and UVs.
    bins=96; envelope=np.zeros(bins,dtype=np.float64)
    for vertex in obj.data.vertices:
        theta=math.atan2(vertex.co.z,vertex.co.y)%(2*math.pi)
        index=int(theta/(2*math.pi)*bins)%bins
        envelope[index]=max(envelope[index],math.hypot(vertex.co.y,vertex.co.z))
    occupied=np.flatnonzero(envelope>.3)
    if len(occupied)<bins*.65: raise RuntimeError("Wheel has no continuous rim; inspect instead of assuming a circular wheel")
    for i in range(bins):
        if envelope[i]>.3: continue
        j=min(occupied,key=lambda n:min(abs(int(n)-i),bins-abs(int(n)-i))); envelope[i]=envelope[j]
    raw_spread=float(envelope.max()-envelope.min())
    for vertex in obj.data.vertices:
        radius=math.hypot(vertex.co.y,vertex.co.z)
        if radius<1e-7: continue
        theta=math.atan2(vertex.co.z,vertex.co.y)%(2*math.pi)
        fi=theta/(2*math.pi)*bins; i=int(fi)%bins
        local=float(envelope[i]*(1-(fi-int(fi)))+envelope[(i+1)%bins]*(fi-int(fi)))
        strength=min(1,max(0,(radius-.24)/.18)); strength=strength*strength*(3-2*strength)
        target=radius*((1-strength)+strength*.6/max(local,.0001))
        target=min(target,.6); vertex.co.y*=target/radius;vertex.co.z*=target/radius
    obj.data.update(); bpy.context.view_layer.update()
    return {"axle":"Unity X / Blender X", "source_pca_axle":list(axle),
            "decorated_outer_face":"Unity +X (raw inspected -Y); both raw hub faces carry jade",
            "left_visual_rotation_unity_degrees":[0,180,0],
            "right_visual_rotation_unity_degrees":[0,0,0],
            "source_axis_eigenvalues":values.tolist(),"source_aligned_span":original_span,
            "rim_pre_correction_spread_m":raw_spread,"target_radius_m":.6,"axial_width_max_m":.26}


def reduce(obj, target):
    original=stats(obj); active(obj)
    if original["triangles"]>target:
        mod=obj.modifiers.new("Palanquin budget", "DECIMATE");mod.decimate_type="COLLAPSE"
        mod.ratio=target*.992/original["triangles"];mod.use_collapse_triangulate=True
        bpy.ops.object.modifier_apply(modifier=mod.name)
    current=stats(obj)
    if current["nonfinite_vertices"]: raise RuntimeError("Invalid vertex after simplification")
    for axis in ("x","y","z"):
        if abs(current["size_unity_m"][axis]-original["size_unity_m"][axis])>.06:
            raise RuntimeError("Simplification changed bounds by >6cm; do not export deformed part")
    return {"requested_triangles":target,"before":original["triangles"],"after":current["triangles"]}


def seated_rays(obj, world_eye=2.18):
    bpy.context.view_layer.update();tree=BVHTree.FromObject(obj,bpy.context.evaluated_depsgraph_get())
    eye=Vector((0,-.65,world_eye-1));results=[]
    for pitch in (-10,0,10):
        for yaw in (-20,-10,0,10,20):
            a=math.radians(yaw);b=math.radians(pitch)
            direction=Vector((math.sin(a)*math.cos(b),-math.cos(a)*math.cos(b),math.sin(b)))
            location,normal,index,distance=tree.ray_cast(eye,direction,5)
            results.append({"yaw":yaw,"pitch":pitch,"clear":location is None,
                            "distance_m":distance,"hit_unity_part":unity(location) if location else None,
                            "face":index})
    low,high=bbox(obj);inside=all(low[i]<eye[i]<high[i] for i in range(3))
    return {"scope":"Static final mesh rays from seated camera; not Unity runtime visibility",
            "eye_inside_cabin_envelope":inside,
            "eye_root_unity":{ "x":0,"y":world_eye,"z":.65},"clear":sum(x["clear"] for x in results),
            "total":len(results),"rays":results}


def remove_front_film(obj, eye_height):
    low,high=bbox(obj);size=high-low;front=-low.y
    bm=bmesh.new();bm.from_mesh(obj.data);victims=[]
    for face in bm.faces:
        c=face.calc_center_median();u=unity(c)
        if (u["z"]>front-size.y*.16 and abs(u["x"])<size.x*.32
            and eye_height-1-.35<u["y"]<eye_height-1+.35 and abs(face.normal.y)>.78
            and all(abs(v.co.x)<size.x*.44 for v in face.verts)):
            victims.append(face)
    count=len(victims)
    if victims:bmesh.ops.delete(bm,geom=victims,context="FACES")
    bm.to_mesh(obj.data);bm.free();obj.data.update()
    return {"removed_faces":count,"scope":"Only confirmed front film in seated sight window; posts, side panels, floor and upper beam excluded"}


def material_textures(part,obj):
    TEXTURES.mkdir(parents=True,exist_ok=True);image_paths={};records=[]
    def image_path(image):
        if image.name in image_paths:return image_paths[image.name]
        safe="".join(c if c.isalnum() or c in "_-" else "_" for c in image.name)
        packed=image.packed_file
        data=bytes(packed.data) if packed else None
        ext=".jpg" if data and data[:2]==b"\xff\xd8" else ".png"
        path=TEXTURES/(part+"_"+safe+ext)
        if data:path.write_bytes(data)
        else:
            image.filepath_raw=str(path);image.file_format="PNG";image.save()
        image.filepath=str(path)
        record={"asset_path":"Assets/"+path.relative_to(ROOT/"Oheangbu/Assets").as_posix(),"sha256":sha(path),"color_space":image.colorspace_settings.name}
        image_paths[image.name]=record;return record
    def find_texture(socket,visited=None):
        visited=set() if visited is None else visited
        for link in socket.links:
            node=link.from_node
            if node.as_pointer() in visited:continue
            visited.add(node.as_pointer())
            if node.type=="TEX_IMAGE" and node.image:return image_path(node.image)
            for input_socket in node.inputs:
                found=find_texture(input_socket,visited)
                if found:return found
        return None
    for material in obj.data.materials:
        record={"name":material.name,"textures":{}}
        bsdf=next((n for n in material.node_tree.nodes if n.type=="BSDF_PRINCIPLED"),None) if material.use_nodes else None
        if bsdf:
            for name in ("Base Color","Metallic","Roughness","Normal","Alpha"):
                socket=bsdf.inputs.get(name)
                if socket:
                    texture=find_texture(socket)
                    if texture:record["textures"][name]=texture
                    elif isinstance(socket.default_value,(float,int)):record[name]=socket.default_value
        records.append(record)
    return records


def export_fbx(obj,path):
    active(obj);path.parent.mkdir(parents=True,exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={"MESH"},
        global_scale=1,apply_unit_scale=True,apply_scale_options="FBX_SCALE_UNITS",axis_forward="-Z",axis_up="Y",
        bake_space_transform=False,use_mesh_modifiers=True,mesh_smooth_type="OFF",use_triangles=True,
        add_leaf_bones=False,bake_anim=False,path_mode="RELATIVE",embed_textures=False)


def texture_manifest():
    entries=[]
    for part in ("Cabin","Roof","Wheel"):
        path=REPORTS/(part+".json")
        if not path.exists():continue
        report=json.loads(path.read_text(encoding="utf-8"))
        for material in report.get("materials",[]):
            maps=material["textures"]
            entries.append({"part":part,"material":material["name"],
                "baseColor":maps.get("Base Color",{}).get("asset_path",""),
                "normal":maps.get("Normal",{}).get("asset_path",""),
                "metallicRoughness":maps.get("Metallic",{}).get("asset_path",""),
                "baseColorSpace":"sRGB","normalColorSpace":"Non-Color",
                "metallicRoughnessColorSpace":"Non-Color",
                "metallicChannel":"B","roughnessChannel":"G",
                "note":"Original glTF packed maps. A shader using albedo/normal only does not apply the packed metallic/roughness map."})
    write_json(ASSET/"TextureManifest.json",{"entries":entries})


def build(args):
    guard();obj,report=import_raw(args.part);report["stage"]="build"
    if not args.confirm_orientation:raise RuntimeError("Read raw four-view renders and explicitly confirm orientation before build")
    for material in obj.data.materials:
        if material:material.name=args.part+"_"+material.name
    weld(obj)
    report["normalization"]=normalize_wheel(obj) if args.part=="Wheel" else normalize_part(obj,args.part,args.yaw_deg)
    if args.part=="Cabin":
        report["seat_before"]=seated_rays(obj,args.seat_y)
        if args.remove_front_film:report["front_film_edit"]=remove_front_film(obj,args.seat_y)
    report["decimation"]=reduce(obj,LIMITS[args.part])
    if args.part=="Wheel":
        # Collapse can move its new vertices a few millimetres outside the rim.
        low,high=bbox(obj);axial_scale=min(1,.26/max(high.x-low.x,.0001))
        for vertex in obj.data.vertices:
            vertex.co.x*=axial_scale
            radius=math.hypot(vertex.co.y,vertex.co.z)
            if radius>.6:vertex.co.y*=.6/radius;vertex.co.z*=.6/radius
        obj.data.update();bpy.context.view_layer.update()
    report["final"]=stats(obj)
    if args.part=="Wheel":
        radii=[math.hypot(v.co.y,v.co.z) for v in obj.data.vertices]
        report["wheel_radius_max_m"]=max(radii);report["wheel_axle_width_m"]=report["final"]["size_unity_m"]["x"]
    if args.part=="Cabin":report["seat_after"]=seated_rays(obj,args.seat_y)
    report["materials"]=material_textures(args.part,obj)
    path=MODELS/(obj.name+".fbx");export_fbx(obj,path)
    report["fbx"]=str(path);report["fbx_sha256"]=sha(path)
    blend=REPORTS/(obj.name+".blend");blend.parent.mkdir(parents=True,exist_ok=True)
    bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(blend),check_existing=False);report["blend"]=str(blend)
    report["views"]=inspect_views(obj,args.part+"_fitted") if args.render else []
    before=report["final"]
    bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(path),use_image_search=False)
    loaded=[o for o in bpy.context.scene.objects if o.type=="MESH"]
    if len(loaded)!=1:raise RuntimeError("Expected one final mesh per independently movable part")
    after=stats(loaded[0]);report["roundtrip"]=after
    error=max(abs(after["bounds_blender"][key][i]-before["bounds_blender"][key][i]) for key in ("minimum","maximum") for i in range(3))
    report["roundtrip_bounds_error_m"]=error
    report["roundtrip_materials_preserved"]=before["materials"]==after["materials"]
    report["roundtrip_uv_layers_preserved"]=before["uv_layers"]==after["uv_layers"]
    report["source_preserved"]=sha(report["source"])==report["source_sha256"]
    report["status"]="PASS_GEOMETRY" if error<.01 and after["triangles"]<=LIMITS[args.part] and after["uv_layers"] and report["source_preserved"] and report["roundtrip_materials_preserved"] and report["roundtrip_uv_layers_preserved"] else "FAIL_GEOMETRY"
    write_json(REPORTS/(args.part+".json"),report);texture_manifest();print(json.dumps(report,ensure_ascii=False),flush=True)


def inspect(args):
    guard();obj,report=import_raw(args.part);report["stage"]="inspect"
    write_json(REPORTS/(args.part+"_raw.json"),report)
    report["views"]=inspect_views(obj,args.part+"_raw")
    write_json(REPORTS/(args.part+"_raw.json"),report);print(json.dumps(report,ensure_ascii=False),flush=True)


def assemble(args):
    guard();bpy.ops.wm.read_factory_settings(use_empty=True);parts={};reports={}
    for part in ("Cabin","Roof","Wheel"):
        report=json.loads((REPORTS/(part+".json")).read_text(encoding="utf-8"))
        if report["status"]!="PASS_GEOMETRY":raise RuntimeError("Part not verified: "+part)
        with bpy.data.libraries.load(report["blend"],link=False) as (available,requested):
            requested.objects=[name for name in available.objects if name=="Palanquin_"+part]
        obj=requested.objects[0];bpy.context.scene.collection.objects.link(obj);parts[part]=obj;reports[part]=report
    cabin=parts["Cabin"];cabin.location=(0,0,1);bpy.context.view_layer.update()
    cabin_top=bbox(cabin)[1].z;parts["Roof"].location=(0,0,cabin_top-.035)
    wheel=parts["Wheel"]
    for i,(x,z,label) in enumerate(((-1.075,1.25,"FL"),(1.075,1.25,"FR"),(-1.075,-1.25,"RL"),(1.075,-1.25,"RR"))):
        obj=wheel if i==0 else wheel.copy()
        if i:obj.data=wheel.data;bpy.context.scene.collection.objects.link(obj)
        obj.name="Wheel_"+label;obj.location=(x,-z,.6)
        obj.rotation_mode="XYZ";obj.rotation_euler=(0,0,math.pi if x<0 else 0)
    seat=Vector((0,-.65,args.seat_y));socket=bpy.data.objects.new("SeatSocket",None);bpy.context.scene.collection.objects.link(socket);socket.location=seat
    manifest={"overrideSeat":True,"seatLocalPosition":unity(seat),"seatLocalEuler":{"x":0,"y":0,"z":0},
              "exitLocalPositions":[{"x":2.05,"y":0,"z":0},{"x":-2.05,"y":0,"z":0},{"x":0,"y":0,"z":-2.85}],
              "coordinateConvention":"Unity +Z forward, +Y up; wheel axle local X; root at ground centre",
              "cabinBottom":1.0,"roofBottom":cabin_top-.035,"wheelRadius":.6,
              "leftWheelVisualEuler":{"x":0,"y":180,"z":0},
              "rightWheelVisualEuler":{"x":0,"y":0,"z":0},
              "actualCombinedTriangles":reports["Cabin"]["roundtrip"]["triangles"]+reports["Roof"]["roundtrip"]["triangles"]+4*reports["Wheel"]["roundtrip"]["triangles"],
              "parts":{key:{"fbx":value["fbx"],"size":value["roundtrip"]["size_unity_m"]} for key,value in reports.items()},
              "verification":"Static authoring assembly only; suspension, acceleration, camera and terrain motion remain Unity tests"}
    if manifest["actualCombinedTriangles"]>40000:raise RuntimeError("Combined repeated-wheel budget exceeds 40,000 triangles")
    bpy.ops.file.pack_all();blend=REPORTS/"Palanquin_Assembly.blend";bpy.ops.wm.save_as_mainfile(filepath=str(blend),check_existing=False)
    write_json(ASSET/"SocketManifest.json",manifest);write_json(REPORTS/"Assembly.json",manifest)
    texture_manifest()
    print(json.dumps(manifest,ensure_ascii=False),flush=True)


if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--stage",choices=("inspect","build","assemble"),required=True)
    parser.add_argument("--part",choices=LIMITS);parser.add_argument("--yaw-deg",type=float,default=0)
    parser.add_argument("--confirm-orientation",action="store_true");parser.add_argument("--remove-front-film",action="store_true")
    parser.add_argument("--seat-y",type=float,default=2.18);parser.add_argument("--render",action="store_true")
    arguments=sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    options=parser.parse_args(arguments)
    if options.stage!="assemble" and not options.part:parser.error("--part is required for inspect/build")
    {"inspect":inspect,"build":build,"assemble":assemble}[options.stage](options)
