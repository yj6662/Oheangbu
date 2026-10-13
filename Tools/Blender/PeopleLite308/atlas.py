"""#308 PeopleLite: layout.json(lite.py) -> 아틀라스 한 장 + 옷 색 변형. 시스템 Python(Pillow · numpy).

    python Tools/Blender/PeopleLite308/atlas.py <BodyName> [...]

옷 영역(top · bottom · skirt · band · wrap · sock)만 다시 칠한다: 원본 밝기 / 그 옷의 기준 밝기(80 백분위)를 주름 음영으로 쓰고
목표 색을 곱한다. 원본에서 유난히 밝은 곳(동정 · 고름 끝)은 소색 깃으로 남긴다. 살 · 머리 · 패랭이 · 머리띠 · 짚신은 그대로.
알파 조각(머리카락 · 눈썹 · 패랭이 끈)은 투명한 곳의 RGB 를 이웃 색으로 번지게 해 밉에서 검은 테가 안 생기게 한다(memory leaf-atlas-black-fringe)."""
import json, os, sys
import numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS = None
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
CFG = json.load(open(os.path.join(HERE, 'peoplelite308.json'), encoding='utf-8')); OUT = os.path.join(ROOT, CFG['out'])
TRIM = np.array([224, 219, 206], dtype=np.float32) / 255
_cache = {}


def source(path, scale):
    """원본을 scale 배로 미리 줄여(면적 평균에 가깝게) float RGBA 로."""
    key = (path, round(scale, 3))
    if key not in _cache:
        if path not in _cache: _cache[path] = Image.open(os.path.join(ROOT, path)).convert('RGBA')
        im = _cache[path]; w, h = im.size; s = min(1.0, scale)
        if s < 1: im = im.resize((max(8, round(w * s)), max(8, round(h * s))), Image.LANCZOS)
        _cache[key] = np.asarray(im, dtype=np.float32) / 255
    return _cache[key]


def crop(r, pad):
    u0, v0, u1, v1 = r['uv']; w, h = r['w'], r['h']
    full = Image.open(os.path.join(ROOT, r['src'])).size if r['src'] not in _cache else _cache[r['src']].size
    a = source(r['src'], max(w / ((u1 - u0) * full[0]), h / ((v1 - v0) * full[1])) * 1.0)
    H, W = a.shape[:2]
    u = u0 + (np.arange(-pad, w + pad) + .5) / w * (u1 - u0); v = v0 + (np.arange(-pad, h + pad) + .5) / h * (v1 - v0)
    if not r.get('wrapped'): u = np.clip(u, 0, 1 - 1e-6); v = np.clip(v, 0, 1 - 1e-6)
    x = np.mod(u, 1.0) * W - .5; y = (1 - np.mod(v, 1.0)) * H - .5
    x0 = np.floor(x).astype(int); y0 = np.floor(y).astype(int); fx = (x - x0)[None, :, None]; fy = (y - y0)[:, None, None]
    X0, X1, Y0, Y1 = x0 % W, (x0 + 1) % W, y0 % H, (y0 + 1) % H
    out = (a[Y0][:, X0] * (1 - fx) + a[Y0][:, X1] * fx) * (1 - fy) + (a[Y1][:, X0] * (1 - fx) + a[Y1][:, X1] * fx) * fy
    return out[::-1]      # 행 0 = 위(v 큰 쪽)


def recolour(px, target):
    rgb = px[..., :3]; L = rgb @ np.array([.299, .587, .114], dtype=np.float32)
    ok = L > .02; ref = float(np.percentile(L[ok], 80)) if ok.any() else .5
    shade = np.clip(L / max(ref, 1e-3), .25, 1.1); t = np.array(target, dtype=np.float32) / 255; lum = float(t @ np.array([.299, .587, .114]))
    k = .45 + .45 * (1 - lum)
    out = t[None, None, :] * (1 - k * (1 - shade))[..., None]
    if ref < .3:      # 어두운 옷 위의 밝은 깃 · 고름
        m = np.clip((L - 2.2 * ref) / max(1.0 * ref, 1e-3), 0, 1) * (L > .3); m = m * m * (3 - 2 * m)
        out = out * (1 - m[..., None]) + (TRIM * (.86 + .14 * np.clip(L, 0, 1))[..., None]) * m[..., None]
    res = px.copy(); res[..., :3] = np.clip(out, 0, 238 / 255); return res


def bleed(px, rounds=16):
    rgb = px[..., :3].copy(); solid = px[..., 3] > .5
    for _ in range(rounds):
        if solid.all(): break
        acc = np.zeros_like(rgb); cnt = np.zeros(solid.shape, dtype=np.float32)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            s = np.roll(solid, (dy, dx), (0, 1)); acc += np.roll(rgb, (dy, dx), (0, 1)) * s[..., None]; cnt += s
        new = ~solid & (cnt > 0); rgb[new] = acc[new] / cnt[new][:, None]; solid = solid | new
    out = px.copy(); out[..., :3] = rgb; return out


def build(name):
    work = os.path.join(OUT, '_work', name); lay = json.load(open(os.path.join(work, 'layout.json'), encoding='utf-8')); S, pad = lay['size'], lay['pad']
    crops = [(r, crop(r, pad)) for r in lay['rects']]
    mean = np.mean([c[..., :3].reshape(-1, 3).mean(0) for r, c in crops if not r['alpha']], axis=0)
    made = {}
    for var in [dict(key='orig')] + CFG['variants']:
        canvas = np.zeros((S, S, 4), dtype=np.float32); canvas[..., :3] = mean; canvas[..., 3] = 1
        for r, c in crops:
            px = c.copy(); role = r.get('role')
            if role and var['key'] != 'orig':
                target = var.get('%s_%s' % (lay['src'], role), var.get(role))
                if target: px = recolour(px, target)
            if r['alpha']: px = bleed(px)
            else: px[..., 3] = 1
            y = S - (r['y'] + r['h'] + pad); canvas[y:y + r['h'] + 2 * pad, r['x'] - pad:r['x'] + r['w'] + pad] = px
        path = os.path.join(work if var['key'] == 'orig' else OUT, '%s_%s.png' % (name, var['key']))
        im = Image.fromarray((np.clip(canvas, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA')
        if not lay['alpha']: im = im.convert('RGB')
        im.save(path, optimize=True); made[var['key']] = os.path.relpath(path, ROOT).replace(os.sep, '/')
        cloth = np.concatenate([canvas[S - (r['y'] + r['h']):S - r['y'], r['x']:r['x'] + r['w'], :3].reshape(-1, 3) for r, c in crops if r.get('role') in ('top', 'bottom', 'skirt')])
        print('ATLAS', name, var['key'], path, 'cloth mean sRGB', (cloth.mean(0) * 255).round().astype(int).tolist(), 'max', int(cloth.max() * 255))
    json.dump(made, open(os.path.join(work, 'textures.json'), 'w', encoding='utf-8'), indent=1)


for n in sys.argv[1:]: build(n)
