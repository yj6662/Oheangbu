using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace Oheangbu.App
{
    // Fixed-size presentation resources. Every strand and bead shares the extraction clock.
    public sealed class HarvestInkFlowRenderer : IDisposable
    {
        private readonly HarvestInkFlowProfileSO profile;
        private readonly GameObject root;
        private readonly LineRenderer[] lines;
        private readonly Vector3[] points;
        private readonly Material material;
        private readonly ParticleSystem beads;
        private readonly ParticleSystem.Particle[] particles;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private float age, envelope;
        private bool extracting;
        public bool Visible => envelope > .001f;
        public Vector3 LastSink { get; private set; }
        public int StrandCount => lines.Length;
        public int LiveDroplets { get; private set; }
        public HarvestInkFlowRenderer(Transform parent, HarvestInkFlowProfileSO settings)
        {
            profile=settings;
            var old=parent.Find("Harvest_InkFlow_Owned");if(old!=null){old.gameObject.SetActive(false);if(Application.isPlaying)UnityEngine.Object.Destroy(old.gameObject);else UnityEngine.Object.DestroyImmediate(old.gameObject);}
            root=new GameObject("Harvest_InkFlow_Owned"); root.transform.SetParent(parent,false);
            material=new Material(settings.Material) {name="HarvestInkFlow_Runtime"};
            lines=new LineRenderer[1+Mathf.Clamp(settings.FineThreads,0,2)];points=new Vector3[Mathf.Clamp(settings.Points,12,48)];
            for(int i=0;i<lines.Length;i++)
            {
                var go=new GameObject(i==0?"MainInkThread":"FineInkThread_"+i);go.transform.SetParent(root.transform,false);
                var line=go.AddComponent<LineRenderer>(); lines[i]=line;line.sharedMaterial=material;
                line.useWorldSpace=true;line.positionCount=points.Length;line.textureMode=LineTextureMode.Stretch;
                line.alignment=LineAlignment.View;line.numCapVertices=3;line.numCornerVertices=2;
                line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;
            }
            var beadObject=new GameObject("FlowDroplets");beadObject.transform.SetParent(root.transform,false);
            beads=beadObject.AddComponent<ParticleSystem>();beads.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=beads.main;main.playOnAwake=false;main.loop=false;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.maxParticles=Mathf.Clamp(settings.Droplets,1,20);main.startSpeed=0;main.startLifetime=100;main.useUnscaledTime=false;
            var emission=beads.emission;emission.enabled=false;var shape=beads.shape;shape.enabled=false;
            var renderer=beads.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.renderMode=ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            particles=new ParticleSystem.Particle[Mathf.Clamp(settings.Droplets,0,20)];
        }
        public static Vector3 Curve(Vector3 source,Vector3 sink,Vector3 approach,float t,float sag)
        {
            t=Mathf.Clamp01(t);var delta=sink-source;
            var a=source+delta*.32f+Vector3.down*sag;
            var b=sink+approach.normalized*Mathf.Min(.24f,delta.magnitude*.16f);
            float u=1-t;return u*u*u*source+3*u*u*t*a+3*u*t*t*b+t*t*t*sink;
        }
        public void Tick(bool active,Vector3 source,Vector3 sink,Vector3 approach,float delta)
        {
            if(delta<=0f) { Clear();return; }
            if(active&&!extracting)age=0f;
            extracting=active;age+=delta;LastSink=sink;
            envelope=Mathf.MoveTowards(envelope,active?1:0,delta/Mathf.Max(.03f,active?profile.RevealSeconds:profile.ReleaseSeconds));
            if(envelope<=.001f) { Clear();return; }
            float reveal=Mathf.Clamp01(age/Mathf.Max(.03f,profile.RevealSeconds));
            float length=Mathf.Max(.05f,Vector3.Distance(source,sink));
            var axis=(sink-source).normalized;var side=Vector3.Cross(axis,Vector3.up).normalized;
            if(side.sqrMagnitude<.1f)side=Vector3.right;
            block.SetFloat("_FlowTime",age*profile.FlowMetersPerSecond/length);
            block.SetFloat("_Bead",0);block.SetColor("_Tint",profile.InkColor);
            for(int j=0;j<lines.Length;j++)
            {
                float phase=j*2.31f;
                for(int i=0;i<points.Length;i++)
                {
                    float t=i/(float)(points.Length-1)*reveal;
                    Vector3 p=Curve(source,sink,approach,t,profile.Sag);
                    float ends=Mathf.Sin(t*Mathf.PI);
                    p+=side*ends*(j==0?.008f:profile.Spread)*Mathf.Sin(t*5.2f-age*2.1f+phase);
                    p+=Vector3.up*ends*(j==0?.004f:profile.Spread*.3f)*Mathf.Sin(t*7-age*1.6f+phase);
                    points[i]=p;
                }
                var line=lines[j];line.enabled=true;line.SetPositions(points);
                float width=j==0?1f:.28f;
                line.startWidth=profile.RootWidth*width*envelope;line.endWidth=profile.TipWidth*width*envelope;
                var color=Color.white;color.a=envelope;line.startColor=color;line.endColor=color;line.SetPropertyBlock(block);
            }
            int count=0;
            for(int i=0;i<particles.Length;i++)
            {
                float t=Mathf.Repeat(age*profile.FlowMetersPerSecond/length+i*.6180339f,1f);
                if(t>reveal)continue;
                var particle=new ParticleSystem.Particle();particle.position=Curve(source,sink,approach,t,profile.Sag);
                particle.startSize=profile.DropletSize*(.65f+.55f*Mathf.Sin(i*3.7f+1.5f))*envelope;
                particle.startColor=new Color(1,1,1,envelope*.92f);particle.remainingLifetime=100;particle.startLifetime=100;
                particles[count++]=particle;
            }
            LiveDroplets=count;if(!beads.isPlaying)beads.Play(false);beads.SetParticles(particles,count);
            block.SetFloat("_Bead",1);beads.GetComponent<ParticleSystemRenderer>().SetPropertyBlock(block);
        }
        public void Clear()
        {
            envelope=0;age=0;extracting=false;LiveDroplets=0;
            foreach(var line in lines)if(line!=null)line.enabled=false;
            if(beads!=null)beads.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        public void Dispose()
        {
            Clear();
            if(Application.isPlaying){if(material!=null)UnityEngine.Object.Destroy(material);if(root!=null)UnityEngine.Object.Destroy(root);}
            else{if(material!=null)UnityEngine.Object.DestroyImmediate(material);if(root!=null)UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
