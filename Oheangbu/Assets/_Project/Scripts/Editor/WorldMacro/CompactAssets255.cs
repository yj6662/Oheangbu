using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static class CompactAssets255
 {
  const string Folder="Assets/_Project/Audio/Compact255";
  const string Candidate="Assets/_Project/Art/World/WorldCompact/Rebuild/slice-5e82ecd76d2a/W_Demo_Compact_MigrationCheck.unity";
  public static string Run(string command)
  {
   if(command=="title")return Title();
   if(command=="install")return Install();
   if(command=="audit")return Audit();
   if(command=="runtime")return Runtime();
   throw new ArgumentException(command);
  }
  static T Asset<T>(string name,T template=null) where T:ScriptableObject
  {
   var path=Folder+"/"+name+".asset";var value=AssetDatabase.LoadAssetAtPath<T>(path);
   if(value==null){value=template!=null?Object.Instantiate(template):ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(value,path);}return value;
  }
  static string Install()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
   EditorSceneManager.OpenScene(Candidate);
   var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
   var ui=Object.FindFirstObjectByType<PlaytestUiRoot>();
   var audio=Object.FindFirstObjectByType<WorldMacroPlaytestAudio>();
   if(session==null||ui==null||audio==null)throw new Exception("Candidate audio/UI owner missing");
   var palette=Asset<CompactSoundPalette255>("Palette");palette.Mix=audio.Profile.Mix;
   var entries=new List<CompactSoundPalette255.Entry>();
   foreach(string path in Directory.GetFiles(Folder,"*.wav").OrderBy(x=>x))
   {
    var clean=path.Replace('\\','/');AssetDatabase.ImportAsset(clean);
    var importer=(AudioImporter)AssetImporter.GetAtPath(clean);var sample=importer.defaultSampleSettings;
    bool changed=sample.loadType!=AudioClipLoadType.DecompressOnLoad||sample.compressionFormat!=AudioCompressionFormat.PCM||sample.sampleRateSetting!=AudioSampleRateSetting.PreserveSampleRate||!importer.forceToMono||importer.loadInBackground;
    sample.loadType=AudioClipLoadType.DecompressOnLoad;sample.compressionFormat=AudioCompressionFormat.PCM;sample.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;
    importer.defaultSampleSettings=sample;importer.forceToMono=true;importer.loadInBackground=false;if(changed)importer.SaveAndReimport();
    string id=Path.GetFileNameWithoutExtension(path);bool loop=id.EndsWith("_loop");
    bool local=id.StartsWith("ui_")||new[]{"equip","unequip","purchase","upgrade","settings_tick","lock_on","lock_off","misfire","map_reveal","wind_loop","vehicle_call_fail","respawn","player_death"}.Contains(id);
    entries.Add(new CompactSoundPalette255.Entry{Id=id,Loop=loop,Cue=new WorldMacroPlaytestAudioProfileSO.Cue{Clip=AssetDatabase.LoadAssetAtPath<AudioClip>(clean),Volume=.75f,SpatialBlend=local?0:1,Cooldown=id=="map_reveal"?2f:id.StartsWith("step_")?.15f:.08f,MaxConcurrent=loop?1:3,AttackSeconds=loop?.16f:.016f,ReleaseSeconds=loop?.18f:.06f}});
   }
   palette.Entries=entries.ToArray();if(entries.Count!=69||entries.Any(e=>e.Cue.Clip==null))throw new Exception("Expected 69 imported clips");
   var profile=Asset<WorldMacroPlaytestAudioProfileSO>("Gameplay",audio.Profile);profile.ExtendedPalette=palette;
   foreach(var field in typeof(WorldMacroPlaytestAudioProfileSO).GetFields())
   {
    if(field.FieldType!=typeof(WorldMacroPlaytestAudioProfileSO.Cue))continue;
    string id=System.Text.RegularExpressions.Regex.Replace(field.Name,"([a-z])([A-Z])","$1_$2").ToLowerInvariant();
    if(field.Name=="SaveFailed")id="ui_error";if(field.Name=="Waiting")id="interact";
    var cue=palette.Find(id);if(cue!=null)field.SetValue(profile,cue);
   }
   audio.ConfigureProfile(profile);
   var theme=Asset<PlaytestUiThemeSO>("Theme",ui.Theme);theme.SoundPalette=palette;theme.AudioMix=palette.Mix;
   theme.PaperSound=palette.Find("ui_paper").Clip;theme.ConfirmSound=palette.Find("ui_confirm").Clip;theme.BackSound=palette.Find("ui_back").Clip;ui.Theme=theme;
   var bridge=session.GetComponent<CompactSoundscape255>();if(bridge==null)bridge=session.gameObject.AddComponent<CompactSoundscape255>();
   bridge.Palette=palette;bridge.Session=session;bridge.Map=ui.MapData;
   bridge.Hearths=Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(t=>t.name.Contains("Hearth")||t.name.Contains("Campfire")).ToArray();
   var caves=Object.FindObjectsByType<CompactCaveAmbience>(FindObjectsInactive.Include,FindObjectsSortMode.None).OrderBy(c=>c.name).ToArray();
   for(int i=0;i<caves.Length;i++)
   {
    bool drip=caves[i].name.Contains("water")||caves[i].name.Contains("drip");
    var source=caves[i].GetComponent<AudioSource>();source.clip=palette.Find(drip?"cave_drip":"cave_air_loop").Clip;
    caves[i].Intermittent=drip;caves[i].Mix=palette.Mix;caves[i].Level=.28f;source.outputAudioMixerGroup=palette.Mix!=null?palette.Mix.Sfx:null;EditorUtility.SetDirty(caves[i]);
   }
   foreach(var o in new Object[]{palette,profile,theme,ui,audio,bridge})EditorUtility.SetDirty(o);
   EditorSceneManager.MarkSceneDirty(session.gameObject.scene);EditorSceneManager.SaveScene(session.gameObject.scene);AssetDatabase.SaveAssets();
   return Audit();
  }
  static string Title()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
   string path="Assets/_Project/Scenes/World/W_Demo_Compact_Title.unity";
   string backup="../Art/Audio/Compact255/title-before.unity";if(!File.Exists(backup))File.Copy(path,backup);
   EditorSceneManager.OpenScene(path);var ui=Object.FindFirstObjectByType<PlaytestUiRoot>();
   if(ui==null)throw new Exception("Title UI missing");
   var theme=Asset<PlaytestUiThemeSO>("TitleTheme",ui.Theme);var palette=AssetDatabase.LoadAssetAtPath<CompactSoundPalette255>(Folder+"/Palette.asset");
   theme.SoundPalette=palette;theme.AudioMix=palette.Mix;theme.PaperSound=palette.Find("ui_paper").Clip;theme.ConfirmSound=palette.Find("ui_confirm").Clip;theme.BackSound=palette.Find("ui_back").Clip;
   ui.Theme=theme;EditorUtility.SetDirty(theme);EditorUtility.SetDirty(ui);EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);EditorSceneManager.SaveScene(ui.gameObject.scene);AssetDatabase.SaveAssets();
   EditorSceneManager.OpenScene(Candidate);return "Compact title UI cue palette attached; scene links and visual theme preserved";
  }
  static string Audit()
  {
   var palette=AssetDatabase.LoadAssetAtPath<CompactSoundPalette255>(Folder+"/Palette.asset");
   if(palette==null)throw new Exception("Missing palette");
   var report=new List<string>{"Edit-mode attachment audit; this is not gameplay or listening approval.","scene="+UnityEngine.SceneManagement.SceneManager.GetActiveScene().path};
   foreach(var e in palette.Entries){if(e.Cue.Clip==null||e.Cue.Clip.length<=0)throw new Exception(e.Id);report.Add(e.Id+" | "+e.Cue.Clip.length.ToString("F3")+"s | "+AssetDatabase.GetAssetPath(e.Cue.Clip));}
   var bridge=Object.FindFirstObjectByType<CompactSoundscape255>();var ui=Object.FindFirstObjectByType<PlaytestUiRoot>();var audio=Object.FindFirstObjectByType<WorldMacroPlaytestAudio>();
   if(bridge==null||bridge.Palette!=palette||ui.Theme.SoundPalette!=palette||audio.Profile.ExtendedPalette!=palette)throw new Exception("Attachment mismatch");
   report.Add("PASS 69 clips / candidate gameplay + UI + world bridge; hearths="+bridge.Hearths.Length+"; cave emitters="+Object.FindObjectsByType<CompactCaveAmbience>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length);
   foreach(var source in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include,FindObjectsSortMode.None))
    report.Add("SOURCE "+source.name+" active="+source.isActiveAndEnabled+" clip="+(source.clip!=null?AssetDatabase.GetAssetPath(source.clip):"none")+" mixer="+(source.outputAudioMixerGroup!=null?source.outputAudioMixerGroup.name:"none"));
   string result=string.Join("\n",report);File.WriteAllText("../Art/Audio/Compact255/attachment.txt",result);return result;
  }
  static string Runtime()
  {
   var bridge=Object.FindFirstObjectByType<CompactSoundscape255>();var audio=Object.FindFirstObjectByType<WorldMacroPlaytestAudio>();
   if(!EditorApplication.isPlaying||bridge==null)throw new Exception("Candidate Play required");
   string result="world="+bridge.Played+"; last="+bridge.LastCue+"; "+string.Join(",",bridge.Counts.Select(p=>p.Key+"="+p.Value))+"\n"+audio.RuntimeCounters;
   File.WriteAllText("../Art/Audio/Compact255/runtime.txt",result);return result;
  }
 }
}
