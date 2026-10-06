# -*- coding: utf-8 -*-
"""SPEC-HUD-LIQUID-308 numpy twin of UI/InkVessel308 (fragment shader) and LiquidSim308 (C#), and the [T] measurements.

  python Tools/Art/hud308_twin.py            state sheet + report
  python Tools/Art/hud308_twin.py --report   measurements only (no images)
  python Tools/Art/hud308_twin.py --plot     Art/UI308/HUD/hud308_slosh_timeline.png from the stage C# trace (simcheck308.json)

out: Art/UI308/HUD/twin_states_v1.png   cluster states on a bright and a dark ground (x3), composited in sRGB like the #304 mockups
     Art/UI308/HUD/twin_report.json      every [T] acceptance number of the Spec, measured here

D308-11b: (1) the liquid moves only in answer to what the player really did - the twin ports LiquidSim308 (a free damped swing
about the level surface, shoved by Drive / SetValue / Kick), ActionMeter308 and the scripted timeline of AC-H16
(HudLiquid308Timeline) and checks them against the stage C# run (Offline/simcheck308.py); (2) each mark carries the key that
triggers it - frag_key() ports the shader's code 6 quad, the cells are picked from the live .inputactions bindings.

What the twin IS: a line-by-line port. frag() follows the shader's fragment function (same names, same order), sim_step()
follows LiquidSim308.Step. The theme (D308-15: lacquer neck band, najeon lids) and the impact light's side (taken per pixel from
the deploy layer's point, like the live HUD shaders) are part of the port; theme(False, False) gives the look before the theme. What it is NOT: the GPU. It samples mip 0 of the atlas with bilinear filtering, composites in sRGB
(the Unity project is Linear: the shader's _LinearInkGamma / _LinearPaperGamma remap is skipped here, as in a Gamma project), and
it cannot find a shader compile error. Numbers = the profile's code defaults (HudLiquid308ProfileSO) and hud308_atlas.json.
"""
import json, math, os, re, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
TEX = os.path.join(ROOT, 'Tools', 'Unity', 'Stage308_hud', '_ProjectAssets', 'Art', 'UI', 'UI308', 'Textures')
OUT = os.path.join(ROOT, 'Art', 'UI308', 'HUD')
INK = np.array([0x14, 0x14, 0x13]) / 255.0
PAPER = np.array([0xE6, 0xE2, 0xD7]) / 255.0
CINNABAR = np.array([0xB8, 0x39, 0x2B]) / 255.0
ALERT = np.array([0xD6, 0x5A, 0x43]) / 255.0
LACQUER = np.array([0x11, 0x0F, 0x0D]) / 255.0          # ThemeSpec308.Lacquer (theme308_tokens colours.Lacquer)
PORCELAIN = np.array([0xDD, 0xE0, 0xDB]) / 255.0        # theme308_tokens Porcelain / PorcelainShade / InlayDark: the kit's Key.Porcelain,
PORCELAIN_SHADE = np.array([0xBF, 0xC5, 0xC0]) / 255.0  # used here only to MEASURE the alternative keycap (the stage ships the lacquer one)
INLAY_DARK = np.array([0x2A, 0x26, 0x23]) / 255.0

with open(os.path.join(TEX, 'hud308_atlas.json'), encoding='utf-8') as _f:
    AJ = json.load(_f)
ATLAS = np.asarray(Image.open(os.path.join(TEX, 'hud308_atlas.png')).convert('RGBA'), float) / 255.0
_TH = AJ['theme']

# ---- material = shader property defaults = HudLiquid308 hud308-setup with the profile's code defaults
M = dict(
    CellVessel=AJ['cells']['vessel'], CellDodge=AJ['cells']['dodge'], CellJump=AJ['cells']['jump'],
    CellVehicle=AJ['cells']['vehicle'], CellVehicleOut=AJ['cells']['vehicle_out'],
    CellDodgeN=AJ['cells']['dodge_n'], CellJumpN=AJ['cells']['jump_n'], CellVehicleN=AJ['cells']['vehicle_n'],
    CellVehicleOutN=AJ['cells']['vehicle_out_n'],
    QuadRef=[AJ['quad_ref'][0], AJ['quad_ref'][1], AJ['margin_px'], AJ['sdf_range_px']],
    Level=[AJ['level']['floor_uv'], AJ['level']['full_uv'], AJ['level']['wavelength_px'], AJ['level']['neck_top_uv']],
    Glass=[2.6, 2.2, .14, .55], Glass2=[4.0, .72, .95, .96], Pool=[5.0, .22, .06, .18], Meniscus=[1.1, .78, 2.0, 6.0],
    Low=[.22, .22, 3.0, .45], Cost=[6.0, 4.0, 1.2, .9], Pour=[2.0], Impact=[9.0, .5, .10, .6], Mark=[.30, .92, .04],
    Look=[.34, .12, .28, .34], Lens=[4.2, 3.6, .29, 24.0],
    # theme (D308-15) = ThemeSpec308 defaults: collar on, lids on, lid dry alpha, family share green + blue
    Theme=[1.0, 1.0, .30, .85],
    NacreN0=np.array([.7254, .7273, .7280]), NacreA=np.array([.0845, -.0314, -.0034]), NacreB=np.array([.0322, .0003, -.0900]),
    NacreHue=[170.0, 245.0, 320.0, 28.0], NacreArc=[150.0, 340.0, 30.0, .42],
    NacreCut=[_TH['collar']['line_half_px'], _TH['collar']['piece_px']],
    Button=[_TH['button']['radius_px'], AJ['mark']['size_px'] + 2 * AJ['mark']['margin_px'], _TH['button']['cell_px'], _TH['button']['ring_inner_px']],
    # key glyph (D308-11b) = MarkSpec308 defaults + the atlas key block: keycap px, hairline px, lip px, dry symbol alpha /
    # symbol box px, symbol lift px, hairline alpha, lip alpha
    Key=[18.0, 1.0, 2.0, .42], Key2=[AJ['keys']['box_px'], 1.0, .8, .28],
    KeyGrid=AJ['keys']['grid'], KeyCell=AJ['keys']['cell'] + [AJ['keys']['columns']],
    # the shader has no colour of its own for the keycap (face = _Lacquer, hairline / lip / symbol = _PaperColor); the twin
    # keeps them apart so the porcelain alternative can be measured (key_style)
    KeyFace=LACQUER, KeyInk=PAPER, KeyLip=PAPER)
KEY_OFFSET = (-15.0, 26.0)          # MarkSpec308.KeyOffset: keycap centre from the lid centre, design px, y down
MIPS = False        # False = sample mip 0 (the [T] measurements, as before the theme); True = trilinear over a box mip chain
_MIP = [ATLAS]


def use_atlas(a):
    """Swap the atlas the twin samples (a variant for a comparison sheet). Returns the previous one."""
    global ATLAS
    old = ATLAS
    ATLAS = a
    del _MIP[:]
    _MIP.append(a)
    return old


def key_style(name='lacquer'):
    """The keycap material. 'lacquer' = what the stage ships (shader: _Lacquer face, _PaperColor hairline / lip / symbol).
    'porcelain' = the kit's Key.Porcelain look (porcelain face, dark inlay rim and symbol, shade lip), for the comparison only."""
    if name == 'porcelain':
        M.update(KeyFace=PORCELAIN, KeyInk=INLAY_DARK, KeyLip=PORCELAIN_SHADE)
        M['Key2'] = [M['Key2'][0], M['Key2'][1], 1.0, 1.0]
    else:
        M.update(KeyFace=LACQUER, KeyInk=PAPER, KeyLip=PAPER)
        M['Key2'] = [M['Key2'][0], M['Key2'][1], .8, .28]


def theme(collar=True, lids=True):
    """Switch the two theme switches of the material (ThemeSpec308.Collar / Lids). Returns the previous pair."""
    old = (M['Theme'][0] >= .5, M['Theme'][1] >= .5)
    M['Theme'][0], M['Theme'][1] = (1.0 if collar else 0.0), (1.0 if lids else 0.0)
    return old


# ---- profile defaults (HudLiquid308ProfileSO field initializers)
LAYOUT = dict(hp=(64, 818, 114, 150), ink=(158, 880, 100, 132), arc=(167, 925), radius=132, deg=(64, 38, 12), mark=44)
# D308-11b: the liquid has no input of its own. MOTION = MotionInputSpec308: how the player's measured actions are read
MOTION = dict(SimStep=1 / 240, RestTilt=.003, RestTiltSpeed=.01, RestWavePx=.2, RestHold=.25, LevelSnap=.0005, MaxDt=.05, LevelFullDelta=.25)
METER = dict(SpeedDeadzone=.12, StepClamp=14.0, StepBand=.2, TurnDeadzone=150.0, TurnClamp=540.0, TurnLowPassHz=10.0, TurnMpsPer180=3.0, TeleportMetres=5.0,
             TiltSign=-1.0)
TILT_OVERSHOOT_LIMIT = 1.5         # MotionInputSpec308.TiltOvershootLimit: hard cap of the slope = MaxTilt x this
HP = dict(SpringHz=1.8, Damping=.28, TiltPerMps=.014, MaxTilt=.22, WaveMaxPx=3.0, WaveTau=.55, WaveSpeed=7.0,
          WaveExcite=4.0, WavePerMps=.30, HeavePerMps=.40, DrainTau=.07, DrainMinSpeed=.6, FillTau=.22, WetAlpha=.34, WetHold=.45, WetDry=.35,
          FreshSeconds=.45, PourMinDelta=.01, PourFadeSeconds=.08, KickTilt=2.4, KickWavePx=2.0, DropKickTilt=1.6, ReducedSeconds=.12, **MOTION)
INKP = dict(SpringHz=1.1, Damping=.55, TiltPerMps=.010, MaxTilt=.16, WaveMaxPx=1.8, WaveTau=.40, WaveSpeed=4.0,
            WaveExcite=2.0, WavePerMps=.18, HeavePerMps=.24, DrainTau=.11, DrainMinSpeed=.6, FillTau=.30, WetAlpha=.30, WetHold=.6, WetDry=3.4,
            FreshSeconds=.45, PourMinDelta=.01, PourFadeSeconds=.08, KickTilt=1.5, KickWavePx=1.2, DropKickTilt=0.0, ReducedSeconds=.12, **MOTION)


def first_peak_share(damping):
    """LiquidSim308.FirstPeakShare: the share of an impulse's ideal swing (speed / w) the first peak of a damped spring reaches."""
    z = min(max(damping, 0.0), .999)
    if z <= 1e-4: return 1.0
    r = math.sqrt(1 - z * z)
    return math.exp(-z * math.atan2(r, z) / r)


for _p in (HP, INKP):
    _p['TiltLimit'] = _p['MaxTilt'] * TILT_OVERSHOOT_LIMIT
    # LiquidSpec308.Resolve: TiltPerMps is the first PEAK a sudden 1 m/s makes; the kick that reaches it
    _p['TiltKickPerMps'] = _p['TiltPerMps'] * 2 * math.pi * _p['SpringHz'] / first_peak_share(_p['Damping'])
LOW = dict(HpDarken=.22, DangerBeginsAtHp=.35, SpellInkCost=.15, InkDryBand=.02)      # no tremble (D308-11b): value and mark only
IMPACT = dict(LightGain=.85, StuckSeconds=.25, MinInterval=.33, KickFloor=.5)


# ============================================================ shader twin
def saturate(x):
    return np.clip(x, 0.0, 1.0)


def lerp(a, b, t):
    return a + (b - a) * t


def smoothstep(e0, e1, x):
    t = saturate((x - e0) / (e1 - e0))
    return t * t * (3 - 2 * t)


def band(d, lo, hi, aa):
    return saturate((d - lo) / aa + .5) * saturate((hi - d) / aa + .5)


def over(dst, rgb, a):
    a = saturate(a)
    out_a = a + dst[..., 3] * (1 - a)
    rgb = np.broadcast_to(rgb, dst[..., :3].shape)
    out_rgb = (rgb * a[..., None] + dst[..., :3] * (dst[..., 3] * (1 - a))[..., None]) / np.maximum(out_a, 1e-5)[..., None]
    return np.dstack([out_rgb, out_a])


def _level(k):
    while len(_MIP) <= k:
        a = _MIP[-1]
        h, w = a.shape[0] // 2, a.shape[1] // 2
        _MIP.append(a[:h * 2, :w * 2].reshape(h, 2, w, 2, 4).mean(axis=(1, 3)))
    return _MIP[k]


def _bilinear(tex, cell, u, v):
    ah, aw = tex.shape[:2]
    x = (cell[0] + u * cell[2]) * aw - .5
    y = (1 - (cell[1] + v * cell[3])) * ah - .5
    x0 = np.clip(np.floor(x).astype(int), 0, aw - 1); x1 = np.clip(x0 + 1, 0, aw - 1)
    y0 = np.clip(np.floor(y).astype(int), 0, ah - 1); y1 = np.clip(y0 + 1, 0, ah - 1)
    fx = (x - np.floor(x))[..., None]; fy = (y - np.floor(y))[..., None]
    return (tex[y0, x0] * (1 - fx) + tex[y0, x1] * fx) * (1 - fy) + (tex[y1, x0] * (1 - fx) + tex[y1, x1] * fx) * fy


def tex2d(cell, u, v, lod=0.0):
    """Sample of the atlas at cell.xy + uv * cell.zw (Unity uv: v up; uv already clamped by the caller's saturate).
    lod 0 = bilinear at mip 0 (the measurements); lod > 0 = trilinear over a box-filtered mip chain (what the GPU does when the
    atlas is minified: 2.5 - 4 atlas px per screen px at 1080p)."""
    u, v = saturate(u), saturate(v)
    if lod <= 0:
        return _bilinear(ATLAS, cell, u, v)
    k = int(math.floor(lod))
    f = lod - k
    a = _bilinear(_level(k), cell, u, v)
    return a if f < 1e-6 else a * (1 - f) + _bilinear(_level(k + 1), cell, u, v) * f


def nacre(hue_deg):
    """SPEC-UI-THEME-308 section 1 (shader Nacre()): rgb = N0 + A cos h + B sin h, hue kept on the theme's arc. sRGB code values
    (the twin composites in sRGB, so FromCode is the identity here)."""
    h = np.radians(np.clip(hue_deg, M['NacreArc'][0], M['NacreArc'][1]))
    return saturate(M['NacreN0'][None, None, :] + M['NacreA'][None, None, :] * np.cos(h)[..., None] + M['NacreB'][None, None, :] * np.sin(h)[..., None])


