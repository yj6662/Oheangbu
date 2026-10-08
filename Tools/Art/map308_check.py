# -*- coding: utf-8 -*-
"""SPEC-MAP-OVERHAUL-308 track A - checks of the baked bundle (offline: no editor, no Play).

    python Tools/resource_guard.py --wait
    python Tools/Art/map308_check.py [--bundle <dir>] [--fast]      -> Tools/Unity/Stage308_map/map308_check.json, exit 1 on a failure

    AC-O1  determinism     the bundle is baked again in memory; every file's sha256 must equal the file on disk
    AC-O2  no objective    no input line is named MainPath / campaign; the bake on a layout copy whose story Role values are all
                           scrambled is byte-identical (the road class reads Width / GradeForVehicle / Traversal only)
    AC-O3  contract        names, sizes, channels, header values, CRC of section 7; atlas / icon channel rules; tileable pattern
    AC-O4  caps            points <= 24,000, strokes file <= 0.6 MB, no 3 x 3 bin block above 6,000 vertices
    AC-O5  road accuracy   every road stroke point within 1.0 m of the source line, every source vertex within 1.0 m of the stroke
    AC-O8  LDR             every picture is 8 bit; no frame art in the bundle (the theme kit owns the frames)
    AC-O10 copies agree    the Main and candidate layout copies carry the same Routes / River / Drainages / Realms blocks
    kit                    the six kit slots of section 5 resolve by name in the theme kit atlas (when the kit is staged)
(AC-O6 / O7 / O9 / O13 are map308_twin.py; AC-O11 / O12 are the runtime track.)
"""
import argparse, json, re, struct, sys, tempfile, zlib
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import map308_lib as M
import cartography308 as CG
import map308_art as ART

BUNDLE = ['map308_terrain.png', 'map308_pattern.png', 'map308_strokes.bytes', 'map308_stroke_atlas.png', 'map308_icons_L.png',
          'map308_icons_S.png', 'map308_reveal_regions.bytes', 'map308_notation.json']
PNG = {'map308_terrain.png': (1000, 1500), 'map308_pattern.png': (256, 256), 'map308_stroke_atlas.png': (1024, 512),
       'map308_icons_L.png': (512, 512), 'map308_icons_S.png': (256, 256)}
SPACING = {2: 12.0, 3: 8.0, 4: 8.0, 1: 10.0}


class Report:
    def __init__(self):
        self.rows = []

    def add(self, ac, name, ok, detail=''):
        self.rows.append(dict(ac=ac, check=name, ok=bool(ok), detail=detail))
        print(f"  {'ok  ' if ok else 'FAIL'} {ac:7s} {name}" + (f' - {detail}' if detail else ''))

    @property
    def failed(self):
        return [r for r in self.rows if not r['ok']]


def read_strokes(b):
    ver, x0, z0, x1, z1, binm, bx, bz, ns, npnt = struct.unpack('<I4ffHHII', b[4:40])
    st = np.frombuffer(b, dtype=[('cls', 'u1'), ('rank', 'u1'), ('state', '<u2'), ('first', '<u4'), ('count', '<u4'), ('hw', '<f4'),
                                 ('len', '<f4'), ('hash', '<u4')], count=ns, offset=40)
    o = 40 + 24 * ns
    pt = np.frombuffer(b, dtype=[('x', '<f4'), ('z', '<f4'), ('d', '<f4'), ('p', 'u1'), ('f', 'u1'), ('r', '<u2')], count=npnt, offset=o)
    o += 16 * npnt
    bins = np.frombuffer(b, dtype=[('first', '<u4'), ('count', '<u4')], count=bx * bz, offset=o); o += 8 * bx * bz
    nref = int(bins['count'].sum())
    refs = np.frombuffer(b, dtype=[('stroke', '<u4'), ('first', '<u4'), ('count', '<u4')], count=nref, offset=o); o += 12 * nref
    return dict(version=ver, bounds=(x0, z0, x1, z1), bin_m=binm, bins=(bx, bz), strokes=st, points=pt, bin_table=bins, refs=refs, end=o)


def scrambled_layout():
    """a temp copy of the Main layout whose Role values are all changed (0 -> 3, 1 -> 0, 2 -> 1, 3 -> 2)."""
    t = M.LAYOUT_MAIN.read_text(encoding='utf-8'); i = t.find('\n  Routes:'); j = t.find('\n  Ridges:')
    blk = re.sub(r'(\n\s+Role: )(\d+)', lambda m: m.group(1) + str((int(m.group(2)) + 3) % 4), t[i:j])
    n = len(re.findall(r'\n\s+Role: \d+', t[i:j]))
    d = Path(tempfile.mkdtemp(prefix='map308_role_')); p = d / 'WorldLayout_roles_scrambled.asset'
    p.write_bytes((t[:i] + blk + t[j:]).encode('utf-8'))
    return p, n


