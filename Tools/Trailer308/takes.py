"""Trailer 4 (2026-10-11): take specs for Presentation297 film:<spec.json>, from Tools/Trailer308/sites308.json.

  python Tools/Trailer308/takes.py                      every take -> Art/Presentation297/Films/specs/<take>.json + Tools/Trailer308/manifest.json
  python Tools/Trailer308/takes.py --check              the same, then the TBD list; exit 2 when a used take has a TBD value
  python Tools/Trailer308/takes.py --round play         only that round's takes (the first one gets warmup 30 - fresh editor)
  python Tools/Trailer308/takes.py --take p_hook --letter 가          one take again with another letter (p_fight*: --letter2)
  python Tools/Trailer308/takes.py --take w_giant --giant-head 19.8   the giant takes with the head height the `guardian:` reply reported
  python Tools/Trailer308/takes.py --take p_burst_giant --giant-site alt   the giant on the capital plaza (the dragon chases)
  python Tools/Trailer308/takes.py --plan p_hook        shell lines for shoot.sh: SPEC / LIMIT / KIND / PRE / POST / MUM / GIANT
  python Tools/Trailer308/takes.py --list               the take table
  --allow-tbd   keep takes with TBD values in the rounds (their fallbacks are printed)
  --letter / --letter2 / --giant-head / --giant-scale / --giant-site are REMEMBERED in manifest.json (entry "overrides"): a later
  regeneration of the same take (shoot.sh fix:, --round, a --giant-head update) keeps them unless the option is given again.
  --check also holds every take against the edit tables (timeline.py TL30 / TL90): a take must be >= 1.5 x the longest cut that
  uses it and must cover the cut's end (src + dur x speed); a violation is listed and exits 2 like a TBD.

Spec fields (Presentation297.FilmSpec; glyph / hud / settle / hitLog are the #308 trailer additions - unknown to older code = ignored):
  followMain + duration + inputs   first person, real Input System events (p_*)      keys[] (time, eye, target, fov)   camera path (w_*)
  inputs: {"type":"glyph","key":"나","time":t,"duration":1.4,"points":[cx,cy,size]}   viewport 0-1, y up; the C# side lays the jamo out
          key (Tab / W / A / D / LeftShift / Space ...), aim (world x,y,z[,eyeHeight]), look (mouse delta x,y over duration), stroke, q
  every time and duration sits on the 1/30 s grid.
Player placement is NOT in the spec (Session.Teleport has no ground snap): the manifest carries a PRE command
  Map307Capture|hold-teleport:x,z,yaw   (top surface + NavMesh within 30 m)   or  hold-teleport:x,y,z,yaw  (exact; cliffway, river bank,
  and every enemy site with "exact": true - the mine gallery lies UNDER the terrain, the snap raycast from y 2000 would put the player on the hill)
Pack enemies: stand_m is the distance to the NEAREST member (the feet move away from the pack centre until every member is >= stand_m).
"""
import argparse
import json
import math
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
SITES_FILE = HERE / 'sites308.json'
MANIFEST = HERE / 'manifest.json'
FILMS = ROOT / 'Art/Presentation297/Films'
SPECS = FILMS / 'specs'
LAYOUT = ROOT / 'Oheangbu/Assets/_Project/Scenes/World/Main/WorldLayout_Main.asset'
FPS = 30
P = 'Presentation297'

ROUNDS = {
    'play': ['p_hook', 'p_hook_b', 'p_fight', 'p_burst_1', 'p_burst_2', 'p_burst_3', 'p_burst_4', 'p_walk'],
    'mum': ['p_mum', 'p_mum_long', 'p_burst_giant'],
    'world': ['w_reveal', 'w_still', 'w_giant', 'w_capital', 'p_village', 'p_jeokro', 'p_cheolong', 'p_cliffway'],
    'car': ['p_fight2', 'p_car'],  # probe는 R5 fix:p_car_probe로만(한 Play에 탑승 1회)
}
# take -> (seconds, kind). kind: hook | fight | burst | giant | mum | walk | car | world
# Lengths follow the edit tables (timeline.py): >= 1.5 x the longest cut that uses the take, and the cut's end must fit
# (TL90: p_fight / p_fight2 7 s cuts -> 11 s takes; p_burst_1 4 s cut from src .3 -> 6 s; burst_hold 3 s from p_burst_giant -> 6 s;
#  p_cliffway / w_still 4 s -> 7 s; w_giant 5 s -> 8 s; the walks 3 s -> 6 s; p_car 6 s -> 10 s). --check enforces it.
TABLE = {
    'p_hook': (8.0, 'hook'), 'p_hook_b': (8.0, 'hook'), 'p_fight': (11.0, 'fight'),
    'p_burst_1': (6.0, 'burst'), 'p_burst_2': (6.0, 'burst'), 'p_burst_3': (6.0, 'burst'), 'p_burst_4': (6.0, 'burst'),
    'p_burst_giant': (6.0, 'giant'), 'p_mum': (6.0, 'mum'), 'p_mum_long': (14.0, 'mum'),
    'p_walk': (4.0, 'world'), 'p_village': (6.0, 'walk'), 'p_jeokro': (6.0, 'walk'), 'p_cheolong': (6.0, 'walk'), 'p_cliffway': (7.0, 'world'),   # 잔도 둘은 길을 따라가는 카메라(어깨 카메라가 암벽 안에 들어갔다)
    'p_fight2': (11.0, 'fight'), 'p_car_probe': (6.0, 'car'), 'p_car': (10.0, 'car'),
    'w_reveal': (6.0, 'world'), 'w_still': (7.0, 'world'), 'w_giant': (8.0, 'world'), 'w_capital': (6.0, 'world'),
}
EDIT_MARGIN = 1.5     # a take is at least this many times the longest cut that uses it
OVERRIDE_KEYS = ('letter', 'letter2', 'giant_head', 'giant_scale', 'giant_site')

