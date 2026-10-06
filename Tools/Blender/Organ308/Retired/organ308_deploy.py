# RETIRED (D308-4b, 2026-10-02): v1 per-species organ parts were replaced by ONE common magic-stone shard.
# Use Tools/Blender/Organ308/shard308_run.py. Kept for history only; running it would rebuild/delete the v1 deploy tree.
raise SystemExit('retired by D308-4b: use Tools/Blender/Organ308/shard308_run.py')
# #308 organ-art: deploy contract with the C# track (Editor/WorldMacro/OrganSurface308.cs, pure Python, no bpy).
# OrganSurface308.EnsurePart resolves  <PartRoot>/<File>/SM_Organ308_<File>.fbx  and  M_Organ308_<File>.mat  by its PartSpec.File id,
# and places the part from  <PartRoot>/<File>/placement.json  (JsonUtility Placement{bone, localPosition, localEulerAngles, worldSize}):
#   position  = bone.TransformPoint(localPosition)             -> where the MESH BOUNDS CENTRE lands (not the pivot)
#   rotation  = bone.rotation * Quaternion.Euler(localEulerAngles)
#   worldScale= worldSize / max(mesh.bounds.size)              -> localScale = worldScale / bone.lossyScale
# Without placement.json it falls back to a heuristic (Dir/Size) that rescales and re-orients the part, so the file is required.
import math

UNITY_DEPLOY = "Assets/_Project/Art/Characters/Organs308"
# art id -> OrganSurface308 PartSpec.File (the C# id). Alternates share their primary's slot (choose one per slot).
DEPLOY_IDS = {
    "dokkaebi_club_knot": "dokkaebi_club_knot",
    "agwi_throat_knot": "agwi_throat_knot",
    "changgui_claw_scar": "changgui_claw_scar",
    "bulgasari_back_iron": "bulgasari_back_iron",
    "bulgasari_jaw_iron": "bulgasari_jaw_iron",
    "fox_spirit_bead": "fox_spirit_mouth_bead",
    "fox_spirit_tail_bead": "fox_spirit_mouth_bead",
    "imugi_yeouiju_socket": "imugi_chin_pearl",
    "imugi_yeokrin": "imugi_chin_pearl",
    "growth_trunk_knot": "growth_knot",
}


def deploy_id(art_id):
    return DEPLOY_IDS[art_id]


def local_dir(row):
    """Folder (relative to Art/Characters/Organ308) holding this part's deploy files, already named by deployId.
    Primaries: the _ProjectAssets mirror (copied as-is into Assets/_Project). Alternates: a staging folder whose <deployId>
    sub-folder replaces the primary's folder when the user picks the alternate."""
    d = deploy_id(row["id"])
    if row.get("alternateOf"):
        return "AlternateDeploy/%s/%s" % (row["id"], d)
    return "_ProjectAssets/Art/Characters/Organs308/%s" % d


def apply(row):
    """Stamp the deploy fields onto a placement row (idempotent)."""
    d = deploy_id(row["id"])
    row["deployId"] = d
    row["deployFbx"] = "%s/%s/SM_Organ308_%s.fbx" % (UNITY_DEPLOY, d, d)
    row["deployLocalDir"] = "Art/Characters/Organ308/" + local_dir(row)
    m = row["material"]
    m["name"] = "M_Organ308_" + d
    m["deployPath"] = "%s/%s/M_Organ308_%s.mat" % (UNITY_DEPLOY, d, d)
    row["placementJson"] = "%s/%s/placement.json" % (UNITY_DEPLOY, d)
    return row


# ---------------------------------------------------------------- Unity math (quaternion (x,y,z,w), Euler ZXY like Quaternion.Euler)
def q_mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz)


def q_rot(q, v):
    x, y, z, w = q
    m = [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
         [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
         [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]
    return [m[i][0] * v[0] + m[i][1] * v[1] + m[i][2] * v[2] for i in range(3)], m


def euler_to_q(e):
    """Quaternion.Euler(x, y, z) = qY * qX * qZ (z applied first)."""
    hx, hy, hz = (math.radians(c) * 0.5 for c in e)
    qx = (math.sin(hx), 0.0, 0.0, math.cos(hx))
    qy = (0.0, math.sin(hy), 0.0, math.cos(hy))
    qz = (0.0, 0.0, math.sin(hz), math.cos(hz))
    return q_mul(q_mul(qy, qx), qz)


def q_to_euler(q):
    """Inverse of euler_to_q for M = Ry Rx Rz, degrees in [0, 360) like Quaternion.eulerAngles."""
    n = math.sqrt(sum(c * c for c in q))
    q = tuple(c / n for c in q)
    _, m = q_rot(q, (0, 0, 0))
    sx = max(-1.0, min(1.0, -m[1][2]))
    x = math.asin(sx)
    if abs(math.cos(x)) > 1e-6:
        y = math.atan2(m[0][2], m[2][2])
        z = math.atan2(m[1][0], m[1][1])
    else:  # gimbal: fold z into y
        y = math.atan2(-m[2][0], m[0][0])
        z = 0.0
    return [round(math.degrees(a) % 360.0, 5) for a in (x, y, z)]


def q_angle_deg(a, b):
    d = abs(sum(x * y for x, y in zip(a, b))) / (math.sqrt(sum(x * x for x in a)) * math.sqrt(sum(x * x for x in b)))
    return math.degrees(2 * math.acos(min(1.0, d)))


def placement_json(row):
    """OrganSurface308 Placement for this part (+ informative extras JsonUtility ignores)."""
    loc = row["local"]
    p, q, s = loc["position"], loc["rotation"], loc["scale"]
    c = row["mesh"]["unityMeshBoundsCenter"]
    off, _ = q_rot(q, [c[0] * s[0], c[1] * s[1], c[2] * s[2]])
    centre = [round(p[i] + off[i], 6) for i in range(3)]
    e = q_to_euler(q)
    err = q_angle_deg(euler_to_q(e), q)
    if err > 0.01:
        raise ValueError("Euler round trip %.4f deg for %s" % (err, row["id"]))
    world_size = max(row["mesh"]["unityMeshBoundsSize"])  # part world scale 1 (real-size metres) in prefab / lesson space
    return dict(
        bone=row["bone"]["name"],
        localPosition=dict(x=centre[0], y=centre[1], z=centre[2]),
        localEulerAngles=dict(x=e[0], y=e[1], z=e[2]),
        worldSize=round(world_size, 6),
        id=row["deployId"], artId=row["id"], variant=row["variant"], bonePath=row["bone"]["path"],
        pivotLocalPosition=dict(x=p[0], y=p[1], z=p[2]), localRotation=dict(x=q[0], y=q[1], z=q[2], w=q[3]),
        localScale=dict(x=s[0], y=s[1], z=s[2]), eulerRoundTripDeg=round(err, 5),
        note="organ308-placement/1 · localPosition = mesh bounds centre in bone space (OrganSurface308.EnsurePart contract); "
             "valid only under 'bone' (bone.fallback must not reuse these values); FBX bind pose; TEST",
    )
