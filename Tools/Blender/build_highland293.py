"""Finish the Highlands293 section meshes in Blender 5.0 (batch).

"C:/Program Files/Blender Foundation/Blender 5.0/blender.exe" -b --factory-startup -P Tools/Blender/build_highland293.py

Input : Art/World/Compact/Rebuild/Highlands293/Variants/<id>/grids.npz (Tools/Art/highland_sections293.py)
Output: Variants/<id>/Meshes/*.json (Import285 format: Unity world coordinates, split sharp edges, LODs)
        Variants/Stones/Stone_<k>_LOD<n>.json — 16 natural step stones (CC0 scans, top lightly flattened)
        Variants/Stones/<set>_<k>_LOD<n>.json — realm sets: broken (Jeokro), slab (Cheolong), cut (Hwanggyeong old stone road)
        Variants/Outcrops, Variants/Meshy, Variants/Props — crest masses, supplementary masses, charred timber
Optional stage filter: ... -P build_highland293.py -- sections stones assets
The 285 outputs (Mountain285/) are read-only inputs and are never written.
"""
import bpy, bmesh, json, math, random
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
VAR = ROOT / 'Art/World/Compact/Rebuild/Highlands293/Variants'
BOULDER = ROOT / 'Art/World/Compact/Rebuild/Mountain285/Meshes/Scanned_boulder_LOD1.json'  # CC0 boulder_01, 5k tris
stats = []


def reset():
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)


def grid_object(name, V, outward, smooth=True):
    """V: (a, b, 3) Unity world grid. Faces oriented so their mean normal agrees with `outward` (Unity)."""
    a, b, _ = V.shape
    verts = [(float(p[0]), float(-p[2]), float(p[1])) for p in V.reshape(-1, 3)]
    faces = []
    for i in range(a - 1):
        for j in range(b - 1):
            k = i * b + j
            faces.append((k, k + b, k + b + 1, k + 1))
    me = bpy.data.meshes.new(name); me.from_pydata(verts, [], faces); me.update()
    ob = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(ob)
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-5)
    bm.normal_update()
    want = np.array([outward[0], -outward[2], outward[1]], float)
    mean = np.sum([np.array(f.normal) * f.calc_area() for f in bm.faces], axis=0)
    if np.dot(mean, want) < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free(); me.update()
    for p in me.polygons:
        p.use_smooth = smooth
    return ob


def export(ob, path, target=None, sharp=38):
    """285 export: optional decimation, split sharp edges, Unity-space JSON."""
    clone = ob.copy(); clone.data = ob.data.copy(); bpy.context.collection.objects.link(clone)
    bpy.ops.object.select_all(action='DESELECT'); clone.select_set(True); bpy.context.view_layer.objects.active = clone
    clone.data.calc_loop_triangles()
    if target and target < len(clone.data.loop_triangles):
        mod = clone.modifiers.new('Preserve_large_planes', 'DECIMATE'); mod.ratio = target / len(clone.data.loop_triangles)
        with bpy.context.temp_override(object=clone, active_object=clone):
            bpy.ops.object.modifier_apply(modifier=mod.name)
    me = clone.data
    bm = bmesh.new(); bm.from_mesh(me); bm.normal_update()
    if not any(f.smooth for f in bm.faces):
        bmesh.ops.split_edges(bm, edges=list(bm.edges))
    else:
        edges = [e for e in bm.edges if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(sharp)]
        bmesh.ops.split_edges(bm, edges=edges)
    bm.to_mesh(me); bm.free(); me.update()
    me.calc_loop_triangles()
    data = {'vertices': [dict(x=v.co.x, y=v.co.z, z=-v.co.y) for v in me.vertices],
            'normals': [dict(x=v.normal.x, y=v.normal.z, z=-v.normal.y) for v in me.vertices],
            'uv': [dict(x=v.co.x, y=-v.co.y) for v in me.vertices],
            'triangles': [i for f in me.loop_triangles for i in f.vertices]}
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, separators=(',', ':')))
    stats.append(dict(name=str(path.relative_to(VAR)), triangles=len(me.loop_triangles)))
    bpy.data.objects.remove(clone, do_unlink=True)


