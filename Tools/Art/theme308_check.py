# -*- coding: utf-8 -*-
"""SPEC-UI-THEME-308 (D308-15) - the offline [T] checks of the kit (no editor, no Play). Read-only on the stage.

  python Tools/resource_guard.py --wait
  python Tools/Art/theme308_check.py            prints one line per AC and writes Art/UI308/Theme/theme308_check.json
                                                exit 1 when any check that could be measured fails

What it can and cannot say: it measures the staged files and the generators' own numbers. It does not import anything into
Unity, so every [E] / [P] item of the Spec stays unmeasured, and AC-T13 / T14 (C#) have nothing to look at until the theme
SO and the editor command exist. The HUD numbers (collar clearance, pictogram areas) need Tools/Art/hud308_mock.py; when
that module cannot be imported they are reported as not measured, never as met.
"""
import glob
import json
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import theme308_lib as L        # noqa: E402
import theme308_tiles as TL     # noqa: E402
import theme308_assets as AS    # noqa: E402
from theme308_kit import Kit    # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
STAGE = os.path.join(ROOT, 'Tools', 'Unity', 'Stage308_theme')
OUT = os.path.join(ROOT, 'Art', 'UI308', 'Theme')
T = L.T
SCREEN = 1920 * 1080.0
RES = []


def say(ac, ok, text, **num):
    """ok: True / False / None (not measured)"""
    RES.append(dict(ac=ac, result='OK' if ok else 'FAIL' if ok is False else 'NOT MEASURED', text=text, **num))
    print('%-7s %-12s %s' % (ac, RES[-1]['result'], text))


NACRE_CELLS = ('frame_mini', 'frame_board', 'line_najeon', 'line_najeon_thin', 'corner_fret', 'piece_north', 'frame_select',
               'pip_petal', 'motif_chrys', 'motif_plum', 'border_fret', 'border_vine', 'motif_gwigap')
SPEC_SLICES = dict(frame_mini=24, frame_board=40, plate_lacquer=8, plaque_porcelain=16, plaque_porcelain_single=16,
                   keycap_porcelain=12, frame_select=12)
SPEC_CELLS = ('frame_mini', 'frame_board', 'line_najeon', 'line_najeon_thin', 'corner_fret', 'piece_north', 'plate_lacquer',
              'plate_round', 'plaque_porcelain', 'plaque_porcelain_single', 'keycap_porcelain', 'frame_select', 'pip_petal',
              'white', 'motif_chrys', 'motif_plum', 'border_fret', 'border_vine', 'motif_gwigap', 'sanggam_lotus',
              'sanggam_stamps', 'sanggam_vine')
MAP_SLOTS = ('Frame.Mini', 'Frame.MapBoard', 'Inlay.Line', 'Corner.Fret', 'Piece.North', 'Plate.Icon')
SPEC_PAIRS = (12.02, 6.17, 10.45, 5.37, 13.33, 5.22, 12.09, 4.73, 13.83, 5.41)
SPEC_AT_CAP = (10.45, 5.37, 9.08, 4.67, 11.59, 4.53, 10.52, 4.12, 12.03, 4.71)


def nacre_pixels(tex):
    """core shell pixels of a baked najeon cell: opaque enough and far brighter than lacquer / wood"""
    a = tex[..., 3]
    lum = L.lum(tex[..., :3])
    return (a >= .98) & (lum > .36)


def grey_at_ratio(ground, toward, ratio):
    lo, hi = 0.0, 1.0
    for _ in range(40):
        mid = (lo + hi) / 2
        if L.contrast(L.over(toward, ground, mid), ground) < ratio:
            lo = mid
        else:
            hi = mid
    return L.over(toward, ground, (lo + hi) / 2)


