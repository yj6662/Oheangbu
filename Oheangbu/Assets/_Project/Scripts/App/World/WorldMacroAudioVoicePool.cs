using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Oheangbu.App.World
{
    /// <summary>Bounded sources with sample-clock envelopes. Tick only retires silent voices.</summary>
    public sealed class WorldMacroAudioVoicePool : IDisposable
    {
        private struct Request
        {
            public WorldMacroPlaytestAudioProfileSO.Cue Cue;
            public Vector3 Point;
            public float Gain;
            public bool Loop;
            public AudioMixerGroup Group;
        }
        private sealed class Voice
        {
            public AudioSource Source;
            public WorldMacroAudioEnvelope Envelope;
            public Request Current, Pending;
            public double Started, ReleaseStarted;
            public float ReleaseSeconds;
            public bool Releasing;
        }
        private readonly Voice[] _voices;
        private readonly int _reserved;
        private readonly bool _ignorePause;
        public AudioSource[] Sources { get; }
        public int ConcurrencyDrops { get; private set; }
        public int FadedSteals { get; private set; }
        public int Starts { get; private set; }
        public float ExternalGain = 1f;

        public WorldMacroAudioVoicePool(Transform parent, int count, int reserved, bool ignorePause, string prefix)
        {
            _reserved = reserved; _ignorePause = ignorePause; _voices = new Voice[count]; Sources = new AudioSource[count];
            for (int i = 0; i < count; i++)
            {
                var old = parent.Find(prefix + i);
                var go = old != null ? old.gameObject : new GameObject(prefix + i); go.transform.SetParent(parent, false);
                var source = go.GetComponent<AudioSource>();
                if (source == null) source = go.AddComponent<AudioSource>();
                source.Stop();source.clip=null;source.volume=0;
                source.playOnAwake = false; source.dopplerLevel = 0; source.pitch = 1; source.ignoreListenerPause = ignorePause;
                source.minDistance = 2; source.maxDistance = 32; source.rolloffMode = AudioRolloffMode.Logarithmic;
                var envelope = go.GetComponent<WorldMacroAudioEnvelope>();
                if (envelope == null) envelope = go.AddComponent<WorldMacroAudioEnvelope>();
                envelope.State.Silence();
                _voices[i] = new Voice { Source = source, Envelope = envelope }; Sources[i] = source;
            }
        }

        public bool Play(WorldMacroPlaytestAudioProfileSO.Cue cue, Vector3 point, float gain, AudioMixerGroup group, int slot = -1, bool loop = false)
        {
            if (cue == null || cue.Clip == null || gain <= 0) return false;
            double now = AudioSettings.dspTime;
            if (slot < 0)
            {
                int matching = 0; double oldest = double.MaxValue;
                for (int i = _reserved; i < _voices.Length; i++)
                {
                    var v = _voices[i];
                    if ((v.Source.isPlaying && ReferenceEquals(v.Current.Cue, cue)) || ReferenceEquals(v.Pending.Cue, cue)) matching++;
                    if (!v.Source.isPlaying && v.Pending.Cue == null && slot < 0) slot = i;
                }
                if (matching >= Mathf.Max(1, cue.MaxConcurrent)) { ConcurrencyDrops++; return false; }
                if (slot < 0)
                {
                    for (int i = _reserved; i < _voices.Length; i++)
                        if (_voices[i].Started < oldest) { oldest = _voices[i].Started; slot = i; }
                    FadedSteals++;
                }
            }
            if (slot < 0 || slot >= _voices.Length) return false;
            Voice voice = _voices[slot];
            var request = new Request { Cue = cue, Point = point, Gain = Mathf.Clamp01(gain), Loop = loop, Group = group };
            if (voice.Source.isPlaying)
            {
                voice.Pending = request;
                BeginRelease(voice, now, .025f);
            }
            else Start(voice, request, now);
            return true;
        }

        public void MoveSlot(int index, Vector3 point) { if (index >= 0 && index < _voices.Length) _voices[index].Source.transform.position = point; }
        public void ReleaseSlot(int index, float seconds)
        {
            if (index < 0 || index >= _voices.Length) return;
            var voice = _voices[index]; voice.Pending = default;
            BeginRelease(voice, AudioSettings.dspTime, seconds);
        }
        public void Tick()
        {
            double now = AudioSettings.dspTime;
            foreach (Voice voice in _voices)
            {
                if (voice.Current.Cue == null) continue;
                voice.Envelope.State.ExternalGain = Mathf.Clamp01(ExternalGain);
                // A virtualized source receives no filter callbacks and is inaudible; it can
                // retire after the requested release. Audible sources finish on the DSP thread.
                bool virtualRelease = voice.Releasing && voice.Source.isVirtual
                    && now - voice.ReleaseStarted > voice.ReleaseSeconds + .1;
                if (voice.Envelope.State.IsSilent || virtualRelease ||
                    (!voice.Source.isPlaying && now-voice.Started>.1))
                {
                    Request pending = voice.Pending; Clear(voice);
                    if (pending.Cue != null) Start(voice, pending, now);
                }
            }
        }
        public void StopAll(bool immediate)
        {
            foreach (Voice voice in _voices)
            {
                voice.Pending = default;
                if (immediate) Clear(voice); else BeginRelease(voice, AudioSettings.dspTime, .04f);
            }
        }
        private void Start(Voice voice, Request request, double now)
        {
            voice.Current = request; voice.Pending = default; voice.Started = now; voice.Releasing = false;
            var source = voice.Source; source.Stop(); source.transform.position = request.Point;
            source.outputAudioMixerGroup = request.Group; source.ignoreListenerPause = _ignorePause;
            source.clip = request.Cue.Clip; source.loop = request.Loop; source.spatialBlend = request.Cue.SpatialBlend;
            voice.Envelope.State.ExternalGain = Mathf.Clamp01(ExternalGain);
            voice.Envelope.State.Start(AudioSettings.outputSampleRate, request.Cue.Clip.length,
                request.Cue.AttackSeconds, request.Cue.ReleaseSeconds,
                request.Cue.Volume * request.Gain, request.Loop);
            source.pitch = 1f; source.volume = 1f; source.Play(); Starts++;
        }
        private static void BeginRelease(Voice voice, double now, float seconds)
        {
            if (voice.Current.Cue == null || voice.Releasing) return;
            voice.ReleaseStarted = now;
            voice.ReleaseSeconds = Mathf.Max(.005f, seconds); voice.Releasing = true;
            voice.Envelope.State.Release(voice.ReleaseSeconds);
        }
        public static float SmoothGain(float fraction) { float t = Mathf.Clamp01(fraction); return t * t * (3f - 2f * t); }
        private static void Clear(Voice voice)
        { voice.Envelope.State.Silence(); voice.Source.volume = 0; voice.Source.Stop(); voice.Source.clip = null; voice.Current = voice.Pending = default; voice.Releasing = false; }
        public void Dispose()
        {
            StopAll(true);
            foreach (Voice voice in _voices) if (voice.Source != null)
            { if (Application.isPlaying) UnityEngine.Object.Destroy(voice.Source.gameObject); else UnityEngine.Object.DestroyImmediate(voice.Source.gameObject); }
        }
    }
}
