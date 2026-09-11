using UnityEngine;
using Oheangbu.Spellcraft;
namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        MeshRenderer[] _liftRoots,_liftMarks;
        Vector3[] _liftBases;
        MaterialPropertyBlock _liftBlock;
        float _liftDelay;
        Vector3 _liftCenter;
        public GameObject RootLiftInstance{get;private set;}
        public bool RootLiftConfigured=>RootLiftInstance!=null;
        public float RootLiftDelay=>_liftDelay;
        public float RootLiftRise{get;private set;}
        public static bool IsRootLift(Vfx120Profile p)=>p!=null&&p.Glyph=="곤"&&p.BodyMesh!=null&&p.BodyMesh.name=="VFX120_RootPalm_015";
        void BuildRootLift()
        {
            if(!IsRootLift(Profile))return;var plan=ReceivedAreaPlan;
            if(plan==null||plan.Shape!=AreaShape.Circle||!WashFinite(plan.Point)||!WashFinite(plan.Radius)||plan.Radius<=0||!WashFinite(plan.Delay)||plan.Delay<0)return;
            _liftCenter=plan.Point;_liftDelay=plan.Delay;
            RootLiftInstance=new GameObject("RootLift_015");RootLiftInstance.transform.SetParent(transform,false);_liftBlock=new MaterialPropertyBlock();_liftRoots=new MeshRenderer[3];_liftMarks=new MeshRenderer[3];_liftBases=new Vector3[3];
            for(int i=0;i<3;i++)
            {
                float a=(i*120+20)*Mathf.Deg2Rad;Vector3 pos=_liftCenter+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*plan.Radius*.48f;pos.y=GroundHeight(pos,pos.y);_liftBases[i]=pos;
                _liftRoots[i]=WardRenderer("ForkRoot_"+i,RootLiftInstance.transform,Profile.BodyMesh,Profile.BodyMaterial);_liftRoots[i].transform.localScale=Vector3.one*(1.05f+i*.06f);
                var mark=WardRenderer("KTP_RootWake_"+i,RootLiftInstance.transform,Profile.AccentMesh,Profile.PatternMaterial);mark.transform.SetPositionAndRotation(pos+Vector3.up*.013f,Quaternion.Euler(90,i*120,0));mark.transform.localScale=Vector3.one*.8f;_liftMarks[i]=mark;
            }
        }
        void SampleRootLift()
        {
            if(!RootLiftConfigured)return;float start=Mathf.Max(0,_liftDelay-.17f);float rise=_liftDelay<=0?1:Mathf.SmoothStep(0,1,Mathf.InverseLerp(start,_liftDelay,Age));
            float fold=Mathf.SmoothStep(0,1,Mathf.InverseLerp(_liftDelay+.12f,_liftDelay+.58f,Age));RootLiftRise=Age<Life?rise*(1-fold):0;
            for(int i=0;i<3;i++)
            {
                var r=_liftRoots[i];Vector3 inward=(_liftCenter-_liftBases[i]);inward.y=0;inward.Normalize();Vector3 direction=(Vector3.up+inward*.4f).normalized;
                Quaternion rest=Quaternion.LookRotation(direction,inward.sqrMagnitude>.001f?-inward:Vector3.forward);
                r.transform.SetPositionAndRotation(_liftBases[i]-Vector3.up*(1-rise)*1.2f-Vector3.up*fold*.8f,rest*Quaternion.Euler(-fold*72,0,0));
                _liftBlock.SetFloat("_GroundY",_liftBases[i].y-.006f);_liftBlock.SetFloat("_Visibility",1-fold);r.SetPropertyBlock(_liftBlock);r.enabled=Age<Life&&rise>0&&fold<1;
                float ink=Mathf.Clamp01(Age/.09f)*(1-fold)*.63f;Tint(_liftMarks[i],Profile.Ink,ink,fold);_liftMarks[i].enabled=Age<Life&&ink>.001f;
            }
            if(_nativeCast!=null)_nativeCast.transform.position=_liftBases[0]+Vector3.up*.02f;
        }
        void ClearRootLift()
        {if(RootLiftInstance!=null)DisposeNativeHost(RootLiftInstance);RootLiftInstance=null;_liftRoots=_liftMarks=null;_liftBases=null;_liftBlock=null;RootLiftRise=0;_liftDelay=0;}
    }
}