TBD = []      # (take, field, fallback)
NOTES = {}    # take -> [str]


def g(t):
    """snap to the 1/30 s grid"""
    return round(round(float(t) * FPS) / FPS, 4)


def note(take, text):
    NOTES.setdefault(take, []).append(text)


def tbd(take, field, fallback):
    TBD.append((take, field, fallback))
    return fallback


def is_tbd(v):
    return isinstance(v, str) and v.upper() == 'TBD'


# ---------------------------------------------------------------- geometry

def yaw_of(dx, dz):
    return math.degrees(math.atan2(dx, dz)) % 360.0


def wrap(a):
    return (a + 180.0) % 360.0 - 180.0


def unit_xz(a, b):
    dx, dz = b[0] - a[0], b[1] - a[1]
    d = math.hypot(dx, dz)
    return (dx / d, dz / d) if d > 1e-6 else (0.0, 1.0)


def centre(points):
    n = float(len(points))
    return [sum(p[i] for p in points) / n for i in range(3)]


# ---------------------------------------------------------------- the layout asset (Places, Routes)

_layout = None


def layout():
    """{'places': {id: (x, z)}, 'routes': {id: {'from','to','bends': [(x, z), ...]}}} from the Unity YAML (regex, no yaml module)"""
    global _layout
    if _layout is not None: return _layout
    _layout = {'places': {}, 'routes': {}}
    if not LAYOUT.exists(): return _layout
    text = LAYOUT.read_text(encoding='utf-8', errors='replace')

    def block(name):
        m = re.search(r'^  %s:\n(.*?)(?=^  [A-Za-z]+:)' % name, text, re.S | re.M)
        return m.group(1) if m else ''
    for entry in re.split(r'^  - Id: ', block('Places'), flags=re.M)[1:]:
        pid = entry.split('\n', 1)[0].strip()
        m = re.search(r'^    XZ: \{x: ([-\d.]+), y: ([-\d.]+)\}', entry, re.M)
        if m: _layout['places'][pid] = (float(m.group(1)), float(m.group(2)))
    for entry in re.split(r'^  - Id: ', block('Routes'), flags=re.M)[1:]:
        rid = entry.split('\n', 1)[0].strip()
        fr = re.search(r'^    From: (\S*)', entry, re.M); to = re.search(r'^    To: (\S*)', entry, re.M)
        bends = [(float(a), float(b)) for a, b in re.findall(r'^    - \{x: ([-\d.]+), y: ([-\d.]+)\}', entry, re.M)]
        _layout['routes'][rid] = {'from': fr.group(1) if fr else '', 'to': to.group(1) if to else '', 'bends': bends}
    return _layout


def nearest_route_point(xz, min_d=0.0, max_d=80.0):
    """(route id, bend index, distance) of the closest route bend to xz within [min_d, max_d], or None"""
    best = None
    for rid, r in layout()['routes'].items():
        for i, b in enumerate(r['bends']):
            d = math.hypot(b[0] - xz[0], b[1] - xz[1])
            if min_d <= d <= max_d and (best is None or d < best[2]): best = (rid, i, d)
    return best


def route_heading(rid, i):
    """yaw along the route at bend i (toward the next bend; the last bend uses the previous one)"""
    b = layout()['routes'][rid]['bends']
    if len(b) < 2: return 0.0
    a, c = (b[i], b[i + 1]) if i + 1 < len(b) else (b[i - 1], b[i])
    return yaw_of(c[0] - a[0], c[1] - a[1])


# ---------------------------------------------------------------- input helpers

def glyph(letter, t, dur=1.4, cx=.5, cy=.5, size=.36):
    return {'type': 'glyph', 'key': letter, 'time': g(t), 'duration': g(dur), 'points': [cx, cy, size]}


def key(name, t, dur):
    return {'type': 'key', 'key': name, 'time': g(t), 'duration': g(dur)}


def aim(pos, t, dur, eye=1.85):
    return {'type': 'aim', 'time': g(t), 'duration': g(dur), 'points': [round(pos[0], 3), round(pos[1], 3), round(pos[2], 3), eye]}


def look(dx, dy, t, dur):
    """total mouse delta over the window (0.1214 deg per unit; y < 0 = look down)"""
    return {'type': 'look', 'time': g(t), 'duration': g(dur), 'points': [dx, dy]}


def stroke(points, t, dur):
    """left button held while the pointer runs the viewport polyline [x0,y0,x1,y1,...] (y up)"""
    return {'type': 'stroke', 'time': g(t), 'duration': g(dur), 'points': list(points)}


def q(t, dur):
    return {'type': 'q', 'time': g(t), 'duration': g(dur)}


def chest(pos):
    return [pos[0], pos[1] + 1.0, pos[2]]


# ---------------------------------------------------------------- timelines (every time on the 1/30 grid)

def hook_timeline(enemy_pos, letter='나', approach=2.0, duration=8.0, draw_dur=1.4, size=.36):
    """walk up (the enemy is parked outside its detection ring), lock on, write, the shot leaves, the hit, a side dodge"""
    tab = approach + 0.4
    draw = tab + 0.4
    dodge = draw + draw_dur + 1.6
    ev = [aim(chest(enemy_pos), 0.0, approach + 0.3), key('W', 0.1, approach), key('Tab', tab, 0.1), glyph(letter, draw, draw_dur, size=size)]
    if dodge + 0.3 < duration: ev += [key('A', dodge, 0.3), key('LeftShift', dodge + 0.05, 0.15)]
    return ev


