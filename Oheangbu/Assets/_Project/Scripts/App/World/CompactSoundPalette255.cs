using System;
using UnityEngine;
namespace Oheangbu.App.World
{
 [CreateAssetMenu(menuName="Oheangbu/Compact/Sound Palette")]
 public sealed class CompactSoundPalette255:ScriptableObject
 {
  [Serializable] public sealed class Entry {public string Id;public WorldMacroPlaytestAudioProfileSO.Cue Cue;public bool Loop;}
  public Entry[] Entries=Array.Empty<Entry>();
  public WorldMacroAudioMixProfileSO Mix;
  public WorldMacroPlaytestAudioProfileSO.Cue Find(string id)
  {foreach(var entry in Entries)if(entry.Id==id)return entry.Cue;return null;}
 }
}
