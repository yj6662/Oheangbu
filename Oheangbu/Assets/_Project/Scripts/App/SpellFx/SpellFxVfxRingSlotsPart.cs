using System.Collections.Generic;
using Oheangbu.App;
using UnityEngine;
using UnityEngine.VFX;

namespace Oheangbu.App.SpellFx
{
    /// <summary>D308-153 variant 우/G2, round 1 of the score loop (2026-10-29, the user: "G2 + I 그림"): the painted wave ring as VFX Graph particles PLACED, not scattered.
    /// Every painted element (I sprites: mound, crest, claw, ink, splash) is one child <see cref="VisualEffect"/> running the point copy of the wash graph (arc-circle radius
    /// ~0.1 m, made by SpellFxSetupG), parked at its own slot on a ring round the cast origin. Each one fires ONE particle (the spawn rate is opened for a single frame at
    /// count / deltaTime) that starts under the ground and is thrown up against a downward force: it rises out of the ground, stands, and sinks back (the depth test hides the
    /// part below the ground, the soft-particle fade softens the contact line).
    /// Layout (angles round the origin, 0 = the side facing away from the camera): the camera side (+-gapHalfDeg) stays open so the caster is seen; mounds all round the
    /// rest, rising from the two front ends toward the back; tall crests on the back half; the climax crest back-right (where the concept's big wave breaks); two big
    /// curls at the front corners; ink strokes over the mounds; foam splash at the crests' feet. A hit adds a crest and a splash at the struck body.
    /// The look only: nothing here is read by a rule.</summary>
    public sealed class SpellFxVfxRingSlotsPart : MonoBehaviour, ISpellFxPart
    {
        enum Tex { Mound, Crest, Claw, Ink, Splash }

        sealed class Slot
        {
            public Tex Tex;
            public Vector3 Pos;            // ground point (world)
            public float Size, T0, Life, Rise, Angle;
            public int Count = 1;
            public bool Hit;
            public VisualEffect Fx;
            public int State;              // 0 waiting, 1 fired (open until a later frame), 2 done
            public int FireFrame;
        }

        [SerializeField] VisualEffectAsset pointGraph;
        [SerializeField] Texture2D moundTex, crestTex, clawTex, inkTex, splashTex;
        [Header("Layout (metres / degrees / seconds)")]
        [SerializeField] float ringRadius = 3.0f;
        [Tooltip("The ring centre is pushed this far from the origin away from the camera: the camera (about 2.8 m behind the caster) must stay outside the ring.")]
        [SerializeField] float centreAhead = 0.9f;
        [SerializeField] float gapHalfDeg = 38f;
        [SerializeField] int mounds = 16;
        [SerializeField] float moundSize = 2.6f;
        [SerializeField] float crestSize = 3.0f;
        [SerializeField] float climaxSize = 4.4f;
        [SerializeField] float climaxDeg = 28f;
        [SerializeField] float cornerDeg = 58f;
        [SerializeField] float cornerRadius = 2.3f;
        [SerializeField] float cornerSize = 1.9f;
        [SerializeField] float sweep = 0.32f;
        [SerializeField] float life = 1.7f;
        [SerializeField] int seed = 7;
        [Tooltip("The arc-circle centre height baked into the graph copy (0.6 m in VFX_V308_na_wash).")]
        [SerializeField] float graphCentreY = 0.6f;

        readonly List<Slot> _slots = new List<Slot>(48);
        readonly HashSet<string> _warned = new HashSet<string>();
        float _age;
        bool _built, _launched, _logged;
        const float Window = 0.12f;
        Vector3 _origin, _back, _right;

        Texture2D TexOf(Tex t)
        {
            switch (t)
            {
                case Tex.Mound: return moundTex;
                case Tex.Crest: return crestTex;
                case Tex.Claw: return clawTex;
                case Tex.Ink: return inkTex;
                default: return splashTex;
            }
        }