def section(folder):
    grids = np.load(folder / 'grids.npz')
    info = json.loads((folder / 'section.json').read_text(encoding='utf8'))
    up = info['profile']['upSign']
    out = folder / 'Meshes'
    # outward directions: the band faces the path and the sky, the apron faces the valley and the sky
    pts = np.array([[p['x'], p['z']] for p in info['profile']['points']])
    t = pts[-1] - pts[0]; t /= np.linalg.norm(t); right = np.array([t[1], -t[0]]) * up  # toward uphill
    band_out = (-right[0] * .7, .7, -right[1] * .7)
    apron_out = (-right[0] * .6, .8, -right[1] * .6)
    face = grid_object('Band', grids['face'], band_out)
    for level, target in enumerate([42000, 13000, 2800]):
        export(face, out / ('Band_LOD%d.json' % level), target)
    apron = grid_object('Apron', grids['apron'], apron_out)
    for level, target in enumerate([9000, 3200, 900]):
        export(apron, out / ('Apron_LOD%d.json' % level), target)
    for key in grids.files:
        if key.startswith('trail_') or key.startswith('collision_'):
            ob = grid_object(key, grids[key], (0, 1, 0), smooth=False)
            export(ob, out / (key.capitalize() + '.json'))
    reset()


SOURCES = ROOT / 'Art/World/Compact/Rebuild/Highlands293/Sources'
STONE_SCANS = ['rock_07', 'rock_09', 'stone_01', 'rock_moss_set_01', 'rock_moss_set_02']   # CC0 Poly Haven (sources.json)
OUTCROP_SCANS = ['mountainside', 'rock_face_01', 'rock_face_02',
                 'namaqualand_cliff_02', 'namaqualand_boulder_04', 'namaqualand_boulder_02']  # Step 2: eroded blocks, rounded tors
# Step 2 realm stone sets (counts = highland_geo293.STONE_SETS): scans kept in their resting pose, tops cut by realm
REALM_STONES = {
    'broken': dict(count=12, sources=['namaqualand_stones_01', 'namaqualand_boulder_05', 'namaqualand_boulder_02', 'rock_07', 'rock_09'],
                   cut=(.05, .11), squash=.55, tilt=12, flat_first=False),
    'slab': dict(count=12, sources=['rock_07', 'rock_09', 'stone_01', 'rock_moss_set_01', 'rock_moss_set_02', 'namaqualand_boulder_02'],
                 cut=(.22, .32), squash=.12, tilt=4, flat_first=True),
}


def components(F, n):
    """Connected triangle groups (loose rocks inside a scanned set)."""
    parent = np.arange(n)
    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]; x = parent[x]
        return x
    for a, b, c in F:
        for u, v in ((a, b), (b, c)):
            ru, rv = find(u), find(v)
            if ru != rv:
                parent[ru] = rv
    roots = np.array([find(i) for i in range(n)])
    return [np.nonzero(roots == r)[0] for r in np.unique(roots)]


def scan(slug):
    """Unity-space (vertices, triangles) of every mesh in a CC0 FBX, in its natural resting orientation."""
    reset()
    bpy.ops.import_scene.fbx(filepath=str(SOURCES / slug / (slug + '_1k.fbx')))
    out = []
    import re
    for ob in [o for o in bpy.context.scene.objects if o.type == 'MESH' and not re.search(r'_LOD[1-9]', o.name)]:  # scans ship LOD1-3 duplicates
        me = ob.data; me.calc_loop_triangles(); M = ob.matrix_world
        W = np.array([tuple(M @ v.co) for v in me.vertices])
        U = np.c_[W[:, 0], W[:, 2], -W[:, 1]]                     # Blender Z-up → Unity Y-up
        F = np.array([t.vertices[:] for t in me.loop_triangles])
        for idx in components(F, len(U)):
            keep = np.isin(F, idx).all(1)
            if keep.sum() < 150:
                continue
            remap = -np.ones(len(U), int); remap[idx] = np.arange(len(idx))
            out.append((slug + '_' + ob.name, U[idx], remap[F[keep]]))
    reset()
    return out


