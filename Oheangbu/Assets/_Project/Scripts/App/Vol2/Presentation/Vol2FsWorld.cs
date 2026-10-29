using UnityEngine;

namespace Oheangbu.App
{
    // 2026-10-27 먹 세계 맞추기 (Docs/Research/vfx120/world_ink_pass_design.md sections 2 / 3): the two switches of the shared ink pass and the world colour grade.
    // Presentation only: nothing here is read by a rule. Domain reload is disabled, so both statics are reset when Play starts (SubsystemRegistration).
    public static class Vol2FsWorld
    {
        /// <summary>The shared ink layer (Vol2FsInkStage) on / off (free:ink:on|off).</summary>
        public static bool InkOn = true;
        /// <summary>The world colour grade, the world accents and the silver two-tone steel on / off (free:grade:on|off).</summary>
        public static bool GradeOn = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { InkOn = true; GradeOn = true; }

        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / Mathf.Max(1e-5f, b - a)); return t * t * (3f - 2f * t); }
        /// <summary>Trapezoid weight: 0 below a, 1 between b and c, 0 above d.</summary>
        static float Band(float x, float a, float b, float c, float d) => Smooth(a, b, x) * (1f - Smooth(c, d, x));
        /// <summary>Value cap that keeps HDR: up to 1 a plain cap, above 1 the excess is kept at 60 %. Nearly idempotent.</summary>
        static float Cap(float v, float c) => v <= 1f ? Mathf.Min(v, c) : c + (v - 1f) * 0.6f;

        /// <summary>The world palette (ART-COLOR 오행 번안): green -> 생죽 청록 (h .43, no neon), saturated sky / water blue -> deep 현색, pale blue whites -> 은백,
        /// pink / magenta -> silver. Purple (오염), red / orange / ochre / yellow (불 · 흙) and the inks are left alone. Alpha and HDR magnitude are kept; a second
        /// pass changes (almost) nothing, so a colour that already went through a factory is not darkened twice.</summary>
        public static Color Grade(Color c)
        {
            if (!GradeOn) return c;
            float m = Mathf.Max(1f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
            Color.RGBToHSV(new Color(c.r / m, c.g / m, c.b / m), out float h, out float s, out float vn);
            float v = vn * m;
            if (v < 0.05f || s < 0.04f) return c;
            float h0 = h, s0 = s;
            // green: pull the hue to 생죽 청록, no neon (a saturation / value cap). r2: h .36 jade-bamboo, value cap .80 (r1 h .43 / .70 read as ice blue on the leaves - 수, not 목)
            float wg = Band(h0, 0.20f, 0.26f, 0.48f, 0.52f);
            if (wg > 0f) { h = Mathf.Lerp(h, 0.36f, 0.75f * wg); s = Mathf.Lerp(s, Mathf.Min(s, 0.62f), wg); v = Mathf.Lerp(v, Cap(v, 0.80f), wg); }
            // blue / sky: saturated -> deep 현색; pale (foam, spray) -> warm silver white
            float wb = Band(h0, 0.50f, 0.54f, 0.68f, 0.72f);
            float pale = 0f;
            if (wb > 0f)
            {
                float ks = Smooth(0.22f, 0.40f, s0);
                s = Mathf.Lerp(s, Mathf.Min(s, 0.50f), wb * ks);
                v = Mathf.Lerp(v, Cap(v, 0.62f), wb * ks);
                pale = wb * (1f - Smooth(0.14f, 0.24f, s0));      // only real whites (spray, foam): the silver-blue accent (s .26) stays as it is
            }
            // pink / magenta: silver
            float wp = Band(h0, 0.78f, 0.84f, 0.95f, 0.98f);
            if (wp > 0f) s = Mathf.Lerp(s, Mathf.Min(s, 0.18f), wp);
            Color o = Color.HSVToRGB(Mathf.Repeat(h, 1f), Mathf.Clamp01(s), 1f) * v;
            if (pale > 0f) o = Color.Lerp(o, new Color(0.90f, 0.89f, 0.86f) * v, 0.55f * pale);
            o.a = c.a;
            return o;
        }

        /// <summary>A gradient with every colour key graded (alpha keys kept).</summary>
        public static Gradient Grade(Gradient g)
        {
            if (!GradeOn || g == null) return g;
            var n = new Gradient();
            var ck = g.colorKeys;
            for (int i = 0; i < ck.Length; i++) ck[i].color = Grade(ck[i].color);
            n.SetKeys(ck, g.alphaKeys);
            return n;
        }
    }
}
