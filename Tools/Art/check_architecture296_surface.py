"""Offline shader contract and numerical sampling checks; no Unity commands.

The numerical tests evaluate continuity, mean colour, repeat correlation and
normal-frame rotation. They are not a replacement for Unity shader compilation,
moving-camera review or a GPU measurement of the final candidate.
"""
from pathlib import Path
import argparse
import hashlib
import json
import re
import numpy as np
from PIL import Image
import build_architecture296_surface as build

ROOT, OUT = build.ROOT, build.OUT


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def hash32(value):
    x = np.asarray(value, dtype=np.uint32)
    with np.errstate(over="ignore"):
        x = (x ^ (x >> 16)) * np.uint32(0x7FEB352D)
        x = (x ^ (x >> 15)) * np.uint32(0x846CA68B)
        return x ^ (x >> 16)


def frame(uv, salt=31, rotate=True):
    grid = np.stack([uv[..., 0] - uv[..., 1] * .577350269,
                     uv[..., 1] * 1.154700538], axis=-1) * .55
    cell = np.floor(grid).astype(np.int64)
    s = grid - cell
    lower = s[..., 0] + s[..., 1] < 1
    a = cell + np.where(lower[..., None], [0, 0], [1, 1])
    b = cell + np.where(lower[..., None], [1, 0], [0, 1])
    c = cell + np.where(lower[..., None], [0, 1], [1, 0])
    w = np.where(lower[..., None], np.stack([1-s[..., 0]-s[..., 1], s[..., 0], s[..., 1]], -1),
                 np.stack([s[..., 0]+s[..., 1]-1, 1-s[..., 0], 1-s[..., 1]], -1))
    w = w*w
    w /= w.sum(axis=-1, keepdims=True)
    coordinates, rotations = [], []
    for v in (a, b, c):
        with np.errstate(over="ignore"):
            h = hash32(v[..., 0].astype(np.uint32) ^ hash32(v[..., 1].astype(np.uint32) + np.uint32((salt*0x9E3779B9)&0xffffffff)))
            j = hash32(h + np.uint32(0x68BC21EB))
        offset = np.stack([h & 65535, j & 65535], -1) / 65536
        turn = ((j >> 16) & 3).astype(int) if rotate else np.zeros(h.shape, int)
        r = np.asarray([[1, 0], [0, 1], [-1, 0], [0, -1]])[turn]
        rotated = np.stack([uv[..., 0]*r[..., 0]-uv[..., 1]*r[..., 1],
                            uv[..., 0]*r[..., 1]+uv[..., 1]*r[..., 0]], -1)
        coordinates.append(rotated + offset)
        rotations.append(r)
    return coordinates, rotations, w


def bilinear(texture, uv):
    h, w = texture.shape[:2]
    pos = uv * [w, h] - .5
    index = np.floor(pos).astype(np.int64)
    f = pos-index
    x, y = index[..., 0] % w, index[..., 1] % h
    a, b = texture[y, x], texture[y, (x+1) % w]
    c, d = texture[(y+1) % h, x], texture[(y+1) % h, (x+1) % w]
    return (a*(1-f[..., 0, None])+b*f[..., 0, None])*(1-f[..., 1, None]) + (c*(1-f[..., 0, None])+d*f[..., 0, None])*f[..., 1, None]


def sample(texture, uv):
    coords, _, weights = frame(uv)
    mean = texture.mean(axis=(0, 1))
    mixed = sum(bilinear(texture, q)*weights[..., k, None] for k, q in enumerate(coords))
    gain = np.minimum(1.35, 1/np.sqrt(np.sum(weights*weights, axis=-1)))
    return np.clip(mean + (mixed-mean)*gain[..., None], 0, 1)


def analytic(uv):
    coords, _, weights = frame(uv)
    samples = [np.stack([.5+.22*np.sin(q[..., 0]*np.pi*2), .4+.19*np.cos(q[..., 1]*np.pi*2),
                         .45+.1*np.sin((q[..., 0]+q[..., 1])*np.pi*2)], -1) for q in coords]
    mixed = sum(c*weights[..., k, None] for k, c in enumerate(samples))
    gain = np.minimum(1.35, 1/np.sqrt(np.sum(weights*weights, axis=-1)))
    mean = np.asarray([.5, .4, .45])
    return np.clip(mean + (mixed-mean)*gain[..., None], 0, 1)


