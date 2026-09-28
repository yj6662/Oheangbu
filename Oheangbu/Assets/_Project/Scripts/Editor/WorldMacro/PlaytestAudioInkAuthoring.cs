using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Explicit entrypoints only. No InitializeOnLoad, scene regeneration, build or automatic playback.
    public static class PlaytestAudioInkAuthoring
    {
        public const string Folder="Assets/_Project/Audio/PlaytestPolish/AudioInk";
        public const string MixPath=Folder+"/PlaytestMix.asset", AudioPath=Folder+"/AudioFeedback.asset";
        public const string FlowPath=Folder+"/InkFlow.asset", ThemePath=Folder+"/UiTheme.asset";
        static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestPolish/AudioInk"));
        const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        [Serializable] sealed class Report { public string status,scope;public string[] checks,failures; }
        static T Load<T>(string p) where T:Object => AssetDatabase.LoadAssetAtPath<T>(p)??throw new InvalidOperationException("Missing "+p);
        static T Owned<T>(string p,T original=null) where T:ScriptableObject
        {
            var value=AssetDatabase.LoadAssetAtPath<T>(p);if(value!=null)return value;
            value=original!=null?Object.Instantiate(original):ScriptableObject.CreateInstance<T>();
            value.name=Path.GetFileNameWithoutExtension(p);AssetDatabase.CreateAsset(value,p);return value;
        }
        static void NeedEdit(){if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required; no live scene mutation.");}
        public static string Execute(string action)
        {
            switch(action)
            {
                case "prepare":return Prepare();
                case "apply-current":return ApplyCurrent();
                case "apply-title":return ApplyTitle();
                case "validate":return Validate();
                case "contracts":return Contracts();
                case "runtime":return Runtime();
                default:throw new ArgumentException("Commands: prepare, apply-current, apply-title, validate, contracts, runtime");
            }
        }
        public static string Prepare()
        {
            NeedEdit();Directory.CreateDirectory(Folder);Directory.CreateDirectory(Output);AssetDatabase.Refresh();
            foreach(string file in Directory.GetFiles("Assets/_Project/Audio/JourneyRenewal","*.wav"))
            {
                string path=file.Replace('\\','/');var importer=AssetImporter.GetAtPath(path) as AudioImporter;
                if(importer==null)throw new InvalidOperationException("Audio importer missing "+path);
                var sample=importer.defaultSampleSettings;sample.loadType=AudioClipLoadType.DecompressOnLoad;
                sample.compressionFormat=AudioCompressionFormat.PCM;sample.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;
                sample.preloadAudioData=true;importer.defaultSampleSettings=sample;importer.forceToMono=false;importer.loadInBackground=false;
                importer.SaveAndReimport();
            }
            var mix=Owned<WorldMacroAudioMixProfileSO>(MixPath);mix.Mixer=PrepareMixer();
            mix.Sfx=Group(mix.Mixer,"SFX");mix.Ui=Group(mix.Mixer,"UI");mix.Harvest=Group(mix.Mixer,"Harvest");mix.MasterHeadroomDb=-6;
            var audio=Owned(AudioPath,Load<WorldMacroPlaytestAudioProfileSO>("Assets/_Project/Audio/PlaytestFeedback/WorldMacroPlaytestAudioProfile.asset"));
            audio.Mix=mix;
            Set(audio.BrushStroke,"brush_stroke",.65f,0,.1f,1);
            Set(audio.CastWood,"cast_wood",.75f,0,.055f,2);Set(audio.CastFire,"cast_fire",.75f,0,.055f,2);
            Set(audio.CastEarth,"cast_earth",.75f,0,.055f,2);Set(audio.CastMetal,"cast_metal",.75f,0,.055f,2);Set(audio.CastWater,"cast_water",.75f,0,.055f,2);
            Set(audio.Impact,"impact",.65f,1,.085f,3);Set(audio.PlayerHit,"player_hit",.70f,0,.12f,2);Set(audio.Parry,"parry",.75f,1,.1f,2);
            Set(audio.Harvest,"harvest",.55f,1,1.4f,1);Set(audio.Interact,"interact",.65f,1,.12f,2);Set(audio.Rest,"rest",.70f,1,.3f,1);
            Set(audio.SummonAppear,"summon_appear",.75f,1,.15f,2);Set(audio.SummonRelease,"summon_release",.7f,1,.15f,2);
            audio.HarvestStart=new WorldMacroPlaytestAudioProfileSO.Cue();audio.HarvestLoop=new WorldMacroPlaytestAudioProfileSO.Cue();audio.HarvestEnd=new WorldMacroPlaytestAudioProfileSO.Cue();
            Set(audio.HarvestStart,"harvest_start",.7f,.35f,.15f,1);Set(audio.HarvestLoop,"harvest_loop",.7f,.35f,0,1);Set(audio.HarvestEnd,"harvest_end",.65f,.35f,.15f,1);
            audio.HarvestLoop.AttackSeconds=.10f;audio.HarvestLoop.ReleaseSeconds=.14f;
            var theme=Owned(ThemePath,Load<PlaytestUiThemeSO>(PlaytestMenuAuthoring.ThemePath));
            theme.AudioMix=mix;theme.PaperSound=Clip("ui_paper");theme.ConfirmSound=Clip("ui_confirm");theme.BackSound=Clip("ui_back");
            string materialPath=Folder+"/InkFlow.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            var shader=Shader.Find("Oheangbu/HarvestInkFlow");if(shader==null)throw new InvalidOperationException("Ink flow shader missing");
            if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,materialPath);}material.shader=shader;
            material.SetTexture("_MainTex",Load<Texture2D>("Assets/_Project/Art/World/WorldMacro/Playtest/HUD/Textures/hp_stroke.png"));
            var flow=Owned<HarvestInkFlowProfileSO>(FlowPath);flow.Material=material;
            foreach(var asset in new Object[]{mix,audio,theme,flow,material})EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();return Validate();
        }
        static AudioClip Clip(string name)=>Load<AudioClip>("Assets/_Project/Audio/JourneyRenewal/"+name+".wav");
        static void Set(WorldMacroPlaytestAudioProfileSO.Cue cue,string clip,float volume,float spatial,float cooldown,int max)
        {cue.Clip=Clip(clip);cue.Volume=volume;cue.SpatialBlend=spatial;cue.Cooldown=cooldown;cue.MaxConcurrent=max;cue.AttackSeconds=.018f;cue.ReleaseSeconds=.06f;}
        static AudioMixerGroup Group(AudioMixer mixer,string name)=>mixer.FindMatchingGroups(name).FirstOrDefault(x=>x.name==name)??throw new InvalidOperationException("Mixer group missing "+name);
        static object Get(object target,string property)=>target.GetType().GetProperty(property,Flags)?.GetValue(target)??throw new MissingMemberException(target.GetType().Name,property);
        static object Call(object target,string method,params object[] args)
        {
            var match=target.GetType().GetMethods(Flags).FirstOrDefault(x=>x.Name==method&&x.GetParameters().Length==args.Length);
            if(match==null)throw new MissingMethodException(target.GetType().Name,method);return match.Invoke(target,args);
        }
        static AudioMixer PrepareMixer()
        {
            string path=Folder+"/PlaytestAudio.mixer";var existing=AssetDatabase.LoadAssetAtPath<AudioMixer>(path);if(existing!=null)return existing;
            var type=typeof(AudioImporter).Assembly.GetType("UnityEditor.Audio.AudioMixerController")??AppDomain.CurrentDomain.GetAssemblies().Select(x=>x.GetType("UnityEditor.Audio.AudioMixerController")).FirstOrDefault(x=>x!=null);
            if(type==null)throw new NotSupportedException("AudioMixer editor controller unavailable; no fake mixer fallback");
            var create=type.GetMethod("CreateMixerControllerAtPath",Flags);if(create==null)throw new MissingMethodException(type.Name,"CreateMixerControllerAtPath");
            var controller=create.Invoke(null,new object[]{path});var master=Get(controller,"masterGroup");
            var sfx=Call(controller,"CreateNewGroup","SFX",false);Call(controller,"AddChildToParent",sfx,master);
            var ui=Call(controller,"CreateNewGroup","UI",false);Call(controller,"AddChildToParent",ui,master);
            var harvest=Call(controller,"CreateNewGroup","Harvest",false);Call(controller,"AddChildToParent",harvest,sfx);
            var exposed=type.GetProperty("exposedParameters",Flags)??throw new MissingMemberException(type.Name,"exposedParameters");
            var parameterType=exposed.PropertyType.GetElementType();Array parameters=Array.CreateInstance(parameterType,3);
            object[] groups={master,sfx,ui};string[] names={"MasterVolumeDb","SfxVolumeDb","UiVolumeDb"};
            for(int i=0;i<3;i++)
            {
                object parameter=Activator.CreateInstance(parameterType);parameterType.GetField("guid",Flags).SetValue(parameter,Call(groups[i],"GetGUIDForVolume"));
                parameterType.GetField("name",Flags).SetValue(parameter,names[i]);parameters.SetValue(parameter,i);
            }
            exposed.SetValue(controller,parameters);
            object snapshot=Get(controller,"startSnapshot");Call(master,"SetValueForVolume",controller,snapshot,-6f);
            foreach(var group in new[]{sfx,ui,harvest})Call(group,"SetValueForVolume",controller,snapshot,0f);
            foreach(var obj in AssetDatabase.LoadAllAssetsAtPath(path))EditorUtility.SetDirty(obj);
            AssetDatabase.SaveAssets();return (AudioMixer)controller;
        }
        public static string ApplyCurrent()
        {
            NeedEdit();var scene=SceneManager.GetActiveScene();
            if(scene.path!=WorldMacroPlaytestAuthoring.ScenePath)throw new InvalidOperationException("Only the owned World Macro playtest scene is supported");
            if(scene.isDirty)throw new InvalidOperationException("Save the current scene before applying audio/ink so rollback is concrete");
            var audio=Object.FindFirstObjectByType<WorldMacroPlaytestAudio>();var flow=Object.FindFirstObjectByType<HarvestInkStreamEffect>();var ui=Object.FindFirstObjectByType<PlaytestUiRoot>();
            if(audio==null||flow==null||ui==null)throw new InvalidOperationException("Expected audio/ink/UI components are missing");
            var newAudio=Load<WorldMacroPlaytestAudioProfileSO>(AudioPath);var newFlow=Load<HarvestInkFlowProfileSO>(FlowPath);var theme=Load<PlaytestUiThemeSO>(ThemePath);
            string backup=BackupScene(scene);var oldAudio=audio.Profile;var oldFlow=flow.FlowProfile;var oldTheme=ui.Theme;
            try
            {
                Undo.RecordObjects(new Object[]{audio,flow,ui},"Apply Audio Ink Polish");audio.ConfigureProfile(newAudio);flow.ConfigureFlowProfile(newFlow);ui.Theme=theme;
                foreach(var item in new Object[]{audio,flow,ui})EditorUtility.SetDirty(item);
                EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new IOException("Scene save failed");
            }
            catch{audio.ConfigureProfile(oldAudio);flow.ConfigureFlowProfile(oldFlow);ui.Theme=oldTheme;EditorSceneManager.MarkSceneDirty(scene);throw;}
            return Write("apply_current",new Report{status="APPLIED",scope="Owned playtest audio, ink flow, UI theme only; backup "+backup,checks=new[]{scene.path},failures=Array.Empty<string>()});
        }
        static string BackupScene(Scene scene)
        {Directory.CreateDirectory(Output+"/BeforeScenes");string file=Output+"/BeforeScenes/"+Path.GetFileName(scene.path);if(!File.Exists(file))File.Copy(scene.path,file);return file;}
        public static string ApplyTitle()
        {
            NeedEdit();var active=SceneManager.GetActiveScene();if(active.isDirty)throw new InvalidOperationException("Save active scene first");
            var theme=Load<PlaytestUiThemeSO>(ThemePath);var title=SceneManager.GetSceneByPath(PlaytestMenuAuthoring.TitlePath);bool opened=!title.IsValid()||!title.isLoaded;
            if(opened)title=EditorSceneManager.OpenScene(PlaytestMenuAuthoring.TitlePath,OpenSceneMode.Additive);
            try
            {
                if(title.isDirty)throw new InvalidOperationException("Title scene has unsaved edits");BackupScene(title);
                var roots=title.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<PlaytestUiRoot>(true)).ToArray();
                if(roots.Length!=1)throw new InvalidOperationException("Expected exactly one title UI root");
                var old=roots[0].Theme;try{roots[0].Theme=theme;EditorUtility.SetDirty(roots[0]);EditorSceneManager.MarkSceneDirty(title);if(!EditorSceneManager.SaveScene(title))throw new IOException("Title save failed");}
                catch{roots[0].Theme=old;throw;}
                return "Title audio theme applied; gameplay/UI/map settings preserved";
            }
            finally{if(opened)EditorSceneManager.CloseScene(title,true);if(active.IsValid())SceneManager.SetActiveScene(active);}
        }
        public static string Validate()
        {
            var checks=new List<string>();var fail=new List<string>();
            foreach(string file in Directory.GetFiles("Assets/_Project/Audio/JourneyRenewal","*.wav"))
            {
                var importer=AssetImporter.GetAtPath(file.Replace('\\','/')) as AudioImporter;
                if(importer==null||importer.defaultSampleSettings.compressionFormat!=AudioCompressionFormat.PCM||!importer.defaultSampleSettings.preloadAudioData)fail.Add("PCM preload: "+file);
                else checks.Add("PCM preload: "+Path.GetFileName(file));
            }
            var mix=Load<WorldMacroAudioMixProfileSO>(MixPath);if(!mix.IsReady)fail.Add("Mixer routing incomplete");else checks.Add("Master > SFX > Harvest; Master > UI");
            foreach(var key in new[]{"MasterVolumeDb","SfxVolumeDb","UiVolumeDb"})if(!mix.Mixer.GetFloat(key,out _))fail.Add("Exposed mixer parameter: "+key);
            var audio=Load<WorldMacroPlaytestAudioProfileSO>(AudioPath);if(audio.Mix!=mix||audio.HarvestStart?.Clip==null||audio.HarvestLoop?.Clip==null||audio.HarvestEnd?.Clip==null)fail.Add("Harvest stages/mixer");
            var flow=Load<HarvestInkFlowProfileSO>(FlowPath);if(flow.Material==null||flow.Material.shader.name!="Oheangbu/HarvestInkFlow"||flow.Material.GetTexture("_MainTex")==null)fail.Add("Textured ink shader");
            var theme=Load<PlaytestUiThemeSO>(ThemePath);if(theme.AudioMix!=mix)fail.Add("UI mix missing");
            return Write("technical_validation",new Report{status=fail.Count==0?"PASS":"FAIL",scope="Asset wiring/import only; actual device output and artistic judgement unverified",checks=checks.ToArray(),failures=fail.ToArray()});
        }
        public static string Contracts()
        {
            var checks=new List<string>();var failures=new List<string>();
            void Check(bool test,string text){(test?checks:failures).Add(text);}
            var pool=new InkPool(null,null);int gains=0;float received=0;pool.Gained+=(x)=>{gains++;received+=x;};
            pool.Restore(.9f);pool.Gain(.3f);Check(gains==1&&Mathf.Abs(received-.1f)<1e-5f,"Gain emits actual clamped income");
            pool.Gain(.2f);pool.Restore(.4f);pool.TrySpend(.1f);Check(gains==1,"Full, Restore and spending do not fake an income event");
            received=0;gains=0;for(int i=0;i<120;i++)pool.Gain(.001f);Check(gains==120&&Mathf.Abs(received-.12f)<1e-4f,"Continuous 120 gain steps conserve income");
            float previous=-1;bool monotonic=true;for(int i=0;i<=1000;i++){float value=WorldMacroAudioVoicePool.SmoothGain(i/1000f);if(value<previous||value<0||value>1)monotonic=false;previous=value;}
            Check(monotonic&&WorldMacroAudioVoicePool.SmoothGain(0)==0&&WorldMacroAudioVoicePool.SmoothGain(1)==1,"Bounded monotonic fade endpoints");
            var source=new Vector3(1,2,3);var sink=new Vector3(-4,1,2);Check(HarvestInkFlowRenderer.Curve(source,sink,Vector3.down,0,.1f)==source&&HarvestInkFlowRenderer.Curve(source,sink,Vector3.down,1,.1f)==sink,"Flow starts at target and ends at the actual brush tip");
            Check(Mathf.Abs(WorldMacroAudioMixProfileSO.VolumeDb(.5f)+6.0206f)<.0002f&&WorldMacroAudioMixProfileSO.VolumeDb(0)==-80,"Options map amplitude to dB without double multiplication");
            return Write("contract_tests",new Report{status=failures.Count==0?"PASS":"FAIL",scope="Service and presentation math; no actual player input or output device",checks=checks.ToArray(),failures=failures.ToArray()});
        }
        public static string Runtime()
        {
            if(!Application.isPlaying)throw new InvalidOperationException("Existing Play mode required");
            var checks=new List<string>();var fail=new List<string>();var audio=Object.FindFirstObjectByType<WorldMacroPlaytestAudio>();
            if(audio==null)fail.Add("Audio reader absent");else{checks.Add(audio.RuntimeCounters);if(audio.VoiceCount!=WorldMacroPlaytestAudio.VoiceLimit)fail.Add("Bounded voice count");}
            foreach(var src in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(x=>x.name.StartsWith("PlaytestAudioVoice_")||x.name.StartsWith("PlaytestUiVoice_")))
            {if(src.isPlaying&&src.outputAudioMixerGroup==null)fail.Add("Unrouted active source "+src.name);if(src.pitch!=1||src.dopplerLevel!=0)fail.Add("Unexpected pitch/Doppler "+src.name);}
            var flow=Object.FindFirstObjectByType<HarvestInkStreamEffect>();if(flow==null||flow.FlowProfile==null)fail.Add("New flow unbound");else{checks.Add("flowVisible="+flow.FlowVisible+"; sink="+flow.FlowSink);if(flow.FlowVisible&&flow.SinkAnchor!=null&&Vector3.Distance(flow.FlowSink,flow.SinkAnchor.position)>.002f)fail.Add("Flow misses actual tip");}
            AudioSettings.GetDSPBufferSize(out int size,out int count);checks.Add("DSP buffer="+size+" x "+count+"; sampleRate="+AudioSettings.outputSampleRate);
            return Write("runtime_snapshot",new Report{status=fail.Count==0?"PASS":"FAIL",scope="Current state snapshot; listener clipping, transition sweeps and subjective sound remain unverified",checks=checks.ToArray(),failures=fail.ToArray()});
        }
        static string Write(string name,Report report){Directory.CreateDirectory(Output);string json=JsonUtility.ToJson(report,true);File.WriteAllText(Output+"/"+name+".json",json);return json;}
    }
}
