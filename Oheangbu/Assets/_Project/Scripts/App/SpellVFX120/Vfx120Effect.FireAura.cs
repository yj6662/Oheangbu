using UnityEngine;
namespace Oheangbu.App.SpellVFX120
{
 public sealed partial class Vfx120Effect
 {
  GameObject _fireAura;ParticleSystem[] _auraFire,_auraHit;MeshRenderer _auraSeal,_auraMark;MaterialPropertyBlock _auraBlock;float _auraSim,_auraHitAt=-1;int _auraDemo;
  public static bool IsFireEmpower(Vfx120Profile p)=>p!=null&&p.Glyph=="넌"&&p.NativeBodyPrefab!=null&&p.NativeBodyPrefab.name=="PF_FireEmpower_033";
  public static bool IsFireResolve(Vfx120Profile p)=>p!=null&&p.Glyph=="넘"&&p.NativeBodyPrefab!=null&&p.NativeBodyPrefab.name=="PF_FireResolve_034";
  public static bool IsFireSword(Vfx120Profile p)=>p!=null&&p.Glyph=="넛"&&p.NativeBodyPrefab!=null&&p.NativeBodyPrefab.name=="PF_FireSword_035";
  public static bool IsFireHealing(Vfx120Profile p)=>p!=null&&p.Glyph=="녹"&&p.NativeBodyPrefab!=null&&p.NativeBodyPrefab.name=="PF_FireHealing_038";
  public static bool IsFireBackblast(Vfx120Profile p)=>p!=null&&p.Glyph=="논"&&p.NativeBodyPrefab!=null&&p.NativeBodyPrefab.name=="PF_FireBackblast_039";
  public static bool IsFireJet(Vfx120Profile p)=>IsFireBackblast(p)||IsFireHealing(p)||p!=null&&p.Glyph=="노"&&p.NativeBodyPrefab!=null&&p.NativeBodyPrefab.name=="PF_FireJet_037";
  public bool EndFireBackblast(Vector3 point){if(!IsFireBackblast(Profile)||!FireAuraConfigured||_empowerConsumedAt>=0||Age>=Life||!Vfx120InterceptionMotion.Finite(point))return false;_empowerConsumedAt=Age;return SignalFireAuraHit(point);}
  public bool FireBackblastEnded=>IsFireBackblast(Profile)&&_empowerConsumedAt>=0;
  MeshRenderer _healingMark;Vector3 _healingGround;float _healingAt=-1;
  public float FireHealingOpacity{get;private set;}
  public bool ConfirmFireHealingHit(Transform confirmedTarget,Vector3 contact,Vector3 ground)
  {if(!IsFireHealing(Profile)||!FireAuraConfigured||_healingAt>=0||confirmedTarget==null||!confirmedTarget.gameObject.activeInHierarchy||!Vfx120InterceptionMotion.Finite(ground)||!Vfx120InterceptionMotion.Finite(contact)||Age>=Life)return false;_healingGround=ground;_healingAt=Age;return SignalFireAuraHit(contact);}
  public static bool IsFireSummon(Vfx120Profile p)=>p!=null&&p.Glyph=="놈"&&p.NativeBodyPrefab!=null&&p.NativeBodyPrefab.name=="PF_FireSummon_040";
  public static bool IsFireMelt(Vfx120Profile p)=>p!=null&&p.Glyph=="놋"&&p.NativeBodyPrefab!=null&&p.NativeBodyPrefab.name=="PF_FireMelt_041";
  public static bool IsFireSteam(Vfx120Profile p)=>p!=null&&p.Glyph=="농"&&p.NativeBodyPrefab!=null&&p.NativeBodyPrefab.name=="PF_FireSteam_042";
  public static bool IsFireWard(Vfx120Profile p)=>p!=null&&p.Glyph=="누"&&p.NativeBodyPrefab!=null&&p.NativeBodyPrefab.name=="PF_FireWard_043";
  public static bool IsFireBurn(Vfx120Profile p)=>p!=null&&p.Glyph=="눈"&&p.NativeBodyPrefab!=null&&p.NativeBodyPrefab.name=="PF_FireBurn_045";
  Vector3 _burnFrom,_burnTo;float _burnAt=-1;
  public float FireBurnProgress=>_burnAt<0?0:Mathf.Clamp01((Age-_burnAt)/1.8f);
  public bool BeginFireBurnPath(Vector3 from,Vector3 to){if(!IsFireBurn(Profile)||!FireAuraConfigured||_burnAt>=0||Age>Life-2.4f||!Vfx120InterceptionMotion.Finite(from)||!Vfx120InterceptionMotion.Finite(to)||(to-from).sqrMagnitude<.0001f)return false;_burnFrom=from;_burnTo=to;_burnAt=Age;return true;}
  public static bool IsFireAura(Vfx120Profile p)=>IsFireBurn(p)||IsFireWard(p)||IsFireSteam(p)||IsFireMelt(p)||IsFireSummon(p)||IsFireJet(p)||IsFireSword(p)||IsFireResolve(p)||IsFireEmpower(p)||p!=null&&p.Glyph=="넉"&&p.NativeBodyPrefab!=null&&p.NativeBodyPrefab.name=="PF_FireAura_032";
  public int FireSteamParticles{get{int n=0;if(_auraFire!=null)foreach(var p in _auraFire)if(p.name=="CoolingSmoke")n+=p.particleCount;return n;}}
  public int FireSteamFlameParticles{get{int n=0;if(_auraFire!=null)foreach(var p in _auraFire)if(p.name=="Combustion"||p.name=="TornFlame")n+=p.particleCount;return n;}}
  Vector3 _auraContactWorld;Quaternion _auraContactFacing;
  public bool SetFireSwordGrip(Vector3 point,Quaternion rotation){if(!IsFireSword(Profile)||!FireAuraConfigured||!Vfx120InterceptionMotion.Finite(point)||float.IsNaN(rotation.x+rotation.y+rotation.z+rotation.w)||float.IsInfinity(rotation.x+rotation.y+rotation.z+rotation.w))return false;_fireAura.transform.SetPositionAndRotation(point,rotation.normalized);return true;}
  public float FireSwordGripError(Vector3 point)=>_fireAura==null?float.PositiveInfinity:Vector3.Distance(_fireAura.transform.position,point);
  float _empowerConsumedAt=-1;
  public bool FireEmpowerConsumed=>_empowerConsumedAt>=0;
  public bool ConsumeFireEmpower(){if(!IsFireEmpower(Profile)||!FireAuraConfigured||FireEmpowerConsumed||Age>=Life)return false;_empowerConsumedAt=Age;return true;}
  public bool ConfirmFireEmpowerHit(Vector3 point){return IsFireEmpower(Profile)&&FireEmpowerConsumed&&FireAuraContacts==0&&SignalFireAuraHit(point);}
  public bool FireAuraConfigured=>_fireAura!=null;
  public int FireAuraContacts{get;private set;}
  public int FireAuraParticles{get{int n=0;if(_fireAura!=null)foreach(var p in _fireAura.GetComponentsInChildren<ParticleSystem>())n+=p.particleCount;return n;}}
  public float FireAuraMarkOpacity{get;private set;}
  public bool SignalFireAuraHit(Vector3 point)
  {
   if(!FireAuraConfigured||Age>=Life||!Vfx120InterceptionMotion.Finite(point)||IsFireEmpower(Profile)&&(!FireEmpowerConsumed||FireAuraContacts>0))return false;
   _auraHitAt=Age;FireAuraContacts++;if(Profile.UseOriginalKtp)ConfirmOriginalImpact(point);_auraMark.transform.position=point;_auraMark.transform.rotation=Quaternion.LookRotation((point-ReceivedOrigin).sqrMagnitude>.001f?(point-ReceivedOrigin).normalized:Vector3.forward);
   _auraContactWorld=point;_auraContactFacing=_auraMark.transform.rotation;
   foreach(var p in _auraHit){p.transform.parent.position=point;p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);p.Simulate(0,false,true,false);}return true;
  }
  void BuildFireAura()
  {
   if(!IsFireAura(Profile))return;_fireAura=Instantiate(Profile.NativeBodyPrefab,transform,false);_fireAura.transform.position=ReceivedOrigin;
   _auraFire=_fireAura.transform.Find("Travel").GetComponentsInChildren<ParticleSystem>();_auraHit=_fireAura.transform.Find("Contact").GetComponentsInChildren<ParticleSystem>();
   _auraSeal=WardRenderer("KTP_FireAura_Floor",_fireAura.transform,QuadMesh.Value,Profile.PatternMaterial);_auraSeal.transform.localPosition=new Vector3(0,-.8f,0);_auraSeal.transform.localRotation=Quaternion.Euler(90,0,0);_auraSeal.transform.localScale=Vector3.one*3.6f;
   _auraMark=WardRenderer("KTP_FireAura_Hit",_fireAura.transform,QuadMesh.Value,Profile.PatternMaterial);_auraMark.transform.localScale=Vector3.one*.85f;_auraMark.enabled=false;_auraBlock=new MaterialPropertyBlock();
   if(IsFireEmpower(Profile)){_auraSeal.transform.localPosition=Vector3.zero;_auraSeal.transform.localRotation=Quaternion.identity;_auraSeal.transform.localScale=Vector3.one*.65f;_auraMark.transform.localScale=Vector3.one*1.6f;}
   if(IsFireResolve(Profile)){_auraSeal.transform.localPosition=new Vector3(0,0,.25f);_auraSeal.transform.localRotation=Quaternion.identity;_auraSeal.transform.localScale=Vector3.one*1.15f;_auraMark.transform.localScale=Vector3.one*.6f;}
   if(IsFireSword(Profile)){_auraSeal.transform.localPosition=new Vector3(0,.15f,.04f);_auraSeal.transform.localRotation=Quaternion.identity;_auraSeal.transform.localScale=Vector3.one*.35f;}
   if(IsFireJet(Profile)){_auraSeal.transform.localPosition=new Vector3(0,0,.02f);_auraSeal.transform.localRotation=Quaternion.identity;_auraSeal.transform.localScale=Vector3.one*.9f;}
   if(IsFireHealing(Profile)){_healingMark=WardRenderer("KTP_ConfirmedHealingFloor",_fireAura.transform,QuadMesh.Value,Profile.PatternMaterial);_healingMark.enabled=false;}
   if(IsFireBurn(Profile)){_auraSeal.transform.localPosition=Vector3.zero;_auraSeal.transform.localRotation=Quaternion.identity;_auraSeal.transform.localScale=Vector3.one*.6f;}
   if(IsFireBackblast(Profile))_auraMark.transform.localScale=Vector3.one*2.1f;
   foreach(var p in _fireAura.GetComponentsInChildren<ParticleSystem>()){p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);p.Simulate(0,false,true,false);}
  }
  void SampleFireAura()
  {
   if(_fireAura==null)return;
   if(IsFireSummon(Profile)&&_meshySummon!=null)_fireAura.transform.position=_meshySummon.transform.position+Vector3.up*.8f;
   if(IsFireMelt(Profile)||IsFireSteam(Profile)||IsFireWard(Profile))_fireAura.transform.position=transform.TransformPoint(new Vector3(_aim.x,_targetGround+.8f,_aim.z));
   if(IsFireBurn(Profile)){if(PreviewControlled&&DemonstrationCues&&_burnAt<0)BeginFireBurnPath(ReceivedOrigin,TargetPoint());if(_burnAt>=0){_fireAura.transform.position=Vector3.Lerp(_burnFrom,_burnTo,FireBurnProgress);if(FireBurnProgress>=1&&FireAuraContacts==0)SignalFireAuraHit(_burnTo);}}
   else if(IsFireJet(Profile)){Vector3 d=TargetPoint()-ReceivedOrigin;if(d.sqrMagnitude>.001f)_fireAura.transform.rotation=Quaternion.LookRotation(d,Mathf.Abs(d.normalized.y)>.98f?Vector3.forward:Vector3.up);float h=NativeImpactClock();if(IsFireBackblast(Profile)){if(PreviewControlled&&DemonstrationCues&&Age>=1.2f&&!FireBackblastEnded)EndFireBackblast(ReceivedOrigin-_fireAura.transform.forward*.8f);}else if(IsFireHealing(Profile)){if(PreviewControlled&&DemonstrationCues&&Age>=.8f&&_healingAt<0)ConfirmFireHealingHit(transform,TargetPoint(),new Vector3(TargetPoint().x,ReceivedOrigin.y-.8f,TargetPoint().z));}else if(h>=0&&Age>=h&&FireAuraContacts==0)SignalFireAuraHit(TargetPoint());}
   else if(PreviewControlled&&DemonstrationCues&&IsFireSword(Profile)){SetFireSwordGrip(ReceivedOrigin,Quaternion.Euler(0,0,Mathf.Lerp(35,-80,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.5f,1.1f,Age)))));if(Age>=.85f&&FireAuraContacts==0)SignalFireAuraHit(ReceivedOrigin+new Vector3(.85f,.45f,0));}
   else if(PreviewControlled&&DemonstrationCues&&IsFireEmpower(Profile)){if(Age>=1.2f&&!FireEmpowerConsumed)ConsumeFireEmpower();if(Age>=1.4f&&FireAuraContacts==0)ConfirmFireEmpowerHit(ReceivedOrigin+Vector3.forward*1.5f);}
   else if(PreviewControlled&&DemonstrationCues&&_auraDemo<3&&Age>=.6f+_auraDemo*.7f){float a=_auraDemo*2.1f;SignalFireAuraHit(ReceivedOrigin+(IsFireResolve(Profile)?new Vector3(Mathf.Cos(a)*.35f,.15f,.3f):new Vector3(Mathf.Cos(a)*1.5f,0,Mathf.Sin(a)*1.5f)));_auraDemo++;}
   const float dt=1f/60f;while(_auraSim+dt<=Mathf.Min(Age,Life)+.00001f){float at=_auraSim+dt;foreach(var p in _auraFire){var em=p.emission;em.enabled=at<Life-.6f&&(_empowerConsumedAt<0||at<_empowerConsumedAt);if(IsFireSteam(Profile))em.enabled=p.name=="CoolingSmoke"?at<Life-.8f:at<1f;if(IsFireBurn(Profile))em.enabled=_burnAt>=0&&at<_burnAt+1.8f;p.Simulate(dt,false,false,false);}if(!Profile.UseOriginalKtp&&_auraHitAt>=0&&at>=_auraHitAt)foreach(var p in _auraHit)p.Simulate(Mathf.Min(dt,at-_auraHitAt),false,false,false);_auraSim=at;}
   if((IsFireSword(Profile)||IsFireJet(Profile)||IsFireBurn(Profile))&&_auraHitAt>=0){_auraMark.transform.SetPositionAndRotation(_auraContactWorld,_auraContactFacing);_fireAura.transform.Find("Contact").position=_auraContactWorld;}
   float fade=Mathf.SmoothStep(0,1,Age/.15f)*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Life-.5f,Life,Age)));
   float sealFade=(IsFireBurn(Profile)?(_burnAt<0?0:1-FireBurnProgress):1)*fade*(_empowerConsumedAt<0?1:Mathf.Clamp01(1-(Age-_empowerConsumedAt)/.18f));
   _auraBlock.SetFloat("_Alpha",sealFade*.65f);_auraBlock.SetFloat("_Erode",0);_auraBlock.SetColor("_BaseColor",new Color(1.5f,.45f,.1f));_auraSeal.SetPropertyBlock(_auraBlock);_auraSeal.enabled=sealFade>0;
   FireAuraMarkOpacity=_auraHitAt<0?0:Mathf.Clamp01(1-(Age-_auraHitAt)/.5f)*fade;_auraBlock.SetFloat("_Alpha",FireAuraMarkOpacity);if(IsFireMelt(Profile))_auraBlock.SetFloat("_Erode",Mathf.Clamp01((Age-_auraHitAt)/.5f));_auraMark.SetPropertyBlock(_auraBlock);_auraMark.enabled=!Profile.UseOriginalKtp&&FireAuraMarkOpacity>0;
   if(_healingMark!=null){float t=Age-_healingAt;FireHealingOpacity=_healingAt<0?0:fade*Mathf.SmoothStep(0,1,Mathf.Clamp01(t/.25f));_healingMark.enabled=FireHealingOpacity>0;_healingMark.transform.SetPositionAndRotation(_healingGround+Vector3.up*.025f,Quaternion.Euler(90,0,0));_healingMark.transform.localScale=Vector3.one*Mathf.Lerp(.5f,2.8f,Mathf.SmoothStep(0,1,Mathf.Clamp01(t/.3f)));_auraBlock.SetFloat("_Alpha",FireHealingOpacity*.8f);_auraBlock.SetColor("_BaseColor",new Color(1.3f,.85f,.32f));_healingMark.SetPropertyBlock(_auraBlock);}
   if(Age>=Life)foreach(var p in _fireAura.GetComponentsInChildren<ParticleSystem>())p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
  }
  void ClearFireAura(){if(_fireAura!=null){_fireAura.SetActive(false);if(Application.isPlaying)Destroy(_fireAura);else DestroyImmediate(_fireAura);}_fireAura=null;_auraSim=0;_auraHitAt=-1;_auraDemo=0;_empowerConsumedAt=-1;_burnAt=-1;_healingAt=-1;_healingMark=null;FireHealingOpacity=0;FireAuraContacts=0;FireAuraMarkOpacity=0;}
 }
}
