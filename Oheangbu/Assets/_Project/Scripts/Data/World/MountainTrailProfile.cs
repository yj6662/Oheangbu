using UnityEngine;
namespace Oheangbu.Data.World {
 [CreateAssetMenu(menuName="Oheangbu/World/Mountain Trail Profile")]
 public sealed class MountainTrailProfile:ScriptableObject {
  public Vector3[] points,knots;
  public float[] widths,distances;
  public int[] types;
  public float length,rise,bridgeStart,bridgeEnd;
  public Vector3 At(float distance){float t=Mathf.Clamp01(distance/length)*(points.Length-1);int i=Mathf.Min(points.Length-2,Mathf.FloorToInt(t));return Vector3.Lerp(points[i],points[i+1],t-i);}
  public float Width(float distance){float t=Mathf.Clamp01(distance/length)*(points.Length-1);int i=Mathf.Min(points.Length-2,Mathf.FloorToInt(t));return Mathf.Lerp(widths[i],widths[i+1],t-i);}
  public Vector3 Right(float distance){var d=At(Mathf.Min(length,distance+.15f))-At(Mathf.Max(0,distance-.15f));d.y=0;return new Vector3(d.z,0,-d.x).normalized;}
 }
}