def mesh_from(name, U, F, smooth=True):
    verts = [(float(p[0]), float(-p[2]), float(p[1])) for p in U]
    me = bpy.data.meshes.new(name); me.from_pydata(verts, [], [tuple(f) for f in F.tolist()]); me.update()
    ob = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(ob)
    bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5); bm.normal_update(); bm.to_mesh(me); bm.free(); me.update()
    for p in me.polygons:
        p.use_smooth = smooth
    return ob


def stones(count=16, seed=293):
    """Natural step stones: real scans kept in their resting pose, only the top 10–18% lightly flattened."""
    d = json.loads(BOULDER.read_text())
    parts = [('boulder_01', np.array([[v['x'], v['y'], v['z']] for v in d['vertices']], float), np.array(d['triangles'], int).reshape(-1, 3))]
    for slug in STONE_SCANS:
        parts += scan(slug)
    def flatness(p):
        size = p[1].max(0) - p[1].min(0); return size[1] / max(1e-6, max(size[0], size[2]))
    parts = sorted(parts, key=flatness)
    usable = [p for p in parts if flatness(p) < .95] or parts
    rng = random.Random(seed); report = []
    for k in range(count):
        name, V, F = usable[k % len(usable)]
        a = math.radians(rng.uniform(-8, 8)); b = math.radians(rng.uniform(-8, 8)); yaw = rng.uniform(0, 2 * math.pi)
        Rx = np.array([[1, 0, 0], [0, math.cos(a), -math.sin(a)], [0, math.sin(a), math.cos(a)]])
        Rz = np.array([[math.cos(b), -math.sin(b), 0], [math.sin(b), math.cos(b), 0], [0, 0, 1]])
        Ry = np.array([[math.cos(yaw), 0, math.sin(yaw)], [0, 1, 0], [-math.sin(yaw), 0, math.cos(yaw)]])
        P = (V - V.mean(0)) @ (Ry @ Rz @ Rx).T
        lo, hi = P.min(0), P.max(0); P = (P - (lo + hi) / 2) / (hi - lo); P[:, 1] -= .5
        cut = -rng.uniform(.10, .18) + rng.uniform(-.04, .04) * P[:, 0] + rng.uniform(-.04, .04) * P[:, 2]
        above = P[:, 1] > cut
        P[above, 1] = cut[above] + (P[above, 1] - cut[above]) * .35
        P[:, 1] = (P[:, 1] - P[:, 1].max()) / (P[:, 1].max() - P[:, 1].min())
        ob = mesh_from('Stone_%d' % k, P, F)
        for level, target in enumerate([520, 150]):
            export(ob, VAR / 'Stones' / ('Stone_%d_LOD%d.json' % (k, level)), target, sharp=55)
        report.append(dict(variant=k, source=name, flatness=round(flatness((name, V, F)), 3)))
        reset()
    (VAR / 'Stones' / 'sources.json').write_text(json.dumps(report, indent=1))


def realm_stones(kind, spec, seed=2935):
    """Broken: angular pieces with a shallow cut (still reads as found stone) · slab: the flattest pieces, broad flat top."""
    parts = []
    for slug in spec['sources']:
        parts += scan(slug)
    def flatness(p):
        size = p[1].max(0) - p[1].min(0); return size[1] / max(1e-6, max(size[0], size[2]))
    if spec['flat_first']:
        parts = sorted(parts, key=flatness)[:max(1, min(len(parts), 8))]
    rng = random.Random(seed + len(kind)); report = []
    for k in range(spec['count']):
        name, V, F = parts[k % len(parts)]
        a = math.radians(rng.uniform(-spec['tilt'], spec['tilt'])); b = math.radians(rng.uniform(-spec['tilt'], spec['tilt'])); yaw = rng.uniform(0, 2 * math.pi)
        Rx = np.array([[1, 0, 0], [0, math.cos(a), -math.sin(a)], [0, math.sin(a), math.cos(a)]])
        Rz = np.array([[math.cos(b), -math.sin(b), 0], [math.sin(b), math.cos(b), 0], [0, 0, 1]])
        Ry = np.array([[math.cos(yaw), 0, math.sin(yaw)], [0, 1, 0], [-math.sin(yaw), 0, math.cos(yaw)]])
        P = (V - V.mean(0)) @ (Ry @ Rz @ Rx).T
        lo, hi = P.min(0), P.max(0); P = (P - (lo + hi) / 2) / (hi - lo); P[:, 1] -= .5
        cut = -rng.uniform(*spec['cut']) + rng.uniform(-.03, .03) * P[:, 0] + rng.uniform(-.03, .03) * P[:, 2]
        above = P[:, 1] > cut
        P[above, 1] = cut[above] + (P[above, 1] - cut[above]) * spec['squash']
        P[:, 1] = (P[:, 1] - P[:, 1].max()) / (P[:, 1].max() - P[:, 1].min())
        ob = mesh_from('%s_%d' % (kind, k), P, F)
        for level, target in enumerate([520, 150]):
            export(ob, VAR / 'Stones' / ('%s_%d_LOD%d.json' % (kind, k, level)), target, sharp=55)
        report.append(dict(variant=k, source=name, flatness=round(flatness((name, V, F)), 3)))
        reset()
    (VAR / 'Stones' / ('%s_sources.json' % kind)).write_text(json.dumps(report, indent=1))


