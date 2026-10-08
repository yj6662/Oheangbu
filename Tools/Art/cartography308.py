# -*- coding: utf-8 -*-
"""SPEC-MAP-OVERHAUL-308 track A - the map bundle baker (files 1-8 of the asset contract, Spec section 7).

    python Tools/resource_guard.py --wait
    python Tools/Art/cartography308.py --stage base        (today: the scenes still carry the base height)
    python Tools/Art/cartography308.py --stage 1a          (after the cliff ledger's `tiles` step applied stage 1a: the `map` step)

Reads the main scene's data copies as text (never imports or writes a Unity asset) and writes
Tools/Unity/Stage308_map/_ProjectAssets/Art/UI/UI308/Map/:

    map308_terrain.png          1000 x 1500 RGBA   R slope wash, G forest density, B water signed distance, A elevation
    map308_pattern.png          256 x 256 RGBA     R tree dots, G ripples, B rock strokes, A wash grain (tileable)
    map308_strokes.bytes        'MS08' v1          brush-band centre lines + 250 m bin table (roads, ridges, cliffs, walls, ...)
    map308_stroke_atlas.png     1024 x 512 RGBA    8 brush rows: A ink, R paper underlay
    map308_icons_L.png / _S.png 512 / 256 RGBA     8 x 8 glyph cells: R ink, G paper plate, B second ink, A = max(R, G)
    map308_reveal_regions.bytes 125 x 188 uint8    region id per 32 m discovery cell (south row first)
    map308_notation.json        the notation tables, input and output sha256, stage

#214: no input line is named MainPath / campaign, and a road's class comes from Width / GradeForVehicle / Traversal only -
the story Role is never parsed (map308_check.py proves the bake is byte-identical on a layout copy with scrambled roles).
Deterministic: two runs give identical bytes. Refuses when the Main and candidate layout copies disagree.
"""
import argparse, json, math, re, struct, sys, time, zlib
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import map308_lib as M
import map308_art as ART
import map308_ridges as RIDGE

ROAD_TOL_M = 0.9            # Douglas-Peucker tolerance of the road centre lines (AC-O5: within 1.0 m of the source line)
MAX_SEG_M = 48.0            # no stroke segment is longer (a visible segment always has an end point inside the loaded 3 x 3 bins)
TICK_M = 200.0              # MapStyle304SO.RoadTickMetres (PROPOSED unit, LDB to confirm)
TICK_HALF_M = 9.0
FORBIDDEN_LINE = re.compile(r'(MainPath|campaign)', re.I)

ZOOM = dict(maxMetresPerPx=[0.6, 1.8, 5.0, None], refMetresPerPx=[0.27, 1.14, 2.66, 7.9], ids=['Z0', 'Z1', 'Z2', 'Z3'],
            screens=['미니맵 갱도 · 갱도 안 펼친 지도', '미니맵 야외 · 펼친 지도 최대 확대', '펼친 지도 기본 보기', '펼친 지도 전체 보기'])
U_ASPECT = {0: 1.5, 1: 1.5, 2: 2.0, 3: 1.0, 4: 2.0, 5: 1.0, 6: 1.0, 7: 0.0}   # texture stretch along U per atlas row (0 = stretch once)
CLASSES = [  # class number = draw order (Spec section 7). widthPx / atlasRow per zoom band Z0..Z3; widthPx 0 = not drawn in that band
    dict(cls=1, name='river', ko='물줄기', row=[0, 0, 0, 0], width=[0, 0, 1.6, 1.6], knock=0.0, alpha=.80, teeth=False, pressure=[.5, 1.25]),
    # ridges: the direction sheet's three ranks, all saw bands (small teeth kept on rank 3) and its ink weights .88 / .78 / .56.
    # Rank 4 = a rank 3 piece of a ridge shorter than 220 m: the same drawing, hidden from the region band on (the sheet's rule).
    dict(cls=2, name='ridge', ko='산줄기', knock=0.0, pressure=[.9, 1.15], min_rank=[4, 4, 3, 2], ranks=[
        dict(rank=1, row=[3, 3, 3, 3], width=[11, 9, 7, 5], alpha=.88, teeth=True),
        dict(rank=2, row=[3, 3, 3, 3], width=[7.5, 6, 4, 2.5], alpha=.78, teeth=True),
        dict(rank=3, row=[3, 3, 3, 3], width=[3.6, 3, 2, 0], alpha=.56, teeth=True),
        dict(rank=4, row=[3, 3, 3, 3], width=[3.6, 3, 0, 0], alpha=.56, teeth=True)]),
    dict(cls=3, name='cliff', ko='벼랑', row=[5, 5, 5, 4], width=[10, 8.5, 6, 1.6], knock=0.0, alpha=.88, teeth=True, pressure=[1.0, 1.1]),
    dict(cls=4, name='outer_range', ko='끝 연봉', row=[3, 3, 3, 3], width=[12, 10, 8, 5], knock=0.0, alpha=.92, teeth=True, pressure=[1.0, 1.1]),
    dict(cls=5, name='wall', ko='성벽', row=[6, 6, 6, 6], width=[6, 5, 4, 3], knock=0.0, alpha=.90, teeth=False, pressure=[1.0, 1.0]),
    dict(cls=6, name='site', ko='터 선', row=[4, 4, 4, 4], width=[2, 1.5, 1.5, 0], knock=0.0, alpha=.75, teeth=False, pressure=[1.0, 1.0]),
    # footpath: the dry-brush row where its character can be seen (Z0, Z1); at 1.8 px and under (the unfolded map) the stroke is
    # the medium brush - a dry texture there only makes the line fall under 4.5:1 (AC-O6.2), rank is told by width alone
    dict(cls=7, name='soro', ko='샛길', row=[2, 2, 1, 1], width=[2.8, 2.2, 1.8, 1.3], knock=1.5, alpha=.84, teeth=False, pressure=[.96, 1.2]),
    dict(cls=8, name='gil', ko='길', row=[1, 1, 1, 1], width=[4.0, 3.2, 2.6, 1.8], knock=1.5, alpha=.86, teeth=False, pressure=[.96, 1.2]),
    dict(cls=9, name='daero', ko='큰길', row=[0, 0, 0, 0], width=[5.6, 4.5, 3.6, 2.4], knock=1.5, alpha=.92, teeth=False, pressure=[.96, 1.2]),
    dict(cls=10, name='bridge', ko='다리', row=[7, 7, 7, 7], width=[12, 10, 8, 0], knock=0.0, alpha=.90, teeth=False, pressure=[1.0, 1.0]),
    dict(cls=11, name='tick', ko='거리 눈금', row=[4, 4, 4, 4], width=[0, 0, 1.3, 0], knock=0.0, alpha=.85, teeth=False, pressure=[1.0, 1.0]),
]
# terrain: the direction sheet's treatment. The wash stays, but SOFT: the slope of the height smoothed by a 3 x 3 box (no 4 m
# terraces), so the 3.5x enlargement on the minimap is an even pale pool; shape comes from the saw bands, the tree dabs and the
# rock strokes. Forest density, dab clusters, wave marks and their inks are the sheet's numbers.
TERRAIN = dict(slopeWashDeg=[22.0, 45.0], slopeWashMax=.18, slopeOf='height smoothed by a 3 x 3 box', elevationWash=.14, elevationMaxM=400.0,
               elevationRangeM=[112.0, 320.0], rockFrom=.96, rockSoftness=.08, rockInk=.45,
               forest=dict(speciesPattern=M.TREE_RX.pattern, treesPerHaFull=110.0, densityFrom=.15, densityTo=.80, densityMax=.90, blurCells=3,
                           showFrom=.05, gain=4.0, dotInk=.55, dotInkWorld=.40, noTreesWithinWaterM=2.0,
                           read='ink = saturate((pattern.r - (1 - density)) * gain) * dotInk: a dab cluster appears when the density passes its level'),
               water=dict(sdfRangeM=32.0, shoreCode=128, shoreLinePx=1.25, shoreInk=.78, patternFromM=9.0, patternFeatherM=5.0, rippleInk=.50,
                          rippleInkWorld=.22),
               cliffTopGrassDensity=.24, patternPeriodM=[70.0, 292.0, 496.0, 704.0])
