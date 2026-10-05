#!/usr/bin/env python3
"""L8 study, part 2: option numbers on the true footprint + the plan picture. READ-ONLY (writes only under Art/Playtest308/Relayout/fix2).

  python Tools/Art/delivery_stop308_plan.py      (run delivery_stop308_study.py first: it writes the 1 m field and the node list)

House frame (scene YAML): pivot (1809.46, 102.843, 2307.96), yaw 311.2 -> local +x = world yaw 41.2 (toward the stop, uphill), local +z = yaw 311.2
(along the contour). Mesh boxes x 1.14 [O m_LocalAABB of the 15 VisualCorridor meshes]: eaves x -3.55..3.56, z -4.98..4.98, y 0..7.61;
platform top (part M2) x -3.19..2.84, z -4.54..4.52 at y +0.32; inner floor (part M7) x -1.67..1.40, z -2.77..1.86 at y +0.79; skirt y -2.77..+0.34.
"""
import json, math, sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/Playtest308/Relayout/fix2'
J = json.loads((OUT / 'delivery_stop_study.json').read_text(encoding='utf-8'))
F = np.load(OUT / 'delivery_stop_field_1m.npy'); X0, Z0 = J['field']['x0'], J['field']['z0']
PIV = (1809.46, 102.843, 2307.96); YAW = 311.2
FX = (math.cos(math.radians(YAW)), -math.sin(math.radians(YAW)))      # local +x in world XZ  (0.659, 0.752)
FZ = (math.sin(math.radians(YAW)), math.cos(math.radians(YAW)))       # local +z in world XZ  (-0.752, 0.659)
PLAT = (-3.19, 2.84, -4.54, 4.52); EAVE = (-3.55, 3.56, -4.98, 4.98); BODY = (-1.67, 1.40, -2.77, 1.86)
PLAT_TOP = 0.32; SKIRT_BOTTOM = -2.77; ROOF = 7.61
PAD_Y = 106.65


def h(x, z):
    fx = x - X0; fz = z - Z0; ix = int(fx); iz = int(fz); u = fx - ix; v = fz - iz
    a = F[iz, ix] + (F[iz, ix + 1] - F[iz, ix]) * u; b = F[iz + 1, ix] + (F[iz + 1, ix + 1] - F[iz + 1, ix]) * u
    return float(a + (b - a) * v)


def slope(x, z, d=2.0):
    return math.degrees(math.atan(math.hypot((h(x + d, z) - h(x - d, z)) / (2 * d), (h(x, z + d) - h(x, z - d)) / (2 * d))))


def w(lx, lz, t=0.0): return (PIV[0] + (lx + t) * FX[0] + lz * FZ[0], PIV[2] + (lx + t) * FX[1] + lz * FZ[1])
def rect(r, t=0.0): return [w(r[0], r[2], t), w(r[1], r[2], t), w(r[1], r[3], t), w(r[0], r[3], t)]
def samples(r, t=0.0, n=13): return [w(r[0] + (r[1] - r[0]) * i / (n - 1), r[2] + (r[3] - r[2]) * j / (n - 1), t) for i in range(n) for j in range(n)]
def loc(p): dx = p[0] - PIV[0]; dz = p[1] - PIV[2]; return (dx * FX[0] + dz * FX[1], dx * FZ[0] + dz * FZ[1])


