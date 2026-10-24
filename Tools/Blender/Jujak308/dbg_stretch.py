"""Debug: worst stretch / compression edges for a pose (uses build_jujak308)."""
import sys, math
import numpy as np
sys.path.insert(0, r"C:\Users\yj666\Oheangbu\Tools\Blender\Jujak308")
import build_jujak308 as J
import bpy

act_name = sys.argv[sys.argv.index("--") + 1]
frame = float(sys.argv[sys.argv.index("--") + 2])
J.stage_load(); J.stage_grade(True); J.stage_assemble(); J.stage_rig(); J.stage_actions(); J.stage_weights()
arm = J.S["arm"]
act = J.S["actions"][act_name]
J.clear_action(arm)
rest = {k: J.eval_co(o) for k, o in J.S["objs"].items()}
J.set_action(arm, act, int(frame))
for k, o in J.S["objs"].items():
    co = J.eval_co(o)
    e = J.edges_of(o)
    l0 = np.linalg.norm(rest[k][e[:, 0]] - rest[k][e[:, 1]], axis=1)
    l1 = np.linalg.norm(co[e[:, 0]] - co[e[:, 1]], axis=1)
    ok = l0 >= 0.002
    r = np.where(ok, l1 / np.maximum(l0, 1e-9), 1)
    for label, idx in (("STRETCH", np.argsort(-r)[:4]), ("COMPRESS", np.argsort(r)[:4])):
        for j in idx:
            a, b = e[j]
            wa = {o.vertex_groups[g.group].name: round(g.weight, 2) for g in o.data.vertices[a].groups}
            wb = {o.vertex_groups[g.group].name: round(g.weight, 2) for g in o.data.vertices[b].groups}
            print("DBG", k, label, round(r[j], 2), "len0", round(l0[j], 3), "at", np.round(rest[k][a], 2), "wa", wa, "wb", wb)
