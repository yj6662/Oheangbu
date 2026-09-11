using UnityEngine;
namespace Oheangbu.App.SpellVFX120
{
 public sealed partial class Vfx120Effect
 {
  public static float BranchStart(int lane,float length)=>lane==0?0:lane<3?length*.3f:length*.62f;
  public static float BranchEnd(int lane,float length)=>lane==0?length*.3f:lane<3?length*.62f:length;
  public static int BranchParticleLane(int seedLane,float along,float length)=>along<length*.3f?0:along<length*.62f?(seedLane%4<2?1:2):3+seedLane%4;
  public static float BranchAcross(int lane,float along,float length)
  {
   float u=Mathf.InverseLerp(BranchStart(lane,length),BranchEnd(lane,length),along);
   float start=lane<3?0:lane<5?-.35f:.35f;
   float end=lane==0?0:lane==1?-.35f:lane==2?.35f:lane==3?-.95f:lane==4?-.3f:lane==5?.3f:.95f;
   return Mathf.Lerp(start,end,u)+Mathf.Sin(u*Mathf.PI)*Mathf.Sin(along*4+lane*2.7f)*.02f;
  }
  public static float RiftAcross(int lane,float along,float length)
  {
   float u=Mathf.Clamp01(along/Mathf.Max(.01f,length));
   return Mathf.Clamp((lane/3f-1)*(.4f+.55f*u)+Mathf.Sin(lane*2.7f+along*4)*.025f,-.98f,.98f);
  }
  void SampleEarthRift(float time,float fade)
  {
   if(_groundBranches!=null){SampleProceduralRift(time,fade);return;}
   if(_riftLines==null)return;var p=ReceivedAreaPlan;
   float front=Mathf.Clamp((time-p.Delay)*p.Speed,0,p.Length);
   for(int lane=0;lane<_riftLines.Length;lane++)
   {
    var line=_riftLines[lane];
    float start=Profile.BranchedEarthRift?BranchStart(lane,p.Length):0,end=Profile.BranchedEarthRift?BranchEnd(lane,p.Length):p.Length;
    line.enabled=front>start+.02f&&fade>0;
    if(Profile.BranchedEarthRift){line.startWidth=lane==0?.46f:lane<3?.24f:.12f;line.endWidth=lane==0?.28f:lane<3?.13f:.055f;}
    for(int i=0;i<25;i++)
    {
     float along=Mathf.Clamp(Mathf.Lerp(start,end,i/24f),start,Mathf.Max(start,front));
     float across=Profile.BranchedEarthRift?BranchAcross(lane,along,p.Length):RiftAcross(lane,along,p.Length);
     var point=_areaStart+_areaForward*along+_areaRight*(across*p.Radius);point.y=AreaGround(along,across)+.025f;
     line.SetPosition(i,point);
    }
    line.startColor=new Color(.55f,.31f,.10f,.85f*fade);line.endColor=new Color(.3f,.16f,.06f,.7f*fade);
   }
  }
 }
}
