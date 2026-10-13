"""#308 cliffs - the climbing cliff ways of stage n7 (SPEC-WORLD-CLIFFS-308 section 5, fix pass 2026-10-12). Called by cliffs308.py.
A way is a ZIG-ZAG PLANK GALLERY pinned to the play side of a tall face: legs run back and forth along the wall, each a little
higher and (the face leans back) a little further in, joined by landings, up to a lookout platform at `share` of the face height.
Nothing is done to the terrain: the walked surface is the deck collider (one even slab per bay), so its slope is the leg grade
and there is no step. The first version (terrain shelves filled against the wall) could not keep a 4 m lattice under 35 deg.
Kit and irregularity numbers: Tools/Unity/Stage308_rugged/gallery308.json (D308-57).
"""
import hashlib, json, math, sys
from pathlib import Path
import numpy as np
from scipy import ndimage as ndi

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B                                                # noqa: E402
from cliff308_base import CELL                                             # noqa: E402
from rugged308 import noise2d, walk_reach, smoothstep                      # noqa: E402
import cliffs308 as C                                                      # noqa: E402
from gallery308 import Mesh, smooth                                        # noqa: E402

UP = np.array([0.0, 1.0, 0.0])


def bil(a, x, z):
    u = np.clip(np.asarray(x, float) / CELL, 0, a.shape[1] - 1.001); w = np.clip(np.asarray(z, float) / CELL, 0, a.shape[0] - 1.001); j = np.floor(u).astype(int); i = np.floor(w).astype(int); fu = u - j; fw = w - i
    return a[i, j] * (1 - fu) * (1 - fw) + a[i, j + 1] * fu * (1 - fw) + a[i + 1, j] * (1 - fu) * fw + a[i + 1, j + 1] * fu * fw


def body_sd(Fn, sigma=1.0):
    di = ndi.distance_transform_edt(Fn, sampling=CELL); do = ndi.distance_transform_edt(~Fn, sampling=CELL); return ndi.gaussian_filter(np.where(Fn, di - 2.0, -(do - 2.0)), sigma)


def unit(v):
    return v / np.linalg.norm(v)


def trace(sd, start, direction, length, iso, step=1.0):
    """walk the iso line sd = iso from the point nearest `start`; `direction` = +1 keeps the body on the left."""
    gz, gx = np.gradient(sd, CELL); p = np.array(start, float)

    def grad(q):
        g = np.array([float(bil(gx, q[0], q[1])), float(bil(gz, q[0], q[1]))]); n = np.linalg.norm(g); return g / n if n > 1e-9 else np.array([1.0, 0.0])

    def snap(q):
        for _ in range(12): q = q + grad(q) * (iso - float(bil(sd, q[0], q[1])))
        return q
    p = snap(p); pts = [p.copy()]; nrm = [grad(p)]
    for _ in range(int(length / step)):
        g = grad(p); t = np.array([g[1], -g[0]]) * direction; p = snap(p + t * step); pts.append(p.copy()); nrm.append(grad(p))
    return np.asarray(pts), np.asarray(nrm)


