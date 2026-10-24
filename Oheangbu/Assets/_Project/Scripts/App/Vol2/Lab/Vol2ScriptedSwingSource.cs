using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>S5-b slice 9: a swing source with no player behind it - the lab's and the fixtures' "hand". A click (Begin) starts a motion whose clip time runs on the scope clock;
    /// the brush stands at the pose last given to <see cref="Aim"/> (or moves along <see cref="Path"/> when one is set). The DRIVER above it is the production one: window,
    /// sweep, SwingId, the swing signal and the 갈무리 report all run for real.</summary>
    public sealed class Vol2ScriptedSwingSource : IVol2SwingSource
    {
        readonly IVol2Clock _clock;
        Vector3 _sleeve, _tip;
        int _serial;
        long _startUs;
        bool _began;

        /// <summary>Seconds of the motion's clip, and where its apex is (the window is read from these, like from the real clip).</summary>
        public float Length = 0.12f, Apex = 0.05f;
        /// <summary>Optional: the pose as a function of clip seconds (a moving tip). null = the fixed pose of <see cref="Aim"/>.</summary>
        public System.Func<float, (Vector3 sleeve, Vector3 tip)> Path;
        public int Begins => _serial;

        public Vol2ScriptedSwingSource(IVol2Clock clock) { _clock = clock; }

        public void Aim(Vector3 sleeve, Vector3 tip) { _sleeve = sleeve; _tip = tip; }

        float Clip => _began ? (float)((_clock.NowUs - _startUs) / 1000000.0) : 0f;

        public bool TrySample(out Vol2SwingSample sample)
        {
            sample = new Vol2SwingSample { Serial = _serial, Swinging = _began, ClipSeconds = Clip, ApexSeconds = Apex, LengthSeconds = Length };
            return true;
        }

        public bool TryPose(out Vector3 sleeve, out Vector3 tip)
        {
            if (Path != null) { var p = Path(Clip); sleeve = p.sleeve; tip = p.tip; }
            else { sleeve = _sleeve; tip = _tip; }
            return true;
        }

        public bool Begin() { _serial++; _began = true; _startUs = _clock.NowUs; return true; }
    }

    /// <summary>A 갈무리 port that only writes down what the driver reported (the lab's swing pass and the fixtures: no HP is touched, no ledger is spent).</summary>
    public sealed class Vol2RecordingGatherPort : IVol2SwingGatherPort
    {
        public readonly System.Collections.Generic.List<(string target, long swing)> Hits = new System.Collections.Generic.List<(string, long)>();
        public readonly System.Collections.Generic.List<long> Misses = new System.Collections.Generic.List<long>();
        public void FirstHit(IVol2Target target, SwingId swing) { Hits.Add((target != null ? target.Name : "", swing.Value)); }
        public void Missed(SwingId swing) { Misses.Add(swing.Value); }
    }
}