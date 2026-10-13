"""#308 PackLite: light, game-ready copies (LOD0/1/2 FBX + <=1024 textures) of owned pack buildings.  Offline, CPU only.

  blender -b --factory-startup -P Tools/Blender/PackLite308/packlite308.py -- --only <Name>[,<Name>] [--force]
  (normal use: python Tools/Blender/PackLite308/run.py  — one Blender process per item, resource guard before each)

Reads packlite308.json. Per item writes Art/World/PackLite308/<class>/<Name>_LOD{0,1,2}.fbx, <Name>.json (manifest entry),
_sheets/<class>/<Name>.png (source | LOD0 | LOD1 | LOD2  x  fromNegZ / fromPosX / top / 3-4), textures under _textures/.
Idempotent: an item whose <Name>.json carries the same config hash is skipped unless --force.
"""
import bpy, bmesh, sys, json, time, hashlib, os, math, re, ctypes
from pathlib import Path
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import pl_load as L
import pl_view as V
import pl_roof as R
import unity_prefab as UP

REPO = HERE.parents[2]
OUT = REPO / 'Art/World/PackLite308'
TEX = OUT / '_textures'
TCACHE = OUT / '_cache/tex'
CLASS_DEFAULT = {   # LOD0 triangle target, smallest feature kept (m) at LOD0/1/2, max materials
    'hero': (60000, (0.10, 0.30, 0.90), 12), 'hall': (22000, (0.12, 0.35, 0.90), 10), 'gate': (20000, (0.12, 0.35, 0.90), 10),
    'pavilion': (12000, (0.10, 0.30, 0.80), 10), 'corridor': (12000, (0.12, 0.35, 0.90), 9), 'wall': (1500, (0.08, 0.25, 0.60), 3),
    'fortress': (25000, (0.15, 0.40, 1.00), 8), 'house': (4000, (0.10, 0.30, 0.70), 7), 'prop': (500, (0.02, 0.04, 0.07), 2),
}


def log(*a): print('PL', *a, flush=True)


def peak_mb():
    class PMC(ctypes.Structure):
        _fields_ = [('cb', ctypes.c_ulong), ('PageFaultCount', ctypes.c_ulong), ('PeakWorkingSetSize', ctypes.c_size_t), ('WorkingSetSize', ctypes.c_size_t),
                    ('QuotaPeakPagedPoolUsage', ctypes.c_size_t), ('QuotaPagedPoolUsage', ctypes.c_size_t), ('QuotaPeakNonPagedPoolUsage', ctypes.c_size_t),
                    ('QuotaNonPagedPoolUsage', ctypes.c_size_t), ('PagefileUsage', ctypes.c_size_t), ('PeakPagefileUsage', ctypes.c_size_t)]
    try:
        p = PMC(); p.cb = ctypes.sizeof(PMC)
        k = ctypes.windll.kernel32; k.GetCurrentProcess.restype = ctypes.c_void_p
        ctypes.windll.psapi.GetProcessMemoryInfo.argtypes = [ctypes.c_void_p, ctypes.POINTER(PMC), ctypes.c_ulong]
        ctypes.windll.psapi.GetProcessMemoryInfo(k.GetCurrentProcess(), ctypes.byref(p), p.cb)
        return int(p.PeakWorkingSetSize / 2**20)
    except Exception: return -1


# ------------------------------------------------------------------ mesh helpers
def activate(o):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o


def tri_count(me): return sum(len(p.vertices) - 2 for p in me.polygons)


def weld(o, dist=1e-4):
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=dist)
    bm.to_mesh(o.data); bm.free(); o.data.update()


def poly_arrays(me):
    npoly = len(me.polygons)
    ls = np.empty(npoly, dtype=np.int32); me.polygons.foreach_get('loop_start', ls)
    lt = np.empty(npoly, dtype=np.int32); me.polygons.foreach_get('loop_total', lt)
    lv = np.empty(len(me.loops), dtype=np.int32); me.loops.foreach_get('vertex_index', lv)
    return ls, lt, lv


def vert_labels(me):
    """connected-component label per vertex (numpy pointer jumping)."""
    nv = len(me.vertices)
    ne = len(me.edges)
    ev = np.empty(ne * 2, dtype=np.int32); me.edges.foreach_get('vertices', ev); ev = ev.reshape(-1, 2)
    lab = np.arange(nv, dtype=np.int64)
    for _ in range(400):
        m = np.minimum(lab[ev[:, 0]], lab[ev[:, 1]])
        new = lab.copy()
        np.minimum.at(new, ev[:, 0], m); np.minimum.at(new, ev[:, 1], m)
        new = new[new]; new = new[new]
        if np.array_equal(new, lab): break
        lab = new
    return lab


def delete_polys(o, kill):
    """kill: bool array per polygon."""
    if not kill.any(): return 0
    me = o.data
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    fs = [bm.faces[i] for i in np.nonzero(kill)[0]]
    bmesh.ops.delete(bm, geom=fs, context='FACES')
    bm.to_mesh(me); bm.free(); me.update()
    return len(fs)


def drop_small(o, min_diag, keep_big_area=None):
    """remove connected components whose bounding-box diagonal is below min_diag."""
    me = o.data
    if not len(me.polygons): return 0
    lab = vert_labels(me)
    nv = len(me.vertices)
    co = np.empty(nv * 3, dtype=np.float32); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    uniq, inv = np.unique(lab, return_inverse=True)
    mn = np.full((len(uniq), 3), np.inf, dtype=np.float32); mx = np.full((len(uniq), 3), -np.inf, dtype=np.float32)
    np.minimum.at(mn, inv, co); np.maximum.at(mx, inv, co)
    diag = np.linalg.norm(mx - mn, axis=1)
    ls, lt, lv = poly_arrays(me)
    pc = inv[lv[ls]]
    return delete_polys(o, diag[pc] < min_diag)


