using Oheangbu.Data.Spell;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.SpellVFX120
{
    // SPEC-SPELL-DEPLOY-308 section 3: one burst = one mesh. Fixed-capacity arrays owned by the host's pool (or by the runtime
    // when it has no host); nothing here allocates after construction.
    public sealed class InkBurstBuffer308
    {
        public readonly Vector3[] Positions;
        public readonly Vector3[] Normals;
        public readonly Vector4[] Tangents;
        public readonly Color32[] Colors;
        public readonly Vector2[] Uv0;
        public readonly Vector4[] Uv1;
        public readonly int[] Indices;
        public int VertexCount, IndexCount;
        public int Dropped;                      // primitives refused because the buffer was full
        private Vector3 _min, _max;

        public InkBurstBuffer308(int maxVertices)
        {
            maxVertices = Mathf.Clamp(maxVertices, 64, 65000);
            Positions = new Vector3[maxVertices]; Normals = new Vector3[maxVertices]; Tangents = new Vector4[maxVertices];
            Colors = new Color32[maxVertices]; Uv0 = new Vector2[maxVertices]; Uv1 = new Vector4[maxVertices];
            Indices = new int[maxVertices * 3];
            Clear();
        }

        public int Capacity => Positions.Length;
        public void Clear() { VertexCount = IndexCount = Dropped = 0; _min = Vector3.positiveInfinity; _max = Vector3.negativeInfinity; }
        public bool Room(int vertices, int indices)
        {
            if (VertexCount + vertices <= Positions.Length && IndexCount + indices <= Indices.Length) return true;
            Dropped++; return false;
        }

        public int Add(Vector3 position, Vector3 normal, Vector4 tangent, Color32 color, Vector2 uv, Vector4 data)
        {
            int i = VertexCount++;
            Positions[i] = position; Normals[i] = normal; Tangents[i] = tangent; Colors[i] = color; Uv0[i] = uv; Uv1[i] = data;
            // the bounds cover both ends of the advance so the shader's shift never leaves them
            Grow(position);
            if (tangent.w != 0f) Grow(position - new Vector3(tangent.x, tangent.y, tangent.z) * tangent.w);
            return i;
        }

        public void Triangle(int a, int b, int c) { Indices[IndexCount++] = a; Indices[IndexCount++] = b; Indices[IndexCount++] = c; }

        private void Grow(Vector3 p) { _min = Vector3.Min(_min, p); _max = Vector3.Max(_max, p); }

        public Bounds Bounds
        {
            get
            {
                if (VertexCount == 0) return new Bounds(Vector3.zero, Vector3.one);
                var b = new Bounds((_min + _max) * .5f, _max - _min);
                b.Expand(1f);
                return b;
            }
        }

        public void Apply(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(Positions, 0, VertexCount);
            mesh.SetNormals(Normals, 0, VertexCount);
            mesh.SetTangents(Tangents, 0, VertexCount);
            mesh.SetColors(Colors, 0, VertexCount);
            mesh.SetUVs(0, Uv0, 0, VertexCount);
            mesh.SetUVs(1, Uv1, 0, VertexCount);
            mesh.SetIndices(Indices, 0, IndexCount, MeshTopology.Triangles, 0, false);
            mesh.bounds = Bounds;
        }
    }

    /// <summary>A bold wet stroke: capsule body with a pooled rim and a dry-brush tail (atlas row 0).</summary>
    public struct InkStroke308
    {
        public Vector3 Tail;          // the tail tip (effect local)
        public Vector3 Axis;          // unit, tail -> head
        public Vector3 Side;          // unit, across the stroke
        public Vector3 Bend;          // unit, the direction it bends toward
        public float Length, HalfWidth;
        public float Curve, SCurve;   // shares of the length
        public float TailShare;       // share of the length that is dry tail (0 = none)
        public int Split;             // tip forks (0 = a blunt wet head)
        public int Segments;
        public int TailCell;          // atlas cell 0..3
        public float BirthCel;
        public float Seed01, TintWeight, MeltBias;
        public Vector3 Advance;       // advance vector in effect local space (zero = static)
        public bool HeadOnly;         // true: the tail stays, the head rides the advance (a stroke that lengthens in cels)
        // ---- look2 round 2 (opt-in: the defaults leave a stroke exactly as the deploy layer wrote it)
        public bool DryStart;         // the wet body begins as brush hairs combed out of the dry tail (no straight cut where it starts)
        public float DryShare;        // DryStart: share of the wet body the hairs run over (0 = the shader's .26; over 1 = a dry stroke, streaked to its point)
        public float HeadTaper;       // share of the wet body over which it narrows toward the tip (0 = full width up to the head)
        public float HeadTaperEnd;    // width left at the end of the taper, as a share of the half width (0 = HeadTaperEndDefault)
        public float RootWidth;       // width the wet body starts with, as a share of the half width (0 = the layer's .62)
        public float RootRamp;        // share of the wet body over which it reaches its full width (0 = the layer's .6)
        // ---- forms3 (D12, opt-in: Press = 0 leaves a stroke exactly as it was): the PRESSED brush head. A brush is pressed
        // down where the builder's "head" is and drawn out into the dry tail: the head is blunt and lopsided - one edge runs
        // on straight, the other curves in to meet it - never a point at both ends (that is a leaf).
        // Set them with InkBurstMeshBuilder308.PressHead (the numbers live in SpellDeploy308ProfileSO.BrushHeadSet).
        public float Press;           // share of the wet body over which the head closes (0 = no pressed head)
        public float PressLean;       // -1 .. 1: +1 = the +Side edge curves in and the -Side edge runs straight, -1 = the other way, 0 = both close alike
        public float PressEnd;        // half width left where the head ends, as a share of the half width (the round nub that closes it)
        public float PressRound;      // 2 = the closing edge is a quarter ellipse; higher = a fuller shoulder (the width falls only at the very end)
        public int PressRings;        // rings of the body that lie inside the head (0 = a third of Segments): enough of them and the outline does not kink
        // forms3 (D3): the share of the dry tail's atlas cell a combed (DryStart) stroke uses - the cell's solid end is left out.
        // Was the code constant .7 of look pass 2; the forms hand over SpellDeploy308ProfileSO.Stroke.DryStartTailU. 0 = that .7.
        public float DryTailU;
        public float PressCap;        // how far the nub stands out, as a share of its own half width (1 = a half disc, under 1 = flatter and blunter; 0 = PressCapDefault)
        // forms3 fix pass: the PRESSED part of a stroke is wider than the part that was drawn (the brush spreads where it is
        // pressed). PressSwell = how much wider the stroke is where the head begins, as a share of its half width; it swells
        // to that over the last PressSwellSpan of the body before the head. 0 = one width from root to head (as before).
        public float PressSwell, PressSwellSpan;
    }

    // Pure functions: stroke, needle, sprite, column and ribbon geometry written into a caller-owned buffer. No state.
    public static class InkBurstMeshBuilder308
    {
        public const float KindBody = 0f, KindTail = 1f, KindNeedle = 2f, KindColumn = 3f, KindSprite = 4f;
        public const int CellTail = 0, CellDrop = 4, CellDropTailed = 5, CellCluster = 6, CellPuddle = 7, CellFoot = 8, CellFootB = 9,
            CellIgnite = 10, CellSquare = 11, CellStar = 12, CellStarB = 13, CellRing = 14, CellDrip = 15;
        private const int CapSteps = 6;
        private const float TailWidth = .62f;   // a stroke is a comet: thin where it starts, full at the head
        private const float HeadTaperEndDefault = .16f;
        private const float PressCapDefault = .45f;
        private const float DryTailUDefault = .7f;   // a stroke that names no DryTailU (the value look pass 2 was accepted with)
        // forms3 fix pass (review S4): the fallbacks of a pressed head that names no number of its own, and the bounds of the
        // builder - named once (they are not tuning: SpellDeploy308ProfileSO.BrushHeadSet holds the tuned values)
        private const float PressRoundDefault = 2f, PressEndDefault = .3f, PressSwellSpanDefault = .4f, HeadShareMax = .45f;
        /// <summary>The dry tail's quad is this much wider than the body's start (the hairs spread). Forms that must stay under a
        /// height limit read it instead of repeating the number.</summary>
        public const float TailSpread = 1.08f;

        public static Vector3 Spine(in InkStroke308 s, float u)
        {
            float bend = s.Curve * u * u + s.SCurve * Mathf.Sin(u * Mathf.PI * 2f) * .5f;
            return s.Tail + s.Axis * (u * s.Length) + s.Bend * (bend * s.Length);
        }

        /// <summary>World-free measure used by the checks: the head tip of a stroke (effect local).</summary>
        public static Vector3 HeadTip(in InkStroke308 s) => Spine(s, 1f);

        /// <summary>forms3 (D12): give a stroke the pressed brush head of `head` (profile data). `flip` = -1 turns the lopsided
        /// side over (which edge closes depends on which way the caller's Side points). A null or switched-off set leaves the
        /// stroke as it is; a pressed head replaces the forked tip and the old both-ends taper (HeadTaper).</summary>
        public static void PressHead(ref InkStroke308 s, SpellDeploy308ProfileSO.BrushHeadSet head, float flip = 1f)
        {
            if (head == null || head.Share <= 0f) return;
            s.Press = Mathf.Clamp(head.Share, .02f, 1f);
            s.PressLean = Mathf.Clamp(head.Lean * flip, -1f, 1f);
            s.PressEnd = Mathf.Clamp(head.End, .05f, 1f);
            s.PressRound = Mathf.Clamp(head.Round, 1f, 8f);
            s.PressRings = Mathf.Max(0, head.Rings);
            s.PressCap = Mathf.Clamp(head.Cap, .05f, 1f);
            s.PressSwell = Mathf.Clamp(head.Swell, 0f, .5f); s.PressSwellSpan = Mathf.Clamp(head.SwellSpan, .05f, 1f);
            s.Split = 0; s.HeadTaper = 0f; s.HeadTaperEnd = 0f;
        }

        /// <summary>forms3: the share of its width a pressed head keeps `t` of the way through it (1 where it starts, PressEnd
        /// where it ends) - a quarter super-ellipse: convex, so the stroke stays full and closes late, with a round shoulder.</summary>
        public static float PressScale(in InkStroke308 s, float t)
        {
            float round = Mathf.Clamp(s.PressRound > 0f ? s.PressRound : PressRoundDefault, 1f, 8f), end = Mathf.Clamp(s.PressEnd > 0f ? s.PressEnd : PressEndDefault, .05f, 1f);
            t = Mathf.Clamp01(t);
            return Mathf.Lerp(end, 1f, Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(t, round)), 1f / round));
        }

        public static bool AddStroke(InkBurstBuffer308 b, in InkStroke308 s)
        {
            int seg = Mathf.Clamp(s.Segments, 2, 24);
            float w = Mathf.Max(.005f, s.HalfWidth);
            float length = Mathf.Max(s.Length, w * 2.5f);
            float tail = Mathf.Clamp(s.TailShare, 0f, .6f);
            bool press = s.Press > 0f;   // forms3: every `press` branch below is new; with Press = 0 the old statements run unchanged
            float pressEnd = press ? Mathf.Clamp(s.PressEnd > 0f ? s.PressEnd : PressEndDefault, .05f, 1f) : 1f;
            float pressCap = press ? Mathf.Clamp(s.PressCap > 0f ? s.PressCap : PressCapDefault, .05f, 1f) : 1f;
            float swell = press ? Mathf.Clamp(s.PressSwell, 0f, .5f) : 0f, swellSpan = Mathf.Clamp(s.PressSwellSpan > 0f ? s.PressSwellSpan : PressSwellSpanDefault, .05f, 1f);
            float headShare = press ? Mathf.Min(HeadShareMax, w * (1f + swell) * pressEnd * pressCap / length)
                : Mathf.Min(.45f, (s.Split > 0 ? 1.7f : 1f) * w * HeadScale(s, 1f) / length);   // a tapered stroke's tip is as small as its end
            float u0 = tail, u1 = 1f - headShare;
            if (u1 <= u0 + .02f) { u0 = Mathf.Max(0f, u1 - .05f); tail = u0; }
            int split = press ? 0 : Mathf.Clamp(s.Split, 0, 3);
            float pressShare = press ? Mathf.Clamp(s.Press, .02f, 1f) : 0f, pressLean = Mathf.Clamp(s.PressLean, -1f, 1f);
            int pressRings = press ? Mathf.Clamp(s.PressRings > 0 ? s.PressRings : seg / 3, 1, seg - 1) : 0;
            float rootShare = s.RootWidth > 0f ? Mathf.Clamp01(s.RootWidth) : TailWidth;
            // the head's closing edge is (about) a quarter ellipse, long along the stroke and short across it: its rings are
            // set so that the outline turns by the same angle from each to the next (even steps along the stroke would leave
            // the whole turn to the last one - a kink)
            float pressLong = press ? Mathf.Max(1e-4f, pressShare * (1f - headShare - tail) * length) : 1f;
            float pressShort = press ? Mathf.Max(1e-4f, w * (1f + swell) * (1f - pressEnd) * (1f + Mathf.Abs(pressLean))) : 1f;
            Vector3 lastMid = Vector3.zero;
            int vertices = (seg + 1) * 3 + (split > 0 ? split * 4 : CapSteps + 1) + (tail > 0f ? 6 : 0);
            int indices = seg * 12 + (split > 0 ? split * 6 : CapSteps * 3) + (tail > 0f ? 12 : 0);
            if (!b.Room(vertices, indices)) return false;

            var color = new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(s.TintWeight) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(s.MeltBias) * 255f), 0, 255);
            // look2 round 2: a body's param is 0 (as before) or 1 + the share where the wet body starts - the shader then combs
            // the body's start out of the dry tail. COLOR.b = where a vertex lies across the stroke (0 one edge, 1 the other).
            // (+ 2 x a whole number = the share the hairs run over, in twentieths; none = the shader's own share)
            var data = new Vector4(KindBody, s.BirthCel, s.Seed01, s.DryStart ? 1f + u0 + 2f * Mathf.Clamp(Mathf.RoundToInt(s.DryShare * 20f), 0, 60) : 0f);
            Color32 colorL = color, colorM = color, colorR = color;
            if (s.DryStart) { colorM.b = 128; colorR.b = 255; }
            bool moves = s.Advance.sqrMagnitude > 1e-8f;
            int first = b.VertexCount;
            for (int i = 0; i <= seg; i++)
            {
                float u, wide, wideL, wideR;
                if (press)
                {
                    // the rings before the head are spread evenly, the head's own lie closer and closer toward its end
                    int bodyRings = seg - pressRings;
                    float turned = (i - bodyRings) / (float)pressRings * Mathf.PI * .5f;
                    float v = i <= bodyRings ? (1f - pressShare) * i / bodyRings
                        : 1f - pressShare + pressShare * Mathf.Sin(Mathf.Atan2(pressLong * Mathf.Sin(turned), pressShort * Mathf.Cos(turned)));
                    u = Mathf.Lerp(u0, u1, v);
                    wide = w * Mathf.Lerp(rootShare, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, v / (s.RootRamp > 0f ? Mathf.Clamp(s.RootRamp, .05f, 1f) : .6f))));
                    // the pressed part is wider than the drawn part: toward the head's start the stroke swells - on the edge(s) that
                    // close (the straight edge stays straight) - and the head closes from that width
                    float extra = swell > 0f ? wide * swell * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((v - (1f - pressShare - swellSpan)) / swellSpan)) : 0f;
                    // what the head takes off the full width is taken off one edge (lean 1), the other (-1) or both (0)
                    float closed = (wide + extra) * 2f * (1f - PressScale(s, (v - (1f - pressShare)) / pressShare));
                    wideR = wide + (extra * 2f - closed) * (1f + pressLean) * .5f; wideL = wide + (extra * 2f - closed) * (1f - pressLean) * .5f;
                }
                else
                {
                    u = Mathf.Lerp(u0, u1, i / (float)seg);
                    wide = w * Mathf.Lerp(s.RootWidth > 0f ? Mathf.Clamp01(s.RootWidth) : TailWidth, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, i / (seg * (s.RootRamp > 0f ? Mathf.Clamp(s.RootRamp, .05f, 1f) : .6f)))))
                        * HeadScale(s, i / (float)seg);
                    wideL = wideR = wide;
                }
                Vector3 centre = Spine(s, u);
                float weight = !moves ? 0f : s.HeadOnly ? Mathf.InverseLerp(u0, u1, u) : 1f;
                var t = new Vector4(s.Advance.x, s.Advance.y, s.Advance.z, weight);
                // (a pressed head's spine vertex lies midway between its two edges, so the pooled rim keeps one width on both)
                Vector3 mid = press ? centre + s.Side * ((wideR - wideL) * .5f) : centre;
                lastMid = mid;
                b.Add(centre - s.Side * wideL, Vector3.zero, t, colorL, new Vector2(u, 0f), data);
                b.Add(mid, Vector3.zero, t, colorM, new Vector2(u, 1f), data);
                b.Add(centre + s.Side * wideR, Vector3.zero, t, colorR, new Vector2(u, 0f), data);
                if (i == 0) continue;
                int p = first + (i - 1) * 3, q = first + i * 3;
                b.Triangle(p, q, p + 1); b.Triangle(p + 1, q, q + 1);
                b.Triangle(p + 1, q + 1, p + 2); b.Triangle(p + 2, q + 1, q + 2);
            }

            int last = first + seg * 3;
            Vector3 end = press ? lastMid : Spine(s, u1);
            float full = w;
            w *= press ? pressEnd * (1f + swell) : HeadScale(s, 1f);   // the tip continues the tapered body
            // a pressed head's nub follows the bent spine (the old round head keeps the stroke's straight axis)
            Vector3 capAxis = s.Axis;
            if (press) { Vector3 ahead = Spine(s, u1) - Spine(s, Mathf.Max(u0, u1 - .04f)); if (ahead.sqrMagnitude > 1e-10f) capAxis = ahead.normalized; }
            var head = new Vector4(s.Advance.x, s.Advance.y, s.Advance.z, moves ? 1f : 0f);
            if (split > 0)
            {
                // forked tip: pointed prongs instead of the round head
                Vector3 left = end - s.Side * w, right = end + s.Side * w;
                for (int k = 0; k < split; k++)
                {
                    Vector3 a = Vector3.Lerp(left, right, k / (float)split), c = Vector3.Lerp(left, right, (k + 1) / (float)split);
                    Vector3 mid = (a + c) * .5f;
                    float spread = k - (split - 1) * .5f;
                    Vector3 tip = mid + s.Axis * (w * (1.5f + .35f * Mathf.Abs(spread))) + s.Side * (w * .45f * spread);
                    int v = b.VertexCount;
                    b.Add(a, Vector3.zero, head, color, new Vector2(u1, 0f), data);
                    b.Add(mid, Vector3.zero, head, color, new Vector2(u1, 1f), data);
                    b.Add(c, Vector3.zero, head, color, new Vector2(u1, 0f), data);
                    b.Add(tip, Vector3.zero, head, color, new Vector2(1f, 0f), data);
                    b.Triangle(v, v + 3, v + 1); b.Triangle(v + 1, v + 3, v + 2);
                }
            }
            else
            {
                // blunt wet head: a half disc
                int fan = b.VertexCount;
                for (int k = 0; k <= CapSteps; k++)
                {
                    float a = Mathf.Lerp(-90f, 90f, k / (float)CapSteps) * Mathf.Deg2Rad;
                    Vector3 p = end + s.Side * (Mathf.Sin(a) * w) + capAxis * (Mathf.Cos(a) * w * pressCap);
                    b.Add(p, Vector3.zero, head, color, new Vector2(Mathf.Lerp(u1, 1f, Mathf.Cos(a)), 0f), data);
                    if (k > 0) b.Triangle(last + 1, fan + k - 1, fan + k);
                }
            }

            if (tail > 0f)
            {
                // dry-brush tail: two quads of the atlas tail cell, U 1 at the wet body
                var tailData = new Vector4(KindTail, s.BirthCel, s.Seed01, CellTail + Mathf.Clamp(s.TailCell, 0, 3));
                var still = new Vector4(s.Advance.x, s.Advance.y, s.Advance.z, moves && !s.HeadOnly ? 1f : 0f);
                int v = b.VertexCount;
                for (int k = 0; k <= 2; k++)
                {
                    float f = k * .5f;
                    Vector3 centre = Spine(s, (tail + .02f) * f);
                    // (a pressed stroke's tail is as wide as the body starts; the old strokes keep their own measure)
                    float half = press ? full * rootShare * TailSpread : w * TailWidth * 1.08f;
                    // a combed start takes over from the tail's hairs: the tail cell's solid end (a block as wide as the tail) is left out
                    float tailU = s.DryStart ? f * (s.DryTailU > 0f ? Mathf.Clamp(s.DryTailU, .3f, 1f) : DryTailUDefault) : f;
                    b.Add(centre - s.Side * half, Vector3.zero, still, color, new Vector2(tailU, 0f), tailData);
                    b.Add(centre + s.Side * half, Vector3.zero, still, color, new Vector2(tailU, 1f), tailData);
                    if (k == 0) continue;
                    int p = v + (k - 1) * 2, q = v + k * 2;
                    b.Triangle(p, q, p + 1); b.Triangle(p + 1, q, q + 1);
                }
            }
            return true;
        }

        // look2 round 2: the body's width over its last HeadTaper share, 1 before it (v = 0 at the body's start, 1 at its end)
        private static float HeadScale(in InkStroke308 s, float v)
        {
            if (s.HeadTaper <= 0f) return 1f;
            float taper = Mathf.Clamp(s.HeadTaper, .05f, 1f);
            float end = s.HeadTaperEnd > 0f ? Mathf.Clamp01(s.HeadTaperEnd) : HeadTaperEndDefault;
            return Mathf.Lerp(1f, end, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((v - (1f - taper)) / taper)));
        }

        /// <summary>A thin dry-brush stroke: the atlas tail cell alone (no wet body).</summary>
        public static bool AddDryStroke(InkBurstBuffer308 b, Vector3 from, Vector3 axis, Vector3 side, float length, float halfWidth, int cell,
            float birthCel, float seed01, float tintWeight, Vector3 advance, float weight)
        {
            if (!b.Room(4, 6)) return false;
            var color = new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(tintWeight) * 255f), (byte)Mathf.RoundToInt(seed01 * 255f), 0, 255);
            var data = new Vector4(KindTail, birthCel, seed01, CellTail + Mathf.Clamp(cell, 0, 3));
            var t = new Vector4(advance.x, advance.y, advance.z, weight);
            Vector3 to = from + axis * length;
            int v = b.VertexCount;
            // the cell's wet end (U 1) is the root, the hairs fly outward
            b.Add(from - side * halfWidth, Vector3.zero, t, color, new Vector2(1f, 0f), data);
            b.Add(from + side * halfWidth, Vector3.zero, t, color, new Vector2(1f, 1f), data);
            b.Add(to - side * halfWidth, Vector3.zero, t, color, new Vector2(0f, 0f), data);
            b.Add(to + side * halfWidth, Vector3.zero, t, color, new Vector2(0f, 1f), data);
            b.Triangle(v, v + 2, v + 1); b.Triangle(v + 1, v + 2, v + 3);
            return true;
        }

        /// <summary>A needle line: a tapered quad the shader keeps at least a pixel and a half wide.</summary>
        public static bool AddNeedle(InkBurstBuffer308 b, Vector3 from, Vector3 direction, float length, float halfWidth, Vector3 planeNormal,
            float birthCel, float seed01, Vector3 advance, float weight)
        {
            if (!b.Room(4, 6)) return false;
            Vector3 side = Vector3.Cross(planeNormal, direction);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(Vector3.up, direction);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            side.Normalize();
            float hw = Mathf.Max(halfWidth, .0001f), tipHalf = Mathf.Max(hw * .15f, .0001f);
            var color = new Color32(0, (byte)Mathf.RoundToInt(seed01 * 255f), 0, 255);
            var t = new Vector4(advance.x, advance.y, advance.z, weight);
            Vector3 to = from + direction * length;
            int v = b.VertexCount;
            b.Add(from - side * hw, -side, t, color, new Vector2(0f, 0f), new Vector4(KindNeedle, birthCel, seed01, hw));
            b.Add(from + side * hw, side, t, color, new Vector2(0f, 1f), new Vector4(KindNeedle, birthCel, seed01, hw));
            b.Add(to - side * tipHalf, -side, t, color, new Vector2(1f, 0f), new Vector4(KindNeedle, birthCel, seed01, tipHalf));
            b.Add(to + side * tipHalf, side, t, color, new Vector2(1f, 1f), new Vector4(KindNeedle, birthCel, seed01, tipHalf));
            b.Triangle(v, v + 2, v + 1); b.Triangle(v + 1, v + 2, v + 3);
            return true;
        }

        /// <summary>An atlas sprite quad (ignition smear, square smear, ring, star).</summary>
        public static bool AddSprite(InkBurstBuffer308 b, Vector3 centre, Vector3 right, Vector3 up, float halfWidth, float halfHeight, int cell,
            float birthCel, float seed01, float tintWeight, Vector3 advance, float weight)
        {
            if (!b.Room(4, 6)) return false;
            var color = new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(tintWeight) * 255f), (byte)Mathf.RoundToInt(seed01 * 255f), 0, 255);
            var data = new Vector4(KindSprite, birthCel, seed01, Mathf.Clamp(cell, 0, 15));
            var t = new Vector4(advance.x, advance.y, advance.z, weight);
            Vector3 x = right * halfWidth, y = up * halfHeight;
            int v = b.VertexCount;
            b.Add(centre - x - y, Vector3.zero, t, color, new Vector2(0f, 0f), data);
            b.Add(centre + x - y, Vector3.zero, t, color, new Vector2(1f, 0f), data);
            b.Add(centre - x + y, Vector3.zero, t, color, new Vector2(0f, 1f), data);
            b.Add(centre + x + y, Vector3.zero, t, color, new Vector2(1f, 1f), data);
            b.Triangle(v, v + 2, v + 1); b.Triangle(v + 1, v + 2, v + 3);
            return true;
        }

        /// <summary>An ink column: vertical strips that rise in cels and drain with round ends (the shader masks them).</summary>
        public static int AddColumn(InkBurstBuffer308 b, Vector3 baseCentre, Vector3 right, Vector3 up, float width, float height, int strips,
            float birthCel, float seed01, float crown = 0f)
        {
            strips = Mathf.Clamp(strips, 1, 40);
            float step = width / strips;
            int made = 0;
            uint hash = (uint)Mathf.RoundToInt(seed01 * 65535f) * 2654435761u + 12345u;
            for (int i = 0; i < strips; i++)
            {
                if (!b.Room(4, 6)) break;
                hash = hash * 1664525u + 1013904223u;
                float r = (hash >> 8) / 16777216f;
                float stripWidth = step * 1.18f;
                // crown > 0: the middle strips stand taller than the edge ones (a rising bundle, not a block)
                float stripHeight = height * (.78f + .22f * r) * Mathf.Lerp(1f - Mathf.Clamp01(crown), 1f, Mathf.Sin(Mathf.PI * (i + .5f) / strips));
                Vector3 centre = baseCentre + right * ((i + .5f) * step - width * .5f);
                var data = new Vector4(KindColumn, birthCel, r, stripWidth / Mathf.Max(stripHeight, .01f));
                var color = new Color32(96, (byte)Mathf.RoundToInt(r * 255f), 0, 255);
                Vector3 x = right * (stripWidth * .5f), y = up * stripHeight;
                int v = b.VertexCount;
                b.Add(centre - x, Vector3.zero, Vector4.zero, color, new Vector2(0f, 0f), data);
                b.Add(centre + x, Vector3.zero, Vector4.zero, color, new Vector2(1f, 0f), data);
                b.Add(centre - x + y, Vector3.zero, Vector4.zero, color, new Vector2(0f, 1f), data);
                b.Add(centre + x + y, Vector3.zero, Vector4.zero, color, new Vector2(1f, 1f), data);
                b.Triangle(v, v + 2, v + 1); b.Triangle(v + 1, v + 2, v + 3);
                made++;
            }
            return made;
        }

        /// <summary>A thin dry stroke winding once round a vertical axis (buff): segments appear one per cel step.</summary>
        public static int AddRibbon(InkBurstBuffer308 b, Vector3 centre, float radius, float fromY, float toY, float turns, float halfWidth, int segments,
            int cell, float firstCel, float celsPerSegment, float seed01, float tintWeight)
        {
            segments = Mathf.Clamp(segments, 3, 32);
            int made = 0;
            float phase = seed01 * Mathf.PI * 2f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = phase + turns * Mathf.PI * 2f * i / segments, a1 = phase + turns * Mathf.PI * 2f * (i + 1) / segments;
                Vector3 p0 = centre + new Vector3(Mathf.Cos(a0) * radius, Mathf.Lerp(fromY, toY, i / (float)segments), Mathf.Sin(a0) * radius);
                Vector3 p1 = centre + new Vector3(Mathf.Cos(a1) * radius, Mathf.Lerp(fromY, toY, (i + 1) / (float)segments), Mathf.Sin(a1) * radius);
                Vector3 axis = p1 - p0; float length = axis.magnitude;
                if (length < 1e-4f) continue;
                axis /= length;
                if (!AddDryStroke(b, p1, -axis, Vector3.up, length * 1.25f, halfWidth, cell + i % 4, firstCel + Mathf.Floor(i * celsPerSegment), seed01, tintWeight, Vector3.zero, 0f)) break;
                made++;
            }
            return made;
        }
    }
}
