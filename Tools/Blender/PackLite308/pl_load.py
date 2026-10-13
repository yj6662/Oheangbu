"""#308 PackLite: build one Blender object from a Unity prefab (or model file) — used by packlite308.py.

Space conventions (measured, see README_import.md):
  Unity mesh-local  = (-x, y, z) * unitScale * globalScale of the raw FBX geometry (Unity flips X for handedness)
  Unity world       = prefab transform chain * mesh-local             (left-handed, Y up, +Z forward)
  Blender work space= C * Unity world,  C: (ux,uy,uz) -> (-ux, -uz, uy)   (right-handed, Z up)
  FBX export (axis_forward='-Z', axis_up='Y', bake_space_transform) then gives Unity exactly (ux,uy,uz) again
  with an identity root transform, i.e.  Blender +Y  == Unity -Z,  Blender -X == Unity +X.
"""
import bpy, bmesh, mmap, struct, sys, time
from pathlib import Path
from mathutils import Matrix
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import unity_prefab as UP

C = Matrix(((-1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))


def fbx_unit_scale(path):
    """UnitScaleFactor (cm per unit) from a binary or ascii FBX -> metres per unit."""
    try:
        with open(path, 'rb') as f, mmap.mmap(f.fileno(), 0, access=mmap.ACCESS_READ) as mm:
            i = mm.find(b'UnitScaleFactor')
            while i >= 0:
                if mm[i - 5:i - 4] == b'S':   # binary property record: S<len>name S.. S.. S.. D<double>
                    j = i + len(b'UnitScaleFactor')
                    for _ in range(3):
                        if mm[j:j + 1] != b'S': break
                        n = struct.unpack('<I', mm[j + 1:j + 5])[0]; j += 5 + n
                    if mm[j:j + 1] == b'D': return struct.unpack('<d', mm[j + 1:j + 9])[0] / 100.0
                else:
                    line = mm[i:i + 200].split(b'\n')[0].decode('ascii', 'ignore')
                    nums = [t for t in line.replace('"', ' ').replace(',', ' ').split() if t.replace('.', '').replace('-', '').replace('e', '').replace('+', '').isdigit()]
                    if nums: return float(nums[-1]) / 100.0
                i = mm.find(b'UnitScaleFactor', i + 10)
    except Exception as e:
        print('unit scale read failed', path, e)
    return 0.01


_fbx_cache = {}


def import_fbx(path):
    """-> list of (mesh datablock, object) imported from the file (cached, hidden from the scene)."""
    if path in _fbx_cache: return _fbx_cache[path]
    before = set(bpy.data.objects)
    t = time.time()
    if path.lower().endswith('.obj'): bpy.ops.wm.obj_import(filepath=path)
    else: bpy.ops.import_scene.fbx(filepath=path, use_anim=False, use_custom_normals=True, ignore_leaf_bones=True, use_image_search=False)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == 'MESH']
    out = {'objs': meshes, 'unit': fbx_unit_scale(path), 'meta': UP.fbx_meta(path), 'secs': time.time() - t}
    for o in new:
        for c in list(o.users_collection): c.objects.unlink(o)
    _fbx_cache[path] = out
    return out


def _used_slots(me):
    used = sorted({p.material_index for p in me.polygons})
    return used


def build(source, name, stats=None, include=None, exclude=None, box=None):
    """Flatten `source` (prefab / fbx path) into ONE new mesh object in Blender work space. Material slots are named by the
    Unity material (name|guid) so later stages can map them to textures."""
    import re
    parts = UP.resolve(source)

    def tag(p): return '/'.join(p.get('via', []) + [p['name']])
    if include: parts = [p for p in parts if re.search(include, tag(p))]
    if exclude: parts = [p for p in parts if not re.search(exclude, tag(p))]
    if box: parts = [p for p in parts if box[0] <= p['matrix'][0][3] <= box[1] and box[2] <= p['matrix'][2][3] <= box[3]]
    col = bpy.context.scene.collection
    made = []
    matcache = {}
    tris = 0
    for part in parts:
        imp = import_fbx(part['fbx'])
        objs = imp['objs']
        if not objs: print('WARN no mesh in', part['fbx']); continue
        s = imp['unit'] * (imp['meta']['globalScale'] if imp['meta']['useFileScale'] else imp['meta']['globalScale'] / max(imp['unit'], 1e-9) * 0.01)
        M = Matrix(part['matrix'])
        if part['mode'] == 'mesh':
            use = objs if len(objs) == 1 else _pick(objs, part, imp)
            F = Matrix.Diagonal((-s, s, s, 1.0))
            mats = [(o, C @ M @ F) for o in use]
        else:   # whole model: Blender's import already is the physical orientation; Unity world = (-bx, bz, -by)
            U = Matrix(((-1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))
            mats = [(o, C @ M @ U @ o.matrix_world) for o in objs]
        for src, W in mats:
            o = bpy.data.objects.new(name + '_p', src.data.copy())
            col.objects.link(o); o.matrix_world = W
            me = o.data
            used = _used_slots(me)
            # i-th used slot <- i-th Unity material
            for i, slot in enumerate(used):
                g = part['materials'][i] if i < len(part['materials']) else None
                if g is None and slot < len(me.materials) and me.materials[slot]:
                    g = imp['meta'].get('ext', {}).get(me.materials[slot].name.split('.0')[0])
                key = g or ('fbx:' + (me.materials[slot].name if slot < len(me.materials) and me.materials[slot] else 'none'))
                if key not in matcache:
                    info = UP.material_info(g) if g else {'name': key}
                    m = bpy.data.materials.new((info.get('name') or key)[:60]); m['unity_guid'] = g or ''; m['pl_info'] = repr(info)
                    matcache[key] = m
                while len(me.materials) <= slot: me.materials.append(None)
                me.materials[slot] = matcache[key]
            tris += sum(len(p.vertices) - 2 for p in me.polygons)
            made.append(o)
    if not made: raise RuntimeError('nothing resolved for ' + str(source))
    bpy.ops.object.select_all(action='DESELECT')
    for o in made: o.select_set(True)
    bpy.context.view_layer.objects.active = made[0]
    if len(made) > 1: bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.material_slot_remove_unused()
    if stats is not None: stats.update(parts=len(parts), fbx=len({p['fbx'] for p in parts}), source_tris=tris)
    return obj


def _pick(objs, part, imp):
    nm = imp['meta']['names'].get(part['mesh_file_id'])
    if nm:
        hit = [o for o in objs if o.data.name == nm or o.name == nm]
        if hit: return hit[:1]
    hit = [o for o in objs if o.name == part['name'] or o.data.name == part['name']]
    if hit: return hit[:1]
    print('WARN multi-mesh fbx, mesh not identified: %s (%d meshes) -> all' % (part['fbx'], len(objs)))
    return objs


def purge_imports():
    for imp in _fbx_cache.values():
        for o in imp['objs']:
            me = o.data
            try: bpy.data.objects.remove(o)
            except Exception: pass
            if me and me.users == 0: bpy.data.meshes.remove(me)
    _fbx_cache.clear()
    for b in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for d in list(b):
            if d.users == 0: b.remove(d)
