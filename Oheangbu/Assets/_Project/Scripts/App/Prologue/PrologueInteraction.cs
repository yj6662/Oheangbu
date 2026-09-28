using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Oheangbu.App.Prologue
{
    public sealed class PrologueInteraction : MonoBehaviour
    {
        public PrologueSession Session;
        public bool HideText;
        public Texture2D InteractionIcon;
        public string FocusedId => focused;
        public PrologueSession.InteractionResult LastResult {get;private set;}
        public int ResultCount {get;private set;}
        Vector3 focusPosition;
        string resultTarget;
        float resultUntil;
        InputAction interact;PlayerMotor motor;string focused,prompt,message;float until;GUIStyle style;Font font;
        void Awake(){interact=new InputAction("WorldInteract",InputActionType.Button,"<Keyboard>/f");motor=Session.Player.GetComponent<PlayerMotor>();}
        void OnEnable(){interact?.Enable();if(Session!=null){Session.Feedback+=Show;Session.InteractionResolved+=ShowResult;}}
        void OnDisable(){interact?.Disable();if(Session!=null){Session.Feedback-=Show;Session.InteractionResolved-=ShowResult;}}
        void ShowResult(PrologueSession.InteractionResult result){LastResult=result;ResultCount++;resultTarget=focused;resultUntil=Time.unscaledTime+1;}
        void OnDestroy(){interact?.Dispose();if(font!=null)Destroy(font);}
        void Show(string text){message=text;until=Time.unscaledTime+6;}
        void Update()
        {
            if(Session.Progress==null)return;if(Time.timeScale==0||Session.JourneySeated){focused=null;prompt=null;return;}focused=null;prompt=null;float best=float.MaxValue;
            foreach(var p in Session.Content.Points){if(HideText&&(p.Kind==Oheangbu.Data.World.PrologueInteractionKind.Currency||p.Kind==Oheangbu.Data.World.PrologueInteractionKind.Evidence)&&Session.Progress.completed.Contains(p.Id))continue;if(p.Id=="wangso_w1"&&Session.Progress.completed.Contains("escort_start"))continue;float distance=Vector3.Distance(Session.Player.position,p.Position+Vector3.up);if(distance<=p.Radius && distance<best){focused=p.Id;prompt=p.Prompt;focusPosition=p.Position+Vector3.up*2.15f;best=distance;}}
            if(Session.Progress.dropCurrency>0 && Vector3.Distance(Session.Player.position,Session.Progress.dropPosition)<2){focused="CurrencyDrop";prompt="남긴 통보 회수";focusPosition=Session.Progress.dropPosition;}
            if(motor!=null&&motor.IsDrawing){focused=null;prompt=null;return;}
            if(interact.WasPressedThisFrame()&&focused!=null)Session.Interact(focused);
        }
        void OnGUI()
        {
            if(Session==null||Time.timeScale==0)return;
            if(HideText){DrawIcon();return;}
            string text=Time.unscaledTime<until?message:(!string.IsNullOrEmpty(prompt)?"[F] "+prompt:"");if(string.IsNullOrEmpty(text))return;
            if(style==null){font=Font.CreateDynamicFontFromOSFont("Malgun Gothic",20);style=new GUIStyle(GUI.skin.box){font=font,fontSize=20,alignment=TextAnchor.MiddleCenter,wordWrap=true};style.normal.textColor=new Color(.93f,.90f,.82f);}
            GUI.Box(new Rect(Screen.width*.22f,Screen.height-130,Screen.width*.56f,85),text,style);
        }
        void DrawIcon()
        {
            if(InteractionIcon==null||focused==null||motor!=null&&motor.IsDrawing)return;
            var camera=Camera.main;if(camera==null)return;
            var position=camera.WorldToScreenPoint(focusPosition);if(position.z<=0||position.x<0||position.x>Screen.width||position.y<0||position.y>Screen.height)return;
            float pulse=resultTarget==focused?Mathf.Clamp01(resultUntil-Time.unscaledTime):0;
            float size=44+Mathf.Sin(pulse*Mathf.PI)*10;
            var previous=GUI.color;
            GUI.color=new Color(.08f,.09f,.08f,.85f);GUI.DrawTexture(new Rect(position.x-size*.5f+1,Screen.height-position.y-size*.5f+1,size,size),InteractionIcon,ScaleMode.ScaleToFit,true);
            GUI.color=pulse<=0?new Color(.92f,.9f,.82f,.85f):LastResult==PrologueSession.InteractionResult.Success?new Color(.75f,.9f,.76f):LastResult==PrologueSession.InteractionResult.Waiting?new Color(.95f,.72f,.37f):new Color(.95f,.36f,.26f);
            GUI.DrawTexture(new Rect(position.x-size*.5f,Screen.height-position.y-size*.5f,size,size),InteractionIcon,ScaleMode.ScaleToFit,true);GUI.color=previous;
        }
    }
}
