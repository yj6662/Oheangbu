using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Oheangbu.App
{
    // [감사 하네스 — SPEC-SPELL-FX-ASSETS P1 전용] 전통 문양 프리팹 가족 대표(변형 01)를 격자로
    // 스폰해 커버리지 감사 스크린샷을 만든다. 에디터 전용(빌드 무동작)·게임 로직 무접촉.
    // 감사 종료 후 씬과 함께 제거 대상 — 런타임 경로에 두지 않는다.
    public sealed class PatternAuditSpawner : MonoBehaviour
    {
        private enum Family { Bottom, Fly }

        [SerializeField] private Family _family = Family.Bottom;
        [SerializeField] private int _columns = 5;
        [SerializeField] private float _spacing = 5f;
        [SerializeField] private float _replayInterval = 4f; // 이펙트를 주기 재생 — 타이밍별 컷 확보
        [SerializeField] private bool _faceCamera; // Fly 개화부 감사용(문양 판 법선=로컬 +X)
        [SerializeField] private GameObject[] _extraPrefabs = new GameObject[0]; // 결합 프리팹 검수 — 실런타임 경로(AttachBloom)로 개화
        [SerializeField] private Color[] _extraTints = new Color[0]; // _extraPrefabs 병렬 — 어휘별 팔레트 색 감사(알파 0=기본 금)

#if UNITY_EDITOR
        // [SPEC-SPELL-FX-REWORK §3.4·§6-7] 더미 대상 — 락온 경로(target≠null, 캡슐 중심=지면+1.1)를 씬 편집 없이 발화한다.
        // 현행 허공(target=null) 경로만으로는 대상 y를 지면으로 쓰던 부유 버그가 은폐됐다(고 감사 원인 ⑤). false=허공 경로 유지
        [Header("더미 대상 [감사 전용] — C1_CombatLoop Enemy 규격(CapsuleCollider height 2·center 0, Vitals 없음 — 판정 무접촉)")]
        [SerializeField] private bool _spawnDummyTargets = true;
        [SerializeField] private float _dummyForward = 4f;          // 프리팹 앞 거리 — 현행 허공 목표와 동일
        [SerializeField] private float _dummyFloorDrop = 1.2f;      // 격자 pos.y(1.2)→지면 근사 낙차 — 탐침 시작·폴백(히트 y가 정본, ThornRise _groundDrop 동형)
        [SerializeField] private float _dummyCapsuleCenter = 1.1f;  // 지면→캡슐 중심 높이

        private const float FloorProbeLift = 0.05f; // 지면 콜라이더 유무 탐침 시작 높이(m)
        private const float FloorProbeDepth = 1f;   // 탐침 깊이(m)

        // [SPEC-SPELL-FX-REWORK §3.1·§3.5] 판정 계획 주입 — 인게임과 같은 입력(AreaImpactPlan)으로 노 cone·모/오 경로 연출을 발화한다.
        // 현행 허공 목표 직접 전달로는 노 방향 회귀(계획 Point=시전자 위치)와 모 진행축(카메라 회전) 문제가 재현되지 않았다.
        [Header("판정 계획 주입 [감사 전용] — 수치는 SpellBook/CombatConfig [TEST] 값의 사본(감사 씬은 배선이 없다)")]
        [SerializeField] private bool _extraUsePlans = true;
        [SerializeField] private bool _extraUseCameraRotation = true;   // 인게임 DrawingRig(=카메라) 회전 재현
        [SerializeField] private bool _flameJetNoPlan;                   // true=노에 계획 없이 fallback=pos 주입(계획 없는 경로의 역전 회귀 재현)
        [SerializeField] private float _conePlanLength = 10f;
        [SerializeField] private float _conePlanAngle = 40f;
        [SerializeField] private float _conePlanDelay = 0.4f;
        [SerializeField] private Vector4 _sandPlan = new Vector4(1.2f, 12f, 7f, 0.4f);   // 반폭·길이·속도·차오름(모)
        [SerializeField] private Vector4 _waterPlan = new Vector4(1.5f, 14f, 6f, 0.45f); // 반폭·길이·속도·차오름(오)
#endif

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private float _nextReplay;

#if UNITY_EDITOR
        private void Start()
        {
            SpawnAll();
            _nextReplay = Time.time + _replayInterval;
        }

        private void Update()
        {
            if (Time.time < _nextReplay) return;
            _nextReplay = Time.time + _replayInterval;
            foreach (var go in _spawned)
            {
                if (go == null) continue;
                go.SetActive(false); // 콜라이더를 즉시 물리 씬에서 제거 — 같은 프레임 SpawnDummyTarget 탐침이 이전 캡슐·바닥을 보지 않는다(Destroy는 프레임 말 지연)
                Destroy(go);
            }
            _spawned.Clear();
            SpawnAll();
        }

        private void SpawnAll()
        {
            int count = _family == Family.Bottom ? 20 : 10;
            for (int i = 1; i <= count; i++)
            {
                string prefabName = _family == Family.Bottom ? $"Bottom{i:00}-01" : $"Fly{i:00}-01";
                string path = $"Assets/KoreanTraditionalPattern_Effect/Prefabs/{_family}/{prefabName}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                int idx = i - 1;
                Vector3 pos = transform.position
                    + new Vector3(idx % _columns * _spacing, 0f, -(idx / _columns) * _spacing);
                var go = Instantiate(prefab, pos, Quaternion.identity);

                var animator = go.GetComponent<Animator>();
                if (animator != null) animator.enabled = false; // Fly 비행 정지 — 제자리 감사

                if (_family == Family.Fly)
                {
                    var projectile = go.transform.Find("Projectile");
                    if (projectile != null) projectile.gameObject.SetActive(false);
                    var explosion = go.transform.Find("Explosion");
                    if (explosion != null)
                    {
                        explosion.localPosition = Vector3.zero;
                        explosion.gameObject.SetActive(true);
                    }
                }

                if (_faceCamera && Camera.main != null)
                {
                    Vector3 toCam = Camera.main.transform.position - go.transform.position;
                    toCam.y = 0f;
                    if (toCam.sqrMagnitude > 0.001f) go.transform.right = toCam.normalized;
                }

                var root = go.GetComponent<ParticleSystem>();
                if (root != null)
                {
                    root.Simulate(0f, true, true);
                    root.Play(true);
                }
                _spawned.Add(go);

                var labelGo = new GameObject($"Label_{prefabName}");
                labelGo.transform.position = pos + Vector3.up * 3.2f;
                if (Camera.main != null)
                {
                    labelGo.transform.rotation = Quaternion.LookRotation(
                        labelGo.transform.position - Camera.main.transform.position);
                }
                var text = labelGo.AddComponent<TextMesh>();
                text.text = prefabName;
                text.fontSize = 48;
                text.characterSize = 0.12f;
                text.anchor = TextAnchor.MiddleCenter;
                text.color = Color.white;
                _spawned.Add(labelGo);
            }

            // 결합 프리팹 검수 — 격자 아랫줄, 실런타임 경로(AttachBloom: 심층 탐색·틴트·수명)로 개화.
            // 틴트=금 은백 근사(감사용 고정값 — 인게임 색은 팔레트가 정본)
            int rows = (count + _columns - 1) / _columns;
            for (int i = 0; i < _extraPrefabs.Length; i++)
            {
                var prefab = _extraPrefabs[i];
                if (prefab == null) continue;
                Vector3 pos = transform.position
                    + new Vector3(i * _spacing, 1.2f, -(rows + 1) * _spacing);
                Quaternion spawnRot = _extraUseCameraRotation && Camera.main != null
                    ? Camera.main.transform.rotation
                    : Quaternion.identity;
                var go = Instantiate(prefab, pos, spawnRot);
                var auditTint = _extraTints != null && i < _extraTints.Length && _extraTints[i].a > 0f
                    ? _extraTints[i]
                    : new Color(0.682f, 0.706f, 0.729f); // 기본=금 팔레트 실측값(9차 검수)
                var sequence = go.GetComponentInChildren<SpellSequenceEffect>(true);
                if (sequence != null)
                {
                    // 판정 계획 주입(노 cone·모/오 경로) — 어댑터와 같은 순서: SetAreaPlan → Begin(fallback=계획 점의 어댑터 의미)
                    var plan = _extraUsePlans ? BuildPlan(sequence, pos) : null;
                    if (plan != null)
                    {
                        sequence.SetAreaPlan(plan);
                        Vector3 fallback = plan.Shape == Spellcraft.AreaShape.Cone
                            ? plan.Point + plan.Direction * plan.Length
                            : plan.Point;
                        sequence.Begin(pos, null, fallback, auditTint);
                    }
                    else if (sequence is FlameJetEffect && _flameJetNoPlan)
                    {
                        sequence.Begin(pos, null, pos - Vector3.up * _dummyFloorDrop, auditTint); // 계획 없는 경로의 역전 회귀 재현(fallback=시전자 위치)
                    }
                    else
                    {
                        // 자체 시계 연출 검수 — 더미 대상(락온 경로) 또는 전방 4m 허공 목표로 발화(실런타임 Begin 경로)
                        Transform dummy = _spawnDummyTargets ? SpawnDummyTarget(pos) : null;
                        if (dummy != null) sequence.Begin(pos, dummy, dummy.position, auditTint);
                        else sequence.Begin(pos, null, pos + Vector3.forward * 4f + Vector3.up * 0.6f, auditTint);
                    }
                    _spawned.Add(sequence.gameObject);
                }
                PatternEffectLifetime.AttachBloom(go, 3f, auditTint);
                _spawned.Add(go);
            }
        }

        // 연출 종류별 계획 — 노=Cone(Point=시전자 발치·전방 +Z), 모/오=Path(시작=발치). 소·고는 대상 경로(더미)로 발화
        private AreaImpactPlan BuildPlan(SpellSequenceEffect sequence, Vector3 pos)
        {
            Vector3 feet = pos - Vector3.up * _dummyFloorDrop;
            if (sequence is FlameJetEffect && !_flameJetNoPlan)
            {
                return new AreaImpactPlan
                {
                    Shape = Spellcraft.AreaShape.Cone, Point = feet, Direction = Vector3.forward,
                    Length = _conePlanLength, Delay = _conePlanDelay, Angle = _conePlanAngle,
                };
            }
            Vector4 p = sequence is SandStormEffect ? _sandPlan
                : sequence is WaterWaveEffect || sequence is GroundWaveEffect ? _waterPlan
                : Vector4.zero;
            if (p.y <= 0f) return null;
            return new AreaImpactPlan
            {
                Shape = Spellcraft.AreaShape.Path, Point = feet, Direction = Vector3.forward,
                Radius = p.x, Length = p.y, Speed = p.z, Delay = p.w,
            };
        }

        // 더미 대상 캡슐 — 프리팹 앞, 캡슐 중심이 지면 위 _dummyCapsuleCenter. 지면은 캡슐 배치 전에 탐침해 히트 y를 정본으로
        // 쓴다(격자 낙차는 폴백) — ThornRise 로그 ground.y와 target.y가 자기 일관(C1_FxAudit Ground y=-0.05 → target.y=1.05).
        // 발밑에 지면 콜라이더가 없으면(다른 감사 씬) Plane 1장을 깔아 ThornRise가 폴백이 아니라 레이캐스트 경로로 착지하게 한다. 4s 재생마다 함께 재생성
        private Transform SpawnDummyTarget(Vector3 pos)
        {
            float floorY = pos.y - _dummyFloorDrop;
            Vector3 center = pos + Vector3.forward * _dummyForward;
            Vector3 probe = new Vector3(center.x, floorY + FloorProbeLift, center.z);
            bool hasFloor = Physics.Raycast(probe, Vector3.down, out RaycastHit hit, FloorProbeDepth,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (hasFloor) floorY = hit.point.y;
            center.y = floorY + _dummyCapsuleCenter;

            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "AuditDummyTarget";
            capsule.transform.position = center;
            _spawned.Add(capsule);

            if (!hasFloor)
            {
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "AuditDummyFloor";
                floor.transform.position = new Vector3(center.x, floorY, center.z);
                _spawned.Add(floor);
            }
            Physics.SyncTransforms(); // 같은 프레임의 Begin 레이캐스트가 새 콜라이더 위치를 본다
            return capsule.transform;
        }
#endif
    }
}
