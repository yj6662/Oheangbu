using System.Collections.Generic;
using Oheangbu.App;
using UnityEngine;
using UnityEngine.VFX;

namespace Oheangbu.App.SpellFx
{
    /// <summary>D308-153 variant 우/G (VFX Graph): the painted-sprite wave ring. It owns several child <see cref="VisualEffect"/> emitters, one per painted element
    /// (mound, crest, ink stroke, foam claw, splash; two more at the struck body). All of them run ONE graph (a copy of VFX_V308_na_ring edited by SpellFxSetupG: the arc-circle
    /// emitter sits on the cast origin with a 2.2 m radius, camera-facing quads) and differ only by the exposed properties set here (texture, lifetime, rise, fall, size curve,
    /// tint, angle) and by when their Spawn Rate is open (a short window = one strike). The look only: nothing here is read by a rule.
    /// The children are made at the begin, in code, because the editor assembly that builds the prefab cannot reference the VFX runtime.
    /// Every exposed name is tried and a missing one is warned about once (the graph is a text-edited copy; it could not be opened offline).</summary>
    public sealed class SpellFxVfxWavePart : MonoBehaviour, ISpellFxPart
    {
        enum Kind { Mound, Crest, Ink, Claw, Splash, HitCrest, HitSplash }

        /// <summary>Everything one emitter needs: spawn window (seconds from the launch / from the hit), rate, life, motion, size keys (start, middle, end metres), tint.</summary>
        struct Recipe
        {
            public float T0, T1, Rate, Life, Div, Drag, S0, S1, S2;
            public Vector3 Vel, Force;
            public Vector2 Angle;
            public Color Tint;
        }

        sealed class Layer
        {
            public Kind Kind;
            public VisualEffect Fx;
            public Recipe R;
            public bool Hit, On;
            public float Origin = 0f, LastRate = -1f;
        }

        [SerializeField] VisualEffectAsset ringGraph, hitGraph;
        [SerializeField] Texture2D moundTex, crestTex, inkTex, clawTex, splashTex;
        [Tooltip("The arc-circle radius baked into the ring graph copy (SpellFxSetupG writes the same number into the .vfx).")]
        [SerializeField] float bakedRadius = 2.2f;

        readonly List<Layer> _layers = new List<Layer>(7);
        readonly HashSet<string> _warned = new HashSet<string>();
        float _age;
        bool _built, _launched;

        // tint multiplied onto the sprite: white = the painted sprite as it is
        static readonly Color Paint = Color.white, Ink = new Color(0.03f, 0.03f, 0.04f, 1f), Foam = new Color(0.98f, 0.96f, 0.9f, 1f);

        static Recipe Of(Kind kind)
        {
            switch (kind)
            {
                case Kind.Mound:    // the body of the wall: many mounds rise round the ring, swell, sink
                    return new Recipe { T0 = 0f, T1 = 0.28f, Rate = 90f, Life = 1.6f, Vel = new Vector3(0f, 1.4f, 0f), Div = 0.15f, Force = new Vector3(0f, -0.9f, 0f), Drag = 0.8f, Angle = new Vector2(-6f, 6f), S0 = 0.9f, S1 = 1.7f, S2 = 1.3f, Tint = Paint };
                case Kind.Crest:    // hooked crests stand higher, a little later
                    return new Recipe { T0 = 0.12f, T1 = 0.40f, Rate = 30f, Life = 1.5f, Vel = new Vector3(0f, 2.2f, 0f), Div = 0.1f, Force = new Vector3(0f, -1.8f, 0f), Drag = 0.4f, Angle = new Vector2(-12f, 12f), S0 = 1.1f, S1 = 2.0f, S2 = 1.6f, Tint = Paint };
                case Kind.Ink:      // black strokes sweeping along the walls
                    return new Recipe { T0 = 0.06f, T1 = 0.34f, Rate = 28f, Life = 1.1f, Vel = new Vector3(0f, 1.8f, 0f), Div = 0.3f, Force = new Vector3(0f, -1f, 0f), Drag = 0.5f, Angle = new Vector2(-50f, 50f), S0 = 1.2f, S1 = 2.6f, S2 = 2.2f, Tint = Ink };
                case Kind.Claw:     // foam claws thrown off the crests
                    return new Recipe { T0 = 0.28f, T1 = 0.70f, Rate = 40f, Life = 0.9f, Vel = new Vector3(0f, 2.8f, 0f), Div = 0.5f, Force = new Vector3(0f, -4f, 0f), Drag = 0.2f, Angle = new Vector2(-180f, 180f), S0 = 0.35f, S1 = 0.6f, S2 = 0.5f, Tint = Foam };
                case Kind.Splash:   // fine spray
                    return new Recipe { T0 = 0.20f, T1 = 0.80f, Rate = 70f, Life = 0.7f, Vel = new Vector3(0f, 3.2f, 0f), Div = 0.8f, Force = new Vector3(0f, -6f, 0f), Drag = 0.1f, Angle = new Vector2(-180f, 180f), S0 = 0.12f, S1 = 0.3f, S2 = 0.2f, Tint = Foam };
                case Kind.HitCrest: // the big wave breaking over the struck body
                    return new Recipe { T0 = 0f, T1 = 0.18f, Rate = 25f, Life = 1.0f, Vel = new Vector3(0f, 1.6f, 0f), Div = 0.2f, Force = new Vector3(0f, -3f, 0f), Drag = 0.3f, Angle = new Vector2(-10f, 10f), S0 = 1.4f, S1 = 2.4f, S2 = 2.0f, Tint = Paint };
                default:            // HitSplash: the burst of drops on contact
                    return new Recipe { T0 = 0f, T1 = 0.25f, Rate = 150f, Life = 0.7f, Vel = new Vector3(0f, 3.5f, 0f), Div = 0.9f, Force = new Vector3(0f, -7f, 0f), Drag = 0.1f, Angle = new Vector2(-180f, 180f), S0 = 0.15f, S1 = 0.4f, S2 = 0.3f, Tint = Foam };
            }
        }

