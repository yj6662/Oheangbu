using System;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static WorldMacroAudioOutputProbe journeyProbe;
  static string JourneyAudioRecord(){
   RoadRestSession();if(journeyProbe!=null)throw new Exception("Recording already active");
   var listener=UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Single(l=>l.isActiveAndEnabled);
   journeyProbe=listener.gameObject.AddComponent<WorldMacroAudioOutputProbe>();journeyProbe.Arm(AudioSettings.outputSampleRate,30);return "Recording actual listener output, up to 30 seconds";
  }
  static string JourneyAudioFinish(){
   if(journeyProbe==null)throw new Exception("Start recording first");if(!journeyProbe.StopAndCopy(out var samples))return "Audio callback completing; retry finish";
   int channels=journeyProbe.Channels,rate=journeyProbe.SampleRate;if(channels<1)throw new Exception("No audio callbacks");
   float peak=0;double sum=0;foreach(float value in samples){peak=Mathf.Max(peak,Mathf.Abs(value));sum+=value*value;}
   using(var w=new BinaryWriter(File.Create("../Art/World/PineRest/journey_audio.wav"))){
    w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));w.Write(36+samples.Length*2);w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)channels);w.Write(rate);w.Write(rate*channels*2);w.Write((short)(channels*2));w.Write((short)16);w.Write(System.Text.Encoding.ASCII.GetBytes("data"));w.Write(samples.Length*2);foreach(float value in samples)w.Write((short)(Mathf.Clamp(value,-1,1)*32767));
   }
   string report="samples="+samples.Length+" channels="+channels+" rate="+rate+" peak="+peak+" rms="+Math.Sqrt(sum/Math.Max(1,samples.Length))+"; "+JourneyAudioState();
   File.WriteAllText("../Art/World/PineRest/audio_output.txt",report);UnityEngine.Object.Destroy(journeyProbe);journeyProbe=null;return report;
  }
  static string JourneyAudio(){
   var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey Edit Mode required");
   const string profilePath=Folder+"/AudioFeedback.asset";
   var profile=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestAudioProfileSO>(profilePath);
   if(profile==null){profile=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestAudioProfileSO>("Assets/_Project/Audio/PlaytestPolish/AudioInk/AudioFeedback.asset"));profile.name="Journey Audio Feedback";AssetDatabase.CreateAsset(profile,profilePath);}
   profile.Waiting=new WorldMacroPlaytestAudioProfileSO.Cue{Clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/JourneyRenewal/ui_paper.wav"),Volume=.3f,Cooldown=.2f};
   profile.SaveFailed=new WorldMacroPlaytestAudioProfileSO.Cue{Clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/JourneyRenewal/ui_back.wav"),Volume=.45f,Cooldown=.3f};
   var session=UnityEngine.Object.FindFirstObjectByType<PrologueSession>();var audio=session.GetComponent<WorldMacroPlaytestAudio>()??session.gameObject.AddComponent<WorldMacroPlaytestAudio>();
   var so=new SerializedObject(audio);
   void Set(string field,UnityEngine.Object value){so.FindProperty(field).objectReferenceValue=value;}
   Set("_profile",profile);Set("_prologue",session);Set("_session",null);Set("_wiring",session.Wiring);
   Set("_drawing",session.Player.GetComponentInChildren<DrawingInputController>(true));Set("_brushAdapter",UnityEngine.Object.FindFirstObjectByType<BrushStrokeFeedAdapter>());
   Set("_playerVitals",session.Player.GetComponent<PlayerVitals>());Set("_harvest",session.Player.GetComponent<HarvestAction>());so.ApplyModifiedPropertiesWithoutUndo();
   EditorUtility.SetDirty(profile);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);return "Journey-owned AudioInk profile and real gameplay/result events connected";
  }
  static string JourneyAudioState(){
   var audio=UnityEngine.Object.FindFirstObjectByType<WorldMacroPlaytestAudio>();if(audio==null)throw new Exception("Journey audio missing");
   string report="focused="+Application.isFocused+"; listeners="+UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(l=>l.isActiveAndEnabled)+"; voices="+audio.VoiceCount+"; "+audio.RuntimeCounters;
   File.WriteAllText("../Art/World/PineRest/audio_runtime.txt",report);return report;
  }
 }
}