def main():
    T = []; R = {}
    def say(s=''): T.append(s)
    nodes = J['nodes']
    say('FRAME local +x = yaw 41.2 (%.3f, %.3f), local +z = yaw 311.2 (%.3f, %.3f); oriented size: eaves %.2f x %.2f m, platform %.2f x %.2f m, house body %.2f x %.2f m, height %.2f m' % (FX[0], FX[1], FZ[0], FZ[1], EAVE[1] - EAVE[0], EAVE[3] - EAVE[2], PLAT[1] - PLAT[0], PLAT[3] - PLAT[2], BODY[1] - BODY[0], BODY[3] - BODY[2], ROOF))
    say('nodes in the house frame (forward toward the pad, lateral): ' + ', '.join('%s (%.1f, %.1f)' % (k, *loc((v['x'], v['z']))) for k, v in nodes.items() if k != 'Guesthouse252'))
    # the flat pad
    flat = [(x, z) for x in range(1800, 1861) for z in range(2296, 2351) if abs(h(x, z) - PAD_Y) <= 0.10 and slope(x, z) <= 2.0]
    R['pad'] = dict(level=PAD_Y, area_m2=len(flat), x=(min(p[0] for p in flat), max(p[0] for p in flat)), z=(min(p[1] for p in flat), max(p[1] for p in flat)))
    say('FLAT PAD at %.2f +- 0.10: %d m2, x %d-%d, z %d-%d (the pressed road end: in-road z 2316-2324, out-road band from (1823.8, 2323))' % (PAD_Y, len(flat), *R['pad']['x'], *R['pad']['z']))
    edge_fwd = min(loc(p)[0] for p in flat if abs(loc(p)[1]) <= 4.5)
    R['pad']['edge_fwd_m'] = round(edge_fwd, 2)
    say('pad edge on the house axis: %.2f m forward of the pivot; platform front edge today at %.2f -> %.2f m of %s slope between them' % (edge_fwd, PLAT[1], edge_fwd - PLAT[1], '14-18°'))
    strip = [h(*w(PLAT[1] + (edge_fwd - PLAT[1]) * i / 10, lz)) for i in range(11) for lz in (-4.5, -2.25, 0, 2.25, 4.5)]
    say('ground on that strip: %.2f .. %.2f' % (min(strip), max(strip)))

    def under(r, t, top):
        ys = [h(*p) for p in samples(r, t)]; c = [h(*p) for p in rect(r, t)]
        return dict(min=round(min(ys), 2), max=round(max(ys), 2), base_max=round(top - min(ys), 2), base_min=round(top - max(ys), 2), corners=[round(v, 2) for v in c])

    # today
    R['today'] = under(PLAT, 0.0, PIV[1] + PLAT_TOP)
    say(); say('TODAY platform top %.2f: ground under the platform %.2f .. %.2f -> base showing %.2f m (low side) / buried %.2f m (high side); corners rear-L, front-L, front-R, rear-R ground %s' % (PIV[1] + PLAT_TOP, R['today']['min'], R['today']['max'], R['today']['base_max'], -R['today']['base_min'], R['today']['corners']))
    # option B: raise in place
    say(); say('B raise in place so the platform top = pad level %.2f (+%.2f m)' % (PAD_Y, PAD_Y - PIV[1] - PLAT_TOP))
    R['B'] = dict(raise_m=round(PAD_Y - PIV[1] - PLAT_TOP, 2), **under(PLAT, 0.0, PAD_Y)); R['B']['skirt_bottom'] = round(PAD_Y - PLAT_TOP + SKIRT_BOTTOM, 2)
    front = [h(*w(PLAT[1], lz)) for lz in (-4.5, -2.25, 0, 2.25, 4.5)]
    R['B']['front_edge_ground'] = [round(v, 2) for v in front]; R['B']['gap_to_pad_m'] = round(edge_fwd - PLAT[1], 2)
    say('  base %.2f m at the rear low corner, %.2f m at the best corner; skirt bottom would be %.2f (ground min %.2f -> skirt short by %.2f m); front edge ground %s = %.2f .. %.2f m below the platform; gap to the pad %.2f m' % (R['B']['base_max'], R['B']['base_min'], R['B']['skirt_bottom'], R['B']['min'], R['B']['skirt_bottom'] - R['B']['min'], R['B']['front_edge_ground'], PAD_Y - max(front), PAD_Y - min(front), R['B']['gap_to_pad_m']))
    # option C1: slide to the pad edge + raise
    say(); say('C1 slide along yaw 41.2 to the pad edge + raise')
    R['C1'] = []
    for t in (9.5, 10.0, 10.3):
        u = under(PLAT, t, PAD_Y); piv = w(0, 0, t)
        fe = [h(*w(PLAT[1], lz, t)) for lz in (-4.5, -2.25, 0, 2.25, 4.5)]
        re = [h(*w(PLAT[0], lz, t)) for lz in (-4.5, -2.25, 0, 2.25, 4.5)]
        ev = under(EAVE, t, PAD_Y)
        inside = [k for k, v in nodes.items() if k != 'Guesthouse252' and EAVE[0] <= loc((v['x'], v['z']))[0] - t <= EAVE[1] and EAVE[2] <= loc((v['x'], v['z']))[1] <= EAVE[3]]
        park = loc((1820.0, 2320.0))[0] - t - PLAT[1]
        row = dict(slide_m=t, pivot=(round(piv[0], 2), round(PAD_Y - PLAT_TOP, 3), round(piv[1], 2)), raise_m=round(PAD_Y - PLAT_TOP - PIV[1], 2), under=u, front_edge_ground=[round(v, 2) for v in fe], rear_edge_ground=[round(v, 2) for v in re], skirt_bottom=round(PAD_Y - PLAT_TOP + SKIRT_BOTTOM, 2), nodes_inside=inside, parking_to_platform_m=round(park, 2),
                   earth1_m=round(math.hypot(piv[0] - 1868, piv[1] - 2354), 1), in_road_end_m=round(math.hypot(w(PLAT[1], 0, t)[0] - 1821.2, w(PLAT[1], 0, t)[1] - 2320.0), 2), eave_under=ev)
        R['C1'].append(row)
        say('  slide %.1f m: pivot (%.2f, %.3f, %.2f) (+%.2f m up); ground under the platform %.2f .. %.2f -> base %.2f m (rear low corner) .. %.2f; front edge ground %s (gap %.2f .. %.2f m below the top); rear edge ground %s; skirt bottom %.2f (%s the lowest ground %.2f by %.2f m); Parking is %.2f m in front of the platform edge; in-road end %.2f m from the edge centre; nodes inside the eaves box: %s; earth1 %.1f m' % (
            t, *row['pivot'], row['raise_m'], u['min'], u['max'], u['base_max'], u['base_min'], row['front_edge_ground'], PAD_Y - max(fe), PAD_Y - min(fe), row['rear_edge_ground'], row['skirt_bottom'], 'below' if row['skirt_bottom'] < u['min'] else 'ABOVE', u['min'], abs(u['min'] - row['skirt_bottom']), park, row['in_road_end_m'], ', '.join(inside) or 'none', row['earth1_m']))
    # option A: sunken yard at 103.16 between platform front and the pad edge
    say(); say('A sunken yard at the platform level between the house and the pad edge')
    R['A'] = []
    for name, lz0, lz1, x0, x1 in (('%.1f x 14 m (house front -> pad edge)' % (edge_fwd - PLAT[1]), -7.0, 7.0, PLAT[1], edge_fwd), ('14 x 16 m (as asked; 5.7 m of it is the road pad itself)', -7.0, 7.0, PLAT[1], PLAT[1] + 16.0)):
        lvl = PIV[1] + PLAT_TOP; n = 0; cut = 0.0; fill = 0.0; mc = 0.0; mf = 0.0; padcells = 0
        for i in range(int((x1 - x0) * 2) + 1):
            for j in range(int((lz1 - lz0) * 2) + 1):
                p = w(x0 + i * 0.5, lz0 + j * 0.5); d = h(*p) - lvl; n += 1
                if d > 0: cut += d * 0.25; mc = max(mc, d)
                else: fill += -d * 0.25; mf = max(mf, -d)
                if abs(h(*p) - PAD_Y) <= 0.1: padcells += 1
        side = [round(h(*w(x0 + (x1 - x0) * i / 4, s)) - lvl, 2) for s in (lz0, lz1) for i in range(5)]
        row = dict(name=name, level=round(lvl, 2), area=round((x1 - x0) * (lz1 - lz0)), cut_m3=round(cut), fill_m3=round(fill), max_cut=round(mc, 2), max_fill=round(mf, 2), side_wall_heights=side, pad_m2_taken=round(padcells * 0.25))
        R['A'].append(row)
        say('  %s at %.2f: cut %d m3 (max %.2f m), fill %d m3 (max %.2f m); retaining wall heights along the two side edges (house -> road) %s; flat road pad consumed %d m2' % (name, lvl, cut, mc, fill, mf, side, row['pad_m2_taken']))
    drop = PAD_Y - (PIV[1] + PLAT_TOP)
    R['A_ramp'] = dict(drop=round(drop, 2), len_6=round(drop / math.tan(math.radians(6)), 1), len_8=round(drop / math.tan(math.radians(8)), 1), len_10=round(drop / math.tan(math.radians(10)), 1))
    say('  road pad -> yard drop %.2f m: a vehicle ramp needs %.0f m at 6°, %.0f m at 8°, %.0f m at 10° (the yard itself is %.1f m deep)' % (0, 0, 0, 0, 0) if False else '  road pad -> yard drop %.2f m: a vehicle ramp needs %.0f m at 6°, %.0f m at 8°, %.0f m at 10° (the yard is only as deep as the house-to-pad gap)' % (drop, R['A_ramp']['len_6'], R['A_ramp']['len_8'], R['A_ramp']['len_10']))
    # option D
    say(); say('D built forecourt, no terrain edit')
    dk = [h(*w(PLAT[1] + (edge_fwd - PLAT[1]) * i / 20, lz)) for i in range(21) for lz in np.linspace(-4.5, 4.5, 10)]
    eave_y = PIV[1] + 3.4
    R['D'] = dict(deck_at_pad=dict(depth=round(edge_fwd - PLAT[1], 1), width=9.0, above_platform=round(drop, 2), max_above_ground=round(PAD_Y - min(dk), 2)), stair=dict(risers=math.ceil(drop / 0.165), run_m=round(edge_fwd - PLAT[1], 1), pitch_deg=round(math.degrees(math.atan(drop / (edge_fwd - PLAT[1]))), 1)), roof_top=round(PIV[1] + ROOF, 2), pad=PAD_Y)
    say('  a deck at pad level reaches the house %.2f m above its platform (roof top is %.2f, only %.2f m above the pad): a walkway at wall-top height; max %.2f m above the ground' % (drop, PIV[1] + ROOF, PIV[1] + ROOF - PAD_Y, PAD_Y - min(dk)))
    say('  a 9 m wide stone stair from the pad edge to the platform: drop %.2f m over %.1f m = %.1f° pitch, %d risers of 0.165 m' % (drop, edge_fwd - PLAT[1], R['D']['stair']['pitch_deg'], R['D']['stair']['risers']))
    # what is seen from the road: eye on the in-road
    say(); say('WHAT THE ROAD SEES (eye 1.6 m; the pad edge hides everything below the line eye -> pad edge)')
    R['see'] = []
    for name, eye in (('in-road 40 m back (1856, 2312)', (1856.0, 2312.0)), ('pad east end (1842, 2326)', (1842.0, 2326.0)), ('pad middle (1832, 2324)', (1832.0, 2324.0)), ('Parking (1820, 2320)', (1820.0, 2320.0))):
        ey = h(*eye) + 1.6
        for label, t, top in (('today', 0.0, PIV[1]), ('B', 0.0, PAD_Y - PLAT_TOP), ('C1 slide 10.0', 10.0, PAD_Y - PLAT_TOP)):
            c = w(0, 0, t); L = math.hypot(c[0] - eye[0], c[1] - eye[1]); n = max(2, int(L)); hid = -1e9
            for i in range(1, n):
                q = (eye[0] + (c[0] - eye[0]) * i / n, eye[1] + (c[1] - eye[1]) * i / n); g = h(*q)
                hid = max(hid, ey + (g - ey) * (L / (L * i / n)))      # height at the house of the ray grazing this ground point
            vis_from = max(hid, top); shown = max(0.0, top + ROOF - vis_from)
            R['see'].append(dict(eye=name, case=label, eye_y=round(ey, 2), hidden_below=round(hid, 2), house_y=(round(top, 2), round(top + ROOF, 2)), shown_m=round(shown, 2), shown_pct=round(100 * shown / ROOF)))
            say('  %-32s %-14s eye y %.2f, dist %.0f m: terrain hides the house below y %.2f; house spans %.2f .. %.2f -> %.2f m of %.2f m shows (%d %%)' % (name, label, ey, L, hid, top, top + ROOF, shown, ROOF, 100 * shown / ROOF))
    # C1b: the same move, the house turned to face the pad from its south rim (front = +Z world, yaw 270)
    pb = [(1822.5 - (PLAT[2] + (PLAT[3] - PLAT[2]) * j / 12), 2316.0 - PLAT[1] + PLAT[0] + (PLAT[1] - PLAT[0]) * i / 12) for i in range(13) for j in range(13)]
    yb = [h(*q) for q in pb]
    R['C1b'] = dict(yaw=270.0, pivot=(1822.5, round(PAD_Y - PLAT_TOP, 3), round(2316.0 - PLAT[1], 2)), platform_x=(round(1822.5 - PLAT[3], 2), round(1822.5 - PLAT[2], 2)), platform_z=(round(2316.0 - (PLAT[1] - PLAT[0]), 2), 2316.0), ground=(round(min(yb), 2), round(max(yb), 2)), base_max=round(PAD_Y - min(yb), 2), skirt_bottom=round(PAD_Y - PLAT_TOP + SKIRT_BOTTOM, 2), earth1_m=round(math.hypot(1822.5 - 1868, 2313.2 - 2354), 1), front_edge_ground=[round(h(x, 2316.0), 2) for x in (1818.0, 1820.25, 1822.5, 1824.75, 1827.0)])
    say(); say('C1b (variant) house turned to yaw 270, front edge on the south rim of the pad z 2316, platform x %.1f-%.1f z %.1f-%.1f: ground under it %.2f .. %.2f -> rear base %.2f m; front edge ground %s; skirt bottom %.2f; earth1 %.1f m' % (*R['C1b']['platform_x'], *R['C1b']['platform_z'], *R['C1b']['ground'], R['C1b']['base_max'], R['C1b']['front_edge_ground'], R['C1b']['skirt_bottom'], R['C1b']['earth1_m']))
    # the flat pocket people can stand on while the 54 m rule holds
    zone = [(x / 2, z / 2) for x in range(3620, 3700) for z in range(4620, 4670) if abs(h(x / 2, z / 2) - PAD_Y) <= 0.10 and slope(x / 2, z / 2) <= 3.0 and math.hypot(x / 2 - 1868, z / 2 - 2354) >= 54.0]
    R['node_zone'] = dict(area_m2=round(len(zone) * 0.25), x=(min(q[0] for q in zone), max(q[0] for q in zone)), z=(min(q[1] for q in zone), max(q[1] for q in zone)))
    say('flat ground (106.65 +- 0.10, slope <= 3 deg) at >= 54 m from earth 2: %d m2, x %.1f-%.1f, z %.1f-%.1f (all of it road bed or its 1 m shoulder)' % (R['node_zone']['area_m2'], *R['node_zone']['x'], *R['node_zone']['z']))
    (OUT / 'delivery_stop_options.json').write_text(json.dumps(R, ensure_ascii=False, indent=1), encoding='utf-8')
    (OUT / 'delivery_stop_options.txt').write_text('\n'.join(T), encoding='utf-8')
    print('\n'.join(T))
    draw(R, flat)


def draw(R, flat):
    import delivery_stop308_draw as DD
    DD.draw(sys.modules[__name__], R, flat)


if __name__ == '__main__':
    main()
