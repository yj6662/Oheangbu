using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        const int SwordTraceCapacity=32;
        private Transform _woodSwordGrip;
        private bool _swordHadGrip,_swordSwing;
        private Vector3 _swordLastPosition,_swordContactPoint;
        private Quaternion _swordLastRotation;
        private float _swordContactAt=-1;
        private MeshRenderer _swordBody,_swordCollar;
        private LineRenderer[] _swordTraces;
        private MaterialPropertyBlock _swordBlock;
        private Vector3[] _swordTips,_swordMids;
        private float[] _swordTraceTimes;
        private int _swordTraceCount;
        public GameObject WoodSwordInstance { get; private set; }
        public bool WoodSwordConfigured=>WoodSwordInstance!=null;
        public int WoodSwordContactCount { get; private set; }
        public int WoodSwordTracePoints=>_swordTraceCount;
        public float WoodSwordContactClock=>_swordContactAt;
        public static bool IsWoodSword(Vfx120Profile p)=>p!=null&&p.Glyph=="것"&&p.BodyMesh!=null&&p.BodyMesh.name=="VFX120_WoodSword_011";
        // Grip uses +Y from handle to tip. The gameplay/animation owner supplies its pose.
        public void SetWoodSwordGrip(Transform grip){_woodSwordGrip=grip;}
        public void SetWoodSwordSwing(bool active){_swordSwing=active;}
        public bool SignalWoodSwordContact(Vector3 worldPoint,float relativeTime=-1)
        {
            float at=relativeTime>=0?relativeTime:PreviewControlled?Age:Mathf.Max(Age,Time.time-_startedAt);
            if(!Begun||!WoodSwordConfigured||!_swordSwing||Age>=Life||at>=Life||float.IsNaN(at)||float.IsInfinity(at)||!Vfx120InterceptionMotion.Finite(worldPoint))return false;
            _swordContactPoint=worldPoint;_swordContactAt=at;WoodSwordContactCount++;
            // One concurrent contact motif, even across multiple confirmed strikes.
            if(_nativeImpact!=null)DisposeNativeHost(_nativeImpact.gameObject);
            _nativeImpact=null;_nativeImpactAttempted=false;NativeImpactStartedAt=-1;
            return true;
        }
        private void BuildWoodSword()
        {
            if(!IsWoodSword(Profile)||Profile.BodyMaterial==null||Profile.PatternMaterial==null||Profile.InkMaterial==null)return;
            WoodSwordInstance=new GameObject("WoodSword_011");WoodSwordInstance.transform.SetParent(transform,false);
            _swordBody=WardRenderer("JointedBlade",WoodSwordInstance.transform,Profile.BodyMesh,Profile.BodyMaterial);
            _swordCollar=WardRenderer("KTP_Collar",WoodSwordInstance.transform,Profile.AccentMesh,Profile.PatternMaterial);
            _swordBlock=new MaterialPropertyBlock();_swordTips=new Vector3[SwordTraceCapacity];_swordMids=new Vector3[SwordTraceCapacity];_swordTraceTimes=new float[SwordTraceCapacity];
            _swordTraces=new LineRenderer[2];
            for(int i=0;i<2;i++)
            {
                var go=new GameObject("SplitInkTrace_"+i);go.transform.SetParent(WoodSwordInstance.transform,false);
                var line=go.AddComponent<LineRenderer>();line.sharedMaterial=Profile.InkMaterial;line.useWorldSpace=true;line.positionCount=0;
                line.widthMultiplier=i==0?.013f:.006f;line.numCapVertices=0;line.numCornerVertices=1;
                line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;
                line.widthCurve=AnimationCurve.Linear(0,0,1,1);_swordTraces[i]=line;
            }
        }
        private void AddSwordTrace(Vector3 position,Quaternion rotation,float time)
        {
            if(_swordTraceCount>0&&time<=_swordTraceTimes[_swordTraceCount-1])return;
            if(_swordTraceCount==SwordTraceCapacity)RemoveSwordTrace();
            _swordTips[_swordTraceCount]=position+rotation*new Vector3(.02f,1.03f,0);
            _swordMids[_swordTraceCount]=position+rotation*new Vector3(.04f,.77f,0);
            _swordTraceTimes[_swordTraceCount++]=time;
        }
        private void RemoveSwordTrace()
        {
            for(int i=1;i<_swordTraceCount;i++){_swordTips[i-1]=_swordTips[i];_swordMids[i-1]=_swordMids[i];_swordTraceTimes[i-1]=_swordTraceTimes[i];}
            _swordTraceCount=Mathf.Max(0,_swordTraceCount-1);
        }
        public void ClearWoodSwordPreviewTrace(){if(PreviewControlled&&DemonstrationCues)_swordTraceCount=0;}
        public void RecordWoodSwordPreviewPose(Vector3 position,Quaternion rotation,float time)
        {if(PreviewControlled&&DemonstrationCues&&WoodSwordConfigured)AddSwordTrace(position,rotation,time);}
        private void SampleWoodSword()
        {
            if(!WoodSwordConfigured)return;
            if(_woodSwordGrip!=null){_swordLastPosition=_woodSwordGrip.position;_swordLastRotation=_woodSwordGrip.rotation;_swordHadGrip=true;}
            var root=WoodSwordInstance.transform;
            root.position=_swordHadGrip?_swordLastPosition:transform.TransformPoint(new Vector3(.38f,.03f,.6f));
            root.rotation=_swordHadGrip?_swordLastRotation:transform.rotation*Quaternion.Euler(15,0,-22);
            float start=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.4f,Age));
            float end=Mathf.SmoothStep(0,1,Mathf.InverseLerp(Life-.6f,Life,Age));
            float fade=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.12f,Age))*(1-end);
            _swordBlock.SetFloat("_Assemble",start*(1-end));_swordBlock.SetFloat("_Visibility",fade);_swordBlock.SetFloat("_GroundY",-10000);
            _swordBody.SetPropertyBlock(_swordBlock);_swordBody.enabled=fade>.001f&&Age<Life;
            Tint(_swordCollar,Profile.Accent,fade*.65f,end);_swordCollar.enabled=_swordBody.enabled;
            if(_nativeCast!=null)_nativeCast.transform.position=root.position;
            if(!PreviewControlled&&_swordSwing&&Age<Life)AddSwordTrace(root.position,root.rotation,Age);
            while(_swordTraceCount>0&&Age-_swordTraceTimes[0]>.15f)RemoveSwordTrace();
            for(int j=0;j<2;j++)
            {
                var line=_swordTraces[j];line.positionCount=_swordTraceCount;
                for(int i=0;i<_swordTraceCount;i++)line.SetPosition(i,j==0?_swordTips[i]:_swordMids[i]);
                float trailFade=_swordTraceCount==0?0:1-Mathf.Clamp01((Age-_swordTraceTimes[_swordTraceCount-1])/.15f);
                Tint(line,j==0?Profile.Ink:Profile.Pigment,trailFade*fade*.65f,1-trailFade);line.enabled=Age<Life&&_swordTraceCount>1&&trailFade>.001f;
            }
        }
        private void ClearWoodSword()
        {
            if(WoodSwordInstance!=null)DisposeNativeHost(WoodSwordInstance);
            WoodSwordInstance=null;_woodSwordGrip=null;_swordHadGrip=_swordSwing=false;_swordContactAt=-1;
            _swordBody=_swordCollar=null;_swordTraces=null;_swordTips=_swordMids=null;_swordTraceTimes=null;_swordTraceCount=0;WoodSwordContactCount=0;
        }
    }
}