def frag(uvx, uvy, tint, liquid4, marks4, extra4, aa, aa_m=None, to_quad=None, lod=0.0):
    """Port of frag() in InkVessel308.shader. Returns straight-alpha RGBA and a dict of intermediate coverages.
    aa / aa_m = design px per screen px of the vessel quad / the mark quad (fwidth(uv.y) * _QuadRef.y / _Button.y).
    to_quad = (x, y) arrays: the vector from every pixel to the impact point in the quad's own frame (x right, y up, design
    px), what the shader gets from _OhImpactHudPoint308 and the uv derivatives; None = no live impact (_OhImpact308.z = 0)."""
    code = extra4[0]
    is_mark = 1.0 if code >= 1.5 else 0.0
    lid = is_mark * (1.0 if M['Theme'][1] >= .5 else 0.0)
    n_ = 'N' if lid else ''
    cell = M['CellVessel']
    if code >= 1.5: cell = M['CellDodge' + n_]
    if code >= 2.5: cell = M['CellJump' + n_]
    if code >= 3.5: cell = M['CellVehicle' + n_]
    if code >= 4.5: cell = M['CellVehicleOut' + n_]
    B = M['Button']
    if lid:
        k = B[1] / max(B[2], 1.0)
        tex = tex2d(cell, (uvx - .5) * k + .5, (uvy - .5) * k + .5, lod)
    else:
        tex = tex2d(cell, uvx, uvy, lod)
    aa_m = aa if aa_m is None else aa_m
    paper, ink, lacquer = PAPER, INK, LACQUER
    strength = float(np.clip(extra4[1], 0, 1))
    if to_quad is None or strength <= 0:
        lx = ly = np.zeros(uvx.shape)
    else:
        tl = np.hypot(to_quad[0], to_quad[1])
        ok = tl > 1e-4
        lx = np.where(ok, to_quad[0] / np.maximum(tl, 1e-9), 0.0) * strength
        ly = np.where(ok, to_quad[1] / np.maximum(tl, 1e-9), 1.0) * strength
    W, H = M['QuadRef'][0], M['QuadRef'][1]
    qx = (uvx - .5) * W
    qy = uvy * H
    d = (tex[..., 0] - .5) * 2.0 * M['QuadRef'][3]
    d_in = d + M['Glass2'][0]
    inside = saturate(.5 - d / aa)
    cavity = saturate(.5 - d_in / aa)
    is_hp = (1.0 if code >= .5 else 0.0) * (1 - is_mark)
    is_ink = 1.0 if code < .5 else 0.0
    low = float(np.clip(marks4[3], 0, 1))
    level = float(np.clip(liquid4[0], 0, 1))
    has_liquid = 1.0 if level >= .004 else 0.0
    wall = saturate(1.0 + d_in / max(M['Meniscus'][3], .01))
    climb = M['Meniscus'][2] * wall * wall
    surf = lerp(M['Level'][0], M['Level'][1], level) * H + liquid4[1] * qx \
        + liquid4[2] * np.sin(qx / max(M['Level'][2], 1.0) * 6.2831853 + liquid4[3]) + climb
    below = saturate((surf - qy) / aa + .5)
    gx, gy = qx / (W * W), (qy - .5 * H) / (H * H)
    gl = np.maximum(np.hypot(gx, gy), 1e-6)
    facing = (gx * lx + gy * ly) / gl
    lit_side, far_side = saturate(facing), saturate(-facing)
    liq = lerp(tint, ink, is_hp * low * M['Low'][0])

    o = np.zeros(uvx.shape + (4,))
    rim_col = lerp(paper[None, None, :], ink[None, None, :], (M['Impact'][3] * far_side)[..., None])
    o = over(o, rim_col, band(d, 0.0, M['Glass'][1], aa) * M['Glass2'][1])                       # 1 paper rim
    o = over(o, paper, inside * M['Glass'][2])                                                    # 2 empty glass wash
    o = over(o, paper, band(d, -M['Glass2'][0] - .2, -1.2, aa) * M['Look'][0])                    # 2b glass thickness (paper value)
    mark_y = lerp(M['Level'][0], M['Level'][1], float(np.clip(marks4[0], 0, 1))) * H
    wet_span = np.maximum(mark_y + climb - surf, 1.0)
    wet_fade = lerp(1.0, .4, saturate((qy - surf) / wet_span))
    wet_a = max(marks4[1], 0.0) * saturate((mark_y + climb - qy) / aa + .5) * (1 - below) * cavity * lerp(.55, 1.0, tex[..., 3]) * wet_fade
    o = over(o, liq, wet_a)                                                                       # 3 wet film (not remapped)
    tide_top = float(np.clip(marks4[2], 0, 1))
    ring = saturate(1.0 + d_in / 7.0)
    tide = np.zeros(uvx.shape)
    for k, f in enumerate((1.0, .72, .44)):
        ty = lerp(M['Level'][0], M['Level'][1], tide_top * f) * H + climb
        tide = tide + saturate(1.0 - np.abs(qy - ty) / 1.2) * (1.0 if M['Low'][2] >= k + .5 else 0.0)
    tide_a = is_hp * min(1.0, low * 6.0) * M['Low'][1] * saturate(tide) * ring * (1 - below) * cavity
    o = over(o, lerp(tint, ink, .5), tide_a)                                                      # 4 tide rings
    pool = saturate(1.0 + d_in / max(M['Pool'][0], .01))
    body = liq[None, None, :] * (1 - M['Pool'][1] * pool)[..., None]
    body = lerp(body, paper[None, None, :], (M['Pool'][2] * (1 - pool))[..., None])
    base_y = lerp(M['Level'][0], M['Level'][1], level) * H
    body = body * (1.0 - M['Look'][1] * saturate((base_y - qy) / max(base_y - M['Level'][0] * H, 6.0)))[..., None]   # thick ink settles
    fresh = float(np.clip(-marks4[1], 0, 1)) * saturate((qy - mark_y) / aa + .5)
    body = lerp(body, paper[None, None, :], (M['Pool'][3] * fresh)[..., None])
    across = saturate(qx / (.5 * W) * lx + (qy - .5 * H) / (.5 * H) * ly)
    body = lerp(body, paper[None, None, :], (M['Impact'][2] * across)[..., None])
    body_cov = cavity * below * has_liquid
    split = smoothstep(M['Low'][3] - .06, M['Low'][3] + .06, tex[..., 3])
    body_cov = body_cov * lerp(1.0, split, is_ink * low)
    o = over(o, body, body_cov * M['Glass2'][3])                                                  # 5 liquid body
    lens_h = lerp(M['Lens'][1], M['Lens'][0], is_hp) * np.sqrt(saturate(-d_in / max(M['Lens'][3], 1.0)))
    lens_on = cavity * has_liquid * (1.0 - is_ink * low) * saturate(lens_h)
    lens_col = lerp(liq, paper, M['Lens'][2])
    lens_a = lens_on * (1 - below) * saturate((surf + lens_h - qy) / aa + .5) * .92
    o = over(o, lens_col, lens_a)                                                                 # 5b far half of the surface (lens)
    o = over(o, paper, lens_on * saturate(1.0 - np.abs(qy - surf - lens_h) / .8) * (lens_h >= .6) * M['Look'][3])
    cost_level = abs(marks4[2])
    cost_y = lerp(M['Level'][0], M['Level'][1], min(1.0, cost_level)) * H
    period = max(M['Cost'][0] + M['Cost'][1], .5)
    dash = ((qx / period + .5) % 1.0 <= M['Cost'][0] / period).astype(float)
    cost_a = is_ink * (1.0 if cost_level >= 1e-4 else 0.0) * cavity * saturate(1.0 - np.abs(qy - cost_y) / max(M['Cost'][2], .1)) * dash * M['Cost'][3]
    o = over(o, ALERT if marks4[2] <= -1e-4 else paper, cost_a)                                    # 6 cost line
    thread = saturate((.5 * M['Pour'][0] - np.abs(qx)) / aa + .5) * saturate((qy - surf) / aa + .5) * saturate((M['Level'][3] * H - qy) / aa + .5)
    thread_a = float(np.clip(extra4[3], 0, 1)) * thread * inside * (1 - is_mark)
    o = over(o, liq, thread_a)                                                                    # 7 pour thread
    surf_a = cavity * has_liquid * saturate(1.0 - np.abs(qy - surf) / max(M['Meniscus'][0], .1)) * M['Meniscus'][1]
    o = over(o, ink, inside * saturate(1.0 + d / max(M['Impact'][0], .01)) * far_side * M['Impact'][1])   # 8 impact shadow (under the line)
    o = over(o, paper, surf_a)                                                                    # 9 surface line
    o = over(o, paper, tex[..., 2] * M['Glass'][3] * inside)                                      # 10 highlight stroke
    glass_col = lerp(ink[None, None, :], paper[None, None, :], lit_side[..., None])
    press = 1.0 + M['Look'][2] * (gx / gl * -.469 + gy / gl * -.883)                              # brush pressure on the line
    o = over(o, glass_col, band(d, -M['Glass'][0] * press, 0.0, aa) * M['Glass2'][2])             # 11 glass line
    # 12 theme: lacquer neck band + cut-shell line (atlas G: 0 none, .5 lacquer, 1 nacre)
    lacquer_a = saturate(tex[..., 1] * 2.0) * M['Theme'][0] * (1 - is_mark)
    nacre_a = saturate(tex[..., 1] * 2.0 - 1.0) * M['Theme'][0] * (1 - is_mark)
    o = over(o, lacquer, lacquer_a)
    piece = np.floor((qx + M['NacreCut'][0]) / max(M['NacreCut'][1], .5)) + 2.0 * is_ink
    family = (piece * .618034 + .31) % 1.0
    hue = np.where(family >= M['Theme'][3], M['NacreHue'][2], np.where(family >= M['NacreArc'][3], M['NacreHue'][1], M['NacreHue'][0])) \
        + M['NacreHue'][3] * np.sin(qx * (6.2831853 / max(M['NacreArc'][2], 1.0)) + piece * 2.4)
    collar_shell = lerp(nacre(hue), ink[None, None, :], (M['Impact'][3] * far_side)[..., None])
    o = over(o, collar_shell, nacre_a)

    # mark: ink glyph + paper rim
    wet = float(np.clip(liquid4[0], 0, 1))
    refill = float(np.clip(extra4[3], 0, 1))
    rewet = (1.0 - smoothstep(refill - M['Mark'][2], refill + M['Mark'][2], tex[..., 2])) * (1.0 if refill >= 1e-3 else 0.0)
    wetness = saturate(np.maximum(wet, rewet))
    dry_a = saturate(M['Mark'][0] * lerp(.55, 1.45, tex[..., 3]))
    facing_m = (uvx - .5) * 2.0 * lx + (uvy - .5) * 2.0 * ly
    mark_rim = lerp(paper[None, None, :], ink[None, None, :], (M['Impact'][3] * saturate(-facing_m))[..., None])
    m = np.zeros(uvx.shape + (4,))
    m = over(m, mark_rim, saturate(tex[..., 1] * (1.0 + saturate(facing_m))) * M['Mark'][1])
    m = over(m, tint, tex[..., 0] * lerp(dry_a, 1.0, wetness))
    # mark as a najeon lid (theme): lacquer disc, the lit edge on an impact, the baked shell (rim line + pictogram pieces)
    pmx, pmy = (uvx - .5) * B[1], (uvy - .5) * B[1]
    rm = np.hypot(pmx, pmy)
    disc = saturate((B[0] - rm) / aa_m + .5)
    rim_line = saturate((rm - B[3]) / aa_m + .5)
    is_jump = 1.0 if 2.5 <= code < 3.5 else 0.0
    order = saturate((pmy if is_jump else pmx) / (2.0 * max(B[3], 1.0)) + .5)
    reseat = (1.0 - smoothstep(refill - M['Mark'][2], refill + M['Mark'][2], order)) * (1.0 if refill >= 1e-3 else 0.0)
    seated = saturate(np.maximum(wet, reseat))
    facing_l = (pmx * lx + pmy * ly) / np.maximum(rm, 1e-3)
    shell = lerp(tex[..., :3], ink[None, None, :], (M['Impact'][3] * saturate(-facing_l))[..., None])
    shell_a = tex[..., 3] * disc * lerp(lerp(M['Theme'][2], 1.0, seated), 1.0, rim_line)
    n = np.zeros(uvx.shape + (4,))
    n = over(n, lacquer, disc)
    n = over(n, paper, band(rm, B[0] - M['Glass'][1], B[0], aa_m) * saturate(facing_l))
    n = over(n, shell, shell_a)
    if lid: m = n
    res = m if is_mark else o
    res = np.dstack([saturate(res[..., :3]), saturate(res[..., 3])])
    return res, dict(surf=surf, qx=qx, qy=qy, cavity=cavity, below=below, body_cov=body_cov, inside=inside, d=d, wet_a=wet_a,
                     surf_a=surf_a, lens_a=lens_a, lacquer_a=lacquer_a, nacre_a=nacre_a, thread_a=thread_a, disc=disc,
                     shell_a=shell_a, rim_line=rim_line, light=(lx, ly))


def frag_key(uvx, uvy, liquid4, extra4, aa_k, lod=0.0):
    """Port of the key glyph branch of frag() in InkVessel308.shader (code 6, D308-11b): a lacquer keycap, a paper hairline,
    a paper lip and one symbol cell of the atlas key block. liquid4[0] = wetness of its mark, extra4[2] = the cell."""
    K, K2 = M['Key'], M['Key2']
    key_index = math.floor(extra4[2] + .5)
    key_row = math.floor((key_index + .5) / max(M['KeyCell'][2], 1.0))
    key_col = key_index - key_row * M['KeyCell'][2]
    pkx, pky = (uvx - .5) * K[0], (uvy - .5) * K[0]
    kux, kuy = pkx / max(K2[0], 1.0) + .5, (pky - K2[1]) / max(K2[0], 1.0) + .5
    key_in = ((kux >= 0) & (kux <= 1) & (kuy >= 0) & (kuy <= 1)).astype(float)
    cell = [M['KeyGrid'][0] + key_col * M['KeyGrid'][2], M['KeyGrid'][1] - key_row * M['KeyGrid'][3], M['KeyCell'][0], M['KeyCell'][1]]
    tex = tex2d(cell, kux, kuy, lod)
    wet = float(np.clip(liquid4[0], 0, 1))
    dk = np.maximum(np.abs(pkx), np.abs(pky)) - .5 * K[0]
    face_k = saturate(.5 - dk / aa_k)
    inner_k = saturate(.5 - (dk + K[1]) / aa_k)
    lip_k = inner_k * saturate((K[1] + K[2] - .5 * K[0] - pky) / aa_k + .5)
    glyph_a = tex[..., 3] * key_in * inner_k * lerp(K[3], 1.0, wet)
    kq = np.zeros(uvx.shape + (4,))
    kq = over(kq, M['KeyFace'], face_k)
    kq = over(kq, M['KeyLip'], lip_k * K2[3])
    kq = over(kq, M['KeyInk'], (face_k - inner_k) * K2[2])
    kq = over(kq, M['KeyInk'], glyph_a)
    kq = np.dstack([saturate(kq[..., :3]), saturate(kq[..., 3])])
    return kq, dict(face=face_k, inner=inner_k, lip=lip_k, glyph=tex[..., 3] * key_in * inner_k, glyph_a=glyph_a, rim=face_k - inner_k)


def key_element(rect, cell, wet, scale=1.0, cluster=1.0, origin=(64.0, 1016.0)):
    """The key glyph quad on the screen pixel grid. rect = the keycap, design px (x, y, w, h), top-left origin."""
    x, y, w, h = rect
    sx = lambda v: (origin[0] + (v - origin[0]) * cluster) * scale
    sy = lambda v: (origin[1] + (v - origin[1]) * cluster) * scale
    x0, y0, x1, y1 = sx(x), sy(y), sx(x + w), sy(y + h)
    px0, py0, px1, py1 = int(math.floor(x0)), int(math.floor(y0)), int(math.ceil(x1)), int(math.ceil(y1))
    ys, xs = np.mgrid[py0:py1, px0:px1]
    uvx = (xs + .5 - x0) / (x1 - x0)
    uvy = 1.0 - (ys + .5 - y0) / (y1 - y0)
    aa_k = max(M['Key'][0] / (y1 - y0), 1e-3)               # fwidth(uv.y) * _Key.x
    lod = 0.0
    if MIPS:
        span = (y1 - y0) * M['Key2'][0] / M['Key'][0]       # screen px the symbol box covers
        lod = max(0.0, math.log2(max(M['KeyCell'][1] * ATLAS.shape[0] / span, 1e-6)))
    rgba, aux = frag_key(uvx, uvy, [wet, 0, 0, 0], [6, 0, cell, 0], aa_k, lod)
    ok = ((uvx >= 0) & (uvx <= 1) & (uvy >= 0) & (uvy <= 1)).astype(float)
    rgba[..., 3] *= ok
    aux.update(px0=px0, py0=py0)
    return rgba, px0, py0, aux


def key_rect(i):
    """The keycap of mark i (HudVessels308: InkVesselGraphic308.ConfigureKey with MarkSpec308.KeyOffset / KeySize)."""
    r = mark_rect(i)
    cx, cy = r[0] + r[2] / 2 + KEY_OFFSET[0], r[1] + r[3] / 2 + KEY_OFFSET[1]
    k = M['Key'][0]
    # HudVessels308.KeyOffset: the keycap (straight edges, 1 px hairline) sits on whole design px
    return (float(round(cx - k / 2)), float(round(cy - k / 2)), k, k)


def element(rect, code, tint, liquid4, marks4, extra4, scale=1.0, cluster=1.0, origin=(64.0, 1016.0), point=None):
    """One quad on the screen pixel grid. rect = design px (x, y, w, h), top-left origin. Returns (rgba, x0, y0, aux).
    point = the impact point in SCREEN px of this render (x right, y down; None = no live impact): the direction of the light is
    taken per pixel from it, the way the shader takes it from _OhImpactHudPoint308 (extra4[1] carries the strength)."""
    x, y, w, h = rect
    if code < 1.5:
        o = AJ['margin_px'] / AJ['ref_rect'][1] * h           # InkVesselGraphic308 outset
    else:
        o = AJ['mark']['margin_px'] / AJ['mark']['size_px'] * h   # marks: the cell's margin (paper rim room)
    x, y, w, h = x - o, y - o, w + 2 * o, h + 2 * o
    k = scale * cluster
    sx = lambda v: (origin[0] + (v - origin[0]) * cluster) * scale
    sy = lambda v: (origin[1] + (v - origin[1]) * cluster) * scale
    x0, y0, x1, y1 = sx(x), sy(y), sx(x + w), sy(y + h)
    px0, py0, px1, py1 = int(math.floor(x0)), int(math.floor(y0)), int(math.ceil(x1)), int(math.ceil(y1))
    ys, xs = np.mgrid[py0:py1, px0:px1]
    uvx = (xs + .5 - x0) / (x1 - x0)
    uvy = 1.0 - (ys + .5 - y0) / (y1 - y0)
    aa = max(M['QuadRef'][1] / (y1 - y0), 1e-3)             # fwidth(uv.y) * _QuadRef.y
    aa_m = max(M['Button'][1] / (y1 - y0), 1e-3)            # fwidth(uv.y) * _Button.y
    to_quad = None
    if point is not None:
        # pixel-frame vector to the impact point -> the quad's frame through d(uv)/d(pixel): ddx(uv.x) = 1 / width px,
        # ddy(uv.y) = -1 / height px on a target whose rows run down (the sign is carried by the derivative, never assumed)
        quad_px = (M['QuadRef'][0], M['QuadRef'][1]) if code < 1.5 else (M['Button'][1], M['Button'][1])
        to_quad = ((point[0] - (xs + .5)) / (x1 - x0) * quad_px[0], -(point[1] - (ys + .5)) / (y1 - y0) * quad_px[1])
    lod = 0.0
    if MIPS:
        lid = code >= 1.5 and M['Theme'][1] >= .5
        cell = M['CellVessel'] if code < 1.5 else M['CellDodgeN'] if lid else M['CellDodge']
        span = (y1 - y0) * (M['Button'][2] / M['Button'][1] if lid else 1.0)           # screen px one cell covers
        lod = max(0.0, math.log2(max(cell[3] * ATLAS.shape[0] / span, 1e-6)))
    rgba, aux = frag(uvx, uvy, tint, liquid4, marks4, extra4, aa, aa_m, to_quad, lod)
    ok = ((uvx >= 0) & (uvx <= 1) & (uvy >= 0) & (uvy <= 1)).astype(float)
    rgba[..., 3] *= ok
    aux.update(px0=px0, py0=py0, design_px=(y1 - y0) / M['QuadRef'][1])
    return rgba, px0, py0, aux


def mark_rect(i):
    a = math.radians(LAYOUT['deg'][i])
    cx = LAYOUT['arc'][0] + LAYOUT['radius'] * math.cos(a)
    cy = LAYOUT['arc'][1] - LAYOUT['radius'] * math.sin(a)
    s = LAYOUT['mark']
    return (cx - s / 2, cy - s / 2, s, s)


def far_point(rect, angle, scale=1.0):
    """A point far away from the element in a direction (radians, counter-clockwise from +x on screen): a directional light."""
    cx, cy = (rect[0] + rect[2] / 2) * scale, (rect[1] + rect[3] / 2) * scale
    return (cx + 1e6 * math.cos(angle), cy - 1e6 * math.sin(angle))


def low_hp(hp):
    th = LOW['DangerBeginsAtHp']
    return 1 - hp / th if hp < th else 0.0


def low_ink(ink):
    return float(np.clip((LOW['SpellInkCost'] - ink) / LOW['InkDryBand'], 0, 1))


SHOW_KEYS = True                    # MarkSpec308.ShowKeys (D308-11b)


def cluster(hp=.62, ink=.44, hp_tilt=0.0, ink_tilt=0.0, hp_wave=0.0, ink_wave=0.0, phase=(.7, 2.1), hp_wet=None, ink_wet=None,
            ink_fresh=None, pour=0.0, cost=0.0, marks=((1, 0, 2), (1, 0, 3), (1, 0, 4)), impact=None, scale=1.0, cluster_scale=1.0,
            ink_level=None, keys=None):
    """The five elements as HudVessels308.Push builds them. marks = (wet01, fill, glyph code) x 3. impact = ((x, y) design px
    of the 1920 x 1080 frame, light): the strength goes into uv3.y, the point is what the deploy layer's global would hold.
    keys = the key glyph cells of the three marks (None = the live bindings, False = no keys): each is the second quad of its
    mark's graphic, hidden with the mark and dimmed with it."""
    if keys is None: keys = live_key_cells() if SHOW_KEYS else False
    point, light = impact if impact else (None, 0.0)
    light = min(1.0, light * IMPACT['LightGain'])
    pt = None if point is None else (point[0] * scale, point[1] * scale)
    els = []
    r = LAYOUT['hp']
    b = [hp_wet[0], hp_wet[1], LOW['DangerBeginsAtHp'], low_hp(hp)] if hp_wet else [0, 0, LOW['DangerBeginsAtHp'], low_hp(hp)]
    els.append(element(r, 1, CINNABAR, [hp, hp_tilt, hp_wave, phase[0] if hp_wave > 0 else 0], b, [1, light, 0, 0], scale, cluster_scale, point=pt))
    r = LAYOUT['ink']
    third = 0.0
    if cost > 1e-4: third = max(.001, ink - cost) if ink - cost >= 0 else -.012
    if ink_fresh is not None: b = [ink_fresh, -1.0, third, low_ink(ink)]
    elif ink_wet: b = [ink_wet[0], ink_wet[1], third, low_ink(ink)]
    else: b = [0, 0, third, low_ink(ink)]
    lvl = ink if ink_level is None else ink_level
    els.append(element(r, 0, INK, [lvl, ink_tilt, ink_wave, phase[1] if ink_wave > 0 else 0], b, [0, light, 0, pour], scale, cluster_scale, point=pt))
    for i, (wet, fill, glyph) in enumerate(marks):
        if glyph is None: continue
        r = mark_rect(i)
        els.append(element(r, glyph, INK, [wet, 0, 0, 0], [0, 0, 0, 0], [glyph, light, 0, 0 if wet >= 1 else fill], scale, cluster_scale, point=pt))
        if keys and keys[i] is not None and keys[i] >= 0:
            els.append(key_element(key_rect(i), keys[i], wet, scale, cluster_scale))
    return els


