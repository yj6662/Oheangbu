"""Reproduce the final broad mountain ink shader's offline math checks.

Uses NumPy only. Does not call Unity, change shader/scene files, or render a build.
Run: python Tools/Unity/verify_compact_broad_brush.py
The JSON records source hashes so it cannot be mistaken for a later revision's test.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from datetime import datetime, timezone
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / "Oheangbu/Assets/_Project"
REPORT = ROOT / "Art/World/WorldMacro/Compact/InkLandscape/BroadBrush/shader_math_checks.json"
PROFILE = PROJECT / "Art/World/WorldCompact/InkLandscape/BroadBrush/Profile.asset"
SHADERS = ("CompactNaturalGround", "WorldMacroTerrain", "CompactNaturalVegetation", "EarlyRegionFoliageCard")


def saturate(value):
    return np.clip(value, 0.0, 1.0)


def smoothstep(start, end, value):
    t = saturate((value - start) / (end - start))
    return t * t * (3.0 - 2.0 * t)


def progress(start, end, distance):
    return smoothstep(start, max(start + 1.0, end), distance)


def hash2(x, y):
    x, y = np.mod(x * .1031, 1.0), np.mod(y * .11369, 1.0)
    addition = x * (y + 19.19) + y * (x + 19.19)
    return np.mod((x + addition) * (y + addition), 1.0)


def read_vector(text, name, fallback):
    match = re.search(r"^\s+" + re.escape(name) + r":\s*\{([^}]+)\}", text, re.MULTILINE)
    if not match:
        return fallback
    return tuple(float(value) for value in re.findall(r":\s*([-+\d.eE]+)", match.group(1)))[:len(fallback)]


def stroke(qx, qy, cell_x, cell_y, seed, scale, edge_fraction):
    a = hash2(cell_x + seed, cell_y + seed)
    b = hash2(cell_y + seed * 2.0, cell_x + seed + 9.7)
    c = hash2(cell_x * 1.73 + seed + 21.0, cell_y * 1.73 + seed + 21.0)
    max_width, max_length = max(1.0, max(scale[:2])), max(1.0, max(scale[2:]))
    tile_x, tile_y = max_width * 1.4, max_length * .65
    x = qx - (cell_x + (a - .5) * .40) * tile_x
    y = qy - (cell_y + (b - .5) * .40) * tile_y
    min_width, min_length = max(1.0, min(scale[:2])), max(1.0, min(scale[2:]))
    width = min_width + (max_width - min_width) * b
    length = min_length + (max_length - min_length) * c
    t = y / length + .5
    curve = (t - .5) ** 2 * width * (a - .5) * 1.6
    tilt_limit = np.minimum(.28, width / length * .60)
    across = x - y * (a - .5) * tilt_limit - curve
    tail_start = .42 + (.64 - .42) * b
    taper = 1.0 - .90 * smoothstep(tail_start, 1.0, t)
    half_width = width * .5 * taper
    edge = width * .5 * np.clip(edge_fraction, .03, .35)
    side = 1.0 - smoothstep(np.maximum(0.0, half_width - edge), half_width + edge, np.abs(across))
    ends = smoothstep(0.0, .07, t) * (1.0 - smoothstep(.90, 1.0, t))
    return saturate(side * ends * (.80 + .20 * c))


def layer(qx, qy, seed, scale, edge_fraction, exhaustive=False):
    tile_x, tile_y = max(1.0, max(scale[:2])) * 1.4, max(1.0, max(scale[2:])) * .65
    cx, cy = np.floor(qx / tile_x), np.floor(qy / tile_y)
    coat = np.zeros_like(qx)
    indices = range(-1, 3) if exhaustive else range(2)
    for y in indices:
        for x in indices:
            value = stroke(qx, qy, cx + x, cy + y, seed, scale, edge_fraction)
            coat += value * (1.0 - coat)
    return saturate(coat)


def tone(broad, pigment, coverage, config):
    low, high = config["mountainTones"]
    coat_low, coat_high = config["coatTones"]
    threshold_a, threshold_b, softness, band_amount = config["bands"]
    half_width = max(.001, softness * .5)
    bands = .5 * smoothstep(threshold_a - half_width, threshold_a + half_width, broad)
    bands += .5 * smoothstep(threshold_b - half_width, threshold_b + half_width, broad)
    banded = broad + (bands - broad) * saturate(band_amount)
    base = np.clip(low + (high - low) * banded + (pigment - .5) * .012, min(low, high), max(low, high))
    ink_coat = coat_low + (coat_high - coat_low) * saturate(banded * .65 + pigment * .35)
    return float(saturate(base + (min(base, ink_coat) - base) * coverage * saturate(config["coverage"][0])))


def to_linear(colour):
    colour = np.asarray(colour)
    return np.where(colour <= .04045, colour / 12.92, ((colour + .055) / 1.055) ** 2.4)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=REPORT)
    parser.add_argument("--grid-width", type=int, default=256)
    parser.add_argument("--grid-height", type=int, default=192)
    args = parser.parse_args()
    hlsl_path = PROJECT / "Shaders/CompactInkLandscape.hlsl"
    hlsl = hlsl_path.read_text(encoding="utf-8-sig")
    profile_text = PROFILE.read_text(encoding="utf-8-sig") if PROFILE.exists() else ""
    config = {
        "scale": read_vector(profile_text, "BroadBrushScale", (80, 240, 420, 1000)),
        "coverage": read_vector(profile_text, "BroadBrushCoverage", (.90, .18, .76, .20)),
        "coatTones": read_vector(profile_text, "BroadBrushTones", (.015, .07)),
        "bands": read_vector(profile_text, "BroadBrushBands", (.33, .66, .12, .8)),
        "mountainTones": read_vector(profile_text, "MountainTones", (.06, .18)),
        "midAirRange": read_vector(profile_text, "MidAirRange", (500, 1500)),
        "farAirRange": read_vector(profile_text, "FarAirRange", (1500, 3400)),
        "airStrengths": read_vector(profile_text, "AirStrengths", (.18, .64)),
        "ink": read_vector(profile_text, "Ink", (.165, .149, .133)),
        "paper": read_vector(profile_text, "Paper", (.969, .945, .894)),
        "air": read_vector(profile_text, "Air", (.9, .88, .83)),
    }
    checks = []

    def check(name, passed, **details):
        checks.append({"name": name, "status": "PASS" if passed else "FAIL", **details})

    check("final shader shape contract", all(fragment in hlsl for fragment in (
        "float2 centre=(cell+(float2(a,b)-.5)*.40)*tile;",
        "float tailStart=lerp(.42,.64,b);",
        "float taper=lerp(1,.10,smoothstep(tailStart,1,t));",
        "float tiltLimit=min(.28,width/strokeLength*.60);",
        "float ends=smoothstep(0,.07,t)*(1-smoothstep(.90,1,t));",
    )))
    used = set(re.findall(r"\b_CI\w+\b", hlsl))
    source_paths = [hlsl_path, PROJECT / "Scripts/Data/World/CompactInkLandscapeProfile.cs"]
    for name in SHADERS:
        path = PROJECT / f"Shaders/{name}.shader"
        source_paths.append(path)
        source = path.read_text(encoding="utf-8-sig")
        buffer = source.split("CBUFFER_START(UnityPerMaterial)", 1)[1].split("CBUFFER_END", 1)[0]
        missing = sorted(used - set(re.findall(r"\b_CI\w+\b", buffer)))
        check(name + " uniform contract", not missing and '_CIBroadBrush("Broad mountain ink coats",Float)=0' in source,
              missing=missing)

    # Support proof valid for all positive stroke width/length settings and allowed feather.
    # At an omitted neighbour, closest possible centre is (1 - .40/2) tiles away.
    transverse_support = .5 + .2 + .15 + .175  # half-width + curve + lean + maximum feather
    omitted_x = (1.0 - .40 / 2.0) * 1.4
    longitudinal_support = .5                 # stroke ends are exactly zero outside t=[0,1]
    omitted_y = (1.0 - .40 / 2.0) * .65
    check("four-neighbour support bound", transverse_support < omitted_x and longitudinal_support < omitted_y,
          maximumTransverseSupportInMaxWidths=transverse_support, closestOmittedXInMaxWidths=omitted_x,
          maximumLongitudinalSupportInMaxLengths=longitudinal_support, closestOmittedYInMaxLengths=omitted_y)

    x, y = np.meshgrid(np.linspace(-2200, 2200, args.grid_width), np.linspace(-1700, 1700, args.grid_height))
    for seed in (5.71, 23.19):
        bounded = layer(x, y, seed, config["scale"], config["coverage"][1])
        exhaustive = layer(x, y, seed, config["scale"], config["coverage"][1], exhaustive=True)
        error = float(np.max(np.abs(bounded - exhaustive)))
        check("four versus sixteen neighbours", error < 1e-12, seed=seed, positions=int(x.size), maximumDifference=error)

    distances = np.arange(0, 4501, dtype=np.float64)
    wash = saturate(max(0, config["airStrengths"][0]) * progress(*config["midAirRange"], distances)
                    + max(0, config["airStrengths"][1]) * progress(*config["farAirRange"], distances))
    ink, paper, air = (to_linear(config[key]) for key in ("ink", "paper", "air"))
    weights = np.asarray((.2126, .7152, .0722))
    samples, decreases, minimum_delta = 0, 0, float("inf")
    for broad in np.linspace(0, 1, 11):
        for pigment in np.linspace(0, 1, 11):
            for coverage in np.linspace(0, 1, 11):
                base_tone = tone(broad, pigment, coverage, config)
                colour = ink + (paper - ink) * base_tone
                luminance = float(colour @ weights) + float((np.maximum(colour, air) - colour) @ weights) * wash
                deltas = np.diff(luminance)
                decreases += int(np.sum(deltas < -1e-12))
                minimum_delta = min(minimum_delta, float(np.min(deltas)))
                samples += int(distances.size)
    check("fixed mountain distance luminance", decreases == 0, samples=samples, surfaceCombinations=1331,
          metres={"start": 0, "end": 4500, "step": 1}, decreases=decreases, minimumAdjacentDelta=minimum_delta)

    result = {
        "status": "PASS" if all(row["status"] == "PASS" for row in checks) else "FAIL",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "revision": "broad coats; body 42-64 percent; centre jitter .40",
        "profile": str(PROFILE) if PROFILE.exists() else "shader/profile default constants",
        "configuration": config,
        "sourceSha256": {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in source_paths},
        "checks": checks,
        "limitations": [
            "CPU double-precision formula parity and support bounds; not a GPU shader execution test.",
            "Fixed world position, normal, light and coverage; material texture mips, SSAO and shadow cascades are outside this test.",
            "Visual stroke approval, Unity compilation and GPU performance require the separate capture/Play receipts.",
        ],
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"status": result["status"], "checks": len(checks), "distanceSamples": samples,
                      "output": str(args.output)}, ensure_ascii=False))
    return 0 if result["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
