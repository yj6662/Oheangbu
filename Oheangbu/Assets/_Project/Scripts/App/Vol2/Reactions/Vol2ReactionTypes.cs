using System;
using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;
using R = Oheangbu.Vol2.Rules.ReactionRulesV3;

namespace Oheangbu.App
{
    /// <summary>S5-b slice 8 (design section 10): what the presentation gets when a reaction has been COMMITTED - never before. It carries the relation, the acting body, the contacted body
    /// and why each side ended; the three legacy looks (들불 / 김 / 진창) are a conversion at the boundary (<see cref="Legacy"/>), the other 22 cells have no legacy look and the
    /// presenter reads the fields. Plain data; it never flows back into a rule.</summary>
    public sealed class Vol2ReactionVisual
    {
        public long EventId, AtUs;
        public R.Kind Relation;
        public R.Product Product;
        public Body ActingBody, ContactedBody;
        /// <summary>The acting side: a material id, or the rule id of a moving shape (ActingIsShape).</summary>
        public long ActingId, ContactId;
        public bool ActingIsShape;
        public CastId ActingCast, ContactCast;
        /// <summary>The owner and faction the product takes (the acting side's).</summary>
        public long OwnerId, FactionId;
        /// <summary>None = the side stays (the contacted side of a blocked or thickened reaction).</summary>
        public Vol2MaterialEnd ActingEnd, ContactEnd;
        /// <summary>A short why-text for the log: "Consumed", "ChildConsumed(dried 0.3 s)", "Blocked(half)", "Thickened".</summary>
        public string ActingReason = "", ContactReason = "";
        public long ChildId; public Body ChildBody; public decimal ChildAmount; public long ChildLifeUs; public int ChildDepth;
        public long DryUs, DurationUs, OcclusionUs;
        public decimal DamageBudget;
        public int AffectedBodies;
        public Vector3 Point;
        public Vol2Footprint Where;
        public bool Returning;
        public ReactionKind308 Legacy;
        /// <summary>진창 has no prefab yet (TODO: Mud look, Vfx308Build.Reaction); the signal is emitted and the presenter falls back to the plain mark.</summary>
        public bool MudPrefabMissing => Legacy == ReactionKind308.Mud;
        public override string ToString() => "#" + EventId + " " + ActingBody + ">" + ContactedBody + " " + Relation + "/" + Product + (ChildId > 0 ? " child " + ChildBody + " M" + ChildId : "") + " @" + AtUs;

        /// <summary>Only three of the 25 cells have an old look: 화가 목에 옮겨붙음 (들불), 수가 화를 꺾음 (김), 토가 수를 꺾음 (진창).</summary>
        public static ReactionKind308 LegacyOf(R.Kind relation, Body acting, Body contacted)
        {
            if (relation == R.Kind.Transfer && acting == Body.Fire && contacted == Body.Wood) return ReactionKind308.Wildfire;
            if (relation == R.Kind.Break && acting == Body.Water && contacted == Body.Fire) return ReactionKind308.Steam;
            if (relation == R.Kind.Break && acting == Body.Earth && contacted == Body.Water) return ReactionKind308.Mud;
            return ReactionKind308.None;
        }
    }

    /// <summary>One line of the engine's ledger: a committed reaction, with what the three ledger checks of design section 10 need (the casts of both sides, their depth, the
    /// consumed ids, the child) and the proof that a reaction's damage gave no groggy and no seed trigger.</summary>
    public sealed class Vol2ReactionRecord
    {
        public long EventId, AtUs;
        public long ActingId, ContactId;
        public bool ActingIsShape;
        public CastId ActingCast, ContactCast;
        public int ActingDepth, ContactDepth;
        public long ActingOwner, ContactOwner, ActingFaction, ContactFaction;
        public Body ActingBody, ContactedBody;
        public R.Kind Relation; public R.Product Product;
        public bool Returning;
        public readonly List<long> Consumed = new List<long>();
        public readonly List<long> ChildConsumed = new List<long>();
        public long ChildId; public int ChildDepth; public long ChildOwner, ChildFaction; public decimal ChildAmount;
        public decimal ParentSum;
        public decimal DamageBudget; public int Affected;
        public readonly List<long> AffectedIds = new List<long>();
        public decimal UnitsGiven; public float HpApplied; public int Hits;
        public int SlowsApplied;
        /// <summary>Always 0: a reaction's damage gives no groggy / posture (it never reaches the posture bridge).</summary>
        public decimal Groggy => 0m;
        /// <summary>Always 0: a reaction's damage is never announced as a contact of a cast, so no seed hears it.</summary>
        public int SeedTriggers => 0;
        /// <summary>The acting side was a moving shape (not a standing material).</summary>
        public bool FromShape;
        public bool Revalidated;
        public override string ToString() => "#" + EventId + " " + ActingBody + ">" + ContactedBody + " " + Relation + "/" + Product + " [" + ActingId + "x" + ContactId + "]";
    }

