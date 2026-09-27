// DRAFT ONLY: outside Unity Assets, intentionally suffixed .cs.tmp.
// Caller owns cloned assets, destination scene, reference rewiring, persistence,
// save-slot isolation, generated geography/cells/maps/materials and navigation.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Oheangbu.App.World.Dressing;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldCompact
{
    /// <summary>
    /// Only explicitly allowlisted absolute coordinates are transformed.
    /// Referenced Objects are never recursively edited, and Transforms are never edited.
    /// Run InspectCoordinateCoverage before generation: Unknown requires review and
    /// CallerOwned requires the corresponding generator. This helper is not a rebake.
    /// </summary>
    public static class WorldCompactCoordinateRemapper
    {
        public enum Treatment { MapVector3, MapVector2XZ, Preserve, CallerOwned, TypedFoliage, Unknown }

        [Serializable]
        public sealed class CoordinateCoverage
        {
            public string objectName, typeName, propertyPath, serializedKind, reason;
            public Treatment treatment;
        }

        sealed class Rule
        {
            public readonly Treatment Treatment;
            public readonly string Reason;
            public Rule(Treatment treatment, string reason) { Treatment = treatment; Reason = reason; }
        }

        sealed class PendingVector
        {
            public string Path;
            public bool IsVector2;
            public Vector2 Value2;
            public Vector3 Value3;
        }

        static readonly Regex ArrayElement = new Regex(@"\.Array\.data\[\d+\]", RegexOptions.Compiled);
        static readonly Dictionary<string, Dictionary<string, Rule>> Rules = BuildRules();

        /// <summary>
        /// Maps allowlisted serialized positions on ONE already cloned object.
        /// Returns only changed property paths. No AssetDatabase.SaveAssets, no scene
        /// saves, no Undo, no reference mutation, no Transform mutation. Unknown fields
        /// remain unchanged and are available through InspectCoordinateCoverage.
        /// Map delegates must be deterministic and preserve full-size local islands.
        /// BoundsMin/Max use map2; caller must validate resulting global bounds.
        /// Scalar height bands remain caller-owned if the supplied map changes Y.
        /// </summary>
        public static List<string> RemapCoordinates(Object obj, Func<Vector3, Vector3> map,
            Func<Vector2, Vector2> map2)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (map2 == null) throw new ArgumentNullException(nameof(map2));
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Coordinate authoring requires Edit mode.");
            if (obj is Transform || obj is GameObject)
                throw new ArgumentException("Transform relocation belongs to the compact scene generator.", nameof(obj));

            if (obj is EarlyRegionFoliage foliage) return RemapFoliage(foliage, map);
            var changed = new List<string>();
            if (!(obj is MonoBehaviour) && !(obj is ScriptableObject)) return changed;

            string typeName = obj.GetType().FullName;
            using (var serialized = new SerializedObject(obj))
            {
                serialized.Update();
                var pending = new List<PendingVector>();
                var iterator = serialized.GetIterator();
                bool enterChildren = true;
                while (iterator.Next(enterChildren))
                {
                    enterChildren = true;
                    if (!IsCoordinateLike(iterator)) continue;
                    enterChildren = false;
                    Rule rule = FindRule(typeName, iterator.propertyPath);
                    if (rule == null) continue;
                    if (rule.Treatment == Treatment.MapVector3)
                    {
                        if (iterator.propertyType != SerializedPropertyType.Vector3)
                            throw new InvalidOperationException("Coordinate schema changed: " + typeName + "." + iterator.propertyPath);
                        Vector3 before = iterator.vector3Value;
                        RequireFinite(before, iterator.propertyPath);
                        Vector3 after = map(before);
                        RequireFinite(after, iterator.propertyPath);
                        if (!before.Equals(after)) pending.Add(new PendingVector { Path = iterator.propertyPath, Value3 = after });
                    }
                    else if (rule.Treatment == Treatment.MapVector2XZ)
                    {
                        if (iterator.propertyType != SerializedPropertyType.Vector2)
                            throw new InvalidOperationException("Coordinate schema changed: " + typeName + "." + iterator.propertyPath);
                        Vector2 before = iterator.vector2Value;
                        RequireFinite(before, iterator.propertyPath);
                        Vector2 after = map2(before);
                        RequireFinite(after, iterator.propertyPath);
                        if (!before.Equals(after)) pending.Add(new PendingVector { Path = iterator.propertyPath, IsVector2 = true, Value2 = after });
                    }
                }

                // Delegate/schema/finite failures above cannot partially alter the object.
                foreach (var entry in pending)
                {
                    var property = serialized.FindProperty(entry.Path);
                    if (property == null) throw new InvalidOperationException("Serialized schema changed during remap: " + entry.Path);
                    if (entry.IsVector2) property.vector2Value = entry.Value2;
                    else property.vector3Value = entry.Value3;
                    changed.Add(entry.Path);
                }
                if (changed.Count > 0)
                {
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(obj);
                }
            }
            return changed;
        }

        /// <summary>
        /// Read-only inventory, with exact serialized paths. Never assumes that an
        /// unfamiliar Vector3 is a position. Does not descend into object references.
        /// Use onlyUnclassified=true for a compact list of missing coordinate rules.
        /// CallerOwned is deliberately included in the default complete report.
        /// </summary>
        public static List<CoordinateCoverage> InspectCoordinateCoverage(Object obj, bool onlyUnclassified = false)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            var result = new List<CoordinateCoverage>();
            string typeName = obj.GetType().FullName;
            using (var serialized = new SerializedObject(obj))
            {
                var iterator = serialized.GetIterator();
                bool enterChildren = true;
                while (iterator.Next(enterChildren))
                {
                    enterChildren = true;
                    if (!IsCoordinateLike(iterator)) continue;
                    enterChildren = false;
                    Rule rule = FindRule(typeName, iterator.propertyPath);
                    Treatment treatment = obj is Transform ? Treatment.CallerOwned : rule?.Treatment ?? Treatment.Unknown;
                    if (onlyUnclassified && treatment != Treatment.Unknown) continue;
                    result.Add(new CoordinateCoverage
                    {
                        objectName = obj.name, typeName = typeName, propertyPath = iterator.propertyPath,
                        serializedKind = iterator.propertyType + ":" + iterator.type,
                        treatment = treatment,
                        reason = obj is Transform ? "Scene hierarchy relocation belongs to caller." :
                            rule?.Reason ?? "Unclassified coordinate-like field; unchanged. Review its coordinate space before adding an exact rule."
                    });
                }
            }
            if (!onlyUnclassified)
            {
                // Scalars and caches below have spatial semantics but are not Vector fields.
                if (typeName == "Oheangbu.App.World.UI.WorldMapBakedDataSO")
                    result.Add(Extra(obj, "Zones[].MinimumY/MaximumY", "Height bands remain valid for Y-preserving maps; caller must transform/revalidate them if Y changes."));
                if (typeName == "Oheangbu.Data.World.WorldMacroDressingSheetSO")
                {
                    result.Add(Extra(obj, "Cells[]", "Rebuild cell indices, centers, min/max height, 17x17 Heights, 64x64 Habitat, RealmWeights and fingerprint from final compact meshes."));
                    result.Add(Extra(obj, "PreservedAreas[].MinimumY/MaximumY", "Height bands remain valid for Y-preserving maps; caller must translate them with the owning island if Y changes."));
                }
                if (typeName == "Oheangbu.App.Demo.DemoEscortSceneRoute")
                    result.Add(Extra(obj, "Stops[].Interaction/Parking/CompanionWait/CargoWait/Checkpoint", "Transform references have no vector value here. Caller must relocate the referenced compact scene objects together."));
            }
            return result;
        }

        static CoordinateCoverage Extra(Object obj, string path, string reason) => new CoordinateCoverage
        {
            objectName = obj.name, typeName = obj.GetType().FullName, propertyPath = path,
            serializedKind = "Spatial dependency", treatment = Treatment.CallerOwned, reason = reason
        };

        public static string NormalizePropertyPath(string path) => ArrayElement.Replace(path, "[]");

        static Rule FindRule(string typeName, string path)
        {
            if (typeName == null || !Rules.TryGetValue(typeName, out var fields)) return null;
            return fields.TryGetValue(NormalizePropertyPath(path), out var rule) ? rule : null;
        }

        static bool IsCoordinateLike(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector4:
                case SerializedPropertyType.Quaternion:
                case SerializedPropertyType.Bounds:
                case SerializedPropertyType.Rect:
                case SerializedPropertyType.Vector2Int:
                case SerializedPropertyType.Vector3Int:
                case SerializedPropertyType.BoundsInt:
                case SerializedPropertyType.RectInt: return true;
                default: return property.type != null && property.type.IndexOf("Matrix4x4", StringComparison.Ordinal) >= 0;
            }
        }

        static Dictionary<string, Dictionary<string, Rule>> BuildRules()
        {
            var all = new Dictionary<string, Dictionary<string, Rule>>(StringComparer.Ordinal);
            void Add(string type, Treatment treatment, string reason, params string[] paths)
            {
                if (!all.TryGetValue(type, out var fields)) all.Add(type, fields = new Dictionary<string, Rule>(StringComparer.Ordinal));
                foreach (string path in paths) fields.Add(path, new Rule(treatment, reason));
            }
            void Points(string type, params string[] paths) => Add(type, Treatment.MapVector3, "Absolute world position; use the owning compact island/connector mapping.", paths);
            void XZ(string type, params string[] paths) => Add(type, Treatment.MapVector2XZ, "Absolute world XZ coordinate.", paths);
            void Keep(string type, params string[] paths) => Add(type, Treatment.Preserve, "Local geometry, size, offset, orientation, range or UI coordinate; preserve physical value.", paths);
            void Owned(string type, params string[] paths) => Add(type, Treatment.CallerOwned, "Generated geography/cache/volume or map projection requires its dedicated caller-owned rebuild.", paths);

            const string Data = "Oheangbu.Data.World.";
            const string World = "Oheangbu.App.World.";
            const string Demo = "Oheangbu.App.Demo.";
            Points(Data + "WorldMacroPlaytestSO", "StartFeet", "InnCheckpointFeet", "Points[].Position", "Checkpoints[].Feet",
                "Encounters[].Feet", "Encounters[].Patrol[]", "MainPath[]", "BranchPath[]");
            Points(Data + "WorldMacroOpeningProfileSO", "StartFeet", "Commission.Position");
            Points(Data + "WorldMacroContentSheetSO", "Entries[].Position");
            Keep(Data + "WorldMacroContentSheetSO", "Entries[].Offset");
            Points(Data + "WorldMacroLandmarkSheetSO", "Landmarks[].Position");
            Keep(Data + "WorldMacroLandmarkSheetSO", "Landmarks[].CourtyardSize");
            Points(Data + "WorldMacroVisualCorridorSO", "Placements[].Position", "Views[].Eye", "Views[].Target");
            Keep(Data + "WorldMacroVisualCorridorSO", "Placements[].Size");
            Points(Data + "PrologueContentSO", "StartPosition", "Points[].Position", "MainPath[]", "BranchPath[]");

            // Geography needs its dedicated source sampler, ridge-height and width policy.
            // This helper must not double-map data already constructed by that generator.
            Owned(Data + "WorldMacroSheetSO", "BoundsMin", "BoundsMax", "Outline[]", "Ridges[].Points[]", "Basins[].Center",
                "Basins[].Radius", "Rivers[].Points[]", "Routes[].Points[]", "Sites[].Position", "Regions[].Polygon[]");
            Points(Data + "WorldMacroDressingSheetSO", "PreservedAreas[].Centre", "Passages[].A", "Passages[].B",
                "StoryClusters[].Centre", "FixedPlacements[].Position");
            Owned(Data + "WorldMacroDressingSheetSO", "Cells[].Centre");
            Keep(Data + "WorldMacroDressingSheetSO", "PreservedAreas[].HalfSize", "PreservedAreas[].Padding", "FixedPlacements[].Euler",
                "Prototypes[].Scale", "Prototypes[].Size", "Prototypes[].GroundPoints[]", "Prototypes[].Lods[].Parts[].Local", "DensityGain");

            const string Map = World + "UI.WorldMapBakedDataSO";
            XZ(Map, "BoundsMin", "BoundsMax", "Outline[]", "Lines[].Points[]", "Markers[].WorldXZ",
                "Zones[].Polygon[]", "Zones[].DetailPath[]", "Zones[].DetailLines[].Points[]");
            Owned(Map, "RegionTiles[].WorldUv"); // UV is normalized projection data, not metres.

            XZ(World + "WorldMacroPlaytestSession", "PreviewSheetBoundsMin", "PreviewSheetBoundsMax");
            Points("Oheangbu.App.Prologue.PrologueEncounter", "PatrolPoints[]");
            Keep(World + "WorldMacroContentPoint", "InspectionPath[]"); // Source uses transform.TransformPoint.
            Points(Demo + "DemoEncounterExpansionTag", "ApproachFeet");
            Points(Demo + "DemoEscortNavigationSeam", "StartWorld", "EndWorld");
            Points(World + "WorldBuildStamp", "cuts[].position");

            // Both endpoints are LOCAL. Moving a hierarchy is the caller's responsibility.
            Keep("Unity.AI.Navigation.NavMeshLink", "m_StartPoint", "m_EndPoint");
            Owned("Unity.AI.Navigation.NavMeshSurface", "m_Center", "m_Size");
            Add(World + "Dressing.EarlyRegionFoliage", Treatment.TypedFoliage,
                "Typed adapter maps matrix translations, preserves mesh basis and rebuilds bounds from all near/far instance meshes.",
                "Packets[].Bounds", "Packets[].Near[].Matrices[]", "Packets[].Far[].Matrices[]");

            // Explicitly reviewed non-world-coordinate fields present in scene_survey.json.
            Keep(World + "WorldMacroReviewController", "ShoulderOffset");
            Keep(World + "Vehicle.WorldMacroPalanquinController", "Wheels[].VisualRotationOffset");
            Keep(World + "Vehicle.WorldMacroPalanquinProfileSO", "CentreOfMass", "HullCentre", "HullSize", "PitchLimits");
            Keep(World + "WorldMacroPlayerAppearanceProfile", "LocalOffset");
            Keep(World + "WorldMacroPlayerGestureProfile", "CarryBrushDirection", "CarryElbowPole", "CarryWristOffset",
                "HandDorsalLocal", "HandForwardLocal", "HandThumbSideLocal", "HarvestWristOffset", "NearShoulderOffset",
                "RightHandGripPosition", "SeatedBrushDirection", "SeatedElbowPole", "SeatedWristOffset", "WorldDrawingHalfSize");
            Keep(World + "WorldMacroHudSkinProfileSO", "BottlePosition", "BottleSize", "HpPosition", "HpSize", "InkBarPosition",
                "InkBarSize", "PromptSize", "ReticleSize");
            Keep("Oheangbu.App.HarvestInkStreamEffect", "_sinkOffset");
            Keep("Oheangbu.BrushRender.BrushStyleSO", "_powerAccuracyRange", "_powerSpeedRange");
            Keep("Oheangbu.Combat.CombatConfigSO", "_attackCooldownRange", "_shoulderDrawOffset", "_shoulderOffset");
            Keep("Oheangbu.Combat.EnemyAttackProfileSO", "CooldownRange");
            Keep("Oheangbu.Spellcraft.SpellBookSO", "_formRange", "_speedRange");
            Keep(Demo + "CheongryongCombatProfile", "HeadOriginOffset", "TailOriginOffset");
            Keep(Demo + "SouthGateGeneralProfile", "WeaponOffset");
            Keep(Demo + "SummonCombatProfile", "FlameOriginOffset", "FootprintOffset", "RootStartOffset", "WaterOriginOffset");
            Keep("Oheangbu.App.SpellVFX120.Vfx120Profile", "GuardianFistContact", "GuardianPivots[]", "KtpCastOffset",
                "KtpFieldOffset", "KtpImpactOffset", "LaunchViewport", "PartScale");
            Keep("UnityEngine.Rendering.Universal.UniversalAdditionalLightData", "m_LightCookieOffset", "m_LightCookieSize");
            return all;
        }

        static List<string> RemapFoliage(EarlyRegionFoliage foliage, Func<Vector3, Vector3> map)
        {
            var changed = new List<string>();
            if (foliage.Packets == null) return changed;
            var prepared = new EarlyRegionFoliage.Packet[foliage.Packets.Length];
            for (int p = 0; p < foliage.Packets.Length; p++)
            {
                var source = foliage.Packets[p];
                if (source == null) continue;
                string prefix = "Packets.Array.data[" + p + "]";
                bool hasBounds = false;
                Bounds bounds = default, originalMeshBounds = default;
                var near = PrepareFoliageParts(source.Near, prefix + ".Near", map, changed, ref hasBounds, ref bounds, ref originalMeshBounds);
                var far = PrepareFoliageParts(source.Far, prefix + ".Far", map, changed, ref hasBounds, ref bounds, ref originalMeshBounds);
                if (!hasBounds)
                {
                    // Empty packets draw nothing; translation preserves their harmless extent.
                    Vector3 center = map(source.Bounds.center);
                    RequireFinite(center, prefix + ".Bounds.center");
                    bounds = new Bounds(center, source.Bounds.size);
                }
                else
                {
                    // Preserve any authored culling slack (for wind, billboard motion,
                    // etc.) outside original static geometry, rather than tightening it.
                    Vector3 lowSlack = Vector3.Max(Vector3.zero, originalMeshBounds.min - source.Bounds.min);
                    Vector3 highSlack = Vector3.Max(Vector3.zero, source.Bounds.max - originalMeshBounds.max);
                    bounds.SetMinMax(bounds.min - lowSlack, bounds.max + highSlack);
                }
                if (!source.Bounds.Equals(bounds)) changed.Add(prefix + ".Bounds");
                prepared[p] = new EarlyRegionFoliage.Packet
                {
                    Bounds = bounds, Near = near, Far = far, Distance = source.Distance, Count = source.Count
                };
            }
            // New nested records prevent accidental aliasing if the caller shallow-cloned arrays.
            // No mutation has occurred before every matrix and mesh bound is validated.
            if (changed.Count > 0)
            {
                foliage.Packets = prepared;
                EditorUtility.SetDirty(foliage);
            }
            return changed;
        }

        static EarlyRegionFoliage.Part[] PrepareFoliageParts(EarlyRegionFoliage.Part[] source, string prefix,
            Func<Vector3, Vector3> map, List<string> changed, ref bool hasBounds, ref Bounds bounds, ref Bounds originalMeshBounds)
        {
            if (source == null) return null;
            var result = new EarlyRegionFoliage.Part[source.Length];
            for (int p = 0; p < source.Length; p++)
            {
                var part = source[p];
                if (part == null) continue;
                string path = prefix + ".Array.data[" + p + "].Matrices";
                Matrix4x4[] matrices = part.Matrices == null ? null : new Matrix4x4[part.Matrices.Length];
                if (matrices != null && matrices.Length > 0 && part.Mesh == null)
                    throw new InvalidOperationException("Cannot rebuild instancing bounds without mesh: " + path);
                for (int i = 0; matrices != null && i < matrices.Length; i++)
                {
                    Matrix4x4 matrix = part.Matrices[i];
                    string itemPath = path + ".Array.data[" + i + "]";
                    for (int component = 0; component < 16; component++)
                        if (!Finite(matrix[component])) throw new InvalidOperationException("Nonfinite instance matrix: " + itemPath);
                    if (Mathf.Abs(matrix.m30) > 0.00001f || Mathf.Abs(matrix.m31) > 0.00001f ||
                        Mathf.Abs(matrix.m32) > 0.00001f || Mathf.Abs(matrix.m33 - 1f) > 0.00001f)
                        throw new InvalidOperationException("Expected affine instance matrix: " + itemPath);
                    Vector3 original = new Vector3(matrix.m03, matrix.m13, matrix.m23);
                    Bounds originalInstanceBounds = TransformBounds(matrix, part.Mesh.bounds);
                    Vector3 translated = map(original);
                    RequireFinite(translated, itemPath);
                    // Only translation changes. Rotation, scale and shear are retained exactly.
                    matrix.m03 = translated.x; matrix.m13 = translated.y; matrix.m23 = translated.z;
                    matrices[i] = matrix;
                    if (!original.Equals(translated)) changed.Add(itemPath);
                    Bounds instanceBounds = TransformBounds(matrix, part.Mesh.bounds);
                    if (!hasBounds) { bounds = instanceBounds; originalMeshBounds = originalInstanceBounds; hasBounds = true; }
                    else { bounds.Encapsulate(instanceBounds); originalMeshBounds.Encapsulate(originalInstanceBounds); }
                }
                result[p] = new EarlyRegionFoliage.Part
                {
                    Mesh = part.Mesh, Material = part.Material, Submesh = part.Submesh, Matrices = matrices
                };
            }
            return result;
        }

        static Bounds TransformBounds(Matrix4x4 matrix, Bounds local)
        {
            Vector3 e = local.extents;
            Vector3 worldExtent = new Vector3(
                Mathf.Abs(matrix.m00) * e.x + Mathf.Abs(matrix.m01) * e.y + Mathf.Abs(matrix.m02) * e.z,
                Mathf.Abs(matrix.m10) * e.x + Mathf.Abs(matrix.m11) * e.y + Mathf.Abs(matrix.m12) * e.z,
                Mathf.Abs(matrix.m20) * e.x + Mathf.Abs(matrix.m21) * e.y + Mathf.Abs(matrix.m22) * e.z);
            Vector3 worldCenter = matrix.MultiplyPoint3x4(local.center);
            RequireFinite(worldCenter, "Foliage transformed bounds center");
            RequireFinite(worldExtent, "Foliage transformed bounds extent");
            return new Bounds(worldCenter, worldExtent * 2f);
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static void RequireFinite(Vector3 value, string path)
        {
            if (!Finite(value.x) || !Finite(value.y) || !Finite(value.z))
                throw new InvalidOperationException("Nonfinite position: " + path);
        }
        static void RequireFinite(Vector2 value, string path)
        {
            if (!Finite(value.x) || !Finite(value.y)) throw new InvalidOperationException("Nonfinite XZ position: " + path);
        }
    }
}
#endif
