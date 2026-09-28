using System;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Core;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class PlaytestMenuAuthoring
    {
        public const string Folder="Assets/_Project/Art/UI/PlaytestMenus";
        public const string ThemePath=Folder+"/PlaytestTheme.asset";
        public const string RuntimePath=Folder+"/PlaytestInputState.asset";
        public const string TitlePath="Assets/_Project/Scenes/World/W_Playtest_Title.unity";
        public static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/UIAudio/PlaytestMenus"));
        static readonly string StonePath="Assets/SeyeonjeongPavilion/Prefabs/SM_Stone_3.prefab";
        public static string Execute(string action)
        {
            switch(action)
            {
                case "install":return Install();
                case "validate":return Validate();
                case "bake":return WorldMapRuntimeBaker.Bake();
                case "register-scenes":return RegisterScenes();
                case "icon": {
                    RequireEdit();
                    var iconTheme=Asset<PlaytestUiThemeSO>(ThemePath);
                    string iconResult=CreateFragmentIcon(iconTheme,true);
                    EditorUtility.SetDirty(iconTheme);AssetDatabase.SaveAssets();return iconResult; }
                case "advanced-begin":return PlaytestMenuAdvancedReview.Execute("begin");
                case "advanced-poll":return PlaytestMenuAdvancedReview.Execute("poll");
                case "paper-begin":return WorldMapPaperReview.Execute("begin");
                case "paper-poll":return WorldMapPaperReview.Execute("poll");
                case "paper-abort":return WorldMapPaperReview.Execute("abort");
                case "paper-prepare":return WorldMapPaperReview.Execute("prepare");
                case "open-title":RequireEdit();EditorSceneManager.OpenScene(TitlePath);return "Title scene opened";
                case "open-play":RequireEdit();EditorSceneManager.OpenScene(WorldMacroPlaytestAuthoring.ScenePath);return "Playtest scene opened";
                default:return PlaytestMenuReview.Execute(action);
            }
        }
        static void RequireEdit(){if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");}
        static T Asset<T>(string path) where T:Object
        {var asset=AssetDatabase.LoadAssetAtPath<T>(path);if(asset==null)throw new InvalidOperationException("Missing "+path);return asset;}
        public static string Install()
        {
            RequireEdit();Directory.CreateDirectory(Output);Directory.CreateDirectory(Output+"/Backups");
            if(SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save the active scene before menu installation.");
            if(SceneManager.GetActiveScene().path!=WorldMacroPlaytestAuthoring.ScenePath)EditorSceneManager.OpenScene(WorldMacroPlaytestAuthoring.ScenePath);
            string backup=Output+"/Backups/W_WorldMacro_Playtest.before-menus.unity";
            if(!File.Exists(backup))File.Copy(WorldMacroPlaytestAuthoring.ScenePath,backup);
            var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();if(session==null)throw new InvalidOperationException("Playtest session missing");
            PrepareTexture(Folder+"/HanjiSurface.png",false,1024);
            PrepareTexture(Folder+"/TitlePalace.png",false,2048);
            var theme=AssetDatabase.LoadAssetAtPath<PlaytestUiThemeSO>(ThemePath);
            if(theme==null){theme=ScriptableObject.CreateInstance<PlaytestUiThemeSO>();AssetDatabase.CreateAsset(theme,ThemePath);}
            theme.Font=Asset<Font>(Folder+"/Fonts/NotoSansCJKkr-Regular.otf");
            theme.StrokeTemplates=Asset<JamoTemplateLibrarySO>("Assets/_Project/Data/Configs/JamoTemplateLibrary.asset");
            theme.PaperTexture=Asset<Texture2D>(Folder+"/HanjiSurface.png");theme.TitleBackdrop=Asset<Texture2D>(Folder+"/TitlePalace.png");
            theme.BrushStroke=Asset<Sprite>(WorldMacroPlaytestHudAuthoring.HpPath);theme.PromptPaper=Asset<Sprite>(WorldMacroPlaytestHudAuthoring.PromptPath);theme.LockRing=Asset<Sprite>(WorldMacroPlaytestHudAuthoring.RingPath);
            theme.PaperSound=Asset<AudioClip>("Assets/_Project/Audio/JourneyRenewal/ui_paper.wav");
            theme.ConfirmSound=Asset<AudioClip>("Assets/_Project/Audio/JourneyRenewal/ui_confirm.wav");
            theme.BackSound=Asset<AudioClip>("Assets/_Project/Audio/JourneyRenewal/ui_back.wav");
            var state=AssetDatabase.LoadAssetAtPath<GameplayRuntimeStateSO>(RuntimePath);
            if(state==null){state=ScriptableObject.CreateInstance<GameplayRuntimeStateSO>();AssetDatabase.CreateAsset(state,RuntimePath);}
            session.RuntimeState=state;session.Walker.Drawing.RuntimeState=state;session.Walker.Motor.RuntimeState=state;
            foreach(var seat in Object.FindObjectsByType<WorldMacroPalanquinSeat>(FindObjectsInactive.Include,FindObjectsSortMode.None)){seat.RuntimeState=state;EditorUtility.SetDirty(seat);}
            EditorUtility.SetDirty(session);EditorUtility.SetDirty(session.Walker.Drawing);EditorUtility.SetDirty(session.Walker.Motor);
            var hud=Object.FindFirstObjectByType<HudController>();
            string skinPath=Folder+"/ResourceBars.asset";
            var skin=AssetDatabase.LoadAssetAtPath<WorldMacroHudSkinProfileSO>(skinPath);
            if(skin==null){skin=Object.Instantiate(Asset<WorldMacroHudSkinProfileSO>(WorldMacroPlaytestHudAuthoring.ProfilePath));skin.name="ResourceBars";AssetDatabase.CreateAsset(skin,skinPath);}
            skin.KoreanFont=theme.Font;skin.UseInkBar=true;skin.HpPosition=new Vector2(86,96);skin.InkBarPosition=new Vector2(86,65);
            skin.HpSize=new Vector2(280,22);skin.InkBarSize=new Vector2(236,16);hud.Skin=skin;EditorUtility.SetDirty(hud);EditorUtility.SetDirty(skin);
            WorldMapRuntimeBaker.Bake();
            var map=Asset<WorldMapBakedDataSO>(WorldMapRuntimeBaker.DataPath);
            InputActionAsset actions=AssetDatabase.FindAssets("t:InputActionAsset").Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<InputActionAsset>).First(x=>x.FindActionMap("Menus",false)!=null);
            var existing=Object.FindFirstObjectByType<PlaytestUiRoot>();
            var root=existing!=null?existing:new GameObject("Playtest_UI").AddComponent<PlaytestUiRoot>();
            Bind(root,theme,state,session.Content,actions,map);
            InstallFragments(session);
            CreateFragmentIcon(theme,false);
            EditorUtility.SetDirty(theme);AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            // The title is a deliberately lightweight scene; it does not keep the 3D world resident.
            var title=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("TitleCamera",typeof(Camera),typeof(AudioListener)).GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=theme.Paper;camera.cullingMask=0;
            root=new GameObject("Playtest_UI").AddComponent<PlaytestUiRoot>();Bind(root,theme,state,Asset<Oheangbu.Data.World.WorldMacroPlaytestSO>(WorldMacroPlaytestAuthoring.Folder+"/Playtest.asset"),actions,map);
            EditorSceneManager.SaveScene(title,TitlePath);
            RegisterScenes();
            EditorSceneManager.OpenScene(WorldMacroPlaytestAuthoring.ScenePath);
            return Validate();
        }
        static string RegisterScenes()
        {
            RequireEdit();
            string backup=Output+"/Backups/EditorBuildSettings.before-menus.asset";
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if(!File.Exists(backup))File.Copy("ProjectSettings/EditorBuildSettings.asset",backup);
            var other=EditorBuildSettings.scenes.Where(x=>x.path!=TitlePath&&x.path!=WorldMacroPlaytestAuthoring.ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(TitlePath,true),new EditorBuildSettingsScene(WorldMacroPlaytestAuthoring.ScenePath,true)}.Concat(other).ToArray();
            return "Registered title/play scenes; preserved remaining editor scene entries";
        }
        static void Bind(PlaytestUiRoot root,PlaytestUiThemeSO theme,GameplayRuntimeStateSO state,Oheangbu.Data.World.WorldMacroPlaytestSO content,InputActionAsset actions,WorldMapBakedDataSO map)
        {
            root.Theme=theme;root.RuntimeState=state;root.Content=content;root.InputActions=actions;root.WorldSheet=WorldMacroBuilder.Sheet;root.MapData=map;EditorUtility.SetDirty(root);
        }
        static void InstallFragments(WorldMacroPlaytestSession session)
        {
            var group=GameObject.Find("Playtest_Seokgyeong");if(group==null)group=new GameObject("Playtest_Seokgyeong");
            var prefab=Asset<GameObject>(StonePath);
            foreach(var bundle in WorldMacroCollectionCatalog.AllBundles)
            {
                var old=group.transform.Find(bundle.Id);if(old!=null)Object.DestroyImmediate(old.gameObject);
                Vector3 p=bundle.Position;
                if(bundle.Id=="start")p=session.Content.StartFeet+Quaternion.Euler(0,session.Content.StartYaw,0)*new Vector3(1.6f,0,0);
                if(bundle.Id=="exit")p=Along(session.Content.MainPath,95)+new Vector3(1.5f,0,0);
                if(bundle.Id=="inn")p=session.Content.Points.First(x=>x.Id=="geumpyo_inn").Position+new Vector3(2.5f,0,-1.5f);
                p=GroundNear(p);
                var anchor=new GameObject(bundle.Id);anchor.transform.SetParent(group.transform,false);anchor.transform.position=p;
                var visuals=new GameObject("StoneFragments").transform;visuals.SetParent(anchor.transform,false);
                for(int i=0;i<3;i++)
                {
                    var stone=(GameObject)PrefabUtility.InstantiatePrefab(prefab,visuals);
                    foreach(var c in stone.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
                    var renderers=stone.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                    float scale=.28f/Mathf.Max(.01f,Mathf.Max(bounds.size.x,bounds.size.z));stone.transform.localScale*=scale;
                    bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                    stone.transform.position+=p-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)+new Vector3((i-1)*.22f,.02f,i%2*.16f);
                    stone.transform.Rotate(0,40+i*67,0,Space.World);
                }
                // Real engraved text, no billboard or emissive marker. The existing F prompt handles discovery.
                var textGo=new GameObject("Engraving",typeof(TextMesh));textGo.transform.SetParent(visuals,false);
                textGo.transform.localPosition=new Vector3(0,.20f,.03f);textGo.transform.localRotation=Quaternion.Euler(90,0,0);
                var text=textGo.GetComponent<TextMesh>();text.text=string.Join(" ",bundle.Fragments.Select(x=>x.Letter));text.font=Asset<Font>(Folder+"/Fonts/NotoSansCJKkr-Regular.otf");text.fontSize=64;text.characterSize=.09f;text.anchor=TextAnchor.MiddleCenter;text.color=new Color(.2f,.18f,.14f);
                textGo.GetComponent<MeshRenderer>().sharedMaterial=text.font.material;
                var pickup=anchor.AddComponent<WorldMacroFragmentPickup>();pickup.BundleId=bundle.Id;pickup.Session=session;pickup.VisualRoot=visuals;pickup.Radius=2.8f;
            }
        }
        static Vector3 Along(Vector3[] points,float meters)
        {
            for(int i=1;i<points.Length;i++){float length=Vector3.Distance(points[i-1],points[i]);if(length<.0001f)continue;if(meters<=length)return Vector3.Lerp(points[i-1],points[i],meters/length);meters-=length;}return points.Last();
        }
        static Vector3 GroundNear(Vector3 p)
        {
            Physics.SyncTransforms();
            var hits=Physics.RaycastAll(p+Vector3.up*4,Vector3.down,100,~0,QueryTriggerInteraction.Ignore)
                .Where(h=>h.normal.y>.65f && (h.collider.name.StartsWith("Terrain_")||h.collider.name.Contains("Floor")||h.collider.name.Contains("Walkable")||h.collider.name.Contains("Ramp"))).OrderBy(h=>h.distance).ToArray();
            if(hits.Length>0)return hits[0].point;
            return WorldMacroPlaytestAuthoring.Ground(p,true);
        }
        static void PrepareTexture(string path,bool sprite,int size)
        {
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var imp=(TextureImporter)AssetImporter.GetAtPath(path);imp.textureType=sprite?TextureImporterType.Sprite:TextureImporterType.Default;imp.spriteImportMode=SpriteImportMode.Single;
            imp.mipmapEnabled=false;imp.alphaIsTransparency=true;imp.wrapMode=TextureWrapMode.Clamp;imp.maxTextureSize=size;imp.SaveAndReimport();
        }
        static string CreateFragmentIcon(PlaytestUiThemeSO theme,bool force)
        {
            string path=Folder+"/SeokgyeongIcon.png";
            if(force||!File.Exists(path))
            {
                const int size=256;PreviewRenderUtility preview=null;GameObject stone=null;Texture2D result=null;bool added=false;
                string temporary=path+".rendering";
                try
                {
                    if(SystemInfo.maxTextureSize<size)throw new InvalidOperationException("GPU cannot render the 256px fragment icon.");
                    preview=new PreviewRenderUtility(false,true);
                    stone=Object.Instantiate(Asset<GameObject>(StonePath));stone.name="SeokgyeongIcon_Source";
                    stone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.Euler(14,-32,0));
                    foreach(var t in stone.GetComponentsInChildren<Transform>(true))t.gameObject.layer=0;
                    foreach(var r in stone.GetComponentsInChildren<Renderer>(true))r.enabled=true;
                    preview.AddSingleGO(stone);added=true;
                    var rs=stone.GetComponentsInChildren<Renderer>(true);if(rs.Length==0)throw new InvalidOperationException("Stone prefab has no renderer.");
                    long vertices=stone.GetComponentsInChildren<MeshFilter>(true).Where(x=>x.sharedMesh!=null).Sum(x=>(long)x.sharedMesh.vertexCount)
                        +stone.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(x=>x.sharedMesh!=null).Sum(x=>(long)x.sharedMesh.vertexCount);
                    if(vertices<=0||vertices>2000000)throw new InvalidOperationException("Stone preview geometry outside bounded vertex budget: "+vertices);
                    Bounds bounds=rs[0].bounds;foreach(var r in rs.Skip(1))bounds.Encapsulate(r.bounds);
                    float span=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
                    if(!float.IsFinite(span)||span<=.0001f)throw new InvalidOperationException("Stone prefab has invalid render bounds.");
                    stone.transform.localScale*=1.6f/span;
                    bounds=rs[0].bounds;foreach(var r in rs.Skip(1))bounds.Encapsulate(r.bounds);
                    stone.transform.position-=bounds.center;
                    bounds=rs[0].bounds;foreach(var r in rs.Skip(1))bounds.Encapsulate(r.bounds);

                    var camera=preview.camera;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
                    camera.allowHDR=false;camera.allowMSAA=true;camera.cullingMask=1;camera.orthographic=true;
                    camera.orthographicSize=bounds.extents.magnitude*1.08f;
                    float distance=Mathf.Max(3f,bounds.extents.magnitude*4f);
                    camera.transform.position=bounds.center+new Vector3(.65f,.55f,-1f).normalized*distance;
                    camera.transform.LookAt(bounds.center,Vector3.up);camera.nearClipPlane=.01f;camera.farClipPlane=distance+bounds.extents.magnitude*3f+2f;
                    preview.ambientColor=new Color(.42f,.40f,.36f,1);
                    preview.lights[0].intensity=1.25f;preview.lights[0].transform.rotation=Quaternion.Euler(35,-35,0);
                    preview.lights[1].intensity=.55f;preview.lights[1].transform.rotation=Quaternion.Euler(330,145,0);
                    preview.BeginPreview(new Rect(0,0,size,size),GUIStyle.none);preview.Render(true,false);
                    var rendered=preview.EndPreview() as RenderTexture;if(rendered==null)throw new InvalidOperationException("Preview render target unavailable.");
                    var previous=RenderTexture.active;
                    try
                    {
                        RenderTexture.active=rendered;result=new Texture2D(size,size,TextureFormat.RGBA32,false,false);
                        result.ReadPixels(new Rect(0,0,size,size),0,0);result.Apply(false,false);
                    }
                    finally{RenderTexture.active=previous;}
                    var pixels=result.GetPixels32();int visible=0;byte low=255,high=0;
                    foreach(var pixel in pixels)if(pixel.a>16){visible++;byte light=(byte)((pixel.r*3+pixel.g*6+pixel.b)/10);low=Math.Min(low,light);high=Math.Max(high,light);}
                    if(visible<size*size/100||high-low<12)throw new InvalidOperationException("Rendered stone icon is blank or flat (visible="+visible+", range="+(high-low)+").");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(temporary,result.EncodeToPNG());
                    if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
                }
                finally
                {
                    if(result!=null)Object.DestroyImmediate(result);
                    if(preview!=null)preview.Cleanup();
                    if(stone!=null&&!added)Object.DestroyImmediate(stone);
                    try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException e){Debug.LogWarning("Fragment icon temporary cleanup: "+e.Message);}
                }
            }
            PrepareTexture(path,true,256);theme.FragmentIcon=Asset<Sprite>(path);
            return "Regenerated bounded RGBA stone thumbnail: "+path;
        }
        public static string Validate()
        {
            var root=Object.FindFirstObjectByType<PlaytestUiRoot>();if(root==null)throw new InvalidOperationException("UI root absent");
            if(root.Theme==null||root.Theme.Font==null||root.MapData==null||root.RuntimeState==null||root.InputActions==null)throw new InvalidOperationException("UI root references incomplete");
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(TitlePath)==null)throw new InvalidOperationException("Title scene missing");
            var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(session!=null)
            {
                if(session.RuntimeState!=root.RuntimeState||session.Walker.Drawing.RuntimeState!=root.RuntimeState||session.Walker.Motor.RuntimeState!=root.RuntimeState)throw new InvalidOperationException("Shared input state mismatch");
                if(Object.FindObjectsByType<WorldMacroFragmentPickup>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length!=5)throw new InvalidOperationException("Expected five fragment bundles");
            }
            string text="PASS menu/theme/font/state references\n"+WorldMacroUiValidation.Execute()+"\n"+WorldMapRuntimeBaker.Validate();
            Directory.CreateDirectory(Output);File.WriteAllText(Output+"/installation_validation.txt",text);return text;
        }
    }
}