    /// <summary>One try of the engine, committed or not: the rejected ones (and why) are kept for the ledger audit and the lab.</summary>
    public readonly struct Vol2ReactionAttempt
    {
        public readonly long EventId, AtUs, FirstId, SecondId;
        public readonly bool Accepted;
        public readonly string Reason;
        public Vol2ReactionAttempt(long eventId, long atUs, long firstId, long secondId, bool accepted, string reason)
        { EventId = eventId; AtUs = atUs; FirstId = firstId; SecondId = secondId; Accepted = accepted; Reason = reason ?? ""; }
        public override string ToString() => "#" + EventId + " " + FirstId + "x" + SecondId + " " + (Accepted ? "OK " : "NO ") + Reason;
    }

    /// <summary>A moving shape as the acting side of a reaction (TE-S8-1): not a material, with a reaction balance of its own. One per cast. The engine makes them for the casts the
    /// coordinator runs; a fixture or an adapter (an enemy projectile, a returning leg) makes its own and submits it.</summary>
    public sealed class Vol2ReactionShape
    {
        public CastId Cast;
        public Glyph Glyph;
        public Body Body;
        public long OwnerId = Vol2Faction.Player, FactionId = Vol2Faction.Player;
        public Vol2ShapeKind Kind;
        public Vol2ShapeSpec Spec;
        public long StartUs, ExpiresUs;
        /// <summary>The balance it brings (> 0) and the direct-area budget the rules cap its damage against.</summary>
        public decimal Balance = 1m, DirectBudget = 1m;
        public bool Consumed, Finished, Stepped;
        public float PrevAlong;
        public int Depth;
        /// <summary>The rule id: far from any MaterialId.</summary>
        public long RuleId => Vol2ReactionEngine.ShapeIdBase + Cast.Value;
        public override string ToString() => "shape " + Cast + " " + Body + (Consumed ? " spent" : "");
    }

    /// <summary>Who may be one side of a contact (design section 10: "몸에 붙은 재료는 일반 접촉 반응 자격과 적 술사 선택 자격을 구분한다"). Two separate questions:
    /// NormalContact = may the material take part in a contact at all; EnemyCasterSelects = may an ENEMY CASTER pick it as the thing it reacts with.
    /// A player's body-attached material reacts when a hostile shape touches the host (normal contact), but no enemy caster may choose it.</summary>
    public static class Vol2ReactionEligibility
    {
        public static bool NormalContact(Vol2Material m, long nowUs)
            => m != null && m.Alive && nowUs >= m.CreatedUs && nowUs < m.ExpiresUs && m.Remaining > 0m && m.Letter != '걱' && m.Footprint != null;

        public static bool EnemyCasterSelects(Vol2Material m, long nowUs) => NormalContact(m, nowUs) && m.SelectableByEnemyCaster;

        /// <summary>The materials an enemy caster of <paramref name="casterFaction"/> may choose: normal contact, not a player's body-attached one, not its own side's.</summary>
        public static List<Vol2Material> EnemyCasterChoices(Vol2MaterialRegistry materials, long casterFaction, long nowUs)
        {
            var list = new List<Vol2Material>();
            foreach (var m in materials.Alive) if (m.FactionId != casterFaction && EnemyCasterSelects(m, nowUs)) list.Add(m);
            list.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
            return list;
        }

        /// <summary>A body-attached material is touched through the host only by a hostile shape.</summary>
        public static bool HostTouchedBy(Vol2Material attached, long shapeFaction) => attached != null && attached.Attached && attached.FactionId != shapeFaction;
    }
}