        /// <summary>Ground point at <paramref name="deg"/> round the origin (0 = away from the camera, + = to the right as the camera sees it).</summary>
        Vector3 At(float deg, float radius)
        {
            float a = deg * Mathf.Deg2Rad;
            return _origin + (_back * Mathf.Cos(a) + _right * Mathf.Sin(a)) * radius;
        }

        void Layout(SpellFxCast cast)
        {
            _origin = cast != null ? cast.Origin : transform.position;
            // "back" = away from the camera, on the ground plane; the cast's forward when there is no camera
            Vector3 back = cast != null ? cast.Forward : transform.forward;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 d = _origin - cam.transform.position; d.y = 0f;
                if (d.sqrMagnitude > 0.01f) back = d;
            }
            back.y = 0f; if (back.sqrMagnitude < 1e-4f) back = Vector3.forward;
            _back = back.normalized; _right = Vector3.Cross(Vector3.up, _back).normalized;
            _origin += _back * centreAhead;

            var rnd = new System.Random(seed);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            float open = 180f - gapHalfDeg;        // mounds run from -open to +open (through the back)

            // mounds: the body of the ring, rising from both front ends toward the back
            for (int i = 0; i < mounds; i++)
            {
                float u = mounds == 1 ? 0.5f : i / (float)(mounds - 1);
                float deg = Mathf.Lerp(-open, open, u) + R(-4f, 4f);
                float fromBack = Mathf.Abs(deg) / open;                    // 1 at the front ends, 0 at the back
                _slots.Add(new Slot { Tex = Tex.Mound, Pos = At(deg, ringRadius + R(-0.25f, 0.25f)), Size = moundSize * R(0.85f, 1.15f), T0 = (1f - fromBack) * sweep + R(0f, 0.04f), Life = life * R(0.92f, 1.05f), Rise = 0.5f, Angle = R(-3f, 3f) });
            }
            // a second, lower row just inside: the ring reads as a band with depth, not a fence
            for (int i = 0; i < mounds / 2; i++)
            {
                float deg = Mathf.Lerp(-open + 15f, open - 15f, (i + 0.5f) / (mounds / 2)) + R(-6f, 6f);
                float fromBack = Mathf.Abs(deg) / open;
                _slots.Add(new Slot { Tex = Tex.Mound, Pos = At(deg, ringRadius - 0.7f), Size = moundSize * 0.7f * R(0.85f, 1.1f), T0 = (1f - fromBack) * sweep + 0.06f, Life = life * 0.95f, Rise = 0.45f, Angle = R(-3f, 3f) });
            }
            // crests on the back half
            foreach (float deg in new[] { -112f, -62f, -18f, 70f, 118f })
            {
                float d = deg + R(-6f, 6f);
                _slots.Add(new Slot { Tex = Tex.Crest, Pos = At(d, ringRadius + 0.15f), Size = crestSize * R(0.8f, 1.1f), T0 = sweep * 0.6f + R(0.05f, 0.2f), Life = life * 0.9f, Rise = 0.5f, Angle = R(-5f, 5f) });
            }
            // the climax: the big breaking wave back-right, last and tallest
            _slots.Add(new Slot { Tex = Tex.Crest, Pos = At(climaxDeg, ringRadius + 0.3f), Size = climaxSize, T0 = sweep + 0.12f, Life = life * 0.95f, Rise = 0.5f, Angle = 4f });
            _slots.Add(new Slot { Tex = Tex.Splash, Pos = At(climaxDeg, ringRadius - 0.2f), Size = climaxSize * 0.45f, T0 = sweep + 0.5f, Life = 0.8f, Rise = 0.35f, Angle = 0f, Count = 2 });
            // the front corners: two big curls close to the camera (the concept's bottom-left / bottom-right)
            foreach (float s in new[] { -1f, 1f })
            {
                _slots.Add(new Slot { Tex = Tex.Claw, Pos = At(s * (180f - cornerDeg), cornerRadius), Size = cornerSize * R(0.95f, 1.1f), T0 = R(0f, 0.06f), Life = life * 0.9f, Rise = 0.55f, Angle = s * -6f });
            }
            // ink strokes riding over the mounds
            for (int i = 0; i < 5; i++)
            {
                float deg = Mathf.Lerp(-open + 20f, open - 20f, (i + 0.5f) / 5f) + R(-10f, 10f);
                _slots.Add(new Slot { Tex = Tex.Ink, Pos = At(deg, ringRadius + 0.05f), Size = moundSize * R(0.8f, 1.0f), T0 = sweep * 0.5f + R(0f, 0.25f), Life = life * 0.7f, Rise = 0.75f, Angle = R(-14f, 14f) });
            }
            // foam at the crests' feet
            for (int i = 0; i < 4; i++)
            {
                float deg = R(-open + 10f, open - 10f);
                _slots.Add(new Slot { Tex = Tex.Splash, Pos = At(deg, ringRadius - 0.3f), Size = R(0.9f, 1.3f), T0 = sweep * 0.8f + R(0f, 0.35f), Life = 0.8f, Rise = 0.35f, Angle = R(-15f, 15f) });
            }
        }

