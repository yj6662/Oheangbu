using System;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    // D308-153 (SPEC-SPELL-FX-RESTART-308): the restart plumbing of the vol.2 spell look. These are the plain data and the part contract the director hands to a catalogue
    // prefab. Nothing here decides anything of a rule: the executors own judgement, damage, range and lifetime; the look only reads their events on the scope clock.

    /// <summary>Why a prefab's cast is over for the look.</summary>
    public enum SpellFxEndReason
    {
        /// <summary>The executor raised CastEnded (the cast finished its own life).</summary>
        Done,
        /// <summary>The lab reset / a scope reset stopped the cast (status Aborted) or the tool cleared the stage: gone at once, no tail.</summary>
        Aborted,
        /// <summary>The hard cap (15 s) ran out before the executor ended the cast.</summary>
        TimedOut,
    }

    /// <summary>One cast as the prefab sees it. Built by the director at CastBegan; the live fields (EndUs) follow the executor.</summary>
    public sealed class SpellFxCast
    {
        public long CastId;
        public char Letter;
        public Body Body;
        public Frame Frame;
        public Verb Verb;
        /// <summary>The catalogue variant this prefab was picked for ("A", "B", ...).</summary>
        public string Variant = "A";
        public Vol2ShapeSpec Spec;
        public Vector3 Origin, Forward, Right;
        /// <summary>The characteristic reach in metres: ring centre-line radius, fan depth, path length, else the brush reach.</summary>
        public float Radius;
        /// <summary>Scope clock (microseconds): the commit, and the moment the shape actually leaves = max(StartUs, Emitter.StartUs).</summary>
        public long StartUs, LaunchUs;
        /// <summary>-1 while the executor has not ended the cast.</summary>
        public long EndUs = -1;
        /// <summary>D308-156: the scope-clock microsecond the cast's ward stands until (-1 = no ward). A ward's look reads its life from here, not from its own number.</summary>
        public long WardUntilUs = -1;
        public bool HasWard => WardUntilUs >= 0;
        /// <summary>Seconds from the launch to the ward's end (0 = no ward).</summary>
        public float WardSeconds => HasWard ? Mathf.Max(0f, (float)((WardUntilUs - LaunchUs) / 1000000.0)) : 0f;
        public bool Ended => EndUs >= 0;
        public Vol2CastLog Log;
        public IVol2Clock Clock;
        /// <summary>Where the great brush's tip / grip are now (null when the scene has no brush); looked up lazily, never required.</summary>
        public Func<Vector3?> BrushTip, BrushGrip;

        /// <summary>Seconds since the launch at <paramref name="nowUs"/> (negative before it).</summary>
        public float AgeAt(long nowUs) => (float)((nowUs - LaunchUs) / 1000000.0);
        public float Age => Clock != null ? AgeAt(Clock.NowUs) : 0f;
        public string Id => Letter + "/" + Variant + "#" + CastId;

        public static float RadiusOf(Vol2ShapeSpec spec, Frame frame)
        {
            if (spec == null) return 0f;
            switch (frame)
            {
                case Frame.Ring: return spec.RingRadius;
                case Frame.Fan: return spec.FanDepth;
                case Frame.Path: return spec.PathLength;
                default: return spec.BrushReach;
            }
        }
    }

    /// <summary>One budget-approved contact of the cast. Position is the body's centre (a collider centre, else 0.9 m above the feet); Feet is its ground point.</summary>
    public struct SpellFxHit
    {
        public Vector3 Position, Feet;
        public long NowUs, TargetId;
        /// <summary>The executor's contact kind: Touch | Instant | Second | Residue | Spread | ReturnSpread.</summary>
        public string Kind;
        /// <summary>The struck body's transform (null for a synthetic body or one already gone).</summary>
        public Transform Target;
        public float TargetRadius;
        /// <summary>Metres along the shape's axis where it met the body (the executor's own number).</summary>
        public float Along;
    }

    public enum SpellFxMaterialPhase { Created, Ended }

    /// <summary>A material of the cast (residue, mark, carrier) appearing or ending. Position is the footprint's centre.</summary>
    public struct SpellFxMaterial
    {
        public SpellFxMaterialPhase Phase;
        public long Id, NowUs;
        public Vol2MaterialKind Kind;
        public Body Body;
        public char Letter;
        public Vector3 Position;
        /// <summary>Disc radius / fan depth / ring outer radius of the footprint; 0 for a material on the caster's body.</summary>
        public float Radius;
        /// <summary>Why it ended (Phase Ended only).</summary>
        public Vol2MaterialEnd End;
        public bool Attached;
    }

    /// <summary>A committed reaction that involves this cast (as the acting or the contacted side).</summary>
    public struct SpellFxReaction
    {
        public long EventId, NowUs;
        public Body ActingBody, ContactedBody;
        public string Relation, Product;
        public Vector3 Point;
        /// <summary>true = this cast is the acting side, false = the contacted side.</summary>
        public bool ActingSide;
    }

    /// <summary>A piece of a catalogue prefab that wants the cast's events (a VFX Graph, a flipbook, a mesh animation ...). The player finds every implementer under its
    /// root and relays the same calls. Every method has an empty default: implement only what the part needs. Exceptions are caught per call by the player.</summary>
    public interface ISpellFxPart
    {
        /// <summary>At the cast's begin, BEFORE the prefab is active (it is built under an inactive holder): hold anything that must not start by itself.</summary>
        void OnBegin(SpellFxCast cast) { }
        /// <summary>Once, the first frame the scope clock reaches the launch.</summary>
        void OnLaunch(SpellFxCast cast) { }
        /// <summary>Every director step from the launch on; <paramref name="ageSeconds"/> is the scope-clock age since the launch.</summary>
        void OnTick(SpellFxCast cast, float ageSeconds) { }
        void OnHit(SpellFxCast cast, SpellFxHit hit) { }
        void OnMaterial(SpellFxCast cast, SpellFxMaterial material) { }
        void OnReaction(SpellFxCast cast, SpellFxReaction reaction) { }
        /// <summary>D308-156: the cast's ward shot an enemy shot down at <paramref name="point"/> (where the shot met the ward's band, at the shot's height).</summary>
        void OnIntercept(SpellFxCast cast, Vector3 point) { }
        /// <summary>D308-156: the cast's ward stopped a melee hit on the caster; <paramref name="point"/> is the ward's wall toward the nearest enemy, chest high.</summary>
        void OnBlock(SpellFxCast cast, Vector3 point) { }
        void OnEnd(SpellFxCast cast, SpellFxEndReason reason) { }
    }
}
