"""Read-only simultaneous edge/motion-ball feasibility audit (not a cloth solver).

This cannot certify collisions, bending, lower length bounds, or native dynamics.
It never changes a mesh, weight, pin, envelope, or acceptance threshold.
"""
import argparse
import hashlib
import json
import time
from pathlib import Path

import cvxpy as cp
import numpy as np

parser = argparse.ArgumentParser()
parser.add_argument("dataset", type=Path)
parser.add_argument("--poses", nargs="+", default=["rest", "combined_reach", "fixture_raised_arms_settle", "fixture_grip_settle"])
parser.add_argument("--all-poses", action="store_true")
parser.add_argument("--out", type=Path, required=True)
parser.add_argument("--fixed-limit", action="store_true", help="Find a witness at the existing 1.35 limit; do not minimize or claim optimality")
parser.add_argument("--witness-dir", type=Path, help="Save exact float64 positions for separate primal residual verification")
args = parser.parse_args()
if args.fixed_limit and args.out.exists():
    parser.error("Fixed-limit evidence must use a new output path; original reports are preserved")
if args.fixed_limit and args.witness_dir is None:
    args.witness_dir = args.out.parent / (args.out.stem + "-witnesses")
if args.witness_dir:
    args.witness_dir.mkdir(parents=True, exist_ok=True)
data = np.load(args.dataset, allow_pickle=False)
rest, edges, mobility = data["rest"], data["edges"].astype(int), data["mobility"]
poses, names = data["posePoints"], [str(n) for n in data["poseNames"]]
assert rest.shape == (len(mobility), 3)
assert poses.shape == (len(names), len(rest), 3)
assert len(set(names)) == len(names), "Ambiguous duplicate pose names"
assert np.all(np.isfinite(rest)) and np.all(np.isfinite(poses))
assert np.all(np.isfinite(mobility)) and np.all(mobility >= 0)
assert edges.ndim == 2 and edges.shape[1] == 2 and np.all(edges >= 0) and np.all(edges < len(rest))
lengths = np.linalg.norm(rest[edges[:, 0]] - rest[edges[:, 1]], axis=1)
assert np.all(lengths > 1e-10), "Unresolved zero length source edge"
pins, free = np.flatnonzero(mobility == 0), np.flatnonzero(mobility > 0)
assert len(pins) and len(free)
indices = list(range(len(names))) if args.all_poses else [i for i, name in enumerate(names) if name in args.poses]
assert args.all_poses or len(indices) == len(args.poses), ("Missing or duplicate requested poses", args.poses, names[:25])

positions = cp.Variable(rest.shape)
centers = cp.Parameter(rest.shape)
stretch = 1.35 if args.fixed_limit else cp.Variable(nonneg=True)
constraints = [positions[pins] == centers[pins],
               cp.norm(positions[free] - centers[free], axis=1) <= mobility[free],
               cp.norm(positions[edges[:, 0]] - positions[edges[:, 1]], axis=1) <= lengths * stretch]
