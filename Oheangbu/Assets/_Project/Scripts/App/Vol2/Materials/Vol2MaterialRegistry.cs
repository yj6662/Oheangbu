using System;
using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // S5-b slice 4 (design s5b_design_v1 section 6, Vol2MaterialRegistry): the ledger of every material a vol.2 cast leaves in the world - the
    // 남긴다 residues, the 번진다 marks, the body-attached carrier of 넌. THE REGISTRY IS THE TRUTH of what a material still holds: the cast's
    // EffectBudgetV3 state only books the one transfer that created it (ContactKind.Transfer, so a later touch, tick or reaction can never be
    // refused as "Expired" by the cast's own life, which 국 may extend); every later draw is a TryDebit here. Slice 8 (reactions) reads
    // snapshots of this registry into ReactionRulesV3.State and commits a step back through TryDebit / TryEnd / Create.
    // One instance per combat scope; nothing static. Single-threaded (main thread): "atomic" means validated first, then committed whole.

    public enum Vol2MaterialKind { Residue, Mark, Carrier }

    /// <summary>Why a material stopped being a material. Natural expiry (a body direction in the final look) differs from a child's drying
    /// (no direction) and from a cancel (design section 11, "종료 원인은 구별한다"). <c>Aborted</c> is the scope / lab reset, never a gameplay end.</summary>
    public enum Vol2MaterialEnd { None, Expired, Consumed, ChildConsumed, Replaced, HitCancelled, Cleansed, Aborted }

    /// <summary>Who a material belongs to. Reactions need it (an enemy patch is burnt by 눈 and cleansed by 웅; a world vine is a gate).</summary>
    public static class Vol2Faction { public const long Player = 1, Enemy = 2, World = 3; }

    public enum Vol2FootprintKind { Attached, Disc, Fan, Ring }

    /// <summary>Where a material is in the world (horizontal plane, like every vol.2 shape). Immutable; a moved material gets a new footprint.
    /// A fan can grow (곡): the end scale is LINEAR on the radius with the angle fixed, so the area grows by scale squared - ONE factor
    /// (peak.곡.area = 1.5 -> linear sqrt 1.5), never 1.5 on both length and width.</summary>
    public sealed class Vol2Footprint
    {
        public Vol2FootprintKind Kind { get; }
        public Vector3 Origin { get; }
        public Vector3 Forward { get; }
        /// <summary>Disc radius.</summary>
        public float Radius { get; }
        public float FanDepth { get; }
        public float FanHalfAngleRad { get; }
        public float RingInner { get; }
        public float RingOuter { get; }
        public float GrowLinear { get; }
        public long GrowStartUs { get; }
        public long GrowEndUs { get; }

        Vol2Footprint(Vol2FootprintKind kind, Vector3 origin, Vector3 forward, float radius, float depth, float half, float inner, float outer, float grow, long growStart, long growEnd)
        { Kind = kind; Origin = origin; Forward = Vol2ShapeGeometry.FlatUnit(forward); Radius = radius; FanDepth = depth; FanHalfAngleRad = half; RingInner = inner; RingOuter = outer; GrowLinear = grow; GrowStartUs = growStart; GrowEndUs = growEnd; }

        /// <summary>On the caster's body: no place in the world, touched only by the swing.</summary>
        public static Vol2Footprint Attached(Vector3 origin) => new Vol2Footprint(Vol2FootprintKind.Attached, origin, Vector3.forward, 0f, 0f, 0f, 0f, 0f, 1f, 0, 0);
        public static Vol2Footprint Disc(Vector3 centre, float radius) => new Vol2Footprint(Vol2FootprintKind.Disc, centre, Vector3.forward, radius, 0f, 0f, 0f, 0f, 1f, 0, 0);
        public static Vol2Footprint Fan(Vol2ShapeSpec spec, float growLinear = 1f, long growStartUs = 0, long growEndUs = 0)
            => new Vol2Footprint(Vol2FootprintKind.Fan, spec.Origin, spec.Forward, 0f, spec.FanDepth, spec.FanHalfAngleRad, 0f, 0f, growLinear, growStartUs, growEndUs);
        public static Vol2Footprint Ring(Vol2ShapeSpec spec) => new Vol2Footprint(Vol2FootprintKind.Ring, spec.Origin, spec.Forward, 0f, 0f, 0f, spec.RingInner, spec.RingOuter, 1f, 0, 0);
        /// <summary>S5-b slice 8 (open seam of slice 7): a ring footprint from plain numbers - a world ring, the band of a returning ring, a ring the reaction rules put down.</summary>
        public static Vol2Footprint RingAt(Vector3 centre, float inner, float outer) => new Vol2Footprint(Vol2FootprintKind.Ring, centre, Vector3.forward, 0f, 0f, 0f, Mathf.Max(0f, inner), Mathf.Max(inner, outer), 1f, 0, 0);
        /// <summary>S5-b slice 8: a fan from plain numbers (a world fan; a carried fan keeps its shape at its new apex).</summary>
        public static Vol2Footprint FanAt(Vector3 apex, Vector3 forward, float depth, float halfAngleRad, float growLinear = 1f, long growStartUs = 0, long growEndUs = 0)
            => new Vol2Footprint(Vol2FootprintKind.Fan, apex, forward, 0f, depth, halfAngleRad, 0f, 0f, growLinear, growStartUs, growEndUs);

        /// <summary>The linear scale at a moment (1 unless the footprint grows; linear in time between start and end).</summary>
        public float ScaleAt(long nowUs)
        {
            if (GrowLinear == 1f || GrowEndUs <= GrowStartUs) return 1f;
            float p = Mathf.Clamp01((float)((nowUs - GrowStartUs) / (double)(GrowEndUs - GrowStartUs)));
            return 1f + (GrowLinear - 1f) * p;
        }

        /// <summary>A point used for "near" tests between materials (fan: the middle of its depth).</summary>
        public Vector3 Centre => Kind == Vol2FootprintKind.Fan ? Origin + Forward * (FanDepth * 0.5f) : Origin;

        public bool Touches(IVol2Target t, long nowUs)
        {
            if (t == null) return false;
            switch (Kind)
            {
                case Vol2FootprintKind.Disc: return Flat(Origin, t.Position) <= Radius + t.Radius;
                case Vol2FootprintKind.Fan: return Vol2ShapeGeometry.FanHit(Origin, Forward, FanHalfAngleRad, FanDepth * ScaleAt(nowUs), t.Position, t.Radius);
                case Vol2FootprintKind.Ring: return Vol2ShapeGeometry.RingCrossed(Origin, RingInner, RingOuter, t.Previous, t.Position, t.Radius);
                default: return false;
            }
        }

        /// <summary>Does this footprint overlap the ring band [inner, outer] around <paramref name="ringCentre"/>? (The fan is sampled: apex, mid and far arc.)</summary>
        public bool OverlapsBand(Vector3 ringCentre, float inner, float outer)
        {
            switch (Kind)
            {
                case Vol2FootprintKind.Disc: { float d = Flat(ringCentre, Origin); return d - Radius <= outer && d + Radius >= inner; }
                case Vol2FootprintKind.Ring: { float d = Flat(ringCentre, Origin); return d <= RingOuter + outer && d + RingOuter >= inner && !(d + outer < RingInner); }
                case Vol2FootprintKind.Fan:
                    {
                        for (int ring = 0; ring <= 2; ring++)
                            for (int k = -2; k <= 2; k++)
                            {
                                float a = FanHalfAngleRad * k / 2f;
                                Vector3 dir = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * Forward;
                                float d = Flat(ringCentre, Origin + dir * (FanDepth * ring / 2f));
                                if (d >= inner && d <= outer) return true;
                            }
                        return false;
                    }
                default: return false;
            }
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return (a - b).magnitude; }

        /// <summary>Planar distance from a point to the fan sector (0 inside). The 번짐 of a fan reaches what stands within 3 m of its edge.</summary>
        public static float FanDistance(Vector3 apex, Vector3 forward, float halfAngleRad, float depth, Vector3 pos)
        {
            Vector2 a = new Vector2(apex.x, apex.z), c = new Vector2(pos.x, pos.z) - a;
            Vector3 fu = Vol2ShapeGeometry.FlatUnit(forward); Vector2 f = new Vector2(fu.x, fu.z);
            float dist = c.magnitude, along = Vector2.Dot(c, f), lat = Mathf.Abs(c.x * f.y - c.y * f.x);
            if (dist < 1e-6f || Mathf.Atan2(lat, along) <= halfAngleRad) return dist <= depth ? 0f : dist - depth;
            Vector2 left = Rot(f, halfAngleRad), right = Rot(f, -halfAngleRad);
            return Mathf.Min(Vol2ShapeGeometry.SegmentDistance(c, Vector2.zero, left * depth), Vol2ShapeGeometry.SegmentDistance(c, Vector2.zero, right * depth));
        }
        static Vector2 Rot(Vector2 v, float rad) { float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad); return new Vector2(v.x * cs - v.y * sn, v.x * sn + v.y * cs); }
    }

    /// <summary>One material. Mutated only through the registry.</summary>
    public sealed class Vol2Material
    {
        public MaterialId Id { get; internal set; }
        public CastId Cast { get; internal set; }
        public char Letter { get; internal set; }
        public Body Body { get; internal set; }
        public Vol2MaterialKind Kind { get; internal set; }
        public long OwnerId { get; internal set; }
        public long FactionId { get; internal set; }
        public long CreatedUs { get; internal set; }
        public long ExpiresUs { get; internal set; }
        /// <summary>What it held when made / what it holds now (action units).</summary>
        public decimal Initial { get; internal set; }
        public decimal Remaining { get; internal set; }
        public decimal Drawn { get; internal set; }
        public int Depth { get; internal set; }
        public Vol2Footprint Footprint { get; internal set; }
        /// <summary>true = the amount is a pool a touch / tick / reaction draws from (a residue). false = the amount was ALREADY paid to a target
        /// when the material was made (a spread mark: it is reaction fuel and a 3 s outline, and is never a second damage draw).</summary>
        public bool PaysPool { get; internal set; }
        public bool LifeExtended { get; internal set; }
        public int Splits { get; internal set; }
        public bool GateDoor { get; internal set; }
        public string Tag { get; internal set; } = "";
        /// <summary>S5-b slice 8: counts every change the registry makes to this material (draw, split, move, extend, thicken, wet, end). The reaction engine reserves a contact by the
        /// revisions it read; a changed revision means another commit got there first and the contact is read again.</summary>
        public int Revision { get; internal set; } = 1;
        /// <summary>S5-b slice 8: the casts this material descends from, besides its own (a reaction child carries both parents'). The rules refuse a reaction between two materials
        /// that share any cast. null = only its own cast.</summary>
        public HashSet<long> Ancestors { get; internal set; }
        /// <summary>S5-b slice 8: a thickened (비화) material cannot be thickened again.</summary>
        public bool Thickened { get; internal set; }
        /// <summary>S5-b slice 8 (open seam of slice 7): "wet" - set by 옹 / 웅 / 엉 on my materials. Presentation and later rules read it; no reaction rule depends on it.</summary>
        public bool Wet { get; internal set; }
        public long WetUntilUs { get; internal set; }
        /// <summary>S5-b slice 8: last time the material was moved (앙). A material moved since the reaction engine last looked is a MOVING body for the rules.</summary>
        public long MovedUs { get; internal set; } = -1;
        /// <summary>The registry's move counter at the last move (0 = never moved). The reaction engine remembers the counter it has seen: a material with a higher one is moving NOW.</summary>
        public long MovedSerial { get; internal set; }
        /// <summary>S5-b slice 8: where it came from - "cast" (a vol.2 cast), "world" (pre-placed field material), "reaction" (a child of a reaction), "split" (half of a split).</summary>
        public string Origin { get; internal set; } = "cast";
        /// <summary>S5-b slice 8 (design section 10, body-attached materials): a material on the caster's body reacts through NORMAL CONTACT only (a hostile shape that touches the host);
        /// an enemy caster must not CHOOSE it as a target. Two separate eligibilities: this is the second (see Vol2ReactionEligibility).</summary>
        public bool SelectableByEnemyCaster => Alive && Remaining > 0m && !(Attached && FactionId == Vol2Faction.Player);
        public Vol2MaterialEnd End { get; internal set; }
        public long EndedUs { get; internal set; }
        public bool Alive => End == Vol2MaterialEnd.None;
        public long LifeUs => ExpiresUs - CreatedUs;
        public bool Attached => Footprint != null && Footprint.Kind == Vol2FootprintKind.Attached;
        public override string ToString() => Id + "[" + Letter + " " + Kind + " " + (Alive ? "alive" : End.ToString()) + " rem " + Remaining.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
    }

    public sealed class Vol2MaterialRegistry
    {
        readonly IVol2Clock _clock;
        readonly Vol2IdIssuer _ids;
        readonly Dictionary<long, Vol2Material> _all = new Dictionary<long, Vol2Material>();
        readonly List<Vol2Material> _alive = new List<Vol2Material>();
        readonly Queue<long> _endedOrder = new Queue<long>();
        const int KeepEnded = 1024;

        public event Action<Vol2Material> Created;
        /// <summary>what = "drawn" | "split" | "moved" | "extended".</summary>
        public event Action<Vol2Material, string> Changed;
        public event Action<Vol2Material, Vol2MaterialEnd> Ended;

        public int AliveCount => _alive.Count;
        public long TotalCreated { get; private set; }
        /// <summary>Counts every TryMove (see Vol2Material.MovedSerial).</summary>
        public long MoveSerial { get; private set; }

        public Vol2MaterialRegistry(IVol2Clock clock, Vol2IdIssuer ids)
        { _clock = clock ?? throw new ArgumentNullException(nameof(clock)); _ids = ids ?? throw new ArgumentNullException(nameof(ids)); }

        public IReadOnlyList<Vol2Material> Alive => _alive;
        public List<Vol2Material> AliveSnapshot() => new List<Vol2Material>(_alive);
        public bool TryGet(MaterialId id, out Vol2Material m) => _all.TryGetValue(id.Value, out m);

        public List<Vol2Material> ByCast(CastId cast)
        {
            var l = new List<Vol2Material>();
            foreach (var m in _all.Values) if (m.Cast == cast) l.Add(m);
            l.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
            return l;
        }

        /// <summary>Makes a material. <paramref name="id"/> 0 = a fresh MaterialId. The expiry is half-open: the material is gone AT created + life.</summary>
        public Vol2Material Create(CastId cast, char letter, Body body, Vol2MaterialKind kind, decimal amount, long lifeUs, Vol2Footprint footprint,
            long ownerId, long factionId, bool paysPool, long nowUs, int depth = 0, long id = 0, string tag = "", bool gateDoor = false,
            IEnumerable<long> ancestors = null, string origin = "cast")
        {
            if (!cast.IsValid) throw new ArgumentException("CastId required");
            if (amount < 0m || lifeUs <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            var mid = id > 0 ? new MaterialId(id) : _ids.NextMaterial();
            if (_all.ContainsKey(mid.Value)) throw new InvalidOperationException("MaterialId already used: " + mid);
            var m = new Vol2Material
            {
                Id = mid, Cast = cast, Letter = letter, Body = body, Kind = kind, OwnerId = ownerId, FactionId = factionId, CreatedUs = nowUs, ExpiresUs = checked(nowUs + lifeUs),
                Initial = amount, Remaining = amount, Depth = depth, Footprint = footprint ?? Vol2Footprint.Attached(Vector3.zero), PaysPool = paysPool, Tag = tag ?? "", GateDoor = gateDoor,
                Origin = origin ?? "cast"
            };
            if (ancestors != null) { var set = new HashSet<long>(ancestors); set.Remove(cast.Value); if (set.Count > 0) m.Ancestors = set; }
            _all.Add(mid.Value, m); _alive.Add(m); TotalCreated++;
            Created?.Invoke(m);
            return m;
        }

        /// <summary>One draw from a material's pool (a touch, a tick, a reaction). Takes min(amount, remaining); a material drawn to zero is consumed
        /// (end reason Consumed). Refused (false, taken 0) when the material is gone, not yet / no longer in its life, or the amount is not positive.</summary>
        public bool TryDebit(MaterialId id, decimal amount, long nowUs, out decimal taken)
        {
            taken = 0m;
            if (amount <= 0m || !_all.TryGetValue(id.Value, out var m) || !m.Alive || nowUs < m.CreatedUs || nowUs >= m.ExpiresUs || m.Remaining <= 0m) return false;
            taken = Math.Min(amount, m.Remaining);
            m.Remaining -= taken; m.Drawn += taken; m.Revision++;
            Changed?.Invoke(m, "drawn");
            if (m.Remaining <= 0m) End(m, Vol2MaterialEnd.Consumed, nowUs);
            return true;
        }

        /// <summary>Splits a material into <paramref name="parts"/> equal parts (가른다, slice 6): the parent KEEPS its id, life and lineage and
        /// holds one part; the others are new materials of the same cast with the same expiry. The parts add up to exactly what it held.
        /// Refused when it is gone or holds nothing.</summary>
        public bool TrySplit(MaterialId id, int parts, long nowUs, out List<Vol2Material> children)
        {
            children = null;
            if (parts < 2 || !_all.TryGetValue(id.Value, out var m) || !m.Alive || m.Remaining <= 0m || nowUs >= m.ExpiresUs) return false;
            decimal before = m.Remaining, each = Math.Round(before / parts, 12);
            children = new List<Vol2Material>();
            m.Remaining = each; m.Splits++; m.Revision++;
            for (int i = 1; i < parts; i++)
            {
                decimal part = i == parts - 1 ? before - each * (parts - 1) : each;
                var c = new Vol2Material
                {
                    Id = _ids.NextMaterial(), Cast = m.Cast, Letter = m.Letter, Body = m.Body, Kind = m.Kind, OwnerId = m.OwnerId, FactionId = m.FactionId, CreatedUs = m.CreatedUs, ExpiresUs = m.ExpiresUs,
                    Initial = part, Remaining = part, Depth = m.Depth, Footprint = m.Footprint, PaysPool = m.PaysPool, LifeExtended = m.LifeExtended, Splits = m.Splits, Tag = m.Tag,
                    Ancestors = m.Ancestors, Thickened = m.Thickened, Wet = m.Wet, WetUntilUs = m.WetUntilUs, Origin = "split"      // slice 8: a half is a material the reaction rules read like any other
                };
                _all.Add(c.Id.Value, c); _alive.Add(c); TotalCreated++; children.Add(c);
            }
            Changed?.Invoke(m, "split");
            foreach (var c in children) Created?.Invoke(c);
            return true;
        }

        /// <summary>Moves a material (앙 carries one to 2 m ahead): same id, same remaining, same life; only where it is changes.</summary>
        public bool TryMove(MaterialId id, Vol2Footprint footprint, long nowUs)
        {
            if (footprint == null || !_all.TryGetValue(id.Value, out var m) || !m.Alive || nowUs >= m.ExpiresUs) return false;
            m.Footprint = footprint; m.MovedUs = nowUs; m.MovedSerial = ++MoveSerial; m.Revision++;
            Changed?.Invoke(m, "moved");
            return true;
        }

        /// <summary>S5-b slice 8, 비화 (same body meets same body): the contacted material is made THICKER - it holds the rules' new amount and its life is renewed from now.
        /// The same material, not a new one: id, owner and lineage stay. A thickened material is never thickened again. Raises Initial if the new amount is above what it
        /// was made with, so the audit (nothing drawn beyond what it held) still holds.</summary>
        public bool TryThicken(MaterialId id, decimal newRemaining, long newExpiresUs, long nowUs)
        {
            if (newRemaining <= 0m || !_all.TryGetValue(id.Value, out var m) || !m.Alive || m.Thickened || nowUs >= m.ExpiresUs) return false;
            m.Remaining = newRemaining; m.ExpiresUs = Math.Max(m.ExpiresUs, newExpiresUs); m.Thickened = true; m.Revision++;
            if (m.Remaining + m.Drawn > m.Initial) m.Initial = m.Remaining + m.Drawn;
            Changed?.Invoke(m, "thickened");
            return true;
        }

        /// <summary>S5-b slice 8, 막힘 (the contacted material holds half): the remaining amount is SET (not drawn: nothing was paid to a body).</summary>
        public bool TrySetRemaining(MaterialId id, decimal remaining, long nowUs)
        {
            if (remaining <= 0m || !_all.TryGetValue(id.Value, out var m) || !m.Alive || nowUs >= m.ExpiresUs || remaining > m.Remaining) return false;
            m.Remaining = remaining; m.Revision++;
            Changed?.Invoke(m, "blocked");
            return true;
        }

        /// <summary>S5-b slice 8 (open seam of slice 7): marks a material wet until <paramref name="untilUs"/> (a flag, not a rule). False if it is gone.</summary>
        public bool TrySetWet(MaterialId id, long untilUs, long nowUs)
        {
            if (!_all.TryGetValue(id.Value, out var m) || !m.Alive || nowUs >= m.ExpiresUs) return false;
            m.Wet = true; m.WetUntilUs = untilUs; m.Revision++;
            Changed?.Invoke(m, "wet");
            return true;
        }

        /// <summary>Stretches a material's whole life by <paramref name="factor"/> (국: x1.5), ONCE: a second extension is refused, so it never stacks.
        /// The material is updated, not remade: its remaining, its id and its lineage stay. extraUs = the life it gained.</summary>
        public bool TryExtend(MaterialId id, decimal factor, long nowUs, out long extraUs)
        {
            extraUs = 0;
            if (factor <= 1m || !_all.TryGetValue(id.Value, out var m) || !m.Alive || m.LifeExtended || nowUs >= m.ExpiresUs) return false;
            long life = m.ExpiresUs - m.CreatedUs;
            long grown = checked((long)(life * factor));
            extraUs = grown - life;
            m.ExpiresUs = checked(m.CreatedUs + grown); m.LifeExtended = true; m.Revision++;
            Changed?.Invoke(m, "extended");
            return true;
        }

        /// <summary>Ends a material for a stated reason (consumed by a child, replaced, cancelled by a hit, cleansed ...). False if it was already gone.</summary>
        public bool TryEnd(MaterialId id, Vol2MaterialEnd reason, long nowUs)
        {
            if (reason == Vol2MaterialEnd.None) throw new ArgumentException("an end needs a reason");
            if (!_all.TryGetValue(id.Value, out var m) || !m.Alive) return false;
            End(m, reason, nowUs);
            return true;
        }

        /// <summary>Natural expiry: every material whose life is over at <paramref name="nowUs"/> ends with Expired. Returns how many.</summary>
        public int Sweep(long nowUs)
        {
            int n = 0;
            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                var m = _alive[i];
                if (nowUs >= m.ExpiresUs) { End(m, Vol2MaterialEnd.Expired, nowUs); n++; }
            }
            return n;
        }

        /// <summary>Scope reset / lab: every material ends with <paramref name="reason"/> (default Aborted).</summary>
        public int EndAll(long nowUs, Vol2MaterialEnd reason = Vol2MaterialEnd.Aborted)
        {
            int n = 0;
            for (int i = _alive.Count - 1; i >= 0; i--) { End(_alive[i], reason, nowUs); n++; }
            return n;
        }

        public void Clear() { _all.Clear(); _alive.Clear(); _endedOrder.Clear(); }

        void End(Vol2Material m, Vol2MaterialEnd reason, long nowUs)
        {
            m.End = reason; m.EndedUs = nowUs; m.Revision++;
            _alive.Remove(m);
            _endedOrder.Enqueue(m.Id.Value);
            while (_endedOrder.Count > KeepEnded) { long old = _endedOrder.Dequeue(); if (_all.TryGetValue(old, out var o) && !o.Alive) _all.Remove(old); }
            Ended?.Invoke(m, reason);
        }

        /// <summary>The invariants a fixture asserts: nothing negative, nothing drawn beyond what it held, an ended material is not in the alive list,
        /// an alive one has no end reason.</summary>
        public List<string> Audit()
        {
            var problems = new List<string>();
            foreach (var m in _all.Values)
            {
                if (m.Remaining < 0m) problems.Add(m.Id + " negative remaining");
                if (m.PaysPool && m.Drawn > m.Initial) problems.Add(m.Id + " drew more than it held");
                if (m.Alive != _alive.Contains(m)) problems.Add(m.Id + " alive list mismatch");
                if (m.ExpiresUs < m.CreatedUs) problems.Add(m.Id + " expires before it was made");
            }
            return problems;
        }
    }
}
