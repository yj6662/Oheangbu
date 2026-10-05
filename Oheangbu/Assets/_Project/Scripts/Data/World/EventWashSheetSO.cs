using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    /// <summary>#308 권역 담채 sheet (SPEC-EVENT-WASH-308 §4–5, D308-6 / D308-6b, all values TEST). A campaign stage that is
    /// available (prerequisites met, not completed) washes the catalog place it maps to. Places come from WorldLocationCatalog ids —
    /// never Stage.Destination. Colours come from ElementPaletteSO by meaning (the planner resolves them; no colour is stored here).
    ///  * 진행 (D308-6b): a wide, flat colour-haze ellipsoid in the AIR above the place (InkWash297 volume term) — seen from afar it
    ///    rises above the ridges, up close the air above the place is very faintly tinted; ground, characters and props are not.
    ///    Its geometry is baked per entry by the editor (wash308-sheet, height field) — an unbaked entry shows nothing.
    ///  * 안식·서사: the D308-6 surface term, unchanged. 보상 (비색): off (RewardOn false).</summary>
    [CreateAssetMenu(menuName = "Oheangbu/World/Event Wash Sheet 308")]
    public sealed class EventWashSheetSO : ScriptableObject
    {
        public enum Meaning { Auto = 0, Progress = 1, Rest = 2, Reward = 3, Narrative = 4, None = 5 }

        [Serializable]
        public sealed class Entry
        {
            public string StageId = "";
            [Tooltip("WorldLocationCatalog entry id (not Stage.Destination / DestinationId)")]
            public string LocationId = "";
            public Meaning Meaning = Meaning.Auto;
            [Min(0f)] public float StrengthScale = 1f;
            [Tooltip("Progress volume floor (world y, m): highest ground of the place disc + VolumeBaseClear. Baked by wash308-sheet; 0 = not baked")]
            public float VolumeBaseY;
            [Tooltip("Progress volume top (world y, m): max(ground + VolumeMinRise, ridge percentile + VolumeRidgeClear). Baked by wash308-sheet")]
            public float VolumeTopY;
            [Tooltip("Progress volume horizontal semi-axis (m), after the NoWash / boss distance limits. Baked by wash308-sheet; 0 = not baked")]
            public float VolumeRadius;
        }

        [Serializable]
        public sealed class RealmElement
        {
            [Tooltip("WorldLocationCatalog realm id, e.g. realm_cheongrim")]
            public string RealmId = "";
            [Tooltip("오행 초성 (CSV 3열 미러): ㄱ 목 / ㄴ 화 / ㅁ 토 / ㅅ 금 / ㅇ 수")]
            public string Initial = "";
        }

        [Header("Global (TEST)")]
        [Range(1, 8)] public int MaxZones = 8;
        [Min(.05f)] public float PollSeconds = .5f;
        [Min(0f)] public float FadeInSeconds = 3f;
        [Min(0f)] public float FadeOutSeconds = 5f;
        [Min(0f)] public float RadiusScale = 1f;
        [Range(0f, 1f)] public float Feather = .35f;
        [Range(0f, 1f)] public float EdgeNoise = .12f;
        [Tooltip("World XZ brush-noise frequency of the reverse-bleed edge (1/m)")]
        [Min(0f)] public float EdgeNoiseScale = .025f;
        [Range(0f, 1f)] public float Chroma = .14f;
        [Range(0f, 1f)] public float Lift = .10f;
        [Tooltip("보상(이면) strength multiplier — LDB-ATTRACTION 이면 인력은 한 박자 약하게 (unused while RewardOn is false; revert path)")]
        [Range(0f, 1f)] public float OptionalScale = .6f;
        [Min(0f)] public float AirRadiusScale = 1.5f;
        [Range(0f, 1f)] public float AirTint = .25f;
        [Tooltip("Surface zones (안식·서사): eye-to-edge distance cut (m)")]
        [Min(0f)] public float MaxDistance = 1600f;
        [Tooltip("One-frame camera move above this (m) re-polls and sets the fades without a transition (teleport, load)")]
        [Min(0f)] public float SnapDistance = 60f;

        [Header("Meanings (D308-6b)")]
        [Tooltip("진행 = realm element colour (D308-6b, TEST). false = revert path (no progress wash)")]
        public bool ProgressOn = true;
        [Tooltip("보상 비색 wash. D308-6b: off (progress colour only). Kept for the revert path")]
        public bool RewardOn;
        [Tooltip("진행 as the air volume above the place (D308-6b). false = the D308-6 ground wash (comparison / revert)")]
        public bool ProgressAsVolume = true;

        [Header("Progress volume (D308-6b, TEST)")]
        [Tooltip("Volume radius = catalog place radius x this (then clamped to Min..Max)")]
        [Min(0f)] public float VolumeRadiusScale = 4f;
        [Min(1f)] public float VolumeRadiusMin = 300f;
        [Min(1f)] public float VolumeRadiusMax = 420f;
        [Tooltip("Floor = highest ground inside the place disc + this (m): eaves 8–12 m, trees 15–25 m stay under it")]
        [Min(0f)] public float VolumeBaseClear = 25f;
        [Tooltip("Top >= ground at the centre + this (m)")]
        [Min(0f)] public float VolumeMinRise = 160f;
        [Tooltip("Ridge sample ring around the place: inner, outer radius (m)")]
        public Vector2 VolumeRidgeRing = new Vector2(250f, 1500f);
        [Tooltip("Percentile of the ring heights taken as the ridge (nearest rank)")]
        [Range(0f, 100f)] public float VolumeRidgePercentile = 90f;
        [Tooltip("Top >= ridge percentile + this (m): the upper part clears the surrounding ridges from 1–2 km")]
        [Min(0f)] public float VolumeRidgeClear = 260f;
        [Tooltip("Radius <= distance to a NoWash / boss / growth place - its radius - this (m)")]
        [Min(0f)] public float VolumeNoWashMargin = 80f;
        [Tooltip("rho0: density at the ellipsoid centre (1/m)")]
        [Min(0f)] public float VolumeDensity = .003f;
        [Tooltip("Glaze alpha ceiling: alpha = Cap x (1 - exp(-tau))")]
        [Range(0f, 1f)] public float VolumeCap = .22f;
        [Tooltip("Pigment saturation of the palette colour (1 = palette as is, 0 = no tint)")]
        [Range(0f, 1f)] public float VolumePigment = .85f;
        [Tooltip("Value preservation of the glaze (a colour haze, not darkening smoke)")]
        [Range(0f, 1f)] public float VolumeValueKeep = .75f;
        [Tooltip("Nothing within this distance of the camera is tinted (m): hands, brush, characters, props")]
        [Min(0f)] public float VolumeNearClip = 30f;
        [Tooltip("Multiplier while the camera is horizontally inside the volume radius")]
        [Range(0f, 1f)] public float VolumeNearKeep = .35f;
        [Tooltip("Near-keep ramp from the volume radius outward (m)")]
        [Min(1f)] public float VolumeNearRamp = 600f;
        [Tooltip("Multiplier of near surfaces (looking down through the volume onto the place); 1 for the sky")]
        [Range(0f, 1f)] public float VolumeSurfaceKeep = .3f;
        [Tooltip("Surface-keep ramp: surface distance start, end (m)")]
        public Vector2 VolumeSurfaceRamp = new Vector2(200f, 900f);
        [Tooltip("Reverse-bleed outline jitter (unit-space density)")]
        [Range(0f, 1f)] public float VolumeEdgeNoise = .3f;
        [Tooltip("Ink mottle of the optical depth (x 1 ± 2·mottle·n)")]
        [Range(0f, 1f)] public float VolumeMottle = .4f;
        [Tooltip("Noise frequency in the ellipsoid's unit space")]
        [Min(0f)] public float VolumeNoiseScale = 2.2f;
        [Tooltip("Eye-to-volume-edge distance cut (m)")]
        [Min(0f)] public float VolumeMaxDistance = 3000f;
        [Tooltip("Fade band before the distance cut (m)")]
        [Min(0f)] public float VolumeDistanceFade = 600f;

        [Header("Realm -> element")]
        public RealmElement[] RealmElements =
        {
            new RealmElement { RealmId = "realm_cheongrim", Initial = "ㄱ" },
            new RealmElement { RealmId = "realm_jeokro", Initial = "ㄴ" },
            new RealmElement { RealmId = "realm_hwanggyeong", Initial = "ㅁ" },
            new RealmElement { RealmId = "realm_cheolong", Initial = "ㅅ" },
            new RealmElement { RealmId = "realm_hyeongang", Initial = "ㅇ" },
        };

        [Header("Exceptions (applied after the automatic rule)")]
        [Tooltip("Progress/Reward here -> Narrative (토 황토 reads as the 황경 contamination colour); Rest stays Rest")]
        public string[] NarrativeRealms = { "realm_hwanggyeong" };
        [Tooltip("Every meaning here -> Narrative (폐광: 청록 광맥·버력 장사 기관 예고와 겹침)")]
        public string[] NarrativeLocations = { "mine_interior" };
        [Tooltip("No wash (boss arenas, contamination candidates — INFERRED list, confirmed with wash308-sheet:dry)")]
        public string[] NoWashLocations = { "arena_jeokro296", "arena_cheolong296", "arena_hyeongang296", "sanctuary", "south_gate", "palace", "old_tree" };

        public Entry[] Entries = Array.Empty<Entry>();

        public string InitialFor(string realmId)
        {
            if (RealmElements == null || string.IsNullOrEmpty(realmId)) return "";
            for (int i = 0; i < RealmElements.Length; i++)
                if (RealmElements[i] != null && string.Equals(RealmElements[i].RealmId, realmId, StringComparison.Ordinal)) return RealmElements[i].Initial ?? "";
            return "";
        }

        public static bool Listed(string[] list, string id)
        {
            if (list == null || string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < list.Length; i++) if (string.Equals(list[i], id, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