        Texture2D TexOf(Kind kind)
        {
            switch (kind)
            {
                case Kind.Mound: return moundTex;
                case Kind.Crest: case Kind.HitCrest: return crestTex;
                case Kind.Ink: return inkTex;
                case Kind.Claw: return clawTex;
                default: return splashTex;
            }
        }

        void Build(SpellFxCast cast)
        {
            if (_built) return;
            _built = true;
            float s = cast != null && cast.Radius > 0.1f ? Mathf.Clamp(cast.Radius / bakedRadius, 0.5f, 2f) : 1f;
            foreach (Kind kind in System.Enum.GetValues(typeof(Kind)))
            {
                bool hit = kind == Kind.HitCrest || kind == Kind.HitSplash;
                var graph = hit ? hitGraph : ringGraph;
                var tex = TexOf(kind);
                if (graph == null || tex == null) { Warn("no graph / texture for " + kind); continue; }
                var go = new GameObject("VFX_" + kind);
                go.transform.SetParent(transform, false);
                if (cast != null) go.transform.SetPositionAndRotation(cast.Origin, Quaternion.identity);
                if (!hit) go.transform.localScale = Vector3.one * s;
                int layerId = VfxAfterFog308.Layer;
                if (layerId >= 0) go.layer = layerId;          // after the realm fog: the particles write no depth, the fog of what is behind them would paint over them
                var fx = go.AddComponent<VisualEffect>();
                fx.visualEffectAsset = graph;
                fx.enabled = false;                            // nothing shows before the launch (the same hold as SpellFxVfxGraphPart)
                _layers.Add(new Layer { Kind = kind, Fx = fx, R = Of(kind), Hit = hit });
            }
            VfxAfterFog308.Route(transform);
        }

        void Warn(string text)
        {
            if (_warned.Add(text)) Debug.LogWarning("[SpellFxVfxWavePart] " + text, this);
        }

        // ---- exposed-property helpers: set when the graph exposes the name, else warn once ----
        void F(VisualEffect fx, string name, float v) { if (fx.HasFloat(name)) fx.SetFloat(name, v); else Warn("float '" + name + "' not exposed"); }
        void B(VisualEffect fx, string name, bool v) { if (fx.HasBool(name)) fx.SetBool(name, v); else Warn("bool '" + name + "' not exposed"); }
        void V2(VisualEffect fx, string name, Vector2 v) { if (fx.HasVector2(name)) fx.SetVector2(name, v); else Warn("vector2 '" + name + "' not exposed"); }
        void T(VisualEffect fx, string name, Texture t) { if (fx.HasTexture(name)) fx.SetTexture(name, t); else Warn("texture '" + name + "' not exposed"); }
        void Curve(VisualEffect fx, string name, AnimationCurve c) { if (fx.HasAnimationCurve(name)) fx.SetAnimationCurve(name, c); else Warn("curve '" + name + "' not exposed"); }
        void Grad(VisualEffect fx, string name, Gradient g) { if (fx.HasGradient(name)) fx.SetGradient(name, g); else Warn("gradient '" + name + "' not exposed"); }

