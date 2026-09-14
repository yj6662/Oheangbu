using System;
using System.Threading;
using UnityEngine;

namespace Oheangbu.App.World
{
    // One immutable command per start/release; no allocation, locks or Unity API in Process.
    // A frame stall can delay a new cue, but cannot turn its fade into a volume step.
    public sealed class WorldMacroAudioEnvelopeState
    {
        private sealed class Command
        {
            public int Generation, Rate;
            public double Duration, Attack, Tail, Release;
            public float Gain;
            public bool Loop, Releasing;
        }
        private volatile Command pending;
        private Command current;
        private int serial, completed;
        private long frame, releaseFrame;
        private float releaseFrom, previousEnvelope, gain;
        private bool releasing;
        public volatile float ExternalGain = 1f;
        public bool IsSilent => pending == null || Volatile.Read(ref completed) == pending.Generation;
        public long ProcessedFrames => Interlocked.Read(ref frame);

        public void Start(int sampleRate, double duration, double attack, double tail, float level, bool loop)
        {
            pending = new Command { Generation = ++serial, Rate = Math.Max(8000, sampleRate),
                Duration = Math.Max(0, duration), Attack = Math.Max(.005, attack), Tail = Math.Max(.005, tail),
                Gain = Math.Max(0, Math.Min(1, level)), Loop = loop };
        }
        public void Release(double seconds)
        {
            var p = pending;
            if (p == null || p.Releasing) return;
            pending = new Command { Generation=p.Generation, Rate=p.Rate, Duration=p.Duration, Attack=p.Attack,
                Tail=p.Tail, Gain=p.Gain, Loop=p.Loop, Releasing=true, Release=Math.Max(.005,seconds) };
        }
        public void Silence() { pending = null; }
        private static float Smooth(double fraction)
        { float t=(float)Math.Max(0,Math.Min(1,fraction));return t*t*(3f-2f*t); }
        public void Process(float[] data, int channels)
        {
            var command = pending;
            if (channels <= 0 || command == null) { Array.Clear(data,0,data.Length); return; }
            if (current == null || current.Generation != command.Generation)
            {
                frame=releaseFrame=0;previousEnvelope=releaseFrom=0;releasing=false;
                gain=Math.Max(0,Math.Min(1,ExternalGain));
            }
            current=command;
            if (command.Releasing && !releasing)
            { releasing=true;releaseFrame=frame;releaseFrom=previousEnvelope; }
            float targetGain=Math.Max(0,Math.Min(1,ExternalGain));
            float gainStep=1f/(command.Rate*.012f);
            for (int i=0;i<data.Length;i+=channels)
            {
                double elapsed=frame/(double)command.Rate;
                float envelope=releasing
                    ? releaseFrom*Smooth(1-(frame-releaseFrame)/(command.Release*command.Rate))
                    : Smooth(elapsed/command.Attack)*(command.Loop?1:Smooth((command.Duration-elapsed)/command.Tail));
                gain += Math.Max(-gainStep,Math.Min(gainStep,targetGain-gain));
                float level=envelope*command.Gain*gain;
                for (int c=0;c<channels && i+c<data.Length;c++) data[i+c]*=level;
                previousEnvelope=envelope;frame++;
                if ((releasing && envelope<=0) || (!command.Loop && elapsed>=command.Duration))
                    Volatile.Write(ref completed,command.Generation);
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class WorldMacroAudioEnvelope : MonoBehaviour
    {
        public readonly WorldMacroAudioEnvelopeState State = new WorldMacroAudioEnvelopeState();
        private void OnAudioFilterRead(float[] data,int channels) { State.Process(data,channels); }
        private void OnDisable() { State.Silence(); }
    }
}
