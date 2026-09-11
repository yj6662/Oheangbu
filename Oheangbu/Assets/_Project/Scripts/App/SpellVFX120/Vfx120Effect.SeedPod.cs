using UnityEngine;
namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        MeshRenderer _podBody,_podMark;
        MeshRenderer[] _podKnots,_podLeaves;
        MaterialPropertyBlock _podBlock;
        Transform _podHost;
        Vector3 _podLocalPoint,_podLocalNormal;
        float _podAttached=-1,_podDetonated=-1;
        public GameObject SeedPodInstance{get;private set;}
        public bool SeedPodConfigured=>SeedPodInstance!=null;
        public float SeedPodAttachedAt=>_podAttached;
        public float SeedPodDetonatedAt=>_podDetonated;
        public Vector3 SeedPodPosition{get;private set;}
        public static bool IsSeedPod(Vfx120Profile p)=>p!=null&&p.Glyph=="감"&&p.BodyMesh!=null&&p.BodyMesh.name=="VFX120_SeedPod_004";
        public bool AttachSeedPod(Transform host,Vector3 point,Vector3 normal,float at=-1)
        {
            float when=at<0?Age:at;
            if(!SeedPodConfigured||_podAttached>=0||!Vfx120InterceptionMotion.Finite(point)||!Vfx120InterceptionMotion.Finite(normal)||normal.sqrMagnitude<.001f||float.IsNaN(when)||float.IsInfinity(when)||when<0||when>=Life)return false;
            _podHost=host;_podLocalPoint=host!=null?host.InverseTransformPoint(point):point;_podLocalNormal=host!=null?host.InverseTransformDirection(normal.normalized):normal.normalized;_podAttached=when;return true;
        }
        public bool DetonateSeedPod(float at=-1)
        {
            float when=at<0?Age:at;
            if(_podAttached<0||_podDetonated>=0||float.IsNaN(when)||float.IsInfinity(when)||when<_podAttached||when>=Life)return false;
            _podDetonated=when;return true;
        }
        Vector3 PodPoint()=>_podHost!=null?_podHost.TransformPoint(_podLocalPoint):_podLocalPoint;
        void BuildSeedPod()
        {
            if(!IsSeedPod(Profile))return;
            SeedPodInstance=new GameObject("SeedPod_004");SeedPodInstance.transform.SetParent(transform,false);_podBlock=new MaterialPropertyBlock();
            _podBody=WardRenderer("FlattenedSeedPod",SeedPodInstance.transform,Profile.BodyMesh,Profile.BodyMaterial);
            _podKnots=new MeshRenderer[4];_podLeaves=new MeshRenderer[4];
            for(int i=0;i<4;i++)
            {
                _podKnots[i]=WardRenderer("BindingVine_"+i,SeedPodInstance.transform,Profile.AccentMesh,Profile.BodyMaterial);
                _podLeaves[i]=WardRenderer("ReleasedLeafStroke_"+i,SeedPodInstance.transform,Profile.GuardianMeshes[0],Profile.InkMaterial);
            }
            _podMark=WardRenderer("KTP_Seal",SeedPodInstance.transform,QuadMesh.Value,Profile.PatternMaterial);
        }
        void SampleSeedPod()
        {
            if(!SeedPodConfigured)return;
            bool attached=_podAttached>=0&&Age>=_podAttached,detonated=_podDetonated>=0&&Age>=_podDetonated;
            Vector3 direction=(TargetPoint()-ReceivedOrigin).normalized;if(direction.sqrMagnitude<.001f)direction=Vector3.forward;
            Vector3 normal=attached?(_podHost!=null?_podHost.TransformDirection(_podLocalNormal):_podLocalNormal):-direction;
            Quaternion facing=Quaternion.LookRotation(normal,Mathf.Abs(normal.y)>.98f?Vector3.forward:Vector3.up);
            SeedPodPosition=attached?PodPoint()+normal*.009f:Vector3.Lerp(ReceivedOrigin,TargetPoint(),Mathf.Clamp01(Age/Mathf.Max(.01f,_flight)));
            float end=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Life-.3f,Life,Age));float after=detonated?Age-_podDetonated:0;
            float vanish=detonated?1-Mathf.Clamp01(after/.14f):1;
            _podBody.transform.SetPositionAndRotation(SeedPodPosition,facing);_podBlock.SetFloat("_Visibility",end*vanish);_podBlock.SetFloat("_GroundY",-10000);_podBody.SetPropertyBlock(_podBlock);_podBody.enabled=Age<Life&&vanish>.001f;
            for(int i=0;i<4;i++)
            {
                Quaternion rotation=facing*Quaternion.Euler(0,0,i*90+18);Vector3 outwards=rotation*Vector3.up;
                var knot=_podKnots[i];knot.transform.SetPositionAndRotation(SeedPodPosition+outwards*(detonated?after*.42f:0)+Vector3.down*(after*after*.2f),rotation*Quaternion.Euler(detonated?after*80:0,0,0));
                knot.transform.localScale=Vector3.one*(attached&&!detonated?1-.035f*Mathf.Clamp01((Age-_podAttached)/.7f):1);
                float release=detonated?1-Mathf.Clamp01(after/.55f):1;_podBlock.SetFloat("_Visibility",release*end);knot.SetPropertyBlock(_podBlock);knot.enabled=Age<Life&&release>.001f;
                var leaf=_podLeaves[i];leaf.transform.SetPositionAndRotation(SeedPodPosition+outwards*(.06f+after*.8f)+normal*.025f,rotation*Quaternion.Euler(90,0,0));leaf.transform.localScale=new Vector3(.7f,1,1.8f);
                _podBlock.SetFloat("_Visibility",detonated?end*(1-Mathf.Clamp01(after/.3f)):0);leaf.SetPropertyBlock(_podBlock);leaf.enabled=detonated&&after<.3f&&Age<Life;
            }
            _podMark.transform.SetPositionAndRotation(SeedPodPosition+normal*.045f,facing);_podMark.transform.localScale=new Vector3(.19f,.23f,1);
            float mark=(attached?1:.3f)*end*vanish;Tint(_podMark,Profile.Ink,mark,1-vanish);_podMark.enabled=Age<Life&&mark>.001f;
        }
        void ClearSeedPod()
        {
            if(SeedPodInstance!=null)DisposeNativeHost(SeedPodInstance);
            SeedPodInstance=null;_podBody=_podMark=null;_podKnots=_podLeaves=null;_podHost=null;_podBlock=null;_podAttached=_podDetonated=-1;
        }
    }
    public static class Vfx120SeedPodReviewFixture
    {
        public static readonly Vector3 Contact=new Vector3(0,1.1f,4);
        public static void Sample(Vfx120Effect e,float age)
        {
            if(e==null||!e.PreviewControlled||!e.DemonstrationCues||!e.SeedPodConfigured)return;
            if(age>=.55f&&e.SeedPodAttachedAt<0)e.AttachSeedPod(null,Contact,Vector3.back,.55f);
            if(age>=1.65f&&e.SeedPodDetonatedAt<0)e.DetonateSeedPod(1.65f);
        }
    }
}