EDGE = dict(thresholdFrom=.14, thresholdSpan=.42, noiseCellsPerFogCell=[1.6, 3.7], noiseWeights=[.62, .38])
# #308 map 3 (M1): the walked edge's ink rim, as notation values (values.fogEdge.inkAlpha / px -> MapNotation308SO.EdgeRimAlpha / EdgeRimPx
# -> the paper's _M308Rim). R3 is the stage default; `set MAP308_RIM=R2` bakes the other variant (only map308_notation.json differs).
RIMS = dict(R2=(.45, 2.0), R3=(.55, 3.0))
RIM_VARIANT = __import__('os').environ.get('MAP308_RIM', 'R3')
if RIM_VARIANT not in RIMS: raise SystemExit('REFUSED: MAP308_RIM must be R2 or R3')
RIM = RIMS[RIM_VARIANT]
# #308 map 4 (D308-24 answer 9): the rim is view dependent BY DATA. The minimap draws the broken dry-brush rim above; the unfolded
# sheet and the whole-world view draw a thin whole rim whose numbers are the data file beside this tool (an input of the bake: its
# sha goes into inputs[] as 'rimViews'). -> values.fogEdge.sheet -> MapNotation308SO.SheetRimWhole / SheetRimPx / SheetRimAlpha ->
# the paper's _M308Rim.z / .w (read only without _MINI_HUD). whole 0 = the sheet draws the minimap's rim (the picture of map 3).
RIM_VIEWS = M.TOOLS / 'map308_rim_views.json'
if not RIM_VIEWS.exists(): raise SystemExit('REFUSED: input missing: ' + M.rel(RIM_VIEWS))
_SHEET = json.loads(RIM_VIEWS.read_text(encoding='utf-8'))['sheet']
SHEET_RIM = dict(whole=float(_SHEET['whole']), px=float(_SHEET['px']), inkAlpha=float(_SHEET['inkAlpha']))
if not (SHEET_RIM['whole'] in (0.0, 1.0) and .5 <= SHEET_RIM['px'] <= 4.0 and 0 < SHEET_RIM['inkAlpha'] <= 1):
    raise SystemExit('REFUSED: map308_rim_views.json sheet: whole must be 0 or 1, px .5 .. 4, inkAlpha 0 .. 1')
VALUES = dict(paper=M.PAPER, wash=M.WASH, ink=M.INK, cinnabar=M.CINNABAR, slopeWashRange=[.10, .18], inkAlpha=[.84, .92],
              # what the #308 shaders draw (MapFog308.cginc MapFog308_Edge + the paper shader's _MAP308 branch): the walked land
              # ends on a CRISP edge (1 px anti-aliased) that wobbles 4.5 .. 18 m INSIDE the walked cells - never on the 32 m grid,
              # never on an unwalked cell - and its outermost px are an ink rim (the direction sheet the user was shown). The
              # #307 soft edge (MapFog308_Known) is still what a map WITHOUT a bundle draws.
              fogEdge=dict(inkAlpha=RIM[0], px=RIM[1], over='wash', colour='#%02X%02X%02X' % tuple(int(round(v * 255)) for v in M.over(M.hexc(M.INK), M.hexc(M.WASH), RIM[0])),
                           # #308 map 3 (M1): the rim is the broken dry-brush rim; its ink and width follow the shader's gate (MapFog308_Rim:
                           # width = px x .25 .. 1.25 by the brush pressure). Two notation variants, the SAME shader: R3 (stage default) and R2.
                           variant=RIM_VARIANT, variants=dict(R2=dict(inkAlpha=RIMS['R2'][0], px=RIMS['R2'][1]), R3=dict(inkAlpha=RIMS['R3'][0], px=RIMS['R3'][1])),
                           where='the outermost px of the walked land: a band up to 1.25 x px wide just inside the crisp edge (thinner toward the end of a run), ink at inkAlpha over the unwalked wash',
                           # #308 map 4: the unfolded sheet and the whole-world view (never the minimap)
                           sheet=dict(whole=SHEET_RIM['whole'], px=SHEET_RIM['px'], inkAlpha=SHEET_RIM['inkAlpha'],
                                      colour='#%02X%02X%02X' % tuple(int(round(v * 255)) for v in M.over(M.hexc(M.INK), M.hexc(M.WASH), SHEET_RIM['inkAlpha'])),
                                      where='the unfolded sheet and the whole-world view: a WHOLE rim px wide (screen px at every zoom) just inside the crisp edge, ink at inkAlpha over the unwalked wash; whole 0 = the minimap rim above'),
                           crispEdge=dict(function='MapFog308_Edge', aaPx=1.0, insetM=[round(EDGE['thresholdFrom'] * M.FOG_CELL, 1),
                                                                                    round((EDGE['thresholdFrom'] + EDGE['thresholdSpan']) * M.FOG_CELL, 1)],
                                          field='bilinear of the four lattice corners of the discovery cell; a corner = min of its four cells (0 on every unwalked cell and its border)',
                                          **EDGE),
                           note='terrain, strips and the rim all use the same edge; an unwalked cell is 0 by construction (#214); the #307 soft edge is the no-bundle path only'),
              washGrainAmp=.03,
              derived=dict(lightWash='#C9C5BB', darkWash='#B7B4AB', roadInkDaero='#242422', roadInkSoro='#343331'))
SIZES = dict(minimap=dict(icon=22, player=28, pin=18, coin=20, edgeMargin=12, wakeRingScale=1.5),
             fullMap=dict(source='MapStyle304SO', player=48, rest=34, place=30, coin=22, pin=30, wakeRingScale=1.5, labelMaxViewWidth=.62))
MARKER_GLYPHS = dict(  # baked marker id -> glyph id (the 23 markers of Map.asset + map3: the long wall gate, D308-18 #7, and the
    # two inns built after section 3 was written (hunter inn LDB:52, road inn LDB:65). They reuse the 'gate' glyph of
    # south_gate and the 'inn' glyph of geumpyo_inn, so the icon sheets do not change)
    jangseong_gate='gate', hunter_inn308='inn', road_inn308='inn',
    mine='mine', geumpyo_inn='inn', relay='relay', village='village', logging='worksite', herb_path='place', deep_forest='deepforest',
    sanctuary='temple', high_cache='trace', village_toolbox='trace', village_cut_trace='trace', inspection_one='checkpoint',
    inspection_two='checkpoint', capital_delivery='gaekju', south_gate='gate', root_cave='cave', old_tree='bigtree',
    mountain_cheongrim_summit='peak', mountain_cheongrim_temple='temple', arena_jeokro296='camp', arena_cheolong296='hall',
    arena_hyeongang296='waterside', palace='hall')
