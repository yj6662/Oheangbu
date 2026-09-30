using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.App.Demo
{
    // Visuals sample the combat owner's immutable plan; they never resolve hits or own a second timer.
    public sealed class CheongryongAttackPresentation : MonoBehaviour
    {
        [SerializeField] CheongryongCombatController controller;
        [SerializeField] GameObject warningPrefab, spikePrefab, impactPrefab;
        [SerializeField] Mesh projectileMesh;
        [SerializeField] Material projectileMaterial;
        CheongryongCombatController bound;
        CheongryongGrowthController growth;
        CheongryongAttackPlan plan;
        GameObject warning, spike, projectile, growthWarning;
        Renderer[] spikeRenderers;
        MaterialPropertyBlock block;
        float spikeHeight;
        readonly List<Remnant> remnants = new List<Remnant>();
        sealed class Remnant { public GameObject Instance; public float Until; }
        public bool HasAllSources => warningPrefab != null && spikePrefab != null && impactPrefab != null && projectileMesh != null && projectileMaterial != null;
        // #306 read-only: the live bolt body (null while not flying) so the organ thread can reach it
        public Transform ProjectileTransform => projectile != null && projectile.activeSelf ? projectile.transform : null;
        public int LiveVisualCount => (warning!=null?1:0)+(spike!=null?1:0)+(projectile!=null?1:0)+(growthWarning!=null?1:0)+remnants.Count;

        public void Configure(CheongryongCombatController source, GameObject warningSource, GameObject spikeSource,
            Mesh boltMesh, Material boltMaterial, GameObject impactSource)
        {
            Unbind(); Clear(); controller=source;warningPrefab=warningSource;spikePrefab=spikeSource;
            projectileMesh=boltMesh;projectileMaterial=boltMaterial;impactPrefab=impactSource;
            if(isActiveAndEnabled)Bind();
        }
        void OnEnable(){Bind();}
        void OnDisable(){Unbind();Clear();}
        void OnDestroy(){Unbind();Clear();}
        void Bind()
        {
            if(controller==null)controller=GetComponent<CheongryongCombatController>();
            if(bound==controller)return; Unbind();bound=controller;
            if(bound==null)return;
            bound.AttackStarted+=Begin;bound.AttackEnded+=End;bound.AttackImpactResolved+=Impact;
            growth=GetComponent<CheongryongGrowthController>();if(growth!=null)growth.StateChanged+=Growth;
        }
        void Unbind()
        {
            if(bound!=null){bound.AttackStarted-=Begin;bound.AttackEnded-=End;bound.AttackImpactResolved-=Impact;}
            if(growth!=null)growth.StateChanged-=Growth;bound=null;growth=null;
        }
        void Begin(CheongryongAttackPlan next)
        {
            if(next==null||next.IsCancelled||next.Attack.Instigator!=controller.Vitals)return;
            ClearAttack();plan=next;
            if(next.Kind==CheongryongAttackKind.RootEruption)
            {
                warning=Spawn(warningPrefab,next.TargetPoint+Vector3.up*.025f,next.Radius*1.5f);
                TimeParticles(warning,next.ReleaseAt-next.StartedAt);
                spike=Spawn(spikePrefab,next.TargetPoint,1.2f);
                if(spike!=null)
                {
                    spikeRenderers=spike.GetComponentsInChildren<Renderer>(true);
                    spikeHeight=2.5f;
                    if(spikeRenderers.Length>0){Bounds b=spikeRenderers[0].bounds;foreach(var r in spikeRenderers)b.Encapsulate(r.bounds);spikeHeight=Mathf.Max(.1f,b.size.y);}
                    SetSpike(0);
                }
            }
            else if(next.Kind==CheongryongAttackKind.WoodProjectile&&projectileMesh!=null&&projectileMaterial!=null)
            {
                projectile=new GameObject("Cheongryong_WoodProjectile");SceneManager.MoveGameObjectToScene(projectile,gameObject.scene);
                projectile.AddComponent<MeshFilter>().sharedMesh=projectileMesh;
                projectile.AddComponent<MeshRenderer>().sharedMaterial=projectileMaterial;
                projectile.transform.SetPositionAndRotation(next.Origin,Quaternion.LookRotation(next.Direction));
                // Source bamboo bolt has its long axis along local Z. Retain its source proportions.
                projectile.transform.localScale=Vector3.one*.8f;projectile.SetActive(false);
            }
        }
        void LateUpdate()
        {
            if(controller==null||controller.Vitals==null||!controller.Vitals.IsAlive){Clear();return;}
            for(int i=remnants.Count-1;i>=0;i--)if(Time.time>=remnants[i].Until){Remove(remnants[i].Instance);remnants.RemoveAt(i);}
            if(plan==null||plan.IsCancelled)return;
            if(plan.Kind==CheongryongAttackKind.RootEruption)
            {
                SetSpike(Mathf.SmoothStep(0,1,Mathf.InverseLerp(plan.ReleaseAt-.12f,plan.ReleaseAt,plan.SampleTime)));
                if(plan.SampleTime>=plan.ReleaseAt){Remove(warning);warning=null;}
            }
            if(projectile!=null)
            {
                projectile.SetActive(plan.SampleTime>=plan.ReleaseAt&&!plan.ContactConsumed);
                projectile.transform.position=plan.ProjectilePosition;
            }
        }
        void SetSpike(float rise)
        {
            if(spike==null||plan==null)return;
            spike.transform.position=plan.TargetPoint-Vector3.up*((1-rise)*spikeHeight);
            if(block==null)block=new MaterialPropertyBlock();
            foreach(var r in spikeRenderers)
            {
                r.enabled=rise>0;r.GetPropertyBlock(block);block.SetFloat("_GroundY",plan.TargetPoint.y);
                block.SetFloat("_Visibility",1);r.SetPropertyBlock(block);
            }
        }
        void Impact(CheongryongAttackImpact hit)
        {
            if(hit.Plan!=plan||!hit.Contact||hit.AppliedDamage<=0||hit.Outcome==ParryOutcome.Half)return;
            // Parry success/half bursts remain in CombatLoopWiring's one owned-contact route.
            var effect=Spawn(impactPrefab,hit.Point,.5f);TimeParticles(effect,.4f);Keep(effect,.45f);
        }
        void End(CheongryongAttackPlan ended,bool cancelled)
        {
            if(plan!=ended)return;
            if(cancelled){foreach(var r in remnants)Remove(r.Instance);remnants.Clear();}
            if(!cancelled&&spike!=null){SetSpike(1);Keep(spike,.35f);spike=null;}
            ClearAttack();
        }
        void Growth(CheongryongGrowthState state)
        {
            Remove(growthWarning);growthWarning=null;
            if(state!=CheongryongGrowthState.Windup)return;
            ClearAttack();
            growthWarning=Spawn(warningPrefab,transform.position+Vector3.up*.03f,3f);TimeParticles(growthWarning,4f);
        }
        void Keep(GameObject instance,float life)
        {
            if(instance==null)return;
            if(remnants.Count>=8){Remove(remnants[0].Instance);remnants.RemoveAt(0);}
            remnants.Add(new Remnant{Instance=instance,Until=Time.time+life});
        }
        GameObject Spawn(GameObject source,Vector3 position,float scale)
        {
            if(source==null)return null;
            var instance=Instantiate(source,position,source.transform.rotation);instance.name="Cheongryong_"+source.name;
            instance.transform.localScale=source.transform.localScale*scale;SceneManager.MoveGameObjectToScene(instance,gameObject.scene);
            foreach(var c in instance.GetComponentsInChildren<Collider>(true))c.enabled=false;return instance;
        }
        static void TimeParticles(GameObject instance,float seconds)
        {
            if(instance==null)return;var systems=instance.GetComponentsInChildren<ParticleSystem>(true);float total=.01f;
            foreach(var ps in systems){var m=ps.main;total=Mathf.Max(total,m.startDelay.constantMax+m.duration+m.startLifetime.constantMax);}
            foreach(var ps in systems)
            {
                ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);var m=ps.main;m.loop=false;m.useUnscaledTime=false;
                m.simulationSpeed=total/Mathf.Max(.01f,seconds);m.stopAction=ParticleSystemStopAction.None;ps.Play(false);
            }
        }
        void ClearAttack(){Remove(warning);Remove(spike);Remove(projectile);warning=spike=projectile=null;spikeRenderers=null;plan=null;}
        void Clear(){ClearAttack();Remove(growthWarning);growthWarning=null;foreach(var r in remnants)Remove(r.Instance);remnants.Clear();}
        static void Remove(Object instance){if(instance==null)return;if(Application.isPlaying)Destroy(instance);else DestroyImmediate(instance);}
    }
}