def paste(dst, els):
    for rgba, x0, y0, _ in els:
        h, w = rgba.shape[:2]
        ya, xa = max(0, y0), max(0, x0)
        yb, xb = min(dst.shape[0], y0 + h), min(dst.shape[1], x0 + w)
        src = rgba[ya - y0:yb - y0, xa - x0:xb - x0]
        a = src[..., 3:4]
        dst[ya:yb, xa:xb] = dst[ya:yb, xa:xb] * (1 - a) + src[..., :3] * a          # sRGB composite (the #304 mockup convention)


# ============================================================ key glyphs: the real bindings -> atlas cells (HudKeyGlyph308 / HudKeyBinding308)
ASSETS = os.path.join(ROOT, 'Oheangbu', 'Assets')
KEY_NAMES = AJ['keys']['names']


def key_cell_by_name(name):
    """HudKeyGlyph308.CellByName."""
    if not name: return -1
    if len(name) == 1:
        c = name.upper()
        if 'A' <= c <= 'Z': return ord(c) - ord('A')
        if '0' <= c <= '9': return 26 + ord(c) - ord('0')
        return -1
    return KEY_NAMES.index(name) if name in KEY_NAMES else -1


def key_table():
    """MarkSpec308.KeyGlyphs default = HudKeyGlyph308.DefaultTable(), read from the stage source (data, not a second copy)."""
    with open(os.path.join(ROOT, 'Tools', 'Unity', 'Stage308_hud', 'App', 'World', 'UI', 'HudKeyGlyph308.cs'), encoding='utf-8-sig') as f:
        src = f.read()
    body = src[src.index('DefaultTable()'):]
    body = body[:body.index('};')]
    rows = re.findall(r'new KeyGlyphRow308[(]"([^"]+)", (?:nameof[(](\w+)[)]|"(\w+)")[)]', body)
    return [(path, a or b) for path, a, b in rows]


def key_cell(path, table):
    """HudKeyGlyph308.Cell: a row of the table wins, a keyboard key named by one letter / digit is that cell, else the generic key."""
    if not path: return -1
    for row_path, name in table:
        if row_path.lower() == path.lower():
            c = key_cell_by_name(name)
            return c if c >= 0 else key_cell_by_name('Generic')
    if path.lower().startswith('<keyboard>/') and len(path) == len('<Keyboard>/') + 1:
        c = key_cell_by_name(path[-1])
        if c >= 0: return c
    return key_cell_by_name('Generic')


_LIVE = {}


def live_bindings():
    """What the game is bound to right now: Gameplay Dodge / Jump of the .inputactions asset (the keyboard / mouse binding, as
    HudKeyBinding308.Path picks it when no gamepad was used last), the vehicle key from the profile default."""
    if 'b' not in _LIVE:
        with open(os.path.join(ASSETS, 'InputSystem_Actions.inputactions'), encoding='utf-8-sig') as f:
            data = json.load(f)
        game = next(m for m in data['maps'] if m['name'] == 'Gameplay')

        def first(action):
            rows = [b['path'] for b in game['bindings'] if b.get('action') == action and not b.get('isComposite')]
            desk = [q for q in rows if q.startswith(('<Keyboard>', '<Mouse>'))]
            return (desk or rows or [None])[0]
        with open(os.path.join(ROOT, 'Tools', 'Unity', 'Stage308_hud', 'App', 'World', 'UI', 'HudLiquid308ProfileSO.cs'), encoding='utf-8-sig') as f:
            prof = f.read()
        _LIVE['b'] = dict(dodge=first('Dodge'), jump=first('Jump'), vehicle=re.search(r'VehicleKeyPath = "([^"]+)"', prof).group(1),
                          vehicle_action=re.search(r'VehicleAction = "([^"]+)"', prof).group(1),
                          gameplay_actions=[a['name'] for a in game['actions']],
                          gamepad_bindings_in_gameplay=[b['path'] for b in game['bindings'] if b['path'].startswith('<Gamepad>')])
    return _LIVE['b']


def live_key_cells():
    """The cells the three marks show with the live bindings: (dodge, jump, vehicle)."""
    b, t = live_bindings(), key_table()
    return (key_cell(b['dodge'], t), key_cell(b['jump'], t), key_cell(b['vehicle'], t))


# ============================================================ LiquidSim308 twin (D308-11b: no input but the player's actions)
def new_state(value):
    return dict(Value=value, Level=value, Tilt=0.0, TiltSpeed=0.0, WavePx=0.0, Phase=0.0, WetTop=0.0, WetAge=-1.0,
                FreshBottom=0.0, FreshAge=-1.0, Pour=0.0, Pouring=False, ReducedSpan=0.0, Accumulator=0.0, DropSide=False, ShoveHold=0.0, Still=True)


def mark_fresh(s, p, frm):
    live = 0 <= s['FreshAge'] < p['FreshSeconds']
    frm = min(1.0, max(0.0, frm))
    s['FreshBottom'] = min(s['FreshBottom'], frm) if live else frm
    s['FreshAge'] = 0.0; s['Still'] = False


def kick(s, p, tilt_speed, wave_px):
    if tilt_speed != tilt_speed or wave_px != wave_px: return          # NaN
    if tilt_speed == 0 and wave_px <= 0: return
    s['TiltSpeed'] += tilt_speed
    s['WavePx'] = min(p['WaveMaxPx'], s['WavePx'] + max(0.0, wave_px))
    s['ShoveHold'] = max(0.0, p['RestHold']); s['Still'] = False          # a shove under the rest thresholds is kept: more may follow


def set_value(s, p, value):
    value = min(1.0, max(0.0, value))
    delta = value - s['Value']
    if delta == 0: return
    strength = min(1.0, max(0.0, abs(delta) / max(p['LevelFullDelta'], 1e-4)))
    if delta < 0:
        if s['Level'] > value:
            live = 0 <= s['WetAge'] < p['WetHold'] + p['WetDry']
            s['WetTop'] = max(s['WetTop'], s['Level']) if live else s['Level']
            s['WetAge'] = 0.0
        s['Pouring'] = False
        if -delta > p['PourMinDelta']:
            s['DropSide'] = not s['DropSide']
            kick(s, p, (1.0 if s['DropSide'] else -1.0) * p['DropKickTilt'] * strength, p['KickWavePx'] * strength)
    elif delta > p['PourMinDelta']:
        mark_fresh(s, p, s['Level'])
        s['Pouring'] = True
        s['WavePx'] = min(p['WaveMaxPx'], s['WavePx'] + p['KickWavePx'] * strength)
    s['Value'] = value
    s['ReducedSpan'] = abs(value - s['Level'])
    s['Still'] = False


def jolt(hp, ink, hp_p, ink_p, strength):
    """LiquidSim308.Jolt: a jolt the game reports (a hit, a chunk landing): ink ripples, half of them on the HP liquid."""
    strength = min(1.0, max(0.0, strength))
    kick(ink, ink_p, 0.0, ink_p['KickWavePx'] * strength)
    kick(hp, hp_p, 0.0, hp_p['KickWavePx'] * strength * .5)


def impact_kick(s, p, away, strength):
    kick(s, p, away * p['KickTilt'] * min(1.0, max(0.0, strength)), p['KickWavePx'])


def drive(s, p, a, sign):
    """LiquidSim308.Drive. a = (LateralStep, ForwardStep, JumpSpeed, LandSpeed)."""
    tilt = sign * p['TiltKickPerMps'] * a[0]
    wave = p['WavePerMps'] * abs(a[1]) + p['HeavePerMps'] * (max(0.0, a[2]) + max(0.0, a[3]))
    kick(s, p, tilt, wave)


def wet_alpha(s, p):
    if s['WetAge'] < 0: return 0.0
    if s['WetAge'] <= p['WetHold']: return p['WetAlpha']
    return p['WetAlpha'] * min(1.0, max(0.0, 1 - (s['WetAge'] - p['WetHold']) / max(p['WetDry'], 1e-4)))


def move_towards(a, b, d):
    return b if abs(b - a) <= d else a + math.copysign(d, b - a)


def sim_step(s, p, reduced, dt):
    """LiquidSim308.Step: no input. The spring swings out what set_value / drive / kick put into it and rests on the flat line."""
    if dt <= 0: return
    gap = s['Value'] - s['Level']
    if gap != 0:
        if reduced:
            s['Level'] = move_towards(s['Level'], s['Value'], dt * max(s['ReducedSpan'], 1e-4) / max(p['ReducedSeconds'], .01))
        elif gap < 0:
            step = max(-gap * (1 - math.exp(-dt / max(p['DrainTau'], 1e-4))), p['DrainMinSpeed'] * dt)
            s['Level'] = max(s['Value'], s['Level'] - step)
        else:
            step = gap * (1 - math.exp(-dt / max(p['FillTau'], 1e-4)))
            s['Level'] = s['Value'] if gap - step <= p['LevelSnap'] else s['Level'] + step
    if s['WetAge'] >= 0:
        s['WetAge'] += dt
        if s['WetAge'] >= p['WetHold'] + p['WetDry'] or s['Value'] >= s['WetTop']: s['WetAge'] = -1.0
    if s['FreshAge'] >= 0:
        s['FreshAge'] += dt
        if s['FreshAge'] >= p['FreshSeconds']: s['FreshAge'] = -1.0
    if s['Pouring'] and (reduced or s['Value'] - s['Level'] <= p['PourMinDelta']): s['Pouring'] = False
    s['Pour'] = move_towards(s['Pour'], 1.0 if s['Pouring'] else 0.0, dt / max(p['PourFadeSeconds'], 1e-3))
    moving = False
    if reduced:
        s['Tilt'] = s['TiltSpeed'] = s['WavePx'] = s['Phase'] = 0.0; s['Accumulator'] = 0.0; s['ShoveHold'] = 0.0
    else:
        w = 2 * math.pi * p['SpringHz']; h = max(p['SimStep'], 1e-4)
        if w * h > .5: h = .5 / max(w, 1e-4)
        limit = max(p.get('TiltLimit', 0.0), p['MaxTilt'])
        speed_limit = 2 * w * limit
        s['TiltSpeed'] = min(speed_limit, max(-speed_limit, s['TiltSpeed']))
        s['Accumulator'] += dt
        guard = 0
        while s['Accumulator'] >= h and guard < 64:
            guard += 1
            s['TiltSpeed'] += (-w * w * s['Tilt'] - 2 * p['Damping'] * w * s['TiltSpeed']) * h
            s['Tilt'] += s['TiltSpeed'] * h
            s['Accumulator'] -= h
        if guard >= 64: s['Accumulator'] = 0.0
        s['TiltSpeed'] = min(speed_limit, max(-speed_limit, s['TiltSpeed']))
        if s['Tilt'] > limit: s['Tilt'] = limit; s['TiltSpeed'] = min(0.0, s['TiltSpeed'])
        elif s['Tilt'] < -limit: s['Tilt'] = -limit; s['TiltSpeed'] = max(0.0, s['TiltSpeed'])
        excite = p['WaveExcite'] * abs(s['TiltSpeed'])
        s['WavePx'] = min(p['WaveMaxPx'], max(0.0, s['WavePx'] + (excite - s['WavePx'] / max(p['WaveTau'], 1e-3)) * dt))
        # the surface rests only RestHold after the last shove: a speed that builds up over many frames arrives in small pieces
        if s['ShoveHold'] > 0: s['ShoveHold'] = max(0.0, s['ShoveHold'] - dt)
        moving = abs(s['Tilt']) >= p['RestTilt'] or abs(s['TiltSpeed']) >= p['RestTiltSpeed'] or s['WavePx'] >= p['RestWavePx'] or s['ShoveHold'] > 0
        if not moving:
            s['Tilt'] = s['TiltSpeed'] = s['WavePx'] = s['Phase'] = 0.0; s['Accumulator'] = 0.0
        else:
            s['Phase'] += p['WaveSpeed'] * dt
            if s['Phase'] > 2 * math.pi: s['Phase'] -= 2 * math.pi
    s['Still'] = (not moving) and s['Level'] == s['Value'] and s['WetAge'] < 0 and s['FreshAge'] < 0 and s['Pour'] <= 0


def sim_frame(hp, ink, hp_p, ink_p, a, sign, reduced, dt):
    """LiquidSim308.Frame = HudVessels308.Tick: the player's action (if any) shoves both liquids, then both swing on."""
    if (not reduced) and dt > 0 and any(v != 0 for v in a):
        drive(hp, hp_p, a, sign); drive(ink, ink_p, a, sign)
    sim_step(hp, hp_p, reduced, dt); sim_step(ink, ink_p, reduced, dt)


def amplitude_px(s, half_width):
    return abs(s['Tilt']) * half_width + s['WavePx']


class ActionMeter:
    """ActionMeter308: the player's body -> (LateralStep, ForwardStep, JumpSpeed, LandSpeed). Changes and events only."""

    def __init__(self):
        self.stage = 0
        self.pos = (0.0, 0.0, 0.0); self.yaw = 0.0
        self.lat = self.fwd = self.vert = self.turn = self.turn_lat = 0.0
        self.jump = self.land = 0

    def sample(self, pos, right, yaw, dt, jump_serial, land_serial, launch, spec=METER):
        if dt <= 1e-5:
            if self.stage == 2: self.stage = 1                       # a pause is not an action: the next speed is a new baseline
            return (0.0, 0.0, 0.0, 0.0)
        rx, rz = right[0], right[2]
        n = math.hypot(rx, rz)
        rx, rz = (rx / n, rz / n) if n > 1e-4 else (1.0, 0.0)
        fx, fz = -rz, rx                                             # Vector3.Cross(right, up)
        d = (pos[0] - self.pos[0], pos[1] - self.pos[1], pos[2] - self.pos[2])
        if self.stage == 0 or d[0] * d[0] + d[1] * d[1] + d[2] * d[2] > spec['TeleportMetres'] ** 2:
            self.stage = 1; self.pos = pos; self.yaw = yaw
            self.lat = self.fwd = self.vert = self.turn = self.turn_lat = 0.0
            self.jump, self.land = jump_serial, land_serial
            return (0.0, 0.0, 0.0, 0.0)
        vx, vy, vz = d[0] / dt, d[1] / dt, d[2] / dt
        lat, fwd = vx * rx + vz * rz, vx * fx + vz * fz
        if lat * lat + fwd * fwd < spec['SpeedDeadzone'] ** 2: lat = fwd = 0.0
        dyaw = (yaw - self.yaw + 180.0) % 360.0 - 180.0
        raw = min(spec['TurnClamp'], max(-spec['TurnClamp'], dyaw / dt))
        k = 1 - math.exp(-2 * math.pi * max(spec['TurnLowPassHz'], .1) * dt)
        self.turn += (raw - self.turn) * k
        if abs(self.turn) < 1e-3: self.turn = 0.0
        over = max(0.0, abs(self.turn) - spec['TurnDeadzone']) * (1 if self.turn > 0 else -1 if self.turn < 0 else 0)
        turn_lat = over / 180.0 * spec['TurnMpsPer180']
        out = (0.0, 0.0, 0.0, 0.0)
        if self.stage == 1:
            self.stage = 2
            self.lat, self.fwd = lat, fwd
        else:
            c = spec['StepClamp']; band = max(0.0, spec['StepBand'])
            clamp = lambda v: min(c, max(-c, v))
            # per axis: handed on once the change since the last one handed on has reached the band, or the body stands
            stopped = lat == 0 and fwd == 0
            lat_step = fwd_step = 0.0
            if stopped or abs(lat - self.lat) >= band: lat_step = clamp(lat - self.lat); self.lat = lat
            if stopped or abs(fwd - self.fwd) >= band: fwd_step = clamp(fwd - self.fwd); self.fwd = fwd
            out = (lat_step + clamp(turn_lat - self.turn_lat), fwd_step,
                   min(c, max(0.0, launch)) if jump_serial != self.jump else 0.0,
                   min(c, max(0.0, -self.vert)) if land_serial != self.land else 0.0)
        self.pos = pos; self.yaw = yaw; self.vert = vy
        self.turn_lat = turn_lat; self.jump, self.land = jump_serial, land_serial
        return out


# ---- the scripted timeline of AC-H16 (port of Editor/WorldMacro/HudLiquid308Timeline.cs)
TL_BODY = dict(WalkSpeed=2.2, RunSpeed=5.5, Acceleration=16.0, Deceleration=22.0, DodgeDistance=3.0, DodgeSeconds=.25, JumpHeight=.75, Gravity=-20.0,
               TurnDegPerSec=480.0, TurnSeconds=.30, TurnRampSeconds=.05, HpBefore=.80, HpAfter=.62, HitDamage=18.0, HitFullDamage=25.0,
               InkStart=.60, InkSpent=.45, InkRefilled=.75, ImpactLight=1.0, ImpactKickFloor=.5)
TL_LIMITS = dict(HpHalfWidthPx=51.0, InkHalfWidthPx=44.7, RestPx=0.0, WalkMinPx=.5, ActionMinPx=1.0, SettleSeconds=3.0)
TL_NAMES = ('stand', 'walk', 'run', 'dodge', 'jump+land', 'turn', 'hit', 'ink spent', 'ink refill', 'impact', 'stand')
TL_START = (0.0, 3.0, 8.0, 13.0, 16.0, 19.5, 22.5, 25.5, 28.5, 31.5, 34.5)
TL_END = 39.5
(SEG_STAND0, SEG_WALK, SEG_RUN, SEG_DODGE, SEG_JUMP, SEG_TURN, SEG_HIT, SEG_INKSPENT, SEG_INKREFILL, SEG_IMPACT, SEG_STANDEND) = range(11)


def tl_jump_speed(b):
    return math.sqrt(2 * max(0.0, b['JumpHeight']) * abs(b['Gravity']))


def tl_action_seconds(seg, b):
    if seg == SEG_WALK: return 2.0 + b['WalkSpeed'] / b['Deceleration']
    if seg == SEG_RUN: return 2.0 + b['RunSpeed'] / b['Deceleration']
    if seg == SEG_DODGE: return b['DodgeSeconds']
    if seg == SEG_JUMP: return 2.0 * tl_jump_speed(b) / abs(b['Gravity'])
    if seg == SEG_TURN: return b['TurnSeconds'] + 2.0 * b['TurnRampSeconds']
    return 0.0


def _towards(v, target, d):
    dx, dz = target[0] - v[0], target[1] - v[1]
    n = math.hypot(dx, dz)
    return target if n <= d or n == 0 else (v[0] + dx / n * d, v[1] + dz / n * d)


