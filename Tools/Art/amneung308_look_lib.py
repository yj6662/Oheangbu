"""amneung308_look_lib.py - shared geometry of the 암릉 rock band LOOK pass (#308 relayout fix 2, item L1).

Used by   Tools/Unity/Stage308_relayout_fix2/amneung/amneung308_look_derive.py   (builds Data/amneung308_look.json)
          Tools/Art/amneung308_look_dry.py                                        (offline proof + silhouette picture)
Reads only. The reach / height-field model is the one of Tools/Art/amneung308_dry.py (imported, not copied): the cores, the stone and
the pads are NOT changed by the look pass, so its proofs A / B stay what they were; this file adds what the visible rocks need:
  * the real Unity meshes (serialized Mesh .asset: m_VertexData / m_IndexBuffer)                                   [O exact]
  * a rock = mesh + position + quaternion + non-uniform scale (Unity TRS: world = p + R (S v)); tilt = lean of local +y toward an azimuth
  * top / bottom raster of every rock on the 0.25 m grid of the dry (per-rock windows, so one rock can be re-fitted alone)
  * the 국-deck running-jump reach height over every cell (the ballistic of the dry, as a height instead of a yes / no)
  * the skyline of the band from an eye (perspective: elevation of the rock tops brought to the band plane)
"""
import os, sys, re, json, math
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__)).replace('\\', '/')
ROOT = os.path.normpath(os.path.join(HERE, '..', '..')).replace('\\', '/')
sys.path.insert(0, HERE)
import amneung308_dry as dry

PROJECT = ROOT + '/Oheangbu/'
_MESH = {}


def unity_mesh(asset_path):
    """vertices (n, 3) and triangles (m, 3) of a serialized Unity Mesh asset (uncompressed, positions = channel 0, float32 x 3, stream 0)"""
    if asset_path in _MESH: return _MESH[asset_path]
    t = open(PROJECT + asset_path, encoding='utf-8').read()
    vc = int(re.search(r'm_VertexCount: (\d+)', t).group(1)); ds = int(re.search(r'm_DataSize: (\d+)', t).group(1))
    ch = re.findall(r'- stream: (\d+)\s+offset: (\d+)\s+format: (\d+)\s+dimension: (\d+)', t)
    if not ch or ch[0] != ('0', '0', '0', '3'): raise SystemExit('mesh %s: channel 0 is not float32 x 3 at offset 0' % asset_path)
    if any(c[0] != '0' for c in ch if c[3] != '0'): raise SystemExit('mesh %s: more than one vertex stream' % asset_path)
    if int(re.search(r'm_MeshCompression: (\d+)', t).group(1)) != 0: raise SystemExit('mesh %s is compressed' % asset_path)
    raw = bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)', t).group(1))
    if len(raw) != ds or ds % vc: raise SystemExit('mesh %s: vertex data size' % asset_path)
    V = np.frombuffer(raw, '<f4').reshape(vc, ds // vc // 4)[:, :3].astype(np.float64)
    fmt = int(re.search(r'm_IndexFormat: (\d)', t).group(1)); ib = bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)', t).group(1))
    I = np.frombuffer(ib, '<u4' if fmt == 1 else '<u2').reshape(-1, 3).astype(np.int64)
    if I.max() >= vc: raise SystemExit('mesh %s: index out of range' % asset_path)
    _MESH[asset_path] = (V, I)
    return V, I


def mesh_table(L):
    return {m['key']: m for m in L['meshes']}


# ---------------------------------------------------------------- rotation (Unity quaternion [x, y, z, w]; the algebra is the usual one)
def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx, aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz)


