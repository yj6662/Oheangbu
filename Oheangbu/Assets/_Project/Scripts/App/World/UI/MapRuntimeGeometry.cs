using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    public static class WorldMapGeometry
    {
        public static Vector2[] Simplify(IReadOnlyList<Vector2> points, float tolerance)
        {
            if (points == null || points.Count == 0) return Array.Empty<Vector2>();
            if (points.Count <= 2) return points.ToArray();
            var keep = new bool[points.Count]; keep[0] = keep[points.Count - 1] = true;
            var ranges = new Stack<Vector2Int>(); ranges.Push(new Vector2Int(0, points.Count - 1));
            float threshold = tolerance * tolerance;
            while (ranges.Count > 0)
            {
                Vector2Int range = ranges.Pop();
                Vector2 a = points[range.x], b = points[range.y], ab = b - a;
                float denominator = ab.sqrMagnitude, best = threshold; int selected = -1;
                for (int i = range.x + 1; i < range.y; i++)
                {
                    float t = denominator > .0001f ? Mathf.Clamp01(Vector2.Dot(points[i] - a, ab) / denominator) : 0f;
                    float distance = (points[i] - (a + ab * t)).sqrMagnitude;
                    if (distance > best) { best = distance; selected = i; }
                }
                if (selected < 0) continue;
                keep[selected] = true;
                ranges.Push(new Vector2Int(range.x, selected)); ranges.Push(new Vector2Int(selected, range.y));
            }
            var result = new List<Vector2>();
            for (int i = 0; i < points.Count; i++) if (keep[i]) result.Add(points[i]);
            return result.ToArray();
        }

        public static Vector2 XZ(Vector3 value) => new Vector2(value.x, value.z);

        public static Vector2[] OrientedRectangle(Vector2 center, Vector2 size, float yaw)
        {
            Quaternion rotation = Quaternion.Euler(0, yaw, 0);
            Vector3[] corners = {
                new Vector3(-size.x*.5f,0,-size.y*.5f), new Vector3(size.x*.5f,0,-size.y*.5f),
                new Vector3(size.x*.5f,0,size.y*.5f), new Vector3(-size.x*.5f,0,size.y*.5f) };
            return corners.Select(c => { Vector3 p = rotation * c; return center + new Vector2(p.x, p.z); }).ToArray();
        }
    }

    public static class WorldMapRuntimeDataFactory
    {
        public static WorldMapBakedDataSO Create(WorldMacroSheetSO sheet, WorldMacroPlaytestSO playtest,
            WorldMacroLandmarkSheetSO landmarks = null, WorldMacroVisualCorridorSO corridor = null,
            Texture2D baseMap = null)
        {
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            var result = ScriptableObject.CreateInstance<WorldMapBakedDataSO>();
            result.name = "WorldMapRuntimeData";
            result.Revision = sheet.Seed + ":" + (playtest != null ? playtest.TerrainRevision : "macro");
            result.BoundsMin = sheet.BoundsMin; result.BoundsMax = sheet.BoundsMax;
            result.Outline = sheet.Outline != null ? sheet.Outline.ToArray() : Array.Empty<Vector2>();
            result.BaseMap = baseMap;
            result.Zones = BuildZones(playtest, landmarks);
            result.Lines = BuildLines(sheet, playtest, result.Zones);
            result.Markers = BuildMarkers(sheet, playtest, landmarks, corridor);
            return result;
        }

        public static WorldMapMarkerSpec[] BuildMarkers(WorldMacroSheetSO sheet, WorldMacroPlaytestSO playtest,
            WorldMacroLandmarkSheetSO landmarks, WorldMacroVisualCorridorSO corridor)
        {
            var result = new List<WorldMapMarkerSpec>();
            if (playtest != null)
            {
                result.Add(new WorldMapMarkerSpec { Id = "Mine", Label = "폐광 입구", Kind = WorldMapMarkerKind.Place,
                    WorldXZ = WorldMapGeometry.XZ(playtest.StartFeet), ZoneId = "cave", InitiallyDiscovered = true });
                result.Add(new WorldMapMarkerSpec { Id = "Inn", Label = "금표 주막", Kind = WorldMapMarkerKind.Rest,
                    WorldXZ = WorldMapGeometry.XZ(playtest.InnCheckpointFeet), ZoneId = "surface" });
            }
            if (landmarks != null && landmarks.Landmarks != null)
            {
                foreach (WorldMacroLandmarkSheetSO.Landmark landmark in landmarks.Landmarks)
                {
                    if (landmark == null || !landmark.PlacementResolved || landmark.Id == "Cave") continue;
                    string id, label;
                    WorldMapMarkerKind kind;
                    switch (landmark.Id)
                    {
                        case "Palace": id = "Palace"; label = "황경 행궁"; kind = WorldMapMarkerKind.Settlement; break;
                        case "Fortress": id = "Cheolong"; label = "철옹 관성"; kind = WorldMapMarkerKind.Gate; break;
                        case "Temple": id = "OldTemple"; label = "북쪽 산사"; kind = WorldMapMarkerKind.Place; break;
                        default: continue;
                    }
                    result.Add(new WorldMapMarkerSpec { Id = id, Label = label, Kind = kind,
                        WorldXZ = WorldMapGeometry.XZ(landmark.Position), ZoneId = "surface" });
                }
            }
            if (corridor != null && corridor.Placements != null)
            {
                WorldMacroVisualCorridorSO.Placement gate = Array.Find(corridor.Placements, p => p != null && p.Id == "CAP_GATE_ARCH");
                if (gate != null) result.Add(new WorldMapMarkerSpec { Id = "SouthGate", Label = "황경 남문",
                    Kind = WorldMapMarkerKind.Gate, WorldXZ = WorldMapGeometry.XZ(gate.Position), ZoneId = "surface" });
            }
            return result.ToArray();
        }

        public static WorldMapLineSpec[] BuildLines(WorldMacroSheetSO sheet, WorldMacroPlaytestSO playtest,
            WorldMapZoneSpec[] zones)
        {
            var result = new List<WorldMapLineSpec>();
            foreach (var river in sheet.Rivers ?? Array.Empty<WorldMacroSheetSO.RiverSpec>())
            {
                if (river == null || river.Points == null || river.Points.Length < 2) continue;
                result.Add(new WorldMapLineSpec { Id = river.Id, Kind = WorldMapLineKind.River,
                    Points = WorldMapGeometry.Simplify(river.Points.Select(WorldMapGeometry.XZ).ToArray(), 6f), PixelWidth = 2.4f });
            }
            foreach (var route in sheet.Routes ?? Array.Empty<WorldMacroSheetSO.RouteSpec>())
            {
                if (route == null || route.Points == null || route.Points.Length < 2) continue;
                if (playtest != null && (route.Id == "Trail_Mine_Inn" || route.Id == "Trail_Bridge_Inn")) continue;
                result.Add(new WorldMapLineSpec { Id = route.Id,
                    Kind = route.Carriage ? WorldMapLineKind.Road : WorldMapLineKind.Trail,
                    Points = WorldMapGeometry.Simplify(route.Points.Select(WorldMapGeometry.XZ).ToArray(), 5f),
                    PixelWidth = route.Carriage ? 2.6f : 1.8f });
            }
            if (playtest != null && playtest.MainPath != null)
            {
                var surfaceRun = new List<Vector2>(); int run = 0;
                for (int pointIndex = 0; pointIndex < playtest.MainPath.Length; pointIndex++)
                {
                    Vector3 point = playtest.MainPath[pointIndex];
                    bool interior = zones != null && Array.Exists(zones, zone => zone != null && zone.Contains(point));
                    if (!interior) surfaceRun.Add(WorldMapGeometry.XZ(point));
                    if (interior || pointIndex == playtest.MainPath.Length - 1)
                    {
                        if (surfaceRun.Count >= 2)
                        {
                            result.Add(new WorldMapLineSpec { Id = "Playtest_MainPath_" + run++, Kind = WorldMapLineKind.Trail,
                                Points = WorldMapGeometry.Simplify(surfaceRun, 2f), PixelWidth = 2.2f });
                        }
                        surfaceRun.Clear();
                    }
                }
                if (surfaceRun.Count >= 2)
                    result.Add(new WorldMapLineSpec { Id = "Playtest_MainPath_" + run, Kind = WorldMapLineKind.Trail,
                        Points = WorldMapGeometry.Simplify(surfaceRun, 2f), PixelWidth = 2.2f });
            }
            return result.ToArray();
        }

        public static WorldMapZoneSpec[] BuildZones(WorldMacroPlaytestSO playtest, WorldMacroLandmarkSheetSO landmarks)
        {
            if (playtest == null) return Array.Empty<WorldMapZoneSpec>();
            WorldMacroLandmarkSheetSO.Landmark cave = landmarks != null && landmarks.Landmarks != null
                ? Array.Find(landmarks.Landmarks, l => l != null && l.Id == "Cave" && l.PlacementResolved) : null;
            Vector2 center = cave != null ? WorldMapGeometry.XZ(cave.Position) : WorldMapGeometry.XZ(playtest.StartFeet);
            float yaw = cave != null ? cave.Yaw : playtest.StartYaw - 180f;
            Vector2 size = cave != null ? new Vector2(Mathf.Max(28, cave.CourtyardSize.x), Mathf.Max(70, cave.CourtyardSize.y)) : new Vector2(28, 70);
            float floor = cave != null ? cave.Position.y : playtest.StartFeet.y;
            Vector3 localOffset = Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0, -13);
            center += new Vector2(localOffset.x, localOffset.z);
            Vector2[] polygon = WorldMapGeometry.OrientedRectangle(center, size, yaw);
            var detail = new List<Vector2>();
            if (playtest.MainPath != null)
                foreach (Vector3 point in playtest.MainPath)
                    if (point.y >= floor - 4 && point.y <= floor + 12 && WorldMapDiscoveryGrid.Contains(polygon, WorldMapGeometry.XZ(point)))
                        detail.Add(WorldMapGeometry.XZ(point));
            return new[] { new WorldMapZoneSpec { Id = "cave", Label = "폐광 내부", Polygon = polygon,
                DetailPath = WorldMapGeometry.Simplify(detail, .5f), MinimumY = floor - 4, MaximumY = floor + 12 } };
        }
    }

    public static class WorldMapRasterizer
    {
        static readonly Color32 Transparent = new Color32(0, 0, 0, 0);
        static readonly Color32 LandLow = new Color32(218, 207, 181, 22);
        static readonly Color32 LandHigh = new Color32(129, 137, 123, 44);
        static readonly Color32 Contour = new Color32(64, 72, 66, 92);
        static readonly Color[] RealmColors =
        {
            new Color(.31f,.46f,.35f,.8f), new Color(.55f,.47f,.33f,.8f), new Color(.61f,.40f,.34f,.8f),
            new Color(.41f,.46f,.51f,.8f), new Color(.30f,.47f,.53f,.8f)
        };

        public static Texture2D Render(WorldMacroSheetSO sheet, WorldMacroPlaytestSO playtest, int width, int height, bool relief)
        {
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            width = Mathf.Max(64, width); height = Mathf.Max(96, height);
            var pixels = Enumerable.Repeat(Transparent, width * height).ToArray();
            float[,] shade = relief ? Relief(sheet, 256, 384) : null;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                Vector2 world = PixelWorld(sheet, x, y, width, height);
                if (!WorldMapDiscoveryGrid.Contains(sheet.Outline, world)) continue;
                float value = shade != null ? Bilinear(shade, x / (float)(width - 1), y / (float)(height - 1)) : .32f;
                Color32 color = Color32.Lerp(LandLow, LandHigh, value);
                for (int r = 0; r < sheet.Regions.Length; r++) if (WorldMapDiscoveryGrid.Contains(sheet.Regions[r].Polygon, world))
                {
                    Color tint = RealmColor((int)sheet.Regions[r].Realm);
                    color = Color32.Lerp(color, (Color32)tint, .08f);
                }
                if (shade != null)
                {
                    float left = Bilinear(shade, Mathf.Max(0, x - 1) / (float)(width - 1), y / (float)(height - 1));
                    float below = Bilinear(shade, x / (float)(width - 1), Mathf.Max(0, y - 1) / (float)(height - 1));
                    int band = Mathf.FloorToInt(value * 11f);
                    if (Mathf.FloorToInt(left * 11f) != band || Mathf.FloorToInt(below * 11f) != band) color = Contour;
                }
                pixels[y * width + x] = color;
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false) { name = "WorldMapBase", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(pixels); texture.Apply(false, false); return texture;
        }

        static float[,] Relief(WorldMacroSheetSO sheet, int width, int height)
        {
            var heights = new float[height, width]; float low = float.MaxValue, high = float.MinValue;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                float wx = Mathf.Lerp(sheet.BoundsMin.x, sheet.BoundsMax.x, x / (float)(width - 1));
                float wz = Mathf.Lerp(sheet.BoundsMin.y, sheet.BoundsMax.y, y / (float)(height - 1));
                float h = WorldMacroTerrain.Height(sheet, wx, wz); heights[y, x] = h;
                if (WorldMapDiscoveryGrid.Contains(sheet.Outline, new Vector2(wx, wz))) { low = Mathf.Min(low, h); high = Mathf.Max(high, h); }
            }
            var result = new float[height, width];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                float dx = heights[y, Mathf.Min(width - 1, x + 1)] - heights[y, Mathf.Max(0, x - 1)];
                float dz = heights[Mathf.Min(height - 1, y + 1), x] - heights[Mathf.Max(0, y - 1), x];
                float light = Mathf.Clamp01(.55f - dx * .004f + dz * .003f);
                float elevation = Mathf.InverseLerp(low, high, heights[y, x]);
                result[y, x] = Mathf.Clamp01(elevation * .62f + (1f - light) * .38f);
            }
            return result;
        }

        static float Bilinear(float[,] values, float u, float v)
        {
            int h = values.GetLength(0), w = values.GetLength(1);
            float fx = u * (w - 1), fy = v * (h - 1); int x = Mathf.FloorToInt(fx), y = Mathf.FloorToInt(fy);
            int nx = Mathf.Min(w - 1, x + 1), ny = Mathf.Min(h - 1, y + 1);
            return Mathf.Lerp(Mathf.Lerp(values[y, x], values[y, nx], fx - x), Mathf.Lerp(values[ny, x], values[ny, nx], fx - x), fy - y);
        }

        static void DrawPolyline(Color32[] pixels, int width, int height, WorldMacroSheetSO sheet, Vector2[] points, Color32 color, float radius)
        {
            for (int i = 1; i < points.Length; i++)
            {
                Vector2 a = WorldPixel(sheet, points[i - 1], width, height), b = WorldPixel(sheet, points[i], width, height);
                float length = Vector2.Distance(a, b); int steps = Mathf.Max(1, Mathf.CeilToInt(length * 1.35f));
                for (int n = 0; n <= steps; n++) DrawDisk(pixels, width, height, sheet, Vector2.Lerp(a, b, n / (float)steps), radius, color);
            }
        }

        static void DrawDisk(Color32[] pixels, int width, int height, WorldMacroSheetSO sheet, Vector2 center, float radius, Color32 color)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius)), maxX = Mathf.Min(width - 1, Mathf.CeilToInt(center.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius)), maxY = Mathf.Min(height - 1, Mathf.CeilToInt(center.y + radius));
            float rr = radius * radius;
            for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
                if ((new Vector2(x, y) - center).sqrMagnitude <= rr && WorldMapDiscoveryGrid.Contains(sheet.Outline, PixelWorld(sheet, x, y, width, height)))
                    pixels[y * width + x] = Color32.Lerp(pixels[y * width + x], color, color.a / 255f);
        }

        static Vector2 PixelWorld(WorldMacroSheetSO sheet, int x, int y, int width, int height)
            => new Vector2(Mathf.Lerp(sheet.BoundsMin.x, sheet.BoundsMax.x, (x + .5f) / width), Mathf.Lerp(sheet.BoundsMin.y, sheet.BoundsMax.y, (y + .5f) / height));
        static Vector2 WorldPixel(WorldMacroSheetSO sheet, Vector2 world, int width, int height)
            => new Vector2(Mathf.InverseLerp(sheet.BoundsMin.x, sheet.BoundsMax.x, world.x) * (width - 1), Mathf.InverseLerp(sheet.BoundsMin.y, sheet.BoundsMax.y, world.y) * (height - 1));
        static Color RealmColor(int realm)
            => RealmColors[Mathf.Abs(realm) % RealmColors.Length];
    }

    public static class WorldMapValidation
    {
        public static string[] Validate(WorldMapBakedDataSO data, WorldMacroSheetSO sheet, WorldMacroPlaytestSO playtest)
        {
            var issues = new List<string>();
            if (data == null || !data.IsUsable) issues.Add("Baked map data is unusable.");
            if (sheet == null) issues.Add("Macro sheet is missing.");
            if (issues.Count > 0) return issues.ToArray();
            if (!WorldMapProjection.Approximately(data.BoundsMin, sheet.BoundsMin) || !WorldMapProjection.Approximately(data.BoundsMax, sheet.BoundsMax))
                issues.Add("Projection bounds do not match the macro sheet.");
            var projection = new WorldMapProjection(data.BoundsMin, data.BoundsMax);
            Vector2 probe = new Vector2(3406.6194f, 1129.2522f);
            if (!WorldMapProjection.Approximately(projection.NormalizedToWorld(projection.WorldToNormalized(probe)), probe, .02f))
                issues.Add("World/map projection does not round-trip the actual mine probe.");
            if (playtest != null)
            {
                WorldMapMarkerSpec mine = Array.Find(data.Markers, m => m != null && m.Id == "Mine");
                if (mine == null || !WorldMapProjection.Approximately(mine.WorldXZ, WorldMapGeometry.XZ(playtest.StartFeet), .1f))
                    issues.Add("Mine marker is not the actual Playtest start/cave position.");
                WorldMapZoneSpec cave = Array.Find(data.Zones, z => z != null && z.Id == "cave");
                if (cave == null || !cave.Contains(playtest.StartFeet)) issues.Add("Explicit cave detail zone does not contain the Playtest start.");
                if (cave == null || cave.DetailLines == null || cave.DetailLines.Length < 3 ||
                    !cave.DetailLines.Any(line => line != null && line.Kind == WorldMapLineKind.DetailOutline))
                    issues.Add("Cave detail lacks an authored walkable floor/ramp footprint.");
            }
            if (data.Markers.Any(m => m != null && (m.Id == "Dragon" || m.Label != null && m.Label.Contains("청룡"))))
                issues.Add("Spoiler/enemy marker leaked into runtime map data.");
            if (data.Markers.Any(m => m != null && m.Label != null && m.Label.Contains("예약")))
                issues.Add("Planning/reservation copy leaked into a runtime marker label.");
            string[] unauthored = { "Logging", "DeepForest", "SouthPost", "Jeokro", "Hyeongang" };
            if (data.Markers.Any(m => m != null && unauthored.Contains(m.Id)))
                issues.Add("A macro-only site leaked into the authored runtime POI set.");
            if (data.Lines == null || data.Lines.Length == 0 || data.Lines.Any(line => line == null || line.Points == null || line.Points.Length < 2))
                issues.Add("Runtime vector geography is missing or malformed.");
            var discovery = new WorldMapDiscoveryGrid(data.BoundsMin, data.BoundsMax, data.Outline);
            discovery.Reveal(probe);
            if (!discovery.IsDiscovered(probe)) issues.Add("Discovery reveal does not include its origin.");
            return issues.ToArray();
        }
    }
}
