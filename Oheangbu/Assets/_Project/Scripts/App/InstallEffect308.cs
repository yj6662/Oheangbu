using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // #308 WP-10 harmony install (COMBAT-INSTALL; the five a + mieum glyphs share this handler, the element is the row's).
    //   cast      a mark flies to the aimed enemy and attaches to it. Attaching deals no damage. No aimed enemy = ink only.
    //   trigger   the next confirmed hit of an attack spell on that enemy (DamageSource.PlayerDirect). Summon, persistent,
    //             companion, retaliation and harmony damage never detonate a mark.
    //   detonate  one hit of the installed element (DamageSource.Harmony: no five-element credit) and a groggy gain through
    //             GroggySource.Harmony, one of the three canon groggy sources. The common interim detonation (TEST).
    //   unfired   the mark expires after install.life seconds, or is lost with its enemy's life. Over install.max the oldest
    //             mark makes room.
    // Every number is a row parameter. The judgement is InstallRule308 (pure); this class holds the Unity half.
    public sealed class InstallEffect308 : ISpellEffect, ISpellHitHook
    {
        public const string HandlerId = "install.mark";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("install.life", .1f, 600f),
            new SpellParamSpec("install.max", 1f, 16f),
            new SpellParamSpec("detonate.groggy", 0f, 10f),
            new SpellParamSpec("detonate.power", 0f, 1000f),
        };

        // What the rule does not need: the enemy, the glyph (for the contact presentation) and the presentation handle.
        struct Carried { public EnemyVitals Target; public char Letter; public SpellFxHandle Fx; }
        struct Flight { public InstallMark308 Mark; public float At, LifeSeconds; public int Max; public Vector3 Origin; public Carried Carried; }

        readonly List<InstallMark308> _marks = new List<InstallMark308>();
        readonly Dictionary<long, Carried> _carried = new Dictionary<long, Carried>();
        readonly List<Flight> _flights = new List<Flight>();
        readonly List<InstallMark308> _expired = new List<InstallMark308>(), _lost = new List<InstallMark308>();
        long _serial;

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;

        // Read-only views for checks and presentation layers. Rules never read them back.
        public int MarkCount => _marks.Count;
        public int InFlight => _flights.Count;
        public bool Marked(EnemyVitals target)
        {
            foreach (var pair in _carried) if (pair.Value.Target == target) return true;
            return false;
        }

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null && ctx.Host.Config != null;

        public bool Commit(in SpellCastContext ctx)
        {
            var host = ctx.Host; var row = ctx.Row;
            var target = host.AimedTarget();
            // no enemy to carry it: the ink is spent and nothing is installed (the stroke adapter shows its own cast)
            if (target == null) return true;
            int id = SpellSnapshots308.IndexOf(host.Targets, target);
            if (id < 0) return true;
            Vector3 from = host.PlayerPosition, origin = from + Vector3.up * .4f;
            float flight = InstallRule308.FlightSeconds(from, target.transform.position, host.Config.SpellProjectileSpeed, ctx.Cast.SpeedMul);
            float life = row.F("install.life", 0f);
            host.PresentationOwned();
            var fx = ctx.Fx.Begin(new SpellFxRequest
            {
                Letter = ctx.Cast.Letter, Role = SpellFxRole.Mark, Element = ctx.Cast.Element, Origin = origin,
                FallbackPoint = target.transform.position, Target = target.transform, ImpactClock = flight, Duration = flight + life,
                Grade01 = ctx.Cast.Brush01,
            });
            _flights.Add(new Flight
            {
                Mark = new InstallMark308
                {
                    TargetId = id, Life = target.LifeRevision, Element = ctx.Cast.Element,
                    Power = InstallRule308.DetonationPower(row.F("detonate.power", 0f), ctx.Cast.Brush, ctx.Cast.HoldScale),
                    Groggy = Mathf.RoundToInt(row.F("detonate.groggy", 0f)),
                },
                At = ctx.Now + flight, LifeSeconds = life, Max = Mathf.RoundToInt(row.F("install.max", 1f)), Origin = origin,
                Carried = new Carried { Target = target, Letter = ctx.Cast.Letter, Fx = fx },
            });
            return true;
        }

        public void Tick(in SpellTickContext ctx)
        {
            if (ctx.Host == null) return;
            var targets = ctx.Host.Targets;
            // arrivals: the mark attaches only to the enemy and life it was thrown at, and only if that enemy is still in sight
            for (int i = _flights.Count - 1; i >= 0; i--)
            {
                if (ctx.Now < _flights[i].At) continue;
                var flight = _flights[i]; _flights.RemoveAt(i);
                var target = SpellSnapshots308.Target(targets, flight.Mark.TargetId, flight.Mark.Life);
                if (target == null || target != flight.Carried.Target || !ctx.Host.TargetVisible(target, flight.Origin))
                { Drop(ctx.Fx, flight.Carried.Fx, SpellFxCue.Miss, flight.Carried.Target); continue; }
                var mark = flight.Mark; mark.Serial = ++_serial; mark.ExpiresAt = ctx.Now + flight.LifeSeconds;
                _lost.Clear();
                InstallRule308.Attach(_marks, mark, flight.Max, _lost);
                Forget(ctx.Fx, _lost, SpellFxCue.Expire);
                _carried[mark.Serial] = flight.Carried;
                ctx.Fx.Cue(flight.Carried.Fx, SpellFxCue.Anchor, new SpellFxCueArgs { Target = target.transform, Point = target.transform.position, At = ctx.Now });
            }
            // unfired marks: time ran out, or the enemy is gone or lives another life (no snapshot array: this runs every frame)
            _expired.Clear(); _lost.Clear();
            for (int i = _marks.Count - 1; i >= 0; i--)
            {
                var mark = _marks[i];
                bool held = SpellSnapshots308.Target(targets, mark.TargetId, mark.Life) != null;
                var fate = InstallRule308.FateOf(mark, held, ctx.Now);
                if (fate == InstallRule308.Fate.Keep) continue;
                (fate == InstallRule308.Fate.Lost ? _lost : _expired).Add(mark);
                _marks.RemoveAt(i);
            }
            Forget(ctx.Fx, _expired, SpellFxCue.Expire);
            Forget(ctx.Fx, _lost, SpellFxCue.TargetDefeated);
        }

        public void OnEnemyHit(in EnemyDamageResult result, in SpellTickContext ctx)
        {
            if (_marks.Count == 0 || result.Target == null || ctx.Host == null) return;
            int id = SpellSnapshots308.IndexOf(ctx.Host.Targets, result.Target);
            if (id < 0) return;
            if (result.Killed)
            {
                _lost.Clear();
                InstallRule308.DropTarget(_marks, id, _lost);
                Forget(ctx.Fx, _lost, SpellFxCue.TargetDefeated);
                return;
            }
            if (!InstallRule308.Detonates(OriginOf(result.Attack.Source), result.AppliedDamage)) return;
            // one mark at a time, taken out of the list before it fires: the detonation's own hit comes back through this
            // hook (as Harmony, which never detonates) and may defeat the enemy
            while (InstallRule308.TakeForDetonation(_marks, id, result.Target.LifeRevision, ctx.Now, out var mark))
                Detonate(mark, ctx);
        }

        void Detonate(in InstallMark308 mark, in SpellTickContext ctx)
        {
            if (!_carried.TryGetValue(mark.Serial, out var carried)) return;
            _carried.Remove(mark.Serial);
            var target = SpellSnapshots308.Target(ctx.Host.Targets, mark.TargetId, mark.Life);
            if (target == null) { Drop(ctx.Fx, carried.Fx, SpellFxCue.TargetDefeated, carried.Target); return; }
            Object instigator = ctx.Host.PlayerVitals != null ? (Object)ctx.Host.PlayerVitals : ctx.Host.Player;
            var attack = AttackProvenance.Create(instigator, DamageSource.Harmony, mark.Element);
            // the mark sits on the enemy: the hit starts at the enemy, so no wall between player and enemy can swallow it
            Vector3 point = target.transform.position + Vector3.up * .4f;
            ctx.Fx.Cue(carried.Fx, SpellFxCue.Detonate, new SpellFxCueArgs { Target = target.transform, Point = point, At = ctx.Now, Value = mark.Power });
            ctx.Host.ApplyDirectHit(target, mark.Life, mark.Power * ctx.Host.DamageScale(mark.Element), point, attack, carried.Letter, true);
            // damage first, groggy second: the detonation is not amplified by the weak point it may open
            if (target.IsAlive && target.LifeRevision == mark.Life) target.AddGroggy(GroggySource.Harmony, attack, mark.Groggy);
            ctx.Fx.End(carried.Fx);
        }

        public void Clear(SpellClearReason reason)
        {
            // the host ends every presentation right after (ISpellPresenter.EndAll)
            _marks.Clear(); _carried.Clear(); _flights.Clear(); _expired.Clear(); _lost.Clear();
        }

        void Forget(ISpellPresenter fx, List<InstallMark308> gone, SpellFxCue cue)
        {
            for (int i = 0; i < gone.Count; i++)
            {
                if (!_carried.TryGetValue(gone[i].Serial, out var carried)) continue;
                _carried.Remove(gone[i].Serial);
                Drop(fx, carried.Fx, cue, carried.Target);
            }
        }

        static void Drop(ISpellPresenter fx, SpellFxHandle handle, SpellFxCue cue, EnemyVitals target)
        {
            if (fx == null) return;
            fx.Cue(handle, cue, new SpellFxCueArgs { Target = target != null ? target.transform : null });
            fx.End(handle);
        }

        static InstallRule308.Origin OriginOf(DamageSource source)
        {
            switch (source)
            {
                case DamageSource.PlayerDirect: return InstallRule308.Origin.AttackSpell;
                case DamageSource.Summon: return InstallRule308.Origin.Summon;
                case DamageSource.PersistentSpell: return InstallRule308.Origin.Persistent;
                case DamageSource.Companion: return InstallRule308.Origin.Companion;
                case DamageSource.Retaliation: return InstallRule308.Origin.Retaliation;
                case DamageSource.Harmony: return InstallRule308.Origin.Harmony;
                case DamageSource.Harvest: return InstallRule308.Origin.Harvest;
                case DamageSource.FiveElementBonus: return InstallRule308.Origin.Bonus;
                default: return InstallRule308.Origin.Other;
            }
        }
    }
}
