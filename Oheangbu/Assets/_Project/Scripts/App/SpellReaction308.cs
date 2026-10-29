using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // #308 D308-128 21 / D308-132 step 6: what a presenter is told when a five-element reaction happens. The cue carries a KIND
    // (ReactionKind308), not a table row: the legacy reaction handler (ReactionZoneEffect308, rows of Reactions308.csv) and the
    // vol.2 adapter (ReactionRulesV3 results, S5-b) raise the same call and the presenter draws the same picture.
    //   ISpellReactionPresenter308.React(args)   the product look: Kind, Point = the product's centre, Value = its radius,
    //                                           FromRadius = the radius the material had (the look grows to Value in .4 s),
    //                                           Direction = the flat way the wave ran (caster -> beyond), Life = seconds it stands
    //   SpellFxCue.React (on the material's own handle)   the material's floor is consumed (Reacted end): it pales out, its
    //                                           optional "React" child folds along Direction. Never a Break splash.
    // Presentation only: rules never read anything back.
    public static class SpellReaction308
    {
        // Which kind a table row stands for: by its id first (wildfire / steam / mud), else by what it leaves behind.
        public static ReactionKind308 KindOf(ReactionRow308 row)
        {
            if (row == null) return ReactionKind308.None;
            switch ((row.Id ?? "").ToLowerInvariant())
            {
                case "wildfire": return ReactionKind308.Wildfire;
                case "steam": return ReactionKind308.Steam;
                case "mud": return ReactionKind308.Mud;
            }
            if (row.Kind == ReactionProductKind308.Damage && row.Product == Element.Fire) return ReactionKind308.Wildfire;
            if (row.Kind == ReactionProductKind308.None && row.Product == Element.Water) return ReactionKind308.Steam;
            if (row.Kind == ReactionProductKind308.Slow && row.Product == Element.Earth) return ReactionKind308.Mud;
            return ReactionKind308.None;
        }

        // The flat unit way from `from` to `to`; `fallback` (itself flattened, forward if it is zero) when they coincide.
        public static Vector3 FlatDirection(Vector3 from, Vector3 to, Vector3 fallback = default)
        {
            Vector3 d = to - from; d.y = 0f;
            if (d.sqrMagnitude > 1e-6f) return d.normalized;
            fallback.y = 0f;
            return fallback.sqrMagnitude > 1e-6f ? fallback.normalized : Vector3.forward;
        }

        public static SpellFxCueArgs Args(ReactionKind308 kind, Vector3 centre, Vector3 direction, float fromRadius, float radius, float life)
            => new SpellFxCueArgs { Kind = kind, Point = centre, Direction = FlatDirection(Vector3.zero, direction), FromRadius = fromRadius, Value = radius, Life = life };
    }
}