def fight_timeline(centre_pos, letters=('가', '사'), approach=1.2, duration=6.0, draw_dur=1.3, size=.36):
    """lock, first letter, side dodge, re-centre + lock again, second letter (Tab is the LockOn key: pressed again after the first
    target dropped it picks the next candidate; if it toggles off instead the second letter still fires at the aimed point)"""
    tab1 = approach + 0.1
    d1 = tab1 + 0.3
    dodge = d1 + draw_dur + 0.6
    re_aim = dodge + 0.4
    tab2 = re_aim + 0.4
    d2 = tab2 + 0.2
    ev = [aim(chest(centre_pos), 0.0, approach), key('W', 0.1, approach), key('Tab', tab1, 0.1), glyph(letters[0], d1, draw_dur, size=size),
          key('A', dodge, 0.3), key('LeftShift', dodge + 0.05, 0.15), aim(chest(centre_pos), re_aim, 0.4), key('Tab', tab2, 0.1)]
    if d2 + draw_dur <= duration: ev.append(glyph(letters[1], d2, draw_dur, size=size))
    return ev


def burst_timeline(enemy_pos, letter, approach=1.0, duration=4.0, draw_dur=1.3, size=.38):
    """short: aim, a step in, lock, the letter large in the frame, the shot, a dodge at the end"""
    tab = approach - 0.2
    draw = tab + 0.3
    dodge = duration - 0.7
    ev = [aim(chest(enemy_pos), 0.0, approach), key('W', 0.0, approach), key('Tab', tab, 0.1), glyph(letter, draw, draw_dur, size=size)]
    if dodge > draw + draw_dur + 0.3: ev += [key('A', dodge, 0.3), key('LeftShift', dodge + 0.05, 0.15)]
    return ev


def giant_timeline(head_pos, duration=4.0):
    """look up the giant, lock on its chest target, raise the brush (Q held, no stroke) - the cut ends on the raise"""
    return [aim(head_pos, 0.0, 1.0), key('Tab', 1.2, 0.1), q(2.4, duration - 2.4)]


def mum_timeline(river_target, duration=6.0, long=False, size=.40):
    """look at the water, 뭄 (initial, medial, final - the C# side lays the three out), the stones rise, the first step(s)"""
    walk_from = 4.0 if long else 4.2
    return [aim(river_target, 0.0, 0.5, 1.85), glyph('뭄', 0.8, 1.6, cy=.5, size=size), key('W', walk_from, duration - walk_from - 0.2)]


def walk_timeline(duration=4.0, pan=40):
    return [key('W', 0.2, duration - 0.4), look(pan, 0, 1.0, 2.0)]


def car_schedule(seat, yaw0, polyline, duration, speed=8.0, wheelbase=2.6, steer_max=20.0, steer_rate=80.0, lookahead=10.0, dead=6.0, throttle_from=0.5, ramp=2.0):
    """A/D intervals from a kinematic bicycle driven along the polyline: a key is held while the heading error is past the dead
    band and released once it is inside a third of it (hysteresis); steering moves at steer_rate toward full lock / zero; speed
    ramps up over `ramp` s. Convention assumed: D = turn right = yaw increases (clockwise from above). Returns ([(key, t0, t1)], path)."""
    x, z, yaw, steer = seat[0], seat[2], float(yaw0), 0.0
    dt = 1.0 / FPS
    frames = []
    path = []
    want = ''
    for i in range(int(round(duration * FPS))):
        t = i * dt
        # closest bend, then the bend `lookahead` metres further along
        j = min(range(len(polyline)), key=lambda k: math.hypot(polyline[k][0] - x, polyline[k][1] - z))
        acc, k = 0.0, j
        while k + 1 < len(polyline) and acc < lookahead:
            acc += math.hypot(polyline[k + 1][0] - polyline[k][0], polyline[k + 1][1] - polyline[k][1]); k += 1
        tx, tz = polyline[k]
        err = wrap(yaw_of(tx - x, tz - z) - yaw)
        if want == '' and abs(err) > dead: want = 'D' if err > 0 else 'A'
        elif want == 'D' and err < dead / 3.0: want = ''
        elif want == 'A' and err > -dead / 3.0: want = ''
        target = steer_max * (1 if want == 'D' else -1 if want == 'A' else 0)
        step = steer_rate * dt
        steer = target if abs(target - steer) <= step else steer + math.copysign(step, target - steer)
        if t >= throttle_from:
            v = speed * min(1.0, (t - throttle_from) / max(1e-3, ramp))
            yaw = (yaw + math.degrees(v / wheelbase * math.tan(math.radians(steer))) * dt) % 360.0
            x += math.sin(math.radians(yaw)) * v * dt; z += math.cos(math.radians(yaw)) * v * dt
        frames.append(want); path.append((round(x, 1), round(z, 1), round(yaw, 1)))
    out, cur, t0 = [], '', 0.0
    for i, k in enumerate(frames + ['']):
        if k != cur:
            if cur: out.append((cur, g(t0), g(i * dt)))
            cur, t0 = k, i * dt
    return out, path


def car_timeline(duration, schedule, throttle_from=0.5):
    """W from throttle_from to the end; A/D from the schedule, never before the throttle (frame 0 is the boarding frame:
    a key held then would move the walker beside the seat, not the car)"""
    ev = [key('W', throttle_from, duration - throttle_from)]
    for k, t0, t1 in schedule:
        t0 = max(t0, throttle_from)
        if t1 - t0 >= 2.0 / FPS: ev.append(key(k, t0, t1 - t0))
    return ev