def route(cfg, cfg_p):
    f, rep, ex = C.faces(cfg, log=lambda *a: None); Fn = ex['Fn'] | ex['F']; K = ex['K']; hard = ex['hard']; sd = body_sd(Fn); out = []
    ground = lambda x, z: float(B.sample_tri(f, x, z))
    for way in cfg['ways']:
        Lw = way['wall_len_m']; pts, nrm = trace(sd, way['start'], way['dir'], Lw, -way['guide_out_m']); nrm = np.stack([smooth(nrm[:, c], 9) for c in range(2)], 1); nrm /= np.linalg.norm(nrm, axis=1, keepdims=True)
        ug = np.concatenate([[0], np.cumsum(np.hypot(np.diff(pts[:, 0]), np.diff(pts[:, 1])))]); Lw = float(ug[-1])
        gh = B.sample_tri(f, pts[:, 0], pts[:, 1]); ds = np.arange(0.0, way['crest_search_m'], 1.0)
        crest = np.array([float(B.sample_tri(f, pts[k, 0] + nrm[k, 0] * ds, pts[k, 1] + nrm[k, 1] * ds).max()) for k in range(len(pts))])
        y0 = float(gh[0]); foot = float(np.median(gh)); H = float(np.median(crest) - foot); y_top = foot + way['share'] * H; rise = y_top - y0
        n_legs = max(2, int(math.ceil(rise / (Lw * math.tan(math.radians(way['grade_deg'])))))); leg_rise = rise / n_legs; grade = math.degrees(math.atan(leg_rise / Lw)); dw = way['deck_w_m']; legs = []
        at = lambda u: (np.array([np.interp(u, ug, pts[:, 0]), np.interp(u, ug, pts[:, 1])]), unit(np.array([np.interp(u, ug, nrm[:, 0]), np.interp(u, ug, nrm[:, 1])])))
        for k in range(n_legs):
            T = np.linspace(0, 1, int(Lw / 0.5) + 1); U = T * Lw if k % 2 == 0 else (1 - T) * Lw; fl = way['landing_m'] / 2 + 0.4
            ramp = lambda sv, L_: np.clip((sv - fl) / max(L_ - 2 * fl, 1.0), 0, 1)      # level at both ends (the landings), an even grade between
            Y = y0 + (k + ramp(T * Lw, Lw)) * leg_rise
            ong = Y < np.interp(U, ug, gh) + way['over_ground_m']      # where the foot ground itself climbs along the wall faster than the leg, the leg is a boardwalk lying on that ground (on the guide line)
            Y = np.maximum(Y, np.interp(U, ug, gh) + way['over_ground_m'])
            # the arriving end swings out by one deck width: on the landing the next leg leaves on the inner lane, beside it and not over it
            lane = smoothstep((T * Lw - (Lw - way['swing_m'])) / (way['swing_m'] * 0.4)) if k < n_legs - 1 else np.zeros(len(T)); dface = np.empty(len(T)); G = []; N = []
            for q in range(len(T)):
                g, n = at(U[q]); G.append(g); N.append(n); dd = np.arange(-2.0, way['crest_search_m'], 0.25); hh = B.sample_tri(f, g[0] + n[0] * dd, g[1] + n[1] * dd); hit = np.flatnonzero(hh >= Y[q] - 0.02)
                if not len(hit): raise SystemExit(f"REFUSED: way {way['id']} leg {k}: no rock at {Y[q]:.1f} m within {way['crest_search_m']} m of the wall foot (share too high for this face)")
                dface[q] = dd[hit[0]]
            G = np.asarray(G); N = np.asarray(N); dface = np.minimum(dface, smooth(dface, 9)); off = way['deck_gap_m'] + dw / 2 + lane * dw; P = np.where(ong[:, None], G, G + N * (dface - off)[:, None])
            for rnd in range(4):      # the deck never runs into rock: the inner edge, the centre and the outer edge stay clear_m over the terrain
                for q in range(len(T)):
                    if ong[q]: Y[q] = max(ground(P[q, 0] + N[q, 0] * o, P[q, 1] + N[q, 1] * o) for o in (0.0, dw / 2, -dw / 2)) + way['over_ground_m']; continue
                    d = 0.0
                    while d < 8.0 and max(ground(P[q, 0] + N[q, 0] * (o - d), P[q, 1] + N[q, 1] * (o - d)) for o in (0.0, dw / 2, -dw / 2)) > Y[q] - way['clear_m']: d += 0.25
                    P[q] -= N[q] * d
                if rnd < 3: P = np.stack([smooth(P[:, c], 7) for c in range(2)], 1)
            # the height is re-timed on the length actually walked (a leg pushed off a bulge is shorter in places than its guide), then cleared once more
            sa = np.concatenate([[0], np.cumsum(np.hypot(np.diff(P[:, 0]), np.diff(P[:, 1])))]); Y2 = y0 + (k + ramp(sa, sa[-1])) * leg_rise; Y = np.where(ong, Y, np.maximum(Y2, np.interp(U, ug, gh) + way['over_ground_m']))
            for q in range(len(T)):
                if ong[q]: continue
                d = 0.0
                while d < 8.0 and max(ground(P[q, 0] + N[q, 0] * (o - d), P[q, 1] + N[q, 1] * (o - d)) for o in (0.0, dw / 2, -dw / 2)) > Y[q] - way['clear_m']: d += 0.25
                P[q] -= N[q] * d
            sa = np.concatenate([[0], np.cumsum(np.hypot(np.diff(P[:, 0]), np.diff(P[:, 1])))]); gmax = float(np.degrees(np.arctan(np.max(np.abs(np.diff(smooth(Y, 5))) / np.maximum(np.diff(sa), 0.05))))) if len(sa) > 2 else 0.0
            legs.append(dict(x=P[:, 0].round(3).tolist(), z=P[:, 1].round(3).tolist(), y=Y.round(3).tolist(), nx=N[:, 0].round(4).tolist(), nz=N[:, 1].round(4).tolist(), lane=lane.round(3).tolist(), on_ground=ong.astype(int).tolist(), grade_max_deg=round(gmax, 1)))
        landings = []
        for k in range(n_legs - 1):
            a = legs[k]; b = legs[k + 1]; E = np.array([a['x'][-1], a['z'][-1]]); S = np.array([b['x'][0], b['z'][0]]); n = np.array([a['nx'][-1], a['nz'][-1]]); t = np.array([n[1], -n[0]])
            landings.append(dict(c=[round(float((E[0] + S[0]) / 2), 3), round(float(a['y'][-1]), 3), round(float((E[1] + S[1]) / 2), 3)], t=t.round(4).tolist(), n=n.round(4).tolist(), along=way['landing_m'],
                                 across=round(float(abs(np.dot(E - S, n)) + dw + 0.3), 2), gap=round(float(np.linalg.norm(E - S)), 2)))
        L = legs[-1]; n = np.array([L['nx'][-1], L['nz'][-1]]); t = np.array([n[1], -n[0]]); tp = way['top']; E = np.array([L['x'][-1], L['z'][-1]]); c = E - n * (tp['across_m'] / 2 - dw / 2)
        trav = unit(np.array([L['x'][-1] - L['x'][-8], L['z'][-1] - L['z'][-8]])); n2 = np.array([-trav[1], trav[0]]); n2 = n2 if np.dot(n2, n) > 0 else -n2      # the lookout lies along the way the leg comes in, its inner edge in line with the deck's
        t = trav; n = n2; c = E - n * (tp['across_m'] / 2 - dw / 2) + trav * (tp['along_m'] / 2 - 0.4)
        top = dict(c=[round(float(c[0]), 3), round(float(L['y'][-1]), 3), round(float(c[1]), 3)], t=t.round(4).tolist(), n=n.round(4).tolist(), along=tp['along_m'], across=tp['across_m'])
        allx = np.concatenate([l_['x'] for l_ in legs]); allz = np.concatenate([l_['z'] for l_ in legs]); jj = np.rint(allx / 4).astype(int); ii = np.rint(allz / 4).astype(int)
        if hard[ii, jj].any(): raise SystemExit(f"REFUSED: way {way['id']} stands over {int(hard[ii, jj].sum())} wet / protected cells")
        length = float(sum(np.hypot(np.diff(l_['x']), np.diff(l_['z'])).sum() for l_ in legs))
        w = dict(id=way['id'], realm=C.realm_of(pts[0, 0], pts[0, 1]), legs=legs, landings=landings, top=top, n_legs=n_legs, grade_deg=round(grade, 2), rise_m=round(rise, 1), face_height_m=round(H, 1), share_of_face=round((y_top - foot) / H, 3),
                 foot_y=round(foot, 1), top_y=round(y_top, 1), crest_y=round(float(np.median(crest)), 1), wall_above_top_m=round(float(np.median(crest) - y_top), 1), length_m=round(length, 1), wall_len_m=round(Lw, 1),
                 start=[round(float(legs[0]['x'][0]), 1), round(y0, 1), round(float(legs[0]['z'][0]), 1)], top_at=[top['c'][0], top['c'][1], top['c'][2]], keepout_cells_under=int(K[ii, jj].sum()))
        out.append(w)
        print(f"cliffs308 route {w['id']} ({w['realm']}): {n_legs} legs x {Lw:.0f} m at {grade:.1f} deg = {length:.0f} m, rise {rise:.1f} m of a {H:.1f} m face ({w['share_of_face']:.0%}), {w['wall_above_top_m']} m of wall above, start {w['start']} top {w['top_at']}, keep-out cells under the decks {w['keepout_cells_under']}")
    B.write_json(C.route_path(cfg), dict(version=cfg['version'], stage=cfg['stage'], faces_sha256=B.sha256(C.stage_path(cfg['stage'], True)), ways=out), indent=None)
    return out


