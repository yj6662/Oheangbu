using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // One derived judgement waiting for its time: the enemy it was made for (and that enemy's life), what it carries and
    // where it leaves from. The Unity half of DerivedHitRule308 (SPEC-SPELL-120-308 WP-04).
    public struct DerivedShot308
    {
        public EnemyVitals Target;
        public uint Life;
        public float Power;           // equipment scale already inside (it comes from the scheduled power of the first hit)
        public Element Element;
        public char Letter;           // carried from the cast for the damage call, never compared
        public Vector3 From;          // where this derived shot leaves; line of sight is judged from here
        public int Fx;                // presenter handle id of the cast
        public int Part;              // which derived body this is (left / right crescent, droplet number, volley shot)
        public SpellRow Row;          // only for shots that may derive again (a bounce)
        public int Depth;             // bounce number
        public float ShotPower;       // power of the shot's first hit, before any bounce
        public EnemyVitals[] Visited; // every enemy the shot has touched so far
    }

    // What the five WP-04 effects do the same way: watch a scheduled impact, present the cast, land a derived judgement.
    // No state here; each effect owns its own lists.
    public static class DerivedHits308
    {
        // Nominal ink cost of the cast, the same expression the wiring spends (before any discount hook).
        public static float CastCost(in SpellCastContext ctx)
        {
            return ctx.Host.Config != null ? ctx.Host.Config.SpellInkCost * Mathf.Max(0f, ctx.Row.F("cost", 1f)) : 0f;
        }

        // The first stage of a single shot that presents its own flying body: it flies as its body flies (SpellPrimary308).
        // An aimed shot is planned without the stroke adapter; a shot nobody was aimed at is the usual missed shot the adapter
        // shows. A shot whose target is beyond its reach answers CanLand = false too: the adapter shows it as missed.
        public static SpellPrimaryShot308 Primary(in SpellCastContext ctx)
        {
            return SpellPrimary308.Plan(ctx, ctx.Host.AimedTarget() == null);
        }

        // The watch of a first stage: it waits until the last moment that shot can still land (a guided shot may arrive
        // later than expected).
        public static DerivedHitWatch308 Watch(in SpellCastContext ctx, in SpellPrimaryShot308 primary, SpellFxHandle fx)
        {
            var watch = Watch(ctx, primary.Hit, 0, fx);
            watch.ImpactAt = primary.LatestImpact;
            return watch;
        }

        public static DerivedHitWatch308 Watch(in SpellCastContext ctx, PlannedHit hit, int shot, SpellFxHandle fx)
        {
            return new DerivedHitWatch308
            {
                AttackId = hit.AttackId, ImpactAt = hit.ImpactTime, Power = hit.Power, Origin = ctx.Host.PlayerPosition,
                Element = ctx.Cast.Element, Letter = ctx.Cast.Letter, Row = ctx.Row, Cost = CastCost(ctx), Shot = shot, Fx = fx.Id,
            };
        }

        // The cast's own presentation: one flying body towards the enemy the wiring scheduled. The stroke adapter is told to
        // stay out (PresentationOwned) by the caller. duration = how long the presentation must live (0 = its own default).
        public static SpellFxHandle BeginShot(in SpellCastContext ctx, PlannedHit hit, float duration, AreaImpactPlan area = null)
        {
            if (ctx.Fx == null) return default;
            Transform target = hit != null && hit.Target != null ? hit.Target.transform : null;
            Vector3 origin = ctx.Host.PlayerPosition + Vector3.up * .4f;
            var request = new SpellFxRequest
            {
                Letter = ctx.Cast.Letter, Role = SpellFxRole.Projectile, Element = ctx.Cast.Element, Origin = origin, Target = target,
                FallbackPoint = area != null ? area.Point : target != null ? target.position : origin + ctx.Host.PlayerForward,
                ImpactClock = hit != null && area == null ? Mathf.Max(0f, hit.ImpactTime - ctx.Now) : 0f, Duration = duration,
                Grade01 = ctx.Cast.Brush01, Area = area, AttackId = hit != null ? hit.AttackId : 0,
            };
            return ctx.Fx.Begin(request);
        }

        // A meaning cue on the cast's presentation. In SpellFxCueArgs, Target is the OTHER actor the cue points at (the next
        // enemy, the caster a shot returns to), never the enemy the cast was begun on.
        public static void Cue(ISpellPresenter fx, int handle, SpellFxCue cue, in SpellFxCueArgs args)
        {
            if (fx != null && handle != 0) fx.Cue(new SpellFxHandle(handle), cue, args);
        }

        // Lands one derived judgement now: the same enemy and the same life it was made for, line of sight from where the
        // derived shot left, and an attack identity of its own (player-direct, the element of the cast). Other enemies' bodies
        // do not stop a derived shot; a wall does. No enemy, another life, or a wall = no damage (default result).
        public static EnemyDamageResult Apply(in SpellTickContext ctx, in DerivedShot308 shot)
        {
            var target = shot.Target;
            if (ctx.Host == null || target == null || !target.isActiveAndEnabled || !target.IsAlive || target.LifeRevision != shot.Life) return default;
            // the same instigator the wiring gives its own scheduled hits
            Object caster = ctx.Host.PlayerVitals != null ? (Object)ctx.Host.PlayerVitals : ctx.Host.Player;
            var attack = AttackProvenance.Create(caster, DamageSource.PlayerDirect, shot.Element);
            return ctx.Host.ApplyDirectHit(target, shot.Life, shot.Power, shot.From + Vector3.up * .4f, attack, shot.Letter, true);
        }

        // Snapshot ids of these enemies in the current target list (an enemy that left the list is skipped).
        public static void Ids(IReadOnlyList<EnemyVitals> targets, EnemyVitals[] enemies, List<int> ids)
        {
            ids.Clear();
            if (enemies == null) return;
            for (int i = 0; i < enemies.Length; i++)
            {
                int id = SpellSnapshots308.IndexOf(targets, enemies[i]);
                if (id >= 0) ids.Add(id);
            }
        }
    }
}
