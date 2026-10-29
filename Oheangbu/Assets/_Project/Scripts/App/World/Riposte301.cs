using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using Oheangbu.Presentation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.World
{
    // 앞잡 (D301, D308-147 plan C, D308-148): Q pressed and released with no stroke, with the locked target's weak point open and in
    // range, writes ONE stroke through the target (일필휘지) - one hit, no volley.
    // Input (D308-147, plan C): the press itself fires nothing. The drawing controller enters its draw mode as usual and asks this class
    // (IDrawPressArbiter) whether the press is a riposte candidate; a candidate holds back the draw presentation until the first stroke
    // (then the gesture is a drawing and this window's riposte is gone), and a release without a stroke comes back here after the mode is
    // left quietly: the candidate is checked again (same lock, same life, same opening, in reach, able to act) and fires once. A window
    // is answered once: by this stroke or by a drawing, named by the enemy + its life + the opening of its weak point
    // (RiposteWindowClaim308). Recognition is untouched: nothing here reads a stroke.
    // D308-148 (step 5): the big brush swing leaves one huge stroke in the air that pierces the target (RiposteStroke308Effect:
    // matte opaque cutout on a tilted world plane, revealed in brush order, then dried by a crack threshold and dropped as flat flakes;
    // the picture's colour drains through the effect's own transient volume). The damage is ONE hit of Profile.TotalDamage (45) on the
    // frame the revealed head crosses the torso (RiposteStroke308Impact); the weak-point window's 1.5 is multiplied by EnemyVitals at
    // that moment (inside = 67.5) and nowhere else. Reaction, specks and the short hit stop come in the same frame. The window is closed
    // by key WindowCloseDelay seconds later (the enemy stays staggered while the stroke dries). The old look - magic circles, glyphs,
    // wisps, trails, ghosts, the per-shot flash and shake - is gone from this path; its profile fields and assets stay, read by nobody.
    // D308-147: with the sword out the Q press only puts the sword away (the wiring's ordinary release) - the stroke is not
    // arbitrated there any more, and no aim-target fallback exists: a riposte is always the locked target's.
    [DefaultExecutionOrder(-80)]
    public sealed class Riposte301 : MonoBehaviour, IDrawPressArbiter
    {
        public Riposte301Profile Profile;
        public WorldMacroCombatWalker Walker;
        /// <summary>A riposte has taken the locked target's open window: the walker's draw gate stays shut for it (no new drawing in a
        /// window the riposte answered). Never true while a draw mode is on - a drawing in progress is not cancelled by a lock change.</summary>
        public bool SuppressDraw => Walker != null && Walker.Drawing != null && !Walker.Drawing.InDrawMode && _candidate.Claims.BlocksNewDraw(KeyOf(LockedTarget()));
        /// <summary>The Q press that is held right now is a riposte candidate that has not drawn (the prompt's condition).</summary>
        public bool IsRiposteCandidate => _candidate.Active;
        /// <summary>The enemy the candidate was fixed on (null when there is none).</summary>
        public EnemyVitals CandidateTarget => _candidate.Active ? _candidateTarget : null;
        public int Volleys { get; private set; }
        /// <summary>The fight half is running: fired and not yet closed (the window is closed WindowCloseDelay after the pierce). A new
        /// riposte is not offered meanwhile. The dry flakes and the colour drain outlive it (VisualAlive) and block nothing.</summary>
        public bool Active => _combatOpen;
        /// <summary>The stroke's drawing, drying or flakes are still on screen.</summary>
        public bool VisualAlive => _effect != null && _effect.AnyLive;
        /// <summary>The stroke's presentation is made (the profile has its materials): a riposte is offered only then.</summary>
        public bool StrokeReady => _effect != null && _effect.IsReady;
        /// <summary>The presentation, for the checks and the film tools.</summary>
        public RiposteStroke308Effect Effect => _effect;
        /// <summary>The frame the stroke pierces: the pierce point and the damage result of that one hit (default when nothing was hit).
        /// The hook for a hit sound or a counter.</summary>
        public event Action<Vector3, EnemyDamageResult> Pierced;

        RiposteStroke308Effect _effect;
        readonly RiposteStroke308HitStop _hitStop = new RiposteStroke308HitStop();
        EnemyVitals _target; uint _targetLife; float _clock, _impactClock;
        bool _combatOpen, _impacted;
        RiposteStroke308Effect.Slot _slot;
        LockOn _lockOn;
        // D308-147: the windows already answered, the candidate of the held press, the window of the stroke that runs
        readonly RiposteCandidate308 _candidate = new RiposteCandidate308();
        bool _beginning;
        EnemyVitals _candidateTarget; RiposteWindowKey308 _volleyKey;
        Riposte301Pose _pose;
        PlayerVisualRig _rig;
        float _shake;
        // captures that render from their own camera (presentation films): the stroke's tail is projected from it instead of the player's eye
        public static Transform BillboardView;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { BillboardView = null; }   // Enter Play Mode keeps statics

        void OnEnable() { RenderPipelineManager.beginCameraRendering += ShakeBegin; RenderPipelineManager.endCameraRendering += ShakeEnd; }

        void Update()
        {
            if (Profile == null || Walker == null || Walker.Motor == null || Walker.Drawing == null) return;
            EnsureWarm();
            if (_lockOn == null) _lockOn = Walker.Motor.GetComponent<LockOn>();
            // D308-147: no press-time fire any more - the drawing controller calls the IDrawPressArbiter members below
            float real = Time.captureDeltaTime > 0f ? Time.captureDeltaTime : Time.unscaledDeltaTime;
            // The hit stop counts down in real seconds, holds while a menu or a card pauses the game, writes back the scale it saved
            // (not Max(1, saved)) and lets go when another system has taken the scale.
            if (_hitStop.Tick(real, Time.timeScale, out float restore)) Time.timeScale = restore;
            _shake = Mathf.Max(0f, _shake - _shake * Profile.ShakeDecay * real - real * .01f);
            float dt = Time.deltaTime;
            // a target that died, left or was reborn before the head reached it ends the stroke (nothing is hit, nothing is closed)
            if (_combatOpen && !_impacted && !TargetStillThere()) EndCombat(true);
            if (_pose == null && _effect != null && _effect.IsReady) EnsurePose();   // warm-up may have come before the walker had a body
            if (_effect != null) _effect.Tick(dt);
            if (_combatOpen) StepCombat(dt);
            if (_pose != null) _pose.Weight = PoseWeight();
        }

        // Once, when the profile has its materials: the pool, the flake system, the volume, the arm pose. Never at a fire.
        void EnsureWarm()
        {
            if (_effect == null) _effect = new RiposteStroke308Effect();
            if (_effect.IsReady || Profile == null || Profile.Stroke == null) return;
            // the colour-drain volume goes on the layer of the scene's LookDriver308 (its own volume is evaluated by the camera, so that layer
            // is in the camera's volume mask); the walker's layer is not known to be in it. Without a driver: layer 0.
            var driver = FindFirstObjectByType<LookDriver308>();
            if (_effect.Warm(transform, Profile.Stroke, driver != null ? driver.gameObject.layer : 0)) { EnsurePose(); ResolveRig(); }
        }

        bool _poseResolved;

        void EnsurePose()
        {
            if (_pose != null || _poseResolved || !Profile.CastPose || Walker == null || Walker.Body == null) return;
            _poseResolved = true;   // once the body is known (a body that is not a human rig is not retried every frame)
            var animator = Walker.Body.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return;
            _pose = animator.GetComponent<Riposte301Pose>();
            if (_pose == null) _pose = animator.gameObject.AddComponent<Riposte301Pose>();
            _pose.Bind(animator);
        }

        void ResolveRig()
        {
            if (_rig != null) return;
            _rig = Walker != null ? Walker.GetComponentInChildren<PlayerVisualRig>(true) : null;
            if (_rig == null) _rig = FindFirstObjectByType<PlayerVisualRig>();
        }

        // presentation hook (#302 trailer cue): fire the stroke now if the same conditions as the released Q hold. It takes the same
        // window claim as the key does: a window answered by a drawing or an earlier stroke refuses it.
        public bool Fire() => Ready() && TryBegin(LockedTarget());

        // ---- IDrawPressArbiter (D308-147, plan C): called by the drawing controller, in this order for one Q gesture:
        //      BeginCandidate (key down) -> OnStrokeChosen (first stroke) | OnReleasedWithoutStroke (key up, no stroke) | CancelCandidate

        // The key went down. A candidate fixes the lock target and its window and consumes nothing.
        DrawPressMode IDrawPressArbiter.BeginCandidate()
        {
            _candidate.Cancel(); _candidateTarget = null;
            EnsureWarm();
            if (!isActiveAndEnabled || !Ready() || !StrokeReady) return DrawPressMode.None;
            var target = LockedTarget();
            // a candidate needs a real window nobody has answered yet (a drawing or a stroke in it refuses the candidate)
            if (!_candidate.Begin(KeyOf(target), CanRiposteAt(target))) return DrawPressMode.None;
            _candidateTarget = target;
            return Profile.DeferPresentationOnRiposteCandidate ? DrawPressMode.Deferred : DrawPressMode.Immediate;
        }

        // The candidate's first stroke: a drawing. This window takes no riposte from now on, whatever happens to the drawing.
        void IDrawPressArbiter.OnStrokeChosen()
        {
            _candidate.StrokeChosen(); _candidateTarget = null;
        }

        // The key came up with no stroke (the draw mode is left already). The candidate is checked again: the same lock, the same life
        // and opening still open, in reach, able to act. Anything else is a miss - never another target, never a drawing or a sword.
        bool IDrawPressArbiter.OnReleasedWithoutStroke()
        {
            if (!_candidate.Active) return false;
            var target = _candidateTarget; _candidateTarget = null;
            bool eligible = isActiveAndEnabled && target != null && LockedTarget() == target && Ready();
            if (!_candidate.ReleaseWithoutStroke(KeyOf(target), eligible)) return false;
            return TryBegin(target);
        }

        void IDrawPressArbiter.CancelCandidate() { _candidate.Cancel(); _candidateTarget = null; }

        // The window the enemy has open right now (none when it is closed or the enemy is gone).
        static RiposteWindowKey308 KeyOf(EnemyVitals target)
            => target == null || !target.IsAlive || !target.WeakPointActive ? default : new RiposteWindowKey308(target.GetInstanceID(), target.LifeRevision, target.WeakPointGeneration);

        bool Ready()
        {
            if (Profile == null || Walker == null || Walker.Motor == null || Walker.Body == null || Walker.Drawing == null || _combatOpen || Walker.Drawing.InDrawMode) return false;
            if (_lockOn == null) _lockOn = Walker.Motor.GetComponent<LockOn>();
            return true;
        }

        EnemyVitals LockedTarget() => _lockOn != null ? _lockOn.Target : null;

        public bool CanRiposte(out EnemyVitals target)
        {
            target = LockedTarget();
            return CanRiposteAt(target) && _candidate.Claims.CanOfferRiposte(KeyOf(target));
        }

        bool CanRiposteAt(EnemyVitals target)
        {
            if (target == null || !target.IsAlive || !target.WeakPointActive || Walker.Seated || !Walker.Motor.CanBeginDrawing) return false;
            return Vector3.Distance(target.transform.position, Walker.Body.transform.position) <= Profile.Range;
        }

        // The one door of a stroke (the released key and the Fire() cue both come through here): not while one is being built, and only
        // if this window has not been answered - the claim is taken last, after every check that can refuse.
        bool TryBegin(EnemyVitals target)
        {
            if (_beginning || !CanRiposteAt(target) || !StrokeReady) return false;
            var key = KeyOf(target);
            if (!_candidate.TryClaim(key)) return false;
            _beginning = true;
            try { return Launch(target, key); }
            finally { _beginning = false; }
        }

        bool Launch(EnemyVitals target, RiposteWindowKey308 key)
        {
            var aim = AimFor(target, out Collider collider);
            var slot = _effect.Begin(aim, target.transform, collider);
            if (slot == null) return false;
            _volleyKey = key; _slot = slot;
            _target = target; _targetLife = target.LifeRevision;
            _clock = 0f; _impactClock = 0f; _impacted = false; _combatOpen = true;
            Volleys++;
            return true;
        }

        // The torso (the target's collider centre; without one the root + 0.6 of its height), the eye and the brush tip of the active view.
        RiposteStroke308Aim AimFor(EnemyVitals target, out Collider collider)
        {
            var caster = Walker.Body.transform;
            collider = target.GetComponent<Collider>();
            if (collider == null) collider = target.GetComponentInChildren<Collider>();
            Vector3 torso;
            if (collider != null && collider.enabled && collider.bounds.size.y > .3f) torso = collider.bounds.center;
            else
            {
                collider = null;
                var renderer = target.GetComponentInChildren<Renderer>();
                float height = renderer != null && renderer.bounds.size.y > .3f ? renderer.bounds.size.y : 2f;
                torso = target.transform.position + Vector3.up * (height * .6f);
            }
            var camera = Camera.main;
            Vector3 eye = BillboardView != null ? BillboardView.position : camera != null ? camera.transform.position : caster.position + Vector3.up * 1.6f;
            if (_rig == null) ResolveRig();
            var tipTransform = _rig != null ? _rig.EffectTip : null;
            return new RiposteStroke308Aim
            {
                Caster = caster.position, Eye = eye, Torso = torso, TorsoRadius = target.BodyRadius,
                BrushTip = tipTransform != null ? tipTransform.position : default, HasBrushTip = tipTransform != null,
            };
        }

        bool TargetStillThere() => _target != null && _target.isActiveAndEnabled && _target.IsAlive && _target.LifeRevision == _targetLife;

        // After the effect's tick of this frame: a hit it raised is resolved in this same frame; later the window is closed by key.
        void StepCombat(float dt)
        {
            _clock += dt;
            if (!_impacted && _slot != null && _slot.ImpactReady) { _slot.ImpactReady = false; ResolveImpact(_slot); }
            if (_impacted && _clock >= _impactClock + Profile.Stroke.WindowCloseDelay) EndCombat(false);
        }

        /// <summary>The one hit: Profile.TotalDamage (45), once, on a living target of the same life. The weak-point window's 1.5 is
        /// EnemyVitals' (it judges the window as it is at this call) - never multiplied here. A melee provenance: it passes a guard.</summary>
        public bool ApplyImpact308(EnemyVitals target, uint life, float total, out EnemyDamageResult result)
        {
            result = default;
            if (target == null || !target.IsAlive || target.LifeRevision != life || !(total > 0f)) return false;
            result = target.TakeDamage(total, AttackProvenance.CreateMelee(this, DamageSource.PlayerDirect, null));   // #308 WP-16: melee passes a guard
            return true;
        }

        // The frame the revealed head crosses the torso: damage, reaction (EnemyVitals' own events), specks, the hit stop, the hook - together.
        void ResolveImpact(RiposteStroke308Effect.Slot slot)
        {
            _impacted = true; _impactClock = _clock;
            float amount = slot.Impact.TakeDamage(Profile.TotalDamage);
            bool hit = ApplyImpact308(_target, _targetLife, amount, out EnemyDamageResult result);
            _effect.Splash(slot);
            if (Profile.HitStopSeconds > 0f && _hitStop.Begin(Time.timeScale, Profile.HitStopScale, Profile.HitStopSeconds, out float scale)) Time.timeScale = scale;
            float shake = Profile.Stroke != null ? Profile.Stroke.PierceShake : 0f;
            if (shake > 0f) _shake = Mathf.Min(shake * 3f, _shake + shake);
            try { Pierced?.Invoke(slot.ImpactPoint, hit ? result : default); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }

        float PoseWeight()
        {
            if (!_combatOpen || _slot == null) return 0f;
            return RiposteStroke308Timeline.Pose(_clock, _slot.ImpactTime, Profile.PoseRaise, .15f, Profile.PoseLower);
        }

        // D308-147: the stroke closes only its own window (this life, this opening). A late close whose window has gone - or has been
        // replaced by the next one - changes nothing; the attack records on the enemy are not touched. A cancel (the target died, left or
        // was reborn before the pierce) closes nothing and hides the stroke at once.
        void EndCombat(bool cancel)
        {
            if (cancel) { if (_slot != null && _effect != null) _effect.Cancel(_slot); }
            else if (Profile.ConsumesWindow && _target != null) _target.TryConsumeWeakPointWindow(_volleyKey.Life, _volleyKey.Generation);
            _target = null; _slot = null; _combatOpen = false; _impacted = false;
            if (_pose != null) _pose.Weight = 0f;
        }

        // view shake: offset each game camera just for its render, restore right after (the camera rig never sees it).
        // Once at the pierce, and only when Stroke.PierceShake > 0 (it is 0 until the mp4 pass asks for it).
        readonly Dictionary<Camera, Vector3> _shaken = new Dictionary<Camera, Vector3>();

        void ShakeBegin(ScriptableRenderContext _, Camera camera)
        {
            if (_shake <= .0005f || camera.cameraType != CameraType.Game) return;
            float t = Time.time * 47f;
            var offset = new Vector3(Mathf.PerlinNoise(t, 1.3f) - .5f, Mathf.PerlinNoise(2.1f, t) - .5f, 0f) * 2f * _shake;
            _shaken[camera] = camera.transform.position;
            camera.transform.position += camera.transform.rotation * offset;
        }

        void ShakeEnd(ScriptableRenderContext _, Camera camera)
        {
            if (_shaken.TryGetValue(camera, out var pos)) { camera.transform.position = pos; _shaken.Remove(camera); }
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= ShakeBegin; RenderPipelineManager.endCameraRendering -= ShakeEnd;
            foreach (var kv in _shaken) if (kv.Key != null) kv.Key.transform.position = kv.Value;
            _shaken.Clear();
            if (_hitStop.Abort(Time.timeScale, out float restore)) Time.timeScale = restore;
            if (_effect != null) _effect.CancelAll();   // the strokes, and the colour drain goes to weight 0 and off
            _combatOpen = false; _impacted = false; _target = null; _slot = null; _candidate.Cancel(); _candidateTarget = null; _shake = 0f;
            if (_pose != null) _pose.Weight = 0f;
        }

        void OnDestroy()
        {
            if (_effect != null) { _effect.Dispose(!Application.isPlaying); _effect = null; }
        }
    }
}