def shelves(cfg, new, ex):
    """the ways no longer touch the terrain. This is the proof: what ground can a walker step onto FROM a deck?
    Seeds = terrain nodes within step_reach_m of a deck station (from_height_m or more over the way's start) whose height is within jump reach of
    the deck and that carry a walkable link. From the seeds the walker floods over walkable links on the cliff bodies; reached cells, their top
    and any exit are reported."""
    R = json.loads(C.route_path(cfg).read_text(encoding='utf-8'))
    if R['faces_sha256'] != B.sha256(C.stage_path(cfg['stage'], True)): raise SystemExit('REFUSED: the route was traced on another faces field (run cliffs308.py faces, then route)')
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components
    r, c = C._links(new); N = new.size; band = (ex['band']).ravel(); play = np.load(C.PLAY).ravel(); ok = band[r] & band[c]
    _, lab = connected_components(coo_matrix((np.ones(int(ok.sum()), bool), (r[ok], c[ok])), shape=(N, N)), directed=False); walk_node = np.zeros(N, bool); walk_node[r] = True; walk_node[c] = True
    X, Z = ex['X'], ex['Z']; rep = {}; pv = cfg['proof']
    for w in R['ways']:
        seeds = np.zeros(new.shape, bool); y0 = w['start'][1]
        for leg in w['legs']:
            for q in range(0, len(leg['x']), 2):
                if leg['y'][q] - y0 < pv['from_height_m']: continue
                j0, i0 = int(round(leg['x'][q] / 4)), int(round(leg['z'][q] / 4)); sl_ = (slice(i0 - 2, i0 + 3), slice(j0 - 2, j0 + 3))
                near = (np.hypot(X[sl_] - leg['x'][q], Z[sl_] - leg['z'][q]) <= pv['step_reach_m']) & (new[sl_] <= leg['y'][q] + pv['jump_m']) & (new[sl_] >= leg['y'][q] - pv['drop_m']); seeds[sl_] |= near
        seeds &= walk_node.reshape(new.shape) & band.reshape(new.shape); comp = np.unique(lab[seeds.ravel()]) if seeds.any() else np.array([], int)
        reached = np.isin(lab, comp).reshape(new.shape) & band.reshape(new.shape) if len(comp) else np.zeros(new.shape, bool)
        rf = reached.ravel(); exits = int(((rf[r] & ~band[c] & ~play[c]) | (rf[c] & ~band[r] & ~play[r])).sum()); back_to_play = int(((rf[r] & ~band[c] & play[c]) | (rf[c] & ~band[r] & play[r])).sum())
        rep[w['id']] = dict(realm=w['realm'], legs=w['n_legs'], grade_deg=w['grade_deg'], length_m=w['length_m'], rise_m=w['rise_m'], face_height_m=w['face_height_m'], share_of_face=w['share_of_face'], wall_above_top_m=w['wall_above_top_m'],
                            start=w['start'], top=w['top_at'], terrain_changed_cells=0, seed_nodes_beside_decks=int(seeds.sum()), body_cells_reachable_from_decks=int(reached.sum()),
                            highest_reachable_over_top_m=round(float(new[reached].max() - w['top_y']), 1) if reached.any() else None, exits_to_ground_outside_play=exits, links_back_to_play_ground=back_to_play, reachable_clusters=B.clusters(reached, 40, 6))
    rep['_seal'] = dict(note='the ways add no terrain: the stage seal numbers are those of the faces (report.seal). Per way: ground a walker could step onto from a deck and everything walkable from there on the cliff bodies.',
                        ways_with_exits=[k for k, v in rep.items() if v['exits_to_ground_outside_play']], ways_reaching_above_top=[k for k, v in rep.items() if (v['highest_reachable_over_top_m'] or -99) > pv['jump_m']])
    return new, rep


