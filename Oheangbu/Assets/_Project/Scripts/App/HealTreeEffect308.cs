using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // zone.tree (SPEC-SPELL-120-308 WP-06): a tree is planted at the caster's feet; the player regains HP while standing in
    // its circle. The tree goes when its life runs out or when it is struck (tree.hits strikes).
    // Struck = an enemy strike touches the trunk (tree.body). Two ways in:
    //   - Break(point, reach): the entry for anything that attacks the tree itself (enemy AI that targets it is not built);
    //   - a melee strike that really hit the player while its attacker stood within tree.reach of the trunk also counts as a
    //     strike on the tree (TEST reading, tree.reach = 0 switches it off). The hook only ever takes the tree away:
    //     it never heals and never gives ink (no reward for being hit).
    public sealed class HealTreeEffect308 : ZoneEffect308, IPlayerHitHook
    {
        public const string HandlerId = "zone.tree";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("tree.radius", 0.5f, 30f), new SpellParamSpec("tree.life", 0.5f, 300f),
            new SpellParamSpec("heal.hp01ps", 0f, 1f), new SpellParamSpec("tree.max", 1f, 8f),
            new SpellParamSpec("tree.body", 0.05f, 10f), new SpellParamSpec("tree.reach", 0f, 20f),
            new SpellParamSpec("tree.hits", 1f, 20f),
        };

        public HealTreeEffect308() : this(null) { }
        public HealTreeEffect308(SpellZoneRuntime308 zones) : base(zones) { }

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        public override bool Commit(in SpellCastContext ctx)
        {
            var host = ctx.Host; var cast = ctx.Cast; var row = ctx.Row;
            Vector3 centre = host.PlayerPosition;
            float life = row.F("tree.life", 0f), radius = row.F("tree.radius", 0f);
            var zone = new SpellZone308
            {
                Owner = HandlerId, Cast = Zones.NextCast(), Letter = cast.Letter, Element = cast.Element, Centre = centre, Radius = radius,
                From = ctx.Now, Until = ctx.Now + life, Life = life, Value = row.F("heal.hp01ps", 0f),
                Body = row.F("tree.body", 0f), Reach = row.F("tree.reach", 0f), HitsLeft = Mathf.RoundToInt(row.F("tree.hits", 1f)),
            };
            host.PresentationOwned();
            var handle = ctx.Fx.Begin(new SpellFxRequest
            {
                Letter = cast.Letter, Role = SpellFxRole.Zone, Element = cast.Element, Origin = centre, FallbackPoint = centre,
                Duration = life, Radius = radius, Grade01 = cast.Brush01,
            });
            zone.Fx = handle.Id;
            if (Place(zone, Mathf.RoundToInt(row.F("tree.max", 1f)), ctx.Now, ctx.Fx) != 0) return true;
            ctx.Fx.End(handle, true);
            return false;
        }

        // Something struck at point with this reach. Returns how many trees it broke; a broken tree stops healing at once.
        public int Break(Vector3 point, float reach) => Zones.Strike(HandlerId, point, reach);

        // An enemy melee strike landed on the player: a tree its attacker could reach is struck too. Retaliation-free and
        // reward-free: the only outcome is losing the tree.
        public void OnPlayerHit(in PlayerHitInfo hit, in SpellTickContext ctx, ref bool interruptsDrawing)
        {
            if (hit.Attacker == null) return;
            if (hit.Kind != IncomingDamageKind.Melee && hit.Kind != IncomingDamageKind.ElementalMelee) return;
            Zones.StrikeFrom(HandlerId, hit.Attacker.transform.position);
        }

        protected override void Act(in SpellTickContext ctx)
        {
            var player = ctx.Host != null ? ctx.Host.PlayerVitals : null;
            if (player == null) return;
            float share = SpellZoneRule308.HealShare(Zones.Zones, HandlerId, ctx.Host.PlayerPosition, ctx.Now - ctx.Delta, ctx.Now);
            if (share > 0f) player.Heal(player.MaxHp * share);
        }
    }
}
