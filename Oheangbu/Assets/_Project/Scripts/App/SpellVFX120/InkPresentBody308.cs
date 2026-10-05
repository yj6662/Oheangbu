using Oheangbu.Data.Spell;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.SpellVFX120
{
    // #308 present add-on: one composed ink body (InkPresentForms308) drawn with the deploy layer's burst material. Pooled by
    // SpellPresentStage308: its buffer and mesh are made once. The clock is the layer's cel clock: the mesh is assembled once in
    // Show, a cel tick only changes the property block. Timeline: birth beat (the strokes are born and glow for their birth
    // cels) -> hold (ink, glow 0) -> melt -> hidden. A carried body follows its carrier and is held until Release.
    // forms3 (D9): a zone's ring stroke lies on the ground the stage fitted (`up` of Show) and is held until Release as well.
    // Presentation only: it reads a transform's position and nothing else.
    public sealed class InkPresentBody308 : MonoBehaviour
    {
        private static readonly int CelId = Shader.PropertyToID("_Cel");
        private static readonly int AdvanceId = Shader.PropertyToID("_Advance");
        private static readonly int MeltId = Shader.PropertyToID("_Melt");
        private static readonly int RiseId = Shader.PropertyToID("_ColumnRise");
        private static readonly int DrainId = Shader.PropertyToID("_ColumnDrain");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int TintAmountId = Shader.PropertyToID("_TintAmount");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int GlowId = Shader.PropertyToID("_Glow");
        private static readonly int GlowCelsId = Shader.PropertyToID("_GlowCels");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int GlowRimId = Shader.PropertyToID("_GlowRim");

        private SpellDeploy308ProfileSO _profile;
        private SpellPresent308SheetSO _sheet;
        private InkBurstBuffer308 _buffer;
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private Transform _carrier;
        private bool _carried;
        private float _start, _cel, _holdEnd, _meltStart, _melt;
        private int _lastCel, _glowEndCel;

        public bool Busy { get; private set; }
        /// <summary>The presenter handle that asked for it (0 = the stage's own, e.g. a state mark).</summary>
        public int Owner { get; private set; }
        public PresentBody308 Form { get; private set; }
        public PresentState308 States { get; private set; }
        public Transform Carrier => _carrier;
        public bool Melting => Busy && _meltStart >= 0f;
        /// <summary>forms3 fix pass (review S1): when the body was shown (the stage lets the oldest ring stroke go first).</summary>
        public float Started => _start;
        public PresentBodyStats308 Stats { get; private set; }
        /// <summary>The glow amount written on the last cel tick: 0 from the end of the birth beat on.</summary>
        public float GlowNow { get; private set; }
        public int GlowEndCel => _glowEndCel;
        public float CelSeconds => _cel;
        public int VertexCount => _buffer != null ? _buffer.VertexCount : 0;
        public int Dropped => _buffer != null ? _buffer.Dropped : 0;
        public Renderer BodyRenderer => _renderer;

        /// <summary>meshName: only the edit-mode preview names the mesh (so that its own cleanup finds it).</summary>
        public void Prepare(SpellDeploy308ProfileSO profile, SpellPresent308SheetSO sheet, DeployTier308 tier, string meshName = null)
        {
            _profile = profile; _sheet = sheet;
            if (profile == null || sheet == null) return;
            _cel = profile.CelSeconds(profile.Tier(tier));
            if (_buffer == null) _buffer = new InkBurstBuffer308(sheet.Pool.BodyVerts);
            if (_mesh == null) { _mesh = new Mesh { name = string.IsNullOrEmpty(meshName) ? "InkPresent308" : meshName, hideFlags = HideFlags.DontSave }; _mesh.MarkDynamic(); }
            if (_renderer == null)
            {
                gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _renderer = gameObject.AddComponent<MeshRenderer>();
                _renderer.shadowCastingMode = ShadowCastingMode.Off; _renderer.receiveShadows = false;
                _renderer.lightProbeUsage = LightProbeUsage.Off; _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                _renderer.enabled = false;
            }
            gameObject.layer = Mathf.Clamp(profile.RenderLayer, 0, 31);
            if (_block == null) _block = new MaterialPropertyBlock();
        }

        /// <summary>Compose and show. `root` and `forward` place the body frame (+Z away from the eye; `forward` = from the eye to the
        /// body for a carried mark, the view direction for a body drawn from the eye). holdSeconds below 0 = the
        /// body is carried until Release; otherwise it melts that long after its birth beat. False = nothing to draw.
        /// forms3: `up` (default: the world's) is the body's +Y - the ground's normal under a zone's ring stroke.</summary>
        public bool Show(in PresentBodyInput308 input, int owner, Transform carrier, Vector3 root, Vector3 forward, float holdSeconds, Color tint, float now, Vector3 up = default)
        {
            if (_profile == null || _sheet == null || _buffer == null) return false;
            _buffer.Clear();
            Stats = InkPresentForms308.Compose(input, _buffer);
            if (_buffer.VertexCount == 0 || _profile.BurstMaterial == null) { Hide(); return false; }
            _buffer.Apply(_mesh);
            Owner = owner; Form = input.Body; States = input.States;
            _carrier = carrier; _carried = carrier != null;
            Place(root, forward, _carried, up);
            _start = now; _lastCel = int.MinValue; _meltStart = -1f; _melt = 0f;
            float beat = InkPresentForms308.BodyBeat(_sheet, Stats.LastBirthCel, _cel);
            _glowEndCel = InkPresentForms308.BodyGlowEndCel(_sheet, Stats.LastBirthCel, _cel);
            // a carried body is still bounded: the sheet's ceiling takes it off even when nobody releases it
            _holdEnd = now + beat + (holdSeconds < 0f ? _sheet.MarkSeconds : Mathf.Max(0f, holdSeconds));
            tint.a = 1f;
            float wash = _profile.ElementOf(input.Element).Tint;
            _block.SetColor(TintId, tint);
            _block.SetFloat(TintAmountId, input.Tinted ? _profile.TintMax * wash : 0f);
            _block.SetFloat(SeedId, (input.Seed & 1023) * .013f);
            _block.SetColor(GlowColorId, InkDeployRuntime308.GlowHue(tint, input.Tinted ? wash : 0f));
            _block.SetFloat(GlowCelsId, _profile.GlowCels);
            _block.SetFloat(GlowRimId, Mathf.Clamp01(_profile.Stroke.GlowRim));
            _block.SetFloat(AdvanceId, 1f); _block.SetFloat(RiseId, 1f); _block.SetFloat(DrainId, 0f);
            _renderer.sharedMaterial = _profile.BurstMaterial;
            _renderer.enabled = true;
            Busy = true;
            Sample(now, root - forward);
            return true;
        }

        /// <summary>The body starts to melt (a mark comes off, the handle ended).</summary>
        public void Release(float now)
        {
            if (Busy && _meltStart < 0f) _meltStart = now;
        }

        /// <summary>One frame: follow the carrier, and on a new cel write the property block. `eye` = the camera's position.</summary>
        public void Sample(float now, Vector3 eye)
        {
            if (!Busy) return;
            if (_carried)
            {
                // a carrier that is gone or switched off takes its mark with it
                if (_carrier == null || !_carrier.gameObject.activeInHierarchy) { _carried = false; _carrier = null; Release(now); }
                else transform.position = _carrier.position;
            }
            if (_meltStart < 0f && now >= _holdEnd) _meltStart = now;
            float melt = 0f;
            if (_meltStart >= 0f)
            {
                melt = Mathf.Clamp01(Mathf.Ceil((now - _meltStart) / _cel) * _cel / Mathf.Max(_cel, _profile.Beats.Melt));
                if (melt >= 1f) { Hide(); return; }
            }
            int cel = Mathf.FloorToInt(Mathf.Max(0f, now - _start) / _cel + .0001f);
            if (cel == _lastCel && Mathf.Approximately(melt, _melt)) return;
            _lastCel = cel; _melt = melt;
            if (_carried) Aim(eye);
            GlowNow = InkPresentForms308.BodyGlow(_profile, cel, _glowEndCel, melt);
            if (Form == PresentBody308.Slash || Form == PresentBody308.Blade) GlowNow *= Mathf.Clamp01(_sheet.Sword.GlowShare);   // look2 round 2: a big near body
            if (Form == PresentBody308.ZoneRing) GlowNow *= Mathf.Clamp01(_profile.StandingGlow);   // forms3: a standing form takes the standing share of the birth glow
            _block.SetFloat(CelId, cel);
            _block.SetFloat(MeltId, melt);
            _block.SetFloat(GlowId, GlowNow);
            _renderer.SetPropertyBlock(_block);
        }

        public void Hide()
        {
            Busy = false; GlowNow = 0f; Owner = 0; _carrier = null; _carried = false; _meltStart = -1f;
            if (_renderer != null) _renderer.enabled = false;
        }

        // a carried mark stands level on its carrier; a body drawn from the eye (slash, blade) keeps the view's pitch
        // (forms3: a body laid on the ground is given the ground's normal as `up`; its forward is put into that plane)
        private void Place(Vector3 root, Vector3 forward, bool level, Vector3 up)
        {
            if (up.sqrMagnitude > 1e-6f && !level)
            {
                up.Normalize();
                Vector3 flat = Vector3.ProjectOnPlane(forward, up);
                if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(Vector3.forward, up);
                if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(Vector3.right, up);
                transform.SetPositionAndRotation(root, Quaternion.LookRotation(flat.normalized, up));
                return;
            }
            if (level || Mathf.Abs(Vector3.Dot(forward.normalized, Vector3.up)) > .98f) forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            transform.SetPositionAndRotation(root, Quaternion.LookRotation(forward.normalized, Vector3.up));
        }

        // a carried mark turns about the vertical so that it faces the eye (on cel ticks only)
        private void Aim(Vector3 eye)
        {
            Vector3 away = transform.position - eye; away.y = 0f;
            if (away.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        private void OnDestroy() { ReleaseResources(); }

        /// <summary>Destroys the mesh (also called by edit-mode tools, where OnDestroy does not run for DontSave objects).</summary>
        public void ReleaseResources()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
            _mesh = null;
        }
    }
}
