using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Dressing;
using Oheangbu.App.Prologue;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroCompactAuthoring
    {
        [Serializable] sealed class CompactOriginalFields
        {
            public string Id, BundleId;
            public int Ranged, CombatConnected;
            public float DetectionRange, Leash, Speed, PreferredDistance, LeashRadius;
        }
        [Serializable] sealed class CompactOriginalComponent
        { public string type, path; public CompactOriginalFields serialized; }
        [Serializable] sealed class CompactOriginalTransform
        { public long transformId; public string path; public float[] position; public Vector3 localScale; }
        [Serializable] sealed class CompactOriginalSnapshot
        { public string sceneSha256; public CompactOriginalComponent[] components; public CompactOriginalTransform[] transforms; }
        [Serializable] sealed class CompactOriginalSurvey
        { public string scene; public int objects; public BoundRecord[] groups; }
        [Serializable] sealed class CompactOriginalReference
        { public string type, path, componentFileId, propertyPath, sourceGuid, sourceFileId; }
        [Serializable] sealed class CompactOriginalReferenceManifest
        { public string sourceSceneHash; public CompactOriginalReference[] references; }
        [Serializable] sealed class CompactAuditCheck
        { public string id, status, detail; }
        [Serializable] sealed class CompactAuditZone
        {
            public string path, kind, status, detail;
            public Vector3 sourceMin, sourceMax, expectedMin, expectedMax, actualMin, actualMax;
            public float maximumBoundsErrorMetres, maximumSizeErrorMetres;
            public string[] rendererDiagnostics;
        }
        [Serializable] sealed class CompactCampaignStageAudit
        { public string id, trigger, eventKind; public bool implemented; public int reward; public string[] requiredDefeatedIds; }
        [Serializable] sealed class CompactContentAudit
        {
            public string source, target, sourceSaveSlot, compactSaveSlot, sourceRevision, compactRevision, campaignPath, campaignHash;
            public bool sameCampaignReference, sameEconomyReference, sameRuleReference;
            public int encounters, interactions, checkpoints;
            public CompactCampaignStageAudit[] campaignStages;
        }
        [Serializable] sealed class CompactAuditReport
        {
            public string status, scope = "Edit-scene structural audit only. Runtime gameplay, road grades, navigation, vehicle driving, saves and visual approval are not certified.", scene, sourceHash, utc;
            public bool gameplayVerified, sourceSceneUnchanged, originalCloneInputsUnchanged, sceneDirtyBefore, sceneDirtyAfter;
            public int sourceObjects, targetObjects, sourceActors, targetActors, sourceContentPoints, targetContentPoints;
            public int protectedZones, comparedRenderZones, deferredNonrenderZones, resolvedTransformScalesChecked, unresolvedOriginalTransforms;
            public int meshJobs, checkedMeshJobs, scalarFieldsChecked, cloneReferencesChecked;
            public int originalRotationsChecked, originalRotationsUnavailable, groundedRootsWithExpectedOffset, transformsWithExpectedGroundOffset;
            public int expectedSourceReferences, matchedSourceReferences, missingRequiredReferences, mismatchedSourceReferences, unresolvedSourceReferences;
            public CompactAuditCheck[] checks; public CompactAuditZone[] zones; public CompactContentAudit[] content;
            public string[] pending = { "Road longitudinal grade/width and connector support audit", "Separate edit NavMesh/link/actor audit", "Runtime walk, car summon/drive/egress, death, rest and save/continue", "Human inspection of actual EditSceneRender images" };
        }

        static string AuditCompact()
        {
            RequireCompact(); var progress = ReadProgress(); var compression = Compression;
            if (compression == null || compression.Mapping == null) throw new InvalidOperationException("Compact compression map is missing.");
            var scene = SceneManager.GetActiveScene();
            var report = new CompactAuditReport { scene = scene.path, sourceHash = progress.sourceHash, utc = DateTime.UtcNow.ToString("o"), sceneDirtyBefore = scene.isDirty };
            var checks = new List<CompactAuditCheck>();
            void Check(string id, bool pass, string detail) => checks.Add(new CompactAuditCheck { id = id, status = pass ? "PASS" : "FAIL", detail = detail });
            report.sourceSceneUnchanged = File.Exists(SourceScene) && Hash(SourceScene) == progress.sourceHash;
            Check("original_scene_hash", report.sourceSceneUnchanged, SourceScene);
            var pairs = progress.assets ?? Array.Empty<AssetPair>();
            var modifiedInputs = pairs.Where(p => !File.Exists(p.source) || Hash(p.source) != p.hash).Select(p => p.source).ToArray();
            report.originalCloneInputsUnchanged = modifiedInputs.Length == 0;
            Check("original_cloned_asset_hashes", report.originalCloneInputsUnchanged, modifiedInputs.Length == 0 ? pairs.Length + " original asset hashes unchanged" : string.Join(" | ", modifiedInputs));

            var original = JsonUtility.FromJson<CompactOriginalSnapshot>(File.ReadAllText(Path.Combine(Output, "content_audit.json")));
            var survey = JsonUtility.FromJson<CompactOriginalSurvey>(File.ReadAllText(Path.Combine(Output, "scene_survey.json")));
            if (original?.components == null || original.sceneSha256 != progress.sourceHash || survey?.groups == null || survey.scene != SourceScene)
                throw new InvalidOperationException("Original component/bounds snapshots do not belong to the prepared source scene.");
            var transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Where(t => (t.hideFlags & HideFlags.DontSave) == 0).ToArray();
            var byPath = transforms.GroupBy(Hierarchy).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            report.sourceObjects = progress.sourceObjects; report.targetObjects = transforms.Length;
            Check("object_count", report.targetObjects == report.sourceObjects, report.sourceObjects + " source / " + report.targetObjects + " compact transforms including inactive objects");
            var actors = transforms.Select(t => t.GetComponent<PrologueEncounter>()).Where(a => a != null).ToArray();
            var contentPoints = transforms.Select(t => t.GetComponent<WorldMacroContentPoint>()).Where(a => a != null).ToArray();
            var originalActors = original.components.Where(c => c.type == typeof(PrologueEncounter).FullName).ToArray();
            var originalPoints = original.components.Where(c => c.type == typeof(WorldMacroContentPoint).FullName).ToArray();
            report.sourceActors = originalActors.Length; report.targetActors = actors.Length;
            report.sourceContentPoints = originalPoints.Length; report.targetContentPoints = contentPoints.Length;
            Check("actor_id_multiset", CompactSameIds(originalActors.Select(c => c.serialized?.Id), actors.Select(c => c.Id)), report.sourceActors + " source / " + report.targetActors + " compact actors; duplicate multiplicity retained");
            Check("content_id_multiset", CompactSameIds(originalPoints.Select(c => c.serialized?.Id), contentPoints.Select(c => c.Id)), report.sourceContentPoints + " source / " + report.targetContentPoints + " compact content points; duplicate multiplicity retained");
            foreach (var actor in actors)
            {
                var matches = originalActors.Where(a => a.path == Hierarchy(actor.transform) && a.serialized?.Id == actor.Id).ToArray();
                var old = matches.Length == 1 ? matches[0].serialized : null;
                Check("actor_parameters/" + actor.Id, old != null && old.Ranged == (actor.Ranged ? 1 : 0) &&
                    CompactClose(old.DetectionRange, actor.DetectionRange) && CompactClose(old.Leash, actor.Leash) &&
                    CompactClose(old.Speed, actor.Speed) && CompactClose(old.PreferredDistance, actor.PreferredDistance), Hierarchy(actor.transform));
            }
            foreach (var point in contentPoints)
            {
                var matches = originalPoints.Where(a => a.path == Hierarchy(point.transform) && a.serialized?.Id == point.Id).ToArray();
                var old = matches.Length == 1 ? matches[0].serialized : null;
                Check("content_parameters/" + Hierarchy(point.transform), old != null && old.CombatConnected == (point.CombatConnected ? 1 : 0) && CompactClose(old.LeashRadius, point.LeashRadius), point.Id);
            }

            // The offline source transform inventory excludes stripped prefab records.
            // Check resolved records without pretending it covers all 7,172 objects.
            var scaleFailures = new List<string>();
            var sourceRotations = CompactSourceLocalRotations();
            var groundOffsets = CompactGroundOffsets(original, byPath);
            report.groundedRootsWithExpectedOffset = groundOffsets.Count(p => Mathf.Abs(p.Value) > .00001f);
            foreach (var old in original.transforms ?? Array.Empty<CompactOriginalTransform>())
            {
                if (old.position == null || old.position.Length != 3 || string.IsNullOrEmpty(old.path)) continue;
                if (!byPath.TryGetValue(old.path, out var candidates)) { scaleFailures.Add(old.path + " missing"); continue; }
                var expected = compression.Map(new Vector3(old.position[0], old.position[1], old.position[2]));
                string groundedRoot = CompactGroundRootPath(old.path);
                if (groundedRoot != null && groundOffsets.TryGetValue(groundedRoot, out float offset)) { expected.y += offset; if (Mathf.Abs(offset) > .00001f) report.transformsWithExpectedGroundOffset++; }
                var current = candidates.OrderBy(t => (t.position - expected).sqrMagnitude).First();
                float positionError = Vector3.Distance(current.position, expected), scaleError = Vector3.Distance(current.localScale, old.localScale);
                if (positionError > .025f || scaleError > .0001f)
                    scaleFailures.Add(old.path + " position error=" + positionError.ToString("R", CultureInfo.InvariantCulture) + " m, local scale error=" + scaleError.ToString("R", CultureInfo.InvariantCulture));
                if (sourceRotations.TryGetValue(old.transformId, out var rotation))
                { report.originalRotationsChecked++; if (CompactRotationError(current.localRotation, rotation) > .00001f) scaleFailures.Add(old.path + " original local rotation changed"); }
                else report.originalRotationsUnavailable++;
                report.resolvedTransformScalesChecked++;
            }
            report.unresolvedOriginalTransforms = Math.Max(0, report.sourceObjects - report.resolvedTransformScalesChecked);
            Check("resolved_transform_positions_and_scales", scaleFailures.Count == 0, scaleFailures.Count == 0 ? report.resolvedTransformScalesChecked + " resolved source records; " + report.originalRotationsChecked + " exact source local rotations; " + report.groundedRootsWithExpectedOffset + " environment roots / " + report.transformsWithExpectedGroundOffset + " descendants use the exact authorized grounding Y delta. Remaining prefab/RectTransform records not claimed" : string.Join(" | ", scaleFailures.Take(30)));

            var zoneRows = new List<CompactAuditZone>();
            foreach (var zone in progress.zones ?? Array.Empty<Zone>())
            {
                var row = new CompactAuditZone { path = zone.path, kind = zone.kind };
                zoneRows.Add(row);
                if (!byPath.TryGetValue(zone.path, out var targets) || targets.Length != 1)
                { row.status = "FAIL"; row.detail = "Protected path is missing or ambiguous."; continue; }
                if (zone.kind != "renderBounds")
                { row.status = "DEFERRED"; row.detail = "Path retained; encounter leash/navigation volume requires its own audit, not renderer bounds."; report.deferredNonrenderZones++; continue; }
                var sources = survey.groups.Where(g => g.path == zone.path).ToArray();
                if (sources.Length != 1) { row.status = "FAIL"; row.detail = "Original renderer bounds are missing or ambiguous."; continue; }
                var source = sources[0]; row.sourceMin = source.min; row.sourceMax = source.max;
                row.expectedMin = compression.Map(source.min); row.expectedMax = compression.Map(source.max);
                var renderers = targets[0].GetComponentsInChildren<Renderer>(true).Where(r => r.GetComponentInParent<Canvas>() == null).ToArray();
                if (renderers.Length == 0) { row.status = "FAIL"; row.detail = "Protected render group lost its renderers."; continue; }
                var actual = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) actual.Encapsulate(renderer.bounds);
                row.actualMin = actual.min; row.actualMax = actual.max;
                row.maximumBoundsErrorMetres = Mathf.Max(CompactMaximumAxisError(row.actualMin, row.expectedMin), CompactMaximumAxisError(row.actualMax, row.expectedMax));
                row.maximumSizeErrorMetres = CompactMaximumAxisError(actual.size, source.max - source.min);
                float mappedSizeError = CompactMaximumAxisError(row.expectedMax - row.expectedMin, source.max - source.min);
                row.status = row.maximumBoundsErrorMetres <= .025f && row.maximumSizeErrorMetres <= .025f && mappedSizeError <= .025f && renderers.Length == source.renderers ? "PASS" : "FAIL";
                row.detail = "25 mm tolerance; unchanged renderer count " + source.renderers + "/" + renderers.Length + "; mapped protected span error=" + mappedSizeError;
                if (row.status == "FAIL") row.rendererDiagnostics = renderers.Select(r => Hierarchy(r.transform) + " pivot=" + r.transform.position.ToString("R", CultureInfo.InvariantCulture) + " min=" + r.bounds.min.ToString("R", CultureInfo.InvariantCulture) + " max=" + r.bounds.max.ToString("R", CultureInfo.InvariantCulture) + " mesh=" + AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>()?.sharedMesh)).ToArray();
                report.comparedRenderZones++;
            }
            report.protectedZones = zoneRows.Count; report.zones = zoneRows.ToArray();
            Check("protected_zone_render_bounds", zoneRows.All(z => z.status != "FAIL"), report.comparedRenderZones + " rigid render zones checked; " + report.deferredNonrenderZones + " nonrender zones explicitly deferred");

            report.meshJobs = progress.meshes?.Length ?? 0;
            var meshFailures = new List<string>();
            foreach (var job in progress.meshes ?? Array.Empty<MeshJob>())
            {
                if (string.IsNullOrEmpty(job.result) || !job.result.StartsWith(Folder + "/Meshes/", StringComparison.Ordinal) || !byPath.TryGetValue(job.path, out var candidates) || candidates.Length != 1)
                { meshFailures.Add(job.path + " has no unique completed compact mesh"); continue; }
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(job.result); var t = candidates[0];
                var filter = t.GetComponent<MeshFilter>(); var collider = t.GetComponent<MeshCollider>();
                if (mesh == null || job.filter && (filter == null || filter.sharedMesh != mesh) || job.collider && (collider == null || collider.sharedMesh != mesh))
                    meshFailures.Add(job.path + " renderer/collider mesh does not match completed job output");
                else report.checkedMeshJobs++;
            }
            if (File.Exists(CompactInnManifestPath)) Check("inn_pivot_mesh_repair", CompactInnRepairValid(out string innDetail), innDetail);
            Check("generated_mesh_and_collider_references", meshFailures.Count == 0 && report.checkedMeshJobs == report.meshJobs, meshFailures.Count == 0 ? report.checkedMeshJobs + " job outputs bound consistently" : string.Join(" | ", meshFailures.Take(30)));

            var originals = new HashSet<Object>(pairs.Select(p => AssetDatabase.LoadMainAssetAtPath(p.source)).Where(o => o != null));
            var objects = transforms.SelectMany(t => t.GetComponents<MonoBehaviour>()).Where(c => c != null).Cast<Object>()
                .Concat(pairs.Select(p => AssetDatabase.LoadMainAssetAtPath(p.target)).Where(o => o != null));
            var referenceLeaks = new List<string>();
            foreach (var obj in objects)
            {
                using (var serialized = new SerializedObject(obj))
                {
                    var property = serialized.GetIterator(); bool enter = true;
                    while (property.Next(enter))
                    {
                        enter = true;
                        if (property.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            enter = false; report.cloneReferencesChecked++;
                            if (property.objectReferenceValue != null && originals.Contains(property.objectReferenceValue)) referenceLeaks.Add(obj.name + ":" + property.propertyPath);
                        }
                        else if (property.propertyType == SerializedPropertyType.Vector3 || property.propertyType == SerializedPropertyType.Vector2 || property.propertyType == SerializedPropertyType.Quaternion || property.propertyType == SerializedPropertyType.Bounds) enter = false;
                        else if (property.isArray && property.arraySize > 10000 && property.arrayElementType != "PPtr<$Object>") enter = false;
                    }
                }
            }
            Check("no_original_mutable_clone_references", referenceLeaks.Count == 0, referenceLeaks.Count == 0 ? report.cloneReferencesChecked + " object-reference fields inspected" : string.Join(" | ", referenceLeaks.Take(30)));
            AuditCompactSourceReferences(transforms, pairs, progress.sourceHash, report, checks);
            var scalarFailures = new List<string>(); var contentRows = new List<CompactContentAudit>();
            var originalSlots = new HashSet<string>(pairs.Select(p => AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(p.source)).Where(p => p != null).Select(p => p.SaveSlot), StringComparer.Ordinal);
            foreach (var pair in pairs)
            {
                var source = AssetDatabase.LoadMainAssetAtPath(pair.source); var target = AssetDatabase.LoadMainAssetAtPath(pair.target);
                if (source is WorldMacroPlaytestSO || source is WorldMacroOpeningProfileSO || source is WorldMacroContentSheetSO || source is WorldMacroLandmarkSheetSO || source is WorldMacroVisualCorridorSO)
                    report.scalarFieldsChecked += CompareCompactGameplayScalars(source, target, scalarFailures);
                if (source is WorldMacroPlaytestSO old && target is WorldMacroPlaytestSO compact)
                {
                    var row = new CompactContentAudit { source = pair.source, target = pair.target, sourceSaveSlot = old.SaveSlot, compactSaveSlot = compact.SaveSlot,
                        sourceRevision = old.TerrainRevision, compactRevision = compact.TerrainRevision, sameCampaignReference = old.Campaign == compact.Campaign,
                        sameEconomyReference = old.Economy == compact.Economy, sameRuleReference = old.TestRules == compact.TestRules,
                        encounters = compact.Encounters?.Length ?? 0, interactions = compact.Points?.Length ?? 0, checkpoints = compact.Checkpoints?.Length ?? 0 };
                    if (compact.Campaign != null)
                    {
                        row.campaignPath = AssetDatabase.GetAssetPath(compact.Campaign); row.campaignHash = Hash(row.campaignPath);
                        if (compact.Campaign.Stages != null)
                            row.campaignStages = compact.Campaign.Stages.Where(s => s != null).Select(s => new CompactCampaignStageAudit { id = s.Id, trigger = s.TriggerId, eventKind = s.Event.ToString(), implemented = s.Implemented, reward = s.TongboReward, requiredDefeatedIds = s.RequiredDefeatedIds }).ToArray();
                    }
                    contentRows.Add(row);
                    Check("compact_save_identity/" + pair.target, !string.IsNullOrWhiteSpace(compact.SaveSlot) && !originalSlots.Contains(compact.SaveSlot) &&
                        !string.IsNullOrWhiteSpace(compact.TerrainRevision) && old.TerrainRevision != compact.TerrainRevision, compact.SaveSlot + " / " + compact.TerrainRevision + "; original save files were not opened");
                    Check("shared_campaign_economy_rules/" + pair.target, row.sameCampaignReference && row.sameEconomyReference && row.sameRuleReference,
                        "Campaign stage flags/rewards/requirements remain on the same shared original profile; scalar rules unchanged");
                    Check("authored_content_ids/" + pair.target, CompactSameIds(old.Encounters.Select(e => e.Id), compact.Encounters.Select(e => e.Id)) &&
                        CompactSameIds(old.Points.Select(e => e.Id), compact.Points.Select(e => e.Id)) && CompactSameIds(old.Checkpoints.Select(e => e.Id), compact.Checkpoints.Select(e => e.Id)), "Encounter, interaction and checkpoint ID multisets retained");
                }
                if (target is WorldMapBakedDataSO map)
                {
                    var bounds = compression.Mapping.TargetBounds;
                    Check("map_bounds_and_owned_images/" + pair.target, map.IsUsable && Vector2.Distance(map.BoundsMin, new Vector2(bounds.min.x, bounds.min.z)) < .001f &&
                        Vector2.Distance(map.BoundsMax, new Vector2(bounds.max.x, bounds.max.z)) < .001f && CompactOwnedMapImage(map.BaseMap) &&
                        (map.IllustratedMap == null || CompactOwnedMapImage(map.IllustratedMap)) && (map.RegionTiles ?? Array.Empty<WorldMapRegionTile>()).All(t => t != null && CompactOwnedMapImage(t.Texture)), "Usable compact bounds and private map texture family");
                }
            }
            report.content = contentRows.ToArray();
            Check("gameplay_asset_scalars", scalarFailures.Count == 0, scalarFailures.Count == 0 ? report.scalarFieldsChecked + " exact noncoordinate fields checked; only names/save identity excluded" : string.Join(" | ", scalarFailures.Take(30)));
            report.sceneDirtyAfter = scene.isDirty;
            Check("audit_preserves_scene_dirty_state", report.sceneDirtyBefore == report.sceneDirtyAfter, "No scene save, source scene load or authored mutation performed by this audit");
            report.checks = checks.ToArray(); report.status = checks.Any(c => c.status == "FAIL") ? "EDIT_AUDIT_FINDINGS" : "EDIT_STRUCTURE_VERIFIED_RUNTIME_PENDING";
            string json = JsonUtility.ToJson(report, true); File.WriteAllText(Path.Combine(Output, "compact_audit.json"), json);
            return json;
        }


        static Dictionary<long, Quaternion> CompactSourceLocalRotations()
        {
            var result = new Dictionary<long, Quaternion>(); long transform = 0;
            var header = new Regex(@"^--- !u!4 &(-?\d+)(?: |$)", RegexOptions.CultureInvariant);
            var rotation = new Regex(@"^  m_LocalRotation: \{x: ([^,]+), y: ([^,]+), z: ([^,]+), w: ([^}]+)\}", RegexOptions.CultureInvariant);
            foreach (var line in File.ReadLines(SourceScene))
            {
                if (line.StartsWith("---", StringComparison.Ordinal)) { var match = header.Match(line); transform = match.Success ? long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0; }
                else if (transform != 0 && line.StartsWith("  m_LocalRotation:", StringComparison.Ordinal))
                { var match = rotation.Match(line); if (match.Success) result[transform] = new Quaternion(float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), float.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture), float.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture)); }
            }
            return result;
        }
        static string CompactGroundRootPath(string path)
        {
            foreach (string parent in new[] { "Playtest_VisualCorridor/02_MountainAndForest/", "Playtest_EarlyArt/" })
                if (path.StartsWith(parent, StringComparison.Ordinal)) { int end = path.IndexOf('/', parent.Length); return end < 0 ? path : path.Substring(0, end); }
            return null;
        }
        static Dictionary<string, float> CompactGroundOffsets(CompactOriginalSnapshot original, Dictionary<string, Transform[]> byPath)
        {
            var offsets = new Dictionary<string, float>(StringComparer.Ordinal);
            if (!File.Exists(GradeProgressPath) || Grade == null) return offsets;
            var state = JsonUtility.FromJson<GradeProgress>(File.ReadAllText(GradeProgressPath));
            bool finalizedFromOriginal = false;
            if (File.Exists(AttachmentReportPath))
            {
                var attachments = JsonUtility.FromJson<AttachmentReport>(File.ReadAllText(AttachmentReportPath));
                finalizedFromOriginal = attachments != null &&
                    (attachments.status == "FINAL_GRADE_ATTACHMENTS_REBUILT_FROM_ORIGINAL" || attachments.status == "FINAL_GRADE_ATTACHMENT_FINDINGS") &&
                    attachments.sourceSceneUnchanged && attachments.originalAssetsUnchanged && attachments.propBasesPreserved &&
                    attachments.sourceSceneHash == original.sceneSha256 && attachments.gradeHash == Hash(AssetDatabase.GetAssetPath(Grade)) &&
                    attachments.mappingHash == Hash(Folder + "/Compression.asset") &&
                    (attachments.reliefHash ?? "") == (Grade.Relief == null ? "" : Hash(AssetDatabase.GetAssetPath(Grade.Relief)));
            }
            // Revised terrain-only grading does not imply that attachments were moved.
            // Permit its exact Y deltas only after a matching completed source-restoration receipt.
            if (state.phase != "GRADED_GEOMETRY_READY_FOR_PHYSICAL_AUDIT" && !finalizedFromOriginal) return offsets;
            var geo = ReadProgress().assets.Select(a => AssetDatabase.LoadMainAssetAtPath(a.target)).OfType<WorldMacroSheetSO>().Single();
            if (geo.CompressionSource == null) throw new InvalidOperationException("Grounding comparison requires the original geographic sampler.");
            foreach (var group in original.transforms.Where(t => t.position != null && t.position.Length == 3 && CompactGroundRootPath(t.path) == t.path).GroupBy(t => t.path, StringComparer.Ordinal))
            {
                if (group.Count() != 1 || !byPath.TryGetValue(group.Key, out var roots) || roots.Length != 1 || roots[0].GetComponent<EarlyRegionFoliage>() != null) continue;
                var old = group.Single(); var position = Compression.Map(new Vector3(old.position[0], old.position[1], old.position[2])); var sourcePosition = Compression.Inverse(position);
                float source = WorldMacroTerrain.SurfaceHeight(geo.CompressionSource, sourcePosition.x, sourcePosition.z);
                float offset = Mathf.Abs(position.y - source) > 8 || Grade.IsProtected(position.x, position.z) ? 0 : Grade.Height(position.x, position.z, source) - source;
                offsets.Add(group.Key, offset);
            }
            return offsets;
        }

        const string CompactInnPath = "WorldMacro_AuthoredGeography/03_SettlementAndLandmark_Massing/Inn";
        static readonly string[] CompactInnNames = { "Polish_Gable_Front", "Polish_Gable_Back", "Polish_Inn_Threshold" };
        static readonly string[] CompactInnSourceGuids = { "082c303383c06f541a303eef9baf6772", "84b9a585a9655c140a847a1f0e7b23f8", "088ff83753f42eb4497d59f3b043da95" };
        static string CompactInnManifestPath => Path.Combine(Output, "inn_pivot_repair.json");
        [Serializable] sealed class CompactInnMeshRepair
        {
            public string path, source, sourceGuid, sourceHash, result, resultHash;
            public long sourceFileId;
            public int vertices;
            public Vector3 originalPivot, rigidWorldDelta, mappedPivotDelta, localVertexOffset;
        }
        [Serializable] sealed class CompactInnRepairReport
        {
            public string status, sourceSceneHash, mappingHash;
            public bool sourceSceneUnchanged, originalMeshesUnchanged;
            public CompactInnMeshRepair[] meshes;
        }
        static string CompactSaveInnManifest(CompactInnRepairReport value)
        {
            string json = JsonUtility.ToJson(value, true), temporary = CompactInnManifestPath + ".tmp";
            File.WriteAllText(temporary, json); if (File.Exists(CompactInnManifestPath)) File.Replace(temporary, CompactInnManifestPath, null); else File.Move(temporary, CompactInnManifestPath); return json;
        }
        public static string RepairInnPivots()
        {
            RequireCompact(); if (Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("System commit >=85%; no Inn mesh repair started.");
            var progress = ReadProgress(); if (Hash(SourceScene) != progress.sourceHash) throw new InvalidOperationException("Original scene hash changed.");
            CompactInnRepairReport report;
            if (File.Exists(CompactInnManifestPath)) report = JsonUtility.FromJson<CompactInnRepairReport>(File.ReadAllText(CompactInnManifestPath));
            else
            {
                var original = JsonUtility.FromJson<CompactOriginalSnapshot>(File.ReadAllText(Path.Combine(Output, "content_audit.json")));
                var survey = JsonUtility.FromJson<CompactOriginalSurvey>(File.ReadAllText(Path.Combine(Output, "scene_survey.json")));
                if (original.sceneSha256 != progress.sourceHash || survey.scene != SourceScene) throw new InvalidOperationException("Original Inn evidence is stale.");
                var bounds = survey.groups.Single(g => g.path == CompactInnPath); var center = (bounds.min + bounds.max) * .5f; var rigidDelta = Compression.Map(center) - center;
                if (CompactMaximumAxisError(Compression.Map(bounds.max) - Compression.Map(bounds.min), bounds.max - bounds.min) > .025f) throw new InvalidOperationException("Inn rendered footprint is not protected at full size.");
                var rows = new List<CompactInnMeshRepair>();
                foreach (string name in CompactInnNames)
                {
                    string path = CompactInnPath + "/" + name; var t = Find(path); var old = original.transforms.Single(v => v.path == path);
                    if (t == null || old.position == null || old.position.Length != 3) throw new InvalidOperationException("Original Inn part missing: " + path);
                    var oldPivot = new Vector3(old.position[0], old.position[1], old.position[2]);
                    if (Vector3.Distance(t.position, Compression.Map(oldPivot)) > .025f || Vector3.Distance(t.localScale, old.localScale) > .0001f) throw new InvalidOperationException("Inn transform differs from the original mapping: " + path);
                    var filter = t.GetComponent<MeshFilter>(); var collider = t.GetComponent<MeshCollider>(); var source = filter?.sharedMesh;
                    if (source == null || collider == null || collider.sharedMesh != source) throw new InvalidOperationException("Inn renderer/collider must share the original generated mesh: " + path);
                    string asset = AssetDatabase.GetAssetPath(source);
                    if (!asset.StartsWith("Assets/_Project/", StringComparison.Ordinal) || asset.StartsWith(Folder + "/", StringComparison.Ordinal) || EditorUtility.IsDirty(source) || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId)) throw new InvalidOperationException("Expected a saved, original owned Inn mesh: " + path);
                    if (guid != CompactInnSourceGuids[Array.IndexOf(CompactInnNames, name)] || localId != 4300000) throw new InvalidOperationException("Inn mesh is not the exact original YAML GUID/subasset: " + path);
                    string result = Folder + "/Meshes/InnPivot_" + guid + "_" + name + ".asset";
                    if (AssetDatabase.LoadMainAssetAtPath(result) != null) throw new InvalidOperationException("Unrecorded Inn output already exists: " + result);
                    var pivotDelta = Compression.Map(oldPivot) - oldPivot;
                    rows.Add(new CompactInnMeshRepair { path = path, source = asset, sourceGuid = guid, sourceFileId = localId, sourceHash = Hash(asset), result = result, vertices = source.vertexCount,
                        originalPivot = oldPivot, rigidWorldDelta = rigidDelta, mappedPivotDelta = pivotDelta, localVertexOffset = t.InverseTransformVector(rigidDelta - pivotDelta) });
                }
                report = new CompactInnRepairReport { status = "PREPARED", sourceSceneHash = progress.sourceHash, mappingHash = Hash(Folder + "/Compression.asset"), meshes = rows.ToArray() };
                CompactSaveInnManifest(report);
            }
            if (report.meshes == null || report.meshes.Length != 3 || report.sourceSceneHash != progress.sourceHash || report.mappingHash != Hash(Folder + "/Compression.asset")) throw new InvalidOperationException("Inn repair manifest does not match this mapping.");
            foreach (var row in report.meshes)
            {
                if (Hash(row.source) != row.sourceHash || AssetDatabase.GUIDToAssetPath(row.sourceGuid) != row.source) throw new InvalidOperationException("Original Inn mesh identity/hash changed: " + row.path);
                var originalMesh = AssetDatabase.LoadAssetAtPath<Mesh>(row.source); var sourceVertices = originalMesh.vertices;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(originalMesh, out string guid, out long localId) || guid != row.sourceGuid || localId != row.sourceFileId || sourceVertices.Length != row.vertices) throw new InvalidOperationException("Original Inn mesh subasset changed: " + row.path);
                var t = Find(row.path); var filter = t.GetComponent<MeshFilter>(); var collider = t.GetComponent<MeshCollider>();
                var clone = AssetDatabase.LoadAssetAtPath<Mesh>(row.result);
                if (filter == null || collider == null || filter.sharedMesh != originalMesh && filter.sharedMesh != clone || collider.sharedMesh != originalMesh && collider.sharedMesh != clone) throw new InvalidOperationException("Unexpected Inn mesh binding: " + row.path);
                if (clone == null)
                {
                    clone = Object.Instantiate(originalMesh); clone.name = originalMesh.name + "_CompactRigidPivot";
                    for (int i = 0; i < sourceVertices.Length; i++) sourceVertices[i] += row.localVertexOffset;
                    clone.vertices = sourceVertices; clone.RecalculateBounds(); AssetDatabase.CreateAsset(clone, row.result); AssetDatabase.SaveAssetIfDirty(clone);
                }
                else
                {
                    var actual = clone.vertices; if (actual.Length != sourceVertices.Length) throw new InvalidOperationException("Unexpected Inn repaired vertex count.");
                    for (int i = 0; i < actual.Length; i++) if (Vector3.Distance(actual[i], sourceVertices[i] + row.localVertexOffset) > .0001f) throw new InvalidOperationException("Existing Inn repaired vertices differ from their exact rigid offset.");
                }
                filter.sharedMesh = clone; collider.sharedMesh = null; collider.sharedMesh = clone; row.resultHash = Hash(row.result); CompactSaveInnManifest(report);
            }
            Physics.SyncTransforms(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            report.sourceSceneUnchanged = Hash(SourceScene) == report.sourceSceneHash; report.originalMeshesUnchanged = report.meshes.All(m => Hash(m.source) == m.sourceHash);
            report.status = report.sourceSceneUnchanged && report.originalMeshesUnchanged ? "INN_THREE_MESH_RIGID_PIVOTS_REPAIRED" : "FAIL_ORIGINAL_HASH_CHANGED"; return CompactSaveInnManifest(report);
        }
        static bool CompactInnRepairValid(out string detail)
        {
            var report = JsonUtility.FromJson<CompactInnRepairReport>(File.ReadAllText(CompactInnManifestPath)); var failures = new List<string>();
            if (report?.meshes == null || report.meshes.Length != 3) { detail = "Inn repair manifest must contain exactly the three known pivot-offset meshes."; return false; }
            if (Hash(SourceScene) != report.sourceSceneHash || Hash(Folder + "/Compression.asset") != report.mappingHash) failures.Add("Source scene/mapping identity changed");
            foreach (var row in report.meshes)
            {
                var t = Find(row.path); var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(row.result);
                if (!File.Exists(row.source) || Hash(row.source) != row.sourceHash || AssetDatabase.GUIDToAssetPath(row.sourceGuid) != row.source) failures.Add(row.path + " original mesh changed");
                if (mesh == null || !File.Exists(row.result) || Hash(row.result) != row.resultHash || mesh.vertexCount != row.vertices || t == null || t.GetComponent<MeshFilter>()?.sharedMesh != mesh || t.GetComponent<MeshCollider>()?.sharedMesh != mesh) failures.Add(row.path + " repaired renderer/collider mesh differs");
            }
            detail = failures.Count == 0 ? "Three private meshes retain original vertex counts, exact planned rigid offsets, saved hashes and shared renderer/collider bindings; original mesh hashes unchanged." : string.Join(" | ", failures);
            return failures.Count == 0;
        }

        static float CompactRotationError(Quaternion a, Quaternion b)
        { var va = new Vector4(a.x, a.y, a.z, a.w); var vb = new Vector4(b.x, b.y, b.z, b.w); return Mathf.Min(Vector4.Distance(va, vb), Vector4.Distance(va, -vb)); }
        static bool CompactSameIds(IEnumerable<string> a, IEnumerable<string> b) => a.OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(b.OrderBy(s => s, StringComparer.Ordinal));
        static bool CompactClose(float a, float b) => Mathf.Abs(a - b) <= .0001f;
        static float CompactMaximumAxisError(Vector3 a, Vector3 b) { var d = a - b; return Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z)); }
        static bool CompactOwnedMapImage(Texture2D texture) => texture != null && AssetDatabase.GetAssetPath(texture).StartsWith(Folder + "/Map/", StringComparison.Ordinal);

        static void AuditCompactSourceReferences(Transform[] transforms, AssetPair[] pairs, string sourceHash, CompactAuditReport report, List<CompactAuditCheck> checks)
        {
            // This manifest was derived from nonzero original YAML references only.
            // Original optional nulls (for example encounter.Session) are not required.
            string manifestPath = Path.Combine(Output, "original_reference_manifest.json");
            var manifest = File.Exists(manifestPath) ? JsonUtility.FromJson<CompactOriginalReferenceManifest>(File.ReadAllText(manifestPath)) : null;
            if (manifest?.references == null || manifest.sourceSceneHash != sourceHash)
            {
                checks.Add(new CompactAuditCheck { id = "original_required_references", status = "FAIL", detail = "Missing or stale original_reference_manifest.json; null-reference coverage is not claimed." });
                return;
            }
            var components = transforms.SelectMany(t => t.GetComponents<Component>()).Where(c => c != null).ToArray();
            var localIds = components.GroupBy(c => c.GetType().FullName + "|" + GlobalObjectId.GetGlobalObjectIdSlow(c).targetObjectId)
                .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            var paths = components.GroupBy(c => c.GetType().FullName + "|" + Hierarchy(c.transform))
                .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            var clonePaths = pairs.ToDictionary(p => p.source, p => p.target, StringComparer.Ordinal);
            var missing = new List<string>(); var mismatch = new List<string>(); var unresolved = new List<string>();
            int missingScripts = transforms.Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            foreach (var expected in manifest.references)
            {
                report.expectedSourceReferences++;
                string label = expected.path + " (" + expected.type + ")." + expected.propertyPath;
                Component component = null;
                if (localIds.TryGetValue(expected.type + "|" + expected.componentFileId, out var idMatches) && idMatches.Length == 1) component = idMatches[0];
                else if (paths.TryGetValue(expected.type + "|" + expected.path, out var pathMatches) && pathMatches.Length == 1) component = pathMatches[0];
                if (component == null) { unresolved.Add(label + " component missing or ambiguous"); continue; }
                using (var serialized = new SerializedObject(component))
                {
                    var property = serialized.FindProperty(expected.propertyPath);
                    if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                    { unresolved.Add(label + " reference field missing or schema changed"); continue; }
                    var actual = property.objectReferenceValue;
                    if (actual == null) { missing.Add(label); continue; }
                    if (!string.IsNullOrEmpty(expected.sourceGuid))
                    {
                        string originalPath = AssetDatabase.GUIDToAssetPath(expected.sourceGuid);
                        if (!string.IsNullOrEmpty(originalPath) && clonePaths.TryGetValue(originalPath, out string compactPath))
                        {
                            if (AssetDatabase.GetAssetPath(actual) != compactPath) { mismatch.Add(label + " must reference " + compactPath); continue; }
                        }
                        else if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(actual, out string actualGuid, out long actualFileId) || actualGuid != expected.sourceGuid || actualFileId.ToString(System.Globalization.CultureInfo.InvariantCulture) != expected.sourceFileId)
                        { mismatch.Add(label + " shared asset identity changed"); continue; }
                    }
                    else
                    {
                        // Prefab instance references can expose a different global object
                        // ID from their YAML stripped ID. Count non-null wiring here; the
                        // copied object/content ID and hierarchy audits cover the targets.
                        var owner = actual is Component c ? c.gameObject : actual as GameObject;
                        if (owner == null || owner.scene != SceneManager.GetActiveScene())
                        { mismatch.Add(label + " no longer references an object in the compact scene"); continue; }
                    }
                    report.matchedSourceReferences++;
                }
            }
            report.missingRequiredReferences = missing.Count; report.mismatchedSourceReferences = mismatch.Count; report.unresolvedSourceReferences = unresolved.Count;
            checks.Add(new CompactAuditCheck { id = "missing_scene_scripts", status = missingScripts == 0 ? "PASS" : "FAIL", detail = missingScripts + " missing MonoBehaviour scripts" });
            checks.Add(new CompactAuditCheck { id = "original_required_references", status = missing.Count + mismatch.Count + unresolved.Count == 0 ? "PASS" : "FAIL",
                detail = report.matchedSourceReferences + "/" + report.expectedSourceReferences + " source nonzero references retained; " + missing.Count + " null, " + mismatch.Count + " wrong target, " + unresolved.Count + " unresolved. Asset identities checked exactly; scene wiring checked non-null and scene ownership. " +
                    string.Join(" | ", missing.Select(s => "NULL " + s).Concat(mismatch).Concat(unresolved).Take(40)) });
        }

        static int CompareCompactGameplayScalars(Object source, Object target, List<string> failures)
        {
            if (target == null || source.GetType() != target.GetType()) { failures.Add(source.name + " missing/wrong clone type"); return 0; }
            int count = 0;
            using (var a = new SerializedObject(source)) using (var b = new SerializedObject(target))
            {
                var p = a.GetIterator(); bool enter = true;
                while (p.Next(enter))
                {
                    enter = true;
                    // Coordinate polylines may be densified without changing authored IDs/events.
                    // Skip only Vector2/Vector3 arrays; all record/ID/event array sizes remain exact.
                    if (p.isArray && (p.arrayElementType == "Vector2f" || p.arrayElementType == "Vector3f" || p.arrayElementType == "Vector2" || p.arrayElementType == "Vector3" ||
                        p.arraySize > 0 && (p.GetArrayElementAtIndex(0).propertyType == SerializedPropertyType.Vector2 || p.GetArrayElementAtIndex(0).propertyType == SerializedPropertyType.Vector3)))
                    { enter = false; continue; }
                    if (p.propertyType == SerializedPropertyType.Vector2 || p.propertyType == SerializedPropertyType.Vector3 || p.propertyType == SerializedPropertyType.Vector4 || p.propertyType == SerializedPropertyType.Quaternion || p.propertyType == SerializedPropertyType.Bounds || p.propertyType == SerializedPropertyType.Rect)
                    { enter = false; continue; }
                    if (p.propertyType == SerializedPropertyType.ObjectReference) { enter = false; continue; }
                    if (p.propertyPath == "m_Name" || source is WorldMacroPlaytestSO && (p.propertyPath == "SaveSlot" || p.propertyPath == "TerrainRevision")) { enter=false; continue; }
                    var other = b.FindProperty(p.propertyPath);
                    if (other == null || other.propertyType != p.propertyType) { failures.Add(source.name + ":" + p.propertyPath + " schema differs"); continue; }
                    bool? same = null;
                    switch (p.propertyType)
                    {
                        case SerializedPropertyType.Boolean: same = p.boolValue == other.boolValue; break;
                        case SerializedPropertyType.Integer: same = p.longValue == other.longValue; break;
                        case SerializedPropertyType.ArraySize: case SerializedPropertyType.Character: same = p.intValue == other.intValue; break;
                        case SerializedPropertyType.Enum: same = p.enumValueIndex == other.enumValueIndex; break;
                        case SerializedPropertyType.Float: same = p.doubleValue.Equals(other.doubleValue); break;
                        case SerializedPropertyType.String: same = p.stringValue == other.stringValue; enter=false; break;
                    }
                    if (same.HasValue) { count++; if (!same.Value) failures.Add(source.name + ":" + p.propertyPath + " scalar changed"); }
                }
            }
            return count;
        }

        [Serializable] sealed class CompactCaptureReport
        {
            public string status, label = "EditSceneRender", view, image, imageHash, scene, poseSource, utc;
            public string limitation = "Actual edit-scene camera render, not native runtime Game View. No walking, gameplay, performance or visual approval claim. Procedural preview caches may populate normally while rendering.";
            public Vector3 eye, target; public float fieldOfView, commitRatio, meanLuminance, luminanceDeviation, blackFraction, transparentFraction;
            public bool runtimeVerified, sceneDirtyBefore, sceneDirtyAfter, sourceSceneUnchanged;
            public int width = 1920, height = 1080;
        }
        static bool compactCaptureRunning;

        static string CaptureCompact(string view,bool terrainDraft=false)
        {
            RequireCompact();
            if (compactCaptureRunning) throw new InvalidOperationException("Only one compact capture camera may run at a time.");
            if (view != "overview" && view != "mine" && view != "inn" && view != "capital") throw new ArgumentException("Capture view must be overview, mine, inn or capital.");
            var progress = ReadProgress();
            if (progress.meshes == null || progress.completedMeshes != progress.meshes.Length || progress.phase == "PREPARED" || progress.phase == "GEOMETRY" || progress.phase == "GEOMETRY_COMPLETE")
                throw new InvalidOperationException("Finish compact geometry and coordinate stages before capture.");
            if (!File.Exists(CompactMapReportPath)) throw new InvalidOperationException("Finish compact map generation before capture.");
            var scene = SceneManager.GetActiveScene();
            var dressings = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldMacroDressingRenderer>(true)).ToArray();
            if (!terrainDraft && dressings.Any(d => d.Sheet == null || d.Sheet.Cells == null || d.Sheet.Cells.Length == 0 || d.Sheet.SourceFingerprint == "COMPACT_REBAKE_PENDING"))
                throw new InvalidOperationException("Finish compact dressing before capture.");
            var sourceCamera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).FirstOrDefault(c => c.GetComponent<UniversalAdditionalCameraData>()?.renderType != CameraRenderType.Overlay && c.GetComponentInParent<Canvas>() == null);
            if (sourceCamera == null) throw new InvalidOperationException("Compact scene has no existing world camera to copy.");
            ResolveCompactCapturePose(view, progress, out var eye, out var target, out string poseSource);
            var report = new CompactCaptureReport { scene = scene.path, view = view, eye = eye, target = target, poseSource = poseSource, utc = DateTime.UtcNow.ToString("o"), sceneDirtyBefore = scene.isDirty };
            if(terrainDraft){report.label="PROVISIONAL_TERRAIN_ONLY";report.limitation="Geography inspection before final road/foliage/navigation validation. Not final art, not gameplay verification.";}
            var oldRt = RenderTexture.active; var oldSky = RenderSettings.skybox;
            Material skySnapshot = oldSky == null ? null : new Material(oldSky) { hideFlags = HideFlags.HideAndDontSave };
            bool oldAsync = ShaderUtil.allowAsyncCompilation;
            GameObject cameraObject = null; Camera camera = null; RenderTexture rt = null; Texture2D pixels = null;
            var diagnosticBlocks=new List<KeyValuePair<Renderer,MaterialPropertyBlock>>();
            var renderErrors = new List<string>();
            void LogError(string message, string stack, LogType type) { if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && renderErrors.Count < 8) renderErrors.Add(type + ": " + message); }
            compactCaptureRunning = true;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                cameraObject = new GameObject("Compact_EditSceneRender_Camera") { hideFlags = HideFlags.HideAndDontSave };
                camera = cameraObject.AddComponent<Camera>(); camera.CopyFrom(sourceCamera); camera.enabled = false;
                camera.tag = "Untagged"; camera.aspect = 16f / 9f; camera.orthographic = false; camera.fieldOfView = view == "overview" ? 52 : 60;
                camera.nearClipPlane = view == "overview" ? 10 : .1f; camera.farClipPlane = view == "overview" ? 25000 : Mathf.Max(4500, sourceCamera.farClipPlane);
                camera.useOcclusionCulling = false; camera.targetTexture = null;
                int ui = LayerMask.NameToLayer("UI"); if (ui >= 0) camera.cullingMask &= ~(1 << ui);
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
                var sourceAdditional = sourceCamera.GetComponent<UniversalAdditionalCameraData>();
                var additional = camera.GetUniversalAdditionalCameraData();
                if (sourceAdditional != null) EditorUtility.CopySerialized(sourceAdditional, additional);
                additional.renderType = CameraRenderType.Base; additional.cameraStack.Clear();
                var look = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Oheangbu.App.WorldLookDriver>(true)).FirstOrDefault();
                look?.PreviewRegionalSky(eye);
                if(terrainDraft&&view=="overview")
                {
                    foreach(var renderer in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).Where(r=>r.sharedMaterials.Any(m=>m!=null&&m.shader!=null&&m.shader.name=="Oheangbu/WorldMacroTerrain")))
                    {
                        var originalBlock=new MaterialPropertyBlock();renderer.GetPropertyBlock(originalBlock);diagnosticBlocks.Add(new KeyValuePair<Renderer,MaterialPropertyBlock>(renderer,originalBlock));
                        var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);block.SetFloat("_WashStrength",0);renderer.SetPropertyBlock(block);
                    }
                    report.limitation+=" Aerial diagnostic only: distance wash temporarily disabled via renderer property blocks, restored immediately after capture.";
                }
                foreach (var dressing in dressings.Where(d => d.enabled && d.PreviewInEditor)) dressing.PrepareView(eye, 24, GuardCompactCapture);
                GuardCompactCapture();
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                rt.Create(); camera.targetTexture = rt; pixels = new Texture2D(1920, 1080, TextureFormat.RGBA32, false, false);
                Application.logMessageReceived += LogError;
                try
                {
                    if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
                    else camera.Render();
                    if (renderErrors.Count != 0) throw new InvalidOperationException("Edit-scene render rejected: " + string.Join(" | ", renderErrors));
                    RenderTexture.active = rt; pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0, false); pixels.Apply(false, false);
                    if (renderErrors.Count != 0) throw new InvalidOperationException("Edit-scene readback rejected: " + string.Join(" | ", renderErrors));
                }
                finally { Application.logMessageReceived -= LogError; }
                MeasureCompactCapture(pixels.GetPixels32(), report);
                if (report.blackFraction > .99f || report.transparentFraction > .99f || report.luminanceDeviation < .0008f)
                    throw new InvalidOperationException("Capture rejected as black, transparent or effectively flat; no previous image overwritten.");
                GuardCompactCapture();
                string directory = Path.Combine(Output, "Captures"); Directory.CreateDirectory(directory);
                report.image = Path.Combine(directory, (terrainDraft?"TerrainDraft_":"EditSceneRender_") + view + ".png");
                File.WriteAllBytes(report.image, pixels.EncodeToPNG()); report.imageHash = Hash(report.image);
                report.fieldOfView = camera.fieldOfView; report.commitRatio = Prologue.PrologueAudit.CommitRatio();
                report.status = "CAPTURED_EDIT_SCENE_REQUIRES_VISUAL_REVIEW";
            }
            finally
            {
                foreach(var pair in diagnosticBlocks)if(pair.Key!=null)pair.Key.SetPropertyBlock(pair.Value);
                if (camera != null) camera.targetTexture = null;
                RenderTexture.active = oldRt;
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                if (oldSky != null && skySnapshot != null) oldSky.CopyPropertiesFromMaterial(skySnapshot);
                RenderSettings.skybox = oldSky;
                if (skySnapshot != null) Object.DestroyImmediate(skySnapshot);
                foreach (var dressing in dressings) if (dressing != null) dressing.ResetCache();
                ShaderUtil.allowAsyncCompilation = oldAsync; compactCaptureRunning = false;
            }
            report.sceneDirtyAfter = scene.isDirty; report.sourceSceneUnchanged = Hash(SourceScene) == progress.sourceHash;
            if (!report.sourceSceneUnchanged) throw new InvalidOperationException("Source scene changed while preparing compact capture.");
            File.WriteAllText(Path.ChangeExtension(report.image, ".json"), JsonUtility.ToJson(report, true));
            return JsonUtility.ToJson(report, true);
        }

        static void GuardCompactCapture() { if (Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("System commit >=85%; stopped compact capture allocation."); }
        static void ResolveCompactCapturePose(string view, Progress progress, out Vector3 eye, out Vector3 target, out string source)
        {
            var map = Compression;
            if (view == "overview")
            {
                var bounds = map.Mapping.TargetBounds; target = new Vector3(bounds.center.x, 150, bounds.center.z);
                eye = target + new Vector3(2600, 8200, -2900); source = "Actual compact bounds; elevated oblique overview camera with complete boundary framing"; return;
            }
            string desired = view == "mine" ? "mine_exit" : view == "capital" ? "palace_front" : "inn_approach";
            foreach (var pair in progress.assets)
            {
                var corridor = AssetDatabase.LoadAssetAtPath<WorldMacroVisualCorridorSO>(pair.target);
                var found = corridor?.Views?.FirstOrDefault(v => v != null && v.Id == desired);
                if (found == null) continue;
                eye = found.Eye; target = found.Target; source = pair.target + ":Views/" + desired; return;
            }
            var session = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
            var pairForContent = progress.assets.Single(p => AssetDatabase.GetAssetPath(session.Content) == p.target);
            var content = AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(pairForContent.source);
            Vector3 anchor = view == "mine" ? content.StartFeet : content.InnCheckpointFeet;
            if (view == "capital")
            {
                var geo = AssetDatabase.LoadAssetAtPath<WorldMacroSheetSO>(progress.assets.First(p => AssetDatabase.LoadAssetAtPath<WorldMacroSheetSO>(p.source) != null).source);
                var site = geo.FindSite("Hwanggyeong") ?? geo.FindSite("SouthGate");
                if (site == null) throw new InvalidOperationException("No authored capital capture anchor exists.");
                anchor = site.Position;
            }
            var approaches = (content.MainPath ?? Array.Empty<Vector3>()).Concat(content.BranchPath ?? Array.Empty<Vector3>())
                .Where(p => Vector3.Distance(p, anchor) >= 16 && Vector3.Distance(p, anchor) <= 80).OrderBy(p => Vector3.Distance(p, anchor)).ToArray();
            Vector3 approach = approaches.Length > 0 ? approaches[0] : anchor - Quaternion.Euler(0, content.StartYaw, 0) * Vector3.forward * 18;
            eye = map.Map(approach) + Vector3.up * 1.7f; target = map.Map(anchor) + Vector3.up * 1.8f;
            // Ground only against close static support; do not place the eye on a roof
            // or move an authored underground view onto unrelated surface terrain.
            float expectedFeetY = eye.y - 1.7f;
            var support = Physics.RaycastAll(eye + Vector3.up * 4, Vector3.down, 12, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => h.rigidbody == null && h.normal.y > .55f && Mathf.Abs(h.point.y - expectedFeetY) < 3)
                .OrderBy(h => Mathf.Abs(h.point.y - expectedFeetY)).ToArray();
            if (support.Length > 0) eye.y = support[0].point.y + 1.7f;
            source = pairForContent.source + ": authored checkpoint/path mapped to compact coordinates; static support probe";
        }
        static void MeasureCompactCapture(Color32[] pixels, CompactCaptureReport report)
        {
            double sum = 0, squared = 0; int black = 0, transparent = 0;
            foreach (var pixel in pixels)
            {
                double luminance = (.2126 * pixel.r + .7152 * pixel.g + .0722 * pixel.b) / 255;
                sum += luminance; squared += luminance * luminance;
                if (luminance < .02) black++; if (pixel.a < 2) transparent++;
            }
            double mean = sum / pixels.Length;
            report.meanLuminance = (float)mean; report.luminanceDeviation = (float)Math.Sqrt(Math.Max(0, squared / pixels.Length - mean * mean));
            report.blackFraction = black / (float)pixels.Length; report.transparentFraction = transparent / (float)pixels.Length;
        }
    }
}
