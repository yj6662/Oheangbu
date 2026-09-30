using Oheangbu.App.Prologue;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    // #306 속성 기관 예고(SPEC-PLAYTEST-306 #12, D306) — 표현 전용. 속성 공격만 기관 먹 테가 선다(무속성은 절대 빛나지 않는다).
    // 공격 소유자(EnemyController / 청룡 계획 / 남문 계획)의 읽기 전용 충돌 예측을 매 프레임 다시 읽고 되먹이지 않는다.
    //   t_peak = t_impact − PeakLead · t_on = max(t0, t_peak − RiseLead) · 절정 맥동 · 충돌 뒤 먹으로 가라앉음(scaled 시계)
    // 먼 사격은 기관이 볼트까지 끈으로 이어지고 볼트 머리에도 같은 색 먹 테가 선다. 등줄기(Spine) 기관은 배열 순서로 빛이 흐른다.
    // 방어 성공 먹 획(ParryCounterStrokeEffect)이 도착하면 Extinguish로 먹 얼룩이 되어 꺼진다. 먹 테·끈은 이 컴포넌트가 풀로 가진다.
    // 이 컴포넌트는 기관 세트 옆(적 루트 또는 Folklore298 모습 프리팹 루트)에 둔다 — 소유자는 부모 쪽에서 찾는다.
    [DisallowMultipleComponent, DefaultExecutionOrder(900)]
    public sealed class EnemyElementTelegraph : MonoBehaviour
    {
        [SerializeField] private EnemyOrganSet _organs;
        [SerializeField] private EnemyTelegraphTimingSO _timing;
        [SerializeField] private ElementPaletteSO _palette;
        [Tooltip("비우면 EnemyController.PlayerTarget → PrologueEncounter.Player")]
        [SerializeField] private Transform _player;

        private const int MaxLit = 32;
        private static readonly int TintId = Shader.PropertyToID("_Tint"), OpenId = Shader.PropertyToID("_Open"),
            PulseId = Shader.PropertyToID("_Pulse"), BlotId = Shader.PropertyToID("_Blot"), BurstId = Shader.PropertyToID("_Burst"),
            BreakId = Shader.PropertyToID("_Break"), AlphaId = Shader.PropertyToID("_Alpha"), BrightId = Shader.PropertyToID("_Brightness"),
            PaperId = Shader.PropertyToID("_PaperBrightness"), SeedId = Shader.PropertyToID("_Seed");

        private EnemyController _enemy;
        private CheongryongCombatController _dragon;
        private CheongryongAttackPresentation _dragonView;
        private SouthGateGeneralController _general;
        private PrologueEncounter _encounter;
        private bool _bound, _deferred;

        private sealed class Halo { public GameObject Go; public Transform T; public MeshRenderer R; }
        private Halo[] _halos = new Halo[0];
        private Halo _boltHalo;
        private InkRibbon306 _thread;
        private Transform _root;
        private Material _haloMaterial, _ownedHaloMaterial;
        private EnemyTelegraphTimingSO _ownedTiming;
        private MaterialPropertyBlock _block;

        private readonly int[] _lit = new int[MaxLit];
        private int _litCount, _chainCount;
        private long _attackId;
        private Element _element;
        private float _t0, _tImpact, _fadeFrom = float.PositiveInfinity, _extinguishAt = float.NegativeInfinity;
        private bool _ended, _cancelled, _held, _break, _done, _hasBolt;
        private Vector3 _boltPoint;
        private float _seed, _heldSince;
        private bool _haloLooked;

        // 읽기 전용 진단(편집기 검사 AC-12b·c): 지금 보이는 가장 밝은 먹 테(0 = 꺼짐) · 보이는 먹 테 수 · 켜진 공격
        public float LitLevel { get; private set; }
        public int VisibleHaloCount { get; private set; }
        public long LitAttackId => _litCount > 0 && !_done ? _attackId : 0;
        public EnemyOrganSet Organs => _organs;

        public void Configure(EnemyOrganSet organs, EnemyTelegraphTimingSO timing, ElementPaletteSO palette)
        { _organs = organs; _timing = timing; _palette = palette; _bound = false; }

        private void Bind()
        {
            if (_bound) return;
            _bound = true;
            if (_organs == null) _organs = GetComponent<EnemyOrganSet>();
            if (_organs == null) _organs = GetComponentInChildren<EnemyOrganSet>(true);
            if (_timing == null) _timing = Resources.Load<EnemyTelegraphTimingSO>(EnemyTelegraphTimingSO.ResourcePath);
            if (_timing == null) { _timing = _ownedTiming = ScriptableObject.CreateInstance<EnemyTelegraphTimingSO>(); _timing.hideFlags = HideFlags.DontSave; }
            // one telegraph per enemy: a nested species-prefab telegraph (Folklore298_Visual) defers to one authored higher up
            var outer = transform.parent != null ? transform.parent.GetComponentInParent<EnemyElementTelegraph>(true) : null;
            _deferred = outer != null && outer != this;
            _dragon = GetComponentInParent<CheongryongCombatController>();
            _dragonView = _dragon != null ? _dragon.GetComponent<CheongryongAttackPresentation>() : null;
            _general = GetComponentInParent<SouthGateGeneralController>();
            _enemy = GetComponentInParent<EnemyController>();
            _encounter = GetComponentInParent<PrologueEncounter>();
        }

        private Transform Player => _player != null ? _player : _enemy != null && _enemy.PlayerTarget != null ? _enemy.PlayerTarget
            : _encounter != null ? _encounter.Player : null;

        // ---- 방어 성공 먹 획 API(ParryCounterStrokeEffect) ----

        // 획의 끝 = 이 공격으로 빛났던 기관(없으면 가슴·핵 기관, 그다음 첫 기관). 기관 세트가 없으면 false(호출자가 가슴으로 대신한다)
        // lift = how far the visible halo sits in front of the bone (camera side) — the stroke lands there, not inside the body
        public bool TryGetCounterTarget(long attackId, out Transform anchor, out Vector3 localOffset, out float lift)
        {
            Bind(); anchor = null; localOffset = default; lift = 0f;
            if (_organs == null || _organs.Count == 0 || _deferred) return false;
            int index = attackId == _attackId && _litCount > 0 ? Primary() : -1;
            if (index < 0) for (int i = 0; i < _organs.Count; i++) { var o = _organs.Get(i); if (o != null && (o.Role == EnemyOrganRole.Chest || o.Role == EnemyOrganRole.Core)) { index = i; break; } }
            if (index < 0) index = 0;
            var organ = _organs.Get(index); if (organ == null) return false;
            anchor = organ.Anchor != null ? organ.Anchor : _organs.transform; localOffset = organ.LocalOffset;
            lift = organ.Radius + (_timing != null ? _timing.SurfaceOffset : 0f);
            return true;
        }
        // 획이 날아가는 동안 먹 테를 잡아 둔다(가라앉음 대기) — 도착(Extinguish)이나 해제가 반드시 뒤따른다(획의 실시간 상한)
        public void HoldForCounter(long attackId) { if (attackId == _attackId && _litCount > 0 && !_done) { _held = true; _heldSince = Time.unscaledTime; } }
        public void ReleaseCounter(long attackId) { if (attackId == _attackId) _held = false; }
        // 도착: 기관의 속성빛이 먹 얼룩으로 꺼지고 먹 테가 터진다. 개화(3번째)는 고리가 깨진다
        public void Extinguish(long attackId, bool blossom)
        {
            if (attackId != _attackId || _litCount == 0 || _done) return;
            _held = false; _break |= blossom;
            if (float.IsNegativeInfinity(_extinguishAt)) _extinguishAt = Time.time;
        }

        // ---- 소유자 읽기(매 프레임) ----

        private bool Sample(out long id, out Element element, out string key, out float t0, out float impact, out bool bolt, out Vector3 boltPoint)
        {
            id = 0; element = Element.Wood; key = null; t0 = impact = 0f; bolt = false; boltPoint = default;
            float now = Time.time; var player = Player;
            if (_dragon != null && _dragon.isActiveAndEnabled)
            {
                var plan = _dragon.CurrentPlan;
                if (plan == null || plan.IsCancelled || !plan.IsElemental || plan.ContactConsumed) return false;
                id = plan.Attack.AttackId; element = plan.Attack.Element.Value; t0 = plan.StartedAt;
                bool isBolt = plan.Kind == CheongryongAttackKind.WoodProjectile;
                key = isBolt ? EnemyOrganSet.KeyBolt : EnemyOrganSet.KeyRoot;
                impact = plan.PredictImpactTime((player != null ? player.position : plan.TargetPoint) + Vector3.up * .8f);
                if (isBolt && now >= plan.ReleaseAt)
                {
                    bolt = true; var body = _dragonView != null ? _dragonView.ProjectileTransform : null;
                    boltPoint = body != null ? body.position : plan.ProjectilePosition;
                }
                return true;
            }
            if (_general != null && _general.isActiveAndEnabled)
            {
                var plan = _general.CurrentPlan; var pulse = plan != null ? plan.ElementalPulse : null;
                if (plan == null || plan.IsCancelled || pulse == null || pulse.Cancelled || pulse.Consumed) return false;
                id = pulse.Attack.AttackId; element = pulse.Attack.Element.Value; key = EnemyOrganSet.KeyWave;
                t0 = plan.EmpowerStartAt; // 창날·가슴·투구는 흙 준비부터
                impact = plan.PredictImpactTime(pulse, player != null ? player.position : plan.Origin + plan.Direction * pulse.Range);
                if (now >= pulse.ReleaseAt) { bolt = true; boltPoint = plan.WaveFront(pulse, now); }
                return true;
            }
            if (_enemy != null && _enemy.isActiveAndEnabled && (_enemy.IsTelegraphing || _enemy.IsProjectileFlying))
            {
                var attack = _enemy.CurrentAttack;
                if (!attack.Element.HasValue || attack.AttackId <= 0) return false; // 무속성 = 빛나지 않는다
                id = attack.AttackId; element = attack.Element.Value; key = _enemy.AttackOrganKey;
                t0 = _enemy.AttackStartTime; impact = _enemy.PredictedImpactTime;
                var body = _enemy.ProjectileTransform;
                if (body != null) { bolt = true; boltPoint = body.position; }
                return !float.IsNegativeInfinity(impact);
            }
            return false;
        }

        private void LateUpdate()
        {
            Bind();
            if (_organs == null || _timing == null || _deferred) { HideAll(); return; }
            float now = Time.time;
            if (Sample(out long id, out Element element, out string key, out float t0, out float impact, out bool bolt, out Vector3 boltPoint))
            {
                if (id != _attackId) Begin(id, element, key);
                _t0 = t0; _tImpact = impact; _hasBolt = bolt; _boltPoint = boltPoint;
            }
            else if (_attackId != 0 && !_ended)
            {
                // 소유자가 공격을 놓았다: 충돌 시각이 지났으면 자연 가라앉음, 아니면 취소(먹으로 꺼짐)
                _ended = true; _hasBolt = false; _cancelled = now < _tImpact - .05f;
                _fadeFrom = _cancelled ? now : Mathf.Min(now, _tImpact);
            }
            Render(now);
        }

        private void Begin(long id, Element element, string key)
        {
            _attackId = id; _element = element; _ended = _cancelled = _held = _break = _done = false;
            _fadeFrom = float.PositiveInfinity; _extinguishAt = float.NegativeInfinity;
            _litCount = _organs.Collect(key, _lit); _chainCount = 0;
            for (int i = 0; i < _litCount; i++) if (_organs.Get(_lit[i]).Role == EnemyOrganRole.Spine) _chainCount++;
            _seed = (id % 97) * .173f;
        }

        // 획이 닿고 끈이 나오는 기관: 입 > 창날 > 눈 > 가슴·핵 > 등줄기(사슬뿐이면 땅 쪽 마디)
        private int Primary()
        {
            int best = -1, bestRank = int.MaxValue;
            for (int i = 0; i < _litCount; i++)
            {
                var role = _organs.Get(_lit[i]).Role;
                int rank = role == EnemyOrganRole.Mouth ? 0 : role == EnemyOrganRole.Weapon ? 1 : role == EnemyOrganRole.Eye ? 2 : role == EnemyOrganRole.Spine ? 4 : 3;
                if (rank < bestRank || (rank == bestRank && rank == 4)) { best = _lit[i]; bestRank = rank; }
            }
            return best;
        }

        private void Render(float now)
        {
            LitLevel = 0f; VisibleHaloCount = 0;
            if (_attackId == 0 || _litCount == 0 || _done) { HideAll(); return; }
            var t = _timing;
            float peak = _tImpact - t.PeakLead, on = Mathf.Max(_t0, peak - t.RiseLead);
            float rise = peak > on + .001f ? Mathf.Clamp01((now - on) / (peak - on)) : now >= on ? 1f : 0f;
            float pulse = now >= peak && now <= peak + t.PeakPulseSeconds ? Mathf.Sin(Mathf.PI * (now - peak) / t.PeakPulseSeconds) : 0f;
            if (_held && Time.unscaledTime - _heldSince > t.RealtimeCap * 2f) _held = false; // 획이 사라져도 먹 테가 남지 않는다
            float blot = 0f, burst = 0f, brk = 0f;
            if (!float.IsNegativeInfinity(_extinguishAt))
            {
                float p = Mathf.Clamp01((now - _extinguishAt) / t.ExtinguishSeconds);
                blot = p; burst = Mathf.Clamp01(p * 2.5f) * (1f - p); brk = _break ? Mathf.Clamp01(p * 3f) : 0f;
            }
            else if (!_held)
            {
                if (_cancelled) blot = Mathf.Clamp01((now - _fadeFrom) / t.CancelFadeSeconds);
                else if (now > _tImpact) blot = Mathf.Clamp01((now - _tImpact) / t.FadeAfterImpact);
            }
            if (blot >= 1f || now < on) { if (blot >= 1f) _done = true; HideAll(); return; }

            var cam = Camera.main; if (cam == null || !HaloMaterialReady()) { HideAll(); return; }
            EnsurePool(_litCount);
            Color tint = ElementColor(_element);
            float alpha = Mathf.Clamp01(rise * 4f), open = Mathf.SmoothStep(0f, 1f, rise);
            int chain = 0;
            for (int i = 0; i < _halos.Length; i++)
            {
                var halo = _halos[i];
                if (i >= _litCount) { halo.R.enabled = false; continue; }
                var organ = _organs.Get(_lit[i]); float level = 1f;
                if (organ.Role == EnemyOrganRole.Spine && _chainCount > 1)
                {
                    // 등줄기 사슬: t_on에서 첫 마디 → t_peak에 땅 쪽 마디. 마지막 마디는 절정부터 충돌까지 머문다
                    float centre = chain / (float)(_chainCount - 1), w = t.ChainWidth;
                    level = chain == _chainCount - 1 ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rise - centre + w) / w))
                        : rise >= 1f ? 0f : Mathf.Clamp01(1f - Mathf.Abs(rise - centre) / w);
                    chain++;
                }
                float a = alpha * level;
                if (a <= .002f && blot <= 0f) { halo.R.enabled = false; continue; }
                Place(halo, _organs.WorldPoint(_lit[i]), organ.Radius, cam, burst);
                Apply(halo.R, tint, open * level, pulse, blot, burst, brk, a, i);
            }
            // 볼트·파동머리: 같은 색 먹 테 + 기관에서 이어진 끈(충돌까지)
            bool thread = _hasBolt && !_ended && now <= _tImpact && float.IsNegativeInfinity(_extinguishAt);
            if (thread)
            {
                if (_boltHalo == null) _boltHalo = MakeHalo("BoltHalo306");
                Place(_boltHalo, _boltPoint, t.ProjectileHaloRadius, cam, 0f);
                Apply(_boltHalo.R, tint, 1f, pulse, 0f, 0f, 0f, alpha, 31);
                if (_thread == null) _thread = new InkRibbon306("OrganThread306", Root(), t.StrokeMaterial, 252);
                int primary = Primary();
                _thread.Draw(_organs.WorldPoint(primary), _boltPoint, 0f, 1f, t.ThreadWidth, 1f, 0f, .55f, tint, t.StrokeTint, alpha, 0f, cam, 8);
            }
            else { if (_boltHalo != null) _boltHalo.R.enabled = false; _thread?.Hide(); }
        }

        private void Place(Halo halo, Vector3 point, float radius, Camera cam, float burst)
        {
            Vector3 toCam = cam.transform.position - point; float distance = toCam.magnitude;
            Vector3 dir = distance > .001f ? toCam / distance : Vector3.up;
            halo.T.SetPositionAndRotation(point + dir * Mathf.Min(distance * .5f, radius + _timing.SurfaceOffset), Quaternion.LookRotation(-dir, cam.transform.up));
            float parent = Mathf.Max(1e-4f, Mathf.Abs(Root().lossyScale.x)); // 적 루트 크기와 무관한 월드 크기
            halo.T.localScale = Vector3.one * (radius * 2f * _timing.HaloScale * (1f + burst * _timing.OrganBurstScale) / parent);
        }

        private void Apply(MeshRenderer r, Color tint, float open, float pulse, float blot, float burst, float brk, float alpha, int index)
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            _block.SetColor(TintId, tint); _block.SetFloat(OpenId, open); _block.SetFloat(PulseId, pulse); _block.SetFloat(BlotId, blot);
            _block.SetFloat(BurstId, burst); _block.SetFloat(BreakId, brk); _block.SetFloat(AlphaId, alpha);
            _block.SetFloat(BrightId, Mathf.Min(1f, _timing.MaxBrightness)); _block.SetFloat(PaperId, Mathf.Min(1f, _timing.PaperBrightness));
            _block.SetFloat(SeedId, _seed + index * 1.37f);
            r.SetPropertyBlock(_block); r.enabled = true;
            float shown = alpha * (1f - Mathf.Clamp01((blot - .55f) / .45f));
            if (shown > LitLevel) LitLevel = shown; VisibleHaloCount++;
        }

        private Color ElementColor(Element element)
        {
            if (_palette == null) return new Color(.45f, .43f, .41f);
            char initial = element == Element.Fire ? 'ㄴ' : element == Element.Earth ? 'ㅁ' : element == Element.Metal ? 'ㅅ' : element == Element.Water ? 'ㅇ' : 'ㄱ';
            return _palette.GetBaseColor(initial); // 팔레트가 단일 출처(ART-COLOR 번역)
        }

        // ---- 풀 ----

        private Transform Root()
        {
            if (_root != null) return _root;
            var go = new GameObject("Telegraph306") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false); _root = go.transform; return _root;
        }

        private void EnsurePool(int count)
        {
            if (_halos.Length >= count) return;
            var grown = new Halo[Mathf.Min(MaxLit, Mathf.Max(count, _halos.Length * 2))];
            for (int i = 0; i < grown.Length; i++) grown[i] = i < _halos.Length ? _halos[i] : MakeHalo("OrganHalo306_" + i);
            _halos = grown;
        }

        // no material = no halo (a null sharedMaterial would draw the magenta error shader — never a fake glow)
        private bool HaloMaterialReady()
        {
            if (_haloMaterial != null) return true;
            _haloMaterial = _timing.HaloMaterial;
            if (_haloMaterial == null && !_haloLooked)
            { _haloLooked = true; var shader = Shader.Find("Oheangbu/InkOrganHalo"); if (shader != null) _haloMaterial = _ownedHaloMaterial = new Material(shader) { hideFlags = HideFlags.DontSave }; }
            return _haloMaterial != null;
        }

        private Halo MakeHalo(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = name; go.hideFlags = HideFlags.DontSave;
            // now, not end of frame: a live MeshCollider under the enemy would join its compound collider / raycasts for a frame
            var collider = go.GetComponent<Collider>(); if (collider != null) DestroyImmediate(collider);
            go.transform.SetParent(Root(), false);
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = _haloMaterial; r.enabled = false;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return new Halo { Go = go, T = go.transform, R = r };
        }

        private void HideAll()
        {
            LitLevel = 0f; VisibleHaloCount = 0;
            foreach (var halo in _halos) if (halo != null && halo.R != null) halo.R.enabled = false;
            if (_boltHalo != null && _boltHalo.R != null) _boltHalo.R.enabled = false;
            _thread?.Hide();
        }

        private void OnDisable() { _attackId = 0; _litCount = 0; _held = false; HideAll(); }
        private void OnDestroy()
        {
            _thread?.Dispose(); _thread = null;
            if (_root != null) Destroy(_root.gameObject);
            if (_ownedHaloMaterial != null) Destroy(_ownedHaloMaterial);
            if (_ownedTiming != null) Destroy(_ownedTiming);
        }
    }
}
