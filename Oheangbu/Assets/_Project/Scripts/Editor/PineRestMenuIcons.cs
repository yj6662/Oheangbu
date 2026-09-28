using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Oheangbu.App.Prologue;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static string MenuIcons(){
   if(EditorApplication.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)throw new Exception("Saved edit scene required");
   var textures=new Texture2D[3];var names=new[]{"resume","save","exit"};
   for(int i=0;i<3;i++){
    var path=Folder+"/MenuIcons/"+names[i]+".png";
    var importer=AssetImporter.GetAtPath(path) as TextureImporter;
    if(importer==null)throw new Exception("Missing icon: "+path);
    importer.textureType=TextureImporterType.Default;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
    textures[i]=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
   }
   var scene=EditorSceneManager.OpenScene(Scene);var menu=UnityEngine.Object.FindFirstObjectByType<ProloguePauseMenu>();menu.MenuIcons=textures;EditorUtility.SetDirty(menu);
   var interaction=UnityEngine.Object.FindFirstObjectByType<PrologueInteraction>();interaction.InteractionIcon=textures[1];EditorUtility.SetDirty(interaction);
   EditorSceneManager.SaveScene(scene);return "Recraft menu and nearby interaction icons connected";
  }
 }
}
