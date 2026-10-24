using System;
using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>What the swing's animation says right now. Serial changes when a NEW swing motion starts (any source); Swinging is true while the motion that
    /// carries a contact window is the one playing; the clip numbers are the shown state's (seconds of CLIP time).</summary>
    public struct Vol2SwingSample
    {
        public bool Swinging;
        public int Serial;
        public float ClipSeconds, ApexSeconds, LengthSeconds;
    }

    /// <summary>Where the swing's motion and the brush come from. Production: the player's SpellSwing308 and the world brush's sockets (Vol2RigSwingSource).
    /// The lab and the edit-mode fixture bring their own, scripted.</summary>
    public interface IVol2SwingSource
    {
        /// <summary>false = there is no rig to ask (menu, loading): nothing happens.</summary>
        bool TrySample(out Vol2SwingSample sample);
        /// <summary>The capsule's two ends in world space: the sleeve and the brush tip. false = no pose this frame.</summary>
        bool TryPose(out Vector3 sleeve, out Vector3 tip);
        /// <summary>Starts the bare swing motion (a click). false = it could not be started (no rig, no such state).</summary>
        bool Begin();
    }

    /// <summary>Where a swing's first enemy contact (or its miss) goes: the 갈무리. Production: the combat wiring's ReportVol2SwingFirstHit / ReportVol2SwingMissed.</summary>
    public interface IVol2SwingGatherPort
    {
        void FirstHit(IVol2Target target, SwingId swing);
        void Missed(SwingId swing);
    }

    /// <summary>One finished swing, as the driver tells it.</summary>
    public readonly struct Vol2SwingReport
    {
        public readonly long SwingId;
        public readonly bool Hit;
        public readonly string Target;
        public readonly long OpenedUs, ClosedUs;
        public readonly int Frames, Events;
        public readonly string Why;
        public Vol2SwingReport(long swing, bool hit, string target, long openedUs, long closedUs, int frames, int events, string why)
        { SwingId = swing; Hit = hit; Target = target ?? ""; OpenedUs = openedUs; ClosedUs = closedUs; Frames = frames; Events = events; Why = why ?? ""; }
        public override string ToString() => "swing " + SwingId + (Hit ? " hit " + Target : " missed") + " frames=" + Frames + " events=" + Events + " (" + Why + ")";
    }

    /// <summary>S5-b slice 9 (D308-150 V2): the vol.2 swing driver. A swing is one motion of the great brush. During the contact window (read from the swing animation's own
    /// clip time, TE-S9s-1) the driver sweeps the capsule sleeve - brush tip from the last frame's pose to this frame's (TE-S9s-4) and, for each step,
    ///   (a) raises <see cref="Vol2SwingSignal"/> with the swing's SwingId: the ㅓ attachments (A5 first / second contact, 것 strip, 섯 front / rear cut, ...) take their real contacts from it;
    ///   (b) tests the capsule against the enemies and, once per swing, reports the first one met to the 갈무리 (<see cref="IVol2SwingGatherPort.FirstHit"/>); a swing that ends
    ///       without meeting an enemy reports <see cref="IVol2SwingGatherPort.Missed"/> (TE-S9s-6).
    /// A plain object (no scene types): the production shell is Vol2SwingDriverHost, the source is Vol2RigSwingSource, a fixture drives <see cref="Update"/> by hand.
    /// One driver per combat scope; nothing is static.</summary>
    public sealed class Vol2SwingDriver
    {
        readonly Vol2CastCoordinator _host;
        readonly Vol2Services _services;
        readonly Vol2SwingTuning _tuning;
        readonly long _cooldownUs;

        bool _open, _active, _hit, _hasPrev;
        int _serial = int.MinValue, _frames, _events, _eventIndex;
        long _swingId, _openedUs, _lastReportUs = long.MinValue / 4;
        float _startClip, _endClip, _lastClip;
        string _hitTarget = "";
        Vector3 _prevSleeve, _prevTip;
        readonly List<IVol2Target> _scratch = new List<IVol2Target>();

        public Vol2SwingTuning Tuning => _tuning;
        IVol2SwingSource _source;
        /// <summary>Where the motion and the brush come from. Changing it drops the swing in progress without a report and forgets the old source's serial (two sources count their motions apart).</summary>
        public IVol2SwingSource Source { get => _source; set { _source = value; Reset(); } }
        public IVol2SwingGatherPort Gather { get; set; }
        public bool Enabled { get; set; } = true;
        /// <summary>The swing in progress (0 = none).</summary>
        public long CurrentSwing => _open ? _swingId : 0;
        public bool InSwing => _open;
        /// <summary>There is a rig to swing with (the source answers).</summary>
        public bool IsAvailable => Source != null && Source.TrySample(out _);
        /// <summary>The contact window of the swing in progress is open now.</summary>
        public bool WindowOpen => _open && _active;
        public float WindowStartClip => _startClip;
        public float WindowEndClip => _endClip;
        public long LastReportUs => _lastReportUs;

        // counters (the lab and the fixture read them)
        public int Swings { get; private set; }          // swings opened
        public int Reports { get; private set; }         // FirstHit + Missed calls
        public int FirstHits { get; private set; }
        public int Misses { get; private set; }
        public int Cancelled { get; private set; }       // cut before the window opened: never a swing
        public int Refused { get; private set; }         // clicks refused (a swing in progress, the cooldown, no source)
        public int RaisedEvents { get; private set; }
        public int FramesSwept { get; private set; }
        public Vol2SwingReport Last { get; private set; }
        public event Action<Vol2SwingReport> Closed;

        public Vol2SwingDriver(Vol2CastCoordinator host, Vol2SwingTuning tuning = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _services = host.Services;
            _tuning = tuning ?? new Vol2SwingTuning();
            if (!_tuning.IsValid) throw new ArgumentException("invalid Vol2SwingTuning", nameof(tuning));
            _cooldownUs = host.Defence.SwingCooldownUs;
        }

        // ---- the click ----

        /// <summary>The bare swing was asked for (the left click). true = a swing motion started and its window is armed.</summary>
        public bool TryBeginSwing()
        {
            if (!Enabled || Source == null) { Refused++; return false; }
            if (_open) { Refused++; return false; }
            long now = _services.Clock.NowUs;
            if (_tuning.GateByCooldown && now - _lastReportUs < _cooldownUs) { Refused++; return false; }
            bool began;
            try { began = Source.Begin(); } catch (Exception e) { Debug.LogException(e); began = false; }
            if (!began) { Refused++; return false; }
            if (Source.TrySample(out var s) && s.Serial != _serial) { _serial = s.Serial; if (s.Swinging) Open(s, now); }
            return true;
        }

        // ---- the frame ----

        /// <summary>Once per rendered frame, after the rig has put the brush in the hand.</summary>
        public void Update()
        {
            if (!Enabled || Source == null) return;
            long now = _services.Clock.NowUs;
            if (!Source.TrySample(out var s)) { if (_open) Close(now, "SourceLost"); return; }
            if (s.Serial != _serial)
            {
                if (_open) Close(now, "Replaced");
                _serial = s.Serial;
                if (s.Swinging) Open(s, now);
            }
            if (!_open) return;
            _lastClip = s.ClipSeconds;
            if (!s.Swinging) { Close(now, "Ended"); return; }
            bool pose = Source.TryPose(out var sleeve, out var tip);
            if (s.ClipSeconds < _startClip)
            {
                // the wind-up: only remember where the brush was, so the first window frame sweeps from here
                if (pose) { _prevSleeve = sleeve; _prevTip = tip; _hasPrev = true; }
                return;
            }
            _active = true;
            bool past = s.ClipSeconds > _endClip;
            // after the window nothing is swept - except when the whole window passed between two frames and nothing was swept yet (a long frame)
            if (pose && (!past || _frames == 0)) Sweep(now, sleeve, tip);
            if (past) Close(now, "Window");
        }

        /// <summary>Drops the swing in progress without a report (the scope is going away, the lab switches sources).</summary>
        public void Reset()
        {
            _open = false; _active = false; _hit = false; _hasPrev = false; _serial = int.MinValue;
        }

        // ---- internals ----

        void Open(Vol2SwingSample s, long now)
        {
            _swingId = _services.Ids.NextSwing().Value;
            _open = true; _active = false; _hit = false; _hasPrev = false; _frames = 0; _events = 0; _eventIndex = 0; _hitTarget = ""; _openedUs = now; _lastClip = s.ClipSeconds;
            Window(s, out _startClip, out _endClip);
            Swings++;
        }

        void Window(Vol2SwingSample s, out float start, out float end)
        {
            float len = s.LengthSeconds;
            if (s.ApexSeconds > 0f)
            {
                start = Mathf.Max(0f, s.ApexSeconds - _tuning.ActiveBeforeApex);
                end = s.ApexSeconds + _tuning.ActiveAfterApex;
                if (len > 0f) end = Mathf.Min(end, len);
                if (end < start) end = start;
            }
            else if (len > 0f) { start = len * _tuning.NoApexStartFraction; end = len * _tuning.NoApexEndFraction; }
            else { start = 0f; end = _tuning.ActiveBeforeApex + _tuning.ActiveAfterApex; }
        }

        void Sweep(long now, Vector3 sleeve, Vector3 tip)
        {
            Vector3 ps = _hasPrev ? _prevSleeve : sleeve, pt = _hasPrev ? _prevTip : tip;
            float travel = Mathf.Max((tip - pt).magnitude, (sleeve - ps).magnitude);
            int n = Mathf.Clamp(Mathf.CeilToInt(travel / _tuning.MaxSubstepMeters), 1, _tuning.MaxSubstepsPerFrame);
            for (int k = 1; k <= n; k++)
            {
                float f = k / (float)n;
                Vector3 s = Vector3.Lerp(ps, sleeve, f), t = Vector3.Lerp(pt, tip, f);
                _host.Swings.Raise(new Vol2SwingEvent(_swingId, s, t, now, _eventIndex++));
                _events++; RaisedEvents++;
                if (!_hit) FirstContact(s, t, now);
            }
            _prevSleeve = sleeve; _prevTip = tip; _hasPrev = true;
            _frames++; FramesSwept++;
        }

        // the nearest body to the capsule's axis wins (ties: the lower id): "the first enemy it met", deterministic
        void FirstContact(Vector3 sleeve, Vector3 tip, long now)
        {
            float radius = _tuning.Radius(_host.Tuning);
            var targets = _host.Targets;
            IVol2Target best = null; float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.Alive) continue;
                if (!Vol2ShapeGeometry.SwingHit(sleeve, tip, radius, t.Position, t.Radius)) continue;
                float d = PlaneDistance(t.Position, sleeve, tip);
                if (best == null || d < bestDistance - 1e-5f || (Mathf.Abs(d - bestDistance) <= 1e-5f && t.Id < best.Id)) { best = t; bestDistance = d; }
            }
            if (best == null) return;
            _hit = true; _hitTarget = best.Name; _lastReportUs = now; FirstHits++; Reports++;
            try { Gather?.FirstHit(best, new SwingId(_swingId)); } catch (Exception e) { Debug.LogException(e); }
        }

        void Close(long now, string why)
        {
            if (!_open) return;
            // a window that came and went between two frames (a long frame): the brush still crossed it - one sweep from where it was to where it is
            if (_frames == 0 && _lastClip >= _startClip && Source != null && Source.TryPose(out var sl, out var tp)) Sweep(now, sl, tp);
            bool wasActive = _frames > 0;
            _open = false; _active = false;
            if (!wasActive && !_hit) { Cancelled++; Last = new Vol2SwingReport(_swingId, false, "", _openedUs, now, 0, 0, why + " (before the window)"); Closed?.Invoke(Last); return; }
            if (!_hit)
            {
                _lastReportUs = now; Misses++; Reports++;
                try { Gather?.Missed(new SwingId(_swingId)); } catch (Exception e) { Debug.LogException(e); }
            }
            Last = new Vol2SwingReport(_swingId, _hit, _hitTarget, _openedUs, now, _frames, _events, why);
            Closed?.Invoke(Last);
        }

        static float PlaneDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 pp = new Vector2(p.x, p.z), aa = new Vector2(a.x, a.z), bb = new Vector2(b.x, b.z);
            Vector2 ab = bb - aa; float len2 = ab.sqrMagnitude;
            if (len2 < 1e-8f) return (pp - aa).magnitude;
            float t = Mathf.Clamp01(Vector2.Dot(pp - aa, ab) / len2);
            return (pp - (aa + ab * t)).magnitude;
        }
    }
}
