using System;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;

namespace Pure308
{
    // #308 D308-124 (debate decision, TEST): inside the weak-point window a player spell whose element overcomes the enemy's
    // attack element hurts x1.25. The window, the spell sources and the enemy's element are read engine-side (EnemyVitals);
    // here only the rule.
    public static partial class Program
    {
        static partial void RunCounter124(Report r, Context c)
        {
            const float m = 1.25f;
            r.Check("상극-1", CounterElementRule308.Scale(true, true, Element.Metal, true, Element.Wood, m) == m &&
                CounterElementRule308.Scale(true, true, Element.Wood, true, Element.Earth, m) == m &&
                CounterElementRule308.Scale(true, true, Element.Earth, true, Element.Water, m) == m &&
                CounterElementRule308.Scale(true, true, Element.Water, true, Element.Fire, m) == m &&
                CounterElementRule308.Scale(true, true, Element.Fire, true, Element.Metal, m) == m,
                "the five overcoming pairs (metal-wood, wood-earth, earth-water, water-fire, fire-metal) are scaled");
            r.Check("상극-1", CounterElementRule308.Scale(true, true, Element.Wood, true, Element.Metal, m) == 1f &&
                CounterElementRule308.Scale(true, true, Element.Wood, true, Element.Wood, m) == 1f &&
                CounterElementRule308.Scale(true, true, Element.Fire, true, Element.Wood, m) == 1f,
                "being overcome, the same element or a generating pair is not scaled");
            r.Check("상극-2", CounterElementRule308.Scale(false, true, Element.Metal, true, Element.Wood, m) == 1f,
                "outside the weak-point window nothing is scaled");
            r.Check("상극-2", CounterElementRule308.Scale(true, false, Element.Metal, true, Element.Wood, m) == 1f,
                "a source that is not a player spell (summon, retaliation) is not scaled");
            r.Check("상극-3", CounterElementRule308.Scale(true, true, null, true, Element.Wood, m) == 1f &&
                CounterElementRule308.Scale(true, true, Element.Metal, false, Element.Wood, m) == 1f,
                "a neutral hit, or an enemy without an attack element, is not scaled");
            r.Check("상극-4", CounterElementRule308.Scale(true, true, Element.Metal, true, Element.Wood, float.NaN) == 1f &&
                CounterElementRule308.Scale(true, true, Element.Metal, true, Element.Wood, float.PositiveInfinity) == 1f &&
                CounterElementRule308.Scale(true, true, Element.Metal, true, Element.Wood, 0f) == 1f &&
                CounterElementRule308.Scale(true, true, Element.Metal, true, Element.Wood, -2f) == 1f,
                "a broken multiplier (not a number, infinite, zero, negative) falls back to 1, never to no damage");
            r.Check("상극-4", CounterElementRule308.Scale(true, true, Element.Metal, true, Element.Wood, 1f) == 1f,
                "a multiplier of 1 turns the rule off");
        }
    }
}