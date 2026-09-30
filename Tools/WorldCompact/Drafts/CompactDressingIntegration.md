# Compact dressing rebake draft

`WorldMacroDressingAuthoring.Compact.cs.tmp` is a new partial of the existing editor authoring class. It requires **no edits to the original SnapshotScene method**. It constructs that class's private Snapshot incrementally and calls the original `Snapshot.Habitat`, `Owner`, `Weights`, and `ReserveFixed` methods.

## Queue integration

```csharp
// Prerequisites: compact scene active in Edit mode; mapped mesh colliders ready;
// cloned settings.Geography, landmark positions, exclusions and fixed props ready.
WorldMacroDressingAuthoring.BeginCompact(clonedDressing, clonedLandmarks);

// On each queue step, call once and yield back to the editor.
bool ready = WorldMacroDressingAuthoring.StepCompact(maxCells: 1);
var progress = WorldMacroDressingAuthoring.CompactProgress;

// Only when ready is true:
var report = WorldMacroDressingAuthoring.FinishCompact();
// Caller now owns EditorUtility.SetDirty(clonedDressing), saving the derivative,
// assigning the compact renderer's Sheet, and ResetCache().
```

Do not put StepCompact in a synchronous `while` loop. Mesh snapshot stages process one terrain/water mesh per call. Route/solid stages process at most 256 items. Cell baking caps `maxCells` at four and yields between cells after a soft 200 ms budget; a single cell is indivisible. `maxCells: 1` is the most responsive queue setting.

`BeginCompact` returns a serializable report. `StepCompact` returns true only at ReadyToFinish. `CompactProgress` exposes counts and current stage. `FinishCompact` validates stable inputs and atomically assigns the new cells, completed regions and compact fingerprint to the supplied cloned settings **in memory**. It does not mark dirty, save assets, edit scenes, assign renderers or refresh Unity. `AbortCompact` releases temporary data without publishing anything. Starting a new bake requires finishing or aborting the previous one.

## Geometry and masks

Terrain must have the root's nonlinear X/Z-warped meshes and matching enabled, non-convex `Terrain_` MeshCollider components on the same objects. The helper casts directly against those colliders; no vertex rounding, analytic old terrain evaluation, `Physics.RaycastAll`, or non-terrain floor substitution occurs. Terrain topology and cave holes therefore determine which sampled nodes have ground.

It produces the current runtime format: 256 m cells, 17×17 heights at 16 m intervals, 64×64 habitat at 4 m intervals, and 17×17×5 realm weights. Actual terrain heights are sampled at the new lattice nodes. A per-cell two-metre probe cache reuses the nine center/edge/corner physical samples of each candidate four-metre habitat tile. Missing ground or missing supporting lattice nodes exclude the tile. Cloned cave landmark masks retain the existing additional entrance reservation. Existing water, routes, non-terrain collider bounds, mapped fixed placements and preserved areas retain their masking rules. No legacy landmark asset is loaded, and no new story/fixed props are created.

Because the runtime continues to interpolate a 16 m lattice, nonlinear terrain between those nodes is approximated. A candidate habitat tile is rejected if any of its nine physical probes differs from that interpolation by more than the optional `maximumHeightError` argument (default **0.35 m**). The report includes the largest measured error and excluded tile count. This is sampled coverage, not a mathematical guarantee for arbitrary sub-two-metre holes or curvature; final physics/visual checks remain required. The existing cave mask supplies a larger explicit exclusion around the cave opening.

The helper supports geography up to 4096×6144 m, covering the requested 4000×6000 m map. Heights cache only actual hits; finite zero placeholders in published height arrays cannot be used by habitat tiles whose supporting nodes are missing. Missing-node counts include outside-world halo samples, not just cave holes.

## Memory and stability

The guard pauses before a step/cell at 85% system commit or 512 MiB managed growth from BeginCompact. The same state can resume after memory recovers, or AbortCompact releases it. Water mesh extraction is capped at 64 MiB per mesh and 1.5 million retained triangles overall. Ground geometry is not copied: only collider references and spatial cell bins are retained. Physical probes cache per cell; lattice nodes cache across the bake.

Do not modify source meshes, collider meshes, transforms, geography, landmarks or masks during the bake. Reference/transform/bounds checks run on relevant terrain chunks and again at Finish; serialized geography/landmark/mask inputs are compared before publication. These checks do not detect an in-place vertex/topology edit that leaves the same mesh reference and bounds unchanged. The compact fingerprint is scoped to the derivative bake and is not compatible with the legacy `Validate()` command, which hard-codes the original sheet path.

## Verification performed

The new partial and the unchanged original authoring file compile together against the installed Unity 6000.3.9f1 and project DLLs, using a standalone external build:

```powershell
dotnet build Tools/WorldCompact/TransformVerification/DressingCompile.csproj --configuration Release --no-restore
```

Result: **0 warnings, 0 errors**. No Unity refresh, scene operation or physics bake was executed by this subtask. The actual compact scene bake and runtime visual checks belong to integration.
