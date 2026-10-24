using System;
using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>A shape that carries no contacts of its own and only keeps time: the ring of a 번진다 judges its crossings itself (the run does it
    /// every tick for the ring's whole life), so the one-shot ring emitter of 한 번 cannot be used.</summary>
    public sealed class Vol2TimedEmitter : IVol2ShapeEmitter
    {
        public Vol2ShapeKind Kind => Spec.Kind;
        public Vol2ShapeSpec Spec { get; }
        public long StartUs { get; }
        public long EndUs { get; }
        public bool Finished { get; private set; }
        public Vol2TimedEmitter(Vol2ShapeSpec spec, long startUs, long lifeUs) { Spec = spec; StartUs = startUs; EndUs = startUs + lifeUs; }
        public void Tick(long nowUs, IReadOnlyList<IVol2Target> targets, List<Vol2ShapeContact> output) { if (nowUs >= EndUs) Finished = true; }
    }

    /// <summary>번진다 (S5-b slice 4, table rows ㄴ): the shape hits what it touches, then the effect SPREADS from there to the bodies next to it
    /// (space.spread.adjacency, 3 m) - the plan's spread amount (1.0) is divided among them, each takes its part as damage and a short MARK
    /// (3 s) is left where it stands. The share is paid ONCE: the target's damage is the rules' Shared / Spread draw, and the mark records
    /// that same share as reaction fuel with PaysPool = false - it is never a second damage draw ("대상 피해와 남긴 자국에 같은 몫을 두 번
    /// 배정하지 않음"). No mark spreads again (no grandchildren).
    /// Where it spreads from: path = from the body it hit; attachment = from each body the swing first touches (one verb share per cast: what the
    /// pool still holds); fan = from the fan's EDGE to the bodies within 3 m of it; ring = from each body that crosses the band (the ring lives
    /// timing.n.life), outward. The split is the reading "equal among the adjacent, at most action.spread.targetCap (4), at most what the cast's
    /// effect-target cap still allows" (a ㅓ cast reaches three bodies in all).
    /// Peaks: 난 (path) every spread body takes 0.5, three at most (sum 1.5), no thinning; 넌 (attachment) the same, spread only by the FIRST TWO
    /// swings and from one shared 1.5 (no refill on the second), the caster carries a 6 s fire carrier (amount 0); 논 (fan) the forward spread 1.0, then
    /// after a pause a back-spread of 0.5 over the SAME bodies in reverse (no third pass); 눈 (ring) fire follows WOOD materials instead of bodies:
    /// every wood material (mine and the world's) the ring reaches and every wood next to those burns (ended Consumed), the bodies standing in them
    /// share the 1.0; enemy patches the ring touches burn too, four at most (peak.눈.burnCount); a burning vine door is reported to the field.</summary>
    public sealed class Vol2SpreadExecutor : IVol2VerbExecutor
    {
        public Verb Verb => Verb.Spread;
        public string Name => "Spread(번진다)";
        public IVol2VerbRun Begin(Vol2CastRun run) => new SpreadRun(run);

        sealed class Reached { public IVol2Target Target; public long Rev; public decimal Amount; public Vol2Material Mark; }

        sealed class SpreadRun : Vol2VerbRunBase
        {
            readonly bool _fixed, _back, _wood, _carrier;
            readonly HashSet<long> _affected = new HashSet<long>();
            readonly HashSet<long> _ringTouched = new HashSet<long>();
            readonly List<Reached> _forward = new List<Reached>();
            readonly List<long> _swingOrder = new List<long>();
            long _backAtUs = -1; bool _backDone, _fanSpread;
            int _burnedEnemy;

            public SpreadRun(Vol2CastRun run) : base(run)
            {
                char c = run.Glyph.Letter;
                _fixed = c == '난' || c == '넌'; _back = c == '논'; _wood = c == '눈'; _carrier = c == '넌';
            }

            public override IVol2ShapeEmitter CreateEmitter(Vol2ShapeSpec spec, long nowUs)
            {
                switch (spec.Kind)
                {
                    case Vol2ShapeKind.Path: return new Vol2PathEmitter(spec, nowUs, Host.VerbTuning.SpreadStopsPathAtFirst, Host.Wall, Host.Tuning.ChestHeight);
                    case Vol2ShapeKind.Fan: return new Vol2FanEmitter(spec, nowUs);
                    case Vol2ShapeKind.Ring: return new Vol2TimedEmitter(spec, nowUs, Run.Plan.LifeUs);
                    default: return new Vol2AttachmentHost(spec, nowUs, Run.Plan.LifeUs);
                }
            }

            public override void Start(long nowUs, IReadOnlyList<IVol2Target> targets)
            {
                if (!_carrier) return;
                var m = Host.Materials.Create(Run.Cast, Run.Glyph.Letter, Run.Glyph.Body, Vol2MaterialKind.Carrier, 0m, Run.Plan.LifeUs, Vol2Footprint.Attached(Run.Spec.Origin),
                    Vol2Faction.Player, Vol2Faction.Player, false, nowUs);
                Run.Log.Materials.Add(m);
            }

            public override void OnContact(Vol2ShapeContact c, long nowUs)
            {
                _affected.Add(c.TargetId);
                var rec = Touch(c, nowUs);
                if (!rec.BudgetAccepted) return;
                switch (Run.Spec.Kind)
                {
                    case Vol2ShapeKind.Path:
                        SpreadFrom(c.Target, nowUs); break;
                    case Vol2ShapeKind.Attachment:
                        if (_fixed)
                        {
                            int idx = _swingOrder.IndexOf(c.SwingId);
                            if (idx < 0) { _swingOrder.Add(c.SwingId); idx = _swingOrder.Count - 1; }
                            if (idx >= (int)Numbers.Get("peak.넌.swings")) return;      // the third swing and later spread nothing
                        }
                        else if (rec.Kind != "Instant") return;                           // a normal ㅓ spreads from a body's first touch
                        SpreadFrom(c.Target, nowUs); break;
                }
            }

            public override void Tick(long nowUs, IReadOnlyList<IVol2Target> targets)
            {
                var spec = Run.Spec;
                if (spec.Kind == Vol2ShapeKind.Fan && Run.Emitter.Finished && !_fanSpread)
                {
                    _fanSpread = true;
                    float adj = (float)Numbers.Get("space.spread.adjacency");
                    var cands = new List<(float d, IVol2Target t)>();
                    foreach (var t in targets)
                    {
                        if (t == null || !t.Alive || _affected.Contains(t.Id)) continue;
                        float d = Vol2Footprint.FanDistance(spec.Origin, spec.Forward, spec.FanHalfAngleRad, spec.FanDepth, t.Position);
                        if (d <= adj) cands.Add((d, t));
                    }
                    Spread(Ordered(cands), nowUs);
                }
                else if (spec.Kind == Vol2ShapeKind.Ring && !Run.Emitter.Finished)
                {
                    foreach (var t in targets)
                    {
                        if (t == null || !t.Alive || _ringTouched.Contains(t.Id)) continue;
                        if (!Vol2ShapeGeometry.RingCrossed(spec.Origin, spec.RingInner, spec.RingOuter, t.Previous, t.Position, t.Radius)) continue;
                        _ringTouched.Add(t.Id); _affected.Add(t.Id);
                        var c = MakeContact(t, nowUs, 0, Flat(spec.Origin, t.Position));
                        var rec = Touch(c, nowUs);
                        if (rec.BudgetAccepted && !_wood) SpreadOutward(t, nowUs);
                    }
                    if (_wood) BurnPass(nowUs);
                }
                if (_back && !_backDone && _backAtUs >= 0 && nowUs >= _backAtUs) BackPass(nowUs);
            }

            public override bool Idle(long nowUs) => !(_back && !_backDone && _forward.Count > 0);

            // ---- who is next to whom ----

            static List<IVol2Target> Ordered(List<(float d, IVol2Target t)> cands)
            {
                cands.Sort((a, b) => { int c = a.d.CompareTo(b.d); return c != 0 ? c : a.t.Id.CompareTo(b.t.Id); });
                var l = new List<IVol2Target>(); foreach (var p in cands) l.Add(p.t); return l;
            }

            void SpreadFrom(IVol2Target origin, long nowUs)
            {
                float adj = (float)Numbers.Get("space.spread.adjacency");
                var cands = new List<(float d, IVol2Target t)>();
                foreach (var t in Host.Targets)
                {
                    if (t == null || !t.Alive || t.Id == origin.Id || _affected.Contains(t.Id)) continue;
                    float d = Flat(origin.Position, t.Position);
                    if (d <= adj) cands.Add((d, t));
                }
                Spread(Ordered(cands), nowUs);
            }

            void SpreadOutward(IVol2Target origin, long nowUs)
            {
                float adj = (float)Numbers.Get("space.spread.adjacency");
                float r0 = Flat(Run.Spec.Origin, origin.Position);
                var cands = new List<(float d, IVol2Target t)>();
                foreach (var t in Host.Targets)
                {
                    if (t == null || !t.Alive || t.Id == origin.Id || _affected.Contains(t.Id)) continue;
                    float d = Flat(origin.Position, t.Position);
                    if (d <= adj && Flat(Run.Spec.Origin, t.Position) > r0) cands.Add((d, t));
                }
                Spread(Ordered(cands), nowUs);
            }

            // ---- the spread itself ----

            int Room()
            {
                int cap = (int)Numbers.Get(_fixed ? "peak.spread.peakTargets" : "action.spread.targetCap");
                int room = cap - _forward.Count;
                if (Run.Plan.Limited) room = Math.Min(room, (int)Numbers.Get("action.effectTargetCap") - Run.Budget.State.Targets.Count);
                return room;
            }

            void Spread(List<IVol2Target> candidates, long nowUs)
            {
                int room = Room();
                if (room <= 0 || candidates.Count == 0) return;
                var take = candidates.GetRange(0, Math.Min(room, candidates.Count));
                decimal pool = Run.Budget.State.Remaining(EffectKind.Spread);
                if (pool <= 0m) return;
                decimal each = _fixed ? Numbers.Get("peak.spread.amountPerTarget") : pool / take.Count;
                long markLife = RuleTypesV3.Microseconds(Numbers.Get("timing.n.life"));
                foreach (var t in take)
                {
                    var attack = Host.NewAttack(Run, 0, nowUs);
                    var rec = Host.NewRecord(Run, MakeContact(t, nowUs), attack, "Spread");
                    long markId = Host.Services.Ids.NextMaterial().Value;
                    var step = Run.Budget.Pay(attack.Value, ContactKind.Shared, EffectKind.Spread, each, TriggerKind.None, t.Id, t.LifeRevision, 0, nowUs, markId);
                    rec.BudgetAccepted = step.Accepted; rec.BudgetReason = step.Reason; rec.MaterialId = markId;
                    if (!step.Accepted || step.Commands.Count == 0) { Host.NotifyContact(Run.Log, rec); continue; }
                    decimal paid = step.Commands[0].ActionAmount;
                    _affected.Add(t.Id);
                    Host.Hit(Run, t, rec, paid, attack, nowUs, t.Position, 0, 0);
                    FirstBody(t, rec, attack, nowUs);
                    var mark = Host.Materials.Create(Run.Cast, Run.Glyph.Letter, Run.Glyph.Body, Vol2MaterialKind.Mark, paid, markLife, Vol2Footprint.Disc(t.Position, Host.VerbTuning.MarkRadius),
                        Vol2Faction.Player, Vol2Faction.Player, false, nowUs, 0, markId);
                    Run.Log.Materials.Add(mark);
                    _forward.Add(new Reached { Target = t, Rev = t.LifeRevision, Amount = paid, Mark = mark });
                    Host.NotifyContact(Run.Log, rec);
                }
                if (_back && _backAtUs < 0 && _forward.Count > 0) _backAtUs = nowUs + Host.VerbTuning.BackSpreadDelayUs;
            }

            /// <summary>논: the same bodies again, in reverse, sharing the back-spread amount (0.5). Nothing spreads from them.</summary>
            void BackPass(long nowUs)
            {
                _backDone = true;
                var valid = new List<Reached>();
                for (int i = _forward.Count - 1; i >= 0; i--)
                {
                    var r = _forward[i];
                    if (r.Target.Alive && r.Target.LifeRevision == r.Rev) valid.Add(r);
                }
                decimal pool = Run.Budget.State.Remaining(EffectKind.ReturnSpread);
                if (valid.Count == 0 || pool <= 0m) return;
                decimal each = pool / valid.Count;
                long markLife = RuleTypesV3.Microseconds(Numbers.Get("timing.n.life"));
                foreach (var r in valid)
                {
                    var t = r.Target;
                    var attack = Host.NewAttack(Run, 0, nowUs);
                    var rec = Host.NewRecord(Run, MakeContact(t, nowUs), attack, "ReturnSpread");
                    long markId = Host.Services.Ids.NextMaterial().Value;
                    var step = Run.Budget.Pay(attack.Value, ContactKind.Shared, EffectKind.ReturnSpread, each, TriggerKind.None, t.Id, t.LifeRevision, 0, nowUs, markId);
                    rec.BudgetAccepted = step.Accepted; rec.BudgetReason = step.Reason; rec.MaterialId = markId;
                    if (!step.Accepted || step.Commands.Count == 0) { Host.NotifyContact(Run.Log, rec); continue; }
                    decimal paid = step.Commands[0].ActionAmount;
                    Host.Hit(Run, t, rec, paid, attack, nowUs, t.Position, 0, 0);
                    var mark = Host.Materials.Create(Run.Cast, Run.Glyph.Letter, Run.Glyph.Body, Vol2MaterialKind.Mark, paid, markLife, Vol2Footprint.Disc(t.Position, Host.VerbTuning.MarkRadius),
                        Vol2Faction.Player, Vol2Faction.Player, false, nowUs, 0, markId);
                    Run.Log.Materials.Add(mark);
                    Host.NotifyContact(Run.Log, rec);
                }
            }

            // ---- 눈: fire along wood ----

            void BurnPass(long nowUs)
            {
                var spec = Run.Spec; Vector3 centre = spec.Origin; float inner = spec.RingInner, outer = spec.RingOuter;
                var mats = Host.Materials.AliveSnapshot();
                mats.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
                int burnCap = (int)Numbers.Get("peak.눈.burnCount");
                foreach (var m in mats)
                {
                    if (_burnedEnemy >= burnCap) break;
                    if (m.FactionId != Vol2Faction.Enemy || m.Cast == Run.Cast || !m.Alive || !m.Footprint.OverlapsBand(centre, inner, outer)) continue;
                    if (Host.Materials.TryEnd(m.Id, Vol2MaterialEnd.Consumed, nowUs)) { _burnedEnemy++; Run.Log.Burned.Add(m.Id.Value + ":Consumed:enemy"); Gate(m); }
                }
                float adj = (float)Numbers.Get("space.spread.adjacency");
                var medium = new List<Vol2Material>();
                foreach (var m in mats)
                    if (m.Alive && m.Body == Body.Wood && m.Cast != Run.Cast && !m.Attached && (m.FactionId == Vol2Faction.Player || m.FactionId == Vol2Faction.World)) medium.Add(m);
                var burning = new List<Vol2Material>();
                var queue = new Queue<Vol2Material>();
                foreach (var m in medium) if (m.Footprint.OverlapsBand(centre, inner, outer)) { burning.Add(m); queue.Enqueue(m); }
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    foreach (var n in medium)
                        if (!burning.Contains(n) && Flat(cur.Footprint.Centre, n.Footprint.Centre) <= adj) { burning.Add(n); queue.Enqueue(n); }
                }
                if (burning.Count == 0) return;
                var occupants = new List<(float d, IVol2Target t)>();
                foreach (var t in Host.Targets)
                {
                    if (t == null || !t.Alive || _affected.Contains(t.Id)) continue;
                    foreach (var m in burning) if (m.Footprint.Touches(t, nowUs)) { occupants.Add((Flat(centre, t.Position), t)); break; }
                }
                foreach (var m in burning)
                    if (Host.Materials.TryEnd(m.Id, Vol2MaterialEnd.Consumed, nowUs)) { Run.Log.Burned.Add(m.Id.Value + ":Consumed:wood"); Gate(m); }
                Spread(Ordered(occupants), nowUs);
            }

            void Gate(Vol2Material m)
            {
                if (!m.GateDoor) return;
                Host.FieldRequest(Run, "vine door " + m.Id + " burnt", f => f.OnVineBurn(Run.Cast, m));
            }
        }
    }
}
