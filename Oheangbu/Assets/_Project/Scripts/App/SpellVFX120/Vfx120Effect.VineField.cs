using UnityEngine;
using Oheangbu.Spellcraft;

namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        private Mesh _vineGroundMesh;
        private MeshRenderer _vineRenderer;
        private MaterialPropertyBlock _vineBlock;
        private LineRenderer[] _vineContacts;
        private float[] _vineContactTimes;
        private int _vineNextContact;
        private float _vineGroundCenter, _vineRadius, _vineStart, _vineRelease=-1;
        private Vector3 _vineCenter;
        public GameObject VineFieldInstance { get; private set; }
        public bool VineFieldConfigured=>VineFieldInstance!=null;
        public float VineFieldSpread { get; private set; }
        public int VineFieldContactCount { get; private set; }
        public int VineFieldActiveContacts { get; private set; }
        public float VineFieldReleaseAt=>_vineRelease;
        public Mesh VineFieldOwnedMesh=>_vineGroundMesh;
        public static bool IsVineField(Vfx120Profile p)=>p!=null&&p.Glyph=="곡"&&p.BodyMesh!=null&&p.BodyMesh.name=="VFX120_VineField_014";

        // A caller supplies an already-confirmed affected foot. This never detects actors or applies slow.
        public bool SignalVineFootContact(Vector3 foot,float relativeTime=-1)
        {
            float at=relativeTime>=0?relativeTime:PreviewControlled?Age:Mathf.Max(Age,Time.time-_startedAt);
            if(!VineFieldConfigured||!WashFinite(foot)||!WashFinite(at)||at<_vineStart||at>=Life||Age>=Life||_vineRelease>=0)return false;
            var delta=foot-_vineCenter;delta.y=0;if(delta.magnitude>_vineRadius)return false;
            int slot=_vineNextContact++%_vineContacts.Length;
            _vineContactTimes[slot]=at;
            var line=_vineContacts[slot];line.positionCount=13;
            for(int i=0;i<13;i++)
            {
                float t=i/12f,angle=Mathf.Lerp(-145,130,t)*Mathf.Deg2Rad;
                line.SetPosition(i,foot+new Vector3(Mathf.Cos(angle)*.15f,.07f+Mathf.Sin(t*Mathf.PI)*.09f,Mathf.Sin(angle)*.13f));
            }
            VineFieldContactCount++;return true;
        }
        public bool ReleaseVineField(float relativeTime=-1)
        {
            float at=relativeTime>=0?relativeTime:PreviewControlled?Age:Mathf.Max(Age,Time.time-_startedAt);
            if(!VineFieldConfigured||!WashFinite(at)||at<_vineStart||at>=Life||Age>=Life||_vineRelease>=0)return false;
            _vineRelease=at;return true;
        }
        private void BuildVineField()
        {
            if(!IsVineField(Profile))return;var p=ReceivedAreaPlan;
            if(p==null||p.Shape!=AreaShape.Circle||!WashFinite(p.Point)||!WashFinite(p.Radius)||p.Radius<=0||!WashFinite(p.Delay)||p.Delay<0)return;
            _vineCenter=p.Point;_vineRadius=p.Radius;_vineStart=p.Delay;
            VineFieldInstance=new GameObject("VineField_014");var host=VineFieldInstance.transform;host.SetParent(transform,false);host.SetPositionAndRotation(_vineCenter,Quaternion.identity);
            // 25 ground probes per creation, bilinear interpolation thereafter. No per-frame raycasts.
            var heights=new float[5,5];
            for(int z=0;z<5;z++)for(int x=0;x<5;x++)
            {var q=_vineCenter+new Vector3((x/2f-1)*_vineRadius,0,(z/2f-1)*_vineRadius);heights[x,z]=GroundHeight(q,q.y)-_vineCenter.y;}
            _vineGroundCenter=_vineCenter.y+heights[2,2];
            _vineGroundMesh=Instantiate(Profile.BodyMesh);_vineGroundMesh.name="VineField_GroundInstance";
            VineFieldInstance.AddComponent<Vfx120VineMeshOwner>().Owned=_vineGroundMesh;
            var verts=_vineGroundMesh.vertices;
            for(int i=0;i<verts.Length;i++)
            {
                var v=verts[i];float gx=Mathf.Clamp((v.x+1)*2,0,4),gz=Mathf.Clamp((v.z+1)*2,0,4);int ix=Mathf.Min(3,Mathf.FloorToInt(gx)),iz=Mathf.Min(3,Mathf.FloorToInt(gz));
                float y=Mathf.Lerp(Mathf.Lerp(heights[ix,iz],heights[ix+1,iz],gx-ix),Mathf.Lerp(heights[ix,iz+1],heights[ix+1,iz+1],gx-ix),gz-iz);
                verts[i]=new Vector3(v.x*_vineRadius,v.y+y,v.z*_vineRadius);
            }
            _vineGroundMesh.vertices=verts;_vineGroundMesh.RecalculateNormals();_vineGroundMesh.RecalculateTangents();_vineGroundMesh.RecalculateBounds();
            _vineRenderer=WardRenderer("Vines_Leaves_KTP",host,_vineGroundMesh,Profile.BodyMaterial);
            _vineRenderer.sharedMaterials=new[]{Profile.BodyMaterial,Profile.InkMaterial,Profile.PatternMaterial};_vineBlock=new MaterialPropertyBlock();
            _vineContacts=new LineRenderer[3];_vineContactTimes=new[]{-1f,-1f,-1f};
            for(int i=0;i<3;i++)
            {
                var child=new GameObject("ConfirmedAnkleInk_"+i);child.transform.SetParent(host,false);var line=child.AddComponent<LineRenderer>();
                line.sharedMaterial=Profile.MistMaterial;line.useWorldSpace=true;line.positionCount=0;line.widthMultiplier=.014f;line.numCapVertices=2;line.numCornerVertices=2;
                line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;_vineContacts[i]=line;
            }
        }
        private void SampleVineField()
        {
            if(!VineFieldConfigured)return;
            float grow=Mathf.SmoothStep(0,1,Mathf.InverseLerp(_vineStart,_vineStart+1.1f,Age));
            float release=_vineRelease>=0?_vineRelease:Mathf.Max(_vineStart,Life-.7f);
            float dry=Mathf.SmoothStep(0,1,Mathf.InverseLerp(release,release+.7f,Age));
            VineFieldSpread=grow*(1-dry);
            _vineBlock.SetFloat("_Spread",VineFieldSpread);_vineBlock.SetFloat("_Dry",dry);_vineBlock.SetFloat("_Age",Age);
            _vineRenderer.SetPropertyBlock(_vineBlock);_vineRenderer.enabled=Age<Life&&VineFieldSpread>.001f;
            VineFieldActiveContacts=0;
            for(int i=0;i<_vineContacts.Length;i++)
            {
                float elapsed=Age-_vineContactTimes[i];float alpha=_vineContactTimes[i]>=0&&elapsed>=0&&elapsed<.55f?(1-elapsed/.55f)*(1-dry):0;
                var line=_vineContacts[i];line.enabled=Age<Life&&alpha>.001f;
                if(line.enabled)VineFieldActiveContacts++;
                line.startColor=line.endColor=new Color(.21f,.25f,.12f,alpha);
            }
            if(_nativeCast!=null)_nativeCast.transform.position=new Vector3(_vineCenter.x,GroundCenterY()+.015f,_vineCenter.z);
        }
        private float GroundCenterY()=>_vineGroundCenter;
        private void ClearVineField()
        {
            if(VineFieldInstance!=null)
            {
                VineFieldInstance.GetComponent<Vfx120VineMeshOwner>()?.Release();
                DisposeNativeHost(VineFieldInstance);
            }
            VineFieldInstance=null;_vineGroundMesh=null;_vineRenderer=null;_vineContacts=null;_vineContactTimes=null;_vineBlock=null;
            _vineRelease=-1;_vineNextContact=VineFieldContactCount=VineFieldActiveContacts=0;VineFieldSpread=0;
        }
    }
}
