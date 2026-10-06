"""SPEC-MAP-OVERHAUL-308 spec-stage probe (read-only on the project): the numbers the Spec quotes.

    python Tools/resource_guard.py --wait
    python Tools/Art/map308_probe.py            -> Art/UI308/Map/probe/probe308.json

Measures, on the 4 m height lattice (base Finish297 and the #308 stage 1a height) and the main scene's data copies:
route length per PHYSICAL class (width / vehicle grade / foot only - never the story role), water and wet area, slope bands,
a ridge-line estimate (length per prominence class), cliff-face cells, forest cover from the vegetation sheets, bridge count,
and from those the stroke-mesh vertex estimate and the texture sizes of the asset contract. Writes nothing inside Oheangbu/.
"""
import json, math, re, sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B

ROOT = B.ROOT
OUT = ROOT / 'Art/UI308/Map/probe'
LAYOUT = B.A / 'Scenes/World/Main/WorldLayout_Main.asset'
STAGE = B.PL / 'Stage/height_p1a.bytes'
BOUNDARY = B.PL / 'boundary308.json'
CROSSINGS = B.RB / 'Watershed295/Generated/crossings.json'


def box(a, r):
    """separable box mean of radius r cells (edge padded)."""
    k = 2 * r + 1
    p = np.pad(a, ((0, 0), (r, r)), mode='edge'); c = np.cumsum(np.pad(p, ((0, 0), (1, 0))), axis=1); a = (c[:, k:] - c[:, :-k]) / k
    p = np.pad(a, ((r, r), (0, 0)), mode='edge'); c = np.cumsum(np.pad(p, ((1, 0), (0, 0))), axis=0); return (c[k:, :] - c[:-k, :]) / k


def routes_from_layout():
    t = LAYOUT.read_text(encoding='utf-8')
    i = t.find('\n  Routes:'); j = t.find('\n  Ridges:'); block = t[i:j]
    out = []
    for m in re.finditer(r'- Id: (.*)\n\s+From: .*\n\s+To: .*\n\s+Role: (\d+)\n\s+RequiredAbility: ?.*\n\s+OneWay: (\d)\n\s+GradeForVehicle: (\d)\n\s+Traversal: (\d)\n\s+Bends:((?:\n\s+- \{x: [-\d.e]+, y: [-\d.e]+\})*)\n\s+Width: ([\d.]+)', block):
        # groups: 1 id, 2 role, 3 one way, 4 vehicle grade, 5 traversal (1 = foot only), 6 bends block, 7 width
        pts = np.array([(float(a), float(b)) for a, b in re.findall(r'\{x: ([-\d.e]+), y: ([-\d.e]+)\}', m.group(6))], float)
        out.append(dict(id=m.group(1), role=int(m.group(2)), vehicle=int(m.group(4)), foot_only=int(m.group(5)), width=float(m.group(7)), bends=pts))
    return out


def phys_class(width, vehicle, foot_only):
    """the Spec's road classes: physical only."""
    if vehicle or width >= 6: return 'daero'      # 대로(가도)
    if width >= 3 and not foot_only: return 'gil'  # 길
    return 'soro'                                  # 소로(산길·샛길)


def poly_len(P):
    P = np.asarray(P, float)
    return float(np.hypot(*np.diff(P, axis=0).T).sum()) if len(P) > 1 else 0.0


