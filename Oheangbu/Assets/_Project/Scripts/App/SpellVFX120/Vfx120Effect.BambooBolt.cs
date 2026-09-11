using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        private MeshRenderer _boltBody,_boltInk;
        private MeshRenderer[] _boltLeaves;
        private MaterialPropertyBlock _boltBlock;
        private Vector3 _boltContact;
        private Quaternion _boltFacing;
        private bool _boltLanded;
        public GameObject BambooBoltInstance { get; private set; }
        public bool BambooBoltConfigured=>BambooBoltInstance!=null;
        public bool BambooBoltContactSeen=>_boltLanded;
        public Vector3 BambooBoltTip { get; private set; }
        public float BambooBoltFlight=>_flight;
        public static bool IsBambooBolt(Vfx120Profile p)=>p!=null&&p.Glyph=="가"&&p.BodyMesh!=null&&p.BodyMesh.name=="VFX120_BambooBolt_001";
        private void BuildBambooBolt()
        {
            if(!IsBambooBolt(Profile))return;
            BambooBoltInstance=new GameObject("BambooBolt_001");BambooBoltInstance.transform.SetParent(transform,false);
            _boltFacing=Quaternion.identity;_boltContact=ReceivedOrigin;
            _boltBody=WardRenderer("ThreeNodes_LeafFins",BambooBoltInstance.transform,Profile.BodyMesh,Profile.BodyMaterial);
            _boltBody.sharedMaterials=new[]{Profile.BodyMaterial,Profile.InkMaterial};_boltBlock=new MaterialPropertyBlock();
            _boltLeaves=new MeshRenderer[3];for(int i=0;i<3;i++)_boltLeaves[i]=WardRenderer("ContactLeaf_"+i,BambooBoltInstance.transform,Profile.AccentMesh,Profile.InkMaterial);
            _boltInk=WardRenderer("KTP_LongInkContact",BambooBoltInstance.transform,QuadMesh.Value,Profile.PatternMaterial);
            _boltInk.transform.localScale=new Vector3(.16f,.36f,1);
        }
        private void SampleBambooBolt()
        {
            if(!BambooBoltConfigured)return;
            float flight=Mathf.Max(.01f,_flight),impact=NativeImpactClock();bool landed=impact>=0&&Age>=impact;
            Vector3 target=TargetPoint(),direction=(target-ReceivedOrigin).normalized;if(direction.sqrMagnitude<.001f)direction=Vector3.forward;
            var facing=Quaternion.LookRotation(direction,Mathf.Abs(direction.y)>.98f?Vector3.forward:Vector3.up);
            if(landed&&!_boltLanded){_boltContact=target;_boltFacing=facing;_boltLanded=true;}
            BambooBoltTip=landed?_boltContact:Vector3.Lerp(ReceivedOrigin,target,Mathf.Clamp01(Age/flight));
            _boltBody.transform.SetPositionAndRotation(BambooBoltTip,landed?_boltFacing:facing);
            float after=landed?Age-impact:0;
            float dry=landed?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.04f,.35f,after)):0;
            float visibility=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Life-.25f,Life,Age));
            _boltBlock.SetFloat("_Build",Mathf.Clamp01(Age/.09f));_boltBlock.SetFloat("_Dry",dry);_boltBlock.SetFloat("_Age",Age);_boltBlock.SetFloat("_Visibility",visibility);
            _boltBody.SetPropertyBlock(_boltBlock);_boltBody.enabled=Age<Life&&visibility>.001f&&dry<1;
            for(int i=0;i<3;i++)
            {
                var leaf=_boltLeaves[i];float a=(i*120+23)*Mathf.Deg2Rad;
                Vector3 outward=_boltFacing*new Vector3(Mathf.Cos(a),Mathf.Sin(a),0);
                leaf.transform.SetPositionAndRotation(_boltContact+outward*(.035f+after*.24f)+Vector3.down*(after*after*.34f),_boltFacing*Quaternion.Euler(15+i*13,after*75,i*120+23));
                _boltBlock.SetFloat("_Build",1);_boltBlock.SetFloat("_Dry",0);_boltBlock.SetFloat("_Visibility",landed?(1-Mathf.Clamp01(after/.6f))*visibility:0);
                leaf.SetPropertyBlock(_boltBlock);leaf.enabled=landed&&after<.6f&&Age<Life;
            }
            _boltInk.transform.SetPositionAndRotation(_boltContact-direction*.008f,_boltFacing);
            float ink=landed?1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.18f,.6f,after)):0;
            Tint(_boltInk,Profile.Ink,ink*.7f,1-ink);_boltInk.enabled=landed&&Age<Life&&ink>.001f;
        }
        private void ClearBambooBolt()
        {
            if(BambooBoltInstance!=null)DisposeNativeHost(BambooBoltInstance);
            BambooBoltInstance=null;_boltBody=_boltInk=null;_boltLeaves=null;_boltBlock=null;_boltLanded=false;BambooBoltTip=Vector3.zero;
        }
    }
}
