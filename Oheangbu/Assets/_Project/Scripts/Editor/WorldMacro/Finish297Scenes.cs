using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 queue helper: open a saved scene by asset path (refuses when the open scene has unsaved changes).
 public static class Finish297Scenes
 {
  public static string Run(string path)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
   if(SceneManager.GetActiveScene().isDirty)throw new Exception("open scene has unsaved changes: "+SceneManager.GetActiveScene().path);
   if(path=="active")return SceneManager.GetActiveScene().path;
   EditorSceneManager.OpenScene(path,OpenSceneMode.Single);return SceneManager.GetActiveScene().path;
  }
 }
}