def decimate(o, target):
    me = o.data
    for _ in range(4):
        n = tri_count(me)
        if n <= target * 1.03: break
        activate(o)
        md = o.modifiers.new('d', 'DECIMATE'); md.decimate_type = 'COLLAPSE'; md.ratio = max(0.002, target / n * 0.985); md.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier='d')
        me = o.data
    return tri_count(o.data)


def _dirs():
    out = [(0, 0, 1.0)]
    for el in (-60, -30, -10, 10, 30, 60):
        e = math.radians(el)
        for k in range(16):
            a = 2 * math.pi * (k + (0.5 if el in (-30, 30) else 0)) / 16
            out.append((math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e)))
    return [Vector(d).normalized() for d in out]


def cull_hidden(o):
    """delete faces that cannot be seen from any outside direction (97 rays from the centroid, then 3 inner points x 33 rays),
    keeping a one-face ring around every visible face."""
    me = o.data
    bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces); bm.faces.ensure_lookup_table()
    tree = BVHTree.FromBMesh(bm, epsilon=0.0)
    dirs = _dirs(); few = dirs[::3]
    nf = len(bm.faces)
    vis = np.zeros(nf, dtype=bool)
    rc = tree.ray_cast
    for f in bm.faces:
        n = f.normal; c = f.calc_center_median()
        hit = False
        for d in dirs:
            s = 0.02 if d.dot(n) >= 0 else -0.02
            if rc(c + n * s + d * 0.01, d)[0] is None: hit = True; break
        if not hit:
            for v in f.verts:
                p = c.lerp(v.co, 0.7)
                for d in few:
                    s = 0.02 if d.dot(n) >= 0 else -0.02
                    if rc(p + n * s + d * 0.01, d)[0] is None: hit = True; break
                if hit: break
        vis[f.index] = hit
    keep = vis.copy()
    for f in bm.faces:
        if vis[f.index]: continue
        for e in f.edges:
            if any(vis[g.index] for g in e.link_faces): keep[f.index] = True; break
    dead = [f for f in bm.faces if not keep[f.index]]
    n = len(dead)
    if dead: bmesh.ops.delete(bm, geom=dead, context='FACES')
    bm.to_mesh(me); bm.free(); me.update()
    return n, nf


