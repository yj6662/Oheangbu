"""Reproduce the first #296 vehicle fixture's two diagnosed errors offline.

Uses immutable first-run receipts. This arithmetic audit does not assert that
any new summon pose or drive passes the real Unity physics checks.
"""
import hashlib
import json
import math
from array import array
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Art/World/Compact/Rebuild/Architecture296"
FIRST = OUT / "History/first-vehicle-fixture"


def point(p):
    return tuple(p[k] for k in "xyz")


def arc(points):
    lengths = [0.0]
    for a, b in zip(points, points[1:]):
        lengths.append(lengths[-1] + math.dist(a, b))
    return lengths


def sample(points, lengths, distance):
    for i in range(1, len(lengths)):
        if distance <= lengths[i]:
            t = (distance - lengths[i - 1]) / (lengths[i] - lengths[i - 1])
            return tuple(a + (b - a) * t for a, b in zip(points[i - 1], points[i]))
    return points[-1]


def main():
    state_path = FIRST / "checks.json"
    drive_path = FIRST / "capital_center__palace_forward.json"
    state = json.loads(state_path.read_text(encoding="utf-8-sig"))
    drive = json.loads(drive_path.read_text(encoding="utf-8-sig"))
    case = next(c for c in state["Cases"] if c["Id"] == drive["Id"])
    points = list(map(point, case["Approach"]))
    lengths = arc(points)
    replay = []
    for trace in drive["Trace"]:
        if trace["Seconds"] < 52:
            continue
        x, y, z, w = (trace["Rotation"][k] for k in "xyzw")
        forward = (2 * (x * z + w * y), 2 * (y * z - w * x), 1 - 2 * (x * x + y * y))
        target = sample(points, lengths, min(lengths[-1], trace["Progress"] + 3.5 + 1.5 * min(1, trace["Speed"] / 4)))
        dx, dz = target[0] - trace["Position"]["x"], target[2] - trace["Position"]["z"]
        yaw = math.degrees(math.atan2(forward[2] * dx - forward[0] * dz, forward[0] * dx + forward[2] * dz))
        replay.append({"seconds": trace["Seconds"], "oldSteer": trace["Steer"],
                       "horizontalYawSteer": max(-1, min(1, yaw / 25)),
                       "pitchDegrees": math.degrees(math.asin(max(-1, min(1, forward[1])))),
                       "speed": trace["Speed"], "groundedWheels": trace["GroundedWheels"]})

    height_path = OUT.parent / "Watershed295/Generated/height.bytes"
    heights = array("f")
    heights.frombytes(height_path.read_bytes())

    def ground(x, z):
        u, v = x / 4, z / 4
        ix, iz = int(u), int(v)
        u, v = u - ix, v - iz
        a, b = heights[iz * 1001 + ix], heights[iz * 1001 + ix + 1]
        c, d = heights[(iz + 1) * 1001 + ix], heights[(iz + 1) * 1001 + ix + 1]
        return a + (b - a) * u + (c - a) * v if u + v <= 1 else d + (c - d) * (1 - u) + (b - d) * (1 - v)

    probes = []
    for behind in [18, 22, 26, 30, 14, 34, 38, 48, 64, 80, 100, 120]:
        z = 2579 + min(40, behind)
        for side in [0, -.7, .7]:
            x = 2000 + side
            y = ground(x, z)
            probes.append({"behindMetres": behind, "xz": [x, z], "terrainY": y,
                           "oldCandidateY": max(91.67604064941406, y),
                           "groundBelowProbeMetres": max(91.67604064941406, y) - y,
                           "safeFeetDownwardReachMetres": 2.5})
    final_pitch = replay[-1]["pitchDegrees"]
    result = {
        "scope": "First-run failure arithmetic only; real permanent support, summon, steering and wheel passage require another Play run.",
        "inputs": {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
                   for p in [state_path, drive_path, height_path]},
        "originalFailures": state["Failures"],
        "oldDoorProbeTerrainSamples": probes,
        "allOldDoorTerrainSamplesOutsideSafeFeetRay": all(p["groundBelowProbeMetres"] > 2.5 for p in probes),
        "doorCorrection": "Open in/out follows Generated capital_center__jeokro through the shared gate; unchanged TrySafeFeet and summon service still decide acceptance.",
        "steeringReplay": replay,
        "idealForceComparison": {"massKg": 780, "motorTorqueNm": 2100, "wheelRadiusM": .6,
            "recordedPitchDegrees": final_pitch, "gradeGravityN": 780 * 9.81 * math.sin(math.radians(final_pitch)),
            "oldThrottleCap": .7, "oldIdealDriveForceN": 2100 * .7 / .6,
            "ordinaryWThrottle": 1, "ordinaryIdealDriveForceN": 2100 / .6,
            "limitation": "Excludes wheel mass, damping, slip and load transfer; explains the input ceiling, not a live pass."},
        "unchanged": ["vehicle profile and runtime controller", "terrain and authored routes", "collision geometry", "passage success/failure thresholds"],
    }
    path = OUT / "Analysis/vehicle-first-run-diagnosis.json"
    path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(path), "oldProbeCount": len(probes),
                      "allOldProbesTooHigh": result["allOldDoorTerrainSamplesOutsideSafeFeetRay"],
                      "lastYawReplay": replay[-1], "originalFailures": state["Failures"]}))


if __name__ == "__main__":
    main()
