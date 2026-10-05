# -*- coding: utf-8 -*-
"""#308 map 3 - MapFog308_Edge / MapFog308_Rim of the map3 cginc in numpy, for the canonical tools (map308_edgecheck.py,
map308_twin.py, map308_bcheck.py). A cginc carries this formula when it has `#define MAPFOG308_E_GATE0` (is_map3(q)).

    field, gate = edge3(q, at, ci, cj, fx, fy, u, v, ppc, noise)     statement by statement, the shader's lines in the comments
    rim         = rim3(q, gate, edge_px, rim_px)                     the paper's line: rim = crisp x MapFog308_Rim(walk.y, edgePx, _M308Rim.y)
    rules       = constant_rules3(q)                                 [(text, ok)] - the #214 constant rules of this formula

q = the cginc's `#define MAPFOG308_E_<name>` values ({name: float | (float, float)}), read by the caller and never retyped.
at(ii, jj) = the walked value (0 / 1, float32) of fog cell (row ii, column jj), clamped; ci, cj = the pixel's cell; fx, fy = its
fraction inside the cell; u, v = the cell coordinate c (float32); ppc = px per cell; noise(x, y) = MapFog308_Noise.
The reference twin is Tools/Unity/Stage308_map3/Offline/map3_edge2.py (field_L); map3_edge3_test.py measures the difference.
"""
import numpy as np

f32 = np.float32
NAMES3 = ('DEEP', 'SK', 'S0', 'S1', 'SG', 'SWMAX', 'BITE', 'BK', 'B0', 'B1', 'BO', 'HALF3', 'FK', 'FE', 'CK', 'RS', 'RD1', 'RD2', 'RPH', 'RF0', 'RF1', 'RIM2', 'WMIN', 'WMAX', 'GATE0')
FLOOR_PER_KCOVER = .809
TEXT3 = ('float land=min(depth,MAPFOG308_E_KCOVER*cover);', 'theta=max(theta,MAPFOG308_E_FLOOR/pxPerCell);', 'float field=land-theta;',
         'field=min(field,dmix-theta-sw);', '*saturate(MAPFOG308_E_CK*cover);', 'return float2(field,gate);')
PAPER3 = 'float rim=crisp*MapFog308_Rim(walk.y,edgePx,_M308Rim.y)*(1-interiorOn);'


def is_map3(q): return q is not None and 'GATE0' in q


def missing3(q): return [k for k in NAMES3 if k not in q]


def sat(x): return np.clip(x, 0, 1)


def sstep(a, b, x):
    t = sat((x - a) / (b - a)); return t * t * (3 - 2 * t)


