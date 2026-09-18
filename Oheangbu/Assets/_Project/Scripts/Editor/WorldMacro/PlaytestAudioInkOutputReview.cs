using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Runs only after explicit Execute("begin"). Signals use production pools and mixer,
    // while gameplay remains paused; no real damage, recognition, extraction or save events.
    [InitializeOnLoad]
    public static class PlaytestAudioInkOutputReview
    {
        [Serializable] sealed class Region
        {public string name;public int fromSample,toSample;public double peakDbfs,rmsDbfs,maximumSampleStep;public int clippedSamples,zeroFrames;public string wav;}
        [Serializable] sealed class Report
        {
            public string status,scope="Real Unity listener OnAudioFilterRead PCM after actual mixer; synthetic isolated/maximum-overlap/harvest presentation signals. Not native input, OS/device loopback or subjective listening approval.";
            public int sampleRate,channels,callbacks,samples,peakGameVoices,peakUiVoices,concurrencyDrops,fadedSteals;
            public double dspSeconds,recordedSeconds,maximumCallbackIntervalSeconds,maximumPeakDbfs,poolTickIntervalSeconds;
            public bool mixerMuteCalibration,cleanup;
            public List<Region> regions=new List<Region>();public List<string> failures=new List<string>();
        }
        static bool repairPass;
        static double lastPoolTick;
        static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,repairPass
            ? "../../Art/PlaytestRecovery/AudioRepair/UnityOutput" : "../../Art/PlaytestPolish/AudioInk/UnityOutput"));
        static bool active,finishing,oldAudioEnabled,oldUiEnabled,oldRun,oldListenerPause,pauseBegan;
        static float oldListenerVolume;
        static readonly string[] keys={"MasterVolumeDb","SfxVolumeDb","UiVolumeDb"};
        static readonly float[] oldMix=new float[3];
        static WorldMacroPlaytestAudio audio;static WorldMacroUiAudioVoices uiVoices;static PlaytestUiRoot ui;
        static WorldMacroPlaytestAudioProfileSO profile;static WorldMacroAudioMixProfileSO mix;
        static WorldMacroAudioOutputProbe probe;static GameObject owned;static WorldMacroAudioVoicePool gamePool,uiPool;
        static AudioSource[] pausedSources;static bool[] sourceWasPlaying;
        static WorldMacroPlaytestAudioProfileSO.Cue uiConfirm,uiPaper;
        static Report report;static int phase;static double beganDsp,phaseAt,deadline,nextStress;static Vector3 point;
        static PlaytestAudioInkOutputReview(){AssemblyReloadEvents.beforeAssemblyReload+=()=>{if(active)AbortForReload();};}
        public static string Execute(string action)
        {
            if(action=="poll")return active?"RUNNING output phase "+phase:(File.Exists(Output+"/listener_output.json")?File.ReadAllText(Output+"/listener_output.json"):"NOT_RUN");
            if(action=="abort"){if(active){report.failures.Add("Aborted explicitly");finishing=true;}return "Output review stopping";}
            if(action!="begin" && action!="begin-lowfps")throw new ArgumentException("Output review: begin | begin-lowfps | poll | abort");
            if(active||!EditorApplication.isPlaying||EditorApplication.isPaused)throw new InvalidOperationException("Unpaused existing Play mode required");
            var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();ui=PlaytestUiRoot.Instance;
            audio=Object.FindFirstObjectByType<WorldMacroPlaytestAudio>();uiVoices=Object.FindFirstObjectByType<WorldMacroUiAudioVoices>();
            if(session==null||string.IsNullOrWhiteSpace(session.TestSaveSuffix))throw new InvalidOperationException("Isolated TestSaveSuffix required; no production-save tests");
            if(ui==null||ui.Pause==null||ui.Pause.IsPaused||session.Walker==null||session.Walker.Drawing.InDrawMode||session.Walker.Seated)throw new InvalidOperationException("Begin while idle, unseated, outside drawing and menus");
            if(audio==null||audio.ActiveVoiceCount!=0||audio.Profile?.Mix==null||!audio.Profile.Mix.IsReady)throw new InvalidOperationException("Prepared audio, real mixer and idle voices required");
            var listeners=Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Where(x=>x.isActiveAndEnabled).ToArray();
            if(listeners.Length!=1)throw new InvalidOperationException("Exactly one active listener required");
            // Same commit-ratio measurement as FixedWardReview.Memory(); sourced from the shared
            // guard so WorldMacro does not depend on the SpellVFX120 assembly.
            if(Oheangbu.EditorTools.Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit at least 85%; output capture not started");
            profile=audio.Profile;mix=profile.Mix;mix.FlushPending();
            for(int i=0;i<3;i++)if(!mix.Mixer.GetFloat(keys[i],out oldMix[i]))throw new InvalidOperationException("Mixer parameter missing "+keys[i]);
            repairPass=action=="begin-lowfps";lastPoolTick=double.NegativeInfinity;
            report=new Report{status="RUNNING",sampleRate=AudioSettings.outputSampleRate,poolTickIntervalSeconds=repairPass?.1:0};
            if(repairPass) report.scope+=" Voice-pool housekeeping is deliberately ticked at 10 Hz; audio envelopes must still be continuous. This does not simulate a blocked OS audio driver.";
            oldAudioEnabled=audio.enabled;oldUiEnabled=uiVoices!=null&&uiVoices.enabled;oldRun=Application.runInBackground;
            oldListenerVolume=AudioListener.volume;oldListenerPause=AudioListener.pause;
            pausedSources=Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            sourceWasPlaying=pausedSources.Select(x=>x.isPlaying).ToArray();active=true;finishing=false;pauseBegan=false;
            try
            {
                ui.Pause.Begin();pauseBegan=true;audio.enabled=false;if(uiVoices!=null)uiVoices.enabled=false;
                for(int i=0;i<pausedSources.Length;i++)if(sourceWasPlaying[i])pausedSources[i].Pause();
                Application.runInBackground=true;AudioListener.pause=false;AudioListener.volume=1;
                mix.Mixer.SetFloat(keys[0],mix.MasterHeadroomDb);mix.Mixer.SetFloat(keys[1],-80);mix.Mixer.SetFloat(keys[2],-80);
                owned=new GameObject("Temporary_AudioInkOutput_QA"){hideFlags=HideFlags.HideAndDontSave};
                gamePool=new WorldMacroAudioVoicePool(owned.transform,12,2,true,"QA_Game_");
                uiPool=new WorldMacroAudioVoicePool(owned.transform,4,0,true,"QA_UI_");
                uiConfirm=new WorldMacroPlaytestAudioProfileSO.Cue{Clip=ui.Theme.ConfirmSound,Volume=.6f,MaxConcurrent=2,AttackSeconds=.018f,ReleaseSeconds=.06f};
                uiPaper=new WorldMacroPlaytestAudioProfileSO.Cue{Clip=ui.Theme.PaperSound,Volume=.55f,MaxConcurrent=2,AttackSeconds=.018f,ReleaseSeconds=.06f};
                probe=listeners[0].gameObject.AddComponent<WorldMacroAudioOutputProbe>();probe.hideFlags=HideFlags.DontSave;
                probe.Arm(report.sampleRate,24);point=listeners[0].transform.position+listeners[0].transform.forward*.5f;
                beganDsp=AudioSettings.dspTime;deadline=EditorApplication.timeSinceStartup+35;phase=-1;
                Advance();EditorApplication.update+=Tick;
                return "RUNNING: listener output capture; paused gameplay; production mixer and bounded voice pools; about 10 seconds";
            }
            catch(Exception e){report.failures.Add(e.ToString());AbortForReload();throw;}
        }
        static void Tick()
        {
            if(!active)return;
            try
            {
                if(!EditorApplication.isPlaying||EditorApplication.timeSinceStartup>deadline){report.failures.Add("Play ended or output deadline exceeded");finishing=true;}
                if(finishing){Finish();return;}
                double now=AudioSettings.dspTime;
                if(now-lastPoolTick>=report.poolTickIntervalSeconds)
                {gamePool.Tick();uiPool.Tick();lastPoolTick=now;}
                report.peakGameVoices=Math.Max(report.peakGameVoices,gamePool.Sources.Count(x=>x.isPlaying));
                report.peakUiVoices=Math.Max(report.peakUiVoices,uiPool.Sources.Count(x=>x.isPlaying));
                if(phase==2&&now>=nextStress)
                {
                    gamePool.Play(profile.Impact,point,1,mix.Sfx);gamePool.Play(profile.CastMetal,point,1,mix.Sfx);
                    uiPool.Play(uiConfirm,point,1,mix.Ui);nextStress=now+.085;
                }
                if(now>=phaseAt)Advance();
            }
            catch(Exception e){report.failures.Add(e.ToString());finishing=true;}
        }
        static void Advance()
        {
            if(report.regions.Count>0)report.regions[report.regions.Count-1].toSample=probe.Samples;
            phase++;double now=AudioSettings.dspTime;string name;double duration;
            switch(phase)
            {
                case 0:name="mixer_muted_calibration";duration=.55;gamePool.Play(profile.CastMetal,point,1,mix.Sfx);break;
                case 1:
                    name="isolated_metal";duration=profile.CastMetal.Clip.length+.35;gamePool.StopAll(true);
                    mix.Mixer.SetFloat(keys[1],0);mix.Mixer.SetFloat(keys[2],0);gamePool.Play(profile.CastMetal,point,1,mix.Sfx);break;
                case 2:
                    name="maximum_role_limited_overlap";duration=2.5;gamePool.StopAll(true);
                    foreach(var cue in new[]{profile.CastWood,profile.CastFire,profile.CastEarth,profile.CastMetal,profile.CastWater})gamePool.Play(cue,point,1,mix.Sfx);
                    for(int i=0;i<3;i++)gamePool.Play(profile.Impact,point,1,mix.Sfx);
                    for(int i=0;i<2;i++)gamePool.Play(profile.PlayerHit,point,1,mix.Sfx);
                    gamePool.Play(profile.BrushStroke,point,1,mix.Sfx,0);gamePool.Play(profile.HarvestLoop,point,1,mix.Harvest,1,true);
                    for(int i=0;i<2;i++){uiPool.Play(uiConfirm,point,1,mix.Ui);uiPool.Play(uiPaper,point,1,mix.Ui);}nextStress=now+.085;break;
                case 3:name="overlap_release";duration=.3;gamePool.StopAll(false);uiPool.StopAll(false);break;
                case 4:
                    name="harvest_start_and_hold";duration=2.4;
                    gamePool.Play(profile.HarvestStart,point,1,mix.Harvest);gamePool.Play(profile.HarvestLoop,point,1,mix.Harvest,1,true);break;
                case 5:
                    name="harvest_release";duration=.8;gamePool.ReleaseSlot(1,.14f);gamePool.Play(profile.HarvestEnd,point,1,mix.Harvest);break;
                case 6:name="quiet_tail";duration=.35;gamePool.StopAll(false);break;
                default:finishing=true;Finish();return;
            }
            report.regions.Add(new Region{name=name,fromSample=probe.Samples});phaseAt=now+duration;
        }
        static void Finish()
        {
            if(!active)return;
            float[] pcm=Array.Empty<float>();if(probe!=null&&!probe.StopAndCopy(out pcm))return;
            report.dspSeconds=AudioSettings.dspTime-beganDsp;
            report.channels=probe!=null?probe.Channels:0;report.samples=pcm.Length;report.callbacks=probe!=null?probe.Callbacks:0;
            report.maximumCallbackIntervalSeconds=probe!=null?probe.MaximumCallbackIntervalSeconds:0;
            report.concurrencyDrops=gamePool?.ConcurrencyDrops??0;report.fadedSteals=gamePool?.FadedSteals??0;
            Directory.CreateDirectory(Output);
            if(report.channels<=0||pcm.Length<report.sampleRate)
                report.failures.Add("No usable AudioListener filter PCM. Actual output is unverified; no source-buffer or GetOutputData surrogate is reported as post-mix output.");
            else
            {
                report.recordedSeconds=pcm.Length/(double)(report.sampleRate*report.channels);
                if(Math.Abs(report.recordedSeconds-report.dspSeconds)>.3)report.failures.Add("DSP elapsed/sample duration mismatch exceeds .3 seconds");
                if(report.regions.Count>0&&report.regions.Last().toSample==0)report.regions.Last().toSample=pcm.Length;
                foreach(var region in report.regions)Analyze(pcm,region);
                var calibration=report.regions.FirstOrDefault(x=>x.name=="mixer_muted_calibration");var isolated=report.regions.FirstOrDefault(x=>x.name=="isolated_metal");
                report.mixerMuteCalibration=calibration!=null&&isolated!=null&&calibration.peakDbfs<-65&&isolated.peakDbfs>-55;
                if(!report.mixerMuteCalibration)report.failures.Add("Mute/unmute calibration did not demonstrate post-mixer capture");
                if(report.regions.Any(x=>x.clippedSamples>0))report.failures.Add("Listener output exceeded full scale");
                report.maximumPeakDbfs=report.regions.Max(x=>x.peakDbfs);
                SaveWave(Output+"/listener_full.wav",pcm,0,pcm.Length,report.channels,report.sampleRate);
            }
            Cleanup();report.status=report.failures.Count==0?"PASS_REAL_UNITY_OUTPUT":"FINDINGS";
            File.WriteAllText(Output+"/listener_output.json",JsonUtility.ToJson(report,true));
            string cards=string.Join("",report.regions.Select(x=>"<article><h2>"+x.name+"</h2><p>Peak "+x.peakDbfs.ToString("F2")+" dBFS · clipped samples "+x.clippedSamples+"</p><audio controls preload='none' src='"+x.wav+"'></audio></article>"));
            File.WriteAllText(Output+"/REVIEW.html","<!doctype html><meta charset='utf-8'><title>Unity 출력 검수</title><style>body{background:#e9e2d4;color:#302a24;font:18px sans-serif;max-width:1000px;margin:45px auto}article{border-top:1px solid #938577;padding:14px}</style><h1>실제 Unity 믹서 출력</h1><p>진단용 소리 신호로 재생했습니다. 실제 입력·공격·수급을 발생시키지 않았고, OS/스피커 출력 및 최종 청취 평가는 별도입니다. 자동 재생 없음.</p><p>"+report.status+"</p>"+cards);
        }
        static void Analyze(float[] pcm,Region region)
        {
            int start=Math.Max(0,region.fromSample/report.channels*report.channels),end=Math.Min(pcm.Length,region.toSample/report.channels*report.channels);
            double peak=0,power=0,step=0;int clipped=0,zeros=0;
            for(int i=start;i<end;i++){double v=Math.Abs(pcm[i]);peak=Math.Max(peak,v);power+=pcm[i]*pcm[i];if(v>=1)clipped++;if(i>=start+report.channels)step=Math.Max(step,Math.Abs(pcm[i]-pcm[i-report.channels]));}
            for(int i=start;i+report.channels<=end;i+=report.channels){bool zero=true;for(int c=0;c<report.channels;c++)if(Math.Abs(pcm[i+c])>1e-5f)zero=false;if(zero)zeros++;}
            region.peakDbfs=20*Math.Log10(Math.Max(1e-12,peak));region.rmsDbfs=10*Math.Log10(Math.Max(1e-24,power/Math.Max(1,end-start)));
            region.maximumSampleStep=step;region.clippedSamples=clipped;region.zeroFrames=zeros;region.wav=region.name+".wav";
            SaveWave(Output+"/"+region.wav,pcm,start,end-start,report.channels,report.sampleRate);
        }
        static void SaveWave(string path,float[] samples,int start,int count,int channels,int rate)
        {
            using(var file=new BinaryWriter(File.Create(path)))
            {
                file.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));file.Write(36+count*4);file.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                file.Write(16);file.Write((short)3);file.Write((short)channels);file.Write(rate);file.Write(rate*channels*4);file.Write((short)(channels*4));file.Write((short)32);
                file.Write(System.Text.Encoding.ASCII.GetBytes("data"));file.Write(count*4);for(int i=start;i<start+count;i++)file.Write(samples[i]);
            }
        }
        static void Cleanup()
        {
            EditorApplication.update-=Tick;active=false;
            var errors=new List<string>();void Attempt(Action a){try{a();}catch(Exception e){errors.Add(e.Message);}}
            Attempt(()=>gamePool?.Dispose());Attempt(()=>uiPool?.Dispose());gamePool=uiPool=null;
            Attempt(()=>{if(probe!=null)Object.DestroyImmediate(probe);});probe=null;
            Attempt(()=>{if(owned!=null)Object.DestroyImmediate(owned);});owned=null;
            Attempt(()=>{for(int i=0;i<3;i++)mix.Mixer.SetFloat(keys[i],oldMix[i]);});
            Attempt(()=>{AudioListener.volume=oldListenerVolume;AudioListener.pause=oldListenerPause;Application.runInBackground=oldRun;});
            Attempt(()=>{for(int i=0;i<pausedSources.Length;i++)if(sourceWasPlaying[i]&&pausedSources[i]!=null)pausedSources[i].UnPause();});
            Attempt(()=>{if(audio!=null)audio.enabled=oldAudioEnabled;if(uiVoices!=null)uiVoices.enabled=oldUiEnabled;});
            Attempt(()=>{if(pauseBegan&&ui!=null)ui.Pause.End();});pauseBegan=false;
            report.cleanup=errors.Count==0;report.failures.AddRange(errors.Select(x=>"Cleanup: "+x));
        }
        static void AbortForReload()
        {
            if(!active)return;report.failures.Add("Interrupted or assembly reload");
            if(probe!=null)probe.StopAndCopy(out _);Cleanup();report.status="ABORTED";
            Directory.CreateDirectory(Output);File.WriteAllText(Output+"/listener_output.json",JsonUtility.ToJson(report,true));
        }
    }
}
