# -*- coding: utf-8 -*-
"""SPEC-MAP-OVERHAUL-308 (DECISIONS D308-14 / 14b / 15) - concept MOCK SHEET for the user. Offline, read-only on the project.

    python Tools/resource_guard.py --wait
    python Tools/Art/map308_mock.py                 -> Art/UI308/Map/map308_mock_sheet.png (+ panels/, half-size JPG, report)
    python Tools/Art/map308_mock.py --only d,e      -> re-render single panels (the sheet is rebuilt from the panel files)

numpy + PIL only, deterministic (seeded hashes, no clock, no unseeded RNG). Everything is drawn from the REAL data the game
reads: the 4 m height field (Finish297 base and the #308 stage-1a field), the main scene's Map.asset copy (lines, markers,
cave zones), WorldLayout_Main.asset (physical road attributes, drainages, realms), wet.bytes, the four vegetation sheets
(trees only), crossings.json, boundary308.json and the Seal308 gate footprint. "Today" = the real captures plus a numpy twin
of the current drawing (WorldMapPaperInk straightened lines, MapStyle304 sizes, the #304 sprites).

ONE NOTATION (2026-10-04, alignment pass): every "new notation" picture of this sheet is drawn FROM THE BUNDLE the game
reads (Tools/Unity/Stage308_map/_ProjectAssets/Art/UI/UI308/Map, baked by cartography308.py) through map308_twin.render()
and glyph_rgba() - terrain wash, tree dabs, wave marks, rock strokes, shore line, every brush band (roads, saw-band ridges,
cliffs, walls, bridges, ticks), the walked edge (the #308 crisp edge and its ink rim: MapFog308_Edge) and every icon (the
brush mark of the current position with its second ink). The private drawing code this file used to carry for those parts is
retired; the rules it introduced now live in map308_ridges.py / map308_art.py. The recommended frames - the minimap's lacquer
frame with its north piece and the lacquer board of the map page - are laid from the THEME KIT atlas by the kit's rules
(48 + 12 n; (80 + 9 n) x (82 + 9 m), board 836 x 910), tiled from the bottom-left as Unity does (map308_twin.Kit).
Still drawn here, because the bundle does not carry them: the rest of the page furniture (legend plates, key caps, nacre
lines, the rejected frame alternatives: D308-15 theme library theme308_lib.py), the place-name plates and realm names, the cave plan re-tint,
"today" (the real captures and a twin of the current drawing), the two REJECTED road notations A and C of panel (d), and
the stage-1a gate wall of panel (h) (a line the bundle leaves out: it is drawn with the bundle's own wall stroke).
Stage 1a pictures read the 1a bundle of Tools/Unity/Stage308_map/Bundles/1a (map308_bake.py --stage 1a; baked here when it is
missing). Writes only under Art/UI308/Map/ (and that bundle folder when it had to bake it).
"""
import glob
import hashlib
import json
import math
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
import cliff308_base as B      # noqa: E402  shared read-only loaders (height, wet, sheets, lattice helpers)
import theme308_lib as TH      # noqa: E402  D308-15 theme kit library (materials, nacre, lattice, inlay)
import map308_lib as ML        # noqa: E402  the bakers' library (bundle folder, geometry)
import map308_twin as TW       # noqa: E402  the bundle composite: the ONE drawing of the notation

ROOT = B.ROOT
OUT = ROOT / 'Art/UI308/Map'
PANELS = OUT / 'panels'
WORK = OUT / '_work'
UI304 = B.A / 'Art/UI/UI304'
FONTS = UI304 / 'Fonts'
TEX = UI304 / 'Textures'
LAYOUT = B.A / 'Scenes/World/Main/WorldLayout_Main.asset'
STAGE_1A = B.PL / 'Stage/height_p1a.bytes'
BOUNDARY = B.PL / 'boundary308.json'
FOOTPRINT = B.ENC / 'Out/Seal308/footprint308.json'
CROSSINGS = B.RB / 'Watershed295/Generated/crossings.json'
CAVE_PLAN = B.A / 'Art/World/Finish297/Map/CavePlan297.png'
CAP_FULL = ROOT / 'Art/UI304/after3/map_revealed.png'                       # today's map page, 1080p, all revealed
CAP_MINI = ROOT / 'Art/Playtest306/Checks/b23_mini_c_outside_2560x1440.png'  # today's minimap, 1440p
HUD_FRAME = ROOT / 'Art/UI308/HUD/mock_frames/a_fullframe_1920.png'          # SPEC-HUD-LIQUID-308 vessels on a real frame
SEED = 308


def map_asset():
    return Path(glob.glob(str(B.A / 'Art/World/Architecture296/Data/0618cb85*_Map.asset'))[0])


# ---------------------------------------------------------------------------------------------------------------- values
def hx(s):
    s = s.lstrip('#')
    return np.array([int(s[i:i + 2], 16) for i in (0, 2, 4)], float) / 255.0


PAPER = hx('#DFDBD0')          # Sheet: walked flat ground, road knock-out, icon rim
WASH = hx('#8F8B81')           # unwalked ground (Spec question 2, recommended)
INK = hx('#141413')
CIN = hx('#B8392B')
VEIL = hx('#0F0F0E')
ASH = hx('#5A574F')
MIST = hx('#A7A398')
PAPER_HI = hx('#E6E2D7')
LACQUER = TH.T['Lacquer']
WOOD = TH.T['Wood']
PORCELAIN = TH.T['Porcelain']
INLAY_DARK = TH.T['InlayDark']
WASH_MAX = 0.18                # darkest slope wash on walked ground
ELEV_WASH = 0.14               # whole-world band: elevation wash instead of slope wash
TODAY_PAPER = np.array([213, 209, 198]) / 255.0   # measured on the captures (Spec L1)
TODAY_WASH = np.array([162, 158, 146]) / 255.0
TODAY_LINE = np.array([91, 90, 84]) / 255.0

# zoom bands (metres per reference px): Z0 <= .6 < Z1 <= 1.8 < Z2 <= 5 < Z3
MPP_MINI, MPP_CAVE, MPP_FULL, MPP_WORLD = 240.0 / 210.0, 56.0 / 210.0, 1970.0 / 740.0, 6000.0 / 760.0


def band(mpp):
    return 0 if mpp <= .6 else 1 if mpp <= 1.8 else 2 if mpp <= 5.0 else 3


# ---------------------------------------------------------------------------------------------------------------- bundle
ROAD_CLASS = dict(soro=7, gil=8, daero=9)
_bundles = {}


def bundle(stage='base'):
    """the baked bundle the game reads: 'base' = the stage folder, another stage = Bundles/<stage> beside it (both are kept)."""
    if stage not in _bundles:
        d = ML.bundle_dir(stage)
        if not (d / 'map308_notation.json').exists():
            import subprocess
            subprocess.run([sys.executable, str(TOOLS / 'cartography308.py'), '--stage', stage, '--out', str(d)], check=True)
        _bundles[stage] = TW.Bundle(d)
    return _bundles[stage]


def road_px(cls, band_):
    """stroke width (reference px) and paper band of a road class in a zoom band, from the bundle's notation."""
    c = bundle().classes[ROAD_CLASS[cls]]
    return c['widthPx'][band_], c['knockoutPx']


# ---------------------------------------------------------------------------------------------------------------- maths
def lum(c):
    c = np.asarray(c, float)
    l = np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)
    return .2126 * l[..., 0] + .7152 * l[..., 1] + .0722 * l[..., 2]


def contrast(a, b):
    la, lb = float(lum(a)), float(lum(b))
    return (max(la, lb) + .05) / (min(la, lb) + .05)


def box(a, r):
    """separable box mean of radius r cells (edge padded)."""
    if r <= 0:
        return a
    k = 2 * r + 1
    p = np.pad(a, ((0, 0), (r, r)), mode='edge')
    c = np.cumsum(np.pad(p, ((0, 0), (1, 0))), axis=1)
    a = (c[:, k:] - c[:, :-k]) / k
    p = np.pad(a, ((r, r), (0, 0)), mode='edge')
    c = np.cumsum(np.pad(p, ((1, 0), (0, 0))), axis=0)
    return (c[k:, :] - c[:-k, :]) / k


def gauss(a, r):
    for _ in range(3):
        a = box(a, r)
    return a


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0 + 1e-12), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def hash2(ix, iy, seed=0):
    """deterministic integer lattice hash -> float 0..1 (uint32 arithmetic, the same on every platform)."""
    a = (np.asarray(ix).astype(np.int64) * 374761393 + np.asarray(iy).astype(np.int64) * 668265263 + (SEED + seed) * 2147483647) & 0xFFFFFFFF
    a = (a ^ (a >> 13)) * 1274126177 & 0xFFFFFFFF
    a = a ^ (a >> 16)
    return (a & 0xFFFFFF) / float(0x1000000)


def vnoise(u, v, seed=0):
    """value noise, period 1 lattice, smooth interpolation; u, v arrays."""
    u = np.asarray(u, float)
    v = np.asarray(v, float)
    iu = np.floor(u)
    iv = np.floor(v)
    fu = u - iu
    fv = v - iv
    fu = fu * fu * (3 - 2 * fu)
    fv = fv * fv * (3 - 2 * fv)
    a = hash2(iu, iv, seed)
    b = hash2(iu + 1, iv, seed)
    c = hash2(iu, iv + 1, seed)
    d = hash2(iu + 1, iv + 1, seed)
    return (a * (1 - fu) + b * fu) * (1 - fv) + (c * (1 - fu) + d * fu) * fv


def fbm(u, v, seed=0, octaves=3):
    s = 0.0
    amp = 0.5
    tot = 0.0
    for o in range(octaves):
        s = s + amp * vnoise(u * 2 ** o, v * 2 ** o, seed + 17 * o)
        tot += amp
        amp *= .5
    return s / tot


def simplify(P, tol):
    """Douglas-Peucker (iterative). P (n, 2)."""
    P = np.asarray(P, float)
    n = len(P)
    if n < 3 or tol <= 0:
        return P
    keep = np.zeros(n, bool)
    keep[0] = keep[-1] = True
    stack = [(0, n - 1)]
    while stack:
        a, b = stack.pop()
        if b <= a + 1:
            continue
        ax, ay = P[a]
        bx, by = P[b]
        dx, dy = bx - ax, by - ay
        L = math.hypot(dx, dy)
        q = P[a + 1:b]
        if L < 1e-9:
            d = np.hypot(q[:, 0] - ax, q[:, 1] - ay)
        else:
            t = np.clip(((q[:, 0] - ax) * dx + (q[:, 1] - ay) * dy) / (L * L), 0, 1)
            d = np.hypot(q[:, 0] - (ax + t * dx), q[:, 1] - (ay + t * dy))
        k = int(np.argmax(d))
        if d[k] > tol:
            m = a + 1 + k
            keep[m] = True
            stack.append((a, m))
            stack.append((m, b))
    return P[keep]


def dedupe(P):
    P = np.asarray(P, float)
    if len(P) < 2:
        return P
    k = np.concatenate([[True], np.hypot(*np.diff(P, axis=0).T) > 1e-6])
    return P[k]


def chaikin(P, n=1):
    P = np.asarray(P, float)
    for _ in range(n):
        if len(P) < 3:
            break
        Q = np.empty((2 * len(P) - 2, 2))
        Q[0::2] = .75 * P[:-1] + .25 * P[1:]
        Q[1::2] = .25 * P[:-1] + .75 * P[1:]
        P = np.vstack([P[:1], Q, P[-1:]])
    return P


def plen(P):
    P = np.asarray(P, float)
    return float(np.hypot(*np.diff(P, axis=0).T).sum()) if len(P) > 1 else 0.0


# ---------------------------------------------------------------------------------------------------------------- data
def _pts(block):
    return np.array([(float(a), float(b)) for a, b in re.findall(r'\{x: ([-\d.e]+), y: ([-\d.e]+)\}', block)], float).reshape(-1, 2)


def yaml_str(s):
    """a Unity YAML scalar: plain, or double-quoted with hex (x HH) / unicode (u XXXX) escapes."""
    s = s.strip()
    if not s.startswith('"'):
        return s
    s = s[1:-1]
    s = re.sub(r'\\x([0-9A-Fa-f]{2})', lambda m: chr(int(m.group(1), 16)), s)
    s = re.sub(r'\\u([0-9A-Fa-f]{4})', lambda m: chr(int(m.group(1), 16)), s)
    return s.replace('\\"', '"')


def load_map_asset():
    """lines (id, kind, points), markers (id, label, kind, xz), cave zones of the main scene's Map.asset copy."""
    t = map_asset().read_text(encoding='utf-8')
    i, j, k = t.find('\n  Lines:'), t.find('\n  Markers:'), t.find('\n  Zones:')
    lines = []
    for blk in t[i:j].split('\n  - Id: ')[1:]:
        lines.append(dict(id=blk.split('\n')[0].strip(), kind=int(re.search(r'Kind: (\d+)', blk).group(1)), pts=dedupe(_pts(blk))))
    markers = []
    for blk in t[j:k].split('\n  - Id: ')[1:]:
        lab = yaml_str(re.search(r'Label: (.*)', blk).group(1))
        m = re.search(r'WorldXZ: \{x: ([-\d.e]+), y: ([-\d.e]+)\}', blk)
        markers.append(dict(id=blk.split('\n')[0].strip(), label=lab, kind=int(re.search(r'Kind: (\d+)', blk).group(1)),
                            xz=(float(m.group(1)), float(m.group(2)))))
    zones = []
    for blk in t[k:].split('\n  - ExploreWalkedPassages')[1:]:
        zid = re.search(r'\n    Id: (\S+)', blk).group(1)
        uv = re.search(r'IllustrationWorldUv:\s+serializedVersion: 2\s+x: ([-\d.e]+)\s+y: ([-\d.e]+)\s+width: ([-\d.e]+)\s+height: ([-\d.e]+)', blk)
        poly = _pts(blk[blk.find('Polygon:'):blk.find('DetailPath:')])
        zones.append(dict(id=zid, uv=tuple(float(uv.group(q)) for q in range(1, 5)), polygon=poly,
                          path=_pts(blk[blk.find('DetailPath:'):blk.find('DetailLines:')])))
    return lines, markers, zones


