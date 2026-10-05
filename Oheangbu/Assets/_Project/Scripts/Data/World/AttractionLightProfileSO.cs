using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    /// <summary>#308 먼 불빛 profile (SPEC-ATTRACTION-LIGHT-308, all values TEST). The look of the far glow card of every man-made
    /// attraction light kind (LDB-ATTRACTION 확정 인력원: 주막 등불 / ART-INK 광원 상등: 성황당 촛불, 광맥). One entry per kind;
    /// the editor tool (FarLight308) bakes one material per entry from these numbers and <c>farlight308.json</c> fills them — the
    /// shader and the code hold no number. Contamination and enemies have no kind here on purpose (오염은 빛나지 않는다).</summary>
    [CreateAssetMenu(menuName = "Oheangbu/World/Attraction Light Profile 308")]
    public sealed class AttractionLightProfileSO : ScriptableObject
    {
        public enum Kind { InnLantern = 0, ShrineCandle = 1, OreVein = 2 }

        [Serializable]
        public sealed class Entry
        {
            public Kind Kind = Kind.InnLantern;
            [Tooltip("Baked by FarLight308 (sync / apply) from this entry. Never edited by hand.")]
            public Material Material;
            [Tooltip("Hue. 주막 등롱 = ElementPaletteSO 안식 「등불 주황」; 성황당 촛불 = flame.mat")]
            [ColorUsage(false, false)] public Color Colour = new Color(1f, .58f, .26f);
            [Tooltip("Linear peak of the card centre. Peak x (1 + Flicker) + (1 - Cover) must stay <= MaxCentre (under the Bloom threshold; the soft knee still takes a few percent).")]
            [Range(0f, 1.1f)] public float Peak = .8f;
            [Tooltip("Strength left at FarFade.x (colour and coverage together): the card thins with distance like a thing in the air, not a marker")]
            [Range(0f, 1f)] public float FarPeak = .8f;
            [Tooltip("1 = paints over the background (a dab of pigment), 0 = purely additive")]
            [Range(0f, 1f)] public float Cover = .85f;
            [Range(1f, 2f)] public float Chroma = 1.1f;
            [Tooltip("Gaussian falloff across the card; the core (half maximum) is about 0.79 x radius wide at 4")]
            [Range(1f, 12f)] public float Falloff = 4f;
            [Tooltip("World radius of the card when it is larger than the pixel clamp (m)")]
            [Min(0f)] public float WorldRadius = .35f;
            [Tooltip("Minimum on-screen radius at NearFade.y / at FarFade.x, and the maximum, in pixels of a 1080-high screen")]
            [Min(0f)] public float MinPx = 7f;
            [Min(0f)] public float MinPxFar = 4f;
            [Min(0f)] public float MaxPx = 16f;
            [Tooltip("Largest world radius (m). Wins over the pixel floor: past that distance the card shrinks on screen like any object.")]
            [Min(0f)] public float MaxWorldRadius = 1.6f;
            [Tooltip("Hidden below x m (the real flame / paper is there), full from y m")]
            public Vector2 NearFade = new Vector2(40f, 90f);
            [Tooltip("Full to x m, gone at y m")]
            public Vector2 FarFade = new Vector2(600f, 800f);
            [Tooltip("Metres the card is pulled toward the camera so the lamp's own body does not bite it")]
            [Min(0f)] public float Pull = .6f;
            [Range(0f, .2f)] public float Flicker = .04f;
            [Range(0f, 4f)] public float FlickerSpeed = 1.1f;
            [Range(0f, .5f)] public float InkRing;
        }

        [Tooltip("Oheangbu/Finish297/FarGlow308 (kept here so a build includes it)")]
        public Shader GlowShader;
        [Tooltip("Reference screen height of MinPx / MaxPx")]
        [Min(1f)] public float ReferenceHeight = 1080f;
        [Tooltip("Cap on Peak x (1 + Flicker) + (1 - Cover): the brightest the card centre can get over a background of 1.0 (the lamp's own HDR body behind it is not counted: under a pixel at card distances). Must stay under the Bloom threshold.")]
        [Range(.5f, 1.15f)] public float MaxCentre = 1.1f;
        [Tooltip("Bloom threshold of the world post profile this cap is checked against (Macro_Subtle_Post 1.15)")]
        [Min(0f)] public float BloomThreshold = 1.15f;
        public Entry[] Entries = Array.Empty<Entry>();

        public Entry Find(Kind kind)
        {
            var list = Entries;
            if (list == null) return null;
            for (int i = 0; i < list.Length; i++) if (list[i] != null && list[i].Kind == kind) return list[i];
            return null;
        }

        /// <summary>Brightest linear value the centre of this entry's card can reach over a background of 1.0, at any distance
        /// (the strength a runs from 1 down to FarPeak: centre = 1 + a x (Peak x (1 + Flicker) - Cover), linear in a).</summary>
        public static float CentreOverWhite(Entry e)
        {
            if (e == null) return 0f;
            float slope = e.Peak * (1f + e.Flicker) - e.Cover;
            return 1f + Mathf.Max(slope, slope * Mathf.Clamp01(e.FarPeak));
        }

        /// <summary>Share of a pixel of brightness <paramref name="centre"/> that URP's Bloom prefilter keeps (0..1). URP turns the
        /// volume threshold from gamma to linear and uses a soft knee of half the threshold, so a value under the threshold still
        /// contributes a little — "under the threshold" is not "no bloom".</summary>
        public static float BloomShare(float centre, float thresholdGamma)
        {
            float threshold = Mathf.GammaToLinearSpace(thresholdGamma), knee = threshold * .5f;
            float soft = Mathf.Clamp(centre - threshold + knee, 0f, 2f * knee);
            soft = soft * soft / (4f * knee + 1e-4f);
            return Mathf.Max(centre - threshold, soft) / Mathf.Max(centre, 1e-4f);
        }

        public bool WithinCaps(Entry e) => e != null && CentreOverWhite(e) <= MaxCentre + 1e-4f && MaxCentre < BloomThreshold;

        /// <summary>Writes one entry into a FarGlow308 material (the only place the numbers reach the shader).</summary>
        public void Write(Entry e, Material m)
        {
            if (e == null || m == null) return;
            m.SetColor("_Color", e.Colour);
            m.SetFloat("_Peak", e.Peak); m.SetFloat("_Cover", e.Cover); m.SetFloat("_Chroma", e.Chroma); m.SetFloat("_Falloff", e.Falloff);
            m.SetFloat("_Radius", e.WorldRadius); m.SetFloat("_MinPx", e.MinPx); m.SetFloat("_MaxPx", e.MaxPx); m.SetFloat("_RefHeight", ReferenceHeight);
            m.SetFloat("_FarPeak", e.FarPeak); m.SetFloat("_MinPxFar", e.MinPxFar); m.SetFloat("_MaxWorld", e.MaxWorldRadius);
            m.SetVector("_NearFade", new Vector4(e.NearFade.x, e.NearFade.y, 0f, 0f));
            m.SetVector("_FarFade", new Vector4(e.FarFade.x, e.FarFade.y, 0f, 0f));
            m.SetFloat("_Pull", e.Pull); m.SetFloat("_Flicker", e.Flicker); m.SetFloat("_FlickerSpeed", e.FlickerSpeed); m.SetFloat("_InkRing", e.InkRing);
        }
    }
}