problem = cp.Problem(cp.Minimize(0 if args.fixed_limit else stretch), constraints)
assert problem.is_dcp()
report = {
    "status": "SIMULTANEOUS_CONVEX_GEOMETRY_AUDIT_NOT_NATIVE_CLOTH_OR_RIG_PASS",
    "mode": "FIXED_LIMIT_PRIMAL_FEASIBILITY" if args.fixed_limit else "MINIMIZE_MAXIMUM_EDGE_STRETCH",
    "initialStretchLimit": 1.35, "residualToleranceMeters": 1e-6,
    "requestedCases": len(indices), "completed": False,
    "dataset": str(args.dataset.resolve()),
    "datasetSha256": hashlib.sha256(args.dataset.read_bytes()).hexdigest(),
    "validatorSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
    "cvxpyVersion": cp.__version__, "solver": "CLARABEL", "sourceVertices": len(rest),
    "sourceEdges": len(edges), "exactPins": len(pins), "cases": [],
    "scope": "Every source vertex, edge and exact mobility ball participates simultaneously. Minimize the maximum edge extension while keeping every original pin exact. This numerical convex relaxation omits bending, tethers, self/body/brush collisions, triangle area and lower edge lengths. A primal objective above acceptance alone does not prove infeasibility: no independently certified dual lower bound is exported. A feasible result cannot approve native physics or visual quality. Fixed-limit mode finds a primal witness without estimating an optimum. Positions are diagnostics, never exported as a mesh or animation. Solver-side numerical residual summaries are recorded; solver status alone is not approval. Saved float64 witnesses require separate scalar residual verification before being described as independently checked.",
}
args.out.parent.mkdir(parents=True, exist_ok=True)
for index in indices:
    centers.value = poses[index]
    start = time.perf_counter()
    value = problem.solve(solver="CLARABEL", max_iter=300, tol_gap_abs=1e-8, tol_gap_rel=1e-8, tol_feas=1e-9, verbose=False)
    item = {"pose": names[index], "poseIndex": index, "solverStatus": problem.status,
            "optimalityClaim": "NOT_REQUESTED_FIXED_LIMIT" if args.fixed_limit else "NUMERICAL_SOLVER_STATUS_ONLY_NO_INDEPENDENT_DUAL_CERTIFICATE",
            "elapsedSeconds": time.perf_counter() - start,
            "solverIterations": problem.solver_stats.num_iters}
    if positions.value is not None and np.all(np.isfinite(positions.value)) and np.isfinite(value):
        solved = np.asarray(positions.value, dtype=np.float64).copy()
        bound = 1.35 if args.fixed_limit else float(value)
        edge_ratios = np.linalg.norm(solved[edges[:, 0]] - solved[edges[:, 1]], axis=1) / lengths
        ball_excess = np.linalg.norm(solved - poses[index], axis=1) - mobility
        worst = int(np.argmax(edge_ratios))
        item.update(actualMaximumStretch=float(np.max(edge_ratios)),
                    maximumMotionBallExcessMeters=float(max(0, np.max(ball_excess))),
                    maximumPinErrorMeters=float(np.max(np.linalg.norm(solved[pins] - poses[index, pins], axis=1))),
                    maximumEdgeConstraintExcessMeters=float(max(0, np.max((edge_ratios - bound) * lengths))),
                    worstEdge=edges[worst].tolist(), worstRestLengthMeters=float(lengths[worst]))
        item["edgeConstraintBound"] = bound
        if args.fixed_limit:
            item["fixedMaximumStretch"] = 1.35
        else:
            item["minimizedMaximumStretch"] = float(value)
        # Solver status remains distinct from a numerically feasible primal witness.
        item["solverSidePrimalWithinInitialLimits"] = (
            item["actualMaximumStretch"] <= 1.35 and item["maximumMotionBallExcessMeters"] <= 1e-6 and
            item["maximumPinErrorMeters"] <= 1e-6 and item["maximumEdgeConstraintExcessMeters"] <= 1e-6)
        if not args.fixed_limit:
            # Preserve the original stricter boolean, including optimal_inaccurate=False.
            item["withinInitialStretchLimitAndResiduals"] = (problem.status == cp.OPTIMAL and
                item["solverSidePrimalWithinInitialLimits"])
        if args.witness_dir:
            witness = args.witness_dir / f"pose-{index:04d}.npy"
            if args.fixed_limit and witness.exists():
                raise FileExistsError(f"Preserving existing witness: {witness}")
            np.save(witness, solved, allow_pickle=False)
            item.update(witnessPath=str(witness.resolve()),
                        witnessSha256=hashlib.sha256(witness.read_bytes()).hexdigest(),
                        witnessDtype=str(solved.dtype), witnessShape=list(solved.shape))
    else:
        item["solverSidePrimalWithinInitialLimits"] = False
        if not args.fixed_limit:
            item["withinInitialStretchLimitAndResiduals"] = False
        item["error"] = "No finite optimum/position array; not a zero-error result"
    report["cases"].append(item)
    args.out.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(item), flush=True)
assert hashlib.sha256(args.dataset.read_bytes()).hexdigest() == report["datasetSha256"], "Dataset changed during execution"
report["completed"] = True
args.out.write_text(json.dumps(report, indent=2, allow_nan=False), encoding="utf-8")
