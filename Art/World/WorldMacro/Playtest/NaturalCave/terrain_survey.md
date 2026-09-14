# Natural cave technical survey — read only, 2026-09-14

## Confirmed cause and current shape

`WorldMacroLandmarkAuthoring.Cave` (line 331 onward) explicitly leaves the original terrain intact and raises a 12m × 34m floor above the highest local terrain sample. Current root is `WorldMacro_Landmarks_Authored/Cave` at approximately (3400.619, 195.674, 1118.860), yaw 30°. Entrance faces local -Z. This is a raised artificial tunnel, not a hole into the mountain.

`WorldMacroPlaytestAssetReuse.CavePanels` (195) makes 5 repeated axial rows, 7m spacing, each containing five flat scan wall panels at 45° angular spacing. That regular cross-section is the cause of the pronounced polygonal appearance. Old `BuildCaveShell` itself uses 24 arc segments but nearly constant inner width and height. Irregular exterior shell does not correct interior proportion.

## Actual terrain evidence and recommended direction

I decoded the existing readable Unity Terrain_052/060 mesh vertex/index channels without loading or modifying Unity. `terrain_samples.json` and `mountain_direction_samples.json` contain barycentrically interpolated heights from the actual saved triangles, not estimates from screenshots. Meshes use world-coordinate vertices, 16m grid, 1024m chunks. Terrain_052 covers this cave, Terrain_060 begins at world Z 1168. Relevant world footprint for those two chunks spans approximately X 3168–3936, Z 144–2192 (outline clips individual triangles).

At local Z 0, ground heights for local X -140/-100/-60/-30/0/+30 are 306.88/244.93/207.40/197.87/194.20/190.60m. At local X 0, local Z 0/40/80/120 terrain is 194.20/193.74/189.45/192.08m. Thus simply extending +Z follows the valley away from the mountain. Turning the interior toward local -X reaches genuine mountain cover rapidly. A chamber centered near local (-100, 0, 0) with floor ~196m would have ~49m terrain overhead, versus only ~11m at local (-60, 0, 0). A curved throat from the existing entrance toward local -X is geographically better than a long freestanding roof mound.

Recommended targeted operation: preserve existing terrain assets and duplicate only intersected chunk meshes; assign the copies to both MeshFilter and MeshCollider of the playable scene. Clip/subdivide only triangles whose terrain crosses the new cave air volume; retain terrain above the roof and all triangles outside the local work volume. For the throat, add a short terrain-conforming rock shoulder blend to close cut edges. Do not remove every triangle in the cave XZ footprint: that would remove the actual mountain roof. Do not raise the entire global height field or call WorldMacroBuilder.Rebuild.

A robust simpler option is to keep the existing entrance floor height and ensure the whole walkable new floor remains above any underlying original terrain until entering a surgically carved mountain throat. Closed irregular inner shell + outer terrain provides actual covered interior. Exterior shell should disappear beneath existing mountain, not sit as a separate dome. Terrain amendments need precise matching border positions and UVs so unchanged chunk edges remain identical.

## Scene geometry that must be archived/deactivated together

- `WorldMacro_Landmarks_Authored/Cave/Hollow_Rock_Shell`: renderer is disabled in last repair, but MeshCollider remains active. Must disable collider to permit widened chambers.
- Same Cave root: `Cave_Back_Collision`, `Cave_Walkable_Floor`, `Cave_Entrance_Stone_Ramp`; named wall LOD children `Wall05B` are old decorative scans. Preserve `Entry`, `ViewTarget` references or reposition intentionally.
- `Playtest_OwnedAssets/Mine_Asset_Lining`: inactive legacy scan set; keep archived inactive.
- `Playtest_OwnedAssets/Mine_Continuous_Asset_Panels`: disable whole group. Its `Material_Batches/Batch_*` are the visible meshes; individual `Rock_Panel_*` renderers are disabled source records. Disabling just the source panels does nothing.
- `Playtest_OwnedAssets/Cave_Visual_Closure`: 1.3x/1.35x enlarged old shell, renderer-only.
- `Playtest_OwnedAssets/Ramp_Scan_Surface`: includes material batches and source parts, tied to old narrow ramp. Preserve only if its geometry stays valid.
- `Playtest_OwnedAssets/Mine_Work_Ambient`, `Mine_Work_Lantern`, `Mine_Work_Basket`: reposition rather than lose investigation support.
- `Playtest_EarlyArt/Mine_Supports`, `Mine_BlastDebris`, `Mine_Workplace`, `Mine_Transport`, `Blast_Cord`, `Exit_Transport_Basket`: combined world-space static geometry. Supports are artificial regular frames and currently lack collision. If changing width/path, archive or reposition/rebuild only these groups. Leave Branch_WorkerTrace, Inn_FuelStore and foliage intact except local placement corrections.
- `Playtest_VisualCorridor/01_TerrainConformedRoutes/Mine_To_Inn`: visible path mesh generated from MainPath but clamped to analytic terrain; any replaced interior/entrance segment should be regenerated locally or masked. Existing exterior route should not be rerouted.

## Owned source reuse and materials

