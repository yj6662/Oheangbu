using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // WP-08 earth buff with the mieum final: an enemy that strikes the player in melee while it runs is slowed for a while
    // (EnemyControlState, movement scale only: actions are never blocked, so a boss is slowed like anyone else). The owner
    // id of the control entry is one attack identity per activation, so repeated hits refresh the slow instead of stacking.
    // Retaliation only: nothing is given to the player.
    public sealed class SlowbackEffect308 : SelfBuffEffect308, IPlayerHitHook
    {
        public const string HandlerId = "buff.slowback";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("duration", .1f, 600f), new SpellParamSpec("slow.speed", 0f, 1f), new SpellParamSpec("slow.duration", .1f, 60f),
        };
        float _speed = 1f, _seconds;
        long _owner;
        SpellActorSnap[] _snaps;

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        protected override bool OnActivating(in SpellCastContext ctx, float duration)
        {
            _speed = ctx.Row.F("slow.speed", 1f); _seconds = ctx.Row.F("slow.duration", 0f);
            _owner = AttackProvenance.Create(Instigator(ctx.Host), DamageSource.Retaliation, ctx.Cast.Element).AttackId;
            return true;
        }

        public void OnPlayerHit(in PlayerHitInfo hit, in SpellTickContext ctx, ref bool interruptsDrawing)
        {
            if (ctx.Host == null || hit.Attacker == null || _owner <= 0 || !Active(ctx.Now) || !Melee(hit.Kind)) return;
            var targets = ctx.Host.Targets;
            if (BuffRule308.Slowback(true, true, SpellSnapshots308.Fill(targets, ref _snaps), SpellSnapshots308.IndexOf(targets, hit.Attacker),
                    ctx.Now, _seconds, _speed, out float until, out float scale))
                hit.Attacker.Control.Apply(_owner, until, scale, false);
        }

        protected override void OnCleared(SpellClearReason reason) { _speed = 1f; _seconds = 0f; _owner = 0; }
    }
}