def tl_run(hp_p=HP, ink_p=INKP, b=TL_BODY, lim=TL_LIMITS, fps=60.0, reduced=False, actions=True, sign=None, capture=None):
    """capture = frame numbers whose liquid states are handed back in tr['states'] (frame -> (hp state, ink state))."""
    sign = METER['TiltSign'] if sign is None else sign
    states = {}
    dt = 1.0 / fps
    frames = int(round(TL_END * fps))
    tr = dict(t=[], segment=[], acting=[], hp_tilt=[], hp_wave=[], ink_tilt=[], ink_wave=[], hp_amp=[], ink_amp=[], hp_level=[], ink_level=[],
              hp_value=[], ink_value=[], pour=[])
    hp, ink = new_state(b['HpBefore']), new_state(b['InkStart'])
    meter = ActionMeter()
    pos = [0.0, 0.0, 0.0]; planar = (0.0, 0.0); yaw = 0.0; vertical = 0.0
    jump_serial = land_serial = 0; airborne = False
    launch = tl_jump_speed(b); fired = -1
    for f in range(frames):
        t = f * dt
        seg = SEG_STAND0
        for k in range(len(TL_START) - 1, -1, -1):
            if t >= TL_START[k] - 1e-5: seg = k; break
        into = t - TL_START[seg]
        if actions and seg == SEG_TURN:
            ramp = max(b['TurnRampSeconds'], 1e-3)
            w = into / ramp if into < ramp else 1.0 if into < ramp + b['TurnSeconds'] else max(0.0, 1.0 - (into - ramp - b['TurnSeconds']) / ramp)
            yaw += b['TurnDegPerSec'] * w * dt
        yr = math.radians(yaw)
        right = (math.cos(yr), 0.0, -math.sin(yr)); fwd = (math.sin(yr), 0.0, math.cos(yr))
        desired = (0.0, 0.0)
        if actions:
            if seg == SEG_WALK and into < 2.0: desired = ((right[0] * .5 + fwd[0] * .866) * b['WalkSpeed'], (right[2] * .5 + fwd[2] * .866) * b['WalkSpeed'])
            elif seg == SEG_RUN and into < 2.0: desired = ((right[0] * -.5 + fwd[0] * .866) * b['RunSpeed'], (right[2] * -.5 + fwd[2] * .866) * b['RunSpeed'])
        rate = b['Deceleration'] if desired[0] ** 2 + desired[1] ** 2 < planar[0] ** 2 + planar[1] ** 2 else b['Acceleration']
        planar = _towards(planar, desired, rate * dt)
        move = planar
        if actions and seg == SEG_DODGE and into < b['DodgeSeconds']:
            v = b['DodgeDistance'] / b['DodgeSeconds']; move = (right[0] * v, right[2] * v)
        if actions and seg == SEG_JUMP and fired != SEG_JUMP:
            fired = SEG_JUMP; jump_serial += 1; airborne = True; vertical = launch
        if airborne:
            y = pos[1] + vertical * dt + .5 * b['Gravity'] * dt * dt
            vertical += b['Gravity'] * dt
            if y <= 0 and vertical < 0: y = 0.0; airborne = False; land_serial += 1; vertical = 0.0
            pos[1] = y
        pos[0] += move[0] * dt; pos[2] += move[1] * dt
        acting = actions and (move[0] ** 2 + move[1] ** 2 > 1e-6 or airborne or (seg == SEG_TURN and into < tl_action_seconds(SEG_TURN, b)))
        if seg == SEG_HIT and fired != SEG_HIT:
            fired = SEG_HIT; acting = actions
            set_value(hp, hp_p, b['HpAfter'])
            if actions and not reduced: jolt(hp, ink, hp_p, ink_p, .25 + min(1.0, max(0.0, b['HitDamage'] / b['HitFullDamage'])) * .75)
        if seg == SEG_INKSPENT and fired != SEG_INKSPENT: fired = SEG_INKSPENT; acting = actions; set_value(ink, ink_p, b['InkSpent'])
        if seg == SEG_INKREFILL and fired != SEG_INKREFILL: fired = SEG_INKREFILL; acting = actions; set_value(ink, ink_p, b['InkRefilled'])
        if seg == SEG_IMPACT and fired != SEG_IMPACT:
            fired = SEG_IMPACT; acting = actions
            if actions and not reduced:
                strength = max(b['ImpactLight'], b['ImpactKickFloor'])
                impact_kick(hp, hp_p, -1.0, strength); impact_kick(ink, ink_p, -1.0, strength)
        a = meter.sample(tuple(pos), right, yaw, dt, jump_serial, land_serial, launch)
        sim_frame(hp, ink, hp_p, ink_p, a, sign, reduced, dt)
        tr['t'].append(t); tr['segment'].append(seg); tr['acting'].append(1 if acting else 0)
        tr['hp_tilt'].append(hp['Tilt']); tr['hp_wave'].append(hp['WavePx']); tr['ink_tilt'].append(ink['Tilt']); tr['ink_wave'].append(ink['WavePx'])
        tr['hp_level'].append(hp['Level']); tr['ink_level'].append(ink['Level']); tr['hp_value'].append(hp['Value']); tr['ink_value'].append(ink['Value'])
        tr['pour'].append(max(hp['Pour'], ink['Pour']))
        tr['hp_amp'].append(amplitude_px(hp, lim['HpHalfWidthPx'])); tr['ink_amp'].append(amplitude_px(ink, lim['InkHalfWidthPx']))
        if capture is not None and f in capture: states[f] = (dict(hp), dict(ink))
    if capture is not None: tr['states'] = states
    return tr


def tl_straight(speed, acceleration, deceleration, fps=60.0, hp_p=HP, ink_p=INKP, lim=TL_LIMITS, sign=None):
    """HudLiquid308Straight.Run: stand .5 s, speed up STRAIGHT ahead (no sideways part), hold 3 s, slow down, stand 4 s.
    Returns (hp peak px, ink peak px, max px in the last second at one speed, seconds from standing to the flat line)."""
    sign = METER['TiltSign'] if sign is None else sign
    dt = 1.0 / fps
    stop_at = .5 + speed / acceleration + 3.0; stood_at = stop_at + speed / deceleration
    frames = int(round((stood_at + 4.0) * fps))
    hp, ink = new_state(.8), new_state(.6)
    meter = ActionMeter(); z = 0.0; v = 0.0; last = -1
    hp_peak = ink_peak = cruise = 0.0
    for f in range(frames):
        t = f * dt
        want = speed if .5 <= t < stop_at else 0.0
        v = move_towards(v, want, (deceleration if want < v else acceleration) * dt)
        z += v * dt
        a = meter.sample((0.0, 0.0, z), (1.0, 0.0, 0.0), 0.0, dt, 0, 0, 0.0)
        sim_frame(hp, ink, hp_p, ink_p, a, sign, False, dt)
        x, y = amplitude_px(hp, lim['HpHalfWidthPx']), amplitude_px(ink, lim['InkHalfWidthPx'])
        hp_peak = max(hp_peak, x); ink_peak = max(ink_peak, y)
        if stop_at - 1.0 <= t < stop_at: cruise = max(cruise, x, y)
        if x > lim['RestPx'] or y > lim['RestPx']: last = f
    return hp_peak, ink_peak, cruise, (0.0 if last < 0 else max(0.0, (last + 1) * dt - stood_at))


def fresh_now(s, p):
    """LiquidSim308.FreshNow."""
    if s['FreshAge'] < 0 or p['FreshSeconds'] <= 0: return 0.0
    t = min(1.0, max(0.0, (s['FreshAge'] - p['FreshSeconds'] * .35) / (p['FreshSeconds'] - p['FreshSeconds'] * .35)))
    return 1.0 - t * t * (3 - 2 * t)


def pose(hp, ink):
    """cluster() arguments for a pair of liquid states, as HudVessels308.Push hands them to the shader."""
    kw = dict(hp=hp['Level'], ink=ink['Value'], ink_level=ink['Level'], hp_tilt=hp['Tilt'], ink_tilt=ink['Tilt'], hp_wave=hp['WavePx'], ink_wave=ink['WavePx'],
              phase=(hp['Phase'], ink['Phase']), pour=ink['Pour'])
    a = wet_alpha(hp, HP)
    if a > 0: kw['hp_wet'] = (hp['WetTop'], a)
    if fresh_now(ink, INKP) > 0: kw['ink_fresh'] = ink['FreshBottom']
    else:
        a = wet_alpha(ink, INKP)
        if a > 0: kw['ink_wet'] = (ink['WetTop'], a)
    return kw


def tl_check(full, none, red, b=TL_BODY, lim=TL_LIMITS, fps=60.0):
    n = len(TL_START)
    r = dict(segment_start=list(TL_START), action_end=[0.0] * n, hp_peak_px=[0.0] * n, ink_peak_px=[0.0] * n, settle_s=[0.0] * n)
    calm = settles = True
    rest_frames = 0; rest_max = 0.0
    T = full['t']
    for k in range(n):
        start, end = TL_START[k], (TL_START[k + 1] if k + 1 < n else TL_END)
        action_end = start + tl_action_seconds(k, b)
        r['action_end'][k] = action_end
        idx = [i for i, t in enumerate(T) if start - 1e-5 <= t < end - 1e-5]
        r['hp_peak_px'][k] = max(full['hp_amp'][i] for i in idx); r['ink_peak_px'][k] = max(full['ink_amp'][i] for i in idx)
        moving = [i for i in idx if full['hp_amp'][i] > lim['RestPx'] or full['ink_amp'][i] > lim['RestPx']]
        if k in (SEG_STAND0, SEG_STANDEND):
            m = max(r['hp_peak_px'][k], r['ink_peak_px'][k])
            r['stand_first_max_px' if k == SEG_STAND0 else 'stand_last_max_px'] = m
            rest_max = max(rest_max, m); rest_frames += len(idx)
            if m > lim['RestPx']: calm = False
            continue
        settled = action_end if not moving else T[moving[-1]] + 1.0 / fps
        r['settle_s'][k] = max(0.0, settled - action_end)
        if r['settle_s'][k] > lim['SettleSeconds'] or settled > end - 2.0 / fps: settles = False
        rest_frames += max(0, idx[-1] - (moving[-1] if moving else idx[0] - 1))
    H, I = r['hp_peak_px'], r['ink_peak_px']
    r['responds'] = bool(H[SEG_WALK] >= lim['WalkMinPx'] and H[SEG_RUN] >= lim['ActionMinPx'] and H[SEG_DODGE] >= lim['ActionMinPx'] and H[SEG_JUMP] >= lim['ActionMinPx']
                         and H[SEG_TURN] >= lim['ActionMinPx'] and H[SEG_HIT] >= lim['ActionMinPx'] and I[SEG_INKSPENT] >= lim['WalkMinPx']
                         and I[SEG_INKREFILL] >= lim['ActionMinPx'] and H[SEG_IMPACT] >= lim['ActionMinPx'] and I[SEG_IMPACT] >= lim['ActionMinPx'])
    r['proportional'] = bool(H[SEG_WALK] < H[SEG_RUN] < H[SEG_DODGE] and I[SEG_WALK] < I[SEG_RUN] < I[SEG_DODGE])
    drift = lambda a_, b_: max(abs(x - y) for x, y in zip(a_, b_))
    r['level_drift_max'] = max(drift(full['hp_level'], none['hp_level']), drift(full['ink_level'], none['ink_level']))
    r['value_drift_max'] = max(drift(full['hp_value'], none['hp_value']), drift(full['ink_value'], none['ink_value']))
    r['reduced_max_tilt'] = max(max(abs(v) for v in red['hp_tilt']), max(abs(v) for v in red['ink_tilt']))
    r['reduced_max_wave_px'] = max(max(red['hp_wave']), max(red['ink_wave']))
    r['reduced_max_pour'] = max(red['pour'])
    r.update(rest_max_px=rest_max, rest_frames=rest_frames, calm=calm, settles=settles,
             reading_kept=bool(r['level_drift_max'] == 0 and r['value_drift_max'] == 0),
             reduced_flat=bool(r['reduced_max_tilt'] == 0 and r['reduced_max_wave_px'] == 0 and r['reduced_max_pour'] == 0))
    r['ok'] = bool(r['calm'] and r['responds'] and r['proportional'] and r['settles'] and r['reading_kept'] and r['reduced_flat'])
    return r


def gate_step(g, active, light, dt):
    """ImpactGate308.Step. Returns (light shown, began)."""
    began = False
    g['since'] += dt
    if not active:
        g.update(was=False, lighting=False, stuck=False, active=0.0); return 0.0, False
    if not g['was']:
        g.update(was=True, active=0.0); began = True
        g['lighting'] = light > 0 and g['since'] >= IMPACT['MinInterval']
        if g['lighting']: g['since'] = 0.0
    else: g['active'] += dt
    if g['active'] > IMPACT['StuckSeconds']: g['stuck'] = True
    return (0.0 if g['stuck'] or not g['lighting'] else light), began


# ============================================================ measurements
def lum(rgb):
    c = np.where(rgb <= .04045, rgb / 12.92, ((rgb + .055) / 1.055) ** 2.4)
    return c[..., 0] * .2126 + c[..., 1] * .7152 + c[..., 2] * .0722


def contrast(a, b):
    hi, lo = max(a, b), min(a, b)
    return (hi + .05) / (lo + .05)


def centre_surface(aux):
    """Surface height of the centre column, in px of the element above its quad bottom."""
    col = aux['surf'].shape[1] // 2
    return float(aux['surf'][:, col].mean()) * aux['design_px']


def reach_time(p, frm, to, tol):
    s = new_state(frm); set_value(s, p, to); dt = 1 / 240; t = 0.0
    while t < 6:
        t += dt; sim_step(s, p, False, dt)
        if abs(s['Level'] - s['Value']) <= tol: return round(t, 4)
    return None


def impulse(p, mps, fps, until, at=None):
    """One sudden sideways speed change of `mps`, then the free swing at `fps`: (first peak, visible swings, tilt at the times `at`)."""
    s = new_state(.5)
    drive(s, p, (mps, 0.0, 0.0, 0.0), 1.0)
    dt = 1 / fps; t = 0.0; last = 0.0; peak = 0.0; swings = 0; nxt = 0; out = []
    while t < until - 1e-6:
        step = min(dt, until - t)
        if at and nxt < len(at) and t + step > at[nxt] - 1e-6: step = max(1e-5, at[nxt] - t)
        sim_step(s, p, False, step); t += step
        if at and nxt < len(at) and abs(t - at[nxt]) < 1e-4: out.append(s['Tilt']); nxt += 1
        peak = max(peak, abs(s['Tilt']))
        if last != 0 and (s['TiltSpeed'] > 0) != (last > 0) and abs(s['Tilt']) > .02 * peak: swings += 1
        last = s['TiltSpeed']
    return peak, swings, out


def rest_time(p):
    s = new_state(.5); s['Tilt'] = p['MaxTilt']; s['WavePx'] = p['WaveMaxPx']; s['Still'] = False
    dt = 1 / 60; t = 0.0
    while t < 8:
        t += dt; sim_step(s, p, False, dt)
        if s['Still']: return round(t, 3)
    return None


def canvas_size(w, h):
    k = 2 ** (.5 * math.log2(w / 1920) + .5 * math.log2(h / 1080))       # CanvasScaler, match .5
    return w / k, h / k, k