def cleanup(o, angle=40.0):
    me = o.data
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bmesh.ops.dissolve_degenerate(bm, dist=1e-5, edges=bm.edges)
    for _ in range(2):
        dead = [f for f in bm.faces if f.calc_area() < 2e-8]
        if dead: bmesh.ops.delete(bm, geom=dead, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    if loose: bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(me); bm.free()
    for a in list(me.attributes):
        if a.name in ('custom_normal', 'sharp_edge', 'sharp_face') or (a.domain == 'CORNER' and a.data_type in ('BYTE_COLOR', 'FLOAT_COLOR')):
            try: me.attributes.remove(a)
            except Exception: pass
    while len(me.uv_layers) > 1: me.uv_layers.remove(me.uv_layers[-1])
    me.polygons.foreach_set('use_smooth', np.ones(len(me.polygons), dtype=bool))
    me.set_sharp_from_angle(angle=math.radians(angle))
    me.update(); me.validate(clean_customdata=False)
    A = V.arrays(me)                                    # last guard: no zero-area triangle may reach the exporter (zero normals bloom white)
    if (A['area'] < 1e-9).any():
        delete_polys(o, A['area'] < 1e-9); o.data.update()


def normal_check(me):
    nl = len(me.loops)
    cn = np.empty(nl * 3, dtype=np.float32); me.corner_normals.foreach_get('vector', cn)
    ln = np.linalg.norm(cn.reshape(-1, 3), axis=1)
    A = V.arrays(me)
    return int((ln < 0.5).sum()), int((A['area'] < 1e-9).sum())


# ------------------------------------------------------------------ textures / materials
def tex_entry(path):
    """copy a <=1024 PNG of `path` to _textures/<pack>/ (once) and return {'rel', 'mean', 'small'}"""
    if not path or not os.path.exists(path): return None
    p = Path(path)
    parts = p.parts
    pack = 'misc'
    for i, s in enumerate(parts):
        if s in ('Assets',) and i + 1 < len(parts): pack = parts[i + 1].replace(' ', '')
    rel = '_textures/%s/%s.png' % (pack, p.stem)
    own = p.resolve().as_posix().startswith(OUT.resolve().as_posix())
    if own: rel = p.resolve().relative_to(OUT.resolve()).as_posix(); pack = 'own_' + p.parent.name
    out = OUT / rel
    npy = TCACHE / ('%s__%s.npy' % (pack, p.stem))
    if own or not (out.exists() and npy.exists()):
        out.parent.mkdir(parents=True, exist_ok=True); TCACHE.mkdir(parents=True, exist_ok=True)
        img = bpy.data.images.load(str(p), check_existing=False)
        w, h = img.size
        if w == 0 or h == 0: bpy.data.images.remove(img); return None
        k = min(1.0, 1024.0 / max(w, h))
        if k < 1.0 and not own: img.scale(max(1, int(w * k)), max(1, int(h * k)))
        if not own: img.filepath_raw = str(out); img.file_format = 'PNG'; img.save()
        img.scale(128, 128)
        px = np.empty(128 * 128 * 4, dtype=np.float32); img.pixels.foreach_get(px)
        small = px.reshape(128, 128, 4)[:, :, :3].copy()
        np.save(npy, small); bpy.data.images.remove(img)
        meta = {'source': str(p).replace('\\', '/'), 'source_size': [w, h]}
        (TCACHE / ('%s__%s.json' % (pack, p.stem))).write_text(json.dumps(meta))
    small = np.load(npy)
    meta = json.loads((TCACHE / ('%s__%s.json' % (pack, p.stem))).read_text())
    return {'rel': rel, 'small': small, 'mean': small.reshape(-1, 3).mean(0), 'source': meta['source'], 'source_size': meta['source_size']}


def consolidate_materials(o, kmax, name):
    """merge slots that share an albedo texture, keep the kmax largest by area, fold the rest into the nearest colour.
    -> (manifest material list, preview material list)"""
    me = o.data
    A = V.arrays(me)
    groups = {}   # key -> dict
    slot_key = []
    for i, m in enumerate(me.materials):
        info = eval(m.get('pl_info', '{}')) if m else {}
        alb = info.get('albedo')
        te = tex_entry(alb) if alb and not str(alb).startswith('guid:') else None
        tint = np.array(info.get('color') or (1, 1, 1), dtype=np.float32)
        if te is None and not info.get('color'): tint = np.array((0.6, 0.6, 0.6), dtype=np.float32)
        key = (te['rel'] if te else 'color:%s' % np.round(tint, 2).tolist()) + ('|clip' if info.get('alphaclip') else '')
        nrm = info.get('tex', {}).get('_BumpMap') or info.get('tex', {}).get('_NormalMap') if info else None
        g = groups.setdefault(key, {'key': key, 'tex': te, 'tint': tint, 'area': 0.0, 'src': [], 'clip': bool(info.get('alphaclip')), 'normal_src': nrm,
                                    'shader_guid': info.get('shader_guid')})
        g['area'] += float(A['area'][A['mat'] == i].sum()); g['src'].append(info.get('name') or (m.name if m else 'none'))
        slot_key.append(key)
    order = sorted(groups.values(), key=lambda g: -g['area'])
    order = [g for g in order if g['area'] > 0]
    kept = order[:kmax]
    total = sum(g['area'] for g in order) or 1.0
    for g in order: g['mean'] = (g['tex']['mean'] if g['tex'] else np.ones(3, dtype=np.float32)) * g['tint']
    remap = {}
    for g in order[kmax:]:
        j = int(np.argmin([np.linalg.norm(g['mean'] - k['mean']) + (10 if k['clip'] != g['clip'] else 0) for k in kept]))
        remap[g['key']] = j; kept[j].setdefault('folded', []).extend(g['src']); kept[j]['folded_area'] = kept[j].get('folded_area', 0) + g['area']
    for j, g in enumerate(kept): remap[g['key']] = j
    new_index = np.array([remap.get(k, 0) for k in slot_key], dtype=np.int32)
    pm = np.empty(len(me.polygons), dtype=np.int32); me.polygons.foreach_get('material_index', pm)
    pm = new_index[np.clip(pm, 0, len(new_index) - 1)] if len(new_index) else pm
    me.materials.clear()
    mani, prev = [], []
    for j, g in enumerate(kept):
        stem = Path(g['tex']['rel']).stem if g['tex'] else 'Flat%d' % j
        pack = Path(g['tex']['rel']).parts[1] if g['tex'] else name
        mname = ('PL_%s_%s%s' % (pack, stem, '_Clip' if g['clip'] else ''))[:63]
        if g['tex'] and not g['tex']['rel'].startswith('_textures'): mname = 'PL_' + stem
        m = bpy.data.materials.get(mname) or bpy.data.materials.new(mname)
        me.materials.append(m)
        ne = tex_entry(g['normal_src']) if g['normal_src'] and not str(g['normal_src']).startswith('guid:') else None
        mani.append({'name': mname, 'albedo': g['tex']['rel'] if g['tex'] else None, 'albedo_source': g['tex']['source'] if g['tex'] else None,
                     'albedo_source_size': g['tex']['source_size'] if g['tex'] else None, 'normal': ne['rel'] if ne else None,
                     'tint': [round(float(x), 3) for x in g['tint']], 'alphaclip': g['clip'], 'area_share': round(g['area'] / total, 4),
                     'source_materials': sorted(set(g['src'])), 'folded_in': sorted(set(g.get('folded', []))), 'folded_area_share': round(g.get('folded_area', 0) / total, 4),
                     'source_shader_guid': g['shader_guid']})
        prev.append({'tex': g['tex']['small'] if g['tex'] else None, 'color': g['tint'], 'mean': g['mean']})
    me.polygons.foreach_set('material_index', pm); me.update()
    return mani, prev


def preview_mats_raw(me):
    out = []
    for m in me.materials:
        info = eval(m.get('pl_info', '{}')) if m else {}
        alb = info.get('albedo')
        te = tex_entry(alb) if alb and not str(alb).startswith('guid:') else None
        out.append({'tex': te['small'] if te else None, 'color': np.array(info.get('color') or (0.6, 0.6, 0.6), dtype=np.float32)})
    return out


def fold_materials(o, prev, mani, k):
    """keep the k largest material slots (by area) of an already consolidated mesh; fold the others into the nearest mean colour.
    -> (prev subset, names kept)"""
    me = o.data
    A = V.arrays(me)
    area = np.array([float(A['area'][A['mat'] == i].sum()) for i in range(len(me.materials))])
    order = [int(i) for i in np.argsort(-area) if area[i] > 0]
    kept = order[:min(2, k)]
    while len(kept) < min(k, len(order)):      # next = the material whose folding would hurt most (area x colour distance to the kept set)
        rest = [i for i in order if i not in kept]
        cost = [area[i] * (0.05 + min(float(np.linalg.norm(prev[i]['mean'] - prev[j]['mean'])) for j in kept)) for i in rest]
        kept.append(rest[int(np.argmax(cost))])
    remap = np.zeros(len(me.materials), dtype=np.int32)
    for i in range(len(me.materials)):
        if i in kept: remap[i] = kept.index(i)
        else: remap[i] = int(np.argmin([np.linalg.norm(prev[i]['mean'] - prev[j]['mean']) for j in kept]))
    pm = np.empty(len(me.polygons), dtype=np.int32); me.polygons.foreach_get('material_index', pm)
    mats = [me.materials[i] for i in kept]
    me.materials.clear()
    for m in mats: me.materials.append(m)
    me.polygons.foreach_set('material_index', remap[np.clip(pm, 0, len(remap) - 1)]); me.update()
    return [prev[i] for i in kept], [m.name for m in mats]


def palette_lod2(o, prev, name, cls):
    """single material: every face's UV points at its material's mean-colour swatch."""
    me = o.data
    K = max(1, len(prev))
    img = np.zeros((8, 8 * K, 3), dtype=np.float32)
    for k, p in enumerate(prev): img[:, 8 * k:8 * k + 8] = np.clip(p['mean'], 0, 1)
    rel = '%s/%s_LOD2_palette.png' % (cls, name)
    V.save_png(str(OUT / rel), img ** (1 / 1.0))
    ls, lt, lv = poly_arrays(me)
    pm = np.empty(len(me.polygons), dtype=np.int32); me.polygons.foreach_get('material_index', pm)
    lm = np.repeat(pm, lt)
    uv = np.empty((len(me.loops), 2), dtype=np.float32); uv[:, 0] = (lm + 0.5) / K; uv[:, 1] = 0.5
    if not me.uv_layers: me.uv_layers.new(name='UVMap')
    me.uv_layers[0].data.foreach_set('uv', uv.reshape(-1))
    me.materials.clear(); m = bpy.data.materials.new('PL_%s_Palette' % name); me.materials.append(m)
    me.polygons.foreach_set('material_index', np.zeros(len(me.polygons), dtype=np.int32)); me.update()
    return rel, [{'tex': np.repeat(np.repeat(img, 16, 0), 16, 1), 'color': np.ones(3, dtype=np.float32)}]


# ------------------------------------------------------------------ export / validate
def export_fbx(o, path):
    activate(o)
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, use_mesh_modifiers=False, mesh_smooth_type='OFF', use_tspace=False, add_leaf_bones=False, bake_anim=False,
                             path_mode='STRIP', embed_textures=False, use_custom_props=False)
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def bounds(me):
    co = np.empty(len(me.vertices) * 3, dtype=np.float32); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    return co.min(0), co.max(0)


