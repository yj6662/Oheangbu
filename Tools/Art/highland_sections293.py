"""Select and build the site-specific highland sections (#293 Step 1 Cheongrim, Step 2 all five realms).

python Tools/Art/highland_sections293.py                      # every mountain in highland_geo293.SPEC
python Tools/Art/highland_sections293.py mountain_jeokro      # rebuild one mountain (others kept in sections.json)

Writes Art/World/Compact/Rebuild/Highlands293/Variants/:
  sections.json                  selection records per mountain (windows, scores, junction margins) and all ids
  <id>/section.json              route profile (MountainTrailProfile fields), terraces, stones, stats
  <id>/grids.npz                 face / apron / trail (visual + collision) vertex grids for Blender
  <id>/terrain.npz               designed 1m terrain, natural terrain, ceiling for the grid refiner
  <id>/debug-*.png               plan view of the terrain edit and cross sections
"""
from pathlib import Path
import json, math, sys
import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
import highland_geo293 as geo

STONE_VARIANTS = 16


def cross_section(sec, patch, s, path):
    """Side view (uphill to the right) of natural ground, designed terrain, ceiling and skins at arc s."""
    W, H = 900, 520
    img = Image.new('RGB', (W, H), (246, 243, 236)); d = ImageDraw.Draw(img)
    us = np.linspace(-26, 30, 700)
    y0 = float(sec.surface(s))
    nat = sec.natural(np.full_like(us, s), us)
    # designed terrain from the patch (nearest cell)
    q = sec.at(np.full_like(us, s), us, 0.)
    ix = np.clip(np.round((q[:, 0] - patch['x0']) / geo.FINE).astype(int), 0, patch['T'].shape[1] - 1)
    iz = np.clip(np.round((q[:, 2] - patch['z0']) / geo.FINE).astype(int), 0, patch['T'].shape[0] - 1)
    T = patch['T'][iz, ix]; C = patch['ceil'][iz, ix]
    fs = sec.face_grid()[3]; fu = sec.face_grid()[2]; fsS = sec.face_grid()[1]
    col = int(np.argmin(np.abs(fsS[:, 0] - s)))
    ag = sec.apron_grid(); acol = int(np.argmin(np.abs(ag[1][:, 0] - s)))
    lo, hi = y0 - 14, y0 + max(18, float(sec.H.max()) + 4)
    X = lambda u: (u + 26) / 56 * W
    Y = lambda y: H - (y - lo) / (hi - lo) * H
    def line(us_, ys, color, width=2):
        pts = [(X(a), Y(b)) for a, b in zip(us_, ys) if np.isfinite(b)]
        if len(pts) > 1: d.line(pts, fill=color, width=width)
    line(us, nat, (150, 150, 150), 2)
    line(us, np.where(np.isfinite(C), C, np.nan), (220, 120, 60), 1)
    line(us, T, (60, 120, 60), 2)
    line(fu[col], fs[col], (40, 40, 40), 3)
    line(ag[2][acol], ag[3][acol], (40, 40, 110), 3)
    w = float(np.interp(s, sec.s, sec.w)); d.line([(X(-w / 2), Y(y0)), (X(w / 2), Y(y0))], fill=(200, 30, 30), width=5)
    d.text((10, 10), f'{sec.id} s={s:.1f}  grey=natural green=designed orange=ceiling black=band blue=apron red=path', fill=(0, 0, 0))
    img.save(path)