def load_layout():
    """physical road attributes per route id (never the story role), drainages, realms."""
    t = LAYOUT.read_text(encoding='utf-8')
    i, j = t.find('\n  Routes:'), t.find('\n  Ridges:')
    cls = {}
    for m in re.finditer(r'- Id: (.*)\n\s+From: .*\n\s+To: .*\n\s+Role: \d+\n\s+RequiredAbility: ?.*\n\s+OneWay: \d\n\s+GradeForVehicle: (\d)\n\s+Traversal: (\d)\n\s+Bends:(?:\n\s+- \{x: [-\d.e]+, y: [-\d.e]+\})*\n\s+Width: ([\d.]+)', t[i:j]):
        veh, foot, w = int(m.group(2)), int(m.group(3)), float(m.group(4))
        cls[m.group(1)] = 'daero' if (veh or w >= 6) else ('gil' if (w >= 3 and not foot) else 'soro')
    d0, d1 = t.find('\n  Drainages:'), t.find('\n  Hydrology:')
    drains = []
    for blk in t[d0:d1].split('\n  - Id: ')[1:]:
        drains.append(dict(id=blk.split('\n')[0].strip(), half=float(re.search(r'HalfWidth: ([\d.]+)', blk).group(1)), pts=_pts(blk)))
    r0, r1 = t.find('\n  Realms:'), t.find('\n  RiverWidth:')
    realms = []
    for blk in t[r0:r1].split('\n  - Id: ')[1:]:
        lab = yaml_str(re.search(r'Label: (.*)', blk).group(1))
        c = re.search(r'Centre: \{x: ([-\d.e]+), y: ([-\d.e]+)\}', blk)
        realms.append(dict(id=blk.split('\n')[0].strip(), label=lab, centre=(float(c.group(1)), float(c.group(2))),
                           polygon=_pts(blk[blk.find('Polygon:'):blk.find('Tint:')])))
    return cls, drains, realms


class World:
    """the notation's source fields on the 4 m lattice for one stage ('base' | '1a') + every line the map draws."""
    _cache = {}

    @classmethod
    def get(cls, stage='base'):
        if stage not in cls._cache:
            cls._cache[stage] = cls(stage)
        return cls._cache[stage]

    def __init__(self, stage):
        self.stage = stage                                                  # terrain, ridges, forest, water: the bundle of this stage
        lines, self.markers, self.zones = load_map_asset()
        cls_by_id, self.drains, self.realms = load_layout()
        self.roads = []
        self.sites = []
        for ln in lines:
            if ln['kind'] == 4:
                self.sites.append(ln['pts'])
            elif ln['kind'] == 2 and plen(ln['pts']) > 3:
                self.roads.append(dict(id=ln['id'], cls=cls_by_id.get(ln['id'], 'soro'), pts=ln['pts']))
        cr = json.loads(CROSSINGS.read_text(encoding='utf-8-sig'))
        self.bridges = [((c['Start']['x'], c['Start']['z']), (c['End']['x'], c['End']['z'])) for c in cr['Crossings' if 'Crossings' in cr else list(cr.keys())[0]]]
        self.cliffs, self.outer, self.walls, self.top = [], [], [], np.zeros((B.HH, B.W), bool)
        self.gate = None
        if stage == '1a':
            bd = json.loads(BOUNDARY.read_text(encoding='utf-8'))
            for s in bd['segments']:
                if s.get('stage') != '1a' or not s.get('polyline'):
                    continue
                P = np.asarray(s['polyline'], float)
                if s.get('class') == 'outer':
                    self.outer.append(dict(id=s['id'], pts=chaikin(P, 2)))
                else:
                    self.cliffs.append(dict(id=s['id'], pts=P, cls=s.get('class')))
            gw = bd['gate_wall']
            self.walls = [np.asarray(gw['west']['line'], float), np.asarray(gw['east']['line'], float)]
            self.gate = ((gw['gate']['A'][0] + gw['gate']['B'][0]) / 2, (gw['gate']['A'][1] + gw['gate']['B'][1]) / 2)
            self.top = np.load(B.WORK / 'reach_p1a_top_UP1.npy')              # cliff-top field (reveal region 1)
        self.ea = np.load(B.WORK / 'reach_p1a_S0_after_wall.npy') if (B.WORK / 'reach_p1a_S0_after_wall.npy').exists() else None

    def road_class_km(self):
        out = {}
        for r in self.roads:
            out[r['cls']] = out.get(r['cls'], 0.0) + plen(r['pts']) / 1000
        return {k: round(v, 2) for k, v in out.items()}


# ---------------------------------------------------------------------------------------------------------------- view
class View:
    """a north-up window: centre (cx, cz) metres, mpp metres per REFERENCE px, w x h reference px, rendered at ss x."""

    def __init__(self, cx, cz, mpp, w, h, ss=2):
        self.cx, self.cz, self.mpp, self.w, self.h, self.ss = float(cx), float(cz), float(mpp), int(w), int(h), int(ss)
        self.W, self.H = self.w * ss, self.h * ss
        self.band = band(mpp)

    def to_px(self, P):
        """world (x, z) -> supersampled px (x, y)."""
        P = np.asarray(P, float).reshape(-1, 2)
        return np.stack([(P[:, 0] - self.cx) / self.mpp * self.ss + self.W / 2, (self.cz - P[:, 1]) / self.mpp * self.ss + self.H / 2], 1)

    def grid(self):
        """world x, z of every supersampled pixel centre."""
        xs = self.cx + ((np.arange(self.W) + .5) - self.W / 2) / self.ss * self.mpp
        zs = self.cz - ((np.arange(self.H) + .5) - self.H / 2) / self.ss * self.mpp
        return np.meshgrid(xs, zs)

    def rect(self):
        return (self.cx - self.w * self.mpp / 2, self.cz - self.h * self.mpp / 2, self.cx + self.w * self.mpp / 2, self.cz + self.h * self.mpp / 2)

    def near(self, P, margin_px=40):
        x0, z0, x1, z1 = self.rect()
        m = margin_px * self.mpp
        P = np.asarray(P, float)
        return P[:, 0].max() >= x0 - m and P[:, 0].min() <= x1 + m and P[:, 1].max() >= z0 - m and P[:, 1].min() <= z1 + m


def down(a, ss):
    """box downsample (H, W[, C]) by ss."""
    if ss == 1:
        return a
    H, W = a.shape[0] // ss, a.shape[1] // ss
    return a[:H * ss, :W * ss].reshape((H, ss, W, ss) + a.shape[2:]).mean(axis=(1, 3))


def to_img(a):
    return Image.fromarray(np.clip(np.round(np.asarray(a) * 255), 0, 255).astype(np.uint8))


# ---------------------------------------------------------------------------------------------------------------- strokes
def stroke_field(polys, H, W, reach):
    """nearest-centreline field over an (H, W) px grid for a set of polylines (px coords): D distance, S arclength along the
    polyline, Q signed offset (+ = left of travel on screen), K polyline index (-1 = none within reach)."""
    D = np.full((H, W), 1e9, np.float32)
    S = np.zeros((H, W), np.float32)
    Q = np.zeros((H, W), np.float32)
    K = np.full((H, W), -1, np.int32)
    for k, P in enumerate(polys):
        P = np.asarray(P, float)
        if len(P) < 2:
            continue
        seg = np.diff(P, axis=0)
        L = np.hypot(seg[:, 0], seg[:, 1])
        cum = np.concatenate([[0], np.cumsum(L)])
        for a in range(len(P) - 1):
            if L[a] < 1e-6:
                continue
            x0, y0 = P[a]
            x1, y1 = P[a + 1]
            xa, xb = max(0, int(math.floor(min(x0, x1) - reach))), min(W, int(math.ceil(max(x0, x1) + reach)) + 1)
            ya, yb = max(0, int(math.floor(min(y0, y1) - reach))), min(H, int(math.ceil(max(y0, y1) + reach)) + 1)
            if xb <= xa or yb <= ya:
                continue
            dx = (np.arange(xa, xb) + .5)[None, :] - x0
            dy = (np.arange(ya, yb) + .5)[:, None] - y0
            tx, ty = seg[a] / L[a]
            s = dx * tx + dy * ty
            sc = np.clip(s, 0, L[a])
            d = np.hypot(dx - tx * sc, dy - ty * sc)
            sub = D[ya:yb, xa:xb]
            better = d < sub
            if not better.any():
                continue
            q = (dx * ty - dy * tx)                                            # + = left of travel (screen y points down)
            sub[better] = d[better]
            S[ya:yb, xa:xb][better] = (cum[a] + sc)[better]
            Q[ya:yb, xa:xb][better] = np.broadcast_to(q, d.shape)[better]
            K[ya:yb, xa:xb][better] = k
    return D, S, Q, K


def aa(d, half, ss):
    """coverage of a band |d| <= half with a 1 supersample-px soft edge."""
    return np.clip((half - d) / 1.0 + .5, 0, 1)


class Canvas:
    """float RGB canvas at the view's supersampled size + helpers that composite ink / paper by coverage."""

    def __init__(self, view, base=PAPER):
        self.v = view
        self.rgb = np.empty((view.H, view.W, 3))
        self.rgb[:] = base

    def ink(self, cov, a=1.0, col=INK):
        c = np.clip(cov * a, 0, 1)[..., None]
        self.rgb = self.rgb * (1 - c) + col * c

    def out(self):
        return down(self.rgb, self.v.ss)


def polys_px(view, lines, tol_px=0.3, min_tol_m=1.0):
    out = []
    for P in lines:
        if not view.near(P):
            continue
        Q = simplify(P, max(min_tol_m, tol_px * view.mpp))
        out.append(view.to_px(Q))
    return out


# ---------------------------------------------------------------------------------------------------------------- fog
class Fog:
    """the game's discovery grid: 32 m cells, a cell is walked when its CENTRE is within 96 m of a visited position."""
    CELL, R = 32.0, 96.0

    def __init__(self):
        self.g = np.zeros((188, 125), bool)                   # row-major, south to north

    def all(self):
        self.g[:] = True
        return self

    def walk(self, P, step=8.0, region=None):
        """P polyline of visited positions. region: optional (mask4m, id) - cells of that reveal region are opened only when
        the walker stands inside it (Spec question 5, recommended); other cells are skipped while standing inside."""
        D, _ = B.resample(np.asarray(P, float), step)
        cx = (np.arange(125) + .5) * 32
        cz = (np.arange(188) + .5) * 32
        CX, CZ = np.meshgrid(cx, cz)
        cell_top = None
        if region is not None:
            rf = region.astype(float)
            cell_top = B.sample(rf, CX, CZ) > .5
        for x, z in D:
            m = (CX - x) ** 2 + (CZ - z) ** 2 <= self.R ** 2
            if cell_top is not None:
                on_top = bool(B.sample(rf, x, z) > .5)
                m &= cell_top if on_top else ~cell_top
            self.g |= m
        return self

    def reveal(self, X, Z, mpp):
        """0..1 per pixel: the #308 crisp walked edge of the stage shaders (MapFog308_Edge, 1 px anti-aliased, always inside the
        walked cells)."""
        w = TW.fog_crisp(self.g, X, Z, bundle().note['values']['fogEdge'].get('crispEdge'))[0]
        return w, w


# ---------------------------------------------------------------------------------------------------------------- render
def render_new(v, world, fog=None, layers=('terrain', 'forest', 'ridges', 'water', 'cliffs', 'walls', 'roads', 'bridges', 'ticks'),
               road_only=None, road_mode='B'):
    """the notation for one window, composed FROM THE BUNDLE by map308_twin.render (the same function the twin sheets use):
    terrain, patterns, shore, brush bands, the crisp walked edge and its ink rim. Rendered at the view's supersampling and
    box-filtered down by Canvas.out(). road_mode A / C = the two rejected road notations: the bundle without its roads, the
    private ruled / stamped roads on top (they are not part of the notation). Returns (canvas, walked value)."""
    b = bundle(world.stage)
    V = TW.View(v.cx, v.cz, v.W, v.H, v.mpp / v.ss, float(v.ss))
    walked = fog.g if fog is not None else np.ones((188, 125), bool)
    show = set(layers) if road_mode == 'B' else set(layers) - {'roads', 'ticks'}
    extra = [(5, 0, P, False) for P in world.walls] if 'walls' in layers else []      # the stage-1a gate wall: not in the bundle
    L = {}
    cv = Canvas(v)
    cv.rgb = TW.render(b, V, walked, 'B', L, show=tuple(show), extra=extra)
    if road_mode != 'B' and 'roads' in layers:
        (draw_roads_ruled if road_mode == 'A' else draw_roads_stamped)(cv, world, *((v.band,) if road_mode == 'A' else ()))
    return cv, L['walked']


def draw_roads_ruled(cv, world, bnd, width=None, ticks=True, col=INK, alpha=225 / 255.0, tol=12.0):
    """option A = today's drawing: every road straightened to a 12 m tolerance, one ruled ink line, a tick every 200 m."""
    v = cv.v
    ss = v.ss
    w = width if width is not None else (2.6 if bnd <= 1 else 2.2)
    im = Image.new('L', (v.W, v.H), 0)
    d = ImageDraw.Draw(im)
    for r in world.roads:
        if not v.near(r['pts']):
            continue
        P = simplify(r['pts'], tol)
        Q = v.to_px(P)
        d.line([tuple(q) for q in Q], fill=255, width=max(1, int(round(w * ss))), joint='curve')
        if ticks and 200.0 / v.mpp >= 16:
            seg = np.hypot(*np.diff(P, axis=0).T)
            cum = np.concatenate([[0], np.cumsum(seg)])
            for s in np.arange(200.0, cum[-1], 200.0):
                k = min(max(int(np.searchsorted(cum, s)) - 1, 0), len(P) - 2)
                t = (P[k + 1] - P[k]) / (seg[k] + 1e-9)
                p = P[k] + t * (s - cum[k])
                n = np.array([-t[1], t[0]])
                a, b = v.to_px([p - n * 3.5 * v.mpp, p + n * 3.5 * v.mpp])
                d.line([tuple(a), tuple(b)], fill=255, width=max(1, int(round(1.3 * ss))))
    cv.ink(np.asarray(im, float) / 255, alpha, col)


