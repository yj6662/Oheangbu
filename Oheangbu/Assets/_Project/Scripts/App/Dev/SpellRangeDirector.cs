using System.Collections.Generic;
using System.Text;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // [사격장 하네스 — SPEC-DEV-SPELL-RANGE] 실플레이 게이트의 계측·안내. 게임 규칙 무접촉 —
    // 글자 채널로 시전 시각을 재고, 배선의 판정 계획(CastPlanned)과 과녁의 실제 착탄을 대조해 콘솔에 남긴다.
    // 기하 기대 집합은 계획의 중심·방향으로 AreaGeometry를 독립 호출해 만든다(SPELL-AREA-SHAPES §3).
    // 바닥 안내선(cone 부채꼴·시작점)은 CombatConfig 수치를 그대로 그린다 — 수치가 바뀌면 선도 따라간다.
    // 키·실적 토글은 TEST-HUB 하네스(DevInteractor·DevEnemyWakePedestal)의 몫 — 여기는 계측만.
    public sealed class SpellRangeDirector : MonoBehaviour
    {
        private const int ArcSegments = 32;
        private const float GuideHeight = 0.03f;

        [Header("읽기 — 게임 계층(규칙 무접촉)")]
        [SerializeField] private DrawnLetterEventChannelSO _letterDrawn;
        [SerializeField] private SpellBookSO _spellBook;
        [SerializeField] private CombatConfigSO _config;
        [SerializeField] private Transform _player;
        [SerializeField] private CombatLoopWiring _wiring; // CastPlanned 재방송 — 계획 대 실제 대조

        [Header("사격장")]
        [SerializeField] private SpellRangeDummy[] _dummies = System.Array.Empty<SpellRangeDummy>();
        [Tooltip("시전 후 집계까지(초, 게임 시계) — 최장 비행(아 15m≈1.4s·소 9발≈2.8s)보다 길게")]
        [SerializeField, Min(0.5f)] private float _reportDelay = 3.5f;

        private Vector3 _origin;
        private Vector3 _forward;
        private Material _guideMaterial;
        private readonly Dictionary<EnemyVitals, SpellRangeDummy> _byVitals = new Dictionary<EnemyVitals, SpellRangeDummy>();

        // 진행 중 시전 1건의 계측
        private bool _casting;
        private int _castFrame = -1;
        private float _castTime;
        private char _castLetter;
        private AreaShape _castShape;
        private bool _hasGeo;
        private readonly Dictionary<SpellRangeDummy, List<float>> _planned = new Dictionary<SpellRangeDummy, List<float>>(); // 계획 예정 지연(오름차순)
        private readonly HashSet<SpellRangeDummy> _expectedGeo = new HashSet<SpellRangeDummy>();
        private readonly Dictionary<SpellRangeDummy, List<float>> _actual = new Dictionary<SpellRangeDummy, List<float>>();
        private readonly List<SpellRangeDummy> _hitOrder = new List<SpellRangeDummy>();
        private readonly StringBuilder _sb = new StringBuilder();

        private void Start()
        {
            _origin = _player != null ? _player.position : transform.position;
            _forward = Flat(_player != null ? _player.forward : Vector3.forward).normalized;
            foreach (var dummy in _dummies)
            {
                if (dummy != null && dummy.Vitals != null) _byVitals[dummy.Vitals] = dummy;
            }
            BuildGuides();
            CaptionDummies();
            Debug.Log("[Range] 사격장 준비 — F: 상호작용(선택대·귀환) · R: 재시작 · 시전/계획/착탄/집계는 콘솔(시간=게임 시계, 감속 포함)");
        }

        private void OnEnable()
        {
            if (_letterDrawn != null) _letterDrawn.Subscribe(OnLetterDrawn);
            if (_wiring != null) _wiring.CastPlanned += OnCastPlanned;
            foreach (var dummy in _dummies)
            {
                if (dummy != null) dummy.Hit += OnDummyHit;
            }
        }

        private void OnDisable()
        {
            if (_letterDrawn != null) _letterDrawn.Unsubscribe(OnLetterDrawn);
            if (_wiring != null) _wiring.CastPlanned -= OnCastPlanned;
            foreach (var dummy in _dummies)
            {
                if (dummy != null) dummy.Hit -= OnDummyHit;
            }
        }

        private void OnDestroy()
        {
            if (_guideMaterial != null) Destroy(_guideMaterial);
        }

        private void Update()
        {
            if (_casting && Time.time >= _castTime + _reportDelay) Report();
        }

        // ---- 계측: 시전 → 계획 → 착탄 → 집계 ----
        // 채널 구독 순서상 배선의 CastPlanned가 OnLetterDrawn보다 먼저 올 수 있다 — 같은 프레임이면 한 시전으로 합친다

        private void OnLetterDrawn(DrawnLetter letter)
        {
            if (_casting && _castFrame == Time.frameCount) return;
            if (_casting) Report();

            SpellBookSO.Entry entry = default;
            bool known = _spellBook != null && _spellBook.TryGet(letter.Letter, out entry);
            if (!known)
            {
                Debug.Log($"[Range] 시전 '{letter.Letter}' — 어휘 미러 밖(불발 취급)");
                return;
            }
            if (entry.Kind == SpellKind.Parry)
            {
                float guard = _config != null ? _config.GuardDuration : 0f;
                float window = _config != null ? _config.ParryWindow : 0f;
                Debug.Log($"[Range] 시전 '{letter.Letter}' 패링({entry.Element}) — 방어막 {guard:0.#}s(판정 창 {window:0.##}s)");
                return;
            }
            StartCast(letter.Letter, entry.AreaShape);
            float mul = entry.ProjectileSpeedMul > 0f ? entry.ProjectileSpeedMul : 1f;
            string kind = entry.AreaShape == AreaShape.None ? $"단일({entry.Kind}) — 탄속 ×{mul:0.##}" : $"광역 {entry.AreaShape}";
            Debug.Log($"[Range] 시전 '{_castLetter}' {kind}");
        }

        private void OnCastPlanned(CastPlan plan)
        {
            if (!_casting || _castFrame != Time.frameCount)
            {
                if (_casting) Report();
                StartCast(plan.Cast.Letter, plan.Cast.AreaShape);
            }
            _castShape = plan.Cast.AreaShape;

            foreach (var hit in plan.Hits)
            {
                if (hit.Target == null || !_byVitals.TryGetValue(hit.Target, out var dummy)) continue;
                if (!_planned.TryGetValue(dummy, out var times)) _planned[dummy] = times = new List<float>();
                times.Add(hit.ImpactTime - _castTime);
            }
            foreach (var times in _planned.Values) times.Sort();

            ComputeGeoExpectation(plan);

            _sb.Clear();
            foreach (var pair in _planned)
            {
                if (_sb.Length > 0) _sb.Append(", ");
                _sb.Append(pair.Key.name).Append('×').Append(pair.Value.Count).Append(" +").Append(pair.Value[0].ToString("0.00")).Append('s');
            }
            string planned = _sb.Length > 0 ? _sb.ToString() : "없음";
            string geo = _hasGeo ? $" · 기하 기대: {Names(_expectedGeo)}" : "";
            string area = plan.Area != null ? DescribeArea(plan.Area) : "단일";
            Debug.Log($"[Range] 판정 계획 '{_castLetter}' {area} — 예정: {planned}{geo}");
        }

        private void StartCast(char letter, AreaShape shape)
        {
            ClearCast();
            _casting = true;
            _castFrame = Time.frameCount;
            _castTime = Time.time;
            _castLetter = letter;
            _castShape = shape;
        }

        // 기하 기대 집합 — 계획의 중심·방향으로 AreaGeometry를 독립 호출(배선의 선택 코드와 별개의 검산)
        private void ComputeGeoExpectation(CastPlan plan)
        {
            _expectedGeo.Clear();
            _hasGeo = plan.Area != null && plan.Area.Shape != AreaShape.None;
            if (!_hasGeo) return;
            var area = plan.Area;
            foreach (var dummy in _dummies)
            {
                if (dummy == null || dummy.Vitals == null || !dummy.Vitals.IsAlive) continue;
                Vector3 pos = dummy.transform.position;
                bool inside;
                switch (area.Shape)
                {
                    case AreaShape.Cone:
                        inside = AreaGeometry.InCone(area.Point, area.Direction, pos,
                            _config != null ? _config.AreaConeAngle : 40f, area.Length, out _, out _);
                        break;
                    case AreaShape.Circle:
                        inside = AreaGeometry.InCircle(area.Point, pos, area.Radius, out _);
                        break;
                    case AreaShape.Path:
                        inside = AreaGeometry.InCorridor(area.Point, area.Direction, pos, area.Radius, area.Length, out _, out _);
                        break;
                    case AreaShape.Volley:
                        inside = AreaGeometry.InCone(_player != null ? _player.position : _origin, area.Direction, pos, area.Radius, area.Length, out _, out _);
                        break;
                    default:
                        inside = false;
                        break;
                }
                if (inside) _expectedGeo.Add(dummy);
            }
        }

        private static string DescribeArea(AreaImpactPlan area)
        {
            switch (area.Shape)
            {
                case AreaShape.Cone: return $"cone 사거리 {area.Length:0.#}m · 판정 +{area.Delay:0.##}s";
                case AreaShape.Circle: return $"원형 중심 ({area.Point.x:0.#}, {area.Point.z:0.#}) 반경 {area.Radius:0.#}m · +{area.Delay:0.##}s";
                case AreaShape.Path: return $"경로 반폭 {area.Radius:0.#}m 길이 {area.Length:0.#}m 속도 {area.Speed:0.#}m/s · 차오름 {area.Delay:0.##}s";
                case AreaShape.Volley: return $"다연발 {area.Shots.Count}발 반각 {area.Radius:0}° 사거리 {area.Length:0.#}m 탄속 {area.Speed:0.#}m/s · 기준 {area.Delay:0.##}s";
                default: return area.Shape.ToString();
            }
        }

        private void OnDummyHit(SpellRangeDummy dummy, float damage)
        {
            if (!_casting)
            {
                Debug.Log($"[Range] {dummy.name} 피격 (시전 계측 밖) 피해 {damage:0.#}");
                return;
            }
            float delay = Time.time - _castTime;
            if (!_actual.TryGetValue(dummy, out var hits)) _actual[dummy] = hits = new List<float>();
            hits.Add(delay);
            if (!_hitOrder.Contains(dummy)) _hitOrder.Add(dummy);
            string expected = "";
            if (_planned.TryGetValue(dummy, out var planned) && hits.Count <= planned.Count)
            {
                expected = $" (예정 {planned[hits.Count - 1]:0.00}s)";
            }
            string count = hits.Count > 1 ? $" #{hits.Count}" : "";
            Debug.Log($"[Range]   '{_castLetter}' → {dummy.Caption}{count} 착탄 +{delay:0.00}s{expected} 피해 {damage:0.#}");
        }

        private void Report()
        {
            _casting = false;
            if (_castShape != AreaShape.None)
            {
                var missed = new List<SpellRangeDummy>();
                foreach (var dummy in _dummies)
                {
                    if (dummy != null && !_hitOrder.Contains(dummy)) missed.Add(dummy);
                }
                bool planMatch = new HashSet<SpellRangeDummy>(_planned.Keys).SetEquals(_hitOrder);
                foreach (var pair in _planned)
                {
                    if (!_actual.TryGetValue(pair.Key, out var hits) || hits.Count != pair.Value.Count) planMatch = false;
                }
                string planVerdict = planMatch ? "계획과 일치" : $"계획({Names(_planned.Keys)})과 불일치 — 확인 필요";
                string geoVerdict = _hasGeo ? (_expectedGeo.SetEquals(_hitOrder) ? "기하 기대와 일치" : $"기하 기대({Names(_expectedGeo)})와 불일치") : "";
                _sb.Clear();
                foreach (var dummy in _hitOrder)
                {
                    if (_sb.Length > 0) _sb.Append(", ");
                    int count = _actual.TryGetValue(dummy, out var hits) ? hits.Count : 0;
                    _sb.Append(dummy.name);
                    if (count > 1) _sb.Append('×').Append(count);
                }
                Debug.Log($"[Range] '{_castLetter}' 집계 — 명중 {_hitOrder.Count}: {(_sb.Length > 0 ? _sb.ToString() : "없음")} / 미명중: {Names(missed)} · {planVerdict}{(geoVerdict.Length > 0 ? " · " + geoVerdict : "")}");
            }
            else if (_hitOrder.Count == 0)
            {
                Debug.Log($"[Range] '{_castLetter}' 집계 — 과녁 명중 없음(허공·실적 대상·먹 부족 중 하나)");
            }
            else
            {
                _sb.Clear();
                foreach (var dummy in _hitOrder)
                {
                    if (_sb.Length > 0) _sb.Append(", ");
                    _sb.Append(dummy.name).Append(" +").Append(_actual[dummy][0].ToString("0.00")).Append('s');
                }
                Debug.Log($"[Range] '{_castLetter}' 집계 — 명중: {_sb}");
            }
            ClearCast();
        }

        private void ClearCast()
        {
            _casting = false;
            _castFrame = -1;
            _castShape = AreaShape.None;
            _hasGeo = false;
            _planned.Clear();
            _expectedGeo.Clear();
            _actual.Clear();
            _hitOrder.Clear();
        }

        // ---- 안내: 캡션·바닥 선 ----

        private void CaptionDummies()
        {
            float coneAngle = _config != null ? _config.AreaConeAngle : 0f;
            float coneRange = _config != null ? _config.AreaConeRange : 0f;
            foreach (var dummy in _dummies)
            {
                if (dummy == null) continue;
                Vector3 flat = Flat(dummy.transform.position - _origin);
                float dist = flat.magnitude;
                float signed = Vector3.SignedAngle(_forward, flat, Vector3.up);
                float abs = Mathf.Abs(signed);
                string side = abs < 0.5f ? "정면" : (signed < 0f ? $"좌{abs:0}°" : $"우{abs:0}°");
                bool inCone = dist <= coneRange && abs <= coneAngle;
                dummy.SetCaption($"{dummy.name}  {side} {dist:0.#}m  {(inCone ? "IN" : "OUT")}");
            }
        }

        private void BuildGuides()
        {
            _guideMaterial = DevLabel.CreateUnlit(DevLabel.Ink);

            float angle = _config != null ? _config.AreaConeAngle : 40f;
            float range = _config != null ? _config.AreaConeRange : 10f;
            Vector3 ground = new Vector3(_origin.x, GuideHeight, _origin.z);

            // cone 부채꼴 — 판정 기하 그대로(수평·반각·사거리)
            var wedge = new List<Vector3> { ground };
            for (int i = 0; i <= ArcSegments; i++)
            {
                float a = Mathf.Lerp(-angle, angle, i / (float)ArcSegments);
                wedge.Add(ground + Quaternion.AngleAxis(a, Vector3.up) * _forward * range);
            }
            CreateLine("Guide_Cone", wedge, 0.08f, true);

            // 시작점 고리 — 안내선의 기준(재시작 위치)
            var ring = new List<Vector3>();
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                ring.Add(ground + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.6f);
            }
            CreateLine("Guide_Origin", ring, 0.05f, true);
        }

        private void CreateLine(string name, List<Vector3> points, float width, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
            line.widthMultiplier = width;
            line.material = _guideMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = points.Count;
            line.SetPositions(points.ToArray());
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v : Vector3.forward;
        }

        private static string Names(IEnumerable<SpellRangeDummy> dummies)
        {
            var sb = new StringBuilder();
            foreach (var dummy in dummies)
            {
                if (dummy == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(dummy.name);
            }
            return sb.Length > 0 ? sb.ToString() : "없음";
        }
    }
}
