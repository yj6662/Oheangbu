using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.Data.World
{
    /// <summary>One continuous geographic surface. Routes never participate in its height.</summary>
    public static class WorldMacroTerrain
    {
        static float Smooth(float a, float b, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, value));
        static float Noise(float x, float z, float scale, float seed)
        {
            return Mathf.PerlinNoise(x / scale + seed, z / scale - seed * .73f) * 2f - 1f;
        }

        public static float Height(WorldMacroSheetSO sheet, float x, float z)
        {
            float seed = (sheet.Seed % 997) * .19f;
            float broad = 70f + (z + 6000f) * .0105f;
            broad += Noise(x, z, 1450f, seed) * 43f + Noise(x, z, 430f, seed + 17f) * 19f;
            float ridge = 0f;
            for (int r = 0; r < sheet.Ridges.Length; r++)
            {
                var source = sheet.Ridges[r];
                float nearest = float.MaxValue, elevation = 0f;
                for (int i = 0; i + 1 < source.Points.Length; i++)
                {
                    var a = source.Points[i]; var b = source.Points[i + 1];
                    if (OutsideSegmentBox(x, z, a, b, source.Width * 1.17f)) continue;
                    float t, d = SegmentDistance(x, z, a, b, out t);
                    if (d < nearest) { nearest = d; elevation = Mathf.Lerp(source.Points[i].y, source.Points[i + 1].y, t); }
                }
                if (nearest >= source.Width * 1.17f) continue;
                float shoulder = source.Width * (1f + Noise(x, z, 510f, seed + r * 1.13f) * .17f);
                float fraction = Mathf.Clamp01(1f - nearest / shoulder);
                // Narrow, branching crests with eroded shoulders, not radial mountain domes.
                float face = Mathf.Pow(fraction, 1.45f);
                float erosion = 1f + Noise(x, z, 210f, seed + r) * .11f;
                float value = elevation * face * erosion;
                ridge = Mathf.Max(ridge, value);
            }
            // Domain-warped relief breaks the empty gaps between mapped ranges into irregular
            // foothills. No route, route distance, or settlement location participates in this field.
            float foothill = 0f;
            if (sheet.FoothillHeight > 0f || sheet.FoothillDetailHeight > 0f)
            {
                float wx=x+Noise(x,z,1370f,seed+51f)*210f;
                float wz=z+Noise(x,z,1630f,seed+62f)*210f;
                float envelope=Mathf.Lerp(.68f,1.28f,Mathf.Clamp01(.5f+Noise(x,z,1930f,seed+73f)));
                float rolling=Mathf.Pow(Mathf.Clamp01(.5f+.85f*Noise(wx,wz,Mathf.Max(32f,sheet.FoothillWavelength),seed+84f)),1.65f);
                float detail=Mathf.Pow(Mathf.Clamp01(.5f+.8f*Noise(wx,wz,Mathf.Max(32f,sheet.FoothillDetailWavelength),seed+95f)),2f);
                foothill=(sheet.FoothillHeight*rolling*envelope+sheet.FoothillDetailHeight*detail)
                    *Mathf.Lerp(1f,.38f,Smooth(75f,380f,ridge));
            }
            float h = broad + ridge + foothill;
            for (int b = 0; b < sheet.Basins.Length; b++)
            {
                var basin = sheet.Basins[b];
                float dx = (x - basin.Center.x) / basin.Radius.x, dz = (z - basin.Center.y) / basin.Radius.y;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d >= sheet.BasinRimFraction) continue;
                float shoulder=Smooth(sheet.BasinCoreFraction,sheet.BasinRimFraction,d);
                float blend = 1f - shoulder;
                float floor = basin.Floor + Noise(x, z, 680f, seed + b * 3.1f) * 8f + Noise(x, z, 180f, seed + 4f) * 2.4f;
                floor+=foothill*.38f*shoulder;
                h = Mathf.Lerp(h, floor, blend);
            }
            h += Noise(x, z, 65f, seed + 31f) * Mathf.Lerp(1.4f, 5.4f, Mathf.Clamp01(ridge / 450f));
            // A river and its floodplain are geological constraints established before roads.
            for (int r = 0; r < sheet.Rivers.Length; r++)
            {
                var river = sheet.Rivers[r];
                float nearest = float.MaxValue, water = 0f;
                float valleyBase = Mathf.Max(sheet.ValleyMinimumWidth, river.Width * sheet.ValleyWidthPerRiverWidth);
                for (int i = 0; i + 1 < river.Points.Length; i++)
                {
                    var a = river.Points[i]; var b = river.Points[i + 1];
                    if (OutsideSegmentBox(x, z, a, b, valleyBase * 1.18f)) continue;
                    float t, d = SegmentDistance(x, z, a, b, out t);
                    if (d < nearest) { nearest = d; water = Mathf.Lerp(river.Points[i].y, river.Points[i + 1].y, t); }
                }
                // A minimum 24 m geological channel keeps even narrow streams supported by the 16 m lattice.
                float half = Mathf.Max(12f, river.Width * .5f);
                if (nearest >= valleyBase * 1.18f) continue;
                float valleyWidth = valleyBase * (1f + Noise(x, z, 850f, seed + r * 3.71f) * .18f);
                if (nearest >= valleyWidth) continue;
                float bed = water - Mathf.Lerp(2.1f, 4.5f, Mathf.InverseLerp(12f, 65f, river.Width));
                float bank = water + 2f + Mathf.Pow(Mathf.Max(0f, nearest - half) / valleyWidth, 1.15f) * 34f;
                bank+=foothill*sheet.ValleyFoothillRetention*Smooth(half+85f,valleyWidth*.82f,nearest);
                float channel = Smooth(half * .7f, half + 12f, nearest);
                float target = Mathf.Lerp(bed, bank, channel);
                // Preserve a low alluvial bank beside the channel, with relief returning on the
                // valley shoulders. The bank is geological and shared by every stream, not roads.
                float valleyBlend = 1f - Smooth(half + sheet.ValleyBankTerrace, valleyWidth, nearest);
                h = Mathf.Lerp(h, Mathf.Min(h, target), valleyBlend);
                // Continuous support through low basins and tributary junctions; no hard radial switch.
                h = Mathf.Lerp(bed, h, channel);
            }
            return h;
        }

        public static float SegmentDistance(float x, float z, Vector3 a, Vector3 b, out float t)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            float length = dx * dx + dz * dz;
            t = length > .001f ? Mathf.Clamp01(((x - a.x) * dx + (z - a.z) * dz) / length) : 0f;
            float ex = x - (a.x + dx * t), ez = z - (a.z + dz * t);
            return Mathf.Sqrt(ex * ex + ez * ez);
        }

        static bool OutsideSegmentBox(float x, float z, Vector3 a, Vector3 b, float margin)
            => x < Mathf.Min(a.x,b.x)-margin || x > Mathf.Max(a.x,b.x)+margin || z < Mathf.Min(a.z,b.z)-margin || z > Mathf.Max(a.z,b.z)+margin;

        public static bool Contains(WorldMacroSheetSO sheet, float x, float z)
        {
            bool inside = false;
            var p = sheet.Outline;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                if (((p[i].y > z) != (p[j].y > z)) && x < (p[j].x - p[i].x) * (z - p[i].y) / (p[j].y - p[i].y) + p[i].x)
                    inside = !inside;
            return inside;
        }

        public static Vector3 Normal(WorldMacroSheetSO sheet, float x, float z)
        {
            const float d = 4f;
            return new Vector3(Height(sheet, x - d, z) - Height(sheet, x + d, z), d * 2f,
                Height(sheet, x, z - d) - Height(sheet, x, z + d)).normalized;
        }

        // Matches the a-c-b / b-c-d diagonal of the fixed global lattice mesh.
        public static float SurfaceHeight(WorldMacroSheetSO sheet, float x, float z)
        {
            float s = sheet.GridSpacing;
            float ix = Mathf.Floor((x - sheet.BoundsMin.x) / s), iz = Mathf.Floor((z - sheet.BoundsMin.y) / s);
            float x0 = sheet.BoundsMin.x + ix * s, z0 = sheet.BoundsMin.y + iz * s;
            float u = (x - x0) / s, v = (z - z0) / s;
            float a = Height(sheet, x0, z0), b = Height(sheet, x0 + s, z0), c = Height(sheet, x0, z0 + s);
            if (u + v <= 1f) return a + (b - a) * u + (c - a) * v;
            float d = Height(sheet, x0 + s, z0 + s);
            return d + (c - d) * (1f - u) + (b - d) * (1f - v);
        }

        public static Mesh BuildTerrainMesh(WorldMacroSheetSO sheet, Rect bounds, float spacing, bool clipOutline = true)
        {
            int nx = Mathf.CeilToInt(bounds.width / spacing), nz = Mathf.CeilToInt(bounds.height / spacing);
            var vertices = new Vector3[(nx + 1) * (nz + 1)];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new List<int>(nx * nz * 6);
            for (int z = 0; z <= nz; z++) for (int x = 0; x <= nx; x++)
            {
                int index = z * (nx + 1) + x;
                float px = Mathf.Lerp(bounds.xMin, bounds.xMax, x / (float)nx), pz = Mathf.Lerp(bounds.yMin, bounds.yMax, z / (float)nz);
                vertices[index] = new Vector3(px, Height(sheet, px, pz), pz);
                normals[index] = Normal(sheet, px, pz);
                uv[index] = new Vector2(px / 48f, pz / 48f);
            }
            for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
            {
                int a = z * (nx + 1) + x, b = a + 1, c = a + nx + 1, d = c + 1;
                Add(a, c, b); Add(b, c, d);
            }
            void Add(int a, int b, int c)
            {
                Vector3 center = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                if (clipOutline && !Contains(sheet, center.x, center.z)) return;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }
            var mesh = new Mesh { name = "MacroTerrain", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv;
            mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
