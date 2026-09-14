"""Keep an explicitly selected brush FBX and its paired material maps together.

Pure Python staging utility; importing it does not load Blender or touch assets.
The character exporter calls apply_selected_brush after rebuilding renderer maps.
"""
import copy
import hashlib
import json
import os
import shutil
from pathlib import Path

TEXTURE_FIELDS = ('baseColor', 'normal', 'metallic', 'roughness', 'occlusion')

def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))

def under(root, relative):
    root = Path(root).resolve()
    path = (root / relative).resolve()
    if not path.is_relative_to(root):
        raise ValueError('Brush selection path leaves its declared root: ' + relative)
    return path

def require_hash(path, expected):
    if not Path(path).is_file():
        raise FileNotFoundError('Selected brush dependency is missing: ' + str(path))
    actual = sha(path)
    if actual != expected:
        raise ValueError('Selected brush dependency hash changed: ' + str(path))
    return actual

def version_copy(source, target, history):
    """Only replace a differing target, after preserving its actual old bytes."""
    source, target, history = Path(source), Path(target), Path(history)
    expected = sha(source)
    previous = None
    if target.exists():
        actual = sha(target)
        if actual == expected:
            return None
        archive = history / actual / target.name
        archive.parent.mkdir(parents=True, exist_ok=True)
        if archive.exists():
            require_hash(archive, actual)
        else:
            shutil.copy2(target, archive)
        require_hash(archive, actual)
        previous = {'path': str(archive), 'sha256': actual, 'previousActivePath': str(target)}
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = target.with_name(target.name + '.selection-tmp')
    shutil.copy2(source, temporary)
    require_hash(temporary, expected)
    os.replace(temporary, target)
    return previous

