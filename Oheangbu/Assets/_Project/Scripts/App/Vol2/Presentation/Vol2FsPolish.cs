using UnityEngine;

namespace Oheangbu.App
{
    // 2026-10-27 per-glyph polish (showcase120_plan.md step 3; the user: "공통 재질 다듬기부터 하고, 그다음 글자별 폴리싱, 그다음 묶음 5로 가자."):
    // small shared recipes the polished glyphs use. Presentation only: nothing here is read by a rule, nothing decides or delays anything.

    /// <summary>Per-glyph polish helpers.</summary>
    public static class Vol2FsPolish
    {
        /// <summary>A licking flame: the plain fs_flame tongue sheet (never the flipbook - Vol2FsPs.NoFlip), coloured by its erosion on the fire ramp with a
        /// short ramp (orange-red bodies, yellow only in the hottest pockets), the noise scrolling up. Batches 0 - 4 / the material pass: big flipbook or
        /// puffy tongues at intensity ~2 read as yellow paper blobs from the shoulder camera.</summary>
        public static Vol2FsMat Lick(float intensity = 1.35f, float addMix = 0.55f, float rampScale = 1.1f)
        {
            var m = Vol2FsMat.Make("fs_flame", Color.white, intensity, addMix);
            m.Ramp = "fs_ramp_fire"; m.RampScale = rampScale; m.NoiseAmt = 0.62f; m.Erode = 0.2f; m.NoiseScale = 1.3f; m.Scroll = new Vector2(0f, -1.5f);
            return m;
        }

        /// <summary>A licking-flame particle system description: tall upright tongues (no random spin), rising and thinning.</summary>
        public static Vol2FsPs Licks(Material mat, float size0, float size1, float life0, float life1, float rise, int max, float aspect = 2.2f)
            => new Vol2FsPs
            {
                Mat = mat, NoFlip = true, Aspect = aspect, RandRot = 14f, Life0 = life0, Life1 = life1, Speed0 = 0.1f, Speed1 = 0.5f, Size0 = size0, Size1 = size1,
                Shape = ParticleSystemShapeType.Sphere, Radius = 0.12f, Tiles = 2, VelOverLife = new Vector3(0f, rise, 0f), Noise = 0.55f, NoiseFreq = 1.6f,
                SizeStart = 0.55f, SizePeak = 1f, SizePeakAt = 0.22f, SizeEnd = 0.2f, AlphaHoldTo = 0.15f, Max = max,
            };

        /// <summary>Dark soot rising off a fire (the ink world's fire carries soot): soft puffs, low opacity.</summary>
        public static Vol2FsMat Soot(float opacity = 0.42f) { var s = Vol2FsMat.Make("fs_puff", new Color(0.06f, 0.045f, 0.04f, 1f), 1f, 0f); s.Opacity = opacity; s.NoiseAmt = 0.65f; s.Erode = 0.4f; return s; }

        /// <summary>Ring glyphs from the shoulder camera (the camera stands just outside the 2 m band): full height on the far arc and ~0.9 on the sides (the
        /// ring's edges frame the shot - it reads closed from inside it), dipping only on the short arc straight behind the caster where it would fill the lens.</summary>
        public static float RingHeight(float ang)
        {
            float a = Mathf.Abs(Mathf.DeltaAngle(0f, ang * Mathf.Rad2Deg));
            return Mathf.Lerp(1f, 0.9f, Vol2Fs.Smooth(50f, 100f, a)) * Mathf.Lerp(1f, 0.3f, Vol2Fs.Smooth(125f, 160f, a));
        }
    }
}
