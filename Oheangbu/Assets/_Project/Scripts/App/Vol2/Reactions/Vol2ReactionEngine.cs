using System;
using System.Collections.Generic;
using System.Globalization;
using Oheangbu.Vol2.Rules;
using UnityEngine;
using R = Oheangbu.Vol2.Rules.ReactionRulesV3;

namespace Oheangbu.App
{
    /// <summary>S5-b slice 8 (design s5b_design_v1 section 10, "반응 처리와 신호"): the 25-direction reaction table running on REAL contacts. One instance per coordinator (per combat scope),
    /// off by default (<see cref="Enabled"/>), single-threaded; nothing static. Every tick it does the design's eight steps in order:
    ///  1 FORECAST  none: nothing is reserved, consumed or damaged before a contact (the rules refuse Forecast).
    ///  2 COLLECT   contacts become tickets: a MOVING SHAPE's reach this step against standing materials (the narrow path swept, a fan or ring at the moment it lands), and every pair of standing
    ///              materials whose footprints overlap (this also reads a material the instant it is made, a split half, a reaction child, a carried or grown footprint).
    ///  3 ORDER     time, then contact number (shapes were collected first, as they move), then the stable ids.
    ///  4 DIRECTION the rules decide who acts (a moving body first; two standing ones by spawn time, then cast id); the engine only tells them which is moving.
    ///  5 RESERVE   one single-threaded pass reads each side again from the registry: a side that is gone loses the contact; a side whose REVISION changed since collection (another commit got
    ///              there first) is read afresh and the contact goes on only if it still holds. Material balance, expiry and overlap are checked here, not at collection.
    ///  6 RULE      <see cref="R.Resolve"/> ONCE per contact.
    ///  7 COMMIT    consume / half / thicken / child / ownership / product, applied together (all sides validated first) - then the ledger line.
    ///  8 PRESENT   only the committed result is published: <see cref="ReactionResolved"/> (a <see cref="Vol2ReactionVisual"/>).
    /// What it guarantees (checked by <see cref="Audit"/> and Vol2ReactionFixture): no reaction between two materials of one cast; no body consumed twice; a returning shape cannot consume
    /// the same material again (the rules' ReturnSameMaterial); a child never has a child (depth 1 at most; a child's re-contact only dries it); a reaction's damage gives no groggy, no
    /// posture and no seed trigger (it is requested with Vol2DamageOrigin.Reaction and never announced as a cast contact). Combat numbers are the rules' (NumbersV3 reaction.*); the readings
    /// and stand-ins are in <see cref="Vol2ReactionTuning"/> (TE-S8-n).</summary>
    public sealed class Vol2ReactionEngine : IDisposable
    {
        /// <summary>Rule ids of moving shapes: apart from any MaterialId.</summary>
        public const long ShapeIdBase = 7000000000L;

        struct Ticket
        {
            public long Seq, AtUs;
            public Vol2ReactionShape Shape;
            public long FirstId, SecondId;
            public int Rev1, Rev2;
            public bool FirstMoving, SecondMoving, Returning, HostTouch;
            public Vector3 Point;
            public string Source;
        }

        sealed class Dot
        {
            public AttackId Attack; public CastId Cast; public Glyph Glyph; public Body Body; public IVol2Target Target; public long Rev, NextUs, TickUs, EventId;
            public int Remaining, Index; public decimal UnitsPerTick; public Vector3 Point; public Vol2ReactionRecord Rec; public long Igniter;
        }

        readonly Vol2CastCoordinator _host;
        readonly Dictionary<long, Vol2ReactionShape> _shapes = new Dictionary<long, Vol2ReactionShape>();
        /// <summary>Casts whose moving shape has had its reach read (finished or spent). A cast whose run lives on (a residue for 10 s) is stepped every tick: without this its shape would be made again
        /// and read again from the start (found by Vol2ReactionFixture R2: a ring cast reacted with the next ring a second later).</summary>
        readonly HashSet<long> _shapeDone = new HashSet<long>();
        readonly List<Ticket> _tickets = new List<Ticket>();
        readonly List<Ticket> _batch = new List<Ticket>();
        readonly List<long> _keys = new List<long>();
        readonly List<Vol2Material> _standing = new List<Vol2Material>();
        readonly List<Dot> _dots = new List<Dot>();
        readonly HashSet<(long, long)> _settled = new HashSet<(long, long)>();
        readonly List<Vol2ReactionRecord> _ledger = new List<Vol2ReactionRecord>();
        readonly List<Vol2ReactionAttempt> _attempts = new List<Vol2ReactionAttempt>();
        readonly Dictionary<string, int> _reasons = new Dictionary<string, int>();
        ReactionNumbersV3Dto _rn;
        R.State _carry = new R.State(new R.Material[0]);
        IReadOnlyList<IVol2Target> _targets = new List<IVol2Target>();
        long _seq, _event, _lastTickUs = -1, _movedSeen;
        bool _disposed;

