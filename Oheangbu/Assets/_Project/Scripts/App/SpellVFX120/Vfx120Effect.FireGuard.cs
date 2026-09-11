using UnityEngine;
namespace Oheangbu.App.SpellVFX120
{
 public sealed partial class Vfx120Effect
 {
  GameObject _fireGuard; ParticleSystem[] _guardFire,_guardBurst; MeshRenderer _guardMark; MaterialPropertyBlock _guardMarkBlock; float _guardSim;
  static Vfx120Effect _activeFireGuard;
  public static bool IsFireGuard(Vfx120Profile p)=>p!=null&&p.Glyph=="너"&&p.NativeBodyPrefab!=null&&p.NativeBodyPrefab.name=="PF_FireGuard_031";
  public bool FireGuardConfigured=>_fireGuard!=null;
  public float FireGuardPulse{get;private set;}
  public int FireGuardParticles{get{int n=0;if(_fireGuard!=null)foreach(var p in _fireGuard.GetComponentsInChildren<ParticleSystem>())n+=p.particleCount;return n;}}
  public static void SelectFireGuard(Vfx120Effect e){_activeFireGuard=e!=null&&e.FireGuardConfigured&&!e.PreviewControlled?e:null;}
  public static bool TrySignalFireParry(Vector3 point,bool externalContact=false)
  {
   var e=_activeFireGuard;if(e==null||!e.isActiveAndEnabled||e.Age>=e.Life||Time.time-e._startedAt>=e.Life||!Vfx120InterceptionMotion.Finite(point))return false;
   e._externalGuardContact=externalContact;e.Signal(Cue.Parry);
   if(e.Profile.KtpPatternShield&&e.Profile.GuardContactPrefab==null)e.BurnIntercept(point);
   return true;
  }
  float FireGuardClock=>ParryAt>=0?ParryAt:PreviewControlled&&DemonstrationCues?.36f:-1;
  void BuildFireGuard()
  {
   if(!IsFireGuard(Profile))return;
   if(Profile.KtpPatternShield){_fireGuard=new GameObject("PatternShieldContactHost");_fireGuard.transform.SetParent(transform,false);return;}
   _fireGuard=Instantiate(Profile.NativeBodyPrefab,transform,false);
   Vector3 d=TargetPoint()-ReceivedOrigin;d.y=0;if(d.sqrMagnitude<.001f)d=Vector3.forward;
   _fireGuard.transform.SetPositionAndRotation(ReceivedOrigin,Quaternion.LookRotation(d));
   _guardFire=_fireGuard.transform.Find("Travel").GetComponentsInChildren<ParticleSystem>();
   _guardBurst=_fireGuard.transform.Find("Contact").GetComponentsInChildren<ParticleSystem>();
   _guardMark=WardRenderer("KTP_FireGuard_Mark",_fireGuard.transform,QuadMesh.Value,Profile.PatternMaterial);
   _guardMark.transform.localPosition=new Vector3(0,0,.7f);_guardMark.transform.localScale=new Vector3(1.8f,1.8f,1);
   _guardMarkBlock=new MaterialPropertyBlock();ResetFireGuard();
  }
  void ResetFireGuard(){_guardSim=0;foreach(var p in _fireGuard.GetComponentsInChildren<ParticleSystem>()){p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);p.Simulate(0,false,true,false);}}
  void SampleFireGuard()
  {
   if(_fireGuard==null)return;if(Age<_guardSim)ResetFireGuard();float hit=FireGuardClock;
   if(Profile.KtpPatternShield){FireGuardPulse=hit>=0&&Age>=hit&&Age<Life?Mathf.Exp(-9*(Age-hit)):0;return;}
   const float dt=1f/60f;while(_guardSim+dt<=Mathf.Min(Age,Life)+.00001f)
   {
    float at=_guardSim+dt;
    foreach(var p in _guardFire){var em=p.emission;em.enabled=at<Life-.55f;p.Simulate(dt,false,false,false);}
    if(!Profile.UseOriginalKtp&&hit>=0&&at>=hit)foreach(var p in _guardBurst)p.Simulate(Mathf.Min(dt,at-hit),false,false,false);
    _guardSim=at;
   }
   FireGuardPulse=hit>=0&&Age>=hit?Mathf.Exp(-9*(Age-hit)):0;
   float fade=Mathf.SmoothStep(0,1,Age/.12f)*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Life-.45f,Life,Age)));
   _guardMark.enabled=fade>.001f;_guardMarkBlock.SetFloat("_Alpha",fade*(.55f+.45f*FireGuardPulse));_guardMarkBlock.SetFloat("_Erode",0);
   _guardMarkBlock.SetColor("_BaseColor",new Color(1.5f,.52f+.4f*FireGuardPulse,.12f));_guardMark.SetPropertyBlock(_guardMarkBlock);
   if(Age>=Life){foreach(var p in _fireGuard.GetComponentsInChildren<ParticleSystem>())p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);FireGuardPulse=0;}
  }
  void BurnIntercept(Vector3 point)
  {
   var source=Profile.NativeBodyPrefab.transform.Find("Contact");if(source==null)return;
   var burn=Instantiate(source.gameObject,_fireGuard.transform,false);burn.name="ConfirmedInterceptBurn";
   burn.transform.SetPositionAndRotation(point,Quaternion.identity);burn.transform.localScale=Vector3.one*.45f;
   foreach(var ps in burn.GetComponentsInChildren<ParticleSystem>(true))
   {
    ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
    var main=ps.main;main.loop=false;main.startDelay=0;main.duration=.2f;main.startLifetime=.55f;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
    ps.Play(false);
   }
   Destroy(burn,.8f);
  }
  void ClearFireGuard(){if(_activeFireGuard==this)_activeFireGuard=null;if(_fireGuard!=null){_fireGuard.SetActive(false);if(Application.isPlaying)Destroy(_fireGuard);else DestroyImmediate(_fireGuard);}_fireGuard=null;_guardMark=null;_guardFire=null;_guardBurst=null;FireGuardPulse=0;}
 }
}