        /// <summary>A Vector / Position typed property is flattened by the graph as "Name_vector" / "Name_position"; the plain name is tried as well.</summary>
        void V3(VisualEffect fx, string name, string suffix, Vector3 v)
        {
            string flat = name + suffix;
            if (fx.HasVector3(flat)) fx.SetVector3(flat, v);
            else if (fx.HasVector3(name)) fx.SetVector3(name, v);
            else Warn("vector3 '" + flat + "' / '" + name + "' not exposed");
        }

        void Apply(Layer l)
        {
            var fx = l.Fx; var r = l.R;
            F(fx, "Spawn Rate", 0f);                            // closed until the window opens (OnTick)
            T(fx, "Main Texture", TexOf(l.Kind));
            V2(fx, "Flipbook Size", Vector2.one);               // one painted sprite per quad, no flipbook
            B(fx, "Use Flipbook", false);
            F(fx, "Flipbook Framerate", 0f);
            F(fx, "LifeTime", r.Life);
            V3(fx, "Initial Velocity", "_vector", r.Vel);
            F(fx, "Initial Velocity Divergence", r.Div);
            V3(fx, "Global Force", "_vector", r.Force);
            F(fx, "Drag", r.Drag);
            V2(fx, "Angle Min/Max", r.Angle);
            Curve(fx, "Size Over Life", new AnimationCurve(new Keyframe(0f, r.S0), new Keyframe(0.4f, r.S1), new Keyframe(1f, r.S2)));
            var g = new Gradient();
            var tint = r.Tint; tint.a = 1f;
            g.SetKeys(new[] { new GradientColorKey(tint, 0f), new GradientColorKey(tint, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(1f, 0.65f), new GradientAlphaKey(0f, 1f) });
            Grad(fx, "Color Over Life", g);
            F(fx, "Soft Particle Fade Distance", 0.08f);        // the base of a sprite stands on the ground: a long soft fade would melt it
            V2(fx, "Camera Fade Min/Max Distance", new Vector2(0.05f, 0.3f));
            // the default bounds are 2 x 3 x 2 m: far too small for a 2.2 m ring (the effect would be culled)
            Vector3 size = l.Hit ? new Vector3(4f, 4f, 4f) : new Vector3(7f, 4.5f, 7f);
            if (fx.HasVector3("Bounds_center")) fx.SetVector3("Bounds_center", new Vector3(0f, 1.2f, 0f)); else Warn("vector3 'Bounds_center' not exposed");
            if (fx.HasVector3("Bounds_size")) fx.SetVector3("Bounds_size", size); else Warn("vector3 'Bounds_size' not exposed");
        }

        void Open(Layer l)
        {
            if (l.Fx == null) return;
            l.Fx.enabled = true;                                // OnPlay is sent by the component itself on enable
            Apply(l);
            l.On = true; l.LastRate = 0f;
        }

        void Rate(Layer l, float rate)
        {
            if (l.Fx == null || Mathf.Approximately(rate, l.LastRate)) return;
            l.LastRate = rate;
            F(l.Fx, "Spawn Rate", rate);
        }

        public void OnBegin(SpellFxCast cast)
        {
            Build(cast);
        }

        public void OnLaunch(SpellFxCast cast)
        {
            Build(cast);
            _launched = true;
            foreach (var l in _layers) if (!l.Hit) Open(l);
        }

        public void OnTick(SpellFxCast cast, float ageSeconds)
        {
            _age = ageSeconds;
            if (!_launched) return;
            foreach (var l in _layers)
            {
                if (!l.On) continue;
                float t = ageSeconds - l.Origin;
                Rate(l, t >= l.R.T0 && t <= l.R.T1 ? l.R.Rate : 0f);
            }
        }

        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            Build(cast);
            Vector3 pos = hit.Feet != Vector3.zero ? hit.Feet : hit.Position - Vector3.up * 0.9f;
            foreach (var l in _layers)
            {
                if (!l.Hit || l.Fx == null) continue;
                l.Fx.transform.position = pos;                  // particles already thrown by an earlier hit stay where they are (world-space simulation)
                if (!l.On) Open(l);
                l.Origin = _age; l.LastRate = -1f;              // the window starts now
            }
        }

        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason)
        {
            foreach (var l in _layers) if (l.On) Rate(l, 0f);
        }
    }
}
