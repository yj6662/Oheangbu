using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    // [8차 검수 — 소(金 광역) 일제 연출] 문양 개화 자리에서 송곳들이 하나씩 형성되고, 짧게 멈췄다가
    // 살짝 뒤로 당겨진 뒤, 무작위 타이밍에 넓은 산포로 발사된다. 착탄점엔 작은 금속 파편만.
    // 투사체 개별 파티클 없음 — 생성·발사·도착 시점에만 집중한다(예준 지시).
    // 피해는 배선의 단일 착탄 시계 그대로 — 연출≠실판정(SPEC-SPELL-FX-ASSETS §9-1).
    // 결합 프리팹의 자식(Volley)에 두고, 어댑터가 커밋 프레임에 Begin으로 떼어내 자체 시계로 굴린다.
    // [SPEC-SPELL-FX-REWORK §3.2 — 벼린 쇠 송곳] 전용 재질(InkMetal)·전용 트레일 재질·칩 메시 슬롯,
    // 첨단축 롤(홀드=면 위를 흐르는 광택 / 비행=드릴 글린트)·이방성 벼림 팝인·발사 명도 플래시(MPB).
    // 전부 표현 계층 — 판정 경로(SetAreaPlan·LaunchAt 역산·Fly 도달 판정)는 무변경.
    public sealed class SpikeVolleyEffect : SpellSequenceEffect
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

        [Header("소 실체 자원 슬롯 [TEST — FX-REWORK §3.2] 비우면 송곳 재질·메시 폴백(기존 동작 보존)")]
        [SerializeField] private Material _trailMaterial; // 발사 궤적 전용 투명 재질(M_SpellTrail_So) — 알파 페이드 회복·송곳 재질 전이 차단
        [SerializeField] private Mesh _shardMesh;         // 킥·착탄 칩 메시(AwlChip_So ≈100tris) — 동시 폴리 예산(칩 폴백 시 ≈24k)
        [SerializeField, Min(0f)] private float _trailMinVertexDistance = 0.05f; // 궤적 정점 간격(m)

        [Header("소 거동 [TEST — FX-REWORK §3.2] 롤·벼림·발사 플래시 — 표현 전용(판정 시계 무접촉)")]
        [SerializeField] private float _holdRollDeg = 90f;       // 홀드 중 첨단축 롤 총량(°) — 플랫 면이 광택을 쓸고 지나간다(TEST 폭 0~90)
        [SerializeField] private float _flyRollDegPerSec = 540f; // 비행 드릴 롤(°/s) — 면·모서리 띠가 교대로 빛을 받는 글린트(0 허용)
        [SerializeField, Min(0f)] private float _growXYLag = 0.05f; // 이방성 팝인 — 첨단축(z)이 먼저, 단면(xy)은 이만큼 늦게(벼림)
        [SerializeField, Range(0f, 0.5f)] private float _launchFlashLift = 0.12f; // 발사 순간 명도 상승 비율(채도·색상 불변 — ART-INK-LOOK §4.1)
        [SerializeField, Min(0f)] private float _launchFlashTime = 0.08f; // 플래시 지속(s) — 경과 후 MPB 해제
        [SerializeField, Min(0f)] private float _endLinger = 0.35f; // 마지막 착탄 칩(0.3s)·궤적(0.18s)이 재질 파괴 전에 스러지는 여유 [TEST]
        [SerializeField] private bool _ignoreLetterScale = false; // 인게임 송곳 크기 고정(글자 크기 상속 차단) — 예준 승인 시 true(§8 예외 8)

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
            public Transform Target;   // 판정 계획의 배정 대상(없으면 공통 대상/허공)
            public float LaunchAt;     // 계획 발사 시각(절대, 착탄 시각−비행시간) — 음수=무계획(무작위 지터)
            public float RollSeed;     // 첨단축 초기 롤(°) — 면 방향이 발마다 달라 은백 톤이 저마다 잡힌다
            public float Roll;         // 현재 첨단축 롤(°) — 홀드 0→_holdRollDeg, 비행은 드릴
            public MeshRenderer Rend;  // 발사 플래시 MPB 대상
            public float FlyStart;     // 발사 시각 — 플래시 해제 기준
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly List<Spike> _spikes = new List<Spike>();
        private Material _ownedMaterial;
        private MaterialPropertyBlock _flashBlock; // 발사 플래시 — 1개 재사용(SetPropertyBlock이 값을 복사한다)
        private Transform _target;
        private Vector3 _fallbackPoint;
        private Vector3 _origin;
        private float _beginTime;
        private bool _running;
        private bool _endScheduled;
        private Color _tint;
        private AreaImpactPlan _plan;

        // 판정 계획 [SPELL-AREA-SHAPES §3] — 발마다 배정 대상과 착탄 시각. 발사 시각=착탄−비행시간으로 역산
        public override void SetAreaPlan(AreaImpactPlan plan)
        {
            _plan = plan != null && plan.Shots.Count > 0 ? plan : null;
        }

        // 커밋 프레임에 어댑터가 호출 — 문양(부모)의 수명과 분리해 자체로 달린다
        public override void Begin(Vector3 origin, Transform target, Vector3 fallbackPoint, Color tint)
        {
            transform.SetParent(null, true);
            if (_ignoreLetterScale) transform.localScale = Vector3.one; // 문양 루트 스케일(letterSize×0.6) 상속 차단 — 크기 기준 통일
            transform.position = origin;
            _origin = origin;
            _target = target;
            _fallbackPoint = fallbackPoint;
            _tint = tint;
            _beginTime = Time.time;
            _running = true;
            if (_plan != null) _count = _plan.Shots.Count; // 발수=판정 발수
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
                    case Phase.Grow: // 하나씩 돋아난다 — 가로로 누운 채(11차 검수), 이방성 팝인: 첨단축이 먼저 뽑히고 단면이 뒤따른다(벼림)
                    {
                        float pz = EaseOutBack(Mathf.Clamp01(t / _growTime));
                        float pxy = EaseOutBack(Mathf.Clamp01((t - _growXYLag) / Mathf.Max(0.01f, _growTime - _growXYLag)));
                        s.Tr.rotation = s.RestRot * RollQ(s.RollSeed);
                        s.Tr.localScale = new Vector3(_spikeScale * pxy, _spikeScale * pxy, _spikeScale * pz);
                        if (t >= _growTime)
                        {
                            s.Tr.localScale = Vector3.one * _spikeScale;
                            Next(s, Phase.Hold, now);
                        }
                        break;
                    }
                    case Phase.Hold: // 사방으로 벌어지며 떠오르고, 뾰족한 끝이 착탄점으로 자연히 돌아선다(11차)
                    {
                        float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / _holdTime));
                        Vector3 outward = s.SpawnPos - _origin;
                        outward -= aim * Vector3.Dot(outward, aim); // 조준축 성분 제거 — 수직면 방사
                        outward = outward.sqrMagnitude > 0.001f ? outward.normalized : Vector3.up;
                        Vector3 held = s.SpawnPos + outward * (_holdSpread * p) + Vector3.up * (_holdRise * p);
                        s.Tr.position = held
                            + Vector3.up * (Mathf.Sin((now - s.PhaseStart) * 5f + s.Seed) * 0.05f);
                        // 회전은 홀드 80% 지점에 정렬 완료 — 남은 시간은 조준한 채 숨을 고른다.
                        // 첨단축 롤 0→_holdRollDeg(홀드 전체) — 플랫 면이 정반사·지평선 반사를 한 번 쓸고 지나간다(반짝임=반사 이동, 발광 아님)
                        float rp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / (_holdTime * 0.8f)));
                        s.Roll = s.RollSeed + _holdRollDeg * p;
                        s.Tr.rotation = Quaternion.Slerp(s.RestRot, AimRotOf(s), rp) * RollQ(s.Roll);
                        if (t >= _holdTime)
                        {
                            s.HoldEndPos = held;
                            s.Tr.position = held;
                            Next(s, Phase.Pull, now);
                        }
                        break;
                    }
                    case Phase.Pull: // 시위를 당기듯 살짝 뒤로 — 홀드가 끝난 그 자리에서, 조준 유지·롤 고정
                        s.Tr.rotation = AimRotOf(s) * RollQ(s.Roll);
                        s.Tr.position = s.HoldEndPos - aim * (_pullback * Mathf.Clamp01(t / _pullbackTime));
                        if (t >= _pullbackTime) Next(s, Phase.Wait, now);
                        break;
                    case Phase.Wait: // 발사 — 계획이 있으면 착탄 시각 역산, 없으면 무작위 지터(흩뿌리는 속사)
                        s.Tr.rotation = AimRotOf(s) * RollQ(s.Roll);
                        if (s.LaunchAt >= 0f ? now >= s.LaunchAt : t >= s.LaunchDelay)
                        {
                            Next(s, Phase.Fly, now);
                            s.FlyStart = now;
                            // 발사 순간의 늘어남 + 궤적·킥(12차 검수 — 발사 방식 확정에 따른 시점 강조) + 명도 플래시
                            s.Tr.localScale = new Vector3(_spikeScale * 0.75f, _spikeScale * 0.75f, _spikeScale * 1.7f);
                            AttachTrail(s.Tr);
                            SpawnShards(s.Tr.position, Vector3.zero, 2, _impactShardScale * 0.7f);
                            ApplyLaunchFlash(s);
                        }
                        break;
                    case Phase.Fly:
                        // 플래시 해제 — 재질 틴트로 복귀(순간 명도만, 잔광 없음)
                        if (s.Rend != null && now - s.FlyStart > _launchFlashTime && s.Rend.HasPropertyBlock())
                            s.Rend.SetPropertyBlock(null);
                        Vector3 dest = TargetPosOf(s) + s.AimOffset;
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
                            s.Roll += _flyRollDegPerSec * Time.deltaTime; // 드릴 롤 — 실루엣 불변, 면만 돌아 글린트가 깜빡인다
                            s.Tr.rotation = Quaternion.FromToRotation(Vector3.forward, flyDir) * RollQ(s.Roll);
                        }
                        break;
                }
            }

            if (allDone && !_endScheduled)
            {
                _endScheduled = true;
                _running = false;
                Destroy(gameObject, _endLinger); // OnDestroy가 _ownedMaterial을 파괴 — 칩·궤적 폴백이 그 재질을 참조하므로 먼저 스러질 여유
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
            var rend = AddMesh(go, _ownedMaterial != null ? _ownedMaterial : _material, _spikeMesh);
            float rollSeed = Random.Range(0f, 360f); // 첨단축 롤 시드 — 9발의 면 방향이 전부 다르다

            var spike = new Spike
            {
                Tr = go.transform,
                Phase = Phase.Grow,
                PhaseStart = Time.time,
                LaunchDelay = Random.Range(0f, _launchJitter),
                LaunchAt = -1f,
                SpawnPos = pos,
                AimOffset = Random.insideUnitSphere * _spreadRadius,
                Seed = Random.Range(0f, 6.28f),
                RestRot = restRot,
                RollSeed = rollSeed,
                Roll = rollSeed,
                Rend = rend,
            };
            // 판정 계획의 발 배정 — 이 발은 그 대상에게 그 시각에 닿는다(발사=착탄−비행). 산포는 조준점 주변 연출뿐
            int index = _spikes.Count;
            if (_plan != null && index < _plan.Shots.Count)
            {
                var shot = _plan.Shots[index];
                spike.Target = shot.Target != null ? shot.Target.transform : null;
                Vector3 dest = spike.Target != null ? spike.Target.position + Vector3.up * 1.1f : _fallbackPoint;
                float flight = Vector3.Distance(pos, dest) / Mathf.Max(0.01f, _flightSpeed);
                spike.LaunchAt = shot.ImpactTime - flight;
            }
            _spikes.Add(spike);
            // 형성 글린트는 10차 검수로 제거 — 작도 완성 시점엔 에셋 문양 이외의 이펙트를 두지 않는다
        }

        // 착탄 — 큰 이펙트 없이 작은 금속 파편(칩 메시 — 없으면 송곳 메시 축소 조각이 튀며 잘게 스러진다)
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
                AddMesh(go, _ownedMaterial != null ? _ownedMaterial : _material, _shardMesh != null ? _shardMesh : _spikeMesh);
                go.AddComponent<SpikeImpactShard>().Init(dir, scale);
            }
        }

        // 발사 궤적 — 자식 GO의 TrailRenderer(12차 검수). 착탄 시 떼어내 잔필이 자연히 스러진다.
        // 재질은 전용 투명 재질(정점색 틴트 — 인스턴스 불요·수거 경로 없음) → 없으면 송곳 재질 폴백(기존 동작)
        private void AttachTrail(Transform spike)
        {
            if (_trailTime <= 0f || (_trailMaterial == null && _material == null)) return;
            var go = new GameObject("Trail");
            go.transform.SetParent(spike, false);
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = _trailTime;
            trail.startWidth = _trailWidth;
            trail.endWidth = 0f;
            trail.minVertexDistance = _trailMinVertexDistance;
            trail.alignment = LineAlignment.View; // 뷰 정렬 리본 — 전용 재질은 Cull Off라 뒤집힘 없음
            trail.sharedMaterial = _trailMaterial != null
                ? _trailMaterial
                : (_ownedMaterial != null ? _ownedMaterial : _material);
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

        // 송곳·칩 공통 렌더러 — 그림자 캐스트·수신 없음(연출 소품). 반환 렌더러는 발사 플래시 MPB 대상
        private static MeshRenderer AddMesh(GameObject go, Material material, Mesh mesh)
        {
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        // 발사 플래시 — 명도만 +_launchFlashLift(채도·색상 불변, 채널별 min 1 → 백색 포화 없음), _launchFlashTime 뒤 해제.
        // MPB라 재질 인스턴스 추가 없음(배칭 이탈은 발당 0.08s). InkMetal의 _PeakClamp가 최종 출력을 0.90 아래로 잡는다(Bloom 미기여)
        private void ApplyLaunchFlash(Spike s)
        {
            if (_launchFlashLift <= 0f || s.Rend == null) return;
            _flashBlock ??= new MaterialPropertyBlock();
            _flashBlock.SetColor(BaseColorId, Lift(_tint, _launchFlashLift));
            s.Rend.SetPropertyBlock(_flashBlock);
        }

        // 로컬 Z(첨단축) 롤 — 자세에 후곱하면 실루엣은 그대로, 면만 돈다(메시 첨단 +Z 전제)
        private static Quaternion RollQ(float deg) => Quaternion.AngleAxis(deg, Vector3.forward);

        private static Color Lift(Color c, float k) => new Color(
            Mathf.Min(1f, c.r * (1f + k)),
            Mathf.Min(1f, c.g * (1f + k)),
            Mathf.Min(1f, c.b * (1f + k)),
            c.a);

        private Vector3 CurrentTargetPos()
        {
            return _target != null ? _target.position + Vector3.up * 1.1f : _fallbackPoint;
        }

        // 발마다의 목적지 — 계획 배정 대상이 있으면 그쪽, 없으면 공통 대상/허공
        private Vector3 TargetPosOf(Spike s)
        {
            return s.Target != null ? s.Target.position + Vector3.up * 1.1f : CurrentTargetPos();
        }

        // 각자의 착탄점(산포 오프셋 포함)을 향한 조준 자세 — 뾰족한 끝(+Z)이 그리로 돌아선다
        private Quaternion AimRotOf(Spike s)
        {
            Vector3 dir = TargetPosOf(s) + s.AimOffset - s.Tr.position;
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
