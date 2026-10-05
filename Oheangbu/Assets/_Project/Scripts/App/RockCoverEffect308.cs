using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // single.cover (SPEC-SPELL-120-308 WP-13, opened by D308-13 Q5): the earth single shot, whose rock takes root where it
    // lands and stays as cover.
    //  - first hit = the single shot of its body (SpellPrimary308: the glyph without a final decides how it flies, here an
    //    unguided fall on the spot the enemy stood on at cast time), presented by the stroke adapter like the base glyph;
    //  - where the rock lands (that spot; for a guided shot the aimed enemy's spot at the moment of the landing; for a cast
    //    without a target, cover.range ahead of the caster) a rock of cover.radius stands for cover.life seconds. While it stands between an enemy and
    //    the player it stops that enemy's ranged strikes (CoverRule308, CoverEffect308). At most cover.max rocks stand at
    //    once: the oldest makes room.
    // The rock is a rule object only: it has no collider of its own (walking and line-of-sight colliders are level work).
    public sealed class RockCoverEffect308 : CoverEffect308
    {
        public const string HandlerId = "single.cover";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("cover.radius", .2f, 10f), new SpellParamSpec("cover.life", .5f, 120f),
            new SpellParamSpec("cover.max", 1f, 8f), new SpellParamSpec("cover.range", 1f, 50f),
        };

        struct Falling
        {
            public EnemyVitals Target; public uint Life;
            public Vector3 Point;
            public float LandsAt, Radius, Stays, Grade;
            public bool Guided;                     // the shot follows its target: the rock roots where the target stands at the landing
            public int Max; public char Letter; public Element Element;
        }
        readonly List<Falling> _falling = new List<Falling>();

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        public int RocksInFlight => _falling.Count;

        public override bool Commit(in SpellCastContext ctx)
        {
            var host = ctx.Host; var cast = ctx.Cast; var row = ctx.Row;
            var shot = SpellPrimary308.Plan(ctx, true);
            if (!shot.Launched) return false;
            var falling = new Falling
            {
                Radius = row.F("cover.radius", 0f), Stays = row.F("cover.life", 0f), Max = Mathf.RoundToInt(row.F("cover.max", 1f)),
                Letter = cast.Letter, Element = cast.Element, Grade = cast.Brush01,
            };
            if (shot.CanLand)
            {
                var hit = shot.Hit;
                falling.Target = hit.Target; falling.Life = hit.Target.LifeRevision;
                falling.Guided = !hit.HasImpactPoint;
                falling.Point = hit.HasImpactPoint ? hit.ImpactPoint : hit.Target.transform.position; falling.LandsAt = hit.ImpactTime;
            }
            else
            {
                // nothing aimed at: the rock still falls, ahead of the caster
                falling.Point = CoverRule308.AirLanding(host.PlayerPosition, host.PlayerForward, row.F("cover.range", 0f));
                falling.LandsAt = ctx.Now + BallisticRule308.FlightSeconds(host.PlayerPosition, falling.Point, SingleShots308.Speed(host, cast));
            }
            _falling.Add(falling);
            return true;
        }

        protected override void Advance(in SpellTickContext ctx)
        {
            for (int i = 0; i < _falling.Count; i++)
            {
                var rock = _falling[i];
                if (ctx.Now < rock.LandsAt) continue;
                _falling.RemoveAt(i); i--;
                var target = rock.Target;
                // an unguided rock lands on the spot it was thrown at; a guided one where its target stands then (a target
                // that is gone leaves the spot it was aimed at)
                bool there = target != null && target.isActiveAndEnabled && target.LifeRevision == rock.Life;
                Vector3 point = CoverRule308.RockLanding(rock.Point, rock.Guided, there, there ? target.transform.position : rock.Point);
                var piece = CoverRule308.RockOf(NewToken(ctx.Host, rock.Element), point, rock.Radius, ctx.Now, rock.Stays);
                if (ctx.Fx != null)
                    piece.Fx = ctx.Fx.Begin(new SpellFxRequest
                    {
                        Letter = rock.Letter, Role = SpellFxRole.Ward, Element = rock.Element, Origin = point, FallbackPoint = point,
                        Duration = rock.Stays, Radius = rock.Radius, Grade01 = rock.Grade, AttackId = piece.Token,
                    }).Id;
                Add(piece, rock.Max, ctx.Fx);
            }
        }

        public override void Clear(SpellClearReason reason)
        {
            base.Clear(reason);
            _falling.Clear();
        }
    }
}
