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
                if (go != null) Destroy(go);
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
                var go = Instantiate(prefab, pos, Quaternion.identity);
                var auditTint = _extraTints != null && i < _extraTints.Length && _extraTints[i].a > 0f
                    ? _extraTints[i]
                    : new Color(0.682f, 0.706f, 0.729f); // 기본=금 팔레트 실측값(9차 검수)
                var sequence = go.GetComponentInChildren<SpellSequenceEffect>(true);
                if (sequence != null)
                {
                    // 자체 시계 연출 검수 — 전방 4m(그리드 앞 여백·플랫폼 위) 허공 목표로 발화(실런타임 Begin 경로)
                    sequence.Begin(pos, null, pos + Vector3.forward * 4f + Vector3.up * 0.6f, auditTint);
                    _spawned.Add(sequence.gameObject);
                }
                PatternEffectLifetime.AttachBloom(go, 3f, auditTint);
                _spawned.Add(go);
            }
        }
#endif
    }
}