def measure():
    R = {}
    hp_rect, ink_rect = LAYOUT['hp'], LAYOUT['ink']
    # ---- AC-H1.1 / H1.3 / H10.2 layout
    els = cluster()
    frame_a = np.zeros((1080, 1920))
    for rgba, x0, y0, _ in els:
        h, w = rgba.shape[:2]
        frame_a[y0:y0 + h, x0:x0 + w] = np.maximum(frame_a[y0:y0 + h, x0:x0 + w], rgba[..., 3])
    ys, xs = np.nonzero(frame_a > .02)
    R['H1.1_cluster_bbox_px'] = [int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1]
    rects = [hp_rect, ink_rect] + [mark_rect(i) for i in range(3)]
    R['H1.1_rect_bbox'] = [round(min(r[0] for r in rects), 1), round(min(r[1] for r in rects), 1),
                           round(max(r[0] + r[2] for r in rects), 1), round(max(r[1] + r[3] for r in rects), 1)]
    R['H1.1_inside_safe'] = bool(R['H1.1_rect_bbox'][0] >= 64 and R['H1.1_rect_bbox'][3] <= 1016)
    sil = []
    for i in (0, 1):
        m = np.zeros((1080, 1920), bool)
        rgba, x0, y0, aux = els[i]
        m[y0:y0 + rgba.shape[0], x0:x0 + rgba.shape[1]] = aux['d'] < 0
        sil.append(m)
    overlap = int((sil[0] & sil[1]).sum())
    R['H1.3_silhouette_px'] = dict(hp=int(sil[0].sum()), ink=int(sil[1].sum()), overlap=overlap,
                                   overlap_share_of_hp=round(overlap / max(1, int(sil[0].sum())), 4))
    hits = {}
    for i, name in enumerate(('dodge', 'jump', 'vehicle')):
        r = mark_rect(i)
        m = np.zeros((1080, 1920), bool)
        m[int(round(r[1])):int(round(r[1] + r[3])), int(round(r[0])):int(round(r[0] + r[2]))] = True
        hits[name] = int((m & (sil[0] | sil[1])).sum())
    R['H1.3_mark_rect_px_on_silhouettes'] = hits
    area = sum(r[2] * r[3] for r in rects)
    R['H10.2_rect_area_share'] = round(area / (1920 * 1080), 5)
    # ---- AC-H1.2 other HUD parts at five resolutions (canvas units; CanvasScaler 1920x1080 match .5)
    res = {}
    bb = R['H1.1_rect_bbox']
    for (w, h) in ((1920, 1080), (2560, 1440), (3440, 1440), (1920, 1200), (1600, 1200)):
        cw, ch, k = canvas_size(w, h)
        left, right, top, bottom = bb[0], bb[2], ch - (1080 - bb[1]), ch - (1080 - bb[3])
        label = 1000 if w / h >= 16 / 9 - 1e-6 else 800
        prompt_left = cw / 2 - (label + 188) / 2                 # HudTokens304: lead 64 + key 40 + gap 20 + tail 64
        boss_left = cw / 2 - 360
        mini_left = cw - 330
        arrival_bottom = 650
        res['%dx%d' % (w, h)] = dict(canvas=[round(cw, 1), round(ch, 1)], scale=round(k, 4), cluster=[left, round(top, 1), right, round(bottom, 1)],
                                     prompt_label_px=label, prompt_left=round(prompt_left, 1), clear_of_prompt=bool(prompt_left >= right),
                                     clear_of_boss_bar=bool(boss_left >= right), clear_of_minimap=bool(mini_left >= right),
                                     clear_of_arrival_card=bool(all(ch - (1080 - r[1]) >= arrival_bottom or r[0] >= 202 for r in rects)),
                                     label_px_that_touches=round(2 * (cw / 2 - right) - 188, 1))
    R['H1.2_resolutions'] = res
    # ---- AC-H2 silhouette (generator)
    R['H2.1_silhouette'] = AJ['measure']
    # ---- AC-H3.1 level mapping (HP vessel, centre column)
    floor_px = M['Level'][0] * M['QuadRef'][1]; fill_px = (M['Level'][1] - M['Level'][0]) * M['QuadRef'][1]
    rows = []; worst = 0.0
    for v in (0, .01, .15, .35, .5, .99, 1):
        rgba, x0, y0, aux = element(hp_rect, 1, CINNABAR, [v, 0, 0, 0], [0, 0, .35, low_hp(v)], [1, 0, 0, 0])
        col = aux['surf'].shape[1] // 2
        measured = floor_px + float(aux['body_cov'][:, col].sum()) / aux['design_px']   # the liquid column stands on the inner floor
        expect = floor_px + v * fill_px
        if v >= .004: worst = max(worst, abs(measured - expect))
        rows.append(dict(value=v, expect_px=round(expect, 2), measured_px=round(float(measured), 2)))
    neck_rows = int((aux['body_cov'][:int((1 - M['Level'][1]) * aux['surf'].shape[0]) - 3, :] > .5).sum())
    R['H3.1_level'] = dict(rows=rows, worst_px=round(worst, 2), liquid_px_above_full_line_at_1=neck_rows, fill_range_px=round(fill_px, 2))
    # ---- AC-H3.2 times
    R['H3.2_reach'] = dict(hp_drain_1=reach_time(HP, 1, 0, .02), ink_drain_1=reach_time(INKP, 1, 0, .02),
                           hp_fill_03=reach_time(HP, .3, .6, .02), ink_fill_03=reach_time(INKP, .3, .6, .02),
                           hp_fill_1=reach_time(HP, 0, 1, .02), ink_fill_1=reach_time(INKP, 0, 1, .02))
    # ---- AC-H3.4 centre column under max tilt + max ripple
    base = centre_surface(element(hp_rect, 1, CINNABAR, [.5, 0, 0, 0], [0, 0, .35, 0], [1, 0, 0, 0])[3])
    dev = 0.0
    for tilt in (-HP['MaxTilt'], HP['MaxTilt']):
        for ph in np.linspace(0, 2 * math.pi, 9):
            aux = element(hp_rect, 1, CINNABAR, [.5, tilt, HP['WaveMaxPx'], ph], [0, 0, .35, 0], [1, 0, 0, 0])[3]
            dev = max(dev, abs(centre_surface(aux) - base))
    R['H3.4_centre_column_dev_px'] = dict(max=round(dev, 2), allowed=HP['WaveMaxPx'] + 1)
    # ---- AC-H4 (D308-11b: the spring has no target; it answers a shove and swings out)
    hp_peak, hp_swings, _ = impulse(HP, 6.0, 240, 5.0); ink_peak, ink_swings, _ = impulse(INKP, 6.0, 240, 5.0)
    R['H4.1_impulse'] = dict(hp=dict(peak=round(hp_peak, 5), want=round(HP['TiltPerMps'] * 6, 5), swings_over_2pct=hp_swings),
                             ink=dict(peak=round(ink_peak, 5), want=round(INKP['TiltPerMps'] * 6, 5), swings_over_2pct=ink_swings),
                             shove_mps=6.0, first_peak_share=dict(hp=round(first_peak_share(HP['Damping']), 5), ink=round(first_peak_share(INKP['Damping']), 5)))
    at = [.25, .5, 1.0]
    v = [impulse(HP, 6.0, f, 1.001, at)[2] for f in (30, 60, 144)]
    R['H4.2_fps_spread'] = round(max(max(x[i] for x in v) - min(x[i] for x in v) for i in range(3)), 5)
    R['H4.3_rest_seconds'] = dict(hp=rest_time(HP), ink=rest_time(INKP))
    # ---- AC-H5
    s = new_state(.8); set_value(s, HP, .5)
    top0, a0 = s['WetTop'], wet_alpha(s, HP); gone = None; t = 0.0; at_hold = None
    while t < 8:
        t += 1 / 240; sim_step(s, HP, False, 1 / 240)
        if at_hold is None and t >= HP['WetHold'] - 1 / 240: at_hold = wet_alpha(s, HP)
        if wet_alpha(s, HP) <= 0: gone = round(t, 3); break
    R['H5.1_hp_film'] = dict(top=top0, alpha_at_drop=a0, alpha_end_of_hold=at_hold, gone_at_s=gone)
    s = new_state(.6); set_value(s, INKP, .45); t = 0.0; gone = None
    while t < 8:
        t += 1 / 60; sim_step(s, INKP, False, 1 / 60)
        if wet_alpha(s, INKP) <= 0: gone = round(t, 3); break
    R['H5.2_ink_film_gone_at_s'] = gone
    con = {}
    for name, bg in (('bright_E4E0D6', np.array([0xE4, 0xE0, 0xD6]) / 255), ('mid_808080', np.array([.5, .5, .5])), ('dark_2A2A26', np.array([0x2A, 0x2A, 0x26]) / 255)):
        row = {}
        for label, rect, code, tint, lvl, wet in (('hp', hp_rect, 1, CINNABAR, .5, (.8, HP['WetAlpha'])), ('ink', ink_rect, 0, INK, .4, (.7, INKP['WetAlpha']))):
            rgba, x0, y0, aux = element(rect, code, tint, [lvl, 0, 0, 0], [wet[0], wet[1], 0 if code == 0 else .35, 0], [code, 0, 0, 0])
            comp = bg[None, None, :] * (1 - rgba[..., 3:4]) + rgba[..., :3] * rgba[..., 3:4]
            film = (aux['wet_a'] > .5 * aux['wet_a'].max()) & (aux['surf_a'] < .05) & (aux['lens_a'] < .05)   # the film itself, not the lens strip
            body = (aux['body_cov'] > .9) & (aux['surf_a'] < .05) & (aux['d'] < -9)
            row[label] = round(contrast(float(lum(comp[film]).mean()), float(lum(comp[body]).mean())), 2)
        con[name] = row
    R['H5.3_film_vs_body_contrast'] = con
    s = new_state(.2); pour = wave = tilt = 0.0; fresh = False; t = 0.0
    while t < 3:
        t += 1 / 60; set_value(s, INKP, s['Value'] + .05 / 60); sim_step(s, INKP, False, 1 / 60)
        pour = max(pour, s['Pour']); wave = max(wave, s['WavePx']); tilt = max(tilt, abs(s['Tilt'])); fresh = fresh or s['FreshAge'] >= 0
    j = new_state(.2); set_value(j, INKP, .5); sim_step(j, INKP, False, 1 / 60)
    R['H5.4_regen'] = dict(thread=pour, ripple=wave, tilt=tilt, fresh=fresh, jump_pours=bool(j['Pouring'] and j['FreshAge'] >= 0),
                           pour_stirs_px=round(j['WavePx'], 4))
    # ---- AC-H6
    R['H6.1_hp_ink_mix'] = dict(at_018=round(low_hp(.18) * LOW['HpDarken'], 4), at_035=round(low_hp(.35) * LOW['HpDarken'], 4))
    cov = {}
    for label, v in (('ink_008_low', .08), ('ink_040', .40)):
        rgba, x0, y0, aux = element(ink_rect, 0, INK, [v, 0, 0, 0], [0, 0, 0, low_ink(v)], [0, 0, 0, 0])
        region = (aux['cavity'] > .5) & (aux['below'] > .5)
        cov[label] = round(float((aux['body_cov'][region] > .5).mean()), 3)
    R['H6.2_ink_body_coverage'] = cov
    a = cluster(hp=.62, ink=.44); b = cluster(hp=.62, ink=.44)
    R['H6.3_two_frames_same_input_max_diff_255'] = round(max(float(np.abs(x[0] - y[0]).max()) for x, y in zip(a, b)) * 255, 3)
    # ---- AC-H8.1 / H9.2 impact
    base_hue = math.degrees(math.atan2(math.sqrt(3) * (CINNABAR[1] - CINNABAR[2]), 2 * CINNABAR[0] - CINNABAR[1] - CINNABAR[2]))
    worst_level = 0.0; worst_line = 99.0; worst_hue = 1.0; max_channel = 0.0; base_line = 99.0; worst_peak = 99.0; base_peak = 99.0

    worst_centre = 99.0

    def line_contrast(rgba, aux, centre=False):
        # centre = the part of the line further than 10 px from the glass (the impact shadow band covers the 9 px by the far wall)
        line = (aux['surf_a'] > .6) & ((aux['d'] < -10) if centre else True)
        body = (aux['body_cov'] > .9) & (aux['surf_a'] < .02) & (aux['d'] < -10)
        if not (line.any() and body.any()): return 99.0, 99.0
        lb = float(lum(rgba[..., :3][body]).mean())
        return contrast(float(lum(rgba[..., :3][line]).mean()), lb), contrast(float(lum(rgba[..., :3][line]).max()), lb)
    for v in (.1, .35, .6, 1.0):
        rgba0, _, _, aux0 = element(hp_rect, 1, CINNABAR, [v, 0, 0, 0], [0, 0, .35, low_hp(v)], [1, 0, 0, 0])
        m0, p0 = line_contrast(rgba0, aux0); base_line = min(base_line, m0); base_peak = min(base_peak, p0)
        ref = centre_surface(element(hp_rect, 1, CINNABAR, [v, 0, 0, 0], [0, 0, .35, low_hp(v)], [1, 0, 0, 0])[3])
        for k in range(8):
            ang = k * math.pi / 4
            rgba, x0, y0, aux = element(hp_rect, 1, CINNABAR, [v, 0, 0, 0], [0, 0, .35, low_hp(v)], [1, IMPACT['LightGain'], 0, 0],
                                        point=far_point(hp_rect, ang))
            worst_level = max(worst_level, abs(centre_surface(aux) - ref))
            max_channel = max(max_channel, float(rgba[..., :3][rgba[..., 3] > .01].max()))
            m1, p1 = line_contrast(rgba, aux); worst_line = min(worst_line, m1); worst_peak = min(worst_peak, p1)
            worst_centre = min(worst_centre, line_contrast(rgba, aux, True)[0])
            body = (aux['body_cov'] > .9) & (aux['surf_a'] < .02) & (aux['d'] < -10)
            if body.any():
                px = rgba[..., :3][body]
                hue = np.degrees(np.arctan2(math.sqrt(3) * (px[:, 1] - px[:, 2]), 2 * px[:, 0] - px[:, 1] - px[:, 2]))
                worst_hue = min(worst_hue, float((np.abs(hue - base_hue) <= 8).mean()))
    R['H8.1_impact_readability'] = dict(centre_level_shift_px=round(worst_level, 3), surface_line_contrast_min=round(worst_line, 2),
                                        surface_line_contrast_min_no_impact=round(base_line, 2),
                                        surface_line_contrast_min_centre=round(worst_centre, 2),
                                        surface_line_peak_contrast_min=round(worst_peak, 2), surface_line_peak_contrast_min_no_impact=round(base_peak, 2),
                                        hp_pixels_within_8deg_hue_min=round(worst_hue, 3))
    for kw in (dict(), dict(hp=.18, ink=.08), dict(impact=((1200, 500), 1.0)), dict(impact=((-400, 1500), 1.0), hp_tilt=-.2, ink_tilt=-.14),
               dict(ink=.4, ink_level=.25, ink_fresh=.15, pour=1.0), dict(cost=.15), dict(ink=.1, cost=.15),
               dict(hp_wet=(.84, .34), ink_wet=(.7, .30), hp_wave=3.0, ink_wave=1.8, hp_tilt=.22, ink_tilt=-.16)):
        for rgba, x0, y0, _ in cluster(**kw):
            vis = rgba[..., 3] > .01
            if vis.any(): max_channel = max(max_channel, float(rgba[..., :3][vis].max()))
    R['H9.2_max_channel_255'] = round(max_channel * 255, 2)
    # ---- AC-H8.2 / H8.3 gate
    g = dict(was=False, lighting=False, stuck=False, active=0.0, since=1e6); dt = 1 / 60
    lit = kicks = 0
    for _ in range(3):
        v, began = gate_step(g, True, 1.0, dt); lit += v > 0; kicks += began
    dark, _ = gate_step(g, False, 0.0, dt)
    second, began2 = gate_step(g, True, 1.0, dt)
    g = dict(was=False, lighting=False, stuck=False, active=0.0, since=1e6); frames = int(math.ceil((IMPACT['StuckSeconds'] + .3) / dt))
    stuck_lit = sum(gate_step(g, True, 1.0, dt)[0] > 0 for _ in range(frames))
    R['H8.2_gate'] = dict(lit_frames_of_3=int(lit), kicks=int(kicks), dark_after=bool(dark <= 0), second_inside_interval_light=second,
                          second_still_kicks=bool(began2), stuck_lit_frames=int(stuck_lit), stuck_frames_total=frames)
    # ---- AC-H13 reduced motion
    s = new_state(1.0); set_value(s, HP, .4); other = new_state(.5); t = 0.0; reach = None; mid = None; moved = 0.0
    while t < 1:
        # the hardest actions on every frame: with reduced motion nothing may move but the level (in a straight line)
        t += 1 / 240; sim_frame(s, other, HP, INKP, (12.0, 12.0, 6.0, 6.0), METER['TiltSign'], True, 1 / 240)
        moved = max(moved, abs(s['Tilt']) + s['WavePx'] + s['Pour'] + abs(other['Tilt']) + other['WavePx'])
        if mid is None and t >= HP['ReducedSeconds'] * .5: mid = round(s['Level'], 4)
        if reach is None and s['Level'] == s['Value']: reach = round(t, 4)
    R['H13.1_reduced'] = dict(level_lands_at_s=reach, level_midway=mid, tilt_ripple_thread=moved)
    return R


def measure_actions():
    """D308-11b: the liquid moves only in answer to what the player really did (AC-H16). The scripted timeline through the
    twin's port of ActionMeter308 -> LiquidSim308.Frame, the idle run, the meter's own cases and the source checks."""
    A = {}
    full, none, red = tl_run(), tl_run(actions=False), tl_run(reduced=True)
    r = tl_check(full, none, red)
    fast = tl_check(tl_run(fps=144.0), tl_run(fps=144.0, actions=False), tl_run(fps=144.0, reduced=True), fps=144.0)
    A['timeline'] = dict(fps=60, frames=len(full['t']), segments=list(TL_NAMES), limits=dict(TL_LIMITS), body=dict(TL_BODY), **r)
    A['timeline']['hp_peak_px_144fps'] = fast['hp_peak_px']; A['timeline']['settle_s_144fps'] = fast['settle_s']; A['timeline']['ok_144fps'] = fast['ok']
    A['trace'] = full
    # nothing done = nothing moves, at every level (low HP included: the tremble is gone)
    moved = 0.0; still = True
    for level in (1.0, .62, .2, .05):
        a, b = new_state(level), new_state(level)
        for _ in range(600):
            sim_frame(a, b, HP, INKP, (0.0, 0.0, 0.0, 0.0), METER['TiltSign'], False, 1 / 60)
            moved = max(moved, abs(a['Tilt']) + a['WavePx'] + abs(b['Tilt']) + b['WavePx'] + abs(a['Phase']) + abs(b['Phase']))
            still = still and a['Still'] and b['Still']
    A['idle_10s_at_four_levels'] = dict(tilt_plus_ripple_plus_phase=moved, still_throughout=bool(still))
    # the meter: a start and a stop sum to the speed change, steady motion / a placement / a slow look / a blocked body are nothing
    m = ActionMeter(); start = stop = steady = tele = 0.0; speed = 0.0; x = 0.0; dt = 1 / 60
    for i in range(300):
        want = 4.5 if 60 <= i < 180 else 0.0
        speed = move_towards(speed, want, (22.0 if want < speed else 16.0) * dt)
        x += speed * dt
        if i == 240: x += 40.0
        a = m.sample((x, 0.0, 0.0), (1.0, 0.0, 0.0), 0.0, dt, 0, 0, 0.0)
        if 60 <= i < 100: start += a[0]
        if i == 150: steady = abs(a[0])
        if 180 <= i < 220: stop += a[0]
        if 240 <= i < 244: tele = max(tele, abs(a[0]) + abs(a[1]))
    look = ActionMeter(); yaw = 0.0; slow = 0.0
    for i in range(180):
        yaw += 90.0 * dt; slow = max(slow, abs(look.sample((0.0, 0.0, 0.0), (1.0, 0.0, 0.0), yaw, dt, 0, 0, 0.0)[0]))
    # a pause (dt 0) while the body runs, the menu stops it, the game resumes with the body standing: not an action
    pm = ActionMeter(); px_ = 0.0; paused = 0.0
    for i in range(120):
        px_ += 5.5 * dt; pm.sample((px_, 0.0, 0.0), (1.0, 0.0, 0.0), 0.0, dt, 0, 0, 0.0)
    for i in range(30): pm.sample((px_, 0.0, 0.0), (1.0, 0.0, 0.0), 0.0, 0.0, 0, 0, 0.0)
    for i in range(60):
        a = pm.sample((px_, 0.0, 0.0), (1.0, 0.0, 0.0), 0.0, dt, 0, 0, 0.0); paused = max(paused, abs(a[0]) + abs(a[1]))
    A['meter'] = dict(start_sum_mps=round(start, 5), stop_sum_mps=round(stop, 5), steady_step=steady, placement_40m_step=tele, slow_look_90dps_step=slow,
                      step_band_mps=METER['StepBand'], stop_across_a_pause_step=paused)
    # AC-H16.9: speed that builds up over many frames with no sideways part (a walk / run straight ahead, a car pulling away)
    S = dict(fps=[30, 60, 144, 240])
    for name, (speed, acc, dec) in (('walk', (2.2, 16.0, 22.0)), ('run', (5.5, 16.0, 22.0)), ('car', (14.0, 5.0, 8.0))):
        rows = [tl_straight(speed, acc, dec, fps=float(f)) for f in S['fps']]
        S[name] = dict(speed=speed, hp_peak_px=[round(r[0], 4) for r in rows], ink_peak_px=[round(r[1], 4) for r in rows],
                       cruise_max_px=[r[2] for r in rows], settle_s=[round(r[3], 4) for r in rows])
    A['straight'] = S
    # sources: no idle term anywhere in the stage
    ui = os.path.join(STAGE, 'App', 'World', 'UI')
    src = {n: open(os.path.join(ui, n), encoding='utf-8-sig').read() for n in ('LiquidSim308.cs', 'HudVessels308.cs', 'HudLiquid308ProfileSO.cs')}
    presenter = open(os.path.join(STAGE, 'App', 'World', 'WorldMacroPlaytestHudPresenter.cs'), encoding='utf-8-sig').read()
    shader = open(os.path.join(STAGE, '_ProjectAssets', 'Art', 'UI', 'UI308', 'Shaders', 'InkVessel308.shader'), encoding='utf-8-sig').read()
    sim = src['LiquidSim308.cs']
    body = lambda name: sim[sim.index('public static void ' + name + '('):].split('\n        }\n', 1)[0]
    writes = lambda text: len(re.findall(r'[.](Level|Value)\s*=[^=]', text))
    present308 = presenter[presenter.index('private void Present308()'):presenter.index('private void ReadKeys308')]
    step_sig = re.search(r'public static void Step[(]([^)]*)[)]', sim).group(1)
    A['sources'] = dict(
        low_agitation_mentions=sum(len(re.findall('LowAgitation', t)) for t in src.values()),
        level_or_value_writes_in_drive_kick_jolt_impact=sum(writes(body(n)) for n in ('Drive', 'Kick', 'Jolt', 'ImpactKick')),
        step_parameters=step_sig, step_takes_no_motion_input=bool('lateral' not in step_sig.lower() and 'yaw' not in step_sig.lower() and 'agitation' not in step_sig.lower()),
        # any camera in the method that measures (an intermediate variable used to hide it from a pattern on the Sample call)
        camera_read_as_liquid_motion=len(re.findall(r'ViewCamera|Camera[.]main|CameraRig', present308)),
        boarding_frame_not_sampled=bool(re.search(r'if [(]seated != _seated308[)] [{][^}]*_motion308[.]Clear[(][)]; [}]\s*else\s*[{]\s*var sample = _motion308[.]Sample', present308)),
        body_is_the_measured_object=bool(re.search(r'_motion308[.]Sample[(]body[.]position', present308)),
        unscaled_time_or_clock_in_liquid_model=len(re.findall(r'Time[.](time|unscaledTime|realtimeSinceStartup)', sim)),
        shader_time_terms=len(re.findall(r'_Time|_SinTime|_CosTime', shader)))
    return A


