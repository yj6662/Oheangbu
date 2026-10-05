using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // single.ballistic (SPEC-SPELL-120-308 WP-11): the unguided rock. It is thrown at the spot the aimed enemy stands on at
    // cast time and lands there after the wiring's own flight time. The enemy is hit only if it still stands within
    // fall.radius of that spot when the rock lands (BallisticRule308); only the aimed enemy can be hit. The hit is held
    // here and landed through the host's direct hit, with the same power, element, source and sight check a scheduled hit
    // has. The stroke adapter presents the shot as before (SingleShots308.Present). A cast without a target is the air
    // cast of before. Reached only through the handover of SpellTakeover308.
    // It is also the first stage of every glyph of the same body behind a final consonant (ISpellPrimaryStage308): those
    // glyphs fall on the spot the enemy stood on and miss an enemy that walked away, with this row's fall.radius.
    public sealed class BallisticEffect308 : ISpellEffect, ISpellPrimaryStage308
    {
        public const string HandlerId = "single.ballistic";
        static readonly SpellParamSpec[] Specs = { new SpellParamSpec("fall.radius", .1f, 20f) };

        struct Rock
        {
            public EnemyVitals Target; public uint Life;
            public Vector3 FallPoint, SightOrigin;
            public float LandsAt, Power, Radius;           // Power: the equipment scale is inside (SingleShots308.HeldHit)
            public char Letter;
            public AttackProvenance Attack;
        }
        readonly List<Rock> _rocks = new List<Rock>();

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int RocksInFlight => _rocks.Count;
        public int Misses { get; private set; }             // rocks that landed on an empty spot (checks read this)

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx) => Launch(ctx.Host, ctx.Cast, ctx.Row, ctx.Now, true).Launched;

        public SpellPrimaryShot308 Launch(ISpellCastHost host, in SpellCast cast, SpellRow ballistics, float now, bool feedAdapter)
        {
            EnemyVitals target = host.AimedTarget();
            if (target == null) return SpellPrimary308.LockOn(host, cast, feedAdapter);     // the air cast of before
            var shown = SingleShots308.Present(host, cast, feedAdapter);
            if (shown == null) return SpellPrimaryShot308.Refused;
            Vector3 fallPoint = target.transform.position;
            // the broadcast plan says where the rock comes down too (a presenter that reads the plan can fly it there)
            if (shown.Hits.Count > 0) { shown.Hits[0].HasImpactPoint = true; shown.Hits[0].ImpactPoint = fallPoint; }
            float landsAt = now + BallisticRule308.FlightSeconds(host.PlayerPosition, fallPoint, SingleShots308.Speed(host, cast));
            var attack = SingleShots308.Identity(host, cast.Element);
            var hit = SingleShots308.HeldHit(host, cast, target, landsAt, attack);
            // the spot is part of the judgement: whoever presents this shot can fly it to where the rock really comes down
            hit.HasImpactPoint = true; hit.ImpactPoint = fallPoint;
            _rocks.Add(new Rock
            {
                Target = target, Life = target.LifeRevision, FallPoint = fallPoint, SightOrigin = SingleShots308.Muzzle(host),
                LandsAt = landsAt, Power = hit.Power, Radius = ballistics.F("fall.radius", 0f), Letter = cast.Letter, Attack = attack,
            });
            return SpellPrimaryShot308.InFlight(hit, landsAt);
        }

        public void Tick(in SpellTickContext ctx)
        {
            for (int i = _rocks.Count - 1; i >= 0; i--)
            {
                var rock = _rocks[i];
                if (ctx.Now < rock.LandsAt) continue;
                _rocks.RemoveAt(i);
                var target = rock.Target;
                bool there = target != null && target.isActiveAndEnabled && target.IsAlive && target.LifeRevision == rock.Life &&
                    BallisticRule308.Lands(rock.FallPoint, target.transform.position, rock.Radius);
                if (!there) { Misses++; continue; }
                SingleShots308.Land(ctx.Host, target, rock.Life, rock.Power, rock.SightOrigin, rock.Attack, rock.Letter);
            }
        }

        public int HeldShots => _rocks.Count;
        public void Recall(int mark) { if (mark >= 0 && _rocks.Count > mark) _rocks.RemoveRange(mark, _rocks.Count - mark); }

        public void Clear(SpellClearReason reason) { _rocks.Clear(); }
    }
}
