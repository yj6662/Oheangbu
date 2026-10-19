using UnityEngine;
namespace Oheangbu.App.SpellVFX120
{
 public static class Vfx120LaunchPoint
 {
  // Presentation origin only. Target, damage schedule and recognition input stay unchanged.
  public static Vector3 Resolve(Vfx120Profile profile,Camera camera,Vector3 fallback)
  {
   // #308 (D308-83a): a cast that leaves a swing starts at the swung brush tip, whatever the profile says (no swing ink = unchanged)
   if(World.SwingInk308.TryOrigin(out var tip))return tip;
   if(profile==null||!profile.CameraOffsetLaunch||camera==null)return fallback;
   float depth=Mathf.Max(camera.nearClipPlane+.15f,profile.LaunchDepth);
   return camera.ViewportToWorldPoint(new Vector3(profile.LaunchViewport.x,profile.LaunchViewport.y,depth));
  }
 }
}
