using System;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>Fixed 32 metre, north-up discovery grid. Storage is row-major, south to north.</summary>
    public sealed class WorldMapDiscoveryGrid
    {
        public const float CellSize = 32f;
        public const float RevealRadius = 96f;

        readonly Vector2 min;
        readonly Vector2 max;
        readonly Vector2[] outline;
        readonly byte[] bits;

        public int Width { get; }
        public int Height { get; }
        public int ByteCount => bits.Length;

        public WorldMapDiscoveryGrid(Vector2 boundsMin, Vector2 boundsMax, Vector2[] playableOutline, byte[] saved = null)
        {
            if (boundsMax.x <= boundsMin.x || boundsMax.y <= boundsMin.y)
                throw new ArgumentException("World map discovery bounds are invalid.");
            min = boundsMin;
            max = boundsMax;
            outline = playableOutline ?? Array.Empty<Vector2>();
            Width = Mathf.CeilToInt((max.x - min.x) / CellSize);
            Height = Mathf.CeilToInt((max.y - min.y) / CellSize);
            bits = new byte[(Width * Height + 7) / 8];
            if (saved != null && saved.Length == bits.Length) Buffer.BlockCopy(saved, 0, bits, 0, bits.Length);
        }

        public bool Reveal(Vector2 worldXZ, float radius = RevealRadius)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt((worldXZ.x - radius - min.x) / CellSize));
            int maxX = Mathf.Min(Width - 1, Mathf.FloorToInt((worldXZ.x + radius - min.x) / CellSize));
            int minY = Mathf.Max(0, Mathf.FloorToInt((worldXZ.y - radius - min.y) / CellSize));
            int maxY = Mathf.Min(Height - 1, Mathf.FloorToInt((worldXZ.y + radius - min.y) / CellSize));
            float radiusSquared = radius * radius;
            bool changed = false;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 center = CellCenter(x, y);
                    if ((center - worldXZ).sqrMagnitude > radiusSquared || !Contains(outline, center)) continue;
                    int index = y * Width + x;
                    int mask = 1 << (index & 7);
                    int slot = index >> 3;
                    if ((bits[slot] & mask) != 0) continue;
                    bits[slot] |= (byte)mask;
                    changed = true;
                }
            return changed;
        }

        public bool IsDiscovered(Vector2 worldXZ)
        {
            int x = Mathf.FloorToInt((worldXZ.x - min.x) / CellSize);
            int y = Mathf.FloorToInt((worldXZ.y - min.y) / CellSize);
            return IsDiscovered(x, y);
        }

        public bool IsDiscovered(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return false;
            int index = y * Width + x;
            return (bits[index >> 3] & (1 << (index & 7))) != 0;
        }

        public bool IsPlayableCell(int x, int y)
            => x >= 0 && x < Width && y >= 0 && y < Height && Contains(outline, CellCenter(x, y));

        public Vector2 CellCenter(int x, int y)
            => new Vector2(Mathf.Min(max.x, min.x + (x + .5f) * CellSize),
                           Mathf.Min(max.y, min.y + (y + .5f) * CellSize));

        public byte[] Export()
        {
            var result = new byte[bits.Length];
            Buffer.BlockCopy(bits, 0, result, 0, bits.Length);
            return result;
        }

        public static bool TryDecode(string base64, int expectedLength, out byte[] bytes)
        {
            bytes = null;
            if (string.IsNullOrEmpty(base64)) return false;
            try
            {
                bytes = Convert.FromBase64String(base64);
                if (bytes.Length == expectedLength) return true;
            }
            catch (FormatException) { }
            bytes = null;
            return false;
        }

        public static bool Contains(Vector2[] polygon, Vector2 point)
        {
            if (polygon == null || polygon.Length < 3) return false;
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i], b = polygon[j];
                if (((a.y > point.y) != (b.y > point.y)) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }
    }
}