# ---------------------------------------------------------------- specs

def spec_first_person(name, duration, inputs, feet=None, yaw=0.0, hud='reticle,ink', settle=20, warmup=12, board=False, mum=False):
    s = {'name': name, 'fps': FPS, 'width': 1920, 'height': 1080, 'warmup': warmup, 'fov': 55, 'hidePlayer': False,
         'followMain': True, 'duration': g(duration), 'inputs': inputs, 'hud': hud, 'settle': settle, 'hitLog': True}
    if feet is not None: s['playerFeet'] = [round(v, 3) for v in feet]; s['playerYaw'] = round(float(yaw), 2)
    if board: s['board'] = True
    # mumUnlock은 격리 저장(_mum295_test) 전용이라 시연 스위치(DemoAllFinals) 세션에서는 예외를 던진다(01:12 'Mum test unlock requires …') → 넣지 않는다
    if mum: s['mumUnlock'] = False
    return s


def spec_world(name, keys, fov=50, settle=25, warmup=12, hud='none'):  # 'none' = 별칭 불일치 → HUD·월드 프롬프트 전부 숨김
    ks = [{'time': g(k[0]), 'eye': [round(v, 3) for v in k[1]], 'target': [round(v, 3) for v in k[2]], 'fov': float(k[3] if len(k) > 3 and k[3] else fov)} for k in keys]
    return {'name': name, 'fps': FPS, 'width': 1920, 'height': 1080, 'warmup': warmup, 'fov': float(fov), 'hidePlayer': True,
            'keys': ks, 'hud': hud, 'settle': settle, 'hitLog': False, 'showBody': False}


# ---------------------------------------------------------------- the cliffway (layout.json path with y)

_cliff = None


def cliff_path(site):
    global _cliff
    if _cliff is None:
        f = ROOT / site['layout']
        _cliff = json.loads(f.read_text(encoding='utf-8'))['path'] if f.exists() else []
    return _cliff


def cliff_point(site, station):
    """[x, y, z] and heading at arc length `station` along the cliff path (clamped)."""
    path = cliff_path(site)
    s0 = [0.0]
    for a, b in zip(path, path[1:]): s0.append(s0[-1] + math.dist(a, b))
    station = max(0.0, min(station, s0[-1] - 0.01))
    for i in range(len(path) - 1):
        if s0[i] <= station <= s0[i + 1]:
            u = 0.0 if s0[i + 1] == s0[i] else (station - s0[i]) / (s0[i + 1] - s0[i])
            a, b = path[i], path[i + 1]
            return [a[k] + (b[k] - a[k]) * u for k in range(3)], yaw_of(b[0] - a[0], b[2] - a[2])
    return list(path[-1]), 0.0


def cliff_window(site, which, walk_m=14.0, margin=10.0):
    """(feet, yaw, station) of the straightest walk_m stretch, away from the rail gaps; 'auto2' = the next best one at least 60 m away"""
    path = cliff_path(site)
    if len(path) < 10: return None
    s = [0.0]
    for a, b in zip(path, path[1:]): s.append(s[-1] + math.dist(a, b))
    heads = [yaw_of(b[0] - a[0], b[2] - a[2]) for a, b in zip(path, path[1:])] + [0.0]
    gaps = site.get('rail_gaps', [])
    scored = []
    for i in range(len(path)):
        if s[i] < 20 or s[i] + walk_m > s[-1] - 20: continue
        if any(a - margin < s[i] + u < b + margin for a, b in gaps for u in (0.0, walk_m)): continue
        j = i
        while j + 1 < len(path) and s[j] - s[i] < walk_m: j += 1
        bend = sum(abs(wrap(heads[k + 1] - heads[k])) for k in range(i, j))
        scored.append((bend, i))
    scored.sort()
    if not scored: return None
    first = scored[0][1]
    pick = first if which != 'auto2' else next((i for _, i in scored if abs(s[i] - s[first]) >= 60.0), first)
    p = path[pick]
    return ([p[0], p[1] + 0.1, p[2]], round(heads[pick], 1), round(s[pick], 1))


# ---------------------------------------------------------------- build

