using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        private Mesh[] _bloomMeshes;
        private Vector3[] _bloomRest, _bloomVertices;
        private MeshRenderer[] _bloomRenderers;
        private MaterialPropertyBlock _bloomBlock;
        private Transform _bloomReceiver;
        private bool _bloomHadReceiver;
        private Vector3 _bloomLastPosition;
        private Quaternion _bloomLastRotation;
        private float _bloomAt=-1;
        public GameObject BloomInstance { get; private set; }
        public bool BloomConfigured => BloomInstance!=null;
        public float BloomOpened { get; private set; }
        public float BloomClock => _bloomAt>=0?_bloomAt:PreviewControlled&&DemonstrationCues?.28f:-1;
        public static bool IsBloom(Vfx120Profile p)=>p!=null&&p.Glyph=="건"&&p.BodyMesh!=null&&p.BodyMesh.name=="VFX120_BloomLeaf_009";
        // The caller supplies a torso anchor and an already resolved healing cue.
        // No Health, buff timer or receiver transform is modified by this presentation.
        public void SetBloomReceiver(Transform torso){_bloomReceiver=torso;}
        public bool SignalBloom()
        {
            if(!Begun||!BloomConfigured||_bloomAt>=0||Age>=Life)return false;
            _bloomAt=PreviewControlled?Age:Mathf.Max(Age,Time.time-_startedAt);return true;
        }
        public static float BloomOpening(float age,float at)=>at<0?0:Mathf.SmoothStep(0,1,Mathf.InverseLerp(at,at+.22f,age));
        public static Vector3 BloomVertex(Vector3 rest,float opened)
        {
            // Roll along the horizontal arc, preserving its length rather than scaling a leaf.
            float curvature=Mathf.Lerp(7.5f,.65f,opened);
            float a=rest.x*curvature;
            return new Vector3(Mathf.Sin(a)/curvature,rest.y,(1-Mathf.Cos(a))/curvature+.035f*Mathf.Pow(rest.y/.43f,2));
        }
        private void BuildBloom()
        {
            if(!IsBloom(Profile)||Profile.BodyMaterial==null)return;
            _bloomBlock=new MaterialPropertyBlock();
            BloomInstance=new GameObject("Bloom_009");BloomInstance.transform.SetParent(transform,false);
            _bloomRest=Profile.BodyMesh.vertices;_bloomVertices=new Vector3[_bloomRest.Length];
            _bloomMeshes=new Mesh[2];_bloomRenderers=new MeshRenderer[2];
            for(int i=0;i<2;i++)
            {
                var mesh=Instantiate(Profile.BodyMesh);mesh.name="BloomRuntime_"+i;mesh.MarkDynamic();_bloomMeshes[i]=mesh;
                _bloomRenderers[i]=WardRenderer("FoldedLeaf_"+i,BloomInstance.transform,mesh,Profile.BodyMaterial);
            }
        }
        private void SampleBloom()
        {
            if(!BloomConfigured)return;
            if(_bloomReceiver!=null)
            {
                _bloomLastPosition=_bloomReceiver.position;Vector3 forward=_bloomReceiver.forward;forward.y=0;
                _bloomLastRotation=forward.sqrMagnitude>.001f?Quaternion.LookRotation(forward):transform.rotation;_bloomHadReceiver=true;
            }
            var root=BloomInstance.transform;
            root.position=_bloomHadReceiver?_bloomLastPosition:transform.TransformPoint(new Vector3(0,.02f,.18f));
            root.rotation=_bloomHadReceiver?_bloomLastRotation:transform.rotation;
            float at=BloomClock;BloomOpened=BloomOpening(Age,at);
            float arrival=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.22f,Age));
            float release=at<0?0:Mathf.SmoothStep(0,1,Mathf.InverseLerp(at+.55f,Mathf.Min(Life,at+1.05f),Age));
            float endFade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Mathf.Max(0,Life-.2f),Life,Age));
            float fade=(1-release)*endFade*arrival;
            for(int v=0;v<_bloomRest.Length;v++)_bloomVertices[v]=BloomVertex(_bloomRest[v],BloomOpened*(1-release*.35f));
            for(int i=0;i<2;i++)
            {
                float side=i==0?-1:1;var renderer=_bloomRenderers[i];
                renderer.transform.localPosition=new Vector3(side*Mathf.Lerp(.33f,.61f,BloomOpened),Mathf.Lerp(-.42f,0,arrival)-release*.16f,.04f);
                renderer.transform.localRotation=Quaternion.Euler(0,side*Mathf.Lerp(34,12,BloomOpened),side*Mathf.Lerp(17,37,BloomOpened));
                _bloomMeshes[i].vertices=_bloomVertices;_bloomMeshes[i].RecalculateNormals();_bloomMeshes[i].RecalculateTangents();_bloomMeshes[i].RecalculateBounds();
                _bloomBlock.SetFloat("_Visibility",fade);_bloomBlock.SetFloat("_PatternStrength",BloomOpened*(1-release)*.5f);
                _bloomBlock.SetFloat("_GroundY",transform.position.y+_originGround-.01f);
                renderer.SetPropertyBlock(_bloomBlock);renderer.enabled=Age<Life&&fade>.001f;
            }
            if(_nativeImpact!=null)_nativeImpact.transform.position=root.position;
        }
        private void ClearBloom()
        {
            if(BloomInstance!=null)DisposeNativeHost(BloomInstance);
            if(_bloomMeshes!=null)foreach(var mesh in _bloomMeshes)if(mesh!=null){if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}
            BloomInstance=null;_bloomMeshes=null;_bloomRenderers=null;_bloomRest=_bloomVertices=null;
            _bloomReceiver=null;_bloomHadReceiver=false;_bloomAt=-1;BloomOpened=0;
        }
    }
}
