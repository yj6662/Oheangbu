using System;
using System.IO;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  const string MenuInputReport="../Art/World/PineRest/menu_input.txt";
  static Keyboard menuKeyboard,priorMenuKeyboard;
  static Key[] menuKeys;
  static int menuPhase;
  static double menuNext;
  static ProloguePauseMenu checkedMenu;
  static Mouse menuMouse,priorMenuMouse;
  static MouseState menuMouseState;
  static readonly List<string> menuEvidence=new List<string>();
  static string MenuInputCheck(){
   var session=RoadRestSession();if(menuKeyboard!=null)throw new Exception("Menu check already running");
   checkedMenu=UnityEngine.Object.FindFirstObjectByType<ProloguePauseMenu>();checkedMenu.SetOpen(false);
   menuEvidence.Clear();menuPhase=0;
   priorMenuKeyboard=Keyboard.current;menuKeyboard=InputSystem.AddDevice<Keyboard>("JourneyMenuAudit");menuKeyboard.MakeCurrent();menuKeys=new[]{Key.Escape};
   priorMenuMouse=Mouse.current;menuMouse=InputSystem.AddDevice<Mouse>("JourneyMenuMouseAudit");menuMouse.MakeCurrent();menuMouseState=new MouseState{position=Vector2.zero};
   InputSystem.onBeforeUpdate+=MenuInputFeed;EditorApplication.update+=MenuInputTick;menuNext=EditorApplication.timeSinceStartup+.5;
   File.WriteAllText(MenuInputReport,"RUNNING");return "Virtual keyboard menu audit started in isolated save";
  }
  static void MenuInputFeed(){if(menuKeyboard!=null)InputSystem.QueueStateEvent(menuKeyboard,new KeyboardState(menuKeys));if(menuMouse!=null)InputSystem.QueueStateEvent(menuMouse,menuMouseState);}
  static void MenuRequire(bool condition,string detail){if(!condition)throw new Exception(detail);menuEvidence.Add("PASS "+detail);}
  static void MenuInputTick(){
   if(EditorApplication.timeSinceStartup<menuNext)return;
   try{
    if(!EditorApplication.isPlaying)throw new Exception("Play stopped");
    var events=UnityEngine.Object.FindFirstObjectByType<EventSystem>();
    switch(menuPhase){
     case 0: MenuRequire(checkedMenu.IsOpen&&Time.timeScale==0,"Escape opens and pauses");MenuRequire(events.currentSelectedGameObject.name=="Continue","Continue initially focused");menuKeys=Array.Empty<Key>();break;
     case 1: menuKeys=new[]{Key.RightArrow};break;
     case 2: MenuRequire(events.currentSelectedGameObject.name=="Save","Right arrow selects Save");menuKeys=Array.Empty<Key>();break;
     case 3: menuKeys=new[]{Key.Enter};break;
     case 4: MenuRequire(MenuSaved(),"Enter saves and displays green result");menuKeys=Array.Empty<Key>();CaptureMenuCanvas();break;
     case 5: menuKeys=new[]{Key.LeftArrow};break;
     case 6: MenuRequire(events.currentSelectedGameObject.name=="Continue","Left arrow returns to Continue");menuKeys=Array.Empty<Key>();break;
     case 7: menuKeys=new[]{Key.Enter};break;
     case 8: MenuRequire(!checkedMenu.IsOpen&&Time.timeScale>0,"Enter resumes");menuKeys=Array.Empty<Key>();break;
     case 9: menuKeys=new[]{Key.Escape};break;
     case 10: MenuRequire(checkedMenu.IsOpen,"Escape reopens");menuKeys=Array.Empty<Key>();break;
     case 11: menuKeys=new[]{Key.Escape};break;
     case 12: MenuRequire(!checkedMenu.IsOpen&&Time.timeScale>0,"Escape closes");menuKeys=Array.Empty<Key>();break;
     case 13: menuKeys=new[]{Key.Escape};break;
     case 14: MenuRequire(checkedMenu.IsOpen,"Menu reopened for mouse");menuKeys=Array.Empty<Key>();MenuMouseAt("Save",false);break;
     case 15: MenuMouseAt("Save",true);break;
     case 16: MenuMouseAt("Save",false);break;
     case 17: MenuRequire(MenuSaved(),"Mouse click saves and displays green result");MenuMouseAt("Continue",false);break;
     case 18: MenuMouseAt("Continue",true);break;
     case 19: MenuMouseAt("Continue",false);break;
     case 20: MenuRequire(!checkedMenu.IsOpen&&Time.timeScale>0,"Mouse click resumes");FinishMenuInput(null);return;
    }
    menuPhase++;menuNext=EditorApplication.timeSinceStartup+.35;
   }catch(Exception e){FinishMenuInput(e.ToString());}
  }
  static bool MenuSaved(){var indicator=checkedMenu.transform.Find("Journey Pause Icons/Save result").GetComponent<UnityEngine.UI.Image>();return indicator.gameObject.activeInHierarchy&&indicator.color.g>indicator.color.r;}
  static void MenuMouseAt(string button,bool pressed){var rect=checkedMenu.transform.Find("Journey Pause Icons/"+button);menuMouseState=new MouseState{position=RectTransformUtility.WorldToScreenPoint(Camera.main,rect.position),buttons=(ushort)(pressed?1:0)};}
  static void CaptureMenuCanvas(){
   Canvas.ForceUpdateCanvases();var camera=Camera.main;var target=new RenderTexture(1920,1080,24);var texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);var prior=RenderTexture.active;float aspect=camera.aspect;
   try{target.Create();camera.aspect=1920f/1080;RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1920,1080),0,0);texture.Apply();File.WriteAllBytes("../Art/World/PineRest/menu_native.png",texture.EncodeToPNG());}
   finally{camera.aspect=aspect;RenderTexture.active=prior;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(texture);}
  }
  static void FinishMenuInput(string error){
   EditorApplication.update-=MenuInputTick;InputSystem.onBeforeUpdate-=MenuInputFeed;
   if(menuKeyboard!=null){InputSystem.RemoveDevice(menuKeyboard);menuKeyboard=null;}if(priorMenuKeyboard!=null&&priorMenuKeyboard.added)priorMenuKeyboard.MakeCurrent();
   if(menuMouse!=null){InputSystem.RemoveDevice(menuMouse);menuMouse=null;}if(priorMenuMouse!=null&&priorMenuMouse.added)priorMenuMouse.MakeCurrent();
   if(checkedMenu!=null)checkedMenu.SetOpen(false);
   if(error!=null)menuEvidence.Add("FAIL phase="+menuPhase+" "+error);
   menuEvidence.Add("Virtual keyboard and mouse input only; actual exit and standalone shown window are separate checks.");File.WriteAllLines(MenuInputReport,menuEvidence);
  }
 }
}