def edge3(q, at, ci, cj, fx, fy, u, v, ppc, noise):
    mn, mx = np.minimum, np.maximum
    w = {(a, b): at(ci + a, cj + b) for a in (-1, 0, 1) for b in (-1, 0, 1)}                # w00 .. w22 (row, column)
    corner = lambda a, b: mn(mn(w[(a - 1, b - 1)], w[(a - 1, b)]), mn(w[(a, b - 1)], w[(a, b)]))
    cover = (corner(0, 0) * (1 - fx) + corner(0, 1) * fx) * (1 - fy) + (corner(1, 0) * (1 - fx) + corner(1, 1) * fx) * fy   # cover=lerp(lerp(k00,k10,f.x),lerp(k01,k11,f.x),f.y);
    ax = [.5 * (1 - fx) * (1 - fx), None, .5 * fx * fx]; ay = [.5 * (1 - fy) * (1 - fy), None, .5 * fy * fy]              # a0=.5*(1-f)*(1-f); a2=.5*f*f;
    ax[1] = 1 - ax[0] - ax[2]; ay[1] = 1 - ay[0] - ay[2]                                    # a1=1-a0-a2;
    bs = 0
    for a in (-1, 0, 1):
        for b in (-1, 0, 1): bs = bs + w[(a, b)] * ax[b + 1] * ay[a + 1]                    # bs=(w00*a0.x+...)*a0.y+...
    depth = 1 - np.sqrt(mx(2 * (1 - bs), 0))                                                # depth=1-sqrt(max(2*(1-bs),0));
    land = mn(depth, q['KCOVER'] * cover)                                                   # float land=min(depth,MAPFOG308_E_KCOVER*cover);
    nz = lambda k, off: noise((u * f32(k) + f32(off)).astype(f32), (v * f32(k) + f32(off)).astype(f32))
    n1 = nz(q['PK'], 5.2); n2 = nz(q['PK'], 31.7)
    wave1 = np.sin((u * f32(q['D1'][0]) + v * f32(q['D1'][1]) + f32(q['PHASE']) * n1).astype(f32)) * sat(ppc * q['HALF1'] * .5 - .5)
    wave2 = np.sin((u * f32(q['D2'][0]) + v * f32(q['D2'][1]) + f32(q['PHASE']) * n2).astype(f32)) * sat(ppc * q['HALF2'] * .5 - .5)
    theta = f32(q['FROM']) + f32(q['SPAN']) * (.5 + .25 * (wave1 + wave2))                  # theta=FROM+SPAN*(.5+.25*(wave1+wave2));
    theta = mx(theta, q['FLOOR'] / ppc)                                                     # theta=max(theta,MAPFOG308_E_FLOOR/pxPerCell);
    field = land - theta                                                                    # float field=land-theta;
    if q['DEEP']:                                                                           # #if MAPFOG308_E_DEEP
        v25 = dict(w)
        for a in (-2, -1, 0, 1, 2):
            for b in (-2, -1, 0, 1, 2):
                if (a, b) not in v25: v25[(a, b)] = at(ci + a, cj + b)                      # v00 .. v44: the ring of sixteen
        h = {(a, b): mn(mn(v25[(a, b - 1)], v25[(a, b)]), v25[(a, b + 1)]) for a in (-2, -1, 0, 1, 2) for b in (-1, 0, 1)}   # h0 .. h4
        e = {(a, b): mn(mn(h[(a - 1, b)], h[(a, b)]), h[(a + 1, b)]) for a in (-1, 0, 1) for b in (-1, 0, 1)}               # e0 .. e2
        bse = 0
        for a in (-1, 0, 1):
            for b in (-1, 0, 1): bse = bse + e[(a, b)] * ax[b + 1] * ay[a + 1]              # bsE=dot(e0,ax)*a0.y+dot(e1,ax)*a1.y+dot(e2,ax)*a2.y;
        de = np.sqrt(2 * mn(bse, .5)) + 1 - np.sqrt(mx(2 * (1 - mx(bse, .5)), 0))           # de=sqrt(2*min(bsE,.5))+1-sqrt(max(2*(1-max(bsE,.5)),0));
        dx = (fx - 1, 1 - 2 * fx, fx); dy = (fy - 1, 1 - 2 * fy, fy)                         # d0=f-1; d1=1-2*f; d2=f;
        gx = 0; gy = 0
        for a in (-1, 0, 1):
            for b in (-1, 0, 1):
                gx = gx + w[(a, b)] * dx[b + 1] * ay[a + 1]; gy = gy + w[(a, b)] * ax[b + 1] * dy[a + 1]                    # g=float2(...);
        pull = mx(depth, 0) / (np.hypot(gx, gy) + q['FE'])
        fu = (u - gx * pull).astype(f32); fv = (v - gy * pull).astype(f32)                  # foot=c-g*(max(depth,0)/(length(g)+FE));
        bite = sstep(q['S0'], q['S1'], noise((fu * f32(q['SK']) + f32(47.3)).astype(f32), (fv * f32(q['SK']) + f32(47.3)).astype(f32))) * sat((1 - depth) * q['FK'])   # bite=smoothstep(S0,S1,Noise(foot*SK+47.3))*saturate((1-depth)*FK);
        sweep = sstep(q['B0'], q['B1'], nz(q['BK'], 73.9))                                  # sweep=smoothstep(B0,B1,Noise(c*BK+73.9));
        sw = (q['SG'] + q['SWMAX'] * bite + q['BITE'] * sweep) * sat(ppc * q['HALF3'] * .5 - .5) * sat(q['CK'] * cover)     # sw=(SG+SWMAX*bite+BITE*sweep)*rows*saturate(pxPerCell*HALF3*.5-.5)*saturate(CK*cover);
        dmix = de + q['BO'] * mx(depth - de, 0)                                             # dmix=de+BO*max(depth-de,0);
        field = mn(field, dmix - theta - sw)                                                # field=min(field,dmix-theta-sw);
    # rows=saturate(_FogTex_TexelSize.w-1) is exactly 1 on the world's fog (188 rows) - the only fog these tools draw. On a fog of one
    # row (the legend swatch) the shader folds the break pattern away and bites nothing; that case is not modelled here.
    fr = sat((ppc - q['RF0']) / (q['RF1'] - q['RF0']))                                      # fr=saturate((pxPerCell-RF0)/(RF1-RF0))*rows;
    rs = .5 + .25 * (np.sin((u * f32(q['RD1'][0]) + v * f32(q['RD1'][1]) + f32(q['RPH']) * n2).astype(f32)) + np.sin((u * f32(q['RD2'][0]) + v * f32(q['RD2'][1]) + f32(q['RPH']) * n1).astype(f32)))
    r1 = nz(q['RK'], 23.1)
    s = 1 + ((r1 + (rs - r1) * q['RS']) - 1) * fr                                           # s=lerp(1,lerp(Noise(c*RK+23.1),rs,RS),fr);
    weight = sat((s - q['RIM0']) / (q['RIM2'] - q['RIM0']))                                 # weight=saturate((s-RIM0)/(RIM2-RIM0));
    gate = mx(sat(field + q['GATE0']), 1 - .08 * (1 + q['RIMBREAK'] * (weight - 1)))        # gate=max(saturate(field+GATE0),1-.08*lerp(1,weight,RIMBREAK));
    return field, gate                                                                      # return float2(field,gate);