        void Build(SpellFxCast cast)
        {
            if (_built) return;
            _built = true;
            if (pointGraph == null) { Warn("no point graph"); return; }
            Layout(cast);
            int layerId = VfxAfterFog308.Layer;
            for (int i = 0; i < _slots.Count; i++) Make(_slots[i], i, layerId);
            VfxAfterFog308.Route(transform);
        }

        void Make(Slot s, int i, int layerId)
        {
            var tex = TexOf(s.Tex);
            if (tex == null) { Warn("no texture for " + s.Tex); return; }
            var go = new GameObject("VFX_" + s.Tex + "_" + i);
            go.transform.SetParent(transform, false);
            // the particle is born at the arc-circle centre (graphCentreY above the transform): park the transform so it is born half a size under the ground
            float startY = -s.Size * s.Rise;
            go.transform.SetPositionAndRotation(s.Pos + Vector3.up * (startY - graphCentreY), Quaternion.identity);
            if (layerId >= 0) go.layer = layerId;
            var fx = go.AddComponent<VisualEffect>();
            fx.visualEffectAsset = pointGraph;
            fx.enabled = false;
            s.Fx = fx;
        }

        void Warn(string text) { if (_warned.Add(text)) Debug.LogWarning("[SpellFxVfxRingSlotsPart] " + text, this); }
        void F(VisualEffect fx, string n, float v) { if (fx.HasFloat(n)) fx.SetFloat(n, v); else Warn("float '" + n + "'"); }
        void B(VisualEffect fx, string n, bool v) { if (fx.HasBool(n)) fx.SetBool(n, v); else Warn("bool '" + n + "'"); }
        void V2(VisualEffect fx, string n, Vector2 v) { if (fx.HasVector2(n)) fx.SetVector2(n, v); else Warn("vector2 '" + n + "'"); }
        void V3(VisualEffect fx, string n, Vector3 v) { if (fx.HasVector3(n + "_vector")) fx.SetVector3(n + "_vector", v); else if (fx.HasVector3(n)) fx.SetVector3(n, v); else Warn("vector3 '" + n + "'"); }