KIND_FALLBACK = dict(Place='place', Settlement='village', Gate='gate', Rest='inn', Checkpoint='inn', Drop='coin', Pin='pin', Mountain='peak')
LABEL_PRIORITY = ['inn', 'village', 'gaekju', 'gate', 'peak']        # then every other glyph (section 4: rest > village > gate > peak > rest)
LEGEND = [  # markers: the 7 rows of MapStyle304SO.DefaultLegend (names unchanged); terrain and roads: 3 x 3 cells drawn from the bundle
    dict(group='marks', name='현재 위치', glyph='player', tint='cinnabar'),
    dict(group='marks', name='쉼터', glyph='inn', withRing='wake_ring'),
    dict(group='marks', name='발견한 장소', glyph='place'),
    dict(group='marks', name='남긴 통보', glyph='coin'),
    dict(group='marks', name='내 표식', glyph='pin'),
    dict(group='marks', name='들은 목적 권역', glyph='objective', tint='cinnabar'),
    dict(group='marks', name='걷지 않은 땅', swatch='wash'),
    dict(group='terrain', name='산줄기', strokeClass=2, rank=1),
    dict(group='terrain', name='벼랑', strokeClass=3),
    dict(group='terrain', name='성벽', strokeClass=5),
    dict(group='terrain', name='물', pattern='g', shore=True),
    dict(group='terrain', name='숲', pattern='r'),
    dict(group='terrain', name='큰길', strokeClass=9),
    dict(group='terrain', name='길', strokeClass=8),
    dict(group='terrain', name='샛길', strokeClass=7),
    dict(group='terrain', name='다리', strokeClass=10, over=9),
]
FRAMES = dict(  # the frames are the THEME KIT's sprites (SPEC-UI-THEME-308, D308-15): this bundle ships no frame art of its own
    theme='SPEC-UI-THEME-308', kitAtlas='Art/UI/UI308/Theme/theme308_atlas.png', kitAtlasJson='Art/UI/UI308/Theme/theme308_atlas.json',
    kitTexelsPerDesignPx=2,
    # the kit's rules (theme308_atlas.json, re-baked after its review): frame_mini 168 x 168 texels, rect = 48 + 12 n;
    # piece_north 32 x 36 texels = 16 x 18 px, its cell bottom 7 px inside the frame's outer edge (centre 2 px ABOVE that edge,
    # the petal's tip 11 px above it); frame_board 214 x 218 texels, rect = (80 + 9 n) x (82 + 9 m). Tiled sides start at the
    # bottom-left (Unity), so a rectangle that keeps the rule ends every side on a whole piece.
    minimap=dict(window=[294, 210], frameWidth=9, outer=[312, 228], centre=[1740, 900], nacreLine=1.5, cornerPiece=14, northPiece=[7, 12],
                 northCell=[16, 18], northCellBottomInsideFrame=7, northCentreFromTop=-2.0, paperUnderFrame=True),
    # the board is 910 high (82 + 9 x 92), not the first 892: the kit's bottom band (inlay line + lattice, the outer 19 px) must
    # lie UNDER the control row (keycaps y 979-1013), which sits on plain lacquer between the sheet (y 960) and the band (y 1017)
    board=dict(rect=[542, 126, 1378, 1036], size=[836, 910], margin=18, controlStrip=76, sheet=[800, 820], sheetAt=[560, 140],
               printWindow=[740, 760], controlRow=[972, 1020], bandPx=19),
    slots={'Frame.Mini': dict(kitCell='frame_mini', use='Tiled', rect='48 + 12 n on both axes: 312 x 228 (n = 22, 15)', rule=dict(base=[48, 48], piece=12)),
           'Frame.MapBoard': dict(kitCell='frame_board', use='Tiled', rect='80 + 9 n wide, 82 + 9 m high: 836 x 910 (n = 84, m = 92)', rule=dict(base=[80, 82], piece=9),
                                  body='White x Lacquer under it'),
           'Inlay.Line': dict(kitCell='line_najeon_thin', use='Tiled', where='legend group heads, place list rules'),
           'Corner.Fret': dict(kitCell='corner_fret', use='Simple', where='already inside Frame.Mini / Frame.MapBoard; loose corners only'),
           'Piece.North': dict(kitCell='piece_north', use='Simple', where='minimap frame top side; slides along the frame in view-rotation mode'),
           'Plate.Icon': dict(kitCell='plate_lacquer', use='Sliced', where='under a legend symbol')},
    temporaryFiles=[], note='contract row 9 (map308_frame_*.png, map308_north_piece.png, map308_plate_icon.png) is NOT produced: the kit exists')


def pbyte(f):
    return int(np.clip(round((f - .5) / .75 * 255), 0, 255))


# ---------------------------------------------------------------------------------------------------------------------
# terrain
# ---------------------------------------------------------------------------------------------------------------------
def bake_terrain(h, wet, trees, tops, outer_lines, log):
    cell = lambda g: (g[:-1, :-1] + g[:-1, 1:] + g[1:, :-1] + g[1:, 1:]) / 4          # node lattice [z, x] -> cell centres
    hc = cell(h)
    gz, gx = np.gradient(M.box(h, 1), M.CELL)                                       # the sheet's rule: no 4 m terraces in the wash
    slope = cell(np.degrees(np.arctan(np.hypot(gx, gz))))
    a, b = TERRAIN['slopeWashDeg']
    R = np.clip((slope - a) / (b - a), 0, 1)
    # forest density from the tree placements: trees per hectare (three box passes), then the sheet's keep curve
    F = TERRAIN['forest']
    cnt = np.zeros((M.TH, M.TW))
    j = np.clip((trees[:, 0] / M.CELL).astype(int), 0, M.TW - 1); i = np.clip((trees[:, 1] / M.CELL).astype(int), 0, M.TH - 1)
    np.add.at(cnt, (i, j), 1.0)
    dens = RIDGE.gauss(cnt, F['blurCells']) / (M.CELL * M.CELL) * 1e4
    k = np.clip((dens / F['treesPerHaFull'] - F['densityFrom']) / (F['densityTo'] - F['densityFrom']), 0, 1)
    G = k * k * (3 - 2 * k) * F['densityMax']
    G = np.where(slope >= b, 0, G)                                                  # no trees on a rock face
    # water signed distance (metres, + inside), node lattice -> cell centres
    rng_cells = TERRAIN['water']['sdfRangeM'] / M.CELL + 1
    d_out = M.near_dist(wet, rng_cells); d_in = M.near_dist(~wet, rng_cells)
    sdf = np.where(wet, d_in - .5, -(d_out - .5)) * M.CELL
    sdf = M.box(sdf, 1)
    sdf_c = (sdf[:-1, :-1] + sdf[:-1, 1:] + sdf[1:, :-1] + sdf[1:, 1:]) / 4
    Bc = 128 + np.clip(sdf_c / TERRAIN['water']['sdfRangeM'], -1, 1) * 127
    wet_c = sdf_c > 0
    R = np.where(wet_c, 0, R); G = np.where(sdf_c > -F['noTreesWithinWaterM'], 0, G)   # water carries no wash; no trees in or at the water
    if tops is not None:                                                            # cliff-top fields: paper + sparse grass dots
        # node lattice -> cells; a mask that already is the cell lattice (the carried cells of a stage whose reach masks are lost) is taken as it is
        tc = tops if tops.shape == (M.TH, M.TW) else (tops[:-1, :-1] | tops[:-1, 1:] | tops[1:, :-1] | tops[1:, 1:])
        G = np.where(tc & (G < TERRAIN['cliffTopGrassDensity']), TERRAIN['cliffTopGrassDensity'], G)
    beyond = np.zeros((M.TH, M.TW), bool)
    if outer_lines:                                                                 # beyond the outer range: bare paper
        X = (np.arange(M.TW) + .5) * M.CELL; Z = (np.arange(M.TH) + .5) * M.CELL
        XX, ZZ = np.meshgrid(X, Z)
        for P, out_left in outer_lines:
            beyond |= _side_of(P, XX, ZZ, out_left)
        R = np.where(beyond, 0, R); G = np.where(beyond, 0, G)
    A = np.clip(hc / TERRAIN['elevationMaxM'], 0, 1)
    img = np.dstack([np.round(R * 255), np.round(G * 255), np.round(Bc), np.round(A * 255)]).astype(np.uint8)[::-1]   # top row = north
    stats = dict(slope_share={k: round(float(((slope >= lo) & (slope < hi)).mean()), 4) for k, (lo, hi) in
                              dict(lt22=(0, 22), wash_22_45=(22, 45), rock_ge45=(45, 91)).items()},
                 forest_cells_share=round(float((G >= F['showFrom']).mean()), 4), forest_full_share=round(float((G >= .99).mean()), 4),
                 trees_per_ha_p50_p90_p99=[round(float(np.quantile(dens[dens > 0], q)), 1) for q in (.5, .9, .99)],
                 water_cells=int(wet_c.sum()), water_ha=round(float(wet_c.sum()) * 16 / 1e4, 1),
                 elevation_m=[round(float(hc.min()), 1), round(float(hc.max()), 1)], beyond_outer_cells=int(beyond.sum()))
    log(f'  terrain: {stats}')
    return img, stats


