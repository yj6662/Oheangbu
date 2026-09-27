using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.Demo
{
    // One geometry/particle sample clock shared with the committed damage front.
    // This class never queries enemies or generates contact feedback.
    [ExecuteAlways]
    public sealed class DemoSummonWaterPresentation : MonoBehaviour
    {
        const int Rings=49, Sides=12;
        Mesh mesh;
        GameObject jet;
        MeshRenderer surface;
        ParticleSystem foam;
        readonly Vector3[] vertices=new Vector3[Rings*Sides];
        readonly Vector2[] uv=new Vector2[Rings*Sides];
        readonly ParticleSystem.Particle[] drops=new ParticleSystem.Particle[64];
        MaterialPropertyBlock block;
        public float VisibleLength {get;private set;}
        public int LiveDrops {get;private set;}
        public Vector3 VisibleStart {get;private set;}
        public Vector3 VisibleEnd {get;private set;}
        public void Sample(SummonCombatProfile profile,SummonWaterAttackPlan plan,float age,bool active)
        {
            if(!active||plan==null||plan.IsCancelled||age<plan.ReleaseAt||age>=plan.EndAt)
            {Hide();return;}
            if(jet==null)Build(profile);
            if(jet==null)return;
            float tail=plan.TailDistance;
            float head=plan.FrontDistance;
            if(head<=tail+.001f){Hide();return;}
            VisibleStart=plan.Origin+plan.Direction*tail;VisibleEnd=plan.Origin+plan.Direction*head;
            VisibleLength=head-tail;jet.SetActive(true);
            jet.transform.SetPositionAndRotation(plan.Origin,Quaternion.LookRotation(plan.Direction,Vector3.up));
            float local=age-plan.ReleaseAt;
            float fade=Mathf.Clamp01((head-tail)/.18f);
            for(int ring=0;ring<Rings;ring++)
            {
                float u=(float)ring/(Rings-1),z=Mathf.Lerp(tail,head,u);
                float taper=Mathf.SmoothStep(.28f,1,Mathf.Min(u*10,(1-u)*10));
                for(int side=0;side<Sides;side++)
                {
                    float theta=(float)side/Sides*Mathf.PI*2;
                    // Never exceeds the combat corridor radius; ripples are radial only.
                    float radius=plan.Radius*(.53f+.10f*Mathf.Sin(z*19-local*28+theta*3)+.06f*Mathf.Sin(z*33-local*37-theta*2))*taper;
                    int i=ring*Sides+side;vertices[i]=new Vector3(Mathf.Cos(theta)*radius,Mathf.Sin(theta)*radius,z);
                    uv[i]=new Vector2((float)side/Sides,z*1.4f);
                }
            }
            mesh.vertices=vertices;mesh.uv=uv;mesh.RecalculateNormals();mesh.RecalculateBounds();
            block.Clear();block.SetFloat("_FlowTime",local);block.SetFloat("_Fade",fade);surface.SetPropertyBlock(block);
            LiveDrops=0;
            if(foam!=null && VisibleLength > .05f)
            {
                // Initialize native particle storage after creation/reactivation without advancing time.
                foam.Simulate(0,false,false,false);
                // Deterministic ParticleSystem particles inside the same visible interval.
                // Re-evaluation at a paused time is identical and owns no emission timer.
                for(int i=0;i<drops.Length;i++)
                {
                    float cycle=Mathf.Repeat(local*1.7f+i*.618034f,1);
                    float z=Mathf.Lerp(tail+.02f,head-.02f,cycle),angle=i*2.399963f+local*.5f;
                    float r=plan.Radius*(.42f+.17f*Mathf.Sin(i*8.21f));
                    float size=.017f+.018f*Mathf.Repeat(i*.471f,1);
                    drops[i]=new ParticleSystem.Particle{position=new Vector3(Mathf.Cos(angle)*r,Mathf.Sin(angle)*r,z),
                        startSize=size,startLifetime=1,remainingLifetime=.5f,
                        startColor=new Color(.75f,.86f,.88f,.65f*fade),rotation=i*37};
                }
                foam.SetParticles(drops,drops.Length);LiveDrops=foam.particleCount;
            }
            else if(foam!=null)foam.Clear();
        }
        void Build(SummonCombatProfile profile)
        {
            if(profile.WaterJetMaterial==null)return;
            block=new MaterialPropertyBlock();jet=new GameObject("TurtleWater_ClockedStream");jet.transform.SetParent(transform,false);
            mesh=new Mesh{name="TurtleWater_OwnedSurface"};mesh.MarkDynamic();
            var indices=new int[(Rings-1)*Sides*6];int k=0;
            for(int r=0;r<Rings-1;r++)for(int s=0;s<Sides;s++)
            {int a=r*Sides+s,b=r*Sides+(s+1)%Sides,c=a+Sides,d=b+Sides;
             indices[k++]=a;indices[k++]=b;indices[k++]=c;indices[k++]=b;indices[k++]=d;indices[k++]=c;}
            mesh.vertices=vertices;mesh.triangles=indices;
            jet.AddComponent<MeshFilter>().sharedMesh=mesh;surface=jet.AddComponent<MeshRenderer>();surface.sharedMaterial=profile.WaterJetMaterial;
            surface.shadowCastingMode=ShadowCastingMode.Off;surface.receiveShadows=false;
            if(profile.WaterFoamMaterial!=null)
            {
                var child=new GameObject("StreamFoam");child.transform.SetParent(jet.transform,false);foam=child.AddComponent<ParticleSystem>();
                foam.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=foam.main;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.Local;
                main.maxParticles=drops.Length;main.startSpeed=0;main.startLifetime=1;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
                var emission=foam.emission;emission.enabled=false;var shape=foam.shape;shape.enabled=false;
                var renderer=foam.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=profile.WaterFoamMaterial;
                renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            }
        }
        void Hide(){if(jet!=null)jet.SetActive(false);if(foam!=null)foam.Clear();VisibleLength=0;LiveDrops=0;}
        void Dispose()
        {Hide();if(jet!=null){if(Application.isPlaying)Destroy(jet);else DestroyImmediate(jet);}jet=null;
         if(mesh!=null){if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}mesh=null;}
        void OnDisable(){Hide();}
        void OnDestroy(){Dispose();}
    }
}
