using UnityEngine;
namespace Oheangbu.App.World
{
 [RequireComponent(typeof(AudioSource))]
 public sealed class CompactMountainBell:MonoBehaviour
 {
  public AudioClip Clip;AudioSource source;float next;
  void Awake(){source=GetComponent<AudioSource>();source.clip=Clip;source.spatialBlend=1;source.minDistance=5;source.maxDistance=160;source.rolloffMode=AudioRolloffMode.Linear;source.volume=.18f;source.playOnAwake=false;next=Time.time+8;}
  void Update(){if(Clip==null||Time.time<next)return;next=Time.time+29;source.Play();}
 }
}
