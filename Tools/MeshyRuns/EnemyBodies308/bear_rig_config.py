"""D308-119 bear (마석 곰 boss body) rig config for rig_creatures.py (landmarks fitted from inspect/vertices-world.json slices).

python bear_rig_config.py <inspect dir> <out config.json>
Mesh: Meshy bear_m1 (Asian black bear, white chest crescent), +Z up, forward -Y, centred (sole z -0.677).
Leg columns: fore x +-0.27 y -0.04 (separate below z -0.38, paws reach forward to y -0.43), hind x +-0.25 y +0.60
(separate below z -0.43, plantigrade soles y +0.41..+0.80). Belly line ~ -0.33; head from y -0.57 forward, nose y -0.947.
Lessons carried over from the boar (boar_rig_config.py, retarget runs 01-10), revised by bear runs 07-15: the hind pivot low
(a little above the belly line), the fore pivot high (a short upper arm folds the elbow front); every box edge that meets the
torso fades to the parent bone (rig_creatures region fade); above the knee/elbow no Lower, Foot only near the wrist;
elbow / stifle inside the visible leg column, leg regions reach up to the pivot (seam near the swing axis), the inner face
of the column starts as torso (inner bound) with a column region below the belly, a brisket strip on the chest chain,
head not rigid, smoothing 40, limits = the Spec gates.
"""
import json
import sys
from pathlib import Path

import numpy as np

inspect = Path(sys.argv[1])
ins = json.loads((inspect / "inspection.json").read_text(encoding="utf-8"))
v = np.array(json.loads((inspect / "vertices-world.json").read_text(encoding="utf-8"))["Mesh_0"]["vertices"])
G = float(v[:, 2].min())
SPLIT_Y = .2   # fore / hind legs are apart along y between +0.10 and +0.38 (belly only)


def leg(side, hind):
    # hind pivot near the belly line (boar lesson; run-07: a hip at z -.12 put the crotch 20 cm under the swing axis -> walk 3.0);
    # fore pivot stays high: a .22 m upper arm (run-12/13, shoulder at -.22) made the IK fold the elbow front to 4-8 %
    if hind:   # stifle forward, hock back and low (plantigrade: the heel is near the ground)
        x = .25 * side
        return {"hip": [.22 * side, .58, -.25], "knee": [x, .54, -.44], "ankle": [x, .72, -.60], "toe": [x, .46, G + .006], "parent": "Pelvis"}
    x = .27 * side   # elbow back, wrist over the paw heel
    return {"hip": [.24 * side, -.06, -.12], "knee": [x, -.01, -.44], "ankle": [x, -.08, -.60], "toe": [x, -.33, G + .006], "parent": "Chest"}


legs = {"Fore_L": leg(1, 0), "Fore_R": leg(-1, 0), "Hind_L": leg(1, 1), "Hind_R": leg(-1, 1)}


def sole(side, hind):
    q = (np.sign(v[:, 0]) == side) & ((v[:, 1] > SPLIT_Y) == bool(hind)) & (v[:, 2] < -.55)
    low = float(v[q, 2].min())
    return [int(i) for i in np.where(q & (v[:, 2] < low + .006))[0]]


contact = {k: [{"object": "Mesh_0", "indices": sole(1 if k.endswith("_L") else -1, k.startswith("Hind"))}] for k in legs}