def draw_roads_stamped(cv, world):
    """option C: a stamped (woodblock) band - two rails with a dot row between, repeated along the road."""
    v = cv.v
    ss = v.ss
    for cls, w in (('soro', 3.0), ('gil', 4.6), ('daero', 6.4)):
        polys = polys_px(v, [r['pts'] for r in world.roads if r['cls'] == cls])
        if not polys:
            continue
        w = w * (1.0 if v.band <= 1 else .8) * ss
        D, S, Q, K = stroke_field(polys, v.H, v.W, w)
        m = K >= 0
        ko = aa(D, w / 2 + 1.0 * ss, ss) * m
        cv.rgb = cv.rgb * (1 - ko[..., None]) + PAPER * ko[..., None]
        rail = aa(np.abs(np.abs(Q) - (w / 2 - .5 * ss)), .5 * ss, ss) * (D < w / 2 + ss)
        pitch = (5.0 if cls != 'soro' else 4.0) * ss
        dot = np.clip(1.2 - np.hypot(((S / pitch) % 1.0 - .5) * pitch, Q) / (.75 * ss), 0, 1) if cls != 'soro' else 0
        if cls == 'soro':
            rail = rail * ((S / pitch) % 1.0 < .6)
        cv.ink(np.maximum(rail, dot) * m, .88)


# ---------------------------------------------------------------------------------------------------------------- glyphs
_fonts = {}


def font(face, px):
    name = dict(S9='NotoSerifKR-900.ttf', S8='NotoSerifKR-800.ttf', S6='NotoSerifKR-600.ttf', N4='NotoSansKR-Regular.ttf', N7='NotoSansKR-Bold.ttf')[face]
    if (face, px) not in _fonts:
        _fonts[(face, px)] = ImageFont.truetype(str(FONTS / name), px)
    return _fonts[(face, px)]


TODAY_PICT = dict(mine='cave', geumpyo_inn='rest', village='village', capital_delivery='village', south_gate='gate',
                  mountain_cheongrim_summit='mountain', root_cave='cave')          # everything else: cave / mountain by a word in the name


def marker_glyph(marker_id):
    """marker id -> glyph id, the bundle's own table (map308_notation.json markerGlyphs)."""
    return bundle().note['markerGlyphs'].get(marker_id, 'place')


def label_rank(gid):
    pr = bundle().note['labelPriority']
    return pr.index(gid) if gid in pr else len(pr)


def icon(gid, px, ink_col=INK, rim_col=PAPER, rim_a=1.0, ink_a=.94):
    """a glyph as the UI draws it: the bundle's S atlas up to 28 px, the L atlas above (MapNotation308SO.SmallAtlasMaxPx),
    scaled to `px`. RGBA float."""
    _, a, ink = TW.glyph_rgba(bundle(), gid, px)
    rgb = rim_col[None, None, :] * (1 - (ink * ink_a)[..., None]) + ink_col[None, None, :] * (ink * ink_a)[..., None]
    return np.dstack([rgb, np.maximum(ink * ink_a, a * rim_a)])


def player_mark(px, heading_deg=0.0, rim_col=PAPER, variant=None):
    """current position: the bundle's BRUSH mark (glyph 'player': cinnabar tuft + the second ink's ferrule, handle and edge),
    turned to the heading. On paper it stands on its hanji plate; on a dark plate (the legend roundel: rim_col = lacquer) there
    is no plate and the second ink is drawn in the paper tone, as MapGlyph308Graphic.SetSecondInk(1, light) does."""
    dark = float(lum(rim_col)) < .2
    col, a, fig = TW.glyph_rgba(bundle(), 'player', px, tint=CIN, rotate=-heading_deg, ink2=PAPER_HI if dark else None)
    if not dark:
        return np.dstack([col, a])
    rgb = (col - PAPER[None, None, :] * (1 - fig[..., None])) / np.maximum(fig[..., None], 1e-3)        # the figure alone, un-premultiplied
    return np.dstack([np.clip(rgb, 0, 1), fig])


def over(dst, src, x, y):
    """straight-alpha 'over' of src RGBA (float) onto dst RGB (float) at integer top-left (x, y); clipped."""
    h, w = src.shape[:2]
    x, y = int(round(x)), int(round(y))
    x0, y0, x1, y1 = max(0, x), max(0, y), min(dst.shape[1], x + w), min(dst.shape[0], y + h)
    if x1 <= x0 or y1 <= y0:
        return
    s = src[y0 - y:y1 - y, x0 - x:x1 - x]
    a = s[..., 3:4]
    dst[y0:y1, x0:x1] = dst[y0:y1, x0:x1] * (1 - a) + s[..., :3] * a


def sprite304(rel, px, col, a=1.0, rot=0.0):
    """a #304 sprite (white glyph + alpha) tinted, scaled to px. RGBA float."""
    im = Image.open(TEX / rel).convert('RGBA')
    if rot:
        im = im.rotate(-rot, Image.BICUBIC)
    im = im.resize((px, px), Image.LANCZOS)
    arr = np.asarray(im, float) / 255
    return np.dstack([np.broadcast_to(col, arr.shape[:2] + (3,)), arr[..., 3] * a])


def text_rgba(s, f, col, pad=3):
    w, h = int(f.getlength(s)) + 2 * pad + 2, int(f.size * 1.5) + 2 * pad
    im = Image.new('L', (w, h), 0)
    ImageDraw.Draw(im).text((pad, pad), s, font=f, fill=255)
    a = np.asarray(im, float) / 255
    return np.dstack([np.broadcast_to(col, a.shape + (3,)), a])


def label_plate(s, px=21):
    """place name on a hanji plate: Sheet alpha .92 + one ink line. RGBA float."""
    f = font('S8', px)
    tw = int(f.getlength(s))
    w, h = tw + 16, int(px * 1.45) + 6
    out = np.zeros((h, w, 4))
    out[..., :3] = PAPER
    out[..., 3] = .92
    edge = INK * .75 + PAPER * .25
    for sl in ((0, slice(None)), (-1, slice(None)), (slice(None), 0), (slice(None), -1)):
        out[sl + (slice(0, 3),)] = edge
        out[sl + (3,)] = .95
    over(out[..., :3], text_rgba(s, f, INK, 0), 8, 2)
    return out


def place_markers(img, v, world, found, size, labels=False, wake=None, style='new'):
    """discovered markers (and the player's own records) on a REFERENCE-px image. Nothing is drawn for an undiscovered id.
    Returns the list of (id, x, y) drawn."""
    drawn = []
    boxes = []
    items = [m for m in world.markers if m['id'] in found]
    items.sort(key=lambda m: label_rank(marker_glyph(m['id'])))
    todo = []
    for m in items:
        px, py = v.to_px([m['xz']])[0] / v.ss
        if not (6 <= px < v.w - 6 and 6 <= py < v.h - 6):
            continue
        gid = marker_glyph(m['id'])
        sz = size + 4 if gid == 'inn' and size >= 30 else size
        if style == 'new':
            todo.append((m, gid, sz, px, py))
        else:
            pict = TODAY_PICT.get(m['id'], 'mountain' if any(k in m['label'] for k in ('산', '능선', '암릉')) else 'cave')
            if size <= 22:
                hud = {'rest': 'rest', 'cave': 'cave', 'mountain': 'mountain'}.get(pict, 'place')
                over(img, sprite304(f'hud/picto_{hud}_rim.png', sz, TODAY_PAPER), px - sz / 2, py - sz / 2)
                over(img, sprite304(f'hud/picto_{hud}.png', sz, INK, .9), px - sz / 2, py - sz / 2)
            else:
                over(img, sprite304(f'map/pict_{pict}.png', sz, INK, .9), px - sz / 2, py - sz / 2)
            if labels:
                over(img, text_rgba(m['label'].split('·')[-1].strip(), font('S8', 21), INK, 0), px + sz / 2 + 2, py - 14)
        drawn.append((m['id'], px, py))
        boxes.append((px - sz / 2, py - sz / 2, px + sz / 2, py + sz / 2))
    if wake is not None:
        px, py = v.to_px([wake])[0] / v.ss
        if 0 <= px < v.w and 0 <= py < v.h:
            sz = size + 4 if size >= 30 else size
            r = int(sz * 1.5)
            if style == 'new':
                over(img, icon('wake_ring', r, INK, PAPER, rim_a=.9), px - r / 2, py - r / 2)
                todo.append((dict(id='wake', label='깨어날 곳'), 'inn', sz, px, py))
            else:
                over(img, sprite304('map/ring_variant.png', r, INK, .9), px - r / 2, py - r / 2)
                over(img, sprite304('map/pict_rest.png', size, INK, .9), px - size / 2, py - size / 2)
            boxes.append((px - r / 2, py - r / 2, px + r / 2, py + r / 2))
            drawn.append(('wake', px, py))
    if labels and style == 'new':                      # plates first look at the bare map: never on a road stroke
        base = img.copy()
        for m, gid, sz, px, py in todo:
            lp = label_plate(m['label'].split('·')[-1].strip())
            h_, w_ = lp.shape[:2]
            off = sz * (.8 if m['id'] == 'wake' else .5)
            for ox, oy in ((off + 4, -h_ / 2), (-off - 4 - w_, -h_ / 2), (-w_ / 2, off + 3), (-w_ / 2, -off - 3 - h_)):
                bx = (px + ox, py + oy, px + ox + w_, py + oy + h_)
                if bx[0] < 4 or bx[1] < 4 or bx[2] > v.w - 4 or bx[3] > v.h - 4:
                    continue
                if any(not (bx[2] < b[0] or bx[0] > b[2] or bx[3] < b[1] or bx[1] > b[3]) for b in boxes):
                    continue
                sub = base[int(bx[1]):int(bx[3]), int(bx[0]):int(bx[2])]
                if sub.size and (lum(sub) < .10).mean() > .03:
                    continue
                over(img, lp, bx[0], bx[1])
                boxes.append(bx)
                break
    for m, gid, sz, px, py in todo:
        over(img, icon(gid, sz), px - sz / 2, py - sz / 2)
    return drawn


# ---------------------------------------------------------------------------------------------------------------- frames (D308-15)
def over_rgba(dst, src, x, y):
    """straight-alpha RGBA over RGBA."""
    h, w = src.shape[:2]
    x, y = int(round(x)), int(round(y))
    x0, y0, x1, y1 = max(0, x), max(0, y), min(dst.shape[1], x + w), min(dst.shape[0], y + h)
    if x1 <= x0 or y1 <= y0:
        return
    s = src[y0 - y:y1 - y, x0 - x:x1 - x]
    d = dst[y0:y1, x0:x1]
    sa, da = s[..., 3:4], d[..., 3:4]
    oa = sa + da * (1 - sa)
    d[..., :3] = np.where(oa > 1e-6, (s[..., :3] * sa + d[..., :3] * da * (1 - sa)) / np.maximum(oa, 1e-6), 0)
    d[..., 3:4] = oa


def fill(a, x0, y0, x1, y1, col, alpha=1.0):
    a[int(y0):int(y1), int(x0):int(x1), :3] = col
    a[int(y0):int(y1), int(x0):int(x1), 3] = alpha


