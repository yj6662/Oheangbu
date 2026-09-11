using UnityEngine;
namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        MeshRenderer _woodLiftTrunk,_woodLiftDeck,_woodLiftMark;MeshRenderer[] _woodLiftLeaves;
        MaterialPropertyBlock _woodLiftBlock;Vector3 _woodLiftBase;float _woodLiftMax,_woodLiftHeight,_woodLiftRelease=-1;
        public GameObject WoodLiftInstance{get;private set;}
        public bool WoodLiftConfigured=>WoodLiftInstance!=null;
        public bool WoodLiftHasPlan{get;private set;}
        public float WoodLiftHeight=>_woodLiftHeight;
        public float WoodLiftReleaseAt=>_woodLiftRelease;
        public float WoodLiftGroundY=>ReceivedOrigin.y+_originGround;
        public static bool IsWoodLift(Vfx120Profile p)=>p!=null&&p.Glyph=="국"&&p.BodyMesh!=null&&p.BodyMesh.name=="VFX120_WoodLiftTrunk_020";
        public bool ConfigureWoodLift(Vector3 basePoint,float maximumHeight)
        {
            if(!WoodLiftConfigured||WoodLiftHasPlan||!Vfx120InterceptionMotion.Finite(basePoint)||!WashFinite(maximumHeight)||maximumHeight<=0||maximumHeight>2.4f)return false;
            _woodLiftBase=basePoint;_woodLiftMax=maximumHeight;WoodLiftHasPlan=true;return true;
        }
        public bool SetWoodLiftHeight(float currentHeight)
        {
            if(!WoodLiftHasPlan||!WashFinite(currentHeight)||currentHeight<0||currentHeight>_woodLiftMax||_woodLiftRelease>=0)return false;
            _woodLiftHeight=currentHeight;return true;
        }
        public bool ReleaseWoodLift(float at=-1)
        {float when=at<0?Age:at;if(!WoodLiftHasPlan||_woodLiftRelease>=0||!WashFinite(when)||when<0||when>=Life)return false;_woodLiftRelease=when;return true;}
        void BuildWoodLift()
        {
            if(!IsWoodLift(Profile))return;
            WoodLiftInstance=new GameObject("WoodLift_020");WoodLiftInstance.transform.SetParent(transform,false);_woodLiftBlock=new MaterialPropertyBlock();
            _woodLiftTrunk=WardRenderer("BraidedTrunk",WoodLiftInstance.transform,Profile.BodyMesh,Profile.BodyMaterial);
            _woodLiftDeck=WardRenderer("BranchDeck",WoodLiftInstance.transform,Profile.AccentMesh,Profile.BodyMaterial);
            _woodLiftLeaves=new MeshRenderer[3];for(int i=0;i<3;i++)_woodLiftLeaves[i]=WardRenderer("SettlingLeaf_"+i,WoodLiftInstance.transform,Profile.GuardianMeshes[0],Profile.InkMaterial);
            _woodLiftMark=WardRenderer("KTP_RootWake",WoodLiftInstance.transform,QuadMesh.Value,Profile.PatternMaterial);
        }
        void SampleWoodLift()
        {
            if(!WoodLiftConfigured)return;
            if(!WoodLiftHasPlan){_woodLiftTrunk.enabled=_woodLiftDeck.enabled=_woodLiftMark.enabled=false;foreach(var leaf in _woodLiftLeaves)leaf.enabled=false;return;}
            float release=_woodLiftRelease>=0?Mathf.Clamp01((Age-_woodLiftRelease)/.65f):0;
            float fade=(1-release)*(1-Mathf.Clamp01((Age-Life+.2f)/.2f));bool visible=Age<Life&&fade>.001f;
            Vector3 top=_woodLiftBase+Vector3.up*_woodLiftHeight;
            _woodLiftTrunk.transform.SetPositionAndRotation(top-Vector3.up*2.4f,Quaternion.identity);_woodLiftDeck.transform.SetPositionAndRotation(top,Quaternion.identity);
            _woodLiftBlock.SetFloat("_GroundY",_woodLiftBase.y-.006f);_woodLiftBlock.SetFloat("_Visibility",fade);_woodLiftTrunk.SetPropertyBlock(_woodLiftBlock);_woodLiftDeck.SetPropertyBlock(_woodLiftBlock);_woodLiftTrunk.enabled=_woodLiftDeck.enabled=visible;
            for(int i=0;i<3;i++)
            {
                var leaf=_woodLiftLeaves[i];float a=i*120;var q=Quaternion.Euler(0,a,0);leaf.transform.SetPositionAndRotation(top+q*Vector3.forward*.31f+Vector3.up*.045f,q*Quaternion.Euler(Mathf.Lerp(-65,0,Mathf.Clamp01(_woodLiftHeight/_woodLiftMax))-release*65,0,0));leaf.transform.localScale=new Vector3(1.3f,1,1.5f);leaf.SetPropertyBlock(_woodLiftBlock);leaf.enabled=visible;
            }
            _woodLiftMark.transform.SetPositionAndRotation(_woodLiftBase+Vector3.up*.013f,Quaternion.Euler(90,20,0));_woodLiftMark.transform.localScale=new Vector3(.65f,.8f,1);Tint(_woodLiftMark,Profile.Ink,fade*.7f,release);_woodLiftMark.enabled=visible;
            if(_nativeCast!=null)_nativeCast.transform.position=_woodLiftBase+Vector3.up*.02f;
        }
        void ClearWoodLift()
        {if(WoodLiftInstance!=null)DisposeNativeHost(WoodLiftInstance);WoodLiftInstance=null;_woodLiftTrunk=_woodLiftDeck=_woodLiftMark=null;_woodLiftLeaves=null;_woodLiftBlock=null;WoodLiftHasPlan=false;_woodLiftHeight=0;_woodLiftRelease=-1;}
    }
    public static class Vfx120WoodLiftReviewFixture
    {
        public static float Height(float age)=>2.1f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.35f,1.8f,age));
        public static void Sample(Vfx120Effect e,float age)
        {
            if(e==null||!e.PreviewControlled||!e.DemonstrationCues||!e.WoodLiftConfigured)return;
            if(!e.WoodLiftHasPlan)e.ConfigureWoodLift(new Vector3(0,e.WoodLiftGroundY,0),2.1f);
            e.SetWoodLiftHeight(Height(age));if(age>=3.5f&&e.WoodLiftReleaseAt<0)e.ReleaseWoodLift(3.5f);
        }
    }
}
