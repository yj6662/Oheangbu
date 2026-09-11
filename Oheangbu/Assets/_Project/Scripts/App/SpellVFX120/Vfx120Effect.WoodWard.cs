using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        public const int WoodWardSupports = 6;
        private sealed class WardSupport
        {
            public Transform Transform, Stamp;
            public MeshRenderer Body, Pattern;
            public Vector3 Position;
            public Quaternion Rotation;
            public float HitAt = -1;
            public MaterialPropertyBlock Block = new MaterialPropertyBlock();
        }
        private WardSupport[] _wardSupports;
        public GameObject WoodWardInstance { get; private set; }
        public bool WoodWardConfigured => WoodWardInstance != null;
        public float WoodWardMaxTilt { get; private set; }
        public int WoodWardHitSignals { get; private set; }
        public static bool IsWoodWard(Vfx120Profile p) => p != null && p.Glyph == "구"
            && p.BodyMesh != null && p.BodyMesh.name == "VFX120_WoodWardSupport_019"
            && p.AccentMesh != null && p.AccentMesh.name == "VFX120_WoodWardPanel_019";

        public static Vector3 WardAnchor(int index, float radius)
        {
            float angle = (30 + index * 60) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(angle) * radius, 0, Mathf.Cos(angle) * radius);
        }
        public static float WardTilt(float age, float hitAt)
        {
            float elapsed = age - hitAt;
            if (hitAt < 0 || elapsed <= 0 || elapsed >= .6f) return 0;
            return Mathf.Sin(elapsed / .6f * Mathf.PI) * Mathf.Exp(-elapsed * 3) * 9;
        }
        // Presentation indices identify the six authored support panels, not enemies or
        // gameplay collision sectors. The future shield adapter supplies resolved hits.
        public bool SignalWoodWardHit(int support)
        {
            if (!Begun || !WoodWardConfigured || support < 0 || support >= WoodWardSupports || Age >= Life) return false;
            _wardSupports[support].HitAt = PreviewControlled ? Age : Mathf.Max(Age, Time.time - _startedAt);
            WoodWardHitSignals++; return true;
        }

        private void BuildWoodWard()
        {
            if (!IsWoodWard(Profile) || Profile.BodyMaterial == null || Profile.PatternMaterial == null) return;
            WoodWardInstance = new GameObject("WoodWard_019"); WoodWardInstance.transform.SetParent(transform, false);
            _wardSupports = new WardSupport[WoodWardSupports]; WoodWardHitSignals = 0;
            float radius = Mathf.Clamp(Profile.Size, .8f, 4f);
            for (int i = 0; i < WoodWardSupports; i++)
            {
                Vector3 anchor = WardAnchor(i, radius);
                Vector3 world = transform.TransformPoint(anchor);
                anchor.y = GroundHeight(world, transform.position.y + _originGround) - transform.position.y;
                var support = new GameObject("WardSupport_" + i); support.transform.SetParent(WoodWardInstance.transform, false);
                var body = WardRenderer("CaneFrame", support.transform, Profile.BodyMesh, Profile.BodyMaterial);
                var pattern = WardRenderer("KTP_InterlockingPanel", support.transform, Profile.AccentMesh, Profile.PatternMaterial);
                _wardSupports[i] = new WardSupport { Transform = support.transform, Body = body, Pattern = pattern,
                    Position = anchor, Rotation = Quaternion.Euler(0, 30 + i * 60, 0) };
                // Only the known runtime copy is aligned to the sampled ground; original
                // KTP curves and the source prefab remain untouched.
                if (_nativeField != null && _nativeField.ContentTransform != null)
                {
                    var stamp = _nativeField.ContentTransform.Find("Footprint_" + i);
                    _wardSupports[i].Stamp = stamp;
                }
            }
        }
        static MeshRenderer WardRenderer(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            return renderer;
        }
        private void SampleWoodWard()
        {
            if (!WoodWardConfigured) return;
            float patternFade = 1 - Mathf.SmoothStep(0,1,Mathf.InverseLerp(Mathf.Max(0,Life-.85f),Mathf.Max(.01f,Life-.35f),Age));
            float bodyFade = 1 - Mathf.SmoothStep(0,1,Mathf.InverseLerp(Mathf.Max(0,Life-.4f),Life,Age));
            WoodWardMaxTilt = 0;
            for (int i=0;i<WoodWardSupports;i++)
            {
                var s = _wardSupports[i]; float start = i * .065f;
                if (s.Stamp != null)
                    s.Stamp.localPosition = (s.Position - new Vector3(0, _originGround, 0)) / Profile.NativeScale;
                float rise = Mathf.SmoothStep(0,1,Mathf.InverseLerp(start,start+.42f,Age));
                float hit = s.HitAt;
                if (PreviewControlled && DemonstrationCues && hit < 0)
                { if(i==1) hit=.95f; else if(i==4) hit=1.6f; }
                float tilt = WardTilt(Age,hit); WoodWardMaxTilt = Mathf.Max(WoodWardMaxTilt,tilt);
                s.Transform.localPosition = s.Position - Vector3.up * ((1-rise)*1.63f);
                s.Transform.localRotation = s.Rotation * Quaternion.Euler(-tilt,0,0);
                s.Transform.localScale = Vector3.one;
                s.Block.SetFloat("_Visibility",bodyFade);
                s.Block.SetFloat("_GroundY", transform.TransformPoint(s.Position).y - .012f);
                s.Body.SetPropertyBlock(s.Block);
                s.Body.enabled = rise > 0 && bodyFade > .001f && Age < Life;
                float inkIn = Mathf.SmoothStep(0,1,Mathf.InverseLerp(start+.28f,start+.6f,Age));
                Tint(s.Pattern,Color.Lerp(Profile.Pigment,Profile.Accent,.24f),inkIn*patternFade*.52f,1-patternFade);
                s.Pattern.enabled = inkIn > .001f && patternFade > .001f && Age < Life;
            }
        }
        private void ClearWoodWard()
        {
            if(WoodWardInstance!=null) DisposeNativeHost(WoodWardInstance);
            WoodWardInstance=null;_wardSupports=null;WoodWardMaxTilt=0;WoodWardHitSignals=0;
        }
    }
}
