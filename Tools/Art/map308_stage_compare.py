# -*- coding: utf-8 -*-
"""SPEC-MAP-OVERHAUL-308 - before / after sheet of two baked bundles (the numpy twin of the map shaders, no editor, no Play).

    python Tools/resource_guard.py --wait
    python Tools/Art/map308_stage_compare.py [--a 1a] [--b 1b] [--out Art/UI308/Map/map308_1b_compare.png]

Four rows, stage A on the left and stage B on the right, each drawn by map308_twin.render from the bundle on disk:
    1  the long wall region, every cell walked, minimap scale (1.14 m / px), enlarged x 2
    2  the gate, every cell walked, 0.7 m / px, enlarged x 2
    3  the same region at the unfolded map's default band (2.66 m / px), enlarged x 2
    4  the cliff-top field (UP-1) seen FROM THE FOOT: every cell the player can reveal without standing on a closed region
       (regions 0 and 255 walked, regions 1.. not) - the field must stay wash
Also prints what differs between the two bundles (strokes per class, wall strokes, reveal regions). Writes one PNG; reads the
bundles and Map.asset only.
"""
import argparse, json, sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import map308_lib as M
import map308_twin as TW

WALL_AT = (1965.0, 1750.0)
GATE_AT = (1902.8, 1746.8)
REGION_AT = (2250.0, 1720.0)
FIELD_AT = (2660.0, 2700.0)
PANEL = (600, 400)


def foot_walk(B):
    """the most the foot can ever reveal: every discovery cell that is not a closed region (the run-time rule: a cell with a
    region id is revealed only while the player stands in a cell of the same id)."""
    return (B.regions == 0) | (B.regions == 255)


def panel(B, at, mpp, walked, k=2):
    V = TW.View(at[0], at[1], PANEL[0], PANEL[1], mpp)
    return TW.upscale(TW.render(B, V, walked), k)


def main():
    M.utf8()
    ap = argparse.ArgumentParser(); ap.add_argument('--a', default='1a'); ap.add_argument('--b', default='1b')
    ap.add_argument('--out', default=str(M.CONCEPT / 'map308_1b_compare.png'))
    a = ap.parse_args()
    Bs = {s: TW.Bundle(M.bundle_dir(s)) for s in (a.a, a.b)}
    rows = [('장성 일대 · 전부 걸은 상태 · 미니맵 배율(1.14 m/px) · 2배 확대', WALL_AT, 1.14, None),
            ('관문 · 전부 걸은 상태 · 0.7 m/px · 2배 확대 (성벽 선이 문 양옆에서 끊기고 큰길이 그 사이를 지난다)', GATE_AT, 0.7, None),
            ('같은 곳 · 펼친 지도 기본 배율(2.66 m/px) · 2배 확대', REGION_AT, 2.66, None),
            ('금표 단애산 위 들(UP-1) · 발치에서 드러낼 수 있는 칸을 전부 걸은 상태 · 1.14 m/px — 위 들은 씻김으로 남아야 한다', FIELD_AT, 1.14, 'foot')]
    cap = TW.font(TW.FONT_SANS, 22); head = TW.font(TW.FONT_SERIF, 30)
    pad = 24; pw, ph = PANEL[0] * 2, PANEL[1] * 2
    W = pad * 3 + pw * 2; H = pad + 56 + len(rows) * (ph + 44 + pad)
    img = np.ones((H, W, 3)) * M.hexc('#2A2A26')
    fg = M.hexc('#D6D1C4')
    for i, s in enumerate((a.a, a.b)):
        n = Bs[s].note
        reg = ' '.join(f'{k}:{v}' for k, v in sorted(n['regions']['cells'].items(), key=lambda kv: int(kv[0])))
        TW.draw_text(img, (pad + i * (pw + pad), pad), f'스테이지 {s}', head, fg)
        TW.draw_text(img, (pad + i * (pw + pad) + 170, pad + 8), f"획 {n['counts']['strokes']} · 점 {n['counts']['points']} · 성벽 {n['counts']['byClass']['5']['strokes']}획 "
                     f"{n['counts']['byClass']['5']['km']} km · 드러남 구역 칸 수 {reg}", cap, fg)
    y = pad + 56
    for title, at, mpp, mode in rows:
        for i, s in enumerate((a.a, a.b)):
            B = Bs[s]; walked = foot_walk(B) if mode == 'foot' else TW.mock_walk(B, all_walked=True)
            x = pad + i * (pw + pad)
            img[y:y + ph, x:x + pw] = panel(B, at, mpp, walked)
        TW.draw_text(img, (pad, y + ph + 8), f'{title}  ·  중심 ({at[0]:.0f}, {at[1]:.0f})', cap, fg)
        y += ph + 44 + pad
    out = M.guard_out(a.out); out.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(TW.to_u8(img), 'RGB').save(out, optimize=True)
    print(f'[map308_stage_compare] wrote {M.rel(out)} {W} x {H}')
    for s in (a.a, a.b):
        n = Bs[s].note; c = n['counts']
        print(f"  {s}: strokes {c['strokes']}, points {c['points']}, file {c['bytes']} B, 3 x 3 bins max {c['maxVertices9Bins']} at {c['maxVertices9BinsAt']}, "
              f"by class {json.dumps({k: [v['strokes'], v['km']] for k, v in sorted(c['byClass'].items(), key=lambda kv: int(kv[0]))})}, regions {n['regions']['cells']}")


if __name__ == '__main__':
    main()
