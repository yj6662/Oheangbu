using Oheangbu.App.Prologue;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    // #306 속성 기관 예고(SPEC-PLAYTEST-306 #12, D306) → #308 모델 기관 표면 발광(SPEC-TELEGRAPH-ORGAN-308, D308-4) — 표현 전용.
    // 속성 공격만 기관이 빛난다(무속성은 절대 빛나지 않는다). 새 오브젝트를 띄우지 않는다: 쿼드 먹 테·볼트 먹 테·기관→볼트 끈은 없다.
    // 빛은 적 모델의 기관 표면에만 선다 — 기관이 가리키는 렌더러에 투명 덧칠(OrganSurfaceOverlay, Oheangbu/InkOrganSurface)을
    // 예고 창 안에서만 붙인다: 먹 고리가 물러나며 한지가 드러나고 가운데 속성색 심이 선다(LDR ≤ MaxBrightness, 블룸 없음).
    // 공격 소유자(EnemyController / 청룡 계획 / 남문 계획)의 읽기 전용 충돌 예측을 매 프레임 다시 읽고 되먹이지 않는다.
    //   t_peak = t_impact − PeakLead · t_on = max(t0, t_peak − RiseLead) · 절정 맥동 · 충돌 뒤 먹으로 가라앉음(scaled 시계)
    // 공격 키가 맞는 기관 가운데 카메라에 보이는 것(바깥 법선 · 카메라 방향 ≥ VisibleDot)을 t_on에 한 번 골라 창 안에서 유지한다.
    // Core 기관은 함께 켜진다(버력 장사 두 어깨). 등줄기(Spine) 사슬은 고르지 않고 배열 순서로 빛이 흐른다.
    // 방어 성공 먹 획(ParryCounterStrokeEffect)은 켜진 기관의 표면점에 닿고, Extinguish로 덧칠 안에서 먹 얼룩이 되어 꺼진다.
    // 이 컴포넌트는 기관 세트 옆(적 루트 또는 Folklore298 모습 프리팹 루트)에 둔다 — 소유자는 부모 쪽에서 찾는다.
    // #308 D308-4d: 결정 둘레 마석 오염(기관의 ContamRenderer = 몸)은 예고와 무관하게 늘 그려진다(카메라 거리 예산 안 — 덧칠 1회 추가).
    // 평소엔 무광 무채(팔레트 오염 먹 쪽으로 어둡힘, 발광 없음)이고, 예고 창 안에서만 같은 결을 따라 속성색 LDR이 결정에서 바깥으로 번진다.
    [DisallowMultipleComponent, DefaultExecutionOrder(900)]
    public sealed class EnemyElementTelegraph : MonoBehaviour
    {
        [SerializeField] private EnemyOrganSet _organs;
        [SerializeField] private EnemyTelegraphTimingSO _timing;
        [SerializeField] private ElementPaletteSO _palette;
        [Tooltip("비우면 EnemyController.PlayerTarget → PrologueEncounter.Player")]
        [SerializeField] private Transform _player;

        private const int MaxLit = 32;

        private EnemyController _enemy;
        private CheongryongCombatController _dragon;
        private SouthGateGeneralController _general;
        private PrologueEncounter _encounter;
        private bool _bound, _deferred;

        private OrganSurfaceOverlay _overlay;
        private EnemyTelegraphTimingSO _ownedTiming;

        private readonly int[] _lit = new int[MaxLit];
        private readonly bool[] _on = new bool[MaxLit];
        private readonly bool[] _was = new bool[MaxLit];
        private float[] _levels = new float[0];
        private int _litCount, _chainCount;
        private long _attackId;
        private Element _element;
        private float _t0, _tImpact, _fadeFrom = float.PositiveInfinity, _extinguishAt = float.NegativeInfinity;
        private bool _ended, _cancelled, _held, _break, _done, _selected, _debugView, _contamDebug, _contamOff, _warnedUnbound;
        private float _seed, _heldSince, _belowSince = -1f;

        // 읽기 전용 진단(편집기 검사 AC-12b·c·d, AC-T1·T2·T5·T7)
        // LitLevel = 셰이더에 넘긴 최대 level × (1 − 얼룩) (0 = 꺼짐) · LitOrganCount = 이번 프레임 덧칠된 기관 수 ·
        // OverlayRendererCount = 덧칠이 붙은 렌더러 수 · SelectionChanges = 이 예고 창 안에서 켜진 기관이 바뀐 횟수
        public float LitLevel { get; private set; }
        public int LitOrganCount { get; private set; }
        public int OverlayRendererCount { get; private set; }
        public int SelectionChanges { get; private set; }
        public int UnboundLitCount { get; private set; }
        // D308-4d: 지금 붙은 오염 덧칠 수(거리 예산 안, 예고와 무관) — 예고 덧칠 수(OverlayRendererCount)와 따로 센다
        public int ContaminationRendererCount { get; private set; }
        public long LitAttackId => _litCount > 0 && !_done ? _attackId : 0;
        public EnemyOrganSet Organs => _organs;
        public OrganSurfaceOverlay Overlay { get { Bind(); return _overlay; } }
        public bool Deferred { get { Bind(); return _deferred; } }
        // 기관 index의 이번 프레임 level(0 = 꺼짐) — AC-T7 두 어깨 검사
        public float OrganLevel(int organIndex) => organIndex >= 0 && organIndex < _levels.Length ? _levels[organIndex] : 0f;
        // AC-T8 _DebugView 1 캡처(Play 안에서만, 저장 안 함): 켜면 묶인 기관 전부의 영역이 흰색으로 선다
        public bool DebugView { get => _debugView; set { _debugView = value; } }
        // D308-4d 오염 영역 캡처(Play 안에서만, 저장 안 함): 켜면 붙은 오염 덧칠의 결 영역이 흰색으로 선다(_DebugView 2). DebugView가 우선
        public bool ContaminationDebugView { get => _contamDebug; set { _contamDebug = value; } }
        // D308-4d 측정용(Play 안에서만, 저장 안 함): 켜면 오염 덧칠을 붙이지 않는다 — 오염 켬/끔 캡처·그리기 수 비교(AC-C3·C5)
        public bool ContaminationSuppressed { get => _contamOff; set { _contamOff = value; } }

        public void Configure(EnemyOrganSet organs, EnemyTelegraphTimingSO timing, ElementPaletteSO palette)
        { _organs = organs; _timing = timing; _palette = palette; _bound = false; _overlay?.DetachAll(); }

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
            _general = GetComponentInParent<SouthGateGeneralController>();
            _enemy = GetComponentInParent<EnemyController>();
            _encounter = GetComponentInParent<PrologueEncounter>();
            if (_overlay == null) _overlay = new OrganSurfaceOverlay();
            _overlay.Bind(_deferred ? null : _organs);
            _levels = new float[_organs != null ? _organs.Count : 0];
        }

        private Transform Player => _player != null ? _player : _enemy != null && _enemy.PlayerTarget != null ? _enemy.PlayerTarget
            : _encounter != null ? _encounter.Player : null;

        // ---- 방어 성공 먹 획 API(ParryCounterStrokeEffect) ----

        // 획의 끝 = 이 공격으로 켜진 기관(없으면 가슴·핵 기관, 그다음 첫 기관)의 표면점. 기관 세트가 없으면 false(호출자가 가슴으로 대신한다)
        // #308: 저작된 표면점(SurfaceLocalPoint, 앵커 기준)과 lift 0. 표면이 저작되지 않은 기관은 구 중심 + lift Radius(카메라 쪽 구 표면)
        public bool TryGetCounterTarget(long attackId, out Transform anchor, out Vector3 localOffset, out float lift)
        {
            Bind(); anchor = null; localOffset = default; lift = 0f;
            int index = CounterOrgan(attackId);
            if (index < 0) return false;
            var organ = _organs.Get(index); if (organ == null) return false;
            anchor = _organs.AnchorOf(index);
            if (organ.HasSurface) { localOffset = organ.SurfaceLocalPoint; lift = 0f; }
            else { localOffset = organ.LocalOffset; lift = organ.Radius; }
            return true;
        }

        // 검사용(AC-12c): 지금 이 공격의 획이 닿아야 할 기관 표면점(월드). 획 착지 오차를 획 자신의 끝점이 아니라 이 점과 잰다
        public bool TryGetCounterSurfacePoint(long attackId, out Vector3 point, out string organId)
        {
            Bind(); point = default; organId = null;
            int index = CounterOrgan(attackId);
            if (index < 0) return false;
            var organ = _organs.Get(index); if (organ == null) return false;
            organId = organ.Id; point = _organs.SurfacePoint(index);
            return true;
        }

        private int CounterOrgan(long attackId)
        {
            if (_organs == null || _organs.Count == 0 || _deferred) return -1;
            int index = attackId == _attackId && _litCount > 0 ? Primary() : -1;
            if (index < 0) for (int i = 0; i < _organs.Count; i++) { var o = _organs.Get(i); if (o != null && (o.Role == EnemyOrganRole.Chest || o.Role == EnemyOrganRole.Core)) { index = i; break; } }
            if (index < 0) index = 0;
            return index;
        }

        // 획이 날아가는 동안 덧칠을 잡아 둔다(가라앉음 대기) — 도착(Extinguish)이나 해제가 반드시 뒤따른다(획의 실시간 상한)
        public void HoldForCounter(long attackId) { if (attackId == _attackId && _litCount > 0 && !_done) { _held = true; _heldSince = Time.unscaledTime; } }
        public void ReleaseCounter(long attackId) { if (attackId == _attackId) _held = false; }
        // 도착: 기관의 속성빛이 덧칠 안에서 먹 얼룩으로 꺼지고 먹 점이 흩어진다. 개화(3번째)는 고리가 깨진다
        public void Extinguish(long attackId, bool blossom)
        {
            if (attackId != _attackId || _litCount == 0 || _done) return;
            _held = false; _break |= blossom;
            if (float.IsNegativeInfinity(_extinguishAt)) _extinguishAt = Time.time;
        }

        // ---- 소유자 읽기(매 프레임) ----

        private bool Sample(out long id, out Element element, out string key, out float t0, out float impact)
        {
            id = 0; element = Element.Wood; key = null; t0 = impact = 0f;
            var player = Player;
            if (_dragon != null && _dragon.isActiveAndEnabled)
            {
                var plan = _dragon.CurrentPlan;
                if (plan == null || plan.IsCancelled || !plan.IsElemental || plan.ContactConsumed) return false;
                id = plan.Attack.AttackId; element = plan.Attack.Element.Value; t0 = plan.StartedAt;
                key = plan.Kind == CheongryongAttackKind.WoodProjectile ? EnemyOrganSet.KeyBolt : EnemyOrganSet.KeyRoot;
                impact = plan.PredictImpactTime((player != null ? player.position : plan.TargetPoint) + Vector3.up * .8f);
                return true;
            }
            if (_general != null && _general.isActiveAndEnabled)
            {
                var plan = _general.CurrentPlan; var pulse = plan != null ? plan.ElementalPulse : null;
                if (plan == null || plan.IsCancelled || pulse == null || pulse.Cancelled || pulse.Consumed) return false;
                id = pulse.Attack.AttackId; element = pulse.Attack.Element.Value; key = EnemyOrganSet.KeyWave;
                t0 = plan.EmpowerStartAt; // 창날은 흙 준비부터
                impact = plan.PredictImpactTime(pulse, player != null ? player.position : plan.Origin + plan.Direction * pulse.Range);
                return true;
            }
            if (_enemy != null && _enemy.isActiveAndEnabled && (_enemy.IsTelegraphing || _enemy.IsProjectileFlying))
            {
                var attack = _enemy.CurrentAttack;
                if (!attack.Element.HasValue || attack.AttackId <= 0) return false; // 무속성 = 빛나지 않는다
                id = attack.AttackId; element = attack.Element.Value; key = _enemy.AttackOrganKey;
                t0 = _enemy.AttackStartTime; impact = _enemy.PredictedImpactTime;
                return !float.IsNegativeInfinity(impact);
            }
            return false;
        }

        private void LateUpdate()
        {
            Bind();
            if (_organs == null || _timing == null || _deferred) { HideAll(); return; }
            float now = Time.time;
            if (Sample(out long id, out Element element, out string key, out float t0, out float impact))
            {
                if (id != _attackId) Begin(id, element, key);
                _t0 = t0; _tImpact = impact;
            }
            else if (_attackId != 0 && !_ended)
            {
                // 소유자가 공격을 놓았다: 충돌 시각이 지났으면 자연 가라앉음, 아니면 취소(먹으로 꺼짐)
                _ended = true; _cancelled = now < _tImpact - .05f;
                _fadeFrom = _cancelled ? now : Mathf.Min(now, _tImpact);
            }
            if (_debugView || _contamDebug) { RenderDebug(); return; }
            Render(now);
        }

        private void Begin(long id, Element element, string key)
        {
            _attackId = id; _element = element; _ended = _cancelled = _held = _break = _done = _selected = false;
            _fadeFrom = float.PositiveInfinity; _extinguishAt = float.NegativeInfinity; _belowSince = -1f; SelectionChanges = 0;
            _litCount = _organs.Collect(key, _lit); _chainCount = 0;
            for (int i = 0; i < _litCount; i++) { _on[i] = false; if (_organs.Get(_lit[i]).Role == EnemyOrganRole.Spine) _chainCount++; }
            _seed = (id % 97) * .173f;
        }

        // 획이 닿는 기관: 입 > 창날 > 눈 > 가슴·핵 > 등줄기(사슬뿐이면 땅 쪽 마디). 고른 뒤에는 켜진 기관만 본다
        private int Primary()
        {
            int best = -1, bestRank = int.MaxValue;
            for (int i = 0; i < _litCount; i++)
            {
                var role = _organs.Get(_lit[i]).Role;
                if (_selected && role != EnemyOrganRole.Spine && !_on[i]) continue;
                int rank = role == EnemyOrganRole.Mouth ? 0 : role == EnemyOrganRole.Weapon ? 1 : role == EnemyOrganRole.Eye ? 2 : role == EnemyOrganRole.Spine ? 4 : 3;
                if (rank < bestRank || (rank == bestRank && rank == 4)) { best = _lit[i]; bestRank = rank; }
            }
            return best;
        }

        // ---- 보이는 기관 고르기(t_on에 한 번, 히스테리시스로만 다시) ----

        private float FacingDot(int organIndex, Camera cam)
        {
            if (cam == null) return 1f;
            Vector3 p = _organs.SurfacePoint(organIndex), toCam = cam.transform.position - p;
            float d = toCam.magnitude;
            return d > 1e-4f ? Vector3.Dot(_organs.SurfaceNormal(organIndex), toCam / d) : 1f;
        }

        private void Select(Camera cam)
        {
            var t = _timing; int best = -1, chosen = 0; float bestDot = float.NegativeInfinity; bool core = false;
            for (int i = 0; i < _litCount; i++)
            {
                _was[i] = _on[i]; _on[i] = false;
                var role = _organs.Get(_lit[i]).Role;
                if (role == EnemyOrganRole.Spine) continue;
                float dot = FacingDot(_lit[i], cam);
                if (dot >= t.VisibleDot) { _on[i] = true; chosen++; }
                if (dot > bestDot) { bestDot = dot; best = i; }
            }
            if (chosen == 0 && best >= 0) _on[best] = true;   // none faces the camera: the most facing one still lights
            for (int i = 0; i < _litCount; i++) if (_on[i] && _organs.Get(_lit[i]).Role == EnemyOrganRole.Core) core = true;
            if (core) for (int i = 0; i < _litCount; i++) if (_organs.Get(_lit[i]).Role == EnemyOrganRole.Core) _on[i] = true;   // Core organs light together
            if (_selected)
            {
                bool changed = false;
                for (int i = 0; i < _litCount && !changed; i++) if (_on[i] != _was[i]) changed = true;
                if (changed) SelectionChanges++;
            }
            _selected = true; _belowSince = -1f;
        }

        private void Recheck(Camera cam, float now)
        {
            var t = _timing; float bestOn = float.NegativeInfinity; bool any = false;
            for (int i = 0; i < _litCount; i++)
            {
                if (!_on[i] || _organs.Get(_lit[i]).Role == EnemyOrganRole.Spine) continue;
                any = true; bestOn = Mathf.Max(bestOn, FacingDot(_lit[i], cam));
            }
            if (!any || bestOn >= t.VisibleDot - t.VisibleHysteresis) { _belowSince = -1f; return; }
            if (_belowSince < 0f) { _belowSince = now; return; }
            if (now - _belowSince > t.VisibleReselectSeconds) Select(cam);
        }

        // ---- 그리기 ----

        // 매 프레임: 예고 창이면 기관 덧칠을 세우고, 창과 무관하게 결정 둘레 오염(D308-4d)은 거리 예산 안에서 늘 붙어 있다.
        // 예고 창 안에서는 오염 결을 따라 속성색이 결정 쪽에서 바깥으로 번진다(앞머리 = 오름 rise × FlowReach, 같은 알파·먹 얼룩)
        private void Render(float now)
        {
            LitLevel = 0f; LitOrganCount = 0; UnboundLitCount = 0;
            for (int i = 0; i < _levels.Length; i++) _levels[i] = 0f;
            var t = _timing;
            var cam = Camera.main;
            float flow = 0f, contamTint = 0f, contamBlot = 0f, pulse = 0f, brk = 0f;
            _overlay.BeginFrame();
            if (_attackId != 0 && _litCount > 0 && !_done) RenderWindow(now, cam, ref flow, ref contamTint, ref contamBlot, ref pulse, ref brk);
            if (_contamOff) _overlay.ClearContamination();
            else _overlay.SetContamination(_organs, cam != null, cam != null ? cam.transform.position : default, t.ContaminationAttachDistance,
                t.ContaminationAttachDistance + t.ContaminationDetachMargin, t.ContaminationFadeStartDistance, flow, contamTint, contamBlot);
            OverlayRendererCount = _overlay.EndFrame(t, ElementColor(_element), pulse, brk, _seed, 0f, ContaminationInk());
            ContaminationRendererCount = _overlay.ContaminationAttachedCount;
            // an elemental attack whose organs have no authored surface yet (TargetRenderer empty — OrganSurface308 apply / parts not run)
            // shows nothing on the model: say so once per telegraph instead of silently dropping the #306 cue
            if (!_warnedUnbound && UnboundLitCount > 0 && LitOrganCount == 0)
            {
                _warnedUnbound = true;
                Debug.LogWarning("[EnemyElementTelegraph] " + name + ": elemental attack has " + UnboundLitCount + " organ(s) with no overlay surface — nothing lights on the model"
                    + (_overlay.Refusals.Length > 0 ? " (" + _overlay.Refusals + ")" : " (run OrganSurface308 apply / prefabs)"), this);
            }
        }

        // 예고 창 안의 기관 값(시점 식·고르기·사슬은 #308 그대로). 창 밖(t_on 전·가라앉음 끝)이면 기관은 아무것도 쓰지 않는다 → EndFrame이 뗀다
        private void RenderWindow(float now, Camera cam, ref float flow, ref float contamTint, ref float contamBlot, ref float pulse, ref float brk)
        {
            var t = _timing;
            float peak = _tImpact - t.PeakLead, on = Mathf.Max(_t0, peak - t.RiseLead);
            float rise = peak > on + .001f ? Mathf.Clamp01((now - on) / (peak - on)) : now >= on ? 1f : 0f;
            pulse = now >= peak && now <= peak + t.PeakPulseSeconds ? Mathf.Sin(Mathf.PI * (now - peak) / t.PeakPulseSeconds) : 0f;
            if (_held && Time.unscaledTime - _heldSince > t.RealtimeCap * 2f) _held = false; // 획이 사라져도 덧칠이 남지 않는다
            float blot = 0f, burst = 0f;
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
            if (blot >= 1f || now < on) { if (blot >= 1f) _done = true; pulse = brk = 0f; return; }

            if (!_selected) Select(cam); else Recheck(cam, now);
            float alpha = Mathf.Clamp01(rise * 4f), open = Mathf.SmoothStep(0f, 1f, rise);
            int chain = 0;
            for (int i = 0; i < _litCount; i++)
            {
                int index = _lit[i]; var organ = _organs.Get(index); float level = 1f;
                if (organ.Role == EnemyOrganRole.Spine && _chainCount > 1)
                {
                    // 등줄기 사슬: t_on에서 첫 마디 → t_peak에 땅 쪽 마디. 마지막 마디는 절정부터 충돌까지 머문다
                    float centre = chain / (float)(_chainCount - 1), w = t.ChainWidth;
                    level = chain == _chainCount - 1 ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rise - centre + w) / w))
                        : rise >= 1f ? 0f : Mathf.Clamp01(1f - Mathf.Abs(rise - centre) / w);
                    chain++;
                }
                else if (organ.Role != EnemyOrganRole.Spine && !_on[i]) continue;
                float a = alpha * level;
                if (a <= .002f) continue;
                if (organ.HasContamination) { contamTint = Mathf.Max(contamTint, a); contamBlot = blot; }   // D308-4d: 이 결정의 오염 결도 물든다
                if (!_overlay.SetOrgan(index, _organs.WorldPoint(index), organ.Radius, a, open * level, blot, burst)) { UnboundLitCount++; continue; }
                float shown = a * (1f - blot);
                _levels[index] = shown;
                if (shown > LitLevel) LitLevel = shown;
                LitOrganCount++;
            }
            flow = rise * t.ContaminationFlowReach;
        }

        private void RenderDebug()
        {
            LitLevel = 0f; LitOrganCount = 0; UnboundLitCount = 0;
            _overlay.BeginFrame();
            if (_debugView)
                for (int i = 0; i < _organs.Count; i++) { var organ = _organs.Get(i); if (organ != null) _overlay.SetDebugOrgan(i, _organs.WorldPoint(i), organ.Radius); }
            var cam = Camera.main;
            if (_contamOff) _overlay.ClearContamination();
            // debug view: full strength at any distance inside the budget (fadeStart = attach → no fade), so the region capture is not dimmed
            else _overlay.SetContamination(_organs, cam != null, cam != null ? cam.transform.position : default, _timing.ContaminationAttachDistance,
                _timing.ContaminationAttachDistance + _timing.ContaminationDetachMargin, _timing.ContaminationAttachDistance, 0f, 0f, 0f);
            OverlayRendererCount = _overlay.EndFrame(_timing, ElementColor(_element), 0f, 0f, _seed, _debugView ? 1f : 2f, ContaminationInk());
            ContaminationRendererCount = _overlay.ContaminationAttachedCount;
        }

        // 오염 먹 = 팔레트 단일 출처(ART-COLOR 오염색 [LOCKED] 검보라·묵색). 팔레트가 없으면 ElementPaletteSO 기본값(#332B36)
        private Color ContaminationInk() => _palette != null ? _palette.CorruptInkColor : DefaultCorruptInk;
        private static readonly Color DefaultCorruptInk = new Color(.200f, .169f, .212f);

        private Color ElementColor(Element element)
        {
            if (_palette == null) return new Color(.45f, .43f, .41f);
            char initial = element == Element.Fire ? 'ㄴ' : element == Element.Earth ? 'ㅁ' : element == Element.Metal ? 'ㅅ' : element == Element.Water ? 'ㅇ' : 'ㄱ';
            return _palette.GetBaseColor(initial); // 팔레트가 단일 출처(ART-COLOR 번역)
        }

        private void HideAll()
        {
            LitLevel = 0f; LitOrganCount = 0; OverlayRendererCount = 0; ContaminationRendererCount = 0;
            _overlay?.DetachAll();
        }

        private void OnDisable() { _attackId = 0; _litCount = 0; _held = false; _selected = false; HideAll(); }
        private void OnDestroy()
        {
            _overlay?.Dispose(); _overlay = null;
            if (_ownedTiming != null) Destroy(_ownedTiming);
        }
    }
}