def _side_of(P, XX, ZZ, left):
    """cells whose nearest point on polyline P is an interior point and that lie on the given side (within 600 m)."""
    P = np.asarray(P, float); best = np.full(XX.shape, np.inf); side = np.zeros(XX.shape); interior = np.zeros(XX.shape, bool)
    x0, z0 = P.min(0) - 600; x1, z1 = P.max(0) + 600
    box = (XX >= x0) & (XX <= x1) & (ZZ >= z0) & (ZZ <= z1)
    xs, zs = XX[box], ZZ[box]; b = np.full(xs.shape, np.inf); sd = np.zeros(xs.shape); it = np.zeros(xs.shape, bool)
    n = len(P) - 1
    for k in range(n):
        ax, az = P[k]; bx, bz = P[k + 1]; L = math.hypot(bx - ax, bz - az)
        if L < 1e-6: continue
        tx, tz = (bx - ax) / L, (bz - az) / L
        s = (xs - ax) * tx + (zs - az) * tz; sc = np.clip(s, 0, L)
        d = np.hypot(xs - (ax + tx * sc), zs - (az + tz * sc)); q = -(xs - ax) * tz + (zs - az) * tx
        better = d < b
        b[better] = d[better]; sd[better] = q[better]
        it[better] = ((s[better] >= 0) | (k > 0)) & ((s[better] <= L) | (k < n - 1))
    out = np.zeros(XX.shape, bool)
    out[box] = it & (b <= 600) & ((sd > 0) if left else (sd < 0))
    return out


# ---------------------------------------------------------------------------------------------------------------------
# strokes
# ---------------------------------------------------------------------------------------------------------------------
class Strokes:
    def __init__(self):
        self.items = []

    def add(self, cls, sid, pts, rank=0, state=0, half_w=0.0, press=None, cap=(True, True), teeth_left=False, bridge=None):
        pts = M.dedupe(np.asarray(pts, float))
        if len(pts) < 2 or M.poly_len(pts) < 2.0: return False
        s0 = M.cum_len(pts)
        pts2 = M.densify(pts, MAX_SEG_M); s = M.cum_len(pts2)
        if press is None: pr = np.full(len(pts2), pbyte(1.0), np.uint8)
        else: pr = np.array([pbyte(v) for v in np.interp(s, s0, press)], np.uint8)
        fl = np.zeros(len(pts2), np.uint8)
        if cap[0]: fl[0] |= 1
        if cap[1]: fl[-1] |= 2
        if teeth_left: fl |= 8
        if bridge is not None:
            for a, b in bridge: fl[(s >= a - 1e-3) & (s <= b + 1e-3)] |= 4
        self.items.append(dict(cls=cls, rank=rank, state=state, id=sid, pts=pts2.astype(np.float32), dist=s.astype(np.float32),
                               press=pr, flags=fl, half_w=float(half_w), length=float(s[-1])))
        return True


def road_pressure(pts, h, sid, lo, hi):
    """pressure factor per point: thinner in bends and on steep ground, a slow breathing along the stroke (decided at bake time)."""
    n = len(pts); s = M.cum_len(pts)
    curv = np.zeros(n)
    if n > 2:
        v1 = pts[1:-1] - pts[:-2]; v2 = pts[2:] - pts[1:-1]
        ang = np.abs(np.arctan2(v1[:, 0] * v2[:, 1] - v1[:, 1] * v2[:, 0], (v1 * v2).sum(1)))
        curv[1:-1] = ang / np.maximum(.5 * (np.hypot(*v1.T) + np.hypot(*v2.T)), 1.0)
    y = M.sample_grid(h, pts[:, 0], pts[:, 1])
    grade = np.abs(np.gradient(y, np.maximum(s, 1e-6) if n < 2 else s)) if n > 2 and s[-1] > 0 else np.zeros(n)
    hsh = M.fnv1a32(sid); p1 = (hsh & 0xFF) / 255 * 2 * math.pi; p2 = ((hsh >> 8) & 0xFF) / 255 * 2 * math.pi
    wave = .5 * np.sin(s / 37.0 + p1) + .5 * np.sin(s / 13.7 + p2)
    f = 1.09 - 2.0 * np.minimum(curv, .06) - .35 * np.minimum(grade, .4) + .08 * wave
    if n >= 3: f = np.convolve(np.pad(f, 1, mode='edge'), [.25, .5, .25], mode='valid')
    return np.clip(f, lo, hi)


def insert_point(P, q, max_d=8.0):
    """insert the projection of q on polyline P (returns P', arclength of the foot) or (P, None) when q is farther than max_d."""
    best = (np.inf, 0, 0.0)
    for k in range(len(P) - 1):
        a, b = P[k], P[k + 1]; d = b - a; L2 = float(d @ d)
        if L2 < 1e-9: continue
        t = float(np.clip(((q - a) @ d) / L2, 0, 1)); f = a + t * d; dist = math.hypot(*(q - f))
        if dist < best[0]: best = (dist, k, t)
    if best[0] > max_d: return P, None
    _, k, t = best
    f = P[k] + t * (P[k + 1] - P[k])
    if t > 1e-6 and t < 1 - 1e-6: P = np.concatenate([P[:k + 1], f[None], P[k + 1:]])
    s = M.cum_len(P); idx = k + 1 if (1e-6 < t < 1 - 1e-6) else (k if t <= 1e-6 else k + 1)
    return P, float(s[idx])


