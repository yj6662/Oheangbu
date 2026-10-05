# #308 organ-art: procedural low-poly geometry helpers (bmesh). All builders work in a part-local frame:
# +Z = outward from the body surface, +Y = "up along the body", origin = attachment point. Units = metres.
import math
import bmesh
from mathutils import Vector, Matrix, noise


def new_bm():
    return bmesh.new()


def _pole_ring(bm, center):
    return [bm.verts.new(center)]


def lathe(bm, profile, segs, sx=1.0, sy=1.0, offset=Vector((0, 0, 0)), twist=0.0):
    """Revolve (r, h) pairs around local +Z. r == 0 makes a pole. Returns created faces."""
    rings = []
    for k, (rad, h) in enumerate(profile):
        if rad <= 1e-6:
            rings.append([bm.verts.new(offset + Vector((0, 0, h)))])
            continue
        ring = []
        for i in range(segs):
            a = 2 * math.pi * i / segs + twist * k
            ring.append(bm.verts.new(offset + Vector((rad * math.cos(a) * sx, rad * math.sin(a) * sy, h))))
        rings.append(ring)
    faces = []
    for k in range(len(rings) - 1):
        a, b = rings[k], rings[k + 1]
        if len(a) == 1 and len(b) == 1:
            continue
        if len(a) == 1:
            for i in range(segs):
                faces.append(bm.faces.new((a[0], b[(i + 1) % segs], b[i])))
        elif len(b) == 1:
            for i in range(segs):
                faces.append(bm.faces.new((a[i], a[(i + 1) % segs], b[0])))
        else:
            for i in range(segs):
                j = (i + 1) % segs
                faces.append(bm.faces.new((a[i], a[j], b[j], b[i])))
    return faces


def tube(bm, pts, sides, radius_fn, side_vecs=None, up_vecs=None, cap=True, closed=False, phase=0.0):
    """Sweep an ellipse (a, b) = radius_fn(i, t) along pts. side_vecs/up_vecs give per-point frame axes
    (ellipse axis a along side, b along up); otherwise a parallel-transport frame is used."""
    n = len(pts)
    tangents = []
    for i in range(n):
        if closed:
            d = pts[(i + 1) % n] - pts[(i - 1) % n]
        else:
            d = pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]
        tangents.append(d.normalized())
    if side_vecs is None:
        ref = Vector((0, 0, 1)) if abs(tangents[0].z) < 0.9 else Vector((1, 0, 0))
        side_vecs, up_vecs = [], []
        s = tangents[0].cross(ref).normalized()
        for i in range(n):
            s = (s - tangents[i] * s.dot(tangents[i])).normalized()
            side_vecs.append(s.copy())
            up_vecs.append(tangents[i].cross(s).normalized())
    rings = []
    for i in range(n):
        t = i / (n - 1 if not closed else n)
        a, b = radius_fn(i, t)
        ring = []
        for k in range(sides):
            ang = 2 * math.pi * k / sides + phase
            ring.append(bm.verts.new(pts[i] + side_vecs[i] * (a * math.cos(ang)) + up_vecs[i] * (b * math.sin(ang))))
        rings.append(ring)
    segs = n if closed else n - 1
    for i in range(segs):
        r0, r1 = rings[i], rings[(i + 1) % n]
        for k in range(sides):
            l = (k + 1) % sides
            bm.faces.new((r0[k], r0[l], r1[l], r1[k]))
    if cap and not closed:
        for ring, tip_dir, idx in ((rings[0], -tangents[0], 0), (rings[-1], tangents[-1], n - 1)):
            a, b = radius_fn(idx, idx / (n - 1))
            c = sum((v.co for v in ring), Vector()) / len(ring) + tip_dir * min(a, b) * 0.6
            pole = bm.verts.new(c)
            for k in range(sides):
                l = (k + 1) % sides
                bm.faces.new((ring[k], ring[l], pole))
    return rings


def ico(bm, radius, subdiv, matrix):
    before = set(bm.verts)
    try:
        bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius, matrix=matrix)
    except TypeError:
        bmesh.ops.create_icosphere(bm, subdivisions=subdiv, diameter=radius * 2, matrix=matrix)
    return [v for v in bm.verts if v not in before]


def uvsphere(bm, radius, u, v, matrix):
    before = set(bm.verts)
    try:
        bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=radius, matrix=matrix)
    except TypeError:
        bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, diameter=radius * 2, matrix=matrix)
    return [v for v in bm.verts if v not in before]


def displace(verts, center, amp, freq, seed=0.0, axis_scale=Vector((1, 1, 1))):
    """Radial Perlin displacement of a vert group away from center (deterministic)."""
    off = Vector((seed * 13.7, seed * 7.3, seed * 3.1))
    for v in verts:
        d = v.co - center
        if d.length < 1e-9:
            continue
        nval = noise.noise(v.co * freq + off)
        v.co += Vector((d.x * axis_scale.x, d.y * axis_scale.y, d.z * axis_scale.z)).normalized() * amp * nval


def displace_n(bm, verts, amp, freq, seed=0.0):
    """Perlin displacement along vertex normals (normals recalculated per connected island first)."""
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    off = Vector((seed * 13.7, seed * 7.3, seed * 3.1))
    moves = [(v, v.normal * amp * noise.noise(v.co * freq + off)) for v in verts]
    for v, d in moves:
        v.co += d


def xform(verts, matrix):
    for v in verts:
        v.co = matrix @ v.co


def finish(bm):
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()


def tri_count(bm):
    return sum(len(f.verts) - 2 for f in bm.faces)


def frame(normal, up_hint):
    """3x3 with columns (x, y, z): z = normal, y = up_hint orthogonalised, x = y × z."""
    z = normal.normalized()
    y = (up_hint - z * up_hint.dot(z))
    if y.length < 1e-6:
        y = Vector((0, 1, 0)) - z * z.y
    y.normalize()
    x = y.cross(z).normalized()
    return Matrix((x, y, z)).transposed()


def rot_z(deg):
    return Matrix.Rotation(math.radians(deg), 4, 'Z')


def project_uv(me):
    """Deterministic spherical UV around the part origin (UV0 must exist for the overlay shader inputs)."""
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    uv = me.uv_layers.active.data
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            u = 0.5 + math.atan2(co.x, co.z if abs(co.z) > 1e-9 else 1e-9) / (2 * math.pi)
            r = co.length if co.length > 1e-9 else 1e-9
            v = 0.5 + math.asin(max(-1.0, min(1.0, co.y / r))) / math.pi
            uv[li].uv = (u, v)
