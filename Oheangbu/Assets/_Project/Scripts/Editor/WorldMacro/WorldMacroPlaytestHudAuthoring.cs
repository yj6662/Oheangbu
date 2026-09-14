using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Installs and reviews only the optional World Macro playtest HUD skin.</summary>
    [InitializeOnLoad]
    public static class WorldMacroPlaytestHudAuthoring
    {
        public const string Folder = "Assets/_Project/Art/World/WorldMacro/Playtest/HUD";
        public const string ProfilePath = Folder + "/WorldMacroHudSkinProfile.asset";
        public const string HpPath = Folder + "/Textures/hp_stroke.png";
        public const string BottlePath = Folder + "/Textures/ink_bottle.png";
        public const string RingPath = Folder + "/Textures/lock_ring.png";
        public const string PromptPath = Folder + "/Textures/prompt_paper.png";
        private static string RuntimeFolder => WorldMacroPlaytestAuthoring.Output + "/HUD/RuntimeReview";
        private const string GameViewSessionPrefix="Oheangbu.WorldMacroHud.CaptureSize.";
        private const string GameViewSessionIds=GameViewSessionPrefix+"Ids";
        private const string GameViewSessionGroup=GameViewSessionPrefix+"Group";

        [Serializable]
        private sealed class Report
        {
            public string status;
            public string command;
            public string scene;
            public string profile;
            public bool profileScriptBound;
            public bool exactlyOneHud;
            public bool exactlyOnePresenter;
            public bool presenterBound;
            public bool fourSpritesBound;
            public bool fallbackLocalToPlaytest;
            public bool legacyPreviewPromptInactive;
            public string[] spritePaths = Array.Empty<string>();
            public string[] failures = Array.Empty<string>();
        }

        [Serializable]
        private sealed class RuntimeReport
        {
            public string utc;
            public string status;
            public string scope = "Actual W_WorldMacro_Playtest current HUD state and one 1920x1080 still. No input synthesis, gameplay-state mutation, automatic walking, audio judgment, or user visual approval.";
            public int width;
            public int height;
            public float hudHp01;
            public float playerHp01;
            public float hudInk01;
            public float hudGroggy01;
            public bool skinned;
            public bool presenterActive;
            public bool interactionStateMatches;
            public bool oneCanvas;
            public bool oneAudioListener;
            public bool screenshotWritten;
            public bool renderedSampleValid;
            public string screenshot;
            public bool presentationStateInjected;
            public string[] stills = Array.Empty<string>();
            public string[] failures = Array.Empty<string>();
        }

        private readonly struct DiagnosticState
        {
            public readonly string Name, Prompt;
            public readonly float Hp, Ink, Groggy;
            public DiagnosticState(string name,float hp,float ink,float groggy,string prompt)
            {Name=name;Hp=hp;Ink=ink;Groggy=groggy;Prompt=prompt;}
        }

        private static readonly DiagnosticState[] DiagnosticStates=
        {
            new DiagnosticState("all_four",1f,.5f,.55f,"[F] 금표 주막에서 쉬기"),
            new DiagnosticState("low_hp",.2f,.5f,.55f,"금표 주막에서 숨을 고른다. 체력과 먹을 회복했다.")
        };

        private static bool runtimeRunning, captureIssued;
        private static int runtimeStartFrame, captureFrame;
        private static double runtimeDeadline;
        private static string runtimeScreenshot;
        private static RuntimeReport runtimeReport;
        private static bool diagnosticStills, diagnosticApplied;
        private static int diagnosticIndex;
        private static HudController diagnosticHud;
        private static float originalHp,originalInk,originalGroggy;
        private static string originalInteraction;
        private static bool originalReticleVisible;
        private static Vector3 originalReticlePosition;
        private static readonly List<string> CapturedStills=new List<string>();

        static WorldMacroPlaytestHudAuthoring()
        {
            AssemblyReloadEvents.beforeAssemblyReload += AbortRuntimeReview;
            EditorApplication.playModeStateChanged += state =>
            {
                if(runtimeRunning && state == PlayModeStateChange.ExitingPlayMode) AbortRuntimeReview();
            };
        }

        public static string Execute(string argument)
        {
            string command=(argument??string.Empty).Trim().ToLowerInvariant();
            if(command=="runtime-begin")return BeginRuntimeReview();
            if(command=="state-stills")return BeginRuntimeReview(true);
            if(command=="runtime-poll")return PollRuntimeReview();
            RequireEditScene();
            switch(command)
            {
                case "capture-size":return SelectCaptureSize();
                case "restore-size":return RestoreCaptureSize();
                case "inspect":return SaveReport(BuildReport("inspect"));
                case "install":return Install();
                case "validate":return SaveReport(BuildReport("validate"));
                default:throw new ArgumentException("Expected inspect, install, validate, capture-size, restore-size, runtime-begin, state-stills, or runtime-poll.");
            }
        }

        private static string SelectCaptureSize()
        {
            Type gameViewType=Type.GetType("UnityEditor.GameView,UnityEditor",true);
            Object[] gameViews=Resources.FindObjectsOfTypeAll(gameViewType);
            Need(gameViews.Length>0,"No existing Game view is open. This command does not open or focus editor windows.");
            Type sizesType=Type.GetType("UnityEditor.GameViewSizes,UnityEditor",true);
            object sizes=Property(sizesType,"instance",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).GetValue(null);
            object group=Property(sizesType,"currentGroup",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(sizes);
            object groupType=Property(sizesType,"currentGroupType",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(sizes);
            Type groupRuntimeType=group.GetType();
            MethodInfo builtinCount=Method(groupRuntimeType,"GetBuiltinCount");
            MethodInfo getSize=Method(groupRuntimeType,"GetGameViewSize");
            int fullHdIndex=-1,count=(int)builtinCount.Invoke(group,null);
            for(int i=0;i<count;i++)
            {
                object size=getSize.Invoke(group,new object[]{i});Type type=size.GetType();
                int width=(int)Property(type,"width",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(size);
                int height=(int)Property(type,"height",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(size);
                string sizeType=Property(type,"sizeType",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(size).ToString();
                if(width==1920&&height==1080&&sizeType=="FixedResolution"){fullHdIndex=i;break;}
            }
            Need(fullHdIndex>=0,"The current GameViewSizes group has no built-in FixedResolution 1920x1080 entry.");
            var ids=new HashSet<int>(ParseIds(SessionState.GetString(GameViewSessionIds,string.Empty)));
            var changes=new List<string>();
            foreach(Object gameView in gameViews)
            {
                PropertyInfo selected=Property(gameViewType,"selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                int previous=(int)selected.GetValue(gameView);int id=gameView.GetInstanceID();string key=GameViewSessionPrefix+id;
                if(string.IsNullOrEmpty(SessionState.GetString(key,string.Empty)))SessionState.SetString(key,previous.ToString());
                ids.Add(id);
                Method(gameViewType,"SizeSelectionCallback").Invoke(gameView,new object[]{fullHdIndex,null});
                changes.Add(id+":"+previous+"->"+(int)selected.GetValue(gameView));
            }
            SessionState.SetString(GameViewSessionIds,string.Join(",",ids));
            SessionState.SetString(GameViewSessionGroup,groupType.ToString());
            SceneView.RepaintAll();
            return "CAPTURE_SIZE_SET target=Full HD FixedResolution 1920x1080 index="+fullHdIndex+" group="+groupType
                +" views="+gameViews.Length+" selections="+string.Join(";",changes);
        }

        private static string RestoreCaptureSize()
        {
            Type gameViewType=Type.GetType("UnityEditor.GameView,UnityEditor",true);
            int[] ids=ParseIds(SessionState.GetString(GameViewSessionIds,string.Empty));
            Need(ids.Length>0,"No preserved Game view size selection exists.");
            Type sizesType=Type.GetType("UnityEditor.GameViewSizes,UnityEditor",true);
            object sizes=Property(sizesType,"instance",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).GetValue(null);
            string currentGroup=Property(sizesType,"currentGroupType",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(sizes).ToString();
            string savedGroup=SessionState.GetString(GameViewSessionGroup,string.Empty);
            Need(string.IsNullOrEmpty(savedGroup)||savedGroup==currentGroup,"Game view size group changed from "+savedGroup+" to "+currentGroup+"; switch it back before restore.");
            var restored=new List<string>();var missing=new List<int>();
            foreach(int id in ids)
            {
                string key=GameViewSessionPrefix+id;string saved=SessionState.GetString(key,string.Empty);
                Object gameView=EditorUtility.InstanceIDToObject(id);
                if(gameView==null||!gameViewType.IsInstanceOfType(gameView)||!int.TryParse(saved,out int previous))
                {missing.Add(id);SessionState.EraseString(key);continue;}
                PropertyInfo selected=Property(gameViewType,"selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                int before=(int)selected.GetValue(gameView);
                Method(gameViewType,"SizeSelectionCallback").Invoke(gameView,new object[]{previous,null});
                restored.Add(id+":"+before+"->"+(int)selected.GetValue(gameView));
                SessionState.EraseString(key);
            }
            SessionState.EraseString(GameViewSessionIds);SessionState.EraseString(GameViewSessionGroup);SceneView.RepaintAll();
            Need(restored.Count>0,"Preserved Game views no longer exist; no size was restored.");
            return "CAPTURE_SIZE_RESTORED group="+currentGroup+" views="+restored.Count+" selections="+string.Join(";",restored)
                +(missing.Count>0?" missingInstanceIds="+string.Join(",",missing):string.Empty);
        }

        private static int[] ParseIds(string value)
        {return string.IsNullOrWhiteSpace(value)?Array.Empty<int>():value.Split(',').Select(part=>int.TryParse(part,out int id)?id:0).Where(id=>id!=0).Distinct().ToArray();}

        private static PropertyInfo Property(Type type,string name,BindingFlags flags)
        {return type.GetProperty(name,flags|BindingFlags.FlattenHierarchy)??throw new MissingMemberException(type.FullName,name);}

        private static MethodInfo Method(Type type,string name)
        {return type.GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)??throw new MissingMethodException(type.FullName,name);}

        private static string Install()
        {
            EnsureFolder(Folder);
            Sprite hp=PrepareSprite(HpPath), bottle=PrepareSprite(BottlePath), ring=PrepareSprite(RingPath), prompt=PrepareSprite(PromptPath);
            var profile=AssetDatabase.LoadAssetAtPath<WorldMacroHudSkinProfileSO>(ProfilePath);
            if(profile==null)
            {
                profile=ScriptableObject.CreateInstance<WorldMacroHudSkinProfileSO>();
                AssetDatabase.CreateAsset(profile,ProfilePath);
            }
            profile.HpStroke=hp;profile.InkBottle=bottle;profile.LockRing=ring;profile.PromptPaper=prompt;
            EditorUtility.SetDirty(profile);

            var huds=Object.FindObjectsByType<HudController>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            var sessions=Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            Need(huds.Length==1,"Expected exactly one HudController in W_WorldMacro_Playtest.");
            Need(sessions.Length==1,"Expected exactly one WorldMacroPlaytestSession in W_WorldMacro_Playtest.");
            HudController hud=huds[0];WorldMacroPlaytestSession session=sessions[0];
            hud.Skin=profile;
            var presenters=Object.FindObjectsByType<WorldMacroPlaytestHudPresenter>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            WorldMacroPlaytestHudPresenter presenter=presenters.FirstOrDefault();
            if(presenter==null)presenter=Undo.AddComponent<WorldMacroPlaytestHudPresenter>(hud.gameObject);
            foreach(var duplicate in presenters.Where(item=>item!=presenter))Undo.DestroyObjectImmediate(duplicate);
            presenter.Hud=hud;presenter.Session=session;presenter.Body=session.Walker!=null?session.Walker.Body:null;
            EditorUtility.SetDirty(hud);EditorUtility.SetDirty(presenter);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Report report=BuildReport("install");
            Need(report.failures.Length==0,"HUD installation validation failed:\n"+string.Join("\n",report.failures));
            report.status="INSTALLED_TECHNICAL_PASS";
            return SaveReport(report);
        }

        private static Report BuildReport(string command)
        {
            var failures=new List<string>();
            var profile=AssetDatabase.LoadAssetAtPath<WorldMacroHudSkinProfileSO>(ProfilePath);
            var huds=Object.FindObjectsByType<HudController>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            var presenters=Object.FindObjectsByType<WorldMacroPlaytestHudPresenter>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            var sessions=Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            bool scriptBound=profile!=null&&MonoScript.FromScriptableObject(profile)!=null;
            bool sprites=profile!=null&&profile.HpStroke!=null&&profile.InkBottle!=null&&profile.LockRing!=null&&profile.PromptPaper!=null;
            bool presenterBound=huds.Length==1&&presenters.Length==1&&sessions.Length==1&&presenters[0].Hud==huds[0]
                &&presenters[0].Session==sessions[0]&&sessions[0].Walker!=null&&presenters[0].Body==sessions[0].Walker.Body;
            bool local=huds.Length==1&&huds[0].Skin==profile;
            bool legacyInactive=Object.FindObjectsByType<WorldMacroContentPreview>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .All(preview=>!preview.ShowPlacementHints||preview.Walker==null||!preview.Walker.isActiveAndEnabled);
            if(profile==null)failures.Add("HUD skin profile is missing.");
            if(!scriptBound)failures.Add("HUD skin profile MonoScript binding is invalid.");
            if(!sprites)failures.Add("One or more of the four Recraft sprite derivatives is missing.");
            if(huds.Length!=1)failures.Add("Expected exactly one HudController; found "+huds.Length+".");
            if(presenters.Length!=1)failures.Add("Expected exactly one WorldMacroPlaytestHudPresenter; found "+presenters.Length+".");
            if(!presenterBound)failures.Add("HUD presenter references do not match the playtest HUD/session/body.");
            if(!local)failures.Add("The playtest HudController does not reference the owned skin profile.");
            if(!legacyInactive)failures.Add("WorldMacroContentPreview can render a competing legacy interaction prompt.");
            return new Report
            {
                status=failures.Count==0?"TECHNICAL_PASS":"FAILED",command=command,scene=SceneManager.GetActiveScene().path,
                profile=ProfilePath,profileScriptBound=scriptBound,exactlyOneHud=huds.Length==1,
                exactlyOnePresenter=presenters.Length==1,presenterBound=presenterBound,fourSpritesBound=sprites,
                fallbackLocalToPlaytest=local,legacyPreviewPromptInactive=legacyInactive,
                spritePaths=new[]{HpPath,BottlePath,RingPath,PromptPath},failures=failures.ToArray()
            };
        }

        private static Sprite PrepareSprite(string path)
        {
            Need(File.Exists(path),"Missing UI derivative: "+path);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            Need(importer!=null,"Expected TextureImporter: "+path);
            bool changed=importer.textureType!=TextureImporterType.Sprite||importer.spriteImportMode!=SpriteImportMode.Single
                ||importer.mipmapEnabled||!importer.alphaIsTransparency||importer.wrapMode!=TextureWrapMode.Clamp
                ||importer.filterMode!=FilterMode.Bilinear||importer.textureCompression!=TextureImporterCompression.Uncompressed;
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;
            importer.filterMode=FilterMode.Bilinear;importer.textureCompression=TextureImporterCompression.Uncompressed;
            if(changed)importer.SaveAndReimport();
            Sprite sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Need(sprite!=null,"Sprite import failed: "+path);
            return sprite;
        }

        private static string BeginRuntimeReview(bool injectPresentationStates=false)
        {
            if(runtimeRunning)throw new InvalidOperationException("HUD runtime review is already running; poll it first.");
            if(!EditorApplication.isPlaying||EditorApplication.isPaused||Time.timeScale<=0f
                ||SceneManager.GetActiveScene().path!=WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("W_WorldMacro_Playtest must already be running and unpaused. This helper does not start Play.");
            if(Screen.width!=1920||Screen.height!=1080)
                throw new InvalidOperationException("Set the Game view to exactly 1920x1080 before HUD runtime review; current="+Screen.width+"x"+Screen.height+".");
            Directory.CreateDirectory(RuntimeFolder);
            diagnosticStills=injectPresentationStates;diagnosticApplied=false;diagnosticIndex=0;diagnosticHud=null;CapturedStills.Clear();
            runtimeScreenshot=Path.Combine(RuntimeFolder,diagnosticStills?"hud_state_all_four_1080p.png":"hud_actual_1080p.png");
            runtimeReport=new RuntimeReport{utc=DateTime.UtcNow.ToString("o"),status="RUNNING",width=Screen.width,height=Screen.height,
                screenshot=runtimeScreenshot,presentationStateInjected=diagnosticStills};
            if(diagnosticStills)runtimeReport.scope="Actual W_WorldMacro_Playtest Canvas with two explicitly injected presentation-only states. Live gameplay values are sampled first and restored after capture; images are UI composition evidence, not gameplay-event evidence or user visual approval.";
            runtimeStartFrame=Time.frameCount;runtimeDeadline=EditorApplication.timeSinceStartup+20d;
            captureIssued=false;runtimeRunning=true;EditorApplication.update+=RuntimeTick;
            return diagnosticStills
                ?"RUNNING: two labeled presentation-state HUD stills; display values are restored and gameplay state/input are never changed."
                :"RUNNING: current actual HUD lifecycle and 1920x1080 still; no gameplay state or input is changed.";
        }

        private static void RuntimeTick()
        {
            if(!runtimeRunning)return;
            try
            {
                if(!EditorApplication.isPlaying||EditorApplication.isPaused||EditorApplication.timeSinceStartup>runtimeDeadline)
                {FinishRuntimeReview("ABORTED","Play state, pause state, or twenty-second deadline interrupted review.");return;}
                if(Time.frameCount-runtimeStartFrame<6)return;
                if(!captureIssued)
                {
                    EvaluateRuntime();
                    if(diagnosticStills)ApplyDiagnosticState(0);
                    IssueCapture(runtimeScreenshot);
                    captureIssued=true;captureFrame=Time.frameCount;return;
                }
                bool written=File.Exists(runtimeScreenshot)&&new FileInfo(runtimeScreenshot).Length>0;
                if(!written&&Time.frameCount-captureFrame<8)return;
                CapturedStills.Add(runtimeScreenshot);
                if(diagnosticStills&&diagnosticIndex+1<DiagnosticStates.Length)
                {
                    diagnosticIndex++;ApplyDiagnosticState(diagnosticIndex);
                    runtimeScreenshot=Path.Combine(RuntimeFolder,"hud_state_"+DiagnosticStates[diagnosticIndex].Name+"_1080p.png");
                    IssueCapture(runtimeScreenshot);captureFrame=Time.frameCount;return;
                }
                runtimeReport.screenshotWritten=CapturedStills.All(path=>File.Exists(path)&&new FileInfo(path).Length>0);
                runtimeReport.renderedSampleValid=runtimeReport.screenshotWritten&&CapturedStills.All(HasRenderedRange);
                runtimeReport.stills=CapturedStills.ToArray();
                var failures=runtimeReport.failures.ToList();
                if(!runtimeReport.screenshotWritten)failures.Add("One or more 1920x1080 Game-view screenshots were not written.");
                else if(!runtimeReport.renderedSampleValid)failures.Add("One or more screenshots lack rendered pixel range or exact 1920x1080 dimensions.");
                runtimeReport.failures=failures.ToArray();
                FinishRuntimeReview(failures.Count==0?"PASS_BOUNDED_RUNTIME":"FINDINGS","Current-state review finished.");
            }
            catch(Exception exception){FinishRuntimeReview("ERROR",exception.ToString());}
        }

        private static void ApplyDiagnosticState(int index)
        {
            if(!diagnosticApplied)
            {
                diagnosticHud=Object.FindFirstObjectByType<HudController>();
                var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
                Need(diagnosticHud!=null&&diagnosticHud.IsSkinned,"Active skinned HudController is required for diagnostic stills.");
                originalHp=diagnosticHud.Hp01;originalInk=diagnosticHud.Ink01;originalGroggy=diagnosticHud.Groggy01;
                originalInteraction=session!=null?session.CurrentHudText:null;
                originalReticleVisible=diagnosticHud.ReticleVisible;
                var reticle=diagnosticHud.transform.Find("HUD_Canvas/Reticle") as RectTransform;
                originalReticlePosition=reticle!=null?reticle.position:Vector3.zero;
                diagnosticApplied=true;
            }
            DiagnosticState state=DiagnosticStates[index];
            diagnosticHud.SetEditorDiagnosticState(state.Hp,state.Ink,state.Groggy,state.Prompt,new Vector2(Screen.width*.5f,Screen.height*.58f));
        }

        private static void IssueCapture(string path)
        {
            if(File.Exists(path))File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
        }

        private static bool HasRenderedRange(string path)
        {
            var texture=new Texture2D(2,2,TextureFormat.RGB24,false,true);
            try
            {
                if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(path),false)||texture.width!=1920||texture.height!=1080)return false;
                float minimum=1f,maximum=0f;
                for(int y=30;y<texture.height;y+=60)
                for(int x=30;x<texture.width;x+=60)
                {
                    Color color=texture.GetPixel(x,y);float luminance=color.r*.2126f+color.g*.7152f+color.b*.0722f;
                    minimum=Mathf.Min(minimum,luminance);maximum=Mathf.Max(maximum,luminance);
                }
                return maximum-minimum>.08f;
            }
            finally{Object.DestroyImmediate(texture);}
        }

        private static void RestoreDiagnosticState()
        {
            if(!diagnosticApplied)return;
            if(diagnosticHud!=null)diagnosticHud.ClearEditorDiagnosticState(originalHp,originalInk,originalGroggy,
                originalInteraction,originalReticleVisible,originalReticlePosition);
            diagnosticApplied=false;diagnosticHud=null;
        }

        private static void EvaluateRuntime()
        {
            var failures=new List<string>();
            var huds=Object.FindObjectsByType<HudController>(FindObjectsInactive.Exclude,FindObjectsSortMode.None);
            var presenters=Object.FindObjectsByType<WorldMacroPlaytestHudPresenter>(FindObjectsInactive.Exclude,FindObjectsSortMode.None);
            var sessions=Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Exclude,FindObjectsSortMode.None);
            HudController hud=huds.Length==1?huds[0]:null;
            WorldMacroPlaytestSession session=sessions.Length==1?sessions[0]:null;
            var vitals=presenters.Length==1&&presenters[0].Body!=null?presenters[0].Body.GetComponent<PlayerVitals>():null;
            runtimeReport.skinned=hud!=null&&hud.IsSkinned;
            runtimeReport.presenterActive=session!=null&&session.CanvasHudPresenterActive;
            runtimeReport.oneCanvas=hud!=null&&hud.GetComponentsInChildren<Canvas>(true).Length==1;
            runtimeReport.oneAudioListener=Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).Count(listener=>listener.isActiveAndEnabled)==1;
            runtimeReport.hudHp01=hud!=null?hud.Hp01:-1f;runtimeReport.playerHp01=vitals!=null?vitals.Hp01:-1f;
            runtimeReport.hudInk01=hud!=null?hud.Ink01:-1f;runtimeReport.hudGroggy01=hud!=null?hud.Groggy01:-1f;
            runtimeReport.interactionStateMatches=hud!=null&&session!=null&&hud.InteractionVisible==!string.IsNullOrEmpty(session.CurrentHudText);
            if(huds.Length!=1)failures.Add("Expected one active HudController; found "+huds.Length+".");
            if(presenters.Length!=1)failures.Add("Expected one active HUD presenter; found "+presenters.Length+".");
            if(sessions.Length!=1)failures.Add("Expected one active playtest session; found "+sessions.Length+".");
            if(!runtimeReport.skinned)failures.Add("Actual HUD is not using the playtest skin.");
            if(!runtimeReport.presenterActive)failures.Add("Canvas interaction presenter is not active.");
            if(!runtimeReport.oneCanvas)failures.Add("HUD must own exactly one runtime Canvas.");
            if(!runtimeReport.oneAudioListener)failures.Add("Scene must have exactly one active AudioListener.");
            if(!runtimeReport.interactionStateMatches)failures.Add("Interaction Canvas visibility disagrees with session CurrentHudText.");
            if(vitals==null||hud==null||Mathf.Abs(vitals.Hp01-hud.Hp01)>.001f)failures.Add("HUD HP does not equal the live PlayerVitals value.");
            runtimeReport.failures=failures.ToArray();
        }

        private static void FinishRuntimeReview(string status,string detail)
        {
            if(!runtimeRunning)return;
            RestoreDiagnosticState();
            runtimeRunning=false;EditorApplication.update-=RuntimeTick;
            runtimeReport.status=status;
            if(status=="ABORTED"||status=="ERROR")runtimeReport.failures=runtimeReport.failures.Concat(new[]{detail}).ToArray();
            Directory.CreateDirectory(RuntimeFolder);
            File.WriteAllText(Path.Combine(RuntimeFolder,"runtime_review.json"),JsonUtility.ToJson(runtimeReport,true));
        }

        private static void AbortRuntimeReview()
        {
            if(runtimeRunning)FinishRuntimeReview("ABORTED","Assembly reload or play-mode exit interrupted HUD review.");
        }

        private static string PollRuntimeReview()
        {
            if(runtimeRunning)return "RUNNING frames="+(Time.frameCount-runtimeStartFrame)+" captureIssued="+captureIssued;
            string path=Path.Combine(RuntimeFolder,"runtime_review.json");
            if(!File.Exists(path))return "NOT_RUN";
            var report=JsonUtility.FromJson<RuntimeReport>(File.ReadAllText(path));
            return report.status+"; screenshot="+report.screenshotWritten+"; findings="+report.failures.Length+"; raw="+path;
        }

        private static string SaveReport(Report report)
        {
            string output=WorldMacroPlaytestAuthoring.Output+"/HUD";Directory.CreateDirectory(output);
            string path=output+"/hud_"+report.command+".json";File.WriteAllText(path,JsonUtility.ToJson(report,true));
            return JsonUtility.ToJson(report,true);
        }

        private static void RequireEditScene()
        {
            if(EditorApplication.isPlaying||SceneManager.GetActiveScene().path!=WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Open W_WorldMacro_Playtest in Edit mode. HUD authoring never starts Play or modifies shared PlayerRig.");
        }

        private static void EnsureFolder(string path)
        {
            string[] parts=path.Split('/');string current=parts[0];
            for(int i=1;i<parts.Length;i++){string next=current+"/"+parts[i];if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(current,parts[i]);current=next;}
        }

        private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