def leg_region(name, side, hind):
    parent = "Pelvis" if hind else "Chest"
    bones = [parent, name + "_Upper", name + "_Lower", name + "_Foot"]
    top = legs[name]["hip"][2]
    # every box edge that meets the torso fades to the parent bone (rig_creatures fade): run-08/09 tore at the haunch top,
    # a 1.5 cm edge from .11 to .43 Upper, where the box handed .9 Upper next to a torso vertex with none
    down = {"axis": 2, "outer": top, "inner": top - .12, "toward": parent}
    if hind:
        # Above the knee the thigh follows Pelvis + Upper only (run-03/06: 24 % Lower at the crotch); the inner face fades
        # to the pelvis toward the groin strip, so the crotch (x .05, run-07: 3.0) sits on a gradient, not a step.
        lo_x, hi_x = (.03, .5) if side > 0 else (-.5, -.03)
        inward = {"axis": 0, "outer": .03 * side, "inner": .08 * side, "toward": parent}   # narrow: run-10 inner knee carried .25 Pelvis
        knee = legs[name]["knee"][2]
        return [{"id": name.lower(), "selector": {"bounds": {"min": [lo_x, .36, -1.0], "max": [hi_x, 1.0, top]}},
                 "bones": bones[:2], "chainBlend": True, "blendRadiusM": .04, "fade": [down, inward]},
                {"id": name.lower() + "_shank", "selector": {"bounds": {"min": [lo_x, .36, -1.0], "max": [hi_x, 1.0, knee + .02]}},
                 "bones": bones[1:], "chainBlend": True, "blendRadiusM": .04}]
    # fore: the same split as the hind leg (run-14: .11-.19 Lower in the armpit front, above the elbow, folded an edge to 3 %
    # when the elbow flexed): above the elbow Chest + Upper, below it Upper + Lower, Foot only from 8 cm above the wrist.
    # The columns start at x ~.11 at the paw and merge into a wide body above the belly; below the belly (z -.37) the whole
    # column is leg again, paw included (fore paws reach y -0.43).
    lo_x, hi_x = (.12, .5) if side > 0 else (-.5, -.12)
    inward = {"axis": 0, "outer": .12 * side, "inner": .18 * side, "toward": parent}
    rows = [{"id": name.lower(), "selector": {"bounds": {"min": [lo_x, -.32, -1.0], "max": [hi_x, .08, top]}},
             "bones": bones[:2], "chainBlend": True, "blendRadiusM": .05, "fade": [down, inward]}]
    lo_x, hi_x = (.05, .5) if side > 0 else (-.5, -.05)
    elbow, wrist = legs[name]["knee"][2], legs[name]["ankle"][2]
    rows.append({"id": name.lower() + "_column", "selector": {"bounds": {"min": [lo_x, -.50, -1.0], "max": [hi_x, .10, -.37]}},
                 "bones": bones[:2], "chainBlend": True, "blendRadiusM": .05})
    rows.append({"id": name.lower() + "_forearm", "selector": {"bounds": {"min": [lo_x, -.50, -1.0], "max": [hi_x, .10, elbow + .02]}},
                 "bones": bones[1:3], "chainBlend": True, "blendRadiusM": .05})
    rows.append({"id": name.lower() + "_paw", "selector": {"bounds": {"min": [lo_x, -.50, -1.0], "max": [hi_x, .10, wrist + .08]}},
                 "bones": bones[2:], "chainBlend": True, "blendRadiusM": .05,
                 "fade": [{"axis": 2, "outer": wrist + .08, "inner": wrist, "toward": name + "_Lower"}]})
    return rows


regions = [
    {"id": "torso", "selector": {"all": True}, "bones": ["Pelvis", "Spine", "Chest", "Neck", "Head"], "chainBlend": True, "blendRadiusM": .07},
    {"id": "head", "selector": {"bounds": {"min": [-1, -1.2, -.1], "max": [1, -.66, 1]}}, "bones": ["Head"]},
] + [r for k in legs for r in leg_region(k, 1 if k.endswith("_L") else -1, k.startswith("Hind"))] + [
    # the groin strip between the hind legs stays on the pelvis chain (legs in opposite phase tear a midline the smoothing shares)
    {"id": "groin", "selector": {"bounds": {"min": [-.03, .30, -1], "max": [.03, 1.0, -.15]}}, "bones": ["Pelvis", "Spine"], "chainBlend": True, "blendRadiusM": .09},
    {"id": "brisket", "selector": {"bounds": {"min": [-.06, -.45, -1], "max": [.06, .10, -.15]}}, "bones": ["Spine", "Chest", "Neck"], "chainBlend": True, "blendRadiusM": .07},
]

cfg = {
    "schemaVersion": 1, "species": "bear", "sourceSha256": ins["sourceSha256"],
    "landmarksReviewed": True, "regionsReviewed": True,
    "coordinateFrame": {"up": "+Z", "forward": [0, -1, 0], "units": "metres"},
    "root": [0, 0, G], "rootBoneLengthM": .12,
    "joints": {"pelvis": [0, .62, .18], "spine": [0, .25, .24], "chest": [0, -.10, .26], "neck": [0, -.45, .30],
               "head": [0, -.62, .30], "nose": [0, -.94, .22], "mouth": [0, -.86, .17]},
    "serpent": None, "legs": legs, "tails": [],
    "regions": {"Mesh_0": regions},
    "contactVertices": contact,
    "weightSmoothIterations": 40,
    "animation": {"fps": 30, "walkStrideM": .2, "walkLiftM": .05, "walkStanceFraction": .65, "walkBodyDropM": .015,
                  "serpentWaveDegreesPerBone": 2.0, "deathRollDegrees": 65, "groundZ": G, "amplitudeScale": 1.0},
    "limits": {"maximumTriangles": 16000, "maxInfluences": 4, "maxAssignmentDistanceM": .75, "maxIkTargetErrorM": .025,
               "maxEdgeStretchRatio": 2.5, "minEdgeCompressionRatio": .15, "edgeLengthFloorM": .002, "maxGroundPenetrationM": .008,
               "maxFootSoleSpreadM": .015, "maxLoopSeamM": .0001, "maxRoundtripPositionErrorM": .0001, "maxRoundtripPoseErrorM": .001},
    "notes": ["D308-119 bear (마석 곰): landmarks from vertices-world slices (bear_rig_config.py), fitted by Claude; proxy review (Claude). "
              "maximumTriangles 16000: the Meshy body has 15663 (boss, one per realm)."],
}
Path(sys.argv[2]).write_text(json.dumps(cfg, indent=1), encoding="utf-8")
print("ground", G, "soles", {k: len(c[0]["indices"]) for k, c in contact.items()})