        /// <summary>One particle, thrown up so its centre climbs from startY to just above half its size at 45 % of its life, then falls back under the ground.</summary>
        void Arm(Slot s)
        {
            var fx = s.Fx; if (fx == null) return;
            fx.enabled = true;
            F(fx, "Spawn Rate", 0f);
            if (fx.HasTexture("Main Texture")) fx.SetTexture("Main Texture", TexOf(s.Tex)); else Warn("texture 'Main Texture'");
            V2(fx, "Flipbook Size", Vector2.one); B(fx, "Use Flipbook", false); F(fx, "Flipbook Framerate", 0f);
            F(fx, "LifeTime", s.Life);
            float startY = -s.Size * s.Rise, peakY = s.Size * 0.5f + 0.04f, tp = s.Life * 0.45f;
            float h = peakY - startY, v = 2f * h / tp, g = v / tp;
            V3(fx, "Initial Velocity", new Vector3(0f, v, 0f));
            F(fx, "Initial Velocity Divergence", 0f);
            V3(fx, "Global Force", new Vector3(0f, -g, 0f));
            F(fx, "Drag", 0f);
            V2(fx, "Angle Min/Max", new Vector2(s.Angle, s.Angle));
            if (fx.HasAnimationCurve("Size Over Life")) fx.SetAnimationCurve("Size Over Life", new AnimationCurve(new Keyframe(0f, s.Size * 0.82f), new Keyframe(0.45f, s.Size), new Keyframe(1f, s.Size * 0.94f)));
            var gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            if (fx.HasGradient("Color Over Life")) fx.SetGradient("Color Over Life", gr);
            F(fx, "Soft Particle Fade Distance", 0.12f);
            V2(fx, "Camera Fade Min/Max Distance", new Vector2(0.05f, 0.3f));
            float b = s.Size * 2.2f + h;
            if (fx.HasVector3("Bounds_center")) fx.SetVector3("Bounds_center", new Vector3(0f, h * 0.5f, 0f));
            if (fx.HasVector3("Bounds_size")) fx.SetVector3("Bounds_size", new Vector3(b, b, b));
        }

        public void OnBegin(SpellFxCast cast) { Build(cast); }

        public void OnLaunch(SpellFxCast cast)
        {
            Build(cast);
            _launched = true;
            foreach (var s in _slots) if (!s.Hit) Arm(s);
        }

        public void OnTick(SpellFxCast cast, float ageSeconds)
        {
            _age = ageSeconds;
            if (!_launched) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-3f);
            foreach (var s in _slots)
            {
                if (s.Fx == null || s.State == 2) continue;
                if (s.State == 1) { if (ageSeconds >= s.T0 + Window) { F(s.Fx, "Spawn Rate", 0f); s.State = 2; } continue; }
                if (ageSeconds >= s.T0) { F(s.Fx, "Spawn Rate", (s.Count + 0.5f) / Window); s.State = 1; s.FireFrame = Time.frameCount; }       // Count particles over a short window (a one-frame window spawned nothing)
            }
            if (!_logged && ageSeconds > 0.9f)
            {
                _logged = true; int alive = 0, armed = 0, on = 0, culled = 0;
                foreach (var s in _slots) { if (s.Fx == null) continue; armed++; if (s.Fx.enabled) on++; alive += s.Fx.aliveParticleCount; if (s.Fx.culled) culled++; }
                Debug.Log("[SpellFxVfxRingSlotsPart] age " + ageSeconds.ToString("0.00") + " slots " + _slots.Count + " fx " + armed + " enabled " + on + " alive particles " + alive + " culled " + culled + " origin " + _origin + " back " + _back + " dt " + dt, this);
            }
        }

        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            Build(cast);
            Vector3 feet = hit.Feet != Vector3.zero ? hit.Feet : hit.Position - Vector3.up * 0.9f;
            int layerId = VfxAfterFog308.Layer;
            var crest = new Slot { Tex = Tex.Crest, Pos = feet + _back * 0.3f, Size = 2.6f, T0 = _age, Life = 1.1f, Rise = 0.5f, Angle = -6f, Hit = true };
            var splash = new Slot { Tex = Tex.Splash, Pos = feet, Size = 1.4f, T0 = _age + 0.18f, Life = 0.8f, Rise = 0.3f, Angle = 0f, Hit = true, Count = 2 };
            foreach (var s in new[] { crest, splash }) { _slots.Add(s); Make(s, _slots.Count - 1, layerId); Arm(s); }
            VfxAfterFog308.Route(transform);
        }

        /// <summary>Nothing: the executor ends a ring cast almost at once, and the slots still to come must rise through the player's tail (round 1 drew nothing because this closed them).</summary>
        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason) { }
    }
}