        public Vol2ReactionTuning Tuning { get; set; } = new Vol2ReactionTuning();
        /// <summary>Off: no contact is collected, none is resolved (the fixtures of slices 3 to 7 and the lab rows of the verbs run this way). The scope's runtime turns it on.</summary>
        public bool Enabled { get; set; }
        /// <summary>The caster's faction for shapes the engine makes (all vol.2 casts are the player's).</summary>
        public long ShapeFaction { get; set; } = Vol2Faction.Player;

        /// <summary>Step 8: a committed reaction, once.</summary>
        public event Action<Vol2ReactionVisual> ReactionResolved;
        /// <summary>Every try, committed or refused (diagnosis; the refused ones never reach the presentation).</summary>
        public event Action<Vol2ReactionAttempt> Attempted;

        public IReadOnlyList<Vol2ReactionRecord> Ledger => _ledger;
        public IReadOnlyList<Vol2ReactionAttempt> Attempts => _attempts;
        public IReadOnlyDictionary<string, int> RefusedReasons => _reasons;
        public int Committed { get; private set; }
        public int Refused { get; private set; }
        /// <summary>Contacts lost in the reservation step (a side was gone or moved apart before its turn).</summary>
        public int Lost { get; private set; }
        /// <summary>Contacts whose revision had changed by their turn and that were read again and went on.</summary>
        public int Revalidated { get; private set; }
        public int Exceptions { get; private set; }
        /// <summary>The engine predicts who acts (to price the product) and the rules decide; a difference is counted here and must stay 0.</summary>
        public int ActorMismatch { get; private set; }
        public int PendingTickets => _tickets.Count;
        public int ShapeCount => _shapes.Count;
        public int ActiveDots => _dots.Count;
        public int SettledPairs => _settled.Count;
        public R.State RuleState => _carry;

        /// <summary>둘레 1 (TE-S8-10): one standing ring per player; on only while the engine is.</summary>
        public Vol2RingSlot RingSlot { get; }

        public Vol2ReactionEngine(Vol2CastCoordinator host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            RingSlot = new Vol2RingSlot(host.Materials, host.Services.Clock, () => Enabled && Tuning.RingSlot);
            host.Materials.Ended += OnMaterialEnded;
        }

        // a settled pair is only worth remembering while both of its materials live
        void OnMaterialEnded(Vol2Material m, Vol2MaterialEnd why)
        {
            if (_settled.Count == 0) return;
            long id = m.Id.Value;
            _settled.RemoveWhere(p => p.Item1 == id || p.Item2 == id);
        }

        public void Dispose() { _disposed = true; _tickets.Clear(); _dots.Clear(); _shapes.Clear(); RingSlot.Dispose(); _host.Materials.Ended -= OnMaterialEnded; }

        /// <summary>Scope reset / the lab between rows: nothing of the previous contacts survives. The rule's own ledger starts empty again.</summary>
        public void Reset()
        {
            foreach (var d in _dots) ReleaseAttack(d.Attack);
            _dots.Clear(); _tickets.Clear(); _shapes.Clear(); _shapeDone.Clear(); _settled.Clear();
            _ledger.Clear(); _attempts.Clear(); _reasons.Clear();
            _carry = new R.State(new R.Material[0]);
            Committed = Refused = Lost = Revalidated = Exceptions = ActorMismatch = 0;
            _lastTickUs = -1; _movedSeen = _host.Materials.MoveSerial;
        }

        ReactionNumbersV3Dto Rn => _rn ?? (_rn = new ReactionNumbersV3Dto(_host.Numbers));

        // ------------------------------------------------------------------ step 2: collect ------------------------------------------------------------------

        static bool Live(Vol2Material m, long now) => m != null && m.Alive && now >= m.CreatedUs && now < m.ExpiresUs && m.Remaining > 0m && m.Letter != '걱' && m.Footprint != null;

        static bool Lineage(Vol2Material x, Vol2Material y)
        {
            if (x.Cast == y.Cast) return true;
            if (x.Ancestors != null && x.Ancestors.Contains(y.Cast.Value)) return true;
            if (y.Ancestors != null && y.Ancestors.Contains(x.Cast.Value)) return true;
            if (x.Ancestors != null && y.Ancestors != null) foreach (long c in x.Ancestors) if (y.Ancestors.Contains(c)) return true;
            return false;
        }

