using UnityEngine;

namespace Oheangbu.App
{
    // [SPELL-FIDELITY §4.2 — 노 「지정 영역에 화염 방사」] 전용 구현(#138 방침 1호 — 재조합 아님).
    // 문법: 커밋 문양이 시전자 앞에 서고(부모 Bloom — 의례 언어), 그 중심에서 전방 cone으로
    // 화염이 「방사」된다. 판정은 배선의 cone 다중 히트(#137)가 같은 형성 딜레이로 동기.
    // 화염 파티클은 코드 구성 — 프리팹 짜깁기 없이 문법이 코드에 명시된다(버전 관리·재현성).
    // 발광 상한: Light 1개가 분사 동안만 살고 감쇠 소멸(순간 발광 — ART-INK 합치).
    public sealed class FlameJetEffect : SpellSequenceEffect
    {
        [Header("시계 [TEST §7] — 판정 딜레이(AreaImpactDelay 0.4s)와 FormTime 수동 동기(§8 예외 1)")]
        [SerializeField, Min(0f)] private float _formTime = 0.3f;     // 문양 형성 → 분사 대기
        [SerializeField, Min(0.1f)] private float _jetDuration = 1.0f; // 분사 지속
        [SerializeField, Min(0f)] private float _settleTime = 1.0f;    // 방출 중단 후 잔여 입자 소멸 대기

        [Header("분사 형상 [TEST]")]
        [SerializeField, Range(5f, 45f)] private float _coneAngle = 25f; // 반각
        [SerializeField, Min(1f)] private float _reach = 8f;             // 도달 거리(m)
        [SerializeField, Min(10f)] private float _emissionRate = 140f;
        [SerializeField] private Vector2 _particleSize = new Vector2(0.45f, 0.9f);

        [Header("자원 참조 — 머티리얼만 재사용(#138 허용면). 비우면 기본 파티클 머티리얼")]
        [SerializeField] private Material _flameMaterial;

        [Header("순간 광원 [TEST] — 분사 동안만, 감쇠 소멸")]
        [SerializeField, Min(0f)] private float _lightIntensity = 2.5f;
        [SerializeField, Min(0.5f)] private float _lightRange = 6f;

        private ParticleSystem _jet;
        private Light _light;
        private float _startTime = -1f;
        private bool _firing;
        private Color _tint;

        public override void Begin(Vector3 origin, Transform target, Vector3 fallbackPoint, Color tint)
        {
            // 부모(문양 프리팹)의 수명(AttachBloom 2.2s)과 분리 — 방사는 자체 시계로 완주한다
            transform.SetParent(null, true);
            transform.position = origin;

            Vector3 goal = target != null ? target.position : fallbackPoint;
            Vector3 dir = goal - origin;
            dir.y = 0f; // 방사는 수평 — cone 판정(#137)과 같은 평면
            if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

            _tint = tint;
            BuildJet();
            _startTime = Time.time;
            _firing = false;
        }

        private void Update()
        {
            if (_startTime < 0f) return;
            float elapsed = Time.time - _startTime;

            if (!_firing && elapsed >= _formTime)
            {
                _firing = true;
                _jet.Play();
                if (_light != null) _light.enabled = true;
            }

            if (_firing && _light != null && _light.enabled)
            {
                // 분사 진행에 따라 감쇠 — 종료 시 0(지속 발광 금지)
                float t = Mathf.Clamp01((elapsed - _formTime) / _jetDuration);
                _light.intensity = _lightIntensity * (1f - t * t);
                if (t >= 1f) _light.enabled = false;
            }

            if (_firing && elapsed >= _formTime + _jetDuration)
            {
                _jet.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }

            if (elapsed >= _formTime + _jetDuration + _settleTime) Destroy(gameObject);
        }

        // cone 화염의 전 구성이 여기 있다 — 방사 문법의 단일 출처.
        // 색: 백(점화) → 속성 틴트(불길) → 먹빛 침전(연기) — 팔레트가 몸통색, 알파는 소멸 페이드
        private void BuildJet()
        {
            var go = new GameObject("Jet");
            go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.identity;

            _jet = go.AddComponent<ParticleSystem>();
            var main = _jet.main;
            main.playOnAwake = false;
            main.loop = true; // 방출 시간은 Stop이 통제
            main.startLifetime = Mathf.Max(0.2f, _reach / 12f); // 도달 거리 = 속도 × 수명
            main.startSpeed = 12f;
            main.startSize = new ParticleSystem.MinMaxCurve(_particleSize.x, _particleSize.y);
            main.startColor = _tint;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // 뿜어진 불길은 세상에 남는다
            main.maxParticles = 600;

            var emission = _jet.emission;
            emission.rateOverTime = _emissionRate;

            var shape = _jet.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = _coneAngle;
            shape.radius = 0.15f;
            shape.rotation = Vector3.zero; // 로컬 +Z 분사 — 루트 LookRotation이 방향 소유

            var col = _jet.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),               // 점화 — 순간 백열
                    new GradientColorKey(Color.white, 0.12f),
                    new GradientColorKey(new Color(1f, 1f, 1f), 0.35f),  // 몸통은 startColor(틴트)와 곱
                    new GradientColorKey(new Color(0.16f, 0.15f, 0.13f), 1f), // 먹빛 침전 — 연기
                },
                new[]
                {
                    new GradientAlphaKey(0.9f, 0f),
                    new GradientAlphaKey(0.75f, 0.5f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(gradient);

            var sol = _jet.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.35f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.12f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (_flameMaterial != null) renderer.sharedMaterial = _flameMaterial;

            var lightGo = new GameObject("JetLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = Vector3.forward * 1.5f;
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = _tint;
            _light.intensity = _lightIntensity;
            _light.range = _lightRange;
            _light.shadows = LightShadows.None;
            _light.enabled = false; // 분사 개시에 점등
        }
    }
}