class Build:
    def __init__(self, sites, giant_head=None, giant_site='main', giant_scale=None, letters=None, allow_tbd=False):
        self.S = sites['sites']; self.T = sites['takes']
        self.giant_head, self.giant_site, self.giant_scale = giant_head, giant_site, giant_scale
        self.letters = letters or {}
        self.allow_tbd = allow_tbd
        self.manifest = {}

    # -- placement -----------------------------------------------------------------------------------------------
    def site(self, name):
        return self.S[name]

    def pos(self, take, site_name, field='pos'):
        v = self.site(site_name).get(field)
        if v is None or is_tbd(v): return tbd(take, '%s.%s' % (site_name, field), [0.0, 0.0, 0.0])
        return list(v)

    def enemy_centre(self, site):
        return centre(site['pos']) if isinstance(site['pos'][0], list) else list(site['pos'])

    def stand(self, take, cfg):
        """player feet + yaw + pack centre + exact flag: stand_m from the NEAREST enemy of the site, on the side the player comes
        from (the feet slide away from the pack centre along that side until every member is >= stand_m away)"""
        enemy = self.site(cfg['enemy']); c = self.enemy_centre(enemy)
        members = enemy['pos'] if isinstance(enemy['pos'][0], list) else [enemy['pos']]
        d = float(cfg.get('stand_m', 19))
        toward = cfg.get('toward', 'route')
        if toward == 'route':
            # the side the player comes from = the nearest road (field packs sit 60-95 m off a road: jackals 94 m, charcoal bandit 92 m)
            hit = nearest_route_point((c[0], c[2]), min_d=d, max_d=150.0)
            if hit:
                rid, i, dist = hit; b = layout()['routes'][rid]['bends'][i]
                ux, uz = unit_xz((c[0], c[2]), b); note(take, 'approach from route %s bend %d (%.0f m)' % (rid, i, dist))
            else:
                ux, uz = 0.0, -1.0; tbd(take, 'approach side of %s (no route within 150 m)' % cfg['enemy'], 'south')
        else:
            p = self.pos(take, toward); ux, uz = unit_xz((c[0], c[2]), (p[0], p[2]))

        def nearest(t):
            return min(math.hypot(c[0] + ux * t - m[0], c[2] + uz * t - m[2]) for m in members)
        t = d
        while nearest(t) < d - 1e-6 and t < d + 40.0: t += 0.25      # a member on the player's side of the centre pushes the feet out
        feet = [c[0] + ux * t, c[1], c[2] + uz * t]
        yaw = yaw_of(-ux, -uz)
        near = nearest(t)
        if len(members) > 1: note(take, 'pack of %d: nearest member %.1f m (centre %.1f m) at stand_m %.0f' % (len(members), near, t, d))
        det = float(enemy.get('detection') or 0)
        if det and near <= det: note(take, 'WARNING nearest enemy %.1f m is inside detection %.0f m: it charges during settle' % (near, det))
        elif det and near - det < 2.0: note(take, 'WARNING nearest enemy %.1f m is only %.1f m outside detection %.0f m (NavMesh snap may close it)' % (near, near - det, det))
        return feet, yaw, c, bool(enemy.get('exact'))

    def giant_cfg(self):
        gs = self.site('giant')
        alt = self.giant_site == 'alt'
        feet = list(gs['alt_pos'] if alt else gs['pos']); yaw = gs['alt_yaw'] if alt else gs['yaw']
        scale = float(self.giant_scale or gs['scale'])
        head = float(self.giant_head) if self.giant_head else gs['head_per_scale'] * scale
        cmd = '%s|guardian:%s:%s:%s:%s%s' % (P, ','.join(str(v) for v in feet), yaw, scale, gs.get('pose', 'Idle@0'), ':target' if gs.get('target') else '')
        return feet, yaw, scale, head, cmd

    @staticmethod
    def teleport(feet, yaw, exact=False):
        if exact: return '%s|hold-teleport:%.2f,%.2f,%.2f,%.1f' % ('Map307Capture', feet[0], feet[1], feet[2], yaw)
        return '%s|hold-teleport:%.2f,%.2f,%.1f' % ('Map307Capture', feet[0], feet[2], yaw)

    # -- one take ------------------------------------------------------------------------------------------------
    def build(self, name, warmup=12):
        duration, kind = TABLE[name]
        cfg = self.T.get(name, {})
        pre, post, verify, feet, yaw, exact = [], [], {}, None, 0.0, False
        letter = self.letters.get(name) or cfg.get('letter')
        letters = list(cfg.get('letters', []))
        if name in self.letters: letters = [self.letters[name]] + letters[1:]
        if name + '#2' in self.letters and letters: letters = letters[:1] + [self.letters[name + '#2']]
        mum = board = False
        if kind == 'hook':
            feet, yaw, c, exact = self.stand(name, cfg)
            inputs = hook_timeline(c, letter, cfg.get('approach_s', 2.0), duration)
            verify = {'glyphs': [letter], 'hits': True}
        elif kind == 'fight':
            feet, yaw, c, exact = self.stand(name, cfg)
            inputs = fight_timeline(c, letters, cfg.get('approach_s', 1.2), duration)
            verify = {'glyphs': letters[:2], 'hits': True}
        elif kind == 'burst':
            feet, yaw, c, exact = self.stand(name, cfg)
            inputs = burst_timeline(c, letter, cfg.get('approach_s', 1.0), duration)
            verify = {'glyphs': [letter], 'hits': True}
        elif kind == 'giant':
            gfeet, gyaw, scale, head, cmd = self.giant_cfg()
            d = float(cfg.get('stand_m', 14))
            feet = [gfeet[0], gfeet[1], gfeet[2] - d]; yaw = 0.0           # south of the giant, looking north at it
            inputs = giant_timeline([gfeet[0], gfeet[1] + head, gfeet[2]], duration)
            pre += [cmd, P + '|guardian-info']; post += [P + '|guardian-clear']
            verify = {'glyphs': [], 'hits': False, 'q': True}
            note(name, 'head height %.1f m (%s); shoot.sh regenerates with --giant-head from the `guardian:` reply ("guardian at (...) head N")' % (head, 'given' if self.giant_head else 'scale x head_per_scale'))
            if self.giant_site != 'alt':
                dr = self.site('cheongryong'); dist = math.dist((feet[0], feet[2]), (dr['pos'][0], dr['pos'][2]))
                note(name, 'cheongryong %.0f m from the player (detection %s): check `actors` first; --giant-site alt moves the giant' % (dist, dr['detection']))
        elif kind == 'mum':
            s = self.site(cfg['site']); feet = list(s['pos']); yaw = s.get('yaw', 0); exact = bool(s.get('exact'))
            inputs = mum_timeline(s['river_target'], duration, long=name.endswith('_long'))
            mum = True
            pre += [P + '|actors', P + '|mum-status']
            verify = {'glyphs': ['뭄'], 'hits': False, 'mum': True}
            if s.get('river_target_y_est'): note(name, 'river_target y is an estimate (aim pitch only)')
        elif kind == 'walk':
            s = self.site(cfg['site'])
            if cfg['site'] == 'cliffway':
                w = cliff_window(s, cfg.get('station', 'auto'))
                if w is None: feet, yaw = list(s['start']), tbd(name, 'cliffway.yaw (layout.json path not readable)', 63.0)
                else: feet, yaw, st = w; note(name, 'cliffway station %.0f m, heading %.0f' % (st, yaw))
                exact = True
            else:
                feet = list(s['pos']); yaw = s.get('yaw', 0)
                if yaw == 'route' or is_tbd(yaw):
                    # walk along the nearest road; when the site point is off the road (> 6 m) the player starts ON the road bend
                    hit = nearest_route_point((feet[0], feet[2]), 0.0, 60.0)
                    if hit:
                        yaw = route_heading(hit[0], hit[1])
                        if hit[2] > 6.0:
                            b = layout()['routes'][hit[0]]['bends'][hit[1]]; feet = [b[0], feet[1], b[1]]
                            note(name, 'moved onto route %s bend %d (%.0f m from the site point)' % (hit[0], hit[1], hit[2]))
                        note(name, 'yaw %.0f along route %s (%.0f m away)' % (yaw, hit[0], hit[2]))
                    else: yaw = tbd(name, '%s.yaw (no route within 60 m)' % cfg['site'], 0.0)
            inputs = walk_timeline(duration)
            verify = {'glyphs': [], 'hits': False}
        elif kind == 'car':
            seat = self.site(cfg['site']); route = self.site(cfg['route'])
            r = layout()['routes'].get(route['route_id'])
            if r and r['bends']:
                bends = list(r['bends'])
                if r['from'] != route.get('drive_from'): bends.reverse()
                sched, path = car_schedule(seat['pos'], seat['yaw'], bends, duration, route.get('speed_mps', 8.0), route.get('wheelbase_m', 2.6),
                                           route.get('steer_max_deg', 20.0), route.get('steer_rate_dps', 80.0), route.get('lookahead_m', 10.0), route.get('dead_deg', 6.0))
                note(name, 'steering from route %s (%d bends, driven from %s): %s; model end (%.0f, %.0f) yaw %.0f' % (
                    route['route_id'], len(bends), route.get('drive_from'), ' '.join('%s %.2f-%.2f' % s for s in sched) or 'straight', path[-1][0], path[-1][1], path[-1][2]))
            else:
                sched = [('A', g(0.5), g(0.5 + 48.0 / 60.0))]      # a plain initial left turn of ~48 deg (steer-rate limited), then straight
                tbd(name, 'car steering (route %s not readable) -> initial left 48 deg + straight' % route['route_id'], 'fallback')
            inputs = car_timeline(duration, sched)
            board = True                                           # StartFilm parks the player beside the seat and boards at frame 0
            verify = {'glyphs': [], 'hits': False, 'board': True}
            note(name, 'board=true: the spec itself seats the player (no teleport); A/D sign convention is an assumption - p_car_probe first')
        elif kind == 'world':
            inputs = None
            keys, feet, yaw, exact = self.world_keys(name, cfg, duration)
            if name in ('w_giant',):
                _, _, _, _, cmd = self.giant_cfg(); pre += [cmd, P + '|guardian-info']; post += [P + '|guardian-clear']
            if name in ('w_still', 'w_giant'): pre += [P + '|actors']   # 청룡(3210,89,3550)이 깨어 있는지 기록만(아침 판정용)
            verify = {'glyphs': [], 'hits': False, 'world': True}
        else:
            raise SystemExit('unknown kind ' + kind)

        if kind == 'world':
            spec = spec_world(name, keys, settle=25, warmup=warmup)
        else:
            spec = spec_first_person(name, duration, inputs, settle=20, warmup=warmup, board=board, mum=mum)
        if feet is not None and not board:
            pre.insert(0, self.teleport(feet, yaw, exact))
        frames = int(round(duration * FPS)) + 1
        limit = max(240, int(90 + spec['settle'] + frames * 0.6))
        SPECS.mkdir(parents=True, exist_ok=True)
        path = SPECS / (name + '.json')
        path.write_text(json.dumps(spec, ensure_ascii=False, indent=1), encoding='utf-8')
        entry = {'kind': 'w' if kind == 'world' else 'p', 'type': kind, 'duration': duration, 'frames': frames, 'spec': str(path),
                 'limit': limit, 'warmup': warmup, 'pre': pre, 'post': post, 'verify': verify,
                 'feet': [round(v, 2) for v in feet] if feet else None, 'yaw': round(float(yaw), 1) if feet else None, 'exact': bool(exact and feet),
                 'notes': NOTES.get(name, []), 'tbd': [f for t, f, _ in TBD if t == name]}
        if kind == 'giant' or name == 'w_giant': entry['giant_site'] = self.giant_site
        self.manifest[name] = entry
        return entry

    # -- camera paths --------------------------------------------------------------------------------------------
    def world_keys(self, name, cfg, duration):
        if name in ('p_walk', 'p_cliffway'):
            # 잔도: 길을 따라 눈높이로 미끄러지는 카메라(1인칭 걷기와 같은 그림). R2의 어깨 카메라는 암벽 안에 들어가 어두운 굴만 찍혔다.
            site = self.site(cfg['site'])
            w = cliff_window(site, cfg.get('station', 'auto'))
            if w is None: raise SystemExit(name + ': cliffway layout.json not readable')
            feet, yaw0, st = w
            travel = min(22.0, 3.2 * duration)
            k = []
            for i, (tt, d) in enumerate(((0.0, 0.0), (duration * 0.5, travel * 0.5), (duration, travel))):
                pnt, head = cliff_point(site, st + d)
                ahead, _ = cliff_point(site, st + d + 10.0)
                k.append((tt, [pnt[0], pnt[1] + 1.65, pnt[2]], [ahead[0], ahead[1] + 1.3, ahead[2]], 58))
            note(name, 'cliff camera: station %.0f m, %.0f m along the path at eye height' % (st, travel))
            return k, feet, yaw0, True
        if name == 'w_reveal':
            # 갱구에서 밖으로 날아오르며 산수를 여는 카메라(R2: 서쪽으로 가서 암벽 안에 들어갔다 → 갱구가 향한 북동으로, 올라가며)
            m = self.pos(name, cfg['from']); tx, tz = cfg['toward_xz']
            ux, uz = unit_xz((m[0], m[2]), (tx, tz))
            def at(d, h): return [m[0] + ux * d, m[1] + h, m[2] + uz * d]
            keys = [(0.0, at(0, 1.8), at(40, 0.5), 52), (2.0, at(8, 2.6), at(70, 0.0), 48), (4.4, at(28, 7.0), at(130, -2.0), 42), (duration, at(46, 12.0), at(180, -4.0), 40)]
            return keys, m, yaw_of(ux, uz), False
        if name == 'w_still':
            y = self.site(cfg['yard']); gate = self.pos(name, cfg['player'])
            cx = (y['x'][0] + y['x'][1]) / 2.0; z0, z1 = y['z']; yy = y['y']
            keys = [(0.0, [cx, yy + 2.6, z0 - 62], [cx - 4, yy + 3.0, z1 - 10], 46), (2.0, [cx, yy + 2.5, z0 - 50], [cx - 4, yy + 3.0, z1 - 10], 46), (duration, [cx, yy + 2.4, z0 - 38], [cx - 4, yy + 3.0, z1 - 10], 46)]
            return keys, gate, 0.0, False
        if name == 'w_giant':
            gfeet, gyaw, scale, head, _ = self.giant_cfg(); gate = self.pos(name, cfg['player'])
            gx, gy, gz = gfeet
            # R2: 안문(z 3490)을 뚫고 지나갔고 끝에 머리가 잘렸다 → 안문 안쪽(gz-34)에서 시작, 끝은 18 m 앞에서 머리 위를 올려다본다
            keys = [(0.0, [gx + 3, gy + 1.1, gz - 34], [gx, gy + head * .40, gz], 46),
                    (2.4, [gx + 2, gy + 1.0, gz - 28], [gx, gy + head * .60, gz], 42),
                    (4.4, [gx - 1, gy + 1.3, gz - 22], [gx, gy + head * .90, gz], 40),
                    (duration, [gx - 2, gy + 1.5, gz - 18], [gx, gy + head * 1.08, gz], 40)]
            note(name, 'low angle rush toward head height %.1f m; shoot.sh regenerates with --giant-head from the `guardian:` reply' % head)
            return keys, gate, 0.0, False
        if name == 'w_capital':
            gp = self.pos(name, cfg['gate']); px, pz = cfg['player_xz']
            x, y, z = gp
            keys = [(0.0, [x, y + 4.0, z - 100], [x, y + 8, z], 45), (3.0, [x, y + 3.6, z - 70], [x, y + 10, z + 10], 42), (duration, [x, y + 3.2, z - 42], [x, y + 12, z + 20], 40)]
            return keys, [px, y, pz], 0.0, False
        raise SystemExit('no camera path for ' + name)


