using UnityEngine;

namespace Oheangbu.App
{
    // [SPELL-FIDELITY §4.4 — 가 「단일 대상에 곧게 뻗는 생목 가시」] 전용 구현(#138).
    // 문법: 글자 자리에서 대상까지 가시 세그먼트가 순차로 「뻗는다」 — 투사가 아니라 성장.
    // 도달 시계 = 배선의 판정 비행시간(SetImpactClock — 피해=착탄 동기화). 스파인 끝점은 대상을
    // 추적 갱신한다: 뻗으며 휘는 생목(유도 보장 명중과 시각이 일치). 도달 후 잔존→침강 소멸.
    // 메시=기존 자원 재사용(#138 허용면 — 첨단 +Z 규격, SPELL-FX §4.2-6), 문법은 전용.
    public sealed class ThornLanceEffect : SpellSequenceEffect
    {
        [Header("자원 — 첨단 +Z 규격 메시(비추적 에셋 GUID 참조)")]
        [SerializeField] private Mesh _segmentMesh;
        [SerializeField] private Material _material;

        [Header("랜스 [TEST §7]")]
        [SerializeField, Range(2, 12)] private int _segmentCount = 6;
        [SerializeField, Min(0.05f)] private float _growTime = 0.25f;  // SetImpactClock이 오버라이드
        [SerializeField, Min(0f)] private float _holdTime = 0.4f;
        [SerializeField, Min(0.05f)] private float _sinkTime = 0.3f;
        [SerializeField, Min(0.05f)] private float _rootScale = 0.9f;  // 뿌리 세그먼트 크기(m)
        [SerializeField, Min(0.05f)] private float _tipScale = 0.45f;  // 끝 세그먼트 크기 — 갈수록 뾰족
        [SerializeField, Min(0f)] private float _waviness = 0.25f;     // 스파인 곡률(수직 처짐+좌우 요) — 생목의 굽음

        private Transform[] _segments;
        private Vector3 _origin;
        private Transform _target;
        private Vector3 _fallback;
        private float _startTime = -1f;
        private float _seed;
        private Material _ownedMaterial;

        public override void SetImpactClock(float flightDuration)
        {
            _growTime = Mathf.Max(0.05f, flightDuration); // 판정 착탄 순간 = 랜스 끝이 대상에 닿는 순간
        }

        public override void Begin(Vector3 origin, Transform target, Vector3 fallbackPoint, Color tint)
        {
            transform.SetParent(null, true); // 부모 문양 수명(2.2s)과 분리 — 자체 완주
            _origin = origin;
            _target = target;
            _fallback = fallbackPoint;
            _seed = Random.value * 100f;
            _startTime = Time.time;

            _segments = new Transform[_segmentCount];
            _ownedMaterial = _material != null ? new Material(_material) : null;
            if (_ownedMaterial != null)
            {
                if (_ownedMaterial.HasProperty("_Color")) _ownedMaterial.SetColor("_Color", tint);
                if (_ownedMaterial.HasProperty("_BaseColor")) _ownedMaterial.SetColor("_BaseColor", tint);
            }
            for (int i = 0; i < _segmentCount; i++)
            {
                var go = new GameObject($"LanceSeg_{i}");
                go.transform.SetParent(transform, false);
                if (_segmentMesh != null) go.AddComponent<MeshFilter>().sharedMesh = _segmentMesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (_ownedMaterial != null) renderer.sharedMaterial = _ownedMaterial;
                go.transform.localScale = Vector3.zero; // 성장 전 은신
                _segments[i] = go.transform;
            }
        }

        private void Update()
        {
            if (_startTime < 0f) return;
            float elapsed = Time.time - _startTime;
            float reveal = Mathf.Clamp01(elapsed / _growTime);
            float sinkT = Mathf.Clamp01((elapsed - _growTime - _holdTime) / _sinkTime);

            Vector3 goal = _target != null ? _target.position + Vector3.up * 1.1f : _fallback;

            for (int i = 0; i < _segments.Length; i++)
            {
                var seg = _segments[i];
                if (seg == null) continue;
                float u = (i + 1f) / _segments.Length; // 스파인상 위치(뿌리 제외 — 글자 밖부터)

                // 스파인: 직선 보간 + 생목의 굽음(수직 처짐·좌우 요 — 세그먼트 고정 시드)
                Vector3 straight = Vector3.Lerp(_origin, goal, u);
                float bend = Mathf.Sin(u * Mathf.PI) * _waviness;
                Vector3 side = Vector3.Cross((goal - _origin).normalized, Vector3.up);
                straight += Vector3.down * (bend * 0.5f)
                    + side * (bend * (Mathf.PerlinNoise(_seed, i * 0.7f) - 0.5f) * 2f);
                seg.position = straight;

                // 첨단(+Z)은 다음 지점을 향한다 — 뻗는 방향이 곧 시선
                Vector3 ahead = (i + 1 < _segments.Length ? Vector3.Lerp(_origin, goal, (i + 2f) / _segments.Length) : goal) - straight;
                if (ahead.sqrMagnitude > 0.001f) seg.rotation = Quaternion.LookRotation(ahead.normalized);

                // 순차 성장: reveal이 자기 자리를 지나면 EaseOutBack 팝 — 「뻗는」 리듬
                float local = Mathf.Clamp01((reveal - (u - 1f / _segments.Length)) * _segments.Length);
                float pop = 1f + 1.6f * Mathf.Pow(local - 1f, 3f) + 1.6f * Mathf.Pow(local - 1f, 2f); // EaseOutBack
                float size = Mathf.Lerp(_rootScale, _tipScale, u) * Mathf.Max(0f, pop);
                if (sinkT > 0f) size *= 1f - sinkT * sinkT; // 침강 — 스러지는 생목
                seg.localScale = Vector3.one * size;
            }

            if (elapsed >= _growTime + _holdTime + _sinkTime) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_ownedMaterial != null) Destroy(_ownedMaterial);
        }
    }
}
