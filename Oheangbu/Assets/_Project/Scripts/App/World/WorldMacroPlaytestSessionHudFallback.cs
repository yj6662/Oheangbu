using UnityEngine;

namespace Oheangbu.App.World
{
    // #307 phase 1 item 12 (Tools/Unity/Plan307/PERF_DESIGN.md C.1): the session's former OnGUI prompt, unchanged (text, font,
    // style, rect). WorldMacroPlaytestSession adds it in Play and keeps it enabled only while no canvas HUD presenter is registered,
    // so a scene with the canvas HUD no longer runs an IMGUI pass every frame. Presentation only; holds no gameplay state.
    [DisallowMultipleComponent]
    public sealed class WorldMacroPlaytestSessionHudFallback:MonoBehaviour
    {
        [System.NonSerialized] public WorldMacroPlaytestSession Session;
        Font font;GUIStyle style;
        void OnGUI()
        {
            var session=Session;
            if(session==null||!session.isActiveAndEnabled||session.CanvasHudPresenterActive)return;
            string text=session.CurrentHudText;
            if(string.IsNullOrEmpty(text))return;
            if(style==null){font=Font.CreateDynamicFontFromOSFont("Malgun Gothic",20);style=new GUIStyle(GUI.skin.box){font=font,fontSize=20,wordWrap=true,alignment=TextAnchor.MiddleCenter};}
            GUI.Box(new Rect(Screen.width*.2f,Screen.height-140,Screen.width*.6f,95),text,style);
        }
        void OnDestroy(){if(font!=null)Destroy(font);}
    }
}