def unity_box(mn, mx):
    """Blender work-space bounds -> Unity min/max (x,y,z)."""
    return [round(float(-mx[0]), 3), round(float(mn[2]), 3), round(float(-mx[1]), 3)], [round(float(-mn[0]), 3), round(float(mx[2]), 3), round(float(-mn[1]), 3)]


def sub_arrays(A, mask):
    return {'co': A['co'], 'tri': A['tri'][mask], 'tl': A['tl'][mask], 'mat': A['mat'][mask], 'uv': A['uv'], 'normal': A['normal'][mask], 'area': A['area'][mask]}


def make_roof(o, item, name, cls, A, target):
    """replace the tile material(s) of `o` by height-field skins.  -> (list of skin objects, info dict) or ([], None)"""
    me = o.data
    if item.get('roof') == 'none': return [], None
    slots = R.roof_slots(me, A, item.get('roof'), log)
    if not slots: return [], None
    mask = np.isin(A['mat'], slots)
    Ar = sub_arrays(A, mask)
    mn, mx = A['co'][Ar['tri'].reshape(-1)].min(0), A['co'][Ar['tri'].reshape(-1)].max(0)
    ext = float(max(mx[0] - mn[0], mx[1] - mn[1]))
    S = int(item.get('roof_tex', 1024))
    Pr, Cr, Nr = V.sample(Ar, ext / S, preview_mats_raw(me), seed=3, cap=6_000_000, density=3.0)
    up = R.layer_split(Pr)
    tl = [None] if up is None else [False, True]
    cell = float(np.clip(ext / 230.0, 0.06, 0.16))
    albedo = np.zeros((S, S, 3), dtype=np.float32); nrm = np.zeros((S, S, 3), dtype=np.float32)
    skins = []; zmin_bot = 1e9
    for t, sel in enumerate(tl):
        k = np.ones(len(Pr), dtype=bool) if sel is None else (up == sel)
        if k.sum() < 200: continue
        P = Pr[k]
        pad = 2 * cell
        rect = (float(P[:, 0].min() - pad), float(P[:, 1].min() - pad), float(P[:, 0].max() + pad), float(P[:, 1].max() + pad))
        c0, c1 = int(S * t / len(tl)), int(S * (t + 1) / len(tl))
        a_, n_, _ = R.bake_top(P, Cr[k], Nr[k], rect, c1 - c0, S)
        albedo[:, c0:c1] = a_; nrm[:, c0:c1] = n_
        so, ztop_min, zbot_min = R.skin_mesh(P, cell, (c0 / S, 0.0, c1 / S, 1.0) + rect, '%s_roof%d' % (name, t))
        zmin_bot = min(zmin_bot, zbot_min)
        log('roof tier', t, 'samples', int(k.sum()), 'z %.2f..%.2f' % (float(P[:, 2].min()), float(P[:, 2].max())), 'skin tris', len(so.data.polygons))
        skins.append(so)
    if not skins: return [], None
    rel_a = '%s/%s_Roof.png' % (cls, name); rel_n = '%s/%s_Roof_N.png' % (cls, name)
    V.save_png(str(OUT / rel_a), albedo); V.save_png(str(OUT / rel_n), nrm)
    m = bpy.data.materials.new('PLROOF_' + name)
    m['pl_info'] = repr({'name': 'baked roof (top-down) of ' + ', '.join(sorted({me.materials[i].name for i in slots})), 'albedo': str(OUT / rel_a), 'color': [1.0, 1.0, 1.0],
                         'tex': {'_BumpMap': str(OUT / rel_n)}, 'alphaclip': False, 'shader_guid': None})
    for so in skins: so.data.materials.append(m)
    # remove the tile faces from the body
    pm = np.empty(len(me.polygons), dtype=np.int32); me.polygons.foreach_get('material_index', pm)
    delete_polys(o, np.isin(pm, slots))
    info = {'tiers': len(skins), 'cell_m': round(cell, 3), 'texture': rel_a, 'normal': rel_n, 'texture_px': S, 'source_tris': int(mask.sum()),
            'source_materials': sorted({me.materials[i].name for i in slots}), 'eave_bottom_z': round(zmin_bot, 3)}
    return skins, info


