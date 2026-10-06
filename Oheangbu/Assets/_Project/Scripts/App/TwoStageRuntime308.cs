using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // Unity half shared by the two-stage attack effects (SPEC-SPELL-120-308 WP-03). The pure rules (TwoStageRule308,
    // PathFrontRule308 and the per-effect *Rule308 files) decide who is hit and when; the classes here turn enemies into
    // snapshots and execute the orders through the host. Nothing here tunes the game.

    // What one cast keeps for its second stage: the enemies it saw and the life each was in, where the caster stood, and
    // one attack identity for the whole second stage (separate from every primary hit, which the wiring numbers itself).
    public sealed class TwoStageCast308
    {
        public readonly char Letter;
        public readonly Element Element;
        public readonly Vector3 CasterPosition, CasterForward;
        public readonly Vector3 SightOrigin;        // where the wiring looks from for the primary hits of the same cast
        public readonly AttackProvenance Attack;
        public readonly SpellActorSnap[] AtCast;
        readonly EnemyVitals[] _actors;
        readonly SpellActorSnap[] _now;

        TwoStageCast308(char letter, Element element, Vector3 position, Vector3 forward, EnemyVitals[] actors, AttackProvenance attack)
        {
            Letter = letter; Element = element; CasterPosition = position; CasterForward = forward;
            SightOrigin = position + Vector3.up * .4f;
            Attack = attack; _actors = actors;
            AtCast = new SpellActorSnap[actors.Length]; _now = new SpellActorSnap[actors.Length];
            Fill(AtCast);
        }

        // Call before the primary is planned. The actor list is copied: an order of this cast always means the same enemy,
        // even if the wiring's target list changes afterwards.
        public static TwoStageCast308 Take(in SpellCastContext ctx)
        {
            var host = ctx.Host;
            var targets = host.Targets;
            int count = targets != null ? targets.Count : 0;
            var actors = new EnemyVitals[count];
            for (int i = 0; i < count; i++) actors[i] = targets[i];
            Object instigator = host.PlayerVitals != null ? (Object)host.PlayerVitals : host.Player;
            return new TwoStageCast308(ctx.Cast.Letter, ctx.Cast.Element, host.PlayerPosition, host.PlayerForward, actors,
                AttackProvenance.Create(instigator, DamageSource.PlayerDirect, ctx.Cast.Element));
        }

        public int Count => _actors.Length;

        public int IndexOf(EnemyVitals target)
        {
            if (target != null)
                for (int i = 0; i < _actors.Length; i++) if (_actors[i] == target) return i;
            return -1;
        }

        // The same enemies as they are now. One buffer per cast, refilled on every call (a front asks every frame).
        public SpellActorSnap[] Now()
        {
            Fill(_now);
            return _now;
        }

        // Slot by slot the mapping of SpellSnapshots308.Take: a missing or disabled enemy is a slot that is not alive.
        void Fill(SpellActorSnap[] snaps)
        {
            for (int i = 0; i < _actors.Length; i++)
            {
                var enemy = _actors[i];
                bool present = enemy != null && enemy.isActiveAndEnabled;
                snaps[i] = present
                    ? new SpellActorSnap(i, enemy.transform.position, enemy.IsAlive, enemy.LifeRevision, enemy.Hp01, enemy.IsBoss)
                    : new SpellActorSnap(i, default, false, 0, 0f, false);
            }
        }

        // Executes second-stage orders now and returns how many applied damage. An order for an enemy that is dead,
        // disabled or in another life than the cast saw is dropped; the host adds its own life and sight checks. Other
        // actors do not block an area judgement; the environment does. The host's direct hit does not apply the equipment
        // scale (its scheduled hit does), so it is applied here.
        public int Apply(ISpellCastHost host, List<SpellHitOrder> orders)
        {
            if (host == null || orders == null) return 0;
            float scale = host.DamageScale(Element);
            int applied = 0;
            for (int i = 0; i < orders.Count; i++)
            {
                var order = orders[i];
                if (order.TargetId < 0 || order.TargetId >= AtCast.Length) continue;
                uint life = AtCast[order.TargetId].Life;
                var target = SpellSnapshots308.Target(_actors, order.TargetId, life);
                if (target == null) continue;
                var result = host.ApplyDirectHit(target, life, order.Power * scale, SightOrigin, Attack, Letter, true);
                if (result.AppliedDamage > 0f) applied++;
            }
            return applied;
        }
    }

    // A front that runs a corridor and judges the enemies it reaches, frame by frame (the delayed second front and the
    // returning front are both this).
    public sealed class PathFrontStage308
    {
        public readonly TwoStageCast308 Cast;
        public readonly PathFront308 Front;
        public readonly float Power;
        readonly bool[] _passed;

        public PathFrontStage308(TwoStageCast308 cast, PathFront308 front, float power)
        {
            Cast = cast; Front = front; Power = power;
            _passed = new bool[cast.Count];
        }

        // One judgement step. judged = TwoStageClock308.Advance of this frame. true = the front has run its corridor:
        // drop the stage.
        public bool Step(ISpellCastHost host, float judged, List<SpellHitOrder> orders)
        {
            if (!Front.Valid) return true;
            if (!Front.Started(judged)) return false;
            orders.Clear();
            PathFrontRule308.Sweep(Front, Cast.AtCast, Cast.Now(), _passed, judged, Power, Cast.Element, orders);
            Cast.Apply(host, orders);
            return Front.Finished(judged);
        }
    }
}
