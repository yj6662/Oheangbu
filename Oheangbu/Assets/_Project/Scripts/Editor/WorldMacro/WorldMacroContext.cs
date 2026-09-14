using System;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>
    /// Render-only continuation beyond the authored footprint. It samples the same
    /// geographic surface and triangle lattice; it is never playable terrain.
    /// </summary>
    public static class WorldMacroContext
    {
        const float Spacing = 16f;
        const float ChunkSize = 1024f;

        public static void Build(WorldMacroSheetSO sheet, Transform parent, Material material)
        {
            if (sheet == null || parent == null || material == null)
                throw new ArgumentNullException("Background context requires sheet, parent and material.");
            if (sheet.Outline == null || sheet.Outline.Length < 3)
                throw new ArgumentException("Background context requires a polygon outline.");
            if (!Mathf.Approximately(sheet.GridSpacing, Spacing))
                throw new ArgumentException("Background context must share the authored 16 m terrain lattice.");

            var root = new GameObject("BackgroundContext_RenderOnly_NoGameplay_NoColliders").transform;
            root.SetParent(parent, false);
            var expanded = Object.Instantiate(sheet);
            expanded.name = sheet.name + "_TemporaryContextOutline";
            expanded.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Vector2 center = PolygonCentroid(sheet.Outline);
                expanded.Outline = new Vector2[sheet.Outline.Length];
                for (int i = 0; i < sheet.Outline.Length; i++)
                    expanded.Outline[i] = center + (sheet.Outline[i] - center) * 1.22f;

                // Anchoring to BoundsMin +/- exactly 1024 preserves the original
                // 16 m vertex positions, including the partial eastern/northern chunks.
                Vector2 min = sheet.BoundsMin - Vector2.one * ChunkSize;
                Vector2 max = sheet.BoundsMax + Vector2.one * ChunkSize;
                int index = 0;
                for (float z = min.y; z < max.y; z += ChunkSize)
                    for (float x = min.x; x < max.x; x += ChunkSize)
                    {
                        var rect = new Rect(x, z, Mathf.Min(ChunkSize, max.x - x), Mathf.Min(ChunkSize, max.y - z));
                        if (EntirelyInside(sheet, rect)) continue;

                        var mesh = WorldMacroTerrain.BuildTerrainMesh(expanded, rect, Spacing, true);
                        var vertices = mesh.vertices;
                        var sourceTriangles = mesh.triangles;
                        var kept = new List<int>(sourceTriangles.Length);
                        for (int t = 0; t < sourceTriangles.Length; t += 3)
                        {
                            int a = sourceTriangles[t], b = sourceTriangles[t + 1], c = sourceTriangles[t + 2];
                            Vector3 centroid = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                            if (WorldMacroTerrain.Contains(sheet, centroid.x, centroid.z)) continue;
                            kept.Add(a); kept.Add(b); kept.Add(c);
                        }
                        if (kept.Count == 0)
                        {
                            Object.DestroyImmediate(mesh);
                            continue;
                        }
                        mesh.SetTriangles(kept, 0);
                        // Original globally sampled normals stay intact. RecalculateNormals
                        // here would create a seam at the authored/context boundary.
                        mesh.RecalculateBounds();
                        mesh.name = "Context_" + index.ToString("D3");
                        var saved = WorldMacroBuilder.Save(mesh, "Meshes/" + mesh.name + ".asset");
                        var go = new GameObject(saved.name + "_RenderOnly");
                        go.transform.SetParent(root, false);
                        go.AddComponent<MeshFilter>().sharedMesh = saved;
                        go.AddComponent<MeshRenderer>().sharedMaterial = material;
                        index++;
                    }
            }
            finally
            {
                Object.DestroyImmediate(expanded);
            }
        }

        static bool EntirelyInside(WorldMacroSheetSO sheet, Rect rect)
        {
            for (int z = 0; z < 3; z++)
                for (int x = 0; x < 3; x++)
                    if (!WorldMacroTerrain.Contains(sheet,
                            Mathf.Lerp(rect.xMin, rect.xMax, x * .5f),
                            Mathf.Lerp(rect.yMin, rect.yMax, z * .5f)))
                        return false;
            // A concave intrusion can contain a polygon vertex even when the nine
            // samples are inside. Keep that chunk so the exact centroid test decides.
            foreach (Vector2 p in sheet.Outline)
                if (p.x >= rect.xMin && p.x <= rect.xMax && p.y >= rect.yMin && p.y <= rect.yMax)
                    return false;
            return true;
        }

        static Vector2 PolygonCentroid(Vector2[] points)
        {
            double areaTwice = 0, x = 0, z = 0;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            {
                double cross = (double)points[j].x * points[i].y - (double)points[i].x * points[j].y;
                areaTwice += cross;
                x += (points[j].x + points[i].x) * cross;
                z += (points[j].y + points[i].y) * cross;
            }
            if (Math.Abs(areaTwice) > .0001)
                return new Vector2((float)(x / (3 * areaTwice)), (float)(z / (3 * areaTwice)));
            Vector2 average = Vector2.zero;
            foreach (Vector2 p in points) average += p;
            return average / points.Length;
        }
    }
}
