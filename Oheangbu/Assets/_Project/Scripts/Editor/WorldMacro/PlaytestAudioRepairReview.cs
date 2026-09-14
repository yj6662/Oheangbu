using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class PlaytestAudioRepairReview
    {
        [Serializable] sealed class Report
        {
            public string status,scope;
            public bool play;
            public int activeListeners,outputRate,dspBufferFrames,dspBufferCount,envelopeSources;
            public List<string> checks=new List<string>(),failures=new List<string>();
        }
        public static string Execute(string command)
        {
            if(command!="audit")throw new ArgumentException("audio repair: audit; real output: PlaytestAudioInkOutputReview begin-lowfps/poll");
            var r=new Report{play=Application.isPlaying,scope="Read-only active scene, importer and mixer audit. No playback, device loopback or listening approval."};
            r.activeListeners=Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(x=>x.isActiveAndEnabled);
            r.outputRate=AudioSettings.outputSampleRate;AudioSettings.GetDSPBufferSize(out r.dspBufferFrames,out r.dspBufferCount);
            if(r.activeListeners!=1)r.failures.Add("Exactly one active listener required; actual="+r.activeListeners);
            var audio=Object.FindFirstObjectByType<WorldMacroPlaytestAudio>();
            if(audio==null||audio.Profile==null)r.failures.Add("Playtest audio/profile missing");
            else
            {
                var p=audio.Profile;
                r.checks.Add("active_profile="+AssetDatabase.GetAssetPath(p));r.checks.Add(audio.RuntimeCounters);
                if(p.Mix==null||!p.Mix.IsReady)r.failures.Add("Production mixer unavailable");
                else
                {
                    r.checks.Add("master_headroom_db="+p.Mix.MasterHeadroomDb);
                    foreach(var name in new[]{"MasterVolumeDb","SfxVolumeDb","UiVolumeDb"})
                        if(p.Mix.Mixer.GetFloat(name,out float value))r.checks.Add(name+"="+value);else r.failures.Add("Missing exposed mixer value "+name);
                }
                var clips=p.GetType().GetFields().Where(x=>x.FieldType==typeof(WorldMacroPlaytestAudioProfileSO.Cue))
                    .Select(x=>(WorldMacroPlaytestAudioProfileSO.Cue)x.GetValue(p)).Where(x=>x?.Clip!=null).Select(x=>x.Clip).Distinct();
                foreach(var clip in clips)
                {
                    string path=AssetDatabase.GetAssetPath(clip);var importer=AssetImporter.GetAtPath(path) as AudioImporter;
                    if(importer==null) {r.failures.Add("Missing importer "+path);continue;}
                    var settings=importer.defaultSampleSettings;
                    if(settings.compressionFormat!=AudioCompressionFormat.PCM||settings.loadType!=AudioClipLoadType.DecompressOnLoad||!settings.preloadAudioData)
                        r.failures.Add("Not preloaded PCM "+path);
                    else r.checks.Add("preloaded_pcm="+clip.name+"; sampleRate="+clip.frequency+"; channels="+clip.channels);
                }
            }
            foreach(var source in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .Where(x=>x.name.StartsWith("PlaytestAudioVoice_")||x.name.StartsWith("PlaytestUiVoice_")))
            {
                var envelope=source.GetComponent<WorldMacroAudioEnvelope>();
                if(envelope==null)r.failures.Add("Missing DSP envelope "+source.name);else r.envelopeSources++;
                if(source.isPlaying && source.volume!=1)r.failures.Add("Main source gain is not fixed at unity "+source.name);
                if(source.isPlaying && source.outputAudioMixerGroup==null)r.failures.Add("Active source bypasses mixer "+source.name);
                if(source.pitch!=1||source.dopplerLevel!=0)r.failures.Add("Unexpected pitch or Doppler "+source.name);
            }
            if(r.play&&r.envelopeSources<WorldMacroPlaytestAudio.VoiceLimit)r.failures.Add("Runtime voice pool not fully equipped");
            if(!r.play)r.checks.Add("Runtime source creation has not been inspected in this Edit-mode audit");
            r.status=r.failures.Count==0?"PASS_AUDIT":"FINDINGS";
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/AudioRepair"));Directory.CreateDirectory(output);
            string json=JsonUtility.ToJson(r,true);File.WriteAllText(Path.Combine(output,r.play?"runtime_audit.json":"edit_audit.json"),json);return json;
        }
    }
}
