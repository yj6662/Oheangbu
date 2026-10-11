"""#308 cliff boundary - `strata308`: the ground material field (rock / debris / wet / road) re-derived where a stage changed the
height. Offline. SPEC-WORLD-CLIFF-BOUNDARY-308 "n1 단계" (closes the Temporary Exception "지층 텍스처 미재생성").

  python Tools/Art/strata308.py Tools/Unity/Stage308_rugged/rugged308.json     # -> plan/Stage/surface_<stage>.png (+ report)

The #295 field (Watershed295/Surface/surface.png, read only) is copied; only cells within `margin_cells` of a cell whose height
differs from the BASE are rewritten: rock from the slope of the stage field, debris on the gentler ground at the foot of a face.
Wet (b) and road (a) are never changed. Texture row 0 = north (the file is the south-first array flipped, as #292 wrote it).
"""
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image
from scipy import ndimage as ndi

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B                                                # noqa: E402


def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)


def main():
    B.utf8(); cfg_p = Path(sys.argv[1]); cfg = json.loads(cfg_p.read_text(encoding='utf-8')); st = cfg['strata']; stage = cfg['stage']
    src = B.A / 'Art/World/Watershed295/Surface/surface.png'; h = B.load_height(); f = B.load_height(B.PL / f'Stage/height_p{stage}.bytes')
    old = np.flipud(np.asarray(Image.open(src).convert('RGBA'))).astype(np.float32) / 255
    if old.shape[:2] != (B.HH, B.W): raise SystemExit(f'REFUSED: {B.rel(src)} is {old.shape}, expected {(B.HH, B.W)}')
    gz, gx = np.gradient(f, B.CELL); slope = np.hypot(gx, gz)
    zone = ndi.binary_dilation(np.abs(f - h) > 0.05, iterations=int(st['margin_cells']))
    rock = smooth(st['rock_from'], st['rock_full'], slope)
    steep = slope >= st['rock_full']; near = ndi.gaussian_filter(steep.astype(np.float32), st['debris_sigma_cells'])
    debris = np.clip(smooth(0.04, 0.3, near) * st['debris_max'], 0, 1) * (1 - rock)
    new = old.copy()
    new[..., 0] = np.where(zone, np.maximum(old[..., 0], rock), old[..., 0])
    new[..., 1] = np.where(zone, np.maximum(old[..., 1] * (1 - rock), debris), old[..., 1])
    out = B.PL / f'Stage/surface_{stage}.png'; B.guard_out(out)
    Image.fromarray((np.flipud(np.clip(new, 0, 1)) * 255 + 0.5).astype(np.uint8), 'RGBA').save(out)
    ch = np.abs(new - old).max(-1) > 1 / 255
    rep = dict(stage=stage, source=B.rel(src), source_sha256=B.sha256(src), height_sha256=B.sha256(B.PL / f'Stage/height_p{stage}.bytes'), out=B.rel(out), out_sha256=B.sha256(out),
               zone_cells=int(zone.sum()), changed_cells=int(ch.sum()), changed_outside_zone=int((ch & ~zone).sum()), wet_changed=int((np.abs(new[..., 2] - old[..., 2]) > 0).sum()), road_changed=int((np.abs(new[..., 3] - old[..., 3]) > 0).sum()),
               rock_mean_in_zone=dict(before=round(float(old[..., 0][zone].mean()), 3), after=round(float(new[..., 0][zone].mean()), 3)),
               rock_on_faces_over_50deg=dict(before=round(float(old[..., 0][zone & (slope > 1.19)].mean()), 3), after=round(float(new[..., 0][zone & (slope > 1.19)].mean()), 3)))
    B.write_json(B.PL / f'Stage/surface_{stage}.report.json', rep); print('strata308:', json.dumps(rep, ensure_ascii=False))
    return 0


if __name__ == '__main__':
    sys.exit(main())