def road_sources(mp, layout, warn=None, fit_report=None):
    """the roads the bake draws -> [dict(id, cls, src, pts, width, in_layout, source)]. A road is a line of Map.asset (kind 1 / 2),
    its class the physical attributes of the layout route with the same id. #308 map bake: map308_road_fit.json (an input) names
    the lines that do NOT follow the ground - such a road is drawn along the layout route (the line the terrain bench was cut
    along) or not at all - and the layout routes the map has no line for. map308_check.py reads the same list (AC-O5)."""
    warn = [] if warn is None else warn; routes = layout['routes']; fit = M.road_fit(); lines = None; roads = []; seen = set()
    for ln in mp['lines']:
        if FORBIDDEN_LINE.search(ln['id']):
            raise SystemExit(f"REFUSED: input line '{ln['id']}' is a campaign path (AC-O2)")
        if ln['kind'] not in (1, 2): continue
        seen.add(ln['id'])
        f = fit['lines'].get(ln['id'], dict(source='map')); P = M.dedupe(ln['pts']); a = routes.get(ln['id'])
        if f['source'] == 'none':
            if fit_report is not None: fit_report.append(dict(id=ln['id'], source='none', drawn=False, mapLineM=round(M.poly_len(P), 1)))
            continue
        if f['source'] == 'layoutRoute':
            lines = M.layout_route_lines() if lines is None else lines
            rid = f.get('route', ln['id'])
            if rid not in lines or len(lines[rid]) < 2: raise SystemExit(f"REFUSED: road fit '{ln['id']}': the layout has no route line '{rid}'")
            G = lines[rid]; a = routes.get(rid)
            if fit_report is not None:
                fit_report.append(dict(id=ln['id'], source='layoutRoute', route=rid, drawn=True, mapLineM=round(M.poly_len(P), 1), groundLineM=round(M.poly_len(G), 1),
                                       mapLineOffGroundMaxM=round(float(M.dist_to_polyline(M.resample(P, 4.0), G).max()), 1) if len(P) > 1 else None,
                                       groundOffMapLineMaxM=round(float(M.dist_to_polyline(M.resample(G, 4.0), P).max()), 1) if len(P) > 1 else None))
            P = G
        if len(P) < 2 or M.poly_len(P) < 2.0:
            warn.append(f"road '{ln['id']}' skipped: degenerate ({len(ln['pts'])} points, {M.poly_len(ln['pts']):.1f} m)"); continue
        cls = M.road_class(a['width'], a['vehicle'], a['foot_only']) if a else 7
        Q = M.dp_simplify(P, ROAD_TOL_M)
        roads.append(dict(id=ln['id'], cls=cls, src=P, pts=Q, width=a['width'] if a else 2.4, in_layout=a is not None, source=f['source']))
        if f['source'] == 'map' and a is not None and len(a['bends']) > 1:
            dev = float(np.percentile(M.dist_to_polyline(M.resample(P, 8.0), a['bends']), 95))
            if dev > 25.0: warn.append(f"road '{ln['id']}': Map.asset line and the layout route differ (p95 {dev:.0f} m); the map line is drawn")
    for f in fit['add']:                                                        # a layout route the map has no line for
        rid = f['route']; lines = M.layout_route_lines() if lines is None else lines
        if rid in seen: raise SystemExit(f"REFUSED: road fit add[]: '{rid}' is already a map line")
        if FORBIDDEN_LINE.search(rid): raise SystemExit(f"REFUSED: road fit add[]: '{rid}' is a campaign path (AC-O2)")
        if rid not in lines or rid not in routes or M.poly_len(lines[rid]) < 2.0: raise SystemExit(f"REFUSED: road fit add[]: the layout has no route line '{rid}'")
        a = routes[rid]; P = lines[rid]; seen.add(rid)
        roads.append(dict(id=rid, cls=M.road_class(a['width'], a['vehicle'], a['foot_only']), src=P, pts=M.dp_simplify(P, ROAD_TOL_M), width=a['width'],
                          in_layout=True, source='layoutRoute(add)'))
        if fit_report is not None: fit_report.append(dict(id=rid, source='layoutRoute(add)', route=rid, drawn=True, groundLineM=round(M.poly_len(P), 1)))
    return roads


def bake_strokes(stage, h, wet, mp, layout, log):
    S = Strokes(); warn = []; C = {c['cls']: c for c in CLASSES}
    routes = layout['routes']
    # ---- roads (classes 7-9): the map's lines (and the road fit); the class comes from the layout's physical attributes
    fit_report = []
    roads = road_sources(mp, layout, warn, fit_report)
    crossings = json.loads(M.CROSSINGS.read_text(encoding='utf-8-sig'))['Crossings']
    spans = {}
    for c in crossings:
        r = next((r for r in roads if r['id'] == c['RouteId']), None)
        a = np.array([c['Start']['x'], c['Start']['z']], float); b = np.array([c['End']['x'], c['End']['z']], float)
        if r is None:
            warn.append(f"bridge '{c['Id']}': route '{c['RouteId']}' is not a map line"); continue
        r['pts'], sa = insert_point(r['pts'], a); r['pts'], sb = insert_point(r['pts'], b)
        if sa is not None:                                                      # the second insert may have shifted the first foot
            _, sa = insert_point(r['pts'], a)
        if sa is None or sb is None:
            warn.append(f"bridge '{c['Id']}': deck ends are more than 8 m from the map line '{r['id']}'")
        else:
            spans.setdefault(r['id'], []).append((min(sa, sb), max(sa, sb)))
    for r in roads:                                                             # free ends get a brush cap, junction ends do not
        caps = []
        for end in (r['pts'][0], r['pts'][-1]):
            near = min((float(M.dist_to_polyline(end[None], o['pts'])[0]) for o in roads if o is not r), default=np.inf)
            caps.append(near > 6.0)
        lo, hi = C[r['cls']]['pressure']
        S.add(r['cls'], r['id'], r['pts'], half_w=r['width'] / 2, press=road_pressure(r['pts'], h, r['id'], lo, hi), cap=tuple(caps),
              bridge=spans.get(r['id']))
    # ---- distance ticks (class 11) on the great roads
    for r in roads:
        if r['cls'] != 9: continue
        s = M.cum_len(r['pts']); nr = M.normals(r['pts']); k = 1
        while k * TICK_M < s[-1] - 30.0:
            t = k * TICK_M
            if not any(a - 6 <= t <= b + 6 for a, b in spans.get(r['id'], [])):
                c = np.array([np.interp(t, s, r['pts'][:, 0]), np.interp(t, s, r['pts'][:, 1])])
                n = np.array([np.interp(t, s, nr[:, 0]), np.interp(t, s, nr[:, 1])]); n /= max(math.hypot(*n), 1e-9)
                S.add(11, f"{r['id']}#tick{k}", np.stack([c - n * TICK_HALF_M, c + n * TICK_HALF_M]), cap=(False, False))
            k += 1
    # ---- bridges (class 10)
    decks = []
    for c in crossings:                                                         # two routes over one deck draw one bridge
        a = np.array([c['Start']['x'], c['Start']['z']]); b = np.array([c['End']['x'], c['End']['z']])
        same = next((d for d in decks if max(min(math.hypot(*(a - d[0])), math.hypot(*(a - d[1]))), min(math.hypot(*(b - d[0])), math.hypot(*(b - d[1])))) < 12.0), None)
        if same is not None:
            warn.append(f"bridge '{c['Id']}' shares its deck with '{same[2]}': drawn once"); continue
        decks.append((a, b, c['Id']))
        S.add(10, c['Id'], [a, b], half_w=c['RouteWidth'] / 2 + 1.0, cap=(False, False))
    # ---- site outlines (class 6)
    for ln in mp['lines']:
        if ln['kind'] == 4: S.add(6, ln['id'], ln['pts'], cap=(False, False))
    # ---- water courses (class 1): drainage centre lines; source -> mouth pressure
    dr = {d['id']: d for d in layout['drainages']}
    chains = []
    main = [dr[k] for k in ('main_upper', 'main_middle', 'main_lower') if k in dr]
    if len(main) == 3 and all(math.hypot(*(main[i]['pts'][-1] - main[i + 1]['pts'][0])) < 12.0 for i in range(2)):
        chains.append(('main', np.concatenate([d['pts'] for d in main]), main[0]['half_width'], C[1]['pressure']))
        rest = [d for d in layout['drainages'] if d not in main]
    else:
        rest = layout['drainages']
    for d in rest: chains.append((d['id'], d['pts'], d['half_width'], (C[1]['pressure'][0], .9)))
    for sid, P, hw, (p0, p1) in chains:
        Q = M.resample(M.chaikin(M.dedupe(P), 1), 10.0); s = M.cum_len(Q)
        S.add(1, 'river.' + sid, Q, half_w=hw, press=p0 + (p1 - p0) * (s / max(s[-1], 1e-6)) ** .7, cap=(True, False))
    # ---- built walls (class 5)
    for wid, P in M.wall_lines(stage):
        S.add(5, 'wall.' + wid, M.dp_simplify(M.dedupe(P), .5), half_w=2.0, cap=(False, False))
    # ---- cliffs (classes 3 / 4): only the segments BUILT at this stage
    outer = []; cliff_stats = []; cliff_mask = np.zeros((M.HH, M.WW), bool)
    if M.STAGES[stage]['built']:
        bd = json.loads(M.stage_boundary(stage).read_text(encoding='utf-8'))
        for seg in bd['segments']:
            if seg.get('stage') not in M.STAGES[stage]['built'] or not seg.get('polyline'): continue
            if 'IMPLEMENTED' not in seg.get('status', ''): continue
            P = M.resample(np.asarray(seg['polyline'], float)[:, :2], 8.0); nr = M.normals(P)
            if seg.get('class') == 'outer':
                edge = lambda X, Z: np.minimum(np.minimum(X, M.WORLD_X - X), np.minimum(Z, M.WORLD_Z - Z))
                left = bool(edge(P[:, 0] + nr[:, 0] * 60, P[:, 1] + nr[:, 1] * 60).mean() < edge(P[:, 0] - nr[:, 0] * 60, P[:, 1] - nr[:, 1] * 60).mean())
                S.add(4, 'cliff.' + seg['id'], P, teeth_left=left, cap=(True, True)); outer.append((P, left))
            else:
                yl = np.mean([M.sample_grid(h, P[:, 0] + nr[:, 0] * d, P[:, 1] + nr[:, 1] * d).mean() for d in (24.0, 40.0)])
                yr = np.mean([M.sample_grid(h, P[:, 0] - nr[:, 0] * d, P[:, 1] - nr[:, 1] * d).mean() for d in (24.0, 40.0)])
                S.add(3, 'cliff.' + seg['id'], P, teeth_left=bool(yl < yr), cap=(True, True))
            cliff_stats.append(dict(id=seg['id'], cls=seg.get('class'), m=round(M.poly_len(P), 1)))
            D = M.resample(P, 2.0)
            cliff_mask[np.clip(np.round(D[:, 1] / M.CELL).astype(int), 0, M.HH - 1), np.clip(np.round(D[:, 0] / M.CELL).astype(int), 0, M.WW - 1)] = True
    # ---- ridges (class 2)
    X = np.zeros((M.HH, M.WW), bool)
    for r in roads:
        D = M.resample(r['src'], 2.0)
        X[np.clip(np.round(D[:, 1] / M.CELL).astype(int), 0, M.HH - 1), np.clip(np.round(D[:, 0] / M.CELL).astype(int), 0, M.WW - 1)] = True
    ridges, ridge_stats = RIDGE.extract(h, wet, X, log, cut_mask=cliff_mask)
    for k, r in enumerate(ridges):
        n = len(r['pts']); s = M.cum_len(r['pts']); hsh = M.fnv1a32(f'ridge{k}')
        lo, hi = (1.0, 1.15) if r['rank'] == 1 else C[2]['pressure']
        f = np.clip((lo + hi) / 2 + (hi - lo) / 2 * np.sin(s / 61.0 + (hsh & 0xFF) / 255 * 6.283), lo, hi)
        S.add(2, f"ridge.{r['rank']}.{int(round(r['pts'][0, 0]))}.{int(round(r['pts'][0, 1]))}.{n}", r['pts'], rank=r['rank'], press=f,
              teeth_left=r['teeth_left'], cap=(r['cap_start'], r['cap_end']))
    # ---- order: class, then rank, then id (the draw order of the file)
    S.items.sort(key=lambda it: (it['cls'], it['rank'], it['id']))
    road_stats = {str(c): dict(n=sum(1 for r in roads if r['cls'] == c), km=round(sum(M.poly_len(r['pts']) for r in roads if r['cls'] == c) / 1000, 2))
                  for c in (9, 8, 7)}
    acc = max(float(M.dist_to_polyline(r['src'], r['pts']).max()) for r in roads)
    acc2 = max(float(M.dist_to_polyline(r['pts'], r['src']).max()) for r in roads)
    stats = dict(roads=road_stats, roadFit=fit_report, road_source_to_stroke_max_m=round(acc, 3), road_stroke_point_to_source_max_m=round(acc2, 3),
                 roads_not_in_layout=sorted(r['id'] for r in roads if not r['in_layout']), ridges=ridge_stats, cliffs=cliff_stats,
                 bridges=len(decks), crossings=len(crossings), warnings=warn)
    return S.items, outer, stats