def build(cfg, cfg_p):
    R = json.loads(C.route_path(cfg).read_text(encoding='utf-8')); f = B.load_height(C.stage_path(cfg['stage'])); G = json.loads((B.ROOT / cfg['kit']).read_text(encoding='utf-8'))
    M = G['materials']; W_ = G['wood']; J = G['irregular']; OUT = C.OUT; (OUT / 'Meshes').mkdir(parents=True, exist_ok=True)
    for old in (OUT / 'Meshes').glob('*.json'): old.unlink()
    ground = lambda p: float(B.sample_tri(f, p[0], p[2])); lay = []; rocks = []; cw_all = {c['id']: c for c in cfg['ways']}
    for w in R['ways']:
        cw = cw_all[w['id']]; dw = cw['deck_w_m']; dt = W_['deck_t']; rng = np.random.default_rng(int.from_bytes(hashlib.sha256(f"{cfg['seed']}:{w['id']}".encode()).digest()[:4], 'little')); U = lambda lo_hi: float(rng.uniform(lo_hi[0], lo_hi[1]))
        deck = Mesh(f"Way_{w['id']}_Deck"); frame = Mesh(f"Way_{w['id']}_Frame"); rail = Mesh(f"Way_{w['id']}_Rail"); col = Mesh(f"Way_{w['id']}_Collision")
        st_ = dict(bays=0, planks=0, posts_to_rock=0, post_max_m=0.0, brackets=0, rail_posts=0, rail_m=0.0, rail_gap_m=0.0, deck_over_rock_min_m=99.0, outer_edge_over_rock_max_m=0.0); walk = []

        def post_down(top, n3):
            """a round post from under the deck edge down to the rock (leaning a little to the face)."""
            axis = unit(-UP + n3 * cw['post_lean'])
            for dd in np.arange(0.2, cw['post_max_m'], 0.1):
                c_ = top + axis * dd
                if ground(c_) >= c_[1]:
                    r0 = U(cw['post_r']); frame.log(top, c_ + axis * 0.4, r0, r0 * 1.15, M['frame'], rng=rng, wob=0.08); st_['posts_to_rock'] += 1; st_['post_max_m'] = max(st_['post_max_m'], float(dd)); return

        def rail_run(a, b, n3, tt):
            """rail post at b, top + mid rail a -> b and their collider; a, b = deck edge points, n3 points to the deck side."""
            hgt = U(J['post_h']); pr = U(J['post_r']); lean = math.radians(U(J['post_lean_deg'])); la = rng.uniform(0, 2 * math.pi); axis = unit(UP * math.cos(lean) + (-n3 * math.cos(la) * 0.6 + tt * math.sin(la)) * math.sin(lean))
            rail.log(b - UP * 0.3, b + axis * (hgt + 0.08), pr, pr * 0.8, M['frame'], rng=rng, wob=0.1); st_['rail_posts'] += 1; ln = float(np.linalg.norm(b - a))
            if ln < 0.05: return
            if rng.uniform() < cw['rail_share']:
                rr_ = U(J['rail_r']); rail.log(a + UP * hgt - tt * 0.1, b + UP * hgt + tt * 0.1, rr_, rr_ * U([0.75, 1.0]), M['frame'], rng=rng, wob=0.1); rail.log(a + UP * hgt * U([0.42, 0.58]), b + UP * hgt * U([0.42, 0.58]), rr_ * 0.85, rr_ * 0.7, M['frame'], rng=rng, wob=0.1)
                rt = (b - a) / ln; col.box((a + b) / 2 + UP * hgt / 2, rt, UP, unit(np.cross(rt, UP)), (ln, hgt, 0.06), M['frame']); st_['rail_m'] += ln
            else: st_['rail_gap_m'] += ln

        def platform(c, t2, n2, along, across, y, rails):
            """a flat deck (landing / lookout): collider slab, boards, posts to the rock under the outer side, rails on the named sides."""
            c3 = np.array([c[0], y, c[2]]); t3 = np.array([t2[0], 0.0, t2[1]]); n3 = np.array([n2[0], 0.0, n2[1]]); col.box(c3 - UP * dt / 2, t3, UP, n3, (along, dt, across), M['deck']); x_ = -along / 2
            while x_ < along / 2 - 0.05:
                wpl = min(U(J['plank_w']), along / 2 - x_); deck.box(c3 + t3 * (x_ + wpl / 2) - UP * (dt / 2 + 0.004 + U(J['plank_drop'])), t3, UP, n3, (wpl, dt, across + U(J['plank_over'])), M['deck']); x_ += wpl + U(J['plank_gap']); st_['planks'] += 1
            for sa in (-1, 1):
                frame.log(c3 + t3 * sa * (along / 2 - 0.25) + n3 * (across / 2 + 0.8) - UP * (dt + 0.12), c3 + t3 * sa * (along / 2 - 0.25) - n3 * (across / 2 + 0.15) - UP * (dt + 0.12), 0.1, 0.09, M['frame'], rng=rng, wob=0.08)
                for sn in (-1.0, -0.2): post_down(c3 + t3 * sa * (along / 2 - 0.25) + n3 * sn * (across / 2 - 0.1) - UP * (dt + 0.2), n3)
            cn = {s_: c3 + t3 * s_[0] * along / 2 + n3 * s_[1] * across / 2 for s_ in ((-1, -1), (1, -1), (1, 1), (-1, 1))}
            if 'out' in rails: rail_run(cn[(-1, -1)], cn[(1, -1)], n3, t3)
            if 'end+' in rails: rail_run(cn[(1, -1)], cn[(1, 1)], -t3, n3)
            if 'end-' in rails: rail_run(cn[(-1, 1)], cn[(-1, -1)], t3, -n3)
        for li, leg in enumerate(w['legs']):
            x = np.asarray(leg['x']); z = np.asarray(leg['z']); y = np.asarray(leg['y']); nn = np.stack([leg['nx'], np.zeros(len(x)), leg['nz']], 1); lane = np.asarray(leg['lane']); P = np.stack([x, y, z], 1)
            zone = cw['landing_m'] / 2 + 1.2; n_leg = len(w['legs'])
            s = np.concatenate([[0], np.cumsum(np.hypot(np.diff(x), np.diff(z)))]); stn = [0.0]
            while stn[-1] < s[-1] - J['bay_m'][0]: stn.append(min(float(s[-1]), stn[-1] + U(J['bay_m'])))
            if s[-1] - stn[-1] > 0.3: stn.append(float(s[-1]))
            at = lambda sv: (np.array([np.interp(sv, s, P[:, c]) for c in range(3)]), unit(np.array([np.interp(sv, s, nn[:, c]) for c in range(3)])), float(np.interp(sv, s, lane)))
            prev_c = None; prev_tt = None
            for b, sv in enumerate(stn):
                c_, n3, ln_ = at(sv); inner = c_ + n3 * dw / 2; outer = c_ - n3 * dw / 2; walk.append((li, float(sv), c_.tolist()))
                st_['deck_over_rock_min_m'] = min(st_['deck_over_rock_min_m'], c_[1] - ground(c_)); st_['outer_edge_over_rock_max_m'] = max(st_['outer_edge_over_rock_max_m'], outer[1] - ground(outer))
                if b:
                    tt = c_ - prev_c; L = np.linalg.norm(tt); tt /= L; side = unit(np.cross(UP, tt)); u2 = np.cross(tt, side); mid = (c_ + prev_c) / 2
                    col.box(mid - u2 * dt / 2, tt, u2, side, (L + 0.04, dt, dw + 0.1), M['deck']); x_ = -L / 2
                    if prev_tt is not None:      # a joint slab 5 cm under the surface at every bay station: on the outside of a bend two butt-jointed rectangles leave a wedge open
                        ja = unit(prev_tt + tt); js = unit(np.cross(UP, ja)); col.box(prev_c - np.cross(ja, js) * (dt / 2 + 0.05), ja, np.cross(ja, js), js, (1.1, dt, dw + 0.1), M['deck'])
                    prev_tt = tt
                    while x_ < L / 2 - 0.05:
                        wpl = min(U(J['plank_w']), L / 2 - x_); gap = U(J['plank_gap']); cx_ = x_ + wpl / 2; x_ += wpl + gap; over = U(J['plank_over']); skew = math.radians(U(J['plank_skew_deg'])); drop = U(J['plank_drop'])
                        pt = tt * math.cos(skew) + side * math.sin(skew); ps = unit(np.cross(UP, pt)); pu = np.cross(pt, ps)
                        deck.box(mid + tt * cx_ - n3 * over / 2 - pu * (dt / 2 + 0.004 + drop), pt, pu, ps, (wpl, dt, dw + over), M['deck']); st_['planks'] += 1
                    for off_ in (-dw / 2 + 0.22, dw / 2 - 0.2):
                        r_ = U(J['stringer_r']); frame.log(prev_c + prev_n * off_ - UP * (dt + r_ + 0.01) - tt * 0.15, c_ + n3 * off_ - UP * (dt + r_ + 0.01) + tt * 0.15, r_, r_ * U([0.82, 1.0]), M['frame'], rng=rng, wob=0.08)
                    at_start = li > 0 and sv < zone; at_end = li < n_leg - 1 and sv > s[-1] - zone; high = outer[1] - ground(outer) > cw['rail_from_m']
                    if high and not at_start: rail_run(prev_outer, outer, n3, tt)      # (a leg leaves a landing on the inner lane: no rail between the two lanes there)
                    if (ln_ > 0.3 or prev_lane > 0.3) and not at_end and inner[1] - ground(inner) > cw['rail_from_m']: rail_run(prev_inner, inner, -n3, tt)      # on the swung-out lane there is a gap to the rock: a rail on that side too, up to the landing
                    st_['bays'] += 1
                # a joist into the rock at every bay, and either a post down to the rock or a bracket to the face
                jr = U(J['joist_r']); jy = UP * (dt + 2 * J['stringer_r'][1] + jr); reach = 0.0
                for dd in np.arange(0.0, 6.0, 0.1):
                    q_ = inner + n3 * dd - jy
                    if ground(q_) >= q_[1]: reach = dd; break
                frame.log(inner + n3 * (reach + U(J['joist_embed'])) - jy, outer - n3 * U(J['joist_over']) - jy, jr, jr * U([0.8, 1.0]), M['frame'], rng=rng, wob=0.1)
                if b % cw['post_every_bays'] == 0 or ln_ > 0.3: post_down(outer + n3 * 0.12 - jy * 1.1, n3)
                else:
                    dropm = U(J['brace_drop'])
                    for dd in np.arange(0.0, 8.0, 0.05):
                        q_ = outer + n3 * dd - UP * dropm
                        if ground(q_) >= q_[1]:
                            br = U(J['brace_r']); frame.log(outer - jy * 1.1, q_ + n3 * 0.35, br, br * 0.85, M['frame'], rng=rng, wob=0.1); st_['brackets'] += 1; break
                prev_c, prev_n, prev_inner, prev_outer, prev_lane = c_, n3, inner, outer, ln_
        for k, ld in enumerate(w['landings']):
            platform(ld['c'], ld['t'], ld['n'], ld['along'], ld['across'], ld['c'][1], ('out',))
        tp = w['top']; Lg = w['legs'][-1]; trav = np.array([Lg['x'][-1] - Lg['x'][-8], Lg['z'][-1] - Lg['z'][-8]]); trav_u = unit(trav); fwd = float(np.dot(trav, tp['t'])) > 0
        platform(tp['c'], tp['t'], tp['n'], tp['along'], tp['across'], tp['c'][1], ('out', 'end+' if fwd else 'end-'))      # open on the side the last leg comes in
        for m in (deck, frame, rail, col):
            if m.v: B.write_json(OUT / 'Meshes' / f'{m.name}.json', m.dump(), indent=None)
        walk.sort(key=lambda t_: (t_[0], t_[1])); wx = [round(t_[2][0], 3) for t_ in walk]; wy = [round(t_[2][1], 3) for t_ in walk]; wz = [round(t_[2][2], 3) for t_ in walk]
        for step_ in (1.2, 2.4):      # onto the lookout, straight ahead
            wx.append(round(Lg['x'][-1] + trav_u[0] * step_, 3)); wy.append(tp['c'][1]); wz.append(round(Lg['z'][-1] + trav_u[1] * step_, 3))
        lay.append(dict(id=w['id'], realm=w['realm'], walkX=wx, walkY=wy, walkZ=wz, legs=w['n_legs'], gradeDeg=w['grade_deg'], length=w['length_m'], rise=w['rise_m'], faceHeight=w['face_height_m'], shareOfFace=w['share_of_face'],
                        wallAboveTop=w['wall_above_top_m'], start=w['start'], top=w['top_at'], vertices={m.name: len(m.v) for m in (deck, frame, rail, col)}, **{k: (round(v, 2) if isinstance(v, float) else v) for k, v in st_.items()}))
    B.write_json(OUT / 'layout.json', dict(version=cfg['version'], stage=cfg['stage'], height_sha256=B.sha256(C.stage_path(cfg['stage'])), ways=lay))
    print('cliffs308 build:', json.dumps([{k: v for k, v in l_.items() if not k.startswith('walk')} for l_ in lay], ensure_ascii=False))
    rocks += foot_rocks(cfg, f)
    dst = B.ROOT / 'Tools/Unity/Stage308_cliffs/Out/rocks.json'
    dst.write_text(json.dumps(dict(version=cfg['version'], stage=cfg['stage'], prefabDir=cfg['rocks']['prefabDir'], counts={k: sum(1 for r in rocks if r['kind'] == k) for k in sorted({r['kind'] for r in rocks})},
                                   by_realm={k: sum(1 for r in rocks if r['realm'] == k) for k in sorted({r['realm'] for r in rocks})}, rocks=rocks), ensure_ascii=False), encoding='utf-8')
    print(f'cliffs308 rocks: {len(rocks)} -> {B.rel(dst)}')