# ---------------------------------------------------------------- CLI

def load_sites():
    return json.loads(SITES_FILE.read_text(encoding='utf-8'))


def load_manifest():
    if MANIFEST.exists():
        try: return json.loads(MANIFEST.read_text(encoding='utf-8'))
        except ValueError: pass
    return {'rounds': ROUNDS, 'takes': {}}


def write_manifest(m):
    m['rounds'] = ROUNDS
    m['generated'] = __import__('time').strftime('%Y-%m-%dT%H:%M:%S')
    MANIFEST.write_text(json.dumps(m, ensure_ascii=False, indent=1), encoding='utf-8')


def plan_lines(entry):
    out = ['SPEC %s' % entry['spec'], 'LIMIT %d' % entry['limit'], 'KIND %s' % entry['kind'], 'TYPE %s' % entry['type'], 'FRAMES %d' % entry['frames']]
    out += ['PRE %s' % c for c in entry['pre']] + ['POST %s' % c for c in entry['post']]
    if entry['verify'].get('mum'): out.append('MUM 1')
    if entry['type'] in ('giant',) or 'guardian' in ' '.join(entry['pre']): out.append('GIANT 1')
    if entry.get('tbd'): out.append('TBD ' + '; '.join(entry['tbd']))
    return out


def edit_check(names):
    """[(take, message)]: every cut of the edit tables (timeline.py TL30 / TL90) that uses a take in `names` must fit it with
    the editing margin - take >= EDIT_MARGIN x cut.dur and take >= cut end (src + dur x speed)"""
    try:
        if str(HERE) not in sys.path: sys.path.insert(0, str(HERE))
        import timeline
    except Exception as e:                      # the edit tables are another module's file; their absence is reported, not fatal
        return [('timeline', 'edit tables not readable (%s)' % e)]
    bad = []
    for length in (30, 90):
        for cut in timeline.timeline(length):
            if cut.kind != 'shot' or not cut.take or cut.take not in TABLE or cut.take not in names: continue
            have = TABLE[cut.take][0]; need = cut.dur * EDIT_MARGIN; end = cut.src + cut.dur * cut.speed
            if have + 1e-6 < need: bad.append((cut.take, 'TL%d cut %s %.1f s needs a take >= %.1f s (x%.1f), take is %.1f s' % (length, cut.name, cut.dur, need, EDIT_MARGIN, have)))
            if have + 1e-6 < end: bad.append((cut.take, 'TL%d cut %s ends at take second %.1f (src %.1f + %.1f x %.1f), take is %.1f s' % (length, cut.name, end, cut.src, cut.dur, cut.speed, have)))
    return bad


