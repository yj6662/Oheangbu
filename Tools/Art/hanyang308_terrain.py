"""#308 Hanyang, phase 1a - stage n7 -> stage n8: the ground of the capital. Offline, deterministic. SPEC-WORLD-HANYANG-308.

  python Tools/Art/hanyang308_terrain.py sites          # site list (proportions of the real city) -> Tools/Unity/Stage308_hanyang/sites308.json
  python Tools/Art/hanyang308_terrain.py build [--dry]  # sites308.json + height_pn7 -> plan/Stage/height_pn8.bytes (+ sha, report) and the measured hand-over
                                                        # Tools/Unity/Stage308_hanyang/hanyang308.json (version 308.hanyang.p1a)

Unlike the cliff stages this one cuts as well as fills (terraces, platforms, graded streets), but only inside the capital's wall line
(+ the bench of the new wall) and never on a wet cell, a protected cell, under a kept object or under the trails. The seal is judged the
same way as stage n7 (cliffs308.seal_report: a walker from the sealed EA play area) plus the bare-terrain 45 deg walk before / after.
"""
import json, math, sys
from pathlib import Path
import numpy as np
from scipy import ndimage as ndi

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B                                                # noqa: E402
from cliff308_base import CELL                                             # noqa: E402

ST = B.ROOT / 'Tools/Unity/Stage308_hanyang'
SITES = ST / 'sites308.json'
PREV = B.PL / 'Stage/height_pn7.bytes'
PREV_SHA = '7a5e9d3ef7f2177eaabb80a156cd385236ac8a9858ccd0b2d7fc4fe868dab1c8'
OUT = B.PL / 'Stage/height_pn8.bytes'
LAT = B.ROOT / 'Docs/Handoff/Recovery308/candidates/scratch_plan_1a_20261003/Art/World/Compact/Rebuild/CliffBoundary308/plan/_work/lat308-576175e43abb-90f234b8da93.json'
VERSION = '308.hanyang.p1a.1'

# ------------------------------------------------------------------ the city in numbers (world XZ metres, x east, z north)
PAL = dict(x0=1794.0, x1=2124.0, z0=3322.0, z1=3852.0)          # the palace wall rectangle (330 x 530)
REAL = dict(w=430.0, d=690.0)                                   # Gyeongbokgung, wall rectangle (rounded; plan 1.3)
S = (PAL['x1'] - PAL['x0']) / REAL['w']                         # one scale for both axes
AX = (PAL['x0'] + PAL['x1']) / 2                                # the axis Gwanghwamun -> Geunjeongjeon -> Gyotaejeon
P0 = 60.0                                                       # level of the first court (m)
WALL = [  # the circuit, clockwise from the south-west. new = built by this job, kept = #297 wall chunks that stay
    ('A', 1650, 2555, 'kept'), ('B', 2250, 2555, 'kept'), ('C', 2360, 3005, 'kept'), ('D', 2310, 3360, 'kept'), ('Q', 2231, 3497, 'new'), ('R', 2236, 3540, 'new'),
    ('S', 2292, 3660, 'new'), ('N2', 2292, 3850, 'new'), ('N3', 2150, 3880, 'new'), ('N4', 1900, 3890, 'new'), ('N5', 1800, 3900, 'new'), ('N6', 1720, 3790, 'new'),
    ('F', 1670, 3600, 'new'), ('J', 1661, 3570, 'kept'), ('G', 1612, 3354, 'new'), ('W2', 1460, 3300, 'new'), ('W3', 1430, 3050, 'new'), ('W4', 1500, 2880, 'new'),
    ('W5', 1650, 2881, 'kept')]


def pal(xr, yr):
    """real metres from the centre of Gwanghwamun (x east, y north) -> world XZ"""
    return [round(AX + xr * S, 1), round(PAL['z0'] + yr * S, 1)]


def psite(sid, name, xr, yr, wr, dr, kind='court', **kw):
    c = pal(xr, yr); return dict(id=sid, name=name, group='palace', kind=kind, x=c[0], z=c[1], w=round(wr * S, 1), d=round(dr * S, 1), real=dict(x=xr, y=yr, w=wr, d=dr), **kw)


def band_levels():
    """the palace steps up northward: real y (m north of Gwanghwamun) -> level"""
    return [(-1e9, P0), (135, P0 + 0.5), (218, P0 + 1.0), (350, P0 + 1.6), (398, P0 + 2.2), (455, P0 + 2.8), (498, P0 + 3.6), (505, P0 + 4.4), (512, P0 + 5.2), (520, P0 + 6.0)]


