using UnityEngine;

namespace Oheangbu.App.Demo
{
    // Phase-gated reuse of the original formation, mane and dissolve particle systems.
    public sealed class DemoSummonPhaseParticles : MonoBehaviour
    {
        ParticleSystem[] systems;
        float[] sampled;
        GameObject instance;
        public void Sample(SummonCombatProfile profile,SummonCombatClock clock)
        {
            if(instance==null&&profile.DissolveDebris!=null&&gameObject.activeInHierarchy)
            {
                instance=Instantiate(profile.DissolveDebris,transform,false);
                systems=instance.GetComponentsInChildren<ParticleSystem>(true);sampled=new float[systems.Length];
                for(int i=0;i<systems.Length;i++)
                {
                    var ps=systems[i];ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main=ps.main;main.playOnAwake=false;main.useUnscaledTime=false;
                    main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
                    if(ps.name=="ManeEmbers")main.loop=true;
                    ps.useAutoRandomSeed=false;ps.randomSeed=(uint)(601+i);sampled[i]=-1;
                }
            }
            if(systems==null)return;
            for(int i=0;i<systems.Length;i++)
            {
                var ps=systems[i];float local=-1;
                if(ps.name.StartsWith("Formation")&&clock.Phase==SummonPhase.Forming)local=clock.Elapsed;
                if(ps.name=="ManeEmbers"&&clock.IsCombatActive)local=clock.Elapsed-profile.FormationSeconds;
                if(ps.name.StartsWith("Dissolve")&&clock.Phase==SummonPhase.Dissolving)local=clock.PhaseElapsed;
                if(local<0)
                {if(sampled[i]>=0)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);sampled[i]=-1;continue;}
                if(sampled[i]<0||local<sampled[i])
                {ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);ps.Simulate(0,false,true,false);sampled[i]=0;}
                const float step=1f/120f;
                while(sampled[i]+step<=local+.000001f){ps.Simulate(step,false,false,false);sampled[i]+=step;}
            }
        }
        void OnDisable(){if(systems!=null)foreach(var ps in systems)if(ps!=null)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);}
    }
}