def join(objs):
    objs = [x for x in objs if x is not None and len(x.data.polygons)]
    bpy.ops.object.select_all(action='DESELECT')
    for x in objs: x.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1: bpy.ops.object.join()
    return bpy.context.view_layer.objects.active


def split_by_material(o, pred):
    """move faces whose material name satisfies pred into a new object; returns it (or None)."""
    me = o.data
    idx = [i for i, m in enumerate(me.materials) if m and pred(m.name)]
    if not idx: return None
    pm = np.empty(len(me.polygons), dtype=np.int32); me.polygons.foreach_get('material_index', pm)
    sel = np.isin(pm, idx)
    if not sel.any(): return None
    o2 = o.copy(); o2.data = me.copy(); bpy.context.scene.collection.objects.link(o2)
    delete_polys(o, sel); delete_polys(o2, ~sel)
    return o2


def comp_stats(me):
    """per polygon: component id; per component: triangle count, bbox diagonal."""
    lab = vert_labels(me)
    nv = len(me.vertices)
    co = np.empty(nv * 3, dtype=np.float32); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    uniq, inv = np.unique(lab, return_inverse=True)
    mn = np.full((len(uniq), 3), np.inf, dtype=np.float32); mx = np.full((len(uniq), 3), -np.inf, dtype=np.float32)
    np.minimum.at(mn, inv, co); np.maximum.at(mx, inv, co)
    ls, lt, lv = poly_arrays(me)
    pc = inv[lv[ls]]
    tris = np.bincount(pc, weights=(lt - 2), minlength=len(uniq))
    return pc, tris, np.linalg.norm(mx - mn, axis=1)


