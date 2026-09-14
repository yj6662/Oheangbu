using System.Collections.Generic;
using Oheangbu.Drawing;
using PDollarGestureRecognizer;
using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    /// <summary>Replays one approved recognizer sample without changing recognition data or rules.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CodexStrokeExample : MaskableGraphic
    {
        struct Segment { public Vector2 A,B; public Segment(Vector2 a,Vector2 b){A=a;B=b;} }
        readonly List<Segment> segments=new List<Segment>();
        Text fallback;
        Font ownedFont;
        string letter="";
        float replayStart;
        const float DrawSeconds=2.25f;
        const float HoldSeconds=.8f;

        public bool HasTemplate=>segments.Count>0;
        public string Letter=>letter;

        protected override void Awake(){base.Awake();raycastTarget=false;}

        public void Initialize(JamoTemplateLibrarySO library,string selectedLetter,Color inkColor)
        {
            letter=selectedLetter??"";color=inkColor;segments.Clear();
            bool built=library!=null&&TryBuild(library,letter);
            EnsureFallback();fallback.text=letter;fallback.color=inkColor;fallback.gameObject.SetActive(!built);
            replayStart=Time.unscaledTime;SetVerticesDirty();
        }

        bool TryBuild(JamoTemplateLibrarySO library,string value)
        {
            if(value.Length!=1)return false;
            int syllable=value[0]-0xAC00;if(syllable<0||syllable>=11172)return false;
            int initialIndex=syllable/(21*28),medialIndex=(syllable/28)%21,finalIndex=syllable%28;
            string initial=InitialName(initialIndex),medial=MedialName(medialIndex),final=FinalName(finalIndex);
            if(initial==null||medial==null||(finalIndex!=0&&final==null))return false;
            Gesture first=Find(library.BuildInitialGestures(),initial);
            Gesture middle=Find(library.BuildMedialGestures(),medial);
            Gesture last=finalIndex==0?null:Find(library.BuildFinalGestures(),final);
            if(first==null||middle==null||(finalIndex!=0&&last==null))return false;

            bool horizontal=medial=="ㅗ"||medial=="ㅜ",hasFinal=last!=null;
            if(horizontal)
            {
                Append(first,hasFinal?new Rect(.14f,.58f,.72f,.37f):new Rect(.12f,.48f,.76f,.47f));
                Append(middle,hasFinal?new Rect(.10f,.34f,.80f,.20f):new Rect(.10f,.10f,.80f,.30f));
            }
            else
            {
                float y=hasFinal?.34f:.10f,height=hasFinal?.61f:.80f;
                Append(first,new Rect(.05f,y,.43f,height));Append(middle,new Rect(.52f,y,.43f,height));
            }
            if(last!=null)Append(last,new Rect(.18f,.04f,.64f,.24f));
            return segments.Count>0;
        }

        static Gesture Find(Gesture[] source,string name)
        {
            if(source==null)return null;
            foreach(var gesture in source)if(gesture!=null&&gesture.Name==name&&gesture.Points!=null&&gesture.Points.Length>1)return gesture;
            return null;
        }

        void Append(Gesture gesture,Rect slot)
        {
            var points=gesture.Points;float minX=float.MaxValue,minY=float.MaxValue,maxX=float.MinValue,maxY=float.MinValue;
            foreach(var p in points){if(p==null)continue;minX=Mathf.Min(minX,p.X);minY=Mathf.Min(minY,p.Y);maxX=Mathf.Max(maxX,p.X);maxY=Mathf.Max(maxY,p.Y);}
            float width=Mathf.Max(.001f,maxX-minX),height=Mathf.Max(.001f,maxY-minY);
            float scale=Mathf.Min(slot.width/width,slot.height/height)*.88f;
            Vector2 center=slot.center,sourceCenter=new Vector2((minX+maxX)*.5f,(minY+maxY)*.5f);
            for(int i=1;i<points.Length;i++)
            {
                var a=points[i-1];var b=points[i];if(a==null||b==null||a.StrokeID!=b.StrokeID)continue;
                Vector2 pa=center+new Vector2(a.X-sourceCenter.x,sourceCenter.y-a.Y)*scale;
                Vector2 pb=center+new Vector2(b.X-sourceCenter.x,sourceCenter.y-b.Y)*scale;
                if((pb-pa).sqrMagnitude>.000001f)segments.Add(new Segment(pa,pb));
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(segments.Count==0)return;
            Rect rect=rectTransform.rect;float unit=Mathf.Min(rect.width,rect.height);
            Color guide=color;guide.a*=.13f;
            foreach(var segment in segments)Line(vh,Local(segment.A,rect),Local(segment.B,rect),Mathf.Max(1.2f,unit*.012f),guide);

            float phase=Mathf.Repeat(Time.unscaledTime-replayStart,DrawSeconds+HoldSeconds);
            float exact=Mathf.Clamp01(phase/DrawSeconds)*segments.Count;int whole=Mathf.FloorToInt(exact);
            float width=Mathf.Clamp(unit*.038f,3.5f,6f);Vector2 tip=default;bool hasTip=false;
            for(int i=0;i<segments.Count&&i<=whole;i++)
            {
                if(i==whole&&whole<segments.Count&&exact-whole<=0f)break;
                Vector2 a=Local(segments[i].A,rect),b=Local(segments[i].B,rect);
                if(i==whole&&whole<segments.Count)b=Vector2.Lerp(a,b,exact-whole);
                Line(vh,a,b,width,color);tip=b;hasTip=true;
            }
            if(hasTip&&phase<DrawSeconds)Disc(vh,tip,width*.62f,color);
        }

        static Vector2 Local(Vector2 normalized,Rect rect)
        {return new Vector2(Mathf.Lerp(rect.xMin,rect.xMax,normalized.x),Mathf.Lerp(rect.yMin,rect.yMax,normalized.y));}

        static void Line(VertexHelper vh,Vector2 a,Vector2 b,float width,Color tint)
        {
            Vector2 direction=b-a;if(direction.sqrMagnitude<.000001f)return;
            Vector2 normal=new Vector2(-direction.y,direction.x).normalized*(width*.5f);
            UIVertex v=UIVertex.simpleVert;v.color=tint;
            int start=vh.currentVertCount;
            v.position=a-normal;vh.AddVert(v);v.position=a+normal;vh.AddVert(v);
            v.position=b+normal;vh.AddVert(v);v.position=b-normal;vh.AddVert(v);
            vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
        }

        static void Disc(VertexHelper vh,Vector2 center,float radius,Color tint)
        {
            int start=vh.currentVertCount;UIVertex v=UIVertex.simpleVert;v.color=tint;v.position=center;vh.AddVert(v);
            const int sides=10;
            for(int i=0;i<=sides;i++){float angle=i*Mathf.PI*2/sides;v.position=center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;vh.AddVert(v);}
            for(int i=0;i<sides;i++)vh.AddTriangle(start,start+i+1,start+i+2);
        }

        void EnsureFallback()
        {
            if(fallback!=null)return;
            var go=new GameObject("FallbackGlyph",typeof(RectTransform),typeof(Text));go.transform.SetParent(transform,false);
            var rect=(RectTransform)go.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            fallback=go.GetComponent<Text>();fallback.alignment=TextAnchor.MiddleCenter;fallback.raycastTarget=false;
            fallback.resizeTextForBestFit=true;fallback.resizeTextMinSize=24;fallback.resizeTextMaxSize=96;
            var root=PlaytestUiRoot.Instance;fallback.font=root!=null&&root.Theme!=null?root.Theme.Font:null;
            if(fallback.font==null){ownedFont=Font.CreateDynamicFontFromOSFont(new[]{"Noto Sans CJK KR","Malgun Gothic","Arial"},64);fallback.font=ownedFont;}
        }

        void Update(){if(segments.Count>0)SetVerticesDirty();}
        protected override void OnDestroy(){if(ownedFont!=null)Destroy(ownedFont);base.OnDestroy();}

        static string InitialName(int index){switch(index){case 0:return "ㄱ";case 2:return "ㄴ";case 6:return "ㅁ";case 9:return "ㅅ";case 11:return "ㅇ";default:return null;}}
        static string MedialName(int index){switch(index){case 0:return "ㅏ";case 4:return "ㅓ";case 8:return "ㅗ";case 13:return "ㅜ";default:return null;}}
        static string FinalName(int index){switch(index){case 0:return "";case 1:return "ㄱ";case 4:return "ㄴ";case 16:return "ㅁ";case 19:return "ㅅ";case 21:return "ㅇ";default:return null;}}
    }
}
