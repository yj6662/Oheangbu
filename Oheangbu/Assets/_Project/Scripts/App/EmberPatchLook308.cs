using System;
using System.Collections.Generic;
using Oheangbu.App.SpellVFX120;
using Oheangbu.App.World;
using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Oheangbu.App
{
    // #308 D308-125 5 (c): the look of the ember patches and of the scorch. Presentation only - it listens to CombatLoopWiring
    // (PatchLaid308, ScorchStarted308 / Washed308 / Ended308) and reads its patches and Ember308; nothing goes back to a rule.
    // Each laid patch gets its profile's Look308 at the burst point, scaled by the rule's Radius, its body given the rule's Life.
    // The patch is turned about its normal at random, its pool given the rule's life and a crumbling tail (PoolFrames, PoolTone),
    // its tongues the life and the first catch (BurnRate). A patch washed by 온 (Wiped) breaks away from the player (Splash: the
    // pool and tongues go at once, the tongues fold, ink drops, a wet stain), one whose life ran out burns out (Dry: the pool
    // crumbles under a grey ash ring), one replaced or cleared pales away (Fade). The scorch puts the profile's
    // ScorchLook308 on whichever right hand is on screen - the close-up hand while drawing, else the world hand when its body is
    // really drawn, else nowhere - and hands the HP vessel the share of the scorch left (HudVessels308.SetScorch01).
    [DefaultExecutionOrder(1100)]
    public sealed class EmberPatchLook308 : MonoBehaviour
    {
        const int MaxLooks = 8;
        const float FadeSeconds = .6f, WashSeconds = .3f, EndLinger = 1.5f, SmokeRate = 3f;
        static readonly Color Pig = new Color(.722f, .263f, .184f), Dried = new Color(.165f, .149f, .133f);
        static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        sealed class Look
        {
            public EnemyGroundPatch308 Patch;
            public Vfx308ZoneBody Body;
            public float Until;
            public bool Splashed;
            public Action<EnemyGroundPatch308> OnWiped;
        }

        readonly List<Look> _looks = new List<Look>();
        CombatLoopWiring _wiring;
        HudController _hud;
        Transform _player;
        EnemyGroundPatchSO _lastProfile;

        WorldMacroPlayerGestureRig _rig;
        Transform _boundWorldHand;
        readonly List<Renderer> _worldHandBody = new List<Renderer>();

        GameObject _scorch;
        ParticleSystem _strokes, _smoke, _wash;
        ParticleSystemRenderer[] _scorchR;
        float[] _scorchOpacity;
        MaterialPropertyBlock _mpb;
        int _scorchHomeLayer = -1, _scorchLayer = -1;
        float _span = 1f, _washAt = float.NaN, _endAt = float.NaN;
        Transform _onHand;
        bool _wasScorched;

        public int LookCount => _looks.Count;
        public bool ScorchShown => _scorch != null && _onHand != null && float.IsNaN(_washAt) && float.IsNaN(_endAt);
        public Transform ScorchHand => _onHand;

        public void Boot(CombatLoopWiring wiring, HudController hud, Transform player)
        {
            _wiring = wiring; _hud = hud; _player = player;
            if (_wiring == null) return;
            _wiring.PatchLaid308 += OnLaid;
            _wiring.ScorchStarted308 += OnScorchStarted;
            _wiring.ScorchWashed308 += OnScorchWashed;
            _wiring.ScorchEnded308 += OnScorchEnded;
        }

        void OnDestroy()
        {
            if (_wiring != null)
            {
                _wiring.PatchLaid308 -= OnLaid;
                _wiring.ScorchStarted308 -= OnScorchStarted;
                _wiring.ScorchWashed308 -= OnScorchWashed;
                _wiring.ScorchEnded308 -= OnScorchEnded;
            }
            foreach (var l in _looks) { Unhook(l); if (l.Body != null) l.Body.Fade(.3f); }
            _looks.Clear();
            DropScorch();
            Vessels?.SetScorch01(0f, 0f);
        }

        Oheangbu.App.World.UI.HudVessels308 Vessels => _hud != null ? _hud.Vessels308 : null;

        // ---------------------------------------------------------------- patches

        void OnLaid(EnemyGroundPatch308 patch)
        {
            if (patch == null || patch.Profile == null) return;
            _lastProfile = patch.Profile;
            var prefab = patch.Profile.Look308;
            if (prefab == null) return;
            while (_looks.Count >= MaxLooks)
            {
                var old = _looks[0];
                _looks.RemoveAt(0);
                Unhook(old);
                if (old.Body != null) old.Body.Fade(.3f);
            }
            var go = Instantiate(prefab, patch.transform.position, patch.transform.rotation);
            var scene = patch.gameObject.scene;
            if (scene.IsValid() && scene.isLoaded && go.scene != scene) SceneManager.MoveGameObjectToScene(go, scene);
            float r = Mathf.Max(.05f, patch.Radius);
            go.transform.localScale = Vector3.one * r;
            go.transform.Rotate(0f, UnityEngine.Random.value * 360f, 0f, Space.Self);   // no two pools alike
            var body = go.GetComponent<Vfx308ZoneBody>();
            if (body == null) { Destroy(go); return; }
            float life = Mathf.Max(.5f, patch.Profile.Life);
            body.Delay = 0f; body.Life = life;
            body.DryFade = Mathf.Max(body.DryFade, PoolTail);
            // the laid quads float a few cm off the ground (debate 8: 3-5 cm along the normal)
            Lift(go.transform.Find("Pool"), r); Lift(go.transform.Find("End"), r, .005f); Lift(go.transform.Find("Break/Wet"), r, .005f);
            // the burning-down layers run once over the rule's life (they are not playing yet: the body starts them)
            foreach (var ps in body.Body)
            {
                if (ps == null) continue;
                var main = ps.main;
                if (ps.name == "Pool")
                {
                    // the pool lives the rule's life and then crumbles over the tail: its cels and its tone along that span
                    main.startLifetime = life + PoolTail;
                    var tsa = ps.textureSheetAnimation; tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, PoolFrames(life));
                    var col = ps.colorOverLifetime; col.color = new ParticleSystem.MinMaxGradient(PoolTone(life));
                    continue;
                }
                if (!(ps.name == "Ring" || ps.name == "Inner" || ps.name == "Wisps")) continue;
                main.duration = life;
                var em = ps.emission; em.rateOverTime = new ParticleSystem.MinMaxCurve(em.rateOverTime.curveMultiplier, BurnRate(life));
            }
            var look = new Look { Patch = patch, Body = body, Until = patch.Until };
            look.OnWiped = p => OnWiped(look, p);
            patch.Wiped += look.OnWiped;
            _looks.Add(look);
        }

        void OnWiped(Look look, EnemyGroundPatch308 patch)
        {
            if (look.Body == null || look.Splashed) return;
            look.Splashed = true;
            var brk = look.Body.transform.Find("Break");
            if (brk != null && patch != null)
            {
                // the fold runs along the Break child's +Z: away from the player, in the ground's plane
                Vector3 n = patch.Normal;
                Vector3 away = _player != null ? Vector3.ProjectOnPlane(patch.Position - _player.position, n) : Vector3.zero;
                if (away.sqrMagnitude > 1e-4f) brk.rotation = Quaternion.LookRotation(away.normalized, n);
            }
            foreach (var ps in look.Body.Body) if (ps != null) ps.Clear(false);   // the pool and the tongues go at once: the wet stain takes the pool's place
            look.Body.Splash();
        }

        static void Lift(Transform t, float radius, float extra = 0f)
        {
            if (t != null) t.localPosition = new Vector3(0f, (PoolLift + extra) / radius, 0f);
        }

        // ---------------------------------------------------------------- the patch's timing (Vfx308Build bakes these at 6 s)

        /// <summary>Seconds the pool crumbles after the rule's life (debate 8 ②: nothing red 7.2 s after a 6 s patch).</summary>
        public const float PoolTail = 1.2f;
        /// <summary>Seconds the rim catches at three times its rate (debate 8: "첫 0.4 s ×3").</summary>
        public const float CatchSeconds = .4f;
        const float PoolLift = .04f;

        /// <summary>The pool's frame (0-1 of its 16 cels) over a life of `life` + PoolTail: it forms black (cels 0-3) in the
        /// catch, holds its reddened rim (4-13) to the end of the life and crumbles (14-15) over the tail.</summary>
        public static AnimationCurve PoolFrames(float life)
        {
            float span = life + PoolTail;
            return new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(CatchSeconds / span, 4f / 16f), new Keyframe(life / span, 13.9f / 16f), new Keyframe(1f, 15.9f / 16f));
        }

        /// <summary>The pool's tone over the same span: whole while it forms, then darker only (5 s into 6 at most .6 - the
        /// tongues carry the rest of debate 8 ②'s halving), paling out over the tail.</summary>
        public static Gradient PoolTone(float life)
        {
            float span = life + PoolTail, k = CatchSeconds / span, end = life / span;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, k), new GradientColorKey(new Color(.6f, .6f, .6f), end * .83f),
                    new GradientColorKey(new Color(.45f, .45f, .45f), end), new GradientColorKey(new Color(.3f, .3f, .3f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, end), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        /// <summary>The tongues' rate over the rule's life (times the layer's rate): three times for the catch, then
        /// burning down to a third.</summary>
        public static AnimationCurve BurnRate(float life)
        {
            float k = Mathf.Clamp(CatchSeconds / Mathf.Max(.1f, life), .01f, .5f);
            return new AnimationCurve(new Keyframe(0f, 3f), new Keyframe(k, 3f), new Keyframe(Mathf.Min(k + .02f, 1f), 1f), new Keyframe(1f, .33f));
        }

        static void Unhook(Look look)
        {
            if ((object)look.Patch != null && look.OnWiped != null) look.Patch.Wiped -= look.OnWiped;
            look.OnWiped = null;
        }

        void TickLooks(float now)
        {
            for (int i = _looks.Count - 1; i >= 0; i--)
            {
                var l = _looks[i];
                if (l.Body == null || l.Body.Gone) { Unhook(l); _looks.RemoveAt(i); continue; }
                if (l.Patch != null) continue;
                // the rule dropped the patch: its life ran out, or it was replaced / cleared (a washed one is already splashing)
                if (!l.Splashed) { if (now >= l.Until - .05f) l.Body.Dry(); else l.Body.Fade(FadeSeconds); }
                Unhook(l);
                _looks.RemoveAt(i);
            }
        }

        // ---------------------------------------------------------------- scorch

        void OnScorchStarted()
        {
            float now = Time.time;
            var profile = StandingProfile(now) ?? _lastProfile;
            var ember = _wiring != null ? _wiring.Ember308 : null;
            float left = ember != null && !float.IsNaN(ember.ScorchUntil) ? ember.ScorchUntil - now : 0f;
            _span = Mathf.Max(.05f, profile != null ? profile.ScorchLife : left);
            DropScorch();
            var prefab = profile != null ? profile.ScorchLook308 : null;
            if (prefab == null) return;
            _scorch = Instantiate(prefab);
            var scene = _player != null ? _player.gameObject.scene : gameObject.scene;
            if (scene.IsValid() && scene.isLoaded && _scorch.scene != scene) SceneManager.MoveGameObjectToScene(_scorch, scene);
            _scorchHomeLayer = _scorch.layer; _scorchLayer = _scorchHomeLayer;
            foreach (var ps in _scorch.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.transform.parent != null && ps.transform.parent.name == "Wash") _wash = ps;
                else if (ps.name == "Strokes") _strokes = ps;
                else if (ps.name == "Smoke") _smoke = ps;
            }
            _scorchR = new[] { R(_strokes), R(_smoke) };
            _scorchOpacity = new float[_scorchR.Length];
            for (int i = 0; i < _scorchR.Length; i++)
            {
                var m = _scorchR[i] != null ? _scorchR[i].sharedMaterial : null;
                _scorchOpacity[i] = m != null && m.HasProperty(OpacityId) ? m.GetFloat(OpacityId) : 1f;
            }
            _mpb ??= new MaterialPropertyBlock();
            _onHand = null;
            Follow();
            if (_strokes != null) _strokes.Play(false);
            if (_smoke != null) _smoke.Play(false);
        }

        void OnScorchWashed()
        {
            if (_scorch == null || !float.IsNaN(_washAt)) return;
            _washAt = Time.time;
            Stop(_strokes); Stop(_smoke);
            if (_wash != null && _onHand != null) _wash.Play(false);
        }

        void OnScorchEnded()
        {
            if (_scorch == null || !float.IsNaN(_endAt) || !float.IsNaN(_washAt)) return;
            _endAt = Time.time;
            Stop(_strokes); Stop(_smoke);
        }

        void TickScorch(float now)
        {
            var ember = _wiring != null ? _wiring.Ember308 : null;
            bool scorched = ember != null && ember.Scorched(now);
            float left = scorched ? ember.ScorchUntil - now : 0f;
            float scorch01 = scorched ? Mathf.Clamp01(left / _span) : 0f;
            // a clear (death, a rest) raises no event: the rule just stops being scorched
            if (_wasScorched && !scorched && _scorch != null && float.IsNaN(_washAt) && float.IsNaN(_endAt)) OnScorchEnded();
            _wasScorched = scorched;
            Vessels?.SetScorch01(scorch01, left);

            if (_scorch == null) return;
            if (!float.IsNaN(_washAt))
            {
                float k = 1f - Mathf.Clamp01((now - _washAt) / WashSeconds);
                SetOpacity(k);
                if (k <= 0f) { if (_strokes != null) _strokes.Clear(false); if (_smoke != null) _smoke.Clear(false); }
                Follow();
                if (now - _washAt >= WashSeconds + EndLinger) DropScorch();
                return;
            }
            if (!float.IsNaN(_endAt))
            {
                Follow();
                if (now - _endAt >= EndLinger) DropScorch();
                return;
            }
            bool shown = Follow();
            if (_strokes != null) { var main = _strokes.main; main.startColor = Color.Lerp(Dried, Pig, scorch01); }
            if (_smoke != null) { var em = _smoke.emission; em.rateOverTime = SmokeRate * scorch01; }
            Emit(_strokes, shown); Emit(_smoke, shown);
        }

        // Puts the scorch on the shown right hand (pose and layer); false = no hand on screen (nothing drawn).
        bool Follow()
        {
            if (_scorch == null) return false;
            BindRig();
            Transform hand = null; int layer = _scorchHomeLayer;
            if (_rig != null && _rig.isActiveAndEnabled)
            {
                var near = _rig.NearHand308;
                if (near != null && _rig.Diagnostics.NearVisible && near.gameObject.activeInHierarchy)
                {
                    hand = near;
                    int l = _rig.NearLayer308;
                    if (l >= 0) layer = l;
                }
                else if (WorldHandShown()) hand = _rig.WorldHand308;
            }
            if (hand != _onHand)
            {
                // the trail of smoke belongs to the hand it rose from
                if (_onHand != null && _smoke != null) _smoke.Clear(false);
                _onHand = hand;
            }
            if (layer != _scorchLayer)
            {
                foreach (var t in _scorch.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                _scorchLayer = layer;
            }
            bool shown = hand != null;
            if (shown) _scorch.transform.SetPositionAndRotation(hand.position, hand.rotation);
            foreach (var r in _scorchR) if (r != null) r.enabled = shown;
            return shown;
        }

        void BindRig()
        {
            if (_rig == null)
            {
                if (_player != null) _rig = _player.root.GetComponentInChildren<WorldMacroPlayerGestureRig>(true);
                if (_rig == null) _rig = FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            }
            var hand = _rig != null ? _rig.WorldHand308 : null;
            if (hand == _boundWorldHand) return;
            _boundWorldHand = hand;
            _worldHandBody.Clear();
            if (hand == null) return;
            // the body renderers the world hand deforms (the close-up arm is not one of them)
            foreach (var smr in hand.root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = smr.bones;
                if (bones == null) continue;
                foreach (var b in bones) if (b == hand) { _worldHandBody.Add(smr); break; }
            }
        }

        // The world body is shadows-only in first person: the hand is on screen only while one of its renderers really draws.
        bool WorldHandShown()
        {
            if (_boundWorldHand == null) return false;
            foreach (var r in _worldHandBody)
                if (r != null && r.enabled && r.gameObject.activeInHierarchy && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly) return true;
            return false;
        }

        EnemyGroundPatchSO StandingProfile(float now)
        {
            if (_wiring == null || _player == null) return null;
            var patches = _wiring.Patches308;
            for (int i = patches.Count - 1; i >= 0; i--)
            {
                var p = patches[i];
                if (p != null && p.Live(now) && p.Contains(_player.position)) return p.Profile;
            }
            return null;
        }

        void SetOpacity(float k)
        {
            if (_scorchR == null) return;
            for (int i = 0; i < _scorchR.Length; i++)
            {
                var r = _scorchR[i];
                if (r == null) continue;
                r.GetPropertyBlock(_mpb); _mpb.SetFloat(OpacityId, _scorchOpacity[i] * k); r.SetPropertyBlock(_mpb);
            }
        }

        void DropScorch()
        {
            if (_scorch != null) Destroy(_scorch);
            _scorch = null; _strokes = _smoke = _wash = null; _scorchR = null; _scorchOpacity = null;
            _washAt = _endAt = float.NaN; _onHand = null; _scorchLayer = _scorchHomeLayer = -1;
        }

        static ParticleSystemRenderer R(ParticleSystem ps) => ps != null ? ps.GetComponent<ParticleSystemRenderer>() : null;

        static void Stop(ParticleSystem ps) { if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting); }

        static void Emit(ParticleSystem ps, bool on)
        {
            if (ps == null) return;
            var em = ps.emission;
            if (em.enabled != on) em.enabled = on;
        }

        void LateUpdate()
        {
            float now = Time.time;
            TickLooks(now);
            TickScorch(now);
        }
    }
}
