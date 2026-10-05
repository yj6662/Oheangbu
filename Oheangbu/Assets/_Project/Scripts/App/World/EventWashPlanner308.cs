using System;
using System.Collections.Generic;
using Oheangbu.BrushRender;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    /// <summary>#308 권역 담채 planner (SPEC-EVENT-WASH-308 §2–2b, §5–6, D308-6 / D308-6b, TEST). Pure: no Unity object lookups, no
    /// static state, no Stage.Destination. Built once from the sheet, the catalog, the palette and the campaign (allocations happen
    /// here only); Poll / Step / Write are allocation-free so the driver's steady state allocates 0 B (AC-W13).
    ///  * meaning: sheet entry (Auto -> rule: BossDefeated/GrowthInterrupted none, Rest 안식, Optional 보상, else 진행), then the
    ///    exceptions: NoWashLocations none, NarrativeLocations (폐광) -> 서사, NarrativeRealms (황경) 진행/보상 -> 서사; boss and
    ///    growth stages never wash (AC-W9); then D308-6b: 보상 off unless RewardOn, 진행 off unless ProgressOn.
    ///  * kind: 진행 with ProgressAsVolume -> Volume (the air ellipsoid above the place, geometry baked into the sheet entry by
    ///    wash308-sheet; unbaked -> skipped); everything else -> Surface (the D308-6 ground term, values unchanged).
    ///  * colour (ElementPaletteSO only): 진행 = realm element base colour, 안식 = RestLanternColor, 보상 = RewardCeladonColor,
    ///    서사 = paper with chroma 0 (lift only). Surface slots get linear values (globals get no sRGB conversion); volume slots get
    ///    the perceptual glaze transmittance T = lerp(1, c / max(c), VolumePigment) of the sRGB palette colour.
    ///  * availability = DemoCampaignProgression.CanComplete per mapped stage (the AvailableStages test without the iterator).
    ///  * fades: linear FadeInSeconds / FadeOutSeconds; snap sets them at once (load, teleport).
    ///  * slots: nearest MaxZones (eye to zone edge) -> volume slots first [0, split), surface slots after [split, count). Two
    ///    stages at the same place share one volume slot: the strongest member gives the strength, the eased fades combine as a
    ///    union 1 − Π(1 − e) (one completes while the next opens: no dip at the hand-over, never above 1).</summary>
    public sealed class EventWashPlanner308
    {
        public const int ShaderZones = 8;   // InkWash297 / RealmFog297 array length

        public enum Kind { Surface = 0, Volume = 1 }

        public sealed class Zone
        {
            public string StageId, LocationId, RealmId, Reason;
            public EventWashSheetSO.Meaning Meaning;
            public Kind Kind;
            public DemoCampaignProfile.Stage Stage;
            public Vector2 Centre;
            public float Radius, MinY, MaxY, Strength, Chroma, Lift;
            public Color LinearColour;
            /// <summary>Volume (D308-6b): ellipsoid centre y, vertical and horizontal semi-axes (m), grain seed.</summary>
            public float VolumeCentreY, VolumeHalfHeight, VolumeRadius, Seed;
            /// <summary>Volume glaze transmittance T (perceptual, from the sRGB palette colour).</summary>
            public Vector3 Pigment;
            /// <summary>Slot group: one per (place, volume); every surface zone is its own group (D308-6 slots unchanged).</summary>
            public int Group;
            public bool Available;
            public float Fade;
        }

        readonly EventWashSheetSO sheet;
        readonly DemoCampaignProfile campaign;
        readonly Zone[] zones;
        readonly int[] order;
        readonly float[] key;
        readonly int[] slotZone = new int[ShaderZones];
        readonly int[] groupPick;
        readonly float[] groupFade;
        readonly List<string> skipped = new List<string>();

        public int Count => zones.Length;
        public Zone this[int index] => zones[index];
        /// <summary>Sheet entries that produce no zone, with the reason (editor reports).</summary>
        public IReadOnlyList<string> Skipped => skipped;
        public EventWashSheetSO Sheet => sheet;
        public DemoCampaignProfile Campaign => campaign;
        public int GroupCount => groupPick.Length;

        public EventWashPlanner308(EventWashSheetSO sheet, WorldLocationCatalog catalog, ElementPaletteSO palette, DemoCampaignProfile campaign)
        {
            this.sheet = sheet; this.campaign = campaign;
            var list = new List<Zone>();
            var groups = new List<string>();
            if (sheet != null && sheet.Entries != null && catalog != null && palette != null && campaign != null && campaign.Stages != null)
            {
                foreach (var entry in sheet.Entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.StageId)) continue;
                    var stage = FindStage(campaign, entry.StageId);
                    if (stage == null) { skipped.Add(entry.StageId + ": stage not in campaign " + campaign.CampaignId); continue; }
                    var place = FindPlace(catalog, entry.LocationId);
                    if (place == null) { skipped.Add(entry.StageId + ": no catalog place '" + entry.LocationId + "'"); continue; }
                    string realm = !string.IsNullOrEmpty(place.RealmId) ? place.RealmId : catalog.RealmAt(place.Centre)?.Id ?? "";
                    var meaning = Resolve(sheet, stage, entry, place.Id, realm, out string reason);
                    if (meaning == EventWashSheetSO.Meaning.None) { skipped.Add(entry.StageId + " @" + place.Id + ": none (" + reason + ")"); continue; }
                    bool volume = meaning == EventWashSheetSO.Meaning.Progress && sheet.ProgressAsVolume;
                    if (volume && !Baked(entry))
                    { skipped.Add(entry.StageId + " @" + place.Id + ": volume not baked (run wash308-sheet) (" + reason + ")"); continue; }
                    var zone = new Zone
                    {
                        StageId = stage.Id, LocationId = place.Id, RealmId = realm, Reason = reason, Meaning = meaning, Stage = stage,
                        Kind = volume ? Kind.Volume : Kind.Surface,
                        Centre = new Vector2(place.Centre.x, place.Centre.z), Radius = Mathf.Max(1f, place.Radius * sheet.RadiusScale),
                        MinY = place.MinimumY, MaxY = place.MaximumY,
                        Strength = Mathf.Max(0f, entry.StrengthScale) * (meaning == EventWashSheetSO.Meaning.Reward ? sheet.OptionalScale : 1f),
                        Lift = 1f, Chroma = meaning == EventWashSheetSO.Meaning.Narrative ? 0f : 1f,
                    };
                    var srgb = ColourFor(sheet, palette, meaning, realm);
                    zone.LinearColour = srgb.linear;
                    if (volume)
                    {
                        zone.VolumeCentreY = .5f * (entry.VolumeBaseY + entry.VolumeTopY);
                        zone.VolumeHalfHeight = .5f * (entry.VolumeTopY - entry.VolumeBaseY);
                        zone.VolumeRadius = entry.VolumeRadius;
                        zone.Pigment = PigmentFor(srgb, sheet.VolumePigment);
                    }
                    // one volume per place (cargo_contract + escort at village -> one slot); surfaces never merge
                    string groupKey = volume ? place.Id + "\u0001volume" : place.Id + "\u0001surface\u0001" + list.Count;
                    int group = groups.IndexOf(groupKey);
                    if (group < 0) { group = groups.Count; groups.Add(groupKey); }
                    zone.Group = group;
                    zone.Seed = 17.31f * (group + 1);   // grain follows the place, not the slot or the stage
                    list.Add(zone);
                }
            }
            zones = list.ToArray();
            order = new int[zones.Length];
            key = new float[zones.Length];
            groupPick = new int[groups.Count];
            groupFade = new float[groups.Count];
            for (int s = 0; s < ShaderZones; s++) slotZone[s] = -1;
        }

        /// <summary>The editor baked this entry's volume (top above the floor, radius &gt; 0).</summary>
        public static bool Baked(EventWashSheetSO.Entry entry) =>
            entry != null && entry.VolumeRadius > 0f && entry.VolumeTopY > entry.VolumeBaseY + 10f;

        static DemoCampaignProfile.Stage FindStage(DemoCampaignProfile campaign, string id)
        {
            foreach (var s in campaign.Stages) if (s != null && s.Id == id) return s;
            return null;
        }

        static WorldLocationCatalog.Entry FindPlace(WorldLocationCatalog catalog, string id)
        {
            if (catalog.Entries == null || string.IsNullOrEmpty(id)) return null;
            foreach (var e in catalog.Entries) if (e != null && e.Id == id) return e;
            return null;
        }

        /// <summary>Meaning after the automatic rule, the exceptions and the D308-6b meaning switches (reason for reports).</summary>
        public static EventWashSheetSO.Meaning Resolve(EventWashSheetSO sheet, DemoCampaignProfile.Stage stage, EventWashSheetSO.Entry entry,
            string locationId, string realmId, out string reason)
        {
            reason = "";
            if (sheet == null || stage == null) { reason = "no sheet/stage"; return EventWashSheetSO.Meaning.None; }
            if (entry != null && entry.Meaning == EventWashSheetSO.Meaning.None) { reason = "sheet: none"; return EventWashSheetSO.Meaning.None; }
            if (stage.Event == DemoEventKind.BossDefeated || stage.Event == DemoEventKind.GrowthInterrupted)
            { reason = "battle/boss/contamination stage (" + stage.Event + ")"; return EventWashSheetSO.Meaning.None; }
            if (EventWashSheetSO.Listed(sheet.NoWashLocations, locationId)) { reason = "NoWashLocations"; return EventWashSheetSO.Meaning.None; }
            var meaning = entry != null ? entry.Meaning : EventWashSheetSO.Meaning.Auto;
            if (meaning == EventWashSheetSO.Meaning.Auto)
            {
                if (stage.Event == DemoEventKind.Rest) { meaning = EventWashSheetSO.Meaning.Rest; reason = "auto: Rest"; }
                else if (stage.Optional) { meaning = EventWashSheetSO.Meaning.Reward; reason = "auto: Optional"; }
                else { meaning = EventWashSheetSO.Meaning.Progress; reason = "auto: " + stage.Event; }
            }
            else reason = "sheet: " + meaning;
            if (EventWashSheetSO.Listed(sheet.NarrativeLocations, locationId) && meaning != EventWashSheetSO.Meaning.Narrative)
            { meaning = EventWashSheetSO.Meaning.Narrative; reason += " -> narrative (NarrativeLocations)"; }
            else if (EventWashSheetSO.Listed(sheet.NarrativeRealms, realmId) &&
                     (meaning == EventWashSheetSO.Meaning.Progress || meaning == EventWashSheetSO.Meaning.Reward))
            { meaning = EventWashSheetSO.Meaning.Narrative; reason += " -> narrative (NarrativeRealms " + realmId + ")"; }
            if (meaning == EventWashSheetSO.Meaning.Reward && !sheet.RewardOn)
            { reason += " -> off (RewardOn false, D308-6b)"; return EventWashSheetSO.Meaning.None; }
            if (meaning == EventWashSheetSO.Meaning.Progress && !sheet.ProgressOn)
            { reason += " -> off (ProgressOn false)"; return EventWashSheetSO.Meaning.None; }
            return meaning;
        }

        /// <summary>sRGB colour of a meaning (palette is the only colour source).</summary>
        public static Color ColourFor(EventWashSheetSO sheet, ElementPaletteSO palette, EventWashSheetSO.Meaning meaning, string realmId)
        {
            switch (meaning)
            {
                case EventWashSheetSO.Meaning.Progress:
                {
                    string initial = sheet != null ? sheet.InitialFor(realmId) : "";
                    return string.IsNullOrEmpty(initial) ? palette.PaperColor : palette.GetBaseColor(initial[0]);
                }
                case EventWashSheetSO.Meaning.Rest: return palette.RestLanternColor;
                case EventWashSheetSO.Meaning.Reward: return palette.RewardCeladonColor;
                default: return palette.PaperColor;   // 서사: achromatic, lift only (chroma 0)
            }
        }

        /// <summary>Glaze transmittance of a palette colour: T = lerp(1, c / max(c.r, c.g, c.b), pigment), normalised in sRGB so the
        /// hues read evenly in perceptual space (linear normalisation makes 화 far too dense).</summary>
        public static Vector3 PigmentFor(Color srgb, float pigment)
        {
            float m = Mathf.Max(srgb.r, Mathf.Max(srgb.g, srgb.b));
            if (m <= 1e-4f) return Vector3.one;
            return Vector3.Lerp(Vector3.one, new Vector3(srgb.r / m, srgb.g / m, srgb.b / m), Mathf.Clamp01(pigment));
        }

        /// <summary>Optical path length (m, density factor 1 − |p|² integrated) of the ray eye + t·dir, t in [tNear, tMax] metres, through
        /// the axis-aligned ellipsoid (radius, halfHeight, radius) at centre. Closed form from the closest approach (no cancellation);
        /// the C# mirror of the InkWash297 volume term without the grain (wash308-unit, AC-W18). A miss is exactly 0.</summary>
        public static float OpticalDepth(Vector3 eye, Vector3 dir, float tNear, float tMax, Vector3 centre, float radius, float halfHeight)
        {
            if (radius <= 0f || halfHeight <= 0f || !(tMax > tNear)) return 0f;
            float length = dir.magnitude;
            if (length < 1e-6f) return 0f;
            var view = dir / length;
            var invS = new Vector3(1f / radius, 1f / halfHeight, 1f / radius);
            var o = Vector3.Scale(eye - centre, invS);
            var v = Vector3.Scale(view, invS);
            float a = Vector3.Dot(v, v);
            float tm = -Vector3.Dot(o, v) / a;
            var q = o + tm * v;
            float k = 1f - Vector3.Dot(q, q);
            if (k <= 0f) return 0f;
            float h = Mathf.Sqrt(k / a);
            float u0 = Mathf.Max(-h, tNear - tm), u1 = Mathf.Min(h, tMax - tm);
            if (u1 <= u0) return 0f;
            return Mathf.Max(0f, k * (u1 - u0) - a * (u1 * u1 * u1 - u0 * u0 * u0) / 3f);
        }

        /// <summary>Availability of every zone's stage (prerequisites/facts/defeats met, implemented, not completed).</summary>
        public void Poll(DemoCampaignState state, ICollection<string> defeated)
        {
            for (int i = 0; i < zones.Length; i++)
                zones[i].Available = state != null && DemoCampaignProgression.CanComplete(campaign, state, zones[i].StageId, defeated);
        }

        public void Clear()
        {
            for (int i = 0; i < zones.Length; i++) zones[i].Available = false;
        }

        /// <summary>Editor preview (wash308-preview): every zone available (or none).</summary>
        public void ForceAvailable(bool on)
        {
            for (int i = 0; i < zones.Length; i++) zones[i].Available = on;
        }

        /// <summary>Moves every fade toward its availability. Returns true while any fade is between 0 and 1 or changed.</summary>
        public bool Step(float dt, bool snap)
        {
            bool moving = false;
            float fadeIn = sheet != null ? sheet.FadeInSeconds : 3f, fadeOut = sheet != null ? sheet.FadeOutSeconds : 5f;
            for (int i = 0; i < zones.Length; i++)
            {
                var z = zones[i];
                float target = z.Available ? 1f : 0f, before = z.Fade;
                if (snap) z.Fade = target;
                else if (z.Fade < target) z.Fade = fadeIn <= 0f ? 1f : Mathf.Min(1f, z.Fade + Mathf.Max(0f, dt) / fadeIn);
                else if (z.Fade > target) z.Fade = fadeOut <= 0f ? 0f : Mathf.Max(0f, z.Fade - Mathf.Max(0f, dt) / fadeOut);
                if (z.Fade != before) moving = true;
            }
            return moving;
        }

        static float Eased(float f) => f * f * (3f - 2f * f);

        static float Smooth(float from, float to, float x)
        {
            if (!(to > from)) return x < from ? 0f : 1f;
            float t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Nearest MaxZones visible zones (eye to zone edge; surface MaxDistance / volume VolumeMaxDistance cut) into the shader
        /// arrays (always ShaderZones long; unused slots zeroed). Volume slots come first: [0, split) = volumes, [split, count) =
        /// surfaces, each in nearest order.
        ///  * volume slot: zone (cx, cz, R, strength·eased·distFade), colour (T, 0), band 0 (RealmFog297's far-air term ignores it:
        ///    chroma 0), volume (yc, Hh, rho0, seed); distFade = 1 − smoothstep(maxD − VolumeDistanceFade, maxD, edge).
        ///  * surface slot: the D308-6 values (cx, cz, r, strength·eased), (linear rgb, lift), (minY, maxY, edgeNoise, chroma); volume 0.
        /// Returns the active count.</summary>
        public int Write(Vector3 eye, Vector4[] zone, Vector4[] colour, Vector4[] band, Vector4[] volume, out int split)
        {
            split = 0;
            float maxSurface = sheet != null ? sheet.MaxDistance : 1600f;
            float maxVolume = sheet != null ? sheet.VolumeMaxDistance : 3000f;
            float distanceFade = sheet != null ? sheet.VolumeDistanceFade : 600f;
            float density = sheet != null ? sheet.VolumeDensity : .003f;
            float edgeNoise = sheet != null ? sheet.EdgeNoise : .12f;
            var eyeXZ = new Vector2(eye.x, eye.z);

            // every volume group (same place, two stages -> one slot): the member with the largest strength x eased fade holds the
            // slot (ties: lower index); the group fade is the union of the members' eased fades, 1 − Π(1 − e) (groupFade keeps the
            // product of (1 − e) while summing)
            for (int g = 0; g < groupPick.Length; g++) { groupPick[g] = -1; groupFade[g] = 1f; }
            for (int i = 0; i < zones.Length; i++)
            {
                var z = zones[i];
                if (z.Kind != Kind.Volume || z.Fade <= 0f || z.Strength <= 0f) continue;
                float e = Eased(z.Fade);
                int held = groupPick[z.Group];
                if (held < 0 || z.Strength * e > zones[held].Strength * Eased(zones[held].Fade)) groupPick[z.Group] = i;
                groupFade[z.Group] *= 1f - e;
            }

            int n = 0;
            for (int i = 0; i < zones.Length; i++)
            {
                var z = zones[i];
                if (z.Fade <= 0f || z.Strength <= 0f) continue;
                bool isVolume = z.Kind == Kind.Volume;
                if (isVolume && groupPick[z.Group] != i) continue;
                float edge = Vector2.Distance(eyeXZ, z.Centre) - (isVolume ? z.VolumeRadius : z.Radius);
                if (edge > (isVolume ? maxVolume : maxSurface)) continue;
                order[n] = i; key[n] = edge; n++;
            }
            int limit = Mathf.Min(sheet != null ? Mathf.Clamp(sheet.MaxZones, 1, ShaderZones) : ShaderZones, ShaderZones);
            int count = Mathf.Min(n, limit);
            // partial selection sort: the `count` nearest first (n is small; no allocation)
            for (int a = 0; a < count; a++)
            {
                int best = a;
                for (int b = a + 1; b < n; b++) if (key[b] < key[best] || key[b] == key[best] && order[b] < order[best]) best = b;
                if (best != a) { (key[a], key[best]) = (key[best], key[a]); (order[a], order[best]) = (order[best], order[a]); }
            }

            // stable split: volumes first, then surfaces (each keeps the nearest order)
            int s = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                bool volumePass = pass == 0;
                for (int a = 0; a < count; a++)
                {
                    var z = zones[order[a]];
                    if ((z.Kind == Kind.Volume) != volumePass) continue;
                    float eased = Eased(z.Fade);
                    if (volumePass)
                    {
                        eased = 1f - groupFade[z.Group];   // group union (a single member: its own eased fade)
                        float distFade = 1f - Smooth(maxVolume - distanceFade, maxVolume, key[a]);
                        zone[s] = new Vector4(z.Centre.x, z.Centre.y, z.VolumeRadius, z.Strength * eased * distFade);
                        colour[s] = new Vector4(z.Pigment.x, z.Pigment.y, z.Pigment.z, 0f);
                        band[s] = Vector4.zero;
                        volume[s] = new Vector4(z.VolumeCentreY, z.VolumeHalfHeight, density, z.Seed);
                        split++;
                    }
                    else
                    {
                        zone[s] = new Vector4(z.Centre.x, z.Centre.y, z.Radius, z.Strength * eased);
                        colour[s] = new Vector4(z.LinearColour.r, z.LinearColour.g, z.LinearColour.b, z.Lift);
                        band[s] = new Vector4(z.MinY, z.MaxY, edgeNoise, z.Chroma);
                        volume[s] = Vector4.zero;
                    }
                    slotZone[s] = order[a];
                    s++;
                }
            }
            for (; s < ShaderZones; s++)
            {
                zone[s] = Vector4.zero; colour[s] = Vector4.zero; band[s] = Vector4.zero; volume[s] = Vector4.zero;
                slotZone[s] = -1;
            }
            return count;
        }

        /// <summary>Zone index in a shader slot of the last Write (-1 = unused), for probes.</summary>
        public int SelectedZone(int slot) => slot >= 0 && slot < ShaderZones ? slotZone[slot] : -1;
    }
}
