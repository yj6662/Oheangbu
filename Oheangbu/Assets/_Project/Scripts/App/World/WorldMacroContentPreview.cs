using System;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Oheangbu.App.World
{
    // Layout review interactions. Deliberately separate from the real campaign save and combat.
    public sealed class WorldMacroContentPreview:MonoBehaviour
    {
        public WorldMacroContentSheetSO Sheet;
        public WorldMacroReviewController Walker;
        public WorldMacroContentPoint[] Points;
        public bool ShowPlacementHints=true;
        public string LastFeedback{get;private set;}
        public string FocusedId{get;private set;}
        public int VisitedCount=>visited.Count;
        readonly HashSet<string> visited=new HashSet<string>();
        readonly Dictionary<string,WorldMacroContentSheetSO.Entry> lookup=new Dictionary<string,WorldMacroContentSheetSO.Entry>();
        float next,until;GUIStyle style;Font font;
        void Awake(){if(Sheet!=null)foreach(var e in Sheet.Entries)lookup[e.Id]=e;}
        public bool Inspect(string id)
        {
            if(!lookup.TryGetValue(id,out var e))return false;
            if(!string.IsNullOrEmpty(e.Requires)&&!visited.Contains(e.Requires))
                LastFeedback="아직 살펴보지 않은 단서가 있다. 이 지점은 배치 검토용이다.";
            else {visited.Add(id);LastFeedback=e.Label+"\n"+e.Text;}
            until=Time.unscaledTime+8;return true;
        }
        void Update()
        {
            if(Walker==null||Sheet==null)return;
            bool walking=Walker.isActiveAndEnabled&&Walker.Mode==1&&Walker.WalkBody!=null;
            if(Time.unscaledTime>=next)
            {
                next=Time.unscaledTime+.2f;FocusedId=null;float closest=4;
                Vector3 eye=walking?Walker.WalkBody.transform.position:Walker.transform.position;
                foreach(var p in Points)
                {
                    if(p==null||!lookup.TryGetValue(p.Id,out var e))continue;
                    float d=Vector3.Distance(p.transform.position,eye);
                    if(p.Visual!=null){bool visible=d<e.ActivationDistance;if(p.Visual.gameObject.activeSelf!=visible)p.Visual.gameObject.SetActive(visible);}
                    if(!walking||d>e.Radius||d>=closest)continue;
                    // Wall-side conversations and discoveries require unobstructed physical access.
                    Vector3 a=eye+Vector3.up*1.25f,b=p.transform.position+Vector3.up*1.25f;
                    bool blocked=false;
                    foreach(var h in Physics.RaycastAll(a,(b-a).normalized,Vector3.Distance(a,b),~0,QueryTriggerInteraction.Ignore))
                        if(!h.transform.IsChildOf(p.transform)&&h.collider!=Walker.WalkBody){blocked=true;break;}
                    if(!blocked){closest=d;FocusedId=e.Id;}
                }
            }
            if(walking&&Keyboard.current!=null&&Keyboard.current.fKey.wasPressedThisFrame&&FocusedId!=null)Inspect(FocusedId);
        }
        void OnDisable(){FocusedId=null;until=0;foreach(var p in Points)if(p!=null&&p.Visual!=null)p.Visual.gameObject.SetActive(true);}
        void OnDestroy(){if(font!=null)Destroy(font);}
        void OnGUI()
        {
            if(!ShowPlacementHints||Walker==null||!Walker.isActiveAndEnabled||Walker.Mode!=1)return;
            string text=Time.unscaledTime<until?LastFeedback:FocusedId!=null?"[F] "+lookup[FocusedId].Label:null;
            if(string.IsNullOrEmpty(text))return;
            if(style==null){font=Font.CreateDynamicFontFromOSFont("Malgun Gothic",19);style=new GUIStyle(GUI.skin.box){font=font,fontSize=19,wordWrap=true,alignment=TextAnchor.MiddleCenter};style.normal.textColor=new Color(.9f,.87f,.78f);}
            GUI.Box(new Rect(Screen.width*.2f,Screen.height-140,Screen.width*.6f,100),text,style);
        }
    }
}
