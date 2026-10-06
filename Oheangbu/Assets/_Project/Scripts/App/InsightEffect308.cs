using System.Collections.Generic;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // WP-08 metal buff with the mieum final: the rule is the buff state alone (timer, Aura request, Expire cue). How the
    // enemy's element and its projectiles become easier to read while it runs belongs to the presentation layer, which sees
    // the same request and cue (Q8 default). No number of the fight changes.
    public sealed class InsightEffect308 : SelfBuffEffect308
    {
        public const string HandlerId = "buff.insight";
        static readonly SpellParamSpec[] Specs = { new SpellParamSpec("duration", .1f, 600f) };

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
    }
}
