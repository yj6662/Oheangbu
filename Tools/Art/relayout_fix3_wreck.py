#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""#308 relayout fix 3 - the fallen palanquin wreck (fix2 item L3, switched on for the D308-22 bake). Offline geometry, READ ONLY.

The set's transform tree comes from the scene YAML (local TRS), the meshes are the REAL FBX files of the palanquin (cabin, roof,
wheel) and of the chest; primitives (the tow beam, rods) are unit cubes. Matrices are composed in full (a child that is turned
under the non-uniformly scaled body is sheared exactly as Unity draws it). apply_op mirrors ContentSeat308.Fix2.F2Pose.
"""
import copy, math, re, sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import relayout_fix3_lib as L            # noqa: E402
import meshbounds308 as MB               # noqa: E402
S, B, PROJECT, h = L.S, L.B, L.PROJECT, L.h

WRECK = 'CheongrimRoadStories264/PalanquinWreck264'
PAL = PROJECT / 'Assets/_Project/Art/World/WorldMacro/Palanquin'
WRECK_MESH = {'Cabin': PAL / 'Models/Palanquin_Cabin.fbx', 'Roof': PAL / 'Models/Palanquin_Roof.fbx', 'Wheel': PAL / 'Models/Palanquin_Wheel.fbx', 'DetachedWheel': PAL / 'Models/Palanquin_Wheel.fbx',
              'SM_M_WoodenBox': PROJECT / 'Assets/HwaseongHaenggung/Meshes/Others/Props/SM_M_WoodenBox.fbx'}
UNIT_CUBE = (np.array([[sx, sy, sz] for sx in (-.5, .5) for sy in (-.5, .5) for sz in (-.5, .5)], float),
             np.array([[0, 1, 3], [0, 3, 2], [4, 6, 7], [4, 7, 5], [0, 4, 5], [0, 5, 1], [2, 3, 7], [2, 7, 6], [0, 2, 6], [0, 6, 4], [1, 5, 7], [1, 7, 3]]))


def fbx_mesh(path):
    """(positions (n, 3) in the Unity mesh-local frame = FBX vertex coordinates x the import scale, triangles) of the largest geometry.
    Valid when the model node carries no geometric transform - the caller checks the box against the BoxCollider the builder fitted."""
    f = MB.Fbx(path); unit, up, geo, model, link, parent = f.scene()
    meta = Path(str(path) + '.meta'); gscale = 1.0; file_scale = True
    if meta.exists():
        t = meta.read_text(encoding='utf-8', errors='replace')
        m = re.search(r'globalScale: ([-\d.eE+]+)', t); gscale = float(m.group(1)) if m else 1.0
        m = re.search(r'useFileScale: (\d)', t); file_scale = (m.group(1) == '1') if m else True
    k = gscale * (unit / 100.0 if file_scale else 1.0)
    objs = f.find(f.top, 'Objects')[0][2]; best = None
    for n in objs:
        if n[0] != 'Geometry': continue
        v = f.find(n[2], 'Vertices'); pi = f.find(n[2], 'PolygonVertexIndex')
        if not v or not pi: continue
        V = np.array(v[0][1][0], float).reshape(-1, 3) * k; tris = []; poly = []
        for i in pi[0][1][0]:
            if i < 0:
                poly.append(-i - 1)
                for j in range(1, len(poly) - 1): tris.append((poly[0], poly[j], poly[j + 1]))
                poly = []
            else: poly.append(i)
        if best is None or len(V) > len(best[0]): best = (V, np.array(tris, int))
    return best


def trs(pos, rot, scale):
    x, y, z, w = rot
    Rm = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)], [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)], [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    Mx = np.eye(4); Mx[:3, :3] = Rm * np.array(scale, float)[None, :]; Mx[:3, 3] = pos
    return Mx


def euler_q(e):
    """Unity Quaternion.Euler(x, y, z) = qy * qx * qz."""
    x, y, z = (math.radians(a) / 2 for a in e)
    qx = (math.sin(x), 0.0, 0.0, math.cos(x)); qy = (0.0, math.sin(y), 0.0, math.cos(y)); qz = (0.0, 0.0, math.sin(z), math.cos(z))
    return B.qmul(qy, B.qmul(qx, qz))


def q_angle(a, b):
    d = abs(sum(x * y for x, y in zip(a, b))); return math.degrees(2 * math.acos(max(-1.0, min(1.0, d))))


class Wreck:
    def __init__(self, alias='main'):
        sk = S.SceneK(PROJECT / S.SCENES[alias]); sc = sk.sc; self.sha = sk.sha[:12]
        root = sk.get(WRECK)
        if root is None: raise SystemExit('no %s in %s' % (WRECK, alias))
        self.nodes = {}; self.order = []

        def walk(t, key, parent):
            n = sc.nodes[t]; comps = sk.comps.get(n.go, [])
            box = next(((i.get('center') or (0, 0, 0), i['size']) for (cl, _, i) in comps if cl == 65 and i.get('size')), None)
            self.nodes[key] = dict(parent=parent, name=n.name, pos=list(n.pos), rot=tuple(n.rot), scale=tuple(n.scale), box=box, has_mesh=any(cl == 33 for (cl, _, i) in comps))
            self.order.append(key)
            kids = sc.children_of(t); names = [sc.nodes[c].name for c in kids]
            for c in kids:
                nm = sc.nodes[c].name; seg = nm + ('#%d' % [x for x in kids if sc.nodes[x].name == nm].index(c) if names.count(nm) > 1 else '')
                walk(c, key + '/' + seg, key)
        walk(root, WRECK, None)
        self.mesh = {}; self.mesh_err = {}
        for nm, p in WRECK_MESH.items():
            try: self.mesh[nm] = fbx_mesh(p)
            except Exception as e: self.mesh[nm] = None; self.mesh_err[nm] = repr(e)

    def copy(self):
        w = Wreck.__new__(Wreck); w.sha = self.sha; w.order = list(self.order); w.mesh = self.mesh; w.mesh_err = self.mesh_err; w.nodes = copy.deepcopy(self.nodes); return w

    def world(self, key):
        n = self.nodes[key]; Mx = trs(n['pos'], n['rot'], n['scale'])
        return Mx if n['parent'] is None else self.world(n['parent']) @ Mx

    def world_q(self, key):
        chain = []; k = key
        while k: chain.append(k); k = self.nodes[k]['parent']
        q = (0.0, 0.0, 0.0, 1.0)
        for k in reversed(chain): q = B.qmul(q, self.nodes[k]['rot'])
        return q

    def subtree(self, key): return [k for k in self.order if k == key or k.startswith(key + '/')]

    def mesh_of(self, key):
        nm = self.nodes[key]['name']
        if self.mesh.get(nm) is not None: return self.mesh[nm]
        return UNIT_CUBE if self.nodes[key]['has_mesh'] else None

    def tris(self, key):
        out = []
        for k in self.subtree(key):
            m = self.mesh_of(k)
            if m is None: continue
            Mx = self.world(k); W = m[0] @ Mx[:3, :3].T + Mx[:3, 3]; out.append(W[m[1]])
        return np.concatenate(out) if out else np.zeros((0, 3, 3))

    def renderer_bounds(self, key, skip=()):
        """Renderer.bounds of the subtree = AABB of each mesh's local AABB corners in world (what PieceBounds measures).
        skip = keys whose subtrees are left out (ContentSeat308.Fix2 F2OwnGap: the children a world op carried away)."""
        lo = np.full(3, np.inf); hi = np.full(3, -np.inf)
        for k in self.subtree(key):
            if any(k == s_ or k.startswith(s_ + '/') for s_ in skip): continue
            m = self.mesh_of(k)
            if m is None: continue
            a = m[0].min(0); b = m[0].max(0); C = np.array([[x, y, z] for x in (a[0], b[0]) for y in (a[1], b[1]) for z in (a[2], b[2])])
            Mx = self.world(k); W = C @ Mx[:3, :3].T + Mx[:3, 3]; lo = np.minimum(lo, W.min(0)); hi = np.maximum(hi, W.max(0))
        return lo, hi

    def collider_corners(self, key):
        out = []
        for k in self.subtree(key):
            b = self.nodes[k]['box']
            if b is None: continue
            ce, sz = b; C = np.array([[ce[0] + sx * sz[0] / 2, ce[1] + sy * sz[1] / 2, ce[2] + sz_ * sz[2] / 2] for sx in (-1, 1) for sy in (-1, 1) for sz_ in (-1, 1)])
            Mx = self.world(k); out.append((k, C @ Mx[:3, :3].T + Mx[:3, 3]))
        return out

    def world_pos(self, key): return self.world(key)[:3, 3].copy()

    def set_world(self, key, pos=None, q=None):
        """t.rotation = q / t.position = pos as Unity resolves them into local values under the parent."""
        n = self.nodes[key]
        if q is not None:
            pq = self.world_q(n['parent']) if n['parent'] else (0.0, 0.0, 0.0, 1.0)
            n['rot'] = B.qmul((-pq[0], -pq[1], -pq[2], pq[3]), q)
        if pos is not None:
            P = self.world(n['parent']) if n['parent'] else np.eye(4)
            lp = np.linalg.solve(P, np.array([pos[0], pos[1], pos[2], 1.0])); n['pos'] = [float(lp[0]), float(lp[1]), float(lp[2])]

    def apply_op(self, op):
        key = op['key']; n = self.nodes[key]; info = {}
        if op.get('euler') is not None: n['rot'] = euler_q(op['euler'])
        if op.get('local_xz') is not None: n['pos'] = [op['local_xz'][0], n['pos'][1], op['local_xz'][1]]
        w = op.get('world')
        if w:
            if w.get('euler') is not None: self.set_world(key, q=euler_q(w['euler']))
            if w.get('xz') is not None: p = self.world_pos(key); self.set_world(key, pos=(w['xz'][0], p[1], w['xz'][1]))
        if op.get('seat') is not None:
            lo, hi = self.renderer_bounds(key); g = h((lo[0] + hi[0]) / 2, (lo[2] + hi[2]) / 2); dy = (g + op['seat']) - lo[1]
            p = self.world_pos(key); self.set_world(key, pos=(p[0], p[1] + dy, p[2])); info['seat_shift'] = float(dy); info['ground'] = g
        return info
