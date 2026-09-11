using UnityEngine;
namespace Oheangbu.App.SpellVFX120
{
 public sealed partial class Vfx120Effect
 {
  sealed class FirePellet{public Transform Travel,Contact;public ParticleSystem[] Fire,Hit;public MeshRenderer Mark;public float Launch=-1,Impact=-1;public Vector3 From,To;}
  GameObject _fireCompanions;FirePellet[] _pellets;float _pelletSim;MaterialPropertyBlock _pelletBlock;
  public static bool IsFireCompanions(Vfx120Profile p)=>p!=null&&p.Glyph=="넝"&&p.NativeBodyPrefab!=null&&p.NativeBodyPrefab.name=="PF_FireCompanions_036";
  public int FirePelletLaunches{get;private set;}public int FirePelletContacts{get;private set;}
  public int FirePelletParticles{get{int n=0;if(_pellets!=null)foreach(var s in _pellets){foreach(var p in s.Fire)n+=p.particleCount;foreach(var p in s.Hit)n+=p.particleCount;}return n;}}
  public bool LaunchFirePellet(int index,Vector3 destination)
  {if(_pellets==null||index<0||index>=3||Age>=Life||!Vfx120InterceptionMotion.Finite(destination))return false;var s=_pellets[index];if(s.Launch>=0)return false;s.From=s.Travel.position;s.To=destination;s.Launch=Age;FirePelletLaunches++;return true;}
  public bool ConfirmFirePelletHit(int index,Vector3 point)
  {if(_pellets==null||index<0||index>=3||Age>=Life||!Vfx120InterceptionMotion.Finite(point))return false;var s=_pellets[index];if(s.Launch<0||s.Impact>=0)return false;s.Impact=Age;s.To=point;s.Contact.position=point;FirePelletContacts++;if(Profile.UseOriginalKtp)ConfirmOriginalImpact(point);return true;}
  void BuildFireCompanions()
  {
   if(!IsFireCompanions(Profile))return;_fireCompanions=Instantiate(Profile.NativeBodyPrefab,transform,false);_pellets=new FirePellet[3];_pelletBlock=new MaterialPropertyBlock();
   for(int i=0;i<3;i++){var root=_fireCompanions.transform.GetChild(i);var s=new FirePellet{Travel=root.Find("Travel"),Contact=root.Find("Contact")};s.Fire=s.Travel.GetComponentsInChildren<ParticleSystem>();s.Hit=s.Contact.GetComponentsInChildren<ParticleSystem>();s.Mark=WardRenderer("KTP_Pellet_"+i,root,QuadMesh.Value,Profile.PatternMaterial);_pellets[i]=s;foreach(var p in root.GetComponentsInChildren<ParticleSystem>()){p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);p.Simulate(0,false,true,false);}}
  }
  void SampleFireCompanions()
  {
   if(_pellets==null)return;
   if(PreviewControlled&&DemonstrationCues)for(int i=0;i<3;i++){if(Age>=.6f+i*.3f&&_pellets[i].Launch<0)LaunchFirePellet(i,ReceivedOrigin+new Vector3((i-1)*.7f,0,2));if(Age>=1.05f+i*.3f&&_pellets[i].Impact<0)ConfirmFirePelletHit(i,_pellets[i].To);}
   const float dt=1f/60f;while(_pelletSim+dt<=Mathf.Min(Age,Life)+.00001f)
   {float at=_pelletSim+dt;for(int i=0;i<3;i++){var s=_pellets[i];float angle=i*Mathf.PI*2/3+at*.6f;s.Travel.position=s.Launch<0?ReceivedOrigin+new Vector3(Mathf.Cos(angle)*.65f,.3f+Mathf.Sin(angle)*.18f,Mathf.Sin(angle)*.65f):Vector3.Lerp(s.From,s.To,Mathf.Clamp01((at-s.Launch)/.45f));foreach(var p in s.Fire){var em=p.emission;em.enabled=(s.Impact<0||at<s.Impact)&&at<Life-.5f;p.Simulate(dt,false,false,false);}if(!Profile.UseOriginalKtp&&s.Impact>=0&&at>=s.Impact)foreach(var p in s.Hit)p.Simulate(Mathf.Min(dt,at-s.Impact),false,false,false);}_pelletSim=at;}
   foreach(var s in _pellets){float opacity=s.Launch<0?.55f:s.Impact<0?0:Mathf.Clamp01(1-(Age-s.Impact)/.55f);if(Age>=Life)opacity=0;s.Mark.enabled=opacity>0&&(!Profile.UseOriginalKtp||s.Impact<0);s.Mark.transform.position=s.Impact<0?s.Travel.position:s.To;s.Mark.transform.localScale=Vector3.one*(s.Impact<0?.3f:.85f);_pelletBlock.SetFloat("_Alpha",opacity);_pelletBlock.SetFloat("_Erode",0);_pelletBlock.SetColor("_BaseColor",new Color(1.6f,.5f,.1f));s.Mark.SetPropertyBlock(_pelletBlock);}
   if(Age>=Life)foreach(var p in _fireCompanions.GetComponentsInChildren<ParticleSystem>())p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
  }
  void ClearFireCompanions(){if(_fireCompanions!=null){_fireCompanions.SetActive(false);if(Application.isPlaying)Destroy(_fireCompanions);else DestroyImmediate(_fireCompanions);}_fireCompanions=null;_pellets=null;_pelletSim=0;FirePelletLaunches=FirePelletContacts=0;}
 }
}