def make_sites():
    P = []
    # --- Gyeongbokgung: positions and sizes are the real ones (approximate, see the Spec for sources) times S
    P += [psite('gbg_gwanghwamun', '광화문', 0, 0, 36, 15, 'gate', note='석축 3홍예 + 중층 문루'),
          psite('gbg_woldae', '광화문 월대', 0, -32, 30, 49, 'terrace', note='길이 48.7 m · 폭 29.7 m(2023 복원 값)'),
          psite('haetae_w', '해태상(서)', -24, -60, 4, 5, 'statue'), psite('haetae_e', '해태상(동)', 24, -60, 4, 5, 'statue'),
          psite('gbg_sipjagak_e', '동십자각', 215, 0, 10, 10, 'tower'), psite('gbg_sipjagak_w', '서십자각', -215, 0, 10, 10, 'tower'),
          psite('gbg_court_outer', '광화문-흥례문 마당', 0, 67, 190, 120, 'court'),
          psite('gbg_heungnyemun', '흥례문', 0, 135, 22, 11, 'gate'), psite('gbg_court_middle', '흥례문-근정문 마당(영제교 · 금천)', 0, 177, 120, 72, 'court'),
          psite('gbg_yeongjegyo', '영제교', 0, 168, 10, 14, 'bridge', note='길이 13.85 m · 너비 9.8 m'), psite('gbg_geumcheon', '금천(마른 도랑)', 0, 168, 120, 6, 'channel'),
          psite('gbg_geunjeongmun', '근정문', 0, 218, 20, 11, 'gate'), psite('gbg_court_jo', '조정(근정전 행각 일곽)', 0, 284, 112, 128, 'court', note='행각 사방, 품계석 12쌍'),
          psite('gbg_geunjeongjeon', '근정전(상 · 하월대)', 0, 300, 56, 56, 'hall', note='정면 5칸 · 측면 5칸 중층'), psite('gbg_pumgye', '품계석 12쌍', 0, 252, 14, 46, 'markers'),
          psite('gbg_sajeongjeon', '사정전(만춘전 · 천추전)', 0, 372, 86, 44, 'hall'), psite('gbg_gangnyeongjeon', '강녕전', 0, 426, 100, 52, 'hall'),
          psite('gbg_gyotaejeon', '교태전', 0, 477, 84, 40, 'hall', note='정면 9칸'), psite('gbg_amisan', '아미산 화계', 0, 509, 64, 22, 'garden', note='4단 화계 + 굴뚝 4'),
          psite('gbg_gyeonghoe_pond', '경회루 방지', -126, 408, 128, 113, 'pond', note='동서 128 m · 남북 113 m'), psite('gbg_gyeonghoeru', '경회루', -108, 408, 34, 28, 'hall', note='정면 7칸 · 측면 5칸 2층'),
          psite('gbg_sujeongjeon', '수정전 · 궐내각사', -118, 292, 96, 76, 'court'), psite('gbg_donggung', '동궁(자선당 · 비현각)', 112, 318, 90, 80, 'court'),
          psite('gbg_sojubang', '소주방', 104, 428, 80, 60, 'court'), psite('gbg_jagyeongjeon', '자경전', 112, 506, 76, 60, 'court'),
          psite('gbg_hyangwon_pond', '향원지', 112, 602, 86, 70, 'pond', note='실제는 축에서 동쪽 약 24 m - 여기서는 골 바닥(동쪽 88 m)으로 옮겼다'), psite('gbg_hyangwonjeong', '향원정', 112, 602, 10, 10, 'pavilion', note='2층 육각 정자 + 취향교'),
          psite('gbg_geoncheong', '건청궁', 124, 660, 80, 40, 'court', note='향원지를 따라 동쪽으로'), psite('gbg_taewonjeon', '태원전', -140, 640, 80, 64, 'hillside', note='백악 기슭 - 땅을 따르는 단'),
          psite('gbg_geonchunmun', '건춘문', 215, 24, 12, 18, 'gate', note='실제는 동장 남쪽 1/3 지점(북으로 약 255 m) - 응봉 격 언덕이 동장에 붙어 있어 남동 모서리 쪽으로 내렸다'), psite('gbg_yeongchumun', '영추문', -215, 255, 12, 18, 'gate'), psite('gbg_sinmumun', '신무문', -20, 690, 18, 12, 'gate')]
    ya = 3176.0   # south end of the avenue (the river bank)
    P += [dict(id='yukjo_street', name='육조거리', group='yukjo', kind='avenue', x=AX, z=(ya + PAL['z0']) / 2, w=34.0, d=PAL['z0'] - ya, level=P0 - 0.2),
          dict(id='yukjo_plaza', name='황토현(육조거리 남단 마당)', group='yukjo', kind='plaza', x=AX - 2, z=ya + 8, w=38.0, d=16.0, level=P0 - 0.2)]
    def lot(sid, name, x0, x1, z0, z1, **kw): return dict(id=sid, name=name, group='yukjo', kind='office', x=(x0 + x1) / 2, z=(z0 + z1) / 2, w=x1 - x0, d=z1 - z0, **kw)
    xe0, xe1, xw0, xw1 = AX + 21, AX + 57, AX - 61, AX - 21
    P += [lot('yukjo_e0', '의정부', xe0, xe1, 3270, 3316, level=P0, door='W'), lot('yukjo_e1', '이조', xe0, xe1, 3232, 3266, level=P0, door='W'), lot('yukjo_e2', '한성부', xe0, xe1, 3196, 3228, level=P0, door='W'),
          lot('yukjo_w0', '예조', xw0, xw1, 3274, 3316, level=P0, door='E'), lot('yukjo_w1', '중추부', xw0, xw1, 3238, 3270, level=P0, door='E'), lot('yukjo_w2', '사헌부', xw0, xw1, 3202, 3234, level=P0, door='E'),
          lot('yukjo_s0', '병조', 1870, 1892, 3192, 3238, door='S', note='새문안로 북편으로 꺾인 줄(실제는 육조거리 서편)'), lot('yukjo_s1', '호조', 1848, 1870, 3192, 3238, door='S', note='실제는 육조거리 동편'),
          lot('yukjo_s2', '형조', 1826, 1848, 3192, 3238, door='S'), lot('yukjo_s3', '공조', 1804, 1826, 3192, 3238, door='S'),
          lot('gwoloe_w', '궐외각사 터(사복시 · 군기시 격)', 1804, 1894, 3244, 3316, level=P0, door='E', note='옛 궁성 가운데 마당(56 m)을 메운 자리')]
    def site(sid, name, group, kind, x, z, w, d, **kw): return dict(id=sid, name=name, group=group, kind=kind, x=x, z=z, w=w, d=d, **kw)
    P += [site('jongmyo_jeongjeon', '종묘 정전(월대 68 x 43)', 'ritual', 'court', 2240, 3252, 96, 62, note='정전 101 m · 월대 109 x 69 m의 1 : 1.6'),
          site('jongmyo_yeongnyeong', '종묘 영녕전', 'ritual', 'court', 2226, 3199, 60, 32), site('jongmyo_gate', '종묘 외대문 마당', 'ritual', 'court', 2181, 3236, 14, 40),
          site('sajik', '사직단(사단 · 직단, 유, 홍살문 4)', 'ritual', 'court', 1694, 3250, 64, 64, note='단 한 변 2장 5자(약 7.6 m)'),
          site('changdeok', '창덕궁(돈화문 · 인정문 · 인정전)', 'palace2', 'terraces', 2170, 3480, 68, 80, bands=3), site('gyeonghui', '경희궁(흥화문 · 숭정전)', 'palace2', 'terraces', 1508, 3242, 70, 96, bands=2),
          site('seonggyun', '성균관 · 문묘', 'school', 'terraces', 2186, 3790, 60, 84, bands=3), site('jonggak', '종각', 'landmark', 'court', 1947, 2964, 12, 12),
          site('tapgol', '원각사지 석탑(탑골)', 'landmark', 'court', 2132, 3170, 16, 16), site('hullyeon', '훈련원 연무장', 'office', 'court', 2180, 2880, 90, 60), site('hyemin', '혜민서', 'office', 'court', 2075, 2905, 30, 26),
          site('apron_donui', '돈의문 안마당', 'gate', 'apron', 1452, 3170, 16, 22), site('apron_sukjeong', '숙정문 안마당', 'gate', 'apron', 1985, 3879, 22, 14), site('apron_hyehwa', '혜화문 안마당', 'gate', 'apron', 2283, 3752, 16, 22),
          site('apron_geonchun', '건춘문 밖 마당', 'gate', 'apron', 2134, 3348, 12, 16, level=P0 + 0.6)]
    zones = [dict(id='namdaemunro', name='남대문로 시전', x0=1970, x1=2046, z0=2600, z1=2925, step=1.0), dict(id='namchon', name='남촌', x0=2050, x1=2250, z0=2595, z1=2925, step=1.5),
             dict(id='seosomun_an', name='서소문 안 마을', x0=1665, x1=1955, z0=2580, z1=2820, step=1.5), dict(id='seochon', name='서촌(돈의문 안)', x0=1450, x1=1630, z0=2940, z1=3140, step=1.5),
             dict(id='bukchon', name='북촌(두 궁 사이)', x0=2130, x1=2200, z0=3335, z1=3432, step=1.5), dict(id='unjong', name='운종가 시전행랑(종로 변)', road='rd_jongno', half=22, step=1.0),
             dict(id='saemunan_lots', name='새문안로 변', road='rd_saemunan', half=20, step=1.0)]
    roads = [dict(id='rd_namdaemun_w', name='남대문로(서 줄)', width=6, grade=0.14, route='capital_center__jeokro', clip=[1990, 2560, 2030, 2962]),
             dict(id='rd_namdaemun_e', name='남대문로(동 줄)', width=6, grade=0.14, route='south_gate__capital_center', clip=[1990, 2560, 2030, 2962]),
             dict(id='rd_jongno', name='종로', width=8, grade=0.12, route='hyeongang__capital_center', clip=[1990, 2950, 2240, 3500]),
             dict(id='rd_yukjo_link', name='광통교 북단 - 황토현', width=8, grade=0.10, points=[[1866, 3102], [1890, 3130], [1916, 3160], [1938, 3184]]),
             dict(id='rd_saemunan', name='새문안로', width=8, grade=0.08, points=[[1938, 3184], [1800, 3178], [1640, 3172], [1560, 3170], [1452, 3170]]),
             dict(id='rd_seosomun', name='서소문로', width=6, grade=0.12, points=[[1660, 2650], [1800, 2646], [1990, 2640]]),
             dict(id='rd_songgi_s', name='송기교 길(남)', width=5, grade=0.14, points=[[1800, 2646], [1770, 2760], [1752, 2852]]),
             dict(id='rd_songgi_n', name='송기교 길(북)', width=5, grade=0.14, points=[[1752, 2924], [1778, 3010], [1800, 3100], [1800, 3174]]),
             dict(id='rd_donhwamun', name='돈화문로', width=6, grade=0.10, points=[[2189, 3379], [2176, 3410], [2170, 3438]]),
             dict(id='rd_bukchon', name='북촌길', width=4, grade=0.16, points=[[2182, 3384], [2140, 3380], [2132, 3440], [2130, 3518]]),
             dict(id='rd_hyehwa', name='혜화문길', width=5, grade=0.16, points=[[2206, 3520], [2240, 3600], [2262, 3700], [2276, 3752]]),
             dict(id='rd_gwanghui', name='광희문길', width=4, grade=0.18, points=[[2019, 2900], [2100, 2890], [2180, 2846], [2240, 2800]]),
             dict(id='rd_sajik', name='사직로', width=5, grade=0.10, points=[[1694, 3174], [1694, 3216]])]
    gates = [dict(id='gate_sungnye', name='숭례문', x=2000, z=2555, yaw=180, state='kept (CapitalSouthGate253, boss gate)'), dict(id='gate_heungin', name='흥인지문', x=2232, z=3494, yaw=30, state='kept capital_exit_2 + 옹성'),
             dict(id='gate_donui', name='돈의문', x=1444, z=3170, yaw=270, state='new'), dict(id='gate_sukjeong', name='숙정문', x=1985, z=3887, yaw=0, state='new, always shut'),
             dict(id='gate_hyehwa', name='혜화문', x=2292, z=3752, yaw=90, state='new'), dict(id='gate_gwanghui', name='광희문', x=2299, z=2757, yaw=105, state='new on a kept edge, shut'),
             dict(id='gate_soui', name='소의문', x=1650, z=2650, yaw=270, state='new on a kept edge'), dict(id='gate_changui', name='창의문', x=1651, z=3517, yaw=285, state='kept capital_exit_3'),
             dict(id='gate_postern', name='서북 암문', x=1636, z=3452, yaw=285, state='kept capital_exit_5')]
    doc = dict(version=VERSION, scale=dict(palace_real_m=[REAL['w'], REAL['d']], palace_game_m=[PAL['x1'] - PAL['x0'], PAL['z1'] - PAL['z0']], factor=round(S, 4), axis_x=AX, gwanghwamun_z=PAL['z0'], first_court_level=P0),
               palace_wall=PAL, palace_levels=[dict(real_y_from=(None if y < -1e8 else y), level=l) for y, l in band_levels()], wall=[dict(id=i, x=x, z=z, edge_to_next=e) for i, x, z, e in WALL],
               gates=gates, sites=P, zones=zones, roads=roads)
    B.write_json(SITES, doc); print(f'sites308: {len(P)} sites, {len(zones)} zones, {len(roads)} roads, scale {S:.4f} -> {B.rel(SITES)}'); return doc


