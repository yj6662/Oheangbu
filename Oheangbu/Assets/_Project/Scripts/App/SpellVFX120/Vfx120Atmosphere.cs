using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.SpellVFX120
{
    // Bounded native-particle material response; no combat, collider, light or damage objects.
    // Renderer materials/meshes are shared. Initialization allocates curves/gradients once;
    // Sample allocates nothing. The owning effect destroys this complete child hierarchy.
    public sealed class Vfx120Atmosphere : MonoBehaviour
    {
        private enum Element { Wood, Fire, Earth, Metal, Water }
        private sealed class Layer
        {
            public ParticleSystem system;
            public float maximumLifetime, rate;
        }
        private Vfx120Profile _profile;
        private Element _element;
        private Layer[] _layers;
        private bool _playing, _preview, _ended;
        private float _life = -1, _flight = -1;
        public int SystemCount => _layers == null ? 0 : _layers.Length;
        public int ParticleBudget { get; private set; }
        public float ConfiguredLife => _life;
        public float EmissionEndsAt { get; private set; }
        public float MaximumParticleLifetime { get; private set; }
        public int LiveParticleCount
        {
            get
            {
                int total = 0;
                if (_layers != null) foreach (var layer in _layers) if (layer.system != null) total += layer.system.particleCount;
                return total;
            }
        }

        public static Vfx120Atmosphere Create(Vfx120Profile profile, Transform parent)
        {
            if (profile == null || parent == null || profile.MistMaterial == null) return null;
            var go = new GameObject("PigmentAtmosphere");
            go.transform.SetParent(parent, false);
            var atmosphere = go.AddComponent<Vfx120Atmosphere>();
            atmosphere._profile = profile;
            atmosphere._element = ElementOf(profile.Glyph);
            atmosphere.Build();
            return atmosphere;
        }

        private void Build()
        {
            // Legacy UseMist now controls only the diffuse veil, not the distinct leaf/spark/drop layer.
            bool veil = _profile.UseMist;
            _layers = new Layer[veil ? 3 : 2];
            int total = Mathf.Clamp(96 + Mathf.Clamp(_profile.Count, 1, 32) * 8, 120, 360);
            if (_element == Element.Metal) total = Mathf.Min(total, 220);
            if (_profile.Behavior == Vfx120Behavior.Reserve) total = Mathf.Min(total, 120);
            int core = Mathf.RoundToInt(total * (veil ? .45f : .65f));
            int secondary = veil ? Mathf.RoundToInt(total * .35f) : 0;
            int debris = total - core - secondary;
            _layers[0] = MakeLayer(0, core);
            if (veil) _layers[1] = MakeLayer(1, secondary);
            _layers[_layers.Length - 1] = MakeLayer(2, debris);
            ParticleBudget = total;
        }

        private Layer MakeLayer(int role, int cap)
        {
            bool mesh = role == 2 && (_element == Element.Wood || _element == Element.Metal)
                && _profile.AccentMesh != null && _profile.InkMaterial != null;
            string name = role == 0 ? "ContinuousMaterial" : role == 1 ? "DiffuseVeil" : "LeafSparkGrainDrop";
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.prewarm = false;
            main.duration = Mathf.Max(.1f, _profile.Duration);
            main.startColor = Color.white; // the lifetime gradient is the sole palette multiplier
            main.maxParticles = cap;
            main.gravityModifier = 0; // local, authored acceleration below; no world Physics query
            main.useUnscaledTime = false;
            main.simulationSpeed = 1;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.stopAction = ParticleSystemStopAction.None;
            main.startRotation3D = mesh;
            main.startRotationZ = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            if (mesh)
            {
                main.startRotationX = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
                main.startRotationY = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            }
            uint glyph = !string.IsNullOrEmpty(_profile.Glyph) ? _profile.Glyph[0] : 1u;
            ps.useAutoRandomSeed = false; ps.randomSeed = glyph * 17u + (uint)(role + 1) * 103u;

            float radius = Mathf.Clamp(_profile.Size * .2f, .07f, .7f);
            bool volume = _profile.Behavior == Vfx120Behavior.Zone || _profile.Behavior == Vfx120Behavior.Wave
                || _profile.Behavior == Vfx120Behavior.Burst || _profile.Behavior == Vfx120Behavior.Shield;
            if (volume) radius *= 1.55f;
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius; shape.radiusThickness = .8f;
            shape.scale = Vector3.one;
            var velocity = ps.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.Local;
            // Unity requires the three linear velocity axes to use one curve mode.
            // Unspecified axes still need TwoConstants, even when both ends are zero.
            velocity.x = new ParticleSystem.MinMaxCurve(0, 0);
            velocity.y = new ParticleSystem.MinMaxCurve(0, 0);
            velocity.z = new ParticleSystem.MinMaxCurve(0, 0);
            var force = ps.forceOverLifetime; force.enabled = true; force.space = ParticleSystemSimulationSpace.Local;
            var rotation = ps.rotationOverLifetime; rotation.enabled = true; rotation.separateAxes = mesh;
            rotation.z = new ParticleSystem.MinMaxCurve(-.7f, .7f);
            if (mesh)
            {
                rotation.x = new ParticleSystem.MinMaxCurve(-1.1f, 1.1f);
                rotation.y = new ParticleSystem.MinMaxCurve(-.8f, .8f);
            }

            float particleLife = .7f, alpha = .3f;
            float minimumSize = .04f, maximumSize = .12f;
            Color first = _profile.Accent, middle = _profile.Pigment, last = _profile.Ink;
            switch (_element)
            {
                case Element.Wood:
                    particleLife = role == 2 ? 1.15f : .8f;
                    minimumSize = role == 2 ? .08f : .025f;
                    maximumSize = role == 1 ? .3f : role == 2 ? .2f : .075f;
                    main.startSpeed = new ParticleSystem.MinMaxCurve(.04f, .2f);
                    velocity.x = new ParticleSystem.MinMaxCurve(-.16f, .16f);
                    velocity.y = new ParticleSystem.MinMaxCurve(.05f, .22f);
                    force.y = -.18f;
                    alpha = role == 1 ? .12f : role == 2 ? .65f : .38f;
                    // Small leaf flutter is rotation, not a mesh vertex mutation.
                    if (mesh) { rotation.x = new ParticleSystem.MinMaxCurve(-2.1f, 2.1f); rotation.z = new ParticleSystem.MinMaxCurve(-1.4f, 1.4f); }
                    break;
                case Element.Fire:
                    particleLife = role == 1 ? 1.1f : role == 2 ? .48f : .55f;
                    minimumSize = role == 2 ? .012f : role == 1 ? .16f : .1f;
                    maximumSize = role == 2 ? .035f : role == 1 ? .4f : .23f;
                    shape.scale = new Vector3(.65f, .85f, .65f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(.03f, role == 2 ? .45f : .15f);
                    velocity.y = new ParticleSystem.MinMaxCurve(role == 2 ? .85f : .35f, role == 2 ? 1.7f : .95f);
                    velocity.x = new ParticleSystem.MinMaxCurve(-.13f, .13f);
                    force.y = role == 2 ? -.75f : .28f;
                    alpha = role == 1 ? .16f : role == 2 ? .75f : .34f;
                    if (role == 1) { first = _profile.Pigment; middle = _profile.Ink; }
                    else first = Color.Lerp(_profile.Accent, new Color(.8f, .65f, .36f, 1), .2f);
                    if (role == 0)
                    {
                        main.startSize3D = true;
                        main.startSizeX = new ParticleSystem.MinMaxCurve(minimumSize, maximumSize);
                        main.startSizeY = new ParticleSystem.MinMaxCurve(minimumSize * 1.8f, maximumSize * 2.1f);
                        main.startSizeZ = new ParticleSystem.MinMaxCurve(minimumSize, maximumSize);
                    }
                    break;
                case Element.Earth:
                    particleLife = role == 1 ? .95f : role == 2 ? .6f : .72f;
                    minimumSize = role == 2 ? .012f : .09f;
                    maximumSize = role == 2 ? .04f : role == 1 ? .38f : .24f;
                    shape.scale = new Vector3(1.3f, .12f, 1.3f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(.08f, role == 2 ? .65f : .24f);
                    velocity.y = new ParticleSystem.MinMaxCurve(.02f, role == 2 ? .35f : .12f);
                    velocity.x = new ParticleSystem.MinMaxCurve(-.25f, .25f);
                    velocity.z = new ParticleSystem.MinMaxCurve(-.2f, .2f);
                    force.y = role == 2 ? -1.5f : -.38f;
                    alpha = role == 1 ? .16f : role == 2 ? .6f : .24f;
                    break;
                case Element.Metal:
                    particleLife = role == 0 ? .16f : role == 2 ? .48f : .35f;
                    minimumSize = role == 2 ? .025f : .025f;
                    maximumSize = role == 1 ? .18f : role == 2 ? .095f : .1f;
                    shape.radius = radius * .6f;
                    main.startSpeed = new ParticleSystem.MinMaxCurve(role == 2 ? .4f : .02f, role == 2 ? 1.2f : .22f);
                    velocity.y = new ParticleSystem.MinMaxCurve(.02f, .3f);
                    force.y = role == 2 ? -2.1f : -.5f;
                    alpha = role == 1 ? .13f : role == 2 ? .8f : .68f;
                    first = Color.Lerp(_profile.Accent, new Color(.75f, .73f, .66f, 1), .25f);
                    if (mesh) { rotation.x = new ParticleSystem.MinMaxCurve(-4.5f, 4.5f); rotation.y = new ParticleSystem.MinMaxCurve(-3.5f, 3.5f); }
                    break;
                default:
                    particleLife = role == 1 ? .75f : role == 2 ? .5f : .65f;
                    minimumSize = role == 2 ? .018f : .07f;
                    maximumSize = role == 2 ? .055f : role == 1 ? .3f : .18f;
                    shape.scale = new Vector3(1.15f, .2f, 1.15f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(.08f, role == 2 ? .6f : .3f);
                    velocity.y = new ParticleSystem.MinMaxCurve(.12f, role == 2 ? .6f : .32f);
                    velocity.x = new ParticleSystem.MinMaxCurve(-.22f, .22f);
                    force.y = role == 2 ? -2.3f : -1.1f;
                    alpha = role == 1 ? .15f : role == 2 ? .62f : .28f;
                    first = Color.Lerp(_profile.Accent, new Color(.7f, .75f, .7f, 1), .18f);
                    break;
            }
            if (_profile.Family == "Cloud")
            {
                particleLife = role == 1 ? 1.5f : 1.1f;
                minimumSize = .35f; maximumSize = role == 1 ? .9f : .6f;
                shape.radius = Mathf.Clamp(_profile.Size * .65f, .5f, 2.1f);
                shape.scale = new Vector3(1, .5f, 1);
                main.startSize3D = false;
                velocity.x = new ParticleSystem.MinMaxCurve(-.10f,.10f);
                velocity.y = new ParticleSystem.MinMaxCurve(.12f,.35f);
                velocity.z = new ParticleSystem.MinMaxCurve(-.06f,.06f);
                force.y = .03f;
                first = _profile.Accent; middle = _profile.Pigment; last = _profile.Pigment;
                alpha = role == 1 ? .2f : .28f;
            }
            if (!main.startSize3D) main.startSize = new ParticleSystem.MinMaxCurve(minimumSize, maximumSize);
            main.startLifetime = new ParticleSystem.MinMaxCurve(particleLife * .55f, particleLife);
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, role == 1
                ? new AnimationCurve(new Keyframe(0, .35f), new Keyframe(.4f, .9f), new Keyframe(1, 1.35f))
                : new AnimationCurve(new Keyframe(0, .25f), new Keyframe(.15f, 1), new Keyframe(.65f, .8f), new Keyframe(1, .1f)));
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(first, 0), new GradientColorKey(middle, .4f), new GradientColorKey(last, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(alpha, .1f), new GradientAlphaKey(alpha * .7f, .62f), new GradientAlphaKey(0, 1) });
            var color = ps.colorOverLifetime; color.enabled = true; color.color = gradient;
            var emission = ps.emission; emission.enabled = true; emission.rateOverTime = 0; emission.rateOverDistance = 0;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = mesh ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
            if (mesh) renderer.mesh = _profile.AccentMesh;
            renderer.sharedMaterial = mesh ? _profile.InkMaterial : _profile.MistMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", Color.white); block.SetFloat("_Alpha", 1); block.SetFloat("_Erode", 0);
            renderer.SetPropertyBlock(block); // vertex gradient is not multiplied by a second palette tint
            float rate = cap / Mathf.Max(.12f, particleLife) * (role == 2 ? .35f : .72f);
            if (_profile.Behavior == Vfx120Behavior.Reserve) rate *= .4f;
            return new Layer { system = ps, maximumLifetime = particleLife, rate = rate };
        }

        public void Sample(float age, Vector3 center, float flight, float life, bool preview)
        {
            if (_layers == null) return;
            life = Mathf.Max(.02f, life); flight = Mathf.Max(0, flight); age = Mathf.Max(0, age);
            transform.localPosition = center + (_profile.Family == "Cloud" ? Vector3.up * .7f : Vector3.zero);
            if (_life != life || _flight != flight) ConfigureClock(life, flight);
            if (age >= life)
            {
                if (!_ended) StopAndClear();
                _ended = true; _playing = false;
                return;
            }
            _ended = false;
            if (preview)
            {
                _preview = true; _playing = false;
                foreach (var layer in _layers) layer.system.Simulate(age, true, true, false);
                return;
            }
            // Edit-mode numerical audits can sample transforms without simulating particles.
            if (!Application.isPlaying) return;
            if (_preview) { StopAndClear(); _preview = false; }
            if (!_playing)
            {
                foreach (var layer in _layers) layer.system.Play(true);
                _playing = true;
            }
            if (age >= EmissionEndsAt)
                foreach (var layer in _layers)
                    if (layer.system.isEmitting) layer.system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        private void ConfigureClock(float life, float flight)
        {
            StopAndClear(); _playing = false; _ended = false;
            _life = life; _flight = flight;
            MaximumParticleLifetime = 0;
            foreach (var layer in _layers)
                MaximumParticleLifetime = Mathf.Max(MaximumParticleLifetime, Mathf.Min(layer.maximumLifetime, life * .35f));
            // Every final emission expires before root Life; Sample(Life) also clears exactly.
            EmissionEndsAt = Mathf.Max(.001f, life - MaximumParticleLifetime - Mathf.Min(.025f, life * .05f));
            bool onArrival = _profile.Behavior == Vfx120Behavior.Burst || _profile.Behavior == Vfx120Behavior.Bind
                || _profile.Behavior == Vfx120Behavior.Zone || _profile.Behavior == Vfx120Behavior.Wave;
            float begin = onArrival ? Mathf.Min(flight, EmissionEndsAt * .65f) : 0;
            foreach (var layer in _layers)
            {
                var main = layer.system.main; main.duration = life;
                float maximum = Mathf.Min(layer.maximumLifetime, life * .35f);
                main.startLifetime = new ParticleSystem.MinMaxCurve(maximum * .55f, maximum);
                float t0 = begin / life;
                float t1 = Mathf.Lerp(begin, EmissionEndsAt, .12f) / life;
                float t2 = Mathf.Lerp(begin, EmissionEndsAt, .72f) / life;
                float t3 = EmissionEndsAt / life;
                var curve = new AnimationCurve(new Keyframe(t0, 0), new Keyframe(t1, 1), new Keyframe(t2, .8f), new Keyframe(t3, 0), new Keyframe(1, 0));
                var emission = layer.system.emission; emission.rateOverTime = new ParticleSystem.MinMaxCurve(layer.rate, curve);
            }
        }

        private void StopAndClear()
        {
            if (_layers == null) return;
            foreach (var layer in _layers)
                if (layer != null && layer.system != null) layer.system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        private void OnDisable() { StopAndClear(); _playing = false; }
        private void OnDestroy() { StopAndClear(); } // native buffers are owned by these child GameObjects
        private static Element ElementOf(string glyph)
        {
            if (string.IsNullOrEmpty(glyph)) return Element.Water;
            int initial = (glyph[0] - 0xAC00) / 588;
            switch (initial) { case 0: return Element.Wood; case 2: return Element.Fire; case 6: return Element.Earth; case 9: return Element.Metal; default: return Element.Water; }
        }
    }
}
