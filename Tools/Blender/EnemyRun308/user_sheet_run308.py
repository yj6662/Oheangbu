#!/usr/bin/env python3
"""#308 enemy run: ONE sheet per monster for the user - top row = the repaired walk played at the rate the clip speed would need,
bottom row = the run; side tiles then front tiles, one shared clock. Drawn from the posed vertices enemyrun308.py dumped
(run/work/<id>_run.npz) with the numpy z-buffer rasteriser of Tools/Blender/EnemyRig308/sheets308.py. NO GPU, no Blender, no Unity.

  python Tools/resource_guard.py --wait
  python Tools/Blender/EnemyRun308/user_sheet_run308.py [ids...]        default ids: agwi changgui
Writes Art/Characters308/DokkaebiVerify/run/<id>_run_user.jpg and <id>_run_user_half.jpg.
The one sentence under the picture is enemyrun308.json monsters.<id>.user_note (what the picture does NOT show well, said plainly).
Flat colours (body grey, own-left arm red, own-right arm blue): Blender-side poses, no texture, no game light, no Unity humanoid
retarget [O Blender / I game]."""
import sys, json, textwrap
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent; sys.path.insert(0, str(HERE.parent / 'EnemyRig308')); sys.path.insert(0, str(HERE))
import sheets308 as S
import sheets_run308 as SR

RUN = SR.RUN; WORK = SR.WORK
CFG = json.loads((HERE / 'enemyrun308.json').read_text(encoding='utf-8')); U = CFG['user_sheet']
try: BIG = ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf', U['title_px']); MID = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', U['label_px'])
except Exception: BIG = MID = ImageFont.load_default()


def run(mid):
    j = json.loads((RUN / ('%s_run.json' % mid)).read_text(encoding='utf-8')); z = np.load(WORK / ('%s_run.npz' % mid))
    H = float(z['height']); tris = z['tris']; tt = S.TINT[z['labels'][tris][:, 0]]; v = float(z['clip_speed']); rate = float(z['walk_rate']); N = int(j['run']['cycle_frames']); fps = CFG['fps']
    r = j['run']; w = j['walk_at_run_speed']; key = '%.2f' % v; tile = tuple(U['tile'])
    views = [('side', U['side_tiles']), ('front', U['front_tiles'])]
    rows = []
    for src in ('all_walk', 'all_run'):
        row = []
        for view, n in views:
            for i in range(n):
                f = int(round(i * N / n))                                             # the same moment for both rows
                row.append(SR.ticks(S.raster(z[src][f % len(z[src])].astype(np.float64), tris, tt, view, H, tile), view, H, v * f / fps))
        rows.append(row)
    left = ['걷기 수리본을\n%.1f m/s에 맞춰 틀면\n%.2f배속\n분당 %.0f걸음' % (v, rate, w['by_actor_speed'][key]['steps_per_minute']),
            '달리기 (새 클립)\n1.00배속\n분당 %.0f걸음\n두 발 다 뜬 프레임\n%d / %d' % (r['steps_per_minute_at_clip_rate'], r['flight_frames_by_height'], r['cycle_frames'])]
    pad = 4; lw = U['label_width']; th = U['title_px'] + 22; hh = U['label_px'] + 16; gap = U['view_gap']
    tw, thh = tile; n_side = views[0][1]; n_all = sum(n for _, n in views)
    width = lw + n_all * (tw + pad) + gap; note = textwrap.wrap(S.KR[mid] + ': ' + CFG['monsters'][mid]['user_note'], width=U['note_wrap'])
    foot = len(note) * (U['label_px'] + 10) + 26
    out = Image.new('RGB', (width, th + hh + 2 * (thh + pad) + foot), (245, 245, 242)); d = ImageDraw.Draw(out)
    d.text((12, 8), '%s — 위: 걷기를 빨리 튼 것 / 아래: 달리기   [Blender 자세 그림 · 게임 화면 아님 · 편집기 · Play 미확인]' % S.KR[mid], fill=(20, 20, 20), font=BIG)
    def x_of(c): return lw + c * (tw + pad) + (gap if c >= n_side else 0)
    d.text((x_of(0) + 6, th + 4), '옆에서 본 모습 (왼쪽으로 달린다 · 바닥 눈금 = 땅에 박힌 점, 0.5 m 간격)', fill=(20, 20, 20), font=MID)
    d.text((x_of(n_side) + 6, th + 4), '앞에서 본 모습 (쫓길 때 보이는 쪽)', fill=(20, 20, 20), font=MID)
    for rr, row in enumerate(rows):
        y = th + hh + rr * (thh + pad)
        for c, t in enumerate(row): out.paste(t, (x_of(c), y))
        for k, line in enumerate(left[rr].split('\n')): d.text((10, y + thh // 2 - 80 + k * (U['label_px'] + 8)), line, fill=(20, 20, 20), font=MID)
    y = th + hh + 2 * (thh + pad) + 12
    for k, line in enumerate(note): d.text((12, y + k * (U['label_px'] + 10)), line, fill=(110, 30, 20), font=MID)
    name = '%s_run_user.jpg' % mid; out.save(RUN / name, quality=92)
    half = out.resize((out.width // 2, out.height // 2), Image.LANCZOS); half.save(RUN / name.replace('.jpg', '_half.jpg'), quality=90)
    print('wrote', name, out.size, 'and', name.replace('.jpg', '_half.jpg'), half.size, flush=True)


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    for mid in (sys.argv[1:] or ['agwi', 'changgui']): run(mid)
