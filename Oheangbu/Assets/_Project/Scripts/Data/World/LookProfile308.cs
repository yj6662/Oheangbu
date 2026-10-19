using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    // #308 look "PBZ-Ink" (SPEC-WORLD-LOOK-308 / PLAN_look308.md, D308-78, TEST): one data source for the low-key, atmospheric base
    // picture and the ink layer drawn over it. LookDriver308 blends the per-realm entries by the camera's realm weights (the same
    // RegionalInkSkyProfile.GetWeights the regional sky uses) and writes shader globals (_OhLook308*), the scene sun / ambient
    // multipliers and a transient post volume. Strength 0, no entry or no driver = the picture before this stage, exactly.
    // Nothing here switches anything on by default: a fresh asset has Strength 0 and no realm.
    // Every colour is sRGB as typed; the driver converts. All numbers TEST until the user has seen stills.
    public sealed class LookProfile308 : ScriptableObject
    {
        /// <summary>Everything that is blended between realms. Multipliers are 1 and amounts 0 in <see cref="Identity"/>.</summary>
        [Serializable]
        public struct Look
        {
            [Header("1 value / light")]
            [Tooltip("x the scene sun intensity (direction is never changed: the >= 25 deg shadow rule and baked casters stay valid)")]
            public float SunIntensity;
            [Tooltip("x the scene sun colour")] public Color SunTint;
            [Tooltip("Sun shadow strength moved toward 1 by this share")] [Range(0, 1)] public float ShadowDeepen;
            [Tooltip("x the ambient the regional sky wrote (probe and colours)")] public float AmbientValue;
            [Tooltip("x ambient colour (cool = blue-grey shadows)")] public Color AmbientTint;
            [Tooltip("Scene exposure in stops, applied in the ink pass before the ink layer (negative = low key)")] public float Exposure;
            [Tooltip("Power contrast around the profile pivot (1 = none)")] public float Contrast;
            [Tooltip("x sky value (the realm sky keeps its hue)")] public float SkyValue;
            [Tooltip("x sky saturation")] public float SkySaturation;

            [Header("2 atmosphere")]
            [Tooltip("x fog and valley-mist colour value")] public float FogValue;
            [Tooltip("x fog colour (realm air tint)")] public Color FogTint;
            [Tooltip("x distance fog amount")] public float FogDensity;
            [Tooltip("x distance fog start (below 1 = fog begins nearer)")] public float FogStart;
            [Tooltip("x valley mist density")] public float MistDensity;
            [Tooltip("Fog brightening toward the sun (in-scatter colour; LDR, not an emission)")] public Color Scatter;
            public float ScatterAmount;
            [Tooltip("Tightness of the in-scatter lobe")] public float ScatterPower;
            [Tooltip("Drifting motes (ash, snow, rain, spores, dust): share of cells that hold one (0 = none)")] [Range(0, 1)] public float MoteDensity;
            [Tooltip("Mote size, px at 1080p")] public float MoteSize;
            [Tooltip("Fall speed m/s (negative rises)")] public float MoteFall;
            [Tooltip("Sideways drift m/s")] public float MoteWind;
            [Tooltip("Vertical stretch (1 = round, 8+ = rain streak)")] public float MoteStreak;
            [Tooltip("Mote colour, a = opacity. Never brighter than paper: motes are pigment, not light")] public Color MoteColour;

            [Header("3 surface response (screen space)")]
            public Color RimColour;
            [Tooltip("Back / rim light on silhouettes (0 = none)")] public float RimAmount;
            public float RimPower;
            [Tooltip("Wetness 0..1: upward faces darken and take a tight highlight")] [Range(0, 1)] public float Wetness;
            [Range(0, 1)] public float WetDarken;
            public float WetSpecular;
            public float WetGloss;

            [Header("4 grade")]
            [Tooltip("x saturation of the base picture (accent hues are protected)")] public float Saturation;
            public Color ShadowTint;
            [Range(0, 1)] public float ShadowTintAmount;
            public Color HighlightTint;
            [Range(0, 1)] public float HighlightTintAmount;
            [Tooltip("Realm 담채: a thin wash that lives in the mid-tones")] public Color Wash;
            [Range(0, 1)] public float WashAmount;

            [Header("5 ink layer")]
            [Tooltip("x InkWash297 _InkTone (below 1 = the realistic base shows through)")] public float InkTone;
            [Tooltip("x the distance paper wash (the 농담 bands replace it)")] public float PaperWash;
            [Tooltip("x highlights toward paper")] public float HighlightPaper;
            [Tooltip("x line darkness")] public float LineInk;
            [Tooltip("x the near-mountain ink band (InkWash297 _NearHillInk): below 1 keeps near slopes from sinking to black in a low-key base")] public float NearHillInk;
            [Tooltip("Line width near the eye, in texels (1 = today)")] public float LineWidth;
            [Tooltip("Line darkness on faces turned to the sun (1 = same as the shadow side)")] public float LineLitSide;
            [Tooltip("Ink pooling: perceptual value that is fully pooled")] public float PoolLow;
            [Tooltip("Ink pooling: perceptual value where pooling ends")] public float PoolHigh;
            [Range(0, 1)] public float PoolAmount;
            [Tooltip("Dark rim where a pool dries (wet edge)")] [Range(0, 1)] public float WetEdge;
            [Tooltip("농담 bands: metres where the first dilution step starts")] public float BandStart;
            [Tooltip("농담 bands: metres where the last step is reached")] public float BandEnd;
            [Range(0, 1)] public float BandAmount;
            [Tooltip("Colour of the air the far bands dilute into (담묵)")] public Color Air;
            [Tooltip("Extra hanji fibre in highlights (x the material grain)")] public float PaperGrain;
            [Tooltip("갈필 break-up of silhouettes against the sky")] [Range(0, 1)] public float DryBrush;

            public static Look Identity => new Look
            {
                SunIntensity = 1f, SunTint = Color.white, ShadowDeepen = 0f, AmbientValue = 1f, AmbientTint = Color.white, Exposure = 0f, Contrast = 1f,
                SkyValue = 1f, SkySaturation = 1f,
                FogValue = 1f, FogTint = Color.white, FogDensity = 1f, FogStart = 1f, MistDensity = 1f, Scatter = Color.white, ScatterAmount = 0f, ScatterPower = 8f,
                MoteDensity = 0f, MoteSize = 2f, MoteFall = 0f, MoteWind = 0f, MoteStreak = 1f, MoteColour = new Color(.5f, .5f, .5f, 0f),
                RimColour = Color.white, RimAmount = 0f, RimPower = 3f, Wetness = 0f, WetDarken = 0f, WetSpecular = 0f, WetGloss = 48f,
                Saturation = 1f, ShadowTint = Color.gray, ShadowTintAmount = 0f, HighlightTint = Color.gray, HighlightTintAmount = 0f, Wash = Color.gray, WashAmount = 0f,
                InkTone = 1f, PaperWash = 1f, HighlightPaper = 1f, LineInk = 1f, NearHillInk = 1f, LineWidth = 1f, LineLitSide = 1f,
                PoolLow = 0f, PoolHigh = .1f, PoolAmount = 0f, WetEdge = 0f, BandStart = 400f, BandEnd = 3000f, BandAmount = 0f, Air = Color.gray,
                PaperGrain = 0f, DryBrush = 0f,
            };

            static Color C(Color s, Color x, float w) => new Color(s.r + x.r * w, s.g + x.g * w, s.b + x.b * w, s.a + x.a * w);

            /// <summary>sum += x * w, field by field (weighted mean when the weights add up to 1).</summary>
            public static void Add(ref Look s, in Look x, float w)
            {
                s.SunIntensity += x.SunIntensity * w; s.SunTint = C(s.SunTint, x.SunTint, w); s.ShadowDeepen += x.ShadowDeepen * w;
                s.AmbientValue += x.AmbientValue * w; s.AmbientTint = C(s.AmbientTint, x.AmbientTint, w); s.Exposure += x.Exposure * w; s.Contrast += x.Contrast * w;
                s.SkyValue += x.SkyValue * w; s.SkySaturation += x.SkySaturation * w;
                s.FogValue += x.FogValue * w; s.FogTint = C(s.FogTint, x.FogTint, w); s.FogDensity += x.FogDensity * w; s.FogStart += x.FogStart * w; s.MistDensity += x.MistDensity * w;
                s.Scatter = C(s.Scatter, x.Scatter, w); s.ScatterAmount += x.ScatterAmount * w; s.ScatterPower += x.ScatterPower * w;
                s.MoteDensity += x.MoteDensity * w; s.MoteSize += x.MoteSize * w; s.MoteFall += x.MoteFall * w; s.MoteWind += x.MoteWind * w; s.MoteStreak += x.MoteStreak * w;
                s.MoteColour = C(s.MoteColour, x.MoteColour, w);
                s.RimColour = C(s.RimColour, x.RimColour, w); s.RimAmount += x.RimAmount * w; s.RimPower += x.RimPower * w;
                s.Wetness += x.Wetness * w; s.WetDarken += x.WetDarken * w; s.WetSpecular += x.WetSpecular * w; s.WetGloss += x.WetGloss * w;
                s.Saturation += x.Saturation * w; s.ShadowTint = C(s.ShadowTint, x.ShadowTint, w); s.ShadowTintAmount += x.ShadowTintAmount * w;
                s.HighlightTint = C(s.HighlightTint, x.HighlightTint, w); s.HighlightTintAmount += x.HighlightTintAmount * w;
                s.Wash = C(s.Wash, x.Wash, w); s.WashAmount += x.WashAmount * w;
                s.InkTone += x.InkTone * w; s.PaperWash += x.PaperWash * w; s.HighlightPaper += x.HighlightPaper * w; s.LineInk += x.LineInk * w;
                s.LineWidth += x.LineWidth * w; s.LineLitSide += x.LineLitSide * w; s.NearHillInk += x.NearHillInk * w;
                s.PoolLow += x.PoolLow * w; s.PoolHigh += x.PoolHigh * w; s.PoolAmount += x.PoolAmount * w; s.WetEdge += x.WetEdge * w;
                s.BandStart += x.BandStart * w; s.BandEnd += x.BandEnd * w; s.BandAmount += x.BandAmount * w; s.Air = C(s.Air, x.Air, w);
                s.PaperGrain += x.PaperGrain * w; s.DryBrush += x.DryBrush * w;
            }

            public static Look Lerp(in Look a, in Look b, float t)
            {
                t = Mathf.Clamp01(t);
                if (t <= 0f) return a;
                if (t >= 1f) return b;
                var s = default(Look);
                s.SunTint = s.AmbientTint = s.FogTint = s.Scatter = s.MoteColour = s.RimColour = s.ShadowTint = s.HighlightTint = s.Wash = s.Air = Color.clear;
                Add(ref s, a, 1f - t); Add(ref s, b, t);
                return s;
            }
        }

        [Serializable]
        public struct RealmEntry
        {
            public RealmId Id;
            public Look Look;
        }

        /// <summary>A place inside a realm with its own mood (the capital's streets): blended over the realm result.</summary>
        [Serializable]
        public struct Zone
        {
            public string Id;
            [Tooltip("World XZ centre (m)")] public Vector2 Centre;
            [Tooltip("Full inside this radius (m)")] public float Radius;
            [Tooltip("Fades out over this many metres beyond the radius")] public float Falloff;
            [Range(0, 1)] public float Weight;
            public Look Look;
        }

        /// <summary>URP post volume values (a transient global volume, weight = Strength). Absolute, not per realm.</summary>
        [Serializable]
        public struct PostGrade
        {
            [Tooltip("Off = the scene's own volume profile is left alone")] public bool Override;
            public float Exposure;
            public float Contrast;
            public float Saturation;
            [Range(0, 1)] public float Vignette;
            [Range(0, 1)] public float FilmGrain;
            [Tooltip("0 keep the scene's tonemapper, 1 Neutral, 2 ACES")] [Range(0, 2)] public int Tonemap;
        }

        [Tooltip("0 = off (today's picture). The whole look scales with this")]
        [Range(0, 1)] public float Strength;
        [Tooltip("Seconds for the look to follow the camera into another realm")]
        [Min(.05f)] public float ResponseSeconds = 2.5f;
        [Tooltip("Linear scene value the contrast pivots on")]
        [Range(.02f, .5f)] public float Pivot = .18f;

        [Header("Accent hues kept from the desaturation (ART-COLOR: 주홍, 비색)")]
        public Color AccentA = new Color(.84f, .27f, .16f);
        public Color AccentB = new Color(.42f, .68f, .60f);
        [Range(0, 1)] public float AccentProtect;
        [Tooltip("Chroma (max-min)/max where protection starts / is full")]
        public Vector2 AccentChroma = new Vector2(.35f, .6f);

        [Header("Ink layer shape (shared by every realm)")]
        [Range(1, 6)] public int BandSteps = 4;
        [Range(.02f, .5f)] public float BandSoftness = .18f;
        [Tooltip("Metres over which the near line width falls back to one texel")]
        [Min(1f)] public float LineWidthFade = 40f;
        [Tooltip("갈필: reach toward the sky in px at 1080p, world noise scale, noise threshold")]
        public Vector3 DryBrushShape = new Vector3(3f, .35f, .45f);
        [Tooltip("Perceptual value where the extra highlight fibre starts / is full")]
        public Vector2 PaperValue = new Vector2(.55f, .9f);
        [Tooltip("Mote shells: nearest and farthest distance from the eye (m)")]
        public Vector2 MoteShell = new Vector2(3f, 22f);

        public PostGrade Post;
        public RealmEntry[] Realms = Array.Empty<RealmEntry>();
        public Zone[] Zones = Array.Empty<Zone>();

        /// <summary>True when the profile can drive a picture: a positive, finite strength and at least one realm entry.</summary>
        public bool Usable => Strength > 0f && !float.IsNaN(Strength) && !float.IsInfinity(Strength) && Realms != null && Realms.Length > 0
            && ResponseSeconds > 0f && Pivot > 0f;

        /// <summary>The realm's look; false (and Identity) when the profile has no entry for it.</summary>
        public bool TryGet(RealmId id, out Look look)
        {
            if (Realms != null)
                for (int i = 0; i < Realms.Length; i++)
                    if (Realms[i].Id == id) { look = Realms[i].Look; return true; }
            look = Look.Identity;
            return false;
        }

        /// <summary>Zone weight at a world position (0 outside, Weight inside, smooth over Falloff).</summary>
        public static float ZoneWeight(in Zone z, Vector3 position)
        {
            if (z.Weight <= 0f || z.Radius <= 0f) return 0f;
            float dx = position.x - z.Centre.x, dz = position.z - z.Centre.y;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            float t = Mathf.Clamp01((d - z.Radius) / Mathf.Max(z.Falloff, .01f));
            return z.Weight * (1f - t * t * (3f - 2f * t));
        }

        /// <summary>The look at a world position: realm entries weighted by <paramref name="realmWeights"/> (index = (int)RealmId,
        /// summing to 1; a realm without an entry contributes Identity), then the zones. Allocation-free.</summary>
        public Look Evaluate(float[] realmWeights, Vector3 position)
        {
            var sum = default(Look);
            sum.SunTint = sum.AmbientTint = sum.FogTint = sum.Scatter = sum.MoteColour = sum.RimColour = sum.ShadowTint = sum.HighlightTint = sum.Wash = sum.Air = Color.clear;
            var identity = Look.Identity;
            float total = 0f;
            if (realmWeights != null)
                for (int r = 0; r < realmWeights.Length; r++)
                {
                    float w = realmWeights[r];
                    if (!(w > 0f)) continue;
                    if (TryGet((RealmId)r, out var look)) Look.Add(ref sum, look, w); else Look.Add(ref sum, identity, w);
                    total += w;
                }
            if (total <= 1e-5f) sum = identity;
            else if (Mathf.Abs(total - 1f) > 1e-4f)
            {
                // weights that do not add up to 1 (truncated buffer): renormalise through a second pass
                var n = default(Look);
                n.SunTint = n.AmbientTint = n.FogTint = n.Scatter = n.MoteColour = n.RimColour = n.ShadowTint = n.HighlightTint = n.Wash = n.Air = Color.clear;
                Look.Add(ref n, sum, 1f / total);
                sum = n;
            }
            if (Zones != null)
                for (int i = 0; i < Zones.Length; i++)
                {
                    float w = ZoneWeight(Zones[i], position);
                    if (w > 0f) sum = Look.Lerp(sum, Zones[i].Look, w);
                }
            return sum;
        }
    }
}
