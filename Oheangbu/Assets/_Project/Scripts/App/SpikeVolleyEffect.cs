using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    // [8차 검수 — 소(金 광역) 일제 연출] 문양 개화 자리에서 송곳들이 하나씩 형성되고, 짧게 멈췄다가
    // 살짝 뒤로 당겨진 뒤, 무작위 타이밍에 넓은 산포로 발사된다. 착탄점엔 작은 금속 파편만.
    // 투사체 개별 파티클 없음 — 생성·발사·도착 시점에만 집중한다(예준 지시).
    // 피해는 배선의 단일 착탄 시계 그대로 — 연출≠실판정(SPEC-SPELL-FX-ASSETS §9-1).
    // 결합 프리팹의 자식(Volley)에 두고, 어댑터가 커밋 프레임에 Begin으로 떼어내 자체 시계로 굴린다.
    public sealed class SpikeVolleyEffect : MonoBehaviour
    {
        [SerializeField] private Mesh _spikeMesh;
        [SerializeField] private Material _material;
        [SerializeField, Min(1)] private int _count = 9;
        [SerializeField] private float _areaRadius = 1.4f;   // 형성 산개 반경(조준축 수직면)
        [SerializeField] private float _spawnInterval = 0.07f; // 하나씩 형성되는 간격
        [SerializeField] private float _growTime = 0.22f;    // 하나씩 돋는 게 읽히는 속도
        [SerializeField] private float _holdTime = 1.1f;     // 멈춤 — 「소환됐다」가 읽히는 시간(10차 검수 재연장)
        [SerializeField] private float _holdRise = 0.5f;     // 홀드 동안 위로 떠오르는 높이
        [SerializeField] private float _holdSpread = 0.45f;  // 홀드 동안 사방으로 벌어지는 폭
        [SerializeField] private float _pullback = 0.3f;     // 발사 예비 — 살짝 뒤로
        [SerializeField] private float _pullbackTime = 0.09f;
        [SerializeField] private float _launchJitter = 0.28f; // 무작위 발사 타이밍 폭
        [SerializeField] private float _flightSpeed = 32f;
        [SerializeField] private float _spikeScale = 0.95f;  // 10차 검수 — 존재감 있는 크기
        [SerializeField] private float _spreadRadius = 1.1f; // 착탄 산포 — 넓은 범위 타격의 읽기
        [SerializeField] private int _impactShards = 5;
        [SerializeField] private float _impactShardScale = 0.16f;
        [SerializeField] private float _trailTime = 0.18f;  // 발사 궤적 잔류 시간(12차 검수) [TEST]
        [SerializeField] private float _trailWidth = 0.07f; // 발사 궤적 폭 [TEST]

        private enum Phase { Grow, Hold, Pull, Wait, Fly, Done }

        private sealed class Spike
        {
            public Transform Tr;
            public Phase Phase;
            public float PhaseStart;
            public float LaunchDelay;
            public Vector3 SpawnPos;
            public Vector3 AimOffset;
            public float Seed; // 부유 위상 — 저마다 다르게 떠 있는다
            public Vector3 HoldEndPos; // 홀드 종료 위치 — 당김·발사의 기준점
            public Quaternion RestRot; // 생성 자세 — 가로로 누운 상태(11차 검수)
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly List<Spike> _spikes = new List<Spike>();
        private Material _ownedMaterial;
        private Transform _target;
        private Vector3 _fallbackPoint;
        private Vector3 _origin;
        private float _beginTime;
        private bool _running;
        private bool _endScheduled;
        private Color _tint;

        // 커밋 프레임에 어댑터가 호출 — 문양(부모)의 수명과 분리해 자체로 달린다
        public void Begin(Vector3 origin, Transform target, Vector3 fallbackPoint, Color tint)
        {
            transform.SetParent(null, true);
            transform.position = origin;
            _origin = origin;
            _target = target;
            _fallbackPoint = fallbackPoint;
            _tint = tint;
            _beginTime = Time.time;
            _running = true;
            if (_material != null)
            {
                _ownedMaterial = new Material(_material); // 인스턴스 1장 공유 — 원본 불변
                if (_ownedMaterial.HasProperty(BaseColorId)) _ownedMaterial.SetColor(BaseColorId, tint);
                if (_ownedMaterial.HasProperty(ColorId)) _ownedMaterial.SetColor(ColorId, tint);
            }
        }

        private void Update()
        {
            if (!_running) return;
            float now = Time.time;
            Vector3 aim = CurrentTargetPos() - _origin;
            aim = aim.sqrMagnitude > 0.001f ? aim.normalized : Vector3.forward;

            int due = Mathf.Min(_count, 1 + (int)((now - _beginTime) / Mathf.Max(0.01f, _spawnInterval)));
            while (_spikes.Count < due) SpawnSpike(aim);

            bool allDone = _spikes.Count >= _count;
            foreach (var s in _spikes)
            {
                if (s.Phase == Phase.Done) continue;
                if (s.Tr == null) { s.Phase = Phase.Done; continue; }
                allDone = false;
                float t = now - s.PhaseStart;
                switch (s.Phase)
                {
                    case Phase.Grow: // 하나씩 돋아난다 — 가로로 누운 채(11차 검수), 살짝 튀는 팝인
                        s.Tr.rotation = s.RestRot;
                        s.Tr.localScale = Vector3.one * (_spikeScale * EaseOutBack(Mathf.Clamp01(t / _growTime)));
                        if (t >= _growTime) Next(s, Phase.Hold, now);
                        break;
                    case Phase.Hold: // 사방으로 벌어지며 떠오르고, 뾰족한 끝이 착탄점으로 자연히 돌아선다(11차)
                    {
                        float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / _holdTime));
                        Vector3 outward = s.SpawnPos - _origin;
                        outward -= aim * Vector3.Dot(outward, aim); // 조준축 성분 제거 — 수직면 방사
                        outward = outward.sqrMagnitude > 0.001f ? outward.normalized : Vector3.up;
                        Vector3 held = s.SpawnPos + outward * (_holdSpread * p) + Vector3.up * (_holdRise * p);
                        s.Tr.position = held
                            + Vector3.up * (Mathf.Sin((now - s.PhaseStart) * 5f + s.Seed) * 0.05f);
                        // 회전은 홀드 80% 지점에 정렬 완료 — 남은 시간은 조준한 채 숨을 고른다
                        float rp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / (_holdTime * 0.8f)));
                        s.Tr.rotation = Quaternion.Slerp(s.RestRot, AimRotOf(s), rp);
                        if (t >= _holdTime)
                        {
                            s.HoldEndPos = held;
                            s.Tr.position = held;
                            Next(s, Phase.Pull, now);
                        }
                        break;
                    }
                    case Phase.Pull: // 시위를 당기듯 살짝 뒤로 — 홀드가 끝난 그 자리에서, 조준 유지
                        s.Tr.rotation = AimRotOf(s);
                        s.Tr.position = s.HoldEndPos - aim * (_pullback * Mathf.Clamp01(t / _pullbackTime));
                        if (t >= _pullbackTime) Next(s, Phase.Wait, now);
                        break;
                    case Phase.Wait: // 무작위 타이밍 발사 — 일제가 아니라 흩뿌리는 속사(조준은 대상 추적)
                        s.Tr.rotation = AimRotOf(s);
                        if (t >= s.LaunchDelay)
                        {
                            Next(s, Phase.Fly, now);
                            // 발사 순간의 늘어남 + 궤적·킥(12차 검수 — 발사 방식 확정에 따른 시점 강조)
                            s.Tr.localScale = new Vector3(_spikeScale * 0.75f, _spikeScale * 0.75f, _spikeScale * 1.7f);
                            AttachTrail(s.Tr);
                            SpawnShards(s.Tr.position, Vector3.zero, 2, _impactShardScale * 0.7f);
                        }
                        break;
                    case Phase.Fly:
                        Vector3 dest = CurrentTargetPos() + s.AimOffset;
                        Vector3 dir = dest - s.Tr.position;
                        float step = _flightSpeed * Time.deltaTime;
                        if (dir.magnitude <= step)
                        {
                            SpawnImpact(dest, dir.sqrMagnitude > 0.001f ? dir.normalized : aim);
                            ReleaseTrail(s.Tr);
                            Destroy(s.Tr.gameObject);
                            s.Phase = Phase.Done;
                        }
                        else
                        {
                            Vector3 flyDir = dir.normalized;
                            s.Tr.position += flyDir * step;
                            s.Tr.rotation = Quaternion.FromToRotation(Vector3.forward, flyDir);
                        }
                        break;
                }
            }

            if (allDone && !_endScheduled)
            {
                _endScheduled = true;
                _running = false;
                Destroy(gameObject, 0.1f);
            }
        }

        private void SpawnSpike(Vector3 aim)
        {
            Vector3 right = Vector3.Cross(aim, Vector3.up);
            right = right.sqrMagnitude > 0.001f ? right.normalized : Vector3.right;
            Vector3 up = Vector3.Cross(right, aim).normalized;
            Vector2 disc = Random.insideUnitCircle * _areaRadius;
            Vector3 pos = _origin + right * disc.x + up * (disc.y * 0.7f + 0.35f);

            // 생성 자세 = 가로로 누움(11차 검수) — 조준축의 수평 수직 방향, 좌우·기울기는 저마다 조금씩.
            // 메시 기준축 = +Z(12차 실측 — Blender FBX 정점이 Z-업 그대로, bake 무효였다)
            Vector3 side = Vector3.Cross(aim, Vector3.up);
            side = side.sqrMagnitude > 0.001f ? side.normalized : Vector3.right;
            if (Random.value < 0.5f) side = -side;
            Quaternion restRot = Quaternion.AngleAxis(Random.Range(-20f, 20f), Vector3.up)
                * Quaternion.FromToRotation(Vector3.forward, side);

            var go = new GameObject("Spike");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.transform.rotation = restRot;
            go.transform.localScale = Vector3.zero;
            AddMesh(go, _ownedMaterial != null ? _ownedMaterial : _material);

            _spikes.Add(new Spike
            {
                Tr = go.transform,
                Phase = Phase.Grow,
                PhaseStart = Time.time,
                LaunchDelay = Random.Range(0f, _launchJitter),
                SpawnPos = pos,
                AimOffset = Random.insideUnitSphere * _spreadRadius,
                Seed = Random.Range(0f, 6.28f),
                RestRot = restRot,
            });
            // 형성 글린트는 10차 검수로 제거 — 작도 완성 시점엔 에셋 문양 이외의 이펙트를 두지 않는다
        }

        // 착탄 — 큰 이펙트 없이 작은 금속 파편(같은 송곳 메시의 축소 조각이 튀며 잘게 스러진다)
        private void SpawnImpact(Vector3 point, Vector3 inDir)
        {
            SpawnShards(point, -inDir * 0.6f, _impactShards, _impactShardScale);
        }

        private void SpawnShards(Vector3 point, Vector3 bias, int count, float scale)
        {
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("SpikeShard");
                go.transform.position = point;
                Vector3 dir = (bias + Random.insideUnitSphere).normalized;
                go.transform.rotation = Quaternion.FromToRotation(Vector3.forward, dir);
                AddMesh(go, _ownedMaterial != null ? _ownedMaterial : _material);
                go.AddComponent<SpikeImpactShard>().Init(dir, scale);
            }
        }

        // 발사 궤적 — 자식 GO의 TrailRenderer(12차 검수). 착탄 시 떼어내 잔필이 자연히 스러진다
        private void AttachTrail(Transform spike)
        {
            if (_trailTime <= 0f || _material == null) return;
            var go = new GameObject("Trail");
            go.transform.SetParent(spike, false);
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = _trailTime;
            trail.startWidth = _trailWidth;
            trail.endWidth = 0f;
            trail.material = _ownedMaterial != null ? _ownedMaterial : _material;
            trail.startColor = _tint;
            trail.endColor = new Color(_tint.r, _tint.g, _tint.b, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
        }

        private static void ReleaseTrail(Transform spike)
        {
            var trail = spike.GetComponentInChildren<TrailRenderer>();
            if (trail == null) return;
            trail.transform.SetParent(null, true);
            trail.autodestruct = true; // 잔필이 다 스러지면 스스로 사라진다
        }

        private void AddMesh(GameObject go, Material material)
        {
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = _spikeMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private Vector3 CurrentTargetPos()
        {
            return _target != null ? _target.position + Vector3.up * 1.1f : _fallbackPoint;
        }

        // 각자의 착탄점(산포 오프셋 포함)을 향한 조준 자세 — 뾰족한 끝(+Z)이 그리로 돌아선다
        private Quaternion AimRotOf(Spike s)
        {
            Vector3 dir = CurrentTargetPos() + s.AimOffset - s.Tr.position;
            return dir.sqrMagnitude > 0.001f
                ? Quaternion.FromToRotation(Vector3.forward, dir.normalized)
                : s.Tr.rotation;
        }

        private void Next(Spike s, Phase phase, float now)
        {
            s.Phase = phase;
            s.PhaseStart = now;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            t -= 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }

        private void OnDestroy()
        {
            if (_ownedMaterial != null) Destroy(_ownedMaterial);
        }
    }

    // 착탄 파편 낱개 — 금속이 유리처럼 잘게 깨져 튀고 가라앉으며 줄어든다(0.3s 소멸·발광 없음)
    internal sealed class SpikeImpactShard : MonoBehaviour
    {
        private const float Life = 0.3f;
        private Vector3 _velocity;
        private float _startTime;
        private float _scale;

        public void Init(Vector3 dir, float scale)
        {
            _velocity = dir * Random.Range(2.2f, 4f);
            _scale = scale * Random.Range(0.6f, 1.3f);
            _startTime = Time.time;
            transform.localScale = Vector3.one * _scale;
        }

        private void Update()
        {
            float t = (Time.time - _startTime) / Life;
            if (t >= 1f) { Destroy(gameObject); return; }
            _velocity += Vector3.down * (6f * Time.deltaTime);
            transform.position += _velocity * Time.deltaTime;
            transform.localScale = Vector3.one * (_scale * (1f - t));
        }
    }
}
