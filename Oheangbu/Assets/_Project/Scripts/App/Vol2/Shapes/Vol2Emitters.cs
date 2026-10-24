using System;
using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>One touch of a shape on a body. The coordinator turns it into a budget contact, a damage request and a body action.</summary>
    public struct Vol2ShapeContact
    {
        public Vol2ShapeKind Shape;
        public IVol2Target Target;
        public long TargetId, LifeRevision, NowUs;
        /// <summary>0 when the contact does not come from a swing.</summary>
        public long SwingId;
        public Vector3 Point;
        /// <summary>Path: metres along the axis at first touch. Fan / ring: distance from the apex / centre. Attachment: the swing capsule distance.</summary>
        public float Along;
        public string Note;
    }

    /// <summary>A shape that lives for a while and reports contacts. Ticked by the coordinator with the scope clock (microseconds) and the
    /// candidate list of that tick.</summary>
    public interface IVol2ShapeEmitter
    {
        Vol2ShapeKind Kind { get; }
        Vol2ShapeSpec Spec { get; }
        long StartUs { get; }
        bool Finished { get; }
        void Tick(long nowUs, IReadOnlyList<IVol2Target> targets, List<Vol2ShapeContact> output);
    }

    /// <summary>ㅏ: a narrow path, a 0.3 m disc travelling along the aim axis up to 12 m (spec numbers), judged by the swept stadium of each
    /// step so a fast step cannot skip a body. Stops at the first wall (a solid that is not an enemy or the caster). With
    /// <c>stopAtFirst</c> (the plan is FirstOnly: 한 번 and 가른다) the path ends at the first body; contacts come in distance order, and the
    /// rules' budget refuses a second body anyway (FirstTargetOnly), so a stray second contact could never be paid.</summary>
    public sealed class Vol2PathEmitter : IVol2ShapeEmitter
    {
        readonly IVol2Obstruction _wall;
        readonly bool _stopAtFirst;
        readonly float _chest;
        readonly HashSet<long> _touched = new HashSet<long>();
        readonly List<(float entry, IVol2Target target)> _step = new List<(float, IVol2Target)>();
        float _along;
        public Vol2ShapeKind Kind => Vol2ShapeKind.Path;
        public Vol2ShapeSpec Spec { get; }
        public long StartUs { get; }
        public bool Finished { get; private set; }
        /// <summary>How far the head has travelled (metres). For the presenter.</summary>
        public float HeadAlong => _along;
        /// <summary>Why it ended: "" while running, "length", "wall", "first".</summary>
        public string EndReason { get; private set; } = "";

        public Vol2PathEmitter(Vol2ShapeSpec spec, long startUs, bool stopAtFirst, IVol2Obstruction wall, float chestHeight)
        { Spec = spec; StartUs = startUs; _stopAtFirst = stopAtFirst; _wall = wall; _chest = chestHeight; }

        public void Tick(long nowUs, IReadOnlyList<IVol2Target> targets, List<Vol2ShapeContact> output)
        {
            if (Finished) return;
            float to = Mathf.Min(Spec.PathLength, Spec.PathSpeed * Vol2Time.ToSecondsF(Math.Max(0, nowUs - StartUs)));
            float from = _along;
            bool walled = false;
            if (_wall != null && to > from)
            {
                Vector3 start = Spec.Origin + Vector3.up * _chest;
                if (_wall.TryBlock(start + Spec.Forward * from, start + Spec.Forward * to, out float dist)) { to = Mathf.Min(to, from + dist); walled = true; }
            }
            _step.Clear();
            float half = Spec.PathWidth * 0.5f;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.Alive || _touched.Contains(t.Id)) continue;
                if (Vol2ShapeGeometry.PathStep(Spec.Origin, Spec.Forward, from, to, half, t.Position, t.Radius, out float entry, out float _)) _step.Add((entry, t));
            }
            _step.Sort((a, b) => { int c = a.entry.CompareTo(b.entry); return c != 0 ? c : a.target.Id.CompareTo(b.target.Id); });
            for (int i = 0; i < _step.Count; i++)
            {
                var (entry, t) = _step[i];
                _touched.Add(t.Id);
                output.Add(new Vol2ShapeContact { Shape = Vol2ShapeKind.Path, Target = t, TargetId = t.Id, LifeRevision = t.LifeRevision, NowUs = nowUs, Along = entry,
                    Point = Spec.Origin + Spec.Forward * entry + Vector3.up * _chest });
                if (_stopAtFirst) { _along = entry; Finished = true; EndReason = "first"; return; }
            }
            _along = to;
            if (walled) { Finished = true; EndReason = "wall"; }
            else if (to >= Spec.PathLength - 1e-4f) { Finished = true; EndReason = "length"; }
        }
    }

    /// <summary>ㅗ: the fan (sector 5 m deep, 4 m across the far chord), apex at the caster. 한 번 is one instant: every body the exact sector test
    /// touches this tick, once, in distance order. The candidates are the registry's targets (no physics overlap is needed: the list is
    /// the scene's enemies and the test is exact).</summary>
    public sealed class Vol2FanEmitter : IVol2ShapeEmitter
    {
        public Vol2ShapeKind Kind => Vol2ShapeKind.Fan;
        public Vol2ShapeSpec Spec { get; }
        public long StartUs { get; }
        public bool Finished { get; private set; }
        public Vol2FanEmitter(Vol2ShapeSpec spec, long startUs) { Spec = spec; StartUs = startUs; }
        public void Tick(long nowUs, IReadOnlyList<IVol2Target> targets, List<Vol2ShapeContact> output)
        {
            if (Finished) return;
            Finished = true;
            var found = new List<(float d, IVol2Target t)>();
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.Alive) continue;
                if (Vol2ShapeGeometry.FanHit(Spec.Origin, Spec.Forward, Spec.FanHalfAngleRad, Spec.FanDepth, t.Position, t.Radius))
                    found.Add((Vector3.Distance(Flat(Spec.Origin), Flat(t.Position)), t));
            }
            found.Sort((a, b) => { int c = a.d.CompareTo(b.d); return c != 0 ? c : a.t.Id.CompareTo(b.t.Id); });
            foreach (var (d, t) in found)
                output.Add(new Vol2ShapeContact { Shape = Vol2ShapeKind.Fan, Target = t, TargetId = t.Id, LifeRevision = t.LifeRevision, NowUs = nowUs, Along = d, Point = t.Position });
        }
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }

    /// <summary>ㅜ: the hollow ring (radius 2 m centre line, 0.6 m thick) fixed where it was cast. A body touches it when its PATH of this tick
    /// (previous to current position) crosses the band: standing in the band, walking or dashing through it, and jumping across it all
    /// count; standing in the empty middle or beyond the band does not. 한 번 is one instant ("바로 사라진다"). The same emitter can host the
    /// lasting rings of R1 to R5: Finished is the only thing that would change.</summary>
    public sealed class Vol2RingEmitter : IVol2ShapeEmitter
    {
        public Vol2ShapeKind Kind => Vol2ShapeKind.Ring;
        public Vol2ShapeSpec Spec { get; }
        public long StartUs { get; }
        public bool Finished { get; private set; }
        public Vol2RingEmitter(Vol2ShapeSpec spec, long startUs) { Spec = spec; StartUs = startUs; }
        public void Tick(long nowUs, IReadOnlyList<IVol2Target> targets, List<Vol2ShapeContact> output)
        {
            if (Finished) return;
            Finished = true;
            var found = new List<(float d, IVol2Target t)>();
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.Alive) continue;
                if (Vol2ShapeGeometry.RingCrossed(Spec.Origin, Spec.RingInner, Spec.RingOuter, t.Previous, t.Position, t.Radius))
                {
                    Vector3 a = t.Position - Spec.Origin; a.y = 0f;
                    found.Add((a.magnitude, t));
                }
            }
            found.Sort((a, b) => { int c = a.d.CompareTo(b.d); return c != 0 ? c : a.t.Id.CompareTo(b.t.Id); });
            foreach (var (d, t) in found)
                output.Add(new Vol2ShapeContact { Shape = Vol2ShapeKind.Ring, Target = t, TargetId = t.Id, LifeRevision = t.LifeRevision, NowUs = nowUs, Along = d, Point = t.Position });
        }
    }

    /// <summary>One report of the real swing: which swing it is (several colliders of one swing report the SAME id), where the sleeve and the brush
    /// tip are, and when.</summary>
    public readonly struct Vol2SwingEvent
    {
        public readonly long SwingId;
        public readonly Vector3 Sleeve, Tip;
        public readonly long NowUs;
        public readonly int ColliderIndex;
        public Vol2SwingEvent(long swingId, Vector3 sleeve, Vector3 tip, long nowUs, int colliderIndex = 0)
        { SwingId = swingId; Sleeve = sleeve; Tip = tip; NowUs = nowUs; ColliderIndex = colliderIndex; }
    }

    /// <summary>The scope's swing contact signal. Raised by the swing driver (slice 9, Vol2SwingDriver: one event per swept step of the contact window of a real
    /// brush swing, all steps of a swing sharing its SwingId). The wiring's Vol2 route returns before the old swing starts (TryRouteVol2), so the old swing
    /// never raised it. The lab's 125 rows still raise it by hand and mark those rows as lab-simulated; its swing pass runs the same rows through the driver.</summary>
    public sealed class Vol2SwingSignal
    {
        public event Action<Vol2SwingEvent> Contact;
        public int Raised { get; private set; }
        public void Raise(Vol2SwingEvent e)
        {
            Raised++;
            var handlers = Contact;
            if (handlers == null) return;
            foreach (Action<Vol2SwingEvent> h in handlers.GetInvocationList())
            {
                try { h(e); } catch (Exception ex) { Debug.LogException(ex); }
            }
        }
    }

    /// <summary>ㅓ: attached to the caster (brush shaft and sleeve) for a window [start, start + window). It makes no world shape. Contacts come from
    /// swings reported through <see cref="OnSwing"/>; several colliders of one swing share a SwingId and give ONE contact per body. For the five
    /// 무받침 ㅓ glyphs the window IS the parry window (combat.parry.window, 0.5 s) and the glyph has no attack term: the host exposes the window and the
    /// body's element for the defence judge of slice 2 and the contacts are recorded without any effect.</summary>
    public sealed class Vol2AttachmentHost : IVol2ShapeEmitter
    {
        readonly HashSet<(long swing, long target)> _seen = new HashSet<(long, long)>();
        public Vol2ShapeKind Kind => Vol2ShapeKind.Attachment;
        public Vol2ShapeSpec Spec { get; }
        public long StartUs { get; }
        public long EndUs { get; }
        public bool Finished { get; private set; }
        public int Reports { get; private set; }
        public int Duplicates { get; private set; }
        public Vol2AttachmentHost(Vol2ShapeSpec spec, long startUs, long windowUs) { Spec = spec; StartUs = startUs; EndUs = startUs + windowUs; }
        public bool Open(long nowUs) => Vol2Time.Contains(StartUs, EndUs, nowUs);

        public void Tick(long nowUs, IReadOnlyList<IVol2Target> targets, List<Vol2ShapeContact> output)
        {
            if (nowUs >= EndUs) Finished = true;
        }

        /// <summary>One collider's report of a swing. Outside the window nothing happens; the same (swing, body) pair is contacted once.</summary>
        public void OnSwing(Vol2SwingEvent e, IReadOnlyList<IVol2Target> targets, List<Vol2ShapeContact> output)
        {
            Reports++;
            if (Finished || !Open(e.NowUs)) return;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.Alive) continue;
                if (!Vol2ShapeGeometry.SwingHit(e.Sleeve, e.Tip, Spec.SwingRadius, t.Position, t.Radius)) continue;
                if (!_seen.Add((e.SwingId, t.Id))) { Duplicates++; continue; }
                Vector3 a = t.Position - e.Sleeve; a.y = 0f;
                output.Add(new Vol2ShapeContact { Shape = Vol2ShapeKind.Attachment, Target = t, TargetId = t.Id, LifeRevision = t.LifeRevision, NowUs = e.NowUs, SwingId = e.SwingId, Along = a.magnitude, Point = t.Position });
            }
        }
    }
}
