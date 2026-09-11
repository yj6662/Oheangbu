using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace Oheangbu.App.SpellVFX120
{
 public sealed partial class Vfx120Effect
 {
  sealed class GroundBranch
  {
   public Vector3[] Centre,Left,Right,Vertices;
   public bool[] Valid;
   public Mesh Mesh;
   public MeshRenderer Renderer;
   public float Start,End;
  }
  ProceduralRiftPath _naturalPath;
  GroundBranch[] _groundBranches;
  public int RiftGroundQueries {get;private set;}
  public int RiftMissingGround {get;private set;}
  public int RiftVisibleBranches {get;private set;}
  public const float RiftSurfaceOffset=.012f;
  bool RiftGround(ref Vector3 point)
  {
   RiftGroundQueries++;
   int mask=LayerMask.GetMask("Default","WorldGround","WorldRidge","WorldRibbon");
   int n=Physics.RaycastNonAlloc(point+Vector3.up*16,Vector3.down,_groundHits,48,mask,QueryTriggerInteraction.Ignore);
   float nearest=float.PositiveInfinity;bool found=false;
   for(int i=0;i<n;i++)
   {
    var h=_groundHits[i];if(h.normal.y<.3f||h.distance>=nearest||h.collider is CharacterController||h.collider.transform.IsChildOf(transform)||h.collider.GetComponentInParent<Oheangbu.Combat.EnemyVitals>()!=null)continue;
    nearest=h.distance;point.y=h.point.y+RiftSurfaceOffset;found=true;
   }
   if(!found)RiftMissingGround++;
   return found;
  }
  void BuildProceduralRift()
  {
   var plan=ReceivedAreaPlan;_naturalPath=new ProceduralRiftPath(plan.VisualSeed,plan.Length,plan.Radius);
   _groundBranches=new GroundBranch[7];RiftGroundQueries=RiftMissingGround=0;
   for(int lane=0;lane<7;lane++)
   {
    var path=_naturalPath.Branches[lane];var line=_riftLines[lane];line.enabled=false;
    // Dense cached samples, including both ribbon edges. No runtime raycasts or collider creation.
    int count=Mathf.Clamp(Mathf.CeilToInt((path.End-path.Start).magnitude/.10f)+1,8,256);
    var b=new GroundBranch{Start=path.Start.y,End=path.End.y,Centre=new Vector3[count],Left=new Vector3[count],Right=new Vector3[count],Valid=new bool[count],Vertices=new Vector3[count*2]};
    var go=new GameObject("ProceduralRift_"+lane);go.transform.SetParent(_areaHost.transform,false);go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
    b.Mesh=new Mesh{name="GroundRift_"+lane};b.Mesh.MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=b.Mesh;
    b.Renderer=go.AddComponent<MeshRenderer>();b.Renderer.sharedMaterial=line.sharedMaterial;b.Renderer.shadowCastingMode=ShadowCastingMode.Off;b.Renderer.receiveShadows=false;
    var colours=new Color[count*2];var triangles=new List<int>();
    for(int i=0;i<count;i++)
    {
     float u=i/(count-1f),along=Mathf.Lerp(b.Start,b.End,u);var p=path.At(along);
     var tangent=path.At(Mathf.Min(b.End,along+.02f))-path.At(Mathf.Max(b.Start,along-.02f));
     var across=(_areaRight*tangent.y-_areaForward*tangent.x).normalized;
     var centre=_areaStart+_areaRight*p.x+_areaForward*p.y;
     float half=path.Width*Mathf.Lerp(1,.55f,u)*.5f;
     var left=centre-across*half;var right=centre+across*half;
     bool c=RiftGround(ref centre),l=RiftGround(ref left),r=RiftGround(ref right);
     b.Centre[i]=centre;b.Left[i]=left;b.Right[i]=right;b.Valid[i]=c&&l&&r;
     b.Vertices[i*2]=left;b.Vertices[i*2+1]=right;
     colours[i*2]=colours[i*2+1]=Color.Lerp(new Color(.55f,.31f,.10f),new Color(.30f,.16f,.06f),u);
     // Missing ground or a vertical discontinuity breaks the strip instead of bridging empty space.
     if(i>0&&b.Valid[i]&&b.Valid[i-1]&&Mathf.Abs(b.Centre[i].y-b.Centre[i-1].y)<.35f)
     {int a=(i-1)*2;triangles.AddRange(new[]{a,a+2,a+1,a+1,a+2,a+3});}
    }
    b.Mesh.vertices=b.Vertices;b.Mesh.colors=colours;b.Mesh.SetTriangles(triangles,0);b.Mesh.RecalculateBounds();b.Renderer.enabled=false;_groundBranches[lane]=b;
   }
  }
  void SampleProceduralRift(float time,float fade)
  {
   float front=(time-ReceivedAreaPlan.Delay)*ReceivedAreaPlan.Speed;RiftVisibleBranches=0;
   foreach(var b in _groundBranches)
   {
    b.Renderer.enabled=front>b.Start+.02f&&fade>0;if(b.Renderer.enabled)RiftVisibleBranches++;
    float head=Mathf.Clamp01((front-b.Start)/(b.End-b.Start))*(b.Centre.Length-1);
    int index=Mathf.Min(b.Centre.Length-2,(int)head);float f=head-index;
    Vector3 left=Vector3.Lerp(b.Left[index],b.Left[index+1],f),right=Vector3.Lerp(b.Right[index],b.Right[index+1],f);
    for(int i=0;i<b.Centre.Length;i++)
    {
     var l=i<=head?b.Left[i]:left;var r=i<=head?b.Right[i]:right;
     var middle=(l+r)*.5f;b.Vertices[i*2]=Vector3.Lerp(middle,l,fade);b.Vertices[i*2+1]=Vector3.Lerp(middle,r,fade);
    }
    b.Mesh.vertices=b.Vertices;b.Mesh.RecalculateBounds();
   }
  }
  bool ProceduralRiftParticle(int seedLane,float along,out Vector3 point)
  {
   int lane=_naturalPath.Lane(seedLane,along);var b=_groundBranches[lane];
   float u=Mathf.InverseLerp(b.Start,b.End,along)*(b.Centre.Length-1);int i=Mathf.Min(b.Centre.Length-2,(int)u);
   point=Vector3.Lerp(b.Centre[i],b.Centre[i+1],u-i);
   return along<=b.End&&b.Valid[i]&&b.Valid[i+1]&&Mathf.Abs(b.Centre[i].y-b.Centre[i+1].y)<.35f;
  }
  void ClearProceduralRift()
  {
   if(_groundBranches!=null)foreach(var b in _groundBranches)if(b.Mesh!=null){if(Application.isPlaying)Destroy(b.Mesh);else DestroyImmediate(b.Mesh);}
   _groundBranches=null;_naturalPath=null;RiftVisibleBranches=0;
  }
 }
}
