using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    /// <summary>What one region drawing came out as (checks).</summary>
    public struct RegionMarkStats308
    {
        public int Strokes, Discs, PaperParts, InkParts;
        public float MinWidth;          // the narrowest stroke (m)
        public float DiscDiameter;      // the largest disc (m)
        public Vector2 Min, Max;        // the drawing's extent in its plane (m)
    }

    // #308 D308-127: the region drawings (RegionMarks308SO), in the mark's own plane: +X right and +Y up as the eye sees them, -Z
    // toward the eye, the origin on the carrier's point. `s` = body height / the reference height. Pure: the same input writes the
    // same mesh. PigmentTrail308's contract: UV0.x = head (0) -> tail (1, where a stroke dries out), UV0.y = across (0..1), the
    // vertex colour multiplies the paper pigment (white = paper, InkTint = ink), alpha = density.
    public static class InkRegionForms308
    {
        public sealed class Buffer
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(256);
            public readonly List<Vector2> Uvs = new List<Vector2>(256);
            public readonly List<Color> Colours = new List<Color>(256);
            public readonly List<int> Triangles = new List<int>(512);
            public void Clear() { Vertices.Clear(); Uvs.Clear(); Colours.Clear(); Triangles.Clear(); }
        }

        public static RegionMarkStats308 Compose(BodyRegion308 region, float s, Color ink, Buffer b)
        {
            var st = new RegionMarkStats308 { MinWidth = float.PositiveInfinity, Min = Vector2.positiveInfinity, Max = Vector2.negativeInfinity };
            if (b == null) return st;
            b.Clear();
            s = Mathf.Clamp(s, .4f, 4f);
            Color paper = Color.white;
            switch (region)
            {
                case BodyRegion308.Legs:
                    // ink running down both shins from the knee to the feet, and the pool it leaves
                    for (int k = -1; k <= 1; k += 2) Stroke(b, ref st, new Vector2(k * .11f, .52f) * s, new Vector2(k * .12f, .03f) * s, .15f * s, ink);
                    Stroke(b, ref st, new Vector2(-.3f, .06f) * s, new Vector2(.32f, .05f) * s, .12f * s, ink);
                    break;
                case BodyRegion308.Ankles:
                    // a ring round each ankle (a shackle of ink) and a short run under it
                    for (int k = -1; k <= 1; k += 2)
                    {
                        Stroke(b, ref st, new Vector2(k * .11f - .12f, .15f) * s, new Vector2(k * .11f + .12f, .14f) * s, .07f * s, ink);
                        Stroke(b, ref st, new Vector2(k * .11f, .11f) * s, new Vector2(k * .115f, .0f) * s, .035f * s, ink);
                    }
                    break;
                case BodyRegion308.WeaponHand:
                    // a blot of ink on the weapon hand, twice a head across, running off it
                    Disc(b, ref st, Vector2.zero, .2f * s, 12, 0f, ink, 0f);
                    Stroke(b, ref st, new Vector2(-.08f, -.12f) * s, new Vector2(-.1f, -.5f) * s, .05f * s, ink);
                    Stroke(b, ref st, new Vector2(.05f, -.15f) * s, new Vector2(.06f, -.62f) * s, .045f * s, ink);
                    Stroke(b, ref st, new Vector2(.13f, -.08f) * s, new Vector2(.15f, -.36f) * s, .035f * s, ink);
                    break;
                case BodyRegion308.Chest:
                    // pale cracks branching over the chest (light lines on the dark coat): three trunks and a twig off each
                    // (g2: 40 - 60 % of a .45 m chest across)
                    Vector2[] trunks = { new Vector2(-.09f, .09f), new Vector2(.11f, .03f), new Vector2(.015f, -.12f) };
                    Vector2[] twigs = { new Vector2(-.02f, .08f), new Vector2(.05f, -.07f), new Vector2(-.07f, -.04f) };
                    for (int i = 0; i < 3; i++)
                    {
                        Stroke(b, ref st, Vector2.zero, trunks[i] * s, .05f * s, paper);
                        Vector2 mid = trunks[i] * .5f * s;
                        Stroke(b, ref st, mid, mid + twigs[i] * s, .03f * s, paper);
                    }
                    break;
                case BodyRegion308.Collar:
                    // a seal pressed on the collarbone: a paper disc, a dark angular seal on it, two runs under it
                    Disc(b, ref st, Vector2.zero, .15f * s, 18, 0f, paper, 0f);
                    Disc(b, ref st, Vector2.zero, .085f * s, 4, 45f, ink, -.004f);
                    Stroke(b, ref st, new Vector2(-.06f, -.13f) * s, new Vector2(-.07f, -.3f) * s, .03f * s, ink);
                    Stroke(b, ref st, new Vector2(.07f, -.12f) * s, new Vector2(.08f, -.24f) * s, .025f * s, ink);
                    break;
                case BodyRegion308.Head:
                    // a band of ink over the eyes, running down the face
                    Stroke(b, ref st, new Vector2(-.17f, 0f) * s, new Vector2(.17f, -.01f) * s, .09f * s, ink);
                    Stroke(b, ref st, new Vector2(-.1f, -.03f) * s, new Vector2(-.11f, -.2f) * s, .03f * s, ink);
                    Stroke(b, ref st, new Vector2(.09f, -.03f) * s, new Vector2(.1f, -.16f) * s, .025f * s, ink);
                    break;
            }
            if (float.IsPositiveInfinity(st.MinWidth)) st.MinWidth = 0f;
            return st;
        }

        // one stroke from `from` (its wet head) to `to` (where it dries out), `width` across
        static void Stroke(Buffer b, ref RegionMarkStats308 st, Vector2 from, Vector2 to, float width, Color colour)
        {
            Vector2 along = to - from; if (along.sqrMagnitude < 1e-8f) return;
            Vector2 side = new Vector2(-along.y, along.x).normalized * (width * .5f);
            const int Rows = 4;
            int first = b.Vertices.Count;
            for (int r = 0; r <= Rows; r++)
            {
                float u = r / (float)Rows; Vector2 p = Vector2.Lerp(from, to, u);
                b.Vertices.Add(new Vector3(p.x - side.x, p.y - side.y, 0f)); b.Uvs.Add(new Vector2(u, 0f)); b.Colours.Add(colour);
                b.Vertices.Add(new Vector3(p.x + side.x, p.y + side.y, 0f)); b.Uvs.Add(new Vector2(u, 1f)); b.Colours.Add(colour);
                Grow(ref st, p - side); Grow(ref st, p + side);
            }
            for (int r = 0; r < Rows; r++)
            {
                int a = first + r * 2;
                b.Triangles.Add(a); b.Triangles.Add(a + 2); b.Triangles.Add(a + 1);
                b.Triangles.Add(a + 1); b.Triangles.Add(a + 2); b.Triangles.Add(a + 3);
            }
            st.Strokes++; st.MinWidth = Mathf.Min(st.MinWidth, width);
            if (colour.r > .5f) st.PaperParts++; else st.InkParts++;
        }

        // a filled polygon (12+ sides reads as a disc); across = 0 at its centre, 1 on its rim (the soft edge)
        static void Disc(Buffer b, ref RegionMarkStats308 st, Vector2 centre, float radius, int sides, float turnDeg, Color colour, float z)
        {
            int first = b.Vertices.Count;
            b.Vertices.Add(new Vector3(centre.x, centre.y, z)); b.Uvs.Add(new Vector2(0f, .5f)); b.Colours.Add(colour);
            for (int i = 0; i < sides; i++)
            {
                float a = (turnDeg + i * 360f / sides) * Mathf.Deg2Rad;
                Vector2 p = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                b.Vertices.Add(new Vector3(p.x, p.y, z)); b.Uvs.Add(new Vector2(0f, 1f)); b.Colours.Add(colour);
                Grow(ref st, p);
            }
            for (int i = 0; i < sides; i++) { b.Triangles.Add(first); b.Triangles.Add(first + 1 + (i + 1) % sides); b.Triangles.Add(first + 1 + i); }
            st.Discs++; st.DiscDiameter = Mathf.Max(st.DiscDiameter, radius * 2f);
            if (colour.r > .5f) st.PaperParts++; else st.InkParts++;
        }

        static void Grow(ref RegionMarkStats308 st, Vector2 p) { st.Min = Vector2.Min(st.Min, p); st.Max = Vector2.Max(st.Max, p); }
    }
}