def hull_components(o, max_diag):
    """replace every dense loose piece smaller than max_diag by its convex hull (closed, so it collapses cleanly); UVs are taken
    from the piece's own vertices, the material from its majority."""
    me = o.data
    if not len(me.polygons): return 0
    pc, tris, diag = comp_stats(me)
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    uvl = bm.loops.layers.uv.active
    groups = {}
    for f in bm.faces: groups.setdefault(int(pc[f.index]), []).append(f)
    dead = []; n = 0
    for c, faces in groups.items():
        if diag[c] >= max_diag or tris[c] <= 12: continue
        verts = list({v for f in faces for v in f.verts})
        if len(verts) < 5: continue
        xs = [v.co for v in verts]
        ext = [max(p[i] for p in xs) - min(p[i] for p in xs) for i in range(3)]
        if min(ext) < 0.02: continue          # flat panel: a hull would be degenerate
        vuv = {}
        mats = {}
        for f in faces:
            mats[f.material_index] = mats.get(f.material_index, 0) + 1
            if uvl is not None:
                for l in f.loops: vuv.setdefault(l.vert, l[uvl].uv.copy())
        mat = max(mats, key=mats.get)
        try: res = bmesh.ops.convex_hull(bm, input=verts, use_existing_faces=False)
        except Exception: continue
        new = [g for g in res['geom'] if isinstance(g, bmesh.types.BMFace)]
        if len(new) < 4: continue
        ns = set(new)
        for f in new:
            f.material_index = mat; f.smooth = True
            if uvl is not None:
                for l in f.loops:
                    if l.vert in vuv: l[uvl].uv = vuv[l.vert]
        dead += [f for f in faces if f not in ns]; n += 1
    dead = [f for f in set(dead) if f.is_valid]
    if dead: bmesh.ops.delete(bm, geom=dead, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    if loose: bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free(); me.update()
    return n


def body_reduce(o, budget, feature, cap):
    """Pack bodies are thousands of loose pieces. Low-poly pieces (beams, boards, panels) are kept untouched — collapsing them
    makes shards. Dense pieces (brackets, turned columns, carvings) are decimated, but never below ~12 triangles a piece
    (below that a column turns into a sliver); small dense pieces become convex hulls first when there are very many.
    Pieces below `feature` are dropped; the limit rises towards `cap` while the counts do not fit the budget."""
    me = o.data
    if not len(me.polygons): return 0
    pc, tris, diag = comp_stats(me)
    thr = 96
    while True:
        simple = (tris <= thr) & (diag >= feature)
        if tris[simple].sum() <= 0.65 * budget or thr <= 12: break
        thr //= 2
    f_s = feature
    while tris[simple & (diag >= f_s)].sum() > 0.65 * budget and f_s < cap: f_s *= 1.25
    simple &= diag >= f_s
    ns = int(tris[simple].sum())
    B = max(int(0.35 * budget), budget - ns)
    f = feature
    cplx = (tris > thr) & (diag >= feature)
    while f < cap and 12 * np.count_nonzero(cplx & (diag >= f)) > 1.6 * B: f *= 1.25
    cplx &= diag >= f
    ncomp = int(np.count_nonzero(cplx)); dense_src = int(tris[cplx].sum())
    cx = o.copy(); cx.data = me.copy(); bpy.context.scene.collection.objects.link(cx)
    delete_polys(o, ~simple[pc]); delete_polys(cx, ~cplx[pc])
    hulls = 0
    if dense_src > 6 * B and ncomp > 40: hulls = hull_components(cx, 1.2)
    # a piece keeps at least ~12 triangles, more when it is big (a 6 m curved wall block must not become a sliver)
    floor = int(np.minimum(tris[cplx], 12 + 1.5 * diag[cplx] ** 2).sum())
    if len(cx.data.polygons): decimate(cx, max(B, floor))
    log('body: plain %d tris (<= %d each, >= %.2f m) | dense %d pieces %d -> %d tris (>= %.2f m, %d hulls) | budget %d' % (
        ns, thr, f_s, ncomp, dense_src, tri_count(cx.data), f, hulls, budget))
    res = join([o, cx])
    return tri_count(res.data)


def decimate_split(o, target, feature, roof_pred, roof_share, cap_mul=4.0):
    """roof skin and body are reduced separately (a shared decimation would eat the smooth roof). Leaves the result active."""
    r = split_by_material(o, roof_pred)
    used = 0
    if r is not None: used = decimate(r, max(60, int(target * roof_share)))
    nb = body_reduce(o, max(40, target - used), feature, feature * cap_mul)
    body = bpy.context.view_layer.objects.active
    res = join([body] + ([r] if r is not None else []))
    log('split: roof', used, 'body', nb, 'target', target)
    return tri_count(res.data)


def occupancy(P, mn, mx, cell=0.25):
    """voxel occupancy of the source surface samples, dilated by one cell."""
    vmin = np.asarray(mn, dtype=np.float32) - 2 * cell
    dims = (np.ceil((np.asarray(mx) - np.asarray(mn) + 4 * cell) / cell).astype(int) + 1)
    vi = ((P - vmin) / cell).astype(np.int32)
    occ = np.zeros(tuple(int(x) for x in dims), dtype=bool)
    occ[vi[:, 0], vi[:, 1], vi[:, 2]] = True
    d = occ.copy()
    for ax in range(3): d |= np.roll(occ, 1, ax) | np.roll(occ, -1, ax)
    return d, vmin, cell


def shard_filter(o, occ3):
    """delete big triangles that run through space the source never occupied (a collapsed piece turned into a spike or a plate)."""
    occ, vmin, cell = occ3
    me = o.data
    A = V.arrays(me)
    if not len(A['tri']): return 0
    p = A['co'][A['tri']].astype(np.float32)
    e = np.stack([np.linalg.norm(p[:, 0] - p[:, 1], axis=1), np.linalg.norm(p[:, 1] - p[:, 2], axis=1), np.linalg.norm(p[:, 2] - p[:, 0], axis=1)], 1).max(1)
    big = np.nonzero(e > 1.0)[0]
    if not len(big): return 0
    q = p[big]
    c = q.mean(1)
    pts = [c, (q[:, 0] + q[:, 1]) / 2, (q[:, 1] + q[:, 2]) / 2, (q[:, 2] + q[:, 0]) / 2, (c + q[:, 0]) / 2, (c + q[:, 1]) / 2, (c + q[:, 2]) / 2]
    hit = np.zeros(len(big), dtype=np.float32)
    dims = np.array(occ.shape)
    for x in pts:
        vi = ((x - vmin) / cell).astype(np.int64)
        ok = ((vi >= 0) & (vi < dims)).all(1)
        v = np.zeros(len(big), dtype=bool)
        v[ok] = occ[vi[ok, 0], vi[ok, 1], vi[ok, 2]]
        hit += v
    bad = big[(hit / len(pts)) < 0.6]
    if not len(bad): return 0
    me.calc_loop_triangles()
    pi = np.empty(len(me.loop_triangles), dtype=np.int32); me.loop_triangles.foreach_get('polygon_index', pi)
    kill = np.zeros(len(me.polygons), dtype=bool); kill[pi[bad]] = True
    return delete_polys(o, kill)


def process(item, force=False):
    name, cls = item['name'], item['class']
    kind = item.get('kind', cls)
    tgt_def, feat_def, kmax_def = CLASS_DEFAULT[kind]
    target = int(item.get('target', tgt_def)); feat = item.get('min_feature', feat_def); kmax = item.get('mats_max', kmax_def)
    h = hashlib.sha1(json.dumps([item, CLASS_DEFAULT[kind], 'v18'], sort_keys=True).encode()).hexdigest()[:12]
    odir = OUT / cls; odir.mkdir(parents=True, exist_ok=True)
    jpath = odir / (name + '.json')
    if jpath.exists() and not force and json.loads(jpath.read_text(encoding='utf-8')).get('config_hash') == h:
        log('skip (up to date)', name); return
    t0 = time.time()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    L._fbx_cache.clear()
    src = item['source']
    src_path = str((REPO / 'Oheangbu' / src) if src.startswith('Assets/') else (OUT / src))
    st = {}
    o = L.build(src_path, name, st, item.get('include'), item.get('exclude'), item.get('box'))
    L.purge_imports()
    me = o.data
    if item.get('yaw'):   # rotate about the vertical so the front faces Unity -Z
        me.transform(__import__('mathutils').Matrix.Rotation(math.radians(-item['yaw']), 4, 'Z')); me.update()
    mn, mx = bounds(me)
    if item.get('height'):   # uniform scale so the model is this tall (props shipped at museum scale)
        me.transform(__import__('mathutils').Matrix.Scale(float(item['height']) / max(1e-6, float(mx[2] - mn[2])), 4)); me.update(); mn, mx = bounds(me)
    gz = float(mn[2]) if (item.get('ground') == 'min' or not (-2.5 <= mn[2] <= 0.6)) else 0.0
    shift = Vector((-(mn[0] + mx[0]) / 2, -(mn[1] + mx[1]) / 2, -gz))
    me.transform(__import__('mathutils').Matrix.Translation(shift)); me.update()
    mn, mx = bounds(me)
    src_tris = tri_count(me)
    log(name, 'source tris', src_tris, 'parts', st['parts'], 'size(unity x,y,z) %.2f %.2f %.2f' % (mx[0] - mn[0], mx[2] - mn[2], mx[1] - mn[1]), 'build %.0fs' % (time.time() - t0))
    corners = np.array([[x, y, z] for x in (mn[0], mx[0]) for y in (mn[1], mx[1]) for z in (mn[2], mx[2])], dtype=np.float64)
    ext = float(max(mx - mn))
    px = ext / 512.0
    A = V.arrays(me)
    Ps, Cs, Ns = V.sample(A, px, preview_mats_raw(me), cap=5_000_000)
    src_area = float(A['area'].sum())
    frames_iou = {v: V.frame(corners, v, (512, 512)) for v in ('fromNegZ', 'fromPosX', 'top')}
    frames_img = {v: V.frame(corners, v, (360, 270)) for v in V.VIEWS}
    src_masks = {v: V.splat(Ps, None, None, f) for v, f in frames_iou.items()}
    tiles = {v: [V.splat(Ps, Cs, Ns, f)[0]] for v, f in frames_img.items()}
    occ3 = occupancy(Ps, mn, mx)
    del Ps, Cs, Ns
    if item.get('lowpoly'): target = max(target, src_tris)     # source is already light: merge + cull only, no collapse at LOD0
    # --- roof skin
    skins, roof = make_roof(o, item, name, cls, A, target)
    del A
    # --- LOD0
    weld(o)
    n_small = drop_small(o, feat[0])
    pre = max(target * 4, 60000)
    if tri_count(o.data) > pre: decimate(o, pre)
    for so in skins: decimate(so, max(3000, int(target * 1.2 / len(skins))))
    log('pre-join tris body', tri_count(o.data), 'skins', [tri_count(x.data) for x in skins])
    o = join([o] + skins); o.name = name
    culled = (0, 0)
    if item.get('interior', 'cull') == 'cull':
        culled = cull_hidden(o)
        weld(o); drop_small(o, feat[0])
    log('after cull tris', tri_count(o.data), 'culled', culled)
    # the roof skin and the body get separate budgets: bracket-heavy bodies are thousands of loose pieces that cannot collapse
    # below a floor, and one shared decimation would then eat the (smooth, cheap) roof instead
    n0 = decimate_split(o, target, feat[0], lambda n: n.startswith('PLROOF_'), item.get('roof_share', 0.35))
    o = bpy.context.view_layer.objects.active; o.name = name
    log('shards removed LOD0:', shard_filter(o, occ3)); n0 = tri_count(o.data)
    mani, prev = consolidate_materials(o, kmax, name)
    cleanup(o)
    is_roof = lambda n: n.endswith('_Roof')
    # --- LOD1
    o1 = o.copy(); o1.data = o.data.copy(); o1.name = name + '_LOD1'; bpy.context.scene.collection.objects.link(o1)
    decimate_split(o1, max(40, int(n0 * item.get('lod1', 0.6 if kind == 'prop' else 0.35))), feat[1], is_roof, item.get('roof_share', 0.35), cap_mul=3.0)
    o1 = bpy.context.view_layer.objects.active; o1.name = name + '_LOD1'
    log('shards removed LOD1:', shard_filter(o1, occ3))
    prev1, names1 = fold_materials(o1, prev, mani, int(item.get('lod1_mats', 2 if kind in ('prop', 'wall') else 6)))
    cleanup(o1, 45.0)
    # --- LOD2: prisms of the LOD0 body + the roof skin decimated hard (or plain decimation when there is no roof and proxy is off)
    t2 = max(24, int(n0 * item.get('lod2', 0.35 if kind == 'prop' else 0.10)))
    o2 = o1.copy(); o2.data = o1.data.copy(); o2.name = name + '_LOD2'; bpy.context.scene.collection.objects.link(o2)
    if item.get('proxy', 'prism' if roof else 'decimate') == 'prism':
        r2 = split_by_material(o2, is_roof)
        prev_l1 = prev1
        A0 = V.arrays(o.data)
        body = ~np.isin(A0['mat'], [i for i, m in enumerate(o.data.materials) if is_roof(m.name)])
        Pb, Cb, _ = V.sample(sub_arrays(A0, body), 0.08, [{'tex': None, 'color': p['mean']} for p in prev], seed=5, cap=4_000_000)
        ztop = (roof['eave_bottom_z'] - 0.05) if roof else float(Pb[:, 2].max()) + 0.01
        cellp = 0.4
        for _ in range(4):
            vs, fs, fc = R.prism_proxy(Pb, Cb, ztop, cellp, name)
            if len(fs) <= max(200, t2 * (0.6 if r2 else 1.0)): break
            cellp *= 1.5
        bpy.data.objects.remove(o2)
        pm_ = bpy.data.meshes.new(name + '_LOD2'); pm_.from_pydata([tuple(map(float, v)) for v in vs], [], fs); pm_.update()
        ucol, inv = np.unique(np.round(fc, 3), axis=0, return_inverse=True)
        prev2 = []
        for c in ucol:
            m = bpy.data.materials.new('pp'); pm_.materials.append(m); prev2.append({'mean': c.astype(np.float32)})
        pm_.polygons.foreach_set('material_index', inv.astype(np.int32).reshape(-1))
        pm_.uv_layers.new(name='UVMap')
        o2 = bpy.data.objects.new(name + '_LOD2', pm_); bpy.context.scene.collection.objects.link(o2)
        if r2:
            decimate(r2, max(60, t2 - len(fs)))
            roof_means = [prev[i]['mean'] for i, m in enumerate(o.data.materials) if is_roof(m.name)]
            for m_i in range(len(r2.data.materials)): prev2.append({'mean': roof_means[0]})
            keep = [m for m in r2.data.materials]
            o2 = join([o2, r2]); o2.name = name + '_LOD2'
            prev2 = prev2[:len(ucol)] + [{'mean': roof_means[0]}] * (len(o2.data.materials) - len(ucol))
        cleanup(o2, 50.0)
        pal_rel, prevp = palette_lod2(o2, prev2, name, cls)
    else:
        drop_small(o2, feat[2]); decimate(o2, t2); cleanup(o2, 50.0)
        pal_rel, prevp = palette_lod2(o2, prev1, name, cls)
    lods = [o, o1, o2]
    o.name = name + '_LOD0'
    entry = {'name': name, 'class': cls, 'kind': kind, 'source': src, 'filter': {k: item[k] for k in ('include', 'exclude', 'box') if k in item}, 'reference': item.get('ref', ''),
             'use': item.get('use', ''), 'front': item.get('front', 'as source prefab'), 'yaw_applied_deg': item.get('yaw', 0),
             'config_hash': h, 'source_tris': src_tris, 'source_parts': st['parts'], 'source_area_m2': round(src_area, 1),
             'pivot': 'bbox centre in XZ, Y=0 at %s' % ('lowest vertex' if gz != 0.0 or item.get('ground') == 'min' else 'the source prefab origin height (authored ground)'),
             'pivot_shift_unity': [round(float(shift[0]) * -1, 3), round(float(shift[2]), 3), round(float(shift[1]) * -1, 3)],
             'removed': {'small_components_faces': int(n_small), 'hidden_faces': int(culled[0]), 'hidden_tested': int(culled[1])},
             'roof_skin': roof, 'materials': mani, 'lod1_materials': names1, 'lod2_palette': pal_rel, 'lods': []}
    umn, umx = unity_box(*bounds(o.data))
    entry['bounds_unity'] = {'min': umn, 'max': umx, 'size': [round(umx[i] - umn[i], 3) for i in range(3)]}
    entry['below_ground_m'] = round(-umn[1], 3)
    for i, lo in enumerate(lods):
        me_i = lo.data
        fbx = odir / ('%s_LOD%d.fbx' % (name, i))
        sha = export_fbx(lo, fbx)
        zn, dg = normal_check(me_i)
        Ai = V.arrays(me_i)
        Pi, Ci, Ni = V.sample(Ai, px, (prev, prev1, prevp)[i], seed=11 + i)
        sc = {}
        for v, f in frames_iou.items():
            _, m_, d_ = V.splat(Pi, None, None, f)
            sc[v] = round(V.iou(src_masks[v][1], m_), 4)
            if v == 'top':
                sm, sd = V.dilate(src_masks[v][1]), src_masks[v][2]
                both = src_masks[v][1] & m_
                hole = both & ((d_ - sd) > 0.6)
                sc['roof_cover'] = round(float((sm & V.dilate(m_)).sum()) / max(1, int(sm.sum())), 4)
                sc['roof_hole_frac'] = round(float(hole.sum()) / max(1, int(both.sum())), 4)
        for v, f in frames_img.items(): tiles[v].append(V.splat(Pi, Ci, Ni, f)[0])
        b0, b1 = bounds(me_i)
        entry['lods'].append({'lod': i, 'fbx': '%s/%s' % (cls, fbx.name), 'tris': int(len(Ai['tri'])), 'verts': int(len(me_i.vertices)), 'materials': len(me_i.materials),
                              'iou': sc, 'zero_normals': zn, 'degenerate_tris': dg, 'bytes': fbx.stat().st_size, 'sha256': sha,
                              'bbox_dev_m': round(float(max(np.abs(b0 - mn).max(), np.abs(b1 - mx).max())), 3)})
        log(name, 'LOD%d tris %d mats %d iou %s zn %d dg %d' % (i, len(Ai['tri']), len(me_i.materials), sc, zn, dg))
        del Ai, Pi, Ci, Ni
    rows = [np.concatenate(tiles[v], axis=1) for v in ('fromNegZ', 'fromPosX', 'top', 'q34')]
    sdir = OUT / '_sheets' / cls; sdir.mkdir(parents=True, exist_ok=True)
    V.save_png(str(sdir / (name + '.png')), np.concatenate(rows, axis=0))
    entry['sheet'] = '_sheets/%s/%s.png' % (cls, name)
    entry['secs'] = round(time.time() - t0, 1); entry['peak_mb'] = peak_mb()
    jpath.write_text(json.dumps(entry, ensure_ascii=False, indent=1), encoding='utf-8')
    log(name, 'DONE %.0fs peak %d MB' % (entry['secs'], entry['peak_mb']))


if __name__ == '__main__':
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    cfg = json.loads((HERE / 'packlite308.json').read_text(encoding='utf-8'))
    only = set(argv[argv.index('--only') + 1].split(',')) if '--only' in argv else None
    for it in cfg['items']:
        if only and it['name'] not in only: continue
        try: process(it, '--force' in argv)
        except Exception as e:
            import traceback; traceback.print_exc(); log(it['name'], 'FAILED', repr(e))
