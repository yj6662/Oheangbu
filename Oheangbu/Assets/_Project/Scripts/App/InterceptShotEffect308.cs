using System.Collections.Generic;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // intercept.single (SPEC-SPELL-120-308 WP-13, opened by D308-13 Q5): the freezing needle that shoots one enemy
    // projectile out of the air.
    //  - With an incoming projectile inside the needle's fan (intercept.angle either side of the caster's facing, within
    //    intercept.range), the needle goes for the one nearest to the caster (InterceptRule308): it reaches it after the
    //    flight time of the base glyph's shot and the projectile is frozen and dropped, if it is still in the air then.
    //    That cast hits no enemy: the needle is used up on the projectile.
    //  - With nothing to intercept, the cast is the base glyph's single shot (the stroke adapter presents it) at
    //    intercept.fallback of its power (D308-123: a weaker metal needle; it still counts as metal for the five elements).
    //    Its target is the host's aimed target: the lock-on, else the enemy on the aim line, else none (never the nearest).
    public sealed class InterceptShotEffect308 : ISpellEffect
    {
        public const string HandlerId = "intercept.single";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("intercept.range", 1f, 100f), new SpellParamSpec("intercept.angle", 1f, 180f), new SpellParamSpec("intercept.fallback", 0.01f, 1f),
        };

        readonly InterceptQueue308 _queue = new InterceptQueue308();
        readonly List<ISpellInterceptable308> _found = new List<ISpellInterceptable308>();
        readonly List<SpellProjectileSnap> _snaps = new List<SpellProjectileSnap>();
        Collider[] _overlaps = new Collider[SpellProjectileTestTarget308.QueryCapacity];

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int NeedlesInFlight => _queue.Count;
        public int Downed => _queue.Downed;

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx)
        {
            var host = ctx.Host; var cast = ctx.Cast; var row = ctx.Row;
            float range = row.F("intercept.range", 0f);
            Intercepts308.Gather(host, range, _found, ref _overlaps);
            Intercepts308.Snap(_found, _snaps);
            int pick = InterceptRule308.PickOne(_snaps, host.PlayerPosition, host.PlayerForward, row.F("intercept.angle", 0f), range);
            if (pick == InterceptRule308.None) { _found.Clear(); return host.PlanSingle(Fallback(cast, row.F("intercept.fallback", 1f)), true) != null; }
            var shot = _found[pick];
            _found.Clear();
            Vector3 muzzle = SingleShots308.Muzzle(host), point = shot.Position;
            float flight = InterceptRule308.FlightSeconds(muzzle, point, SingleShots308.Speed(host, cast));
            host.PresentationOwned();
            var handle = ctx.Fx.Begin(new SpellFxRequest
            {
                Letter = cast.Letter, Role = SpellFxRole.Projectile, Element = cast.Element, Origin = muzzle, FallbackPoint = point,
                ImpactClock = flight, Grade01 = cast.Brush01,
            });
            _queue.Add(shot, ctx.Now + flight, SingleShots308.Identity(host, cast.Element).AttackId, handle.Id);
            return true;
        }

        // The cast with its power cut to the share; the brush and everything else stay.
        public static SpellCast Fallback(in SpellCast cast, float share)
        {
            if (!(share > 0f) || share >= 1f) return cast;
            return new SpellCast(cast.Letter, cast.Kind, cast.Element, cast.Power * share, cast.Area, cast.SpeedMul, cast.HoldScale, cast.Brush, cast.Brush01);
        }

        public void Tick(in SpellTickContext ctx) { _queue.Fire(ctx.Now, ctx.Fx); }
        public void Clear(SpellClearReason reason) { _queue.Clear(); _found.Clear(); _snaps.Clear(); }
    }
}
