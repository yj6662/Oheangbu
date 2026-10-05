"""#308 enemy run: a second, stricter arm-in-body test, shared by the builder (enemyrun308.py) and the re-measure (recheck_run308.py).

rig308.py (the walk repair's module, imported, not edited) tests the vertices whose arm weight is >= 0.9 with the nearest face normal
and a ray parity vote. Two things slip through: the sleeve vertices around the elbow (weights below 0.9), and deep points where the
nearest normal misleads. Here:
  inside      generalised winding number (Jacobson 2013) of every forearm / hand vertex (weight >= arm_weight_min) against the
              surface of everything but that arm: ~1 inside a closed surface, ~0 outside, no normals involved
  ring        smallest distance to the body of the arm vertices rig308 leaves out of its clearance set
  self-test   the three spine joints must read inside, a point one metre beside the hips outside; a result without it is not a result
Every threshold comes from enemyrun308.json 'recheck'. CPU only (numpy + Blender's BVH), no render."""
import math
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

SIDES = ('Left', 'Right')


def winding(points, tri):
    out = np.zeros(len(points))
    for i in range(0, len(points), 64):
        p = points[i:i + 64][:, None, :]; a = tri[None, :, 0] - p; b = tri[None, :, 1] - p; c = tri[None, :, 2] - p
        la = np.linalg.norm(a, axis=2); lb = np.linalg.norm(b, axis=2); lc = np.linalg.norm(c, axis=2)
        det = np.einsum('ijk,ijk->ij', a, np.cross(b, c)); den = la * lb * lc + np.einsum('ijk,ijk->ij', a, b) * lc + np.einsum('ijk,ijk->ij', b, c) * la + np.einsum('ijk,ijk->ij', c, a) * lb
        out[i:i + 64] = (2 * np.arctan2(det, den)).sum(1) / (4 * math.pi)
    return out


class ArmInside:
    def __init__(self, rig, rc):
        self.rig = rig; self.rc = rc
        arm_w = {s: rig.w(s + 'Arm', s + 'ForeArm', s + 'Hand') for s in SIDES}; fore_w = {s: rig.w(s + 'ForeArm', s + 'Hand') for s in SIDES}
        self.fore = {s: np.where(fore_w[s] >= rc['arm_weight_min'])[0] for s in SIDES}
        self.ring = {s: np.setdiff1d(np.where((arm_w[s] >= rc['arm_weight_min']) & (fore_w[s] > rc['ring_fore_weight_min']))[0], rig.S[s]['test']) for s in SIDES}
        # surface for one arm = every triangle without a vertex of THAT arm (the other arm stays in: as closed as the model allows)
        self.surf = {s: rig.tris[(arm_w[s][rig.tris] < rc['surface_arm_weight_max']).all(1)] for s in SIDES}

    def self_test(self, V, joints):
        """joints: name -> world position. -> per side the winding numbers of the control points and whether they read as they must."""
        out = {}; ok = True
        for s in SIDES:
            w = winding(np.array([joints[n] for n in ('Spine02', 'Spine01', 'Spine')] + [np.array(joints['Hips']) + np.array([1.0, 0, 0])]), V[self.surf[s]])
            out[s] = {'Spine02 joint': round(float(w[0]), 3), 'Spine01 joint': round(float(w[1]), 3), 'Spine joint': round(float(w[2]), 3), '1 m beside the hips': round(float(w[3]), 3)}
            ok = ok and float(w[:3].min()) > self.rc['winding_inside'] and abs(float(w[3])) < self.rc['winding_outside_max']
        return out, bool(ok)

    def measure(self, V, with_test_set=False):
        """-> per side: vertices inside, largest winding number, ring clearance (cm), optionally the clearance of rig308's own set."""
        body = self.rig.body_tris; bvh = BVHTree.FromPolygons([Vector(p) for p in V], [tuple(int(i) for i in t) for t in body], all_triangles=True); out = {}
        def clear(idx):
            dmin = None
            for i in idx:
                hit = bvh.find_nearest(Vector(V[i]), .25)
                if hit[0] is not None and (dmin is None or hit[3] < dmin): dmin = hit[3]
            return None if dmin is None else dmin * 100
        for s in SIDES:
            w = winding(V[self.fore[s]], V[self.surf[s]])
            out[s] = {'inside': int((w > self.rc['winding_inside']).sum()), 'winding_max': float(w.max()), 'ring_clear_cm': clear(self.ring[s])}
            if with_test_set: out[s]['test_set_clear_cm'] = clear(self.rig.S[s]['test'])
        return out


def fold(rows):
    """Per-frame measure() results -> the cycle's worst per side."""
    out = {}
    for s in SIDES:
        ring = [r[s]['ring_clear_cm'] for r in rows if r[s]['ring_clear_cm'] is not None]; ts = [r[s]['test_set_clear_cm'] for r in rows if r[s].get('test_set_clear_cm') is not None]
        out[s] = {'inside_vertices_max': max(r[s]['inside'] for r in rows), 'inside_frames': sum(1 for r in rows if r[s]['inside']), 'winding_max': round(max(r[s]['winding_max'] for r in rows), 3),
                  'ring_clear_min_cm': round(min(ring), 2) if ring else None}
        if ts: out[s]['test_set_clear_min_cm'] = round(min(ts), 2)
    return out
