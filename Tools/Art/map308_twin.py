# -*- coding: utf-8 -*-
"""SPEC-MAP-OVERHAUL-308 track A - the numpy twin of the map composite: concept sheets and the legibility numbers.

    python Tools/resource_guard.py --wait
    python Tools/Art/map308_twin.py            -> Art/UI308/Map/  (file 10 of the asset contract)

    map308_roads_abc.png        road notation A (ruled line) / B (brush band, the design) / C (stamped band), same minimap window, 1:1 + 2x
    map308_notation_sheet.png   every stroke class at the three outdoor zoom bands, the patterns, the values, both icon sheets, the frames
    map308_mini_mock.png        the HUD minimap (294 x 210 window in its frame) at six fixed windows, 1:1 + 2x
    map308_full_mock.png        the unfolded map page (lacquer board, sheet, legend), current view and whole view
    map308_legibility.json      AC-O6 / O7 / O9 measured on the composites (1080p and 1440p; roads: every road window + the unfolded map)

The twin reads ONLY the bundle in the stage folder (one source for both maps) and composites it the way the stage shaders
draw it: terrain texture + world-anchored patterns + shoreline from the distance field + brush bands with the atlas (the
strip shader's mip clamp included) + icons, all under the #308 walked-land edge (fog_edge = MapFog308_Edge of the stage
shaders: a CRISP edge that always lies inside the walked cells, an ink rim on its inner side); an unwalked pixel is the flat
wash x grain and nothing else. MapFog308_Edge is drawn in the formula of the LIVE MapFog308.cginc (--cginc <file> for a stage
file): the threshold formula with the bundle's crispEdge numbers, or formula F of #308 map fix 2 with the cginc's own
`#define MAPFOG308_E_*` numbers (see fog_edge). The
direction sheet (map308_mock.py) draws its map surfaces and icons through render() / glyph_rgba() of this file, so the sheet
and these renders are one notation. It is the reference picture for the runtime track, not the runtime. Colours are
sRGB-coded 'over', contrast = WCAG relative luminance ratio.
"""
import argparse, json, math, re, struct, sys, time, zlib
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, str(Path(__file__).resolve().parent))
import map308_lib as M

FONT_SERIF = M.A / 'Art/UI/UI304/Fonts/NotoSerifKR-800.ttf'
FONT_SANS = M.A / 'Art/UI/UI304/Fonts/NotoSansKR-Regular.ttf'
PAPER, WASH, INK, CINN = (M.hexc(c) for c in (M.PAPER, M.WASH, M.INK, M.CINNABAR))
VEIL = M.hexc('#0F0F0E')
WINDOWS = [  # six fixed windows (world x, z of the player, a little off the place so the mark is not under the brush tip)
    dict(id='inn', ko='금표 주막 앞', at=(3040, 2306)), dict(id='village', ko='청림 벌목마을 어귀', at=(2792, 2166)),
    dict(id='trail', ko='금표 암릉 산길', at=(2976, 2900)), dict(id='south_gate', ko='황경 남문 밖', at=(2024, 2496)),
    dict(id='bridge', ko='대전 앞 다리', at=(2160, 3300)), dict(id='pass', ko='고개 검문 아래', at=(2290, 1862)),
]
WALK_START = (3283.0, 1909.0)        # the mock's walked ground: everything within 96 m of the roads within this walk distance
WALK_REACH_M = 5600.0
MINI_MARGIN = 12                    # design px shown round the minimap frame (the kit's north piece stands 11 px above it)


def font(path, size):
    try:
        return ImageFont.truetype(str(path), size)
    except Exception:
        return ImageFont.load_default()


# ---------------------------------------------------------------------------------------------------------------------
class Bundle:
    def __init__(self, d=M.OUT):
        d = Path(d); self.dir = d
        self.note = json.loads((d / 'map308_notation.json').read_text(encoding='utf-8'))
        t = np.asarray(Image.open(d / 'map308_terrain.png').convert('RGBA'), float) / 255.0
        self.terrain = [t[::-1]]                                           # south row first
        for _ in range(2): self.terrain.append(M.down(self.terrain[-1], 2))
        p = np.asarray(Image.open(d / 'map308_pattern.png').convert('RGBA'), float) / 255.0
        self.pattern = [p]
        for _ in range(4): self.pattern.append(M.down(self.pattern[-1], 2))
        a = np.asarray(Image.open(d / 'map308_stroke_atlas.png').convert('RGBA'), float) / 255.0
        self.rows = []
        for r in range(8):
            row = a[r * 64:(r + 1) * 64]; mips = [row]
            for _ in range(4): mips.append(M.down(mips[-1], 2))
            self.rows.append(mips)
        self.icons = {s: np.asarray(Image.open(d / f'map308_icons_{s}.png').convert('RGBA'), float) / 255.0 for s in ('L', 'S')}
        self.regions = np.frombuffer((d / 'map308_reveal_regions.bytes').read_bytes(), np.uint8).reshape(M.FOG_H, M.FOG_W)
        self.strokes = self._read_strokes((d / 'map308_strokes.bytes').read_bytes())
        self.classes = {c['class']: c for c in self.note['strokeClasses']}
        self.glyph = {g['id']: g['cell'] for g in self.note['glyphs']}
        self.atlas_rows = {r['row']: r for r in self.note['strokeAtlas']['rows']}

    @staticmethod
    def _read_strokes(b):
        assert b[:4] == b'MS08' and zlib.crc32(b[:-4]) & 0xFFFFFFFF == struct.unpack('<I', b[-4:])[0]
        ver, x0, z0, x1, z1, binm, bx, bz, ns, npnt = struct.unpack('<I4ffHHII', b[4:40])
        st = np.frombuffer(b, dtype=[('cls', 'u1'), ('rank', 'u1'), ('state', '<u2'), ('first', '<u4'), ('count', '<u4'), ('hw', '<f4'),
                                     ('len', '<f4'), ('hash', '<u4')], count=ns, offset=40)
        pt = np.frombuffer(b, dtype=[('x', '<f4'), ('z', '<f4'), ('d', '<f4'), ('p', 'u1'), ('f', 'u1'), ('r', '<u2')], count=npnt, offset=40 + 24 * ns)
        return dict(strokes=st, points=pt)

    def band(self, mpp_ref):
        for i, z in enumerate(self.note['zoomBands']):
            if z['maxMetresPerPx'] is None or mpp_ref <= z['maxMetresPerPx']: return i
        return 3

    def stroke_style(self, cls, rank, band):
        c = self.classes[int(cls)]
        if 'ranks' in c:
            r = next(x for x in c['ranks'] if x['rank'] == int(rank))
            if int(rank) > c['minRankByBand'][band]: return None
            return dict(width=r['widthPx'][band], row=r['atlasRowByBand'][band], alpha=r['inkAlpha'], knock=c['knockoutPx'])
        return dict(width=c['widthPx'][band], row=c['atlasRowByBand'][band], alpha=c['inkAlpha'], knock=c['knockoutPx'])


def bilinear(img, fx, fy, wrap=False):
    H, W = img.shape[:2]
    x0 = np.floor(fx).astype(int); y0 = np.floor(fy).astype(int); tx = fx - x0; ty = fy - y0
    if wrap:
        x1 = (x0 + 1) % W; y1 = (y0 + 1) % H; x0 %= W; y0 %= H
    else:
        x1 = np.clip(x0 + 1, 0, W - 1); y1 = np.clip(y0 + 1, 0, H - 1); x0 = np.clip(x0, 0, W - 1); y0 = np.clip(y0, 0, H - 1)
    if img.ndim == 3: tx = tx[..., None]; ty = ty[..., None]
    return (img[y0, x0] * (1 - tx) + img[y0, x1] * tx) * (1 - ty) + (img[y1, x0] * (1 - tx) + img[y1, x1] * tx) * ty


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a + 1e-12), 0, 1)
    return t * t * (3 - 2 * t)


