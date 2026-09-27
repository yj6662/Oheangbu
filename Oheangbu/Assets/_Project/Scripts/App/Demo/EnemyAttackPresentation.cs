using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.App.Demo
{
    // Wood ground attacks only. Reuses authored KTP/mesh prefabs; no combat or shared material mutation.
    [DisallowMultipleComponent]
    public sealed class EnemyAttackPresentation : MonoBehaviour
    {
        [SerializeField] private EnemyController _controller;
        [SerializeField] private GameObject _warningPrefab;
        [SerializeField] private GameObject _spikePrefab;
        [SerializeField] private GameObject _impactPrefab;
        [SerializeField, Min(.01f)] private float _warningScalePerRadius = .45f;
        [SerializeField, Min(.01f)] private float _spikeScale = .75f;
        [SerializeField, Min(.01f)] private float _impactScale = .6f;
        [SerializeField, Range(1, 5)] private int _spikeCount = 3;
        [SerializeField, Min(.01f)] private float _riseSeconds = .12f;
        [SerializeField, Min(.05f)] private float _afterImpactSeconds = .5f;
        [SerializeField] private LayerMask _groundMask = 1;
        private EnemyController _boundController;
        private EnemyVitals _vitals;
        private readonly Dictionary<long, Effect> _effects = new Dictionary<long, Effect>();
        private readonly HashSet<long> _seen = new HashSet<long>();
        private readonly Queue<long> _seenOrder = new Queue<long>();
        private readonly List<long> _finished = new List<long>();
        // Native Unity objects cannot safely be created during MonoBehaviour field initialization.
        private MaterialPropertyBlock _block;
        private RaycastHit[] _groundHits;
        public int ActiveAttackCount => _effects.Count;
        public int ActiveInstanceCount
        {
            get
            {
                int count = 0;
                foreach (var effect in _effects.Values)
                { if (effect.Warning != null) count++; if (effect.Impact != null) count++; foreach (var spike in effect.Spikes) if (spike != null) count++; }
                return count;
            }
        }
        public bool HasAllSources => _warningPrefab != null && _spikePrefab != null && _impactPrefab != null;

        private sealed class Effect
        {
            public long Id;
            public Vector3 Point;
            public float Start, Duration, End;
            public bool ImpactReceived;
            public GameObject Warning, Impact;
            public GameObject[] Spikes = System.Array.Empty<GameObject>();
            public Renderer[][] Renderers;
            public Vector3[] Feet;
        }

        public void Configure(EnemyController controller, GameObject warning, GameObject spike, GameObject impact)
        {
            Clear(); Unbind();
            _controller = controller; _warningPrefab = warning; _spikePrefab = spike; _impactPrefab = impact;
            if (isActiveAndEnabled) Bind();
        }
        private void OnEnable() { Bind(); }
        private void OnDisable() { Unbind(); Clear(); }
        private void OnDestroy() { Unbind(); Clear(); }
        private void Bind()
        {
            if (_controller == null) _controller = GetComponent<EnemyController>();
            if (_boundController == _controller) return;
            Unbind(); _boundController = _controller;
            if (_boundController == null) return;
            _boundController.AttackTelegraphed += OnTelegraphed;
            _boundController.AttackImpactResolved += OnImpact;
            _boundController.AttackPresentationEnded += OnAttackEnded;
            _vitals = _boundController.GetComponent<EnemyVitals>();
            if (_vitals != null) _vitals.Died += Clear;
        }
        private void Unbind()
        {
            if (_boundController != null)
            {
                _boundController.AttackTelegraphed -= OnTelegraphed;
                _boundController.AttackImpactResolved -= OnImpact;
                _boundController.AttackPresentationEnded -= OnAttackEnded;
            }
            if (_vitals != null) _vitals.Died -= Clear;
            _boundController = null; _vitals = null;
        }
        private void OnTelegraphed(EnemyAttackCue cue)
        {
            if (!isActiveAndEnabled || cue.Attack.Source != DamageSource.Enemy || cue.Attack.Element != Element.Wood ||
                cue.Delivery != EnemyAttackDelivery.GroundEruption || cue.Attack.AttackId <= 0 ||
                _boundController == null || cue.Attack.Instigator != _vitals || !_seen.Add(cue.Attack.AttackId)) return;
            _seenOrder.Enqueue(cue.Attack.AttackId);
            if (_seenOrder.Count > 128) _seen.Remove(_seenOrder.Dequeue());
            var effect = new Effect { Id = cue.Attack.AttackId, Point = cue.Point, Start = Time.time,
                Duration = Mathf.Max(.05f, cue.Duration), End = Time.time + Mathf.Max(.05f, cue.Duration) + _afterImpactSeconds };
            _effects.Add(effect.Id, effect); // Track ownership before the first native spawn can fail.
            try
            {
            effect.Warning = Spawn(_warningPrefab, cue.Point + Vector3.up * .03f, cue.Radius * _warningScalePerRadius);
            TimeParticles(effect.Warning, effect.Duration);
            int count = Mathf.Clamp(_spikeCount, 1, 5);
            effect.Spikes = new GameObject[count]; effect.Renderers = new Renderer[count][]; effect.Feet = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float angle = i * 2.399963f + (cue.Attack.AttackId % 97) * .1f;
                Vector3 offset = i == 0 ? Vector3.zero : new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * cue.Radius * .45f;
                effect.Feet[i] = GroundedFoot(cue.Point + offset, cue.Point.y);
                effect.Spikes[i] = Spawn(_spikePrefab, effect.Feet[i] - Vector3.up * 2.1f, _spikeScale);
                if (effect.Spikes[i] != null)
                {
                    effect.Spikes[i].transform.rotation *= Quaternion.Euler(0, angle * Mathf.Rad2Deg, 0);
                    effect.Renderers[i] = effect.Spikes[i].GetComponentsInChildren<Renderer>(true);
                    SetSpike(effect, i, 0, 0);
                }
            }
            }
            catch
            {
                Dispose(effect); _effects.Remove(effect.Id); _seen.Remove(effect.Id);
                throw;
            }
        }
        private void OnImpact(EnemyAttackImpact impact)
        {
            if (!_effects.TryGetValue(impact.Attack.AttackId, out var effect) || effect.ImpactReceived) return;
            effect.ImpactReceived = true;
            // Parry feedback already comes from CombatLoopWiring; don't duplicate its contact burst.
            if (impact.InShape && impact.AppliedDamage > 0 && impact.Outcome != ParryOutcome.Half)
            {
                effect.Impact = Spawn(_impactPrefab, effect.Point + Vector3.up * .08f, _impactScale);
                TimeParticles(effect.Impact, Mathf.Min(.6f, _afterImpactSeconds));
            }
        }
        private void OnAttackEnded(AttackProvenance attack, bool cancelled)
        {
            if (!_effects.TryGetValue(attack.AttackId, out var effect)) return;
            if (cancelled) { Dispose(effect); _effects.Remove(attack.AttackId); return; }
            Remove(effect.Warning); effect.Warning = null;
            effect.End = Time.time + _afterImpactSeconds;
        }
        private void Update()
        {
            _finished.Clear();
            foreach (var pair in _effects)
            {
                Effect effect = pair.Value;
                if (Time.time >= effect.End) { Dispose(effect); _finished.Add(pair.Key); continue; }
                float age = Time.time - effect.Start;
                float rise = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Mathf.Max(0, effect.Duration - _riseSeconds), effect.Duration, age));
                float fade = Mathf.Clamp01((effect.End - Time.time) / .18f);
                for (int i = 0; i < effect.Spikes.Length; i++) SetSpike(effect, i, rise, fade);
                if (age >= effect.Duration && effect.Warning != null) { Remove(effect.Warning); effect.Warning = null; }
            }
            foreach (long id in _finished) _effects.Remove(id);
        }
        private void SetSpike(Effect effect, int index, float rise, float fade)
        {
            var spike = effect.Spikes[index]; if (spike == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            spike.transform.position = effect.Feet[index] - Vector3.up * ((1 - rise) * 2.1f);
            foreach (var renderer in effect.Renderers[index])
            {
                renderer.enabled = rise > 0 && fade > 0;
                renderer.GetPropertyBlock(_block);
                _block.SetFloat("_Visibility", fade); _block.SetFloat("_DissolveHeight", 2.2f * fade);
                _block.SetFloat("_GroundY", effect.Feet[index].y); renderer.SetPropertyBlock(_block);
            }
        }
        private GameObject Spawn(GameObject source, Vector3 position, float scale)
        {
            if (source == null) return null;
            var instance = Instantiate(source, position, source.transform.rotation);
            instance.name = "EnemyWood_" + source.name;
            instance.transform.localScale = source.transform.localScale * Mathf.Max(.01f, scale);
            SceneManager.MoveGameObjectToScene(instance, gameObject.scene);
            // Source references are visual prefabs. Defensive clones must never introduce blockers.
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            return instance;
        }
        private static void TimeParticles(GameObject instance, float lifetime)
        {
            if (instance == null) return;
            var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
            float sourceLength = .05f;
            foreach (var ps in particles)
            { var main = ps.main; sourceLength = Mathf.Max(sourceLength, main.startDelay.constantMax + main.duration + main.startLifetime.constantMax); }
            // One common clock preserves the relative timing and curves of the source hierarchy.
            float speed = Mathf.Max(.01f, sourceLength / Mathf.Max(.05f, lifetime));
            foreach (var ps in particles)
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = false; main.useUnscaledTime = false; main.stopAction = ParticleSystemStopAction.None;
                main.simulationSpeed = speed;
                ps.Play(false);
            }
        }
        private Vector3 GroundedFoot(Vector3 point, float fallbackY)
        {
            point.y = fallbackY;
            float nearest = float.PositiveInfinity;
            int count = ScenePhysicsQuery.RaycastAll(gameObject.scene, point + Vector3.up * 2, Vector3.down, 4, _groundMask, ref _groundHits);
            for (int i = 0; i < count; i++)
            {
                var hit = _groundHits[i];
                if (hit.distance >= nearest || hit.normal.y < .35f || hit.collider.GetComponentInParent<EnemyVitals>() != null ||
                    hit.collider.GetComponentInParent<PlayerVitals>() != null) continue;
                nearest = hit.distance; point.y = hit.point.y;
            }
            return point;
        }
        private void Clear()
        {
            foreach (var effect in _effects.Values) Dispose(effect);
            _effects.Clear(); _finished.Clear();
        }
        private void Dispose(Effect effect)
        { Remove(effect.Warning); Remove(effect.Impact); foreach (var spike in effect.Spikes) Remove(spike); }
        private static void Remove(GameObject instance)
        {
            if (instance == null) return;
            instance.SetActive(false);
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true)) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (Application.isPlaying) Destroy(instance); else DestroyImmediate(instance);
        }
    }
}
