using UnityEngine;
using Oheangbu.App.World.UI;

namespace Oheangbu.App.World
{
    [RequireComponent(typeof(AudioSource), typeof(AudioLowPassFilter))]
    public sealed class CompactCaveAmbience : MonoBehaviour
    {
        public WorldMacroPlaytestSession Session;
        public WorldMapBakedDataSO Map;
        public WorldMacroAudioMixProfileSO Mix;
        UserSettingsService settings;
        public string ZoneId = "mine_interior";
        [Range(0, 1)] public float Level = .16f;
        public float Radius = 28f;
        public bool Intermittent;
        float nextDrip;bool suspended;
        public bool Occluded { get; private set; }
        AudioSource source;
        AudioLowPassFilter filter;
        float nextProbe, target;

        void Awake()
        {
            source = GetComponent<AudioSource>(); filter = GetComponent<AudioLowPassFilter>();
            settings=FindFirstObjectByType<UserSettingsService>();
            if(Mix!=null)source.outputAudioMixerGroup=Mix.Sfx;
            source.volume = 0; source.loop = !Intermittent; source.spatialBlend = 1; source.dopplerLevel = 0;
            source.minDistance = 2; source.maxDistance = Radius; source.rolloffMode = AudioRolloffMode.Linear;
            nextDrip=Time.time+Random.Range(2f,6f);
            if (source.clip != null && !Intermittent) source.Play();
        }
        void Update()
        {
            if (Session == null || Session.Walker == null || Session.Walker.ViewCamera == null) return;
            if (Time.unscaledTime >= nextProbe)
            {
                nextProbe = Time.unscaledTime + .25f;
                var feet = Session.Walker.Body.transform.position;
                var eye = feet + Vector3.up * 1.1f;
                var zone = Map != null ? Map.ZoneAt(feet) : null;
                bool inRange = zone != null && zone.Id == ZoneId && Vector3.Distance(eye, transform.position) < Radius;
                Occluded = inRange && Physics.Linecast(eye, transform.position, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                target = inRange ? Level * (Occluded ? .18f : 1f) : 0;
                filter.cutoffFrequency = Occluded ? 650 : 2600;
            }
            source.volume = Mathf.MoveTowards(source.volume, target * (Mix!=null&&Mix.IsReady?1f:settings!=null?settings.EffectiveGameplayVolume:1f), Time.unscaledDeltaTime * .2f);
            bool pause=Time.timeScale==0||!Application.isFocused;
            if(pause&&!suspended)source.Pause();
            if(!pause&&suspended)source.UnPause();
            suspended=pause;
            if(Intermittent&&!pause&&target>0&&Time.time>=nextDrip&&!source.isPlaying)
            {nextDrip=Time.time+Random.Range(4f,9f);source.Play();}
        }
    }
}