def lattice_fill(name, w, h, u=None, bar=None):
    """a lattice family tiled over w x h px at 1 texel per px -> bar coverage 0..1 (theme308_lib construction rules)."""
    t = TH.lattice(name, scale=1, edge=name in TH.LATTICE_EDGE, u=u, bar=bar)
    return np.tile(t, (h // t.shape[0] + 2, w // t.shape[1] + 2))[:h, :w]


FRAME_PAD = 12                   # transparent margin round a minimap frame (the kit's north piece stands 11 px above the frame)


def frame_mini(kind):
    """minimap frame as RGBA + the map window rect inside it. All four share the 312 x 228 footprint (round: 232 disc)."""
    P = FRAME_PAD
    if kind == 'round':          # the theme kit's own v0 drawing: lacquer ring, nacre kkeuneumjil ring, north petal
        N = 232 + 2 * P
        c = N / 2
        yy, xx = np.mgrid[0:N * 4, 0:N * 4]
        r = np.hypot((xx + .5) / 4 - c, (yy + .5) / 4 - c)
        ring = down(((r >= 96) & (r <= 114.6)).astype(float), 4)
        hair = down((((r > 114.6) & (r <= 116.0)) | ((r >= 94.6) & (r < 96))).astype(float), 4)
        out = np.zeros((N, N, 4))
        out[..., :3] = LACQUER
        out[..., 3] = ring
        h_ = np.dstack([np.broadcast_to(PAPER_HI, (N, N, 3)), hair * .92])
        over_rgba(out, h_, 0, 0)
        pcs = []
        for a0 in (-90, 0, 90, 180):
            pcs += TH.arc_strips(c, c, 105.3, a0 + 7, a0 + 83, 2.0, piece=11)
        for a in (0, 90, 180):
            pcs.append(dict(poly=TH._circle(c + 105.3 * math.cos(math.radians(a)), c + 105.3 * math.sin(math.radians(a)), 2.2, 14), axis=0, fam=1))
        pw, ph, pet = TH.najeon_petal(8, 12)
        pcs += [dict(p, poly=[(x + c - pw / 2, y + c - 105.3 - ph / 2) for x, y in p['poly']]) for p in pet]
        over_rgba(out, TH.render_pieces(N, N, pcs), 0, 0)
        return out, ('disc', c, c, 96.0)
    W, H = 312 + 2 * P, 228 + 2 * P
    out = np.zeros((H, W, 4))
    win = (P + 9, P + 9, 294, 210)
    if kind == 'lacquer':        # Spec section 5 (recommended): the THEME KIT's frame_mini (lacquer band 9 px, cut-shell line, corner ears)
        # laid at 312 x 228 = 48 + 12 n by the kit's rule, tiled from the bottom-left like Unity; piece_north (16 x 18 px) with its
        # cell bottom 7 px inside the frame's outer edge
        K = TW.kit(); F = bundle().note['frames']['minimap']
        assert K.ok and K.fits('Frame.Mini', *F['outer']), 'theme kit missing, or 312 x 228 breaks the kit rule 48 + 12 n'
        over_rgba(out, K.frame('Frame.Mini', F['outer'][0], F['outer'][1]), P, P)
        npc = K.simple('Piece.North')
        over_rgba(out, npc, int(round(W / 2 - npc.shape[1] / 2)), P + int(round(F['northCentreFromTop'] - npc.shape[0] / 2)))
    elif kind == 'lattice':      # window frame: wood door frame + an aja lattice transom above and below the map
        win = (P + 9, P + 19, 294, 190)
        fill(out, P - 1, P - 1, W - P + 1, H - P + 1, PAPER_HI, .80)
        fill(out, P, P, W - P, H - P, WOOD)
        for y0 in (P + 7, H - P - 17):
            fill(out, P + 9, y0, W - P - 9, y0 + 10, TH.T['Chip'])
            bars = lattice_fill('aja', 294, 10, u=5, bar=1.3)
            sub = out[y0:y0 + 10, P + 9:W - P - 9]
            sub[..., :3] = sub[..., :3] * (1 - bars[..., None]) + WOOD * bars[..., None]
        fill(out, win[0], win[1], win[0] + win[2], win[1] + win[3], 0, 0)
        fill(out, W / 2 - 2, P - 4, W / 2 + 2, P + 3, PAPER_HI, .95)          # north: one hanji tick on the head rail
    elif kind == 'porcelain':    # white porcelain rim with a dark inlaid line (sanggam)
        fill(out, P - 1, P - 1, W - P + 1, H - P + 1, TH.T['PorcelainShade'])
        fill(out, P, P, W - P, H - P, PORCELAIN)
        fill(out, P + 8, P + 8, W - P - 8, H - P - 8, TH.T['PorcelainShade'])
        m = np.zeros((H, W))
        m[P + 3:H - P - 3, P + 3:W - P - 3] = 1
        m[P + 5:H - P - 5, P + 5:W - P - 5] = 0
        out[..., :3] = out[..., :3] * (1 - m[..., None]) + INLAY_DARK * m[..., None]
        fill(out, P + 9, P + 9, W - P - 9, H - P - 9, 0, 0)
        fill(out, W / 2 - 3, P - 4, W / 2 + 3, P + 2, TH.T['InlayIron'])     # north: one iron-red inlay tick (material colour)
    return out, win


def frame_board(kind):
    """the full-map frame: an opaque 836 x 910 board under the paper (its top-left sits at page (542, 126)). RGB float.
    'lacquer' (recommended) = the THEME KIT's frame_board laid by its rule (80 + 9 n) x (82 + 9 m) over a lacquer body."""
    W, H = BOARD[2], BOARD[3]
    out = np.empty((H, W, 3))
    K = TW.kit()
    if kind == 'lacquer' and K.ok:
        assert K.fits('Frame.MapBoard', W, H), '%d x %d breaks the kit rule (80 + 9 n) x (82 + 9 m)' % (W, H)
        out[:] = LACQUER
        over(out, K.frame('Frame.MapBoard', W, H), 0, 0)
    elif kind == 'lacquer':      # no kit staged: the first private drawing (lacquer tray, nacre line, a faint wanja band)
        out[:] = LACQUER
        bars = lattice_fill('wanja', W, H, u=4, bar=1.0)
        band = np.zeros((H, W))
        band[7:H - 7, 7:W - 7] = 1
        band[17:H - 17, 17:W - 17] = 0
        out[:] = out * (1 - (bars * band * .55)[..., None]) + WOOD * (bars * band * .55)[..., None]
        pcs = TH.strips([(4.5, 4.5), (W - 4.5, 4.5), (W - 4.5, H - 4.5), (4.5, H - 4.5)], 2.0, piece=13, closed=True)
        pcs += TH.strips([(20, H - 60), (W - 20, H - 60)], 1.4, piece=16)                  # the control row sits under this line
        rgba = TH.render_pieces(W, H, pcs)
        over(out, rgba, 0, 0)
        cw, ch, cp = TH.najeon_chrys(7.0, 10)
        ch_ = TH.render_pieces(cw, ch, cp)
        for x, y in ((12, 12), (W - 12, 12), (12, H - 12), (W - 12, H - 12)):
            fill_ = np.zeros((int(ch) + 6, int(cw) + 6, 4))
            fill_[..., :3] = LACQUER
            fill_[..., 3] = 1
            over(out, fill_, x - cw / 2 - 3, y - ch / 2 - 3)
            over(out, ch_, x - cw / 2, y - ch / 2)
    elif kind == 'door':         # the theme kit's v0 sketch: door frame 8 + sutdae lattice band 26 + inner frame 4, paper inside
        out[:] = WOOD
        fill4 = lambda x0, y0, x1, y1, col: out.__setitem__((slice(int(y0), int(y1)), slice(int(x0), int(x1))), col)
        fill4(8, 8, W - 8, H - 64, LACQUER)
        bars = lattice_fill('sutdae', W - 16, H - 72, u=13, bar=1.6)
        sub = out[8:H - 64, 8:W - 8]
        sub[:] = sub * (1 - (bars * .9)[..., None]) + WOOD * 1.25 * (bars * .9)[..., None]
        fill4(34, 34, W - 34, H - 90, WOOD)
        fill4(38, 38, W - 38, H - 94, PAPER)
        fill4(8, H - 56, W - 8, H - 8, LACQUER)
        cw, ch, cp = TH.najeon_chrys(8.0, 10)
        ch_ = TH.render_pieces(cw, ch, cp)
        for x, y in ((21, 21), (W - 21, 21), (21, H - 77), (W - 21, H - 77)):
            fill4(x - 11, y - 11, x + 11, y + 11, LACQUER)
            over(out, ch_, x - cw / 2, y - ch / 2)
    else:                        # porcelain tray: inlaid double line + a lotus band above the control row
        out[:] = PORCELAIN
        m = TH.inlay_frame(W, H, inset=6.0, w1=2.0, gap=2.5, w2=1.0)
        out[:] = out * (1 - m[..., None]) + INLAY_DARK * m[..., None]
        lot = TH.inlay_lotus(48, pw=16.0, ph=11.0)
        lh, lw = lot.shape
        sub = out[H - 64:H - 64 + lh, 34:34 + lw]
        sub[:] = sub * (1 - lot[..., None]) + INLAY_DARK * lot[..., None]
    return out


# ---------------------------------------------------------------------------------------------------------------- minimap
def mini_new(world, cx, cz, heading, fog, found, kind='lacquer', mpp=MPP_MINI, cave=None, mark=28):
    """the proposed minimap: map (notation twin) + markers 22 px + brush-tip mark, inside a frame. RGBA patch + its centre."""
    fr, win = frame_mini(kind)
    if win[0] == 'disc':
        _, c0, c1, R = win
        w = h = int(2 * R)
        x0, y0 = int(round(c0 - R)), int(round(c1 - R))
    else:
        x0, y0, w, h = win
    v = View(cx, cz, mpp, w, h, 3)
    if cave is not None:
        img = cave_map(v, cave, 'new')
    else:
        cv, _ = render_new(v, world, fog)
        img = cv.out()
        place_markers(img, v, world, found, 22)
    over(img, player_mark(mark, heading), w / 2 - mark / 2, h / 2 - mark / 2)
    out = np.zeros(fr.shape)
    a = np.ones((h, w))
    if win[0] == 'disc':
        yy, xx = np.mgrid[0:h, 0:w]
        a = np.clip(R - np.hypot(xx + .5 - R, yy + .5 - R) + .5, 0, 1)
    out[y0:y0 + h, x0:x0 + w, :3] = img
    out[y0:y0 + h, x0:x0 + w, 3] = a
    over_rgba(out, fr, 0, 0)
    return out, img


def mini_today(world, cx, cz, heading, fog, found, mpp=MPP_MINI, cave=None):
    """today's minimap (numpy twin of the #307 state): a bare 300 x 214 hanji rectangle with a soft torn edge, flat paper on
    walked ground, flat wash elsewhere, every road one grey 2.6 px line straightened to 12 m, 20 px pictures, a 26 px arrow
    cell (figure 13.5 x 17 px) and a 4 x 14 px north tick."""
    w, h = 300, 214
    v = View(cx, cz, mpp, w, h, 3)
    X, Z = v.grid()
    cvs = Canvas(v, TODAY_PAPER)
    if cave is not None:
        img = cave_map(v, cave, 'today')
    else:
        draw_roads_ruled(cvs, world, 1, width=2.25, ticks=False, col=TODAY_LINE, alpha=1.0)
        r, _ = fog.reveal(X, Z, mpp)
        cvs.rgb = cvs.rgb * r[..., None] + TODAY_WASH * (1 - r[..., None])
        img = cvs.out()
        place_markers(img, v, world, found, 20, style='today')
    arrow = sprite304('arrow.png', 26, CIN, 1.0, rot=heading)
    rim = sprite304('arrow.png', 26, TODAY_PAPER, 1.0, rot=heading)
    ra = np.asarray(Image.fromarray((rim[..., 3] * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(3)), float) / 255
    over(img, np.dstack([rim[..., :3], ra]), w / 2 - 13, h / 2 - 13)
    over(img, arrow, w / 2 - 13, h / 2 - 13)
    P = 10
    out = np.zeros((h + 2 * P, w + 2 * P, 4))
    yy, xx = np.mgrid[0:h, 0:w]
    edge = np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy)) + (fbm(xx / 9.0, yy / 9.0, 301, 2) - .5) * 5
    out[P:P + h, P:P + w, :3] = img
    out[P:P + h, P:P + w, 3] = smooth(0.0, 5.0, edge)
    fill(out, P + w / 2 - 2, P - 9, P + w / 2 + 2, P + 5, INK, .9)            # north tick 4 x 14
    return out, img


def cave_map(v, zone, style):
    """the mine gallery map: passage = paper (walked cells only), wall = ink line, rock = the unwalked wash. Same re-tint in
    both maps (today the full map shows the plan's original browns). `zone` = (zone dict, walked polyline)."""
    z, walked = zone
    plan = np.asarray(Image.open(CAVE_PLAN).convert('RGB'), float) / 255
    passage = (plan[..., 0] > .62).astype(float)                              # the sand-coloured corridor of CavePlan297
    X, Z = v.grid()
    u = (X / 4000.0 - z['uv'][0]) / z['uv'][2]
    w_ = (Z / 6000.0 - z['uv'][1]) / z['uv'][3]
    H_, W_ = passage.shape
    jj = np.clip((u * W_).astype(int), 0, W_ - 1)
    ii = np.clip(((1 - w_) * H_).astype(int), 0, H_ - 1)
    inside = (u >= 0) & (u < 1) & (w_ >= 0) & (w_ < 1)
    pas = gauss(passage, 3)[ii, jj] * inside
    D, _ = B.resample(np.asarray(walked, float), 1.0)
    cell = 2.0
    cxs, czs = np.floor(X / cell), np.floor(Z / cell)
    seen = np.zeros(X.shape, bool)
    for x, zz in D[::2]:
        seen |= np.hypot((cxs + .5) * cell - x, (czs + .5) * cell - zz) <= 8.0
    seen_s = gauss(seen.astype(float), 2 * v.ss)
    r = smooth(.35, .65, seen_s)
    paper, wash = (PAPER, WASH) if style == 'new' else (TODAY_PAPER, TODAY_WASH)
    grain = (fbm(X / (v.mpp * 3.1), Z / (v.mpp * 3.1), 101, 2) - .5) * .02
    rgb = np.empty(X.shape + (3,))
    rgb[:] = wash
    rgb += grain[..., None]
    floor = smooth(.45, .55, pas) * r
    rgb = rgb * (1 - floor[..., None]) + (paper + grain[..., None]) * floor[..., None]
    gy, gx = np.gradient(pas)
    d_px = (pas - .5) / (np.hypot(gx, gy) + 1e-9) / v.ss
    wall = np.clip(1.25 - np.abs(d_px) / (.9 if style == 'new' else .8), 0, 1) * r * (.85 if style == 'new' else .7)
    rgb = rgb * (1 - wall[..., None]) + INK * wall[..., None]
    return down(rgb, v.ss)


# ---------------------------------------------------------------------------------------------------------------- map page
MAP_WIN = (590, 170, 740, 760)   # print window on the 1920 x 1080 page
BOARD = (542, 126, 836, 910)     # frames.board.rect of the notation: the kit's bottom band lies under the control row, not behind it
SHEET_AT = (560, 140)


def clean_backdrop(cap):
    """the menu veil of a capture with every bright mark removed (min filter + blur at 1/8 scale)."""
    w, h = cap.size
    sm = cap.resize((w // 8, h // 8), Image.BOX).filter(ImageFilter.MinFilter(7)).filter(ImageFilter.GaussianBlur(3))
    return np.asarray(sm.resize((w, h), Image.BICUBIC), float) / 255


def keycap(letter):
    """porcelain key cap (theme kit: plain white porcelain button, inlay-dark letter)."""
    out = np.zeros((32, 32, 4))
    fill(out, 1, 1, 31, 31, PORCELAIN)
    fill(out, 1, 27, 31, 31, TH.T['PorcelainShade'])
    over(out[..., :3], text_rgba(letter, font('N7', 18), INLAY_DARK, 0), 16 - font('N7', 18).getlength(letter) / 2, 2)
    return out


def swatch(kind, w=44, h=24, scale=1):
    """one legend sample drawn FROM THE BUNDLE at region-band widths (the game's legend draws the same rows of the stroke
    atlas and the same pattern channels). RGB float (h, w, 3)."""
    b = bundle(); T = b.note['terrain']
    out = np.empty((h, w, 3)); out[:] = PAPER

    def band_(cls, rank=0, y=None, x0=2.0, x1=None, left=True):
        sty = b.stroke_style(cls, rank, 2); ar = b.atlas_rows[sty['row']]
        a = np.zeros((h, w)); k = np.zeros((h, w)); yy = h / 2 if y is None else y
        P = np.array([[x0, yy], [w - 2.0 if x1 is None else x1, yy]])
        TW.raster(a, k, P, ML.cum_len(P), np.ones(2), np.full(2, 8 if left else 0, np.uint8), sty['width'], b.rows[sty['row']], ar['axisV'],
                  1024.0 * (sty['width'] / 40.0) * (ar['uAspect'] or 1.0), 0, ar['uMode'] == 'stretch', float(ML.cum_len(P)[-1]))
        a = a * sty['alpha']
        out[:] = out * (1 - a[..., None]) + INK * a[..., None]
    if kind == 'ridge':
        band_(2, 1, y=h * .62)
    elif kind == 'cliff':
        band_(3, 0, y=h * .68)
    elif kind == 'wall':
        band_(5)
    elif kind in ('water', 'forest'):
        ch = 1 if kind == 'water' else 0
        pp = b.pattern[0][8:8 + h, 6:6 + w, ch] * (T['water']['rippleInk'] if ch else T['forest']['dotInk'])
        out[:] = out * (1 - pp[..., None]) + INK * pp[..., None]
        if kind == 'water':
            out[3:5, :] = PAPER * (1 - T['water']['shoreInk']) + INK * T['water']['shoreInk']
    elif kind in ROAD_CLASS:
        band_(ROAD_CLASS[kind])
    elif kind == 'bridge':
        band_(9); band_(10, x0=10.0, x1=w - 10.0)
    elif kind == 'unwalked':
        out[:, w // 2:] = WASH
        out[:, w // 2 - 1:w // 2 + 1] = out[:, w // 2 - 1:w // 2 + 1] * .55 + INK * .45
    return out


LEGEND_ROWS = [('player', '현재 위치', '지금 선 자리와 바라보는 쪽'), ('inn', '쉼터', '쉬면 체력과 먹이 찬다. 고리 두른 곳에서 깬다'),
               ('village', '발견한 장소', '다녀가서 이름을 안 곳. 그림이 곳의 생김이다'), ('coin', '남긴 통보', '쓰러진 자리에 떨어뜨린 조선통보'),
               ('pin', '내 표식', '지도를 눌러 직접 찍은 자리'), ('objective', '들은 목적 권역', '의뢰에서 들은 곳 일대'), ('unwalked', '걷지 않은 땅', '걸어 본 땅만 먹이 선명하다')]
LEGEND_GRID = [('ridge', '산줄기'), ('cliff', '벼랑'), ('wall', '성벽'), ('water', '물'), ('forest', '숲'), ('daero', '큰길'), ('gil', '길'), ('soro', '샛길'), ('bridge', '다리')]


def roundel(gid, d=44):
    """Plate.Icon: lacquer roundel with a nacre rim, the SAME atlas glyph tinted paper (cinnabar for the two cinnabar marks)."""
    out = np.zeros((d + 2, d + 2, 4))
    yy, xx = np.mgrid[0:(d + 2) * 4, 0:(d + 2) * 4]
    r = np.hypot((xx + .5) / 4 - (d + 2) / 2, (yy + .5) / 4 - (d + 2) / 2)
    out[..., :3] = LACQUER
    out[..., 3] = down((r <= d / 2).astype(float), 4)
    over_rgba(out, TH.render_pieces(d + 2, d + 2, TH.arc_strips((d + 2) / 2, (d + 2) / 2, d / 2 - 1.2, 0, 360, 1.4, piece=12)), 0, 0)
    g = int(d * .74)
    if gid == 'player':
        pm = player_mark(g, 28, rim_col=LACQUER)
        over_rgba(out, pm, (d + 2 - g) / 2, (d + 2 - g) / 2)
    else:
        ic = icon(gid, g, ink_col=CIN if gid == 'objective' else PAPER_HI, rim_col=LACQUER, rim_a=0.0, ink_a=1.0)
        over_rgba(out, ic, (d + 2 - g) / 2, (d + 2 - g) / 2)
    return out


def nacre_rule(w, width=1.6):
    """Inlay.Line: one row of cut shell strips."""
    return TH.render_pieces(w, 4, TH.strips([(1, 2), (w - 1, 2)], width, piece=14))


def page_new(world, map_img, here='청림 · 벌목마을', board='lacquer'):
    """the proposed full-map page at 1920 x 1080: today's capture keeps the tab rail and both side columns' text blocks; the
    map, its frame, the legend and the control row are redrawn."""
    cap = Image.open(CAP_FULL).convert('RGB')
    page = np.asarray(cap, float) / 255
    bg = clean_backdrop(cap)
    # legend column and the divider strokes: back to the bare veil
    for x0, y0, x1, y1 in ((40, 284, 548, 1070), (1390, 336, 1920, 372), (40, 246, 548, 280)):
        yy, xx = np.mgrid[y0:y1, x0:x1]
        f = np.minimum(np.minimum(xx - x0, x1 - 1 - xx), np.minimum(yy - y0, y1 - 1 - yy)) / 10.0
        f = np.clip(f, 0, 1)[..., None]
        page[y0:y1, x0:x1] = page[y0:y1, x0:x1] * (1 - f) + bg[y0:y1, x0:x1] * f
    # board + sheet + map
    bx, by, bw, bh = BOARD
    page[by:by + bh, bx:bx + bw] = frame_board(board)
    mx, my, mw, mh = MAP_WIN
    if board == 'door':
        page[by + 38:by + bh - 94, bx + 38:bx + bw - 38] = PAPER
    else:
        if board == 'porcelain':                      # paper on porcelain needs its own shadow line to separate
            sh = np.asarray(Image.open(TEX / 'sheet_map.png').convert('RGBA'), float)[..., 3] / 255
            sh = np.asarray(Image.fromarray((sh * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(5)), float) / 255
            over(page, np.dstack([np.broadcast_to(INLAY_DARK, sh.shape + (3,)), sh * .45]), SHEET_AT[0] + 3, SHEET_AT[1] + 4)
        over(page, np.asarray(Image.open(TEX / 'sheet_map.png').convert('RGBA'), float) / 255, *SHEET_AT)
    yy, xx = np.mgrid[0:mh, 0:mw]
    edge = np.minimum(np.minimum(xx, mw - 1 - xx), np.minimum(yy, mh - 1 - yy)) + (fbm(xx / 14.0, yy / 14.0, 311, 2) - .5) * 6
    over(page, np.dstack([map_img, smooth(0, 7, edge)]), mx, my)
    slip = np.asarray(cap.crop((604, 158, 694, 364)), float) / 255        # the title slip is paper + ink: kept as it is today
    page[158:364, 604:694] = slip
    # control row on the board
    for letter, label, x in (('R', '현재 위치', 630), ('T', '전체 보기', 799), ('L', '범례 접기', 968), ('X', '표식 지우기', 1137)):
        col = PAPER_HI if board != 'porcelain' else INLAY_DARK
        kc = keycap(letter)
        if board == 'porcelain':
            kc[..., :3] = np.where(kc[..., :3].sum(-1, keepdims=True) > 2.2, LACQUER, PAPER_HI)
        over(page, kc, x, 972)
        over(page, text_rgba(label, font('N7', 22), col, 0), x + 44, 973)
    # legend: markers (7 rows, 72 px) then terrain & roads (3 x 3)
    over(page, text_rgba('범례', font('N4', 19), MIST, 0), 64, 292)
    over(page, nacre_rule(440), 64, 322)
    for i, (gid, name, mean) in enumerate(LEGEND_ROWS):
        y = 336 + i * 70
        if gid == 'unwalked':
            sw = swatch('unwalked', 44, 30)
            page[y + 8:y + 38, 64:108] = sw
        else:
            over(page, roundel(gid), 63, y)
        over(page, text_rgba(name, font('S8', 24), PAPER_HI, 0), 126, y - 1)
        over(page, text_rgba(mean, font('N4', 17), MIST, 0), 126, y + 32)
    y0 = 336 + 7 * 70 + 6
    over(page, text_rgba('지형과 길', font('N4', 19), MIST, 0), 64, y0)
    over(page, nacre_rule(440), 64, y0 + 30)
    for i, (kind, name) in enumerate(LEGEND_GRID):
        r, c = divmod(i, 3)
        x, y = 64 + c * 150, y0 + 44 + r * 44
        page[y:y + 24, x:x + 44] = swatch(kind)
        page[y - 1, x - 1:x + 45] = page[y + 24, x - 1:x + 45] = LACQUER
        over(page, text_rgba(name, font('N7', 18), PAPER_HI, 0), x + 54, y - 3)
    over(page, nacre_rule(430), 1404, 350)           # place-list divider = inlay line
    over(page, nacre_rule(430), 64, 262)
    return page


def map_overlays(img, v, world, found, wake, player, heading, realm_names=True, left_pad=110):
    """what sits ON the paper of the full map: realm names (always, D269), place plates, marks, the brush tip, north."""
    if realm_names:
        im = to_img(img)
        d = ImageDraw.Draw(im)
        for r in world.realms:
            px, py = v.to_px([r['centre']])[0] / v.ss
            f = font('S9', 44)
            tw = f.getlength(r['label'])
            inview = -.15 * v.w < px < 1.15 * v.w and -.15 * v.h < py < 1.15 * v.h
            px = min(max(px, left_pad + tw / 2), v.w - 24 - tw / 2)
            py = min(max(py, 60), v.h - 40)
            if inview:
                d.text((px - tw / 2, py - 32), r['label'], font=f, fill=tuple(int(c * 255) for c in INK), stroke_width=3, stroke_fill=tuple(int(c * 255) for c in PAPER))
        img[:] = np.asarray(im, float) / 255
    place_markers(img, v, world, found, 30, labels=True, wake=wake)
    if player is not None:
        px, py = v.to_px([player])[0] / v.ss
        over(img, player_mark(48, heading), px - 24, py - 24)
    over(img, text_rgba('북', font('S8', 22), INK, 0), v.w - 50, 14)
    img[46:70, v.w - 41:v.w - 39] = INK


# ---------------------------------------------------------------------------------------------------------------- panels
BG = hx('#CFCABD')
A_CENTRE = (2747.0, 2155.0)      # the view of today's capture (map_revealed.png): fitted on the realm border corner
A_WAKE = (3247.0, 1892.0)
HEADING = 22.0
SW = 3880                        # panel width
REPORT = {}


class Pn:
    def __init__(self, w, h, bg=BG):
        self.a = np.empty((h, w, 3))
        self.a[:] = bg

    def put(self, img, x, y, outline=True):
        img = np.asarray(img, float)
        h, w = img.shape[:2]
        self.a[y:y + h, x:x + w] = img[:self.a.shape[0] - y, :self.a.shape[1] - x]
        if outline:
            self.a[y - 1:y, x - 1:x + w + 1] = self.a[y + h:y + h + 1, x - 1:x + w + 1] = ASH
            self.a[y - 1:y + h + 1, x - 1:x] = self.a[y - 1:y + h + 1, x + w:x + w + 1] = ASH

    def t(self, s, x, y, face='N4', px=22, col=INK):
        over(self.a, text_rgba(s, font(face, px), col, 0), x, y)

    def head(self, s, sub=()):
        self.t(s, 0, 0, 'S9', 38)
        for i, line in enumerate(sub):
            self.t(line, 0, 54 + i * 30, 'N4', 21, ASH)
        return 54 + len(sub) * 30 + 14

    def img(self):
        return to_img(self.a)


def zoom(a, k):
    return np.repeat(np.repeat(np.asarray(a), k, axis=0), k, axis=1)


def road_pts(world, rid):
    return [r for r in world.roads if r['id'] == rid][0]['pts']


def cut(P, metres):
    """the first `metres` of a polyline."""
    D, t = B.resample(P, 4.0)
    return D[t <= metres]


def walked_route(world):
    """a believable first hour: mine -> overlook -> inn -> relay -> village, then a little way out on two roads."""
    parts = [road_pts(world, 'mine__mine_overlook'), road_pts(world, 'mine_overlook__geumpyo_inn'), road_pts(world, 'inn_relay245'),
             road_pts(world, 'relay_village245'), cut(road_pts(world, 'village_logging251'), 330.0), cut(road_pts(world, 'merchant__road_pass'), 210.0)]
    return parts


def found_on(world, parts, r=26.0):
    out = set()
    for m in world.markers:
        for P in parts:
            D, _ = B.resample(P, 6.0)
            if np.hypot(D[:, 0] - m['xz'][0], D[:, 1] - m['xz'][1]).min() <= r:
                out.add(m['id'])
    return out


def fog_of(world, parts, region=False):
    f = Fog()
    for P in parts:
        f.walk(P, region=world.top if (region and world.top.any()) else None)
    return f


def measure_roads(img, v, world, classes=('daero', 'gil', 'soro')):
    """WCAG contrast of the road core against the ground 3 px outside the stroke, sampled along the real centre lines."""
    L = lum(img)
    out = {}
    for cls in classes:
        vals = []
        half = road_px(cls, v.band)[0] / 2
        for r in world.roads:
            if r['cls'] != cls or not v.near(r['pts'], 0):
                continue
            D, _ = B.resample(r['pts'], 3.0 * v.mpp)
            P = v.to_px(D) / v.ss
            d = np.gradient(P, axis=0)
            n = np.stack([-d[:, 1], d[:, 0]], 1) / (np.hypot(d[:, 0], d[:, 1])[:, None] + 1e-9)
            for sg in (-1, 1):
                Q = P + n * sg * (half + road_px(cls, v.band)[1] + 3.0)
                ok = (P[:, 0] > 2) & (P[:, 0] < v.w - 2) & (P[:, 1] > 2) & (P[:, 1] < v.h - 2) & (Q[:, 0] > 0) & (Q[:, 0] < v.w - 1) & (Q[:, 1] > 0) & (Q[:, 1] < v.h - 1)
                a = L[P[ok, 1].astype(int), P[ok, 0].astype(int)]
                b_ = L[Q[ok, 1].astype(int), Q[ok, 0].astype(int)]
                vals += list((np.maximum(a, b_) + .05) / (np.minimum(a, b_) + .05))
        if vals:
            vals = np.array(vals)
            out[cls] = dict(median=round(float(np.median(vals)), 2), share_ge_4_5=round(float((vals >= 4.5).mean()), 3), share_ge_3=round(float((vals >= 3).mean()), 3), n=int(len(vals)))
    return out


def panel_a(world):
    """(a) the full map page: today's capture vs the proposed notation, same crop, 1920 x 1080."""
    v = View(A_CENTRE[0], A_CENTRE[1], MPP_FULL, 740, 760, 2)
    cv, _ = render_new(v, world)
    bare = cv.out().copy()
    img = bare.copy()
    map_overlays(img, v, world, {'village'}, A_WAKE, A_CENTRE, HEADING)
    page = page_new(world, img)
    cap = np.asarray(Image.open(CAP_FULL).convert('RGB'), float) / 255
    REPORT['a_road_contrast_new'] = measure_roads(bare, v, world)
    p = Pn(SW, 1080 + 190)
    y = p.head('(가) 펼친 지도 — 지금과 새 표기, 같은 자리 · 같은 배율 (1920×1080 실제 크기)',
               ('왼쪽 = 지금의 실제 캡처(전부 드러낸 검사 상태). 오른쪽 = 같은 창(중심 x 2747 · z 2155, 2.66 m/px)을 실제 높이장 · 길 · 숲 자료로 새 표기법에 따라 다시 그린 것.',
                '기복 음영을 버리고 산줄기(톱니 띠) · 담묵(비탈) · 숲 점 · 붓 띠 길(큰길 · 길 · 샛길)로 그렸다. 틀 = 한지를 받친 흑칠 판 + 자개 끊음질 선, 범례 = 표식 7행 + 지형과 길 9칸.'))
    p.t('지금', 0, y, 'S8', 26)
    p.t('새 표기 (권장안: 갈필 붓 띠 길 · 옻칠 판)', 1960, y, 'S8', 26, CIN)
    p.put(cap, 0, y + 44)
    p.put(page, 1960, y + 44)
    b_ = REPORT['a_road_contrast_new']
    p.t('길 심과 둘레(획 밖 3 px)의 대비 — 지금: 중앙값 2.92:1, 길 화소의 52%%가 3:1 미만(Spec L6, 이 캡처에서 잰 값) / 새(이 합성에서 잰 값): 큰길 %.1f:1 · 길 %.1f:1 · 샛길 %.1f:1, 4.5:1 이상인 표본 %d%% · %d%% · %d%% (나머지는 둘레 표본이 다른 길 · 능선 위에 떨어진 곳과 샛길의 마른 끊김)' % (
        b_['daero']['median'], b_['gil']['median'], b_['soro']['median'],
        round(b_['daero']['share_ge_4_5'] * 100), round(b_['gil']['share_ge_4_5'] * 100), round(b_['soro']['share_ge_4_5'] * 100)), 0, y + 44 + 1080 + 12, 'N7', 21)
    return p.img(), dict(page=page, map=img, bare=bare, view=v)


def hud_backdrop():
    return np.asarray(Image.open(HUD_FRAME).convert('RGB'), float) / 255


def mini_scene(world):
    """the shared minimap state: standing 40 m out of the village on the relay road, having walked in from the mine."""
    parts = walked_route(world)
    fog = fog_of(world, parts)
    P = road_pts(world, 'relay_village245')
    D, t = B.resample(P[::-1], 2.0)
    here = D[np.searchsorted(t, 46.0)]
    return dict(fog=fog, found=found_on(world, parts), here=(float(here[0]), float(here[1])), heading=205.0)


def panel_b(world):
    """(b) the minimap at its two windows, today vs new, real size beside the HUD vessels + 300 %."""
    sc = mini_scene(world)
    zone = [z for z in world.zones if z['id'] == 'mine_interior'][0]
    cave_walk = zone['path'][:5]
    cave_here = tuple(cave_walk[-1])
    hud = hud_backdrop()
    cx, cy = 1740, 900
    t_out, t_img = mini_today(world, *sc['here'], sc['heading'], sc['fog'], sc['found'])
    n_out, n_img = mini_new(world, *sc['here'], sc['heading'], sc['fog'], sc['found'])
    t_cave, _ = mini_today(world, *cave_here, 90.0, None, set(), mpp=MPP_CAVE, cave=(zone, cave_walk))
    n_cave, _ = mini_new(world, *cave_here, 90.0, None, set(), mpp=MPP_CAVE, cave=(zone, cave_walk))

    def on_hud(patch):
        f = hud.copy()
        over(f, patch, cx - patch.shape[1] / 2, cy - patch.shape[0] / 2)
        return f
    p = Pn(SW, 1800)
    y = p.head('(나) 미니맵 — 야외 창(336×240 m)과 갱도 창(78×56 m), 지금과 새 표기',
               ('위 = 1080p 실제 화면 아랫단(1920×330)에 그대로 놓은 것. 왼쪽 아래 먹 용기 군집(SPEC-HUD-LIQUID-308 시안)과 나란히 본다. 가운데 = 300% 확대(화소 그대로). 아래 = 갱도 창과 1440p 실제 캡처.',
                '지금: 틀 없는 한지 300×214, 걸은 땅 = 빈 종이, 길 = 회색 한 줄 2.25 px(12 m로 편 선), 화살표 13.5×17 px.  새: 흑칠 틀 9 px + 자개 선, 능선 · 담묵 · 숲 점, 길 4.5 / 3.2 / 2.2 px + 종이 여백, 자루 달린 주사 붓 28 px 칸, 안 걸은 땅 한 단 짙게 + 또렷한 경계의 먹 테.'))
    p.t('지금 (쌍둥이 합성)', 0, y, 'S8', 26)
    p.t('새 표기', 1960, y, 'S8', 26, CIN)
    p.put(on_hud(t_out)[750:1080], 0, y + 42)
    p.put(on_hud(n_out)[750:1080], 1960, y + 42)
    y2 = y + 42 + 330 + 50
    items = (('지금 · 야외 300%', t_out), ('새 · 야외 300%', n_out), ('지금 · 갱도 300%', t_cave), ('새 · 갱도 300%', n_cave))
    for i, (cap_, patch) in enumerate(items):
        f = hud.copy()
        over(f, patch, cx - patch.shape[1] / 2, cy - patch.shape[0] / 2)
        crop = f[900 - 128:900 + 120, 1740 - 162:1740 + 162]          # 4 px up: the kit's north piece stands 11 px above the frame
        x = i * 973
        p.t(cap_, x, y2, 'S8', 24, CIN if '새' in cap_ else INK)
        p.put(zoom(crop, 3)[:, :954], x, y2 + 38)
    y3 = y2 + 38 + 744 + 50
    p.t('갱도 창 100% (지금 / 새)', 0, y3, 'S8', 24)
    p.put(on_hud(t_cave)[750:1080, 1500:1920], 0, y3 + 38)
    p.put(on_hud(n_cave)[750:1080, 1500:1920], 440, y3 + 38)
    p.t('1440p 실제 캡처 (0.75배 = 1080 기준 크기): 야외 · 갱도 — 쌍둥이가 맞는지 보는 기준', 900, y3, 'S8', 24)
    for i, f in enumerate((CAP_MINI, ROOT / 'Art/Playtest306/Checks/b23_mini_a_cave_2560x1440.png')):
        c = Image.open(f).convert('RGB').crop((2000, 1000, 2560, 1440)).resize((420, 330), Image.LANCZOS)
        p.put(np.asarray(c, float) / 255, 900 + i * 440, y3 + 38)
    # numbers
    REPORT['b_values'] = dict(walked_vs_unwalked_today=round(contrast(TODAY_PAPER, TODAY_WASH), 2), walked_vs_unwalked_new=round(contrast(PAPER, WASH), 2),
                              darkest_wash_vs_unwalked=round(contrast(PAPER * (1 - WASH_MAX), WASH), 2),
                              road_ink_vs_paper={k: round(contrast(PAPER * (1 - bundle().classes[ROAD_CLASS[k]]['inkAlpha']) + INK * bundle().classes[ROAD_CLASS[k]]['inkAlpha'], PAPER), 2) for k in ('daero', 'gil', 'soro')},
                              ink_vs_unwalked=round(contrast(PAPER * .15 + INK * .85, WASH), 2), cinnabar_vs_paper=round(contrast(CIN, PAPER), 2),
                              cinnabar_vs_paper_cvd={k: round(contrast(TH.cvd(CIN, k), TH.cvd(PAPER, k)), 2) for k in ('protan', 'deutan', 'tritan')},
                              paper_vs_wash_cvd={k: round(contrast(TH.cvd(PAPER, k), TH.cvd(WASH, k)), 2) for k in ('protan', 'deutan', 'tritan')},
                              lacquer_vs_nacre=round(contrast(LACQUER, TH.nacre_token()), 2))
    REPORT['b_mini_road_contrast_new'] = measure_roads(n_img, View(*sc['here'], MPP_MINI, 294, 210, 3), world)
    b_ = REPORT['b_values']
    x = 1800
    for i, line in enumerate(('대비 (WCAG 휘도 비, 합성값)',
                              '걸은 땅 / 안 걸은 땅: 지금 %.2f → 새 %.2f:1 (+ 경계 먹 테)' % (b_['walked_vs_unwalked_today'], b_['walked_vs_unwalked_new']),
                              '가장 짙은 담묵 / 안 걸은 땅: %.2f:1' % b_['darkest_wash_vs_unwalked'],
                              '길 먹 / 종이: 큰길 %.1f · 길 %.1f · 샛길 %.1f:1' % tuple(b_['road_ink_vs_paper'][k] for k in ('daero', 'gil', 'soro')),
                              '먹 선 / 안 걸은 땅: %.1f:1' % b_['ink_vs_unwalked'],
                              '주사 / 종이: %.2f:1 — 색각 모의 %.2f · %.2f · %.2f:1' % ((b_['cinnabar_vs_paper'],) + tuple(b_['cinnabar_vs_paper_cvd'][k] for k in ('protan', 'deutan', 'tritan'))),
                              '종이 / 씻김 색각 모의: %.2f · %.2f · %.2f:1 (무채색이라 그대로)' % tuple(b_['paper_vs_wash_cvd'][k] for k in ('protan', 'deutan', 'tritan')),
                              '틀: 자개 / 흑칠 %.1f:1' % b_['lacquer_vs_nacre'],
                              '최소 크기(1080p): 큰길 4.5 · 길 3.2 · 샛길 2.2 · 능선 9 / 6 / 3 px, 아이콘 22 px, 현재 위치 먹 20×24 px(28 px 칸)',
                              '비용(Spec 값, 미실측): 배치 +2(띠 · 틀), 텍스처 +4.5 MB 이하, 길 재인쇄 0회')):
        p.t(line, x, y3 + 38 + i * 31, 'N7' if i == 0 else 'N4', 22 if i == 0 else 21)
    return p.img(), dict(scene=sc, new=n_out, today=t_out)


def panel_c(world):
    """(c) the terrain notation alone at three scales."""
    p = Pn(SW, 1010)
    y = p.head('(다) 지형 표기만 — 세 배율 (길 · 아이콘 없음)',
               ('전도(7.9 m/px): 고도 담묵 + 1 · 2등급 산줄기 + 물줄기.  권역(2.66 m/px): 비탈 담묵(22°–45° → 0–18%) + 산줄기 3등급 + 숲 점 + 물가 선.  발밑(1.14 m/px, 미니맵): 같은 것을 더 굵게, 급사면에 암준, 물결.',
                '방향 음영(햇빛 기복)은 없다. 덩어리는 능선 띠 · 담묵 · 숲 점이 만들고 골짜기와 평지는 맨 종이로 남는다. 산줄기 톱니는 가파른 쪽을 향한다.'))
    lay = ('terrain', 'forest', 'ridges', 'water', 'cliffs', 'walls')
    v3 = View(2000, 3000, MPP_WORLD, 507, 760, 2)
    v2 = View(A_CENTRE[0], A_CENTRE[1], MPP_FULL, 740, 760, 2)
    p.t('전도 507×760 px', 0, y, 'S8', 24)
    p.put(render_new(v3, world, layers=lay)[0].out(), 0, y + 36)
    p.t('권역 740×760 px', 560, y, 'S8', 24)
    p.put(render_new(v2, world, layers=lay)[0].out(), 560, y + 36)
    for i, (name, c) in enumerate((('발밑 · 산등성이와 숲 (x 2740 · z 2640) 300%', (2740, 2640)), ('발밑 · 물가 (x 3400 · z 3660) 300%', (3400, 3660)))):
        v1 = View(c[0], c[1], MPP_MINI, 294, 210, 3)
        im = render_new(v1, world, layers=lay)[0].out()
        x = 1340 + i * 920
        p.t(name, x, y, 'S8', 24)
        p.put(zoom(im, 3), x, y + 36)
        p.t('100%', 3190, y + 36 + i * 260 - 0, 'N4', 20, ASH)
        p.put(im, 3190, y + 64 + i * 260)
    p.t('발밑 창은 미니맵과 같은 336×240 m. 4 m 높이 칸이 3.5 px로 늘어나는 배율이라 담묵은 부드럽게만 쓰고 모양은 띠와 점(세계에 붙은 무늬)이 낸다.', 1340, y + 36 + 630 + 14, 'N4', 21, ASH)
    return p.img(), {}


def panel_d(world):
    """(d) three road notations on the same road."""
    sc = mini_scene(world)
    p = Pn(SW, 900)
    y = p.head('(라) 길 표기 세 안 — 같은 길, 같은 창',
               ('A 옛 지도식 곧은 선 + 200 m 눈금(지금 방식): 12 m로 편 선이라 서 있는 길과 선이 어긋난다(붓 촉이 선 밖).  B 갈필 붓 띠(권장): 실제 길 중심선을 따르고 굵기 · 마른 결로 큰길 / 길 / 샛길을 가른다.  C 판각 띠: 찍은 무늬가 기계적으로 읽힌다.',
                '등급은 폭 · 수레 통행 같은 물리 속성으로만 나눈다(본선 · 지름길 같은 역할은 읽지 않는다 — 추천 경로가 되지 않게).'))
    v1 = View(*sc['here'], MPP_MINI, 294, 210, 3)
    v2 = View(2880, 2290, MPP_FULL, 366, 420, 2)
    for i, (mode, name) in enumerate((('A', 'A 곧은 먹선 + 거리 눈금'), ('B', 'B 갈필 붓 띠 (권장)'), ('C', 'C 판각 띠'))):
        x = i * 1300
        p.t(name, x, y, 'S8', 26, CIN if mode == 'B' else INK)
        im = render_new(v1, world, road_mode=mode)[0].out()
        over(im, player_mark(28, sc['heading']), 147 - 14, 105 - 14)
        p.put(zoom(im, 3), x, y + 40)
        p.t('미니맵 100%', x + 900, y + 40, 'N4', 20, ASH)
        p.put(im, x + 900, y + 68)
        p.t('펼친 지도 100%', x + 900, y + 68 + 224, 'N4', 20, ASH)
        im2 = render_new(v2, world, road_mode=mode)[0].out()
        p.put(im2[:380], x + 900, y + 68 + 252)
        if mode == 'A':
            # how far the straightened line is from the real one inside this window
            dmax = 0.0
            for r in world.roads:
                if not v1.near(r['pts'], 0):
                    continue
                S_ = simplify(r['pts'], 12.0)
                D, _ = B.resample(r['pts'], 2.0)
                seg = np.diff(S_, axis=0)
                for q in D:
                    if abs(q[0] - v1.cx) > 168 or abs(q[1] - v1.cz) > 120:
                        continue
                    tt = np.clip(((q - S_[:-1]) * seg).sum(1) / ((seg ** 2).sum(1) + 1e-9), 0, 1)
                    dmax = max(dmax, float(np.hypot(*(S_[:-1] + seg * tt[:, None] - q).T).min()))
            REPORT['d_straighten_error_in_window_m'] = round(dmax, 1)
            p.t('이 창 안에서 편 선과 실제 길의 최대 어긋남 %.1f m = %.1f px' % (dmax, dmax / MPP_MINI), x, y + 40 + 630 + 12, 'N4', 21, ASH)
    return p.img(), {}


def panel_e(world):
    """(e) the icon family: every mark of the bundle's atlas at real size (22 px minimap, 30 / 34 px map) and 400 %."""
    b = bundle()
    cols, cw, chh = 7, 554, 236
    cells = [g['id'] for g in b.note['glyphs']] + ['north']
    names = {g['id']: g['nameKo'] + (' (예약)' if g.get('reserved') else '') for g in b.note['glyphs']}
    rows = (len(cells) + cols - 1) // cols
    p = Pn(SW, 150 + rows * chh + 96)
    y = p.head('(마) 아이콘 한 벌 — 먹 기호 + 한지 테, 실제 크기와 400% (게임이 읽는 아틀라스 map308_icons_S / _L에서 그대로)',
               ('칸마다: 큰 그림 둘 = 미니맵 22 px(S 판)와 펼친 지도 30 px(L 판)의 400%(화소 그대로). 작은 그림 = 실제 크기로 종이(걸은 땅) 위와 씻김(안 걸은 땅 경계) 위. 오른쪽 아래 회색 = 지금 쓰는 그림.',
                '곳의 생김만 그린다(보스 · 목표 표시 없음). 표식 id → 기호 표로 고른다. 지금은 그림이 5종이고 나머지는 이름 낱말로 굴 / 산 그림을 고른다. 주사는 현재 위치와 들은 목적 권역뿐.'))
    today_of = dict(inn='rest', village='village', gaekju='village', relay='cave', checkpoint='cave', gate='gate', mine='cave', cave='cave', temple='cave',
                    peak='mountain', bigtree='cave', deepforest='cave', worksite='cave', trace='cave', camp='cave', hall='cave', waterside='cave', place='cave')
    for i, gid in enumerate(cells):
        r, c = divmod(i, cols)
        x0, y0 = c * cw, y + r * chh
        p.a[y0:y0 + chh - 14, x0:x0 + cw - 14] = PAPER
        p.a[y0 + 150:y0 + chh - 14, x0 + 340:x0 + 452] = WASH
        if gid == 'player':
            name = '현재 위치 (주사 붓: 촉 · 가락지 · 자루)'
            s22, s30 = player_mark(28, 30.0), player_mark(48, 30.0)
        elif gid == 'north':
            name = '북쪽 (틀 위 자개 조각)'
            pw, ph, pet = TH.najeon_petal(9, 13)
            n_ = TH.render_pieces(pw, ph, pet)
            s22 = np.zeros((22, 22, 4))
            s22[..., :3] = LACQUER
            s22[..., 3] = 1
            over_rgba(s22, n_, 11 - pw / 2, 11 - ph / 2)
            s30 = s22
        else:
            name = names[gid]
            col = CIN if gid == 'objective' else INK
            s22, s30 = icon(gid, 22, ink_col=col), icon(gid, 34 if gid == 'inn' else 30, ink_col=col)
        p.t(name, x0 + 10, y0 + 6, 'S8', 22)

        def chip(src, bg):
            a = np.empty(src.shape[:2] + (3,))
            a[:] = bg
            over(a, src, 0, 0)
            return a
        z22, z30 = zoom(chip(s22, PAPER), 4), zoom(chip(s30, PAPER), 4)
        p.a[y0 + 40:y0 + 40 + z22.shape[0], x0 + 10:x0 + 10 + z22.shape[1]] = z22
        z30 = z30[:178]
        xb = x0 + 20 + z22.shape[1]
        p.a[y0 + 40:y0 + 40 + z30.shape[0], xb:xb + z30.shape[1]] = z30
        over(p.a, s22, x0 + 350, y0 + 62)
        over(p.a, s30, x0 + 390, y0 + 56)
        over(p.a, s22, x0 + 350, y0 + 166)
        over(p.a, s30, x0 + 390, y0 + 160)
        p.t('%d px · %d px' % (s22.shape[0], s30.shape[0]), x0 + 346, y0 + 112, 'N4', 16, ASH)
        if gid in today_of:
            over(p.a, sprite304(f'map/pict_{today_of[gid]}.png', 30, ASH), x0 + 474, y0 + 56)
            p.t('지금', x0 + 472, y0 + 92, 'N4', 16, ASH)
        elif gid == 'player':
            over(p.a, sprite304('arrow.png', 48, CIN, rot=30.0), x0 + 466, y0 + 48)
            p.t('지금', x0 + 472, y0 + 100, 'N4', 16, ASH)
        elif gid == 'coin':
            over(p.a, sprite304('map/coin_tongbo.png', 22, ASH), x0 + 478, y0 + 60)
            p.t('지금', x0 + 472, y0 + 92, 'N4', 16, ASH)
    num = TW.icon_numbers(b)
    REPORT['e_icons'] = num
    pl, bl = num['confusable_pairs_at_22px']['plain'], num['confusable_pairs_at_22px']['blur_0_8px']
    p.t('획 굵기: S 판(32 px)과 22 px에서 2 px보다 가는 몸통 조각(3 px 이상) %d종 · %d종. 22 px에서 2 px 여닫기 뒤 남는 먹 최소 %.0f%%(잃는 것은 뾰족한 끝).' % (
        len(num['thin_parts']['glyphs_with_a_thin_part_at_S_32px']), len(num['thin_parts']['glyphs_with_a_thin_part_at_22px']), num['S_at_22px_stroke_ge_2px_survival_min'] * 100),
        0, y + rows * chh + 6, 'N4', 21, ASH)
    sh, pm = num['confusable_pairs_at_22px']['shift_1px_in_use'], num['player_mark_at_28']
    p.t('닮은 쌍(22 px 먹 상관): 장소 기호 21종 · 210쌍 가운데 .70 넘는 쌍 %d개(최대 %.2f %s–%s). 쓰는 기호 %d종을 한 화소씩 밀어 가며 재면 %d개(최대 %.2f %s–%s). 0.8 px 흐려 재면 %d개(최대 %.2f).  현재 위치 그림 %d×%d px, 한지 판 포함 %d×%d px(28 px 칸), 지금 13.5×17 px.' % (
        pl['above_0_70'], pl['max'], names[pl['max_pair'][0]], names[pl['max_pair'][1]], sh['glyphs'], sh['above_0_70'], sh['max'], names[sh['max_pair'][0]], names[sh['max_pair'][1]],
        bl['above_0_70'], bl['max'], pm['figure_px'][0], pm['figure_px'][1], pm['with_plate_px'][0], pm['with_plate_px'][1]), 0, y + rows * chh + 40, 'N4', 21, ASH)
    return p.img(), {}


def panel_f(world):
    """(f) a partly explored state: relief, roads and icons all end where the walked ground ends."""
    parts = walked_route(world)
    fog = fog_of(world, parts)
    found = found_on(world, parts)
    v = View(3000, 2180, MPP_FULL, 740, 760, 2)
    cv, reveal = render_new(v, world, fog)
    bare = cv.out().copy()
    img = bare.copy()
    D, _ = B.resample(parts[-2], 2.0)
    here = tuple(D[-1])
    map_overlays(img, v, world, found, (3070.0, 2330.0), here, 120.0)
    rv = down(reveal, v.ss)
    un = ~B.dilate(rv >= .02, 4)                      # unwalked and clear of the ink rim band (the rim is the designed border)
    dev = np.abs(bare - WASH).max(-1)
    REPORT['f_unwalked'] = dict(pixels=int(un.sum()), max_abs_dev_from_wash=round(float(dev[un].max()), 4), pixels_over_grain_0_03=int((dev[un] > .03).sum()),
                                walked_flat_vs_unwalked_measured=round(contrast(np.median(bare[rv > .98].reshape(-1, 3), axis=0), np.median(bare[un].reshape(-1, 3), axis=0)), 2),
                                markers_in_view=len([m for m in world.markers if v.near(np.array([m['xz']]), 0)]), markers_drawn=len(found))
    p = Pn(SW, 960)
    y = p.head('(바) 일부만 걸은 상태 — 지형 · 길 · 아이콘이 모두 걸은 땅의 끝에서 끝난다 (#214)',
               ('폐광 → 전망대 → 금표 주막 → 역참 → 벌목마을까지 걷고 두 길로 조금 나간 상태. 32 m 칸 · 반경 96 m(게임과 같은 규칙). 걷지 않은 칸 = 평평한 씻김 + 결뿐. 능선도 길도 숲도 그 위에는 한 화소도 없다.',
                '지도는 목표를 가리키지 않는다: 길 굵기는 폭 · 수레 통행뿐, 아이콘은 도착한 곳만, 권역 이름(사용자 요청 D269)만 미리 선다. 권역 경계 점선은 없앴다.'))
    p.t('펼친 지도 740×760 px (100%)', 0, y, 'S8', 24)
    p.put(img, 0, y + 36)
    cx_, cy_ = 300, 250
    crop = img[cy_:cy_ + 250, cx_:cx_ + 300]
    p.t('경계 300% — 붓 띠 · 톱니 · 숲 점이 먹 테에서 끊긴다', 790, y, 'S8', 24)
    p.put(zoom(crop, 3), 790, y + 36)
    p.a[y + 36 + cy_ - 1:y + 36 + cy_ + 251, cx_ - 1:cx_ + 1] = CIN
    p.a[y + 36 + cy_ - 1:y + 36 + cy_ + 251, cx_ + 299:cx_ + 301] = CIN
    p.a[y + 36 + cy_ - 1:y + 36 + cy_ + 1, cx_:cx_ + 300] = CIN
    p.a[y + 36 + cy_ + 249:y + 36 + cy_ + 251, cx_:cx_ + 300] = CIN
    # proof image: |pixel - wash| x 12 on the unwalked area (black = exactly the flat wash)
    proof = np.zeros(bare.shape)
    proof[un] = np.clip(dev[un] * 12, 0, 1)[:, None]
    proof[~un] = PAPER * .55
    p.t('검사: 걷지 않은 칸에서 씻김 값과의 차 ×12 (검정 = 차 없음)', 1730, y, 'S8', 24)
    p.put(proof, 1730, y + 36)
    v3 = View(2000, 3000, MPP_WORLD, 507, 760, 2)
    cv3, _ = render_new(v3, world, fog)
    im3 = cv3.out()
    map_overlays(im3, v3, world, found, None, here, 120.0, realm_names=True, left_pad=16)
    p.t('전체 보기 507×760 px', 2510, y, 'S8', 24)
    p.put(im3, 2510, y + 36)
    f = REPORT['f_unwalked']
    for i, line in enumerate(('걷지 않은 화소(먹 테 밖) %s개' % format(f['pixels'], ','), '씻김과의 최대 차 %.3f (결 진폭 0.03 안)' % f['max_abs_dev_from_wash'],
                              '결 진폭을 넘는 화소 %d개' % f['pixels_over_grain_0_03'], '걸은 땅(담묵 포함 중앙값) / 안 걸은 땅 %.2f:1 · 맨 종이 기준 2.46:1' % f['walked_flat_vs_unwalked_measured'],
                              '이 창 안 표식 %d개 중 그려진 것 %d개(도착한 곳만)' % (f['markers_in_view'], f['markers_drawn']),
                              '', '드러남 규칙', '· 지형 값 · 무늬 · 물가 선: 걸은 칸에서만', '· 붓 띠(길 · 능선 · 단애 · 성벽 · 다리 · 눈금): 걸은 칸 위의 조각만',
                              '· 아이콘 · 장소 이름: 도착 사실이 있는 곳만', '· 권역 이름 다섯: 늘(글자만, 경계선 없음)', '· 깨어날 곳 · 통보 · 내 표식: 플레이어의 기록')):
        p.t(line, 3060, y + 40 + i * 32, 'N7' if line == '드러남 규칙' else 'N4', 21)
    return p.img(), dict(fog=fog, found=found, map=img)


def panel_g(world, ctx):
    """(g) frame options in the D308-15 craft theme, for both maps."""
    sc = mini_scene(world)
    hud = hud_backdrop()
    p = Pn(SW, 1800)
    y = p.head('(사) 틀 — 나전칠기 · 백자 상감 · 창호 (D308-15). 지도 면은 종이와 먹 그대로, 틀만 공예 테마',
               ('미니맵 4안: ① 직사각 흑칠 틀 + 자개 끊음질 선 + 귀 꺾임 + 북쪽 자개 조각(Spec 권장)  ② 둥근 흑칠 원테(테마 키트 v0 도해의 안, 지름 232)  ③ 창호 문틀 + 아자살 교창  ④ 백자 테 + 흑상감 선.',
                '각 안: 실제 화면 오른쪽 아래(어두운 풀밭) · 밝은 하늘 위 100%, 귀퉁이 400%. 재질 · 자개 색 · 문살 구성은 테마 키트 라이브러리(theme308_lib)로 그렸다. 키트의 틀 스프라이트가 나오면 그것으로 바꾼다.'))
    sky = np.empty((300, 420, 3))
    sky[:] = hx('#E4E0D6')
    kinds = (('lacquer', '① 직사각 흑칠 + 자개 (권장)'), ('round', '② 둥근 흑칠 원테 (키트 v0)'), ('lattice', '③ 창호 문틀 + 교창'), ('porcelain', '④ 백자 테 + 흑상감'))
    areas = {}
    for i, (kind, name) in enumerate(kinds):
        x = i * 975
        patch, img = mini_new(world, *sc['here'], sc['heading'], sc['fog'], sc['found'], kind=kind)
        areas[kind] = int(img.shape[0] * img.shape[1] * (math.pi / 4 if kind == 'round' else 1))
        p.t(name, x, y, 'S8', 24, CIN if kind == 'lacquer' else INK)
        f = hud.copy()
        over(f, patch, 1740 - patch.shape[1] / 2, 900 - patch.shape[0] / 2)
        p.put(f[750:1050, 1530:1950 - 30], x, y + 36)
        s = sky.copy()
        over(s, patch, 210 - patch.shape[1] / 2, 150 - patch.shape[0] / 2)
        p.put(s[:, :400], x + 400, y + 36)
        c = f[900 - patch.shape[0] // 2 - 2:900 - patch.shape[0] // 2 + 38, 1740 - patch.shape[1] // 2 - 2:1740 - patch.shape[1] // 2 + 58]
        if kind == 'round':                              # the ring has no corner: show its head with the north petal
            c = f[900 - patch.shape[0] // 2 - 2:900 - patch.shape[0] // 2 + 38, 1740 - 30:1740 + 30]
        p.put(zoom(c, 4)[:, :150], x + 805, y + 36)
        p.t('지도 면적 %s px² (%d%%)' % (format(areas[kind], ','), round(areas[kind] / areas['lacquer'] * 100)), x, y + 36 + 306, 'N4', 20, ASH)
    REPORT['g_minimap_map_area_px2'] = areas
    # vessel cluster beside, same scale
    yv = y + 36 + 350
    p.t('먹 용기 군집(왼쪽 아래)과 ① 틀(오른쪽 아래)을 같은 화면에서 — 같은 흑칠 값 · 같은 자개 선 굵기', 0, yv, 'S8', 24)
    patch, _ = mini_new(world, *sc['here'], sc['heading'], sc['fog'], sc['found'], kind='lacquer')
    f = hud.copy()
    over(f, patch, 1740 - patch.shape[1] / 2, 900 - patch.shape[0] / 2)
    p.put(f[760:1060, 30:400], 0, yv + 36)
    p.put(f[760:1060, 1540:1910], 390, yv + 36)
    p.put(zoom(f[786:826, 1584:1684], 5), 800, yv + 36)
    p.put(zoom(f[776:816, 1712:1772], 5), 1320, yv + 36)
    p.t('귀 꺾임 500%', 800, yv + 36 + 206, 'N4', 20, ASH)
    p.t('북쪽 자개 조각 500%', 1320, yv + 36 + 206, 'N4', 20, ASH)
    for i, line in enumerate(('① 권장 이유: 9월 30일 "직사각 한지" 결정을 지키고 지도 면적을 그대로 둔다. 어두운 숲 위에서는 자개 선(흑칠 대비 %.1f:1)과 바깥 한지 실테가 틀을 떼어 준다.' % contrast(LACQUER, TH.nacre_token()),
                              '② 둥근 원테: 용기의 둥근 몸과 가장 잘 어울리지만 지도 면적이 %d%%로 줄고 길이 원 밖으로 금방 나간다(직사각 결정을 되돌림).' % round(areas['round'] / areas['lacquer'] * 100),
                              '③ 창호 문틀: 살대색(#4B4037)이 어두운 장면에서 묻히고 교창이 지도 높이 20 px를 먹는다(%d%%). 밝은 하늘에서는 가장 조용하다.' % round(areas['lattice'] / areas['lacquer'] * 100),
                              '④ 백자 테: 어두운 장면에서 화면에서 가장 밝은 물건이 된다(먹 용기보다 눈에 띈다). 밝은 하늘에서는 테가 사라진다.')):
        p.t(line, 1660, yv + 44 + i * 34, 'N4', 21)
    # full-map boards
    yb = yv + 36 + 300 + 60
    p.t('펼친 지도 3안 (0.75배): ① 한지를 받친 흑칠 판 + 자개 선 + 완자살 띠 (권장) · ② 창호 문틀 + 숫대살 띠 + 국화 자개 (키트 v0 도해의 안) · ③ 백자 판 + 흑상감 이중선 + 연판 띠', 0, yb, 'S8', 24)
    for i, (kind, name) in enumerate((('lacquer', '① 흑칠 판 (권장)'), ('door', '② 창호 문틀'), ('porcelain', '③ 백자 판'))):
        page = ctx['page'] if kind == 'lacquer' else page_new(world, ctx['map'], board=kind)
        crop = page[104:1040, 500:1420]
        sm = np.asarray(to_img(crop).resize((690, 702), Image.LANCZOS), float) / 255
        x = i * 1290
        p.t(name, x, yb + 40, 'S8', 22, CIN if kind == 'lacquer' else INK)
        p.put(sm, x, yb + 74)
        p.put(zoom(page[118:238, 534:654], 4), x + 710, yb + 74)
        p.t('왼쪽 위 귀 400%', x + 710, yb + 74 + 486, 'N4', 20, ASH)
        p.put(page[940:1030, 600:1100], x + 710, yb + 74 + 524)
        p.t('조작 줄 100%', x + 710, yb + 74 + 524 + 96, 'N4', 20, ASH)
    p.t('②는 종이의 찢긴 가장자리와 접기 연출이 사라진다(문에 바른 창호지). ③은 종이(#DFDBD0)와 백자(#DDE0DB)의 대비가 %.2f:1이라 그림자 선 없이는 종이가 판에서 안 떨어지고, 어두운 메뉴에서 836×910 px가 통째로 밝아진다.' % contrast(PAPER, PORCELAIN),
        0, yb + 74 + 702 + 16, 'N4', 21, ASH)
    return p.img(), {}


def panel_h():
    """(h) stage 1a: cliff mountains, the cliff-top field and the long wall with its gate."""
    w0, w1 = World.get('base'), World.get('1a')
    p = Pn(SW, 1600)
    y = p.head('(아) 절벽 1a와 장성이 지도에 그려지는 모양 (height_p1a.bytes · boundary308.json · 관문 성벽 선)',
               ('왼쪽 = 지금 높이장, 오른쪽 = 1a 높이장으로 다시 구운 것(권역 배율). 안쪽 단애 = 마루 선 + 낮은 쪽 빗금, 끝 연봉 = 가장 굵은 톱니 띠(그 너머는 빈 종이), 절벽 위 들 = 종이 + 풀 점, 장성 = 성가퀴 선 + 문 아이콘.',
                '권역 경계 점선은 없다 — 경계는 절벽 · 장성 · 물이 맡고, 그 선들도 걸은 칸에서만 보인다. 아래: 관문 앞과 금표 단애산 발치를 미니맵 배율로.'))
    va = dict(cx=2400, cz=2150, mpp=MPP_FULL, w=1280, h=660)
    for i, (wd, name) in enumerate(((w0, '지금 높이장 (절벽 없음)'), (w1, '1a 스테이지 — 적로 북단애 · 금표 단애산 · 강 단애 · 관문 장성'))):
        v = View(va['cx'], va['cz'], va['mpp'], va['w'], va['h'], 2)
        cv, _ = render_new(v, wd)
        img = cv.out()
        if wd.gate:
            px, py = v.to_px([wd.gate])[0] / v.ss
            over(img, icon('gate', 30), px - 15, py - 15)
        x = i * 1320
        p.t(name, x, y, 'S8', 24, CIN if i else INK)
        p.put(img, x, y + 36)
    ve = View(4000 - 500 * MPP_FULL / 2, 3250, MPP_FULL, 500, 660, 2)      # right edge = the world edge (x 4000)
    cv, _ = render_new(ve, w1)
    p.t('동쪽 끝 연봉 (1a)', 2640, y, 'S8', 24, CIN)
    p.put(cv.out()[:, :500], 2640, y + 36)
    y2 = y + 36 + 660 + 50
    # gate close-up
    vg = View(1930, 1740, MPP_MINI, 294, 210, 3)
    cv, _ = render_new(vg, w1)
    img = cv.out()
    px, py = vg.to_px([w1.gate])[0] / vg.ss
    over(img, icon('gate', 22), px - 11, py - 11)
    p.t('관문과 장성 — 미니맵 배율 300%', 0, y2, 'S8', 24)
    p.put(zoom(img, 3), 0, y2 + 36)
    # plateau foot: the recommended reveal rule vs today's radius rule
    ii, jj = np.nonzero(w1.top)
    tx, tz = jj * 4.0, ii * 4.0
    # stand at the walkable (EA-reachable) node closest to the plateau rim
    ea = w1.ea & ~w1.top
    ei, ej = np.nonzero(ea[380:760, 560:760])
    ex, ez = (ej + 560) * 4.0, (ei + 380) * 4.0
    best, bd = None, 1e9
    for k in range(0, len(ex), 7):
        d = np.hypot(tx - ex[k], tz - ez[k]).min()
        if d < bd:
            bd, best = d, (ex[k], ez[k])
    walk = np.array([(best[0] + 60, best[1] - 150), (best[0] + 20, best[1] - 60), best, (best[0] + 10, best[1] + 70), (best[0] + 40, best[1] + 150)])
    vp = View(best[0] - 40, best[1], MPP_MINI, 294, 210, 3)
    REPORT['h_plateau_foot'] = dict(stand=[float(best[0]), float(best[1])], metres_to_top_cell=round(float(bd), 1))
    for i, (reg, name) in enumerate(((True, '금표 단애산 발치 — 권장 규칙: 위 들은 올라가야 드러난다 300%'), (False, '같은 자리 — 지금 규칙(반경 96 m): 발치에서 위 들이 드러난다 300%'))):
        fog = Fog()
        fog.walk(walk, region=w1.top if reg else None)
        cv, _ = render_new(vp, w1, fog)
        img = cv.out()
        over(img, player_mark(28, 10.0), 147 - 14 + 40 / MPP_MINI, 105 - 14)
        x = 930 + i * 930
        p.t(name, x, y2, 'S8', 24, CIN if reg else INK)
        p.put(zoom(img, 3), x, y2 + 36)
    for i, line in enumerate(('나중의 map 단계가 넣어 줄 것', '· 스테이지 높이장(height_p1a.bytes)', '· boundary308.json segments(id · class · stage · polyline)', '· passes(능선 띠를 끊는 틈)',
                              '· 절벽 위 들 도달 영역(reach_p1a_top_*.npy)', '· 성벽 선(관문 footprint, 1b부터 jangseong308.json)', '· 씬에 실제로 적용된 스테이지',
                              '', '이 시안에서 쓴 것', '· 단애 %d구간 · 끝 연봉 %d구간 · 성벽 2줄' % (len(w1.cliffs), len(w1.outer)), '· 위 들 %d칸(4 m)' % int(w1.top.sum()))):
        p.t(line, 2800, y2 + 40 + i * 32, 'N7' if line in ('나중의 map 단계가 넣어 줄 것', '이 시안에서 쓴 것') else 'N4', 21)
    return p.img(), {}


# ---------------------------------------------------------------------------------------------------------------- sheet
PANEL_FILES = dict(a='a_full_today_vs_new.png', b='b_minimap_today_vs_new.png', c='c_terrain_three_scales.png', d='d_roads_abc.png',
                   e='e_icons.png', f='f_partly_explored.png', g='g_frames.png', h='h_cliff_1a_wall.png')


def build_sheet():
    ims = [Image.open(PANELS / PANEL_FILES[k]).convert('RGB') for k in 'abcdefgh']
    M_ = 40
    title_h = 190
    H = title_h + sum(im.size[1] + 70 for im in ims) + 40
    sheet = Image.new('RGB', (SW + 2 * M_, H), tuple(int(c * 255) for c in BG))
    a = np.asarray(sheet, float) / 255
    over(a, text_rgba('오행부 #308 지도 2종 전면 개선 — 시안 (길 · 지형 표기 · 아이콘 · 가독성 · 틀)', font('S9', 54), INK, 0), M_, 26)
    over(a, text_rgba('SPEC-MAP-OVERHAUL-308 · DECISIONS D308-14 / 14b / 15 · 2026-10-04 · TEST · numpy + PIL 오프라인 합성(결정적) — 게임 캡처가 아니다(왼쪽 "지금" 캡처 제외). 실제 높이장 · 길 · 숲 · 물 · 표식 자료로 그렸다.', font('N4', 23), ASH, 0), M_, 108)
    over(a, text_rgba('보실 것: ① 길 표기(라) ② 안 걸은 땅 농도(나 · 바) ③ 펼친 지도의 걷지 않은 땅을 완전히 가릴지(바) ④ 미니맵 틀 모양(사) ⑤ 절벽 위 들 드러남(아) ⑥ 들은 목적 권역 고리는 그대로 둠', font('N7', 23), INK, 0), M_, 144)
    sheet = to_img(a)
    y = title_h
    for im in ims:
        sheet.paste(im, (M_, y))
        y += im.size[1] + 70
    OUT.mkdir(parents=True, exist_ok=True)
    sheet.save(OUT / 'map308_mock_sheet.png', optimize=True)
    sheet.resize((sheet.size[0] // 2, sheet.size[1] // 2), Image.LANCZOS).save(OUT / 'map308_mock_sheet_half.jpg', quality=88)
    return sheet.size


def main():
    B.utf8()
    only = None
    if '--only' in sys.argv:
        only = set(sys.argv[sys.argv.index('--only') + 1].split(','))
    PANELS.mkdir(parents=True, exist_ok=True)
    world = World.get('base')
    ctx = {}
    rep_path = OUT / 'map308_mock_report.json'
    if rep_path.exists():
        REPORT.update(json.loads(rep_path.read_text(encoding='utf-8')))

    def run(k, fn, *args):
        if only and k not in only:
            return None
        im, extra = fn(*args)
        im.save(PANELS / PANEL_FILES[k], optimize=True)
        print('panel', k, im.size, flush=True)
        return extra
    extra = run('a', panel_a, world)
    if extra:
        ctx.update(extra)
    run('b', panel_b, world)
    run('c', panel_c, world)
    run('d', panel_d, world)
    run('e', panel_e, world)
    run('f', panel_f, world)
    if not only or 'g' in only:
        if 'page' not in ctx:
            v = View(A_CENTRE[0], A_CENTRE[1], MPP_FULL, 740, 760, 2)
            img = render_new(v, world)[0].out()
            map_overlays(img, v, world, {'village'}, A_WAKE, A_CENTRE, HEADING)
            ctx.update(page=page_new(world, img), map=img)
        run('g', panel_g, world, ctx)
    run('h', panel_h)
    REPORT.update(id='map308_mock', spec='SPEC-MAP-OVERHAUL-308', status='TEST (concept mock, numpy twin - not a game capture)',
                  inputs=dict(height_base_sha256=B.sha256(B.BASE_HEIGHT), height_1a_sha256=B.sha256(STAGE_1A), map_asset_sha256=B.sha256(map_asset()),
                              layout_sha256=B.sha256(LAYOUT), boundary_sha256=B.sha256(BOUNDARY)),
                  road_class_km=world.road_class_km(), ridge_km_by_rank=bundle().note['stats']['strokes']['ridges']['km_by_rank'],
                  notation='every new-notation picture is drawn from the bundle: ' + ML.rel(ML.OUT),
                  bundle_notation_sha256=ML.sha256(ML.OUT / 'map308_notation.json'),
                  widths_px_Z0_Z3={c['name']: c['widthPx'] for c in bundle().note['strokeClasses']})
    rep_path.write_bytes((json.dumps(REPORT, ensure_ascii=False, indent=1, default=B._js) + '\n').encode('utf-8'))
    if all((PANELS / f).exists() for f in PANEL_FILES.values()):
        print('sheet', build_sheet(), flush=True)


if __name__ == '__main__':
    main()