# ------------------------------------------------------------------ helpers
def rect_mask(X, Z, x, z, w, d, grow=0.0): return (np.abs(X - x) <= w / 2 + grow) & (np.abs(Z - z) <= d / 2 + grow)
def sstep(t): t = np.clip(t, 0, 1); return t * t * (3 - 2 * t)


def profile(P, h, pinned_fn, grade, sigma_m=10.0, step=2.0):
    """longitudinal profile of a street: the ground smoothed along the line, held where pinned, grade limited"""
    Q = B.resample(np.asarray(P, float), step)[0]; y = np.array([B.sample(h, x, z) for x, z in Q]); pin = np.array([pinned_fn(x, z) for x, z in Q]); pin[0] = pin[-1] = True
    y0 = y.copy(); sg = sigma_m / step
    for _ in range(40):
        y = ndi.gaussian_filter1d(y, sg, mode='nearest'); y[pin] = y0[pin]
    lim = grade * step
    for _ in range(400):   # slope limiting that respects the pins (pins win: the excess is reported)
        d = np.diff(y); bad = np.abs(d) > lim
        if not bad.any(): break
        for i in np.flatnonzero(bad):
            e = (abs(d[i]) - lim) / 2 * np.sign(d[i]); a, b = i, i + 1
            if not pin[a] and not pin[b]: y[a] += e; y[b] -= e
            elif not pin[a]: y[a] += 2 * e
            elif not pin[b]: y[b] -= 2 * e
    free = ~(pin[:-1] & pin[1:]); dd = np.abs(np.diff(y)) / step
    return Q, y, (float(dd[free].max()) if free.any() else 0.0, float(dd.max()), float(pin.mean()))


