using System;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    public enum MineBossMove { None, Charge, Sweep, Shard, CrystalRing, Combo }

    // #306 #11 폐광 튜토리얼 보스(인간형 요괴, D306 보강 2026-10-02)의 기술 순서 지정기(SPEC-PLAYTEST-306 #11, D306, TEST).
    // 전투 규칙의 주인은 그대로 EnemyController(저작 공격 프로필)다: 예고·판정·패링·피해·그로기·기관 발광·리그·소리는
    // 기존 배선을 탄다. 이 컴포넌트는 (1) 공격 사이에만 다음 기술의 프로필을 고르고 (2) 감독이 요청하면 새 공격과 이동을
    // 멈추며(보스 자기 시계만 멈춘다 — timeScale은 건드리지 않는다) (3) 파편을 플레이어 앞에서 기다리게 할 뿐이다.
    [DisallowMultipleComponent, RequireComponent(typeof(EnemyController))]
    public sealed class MineBossController : MonoBehaviour
    {
        [SerializeField] MineBossProfileSO _profile;
        EnemyController _enemy;
        EnemyVitals _vitals;
        PrologueEncounter _encounter;
        MineBossMove _forced, _armed, _current, _last, _beforeLast;
        bool _comboFollow, _hold, _bound;
        float _holdSince = float.NegativeInfinity;
        System.Random _random = new System.Random(306011);

        public MineBossProfileSO Profile => _profile;
        public EnemyController Enemy => _enemy;
        public EnemyVitals Vitals => _vitals;
        public MineBossMove Current => _current;
        public MineBossMove Armed => _armed;
        /// <summary>The weak point opened at least once this life (unlocks M5).</summary>
        public bool Groggied { get; private set; }
        public int MovesStarted { get; private set; }
        /// <summary>Director gate for the shard wait (true = keep waiting). The 6 s cap is applied here.</summary>
        public Func<bool> ShardHoldGate { get; set; }
        public bool ShardHeld => _enemy != null && _enemy.FlightHeld;

        public event Action<MineBossMove> MoveTelegraphed;
        public event Action<MineBossMove, EnemyAttackImpact> MoveImpact;
        public event Action<MineBossMove, bool> MoveEnded;
        public event Action GroggyFilled;
        public event Action StunEnded;

        /// <summary>Boss-only wait: no new attack, no chase. An attack already under way finishes.</summary>
        public bool Hold
        {
            get => _hold;
            set { _hold = value; Apply(); }
        }

        public void ConfigureProfile(MineBossProfileSO profile)
        {
            if (profile != null && !profile.TryValidate(out var error)) throw new ArgumentException(error, nameof(profile));
            _profile = profile; _armed = MineBossMove.None;
        }

        /// <summary>The next move the boss starts (director script). None = back to the boss's own choice.</summary>
        public void Force(MineBossMove move)
        {
            _forced = move;
            if (_armed != MineBossMove.None && _armed != move && move != MineBossMove.None) _armed = MineBossMove.None;
        }

        public void ResetEncounter()
        {
            _forced = _armed = _current = _last = _beforeLast = MineBossMove.None; _comboFollow = false;
            Groggied = false; MovesStarted = 0; _hold = false; Apply();
        }

        void Awake() { Bind(); }
        void OnEnable() { Bind(); Apply(); }
        void OnDisable() { Unbind(); _armed = MineBossMove.None; _current = MineBossMove.None; }
        void OnDestroy() { Unbind(); }

        void Bind()
        {
            if (_enemy == null) _enemy = GetComponent<EnemyController>();
            if (_vitals == null) _vitals = GetComponent<EnemyVitals>();
            if (_encounter == null) _encounter = GetComponent<PrologueEncounter>();
            if (_bound || _enemy == null) return;
            _bound = true;
            _enemy.AttackTelegraphed += OnTelegraphed;
            _enemy.AttackImpactResolved += OnImpact;
            _enemy.AttackPresentationEnded += OnEnded;
            _enemy.StunEnded += OnStunEnded;
            if (_vitals != null) { _vitals.WeakPointOpened += OnWeakPoint; _vitals.Died += OnDied; }
            _enemy.FlightHold = ShardGate;
        }

        void Unbind()
        {
            if (!_bound) return;
            _bound = false;
            if (_enemy != null)
            {
                _enemy.AttackTelegraphed -= OnTelegraphed; _enemy.AttackImpactResolved -= OnImpact;
                _enemy.AttackPresentationEnded -= OnEnded; _enemy.StunEnded -= OnStunEnded;
                _enemy.FlightHold = null; _enemy.AttackHeld = false;
            }
            if (_vitals != null) { _vitals.WeakPointOpened -= OnWeakPoint; _vitals.Died -= OnDied; }
            if (_encounter != null) _encounter.HoldPosition = false;
        }

        void Apply()
        {
            if (_enemy != null) _enemy.AttackHeld = _hold;
            if (_encounter != null) _encounter.HoldPosition = _hold;
        }

        bool ShardGate()
        {
            if (_profile == null || ShardHoldGate == null) return false;
            float now = Time.unscaledTime;
            if (!_enemy.FlightHeld) _holdSince = now;
            if (now - _holdSince >= _profile.ShardHoldMaxSeconds) return false;
            return ShardHoldGate();
        }

        void Update()
        {
            if (_profile == null || _enemy == null || _vitals == null || !_vitals.IsAlive) return;
            _enemy.FlightHoldDistance = _profile.ShardHoldDistance;
            Apply();
            if (_armed != MineBossMove.None || _enemy.AttackInProgress || _enemy.IsStunned) return;
            MineBossMove next;
            float cooldown = _profile.Cooldown;
            if (_comboFollow) { next = MineBossMove.Shard; cooldown = _profile.ComboGap; }
            else if (_forced != MineBossMove.None) { next = _forced; cooldown = Mathf.Min(cooldown, .6f); }
            else next = Choose();
            var attack = ProfileOf(next == MineBossMove.Combo ? MineBossMove.Charge : next);
            if (attack == null || !_enemy.TrySetIdleProfile(attack, cooldown)) return;
            _armed = next;
            if (_encounter != null) _encounter.PreferredDistance = DistanceOf(next);
        }

        MineBossMove Choose()
        {
            var player = _enemy.PlayerTarget;
            float d = player != null ? Vector3.Distance(transform.position, player.position) : 99f;
            double r = _random.NextDouble();
            MineBossMove pick;
            if (Groggied && d <= _profile.Charge.Range && r < _profile.ComboChance) pick = MineBossMove.Combo;
            else if (d > _profile.Charge.Range) pick = r < .6 ? MineBossMove.Shard : MineBossMove.CrystalRing;
            else if (d <= _profile.Sweep.Range && r < .4) pick = MineBossMove.Sweep;
            else pick = r < .75 ? MineBossMove.Charge : MineBossMove.CrystalRing;
            // never the same move three times in a row
            if (pick == _last && pick == _beforeLast) pick = pick == MineBossMove.Shard ? MineBossMove.Charge : MineBossMove.Shard;
            return pick;
        }

        EnemyAttackProfileSO ProfileOf(MineBossMove move)
        {
            switch (move)
            {
                case MineBossMove.Charge: return _profile.Charge;
                case MineBossMove.Sweep: return _profile.Sweep;
                case MineBossMove.Shard: return _profile.Shard;
                case MineBossMove.CrystalRing: return _profile.CrystalRing;
                default: return null;
            }
        }

        float DistanceOf(MineBossMove move) =>
            move == MineBossMove.Shard ? _profile.ShardDistance : move == MineBossMove.CrystalRing ? _profile.RingDistance : _profile.MeleeDistance;

        void OnTelegraphed(EnemyAttackCue cue)
        {
            bool follow = _comboFollow;
            _current = follow ? MineBossMove.Shard : _armed == MineBossMove.Combo ? MineBossMove.Charge : _armed;
            _comboFollow = !follow && _armed == MineBossMove.Combo;
            if (_forced != MineBossMove.None && _armed == _forced) _forced = MineBossMove.None;
            _beforeLast = _last; _last = _armed; _armed = MineBossMove.None; MovesStarted++;
            Raise(MoveTelegraphed, _current);
        }

        void OnImpact(EnemyAttackImpact impact)
        {
            var handler = MoveImpact; if (handler == null) return;
            try { handler(_current, impact); } catch (Exception e) { Debug.LogException(e, this); }
        }

        void OnEnded(AttackProvenance attack, bool cancelled)
        {
            var move = _current; _current = MineBossMove.None;
            if (cancelled) _comboFollow = false;
            var handler = MoveEnded; if (handler == null) return;
            try { handler(move, cancelled); } catch (Exception e) { Debug.LogException(e, this); }
        }

        void OnWeakPoint() { Groggied = true; _comboFollow = false; _armed = MineBossMove.None; Raise(GroggyFilled); }
        void OnStunEnded() { Raise(StunEnded); }
        void OnDied() { _armed = _current = MineBossMove.None; _comboFollow = false; _hold = false; Apply(); }

        void Raise(Action<MineBossMove> handler, MineBossMove move)
        { if (handler == null) return; try { handler(move); } catch (Exception e) { Debug.LogException(e, this); } }
        void Raise(Action handler)
        { if (handler == null) return; try { handler(); } catch (Exception e) { Debug.LogException(e, this); } }
    }
}
