using System;
using System.Collections.Generic;
using System.Globalization;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.Combat
{
    /// <summary>The economy state that must outlive a scene (design section 9): the 갈무리 per-target ink ledger (12 normal / 24 boss, never
    /// refilled by a scene reload or a LifeRevision change) and the 걱 out-of-combat budget (HP 30 per rest). A plain object, no statics: the
    /// owner decides its lifetime. TODAY the combat scope registers one (Vol2DefenceInstaller); moving it to the session scope is an open
    /// item (no session scope exists in the spell lab). A loaded save marks the restored living enemies with an empty ledger (OnLoaded).</summary>
    public sealed class Vol2SessionLedger
    {
        public DefenceNumbersV3Dto Numbers { get; }
        public NoInkSwingV3.State Gather { get; set; }
        public GeokRecoveryV3.State Recovery { get; set; }
        /// <summary>Interned scene+hierarchy keys of enemies (Vol2TargetBook.StableId).</summary>
        public Vol2KeyBook Keys { get; } = new Vol2KeyBook();
        public Vol2SessionLedger(DefenceNumbersV3Dto numbers)
        {
            Numbers = numbers ?? throw new ArgumentNullException(nameof(numbers));
            Gather = new NoInkSwingV3.State(numbers);
            Recovery = new GeokRecoveryV3.State(numbers);
        }
        /// <summary>A save was loaded: the living enemies it restored have no 갈무리 ink left to give (design section 9).</summary>
        public void OnLoaded(IEnumerable<NoInkSwingV3.TargetKey> restoredAlive) { Gather = NoInkSwingV3.RestoreLoaded(Gather, restoredAlive); }
        public GeokRecoveryV3.Snapshot SaveRecovery() => GeokRecoveryV3.Save(Recovery);
        public void RestoreRecovery(GeokRecoveryV3.Snapshot snapshot) { Recovery = GeokRecoveryV3.Restore(Numbers, snapshot); }
    }

    public readonly struct Vol2GatherRecord
    {
        public readonly bool Accepted;
        public readonly string Reason;           // Gathered | Swing | CooldownOrOldSwing | FirstEnemyOnly | DuplicateEvent | Input
        public readonly long EventId, SwingId, TargetId, StableId, LifeRevision;
        public readonly decimal DamageUnits;     // as the rules answered (floor and window included), before the armour of the target
        public readonly float RequestedHp, AppliedHp;
        public readonly decimal InkGainRequested, InkGained;
        public readonly bool Boss, Window;
        public readonly int Contacts;
        public Vol2GatherRecord(bool accepted, string reason, long eventId, long swing, long target, long stable, long life, decimal damageUnits, float requestedHp,
            float appliedHp, decimal inkRequested, decimal inkGained, bool boss, bool window, int contacts)
        { Accepted = accepted; Reason = reason; EventId = eventId; SwingId = swing; TargetId = target; StableId = stable; LifeRevision = life; DamageUnits = damageUnits;
          RequestedHp = requestedHp; AppliedHp = appliedHp; InkGainRequested = inkRequested; InkGained = inkGained; Boss = boss; Window = window; Contacts = contacts; }
        public override string ToString() => Reason + (Accepted ? "" : " (refused)") + " swing=" + SwingId + " target=" + TargetId + "/" + StableId + " damage=" +
            DamageUnits.ToString(CultureInfo.InvariantCulture) + "u hp " + RequestedHp.ToString("0.###", CultureInfo.InvariantCulture) + "->" +
            AppliedHp.ToString("0.###", CultureInfo.InvariantCulture) + " ink +" + InkGained.ToString(CultureInfo.InvariantCulture) + "/" +
            InkGainRequested.ToString(CultureInfo.InvariantCulture) + (Boss ? " boss" : "") + (Window ? " window" : "") + " contacts=" + Contacts;
    }

    /// <summary>S5-b slice 2 (design section 9): 갈무리 = the inkless swing that meets its first enemy. NoInkSwingV3 decides (damage 0.3 with B at
    /// the frame, never lethal: HP floor 1; ink +4 per hit up to 12 per normal / 24 per boss target for the whole session; the carry of
    /// seeds and attachments as contacts); this service applies it: the HP through EnemyVitals.TakeResolvedVol2Damage (floor on), the ink
    /// through the pool. The swing's contact collector (slice 3) calls OnSwingFirstHit once per swing with the first enemy it met (or
    /// OnSwingMissed). Carried contacts go to whoever subscribes to Gathered (the seed / attachment executors, slices 4 to 6): they
    /// are independent spell damage, the HP floor does not travel with them.</summary>
    public sealed class Vol2GatherService
    {
        readonly Vol2SessionLedger _ledger;
        readonly DefenceNumbersV3Dto _n;
        readonly IVol2Clock _clock;
        readonly Vol2InkAccount _ink;
        readonly Vol2TargetBook _targets;
        readonly float _hpPerUnit;
        long _event;

        /// <summary>The caster's carried materials at this moment (seeds, attachments). null = nothing carried.</summary>
        public Func<IEnumerable<NoInkSwingV3.Carry>> CarryProvider { get; set; }
        /// <summary>The caster id the carries must match (NoInkSwingV3.Carry.OwnerId).</summary>
        public long OwnerId { get; set; } = 1;
        /// <summary>One session-wide encounter bucket (the TargetKey of the ledger is (encounter, target)); a real encounter id may replace it.</summary>
        public long EncounterId { get; set; } = 1;
        public Vol2GatherRecord Last { get; private set; }
        public NoInkSwingV3.Step LastStep { get; private set; }
        public event Action<Vol2GatherRecord, NoInkSwingV3.Step> Gathered;

        public Vol2GatherService(Vol2SessionLedger ledger, IVol2Clock clock, Vol2InkAccount ink, Vol2TargetBook targets, float hpPerUnit)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _n = ledger.Numbers;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _ink = ink ?? throw new ArgumentNullException(nameof(ink));
            _targets = targets ?? throw new ArgumentNullException(nameof(targets));
            if (hpPerUnit <= 0f || float.IsNaN(hpPerUnit) || float.IsInfinity(hpPerUnit)) throw new ArgumentOutOfRangeException(nameof(hpPerUnit));
            _hpPerUnit = hpPerUnit;
        }

        /// <summary>The swing cut air (no enemy met): only the swing bookkeeping advances.</summary>
        public Vol2GatherRecord OnSwingMissed(SwingId swing) => Apply(null, swing, null);

        public Vol2GatherRecord OnSwingFirstHit(EnemyVitals target, SwingId swing, UnityEngine.Object instigator = null) => Apply(target, swing, instigator);

        Vol2GatherRecord Apply(EnemyVitals target, SwingId swing, UnityEngine.Object instigator)
        {
            bool enemy = target != null && target.IsAlive;
            long id = enemy ? _targets.Id(target) : 0, stable = enemy ? _targets.StableId(target) : 0, life = enemy ? target.LifeRevision : 0;
            bool window = enemy && target.WeakPointActive;
            decimal hpRule = enemy ? Vol2Units.RuleHp(target.Hp, _hpPerUnit, _n.HpFloor) : 0m;
            var carries = CarryProvider != null ? CarryProvider() : null;
            long eventId = ++_event;
            var input = new NoInkSwingV3.Input(eventId, swing.Value, _clock.NowUs, enemy ? EncounterId : 0, enemy ? stable : 0, hpRule, _ink.Units, _ink.CapacityUnits,
                enemy, enemy && target.IsBoss, OwnerId, life, window, carries);
            var step = NoInkSwingV3.Resolve(_ledger.Gather, input);
            LastStep = step;
            if (!step.Accepted) return Publish(new Vol2GatherRecord(false, step.Reason, eventId, swing.Value, id, stable, life, 0m, 0f, 0f, 0m, 0m, false, window, 0), step);
            _ledger.Gather = step.State;
            float requested = 0f, applied = 0f;
            if (enemy && step.Damage > 0m)
            {
                requested = (float)step.Damage * _hpPerUnit;
                var terms = Vol2DamageResolver.Resolve(_n, _n.SwingDamage, 1m, 1m, false, false, window, _hpPerUnit);
                var result = target.TakeResolvedVol2Damage(requested, Vol2Attack.Create(instigator, DamageSource.Harvest, null), terms, true);
                applied = result.AppliedDamage;
            }
            decimal gained = step.InkDelta > 0m ? _ink.Gain(step.InkDelta) : 0m;
            return Publish(new Vol2GatherRecord(true, step.Reason, eventId, swing.Value, id, stable, life, step.Damage, requested, applied, step.InkDelta, gained,
                enemy && target.IsBoss, window, step.Contacts.Count), step);
        }

        Vol2GatherRecord Publish(Vol2GatherRecord r, NoInkSwingV3.Step step)
        {
            Last = r;
            Gathered?.Invoke(r, step);
            return r;
        }
    }
}
