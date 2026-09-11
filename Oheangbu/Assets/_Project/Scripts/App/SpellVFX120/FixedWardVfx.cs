using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace Oheangbu.App.SpellVFX120
{
 // Presentation only. No gameplay collider, defender registration or combat event.
 public sealed class FixedWardVfx:MonoBehaviour
 {
  sealed class Panel{public Transform Root;public Vector3 Foot;public Quaternion Rotation;public MeshRenderer[] Renderers;public Vfx120TraditionalMotif Pattern;public float HitAt=-10;public Vector3 Hit;public bool Grounded;public Mesh Water;public Vector3[] Rest,Deformed;}
  sealed class Contact{public int Id;public float At;public GameObject Root;public Vfx120TraditionalMotif Pattern;public ParticleSystem[] Particles;}
  readonly List<Mesh> meshes=new List<Mesh>();readonly List<Contact> contacts=new List<Contact>();readonly HashSet<int> ids=new HashSet<int>();
  readonly RaycastHit[] hits=new RaycastHit[32];MaterialPropertyBlock block;
  Panel[] panels;GameObject root;Vfx120Profile profile;Vfx120TraditionalMotif floor;ParticleSystem[] ambient;
  Vector3[] ring;bool[] grounded;float clock;bool disposed,released;Material waterMaterial;
  public Vector3 Centre {get;private set;}public Vector3 Forward{get;private set;}
  public int AcceptedContacts{get;private set;}public int MissingGround{get;private set;}public int GroundQueries{get;private set;}
  public int PanelCount=>panels==null?0:panels.Length;
  public int LiveContacts=>contacts.Count;
  public int VisiblePanels{get;private set;}
  public int LiveParticles{get{int n=0;if(root!=null)foreach(var p in root.GetComponentsInChildren<ParticleSystem>())n+=p.particleCount;return n;}}
  public bool Ended=>profile!=null&&clock>=profile.Duration;
  public Vector3 ContactPoint(int panel)=>panels[panel%panels.Length].Foot+Vector3.up*profile.WardHeight*.55f;
  public Vector3 ContactNormal(int panel)=>Vector3.ProjectOnPlane(panels[panel%panels.Length].Foot-Centre,Vector3.up).normalized;
  public float InternalOpacity(Vector3 camera)=>Mathf.Lerp(profile.WardKind==Vfx120WardKind.Water?.05f:.18f,1,CameraExterior(camera));
  float CameraExterior(Vector3 camera)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(profile.WardRadius-.15f,profile.WardRadius+.35f,Vector3.ProjectOnPlane(camera-Centre,Vector3.up).magnitude));
  public bool TryGround(Vector3 input,out Vector3 point)
  {
   GroundQueries++;point=input;float best=float.PositiveInfinity;bool found=false;
   int count=Physics.RaycastNonAlloc(input+Vector3.up*16,Vector3.down,hits,48,LayerMask.GetMask("Default","WorldGround","WorldRidge","WorldRibbon"),QueryTriggerInteraction.Ignore);
   for(int i=0;i<count;i++){var h=hits[i];if(h.distance>=best||h.normal.y<.3f||h.collider is CharacterController||h.collider.GetComponentInParent<Oheangbu.Combat.EnemyVitals>()!=null||h.collider.transform.IsChildOf(transform))continue;best=h.distance;point=h.point;found=true;}
   if(!found)MissingGround++;return found;
  }
  public void Configure(Vfx120Profile p,Vector3 centre,Vector3 forward)
  {
   block=new MaterialPropertyBlock();
   profile=p;Centre=centre;Forward=Vector3.ProjectOnPlane(forward,Vector3.up).normalized;if(Forward.sqrMagnitude<.1f)Forward=Vector3.forward;
   bool centreGrounded=TryGround(centre,out var ground);if(centreGrounded)Centre=ground;
   root=new GameObject("FixedWard_"+p.Glyph);root.transform.SetParent(transform,false);root.transform.SetPositionAndRotation(Centre,Quaternion.LookRotation(Forward));
   int count=p.WardKind==Vfx120WardKind.Wood?6:8;panels=new Panel[count];ring=new Vector3[129];grounded=new bool[129];
   for(int i=0;i<129;i++){float a=i/128f*Mathf.PI*2;var pos=Centre+root.transform.TransformDirection(new Vector3(Mathf.Sin(a),0,Mathf.Cos(a)))*p.WardRadius;grounded[i]=TryGround(pos,out ring[i]);}
   if(centreGrounded)floor=Motif("FloorKTP",root.transform,p.WardPatternPrefab,Centre+Vector3.up*.025f,Quaternion.FromToRotation(Vector3.forward,Vector3.up),p.WardRadius*2,p.Duration,Vfx120TraditionalMotif.Role.Shield);
   if(p.WardKind==Vfx120WardKind.Water&&p.WardWaterMaterial!=null)waterMaterial=p.WardWaterMaterial;
   for(int j=0;j<count;j++)BuildPanel(j,count);
   BuildAmbient();RenderPipelineManager.beginCameraRendering+=ForCamera;Sample(0);
  }
  void BuildPanel(int j,int count)
  {
   float angle=(j+.5f)*Mathf.PI*2/count;var outward=root.transform.TransformDirection(new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle)));
   var panelGo=new GameObject("WardPanel_"+j);panelGo.transform.SetParent(root.transform,false);
   bool valid=TryGround(Centre+outward*profile.WardRadius,out var foot);var rotation=Quaternion.LookRotation(outward);
   panelGo.transform.SetPositionAndRotation(foot,rotation);var renderers=new List<MeshRenderer>();
   var panel=new Panel{Root=panelGo.transform,Foot=foot,Rotation=rotation,Grounded=valid};panels[j]=panel;
   float span=2*profile.WardRadius*Mathf.Sin(Mathf.PI/count)*1.02f;
   if(profile.WardKind==Vfx120WardKind.Wood)
   {
    renderers.Add(Part(panelGo.transform,"ExistingVineSupport",Normalize(profile.BodyMesh,new Vector3(.28f,profile.WardHeight,.25f)),profile.WardMaterial));
    for(int vine=0;vine<3;vine++)
    {
     var points=new Vector3[25];for(int k=0;k<25;k++)
     {
      float t=k/24f,a=angle+t*Mathf.PI*2/count;var world=Centre+root.transform.TransformDirection(new Vector3(Mathf.Sin(a),0,Mathf.Cos(a)))*profile.WardRadius;
      float at=Mathf.Repeat(a,Mathf.PI*2)/(Mathf.PI*2)*128;int index=Mathf.Min(127,(int)at);world.y=Mathf.Lerp(ring[index].y,ring[index+1].y,at-index)+.4f+vine*.53f+.23f*Mathf.Sin(t*Mathf.PI*2)*Mathf.Sin(t*Mathf.PI);
      points[k]=panelGo.transform.InverseTransformPoint(world);
     }
     renderers.Add(Part(panelGo.transform,"WovenVine",Tube(points,.038f),profile.WardMaterial));
    }
   }
   else if(profile.WardKind==Vfx120WardKind.Earth)
    renderers.Add(Part(panelGo.transform,"ExistingStoneSlab",Normalize(profile.BodyMesh,new Vector3(span,profile.WardHeight,.38f)),profile.WardMaterial));
   else if(profile.WardKind==Vfx120WardKind.Metal)
   {
    var bar=Normalize(profile.BodyMesh,new Vector3(.075f,profile.WardHeight,.075f));
    var ends=new Vector3[2];for(int edge=0;edge<2;edge++){int ix=(j+edge)*128/count;ends[edge]=panelGo.transform.InverseTransformPoint(ring[ix]);var r=Part(panelGo.transform,"ExistingMetalFrame",bar,profile.WardMaterial);r.transform.localPosition=ends[edge];renderers.Add(r);}
    foreach(float y in new[]{.06f,profile.WardHeight-.06f}){var points=new[]{ends[0]+Vector3.up*y,ends[1]+Vector3.up*y};renderers.Add(Part(panelGo.transform,"FrameJoin",Tube(points,.04f),profile.WardMaterial));}
   }
   // Every membrane column is anchored to its own ground sample; panels stay upright.
   var surface=Surface(j,count,panelGo.transform,out bool allGround);panel.Grounded&=allGround;
   if(profile.WardKind==Vfx120WardKind.Earth)Thicken(surface,.32f);
   var membrane=Part(panelGo.transform,"GroundFollowingMembrane",surface,profile.WardKind==Vfx120WardKind.Water?waterMaterial:profile.WardMaterial);renderers.Add(membrane);
   panel.Renderers=renderers.ToArray();
   if(profile.WardKind==Vfx120WardKind.Water){panel.Water=surface;panel.Rest=surface.vertices;panel.Deformed=(Vector3[])panel.Rest.Clone();surface.MarkDynamic();}
   if(profile.WardKind==Vfx120WardKind.Earth)
   {
    var stone=panel.Renderers[0].GetComponent<MeshFilter>().sharedMesh;var vertices=stone.vertices;
    for(int k=0;k<vertices.Length;k++){var wp=panelGo.transform.TransformPoint(new Vector3(vertices[k].x,0,vertices[k].z));float angle2=Mathf.Repeat(Mathf.Atan2(root.transform.InverseTransformPoint(wp).x,root.transform.InverseTransformPoint(wp).z),Mathf.PI*2);float at=angle2/(Mathf.PI*2)*128;int ix=Mathf.Min(127,(int)at);vertices[k].y+=Mathf.Lerp(ring[ix].y,ring[ix+1].y,at-ix)-foot.y;}
    stone.vertices=vertices;stone.RecalculateNormals();stone.RecalculateBounds();
   }
   panel.Pattern=Motif("PanelKTP",panelGo.transform,profile.WardPatternPrefab,foot+Vector3.up*profile.WardHeight*.52f+outward*.035f,rotation,profile.WardHeight*.78f,profile.Duration,Vfx120TraditionalMotif.Role.Shield);
  }
  MeshRenderer Part(Transform parent,string name,Mesh mesh,Material mat)
  {var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;return r;}
  Mesh Normalize(Mesh source,Vector3 size)
  {
   if(source==null)throw new InvalidOperationException("Ward source mesh missing");var mesh=Instantiate(source);mesh.name="WardReuse_"+source.name;
   var b=source.bounds;var v=mesh.vertices;var uv=new Vector2[v.Length];
   for(int i=0;i<v.Length;i++){var q=v[i]-b.min;v[i]=Vector3.Scale(new Vector3(q.x/Mathf.Max(.001f,b.size.x)-.5f,q.y/Mathf.Max(.001f,b.size.y),q.z/Mathf.Max(.001f,b.size.z)-.5f),size);uv[i]=new Vector2(q.x/Mathf.Max(.001f,b.size.x),q.y/Mathf.Max(.001f,b.size.y));}
   mesh.vertices=v;mesh.uv=uv;var centers=new Color[v.Length];for(int i=0;i<v.Length;i++)centers[i]=new Color(0,v[i].y,0,1);mesh.colors=centers;mesh.RecalculateNormals();mesh.RecalculateBounds();meshes.Add(mesh);return mesh;
  }
  Mesh Surface(int panel,int count,Transform parent,out bool valid)
  {
   const int nx=16,ny=12;var v=new Vector3[(nx+1)*(ny+1)];var uv=new Vector2[v.Length];var t=new List<int>();var ok=new bool[nx+1];valid=true;
   for(int x=0;x<=nx;x++)
   {
    float a=(panel+x/(float)nx)/count*Mathf.PI*2;var p=Centre+root.transform.TransformDirection(new Vector3(Mathf.Sin(a),0,Mathf.Cos(a)))*profile.WardRadius;ok[x]=TryGround(p,out var g);valid&=ok[x];
    for(int y=0;y<=ny;y++){int i=y*(nx+1)+x;v[i]=parent.InverseTransformPoint(g+Vector3.up*(.012f+y/(float)ny*profile.WardHeight));uv[i]=new Vector2(x/(float)nx,y/(float)ny);}
   }
   for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)if(ok[x]&&ok[x+1]){int i=y*(nx+1)+x;t.AddRange(new[]{i,i+1,i+nx+1,i+1,i+nx+2,i+nx+1});}
   return MeshOf("WardSurface",v,uv,t);
  }
  Mesh Tube(Vector3[] points,float radius)
  {
   const int sides=6;var v=new Vector3[points.Length*sides];var uv=new Vector2[v.Length];var t=new List<int>();
   for(int j=0;j<points.Length;j++)for(int k=0;k<sides;k++)
   {
    var dir=(points[Mathf.Min(j+1,points.Length-1)]-points[Mathf.Max(0,j-1)]).normalized;var side=Vector3.Cross(dir,Vector3.forward).normalized;if(side.sqrMagnitude<.1f)side=Vector3.right;var up=Vector3.Cross(dir,side);
    float a=k/(float)sides*Mathf.PI*2;int i=j*sides+k;v[i]=points[j]+radius*(side*Mathf.Cos(a)+up*Mathf.Sin(a));uv[i]=new Vector2(k/(float)sides,Mathf.Clamp01(points[j].y/profile.WardHeight));
    if(j<points.Length-1){int n=j*sides+(k+1)%sides;t.AddRange(new[]{i,n,i+sides,n,n+sides,i+sides});}
   }
   var mesh=MeshOf("WardConnection",v,uv,t);var centers=new Color[v.Length];for(int j=0;j<points.Length;j++)for(int k=0;k<sides;k++)centers[j*sides+k]=new Color(points[j].x,points[j].y,points[j].z,1);mesh.colors=centers;return mesh;
  }
  void Thicken(Mesh mesh,float thickness)
  {
   var old=mesh.vertices;var oldUv=mesh.uv;var oldTriangles=mesh.triangles;int n=old.Length;var vertices=new Vector3[n*2];var uv=new Vector2[n*2];var triangles=new List<int>(oldTriangles);
   for(int i=0;i<n;i++){vertices[i]=old[i];vertices[n+i]=old[i]-Vector3.forward*thickness;uv[i]=uv[n+i]=oldUv[i];}
   for(int i=0;i<oldTriangles.Length;i+=3)triangles.AddRange(new[]{oldTriangles[i]+n,oldTriangles[i+2]+n,oldTriangles[i+1]+n});
   for(int x=0;x<16;x++){int a=12*17+x;triangles.AddRange(new[]{a,a+n,a+1,a+1,a+n,a+n+1});}
   mesh.vertices=vertices;mesh.uv=uv;mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
  }
  Mesh MeshOf(string name,Vector3[] v,Vector2[] uv,List<int> t){var m=new Mesh{name=name};m.vertices=v;m.uv=uv;m.colors=new Color[v.Length];m.SetTriangles(t,0);m.RecalculateNormals();m.RecalculateBounds();meshes.Add(m);return m;}
  Vfx120TraditionalMotif Motif(string name,Transform parent,GameObject prefab,Vector3 position,Quaternion rotation,float diameter,float duration,Vfx120TraditionalMotif.Role role)
  {
   if(prefab==null)return null;var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.SetPositionAndRotation(position,rotation);
   var motif=go.AddComponent<Vfx120TraditionalMotif>();var options=Vfx120TraditionalMotif.Settings.DefaultFor(role);options.PreserveAuthored=true;options.PreviewControlled=true;options.HierarchyScaling=true;options.PatternFocus=true;options.FiniteWindow=true;options.Lifetime=duration;options.FadeSeconds=role==Vfx120TraditionalMotif.Role.Impact?.08f:.5f;options.Brightness=1.8f;
   motif.Configure(prefab,profile.Pigment,profile.Ink,role,options);motif.Sample(.12f);
   Bounds bounds=new Bounds(position,Vector3.zero);bool first=true;foreach(var r in go.GetComponentsInChildren<Renderer>()){if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);}
   float extent=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));if(extent>.01f)go.transform.localScale=Vector3.one*(diameter/extent);
   motif.Sample(0);return motif;
  }
  void BuildAmbient()
  {
   var list=new List<ParticleSystem>();if(profile.WardKind==Vfx120WardKind.Water||profile.WardKind==Vfx120WardKind.Fire)
    for(int i=0;i<8;i++)if(grounded[i*16]){var go=Instantiate(profile.WardDebrisPrefab,root.transform);go.name="BoundaryParticles";go.transform.position=ring[i*16]+Vector3.up*.035f;foreach(var ps in go.GetComponentsInChildren<ParticleSystem>()){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;main.loop=true;main.maxParticles=16;main.startLifetime=.45f;main.startSize=profile.WardKind==Vfx120WardKind.Water?.15f:.025f;main.startSpeed=.15f;var em=ps.emission;em.SetBursts(Array.Empty<ParticleSystem.Burst>());em.rateOverTime=5;ps.useAutoRandomSeed=false;ps.randomSeed=(uint)(200+i);list.Add(ps);}}
   ambient=list.ToArray();
  }
  public bool ContactAt(int id,Vector3 point,Vector3 normal)
  {
   if(disposed||root==null||clock<profile.WardFormation||clock>=profile.Duration-profile.WardFade||ids.Contains(id)||!Finite(point)||!Finite(normal)||normal.sqrMagnitude<.01f)return false;
   float radius=Vector3.ProjectOnPlane(point-Centre,Vector3.up).magnitude;if(Mathf.Abs(radius-profile.WardRadius)>.55f)return false;
   int nearest=0;float best=float.MaxValue;for(int i=0;i<panels.Length;i++){float d=(panels[i].Foot-point).sqrMagnitude;if(d<best){nearest=i;best=d;}}
   var panel=panels[nearest];if(!panel.Grounded||point.y<panel.Foot.y-.1f||point.y>panel.Foot.y+profile.WardHeight+.1f)return false;
   ids.Add(id);AcceptedContacts++;panel.HitAt=clock;panel.Hit=point;
   var go=new GameObject("WardContact_"+id);go.transform.SetParent(root.transform,false);go.transform.SetPositionAndRotation(point,Quaternion.LookRotation(normal.normalized));
   var motif=Motif("ContactKTP",go.transform,profile.WardContactPrefab,point+normal.normalized*.04f,Quaternion.LookRotation(normal.normalized),.7f,.25f,Vfx120TraditionalMotif.Role.Impact);
   var debris=Instantiate(profile.WardDebrisPrefab,go.transform);debris.transform.localPosition=Vector3.zero;
   var particles=debris.GetComponentsInChildren<ParticleSystem>();foreach(var ps in particles){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);ps.useAutoRandomSeed=false;ps.randomSeed=(uint)(Math.Abs(id)+10);}
   contacts.Add(new Contact{Id=id,At=clock,Root=go,Pattern=motif,Particles=particles});return true;
  }
  static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
  public void Sample(float age)
  {
   if(disposed||root==null)return;root.transform.SetPositionAndRotation(Centre,Quaternion.LookRotation(Forward));clock=Mathf.Max(0,age);float fade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(profile.Duration-profile.WardFade,profile.Duration,clock));VisiblePanels=0;
   floor?.Sample(clock);
   for(int i=0;i<panels.Length;i++)
   {
    var p=panels[i];float stagger=profile.WardKind==Vfx120WardKind.Earth?i*.025f:0;
    float grow=Mathf.SmoothStep(0,1,Mathf.InverseLerp(stagger,profile.WardFormation,clock));
    float drop=profile.WardKind==Vfx120WardKind.Earth?(1-fade)*profile.WardHeight:0;
    p.Root.position=p.Foot-Vector3.up*drop;p.Root.rotation=p.Rotation;
    if(profile.WardKind==Vfx120WardKind.Wood){float h=clock-p.HitAt;p.Root.rotation*=Quaternion.Euler(Mathf.Sin(Mathf.Clamp01(h/.35f)*Mathf.PI)*6*Mathf.Clamp01(1-h/.5f),0,0);}
    bool visible=p.Grounded&&grow>0&&fade>0;if(visible)VisiblePanels++;
    if(p.Water!=null)
    {
     var uv=p.Water.uv;
     for(int k=0;k<p.Rest.Length;k++){var v=p.Rest[k];float h=uv[k].y;v.y-=h*profile.WardHeight*(1-grow*fade);v.z+=Mathf.Sin(clock*2.2f+uv[k].x*6+i)*.035f*h;float distance=Vector3.Distance(p.Root.TransformPoint(p.Rest[k]),p.Hit);float elapsed=clock-p.HitAt;v.z+=Mathf.Sin(distance*18-elapsed*14)*.07f*Mathf.Clamp01(1-elapsed/.5f)*Mathf.Clamp01(1-distance/1.2f)*h;p.Deformed[k]=v;}
     p.Water.vertices=p.Deformed;p.Water.RecalculateNormals();p.Water.RecalculateBounds();
    }
    foreach(var r in p.Renderers)
    {
     r.enabled=visible;block.Clear();block.SetVector("_WardCenter",Centre);block.SetFloat("_Radius",profile.WardRadius);block.SetFloat("_EffectTime",clock);block.SetFloat("_Progress",grow);block.SetFloat("_Dissolve",1-fade);block.SetVector("_HitPoint",p.Hit);block.SetFloat("_HitAge",clock-p.HitAt);
     float opacity=r.name=="GroundFollowingMembrane"?(profile.WardKind==Vfx120WardKind.Earth?1:.24f):1;block.SetFloat("_Opacity",opacity*fade);block.SetFloat("_WardOpacity",.38f*grow*fade);r.SetPropertyBlock(block);
    }
    p.Pattern?.Sample(clock);
    if(p.Pattern!=null)p.Pattern.gameObject.SetActive(visible);
   }
   if(ambient!=null)foreach(var ps in ambient){var em=ps.emission;em.enabled=clock>.2f&&clock<profile.Duration-.5f;ps.Simulate(clock,false,true,false);if(Ended)ps.Clear();}
   if(!released&&clock>=profile.Duration-profile.WardFade&&!Ended)
   {
    released=true;foreach(var p in panels)if(p.Grounded){var go=Instantiate(profile.WardDebrisPrefab,root.transform);go.name="WardReleaseFragments";go.transform.position=p.Foot+Vector3.up*.45f;var systems=go.GetComponentsInChildren<ParticleSystem>();foreach(var ps in systems)ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);contacts.Add(new Contact{At=clock,Root=go,Particles=systems});}
   }
   for(int i=contacts.Count-1;i>=0;i--)
   {var c=contacts[i];float t=clock-c.At;if(t>=.6f||Ended){Remove(c.Root);contacts.RemoveAt(i);continue;}c.Pattern?.Sample(t);foreach(var ps in c.Particles)ps.Simulate(Mathf.Max(0,t),false,true,false);}
   if(Ended&&root.activeSelf)root.SetActive(false);
  }
  void ForCamera(ScriptableRenderContext context,Camera camera)
  {
   if(disposed||root==null||panels==null||camera==null)return;
   if(profile.WardKind==Vfx120WardKind.Water)
   {
    float fade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(profile.Duration-profile.WardFade,profile.Duration,clock));
    float exterior=CameraExterior(camera.transform.position);
    foreach(var p in panels)
    {
     p.Pattern?.SetViewOpacity(Mathf.Lerp(.3f,1,exterior));
     foreach(var r in p.Renderers)if(r.sharedMaterial==waterMaterial)
     {
      r.GetPropertyBlock(block);block.SetFloat("_WardOpacity",.38f*InternalOpacity(camera.transform.position)*fade*Mathf.Clamp01(clock/profile.WardFormation));
      block.SetFloat("_Refration_Intensity",Mathf.Lerp(.0015f,waterMaterial.GetFloat("_Refration_Intensity"),exterior));r.SetPropertyBlock(block);
     }
    }
   }
  }
  static void Remove(UnityEngine.Object o){if(o==null)return;if(Application.isPlaying)Destroy(o);else DestroyImmediate(o);}
  public void Dispose(){if(disposed)return;disposed=true;RenderPipelineManager.beginCameraRendering-=ForCamera;if(root!=null){root.SetActive(false);Remove(root);}foreach(var mesh in meshes)Remove(mesh);meshes.Clear();contacts.Clear();ids.Clear();}
  void OnDestroy()=>Dispose();
 }
}