Reduced FBXs already exist under `Assets/_Project/Art/World/WorldMacro/Landmarks/Models/LM_*.fbx`: Wall02A, Wall05B, Ceiling05A, Floor02B, LOD0/1/2. Retain source UVs and use source surface material maps under `Assets/BillemotdonggulLavaTubePack/Material/{MI_Wall02A,MI_Wall05B,MI_Ceiling05A,M_Floor02B}.mat`. LOD imports have placeholder material assignments; resolve real scan material explicitly. Avoid reimporting enormous source FBXs. Floor02B actual triangles: LOD0 14,879 / LOD1 6,944 / LOD2 2,976. Other counts are recorded in `Art/World/WorldMacro/Landmarks/ModelReports/DELIVERY.md`.

Do not stretch each scan across an entire chamber: reuse irregular detail outcrops around a smoothly varying structural cave mesh. Base-map UVs on scans are atlased and cannot be repeated on procedural geometry indiscriminately. `Oheangbu/WorldMacroTexturedSurface` supports BaseMap, BumpMap, AmbientFloor, LightResponse and shadows. Its default AmbientFloor=.4 can make interior too evenly bright; a dedicated cave material copy may reduce this locally. Preserve global exposure/sky/fog, keep only work-lamp and ore motivated lighting.

## Gameplay, saves and map dependencies

- `WorldMacroPlaytestSession.Content`: `WorldMacroPlaytestSO` at `.../Playtest/Playtest.asset`, TerrainRevision currently `macro-first-section-2`. Contains StartFeet, StartYaw, MainPath, BranchPath, Points, enemy spawns, InnCheckpointFeet. MainPath is actual play corridor. Keep original gameplay IDs and one-time completion meanings.
- Current StartFeet (3406.619,195.774,1129.252). `mine_inquiry` at (3406.3513,195.72368,1124.7881). `mine_beast/0` near (3395.619,195.734,1110.200). Larger chamber can preserve these central locations, or update them together with PreviewPoints, PreviewSheet entries, corresponding visual props and new route.
- Saves: Session.RepairProgress uses TerrainRevision mismatch or failed TrySafeFeet to relocate to checkpoint while preserving completed IDs/currency/drops. If relocating spawn/changing floor significantly, update revision deliberately; never reset ledger. TrySafeFeet probes from candidate+1.5m down 4m, slope then capsule and four support rays. Test both mine and inn checkpoints after edits. Do not inadvertently validate a roof as an interior floor using a ray from Y=2200.
- Enemy navigation: `Playtest_Local_Navigation`, NavMeshSurface Volume currently (140,60,160), centered cave.position-cave.forward*38, physics collider geometry, voxel .18, tile128. New -X chambers lie outside that volume. Clone/update navigation asset and local bounds, rebake only this surface. Enemy agents are intentionally saved disabled and bound in Session.Start after NavMesh loads; `WorldMacroPlaytestNavStartupAuthoring` validates three actors/patrol positions. Do not enable agents prematurely in Edit.
- Collection: `WorldMacroCollectionCatalog` includes hardcoded start bundle fallback near mine_inquiry, but actual pickups are saved scene objects. Move the start bundle pickup/anchor with inquiry if needed, preserving BundleId.
- Map: `Assets/_Project/Resources/WorldMap/WorldMapBakedData.asset` has explicit `Zones`; runtime ZoneAt uses Polygon + min/maxY. Existing MapRuntimeGeometry.BuildZones makes a rectangle from Cave landmark courtyard (min28×70), offset localZ -13, floor-4 to floor+12. This must expand to actual irregular cave footprint and include the taller chambers while not identifying the mountain surface as cave.
- Existing full WorldMapRuntimeBaker.Bake reads old `Landmarks/Prefabs/Cave.prefab` and `Playtest/PolishMeshes/Cave_Ramp_Closed.asset`, then makes a convex hull of floor/ramp. It will incorrectly restore old footprint and overwrite map data if called unchanged. Better save a dedicated cave footprint/path asset and update only the baked `cave` zone/DetailLines/DetailPath from new boundary data. Keep outdoor illustration and discovery state. Update bake inputs so later bakes preserve new shape.
- Dressing ground uses precomputed `WorldMacroDressingSheetSO.Cell.Heights`, not physics. If terrain changes around cave, add a targeted preserve/exclusion area or correct those cells and authored foliage positions. Otherwise trees may spawn in cave air or old ground plane. Do not globally re-density/rebuild forest.

## Needed actual checks after authoring

1. Cast roof/floor and lateral collision from sampled walkable chamber points; compare rendered/collision shapes and check no old octagonal collider remains.
2. Every MainPath segment near new cave: capsule clearance, step/grade, floor continuity, entrance threshold. Sweep/sampling is structural diagnosis, not an automatic walk completion claim.
3. Safe spawn/checkpoint, inquiry + fragment approach, one neutral and one fire encounter support/LOS/NavMesh.
4. Cave map switches inside new outline, remains outdoors on mountain roof and at exit; markers align.
5. Still images from outside showing cave entering mountain; long interior, expanded chamber and route-to-exit eye-level views; no labels baked inside images.
6. Shader errors, added triangles/renderers/colliders, memory guard. Full runtime performance remains unverified unless measured in actual Play.

No Unity queue was called, no existing files were changed. This report and two sample JSON files are the only outputs.
