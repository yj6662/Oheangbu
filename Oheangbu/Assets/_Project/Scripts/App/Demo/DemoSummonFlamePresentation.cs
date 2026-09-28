using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    // Reuses the owned KTP flame asset. Combat determines origin, direction and time.
    // No Update, damage, separate lifetime, camera following or independent random path.
    [ExecuteAlways] // Manual-clock previews must receive the same material cleanup callbacks as Play.
    public sealed class DemoSummonFlamePresentation : MonoBehaviour
    {
        const float Step = 1f / 120f;
        GameObject instance;
        ParticleSystem[] particles;
        SummonFlameAttackPlan plan;
        float simulated;
        readonly Dictionary<Material,Material> ownedMaterials = new Dictionary<Material,Material>();
        public int ParticleSystems => particles == null ? 0 : particles.Length;
        public int LiveParticles { get; private set; }
        public float SimulatedTime => simulated;

        public void Sample(SummonCombatProfile profile, SummonFlameAttackPlan next, float age, bool active)
        {
            if (!ReferenceEquals(plan, next))
            {
                Clear(); plan = next;
                if (plan != null && profile.FlamePrefab != null) Build(profile);
            }
            LiveParticles = 0;
            if (instance == null) return;
            if (!active || plan == null || plan.IsCancelled || age >= plan.EndAt)
            { Clear(); return; }
            // The field is installed at the cast snapshot; particles do not chase a moving target.
            instance.transform.SetPositionAndRotation(plan.Origin, Quaternion.LookRotation(plan.Direction, Vector3.up));
            float target = Mathf.Max(0, age - plan.ReleaseAt);
            if (target < simulated)
            {
                foreach (var ps in particles) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                simulated = 0;
            }
            while (simulated + Step <= target + .000001f)
            {
                bool emit = plan.ReleaseAt + simulated < plan.SprayEndAt;
                foreach (var ps in particles)
                {
                    var emission = ps.emission; emission.enabled = emit;
                    ps.Simulate(Step, false, false, false);
                }
                simulated += Step;
            }
            foreach (var ps in particles) LiveParticles += ps.particleCount;
        }

        void Build(SummonCombatProfile profile)
        {
            instance = Instantiate(profile.FlamePrefab, transform, false);
            instance.name = "KTP_Haetae_Flame_ManualClock";
            instance.transform.localScale = new Vector3(plan.Range * Mathf.Tan(plan.HalfAngleDegrees * Mathf.Deg2Rad), 1, plan.Range);
            foreach (var script in instance.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            particles = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                var ps = particles[i]; ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.playOnAwake = false; main.useUnscaledTime = false;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                ps.useAutoRandomSeed = false; ps.randomSeed = (uint)(743 + i);
                ps.Simulate(0, false, true, false);
                if(profile.FlameConstrainVisual)
                {
                    var renderer=ps.GetComponent<ParticleSystemRenderer>();
                    var materials=renderer.sharedMaterials;
                    for(int m=0;m<materials.Length;m++)
                    {
                        var source=materials[m];
                        if(source==null)throw new InvalidOperationException("Flame source material missing");
                        if(!ownedMaterials.TryGetValue(source,out var replacement))
                        {
                            bool additive=source.shader.name.IndexOf("AdditiveBlend",StringComparison.OrdinalIgnoreCase)>=0;
                            bool alpha=source.shader.name.IndexOf("AlphaBlend",StringComparison.OrdinalIgnoreCase)>=0;
                            if(!additive&&!alpha)throw new InvalidOperationException("Unmapped flame source shader: "+source.shader.name);
                            replacement=new Material(source){name=source.name+"_OwnedCone",shader=additive?profile.FlameAdditiveShader:profile.FlameAlphaShader};
                            replacement.SetVector("_FlameConeOrigin",plan.Origin);
                            replacement.SetVector("_FlameConeDirection",plan.Direction);
                            replacement.SetFloat("_FlameConeRange",plan.Range);
                            replacement.SetFloat("_FlameConeHalfAngle",plan.HalfAngleDegrees);
                            replacement.SetFloat("_FlameConeHeight",plan.VerticalTolerance);
                            replacement.SetFloat("_FlameConeFeather",profile.FlameBoundaryFeather);
                            replacement.SetFloat("_FlameIntensity",profile.FlameVisualIntensity);
                            ownedMaterials.Add(source,replacement);
                        }
                        materials[m]=replacement;
                    }
                    renderer.sharedMaterials=materials;
                }
            }
            instance.SetActive(true);
        }

        void Clear()
        {
            if (instance != null)
            {
                instance.SetActive(false);
                if (Application.isPlaying) Destroy(instance); else DestroyImmediate(instance);
            }
            instance = null; particles = null; simulated = 0; LiveParticles = 0;
            foreach(var material in ownedMaterials.Values)
                if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);
            ownedMaterials.Clear();
        }
        void OnDisable() { Clear(); }
        void OnDestroy() { Clear(); }
    }
}
