using System;
using UnityEngine;

namespace Oheangbu.Combat
{
    public enum HarvestState { Idle, Pulling, Cooldown }
    public enum HarvestCancelReason { None, Dodge, Hit, Drawing, InputBlocked, TargetLost, OutOfRange, Disabled }

    // 갈무리 — COMBAT-HARVEST(D306 [TEST] 덩어리 뽑기): 락온 대상 한정 · 사거리 내 · 좌클릭 한 번.
    // 대기 → 뽑는 중(PullSeconds) → 끊기(피해 1회·수입 1회) → 냉각. #132 홀드의 매 프레임 피해·반응은 없앴다
    // (연속 피해가 적을 매 프레임 피격 자세로 묶었다). 취소=수입 0. 「갈무리만으로 처치 불가」 — HP 1 바닥.
    // 그로기는 쌓지 않는다(COMBAT-GROGGY): TakeDamage는 그로기를 건드리지 않는다.
    public sealed class HarvestAction : MonoBehaviour
    {
        [SerializeField] private CombatConfigSO _config;
        [SerializeField] private LockOn _lockOn;
        [Tooltip("피격 취소용 — 비우면 같은 오브젝트에서 찾는다")]
        [SerializeField] private PlayerVitals _vitals;

        private InkPool _ink;
        private EnemyVitals _target;
        private uint _targetLife;
        private float _pullStart, _cooldownUntil = float.NegativeInfinity;
        private EnemyVitals _chunkTarget;
        private uint _chunkLife;
        private int _chunkCount;
        // 편집기 자세 진단(ReRig QA) 전용 대리 신호 — 런타임은 쓰지 않는다. int.MinValue 금지(frameCount 뺄셈 오버플로)
        private int _lastExtractFrame = -1000;

        public HarvestState State { get; private set; }
        // 표현 계층(App) 읽기 전용 신호 — 뽑는 중에만 참. 판정·수치는 여기서 끝나고 연출은 읽기만 한다(인식·필세 불가침과 같은 결)
        public bool IsExtracting => State == HarvestState.Pulling || Time.frameCount - _lastExtractFrame <= 2;
        public EnemyVitals PullTarget => State == HarvestState.Pulling ? _target : null;
        public float PullProgress01 => State != HarvestState.Pulling || _config == null ? 0f : Mathf.Clamp01((Time.time - _pullStart) / _config.HarvestPullSeconds);
        public float CooldownRemaining => Mathf.Max(0f, _cooldownUntil - Time.time);
        public Vector3 ExtractSourcePosition { get; private set; }
        // 끊기의 Gain 호출 동안만 참 — InkPool.Gained 구독자가 「덩어리 수입」을 알아보는 읽기 전용 신호
        public bool PayingChunk { get; private set; }
        // 끊기 피해에 적 반응(피격 자세·소리) 한 번을 허용하는가 — 뽑는 쪽 설정이 정본(EnemyVitals.ReactsTo가 Instigator로 읽는다)
        public bool ChunkReaction => _config == null || _config.HarvestChunkReaction;
        // 이펙트 훅(배치 2 덩어리 VFX가 붙는다): 시작(대상·가슴 위치) / 끊기(대상·위치·실수입) / 취소(사유)
        public event Action<EnemyVitals, Vector3> PullStarted;
        public event Action<EnemyVitals, Vector3, float> ChunkExtracted;
        public event Action<HarvestCancelReason> PullCanceled;
        // 구 신호 호환 — 이제 덩어리를 끊는 순간 한 번만 발화한다(매 프레임 아님)
        public event Action<Vector3> Extracted;

        public void Init(InkPool ink)
        {
            _ink = ink;
        }

