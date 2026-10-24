using System;
using System.Collections.Generic;
using System.Globalization;
using Oheangbu.Combat;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // S5-b slice 6: the parts around the 가른다 executor - the strip state book, the ports to the game (the enemy's armour, the posture bridge of the
    // 날붓), the per-cast data the lab / fixtures read, and the footprint geometry. The executor itself is Vol2SplitExecutor.cs.

    // ------------------------------------------------------------------------------------------------------------------------------
    // strip
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>One strip of one body: when it began, until when (the scope clock), the value the cast booked with it (0.4 on the first only).</summary>
    public sealed class Vol2StripEntry
    {
        public CastId Cast;
        public long TargetId, LifeRevision, AtUs, UntilUs, DurationUs;
        public string TargetName = "";
        /// <summary>The 0.4 the budget converted with this strip (only the first strip of a cast carries it).</summary>
        public decimal Value;
        /// <summary>true = a second crossing of the ring: the state is renewed, nothing is paid again.</summary>
        public bool Restrip;
        /// <summary>What the port to the game answered ("strip ok guard=broken armour=off ...", "NotAnEnemy" for a fixture's fake body).</summary>
        public string PortNote = "";
    }

    /// <summary>The scope clock's truth of who is stripped until when, keyed by (body, life): a new life is not stripped. Instance, never a static.
    /// The game's own state (Modifiers.Stance / ShredDefence) is the port's job; this book is what the lab and the fixtures assert on.</summary>
    public sealed class Vol2StripBook
    {
        readonly Dictionary<(long id, long life), long> _until = new Dictionary<(long, long), long>();
        readonly List<Vol2StripEntry> _entries = new List<Vol2StripEntry>();
        const int Keep = 1024;
        public IReadOnlyList<Vol2StripEntry> Entries => _entries;
        public int Count => _until.Count;

        public Vol2StripEntry Apply(IVol2Target t, CastId cast, long nowUs, long durationUs, decimal value, bool restrip, string portNote)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            long until = checked(nowUs + durationUs);
            var key = (t.Id, t.LifeRevision);
            if (_until.TryGetValue(key, out long old) && old > until) until = old;       // a strip never shortens a running one
            _until[key] = until;
            var e = new Vol2StripEntry { Cast = cast, TargetId = t.Id, LifeRevision = t.LifeRevision, AtUs = nowUs, UntilUs = until, DurationUs = durationUs, TargetName = t.Name, Value = value, Restrip = restrip, PortNote = portNote ?? "" };
            _entries.Add(e);
            if (_entries.Count > Keep) _entries.RemoveRange(0, _entries.Count - Keep);
            return e;
        }

        public bool IsStripped(IVol2Target t, long nowUs) => t != null && _until.TryGetValue((t.Id, t.LifeRevision), out long until) && nowUs < until;
        public long UntilOf(IVol2Target t) => t != null && _until.TryGetValue((t.Id, t.LifeRevision), out long until) ? until : 0;
        public void Clear() { _until.Clear(); _entries.Clear(); }
    }

    /// <summary>The game's side of a strip. The default (<see cref="Vol2EnemyStripPort"/>) hangs it on the enemy's armour state; a fixture swaps a fake.</summary>
    public interface IVol2StripPort
    {
        /// <summary>Strips <paramref name="t"/> for <paramref name="durationUs"/>; the answer is a short text for the log ("strip ok ...").</summary>
        string Apply(IVol2Target t, long durationUs, long nowUs);
    }

    /// <summary>Strips a real enemy through Vol2Strip (guard broken, armour lowered by its whole share). Any other target answers "NotAnEnemy".</summary>
    public sealed class Vol2EnemyStripPort : IVol2StripPort
    {
        public int Applied { get; private set; }
        public string Apply(IVol2Target t, long durationUs, long nowUs)
        {
            var enemy = t as Vol2EnemyTarget;
            if (enemy == null || enemy.Vitals == null) return "NotAnEnemy";
            var r = Vol2Strip.Apply(enemy.Vitals, Vol2Time.ToSecondsF(durationUs), Time.time);
            if (r.Applied) Applied++;
            return r.ToString();
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // 날붓
    // ------------------------------------------------------------------------------------------------------------------------------

    public readonly struct Vol2BladeResult
    {
        public readonly bool Called, Counter, Accepted, Opened;
        public readonly decimal Added, PostureAfter;
        public readonly string Reason;
        public Vol2BladeResult(bool called, bool counter, bool accepted, decimal added, decimal after, bool opened, string reason)
        { Called = called; Counter = counter; Accepted = accepted; Added = added; PostureAfter = after; Opened = opened; Reason = reason ?? ""; }
        public static Vol2BladeResult None(string reason) => new Vol2BladeResult(false, false, false, 0m, 0m, false, reason);
        public override string ToString() => (Called ? "counter=" + (Counter ? "Y" : "N") + " +" + Added.ToString(CultureInfo.InvariantCulture) + " posture=" + PostureAfter.ToString(CultureInfo.InvariantCulture) + (Opened ? " WINDOW" : "") + " " : "not called ") + Reason;
    }

    /// <summary>The posture side of the 날붓: a melee direct hit of a ㅓ+ㅅ cast that overcomes the target's attack body adds 10 (GroggyRulesV3 CounterBlade).</summary>
    public interface IVol2BladePort
    {
        Vol2BladeResult Hit(IVol2Target target, Glyph glyph, long nowUs);
    }

    /// <summary>The real port: Vol2PostureBridge.ApplyBlade of the combat wiring's defence kit. The counter test is the damage entry's own (Vol2Strip.CounterOf).</summary>
    public sealed class Vol2PostureBladePort : IVol2BladePort
    {
        readonly Vol2PostureBridge _posture;
        public Vol2PostureBladePort(Vol2PostureBridge posture) { _posture = posture ?? throw new ArgumentNullException(nameof(posture)); }
        public Vol2BladeResult Hit(IVol2Target target, Glyph glyph, long nowUs)
        {
            var enemy = target as Vol2EnemyTarget;
            if (enemy == null || enemy.Vitals == null) return Vol2BladeResult.None("NotAnEnemy");
            bool counter = Vol2Strip.CounterOf(enemy.Vitals, glyph.Body);
            var r = _posture.ApplyBlade(enemy.Vitals, glyph.Letter, true, true, counter);
            return new Vol2BladeResult(true, counter, r.Accepted, r.Added, r.PostureAfter, r.Opened, r.Reason);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // per-cast data
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>One material cut in two: the parent (same id, same life, half of what it held) and its new children (same cast, owner, expiry).</summary>
    public sealed class Vol2SplitRecord
    {
        public CastId Cast;
        public long AtUs;
        public Vol2Material Parent;
        public readonly List<Vol2Material> Children = new List<Vol2Material>();
        public decimal Before;
        public string Where = "";
    }

    public sealed class Vol2BladeEvent
    {
        public AttackId Attack;
        public long TargetId;
        public string TargetName = "", Kind = "";
        public Vol2BladeResult Result;
    }

    /// <summary>What one 가른다 cast did besides the contacts in its log: the materials it cut, the strips it applied, the 날붓 answers.</summary>
    public sealed class Vol2SplitCastData
    {
        public CastId Cast;
        public readonly List<Vol2SplitRecord> Splits = new List<Vol2SplitRecord>();
        public readonly List<Vol2StripEntry> Strips = new List<Vol2StripEntry>();
        public readonly List<Vol2BladeEvent> Blades = new List<Vol2BladeEvent>();
        public int BladeDuplicates, RearHits;
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // geometry: shape vs footprint
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>A material's footprint as discs. A disc is its own sample; the others are sampled (TE-S6-2).</summary>
    public readonly struct Vol2Sample
    {
        public readonly Vector3 P; public readonly float R;
        public Vol2Sample(Vector3 p, float r) { P = p; R = r; }
    }

    public static class Vol2SplitGeometry
    {
        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        public static List<Vol2Sample> Samples(Vol2Footprint fp, long nowUs, Vol2SplitTuning t)
        {
            var l = new List<Vol2Sample>();
            switch (fp.Kind)
            {
                case Vol2FootprintKind.Disc: l.Add(new Vol2Sample(fp.Origin, fp.Radius)); break;
                case Vol2FootprintKind.Ring:
                    {
                        float mid = (fp.RingInner + fp.RingOuter) * 0.5f; int n = Mathf.Max(8, t.RingSamples);
                        for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n; l.Add(new Vol2Sample(fp.Origin + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * mid, t.SampleRadius)); }
                        break;
                    }
                case Vol2FootprintKind.Fan:
                    {
                        float depth = fp.FanDepth * fp.ScaleAt(nowUs); int rows = Mathf.Max(1, t.FanSampleRows), cols = Mathf.Max(1, t.FanSampleCols), half = cols / 2;
                        l.Add(new Vol2Sample(fp.Origin, t.SampleRadius));
                        for (int r = 1; r <= rows; r++)
                            for (int k = -half; k <= half; k++)
                            {
                                float a = half == 0 ? 0f : fp.FanHalfAngleRad * k / half;
                                l.Add(new Vol2Sample(fp.Origin + Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * fp.Forward * (depth * r / rows), t.SampleRadius));
                            }
                        break;
                    }
            }
            return l;
        }

        /// <summary>The path's head swept from <paramref name="fromAlong"/> to <paramref name="toAlong"/> against the samples.</summary>
        public static bool PathHit(Vol2ShapeSpec spec, float fromAlong, float toAlong, List<Vol2Sample> samples)
        {
            if (toAlong < fromAlong) return false;
            float half = spec.PathWidth * 0.5f;
            foreach (var s in samples)
                if (Vol2ShapeGeometry.PathStep(spec.Origin, spec.Forward, fromAlong, toAlong, half, s.P, s.R, out _, out _)) return true;
            return false;
        }

        public static bool FanHit(Vol2ShapeSpec spec, List<Vol2Sample> samples)
        {
            foreach (var s in samples)
                if (Vol2ShapeGeometry.FanHit(spec.Origin, spec.Forward, spec.FanHalfAngleRad, spec.FanDepth, s.P, s.R)) return true;
            return false;
        }

        public static bool RingHit(Vol2ShapeSpec spec, List<Vol2Sample> samples)
        {
            foreach (var s in samples)
                if (Vol2ShapeGeometry.RingHit(spec.Origin, spec.RingInner, spec.RingOuter, s.P, s.R)) return true;
            return false;
        }

        /// <summary>A moving disc (the caster's path of the tick, radius <paramref name="radius"/>) or a capsule (a swing, thickness = radius) from a to b.</summary>
        public static bool SegmentHit(Vector3 a, Vector3 b, float radius, List<Vol2Sample> samples)
        {
            foreach (var s in samples)
                if (Vol2ShapeGeometry.SegmentDistance(Flat(s.P), Flat(a), Flat(b)) <= radius + s.R) return true;
            return false;
        }
    }
}
