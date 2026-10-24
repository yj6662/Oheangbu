using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // S5-b slice 2 (D308-149, design sections 5 and 9): the combat wiring's side of the vol.2 defence kit. This partial owns the hooks and
    // nothing else:
    //   - binds the kit (ink payment gate on the adapter, the vol.2 impact resolver on the ParryJudge, the enemy telegraph cues, the 걱 gate context),
    //   - ticks it from Update, ends the recovery when the player is hit / dies,
    //   - gives the swing's contact collector (slice 3) its two entries (first enemy met / air).
    // The rules live in Combat/Vol2 and App/Vol2/Defence. Without a Vol2 route AND an ink pool (Legacy308, a bare edit-mode wiring) nothing is
    // bound and every line of the old wiring behaves as before.
    public sealed partial class CombatLoopWiring
    {
        private Vol2DefenceKit _vol2Defence;
        private bool _vol2Hooked;
        private readonly List<EnemyController> _vol2HookedControllers = new List<EnemyController>();

        /// <summary>The bound defence kit; null in Legacy308 or before the first frame of a Vol2 scope.</summary>
        public Vol2DefenceKit Vol2Defence => _vol2Defence;

        /// <summary>True while the vol.2 parry resolver stands on the judge: the old parry rewards (ink refund, AddParry) must not run.</summary>
        private bool Vol2DefenceActive => _vol2Defence != null && _judge != null && _judge.Vol2Resolver != null;

        /// <summary>Builds and binds the kit from the route's adapter, the ink pool and the player. Idempotent. null when the route is not Vol2 or
        /// there is no ink pool. Called by Vol2DefenceInstaller (build callback, which also binds the coordinator's ports) or, if that line is
        /// absent, by the wiring itself (then the gate handles the parry and the 걱 recovery only).</summary>
        public Vol2DefenceKit BindVol2Defence(Vol2SessionLedger session = null, float hpPerUnit = Vol2Units.DefaultHpPerUnit, bool paysOrdinaryCasts = false)
        {
            if (_vol2Defence != null) return _vol2Defence;
            var adapter = _spellRoute != null && _spellRoute.IsVol2 ? _spellRoute.Adapter : null;
            if (adapter == null || _ink == null || adapter.Numbers == null || !adapter.Numbers.IsValidated) return null;
            _vol2Defence = new Vol2DefenceKit(adapter.Numbers, adapter.Clock, _ink, _playerVitals, session, new Vol2WiringContext(this), hpPerUnit);
            // Bound alone (no installer line) the kit leaves the ink of ordinary casts to the shape coordinator's own spender, so nothing is charged
            // twice; the installer turns it on together with the coordinator's PaidInk port.
            _vol2Defence.PaysOrdinaryCasts = paysOrdinaryCasts;
            adapter.Payment = _vol2Defence.TryPay;
            HookVol2Defence();
            return _vol2Defence;
        }

        private void EnsureVol2Defence()
        {
            if (_vol2Defence == null && _spellRoute != null && _spellRoute.IsVol2) BindVol2Defence();
        }

        private void HookVol2Defence()
        {
            if (_vol2Defence == null || _vol2Hooked) return;
            if (_judge != null) _judge.Vol2Resolver = _vol2Defence.Parry;
            foreach (var controller in _controllers)
            {
                if (controller == null || _vol2HookedControllers.Contains(controller)) continue;
                controller.AttackTelegraphed += OnVol2Telegraph;
                _vol2HookedControllers.Add(controller);
            }
            _vol2Hooked = true;
        }

        private void UnhookVol2Defence()
        {
            if (!_vol2Hooked) return;
            if (_judge != null && _vol2Defence != null && ReferenceEquals(_judge.Vol2Resolver, _vol2Defence.Parry)) _judge.Vol2Resolver = null;
            foreach (var controller in _vol2HookedControllers) if (controller != null) controller.AttackTelegraphed -= OnVol2Telegraph;
            _vol2HookedControllers.Clear();
            _vol2Hooked = false;
        }

        private void TickVol2Defence()
        {
            EnsureVol2Defence();
            if (_vol2Defence == null) return;
            if (!_vol2Hooked) HookVol2Defence();
            _vol2Defence.Tick();
        }

        // the cue carries what ResolveImpact cannot tell: projectile or not, and the length of the telegraph (AttackId is the key)
        private void OnVol2Telegraph(EnemyAttackCue cue) { _vol2Defence?.Telegraphs.Record(cue); }

        private void OnVol2PlayerDamaged(float damage) { _vol2Defence?.Recovery.OnPlayerDamaged(damage); }

        private void OnVol2PlayerDied() { _vol2Defence?.OnPlayerDied(); }

        /// <summary>A rest (lantern, bonfire) happened: the out-of-combat 걱 budget (HP 30) is whole again. Nothing in the game raises a rest
        /// event yet; the owner of resting calls this.</summary>
        public void NotifyVol2Rest() { _vol2Defence?.Recovery.NotifyRest(); }

        /// <summary>The swing (slice 3's contact collector) met its first enemy: 갈무리. Once per SwingId. Empty record when no kit is bound.</summary>
        public Vol2GatherRecord ReportVol2SwingFirstHit(EnemyVitals target, SwingId swing)
        {
            EnsureVol2Defence();
            return _vol2Defence != null ? _vol2Defence.Gather.OnSwingFirstHit(target, swing, _playerVitals) : default;
        }

        /// <summary>The swing met no enemy.</summary>
        public Vol2GatherRecord ReportVol2SwingMissed(SwingId swing)
        {
            EnsureVol2Defence();
            return _vol2Defence != null ? _vol2Defence.Gather.OnSwingMissed(swing) : default;
        }

        // TEST approximations of the 걱 gate until the game has real encounter / boss-phase ids (reported as open):
        //   Engaged     = the same "in combat" the ink regeneration uses (a lock-on target, or a hit on the player within the linger time)
        //   window      = the lock-on target's, else the engaged boss's, else any living target's open weak-point window
        //   WindowId    = (target, life, WeakPointGeneration): one id per opening
        //   EncounterId = (target, life) of the window's owner, else of the lock-on target / engaged boss; BossPhaseId = 0 (no phase source yet)
        private sealed class Vol2WiringContext : IVol2CombatContext
        {
            readonly CombatLoopWiring _w;
            public Vol2WiringContext(CombatLoopWiring wiring) { _w = wiring; }

            public bool Engaged
            {
                get
                {
                    if (_w._config == null) return false;
                    return _w._lockOn != null && _w._lockOn.Target != null || Time.time - _w._lastPlayerHitTime < _w._config.InkRegenCombatLinger;
                }
            }

            EnemyVitals WindowOwner()
            {
                var locked = _w._lockOn != null ? _w._lockOn.Target : null;
                if (locked != null && locked.IsAlive && locked.WeakPointActive) return locked;
                var boss = _w._engagedBoss;
                if (boss != null && boss.IsAlive && boss.WeakPointActive) return boss;
                foreach (var target in _w._targets) if (target != null && target.IsAlive && target.isActiveAndEnabled && target.WeakPointActive) return target;
                return null;
            }

            public bool WeakWindowOpen => WindowOwner() != null;
            public long BossPhaseId => 0;

            public long WindowId
            {
                get
                {
                    var owner = WindowOwner(); var kit = _w._vol2Defence;
                    return owner == null || kit == null ? 0 : kit.Keys.Id("w:" + kit.Targets.Id(owner) + ":" + owner.LifeRevision + ":" + owner.WeakPointGeneration);
                }
            }

            public long EncounterId
            {
                get
                {
                    var kit = _w._vol2Defence;
                    var owner = WindowOwner() ?? (_w._lockOn != null ? _w._lockOn.Target : null) ?? _w._engagedBoss;
                    return owner == null || kit == null ? 0 : kit.Keys.Id("e:" + kit.Targets.Id(owner) + ":" + owner.LifeRevision);
                }
            }
        }
    }
}
