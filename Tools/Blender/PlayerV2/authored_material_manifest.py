"""Append missing directly authored Principled materials without exporting geometry.

Blender CLI: --background --factory-startup --python this.py -- --blend FILE
Existing manifest definitions and renderer slots are preserved verbatim.
"""
import argparse
import hashlib
import json
import math
import re
import sys
from pathlib import Path
import bpy

ROOT = Path('C:/Users/yj666/Oheangbu')
STAGING = ROOT / 'Art/PlayerV2/Staging'
ALIASES = {'Material_0': 'DosaV2_Source'}


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def _socket_source(socket):
    if not socket.is_linked:
        return None
    if len(socket.links) != 1:
        raise ValueError('Multiple links cannot be represented: ' + socket.name)
    source = socket.links[0].from_socket
    while source.node.type == 'REROUTE':
        source = source.node.inputs[0].links[0].from_socket
    return source


def _image_channel(socket, normal=False):
    source = _socket_source(socket)
    if source is None:
        return None, 0
    channel = 0
    if normal:
        if source.node.type != 'NORMAL_MAP' or source.node.space != 'TANGENT':
            raise ValueError('Only tangent Normal Map -> image is supported: ' + socket.name)
        if source.node.inputs['Strength'].is_linked:
            raise ValueError('Linked normal strength cannot be represented by a scalar.')
        source = _socket_source(source.node.inputs['Color'])
    if source and source.node.type in ('SEPARATE_COLOR', 'SEPRGB'):
        channel = {'Red': 0, 'Green': 1, 'Blue': 2, 'R': 0, 'G': 1, 'B': 2}.get(source.name)
        if channel is None:
            raise ValueError('Unsupported image channel: ' + source.name)
        source = _socket_source(source.node.inputs[0])
    if source is None or source.node.type != 'TEX_IMAGE' or source.node.image is None:
        raise ValueError('Unsupported linked material graph: ' + socket.name)
    if source.name == 'Alpha':
        channel = 3
    return source.node.image, channel


def _copy_image(image, stage, material, channel, copied):
    original = Path(bpy.path.abspath(image.filepath))
    packed = image.packed_file
    if original.is_file():
        data = original.read_bytes()
        suffix = original.suffix.lower()
    elif packed:
        data = bytes(packed.data)
        suffix = Path(image.filepath).suffix.lower() or '.png'
    else:
        raise ValueError('Texture has no readable or packed file: ' + image.name)
    if suffix not in ('.png', '.jpg', '.jpeg', '.tga', '.exr', '.tif', '.tiff'):
        raise ValueError('Unsupported texture file extension: ' + suffix)
    safe = re.sub(r'[^a-zA-Z0-9_\-]', '_', material)
    destination = stage / 'AuthoredTextures' / safe / (channel + suffix)
    destination.parent.mkdir(parents=True, exist_ok=True)
    if not destination.is_file() or destination.read_bytes() != data:
        destination.write_bytes(data)
    copied.append({'image': image.name, 'source': str(original), 'packed': bool(packed),
                   'destination': str(destination), 'sha256': hashlib.sha256(data).hexdigest()})
    return destination.relative_to(stage).as_posix()


def append_missing_authored_materials(manifest, meshes, stage=STAGING):
    stage = Path(stage)
    known = {entry['name'] for entry in manifest['materials']}
    materials = {}
    for obj in meshes:
        for material in obj.data.materials:
            if material is not None:
                name = ALIASES.get(material.name, material.name)
                if name not in known:
                    if name in materials and materials[name] != material:
                        raise ValueError('Ambiguous authored material identity: ' + name)
                    materials[name] = material
    added = []
    copied = []
    for name, material in sorted(materials.items()):
        if not material.use_nodes:
            raise ValueError('New material needs a Principled node recipe: ' + name)
        output = next((n for n in material.node_tree.nodes if n.type == 'OUTPUT_MATERIAL' and n.is_active_output), None)
        source = _socket_source(output.inputs['Surface']) if output else None
        if source is None or source.node.type != 'BSDF_PRINCIPLED':
            raise ValueError('New material has an unsupported surface graph: ' + name)
        bsdf = source.node
        alpha = bsdf.inputs['Alpha']
        if alpha.is_linked or abs(alpha.default_value - 1) > 1e-7:
            raise ValueError('Non-opaque material requires explicit importer surface support: ' + name)
        entry = {'name': name, 'metallicFallback': float(bsdf.inputs['Metallic'].default_value),
                 'roughnessFallback': float(bsdf.inputs['Roughness'].default_value),
                 'doubleSided': not material.use_backface_culling}
        base, _ = _image_channel(bsdf.inputs['Base Color'])
        tint = (1, 1, 1, 1) if base else tuple(bsdf.inputs['Base Color'].default_value)
        if not all(math.isfinite(x) for x in tint):
            raise ValueError('Nonfinite authored base color: ' + name)
        entry['baseTint'] = dict(zip(('r', 'g', 'b', 'a'), tint))
        entry['useSolidColor'] = base is None
        normal_source = _socket_source(bsdf.inputs['Normal'])
        entry['normalStrength'] = float(normal_source.node.inputs['Strength'].default_value) if normal_source and normal_source.node.type == 'NORMAL_MAP' else 1.0
        if not math.isfinite(entry['normalStrength']) or entry['normalStrength'] < 0:
            raise ValueError('Invalid authored normal strength: ' + name)
        for key, socket, normal in [('baseColor', 'Base Color', False), ('normal', 'Normal', True),
                                    ('metallic', 'Metallic', False), ('roughness', 'Roughness', False)]:
            image, component = _image_channel(bsdf.inputs[socket], normal)
            if image:
                entry[key] = _copy_image(image, stage, name, key, copied)
                if key in ('metallic', 'roughness'):
                    entry[key + 'Channel'] = component
        for key in ('metallicFallback', 'roughnessFallback'):
            if not math.isfinite(entry[key]) or not 0 <= entry[key] <= 1:
                raise ValueError('Invalid authored scalar: ' + name + '.' + key)
        added.append(entry)
    # Do not partially replace material definitions if a later recipe is unsupported.
    manifest['materials'].extend(added)
    return {'status': 'MISSING_AUTHORED_MATERIALS_APPENDED', 'added': added, 'copiedTextures': copied,
            'existingMaterialDefinitionsPreserved': True, 'rendererSlotsPreserved': True,
            'geometryExported': False, 'sourceNodeRecipe': 'Direct opaque Principled scalar/image inputs; tangent normal image plus authored normalStrength scalar. Unsupported graphs fail.'}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--blend', type=Path, default=ROOT / 'Art/PlayerV2/DosaV2_Assembled.blend')
    parser.add_argument('--manifest', type=Path, default=STAGING / 'texture-manifest.json')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    source_hash = sha(args.blend)
    bpy.ops.wm.open_mainfile(filepath=str(args.blend))
    manifest = json.loads(args.manifest.read_text(encoding='utf-8-sig'))
    before = sha(args.manifest)
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name != 'DosaV2_SourceSurface'
              and o.name.startswith(('DosaV2_', 'DosaPackV2_'))]
    result = append_missing_authored_materials(manifest, meshes, args.manifest.parent)
    args.manifest.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    result.update({'source': str(args.blend), 'sourceSha256': source_hash, 'manifest': str(args.manifest),
                   'manifestBeforeSha256': before, 'manifestAfterSha256': sha(args.manifest)})
    assert sha(args.blend) == source_hash
    (args.manifest.parent / 'authored-material-manifest-patch.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