def cut_stones(count=10, seed=2936):
    """Old stone road blocks (Hwanggyeong): dressed granite with bevelled, worn and chipped edges and a dished tread."""
    from mathutils import Vector, noise
    rng = random.Random(seed); report = []
    for k in range(count):
        bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.bevel(bm, geom=list(bm.verts) + list(bm.edges), offset=rng.uniform(.035, .07), offset_type='OFFSET',
                        segments=2, profile=.5, affect='EDGES')
        bmesh.ops.subdivide_edges(bm, edges=list(bm.edges), cuts=2, use_grid_fill=True)
        corners = [Vector((rng.choice([-.5, .5]), rng.choice([-.5, .5]), .5)) for _ in range(rng.randint(1, 3))]
        off = Vector((rng.uniform(0, 50), rng.uniform(0, 50), rng.uniform(0, 50)))
        taper = rng.uniform(0., .05)
        for v in bm.verts:
            c = v.co
            c.x *= 1 - taper * (.5 - c.z); c.y *= 1 - taper * (.5 - c.z)      # slightly narrower at the buried base
            if c.z > .42:
                c.z -= .02 * max(0., 1 - (c.x * c.x + c.y * c.y) * 3.5)       # worn, dished tread
            for q in corners:                                               # chipped corners
                d = (c - q).length
                if d < .2:
                    c += (-q).normalized() * (.2 - d) * .55
            c += Vector((noise.noise(c * 3.1 + off), noise.noise(c * 3.1 + off * 1.7), noise.noise(c * 3.1 + off * 2.3))) * .012
        me = bpy.data.meshes.new('cut_%d' % k); bm.to_mesh(me); bm.free()
        V = np.array([tuple(v.co) for v in me.vertices]); lo, hi = V.min(0), V.max(0)
        for v in me.vertices:  # unit footprint, top at 0, bottom at -1 (Unity scales by the stone size)
            x, y, z = v.co
            v.co = ((x - (lo[0] + hi[0]) / 2) / (hi[0] - lo[0]), (y - (lo[1] + hi[1]) / 2) / (hi[1] - lo[1]), (z - hi[2]) / (hi[2] - lo[2]))
        ob = bpy.data.objects.new('cut_%d' % k, me); bpy.context.collection.objects.link(ob)
        for poly in me.polygons:
            poly.use_smooth = True
        for level, target in enumerate([520, 150]):
            export(ob, VAR / 'Stones' / ('cut_%d_LOD%d.json' % (k, level)), target, sharp=40)
        report.append(dict(variant=k, source='procedural dressed granite'))
        reset()
    (VAR / 'Stones' / 'cut_sources.json').write_text(json.dumps(report, indent=1))


