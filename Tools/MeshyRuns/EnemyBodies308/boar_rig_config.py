"""D308-119 boar rig config for rig_creatures.py (landmarks fitted from inspect/vertices-world.json slices).

python boar_rig_config.py <inspect dir> <out config.json>
Mesh: Meshy boar_m1, +Z up, forward -Y, centred (sole z -0.592). Leg columns: fore x +-0.11 y -0.10,
hind x +-0.12 y +0.77, joined to the body at the belly z ~ -0.25; the hump (z .587) is over the forelegs.
Knee planes are bent slightly (elbow back, stifle forward, hock back) so the two-bone IK has a plane.
Elbow/stifle sit inside the visible leg column (z -.33/-.32), below the belly line: a joint at the belly line
(run-05: -.22) dragged the chest underside skin with the forearm (walk stretch 5.4, probe DEBUG_Walk).
"""
import json
import sys
from pathlib import Path

import numpy as np

inspect = Path(sys.argv[1])
ins = json.loads((inspect / "inspection.json").read_text(encoding="utf-8"))
v = np.array(json.loads((inspect / "vertices-world.json").read_text(encoding="utf-8"))["Mesh_0"]["vertices"])
G = float(v[:, 2].min())


def leg(side, hind):
    x = .12 * side
    if hind:
        return {"hip": [.14 * side, .70, .0], "knee": [x, .70, -.32], "ankle": [x, .80, -.45], "toe": [x, .715, G + .006], "parent": "Pelvis"}
    return {"hip": [.13 * side, -.12, -.08], "knee": [x, -.06, -.38], "ankle": [x, -.10, -.50], "toe": [x, -.185, G + .006], "parent": "Chest"}


legs = {"Fore_L": leg(1, 0), "Fore_R": leg(-1, 0), "Hind_L": leg(1, 1), "Hind_R": leg(-1, 1)}


def sole(side, hind):
    q = (np.sign(v[:, 0]) == side) & ((v[:, 1] > .35) == bool(hind)) & (v[:, 2] < -.45)
    low = float(v[q, 2].min())   # the hind hooves sit ~1.5 cm above the fore ones
    return [int(i) for i in np.where(q & (v[:, 2] < low + .006))[0]]


contact = {k: [{"object": "Mesh_0", "indices": sole(1 if k.endswith("_L") else -1, k.startswith("Hind"))}] for k in legs}


def leg_region(name, side, hind):
    inner = .08   # the column's inner face starts as torso, so the Upper weight diffuses across the
    lo_x, hi_x = (inner, .4) if side > 0 else (-.4, -inner)   # ~5 cm column, not the 1.5 cm armpit crease (run-08: 3.3 at k .55)
    lo_y, hi_y = (.52, 1.0) if hind else (-.30, .04)   # flank seams close to the pivot (run-06: flank at y .11 folded to .09)
    parent = "Pelvis" if hind else "Chest"
    # the region reaches up to the hip/shoulder head: the torso/leg seam then lies near the swing axis, where a swing
    # barely moves the skin (run-06: a seam at the belly line, .25 m under the pivot, stretched 3.3)
    top = legs[name]["hip"][2]
    bones = [parent, name + "_Upper", name + "_Lower", name + "_Foot"]
    rows = [{"id": name.lower(), "selector": {"bounds": {"min": [lo_x, lo_y, -1.0], "max": [hi_x, hi_y, top]}},
             "bones": bones, "chainBlend": True, "blendRadiusM": .04}]
    if True:   # below the belly the whole column is leg again (inner soles at x .079 fore, .08 hind)
        lo_x, hi_x = (.03, .4) if side > 0 else (-.4, -.03)
        rows.append({"id": name.lower() + "_column", "selector": {"bounds": {"min": [lo_x, lo_y, -1.0], "max": [hi_x, hi_y, -.28]}},
                     "bones": bones, "chainBlend": True, "blendRadiusM": .04})
    return rows


regions = [
    {"id": "torso", "selector": {"all": True}, "bones": ["Pelvis", "Spine", "Chest", "Neck", "Head"], "chainBlend": True, "blendRadiusM": .06},
    {"id": "head", "selector": {"bounds": {"min": [-1, -1.2, -1], "max": [1, -.66, 1]}}, "bones": ["Head"]},   # not rigid: a pinned seam 6 cm ahead of the head pivot tore at the head toss (run-09 attack 2.8)
] + [r for k in legs for r in leg_region(k, 1 if k.endswith("_L") else -1, k.startswith("Hind"))] + [
    # pivots low (fore -.08, hind .0): the swing then moves the belly-line junction half as far (run-08 armpit 2.7);
    # the brisket strip between the forelegs stays on the chest: smoothing had carried each foreleg's weight to the
    # midline, so legs in opposite phase tore it (retarget run-06 probe: walk 3.2, attack 4.6 at x ~0)
    {"id": "brisket", "selector": {"bounds": {"min": [-.04, -.45, -1], "max": [.04, .10, -.10]}}, "bones": ["Spine", "Chest", "Neck"], "chainBlend": True, "blendRadiusM": .06},
]

cfg = {
    "schemaVersion": 1, "species": "boar", "sourceSha256": ins["sourceSha256"],
    "landmarksReviewed": True, "regionsReviewed": True,
    "coordinateFrame": {"up": "+Z", "forward": [0, -1, 0], "units": "metres"},
    "root": [0, 0, G], "rootBoneLengthM": .1,
    "joints": {"pelvis": [0, .68, .20], "spine": [0, .30, .24], "chest": [0, -.08, .24], "neck": [0, -.40, .22],
               "head": [0, -.60, .16], "nose": [0, -.94, -.06], "mouth": [0, -.84, -.09]},
    "serpent": None, "legs": legs, "tails": [],
    "regions": {"Mesh_0": regions},
    "contactVertices": contact,
    "weightSmoothIterations": 40,
    "animation": {"fps": 30, "walkStrideM": .18, "walkLiftM": .04, "walkStanceFraction": .65, "walkBodyDropM": .015,
                  "serpentWaveDegreesPerBone": 2.0, "deathRollDegrees": 65, "groundZ": G, "amplitudeScale": 1.0},
    "limits": {"maximumTriangles": 15000, "maxInfluences": 4, "maxAssignmentDistanceM": .75, "maxIkTargetErrorM": .025,
               "maxEdgeStretchRatio": 2.5, "minEdgeCompressionRatio": .15, "edgeLengthFloorM": .002, "maxGroundPenetrationM": .008,
               "maxFootSoleSpreadM": .015, "maxLoopSeamM": .0001, "maxRoundtripPositionErrorM": .0001, "maxRoundtripPoseErrorM": .001},
    "notes": ["D308-119 boar: landmarks from vertices-world slices (boar_rig_config.py), fitted by Claude; proxy review (Claude)."],
}
Path(sys.argv[2]).write_text(json.dumps(cfg, indent=1), encoding="utf-8")
print("ground", G, "soles", {k: len(c[0]["indices"]) for k, c in contact.items()})
