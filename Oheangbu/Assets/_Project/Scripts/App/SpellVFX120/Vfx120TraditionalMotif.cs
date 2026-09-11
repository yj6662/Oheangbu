using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.SpellVFX120
{
    /// <summary>
    /// Owns one copy of an authored particle motif (a prefab or selected prefab subtree).
    /// Does not emit gameplay events,
    /// change the source prefab/material/mesh, or control the host's transform/lifetime.
    /// The owner must destroy the host. This component destroys its runtime clone on completion.
    /// </summary>
    public sealed class Vfx120TraditionalMotif : MonoBehaviour
    {
        public enum Role { Cast, Impact, Summon, Shield }

        [Serializable]
        public struct Settings
        {
            public float Lifetime, FadeSeconds;
            // Field patterns unfold once, hold their authored pose, then fade on release.
            public float HoldAt;
            public int MaxSystems, MaxParticles;
            public uint Seed;
            public bool PreviewControlled, Sustain;
            public bool HierarchyScaling;
            public bool PreserveAuthored;
            public float Brightness;
            public bool PatternFocus;
            public bool FiniteWindow;
            // Only needed to disambiguate a multi-clip controller for deterministic preview.
            // Runtime keeps the original controller. This is not an animation replacement.
            public AnimationClip PreviewClip;

            public static Settings DefaultFor(Role role) => new Settings
            {
                Lifetime = role == Role.Cast ? 1.2f : role == Role.Impact ? 1.6f : 4f,
                FadeSeconds = .4f, MaxSystems = 32, MaxParticles = 400, Seed = 1,
                HoldAt = role == Role.Shield || role == Role.Summon ? .75f : -1,
                // Sustain is explicit: a finite summon still ends at Lifetime by default.
                PreviewControlled = false, Sustain = false
            };
        }

        private sealed class Layer
        {
            public ParticleSystem System;
            public bool EmissionEnabled, Active;
            public bool SubEmitter, WasActive;
            public int Demand, Capacity;
        }

        private sealed class AnimationLayer
        {
            public Animator Animator;
            public AnimationClip Clip;
        }

        private struct RestTransform
        {
            public Transform Transform;
            public Vector3 Position, Scale;
            public Quaternion Rotation;
            public bool Active;
        }

        private sealed class TintSlot
        {
            public Renderer Renderer;
            public int Slot;
            public MaterialPropertyBlock Block;
            public int[] Properties;
            public Color[] Original;
        }

        private GameObject _instance;
        private Layer[] _layers;
        private ParticleSystem[] _simulationRoots;
        private TintSlot[] _tints;
        private AnimationLayer[] _animations;
        private RestTransform[] _restTransforms;
        private Settings _settings;
        private Color _pigment, _ink;
        private float _releaseAt, _endAt, _startedAt;
        private bool _emissionStopped, _configured;
        private bool _poseHeld;

        public Role MotifRole { get; private set; }
        public string Status { get; private set; } = "NOT_CONFIGURED";
        public string Diagnostic { get; private set; } = "";
        public float Age { get; private set; }
        public bool IsComplete { get; private set; }
        public bool DestroyHostOnCompletion {get;set;}
        public bool PreserveAuthored => _settings.PreserveAuthored;
        public bool PreviewControlled => _settings.PreviewControlled;
        public int SourceSystemCount { get; private set; }
        public int ActiveSystemCount { get; private set; }
        public int ParticleBudget { get; private set; }
        public int SourceParticleCapacity { get; private set; }
        public int UntintedMaterialSlots { get; private set; }
        public int MaterialCloneCount => 0;
        public int InstanceCount => _instance == null ? 0 : 1;
        // The caller can place/recenter an extracted Explosion/Charge carrier without
        // changing any of its children or the asset's authored transform hierarchy.
        public Transform ContentTransform => _instance == null ? null : _instance.transform;
        public int AnimatorCount => _animations == null ? 0 : _animations.Length;
        public float EmissionStopsAt => _releaseAt;
        public float EndsAt => _endAt;
        public int LiveParticleCount
        {
            get
            {
                int count = 0;
                if (_layers != null) foreach (var row in _layers)
                    if (row.System != null) count += row.System.particleCount;
                return count;
            }
        }

        public bool Configure(GameObject source, Color pigment, Color ink, Role role, Settings settings)
        {
            Clear();
            SourceSystemCount = ActiveSystemCount = ParticleBudget = SourceParticleCapacity = UntintedMaterialSlots = 0;
            if (source == null) return Reject("SOURCE_MISSING");
            if (!Finite(settings.Lifetime) || settings.Lifetime <= 0 || !Finite(settings.FadeSeconds)
                || settings.FadeSeconds <= 0 || !Finite(settings.HoldAt) || settings.MaxSystems < 1 || settings.MaxParticles < 1
                || !Finite(pigment) || !Finite(ink)) return Reject("INVALID_SETTINGS");
            // KTP package inventory: 250 prefabs, no MonoBehaviour/package scripts.
            // Reject before Instantiate so Awake/OnEnable cannot escape the staging hierarchy.
            if (source.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                return Reject("SOURCE_MONOBEHAVIOUR_OR_MISSING_SCRIPT_UNSUPPORTED");
            if (source.GetComponentsInChildren<Animation>(true).Length != 0)
                return Reject("LEGACY_ANIMATION_UNSUPPORTED");
            var sourceAnimators = source.GetComponentsInChildren<Animator>(true);
            foreach (var animator in sourceAnimators)
            {
                var controller = animator.runtimeAnimatorController;
                if (controller == null) return Reject("ANIMATOR_CONTROLLER_MISSING");
                var clips = controller.animationClips;
                foreach (var clip in clips)
                    if (clip != null && clip.events.Length != 0) return Reject("ANIMATION_EVENTS_UNSUPPORTED");
                if (settings.PreviewControlled && PreviewClip(clips, settings.PreviewClip) == null)
                    return Reject("PREVIEW_REQUIRES_EXPLICIT_SINGLE_AUTHORED_CLIP");
            }

            var sourceSystems = source.GetComponentsInChildren<ParticleSystem>(true);
            SourceSystemCount = sourceSystems.Length;
            if (SourceSystemCount == 0) return Reject("SOURCE_HAS_NO_PARTICLES");
            // Keep all authored systems; a too-large candidate is rejected rather than truncated.
            if (!settings.PreserveAuthored && (SourceSystemCount > settings.MaxSystems || SourceSystemCount > settings.MaxParticles))
                return Reject("SOURCE_SYSTEM_COUNT_EXCEEDS_BUDGET");
            foreach (var ps in sourceSystems)
            {
                var sub = ps.subEmitters;
                for (int i = 0; i < sub.subEmittersCount; i++)
                {
                    var target = sub.GetSubEmitterSystem(i);
                    if (target != null && !target.transform.IsChildOf(source.transform))
                        return Reject("EXTERNAL_SUB_EMITTER_UNSUPPORTED");
                }
                var main = ps.main;
                if (main.simulationSpace == ParticleSystemSimulationSpace.Custom
                    && main.customSimulationSpace != null && !main.customSimulationSpace.IsChildOf(source.transform))
                    return Reject("EXTERNAL_SIMULATION_SPACE_UNSUPPORTED");
            }

            _settings = settings;
            _poseHeld = false;
            _settings.FadeSeconds = Mathf.Min(settings.FadeSeconds, settings.Lifetime);
            _pigment = pigment; _ink = ink; MotifRole = role;
            _releaseAt = settings.Sustain ? float.PositiveInfinity : settings.Lifetime - _settings.FadeSeconds;
            _endAt = settings.Sustain ? float.PositiveInfinity : settings.Lifetime;
            if(settings.PreserveAuthored)
            {
                _settings.HoldAt=-1;
                float cycle=0,tail=0;
                foreach(var ps in sourceSystems){var m=ps.main;float speed=Mathf.Max(.001f,m.simulationSpeed);cycle=Mathf.Max(cycle,(m.duration+CurveMaximum(m.startDelay))/speed);tail=Mathf.Max(tail,CurveMaximum(m.startLifetime)/speed);}
                foreach(var animator in sourceAnimators)foreach(var clip in animator.runtimeAnimatorController.animationClips)if(clip!=null)cycle=Mathf.Max(cycle,clip.length);
                _settings.FadeSeconds=Mathf.Max(.1f,tail);
                _releaseAt=settings.Sustain?float.PositiveInfinity:Mathf.Max(.1f,cycle);
                _endAt=_releaseAt+_settings.FadeSeconds;
            }
            if(settings.FiniteWindow)
            {
                _settings.FadeSeconds=Mathf.Min(.12f,settings.Lifetime);
                _releaseAt=settings.Lifetime-_settings.FadeSeconds;
                _endAt=settings.Lifetime;
            }
            Age = 0; IsComplete = false; _startedAt = Time.time;
            var staging = new GameObject("MotifInactiveStaging");
            staging.SetActive(false);
            staging.transform.SetParent(transform, false);
            try
            {
                _instance = Instantiate(source, staging.transform, false);
                _instance.name = "TraditionalMotif_" + role + "_" + source.name;
                _instance.SetActive(false);
                _instance.transform.SetParent(transform, false);
                // Preserve the source root's authored scale/rotation/local offset.
                var systems = _instance.GetComponentsInChildren<ParticleSystem>(true);
                // Keep the source's alpha/time curves, but remove its baked neon hue
                // before applying the spell's ink pigment. No source asset is modified.
                foreach (var ps in systems)
                {
                    var mainColor = ps.main; mainColor.startColor = Neutral(mainColor.startColor);
                    var overLife = ps.colorOverLifetime;
                    if (overLife.enabled) overLife.color = Neutral(overLife.color);
                    var bySpeed = ps.colorBySpeed;
                    if (bySpeed.enabled) bySpeed.color = Neutral(bySpeed.color);
                }
                _layers = new Layer[systems.Length];
                int totalDemand = 0;
                for (int i = 0; i < systems.Length; i++)
                {
                    var ps = systems[i];
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = ps.main;
                    if (settings.HierarchyScaling) main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    bool active = ActiveBelowRoot(ps.transform, _instance.transform);
                    var row = new Layer { System = ps, Active = active,
                        EmissionEnabled = ps.emission.enabled,
                        // Animator may activate an initially hidden Explosion later.
                        Demand = EstimateDemand(ps, active || sourceAnimators.Length != 0) };
                    _layers[i] = row;
                    totalDemand += row.Demand;
                    SourceParticleCapacity += main.maxParticles;
                    if (active) ActiveSystemCount++;
                    // In runtime an authored Animator can turn children on later, so
                    // preserve their playOnAwake. Preview drives each activation explicitly.
                    if (settings.PreviewControlled || sourceAnimators.Length == 0) main.playOnAwake = false;
                    main.stopAction = ParticleSystemStopAction.None;
                    main.useUnscaledTime = false;
                    main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                    // No loop, duration, delay, velocity, shape, rotation, size, lifetime,
                    // gradient, renderer mode, mesh or material replacement.
                    ps.useAutoRandomSeed = false;
                    ps.randomSeed = Seed(settings.Seed, i);
                    var collision = ps.collision; collision.sendCollisionMessages = false;
                }
                if(settings.PreserveAuthored){foreach(var row in _layers){row.Capacity=row.System.main.maxParticles;ParticleBudget+=row.Capacity;}}
                else AllocateBudget(settings.MaxParticles, totalDemand);
                BuildSimulationRoots(systems);
                BuildAnimationLayers();
                BuildTintSlots();
                // Motifs are presentation-only; native physics/audio/light side effects are excluded.
                foreach (var collider in _instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (var rigidbody in _instance.GetComponentsInChildren<Rigidbody>(true))
                { rigidbody.isKinematic = true; rigidbody.detectCollisions = false; }
                foreach (var audio in _instance.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
                if(!settings.PreserveAuthored)foreach (var light in _instance.GetComponentsInChildren<Light>(true)) light.enabled = false;
                _configured = true;
                _instance.SetActive(true);
                ApplyTint(0);
                Status = "CONFIGURED_NOT_VISUALLY_VERIFIED";
                Diagnostic = UntintedMaterialSlots == 0 ? "Original shader blending preserved; additive ink-darkening needs visual review."
                    : "Some shaders have no color property; their fade is unverified. All clone renderers are removed at the end.";
                if (settings.PreviewControlled) Sample(0);
                else foreach (var row in _layers)
                    if (!row.SubEmitter && row.System.gameObject.activeInHierarchy) row.System.Play(false);
                return true;
            }
            catch (Exception error)
            {
                Clear();
                return Reject("CONFIGURATION_FAILED: " + error.GetType().Name + ": " + error.Message);
            }
            finally { Dispose(staging); }
        }

        private void AllocateBudget(int budget, int totalDemand)
        {
            // Reserve one slot for every system, then proportionally allocate useful demand.
            // Unlike maxParticles/1000 scaling, a one-particle emblem keeps its one burst.
            int extra = Mathf.Max(0, budget - _layers.Length);
            int useful = Mathf.Max(0, totalDemand - _layers.Length);
            foreach (var row in _layers)
            {
                row.Capacity = 1 + (useful == 0 ? 0 : (int)((long)extra * (row.Demand - 1) / useful));
                row.Capacity = Mathf.Min(row.Demand, row.Capacity);
                ParticleBudget += row.Capacity;
            }
            // Largest deficits first; initialization only. Never exceeds source demand or cap.
            while (ParticleBudget < budget)
            {
                Layer best = null; float deficit = 0;
                foreach (var row in _layers)
                {
                    float remaining = (row.Demand - row.Capacity) / (float)row.Demand;
                    if (remaining > deficit) { best = row; deficit = remaining; }
                }
                if (best == null) break;
                best.Capacity++; ParticleBudget++;
            }
            foreach (var row in _layers)
            {
                var main = row.System.main; main.maxParticles = row.Capacity;
                float ratio = Mathf.Min(1, row.Capacity / (float)row.Demand);
                var emission = row.System.emission;
                emission.rateOverTimeMultiplier *= ratio;
                emission.rateOverDistanceMultiplier *= ratio;
                for (int i = 0; i < emission.burstCount; i++)
                {
                    var burst = emission.GetBurst(i);
                    // Preserve time/repeat interval/probability and the complete count curve mode.
                    burst.count = ScaleCurve(burst.count, ratio);
                    emission.SetBurst(i, burst);
                }
            }
        }

        private static int EstimateDemand(ParticleSystem ps, bool active)
        {
            if (!active) return 1;
            var main = ps.main; var emission = ps.emission;
            float lifetime = Mathf.Max(.01f, CurveMaximum(main.startLifetime));
            float demand = CurveMaximum(emission.rateOverTime) * lifetime;
            // Distance-driven rate depends on the owner's movement. Keep its native cap
            // conservative rather than inventing a world speed or removing its curve.
            if (CurveMaximum(emission.rateOverDistance) > 0) demand = main.maxParticles;
            for (int i = 0; i < emission.burstCount; i++)
            {
                var burst = emission.GetBurst(i);
                int overlap = burst.repeatInterval > .001f ? Mathf.CeilToInt(lifetime / burst.repeatInterval) + 1 : 1;
                if (burst.cycleCount > 0) overlap = Mathf.Min(overlap, burst.cycleCount);
                demand += CurveMaximum(burst.count) * Mathf.Max(1, overlap);
            }
            return Mathf.Clamp(Mathf.CeilToInt(demand), 1, Mathf.Max(1, main.maxParticles));
        }

        private static float CurveMaximum(ParticleSystem.MinMaxCurve curve)
        {
            float max = 0;
            for (int i = 0; i <= 32; i++)
            {
                float t = i / 32f;
                max = Mathf.Max(max, curve.Evaluate(t, 0), curve.Evaluate(t, 1));
            }
            return Finite(max) ? Mathf.Clamp(max, 0, 1000000) : 0;
        }

        private static ParticleSystem.MinMaxCurve ScaleCurve(ParticleSystem.MinMaxCurve curve, float ratio)
        {
            if (curve.mode == ParticleSystemCurveMode.Constant) curve.constant *= ratio;
            else if (curve.mode == ParticleSystemCurveMode.TwoConstants)
            { curve.constantMin *= ratio; curve.constantMax *= ratio; }
            else curve.curveMultiplier *= ratio;
            return curve;
        }

        private void BuildSimulationRoots(ParticleSystem[] systems)
        {
            var subSystems = new HashSet<ParticleSystem>();
            foreach (var ps in systems)
            {
                var sub = ps.subEmitters;
                for (int i = 0; i < sub.subEmittersCount; i++) subSystems.Add(sub.GetSubEmitterSystem(i));
            }
            var roots = new List<ParticleSystem>();
            foreach (var row in _layers)
            {
                row.SubEmitter = subSystems.Contains(row.System);
                if (!row.Active || subSystems.Contains(row.System)) continue;
                bool parentSystem = false;
                for (var p = row.System.transform.parent; p != null && p != transform; p = p.parent)
                    if (p.GetComponent<ParticleSystem>() != null) { parentSystem = true; break; }
                if (!parentSystem) roots.Add(row.System);
            }
            _simulationRoots = roots.ToArray();
        }

        private static AnimationClip PreviewClip(AnimationClip[] clips, AnimationClip requested)
        {
            if (requested != null)
            {
                foreach (var clip in clips) if (clip == requested) return clip;
                return null;
            }
            AnimationClip only = null;
            foreach (var clip in clips)
            {
                if (clip == null) continue;
                if (only != null && only != clip) return null;
                only = clip;
            }
            return only;
        }

        private void BuildAnimationLayers()
        {
            var animators = _instance.GetComponentsInChildren<Animator>(true);
            _animations = new AnimationLayer[animators.Length];
            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                _animations[i] = new AnimationLayer { Animator = animator,
                    Clip = PreviewClip(animator.runtimeAnimatorController.animationClips, _settings.PreviewClip) };
                animator.fireEvents = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                // The original controller runs normally in gameplay. Preview samples
                // its single verified authored clip before stepping the native particles.
                if (_settings.PreviewControlled) animator.enabled = false;
            }
            var transforms = _instance.GetComponentsInChildren<Transform>(true);
            _restTransforms = new RestTransform[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                _restTransforms[i] = new RestTransform { Transform = t, Position = t.localPosition,
                    Rotation = t.localRotation, Scale = t.localScale, Active = t.gameObject.activeSelf };
            }
        }

        private void BuildTintSlots()
        {
            var slots = new List<TintSlot>();
            foreach (var renderer in _instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    var material = materials[slot];
                    if (material == null || material.shader == null) continue;
                    var ids = new List<int>(); var colors = new List<Color>();
                    var shader = material.shader;
                    for (int i = 0; i < shader.GetPropertyCount(); i++)
                    {
                        // ShaderGraph custom color GUIDs are supported; vector UV/flow properties aren't recolored.
                        if (shader.GetPropertyType(i) != ShaderPropertyType.Color) continue;
                        int id = shader.GetPropertyNameId(i);
                        ids.Add(id); colors.Add(material.GetColor(id));
                    }
                    if (ids.Count == 0) UntintedMaterialSlots++;
                    var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, slot);
                    if(_settings.PatternFocus && renderer.name.IndexOf("pattern",StringComparison.OrdinalIgnoreCase)>=0 && material.HasProperty("_Emission"))block.SetFloat("_Emission",1f);
                    slots.Add(new TintSlot { Renderer = renderer, Slot = slot, Block = block,
                        Properties = ids.ToArray(), Original = colors.ToArray() });
                }
            }
            _tints = slots.ToArray();
        }

        // Preview seeks restart native systems with stable seeds; no live gameplay Update
        // is replaced by a preview sample. The owner must move the host before sampling.
        public void Sample(float age)
        {
            if (!_configured || !_settings.PreviewControlled || !Finite(age) || _instance == null) return;
            if (age < 0)
            {
                // Delayed impacts remain configured during a backwards preview seek,
                // but cannot leave their previously sampled particles visible before hit.
                foreach (var row in _layers)
                    row.System.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                _instance.SetActive(false); Age = 0; IsComplete = false;
                Status = "PREVIEW_WAITING_FOR_START";
                return;
            }
            Age = age; IsComplete = age >= _endAt;
            _instance.SetActive(!IsComplete);
            foreach (var row in _layers)
            {
                row.System.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var emission = row.System.emission; emission.enabled = row.EmissionEnabled;
                row.WasActive = false;
            }
            _emissionStopped = false;
            if (IsComplete) { Status = "PREVIEW_ENDED"; return; }
            if (_animations.Length == 0 && _settings.HoldAt > 0)
            {
                foreach (var ps in _simulationRoots) ps.Simulate(Mathf.Min(age, _settings.HoldAt), true, true, true);
                if (age >= _settings.HoldAt) foreach (var ps in _simulationRoots) ps.Pause(true);
            }
            else if (_animations.Length != 0) SampleAnimated(age);
            else
            {
                float beforeStop = Mathf.Min(age, _releaseAt);
                foreach (var ps in _simulationRoots) ps.Simulate(beforeStop, true, true, true);
                if (age >= _releaseAt)
                {
                    StopEmission();
                    foreach (var ps in _simulationRoots) ps.Simulate(age - _releaseAt, true, false, true);
                }
            }
            ApplyTint(age);
            Status = "PREVIEW_SAMPLE_ONLY";
        }

        private void SampleAnimated(float age)
        {
            foreach (var row in _restTransforms)
            {
                // Leave the extracted carrier's placement/scale under caller control.
                if (row.Transform == _instance.transform) continue;
                row.Transform.localPosition = row.Position; row.Transform.localRotation = row.Rotation;
                row.Transform.localScale = row.Scale; row.Transform.gameObject.SetActive(row.Active);
            }
            SampleAnimationAt(0);
            // Fixed 60 Hz seeks preserve the animation's moving emission positions and
            // delayed activation. Sampling only the final pose then simulating age seconds
            // would incorrectly emit Explosion from t=0. No simulation runs in live Update.
            const float step = 1f / 60f;
            float cursor = 0;
            while (cursor < age)
            {
                float next = Mathf.Min(age, cursor + step);
                if (cursor < _releaseAt && next > _releaseAt) next = _releaseAt;
                SampleAnimationAt(next);
                if (cursor >= _releaseAt && !_emissionStopped) StopEmission();
                foreach (var row in _layers)
                {
                    var ps = row.System;
                    bool active = ps.gameObject.activeInHierarchy;
                    if (row.SubEmitter) continue; // parent native simulation owns sub-emissions
                    if (!active)
                    {
                        if (row.WasActive) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                    else
                    {
                        if (!row.WasActive)
                        {
                            ps.Simulate(0, false, true, false);
                            if (_emissionStopped)
                            { var emission = ps.emission; emission.enabled = false; }
                        }
                        ps.Simulate(next - cursor, false, false, false);
                    }
                    row.WasActive = active;
                }
                cursor = next;
            }
        }

        private void SampleAnimationAt(float age)
        {
            foreach (var layer in _animations)
            {
                float time = layer.Clip.isLooping && layer.Clip.length > 0
                    ? Mathf.Repeat(age, layer.Clip.length) : Mathf.Min(age, layer.Clip.length);
                layer.Clip.SampleAnimation(layer.Animator.gameObject, time);
            }
        }

        public void Release()
        {
            if (!_configured || IsComplete) return;
            _releaseAt = Mathf.Min(_releaseAt, Age);
            _endAt = Mathf.Min(_endAt, _releaseAt + _settings.FadeSeconds);
            if (!_settings.PreviewControlled) StopEmission();
        }

        private void Update()
        {
            if (!_configured || _settings.PreviewControlled || IsComplete) return;
            Age = Mathf.Max(0, Time.time - _startedAt);
            if (!_poseHeld && _settings.HoldAt > 0 && _animations.Length == 0 && Age >= _settings.HoldAt)
            {
                foreach (var ps in _simulationRoots) ps.Pause(true);
                _poseHeld = true;
            }
            if (Age >= _releaseAt && !_emissionStopped) StopEmission();
            if (Age >= _endAt)
            {
                ClearInstance(); IsComplete = true; Status = "RUNTIME_ENDED";
                if(DestroyHostOnCompletion)Destroy(gameObject);
                return;
            }
            ApplyTint(Age);
        }

        private void StopEmission()
        {
            if (_layers != null) foreach (var row in _layers)
                if (row.System != null)
                {
                    row.System.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    // An Animator activating a child after release must not restart its bursts.
                    var emission = row.System.emission; emission.enabled = false;
                }
            _emissionStopped = true;
            if (_poseHeld && _simulationRoots != null) foreach (var ps in _simulationRoots) ps.Pause(true);
        }

        private float _viewOpacity = 1f;
        // Per-camera presentation attenuation; reuses tint blocks without resimulating particles.
        public void SetViewOpacity(float opacity)
        {
            _viewOpacity = Mathf.Clamp01(opacity);
            if (_tints != null) ApplyTint(Age);
        }

        private void ApplyTint(float age)
        {
            float dry = float.IsPositiveInfinity(_releaseAt) ? 0
                : Mathf.SmoothStep(0, 1, Mathf.InverseLerp(_releaseAt, _endAt, age));
            float opacity = _settings.PreserveAuthored?1:1-dry;
            foreach (var slot in _tints)
            {
                if (slot.Renderer == null) continue;
                for (int i = 0; i < slot.Properties.Length; i++)
                {
                    Color original = slot.Original[i];
                    float sourceValue = _settings.PreserveAuthored?Mathf.Max(original.r,Mathf.Max(original.g,original.b)):Mathf.Min(1.35f, Mathf.Max(original.r, Mathf.Max(original.g, original.b)));
                    float peak = Mathf.Max(.01f, Mathf.Max(_pigment.r, Mathf.Max(_pigment.g, _pigment.b)));
                    Color colored = new Color(sourceValue * _pigment.r / peak * .70f,
                        sourceValue * _pigment.g / peak * .70f, sourceValue * _pigment.b / peak * .70f, original.a);
                    Color value = Color.Lerp(colored, new Color(_ink.r, _ink.g, _ink.b, original.a), dry);
                    value.a = original.a * _pigment.a * opacity;
                    if(_settings.PreserveAuthored)value=new Color(sourceValue*_pigment.r/peak,sourceValue*_pigment.g/peak,sourceValue*_pigment.b/peak,original.a*(_settings.FiniteWindow?1-dry:1));
                    float brightness = _settings.Brightness > 0 && Finite(_settings.Brightness) ? _settings.Brightness : 1f;
                    if(_settings.PatternFocus && slot.Renderer.name.IndexOf("pattern",StringComparison.OrdinalIgnoreCase)<0)brightness=.25f;
                    else if(_settings.PatternFocus)brightness*=peak;
                    value.r *= brightness; value.g *= brightness; value.b *= brightness;
                    value.r *= _viewOpacity; value.g *= _viewOpacity; value.b *= _viewOpacity;
                    value.a *= _viewOpacity;
                    slot.Block.SetColor(slot.Properties[i], value);
                }
                slot.Renderer.SetPropertyBlock(slot.Block, slot.Slot);
            }
        }

        public void Clear()
        {
            ClearInstance(); _configured = false; _emissionStopped = false;
            Age = 0; IsComplete = true; Status = "CLEARED"; Diagnostic = "";
        }

        private void ClearInstance()
        {
            if (_layers != null) foreach (var row in _layers)
                if (row.System != null) row.System.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (_instance != null) { _instance.SetActive(false); Dispose(_instance); }
            _instance = null; _layers = null; _simulationRoots = null; _tints = null;
            _animations = null; _restTransforms = null;
        }

        private void OnDisable() { if (_configured) Clear(); }
        internal static ParticleSystem.MinMaxGradient Neutral(ParticleSystem.MinMaxGradient color)
        {
            color.colorMin = Gray(color.colorMin); color.colorMax = Gray(color.colorMax);
            if (color.gradientMin != null) color.gradientMin = Gray(color.gradientMin);
            if (color.gradientMax != null) color.gradientMax = Gray(color.gradientMax);
            return color;
        }
        private static Color Gray(Color color)
        {
            float value = Mathf.Max(color.r, color.g, color.b);
            return new Color(value, value, value, color.a);
        }
        private static Gradient Gray(Gradient source)
        {
            var keys = source.colorKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].color = Gray(keys[i].color);
            var gradient = new Gradient { mode = source.mode }; gradient.SetKeys(keys, source.alphaKeys); return gradient;
        }
        private void OnDestroy() { ClearInstance(); }
        private bool Reject(string reason) { Status = "REJECTED"; Diagnostic = reason; return false; }
        private static bool ActiveBelowRoot(Transform part, Transform root)
        {
            for (var t = part; t != null && t != root; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return true;
        }
        private static uint Seed(uint seed, int index)
        { unchecked { uint value = (seed == 0 ? 1 : seed) * 16777619u ^ (uint)(index + 1) * 2166136261u; return value == 0 ? 1 : value; } }
        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        private static bool Finite(Color c) => Finite(c.r) && Finite(c.g) && Finite(c.b) && Finite(c.a);
        private static void Dispose(UnityEngine.Object obj)
        { if (obj != null) { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); } }
    }
}
