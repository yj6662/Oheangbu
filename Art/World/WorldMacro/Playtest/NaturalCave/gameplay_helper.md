# Natural cave integration helper

Editor class: `Oheangbu.EditorTools.WorldMacro.WorldMacroNaturalCaveGameplay`.

Entry: `Execute("apply")` (also public `Apply()`), `Execute("validate")` (also public `Validate()`), `Execute("navigation")`, `Execute("map")`.

Preconditions: World macro playtest Edit scene, generated `Playtest_NaturalCave` root at the old Cave pose, independent upward-facing floor named `Cave_Walkable_Floor`, `Natural_Cave_Floor` or `NaturalCave_Floor`, old obstructing cave shell/colliders disabled. The floor must have one closed top-surface boundary loop after welding at 1mm. Small holes <=8m짼 are rejected only when they form a disconnected island; larger additional loops require explicit map zones. A boundary failure is reported instead of substituting a rectangular approximation.

Apply backs up Playtest.asset, ContentPositions.asset, map data, navigation and the currently saved scene to `NaturalCave/Backups`, then updates only start/inquiry, first neutral encounter, actual start fragment/preview positions and the underground MainPath prefix. The exterior route tail is retained exactly and checked in the integration record. Active old Mine_Work_Basket, Mine_BlastDebris and Blast_Cord are translated by the inquiry delta; inactive archived art is untouched. Spawn starts local(-136,.13,4), inquiry(-131,.13,-2), neutral enemy(-77,.13,5). Local NavMesh is expanded around the new corridor and saved to a separate `.../NaturalCave/Navigation.asset`; agents remain disabled for the established Start-time binding.

Map floor outline is extracted from actual upward triangle boundary edges; map fill uses scan lines over the concave polygon. The existing illustration, world projection and discovery state are preserved. Cave zone Y spans rootY-1 to rootY+4 for walking/jumping feet and excludes mountain roof. Existing map marker follows current StartFeet as the existing validator requires. A persisted `CaveMapOverride.json` is read by a small WorldMapRuntimeBaker hook so a future bake does not restore the archived rectangular Cave.prefab footprint.

SaveSlot is not changed, no user save file is accessed. TerrainRevision becomes `macro-natural-cave-20260915`. Existing Session.RepairProgress safely restores older terrain saves at their registered checkpoint and preserves progress/currency, moving unsupported drops to the checkpoint. Relaunch of a real old save is not claimed tested.

Validation writes `gameplay_validation.txt`: checkpoint capsule support; underground path at 0.5m intervals; inquiry LOS approach; start fragments; NavMesh actor/patrol path; map inside/outside/roof; unchanged exterior route and IDs. This is structural checking, not an actual input walkthrough. Runtime combat/AI, old-save relaunch and appearance remain unverified until executed.

File-only checks run: git diff --check, balanced structural delimiters. No Unity refresh, compile, queue operation, scene mutation, build or screenshot was run by this subagent.