# ---------------------------------------------------------------------------------------------------------------------
# the mock's walked ground (a deterministic stand-in for a save file)
# ---------------------------------------------------------------------------------------------------------------------
def mock_walk(B, reach=WALK_REACH_M, all_walked=False):
    walked = np.zeros((M.FOG_H, M.FOG_W), bool)
    if all_walked:
        walked[:] = True; return walked
    st, pt = B.strokes['strokes'], B.strokes['points']
    roads = [np.stack([pt['x'][s['first']:s['first'] + s['count']], pt['z'][s['first']:s['first'] + s['count']]], 1).astype(float)
             for s in st if s['cls'] in (7, 8, 9)]
    # breadth-first over the road network by walked metres from WALK_START (deterministic: fixed stroke order)
    dist = [np.full(len(r), np.inf) for r in roads]
    start = np.asarray(WALK_START)
    for k, r in enumerate(roads):
        d = np.hypot(*(r - start).T); i = int(np.argmin(d))
        if d[i] < 20: dist[k][i] = 0.0
    for _ in range(60):
        changed = False
        for k, r in enumerate(roads):
            seg = np.hypot(*np.diff(r, axis=0).T)
            for i in range(1, len(r)):
                v = dist[k][i - 1] + seg[i - 1]
                if v < dist[k][i]: dist[k][i] = v; changed = True
            for i in range(len(r) - 2, -1, -1):
                v = dist[k][i + 1] + seg[i]
                if v < dist[k][i]: dist[k][i] = v; changed = True
        for k, r in enumerate(roads):                                    # hop across junctions (points within 8 m)
            for e in (0, len(r) - 1):
                for k2, r2 in enumerate(roads):
                    if k2 == k: continue
                    d = np.hypot(*(r2 - r[e]).T); j = int(np.argmin(d))
                    if d[j] < 8.0:
                        if dist[k2][j] + d[j] < dist[k][e]: dist[k][e] = dist[k2][j] + d[j]; changed = True
                        if dist[k][e] + d[j] < dist[k2][j]: dist[k2][j] = dist[k][e] + d[j]; changed = True
        if not changed: break
    cx = (np.arange(M.FOG_W) + .5) * M.FOG_CELL; cz = (np.arange(M.FOG_H) + .5) * M.FOG_CELL
    CX, CZ = np.meshgrid(cx, cz)
    for k, r in enumerate(roads):
        D = M.resample(r, 16.0); s_src = M.cum_len(r); s_new = np.linspace(0, s_src[-1], len(D))
        dd = np.interp(s_new, s_src, np.where(np.isfinite(dist[k]), dist[k], 1e9))
        for (x, z), dv in zip(D, dd):
            if dv > reach: continue
            j0, j1 = max(0, int((x - 96) // 32)), min(M.FOG_W, int((x + 96) // 32) + 1)
            i0, i1 = max(0, int((z - 96) // 32)), min(M.FOG_H, int((z + 96) // 32) + 1)
            walked[i0:i1, j0:j1] |= np.hypot(CX[i0:i1, j0:j1] - x, CZ[i0:i1, j0:j1] - z) <= 96.0
    return walked


# ---------------------------------------------------------------------------------------------------------------------
# composite
# ---------------------------------------------------------------------------------------------------------------------
class View:
    """a north-up window: (cx, cz) world centre, w x h output px, mpp metres per OUTPUT px; scale = output px per design px."""

    def __init__(self, cx, cz, w, h, mpp, scale=1.0):
        self.cx, self.cz, self.w, self.h, self.mpp, self.scale = cx, cz, w, h, mpp, scale
        xs = (np.arange(w) + .5 - w / 2) * mpp + cx; zs = cz - (np.arange(h) + .5 - h / 2) * mpp
        self.X, self.Z = np.meshgrid(xs, zs)

    def to_px(self, x, z):
        return (np.asarray(x, float) - self.cx) / self.mpp + self.w / 2, (self.cz - np.asarray(z, float)) / self.mpp + self.h / 2


FOG_SOFT, FOG_NOISE = 1.0, .22      # MapStyle304.asset: FogSoftEdge 1, FogEdgeNoise .22 (the values the game sets on both shaders)
LAYERS = ('terrain', 'forest', 'water', 'ridges', 'cliffs', 'walls', 'roads', 'bridges', 'ticks')
_LAYER_OF = {1: 'water', 2: 'ridges', 3: 'cliffs', 4: 'cliffs', 5: 'walls', 6: 'roads', 7: 'roads', 8: 'roads', 9: 'roads', 10: 'bridges', 11: 'ticks'}


def _fog_hash(x, y):
    """MapFog308_Hash: frac(sin(dot(p, (127.1, 311.7))) * 43758.5453), in float32 like the GPU."""
    s = np.sin((x * np.float32(127.1) + y * np.float32(311.7)).astype(np.float32)).astype(np.float32) * np.float32(43758.5453)
    return s - np.floor(s)


def _fog_noise(x, y):
    ix = np.floor(x); iy = np.floor(y); fx = x - ix; fy = y - iy
    fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy)
    a = _fog_hash(ix, iy); b = _fog_hash(ix + 1, iy); c = _fog_hash(ix, iy + 1); d = _fog_hash(ix + 1, iy + 1)
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fog_known(walked, X, Z, soft=FOG_SOFT, noise=FOG_NOISE):
    """MapFog308.cginc MapFog308_Known, line for line: 1 = walked, 0 = not walked, for world positions X, Z (metres).
    walked = bool [FOG_H, FOG_W], south row first (the fog texture holds alpha 1 = NOT walked, point sampled, clamped).
    The edge is the #307 one: inside a walked cell the land fades in from the cell border (field .5) to .45 of a cell in
    (field .95), wobbled by a value noise; an unwalked cell is exactly 0."""
    A = (~walked).astype(np.float32)
    u = (X / M.FOG_CELL).astype(np.float32); v = (Z / M.FOG_CELL).astype(np.float32)
    at = lambda ii, jj: A[np.clip(ii, 0, M.FOG_H - 1), np.clip(jj, 0, M.FOG_W - 1)]
    ci = np.floor(v).astype(int); cj = np.floor(u).astype(int); cx = u - np.floor(u); cy = v - np.floor(v)
    here = 1 - at(ci, cj)
    known = here * (1 + (smoothstep(0, .36, cx) - 1) * at(ci, cj - 1)) * (1 + (smoothstep(0, .36, 1 - cx) - 1) * at(ci, cj + 1)) \
        * (1 + (smoothstep(0, .36, cy) - 1) * at(ci - 1, cj)) * (1 + (smoothstep(0, .36, 1 - cy) - 1) * at(ci + 1, cj))
    tu = u - .5; tv = v - .5; fu = tu - np.floor(tu); fv = tv - np.floor(tv); bj = np.floor(tu).astype(int); bi = np.floor(tv).astype(int)
    field = 1 - ((at(bi, bj) * (1 - fu) + at(bi, bj + 1) * fu) * (1 - fv) + (at(bi + 1, bj) * (1 - fu) + at(bi + 1, bj + 1) * fu) * fv)
    field = field + (_fog_noise(u * np.float32(2.5), v * np.float32(2.5)) - .5) * noise
    soft_v = here * smoothstep(.5, .95, field)
    return np.clip(known + (soft_v - known) * min(max(soft, 0.0), 1.0), 0, 1)


EDGE_DEFAULT = dict(thresholdFrom=.14, thresholdSpan=.42, noiseCellsPerFogCell=[1.6, 3.7], noiseWeights=[.62, .38])   # the THRESHOLD formula's numbers
# MapFog308_Edge exists in two formulas. The twin draws the one the cginc below carries, told apart by `MAPFOG308_E_FLOOR` in
# its text: the threshold formula (numbers = the bundle's fogEdge.crispEdge, else EDGE_DEFAULT) or formula F of #308 map fix 2
# (numbers = the cginc's own `#define MAPFOG308_E_*` lines, read from the file and never retyped here).
EDGE_CGINC = M.A / 'Art/UI/UI308/Map/Shaders/MapFog308.cginc'       # the LIVE shader (read only); use_cginc() / --cginc point elsewhere
EDGE_F_NAMES = ('FROM', 'SPAN', 'KCOVER', 'FLOOR', 'D1', 'D2', 'HALF1', 'HALF2', 'PHASE', 'PK', 'RK', 'RIM0', 'RIM1', 'RIMBREAK')
import map308_edge3 as E3       # #308 map 3: the formula of a cginc that has `#define MAPFOG308_E_GATE0`
_EDGE_F = {}                                                        # {cginc path: None (threshold formula) | constants of formula F}


def use_cginc(path):
    """point the twin at another MapFog308.cginc (a stage file before its deploy)"""
    global EDGE_CGINC
    EDGE_CGINC = Path(path)


def edge_constants(cginc=None):
    """-> None when the cginc carries the threshold formula, else formula F's constants {name: float | (float, float)} from its
    `#define MAPFOG308_E_<name>` lines. Read once per file."""
    path = Path(cginc or EDGE_CGINC)
    if path not in _EDGE_F:
        if not path.exists(): raise FileNotFoundError(f'map308_twin: no MapFog308.cginc at {path} - the walked edge follows that file; pass --cginc or call use_cginc()')
        src = path.read_bytes().decode('utf-8-sig'); q = None
        if 'MAPFOG308_E_FLOOR' in src:
            q = {}
            for m in re.finditer(r'^[ \t]*#define[ \t]+MAPFOG308_E_(\w+)[ \t]+(float2\(([^)]*)\)|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)', src, re.M):
                q[m.group(1)] = tuple(float(v) for v in m.group(3).split(',')) if m.group(3) else float(m.group(2))
            missing = [k for k in EDGE_F_NAMES if k not in q] + (E3.missing3(q) if E3.is_map3(q) else [])
            if missing: raise ValueError(f"{path} carries edge formula F but no #define for MAPFOG308_E_{', MAPFOG308_E_'.join(missing)}")
        _EDGE_F[path] = q
    return _EDGE_F[path]


def _fog_edge_f(walked, X, Z, q):
    """formula F (#308 map fix 2), the stage / live cginc line for line: -> (field, rim gate).
    cover = the lattice-corner bilinear (exactly 0 on an unwalked cell); depth = 1 - sqrt(2 (1 - bs)), bs = the quadratic
    B-spline of the SAME nine cells (round at the corners of the cell staircase); land = min(depth, KCOVER x cover) <= 0 on
    every unwalked cell; theta = FROM + SPAN x (.5 + .25 x (two sine waves whose phase a slow value noise pushes)), never under
    FLOOR output px; field = land - theta. Behind a long straight border the edge lies FROM .. FROM + SPAN cells inside the
    walked cells (3.8 .. 19.8 m with .12 / .50); within a cell of a corner of the unwalked land the spline rounds the corner
    and only the clamp holds the edge (cover >= FROM / KCOVER = .08: 2.6 m off a side, about 2 m diagonally off a lone
    corner). It never shows the 32 m grid as a cut. Everything is float32, as in the shader (sin of ~1300 rad); the screen
    derivatives of the cell coordinate are np.gradient, as in fog_crisp. The rim gate is the function's .y (saturate(land),
    raised in patches of a 25 m noise when RIMBREAK is 1)."""
    f32 = np.float32; sat = lambda x: np.clip(x, 0, 1)
    W = walked.astype(f32)
    u = (X / M.FOG_CELL).astype(f32); v = (Z / M.FOG_CELL).astype(f32)                       # c=uv*_FogTex_TexelSize.zw;
    cj = np.floor(u); ci = np.floor(v); fx = (u - cj).astype(f32); fy = (v - ci).astype(f32)   # cell=floor(c); f=c-cell;
    cj = cj.astype(int); ci = ci.astype(int)
    at = lambda ii, jj: W[np.clip(ii, 0, M.FOG_H - 1), np.clip(jj, 0, M.FOG_W - 1)]         # the nine cells at their texel centres, clamped
    w = {(di, dj): at(ci + di, cj + dj) for di in (-1, 0, 1) for dj in (-1, 0, 1)}
    corner = lambda di, dj: np.minimum(np.minimum(w[(di - 1, dj - 1)], w[(di - 1, dj)]), np.minimum(w[(di, dj - 1)], w[(di, dj)]))
    cover = (corner(0, 0) * (1 - fx) + corner(0, 1) * fx) * (1 - fy) + (corner(1, 0) * (1 - fx) + corner(1, 1) * fx) * fy   # cover=lerp(lerp(k00,k10,f.x),lerp(k01,k11,f.x),f.y);
    ax = (.5 * (1 - fx) ** 2, None, .5 * fx ** 2); ay = (.5 * (1 - fy) ** 2, None, .5 * fy ** 2)   # a0=.5*(1-f)*(1-f); a2=.5*f*f;
    ax = (ax[0], 1 - ax[0] - ax[2], ax[2]); ay = (ay[0], 1 - ay[0] - ay[2], ay[2])           # a1=1-a0-a2;
    bs = 0
    for di in (-1, 0, 1):                                                                   # bs = the nine cells x a?.x x a?.y
        for dj in (-1, 0, 1): bs = bs + w[(di, dj)] * ax[dj + 1] * ay[di + 1]
    depth = 1 - np.sqrt(np.maximum(2 * (1 - bs), 0))                                        # depth=1-sqrt(max(2*(1-bs),0));
    land = np.minimum(depth, q['KCOVER'] * cover)                                           # land=min(depth,KCOVER*cover);
    if X.ndim == 2 and min(X.shape) > 1:                                                    # pxPerCell=1/max(length(float2(length(ddx(c)),length(ddy(c))))*.7071,1e-6);
        uy, ux = np.gradient(u); vy, vx = np.gradient(v)
        ppc = 1.0 / np.maximum(np.hypot(np.hypot(ux, vx), np.hypot(uy, vy)) * .7071, 1e-6)
    else:
        ppc = M.FOG_CELL / max(abs(float(X.flat[1] - X.flat[0])) if X.size > 1 else 1.0, 1e-6)
    noise = lambda k, off: _fog_noise((u * f32(k) + f32(off)).astype(f32), (v * f32(k) + f32(off)).astype(f32))
    n1 = noise(q['PK'], 5.2); n2 = noise(q['PK'], 31.7)                                     # n1=MapFog308_Noise(c*PK+5.2); n2=MapFog308_Noise(c*PK+31.7);
    wave1 = np.sin((u * f32(q['D1'][0]) + v * f32(q['D1'][1]) + f32(q['PHASE']) * n1).astype(f32)) * sat(ppc * q['HALF1'] * .5 - .5)
    wave2 = np.sin((u * f32(q['D2'][0]) + v * f32(q['D2'][1]) + f32(q['PHASE']) * n2).astype(f32)) * sat(ppc * q['HALF2'] * .5 - .5)
    theta = f32(q['FROM']) + f32(q['SPAN']) * (.5 + .25 * (wave1 + wave2))                  # theta=FROM+SPAN*(.5+.25*(wave1+wave2));
    theta = np.maximum(theta, q['FLOOR'] / ppc)                                             # theta=max(theta,FLOOR/pxPerCell);
    if E3.is_map3(q): return E3.edge3(q, at, ci, cj, fx, fy, u, v, ppc, _fog_noise)         # #308 map 3: bites + the continuous rim gate (map308_edge3.py)
    nr = .5 + (noise(q['RK'], 23.1) - .5) * sat(ppc / q['RK'] * .5 - .5)                    # nr=lerp(.5,MapFog308_Noise(c*RK+23.1),saturate(pxPerCell/RK*.5-.5));
    t = sat((nr - q['RIM0']) / (q['RIM1'] - q['RIM0']))
    gate = np.maximum(sat(land), q['RIMBREAK'] * (1 - .08 * (t * t * (3 - 2 * t))))         # gate=max(saturate(land),RIMBREAK*(1-.08*smoothstep(RIM0,RIM1,nr)));
    return land - theta, gate                                                               # return float2(land-theta,gate);


def fog_edge(walked, X, Z, edge=None):
    """MapFog308.cginc MapFog308_Edge, line for line, in the formula EDGE_CGINC carries: -> (field, .y). field = the signed
    field of the #308 CRISP walked edge (> 0 = walked land); .y = what the paper shader gates the ink rim with.

    Formula F (the cginc has `#define MAPFOG308_E_*`): see _fog_edge_f - `edge` (the bundle's crispEdge) is not read, as the
    shader no longer reads _M308Edge / _S308Edge; the edge lies 3.8 .. 19.8 m inside the walked cells behind a straight
    border (nearer by a corner of the unwalked land, see there); .y = the rim gate.

    Threshold formula (every cginc before #308 map fix 2), below: .y = cover, the lattice-corner value alone (1 deep inside the
    walked land).
    walked = bool [FOG_H, FOG_W], south row first. e = bilinear of the four LATTICE CORNERS of the cell the point is in, where
    a corner is 1 only when all four cells round it are walked: so e is exactly 0 on every unwalked cell and on its border
    (#214 by construction, no clamp needed), rises over one cell behind a straight border and is 1 deep inside. The edge is
    the contour e = thresholdFrom + thresholdSpan x noise (two octaves of the fog value noise, world anchored): it wobbles
    4.5 .. 18 m inside the walked cells and never shows the 32 m grid as a cut. An octave whose lattice is under ~2 output px
    is faded to its mean, as the shader does from its screen derivatives (no speckle on the whole-world view)."""
    F = edge_constants()
    if F is not None: return _fog_edge_f(walked, X, Z, F)
    E = edge or EDGE_DEFAULT
    W = walked.astype(np.float32)
    u = (X / M.FOG_CELL).astype(np.float32); v = (Z / M.FOG_CELL).astype(np.float32)
    cj = np.floor(u).astype(int); ci = np.floor(v).astype(int); fx = u - cj; fy = v - ci
    at = lambda ii, jj: W[np.clip(ii, 0, M.FOG_H - 1), np.clip(jj, 0, M.FOG_W - 1)]
    w = {(di, dj): at(ci + di, cj + dj) for di in (-1, 0, 1) for dj in (-1, 0, 1)}
    corner = lambda di, dj: np.minimum(np.minimum(w[(di - 1, dj - 1)], w[(di - 1, dj)]), np.minimum(w[(di, dj - 1)], w[(di, dj)]))
    e = (corner(0, 0) * (1 - fx) + corner(0, 1) * fx) * (1 - fy) + (corner(1, 0) * (1 - fx) + corner(1, 1) * fx) * fy
    f1, f2 = (np.float32(k) for k in E['noiseCellsPerFogCell']); w1, w2 = E['noiseWeights']
    px_per_cell = M.FOG_CELL / max(abs(float(X[0, 1] - X[0, 0])) if X.shape[1] > 1 else 1.0, 1e-6)       # the shader: 1 / |d(cell) / d(px)|
    fade = lambda f: float(np.clip(px_per_cell / float(f) * .5 - .5, 0, 1))
    n1 = .5 + (_fog_noise(u * f1, v * f1) - .5) * fade(f1); n2 = .5 + (_fog_noise(u * f2 + np.float32(17.3), v * f2 + np.float32(17.3)) - .5) * fade(f2)
    return e - (E['thresholdFrom'] + E['thresholdSpan'] * (n1 * w1 + n2 * w2)), e


def fog_crisp4(walked, X, Z, edge=None):
    """fog_crisp + the field itself (cells): -> (walked value, signed distance px, .y, field). #308 map 4: the sheet's whole rim reads the field."""
    g, cover = fog_edge(walked, X, Z, edge)
    gy, gx = np.gradient(g)
    d = g / np.maximum(np.hypot(gx, gy), 1e-5)
    return np.clip(d + .5, 0, 1), d, cover, g


def fog_crisp(walked, X, Z, edge=None):
    """-> (walked value 0..1 with a 1 px anti-aliased edge, signed distance to the edge in OUTPUT px, cover), as the paper
    shader computes them: d = field / |screen gradient of the field| (np.gradient stands in for ddx / ddy), walked = saturate(d + .5).
    The third value is MapFog308_Edge's .y, which gates the ink rim: the cover (threshold formula) or the rim gate (formula F)."""
    g, cover = fog_edge(walked, X, Z, edge)
    gy, gx = np.gradient(g)
    d = g / np.maximum(np.hypot(gx, gy), 1e-5)
    return np.clip(d + .5, 0, 1), d, cover


def render(B, V, walked, road_style='B', layers=None, show=LAYERS, extra=()):
    """-> RGB float [h, w, 3] of the map SURFACE (no icons, no frame), composed in the order the two shaders draw:
    the paper (wash, patterns, shore; all x the walked value), the ink rim inside the crisp edge, then every strip with alpha x the walked value
    (roads: every paper band first, then every ink). layers (dict) receives the masks the measurements use. show = which
    notation layers are drawn. extra = [(class, rank, Nx2 world points, teeth_left)]: lines the bundle does not carry, drawn
    with the bundle's own stroke style (the direction sheet's gate wall)."""
    n = B.note; T = n['terrain']; F = T['forest']; Wt = T['water']; mpp_ref = V.mpp * V.scale; band = B.band(mpp_ref); world = band >= 3
    lvl = int(np.clip(math.floor(math.log2(max(V.mpp / M.CELL, 1e-6)) + .5), 0, 2))
    ter = bilinear(B.terrain[lvl], V.X / (M.CELL * 2 ** lvl) - .5, V.Z / (M.CELL * 2 ** lvl) - .5)
    R, G, Bc, A = ter[..., 0], ter[..., 1], ter[..., 2], ter[..., 3]
    period = T['patternPeriodM'][band]
    plv = int(np.clip(round(math.log2(max(256 * V.mpp / period, 1e-6))), 0, 4)); ps = 256 / 2 ** plv
    pat = bilinear(B.pattern[plv], V.X / period * ps - .5, -V.Z / period * ps - .5, wrap=True)
    sdf = (Bc * 255 - Wt['shoreCode']) / 127.0 * Wt['sdfRangeM']
    # --- paper + wash (the shader: lerp(R x slope max, smoothstep(elevation range) x elevation wash, world band) x step(dm, 0))
    e0, e1 = (v / T['elevationMaxM'] for v in T.get('elevationRangeM', [90.0, 330.0]))
    wash = (T['elevationWash'] * smoothstep(e0, e1, A)) if world else R * T['slopeWashMax']
    wash = np.where(sdf > 0, 0, wash)
    if 'terrain' not in show: wash = wash * 0
    land = PAPER[None, None] * (1 - wash[..., None])
    ink = np.zeros(R.shape)
    # --- rock strokes on the steepest ground, tree dabs by density, wave marks and the shore line (max, as the shader)
    if not world and 'terrain' in show:
        ink = np.maximum(ink, smoothstep(T['rockFrom'] - T.get('rockSoftness', .08), 1.0, R) * pat[..., 2] * T.get('rockInk', .70))
    if 'forest' in show:
        ink = np.maximum(ink, np.clip((pat[..., 0] - (1 - G)) * F.get('gain', 6.0), 0, 1) * (G >= F['showFrom'])
                         * (F.get('dotInkWorld', .45) if world else F.get('dotInk', .62)))
    if 'water' in show or 'terrain' in show:
        ink = np.maximum(ink, pat[..., 1] * smoothstep(Wt['patternFromM'], Wt['patternFromM'] + Wt.get('patternFeatherM', 4.0), sdf)
                         * (Wt.get('rippleInkWorld', .3) if world else Wt.get('rippleInk', .5)))
        hw = Wt['shoreLinePx'] * V.scale / 2
        ink = np.maximum(ink, np.clip(hw - np.abs(sdf) / V.mpp + .5, 0, 1) * Wt.get('shoreInk', .80))
    land = land * (1 - ink[..., None]) + INK * ink[..., None]
    terrain_ink = ink.copy()
    # --- fog: the #308 crisp walked edge (the stage shaders' function); an unwalked pixel is the flat wash x grain, nothing else
    E = n['values']['fogEdge']
    w, dpx, cover, field = fog_crisp4(walked, V.X, V.Z, E.get('crispEdge'))
    w = w * ((V.X >= 0) & (V.X < M.WORLD_X) & (V.Z >= 0) & (V.Z < M.WORLD_Z))
    g_amp = n['values']['washGrainAmp']
    unk = WASH[None, None] * (1 + (pat[..., 3:4] - .5) * 2 * g_amp)
    col = unk * (1 - w[..., None]) + land * w[..., None]
    # the ink rim: the outermost E.px px of the walked land, ink at E.inkAlpha over the unwalked wash (never outside the edge)
    if E3.is_map3(edge_constants()): rim = w * E3.rim3(edge_constants(), cover, dpx, E['px'] * V.scale)   # #308 map 3: crisp x MapFog308_Rim(walk.y, edgePx, _M308Rim.y)
    else: rim = w * np.clip(E['px'] * V.scale - dpx + .5, 0, 1) * (1 - smoothstep(.92, 1.0, cover))      # never deep inside (cover = 1)
    rim_alpha = E['inkAlpha']
    # #308 map 4 (D308-24 answer 9): a View marked `sheet` (the unfolded map, the whole-world view) draws the notation's thin whole rim:
    # rim = lerp(rim, crisp x MapFog308_Rim(gateW, edgePx, _M308Rim.w / WMAX), _M308Rim.z); the ink alpha goes with it. A View without the
    # mark is the minimap (and every measurement window of this tool): the broken rim above, unchanged.
    S = E.get('sheet')
    if getattr(V, 'sheet', False) and S and E3.is_map3(edge_constants()):
        z = min(max(float(S.get('whole', 0)), 0.0), 1.0)
        rim = rim + (w * E3.rim3_whole(edge_constants(), field, dpx, S['px'] * V.scale) - rim) * z
        rim_alpha = E['inkAlpha'] + (S['inkAlpha'] - E['inkAlpha']) * z
    rim_col = unk * (1 - rim_alpha) + INK * rim_alpha
    col = col * (1 - rim[..., None]) + rim_col * rim[..., None]
    # --- brush bands (strips): alpha x walked, drawn in the file's order; roads: all paper bands, then all inks
    st, pt = B.strokes['strokes'], B.strokes['points']
    pending = []
    for s in st:
        if _LAYER_OF[int(s['cls'])] not in show: continue
        sty = B.stroke_style(s['cls'], s['rank'], band)
        if sty is None or sty['width'] <= 0: continue
        if s['cls'] == 1 and 2 * s['hw'] / mpp_ref >= B.classes[1].get('drawWhenPhysicalWidthPxBelow', 3.2): continue
        sl = slice(int(s['first']), int(s['first'] + s['count']))
        px, py = V.to_px(pt['x'][sl], pt['z'][sl])
        if px.max() < -30 or px.min() > V.w + 30 or py.max() < -30 or py.min() > V.h + 30: continue
        pending.append((int(s['cls']), sty, np.stack([px, py], 1), pt['d'][sl].astype(float), .5 + pt['p'][sl] / 255.0 * .75, pt['f'][sl], float(s['len'])))
    for cls, rank, Pw, left in extra:
        sty = B.stroke_style(cls, rank, band)
        if sty is None or sty['width'] <= 0: continue
        Pw = M.densify(np.asarray(Pw, float), 48.0); px, py = V.to_px(Pw[:, 0], Pw[:, 1]); d = M.cum_len(Pw)
        pending.append((int(cls), sty, np.stack([px, py], 1), d, np.ones(len(Pw)), np.full(len(Pw), 8 if left else 0, np.uint8), float(d[-1])))
    pending.sort(key=lambda q: q[0])
    road_ink = np.zeros(R.shape); road_paper = np.zeros(R.shape); other = []; inks = []
    for cls, sty, P, d, pr, fl, length in pending:
        if road_style != 'B' and cls in (7, 8, 9, 11): continue
        ar = B.atlas_rows[sty['row']]; W = sty['width'] * V.scale
        a = np.zeros(R.shape); k = np.zeros(R.shape)
        raster(a, k, P, d / V.mpp, pr, fl, W, B.rows[sty['row']], ar['axisV'], 1024.0 * (W / 40.0) * (ar['uAspect'] or 1.0),
               sty['knock'] * V.scale if cls in (7, 8, 9) else 0.0, ar['uMode'] == 'stretch', length / V.mpp)
        a = a * sty['alpha']
        if cls in (7, 8, 9):
            road_paper = np.maximum(road_paper, k); road_ink = np.maximum(road_ink, a)
        else:
            other.append((cls, a))
        inks.append((cls, a))
    alt = alt_roads(B, V, road_style) if (road_style != 'B' and 'roads' in show) else None
    if alt is not None: road_ink = alt
    ops = []; paper_done = False; alt_done = alt is None
    for cls, a in inks:
        if cls >= 7 and not paper_done: ops.append((PAPER, road_paper)); paper_done = True     # the paper band of every road first
        if cls >= 10 and not alt_done: ops.append((INK, alt)); alt_done = True
        ops.append((INK, a))
    if not alt_done: ops.append((INK, alt))
    for tone, a in ops:
        a = a * w; col = col * (1 - a[..., None]) + tone * a[..., None]
    if layers is not None:
        oth = np.maximum.reduce([a for c, a in other] or [np.zeros(R.shape)])
        flat = (wash < .01) & (terrain_ink < .02) & (rim < .01) & (road_ink < .02) & (road_paper < .02) & (oth < .02)
        layers.update(walked=w, flat=flat, road_ink=road_ink * w, road_paper=road_paper * w, terrain_ink=terrain_ink * w, wash=wash, band=band,
                      other_ink=oth * w, unknown=np.broadcast_to(unk, col.shape), rim=rim,
                      presence={c: (np.maximum.reduce([a for cc, a in other if cc == c] or [np.zeros(R.shape)]) > .3) for c in (1, 2, 3, 4, 5)})
    return np.clip(col, 0, 1)


ATLAS_FOOTPRINT_TEXELS = 4.8       # the strip shader keeps the mip footprint across a row under .8 x the row margin (6 texels)


def raster(ink, paper, P, dist_px, press, flags, W, mips, axis_v, tile_px, knock, stretch, length_px):
    """one brush band into the coverage layers (max). P = points in output px; W = nominal width px; texture v: row top = left."""
    H, Wd = ink.shape
    lv = int(np.clip(round(math.log2(min(max(40.0 / max(W, .1), 1.0), ATLAS_FOOTPRINT_TEXELS))), 0, 4)); row = mips[lv]; sc = 2 ** lv
    teeth_left = bool(flags[0] & 8)
    n = len(P)
    for i in range(n - 1):
        ax, ay = P[i]; bx, by = P[i + 1]; L = math.hypot(bx - ax, by - ay)
        if L < 1e-6: continue
        hwa = W * press[i] / 2; hwb = W * press[i + 1] / 2; ext = max(hwa, hwb) * 1.6 + knock + 2
        x0 = max(0, int(min(ax, bx) - ext)); x1 = min(Wd, int(max(ax, bx) + ext) + 2)
        y0 = max(0, int(min(ay, by) - ext)); y1 = min(H, int(max(ay, by) + ext) + 2)
        if x1 <= x0 or y1 <= y0: continue
        X, Y = np.meshgrid(np.arange(x0, x1) + .5, np.arange(y0, y1) + .5)
        tx, ty = (bx - ax) / L, (by - ay) / L; rx, ry = X - ax, Y - ay
        s = rx * tx + ry * ty; sc_ = np.clip(s, 0, L); q = rx * ty - ry * tx                # q > 0 = left of the direction (north-up screen)
        over = s - sc_; d = np.hypot(over, q); sd = np.where(q >= 0, d, -d)
        hw = hwa + (hwb - hwa) * (sc_ / L)
        along = dist_px[i] + sc_ / L * (dist_px[i + 1] - dist_px[i])
        cap = np.ones_like(hw)
        if flags[0] & 1: cap = np.minimum(cap, .35 + .65 * smoothstep(0, 2.5 * W, along))
        if flags[-1] & 2: cap = np.minimum(cap, .35 + .65 * smoothstep(0, 2.5 * W, length_px - along))
        hw = hw * cap
        v = axis_v + (-sd if teeth_left else sd) * (40.0 / np.maximum(2 * hw, 1e-3))
        u = (along / max(length_px, 1e-6) * 1024.0) if stretch else (along / tile_px * 1024.0)
        ok = (v >= 0) & (v < 64) & (np.abs(over) <= hw * .6 + .5)
        a = bilinear(row, (u % 1024.0) / sc - .5, np.clip(v, 0, 63.999) / sc - .5, wrap=True)
        ink[y0:y1, x0:x1] = np.maximum(ink[y0:y1, x0:x1], np.where(ok, a[..., 3], 0))
        if knock > 0:
            paper[y0:y1, x0:x1] = np.maximum(paper[y0:y1, x0:x1], np.clip(hw + knock - d + .5, 0, 1))


def alt_roads(B, V, style):
    """the two rejected road notations of Spec section 2, on the same data: A ruled line (the source line straightened with a
    12 m tolerance, 2.6 px, what the minimap prints today), C stamped band (double line + dots)."""
    st, pt = B.strokes['strokes'], B.strokes['points']; out = np.zeros((V.h, V.w)); ss = 4
    im = Image.new('L', (V.w * ss, V.h * ss), 0); d = ImageDraw.Draw(im)
    for s in st:
        if s['cls'] not in (7, 8, 9): continue
        sl = slice(int(s['first']), int(s['first'] + s['count']))
        Pw = np.stack([pt['x'][sl], pt['z'][sl]], 1).astype(float)
        if style == 'A': Pw = M.dp_simplify(Pw, 12.0)
        px, py = V.to_px(Pw[:, 0], Pw[:, 1]); P = np.stack([px, py], 1)
        if px.max() < -20 or px.min() > V.w + 20 or py.max() < -20 or py.min() > V.h + 20: continue
        if style == 'A':
            d.line([(x * ss, y * ss) for x, y in P], fill=230, width=int(round(2.6 * V.scale * ss)), joint='curve')
        else:
            wd = {9: 4.5, 8: 3.2, 7: 2.2}[int(s['cls'])] * V.scale; nr = M.normals(P)
            for sgn in (-1, 1):
                d.line([((x + sgn * nx * wd / 2) * ss, (y + sgn * ny * wd / 2) * ss) for (x, y), (nx, ny) in zip(P, nr)], fill=220,
                       width=max(1, int(round(1.0 * V.scale * ss))))
            if s['cls'] != 7:
                D = M.resample(P, 6.0 * V.scale)
                for x, y in D:
                    r = .8 * V.scale
                    d.ellipse([(x - r) * ss, (y - r) * ss, (x + r) * ss, (y + r) * ss], fill=220)
    return M.down(np.asarray(im, float) / 255.0, ss)


# ---------------------------------------------------------------------------------------------------------------------
# icons, frame, text
# ---------------------------------------------------------------------------------------------------------------------
def glyph_rgba(B, gid, size_px, tint=None, rotate=0.0, ink2=None):
    """a glyph as UI/MapIcon308 draws it: the paper plate (G), the first ink in the tint (R: ink, or cinnabar), the second ink
    in the ink token over it (B: the brush mark's ferrule, handle and edge; 0 for every other glyph). ink2 = the colour of the
    second ink (default the ink token; the paper token on a dark plate). -> (rgb, alpha, figure = both inks)."""
    sheet = 'S' if size_px <= 28 else 'L'; cell = 32 if sheet == 'S' else 64        # MapNotation308SO.SmallAtlasMaxPx
    c = B.glyph[gid]; r, k = divmod(c, 8)
    g = B.icons[sheet][r * cell:(r + 1) * cell, k * cell:(k + 1) * cell]
    n = max(2, int(round(size_px))); ch = []
    for k in (0, 1, 2):                                 # the channels are independent masks: resize them one by one
        im = Image.fromarray(np.round(g[..., k] * 255).astype(np.uint8), 'L')
        if rotate: im = im.rotate(rotate, resample=Image.BICUBIC)
        ch.append(np.asarray(im.resize((n, n), Image.LANCZOS), float) / 255.0)
    ink, plate, second = ch
    col = np.ones((n, n, 3)) * PAPER
    col = col * (1 - ink[..., None]) + (INK if tint is None else tint) * ink[..., None]
    col = col * (1 - second[..., None]) + (INK if ink2 is None else ink2) * second[..., None]
    fig = np.maximum(ink, second)
    return col, np.maximum(fig, plate), fig


def paste(img, col, alpha, cx, cy):
    h, w = alpha.shape; x0 = int(round(cx - w / 2)); y0 = int(round(cy - h / 2))
    xa, ya = max(0, x0), max(0, y0); xb, yb = min(img.shape[1], x0 + w), min(img.shape[0], y0 + h)
    if xb <= xa or yb <= ya: return
    a = alpha[ya - y0:yb - y0, xa - x0:xb - x0][..., None]
    c = col[ya - y0:yb - y0, xa - x0:xb - x0] if col.ndim == 3 else col
    img[ya:yb, xa:xb] = img[ya:yb, xa:xb] * (1 - a) + c * a


class Kit:
    """the theme kit's atlas (SPEC-UI-THEME-308). The map bundle carries no frame art; the mocks read the kit cells by name."""

    def __init__(self):
        pj, pp = M.THEME_DIR / 'theme308_atlas.json', M.THEME_DIR / 'theme308_atlas.png'
        self.ok = pj.exists() and pp.exists()
        if not self.ok: return
        self.meta = json.loads(pj.read_text(encoding='utf-8')); self.tpp = self.meta['texels_per_design_px']
        self.img = np.asarray(Image.open(pp).convert('RGBA'), float) / 255.0
        self.cells = {c['kit']: c for c in self.meta['cells']}

    def sprite(self, kit_name):
        c = self.cells[kit_name]; x, y, w, h = c['rect']
        return self.img[y:y + h, x:x + w], c

    @staticmethod
    def _index(n, a, b, n_src, tiled, from_end):
        """dest texel -> source texel along one axis of a bordered sprite; a = border at index 0, b = border at the far end.
        Unity's Image.Type.Tiled lays the middle tiles from the BOTTOM-LEFT: along x from index 0, along y (picture rows run
        from the top) from the far end - from_end. A cut tile, if any, is at the right / at the top."""
        i = np.arange(n); mid_src, mid_dst = n_src - a - b, n - a - b
        if mid_src <= 0: return np.clip(i, 0, n_src - 1)
        if tiled: mid = (n_src - b - 1 - ((n - b - 1 - i) % mid_src)) if from_end else a + (i - a) % mid_src
        else: mid = a + np.clip(((i - a + .5) * mid_src / max(1, mid_dst)).astype(int), 0, mid_src - 1)
        return np.where(i < a, i, np.where(i >= n - b, n_src - (n - i), mid))

    def rule(self, kit_name):
        """the rectangle rule of a tiled frame cell, from the kit json: design px (left + right) + piece x n wide,
        (bottom + top) + piece x m high. None for a cell without a piece length."""
        c = self.cells[kit_name]
        if 'piece_px' not in c: return None
        l, b, r, t = c['border_px']
        return dict(wide=l + r, high=b + t, piece=c['piece_px'])

    def fits(self, kit_name, w, h):
        """True when w x h design px ends every tiled side on a whole piece (the kit's rectangle rule)."""
        R = self.rule(kit_name)
        if R is None: return True
        ok = lambda v, base: v >= base and abs((v - base) / R['piece'] - round((v - base) / R['piece'])) < 1e-6
        return ok(w, R['wide']) and ok(h, R['high'])

    def frame(self, kit_name, w, h, scale=1.0):
        """a bordered kit sprite laid out at w x h DESIGN px the way Unity's Image does (Sliced, or Tiled from the bottom-left)
        -> RGBA at design px x scale."""
        spr, c = self.sprite(kit_name); k = self.tpp
        l, b, r, t = c['border_texels']; W, H = int(round(w * k)), int(round(h * k)); tiled = c['use'] == 'Tiled'
        out = spr[self._index(H, t, b, spr.shape[0], tiled, True)][:, self._index(W, l, r, spr.shape[1], tiled, False)].copy()
        if not c.get('fill_center', False): out[t:H - b, l:W - r, 3] = 0.0
        n = (int(round(w * scale)), int(round(h * scale)))
        return np.asarray(Image.fromarray(np.round(out * 255).astype(np.uint8), 'RGBA').resize(n, Image.LANCZOS), float) / 255.0

    def simple(self, kit_name, scale=1.0):
        spr, c = self.sprite(kit_name)
        n = (max(1, int(round(spr.shape[1] / self.tpp * scale))), max(1, int(round(spr.shape[0] / self.tpp * scale))))
        return np.asarray(Image.fromarray(np.round(spr * 255).astype(np.uint8), 'RGBA').resize(n, Image.LANCZOS), float) / 255.0


KIT = None


def kit():
    global KIT
    if KIT is None: KIT = Kit()
    return KIT


def over_rgba(img, rgba, x0, y0):
    h, w = rgba.shape[:2]
    xa, ya = max(0, x0), max(0, y0); xb, yb = min(img.shape[1], x0 + w), min(img.shape[0], y0 + h)
    if xb <= xa or yb <= ya: return
    s = rgba[ya - y0:yb - y0, xa - x0:xb - x0]
    img[ya:yb, xa:xb] = img[ya:yb, xa:xb] * (1 - s[..., 3:4]) + s[..., :3] * s[..., 3:4]


def draw_text(img, xy, text, fnt, fill, anchor='la'):
    im = Image.fromarray(np.round(np.clip(img, 0, 1) * 255).astype(np.uint8), 'RGB')
    ImageDraw.Draw(im).text(xy, text, font=fnt, fill=tuple(int(round(v * 255)) for v in fill), anchor=anchor)
    img[:] = np.asarray(im, float) / 255.0


def markers_in(B, mp, walked):
    """the mock's discovered markers. The game reveals a baked marker by ARRIVAL: the player stood there, so every cell within
    the 96 m reveal radius is walked. Stand-in: the marker's cell and its eight neighbours are walked (an icon is then never
    outside the crisp edge)."""
    out = []
    for m in mp['markers']:
        r, c = int(m['z'] // M.FOG_CELL), int(m['x'] // M.FOG_CELL)
        if 1 <= r < M.FOG_H - 1 and 1 <= c < M.FOG_W - 1 and walked[r - 1:r + 2, c - 1:c + 2].all():
            out.append(dict(m, glyph=B.note['markerGlyphs'].get(m['id'], 'place')))
    return out


def draw_marks(B, img, V, marks, size_px, player=None, player_px=28, wake=None, margin=12, labels=None):
    """icons on the composite; returns the list of drawn label boxes. size_px / player_px are DESIGN px (x V.scale)."""
    s = V.scale; drawn = []
    for m in marks:
        x, y = V.to_px(m['x'], m['z'])
        if not (margin * s <= x <= V.w - margin * s and margin * s <= y <= V.h - margin * s): continue
        sz = size_px * s * (34 / 30 if (m['glyph'] == 'inn' and size_px >= 30) else 1.0)
        if wake and m['id'] == wake:
            c, a, _ = glyph_rgba(B, 'wake_ring', sz * B.note['sizes']['minimap']['wakeRingScale']); paste(img, c, a, x, y)
        c, a, _ = glyph_rgba(B, m['glyph'], sz); paste(img, c, a, x, y); drawn.append((m, x, y, sz))
    if labels:
        pr = B.note['labelPriority']; fnt = font(FONT_SERIF, int(round(labels['size'] * s))); boxes = []
        order = sorted(drawn, key=lambda t: (pr.index(t[0]['glyph']) if t[0]['glyph'] in pr else len(pr), t[0]['id']))
        tmp = ImageDraw.Draw(Image.new('L', (8, 8)))
        for m, x, y, sz in order:
            text = m['label'].split('·')[-1].strip()
            l, t, r, b = tmp.textbbox((0, 0), text, font=fnt); tw, th = r - l, b - t; pad = 5 * s
            placed = None
            for dx, dy in ((0, sz / 2 + th / 2 + pad + 2 * s), (0, -(sz / 2 + th / 2 + pad + 2 * s)), (sz / 2 + tw / 2 + pad + 3 * s, 0), (-(sz / 2 + tw / 2 + pad + 3 * s), 0)):
                bx = (x + dx - tw / 2 - pad, y + dy - th / 2 - pad, x + dx + tw / 2 + pad, y + dy + th / 2 + pad)
                if bx[0] < 2 or bx[1] < 2 or bx[2] > V.w - 2 or bx[3] > V.h - 2: continue
                if any(not (bx[2] < o[0] or bx[0] > o[2] or bx[3] < o[1] or bx[1] > o[3]) for o in boxes): continue
                ink_under = labels['road_ink'][int(max(0, bx[1])):int(bx[3]) + 1, int(max(0, bx[0])):int(bx[2]) + 1]
                if ink_under.size and float((ink_under > .4).mean()) > .02: continue          # never on a road stroke
                placed = bx; break
            if placed is None: continue                                                    # the lower priority label is hidden; its icon stays
            boxes.append(placed); x0, y0, x1, y1 = (int(round(v)) for v in placed)
            img[y0:y1, x0:x1] = img[y0:y1, x0:x1] * .08 + PAPER * .92
            for yy in (y0, y1 - 1): img[yy, x0:x1] = img[yy, x0:x1] * .25 + INK * .75
            for xx in (x0, x1 - 1): img[y0:y1, xx] = img[y0:y1, xx] * .25 + INK * .75
            draw_text(img, ((x0 + x1) / 2, (y0 + y1) / 2), text, fnt, INK, anchor='mm')
        labels['boxes'] = boxes
    if player:
        c, a, ink = glyph_rgba(B, 'player', player_px * s, tint=CINN, rotate=-player.get('heading', 0.0))
        x, y = V.to_px(player['x'], player['z']); paste(img, c, a, x, y)
        return a, ink
    return None, None


def minimap(B, mp, walked, at, scale=1.0, road_style='B', heading=30.0, layers=None, frame=True, wake='geumpyo_inn'):
    """the HUD minimap: 294 x 210 design px window at 120 m (336 x 240 m), frame 9 px, MINI_MARGIN px of backdrop round it.
    -> RGB [(228 + 24) s, (312 + 24) s, 3]; frame=False -> the window alone."""
    F = B.note['frames']['minimap']; ww, wh = F['window']; f = F['frameWidth']
    w, h = int(round(ww * scale)), int(round(wh * scale))
    V = View(at[0], at[1], w, h, 240.0 / wh / scale, scale)
    L = {} if layers is None else layers
    img = render(B, V, walked, road_style, L)
    marks = markers_in(B, mp, walked)
    pa, pink = draw_marks(B, img, V, marks, B.note['sizes']['minimap']['icon'], player=dict(x=at[0], z=at[1], heading=heading),
                          player_px=B.note['sizes']['minimap']['player'], wake=wake, margin=B.note['sizes']['minimap']['edgeMargin'])
    L['player_alpha'] = pa; L['player_ink'] = pink; L['view'] = V
    if not frame: return img
    fb = int(round(f * scale)); mg = int(round(MINI_MARGIN * scale))
    out = np.ones((h + 2 * fb + 2 * mg, w + 2 * fb + 2 * mg, 3)) * M.hexc('#2A2A26')
    out[mg:mg + h + 2 * fb, mg:mg + w + 2 * fb] = M.hexc('#110F0D')
    out[mg + fb:mg + fb + h, mg + fb:mg + fb + w] = img
    K = kit()
    if K.ok:
        over_rgba(out, K.frame('Frame.Mini', F['outer'][0], F['outer'][1], scale), mg, mg)    # 312 x 228 = 48 + 12 n (the kit's rule)
        npc = K.simple('Piece.North', scale)
        over_rgba(out, npc, int(round(out.shape[1] / 2 - npc.shape[1] / 2)), mg + int(round(F['northCentreFromTop'] * scale - npc.shape[0] / 2)))
    return out


# ---------------------------------------------------------------------------------------------------------------------
# measurements (AC-O6, O7, O9)
# ---------------------------------------------------------------------------------------------------------------------
def lum_img(img):
    return M.lum(img)


def road_contrast(img, L):
    """share of the road core pixels whose contrast against the non-road ground within 3 px is >= 4.5 / >= 3."""
    core = L['road_ink'] >= .70
    if core.sum() < 20: return None
    lum = lum_img(img); ground = (L['road_ink'] < .08) & (L['walked'] > .99)
    r = 3 + int(math.ceil(2 * 1.0))
    k = 2 * r + 1
    pad = lambda a: np.pad(a, r, mode='constant')
    def boxsum(a):
        c = np.cumsum(np.cumsum(pad(a), axis=0), axis=1); c = np.pad(c, ((1, 0), (1, 0)))
        return c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]
    s = boxsum(lum * ground); cnt = boxsum(ground.astype(float))
    ok = core & (cnt > 3)
    lg = s[ok] / cnt[ok]; lc = lum[ok]
    ratio = (np.maximum(lg, lc) + .05) / (np.minimum(lg, lc) + .05)
    return dict(core_px=int(ok.sum()), share_ge_4_5=round(float((ratio >= 4.5).mean()), 4), share_ge_3=round(float((ratio >= 3).mean()), 4),
                median=round(float(np.median(ratio)), 2), p05=round(float(np.quantile(ratio, .05)), 2))


def road_centreline(img, V, B):
    """per road class -> contrast ratios of the stroke AT ITS BAKED CENTRE LINE (the darkest of the +-1.5 px across) against the
    walked ground 3..6 px to both sides, one sample every 4 design px along the line. Dry and thin places are in the number
    (road_contrast only looks at pixels the ink already covers, so it cannot fail on a faint stroke). A sample whose surround
    is itself dark (another road, a ridge band, the wash) is left out: it is not a stroke-on-paper reading."""
    lum = lum_img(img); st, pt = B.strokes['strokes'], B.strokes['points']; out = {}
    offs = np.arange(-6.0, 6.01, .5) * V.scale; a = np.abs(offs) / V.scale
    for s in st:
        if s['cls'] not in (7, 8, 9): continue
        sl = slice(int(s['first']), int(s['first'] + s['count']))
        px, py = V.to_px(pt['x'][sl], pt['z'][sl]); P = np.stack([px, py], 1)
        if px.max() < 0 or px.min() > V.w or py.max() < 0 or py.min() > V.h or M.poly_len(P) < 12.0 * V.scale: continue
        D = M.resample(P, 4.0 * V.scale)
        if len(D) < 3: continue
        tg = np.gradient(D, axis=0); ln = np.hypot(tg[:, 0], tg[:, 1])
        n = np.stack([-tg[:, 1], tg[:, 0]], 1) / np.maximum(ln, 1e-6)[:, None]
        m = 8.0 * V.scale
        inside = (D[:, 0] > m) & (D[:, 0] < V.w - m) & (D[:, 1] > m) & (D[:, 1] < V.h - m) & (ln > 1e-6)
        if not inside.any(): continue
        X = D[inside, 0, None] + n[inside, 0, None] * offs[None]; Y = D[inside, 1, None] + n[inside, 1, None] * offs[None]
        prof = bilinear(lum, X - .5, Y - .5)
        core = prof[:, a <= 1.5].min(1); side = np.median(prof[:, (a >= 3) & (a <= 6)], 1)
        keep = side > .40
        if keep.any(): out.setdefault(int(s['cls']), []).append((side[keep] + .05) / (core[keep] + .05))
    return {c: np.concatenate(v) for c, v in out.items()}


def road_windows(B, win=(336.0, 240.0)):
    """every window of a regular tiling of the world (no overlap) that holds a baked road point -> [(cx, cz)]. One road
    sample is then measured once; the six fixed windows were a small, hand-picked share of the roads."""
    st, pt = B.strokes['strokes'], B.strokes['points']
    nx = int(math.ceil(M.WORLD_X / win[0])); nz = int(math.ceil(M.WORLD_Z / win[1])); has = np.zeros((nz, nx), bool)
    for s in st:
        if s['cls'] not in (7, 8, 9): continue
        sl = slice(int(s['first']), int(s['first'] + s['count']))
        P = M.resample(np.stack([pt['x'][sl], pt['z'][sl]], 1).astype(float), 8.0)
        has[np.clip((P[:, 1] // win[1]).astype(int), 0, nz - 1), np.clip((P[:, 0] // win[0]).astype(int), 0, nx - 1)] = True
    return [((j + .5) * win[0], (i + .5) * win[1]) for i in range(nz) for j in range(nx) if has[i, j]]


def road_centreline_all(B):
    """AC-O6.2 with the review's own reading (road_centreline) over EVERY road window, all walked: the minimap at 1080p and
    1440p (336 x 240 m tiles holding a road) and the unfolded map at its default zoom (740 x 760 px tiles at 2.66 m / px)."""
    walked = np.ones((M.FOG_H, M.FOG_W), bool); names = {9: 'daero', 8: 'gil', 7: 'soro'}; out = {}
    F = B.note['frames']['minimap']; ww, wh = F['window']; wins = road_windows(B)

    def table(acc, n):
        d = dict(windows=n)
        for c in sorted(acc, reverse=True):
            r = np.concatenate(acc[c])
            d[names[c]] = dict(samples=int(r.size), median=round(float(np.median(r)), 2), p05=round(float(np.quantile(r, .05)), 2),
                               share_ge_4_5=round(float((r >= 4.5).mean()), 4), share_ge_3=round(float((r >= 3).mean()), 4), ok=bool((r >= 4.5).mean() >= .95))
        return d
    for scale, tag in ((1.0, 'minimap_1080p'), (4 / 3, 'minimap_1440p')):
        acc = {}
        for at in wins:
            V = View(at[0], at[1], int(round(ww * scale)), int(round(wh * scale)), 240.0 / wh / scale, scale)
            for c, v in road_centreline(render(B, V, walked), V, B).items(): acc.setdefault(c, []).append(v)
        out[tag] = table(acc, len(wins))
    mpp = B.note['zoomBands'][2]['refMetresPerPx']; pw, ph = B.note['frames']['board']['printWindow']; acc = {}; n = 0
    for cz in np.arange(ph * mpp / 2, M.WORLD_Z + ph * mpp / 2 - 1, ph * mpp):
        for cx in np.arange(pw * mpp / 2, M.WORLD_X + pw * mpp / 2 - 1, pw * mpp):
            V = View(cx, cz, pw, ph, mpp, 1.0); n += 1
            for c, v in road_centreline(render(B, V, walked), V, B).items(): acc.setdefault(c, []).append(v)
    out['full_map_Z2'] = table(acc, n)
    out['floor_share_ge_4_5'] = .95
    out['note'] = ('road_centreline over every road window, all walked: one sample every 4 design px along each baked road line; stroke = the darkest of '
                   '+-1.5 px across, surround = median of 3..6 px to both sides; dry places, caps and junctions included')
    return out


def edge_numbers(B, walked, at_list):
    """the #308 walked edge on minimap windows of a partly walked save: no walked value on an unwalked cell (#214), where the
    crisp edge lies (metres inside the walked cells), and the rim's contrast against both sides. The edge is drawn in the
    formula EDGE_CGINC carries (fog_edge). On the mock walk the inset reads 5.25 .. 21.14 m (median 11.14) with the threshold
    formula and 2.06 .. 20.29 m (median 10.57) with formula F as staged on 2026-10-05 (FROM .12, SPAN .50, KCOVER 1.5; 2 % of
    the edge pixels lie under FROM x 32 m = 3.84 m, all by a corner of the unwalked land). With formula F the result also
    names the formula and the cginc it was read from."""
    F = B.note['frames']['minimap']; ww, wh = F['window']; E = B.note['values']['fogEdge']
    outside = 0; inset = []; rim_px = 0; checked = 0
    for at in at_list:
        V = View(at[0], at[1], ww, wh, 240.0 / wh, 1.0)
        w, d, _ = fog_crisp(walked, V.X, V.Z, E.get('crispEdge'))
        ci = np.clip((V.Z // M.FOG_CELL).astype(int), 0, M.FOG_H - 1); cj = np.clip((V.X // M.FOG_CELL).astype(int), 0, M.FOG_W - 1)
        cellw = walked[ci, cj]; checked += int(w.size)
        outside += int(((w > 0) & ~cellw).sum())
        rim_px += int(((w > .5) & (d <= E['px'])).sum())
        ys, xs = np.nonzero((w > 0) & (w < 1))
        if len(ys) == 0: continue
        px, pz = V.X[ys, xs], V.Z[ys, xs]; best = np.full(len(ys), 1e9)
        for di in range(-2, 3):
            for dj in range(-2, 3):
                i = ci[ys, xs] + di; j = cj[ys, xs] + dj
                ok = (i >= 0) & (i < M.FOG_H) & (j >= 0) & (j < M.FOG_W)
                un = ok & ~walked[np.clip(i, 0, M.FOG_H - 1), np.clip(j, 0, M.FOG_W - 1)]
                dx = np.maximum(np.maximum(j * M.FOG_CELL - px, px - (j + 1) * M.FOG_CELL), 0); dz = np.maximum(np.maximum(i * M.FOG_CELL - pz, pz - (i + 1) * M.FOG_CELL), 0)
                best = np.where(un, np.minimum(best, np.hypot(dx, dz)), best)
        inset.append(best[best < 1e8])
    inset = np.concatenate(inset) if inset else np.zeros(0)
    v = B.note['values']; paper, wash, ink = (M.hexc(v[k]) for k in ('paper', 'wash', 'ink')); rim = M.over(ink, wash, E['inkAlpha'])
    out = dict(px_checked=checked, walked_value_on_an_unwalked_cell_px=outside, rim_px=rim_px,
               edge_inset_m=dict(min=round(float(inset.min()), 2), median=round(float(np.median(inset)), 2), max=round(float(inset.max()), 2)) if inset.size else None,
               rim_colour='#%02X%02X%02X' % tuple(int(round(c * 255)) for c in rim), rim_vs_unwalked=round(M.contrast(rim, wash), 2), rim_vs_paper=round(M.contrast(rim, paper), 2),
               note='windows of the mock walk; inset = distance from an edge pixel to the nearest unwalked cell (the edge never reaches the 32 m grid)')
    q = edge_constants()
    if q is not None:        # formula F: the numbers are the cginc's, not the bundle's crispEdge (the threshold formula's result keeps its old keys)
        out.update(edge_formula='F', cginc=str(EDGE_CGINC).replace('\\', '/'), constants=q)
    return out


def straight_width(B, cls, rank, band, scale=1.0, pressure=None):
    """drawn width of a class on a straight 240 px test stroke at the class's LOWEST baked pressure. extent = ink pixels (coverage
    > .5) across the stroke per column; envelope = the largest extent inside one pattern pitch (teeth, hachures, merlons: the
    width of the band the eye reads). judged = envelope for a patterned row, median extent of the inked columns otherwise."""
    sty = B.stroke_style(cls, rank, band)
    if sty is None or sty['width'] <= 0: return 0.0
    c = B.classes[cls]; p = c['pressureRange'][0] if pressure is None else pressure
    if cls == 2 and rank == 1: p = max(p, 1.0)
    W = sty['width'] * scale; ar = B.atlas_rows[sty['row']]
    a = np.zeros((80, 300)); k = np.zeros((80, 300))
    P = np.array([[30.0, 40.0], [270.0, 40.0]])
    raster(a, k, P, np.array([0.0, 240.0]), np.array([p, p]), np.array([8, 8], np.uint8), W, B.rows[sty['row']], ar['axisV'],
           1024.0 * (W / 40.0) * (ar['uAspect'] or 1.0), 0.0, ar['uMode'] == 'stretch', 240.0)
    cols = a[:, 40:260] > .5
    rows = np.arange(80)[:, None]
    top = np.where(cols, rows, 999).min(axis=0); bot = np.where(cols, rows, -1).max(axis=0)
    extent = cols.sum(axis=0); inked = extent > 0
    pitch = max(3, int(math.ceil(48.0 * W / 40.0)))
    env = [int(bot[i:i + pitch].max() - top[i:i + pitch].min() + 1) for i in range(0, 220 - pitch) if inked[i:i + pitch].any()]
    patterned = ar['name'] in ('teeth', 'hachure', 'battlement')
    med = float(np.median(extent[inked])) if inked.any() else 0.0
    return dict(nominal=round(W, 2), min_pressure=p, mean_ink_px=round(float(a[:, 40:260].sum() / 220.0), 2), median_extent_px=med,
                p10_extent_px=float(np.quantile(extent[inked], .1)) if inked.any() else 0.0, envelope_px=float(np.median(env)) if env else 0.0,
                inked_share=round(float(inked.mean()), 3), judged_px=float(np.median(env)) if patterned and env else med)


def values_table(B):
    v = B.note['values']; paper, wash, ink, cin = (M.hexc(v[k]) for k in ('paper', 'wash', 'ink', 'cinnabar'))
    dark = paper * (1 - B.note['terrain']['slopeWashMax']); light = paper * (1 - v['slopeWashRange'][0])
    daero = M.over(ink, paper, B.classes[9]['inkAlpha']); soro = M.over(ink, paper, B.classes[7]['inkAlpha']); ridge = M.over(ink, dark, B.classes[2]['ranks'][0]['inkAlpha'])
    t = {
        'walked_vs_unwalked': (paper, wash, 2.3), 'darkest_wash_vs_unwalked': (dark, wash, 1.5), 'road_daero_vs_paper': (daero, paper, 7.0),
        'road_soro_vs_paper': (soro, paper, 7.0), 'road_daero_vs_darkest_wash': (daero, dark, 4.5), 'ink_line_vs_unwalked': (M.over(ink, wash, .92), wash, 4.5),
        'ridge_ink_vs_wash': (ridge, dark, 4.5), 'cinnabar_vs_paper': (cin, paper, 4.0), 'cinnabar_vs_wash': (cin, wash, None),
    }
    out = {k: dict(ratio=round(M.contrast(a, b), 2), floor=fl, ok=(fl is None or M.contrast(a, b) >= fl)) for k, (a, b, fl) in t.items()}
    cv = {}
    for kind in ('protan', 'deutan', 'tritan'):
        cv[kind] = dict(cinnabar_vs_paper=round(M.contrast(M.cvd(cin, kind), M.cvd(paper, kind)), 2),
                        paper_vs_wash=round(M.contrast(M.cvd(paper, kind), M.cvd(wash, kind)), 2),
                        road_vs_paper=round(M.contrast(M.cvd(daero, kind), M.cvd(paper, kind)), 2))
    return out, cv


def icon_cells(B, sheet='S', channel=0):
    S = B.icons[sheet]; cell = 32 if sheet == 'S' else 64
    return {g['id']: S[(g['cell'] // 8) * cell:(g['cell'] // 8 + 1) * cell, (g['cell'] % 8) * cell:(g['cell'] % 8 + 1) * cell, channel] for g in B.note['glyphs']}


def icon_numbers(B):
    """AC-O6.4 and the review's two icon questions, measured on the S sheet (the one every minimap icon is drawn from):
    thin parts (a piece of 3 px or more that a 2 x 2 opening removes = a body part under 2 px; a pointed tip loses 1-2 px),
    confusable pairs (Pearson correlation of the ink of the 21 place signs at 22 px; the second row blurs both by 0.8 px, a
    stricter reading that forgives a 1 px shift), and the heading mark's ink box."""
    import map308_art as ART
    out = {}
    cells = icon_cells(B); worst = 1.0; names = []; thin32 = {}; thin22 = {}
    for gid, c in cells.items():
        sv = ART.open_survival(ART.resize_channel(c, 22) >= .5, 2)
        worst = min(worst, sv)
        if sv < .9: names.append(gid)
        a = ART.thin_parts(c >= .5, 2); b = ART.thin_parts(ART.resize_channel(c, 22) >= .5, 2)
        if a[0] >= 3: thin32[gid] = a[0]
        if b[0] >= 3: thin22[gid] = b[0]
    out['S_at_22px_stroke_ge_2px_survival_min'] = round(worst, 4); out['S_at_22px_below_0_9'] = names
    out['thin_parts'] = dict(rule='largest piece (px) removed by a 2 x 2 opening; >= 3 px = a body part thinner than 2 px',
                             glyphs_with_a_thin_part_at_S_32px=thin32, glyphs_with_a_thin_part_at_22px=thin22, glyphs=len(cells))
    place = {k: v for k, v in cells.items() if k in ART.PLACE_GLYPHS}
    pairs = {}
    for blur, tag in ((0.0, 'plain'), (0.8, 'blur_0_8px')):
        pr = ART.pair_correlation(place, 22, blur)
        pairs[tag] = dict(pairs=len(pr), above_0_70=sum(r > .70 for r, _, _ in pr), max=round(pr[0][0], 3), max_pair=[pr[0][1], pr[0][2]],
                          top=[[round(r, 3), a, b] for r, a, b in pr[:6]])
        d = {(a, b): r for r, a, b in pr}
        pairs[tag]['named'] = {f'{a}-{b}': round(d.get((a, b), d.get((b, a), 0.0)), 3) for a, b in
                               (('gaekju', 'relay'), ('inn', 'relay'), ('checkpoint', 'mine'), ('gate', 'waterside'))}
    used = sorted(set(B.note['markerGlyphs'].values()) | set(B.note['kindFallback'].values()))
    inuse = {k: v for k, v in place.items() if k in used}
    pr = ART.pair_correlation(inuse, 22, 0.0, shift=1)
    pairs['shift_1px_in_use'] = dict(glyphs=len(inuse), pairs=len(pr), above_0_70=sum(r > .70 for r, _, _ in pr), max=round(pr[0][0], 3), max_pair=[pr[0][1], pr[0][2]],
                                     top=[[round(r, 3), a, b] for r, a, b in pr[:6]], hall=[[round(r, 3), a if b == 'hall' else b] for r, a, b in pr if 'hall' in (a, b)][:4],
                                     rule='largest Pearson correlation over every offset of one sign by -1..1 px in x and y; the place signs a marker of Map.asset or a kind fallback uses')
    out['confusable_pairs_at_22px'] = pairs
    for px in (22, 28, 48):
        c, a, ink = glyph_rgba(B, 'player', px)
        ys, xs = np.nonzero(ink >= .5); out[f'player_ink_px_at_{px}'] = [int(xs.max() - xs.min() + 1), int(ys.max() - ys.min() + 1)]
    c, a, ink = glyph_rgba(B, 'player', 28)
    ys, xs = np.nonzero(a >= .5); out['player_figure_px_at_28_with_plate'] = [int(xs.max() - xs.min() + 1), int(ys.max() - ys.min() + 1)]
    out['player_half_turn_self_correlation_at_28'] = round(float(np.corrcoef(ink.ravel(), ink[::-1, ::-1].ravel())[0, 1]), 3)
    cols = (ink >= .5).sum(axis=0); rows = np.nonzero((ink >= .5).any(axis=1))[0]; mid = ink.shape[1] // 2
    centre = np.nonzero(ink[:, mid - 1:mid + 1].max(axis=1) >= .5)[0]
    out['player_notch_px_at_28'] = int(rows.max() - centre.max()) if len(centre) else 0
    # the brush mark: figure = both inks; heading = the pointed tip, and the second ink (ferrule + handle) at the back only
    c0 = B.glyph['player']; S = B.icons['S'][(c0 // 8) * 32:(c0 // 8 + 1) * 32, (c0 % 8) * 32:(c0 % 8 + 1) * 32]
    for px in (22, 28):
        r_, g_, b_ = (ART.resize_channel(S[..., k], px) for k in (0, 1, 2))
        fig = np.maximum(r_, b_) >= .5; ys, xs = np.nonzero(fig); pl = np.maximum(fig, g_ >= .5); yp, xp = np.nonzero(pl)
        tone = r_ * (1 - b_) - b_                                             # cinnabar tuft +1, second ink -1, paper 0
        cols = fig.sum(axis=0); top, bot = ys.min(), ys.max(); L = bot - top + 1
        front = fig[top:top + max(2, L // 5)].sum(axis=1)                      # widths over the first fifth of the length
        handle = (b_ >= .5); hy = np.nonzero(handle.any(axis=1))[0]
        out[f'player_mark_at_{px}'] = dict(
            figure_px=[int(xs.max() - xs.min() + 1), int(L)], with_plate_px=[int(xp.max() - xp.min() + 1), int(yp.max() - yp.min() + 1)],
            length_to_width=round(float(L) / float(xs.max() - xs.min() + 1), 2), second_ink_px=int(handle.sum()),
            tip_width_px_rows_1_to_3=[int(v) for v in fig[top:top + 3].sum(axis=1)],
            second_ink_in_the_back_share=round(float(handle[top + int(L * .6):].sum()) / max(1, int(handle.sum())), 3),
            ferrule_band_rows=int(((handle.sum(axis=1) >= .8 * np.maximum(fig.sum(axis=1), 1)) & (fig.sum(axis=1) > 0))[top + int(L * .55):].sum()),
            half_turn_correlation_silhouette=round(float(np.corrcoef(fig.ravel().astype(float), fig[::-1, ::-1].ravel().astype(float))[0, 1]), 3),
            half_turn_correlation_two_inks=round(float(np.corrcoef(tone.ravel(), tone[::-1, ::-1].ravel())[0, 1]), 3))
    out['second_ink_cells'] = [g['id'] for g in B.note['glyphs'] if B.icons['S'][(g['cell'] // 8) * 32:(g['cell'] // 8 + 1) * 32, (g['cell'] % 8) * 32:(g['cell'] % 8 + 1) * 32, 2].max() > 0]
    return out


def ridge_windows(B, step_m=60.0, win=(336.0, 240.0), need_m=40.0):
    """review question: how often does a minimap window on a road hold terrain STROKES? One window (336 x 240 m, the outdoor
    minimap) every step_m along every baked road stroke; the ridge length inside it (2 m samples), ranks 1-2 and any rank."""
    st, pt = B.strokes['strokes'], B.strokes['points']
    P = lambda s: np.stack([pt['x'][s['first']:s['first'] + s['count']], pt['z'][s['first']:s['first'] + s['count']]], 1).astype(float)
    ridges = [(int(s['rank']), M.resample(P(s), 2.0)) for s in st if s['cls'] == 2]
    cent = []
    for s in st:
        if s['cls'] not in (7, 8, 9): continue
        r = P(s); d = M.cum_len(r)
        cent += [(np.interp(x, d, r[:, 0]), np.interp(x, d, r[:, 1])) for x in np.arange(0, d[-1], step_m)]
    wash = B.terrain[0][..., 0]; n = len(cent); h12 = hany = washy = washy_no = 0
    for cx, cz in cent:
        l12 = l3 = 0.0
        for rk, D in ridges:
            ins = (np.abs(D[:, 0] - cx) <= win[0] / 2) & (np.abs(D[:, 1] - cz) <= win[1] / 2)
            if not ins.any(): continue
            seg = np.hypot(*np.diff(D, axis=0).T); m = ins[:-1] & ins[1:]
            if rk <= 2: l12 += seg[m].sum()
            else: l3 += seg[m].sum()
        h12 += l12 >= need_m; hany += (l12 + l3) >= need_m
        j0, j1 = int(max(0, (cx - win[0] / 2) // M.CELL)), int(min(M.TW, (cx + win[0] / 2) // M.CELL))
        i0, i1 = int(max(0, (cz - win[1] / 2) // M.CELL)), int(min(M.TH, (cz + win[1] / 2) // M.CELL))
        ws = float((wash[i0:i1, j0:j1] > .25).mean()) if (i1 > i0 and j1 > j0) else 0.0
        if ws >= .20:
            washy += 1; washy_no += (l12 + l3) < need_m
    km = {str(r): round(sum(M.poly_len(D) for rk, D in ridges if rk == r) / 1000, 2) for r in (1, 2, 3, 4)}
    return dict(windows=n, step_m=step_m, window_m=list(win), need_ridge_m=need_m, share_rank_1_2=round(float(h12) / max(n, 1), 4),
                share_any_rank=round(float(hany) / max(n, 1), 4), ridge_km_by_rank=km, ridge_km=round(sum(km.values()), 2),
                windows_with_slope_wash=int(washy), of_those_without_a_ridge=int(washy_no),
                note='windows_with_slope_wash = at least 20 % of the window carries a visible slope wash (R > .25, steeper than 28 deg)')


def measure(B, mp, log=print):
    res = dict(id='map308_legibility', spec='SPEC-MAP-OVERHAUL-308 AC-O6 / O7 / O9', stage=B.note['stage'], method=(
        'numpy twin composite of the bundle (map308_twin.py); sRGB-coded over; WCAG relative-luminance contrast. Token rows are computed '
        'from the notation values; window rows are measured on the composite pixels. Not measured in Unity.'))
    res['values'], res['cvd'] = values_table(B)
    widths = {}
    for scale, tag in ((1.0, '1080p'), (4 / 3, '1440p')):
        widths[tag] = {name: straight_width(B, cls, rank, 1, scale) for name, cls, rank in
                       (('daero', 9, 0), ('gil', 8, 0), ('soro', 7, 0), ('ridge1', 2, 1), ('ridge2', 2, 2), ('ridge3', 2, 3), ('cliff', 3, 0),
                        ('outer_range', 4, 0), ('wall', 5, 0))}
    res['widths_Z1'] = widths
    floors = dict(daero=4.0, gil=3.0, soro=2.0, ridge1=8.0)
    res['min_width_Z1_1080p'] = {k: dict(floor=v, judged_px=widths['1080p'][k]['judged_px'], ok=widths['1080p'][k]['judged_px'] >= v) for k, v in floors.items()}
    res['icons'] = icon_numbers(B)
    res['ridge_windows'] = ridge_windows(B)
    walked_all = mock_walk(B, all_walked=True); walked = mock_walk(B)
    wins = {}; leak = dict(px_checked=0, max_dev_from_wash=0.0, stroke_px=0, terrain_px=0)
    agree = dict(cells=0, mismatch=0); centre = {}
    for scale, tag in ((1.0, '1080p'), (4 / 3, '1440p')):
        for wdef in WINDOWS:
            F = B.note['frames']['minimap']; ww, wh = F['window']
            Vs = View(wdef['at'][0], wdef['at'][1], int(round(ww * scale)), int(round(wh * scale)), 240.0 / wh / scale, scale)
            L = {}; img_all = render(B, Vs, walked_all, layers=L); rc = road_contrast(img_all, L)
            for c, v in road_centreline(img_all, Vs, B).items(): centre.setdefault((tag, c), []).append(v)
            L2 = {}; img2 = minimap(B, mp, walked, wdef['at'], scale, layers=L2, frame=False)
            unk = L2['walked'] <= 0
            # AC-O7: the unwalked pixels are the wash x grain and carry no stroke / terrain ink (icons excluded: none is drawn there)
            pa = L2['player_alpha'] if L2['player_alpha'] is not None else None
            V = L2['view']; base = render(B, V, walked)
            dev = np.abs(base - L2['unknown'])[unk]
            leak['px_checked'] += int(unk.sum()); leak['max_dev_from_wash'] = max(leak['max_dev_from_wash'], float(dev.max()) if dev.size else 0.0)
            leak['stroke_px'] += int(((L2['road_ink'] + L2['other_ink'] + L2['road_paper']) > 0)[unk].sum()); leak['terrain_px'] += int((L2['terrain_ink'] > 0)[unk].sum())
            walked_px = L2['walked'] >= 1
            wl = lum_img(base)
            wins[f"{wdef['id']}@{tag}"] = dict(at=list(wdef['at']), road=rc, walked_share=round(float(walked_px.mean()), 3),
                                               walked_median_lum=round(float(np.median(wl[walked_px])), 4) if walked_px.any() else None,
                                               unwalked_median_lum=round(float(np.median(wl[unk])), 4) if unk.any() else None)
            flat = walked_px & L2['flat']
            if flat.any() and unk.any():
                a, b = np.median(wl[flat]), np.median(wl[unk])
                wins[f"{wdef['id']}@{tag}"]['flat_walked_vs_unwalked'] = round(float((max(a, b) + .05) / (min(a, b) + .05)), 2)
                a = np.median(wl[walked_px])
                wins[f"{wdef['id']}@{tag}"]['all_walked_median_vs_unwalked'] = round(float((max(a, b) + .05) / (min(a, b) + .05)), 2)
            if tag == '1080p':
                # AC-O9: the same bundle through both maps: presence of water / ridge / road per 4 m cell must agree
                Vm = View(wdef['at'][0], wdef['at'][1], 84, 60, 4.0, 1.0); Lm = {}; render(B, Vm, walked_all, layers=Lm)
                Vf = View(wdef['at'][0], wdef['at'][1], 84, 60, 4.0, 1.0); Lf = {}; render(B, Vf, walked_all, layers=Lf)
                for c in (1, 2):
                    agree['cells'] += Lm['presence'][c].size; agree['mismatch'] += int((Lm['presence'][c] != Lf['presence'][c]).sum())
                agree['cells'] += Lm['road_ink'].size; agree['mismatch'] += int(((Lm['road_ink'] > .3) != (Lf['road_ink'] > .3)).sum())
    res['windows'] = wins
    rs = [w['road'] for w in wins.values() if w['road']]
    tot = sum(r['core_px'] for r in rs)
    res['road_core_vs_surround'] = dict(core_px=tot, share_ge_4_5=round(sum(r['share_ge_4_5'] * r['core_px'] for r in rs) / max(tot, 1), 4),
                                        share_ge_3=round(sum(r['share_ge_3'] * r['core_px'] for r in rs) / max(tot, 1), 4), floor_share_ge_4_5=.95,
                                        note='only pixels the road ink already covers (cover >= .70): a faint or dry stroke cannot fail here; '
                                             'road_centre_line is the reading along the baked centre lines')
    names = {9: 'daero', 8: 'gil', 7: 'soro'}; res['road_centre_line'] = {}
    for (tag, c), v in sorted(centre.items()):
        r = np.concatenate(v)
        res['road_centre_line'].setdefault(tag, {})[names[c]] = dict(
            samples=int(r.size), median=round(float(np.median(r)), 2), p05=round(float(np.quantile(r, .05)), 2),
            share_ge_4_5=round(float((r >= 4.5).mean()), 4), share_ge_3=round(float((r >= 3).mean()), 4))
    res['road_centre_line']['note'] = ('six minimap windows, all walked; one sample every 4 design px along each baked road line; stroke = the '
                                       'darkest of +-1.5 px across, surround = median of 3..6 px to both sides; dry and thin places included')
    res['road_centre_line_all'] = road_centreline_all(B)
    res['walked_edge'] = edge_numbers(B, walked, [w['at'] for w in WINDOWS])
    res['fog_214'] = dict(leak, grain_amp=B.note['values']['washGrainAmp'],
                          note='unwalked pixels of the six minimap windows (mock walk): deviation of the composite from wash x grain, and ink pixels there')
    res['two_maps_agree'] = dict(agree, note='both maps read one bundle through one function; presence masks at 4 m cells compared in the six windows')
    return res


# ---------------------------------------------------------------------------------------------------------------------
# sheets
# ---------------------------------------------------------------------------------------------------------------------
def to_u8(img):
    return np.round(np.clip(img, 0, 1) * 255).astype(np.uint8)


def upscale(img, k):
    return np.repeat(np.repeat(img, k, axis=0), k, axis=1)


def sheet_mini(B, mp, log):
    walked = mock_walk(B); cap = font(FONT_SANS, 15)
    tiles = [minimap(B, mp, walked, w['at'], 1.0) for w in WINDOWS]
    th, tw = tiles[0].shape[:2]; pad = 22
    W = pad + 3 * (tw + pad) + 2 * tw + pad + pad; H = pad + 2 * (th + 34 + pad) + 34
    H2 = pad + 2 * th + 34 + pad
    img = np.ones((max(H, H2 * 1 + (th + 34 + pad)), W, 3)) * M.hexc('#2A2A26')
    for i, (t, w) in enumerate(zip(tiles, WINDOWS)):
        x = pad + (i % 3) * (tw + pad); y = pad + (i // 3) * (th + 34 + pad)
        img[y:y + th, x:x + tw] = t
        draw_text(img, (x, y + th + 6), f"{w['ko']}  ({w['at'][0]}, {w['at'][1]})  120 m · 1:1", cap, M.hexc('#D6D1C4'))
    x = pad + 3 * (tw + pad); big = upscale(tiles[0], 2)
    img[pad:pad + 2 * th, x:x + 2 * tw] = big
    draw_text(img, (x, pad + 2 * th + 6), f"{WINDOWS[0]['ko']} · 2배 확대", cap, M.hexc('#D6D1C4'))
    return img


def sheet_roads(B, mp, log):
    walked = mock_walk(B, all_walked=True); cap = font(FONT_SANS, 15); at = WINDOWS[1]['at']
    names = [('A', 'A 옛 지도식 곧은 선 (12 m로 편 선 · 2.6 px, 지금 미니맵)'), ('B', 'B 갈필 붓 띠 (실제 길 · 굵기 3등급 · 길 밑 여백) — 권장'),
             ('C', 'C 판각 띠 (쌍선 + 점)')]
    tiles = [minimap(B, mp, walked, at, 1.0, road_style=k) for k, _ in names]
    th, tw = tiles[0].shape[:2]; pad = 22
    img = np.ones((pad + th + 34 + pad + 2 * th + 34 + pad, pad + 3 * (2 * tw + pad), 3)) * M.hexc('#2A2A26')
    for i, (t, (k, name)) in enumerate(zip(tiles, names)):
        x = pad + i * (2 * tw + pad)
        img[pad:pad + th, x:x + tw] = t
        draw_text(img, (x, pad + th + 6), name + ' · 1:1', cap, M.hexc('#D6D1C4'))
        y = pad + th + 34 + pad
        img[y:y + 2 * th, x:x + 2 * tw] = upscale(t, 2)
        draw_text(img, (x, y + 2 * th + 6), '2배 확대', cap, M.hexc('#D6D1C4'))
    return img


def sheet_notation(B, mp, log):
    cap = font(FONT_SANS, 14); head = font(FONT_SERIF, 20)
    W, H = 1500, 1180; img = np.ones((H, W, 3)) * PAPER
    draw_text(img, (24, 16), '오행부 지도 표기 — SPEC-MAP-OVERHAUL-308 (묶음에서 직접 그림, 1:1 px)', head, INK)
    # stroke classes at Z1 / Z2 / Z3
    y = 64; x0 = 24
    for bi, bname in ((1, 'Z1 미니맵 야외'), (2, 'Z2 펼친 지도 기본'), (3, 'Z3 전도')):
        draw_text(img, (x0 + 150 + (bi - 1) * 250, y - 4), bname, cap, INK)
    y += 22
    rows = [(9, 0, '큰길 (대로)'), (8, 0, '길'), (7, 0, '샛길 (소로)'), (10, 0, '다리'), (11, 0, '거리 눈금'), (2, 1, '산줄기 1등급'), (2, 2, '산줄기 2등급'),
            (2, 3, '산줄기 3등급'), (3, 0, '벼랑 (안쪽 단애)'), (4, 0, '끝 연봉'), (5, 0, '성벽'), (6, 0, '터 선'), (1, 0, '물줄기')]
    for cls, rank, name in rows:
        draw_text(img, (x0, y + 8), name, cap, INK)
        for bi in (1, 2, 3):
            sty = B.stroke_style(cls, rank, bi)
            if sty is None or sty['width'] <= 0:
                draw_text(img, (x0 + 150 + (bi - 1) * 250, y + 8), '— (숨김)', cap, M.hexc('#5A574F')); continue
            a = np.zeros((34, 230)); k = np.zeros((34, 230)); ar = B.atlas_rows[sty['row']]
            t = np.linspace(0, 1, 40); P = np.stack([10 + 210 * t, 17 + 5 * np.sin(t * 5.5)], 1)
            if cls == 11: P = np.array([[110.0, 10.0], [110.0, 24.0]])
            if cls == 10: P = np.array([[80.0, 17.0], [150.0, 17.0]])
            d = M.cum_len(P); pr = np.full(len(P), 1.0); fl = np.full(len(P), 8, np.uint8)
            if cls in (7, 8, 9): fl[0] |= 1; fl[-1] |= 2
            raster(a, k, P, d, pr, fl, sty['width'], B.rows[sty['row']], ar['axisV'], 1024.0 * (sty['width'] / 40.0) * (ar['uAspect'] or 1.0),
                   sty['knock'], ar['uMode'] == 'stretch', float(d[-1]))
            xx = x0 + 150 + (bi - 1) * 250; sub = img[y:y + 34, xx:xx + 230]
            a = a * sty['alpha']; sub[:] = sub * (1 - a[..., None]) + INK * a[..., None]
            draw_text(img, (xx + 232 - 60, y + 20), f"{sty['width']:g}px", cap, M.hexc('#5A574F'))
        y += 38
    # patterns
    y0 = 64; xp = 940
    T = B.note['terrain']; per = T['patternPeriodM'][1]
    draw_text(img, (xp, y0 - 4), f'무늬 (map308_pattern.png, Z1 주기 {per:g} m)', cap, INK); y = y0 + 22
    V = View(0, 0, 250, 110, 1.14)
    pat = bilinear(B.pattern[0], V.X / per * 256 - .5, -V.Z / per * 256 - .5, wrap=True)
    grad = np.linspace(0, 1, 250)[None].repeat(110, 0)
    for name, a, bg in (('숲 (밀도 0 → 1)', np.clip((pat[..., 0] - (1 - grad)) * T['forest'].get('gain', 6), 0, 1) * T['forest'].get('dotInk', .62), PAPER),
                        ('물결', pat[..., 1] * T['water'].get('rippleInk', .5), PAPER),
                        ('암준 (급사면)', pat[..., 2] * T.get('rockInk', .7), PAPER * (1 - .18))):
        sub = img[y:y + 110, xp:xp + 250]; sub[:] = bg * (1 - a[..., None]) + INK * a[..., None]
        draw_text(img, (xp + 258, y + 44), name, cap, INK); y += 122
    g = pat[..., 3]; sub = img[y:y + 110, xp:xp + 250]; sub[:] = WASH * (1 + (g[..., None] - .5) * 2 * B.note['values']['washGrainAmp'])
    draw_text(img, (xp + 258, y + 44), '걷지 않은 땅 (씻김 + 결)', cap, INK); y += 126
    # values
    vt, cv = values_table(B)
    draw_text(img, (xp, y), '값 구조 · 대비 (토큰 계산값)', cap, INK); y += 22
    sw = [('종이', PAPER), ('담묵 .10', PAPER * .9), ('담묵 .18', PAPER * .82), ('씻김', WASH), ('먹 α.84', M.over(INK, PAPER, .84)), ('먹 α.92', M.over(INK, PAPER, .92)), ('주사', CINN)]
    for i, (nm, c) in enumerate(sw):
        img[y:y + 34, xp + i * 76:xp + i * 76 + 70] = c
        draw_text(img, (xp + i * 76, y + 38), nm, cap, INK)
    y += 62
    for k, v in vt.items():
        draw_text(img, (xp, y), f"{k}: {v['ratio']}:1" + (f"  (하한 {v['floor']})" if v['floor'] else ''), cap, INK if v['ok'] else CINN); y += 19
    # icons
    yi = 620
    draw_text(img, (24, yi), '아이콘 L (64 px 칸 → 30 px) / S (32 px 칸 → 22 px), 걷지 않은 땅·걸은 땅 위', cap, INK); yi += 26
    img[yi:yi + 250, 24:24 + 880] = WASH; img[yi + 125:yi + 250, 24:24 + 880] = PAPER * .86
    gl = B.note['glyphs']
    for i, g in enumerate(gl):
        col_i, row_i = i % 13, i // 13
        for half, (sz, oy) in enumerate(((30, 26), (22, 86))):
            for bgrow in (0, 1):
                tint = CINN if g['id'] in ('player', 'objective') else None
                c, a, _ = glyph_rgba(B, g['id'], sz if g['id'] != 'player' else (48 if sz == 30 else 28), tint=tint)
                if row_i == bgrow:
                    paste(img, c, a, 24 + 40 + col_i * 66, yi + bgrow * 125 + oy)
    yi += 258
    for i, g in enumerate(gl):
        draw_text(img, (24 + (i % 7) * 126, yi + (i // 7) * 19), f"{g['cell']:02d} {g['nameKo']}" + (' (예약)' if g.get('reserved') else ''), cap, INK)
    # frames
    yf = yi + 4 * 19 + 20
    draw_text(img, (24, yf), '틀 = 테마 키트(SPEC-UI-THEME-308) 칸: Frame.Mini · Piece.North · Frame.MapBoard · Plate.Icon · Inlay.Line · Corner.Fret', cap, INK); yf += 24
    area = np.ones((150, 880, 3)) * M.hexc('#2A2A26')
    K = kit()
    if K.ok:
        area[14 + 9:14 + 111, 16 + 9:16 + 195] = PAPER
        over_rgba(area, K.frame('Frame.Mini', 204, 120), 16, 14)
        npc = K.simple('Piece.North'); over_rgba(area, npc, 16 + 102 - npc.shape[1] // 2, 14 + 5 - npc.shape[0] // 2)
        area[14:14 + 118, 250:250 + 296] = M.hexc('#110F0D'); over_rgba(area, K.frame('Frame.MapBoard', 296, 118), 250, 14)
        area[14 + 18:14 + 118, 250 + 18:250 + 278] = M.hexc('#E6E2D7')
        over_rgba(area, K.frame('Plate.Icon', 64, 64), 580, 40)
        c, a, _ = glyph_rgba(B, 'inn', 34); paste(area, c, a, 580 + 32, 40 + 32)
        ln = K.simple('Inlay.Line'); over_rgba(area, ln, 670, 60)
        cf = K.simple('Corner.Fret'); over_rgba(area, upscale(cf, 2), 820, 50)
    img[yf:yf + 150, 24:24 + 880] = area
    return img


def sheet_full(B, mp, log):
    """1920 x 1080 page mock: veil, the lacquer board of frames.board.rect, sheet 800 x 820 at (560, 140), print window 740 x 760."""
    walked = mock_walk(B); page = np.ones((1080, 1920, 3)) * VEIL
    F = B.note['frames']; bx0, by0, bx1, by1 = F['board']['rect']
    page[by0:by1, bx0:bx1] = M.hexc('#110F0D'); K = kit()
    if K.ok: over_rgba(page, K.frame('Frame.MapBoard', bx1 - bx0, by1 - by0), bx0, by0)
    sx0, sy0 = F['board'].get('sheetAt', [560, 140]); page[sy0:sy0 + 820, sx0:sx0 + 800] = M.hexc('#E6E2D7')
    px0, py0 = sx0 + 30, sy0 + 30
    at = WINDOWS[0]['at']
    V = View(at[0], at[1], 740, 760, 4.0 / 1.5, 1.0); L = {}; V.sheet = True     # #308 map 4: the unfolded sheet draws the sheet's rim
    img = render(B, V, walked, layers=L)
    marks = markers_in(B, mp, walked)
    draw_marks(B, img, V, marks, 30, player=dict(x=at[0], z=at[1], heading=30.0), player_px=48, wake='geumpyo_inn', margin=10,
               labels=dict(size=21, road_ink=L['road_ink']))
    page[py0:py0 + 760, px0:px0 + 740] = img
    # legend (left column): 7 mark rows at 72 px + the 3 x 3 terrain and road cells drawn from the bundle
    cap = font(FONT_SANS, 20); small = font(FONT_SANS, 16); head = font(FONT_SERIF, 24)
    lx, ly = 70, 150; pale = M.hexc('#E6E2D7')
    draw_text(page, (lx, ly - 44), '범례', head, pale)
    plate = K.frame('Plate.Icon', 64, 64) if K.ok else None
    for i, row in enumerate([r for r in B.note['legend'] if r['group'] == 'marks']):
        y = ly + i * 72
        if plate is not None: over_rgba(page, plate, lx, y)
        if 'glyph' in row:
            if row.get('withRing'):
                c, a, _ = glyph_rgba(B, row['withRing'], 34 * 1.5); paste(page, c, a, lx + 32, y + 32)
            c, a, _ = glyph_rgba(B, row['glyph'], 34, tint=CINN if row.get('tint') else None); paste(page, c, a, lx + 32, y + 32)
        else:
            g = B.pattern[0][:40, :40, 3]; page[y + 12:y + 52, lx + 12:lx + 52] = WASH * (1 + (g[..., None] - .5) * .1)
        draw_text(page, (lx + 80, y + 20), row['name'], cap, pale)
    ty = ly + 7 * 72 + 30
    draw_text(page, (lx, ty - 34), '지형과 길', head, pale)
    cells = [r for r in B.note['legend'] if r['group'] == 'terrain']
    for i, row in enumerate(cells):
        x = lx + (i % 3) * 150; y = ty + (i // 3) * 74
        sw = np.ones((24, 44, 3)) * PAPER
        if 'strokeClass' in row:
            for cls in ([row['over']] if 'over' in row else []) + [row['strokeClass']]:
                sty = B.stroke_style(cls, row.get('rank', 0), 2); ar = B.atlas_rows[sty['row']]
                a = np.zeros((24, 44)); k = np.zeros((24, 44)); P = np.array([[2.0, 12.0], [42.0, 12.0]]) if cls != 10 else np.array([[10.0, 12.0], [34.0, 12.0]])
                raster(a, k, P, M.cum_len(P), np.ones(2), np.full(2, 8, np.uint8), sty['width'], B.rows[sty['row']], ar['axisV'],
                       1024.0 * (sty['width'] / 40.0) * (ar['uAspect'] or 1.0), 0, ar['uMode'] == 'stretch', float(M.cum_len(P)[-1]))
                a *= sty['alpha']; sw = sw * (1 - a[..., None]) + INK * a[..., None]
        else:
            ch = {'r': 0, 'g': 1}[row['pattern']]; Tn = B.note['terrain']
            pp = B.pattern[0][:24, :44, ch] * (Tn['forest'].get('dotInk', .62) if ch == 0 else Tn['water'].get('rippleInk', .5))
            sw = sw * (1 - pp[..., None]) + INK * pp[..., None]
            if row.get('shore'): sw[3:5, :] = M.over(INK, PAPER, Tn['water'].get('shoreInk', .8))
        page[y:y + 24, x:x + 44] = sw
        draw_text(page, (x + 52, y - 1), row['name'], small, pale)
    # the whole-world view (Z3) as an inset on the right: the same bundle at 7.9 m/px
    Vw = View(2000, 3000, 507, 760, 6000.0 / 760.0, 1.0); Vw.sheet = True; wimg = render(B, Vw, walked)
    draw_marks(B, wimg, Vw, marks, 22, player=dict(x=at[0], z=at[1], heading=30.0), player_px=28, margin=4)
    wx, wy = 1395, 176; page[wy:wy + 760, wx:wx + 507] = wimg
    draw_text(page, (wx, wy - 30), '전체 보기 (7.9 m/px) — 같은 묶음', small, pale)
    draw_text(page, (px0, py0 - 24), '현재 위치 보기 (2.66 m/px)', small, M.hexc('#5A574F'))
    return page


def main():
    M.utf8()
    ap = argparse.ArgumentParser()
    ap.add_argument('--bundle', default=str(M.OUT)); ap.add_argument('--out', default=str(M.CONCEPT))
    ap.add_argument('--only', default='', help='comma list of: mini, roads, notation, full, legibility')
    ap.add_argument('--cginc', default='', help='the MapFog308.cginc whose MapFog308_Edge is drawn (default: the live shader); a stage file before its deploy')
    a = ap.parse_args(); t0 = time.time(); out = Path(a.out); only = [s for s in a.only.split(',') if s]
    if a.cginc: use_cginc(a.cginc)
    B = Bundle(a.bundle); mp = M.parse_map()
    print(f"[map308_twin] bundle stage {B.note['stage']} -> {M.rel(out)}")
    if edge_constants() is not None:        # (the threshold formula prints what it always printed)
        q = edge_constants(); print(f"  walked edge = formula F of {str(EDGE_CGINC).replace(chr(92), '/')} (FROM {q['FROM']:g} SPAN {q['SPAN']:g} KCOVER {q['KCOVER']:g} FLOOR {q['FLOOR']:g} RIMBREAK {q['RIMBREAK']:g})")
    jobs = [('mini', 'map308_mini_mock.png', sheet_mini), ('roads', 'map308_roads_abc.png', sheet_roads),
            ('notation', 'map308_notation_sheet.png', sheet_notation), ('full', 'map308_full_mock.png', sheet_full)]
    for key, name, fn in jobs:
        if only and key not in only: continue
        t = time.time(); img = fn(B, mp, print); sha = M.write_png(out / name, to_u8(img))
        print(f'  {name:28s} {img.shape[1]} x {img.shape[0]}  {sha[:16]}  {time.time() - t:.1f} s')
    if not only or 'legibility' in only:
        t = time.time(); res = measure(B, mp); sha = M.write_text(out / 'map308_legibility.json', M.dumps(res))
        r = res['road_core_vs_surround']
        print(f"  map308_legibility.json       road core >= 4.5:1 share {r['share_ge_4_5']} (floor .95), fog leak px {res['fog_214']['stroke_px'] + res['fog_214']['terrain_px']}, "
              f"{time.time() - t:.1f} s")
        for tag, cl in res['road_centre_line'].items():
            if tag == 'note': continue
            print(f"    centre line @{tag}: " + ', '.join(f"{k} {v['share_ge_4_5']:.3f} >= 4.5:1 / {v['share_ge_3']:.3f} >= 3:1 (median {v['median']}, n {v['samples']})" for k, v in cl.items()))
        for tag, cl in res['road_centre_line_all'].items():
            if not isinstance(cl, dict): continue
            print(f"    every road window @{tag} ({cl['windows']} windows): " + ', '.join(
                f"{k} {v['share_ge_4_5']:.3f} >= 4.5:1 (p05 {v['p05']}, n {v['samples']})" for k, v in cl.items() if isinstance(v, dict)))
        e = res['walked_edge']
        print(f"    walked edge: walked value on an unwalked cell {e['walked_value_on_an_unwalked_cell_px']} px of {e['px_checked']}, inset {e['edge_inset_m']} m, rim {e['rim_colour']} "
              f"{e['rim_vs_unwalked']}:1 against the wash, {e['rim_vs_paper']}:1 against the paper")
        ok_roads = all(v['ok'] for cl in res['road_centre_line_all'].values() if isinstance(cl, dict) for v in cl.values() if isinstance(v, dict))
        if not ok_roads or e['walked_value_on_an_unwalked_cell_px']:
            print('  FAILED: a road class is under 95 % on the centre line, or the walked edge reaches an unwalked cell'); sys.exit(1)
    print(f'[map308_twin] done in {time.time() - t0:.1f} s')


if __name__ == '__main__':
    main()
