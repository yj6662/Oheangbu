using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // single.homing (SPEC-SPELL-120-308 WP-11): the slow guided shot. It leaves towards the aimed enemy and steers after it
    // every frame, limited by turn.rate (degrees per second) and turn.limit (degrees in total). It hits when it comes
    // within hit.radius of the enemy; it misses when the turn limit is used up and the enemy is no longer ahead, or after
    // flight.max seconds (HomingRule308). The hit is held here and landed through the host's direct hit, with the same
    // power, element, source and sight check a scheduled hit has. The stroke adapter presents the shot as before
    // (SingleShots308.Present). A cast without a target is the air cast of before. Reached only through the handover of
    // SpellTakeover308.
    // It is also the first stage of every glyph of the same body behind a final consonant (ISpellPrimaryStage308): those
    // glyphs steer within this row's turn limits and lose an enemy that gets behind them.
    public sealed class HomingEffect308 : ISpellEffect, ISpellPrimaryStage308
    {
        public const string HandlerId = "single.homing";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("turn.rate", 0f, 3600f), new SpellParamSpec("turn.limit", 0f, 3600f),
            new SpellParamSpec("hit.radius", .05f, 10f), new SpellParamSpec("flight.max", .1f, 60f),
        };

        sealed class Shot
        {
            public EnemyVitals Target; public uint Life;
            public HomingShot308 State;
            public Vector3 SightOrigin;
            public float Speed, TurnRate, TurnLimit, HitRadius, MaxAge, Power;   // Power: the equipment scale is inside
            public char Letter;
            public AttackProvenance Attack;
        }
        readonly List<Shot> _shots = new List<Shot>();

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int ShotsInFlight => _shots.Count;
        public int Misses { get; private set; }             // shots that lost their target (checks read this)

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx) => Launch(ctx.Host, ctx.Cast, ctx.Row, ctx.Now, true).Launched;

        public SpellPrimaryShot308 Launch(ISpellCastHost host, in SpellCast cast, SpellRow ballistics, float now, bool feedAdapter)
        {
            EnemyVitals target = host.AimedTarget();
            if (target == null) return SpellPrimary308.LockOn(host, cast, feedAdapter);     // the air cast of before
            if (SingleShots308.Present(host, cast, feedAdapter) == null) return SpellPrimaryShot308.Refused;
            Vector3 muzzle = SingleShots308.Muzzle(host);
            float speed = SingleShots308.Speed(host, cast), maxAge = ballistics.F("flight.max", 0f);
            var attack = SingleShots308.Identity(host, cast.Element);
            // expected landing = the straight flight the stroke adapter shows; a shot that has to turn arrives later, up to flight.max
            var hit = SingleShots308.HeldHit(host, cast, target, now + BallisticRule308.FlightSeconds(host.PlayerPosition, target.transform.position, speed), attack);
            _shots.Add(new Shot
            {
                Target = target, Life = target.LifeRevision, SightOrigin = muzzle,
                State = HomingRule308.Launch(muzzle, Aim(target), host.PlayerForward),
                Speed = speed, TurnRate = ballistics.F("turn.rate", 0f), TurnLimit = ballistics.F("turn.limit", 0f),
                HitRadius = ballistics.F("hit.radius", 0f), MaxAge = maxAge, Power = hit.Power, Letter = cast.Letter, Attack = attack,
            });
            return SpellPrimaryShot308.InFlight(hit, now + maxAge + SingleShots308.WatchSlack);
        }

        public void Tick(in SpellTickContext ctx)
        {
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                var shot = _shots[i];
                var target = shot.Target;
                if (target == null || !target.isActiveAndEnabled || !target.IsAlive || target.LifeRevision != shot.Life)
                { _shots.RemoveAt(i); Misses++; continue; }
                var status = HomingRule308.Step(ref shot.State, Aim(target), ctx.Delta, shot.Speed, shot.TurnRate, shot.TurnLimit, shot.HitRadius, shot.MaxAge);
                if (status == HomingStatus308.Flying) continue;
                _shots.RemoveAt(i);
                if (status == HomingStatus308.Missed) { Misses++; continue; }
                SingleShots308.Land(ctx.Host, target, shot.Life, shot.Power, shot.SightOrigin, shot.Attack, shot.Letter);
            }
        }

        public int HeldShots => _shots.Count;
        public void Recall(int mark) { if (mark >= 0 && _shots.Count > mark) _shots.RemoveRange(mark, _shots.Count - mark); }

        public void Clear(SpellClearReason reason) { _shots.Clear(); }

        // The point of an enemy a shot flies at: the height the wiring's sight checks use.
        static Vector3 Aim(EnemyVitals target) => target.transform.position + Vector3.up * .4f;
    }
}
