# part 2 of hanyang308_town.py (exec'd inside it): C4 시전 · C5 주거 · C6 관아/성균관.  Not run on its own.
GEN.extend(['Gen_Pedestal_Haetae', 'Gen_Wall_Makdol4', 'Gen_Wall_Todam4'])
TRI_BUDGET_HOUSING = 470000


def path(line, st=1.0):
    """polyline -> [(s, x, z, tx, tz)] every st metres"""
    out = []; s = 0.0
    for (x0, z0), (x1, z1) in zip(line[:-1], line[1:]):
        L = math.hypot(x1 - x0, z1 - z0)
        if L < 1e-6: continue
        n = max(1, int(L / st))
        for k in range(n): out.append((s + L * k / n, x0 + (x1 - x0) * k / n, z0 + (z1 - z0) * k / n, (x1 - x0) / L, (z1 - z0) / L))
        s += L
    return out


def at(P, s):
    return min(P, key=lambda q: abs(q[0] - s))


def smooth_t(P, s, win=8.0):
    a = at(P, s - win); b = at(P, s + win); L = math.hypot(b[1] - a[1], b[2] - a[2]) or 1.0; return (b[1] - a[1]) / L, (b[2] - a[2]) / L


def plinth(bl, cx, cz, w, d, yaw, y_top, y_bot):
    """기단: stretched stone pedestal modules (1.9 x 1.4 x 3.1) side by side under a rotated footprint"""
    if y_top - y_bot < 0.12: return
    n = max(1, int(round(w / 7.0)))
    for k in range(n):
        lx = -w / 2 + (k + 0.5) * w / n; x, z = to_world(cx, cz, yaw, lx, 0); bl.add('Gen_Pedestal_Haetae', x, y_bot, z, yaw, (w / n / 1.9 * 1.1, (y_top - y_bot) / 1.4, d / 3.1), l=0)


def wall_run(bl, cx, cz, yaw, lx0, lx1, lz, kind='Gen_Wall_Makdol4', gap=None, hs=1.0):
    """담: 4 m modules along local X at local z = lz, each seated on its own ground; gap = (lx_a, lx_b) left open"""
    L = lx1 - lx0; n = max(1, int(round(L / 4.0))); m = L / n
    for k in range(n):
        a, b_ = lx0 + k * m, lx0 + (k + 1) * m
        if gap and not (b_ <= gap[0] or a >= gap[1]): continue
        x, z = to_world(cx, cz, yaw, (a + b_) / 2, lz); xa, za = to_world(cx, cz, yaw, a, lz); xb, zb = to_world(cx, cz, yaw, b_, lz)
        ys = [g(x, z), g(xa, za), g(xb, zb)]
        if max(ys) - min(ys) > 1.3: continue
        bl.add(kind, x, min(ys) - 0.15, z, yaw, (m / 4.0, hs + (max(ys) - min(ys)) / 1.8, 1.0), l=0)


def colbox(G_, cx, cz, yaw, w, d, y, h, n='Body'):
    G_.items.append(item('col', cx, y + h / 2, cz, yaw, (max(1.5, w), h, max(1.5, d)), n=n))


def lot(cx, cz, w, d, yaw, max_rel=1.6, pad=0.35):
    """a free, level enough footprint? -> (y_top, y_bot) or None"""
    if not free(cx, cz, w, d, yaw, pad): counts['f_occ'] = counts.get('f_occ', 0) + 1; return None
    lo, hi = ground(cx, cz, w, d, yaw)
    if hi - lo > max_rel: counts['f_rel'] = counts.get('f_rel', 0) + 1; return None
    return math.ceil(hi / 0.05) * 0.05 + 0.02, lo - 0.45


