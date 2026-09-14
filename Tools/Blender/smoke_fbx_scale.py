# SPEC-WORLD-MAP §8 P0 chore — Blender 헤드리스 스모크: 1 m 큐브를 랜드마크 계약 FBX 옵션으로 내보내
# Unity 반입 시 루트 scale·fileScale 실측의 원본을 만든다(§13 기록란). 결과 FBX = Tools/Blender/_smoke/smoke_cube_1m.fbx (gitignore).
# 실행: "<blender.exe>" --background --python Tools/Blender/smoke_fbx_scale.py -- --out <dir>
import bpy, sys, os, json

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out_dir = argv[argv.index("--out") + 1] if "--out" in argv else os.path.join(os.path.dirname(os.path.abspath(__file__)), "_smoke")
os.makedirs(out_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0

bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.0, 0.0, 0.5))
cube = bpy.context.active_object
cube.name = "LM_SmokeCube"
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

fbx_path = os.path.join(out_dir, "smoke_cube_1m.fbx")
bpy.ops.export_scene.fbx(
    filepath=fbx_path,
    use_selection=False,
    axis_forward='-Z', axis_up='Y',
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
    bake_space_transform=True, use_mesh_modifiers=True,
    mesh_smooth_type='OFF', path_mode='STRIP',
    object_types={'MESH'}, add_leaf_bones=False,
)

report = {
    "blender": bpy.app.version_string,
    "unit_system": scene.unit_settings.system,
    "scale_length": scene.unit_settings.scale_length,
    "object": cube.name,
    "dimensions_m": [round(v, 4) for v in cube.dimensions],
    "fbx": fbx_path,
    "fbx_bytes": os.path.getsize(fbx_path),
    "fbx_options": {"axis_forward": "-Z", "axis_up": "Y", "apply_scale_options": "FBX_SCALE_UNITS", "bake_space_transform": True},
}
with open(os.path.join(out_dir, "smoke_report.json"), "w", encoding="utf-8") as f:
    json.dump(report, f, ensure_ascii=False, indent=1)
print("SMOKE_OK " + json.dumps(report))
