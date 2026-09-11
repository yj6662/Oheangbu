using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        private sealed class CompanionSeed
        {
            public Transform Root;
            public MeshRenderer[] Shells=new MeshRenderer[2];
            public MeshRenderer Mark;
            public float HitAt=-1;
            public Vector3 Contact,Normal;
            public MaterialPropertyBlock Block=new MaterialPropertyBlock();
        }
        private CompanionSeed[] _companionSeeds;
        private Transform _companionPrimary;
        private float _companionLaunch=-1,_companionArrival;
        private bool _companionHadPrimary;
        private Vector3 _companionLastPosition;
        private Quaternion _companionLastRotation;
        public GameObject CompanionSeedsInstance { get; private set; }
        public bool CompanionSeedsConfigured=>CompanionSeedsInstance!=null;
        public int CompanionHitMask { get; private set; }
        public int CompanionOpenMask { get; private set; }
        public static bool IsCompanionSeeds(Vfx120Profile p)=>p!=null&&p.Glyph=="겅"&&p.BodyMesh!=null&&p.BodyMesh.name=="VFX120_CompanionSeedShell_012";
        // Read-only primary projectile attachment. Supplied times are relative to this effect.
        public bool SetCompanionProjectile(Transform primary,float launchAt,float arrivalAt)
        {
            if(!CompanionSeedsConfigured||primary==null||float.IsNaN(launchAt)||float.IsNaN(arrivalAt)||float.IsInfinity(launchAt)||float.IsInfinity(arrivalAt)||launchAt<0||arrivalAt<=launchAt)return false;
            _companionPrimary=primary;_companionLaunch=launchAt;_companionArrival=arrivalAt;return true;
        }
        public bool ConfirmCompanionHit(int index,Vector3 point,Vector3 normal,float relativeTime=-1)
        {
            float at=relativeTime>=0?relativeTime:PreviewControlled?Age:Mathf.Max(Age,Time.time-_startedAt);
            if(!Begun||!CompanionSeedsConfigured||index<0||index>=2||_companionLaunch<0||at<_companionLaunch||at>=Life||Age>=Life||float.IsNaN(at)||float.IsInfinity(at)
                ||!Vfx120InterceptionMotion.Finite(point)||!Vfx120InterceptionMotion.Finite(normal)||normal.sqrMagnitude<.0001f||_companionSeeds[index].HitAt>=0)return false;
            var seed=_companionSeeds[index];seed.HitAt=at;seed.Contact=point;seed.Normal=normal.normalized;CompanionHitMask|=1<<index;if(Profile.UseOriginalKtp)ConfirmOriginalImpact(point);return true;
        }
        public Vector3 CompanionPosition(int index)=>CompanionSeedsConfigured&&index>=0&&index<2?_companionSeeds[index].Root.position:Vector3.zero;
        private void BuildCompanionSeeds()
        {
            if(!IsCompanionSeeds(Profile)||Profile.BodyMaterial==null||Profile.PatternMaterial==null)return;
            CompanionSeedsInstance=new GameObject("CompanionSeeds_012");CompanionSeedsInstance.transform.SetParent(transform,false);_companionSeeds=new CompanionSeed[2];
            for(int i=0;i<2;i++)
            {
                var root=new GameObject("Seed_"+i);root.transform.SetParent(CompanionSeedsInstance.transform,false);var s=new CompanionSeed{Root=root.transform};
                for(int j=0;j<2;j++)s.Shells[j]=WardRenderer("Shell_"+j,root.transform,Profile.BodyMesh,Profile.BodyMaterial);
                s.Mark=WardRenderer("KTP_LeafContact_"+i,CompanionSeedsInstance.transform,Profile.AccentMesh,Profile.PatternMaterial);s.Mark.transform.localScale=Vector3.one*.23f;s.Mark.enabled=false;_companionSeeds[i]=s;
            }
        }
        private void SampleCompanionSeeds()
        {
            if(!CompanionSeedsConfigured)return;
            if(_companionPrimary!=null){_companionLastPosition=_companionPrimary.position;_companionLastRotation=_companionPrimary.rotation;_companionHadPrimary=true;}
            bool launched=_companionLaunch>=0&&Age>=_companionLaunch;
            Vector3 primary=_companionHadPrimary?_companionLastPosition:transform.TransformPoint(new Vector3(.3f,.1f,.5f));
            Quaternion rotation=_companionHadPrimary?_companionLastRotation:transform.rotation;
            if(!launched)primary=transform.TransformPoint(new Vector3(.3f,.1f,.5f));
            float spread=launched?Mathf.SmoothStep(0,1,Mathf.InverseLerp(Mathf.Lerp(_companionLaunch,_companionArrival,.65f),_companionArrival,Age)):0;
            float birth=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.18f,Age));
            float end=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Mathf.Max(0,Life-.25f),Life,Age));CompanionOpenMask=0;
            for(int i=0;i<2;i++)
            {
                var s=_companionSeeds[i];float side=i==0?-1:1;
                bool hit=s.HitAt>=0&&Age>=s.HitAt;float elapsed=hit?Age-s.HitAt:0;
                float opened=hit?Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.18f,elapsed)):0;if(opened>.9f)CompanionOpenMask|=1<<i;
                s.Root.position=hit?s.Contact+s.Normal*.085f:primary+rotation*new Vector3(side*(.12f+spread*.12f),-.025f,-.06f);
                s.Root.rotation=hit?Quaternion.LookRotation(-s.Normal):rotation;
                float shellFade=(hit?1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.22f,.6f,elapsed)):1)*end*birth;
                for(int j=0;j<2;j++)
                {
                    float half=j==0?1:-1;var shell=s.Shells[j];shell.transform.localPosition=new Vector3(half*opened*.026f,-elapsed*elapsed*.09f,opened*.035f);
                    shell.transform.localRotation=Quaternion.Euler(0,half*opened*55,j==0?0:180);
                    s.Block.SetFloat("_Visibility",shellFade);s.Block.SetFloat("_GroundY",-10000);shell.SetPropertyBlock(s.Block);shell.enabled=Age<Life&&shellFade>.001f;
                }
                float markFade=hit&&!Profile.UseOriginalKtp?(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.16f,.55f,elapsed)))*end:0;
                if(hit){s.Mark.transform.position=s.Contact+s.Normal*.004f;s.Mark.transform.rotation=Quaternion.LookRotation(s.Normal);}
                Tint(s.Mark,Profile.Pigment,markFade*.7f,1-markFade);s.Mark.enabled=Age<Life&&markFade>.001f;
            }
            if(_nativeCast!=null)_nativeCast.transform.position=transform.TransformPoint(new Vector3(.3f,.1f,.5f));
        }
        private void ClearCompanionSeeds()
        {
            if(CompanionSeedsInstance!=null)DisposeNativeHost(CompanionSeedsInstance);
            CompanionSeedsInstance=null;_companionSeeds=null;_companionPrimary=null;_companionLaunch=-1;_companionHadPrimary=false;CompanionHitMask=CompanionOpenMask=0;
        }
    }
}
