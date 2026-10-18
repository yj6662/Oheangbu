using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // [SPEC-SPELL-LAB-308 §2.4, D308-111] the lab's counterattack dummy: while armed it throws a small elemental ranged shot at
    // the player every few seconds, through the one enemy -> player doorway (EnemyStrike308.Deliver), so the ward ripple
    // (EAWardRuntime, Spec 11.43) and "no KTP when the player is hit" (D308-110) can be seen in the lab. The shot is a plain
    // dark ball (no emission, no trail); the damage lands when it arrives. Dev scene only - real enemies are untouched.
    public sealed class SpellLabStriker308 : MonoBehaviour
    {
        [SerializeField] private EnemyVitals _vitals;
        [SerializeField] private PlayerVitals _player;
        [SerializeField] private Transform _aim;            // the player's body (chest = +1.2 m)
        [SerializeField] private bool _armed;
        [SerializeField, Min(.5f)] private float _interval = 3f;
        [SerializeField, Min(0f)] private float _amount = 2f;
        [SerializeField, Min(1f)] private float _speed = 14f;

        private float _next;
        private Transform _shot;
        private Material _ink;
        private Vector3 _from, _to;
        private float _flight, _flown;
        private int _thrown, _landed;

        public bool Armed => _armed;
        public int Thrown => _thrown;
        public int Landed => _landed;

        public void Arm(bool on)
        {
            _armed = on;
            _next = Time.time + .5f;
            if (!on) DropShot();
        }

        private void Update()
        {
            if (_shot != null)
            {
                _flown += Time.deltaTime;
                float t = Mathf.Clamp01(_flown / _flight);
                _shot.position = Vector3.Lerp(_from, _to, t);
                if (t >= 1f)
                {
                    DropShot();
                    if (EnemyStrike308.Deliver(_player, _vitals, _amount, IncomingDamageKind.ElementalRanged)) _landed++;
                }
                return;
            }
            if (!_armed || _player == null || _aim == null || Time.time < _next) return;
            if (_vitals != null && !_vitals.IsAlive) return;
            _next = Time.time + _interval;
            Throw();
        }

        private void Throw()
        {
            _from = transform.position + Vector3.up * .9f;
            _to = _aim.position + Vector3.up * 1.2f;
            _flight = Mathf.Max(.05f, Vector3.Distance(_from, _to) / _speed);
            _flown = 0f;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Lab_StrikerShot";
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * .22f;
            go.transform.position = _from;
            var r = go.GetComponent<Renderer>();
            if (_ink == null) { _ink = new Material(r.sharedMaterial); _ink.color = new Color(.08f, .07f, .06f); }
            r.sharedMaterial = _ink;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _shot = go.transform;
            _thrown++;
        }

        private void DropShot()
        {
            if (_shot != null) Destroy(_shot.gameObject);
            _shot = null;
        }

        private void OnDisable() => DropShot();

        private void OnDestroy() { if (_ink != null) Destroy(_ink); }
    }
}