def plan_view(sec, patch, path):
    delta = patch['T'] - patch['nat']
    v = np.clip(delta / 12, -1, 1)
    rgb = np.zeros(delta.shape + (3,), np.uint8)
    rgb[..., 0] = (128 + 127 * np.maximum(v, 0) - 60 * np.maximum(-v, 0)).astype(np.uint8)
    rgb[..., 1] = (128 - 70 * np.abs(v)).astype(np.uint8)
    rgb[..., 2] = (128 + 127 * np.maximum(-v, 0) - 60 * np.maximum(v, 0)).astype(np.uint8)
    strict = np.isfinite(patch['ceil'])
    rgb[strict] = (rgb[strict] * .6 + np.array([255, 200, 0]) * .4).astype(np.uint8)
    img = Image.fromarray(np.flipud(rgb)).resize((delta.shape[1] * 3, delta.shape[0] * 3), Image.NEAREST)
    d = ImageDraw.Draw(img)
    for k in range(0, sec.N, 10):
        x, z = sec.xz[k]
        px = (x - patch['x0']) * 3; pz = (delta.shape[0] - 1 - (z - patch['z0'])) * 3
        d.point((px, pz), fill=(0, 0, 0))
    d.text((6, 6), sec.id + ' red=raised blue=carved yellow=ceiling(exposed skin) black=route', fill=(0, 0, 0))
    img.save(path)