def stamp_line(Q, y, half, X, Z, pad=24.0):
    """cells within `half` of the polyline -> (mask, level of the nearest station)"""
    m = np.zeros(X.shape, bool); L = np.zeros(X.shape); best = np.full(X.shape, 1e9)
    x0, x1, z0, z1 = Q[:, 0].min() - pad, Q[:, 0].max() + pad, Q[:, 1].min() - pad, Q[:, 1].max() + pad
    js = slice(max(int(x0 // CELL), 0), int(x1 // CELL) + 2); is_ = slice(max(int(z0 // CELL), 0), int(z1 // CELL) + 2); Xs, Zs = X[is_, js], Z[is_, js]; bs = best[is_, js]; Ls = L[is_, js]
    for k in range(len(Q) - 1):
        ax, az = Q[k]; bx, bz = Q[k + 1]; dx, dz = bx - ax, bz - az; ln2 = dx * dx + dz * dz
        if ln2 < 1e-9: continue
        t = np.clip(((Xs - ax) * dx + (Zs - az) * dz) / ln2, 0, 1); d = np.hypot(Xs - (ax + t * dx), Zs - (az + t * dz)); better = d < bs
        bs[better] = d[better]; Ls[better] = (y[k] + t * (y[k + 1] - y[k]))[better]
    m[is_, js] = bs <= half; return m, L, best


def terrace(h, step, sigma=1.5, riser=0.35):
    """contour terraces: the smoothed ground quantised to `step` with a soft riser (the house rows of a slope town, D308-45)"""
    s = ndi.gaussian_filter(h, sigma); q = s / step; f = np.floor(q); r = q - f
    return (f + sstep((r - (0.5 - riser / 2)) / riser)) * step


def city_poly(): return [[x, z] for _, x, z, _ in WALL]


def build(dry=False):
    doc = json.loads(SITES.read_text(encoding='utf-8')) if SITES.exists() else make_sites()
    if B.sha256(PREV) != PREV_SHA: raise SystemExit(f'REFUSED: {B.rel(PREV)} is not the n7 field {PREV_SHA}')
    prev = B.load_height(PREV); base = B.load_height(); X, Z = B.grid_xz(); wet = B.wet(); prot = B.protected(); lat = json.loads(LAT.read_text(encoding='utf-8')); routes = {r['id']: r for r in lat['routes']}
    inside = B.poly_mask(city_poly(), X, Z)
    # ---- where nothing may change
    hard = ndi.binary_dilation(wet, iterations=2) | prot                      # water + 8 m, protected cells
    trail = np.zeros(X.shape, bool)
    for r in lat['routes']:
        if r['id'].startswith('mountain_hwanggyeong'): trail |= B.stamp(np.asarray(r['pts'], float), r['width'] / 2 + 3.0, X, Z)
    kept = np.zeros(X.shape, bool); kp = ST / 'Out/keep_boxes.json'
    for b in json.loads(kp.read_text(encoding='utf-8'))['boxes']:
        if 'stone_bridge' in b[4] or 'main_bridge' in b[4]: continue              # diagonal bridges: their line, not their box
        kept |= (X >= b[0] - 4) & (X <= b[1] + 4) & (Z >= b[2] - 4) & (Z <= b[3] + 4)
    for bid in ('bridge.capital_shared_stone_bridge', 'bridge.mountain_hwanggyeong_main_bridge'):
        Pb = np.asarray(routes[bid]['pts'], float); u = (Pb[-1] - Pb[0]) / max(np.hypot(*(Pb[-1] - Pb[0])), 1e-6); kept |= B.stamp(np.stack([Pb[0] - u * 14, Pb[-1] + u * 14]), 12.0, X, Z)
    bench_lines = []; W = doc['wall']
    for i, v in enumerate(W):
        if v['edge_to_next'] == 'new': n = W[(i + 1) % len(W)]; bench_lines.append([[v['x'], v['z']], [n['x'], n['z']]])
    domain = inside & (X <= 2340)
    for ln in bench_lines: domain |= B.stamp(np.asarray(ln, float), 26.0, X, Z)
    K = hard | trail | kept | ~domain
    dK = ndi.distance_transform_edt(~K) * CELL
    # ---- 1. terraced housing ground
    Hz = prev.copy(); zone_cells = {}
    road_lines = {}
    for r in doc['roads']:
        if 'route' in r:
            Pp = np.asarray(routes[r['route']]['pts'], float); c = r['clip']; ok = (Pp[:, 0] >= c[0]) & (Pp[:, 0] <= c[2]) & (Pp[:, 1] >= c[1]) & (Pp[:, 1] <= c[3]); Pp = Pp[ok]
        else: Pp = np.asarray(r['points'], float)
        road_lines[r['id']] = Pp
    for zn in doc['zones']:
        if 'road' in zn: m = B.stamp(road_lines[zn['road']], zn['half'], X, Z)
        else: m = (X >= zn['x0']) & (X <= zn['x1']) & (Z >= zn['z0']) & (Z <= zn['z1'])
        m &= domain; wgt = sstep(ndi.distance_transform_edt(m) * CELL / 12.0); t = terrace(prev, zn['step']); Hz = Hz + (t - Hz) * wgt * (ndi.gaussian_filter(m.astype(float), 1.0) > 0.01); zone_cells[zn['id']] = int(m.sum())
    # ---- 2. platforms (hard levels)
    hardm = np.zeros(X.shape, bool); Lh = np.zeros(X.shape); owner = np.full(X.shape, -1, int); levels = {}
    pm = (X >= PAL['x0'] - 3) & (X <= PAL['x1'] + 3) & (Z >= PAL['z0'] - 3) & (Z <= PAL['z1'] + 3)
    yr = (Z - PAL['z0']) / S; Lp = np.full(X.shape, P0)
    xr = (X - AX) / S
    for y0, l in band_levels(): Lp = np.where((yr >= y0) & ((y0 < 498) | (np.abs(xr) <= 46)), l, Lp)
    nat = ndi.gaussian_filter(prev, 2.0); garden = terrace(prev, 2.0)
    hill = sstep((nat - Lp - 5.0) / 6.0)                                # 0 = court level, 1 = the hillside keeps its (terraced) ground
    hill[yr < 128] = 0.0                                                # the outer court is level from wall to wall
    forced = np.zeros(X.shape, bool)
    for s_ in doc['sites']:
        if s_['group'] == 'palace' and s_['kind'] in ('court', 'hall', 'gate', 'garden', 'terrace', 'tower', 'pond') and s_['real']['y'] < 560: forced |= rect_mask(X, Z, s_['x'], s_['z'], s_['w'], s_['d'], 6.0)
    hill[forced] = 0.0; hill = np.clip(ndi.gaussian_filter(hill, 1.2), 0, 1); hill[forced] = 0.0
    north = sstep((yr - 520) / 30.0)                                    # the rear garden follows the ground, never below the last court
    pal_t = Lp * (1 - hill) + garden * hill; rear = np.maximum(garden, P0 + 6.0 - 0.0 * yr); pal_t = pal_t * (1 - north) + np.where(hill > 0, np.maximum(pal_t, garden), np.maximum(rear, Lp)) * north
    # the hillside is not held level, so it is not a hard platform: only the level part is
    level_part = pm & (hill < 0.02) & (north < 0.02)
    hardm |= level_part; Lh[level_part] = Lp[level_part]; owner[level_part] = -2
    soft_pal = pm & ~level_part
    order = {s['id']: k for k, s in enumerate(doc['sites'])}
    for s in doc['sites']:
        m = rect_mask(X, Z, s['x'], s['z'], s['w'], s['d'], 2.0) & domain
        if s['group'] == 'palace':
            if s['kind'] == 'pond':
                core = rect_mask(X, Z, s['x'], s['z'], s['w'], s['d'], 0.0); bank = float(np.median(Lp[core])) if s['real']['y'] < 520 else float(np.percentile(prev[rect_mask(X, Z, s['x'], s['z'], s['w'], s['d'], 10.0)], 35)); hardm |= core; Lh[core] = bank - 2.4; owner[core] = order[s['id']]; levels[s['id']] = [round(bank - 2.4, 2)]; s['bank_level'] = round(bank, 2)
                ring = rect_mask(X, Z, s['x'], s['z'], s['w'], s['d'], 10.0) & ~core; hardm |= ring; Lh[ring] = bank; owner[ring] = -2
            elif s['kind'] == 'channel':
                core = rect_mask(X, Z, s['x'], s['z'], s['w'], s['d'], 0.0); hardm |= core; Lh[core] = Lp[core] - 1.2; owner[core] = order[s['id']]; levels[s['id']] = [round(float(np.median(Lp[core])) - 1.2, 2)]
            elif s['real']['y'] >= 560 and s['kind'] == 'court':
                l = float(np.median(prev[m])); hardm |= m; Lh[m] = l; owner[m] = order[s['id']]; levels[s['id']] = [round(l, 2)]
            continue
        if s['kind'] == 'terraces':
            n = s['bands']; lv = []
            for k in range(n):
                zb0 = s['z'] - s['d'] / 2 + s['d'] * k / n; zb1 = zb0 + s['d'] / n; mb = m & (Z >= zb0 - (2 if k == 0 else 0)) & (Z < zb1 + (2.01 if k == n - 1 else 0))
                if not mb.any(): continue
                l = float(np.median(prev[mb])); hardm |= mb; Lh[mb] = l; owner[mb] = order[s['id']]; lv.append(round(l, 2))
            levels[s['id']] = lv
        else:
            l = s.get('level'); l = float(np.median(prev[m])) if l is None else l; hardm |= m; Lh[m] = l; owner[m] = order[s['id']]; levels[s['id']] = [round(l, 2)]
    plat = hardm.copy(); Lplat = Lh.copy()
    # ---- 3. streets: profile over the terraced ground, pinned on platforms and on untouchable ground
    Hpre = np.where(plat, Lplat, Hz); road_rep = {}; road_prof = {}
    def pinned(x, z):
        i, j = int(round(z / CELL)), int(round(x / CELL)); return bool(plat[i, j] or dK[i, j] < 6.0)
    for r in doc['roads']:
        Pp = road_lines[r['id']]
        if len(Pp) < 2: continue
        Q, y, g = profile(Pp, Hpre, pinned, r['grade']); m, L, _ = stamp_line(Q, y, r['width'] / 2 + 1.0, X, Z); m &= ~plat & domain
        hardm |= m; Lh[m] = L[m]; owner[m] = 1000 + len(road_rep); road_prof[r['id']] = (Q, y)
        road_rep[r['id']] = dict(length_m=round(B.polyline_len(Q), 1), grade_max=round(g[0], 3), grade_max_incl_untouched=round(g[1], 3), untouched_share=round(g[2], 2), level_min=round(float(y.min()), 1), level_max=round(float(y.max()), 1), cells=int(m.sum()))
    bench_rep = []
    for k, ln in enumerate(bench_lines):
        Q, y, g = profile(ln, Hpre, pinned, 0.45, sigma_m=8.0); m, L, _ = stamp_line(Q, y, 5.0, X, Z); m &= ~hardm & domain; hardm |= m; Lh[m] = L[m]; owner[m] = 2000 + k
        bench_rep.append(dict(a=ln[0], b=ln[1], length_m=round(B.polyline_len(Q), 1), grade_max=round(g[0], 3), grade_max_incl_untouched=round(g[1], 3), level_min=round(float(y.min()), 1), level_max=round(float(y.max()), 1)))
    # ---- 4. compose: hard cells exact, a skirt of 3 x height difference (>= 8 m) around them, nothing on untouchable ground
    soft = Hz.copy(); soft[soft_pal] = pal_t[soft_pal]
    d, (ii, jj) = ndi.distance_transform_edt(~hardm, return_indices=True); d *= CELL; Ln = Lh[ii, jj]; diff = Ln - soft; wd = np.clip(3.0 * np.abs(diff), 8.0, 60.0); f = 1 - sstep(d / wd)
    new = np.where(hardm, Lh, soft + diff * f)
    fade = sstep(dK / 10.0); new = prev + (new - prev) * fade; new[K] = prev[K]
    new = np.where(np.abs(new - prev) < 0.02, prev, new)
    # one light pass over the changed soft ground only (keeps platforms exact): takes the 4 m staircase out of skirts
    ch = np.abs(new - prev) > 0.02; sm = ndi.gaussian_filter(new, 0.8); mixm = ch & ~hardm & (dK > 12.0); new[mixm] = sm[mixm]
    rep, extra = report(doc, prev, base, new, K, hard, hardm, Lh, plat, levels, road_rep, bench_rep, zone_cells, inside, X, Z)
    if dry:
        np.save(ST / 'Out/n8_dry.npy', new.astype('<f4')); B.write_json(ST / 'Out/n8_dry.report.json', rep); print(json.dumps({k: v for k, v in rep.items() if k not in ('tiles', 'tiles_changed_vs_prev', 'sites', 'roads')}, ensure_ascii=False, indent=1)); return rep
    (ST / 'Out/veg_mask.bytes').write_bytes(np.ascontiguousarray((hardm & ~K & (np.abs(new - prev) >= 0) ).astype(np.uint8)).tobytes())
    B.guard_out(OUT); sha = B.save_height(OUT, new); B.write_text(str(OUT) + '.sha256', f'{sha}  {OUT.name}\n')
    new32 = B.load_height(OUT); chb = np.abs(new32 - base) > 0.05; chp = np.abs(new32 - prev) > 0.05
    rep.update(stage='n8', cfg_version=VERSION, base='Oheangbu/Assets/_Project/Art/World/Finish297/Surface/height.bytes', base_sha256=B.sha256(B.BASE_HEIGHT), prev=B.rel(PREV), prev_sha256=PREV_SHA, prev_stage='n7',
               tiles=B.tile_names(B.tiles_of(chb, 8)), tiles_changed_vs_prev=B.tile_names(B.tiles_of(chp, 8)), cfg=B.rel(SITES), cfg_sha256=B.sha256(SITES), out=B.rel(OUT), out_sha256=sha, ops=B.rel(SITES), ops_sha256=B.sha256(SITES),
               ops_version=VERSION, ops_note='stage n8 = hanyang308_terrain(stage n7): the ledger ops sha is sites308.json. Cuts and fills inside the capital wall line only.')
    B.write_json(str(OUT.with_suffix('')) + '.report.json', rep); handover(doc, new32, levels, road_prof, bench_lines, rep)
    print(f"hanyang308 {VERSION} stage n8: {rep['changed_cells_vs_prev']} cells vs n7 -> {B.rel(OUT)} sha256 {sha}"); return rep


def report(doc, prev, base, new, K, hard, hardm, Lh, plat, levels, road_rep, bench_rep, zone_cells, inside, X, Z):
    from cliffs308 import seal_report, footprint
    from rugged308 import walk_reach
    ch = np.abs(new - prev) > 0.05; dlt = new - prev
    scarp = B.load_height(B.PL / 'Stage/height_pt1.bytes'); band = footprint(dict(footprint=dict(min_fill_m=0.05, min_cells=200)), base, scarp) | ((prev - base) > 0.5)
    foot = ndi.minimum_filter(prev, size=9)
    seal, sx = seal_report(prev, new, band, foot)
    rp = walk_reach(prev, B.SEED_NODE); rn = walk_reach(new, B.SEED_NODE); gained = rn & ~rp; lost = rp & ~rn; play = sx['play']
    slope = B.slope_deg(new); sp = B.slope_deg(prev)
    sites = {}
    for s in doc['sites']:
        m = rect_mask(X, Z, s['x'], s['z'], s['w'], s['d'], 0.0)
        if not m.any(): continue
        sites[s['id']] = dict(level=levels.get(s['id']), ground_min=round(float(new[m].min()), 2), ground_max=round(float(new[m].max()), 2), relief_before=round(float(prev[m].max() - prev[m].min()), 1), relief_after=round(float(new[m].max() - new[m].min()), 2),
                              cut_max=round(float(-dlt[m].min()), 1), fill_max=round(float(dlt[m].max()), 1), kept_cells=int((m & K).sum()), cells=int(m.sum()))
    rep = dict(changed_cells_vs_prev=int(ch.sum()), changed_ha=round(float(ch.sum()) * 16 / 1e4, 1), raised_max_m=round(float(dlt.max()), 1), lowered_max_m=round(float(-dlt.min()), 1), cut_m3=round(float(-dlt[dlt < 0].sum() * 16)), fill_m3=round(float(dlt[dlt > 0].sum() * 16)),
               changed_outside_wall_cells=int((ch & ~inside).sum()), hard_cells_changed=int((ch & hard).sum()), keepout_cells_changed=int((ch & K).sum()), play_cells_changed=int((ch & play).sum()),
               slope_over_45_changed_cells=dict(prev=int(((sp > 45) & ch).sum()), new=int(((slope > 45) & ch).sum())), slope_over_35_changed_cells=dict(prev=int(((sp > 35) & ch).sum()), new=int(((slope > 35) & ch).sum())),
               steep_new_clusters=B.clusters((slope > 45) & ch & (sp <= 45), 60, 14), platform_cells=int(plat.sum()), zones=zone_cells, roads=road_rep, wall_bench=bench_rep, sites=sites,
               walk=dict(model='tile triangle slope < 45 deg from the new-game node', reach_prev=int(rp.sum()), reach_new=int(rn.sum()), gained_cells=int(gained.sum()), gained_outside_wall=int((gained & ~inside).sum()), gained_clusters=B.clusters(gained, 100, 12),
                         lost_cells=int(lost.sum()), lost_outside_wall=int((lost & ~inside).sum()), lost_clusters=B.clusters(lost, 100, 12)),
               seal=seal)
    return rep, dict(gained=gained, lost=lost)


def handover(doc, h, levels, road_prof, bench_lines, rep):
    """the ground-truthed site data for the later phases"""
    def g(x, z): return round(float(B.sample(h, x, z)), 2)
    out = dict(id='hanyang308', version='308.hanyang.p1a', status='PHASE 1a DONE (ground prepared on stage n8; no building yet)', stage='n8', stage_sha256=rep['out_sha256'],
               doc=['Units metres, world XZ of W_Demo_Main (x east, z north), yaw degrees clockwise from +z.', 'Heights are sampled from height_pn8 (the field the terrain tiles are meshed from).',
                    'Supersedes 308.hanyang.plan1 (kept as hanyang308.plan1.json): the palace is rebuilt as Gyeongbokgung on a new axis, people inside the walls are wanted (D308-77).'],
               scale=doc['scale'], palace_wall=dict(doc['palace_wall'], corners=[[x, z, g(x, z)] for x, z in ((PAL['x0'], PAL['z0']), (PAL['x1'], PAL['z0']), (PAL['x1'], PAL['z1']), (PAL['x0'], PAL['z1']))]), palace_levels=doc['palace_levels'])
    ring = [(PAL['x0'], PAL['z0']), (PAL['x1'], PAL['z0']), (PAL['x1'], PAL['z1']), (PAL['x0'], PAL['z1']), (PAL['x0'], PAL['z0'])]
    Q = B.resample(np.asarray(ring, float), 10.0)[0]; out['palace_wall']['line'] = [[round(float(x), 1), round(float(z), 1), g(x, z)] for x, z in Q]
    W = doc['wall']; segs = []
    for i, v in enumerate(W):
        n = W[(i + 1) % len(W)]; Q = B.resample(np.asarray([[v['x'], v['z']], [n['x'], n['z']]], float), 10.0)[0]; ys = [g(x, z) for x, z in Q]; L = float(math.hypot(n['x'] - v['x'], n['z'] - v['z']))
        segs.append(dict(a=v['id'], b=n['id'], status=v['edge_to_next'], length=round(L, 1), yaw=round(math.degrees(math.atan2(n['x'] - v['x'], n['z'] - v['z'])) % 360, 1), h_min=min(ys), h_max=max(ys),
                         grade_max=round(max([abs(ys[k + 1] - ys[k]) / max(L / max(len(ys) - 1, 1), 1e-6) for k in range(len(ys) - 1)] or [0]), 3), line=[[round(float(x), 1), round(float(z), 1), y] for (x, z), y in zip(Q, ys)]))
    out['wall'] = dict(spec=dict(outer_min_m=6.5, walk_w=3.0, parapet_h=1.3, block_rule_m=4.0, chunk_m=48, bench_half_m=5.0), vertices=W, segments=segs, circuit_m=round(sum(s['length'] for s in segs), 1), new_wall_m=round(sum(s['length'] for s in segs if s['status'] == 'new'), 1),
                       joins='new wall starts at the jamb of capital_exit_2 (Q), ends on kept chunk CapitalWall_1_13 (J); west bulge starts on kept chunk CapitalWall_3_01 (G) and ends on kept chunk CapitalWall_3_12 (W5)')
    out['gates'] = [dict(gt, ground=g(gt['x'], gt['z'])) for gt in doc['gates']]
    sites = []
    for s in doc['sites']:
        x0, x1, z0, z1 = s['x'] - s['w'] / 2, s['x'] + s['w'] / 2, s['z'] - s['d'] / 2, s['z'] + s['d'] / 2
        e = dict(s, yaw=0, level=levels.get(s['id']), ground_centre=g(s['x'], s['z']), corners=[[round(x, 1), round(z, 1), g(x, z)] for x, z in ((x0, z0), (x1, z0), (x1, z1), (x0, z1))], measured=rep['sites'].get(s['id']))
        sites.append(e)
    out['sites'] = sites; out['zones'] = [dict(z, cells=rep['zones'].get(z['id'])) for z in doc['zones']]
    out['roads'] = [dict(r, measured=rep['roads'].get(r['id']), line=[[round(float(x), 1), round(float(z), 1), round(float(y), 2)] for (x, z), y in zip(*road_prof[r['id']])][::5] if r['id'] in road_prof else []) for r in doc['roads']]
    old = ST / 'hanyang308.plan1.json'
    if not old.exists(): old.write_bytes((ST / 'hanyang308.json').read_bytes())
    p1 = json.loads(old.read_text(encoding='utf-8')); out['walkers_plan1'] = p1.get('walkers'); out['water_gates'] = p1.get('water_gates')
    extra = ST / 'handover_extra.json'
    if extra.exists(): out.update(json.loads(extra.read_text(encoding='utf-8')))
    B.write_json(ST / 'hanyang308.json', out)


if __name__ == '__main__':
    B.utf8()
    if len(sys.argv) < 2 or sys.argv[1] not in ('sites', 'build'): print(__doc__); sys.exit(2)
    if sys.argv[1] == 'sites': make_sites()
    else: build('--dry' in sys.argv)
