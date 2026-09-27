"""#297 terrain pass: #295 height field + compound/cave terrain ops -> staged #297 height field (4 m grid, 1001 x 1501).

Ops (from <compound>/unity.json terrainOps and Finish297/Cave/portal.json):
  pad   — rotated rectangle `half` (u, v) around `centre`, cut to `top` (never raised: fill is the platform's stone face),
          blended back to the natural ground over `blend` metres.
  notch — corridor from `a` to `b` (XZ) of `width`, bottom linear from `ya` to `yb`, side blend `blend` (cave portal cut);
          optional `blendBack` shortens the blend behind `a` (the portal face stands there).
Conservative: inside an op the 4 m vertices are <= the target, so the triangulated game surface stays below pads.
Protected #293 high-mountain cells (Watershed295 protected.bytes) are never edited (reported if an op touches them).
Writes Finish297/Stage/Surface/height.bytes, edits.json (per-op stats, changed-cell bounds), edits.png (cut map).
"""
import json, math, sys
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'Oheangbu/Assets/_Project/Art/World/Watershed295/Surface'
FIN = ROOT / 'Art/World/Compact/Rebuild/Finish297'
OUT = FIN / 'Stage/Surface'
W, HH, CELL = 1001, 1501, 4.0


def load_ops(sources=None):
    """sources: op sources to include (compound names or 'Cave'). Default = Finish297/surface-sources.json, the list of
    sources whose scene work is built (a generated-but-unbuilt compound, e.g. a staged palace layout, must not cut the
    terrain under the #296 buildings it has not replaced yet). Without that file: all found."""
    if not sources:
        listed = FIN / 'surface-sources.json'
        if listed.exists(): sources = json.loads(listed.read_text(encoding='utf-8'))['sources']
    ops = []
    for u in sorted(FIN.glob('*/unity.json')):
        d = json.loads(u.read_text(encoding='utf-8'))
        for o in d.get('terrainOps', []): o['source'] = d.get('compound', u.parent.name); ops.append(o)
    portal = FIN / 'Cave/portal.json'
    if portal.exists():
        for o in json.loads(portal.read_text(encoding='utf-8')).get('terrainOps', []): o['source'] = 'Cave'; ops.append(o)
    return [o for o in ops if not sources or o['source'] in sources]


def apply(h, protected, ops):
    X, Z = np.meshgrid(np.arange(W) * CELL, np.arange(HH) * CELL)
    out = h.copy(); stats = []
    for o in ops:
        if o['type'] == 'pad':
            cx, cz = o['centre']; a = math.radians(o['yaw']); ca, sa = math.cos(a), math.sin(a)
            r = max(o['half']) + o['blend'] + 8
            sel = (np.abs(X - cx) < r) & (np.abs(Z - cz) < r)
            u = (X[sel] - cx) * ca - (Z[sel] - cz) * sa; v = (X[sel] - cx) * sa + (Z[sel] - cz) * ca
            d = np.hypot(np.maximum(np.abs(u) - o['half'][0], 0), np.maximum(np.abs(v) - o['half'][1], 0))
            w = np.clip(1 - d / o['blend'], 0, 1); w = w * w * (3 - 2 * w)
            target = o['top']
        elif o['type'] == 'notch':
            (ax, az), (bx, bz) = o['a'], o['b']; L = math.hypot(bx - ax, bz - az); tx, tz = (bx - ax) / L, (bz - az) / L
            r = L + o['width'] + o['blend'] + 8; mx, mz = (ax + bx) / 2, (az + bz) / 2
            sel = (np.abs(X - mx) < r) & (np.abs(Z - mz) < r)
            s = (X[sel] - ax) * tx + (Z[sel] - az) * tz; q = -(X[sel] - ax) * tz + (Z[sel] - az) * tx
            sc = np.clip(s, 0, L); along = np.abs(s - sc); side = np.maximum(np.abs(q) - o['width'] / 2, 0)
            # behind `a` the cut may end sharply (cave portal face covers it): blendBack, default = blend
            back = o.get('blendBack', o['blend']); along = np.where(s < 0, along * (o['blend'] / max(back, 1e-3)), along)
            d = np.hypot(along, side); w = np.clip(1 - d / o['blend'], 0, 1); w = w * w * (3 - 2 * w)
            target = o['ya'] + (o['yb'] - o['ya']) * sc / L
        else:
            continue
        cur = out[sel]; want = np.where(cur > target, cur * (1 - w) + target * w, cur)   # cut only
        prot = protected[sel] if protected is not None else np.zeros(cur.shape, bool)
        touched = (np.abs(want - cur) > 1e-4)
        want = np.where(prot, cur, want)
        out[sel] = want
        xs_, zs_ = X[sel][touched], Z[sel][touched]
        box = dict(min=[float(xs_.min()), float(zs_.min())], max=[float(xs_.max()), float(zs_.max())]) if touched.any() else None
        stats.append(dict(type=o['type'], source=o.get('source'), cells=int(touched.sum()), protectedTouched=int((touched & prot).sum()),
                          maxCut=round(float((cur - want).max()) if cur.size else 0, 3), bounds=box))
    return out, stats


def main():
    h = np.fromfile(SRC / 'height.bytes', '<f4').reshape(HH, W).astype(np.float64)
    prot_path = SRC / 'protected.bytes'
    protected = (np.fromfile(prot_path, np.uint8).reshape(HH, W) > 0) if prot_path.exists() else None
    ops = load_ops(sys.argv[1:] or None); out, stats = apply(h, protected, ops)
    diff = h - out; changed = np.argwhere(np.abs(diff) > 1e-4)
    OUT.mkdir(parents=True, exist_ok=True)
    out.astype('<f4').tofile(OUT / 'height.bytes')
    bounds = None
    if len(changed):
        (z0, x0), (z1, x1) = changed.min(0), changed.max(0); bounds = dict(min=[float(x0 * CELL), float(z0 * CELL)], max=[float(x1 * CELL), float(z1 * CELL)])
    img = np.clip(diff / 4.0, 0, 1)
    Image.fromarray((255 - img * 255).astype(np.uint8)[::-1]).save(OUT / 'edits.png')
    report = dict(sources=sorted({o['source'] for o in ops}), ops=len(ops), changedCells=int(len(changed)), fraction=float(len(changed) / (W * HH)), maxCut=float(diff.max()) if len(changed) else 0.0,
                  maxFill=float(-diff.min()) if len(changed) else 0.0, bounds=bounds, perOp=stats,
                  protectedTouched=int(sum(s['protectedTouched'] for s in stats)))
    (OUT / 'edits.json').write_text(json.dumps(report, indent=1), encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k != 'perOp'}, indent=1))


if __name__ == '__main__':
    main()