        private void Awake() { if (_vitals == null) _vitals = GetComponent<PlayerVitals>(); }
        private void OnEnable() { if (_vitals != null) _vitals.Damaged += OnDamaged; }
        private void OnDisable()
        {
            if (_vitals != null) _vitals.Damaged -= OnDamaged;
            Cancel(HarvestCancelReason.Disabled); // 냉각은 유지 — 끄고 켜기로 냉각을 건너뛰지 않는다(Update·TryBeginPull이 시각으로 푼다)
        }
        private void OnDamaged(float _) => Cancel(HarvestCancelReason.Hit);

        // 입력 계층(PlayerMotor)이 좌클릭 누름 프레임에 부른다. 게이트를 못 넘으면 아무 일도 없다(냉각 중 연타 포함)
        public bool TryBeginPull()
        {
            if (_config == null || _lockOn == null || !isActiveAndEnabled) return false;
            if (State == HarvestState.Pulling) return false;
            if (State == HarvestState.Cooldown && Time.time < _cooldownUntil) return false;
            var target = _lockOn.Target;
            if (!Eligible(target)) return false;
            if (_config.HarvestChunksPerLife > 0 && _chunkTarget == target && _chunkLife == target.LifeRevision
                && _chunkCount >= _config.HarvestChunksPerLife) return false;
            _target = target; _targetLife = target.LifeRevision; _pullStart = Time.time;
            State = HarvestState.Pulling; UpdateSource();
            PullStarted?.Invoke(_target, ExtractSourcePosition);
            return true;
        }

        public void Cancel(HarvestCancelReason reason)
        {
            if (State != HarvestState.Pulling) return;
            State = HarvestState.Idle; _target = null;
            PullCanceled?.Invoke(reason);
        }

        private bool Eligible(EnemyVitals target) => target != null && target.IsAlive && target.isActiveAndEnabled
            && Vector3.Distance(transform.position, target.transform.position) <= _config.HarvestRange;

        private void Update()
        {
            if (State == HarvestState.Cooldown && Time.time >= _cooldownUntil) State = HarvestState.Idle;
            if (State != HarvestState.Pulling) return;
            if (_config == null || _target == null || !_target.IsAlive || !_target.isActiveAndEnabled || _target.LifeRevision != _targetLife
                || _lockOn == null || _lockOn.Target != _target) { Cancel(HarvestCancelReason.TargetLost); return; }
            if (Vector3.Distance(transform.position, _target.transform.position) > _config.HarvestRange) { Cancel(HarvestCancelReason.OutOfRange); return; }
            UpdateSource();
            if (Time.time - _pullStart >= _config.HarvestPullSeconds) Extract();
        }

        // 끊기 — 피해 1회(HP 1 바닥, Harvest 출처)와 수입 1회. 적 반응은 이 피해 한 번에서만 난다
        private void Extract()
        {
            var target = _target;
            State = HarvestState.Cooldown; _cooldownUntil = Time.time + _config.HarvestCooldown; _target = null;
            target.TakeDamage(_config.HarvestChunkDamage, AttackProvenance.Create(this, DamageSource.Harvest, null), floorAtOneHp: true);
            if (_chunkTarget != target || _chunkLife != _targetLife) { _chunkTarget = target; _chunkLife = _targetLife; _chunkCount = 0; }
            _chunkCount++;
            float before = _ink != null ? _ink.Value : 0f;
            PayingChunk = true; // 표현 계층이 이 Gained를 덩어리 흡수 순간으로 미룰 수 있게(값은 즉시)
            try { _ink?.Gain(_config.HarvestChunkInk); } // 먹 수급 — 교전·락온에 묶인 수입(먹 3원)
            finally { PayingChunk = false; }
            float received = _ink != null ? _ink.Value - before : 0f;
            ChunkExtracted?.Invoke(target, ExtractSourcePosition, received);
            Extracted?.Invoke(ExtractSourcePosition);
        }

        private void UpdateSource() => ExtractSourcePosition = _target.transform.position + Vector3.up * 1.1f; // 가슴 높이 — 먹이 뽑히는 자리
    }
}