        Vol2ReactionShape ShapeOf(Vol2CastRun run)
        {
            if (_shapes.TryGetValue(run.Cast.Value, out var s)) return s;
            if (_shapeDone.Contains(run.Cast.Value)) return null;
            decimal balance = Math.Max(run.Plan.Total, Tuning.GhostMinBalance);
            s = new Vol2ReactionShape
            {
                Cast = run.Cast, Glyph = run.Glyph, Body = run.Glyph.Body, Kind = run.Spec.Kind, Spec = run.Spec, OwnerId = ShapeFaction, FactionId = ShapeFaction,
                StartUs = run.StartUs, ExpiresUs = checked(run.StartUs + Math.Max(1L, run.Plan.LifeUs)), Balance = balance, DirectBudget = balance
            };
            _shapes[run.Cast.Value] = s;
            return s;
        }

        /// <summary>The coordinator calls this after a cast's emitter stepped. A moving shape's reach this step is read against the standing materials (TE-S8-1, TE-S8-6, TE-S8-9).</summary>
        public void OnShapeStep(Vol2CastRun run, long now)
        {
            if (!Enabled || _disposed || run == null) return;
            if (run.Glyph.IsParry || run.Plan.IsRecovery || run.Spec.Kind == Vol2ShapeKind.Attachment) return;
            // TE-S8-14: a 가른다 cast' s contact with a material IS the split (table: "닿은 재료를 둘로 나눔"); its shape does not also react with it, or the engine would burn the very
            // patch the verb exists to cut. (Found by the engine-on diagnostic lab run: 13 split rows lost their split.) The halves it makes are materials and react like any.
            if (run.Glyph.Verb == Verb.Split) return;
            var shape = ShapeOf(run);
            if (shape == null) return;
            if (shape.Finished || shape.Consumed) { _shapeDone.Add(run.Cast.Value); return; }
            var hits = new List<(float key, Vol2Material m, Vector3 point)>();
            var spec = run.Spec;
            switch (spec.Kind)
            {
                case Vol2ShapeKind.Path:
                    {
                        var pe = run.Emitter as Vol2PathEmitter;
                        if (pe == null) { shape.Finished = true; return; }
                        float from = shape.PrevAlong, to = pe.HeadAlong;
                        if (to < from) to = from;
                        float half = spec.PathWidth * 0.5f;
                        foreach (var m in _host.Materials.Alive)
                        {
                            if (!Live(m, now) || m.Attached || m.Cast == run.Cast || (m.Ancestors != null && m.Ancestors.Contains(run.Cast.Value))) continue;
                            if (Vol2FootprintGeometry.SweptDiscTouches(spec.Origin, spec.Forward, from, to, half, m.Footprint, now, Tuning.SampleStepMeters, out float entry))
                                hits.Add((entry, m, spec.Origin + spec.Forward * entry + Vector3.up * 0.1f));
                        }
                        shape.PrevAlong = to; shape.Stepped = true;
                        if (pe.Finished) shape.Finished = true;
                        break;
                    }
                case Vol2ShapeKind.Fan:
                case Vol2ShapeKind.Ring:
                    {
                        shape.Finished = true;               // a fan or a ring lands in one step (the ones that keep living leave a material, which the sweep reads)
                        foreach (var m in _host.Materials.Alive)
                        {
                            if (!Live(m, now) || m.Attached || m.Cast == run.Cast || (m.Ancestors != null && m.Ancestors.Contains(run.Cast.Value))) continue;
                            bool touch = spec.Kind == Vol2ShapeKind.Fan
                                ? Vol2FootprintGeometry.FanTouches(spec.Origin, spec.Forward, spec.FanHalfAngleRad, spec.FanDepth, m.Footprint, now)
                                : Vol2FootprintGeometry.RingTouches(spec.Origin, spec.RingInner, spec.RingOuter, m.Footprint, now);
                            if (touch) hits.Add((Flat(spec.Origin, m.Footprint.Centre), m, m.Footprint.Centre));
                        }
                        break;
                    }
            }
            hits.Sort((a, b) => { int c = a.key.CompareTo(b.key); return c != 0 ? c : a.m.Id.Value.CompareTo(b.m.Id.Value); });
            foreach (var h in hits)
                _tickets.Add(new Ticket { Seq = ++_seq, AtUs = now, Shape = shape, FirstId = shape.RuleId, SecondId = h.m.Id.Value, Rev1 = 0, Rev2 = h.m.Revision, FirstMoving = true, Point = h.point, Source = "shape" });
        }

        /// <summary>Entry for an adapter that owns a moving shape of its own (an enemy projectile, a returning leg, a fixture): the shape touches a material. Not pre-filtered: the rules refuse
        /// what they refuse (same cast, spent balance, the same material again on a return). Returns false only if the material is unknown.</summary>
        public bool SubmitShape(Vol2ReactionShape shape, MaterialId contacted, bool returning = false, Vector3? point = null)
        {
            if (shape == null || !_host.Materials.TryGet(contacted, out var m)) return false;
            _shapes[shape.Cast.Value] = shape;
            _tickets.Add(new Ticket { Seq = ++_seq, AtUs = _host.Services.Clock.NowUs, Shape = shape, FirstId = shape.RuleId, SecondId = m.Id.Value, Rev2 = m.Revision, FirstMoving = true, Returning = returning,
                Point = point ?? (m.Footprint != null ? m.Footprint.Centre : Vector3.zero), Source = "submit" });
            return true;
        }

