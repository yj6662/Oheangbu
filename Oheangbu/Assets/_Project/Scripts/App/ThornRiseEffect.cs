using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    // [P4 — 고(木 광역) 솟음 연출] 지정 영역(락온 대상 발밑, 없으면 허공 착탄점의 지면)에서
    // 가시 클러스터들이 시간차로 땅을 뚫고 솟구쳤다가, 짧게 잔존하고, 도로 가라앉는다.
    // 곡직(曲直) — 클러스터마다 무작위 요·틸트로 굽은 생목의 결. 솟는 순간 흙 파편 소량(소 문법 승계).
    // 피해는 배선의 단일 착탄 시계 그대로 — 연출≠실판정(SPEC-SPELL-FX-ASSETS §9-1).
    public sealed class ThornRiseEffect : SpellSequenceEffect
    {
        [SerializeField] private Mesh _thornMesh;      // 생성 가시 클러스터(첨단축 +Z — §4.2-6 수칙)
        [SerializeField] private Material _material;
        [SerializeField, Min(1)] private int _count = 5;
        [SerializeField] private float _areaRadius = 1.6f;   // 솟음 산개 반경(지면)
        [SerializeField] private float _spawnInterval = 0.04f; // 시간차 솟음 — 일제이되 파도치듯
        [SerializeField] private float _riseTime = 0.16f;    // 즉발 — 빠르게 뚫고 나온다
        [SerializeField] private float _buriedDepth = 1.7f;  // 시작 매장 깊이(m) — 메시가 지면에 완전히 숨는 값
        [SerializeField] private float _holdTime = 0.7f;     // 잔존 — 「솟았다」가 읽히는 시간
        [SerializeField] private float _sinkTime = 0.35f;    // 가라앉음
        [SerializeField] private float _thornScale = 1.5f;
        [SerializeField] private float _tiltMax = 18f;       // 곡직 — 클러스터별 무작위 기울기(도)
        [SerializeField] private int _burstShards = 3;       // 솟는 순간 흙 파편
        [SerializeField] private float _burstShardScale = 0.14f;

        private enum Phase { Wait, Rise, Hold, Sink, Done }

        private sealed class Thorn
        {
            public Transform Tr;
            public Phase Phase;
            public float PhaseStart;
            public float Delay;
            public Vector3 GroundPos;
            public Quaternion Rot;
            public float Scale;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly List<Thorn> _thorns = new List<Thorn>();
        private Material _ownedMaterial;
        private float _beginTime;
        private bool _running;
        private Color _tint;

        public override void Begin(Vector3 origin, Transform target, Vector3 fallbackPoint, Color tint)
        {
            transform.SetParent(null, true);
            _tint = tint;
            _beginTime = Time.time;
            _running = true;
            if (_material != null)
            {
                _ownedMaterial = new Material(_material); // 인스턴스 1장 공유 — 원본 불변
                if (_ownedMaterial.HasProperty(BaseColorId)) _ownedMaterial.SetColor(BaseColorId, tint);
                if (_ownedMaterial.HasProperty(ColorId)) _ownedMaterial.SetColor(ColorId, tint);
            }

            // 솟음 중심 = 대상 발밑(락온) / 허공 착탄점의 지면 투영(비명중) — 시전자 높이의 흔들림 배제
            Vector3 center = target != null ? target.position : fallbackPoint;
            center.y = origin.y - 1.2f; // 문양(가슴 높이) 아래 지면 근사 — 개발 씬 평지 기준 [TEST]
            if (target != null) center.y = target.position.y;
            transform.position = center;

            for (int i = 0; i < _count; i++)
            {
                Vector2 disc = Random.insideUnitCircle * _areaRadius;
                var thorn = new Thorn
                {
                    Delay = i * _spawnInterval,
                    GroundPos = center + new Vector3(disc.x, 0f, disc.y),
                    // 첨단=+Z를 위로 세우고, 곡직의 결 — 무작위 요·틸트
                    Rot = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up)
                        * Quaternion.AngleAxis(Random.Range(0f, _tiltMax), Vector3.right)
                        * Quaternion.FromToRotation(Vector3.forward, Vector3.up),
                    Scale = _thornScale * Random.Range(0.75f, 1.15f),
                    Phase = Phase.Wait,
                };
                _thorns.Add(thorn);
            }
        }

        private void Update()
        {
            if (!_running) return;
            float now = Time.time;
            bool allDone = true;

            foreach (var t in _thorns)
            {
                if (t.Phase == Phase.Done) continue;
                allDone = false;

                if (t.Phase == Phase.Wait)
                {
                    if (now - _beginTime < t.Delay) continue;
                    t.Tr = SpawnThornTransform(t);
                    t.Phase = Phase.Rise;
                    t.PhaseStart = now;
                    SpawnShards(t.GroundPos, t.Scale);
                }
                if (t.Tr == null) { t.Phase = Phase.Done; continue; }

                float p = now - t.PhaseStart;
                switch (t.Phase)
                {
                    case Phase.Rise: // 땅속에서 솟구침 — 지면이 가려주는 아래에서 밀고 올라온다(피벗=바닥)
                    {
                        float k = Mathf.Clamp01(p / _riseTime);
                        float ease = 1f - (1f - k) * (1f - k); // EaseOutQuad — 뚫는 기세
                        t.Tr.position = t.GroundPos + Vector3.up * (_buriedDepth * (ease - 1f));
                        if (k >= 1f) { t.Phase = Phase.Hold; t.PhaseStart = now; }
                        break;
                    }
                    case Phase.Hold:
                        if (p >= _holdTime) { t.Phase = Phase.Sink; t.PhaseStart = now; }
                        break;
                    case Phase.Sink: // 도로 가라앉는다 — 남는 것 없음(잔존·발광 금지)
                    {
                        float k = Mathf.Clamp01(p / _sinkTime);
                        t.Tr.position = t.GroundPos - Vector3.up * (_buriedDepth * k * k); // EaseIn — 스르륵 잠김
                        if (k >= 1f)
                        {
                            Destroy(t.Tr.gameObject);
                            t.Phase = Phase.Done;
                        }
                        break;
                    }
                }
            }

            if (allDone)
            {
                _running = false;
                Destroy(gameObject, 0.1f);
            }
        }

        private void OnDestroy()
        {
            if (_ownedMaterial != null) Destroy(_ownedMaterial);
        }

        private Transform SpawnThornTransform(Thorn t)
        {
            if (_thornMesh == null || _ownedMaterial == null) return null;
            var go = new GameObject("Thorn");
            go.transform.SetPositionAndRotation(t.GroundPos - Vector3.up * _buriedDepth, t.Rot);
            go.transform.localScale = Vector3.one * t.Scale;
            go.transform.SetParent(transform, true); // 수명 동반 — 위치는 매 프레임 월드로 구동
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = _thornMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _ownedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        // 솟는 순간의 흙 파편 — 소 착탄 파편(SpikeImpactShard) 재사용: 위로 튀고 중력 낙하·축소 소멸
        private void SpawnShards(Vector3 point, float scale)
        {
            if (_thornMesh == null || _ownedMaterial == null) return;
            for (int i = 0; i < _burstShards; i++)
            {
                var go = new GameObject("ThornShard");
                go.transform.position = point + Vector3.up * 0.1f;
                go.transform.rotation = Random.rotation;
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = _thornMesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _ownedMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Vector3 dir = new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(0.7f, 1f), Random.Range(-0.6f, 0.6f)).normalized;
                go.AddComponent<SpikeImpactShard>().Init(dir, _burstShardScale * scale);
            }
        }
    }
}
