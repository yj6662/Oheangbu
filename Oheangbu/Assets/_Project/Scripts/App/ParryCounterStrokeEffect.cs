using System;
using Oheangbu.App.Demo;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // #306 방어 성공 먹 획(SPEC-PLAYTEST-306 #12, D306) — 표현 전용. 판정·그로기·먹 값은 이미 확정된 뒤다(배선부 훅이 부른다).
    // 방어 글자 접촉점 → 빛났던 기관(매 프레임 위치)까지 방어 글자 속성색으로 물든 먹 획이 날아간다: 머리 0.2초, 폭 1.0 → 0.35,
    // 꼬리가 녹는다. 도착 = 기관 빛이 먹 얼룩으로 꺼지고 터짐(EnemyElementTelegraph.Extinguish) + 원상 펄스·채움 표시(콜백).
    // #308(SPEC-TELEGRAPH-ORGAN-308): 획 끝 = 켜진 기관의 저작된 표면점(lift 0) — 띄운 쿼드 자리가 아니라 모델 표면에 닿는다.
    //   표면이 저작되지 않은 기관만 구 중심에서 카메라 쪽으로 Radius, 기관이 없으면 가슴 대체(FallbackChestLift)는 그대로다.
    // 개화 = 1.5배 굵고 기관 고리가 깨진다. 반만 성공 = 가는 획이 반쯤에서 녹고 채움 없음. 실패 = 획 없음(호출되지 않는다).
    // 풀: 획 MaxLiveStrokes개(기본 3)를 처음 한 번 만들고 재사용한다 — 패링마다 할당 없음. 넘치면 가장 오래된 획을 즉시 도착시킨다.
    // 시계: scaled로 흐르되 실시간의 RealtimeFloor 배 아래로 느려지지 않고(히트스톱), RealtimeCap 실초 안에 반드시 도착한다.
    // 정지 메뉴(timeScale 0)에서는 멈춘다. 적 피격 반응은 만들지 않는다.
    [DisallowMultipleComponent, DefaultExecutionOrder(950)]
    public sealed class ParryCounterStrokeEffect : MonoBehaviour
    {
        private sealed class Stroke
        {
            public InkRibbon306 Ribbon;
            public bool Live, Arrived, Half, Blossom, HoldsGroggy, Pulse;
            public EnemyVitals Target;
            public EnemyElementTelegraph Organ;
            public long AttackId;
            public Transform Anchor;
            public Vector3 Offset, Start, End;
            public Color Colour;
            public float Progress, Melt, RealAge, HeldGroggy, TailAtArrival, Lift;
            public int Order;
        }

        private EnemyTelegraphTimingSO _timing;
        private Action<bool> _arrived;
        private Action _lateTick, _unhook;
        private Stroke[] _strokes = new Stroke[0];
        private Transform _root;
        private int _order;

        public int LiveCount { get { int n = 0; foreach (var s in _strokes) if (s.Live) n++; return n; } }
        public int Capacity => _strokes.Length;
        // 편집기 검사(AC-12c): 마지막으로 도착한 획의 끝과 그 순간 텔레그래프가 내놓는 켜진 기관 표면점 사이 거리(m) —
        // 획 자신의 끝점과 재면 늘 0이라 따로 묻는다(기관이 없으면 NaN). LastArrivalOrgan = 그 기관 Id
        public float LastArrivalError { get; private set; } = float.NaN;
        public string LastArrivalOrgan { get; private set; } = "";
        public int Launched { get; private set; }

        // arrived(pulse): 도착한 성공 획 — 배선부가 원상 펄스·그로기 표시를 푼다. lateTick: 매 LateUpdate(배선부 대기 펄스 정리)
        public void Configure(EnemyTelegraphTimingSO timing, Action<bool> arrived, Action lateTick)
        {
            _timing = timing; _arrived = arrived; _lateTick = lateTick;
            int capacity = Mathf.Clamp(timing != null ? timing.MaxLiveStrokes : 3, 1, 8);
            if (_strokes.Length == capacity) return;
            Clear(); foreach (var s in _strokes) s.Ribbon?.Dispose();
            _strokes = new Stroke[capacity];
            // 풀은 여기서 한 번 데운다 — 첫 패링도 할당 없이
            for (int i = 0; i < capacity; i++) _strokes[i] = new Stroke { Ribbon = new InkRibbon306("ParryCounterStroke306_" + i, Root(), timing != null ? timing.StrokeMaterial : null, 253 + i % 3) };
        }
        public void SetUnhook(Action unhook) { _unhook = unhook; }

        // 성공(half=false) 또는 반만 성공(half=true) 획 하나. holdsGroggy면 도착까지 target의 그로기 표시를 heldGroggy로 잡는다
        public bool Launch(Vector3 start, Color colour, EnemyVitals target, long attackId, bool half, bool blossom, bool holdsGroggy, float heldGroggy, bool pulse)
        {
            if (_timing == null || target == null || _strokes.Length == 0 || !float.IsFinite(start.x + start.y + start.z)) return false;
            Stroke stroke = null, oldest = null;
            foreach (var s in _strokes) { if (!s.Live) { stroke = s; break; } if (oldest == null || s.Order < oldest.Order) oldest = s; }
            if (stroke == null) { if (!oldest.Arrived) Arrive(oldest); Retire(oldest); stroke = oldest; }
            stroke.Live = true; stroke.Arrived = false; stroke.Half = half; stroke.Blossom = blossom && !half;
            stroke.HoldsGroggy = holdsGroggy && !half; stroke.HeldGroggy = heldGroggy; stroke.Pulse = pulse && !half;
            stroke.Target = target; stroke.AttackId = attackId; stroke.Start = start; stroke.Colour = colour;
            stroke.Progress = stroke.Melt = stroke.RealAge = stroke.TailAtArrival = 0f; stroke.Order = ++_order;
            stroke.Organ = target.GetComponentInChildren<EnemyElementTelegraph>();
            if (stroke.Organ == null || !stroke.Organ.TryGetCounterTarget(attackId, out stroke.Anchor, out stroke.Offset, out stroke.Lift))
            {
                // 기관이 없으면 적의 가슴(Humanoid Chest → 루트 + 1.2 m)
                var animator = target.GetComponentInChildren<Animator>();
                stroke.Anchor = animator != null && animator.avatar != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Chest) : null;
                stroke.Offset = Vector3.zero; stroke.Lift = _timing.FallbackChestLift;
                if (stroke.Anchor == null) { stroke.Anchor = target.transform; stroke.Offset = Vector3.up * 1.2f; }
                stroke.Organ = null;
            }
            else if (!half) stroke.Organ.HoldForCounter(attackId);
            stroke.End = EndPoint(stroke, Camera.main);
            Launched++;
            return true;
        }

        public bool TryGetHeldGroggy(EnemyVitals target, out float value)
        {
            value = 0f; Stroke best = null;
            foreach (var s in _strokes) if (s.Live && !s.Arrived && s.HoldsGroggy && s.Target == target && (best == null || s.Order < best.Order)) best = s;
            if (best == null) return false;
            value = best.HeldGroggy; return true;
        }

        // 배선 비활성·판 초기화 — 표시 잡기·기관 잡기를 모두 푼다(콜백 없음)
        public void Clear()
        {
            foreach (var s in _strokes) if (s.Live) { if (s.Organ != null) s.Organ.ReleaseCounter(s.AttackId); Retire(s); }
        }

        private void LateUpdate()
        {
            _lateTick?.Invoke();
            if (_timing == null) return;
            bool paused = Time.timeScale <= .0001f;
            float step = paused ? 0f : Mathf.Max(Time.deltaTime, Time.unscaledDeltaTime * _timing.RealtimeFloor);
            float real = paused ? 0f : Time.unscaledDeltaTime;
            var cam = Camera.main;
            foreach (var s in _strokes)
            {
                if (!s.Live) continue;
                if (s.Anchor != null) s.End = EndPoint(s, cam); // 기관을 매 프레임 따라간다(대상이 사라지면 마지막 자리)
                s.RealAge += real;
                if (!s.Arrived)
                {
                    s.Progress += step / Mathf.Max(.01f, _timing.StrokeFlightSeconds);
                    if (s.Progress >= 1f || s.RealAge >= _timing.RealtimeCap) Arrive(s);
                }
                else
                {
                    s.Melt += step / Mathf.Max(.01f, _timing.TailMeltSeconds);
                    if (s.Melt >= 1f || s.RealAge >= _timing.RealtimeCap + _timing.TailMeltSeconds * 2f) { Retire(s); continue; }
                }
                if (cam != null) Draw(s, cam);
            }
        }

        private void Arrive(Stroke s)
        {
            s.Arrived = true; s.Progress = 1f; s.TailAtArrival = Mathf.Max(0f, Reach(s) - _timing.TailLength);
            if (s.Half) { if (s.Organ != null) s.Organ.ReleaseCounter(s.AttackId); return; }
            if (s.Anchor != null) s.End = EndPoint(s, Camera.main);
            LastArrivalError = float.NaN; LastArrivalOrgan = "";
            if (s.Organ != null && s.Organ.TryGetCounterSurfacePoint(s.AttackId, out var surface, out var organId))
            { LastArrivalError = Vector3.Distance(Head(s, 1f), surface); LastArrivalOrgan = organId; }
            if (s.Organ != null) s.Organ.Extinguish(s.AttackId, s.Blossom);
            bool held = s.HoldsGroggy; s.HoldsGroggy = false;
            if (held || s.Pulse) _arrived?.Invoke(s.Pulse);
        }

        private void Retire(Stroke s) { s.Live = false; s.HoldsGroggy = false; s.Target = null; s.Anchor = null; s.Organ = null; s.Ribbon?.Hide(); }

        // 획 끝 = 앵커 · 오프셋(#308 표면점, lift 0). lift > 0(표면 미저작 기관·가슴 대체)만 카메라 쪽으로 띄운다 — 몸 속에서 끝나지 않게
        private static Vector3 EndPoint(Stroke s, Camera cam)
        {
            Vector3 p = s.Anchor.TransformPoint(s.Offset);
            if (cam == null || s.Lift <= 0f) return p;
            Vector3 toCam = cam.transform.position - p; float d = toCam.magnitude;
            return d > .001f ? p + toCam / d * Mathf.Min(d * .5f, s.Lift) : p;
        }

        private float Reach(Stroke s) => s.Half ? _timing.HalfReach : 1f;
        private Vector3 Head(Stroke s, float u) => Vector3.Lerp(s.Start, s.End, u);

        private void Draw(Stroke s, Camera cam)
        {
            var t = _timing;
            float reach = Reach(s), fly = 1f - (1f - Mathf.Clamp01(s.Progress)) * (1f - Mathf.Clamp01(s.Progress)); // 빠르게 떠나 감속해 닿는다
            float head = reach * fly;
            float tail = s.Arrived ? Mathf.Lerp(s.TailAtArrival, head, Mathf.Clamp01(s.Melt)) : Mathf.Max(0f, head - t.TailLength);
            float width = t.StrokeWidth * (s.Blossom ? t.BlossomWidthScale : 1f) * (s.Half ? t.HalfWidthScale : 1f);
            // 반만 성공: 가는 획이 반쯤 가며 이미 마르기 시작한다
            float dissolve = s.Arrived ? Mathf.Clamp01(s.Melt) * .85f : s.Half ? fly * .45f : 0f;
            float fade = s.Arrived ? 1f - Mathf.Clamp01(s.Melt) * Mathf.Clamp01(s.Melt) : 1f;
            s.Ribbon.Draw(s.Start, s.End, tail, head, width, t.StrokeEndWidth, t.StrokeArc * (s.Order % 2 == 0 ? 1f : -1f), .35f,
                s.Colour, t.StrokeTint, fade, dissolve, cam, 14);
        }

        private Transform Root()
        {
            if (_root != null) return _root;
            var go = new GameObject("ParryCounterStrokes306") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false); _root = go.transform; return _root;
        }

        private void OnDisable() { Clear(); }
        private void OnDestroy()
        {
            _unhook?.Invoke(); _unhook = null;
            foreach (var s in _strokes) s.Ribbon?.Dispose();
            if (_root != null) Destroy(_root.gameObject);
        }
    }

    // #306 카메라를 보는 먹 리본 한 가닥(방어 성공 획 — #308에서 기관 끈은 없어졌다) — RibbonMeshBuilder + InkStroke 재질 사본(획마다 하나, 처음 한 번).
    // 발광 상한: _FlashAdd = 0(HDR 가산 없음) — 속성색은 먹이 물드는 틴트로만(LDR). 차폐 투시 패스는 끈다(세상 물체).
    // 경로: from→to 직선 위 [uTail, uHead] 구간, 카메라 평면으로 arc만큼 휜다. 폭은 기필 1 → 끝 endScale, 꼬리 쪽은 가늘고 마른다.
    public sealed class InkRibbon306
    {
        private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor"), FlashStrengthId = Shader.PropertyToID("_FlashStrength"),
            FlashTintId = Shader.PropertyToID("_FlashTint"), FlashAddId = Shader.PropertyToID("_FlashAdd"), FadeId = Shader.PropertyToID("_FadeMul"),
            DissolveId = Shader.PropertyToID("_DissolveT"), StencilId = Shader.PropertyToID("_StencilRef"), SeedId = Shader.PropertyToID("_NoiseSeed");
        private readonly GameObject _go;
        private readonly Transform _t;
        private readonly MeshRenderer _renderer;
        private readonly Mesh _mesh;
        private readonly Material _material;
        private readonly BrushStrokeData _data = new BrushStrokeData();
        private readonly RibbonMeshBuilder _builder = new RibbonMeshBuilder();
        private static readonly Color Ink = new Color(.165f, .149f, .133f, 1f);

        public InkRibbon306(string name, Transform parent, Material source, int stencilRef)
        {
            _go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            _t = _go.transform; _t.SetParent(parent, false);
            _mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave }; _mesh.MarkDynamic();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            var shader = source == null ? Shader.Find("Oheangbu/InkStroke") : null;
            _material = source != null ? new Material(source) : shader != null ? new Material(shader) : null;
            if (_material != null)
            {
                _material.hideFlags = HideFlags.DontSave;
                _material.SetFloat(StencilId, Mathf.Clamp(stencilRef, 1, 255)); _material.SetFloat(FlashAddId, 0f);
                _material.SetFloat(SeedId, stencilRef * 3.17f); _material.SetShaderPassEnabled("SRPDefaultUnlit", false);
            }
            _renderer.sharedMaterial = _material; _renderer.enabled = false;
        }

        public void Draw(Vector3 from, Vector3 to, float uTail, float uHead, float width, float endScale, float arc, float inkTail,
            Color flash, float tint, float fade, float dissolve, Camera cam, int samples)
        {
            Vector3 line = to - from; float length = line.magnitude;
            if (_material == null || length < .01f || uHead - uTail < .002f || fade <= .002f) { Hide(); return; }
            Vector3 x = line / length, toCam = cam.transform.position - (from + to) * .5f;
            Vector3 z = toCam - x * Vector3.Dot(toCam, x);
            if (z.sqrMagnitude < 1e-6f) z = Vector3.Cross(x, Mathf.Abs(x.y) < .9f ? Vector3.up : Vector3.right);
            z.Normalize(); Vector3 y = Vector3.Cross(z, x);
            _t.SetPositionAndRotation(from, Quaternion.LookRotation(z, y)); _t.localScale = Vector3.one;
            if (_t.parent != null) { var ls = _t.parent.lossyScale; _t.localScale = new Vector3(1f / Mathf.Max(1e-4f, ls.x), 1f / Mathf.Max(1e-4f, ls.y), 1f / Mathf.Max(1e-4f, ls.z)); }
            _data.Clear(); float now = Time.time; int n = Mathf.Max(2, samples);
            for (int i = 0; i < n; i++)
            {
                float k = i / (float)(n - 1), u = Mathf.Lerp(uTail, uHead, k);
                float taper = Mathf.SmoothStep(.25f, 1f, Mathf.Clamp01(k * 3f)); // 꼬리는 가늘게
                float w = width * Mathf.Lerp(1f, endScale, u) * taper;
                _data.Add(new BrushStrokePoint(new Vector3(u * length, Mathf.Sin(u * Mathf.PI) * arc * length, 0f), w, Mathf.Lerp(inkTail, 1f, k), now));
            }
            _builder.Build(_data, Ink, 1.15f, 0, 0, 3, _mesh);
            _material.SetColor(FlashColorId, flash); _material.SetFloat(FlashStrengthId, 1f); _material.SetFloat(FlashTintId, Mathf.Clamp01(tint));
            _material.SetFloat(FadeId, Mathf.Clamp01(fade)); _material.SetFloat(DissolveId, Mathf.Clamp01(dissolve));
            _renderer.enabled = true;
        }

        public void Hide() { if (_renderer != null) _renderer.enabled = false; }

        public void Dispose()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }
    }
}
