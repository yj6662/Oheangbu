using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // #308 presentation seam (SPEC-SPELL-120-308 section 9). Effect code says what happened in rule terms; a presenter
    // decides what is drawn. Rules never read anything back from a presenter, so swapping it cannot change a result.
    // Today's implementation is Vfx120SpellPresenter308 (catalogue prefabs, interim wiring); the registration is the one
    // line in SpellPresentationInstaller308, which is the line SPEC-SPELL-DEPLOY-308 replaces or wraps.

    public enum SpellFxRole { Cast, Projectile, Attached, Zone, Aura, Mark, Ward, Summon, Field, Companion }

    public enum SpellFxCue { Hit, Miss, Tick, Secondary, Split, Return, Detonate, Intercept, Consume, Break, Expire, TargetDefeated, Anchor, React }

    // #308 D308-128 21 / D308-132: what a five-element reaction looks like is keyed by this kind, not by a table row, so the legacy
    // reaction handler and the vol.2 adapter raise the same call (SpellReaction308 builds the args, ISpellReactionPresenter308 takes them).
    // Mud is a recognised kind whose look is not built yet.
    public enum ReactionKind308 { None, Wildfire, Steam, Mud }

    public struct SpellFxRequest
    {
        public char Letter;
        public SpellFxRole Role;
        public Element Element;
        public Vector3 Origin, FallbackPoint;
        public Transform Target, Follow;
        public float ImpactClock;     // seconds from Begin to the first judgement (0 = none)
        public float Duration;        // seconds the presentation should live (0 = its own default)
        public float Radius, Height;
        public float Grade01;         // SpellCast.Brush01: read-only grade of the drawn letter
        public AreaImpactPlan Area;   // the judgement plan the rule made (null = none)
        public long AttackId;
    }

    public struct SpellFxCueArgs
    {
        public Transform Target;
        public Transform[] Targets;
        public Vector3 Point, Normal;
        public float At, Value;
        public int Index;
        public bool Finish;   // D308-127 debate 15: the last strike of a combo (Index = its step)
        // D308-132 step 6 (React cue on the consumed zone's handle; ISpellReactionPresenter308.React for the product's look):
        // Kind = the reaction, Direction = the flat way the wave ran (caster -> beyond), Point = centre, Value = the product's
        // radius, FromRadius = the radius the material had, Life = seconds the product stands.
        public ReactionKind308 Kind;
        public Vector3 Direction;
        public float Life, FromRadius;
    }

    // Id 0 = nothing is shown. Rules never depend on it.
    public readonly struct SpellFxHandle
    {
        public readonly int Id;
        public SpellFxHandle(int id) { Id = id; }
        public bool Shown => Id != 0;
    }

    public interface ISpellPresenter
    {
        SpellFxHandle Begin(in SpellFxRequest request);
        void Cue(SpellFxHandle handle, SpellFxCue cue, in SpellFxCueArgs args);
        void End(SpellFxHandle handle, bool immediate = false);
        void EndAll();
    }

    // #308 D308-132 step 6: optional, found with `as` like ISpellBurstPresenter308 (the guard forwards it after the rules of the cast
    // are in). Draws a reaction's product where the material stood. Rules never read anything back.
    public interface ISpellReactionPresenter308
    {
        void React(in SpellFxCueArgs args);
    }

    // The wiring hands scene-side lookups to a presenter that wants them (the catalogue visual set, a parent for instances).
    public interface ISpellPresenterBinding
    {
        void Bind(SpellVisualSetSO visuals, Transform owner);
    }

    // Draws nothing. Checks run with it: the rule result must be the same as with any other presenter.
    public sealed class NullSpellPresenter308 : ISpellPresenter
    {
        public int Begun { get; private set; }
        public int Cues { get; private set; }
        public int Ended { get; private set; }
        public SpellFxHandle Begin(in SpellFxRequest request) { Begun++; return default; }
        public void Cue(SpellFxHandle handle, SpellFxCue cue, in SpellFxCueArgs args) { Cues++; }
        public void End(SpellFxHandle handle, bool immediate = false) { Ended++; }
        public void EndAll() { }
    }
}
