using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Oheangbu.C02RigFaceLab
{
    public sealed class LabPlaybackController : MonoBehaviour
    {
        public Animator bodyAnimator;
        public LabProfileSO profile;
        public string[] states = Array.Empty<string>();
        public float[] clipSeconds = Array.Empty<float>();
        public string outputDirectory;
        public string currentState { get; private set; }
        public string sequenceStatus { get; private set; } = "IDLE";
        public int currentStep { get; private set; }
        readonly List<Step> sequence = new List<Step>();
        readonly List<EventRecord> events = new List<EventRecord>();
        float stepTime;
        float sequenceTime;
        int sampledFrames;
        struct Step { public string name; public float duration; public int cycles; }
        [Serializable] public class EventRecord { public string name; public int frame; public float time; public string state; public int cycles; public float duration; }
        [Serializable] public class PlaybackRecord { public string status; public string scope = "Unity experiment Animator playback, in-place, root motion OFF; visual quality UNVERIFIED"; public float playbackSpeed; public int sampledFrames; public EventRecord[] events; }

        public float ClipDuration(string name)
        {
            int index = Array.IndexOf(states, name);
            if (index < 0) throw new InvalidOperationException("Missing body clip: " + name);
            return clipSeconds[index];
        }
        public void Play(string name, bool immediate = false)
        {
            ClipDuration(name);
            bodyAnimator.speed = 1f;
            bodyAnimator.applyRootMotion = false;
            if (immediate) bodyAnimator.Play(name, 0, 0f);
            else bodyAnimator.CrossFadeInFixedTime(name, profile.transitionSeconds, 0, 0f);
            currentState = name;
        }
        public void StartSequence()
        {
            sequence.Clear(); events.Clear();
            foreach (var name in new[] { "Idle", "Run", "Idle", "Attack" })
            {
                int cycles = name == "Run" ? profile.runCycles : 1;
                sequence.Add(new Step { name = name, cycles = cycles, duration = ClipDuration(name) * cycles });
            }
            currentStep = 0; stepTime = 0; sequenceTime = 0; sampledFrames = 0; sequenceStatus = "RUNNING";
            Play(sequence[0].name, true); Record("sequence_start", sequence[0]);
        }
        public void StopSequence() { sequenceStatus = "STOPPED"; WriteReport(); }
        void Record(string name, Step step) { events.Add(new EventRecord { name = name, frame = sampledFrames, time = sequenceTime, state = step.name, cycles = step.cycles, duration = step.duration }); }
        void Update()
        {
            if (sequenceStatus != "RUNNING") return;
            sampledFrames++; stepTime += Time.deltaTime; sequenceTime += Time.deltaTime;
            if (stepTime + 0.00001f < sequence[currentStep].duration) return;
            Record("step_end", sequence[currentStep]); currentStep++;
            if (currentStep >= sequence.Count) { sequenceStatus = "COMPLETE"; WriteReport(); return; }
            stepTime = 0; Play(sequence[currentStep].name); Record("transition_start", sequence[currentStep]);
        }
        public void WriteReport()
        {
            if (string.IsNullOrEmpty(outputDirectory)) return;
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "playback_events.json"), JsonUtility.ToJson(new PlaybackRecord { status = sequenceStatus, playbackSpeed = bodyAnimator == null ? 0 : bodyAnimator.speed, sampledFrames = sampledFrames, events = events.ToArray() }, true));
        }
        void OnDisable() { if (sequenceStatus == "RUNNING") { sequenceStatus = "INTERRUPTED"; WriteReport(); } }
    }
}
