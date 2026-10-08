using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    [CreateAssetMenu(menuName = "Oheangbu/UI/Region loading profile")]
    public sealed class WorldLoadingProfile270 : ScriptableObject
    {
        [Serializable] public sealed class Illustration
        {
            public string LocationId;
            public Texture2D Image;
        }
        /// <summary>#304 (REFERENCE_BOARD D20): one quiet line shown while waiting. Descriptive data (권역 설화, 작도 규칙), never an
        /// instruction ("~하세요" is banned, DESIGN §3.3).</summary>
        [Serializable] public sealed class WaitingLine
        {
            [Tooltip("zone / place / realm id the line belongs to (realm_cheongrim, mine_interior ...); empty = any region")]
            public string LocationId;
            [Tooltip("optional head glyph drawn before the text (a jamo for a stroke rule); empty = none")]
            public string Head;
            [TextArea] public string Text;
        }
        public string LoadingScene = "W_Compact_Loading";
        [Tooltip("#308 realm skin streaming (SPEC-WORLD-REALM-STREAM-308): the realm scenes around the start point are brought in before the play scene. Empty = the play scene is whole")]
        public RealmStreamSheet308 RealmStream;
        public WorldMapBakedDataSO Map;
        [Tooltip("legacy uGUI font (270). The #304 screen draws TMP through Style304.")]
        public Font Font;
        public Texture2D Fallback;
        public Illustration[] Illustrations = Array.Empty<Illustration>();
        public float InitializationTimeout = 90;
        public float WarmupTimeout = 60;

        [Header("#304 region loading (loading.png + D20)")]
        [Tooltip("UI304 style of the loading canvas; empty = the playtest root theme's Style304 (then the token-only fallback)")]
        public UiStyle304SO Style304;
        [Tooltip("authored lines for the waiting row; region lines first, then lines without a LocationId")]
        public WaitingLine[] WaitingLines = Array.Empty<WaitingLine>();
        [Tooltip("also cycle the codex stroke rules of the style (UiStyle304SO.StrokeRules: ㄱ 가로에서 꺾어 한 번에 내린다 ...)")]
        public bool StrokeRulesWhileWaiting = true;
        [Tooltip("the waiting line changes once the progress has not grown for this many unscaled seconds")]
        public float StallSeconds = 2f;
        [Tooltip("while the progress stays still, the line keeps changing every LineSeconds after the first change")]
        public float LineSeconds = 6f;

        public Texture2D Select(Vector3 position)
        {
            foreach (string id in LocationIds(position))
            {
                var image = Illustrations.FirstOrDefault(x => x.LocationId == id && x.Image != null)?.Image;
                if (image != null) return image;
            }
            return Fallback;
        }

        /// <summary>Ids that describe `position`, most specific first: map zone (mine, cave), location entry, realm.
        /// The same order Select uses for the illustration.</summary>
        public IEnumerable<string> LocationIds(Vector3 position)
        {
            string zone = Map != null ? Map.ZoneAt(position)?.Id : null;
            if (!string.IsNullOrEmpty(zone)) yield return zone;
            var place = Map?.Locations?.Resolve(position, null);
            if (!string.IsNullOrEmpty(place?.Id)) yield return place.Id;
            string realm = Map?.Locations?.RealmAt(position)?.Id ?? place?.RealmId;
            if (!string.IsNullOrEmpty(realm)) yield return realm;
        }

        /// <summary>Player-facing region name for the loading band (loading.png "청림"): the zone label (폐광 ...), else the realm
        /// name, else the location name, else "".</summary>
        public string RegionName(Vector3 position)
        {
            var zone = Map != null ? Map.ZoneAt(position) : null;
            if (zone != null && !string.IsNullOrWhiteSpace(zone.Label)) return zone.Label;
            var catalog = Map != null ? Map.Locations : null;
            if (catalog == null) return "";
            var place = catalog.Resolve(position, null);
            var realm = catalog.RealmAt(position);
            if (realm == null && place != null && !string.IsNullOrEmpty(place.RealmId))
                foreach (var e in catalog.Entries) if (e != null && e.Priority == 0 && e.Id == place.RealmId) { realm = e; break; }
            if (realm != null && !string.IsNullOrWhiteSpace(realm.Name)) return realm.Name;
            return place != null ? place.Name ?? "" : "";
        }

        /// <summary>The waiting lines for `position`: authored lines of its ids, authored lines without an id, then the style's
        /// stroke rules (when StrokeRulesWhileWaiting). Blank entries are skipped.</summary>
        public void CollectWaitingLines(Vector3 position, UiStyle304SO style, List<WaitingLine> into)
        {
            if (into == null) return;
            into.Clear();
            var ids = new List<string>(LocationIds(position));
            if (WaitingLines != null)
            {
                foreach (var id in ids)
                    foreach (var line in WaitingLines)
                        if (line != null && !string.IsNullOrWhiteSpace(line.Text) && line.LocationId == id) into.Add(line);
                foreach (var line in WaitingLines)
                    if (line != null && !string.IsNullOrWhiteSpace(line.Text) && string.IsNullOrEmpty(line.LocationId)) into.Add(line);
            }
            if (StrokeRulesWhileWaiting && style != null && style.StrokeRules != null)
                foreach (var rule in style.StrokeRules)
                    if (rule != null && !string.IsNullOrWhiteSpace(rule.Rule)) into.Add(new WaitingLine { Head = rule.Jamo, Text = rule.Rule });
        }
    }

    // Readiness, not a promise about sustained gameplay FPS. First-frame spikes reset the quiet window.
    public sealed class WorldLoadingReadiness270
    {
        readonly float[] samples = new float[15];
        readonly float[] sorted = new float[15];
        int count, index, quietFrames;
        float quietSeconds;
        public int RenderedFrames { get; private set; }
        public float Progress => Mathf.Clamp01(Mathf.Min(quietFrames / 10f, quietSeconds / .75f));
        public bool Ready => RenderedFrames >= 15 && quietFrames >= 10 && quietSeconds >= .75f;

        public void Sample(float delta, bool rendered, bool texturesReady)
        {
            if (!rendered || !texturesReady || !float.IsFinite(delta) || delta <= 0)
            { quietFrames = 0; quietSeconds = 0; return; }
            RenderedFrames++;
            samples[index++ % samples.Length] = delta;
            count = Mathf.Min(count + 1, samples.Length);
            Array.Copy(samples, sorted, count); Array.Sort(sorted, 0, count);
            float threshold = Mathf.Clamp(sorted[count / 2] * 1.6f, .05f, .15f);
            if (count < 8 || delta > threshold) { quietFrames = 0; quietSeconds = 0; return; }
            quietFrames++; quietSeconds += Mathf.Min(delta, .1f);
        }
    }
}
