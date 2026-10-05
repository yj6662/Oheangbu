using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>One class / rank resolved for a zoom band (MapStrokes308Graphic.Resolve): what the strip builder needs.
    /// Atlas convention (map308_notation.json strokeAtlas): a row is 64 px with a 6 px margin above and below its content; the
    /// class width x pressure maps onto the row's 40 px ink span and the baked centre line sits on the row's axis (32, or 40 /
    /// 46 on the one-sided teeth / hachure rows). The row's top is the stroke's LEFT where point flag bit3 is set, else its
    /// RIGHT.</summary>
    public struct MapStripStyle308
    {
        public bool Draw;
        /// <summary>the class lays a paper band under its ink (roads)</summary>
        public bool Knock;
        /// <summary>px from the centre line to the row's content top / bottom edge, at pressure 1</summary>
        public float TopPx, BottomPx;
        /// <summary>paper pass: the band's half width = InkHalfPx x pressure + KnockPx</summary>
        public float InkHalfPx, KnockPx;
        /// <summary>atlas v at the content top / bottom of the row</summary>
        public float VTop, VBottom;
        /// <summary>metres per atlas repeat; 0 = the row is stretched once over the whole stroke</summary>
        public float TileMetres;
        public float InkAlpha, CapScale, CapPx, PhysicalBelowPx;
    }

    /// <summary>Growable vertex / index arrays of one strip mesh (kept between rebuilds: no allocation once warm).</summary>
    public sealed class MapStripBuffer308
    {
        public Vector3[] Positions = new Vector3[0];
        public Vector4[] Uv0 = new Vector4[0], Uv1 = new Vector4[0];
        public Color32[] Colors = new Color32[0];
        public int[] Indices = new int[0];
        public int VertexCount, IndexCount, Strips, DroppedStrips;
        /// <summary>the last build drew the paper bands of all roads before any road ink</summary>
        public bool PaperFirst;

        public void Clear() { VertexCount = IndexCount = Strips = DroppedStrips = 0; PaperFirst = false; }

        public void Ensure(int vertices, int indices)
        {
            if (Positions.Length < vertices)
            {
                int n = Mathf.Max(vertices, Positions.Length * 2, 256);
                Array.Resize(ref Positions, n); Array.Resize(ref Uv0, n); Array.Resize(ref Uv1, n); Array.Resize(ref Colors, n);
            }
            if (Indices.Length < indices) Array.Resize(ref Indices, Mathf.Max(indices, Indices.Length * 2, 384));
        }
    }

    /// <summary>Builds the brush strips: a centre line (point, arc length, pressure, flags) becomes a mitred ribbon whose width is
    /// the class's px width of the zoom band (not metres), so strokes keep their px width at every scale. Vertex payload for
    /// UI/MapStroke308: position = (world - origin) x px per metre, uv0 = (arc length / tile, atlas row v), uv1 = (world u, world v,
    /// class ink alpha, layer: 0 ink, 1 paper band + ink, 2 paper band only), colour alpha = class ink alpha (so the shader can
    /// recover the canvas group alpha). Roads: the paper bands of all roads are laid first, then every ink (no notch where one
    /// road's band would cut another road's ink); when that would pass the vertex ceiling, band and ink go as one layer per
    /// stroke. Pure functions over arrays; the only state is the caller's buffer.</summary>
    public static class MapStrokes308Mesh
    {
        public struct Range { public int Stroke, First, Count; }

        public const int StyleSlots = 128;   // class 0..15 x rank 0..7 (ridges carry ranks 1..4: 4 = a short minor ridge, hidden one band earlier)
        public static int StyleIndex(int cls, int rank) => (cls & 15) * 8 + (rank & 7);

        const int LayerInk = 0, LayerBoth = 1, LayerBand = 2;

        // immutable ordering delegate (stroke table order = draw order, then along the stroke)
        static readonly Comparison<Range> ByStroke = (a, b) => a.Stroke != b.Stroke ? a.Stroke.CompareTo(b.Stroke) : a.First.CompareTo(b.First);

        /// <summary>The stroke pieces of the bins [bx0..bx1] x [bz0..bz1], in draw order (stroke table order), with pieces of one
        /// stroke that touch or overlap merged into one run (a stroke crossing a bin edge is drawn once, not twice).</summary>
        public static void CollectBins(MapStrokes308Data d, int bx0, int bz0, int bx1, int bz1, List<Range> into)
        {
            into.Clear();
            for (int bz = bz0; bz <= bz1; bz++)
                for (int bx = bx0; bx <= bx1; bx++)
                {
                    int bin = bz * d.BinsX + bx;
                    for (int k = d.BinFirst[bin], end = d.BinFirst[bin] + d.BinCount[bin]; k < end; k++)
                        into.Add(new Range { Stroke = d.RefStroke[k], First = d.RefFirst[k], Count = d.RefCount[k] });
                }
            if (into.Count < 2) return;
            into.Sort(ByStroke);
            int w = 0;
            for (int i = 1; i < into.Count; i++)
            {
                Range cur = into[w], next = into[i];
                if (next.Stroke == cur.Stroke && next.First <= cur.First + cur.Count)
                {
                    int end = Mathf.Max(cur.First + cur.Count, next.First + next.Count);
                    cur.Count = end - cur.First; into[w] = cur;
                }
                else into[++w] = next;
            }
            into.RemoveRange(w + 1, into.Count - w - 1);
        }

        /// <summary>Every stroke, whole (the unfolded sheet's whole-world view).</summary>
        public static void CollectAll(MapStrokes308Data d, List<Range> into)
        {
            into.Clear();
            for (int s = 0; s < d.StrokeCount; s++) into.Add(new Range { Stroke = s, First = d.First[s], Count = d.Count[s] });
        }

        // the part of a range that is drawn: style on, state open, physical-width rule, at least one segment
        static bool Visible(MapStrokes308Data d, Range r, MapStripStyle308[] styles, bool[] stateOpen, float pxPerMetre, out MapStripStyle308 st, out int a, out int b)
        {
            int s = r.Stroke;
            st = styles[StyleIndex(d.Class[s], d.Rank[s])];
            a = b = 0;
            if (!st.Draw) return false;
            int state = d.State[s];
            if (state > 0 && (stateOpen == null || state > stateOpen.Length || !stateOpen[state - 1])) return false;
            if (st.PhysicalBelowPx > 0f && 2f * d.HalfWidthM[s] * pxPerMetre >= st.PhysicalBelowPx) return false;
            int sFirst = d.First[s], sLast = sFirst + d.Count[s] - 1;
            a = Mathf.Max(r.First, sFirst); b = Mathf.Min(r.First + r.Count - 1, sLast);
            return b - a >= 1;
        }

        /// <summary>Appends the strips of `ranges` to the buffer. stateOpen[state - 1] gates strokes with state &gt; 0 (null = all
        /// such strokes closed). paperFirst = lay every road's paper band before any road ink when the mesh stays under
        /// maxVertices. Strips that would pass maxVertices are left out and counted in DroppedStrips.</summary>
        public static void Build(MapStrokes308Data d, List<Range> ranges, MapStripStyle308[] styles, bool[] stateOpen, Vector2 origin, float pxPerMetre,
            int maxVertices, bool paperFirst, MapStripBuffer308 buffer)
        {
            int firstKnock = -1; long all = 0, bands = 0;
            for (int n = 0; n < ranges.Count; n++)
            {
                if (!Visible(d, ranges[n], styles, stateOpen, pxPerMetre, out var st, out int a, out int b)) continue;
                int need = (b - a + 1) * 2 + 4;
                all += need;
                if (st.Knock) { bands += need; if (firstKnock < 0) firstKnock = n; }
            }
            bool twoPass = paperFirst && firstKnock >= 0 && all + bands <= maxVertices;
            buffer.PaperFirst = twoPass;
            if (!twoPass) { Emit(d, ranges, 0, ranges.Count, styles, stateOpen, origin, pxPerMetre, maxVertices, LayerBoth, buffer); return; }
            Emit(d, ranges, 0, firstKnock, styles, stateOpen, origin, pxPerMetre, maxVertices, LayerBoth, buffer);
            Emit(d, ranges, firstKnock, ranges.Count, styles, stateOpen, origin, pxPerMetre, maxVertices, LayerBand, buffer);
            Emit(d, ranges, firstKnock, ranges.Count, styles, stateOpen, origin, pxPerMetre, maxVertices, LayerInk, buffer);
        }

        // pass LayerBoth: band + ink as one layer per stroke; LayerBand: the paper bands of the knock-out classes only; LayerInk: ink only
        static void Emit(MapStrokes308Data d, List<Range> ranges, int from, int to, MapStripStyle308[] styles, bool[] stateOpen, Vector2 origin, float pxPerMetre,
            int maxVertices, int pass, MapStripBuffer308 buffer)
        {
            float invW = 1f / d.Width, invH = 1f / d.Height;
            for (int n = from; n < to; n++)
            {
                Range r = ranges[n];
                if (!Visible(d, r, styles, stateOpen, pxPerMetre, out var st, out int a, out int b)) continue;
                if (pass == LayerBand && !st.Knock) continue;
                int layer = pass == LayerBoth ? (st.Knock ? LayerBoth : LayerInk) : pass;
                int s = r.Stroke, sFirst = d.First[s], sLast = sFirst + d.Count[s] - 1;
                int need = (b - a + 1) * 2 + 4;
                if (buffer.VertexCount + need > maxVertices) { buffer.DroppedStrips++; continue; }
                buffer.Ensure(buffer.VertexCount + need, buffer.IndexCount + (need / 2 - 1) * 6);
                int stripStart = buffer.VertexCount;
                float capMetres = st.CapPx / Mathf.Max(1e-4f, pxPerMetre);
                float tile = st.TileMetres > 0f ? st.TileMetres : Mathf.Max(.01f, d.LengthM[s]);
                for (int i = a; i <= b; i++)
                {
                    var p = new Vector2(d.X[i], d.Z[i]);
                    int ip = i > sFirst ? i - 1 : i, inx = i < sLast ? i + 1 : i;
                    Vector2 dirIn = Dir(p - new Vector2(d.X[ip], d.Z[ip])), dirOut = Dir(new Vector2(d.X[inx], d.Z[inx]) - p);
                    if (ip == i) dirIn = dirOut;
                    if (inx == i) dirOut = dirIn;
                    if (dirIn == Vector2.zero) dirIn = dirOut.sqrMagnitude > 0f ? dirOut : Vector2.right;
                    if (dirOut == Vector2.zero) dirOut = dirIn;
                    Vector2 nIn = new Vector2(-dirIn.y, dirIn.x), nOut = new Vector2(-dirOut.y, dirOut.x);
                    Vector2 m = nIn + nOut; float ml = m.magnitude;
                    m = ml < 1e-4f ? nIn : m / ml;
                    float miter = 1f / Mathf.Max(.5f, Vector2.Dot(m, nIn));
                    float press = Mathf.Lerp(.5f, 1.25f, d.Pressure[i] / 255f);
                    int flags = d.Flags[i];
                    bool topLeft = (flags & MapStrokes308Data.FlagLeft) != 0;
                    bool capStart = i == sFirst && (flags & MapStrokes308Data.FlagStartCap) != 0 && st.CapScale < .999f;
                    bool capEnd = i == sLast && (flags & MapStrokes308Data.FlagEndCap) != 0 && st.CapScale < .999f;
                    // a capped end tapers over capMetres: the end pair is narrow, one extra full-width pair sits capMetres in
                    if (capEnd && i > a)
                    {
                        float seg = d.Dist[i] - d.Dist[i - 1];
                        if (seg > capMetres * 1.1f && capMetres > 0f)
                            Pair(buffer, d, st, layer, p - dirIn * capMetres, nIn, press, 1f, (d.Dist[i] - capMetres) / tile, topLeft, origin, pxPerMetre, invW, invH);
                    }
                    Pair(buffer, d, st, layer, p, m, press * (capStart || capEnd ? st.CapScale : 1f), miter, d.Dist[i] / tile, topLeft, origin, pxPerMetre, invW, invH);
                    if (capStart && i < b)
                    {
                        float seg = d.Dist[i + 1] - d.Dist[i];
                        if (seg > capMetres * 1.1f && capMetres > 0f)
                            Pair(buffer, d, st, layer, p + dirOut * capMetres, nOut, press, 1f, (d.Dist[i] + capMetres) / tile, topLeft, origin, pxPerMetre, invW, invH);
                    }
                }
                Quads(buffer, stripStart);
                buffer.Strips++;
            }
        }

        /// <summary>A run-time polyline (a discovered shortcut the bake does not carry) as one more strip of `st`.</summary>
        public static void AppendPolyline(MapStrokes308Data d, IReadOnlyList<Vector2> points, MapStripStyle308 st, Vector2 origin, float pxPerMetre,
            int maxVertices, MapStripBuffer308 buffer)
        {
            if (points == null || points.Count < 2 || !st.Draw) return;
            int need = points.Count * 2;
            if (buffer.VertexCount + need > maxVertices) { buffer.DroppedStrips++; return; }
            buffer.Ensure(buffer.VertexCount + need, buffer.IndexCount + (points.Count - 1) * 6);
            float invW = 1f / d.Width, invH = 1f / d.Height, dist = 0f, total = 0f;
            for (int i = 1; i < points.Count; i++) total += (points[i] - points[i - 1]).magnitude;
            float tile = st.TileMetres > 0f ? st.TileMetres : Mathf.Max(.01f, total);
            int stripStart = buffer.VertexCount, layer = st.Knock ? LayerBoth : LayerInk;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 p = points[i];
                Vector2 dirIn = Dir(p - points[Mathf.Max(0, i - 1)]), dirOut = Dir(points[Mathf.Min(points.Count - 1, i + 1)] - p);
                if (i == 0) dirIn = dirOut;
                if (i == points.Count - 1) dirOut = dirIn;
                if (dirIn == Vector2.zero) dirIn = dirOut.sqrMagnitude > 0f ? dirOut : Vector2.right;
                if (dirOut == Vector2.zero) dirOut = dirIn;
                Vector2 nIn = new Vector2(-dirIn.y, dirIn.x), nOut = new Vector2(-dirOut.y, dirOut.x);
                Vector2 m = nIn + nOut; float ml = m.magnitude;
                m = ml < 1e-4f ? nIn : m / ml;
                if (i > 0) dist += (p - points[i - 1]).magnitude;
                bool end = i == 0 || i == points.Count - 1;
                Pair(buffer, d, st, layer, p, m, end ? Mathf.Min(1f, st.CapScale) : 1f, 1f / Mathf.Max(.5f, Vector2.Dot(m, nIn)), dist / tile, false, origin, pxPerMetre, invW, invH);
            }
            Quads(buffer, stripStart);
            buffer.Strips++;
        }

        static void Quads(MapStripBuffer308 buffer, int stripStart)
        {
            for (int v = stripStart; v + 3 < buffer.VertexCount; v += 2)
            {
                int k = buffer.IndexCount;
                buffer.Indices[k] = v; buffer.Indices[k + 1] = v + 1; buffer.Indices[k + 2] = v + 3;
                buffer.Indices[k + 3] = v; buffer.Indices[k + 4] = v + 3; buffer.Indices[k + 5] = v + 2;
                buffer.IndexCount += 6;
            }
        }

        static Vector2 Dir(Vector2 v) { float l = v.magnitude; return l > 1e-6f ? v / l : Vector2.zero; }

        // left vertex then right vertex (left of the travel direction = +normal). scale = pressure (x cap share).
        static void Pair(MapStripBuffer308 buffer, MapStrokes308Data d, MapStripStyle308 st, int layer, Vector2 world, Vector2 normal, float scale, float miter,
            float u, bool topLeft, Vector2 origin, float pxPerMetre, float invW, float invH)
        {
            float top, bottom;
            if (layer == LayerBand) top = bottom = st.InkHalfPx * scale + st.KnockPx;   // the paper band: the ink half width + the margin
            else { top = st.TopPx * scale; bottom = st.BottomPx * scale; }
            // the row's top is the stroke's left where bit3 is set, else its right
            float leftPx = (topLeft ? top : bottom) * miter, rightPx = (topLeft ? bottom : top) * miter;
            float vLeft = topLeft ? st.VTop : st.VBottom, vRight = topLeft ? st.VBottom : st.VTop;
            float perPx = 1f / Mathf.Max(1e-4f, pxPerMetre);
            Vector2 centre = (world - origin) * pxPerMetre;
            Vector2 wl = world + normal * (leftPx * perPx), wr = world - normal * (rightPx * perPx);
            var colour = new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt(st.InkAlpha * 255f), 1, 255));
            int k = buffer.VertexCount;
            buffer.Positions[k] = centre + normal * leftPx;
            buffer.Uv0[k] = new Vector4(u, vLeft, 0f, 0f);
            buffer.Uv1[k] = new Vector4((wl.x - d.Min.x) * invW, (wl.y - d.Min.y) * invH, st.InkAlpha, layer);
            buffer.Colors[k] = colour;
            buffer.Positions[k + 1] = centre - normal * rightPx;
            buffer.Uv0[k + 1] = new Vector4(u, vRight, 0f, 0f);
            buffer.Uv1[k + 1] = new Vector4((wr.x - d.Min.x) * invW, (wr.y - d.Min.y) * invH, st.InkAlpha, layer);
            buffer.Colors[k + 1] = colour;
            buffer.VertexCount += 2;
        }
    }
}
