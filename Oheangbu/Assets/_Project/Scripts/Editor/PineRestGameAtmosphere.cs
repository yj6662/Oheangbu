using System;
using Oheangbu.App;
using Oheangbu.App.Prologue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  const string StudyScene="Assets/_Project/Scenes/World/W_ArtStudy_PineRest.unity";
  static string VisualAudit(){
   if(!EditorApplication.isPlaying)throw new Exception("Play required");
   var visible=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
   int texts=0;foreach(var b in visible){var type=b.GetType();if(type.FullName=="UnityEngine.UI.Text"||type.FullName=="TMPro.TextMeshProUGUI"){var value=type.GetProperty("text")?.GetValue(b) as string;if(b.isActiveAndEnabled&&!string.IsNullOrEmpty(value))texts++;}}
   var interaction=Object.FindFirstObjectByType<PrologueInteraction>();var menu=Object.FindFirstObjectByType<ProloguePauseMenu>();var hud=Object.FindFirstObjectByType<HudController>();
   var result="activeUiText="+texts+" interactionHidden="+interaction.HideText+" hudTextHidden="+hud.HideText+" iconMenu="+menu.Textless+" skybox="+(RenderSettings.skybox!=null&&Camera.main.clearFlags==CameraClearFlags.Skybox);
   System.IO.File.WriteAllText("../Art/World/PineRest/visual_change_audit.txt",result);return result;
  }
  static string Atmosphere(){
   if(EditorApplication.isPlaying||SceneManager.GetActiveScene().isDirty)throw new Exception("Saved edit scene required");
   string path=Folder+"/Sky.mat";var sky=AssetDatabase.LoadAssetAtPath<Material>(path);if(sky==null){AssetDatabase.CopyAsset("Assets/_Project/Art/World/WorldMacro/Sky.mat",path);sky=AssetDatabase.LoadAssetAtPath<Material>(path);}
   sky.SetColor("_Horizon",new Color(.88f,.89f,.88f));sky.SetColor("_Zenith",new Color(.43f,.56f,.65f));sky.SetColor("_Cloud",new Color(.70f,.74f,.75f));sky.SetFloat("_CloudDensity",.38f);EditorUtility.SetDirty(sky);
   foreach(var scenePath in new[]{Scene,StudyScene}){
    var scene=EditorSceneManager.OpenScene(scenePath);var driver=Object.FindFirstObjectByType<WorldLookDriver>();var so=new SerializedObject(driver);so.FindProperty("_useSkybox").boolValue=true;so.FindProperty("_skyboxMaterial").objectReferenceValue=sky;so.FindProperty("_inkSkyProfile").objectReferenceValue=null;so.FindProperty("_regionalSkyProfile").objectReferenceValue=null;so.ApplyModifiedPropertiesWithoutUndo();driver.Apply();
    RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.55f,.60f,.63f);RenderSettings.ambientEquatorColor=new Color(.35f,.37f,.34f);RenderSettings.ambientGroundColor=new Color(.16f,.16f,.14f);
    foreach(var interaction in Object.FindObjectsByType<PrologueInteraction>(FindObjectsSortMode.None)){interaction.HideText=true;EditorUtility.SetDirty(interaction);}
    foreach(var menu in Object.FindObjectsByType<ProloguePauseMenu>(FindObjectsSortMode.None)){menu.Textless=true;EditorUtility.SetDirty(menu);}
    foreach(var hud in Object.FindObjectsByType<HudController>(FindObjectsSortMode.None)){var hs=new SerializedObject(hud);hs.FindProperty("HideText").boolValue=true;hs.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.RecordPrefabInstancePropertyModifications(hud);}
    EditorSceneManager.SaveScene(scene);
   }
   AssetDatabase.SaveAssets();EditorSceneManager.OpenScene(Scene);return "Study and Journey skybox connected; Journey text removed from interaction, HUD and pause menu";
  }
 }
}
