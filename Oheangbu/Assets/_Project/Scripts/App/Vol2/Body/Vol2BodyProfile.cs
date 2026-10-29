using System;
using Oheangbu.Core.Domain;
using Oheangbu.Vol2.Rules;

namespace Oheangbu.App
{
    /// <summary>What the body does besides its instant hit, for the 무받침 (한 번) glyphs. Read from the 한줄정의 / 몸거동 of the 20 rows:
    /// 목 붙든다 (Bind), 화 데운다 (Burn), 토 민다 (Push), 금 벤다 (Cut), 수 적신다 (Wet).</summary>
    public enum Vol2BodyActionKind { Bind, Burn, Push, Cut, Wet }

    /// <summary>The body's numbers, all read from NumbersV3 (body.*), nothing hard-coded. Immutable once built.
    /// Interpretation notes (the data names the values, the sentences name the actions):
    ///  - Bind: movement x MoveMultiplier for Duration (body.wood.moveMultiplier / duration). body.wood.utility (0.4) is the VALUE the bind is
    ///    priced at, it is not damage.
    ///  - Burn: the 지속 피해 is real damage. Its total is body.fire.utility (0.3 action units, the same unit as the instant) spread over
    ///    body.fire.duration. The tick cadence is not in the data (Vol2ShapeTuning.DotTickUs, TE-S3-3).
    ///  - Push: body.earth.utility is the value of the push; the distance is not in the data (TE-S3-4).
    ///  - Cut: the instant only, x body.wet.metalMultiplier against a wet body (the 젖은 적 x1.25 of the sentence). The faster path of
    ///    body.metal.speedUtility is a price, not a number the data gives a speed for.
    ///  - Wet: a mark for body.water.duration. A wet body takes fire x body.wet.fireMultiplier and metal x body.wet.metalMultiplier.
    ///    The spread of wetness between enemies is a later slice and is NOT built here.</summary>
    public sealed class Vol2BodyProfile
    {
        public Body Body { get; }
        public Element Element { get; }
        public Vol2BodyActionKind Action { get; }
        /// <summary>body.X.instant: the base hit in action units (H of the damage formula).</summary>
        public decimal Instant { get; }
        /// <summary>body.X.utility (metal: speedUtility), the priced value of the extra action.</summary>
        public decimal Utility { get; }
        public long DurationUs { get; }
        /// <summary>Bind: the movement multiplier. 1 for the others.</summary>
        public decimal MoveMultiplier { get; }
        /// <summary>Burn: the total of the lasting damage in action units. 0 for the others.</summary>
        public decimal BurnTotal { get; }
        public decimal WetFireMultiplier { get; }
        public decimal WetMetalMultiplier { get; }

        Vol2BodyProfile(Body body, NumbersV3Dto n)
        {
            Body = body; Element = ElementOf(body); Action = ActionOf(body);
            string k = "body." + RuleTypesV3.BodyKey(body) + ".";
            Instant = n.Get(k + "instant");
            Utility = body == Body.Metal ? n.Get(k + "speedUtility") : n.Get(k + "utility");
            DurationUs = body == Body.Wood || body == Body.Fire || body == Body.Water ? RuleTypesV3.Microseconds(n.Get(k + "duration")) : 0;
            MoveMultiplier = body == Body.Wood ? n.Get(k + "moveMultiplier") : 1m;
            BurnTotal = body == Body.Fire ? n.Get(k + "utility") : 0m;
            WetFireMultiplier = n.Get("body.wet.fireMultiplier");
            WetMetalMultiplier = n.Get("body.wet.metalMultiplier");
        }

        public static Vol2BodyProfile For(Body body, NumbersV3Dto numbers)
        {
            if (numbers == null || !numbers.IsValidated) throw new ArgumentException("validated numbers required");
            return new Vol2BodyProfile(body, numbers);
        }

        /// <summary>Explicit mapping. The enums happen to line up today; a cast would break silently the day one of them is reordered.</summary>
        public static Element ElementOf(Body body)
        {
            switch (body)
            {
                case Body.Wood: return Element.Wood;
                case Body.Fire: return Element.Fire;
                case Body.Earth: return Element.Earth;
                case Body.Metal: return Element.Metal;
                case Body.Water: return Element.Water;
                default: throw new ArgumentOutOfRangeException(nameof(body));
            }
        }

        public static Vol2BodyActionKind ActionOf(Body body)
        {
            switch (body)
            {
                case Body.Wood: return Vol2BodyActionKind.Bind;
                case Body.Fire: return Vol2BodyActionKind.Burn;
                case Body.Earth: return Vol2BodyActionKind.Push;
                case Body.Metal: return Vol2BodyActionKind.Cut;
                case Body.Water: return Vol2BodyActionKind.Wet;
                default: throw new ArgumentOutOfRangeException(nameof(body));
            }
        }

        /// <summary>The factor a wet target puts on THIS body's instant hit (fire x0.5, metal x1.25); 1 otherwise or when the target is dry.</summary>
        public decimal WetFactor(bool targetWet)
        {
            if (!targetWet) return 1m;
            return Body == Body.Fire ? WetFireMultiplier : Body == Body.Metal ? WetMetalMultiplier : 1m;
        }
    }
}
