using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace Oheangbu.App
{
    // D306 #10 chunk pull presentation: latch -> tear -> snap -> fly -> absorb, or cancel (tendrils snap back, nothing flies).
    // Event driven by HarvestAction.PullStarted / ChunkExtracted / PullCanceled through HarvestInkStreamEffect; it never judges.
    // Fixed-size pooled resources (3 tendrils, 1 blob, a droplet pool, 2 stains); nothing is allocated per frame.
    // Matte ink only (HarvestInkFlow / InkPigment alpha blend): no emission, no bloom (ART-INK). Widths are clamped by camera
    // depth so the pull never covers the near view in first person and still reads at 12 m.
    public sealed class HarvestInkFlowRenderer : IDisposable
    {
        private const int Tendrils = 3, AlphaKeys = 8;
        private static readonly float[] TendrilScale = { 1f, .78f, .62f };
        private struct Drop { public Vector3 Position, Velocity; public float Age, Life, Size; }
        private readonly HarvestInkFlowProfileSO profile;
        private readonly GameObject root;
        private readonly LineRenderer[] lines = new LineRenderer[Tendrils];
        private readonly Vector3[] points;
        private readonly Material material, stainMaterial;
        private readonly bool pigmentStain;
        private readonly ParticleSystem blob, drops, stains;
        private readonly ParticleSystemRenderer blobRenderer, dropRenderer, stainRenderer;
        private readonly ParticleSystem.Particle[] blobParticles = new ParticleSystem.Particle[1];
        private readonly ParticleSystem.Particle[] stainParticles = new ParticleSystem.Particle[2];
        private readonly ParticleSystem.Particle[] dropParticles;
        private readonly Drop[] pool;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private readonly Gradient gradient = new Gradient();
        private readonly GradientColorKey[] colorKeys = { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) };
        private readonly GradientAlphaKey[] alphaKeys = new GradientAlphaKey[AlphaKeys];
        private Transform body;
        private Vector3 bodyLocal, sourceFallback, flyFrom, flyPrevious;
        private float pullAge = -1f, retractAge = -1f, retractFrom, flyAge = -1f, absorbAge = -1f;
        private float stainAge = -1f, stainFade = -1f, splatAge = -1f, blobAtSnap, clock, stainSpin, splatSpin;
        private int live, spattered, trailed, seed;
        private bool blobShown, dropsShown, stainsShown, linesShown;
        private static readonly int TintId = Shader.PropertyToID("_Tint"), FlowId = Shader.PropertyToID("_FlowTime"), BeadId = Shader.PropertyToID("_Bead");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), AlphaId = Shader.PropertyToID("_Alpha"), SoftId = Shader.PropertyToID("_Soft"), ErodeId = Shader.PropertyToID("_Erode");
        public bool Visible { get; private set; }
        public Vector3 LastSink { get; private set; }
        public int StrandCount => lines.Length;
        public int LiveDroplets => live;
        public bool Flying => flyAge >= 0f;
        public bool Pulling => pullAge >= 0f;
        // QA/readout only: Idle, Latch, Tear, Fly, Absorb, Recoil
        public string Phase { get; private set; } = "Idle";
        public float BlobDiameter { get; private set; }
        // the torn chunk reached the brush tip (once per snap; never on cancel)
        public event Action Absorbed;

        public HarvestInkFlowRenderer(Transform parent, HarvestInkFlowProfileSO settings)
        {
            profile=settings;
            var old=parent.Find("Harvest_InkFlow_Owned");if(old!=null){old.gameObject.SetActive(false);if(Application.isPlaying)UnityEngine.Object.Destroy(old.gameObject);else UnityEngine.Object.DestroyImmediate(old.gameObject);}
            root=new GameObject("Harvest_InkFlow_Owned"); root.transform.SetParent(parent,false);
            material=new Material(settings.Material) {name="HarvestInkFlow_Runtime"};
            pigmentStain=settings.StainMaterial!=null;
            stainMaterial=pigmentStain?new Material(settings.StainMaterial){name="HarvestInkStain_Runtime"}:material;
            points=new Vector3[Mathf.Clamp(settings.Points,12,48)];
            for(int i=0;i<lines.Length;i++)
            {
                var go=new GameObject("InkTendril_"+i);go.transform.SetParent(root.transform,false);
                var line=go.AddComponent<LineRenderer>(); lines[i]=line;line.sharedMaterial=material;
                line.useWorldSpace=true;line.positionCount=points.Length;line.textureMode=LineTextureMode.Stretch;
                line.alignment=LineAlignment.View;line.numCapVertices=3;line.numCornerVertices=2;
                line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;
            }
            blob=System("InkChunk",material,ParticleSystemRenderMode.Stretch,1,out blobRenderer);
            blobRenderer.lengthScale=1f;blobRenderer.velocityScale=settings.FlyStretch;
            pool=new Drop[Mathf.Clamp(settings.Droplets,4,20)];dropParticles=new ParticleSystem.Particle[pool.Length+1];
            drops=System("InkDroplets",material,ParticleSystemRenderMode.Billboard,dropParticles.Length,out dropRenderer);
            stains=System("InkStains",stainMaterial,ParticleSystemRenderMode.Billboard,stainParticles.Length,out stainRenderer);
        }
        private ParticleSystem System(string name,Material mat,ParticleSystemRenderMode mode,int max,out ParticleSystemRenderer renderer)
        {
            var go=new GameObject(name);go.transform.SetParent(root.transform,false);
            var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.playOnAwake=false;main.loop=false;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.maxParticles=max;main.startSpeed=0;main.startLifetime=100;main.useUnscaledTime=false;main.simulationSpeed=0f; // fully script-driven
            var emission=ps.emission;emission.enabled=false;var shape=ps.shape;shape.enabled=false;
            renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=mat;renderer.renderMode=mode;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            return ps;
        }
        public static Vector3 Curve(Vector3 source,Vector3 sink,Vector3 approach,float t,float sag)
        {
            t=Mathf.Clamp01(t);var delta=sink-source;
            var a=source+delta*.32f+Vector3.down*sag;
            var b=sink+approach.normalized*Mathf.Min(.24f,delta.magnitude*.16f);
            float u=1-t;return u*u*u*source+3*u*u*t*a+3*u*t*t*b+t*t*t*sink;
        }

        // PullStarted: tendrils leave the brush tip for the chest; the stain follows the body transform
        public void Begin(Transform target,Vector3 source)
        {
            body=target;sourceFallback=source;bodyLocal=target!=null?target.InverseTransformPoint(source):Vector3.zero;
            pullAge=0f;retractAge=-1f;stainAge=-1f;stainFade=-1f;spattered=0;seed++;
            stainSpin=Hash01(seed*31+3)*360f;
        }
        // ChunkExtracted: the blob tears off and flies; tendrils rewind through the dry-brush mask; ink splats on the body
        public void Snap(Vector3 source)
        {
            if(pullAge<0f&&body==null)sourceFallback=source;
            Vector3 src=Source(source);float reach=pullAge<0f?1f:Smooth01(pullAge/Mathf.Max(.01f,profile.LatchSeconds));
            float size=pullAge<0f?profile.BlobSize:BlobSizeAt(pullAge);
            flyFrom=BlobRoot(src,LastSink,size);flyPrevious=flyFrom;flyAge=0f;trailed=0;blobAtSnap=size;absorbAge=-1f;
            retractFrom=reach;retractAge=0f;pullAge=-1f;
            if(stainAge<0f)stainAge=0f;stainFade=0f;splatAge=0f;splatSpin=Hash01(seed*17+11)*360f;
            for(int i=0;i<5;i++)
            {
                Vector3 dir=new Vector3(Hash01(seed*7+i*3)*2-1,.35f+Hash01(seed*5+i*11),Hash01(seed*13+i*5)*2-1).normalized;
                Emit(src,dir*(1.4f+Hash01(seed+i*19)),.42f,profile.DropletSize*(1.2f+Hash01(seed*3+i)*.8f));
            }
            SpawnSplatContact(src);
        }
        // PullCanceled: tendrils snap back, a grown blob drops, nothing flies and nothing is absorbed
        public void Cancel()
        {
            if(pullAge<0f)return;
            Vector3 src=Source(sourceFallback);
            retractFrom=Smooth01(pullAge/Mathf.Max(.01f,profile.LatchSeconds));retractAge=0f;
            if(pullAge>=profile.LatchSeconds*.8f)Emit(BlobRoot(src,LastSink,BlobSizeAt(pullAge)),Vector3.down*.4f,.45f,BlobSizeAt(pullAge)*.6f);
            pullAge=-1f;if(stainAge>=0f)stainFade=0f;
        }

        public void Tick(Vector3 source,Vector3 sink,Vector3 approach,Camera cam,float delta)
        {
            if(delta<=0f)return; // frozen with the world (pause, tutorial stop); Clear() hides it
            clock+=delta;LastSink=sink;
            if(body!=null&&pullAge>=0f)bodyLocal=body.InverseTransformPoint(source); // live chest point while pulling
            Vector3 src=Source(source);
            if(pullAge>=0f){pullAge+=delta;if(stainAge<0f&&pullAge>=profile.LatchSeconds)stainAge=0f;}
            if(retractAge>=0f){retractAge+=delta;if(retractAge>=profile.RecoilSeconds)retractAge=-1f;}
            if(flyAge>=0f){flyAge+=delta;if(flyAge>=profile.FlySeconds){flyAge=-1f;absorbAge=0f;Absorbed?.Invoke();}}
            else if(absorbAge>=0f){absorbAge+=delta;if(absorbAge>=profile.AbsorbSeconds)absorbAge=-1f;}
            if(stainAge>=0f)stainAge+=delta;
            if(stainFade>=0f){stainFade+=delta;if(stainFade>=profile.StainSeconds)stainAge=stainFade=-1f;}
            if(splatAge>=0f){splatAge+=delta;if(splatAge>=profile.StainSeconds)splatAge=-1f;}
            bool tendrils=DrawTendrils(src,sink,approach,cam);
            bool chunk=DrawBlob(src,sink,approach,cam,delta);
            bool dropsVisible=DrawDrops(sink,cam,delta);
            bool stainVisible=DrawStains(src,cam);
            Visible=tendrils||chunk||dropsVisible||stainVisible;
            Phase=pullAge>=0f?(pullAge<profile.LatchSeconds?"Latch":"Tear"):flyAge>=0f?"Fly":absorbAge>=0f?"Absorb":retractAge>=0f?"Recoil":"Idle";
        }

        private bool DrawTendrils(Vector3 src,Vector3 sink,Vector3 approach,Camera cam)
        {
            float reach,tear01=0f,alpha=1f,dry=0f;
            if(pullAge>=0f)
            {
                reach=Smooth01(pullAge/Mathf.Max(.01f,profile.LatchSeconds));
                tear01=Mathf.Clamp01((pullAge-profile.LatchSeconds)/Mathf.Max(.01f,profile.TearSeconds-profile.LatchSeconds));
            }
            else if(retractAge>=0f)
            {
                float r=Mathf.Clamp01(retractAge/profile.RecoilSeconds);
                reach=retractFrom*(1f-r*r);dry=r;alpha=1f-r*r;
            }
            else reach=0f;
            if(reach<=.01f||alpha<=.01f){if(linesShown)foreach(var line in lines)line.enabled=false;linesShown=false;return false;}
            linesShown=true;
            Vector3 axis=sink-src;if(axis.sqrMagnitude<1e-6f)axis=Vector3.forward;axis.Normalize();
            Vector3 side=Vector3.Cross(axis,Vector3.up);if(side.sqrMagnitude<.01f)side=Vector3.right;side.Normalize();
            Vector3 up=Vector3.Cross(side,axis);
            float sag=profile.Sag*(1f-.85f*tear01);
            block.SetColor(TintId,profile.InkColor);block.SetFloat(FlowId,clock*.8f);block.SetFloat(BeadId,0f);
            float twoPi=Mathf.PI*2f;
            for(int j=0;j<lines.Length;j++)
            {
                float angle=j*twoPi/Tendrils+.4f;
                Vector3 dir=side*Mathf.Cos(angle)+up*Mathf.Sin(angle),perp=side*Mathf.Cos(angle+1.571f)+up*Mathf.Sin(angle+1.571f);
                for(int i=0;i<points.Length;i++)
                {
                    float u=i/(float)(points.Length-1),t=1f-u*reach; // index 0 at the brush tip, the head toward the body
                    Vector3 p=Curve(src,sink,approach,t,sag);
                    float ends=Mathf.Sin(t*Mathf.PI),splay=ends*.75f+(1f-t)*.25f; // hooks splay on the body, meet at the brush
                    p+=dir*profile.TendrilSpread*splay*(1f-.5f*tear01);
                    p+=dir*.03f*(1f-reach)*Mathf.Sin(t*9f-clock*14f+j);                                   // latch writhe
                    p+=perp*profile.TremorAmplitude*tear01*ends*Mathf.Sin(clock*profile.TremorHz*twoPi+t*17f+j*2.1f); // taut tremble
                    points[i]=p;
                }
                var line=lines[j];line.enabled=true;line.SetPositions(points);
                float scale=TendrilScale[j]*(1f-.5f*dry);
                line.startWidth=ClampWidth(profile.TendrilTipWidth*scale,ViewHeight(cam,points[0]),profile.MinScreenWidth01,profile.MaxScreenWidth01);
                line.endWidth=ClampWidth(Mathf.Lerp(profile.TendrilTipWidth,profile.TendrilRootWidth,reach)*scale,ViewHeight(cam,points[points.Length-1]),profile.MinScreenWidth01,profile.MaxScreenWidth01);
                for(int k=0;k<AlphaKeys;k++)
                {
                    float mask=Mathf.Clamp01((Hash01(k*7+j*13+seed*29)+.2f-dry*1.2f)*5f); // dry-brush gaps widen as it rewinds
                    alphaKeys[k]=new GradientAlphaKey(alpha*mask,k/(float)(AlphaKeys-1));
                }
                gradient.SetKeys(colorKeys,alphaKeys);line.colorGradient=gradient;line.SetPropertyBlock(block);
            }
            return true;
        }

        private bool DrawBlob(Vector3 src,Vector3 sink,Vector3 approach,Camera cam,float delta)
        {
            Vector3 position,velocity;float size;
            if(pullAge>=0f&&pullAge>=profile.LatchSeconds*.8f)
            {
                size=BlobSizeAt(pullAge)*(1f+.06f*Mathf.Sin(clock*21f));position=BlobRoot(src,sink,size);
                velocity=(sink-src).normalized*.001f; // round while it grows
            }
            else if(flyAge>=0f)
            {
                float u=Mathf.Clamp01(flyAge/profile.FlySeconds),e=u*u*(3f-2f*u);
                position=Curve(flyFrom,sink,approach,e,profile.Sag*.5f);
                velocity=(position-flyPrevious)/Mathf.Max(1e-4f,delta);flyPrevious=position;
                size=Mathf.Lerp(blobAtSnap,profile.TipWidth*3f,e);
                while(trailed<profile.FlyDroplets&&e>=(trailed+1f)/(profile.FlyDroplets+1f))
                {
                    Vector3 jitter=new Vector3(Hash01(seed*3+trailed*7)-.5f,Hash01(seed*11+trailed*5)*.5f,Hash01(seed*23+trailed*3)-.5f);
                    Emit(position,velocity*.12f+jitter,.45f,profile.DropletSize*(.8f+Hash01(seed+trailed*13)*.6f));trailed++;
                }
                float speed=velocity.magnitude,cap=profile.FlyStretch>0f?size*2f/profile.FlyStretch:0f; // stretch <= 3x the diameter
                if(speed>cap&&speed>0f)velocity*=cap/speed;
                if(velocity.sqrMagnitude<1e-8f)velocity=(sink-flyFrom).normalized*.001f;
            }
            else{BlobDiameter=0f;if(blobShown)blob.Clear();blobShown=false;return false;}
            blobShown=true;
            float view=ViewHeight(cam,position);if(view>0f)size=Mathf.Min(size,view*profile.MaxScreenBlob01);
            BlobDiameter=size;
            var p=new ParticleSystem.Particle{position=position,velocity=velocity,startSize=size,startColor=new Color(1,1,1,.96f),remainingLifetime=100,startLifetime=100};
            blobParticles[0]=p;
            if(!blob.isPlaying)blob.Play(false);blob.SetParticles(blobParticles,1);
            block.SetColor(TintId,profile.InkColor);block.SetFloat(BeadId,1f);blobRenderer.SetPropertyBlock(block);
            return true;
        }

        private bool DrawDrops(Vector3 sink,Camera cam,float delta)
        {
            if(pullAge>=0f&&pullAge>=profile.LatchSeconds)
            {
                float tear01=Mathf.Clamp01((pullAge-profile.LatchSeconds)/Mathf.Max(.01f,profile.TearSeconds-profile.LatchSeconds));
                Vector3 src=Source(sourceFallback),axis=(sink-src).normalized,side=Vector3.Cross(axis,Vector3.up).normalized;
                int target=Mathf.FloorToInt(tear01*profile.SpatterDroplets);
                while(spattered<target)
                {
                    int n=seed*41+spattered*9;
                    Vector3 dir=(side*(Hash01(n)*2f-1f)+Vector3.up*(.4f+Hash01(n+1))+axis*(Hash01(n+2)-.3f)).normalized;
                    Emit(BlobRoot(src,sink,BlobSizeAt(pullAge)),dir*(1.2f+Hash01(n+3)),.4f,profile.DropletSize*(1f+Hash01(n+4)*.6f));spattered++;
                }
            }
            int count=0;
            for(int i=live-1;i>=0;i--)
            {
                var d=pool[i];d.Age+=delta;
                if(d.Age>=d.Life){pool[i]=pool[--live];continue;}
                d.Velocity+=Physics.gravity*delta;d.Position+=d.Velocity*delta;pool[i]=d;
                float fade=1f-(d.Age/d.Life)*(d.Age/d.Life);
                dropParticles[count++]=new ParticleSystem.Particle{position=d.Position,startSize=d.Size,startColor=new Color(1,1,1,.92f*fade),remainingLifetime=100,startLifetime=100};
            }
            if(absorbAge>=0f)
            {
                float a=Mathf.Clamp01(absorbAge/profile.AbsorbSeconds),size=profile.TipSwell*Mathf.Sin(a*Mathf.PI);
                float view=ViewHeight(cam,sink);if(view>0f)size=Mathf.Min(size,view*profile.MaxScreenBlob01);
                dropParticles[count++]=new ParticleSystem.Particle{position=sink,startSize=size,startColor=new Color(1,1,1,.95f),remainingLifetime=100,startLifetime=100};
            }
            if(count==0){if(dropsShown)drops.Clear();dropsShown=false;return false;}
            dropsShown=true;
            if(!drops.isPlaying)drops.Play(false);drops.SetParticles(dropParticles,count);
            block.SetColor(TintId,profile.InkColor);block.SetFloat(BeadId,1f);dropRenderer.SetPropertyBlock(block);
            return true;
        }

        private bool DrawStains(Vector3 src,Camera cam)
        {
            Vector3 toCamera=cam!=null?(cam.transform.position-src).normalized:(LastSink-src).normalized;
            Vector3 surface=src+toCamera*profile.SurfaceOffset;
            float view=ViewHeight(cam,surface),cap=view>0f?view*profile.MaxScreenStain01:float.MaxValue; // screen clamp like the tendrils
            int count=0;float dried=0f;
            if(stainAge>=0f)
            {
                float fade=stainFade>=0f?Mathf.Clamp01(stainFade/profile.StainSeconds):0f;dried=Mathf.Max(dried,fade);
                stainParticles[count++]=new ParticleSystem.Particle{position=surface,startSize=Mathf.Min(cap,profile.StainSize*Smooth01(stainAge/.08f)),rotation=stainSpin,
                    startColor=new Color(1,1,1,.85f*(1f-fade)),remainingLifetime=100,startLifetime=100};
            }
            if(splatAge>=0f)
            {
                float a=Mathf.Clamp01(splatAge/profile.StainSeconds);dried=Mathf.Max(dried,a);
                stainParticles[count++]=new ParticleSystem.Particle{position=surface+toCamera*.02f,startSize=Mathf.Min(cap,profile.SplatSize*(.55f+.45f*Smooth01(splatAge/.1f))),rotation=splatSpin,
                    startColor=new Color(1,1,1,.9f*(1f-Mathf.Pow(a,1.5f))),remainingLifetime=100,startLifetime=100};
            }
            if(count==0){if(stainsShown)stains.Clear();stainsShown=false;return false;}
            stainsShown=true;
            if(!stains.isPlaying)stains.Play(false);stains.SetParticles(stainParticles,count);
            if(pigmentStain)
            {
                var ink=profile.InkColor;ink.a=1f;
                block.SetColor(BaseColorId,ink);block.SetFloat(AlphaId,1f);block.SetFloat(SoftId,1f);block.SetFloat(ErodeId,.1f+.7f*dried); // dry-brush drying
            }
            else{block.SetColor(TintId,profile.InkColor);block.SetFloat(BeadId,1f);}
            stainRenderer.SetPropertyBlock(block);
            return true;
        }

        // optional vendor contact splat, retinted to matte ink (event-time spawn, not per frame)
        private void SpawnSplatContact(Vector3 point)
        {
            if(profile.SplatContact==null||!Application.isPlaying)return;
            var cam=Camera.main;var rotation=cam!=null?cam.transform.rotation:Quaternion.identity;
            var effect=SpellVFX120.KtpContactEffect.Spawn(profile.SplatContact,point,rotation,profile.SplatContactScale,root.scene);
            if(effect==null||effect.Content==null)return;
            var ink=profile.InkColor;var tint=new MaterialPropertyBlock();
            foreach(var renderer in effect.Content.GetComponentsInChildren<Renderer>(true))
            {
                var mats=renderer.sharedMaterials;
                for(int i=0;i<mats.Length;i++)
                {
                    if(mats[i]==null)continue;renderer.GetPropertyBlock(tint,i);
                    if(mats[i].HasProperty(BaseColorId))tint.SetColor(BaseColorId,ink);
                    if(mats[i].HasProperty("_Color"))tint.SetColor("_Color",ink);
                    if(mats[i].HasProperty("_TintColor"))tint.SetColor("_TintColor",ink);
                    renderer.SetPropertyBlock(tint,i);
                }
            }
        }

        private Vector3 Source(Vector3 fallback)
        {
            if(body!=null)return body.TransformPoint(bodyLocal);
            return fallback;
        }
        private float BlobSizeAt(float age)
        {
            float tear01=Mathf.Clamp01((age-profile.LatchSeconds)/Mathf.Max(.01f,profile.TearSeconds-profile.LatchSeconds));
            return Mathf.Lerp(profile.BlobStartSize,profile.BlobSize,1f-(1f-tear01)*(1f-tear01));
        }
        private static Vector3 BlobRoot(Vector3 src,Vector3 sink,float size)
        { Vector3 d=sink-src;return d.sqrMagnitude>1e-6f?src+d.normalized*(size*.3f):src; }
        private void Emit(Vector3 position,Vector3 velocity,float life,float size)
        {
            if(pool.Length==0)return;
            int i=live<pool.Length?live++:0;
            if(i==0&&live==pool.Length)for(int k=1;k<pool.Length;k++)if(pool[k].Age/pool[k].Life>pool[i].Age/pool[i].Life)i=k; // pool full: overwrite the most spent drop
            pool[i]=new Drop{Position=position,Velocity=velocity,Age=0f,Life=life,Size=size};
        }
        private static float ViewHeight(Camera cam,Vector3 point)
        {
            if(cam==null)return 0f;
            if(cam.orthographic)return cam.orthographicSize*2f;
            float depth=Mathf.Max(cam.nearClipPlane,Vector3.Dot(point-cam.transform.position,cam.transform.forward));
            return 2f*depth*Mathf.Tan(cam.fieldOfView*.5f*Mathf.Deg2Rad);
        }
        private static float ClampWidth(float width,float view,float min01,float max01)
            => view<=0f?width:Mathf.Clamp(width,view*min01,Mathf.Max(view*min01,view*max01));
        private static float Smooth01(float x){x=Mathf.Clamp01(x);return x*x*(3f-2f*x);}
        private static float Hash01(int n){unchecked{n=(n<<13)^n;return ((n*(n*n*15731+789221)+1376312589)&0x7fffffff)/2147483647f;}}

        public void Clear()
        {
            pullAge=retractAge=flyAge=absorbAge=stainAge=stainFade=splatAge=-1f;live=0;Visible=false;blobShown=dropsShown=stainsShown=linesShown=false;Phase="Idle";BlobDiameter=0f;body=null;
            foreach(var line in lines)if(line!=null)line.enabled=false;
            if(blob!=null)blob.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(drops!=null)drops.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(stains!=null)stains.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        public void Dispose()
        {
            Clear();
            bool play=Application.isPlaying;
            if(material!=null){if(play)UnityEngine.Object.Destroy(material);else UnityEngine.Object.DestroyImmediate(material);}
            if(pigmentStain&&stainMaterial!=null){if(play)UnityEngine.Object.Destroy(stainMaterial);else UnityEngine.Object.DestroyImmediate(stainMaterial);}
            if(root!=null){if(play)UnityEngine.Object.Destroy(root);else UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
