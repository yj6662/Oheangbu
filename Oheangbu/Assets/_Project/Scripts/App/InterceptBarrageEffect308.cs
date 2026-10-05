using System.Collections.Generic;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // intercept.barrage (SPEC-SPELL-120-308 WP-13, opened by D308-13 Q5): the barrage that shoots enemy projectiles down
    // together.
    //  - first hit = the base glyph's volley on the enemies in its fan, unchanged (the stroke adapter presents it);
    //  - at the moment the barrage forms (the volley's formation delay), every incoming enemy projectile inside the same
    //    fan (the volley's half angle and range, from where the caster stood) is frozen and dropped at once, nearest
    //    first, at most intercept.max of them (InterceptRule308). What is in the air is judged at that moment, not at
    //    cast time: a projectile that has landed by then is not there to shoot down.
    // The interceptions cost the volley nothing: its shots and its power on enemies are the base glyph's.
    public sealed class InterceptBarrageEffect308 : ISpellEffect
    {
        public const string HandlerId = "intercept.barrage";
        static readonly SpellParamSpec[] Specs = { new SpellParamSpec("intercept.max", 1f, 64f) };

        struct Barrage { public Vector3 Caster, Forward; public float At, HalfAngle, Range; public int Max; public long Owner; }
        readonly List<Barrage> _barrages = new List<Barrage>();
        readonly List<ISpellInterceptable308> _found = new List<ISpellInterceptable308>();
        readonly List<SpellProjectileSnap> _snaps = new List<SpellProjectileSnap>();
        readonly List<int> _picked = new List<int>();
        Collider[] _overlaps = new Collider[SpellProjectileTestTarget308.QueryCapacity];

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int BarragesForming => _barrages.Count;
        public int Downed { get; private set; }            // projectiles shot down so far (checks read this)

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx)
        {
            var host = ctx.Host; var cast = ctx.Cast;
            Vector3 caster = host.PlayerPosition, forward = host.PlayerForward;
            var plan = host.PlanVolley(cast, true);
            if (plan == null) return false;
            var area = plan.Area;
            if (area == null) return true;
            _barrages.Add(new Barrage
            {
                Caster = caster, Forward = forward, At = ctx.Now + area.Delay, HalfAngle = area.Radius, Range = area.Length,
                Max = Mathf.RoundToInt(ctx.Row.F("intercept.max", 0f)), Owner = SingleShots308.Identity(host, cast.Element).AttackId,
            });
            return true;
        }

        public void Tick(in SpellTickContext ctx)
        {
            for (int i = _barrages.Count - 1; i >= 0; i--)
            {
                var barrage = _barrages[i];
                if (ctx.Now < barrage.At) continue;
                _barrages.RemoveAt(i);
                // the fan may reach further than the caster stands now: gather around where the caster stood, by the fan's range
                Intercepts308.Gather(ctx.Host, barrage.Range + Vector3.Distance(ctx.Host.PlayerPosition, barrage.Caster), _found, ref _overlaps);
                Intercepts308.Snap(_found, _snaps);
                InterceptRule308.PickMany(_snaps, barrage.Caster, barrage.Forward, barrage.HalfAngle, barrage.Range, barrage.Max, _picked);
                for (int p = 0; p < _picked.Count; p++)
                    if (_found[_picked[p]].Intercept(barrage.Owner)) Downed++;
                _found.Clear();
            }
        }

        public void Clear(SpellClearReason reason) { _barrages.Clear(); _found.Clear(); _snaps.Clear(); _picked.Clear(); }
    }
}