def qmat(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def q_axis(ax, deg):
    a = math.radians(deg) * 0.5; s = math.sin(a); n = math.sqrt(ax[0] ** 2 + ax[1] ** 2 + ax[2] ** 2)
    return (ax[0] / n * s, ax[1] / n * s, ax[2] / n * s, math.cos(a))


def q_yaw_dip(yaw_deg, dip_deg, dip_az_deg, roll_deg=0.0):
    """yaw about +y (Unity: local +x -> (cos, 0, -sin)), then an optional roll about the turned +x, then the lean: local +y toward the
    horizontal azimuth dip_az (0 = +z = north, 90 = +x = east) by dip_deg"""
    q = q_axis((0, 1, 0), yaw_deg)
    if roll_deg:
        a = math.radians(yaw_deg); q = qmul(q_axis((math.cos(a), 0, -math.sin(a)), roll_deg), q)
    if dip_deg:
        a = math.radians(dip_az_deg); d = (math.sin(a), 0.0, math.cos(a))
        q = qmul(q_axis((d[2], 0.0, -d[0]), dip_deg), q)          # axis = y x d : turns +y toward d
    n = math.sqrt(sum(c * c for c in q))
    return tuple(c / n for c in q)


def world_verts(r, V):
    R = qmat(r['q']); S = np.asarray(r['scale'], float)
    return np.array([r['x'], r['y'], r['z']]) + (V * S) @ R.T


# ---------------------------------------------------------------- raster
class Sub:
    """top / bottom of one rock over its window of the grid"""
    __slots__ = ('i0', 'i1', 'j0', 'j1', 'top', 'bot')


def raster(g, W, T):
    """highest and lowest point of a triangle mesh (world vertices W, triangles T) over the grid cells it covers"""
    s = Sub()
    s.j0 = max(0, int((W[:, 0].min() - g.x0) / g.c)); s.j1 = min(g.nx, int((W[:, 0].max() - g.x0) / g.c) + 1)
    s.i0 = max(0, int((W[:, 2].min() - g.z0) / g.c)); s.i1 = min(g.nz, int((W[:, 2].max() - g.z0) / g.c) + 1)
    if s.j1 <= s.j0 or s.i1 <= s.i0:
        s.i1 = s.i0; s.j1 = s.j0; s.top = np.zeros((0, 0)); s.bot = np.zeros((0, 0)); return s
    top = np.full((s.i1 - s.i0, s.j1 - s.j0), -np.inf); bot = np.full(top.shape, np.inf)
    X, Y, Z = W[:, 0], W[:, 1], W[:, 2]
    for t in T:
        x, z, y = X[t], Z[t], Y[t]
        j0 = max(s.j0, int((x.min() - g.x0) / g.c)); j1 = min(s.j1, int((x.max() - g.x0) / g.c) + 1)
        i0 = max(s.i0, int((z.min() - g.z0) / g.c)); i1 = min(s.i1, int((z.max() - g.z0) / g.c) + 1)
        if j1 <= j0 or i1 <= i0: continue
        den = (z[1] - z[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (z[0] - z[2])
        if abs(den) < 1e-12: continue
        GX = g.xs[j0:j1][None, :]; GZ = g.zs[i0:i1][:, None]
        l1 = ((z[1] - z[2]) * (GX - x[2]) + (x[2] - x[1]) * (GZ - z[2])) / den
        l2 = ((z[2] - z[0]) * (GX - x[2]) + (x[0] - x[2]) * (GZ - z[2])) / den
        l3 = 1 - l1 - l2
        m = (l1 >= -1e-6) & (l2 >= -1e-6) & (l3 >= -1e-6)
        if not m.any(): continue
        yy = l1 * y[0] + l2 * y[1] + l3 * y[2]
        a = top[i0 - s.i0:i1 - s.i0, j0 - s.j0:j1 - s.j0]; a[m] = np.maximum(a[m], yy[m])
        b = bot[i0 - s.i0:i1 - s.i0, j0 - s.j0:j1 - s.j0]; b[m] = np.minimum(b[m], yy[m])
    s.top = top; s.bot = bot
    return s


def rock_raster(g, r, meshes, lod):
    m = meshes[r['mesh']]; V, T = unity_mesh(m['lod0' if lod == 0 else 'lod1'])
    return raster(g, world_verts(r, V), T)


def combine(g, subs):
    top = np.full(g.ground.shape, -np.inf)
    for s in subs:
        if s.top.size: a = top[s.i0:s.i1, s.j0:s.j1]; np.maximum(a, s.top, out=a)
    return top


def base_rock_as_look(r):
    """a rock of the base data (unit mesh, yaw only) in the look form"""
    return dict(id=r['id'], role='base', mesh='massif', x=r['x'], y=r['y'], z=r['z'], q=list(q_yaw_dip(r['yaw'], 0, 0)), scale=list(r['scale']), ground_y=r['ground_y'])


# ---------------------------------------------------------------- band line
class Line:
    def __init__(s, pts):
        s.P = np.asarray(pts, float); s.seg = np.hypot(*(s.P[1:] - s.P[:-1]).T); s.cum = np.concatenate([[0], np.cumsum(s.seg)]); s.L = float(s.cum[-1])

    def at(s, st):
        """point, tangent (unit, W -> E) and left normal (north side) at a station; outside [0, L] the end segments run on straight"""
        if st <= 0: i, t = 0, st / s.seg[0]
        elif st >= s.L: i = len(s.seg) - 1; t = 1 + (st - s.L) / s.seg[i]
        else:
            i = int(min(len(s.seg) - 1, np.searchsorted(s.cum, st, side='right') - 1)); t = (st - s.cum[i]) / s.seg[i]
        a, b = s.P[i], s.P[i + 1]; d = (b - a) / s.seg[i]
        return a + (b - a) * t, d, np.array([-d[1], d[0]])

    def station(s, x, z):
        """station and signed offset (+ = left = north) of a point; the end segments run on straight"""
        best = None
        for i in range(len(s.seg)):
            a, b = s.P[i], s.P[i + 1]; t = ((x - a[0]) * (b[0] - a[0]) + (z - a[1]) * (b[1] - a[1])) / s.seg[i] ** 2
            if not (i == 0 and t < 0) and not (i == len(s.seg) - 1 and t > 1): t = min(1.0, max(0.0, t))
            p = a + (b - a) * t; d = math.hypot(x - p[0], z - p[1]); n = np.array([-(b[1] - a[1]), b[0] - a[0]]) / s.seg[i]
            if best is None or d < best[0]: best = (d, s.cum[i] + t * s.seg[i], (x - p[0]) * n[0] + (z - p[1]) * n[1])
        return best[1], best[2]


def ground_at(h4, x, z):
    return float(dry.surface(h4, np.array([x], float), np.array([z], float))[0][0])


# ---------------------------------------------------------------- reach height of a 국 deck + running jump (dry.ballistic as a height)
def reach_height(g, H, src):
    """the highest feet height a body gets to over every cell: standing on a 국 deck (surface + GUK) raised on a `src` cell, with a
    running jump (dry.RUN_SPEED, apex dry.JUMP). -inf where no source is within dry.BALLISTIC_MAX_M. A landing dry.STEP under an
    edge still gets up, so a top is 'in reach' when it is <= this + dry.STEP."""
    best = np.full(H.shape, -np.inf); c = g.c; R = int(dry.BALLISTIC_MAX_M / c); t_up = dry.V_JUMP / dry.GRAVITY
    Hs = np.where(src, H, -np.inf); nz, nx = H.shape
    for di in range(-R, R + 1):
        for dj in range(-R, R + 1):
            d = c * math.hypot(di, dj)
            if d > dry.BALLISTIC_MAX_M: continue
            t = d / dry.RUN_SPEED; rise = dry.GUK + (dry.JUMP if t <= t_up else dry.V_JUMP * t - 0.5 * dry.GRAVITY * t * t)
            sh = np.full(H.shape, -np.inf)
            sh[max(0, di):nz + min(0, di), max(0, dj):nx + min(0, dj)] = Hs[max(0, -di):nz + min(0, -di), max(0, -dj):nx + min(0, -dj)]
            np.maximum(best, sh + rise, out=best)
    return best


# ---------------------------------------------------------------- skyline from an eye
def skyline(g, h4, top, line, eye, s0, s1, step=0.5):
    """Top line of the rocks as an eye sees it, as a height over the ground of the band line, every `step` m of station.
    For every rock cell: azimuth from the eye -> the station whose line point has that azimuth; elevation -> the height the sight line
    has over that line point. NaN where no rock shows over the terrain horizon between the eye and the line point.
    Returns stations, heights, or (None, None) when the eye looks along the band (azimuth not monotonic along the line)."""
    S = np.arange(s0, s1 + 1e-9, step); P = np.array([line.at(s)[0] for s in S])
    az = np.unwrap(np.arctan2(P[:, 0] - eye[0], P[:, 1] - eye[2])); dist = np.hypot(P[:, 0] - eye[0], P[:, 1] - eye[2])
    dz = np.diff(az)
    if not (np.all(dz > 1e-5) or np.all(dz < -1e-5)): return None, None
    m = np.isfinite(top); cx = g.X[m]; cz = g.Z[m]; cy = top[m]
    caz = np.arctan2(cx - eye[0], cz - eye[2]); caz = caz + np.round((az.mean() - caz) / (2 * math.pi)) * 2 * math.pi
    cd = np.hypot(cx - eye[0], cz - eye[2]); tan = (cy - eye[1]) / np.maximum(cd, 0.5)
    order = np.argsort(az); k = np.interp(caz, az[order], np.arange(len(S))[order], left=-9, right=-9)
    ok = k > -1; k = np.rint(k[ok]).astype(int); tan = tan[ok]
    best = np.full(len(S), -np.inf); np.maximum.at(best, k, tan)
    gy = dry.surface(h4, P[:, 0], P[:, 1])[0]
    out = np.full(len(S), np.nan)
    for i in range(len(S)):
        if not np.isfinite(best[i]): out[i] = 0.0; continue
        # terrain horizon along the sight line up to the band
        ts = np.linspace(0.02, 1.0, 60); hx = eye[0] + (P[i, 0] - eye[0]) * ts; hz = eye[2] + (P[i, 1] - eye[2]) * ts
        hor = ((dry.surface(h4, hx, hz)[0] - eye[1]) / (dist[i] * ts)).max()
        if best[i] <= hor: continue                                  # hidden behind the slope
        out[i] = eye[1] + best[i] * dist[i] - gy[i]
    return S, out


# ---------------------------------------------------------------- a small renderer (perspective, painter by triangle depth)
def render_view(draw, box, eye, look, fov_deg, tris_ground, tris_rock, shade_rock=(150, 150, 146), outline=(40, 40, 40), mark=None):
    """box = (x0, y0, w, h) in the image. tris_* = arrays (n, 3, 3) of world triangles. Ground first, then rocks, far to near."""
    x0, y0, w, h = box
    f = np.array(look, float) - np.array(eye, float); f /= np.linalg.norm(f)
    r = np.cross([0, 1, 0], f); r /= np.linalg.norm(r); u = np.cross(f, r)
    fl = (h * 0.5) / math.tan(math.radians(fov_deg) * 0.5)

    def proj(T):
        d = T - np.array(eye, float); zc = d @ f; return x0 + w * 0.5 + (d @ r) / np.maximum(zc, 1e-6) * fl, y0 + h * 0.5 - (d @ u) / np.maximum(zc, 1e-6) * fl, zc
    L = np.array([0.35, 0.8, -0.5]); L /= np.linalg.norm(L)
    items = []
    for T, kind in ((tris_ground, 0), (tris_rock, 1)):
        if T is None or not len(T): continue
        px, py, zc = proj(T)
        n = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0]); n /= np.maximum(np.linalg.norm(n, axis=1), 1e-12)[:, None]
        lit = np.abs(n @ L)
        keep = (zc.min(1) > 0.3) & (px.max(1) > x0) & (px.min(1) < x0 + w) & (py.max(1) > y0) & (py.min(1) < y0 + h)
        for i in np.nonzero(keep)[0]:
            items.append((float(zc[i].mean()), kind, [(float(px[i, j]), float(py[i, j])) for j in range(3)], float(lit[i])))
    items.sort(key=lambda a: -a[0])
    for zc, kind, pts, lit in items:
        pts = [(min(x0 + w, max(x0, p[0])), min(y0 + h, max(y0, p[1]))) for p in pts]
        if kind == 0:
            v = int(206 + 26 * lit); draw.polygon(pts, fill=(v, v - 2, v - 12))
        else:
            v = lit; c = tuple(int(sr * (0.45 + 0.6 * v)) for sr in shade_rock); draw.polygon(pts, fill=c)
    if mark:
        for (p, col, rad) in mark:
            px, py, zc = proj(np.array([[p]], float))
            if zc[0, 0] > 0.3 and x0 < px[0, 0] < x0 + w and y0 < py[0, 0] < y0 + h:
                draw.ellipse([px[0, 0] - rad, py[0, 0] - rad, px[0, 0] + rad, py[0, 0] + rad], outline=col, width=2)
    draw.rectangle([x0, y0, x0 + w, y0 + h], outline=outline)


def ground_tris(h4, x0, x1, z0, z1):
    """the 4 m tile triangles of the height field in a window (the split the dry uses: (00,10,01) / (10,01,11))"""
    js = np.arange(int(x0 // 4), int(x1 // 4) + 1); iz = np.arange(int(z0 // 4), int(z1 // 4) + 1)
    out = []
    for i in iz:
        for j in js:
            a = (j * 4.0, h4[i, j], i * 4.0); b = (j * 4.0 + 4, h4[i, j + 1], i * 4.0); c = (j * 4.0, h4[i + 1, j], i * 4.0 + 4); d = (j * 4.0 + 4, h4[i + 1, j + 1], i * 4.0 + 4)
            out.append((a, b, c)); out.append((b, c, d))
    return np.array(out, float)


# ---------------------------------------------------------------- z-buffer picture (look revision 3: the stills' eyes, rock by rock)
def subdivide(T, n):
    """every triangle of T (m, 3, 3) into n * n smaller ones (the same plane)"""
    out = []
    a, b, c = T[:, 0], T[:, 1], T[:, 2]
    for i in range(n):
        for j in range(n - i):
            p0 = a + (b - a) * (i / n) + (c - a) * (j / n); p1 = a + (b - a) * ((i + 1) / n) + (c - a) * (j / n); p2 = a + (b - a) * (i / n) + (c - a) * ((j + 1) / n)
            out.append(np.stack([p0, p1, p2], 1))
            if j < n - i - 1:
                p3 = a + (b - a) * ((i + 1) / n) + (c - a) * ((j + 1) / n); out.append(np.stack([p1, p3, p2], 1))
    return np.concatenate(out)


def box_tris(cx, cz, yaw_deg, sx, sz, y0, y1):
    """the 12 triangles of an upright box (yaw as Unity: local +x -> (cos, 0, -sin))"""
    a = math.radians(yaw_deg); ux, uz, vx, vz = math.cos(a), -math.sin(a), math.sin(a), math.cos(a)
    cs = [(cx + ux * p * sx * 0.5 + vx * q * sz * 0.5, cz + uz * p * sx * 0.5 + vz * q * sz * 0.5) for p, q in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    out = []
    for k in range(4):
        (xa, za), (xb, zb) = cs[k], cs[(k + 1) % 4]
        out += [[(xa, y0, za), (xb, y0, zb), (xb, y1, zb)], [(xa, y0, za), (xb, y1, zb), (xa, y1, za)]]
    for y in (y0, y1):
        out += [[(cs[0][0], y, cs[0][1]), (cs[1][0], y, cs[1][1]), (cs[2][0], y, cs[2][1])], [(cs[0][0], y, cs[0][1]), (cs[2][0], y, cs[2][1]), (cs[3][0], y, cs[3][1])]]
    return np.array(out, float)


def render_z(width, height, eye, look, fov_deg, tris, tid, rgb, near=0.25, sky=(203, 209, 200), ink=(36, 34, 32), ss=2, light=(0.35, 0.8, -0.5)):
    """A z-buffer picture: flat shading per triangle, an ink line where the object id changes or the depth jumps inside one object.
    tris (n, 3, 3) world triangles, tid (n,) object id >= 0, rgb (n, 3) base colour. fov = vertical (Unity). Triangles that reach
    behind the near plane are dropped (cut the ground small near the eye). Returns the picture (height, width, 3) uint8, the id
    buffer and the depth buffer at ss x the size, and the projector p(world point) -> (x, y, depth) in picture pixels."""
    Wd, Hd = width * ss, height * ss
    eye = np.asarray(eye, float); f = np.asarray(look, float) - eye; f /= np.linalg.norm(f)
    r = np.cross([0.0, 1.0, 0.0], f); r /= np.linalg.norm(r); u = np.cross(f, r)
    fl = (Hd * 0.5) / math.tan(math.radians(fov_deg) * 0.5)
    tris = np.asarray(tris, float); tid = np.asarray(tid); rgb = np.asarray(rgb, float)
    d = tris - eye; zc = d @ f
    ok = zc.min(1) > near; tris, tid, rgb, d, zc = tris[ok], tid[ok], rgb[ok], d[ok], zc[ok]
    px = Wd * 0.5 + (d @ r) / zc * fl; py = Hd * 0.5 - (d @ u) / zc * fl
    ok = (px.max(1) >= 0) & (px.min(1) <= Wd) & (py.max(1) >= 0) & (py.min(1) <= Hd)
    tris, tid, rgb, zc, px, py = tris[ok], tid[ok], rgb[ok], zc[ok], px[ok], py[ok]
    nrm = np.cross(tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0]); nrm /= np.maximum(np.linalg.norm(nrm, axis=1), 1e-12)[:, None]
    L = np.asarray(light, float); L /= np.linalg.norm(L); lit = np.abs(nrm @ L)
    col = np.clip(rgb * (0.42 + 0.62 * lit)[:, None], 0, 255)
    x0 = np.clip(np.floor(px.min(1)).astype(int), 0, Wd - 1); x1 = np.clip(np.floor(px.max(1)).astype(int), 0, Wd - 1)
    y0 = np.clip(np.floor(py.min(1)).astype(int), 0, Hd - 1); y1 = np.clip(np.floor(py.max(1)).astype(int), 0, Hd - 1)
    size = np.maximum(x1 - x0, y1 - y0) + 1
    zbuf = np.full(Hd * Wd, np.inf); ibuf = np.full(Hd * Wd, -1, np.int64); cbuf = np.tile(np.asarray(sky, float), (Hd * Wd, 1))
    den = (py[:, 1] - py[:, 2]) * (px[:, 0] - px[:, 2]) + (px[:, 2] - px[:, 1]) * (py[:, 0] - py[:, 2])
    good = np.abs(den) > 1e-9

    def batch(sel, K):
        n = len(sel)
        if not n: return
        gx = x0[sel][:, None] + np.arange(K)[None, :]; gy = y0[sel][:, None] + np.arange(K)[None, :]
        PX = (gx + 0.5)[:, None, :]; PY = (gy + 0.5)[:, :, None]
        ax, bx, cx = px[sel, 0][:, None, None], px[sel, 1][:, None, None], px[sel, 2][:, None, None]
        ay, by, cy = py[sel, 0][:, None, None], py[sel, 1][:, None, None], py[sel, 2][:, None, None]
        dn = den[sel][:, None, None]
        l1 = ((by - cy) * (PX - cx) + (cx - bx) * (PY - cy)) / dn
        l2 = ((cy - ay) * (PX - cx) + (ax - cx) * (PY - cy)) / dn
        l3 = 1.0 - l1 - l2
        inside = (l1 >= -1e-4) & (l2 >= -1e-4) & (l3 >= -1e-4) & (gx[:, None, :] <= x1[sel][:, None, None]) & (gy[:, :, None] <= y1[sel][:, None, None])
        if not inside.any(): return
        iz = l1 / zc[sel, 0][:, None, None] + l2 / zc[sel, 1][:, None, None] + l3 / zc[sel, 2][:, None, None]
        ti, yi, xi = np.nonzero(inside)
        z = 1.0 / iz[ti, yi, xi]; pix = gy[ti, yi] * Wd + gx[ti, xi]
        keep = z < zbuf[pix]; ti, z, pix = ti[keep], z[keep], pix[keep]
        o = np.argsort(-z, kind='stable'); ti, z, pix = ti[o], z[o], pix[o]
        zbuf[pix] = z; ibuf[pix] = tid[sel][ti]; cbuf[pix] = col[sel][ti]

    prev = 0
    for K in (2, 4, 8, 16, 32, 64, 128):
        sel = np.nonzero(good & (size > prev) & (size <= K))[0]; prev = K; step = max(1, int(3e6 // (K * K)))
        for k in range(0, len(sel), step): batch(sel[k:k + step], K)
    for i in np.nonzero(good & (size > prev))[0]: batch(np.array([i]), int(size[i]))
    zb = zbuf.reshape(Hd, Wd); ib = ibuf.reshape(Hd, Wd); img = cbuf.reshape(Hd, Wd, 3)
    edge = np.zeros((Hd, Wd), bool); zf = np.where(np.isfinite(zb), zb, 1e6)
    for ax_ in (0, 1):
        a_ = ib if ax_ == 0 else ib.T; z_ = zf if ax_ == 0 else zf.T; e_ = edge if ax_ == 0 else edge.T
        dif = (a_[1:] != a_[:-1]) | ((np.abs(z_[1:] - z_[:-1]) > 0.035 * np.minimum(z_[1:], z_[:-1]) + 0.12) & (a_[1:] > 0))
        e_[1:] |= dif; e_[:-1] |= dif & (z_[:-1] > z_[1:])
    if ss > 1:
        e2 = edge.copy(); e2[1:] |= edge[:-1]; e2[:, 1:] |= edge[:, :-1]; edge = e2
    img[edge] = np.asarray(ink, float)
    if ss > 1: img = img.reshape(height, ss, width, ss, 3).mean((1, 3))

    def proj(p):
        q = np.asarray(p, float) - eye; z = q @ f
        return (Wd * 0.5 + (q @ r) / max(z, 1e-6) * fl) / ss, (Hd * 0.5 - (q @ u) / max(z, 1e-6) * fl) / ss, z
    return np.clip(img, 0, 255).astype(np.uint8), ib, zb, proj


# ---------------------------------------------------------------- the editor's V3 for look rocks (Amneung308.Look.cs LookVerifyRocks)
def low_sink(h4, r, meshes, lods=('lod1', 'lod0')):
    """Per LOD: (sink, x, y, z) of the LOWEST vertex of the rock's mesh (the first smallest world y, as the editor loop finds it) -
    sink = ground under THAT vertex minus the vertex. The editor asks sink >= checks.sunk_under_m on LOD1 against the physical
    ground; here the ground is the 1b height field (it differed from the physical ground by up to 0.05 m in earlier runs [M])."""
    out = {}
    for k in lods:
        V, _ = unity_mesh(meshes[r['mesh']][k]); Wv = world_verts(r, V); i = int(np.argmin(Wv[:, 1]))
        out[k] = (float(ground_at(h4, Wv[i, 0], Wv[i, 2]) - Wv[i, 1]), float(Wv[i, 0]), float(Wv[i, 1]), float(Wv[i, 2]))
    return out


def scale_stretch(r):
    """scale y over the geometric mean of scale x and z: 1 = the mesh's own proportions (what the granite's 3-axis projection and the
    mesh's own joints are stretched by)"""
    return float(r['scale'][1] / math.sqrt(r['scale'][0] * r['scale'][2]))


def box_distance(x, z, st):
    """horizontal distance of a point to the stone box footprint (0 inside)"""
    u, v = dry.box_uv(np.array([x], float), np.array([z], float), st['x'], st['z'], st['yaw_box'])
    return float(np.hypot(max(abs(float(u[0])) - st['half_m'], 0.0), max(abs(float(v[0])) - st['half_m'], 0.0))), float(u[0])


# ---------------------------------------------------------------- the editor's V10 sampling (Amneung308.Look.cs LookVerifyFit), shared by the derive and the dry
def verify_cells(D, h4, cell):
    """the cells of the EDITOR verify V10: per core its own local grid, u along the core, v across, cell centres from -len / 2 + cell / 2;
    a cell under two cores is counted twice (3473 cells, not the 3073 of the world grid). Rows: x, z, ground, core top"""
    pts = []
    for c in D['cores']:
        a = math.radians(c['yaw']); ux, uz = math.cos(a), -math.sin(a); vx, vz = math.sin(a), math.cos(a)
        for u in np.arange(-c['size'][0] * 0.5 + cell * 0.5, c['size'][0] * 0.5, cell):
            for v in np.arange(-c['size'][2] * 0.5 + cell * 0.5, c['size'][2] * 0.5, cell):
                x = c['x'] + ux * u + vx * v; z = c['z'] + uz * u + vz * v
                pts.append((x, z, ground_at(h4, x, z), c['top_y']))
    return np.array(pts)


def verify_faces(D, h4, cell):
    """the face points of the EDITOR verify V10 (second line): per core, both long faces, u from -len / 2 every cell up to len / 2 + 1e-3"""
    pts = []
    for c in D['cores']:
        a = math.radians(c['yaw']); ux, uz = math.cos(a), -math.sin(a); vx, vz = math.sin(a), math.cos(a)
        for side in (-1, 1):
            for u in np.arange(-c['size'][0] * 0.5, c['size'][0] * 0.5 + 1e-3, cell):
                x = c['x'] + ux * u + vx * side * c['size'][2] * 0.5; z = c['z'] + uz * u + vz * side * c['size'][2] * 0.5
                pts.append((x, z, ground_at(h4, x, z), 0.0))
    return np.array(pts)


def exact_top_rock(pts, r, meshes, lod):
    """highest point of ONE rock's real triangles straight over every point (the editor's point-in-triangle test, no raster); -inf where none"""
    top = np.full(len(pts), -np.inf); X = pts[:, 0]; Z = pts[:, 1]
    V, T = unity_mesh(meshes[r['mesh']]['lod0' if lod == 0 else 'lod1']); Wv = world_verts(r, V)
    m = (X >= Wv[:, 0].min()) & (X <= Wv[:, 0].max()) & (Z >= Wv[:, 2].min()) & (Z <= Wv[:, 2].max())
    if not m.any(): return top
    idx = np.nonzero(m)[0]; px = X[idx][None, :]; pz = Z[idx][None, :]
    a = Wv[T[:, 0]]; b = Wv[T[:, 1]]; c = Wv[T[:, 2]]
    den = ((b[:, 2] - c[:, 2]) * (a[:, 0] - c[:, 0]) + (c[:, 0] - b[:, 0]) * (a[:, 2] - c[:, 2]))
    ok = np.abs(den) >= 1e-9; a, b, c, den = a[ok], b[ok], c[ok], den[ok]
    l1 = ((b[:, 2] - c[:, 2])[:, None] * (px - c[:, 0][:, None]) + (c[:, 0] - b[:, 0])[:, None] * (pz - c[:, 2][:, None])) / den[:, None]
    l2 = ((c[:, 2] - a[:, 2])[:, None] * (px - c[:, 0][:, None]) + (a[:, 0] - c[:, 0])[:, None] * (pz - c[:, 2][:, None])) / den[:, None]
    l3 = 1 - l1 - l2; inside = (l1 >= -1e-5) & (l2 >= -1e-5) & (l3 >= -1e-5)
    top[idx] = np.where(inside, l1 * a[:, 1][:, None] + l2 * b[:, 1][:, None] + l3 * c[:, 1][:, None], -np.inf).max(0)
    return top


def exact_top(pts, rocks, meshes, lod):
    top = np.full(len(pts), -np.inf)
    for r in rocks: np.maximum(top, exact_top_rock(pts, r, meshes, lod), out=top)
    return top


# ---------------------------------------------------------------- review of look.3 (S1 / S2): the invisible core against the visible top line; the core faces beside the deck
def verify_columns(D, cell):
    """for every cell of verify_cells (same order) the index of its column = one place along one core (all the cells across its thickness);
    and per column the index of its core"""
    col = []; core = []; n = 0
    for ci, c in enumerate(D['cores']):
        nv = len(np.arange(-c['size'][2] * 0.5 + cell * 0.5, c['size'][2] * 0.5, cell))
        for _ in np.arange(-c['size'][0] * 0.5 + cell * 0.5, c['size'][0] * 0.5, cell):
            col += [n] * nv; core.append(ci); n += 1
    return np.array(col), np.array(core)


def top_line(D, pts, col, top):
    """per column of verify_columns, from the exact rock tops over the verify cells (`top`, -inf where no rock): rows
    (visible top line over the ground, core top over that top line, core top over the ground, distance to the stone box, u in the stone box frame: + = east).
    Top line = the highest rock over the column (the ground where there is none); ground = mean of the column's cells."""
    st = D['stone']; t = np.where(np.isfinite(top), top, pts[:, 2]); n = int(col.max()) + 1
    su, sv = dry.box_uv(pts[:, 0], pts[:, 1], st['x'], st['z'], st['yaw_box'])
    bd = np.hypot(np.maximum(np.abs(su) - st['half_m'], 0.0), np.maximum(np.abs(sv) - st['half_m'], 0.0))
    tl = np.full(n, -np.inf); np.maximum.at(tl, col, t)
    cnt = np.bincount(col, minlength=n).astype(float); g = np.bincount(col, weights=pts[:, 2], minlength=n) / cnt; u = np.bincount(col, weights=su, minlength=n) / cnt
    ct = np.zeros(n); ct[col] = pts[:, 3]; d = np.full(n, np.inf); np.minimum.at(d, col, bd)
    return np.stack([tl - g, ct - tl, ct - g, d, u], 1)


def deck_cores(D):
    """the cores that stand ON the deck: one end inside the stone box. Rows (core, end): end = +1 when the core's local +x end is the one on the deck"""
    st = D['stone']; out = []
    for c in D['cores']:
        a = math.radians(c['yaw'])
        for end in (1, -1):
            x = c['x'] + math.cos(a) * end * c['size'][0] * 0.5; z = c['z'] - math.sin(a) * end * c['size'][0] * 0.5
            u, v = dry.box_uv(np.array([x], float), np.array([z], float), st['x'], st['z'], st['yaw_box'])
            if abs(float(u[0])) < st['half_m'] and abs(float(v[0])) < st['half_m']: out.append((c, end))
    return out


def deck_face_points(D, along_m, step_m, over_m):
    """Points on the two LONG faces of each core that stands on the deck: the first along_m from its deck end, from the deck top to
    + over_m (what a body on the deck can touch: standing height + a jump). Rows (core id, 'north' | 'south', points (n, 3))."""
    st = D['stone']; ys = np.arange(st['top_y'] + step_m * 0.5, st['top_y'] + over_m + 1e-6, step_m); al = np.arange(step_m * 0.5, along_m + 1e-6, step_m); out = []
    for c, end in deck_cores(D):
        a = math.radians(c['yaw']); ax = np.array([math.cos(a), 0.0, -math.sin(a)]); az = np.array([math.sin(a), 0.0, math.cos(a)])
        e0 = np.array([c['x'], 0.0, c['z']]) + ax * end * c['size'][0] * 0.5; din = -ax * end
        for side in (1, -1):
            P = np.array([e0 + din * s_ + az * side * c['size'][2] * 0.5 + np.array([0.0, y, 0.0]) for s_ in al for y in ys])
            out.append((c['id'], 'north' if az[2] * side > 0 else 'south', P))
    return out


def inside_rock(P, r, meshes, lod):
    """True where a point (n, 3) lies inside the closed mesh of ONE rock: the crossings of the ray straight up from the point, odd = inside"""
    out = np.zeros(len(P), bool)
    V, T = unity_mesh(meshes[r['mesh']]['lod0' if lod == 0 else 'lod1']); Wv = world_verts(r, V)
    m = (P[:, 0] >= Wv[:, 0].min()) & (P[:, 0] <= Wv[:, 0].max()) & (P[:, 2] >= Wv[:, 2].min()) & (P[:, 2] <= Wv[:, 2].max()) & (P[:, 1] <= Wv[:, 1].max())
    if not m.any(): return out
    idx = np.nonzero(m)[0]; px = P[idx, 0][None, :]; pz = P[idx, 2][None, :]; py = P[idx, 1][None, :]
    a = Wv[T[:, 0]]; b = Wv[T[:, 1]]; c = Wv[T[:, 2]]
    den = ((b[:, 2] - c[:, 2]) * (a[:, 0] - c[:, 0]) + (c[:, 0] - b[:, 0]) * (a[:, 2] - c[:, 2]))
    ok = np.abs(den) >= 1e-12; a, b, c, den = a[ok], b[ok], c[ok], den[ok]
    l1 = ((b[:, 2] - c[:, 2])[:, None] * (px - c[:, 0][:, None]) + (c[:, 0] - b[:, 0])[:, None] * (pz - c[:, 2][:, None])) / den[:, None]
    l2 = ((c[:, 2] - a[:, 2])[:, None] * (px - c[:, 0][:, None]) + (a[:, 0] - c[:, 0])[:, None] * (pz - c[:, 2][:, None])) / den[:, None]
    l3 = 1 - l1 - l2; hit = (l1 >= 0) & (l2 >= 0) & (l3 >= 0)
    y = l1 * a[:, 1][:, None] + l2 * b[:, 1][:, None] + l3 * c[:, 1][:, None]
    out[idx] = ((hit & (y > py)).sum(0) % 2) == 1
    return out


def deck_reach_cells(D, pts, within_m):
    """the editor's V10 cells (verify_cells rows) within within_m of the stone box, west and east of it: index arrays"""
    st = D['stone']; su, sv = dry.box_uv(pts[:, 0], pts[:, 1], st['x'], st['z'], st['yaw_box'])
    near = np.hypot(np.maximum(np.abs(su) - st['half_m'], 0.0), np.maximum(np.abs(sv) - st['half_m'], 0.0)) <= within_m
    return dict(west=np.nonzero(near & (su < 0))[0], east=np.nonzero(near & (su > 0))[0])


def deck_reach_count(D, pts, top):
    """review S1, 'off the deck': of the given cells (rows of verify_cells) those where the rock top (-inf = none) is under the core top AND under
    what a body reaches from a 국 deck cast ON the stone deck with a jump and a step (deck top + GUK + JUMP + STEP): the visible rock looks like
    something to step onto, the invisible core stands over it"""
    t = np.where(np.isfinite(top), top, pts[:, 2])
    return int(((t < pts[:, 3] - 1e-3) & (t <= D['stone']['top_y'] + dry.GUK + dry.JUMP + dry.STEP)).sum())


def inside_any(P, rocks, meshes, lod):
    out = np.zeros(len(P), bool)
    for r in rocks: out |= inside_rock(P, r, meshes, lod)
    return out