def merged_overrides(a, name, prev):
    """the take's remembered overrides (manifest entry "overrides") under this call's explicit options"""
    ov = {k: v for k, v in (prev or {}).items() if k in OVERRIDE_KEYS and v is not None}
    if a.take:                                   # --letter / --letter2 apply to the takes named by --take only
        if a.letter: ov['letter'] = a.letter
        if a.letter2: ov['letter2'] = a.letter2
    if a.giant_head is not None: ov['giant_head'] = a.giant_head
    if a.giant_scale is not None: ov['giant_scale'] = a.giant_scale
    if a.giant_site: ov['giant_site'] = a.giant_site
    return ov


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--check', action='store_true'); ap.add_argument('--list', action='store_true')
    ap.add_argument('--take', default=''); ap.add_argument('--round', default='', choices=['', 'play', 'mum', 'world', 'car'])
    ap.add_argument('--letter', default=''); ap.add_argument('--letter2', default='')
    ap.add_argument('--giant-head', type=float, default=None); ap.add_argument('--giant-scale', type=float, default=None)
    ap.add_argument('--giant-site', default=None, choices=['main', 'alt'], help='default: what the manifest remembers for the take (else main)')
    ap.add_argument('--plan', default=''); ap.add_argument('--allow-tbd', action='store_true')
    ap.add_argument('--forget', action='store_true', help='drop the remembered overrides of the named takes before building')
    a = ap.parse_args()
    if a.list:
        for r, names in ROUNDS.items():
            for n in names: print('%-5s %-14s %5.1f s %-6s %4d f' % (r, n, TABLE[n][0], TABLE[n][1], int(round(TABLE[n][0] * FPS)) + 1))
        return 0
    if a.plan:
        m = load_manifest()
        if a.plan not in m['takes']: print('no manifest entry for ' + a.plan + ' (run takes.py first)'); return 2
        print('\n'.join(plan_lines(m['takes'][a.plan]))); return 0
    sites = load_sites()
    if a.take: names = [t for t in a.take.split(',') if t]
    elif a.round: names = list(ROUNDS[a.round])
    else: names = [n for r in ROUNDS.values() for n in r]
    unknown = [n for n in names if n not in TABLE]
    if unknown: print('unknown take(s): ' + ', '.join(unknown)); return 2
    manifest = load_manifest()
    first = True
    for n in names:
        prev = manifest['takes'].get(n) or {}
        ov = merged_overrides(a, n, {} if a.forget else prev.get('overrides'))
        letters = {}
        if ov.get('letter'): letters[n] = ov['letter']
        if ov.get('letter2'): letters[n + '#2'] = ov['letter2']
        b = Build(sites, ov.get('giant_head'), ov.get('giant_site') or 'main', ov.get('giant_scale'), letters, a.allow_tbd)
        # a fresh editor's first take gets warmup 30; a regeneration of one existing take keeps the warmup it had
        warmup = prev.get('warmup') if (a.take and prev.get('warmup')) else (30 if (first and (a.round or a.take)) else 12)
        e = b.build(n, warmup=warmup)
        first = False
        e['overrides'] = ov
        manifest['takes'][n] = e
        flag = ' TBD' if e['tbd'] else ''
        print('%-14s %-6s %5.1f s %4d f limit %3d s  pre %d post %d%s%s' % (n, e['type'], e['duration'], e['frames'], e['limit'], len(e['pre']), len(e['post']), flag,
                                                                       '  overrides ' + json.dumps(ov, ensure_ascii=False) if ov else ''))
        for line in e['notes']: print('    ' + line)
    used_tbd = [(t, f, fb) for t, f, fb in TBD if t in names]
    excluded = sorted({t for t, _, _ in used_tbd})
    manifest['excluded'] = [] if a.allow_tbd else excluded
    write_manifest(manifest)
    print('specs -> %s (%d), manifest -> %s' % (SPECS, len(names), MANIFEST))
    bad_edit = []
    if a.check or used_tbd:
        if used_tbd:
            print('TBD (%d):' % len(used_tbd))
            for t, f, fb in used_tbd: print('  %-14s %s  (fallback %s)' % (t, f, fb))
            print('excluded from the rounds: %s' % (', '.join(excluded) if not a.allow_tbd else 'none (--allow-tbd)'))
        else:
            print('TBD: none')
    if a.check:
        bad_edit = edit_check(set(names))
        if bad_edit:
            print('EDIT TABLE (%d): take shorter than the cuts that use it (x%.1f margin)' % (len(bad_edit), EDIT_MARGIN))
            for t, msg in bad_edit: print('  %-14s %s' % (t, msg))
        else:
            print('edit tables: every take >= x%.1f its longest cut and covers the cut end' % EDIT_MARGIN)
    return 2 if (a.check and ((used_tbd and not a.allow_tbd) or bad_edit)) else 0


if __name__ == '__main__':
    sys.exit(main())
