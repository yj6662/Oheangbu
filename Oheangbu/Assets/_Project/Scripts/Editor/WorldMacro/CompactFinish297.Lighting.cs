using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 lighting foundation for the #296 candidate (SPEC-WORLD-FINISH-297 §1). Measured cause: InkSkyProfile forced a flat
 // black ambient with zero reflections every frame; the candidate renderer had no SSAO/AA; shadows ended at 50 m.
 // Candidate-only: gradient ambient from the realm skies, a private renderer (SSAO + the existing mist pass), SMAA, a post
 // grade, a lower warmer sun. Global (user-approved): PC pipeline shadow distance/cascades. Originals are recorded once.
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] class LightingOriginal297
  {
   public Vector3 sunEuler;public float sunIntensity,sunShadowStrength;public Color sunColor;
   public bool gradient;public string regionalProfile;public int regions;public int cameraRenderer;public int cameraAA,cameraAAQuality;
   public string postBackup;public float shadowDistance;public int cascades;public Vector3 cascadeSplit;public string rendererAsset;
  }
  // ART-SKY seeds (TEST): 청림 어둑한 녹음 / 적로 탄 주홍 / 철옹 쇠빛 회청 / 현강 물안개 청묵 / 황경 황토 기운
  static readonly (RealmId realm,Color horizon,Color zenith,Color cloud,float density)[] RealmSky297={
   (RealmId.Cheongrim,new Color(.82f,.86f,.80f),new Color(.63f,.71f,.66f),new Color(.50f,.56f,.50f),.18f),
   (RealmId.Jeokro,new Color(.90f,.81f,.71f),new Color(.73f,.66f,.60f),new Color(.60f,.52f,.46f),.16f),
   (RealmId.Cheolong,new Color(.80f,.84f,.88f),new Color(.60f,.66f,.74f),new Color(.52f,.56f,.62f),.20f),
   (RealmId.Hyeongang,new Color(.80f,.86f,.88f),new Color(.58f,.66f,.70f),new Color(.54f,.60f,.62f),.24f),
   (RealmId.Hwanggyeong,new Color(.90f,.86f,.76f),new Color(.72f,.72f,.66f),new Color(.62f,.60f,.52f),.14f),
  };

  static string Lighting297(bool revert)
  {
   RequireClean292();var session=Session292();var scene=SceneManager.GetActiveScene();string file=K297+"/lighting-original.json";
   var driver=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldLookDriver>(true)).Single();var ds=new SerializedObject(driver);
   var sky=(InkSkyProfile)ds.FindProperty("_inkSkyProfile").objectReferenceValue;var geo=(WorldMacroSheetSO)ds.FindProperty("_skyGeography").objectReferenceValue;
   var sun=RenderSettings.sun!=null?RenderSettings.sun:Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l=>l.type==LightType.Directional&&l.gameObject.scene==scene);
   var camera=session.Walker.ViewCamera;var cam=camera.GetUniversalAdditionalCameraData();
   var volume=Object.FindObjectsByType<Volume>(FindObjectsSortMode.None).Where(v=>v.gameObject.scene==scene&&v.isGlobal).OrderByDescending(v=>v.priority).First();
   var profile=volume.sharedProfile;string profilePath=AssetDatabase.GetAssetPath(profile);
   var pipe=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
   int rendererIndex=new SerializedObject(cam).FindProperty("m_RendererIndex").intValue;
   if(revert)
   {
    var o=JsonUtility.FromJson<LightingOriginal297>(File.ReadAllText(file));
    sun.transform.eulerAngles=o.sunEuler;sun.intensity=o.sunIntensity;sun.color=o.sunColor;sun.shadowStrength=o.sunShadowStrength;EditorUtility.SetDirty(sun);
    sky.GradientAmbient=o.gradient;EditorUtility.SetDirty(sky);
    ds.FindProperty("_regionalSkyProfile").objectReferenceValue=string.IsNullOrEmpty(o.regionalProfile)?null:AssetDatabase.LoadAssetAtPath<RegionalInkSkyProfile>(o.regionalProfile);ds.ApplyModifiedPropertiesWithoutUndo();
    if(geo!=null&&o.regions==0){geo.Regions=Array.Empty<WorldMacroSheetSO.RegionSpec>();EditorUtility.SetDirty(geo);}
    camera.GetUniversalAdditionalCameraData().SetRenderer(o.cameraRenderer);foreach(var c in SceneCameras297(scene))if(new SerializedObject(c.GetUniversalAdditionalCameraData()).FindProperty("m_RendererIndex").intValue!=o.cameraRenderer)c.GetUniversalAdditionalCameraData().SetRenderer(o.cameraRenderer);
    cam.antialiasing=(AntialiasingMode)o.cameraAA;cam.antialiasingQuality=(AntialiasingQuality)o.cameraAAQuality;EditorUtility.SetDirty(cam);
    if(File.Exists(o.postBackup)){File.Copy(o.postBackup,profilePath,true);AssetDatabase.ImportAsset(profilePath,ImportAssetOptions.ForceUpdate);}
    pipe.shadowDistance=o.shadowDistance;pipe.shadowCascadeCount=o.cascades;pipe.cascade4Split=o.cascadeSplit;EditorUtility.SetDirty(pipe);
    AssetDatabase.SaveAssets();Save292();return "Reverted #297 lighting to the recorded originals";
   }
   if(!File.Exists(file))
   {
    string backup=K297+"/Baseline/"+Path.GetFileName(profilePath)+".before";Directory.CreateDirectory(K297+"/Baseline");File.Copy(profilePath,backup,true);
    File.WriteAllText(file,JsonUtility.ToJson(new LightingOriginal297{sunEuler=sun.transform.eulerAngles,sunIntensity=sun.intensity,sunColor=sun.color,sunShadowStrength=sun.shadowStrength,
     gradient=sky.GradientAmbient,regionalProfile=AssetDatabase.GetAssetPath(ds.FindProperty("_regionalSkyProfile").objectReferenceValue),regions=geo!=null?geo.Regions.Length:0,
     cameraRenderer=rendererIndex,cameraAA=(int)cam.antialiasing,cameraAAQuality=(int)cam.antialiasingQuality,postBackup=backup,
     shadowDistance=pipe.shadowDistance,cascades=pipe.shadowCascadeCount,cascadeSplit=pipe.cascade4Split},true));
   }
   var report=new List<string>();
   // 1. gradient ambient (+ realm tint) and realm skies
   sky.GradientAmbient=true;sky.AmbientSky=new Color(.52f,.54f,.55f);sky.AmbientEquator=new Color(.45f,.44f,.41f);sky.AmbientGround=new Color(.22f,.20f,.17f);
   sky.GradientIntensity=1f;sky.RegionalTint=.35f;sky.GradientReflection=.45f;EditorUtility.SetDirty(sky);
   var regional=Asset297<RegionalInkSkyProfile>("Data/RegionalSky297.asset",()=>ScriptableObject.CreateInstance<RegionalInkSkyProfile>());
   regional.BlendDistance=1200;regional.ResponseSeconds=2.5f;regional.DefaultRealm=RealmId.Hwanggyeong;
   regional.Regions=RealmSky297.Select(r=>new RegionalInkSkyProfile.Entry{Realm=r.realm,Horizon=r.horizon,Zenith=r.zenith,Cloud=r.cloud,CloudDensity=r.density}).ToArray();EditorUtility.SetDirty(regional);
   if(geo!=null&&(geo.Regions==null||geo.Regions.Length==0))
   {
    geo.Regions=session.MountainLayout.Realms.Select(a=>new WorldMacroSheetSO.RegionSpec{Id=a.Id.ToLowerInvariant(),Label=a.Label,Realm=(RealmId)Enum.Parse(typeof(RealmId),a.Id,true),Polygon=a.Polygon.ToArray(),Density=a.TreeDensity}).ToArray();
    EditorUtility.SetDirty(geo);
   }
   ds.FindProperty("_regionalSkyProfile").objectReferenceValue=regional;ds.ApplyModifiedPropertiesWithoutUndo();
   report.Add("ambient=gradient (realm-tinted) reflection="+sky.GradientReflection+" regions="+(geo!=null?geo.Regions.Length:0));
   // 2. private renderer: the current candidate renderer's mist pass + SSAO; registered at one index in every quality pipeline
   var source=(UniversalRendererData)new SerializedObject(pipe).FindProperty("m_RendererDataList").GetArrayElementAtIndex(rendererIndex).objectReferenceValue;
   string rpath=A297+"/Renderer297.asset";var data=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rpath);
   if(data==null){data=Object.Instantiate(source);data.rendererFeatures.Clear();data.name="Renderer297";DevSceneKit.EnsureFolder(A297);AssetDatabase.CreateAsset(data,rpath);}
   var pcRenderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
   var ssaoSource=pcRenderer.rendererFeatures.FirstOrDefault(f=>f!=null&&f.GetType().Name=="ScreenSpaceAmbientOcclusion");
   var ssao=data.rendererFeatures.FirstOrDefault(f=>f!=null&&f.GetType().Name=="ScreenSpaceAmbientOcclusion");
   if(ssao==null&&ssaoSource!=null){ssao=Object.Instantiate(ssaoSource);ssao.name="SSAO297";AssetDatabase.AddObjectToAsset(ssao,data);data.rendererFeatures.Insert(0,ssao);}
   if(ssao!=null)
   {
    var so=new SerializedObject(ssao);void F(string p,float v){var q=so.FindProperty("m_Settings."+p);if(q!=null)q.floatValue=v;}void I(string p,int v){var q=so.FindProperty("m_Settings."+p);if(q!=null){if(q.propertyType==SerializedPropertyType.Boolean)q.boolValue=v!=0;else q.intValue=v;}}
    F("Intensity",.9f);F("Radius",.55f);F("DirectLightingStrength",.30f);F("Falloff",140f);I("Downsample",0);I("AfterOpaque",0);I("Source",1);I("NormalSamples",1);I("Samples",1);I("BlurQuality",1);
    so.ApplyModifiedPropertiesWithoutUndo();ssao.SetActive(true);EditorUtility.SetDirty(ssao);
   }
   foreach(var f in source.rendererFeatures.OfType<FullScreenPassRendererFeature>())
   {
    if(data.rendererFeatures.OfType<FullScreenPassRendererFeature>().Any(x=>x.name==f.name))continue;
    var copy=Object.Instantiate(f);copy.name=f.name;AssetDatabase.AddObjectToAsset(copy,data);data.rendererFeatures.Add(copy);
   }
   // 수묵담채 screen pass (candidate shader derived from the lookdev InkWorldPost): 450 BeforeRenderingTransparents, depth +
   // normals; material values are created once and then owned by the material (tuning is not overwritten here)
   var inkMat=AssetDatabase.LoadAssetAtPath<Material>(A297+"/Materials/M_InkWash297.mat");
   if(inkMat==null){DevSceneKit.EnsureFolder(A297+"/Materials");inkMat=new Material(Shader.Find("Oheangbu/Finish297/InkWash"));AssetDatabase.CreateAsset(inkMat,A297+"/Materials/M_InkWash297.mat");}
   var ink=data.rendererFeatures.OfType<FullScreenPassRendererFeature>().FirstOrDefault(f=>f.name=="InkWash297");
   if(ink==null){ink=ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();ink.name="InkWash297";AssetDatabase.AddObjectToAsset(ink,data);data.rendererFeatures.Add(ink);}
   ink.passMaterial=inkMat;ink.injectionPoint=FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingTransparents;ink.fetchColorBuffer=true;
   ink.requirements=ScriptableRenderPassInput.Depth|ScriptableRenderPassInput.Normal;ink.passIndex=0;EditorUtility.SetDirty(ink);
   var rs=new SerializedObject(data);var map=rs.FindProperty("m_RendererFeatureMap");map.arraySize=data.rendererFeatures.Count;
   for(int i=0;i<map.arraySize;i++){AssetDatabase.TryGetGUIDAndLocalFileIdentifier(data.rendererFeatures[i],out string guid,out long id);map.GetArrayElementAtIndex(i).longValue=id;}
   // Forward (measured 2026-09-28, Cheolong interior Editor Play: Forward+ CPU 16.80 ms vs Forward 13.47 ms). The one-renderer
   // V4 cave's 4-lights-per-object limit is solved by chunking its render mesh instead (cave-portal).
   rs.FindProperty("m_RenderingMode").intValue=0;
   rs.ApplyModifiedPropertiesWithoutUndo();data.SetDirty();EditorUtility.SetDirty(data);
   int index=-1;
   foreach(var q in Enumerable.Range(0,QualitySettings.names.Length).Select(QualitySettings.GetRenderPipelineAssetAt).OfType<UniversalRenderPipelineAsset>().Distinct())
   {
    var sq=new SerializedObject(q);var list=sq.FindProperty("m_RendererDataList");int at=-1;
    for(int i=0;i<list.arraySize;i++)if(list.GetArrayElementAtIndex(i).objectReferenceValue==data)at=i;
    if(at<0){at=list.arraySize;list.InsertArrayElementAtIndex(at);list.GetArrayElementAtIndex(at).objectReferenceValue=data;sq.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(q);}
    if(index>=0&&index!=at)throw new Exception("Renderer297 index differs between quality pipelines");index=at;
   }
   foreach(var c in SceneCameras297(scene))if(new SerializedObject(c.GetUniversalAdditionalCameraData()).FindProperty("m_RendererIndex").intValue==rendererIndex)c.GetUniversalAdditionalCameraData().SetRenderer(index);
   cam.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;cam.antialiasingQuality=AntialiasingQuality.High;EditorUtility.SetDirty(cam);
   report.Add("renderer297 index="+index+" Forward features="+string.Join(",",data.rendererFeatures.Select(f=>f.name))+" camera AA=SMAA High");
   // 3. post grade (candidate profile; the original file is backed up)
   T Comp<T>() where T:VolumeComponent{if(!profile.TryGet<T>(out var c)){c=profile.Add<T>(false);c.name=typeof(T).Name;AssetDatabase.AddObjectToAsset(c,profile);}return c;}
   var tone=Comp<Tonemapping>();tone.mode.Override(TonemappingMode.Neutral);
   var grade=Comp<ColorAdjustments>();grade.postExposure.Override(.15f);grade.contrast.Override(10f);grade.saturation.Override(PostSaturation297);
   var wb=Comp<WhiteBalance>();wb.temperature.Override(6f);wb.tint.Override(0f);
   var smh=Comp<ShadowsMidtonesHighlights>();smh.shadows.Override(new Vector4(1.02f,.99f,.95f,-.03f));smh.midtones.Override(new Vector4(1f,1f,.99f,0f));smh.highlights.Override(new Vector4(1.02f,1f,.96f,0f));
   var vig=Comp<Vignette>();vig.intensity.Override(.16f);vig.smoothness.Override(.5f);vig.color.Override(new Color(.10f,.09f,.08f));
   // saturation/bloom shared with the attraction pass (CompactFinish297.Attraction.cs, TEST): colour = guidance, Bloom = light sources
   var bloom=Comp<Bloom>();bloom.threshold.Override(BloomThreshold297);bloom.intensity.Override(BloomIntensity297);bloom.scatter.Override(BloomScatter297);
   EditorUtility.SetDirty(profile);report.Add("post: Neutral tonemapping, exposure .15, contrast 10, saturation "+PostSaturation297+" (tone owned by InkWash297), WB +6, SMH warm ink shadows/hanji highlights, vignette .16, bloom "+BloomIntensity297+"@"+BloomThreshold297+" scatter "+BloomScatter297);
   // 4. sun: lower and warmer so forms read (long shadows), balanced against the new ambient
   sun.transform.eulerAngles=new Vector3(33,322,0);sun.intensity=1.42f;sun.color=new Color(1f,.94f,.86f);sun.shadowStrength=.88f;sun.shadows=LightShadows.Soft;EditorUtility.SetDirty(sun);
   // 5. global (user-approved): longer shadows with redistributed cascades on the PC pipeline
   pipe.shadowDistance=150;pipe.shadowCascadeCount=4;pipe.cascade4Split=new Vector3(.067f,.2f,.46f);EditorUtility.SetDirty(pipe);
   report.Add("sun 33deg/1.42 warm; PC shadows 150 m, 4 cascades (.067/.2/.46)");
   AssetDatabase.SaveAssets();Save292();
   string text=string.Join("\n",report);File.WriteAllText(K297+"/lighting.txt",text);return text;
  }

  static IEnumerable<Camera> SceneCameras297(Scene scene)=>scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).Where(c=>c.cameraType==CameraType.Game);

  static T Asset297<T>(string relative,Func<T> create) where T:Object
  {
   string path=A297+"/"+relative;DevSceneKit.EnsureFolder(Path.GetDirectoryName(path).Replace('\\','/'));
   var a=AssetDatabase.LoadAssetAtPath<T>(path);if(a==null){a=create();AssetDatabase.CreateAsset(a,path);}return a;
  }
 }
}