def main():
    # ---- T1 determinism ------------------------------------------------------------------------------------------------
    t1, _ = TL.build()
    t2, _ = TL.build()
    a1, meta, _ = AS.build()
    a2, _, _ = AS.build()
    files = dict(t1, **a1)
    bad = [k for k in t1 if t1[k] != t2[k]] + [k for k in a1 if a1[k] != a2[k]]
    for k, v in files.items():
        p = os.path.join(TL.OUT, k.replace('/', os.sep))
        if not os.path.exists(p) or open(p, 'rb').read() != v:
            bad.append(k)
    say('AC-T1', not bad, 'two builds and the disk agree on %d kit files' % len(files) if not bad else 'different: ' + ', '.join(sorted(set(bad))),
        files=len(files), atlas_sha256=meta['sha256'])
    kit = Kit()

    # ---- T2 lattice tiles ----------------------------------------------------------------------------------------------
    rows, ok = {}, True
    for n in L.LATTICE_ORDER:
        t = kit.tiles[n]
        q = np.round(t * 255)
        crisp = float(((q == 0) | (q == 255)).mean())
        seam = L.seam_error(t)
        rows[n] = dict(size=[t.shape[1], t.shape[0]], seam=round(seam, 6), crisp=round(crisp, 4), bar_px=L.LATTICE[n]['bar'])
        ok &= seam == 0 and L.LATTICE[n]['bar'] >= 1.5 and (crisp == 1.0 or n not in L.LATTICE_EDGE)
    say('AC-T2', ok and len(rows) == 8, '8 families, seam error 0, the six axis-aligned families fully crisp, every bar >= 1.5 px', tiles=rows)

    # ---- T2b the same question one mip down (review 2026-10-04) --------------------------------------------------------
    # At 1080p a design px is one screen px and the 2x kit is read from mip 1. A straight line that is "crisp" in the texture
    # but starts on an odd texel is smeared over two half-covered pixels there. Checked: the lattice families with 2 px
    # bars, the inlaid lines of the porcelain plaque and the key cap, the 1 px bars of the map board's band.
    def mip1(t):
        h, w = t.shape[0] - t.shape[0] % 2, t.shape[1] - t.shape[1] % 2
        return t[:h, :w].reshape(h // 2, 2, w // 2, 2, -1).mean(axis=(1, 3))

    def mixed(px, palette):                              # share of pixels that are none of the palette's flat values
        dist = np.min([np.abs(px - T[k][None, :]).max(axis=1) for k in palette], axis=0)
        return float((dist > 1.5 / 255).mean())
    soft = {}
    for n in L.LATTICE_ORDER:
        if n in L.LATTICE_EDGE and L.LATTICE[n]['bar'] % 1 == 0:
            q = np.round(mip1(kit.tiles[n][..., None])[..., 0] * 255)
            soft['lat_' + n] = float(1 - ((q == 0) | (q == 255)).mean())
    for n in ('plaque_porcelain', 'keycap_porcelain'):
        soft[n] = mixed(mip1(kit.tex(n))[..., :3].reshape(-1, 3), ('Porcelain', 'PorcelainShade', 'InlayDark'))
    bd = mip1(kit.tex('frame_board'))
    b0, b1 = TL.SIZES['BoardBand']
    sl, pr = kit.cells['frame_board']['border_px'][0], kit.cells['frame_board']['period_px']
    band = bd[int(b0):int(b1), int(sl):int(sl + pr), :3].reshape(-1, 3)
    soft['frame_board_band'] = mixed(band, ('Lacquer', 'Wood'))
    half = [k for k, v in TL.SIZES.items() if k in ('PlaqueInset', 'PlaqueFoot', 'KeycapRim', 'KeycapLip', 'MiniFrame', 'SlotBarV', 'SlotBarH', 'SlotFrame')
            and float(v) % 1 != 0]
    half += ['PlaqueLines'] if sum(TL.SIZES['PlaqueLines'][:2]) % 1 != 0 else []
    half += ['BoardBand'] if b0 % 1 != 0 else []
    ok = max(soft.values()) == 0.0 and not half
    say('AC-T2b', ok, 'at mip 1 (1080p) half-covered pixels: %s; sizes that are not whole px: %s'
        % (', '.join('%s %.1f %%' % (k, v * 100) for k, v in soft.items()), ', '.join(half) or 'none'), soft=soft)

    # ---- T3 no emission / HDR ------------------------------------------------------------------------------------------
    nmax, pmax, kmax = 0, 0, 0
    for c in kit.meta['cells']:
        x, y, w, h = c['rect']
        t8 = kit.at8[y:y + h, x:x + w]
        if c['mode'] != 'baked':
            continue                                    # masks are white by format; their colour is a token (<= 230)
        vis = t8[..., 3] > 0
        if vis.any():
            kmax = max(kmax, int(t8[..., :3][vis].max()))
        if c['name'] in NACRE_CELLS:
            m = nacre_pixels(kit.tex(c['name']))
            if m.any():
                nmax = max(nmax, int(t8[..., :3][m].max()))
        if c['name'].startswith(('plaque', 'keycap')):
            pmax = max(pmax, int(t8[..., :3].max()))
    tok_max = max(int(round(float(np.max(v)) * 255)) for v in T.values())
    shaders = glob.glob(os.path.join(STAGE, '**', '*.shader'), recursive=True)
    bad_sh = [s for s in shaders if any(w in open(s, encoding='utf-8', errors='ignore').read() for w in ('_Emission', '[HDR]', 'Blend One One'))]
    ok = kit.at8.dtype == np.uint8 and nmax <= 217 and pmax <= 224 and kmax <= 230 and tok_max <= 230 and not bad_sh
    say('AC-T3', ok, '8-bit; max channel: nacre %d (<= 217), porcelain %d (<= 224), baked kit %d (<= 230), tokens %d; stage shaders %d (phase 1 adds none)'
        % (nmax, pmax, kmax, tok_max, len(shaders)), nacre_max=nmax, porcelain_max=pmax, kit_max=kmax, token_max=tok_max, shaders=len(shaders))

    # ---- T4 nacre colour -----------------------------------------------------------------------------------------------
    hues, sats, lums = [], [], []
    for n in NACRE_CELLS:
        t = kit.tex(n)
        m = nacre_pixels(t)
        px = t[..., :3][m]
        if not len(px):
            continue
        lab = L.srgb_to_oklab(px)
        oa, ob = lab[..., 1], lab[..., 2]
        chroma = np.hypot(oa, ob)
        keep = chroma > .008                             # hue is undefined on a nearly grey pixel (8-bit rounding)
        hues.append(np.degrees(np.arctan2(ob, oa))[keep] % 360)
        sats.append((px.max(axis=1) - px.min(axis=1)) / px.max(axis=1))
        lums.append(L.lum(px))
    hue = np.concatenate(hues)
    sat = np.concatenate(sats)
    lum = np.concatenate(lums)
    lo, hi = L.NACRE['H_centre'] - L.NACRE['H_span'] / 2, L.NACRE['H_centre'] + L.NACRE['H_span'] / 2
    TOL = 6.0                                                        # deg: one 8-bit step at chroma .02 - .034 turns the hue this far
    out = float(((hue < lo - TOL) | (hue > hi + TOL)).mean())
    hs = np.linspace(lo, hi, 191)
    base = np.array([L.oklch(np.float64(L.NACRE['L']), L.NACRE['C'], h) for h in hs])
    hue_only = float((L.lum(base).max() + .05) / (L.lum(base).min() + .05))
    Hr = np.radians(hs)
    Gm = np.stack([np.ones_like(Hr), np.cos(Hr), np.sin(Hr)], axis=1)
    coef, *_ = np.linalg.lstsq(Gm, base, rcond=None)
    fit = float(np.abs(Gm @ coef - base).max() * 255)
    ok = out == 0.0 and float(sat.max()) <= .20 and hue_only <= 1.05 and fit <= 1.5
    say('AC-T4', ok, 'shell pixels %d: hue %.0f - %.0f deg (arc %.0f - %.0f; beyond the arc by more than the %.0f deg of 8-bit rounding: %.2f %%), HSV saturation max %.3f, hue-only value ratio %.3f, shader fit %.2f / 255'
        % (len(sat), hue.min(), hue.max(), lo, hi, TOL, out * 100, sat.max(), hue_only, fit),
        hue_min=round(float(hue.min()), 1), hue_max=round(float(hue.max()), 1), saturation_max=round(float(sat.max()), 3),
        value_ratio_all_shell=round(float((lum.max() + .05) / (lum.min() + .05)), 3), hue_only_ratio=round(hue_only, 3), fit_error=round(fit, 2))

    # ---- T5 pattern under text -----------------------------------------------------------------------------------------
    veil90, veil85 = L.over(T['Veil'], L.SKY, .90), L.over(T['Veil'], L.SKY, .85)
    pairs = ((T['Paper'], veil90), (T['Mist'], veil90), (T['Paper'], veil85), (T['Mist'], veil85), (T['Ink'], T['Sheet']),
             (T['Ash'], T['Sheet']), (T['Ink'], T['Chip']), (T['Ash'], T['Chip']), (T['Ink'], T['Porcelain']), (T['Ash'], T['Porcelain']))
    plain = [round(L.contrast(a, b), 2) for a, b in pairs]
    at_cap = [round(L.contrast(a, grey_at_ratio(b, T['Paper'] if L.lum(b) < .2 else T['Ink'], 1.15)), 2) for a, b in pairs]
    same = all(abs(x - y) <= .02 for x, y in zip(plain, SPEC_PAIRS)) and all(abs(x - y) <= .02 for x, y in zip(at_cap, SPEC_AT_CAP))
    sheet_json = os.path.join(OUT, 'theme308_sheet.json')
    sheet_ok, sheet_txt = None, 'sample sheet json not found'
    if os.path.exists(sheet_json):
        rep = json.load(open(sheet_json, encoding='utf-8'))
        leg = rep.get('legibility', [])
        dflt = [r for r in leg if r['row'] == 'plain']
        cap = [r for r in leg if r['row'] == 'cap_1.15']
        sheet_ok = (bool(dflt) and all(r['ok'] and r['pattern'] <= 1.001 for r in dflt) and all(r['ok'] and r['pattern'] <= 1.155 for r in cap)
                    and rep.get('kit_atlas_sha256') == meta['sha256'])
        sheet_txt = 'sheet: %d plain grounds pattern 1.00 and %d grounds at the cap keep the text floors (measured on the pixels under the text)' % (len(dflt), len(cap))
        if rep.get('kit_atlas_sha256') != meta['sha256']:
            sheet_txt = 'the sample sheet was rendered from another atlas: run theme308_sheet.py again'
    say('AC-T5', same and sheet_ok is not False if sheet_ok is not None else None,
        'ten text / ground pairs equal the Spec table within .02 (%s); %s. NOT checked here: the 8 / 16 px clearances of the three '
        'consumers (their placement tables do not exist yet)' % ('yes' if same else 'NO', sheet_txt),
        pairs=plain, pairs_at_cap=at_cap)

    # ---- T6 two values -------------------------------------------------------------------------------------------------
    nac = L.nacre_token()
    c = dict(lacquer_sky=L.contrast(T['Lacquer'], L.SKY), nacre_lacquer=L.contrast(nac, T['Lacquer']), nacre_pine=L.contrast(nac, L.PINE),
             rim_pine=L.contrast(L.over(T['Paper'], L.PINE, .92), L.PINE))
    stray = 0
    for n in NACRE_CELLS:                                # a shell pixel may touch shell, lacquer, wood or nothing - never a bright ground
        t = kit.tex(n)
        m = nacre_pixels(t)
        other = (t[..., 3] >= .98) & ~m & (L.lum(t[..., :3]) > .12)       # opaque, not shell, brighter than wood (.054)
        grow = np.zeros_like(m)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            grow |= np.roll(np.roll(m, dy, axis=0), dx, axis=1)
        stray += int((other & ~grow).sum())              # bright opaque pixels that are not the anti-aliased skin of a shell piece
    ok = c['lacquer_sky'] >= 7 and c['nacre_lacquer'] >= 7 and c['nacre_pine'] >= 4.5 and c['rim_pine'] >= 7 and stray == 0
    say('AC-T6', ok, 'lacquer / sky %.2f, nacre / lacquer %.2f, nacre / pine %.2f, paper rim / pine %.2f; bright non-shell pixels in the najeon cells: %d'
        % (c['lacquer_sky'], c['nacre_lacquer'], c['nacre_pine'], c['rim_pine'], stray), **{k: round(v, 2) for k, v in c.items()})

    # ---- T7 ornament budget (the default design, measured on the kit as Unity would lay it) ------------------------------
    def shell_area(rgba):                                # design px^2 of shell in a picture laid at 2 texels per px
        return float((rgba[..., 3] * (L.lum(rgba[..., :3]) > .36)).sum() / 4.0)
    mini = shell_area(kit.rect('frame_mini', 312, 228, 2.0)) + shell_area(kit.simple('piece_north', 2.0))
    rail = shell_area(kit.strip('line_najeon', 1792, 2.0))
    sel = shell_area(kit.rect('frame_select', 114, 114, 2.0))
    pip = shell_area(kit.simple('pip_petal', 2.0))
    menu = rail + sel + 15 * pip
    hud, hud_txt, hm = None, 'HUD parts not measured (hud308_mock import failed)', None
    try:
        import hud308_mock as HM
        hm = HM
        col = 0.0
        for kind in ('hp', 'ink'):
            rw, rh = HM.RECT[kind][2:]
            g = HM.vessel_geo(rw, rh, 1.0, 4)
            _, nacre = L.collar_fields(g.d, g.x, g.y, g.cx, g.lip_h, g.neck_h, HM.SHAPE['neck_w'] * rw, 1.0 / g.k)
            col += float(nacre.sum() / (g.k * g.k))
        gl = 0.0
        for kind in ('dodge', 'jump', 'vehicle'):
            gg = HM.Glyph(1.0, 4)
            HM.GLYPHS[kind](gg)
            gl += float(np.clip(gg.m, 0, 1).sum() / (gg.k * gg.k)) * 0.74 ** 2
        hud = col + gl + mini
        hud_txt = 'HUD shell %.0f px^2 = %.3f %% (collar %.0f + three pictograms %.0f + minimap frame %.0f)' % (hud, hud / SCREEN * 100, col, gl, mini)
    except Exception as e:                                # another work stream owns that module
        hud_txt += ': ' + repr(e)[:80]
    ok = (menu / SCREEN * 100 <= .40) and (hud is None or hud / SCREEN * 100 <= .20)
    say('AC-T7', ok if hud is not None else None if ok else False,
        '%s (<= .20 %%); menu shell %.0f px^2 = %.3f %% (rail %.0f + kept selection %.0f + 15 pips %.0f) (<= .40 %%); figurative motifs in the default design: 0'
        % (hud_txt, menu, menu / SCREEN * 100, rail, sel, 15 * pip),
        hud_pct=None if hud is None else round(hud / SCREEN * 100, 3), menu_pct=round(menu / SCREEN * 100, 3), minimap_frame_px2=round(mini, 1))

    # ---- T8 colour vision ----------------------------------------------------------------------------------------------
    r = {k: L.contrast(L.cvd(nac, k), L.cvd(T['Cinnabar'], k)) for k in ('protan', 'deutan', 'tritan')}
    r['normal'] = L.contrast(nac, T['Cinnabar'])
    say('AC-T8', min(r.values()) >= 2.5, 'kept selection (nacre) / focus (cinnabar): normal %.2f, protan %.2f, deutan %.2f, tritan %.2f (>= 2.5); '
        'InlayIron has no cell in the atlas (it is a tint nobody is given)' % (r['normal'], r['protan'], r['deutan'], r['tritan']),
        **{k: round(v, 2) for k, v in r.items()})

    # ---- T9 the level stays readable -----------------------------------------------------------------------------------
    if hm is not None:
        gap = {}
        for kind in ('hp', 'ink'):
            rw, rh = hm.RECT[kind][2:]
            g = hm.vessel_geo(rw, rh, 1.0, 4)
            gap[kind] = float(g.full_y - (g.lip_h + g.neck_h + L.COLLAR['bottom']))
        say('AC-T9', min(gap.values()) > 0, 'the collar ends above the full line: HP %.2f px, ink %.2f px (the twin of AC-H3-1 is the HUD stage\'s to run)'
            % (gap['hp'], gap['ink']), **{k: round(v, 2) for k, v in gap.items()})
    else:
        say('AC-T9', None, 'collar clearance not measured (hud308_mock import failed)')

    # ---- T10 memory ----------------------------------------------------------------------------------------------------
    tiles_b = sum(t.size for t in kit.tiles.values()) * 4 / 3.0
    atlas_b = kit.at8.shape[0] * kit.at8.shape[1] * 4 * 4 / 3.0
    ok = kit.at8.shape[0] <= 512 and kit.at8.shape[1] <= 512 and tiles_b / 2 ** 20 <= .05 and (tiles_b + atlas_b) / 2 ** 20 <= 1.5
    say('AC-T10', ok, 'atlas %d x %d RGBA32 + mips %.2f MB, tiles Alpha8 + mips %.3f MB, sum %.2f MB (<= 1.5; the imported GPU size is an [E] item)'
        % (kit.at8.shape[1], kit.at8.shape[0], atlas_b / 2 ** 20, tiles_b / 2 ** 20, (tiles_b + atlas_b) / 2 ** 20),
        atlas_mb=round(atlas_b / 2 ** 20, 3), tiles_mb=round(tiles_b / 2 ** 20, 3), atlas_fill=meta['fill'])

    # ---- T11 where the patterns come from ------------------------------------------------------------------------------
    gens = ('theme308_lib.py', 'theme308_tiles.py', 'theme308_assets.py')
    src = {g: open(os.path.join(HERE, g), encoding='utf-8').read() for g in gens}
    opens = sum(v.count('Image.' + 'open(') for v in src.values())
    refs = 0
    for p in glob.glob(os.path.join(STAGE, '**', '*'), recursive=True):
        if os.path.isfile(p) and not p.lower().endswith('.png'):
            refs += open(p, encoding='utf-8', errors='ignore').read().count('changho_lattice_ref')
        refs += int('changho' in os.path.basename(p).lower())
    say('AC-T11', opens == 0 and refs == 0, 'picture reads in the three generators: %d; the reference picture in the stage: %d' % (opens, refs))

    # ---- T12 kit completeness ------------------------------------------------------------------------------------------
    missing = [n for n in SPEC_CELLS if n not in kit.cells]
    slices = {n: kit.cells[n]['border_px'] for n in SPEC_SLICES if n in kit.cells}
    off = {n: v for n, v in slices.items() if any(abs(b - SPEC_SLICES[n]) > 1e-9 for b in v)}
    slots = [n for n in MAP_SLOTS if n not in kit.by_kit_name]
    ok = not missing and not slots and set(off) <= {'frame_board'}
    say('AC-T12', ok, '%d / %d cells; map kit slots missing: %d; slices as the Spec table except: %s'
        % (len(SPEC_CELLS) - len(missing), len(SPEC_CELLS), len(slots),
           ', '.join('%s %s' % (k, v) for k, v in off.items()) or 'none'), deviations=off)

    # ---- T12b a Tiled frame cut at any multiple of its piece length ends on a joint (review 2026-10-04) -----------------
    def shortest_piece(name, w, h, row_px, x0):           # px: the shortest shell piece along the frame's top line
        r = kit.rect(name, w, h, 2.0)
        line = r[int(round(row_px * 2))]
        v = (line[:, 3] * L.lum(line[:, :3]))[int(x0 * 2):line.shape[0] - int(x0 * 2)]      # the straight run between the corners
        if v.min() < .1 * np.median(v):
            return 0.0                                    # a hole in the run: lacquer or nothing, wider than a joint
        nb = np.minimum(np.roll(v, 3), np.roll(v, -3))    # a joint = a 1 - 2 texel dip against both neighbours three texels away
        dip = np.flatnonzero((v < .9 * nb)[3:-3]) + 3
        cuts = [0] + [int(i) for i in dip if i - 1 not in dip] + [len(v)]
        return float(np.diff(cuts).min() / 2.0)
    sp = {}
    for name, base, row, x0 in (('frame_mini', 48, 7.75, 14.5), ('frame_select', 24, 1.0, 0.0), ('frame_board', 80, 3.75, 12.5)):
        c = kit.cells[name]
        sp[name] = min(shortest_piece(name, base + c['piece_px'] * n, base + 2 + c['piece_px'] * n, row, x0) for n in range(1, 9))
    sizes = dict(joint=L.NACRE['joint_px'], pieces={k: kit.cells[k]['period_px'] / kit.cells[k]['piece_px'] for k in sp})
    ok = min(sp.values()) >= 8.0 and min(sizes['pieces'].values()) >= 3 and L.NACRE['joint_px'] <= .5
    say('AC-T12b', ok, 'frames laid at slices + n pieces (n = 1 .. 8): shortest shell piece %s px (>= 8: no sliver); pieces per period %s (>= 3); '
        'joint %.1f px (<= .5: a hairline, not a dash)' % (', '.join('%s %.1f' % kv for kv in sp.items()),
                                                         ', '.join('%s %d' % (k, v) for k, v in sizes['pieces'].items()), L.NACRE['joint_px']),
        shortest_piece_px=sp, **sizes)

    # ---- T13 / T14: C# -------------------------------------------------------------------------------------------------
    cs = glob.glob(os.path.join(STAGE, '**', '*.cs'), recursive=True)
    # apply review 2026-10-04: the stage now holds the import command (Editor/WorldMacro/UiTheme308.cs). It carries no theme
    # value: a token colour typed into it (hex or Color32) would be a FAIL. The whole of AC-T13 stays open until UiTheme308SO
    # exists (the HUD holds a checked copy of the tokens, the equipment setup reads theme308_tokens.json).
    typed = []
    with open(os.path.join(STAGE, '_ProjectAssets', 'Art', 'UI', 'UI308', 'Theme', 'theme308_tokens.json'), encoding='utf-8') as f:
        token_hex = json.load(f)['colours']
    for p in cs:
        text = open(p, encoding='utf-8', errors='ignore').read()
        for name, hx in token_hex.items():
            if hx.lower() in text.lower() or ('0x%s, 0x%s, 0x%s' % (hx[1:3], hx[3:5], hx[5:7])).lower() in text.lower():
                typed.append('%s in %s' % (name, os.path.basename(p)))
    say('AC-T13', False if typed else None, 'kit stage C#: %d file(s), token colours typed in them: %s; UiTheme308SO does not exist yet, so "one place for the values" '
        'is not measurable' % (len(cs), ', '.join(typed) if typed else 'none'))
    say('AC-T14', None, 'kit stage C#: %d file(s); the offline compile is a separate command (Tools/Unity/offline_compile297.py --stage ... Stage308_theme)' % len(cs))

    # ---- T20 no text in a texture --------------------------------------------------------------------------------------
    fonts = sum(v.count('ImageFont') + v.count('.text(') for v in src.values())
    say('AC-T20', fonts == 0, 'font / text calls in the three generators: %d' % fonts)

    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, 'theme308_check.json'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(dict(spec='SPEC-UI-THEME-308', atlas_sha256=meta['sha256'], results=RES), ensure_ascii=False, indent=1) + '\n')
    failed = [r['ac'] for r in RES if r['result'] == 'FAIL']
    open_ = [r['ac'] for r in RES if r['result'] == 'NOT MEASURED']
    print('summary: %d OK, %d FAIL%s, %d not measured%s; [E] / [P] items (T15 - T19, T21) are not measurable offline'
          % (sum(r['result'] == 'OK' for r in RES), len(failed), (' (' + ', '.join(failed) + ')') if failed else '',
             len(open_), (' (' + ', '.join(open_) + ')') if open_ else ''))
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
