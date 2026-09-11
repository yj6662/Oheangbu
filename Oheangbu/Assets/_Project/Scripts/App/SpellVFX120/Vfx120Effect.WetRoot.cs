using UnityEngine;
namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        MeshRenderer _wetThorn,_wetMark;
        LineRenderer[] _wetLines;
        MaterialPropertyBlock _wetBlock;
        Vector3[][] _wetPaths;
        Transform _wetHost;
        Vector3 _wetNormal;
        float _wetAt=-1;
        public GameObject WetRootInstance{get;private set;}
        public bool WetRootConfigured=>WetRootInstance!=null;
        public float WetRootAt=>_wetAt;
        public int WetRootVisiblePaths{get;private set;}
        public static bool IsWetRoot(Vfx120Profile p)=>p!=null&&p.Glyph=="강"&&p.BodyMesh!=null&&p.BodyMesh.name=="VFX120_WetThorn_006";
        // Three confirmed surface paths, copied to host-local coordinates once.
        public bool SignalWetRootContact(Transform host,Vector3[][] paths,Vector3 normal,float at=-1)
        {
            float when=at<0?Age:at;
            if(!WetRootConfigured||_wetAt>=0||paths==null||paths.Length!=3||!Vfx120InterceptionMotion.Finite(normal)||normal.sqrMagnitude<.001f||float.IsNaN(when)||float.IsInfinity(when)||when<0||when>=Life)return false;
            foreach(var path in paths){if(path==null||path.Length<2||path.Length>24)return false;foreach(var point in path)if(!Vfx120InterceptionMotion.Finite(point))return false;}
            _wetHost=host;_wetPaths=new Vector3[3][];
            for(int i=0;i<3;i++){_wetPaths[i]=new Vector3[paths[i].Length];for(int j=0;j<paths[i].Length;j++)_wetPaths[i][j]=host!=null?host.InverseTransformPoint(paths[i][j]):paths[i][j];}
            _wetNormal=host!=null?host.InverseTransformDirection(normal.normalized):normal.normalized;_wetAt=when;return true;
        }
        Vector3 WetPoint(int path,float u)
        {
            var points=_wetPaths[path];float f=Mathf.Clamp01(u)*(points.Length-1);int k=Mathf.Min(points.Length-2,(int)f);var p=Vector3.Lerp(points[k],points[k+1],f-k);return _wetHost!=null?_wetHost.TransformPoint(p):p;
        }
        void BuildWetRoot()
        {
            if(!IsWetRoot(Profile))return;
            WetRootInstance=new GameObject("WetRoot_006");WetRootInstance.transform.SetParent(transform,false);_wetBlock=new MaterialPropertyBlock();
            _wetThorn=WardRenderer("WetThorn",WetRootInstance.transform,Profile.BodyMesh,Profile.BodyMaterial);
            _wetMark=WardRenderer("KTP_SeepingInk",WetRootInstance.transform,QuadMesh.Value,Profile.PatternMaterial);
            _wetLines=new LineRenderer[3];for(int i=0;i<3;i++)
            {
                var go=new GameObject("SurfaceRoot_"+i);go.transform.SetParent(WetRootInstance.transform,false);var line=go.AddComponent<LineRenderer>();line.sharedMaterial=Profile.InkMaterial;line.useWorldSpace=true;line.positionCount=16;line.widthCurve=AnimationCurve.Linear(0,.012f,1,.002f);line.numCapVertices=2;line.numCornerVertices=1;line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;_wetLines[i]=line;
            }
        }
        void SampleWetRoot()
        {
            if(!WetRootConfigured)return;bool hit=_wetAt>=0&&Age>=_wetAt;float after=hit?Age-_wetAt:0;
            Vector3 direction=(TargetPoint()-ReceivedOrigin).normalized;if(direction.sqrMagnitude<.001f)direction=Vector3.forward;
            Vector3 normal=hit?(_wetHost!=null?_wetHost.TransformDirection(_wetNormal):_wetNormal):-direction;
            Quaternion facing=Quaternion.LookRotation(-normal,Mathf.Abs(normal.y)>.98f?Vector3.forward:Vector3.up);
            Vector3 contact=hit?WetPoint(0,0):TargetPoint();float end=1-Mathf.Clamp01((Age-Life+.25f)/.25f);
            _wetThorn.transform.SetPositionAndRotation(hit?contact-normal*Mathf.Min(.18f,after*.4f):Vector3.Lerp(ReceivedOrigin,contact,Mathf.Clamp01(Age/Mathf.Max(.01f,_flight))),facing);
            float body=hit?1-Mathf.Clamp01(after/.3f):1;_wetBlock.SetFloat("_Visibility",body*end);_wetThorn.SetPropertyBlock(_wetBlock);_wetThorn.enabled=Age<Life&&body>.001f;
            WetRootVisiblePaths=0;float head=Mathf.Clamp01(after/.3f),tail=Mathf.Clamp01((after-.3f)/.5f);
            for(int i=0;i<3;i++)
            {
                var line=_wetLines[i];line.enabled=hit&&Age<Life&&after<.8f&&head>tail;if(!line.enabled)continue;
                WetRootVisiblePaths++;for(int j=0;j<16;j++)line.SetPosition(j,WetPoint(i,Mathf.Lerp(tail,head,j/15f))+normal*.004f);
                Tint(line,Profile.Ink,end*(1-tail*.5f),tail*.45f);
            }
            _wetMark.transform.SetPositionAndRotation(contact+normal*.005f,Quaternion.LookRotation(normal,Mathf.Abs(normal.y)>.98f?Vector3.forward:Vector3.up));_wetMark.transform.localScale=Vector3.one*.08f;
            float mark=hit?end*Mathf.Clamp01(after/.1f)*(1-Mathf.Clamp01((after-.3f)/.55f)):0;Tint(_wetMark,Profile.Ink,mark,1-mark);_wetMark.enabled=Age<Life&&mark>.001f;
        }
        void ClearWetRoot()
        {
            if(WetRootInstance!=null)DisposeNativeHost(WetRootInstance);
            WetRootInstance=null;_wetThorn=_wetMark=null;_wetLines=null;_wetPaths=null;_wetHost=null;_wetBlock=null;_wetAt=-1;WetRootVisiblePaths=0;
        }
    }
    public static class Vfx120WetRootReviewFixture
    {
        public static Vector3[][] Paths()
        {
            var result=new Vector3[3][];for(int i=0;i<3;i++){result[i]=new Vector3[12];for(int j=0;j<12;j++){float u=j/11f;result[i][j]=new Vector3((i-1)*.29f*u+.025f*Mathf.Sin(u*7),1.1f+(i==1?-.35f:.12f)*u,4+.05f*u*u);}}return result;
        }
        public static void Sample(Vfx120Effect e,float age)
        {if(e!=null&&e.PreviewControlled&&e.DemonstrationCues&&e.WetRootConfigured&&age>=.6f&&e.WetRootAt<0)e.SignalWetRootContact(null,Paths(),Vector3.back,.6f);}
    }
}
