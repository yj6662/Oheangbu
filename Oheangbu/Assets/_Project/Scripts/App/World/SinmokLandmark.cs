using UnityEngine;
using Oheangbu.Combat;
namespace Oheangbu.App.World
{
 public sealed class SinmokLandmark:MonoBehaviour
 {
  public EnemyVitals Vitals;public GameObject Remnant;
  void Update(){if(Remnant!=null&&Vitals!=null)Remnant.SetActive(!Vitals.IsAlive);}
 }
}