def unskew(grid):
    y = grid[..., 1]/(.55*1.154700538)
    return np.stack([grid[..., 0]/.55+.577350269*y, y], -1)


def run():
    checks = []
    def check(name, ok, details):
        checks.append({"name": name, "passed": bool(ok), "details": details})
    expected = build.run(check=True)
    check("Generated source copies reproduce exactly", expected["passed"], expected["files"])
    helper = (OUT / "Stochastic296.hlsl").read_text(encoding="utf-8")
    ground = (OUT / "KoreanInkGround296.shader").read_text(encoding="utf-8")
    surface = (OUT / "KoreanSurface296.hlsl").read_text(encoding="utf-8")
    highland = (OUT / "HighlandGranite296.shader").read_text(encoding="utf-8")
    highland_surface = (OUT / "HighlandSurface296.hlsl").read_text(encoding="utf-8")
    old_ground = (build.ASSETS / "Watershed295/Shaders/KoreanInkGround295.shader").read_text(encoding="utf-8")
    check("Ground shared inline samplers fit per-pass bindings", len(re.findall(r"\bSAMPLER\(", ground)) == 2 and len(re.findall(r"\bSAMPLER\(", highland)) == 5
          and "SAMPLER(sampler_Surface296_linear_repeat_aniso8)" in ground and "SAMPLER(sampler_SurfaceLinearClamp296)" in ground
          and "sampler_GroundPathMask" not in ground+surface,
          {"ground": len(re.findall(r"\bSAMPLER\(", ground)), "old_ground": len(re.findall(r"\bSAMPLER\(", old_ground)), "highland": len(re.findall(r"\bSAMPLER\(", highland)), "reason": "Inline states survive texture optimization in DepthNormals; ground scan repeat/aniso8 and mask clamp states have no per-texture binding dependency."})
    check("Helper is world anchored and uses explicit gradients", all(key in helper for key in ["SAMPLE_TEXTURE2D_GRAD", "f.dx", "f.dy", "SurfaceHash296", "rotation.x,-rotation.y"])
          and not re.search(r"\b_Time\b|_WorldSpaceCameraPos|screen|Screen", helper), "No time/camera/screen key; quarter-turn normal inverse and hashed lattice coordinates present.")
    check("Five realm floors use shared nonrepeating colour frame", surface.count("FloorNear296(SurfaceColour296(") == 5 and "float2 uv2=" not in surface, "5 colour branches; original repeating second tap removed.")
    check("Floor and bank normals declared without extra samplers", all(f"TEXTURE2D({p});" in ground and f"{p}(" in ground for p in build.NORMAL_PROPERTIES), build.NORMAL_PROPERTIES)
    check("Bank projection and seed aligned", surface.count("41u,1") == 2 and surface.count("43u,1") == 2 and "mn.xy=float2(mn.y,-mn.x)" in surface, "Colour/normal use seeds41 and43; original mud rotation is inverted for normal.")
    check("Dirt and rock channels align", surface.count("11u,1") == 2 and surface.count("17u,0") == 6 and surface.count("19u,0") == 6, "Matching frame seeds and world scales for each colour/cavity/normal family.")
    check("Far expensive detail is bounded", "if(detail<=0)return n" in surface and "if(distanceWS<750)" in surface and "if(distanceWS<240)" in surface and "if(distanceWS<110)" in surface and "if(nearDetail>0)" in highland_surface and "smoothstep(500,750,distanceWS)" in highland_surface, "Ground normal≤150m, cavity≤240m, gravel≤110m; highland normal/AO only while nearDetail>0; highland colour fades to existing mean by750m.")
    for path in (build.ASSETS / "Reworld292/Highlands293/Textures").glob("*_nor_gl_2k.jpg.meta"):
        if any(slug in path.name for slug in ["forrest_ground_01", "burned_ground_01", "rocks_ground_06", "brown_mud_rocks_01", "dry_ground_rocks"]):
            check("Paired source normal import: "+path.stem, "textureType: 1" in path.read_text(encoding="utf-8"), "Read-only source normal import remains NormalMap.")
    # Probe both sides of triangle diagonals and neighbouring lattice-cell edges.
    probes = []
    for origin in [(-15, -8), (0, 0), (18, 31)]:
        for t in np.linspace(.05, .95, 19):
            for edge in [([t, 1-t], [1, 1]), ([1, t], [1, 0]), ([t, 1], [0, 1])]:
                p = np.asarray(origin)+edge[0]
                probes.append((p-np.asarray(edge[1])*1e-7, p+np.asarray(edge[1])*1e-7))
    aa = analytic(unskew(np.asarray([p[0] for p in probes])))
    bb = analytic(unskew(np.asarray([p[1] for p in probes])))
    seam_error = float(np.max(np.abs(aa-bb)))
    check("Colour continuous across all lattice edge types", seam_error < 1e-5, {"edge_pairs": len(probes), "max_delta": seam_error})
    n = np.array([.2, -.3])
    rotations = np.array([[1, 0], [0, 1], [-1, 0], [0, -1]])
    forward = np.stack([n[0]*rotations[:, 0]-n[1]*rotations[:, 1], n[0]*rotations[:, 1]+n[1]*rotations[:, 0]], -1)
    back = np.stack([forward[:, 0]*rotations[:, 0]+forward[:, 1]*rotations[:, 1], -forward[:, 0]*rotations[:, 1]+forward[:, 1]*rotations[:, 0]], -1)
    check("Normal quarter-turns preserve the physical vector", np.max(np.abs(back-n)) < 1e-12, {"max_roundtrip_error": float(np.max(np.abs(back-n)))})
    coords = np.stack(np.meshgrid(np.arange(192)*.5+1500, np.arange(192)*.5+4100), -1)*.3125
    stats = []
    for slug in ["forrest_ground_01", "burned_ground_01", "rocks_ground_06", "brown_mud_rocks_01", "dry_ground_rocks"]:
        path = build.ASSETS / "Reworld292/Highlands293/Textures" / (slug+"_diff_2k.jpg")
        srgb = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32)/255
        linear = np.where(srgb<=.04045, srgb/12.92, ((srgb+.055)/1.055)**2.4)
        current = sample(linear, coords)
        shifted = sample(linear, coords + [1, 0])
        mean = linear.mean(axis=(0, 1))
        luma = [.2126, .7152, .0722]
        mean_delta = float(abs(np.mean(current @ luma) - mean @ luma)/max(.01, mean @ luma))
        correlation = float(np.corrcoef((current@luma).ravel(), (shifted@luma).ravel())[0, 1])
        row = {"texture": slug, "mean_luminance_relative_delta": mean_delta,
               "correlation_at_one_original_repeat": correlation, "source_sha256": sha(path)}
        stats.append(row)
        check("Scan mean retained: "+slug, mean_delta < .05, row)
        check("Original single-tile repeat broken: "+slug, correlation < .75, row)
    sources = [build.ASSETS / p for p in ["Watershed295/Shaders/KoreanInkGround295.shader", "Watershed295/Shaders/KoreanSurface295.hlsl", "Reworld292/Shaders/HighlandGranite293.shader", "Reworld292/Shaders/HighlandSurface293.hlsl"]]
    return {"passed": all(c["passed"] for c in checks), "scope": "Offline source and numerical contracts. Unity compiler, actual frames and GPU timing still required.",
            "checks": checks, "texture_statistics": stats, "protected_source_sha256": {str(p.relative_to(ROOT)): sha(p) for p in sources},
            "private_sha256": {p.name: sha(p) for p in sorted(OUT.glob("*.hlsl"))} | {p.name: sha(p) for p in sorted(OUT.glob("*.shader"))}}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    result = run()
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({"passed": result["passed"], "checks": len(result["checks"]), "failures": [x for x in result["checks"] if not x["passed"]], "texture_statistics": result["texture_statistics"]}, indent=2))
    raise SystemExit(0 if result["passed"] else 1)
