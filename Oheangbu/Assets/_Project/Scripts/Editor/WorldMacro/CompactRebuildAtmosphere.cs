using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Oheangbu.App.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static Func<float,float,RaycastHit> FinalSurface(Scene scene)
  {
   var colliders=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshCollider>()).Where(c=>c.name.StartsWith("Terrain_")||c.name=="Cave_ExteriorCover237"||c.name=="Approach_Bank240").ToArray();
   var bounds=colliders.Select(c=>c.bounds).ToArray();
   return (x,z)=>{bool found=false;RaycastHit best=default;var ray=new Ray(new Vector3(x,2000,z),Vector3.down);
    for(int i=0;i<colliders.Length;i++){var b=bounds[i];if(x<b.min.x-.01f||x>b.max.x+.01f||z<b.min.z-.01f||z>b.max.z+.01f)continue;if(colliders[i].Raycast(ray,out var hit,4000)&&(!found||hit.point.y>best.point.y)){best=hit;found=true;}}
    if(!found)throw new Exception("No final world surface at "+x+","+z);return best;};
  }
  static string ExportFinalHeights(Scene scene)
  {
   var sample=FinalSurface(scene);const int width=1600,height=2400;const float step=2.5f;
   string dir=Output+"/Cartography";Directory.CreateDirectory(dir);string temp=dir+"/heights.pending";
   using(var writer=new BinaryWriter(File.Create(temp)))for(int z=0;z<height;z++)for(int x=0;x<width;x++)writer.Write(sample((x+.5f)*step,(z+.5f)*step).point.y);
   File.Copy(temp,dir+"/heights.f32",true);File.Delete(temp);
   File.WriteAllText(dir+"/heights.json","{\"width\":1600,\"height\":2400,\"cellMetres\":2.5,\"source\":\"final terrain colliders plus cave exterior cover; highest surface\"}");
   return "Final scene surface exported: 1600x2400 / 2.5m; cave cover included";
  }
  static string Atmosphere238(string command)
  {
   string dir=Output+"/Atmosphere238";Directory.CreateDirectory(dir);
   var scene=SceneManager.GetActiveScene();var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
   if(scene.path!=receipt.scene)throw new Exception("Open latest candidate first");
   var roots=scene.GetRootGameObjects();var session=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
   string folder=Path.GetDirectoryName(receipt.scene).Replace('\\','/')+"/Atmosphere238";
   if(command=="export")return ExportFinalHeights(scene);
   if(command.StartsWith("capture:"))return AtmosphereCapture(command.Substring(8));
   if(command=="audit")
   {
    var lines=new List<string>();void Check(bool ok,string name)=>lines.Add((ok?"PASS ":"FAIL ")+name);
    var terrain=roots.SelectMany(g=>g.GetComponentsInChildren<Renderer>()).Where(r=>r.name.StartsWith("Terrain_")||r.name=="Cave_ExteriorCover237").ToArray();
    Check(terrain.Length>0&&terrain.All(r=>r.sharedMaterials.All(m=>m.HasProperty("_RebuildDetailStrength")&&m.GetFloat("_RebuildDetailStrength")>.5f)),"all "+terrain.Length+" ground chunks and cave host use near material detail");
    Check(terrain.All(r=>r.sharedMaterials.All(m=>m.GetFloat("_RebuildScreenFog")>.5f)),"terrain surface wash bypassed for camera fog");
    var mats=roots.SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).Where(r=>r.gameObject.activeInHierarchy).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct();
    Check(mats.All(m=>m.shader!=null&&m.shader.isSupported&&!ShaderUtil.ShaderHasError(m.shader)),"scene shaders valid");
    var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(folder+"/Renderer.asset");
    Check(renderer!=null&&renderer.rendererFeatures.Count(f=>f.name=="CompactMist238"&&f.isActive)==1,"one private fog feature");
    var auditedFog=AssetDatabase.LoadAssetAtPath<Material>(folder+"/Fog.mat");Check(auditedFog!=null&&!ShaderUtil.ShaderHasError(auditedFog.shader),"fog shader compiled");
    var cameraData=session.Walker.ViewCamera.GetUniversalAdditionalCameraData();int selected=new SerializedObject(cameraData).FindProperty("m_RendererIndex").intValue;
    var pipelines=Enumerable.Range(0,QualitySettings.names.Length).Select(QualitySettings.GetRenderPipelineAssetAt).OfType<UniversalRenderPipelineAsset>().Distinct();
    Check(pipelines.All(p=>{var data=new SerializedObject(p).FindProperty("m_RendererDataList");return selected>=0&&selected<data.arraySize&&data.GetArrayElementAtIndex(selected).objectReferenceValue==renderer;}),"camera fog registered at selected index in every quality pipeline");
    var activePipeline=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
    Check(selected>=0&&cameraData.scriptableRenderer==activePipeline.GetRenderer(selected),"active camera uses selected renderer: "+activePipeline.name);
    Check(session.Content.SaveSlot=="world-demo-compact-cave-v4"&&(EditorApplication.isPlaying?session.TestSaveSuffix.StartsWith("_compact_slice_"):session.TestSaveSuffix==""),"candidate slot preserved; diagnostics isolated");
    string report=string.Join("\n",lines);File.WriteAllText(dir+"/audit.txt",report);return report;
   }
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
   if(command!="build")throw new ArgumentException(command);
   DevSceneKit.EnsureFolder(folder);var groundShader=Shader.Find("Oheangbu/Study/InkPaintingGround");
   var mapped=new Dictionary<Material,Material>();int grounds=0;
   Material Copy(Material original)
   {
    if(original==null)return null;if(mapped.TryGetValue(original,out var done))return done;
    if(original.shader!=groundShader&&!original.HasProperty("_WashStrength")&&!original.HasProperty("_CIAirStrengths"))return original;
    string path=AssetDatabase.GetAssetPath(original);var material=path.StartsWith(folder+"/")?original:new Material(original);
    if(material!=original){if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original,out string id,out long local))throw new Exception("Unsaved material "+original.name);material=SurveyMaterial(material,folder+"/M_"+id+"_"+local+".mat");}
    if(material.shader==groundShader)
    {
     material.SetFloat("_RebuildDetailStrength",.88f);material.SetVector("_RebuildDetailRange",new Vector4(28,155,0,0));material.SetFloat("_RebuildScreenFog",1);
     material.SetVector("_Tiling",new Vector4(.30f,.32f,.24f,0));material.SetFloat("_CINormalStrength",.9f);material.SetFloat("_BumpScale",.65f);// Preserve authored atlas UVs on the inn and other source meshes.
     material.SetVector("_CIDetailRange",new Vector4(28,165,0,0));grounds++;
    }
    // Surface atmosphere is disabled only on the candidate's private materials.
    if(material.HasProperty("_CIAtmosphereOverride"))material.SetFloat("_CIAtmosphereOverride",1);
    if(material.HasProperty("_CIAirStrengths"))material.SetVector("_CIAirStrengths",Vector4.zero);
    if(material.HasProperty("_WashStrength"))material.SetFloat("_WashStrength",0);
    EditorUtility.SetDirty(material);mapped.Add(original,material);return material;
   }
   foreach(var renderer in roots.SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)))renderer.sharedMaterials=renderer.sharedMaterials.Select(Copy).ToArray();
   foreach(var art in roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)))
   {
    var sheet=Object.Instantiate(art.Sheet);sheet.name="CompactRebuildAtmosphereArt";foreach(var prototype in sheet.Prototypes)foreach(var lod in prototype.Lods)foreach(var part in lod.Parts)part.Material=Copy(part.Material);
    art.Sheet=SavePrivate(sheet,folder+"/ArtSheet.asset");art.Invalidate();
   }
   var camera=session.Walker.ViewCamera.GetUniversalAdditionalCameraData();var pipe=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;var po=new SerializedObject(pipe);var list=po.FindProperty("m_RendererDataList");
   string rendererPath=folder+"/Renderer.asset";var privateRenderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
   if(privateRenderer==null)
   {
    var co=new SerializedObject(camera);int current=co.FindProperty("m_RendererIndex").intValue;if(current<0)current=po.FindProperty("m_DefaultRendererIndex").intValue;
    string source=AssetDatabase.GetAssetPath(list.GetArrayElementAtIndex(current).objectReferenceValue);
    File.Copy(AssetDatabase.GetAssetPath(pipe),dir+"/pipeline_before.asset",true);
    if(!AssetDatabase.CopyAsset(source,rendererPath))throw new Exception("Cannot copy renderer");privateRenderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
   }
   int index=-1;for(int i=0;i<list.arraySize;i++)if(list.GetArrayElementAtIndex(i).objectReferenceValue==privateRenderer)index=i;
   if(index<0){index=list.arraySize;list.InsertArrayElementAtIndex(index);list.GetArrayElementAtIndex(index).objectReferenceValue=privateRenderer;po.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(pipe);}
   // Runtime settings switch PC/Mobile pipeline. Keep the candidate at the same
   // appended index in both, without replacing any existing or default entry.
   var qualityPipelines=Enumerable.Range(0,QualitySettings.names.Length).Select(QualitySettings.GetRenderPipelineAssetAt).OfType<UniversalRenderPipelineAsset>().Distinct().ToArray();
   foreach(var other in qualityPipelines)
   {
    var settings=new SerializedObject(other);var renderers=settings.FindProperty("m_RendererDataList");
    if(index<renderers.arraySize&&renderers.GetArrayElementAtIndex(index).objectReferenceValue==privateRenderer)continue;
    if(renderers.arraySize!=index)throw new Exception("Candidate renderer index conflicts with "+other.name);
    string backup=dir+"/"+other.name+"_before.asset";if(!File.Exists(backup))File.Copy(AssetDatabase.GetAssetPath(other),backup);
    renderers.InsertArrayElementAtIndex(index);renderers.GetArrayElementAtIndex(index).objectReferenceValue=privateRenderer;settings.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(other);
   }
   camera.SetRenderer(index);
   foreach(var feature in privateRenderer.rendererFeatures)if(feature!=null&&feature.name.Contains("InkWorldPost"))feature.SetActive(false);
   var fog=SurveyMaterial(new Material(Shader.Find("Oheangbu/Compact/RebuildFog")),folder+"/Fog.mat");fog.SetColor("_FogColor",new Color(.72f,.76f,.73f,1));fog.SetVector("_FogRange",new Vector4(170,1250,0,0));EditorUtility.SetDirty(fog);
   var pass=privateRenderer.rendererFeatures.OfType<FullScreenPassRendererFeature>().FirstOrDefault(f=>f.name=="CompactMist238");
   if(pass==null){pass=ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();pass.name="CompactMist238";AssetDatabase.AddObjectToAsset(pass,privateRenderer);privateRenderer.rendererFeatures.Add(pass);}
   pass.passMaterial=fog;pass.fetchColorBuffer=true;pass.requirements=ScriptableRenderPassInput.Depth;pass.injectionPoint=FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;pass.SetActive(true);
   var rs=new SerializedObject(privateRenderer);var map=rs.FindProperty("m_RendererFeatureMap");map.arraySize=privateRenderer.rendererFeatures.Count;
   for(int i=0;i<map.arraySize;i++){AssetDatabase.TryGetGUIDAndLocalFileIdentifier(privateRenderer.rendererFeatures[i],out string guid,out long id);map.GetArrayElementAtIndex(i).longValue=id;}rs.ApplyModifiedPropertiesWithoutUndo();
   EditorUtility.SetDirty(pass);EditorUtility.SetDirty(privateRenderer);privateRenderer.SetDirty();AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
   string result="Private renderer index="+index+"; detailed ground materials="+grounds+"; cloned materials="+mapped.Count+"; geometry and save unchanged";File.WriteAllText(dir+"/build.txt",result);return result;
  }
  static string AtmosphereCapture(string label)
  {
   var roots=SceneManager.GetActiveScene().GetRootGameObjects();var session=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();var ground=FinalSurface(SceneManager.GetActiveScene());
   var go=new GameObject("Temporary_Atmosphere238_Camera");var camera=go.AddComponent<Camera>();camera.CopyFrom(session.Walker.ViewCamera);camera.enabled=false;EditorUtility.CopySerialized(session.Walker.ViewCamera.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
   var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).ToArray();var observers=art.Select(a=>a.Observer).ToArray();foreach(var a in art)a.Observer=camera;
   var points=new[]{new Vector2(3110,2228),new Vector2(3355,1888),new Vector2(3280,1955),new Vector2(2050,2920)};var rt=new RenderTexture(1920,1080,24);var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);var prior=RenderTexture.active;
   try{camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=65;
    for(int i=0;i<points.Length;i++){var p=points[i];var eye=ground(p.x,p.y).point+Vector3.up*1.7f;var direction=i==0?new Vector3(0,-.3f,1):i==1?new Vector3(-.45f,-.30f,.85f):i==2?new Vector3(-.7f,-.1f,.7f):new Vector3(.8f,-.12f,-.5f);camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(direction));camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(Output+"/Atmosphere238/"+label+"_"+i+".png",image.EncodeToPNG());}
   }finally{for(int i=0;i<art.Length;i++)art[i].Observer=observers[i];camera.targetTexture=null;RenderTexture.active=prior;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(go);}
   return "Four final-ground viewpoints captured with candidate vegetation: "+label;
  }
 }
}
