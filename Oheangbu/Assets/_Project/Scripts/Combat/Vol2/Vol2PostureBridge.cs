using System;
using System.Globalization;
using Oheangbu.Vol2.Rules;

namespace Oheangbu.Combat
{
    /// <summary>What one posture event did (for the log and the lab CSV).</summary>
    public readonly struct Vol2PostureRecord
    {
        public readonly bool Accepted;
        public readonly string Reason;
        public readonly GroggyRulesV3.Source Source;
        public readonly long TargetId, LifeRevision;
        public readonly decimal Added, PostureAfter;
        public readonly long WindowUs;
        public readonly int WindowNumber;
        public readonly decimal TotalWindowSeconds;
        /// <summary>The window really opened on the enemy (EnemyVitals.OpenWeakPointFor answered true).</summary>
        public readonly bool OpenedOnVitals;
        public Vol2PostureRecord(bool accepted, string reason, GroggyRulesV3.Source source, long target, long life, decimal added, decimal after,
            long windowUs, int number, decimal total, bool opened)
        { Accepted = accepted; Reason = reason; Source = source; TargetId = target; LifeRevision = life; Added = added; PostureAfter = after;
          WindowUs = windowUs; WindowNumber = number; TotalWindowSeconds = total; OpenedOnVitals = opened; }
        public bool Opened => WindowUs > 0;
        public override string ToString() => Reason + " " + Source + " target=" + TargetId + " life=" + LifeRevision + " +" + Added.ToString(CultureInfo.InvariantCulture) +
            " posture=" + PostureAfter.ToString(CultureInfo.InvariantCulture) + (Opened ? " WINDOW " + WindowUs + "us #" + WindowNumber + " total=" + TotalWindowSeconds.ToString(CultureInfo.InvariantCulture) + "s" : "");
    }

    /// <summary>S5-b slice 2 (design sections 5 and 9): projects GroggyRulesV3 (posture 40 / 60 / 10 toward 100; window 2 s x 0.6^(n-1), a
    /// bundle of at most 5 s, the count reset after 6 s without an opening) onto the shared window state of the enemy: the HUD meter
    /// (GroggyMeter.ProjectExternal) and the weak-point window (EnemyVitals.OpenWeakPointFor - same generation, same events, so the
    /// riposte's TryConsumeWeakPointWindow keeps working). The old GroggyMeter step counter is never touched: 40 / 60 / 10 are not steps.
    /// One state per combat scope; targets are keyed by (TargetId, LifeRevision) inside the rules, so a new life starts clean.</summary>
    public sealed class Vol2PostureBridge
    {
        readonly DefenceNumbersV3Dto _n;
        readonly IVol2Clock _clock;
        readonly Vol2TargetBook _targets;
        GroggyRulesV3.State _state;
        long _event;
        public Vol2PostureRecord Last { get; private set; }
        public event Action<EnemyVitals, Vol2PostureRecord> Applied;

        public Vol2PostureBridge(DefenceNumbersV3Dto numbers, IVol2Clock clock, Vol2TargetBook targets)
        {
            _n = numbers ?? throw new ArgumentNullException(nameof(numbers));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _targets = targets ?? throw new ArgumentNullException(nameof(targets));
            _state = new GroggyRulesV3.State(_n);
        }

        /// <summary>A correct-body parry that met a long enough telegraph (ParryRulesV3 said Posture 40).</summary>
        public Vol2PostureRecord ApplyParry(EnemyVitals target) => Apply(target, GroggyRulesV3.Source.Parry, '\0', false, false, true);
        /// <summary>The owner's later direct hit (or 갈무리 swing) on an active ㅏ+ㅁ seed: 60.</summary>
        public Vol2PostureRecord ApplyActiveSeed(EnemyVitals target, char letter, bool direct, bool own) => Apply(target, GroggyRulesV3.Source.ActiveSeed, letter, direct, own, false);
        /// <summary>The 날붓: a melee direct hit of ㅓ+ㅅ that overcomes the target's body: 10.</summary>
        public Vol2PostureRecord ApplyBlade(EnemyVitals target, char letter, bool direct, bool own, bool counter) => Apply(target, GroggyRulesV3.Source.CounterBlade, letter, direct, own, counter);

        public Vol2PostureRecord Apply(EnemyVitals target, GroggyRulesV3.Source source, char letter, bool direct, bool own, bool counter, bool success = true)
        {
            if (target == null || !target.IsAlive) return Record(null, new Vol2PostureRecord(false, "NoTarget", source, 0, 0, 0, 0, 0, 0, 0, false));
            long id = _targets.Id(target), life = target.LifeRevision;
            var step = GroggyRulesV3.Apply(_state, new GroggyRulesV3.Event(++_event, id, source, letter, direct, own, counter, success, life), _clock.NowUs);
            if (!step.Accepted) return Record(target, new Vol2PostureRecord(false, step.Reason, source, id, life, 0, PostureOf(id, life), 0, 0, 0, false));
            _state = step.State;
            var t = _state.Get(id, life);
            bool opened = false;
            if (step.Opened)
            {
                target.Groggy.ProjectExternal(1f);
                opened = target.OpenWeakPointFor(Vol2Time.ToSecondsF(step.WindowUs));
            }
            else target.Groggy.ProjectExternal((float)(t.Posture / _n.PostureThreshold));
            return Record(target, new Vol2PostureRecord(true, step.Reason, source, id, life, step.AddedPosture, t.Posture, step.WindowUs, t.WindowNumber, t.TotalWindowSeconds, opened));
        }

        Vol2PostureRecord Record(EnemyVitals target, Vol2PostureRecord r)
        {
            Last = r;
            Applied?.Invoke(target, r);
            return r;
        }

        public decimal PostureOf(EnemyVitals target) => target == null ? 0m : PostureOf(_targets.Id(target), target.LifeRevision);
        decimal PostureOf(long id, long life) => _state.Get(id, life).Posture;
        public int WindowNumberOf(EnemyVitals target) => target == null ? 0 : _state.Get(_targets.Id(target), target.LifeRevision).WindowNumber;
        public decimal TotalWindowSecondsOf(EnemyVitals target) => target == null ? 0m : _state.Get(_targets.Id(target), target.LifeRevision).TotalWindowSeconds;

        /// <summary>A new encounter / a scene reset: every target's posture and window count start from zero.</summary>
        public void Reset() { _state = new GroggyRulesV3.State(_n); }
    }
}