def props():
    """Charred timber (Jeokro) from the CC0 dead_tree_trunk scan: a 3m log, base at the origin."""
    report = []
    for slug in ['dead_tree_trunk']:
        parts = scan(slug)
        if not parts:
            continue
        name, U, F = max(parts, key=lambda p: len(p[2]))
        U = U - np.array([(U[:, 0].min() + U[:, 0].max()) / 2, U[:, 1].min(), (U[:, 2].min() + U[:, 2].max()) / 2])
        ob = mesh_from(slug, U, F)
        for level, target in enumerate([3000, 700]):
            export(ob, VAR / 'Props' / ('%s_LOD%d.json' % (slug, level)), target, sharp=50)
        report.append(dict(slug=slug, size=[round(float(x), 2) for x in U.max(0) - U.min(0)]))
        reset()
    (VAR / 'Props').mkdir(parents=True, exist_ok=True)
    (VAR / 'Props' / 'sources.json').write_text(json.dumps(report, indent=1))


def outcrops():
    """Large CC0 cliff scans at their real scale for breaking the crest behind a band."""
    report = []
    for slug in OUTCROP_SCANS:
        parts = scan(slug)
        if not parts:
            continue
        name, U, F = max(parts, key=lambda p: len(p[2]))
        base = U.min(0); U = U - np.array([(U[:, 0].min() + U[:, 0].max()) / 2, base[1], (U[:, 2].min() + U[:, 2].max()) / 2])
        ob = mesh_from(slug, U, F)
        for level, target in enumerate([14000, 3600, 800]):
            export(ob, VAR / 'Outcrops' / ('%s_LOD%d.json' % (slug, level)), target, sharp=50)
        size = U.max(0) - U.min(0)
        report.append(dict(slug=slug, source=name, size=[round(float(x), 2) for x in size], triangles=int(len(F))))
        reset()
    (VAR / 'Outcrops' / 'sources.json').write_text(json.dumps(report, indent=1))


MESHY = ROOT / 'Art/World/Compact/Rebuild/Highlands293/Meshy'   # user-approved 2026-09-27 (ledger.json)


def meshy():
    """Supplementary Meshy masses: weld, base at the origin, unit height (Unity scales them), three LODs."""
    report = []
    for folder in sorted(p for p in MESHY.iterdir() if (p / 'preview/model_urls_glb.glb').exists()):
        reset()
        bpy.ops.import_scene.gltf(filepath=str(folder / 'preview/model_urls_glb.glb'))
        meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
        bpy.ops.object.select_all(action='DESELECT')
        for o in meshes:
            o.select_set(True)
        bpy.context.view_layer.objects.active = meshes[0]
        if len(meshes) > 1:
            bpy.ops.object.join()
        ob = bpy.context.view_layer.objects.active
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        V = np.array([tuple(v.co) for v in ob.data.vertices]); lo, hi = V.min(0), V.max(0)
        centre = np.array([(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2]]); height = hi[2] - lo[2]
        for v in ob.data.vertices:
            v.co = tuple((np.array(tuple(v.co)) - centre) / height)
        bm = bmesh.new(); bm.from_mesh(ob.data); bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-5)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces)); bm.to_mesh(ob.data); bm.free(); ob.data.update()
        for poly in ob.data.polygons:
            poly.use_smooth = True
        for level, target in enumerate([12000, 3500, 900]):
            export(ob, VAR / 'Meshy' / ('%s_LOD%d.json' % (folder.name, level)), target, sharp=48)
        size = (hi - lo) / height
        report.append(dict(name=folder.name, proportions=[round(float(size[0]), 3), round(float(size[1]), 3), 1.0]))
    (VAR / 'Meshy').mkdir(parents=True, exist_ok=True)
    (VAR / 'Meshy' / 'sources.json').write_text(json.dumps(report, indent=1))


import sys
only = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []  # e.g. -- sections stones
reset()
if not only or 'sections' in only:
    for folder in sorted(p for p in VAR.iterdir() if (p / 'grids.npz').exists()):
        section(folder)
if not only or 'stones' in only:
    stones()
    for kind, spec in REALM_STONES.items():
        realm_stones(kind, spec)
    cut_stones()
if not only or 'assets' in only:
    outcrops()
    meshy()
    props()
(VAR / 'mesh-report.json').write_text(json.dumps(stats, indent=1))
print('Highlands293 meshes:', len(stats))
