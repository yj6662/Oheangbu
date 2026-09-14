using UnityEngine;
using Oheangbu.Presentation;

namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlayerGestureRig
    {
        bool _pendantActive;
        float _pendantProgress;
        Transform _pendantProp;
        bool[] _pendantBrushVisibility;
        Quaternion _pendantLeftFrame, _pendantRightFrame;
        public bool PendantActive => _pendantActive;
        public bool CanPresentPendant => IsPresentationReady && _world.LeftUpper!=null && _world.LeftForearm!=null && _world.LeftHand!=null;
        public Vector3 PendantLeftWrist => _world!=null&&_world.LeftHand!=null?_world.LeftHand.position:Vector3.zero;
        public Vector3 PendantRightWrist => _world!=null&&_world.Hand!=null?_world.Hand.position:Vector3.zero;

        public bool BeginPendant(Transform prop)
        {
            if(!CanPresentPendant||prop==null||_pendantActive)return false;
            RestoreAnimatedPose();
            _pendantActive=true;_pendantProgress=0;_pendantProp=prop;
            prop.gameObject.SetActive(false);
            _pendantLeftFrame=HandFrame(_world.LeftHand,"Left");
            _pendantRightFrame=HandFrame(_world.Hand,"Right");
            _pendantBrushVisibility=new bool[_worldBrush.Renderers.Length];
            for(int i=0;i<_pendantBrushVisibility.Length;i++)
            {var r=_worldBrush.Renderers[i];_pendantBrushVisibility[i]=r!=null&&r.enabled;if(r!=null)r.enabled=false;}
            SetNearVisible(false);
            return true;
        }
        public void SetPendantProgress(float progress){_pendantProgress=Mathf.Clamp01(progress);}
        public void EndPendant()
        {
            if(!_pendantActive)return;
            RestoreAnimatedPose();
            _pendantActive=false;
            if(_pendantProp!=null)_pendantProp.gameObject.SetActive(false);
            if(_pendantBrushVisibility!=null)
                for(int i=0;i<_pendantBrushVisibility.Length;i++)
                    if(_worldBrush.Renderers[i]!=null)_worldBrush.Renderers[i].enabled=_pendantBrushVisibility[i];
            _pendantProp=null;_pendantBrushVisibility=null;
        }
        Quaternion HandFrame(Transform hand,string side)
        {
            var middle=Find(_world.Root,side+"HandMiddle1");
            var index=Find(_world.Root,side+"HandIndex1");
            var little=Find(_world.Root,side+"HandPinky1");
            if(middle==null||index==null||little==null)return Quaternion.identity;
            Vector3 fingers=(middle.position-hand.position).normalized;
            Vector3 palm=Vector3.Cross(fingers,index.position-little.position).normalized;
            if(side=="Right")palm=-palm;
            return Quaternion.Inverse(hand.rotation)*Quaternion.LookRotation(fingers,palm);
        }
        void ApplyPendantPose()
        {
            float t=_pendantProgress;
            float weight=Mathf.SmoothStep(0,1,Mathf.Clamp01(t/.16f))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.86f,1,t)));
            float lift=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.18f,.43f,t))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.68f,.9f,t)));
            var basis=_world.Root;
            // Chest-relative placement follows this actor's existing skeleton and scale, without root motion.
            Vector3 origin=_world.Chest!=null?_world.Chest.position:(_world.Upper.position+_world.LeftUpper.position)*.5f;
            Vector3 stowed=origin+basis.TransformDirection(new Vector3(-.19f,-.40f,.10f));
            Vector3 raised=origin+basis.TransformDirection(new Vector3(.30f,.02f,.32f));
            Vector3 center=Vector3.Lerp(stowed,raised,lift);
            Vector3 normal=(-basis.forward*.85f+basis.up*.53f).normalized;
            Quaternion cardRotation=Quaternion.LookRotation(normal,basis.up);
            Vector3 leftTarget=center+cardRotation*new Vector3(-.105f,-.055f,-.018f);
            Quaternion leftRotation=Quaternion.LookRotation(cardRotation*Vector3.up,normal)*Quaternion.Inverse(_pendantLeftFrame);
            PlayerVisualIK.Solve(_world.LeftUpper,_world.LeftForearm,_world.LeftHand,leftTarget,
                origin+basis.TransformDirection(new Vector3(-.46f,-.32f,.08f)),leftRotation,weight,.98f);
            if(_pendantProp!=null)
            {
                // The plaque is rigidly attached to the evaluated hand, including reach limiting.
                center=_world.LeftHand.position-cardRotation*new Vector3(-.105f,-.055f,-.018f);
                _pendantProp.SetPositionAndRotation(center,cardRotation);
                _pendantProp.gameObject.SetActive(t>=.19f&&t<.89f);
            }
            float reach=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.30f,.47f,t))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.63f,.82f,t)));
            float tap=Mathf.Sin(Mathf.Clamp01(Mathf.InverseLerp(.47f,.59f,t))*Mathf.PI);
            Vector3 rightRest=origin+basis.TransformDirection(new Vector3(.25f,-.39f,.12f));
            Vector3 rightTap=center+cardRotation*new Vector3(.045f,.014f,.16f-.035f*tap);
            Quaternion rightRotation=Quaternion.LookRotation(-normal,basis.up)*Quaternion.Inverse(_pendantRightFrame);
            PlayerVisualIK.Solve(_world.Upper,_world.Forearm,_world.Hand,Vector3.Lerp(rightRest,rightTap,reach),
                origin+basis.TransformDirection(new Vector3(.45f,-.27f,.10f)),rightRotation,weight,.98f);
            foreach(var finger in _fingers)
            {
                var bone=finger.World;if(bone==null)continue;
                bool left=finger.Pose.BoneName.StartsWith("Left");
                bool index=finger.Pose.BoneName.Contains("Index");
                bool thumb=finger.Pose.BoneName.Contains("Thumb");
                bone.localRotation=Quaternion.Slerp(bone.localRotation,finger.Rest,weight);
                if(bone.childCount==0)continue;
                Vector3 direction=bone.GetChild(0).position-bone.position;
                Vector3 bendToward=left?normal: -normal;
                Vector3 axis=Vector3.Cross(direction,bendToward);
                float angle=thumb?18:(left?38:(index?3:48));
                if(axis.sqrMagnitude>.000001f)bone.rotation=Quaternion.AngleAxis(angle*weight,axis.normalized)*bone.rotation;
            }
            SetNearVisible(false);
            _cameraRig?.SetDrawingPresentationActive(false);
            _diagnostics.State=GestureState.Suspended;
        }
    }
}