def main():
    B.utf8()
    res = {'id': 'map308_probe', 'date': '2026-10-04', 'grid': '4 m lattice 1501 rows x 1001 cols'}
    h0 = B.load_height(); h1 = B.load_height(STAGE)
    res['height'] = dict(base_sha256=B.sha256(B.BASE_HEIGHT), stage_1a_sha256=B.sha256(STAGE), y_min=float(h1.min()), y_max=float(h1.max()),
                         changed_cells_1a=int((np.abs(h1 - h0) > .05).sum()))
    # ---- slope bands (stage 1a)
    gz, gx = np.gradient(h1, B.CELL); slope = np.degrees(np.arctan(np.hypot(gx, gz)))
    cells = slope.size
    res['slope_share'] = {k: round(float(((slope >= a) & (slope < b)).sum()) / cells, 4) for k, (a, b) in
                          dict(flat_lt8=(0, 8), gentle_8_22=(8, 22), steep_22_45=(22, 45), cliff_45_60=(45, 60), wall_ge60=(60, 91)).items()}
    res['cliff_face_cells_ge55deg'] = dict(base=int((np.degrees(np.arctan(np.hypot(*np.gradient(h0, B.CELL)))) >= 55).sum()), stage_1a=int((slope >= 55).sum()))
    # ---- water
    L = B.lattice(log=lambda *a: None)
    wet = B.wet(); water = L['water4'].astype(bool) if L['water4'].shape == h1.shape else None
    res['water'] = dict(wet_cells=int(wet.sum()), wet_ha=round(float(wet.sum()) * 16 / 1e4, 1), deep_water_cells=int(water.sum()) if water is not None else None)
    # ---- routes (lattice routes = what the game walks; layout routes carry the physical attributes)
    lay = routes_from_layout(); by = {r['id']: r for r in lay}
    cls_len = {'daero': 0.0, 'gil': 0.0, 'soro': 0.0}; cls_n = {'daero': 0, 'gil': 0, 'soro': 0}; unmatched = []
    total = 0.0
    for r in L['routes']:
        ln = poly_len(r['pts']); total += ln
        a = by.get(r['id'])
        c = phys_class(r['width'], a['vehicle'] if a else 0, a['foot_only'] if a else 0)
        if a is None: unmatched.append(r['id'])
        cls_len[c] += ln; cls_n[c] += 1
    res['routes'] = dict(lattice_routes=len(L['routes']), layout_routes=len(lay), total_km=round(total / 1000, 2),
                         by_class_km={k: round(v / 1000, 2) for k, v in cls_len.items()}, by_class_n=cls_n, not_in_layout=unmatched[:40],
                         layout_role_counts={str(k): sum(1 for r in lay if r['role'] == k) for k in range(4)})
    # ---- ridge estimate: cells that stand above the 100 m mean (topographic position) and are a local crest across one axis
    hs = box(h1, 2)
    tpi = hs - box(hs, 25)
    crest = np.zeros(hs.shape, bool)
    for di, dj in ((0, 1), (1, 0), (1, 1), (1, -1)):
        r = 6
        a = np.roll(hs, (r * di, r * dj), axis=(0, 1)); b = np.roll(hs, (-r * di, -r * dj), axis=(0, 1))
        crest |= (hs > a + 1.5) & (hs > b + 1.5)
    ridge = crest & (tpi > 6)
    # thin estimate: a crest band is ~ (2 r) cells wide across; length ~= cells * cell / band width
    def length_km(mask, band_cells=5.0): return round(float(mask.sum()) * B.CELL / band_cells / 1000, 1)
    res['ridges_estimate'] = dict(method='box-smoothed height, crest across one of 4 axes at +-24 m by >= 1.5 m, topographic position (100 m mean) > 6 m; length = cells x 4 m / 5',
                                  all_km=length_km(ridge), major_tpi_ge20_km=length_km(ridge & (tpi >= 20)), grand_tpi_ge40_km=length_km(ridge & (tpi >= 40)))
    # ---- forest cover from the sheets (trees per 32 m cell)
    S = B.sheets(log=lambda *a: None)
    pts = np.concatenate([v[:, [0, 2]] for v in S.values() if len(v)], axis=0)
    gx_ = np.clip((pts[:, 0] / 32).astype(int), 0, 124); gz_ = np.clip((pts[:, 1] / 32).astype(int), 0, 187)
    dens = np.zeros((188, 125), int); np.add.at(dens, (gz_, gx_), 1)
    res['forest'] = dict(sheet_instances={k: int(len(v)) for k, v in S.items()}, cells32_with_ge4=int((dens >= 4).sum()), cells32_total=int(dens.size),
                         share_ge4=round(float((dens >= 4).mean()), 3), note='sheet FixedPlacements hold trees AND rocks/props; the baker must filter by prototype category')
    # ---- cliff boundary lines (what the later map step feeds in)
    bd = json.loads(BOUNDARY.read_text(encoding='utf-8'))
    segs = [dict(id=s['id'], cls=s.get('class'), stage=s.get('stage'), km=round(poly_len(s['polyline']) / 1000, 3), points=len(s['polyline'])) for s in bd['segments'] if s.get('polyline')]
    res['cliff_lines'] = dict(version=bd['version'], segments=len(segs), stage_1a=[s for s in segs if s['stage'] == '1a'],
                              stage_1a_km=round(sum(s['km'] for s in segs if s['stage'] == '1a'), 2), all_km=round(sum(s['km'] for s in segs), 2))
    gw = bd.get('gate_wall', {})
    res['gate_wall_keys'] = {k: (list(v.keys())[:12] if isinstance(v, dict) else type(v).__name__) for k, v in gw.items() if k in ('gate', 'west', 'east')}
    # ---- bridges
    if CROSSINGS.exists():
        cr = json.loads(CROSSINGS.read_text(encoding='utf-8-sig')); key = 'Crossings' if 'Crossings' in cr else list(cr.keys())[0]
        res['crossings'] = dict(count=len(cr[key]), fields=sorted(cr[key][0].keys()) if cr[key] else [])
    # ---- stroke mesh estimate: one quad per STEP metres of centreline, 2 new vertices per step (+2 per strip start)
    step = dict(road=6.0, ridge=12.0, cliff=8.0, river=10.0)
    river_km = 0.0
    t = LAYOUT.read_text(encoding='utf-8'); k0 = t.find('\n  River:'); d0 = t.find('\n  Drainages:'); d1 = t.find('\n  Hydrology:')
    rp = np.array([(float(a), float(b)) for a, b in re.findall(r'\{x: ([-\d.e]+), y: ([-\d.e]+)\}', t[k0:d0])], float); river_km += poly_len(rp) / 1000
    for blk in t[d0:d1].split('- Id:')[1:]:
        dp = np.array([(float(a), float(b)) for a, b in re.findall(r'\{x: ([-\d.e]+), y: ([-\d.e]+)\}', blk)], float); river_km += poly_len(dp) / 1000
    res['rivers_km'] = round(river_km, 2)
    v_road = int(total / step['road']) * 2 + 2 * len(L['routes'])
    v_ridge = int(res['ridges_estimate']['all_km'] * 1000 / step['ridge']) * 2
    v_cliff = int(res['cliff_lines']['all_km'] * 1000 / step['cliff']) * 2
    v_river = int(river_km * 1000 / step['river']) * 2
    res['stroke_mesh_estimate'] = dict(step_m=step, vertices=dict(road=v_road, ridge=v_ridge, cliff=v_cliff, river=v_river, total=v_road + v_ridge + v_cliff + v_river),
                                       bytes_at_28_per_vertex=(v_road + v_ridge + v_cliff + v_river) * 28, index_bytes_u16_x6_per_quad=(v_road + v_ridge + v_cliff + v_river) // 2 * 6 * 2)
    # ---- on-screen scale table (page px at 1080)
    scales = {'minimap_outside_120m': 240.0 / 210.0, 'minimap_cave_28m': 56.0 / 210.0, 'map_current_view': 4000.0 * (740 / 1.5 / 1001) / 740, 'map_whole_world': 6000.0 / 760.0}
    res['metres_per_px_1080'] = {k: round(v, 3) for k, v in scales.items()}
    res['terrain_texel_px'] = {k: round(4.0 / v, 2) for k, v in scales.items()}
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'probe308.json').write_bytes((json.dumps(res, ensure_ascii=False, indent=1, default=B._js) + '\n').encode('utf-8'))
    print(json.dumps(res, ensure_ascii=False, indent=1, default=B._js))


if __name__ == '__main__':
    main()
