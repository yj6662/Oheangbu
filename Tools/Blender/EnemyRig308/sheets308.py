#!/usr/bin/env python3
"""#308 enemy rig: before / after sheets from the posed vertices that enemyrig308.py dumped (fix/work/<id>_walk.npz, <id>_idle.npz).
Plain Python: numpy z-buffer rasteriser + PIL. NO GPU, no Blender, no Unity. Flat shading, parts tinted so an arm inside the body
is easy to see (body warm grey, own-left arm red, own-right arm blue, legs dark). The thin line is the ground (model origin plane).

  python Tools/resource_guard.py --wait
  python Tools/Blender/EnemyRig308/sheets308.py [ids...]        default: dokkaebi agwi changgui
Writes Art/Characters308/DokkaebiVerify/fix/<id>_walk_<view>.jpg, <id>_idle.jpg (+ _half.jpg).
These are Blender-side poses: the "game relax" rows simulate the game's formula on the source bones [O sim / I game]."""
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[3]
FIX = ROOT / 'Art/Characters308/DokkaebiVerify/fix'; WORK = FIX / 'work'
KR = {'dokkaebi': '도깨비', 'agwi': '아귀', 'changgui': '창귀'}
TINT = np.array([[.74, .72, .67], [.86, .40, .32], [.32, .52, .84], [.42, .42, .46]])
BG = np.array([.93, .93, .91]); SS = 2
try: FONT = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 20); SMALL = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 15)
except Exception: FONT = SMALL = ImageFont.load_default()
# view -> (screen-x axis, depth axis with "smaller = nearer"), Blender axes (+Z up, character faces -Y, own left = +X)
VIEWS = {'front': (np.array([1., 0, 0]), np.array([0., 1, 0])), 'side': (np.array([0., 1, 0]), np.array([-1., 0, 0])), 'back': (np.array([-1., 0, 0]), np.array([0., -1, 0]))}
VIEW_KR = {'front': '앞', 'side': '옆(자기 왼쪽에서)', 'back': '뒤'}
LIGHT = np.array([-.35, -.55, .76]); LIGHT /= np.linalg.norm(LIGHT)


def raster(V, tris, tri_tint, view, height, size):
    w, h = size[0] * SS, size[1] * SS; ax, dz = VIEWS[view]
    span = height * 1.30; scale = h / span; cz = height * .5
    sx = (V @ ax) * scale + w / 2; sy = h / 2 - (V[:, 2] - cz) * scale; dep = V @ dz
    P = np.stack([sx, sy], 1)[tris]; D = dep[tris]; T = V[tris]
    nrm = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0]); ln = np.linalg.norm(nrm, axis=1); ok = ln > 1e-12
    nrm[ok] /= ln[ok][:, None]
    facing = np.where((nrm @ -dz) < 0, -1.0, 1.0)[:, None]; nrm = nrm * facing           # two-sided cloth: normal toward the viewer
    L = LIGHT - dz * .25; L /= np.linalg.norm(L)
    shade = .34 + .66 * np.clip(nrm @ L, 0, 1); col = tri_tint * shade[:, None]
    img = np.empty((h, w, 3)); img[:] = BG; zb = np.full((h, w), np.inf)
    x0 = np.floor(P[:, :, 0].min(1)).astype(int).clip(0, w - 1); x1 = np.ceil(P[:, :, 0].max(1)).astype(int).clip(0, w - 1)
    y0 = np.floor(P[:, :, 1].min(1)).astype(int).clip(0, h - 1); y1 = np.ceil(P[:, :, 1].max(1)).astype(int).clip(0, h - 1)
    area = (P[:, 1, 0] - P[:, 0, 0]) * (P[:, 2, 1] - P[:, 0, 1]) - (P[:, 2, 0] - P[:, 0, 0]) * (P[:, 1, 1] - P[:, 0, 1])
    for i in np.where(ok & (np.abs(area) > 1e-9))[0]:
        xs = np.arange(x0[i], x1[i] + 1) + .5; ys = np.arange(y0[i], y1[i] + 1) + .5
        if not len(xs) or not len(ys): continue
        X, Y = np.meshgrid(xs, ys); a, b, c = P[i]
        w0 = ((b[0] - X) * (c[1] - Y) - (c[0] - X) * (b[1] - Y)) / area[i]
        w1 = ((c[0] - X) * (a[1] - Y) - (a[0] - X) * (c[1] - Y)) / area[i]
        w2 = 1 - w0 - w1; inside = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
        if not inside.any(): continue
        z = w0 * D[i, 0] + w1 * D[i, 1] + w2 * D[i, 2]
        sub = zb[y0[i]:y1[i] + 1, x0[i]:x1[i] + 1]; win = inside & (z < sub)
        if not win.any(): continue
        sub[win] = z[win]; img[y0[i]:y1[i] + 1, x0[i]:x1[i] + 1][win] = col[i]
    # depth edge: darken where the nearest surface jumps (reads as an ink outline, shows an arm in front of the body)
    zf = np.where(np.isfinite(zb), zb, 1e3); edge = np.zeros((h, w), bool)
    edge[:, 1:] |= np.abs(zf[:, 1:] - zf[:, :-1]) > .04; edge[1:, :] |= np.abs(zf[1:, :] - zf[:-1, :]) > .04
    img[edge] *= .35
    gy = int(round(h / 2 + cz * scale)); empty = ~np.isfinite(zb)
    if 0 <= gy < h - SS: img[gy:gy + SS][empty[gy:gy + SS]] = (.25, .25, .25)
    out = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).resize(size, Image.BOX)
    return out