        /// <summary>A hostile shape touches the HOST of a body-attached material (normal-contact eligibility, TE-S8-6). Refused if the material is not attached or not hostile to the shape.</summary>
        public bool SubmitHostTouch(Vol2ReactionShape hostile, MaterialId attached)
        {
            if (hostile == null || !_host.Materials.TryGet(attached, out var m) || !Vol2ReactionEligibility.HostTouchedBy(m, hostile.FactionId)) return false;
            if (!SubmitShape(hostile, attached, false, m.Footprint.Origin)) return false;
            var t = _tickets[_tickets.Count - 1]; t.HostTouch = true; t.Source = "host"; _tickets[_tickets.Count - 1] = t;
            return true;
        }

        /// <summary>Two standing materials are in contact (an adapter, a fixture). <paramref name="firstMoving"/> / <paramref name="secondMoving"/> tell the rules which one is on the move.</summary>
        public bool SubmitMaterials(MaterialId first, MaterialId second, bool firstMoving = false, bool secondMoving = false, bool returning = false)
        {
            if (!_host.Materials.TryGet(first, out var a) || !_host.Materials.TryGet(second, out var b)) return false;
            _tickets.Add(new Ticket { Seq = ++_seq, AtUs = _host.Services.Clock.NowUs, FirstId = a.Id.Value, SecondId = b.Id.Value, Rev1 = a.Revision, Rev2 = b.Revision, FirstMoving = firstMoving, SecondMoving = secondMoving,
                Returning = returning, Point = b.Footprint != null ? b.Footprint.Centre : Vector3.zero, Source = "submit" });
            return true;
        }

        void CollectStanding(long now)
        {
            var list = _standing; list.Clear();
            foreach (var m in _host.Materials.Alive) { if (Live(m, now) && !m.Attached && m.Footprint.Kind != Vol2FootprintKind.Attached) list.Add(m); if (list.Count >= Tuning.MaxMaterialsScanned) break; }
            if (list.Count < 2) return;
            list.Sort((x, y) => x.Id.Value.CompareTo(y.Id.Value));
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    var x = list[i]; var y = list[j];
                    if (_settled.Contains((x.Id.Value, y.Id.Value)) || Lineage(x, y)) continue;
                    if (!Vol2FootprintGeometry.Overlaps(x.Footprint, y.Footprint, now)) continue;
                    _tickets.Add(new Ticket
                    {
                        Seq = ++_seq, AtUs = now, FirstId = x.Id.Value, SecondId = y.Id.Value, Rev1 = x.Revision, Rev2 = y.Revision,
                        FirstMoving = x.MovedSerial > _movedSeen, SecondMoving = y.MovedSerial > _movedSeen,
                        Point = (x.Footprint.Centre + y.Footprint.Centre) * 0.5f, Source = "standing"
                    });
                }
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return (a - b).magnitude; }

        // ------------------------------------------------------------------ tick ------------------------------------------------------------------

        public void Tick(long now, IReadOnlyList<IVol2Target> targets)
        {
            if (_disposed) return;
            if (targets != null) _targets = targets;
            if (!Enabled) { _tickets.Clear(); return; }
            try
            {
                _keys.Clear(); _keys.AddRange(_shapes.Keys);
                foreach (var key in _keys) { var s = _shapes[key]; if (s.Finished || s.Consumed || now >= s.ExpiresUs) { _shapes.Remove(key); _shapeDone.Add(key); } }
                if (_shapeDone.Count > 8192) _shapeDone.Clear();        // a long session: forget the oldest (a run lives 10 s at most)
                CollectStanding(now);
                _tickets.Sort((a, b) =>
                {
                    int c = a.AtUs.CompareTo(b.AtUs); if (c != 0) return c;
                    c = a.Seq.CompareTo(b.Seq); if (c != 0) return c;
                    c = Math.Min(a.FirstId, a.SecondId).CompareTo(Math.Min(b.FirstId, b.SecondId)); if (c != 0) return c;
                    return Math.Max(a.FirstId, a.SecondId).CompareTo(Math.Max(b.FirstId, b.SecondId));
                });
                _batch.Clear(); _batch.AddRange(_tickets); _tickets.Clear();
                foreach (var t in _batch)
                {
                    try { Process(t, now); }
                    catch (Exception e) { Exceptions++; Debug.LogException(e); }
                }
                TickDots(now);
            }
            finally { _lastTickUs = now; _movedSeen = _host.Materials.MoveSerial; }
        }

