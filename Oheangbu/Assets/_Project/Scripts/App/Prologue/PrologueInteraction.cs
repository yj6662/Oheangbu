using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Oheangbu.App.Prologue
{
    public sealed class PrologueInteraction : MonoBehaviour
    {
        public PrologueSession Session;
        InputAction interact;PlayerMotor motor;string focused,prompt,message;float until;GUIStyle style;Font font;
        void Awake(){interact=new InputAction("WorldInteract",InputActionType.Button,"<Keyboard>/f");motor=Session.Player.GetComponent<PlayerMotor>();}
        void OnEnable(){interact?.Enable();if(Session!=null)Session.Feedback+=Show;}
        void OnDisable(){interact?.Disable();if(Session!=null)Session.Feedback-=Show;}
        void OnDestroy(){interact?.Dispose();if(font!=null)Destroy(font);}
        void Show(string text){message=text;until=Time.unscaledTime+6;}
        void Update()
        {
            if(Session.Progress==null)return;focused=null;prompt=null;float best=float.MaxValue;
            foreach(var p in Session.Content.Points){float distance=Vector3.Distance(Session.Player.position,p.Position+Vector3.up);if(distance<=p.Radius && distance<best){focused=p.Id;prompt=p.Prompt;best=distance;}}
            if(Session.Progress.dropCurrency>0 && Vector3.Distance(Session.Player.position,Session.Progress.dropPosition)<2){focused="CurrencyDrop";prompt="남긴 통보 회수";}
            if(motor!=null&&motor.IsDrawing){focused=null;prompt=null;return;}
            if(interact.WasPressedThisFrame()&&focused!=null)Session.Interact(focused);
        }
        void OnGUI()
        {
            if(Session==null)return;
            string text=Time.unscaledTime<until?message:(!string.IsNullOrEmpty(prompt)?"[F] "+prompt:"");if(string.IsNullOrEmpty(text))return;
            if(style==null){font=Font.CreateDynamicFontFromOSFont("Malgun Gothic",20);style=new GUIStyle(GUI.skin.box){font=font,fontSize=20,alignment=TextAnchor.MiddleCenter,wordWrap=true};style.normal.textColor=new Color(.93f,.90f,.82f);}
            GUI.Box(new Rect(Screen.width*.22f,Screen.height-130,Screen.width*.56f,85),text,style);
        }
    }
}
