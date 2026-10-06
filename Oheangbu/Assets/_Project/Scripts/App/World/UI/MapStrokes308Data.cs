using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>#308 brush-strip centre lines: map308_strokes.bytes v1 (SPEC-MAP-OVERHAUL-308 §7), parsed once per map presenter
    /// and read by both maps. Little endian. Layout: header 40 B ('MS08', version, bounds, bin size, bins, counts), stroke table
    /// 24 B each, points 16 B each, bin table 8 B x binsX x binsZ, bin references 12 B each, CRC32 of everything before it.
    /// Plain managed data (arrays): no Unity object, no static state. Only Vector2 / Mathf of UnityEngine are used, so the
    /// offline harness (Tools/Unity/Stage308_map/Offline) runs this file as it is.</summary>
    public sealed class MapStrokes308Data
    {
        public const uint Magic = 0x3830534Du;   // 'M' 'S' '0' '8'
        public const int FormatVersion = 1;
        public const int HeaderBytes = 40, StrokeBytes = 24, PointBytes = 16, BinBytes = 8, RefBytes = 12;
        // point flags
        public const int FlagStartCap = 1, FlagEndCap = 2, FlagBridge = 4, FlagLeft = 8;

        public Vector2 Min, Max;
        public float BinMetres;
        public int BinsX, BinsZ, StrokeCount, PointCount, RefTotal;
        // stroke table
        public byte[] Class, Rank;
        public ushort[] State;
        public int[] First, Count;
        public float[] HalfWidthM, LengthM;
        public uint[] IdHash;
        // points
        public float[] X, Z, Dist;
        public byte[] Pressure, Flags;
        // 250 m bins
        public int[] BinFirst, BinCount, RefStroke, RefFirst, RefCount;

        public float Width => Max.x - Min.x;
        public float Height => Max.y - Min.y;

        /// <summary>null + error when the bytes are not a valid v1 file (wrong magic, version, sizes, ranges or CRC).</summary>
        public static MapStrokes308Data Parse(byte[] b, out string error)
        {
            error = null;
            if (b == null || b.Length < HeaderBytes + 4) { error = "too short"; return null; }
            if (U32(b, 0) != Magic) { error = "magic is not MS08"; return null; }
            if (U32(b, 4) != FormatVersion) { error = "version " + U32(b, 4) + " (reader " + FormatVersion + ")"; return null; }
            var d = new MapStrokes308Data
            {
                Min = new Vector2(F32(b, 8), F32(b, 12)), Max = new Vector2(F32(b, 16), F32(b, 20)), BinMetres = F32(b, 24),
                BinsX = U16(b, 28), BinsZ = U16(b, 30),
            };
            long strokes = U32(b, 32), points = U32(b, 36);
            if (!(d.Max.x > d.Min.x) || !(d.Max.y > d.Min.y) || !(d.BinMetres > 0f) || d.BinsX <= 0 || d.BinsZ <= 0) { error = "bad header (bounds / bins)"; return null; }
            if (strokes > 1000000 || points > 16000000) { error = "counts out of range"; return null; }
            int bins = d.BinsX * d.BinsZ;
            long binTable = HeaderBytes + strokes * StrokeBytes + points * PointBytes;
            long refs0 = binTable + (long)bins * BinBytes;
            if (refs0 + 4 > b.Length) { error = "truncated before the bin table"; return null; }
            long refTotal = 0;
            for (int i = 0; i < bins; i++) refTotal += U32(b, (int)binTable + i * BinBytes + 4);
            long end = refs0 + refTotal * RefBytes;
            if (end + 4 != b.Length) { error = "size " + b.Length + " != expected " + (end + 4); return null; }
            uint crc = Crc32(b, 0, (int)end);
            if (crc != U32(b, (int)end)) { error = "CRC32 mismatch"; return null; }

            int n = (int)strokes, p = (int)points, r = (int)refTotal;
            d.StrokeCount = n; d.PointCount = p; d.RefTotal = r;
            d.Class = new byte[n]; d.Rank = new byte[n]; d.State = new ushort[n]; d.First = new int[n]; d.Count = new int[n];
            d.HalfWidthM = new float[n]; d.LengthM = new float[n]; d.IdHash = new uint[n];
            int o = HeaderBytes;
            for (int i = 0; i < n; i++, o += StrokeBytes)
            {
                d.Class[i] = b[o]; d.Rank[i] = b[o + 1]; d.State[i] = (ushort)U16(b, o + 2);
                long first = U32(b, o + 4), count = U32(b, o + 8);
                if (first + count > points) { error = "stroke " + i + " points out of range"; return null; }
                d.First[i] = (int)first; d.Count[i] = (int)count;
                d.HalfWidthM[i] = F32(b, o + 12); d.LengthM[i] = F32(b, o + 16); d.IdHash[i] = U32(b, o + 20);
            }
            d.X = new float[p]; d.Z = new float[p]; d.Dist = new float[p]; d.Pressure = new byte[p]; d.Flags = new byte[p];
            for (int i = 0; i < p; i++, o += PointBytes)
            {
                d.X[i] = F32(b, o); d.Z[i] = F32(b, o + 4); d.Dist[i] = F32(b, o + 8); d.Pressure[i] = b[o + 12]; d.Flags[i] = b[o + 13];
            }
            d.BinFirst = new int[bins]; d.BinCount = new int[bins];
            for (int i = 0; i < bins; i++, o += BinBytes)
            {
                long first = U32(b, o), count = U32(b, o + 4);
                if (first + count > refTotal) { error = "bin " + i + " references out of range"; return null; }
                d.BinFirst[i] = (int)first; d.BinCount[i] = (int)count;
            }
            d.RefStroke = new int[r]; d.RefFirst = new int[r]; d.RefCount = new int[r];
            for (int i = 0; i < r; i++, o += RefBytes)
            {
                long stroke = U32(b, o), first = U32(b, o + 4), count = U32(b, o + 8);
                if (stroke >= strokes || first + count > points) { error = "reference " + i + " out of range"; return null; }
                int s = (int)stroke;
                if (first < d.First[s] || first + count > (long)d.First[s] + d.Count[s]) { error = "reference " + i + " leaves its stroke"; return null; }
                d.RefStroke[i] = s; d.RefFirst[i] = (int)first; d.RefCount[i] = (int)count;
            }
            return d;
        }

        static uint U32(byte[] b, int o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);
        static int U16(byte[] b, int o) => b[o] | b[o + 1] << 8;
        static float F32(byte[] b, int o) => BitConverter.ToSingle(b, o);   // every Unity platform is little endian

        /// <summary>CRC-32 (IEEE 802.3, the zlib one) of data[offset .. offset + count).</summary>
        public static uint Crc32(byte[] data, int offset, int count)
        {
            var table = new uint[256];   // local: no static table to keep (1 KB, built once per parse)
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < count; i++) crc = table[(crc ^ data[offset + i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        /// <summary>FNV-1a 32 of the id's UTF-8 bytes (the stroke table's idHash).</summary>
        public static uint Fnv1a32(string id)
        {
            uint h = 2166136261u;
            if (string.IsNullOrEmpty(id)) return h;
            var bytes = System.Text.Encoding.UTF8.GetBytes(id);
            for (int i = 0; i < bytes.Length; i++) { h ^= bytes[i]; h *= 16777619u; }
            return h;
        }

        /// <summary>First stroke with this id hash, or -1.</summary>
        public int FindStroke(uint idHash)
        {
            for (int i = 0; i < StrokeCount; i++) if (IdHash[i] == idHash) return i;
            return -1;
        }

        public void BinRange(float minX, float minZ, float maxX, float maxZ, out int bx0, out int bz0, out int bx1, out int bz1)
        {
            bx0 = Mathf.Clamp(Mathf.FloorToInt((minX - Min.x) / BinMetres), 0, BinsX - 1);
            bx1 = Mathf.Clamp(Mathf.FloorToInt((maxX - Min.x) / BinMetres), 0, BinsX - 1);
            bz0 = Mathf.Clamp(Mathf.FloorToInt((minZ - Min.y) / BinMetres), 0, BinsZ - 1);
            bz1 = Mathf.Clamp(Mathf.FloorToInt((maxZ - Min.y) / BinMetres), 0, BinsZ - 1);
        }

        /// <summary>True when a segment of a stroke of class [classFrom, classTo] crosses the world rectangle (label placement:
        /// a place name is not laid over a road). Looks only at the bins the rectangle touches. `shown` (the middle of the
        /// segment, world x / z) and `stateOpen` keep the test to the strokes the map actually draws: a road on unwalked land
        /// or a shortcut not found yet must not move a name (#214: nothing on the map hints at ground not walked yet).</summary>
        public bool AnySegmentInRect(float minX, float minZ, float maxX, float maxZ, int classFrom, int classTo, Func<Vector2, bool> shown = null, bool[] stateOpen = null)
        {
            BinRange(minX, minZ, maxX, maxZ, out int bx0, out int bz0, out int bx1, out int bz1);
            for (int bz = bz0; bz <= bz1; bz++)
                for (int bx = bx0; bx <= bx1; bx++)
                {
                    int bin = bz * BinsX + bx;
                    for (int k = BinFirst[bin], end = BinFirst[bin] + BinCount[bin]; k < end; k++)
                    {
                        int stroke = RefStroke[k], cls = Class[stroke], state = State[stroke];
                        if (cls < classFrom || cls > classTo) continue;
                        if (state > 0 && (stateOpen == null || state > stateOpen.Length || !stateOpen[state - 1])) continue;
                        for (int i = RefFirst[k], last = RefFirst[k] + RefCount[k] - 1; i < last; i++)
                            if (SegmentInRect(X[i], Z[i], X[i + 1], Z[i + 1], minX, minZ, maxX, maxZ)
                                && (shown == null || shown(new Vector2((X[i] + X[i + 1]) * .5f, (Z[i] + Z[i + 1]) * .5f)))) return true;
                    }
                }
            return false;
        }

        // Liang-Barsky clip of the segment against the rectangle
        static bool SegmentInRect(float x0, float y0, float x1, float y1, float minX, float minY, float maxX, float maxY)
        {
            float t0 = 0f, t1 = 1f, dx = x1 - x0, dy = y1 - y0;
            return Clip(-dx, x0 - minX, ref t0, ref t1) && Clip(dx, maxX - x0, ref t0, ref t1) && Clip(-dy, y0 - minY, ref t0, ref t1) && Clip(dy, maxY - y0, ref t0, ref t1);
        }

        static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Abs(p) < 1e-9f) return q >= 0f;
            float t = q / p;
            if (p < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }
    }
}
