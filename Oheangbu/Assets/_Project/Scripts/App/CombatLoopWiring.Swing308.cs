using System;
using Oheangbu.Combat;
using Oheangbu.Data.Spell;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // SPEC-SPELL-SWING-308 (D308-80): the swing between the release and the launch.
    //  - Release still confirms the cast (recognition, hold power, cast hooks: all taken at release, so the power is the release's).
    //  - A waiting category is kept here, one at a time, and handed to the unchanged dispatch at the apex: ink, aim, plan and the
    //    stroke adapter's pushes all happen then, in one frame, exactly as they did at release before. One clock for damage and
    //    presentation (SPEC-SPELL-DEPLOY-308 section 2) is kept because nothing is planned early.
    //  - Until the lock ends the drawing gate is closed (WorldMacroCombatWalker reads SwingLocked308). Movement, look, dodge and
    //    jump are not touched.
    //  - A dodge that starts before the apex drops the cast: nothing is launched, the ink is still paid.
    //  - No profile = none of this runs.
    public sealed partial class CombatLoopWiring
    {
        [Header("#308 휘두르기 (SPEC-SPELL-SWING-308) — 비우면 Resources, 그것도 없으면 예전 동작")]
        [SerializeField] private SpellSwingProfile308 _swing308;

        private bool _swingLooked, _swingPending, _swingDodging;
        private SpellCast _swingCast;
        private SpellRow _swingRow;
        private float _swingReleasedAt, _swingApexAt, _swingLockUntil = float.NegativeInfinity;

        /// <summary>The drawing mode may not be entered: a swing is still running.</summary>
        public bool SwingLocked308 => _swingPending || Time.time < _swingLockUntil;
        public bool SwingPending308 => _swingPending;
        /// <summary>(cast, state name, apex time, lock end). Raised at release for every category that has an entry. Read only.</summary>
        public event Action<SpellCast, string, float, float> SwingBegan308;
        /// <summary>(cast, seconds from release to launch). Raised right after the dispatch at the apex.</summary>
        public event Action<SpellCast, float> SwingLaunched308;
        /// <summary>(cast). The waiting cast was dropped (dodge before the apex, death, the wiring switched off).</summary>
        public event Action<SpellCast> SwingFizzled308;

        private SpellSwingProfile308 Swing308()
        {
            if (_swing308 == null && !_swingLooked)
            {
                _swingLooked = true;
                _swing308 = Resources.Load<SpellSwingProfile308>("Swing308/SpellSwingProfile308");
            }
            return _swing308 != null && _swing308.IsValid ? _swing308 : null;
        }

        private static bool SwingEntry308(SpellSwingProfile308 profile, in SpellCast cast, SpellRow row, out SpellSwingProfile308.Entry entry, out bool waits)
        {
            waits = false; entry = default;
            switch (row != null ? row.Category : SpellCategory.Blank)
            {
                case SpellCategory.Attack: entry = cast.Kind == SpellKind.AttackArea ? profile.AttackArea : profile.AttackSingle; waits = true; break;
                case SpellCategory.Summon: entry = profile.Summon; waits = true; break;
                case SpellCategory.Field: entry = profile.Field; waits = true; break;
                case SpellCategory.Install: entry = profile.Install; waits = true; break;
                case SpellCategory.Parry: entry = profile.Parry; break;
                case SpellCategory.Ward: entry = profile.Ward; break;
                case SpellCategory.Buff: entry = profile.Buff; break;
                default: return false;
            }
            if (row != null)
            {
                entry.ApexSeconds = Mathf.Max(0f, row.F("swing_apex", entry.ApexSeconds));
                entry.LockSeconds = Mathf.Max(0f, row.F("swing_lock", entry.LockSeconds));
            }
            if (!waits) entry.ApexSeconds = 0f;
            waits = waits && entry.ApexSeconds > 0f;
            return true;
        }

        // The ink one cast of this row asks for, as the dispatch will ask (the combat summon's double cost aside: that is only
        // known after its Prepare). Used for the early "not enough ink" answer and for the price of a dropped cast.
        private float SwingCost308(in SpellCast cast, SpellRow row)
        {
            float cost = _config != null ? _config.SpellInkCost : 0f;
            if (row != null && SpellTakeover308.UsesHandler(row, _spells308 != null && _spells308.Has(row.Handler))) cost *= Mathf.Max(0f, row.F("cost", 1f));
            return cost * (EABuffs?.CostScale(Time.time) ?? 1f) * CostScale308(cast);
        }

        // Called at release with the cast as it will be judged. True = kept for the apex (the caller does nothing more).
        private bool TryBeginSwing308(in SpellCast cast, SpellRow row)
        {
            var profile = Swing308();
            if (profile == null || !SwingEntry308(profile, cast, row, out var entry, out bool waits)) return false;
            float now = Time.time;
            // not enough ink: the refusal is immediate, as before, with no swing and no lock
            if (waits && (_ink == null || _ink.Value < SwingCost308(cast, row))) return false;
            _swingReleasedAt = now; _swingApexAt = now + entry.ApexSeconds;
            _swingLockUntil = now + Mathf.Max(entry.LockSeconds, entry.ApexSeconds);
            _swingDodging = _motor != null && _motor.IsDodging;
            if (waits)
            {
                _swingPending = true; _swingCast = cast; _swingRow = row;
                _brushAdapter?.HoldCommit308();
            }
            SwingBegan308?.Invoke(cast, entry.State, _swingApexAt, _swingLockUntil);
            return waits;
        }

        private void TickSwing308()
        {
            bool dodging = _motor != null && _motor.IsDodging;
            bool dodgeStarted = dodging && !_swingDodging;
            _swingDodging = dodging;
            if (!_swingPending)
            {
                if (dodgeStarted) _swingLockUntil = float.NegativeInfinity;   // after the apex a dodge only ends the follow-through
                return;
            }
            bool gone = _playerVitals != null && _playerVitals.Hp01 <= 0f || _motor != null && (_motor.InputBlocked || !_motor.isActiveAndEnabled);
            if (dodgeStarted || gone) { DropSwing308(true); return; }
            if (Time.time < _swingApexAt) return;
            var cast = _swingCast; var row = _swingRow;
            _swingPending = false; _swingRow = null;
            try { Dispatch308(cast, row); }
            finally { _brushAdapter?.ReleaseCommit308(); }
            SwingLaunched308?.Invoke(cast, Time.time - _swingReleasedAt);
        }

        private void SwingHit308(float damage)
        {
            if (!_swingPending || _swing308 == null || _playerVitals == null) return;
            float share = _swing308.LargeHitFizzleShare;
            if (share > 0f && damage >= share * _playerVitals.MaxHp) DropSwing308(true);
        }

        private void DropSwing308(bool pay)
        {
            if (!_swingPending) return;
            var cast = _swingCast; var row = _swingRow;
            _swingPending = false; _swingRow = null; _swingLockUntil = float.NegativeInfinity;
            if (pay) _ink?.SpendClamped(SwingCost308(cast, row));
            _brushAdapter?.NotifyCastFailed();
            _brushAdapter?.ReleaseCommit308();
            SwingFizzled308?.Invoke(cast);
        }
    }
}