def main():
    M.utf8()
    ap = argparse.ArgumentParser(); ap.add_argument('--bundle', default=str(M.OUT)); ap.add_argument('--fast', action='store_true', help='skip the two re-bakes (AC-O1, AC-O2 byte test)')
    ap.add_argument('--report', default='', help='default: Tools/Unity/Stage308_map/map308_check.json for the stage folder bundle, <bundle>/map308_check.json otherwise')
    a = ap.parse_args(); d = Path(a.bundle); R = Report()
    if not a.report: a.report = str((M.STAGE_DIR if d.resolve() == M.OUT.resolve() else d) / 'map308_check.json')
    print(f'[map308_check] {M.rel(d)}')
    missing = [n for n in BUNDLE if not (d / n).exists()]
    R.add('AC-O3', 'bundle files 1-8 present', not missing, ', '.join(missing))
    if missing:
        M.write_text(a.report, M.dumps(dict(ok=False, rows=R.rows))); sys.exit(1)
    note = json.loads((d / 'map308_notation.json').read_text(encoding='utf-8')); stage = note['stage']
    raw = (d / 'map308_notation.json').read_bytes()
    R.add('AC-O3', 'notation: UTF-8, LF, no BOM, contract version 1', b'\r' not in raw and not raw.startswith(b'\xef\xbb\xbf') and note['version'] == 1)
    keys = ['version', 'stage', 'world', 'inputs', 'outputs', 'zoomBands', 'strokeClasses', 'terrain', 'values', 'glyphs', 'markerGlyphs', 'kindFallback',
            'sizes', 'states', 'legend', 'frames', 'counts']
    R.add('AC-O3', 'notation keys of section 7', all(k in note for k in keys), ', '.join(k for k in keys if k not in note))
    sc = note['strokeClasses']; need = ['class', 'name', 'atlasRow', 'tileMetres', 'widthPx', 'knockoutPx', 'inkAlpha', 'minRankByBand', 'teeth']
    R.add('AC-O3', 'strokeClasses: 11 classes in draw order with the contract fields', [c['class'] for c in sc] == list(range(1, 12)) and all(all(k in c for k in need) and len(c['widthPx']) == 4 for c in sc))
    for o in note['outputs']:
        p = d / o['file']
        R.add('AC-O3', f"outputs[] sha: {o['file']}", p.exists() and M.sha256(p) == o['sha256'] and p.stat().st_size == o['bytes'])
    stale = M.stale_inputs(note)
    R.add('inputs', 'every input still has the sha recorded in the notation (bundle not stale)', not stale,
          ' | '.join(f'{role}: {path} ({what})' for role, path, what in stale))
    # ---- pictures
    for name, (w, h) in PNG.items():
        im = Image.open(d / name)
        R.add('AC-O3', f'{name}: {w} x {h} RGBA 8 bit', im.size == (w, h) and im.mode == 'RGBA', f'{im.size} {im.mode}')
    pat = np.asarray(Image.open(d / 'map308_pattern.png'), float)
    seam = max(float(np.abs(pat[0] - pat[-1]).max()), float(np.abs(pat[:, 0] - pat[:, -1]).max())); inner = max(float(np.abs(np.diff(pat, axis=0)).max()), float(np.abs(np.diff(pat, axis=1)).max()))
    R.add('AC-O3', 'pattern tiles (wrap step no larger than the largest inner step)', seam <= inner, f'wrap {seam:.0f} / inner {inner:.0f} code values')
    at = np.asarray(Image.open(d / 'map308_stroke_atlas.png'))
    R.add('AC-O3', 'stroke atlas: G = B = 0, 2 clear texels at every row edge, U wrap', at[..., 1].max() == 0 and at[..., 2].max() == 0 and
          all(at[r * 64:r * 64 + 2].max() == 0 and at[r * 64 + 62:r * 64 + 64].max() == 0 for r in range(8)))
    R.add('AC-O3', 'stroke atlas: the paper underlay (R) only on the three road rows', all((at[r * 64:(r + 1) * 64, :, 0].max() > 0) == (r in (0, 1, 2)) for r in range(8)))
    dry = at[2 * 64:3 * 64, :, 3].astype(float) / 255.0
    R.add('AC-O6', 'stroke atlas: the dry row (footpath) keeps an unbroken core - no column of the middle 8 texels under .88 cover',
          float(dry[28:36].mean(axis=0).min()) >= .88, f'min {dry[28:36].mean(axis=0).min():.3f}, mean cover of the ink span {dry[12:52].mean():.3f}')
    road_rows = {c['class']: c['atlasRowByBand'] for c in note['strokeClasses'] if c['class'] in (7, 8, 9)}
    R.add('AC-O3', 'roads: every road row carries the paper underlay; the footpath uses the dry row at Z0 / Z1 only', all(r in (0, 1, 2) for v in road_rows.values() for r in v) and
          road_rows[7][:2] == [2, 2] and 2 not in road_rows[7][2:], str(road_rows))
    # #308 map fix 2: this row reads the NOTATION's crispEdge. Those numbers drive the shader only while MapFog308.cginc carries
    # the threshold formula (cover - (p.x + p.y x noise)). Under formula F (the cginc has `#define MAPFOG308_E_*`) the shader no
    # longer reads _M308Edge / _S308Edge, so crispEdge moves nothing: the numbers that keep the edge off an unwalked cell are
    # the cginc's own constants, checked by map308_bcheck.py (FROM > 0, FROM + SPAN < 1, FLOOR >= .809 x KCOVER) and measured by
    # map308_edgecheck.py. The row stays as the bundle contract of the notation value (and passes unchanged: the bundles keep it).
    E = note['values']['fogEdge']; ce = E.get('crispEdge', {})
    R.add('AC-O7', 'walked edge: a crisp edge that cannot reach an unwalked cell (threshold > 0, threshold + span < 1) with an ink rim inside it',
          0 < ce.get('thresholdFrom', 0) and ce.get('thresholdFrom', 0) + ce.get('thresholdSpan', 1) < 1 and E.get('over') == 'wash' and E['px'] >= 1 and 0 < E['inkAlpha'] <= 1,
          f"threshold {ce.get('thresholdFrom')} + {ce.get('thresholdSpan')} x noise, rim {E['px']} px a {E['inkAlpha']} over the wash = {E.get('colour')}")
    for s_ in ('L', 'S'):
        ic = np.asarray(Image.open(d / f'map308_icons_{s_}.png')); cell = 64 if s_ == 'L' else 32
        rule = np.array_equal(ic[..., 3], np.maximum(ic[..., 0], ic[..., 1]))
        used = [bool(ic[(i // 8) * cell:(i // 8 + 1) * cell, (i % 8) * cell:(i % 8 + 1) * cell, 3].max() > 0) for i in range(64)]
        R.add('AC-O3', f'icons {s_}: A = max(R, G), {len(note["glyphs"])} glyph cells drawn', rule and used == [i < len(note['glyphs']) for i in range(64)])
        second = [g['id'] for g in note['glyphs'] if ic[(g['cell'] // 8) * cell:(g['cell'] // 8 + 1) * cell, (g['cell'] % 8) * cell:(g['cell'] % 8 + 1) * cell, 2].max() > 0]
        inside = bool(np.all(ic[..., 2] <= ic[..., 0]))
        R.add('AC-O3', f'icons {s_}: the second ink (B) only in the cells of icons.secondInk, and inside the figure (B <= R)',
              second == list(note['icons']['secondInk']['glyphs']) and inside, f'B in {second}')
        rep = note['icons']['report'][s_]
        bad = [r['id'] for r in rep if r['plate_touches_cell_edge'] or r['stroke_survival'] < .85]
        R.add('AC-O6', f'icons {s_}: no plate leaves its cell, strokes >= {ART.ICON[s_]["min_stroke"]:g} px (opening survival >= .85)', not bad, ', '.join(bad))
        if s_ == 'S':
            cells = {g['id']: ic[(g['cell'] // 8) * 32:(g['cell'] // 8 + 1) * 32, (g['cell'] % 8) * 32:(g['cell'] % 8 + 1) * 32, 0] / 255.0 for g in note['glyphs']}
            thin = [k for k, c in cells.items() if ART.thin_parts(c >= .5, 2)[0] >= 3 or ART.thin_parts(ART.resize_channel(c, 22) >= .5, 2)[0] >= 3]
            R.add('AC-O6', 'icons S: no body part under 2 px at 32 px and at 22 px (no piece of 3 px or more is removed by a 2 x 2 opening)', not thin, ', '.join(thin))
            pr = ART.pair_correlation({k: v for k, v in cells.items() if k in ART.PLACE_GLYPHS}, 22, 0.0)
            R.add('AC-O6', 'icons S: no two place signs correlate above .70 at 22 px (Pearson, ink)', pr[0][0] <= .70, f'max {pr[0][0]:.3f} {pr[0][1]}-{pr[0][2]} of {len(pr)} pairs')
            used_ids = set(note['markerGlyphs'].values()) | set(note['kindFallback'].values())
            pr = ART.pair_correlation({k: v for k, v in cells.items() if k in ART.PLACE_GLYPHS and k in used_ids}, 22, 0.0, shift=1)
            R.add('AC-O6', 'icons S: the place signs in use stay under .70 at 22 px when one of a pair is shifted by up to 1 px', pr[0][0] <= .70,
                  f'max {pr[0][0]:.3f} {pr[0][1]}-{pr[0][2]} of {len(pr)} pairs')
            # the brush mark: figure = both inks (R holds the whole silhouette); slender on purpose - heading = the pointed tip in
            # front, the dark ferrule band and handle (second ink) at the back only
            pc = ic[:32, :32] / 255.0 if note['glyphs'][0]['id'] == 'player' else None
            fig = ART.resize_channel(pc[..., 0], 28) >= .5; b2 = ART.resize_channel(pc[..., 2], 28) >= .5; pl = fig | (ART.resize_channel(pc[..., 1], 28) >= .5)
            ys, xs = np.nonzero(fig); yp, xp = np.nonzero(pl); L = int(ys.max() - ys.min() + 1); Wd = int(xs.max() - xs.min() + 1)
            tip = int(fig[ys.min()].sum()); fw_ = fig.sum(axis=1); band = (b2.sum(axis=1) >= .8 * np.maximum(fw_, 1)) & (fw_ >= 4)   # rows the second ink crosses (not the 1-3 px tip, which is all edge)
            band_rows = np.nonzero(band)[0]; front = fig[ys.min():ys.min() + L // 2]; front2 = float((b2[ys.min():ys.min() + L // 2] & front).sum()) / max(1, int(front.sum()))
            R.add('AC-O6', 'current position (brush, 28 px): figure >= 14 x 23 px, with its plate >= 16 x 26, 1.4 <= length / width <= 1.9', Wd >= 14 and L >= 23 and
                  (xp.max() - xp.min() + 1) >= 16 and (yp.max() - yp.min() + 1) >= 26 and 1.4 <= L / Wd <= 1.9, f'{Wd} x {L} px, with plate {xp.max() - xp.min() + 1} x {yp.max() - yp.min() + 1}')
            R.add('AC-O6', 'current position: the heading reads - a tip of 1-3 px in front; the dark rows (second ink across >= 80 % of the figure) number >= 5 and all lie '
                  'in the back 45 % of the length; in the front half the second ink is only the thin edge (<= 45 % of the figure)',
                  tip <= 3 and len(band_rows) >= 5 and int(band_rows.min()) >= ys.min() + int(L * .55) and front2 <= .45,
                  f'tip row {tip} px, {len(band_rows)} dark rows from {(int(band_rows.min()) - int(ys.min())) / L:.2f} of the length, second ink in the front half {front2:.2f}')
    same = [g['id'] for g in note['glyphs']]
    R.add('AC-O3', 'glyph table: same cell number = same symbol in both sheets; every marker of Map.asset has a glyph',
          same == [g[0] for g in ART.GLYPHS] and not note['markersWithoutGlyph'] and all(v in same for v in note['markerGlyphs'].values()), ', '.join(note['markersWithoutGlyph']))
    reg = (d / 'map308_reveal_regions.bytes').read_bytes()
    R.add('AC-O3', 'reveal regions: 23,500 bytes (125 x 188)', len(reg) == 23500, str(len(reg)))
    if stage == 'base':
        R.add('AC-O3', 'reveal regions (base): only 0 and 255', set(reg) <= {0, 255}, str(sorted(set(reg))))
    elif note['regions']['cells'].get('2'):
        R.add('AC-O3', f'reveal regions ({stage}): closed regions 1 and 2 are present, nothing else but 0 and 255', set(reg) == {0, 1, 2, 255}, str(sorted(set(reg))))
    else:
        # a stage whose reach masks leave no cliff top that is reached only after the gate (1b: D308-9e closed the back slopes of the
        # 적로 plateaus, so those tops are 'no standable ground'): region 2 is absent by data, region 1 (the cliff-top field) must stay
        R.add('AC-O3', f'reveal regions ({stage}): closed region 1 is present, no after-the-gate top in the reach masks (region 2 absent), nothing else but 0 and 255',
              set(reg) == {0, 1, 255}, str(sorted(set(reg))))
        m = np.frombuffer(reg, np.uint8).reshape(M.FOG_H, M.FOG_W); n_ = int(M.FOG_CELL / M.CELL)
        carried = 'carried' in M.STAGES.get(stage, {})
        if carried:
            # #308 map bake: the reach masks are lost; the two rows below cannot be asked of the masks. What CAN be asked: the
            # bundle's regions are the carried grid byte for byte, the carried record is for this bundle's height, and every
            # carried cliff-top cells lie in and around the region 1 cells (reported - a cliff-top cell whose forest was already as
            # dense as the cliff-top grass left no trace in the picture and is not in the mask).
            c = M.STAGES[stage]['carried']; man = json.loads(c['manifest'].read_text(encoding='utf-8')); sha = {i['role']: i['sha256'] for i in note['inputs']}
            R.add('AC-O7', f'cliff-top field ({stage}): the regions are the carried grid byte for byte and the carried record is for the height of this bundle',
                  reg == c['regions'].read_bytes() and sha.get('height') == man['forHeightSha256'] and sha.get('carried.regions') == man['files']['regions']['sha256'],
                  f"carried from bundle {man['fromBundle']['notationSha256'][:12]} (baked {man['fromBundle']['bakedOn']}), height {man['forHeightSha256'][:12]}")
            tops = np.unpackbits(np.frombuffer(c['tops'].read_bytes(), np.uint8)).reshape(M.TH, M.TW).astype(bool)
            ti, tj = np.nonzero(tops); per = np.zeros((M.FOG_H, M.FOG_W), int)
            np.add.at(per, (np.clip(ti // n_, 0, M.FOG_H - 1), np.clip(tj // n_, 0, M.FOG_W - 1)), 1)
            empty = int(((m == 1) & (per == 0)).sum())
            R.add('AC-O7', f'cliff-top field ({stage}): region 1 is present and the carried cliff-top cells are not empty (reported, no limit: region 1 cells without a '
                  'carried cell, carried cells outside region 1 cells)', int((m == 1).sum()) > 0 and int(tops.sum()) > 0,
                  f'{int((m == 1).sum())} region 1 cells, {empty} without a cliff-top cell; {int(tops.sum())} cliff-top cells, {int(per[m != 1].sum())} of them outside region 1 cells')
        rs = {} if carried else {k: np.load(M.abs_of(i['path'])) for k, i in ((i['role'][6:], i) for i in note['inputs'] if i.get('role', '').startswith('reach.'))}
        if not carried:
            ii, jj = np.nonzero((rs['S0'] | rs['S2']) & ~rs['UP1']); foot_in_top = int((m[np.clip(ii // n_, 0, M.FOG_H - 1), np.clip(jj // n_, 0, M.FOG_W - 1)] == 1).sum())
            R.add('AC-O7', f'cliff-top field ({stage}): no node the player can stand on at the foot (S0 or S2 reach) lies in a region 1 cell, and no node is both '
                  'field and foot - standing at the foot never opens a region 1 cell (the rim of the field in mixed cells is the next row)',
                  foot_in_top == 0 and not bool((rs['UP1'] & (rs['S0'] | rs['S2'])).any()), f'{foot_in_top} foot nodes in the {int((m == 1).sum())} region 1 cells')
            ui, uj = np.nonzero(rs['UP1']); ucell = m[np.clip(ui // n_, 0, M.FOG_H - 1), np.clip(uj // n_, 0, M.FOG_W - 1)]; rim = ucell != 1
            rim_cells = len(set(zip((ui[rim] // n_).tolist(), (uj[rim] // n_).tolist())))
            # NOT a pass / fail limit: how much of the field shares a 32 m cell with foot ground (rule 'a mixed cell stays 0') and is
            # therefore revealed with it. A limit, or a finer region grid, is a design decision (D308-14c) - the row only fails when
            # the field has no region 1 node at all
            R.add('AC-O7', f'cliff-top field ({stage}): nodes of the field that lie OUTSIDE region 1 cells (mixed rim cells: revealed together with the foot ground of '
                  'the same cell) - reported, no limit set', int((~rim).sum()) > 0,
                  f'{int(rim.sum())} of {len(ui)} field nodes ({100.0 * rim.sum() / max(1, len(ui)):.1f} %), {rim_cells} cells, {rim.sum() * M.CELL * M.CELL / 1e4:.2f} ha; '
                  f'{int((~rim).sum())} nodes in region 1 cells')
    if stage != 'base':
        R.add('AC-O3', f'cliffs ({stage}): the built cliff segments are baked as classes 3 / 4', sum(1 for s_ in read_strokes((d / 'map308_strokes.bytes').read_bytes())['strokes'] if int(s_['cls']) in (3, 4)) > 0)
    # ---- strokes
    b = (d / 'map308_strokes.bytes').read_bytes()
    crc = zlib.crc32(b[:-4]) & 0xFFFFFFFF == struct.unpack('<I', b[-4:])[0]
    S = read_strokes(b); st, pt = S['strokes'], S['points']
    R.add('AC-O3', "strokes: 'MS08' v1, bounds (0,0)-(4000,6000), 250 m bins 16 x 24, CRC32", b[:4] == b'MS08' and S['version'] == 1 and S['bounds'] == (0, 0, 4000, 6000) and
          S['bin_m'] == 250 and S['bins'] == (16, 24) and crc and S['end'] + 4 == len(b))
    ranks = sorted({int(s['rank']) for s in st if int(s['cls']) == 2}); listed = sorted(r['rank'] for r in sc[1].get('ranks', []))
    R.add('AC-O3', 'ridges: ranks 1..4 only, every rank has a style in strokeClasses, other classes rank 0', set(ranks) <= {1, 2, 3, 4} and set(ranks) <= set(listed) and
          all(int(s['rank']) == 0 for s in st if int(s['cls']) != 2), f'ranks {ranks}, styles {listed}')
    order = [(int(s['cls']), int(s['rank'])) for s in st]
    R.add('AC-O3', 'strokes: table sorted by class then rank; firstPoint / pointCount tile the point block', order == sorted(order) and
          int(st['first'][0]) == 0 and bool(np.all(st['first'][1:] == st['first'][:-1] + st['count'][:-1])) and int(st['first'][-1] + st['count'][-1]) == len(pt))
    maxseg = 0.0; bad_sp = []
    for s in st:
        P = np.stack([pt['x'][s['first']:s['first'] + s['count']], pt['z'][s['first']:s['first'] + s['count']]], 1).astype(float)
        seg = float(M.seg_len(P).max()); maxseg = max(maxseg, seg)
        if int(s['cls']) in SPACING and seg > SPACING[int(s['cls'])] + .05: bad_sp.append((int(s['cls']), round(seg, 1)))
        dd = pt['d'][s['first']:s['first'] + s['count']]
        if abs(float(dd[-1]) - float(s['len'])) > .05 or float(dd[0]) != 0: bad_sp.append(('dist', int(s['hash'])))
    R.add('AC-O3', 'strokes: no segment above 48 m; ridge <= 12 m, cliff <= 8 m, water course <= 10 m; dist runs 0..lengthM', maxseg <= 48.01 and not bad_sp, f'max {maxseg:.1f} m {bad_sp[:4]}')
    ok_ref = all(int(r['first']) >= int(st['first'][r['stroke']]) and int(r['first'] + r['count']) <= int(st['first'][r['stroke']] + st['count'][r['stroke']]) for r in S['refs'])
    cover = np.zeros(len(pt), bool)
    for r in S['refs']: cover[int(r['first']):int(r['first'] + r['count'])] = True
    R.add('AC-O3', 'strokes: bin references stay inside their stroke and cover every point', ok_ref and bool(cover.all()))
    c = note['counts']
    R.add('AC-O4', 'caps: points <= 24,000, file <= 600,000 B, 3 x 3 bins <= 6,000 vertices', len(pt) <= 24000 and len(b) <= 600000 and c['maxVertices9Bins'] <= 6000,
          f"{len(pt)} points, {len(b)} B, {c['maxVertices9Bins']} vertices at bin {c['maxVertices9BinsAt']}")
    R.add('AC-O3', 'states: no baked stroke is state-gated (states[] empty, every state = 0)', note['states'] == [] and int(st['state'].max()) == 0)
    if 'long_wall' in M.STAGES.get(stage, {}):
        # the long wall of the stage must really be in the bundle (a silently dropped long_wall input would pass every other row)
        lw = M.STAGES[stage]['long_wall']; J = json.loads(Path(lw).read_text(encoding='utf-8')); gate = J['gate']
        want = {M.fnv1a32('wall.jangseong.' + w): w for w in ('west', 'east')}; got = {}
        for s in st:
            if int(s['hash']) in want:
                got.setdefault(want[int(s['hash'])], []).append((int(s['cls']), np.stack([pt['x'][s['first']:s['first'] + s['count']], pt['z'][s['first']:s['first'] + s['count']]], 1).astype(float)))
        two = sorted(got) == ['east', 'west'] and all(len(v) == 1 and v[0][0] == 5 for v in got.values())
        R.add('AC-O3', f'long wall ({stage}): the two wings are baked - one class 5 stroke each with the hash of wall.jangseong.west / .east; the layout is in inputs[]',
              two and any(i['path'] == M.rel(lw) for i in note['inputs']), ', '.join(f'{w}: {[c for c, _ in v]}' for w, v in sorted(got.items())) or 'no stroke with either hash')
        if two:
            off = max(max(float(M.dist_to_polyline(got[w][0][1], np.asarray(J['wings'][w]['built'], float)).max()),
                          float(M.dist_to_polyline(np.asarray(J['wings'][w]['built'], float), got[w][0][1]).max())) for w in ('west', 'east'))
            R.add('AC-O5', f'long wall ({stage}): every stroke point within 1.0 m of the as-built joint line (wings.*.built), every joint within 1.0 m of the stroke', off <= 1.0,
                  f"largest {off:.3f} m; west {M.poly_len(got['west'][0][1]):.1f} m, east {M.poly_len(got['east'][0][1]):.1f} m")
            c = np.asarray(gate['centre'], float); e = {w: got[w][0][1][int(np.argmin(np.hypot(*(got[w][0][1] - c).T)))] for w in ('west', 'east')}
            gap = float(np.hypot(*(e['west'] - e['east']))); block = float(gate['blockS1']) - float(gate['blockS0'])
            mid = float(np.hypot(*((e['west'] + e['east']) / 2 - c)))
            R.add('AC-O3', f'long wall ({stage}): the wall line is open at the gate - the two wing ends stand one gate block apart (+- 0.5 m) with the gate centre between them (within 1 m of the midpoint)',
                  abs(gap - block) <= .5 and mid <= 1.0, f'gap {gap:.2f} m, gate block {block:.2f} m, midpoint off the gate centre {mid:.2f} m')
    if any(i.get('role', '').startswith('requires.') for i in note['inputs']):
        pending, changed = M.requires_status(note)
        R.add('inputs', f'build order ({stage}): every requires.* ledger is as recorded at the bake (ok while PENDING = baked ahead of the build: the editor import refuses this '
              'bundle until the ledgers are applied and the bundle is baked again)', not changed,
              ('BAKE AGAIN: ' + ' | '.join(changed)) if changed else ('PENDING, not importable yet: ' + ' | '.join(pending)) if pending else M.requires_text(note))
    # ---- AC-O5 road accuracy + AC-O2 names
    mp = M.parse_map(); lay = M.parse_layout()
    # #308 map bake: the source of a road is the line the bake drew it from (cartography308.road_sources: the map line, or the
    # layout route where map308_road_fit.json says the map line is not the ground road)
    srcs = CG.road_sources(mp, lay)
    by_hash = {M.fnv1a32(r['id']): r for r in srcs}
    worst_pt = worst_src = 0.0; n_roads = 0; cls_bad = []; got = set()
    for s in st:
        if int(s['cls']) not in (7, 8, 9): continue
        src = by_hash.get(int(s['hash'])); n_roads += 1
        if src is None: cls_bad.append(int(s['hash'])); continue
        got.add(src['id'])
        P = np.stack([pt['x'][s['first']:s['first'] + s['count']], pt['z'][s['first']:s['first'] + s['count']]], 1).astype(float)
        Q = M.dedupe(src['src'])
        worst_pt = max(worst_pt, float(M.dist_to_polyline(P, Q).max())); worst_src = max(worst_src, float(M.dist_to_polyline(Q, P).max()))
        if src['cls'] != int(s['cls']): cls_bad.append(src['id'])
    R.add('AC-O5', 'road accuracy: every stroke point within 1.0 m of the source line, every source vertex within 1.0 m of the stroke', worst_pt <= 1.0 and worst_src <= 1.0 and n_roads > 0,
          f'{n_roads} roads, stroke point off the source {worst_pt:.3f} m, source vertex off the stroke {worst_src:.3f} m')
    R.add('AC-O2', 'road class = physical attributes of the layout route with the same id (else foot path)', not cls_bad, str(cls_bad[:4]))
    fitted = [r for r in srcs if r['source'] != 'map']; lines = M.layout_route_lines(); worst_fit = 0.0
    for r in fitted:
        G = lines[M.road_fit()['lines'].get(r['id'], {}).get('route', r['id'])]
        worst_fit = max(worst_fit, float(M.dist_to_polyline(M.resample(r['src'], 4.0), G).max()), float(M.dist_to_polyline(M.resample(G, 4.0), r['src']).max()))
    R.add('AC-O5', 'road fit: every road the fit names is a stroke, drawn on the layout route line (0 m), and inputs[] carries the fit data',
          {r['id'] for r in srcs} == got and worst_fit <= 1e-6 and any(i.get('role') == 'roadFit' for i in note['inputs']),
          f"{len(fitted)} fitted: {', '.join(r['id'] + ' <- ' + r['source'] for r in fitted) or 'none'}; off the route {worst_fit:.3f} m; "
          f"not drawn: {', '.join(k for k, v in M.road_fit()['lines'].items() if v['source'] == 'none') or 'none'}")
    names = [l['id'] for l in mp['lines'] if CG.FORBIDDEN_LINE.search(l['id'])]
    R.add('AC-O2', 'no input line named MainPath / campaign', not names, ', '.join(names))
    cg_src = Path(CG.__file__).read_text(encoding='utf-8')
    R.add('AC-O2', "the baker never asks for a route's Role (map308_lib.parse_layout(read_role=True) is never called)", 'read_role' not in cg_src)
    # ---- AC-O10
    sa, sb = M.layout_block_shas(M.LAYOUT_MAIN), M.layout_block_shas(M.LAYOUT_CAND); roles = {i.get('role') for i in note['inputs']}
    R.add('AC-O10', 'Main and candidate layout copies: Routes / River / Drainages / Realms blocks identical, both in inputs[]', sa == sb and {'layoutMain', 'layoutCandidate'} <= roles,
          'Routes ' + sa['Routes'][:12])
    # ---- AC-O8
    R.add('AC-O8', 'LDR: every picture 8 bit; no frame picture in the bundle (frames = theme kit)', not list(d.glob('map308_frame*')) and not list(d.glob('*.exr')) and not list(d.glob('*.hdr')))
    kj = M.THEME_DIR / 'theme308_atlas.json'
    if kj.exists():
        kit = {c['kit']: c for c in json.loads(kj.read_text(encoding='utf-8'))['cells']}; slots = note['frames']['slots']
        names = {c['name'] for c in kit.values()}
        miss = [k for k, v in slots.items() if k not in kit or v['kitCell'] not in names]
        R.add('kit', 'the six frame slots resolve by name in the staged theme kit atlas', not miss and len(slots) == 6, ', '.join(miss))
        # the kit's rectangle rules (its json: borders + piece length): a tiled frame must end every side on a whole piece
        fr = note['frames']; bad = []
        for slot, (w, h) in (('Frame.Mini', fr['minimap']['outer']), ('Frame.MapBoard', fr['board']['size'])):
            c = kit[slot]; l, b_, r_, t_ = c['border_px']; pc = c.get('piece_px'); want = slots[slot].get('rule')
            if pc is None or want is None or want['base'] != [l + r_, b_ + t_] or want['piece'] != pc: bad.append(f"{slot}: the notation's rule {want} is not the kit's ({[l + r_, b_ + t_]} + {pc} n)"); continue
            if (w - l - r_) % pc or (h - b_ - t_) % pc or w < l + r_ or h < b_ + t_: bad.append(f'{slot}: {w} x {h} does not end on a whole piece')
        n_ = kit['Piece.North']['design_px']
        if list(fr['minimap'].get('northCell', [])) != [int(n_[0]), int(n_[1])]: bad.append(f"Piece.North: the notation's cell {fr['minimap'].get('northCell')} is not the kit's {n_}")
        if fr['minimap']['northCentreFromTop'] != fr['minimap']['northCellBottomInsideFrame'] - n_[1] / 2: bad.append('Piece.North: centre is not (cell bottom 7 px inside the frame) - half the cell')
        cr = fr['board']['controlRow']; band_top = fr['board']['rect'][3] - fr['board']['bandPx']
        if not (fr['board']['sheetAt'][1] + fr['board']['sheet'][1] <= cr[0] and cr[1] <= band_top + 3 and fr['board']['size'] == [836, 910]): bad.append('board: the control row is not on plain lacquer between the sheet and the bottom band')
        R.add('kit', "the frames keep the kit's rectangle rules: minimap 312 x 228 = 48 + 12 n, board 836 x 910 = (80 + 9 n) x (82 + 9 m), north piece 16 x 18, control row clear of the band",
              not bad, ' | '.join(bad))
    else:
        R.add('kit', 'theme kit atlas staged', False, 'Tools/Unity/Stage308_theme/.../theme308_atlas.json missing: the frames cannot be bound')
    # ---- AC-O1 / AC-O2 re-bakes
    if not a.fast:
        files, meta, _ = CG.compute(stage, log=lambda *x: None)
        diff = [n for n, v in files.items() if M.sha256_bytes(v) != M.sha256(d / n)]
        R.add('AC-O1', 'determinism: a second bake gives the same sha256 for files 1-7', not diff, ', '.join(diff))
        outs = [dict(file=n, sha256=M.sha256_bytes(files[n]), bytes=len(files[n])) for n in sorted(files)]
        nb = M.dumps(CG.notation(meta['stage'], meta['inputs'], outs, meta['counts'], meta['stats'], meta['markers'], meta['icon_rep'])).encode('utf-8')
        R.add('AC-O1', 'determinism: the notation (file 8) is byte-identical too', nb == raw)
        lp, nroles = scrambled_layout()
        f2, _, _ = CG.compute(stage, layout_path=lp, log=lambda *x: None)
        diff = [n for n, v in f2.items() if M.sha256_bytes(v) != M.sha256(d / n)]
        R.add('AC-O2', f'bake on a layout copy with all {nroles} Role values changed is byte-identical', not diff and nroles > 0, ', '.join(diff))
        try:
            lp.unlink(); lp.parent.rmdir()
        except OSError:
            pass
    out = dict(id='map308_check', bundle=M.rel(d), stage=stage, ok=not R.failed, failed=len(R.failed), rows=R.rows)
    M.write_text(a.report, M.dumps(out))
    print(f"[map308_check] {len(R.rows) - len(R.failed)} ok, {len(R.failed)} failed -> {M.rel(a.report)}")
    sys.exit(1 if R.failed else 0)


if __name__ == '__main__':
    main()
