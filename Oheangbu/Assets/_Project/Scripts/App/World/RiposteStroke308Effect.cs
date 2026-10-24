using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Oheangbu.App.World
{
    // D308-148 (앞잡 = 일필휘지 한 획, step 5): the presentation of the one stroke. Owned by Riposte301 (no singleton, no static
    // state). Everything is made ONCE in Warm(): the stroke meshes of the pool, ONE flake ParticleSystem, and ONE transient global post
    // volume of the riposte's own. After that a fire (Begin) and a frame (Tick) allocate nothing: paths, meshes, property blocks,
    // EmitParams and the hash scatter are all reused.
    //
    //  * the stroke = RiposteStroke308Mesh on the plane of RiposteStroke308Path, revealed by the shader's _RevealS in brush order,
    //    dried by its crack threshold _Dry (a cutout through a crack field - never a fade);
    //  * the hit = the frame the revealed head reaches the torso mark (RiposteStroke308Impact, level triggered): Tick raises
    //    Slot.ImpactReady in that frame and the owner resolves damage, reaction, specks and the hit stop in the same Update;
    //  * dry flakes = Emit() along the centre line, from the tail toward the head, flat matte pieces that only fall;
    //  * the colour drain = a volume made here (HideAndDontSave, global, above LookDriver308's priority): ColorAdjustments saturation
    //    down and ShadowsMidtonesHighlights highlights toward the paper hue at unit luminance. Exposure is never overridden, and
    //    LookDriver308 / LookProfile308 / _OhLook308* are not touched. Its weight is the clock's LookWeight; it is switched off at the
    //    end, on cancel and on dispose, and only this volume is ever written.
    public sealed class RiposteStroke308Effect
    {
        public sealed class Slot
        {
            internal RiposteStroke308Mesh Mesh;
            public readonly RiposteStroke308Path Path = new RiposteStroke308Path();
            public readonly RiposteStroke308Impact Impact = new RiposteStroke308Impact();
            public bool Live, ImpactReady, HasImpact;
            public float Age, ImpactT, PredictedImpactT, RevealU, DryNow;
            public uint Seed;
            public int Order, FlakesEmitted;
            public Vector3 ImpactPoint;
            internal Collider Col;
            internal Transform Root;
            internal Vector3 RootAtFire, TorsoAtFire;
            /// <summary>The pierce time: the real one once it happened, the predicted one before.</summary>
            public float ImpactTime => HasImpact ? ImpactT : PredictedImpactT;
            public RiposteStroke308Mesh StrokeMesh => Mesh;
        }

        static readonly Color32 SpeckInk = new Color32(36, 33, 31, 255);

        RiposteStroke308Profile _profile;
        RiposteStroke308Shape _shape;
        RiposteStroke308Timing _timing;
        Slot[] _slots = new Slot[0];
        Transform _root;
        int _order;
        uint _fires;
        // flakes
        GameObject _psObject;
        ParticleSystem _flakes;
        ParticleSystem.EmitParams _emit;
        // the colour drain
        GameObject _volumeObject;
        Volume _volume;
        VolumeProfile _volumeProfile;
        ColorAdjustments _colour;
        ShadowsMidtonesHighlights _smh;
        float _look;

        public bool IsReady { get; private set; }
        public int Capacity => _slots.Length;
        public Slot SlotAt(int i) => _slots[i];
        public float LookWeight => _look;
        public Volume LookVolume => _volume;
        public ColorAdjustments LookColour => _colour;
        public ShadowsMidtonesHighlights LookHighlights => _smh;
        public ParticleSystem Flakes => _flakes;
        public Transform Root => _root;
        public bool AnyLive { get { foreach (var s in _slots) if (s.Live) return true; return false; } }
        public int LiveCount { get { int n = 0; foreach (var s in _slots) if (s.Live) n++; return n; } }

        /// <summary>Makes everything the effect will ever need. Once per scene; false when the profile has no materials yet.</summary>
        public bool Warm(Transform parent, RiposteStroke308Profile profile, int volumeLayer)
        {
            if (IsReady) return true;
            if (profile == null || !profile.Ready) return false;
            _profile = profile; _shape = profile.Shape(); _timing = profile.Timing();
            // a scene-root object of the owner's scene: the stroke is a WORLD thing, a walking caster (the owner's transform) must not carry it
            var rootObject = new GameObject("RiposteStroke308") { hideFlags = HideFlags.DontSave, layer = profile.Layer };
            _root = rootObject.transform;
            if (parent != null && parent.gameObject.scene.IsValid()) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rootObject, parent.gameObject.scene);
            int n = Mathf.Clamp(profile.PoolSize, 1, 4);
            _slots = new Slot[n];
            for (int i = 0; i < n; i++)
                _slots[i] = new Slot { Mesh = new RiposteStroke308Mesh("RiposteStroke308_" + i, _root, profile.StrokeMaterial, profile.Layer) };
            BuildFlakes(profile);
            BuildVolume(profile, volumeLayer);
            IsReady = true;
            return true;
        }

        // ---------------------------------------------------------------- fire

        /// <summary>Starts a stroke on a free slot (the oldest one when none is free: its flakes are already in the particle pool).</summary>
        public Slot Begin(in RiposteStroke308Aim aim, Transform root, Collider col)
        {
            if (!IsReady) return null;
            Slot s = null, oldest = null;
            foreach (var c in _slots) { if (!c.Live) { s = c; break; } if (oldest == null || c.Order < oldest.Order) oldest = c; }
            if (s == null) { Cancel(oldest); s = oldest; }
            _fires++;
            s.Seed = _fires * 2654435761u + 12345u;
            s.Live = true; s.ImpactReady = false; s.HasImpact = false; s.Age = 0f; s.ImpactT = 0f; s.RevealU = 0f; s.DryNow = 0f;
            s.FlakesEmitted = 0; s.Order = ++_order; s.Impact.Reset();
            s.Root = root; s.Col = col; s.RootAtFire = root != null ? root.position : aim.Torso; s.TorsoAtFire = aim.Torso;
            s.Path.Build(_shape, aim);
            s.PredictedImpactT = RiposteStroke308Timeline.TimeAtReveal(_timing, s.Path.ImpactU);
            s.ImpactPoint = s.Path.MidWorld;
            s.Mesh.Place(s.Path); s.Mesh.Rebuild(s.Path);
            s.Mesh.SetState(0f, 0f, Seed01(s));
            s.Mesh.Show(false);
            return s;
        }

        public void Cancel(Slot s)
        {
            if (s == null) return;
            s.Live = false; s.ImpactReady = false;
            if (s.Mesh != null) s.Mesh.Hide();
            s.Root = null; s.Col = null;
        }

        public void CancelAll()
        {
            foreach (var s in _slots) Cancel(s);
            ApplyLook(0f);
        }

        // ---------------------------------------------------------------- frame

        /// <summary>One frame, dt = scaled seconds (the hit stop slows it like the fight). A hit sets Slot.ImpactReady in this very call.</summary>
        public void Tick(float dt)
        {
            if (!IsReady) return;
            float look = 0f;
            for (int i = 0; i < _slots.Length; i++)
            {
                var s = _slots[i];
                if (!s.Live) continue;
                s.Age += Mathf.Max(0f, dt);
                float u = RiposteStroke308Timeline.Reveal(_timing, s.Age);
                if (!s.Impact.Fired)
                {
                    // the head has not reached the body: the unwritten part follows the target, the written part stays in the world
                    s.Path.Retarget(TorsoNow(s), s.RevealU);
                    if (s.Impact.Advance(u, s.Path.ImpactU))
                    {
                        s.Path.Freeze();
                        s.HasImpact = true; s.ImpactT = s.Age; s.ImpactReady = true; s.ImpactPoint = s.Path.MidWorld;
                    }
                    s.Mesh.Rebuild(s.Path);   // the pierce frame included: the mesh that is on screen is the one that was hit
                }
                s.RevealU = u;
                float dry = RiposteStroke308Timeline.Dry(_timing, s.Age);
                s.DryNow = dry;
                s.Mesh.SetState(u, dry, Seed01(s));
                s.Mesh.Show(u > .001f && dry < .999f);
                EmitFlakes(s);
                look = Mathf.Max(look, RiposteStroke308Timeline.LookWeight(_timing, s.Age, s.ImpactTime));
                // the stroke is over when it has dried away; a hit not collected yet keeps the slot alive for the owner
                if (s.Age >= _timing.FallEnd && s.Impact.Fired && !s.ImpactReady) { s.Live = false; s.Mesh.Hide(); s.Root = null; s.Col = null; }
            }
            ApplyLook(look);
        }

        Vector3 TorsoNow(Slot s)
        {
            if (s.Col != null && s.Col.enabled && s.Col.gameObject.activeInHierarchy) return s.Col.bounds.center;
            if (s.Root != null) return s.TorsoAtFire + (s.Root.position - s.RootAtFire);
            return s.TorsoAtFire;
        }

        static float Seed01(Slot s) => (s.Seed & 0xFFFFu) / 65536f * 17f;

        // ---------------------------------------------------------------- flakes and specks

        void BuildFlakes(RiposteStroke308Profile profile)
        {
            _psObject = new GameObject("RiposteFlakes308") { hideFlags = HideFlags.DontSave, layer = profile.Layer };
            _psObject.transform.SetParent(_root, false);
            _flakes = _psObject.AddComponent<ParticleSystem>();
            _flakes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _flakes.main;
            main.loop = true; main.playOnAwake = false; main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Clamp(profile.MaxParticles, 16, 256);
            main.gravityModifier = profile.FlakeGravity;
            main.startLifetime = 1f; main.startSpeed = 0f; main.startSize = .15f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.stopAction = ParticleSystemStopAction.None;
            var emission = _flakes.emission; emission.enabled = false;
            var shape = _flakes.shape; shape.enabled = false;
            var sheet = _flakes.textureSheetAnimation;
            sheet.enabled = true; sheet.mode = ParticleSystemAnimationMode.Grid; sheet.numTilesX = 4; sheet.numTilesY = 2;
            sheet.animation = ParticleSystemAnimationType.WholeSheet; sheet.cycleCount = 1;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);      // a random cell of the sheet per piece
            var spin = _flakes.rotationOverLifetime; spin.enabled = true; spin.z = new ParticleSystem.MinMaxCurve(-1.4f, 1.4f);   // a little, rad/s
            // the pieces erode away through their field at the end of their life (the shader reads the vertex alpha as a threshold)
            var colour = _flakes.colorOverLifetime; colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, .6f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);
            var renderer = _psObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = profile.FlakeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _emit = new ParticleSystem.EmitParams();
            _flakes.Play();
        }

        void EmitFlakes(Slot s)
        {
            int total = _profile.FlakeCount;
            if (total <= 0 || s.FlakesEmitted >= total) return;
            float from = _timing.DryStart + .03f, to = _timing.CrackEnd + .12f;
            int want = Mathf.Min(total, (int)(Mathf.Clamp01((s.Age - from) / Mathf.Max(1e-3f, to - from)) * total + 1e-4f));
            while (s.FlakesEmitted < want) { EmitFlake(s, s.FlakesEmitted, total); s.FlakesEmitted++; }
        }

        // one dry piece: leaves a point of the centre line (the tail first), only falls
        void EmitFlake(Slot s, int k, int total)
        {
            var p = _profile; var path = s.Path;
            float u = Mathf.Clamp(Mathf.Lerp(.96f, .12f, (k + RiposteStroke308Timeline.Hash01(s.Seed, k * 7 + 1) * .8f) / total), .05f, .98f);
            Vector3 tangent = path.TangentAt(u), across = new Vector3(-tangent.y, tangent.x, 0f);
            Vector3 local = path.LocalAt(u) + across * ((RiposteStroke308Timeline.Hash01(s.Seed, k * 7 + 2) - .5f) * path.WidthAt(u) * .8f);
            float r3 = RiposteStroke308Timeline.Hash01(s.Seed, k * 7 + 3), r4 = RiposteStroke308Timeline.Hash01(s.Seed, k * 7 + 4);
            float r5 = RiposteStroke308Timeline.Hash01(s.Seed, k * 7 + 5), r6 = RiposteStroke308Timeline.Hash01(s.Seed, k * 7 + 6);
            _emit.position = path.ToWorld(local);
            _emit.velocity = Vector3.down * (p.FlakeDrop * (.6f + .8f * r3)) + path.H * ((r4 - .5f) * p.FlakeSpread) + path.N * ((r5 - .5f) * p.FlakeSpread * .6f);
            _emit.startSize = Mathf.Lerp(p.FlakeSize.x, p.FlakeSize.y, r6);
            _emit.startLifetime = Mathf.Lerp(p.FlakeLife.x, p.FlakeLife.y, RiposteStroke308Timeline.Hash01(s.Seed, k * 7 + 7));
            _emit.rotation = RiposteStroke308Timeline.Hash01(s.Seed, k * 7 + 8) * 360f;
            _emit.startColor = (Color32)p.FlakeInk;
            _flakes.Emit(_emit, 1);
        }

        /// <summary>The specks thrown at the pierce: dry points and blots out of the torso mark, mostly along the plane. Called once per hit.</summary>
        public void Splash(Slot s)
        {
            if (!IsReady || s == null) return;
            var p = _profile; var path = s.Path;
            for (int k = 0; k < p.SplashCount; k++)
            {
                float a = RiposteStroke308Timeline.Hash01(s.Seed, 1000 + k * 5) * 6.2831853f;
                float speed = p.SplashSpeed * (.35f + .9f * RiposteStroke308Timeline.Hash01(s.Seed, 1001 + k * 5));
                Vector3 dir = path.H * Mathf.Cos(a) + path.V * Mathf.Sin(a);
                Vector3 velocity = dir * speed + path.N * ((RiposteStroke308Timeline.Hash01(s.Seed, 1002 + k * 5) - .5f) * speed * .4f);
                velocity.y = Mathf.Min(velocity.y, speed * .4f);   // sprayed ink does not float up
                _emit.position = s.ImpactPoint + dir * .08f;
                _emit.velocity = velocity;
                _emit.startSize = Mathf.Lerp(p.SplashSize.x, p.SplashSize.y, RiposteStroke308Timeline.Hash01(s.Seed, 1003 + k * 5));
                _emit.startLifetime = Mathf.Lerp(p.SplashLife.x, p.SplashLife.y, RiposteStroke308Timeline.Hash01(s.Seed, 1004 + k * 5));
                _emit.rotation = RiposteStroke308Timeline.Hash01(s.Seed, 1005 + k * 5) * 360f;
                _emit.startColor = SpeckInk;
                _flakes.Emit(_emit, 1);
            }
        }

        // ---------------------------------------------------------------- the colour drain

        void BuildVolume(RiposteStroke308Profile profile, int layer)
        {
            _volumeObject = new GameObject("Riposte308 look (transient)") { hideFlags = HideFlags.HideAndDontSave, layer = layer };
            _volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _volumeProfile.hideFlags = HideFlags.HideAndDontSave;
            _colour = _volumeProfile.Add<ColorAdjustments>(false);
            _smh = _volumeProfile.Add<ShadowsMidtonesHighlights>(false);
            // saturation only (exposure, contrast and the rest are left to the stack below)
            _colour.saturation.Override(-Mathf.Clamp(profile.Desaturate, 0f, 100f));
            // the highlights lean toward the paper hue at unit luminance: the value of the picture is kept, so it cannot read as a flash
            Color paper = profile.PaperColor.linear;
            float luma = Mathf.Max(.02f, .2126f * paper.r + .7152f * paper.g + .0722f * paper.b), k = Mathf.Clamp01(profile.PaperAmount);
            _smh.highlights.Override(new Vector4(Mathf.Lerp(1f, paper.r / luma, k), Mathf.Lerp(1f, paper.g / luma, k), Mathf.Lerp(1f, paper.b / luma, k), 0f));
            _volume = _volumeObject.AddComponent<Volume>();
            _volume.isGlobal = true; _volume.priority = profile.VolumePriority; _volume.sharedProfile = _volumeProfile;
            _volume.weight = 0f; _volume.enabled = false;
        }

        void ApplyLook(float weight)
        {
            weight = Mathf.Clamp01(weight);
            _look = weight;
            if (_volume == null) return;
            bool on = weight > .0005f;
            if (_volume.enabled != on) _volume.enabled = on;
            _volume.weight = on ? weight : 0f;
        }

        // ---------------------------------------------------------------- end

        /// <summary>Destroys everything the effect made. immediate = edit mode (the checks).</summary>
        public void Dispose(bool immediate = false)
        {
            foreach (var s in _slots) { if (s.Mesh != null) { if (immediate) s.Mesh.DisposeImmediate(); else s.Mesh.Dispose(); s.Mesh = null; } s.Live = false; }
            ApplyLook(0f);
            if (_volumeObject != null) Kill(_volumeObject, immediate);
            if (_volumeProfile != null)
            {
                // the components are sub-objects of the transient profile
                foreach (var c in _volumeProfile.components) if (c != null) Kill(c, immediate);
                Kill(_volumeProfile, immediate);
            }
            if (_psObject != null) Kill(_psObject, immediate);
            if (_root != null) Kill(_root.gameObject, immediate);
            _volumeObject = null; _volume = null; _volumeProfile = null; _colour = null; _smh = null; _psObject = null; _flakes = null; _root = null;
            IsReady = false;
        }

        static void Kill(Object o, bool immediate) { if (immediate) Object.DestroyImmediate(o); else Object.Destroy(o); }
    }
}
