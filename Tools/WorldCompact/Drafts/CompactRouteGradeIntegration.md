# Compact route grade fit

Consume **Art/World/WorldMacro/Compact/route_grade_fit.json** only when `readyForSculpt` is true. Its `routes` array contains `{id, carriage, width, points}`; `mainPath` is the compact Content.MainPath Vector3 array. Extra arrays preserve source indices, original Y, point pins and protected-edge classifications for auditing.

The current validated output has 36 geography routes and 7,305 MainPath points. All original shared route endpoints and every sampled point inside 201 actual content rectangles retain their source Y exactly. The rectangles include 2 m padding. Twenty navigation-only rectangles are excluded from height pins; one-dimensional compression bands are never used as content masks.

The generator samples each source centerline at no more than 2 m horizontal spacing **before** applying F, and inserts exact source rectangle boundary cuts. This removes the false bridge-grade failure produced by mapping only a coarse edge's endpoints. It then adds 2,052 intersection/projection samples without moving any existing XZ: 211 proper crossing pairs and same-level parallel overlaps enter the coupling graph. Source heights must agree within 0.1 m, and incompatible authored pin heights cannot be merged.

The fit uses a sparse convex quadratic objective: deviation from original heights plus a penalty on changes between neighboring grades. Shared graph nodes force coincident/crossing routes to the same height; vehicle constraints take priority on shared graph edges. Hard outside-protection limits use 13.9° for vehicle and 29.9° for walking, leaving 0.1° for float import. Zero-length edges after float32 XZ conversion receive zero allowed height difference. Two MainPath protected source exceptions are retained separately.

Hard-pin Lipschitz envelopes establish global feasibility before fitting. The same graph envelopes remove the optimizer's final constraint residual without moving pins. The current OSQP run reports solved; the residual repair moved at most 0.000186 m. The deliverable does not claim a proven globally optimal sculpt or a C1 vertical curve. Its dense piecewise-linear centerline has a soft smoothness penalty; any later spline should be checked again for grade overshoot.

## Checks and retained exceptions

- Independent emitted-JSON float32 maximum outside protected areas: **13.932092° vehicle**, **29.951431° walking**; no cap violations.
- Final pin error: **0 m**. Final graph edge residual: below **1e-12 m**.
- Accepted explicit junction welds: **9,800**, with **0 m** height mismatch. Coincident samples with matching source levels have no new height conflict.
- Largest cut: **33.3284 m**, Road_NorthPass_Bridge. Largest fill: **39.5154 m**, Return_SouthSaddle_CapitalSouthBridge. The latter is close to the lower bound forced by retained endpoint/content elevations and needs a broad terrain shoulder.
- MainPath source indices 155→156 retain the existing identical-XZ, 0.35451 m vertical discontinuity. It may be removed only after checking the actual cave floor, not by treating it as a successful slope fit.
- MainPath source indices 7227→7228 retain 0.5 m horizontal / 0.36417 m vertical travel inside the inn: 36.06735°. This is classified as a possible existing step and requires physical step validation.
- Four explicit coupling candidates stay separate to preserve authored pins: three approximately 4.1–4.25 cm MainPath/trail offsets and one sub-micrometre offset. These are recorded in `validation.retainedAuthoredPinOffsets`; no protected elevation is silently changed.

`route_grade_feasibility.json` records source and compact route statistics and the necessary pin-to-pin distance test. Eight original source centerlines already exceeded their selected target somewhere; their original statistics remain visible. `route_grade_coupled_report.json` contains per-route before/after maximum and p95 grades, cut/fill extrema, solver details and coupling exceptions. `route_grade_fit_validation.json` contains the independent float32 checks.

## Reproduce without Unity

```powershell
python -X utf8 Tools/WorldCompact/Drafts/route_grade_coupled.py.tmp
python -X utf8 Tools/WorldCompact/Drafts/check_route_grade_fit.py.tmp
```

The first command writes `readyForSculpt: false`; the second promotes it only after coupled-edge, pin, weld and float32 checks pass. The feasibility-only script writes independent diagnostic candidates under a separate filename and cannot replace the final coupled fit.

System Python provides NumPy/PyYAML. SciPy, OSQP and their small Python dependencies are installed only in ignored `Tools/WorldCompact/.python_deps`; no global Python packages were changed. The intersection utility passed four synthetic cases and a full-data check that all original samples/XZ were retained.

All scripts write reports/JSON only. Terrain vertices, road mesh Y projection, collider refresh, nearby loose props, dressing rebake, broad 20–50 m shoulder blending, source preservation checks, physical driving/walking and visual review remain the caller's integration work. Keep protected actual rectangles unchanged during terrain sculpting.
