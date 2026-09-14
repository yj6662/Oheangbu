using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Deterministic, scene-free bake of the shared north-up runtime map.</summary>
    public static class WorldMapRuntimeBaker
    {
        public const string Folder = "Assets/_Project/Resources/WorldMap";
        public const string TexturePath = Folder + "/WorldMapBase.png";
        public const string DataPath = Folder + "/WorldMapBakedData.asset";
        public const string ManifestPath = Folder + "/WorldMapBakeManifest.json";
        const string PlaytestPath = WorldMacroBuilder.Folder + "/Playtest/Playtest.asset";
        const string LandmarkPath = WorldMacroBuilder.Folder + "/Landmarks/Landmarks.asset";
        const string CorridorPath = WorldMacroBuilder.Folder + "/Playtest/VisualCorridor/VisualCorridor.asset";
        const string CavePrefabPath = WorldMacroBuilder.Folder + "/Landmarks/Prefabs/Cave.prefab";
        const string CaveRampMeshPath = WorldMacroBuilder.Folder + "/Playtest/PolishMeshes/Cave_Ramp_Closed.asset";

        public static string Bake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Runtime map baking requires Edit mode.");
            WorldMacroSheetSO sheet = WorldMacroBuilder.Sheet;
            var playtest = AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(PlaytestPath);
            var landmarks = AssetDatabase.LoadAssetAtPath<WorldMacroLandmarkSheetSO>(LandmarkPath);
            var corridor = AssetDatabase.LoadAssetAtPath<WorldMacroVisualCorridorSO>(CorridorPath);
            if (sheet == null || playtest == null || landmarks == null || corridor == null)
                throw new InvalidOperationException("Runtime map bake inputs are incomplete.");
            EnsureFolder(Folder);

            Texture2D rendered = WorldMapRasterizer.Render(sheet, playtest, 2048, 3072, true);
            File.WriteAllBytes(TexturePath, rendered.EncodeToPNG());
            Object.DestroyImmediate(rendered);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);

            WorldMapBakedDataSO generated = WorldMapRuntimeDataFactory.Create(sheet, playtest, landmarks, corridor, texture);
            // A subsequent bake must retain the actual natural-cave outline instead of
            // restoring the archived rectangular floor/ramp from the landmark prototype.
            bool authoredNaturalCave = WorldMacroNaturalCaveGameplay.TryApplyMapOverride(generated);
            WorldMapZoneSpec caveZone = Array.Find(generated.Zones, zone => zone != null && zone.Id == "cave");
            if (caveZone == null) throw new InvalidOperationException("Explicit cave map zone is missing.");
            if (!authoredNaturalCave) caveZone.DetailLines = BakeCaveWalkableDetail(landmarks, caveZone);
            WorldMapBakedDataSO data = AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(DataPath);
            if (data == null)
            {
                data = Object.Instantiate(generated); data.name = "WorldMapBakedData";
                AssetDatabase.CreateAsset(data, DataPath);
            }
            else
            {
                data.Version = generated.Version; data.Revision = generated.Revision;
                data.BoundsMin = generated.BoundsMin; data.BoundsMax = generated.BoundsMax;
                data.Outline = generated.Outline; data.BaseMap = texture;
                data.Lines = generated.Lines; data.Markers = generated.Markers; data.Zones = generated.Zones;
                EditorUtility.SetDirty(data);
            }
            Object.DestroyImmediate(generated);
            string[] issues = WorldMapValidation.Validate(data, sheet, playtest);
            if (issues.Length > 0) throw new InvalidOperationException("Runtime map validation failed: " + string.Join(" | ", issues));
            var manifest = new BakeManifest
            {
                version = 3, revision = data.Revision,
                ownedAssets = new[] { TexturePath, DataPath, ManifestPath },
                inputs = new[] { WorldMacroBuilder.SheetPath, PlaytestPath, LandmarkPath, CorridorPath, CavePrefabPath, CaveRampMeshPath }
                    .Concat(authoredNaturalCave ? new[] {WorldMacroNaturalCaveGameplay.MapOverridePath} : Array.Empty<string>()).ToArray(),
                projection = "north-up XZ; u=(x-minX)/(maxX-minX), v=(z-minZ)/(maxZ-minZ)",
                discovery = "32m outline-clipped cells; 96m reveal radius",
                routePolicy = authoredNaturalCave ? "crisp runtime vector rivers/routes; natural cave uses the persisted actual concave floor boundary and current underground MainPath; outdoor route tail preserved" :
                    "crisp runtime vector rivers/routes; actual surface MainPath replaces macro mine routes; cave detail uses the authored walkable floor and ramp mesh footprints"
            };
            File.WriteAllText(ManifestPath, JsonUtility.ToJson(manifest, true));
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
            return "WORLD_MAP_BAKED data=" + DataPath + " texture=" + TexturePath +
                   " 2048x3072 lines=" + data.Lines.Length + " markers=" + data.Markers.Length + " zones=" + data.Zones.Length +
                   " caveDetailLines=" + caveZone.DetailLines.Length + " discovery=32m reveal=96m actualMine=" + playtest.StartFeet;
        }

        static WorldMapLineSpec[] BakeCaveWalkableDetail(WorldMacroLandmarkSheetSO landmarks, WorldMapZoneSpec zone)
        {
            var cave = landmarks != null && landmarks.Landmarks != null
                ? Array.Find(landmarks.Landmarks, item => item != null && item.Id == "Cave" && item.PlacementResolved) : null;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CavePrefabPath);
            if (cave == null || prefab == null) throw new InvalidOperationException("Authored cave prefab/placement is unavailable for map detail.");
            Matrix4x4 placed = Matrix4x4.TRS(cave.Position, Quaternion.Euler(0, cave.Yaw, 0), Vector3.one);
            var lines = new List<WorldMapLineSpec>();
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || (filter.name != "Cave_Walkable_Floor" && filter.name != "Cave_Entrance_Stone_Ramp")) continue;
                Mesh footprintMesh = filter.name == "Cave_Entrance_Stone_Ramp"
                    ? AssetDatabase.LoadAssetAtPath<Mesh>(CaveRampMeshPath) : filter.sharedMesh;
                if (footprintMesh == null) footprintMesh = filter.sharedMesh;
                var points = new List<Vector2>(footprintMesh.vertexCount);
                foreach (Vector3 vertex in footprintMesh.vertices)
                {
                    Vector3 prefabWorld = filter.transform.TransformPoint(vertex);
                    Vector3 local = prefab.transform.InverseTransformPoint(prefabWorld);
                    Vector3 world = placed.MultiplyPoint3x4(local);
                    Vector2 xz = new Vector2(world.x, world.z);
                    if (WorldMapDiscoveryGrid.Contains(zone.Polygon, xz)) points.Add(xz);
                }
                Vector2[] hull = ConvexHull(points);
                if (hull.Length < 3) continue;
                Vector2[] outline = new Vector2[hull.Length + 1];
                Array.Copy(hull, outline, hull.Length); outline[hull.Length] = hull[0];
                lines.Add(new WorldMapLineSpec { Id = filter.name + "_Outline", Kind = WorldMapLineKind.DetailOutline, Points = outline, PixelWidth = 2.2f });
                AddHatch(lines, filter.name, hull);
            }
            if (lines.Count < 3) throw new InvalidOperationException("No reliable authored cave walkable footprint was found; refusing a synthetic detail shape.");
            return lines.ToArray();
        }

        static Vector2[] ConvexHull(List<Vector2> source)
        {
            Vector2[] points = source.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToArray();
            if (points.Length <= 2) return points;
            var hull = new List<Vector2>(points.Length * 2);
            foreach (Vector2 point in points)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= .0001f) hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            int lower = hull.Count;
            for (int i = points.Length - 2; i >= 0; i--)
            {
                Vector2 point = points[i];
                while (hull.Count > lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= .0001f) hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull.ToArray();
        }

        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        static void AddHatch(List<WorldMapLineSpec> lines, string id, Vector2[] polygon)
        {
            float min = polygon.Min(p => p.y), max = polygon.Max(p => p.y);
            int row = 0;
            for (float y = min + .75f; y < max; y += 1.5f)
            {
                var crossings = new List<float>();
                for (int i = 0; i < polygon.Length; i++)
                {
                    Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                    if ((a.y <= y && b.y > y) || (b.y <= y && a.y > y)) crossings.Add(Mathf.Lerp(a.x, b.x, (y - a.y) / (b.y - a.y)));
                }
                crossings.Sort();
                for (int i = 1; i < crossings.Count; i += 2)
                    lines.Add(new WorldMapLineSpec { Id = id + "_Fill_" + row++, Kind = WorldMapLineKind.DetailFill,
                        Points = new[] { new Vector2(crossings[i - 1], y), new Vector2(crossings[i], y) }, PixelWidth = 5f });
            }
        }

        public static string Validate()
        {
            var data = AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(DataPath);
            var sheet = WorldMacroBuilder.Sheet;
            var playtest = AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(PlaytestPath);
            string[] issues = WorldMapValidation.Validate(data, sheet, playtest);
            return issues.Length == 0 ? "WORLD_MAP_VALID projection+mine+zone+discovery+spoilers" : "WORLD_MAP_INVALID\n" + string.Join("\n", issues);
        }

        static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/'); string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        [Serializable]
        sealed class BakeManifest
        {
            public int version;
            public string revision, projection, discovery, routePolicy;
            public string[] ownedAssets, inputs;
        }
    }
}