def grid(rows, top, left, title, pad=4, lw=150, th=34, tt=26):
    w, h = rows[0][0].size; out = Image.new('RGB', (lw + len(rows[0]) * (w + pad), th + tt + len(rows) * (h + pad)), (245, 245, 242)); d = ImageDraw.Draw(out)
    d.text((8, 4), title, fill=(20, 20, 20), font=FONT)
    for c, l in enumerate(top): d.text((lw + c * (w + pad) + 6, th + 2), l, fill=(20, 20, 20), font=SMALL)
    for r, row in enumerate(rows):
        for c, t in enumerate(row): out.paste(t, (lw + c * (w + pad), th + tt + r * (h + pad)))
        for k, line in enumerate(left[r].split('\n')): d.text((6, th + tt + r * (h + pad) + h // 2 - 22 + k * 20), line, fill=(20, 20, 20), font=SMALL)
    return out


def save(img, name):
    img.save(FIX / name, quality=90); img.resize((img.width // 2, img.height // 2), Image.LANCZOS).save(FIX / name.replace('.jpg', '_half.jpg'), quality=88)
    print('wrote', name, img.size, flush=True)


def tints(z): lab = z['labels'][z['tris']]; return TINT[lab[:, 0]]


def run(mid):
    import json
    fx = json.loads((FIX / ('%s_fix.json' % mid)).read_text(encoding='utf-8')); me = json.loads((FIX / ('%s_measure.json' % mid)).read_text(encoding='utf-8'))
    H = me['rig']['height_m']; tgt = fx['parameters']['abduction']['target_mean_deg']
    z = np.load(WORK / ('%s_walk.npz' % mid)); tris = z['tris']; tt = tints(z); frames = [int(f) for f in z['frames']]
    rows_def = [('before', '수리 전 원본\n(보정 없음)'), ('legacy', '수리 전 + 지금 게임 보정\n.55 / .5 (모사)'), ('after', '수리본 walk_fix308\n(보정 0)')]
    for view in ('front', 'side', 'back'):
        rows = [[raster(z[k][i].astype(np.float64), tris, tt, view, H, (270, 360)) for i in range(len(frames))] for k, _ in rows_def]
        title = '%s 걷기 — %s  |  벌림 평균 %.0f° → %s, 팔꿈치 좌우 범위 차 %.0f° → %.0f°, 디딤 발바닥 %.1f…%.1f cm → %.1f…%.1f cm  [O] Blender, 게임 화면 아님' % (
            KR[mid], VIEW_KR[view], fx['before']['arms']['Left']['abduction_mean'], ('%.0f°' % tgt) if tgt is not None else '그대로',
            abs(fx['before']['arms']['asymmetry']['elbow_range_L_minus_R']), abs(fx['after']['arms']['asymmetry']['elbow_range_L_minus_R']),
            min(fx['before']['gait'][s]['sole_min_cm_stance'] for s in ('Left', 'Right')), max(fx['before']['gait'][s]['sole_max_cm_stance'] for s in ('Left', 'Right')),
            min(fx['after']['gait'][s]['sole_min_cm_stance'] for s in ('Left', 'Right')), max(fx['after']['gait'][s]['sole_max_cm_stance'] for s in ('Left', 'Right')))
        save(grid(rows, ['f%d' % f for f in frames], [l for _, l in rows_def], title), '%s_walk_%s.jpg' % (mid, view))
    z = np.load(WORK / ('%s_idle.npz' % mid)); tris = z['tris']; tt = tints(z); frames = [int(f) for f in z['frames']]; pr = me['idle_sweep']['proposed']
    rows_def = [('raw', '대기 원본\n(보정 없음)'), ('legacy', '지금 게임 보정\n.55 / .5 (모사)'), ('proposed', '제안값\n%.2f / %.2f (모사)' % (pr[0], pr[1]))]
    cols = [(v, i) for v in ('front', 'side', 'back') for i in (0, 2)]
    # the Idle take carries a Hips scale that a humanoid clip drops; without it the body floats (about 20 cm). The Unity import
    # grounds the prefab on the first idle pose, so the sheet does the same: one constant shift for every tile.
    drop = np.array([0, 0, float(z['raw'][0][:, 2].min())])
    rows = [[raster(z[k][i].astype(np.float64) - drop, tris, tt, v, H, (300, 400)) for v, i in cols] for k, _ in rows_def]
    title = '%s 대기 — 손 · 아래팔 관통: 지금 보정 %.1f / %.1f cm → 제안값 %.1f / %.1f cm  [O] 모사(Hips 스케일 뺌 · 첫 자세 최저점을 지면에 맞춤), 게임 화면 아님' % (
        KR[mid], me['clips']['idle+relax_legacy']['pen']['Left']['fore_hand_max_cm'], me['clips']['idle+relax_legacy']['pen']['Right']['fore_hand_max_cm'],
        me['clips']['idle+relax_proposed']['pen']['Left']['fore_hand_max_cm'], me['clips']['idle+relax_proposed']['pen']['Right']['fore_hand_max_cm'])
    save(grid(rows, ['%s f%d' % (VIEW_KR[v], frames[i]) for v, i in cols], [l for _, l in rows_def], title), '%s_idle.jpg' % mid)


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    for mid in (sys.argv[1:] or ['dokkaebi', 'agwi', 'changgui']): run(mid)
