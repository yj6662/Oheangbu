using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #303 body reactions on the Reaction303 animator layer (SPEC-PLAYER-FEEL-300 H). Listens to PlayerVitals.Damaged only
    // (presentation: no hit stun, no gameplay change). The hit direction comes from the nearest living enemy; a single hit of
    // LargeHitShare of max HP or more staggers. Death holds the fall until the rest-spot respawn plays the get-up.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(120)]
    public sealed class WorldMacroPlayerReaction303 : MonoBehaviour
    {
        public PlayerReaction303Profile Profile;
        public Animator Animator;
        public WorldMacroPlaytestSession Session;
        [Tooltip("Test hook (review harness): when set, this transform is the attacker for the hit direction.")]
        [System.NonSerialized] public Transform DebugThreat;

        PlayerVitals _vitals; PlayerMotor _motor;
        int _layer = -1; float _weight, _target, _releaseAt = -1f, _stateStarted; string _state;

        public bool Lying { get; private set; }
        public bool GettingUp => Profile != null && _state == Profile.GetUp && _target > 0f;
        public float GetUpProgress => Profile == null || _state != Profile.GetUp ? 1f : Mathf.Clamp01((Time.time - _stateStarted) / Mathf.Max(.1f, Profile.GetUpSeconds));
        public float Weight => _weight;
        public string State => _state;
        public int Hits { get; private set; }

        void OnEnable() { Bind(); }
        void OnDisable() { if (_vitals != null) _vitals.Damaged -= OnDamaged; _vitals = null; }

        void Bind()
        {
            if (Session == null) Session = FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (Animator == null) Animator = GetComponent<Animator>();
            _layer = Animator != null && Profile != null ? Animator.GetLayerIndex(Profile.Layer) : -1;
            var body = Session != null && Session.Walker != null ? Session.Walker.Body : null;
            var v = body != null ? body.GetComponent<PlayerVitals>() : null;
            if (v != _vitals) { if (_vitals != null) _vitals.Damaged -= OnDamaged; _vitals = v; if (_vitals != null) _vitals.Damaged += OnDamaged; }
            _motor = body != null ? body.GetComponent<PlayerMotor>() : null;
            // H.1 get-up camera: ensured at run time (precedent: WorldMacroCombatWalker adds Riposte301), so the three #303
            // scenes pick it up from this component and the shared profile with no scene save or re-promotion
            if (_getUpCamera == null) _getUpCamera = GetComponent<WorldMacroPlayerGetUpCamera303>();
            if (_getUpCamera == null && Profile != null && Application.isPlaying) _getUpCamera = gameObject.AddComponent<WorldMacroPlayerGetUpCamera303>();
            if (_getUpCamera != null) { _getUpCamera.Profile = Profile; _getUpCamera.Reaction = this; _getUpCamera.Session = Session; }
        }

        WorldMacroPlayerGetUpCamera303 _getUpCamera;
        public WorldMacroPlayerGetUpCamera303 GetUpCamera => _getUpCamera;

        void OnDamaged(float amount)
        {
            if (_layer < 0 || _vitals == null || _vitals.Hp01 <= 0f || Lying) return;   // the killing blow belongs to the death
            bool large = amount >= Profile.LargeHitShare * _vitals.MaxHp;
            string dir = Direction(out Vector3 from);
            Play(large ? Profile.HitLarge : dir, Profile.HitFadeIn, large ? Profile.LargeHitHold : Profile.HitHold);
            if (_lean == null) _lean = GetComponent<WorldMacroPlayerLean303>();
            if (_lean != null) _lean.Recoil(from, large ? 1.6f : 1f);   // the clip's flinch is subtle at play distance
            Hits++;
        }

        WorldMacroPlayerLean303 _lean;
        string Direction(out Vector3 from)
        {
            from = Vector3.forward;
            var body = Session != null && Session.Walker != null ? Session.Walker.Body.transform : transform;
            Transform threat = DebugThreat; float best = Profile.ThreatRadius;
            if (threat == null && Session != null && Session.Actors != null)
                foreach (var a in Session.Actors)
                {
                    if (a == null || !a.isActiveAndEnabled) continue;
                    var ev = a.GetComponent<EnemyVitals>(); if (ev != null && !ev.IsAlive) continue;
                    float d = Vector3.Distance(a.transform.position, body.position); if (d < best) { best = d; threat = a.transform; }
                }
            if (threat == null) return Profile.HitFront;
            var local = body.InverseTransformPoint(threat.position); from = local; float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            if (Mathf.Abs(angle) <= 50f) return Profile.HitFront;
            if (Mathf.Abs(angle) >= 130f) return Profile.HitBack;
            return angle > 0f ? Profile.HitRight : Profile.HitLeft;   // "From Right": the blow comes from the right
        }

        void Play(string state, float fade, float hold)
        {
            if (Animator == null || _layer < 0) return;
            Animator.CrossFadeInFixedTime(state, fade, _layer, 0f);
            _state = state; _stateStarted = Time.time; _releaseAt = hold > 0f ? Time.time + hold : -1f;
            bool moving = _motor != null && _motor.ActualLocalVelocity.magnitude > Profile.MovingSpeed;
            _target = moving && state != Profile.Death && state != Profile.GetUp ? Profile.MovingWeight : 1f;
        }

        public void BeginDeath()
        {
            if (_layer < 0) Bind(); if (_layer < 0) return;
            Lying = true; Play(Profile.Death, Profile.DeathFadeIn, -1f); _target = 1f;
        }

        public void BeginGetUp()
        {
            if (_layer < 0) Bind(); if (_layer < 0) { Lying = false; return; }
            Lying = false;
            Animator.Play(Profile.GetUp, _layer, 0f); _weight = 1f; Animator.SetLayerWeight(_layer, 1f);
            _state = Profile.GetUp; _stateStarted = Time.time; _target = 1f;
            _releaseAt = Time.time + Mathf.Max(.2f, Profile.GetUpSeconds - Profile.GetUpFadeOut);
        }

        public void Clear() { Lying = false; _target = 0f; _releaseAt = -1f; }

        void Update()
        {
            if (Profile == null) return;
            if (_layer < 0 || _vitals == null) { Bind(); if (_layer < 0) return; }
            if (_releaseAt > 0f && Time.time >= _releaseAt) { _target = 0f; _releaseAt = -1f; }
            float fade = _target > _weight ? Profile.HitFadeIn : (_state == Profile.GetUp ? Profile.GetUpFadeOut : Profile.HitFadeOut);
            _weight = Mathf.MoveTowards(_weight, _target, Time.deltaTime / Mathf.Max(.01f, fade));
            Animator.SetLayerWeight(_layer, _weight);
            if (_weight <= 0f && _target <= 0f && _state != null && !Lying) _state = null;
        }
    }
}
