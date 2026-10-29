using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>2026-10-30 초성 ㄱ(목) 24자, 경로 ㅏ template (first glyph 가; the user: "이제 'ㄱ' 술식부터 전체 진행하자" -> 90점 · 최대 6회차):
    /// a vine shot from the brush tip along the aim. Look-agnostic geometry; the look is the materials (tube / leaf / flash shaders).
    /// Strands are tubes rebuilt every tick into ONE mesh (only the visible span [tail, head] is built, the head tapers):
    ///   shot  - brush tip -> aim end (the hit point once a hit comes), a gentle S with a small helix wobble; it pulls in after the bind takes;
    ///   coil  - a helix round the struck body (the bind), grows on the hit, swells a little while it holds ("윤곽이 조금 자란다"), unravels at the end;
    ///   stem  - one short upright branch at the hit point ("착점 한 줄기");
    ///   roots - fine roots running forward out of the coil as it unravels ("잔뿌리로 풀림, 앞으로 뻗으며");
    ///   curls - small side tendrils on the shot.
    /// Vertex colour: r = glow (hit flash, shader adds light), g = strand kind / 4, b = along 0..1, a = fade. uv0 = (metres along, around 0..1).
    /// Leaves are cards at fixed places on the strands (uv0 0..1 card, colour like the tubes). The flash is a camera-facing card at the hit.
    /// Times are seconds from the launch (the scope clock via OnTick). The look only - nothing here touches a rule.</summary>
    public sealed class SpellFxVinePart : MonoBehaviour, ISpellFxPart
    {
        [SerializeField] Material tubeMaterial;
        [SerializeField] Material leafMaterial;
        [SerializeField] Material flashMaterial;
        [SerializeField] SpellFxBurstBody burstBody = SpellFxBurstBody.Wood;
        [Tooltip("What the bite becomes (the verb): 가 Coil (한 번) · 각 Cage (남긴다) · 간 Spread (번진다) · 감 Seed (심는다) · 갓 Cut (가른다) · 강 Return (돌아온다).")]
        [SerializeField] VineBite bite = VineBite.Coil;

        [Header("Shot")]
        [Tooltip("Visible head speed (m/s). The rule's path is faster (36 m/s); the head is snapped to the target when the hit comes first.")]
        [SerializeField] float headSpeed = 22f;
        [SerializeField] float shotRadius = 0.06f;
        [SerializeField] float sideWave = 0.28f;
        [SerializeField] float upArc = 0.15f;
        [SerializeField] float maxReach = 12f;
        [Tooltip("Seconds after the hit before the shot line starts to pull in toward the target, and how long that takes.")]
        [SerializeField] float pullAfter = 0.6f, pullSeconds = 0.35f;

        [Header("Bind")]
        [SerializeField] float coilRadius = 0.18f;
        [SerializeField] float coilTurns = 1.5f;
        [SerializeField] float coilHeight = 0.9f;
        [SerializeField] float coilGrowSeconds = 0.22f;
        [Tooltip("The bind's length (the rule: 2 s slow).")]
        [SerializeField] float holdSeconds = 2f;
        [SerializeField] float unravelSeconds = 0.6f;
        [SerializeField] float stemLength = 1.2f;
        [SerializeField] float rootLength = 0.8f;

        [Header("Leaves / flash")]
        [SerializeField] float leafSize = 0.16f;
        [SerializeField] float flashSize = 0.9f;
        [SerializeField] float flashSeconds = 0.28f;

        public enum VineBite { Coil, Cage, Spread, Seed, Cut, Return }

        enum Kind { Shot, Coil, Stem, Root, Curl, Cage, Jump, Pillar, Crack, Back }

        sealed class Strand
        {
            public Kind Kind;
            public System.Func<float, Vector3> At;      // 0..1 -> world point
            public float Length;                        // metres (approx)
            public float Radius;
            public float Head = 0f, Tail = 0f;          // visible span 0..1
            public float Glow, Fade = 1f;
            public float Seed;
        }

        sealed class Leaf { public Strand On; public float T; public float Side; public float Size; public float Seed; }

        readonly List<Strand> _strands = new List<Strand>();
        readonly List<Leaf> _leaves = new List<Leaf>();
        GameObject _tubeGo, _leafGo, _flashGo;
        Mesh _tube, _leafMesh, _flashMesh;
        Material _tubeMat, _leafMat, _flashMat;
        readonly List<Vector3> _v = new List<Vector3>(); readonly List<Vector3> _n = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>(); readonly List<Color> _c = new List<Color>(); readonly List<int> _t = new List<int>();

        Vector3 _start, _aimEnd, _end, _side;
        Strand _shot, _coil, _stem;
        readonly List<Strand> _roots = new List<Strand>();
        bool _launched, _hit, _over;
        float _launchAge, _hitAge = -1f, _age;
        Vector3 _hitPos, _hitNormal, _target;
        float _targetRadius = 0.4f;
        Transform _targetTr;
        Vector3 _targetOffset;
        System.Random _rng;

        // ------------------------------------------------------------------ ISpellFxPart

        public void OnBegin(SpellFxCast cast)
        {
            _rng = new System.Random((int)(cast.CastId & 0x7fffffff));
            if (_tubeGo == null) Build();
        }

        public void OnLaunch(SpellFxCast cast)
        {
            _launched = true; _launchAge = 0f; _origin = cast.Origin;
            Vector3 fwd = cast.Forward; fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
            _side = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3? tip = cast.BrushTip != null ? cast.BrushTip() : null;
            _start = tip ?? (cast.Origin + Vector3.up * 1.35f + fwd * 0.4f + _side * 0.25f);
            _start.y = Mathf.Min(_start.y, cast.Origin.y + 1.75f);            // r3 (Fable r2 #6): the arc leaves at the shoulder, not a metre over the head
            float reach = Mathf.Min(maxReach, cast.Spec != null && cast.Spec.PathLength > 0.5f ? cast.Spec.PathLength : maxReach);
            _aimEnd = cast.Origin + fwd * reach + Vector3.up * 1.0f;
            _end = _aimEnd;
            float sgn = Rand() < 0.5f ? -1f : 1f;
            _shotSign = sgn;
            _shot = Add(Kind.Shot, t => ShotPoint(t, sgn), shotRadius);
            _shot.Length = Vector3.Distance(_start, _end);
            // two small curls on the shot, sprouting as the head passes
            for (int k = 0; k < 2; k++)
            {
                float at = 0.35f + 0.25f * k; float dir = k == 0 ? 1f : -1f; var seed = Rand();
                var curl = Add(Kind.Curl, t => CurlPoint(at, dir, seed, t), shotRadius * 0.45f);
                curl.Length = 0.4f; curl.Seed = at;
            }
            // r2 (Fable r1 #5): fewer, smaller leaves along the path and none at the brush
            for (int i = 0; i < 4; i++) _leaves.Add(new Leaf { On = _shot, T = 0.28f + 0.18f * i, Side = (i & 1) == 0 ? 1f : -1f, Size = leafSize * 0.6f * (0.85f + 0.3f * Rand()), Seed = Rand() });
            if (_tubeGo != null) { _tubeGo.SetActive(true); _leafGo.SetActive(true); }
        }

        /// <summary>The rule's path (36 m/s) reaches the body before the drawn head does: the hit is held until the head arrives at the struck body, then the bite plays.</summary>
        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            if (!_launched) return;
            if (_hit || _pending)
            {
                // later contacts: 간 spreads to the next bodies (a jump each); 감 the seed's activation binds; 강 the return touch (drawn by the back strand)
                if (bite == VineBite.Spread && hit.TargetId != _pendingHit.TargetId) _jumpQueue.Add(hit);
                else if (bite == VineBite.Seed && _hit && _coil == null) _activate = true;
                return;
            }
            _pending = true; _pendingHit = hit;
            _targetTr = hit.Target; _target = hit.Position;
            _targetOffset = _targetTr != null ? _target - _targetTr.position : Vector3.zero;
            _targetRadius = Mathf.Clamp(hit.TargetRadius, 0.25f, 0.9f);
            Vector3 toCaster = _start - _target; toCaster.y = 0f; toCaster = toCaster.sqrMagnitude > 1e-4f ? toCaster.normalized : -transform.forward;
            _end = _target + toCaster * (_targetRadius + coilRadius * 0.5f);
            if (_shot != null) _shot.Length = Vector3.Distance(_start, _end);
        }

        bool _pending;
        SpellFxHit _pendingHit;
        readonly List<SpellFxHit> _jumpQueue = new List<SpellFxHit>();
        readonly List<Strand> _cage = new List<Strand>(), _jumps = new List<Strand>(), _marks = new List<Strand>();
        Strand _pillar, _crack, _back;
        bool _activate, _matLive;
        float _matEndAge = -1f, _activateAge = -1f;
        Vector3 _feet;
        float _shotSign = 1f, _flashAge = -1f, _returnAge = -1f;
        float _feetBelow = 0.5f, _coilR = 0.5f;
        Vector3 _origin;

        float GroundY(Vector3 above, Transform body)
        {
            var hits = Physics.RaycastAll(above + Vector3.up * 0.5f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (body != null && h.transform.IsChildOf(body.root)) continue;
                if (h.point.y < above.y && h.point.y > best) best = h.point.y;
            }
            return best > float.NegativeInfinity ? best : _origin.y;
        }

        /// <summary>The chest: how far above the feet the bind's middle sits (half again the body's centre, at least 0.5 m).</summary>
        float ChestAbove() => Mathf.Max(0.5f, _feetBelow * 1.15f);

        /// <summary>A material of this cast (각's cage wood, 감's waiting seed) keeps the bind standing until it ends.</summary>
        public void OnMaterial(SpellFxCast cast, SpellFxMaterial m)
        {
            if (m.Phase == SpellFxMaterialPhase.Created) { _matLive = true; _matEndAge = -1f; }
            else { _matLive = false; _matEndAge = _age; }
        }

        /// <summary>Seconds after the bite when the bind starts to unravel.</summary>
        float HoldEnd()
        {
            if (bite == VineBite.Cage || bite == VineBite.Seed)
            {
                if (_matLive) return float.MaxValue;
                if (_matEndAge >= 0f) return Mathf.Max(holdSeconds * 0.75f, _matEndAge - _hitAge);
            }
            if (bite == VineBite.Seed && _activateAge >= 0f) return _activateAge - _hitAge + holdSeconds;
            if (bite == VineBite.Seed) return float.MaxValue;
            return holdSeconds;
        }

        void Bite(SpellFxHit hit)
        {
            _hit = true; _hitAge = _age;
            _targetTr = hit.Target;
            _target = hit.Position;
            _targetOffset = _targetTr != null ? _target - _targetTr.position : Vector3.zero;
            _targetRadius = Mathf.Clamp(hit.TargetRadius, 0.25f, 0.9f);
            Vector3 toCaster = _start - _target; toCaster.y = 0f; toCaster = toCaster.sqrMagnitude > 1e-4f ? toCaster.normalized : -transform.forward;
            _hitNormal = toCaster;
            // r2 (각 / 갓 drew from the bear's back: the registry's position is not the ground): the ground under the body, by a ray that skips the body itself,
            // else the cast point's height (the caster stands on it)
            _feet = hit.Feet; _feet.y = GroundY(_target, hit.Target);
            _feetBelow = Mathf.Max(0.2f, _target.y - _feet.y);
            // r2 (Fable r1 #4): the bind sits on the chest, tight to the body (radius ~0.55 of its width), not a wide ring at the feet
            _coilR = _targetRadius * 0.75f + 0.05f;
            _hitPos = _target + toCaster * _coilR + Vector3.up * (ChestAbove() - _feetBelow);
            _end = _hitPos;
            if (_shot != null) { _shot.Length = Vector3.Distance(_start, _end); _shot.Head = 1f; }
            switch (bite)
            {
                case VineBite.Cage: SpawnCage(); break;
                case VineBite.Seed: SpawnSeed(); break;
                case VineBite.Return: SpawnCoil(0.45f); SpawnBack(); break;
                case VineBite.Cut: SpawnCrack(); SpawnCoil(1f); SpellFxLabShot.InkBurst(_target, toCaster, SpellFxBurstBody.Metal); break;
                default: SpawnCoil(bite == VineBite.Spread ? 0.75f : 1f); break;
            }
            if (bite != VineBite.Seed && bite != VineBite.Cage)
            {
                // r2 (Fable r1 #3 / #6): "착점 한 줄기" = one ink line on the GROUND at the hit (1.2 m along the path, 2 s), not an upright stem on the body
                Vector3 dir = _target - _start; dir.y = 0f; dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward;
                Vector3 g0 = _feet + Vector3.up * 0.015f - dir * (_targetRadius + 0.3f + stemLength); float gs = Rand();     // r3 (Fable r2 #4): in front of the body, on the camera side
                _stem = Add(Kind.Crack, t => g0 + dir * (stemLength * t) + Vector3.Cross(Vector3.up, dir) * ((Mathf.PerlinNoise(t * 9f, gs * 7f) - 0.5f) * 0.05f), shotRadius * 0.9f);
                _stem.Length = stemLength;
            }
            // r2 (Fable r1 #3): the bite is an ink splash (black drops), a few leaves with it
            SpellFxLabShot.InkBurst(_hitPos, toCaster, SpellFxBurstBody.Water);          // r3 (Fable r2 #5): ink only - the wood burst's leaf sprites read as green stars
            if (_flashGo != null) { _flashGo.SetActive(true); _flashGo.transform.position = _hitPos; }
        }

        public void OnTick(SpellFxCast cast, float ageSeconds)
        {
            _age = ageSeconds;
            if (!_launched || _tubeGo == null) return;
            if (_targetTr != null) _target = _targetTr.position + _targetOffset;
            Animate();
            Rebuild();
        }

        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason)
        {
            if (reason == SpellFxEndReason.Aborted) { _over = true; if (_tubeGo != null) { _tubeGo.SetActive(false); _leafGo.SetActive(false); _flashGo.SetActive(false); } }
        }

        void OnDestroy()
        {
            if (_tube != null) Destroy(_tube); if (_leafMesh != null) Destroy(_leafMesh); if (_flashMesh != null) Destroy(_flashMesh);
            if (_tubeMat != null) Destroy(_tubeMat); if (_leafMat != null) Destroy(_leafMat); if (_flashMat != null) Destroy(_flashMat);
        }

        // ------------------------------------------------------------------ timeline

        void Animate()
        {
            float a = _age;
            float travel = Mathf.Max(0.08f, Vector3.Distance(_start, _end) / headSpeed);
            if (_shot != null)
            {
                if (!_hit)
                {
                    _shot.Head = Mathf.Clamp01(a / travel);
                    if (_pending && _shot.Head >= 1f) Bite(_pendingHit);
                }
                if (_hit)
                {
                    float hs = a - _hitAge;
                    if (bite != VineBite.Return) _shot.Tail = Smooth((hs - pullAfter) / pullSeconds);
                    _shot.Glow = Flash(hs) * 0.5f;
                }
                else if (!_pending && a > travel + 0.6f)
                {
                    // no hit: the shot reached its end and unravels where it is
                    float u = (a - travel - 0.6f) / unravelSeconds;
                    _shot.Tail = Smooth(u); _shot.Fade = 1f - Smooth(u - 0.4f);
                }
            }
            foreach (var s in _strands)
            {
                if (s.Kind == Kind.Curl) { s.Head = _shot != null ? Smooth((_shot.Head - s.Seed) / 0.18f) : 0f; s.Tail = _shot != null ? Smooth((_shot.Tail - s.Seed) / 0.1f) : 0f; s.Glow = _shot != null ? _shot.Glow : 0f; s.Fade = _shot != null ? _shot.Fade : 1f; }
            }
            if (_hit)
            {
                float hs = a - _hitAge;
                if (bite == VineBite.Seed && _activate && _coil == null) { _activateAge = a; SpawnCoil(1f); _flashAge = a; SpellFxLabShot.InkBurst(_hitPos, _hitNormal, SpellFxBurstBody.Water); }
                while (_jumpQueue.Count > 0) { SpawnJump(_jumpQueue[0]); _jumpQueue.RemoveAt(0); }
                float endAt = HoldEnd();
                float u = (hs - endAt) / unravelSeconds;
                float fadeOut = u > 0f ? 1f - Smooth(u - 0.5f) : 1f;
                if (_coil != null)
                {
                    float cs = bite == VineBite.Seed ? a - _activateAge : hs;
                    _coil.Head = Smooth((cs - HitStop) / coilGrowSeconds);              // r2 (Fable r1 #3): a held beat (hit-stop) before it winds
                    _coil.Glow = Flash(cs) + 0.15f * Mathf.Clamp01(1f - cs / 1.2f);
                    _coil.Radius = shotRadius * (1.0f + 0.3f * Smooth(cs / Mathf.Min(endAt, 3f)));         // swells x1.3 while it holds ("윤곽이 조금 자란다")
                    // r2 (Fable r1 #2): it does not snap away - it dissolves into ink while the roots run on
                    if (u > 0f) { _coil.Tail = Smooth(u) * 0.35f; _coil.Fade = 1f - Smooth(u * 1.1f); }
                }
                if (_stem != null)
                {
                    _stem.Head = Smooth((hs - HitStop) / 0.18f);
                    _stem.Glow = Flash(hs) * 0.6f;
                    float su = (hs - 2f) / 0.5f;                                         // the ground line: 2 s
                    if (su > 0f) _stem.Fade = 1f - Smooth(su);
                }
                foreach (var s in _cage)
                {
                    s.Head = Smooth((hs - s.Seed) / 0.75f);           // the cage grows over the bind (1.5 s cap: it is closed well before)
                    s.Glow = Flash(hs - s.Seed) * 0.7f + 0.1f;
                    if (u > 0f) { s.Tail = Smooth(u - s.Seed); s.Fade = fadeOut; }
                }
                if (_pillar != null)
                {
                    _pillar.Head = Smooth(hs / 0.35f);
                    _pillar.Glow = 0.85f + 0.15f * Mathf.Sin(hs * 5f);       // the waiting seed breathes (r1: bright - a black rod read as a pole)
                    if (_activateAge >= 0f) { float k = Smooth((a - _activateAge) / 0.3f); _pillar.Tail = k; }
                    if (u > 0f) _pillar.Fade = fadeOut;
                }
                if (_crack != null)
                {
                    _crack.Head = Smooth(hs / 0.12f);                         // the cut is already there when the head arrives - it races ahead
                    _crack.Glow = Flash(hs);
                    float cu = (hs - 1.8f) / 0.6f; if (cu > 0f) { _crack.Tail = Smooth(cu); _crack.Fade = 1f - Smooth(cu - 0.4f); }
                }
                if (_back != null)
                {
                    float bs = hs - 0.12f;
                    _back.Head = Mathf.Clamp01(bs * headSpeed / Mathf.Max(0.5f, _back.Length));
                    _back.Glow = Flash(hs) * 0.5f;
                    if (_back.Head >= 1f && _returnAge < 0f) { _returnAge = a; _flashAge = a; }
                    if (_returnAge >= 0f)
                    {
                        float rs = a - _returnAge;
                        _back.Tail = Smooth((rs - 0.25f) / 0.35f);
                        if (_shot != null) { _shot.Tail = Smooth((rs - 0.1f) / 0.35f); }
                    }
                }
                foreach (var s in _jumps)
                {
                    float js = a - s.Seed;
                    s.Head = s.Kind == Kind.Jump ? Smooth(js / 0.18f) : Smooth((js - 0.15f) / coilGrowSeconds);
                    s.Glow = Flash(js - (s.Kind == Kind.Jump ? 0.15f : 0f));
                    float ju = (js - holdSeconds) / unravelSeconds;
                    if (s.Kind == Kind.Jump) { float jt = (js - 0.5f) / 0.3f; if (jt > 0f) s.Tail = Smooth(jt); }
                    if (ju > 0f) { s.Tail = Mathf.Max(s.Tail, Smooth(ju)); s.Fade = 1f - Smooth(ju - 0.5f); }
                }
                foreach (var s in _marks)
                {
                    if (s.Seed < 0f) { s.Head = Smooth((hs - 0.1f) / 0.3f); if (u > 0f) s.Fade = fadeOut; continue; }
                    float ms = a - s.Seed;
                    s.Head = Smooth((ms - 0.2f) / 0.4f);
                    float mu = (ms - 3f) / 0.5f; if (mu > 0f) { s.Tail = Smooth(mu); s.Fade = 1f - Smooth(mu - 0.5f); }     // the spread's mark: 3 s
                }
                if (u > 0f && _coil != null)
                {
                    if (_roots.Count == 0) SpawnRoots();
                    foreach (var r in _roots) { r.Head = Smooth(u * 1.6f); r.Fade = 1f - Smooth((u - 0.45f) / 0.55f); }
                }
                if (_flashGo != null)
                {
                    float f = Mathf.Max(Flash(hs), _flashAge >= 0f ? Flash(a - _flashAge) : 0f);
                    if (bite == VineBite.Seed && _activateAge < 0f) f *= 0.5f;
                    _flashGo.SetActive(f > 0.01f);
                    if (f > 0.01f)
                    {
                        var cam = Camera.main;
                        _flashGo.transform.position = _hitPos;
                        if (cam != null) _flashGo.transform.rotation = Quaternion.LookRotation(_flashGo.transform.position - cam.transform.position, Vector3.up);
                        _flashGo.transform.localScale = Vector3.one * flashSize * (0.6f + 0.6f * Mathf.Clamp01(hs / flashSeconds));
                        _flashMat.SetFloat("_Flash", f);
                        _flashMat.SetFloat("_Seed", _hitAge);
                    }
                }
            }
        }

        const float HitStop = 0.05f;
        float Flash(float hs) => hs < 0f ? 0f : Mathf.Clamp01(1f - hs / flashSeconds) * Mathf.Clamp01(hs / 0.02f + 0.5f);
        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        float Rand() => (float)_rng.NextDouble();

        void SpawnRoots()
        {
            Vector3 fwd = _target - _start; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            // r2 (Fable r1 #2): four fine roots leave the bind and run forward past the body, down to the ground, as it dissolves
            float floor = _target.y - _feetBelow + 0.02f;
            // r3 (Fable r2 #3): from the coil's FRONT arc (the camera side) toward the viewer and down, where the camera sees them
            for (int k = 0; k < 4; k++)
            {
                float spread = (k - 1.5f) * 0.45f, seed = Rand();
                Vector3 from = _target + _hitNormal * _coilR + side * spread * _coilR * 0.8f + Vector3.up * (ChestAbove() - _feetBelow - 0.1f);
                Vector3 dir = (_hitNormal * 0.8f + side * spread * 0.8f).normalized;
                var root = Add(Kind.Root, t =>
                {
                    Vector3 p = from + dir * (t * rootLength) + Vector3.down * (0.9f * t);
                    p += side * Mathf.Sin(t * 9f + seed * 6f) * 0.05f * t;
                    if (p.y < floor) p.y = floor;
                    return p;
                }, shotRadius * 0.3f);
                root.Length = rootLength; _roots.Add(root);
            }
        }

        // ------------------------------------------------------------------ the verbs' bites

        void SpawnCoil(float turnScale)
        {
            Vector3 toCaster = _hitNormal;
            float r = _coilR;
            float phase0 = Mathf.Atan2(Vector3.Dot(toCaster, Vector3.forward), Vector3.Dot(toCaster, Vector3.right));
            float turns = coilTurns * turnScale;
            _coil = Add(Kind.Coil, t => CoilPoint(t, r, phase0, turns), shotRadius * 1.25f);
            _coil.Length = turns * 2f * Mathf.PI * r;
            int n = Mathf.Max(2, Mathf.RoundToInt(3 * turnScale));     // r3 (Fable r2 #1): two or three leaves on the bind
            for (int i = 0; i < n; i++) _leaves.Add(new Leaf { On = _coil, T = 0.08f + 0.85f * i / n, Side = (i & 1) == 0 ? 1f : -1f, Size = leafSize * (0.9f + 0.5f * Rand()), Seed = Rand() });
        }

        /// <summary>각: branches rise from the ground round the body and arch over it into a cage; the tips curl on above the top ("위로 솟는 가지 완성형").</summary>
        void SpawnCage()
        {
            // r2 (my look: wide and low it read as spider legs from the camera): narrow and tall - a cage, not a dome
            const int n = 9;
            float r = _targetRadius * 0.8f + 0.2f;
            float top = Mathf.Max(1.7f, _feetBelow * 2f + 0.9f);
            for (int i = 0; i < n; i++)
            {
                float a0 = i / (float)n * Mathf.PI * 2f + Rand() * 0.3f, seed = Rand();
                Vector3 dir = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                // r1 (my look: the branches splayed out like legs): rise nearly upright, then bow INWARD to meet over the body
                Vector3 p0 = _feet + dir * r, apex = _feet + Vector3.up * (top * 1.05f) + dir * 0.06f;
                Vector3 ctrl = _feet + dir * (r * 0.95f) + Vector3.up * (top * 1.05f);
                var s = Add(Kind.Cage, t =>
                {
                    if (t < 0.82f)
                    {
                        float u = t / 0.82f;
                        Vector3 a = Vector3.Lerp(p0, ctrl, u), b = Vector3.Lerp(ctrl, apex, u);
                        return Vector3.Lerp(a, b, u) + Vector3.Cross(Vector3.up, dir) * Mathf.Sin(u * 9f + seed * 6f) * 0.04f;
                    }
                    float w = (t - 0.82f) / 0.18f;           // the tip curls on, upward ("위로 솟는 가지")
                    return apex + dir * (0.07f * w) + Vector3.up * (0.4f * w) + Vector3.Cross(Vector3.up, dir) * (Mathf.Sin(w * 3.5f) * 0.08f);
                }, shotRadius * 1.1f);
                s.Length = top * 1.6f; s.Seed = i * 0.06f;
                _cage.Add(s);
                for (int k = 0; k < 2; k++) _leaves.Add(new Leaf { On = s, T = 0.35f + 0.45f * k + 0.1f * Rand(), Side = (k & 1) == 0 ? 1f : -1f, Size = leafSize * (1f + 0.5f * Rand()), Seed = Rand() });
            }
        }

        /// <summary>감: a seed lodged at the hit with a thin pillar of light-ink standing over it ("세로 기둥 · 점"); it waits.</summary>
        void SpawnSeed()
        {
            Vector3 at = _hitPos;
            _pillar = Add(Kind.Pillar, t => at + Vector3.up * (t * 1.6f) + _side * (Mathf.Sin(t * 5f) * 0.02f), shotRadius * 0.35f);
            _pillar.Length = 1.6f;
            for (int k = 0; k < 2; k++)
            {
                float dir = k == 0 ? 1f : -1f, seed = Rand();
                var c = Add(Kind.Curl, t => { float ang = t * 4f; return at + _hitNormal * 0.05f + _side * dir * (Mathf.Sin(ang) * 0.12f) + Vector3.up * ((1f - Mathf.Cos(ang)) * 0.09f + t * 0.1f); }, shotRadius * 0.5f);
                c.Length = 0.35f; c.Seed = -1f;
                _marks.Add(c);
                _leaves.Add(new Leaf { On = c, T = 0.85f, Side = dir, Size = leafSize * 0.9f, Seed = seed });
            }
        }

        /// <summary>갓: a straight crack carved into the ground along the path, from the caster's feet through the body and on ("아래로 파인 균열선").</summary>
        void SpawnCrack()
        {
            Vector3 a = _origin, b = _feet; a.y = b.y = _feet.y + 0.015f;           // r1: the cast point (the prefab root is not where the caster stands)
            Vector3 dir = b - a; float len = dir.magnitude + 1.6f; dir.y = 0f; dir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, dir); float seed = Rand();
            _crack = Add(Kind.Crack, t => a + dir * (len * t) + side * ((Mathf.PerlinNoise(t * 18f, seed * 10f) - 0.5f) * 0.09f), shotRadius * 0.7f);
            _crack.Length = len;
        }

        /// <summary>강: the head turns at the body and comes back to the brush along the other side - one closed loop ("닫힌 곡선").</summary>
        void SpawnBack()
        {
            float sgn = -_shotSign;
            _back = Add(Kind.Back, t =>
            {
                // r1 (my look: it swung past the camera as a black slab): a high, narrow return arc on the other side, ending at the brush
                Vector3 p = Vector3.Lerp(_hitPos, _start, t);
                float bell = Mathf.Sin(Mathf.PI * t);
                return p + _side * (sgn * sideWave * 1.3f * bell) + Vector3.up * (0.75f * bell);
            }, shotRadius);
            _back.Length = Vector3.Distance(_hitPos, _start) * 1.15f;
            for (int i = 0; i < 4; i++) _leaves.Add(new Leaf { On = _back, T = 0.2f + 0.18f * i, Side = (i & 1) == 0 ? 1f : -1f, Size = leafSize * (0.8f + 0.4f * Rand()), Seed = Rand() });
        }

        /// <summary>간: a thinner vine hops from the first body to the next one (3 m), coils it, and leaves a short flat mark on the ground there (3 s).</summary>
        void SpawnJump(SpellFxHit hit)
        {
            Vector3 from = _target, to = hit.Position; float r = Mathf.Clamp(hit.TargetRadius, 0.25f, 0.9f) + coilRadius * 0.8f;
            Transform tr = hit.Target; Vector3 off = tr != null ? to - tr.position : Vector3.zero;
            System.Func<Vector3> now = () => tr != null ? tr.position + off : to;
            var j = Add(Kind.Jump, t =>
            {
                Vector3 p = Vector3.Lerp(from, now(), t);
                return p + Vector3.up * (Mathf.Sin(Mathf.PI * t) * 0.7f);
            }, shotRadius * 0.75f);
            j.Length = Vector3.Distance(from, to) + 0.5f; j.Seed = _age;
            _jumps.Add(j);
            float phase0 = Rand() * 6.28f;
            var c = Add(Kind.Coil, t => { float ang = phase0 + t * 1.8f * 2f * Mathf.PI; return now() + new Vector3(Mathf.Cos(ang) * r, Mathf.Lerp(0.2f, -0.45f, t) * coilHeight, Mathf.Sin(ang) * r); }, shotRadius);
            c.Length = 1.8f * 2f * Mathf.PI * r; c.Seed = _age;
            _jumps.Add(c);
            Vector3 feet = hit.Feet; feet.y = GroundY(hit.Position, hit.Target) + 0.02f; float s0 = Rand();
            var mark = Add(Kind.Root, t => { float ang = s0 * 6.28f + t * 5f; float rr = 0.25f + 0.45f * t; return feet + new Vector3(Mathf.Cos(ang) * rr, 0f, Mathf.Sin(ang) * rr); }, shotRadius * 0.55f);
            mark.Length = 2.2f; mark.Seed = _age;
            _marks.Add(mark);
            for (int i = 0; i < 4; i++) _leaves.Add(new Leaf { On = c, T = 0.15f + 0.2f * i, Side = (i & 1) == 0 ? 1f : -1f, Size = leafSize, Seed = Rand() });
            SpellFxLabShot.InkBurst(to, (from - to).normalized, SpellFxBurstBody.Water);
        }

        // ------------------------------------------------------------------ curves

        Vector3 ShotPoint(float t, float sgn)
        {
            Vector3 p = Vector3.Lerp(_start, _end, t);
            float bell = Mathf.Sin(Mathf.PI * t);
            p += _side * (sgn * sideWave * Mathf.Sin(Mathf.PI * 2f * t) * bell);
            p += Vector3.up * (upArc * bell);
            // a small helix wobble that dies at both ends
            float w = 0.05f * bell;
            p += _side * Mathf.Cos(t * 22f) * w + Vector3.up * Mathf.Sin(t * 22f) * w;
            return p;
        }

        Vector3 CurlPoint(float at, float dir, float seed, float t)
        {
            if (_shot == null) return _start;
            Vector3 b = ShotPoint(at, 1f);
            Vector3 tan = (ShotPoint(at + 0.01f, 1f) - b).normalized;
            Vector3 n = Vector3.Cross(tan, Vector3.up).normalized * dir;
            float ang = t * (3.6f + seed);
            float rad = 0.16f * (1f - 0.6f * t);
            return b + n * (Mathf.Sin(ang) * rad + t * 0.12f) + Vector3.up * ((1f - Mathf.Cos(ang)) * rad * 0.8f) + tan * (t * 0.1f);
        }

        Vector3 CoilPoint(float t, float r, float phase0, float turns)
        {
            float ang = phase0 + t * turns * 2f * Mathf.PI;
            // round the chest: from a little above it down over the body (heights above the feet, which follow the body)
            float chest = ChestAbove();
            float y = Mathf.Lerp(chest + 0.25f * coilHeight, Mathf.Max(0.15f, chest - 0.5f * coilHeight), t) - _feetBelow;
            float rr = r * (1f + 0.08f * Mathf.Sin(t * 19f));
            y += Mathf.Sin(ang + 0.7f) * rr * 0.27f;                       // r3 (Fable r2 #1): the loops tilt ±15° so the front arc bends, not a straight bar
            return _target + new Vector3(Mathf.Cos(ang) * rr, y, Mathf.Sin(ang) * rr);
        }

        Vector3 StemPoint(float t)
        {
            Vector3 up = Vector3.up, out_ = _hitNormal;
            float curl = t * t;
            return _hitPos + out_ * (0.12f * t + 0.1f * curl) + up * (stemLength * t) + _side * (Mathf.Sin(t * 4f) * 0.06f * t);
        }

        Strand Add(Kind k, System.Func<float, Vector3> at, float radius)
        {
            var s = new Strand { Kind = k, At = at, Radius = radius, Seed = Rand() };
            _strands.Add(s);
            return s;
        }

        // ------------------------------------------------------------------ meshes

        void Build()
        {
            int layerId = VfxAfterFog308.Layer;
            // r1: the tubes write depth (alpha-tested ink): they stay on the default layer - on the after-fog layer they skip InkWash297 and drew as flat mint (VfxAfterFog308 note)
            _tubeGo = MakeChild("VineTubes", tubeMaterial, out _tube, out _tubeMat, -1);
            _leafGo = MakeChild("VineLeaves", leafMaterial, out _leafMesh, out _leafMat, layerId);
            _flashGo = MakeChild("VineFlash", flashMaterial, out _flashMesh, out _flashMat, layerId);
            // the flash: one quad in local XY
            _flashMesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            _flashMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            _flashMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            _flashMesh.RecalculateBounds();
            _tubeGo.transform.SetParent(null, true); _leafGo.transform.SetParent(null, true);      // world-space meshes (kept as our children below for cleanup)
            _tubeGo.transform.SetParent(transform, true); _leafGo.transform.SetParent(transform, true);
            _tubeGo.SetActive(false); _leafGo.SetActive(false); _flashGo.SetActive(false);
        }

        GameObject MakeChild(string name, Material src, out Mesh mesh, out Material mat, int layerId)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            if (layerId >= 0) go.layer = layerId;
            mesh = new Mesh { name = name }; mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            mat = src != null ? new Material(src) : null;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go;
        }

        const int Around = 8;

        void Rebuild()
        {
            // tubes are written in world space: the child sits at identity under a root that may have moved, so write local = inverse of its transform
            var toLocal = _tubeGo.transform.worldToLocalMatrix;
            _v.Clear(); _n.Clear(); _uv.Clear(); _c.Clear(); _t.Clear();
            foreach (var s in _strands)
            {
                if (s.Head - s.Tail < 0.002f || s.Fade <= 0.002f) continue;
                int segs = Mathf.Clamp(Mathf.CeilToInt(s.Length * (s.Head - s.Tail) * 14f), 4, 90);
                Vector3 prevT = Vector3.zero, normal = Vector3.zero;
                int baseV = _v.Count;
                for (int i = 0; i <= segs; i++)
                {
                    float u = Mathf.Lerp(s.Tail, s.Head, i / (float)segs);
                    Vector3 p = s.At(u), q = s.At(Mathf.Min(1f, u + 0.01f)), pp = s.At(Mathf.Max(0f, u - 0.01f));
                    Vector3 tan = (q - pp); if (tan.sqrMagnitude < 1e-8f) tan = prevT; tan.Normalize();
                    if (i == 0) { normal = Vector3.Cross(tan, Vector3.up); if (normal.sqrMagnitude < 1e-4f) normal = Vector3.Cross(tan, Vector3.right); normal.Normalize(); }
                    else { normal = Vector3.ProjectOnPlane(normal, tan).normalized; }          // parallel transport (no twist)
                    Vector3 bin = Vector3.Cross(tan, normal);
                    prevT = tan;
                    // taper: the head comes to a point, the tail thins as it pulls in
                    float headTaper = Mathf.Clamp01((s.Head - u) / Mathf.Max(0.02f, 0.25f / Mathf.Max(0.5f, s.Length)));
                    float tailTaper = s.Tail > 0f ? Mathf.Clamp01((u - s.Tail) / 0.08f) : 1f;
                    float startTaper = s.Kind == Kind.Shot ? Mathf.Lerp(0.25f, 1f, Smooth(u / 0.12f)) : s.Kind == Kind.Back ? Mathf.Lerp(1f, 0.25f, Smooth((u - 0.6f) / 0.4f)) : 1f;   // r2 (Fable r1 #5): no plank at the brush (강: nor where it comes home)
                    float rootTaper = s.Kind == Kind.Curl || s.Kind == Kind.Root || s.Kind == Kind.Stem ? Mathf.Lerp(1f, 0.35f, u) : 1f;
                    float rad = s.Radius * Mathf.Sqrt(headTaper) * Mathf.Lerp(0.4f, 1f, tailTaper) * rootTaper * startTaper;
                    for (int j = 0; j < Around; j++)
                    {
                        float ang = j / (float)Around * Mathf.PI * 2f;
                        Vector3 dir = normal * Mathf.Cos(ang) + bin * Mathf.Sin(ang);
                        _v.Add(toLocal.MultiplyPoint3x4(p + dir * rad));
                        _n.Add(toLocal.MultiplyVector(dir));
                        _uv.Add(new Vector2(u * s.Length, j / (float)Around));
                        _c.Add(new Color(s.Glow, (int)s.Kind / 10f, u, s.Fade));
                    }
                    if (i < segs)
                        for (int j = 0; j < Around; j++)
                        {
                            int a = baseV + i * Around + j, b = baseV + i * Around + (j + 1) % Around, c = a + Around, d = b + Around;
                            // r2: outward-facing winding (the first order drew the tube's inside: every normal faced away, so the whole stalk read as rim light)
                            _t.Add(a); _t.Add(b); _t.Add(c); _t.Add(b); _t.Add(d); _t.Add(c);
                        }
                }
            }
            _tube.Clear();
            _tube.SetVertices(_v); _tube.SetNormals(_n); _tube.SetUVs(0, _uv); _tube.SetColors(_c); _tube.SetTriangles(_t, 0);
            _tube.bounds = new Bounds(Vector3.zero, Vector3.one * 200f);

            // leaves: a card leaning off the strand, opening as the head passes it
            var toLocalL = _leafGo.transform.worldToLocalMatrix;
            _v.Clear(); _n.Clear(); _uv.Clear(); _c.Clear(); _t.Clear();
            foreach (var l in _leaves)
            {
                var s = l.On;
                if (s.Head < l.T || s.Tail > l.T || s.Fade <= 0.01f) continue;
                float open = Smooth((s.Head - l.T) / 0.15f) * (s.Tail > 0f ? 1f - Smooth((s.Tail - l.T + 0.1f) / 0.1f) : 1f);
                if (open <= 0.01f) continue;
                Vector3 p = s.At(l.T), tan = (s.At(Mathf.Min(1f, l.T + 0.02f)) - s.At(Mathf.Max(0f, l.T - 0.02f))).normalized;
                Vector3 side = Vector3.Cross(tan, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.right; side.Normalize();
                Vector3 dir = (side * l.Side * 0.8f + Vector3.up * 0.5f + tan * 0.3f).normalized;   // the leaf's long axis
                Vector3 wid = Vector3.Cross(dir, tan).normalized;
                float len = l.Size * open, w = l.Size * 0.45f * open;
                Vector3 b0 = p + dir * 0.0f, b1 = p + dir * len;
                int bv = _v.Count;
                _v.Add(toLocalL.MultiplyPoint3x4(b0 - wid * w)); _v.Add(toLocalL.MultiplyPoint3x4(b0 + wid * w));
                _v.Add(toLocalL.MultiplyPoint3x4(b1 - wid * w)); _v.Add(toLocalL.MultiplyPoint3x4(b1 + wid * w));
                Vector3 nn = toLocalL.MultiplyVector(Vector3.Cross(dir, wid));
                for (int k = 0; k < 4; k++) _n.Add(nn);
                _uv.Add(new Vector2(0, 0)); _uv.Add(new Vector2(1, 0)); _uv.Add(new Vector2(0, 1)); _uv.Add(new Vector2(1, 1));
                var col = new Color(s.Glow, l.Seed, open, s.Fade);
                for (int k = 0; k < 4; k++) _c.Add(col);
                _t.Add(bv); _t.Add(bv + 2); _t.Add(bv + 1); _t.Add(bv + 1); _t.Add(bv + 2); _t.Add(bv + 3);
            }
            _leafMesh.Clear();
            _leafMesh.SetVertices(_v); _leafMesh.SetNormals(_n); _leafMesh.SetUVs(0, _uv); _leafMesh.SetColors(_c); _leafMesh.SetTriangles(_t, 0);
            _leafMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 200f);
        }
    }
}
