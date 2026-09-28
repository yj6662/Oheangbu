using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  const string TreeFolder="Assets/_Project/Art/World/PineRestTrees";
  static string Trees(){
   if(EditorApplication.isPlaying||SceneManager.GetActiveScene().isDirty)throw new Exception("Saved edit required");
   var names=new[]{"PhotorealPine"};var prefabs=new GameObject[1];
   for(int i=0;i<names.Length;i++){
    string folder=TreeFolder+"/"+names[i];prefabs[i]=AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/Tree.prefab");if(prefabs[i]!=null)continue;string modelPath=folder+"/model.fbx";
    var importer=(ModelImporter)AssetImporter.GetAtPath(modelPath);if(importer==null)throw new Exception("Generated model not imported: "+modelPath);
    importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.SaveAndReimport();
    string normalPath=folder+"/normal.png";if(AssetImporter.GetAtPath(normalPath) is TextureImporter ti){ti.textureType=TextureImporterType.NormalMap;ti.SaveAndReimport();}
    var mat=AssetDatabase.LoadAssetAtPath<Material>(folder+"/Tree.mat");if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,folder+"/Tree.mat");}
    mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/base_color.png"));mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));mat.EnableKeyword("_NORMALMAP");mat.SetFloat("_Smoothness",.08f);mat.SetFloat("_Metallic",0);mat.SetFloat("_Cull",0);mat.enableInstancing=true;EditorUtility.SetDirty(mat);
    var model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));try{model.name=names[i];foreach(var renderer in model.GetComponentsInChildren<Renderer>())renderer.sharedMaterials=renderer.sharedMaterials.Select(_=>mat).ToArray();prefabs[i]=PrefabUtility.SaveAsPrefabAsset(model,folder+"/Tree.prefab");}finally{Object.DestroyImmediate(model);}
   }
   int replaced=0;
   foreach(string scenePath in new[]{Scene,StudyScene}){
    var scene=EditorSceneManager.OpenScene(scenePath);
    var trees=Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t=>t.name.StartsWith("Korean_Pine_")||t.name=="Left frame pine"||t.name=="Inn shelter pine").OrderBy(t=>t.name).ToArray();
    foreach(var tree in trees){if(tree.Find("Meshy 7.1 tree")!=null)continue;var old=tree.GetComponentsInChildren<Renderer>();if(old.Length==0)continue;var bounds=old[0].bounds;foreach(var r in old.Skip(1))bounds.Encapsulate(r.bounds);
     var model=(GameObject)PrefabUtility.InstantiatePrefab(prefabs[replaced%prefabs.Length],scene);model.name="Meshy 7.1 tree";model.transform.SetParent(tree,false);model.transform.localRotation=Quaternion.identity;
     var renderers=model.GetComponentsInChildren<Renderer>();var b=renderers[0].bounds;foreach(var r in renderers.Skip(1))b.Encapsulate(r.bounds);float height=Mathf.Clamp(bounds.size.y,6,12);model.transform.localScale*=height/Mathf.Max(.01f,b.size.y);
     b=renderers[0].bounds;foreach(var r in renderers.Skip(1))b.Encapsulate(r.bounds);
     model.transform.position+=new Vector3(tree.position.x,tree.position.y-height*.055f,tree.position.z)-new Vector3(b.center.x,b.min.y,b.center.z);
     foreach(var r in old)r.enabled=false;
     // Original trunk colliders remain so gameplay navigation is unchanged.
     replaced++;
    }
    EditorSceneManager.SaveScene(scene);
   }
   AssetDatabase.SaveAssets();EditorSceneManager.OpenScene(Scene);return "Generated trees placed: "+replaced+"; original trunk collision preserved";
  }
 }
}