def build(mid, h, layout, running):
    choice = geo.select_sections(h, layout, mid)
    m = geo.mountain(layout, mid)
    geo.VARIANTS.mkdir(parents=True, exist_ok=True)
    report = dict(choice)
    report['built'] = []
    # later sections are built on the ground left by earlier ones (mountains never overlap). Explicit repair
    # windows (walkability fixes outside the selected windows) take the next indices so existing ids keep their seeds.
    repairs = [tuple(r) for r in geo.SPEC[mid].get('repairs', [])]
    report['repairs'] = [list(r) for r in repairs]
    for index, (kind, s0, s1) in enumerate(list(choice['sections']) + repairs):
        sec = geo.Section(running, m, kind, s0, s1, index, choice['realm'], geo.SPEC[mid]['seed'])
        out = geo.VARIANTS / sec.id; out.mkdir(parents=True, exist_ok=True)
        face, _, _, _ = sec.face_grid()
        apron, _, _, _ = sec.apron_grid()
        parts = [(0., sec.bridge[0]), (sec.bridge[1], sec.length)] if sec.bridge else [(0., sec.length)]
        trails = {}
        for p, (a, b) in enumerate(parts):
            trails['trail_%d' % p] = sec.trail_grid(a, b)[0]
            trails['collision_%d' % p] = sec.trail_grid(a, b, collision=True)[0]
        np.savez_compressed(out / 'grids.npz', face=face, apron=apron, **trails)
        patch = sec.terrain_patch()
        np.savez_compressed(out / 'terrain.npz', T=patch['T'], nat=patch['nat'], ceil=patch['ceil'], x0=patch['x0'], z0=patch['z0'],
                            we=patch['we'])
        fit = geo.fit_coarse(running, patch, sec)
        running[fit['iz0']:fit['iz0'] + fit['coarse'].shape[0], fit['ix0']:fit['ix0'] + fit['coarse'].shape[1]] = fit['coarse']
        np.savez_compressed(out / 'coarse.npz', **{k: fit[k] for k in ('ix0', 'iz0', 'coarse', 'base', 'rock', 'walk')})
        # MainPath replacement: ~0.9m spacing on the walking surface (treads keep their stepped heights)
        keep = np.r_[np.arange(0, sec.N, 5), sec.N - 1]
        mainpath = sec.at(sec.s[keep], 0., sec.y[keep])
        if sec.reversed:
            mainpath = mainpath[::-1]  # back to main-path order for the splice
        stones = sec.stones(STONE_VARIANTS)
        risers = [t['jump'] for t in sec.terraces]; treads = [t['b'] - t['a'] for t in sec.terraces]
        stats = dict(id=sec.id, kind=kind, s0=s0, s1=s1, length=sec.length, rise=sec.R, upSign=sec.up_sign,
                     minRadius=sec.min_radius, cliff=[float(sec.H.min()), float(sec.H.max())], apron=[float(sec.D.min()), float(sec.D.max())],
                     width=[float(sec.w.min()), float(sec.w.max())], steps=len(sec.terraces),
                     riser=[min(risers), max(risers)] if risers else None, tread=[min(treads), max(treads)] if treads else None,
                     stepGrade=max(((t['yb'] - t['ya']) / (t['b'] - t['a']) for t in sec.terraces), default=0.),
                     bridge=list(sec.bridge) if sec.bridge else None, stones=dict(step=sum(1 for s in stones if s['kind'] == 'step'),
                                                                                pave=sum(1 for s in stones if s['kind'] == 'pave')),
                     raised=float((patch['T'] - patch['nat']).max()), carved=float((patch['T'] - patch['nat']).min()),
                     grid=dict(constraints=fit['constraints'], lowered=fit['lowered'], residual=fit['residual'],
                               changed=int((np.abs(fit['coarse'] - fit['base']) > 1e-6).sum())))
        section = dict(profile=sec.profile_json(), terraces=sec.terraces, stones=stones, stats=stats, realm=choice['realm'],
                       mountain=mid, parts=[list(p) for p in parts], kind=kind, s0=s0, s1=s1,
                       mainPath=[dict(x=float(a), y=float(b), z=float(c)) for a, b, c in mainpath])
        (out / 'section.json').write_text(json.dumps(section), encoding='utf8')
        # Unity JsonUtility inputs (flat objects, Vector3 as {x,y,z})
        (out / 'profile.json').write_text(json.dumps(sec.profile_json()), encoding='utf8')
        v3 = lambda q: dict(x=float(q[0]), y=float(q[1]), z=float(q[2]))
        stone_set = sec.style.get('stones', 'natural')
        (out / 'stones.json').write_text(json.dumps(dict(set=stone_set, stones=[dict(kind=t['kind'], v=t['v'], p=v3(t['p']), size=v3(t['size']), yaw=t['yaw'],
                                                                                      pitch=t.get('pitch', 0.), tone=t.get('tone', 0)) for t in stones])), encoding='utf8')
        (out / 'meta.json').write_text(json.dumps(dict(id=sec.id, kind=kind, realm=choice['realm'], mountain=mid, upSign=int(sec.up_sign),
                                                       s0=s0, s1=s1, length=sec.length, stones=stone_set,
                                                       walkway=bool(sec.bridge), span=float(getattr(sec, 'span', 0.)), reversed=bool(sec.reversed))), encoding='utf8')
        plan_view(sec, patch, out / 'debug-plan.png')
        for k, frac in enumerate([.2, .5, .8]):
            cross_section(sec, patch, frac * sec.length, out / ('debug-cross-%d.png' % k))
        report['built'].append(stats)
        print(json.dumps(stats))
    n = len(choice['sections'])
    report['ids'] = [b['id'] for b in report['built'][:n]]
    report['repair_ids'] = [b['id'] for b in report['built'][n:]]
    return report


def main(*mids):
    h, layout = geo.load_base()
    path = geo.VARIANTS / 'sections.json'
    old = json.loads(path.read_text(encoding='utf8')) if path.exists() else {}
    known = old.get('mountains', [old] if 'mountain' in old else [])  # Step 1 wrote one mountain record
    wanted = list(mids) or list(geo.SPEC)
    running = h.copy()
    fresh = {mid: build(mid, h, layout, running) for mid in wanted}
    records = [fresh.get(r['mountain'], r) for r in known if r['mountain'] in geo.SPEC] + [r for mid, r in fresh.items() if mid not in {k['mountain'] for k in known}]
    records.sort(key=lambda r: list(geo.SPEC).index(r['mountain']))
    # repairs are appended after every selected section so the Unity per-index seeds of existing sections stay put
    ids = [i for r in records for i in r['ids']] + [i for r in records for i in r.get('repair_ids', [])]
    path.write_text(json.dumps(dict(mountains=records, ids=ids), indent=1), encoding='utf8')
    print('sections:', len(ids), 'on', len(records), 'mountains')


if __name__ == '__main__':
    main(*sys.argv[1:])