def pack_strokes(items):
    """-> (bytes, counts). Layout: Spec section 7 'map308_strokes.bytes v1'."""
    npts = sum(len(it['pts']) for it in items)
    out = bytearray()
    out += b'MS08' + struct.pack('<I4ffHHII', 1, 0.0, 0.0, M.WORLD_X, M.WORLD_Z, M.BIN_M, M.BINS_X, M.BINS_Z, len(items), npts)
    assert len(out) == 40
    first = 0; firsts = []
    for it in items:
        firsts.append(first)
        out += struct.pack('<BBHIIffI', it['cls'], it['rank'], it['state'], first, len(it['pts']), it['half_w'], it['length'], M.fnv1a32(it['id']))
        first += len(it['pts'])
    for it in items:
        rec = np.zeros(len(it['pts']), dtype=[('x', '<f4'), ('z', '<f4'), ('d', '<f4'), ('p', 'u1'), ('f', 'u1'), ('r', '<u2')])
        rec['x'] = it['pts'][:, 0]; rec['z'] = it['pts'][:, 1]; rec['d'] = it['dist']; rec['p'] = it['press']; rec['f'] = it['flags']
        out += rec.tobytes()
    bins = [[] for _ in range(M.BINS_X * M.BINS_Z)]
    for k, it in enumerate(items):
        bx = np.clip((it['pts'][:, 0] // M.BIN_M).astype(int), 0, M.BINS_X - 1); bz = np.clip((it['pts'][:, 1] // M.BIN_M).astype(int), 0, M.BINS_Z - 1)
        b = bz * M.BINS_X + bx; n = len(b); i = 0
        while i < n:
            j = i
            while j + 1 < n and b[j + 1] == b[i]: j += 1
            a = max(i - 1, 0); e = min(j + 1, n - 1)                              # one point of overlap at each end
            bins[int(b[i])].append((k, firsts[k] + a, e - a + 1))
            i = j + 1
    table = bytearray(); refs = bytearray(); nref = 0
    for lst in bins:
        table += struct.pack('<II', nref, len(lst))
        for r in lst: refs += struct.pack('<III', *r)
        nref += len(lst)
    out += table + refs
    out += struct.pack('<I', zlib.crc32(bytes(out)) & 0xFFFFFFFF)
    per_bin = np.array([sum(r[2] for r in lst) for lst in bins]).reshape(M.BINS_Z, M.BINS_X)
    best = 0; where = (0, 0)
    for z in range(M.BINS_Z - 2):
        for x in range(M.BINS_X - 2):
            v = int(per_bin[z:z + 3, x:x + 3].sum()) * 2
            if v > best: best, where = v, (x, z)
    by_cls = {}
    for it in items:
        d = by_cls.setdefault(str(it['cls']), dict(strokes=0, points=0, km=0.0)); d['strokes'] += 1; d['points'] += len(it['pts']); d['km'] += it['length'] / 1000
    for d in by_cls.values(): d['km'] = round(d['km'], 2)
    counts = dict(strokes=len(items), points=npts, refs=nref, bytes=len(out), byClass=by_cls, maxPointsInBin=int(per_bin.max()),
                  maxVertices9Bins=best, maxVertices9BinsAt=list(where), maxSegmentM=round(max(float(M.seg_len(it['pts']).max()) for it in items), 2))
    return bytes(out), counts


# ---------------------------------------------------------------------------------------------------------------------
# reveal regions (125 x 188, south row first)
# ---------------------------------------------------------------------------------------------------------------------
def bake_regions(stage, h, h_base, wet, log):
    """0 common ground, 1..254 closed region (a cell is revealed only while the player stands in a cell of the same id),
    255 nobody can stand there (revealed by the radius as today). A 32 m cell gets a region id only when EVERY standable 4 m
    node in it belongs to that region - a mixed cell stays 0, so standing at the foot never switches the player to the top."""
    reg = np.zeros((M.FOG_H, M.FOG_W), np.uint8); tops = None; names = {'0': 'common ground', '255': 'no standable ground'}
    slope = np.degrees(np.arctan(np.hypot(*np.gradient(h, M.CELL))))
    if 'carried' in M.STAGES[stage]:
        # #308 map bake: the reach masks of this stage are lost. The regions and the cliff-top cells are the carried files
        # (compute() has checked that they were taken for exactly this height).
        c = M.STAGES[stage]['carried']; man = json.loads(c['manifest'].read_text(encoding='utf-8'))
        raw = c['regions'].read_bytes()
        if len(raw) != M.FOG_W * M.FOG_H or not set(raw) <= {0, 1, 2, 255}: raise SystemExit('REFUSED: ' + M.rel(c['regions']) + ' is not a 125 x 188 region grid')
        bits = np.frombuffer(c['tops'].read_bytes(), np.uint8)
        if bits.size * 8 != M.TH * M.TW: raise SystemExit('REFUSED: ' + M.rel(c['tops']) + f' holds {bits.size} bytes, expected {M.TH * M.TW // 8}')
        tops = np.unpackbits(bits).reshape(M.TH, M.TW).astype(bool)                 # terrain CELLS, south row first
        reg = np.frombuffer(raw, np.uint8).reshape(M.FOG_H, M.FOG_W)
        names.update(man['regionNames'])
        stats = dict(cells={str(int(k)): int(v) for k, v in zip(*np.unique(reg, return_counts=True))}, names=names,
                     carried=dict(fromBundle=man['fromBundle']['notationSha256'][:12], cliffTopCells=int(tops.sum())))
        log(f'  reveal regions (carried): {stats["cells"]}, cliff-top cells {int(tops.sum())}')
        return raw, tops, stats
    if 'reach' in M.STAGES[stage]:
        R = {k: np.load(p) for k, p in M.STAGES[stage]['reach'].items()}
        up = R['UP1']; raised = (h - h_base) > 3.0
        top2 = raised & R['S2'] & ~R['S0'] & ~up
        stand = R['S0'] | R['S2'] | up
        node_reg = np.zeros(h.shape, np.uint8); node_reg[top2] = 2; node_reg[up] = 1
        tops = up | top2
        names.update({'1': f"cliff-top field UP-1 ({M.STAGES[stage]['reach']['UP1'].stem})", '2': 'other cliff tops: raised > 3 m and reachable only after the gate'})
    else:
        stand = ~wet & (slope < 50.0); node_reg = np.zeros(h.shape, np.uint8)
    n = int(M.FOG_CELL / M.CELL)
    for r in range(M.FOG_H):
        for c in range(M.FOG_W):
            st = stand[r * n:(r + 1) * n, c * n:(c + 1) * n]
            if not st.any(): reg[r, c] = 255; continue
            ids = np.unique(node_reg[r * n:(r + 1) * n, c * n:(c + 1) * n][st])
            reg[r, c] = ids[0] if len(ids) == 1 else 0
    stats = dict(cells={str(int(k)): int(v) for k, v in zip(*np.unique(reg, return_counts=True))}, names=names)
    log(f'  reveal regions: {stats["cells"]}')
    return reg.tobytes(), tops, stats


# ---------------------------------------------------------------------------------------------------------------------
# notation
# ---------------------------------------------------------------------------------------------------------------------
def stroke_class_table():
    ref = ZOOM['refMetresPerPx']; out = []

    def tile(rows, widths):
        return [round(1024.0 * (w / 40.0) * U_ASPECT[r] * m, 2) if w > 0 else 0.0 for r, w, m in zip(rows, widths, ref)]
    for c in CLASSES:
        base = c['ranks'][0] if 'ranks' in c else c
        e = dict(name=c['name'], nameKo=c['ko'], atlasRow=base['row'][1], tileMetres=tile(base['row'], base['width'])[1], widthPx=base['width'],
                 knockoutPx=c['knock'], inkAlpha=base['alpha'], minRankByBand=c.get('min_rank', [0, 0, 0, 0]), teeth=base['teeth'],
                 atlasRowByBand=base['row'], tileMetresByBand=tile(base['row'], base['width']), pressureRange=c['pressure'])
        e['class'] = c['cls']
        if c['cls'] == 1: e['drawWhenPhysicalWidthPxBelow'] = 3.2
        if c['cls'] == 10: e['uMode'] = 'stretch'
        if 'ranks' in c:
            e['ranks'] = [dict(rank=r['rank'], widthPx=r['width'], atlasRowByBand=r['row'], tileMetresByBand=tile(r['row'], r['width']),
                               inkAlpha=r['alpha'], teeth=r['teeth']) for r in c['ranks']]
        out.append(e)
    return out


def notation(stage, inputs, outputs, counts, stats, markers, icon_rep):
    glyphs = [dict(id=g[0], cell=i, nameKo=g[1], reserved=g[0] in ART.RESERVED) for i, g in enumerate(ART.GLYPHS)]
    unknown = sorted(m['id'] for m in markers if m['id'] not in MARKER_GLYPHS)
    return dict(
        version=M.CONTRACT_VERSION, spec='SPEC-MAP-OVERHAUL-308', stage=stage,
        world=dict(min=[0, 0], max=[M.WORLD_X, M.WORLD_Z], terrainTexel=M.CELL, fogCell=M.FOG_CELL, fogGrid=[M.FOG_W, M.FOG_H],
                   uv='u = x / 4000, v = z / 6000; the PNG top row is the north edge'),
        bakedFor=M.rel(M.MAP_ASSET), inputs=inputs, outputs=outputs,
        zoomBands=[dict(id=i, maxMetresPerPx=m, refMetresPerPx=r, screens=s) for i, m, r, s in
                   zip(ZOOM['ids'], ZOOM['maxMetresPerPx'], ZOOM['refMetresPerPx'], ZOOM['screens'])],
        strokeFile=dict(magic='MS08', version=1, header=40, strokeRecord=24, pointRecord=16, binRecord=8, refRecord=12, binMetres=M.BIN_M,
                        bins=[M.BINS_X, M.BINS_Z], pressure='width factor = 0.5 + byte / 255 * 0.75',
                        flags=dict(bit0='start cap', bit1='end cap', bit2='on a bridge deck', bit3='teeth / hachures on the LEFT of the direction'),
                        maxSegmentMetres=MAX_SEG_M, order='class, rank, id'),
        strokeAtlas=dict(size=[1024, 512], rowPx=ART.ROW_PX, marginPx=ART.ROW_MARGIN, inkTop=ART.INK_TOP, inkBottom=ART.INK_BOTTOM,
                         channels='A ink, R paper underlay, G B 0', v='row top = the LEFT side of the stroke; flip V when flag bit3 is clear',
                         width='widthPx x pressure maps onto the 40 px ink span (inkTop..inkBottom); axisV sits on the baked centre line',
                         rows=[dict(r, uAspect=U_ASPECT[r['row']]) for r in ART.ATLAS_ROWS],
                         paperPass='draw the paper underlay of every road first, then every ink (no notches at junctions)'),
        strokeClasses=stroke_class_table(), terrain=TERRAIN, values=VALUES,
        glyphs=glyphs, markerGlyphs=MARKER_GLYPHS, kindFallback=KIND_FALLBACK, labelPriority=LABEL_PRIORITY,
        icons=dict(L=dict(file='map308_icons_L.png', cell=64, minStrokePx=5.0, platePx=3.0), S=dict(file='map308_icons_S.png', cell=32, minStrokePx=3.0, platePx=1.5),
                   channels='R ink (the whole figure, tinted by the page), G paper plate (solid silhouette), B second ink (the ink token over R; 0 unless listed in secondInk), A = max(R, G)',
                   secondInk=dict(glyphs=list(ART.SECOND_INK), colour='ink', onDarkPlate='paper',
                                  note='player: R = the cinnabar tuft and, under B, the rest of the brush; B = ferrule, handle stub and a thin ink edge. A reader that ignores B still draws the whole brush in one colour'),
                   report=icon_rep),
        sizes=SIZES, states=[], statesNote='no baked stroke is state-gated: the main scene has no WorldActShortcut; a discovered shortcut is appended at run time as a class 7 stroke',
        legend=LEGEND, frames=FRAMES, regions=stats['regions'], counts=counts,
        stats=dict(terrain=stats['terrain'], strokes=stats['strokes']),
        markersWithoutGlyph=unknown)


# ---------------------------------------------------------------------------------------------------------------------
def gather_inputs(stage):
    paths = [('height', M.STAGES[stage]['height']), ('map', M.MAP_ASSET), ('layoutMain', M.LAYOUT_MAIN), ('layoutCandidate', M.LAYOUT_CAND),
             ('wet', M.WET), ('crossings', M.CROSSINGS), ('rimViews', RIM_VIEWS)]
    paths += [('sheet.' + k, M.A / p) for k, p in M.SHEETS.items()]
    paths += list(zip(M.WALL_ROLES, M.WALL_LAYOUTS)) + [('roadFit', M.ROAD_FIT)]
    g = re.search(r'Locations: \{fileID: \d+, guid: ([0-9a-f]{32})', M.MAP_ASSET.read_text(encoding='utf-8'))
    loc = [p for p in sorted((M.A / 'Art/World/Architecture296/Data').glob('*_Locations.asset'))
           if g and ('guid: ' + g.group(1)) in Path(str(p) + '.meta').read_text(encoding='utf-8')]
    if loc: paths.append(('locations', loc[0]))                                  # the place catalogue Map.asset points at (recorded, not read)
    if M.STAGES[stage]['built']:
        paths += [('baseHeight', M.BASE_HEIGHT), ('boundary', M.stage_boundary(stage))]
        paths += [('reach.' + k, p) for k, p in M.STAGES[stage].get('reach', {}).items()]
        paths += [('carried.' + k, p) for k, p in M.STAGES[stage].get('carried', {}).items()]
    if 'long_wall' in M.STAGES[stage]: paths.append(('walls.' + M.STAGES[stage]['long_wall'].parent.name, M.STAGES[stage]['long_wall']))
    for _, p in paths:
        if not Path(p).exists(): raise SystemExit(f'REFUSED: input missing: {M.rel(p)}')
    # 'role' of the FILE in the bake (the runtime import reads it); 'requires.*' = ledgers that must be applied before the import
    return [dict(role=r, path=M.rel(p), sha256=M.sha256(p)) for r, p in paths] + M.require_entries(stage)


def compute(stage='base', layout_path=None, log=print):
    """-> (dict file name -> bytes, notation dict without outputs, stats). layout_path overrides the layout copy (AC-O2 self test)."""
    if stage not in M.STAGES: raise SystemExit(f"REFUSED: unknown stage '{stage}' (known: {', '.join(M.STAGES)})")
    a, b = M.layout_block_shas(M.LAYOUT_MAIN), M.layout_block_shas(M.LAYOUT_CAND)
    if a != b:
        raise SystemExit('REFUSED: the Main and candidate layout copies disagree in ' + ', '.join(k for k in a if a[k] != b[k]) + ' (Spec 6.4)')
    inputs = gather_inputs(stage)
    for i in inputs:
        if i.get('role') in ('layoutMain', 'layoutCandidate'): i['blocks'] = a
    if 'carried' in M.STAGES[stage]:                                             # the carried files stand for ONE height and one set of files
        c = M.STAGES[stage]['carried']; man = json.loads(c['manifest'].read_text(encoding='utf-8')); sha = {i['role']: i['sha256'] for i in inputs}
        if man.get('id') != 'map308_carried' or man.get('stage') != stage: raise SystemExit('REFUSED: ' + M.rel(c['manifest']) + f' is not the carried record of stage {stage}')
        for role, want in (('height', man['forHeightSha256']), ('baseHeight', man['forBaseHeightSha256']), ('carried.regions', man['files']['regions']['sha256']),
                           ('carried.tops', man['files']['tops']['sha256'])):
            if sha.get(role) != want:
                raise SystemExit(f"REFUSED: the carried reach record of stage {stage} ({M.rel(c['manifest'])}) is for {role} {want[:12]}, on disk {str(sha.get(role))[:12]}: "
                                 'the reach masks must be made again for this height (cliff308_closure.py) or the carried record taken again')
    h = M.load_height(M.STAGES[stage]['height']); h_base = M.load_height(M.BASE_HEIGHT) if M.STAGES[stage]['built'] else h
    wet = M.load_wet(); mp = M.parse_map(); layout = M.parse_layout(layout_path or M.LAYOUT_MAIN)
    log(f"  stage {stage}: height {inputs[0]['sha256'][:12]}, map lines {len(mp['lines'])}, markers {len(mp['markers'])}, routes {len(layout['routes'])}")
    files = {}; stats = {}
    reg_bytes, tops, stats['regions'] = bake_regions(stage, h, h_base, wet, log)
    items, outer, stats['strokes'] = bake_strokes(stage, h, wet, mp, layout, log)
    files['map308_strokes.bytes'], counts = pack_strokes(items)
    trees, tree_counts = M.tree_points(log)
    terrain, stats['terrain'] = bake_terrain(h, wet, trees, tops, outer, log)
    stats['terrain']['trees'] = tree_counts
    files['map308_terrain.png'] = M.png_bytes(terrain)
    files['map308_pattern.png'] = M.png_bytes(ART.pattern())
    files['map308_stroke_atlas.png'] = M.png_bytes(ART.stroke_atlas())
    icon_rep = {}
    for size in ('L', 'S'):
        img, rep = ART.icons(size); files[f'map308_icons_{size}.png'] = M.png_bytes(img); icon_rep[size] = rep
    files['map308_reveal_regions.bytes'] = reg_bytes
    return files, dict(stage=stage, inputs=inputs, counts=counts, stats=stats, markers=mp['markers'], icon_rep=icon_rep), items


def main():
    M.utf8()
    ap = argparse.ArgumentParser(description='SPEC-MAP-OVERHAUL-308 bundle baker (files 1-8)')
    ap.add_argument('--stage', default='base', choices=sorted(M.STAGES))
    ap.add_argument('--out', default=str(M.OUT))
    a = ap.parse_args()
    t0 = time.time()
    print(f'[cartography308] stage {a.stage} -> {M.rel(a.out)}')
    files, meta, _ = compute(a.stage)
    out = Path(a.out); outputs = []
    for name in sorted(files):
        sha = M.write_bytes(out / name, files[name]); outputs.append(dict(file=name, sha256=sha, bytes=len(files[name])))
    note = notation(meta['stage'], meta['inputs'], outputs, meta['counts'], meta['stats'], meta['markers'], meta['icon_rep'])
    sha = M.write_text(out / 'map308_notation.json', M.dumps(note))
    for o in outputs: print(f"  {o['file']:28s} {o['bytes']:>9d} B  {o['sha256']}")
    print(f"  {'map308_notation.json':28s} {(out / 'map308_notation.json').stat().st_size:>9d} B  {sha}")
    for w in meta['stats']['strokes']['warnings']: print('  WARN ' + w)
    c = meta['counts']
    print(f"  strokes {c['strokes']}, points {c['points']} (cap 24000), file {c['bytes']} B (cap 600000), max vertices in 3 x 3 bins {c['maxVertices9Bins']} (cap 6000)")
    print(f'[cartography308] done in {time.time() - t0:.1f} s')


if __name__ == '__main__':
    main()
