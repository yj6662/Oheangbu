using UnityEngine;
using Oheangbu.Spellcraft;
namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        MeshRenderer[] _stakes;MeshRenderer _stakeMark;
        Vector3[] _stakeBases;Quaternion[] _stakeRotations;float[] _stakeTicks;
        MaterialPropertyBlock _stakeBlock;Vector3 _stakeCenter;float _stakeDelay,_stakeRadius;
        public GameObject StakeFieldInstance{get;private set;}
        public bool StakeFieldConfigured=>StakeFieldInstance!=null;
        public float StakeFieldDelay=>_stakeDelay;
        public int StakeContactCount{get;private set;}
        public int StakeActiveFeedback{get;private set;}
        public static bool IsStakeField(Vfx120Profile p)=>p!=null&&p.Glyph=="곳"&&p.BodyMesh!=null&&p.BodyMesh.name=="VFX120_HardStake_017";
        public static Vector2[] StakeFeet(float radius)
        {
            var points=new Vector2[19];int i=0;for(int q=-2;q<=2;q++)for(int r=Mathf.Max(-2,-q-2);r<=Mathf.Min(2,-q+2);r++)points[i++]=new Vector2(q+r*.5f,r*.8660254f)*radius*.42f;return points;
        }
        public bool SignalStakeContact(Vector3 confirmedPoint,float at=-1)
        {
            float when=at<0?Age:at;
            if(!StakeFieldConfigured||!Vfx120InterceptionMotion.Finite(confirmedPoint)||float.IsNaN(when)||float.IsInfinity(when)||when<_stakeDelay||when>=Life-.55f)return false;
            var delta=confirmedPoint-_stakeCenter;delta.y=0;if(delta.magnitude>_stakeRadius)return false;
            int first=-1,second=-1;float a=float.MaxValue,b=float.MaxValue;
            for(int i=0;i<19;i++){var d=_stakeBases[i]-confirmedPoint;d.y=0;float dist=d.sqrMagnitude;if(dist<a){second=first;b=a;first=i;a=dist;}else if(dist<b){second=i;b=dist;}}
            if(first<0||when-_stakeTicks[first]<.06f)return false;
            _stakeTicks[first]=when;if(second>=0)_stakeTicks[second]=when;StakeContactCount++;return true;
        }
        void BuildStakeField()
        {
            if(!IsStakeField(Profile))return;var plan=ReceivedAreaPlan;
            if(plan==null||plan.Shape!=AreaShape.Circle||!WashFinite(plan.Point)||!WashFinite(plan.Radius)||plan.Radius<=0||!WashFinite(plan.Delay)||plan.Delay<0)return;
            _stakeCenter=plan.Point;_stakeRadius=plan.Radius;_stakeDelay=plan.Delay;StakeFieldInstance=new GameObject("StakeField_017");StakeFieldInstance.transform.SetParent(transform,false);
            _stakes=new MeshRenderer[19];_stakeBases=new Vector3[19];_stakeRotations=new Quaternion[19];_stakeTicks=new float[19];_stakeBlock=new MaterialPropertyBlock();var feet=StakeFeet(_stakeRadius);
            for(int i=0;i<19;i++)
            {
                var p=_stakeCenter+new Vector3(feet[i].x,0,feet[i].y);p.y=GroundHeight(p,p.y);_stakeBases[i]=p;_stakeRotations[i]=Quaternion.Euler(0,i*97,0);_stakeTicks[i]=-100;
                var r=WardRenderer("HardStake_"+i,StakeFieldInstance.transform,Profile.BodyMesh,Profile.BodyMaterial);r.transform.localScale=new Vector3(1,.6f+(i%5)*.1f,1);_stakes[i]=r;
            }
            _stakeMark=WardRenderer("KTP_ResidualRootMark",StakeFieldInstance.transform,Profile.AccentMesh,Profile.PatternMaterial);var c=_stakeCenter;c.y=GroundHeight(c,c.y)+.013f;_stakeMark.transform.SetPositionAndRotation(c,Quaternion.Euler(90,25,0));_stakeMark.transform.localScale=new Vector3(.7f,.8f,1);
        }
        void SampleStakeField()
        {
            if(!StakeFieldConfigured)return;float rise=_stakeDelay<=0?1:Mathf.SmoothStep(0,1,Mathf.InverseLerp(Mathf.Max(0,_stakeDelay-.15f),_stakeDelay,Age));float collapse=Mathf.SmoothStep(0,1,Mathf.InverseLerp(Life-.55f,Life-.12f,Age));StakeActiveFeedback=0;
            for(int i=0;i<19;i++)
            {
                var r=_stakes[i];float since=Age-_stakeTicks[i];bool feedback=since>=0&&since<.24f;if(feedback)StakeActiveFeedback++;
                float twitch=feedback?Mathf.Sin(since*70)*Mathf.Exp(-since*18)*2.5f:0;
                r.transform.SetPositionAndRotation(_stakeBases[i]-Vector3.up*((1-rise)*1.1f+collapse*.055f),_stakeRotations[i]*Quaternion.Euler(twitch+collapse*83,0,0));
                _stakeBlock.SetFloat("_GroundY",_stakeBases[i].y-.006f);_stakeBlock.SetFloat("_Visibility",1-Mathf.Clamp01((Age-Life+.2f)/.2f));r.SetPropertyBlock(_stakeBlock);r.enabled=Age<Life&&rise>0;
            }
            float ink=Mathf.Clamp01(Age/.1f)*(1-Mathf.Clamp01((Age-Life+.15f)/.15f))*.7f;Tint(_stakeMark,Profile.Ink,ink,1-ink);_stakeMark.enabled=Age<Life&&ink>.001f;
            if(_nativeCast!=null)_nativeCast.transform.position=_stakeMark.transform.position;
        }
        void ClearStakeField()
        {if(StakeFieldInstance!=null)DisposeNativeHost(StakeFieldInstance);StakeFieldInstance=null;_stakes=null;_stakeMark=null;_stakeBases=null;_stakeRotations=null;_stakeTicks=null;_stakeBlock=null;StakeContactCount=StakeActiveFeedback=0;}
    }
    public static class Vfx120StakeFieldReviewFixture
    {
        public static Vector3 Contact(int index)=>new Vector3(index==0?-.4f:.55f,0,index==0?3.7f:4.3f);
        public static void Sample(Vfx120Effect e,float age)
        {
            if(e==null||!e.PreviewControlled||!e.DemonstrationCues||!e.StakeFieldConfigured)return;
            if(age>=1.4f&&e.StakeContactCount==0)e.SignalStakeContact(Contact(0),1.4f);
            if(age>=2.5f&&e.StakeContactCount==1)e.SignalStakeContact(Contact(1),2.5f);
        }
    }
}
