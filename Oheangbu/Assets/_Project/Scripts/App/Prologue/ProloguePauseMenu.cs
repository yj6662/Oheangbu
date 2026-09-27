using System;
using Oheangbu.App.World.UI;
using Oheangbu.Core;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Oheangbu.App.Prologue
{
    // Scene-owned menu; existing pause/input-neutral policy remains the sole input owner.
    [DisallowMultipleComponent]
    public sealed class ProloguePauseMenu : MonoBehaviour
    {
        public PrologueSession Session;
        public bool Textless;
        public Texture2D[] MenuIcons;
        Texture2D[] icons;
        JourneyIconMenuView iconView;
        bool? saved;
        public PauseCoordinator Pause;
        GameplayRuntimeStateSO state;
        InputAction toggle;
        Font font;
        GUIStyle label,button;
        string message="";
        public bool IsOpen => Pause!=null && Pause.IsPaused;
        void Awake()
        {
            state=ScriptableObject.CreateInstance<GameplayRuntimeStateSO>();
            var gate=gameObject.AddComponent<GameplayUiGate>();gate.State=state;gate.Initialize();
            Pause=gameObject.AddComponent<PauseCoordinator>();Pause.Gate=gate;
            Pause.Drawing=Session.Player.GetComponentInChildren<DrawingInputController>(true);
            Session.Player.GetComponent<PlayerMotor>().RuntimeState=state;
            Pause.Initialize();
            toggle=new InputAction("JourneyPause",InputActionType.Button,"<Keyboard>/escape");toggle.Enable();
        }
        void Update(){if(toggle.WasPressedThisFrame())SetOpen(!IsOpen);if(iconView!=null)iconView.Refresh(IsOpen,saved);}
        public void SetOpen(bool open){if(open==IsOpen)return;if(open){message="";saved=null;Pause.Begin();}else Pause.End();if(Textless){if(iconView==null){iconView=gameObject.AddComponent<JourneyIconMenuView>();iconView.Initialize(this);}iconView.Refresh(open,saved);}}
        public bool SaveProgress()
        {
            try{if(Session.Progress==null){message="아직 여정을 시작하는 중입니다.";return false;}Session.Save();saved=true;message="여정을 기록했습니다.";return true;}
            catch(Exception e){saved=false;Debug.LogWarning("[Journey] Save failed: "+e.Message);message="기록하지 못했습니다. 저장 위치를 확인하고 다시 시도해 주세요.";return false;}
        }
        void OnGUI()
        {
            if(!IsOpen)return;
            if(Textless)return;
            if(label==null){font=Font.CreateDynamicFontFromOSFont("Malgun Gothic",22);label=new GUIStyle(GUI.skin.label){font=font,fontSize=20,wordWrap=true};button=new GUIStyle(GUI.skin.button){font=font,fontSize=21};}
            var width=Mathf.Min(540,Screen.width-32);GUILayout.BeginArea(new Rect((Screen.width-width)/2,Screen.height*.2f,width,Screen.height*.7f),GUI.skin.box);
            GUILayout.Label("잠시 붓을 내려놓는다",label);GUILayout.Space(22);
            if(GUILayout.Button("여정 계속",button,GUILayout.Height(46)))SetOpen(false);
            if(GUILayout.Button("여정 기록",button,GUILayout.Height(46)))SaveProgress();
            if(GUILayout.Button("기록하고 종료",button,GUILayout.Height(46))&&SaveProgress()){
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying=false;
#else
                Application.Quit();
#endif
            }
            GUILayout.Space(20);GUILayout.Label(@"WASD 이동 · 마우스 시선
Q 작도 · 마우스로 획 긋기
F 살피기 / 대화 / 휴식 · Esc 계속",label);
            if(!string.IsNullOrEmpty(message)){GUILayout.Space(12);GUILayout.Label(message,label);}GUILayout.EndArea();
        }
        internal Texture2D IconAt(int index)
        {
            if(MenuIcons!=null&&MenuIcons.Length>index&&MenuIcons[index]!=null)return MenuIcons[index];
            if(icons==null){icons=new Texture2D[3];for(int i=0;i<3;i++){var t=new Texture2D(48,48,TextureFormat.RGBA32,false);t.filterMode=FilterMode.Point;for(int y=0;y<48;y++)for(int x=0;x<48;x++){
                bool ink=i==0?x>=12&&x<=36&&Mathf.Abs(y-24)<(36-x)*.7f:i==1?x>8&&x<40&&y>8&&y<40&&!(x>16&&x<32&&y>24&&y<38)&&!(x>15&&x<33&&y>10&&y<20):((x>=10&&x<=13&&y>8&&y<40)||(y>=8&&y<=11&&x>10&&x<29)||(y>=37&&y<=40&&x>10&&x<29)||(y>22&&y<26&&x>22&&x<41)||(x>31&&x<41&&Mathf.Abs(y-24)<42-x));
                t.SetPixel(x,y,ink?new Color(.88f,.87f,.81f):Color.clear);
            }t.Apply();icons[i]=t;}}
            return icons[index];
        }
        public void SaveAndExit(){if(!SaveProgress())return;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit();
#endif
        }
        void OnDestroy(){if(icons!=null)foreach(var icon in icons)if(icon!=null)Destroy(icon);toggle?.Dispose();if(Pause!=null)Pause.ForceResume();if(state!=null)Destroy(state);if(font!=null)Destroy(font);}
    }
}