        // ------------------------------------------------------------------ steps 4 - 8 ------------------------------------------------------------------

        static R.Material ToRule(Vol2Material m)
            => new R.Material(m.Id.Value, m.Cast.Value, m.Body, m.Remaining, m.CreatedUs, Math.Max(m.ExpiresUs, m.CreatedUs), m.OwnerId, m.FactionId, m.Id.Value, m.Depth, true, m.Thickened, !m.Alive, m.Letter, m.Ancestors);

        static R.Material ToRule(Vol2ReactionShape s)
            => new R.Material(s.RuleId, s.Cast.Value, s.Body, s.Balance, s.StartUs, Math.Max(s.ExpiresUs, s.StartUs), s.OwnerId, s.FactionId, s.RuleId, s.Depth, false, false, s.Consumed, s.Glyph.Letter);

        /// <summary>Mirrors the rule's choice of the acting side so the product can be priced before the rule is asked; the rule remains the authority (a difference is counted).</summary>
        static bool FirstActs(R.Material f, R.Material s, bool fm, bool sm) => fm != sm ? fm : f.CreatedUs != s.CreatedUs ? f.CreatedUs > s.CreatedUs : f.CastId > s.CastId;

        void Note(long eventId, long now, long first, long second, bool ok, string reason)
        {
            var at = new Vol2ReactionAttempt(eventId, now, first, second, ok, reason);
            _attempts.Add(at); while (_attempts.Count > Tuning.AttemptKeep) _attempts.RemoveAt(0);
            if (!ok) { Refused++; _reasons.TryGetValue(reason, out int n); _reasons[reason] = n + 1; }
            Attempted?.Invoke(at);
        }

        void Lose(in Ticket t, long now, string why)
        {
            Lost++;
            _reasons.TryGetValue("ReservationLost", out int n); _reasons["ReservationLost"] = n + 1;
            var at = new Vol2ReactionAttempt(0, now, t.FirstId, t.SecondId, false, "ReservationLost: " + why);
            _attempts.Add(at); while (_attempts.Count > Tuning.AttemptKeep) _attempts.RemoveAt(0);
            Attempted?.Invoke(at);
        }

        void Process(Ticket t, long now)
        {
            // 5 RESERVE ------------------------------------------------------------------
            Vol2Material m1 = null, m2;
            if (t.Shape == null)
            {
                if (!_host.Materials.TryGet(new MaterialId(t.FirstId), out m1) || !Live(m1, now)) { Lose(t, now, "first side gone"); return; }
            }
            else if (t.Shape.Consumed || now >= t.Shape.ExpiresUs) { Lose(t, now, "shape spent"); return; }
            if (!_host.Materials.TryGet(new MaterialId(t.SecondId), out m2) || !Live(m2, now)) { Lose(t, now, "second side gone"); return; }
            bool revalidated = false;
            if ((m1 != null && m1.Revision != t.Rev1) || m2.Revision != t.Rev2)
            {
                revalidated = true; Revalidated++;      // another commit changed a side since collection: this contact reads it afresh and goes on only if it still holds
                if (m1 != null && !t.HostTouch && !Vol2FootprintGeometry.Overlaps(m1.Footprint, m2.Footprint, now)) { Lose(t, now, "moved apart"); return; }
            }
            R.Material a1 = m1 != null ? ToRule(m1) : ToRule(t.Shape), a2 = ToRule(m2);
            bool firstActs = FirstActs(a1, a2, t.FirstMoving, t.SecondMoving);
            R.Material actor = firstActs ? a1 : a2, other = firstActs ? a2 : a1;
            decimal direct = firstActs ? (m1 != null ? m1.Initial : t.Shape.DirectBudget) : m2.Initial;
            bool enemyIgniter = actor.FactionId == Vol2Faction.Enemy;

            // 6 RULE ------------------------------------------------------------------
            long childId = 0;
            var rn = Rn;
            var kind = R.Relation(actor.Body, other.Body);
            if ((kind == R.Kind.Feed || kind == R.Kind.Transfer) && actor.Depth < rn.Depth && other.Depth < rn.Depth) childId = _host.Services.Ids.NextMaterial().Value;
            long eventId = ++_event;
            var pair = new R.ContactPair(eventId, a1.Id, a2.Id, now, childId, t.FirstMoving, t.SecondMoving, false, t.Returning, direct, enemyIgniter);
            var step = R.Resolve(R.State.Carry(_carry, new[] { a1, a2 }), pair, rn);
            Note(eventId, now, a1.Id, a2.Id, step.Accepted, step.Reason);
            if (!step.Accepted) { _settled.Add(Key(a1.Id, a2.Id)); return; }
            var cmd = step.Commands[0];
            if (cmd.ActingId != actor.Id) ActorMismatch++;

            // 7 COMMIT ------------------------------------------------------------------
            if (!_host.Services.Commands.TryBegin(new CommandKey(_host.Services.Ids.ScopeId, CommandRuleKind.Material, eventId, 0))) { Lost++; return; }       // the same event twice is one commit
            Commit(t, cmd, step.State, m1, m2, actor, other, direct, enemyIgniter, revalidated, now);
            _carry = step.State;
        }

