using System;
using System.Collections.Generic;
using UnityEngine;
namespace Oheangbu.App.SpellVFX120
{
 // Static summon presentation. No combat registration, Animator or gameplay collider.
 public sealed class FireHaetaeVfx:MonoBehaviour
 {
  public const float ReviewLife=4.6f;
  public Vector3 Centre{get;private set;}
  public Vector3 Forward{get;private set;}
  public bool Grounded{get;private set;}
  public float FootError{get;private set;}
  public float Age{get;private set;}
  public int LiveParticles{get{int n=0;if(root!=null)foreach(var ps in systems)n+=ps.particleCount;return n;}}
  public bool Ended=>Age>=ReviewLife;
  public int VisibleRenderers{get{int n=0;if(root!=null&&root.activeInHierarchy)foreach(var r in renderers)if(r.enabled)n++;return n;}}
  readonly List<Mesh> owned=new List<Mesh>();readonly RaycastHit[] hits=new RaycastHit[32];
  GameObject root,model;MeshRenderer[] renderers;ParticleSystem[] systems;Vfx120TraditionalMotif seal;MaterialPropertyBlock block;
  Vector3[] feet;Quaternion rotation;float height;bool disposed;
  bool Ground(Vector3 p,out Vector3 q)
  {
   q=p;float best=float.MaxValue;int n=Physics.RaycastNonAlloc(p+Vector3.up*5,Vector3.down,hits,15,LayerMask.GetMask("Default","WorldGround","WorldRidge","WorldRibbon"),QueryTriggerInteraction.Ignore);bool found=false;
   for(int i=0;i<n;i++){var h=hits[i];if(h.distance>=best||h.normal.y<.65f||h.collider is CharacterController||h.collider.GetComponentInParent<Oheangbu.Combat.EnemyVitals>()!=null||h.collider.transform.IsChildOf(transform))continue;best=h.distance;q=h.point;found=true;}return found;
  }
  public void Configure(Vfx120Profile p,Vector3 origin,Vector3 forward)
  {
   block=new MaterialPropertyBlock();Forward=Vector3.ProjectOnPlane(forward,Vector3.up).normalized;if(Forward.sqrMagnitude<.1f)Forward=Vector3.forward;rotation=Quaternion.LookRotation(Forward);
   Grounded=Ground(origin+Forward*4,out var center);Centre=center;
   root=new GameObject("FireHaetaePresentation");root.transform.SetParent(transform,false);root.transform.SetPositionAndRotation(Centre,rotation);
   model=Instantiate(p.FireHaetaePrefab,root.transform);model.name="FireHaetaeStaticModel";
   var anchors=new List<Transform>();foreach(var t in model.GetComponentsInChildren<Transform>())if(t.name.StartsWith("Foot_",StringComparison.Ordinal))anchors.Add(t);
   if(anchors.Count!=3)throw new InvalidOperationException("Three planted paw anchors required");feet=new Vector3[3];var offset=new float[3];
   for(int i=0;i<3;i++){feet[i]=root.transform.InverseTransformPoint(anchors[i].position);Grounded&=Ground(anchors[i].position,out var g);offset[i]=g.y-anchors[i].position.y+.008f;if(Mathf.Abs(offset[i])>.15f)Grounded=false;}
   // Only the bottom 35cm conforms. The authored torso, face, mane and leg lengths above it are untouched.
   foreach(var f in model.GetComponentsInChildren<MeshFilter>())
   {
    var clone=Instantiate(f.sharedMesh);owned.Add(clone);var v=clone.vertices;
    for(int k=0;k<v.Length;k++)
    {
     var local=root.transform.InverseTransformPoint(f.transform.TransformPoint(v[k]));if(local.y>.35f)continue;int nearest=0;float best=float.MaxValue;
     for(int j=0;j<3;j++){float d=Vector2.Distance(new Vector2(local.x,local.z),new Vector2(feet[j].x,feet[j].z));if(d<best){best=d;nearest=j;}}
     local.y+=offset[nearest]*(1-Mathf.SmoothStep(0,1,local.y/.35f));v[k]=f.transform.InverseTransformPoint(root.transform.TransformPoint(local));
    }
    clone.vertices=v;clone.RecalculateBounds();clone.RecalculateNormals();clone.RecalculateTangents();f.sharedMesh=clone;
   }
   for(int i=0;i<3;i++){feet[i].y+=offset[i];var wp=root.transform.TransformPoint(feet[i]);if(Ground(wp,out var g))FootError=Mathf.Max(FootError,Mathf.Abs(wp.y-g.y));}
   renderers=root.GetComponentsInChildren<MeshRenderer>();Bounds b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);height=b.max.y-Centre.y;
   var sealGo=new GameObject("GroundKtpSeal");sealGo.transform.SetParent(root.transform,false);sealGo.transform.position=Centre+Vector3.up*.025f;sealGo.transform.rotation=Quaternion.FromToRotation(Vector3.forward,Vector3.up);
   seal=sealGo.AddComponent<Vfx120TraditionalMotif>();var settings=Vfx120TraditionalMotif.Settings.DefaultFor(Vfx120TraditionalMotif.Role.Summon);settings.PreserveAuthored=true;settings.PreviewControlled=true;settings.HierarchyScaling=true;settings.PatternFocus=true;settings.FiniteWindow=true;settings.Lifetime=ReviewLife;settings.FadeSeconds=.8f;settings.Brightness=.95f;
   seal.Configure(p.FireHaetaeSeal,p.Pigment,p.Ink,Vfx120TraditionalMotif.Role.Summon,settings);seal.Sample(1.2f);float diameter=0;
   foreach(var ps in seal.GetComponentsInChildren<ParticleSystem>())
   {
    var particles=new ParticleSystem.Particle[ps.main.maxParticles];int count=ps.GetParticles(particles);
    for(int i=0;i<count;i++)diameter=Mathf.Max(diameter,particles[i].GetCurrentSize(ps)*Mathf.Abs(ps.transform.lossyScale.x));
   }
   if(diameter<.01f)throw new InvalidOperationException("KTP seal emitted no readable particle");
   sealGo.transform.localScale=Vector3.one*(3.1f/diameter);seal.Sample(0);
   var debris=Instantiate(p.FireHaetaeDebris,root.transform);systems=debris.GetComponentsInChildren<ParticleSystem>();foreach(var ps in systems){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);ps.useAutoRandomSeed=false;ps.randomSeed=601;}
   Sample(0);
  }
  public void Sample(float age)
  {
   if(disposed||root==null)return;Age=Mathf.Max(0,age);root.transform.SetPositionAndRotation(Centre,rotation);root.SetActive(Grounded&&!Ended);
   foreach(var r in renderers){block.Clear();block.SetFloat("_Age",Age);block.SetFloat("_Ground",Centre.y);block.SetFloat("_Height",height);r.SetPropertyBlock(block);r.enabled=Age>.2f&&!Ended;}
   if(Grounded&&!Ended)seal.Sample(Age);
   foreach(var ps in systems)
   {
    float local=ps.name=="FormationSparks"?Age:ps.name=="ManeEmbers"?Age-.8f:Age-3.8f;
    bool window=ps.name=="FormationSparks"?Age<1.2f:ps.name=="ManeEmbers"?Age<3.8f:!Ended;
    if(local>=0&&window&&!Ended)ps.Simulate(local,false,true,false);else ps.Clear();
   }
  }
  public Vector3[] FeetWorld(){var result=new Vector3[feet.Length];for(int i=0;i<feet.Length;i++)result[i]=root.transform.TransformPoint(feet[i]);return result;}
  public void Dispose(){if(disposed)return;disposed=true;if(root!=null)Remove(root);foreach(var m in owned)Remove(m);owned.Clear();}
  static void Remove(UnityEngine.Object o){if(o==null)return;if(Application.isPlaying)Destroy(o);else DestroyImmediate(o);}
  void OnDestroy()=>Dispose();
 }
}
