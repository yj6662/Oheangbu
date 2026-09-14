using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // SPEC-ASSET-INTAKE. Repairs only proven dangling references in the received packs.
    // Evidence: source FBX Geometry/Model/Material nodes, importer name tables and prefab YAML.
    // A source-export fallback is explicitly not described as a restored artistic material.
    public static class AssetIntakeReferenceRepair
    {
        [Serializable]
        public sealed class Item
        {
            public string prefab, status, evidence, note, backup, sourceAsset, sourceName, sourceGuid;
            public long sourceLocalId;
            public int removedEmptyLodNodes, connectedColliders, connectedMaterialSlots;
            public float finalCullThreshold;
        }

        [Serializable]
        public sealed class Report
        {
            public string created;
            public List<Item> items = new List<Item>();
        }

        private static string UnityRoot => Directory.GetParent(Application.dataPath).FullName;
        private static string RepoRoot => Directory.GetParent(UnityRoot).FullName;
        private static string ReportFolder => Path.Combine(RepoRoot, "Docs/Assets");

        public static string Repair()
        {
            if (EditorApplication.isPlaying) return "FAIL: stop Play mode before repairing prefab assets";
            var report = new Report { created = DateTimeOffset.Now.ToString("o") };
            RepairSingleLod(report, "Assets/KoreanTraditionalFestival/Prefabs/SM_FoodMesh.prefab",
                "Assets/KoreanTraditionalFestival/Meshs/21_FoodMesh/SM_FoodMesh.fbx");
            RepairSingleLod(report, "Assets/KoreanTraditionalFestival/Prefabs/SM_GalaeShovel_NoRope.prefab",
                "Assets/KoreanTraditionalFestival/Meshs/01_GalaeShovel/SM_GalaeShovel_NoRope.fbx");

            RepairEmbeddedMaterial(report, "Assets/HwaseongForteressGate/Prefabs/SM_Bastion_Parapet.prefab",
                "Assets/HwaseongForteressGate/Models/CastleWall/SM_Bastion_Parapet.fbx", 2,
                new[] { "WorldGridMaterial" },
                "Original slot is Unreal's WorldGridMaterial. Its external material and Engine grid textures are absent. "
                + "An exact embedded match is source fallback geometry coverage, not recovered stone artwork.");
            RepairEmbeddedMaterial(report, "Assets/SeyeonjeongPavilion/Prefabs/SM_PIllar_2.prefab",
                "Assets/SeyeonjeongPavilion/Mesh/Building/SM_PIllar_2.fbx", 1,
                new[] { "WorldGridMaterial" },
                "The missing second slot on all four LODs is Unreal's WorldGridMaterial. Engine grid textures are absent; "
                + "the embedded source fallback still needs artistic review before scene placement.");
            RepairEmbeddedMaterial(report, "Assets/SeyeonjeongPavilion/Prefabs/SM_Landscape_0.prefab",
                "Assets/SeyeonjeongPavilion/Mesh/Ground/SM_Landscape_0.fbx", 0,
                new[] { "MI_Landscape_blend(MaterialInstanceDynamic_69_52357)", "MaterialInstanceDynamic_69" },
                "The external Dynamic_69 material GUID is missing. Reconnect only the matching embedded FBX material. "
                + "Original Unreal terrain layer blending was not preserved by the exported URP/Lit material; "
                + "reference repair does not restore those layer weights or the original landscape appearance.");
            RepairEmbeddedMaterial(report, "Assets/SeyeonjeongPavilion/Prefabs/SM_Landscape_1.prefab",
                "Assets/SeyeonjeongPavilion/Mesh/Ground/SM_Landscape_1.fbx", 0,
                new[] { "MI_Landscape_blend(MaterialInstanceDynamic_68_52155)", "MaterialInstanceDynamic_68" },
                "The external Dynamic_68 material GUID is missing. Reconnect only the matching embedded FBX material. "
                + "Original Unreal terrain layer blending was not preserved by the exported URP/Lit material; "
                + "reference repair does not restore those layer weights or the original landscape appearance.");

            report.items.Add(new Item
            {
                prefab = "Assets/HwaseongHaenggung/Prefabs/SM_SkySphere.prefab",
                status = "UNRESOLVED_SOURCE_MESH_AND_MATERIAL",
                evidence = "Mesh GUID abc00000000003283870018903009347 and material GUID abc00000000010257273874139969994 have no matching asset meta in Assets.",
                note = "Incomplete exported sky utility. Preserve the prefab and exclude it from world placement; "
                    + "do not substitute an arbitrary sphere or count this item as visually inspected geometry."
            });
            report.items.Add(new Item
            {
                prefab = "Assets/SeyeonjeongPavilion/Prefabs/Plane.prefab",
                status = "UNRESOLVED_SOURCE_MESH",
                evidence = "Mesh GUID abc00000000016999198234246839076 has no matching asset meta in Assets. "
                    + "The existing material GUID resolves to SeyeonjeongPavilion/Material/Ground/MI_Water.mat.",
                note = "Water-plane utility with a 1 x 0 x 1 box collider. Preserve the prefab and classify as missing-source water utility. "
                    + "The separate Haenggung Plane.fbx has a different GUID and is not a proven matching source."
            });

            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(ReportFolder);
            File.WriteAllText(Path.Combine(ReportFolder, "PrefabReferenceRepairs.json"), JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            var notes = new StringBuilder("# 프리팹 참조 복구 기록\n\n");
            notes.AppendLine("원본 FBX의 실제 노드와 .meta 이름 표를 대조했다. 같은 FBX의 동일 이름 메시·재질만 연결하며, 원본에 없는 모델·재질은 임의 생성하지 않는다.\n");
            foreach (var item in report.items)
            {
                notes.AppendLine("- `" + item.prefab + "` — **" + item.status + "**");
                notes.AppendLine("  - 근거: " + item.evidence);
                notes.AppendLine("  - 한계/후속: " + item.note);
            }
            File.WriteAllText(Path.Combine(ReportFolder, "PrefabReferenceRepairNotes.md"), notes.ToString(), new UTF8Encoding(false));
            return $"Reference repair: {report.items.Count(i => i.status.StartsWith("REPAIRED"))} repaired, "
                + $"{report.items.Count(i => i.status.StartsWith("ALREADY"))} already repaired, "
                + $"{report.items.Count(i => i.status.StartsWith("UNRESOLVED") || i.status.StartsWith("FAIL"))} unresolved. "
                + "See Docs/Assets/PrefabReferenceRepairs.json; source-export appearance limitations remain explicit.";
        }

        private static void RepairSingleLod(Report report, string prefabPath, string modelPath)
        {
            var item = new Item
            {
                prefab = prefabPath, sourceAsset = modelPath,
                evidence = "Binary FBX contains exactly one Geometry and one LOD0 Mesh Model; LOD1-3 and ConvexHulls are absent. "
                    + "Prefab and importer name table still contain obsolete local file IDs 4300002/4/6/8.",
                note = "Use the existing source LOD0, preserve the previous last LOD's cull threshold and collider convex/cooking settings. "
                    + "No substitute LOD geometry is generated."
            };
            report.items.Add(item);
            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(prefabPath);
                var groups = contents.GetComponentsInChildren<LODGroup>(true);
                if (groups.Length != 1) throw new InvalidOperationException("Expected exactly one LODGroup");
                var group = groups[0]; var levels = group.GetLODs();
                if (levels.Length == 0) throw new InvalidOperationException("Source LOD0 is missing");
                var highest = levels[0].renderers.Where(r => r != null && MeshFor(r) != null).ToArray();
                if (highest.Length != 1 || AssetDatabase.GetAssetPath(MeshFor(highest[0])) != modelPath)
                    throw new InvalidOperationException("LOD0 is not the expected source FBX mesh");
                var mesh = MeshFor(highest[0]);
                if (mesh.vertexCount <= 0) throw new InvalidOperationException("LOD0 mesh is empty");
                var actualMeshes = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Mesh>().Where(m => m.vertexCount > 0).ToArray();
                if (actualMeshes.Length != 1 || actualMeshes[0] != mesh)
                    throw new InvalidOperationException("FBX now has a different mesh set; re-audit before changing LODs");
                var empty = contents.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh == null).ToArray();
                foreach (var filter in empty)
                {
                    if (!(filter.name.EndsWith("_LOD1", StringComparison.Ordinal)
                        || filter.name.EndsWith("_LOD2", StringComparison.Ordinal)
                        || filter.name.EndsWith("_LOD3", StringComparison.Ordinal)))
                        throw new InvalidOperationException("Unexpected empty MeshFilter " + filter.name);
                    if (filter.transform.childCount != 0 || filter.GetComponents<Component>().Any(c => !(c is Transform || c is MeshFilter || c is MeshRenderer)))
                        throw new InvalidOperationException("Empty LOD node has other content; preserve it for review");
                }
                var colliders = contents.GetComponentsInChildren<MeshCollider>(true);
                if (colliders.Length != 1) throw new InvalidOperationException("Expected one source MeshCollider");
                if (colliders[0].sharedMesh != null && colliders[0].sharedMesh != mesh)
                    throw new InvalidOperationException("A different valid collider mesh is already assigned");
                item.sourceName = mesh.name;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out item.sourceGuid, out item.sourceLocalId);
                item.finalCullThreshold = levels[levels.Length - 1].screenRelativeTransitionHeight;
                if (levels.Length == 1 && empty.Length == 0 && colliders[0].sharedMesh == mesh)
                { item.status = "ALREADY_REPAIRED_SINGLE_LOD"; return; }

                item.backup = BackupPrefab(prefabPath);
                group.SetLODs(new[] { new LOD(item.finalCullThreshold, highest) });
                foreach (var filter in empty) { Object.DestroyImmediate(filter.gameObject); item.removedEmptyLodNodes++; }
                var collider = colliders[0];
                collider.sharedMesh = null; collider.sharedMesh = mesh;
                Physics.BakeMesh(mesh.GetInstanceID(), collider.convex, collider.cookingOptions);
                item.connectedColliders = 1;
                group.RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath, out bool saved);
                if (!saved) throw new IOException("Prefab save failed");
                item.status = "REPAIRED_SINGLE_LOD_AND_COLLIDER";
            }
            catch (Exception ex) { item.status = "FAIL: " + ex.GetType().Name + ": " + ex.Message; }
            finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static void RepairEmbeddedMaterial(Report report, string prefabPath, string modelPath, int slot,
            string[] expectedNames, string note)
        {
            var item = new Item
            {
                prefab = prefabPath, sourceAsset = modelPath, note = note,
                evidence = "Source FBX and importer name table identify slot " + slot + " as " + string.Join(" / ", expectedNames)
                    + "; reconnect only an exact named embedded material from that same FBX."
            };
            report.items.Add(item);
            GameObject contents = null;
            try
            {
                var embedded = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>().ToArray();
                var matches = embedded.Where(m => expectedNames.Contains(m.name, StringComparer.Ordinal)).ToArray();
                if (matches.Length != 1)
                {
                    item.status = "UNRESOLVED_SOURCE_MATERIAL";
                    item.note += " Exact embedded match count=" + matches.Length + "; available names=" + string.Join(", ", embedded.Select(m => m.name));
                    return;
                }
                var material = matches[0];
                item.sourceName = material.name;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out item.sourceGuid, out item.sourceLocalId);
                contents = PrefabUtility.LoadPrefabContents(prefabPath);
                var renderers = contents.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r => MeshFor(r) != null && AssetDatabase.GetAssetPath(MeshFor(r)) == modelPath).ToArray();
                if (renderers.Length == 0) throw new InvalidOperationException("No renderer matches the expected FBX");
                foreach (var renderer in renderers)
                {
                    var materials = renderer.sharedMaterials;
                    if (slot >= materials.Length) throw new InvalidOperationException("Expected material slot is absent");
                    if (materials[slot] != null && materials[slot] != material)
                        throw new InvalidOperationException("Target slot has a different valid material; preserve it");
                }
                if (renderers.All(r => r.sharedMaterials[slot] == material))
                { item.status = "ALREADY_REPAIRED_REFERENCE_SOURCE_APPEARANCE_REVIEW"; return; }
                item.backup = BackupPrefab(prefabPath);
                foreach (var renderer in renderers)
                {
                    var materials = renderer.sharedMaterials;
                    if (materials[slot] == material) continue;
                    materials[slot] = material; renderer.sharedMaterials = materials; item.connectedMaterialSlots++;
                }
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath, out bool saved);
                if (!saved) throw new IOException("Prefab save failed");
                item.status = "REPAIRED_REFERENCE_SOURCE_APPEARANCE_REVIEW";
            }
            catch (Exception ex) { item.status = "FAIL: " + ex.GetType().Name + ": " + ex.Message; }
            finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static Mesh MeshFor(Renderer renderer) => renderer.GetComponent<MeshFilter>()?.sharedMesh;

        private static string BackupPrefab(string assetPath)
        {
            string source = Path.Combine(UnityRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
            string backup = Path.Combine(RepoRoot, "Art/AssetIntakeBackups/20260907", assetPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!File.Exists(backup)) File.Copy(source, backup);
            if (File.Exists(source + ".meta") && !File.Exists(backup + ".meta")) File.Copy(source + ".meta", backup + ".meta");
            return backup;
        }
    }
}
