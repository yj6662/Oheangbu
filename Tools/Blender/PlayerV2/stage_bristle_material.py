"""Stage a material-only dielectric correction; never overwrite the source asset.

Run with background Blender. The Unity manifest fragment deliberately retains the
existing matched base/normal maps and omits metallic/roughness maps for hair only.
"""
import bpy
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
ART = ROOT / 'Art/PlayerV2'
SOURCE = ART / 'DosaBrushV2.blend'
OUT = ART / 'MaterialCandidates/Brush'
OUT.mkdir(parents=True, exist_ok=True)
TARGET = OUT / 'DosaBrushV2_BristlesDielectric.blend'

def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()

def matrix(m):
    return [float(x) for row in m for x in row]

def graph(material):
    tree = material.node_tree
    return {
        'name': material.name,
        'nodes': [{
            'name': n.name, 'type': n.bl_idname,
            'image': n.image.name if n.type == 'TEX_IMAGE' and n.image else None,
            'defaults': {s.identifier: list(s.default_value) if hasattr(s.default_value, '__len__') and not isinstance(s.default_value, str) else s.default_value
                         for s in n.inputs if hasattr(s, 'default_value')},
        } for n in tree.nodes],
        'links': [(l.from_node.name, l.from_socket.identifier, l.to_node.name, l.to_socket.identifier) for l in tree.links],
    }

def invariants():
    result = {}
    for ob in bpy.data.objects:
        item = {'type': ob.type, 'matrix': matrix(ob.matrix_local),
                'parent': ob.parent.name if ob.parent else None,
                'parent_type': ob.parent_type, 'parent_bone': ob.parent_bone}
        if ob.type == 'MESH':
            me = ob.data
            item.update({
                'vertices': [list(v.co) for v in me.vertices],
                'polygons': [list(p.vertices) for p in me.polygons],
                'smooth': [p.use_smooth for p in me.polygons],
                'material_indices': [p.material_index for p in me.polygons],
                'uv': {uv.name: [list(v.uv) for v in uv.data] for uv in me.uv_layers},
                'groups': [g.name for g in ob.vertex_groups],
                'weights': [[(g.group, g.weight) for g in v.groups] for v in me.vertices],
                'shape_keys': {k.name: {'value': k.value, 'coordinates': [list(v.co) for v in k.data]}
                               for k in me.shape_keys.key_blocks} if me.shape_keys else {},
                'normals': [list(n.vector) for n in me.corner_normals],
                'modifiers': [(m.name, m.type, m.object.name if m.type == 'ARMATURE' and m.object else None)
                              for m in ob.modifiers],
            })
        elif ob.type == 'ARMATURE':
            item['bones'] = {b.name: {'parent': b.parent.name if b.parent else None,
                                      'matrix': matrix(b.matrix_local), 'length': b.length,
                                      'pose': matrix(ob.pose.bones[b.name].matrix_basis)} for b in ob.data.bones}
        result[ob.name] = digest(item)
    result['handle_material'] = digest(graph(bpy.data.objects['DosaBrushV2_Handle'].data.materials[0]))
    result['images'] = digest([(i.name, i.filepath, list(i.size), i.colorspace_settings.name,
                                hashlib.sha256(bytes(i.packed_file.data)).hexdigest() if i.packed_file else None)
                               for i in bpy.data.images])
    return result

source_hash = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
before = invariants()
hair = bpy.data.objects['DosaBrushV2_Bristles']
source_material = hair.data.materials[0]
material = source_material.copy()
material.name = 'M_DosaBrushV2_Bristles'
bsdf = next(n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
for key, value in [('Metallic', 0.0), ('Roughness', 0.85)]:
    for link in list(bsdf.inputs[key].links):
        material.node_tree.links.remove(link)
    bsdf.inputs[key].default_value = value
hair.data.materials[0] = material
after = invariants()
assert before == after, 'A non-material invariant changed.'
bpy.ops.wm.save_as_mainfile(filepath=str(TARGET), check_existing=False)
bpy.ops.wm.open_mainfile(filepath=str(TARGET))
roundtrip = invariants()
assert before == roundtrip, 'Material-only save/load changed a structural invariant.'
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest() == source_hash, 'Source was modified.'

entry = {
    'name': 'DosaBrushV2_Bristles',
    'baseColor': 'Brush/Textures/T_DosaBrushV2_BaseColor.png',
    'normal': 'Brush/Textures/T_DosaBrushV2_Normal.png',
    'metallicFallback': 0.0, 'roughnessFallback': 0.85,
    'baseTint': {'r': 1.0, 'g': 1.0, 'b': 1.0, 'a': 1.0},
    'doubleSided': True,
}
fragment = {'materials': [entry],
            'rendererMaterials': [{'renderer': 'DosaBrushV2_Bristles', 'materials': ['DosaBrushV2_Bristles']}]}
fragment_path = OUT / 'texture-manifest.fragment.json'
fragment_path.write_text(json.dumps(fragment, indent=2), encoding='utf-8')
report = {
    'source': str(SOURCE), 'sourceSha256': source_hash,
    'candidate': str(TARGET), 'candidateSha256': hashlib.sha256(TARGET.read_bytes()).hexdigest(),
    'fragment': str(fragment_path), 'fragmentSha256': hashlib.sha256(fragment_path.read_bytes()).hexdigest(),
    'onlyMutation': 'Bristle renderer material copy: unlink metallic and roughness; set 0 and 0.85. Base color and normal links unchanged.',
    'invariantsBefore': before, 'invariantsAfterSaveReload': roundtrip,
    'geometryWeightsShapesBonesSocketsImagesHandleMaterialPreserved': before == roundtrip,
    'sourceUnchanged': hashlib.sha256(SOURCE.read_bytes()).hexdigest() == source_hash,
    'productionAssetEdited': False, 'unityCandidateRenderVerified': False,
    'candidateScope': 'MATERIAL_CORRECTION_ONLY; original bristle geometry still has coarse pointed blades visible in gray diffuse diagnostic.',
    'integration': 'Append materials entry to canonical manifest and replace only existing bristle rendererMaterials entry. Do not replace the whole manifest. Existing handle entry stays unchanged. ImportMaterials/rebind is sufficient; no mesh or FBX re-export required.',
}
(OUT / 'material-only-handoff.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report))
