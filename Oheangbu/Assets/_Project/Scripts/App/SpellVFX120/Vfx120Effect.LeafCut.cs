using UnityEngine;
namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        MeshRenderer[] _cutLeaves;
        MeshRenderer _cutArc;
        MaterialPropertyBlock _cutBlock;
        Vector3 _cutPoint,_cutNormal;
        float _leafCutAt=-1;
        public GameObject LeafCutInstance{get;private set;}
        public bool LeafCutConfigured=>LeafCutInstance!=null;
        public bool LeafCutExecution{get;private set;}
        public float LeafCutAt=>_leafCutAt;
        public float LeafCutEmphasis=>LeafCutExecution?1.85f:1;
        public static bool IsLeafCut(Vfx120Profile p)=>p!=null&&p.Glyph=="갓"&&p.BodyMesh!=null&&p.BodyMesh.name=="VFX120_LeafCutHalf_005";
        // The caller has already classified this hit. No health threshold is evaluated here.
        public bool SignalLeafCut(Vector3 point,Vector3 normal,bool executionConfirmed,float at=-1)
        {
            float when=at<0?Age:at;
            if(!LeafCutConfigured||_leafCutAt>=0||!Vfx120InterceptionMotion.Finite(point)||!Vfx120InterceptionMotion.Finite(normal)||normal.sqrMagnitude<.001f||float.IsNaN(when)||float.IsInfinity(when)||when<0||when>=Life)return false;
            _cutPoint=point;_cutNormal=normal.normalized;_leafCutAt=when;LeafCutExecution=executionConfirmed;return true;
        }
        void BuildLeafCut()
        {
            if(!IsLeafCut(Profile))return;
            LeafCutInstance=new GameObject("LeafCut_005");LeafCutInstance.transform.SetParent(transform,false);_cutBlock=new MaterialPropertyBlock();
            _cutLeaves=new MeshRenderer[2];for(int i=0;i<2;i++)_cutLeaves[i]=WardRenderer("LeafBladeHalf_"+i,LeafCutInstance.transform,Profile.BodyMesh,Profile.BodyMaterial);
            _cutArc=WardRenderer("KTP_BrokenCutArc",LeafCutInstance.transform,Profile.AccentMesh,Profile.PatternMaterial);
        }
        void SampleLeafCut()
        {
            if(!LeafCutConfigured)return;
            bool hit=_leafCutAt>=0&&Age>=_leafCutAt;float after=hit?Age-_leafCutAt:0;
            Vector3 direction=(TargetPoint()-ReceivedOrigin).normalized;if(direction.sqrMagnitude<.001f)direction=Vector3.forward;
            Vector3 normal=hit?_cutNormal:-direction;
            var facing=Quaternion.LookRotation(normal,Mathf.Abs(normal.y)>.98f?Vector3.forward:Vector3.up);
            Vector3 center=hit?_cutPoint:Vector3.Lerp(ReceivedOrigin,TargetPoint(),Mathf.Clamp01(Age/Mathf.Max(.01f,_flight)));
            float stroke=hit?Mathf.Clamp01(after/.14f):0;
            Quaternion blade=facing*Quaternion.Euler(0,0,Mathf.Lerp(-35,32,stroke));
            center+=blade*Vector3.right*(hit?Mathf.Lerp(-.30f,.26f,stroke):-.15f);
            float split=hit?Mathf.Clamp01((after-.10f)/.25f):0;
            float visibility=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Life-.25f,Life,Age)))*(hit?1-Mathf.Clamp01((after-.2f)/.3f):1);
            for(int i=0;i<2;i++)
            {
                float side=i==0?1:-1;var r=_cutLeaves[i];r.transform.SetPositionAndRotation(center+blade*Vector3.up*(side*split*.15f),blade*Quaternion.Euler(0,0,side*split*18));r.transform.localScale=new Vector3(1,side,1);
                _cutBlock.SetFloat("_Visibility",visibility);_cutBlock.SetFloat("_GroundY",-10000);r.SetPropertyBlock(_cutBlock);r.enabled=visibility>.001f&&Age<Life;
            }
            _cutArc.transform.SetPositionAndRotation(_cutPoint+normal*.012f,facing*Quaternion.Euler(0,0,8));_cutArc.transform.localScale=Vector3.one*LeafCutEmphasis;
            float ink=hit?(1-Mathf.Clamp01(after/(LeafCutExecution?.34f:.2f))):0;
            Tint(_cutArc,Profile.Ink,ink*(LeafCutExecution?.95f:.52f),1-ink);_cutArc.enabled=Age<Life&&ink>.001f;
        }
        void ClearLeafCut()
        {
            if(LeafCutInstance!=null)DisposeNativeHost(LeafCutInstance);
            LeafCutInstance=null;_cutLeaves=null;_cutArc=null;_cutBlock=null;_leafCutAt=-1;LeafCutExecution=false;
        }
    }
    public static class Vfx120LeafCutReviewFixture
    {
        public static readonly Vector3 Contact=new Vector3(0,1.1f,4);
        public static void Sample(Vfx120Effect e,float age)
        {
            if(e!=null&&e.PreviewControlled&&e.DemonstrationCues&&e.LeafCutConfigured&&age>=.6f&&e.LeafCutAt<0)e.SignalLeafCut(Contact,Vector3.back,true,.6f);
        }
    }
}