def foot_rocks(cfg, f):
    """dressing blocks where this stage moved the foot: clusters at the toes of the new buttresses and in the re-entrants between them
    (never on a keep-out cell). Half sunk; sizes and realm prefabs from cfg.rocks."""
    rk = cfg['rocks']; base, prev, scarp = C.load_inputs(cfg); X, Z = B.grid_xz(); K, hard, _ = C.keepout(cfg, X, Z); ch = (f - prev) > 0.5
    body = (f - prev > 6.0) | C.footprint(cfg, base, scarp); d_out = ndi.distance_transform_edt(~body, sampling=CELL); d_in = ndi.distance_transform_edt(body, sampling=CELL); sl = B.slope_deg(f)
    near_new = ndi.binary_dilation(ch, iterations=4); rng = np.random.default_rng(int.from_bytes(hashlib.sha256(f"{cfg['seed']}:rocks".encode()).digest()[:4], 'little')); out = []; taken = set()
    dkk = ndi.distance_transform_edt(~K, sampling=CELL)
    def seat(x, z, size, on_face):
        if not on_face: return float(B.sample_tri(f, x, z))
        q = np.linspace(0, 2 * math.pi, 9)[:-1]; return float(min(float(B.sample_tri(f, x, z)), float(np.min(B.sample_tri(f, x + np.cos(q) * size * 0.4, z + np.sin(q) * size * 0.4)))))
    def pick(mask, S, kind):
        ii, jj = np.nonzero(mask); order = rng.permutation(len(ii)); got = 0
        for k in order:
            if got >= S['count']: break
            i, j = int(ii[k]), int(jj[k]); key = (int(i * 4 // S['spacing_m']), int(j * 4 // S['spacing_m']))
            if key in taken: continue
            taken.add(key); n_cl = int(rng.integers(S['cluster'][0], S['cluster'][1] + 1)); cx, cz = j * 4 + rng.uniform(-1.5, 1.5), i * 4 + rng.uniform(-1.5, 1.5); realm = C.realm_of(cx, cz)
            if not realm or realm not in rk['by_realm']: continue
            for c_ in range(n_cl):
                a = rng.uniform(0, 2 * math.pi); r_ = 0.0 if c_ == 0 else rng.uniform(1.5, S['cluster_r_m']); x = cx + math.cos(a) * r_; z = cz + math.sin(a) * r_
                if not (4 < x < 3990 and 4 < z < 5990) or K[int(round(z / 4)), int(round(x / 4))]: continue
                size = rng.uniform(S['size'][0], S['size'][1]) * (1.0 if c_ == 0 else rng.uniform(0.35, 0.7))
                out.append(dict(prefab=str(rng.choice(rk['face_by_realm'][realm] if kind == 'outcrop' else rk['by_realm'][realm])), realm=realm, x=round(float(x), 2), y=round(seat(x, z, size, kind != 'fallen'), 2), z=round(float(z), 2), yaw=round(float(rng.uniform(0, 360)), 1), size=round(float(size), 2),
                                sink=round(float(rng.uniform(S['sink'][0], S['sink'][1])), 2), tiltX=round(float(rng.uniform(-S['tilt'], S['tilt'])), 1), tiltZ=round(float(rng.uniform(-S['tilt'], S['tilt'])), 1), kind=kind))
            got += 1
    pick(~body & near_new & (d_out <= rk['fallen']['within_m']) & (sl < 32) & (dkk > 6), rk['fallen'], 'fallen')
    pick(body & near_new & (d_in <= rk['toe']['within_m']) & (dkk > 6), rk['toe'], 'toe')
    pick(body & (sl > 52) & (d_in >= rk['outcrop']['in_from_m']) & (d_in <= rk['outcrop']['in_to_m']) & (dkk > 6) & ndi.binary_dilation(near_new, iterations=6), rk['outcrop'], 'outcrop')
    return out
