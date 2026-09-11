using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        private sealed class GuardSurface
        {
            public Mesh Mesh;
            public Vector3[] Rest, Deformed;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Block = new MaterialPropertyBlock();
        }
        private GuardSurface[] _guardSurfaces;
        private static Vfx120Effect _activeBambooGuard;
        private Vector3 _guardContact = new Vector3(0, .2f, .83f);
        public GameObject BambooGuardInstance { get; private set; }
        public bool BambooGuardConfigured => BambooGuardInstance != null;
        public float BambooGuardBend { get; private set; }
        public static bool IsBambooGuard(Vfx120Profile p) => p != null && p.Glyph == "거"
            && p.BodyMesh != null && p.BodyMesh.name == "VFX120_BambooGuard_007"
            && p.AccentMesh != null && p.AccentMesh.name == "VFX120_BambooGuardBorder_007";

        // The single-player cast adapter selects the current guard. Selecting a different
        // guard clears the old receiver; catalog previews never register themselves.
        public static void SelectBambooGuard(Vfx120Effect current)
        { _activeBambooGuard = current != null && current.BambooGuardConfigured && !current.PreviewControlled ? current : null; }

        public static bool TrySignalBambooParry(Vector3 worldContact, bool externalContact = false)
        {
            var effect = _activeBambooGuard;
            if (effect == null || !effect.Begun || !effect.BambooGuardConfigured || !effect.isActiveAndEnabled
                || effect.Age >= effect.Life || Time.time - effect._startedAt >= effect.Life) return false;
            effect._externalGuardContact = externalContact;
            return effect.SignalBambooContact(worldContact);
        }

        public bool SignalBambooContact(Vector3 worldContact)
        {
            if (!Begun || !BambooGuardConfigured || !Vfx120InterceptionMotion.Finite(worldContact) || Age >= Life) return false;
            if (ParryAt >= 0) return true; // one flex/contact motif per guard, without retiming
            Vector3 local = transform.InverseTransformPoint(worldContact);
            _guardContact = new Vector3(Mathf.Clamp(local.x, -.85f, .85f), Mathf.Clamp(local.y, -.35f, .4f), .83f);
            _guardContact.z -= .22f * _guardContact.x * _guardContact.x;
            Signal(Cue.Parry); return true;
        }

        private float BambooParryClock => ParryAt >= 0 ? ParryAt : PreviewControlled && DemonstrationCues ? .36f : -1;

        public static float GuardFlex(float age, float parryAt)
        {
            if (parryAt < 0 || age <= parryAt || age >= parryAt + .48f) return 0;
            float t = (age - parryAt) / .48f;
            return Mathf.Sin(t * Mathf.PI) * Mathf.Exp(-2.4f * t) * .25f;
        }

        public static Vector3 GuardVertex(Vector3 rest, float age, float bend, float contactX)
        {
            const float pivotY = -.6f;
            float open = Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / .20f));
            float angle = Mathf.Atan2(rest.x, rest.y - pivotY);
            float radius = new Vector2(rest.x, rest.y - pivotY).magnitude;
            float a = angle * Mathf.Lerp(.06f, 1, open);
            var v = new Vector3(Mathf.Sin(a) * radius, pivotY + Mathf.Cos(a) * radius, rest.z);
            float upper = Mathf.Clamp01((rest.y - pivotY) / 1.12f);
            float localWeight = Mathf.Exp(-Mathf.Pow((rest.x - contactX) / .65f, 2));
            v.z -= bend * upper * upper * (.25f + .75f * localWeight);
            return v;
        }

        private void BuildBambooGuard()
        {
            if (!IsBambooGuard(Profile) || Profile.BodyMaterial == null || Profile.PatternMaterial == null) return;
            _guardContact = new Vector3(0, .2f, .83f);
            BambooGuardInstance = new GameObject("BambooGuard_007");
            BambooGuardInstance.transform.SetParent(transform, false);
            if(Profile.KtpPatternShield)return;
            _guardSurfaces = new GuardSurface[2];
            for (int i = 0; i < 2; i++)
            {
                var source = i == 0 ? Profile.BodyMesh : Profile.AccentMesh;
                var mesh = Instantiate(source); mesh.name = source.name + "_Runtime"; mesh.MarkDynamic();
                var go = new GameObject(i == 0 ? "BambooRibs" : "KTP_OpenBorder");
                go.transform.SetParent(BambooGuardInstance.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = i == 0 ? Profile.BodyMaterial : Profile.PatternMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _guardSurfaces[i] = new GuardSurface { Mesh = mesh, Rest = mesh.vertices,
                    Deformed = new Vector3[mesh.vertexCount], Renderer = mr };
            }
        }

        private void SampleBambooGuard()
        {
            if (!BambooGuardConfigured) return;
            float end = Life;
            if (BreakAt >= 0) end = Mathf.Min(end, BreakAt + .2f);
            if (ReleaseAt >= 0) end = Mathf.Min(end, ReleaseAt + .3f);
            float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Mathf.Max(0, end - .35f), end, Age));
            float alpha = Mathf.SmoothStep(0, 1, Mathf.Clamp01(Age / .1f)) * fade;
            float brightWindow = ReceivedGuardBrightWindow > 0 ? ReceivedGuardBrightWindow : .55f;
            float residual = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(brightWindow, brightWindow + .2f, Age));
            BambooGuardBend = GuardFlex(Age, BambooParryClock);
            if(Profile.KtpPatternShield)return;
            for (int i = 0; i < _guardSurfaces.Length; i++)
            {
                var part = _guardSurfaces[i];
                for (int v = 0; v < part.Rest.Length; v++)
                    part.Deformed[v] = GuardVertex(part.Rest[v], Age, BambooGuardBend, _guardContact.x);
                part.Mesh.vertices = part.Deformed;
                if (i == 0) { part.Mesh.RecalculateNormals(); part.Mesh.RecalculateTangents(); }
                // Fixed conservative bounds cover the authored unfolding and bounded flex.
                part.Mesh.bounds = new Bounds(new Vector3(0, 0, .75f), new Vector3(2.5f, 1.6f, .9f));
                if (i == 0)
                {
                    part.Block.SetFloat("_Visibility", alpha);
                    part.Block.SetFloat("_GroundY", -10000);
                    part.Block.SetColor("_BaseColor", Color.Lerp(Color.white, new Color(.52f,.57f,.51f,1), residual));
                    part.Renderer.SetPropertyBlock(part.Block);
                }
                else Tint(part.Renderer, Color.Lerp(Profile.Accent, Profile.Pigment, residual), alpha * Mathf.Lerp(.72f,.32f,residual), 1-fade);
                part.Renderer.enabled = alpha > .001f && Age < end;
            }
        }

        private void ClearBambooGuard()
        {
            if (_activeBambooGuard == this) _activeBambooGuard = null;
            if (_guardSurfaces != null) foreach (var part in _guardSurfaces)
                if (part != null && part.Mesh != null)
                { if (Application.isPlaying) Destroy(part.Mesh); else DestroyImmediate(part.Mesh); }
            if (BambooGuardInstance != null) DisposeNativeHost(BambooGuardInstance);
            BambooGuardInstance = null; _guardSurfaces = null; BambooGuardBend = 0;
        }
    }
}