def measure_keys():
    """D308-11b: the key glyph of each mark (AC-H15). Where the label comes from, where the keycap sits, how it reads on the
    brightest and the darkest scene (the shipped lacquer keycap against the kit's porcelain one), its states."""
    K = {}
    b, table = live_bindings(), key_table()
    cells = live_key_cells()
    names = [KEY_NAMES[c] if c >= 0 else None for c in cells]
    summon = open(os.path.join(ASSETS, '_Project', 'Scripts', 'App', 'World', 'Vehicle', 'WorldMacroPalanquinSummon.Shortcut.cs'), encoding='utf-8-sig').read()
    polled = re.findall(r'Keyboard[.]current[.](\w+)Key[.]wasPressedThisFrame', summon)
    rebinding = 0
    for base, _, files in os.walk(os.path.join(ASSETS, '_Project', 'Scripts')):
        for name in files:
            if name.endswith('.cs'):
                with open(os.path.join(base, name), encoding='utf-8-sig', errors='replace') as f:
                    rebinding += len(re.findall(r'ApplyBindingOverride|PerformInteractiveRebinding|LoadBindingOverridesFromJson', f.read()))
    K['source'] = dict(dodge=b['dodge'], jump=b['jump'], vehicle=b['vehicle'], cells=list(cells), cell_names=names,
                       rebound_examples=dict(j=key_cell('<Keyboard>/j', table), digit_7=key_cell('<Keyboard>/7', table), f7=key_cell('<Keyboard>/f7', table),
                                             pad_south=key_cell('<Gamepad>/buttonSouth', table), none=key_cell(None, table)),
                       vehicle_summon_polls=polled, vehicle_path_matches_summon=bool(polled and all('<Keyboard>/' + k == b['vehicle'] for k in polled)),
                       vehicle_action_in_gameplay_map=bool(b['vehicle_action'] in b['gameplay_actions']),
                       gamepad_bindings_in_gameplay=b['gamepad_bindings_in_gameplay'], rebinding_calls_in_project=rebinding, table_rows=len(table))
    # no key constant is handed to a mark by code: the cells reach the marks only through HudKeyGlyph308.Cell(path, table)
    ui = os.path.join(STAGE, 'App', 'World', 'UI')
    runtime = {n: open(os.path.join(ui, n), encoding='utf-8-sig').read() for n in ('HudVessels308.cs', 'HudActionRules308.cs', 'InkVesselGraphic308.cs', 'HudKeyBinding308.cs')}
    presenter = open(os.path.join(STAGE, 'App', 'World', 'WorldMacroPlaytestHudPresenter.cs'), encoding='utf-8-sig').read()
    runtime['presenter'] = presenter
    consts = sum(len(re.findall(r'HudKeyGlyph308[.](Shift|Space|Ctrl|Alt|Tab|Enter|Mouse\w+|Generic|Letters|Digits)\b', t)) for t in runtime.values())
    literals = sum(len(re.findall(r'"(Shift|Space|G|LShift|Spacebar)"', t)) for t in runtime.values())
    vessels = runtime['HudVessels308.cs']
    K['whitelist'] = dict(key_cell_constants_in_hud_code=consts, key_name_literals_in_hud_code=literals,
                          graphics_built=len(re.findall(r'= Element[(]profile, rect, "', vessels)),
                          text_objects_built=sum(len(re.findall(r'V[.]Keycap[(]|V[.]Label[(]|AddComponent<TMP|TextMeshPro', t)) for t in runtime.values()),
                          cells_in_block=len(KEY_NAMES), one_symbol_per_cell=True,
                          presenter_sets_cells_through_lookup=len(re.findall(r'HudKeyGlyph308[.]Cell[(]', presenter)))
    # ---- place: the three keycaps against the vessels' silhouettes (paper rim included), the other lids and the pictograms
    els = cluster(keys=False)
    occ = np.zeros((1080, 1920), bool)
    for i in (0, 1):
        rgba, x0, y0, aux = els[i]
        occ[y0:y0 + rgba.shape[0], x0:x0 + rgba.shape[1]] |= aux['d'] < M['Glass'][1]
    ys, xs = np.mgrid[0:1080, 0:1920]
    place = {}
    bb = [1e9, 1e9, -1e9, -1e9]
    for i, name in enumerate(('dodge', 'jump', 'vehicle')):
        x0, y0, w, h = key_rect(i)
        dx = np.maximum(np.maximum(x0 - (xs + .5), (xs + .5) - (x0 + w)), 0); dy = np.maximum(np.maximum(y0 - (ys + .5), (ys + .5) - (y0 + h)), 0)
        to_vessel = float(np.hypot(dx, dy)[occ].min())
        to_lid = 1e9
        for j in range(3):
            r = mark_rect(j); cx, cy = r[0] + 22, r[1] + 22
            ddx = max(x0 - cx, 0, cx - (x0 + w)); ddy = max(y0 - cy, 0, cy - (y0 + h))
            d = math.hypot(ddx, ddy)
            if j == i: own = d
            else: to_lid = min(to_lid, d - _TH['button']['radius_px'])
        place[name] = dict(rect=[round(v, 1) for v in (x0, y0, w, h)], clear_of_vessels_px=round(to_vessel, 1), clear_of_other_lids_px=round(to_lid, 1),
                           nearest_point_from_own_lid_centre_px=round(own, 2), covers_own_pictogram=bool(own < _TH['button']['glyph_r_px']),
                           bites_own_lid_px=round(_TH['button']['radius_px'] - own, 2))
        bb = [min(bb[0], x0), min(bb[1], y0), max(bb[2], x0 + w), max(bb[3], y0 + h)]
    rects = [LAYOUT['hp'], LAYOUT['ink']] + [mark_rect(i) for i in range(3)]
    cb = [min(r[0] for r in rects), min(r[1] for r in rects), max(r[0] + r[2] for r in rects), max(r[1] + r[3] for r in rects)]
    K['place'] = dict(keys=place, keys_bbox=[round(v, 1) for v in bb], cluster_rect_bbox=[round(v, 1) for v in cb],
                      inside_cluster_bbox=bool(bb[0] >= cb[0] and bb[1] >= cb[1] and bb[2] <= cb[2] and bb[3] <= cb[3]),
                      key_px2_total=round(3 * M['Key'][0] ** 2, 1), key_area_share_of_screen=round(3 * M['Key'][0] ** 2 / (1920 * 1080), 6))
    # ---- legibility at 1080p real size on the brightest and the darkest scene, lacquer keycap against porcelain
    global MIPS
    mips, MIPS = MIPS, True
    scenes = dict(brightest_sky_E4E0D6=SKY, darkest_pine_2A2A26=PINE)
    read = {}
    for style in ('lacquer', 'porcelain'):
        key_style(style)
        face_col, ink_col = M['KeyFace'], M['KeyInk']
        row = {}
        for sname, bg in scenes.items():
            worst_glyph = 99.0; worst_dry = 99.0; peak_cov = 1.0
            for cell in cells:
                for wet, slot in ((1.0, 'wet'), (0.0, 'dry')):
                    rgba, x0, y0, aux = key_element(key_rect(2), cell, wet)
                    comp = bg_over(rgba, bg)
                    core = aux['glyph'] >= aux['glyph'].max() - 1e-6          # the symbol's most covered pixels: the stroke as the eye finds it
                    inside = (aux['inner'] > .99) & (aux['glyph'] < .02) & (aux['lip'] < .02)
                    c = contrast(float(lum(comp[core]).mean()), float(lum(comp[inside]).mean()))
                    if slot == 'wet': worst_glyph = min(worst_glyph, c); peak_cov = min(peak_cov, float(aux['glyph'].max()))
                    else: worst_dry = min(worst_dry, c)
            rgba, x0, y0, aux = key_element(key_rect(2), cells[2], 1.0)
            comp = bg_over(rgba, bg)
            inside = (aux['inner'] > .99) & (aux['glyph'] < .02) & (aux['lip'] < .02)
            rim = aux['rim'] > .9
            face_l, rim_l, bg_l = float(lum(comp[inside]).mean()), float(lum(comp[rim]).mean()), float(lum(bg))
            row[sname] = dict(symbol_vs_keycap=round(worst_glyph, 2), dry_symbol_vs_keycap=round(worst_dry, 2),
                              keycap_face_vs_ground=round(contrast(face_l, bg_l), 2), keycap_hairline_vs_ground=round(contrast(rim_l, bg_l), 2),
                              keycap_shape_vs_ground=round(max(contrast(face_l, bg_l), contrast(rim_l, bg_l)), 2),
                              keycap_face_luminance=round(face_l, 4), symbol_peak_coverage_1080p=round(peak_cov, 3))
        # how loud the keycap is next to its lid (the mark itself): mean luminance of the keycap against the lid's, on the dark scene
        rgba, x0, y0, aux = key_element(key_rect(2), cells[2], 1.0)
        key_l = float(lum(bg_over(rgba, PINE))[aux['face'] > .5].mean())
        lid, _, _, la = element(mark_rect(2), 4, INK, [1, 0, 0, 0], [0, 0, 0, 0], [4, 0, 0, 0])
        lid_l = float(lum(bg_over(lid, PINE))[la['disc'] > .5].mean())
        row['keycap_vs_lid_mean_luminance_dark_scene'] = round(contrast(key_l, lid_l), 2)
        row['max_channel_255'] = round(float(rgba[..., :3][rgba[..., 3] > .01].max()) * 255, 1)
        read[style] = row
    key_style('lacquer')
    K['legibility_1080p'] = read
    L = read['lacquer']; Pc = read['porcelain']
    K['choice'] = dict(shipped='lacquer keycap (face = Lacquer, hairline / lip / symbol = Paper)',
                       why='the symbol stands on lacquer on every scene; the porcelain keycap loses its shape on the brightest scene (face %s:1, only its '
                           '1 px rim holds it) and on the darkest scene is %s x the lid in mean luminance - the key would outshine the mark it belongs to'
                           % (Pc['brightest_sky_E4E0D6']['keycap_face_vs_ground'], Pc['keycap_vs_lid_mean_luminance_dark_scene']))
    # ---- the symbols at 1080p real size: cap height, stroke coverage, how unlike the three live symbols are (16 sub-pixel phases)
    sizes = {}
    worst_overlap = 0.0; min_cap = 99.0; min_cov = 1.0
    stack = {}
    for cell in cells:
        masks = []
        for ph in range(16):
            ox, oy = (ph % 4) / 4.0, (ph // 4) / 4.0
            r = key_rect(2)
            rgba, x0, y0, aux = key_element((r[0] + ox, r[1] + oy, r[2], r[3]), cell, 1.0)
            g = aux['glyph']
            rows = np.nonzero(g.max(axis=1) > .35)[0]; cols = np.nonzero(g.max(axis=0) > .35)[0]
            min_cov = min(min_cov, float(g.max()))
            masks.append((g, len(rows), len(cols)))
        sizes[KEY_NAMES[cell]] = dict(rows_px=[min(m[1] for m in masks), max(m[1] for m in masks)], cols_px=[min(m[2] for m in masks), max(m[2] for m in masks)])
        stack[cell] = masks[0][0]
    for a in cells:
        for c in cells:
            if a >= c: continue
            ga, gc = stack[a], stack[c]
            n = min(ga.shape[0], gc.shape[0]); m_ = min(ga.shape[1], gc.shape[1])
            ga, gc = ga[:n, :m_], gc[:n, :m_]
            # how alike two symbols are on the real pixel grid: shared pixels over pixels either one covers (coverage >= .5)
            worst_overlap = max(worst_overlap, float(((ga >= .5) & (gc >= .5)).sum()) / max(float(((ga >= .5) | (gc >= .5)).sum()), 1.0))
    letter = sizes.get('G', sizes[KEY_NAMES[cells[2]]])
    K['symbols_1080p'] = dict(sizes_px=sizes, letter_cap_rows_px=letter['rows_px'], peak_coverage_min=round(min_cov, 3),
                              most_alike_pair_shared_pixels=round(worst_overlap, 3), atlas=dict(AJ['keys']['symbols'][KEY_NAMES[cells[0]]], name=KEY_NAMES[cells[0]]))
    # ---- states: a dry mark dims its key, a hidden mark has none, reduced motion / an impact frame change nothing
    wet_px, _, _, wa = key_element(key_rect(0), cells[0], 1.0)
    dry_px, _, _, da = key_element(key_rect(0), cells[0], 0.0)
    g = wa['glyph'] > .9
    hidden = cluster(marks=((1, 0, 2), (1, 0, 3), (1, 0, None)))
    shown = cluster()
    normal = cluster(); lit = cluster(impact=((1250, 420), 1.0))
    key_quads = lambda els: [e for e in els if 'glyph' in e[3]]
    same = max(float(np.abs(a[0] - c[0]).max()) for a, c in zip(key_quads(normal), key_quads(lit)))
    K['states'] = dict(dry_symbol_alpha=round(float((da['glyph_a'][g] / np.maximum(wa['glyph_a'][g], 1e-9)).mean()), 4), want=M['Key'][3],
                       hairline_unchanged_when_dry=bool(np.abs(wet_px[..., :3][wa['rim'] > .9] - dry_px[..., :3][da['rim'] > .9]).max() < 1e-9),
                       key_quads_with_all_marks=len(key_quads(shown)), key_quads_with_vehicle_hidden=len(key_quads(hidden)),
                       key_pixels_changed_by_an_impact_frame=same,
                       reduced_motion_or_flash_setting_reach_the_key=False,
                       note='the key quad takes two numbers only: the cell and the wetness of its mark (InkVesselGraphic308.SetKey); '
                            'HudVessels308.Mark sets them whatever reducedMotion / the impact light are')
    MIPS = mips
    return K


def verdicts_actions(A):
    v = {}
    t = A['timeline']
    v['AC-H16.1 flat while the player stands still (first 3 s, last 5 s, after every action settled)'] = t['calm'] and t['rest_max_px'] <= t['limits']['RestPx']
    v['AC-H16.1 every action is answered, a stronger one more (walk < run < dodge)'] = t['responds'] and t['proportional']
    v['AC-H16.1 at rest within 3 s of every action'] = t['settles']
    v['AC-H16.2 the reading never moves with the slosh (level / value drift 0)'] = t['reading_kept']
    v['AC-H16.3 reduced motion: flat throughout the timeline'] = t['reduced_flat']
    v['AC-H16 the timeline holds at 144 fps'] = t['ok_144fps']
    i = A['idle_10s_at_four_levels']
    v['AC-H16.1 10 s of nothing at four levels (low HP too): nothing moves'] = i['tilt_plus_ripple_plus_phase'] == 0 and i['still_throughout']
    m = A['meter']
    v['ActionMeter: a start is handed on within one band, start + stop = 0; steady, placement, slow look, a stop across a pause = 0'] = \
        abs(m['start_sum_mps'] - 4.5) <= m['step_band_mps'] + .01 and abs(m['start_sum_mps'] + m['stop_sum_mps']) < .01 \
        and m['steady_step'] == 0 and m['placement_40m_step'] == 0 and m['slow_look_90dps_step'] == 0 and m['stop_across_a_pause_step'] == 0
    st = A['straight']
    v['AC-H16.9 straight ahead (no sideways part) is answered at 30 / 60 / 144 / 240 fps, flat at one speed'] = all(
        st['walk']['hp_peak_px'][i] >= .4 and st['run']['hp_peak_px'][i] >= 1.0 and st['car']['hp_peak_px'][i] >= 1.0
        and st['walk']['hp_peak_px'][i] < st['run']['hp_peak_px'][i]
        and st['walk']['cruise_max_px'][i] == 0 and st['run']['cruise_max_px'][i] == 0 and st['car']['cruise_max_px'][i] == 0
        and max(st['walk']['settle_s'][i], st['run']['settle_s'][i], st['car']['settle_s'][i]) <= 3.0 for i in range(4))
    q = A['sources']
    v['AC-H16.4 the slosh code never writes Level / Value'] = q['level_or_value_writes_in_drive_kick_jolt_impact'] == 0
    v['AC-H16.5 no idle term in the stage (tremble, camera, target tilt, clock, shader time)'] = q['low_agitation_mentions'] == 0 and q['step_takes_no_motion_input'] \
        and q['camera_read_as_liquid_motion'] == 0 and q['body_is_the_measured_object'] and q['boarding_frame_not_sampled'] \
        and q['unscaled_time_or_clock_in_liquid_model'] == 0 and q['shader_time_terms'] == 0
    return {k: bool(x) for k, x in v.items()}


def verdicts_keys(K):
    v = {}
    q = K['source']; w = K['whitelist']
    v['AC-H15.1 the key glyph comes from the binding (live: Shift, Space, G; no key constant in the HUD code)'] = q['cell_names'] == ['Shift', 'Space', 'G'] \
        and w['key_cell_constants_in_hud_code'] == 0 and w['key_name_literals_in_hud_code'] == 0 and w['presenter_sets_cells_through_lookup'] == 3 \
        and q['rebound_examples']['j'] == key_cell_by_name('J') and q['rebound_examples']['f7'] == key_cell_by_name('Generic') and q['rebound_examples']['none'] == -1
    pl = K['place']
    v['AC-H15.2 keycaps inside the cluster, >= 8 px clear of vessels and other lids, off their own pictogram'] = pl['inside_cluster_bbox'] and all(
        k['clear_of_vessels_px'] >= 8 and k['clear_of_other_lids_px'] >= 8 and not k['covers_own_pictogram'] for k in pl['keys'].values())
    L = K['legibility_1080p']['lacquer']
    v['AC-H15.3 symbol / keycap >= 7:1, dry symbol >= 3:1, keycap / ground >= 3:1 on the brightest and the darkest scene'] = all(
        L[s_]['symbol_vs_keycap'] >= 7 and L[s_]['dry_symbol_vs_keycap'] >= 3 and L[s_]['keycap_shape_vs_ground'] >= 3 for s_ in ('brightest_sky_E4E0D6', 'darkest_pine_2A2A26'))
    sy = K['symbols_1080p']
    v['AC-H15.4 symbols at 1080p: capital height >= 9 px, stroke coverage >= .8, any two share <= half their pixels'] = sy['letter_cap_rows_px'][0] >= 9 \
        and sy['peak_coverage_min'] >= .8 and sy['most_alike_pair_shared_pixels'] <= .5
    st = K['states']
    v['AC-H15.5 a dry mark dims its key, a hidden mark has none, an impact frame changes nothing'] = abs(st['dry_symbol_alpha'] - st['want']) < .01 \
        and st['hairline_unchanged_when_dry'] and st['key_quads_with_all_marks'] == 3 and st['key_quads_with_vehicle_hidden'] == 2 and st['key_pixels_changed_by_an_impact_frame'] == 0
    v['AC-H15.6 the vehicle key path = the key the summon polls'] = q['vehicle_path_matches_summon']
    v['AC-H15.7 five graphics, no text object, one symbol per cell'] = w['graphics_built'] == 5 and w['text_objects_built'] == 0 and w['cells_in_block'] == 48
    v['AC-H9.2 key glyph LDR (<= 230)'] = K['legibility_1080p']['lacquer']['max_channel_255'] <= 230.5
    return {k: bool(x) for k, x in v.items()}


def bg_over(rgba, bg):
    return bg[None, None, :] * (1 - rgba[..., 3:4]) + rgba[..., :3] * rgba[..., 3:4]


SKY = np.array([0xE4, 0xE0, 0xD6]) / 255.0       # DESIGN 2.3: the brightest sky
PINE = np.array([0x2A, 0x2A, 0x26]) / 255.0      # DESIGN 2.3: the dark pine wood


def measure_theme(R):
    """D308-15 on the cluster: what the lacquer neck band and the najeon lids change, and what they must not change."""
    T = {}
    hp_rect, ink_rect = LAYOUT['hp'], LAYOUT['ink']
    was = theme(True, True)
    # ---- the level reading is untouched by the band (AC-H3 / SPEC-UI-THEME-308 AC-T9)
    worst_below = 0.0; changed_rows = []; cov_diff = 0.0
    for rect, code, tint in ((hp_rect, 1, CINNABAR), (ink_rect, 0, INK)):
        for v in (0, .01, .15, .35, .5, .99, 1):
            b = [0, 0, .35 if code else 0, low_hp(v) if code else low_ink(v)]
            theme(True, True); on, _, _, aux_on = element(rect, code, tint, [v, 0, 0, 0], b, [code, 0, 0, 0])
            theme(False, True); off, _, _, aux_off = element(rect, code, tint, [v, 0, 0, 0], b, [code, 0, 0, 0])
            diff = np.abs(on - off).max(axis=2)
            rows = np.nonzero(diff.max(axis=1) > 1e-9)[0]
            y_px = (rows + .5) / aux_on['design_px'] - AJ['margin_px']           # design px of the HP drawing, from the rect top
            changed_rows.append((float(y_px.min()), float(y_px.max())))
            cov_diff = max(cov_diff, float(np.abs(aux_on['body_cov'] - aux_off['body_cov']).max()), float(np.abs(aux_on['surf_a'] - aux_off['surf_a']).max()))
            band_low = _TH['collar']['bottom_px'] + 1.0                          # one px of anti-aliasing under the band
            below = ((np.arange(on.shape[0]) + .5) / aux_on['design_px'] - AJ['margin_px']) > band_low
            worst_below = max(worst_below, float(diff[below].max()))
    theme(True, True)
    full_y = AJ['ref_rect'][1] + AJ['margin_px'] - M['Level'][1] * M['QuadRef'][1]       # design px from the rect top
    T['level_reading'] = dict(
        rgba_max_diff_below_the_band=round(worst_below, 6), body_and_surface_coverage_max_diff=round(cov_diff, 6),
        rows_changed_px=[round(min(a for a, _ in changed_rows), 2), round(max(b for _, b in changed_rows), 2)],
        band_px=[_TH['collar']['top_px'], _TH['collar']['bottom_px']], full_line_px=round(full_y, 2),
        band_clear_of_full_line_px=dict(hp=round(full_y - _TH['collar']['bottom_px'], 2),
                                        ink=round((full_y - _TH['collar']['bottom_px']) * ink_rect[3] / hp_rect[3], 2)),
        H3_1_worst_px=R['H3.1_level']['worst_px'], H3_1_rows=R['H3.1_level']['rows'])
    # ---- the pour thread: how much of it shows (kit review weakness 2). Rows of the centre column, design px of each vessel
    th = {}
    for name, rect, code, tint in (('hp', hp_rect, 1, CINNABAR), ('ink', ink_rect, 0, INK)):
        rgba, _, _, aux = element(rect, code, tint, [.36, 0, 0, 0], [.25, -1.0, .35 if code else 0, 0], [code, 0, 0, 1.0], scale=4.0)
        col = aux['surf'].shape[1] // 2
        thread = aux['thread_a'][:, col] > .5
        hidden = thread & (aux['lacquer_a'][:, col] > .5)
        per = 4.0                                                                 # rendered at 4 screen px per px of a 1080p screen
        first = np.nonzero(thread)[0][0]; first_hidden = np.nonzero(hidden)[0][0]
        th[name] = dict(thread_px=round(float(thread.sum()) / per, 2), hidden_behind_band_px=round(float(hidden.sum()) / per, 2),
                        shows_above_band_px=round(float(first_hidden - first) / per, 2))
    k_ink = ink_rect[3] / hp_rect[3]
    th['first_form_hidden_px'] = dict(hp=round(_TH['collar']['bottom_px'] - 1.0, 2), ink=round((_TH['collar']['bottom_px'] - 1.0) * k_ink, 2),
                                      note='the kit\'s first collar filled the lip and the neck: the thread only appeared under it (computed)')
    T['pour_thread'] = th
    # ---- collar against the ink below it (kit review weakness 3): ink vessel full, real size, mips on, dark and bright ground
    global MIPS
    mips = MIPS; MIPS = True
    sep = {}
    for gname, bg in (('dark_2A2A26', PINE), ('bright_E4E0D6', SKY)):
        rgba, _, _, aux = element(ink_rect, 0, INK, [1.0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0])
        comp = lum(bg_over(rgba, bg))
        col = aux['surf'].shape[1] // 2
        strip = comp[:, col - 6:col + 7].mean(axis=1)                             # 13 px wide column through the neck
        rows = (np.arange(len(strip)) + .5) / aux['design_px'] - AJ['margin_px']   # design px of the HP drawing
        c = _TH['collar']
        lac_rows = (rows > c['top_px'] + .8) & (rows < c['line_px'][0] - .8)
        line_rows = (rows > c['line_px'][0] - .6) & (rows < c['line_px'][1] + .6)
        body_rows = (aux['body_cov'][:, col] > .9) & (aux['surf_a'][:, col] < .02) & (aux['d'][:, col] < -9)
        gap_rows = (rows > c['bottom_px'] + 1.2) & (rows < full_y - 2.5)
        lac, line, ink_l, gap = float(strip[lac_rows].mean()), float(strip[line_rows].max()), float(strip[body_rows].mean()), float(strip[gap_rows].mean())
        sep[gname] = dict(lacquer_vs_ink=round(contrast(lac, ink_l), 2), shell_line_vs_lacquer=round(contrast(line, lac), 2),
                          shell_line_vs_ink=round(contrast(line, ink_l), 2), glass_between_vs_ink=round(contrast(gap, ink_l), 2))
    MIPS = mips
    T['collar_vs_ink_full_vessel_1080p'] = sep
    # ---- lids: shell on lacquer, the dry seat, the lid on the two grounds (4 screen px per design px: material contrasts)
    lids = {}
    for i, (name, code) in enumerate((('dodge', 2), ('jump', 3), ('vehicle', 4), ('vehicle_out', 5))):
        r = mark_rect(min(i, 2))
        wet, _, _, a1 = element(r, code, INK, [1, 0, 0, 0], [0, 0, 0, 0], [code, 0, 0, 0], scale=4.0)
        dry, _, _, a0 = element(r, code, INK, [0, 0, 0, 0], [0, 0, 0, 0], [code, 0, 0, 0], scale=4.0)
        glyph = (a1['shell_a'] > .9) & (a1['rim_line'] < .01)
        seat = (a1['disc'] > .99) & (a1['shell_a'] < .01)
        ring = (a1['shell_a'] > .9) & (a1['rim_line'] >= 1.0)
        lacq = float(lum(wet[..., :3][seat]).mean())
        lids[name] = dict(shell_vs_lacquer=round(contrast(float(lum(wet[..., :3][glyph]).mean()), lacq), 2),
                          dry_seat_vs_lacquer=round(contrast(float(lum(dry[..., :3][glyph]).mean()), lacq), 2),
                          rim_line_vs_pine=round(contrast(float(lum(wet[..., :3][ring]).mean()), float(lum(PINE))), 2),
                          rim_line_when_dry_unchanged=bool(np.abs(wet[..., :3][ring] - dry[..., :3][ring]).max() < 1e-9),
                          lacquer_vs_sky=round(contrast(lacq, float(lum(SKY))), 2))
    T['lids'] = lids
    # ---- LDR: every pixel of the themed cluster in every state (the H9.2 loop ran with the theme on) + shell alone
    shell_max = 0.0
    for name in ('dodge_n', 'jump_n', 'vehicle_n', 'vehicle_out_n'):
        x, y, w, h = AJ['cells'][name]
        ah, aw = ATLAS.shape[:2]
        cellpx = ATLAS[int(round((1 - y - h) * ah)):int(round((1 - y) * ah)), int(round(x * aw)):int(round((x + w) * aw))]
        shell_max = max(shell_max, float(cellpx[..., :3][cellpx[..., 3] > .5].max()))
    arc = np.arange(M['NacreArc'][0], M['NacreArc'][1] + .5, 1.0)[None, :]
    T['ldr'] = dict(cluster_max_channel_255=R['H9.2_max_channel_255'], lid_shell_max_channel_255=round(shell_max * 255, 1),
                    collar_formula_max_channel_255=round(float(nacre(arc).max()) * 255, 1), paper_255=230,
                    shader_time_terms=0, emission_properties=0)
    # ---- nacre and lacquer area of the cluster at 1080p (SPEC-UI-THEME-308 section 4.2: HUD nacre <= .20 % of the screen)
    screen = 1920.0 * 1080.0
    frame_kit = 1558.0          # the minimap frame's nacre, measured by the kit (theme308_check: HUD .116 % = collar + lids + frame)
    T['area'] = dict(nacre_px2_cluster=_TH['nacre_px2_cluster'], nacre_pct_cluster=round(_TH['nacre_px2_cluster'] / screen * 100, 4),
                     nacre_pct_with_minimap_frame=round((_TH['nacre_px2_cluster'] + frame_kit) / screen * 100, 4), limit_pct=.20,
                     lacquer_px2_cluster=_TH['lacquer_px2_cluster'], figure_motifs_on_the_hud=0)
    # ---- impact light: one point, one side. The quad-frame direction got through the uv derivatives is the same on a target
    # whose rows run down (D3D / Vulkan / Metal) and on one whose rows run up, and it points at the point from every element
    pt = (1250.0, 420.0)
    err = 0.0; flip = 0.0; spread = {}
    for name, rect, code in (('hp', hp_rect, 1), ('ink', ink_rect, 0), ('dodge', mark_rect(0), 2), ('jump', mark_rect(1), 3), ('vehicle', mark_rect(2), 4)):
        rgba, x0, y0, aux = element(rect, code, INK, [.5, 0, 0, 0], [0, 0, 0, 0], [code, 1.0, 0, 0], point=pt)
        lx, ly = aux['light']
        hgt, wid = lx.shape
        ys, xs = np.mgrid[y0:y0 + hgt, x0:x0 + wid]
        tx, ty = pt[0] - (xs + .5), -(pt[1] - (ys + .5))                          # the true direction, y up
        n = np.hypot(tx, ty)
        err = max(err, float(np.degrees(np.arccos(np.clip((lx * tx + ly * ty) / n, -1, 1))).max()))
        # rows-up target: pixel y' = Hs - y, the point is written as Hs - py, ddy(uv.y) = +1 / height
        Hs = 1080.0
        ty_up = ((Hs - pt[1]) - (Hs - (ys + .5)))
        flip = max(flip, float(np.abs(ty_up - ty).max()))
        cx, cy = rect[0] + rect[2] / 2, rect[1] + rect[3] / 2
        centre = math.degrees(math.atan2(-(pt[1] - cy), pt[0] - cx))
        ang = np.degrees(np.arctan2(ly, lx))
        spread[name] = dict(to_point_deg=round(centre, 1), per_pixel_deg=[round(float(ang.min()), 1), round(float(ang.max()), 1)])
    T['impact_one_side'] = dict(point_px=list(pt), direction_error_deg_max=round(err, 4), rows_up_vs_rows_down_max_diff_px=round(flip, 6),
                                elements=spread, writers_of_the_globals_in_this_stage=stage_global_writers())
    T['values_vs_kit_tokens'] = tokens_check()
    theme(*was)
    return T


STAGE = os.path.join(ROOT, 'Tools', 'Unity', 'Stage308_hud')
TOKENS = os.path.join(ROOT, 'Tools', 'Unity', 'Stage308_theme', '_ProjectAssets', 'Art', 'UI', 'UI308', 'Theme', 'theme308_tokens.json')


def stage_global_writers():
    """How many times the stage's C# sets a shader global (the impact globals have one writer: the deploy layer)."""
    n = 0
    for base, _, files in os.walk(STAGE):
        for name in files:
            if name.endswith('.cs') or name.endswith('.shader'):
                with open(os.path.join(base, name), encoding='utf-8-sig') as f:
                    text = f.read()
                n += len(re.findall(r'Shader[.]SetGlobal[A-Za-z]+[(]', text))      # calls, not the word in a comment
    return n


def tokens_check():
    """The theme values the HUD carries (twin material = ThemeSpec308 defaults = shader defaults) against the kit's token file."""
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import theme308_lib as KIT          # the lids' face chroma is applied by hud308_najeon from this constant
    with open(TOKENS, encoding='utf-8') as f:
        tk = json.load(f)
    nc = tk['nacre']
    lo, hi = nc['H_centre'] - nc['H_span'] / 2, nc['H_centre'] + nc['H_span'] / 2
    want = dict(Lacquer=tk['colours']['Lacquer'], N0=nc['shader_fit']['N0'], A=nc['shader_fit']['A'], B=nc['shader_fit']['B'],
                families=nc['families'], swing=nc['piece_span'] / 2, period=nc['hue_period'], arc=[lo, hi],
                share=[nc['family_share'][0], nc['family_share'][0] + nc['family_share'][1]], face_chroma=nc['face_chroma'], joint=nc['joint_px'])
    have = dict(Lacquer='#%02X%02X%02X' % tuple(int(round(c * 255)) for c in LACQUER), N0=list(M['NacreN0']), A=list(M['NacreA']), B=list(M['NacreB']),
                families=M['NacreHue'][:3], swing=M['NacreHue'][3], period=M['NacreArc'][2], arc=M['NacreArc'][:2],
                share=[M['NacreArc'][3], M['Theme'][3]], face_chroma=KIT.NACRE_FACE_CHROMA, joint=_TH['collar']['joint_px'])
    same = {k: bool(np.allclose(np.asarray(have[k], float), np.asarray(want[k], float), atol=1e-6)) if k != 'Lacquer' else have[k] == want[k] for k in want}
    # the same literals in the stage's C# defaults and in the shader's property defaults (text search; data, not logic)
    with open(os.path.join(STAGE, 'App', 'World', 'UI', 'HudLiquid308ProfileSO.cs'), encoding='utf-8-sig') as f:
        cs = f.read()
    with open(os.path.join(STAGE, '_ProjectAssets', 'Art', 'UI', 'UI308', 'Shaders', 'InkVessel308.shader'), encoding='utf-8-sig') as f:
        sh = f.read()
    cs_ok = all(t in cs for t in ('new Color32(0x11, 0x0F, 0x0D, 0xFF)', 'new Vector3(.7254f, .7273f, .7280f)', 'new Vector3(.0845f, -.0314f, -.0034f)',
                                  'new Vector3(.0322f, .0003f, -.0900f)', 'new Vector3(170f, 245f, 320f)', 'new Vector2(.42f, .43f)',
                                  'NacreSwingDeg = 28f, NacreHuePeriodPx = 30f', 'new Vector2(150f, 340f)', 'LidDryAlpha = .30f'))
    sh_ok = all(t in sh for t in ('= (0.0667, 0.0588, 0.0510, 1)', '= (0.7254, 0.7273, 0.7280, 0)', '= (0.0845, -0.0314, -0.0034, 0)',
                                  '= (0.0322, 0.0003, -0.0900, 0)', '= (170, 245, 320, 28)', '= (150, 340, 30, 0.42)', '= (1, 1, 0.3, 0.85)'))
    return dict(twin_material_equals_tokens=same, profile_defaults_equal_tokens=bool(cs_ok), shader_defaults_equal_tokens=bool(sh_ok),
                tokens='Tools/Unity/Stage308_theme/_ProjectAssets/Art/UI/UI308/Theme/theme308_tokens.json')


def verdicts_theme(T, before=None):
    v = {}
    lr = T['level_reading']
    v['THEME level reading untouched (AC-H3 / AC-T9)'] = lr['rgba_max_diff_below_the_band'] == 0 and lr['body_and_surface_coverage_max_diff'] == 0 \
        and lr['rows_changed_px'][1] <= lr['band_px'][1] + 1.0 and min(lr['band_clear_of_full_line_px'].values()) >= 5.0
    if before is not None:
        v['THEME AC-H3.1 numbers = before the theme'] = lr['H3_1_rows'] == before['H3.1_level']['rows'] and lr['H3_1_worst_px'] == before['H3.1_level']['worst_px']
    v['THEME pour thread shows above the band (>= 6 px)'] = all(T['pour_thread'][k]['shows_above_band_px'] >= 6.0 for k in ('hp', 'ink'))
    v['THEME shell line separates collar and ink (>= 3:1 both ways, dark ground)'] = \
        T['collar_vs_ink_full_vessel_1080p']['dark_2A2A26']['shell_line_vs_lacquer'] >= 3 and T['collar_vs_ink_full_vessel_1080p']['dark_2A2A26']['shell_line_vs_ink'] >= 3
    v['THEME lids: shell / lacquer >= 7:1, rim line / pine >= 4.5:1, lacquer / sky >= 7:1 (AC-T6)'] = all(
        x['shell_vs_lacquer'] >= 7 and x['rim_line_vs_pine'] >= 4.5 and x['lacquer_vs_sky'] >= 7 and x['rim_line_when_dry_unchanged'] for x in T['lids'].values())
    v['THEME LDR: cluster <= 230, shell <= 217 (AC-H9.2 / AC-T3)'] = T['ldr']['cluster_max_channel_255'] <= 230.5 \
        and T['ldr']['lid_shell_max_channel_255'] <= 217.5 and T['ldr']['collar_formula_max_channel_255'] <= 217.5
    v['THEME nacre area <= .20 % of the screen with the minimap frame (AC-T7)'] = T['area']['nacre_pct_with_minimap_frame'] <= T['area']['limit_pct']
    v['THEME impact light from one point (error <= .1 deg, flip-safe), no writer of the globals here'] = \
        T['impact_one_side']['direction_error_deg_max'] <= .1 and T['impact_one_side']['rows_up_vs_rows_down_max_diff_px'] == 0 \
        and T['impact_one_side']['writers_of_the_globals_in_this_stage'] == 0
    k = T['values_vs_kit_tokens']
    v['THEME values = the kit tokens (twin, profile defaults, shader defaults)'] = all(k['twin_material_equals_tokens'].values()) \
        and k['profile_defaults_equal_tokens'] and k['shader_defaults_equal_tokens']
    if 'vehicle_detail_1080p' in T:
        d = T['vehicle_detail_1080p']
        v['THEME vehicle pictogram keeps its counters at 1080p'] = d['najeon_lid']['counters_1080p'] == d['najeon_lid']['counters']
    return {k: bool(x) for k, x in v.items()}


def verdicts(R):
    v = {}
    bb = R['H1.1_rect_bbox']
    v['AC-H1.1'] = abs(bb[0] - 64) <= 2 and abs(bb[1] - 784) <= 2 and abs(bb[2] - 318) <= 2 and abs(bb[3] - 1012) <= 2 and R['H1.1_inside_safe']
    v['AC-H1.2'] = all(r['clear_of_prompt'] and r['clear_of_boss_bar'] and r['clear_of_minimap'] and r['clear_of_arrival_card'] for r in R['H1.2_resolutions'].values())
    s = R['H1.3_silhouette_px']
    v['AC-H1.3'] = .01 <= s['overlap_share_of_hp'] <= .04 and all(x == 0 for x in R['H1.3_mark_rect_px_on_silhouettes'].values())
    m = R['H2.1_silhouette']
    v['AC-H2.1'] = 1.30 <= m['h_over_w'] <= 1.40 and 1.18 <= m['body_h_over_w'] <= 1.28
    v['AC-H2.2'] = m['atlas_vs_formula_max_px'] <= 1.0
    v['AC-H3.1'] = R['H3.1_level']['worst_px'] <= 1.0 and R['H3.1_level']['liquid_px_above_full_line_at_1'] == 0
    r = R['H3.2_reach']
    v['AC-H3.2'] = r['hp_drain_1'] <= .30 and r['ink_drain_1'] <= .45 and r['hp_fill_03'] <= .7 and r['ink_fill_03'] <= .9 and r['hp_fill_1'] <= 1.0 and r['ink_fill_1'] <= 1.3
    v['AC-H3.4'] = R['H3.4_centre_column_dev_px']['max'] <= R['H3.4_centre_column_dev_px']['allowed']
    st = R['H4.1_impulse']
    v['AC-H4.1'] = abs(st['hp']['peak'] - st['hp']['want']) <= .05 * st['hp']['want'] and st['hp']['swings_over_2pct'] >= 3 \
        and abs(st['ink']['peak'] - st['ink']['want']) <= .05 * st['ink']['want'] and st['ink']['swings_over_2pct'] <= 2
    v['AC-H4.2'] = R['H4.2_fps_spread'] <= .01
    v['AC-H4.3'] = all(x is not None and x <= 3 for x in R['H4.3_rest_seconds'].values())
    f = R['H5.1_hp_film']
    v['AC-H5.1'] = abs(f['top'] - .8) < 1e-6 and abs(f['alpha_at_drop'] - .34) <= .03 and abs(f['alpha_end_of_hold'] - .34) <= .03 and abs(f['gone_at_s'] - .8) <= .02
    v['AC-H5.2'] = 3.0 <= R['H5.2_ink_film_gone_at_s'] <= 5.0
    v['AC-H5.3'] = all(x >= 2.0 for row in R['H5.3_film_vs_body_contrast'].values() for x in row.values())
    g = R['H5.4_regen']
    v['AC-H5.4'] = g['thread'] == 0 and g['ripple'] == 0 and g['tilt'] == 0 and not g['fresh'] and g['jump_pours'] and g['pour_stirs_px'] > 0
    v['AC-H6.1'] = R['H6.1_hp_ink_mix']['at_018'] > .08 and R['H6.1_hp_ink_mix']['at_035'] == 0
    c = R['H6.2_ink_body_coverage']
    v['AC-H6.2'] = .45 <= c['ink_008_low'] <= .65 and c['ink_040'] >= .95
    v['AC-H6.3'] = R['H6.3_two_frames_same_input_max_diff_255'] <= 1
    i = R['H8.1_impact_readability']
    # the whole line (the 9 px impact-shadow band by the far wall included) and its centre part are judged separately
    v['AC-H8.1'] = i['centre_level_shift_px'] <= 2 and i['surface_line_contrast_min'] >= 3 and i['hp_pixels_within_8deg_hue_min'] >= .8
    v['AC-H8.1 (line centre only)'] = i['centre_level_shift_px'] <= 2 and i['surface_line_contrast_min_centre'] >= 3 and i['hp_pixels_within_8deg_hue_min'] >= .8
    v['AC-H9.2'] = R['H9.2_max_channel_255'] <= 230.5
    v['AC-H10.2'] = R['H10.2_rect_area_share'] <= .022
    return {k: bool(x) for k, x in v.items()}


# ============================================================ sheet
def font(size):
    try: return ImageFont.truetype('C:/Windows/Fonts/NotoSansKR-Bold.ttf', size)
    except Exception:
        try: return ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf', size)
        except Exception: return ImageFont.load_default()


def sheet():
    states = [
        ('평상  체력 62 · 먹 44', dict()),
        ('회피 직후  기울기 + 물결 (행동의 답)', dict(hp_tilt=.22, ink_tilt=-.16, hp_wave=3.0, ink_wave=1.8)),
        ('잃은 몫  젖은 자국 (체력 84→62, 먹 70→44)', dict(hp_wet=(.84, .34), ink_wet=(.70, .30))),
        ('붓는 중  먹 +.3 (실 · 새 몫 담묵)', dict(ink=.55, ink_level=.36, ink_fresh=.25, pour=1.0, ink_wave=1.2)),
        ('낮음  체력 18 · 먹 8 (갈필) · 표시 마름 · 수면 잔잔', dict(hp=.18, ink=.08, marks=((0, 0, 2), (0, 0, 3), (0, 0, 4)))),
        ('먹 비용  술식 .15 (점선) · 회피 되젖는 중', dict(cost=.15, marks=((0, .5, 2), (1, 0, 3), (1, 0, 5)))),
        ('임팩트 프레임  빛 = 오른쪽 위', dict(impact=((1250, 420), 1.0), hp_tilt=-.2, ink_tilt=-.12, hp_wave=2.0, ink_wave=1.2)),
        ('첫 의뢰 전  자동차 표시 없음', dict(marks=((1, 0, 2), (1, 0, 3), (1, 0, None)))),
    ]
    crop = (40, 760, 360, 1040)
    zoom = 3
    cw, ch = (crop[2] - crop[0]) * zoom, (crop[3] - crop[1]) * zoom
    rows = []
    for bg_name in ('road', 'palace'):
        path = os.path.join(ROOT, 'Art', 'UI304', 'clean', bg_name + '.png')
        bg = np.asarray(Image.open(path).convert('RGB'), float) / 255 if os.path.exists(path) else np.full((1080, 1920, 3), .5)
        cells = []
        for _, kw in states:
            # rendered at 3 screen px per design px (a 3x canvas), so the sheet shows the shader's own edges, not a resize
            big = np.asarray(Image.fromarray((bg[crop[1]:crop[3], crop[0]:crop[2]] * 255).astype(np.uint8)).resize((cw, ch), Image.BICUBIC), float) / 255
            els = cluster(scale=zoom, **kw)
            shifted = [(rgba, x0 - crop[0] * zoom, y0 - crop[1] * zoom, aux) for rgba, x0, y0, aux in els]
            paste(big, shifted)
            cells.append(big)
        rows.append(np.vstack([np.hstack(cells[:4]), np.hstack(cells[4:])]))
    img = Image.fromarray((np.clip(np.vstack(rows), 0, 1) * 255 + .5).astype(np.uint8))
    dr = ImageDraw.Draw(img)
    f = font(26)
    for r in range(4):
        for c in range(4):
            label = states[(r % 2) * 4 + c][0]
            x, y = c * cw + 12, r * ch + 10
            dr.rectangle([x - 4, y - 2, x + 8 + dr.textlength(label, font=f), y + 36], fill=(20, 20, 19))
            dr.text((x, y), label, fill=(230, 226, 215), font=f)
    os.makedirs(OUT, exist_ok=True)
    out = os.path.join(OUT, 'twin_states_v1.png')
    img.save(out)
    img.resize((img.width // 2, img.height // 2), Image.LANCZOS).convert('RGB').save(os.path.join(OUT, 'twin_states_v1_preview.jpg'), quality=88)
    return out


def dump_report(report):
    """indent 1, but the long per-frame arrays of the timeline trace on one line each."""
    trace = report['timeline'].pop('trace')
    text = json.dumps(report, ensure_ascii=False, indent=1, default=float)
    rows = ',\n'.join('   "%s": %s' % (k, json.dumps([round(x, 6) for x in v], separators=(',', ':'))) for k, v in trace.items())
    report['timeline']['trace'] = trace
    marker = '\n "timeline": {'
    i = text.rindex(marker) + len(marker)
    return text[:i] + '\n  "trace": {\n' + rows + '\n  },' + text[i:]


def plot_timeline():
    """Art/UI308/HUD/hud308_slosh_timeline.png: the scripted timeline of AC-H16 as the STAGE C# ran it (the trace in
    simcheck308.json, written by Tools/Unity/Stage308_hud/Offline/simcheck308.py). Small multiples over one time axis: what the
    eye sees move (px at the glass), the slope, the ripple, and the reading. The actions are the shaded spans.
    Form and colour follow the dataviz method: change over time = lines, one axis per panel (no dual axis), two series in a
    validated pair (orange / blue: validate_palette.js all checks pass on #fcfcfb), text in text tokens, a legend and
    selective direct labels, hairline grid."""
    with open(os.path.join(OUT, 'simcheck308.json'), encoding='utf-8') as f:
        sim = json.load(f)
    tl = sim['csharp']['timeline']
    tr = tl['trace']
    T = tr['t']
    W, H = 2400, 1640
    SURFACE, TEXT, TEXT2, MUTED, GRID, BAND = (252, 252, 251), (11, 11, 11), (82, 81, 78), (128, 126, 120), (232, 231, 227), (240, 239, 235)
    HP_C, INK_C = (235, 104, 52), (42, 120, 214)                     # series 2 orange = HP (주묵), series 1 blue = ink
    im = Image.new('RGB', (W, H), SURFACE)
    dr = ImageDraw.Draw(im)

    def fnt(size, bold=False):
        try: return ImageFont.truetype('C:/Windows/Fonts/NotoSansKR-Bold.ttf' if bold else 'C:/Windows/Fonts/NotoSansKR-Regular.ttf', size)
        except Exception: return font(size)
    L, R_ = 150, W - 250
    x_of = lambda t: L + (R_ - L) * t / 39.5
    ko = dict(zip(tl['segments'], ('서 있음', '걷기', '달리기', '회피', '점프·착지', '급한 시점 전환', '피격', '먹을 씀', '먹을 채움', '임팩트 프레임', '서 있음')))
    starts, ends = tl['segment_start'], tl['action_end']
    dr.text((L, 34), '병 속 액체는 플레이어의 행동에만 답한다 — 타임라인 (D308-11b · AC-H16)', fill=TEXT, font=fnt(40, True))
    dr.text((L, 92), '스테이지 C#(ActionMeter308 → LiquidSim308.Frame)을 Unity 밖에서 60 fps로 돌린 값. 회색 띠 = 플레이어가 행동하는 동안, 그 밖 = 가만히 서 있음.',
            fill=TEXT2, font=fnt(24))
    panels = (('수면이 움직인 크기 — 유리 벽에서 (px)', 'hp_amp', 'ink_amp', 0.0, None, '%.0f'),
              ('수면 기울기 (높이 / 폭)', 'hp_tilt', 'ink_tilt', None, None, '%.2f'),
              ('잔물결 진폭 (px)', 'hp_wave', 'ink_wave', 0.0, None, '%.0f'),
              ('읽는 값 = 수위 (0–1): 출렁임이 바꾸지 않는다', 'hp_level', 'ink_level', 0.0, 1.0, '%.1f'))
    top = 190; ph = (310, 250, 210, 210); gap = 96
    y0 = top
    for pi, (title, ka, kb, lo, hi, fmt) in enumerate(panels):
        h = ph[pi]
        va, vb = tr[ka], tr[kb]
        mx = max(max(abs(v) for v in va), max(abs(v) for v in vb))
        if lo is None:
            step = .05 if mx < .22 else .1
            top_v = math.ceil(mx / step) * step; lo_v, hi_v = -top_v, top_v
        else:
            step = (4 if mx > 8 else 1) if hi is None else .5
            lo_v, hi_v = lo, (hi if hi is not None else max(step, math.ceil(mx / step) * step))
        y_of = lambda v, y0=y0, h=h, lo_v=lo_v, hi_v=hi_v: y0 + h - (v - lo_v) / (hi_v - lo_v) * h
        # the action spans (recessive bands) and the segment starts
        for k in range(len(starts)):
            if ends[k] > starts[k]:
                dr.rectangle([x_of(starts[k]), y0, x_of(ends[k]), y0 + h], fill=BAND)
            elif k not in (0, len(starts) - 1):
                dr.rectangle([x_of(starts[k]) - 2, y0, x_of(starts[k]) + 2, y0 + h], fill=BAND)
        # hairline grid + ticks (clean numbers)
        v = lo_v
        while v <= hi_v + 1e-9:
            y = y_of(v)
            dr.line([L, y, R_, y], fill=GRID, width=1)
            dr.text((L - 14, y), fmt % v if abs(v) > 1e-9 else '0', fill=TEXT2, font=fnt(20), anchor='rm')
            v += step
        dr.text((L, y0 - 40), title, fill=TEXT, font=fnt(26, True))
        for vals, col in ((vb, INK_C), (va, HP_C)):
            pts = [(x_of(T[i]), y_of(vals[i])) for i in range(len(T))]
            dr.line(pts, fill=col, width=4, joint='curve')
        # end labels beside the line ends (text in the text token, a line key beside it carries the colour)
        ea, eb = y_of(va[-1]), y_of(vb[-1])
        if abs(ea - eb) < 30:                                        # too close: stack them, the higher line's label on top
            mid = (ea + eb) / 2
            ea, eb = (mid - 16, mid + 16) if ea <= eb else (mid + 16, mid - 16)
        for name, col, yy in (('체력 액체', HP_C, ea), ('먹 액체', INK_C, eb)):
            dr.line([R_ + 14, yy, R_ + 46, yy], fill=col, width=4)
            dr.text((R_ + 56, yy), name, fill=TEXT, font=fnt(22), anchor='lm')
        if pi == 0:
            # selective direct labels: the peak of the liquid each action is about, and where the surface is flat again
            for k, seg in enumerate(tl['segments']):
                if k in (0, len(starts) - 1): continue
                key = 'ink_peak_px' if seg in ('ink spent', 'ink refill') else 'hp_peak_px'
                series = tr['ink_amp'] if key == 'ink_peak_px' else tr['hp_amp']
                nxt = starts[k + 1]
                idx = [i for i in range(len(T)) if starts[k] - 1e-6 <= T[i] < nxt]
                ip = max(idx, key=lambda i: series[i])
                px, py = x_of(T[ip]), y_of(series[ip])
                dr.ellipse([px - 6, py - 6, px + 6, py + 6], fill=INK_C if key == 'ink_peak_px' else HP_C, outline=SURFACE, width=2)
                dr.text((px + 12, max(y0 + 12, py - 4)), '%.1f px' % tl[key][k], fill=TEXT, font=fnt(21, True), anchor='ls')
                # where the surface is a flat line again: a tick under the baseline (clear of the curves and of the peak labels)
                settle = ends[k] + tl['settle_s'][k]
                sx = x_of(settle)
                dr.line([sx, y_of(0), sx, y_of(0) + 12], fill=MUTED, width=2)
                dr.text((sx, y_of(0) + 14), '멎음 +%.1f s' % tl['settle_s'][k], fill=TEXT2, font=fnt(18), anchor='ma')
            # the action names over the first panel
            for k, seg in enumerate(tl['segments']):
                cx = (x_of(starts[k]) + x_of(starts[k + 1] if k + 1 < len(starts) else 39.5)) / 2 if k in (0, len(starts) - 1) else x_of(starts[k])
                dr.text((cx if k in (0, len(starts) - 1) else cx + 4, y0 + 8), ko[seg], fill=TEXT2, font=fnt(20, True), anchor='ma' if k in (0, len(starts) - 1) else 'la')
        y0 += h + gap
    # time axis under the last panel
    base = y0 - gap
    for t in range(0, 40, 5):
        dr.line([x_of(t), base, x_of(t), base + 8], fill=MUTED, width=1)
        dr.text((x_of(t), base + 12), '%d s' % t, fill=TEXT2, font=fnt(20), anchor='ma')
    # the checks, as text (the same numbers as simcheck308.json)
    lim = tl['limits']
    lines = ['가만히 서 있는 %d프레임(처음 3초 · 끝 5초 · 행동마다 멎은 뒤)의 수면 진폭 최대 %s px — 수평선 그대로.  행동 뒤 멎기까지 가장 길게 %.2f초(한도 %.0f초).'
             % (tl['rest_frames'], ('%g' % tl['rest_max_px']), max(tl['settle_s']), lim['settle_s']),
             '세기에 비례: 걷기 %.1f < 달리기 %.1f < 회피 %.1f px (체력 액체).  읽는 값은 행동을 뺀 실행과 수위 차 %g · 값 차 %g.  "움직임 줄이기": 기울기 %g · 물결 %g.'
             % (tl['hp_peak_px'][1], tl['hp_peak_px'][2], tl['hp_peak_px'][3], tl['level_drift_max'], tl['value_drift_max'], tl['reduced_max_tilt'], tl['reduced_max_wave_px'])]
    yy = base + 66
    for line in lines:
        dr.text((L, yy), line, fill=TEXT, font=fnt(23)); yy += 38
    dr.text((L, yy + 4), '표로 볼 값: Art/UI308/HUD/simcheck308.json (csharp.timeline). 픽셀 = 기울기 × 용기 속 반폭(체력 %g · 먹 %g px) + 잔물결.  셰이더 · 게임 화면이 아니라 모델의 실행이다.'
            % tuple(tl['half_width_px']), fill=TEXT2, font=fnt(20))
    out = os.path.join(OUT, 'hud308_slosh_timeline.png')
    im.crop((0, 0, W, min(H, yy + 60))).save(out)
    return out


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    if '--plot' in sys.argv:
        print(plot_timeline()); return
    R = measure()
    V = verdicts(R)
    A = measure_actions()
    K = measure_keys()
    V.update(verdicts_actions(A)); V.update(verdicts_keys(K))
    T = measure_theme(R)
    if '--fast' not in sys.argv:          # what survives of the vehicle pictogram at 1080p (about half a minute)
        sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
        import hud308_najeon as NJ
        T['vehicle_detail_1080p'] = dict(najeon_lid=NJ.detail('vehicle'), kit_first_form_ink_pictogram_at_074=NJ.detail('vehicle', 'kit'),
                                         vehicle_out_lid=NJ.detail('vehicle_out'),
                                         rule='counters = lacquer areas enclosed by shell (window slots, spaces between spokes); 1 px per '
                                              'design px, 16 sub-pixel phases, a pixel is shell at >= 50 % coverage, the worst phase counts')
    before = None
    prev = os.path.join(OUT, 'twin_report.json')
    if os.path.exists(prev):
        with open(prev, encoding='utf-8') as f:
            old = json.load(f)
        before = old.get('before_theme') or (old['measured'] if 'theme' not in old else None)
    VT = verdicts_theme(T, before)
    V.update(VT)
    report = dict(spec='SPEC-HUD-LIQUID-308', twin='Tools/Art/hud308_twin.py', atlas_sha256=AJ['atlas']['sha256'],
                  note='sRGB composite, mip 0 (the 1080p collar profile and the key glyphs use the mip chain), profile code defaults, theme on; not the GPU',
                  within_ac=V, measured=R, theme=T, actions={k: v for k, v in A.items() if k != 'trace'}, keys=K,
                  timeline=dict(A['timeline'], trace=A['trace']))
    if before is not None:
        report['before_theme'] = {k: before[k] for k in ('H3.1_level', 'H3.4_centre_column_dev_px', 'H8.1_impact_readability', 'H9.2_max_channel_255') if k in before}
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, 'twin_report.json'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(dump_report(report))
    print(json.dumps(dict(within_ac=V, outside=[k for k, x in V.items() if not x]), ensure_ascii=False, indent=1))
    if '--report' not in sys.argv:
        print(sheet())


if __name__ == '__main__':
    main()
