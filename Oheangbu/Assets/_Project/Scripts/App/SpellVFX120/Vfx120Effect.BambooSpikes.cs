using UnityEngine;
using Oheangbu.Spellcraft;

namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        private sealed class BambooSpike
        {
            public Transform Root;
            public MeshRenderer Renderer;
            public Vector3 Position, Scale;
            public float Ground, Height;
            public MaterialPropertyBlock Block;
        }
        private BambooSpike[] _bambooSpikes;
        private MeshRenderer[] _bambooRootMarks;
        private Vector3 _bambooCenter;
        private float _bambooRadius, _bambooDelay;
        public GameObject BambooSpikesInstance { get; private set; }
        public bool BambooSpikesConfigured { get; private set; }
        public int BambooSpikesRaised { get; private set; }
        public float BambooSpikesRise { get; private set; }
        public float BambooSpikesDelay => _bambooDelay;
        public float BambooSpikesRadius => _bambooRadius;
        public Vector3 BambooSpikesCenter => _bambooCenter;
        public static bool IsBambooSpikes(Vfx120Profile p) => p != null && p.Glyph == "고"
            && p.BodyMesh != null && p.BodyMesh.name == "VFX120_BambooSpike_013";

        public static Vector2 BambooSpikeFoot(int index, float radius)
        {
            float angle = index * 2.39996323f;
            float r = Mathf.Sqrt((index + .35f) / 17f) * radius * .82f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
        }
        public Vector3 BambooSpikePosition(int index) => _bambooSpikes != null && index >= 0 && index < _bambooSpikes.Length
            ? _bambooSpikes[index].Root.position : Vector3.zero;
        public Vector3 BambooSpikeScale(int index) => _bambooSpikes[index].Root.localScale;

        private void BuildBambooSpikes()
        {
            if (!IsBambooSpikes(Profile)) return;
            var p = ReceivedAreaPlan;
            // A missing/invalid combat plan stays quiet; it never revives the old field.
            if (p == null || p.Shape != AreaShape.Circle || !WashFinite(p.Point)
                || !WashFinite(p.Radius) || p.Radius <= 0 || !WashFinite(p.Delay) || p.Delay < 0) return;
            _bambooCenter = p.Point; _bambooRadius = p.Radius; _bambooDelay = p.Delay;
            BambooSpikesInstance = new GameObject("BambooSpikes_013");
            var host = BambooSpikesInstance.transform;
            host.SetParent(transform, false); host.SetPositionAndRotation(_bambooCenter, Quaternion.identity);
            _bambooSpikes = new BambooSpike[17];
            for (int i = 0; i < _bambooSpikes.Length; i++)
            {
                var foot = BambooSpikeFoot(i, _bambooRadius);
                Vector3 point = _bambooCenter + new Vector3(foot.x, 0, foot.y);
                float ground = GroundHeight(point, point.y);
                var renderer = WardRenderer("Cane_" + i, host, Profile.BodyMesh, Profile.BodyMaterial);
                var t = renderer.transform;
                Vector3 direction = (Vector3.up - new Vector3(foot.x, 0, foot.y).normalized * (.06f + (i%3)*.03f)).normalized;
                t.rotation = Quaternion.FromToRotation(Vector3.up, direction) * Quaternion.Euler(0, i * 73, 0);
                // Fixed authoring variation; no animated scaling during rise or resolve.
                Vector3 scale = new Vector3(.65f + (i%4)*.15f, .75f + (i%5)*.17f, .65f + (i%4)*.15f);
                t.localScale = scale;
                _bambooSpikes[i] = new BambooSpike { Root=t, Renderer=renderer, Position=new Vector3(point.x,ground,point.z),
                    Scale=scale, Ground=ground, Height=Profile.BodyMesh.bounds.max.y*scale.y+.08f, Block=new MaterialPropertyBlock() };
            }
            _bambooRootMarks = new MeshRenderer[5];
            for (int i=0;i<_bambooRootMarks.Length;i++)
            {
                float a=i*2.39996323f;var point=_bambooCenter+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*_bambooRadius*.45f;
                point.y=GroundHeight(point,point.y)+.014f;
                var r=WardRenderer("RootInk_"+i,host,Profile.AccentMesh,Profile.PatternMaterial);
                r.transform.SetPositionAndRotation(point,Quaternion.Euler(90,i*137,0));
                r.transform.localScale=new Vector3(.55f,.8f,1)*Mathf.Min(1.4f,_bambooRadius);
                _bambooRootMarks[i]=r;
            }
            BambooSpikesConfigured=true;
        }

        private void SampleBambooSpikes()
        {
            if (!BambooSpikesConfigured || BambooSpikesInstance == null) return;
            // Complete on the supplied damage clock, not a new timer after damage.
            float begin=Mathf.Max(0,_bambooDelay-.13f);
            float rise=_bambooDelay<=0?1:Mathf.SmoothStep(0,1,Mathf.InverseLerp(begin,_bambooDelay,Age));
            float resolve=Mathf.SmoothStep(0,1,Mathf.InverseLerp(Mathf.Max(_bambooDelay+.2f,Life-.55f),Life,Age));
            bool alive=Age<Life;
            BambooSpikesRise=alive?rise:0;BambooSpikesRaised=alive&&rise>.999f?17:0;
            foreach(var part in _bambooSpikes)
            {
                part.Root.position=part.Position-Vector3.up*part.Height*(1-rise);
                part.Block.SetFloat("_GroundY",part.Ground-.006f);
                part.Block.SetFloat("_Visibility",1);
                part.Block.SetFloat("_DissolveHeight",Mathf.Lerp(1.45f,-.15f,resolve));
                part.Renderer.SetPropertyBlock(part.Block);
                part.Renderer.enabled=alive&&rise>0&&resolve<1;
            }
            float ink=Mathf.Clamp01(Age/.12f)*(1-resolve)*.68f;
            foreach(var r in _bambooRootMarks)
            { Tint(r,Profile.Pigment*.55f,ink,resolve*.45f);r.enabled=alive&&ink>.001f; }
            if(_nativeCast!=null) _nativeCast.transform.position=new Vector3(_bambooCenter.x,_bambooSpikes[0].Ground+.02f,_bambooCenter.z);
        }
        private void ClearBambooSpikes()
        {
            if(BambooSpikesInstance!=null)DisposeNativeHost(BambooSpikesInstance);
            BambooSpikesInstance=null;_bambooSpikes=null;_bambooRootMarks=null;
            BambooSpikesConfigured=false;BambooSpikesRaised=0;BambooSpikesRise=0;
            _bambooCenter=Vector3.zero;_bambooRadius=_bambooDelay=0;
        }
    }
}