def rim3(q, gate, edge_px, rim_px):
    """MapFog308_Rim(y, edgePx, rimPx) - multiply by crisp (and 1 - interiorOn) as the paper does"""
    w = sat((1 - gate) * 12.5); press = w * w * (3 - 2 * w)
    ink = sat(w * (q['RIM2'] - q['RIM0']) / (q['RIM1'] - q['RIM0']))
    return ink * sat(rim_px * (q['WMIN'] + (q['WMAX'] - q['WMIN']) * press) - edge_px + .5)


def constant_rules3(q):
    need = FLOOR_PER_KCOVER * q['KCOVER']; top = q['FROM'] + q['SPAN'] + q['SG'] + q['SWMAX'] + q['BITE']
    return [('FROM %g > 0' % q['FROM'], q['FROM'] > 0), ('FROM + SPAN %g < 1' % (q['FROM'] + q['SPAN']), q['FROM'] + q['SPAN'] < 1),
            ('FLOOR %g >= .809 x KCOVER %g = %g' % (q['FLOOR'], q['KCOVER'], need), q['FLOOR'] >= need - 1e-9),
            ('SG %g < 0' % q['SG'], q['SG'] < 0), ('SWMAX %g >= 0 and BITE %g >= 0' % (q['SWMAX'], q['BITE']), q['SWMAX'] >= 0 and q['BITE'] >= 0),
            ('FROM + SPAN + SG + SWMAX + BITE %g < 1' % top, top < 1), ('BO %g >= 1' % q['BO'], q['BO'] >= 1), ('CK %g > 0, FE %g > 0, FK %g >= 1' % (q['CK'], q['FE'], q['FK']), q['CK'] > 0 and q['FE'] > 0 and q['FK'] >= 1),
            # review 2026-10-05 (review_leak_indep.py, worst-case reading): the extra inset may not grow faster off an unwalked cell than
            # the lattice-corner clamp does. CK 4 passes with any bite / sweep noise (least margin .14 px); CK 6 does not, CK 16 leaves
            # .02 px with the real noise, CK 4000 leaks - and "CK > 0" alone let all of them through.
            ('(SWMAX + BITE) x CK %g <= KCOVER %g' % ((q['SWMAX'] + q['BITE']) * q['CK'], q['KCOVER']), (q['SWMAX'] + q['BITE']) * q['CK'] <= q['KCOVER'] + 1e-9),
            ('0 < GATE0 %g < 1' % q['GATE0'], 0 < q['GATE0'] < 1), ('RIM0 < RIM1 <= RIM2', q['RIM0'] < q['RIM1'] <= q['RIM2']), ('0 <= RF0 %g < RF1 %g' % (q['RF0'], q['RF1']), 0 <= q['RF0'] < q['RF1']),
            ('0 <= WMIN <= WMAX %g <= 4 / 3' % q['WMAX'], 0 <= q['WMIN'] <= q['WMAX'] <= 4 / 3)]
