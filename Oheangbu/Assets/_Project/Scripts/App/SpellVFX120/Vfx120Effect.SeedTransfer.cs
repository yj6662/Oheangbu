using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        MeshRenderer _transferThorn,_transferMark;
        MeshRenderer[] _transferShells;
        MaterialPropertyBlock _transferBlock;
        Transform _transferTarget;
        Vector3 _transferFrom,_transferFallback,_transferContact;
        float _transferAt=-1,_transferHit=-1;
        public GameObject SeedTransferInstance {get;private set;}
        public bool SeedTransferConfigured=>SeedTransferInstance!=null;
        public Vector3 SeedTransferPosition {get;private set;}
        public float SeedTransferStartedAt=>_transferAt;
        public float SeedTransferHitAt=>_transferHit;
        public static bool IsSeedTransfer(Vfx120Profile p)=>p!=null&&p.Glyph=="간"&&p.BodyMesh!=null&&p.BodyMesh.name=="VFX120_SeedThorn_003";
        // A gameplay adapter selects the recipient and confirms its hit. Presentation never searches or damages actors.
        public bool RequestSeedTransfer(Transform recipient,Vector3 fallback,float at=-1)
        {
            float when=at<0?Age:at;
            if(!SeedTransferConfigured||_transferAt>=0||!Vfx120InterceptionMotion.Finite(fallback)||float.IsNaN(when)||float.IsInfinity(when)||when<_flight||when+.55f>=Life)return false;
            _transferTarget=recipient;_transferFallback=fallback;_transferFrom=TargetPoint();_transferAt=when;return true;
        }
        public bool ConfirmSeedTransferHit(Vector3 point,float at=-1)
        {
            float when=at<0?Age:at;
            if(_transferAt<0||_transferHit>=0||!Vfx120InterceptionMotion.Finite(point)||float.IsNaN(when)||float.IsInfinity(when)||when<_transferAt+.025f||when>=Life)return false;
            _transferHit=when;_transferContact=point;return true;
        }
        void BuildSeedTransfer()
        {
            if(!IsSeedTransfer(Profile))return;
            SeedTransferInstance=new GameObject("SeedTransfer_003");SeedTransferInstance.transform.SetParent(transform,false);
            _transferThorn=WardRenderer("EmbeddedThorn",SeedTransferInstance.transform,Profile.BodyMesh,Profile.BodyMaterial);
            _transferShells=new MeshRenderer[2];for(int i=0;i<2;i++)_transferShells[i]=WardRenderer("SeedHalf_"+i,SeedTransferInstance.transform,Profile.AccentMesh,Profile.BodyMaterial);
            _transferMark=WardRenderer("KTP_LeafBud",SeedTransferInstance.transform,QuadMesh.Value,Profile.PatternMaterial);
            _transferBlock=new MaterialPropertyBlock();
        }
        void SampleSeedTransfer()
        {
            if(!SeedTransferConfigured)return;
            Vector3 primary=TargetPoint(),direction=(primary-ReceivedOrigin).normalized;
            if(direction.sqrMagnitude<.001f)direction=Vector3.forward;
            Quaternion facing=Quaternion.LookRotation(direction,Mathf.Abs(direction.y)>.98f?Vector3.forward:Vector3.up);
            Vector3 arrival=Vector3.Lerp(ReceivedOrigin,primary,Mathf.Clamp01(Age/Mathf.Max(.01f,_flight)));
            float fade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Life-.35f,Life,Age));
            float departure=_transferAt>=0&&Age>=_transferAt?Mathf.Clamp01((Age-_transferAt)/.55f):0;
            _transferThorn.transform.SetPositionAndRotation(arrival,facing);
            _transferBlock.SetFloat("_Visibility",fade*(1-departure));_transferBlock.SetFloat("_GroundY",-10000);
            _transferThorn.SetPropertyBlock(_transferBlock);_transferThorn.enabled=Age<Life&&departure<1;
            SeedTransferPosition=arrival-facing*Vector3.forward*.17f+Vector3.down*(.025f+Mathf.Sin(Age*5)*.012f);
            bool moving=_transferAt>=0&&Age>=_transferAt,hit=_transferHit>=0&&Age>=_transferHit;
            Vector3 recipient=_transferTarget!=null?_transferTarget.position+Vector3.up*1.1f:_transferFallback;
            if(moving)
            {
                SeedTransferPosition=Vector3.Lerp(_transferFrom,recipient,departure)+Vector3.up*(4*departure*(1-departure)*.55f);
                Vector3 tangent=recipient-_transferFrom+Vector3.up*((1-2*departure)*2.2f);
                if(tangent.sqrMagnitude>.001f)facing=Quaternion.LookRotation(tangent.normalized,Vector3.up);
            }
            if(hit)SeedTransferPosition=_transferContact;
            float opening=hit?Mathf.SmoothStep(0,1,Mathf.InverseLerp(_transferHit,_transferHit+.18f,Age)):0;
            for(int i=0;i<2;i++)
            {
                var shell=_transferShells[i];float side=i==0?1:-1;
                shell.transform.SetPositionAndRotation(SeedTransferPosition+facing*Vector3.right*(side*opening*.04f),facing*Quaternion.Euler(0,side*opening*42,i*180));
                _transferBlock.SetFloat("_Visibility",fade);shell.SetPropertyBlock(_transferBlock);shell.enabled=Age<Life;
            }
            _transferMark.transform.SetPositionAndRotation(SeedTransferPosition-facing*Vector3.forward*.008f,facing*Quaternion.Euler(0,0,22));
            _transferMark.transform.localScale=new Vector3(.14f,.23f,1);
            float mark=Age>=_flight?fade*(hit?opening:(!moving?.45f:0)):0;
            Tint(_transferMark,Profile.Pigment,mark,1-mark);_transferMark.enabled=Age<Life&&mark>.001f;
        }
        void ClearSeedTransfer()
        {
            if(SeedTransferInstance!=null)DisposeNativeHost(SeedTransferInstance);
            SeedTransferInstance=null;_transferShells=null;_transferThorn=_transferMark=null;_transferTarget=null;_transferBlock=null;_transferAt=_transferHit=-1;SeedTransferPosition=Vector3.zero;
        }
    }
}
