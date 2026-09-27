#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.World.Dressing
{
    public sealed partial class WorldMacroDressingRenderer
    {
        [Serializable] sealed class DeformationRow
        {
            public int cellIndex, cellX, cellZ, layer, original, deformed, duplicates, identityMismatches, heightMismatches, matrixMismatches, treeLodMismatches;
            public float maximumAbsoluteDelta;
            public bool pass;
        }
        [Serializable] sealed class DeformationReport
        {
            public string status;
            public string scope = "Actual retained candidate generation with final height deformation bypassed/enabled. No asset mutation. IDs/counts/prototypes/XZ/scale/rotation and matrix entries other than world Y must match exactly; final Y follows the shared deformation sampler. Includes fixed placements and all enabled procedural layers. This does not certify terrain-versus-field interpolation error or actor traversal.";
            public bool deformationEnabled;
            public DeformationRow[] rows;
        }
        public string ValidateSurfaceDeformation(int[] cellIndices = null)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run population identity audit in Edit mode, separately from performance sampling.");
            if (Sheet == null || Sheet.Geography == null || Sheet.Cells.Length == 0) throw new InvalidOperationException("Populated dressing sheet required.");
            if (loadedSheet != Sheet || prototypes == null) ResetCache();
            if (cellIndices == null || cellIndices.Length == 0)
            {
                var eye = Observer != null ? Observer.transform.position : Sheet.Cells[0].Centre;
                float best = float.PositiveInfinity; int nearest = 0;
                for (int i = 0; i < Sheet.Cells.Length; i++) { float distance = FlatDistanceSquared(eye, Sheet.Cells[i].Centre); if (distance < best) { best = distance; nearest = i; } }
                cellIndices = new[] { nearest };
            }
            var rows = new List<DeformationRow>(); var visited = new HashSet<int>(); bool pass = true;
            foreach (int cellIndex in cellIndices)
            {
                if (cellIndex < 0 || cellIndex >= Sheet.Cells.Length) throw new ArgumentOutOfRangeException(nameof(cellIndices));
                if (!visited.Add(cellIndex)) continue;
                var cell = Sheet.Cells[cellIndex];
                for (int layer = 0; layer < 7; layer++)
                {
                    if (layer == 4 && (!Sheet.DenseVegetation || !Sheet.LowGrassInfill) || layer == 5 && !Sheet.DenseVegetation) continue;
                    int category = layer == 0 ? 0 : layer == 1 ? 1 : layer == 2 ? 3 : 2;
                    Item[] original = layer == 6 ? Fixed(cell, false) : Generate(cell, category, layer == 0, layer == 5, layer == 4, true, false);
                    Item[] deformed = layer == 6 ? Fixed(cell) : Generate(cell, category, layer == 0, layer == 5, layer == 4);
                    var row = new DeformationRow { cellIndex = cellIndex, cellX = cell.X, cellZ = cell.Z, layer = layer, original = original.Length, deformed = deformed.Length };
                    var byId = new Dictionary<long, Item>(); var seen = new HashSet<long>();
                    foreach (var item in original) if (!byId.TryAdd(item.Id, item)) row.duplicates++;
                    foreach (var item in deformed)
                    {
                        if (!seen.Add(item.Id)) row.duplicates++;
                        if (!byId.TryGetValue(item.Id, out var before)) { row.identityMismatches++; continue; }
                        if (item.Prototype != before.Prototype || item.Scale != before.Scale || item.Position.x != before.Position.x || item.Position.z != before.Position.z) row.identityMismatches++;
                        float delta = SurfaceDelta(before.Position.x, before.Position.z);
                        row.maximumAbsoluteDelta = Mathf.Max(row.maximumAbsoluteDelta, Mathf.Abs(delta));
                        if (item.Position.y != before.Position.y + delta || item.Matrix.m13 != before.Matrix.m13 + delta) row.heightMismatches++;
                        for (int element = 0; element < 16; element++) if (element != 13 && item.Matrix[element] != before.Matrix[element]) row.matrixMismatches++;
                    }
                    if (layer == 0 && Sheet.DenseVegetation)
                    {
                        var far = new Dictionary<long, Item>(); foreach (var item in deformed) far[item.Id] = item;
                        var near = Generate(cell, 0, false);
                        foreach (var item in near) if (!far.TryGetValue(item.Id, out var other) || !SameRetentionItem(item, other)) row.treeLodMismatches++;
                        row.treeLodMismatches += Math.Abs(near.Length - deformed.Length);
                    }
                    row.pass = row.original == row.deformed && row.duplicates + row.identityMismatches + row.heightMismatches + row.matrixMismatches + row.treeLodMismatches == 0;
                    pass &= row.pass; rows.Add(row);
                }
            }
            return JsonUtility.ToJson(new DeformationReport { status = pass ? (Sheet.SurfaceDeformation == null ? "PASS_NULL_LEGACY_IDENTITY" : "PASS_SURFACE_DEFORMATION_IDENTITY") : "FAIL", deformationEnabled = Sheet.SurfaceDeformation != null, rows = rows.ToArray() }, true);
        }
    }
}
#endif