def apply_selected_brush(manifest, stage):
    stage = Path(stage).resolve()
    art = stage.parent
    choice = read(stage / 'BrushRefined/active-selection.json')
    manifest_before = hashlib.sha256(json.dumps(manifest, sort_keys=True).encode()).hexdigest()
    selected = under(stage, choice['fbx'])
    fragment_path = under(stage, choice['materialFragment'])
    build_path = under(stage, choice['buildReport'])
    validation_path = under(stage, choice['validationReport'])
    original_blend = under(art, choice['preservedOriginalBlend'])
    selected_blend = under(art, choice['selectedBlend'])
    require_hash(selected, choice['fbxSha256'])
    require_hash(selected_blend, choice['selectedBlendSha256'])
    require_hash(original_blend, choice['preservedOriginalBlendSha256'])
    require_hash(fragment_path, choice['materialFragmentSha256'])
    build, validation, fragment = read(build_path), read(validation_path), read(fragment_path)
    if build['outputFbxSha256'] != choice['fbxSha256'] or validation['fbx_sha256'] != choice['fbxSha256']:
        raise ValueError('Selected FBX is not the one covered by build and round-trip validation.')
    if build['outputBlendSha256'] != choice['selectedBlendSha256']:
        raise ValueError('Selected authoring blend is not covered by its build report.')
    entries = fragment.get('materials', [])
    mappings = fragment.get('rendererMaterials', [])
    if len(entries) != 1 or entries[0]['name'] != 'DosaBrushV2_Bristles' or mappings != [
        {'renderer': 'DosaBrushV2_Bristles', 'materials': ['DosaBrushV2_Bristles']}]:
        raise ValueError('Brush fragment must change only the bristle material and renderer.')
    material = entries[0]
    if material.get('metallic') or material.get('roughness') or material.get('metallicFallback') != 0:
        raise ValueError('Selected bristle recipe must remain nonmetallic without the old metallic map.')
    texture_records = []
    paired_textures = {Path(item['path']).name: item['sha256'] for item in build['textures']}
    for channel in ('baseColor', 'normal'):
        texture = under(stage, material[channel])
        expected = paired_textures.get(texture.name)
        if not expected:
            raise ValueError('Bristle map is not paired with the selected UV build: ' + texture.name)
        require_hash(texture, expected)
        texture_records.append({'channel': channel, 'relativePath': material[channel], 'sha256': expected})
    # Validate everything before replacing the active FBX. Work on a copy until done.
    updated = copy.deepcopy(manifest)
    handle = next((m for m in updated['materials'] if m['name'] == 'DosaBrushV2'), None)
    if handle is None:
        raise ValueError('Original handle material is missing from character manifest.')
    handle_before = copy.deepcopy(handle)
    updated['materials'] = [m for m in updated['materials'] if m['name'] != material['name']] + [copy.deepcopy(material)]
    updated['rendererMaterials'] = [m for m in updated['rendererMaterials']
        if m['renderer'] not in ('DosaBrushV2_Handle', 'DosaBrushV2_Bristles')]
    updated['rendererMaterials'] += [{'renderer': 'DosaBrushV2_Handle', 'materials': ['DosaBrushV2']}] + copy.deepcopy(mappings)
    if next(m for m in updated['materials'] if m['name'] == 'DosaBrushV2') != handle_before:
        raise AssertionError('Handle material changed.')
    for field in TEXTURE_FIELDS:
        if handle.get(field) and not under(stage, handle[field]).is_file():
            raise FileNotFoundError('Preserved handle texture is missing: ' + handle[field])
    history = art / 'History/Brush'
    original_fbx_archive = None
    if choice.get('preservedOriginalFbxSha256'):
        original_fbx_archive = history / choice['preservedOriginalFbxSha256'] / 'SM_DosaBrushV2.fbx'
        active = stage / 'Brush/SM_DosaBrushV2.fbx'
        if not original_fbx_archive.exists() and (not active.exists() or sha(active) != choice['preservedOriginalFbxSha256']):
            raise ValueError('Original brush FBX is absent from both active staging and verified history.')
    ledger_path = stage / 'Brush/active-selection-ledger.json'
    old_ledger = read(ledger_path) if ledger_path.exists() else {}
    archives = list(old_ledger.get('historicalCopies', []))
    # The original authoring file stays where it was; also retain a hash-bound copy.
    original_archive = history / choice['preservedOriginalBlendSha256'] / original_blend.name
    original_archive.parent.mkdir(parents=True, exist_ok=True)
    if not original_archive.exists():
        shutil.copy2(original_blend, original_archive)
    require_hash(original_archive, choice['preservedOriginalBlendSha256'])
    original_record = {'path': str(original_archive), 'sha256': choice['preservedOriginalBlendSha256'],
                       'originalStillPreservedAt': str(original_blend)}
    if original_record not in archives:
        archives.append(original_record)
    # Old reports are archived too, preventing stale active-path provenance.
    for source, target in [(selected, stage / 'Brush/SM_DosaBrushV2.fbx'),
                           (build_path, stage / 'Brush/brush-build-report.json'),
                           (validation_path, stage / 'Brush/brush-fbx-validation.json')]:
        record = version_copy(source, target, history)
        if record and record not in archives:
            archives.append(record)
    manifest.clear(); manifest.update(updated)
    if original_fbx_archive:
        require_hash(original_fbx_archive, choice['preservedOriginalFbxSha256'])
    ledger = {'status': 'SELECTED_STATIC_BRUSH_STAGED_NOT_RIG_PASS',
              'selection': choice, 'activeFbx': str(stage / 'Brush/SM_DosaBrushV2.fbx'),
              'activeFbxSha256': sha(stage / 'Brush/SM_DosaBrushV2.fbx'),
              'pairedTextures': texture_records, 'handleMaterialUnchanged': True,
              'historicalCopies': archives, 'canonicalOriginalBlendPreserved': True,
              'originalFbxArchived': str(original_fbx_archive) if original_fbx_archive else None,
              'manifestLogicalBeforeSha256': manifest_before,
              'manifestLogicalAfterSha256': hashlib.sha256(json.dumps(manifest, sort_keys=True).encode()).hexdigest(),
              'selectionUtilitySha256': sha(Path(__file__)),
              'liveUnityEdited': False, 'rigPass': False}
    ledger_path.write_text(json.dumps(ledger, indent=2), encoding='utf-8')
    return ledger

if __name__ == '__main__':
    stage = Path(__file__).resolve().parents[3] / 'Art/PlayerV2/Staging'
    path = stage / 'texture-manifest.json'
    manifest = read(path)
    result = apply_selected_brush(manifest, stage)
    path.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print(json.dumps(result))
