# Compact grade sculpt: geometry review

Read-only review of serialized source/output terrain meshes and authoring code. No Unity calls or asset mutations. The persisted grade receipt was already at 88/88 chunks when inspected.

## Confirmed cause of the 155.73 m change

`WorldMacroRoadGradeSO.Height` transfers the nearest centerline's **absolute height** across a 50 m shoulder. A route's longitudinal cut/fill bound therefore does not bound the sculpt's lateral hillside removal.

Exact largest source/output vertex difference:

- Terrain_024, vertex 3004.
- Source XYZ = (-704, 308.48931885, -2192).
- Compact XZ = (-718.44202996, -1942.82761447).
- Output Y = 152.75549316; cut = 155.73382568 m.
- Nearest road = Road_Jeokro_CapitalSouthBridge, 27.41550 m away, width 8 m.
- Original centerline Y = 36.188904; fitted centerline Y = 35.90209385.
- The route adjustment here is only **-0.28681015 m**. The original hillside vertex is **272.30041485 m above the source centerline**.

The next largest cut, 154.73913574 m, affects the same road shoulder with the same -0.28681 m centerline adjustment. This is not evidence of a failed centerline optimizer; it is direct evidence of broad lateral flattening.

## Actual triangles do not realize the dense fitted profile

The source sheet has GridSpacing=16 m. `GeometryStep` transforms existing vertices and retains all source triangles. `StepGrade` changes only existing vertex Y values. There is no grade-driven refinement or insertion of road centerline/edge vertices.

Across 383,516 serialized terrain vertices, the measured compact XZ length of the first edge of each existing triangle has median 15.9974 m and p95 22.6274 m. These are global mesh measurements, not corridor-only measurements. Many triangles remain wider than the full-width 2.2 m trail, 4.1 m MainPath, or 8 m road. A passing dense analytic centerline therefore does not establish grade, clearance, or crossfall on actual triangles.

`WorldMacroTerrain.SurfaceHeight(compactSheet,x,z)` returns `RoadGrade.Height` at the query position after inverse-sampling the source field. It does **not** interpolate the final compact sculpted mesh. `StepGrade` sets road-ribbon vertices to this analytic result +0.012 m. Those ribbon vertices can float above or penetrate the actual coarse terrain collider between its vertices.

Concrete geometry fix:

1. Restore the immutable pre-grade compact vertex positions before applying a changed sculpt function. Reapplying blends to already-sculpted vertices compounds the deformation.
2. Refine only affected road-core triangles and insert centerline/road-edge samples as constrained mesh vertices. A practical starting edge limit is `min(roadWidth/3, 2 m)` in the core; use progressively coarser triangles through shoulders. Share split vertices on chunk boundaries.
3. Evaluate road ribbons and attachment support from the resulting actual terrain triangles/colliders. Audit centerline, both road edges and wheel tracks; grade approval belongs to those triangles, not the scalar field alone.
4. Protected rectangles need mesh boundary cuts or an equivalent rule protecting whole intersecting triangles. Keeping only vertices inside a rectangle fixed does not prevent an outside corner from tilting the part of a triangle inside that rectangle.

## Nearby routes at different levels

`Height` picks the nearest centerline in XZ without consulting its original/source height. Its segment record does not retain original Y or route identity. An unrelated higher/lower route can claim a hillside, and nearest-route identity can switch abruptly at a Voronoi boundary even when each individual shoulder weight is smooth. Centerline welds only solve same-level crossings, not nearby distinct levels or competing shoulders.

Preserve original source-height samples in the grade profile. Select or blend only source-level-compatible corridor components, with explicit road-surface ownership near crossings. Do not blend physically distinct overpass/cave levels into a single heightfield; use the relevant separate physical surface there.

## Lateral displacement formula

For shoulder point q and nearest same-level route projection p, use a displacement field:

`delta(p) = fittedRouteY(p) - originalRouteY(p)`

`newTerrainY(q) = originalTerrainY(q) + shoulderWeight(q) * delta(p)`

This preserves the original lateral relief and bounds shoulder movement by the route adjustment. Actual road-core leveling is a separate narrow cross-section problem: constrain the fitted center and road edges, allow a chosen crossfall, and blend that local bed into the displacement-preserving shoulder. Applying the broad absolute center height to the full shoulder is what produced the measured 155.73 m cut.

## Additional sampling issue

`WorldMacroRoadGradeSO.Ensure` retains segments longer than about 3.16 mm, but `WorldMacroTerrain.SegmentDistance` forces t=0 for every segment shorter than about 31.62 mm (`length > .001f` compares squared length). Dense intersection/projection samples include such short segments. Give grade sampling its own projection with a small squared-length degeneracy threshold; otherwise it samples the start height rather than the correct interpolation fraction. This does not explain 155 m changes, but matters for accurate small residuals.
