using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    // [4차 검수 2026-08-28] 커밋 문양(전통 문양 에셋) 인스턴스의 모드·틴트·수명 관리.
    // Fly 프리팹은 「투사체 비행(Projectile) → 착탄 개화(Explosion)」 구성이다(플레이모드 실측):
    //   · Bloom 모드(패링 작도) — 비행부를 끄고 개화부만 글자 자리에서 즉시 피운다(글자의 변형)
    //   · Projectile 모드(공격 작도) — 문양이 목표로 날아가 착탄 지점에서 개화한다(본래 용도)
    // 수명: 개화 시작 후 70%에 방출 중단(잔여 입자 자연 소멸), 끝에 파괴 — 지속 발광 금지 규약 합치.
    // 틴트는 인스턴스에만 — 유료 에셋 원본은 불변.
    public sealed class PatternEffectLifetime : MonoBehaviour
    {
        private enum Mode { Bloom, Projectile }

        private const float FlightSafetyMargin = 1f; // 비행 안전핀 여유(초) — 예정 시간+여유를 넘기면 정리(기술 상수)

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly List<Material> _ownedMaterials = new List<Material>();
        private Mode _mode;
        private float _lifetime;
        private float _startTime;
        private bool _stopped;
        private Transform _targetTransform; // 유도 추적 대상 — 없으면 고정 착탄점
        private Vector3 _fallbackPoint;
        private float _flightDuration;
        private float _flightStart;
        private Vector3 _flightOrigin;
        private float _arcHeight; // 연출 포물선 높이(마 — §3.1 B안). 같은 비행시간의 lerp에 수직 항만 얹는다
        private bool _arrived;
        private Transform _projectilePart;
        private Transform _explosionPart;

        // 패링 작도 — 글자 자리 제자리 개화
        public static void AttachBloom(GameObject go, float lifetime, Color tint)
        {
            var self = Create(go, lifetime, tint, Mode.Bloom);
            self.PrepareStationaryBloom();
            self.Restart();
        }

        // 공격 작도 — 문양 투사체: 배선이 확정한 비행시간으로 목표를 추적해 착탄 개화.
        // 피해는 배선이 같은 시각에 적용한다(피해=착탄 동기화 — 시계가 하나라 어긋나지 않는다)
        public static void AttachProjectile(GameObject go, float lifetime, Color tint,
            Transform target, Vector3 fallbackPoint, float flightDuration, float arcHeight = 0f)
        {
            var self = Create(go, lifetime, tint, Mode.Projectile);
            self._targetTransform = target;
            self._fallbackPoint = fallbackPoint;
            self._flightDuration = Mathf.Max(0.05f, flightDuration);
            self._flightStart = Time.time;
            self._flightOrigin = go.transform.position;
            self._arcHeight = Mathf.Max(0f, arcHeight);
            self.PrepareFlight();
            self.Restart();
        }

        private static PatternEffectLifetime Create(GameObject go, float lifetime, Color tint, Mode mode)
        {
            var self = go.AddComponent<PatternEffectLifetime>();
            self._mode = mode;
            self._lifetime = Mathf.Max(0.2f, lifetime);
            self._startTime = Time.time;
            // 결합 프리팹(FX-ASSETS §4.3)은 문양 원본을 중첩 인스턴스로 품는다 —
            // 이름 규약(Projectile/Explosion)을 깊이 무관하게 찾는다
            self._projectilePart = FindDeep(go.transform, "Projectile");
            self._explosionPart = FindDeep(go.transform, "Explosion");
            foreach (var animator in go.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled = false; // 비행·개화 타이밍은 이 컴포넌트가 소유(중첩 인스턴스 포함)
            }
            self.Tint(tint);
            return self;
        }

        // 개화부만 원점에서 — 해당 이름이 없는 프리팹(Bottom 등)은 자연 무시.
        // 문양 판의 법선은 로컬 +X(실측 — 비행축과 같다): 카메라를 향해 돌려야 글자 평면처럼 정면으로
        // 보인다. 안 돌리면 판이 모서리로 보여 「세로로 선」 문양이 된다(7차 검수 2)
        private void PrepareStationaryBloom()
        {
            if (_projectilePart != null) _projectilePart.gameObject.SetActive(false);
            if (_explosionPart != null) _explosionPart.localPosition = Vector3.zero;

            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 toCam = cam.transform.position - transform.position;
                if (toCam.sqrMagnitude > 0.001f) transform.right = toCam.normalized;
            }
        }

        // 비행부만 켜고 개화부는 착탄까지 잠재운다. 비행축 = 로컬 +X(실측 — Animator가 착탄점을 +X로 옮긴다).
        // 비행부·개화부 로컬 오프셋은 원점으로 강제 — 프리팹 기본 오프셋 탓에 시작점이
        // 플레이어 뒤로 밀리던 문제의 교정(5차 검수 1)
        private void PrepareFlight()
        {
            if (_projectilePart != null) _projectilePart.localPosition = Vector3.zero;
            if (_explosionPart != null)
            {
                _explosionPart.localPosition = Vector3.zero;
                _explosionPart.gameObject.SetActive(false);
            }
            Vector3 dir = CurrentTargetPos() - transform.position;
            if (dir.sqrMagnitude > 0.001f) transform.right = dir.normalized;
        }

        private static Gradient Whiten(Gradient source)
        {
            if (source == null) return null;
            var keys = source.colorKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].color = Color.white;
            var gradient = new Gradient();
            gradient.SetKeys(keys, source.alphaKeys);
            return gradient;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.name == name) return child;
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private Vector3 CurrentTargetPos()
        {
            return _targetTransform != null
                ? _targetTransform.position + Vector3.up * 1.1f
                : _fallbackPoint;
        }

        // 속성↔문양색 연동 — 각 시스템의 startColor에 팔레트색을 넣는다(시스템별 고정색이 박혀 있어
        // 머티리얼 _Color만으론 색이 왜곡된다 — 실측). 머티리얼 _Color는 백색으로 중립화:
        // 색의 정본은 팔레트 하나다(색=의미 단일 출처). 비활성 자식 포함 — 착탄 개화부도 미리 물들인다.
        private void Tint(Color tint)
        {
            TintHierarchy(gameObject, tint, _ownedMaterials);
        }

        // 계층 전체 팔레트 틴트 — 자체 시계 연출(SpellSequenceEffect 파생)도 같은 색 규율을 쓴다(P4 공용화).
        // ownedMaterials: 인스턴스화된 머티리얼 수거 목록 — 호출자가 수명 종료 시 Destroy 책임.
        public static void TintHierarchy(GameObject root, Color tint, List<Material> ownedMaterials)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(tint);
                // colorOverLifetime에 박힌 에셋 고유색도 백색 중립화 — 9차 검수: 고유 청색이
                // 금 팔레트를 뚫고 나왔다. 알파 키(페이드 곡선)는 보존 — 색만 팔레트 단일 출처로
                var col = ps.colorOverLifetime;
                if (col.enabled)
                {
                    var mmg = col.color;
                    switch (mmg.mode)
                    {
                        case ParticleSystemGradientMode.Color:
                            col.color = new ParticleSystem.MinMaxGradient(
                                new Color(1f, 1f, 1f, mmg.color.a));
                            break;
                        case ParticleSystemGradientMode.TwoColors:
                            col.color = new ParticleSystem.MinMaxGradient(
                                new Color(1f, 1f, 1f, mmg.colorMin.a),
                                new Color(1f, 1f, 1f, mmg.colorMax.a));
                            break;
                        case ParticleSystemGradientMode.Gradient:
                        case ParticleSystemGradientMode.RandomColor:
                            col.color = new ParticleSystem.MinMaxGradient(Whiten(mmg.gradient));
                            break;
                        case ParticleSystemGradientMode.TwoGradients:
                            col.color = new ParticleSystem.MinMaxGradient(
                                Whiten(mmg.gradientMin), Whiten(mmg.gradientMax));
                            break;
                    }
                }
            }

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var material = r.material; // 인스턴스화 — 원본 에셋 머티리얼 보호
                if (material == null) continue;
                // 파티클은 백색 중립화(색의 정본=startColor) / 메시(생성 모델 실체 — FX-ASSETS §4.2-2)는
                // startColor 경로가 없으므로 머티리얼에 직접 팔레트색을 물린다 — URP는 _BaseColor가 정본 슬롯
                Color meshColor = r is MeshRenderer ? tint : Color.white;
                if (material.HasProperty(ColorId)) material.SetColor(ColorId, meshColor);
                if (r is MeshRenderer && material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, meshColor);
                ownedMaterials?.Add(material);
            }

            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                light.color = tint; // 개화 순간의 광원 — 수 초 뒤 파괴되므로 지속 발광 아님
            }
        }

        // 스폰 시점의 시계 재시작 — playOnAwake 잔여 상태·프리팹 저장 상태와 무관하게 처음부터 재생
        private void Restart()
        {
            var root = GetComponent<ParticleSystem>();
            if (root == null) return;
            root.Simulate(0f, true, true);
            root.Play(true);
        }

        private void Update()
        {
            if (_mode == Mode.Projectile && !_arrived)
            {
                TickFlight();
                if (!_arrived && Time.time - _flightStart > _flightDuration + FlightSafetyMargin) Destroy(gameObject);
                return;
            }

            float elapsed = Time.time - _startTime;

            if (!_stopped && elapsed >= _lifetime * 0.7f)
            {
                _stopped = true;
                foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            if (elapsed >= _lifetime) Destroy(gameObject);
        }

        // 시간-lerp 비행(scaled — 감속 중엔 투사체도 함께 느려진다): 배선의 착탄 시각과 같은 시계라
        // 움직이는 대상도 정확히 그 순간에 맞는다(적 화염구와 같은 문법)
        private void TickFlight()
        {
            float t = (Time.time - _flightStart) / _flightDuration;
            Vector3 targetPos = CurrentTargetPos();
            Vector3 next = t >= 1f ? targetPos : Vector3.Lerp(_flightOrigin, targetPos, t);
            // 포물선 아크(연출만): 양 끝 0이라 출발점·착탄점·비행시간이 전부 불변 — 판정 시계 무접촉
            if (t < 1f) next += Vector3.up * (_arcHeight * 4f * t * (1f - t));

            // 허공 비행(대상 미확정)은 첫 장애물에서 개화 — 적·벽을 뚫고 지나가지 않는다(7차 검수 1).
            // 유도(대상 확정)는 보장 명중이라 차단하지 않는다 — 피해와 연출의 시계를 지킨다
            if (_targetTransform == null && Physics.Linecast(transform.position, next, out RaycastHit hit))
            {
                transform.position = hit.point;
                Arrive();
                return;
            }

            Vector3 prev = transform.position;
            transform.position = next;
            if (t >= 1f)
            {
                Arrive();
                return;
            }

            // 아크 비행은 진행 방향(속도)을 향한다 — 포물선 따라 기수가 눕고 든다. 직선은 현행 유지
            Vector3 dir = _arcHeight > 0f ? next - prev : targetPos - transform.position;
            if (dir.sqrMagnitude > 0.001f) transform.right = dir.normalized;
        }

        // 착탄 — 비행부를 끄고 개화부를 그 자리에서 깨운다(활성화가 곧 playOnAwake 점화)
        private void Arrive()
        {
            _arrived = true;
            _startTime = Time.time; // 개화 수명은 착탄부터 센다

            var root = GetComponent<ParticleSystem>();
            if (root != null) root.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            if (_projectilePart != null) _projectilePart.gameObject.SetActive(false);
            if (_explosionPart != null) _explosionPart.gameObject.SetActive(true);
        }

        private void OnDestroy()
        {
            foreach (var material in _ownedMaterials)
            {
                if (material != null) Destroy(material);
            }
        }
    }
}
