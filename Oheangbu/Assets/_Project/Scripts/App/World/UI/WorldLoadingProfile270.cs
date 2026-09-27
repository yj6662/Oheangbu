using System;
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
        public string LoadingScene = "W_Compact_Loading";
        public WorldMapBakedDataSO Map;
        public Font Font;
        public Texture2D Fallback;
        public Illustration[] Illustrations = Array.Empty<Illustration>();
        public float InitializationTimeout = 90;
        public float WarmupTimeout = 60;

        public Texture2D Select(Vector3 position)
        {
            string zone = Map != null ? Map.ZoneAt(position)?.Id : null;
            var image = Illustrations.FirstOrDefault(x => x.LocationId == zone && x.Image != null)?.Image;
            if (image != null) return image;
            var place = Map?.Locations?.Resolve(position, null);
            image = Illustrations.FirstOrDefault(x => x.LocationId == place?.Id && x.Image != null)?.Image;
            if (image != null) return image;
            string realm = Map?.Locations?.RealmAt(position)?.Id ?? place?.RealmId;
            return Illustrations.FirstOrDefault(x => x.LocationId == realm && x.Image != null)?.Image ?? Fallback;
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
