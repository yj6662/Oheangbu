using System;
using System.Collections.Generic;
using UnityEngine;
using Oheangbu.Spellcraft;
namespace Oheangbu.App.SpellVFX120
{
 public sealed partial class Vfx120Effect
 {
  LineRenderer[] _riftLines;
  GameObject _areaHost;Transform[] _areaBodies;Renderer[] _areaRenderers;
  Vector3[] _areaFeet;ParticleSystem[] _areaParticles;Vfx120TraditionalMotif _areaSeal;
  Mesh _areaWater;Vector3[] _areaWaterVertices;float[] _areaGround;float _areaSim;
  MaterialPropertyBlock _areaBlock;Vector3 _areaForward,_areaRight,_areaStart;
  ParticleSystem.Particle[] _wideParticles=new ParticleSystem.Particle[256];
  public int AreaRaised {get;private set;}
  public float AreaWavePeak {get;private set;}
  public int AreaVisibleShots {get;private set;}
  public bool AreaConfigured=>_areaHost!=null;
  public int AreaParticles {get{int n=0;if(_areaParticles!=null)foreach(var p in _areaParticles)if(p!=null)n+=p.particleCount;return n;}}
  public float AreaTime=>Mathf.Max(0,Age+_startedAt-(ReceivedAreaPlan!=null?ReceivedAreaPlan.CreatedAt:_startedAt));
  public Vector3 AreaFront=>_areaStart+_areaForward*Mathf.Clamp((AreaTime-ReceivedAreaPlan.Delay)*ReceivedAreaPlan.Speed,0,ReceivedAreaPlan.Length);
  void BuildAreaRemake()
  {
   var plan=ReceivedAreaPlan;if(plan==null||Profile.NativeBodyPrefab==null)return;
   var k=Profile.AreaRemake;
   bool valid=k==Vfx120AreaRemake.BambooField?plan.Shape==AreaShape.Circle&&plan.Spikes.Count==17:
    k==Vfx120AreaRemake.FlameCone?plan.Shape==AreaShape.Cone&&plan.Length>0:
    k==Vfx120AreaRemake.NeedleVolley?plan.Shape==AreaShape.Volley&&plan.ShotCount>0:
    plan.Shape==AreaShape.Path&&plan.Radius>0&&plan.Length>0&&plan.Speed>0;
   if(!valid)return;
   _areaHost=new GameObject("AreaRemake_"+Profile.Glyph);_areaHost.transform.SetParent(transform,false);_areaBlock=new MaterialPropertyBlock();
   _areaForward=plan.Direction.sqrMagnitude>.001f?plan.Direction.normalized:transform.forward;_areaForward.y=0;_areaForward.Normalize();
   _areaRight=Vector3.Cross(Vector3.up,_areaForward);_areaStart=plan.Point;
   if(Profile.AreaFlowRevision&&k==Vfx120AreaRemake.FlameCone)Life=Mathf.Max(Life,plan.Delay+2.5f);
   if(k==Vfx120AreaRemake.BambooField)Life=Mathf.Max(Life,plan.Delay+.6f+.7f);
   if(k==Vfx120AreaRemake.NeedleVolley)Life=Mathf.Max(Life,plan.Delay+(plan.ShotCount-1)*plan.ShotInterval+plan.Length/Mathf.Max(1,plan.Speed)+.7f);
   int count=k==Vfx120AreaRemake.BambooField?17:k==Vfx120AreaRemake.NeedleVolley?plan.ShotCount:1;
   _areaBodies=new Transform[count];_areaRenderers=new Renderer[count];_areaFeet=new Vector3[count];
   for(int i=0;i<count;i++)
   {
    var body=Instantiate(Profile.NativeBodyPrefab,_areaHost.transform,false);body.name="AreaBody_"+i;_areaBodies[i]=body.transform;_areaRenderers[i]=body.GetComponentInChildren<MeshRenderer>();
    if(k==Vfx120AreaRemake.BambooField){var point=plan.Spikes[i].Point;point.y=GroundHeight(point,point.y);_areaFeet[i]=point;body.transform.localScale=new Vector3(.8f+i%3*.11f,.85f+i%5*.1f,.8f+i%3*.11f);body.transform.rotation=Quaternion.Euler(i%3*3,i*71,i%2*4-2);}
   }
   _areaParticles=_areaHost.GetComponentsInChildren<ParticleSystem>();
   for(int i=0;i<_areaParticles.Length;i++){var ps=_areaParticles[i];ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);ps.useAutoRandomSeed=false;ps.randomSeed=(uint)(271+i);var main=ps.main;main.playOnAwake=false;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;ps.Simulate(0,false,true,false);}
   if(k==Vfx120AreaRemake.FlameCone)
   {
    _areaBodies[0].SetPositionAndRotation(plan.Point,Quaternion.LookRotation(_areaForward));
    foreach(var ps in _areaParticles){var shape=ps.shape;shape.shapeType=Profile.AreaFlowRevision?ParticleSystemShapeType.Cone:ParticleSystemShapeType.ConeVolume;shape.angle=plan.Angle;shape.radius=.08f;shape.length=Profile.AreaFlowRevision?0:Mathf.Max(1,plan.Length-1.6f);shape.position=_areaBodies[0].InverseTransformPoint(ReceivedOrigin);shape.scale=Vector3.one;var m=ps.main;m.startSize=Profile.AreaFlowRevision?.12f:ps.name=="FlameCore"?1.3f:.85f;}
   }
   if(k==Vfx120AreaRemake.SandFront||k==Vfx120AreaRemake.WaterWave)
   {
    _areaGround=new float[17*9];for(int z=0;z<17;z++)for(int x=0;x<9;x++){var point=_areaStart+_areaForward*(plan.Length*z/16f)+_areaRight*((x/8f*2-1)*plan.Radius);_areaGround[z*9+x]=GroundHeight(point,point.y);}
   }
   if(k==Vfx120AreaRemake.WaterWave)
   {
    var filter=_areaBodies[0].GetComponent<MeshFilter>();_areaWater=Instantiate(filter.sharedMesh);_areaWater.MarkDynamic();filter.sharedMesh=_areaWater;_areaWaterVertices=_areaWater.vertices;
    _areaBodies[0].SetPositionAndRotation(Vector3.zero,Quaternion.identity);_areaBodies[0].localScale=Vector3.one;
    var camera=Camera.main;if(camera!=null){var data=camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();if(data!=null){data.requiresDepthTexture=true;data.requiresColorTexture=true;}}
   }
   if(Profile.EarthRift)_riftLines=_areaHost.GetComponentsInChildren<LineRenderer>();
   if(Profile.EarthRift&&Profile.ProceduralEarthRift)BuildProceduralRift();
   BuildAreaSeal();
  }
  void BuildAreaSeal()
  {
   if(Profile.NativeCastPrefab==null)return;
   var host=new GameObject("AreaCastPattern");host.transform.SetParent(_areaHost.transform,false);
   bool ground=Profile.AreaRemake!=Vfx120AreaRemake.FlameCone&&Profile.AreaRemake!=Vfx120AreaRemake.NeedleVolley;
   if(ground)
   {
    var point=ReceivedAreaPlan.Point;point.y=GroundHeight(point,point.y)+(Profile.WideAreaRevision?.08f:.025f);
    Vector3 normal=Vector3.up;if(Physics.Raycast(point+Vector3.up*2,Vector3.down,out var hit,4,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)&&hit.normal.y>.45f)normal=hit.normal;
    host.transform.SetPositionAndRotation(point,Quaternion.FromToRotation(Vector3.forward,normal));
    host.transform.localScale=Vector3.one*(Profile.AreaRemake==Vfx120AreaRemake.BambooField?ReceivedAreaPlan.Radius*.32f:ReceivedAreaPlan.Radius*.32f);
   }
   else{host.transform.SetPositionAndRotation(ReceivedOrigin,Quaternion.LookRotation(_areaForward));host.transform.localScale=Vector3.one*(Profile.AreaFlowRevision&&Profile.Glyph=="노"?.18f:Profile.WideAreaRevision&&Profile.Glyph=="노"?.32f:.264f);}
   _areaSeal=host.AddComponent<Vfx120TraditionalMotif>();var options=Vfx120TraditionalMotif.Settings.DefaultFor(Vfx120TraditionalMotif.Role.Cast);
   options.PreserveAuthored=true;options.PreviewControlled=true;options.HierarchyScaling=true;options.PatternFocus=true;options.Brightness=2.2f;options.Lifetime=Profile.AreaCastSeconds;
   _areaSeal.Configure(Profile.NativeCastPrefab,Profile.Pigment,Profile.Ink,Vfx120TraditionalMotif.Role.Cast,options);
  }
  // V1 rolling crest, widened with a softer shore transition; no bell silhouette.
  public static float ShoreWaveHeight(float across,float behind,float time)
  {
   float edge=Mathf.SmoothStep(0,1,Mathf.Clamp01((1-Mathf.Abs(across))/.35f));
   float envelope=Mathf.Pow(Mathf.Max(0,Mathf.Sin(Mathf.PI*Mathf.Clamp01(-behind/2.6f))),1.4f);
   return edge*envelope*(.95f+.2f*Mathf.Sin(time*Mathf.PI*2+across*2.5f));
  }
  float CurrentWaveHeight(float across,float behind,float time)=>Profile.AreaFlowRevision?ShoreWaveHeight(across,behind,time):Profile.WideAreaRevision?BellWaveHeight(across,behind,time):WaveHeight(across,behind,time);
  public static Vector3 FlameStreamPoint(Vector3 nozzle,Vector3 ground,Vector3 forward,Vector3 right,float length,float angle,float seed,float progress)
  {
   float spread=Mathf.Sin((seed+.17f)*31.7f)*angle*Mathf.Deg2Rad;
   var end=ground+(forward*Mathf.Cos(spread)+right*Mathf.Sin(spread))*length+Vector3.up*(.7f+seed*.7f);
   return Vector3.Lerp(nozzle,end,Mathf.Clamp01(progress));
  }
  public static float BellWaveHeight(float across,float behind,float time)
  {
   float sigma=.42f+.025f*Mathf.Sin(time*2*Mathf.PI),edge=Mathf.Exp(-.5f/(sigma*sigma));
   float bell=Mathf.Max(0,(Mathf.Exp(-.5f*across*across/(sigma*sigma))-edge)/(1-edge));
   float envelope=Mathf.Pow(Mathf.Max(0,Mathf.Sin(Mathf.PI*Mathf.Clamp01(-behind/2.6f))),1.4f);
   return bell*envelope*(2.1f+.15f*Mathf.Sin(time*2*Mathf.PI));
  }
  public static float WaveHeight(float across,float behind,float time)
  {
   float edge=Mathf.SmoothStep(0,1,Mathf.Clamp01((1-Mathf.Abs(across))/.18f));
   float envelope=Mathf.Sin(Mathf.PI*Mathf.Clamp01(-behind/2.6f));
   float crest=.95f+.2f*Mathf.Sin(time*Mathf.PI*2+across*2.5f);
   return Mathf.Pow(Mathf.Max(0,envelope),1.4f)*edge*crest;
  }
  float AreaGround(float along,float across)
  {
   float z=Mathf.Clamp01(along/ReceivedAreaPlan.Length)*16,x=Mathf.Clamp01((across+1)*.5f)*8;
   int zi=Mathf.Min(15,(int)z),xi=Mathf.Min(7,(int)x);
   return Mathf.Lerp(Mathf.Lerp(_areaGround[zi*9+xi],_areaGround[zi*9+xi+1],x-xi),Mathf.Lerp(_areaGround[(zi+1)*9+xi],_areaGround[(zi+1)*9+xi+1],x-xi),z-zi);
  }
  void SampleAreaRemake()
  {
   if(_areaHost==null)return;var p=ReceivedAreaPlan;float t=AreaTime;var kind=Profile.AreaRemake;
   float fade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Life-.3f,Life,Age));
   if(_areaSeal!=null)
   {
    float at=t-Mathf.Max(0,p.Delay-.16f);
    if(kind==Vfx120AreaRemake.NeedleVolley)
    {
     float last=p.Delay+(p.ShotCount-1)*p.ShotInterval;
     at=t<p.Delay?t-p.Delay+.08f:t<=last? .08f+Mathf.Repeat(t-p.Delay,Mathf.Max(.01f,p.ShotInterval))*.7f:.16f+(t-last);
    }
    _areaSeal.Sample(at);
   }
   if(kind==Vfx120AreaRemake.BambooField)
   {
    AreaRaised=0;for(int i=0;i<17;i++)
    {
     float rise=Mathf.SmoothStep(0,1,Mathf.InverseLerp(p.Spikes[i].RiseAt-.12f,p.Spikes[i].RiseAt,t));
     if(rise>=.999f)AreaRaised++;_areaBodies[i].position=_areaFeet[i]-Vector3.up*((1-rise)*2.1f);
     _areaRenderers[i].enabled=rise>0&&fade>0;_areaBlock.Clear();_areaBlock.SetFloat("_Visibility",fade);_areaBlock.SetFloat("_DissolveHeight",Mathf.Lerp(0,2.2f,fade));_areaBlock.SetFloat("_GroundY",_areaFeet[i].y);_areaRenderers[i].SetPropertyBlock(_areaBlock);
    }
   }
   else if(kind==Vfx120AreaRemake.NeedleVolley)
   {
    AreaVisibleShots=0;for(int i=0;i<_areaBodies.Length;i++)
    {
     float launch=p.Delay+i*p.ShotInterval,hit=launch+p.Length/Mathf.Max(1,p.Speed);Vector3 end=ReceivedOrigin+_areaForward*p.Length;
     if(i<p.Shots.Count){var shot=p.Shots[i];launch=shot.LaunchTime-p.CreatedAt;hit=shot.ImpactTime-p.CreatedAt;end=shot.HasImpactPoint?shot.ImpactPoint:shot.Target!=null?shot.Target.transform.position+Vector3.up*1.1f:end;}
     else end+=_areaRight*Mathf.Sin(i*2.4f)*p.Length*.12f;
     bool visible=t>=launch&&t<hit;_areaBodies[i].gameObject.SetActive(visible);if(!visible)continue;AreaVisibleShots++;
     _areaBodies[i].position=Vector3.Lerp(ReceivedOrigin,end,Mathf.Clamp01((t-launch)/Mathf.Max(.001f,hit-launch)));_areaBodies[i].rotation=Quaternion.LookRotation(end-ReceivedOrigin);
    }
   }
   else if(kind==Vfx120AreaRemake.FlameCone)
   {
    var body=_areaBodies[0];body.SetPositionAndRotation(p.Point,Quaternion.LookRotation(_areaForward));body.localScale=Vector3.one;
   }
   else
   {
    float along=Mathf.Clamp((t-p.Delay)*p.Speed,0,p.Length);
    if(kind==Vfx120AreaRemake.SandFront){var front=_areaStart+_areaForward*along;front.y=AreaGround(along,0)+.35f;_areaBodies[0].SetPositionAndRotation(front,Quaternion.LookRotation(_areaForward));_areaBodies[0].localScale=Profile.WideAreaRevision?Vector3.one:new Vector3(p.Radius*2,1,1);}
    else
    {
     AreaWavePeak=0;int index=0;
     for(int z=0;z<=16;z++)for(int x=0;x<=32;x++)
     {
      float across=x/16f-1,behind=-2.6f*z/16f;float h=CurrentWaveHeight(across,behind,t);AreaWavePeak=Mathf.Max(AreaWavePeak,h);
      var point=_areaStart+_areaForward*(along+behind)+_areaRight*(across*p.Radius);point.y=AreaGround(along+behind,across)+.014f+h*fade;
      _areaWaterVertices[index++]=point;
     }
     _areaWater.vertices=_areaWaterVertices;_areaWater.RecalculateNormals();_areaWater.RecalculateBounds();_areaRenderers[0].enabled=t>=p.Delay&&fade>0;
     _areaBlock.Clear();_areaBlock.SetFloat("_EffectTime",t);_areaRenderers[0].SetPropertyBlock(_areaBlock);
     var foam=_areaBodies[0].Find("Foam");if(foam!=null){var point=_areaStart+_areaForward*(along-1.3f);point.y=AreaGround(along,0)+WaveHeight(0,-1.3f,t);foam.position=point;foam.rotation=Quaternion.LookRotation(_areaForward);foam.localScale=Profile.WideAreaRevision?Vector3.one:new Vector3(p.Radius*2,1,1);}
    }
   }
   if(Profile.EarthRift)SampleEarthRift(t,fade);
   if(_areaParticles!=null&&_areaParticles.Length>0)
   {
    if(t<_areaSim){foreach(var ps in _areaParticles){ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);ps.Simulate(0,false,true,false);}_areaSim=0;}
    const float dt=1f/120;
    while(_areaSim+dt<=t+.00001f)
    {
     _areaSim+=dt;bool emit=kind==Vfx120AreaRemake.FlameCone?_areaSim>=Mathf.Max(0,p.Delay-.12f)&&_areaSim<p.Delay+(Profile.AreaFlowRevision?1.68f:Profile.WideAreaRevision?.65f:.18f):_areaSim>=p.Delay&&_areaSim<p.Delay+p.Length/Mathf.Max(.1f,p.Speed);
     foreach(var ps in _areaParticles){var em=ps.emission;em.enabled=emit;ps.Simulate(dt,false,false,false);}
    }
    if((Profile.AreaFlowRevision&&kind==Vfx120AreaRemake.FlameCone)||Profile.WideAreaRevision&&(kind==Vfx120AreaRemake.SandFront||kind==Vfx120AreaRemake.WaterWave))
    {
     float along=Mathf.Clamp((t-p.Delay)*p.Speed,0,p.Length);
     foreach(var ps in _areaParticles)
     {
      int count=ps.GetParticles(_wideParticles);
      for(int i=0;i<count;i++)
      {
       var particle=_wideParticles[i];float seed=(particle.randomSeed%10007)/10007f,life=1-particle.remainingLifetime/particle.startLifetime;
       if(kind==Vfx120AreaRemake.FlameCone)
       {
        particle.position=FlameStreamPoint(ReceivedOrigin,p.Point,_areaForward,_areaRight,p.Length,Profile.AreaReadabilityRevision?p.Angle*(seed<.85f?.22f:.65f):p.Angle,seed,life);
        particle.startSize=Profile.AreaReadabilityRevision?(ps.name=="FlameCore"?.09f:.05f)+Mathf.SmoothStep(0,1,life)*(ps.name=="FlameCore"?.62f:.25f):(ps.name=="FlameCore"?.12f:.07f)+Mathf.SmoothStep(0,1,life)*(ps.name=="FlameCore"?.8f:.5f);
       }
       else if(Profile.EarthRift)
       {
        float birth=t-life*particle.startLifetime;
        float station=Mathf.Clamp(Mathf.Floor(Mathf.Max(0,birth-p.Delay)*p.Speed/1.2f)*1.2f,0,p.Length);
        int lane=(int)(particle.randomSeed%7);
        if(Profile.BranchedEarthRift)lane=BranchParticleLane((int)(particle.randomSeed%4),station,p.Length);
        float x=(Profile.BranchedEarthRift?BranchAcross(lane,station,p.Length):RiftAcross(lane,station,p.Length))*p.Radius;
        float spread=(seed-.5f)*life*.65f;
        var point=_areaStart+_areaForward*(station+spread)+_areaRight*(x+spread);
        float lift=4*life*(1-life)*(ps.name=="Grains"?1.25f:.8f);
        point.y=AreaGround(station,x/p.Radius)+.025f+lift;
        if(_groundBranches!=null)
        {
         bool valid=ProceduralRiftParticle((int)(particle.randomSeed%4),station,out point);
         point.y+=lift;if(!valid)particle.remainingLifetime=0;
        }
        particle.position=point;
        particle.startSize=ps.name=="Grains"?.07f+seed*.05f:.22f+life*.55f;
       }
       else if(kind==Vfx120AreaRemake.SandFront)
       {
        float y=.1f+3.7f*life,angle=seed*Mathf.PI*2+t*8+y*2.5f,radius=.35f+y*.37f;
        float x=((int)(particle.randomSeed%3)-1)*p.Radius*.62f+Mathf.Cos(angle)*radius;
        var point=_areaStart+_areaForward*(along+Mathf.Sin(angle)*radius)+_areaRight*x;point.y=AreaGround(along,x/p.Radius)+y;
        particle.position=point;particle.rotation=angle*Mathf.Rad2Deg;particle.startSize=ps.name=="Grains"?.06f:.6f+life*.85f;
       }
       else
       {
        float across=seed*2-1,behind=-1.3f-life*.5f;
        if(Profile.AreaFlowRevision&&ps.name=="ShoreFoam")
        {
         int lane=(int)(particle.randomSeed%4);
         if(lane<2){across=(lane==0?-1:1)*(.88f+.12f*life);behind=-2.6f*seed;}
         else {behind=lane==2?-.04f-life*.12f:-2.56f+life*.12f;}
         particle.startSize=.18f+.22f*life;
        }
        var point=_areaStart+_areaForward*(along+behind)+_areaRight*(across*p.Radius);
        point.y=Profile.AreaFlowRevision?AreaGround(along+behind,across)+CurrentWaveHeight(across,behind,t)*fade+.04f:AreaGround(along,across)+BellWaveHeight(across,-1.3f,t)+.06f;particle.position=point;
       }
       _wideParticles[i]=particle;
      }
      ps.SetParticles(_wideParticles,count);
     }
    }
   }
  }
  void ClearAreaRemake()
  {
   ClearProceduralRift();
   if(_areaHost!=null){_areaHost.SetActive(false);if(Application.isPlaying)Destroy(_areaHost);else DestroyImmediate(_areaHost);}
   if(_areaWater!=null){if(Application.isPlaying)Destroy(_areaWater);else DestroyImmediate(_areaWater);}
   _riftLines=null;_areaHost=null;_areaWater=null;_areaParticles=null;_areaSeal=null;_areaSim=0;AreaRaised=AreaVisibleShots=0;
  }
 }
}
