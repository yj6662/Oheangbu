using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        private sealed class RegrowthSprout
        {
            public Transform Root;
            public MeshRenderer Stem;
            public Transform[] Leaves = new Transform[2];
            public MeshRenderer[] Renderers = new MeshRenderer[2];
            public MaterialPropertyBlock Block = new MaterialPropertyBlock();
        }
        private RegrowthSprout[] _regrowthSprouts;
        private float[] _regrowthLeafTimes;
        private Transform _regrowthFootAnchor;
        private bool _regrowthHadAnchor;
        private Vector3 _regrowthLastFoot;
        private Quaternion _regrowthLastRotation;
        public GameObject RegrowthInstance { get; private set; }
        public bool RegrowthConfigured => RegrowthInstance != null;
        public int RegrowthTicks { get; private set; }
        public int RegrowthOpenedMask { get; private set; }
        public static bool IsRegrowth(Vfx120Profile p) => p != null && p.Glyph == "걱"
            && p.BodyMesh != null && p.BodyMesh.name == "VFX120_RegrowthStem_008"
            && p.AccentMesh != null && p.AccentMesh.name == "VFX120_RegrowthLeaf_008";

        // Feet/root position supplied by the buff owner. Never writes the receiver.
        // A destroyed/detached receiver leaves the last position rather than snapping back.
        public void SetRegrowthFootAnchor(Transform anchor) { _regrowthFootAnchor = anchor; }
        public bool SignalRegrowthTick()
        {
            if(!Begun || !RegrowthConfigured || Age>=Life) return false;
            int sprout=RegrowthTicks%3, side=(RegrowthTicks/3)%2;
            _regrowthLeafTimes[sprout*2+side]=PreviewControlled?Age:Mathf.Max(Age,Time.time-_startedAt);
            RegrowthTicks++;return true;
        }
        public static float RegrowthLeafOpen(float age,float tick)
            => tick<0?0:Mathf.SmoothStep(0,1,Mathf.InverseLerp(tick,tick+.3f,age));
        private void BuildRegrowth()
        {
            if(!IsRegrowth(Profile) || Profile.BodyMaterial==null || Profile.InkMaterial==null) return;
            RegrowthInstance=new GameObject("Regrowth_008");RegrowthInstance.transform.SetParent(transform,false);
            _regrowthLeafTimes=new[]{-1f,-1f,-1f,-1f,-1f,-1f};_regrowthSprouts=new RegrowthSprout[3];
            Vector3[] positions={new Vector3(-.23f,0,.10f),new Vector3(.23f,0,-.06f),new Vector3(.09f,0,.28f)};
            for(int i=0;i<3;i++)
            {
                var root=new GameObject("Sprout_"+i);root.transform.SetParent(RegrowthInstance.transform,false);
                root.transform.localPosition=positions[i];root.transform.localRotation=Quaternion.Euler(0,20+i*113,0);
                root.transform.localScale=Vector3.one*(i==0?.85f:i==1?1f:.92f);
                var s=new RegrowthSprout{Root=root.transform,Stem=WardRenderer("Stem",root.transform,Profile.BodyMesh,Profile.BodyMaterial)};
                for(int j=0;j<2;j++)
                {
                    var leaf=WardRenderer("Leaf_"+j,root.transform,Profile.AccentMesh,Profile.InkMaterial);
                    s.Renderers[j]=leaf;s.Leaves[j]=leaf.transform;
                    leaf.transform.localPosition=new Vector3(j==0?-.002f:.002f,.095f,0);
                }
                _regrowthSprouts[i]=s;
            }
        }
        private void SampleRegrowth()
        {
            if(!RegrowthConfigured)return;
            if(_regrowthFootAnchor!=null)
            {
                _regrowthLastFoot=_regrowthFootAnchor.position;var forward=_regrowthFootAnchor.forward;forward.y=0;
                _regrowthLastRotation=forward.sqrMagnitude>.001f?Quaternion.LookRotation(forward):transform.rotation;
                _regrowthHadAnchor=true;
            }
            var root=RegrowthInstance.transform;
            root.position=_regrowthHadAnchor?_regrowthLastFoot:transform.TransformPoint(new Vector3(0,_originGround,0));
            root.rotation=_regrowthHadAnchor?_regrowthLastRotation:transform.rotation;
            if(_nativeCast!=null) _nativeCast.transform.position=root.position+Vector3.up*.018f;
            float fade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Mathf.Max(0,Life-.28f),Life,Age));
            float dry=Mathf.SmoothStep(0,1,Mathf.InverseLerp(Mathf.Max(0,Life-.65f),Mathf.Max(.01f,Life-.16f),Age));
            RegrowthOpenedMask=0;
            for(int i=0;i<3;i++)
            {
                var s=_regrowthSprouts[i];float rise=Mathf.SmoothStep(0,1,Mathf.InverseLerp(i*.12f,i*.12f+.35f,Age));
                var position=s.Root.localPosition;position.y=-(1-rise)*.28f;s.Root.localPosition=position;
                s.Block.SetFloat("_GroundY",root.position.y-.006f);s.Block.SetFloat("_Visibility",fade);
                s.Block.SetFloat("_VeinOnly",0);s.Stem.SetPropertyBlock(s.Block);s.Stem.enabled=Age<Life&&rise>0&&fade>.001f;
                for(int j=0;j<2;j++)
                {
                    int slot=i*2+j;float tick=_regrowthLeafTimes[slot];
                    if(PreviewControlled && DemonstrationCues && tick<0) tick=.45f+(j*3+i)*.45f;
                    float opened=RegrowthLeafOpen(Age,tick);
                    if(opened>.9f)RegrowthOpenedMask|=1<<slot;
                    float angle=Mathf.Lerp(7,62,opened);angle=Mathf.Lerp(angle,103,dry);
                    s.Leaves[j].localRotation=Quaternion.Euler(0,j==0?8:-8,(j==0?1:-1)*angle);
                    s.Block.SetFloat("_VeinOnly",dry);s.Renderers[j].SetPropertyBlock(s.Block);
                    s.Renderers[j].enabled=Age<Life&&rise>.15f&&fade>.001f;
                }
            }
        }
        private void ClearRegrowth()
        {
            if(RegrowthInstance!=null)DisposeNativeHost(RegrowthInstance);
            RegrowthInstance=null;_regrowthSprouts=null;_regrowthLeafTimes=null;_regrowthFootAnchor=null;
            _regrowthHadAnchor=false;RegrowthTicks=RegrowthOpenedMask=0;
        }
    }
}