        static (long, long) Key(long a, long b) => a < b ? (a, b) : (b, a);

        void Commit(in Ticket t, R.Command cmd, R.State after, Vol2Material m1, Vol2Material m2, R.Material actor, R.Material other, decimal direct, bool enemyIgniter, bool revalidated, long now)
        {
            var reg = _host.Materials;
            var rec = new Vol2ReactionRecord
            {
                EventId = cmd.Id, AtUs = now, ActingId = actor.Id, ContactId = other.Id, ActingIsShape = !actor.IsMaterial, ActingCast = new CastId(actor.CastId), ContactCast = new CastId(other.CastId),
                ActingDepth = actor.Depth, ContactDepth = other.Depth, ActingOwner = actor.OwnerId, ContactOwner = other.OwnerId, ActingFaction = actor.FactionId, ContactFaction = other.FactionId,
                ActingBody = actor.Body, ContactedBody = other.Body, Relation = cmd.Reaction, Product = cmd.Product, Returning = t.Returning, ParentSum = actor.Remaining + other.Remaining,
                DamageBudget = cmd.DamageBudget, Revalidated = revalidated, FromShape = t.Shape != null
            };
            var visual = new Vol2ReactionVisual
            {
                EventId = cmd.Id, AtUs = now, Relation = cmd.Reaction, Product = cmd.Product, ActingBody = actor.Body, ContactedBody = other.Body, ActingId = actor.Id, ContactId = other.Id,
                ActingIsShape = !actor.IsMaterial, ActingCast = rec.ActingCast, ContactCast = rec.ContactCast, OwnerId = cmd.OwnerId, FactionId = cmd.FactionId,
                DryUs = cmd.DryUs, DurationUs = cmd.DurationUs, OcclusionUs = cmd.OcclusionUs, DamageBudget = cmd.DamageBudget, Point = t.Point, Returning = t.Returning,
                Legacy = Vol2ReactionVisual.LegacyOf(cmd.Reaction, actor.Body, other.Body)
            };
            Vol2Material actingMat = actor.IsMaterial ? (actor.Id == t.SecondId ? m2 : m1) : null;
            Vol2Material contactMat = other.Id == t.SecondId ? m2 : m1;
            visual.Where = contactMat != null ? contactMat.Footprint : null;
            if (contactMat != null && !t.HostTouch) visual.Point = contactMat.Footprint.Centre;

            // all sides were validated in the reservation step; now apply them together
            var consumedSet = new HashSet<long>(cmd.ConsumedIds);
            var driedSet = new HashSet<long>(cmd.ChildConsumedIds);
            foreach (long id in cmd.ConsumedIds)
            {
                bool dried = driedSet.Contains(id);
                var reason = dried ? Vol2MaterialEnd.ChildConsumed : Vol2MaterialEnd.Consumed;
                rec.Consumed.Add(id); if (dried) rec.ChildConsumed.Add(id);
                if (id >= ShapeIdBase) { if (t.Shape != null && t.Shape.RuleId == id) t.Shape.Consumed = true; }
                else reg.TryEnd(new MaterialId(id), reason, now);
                if (id == actor.Id) { visual.ActingEnd = reason; visual.ActingReason = dried ? "ChildConsumed(dried " + (cmd.DryUs / 1000) + " ms)" : "Consumed"; }
                else { visual.ContactEnd = reason; visual.ContactReason = dried ? "ChildConsumed(dried " + (cmd.DryUs / 1000) + " ms)" : "Consumed"; }
            }
            if (!consumedSet.Contains(other.Id) && contactMat != null)
            {
                var o = after.Materials[other.Id];
                if (cmd.Reaction == R.Kind.Block) { if (reg.TrySetRemaining(contactMat.Id, o.Remaining, now)) visual.ContactReason = "Blocked(half)"; }
                else if (cmd.Reaction == R.Kind.Same) { if (reg.TryThicken(contactMat.Id, o.Remaining, o.ExpiresUs, now)) visual.ContactReason = "Thickened"; }
            }
            if (cmd.Child != null)
            {
                var c = cmd.Child;
                var fp = contactMat != null ? contactMat.Footprint : (actingMat != null ? actingMat.Footprint : null);
                var made = reg.Create(new CastId(c.CastId), '\0', c.Body, Vol2MaterialKind.Residue, c.Remaining, c.ExpiresUs - c.CreatedUs, fp, c.OwnerId, c.FactionId, false, c.CreatedUs, c.Depth, c.Id, "child", false, c.Ancestors, "reaction");
                rec.ChildId = made.Id.Value; rec.ChildDepth = made.Depth; rec.ChildOwner = made.OwnerId; rec.ChildFaction = made.FactionId; rec.ChildAmount = made.Remaining;
                visual.ChildId = made.Id.Value; visual.ChildBody = made.Body; visual.ChildAmount = made.Remaining; visual.ChildLifeUs = made.LifeUs; visual.ChildDepth = made.Depth;
            }
            // the vine of a gate that fire reached opens (the same report 눈 makes)
            if (contactMat != null && contactMat.GateDoor && actor.Body == Body.Fire && other.Body == Body.Wood && consumedSet.Contains(other.Id))
            {
                string note = _host.Field != null ? _host.Field.OnVineBurn(new CastId(actor.CastId), contactMat) : "no field port: recorded only";
                visual.ContactReason += " | gate: " + note;
            }
            // product ------------------------------------------------------------------
            if (cmd.Product == R.Product.Damage || cmd.Product == R.Product.Slow) ApplyProduct(cmd, actor, other, contactMat, rec, visual, now);
            _ledger.Add(rec); while (_ledger.Count > Tuning.LedgerKeep) _ledger.RemoveAt(0);
            if (_host.TryGetLog(rec.ActingCast, out var actingLog)) actingLog.Notes.Add("reaction #" + rec.EventId + " " + rec.ActingBody + ">" + rec.ContactedBody + " " + rec.Relation + "/" + rec.Product + " on M" + rec.ContactId + (rec.ChildId > 0 ? " -> child M" + rec.ChildId : "") + " at " + now + " us");
            Committed++;
            // 8 PRESENT ------------------------------------------------------------------
            try { ReactionResolved?.Invoke(visual); } catch (Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------------ products ------------------------------------------------------------------

        List<IVol2Target> Affected(Vol2Material contact, R.Command cmd, long now)
        {
            var list = new List<IVol2Target>();
            if (contact == null || contact.Attached) return list;          // TE-S8-3: the player is no Vol2 target
            if (!cmd.Affects(Vol2Faction.Enemy)) return list;               // an enemy-owned reaction does not harm enemies
            foreach (var t in _targets)
            {
                if (t == null || !t.Alive) continue;
                if (contact.Footprint.Touches(t, now)) list.Add(t);
            }
            list.Sort((x, y) => x.Id.CompareTo(y.Id));
            return list;
        }

        Glyph GlyphOf(CastId cast)
        {
            if (_host.TryGetLog(cast, out var log)) return log.Glyph;
            if (_shapes.TryGetValue(cast.Value, out var s)) return s.Glyph;
            return default;
        }

        void ApplyProduct(R.Command cmd, R.Material actor, R.Material other, Vol2Material contact, Vol2ReactionRecord rec, Vol2ReactionVisual visual, long now)
        {
            var affected = Affected(contact, cmd, now);
            rec.Affected = affected.Count; visual.AffectedBodies = affected.Count;
            foreach (var t in affected) rec.AffectedIds.Add(t.Id);
            if (affected.Count == 0) return;
            var cast = new CastId(actor.CastId);
            var glyph = GlyphOf(cast);
            if (cmd.Product == R.Product.Slow)
            {
                float magnitude = (float)_host.Profile(Body.Wood).MoveMultiplier;
                foreach (var t in affected)
                {
                    var attack = NewAttack(cast, glyph, actor, other, now, 1);
                    Vector3 dir = Vol2ShapeGeometry.FlatUnit(t.Position - (contact != null ? contact.Footprint.Origin : Vector3.zero));
                    if (_host.Status != null) { _host.Status.Apply(new Vol2BodyEffectRequest(Vol2BodyActionKind.Bind, cast, attack, t, now, cmd.DurationUs, magnitude, dir, 0f)); rec.SlowsApplied++; }
                    ReleaseAttack(attack);
                }
                return;
            }
            // damage: the product's budget is SHARED by the bodies it reaches (TE-S8-2); a lasting one ticks (TE-S8-5)
            decimal share = cmd.DamageBudget / affected.Count;
            if (!(share > 0m)) return;
            long tick = Math.Max(1L, Tuning.DamageTickUs);
            int ticks = cmd.DurationUs > 0 ? (int)Math.Max(1L, cmd.DurationUs / tick) : 1;
            int ordinal = 0;
            foreach (var t in affected)
            {
                var attack = NewAttack(cast, glyph, actor, other, now, 1);
                if (ticks <= 1)
                {
                    DealTick(t, attack, cast, glyph, actor.Body, share, contact != null ? contact.Footprint.Centre : t.Position, now, rec, ordinal++);
                    ReleaseAttack(attack);
                }
                else
                    _dots.Add(new Dot { Attack = attack, Cast = cast, Glyph = glyph, Body = actor.Body, Target = t, Rev = t.LifeRevision, NextUs = now + tick, TickUs = tick, Remaining = ticks, UnitsPerTick = share / ticks,
                        Point = t.Position, Rec = rec, EventId = cmd.Id, Igniter = actor.OwnerId });
                rec.UnitsGiven += share;
            }
            visual.DamageBudget = cmd.DamageBudget;
            rec.DamageBudget = cmd.DamageBudget;
        }

        AttackId NewAttack(CastId cast, Glyph glyph, R.Material actor, R.Material other, long now, int depth)
        {
            var attack = _host.Services.Ids.NextAttack();
            _host.Services.Attacks.Register(new Vol2AttackMetadata(attack, cast, new SwingId(0), glyph, now, depth, other.OwnerId, actor.OwnerId));
            return attack;
        }

        void ReleaseAttack(AttackId attack) { if (attack.IsValid) _host.Services.Attacks.Release(attack); }

        void DealTick(IVol2Target t, AttackId attack, CastId cast, Glyph glyph, Body body, decimal units, Vector3 point, long now, Vol2ReactionRecord rec, int ordinal)
        {
            if (!_host.Services.Commands.TryBegin(new CommandKey(_host.Services.Ids.ScopeId, CommandRuleKind.Damage, attack.Value, ordinal))) return;
            if (_host.Damage == null) return;
            var profile = _host.Profile(body);
            // R = 0, S = 1: a reaction has no structure term; the entry applies the target's own window and armour once and converts units to HP (Vol2DamageOrigin.Reaction: no groggy, no posture, no seed)
            var request = new Vol2DamageRequest(cast, attack, 0, t, glyph, profile.Element, profile.Instant, 0m, 1m, units, 1m, true, now, point, _host.Caster != null ? _host.Caster.Anchor : null, Vol2DamageOrigin.Reaction);
            var result = _host.Damage.Apply(request);
            rec.Hits++; if (result.Applied) rec.HpApplied += result.Hp;
        }

        void TickDots(long now)
        {
            for (int i = _dots.Count - 1; i >= 0; i--)
            {
                var d = _dots[i];
                while (d.Remaining > 0 && now >= d.NextUs)
                {
                    bool alive = d.Target.Alive && d.Target.LifeRevision == d.Rev;
                    if (!alive) { d.Remaining = 0; break; }
                    DealTick(d.Target, d.Attack, d.Cast, d.Glyph, d.Body, d.UnitsPerTick, d.Point, now, d.Rec, 100 + d.Index);
                    d.Index++; d.Remaining--; d.NextUs += d.TickUs;
                }
                if (d.Remaining <= 0) { ReleaseAttack(d.Attack); _dots.RemoveAt(i); }
            }
        }

        // ------------------------------------------------------------------ the ledger checks ------------------------------------------------------------------

        /// <summary>The ledger checks of design section 10. Returns the problems found (empty = clean): no reaction between two materials of one cast; no contact (cast x material) twice; no body
        /// consumed by two reactions; a returning shape never consumed the same material twice; no grandchildren; the child takes the acting side's owner and faction; damage gave no
        /// groggy and no seed trigger.</summary>
        public List<string> Audit()
        {
            var problems = new List<string>();
            var contacts = new HashSet<string>(); var consumed = new HashSet<long>();
            foreach (var r in _ledger)
            {
                if (r.ActingCast == r.ContactCast) problems.Add(r + " same cast");
                string key = r.ActingCast.Value + ":" + r.ContactId;
                if (!contacts.Add(key)) problems.Add(r + " contact twice (" + key + ")" + (r.Returning ? " [a return consumed the same material again]" : ""));
                foreach (long id in r.Consumed) if (!consumed.Add(id)) problems.Add(r + " body " + id + " consumed twice");
                if (r.ChildId > 0 && (r.ActingDepth > 0 || r.ContactDepth > 0)) problems.Add(r + " a child had a child (grandchild " + r.ChildId + ")");
                if (r.ChildId > 0 && r.ChildDepth > 1) problems.Add(r + " child depth " + r.ChildDepth);
                if (r.ChildId > 0 && (r.ChildOwner != r.ActingOwner || r.ChildFaction != r.ActingFaction)) problems.Add(r + " the child did not take the acting owner");
                if (r.Groggy != 0m || r.SeedTriggers != 0) problems.Add(r + " groggy / seed trigger");
                if (r.Relation == R.Kind.Break && r.Product == R.Product.Child) problems.Add(r + " a break made a child");
            }
            if (ActorMismatch != 0) problems.Add("the engine predicted the acting side wrongly " + ActorMismatch + " time(s)");
            return problems;
        }
    }
}
