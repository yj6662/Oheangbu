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
    // 글자 채널을 읽어 시전 시각을 재고, 과녁의 피격을 받아 착탄 지연·명중 집합을 콘솔에 남긴다.
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

        [Header("사격장")]
        [SerializeField] private SpellRangeDummy[] _dummies = System.Array.Empty<SpellRangeDummy>();
        [Tooltip("시전 후 집계까지(초, 게임 시계) — 최장 비행(아 12m≈1.1s)보다 길게")]
        [SerializeField, Min(0.5f)] private float _reportDelay = 2.5f;

        private Vector3 _origin;
        private Vector3 _forward;
        private Material _guideMaterial;

        // 진행 중 시전 1건의 계측
        private bool _casting;
        private float _castTime;
        private char _castLetter;
        private bool _castIsCone;
        private readonly Dictionary<SpellRangeDummy, float> _expectedArrival = new Dictionary<SpellRangeDummy, float>();
        private readonly HashSet<SpellRangeDummy> _expectedCone = new HashSet<SpellRangeDummy>();
        private readonly List<SpellRangeDummy> _hitOrder = new List<SpellRangeDummy>();
        private readonly Dictionary<SpellRangeDummy, float> _hitDelay = new Dictionary<SpellRangeDummy, float>();
        private readonly StringBuilder _sb = new StringBuilder();

        private void Start()
        {
            _origin = _player != null ? _player.position : transform.position;
            _forward = Flat(_player != null ? _player.forward : Vector3.forward).normalized;
            BuildGuides();
            CaptionDummies();
            Debug.Log("[Range] 사격장 준비 — F: 상호작용(선택대·귀환) · R: 재시작 · 시전/착탄/집계는 콘솔(시간=게임 시계, 감속 포함)");
        }

        private void OnEnable()
        {
            if (_letterDrawn != null) _letterDrawn.Subscribe(OnLetterDrawn);
            foreach (var dummy in _dummies)
            {
                if (dummy != null) dummy.Hit += OnDummyHit;
            }
        }

        private void OnDisable()
        {
            if (_letterDrawn != null) _letterDrawn.Unsubscribe(OnLetterDrawn);
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

        // ---- 계측: 시전 → 착탄 → 집계 ----

        private void OnLetterDrawn(DrawnLetter letter)
        {
            if (_casting) Report();
            ClearCast();

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

            _casting = true;
            _castTime = Time.time;
            _castLetter = letter.Letter;
            _castIsCone = entry.AreaShape == AreaShape.Cone;
            Vector3 from = _player != null ? _player.position : _origin;

            if (_castIsCone)
            {
                float angle = _config != null ? _config.AreaConeAngle : 0f;
                float range = _config != null ? _config.AreaConeRange : 0f;
                float delay = _config != null ? _config.AreaImpactDelay : 0f;
                Vector3 forward = Flat(_player != null ? _player.forward : _forward);
                foreach (var dummy in _dummies)
                {
                    if (dummy == null || dummy.Vitals == null || !dummy.Vitals.IsAlive) continue;
                    Vector3 flat = Flat(dummy.transform.position - from);
                    _expectedArrival[dummy] = delay;
                    if (flat.magnitude <= range && Vector3.Angle(forward, flat) <= angle) _expectedCone.Add(dummy);
                }
                Debug.Log($"[Range] 시전 '{_castLetter}' 광역 cone {angle:0}°/{range:0.#}m — 판정 +{delay:0.##}s · 기하 예상 명중: {Names(_expectedCone)}");
            }
            else
            {
                float mul = entry.ProjectileSpeedMul > 0f ? entry.ProjectileSpeedMul : 1f;
                float speed = Mathf.Max(1f, (_config != null ? _config.SpellProjectileSpeed : 18f) * mul);
                foreach (var dummy in _dummies)
                {
                    if (dummy == null) continue;
                    _expectedArrival[dummy] = Vector3.Distance(from, dummy.transform.position) / speed;
                }
                Debug.Log($"[Range] 시전 '{_castLetter}' 단일({entry.Kind}) — 탄속 ×{mul:0.##} = {speed:0.#}m/s");
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
            _hitDelay[dummy] = delay;
            if (!_hitOrder.Contains(dummy)) _hitOrder.Add(dummy);
            string expected = _expectedArrival.TryGetValue(dummy, out float exp) ? $" (예상 {exp:0.00}s)" : "";
            Debug.Log($"[Range]   '{_castLetter}' → {dummy.Caption} 착탄 +{delay:0.00}s{expected} 피해 {damage:0.#}");
        }

        private void Report()
        {
            _casting = false;
            if (_castIsCone)
            {
                var missed = new List<SpellRangeDummy>();
                foreach (var dummy in _dummies)
                {
                    if (dummy != null && !_hitOrder.Contains(dummy)) missed.Add(dummy);
                }
                bool match = _expectedCone.SetEquals(_hitOrder);
                string verdict = match ? "기하 예상과 일치" : $"기하 예상({Names(_expectedCone)})과 불일치 — 확인 필요";
                Debug.Log($"[Range] '{_castLetter}' 집계 — 명중 {_hitOrder.Count}: {Names(_hitOrder)} / 미명중: {Names(missed)} · {verdict}");
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
                    _sb.Append(dummy.name).Append(" +").Append(_hitDelay[dummy].ToString("0.00")).Append('s');
                }
                Debug.Log($"[Range] '{_castLetter}' 집계 — 명중: {_sb}");
            }
            ClearCast();
        }

        private void ClearCast()
        {
            _casting = false;
            _castIsCone = false;
            _expectedArrival.Clear();
            _expectedCone.Clear();
            _hitOrder.Clear();
            _hitDelay.Clear();
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