def seat(cx, cz, w, d, yaw, along_max):
    """a shop on a street: floor = street-side ground. Returns (floor, plinth bottom) or None (not free / street too steep along the front / back too deep in the hill / too high a plinth)"""
    if not free(cx, cz, w, d, yaw, 0.15): return None
    fr = [g(*to_world(cx, cz, yaw, lx, -d / 2 + 0.6)) for lx in np.linspace(-w / 2 + 0.5, w / 2 - 0.5, 7)]; lo, hi = ground(cx, cz, w - 1.0, d - 1.5, yaw)
    if max(fr) - min(fr) > along_max or hi - max(fr) > 1.3 or max(fr) - lo > 2.8: return None
    return math.ceil(max(fr) / 0.05) * 0.05 + 0.05, lo - 0.45


# =====================================================================================================
# C4  운종가 시전행랑 · 피맛골 · 남대문로 시전 · 좌판
# =====================================================================================================
ROW = 'Gen_Sijeon_Row8'; RW, RD = 21.2, 9.4     # body of the row (bounds 22.0 x 10.7 with eaves)


def shop_rows(u, tag, line, side, s0, s1, lane=False, pitch=22.6):
    P = path(line); n_rows = 0; k = 0; s = s0; Gcur = None; bl = None; pr = None; cnt = 0
    while s + RW / 2 < min(s1, P[-1][0] - 2):
        sc = s + RW / 2; _, x, z, _, _ = at(P, sc); tx, tz = smooth_t(P, sc, 10.0); nx, nz = -tz * side, tx * side     # side +1 = left of the direction of travel
        off = ROAD_W / 2 + 2.3 + SIZE[ROW][2] / 2; cx, cz = x + nx * off, z + nz * off; yaw = face_yaw(-nx, -nz); kind = ROW; ok = seat(cx, cz, SIZE[ROW][0], SIZE[ROW][2], yaw, 1.0)
        if ok is None:
            kind = 'Gen_Giwa_Shop3'; sc = s + SIZE[kind][0] / 2; _, x, z, _, _ = at(P, sc); tx, tz = smooth_t(P, sc, 6.0); nx, nz = -tz * side, tx * side; off = ROAD_W / 2 + 2.3 + SIZE[kind][2] / 2; cx, cz = x + nx * off, z + nz * off; yaw = face_yaw(-nx, -nz)
            ok = seat(cx, cz, SIZE[kind][0], SIZE[kind][2], yaw, 0.8)
        if ok is None: s += 2.5; continue
        half = kind != ROW; KW, KD = SIZE[kind][0], SIZE[kind][2]
        if Gcur is None or cnt % 4 == 0:
            if bl: bl.place(Gcur); pr.place(Gcur) if pr.h else blocks.remove(pr)
            Gcur = G(u, '%s_%02d' % (tag, cnt // 4), cx, cz, cull=0.006); bl = Blk('%s_%02d' % (tag, cnt // 4), top=0, cut=0.3, cull=0.012); pr = Blk('%s_%02dp' % (tag, cnt // 4), top=1, cut=0.55, cull=0.06)
        yt, yb = ok; w_ = KW
        bl.add(kind, cx, yt, cz, yaw, (1.0, 1.0 + 0.05 * ((cnt * 7) % 3 - 1), 1.0), l=0); plinth(bl, cx, cz, w_ - 0.9, KD - 1.9, yaw, yt + 0.02, yb)
        colbox(Gcur, cx, cz, yaw, w_ - 1.2, KD - 2.0, yb, yt - yb + 3.6); mark(cx, cz, w_, KD, yaw, 0.15); cnt += 1; n_rows += 1; counts['shops'] = counts.get('shops', 0) + (3 if half else 8)
        fx, fz = to_world(cx, cz, yaw, 0, -(KD / 2 + 1.4)); anchors['shop_fronts'].append(dict(id='%s_%02d' % (tag, cnt), at=[round(fx, 1), round(g(fx, fz), 2), round(fz, 1)], yaw=round(yaw, 1), bays=3 if half else 8))
        # 좌판 · 독 · 지게 · 수레 in front of the eaves (clear of the street centre)
        for j, lx in enumerate((-7.5, -2.6, 2.4, 7.4) if not half else (-2.0, 2.2)):
            if R.random() < 0.38: continue
            lz_ = -(KD / 2 + 0.2 + R.random() * 0.5); px, pz = to_world(cx, cz, yaw, lx, lz_); py = g(px, pz); kind = R.choice(['Prop_Bench', 'Prop_Bench', 'Prop_Jars', 'Prop_Jige', 'Prop_WaterJar', 'Prop_Cart'])
            if kind == 'Prop_Cart' and R.random() < 0.6: kind = 'Prop_Jars'
            yawp = yaw + R.uniform(-12, 12) + (90 if kind == 'Prop_Cart' else 0)
            if kind == 'Prop_WaterJar':
                for q in range(3): qx, qz = to_world(cx, cz, yaw, lx + q * 0.75 - 0.7, lz_); pr.add(kind, qx, g(qx, qz), qz, yawp + q * 40, (2.2, 2.6, 2.2))
            else: pr.add(kind, px, py + (0.64 if kind == 'Prop_Cart' else 0.0), pz, yawp)
            if kind == 'Prop_Bench':
                pr.add('Prop_WaterJar', px + 0.3, py + 0.47, pz - 0.2, yawp, (1.8, 2.0, 1.8)); anchors['stalls'].append(dict(at=[round(px, 1), round(py, 2), round(pz, 1)], kind='jwapan'))
            counts['props'] = counts.get('props', 0) + 1
        # 옹기 도관 굴뚝 at a back corner + an unlit 마석 등 post every other row (D308-64: no sustained emission)
        qx, qz = to_world(cx, cz, yaw, w_ / 2 - 0.9, KD / 2 - 1.3); Gcur.cyl('h308_onggi', qx, yb, qz, 0.2, yt + 4.4, 8); Gcur.cyl('h308_onggi', qx, yt + 4.4, qz, 0.28, yt + 4.7, 8)
        if cnt % 2 == 0:
            qx, qz = to_world(cx, cz, yaw, -w_ / 2 - 0.3, -(KD / 2 + 0.3)); qy = g(qx, qz); Gcur.box('wood_dark', qx - 0.07, qy - 0.3, qz - 0.07, qx + 0.07, qy + 2.6, qz + 0.07); Gcur.box('h308_brass', qx - 0.16, qy + 2.6, qz - 0.16, qx + 0.16, qy + 2.66, qz + 0.16, True); Gcur.box('h308_maseok', qx - 0.11, qy + 2.66, qz - 0.11, qx + 0.11, qy + 2.92, qz + 0.11, True); Gcur.pyr('h308_brass', qx - 0.2, qy + 2.92, qz - 0.2, qx + 0.2, qy + 3.1, qz + 0.2)
        # 피맛골: a narrow back lane and small 주막 · 국밥집 · 대장간 fronts behind the row
        if lane:
            for lx in ((-5.6, 5.6) if not half else (0.0,)):
                kind = R.choice(['Choga_Tavern', 'Gen_Giwa_Shop3', 'Choga_Tavern', 'Choga_Smithy', 'Gen_Giwa_Shop3']); sw, sd = SIZE[kind][0], SIZE[kind][2]; turn = 90 if kind == 'Choga_Smithy' else 0
                if turn: sw, sd = sd, sw
                hx_, hz_ = to_world(cx, cz, yaw, lx + R.uniform(-0.6, 0.6), KD / 2 + 3.2 + sd / 2); o2 = lot(hx_, hz_, sw, sd, yaw, 1.6, 0.3)
                if o2 is None: continue
                bl.add(kind, hx_, o2[0], hz_, yaw + turn, l=0 if kind.startswith('Gen_') else 1); plinth(bl, hx_, hz_, sw - 1.6, sd - 1.6, yaw, o2[0] + 0.02, o2[1]); colbox(Gcur, hx_, hz_, yaw, sw - 2.0, sd - 2.0, o2[1], o2[0] - o2[1] + 3.2); mark(hx_, hz_, sw, sd, yaw, 0.3)
                counts['pimat'] = counts.get('pimat', 0) + 1
                if kind == 'Choga_Tavern':
                    ax_, az_ = to_world(hx_, hz_, yaw, 0, -(sd / 2 + 1.0)); anchors['taverns'].append(dict(at=[round(ax_, 1), round(g(ax_, az_), 2), round(az_, 1)], yaw=round(yaw, 1)))
                    if not counts.get('lantern'):
                        lx_, lz2 = to_world(hx_, hz_, yaw, sw / 2 - 0.8, -(sd / 2 + 0.2)); ly = g(lx_, lz2); Gcur.box('wood_dark', lx_ - 0.05, ly - 0.2, lz2 - 0.05, lx_ + 0.05, ly + 2.3, lz2 + 0.05); Gcur.box('InnLantern297', lx_ - 0.14, ly + 2.0, lz2 - 0.14, lx_ + 0.14, ly + 2.36, lz2 + 0.14, True); counts['lantern'] = 1; anchors['taverns'][-1]['lantern'] = True
        s += w_ + 0.5 + (3.6 if cnt % 4 == 0 else 0.0)
    if bl: bl.place(Gcur); pr.place(Gcur) if pr.h else blocks.remove(pr)
    counts['rows_' + tag] = n_rows; return n_rows


def c4():
    global ROAD_W
    u = unit('Sijeon'); jl = [p[:2] for p in ROAD['rd_jongno']['line']][::-1]; ROAD_W = 8.0
    shop_rows(u, 'JongnoN', jl, +1, 14.0, 520.0, lane=True); shop_rows(u, 'JongnoS', jl, -1, 20.0, 520.0)
    ROAD_W = 6.0; w = [p[:2] for p in ROAD['rd_namdaemun_w']['line']]; e = [p[:2] for p in ROAD['rd_namdaemun_e']['line']]
    shop_rows(u, 'NamdaeW', w, -1, 34.0, 345.0); shop_rows(u, 'NamdaeE', e[::-1], +1, 34.0, 345.0)
    # 난전: stalls on the strip between the two lines of 남대문로 (x 2003.6 .. 2012.4)
    Gm = None; pr = None; k = 0
    for z in np.arange(2650.0, 2905.0, 5.0):
        if k % 8 == 0:
            if pr: pr.place(Gm)
            Gm = G(u, 'Nanjeon_%d' % (k // 8), 2008.0, z + 26, cull=0.008); pr = Blk('Nanjeon_%d' % (k // 8), top=1, cut=0.55, cull=0.06)
        x = 2008.0 + R.uniform(-0.8, 0.8); y = g(x, z); kind = ['Prop_Bench', 'Prop_Cart', 'Prop_Bench', 'Prop_Jars'][k % 4]; k += 1
        if abs(g(2005.5, z) - g(2010.5, z)) > 0.7: continue
        pr.add(kind, x, y + (0.64 if kind == 'Prop_Cart' else 0), z, R.uniform(0, 360) if kind != 'Prop_Bench' else R.choice([0, 90]) + R.uniform(-8, 8))
        if kind == 'Prop_Bench':
            pr.add('Prop_WaterJar', x - 0.3, y + 0.47, z + 0.2, 0, (2.0, 2.2, 2.0)); pr.add('Prop_Jige', x + 1.6, g(x + 1.6, z + 0.8), z + 0.8, R.uniform(0, 360))
            Gm.box('h308_straw', x - 1.1, y - 0.2, z + 1.5, x + 1.1, g(x, z + 2.3) + 0.05, z + 3.1)     # 멍석
            for q in range(3): Gm.cyl('h308_straw', x - 0.7 + q * 0.7, g(x, z + 2.3) + 0.04, z + 2.3, 0.3, g(x, z + 2.3) + 0.62, 7)   # 가마니 · 섬
            Gm.box('wood_dark', x - 1.4, y - 0.3, z - 1.55, x - 1.32, y + 3.3, z - 1.47); Gm.box('h308_cloth', x - 1.33, y + 2.2, z - 1.53, x - 0.5, y + 3.2, z - 1.49, True)   # 깃대 + 천 (글자 없음)
        anchors['stalls'].append(dict(at=[round(x, 1), round(y, 2), round(float(z), 1)], kind='nanjeon:' + kind)); counts['props'] = counts.get('props', 0) + 1
    if pr: pr.place(Gm)
    anchors['street_clear'] = dict(jongno='8 m road + 2.3 m each side to the eaves (12.6 m between shop fronts); stalls stay within 1.6 m of the eaves -> >= 9 m clear', namdaemunro='two 6 m lines (x 2000 and 2016); stalls only on the median x 2006.8 - 2009.2 -> 2.8 m free on each side of the median, both lines fully clear')


# =====================================================================================================
# C5  주거지
# =====================================================================================================
CHOGA = [('Choga_Long', 3), ('Choga_L', 3), ('Choga_Tavern', 1.2), ('Choga_Bench', 1), ('Choga_Smithy', 0.4), ('Choga_LShaped', 0.5), ('Gen_Giwa_4kan', 0.7)]
MIXED = [('Gen_Giwa_4kan', 3), ('Gen_Giwa_L', 3), ('Choga_L', 2), ('Choga_Long', 2), ('Choga_Bench', 0.6)]
GIWA = [('Gen_Giwa_L', 3), ('Gen_Giwa_4kan', 2.2), ('Giwa_Sarangchae', 1), ('Giwa_Anchae', 0.8)]
CHOGA = [('Choga_Long', 4), ('Choga_L', 4), ('Choga_Tavern', 0.6), ('Choga_Bench', 0.5), ('Choga_Smithy', 0.3), ('Gen_Giwa_4kan', 0.8)]
MIXED = [('Gen_Giwa_4kan', 3), ('Gen_Giwa_L', 3), ('Choga_L', 2), ('Choga_Long', 2)]
ZONES = [dict(id='Jongno', rect=(2010, 2940, 2130, 3180), mix=MIXED, wall='Gen_Wall_Todam4', wallp=0.5, keep=0.9, gate=0.1, pz=15.5),
         dict(id='Namchon', rect=(2020, 2595, 2250, 2935), mix=CHOGA, wall='Gen_Wall_Makdol4', wallp=0.55, keep=0.9, gate=0.0, pz=15.0),
         dict(id='Saemunan', rect=(1545, 3184, 1792, 3330), mix=MIXED, wall='Gen_Wall_Todam4', wallp=0.7, keep=0.9, gate=0.3, pz=15.5),
         dict(id='SaemunanS', rect=(1470, 3136, 1930, 3164), mix=MIXED, wall='Gen_Wall_Todam4', wallp=0.7, keep=0.9, gate=0.3, pz=15.0),
         dict(id='Myeongdong', rect=(1800, 2822, 1992, 2962), mix=CHOGA, wall='Gen_Wall_Makdol4', wallp=0.5, keep=0.85, gate=0.0, pz=15.0),
         dict(id='Seosomun', rect=(1665, 2580, 1992, 2820), mix=CHOGA, wall='Gen_Wall_Makdol4', wallp=0.55, keep=0.8, gate=0.0, pz=15.0),
         dict(id='Seochon', rect=(1655, 3332, 1788, 3530), mix=GIWA, wall='Gen_Wall_Todam4', wallp=0.85, keep=0.9, gate=0.5, pz=17.0),
         dict(id='Jeongdong', rect=(1440, 2880, 1800, 3134), mix=MIXED, wall='Gen_Wall_Todam4', wallp=0.6, keep=0.75, gate=0.15, pz=16.0),
         dict(id='East', rect=(2130, 2940, 2300, 3180), mix=CHOGA, wall='Gen_Wall_Makdol4', wallp=0.5, keep=0.7, gate=0.0, pz=16.0),
         dict(id='Bukchon', rect=(2128, 3320, 2305, 3436), mix=GIWA, wall='Gen_Wall_Todam4', wallp=0.9, keep=1.0, gate=0.6, pz=16.0)]


def pick(mix):
    t = sum(w for _, w in mix); r = R.random() * t
    for n, w in mix:
        r -= w
        if r <= 0: return n
    return mix[-1][0]


def top_lod(n): return 0 if n.startswith('Gen_') else 1


def c5():
    spent = 0; zc = {}
    for Z_ in ZONES:
        counts['f_occ'] = counts['f_rel'] = 0
        u = unit('Town_' + Z_['id']); x0, z0, x1, z1 = Z_['rect']; cells = {}; nh = 0; lane = 4.2; pz = Z_['pz']; row = 0; z = z0 + 7.0; tried = 0; wells = 0
        while z < z1 - 6.0:
            face_s = row % 2 == 0; x = x0 + R.uniform(2.0, 9.0)
            while x < x1 - 6.0:
                kind = pick(Z_['mix']); tried += 1
                if R.random() > Z_['keep'] or spent > TRI_BUDGET_HOUSING: x += R.uniform(9.0, 16.0); continue
                sw, sd = SIZE[kind][0], SIZE[kind][2]; turn = R.random() < 0.16
                if sd > sw * 1.25: turn = not turn                      # long side along the lane by default
                base = 0 if face_s else 180; px_, pz_ = x + sw / 2, z
                gx_ = (g(px_ + 4, pz_) - g(px_ - 4, pz_)) / 8.0; gz_ = (g(px_, pz_ + 4) - g(px_, pz_ - 4)) / 8.0
                if math.hypot(gx_, gz_) > 0.05: base = round(face_yaw(-gx_, -gz_) / 15.0) * 15.0      # on a slope: long side along the contour, front downhill
                yaw = base + (90 if turn else 0) + R.uniform(-7, 7); w_, d_ = (sd, sw) if turn else (sw, sd); cx = x + sw / 2; cz = z + R.uniform(-1.6, 1.6)
                # the lot = house + a front yard strip; local -Z of yaw0 is the front
                fy = yaw - (90 if turn else 0)    # yaw of the lot frame (front toward the lane / downhill)
                yard = 3.4; lcx, lcz = to_world(cx, cz, fy, 0, -yard / 2); ok = lot(lcx, lcz, w_ + 1.6, d_ + yard, fy, 9.0, 0.35); ok2 = lot(cx, cz, w_, d_, fy, 1.6, 0.0) if ok else None
                if ok2 is None: x += R.uniform(4.0, 8.0); continue
                yt, yb = ok2; key = (int(cx // 64), int(cz // 64))
                if key not in cells: cells[key] = (G(u, '%s_%d_%d' % (Z_['id'], key[0], key[1]), key[0] * 64 + 32, key[1] * 64 + 32, cull=0.006), Blk('%s_%d_%d' % (Z_['id'], key[0], key[1]), top=1, cut=0.5, cull=0.02))
                Gb, bl = cells[key]; bl.add(kind, cx, yt, cz, yaw, l=top_lod(kind)); plinth(bl, cx, cz, w_ - 1.7, d_ - 1.7, fy, yt + 0.03, yb); colbox(Gb, cx, cz, fy, w_ - 2.0, d_ - 2.0, yb, yt - yb + 3.2)
                mark(lcx, lcz, w_ + 1.6, d_ + yard, fy, 0.35); nh += 1; spent += TRI[kind][top_lod(kind)]; zc[kind] = zc.get(kind, 0) + 1
                if R.random() < Z_['wallp']:
                    fz_ = -(d_ / 2 + yard - 0.5); gate = R.random() < Z_['gate'] and w_ + 1.2 > 15.0
                    if gate:
                        gx_, gz_ = to_world(cx, cz, fy, 0, fz_); gl, gh = ground(gx_, gz_, 14.4, 4.4, fy)
                        if gh - gl < 0.9: bl.add('Gen_Soseuldaemun', gx_, gh + 0.02, gz_, fy, l=0); plinth(bl, gx_, gz_, 14.0, 4.0, fy, gh + 0.04, gl - 0.4); colbox(Gb, gx_, gz_, fy, 14.0, 3.0, gl, gh - gl + 3.0, 'Gate'); counts['soseul'] = counts.get('soseul', 0) + 1
                        else: gate = False
                    if not gate: wall_run(bl, cx, cz, fy, -(w_ / 2 + 0.6), w_ / 2 + 0.6, fz_, Z_['wall'], gap=(-1.3, 1.3))
                    for sgn in (-1, 1):
                        if R.random() < 0.6: wall_run(bl, cx + 0, cz, fy + 90, -(d_ / 2), d_ / 2 + yard - 0.5, sgn * (w_ / 2 + 0.6) * (1 if True else 1), Z_['wall'])
                    counts['walled'] = counts.get('walled', 0) + 1
                if R.random() < 0.32:
                    jx, jz = to_world(cx, cz, fy, w_ / 2 - 1.4, -(d_ / 2 + 1.3)); bl.add('Prop_Jars', jx, g(jx, jz), jz, fy + R.uniform(-20, 20), l=1); counts['jangdok'] = counts.get('jangdok', 0) + 1
                x += w_ + R.uniform(2.4, 6.5)
            z += pz + (lane if not face_s else R.uniform(0.5, 2.0)); row += 1
        for key, (Gb, bl) in cells.items():      # 공동 우물: one per neighbourhood cell with 4+ houses, on free level ground near the middle
            hs = [h for h in bl.h if not h['a'].startswith(('Gen_Pedestal', 'Gen_Wall', 'Prop_'))]
            if len(hs) < 4: continue
            mx = sum(h['p'][0] for h in hs) / len(hs); mz = sum(h['p'][2] for h in hs) / len(hs)
            for _ in range(40):
                wx_, wz_ = mx + R.uniform(-16, 16), mz + R.uniform(-16, 16); lo, hi = ground(wx_, wz_, 3.0, 3.0, 0)
                if hi - lo < 0.35 and free(wx_, wz_, 3.0, 3.0, 0, 0.4):
                    bl.add('Prop_Well_Changwon', wx_, lo, wz_, R.uniform(0, 90), l=1); colbox(Gb, wx_, wz_, 0, 1.8, 1.8, lo - 0.3, 1.5, 'Well'); mark(wx_, wz_, 2.4, 2.4, 0, 0.2); wells += 1; anchors['wells'].append(dict(zone=Z_['id'], at=[round(wx_, 1), round(lo, 2), round(wz_, 1)])); break
        for Gb, bl in cells.values(): bl.place(Gb)
        counts['houses_' + Z_['id']] = nh; counts['wells_' + Z_['id']] = wells; counts['fail_' + Z_['id']] = [counts['f_occ'], counts['f_rel']]
    counts['house_types'] = zc; counts['housing_tris'] = spent
    # 빨래터: flat stones on the bank nearest to the 서소문 안 마을 lanes (9 m off the wet cells)
    u = unit('Town_Life'); Gl = G(u, 'Laundry', 1752, 2836, cull=0.01)
    for (x, z) in ((1741.0, 2832.0), (1764.0, 2946.0)):
        y = g(x, z)
        for q in range(5): px, pz_ = x + q * 1.3 - 2.6, z + (q % 2) * 0.8; Gl.box('stone_rough', px - 0.55, g(px, pz_) - 0.4, pz_ - 0.4, px + 0.55, g(px, pz_) + 0.16, pz_ + 0.4)
        anchors['laundry'].append(dict(at=[x, round(y, 2), z]))


# =====================================================================================================
# C6  성균관 · 혜민서 · 훈련원
# =====================================================================================================
def c6():
    u = unit('Civic')
    # 성균관: 대성전(북) + 명륜당(남)  -  실제는 전묘후학(대성전이 앞). 터의 단 높이에 맞춰 둘을 놓는다
    s = SITE['seonggyun']; Gs = G(u, 'Seonggyungwan', s['x'], s['z'], cull=0.005); x0, z0, x1, z1 = s['x'] - 28, s['z'] - 40, s['x'] + 28, s['z'] + 40
    Lv = s['level']; zb = [s['z'] - 28.0, s['z'], s['z'] + 28.0]     # three bands, south (high) -> north (low); the halls face north (downhill), the gate is in the north wall
    for nm, hn, k in (('Daeseongjeon', 'Hall_Yeonhuigak_L1', 0), ('Myeongnyundang', 'Hall_Honhwagak_L1', 1)):
        sz = SIZE[hn[:-3]]; lo, hi = ground(s['x'], zb[k], sz[0], sz[2], 0); rep['levels']['sgg_' + nm] = [round(lo, 2), round(hi, 2)]
        if hi - lo > 1.2: rep['warn'].append('seonggyun %s relief %.1f - skipped' % (nm, hi - lo)); continue
        hall(Gs, hn, s['x'], math.ceil(hi / 0.05) * 0.05, zb[k], 180, n='Sgg_' + nm)
    stairs(Gs, s['x'], s['z'] + 14.0, 0, 4.0, Lv[2], Lv[1]); stairs(Gs, s['x'] + 20.0, s['z'] - 14.0, 0, 3.0, Lv[1], Lv[0])
    wall(Gs, (x0, z1), (x1, z1), h=2.4, gaps=[(28 - 5.2, 28 + 5.2)]); wall(Gs, (x0, z0), (x1, z0), h=2.4); wall(Gs, (x0, z0), (x0, z1), h=2.4); wall(Gs, (x1, z0), (x1, z1), h=2.4)
    samun(Gs, s['x'], z1, 'x', math.ceil(max(g(s['x'] + d, z1) for d in (-5, 0, 5)) / 0.05) * 0.05, bays=3, bay=3.0, h=3.4, n='Sgg_Sinsammun')
    # 혜민서: 담 + 삼문 + 대청
    s = SITE['hyemin']; Gh = G(u, 'Hyeminseo', s['x'], s['z'], cull=0.006); x0, z0, x1, z1 = s['x'] - 14, s['z'] - 12, s['x'] + 14, s['z'] + 12; Y = s['level'][0]
    wall(Gh, (x0, z0), (x1, z0), h=2.3, gaps=[(14 - 4.6, 14 + 4.6)]); wall(Gh, (x0, z1), (x1, z1), h=2.3); wall(Gh, (x0, z0), (x0, z1), h=2.3); wall(Gh, (x1, z0), (x1, z1), h=2.3)
    samun(Gh, s['x'], z0, 'x', Y, bays=3, bay=2.6, h=3.1, depth=2.8, n='Hyemin_Sammun'); hall(Gh, 'Hall_Wooryeondang_L1', s['x'], Y, s['z'] + 3.0, 0, (0.8, 0.9, 0.8), n='Hyemin_Daecheong')
    # 훈련원: 사청 + 과녁 셋
    s = SITE['hullyeon']; Gt = G(u, 'Hullyeonwon', s['x'], s['z'], cull=0.006); Y = s['level'][0]
    hall(Gt, 'Hall_Yeonjuhyeopdang_L1', s['x'], Y, s['z'] + 20.0, 0, n='Hullyeon_Sacheong')
    for k in (-1, 0, 1):
        tx_, tz_ = s['x'] + k * 9.0, s['z'] - 25.0
        for sx in (-0.9, 0.9): Gt.box('wood_dark', tx_ + sx - 0.07, Y - 0.3, tz_ - 0.07, tx_ + sx + 0.07, Y + 2.6, tz_ + 0.07)
        Gt.box('h308_cloth', tx_ - 1.0, Y + 0.7, tz_ - 0.04, tx_ + 1.0, Y + 2.5, tz_ + 0.04, True); Gt.col(tx_ - 1.0, Y, tz_ - 0.15, tx_ + 1.0, Y + 2.6, tz_ + 0.15, n='Target')
    anchors['plazas'].append(dict(id='hullyeonwon', at=[s['x'], Y, s['z']], r=22))
