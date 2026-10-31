using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    // 2026-10-31 (D308-161 / D308-162, PLAN-308-INK-STROKE-SPELLS 1.5 / 1.6): the five bodies' MATERIAL as the stroke's shape - "몸은 색이 아니라 꼴로 말한다". Every body is black ink with ONE accent colour that
    // only lights the core line, the sparks and the ends (the accent itself lives in the material's _Accent; the particles read it from there). All numbers are TEST.
    //   목 송곳 (wood)   long straight stroke, the head splits into 4 bristle strands converging to an awl, hooked thorns 5 - 8 per edge; on contact strands COIL the body (R7); jade grains; end = root threads
    //   화 화살 (fire)   a wedge head, a thin core, EXACTLY 3 dry tongues licking up off the tail, ash flakes (dark, many) and a few vermilion embers; end = it burns from its lower edge, ash rises
    //   토 먹돌 (earth)  short and blunt (1.0 m, almost no tail), 4 flat matte ink stones ride it and drop at the hit, a ground slab lifts 10 cm, ochre dust; end = sinks
    //   금 칼날 (metal)  the thinnest, straightest stroke, a standing faint core, one blade flick at the head; 15 - 20 gold-white sparks out to the side; end = 2-frame flash, gone in 3 frames
    //   수 채찍 (water)  an S whip with a lashing head, wide wet edges, a faint core; after the hit 2 - 3 lines of ink run down the fur, blue-white glints below; end = it slides down
    // Nothing here holds state: readonly tables and pure functions (the particle calls spawn short-lived holders of SpellFxLabShot.Drops).

    public static class InkBody
    {
        /// <summary>The rule body -> the kit's burst body (the orders differ: never cast one to the other).</summary>
        public static SpellFxBurstBody Burst(Body b)
        {
            switch (b)
            {
                case Body.Wood: return SpellFxBurstBody.Wood;
                case Body.Fire: return SpellFxBurstBody.Fire;
                case Body.Earth: return SpellFxBurstBody.Earth;
                case Body.Metal: return SpellFxBurstBody.Metal;
                default: return SpellFxBurstBody.Water;
            }
        }

        /// <summary>The flying stroke of each body (metres, TEST). PLAN 2.1 가: 1.2 - 1.5 m, head 0.3 m; 토: 1.0 m, almost no tail; 금: the thinnest.</summary>
        public static InkStrokeShape Shape(Body b)
        {
            switch (b)
            {
                case Body.Fire:
                    return new InkStrokeShape { Length = 1.5f, HeadW = 0.26f, TorsoW = 0.11f, TailW = 0.05f, HeadZone = 0.30f, TailZone = 0.6f, HeadPoint = 0.30f, TailPoint = 0.2f,
                        FlickHead = 0.05f, FlickTail = 0.3f, Wet0 = 0f, Wet1 = 0.75f, Core = 1f };
                case Body.Earth:
                    return new InkStrokeShape { Length = 1.0f, HeadW = 0.46f, TorsoW = 0.36f, TailW = 0.22f, HeadZone = 0.25f, TailZone = 0.2f, HeadPoint = 0.10f, HeadRound = true, TailPoint = 0.10f,
                        FlickHead = 0f, FlickTail = 0.15f, Wet0 = 0.3f, Wet1 = 0.9f, Core = 1f };
                case Body.Metal:
                    return new InkStrokeShape { Length = 1.7f, HeadW = 0.09f, TorsoW = 0.055f, TailW = 0.012f, HeadZone = 0.25f, TailZone = 0.5f, HeadPoint = 0.22f, TailPoint = 0.15f,
                        FlickHead = 0.9f, FlickTail = 0f, Wet0 = 0f, Wet1 = 0.5f, Core = 1f };
                case Body.Water:
                    return new InkStrokeShape { Length = 1.7f, HeadW = 0.32f, TorsoW = 0.16f, TailW = 0.05f, HeadZone = 0.35f, TailZone = 0.6f, HeadPoint = 0.16f, HeadRound = true, TailPoint = 0.3f,
                        FlickHead = 0.4f, FlickTail = 0.2f, Wet0 = 0.6f, Wet1 = 1f, Core = 1f };
                default:
                    return new InkStrokeShape { Length = 1.4f, HeadW = 0.30f, TorsoW = 0.13f, TailW = 0.025f, HeadZone = 0.35f, TailZone = 0.55f, HeadPoint = 0.30f, TailPoint = 0.25f,
                        FlickHead = 0.15f, FlickTail = 0.5f, Wet0 = 0.1f, Wet1 = 1f, Core = 1f, Split = 4, SplitLen = 0.32f };
            }
        }

        /// <summary>The short mark the head leaves stuck on the body's surface (the "맞은 자국" that stays for the grip): metres long, the body's own head shape.</summary>
        public static InkStrokeShape Mark(Body b)
        {
            var s = Shape(b);
            s.Split = 0;
            s.Length = b == Body.Earth ? 0.35f : b == Body.Metal ? 0.5f : 0.42f;
            s.HeadZone = Mathf.Min(s.HeadZone, s.Length * 0.6f); s.TailZone = s.Length * 0.6f; s.TailPoint = s.Length * 0.45f;
            s.HeadPoint = Mathf.Min(s.HeadPoint, s.Length * 0.4f);
            s.TailW = s.TorsoW * 0.5f;
            return s;
        }

        /// <summary>The thin strand that coils (목) or runs (수): a rope 4 - 5 cm wide with pointed ends.</summary>
        public static InkStrokeShape Strand(Body b, float scale = 1f)
        {
            float w = (b == Body.Water ? 0.035f : 0.045f) * scale;
            return new InkStrokeShape { Length = 1f, HeadW = w * 0.8f, TorsoW = w, TailW = w * 0.7f, HeadZone = 0.15f, TailZone = 0.15f, HeadPoint = 0.12f, TailPoint = 0.06f,
                FlickHead = 0.3f, Wet0 = b == Body.Water ? 1f : 0.6f, Wet1 = 1f, Core = 0.6f };
        }

        /// <summary>A fire tongue / a rising thread: root (u = 0) about <paramref name="w"/> wide, a point at its tip (u = 1), dry.</summary>
        public static InkStrokeShape Tongue(float w, float core)
        {
            return new InkStrokeShape { Length = 1f, HeadW = w * 0.25f, TorsoW = w * 0.75f, TailW = w, HeadZone = 0.2f, TailZone = 0.1f, HeadPoint = 0.18f, TailPoint = 0.02f,
                FlickHead = 0.5f, Wet0 = 0.15f, Wet1 = 0f, Core = core };
        }

        /// <summary>The resting core-line weight between hits (_CoreK): 금 a faint standing core (its blade is the brightest), 수 a faint one along the whip (concept 아), the rest 0 (lit only at the hit, PLAN 1.3).</summary>
        public static float CoreBase(Body b) => b == Body.Metal ? 0.45f : b == Body.Water ? 0.3f : 0f;

        /// <summary>Spark counts at a full-size contact (PLAN 1.5): 금 the most and longest, 목 the fewest.</summary>
        public static int SparkCount(Body b) => b == Body.Metal ? 18 : b == Body.Fire ? 9 : b == Body.Wood ? 5 : 4;

        /// <summary>The body's accent when the material has none (the material's _Accent wins; PBZ reference values).</summary>
        public static Color FallbackAccent(Body b)
        {
            switch (b)
            {
                case Body.Fire: return new Color(0.91f, 0.35f, 0.18f, 1f);
                case Body.Earth: return new Color(0.78f, 0.63f, 0.31f, 1f);
                case Body.Metal: return new Color(0.95f, 0.88f, 0.62f, 1f);
                case Body.Water: return new Color(0.62f, 0.82f, 1f, 1f);
                default: return new Color(0.37f, 0.75f, 0.56f, 1f);
            }
        }

        /// <summary>A material's _Accent as a particle colour (HDR clamped to 0..1, keeping its hue).</summary>
        public static Color AccentOf(Material m, Body b)
        {
            Color c = m != null && m.HasProperty("_Accent") ? m.GetColor("_Accent") : FallbackAccent(b);
            float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (mx > 1f) c = new Color(c.r / mx, c.g / mx, c.b / mx, 1f);
            c.a = 1f;
            return c;
        }

        static readonly Color InkA = SpellFxHexKit.InkA, InkB = SpellFxHexKit.InkB;
        static readonly Color AshA = new Color(0.07f, 0.065f, 0.06f, 0.95f), AshB = new Color(0.2f, 0.17f, 0.15f, 0.9f);

        /// <summary>The contact (PLAN 1.5: three things in ONE frame - ink splats, accent sparks, the core lit; the part does the core and the stop): <paramref name="at"/> = the contact (world), <paramref name="travel"/> = the
        /// stroke's direction there. Splats are small STRETCHED flat splats flying ON and UP only (never back to the caster: judge Q9 / Q10). <paramref name="scale"/> 0.5 for a small second contact.</summary>
        public static void Impact(Body b, Vector3 at, Vector3 travel, Color accent, float scale)
        {
            if (!Application.isPlaying) return;
            Vector3 d = travel.sqrMagnitude > 1e-6f ? travel.normalized : Vector3.forward;
            Vector3 fwdUp = (d + Vector3.up * 0.45f).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, d); if (side.sqrMagnitude < 1e-6f) side = Vector3.right; side.Normalize();
            int nSplat = Mathf.Max(3, Mathf.RoundToInt((b == Body.Water ? 10 : 7) * scale));
            SpellFxLabShot.Drops(at, fwdUp, nSplat, 0.03f, 0.06f, 1.5f, 3.2f, 0.45f, 1.4f, InkA, InkB, b == Body.Water ? 42f : 28f, 0.04f, false, 1.3f, 0.55f);
            int n = Mathf.Max(2, Mathf.RoundToInt(SparkCount(b) * scale));
            Color hot = Color.Lerp(accent, Color.white, 0.35f);
            switch (b)
            {
                case Body.Wood:      // a few small jade grains, forward
                    SpellFxLabShot.Drops(at, fwdUp, n, 0.02f, 0.035f, 0.8f, 1.8f, 0.35f, 0.25f, accent, hot, 50f, 0.05f, false, 0f, 0.4f);
                    break;
                case Body.Fire:      // vermilion embers going UP + dark ash flakes rising
                    SpellFxLabShot.Drops(at, (Vector3.up + d * 0.35f).normalized, n, 0.018f, 0.03f, 1.2f, 2.6f, 0.55f, -0.25f, accent, hot, 35f, 0.06f, false, 1.4f, 0.3f);
                    SpellFxLabShot.Drops(at, Vector3.up, Mathf.RoundToInt(8 * scale), 0.03f, 0.05f, 0.5f, 1.0f, 0.6f, -0.1f, AshA, AshB, 40f, 0.1f, false, 0f, 0.5f);
                    break;
                case Body.Earth:     // ochre dust, low and blunt (the stones and the slab are the part's)
                    SpellFxLabShot.Drops(at, (d + Vector3.up * 0.2f).normalized, n, 0.12f, 0.2f, 0.2f, 0.6f, 0.6f, 0.05f, new Color(accent.r * 0.65f, accent.g * 0.6f, accent.b * 0.5f, 0.45f), new Color(0.55f, 0.44f, 0.3f, 0.35f), 45f, 0.1f, false, 0f, 1.8f);
                    break;
                case Body.Metal:     // the iron sparks: the most and the longest, out to BOTH sides, a little on
                    for (int sg = -1; sg <= 1; sg += 2)
                        SpellFxLabShot.Drops(at, (side * (sg * 0.8f) + d * 0.4f + Vector3.up * 0.2f).normalized, (n + 1) / 2, 0.015f, 0.03f, 2.5f, 5f, 0.3f, 1.2f, accent, hot, 25f, 0.03f, false, 3f, 0.4f);
                    break;
                default:             // water: a few blue-white glints DOWN and on
                    SpellFxLabShot.Drops(at, (Vector3.down * 0.7f + d * 0.5f).normalized, n, 0.02f, 0.035f, 0.6f, 1.4f, 0.45f, 1.6f, accent, hot, 30f, 0.05f, false, 1.5f, 0.4f);
                    break;
            }
        }

        /// <summary>토's ink stones breaking off at the contact: flat angular ink plates with an ochre rim, flung on and falling hard (the kit's armour-plate texture).</summary>
        public static void StonesOff(Vector3 at, Vector3 travel, int count, Color accent)
        {
            if (!Application.isPlaying || count <= 0) return;
            Vector3 d = travel.sqrMagnitude > 1e-6f ? travel.normalized : Vector3.forward;
            SpellFxLabShot.Drops(at, (d + Vector3.up * 0.6f).normalized, count, 0.07f, 0.12f, 1.0f, 2.2f, 0.6f, 2.0f, new Color(0.08f, 0.07f, 0.06f, 1f), accent, 35f, 0.08f, true, 0f, 0.9f);
        }

        /// <summary>갓's torn armour: 2 flat ink flakes (0.2 - 0.28 m), on and toward the camera's side, falling.</summary>
        public static void ArmourFlakes(Vector3 at, Vector3 travel, Vector3 toCam, Color rim)
        {
            if (!Application.isPlaying) return;
            Vector3 d = travel.sqrMagnitude > 1e-6f ? travel.normalized : Vector3.forward;
            Vector3 c = InkMath.Flat(toCam).sqrMagnitude > 1e-6f ? InkMath.Flat(toCam).normalized : Vector3.zero;
            SpellFxLabShot.Drops(at, (d * 0.6f + c * 0.5f + Vector3.up * 0.5f).normalized, 2, 0.2f, 0.28f, 1.6f, 2.8f, 0.8f, 1.8f, new Color(0.05f, 0.05f, 0.05f, 1f), rim, 30f, 0.06f, true, 0f, 0.9f);
        }

        /// <summary>화 in flight: a few dark ash flakes off the tail (and one ember now and then) - the part calls it a handful of times per flight, never per frame.</summary>
        public static void FlightAsh(Vector3 at, Vector3 travel, Color accent, bool ember)
        {
            if (!Application.isPlaying) return;
            Vector3 up = (Vector3.up - (travel.sqrMagnitude > 1e-6f ? travel.normalized : Vector3.zero) * 0.3f).normalized;
            SpellFxLabShot.Drops(at, up, 3, 0.025f, 0.045f, 0.4f, 0.8f, 0.5f, -0.15f, AshA, AshB, 50f, 0.05f, false, 0f, 0.5f);
            if (ember) SpellFxLabShot.Drops(at, up, 1, 0.018f, 0.025f, 0.6f, 1.0f, 0.45f, -0.2f, accent, accent, 30f, 0.03f, false, 1.2f, 0.3f);
        }

        /// <summary>수 after the hit: the drops at the bottom of the runs (plain ink) and a glint.</summary>
        public static void RunDrops(Vector3 at, Color accent)
        {
            if (!Application.isPlaying) return;
            SpellFxLabShot.Drops(at, Vector3.down, 2, 0.03f, 0.05f, 0.2f, 0.5f, 0.4f, 1.6f, InkA, InkB, 20f, 0.02f, false, 0f, 0.4f);
            SpellFxLabShot.Drops(at, Vector3.down, 1, 0.015f, 0.02f, 0.3f, 0.6f, 0.35f, 1.4f, accent, Color.white, 15f, 0.01f, false, 1.2f, 0.3f);
        }

        // ------------------------------------------------------------ what rides the flying stroke

        /// <summary>The body's look riding the flying stroke [<paramref name="s0"/>, <paramref name="sHead"/>] of <paramref name="path"/> (world): 목 hooked thorns on both edges, 화 three tongues off the tail licking up and back,
        /// 토 four flat ink stones beside the head (until <paramref name="stonesOn"/> is false). 금 and 수 carry nothing extra (the blade flick and the S are the shape and the path). <paramref name="time"/> = the look clock
        /// (stepped by the caller for the hand-drawn flicker).</summary>
        public static void RideFlight(Body b, InkMeshBuffer buf, InkCurve path, float s0, float sHead, in InkStrokeShape shape, in InkFace face, in InkPaint paint, Vector3? tipCap, float time, bool stonesOn, InkCurve scratch)
        {
            float vis = sHead - s0;
            if (vis < 0.15f) return;
            switch (b)
            {
                case Body.Wood:
                {
                    int per = Mathf.Clamp(Mathf.RoundToInt(vis * 5f), 2, 7);
                    InkStrokeBuilder.Thorns(buf, path, s0, sHead - shape.SplitLen * 0.5f, sHead, shape, face, paint, per, paint.Seed, tipCap);
                    break;
                }
                case Body.Fire:
                {
                    // EXACTLY 3 tongues (judge r2 나 handle), each shorter than the stroke, rooted on its rear half, never behind the brush tip
                    var tshape = Tongue(0.06f * paint.WidthMul, 0.5f);
                    for (int k = 0; k < 3; k++)
                    {
                        float s = s0 + vis * (0.12f + 0.16f * k);
                        if (s < 0.25f) continue;
                        Vector3 root = path.At(s), t = path.Dir(s);
                        float len = Mathf.Min(0.42f - 0.06f * k, vis * 0.35f);
                        if (len < 0.08f) continue;
                        scratch.Clear();
                        InkStrokeBuilder.Tongue(scratch, root, (-t + Vector3.up * 0.2f), len, paint.Seed * 7f + k * 1.37f, time, 8);
                        var p2 = paint; p2.Seed = InkMath.Frac(paint.Seed + 0.17f * (k + 1));
                        InkStrokeBuilder.Ribbon(buf, scratch, 0f, scratch.Length, tshape, InkFace.Camera(face.Cam), p2, tipCap);
                    }
                    break;
                }
                case Body.Earth:
                {
                    if (!stonesOn) break;
                    for (int k = 0; k < 4; k++)
                    {
                        float s = sHead - Mathf.Min(vis, shape.Length) * (0.18f + 0.18f * k);
                        if (s < s0 + 0.05f || s < 0.35f) continue;
                        Vector3 p = path.At(s), t = path.Dir(s);
                        face.At(p, t, out Vector3 side, out Vector3 nrm);
                        float w = shape.WidthAt(sHead - s, s - s0) * paint.WidthMul;
                        float sign = (k & 1) == 0 ? 1f : -1f;
                        Vector3 c = p + side * (sign * (w * 0.5f + 0.03f)) + Vector3.up * (0.02f * k);
                        float rad = 0.055f + 0.03f * InkMath.Hash(paint.Seed * 5f + k);
                        if (tipCap.HasValue) rad = Mathf.Min(rad, InkStrokeBuilder.NearCap(Vector3.Distance(tipCap.Value, c), Vector3.Distance(face.Cam, c), rad * 2f) * 0.5f);
                        InkStrokeBuilder.Polygon(buf, c, face.Cam - c, t, rad, 5, InkMath.Frac(paint.Seed + 0.11f * k), 0f, 0f, 0.15f, paint.Alpha, 0.75f);
                    }
                    break;
                }
            }
        }
    }
}
