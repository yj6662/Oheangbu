using UnityEngine;

namespace Oheangbu.App
{
    // [임시 이펙트 — 3차 플레이 검수] 패링 성공 접점 버스트: 투사체가 방어막에 부딪혀 꺼지는 지점에서
    // 방어막 속성색 조각들이 터지듯 퍼졌다 스러진다. 정식 이펙트는 ART 트랙에서 교체 예정.
    // Sprites/Default(투명 지원·내장) 쿼드 — 에셋 의존 없음.
    public sealed class ParryBurstEffect : MonoBehaviour
    {
        private const int ShardCount = 6;
        private const float Lifetime = 0.35f;
        private const float Radius = 0.55f;    // 퍼지는 반경
        private const float ShardSize = 0.16f;

        private readonly Transform[] _shards = new Transform[ShardCount];
        private readonly Vector3[] _dirs = new Vector3[ShardCount];
        private Material _material;
        private float _startTime;

        public static void Spawn(Vector3 position, Color color, Camera cam)
        {
            var go = new GameObject("ParryBurst");
            go.transform.position = position;
            // 카메라를 바라보는 평면에서 퍼진다 — 어느 각도에서든 「막에 부딪혔다」로 읽히게
            if (cam != null) go.transform.rotation = Quaternion.LookRotation(go.transform.position - cam.transform.position);
            go.AddComponent<ParryBurstEffect>().Build(color);
        }

        private void Build(Color color)
        {
            _startTime = Time.time;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                // 빌드에 셰이더가 없으면 이펙트만 조용히 생략 — 판정·보상은 이미 끝났다
                Destroy(gameObject);
                return;
            }
            _material = new Material(shader);
            _material.color = color;

            for (int i = 0; i < ShardCount; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Shard";
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(transform, false);

                float angle = (360f / ShardCount) * i + Random.Range(-18f, 18f);
                _dirs[i] = Quaternion.Euler(0f, 0f, angle) * Vector3.up;
                quad.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                quad.transform.localScale = Vector3.zero;

                var quadRenderer = quad.GetComponent<MeshRenderer>();
                quadRenderer.sharedMaterial = _material;
                quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                quadRenderer.receiveShadows = false;
                _shards[i] = quad.transform;
            }
        }

        private void OnDestroy()
        {
            // 수명 만료·씬 언로드 어느 경로든 런타임 머티리얼 인스턴스를 동반 파괴
            if (_material != null) Destroy(_material);
        }

        private void Update()
        {
            if (_material == null) return;

            float t = (Time.time - _startTime) / Lifetime;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            // 바깥으로 빠르게 퍼지고(감속 곡선), 커졌다가 스러진다
            float spread = 1f - (1f - t) * (1f - t);
            float size = ShardSize * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI); // 0→피크→0
            for (int i = 0; i < ShardCount; i++)
            {
                if (_shards[i] == null) continue;
                _shards[i].localPosition = _dirs[i] * (Radius * spread);
                _shards[i].localScale = new Vector3(size, size, 1f);
            }

            var color = _material.color;
            color.a = 1f - t * t;
            _material.color = color;
        }
    }
}
