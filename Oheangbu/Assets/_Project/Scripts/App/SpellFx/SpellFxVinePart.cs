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
        [SerializeField] float headSpeed = 34f;      // r5 (Fable r4 #4): about the rule's own 36 m/s - the head arrives with the flash, no lingering touch
        [SerializeField] float shotRadius = 0.06f;
        [SerializeField] float sideWave = 0.28f;
        [SerializeField] float upArc = 0.15f;
        [SerializeField] float maxReach = 12f;
        [Tooltip("Seconds after the hit before the shot line starts to pull in toward the target, and how long that takes.")]
        [SerializeField] float pullAfter = 0.3f, pullSeconds = 0.25f;

        [Header("Bind")]
        [SerializeField] float coilRadius = 0.18f;
        [SerializeField] float coilTurns = 1.5f;
        [SerializeField] float coilHeight = 0.9f;
        [SerializeField] float coilGrowSeconds = 0.22f;
        [Tooltip("The bind's length (the rule: 2 s slow).")]
        [SerializeField] float holdSeconds = 2f;
        [SerializeField] float unravelSeconds = 0.8f;
        [SerializeField] float stemLength = 1.2f;
        [SerializeField] float rootLength = 0.6f;

        [Header("Leaves / flash")]
        [SerializeField] float leafSize = 0.16f;
        [SerializeField] float flashSize = 0.9f;
        [SerializeField] float flashSeconds = 0.28f;

        /// <summary>Path verbs (ㅏ, the vine is shot) and, from Parry on, the attachment verbs (ㅓ, the vine stays on the caster: 거 Parry · 걱 Heal · 건 Cling ·
        /// 검 SelfSeed · 것 Blade · 겅 Orbit).</summary>
        public enum VineBite { Coil, Cage, Spread, Seed, Cut, Return, Parry, Heal, Cling, SelfSeed, Blade, Orbit, Sweep, Thicket, FanSpread, FanSeed, FanCut, FanReturn, RingPillar, RingSpread, RingSeed, RingCut, RingReturn }

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
            public bool Flat;                           // a mark on the ground: pressed flat and wide (a decal-like stroke, not a lying vine)
            public float Grow = 0.35f;                  // fan: seconds to grow from its start (Seed = start delay)
        }

        sealed class Leaf { public Strand On; public float T; public float Side; public float Size; public float Seed; public bool Along; public bool Lying; public float Width = 1f; }

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
            _launched = true; _launchAge = 0f; _origin = cast.Origin; _cast = cast;
            Vector3 fwd = cast.Forward; fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
            _side = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3? tip = cast.BrushTip != null ? cast.BrushTip() : null;
            _start = tip ?? (cast.Origin + Vector3.up * 1.35f + fwd * 0.4f + _side * 0.25f);
            _start.y = Mathf.Min(_start.y, cast.Origin.y + 1.75f);            // r3 (Fable r2 #6): the arc leaves at the shoulder, not a metre over the head
            float reach = Mathf.Min(maxReach, cast.Spec != null && cast.Spec.PathLength > 0.5f ? cast.Spec.PathLength : maxReach);
            _aimEnd = cast.Origin + fwd * reach + Vector3.up * 1.0f;
            _end = _aimEnd;
            if (IsFan) { SpawnFan(cast, fwd); if (_tubeGo != null) { _tubeGo.SetActive(true); _leafGo.SetActive(true); } return; }
            if (bite >= VineBite.Parry) { SpawnSelf(cast, fwd); if (_tubeGo != null) { _tubeGo.SetActive(true); _leafGo.SetActive(true); } return; }
            float sgn = Rand() < 0.5f ? -1f : 1f;
            _shotSign = sgn;
            _shot = Add(Kind.Shot, t => ShotPoint(t, sgn), shotRadius);
            _shot.Length = Vector3.Distance(_start, _end);
            if (bite == VineBite.Cut) SpawnCrack();
            // r6 (Fable 가 r5 #4): no side curls - the one near the body read as an ink "hook" over the eye before the bite
            // r2 (Fable r1 #5): fewer, smaller leaves along the path and none at the brush
            for (int i = 0; i < 4; i++) _leaves.Add(new Leaf { On = _shot, T = 0.28f + 0.18f * i, Side = (i & 1) == 0 ? 1f : -1f, Size = leafSize * 0.6f * (0.85f + 0.3f * Rand()), Seed = Rand() });
            if (_tubeGo != null) { _tubeGo.SetActive(true); _leafGo.SetActive(true); }
        }

        /// <summary>The rule's path (36 m/s) reaches the body before the drawn head does: the hit is held until the head arrives at the struck body, then the bite plays.</summary>
        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            if (!_launched) return;
            if (IsFan) { FanHit(hit); return; }
            if (bite >= VineBite.Parry) return;
            if (_hit || _pending)
            {
                // later contacts: 간 spreads to the next bodies (a jump each); 감 the seed's activation binds; 강 the return touch (drawn by the back strand)
                if (bite == VineBite.Spread && hit.TargetId != _pendingHit.TargetId) _jumpQueue.Add(hit);
                else if (bite == VineBite.Seed && _hit && _coil == null) _activate = true;
                return;
            }
            _pending = true; _pendingHit = hit;
            Aim(hit);              // r4 (Fable g r3 #5): the head flies to the very point the bite starts from - no jump, no early touch
            _end = _hitPos;
            if (_shot != null) _shot.Length = Vector3.Distance(_start, _end);
        }

        /// <summary>Where the bite happens on this body: the ground under it, its chest, the coil radius and the point on the caster's side.</summary>
        void Aim(SpellFxHit hit)
        {
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
            // r2 (Fable r1 #4): the bind sits on the neck / shoulder line, tight to the body (radius ~0.55 of its width)
            _coilR = _targetRadius * 0.75f + 0.05f;
            _hitPos = _target + toCaster * _coilR + Vector3.up * (ChestAbove() - _feetBelow);
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
        Vector3 _flashAt, _home;
        Transform _cam;
        SpellFxCast _cast;
        readonly List<Leaf> _motes = new List<Leaf>();
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
        float ChestAbove() => Mathf.Max(0.4f, _feetBelow * 0.75f);     // r4 (Fable g r3 #1): the neck / shoulder line, so the front arc crosses the body

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
            Aim(hit);
            Vector3 toCaster = _hitNormal;
            _end = _hitPos;
            if (_shot != null) { _shot.Length = Vector3.Distance(_start, _end); _shot.Head = 1f; }
            switch (bite)
            {
                case VineBite.Cage: SpawnCoil(0.6f); SpawnCage(); break;       // r2 (Fable 각 r1 #5): it binds first (a short wind), then the cage rises
                case VineBite.Seed: SpawnSeed(); break;
                case VineBite.Return: SpawnBack(); break;      // r5 (Fable 강 r3-r4): no half-coil - over the head it read as a hose; the touch is the flash
                case VineBite.Cut: SpawnCrack(); SpawnCoil(1f); for (int k = 0; k < 3; k++) SpellFxLabShot.InkBurst(_hitPos + toCaster * 0.3f + Vector3.up * (0.1f + 0.15f * k), Quaternion.AngleAxis(-40f + 40f * k, Vector3.up) * toCaster, SpellFxBurstBody.Armour); break;   // r2 (Fable 갓 r1 #1): the armour flies off in pieces
                default:
                    SpawnCoil(bite == VineBite.Spread ? 0.75f : 1f);
                    if (bite == VineBite.Spread)
                    {
                        // r6 (Fable 간 r5 #3): the first body gets the same crescent mark as the bodies it spreads to (in front of the chin, 3 s)
                        Vector3 gf = _feet + _hitNormal * (_targetRadius + 0.3f); gf.y = _feet.y + 0.02f; float s1 = Rand();
                        var m0 = Add(Kind.Root, t => { float ang = s1 * 6.28f + t * 5f; float rr = 0.25f + 0.45f * t; return gf + new Vector3(Mathf.Cos(ang) * rr, 0f, Mathf.Sin(ang) * rr); }, shotRadius * 0.55f);
                        m0.Length = 2.2f; m0.Seed = _age; m0.Flat = true; _marks.Add(m0);
                    }
                    break;
            }
            if (bite != VineBite.Seed && bite != VineBite.Cage)
            {
                // r2 (Fable r1 #3 / #6): "착점 한 줄기" = one ink line on the GROUND at the hit (1.2 m along the path, 2 s), not an upright stem on the body
                Vector3 dir = _target - _start; dir.y = 0f; dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward;
                // r4 (Fable g r3 #4): off the body (0.5 m in front of it), 20° off the aim so it is not a vertical "beard", widening toward the camera
                Vector3 gd = Quaternion.AngleAxis(Rand() < 0.5f ? 20f : -20f, Vector3.up) * -dir;
                Vector3 g0 = _feet + Vector3.up * 0.015f - dir * (_targetRadius + 0.5f); float gs = Rand();
                _stem = Add(Kind.Crack, t => g0 + gd * (stemLength * t) + Vector3.Cross(Vector3.up, gd) * ((Mathf.PerlinNoise(t * 9f, gs * 7f) - 0.5f) * 0.06f), shotRadius * 0.7f);
                _stem.Length = stemLength; _stem.Seed = -2f; _stem.Flat = true;      // Seed -2 = widens along its length (Rebuild)
            }
            // r2 (Fable r1 #3): the bite is an ink splash (black drops), a few leaves with it
            SpellFxLabShot.InkBurst(_hitPos, toCaster, SpellFxBurstBody.Water);          // r3 (Fable r2 #5): ink only - the wood burst's leaf sprites read as green stars
            if (_flashGo != null) { _flashGo.SetActive(true); _flashGo.transform.position = _hitPos; }
        }

        public void OnTick(SpellFxCast cast, float ageSeconds)
        {
            _age = ageSeconds;
            if (!_launched || _tubeGo == null) return;
            if (bite >= VineBite.Parry) { _cam = Camera.main != null ? Camera.main.transform : null; if (IsFan) AnimateFan(_age); else AnimateSelf(_age); Rebuild(); return; }
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
            // (the shot's side curls are gone since r6 - their old "follow the shot" step also folded away 감's seed knot and leaf stems, which are Curl strands too)
            if (_hit)
            {
                float hs = a - _hitAge;
                if (bite == VineBite.Seed && _activate && _coil == null) { _activateAge = a; SpawnCoil(1f); _flashAge = a; _flashAt = _hitPos; SpellFxLabShot.InkBurst(_hitPos, _hitNormal, SpellFxBurstBody.Water); }
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
                    _stem.Glow = 0.55f + Flash(hs) * 0.45f;          // r5 (Fable r4 #2): ink on red soil vanished - jade edges keep the ground line readable
                    float su = (hs - 2f) / 0.5f;                                         // the ground line: 2 s
                    if (su > 0f) _stem.Fade = 1f - Smooth(su);
                }
                foreach (var s in _cage)
                {
                    s.Head = Smooth((hs - s.Seed) / 0.75f);           // the cage grows over the bind (1.5 s cap: it is closed well before)
                    s.Glow = s.Flat ? 0.5f : Flash(hs - s.Seed) * 0.4f;     // r2 (Fable 각 r1 #3): no lit stripe at rest (the ink pool: jade rim)
                    if (u > 0f) { s.Tail = Smooth(u - s.Seed); s.Fade = fadeOut; }
                }
                if (_pillar != null)
                {
                    _pillar.Head = Smooth(hs / 0.35f);
                    for (int m = 0; m < _motes.Count; m++) _motes[m].T = Mathf.Repeat(m / 4f + hs * 0.31f, 0.95f);   // motes rise up the column at ~0.5 m/s
                    _pillar.Glow = 0.75f + 0.25f * Mathf.Sin(hs * 6.28f);    // the waiting seed breathes at 1 Hz (r1: bright - a black rod read as a pole)
                    if (_activateAge >= 0f) { float k = Smooth((a - _activateAge) / 0.3f); _pillar.Tail = k; }
                    if (u > 0f) _pillar.Fade = fadeOut;
                }
                if (_crack != null)
                {
                    // past the body: it runs on 1.6 m after the bite, then holds
                    float reach = Vector3.Dot(_feet - _crackA, _crackDir) + 1.6f * Smooth(hs / 0.15f);
                    _crack.Head = Mathf.Clamp01(reach / _crack.Length);
                    _crack.Glow = 0.55f + Flash(hs) * 0.45f;
                    float cu = (hs - 2.4f) / 0.6f; if (cu > 0f) { _crack.Tail = Smooth(cu); _crack.Fade = 1f - Smooth(cu - 0.4f); }
                }
                if (_back != null)
                {
                    float bs = hs - 0.3f;                                    // r3 (Fable 강 r2 #1): a beat at the body, then the head flies home
                    _back.Head = Mathf.Clamp01(bs / 0.3f);
                    _back.Glow = _returnAge < 0f ? 0.6f : 0.2f;                         // the homing head is lit while it flies
                    if (_back.Head >= 1f && _returnAge < 0f) { _returnAge = a; _flashAge = a; _flashAt = _back.At(1f); }
                    if (_returnAge >= 0f)
                    {
                        float rs = a - _returnAge;
                        _back.Tail = Smooth((rs - 0.25f) / 0.35f);
                        if (_shot != null) { _shot.Tail = Smooth((rs - 0.1f) / 0.35f); }
                    }
                }
                for (int k = _arrivals.Count - 1; k >= 0; k--)
                    if (a >= _arrivals[k].At)
                    {
                        SpellFxLabShot.InkBurst(_arrivals[k].Pos, _arrivals[k].Dir, SpellFxBurstBody.Water);
                        if (_arrivals[k].Dir != Vector3.up) Spark(_arrivals[k].Pos + _arrivals[k].Dir * 0.3f);     // r5 (Fable 간 r4 #1): each spread hit flashes like the first
                        _arrivals.RemoveAt(k);
                    }
                for (int k = 0; k < _sparks.Count; k++)
                {
                    var sp = _sparks[k]; float f = Flash(a - sp.At);
                    sp.Go.SetActive(f > 0.01f);
                    if (f <= 0.01f) continue;
                    if (_cam != null) sp.Go.transform.rotation = Quaternion.LookRotation(sp.Go.transform.position - _cam.position, Vector3.up);
                    sp.Go.transform.localScale = Vector3.one * flashSize * (0.6f + 0.6f * Mathf.Clamp01((a - sp.At) / flashSeconds));
                    sp.Mat.SetFloat("_Flash", f);
                }
                foreach (var s in _jumps)
                {
                    float js = a - s.Seed;
                    s.Head = s.Kind == Kind.Jump ? Smooth(js / 0.14f) : Smooth((js - 0.14f - HitStop) / coilGrowSeconds);
                    s.Glow = s.Kind == Kind.Jump ? 0.15f + Flash(js) * 0.4f : Flash(js - 0.14f) * 1.2f;
                    float ju = (js - holdSeconds) / unravelSeconds;
                    if (s.Kind == Kind.Jump) { float jt = (js - 0.5f) / 0.3f; if (jt > 0f) s.Tail = Smooth(jt); }
                    if (ju > 0f) { s.Tail = Mathf.Max(s.Tail, Smooth(ju)); s.Fade = 1f - Smooth(ju - 0.5f); }
                }
                foreach (var s in _marks)
                {
                    if (s.Seed < 0f) { s.Head = Smooth((hs - 0.1f) / 0.3f); if (s.Flat) s.Glow = 0.5f; if (u > 0f) s.Fade = fadeOut; continue; }
                    float ms = a - s.Seed;
                    s.Head = Smooth((ms - 0.2f) / 0.4f);
                    s.Glow = 0.5f;                                   // ground marks: jade edges or they vanish on red soil
                    float mu = (ms - 3f) / 0.5f; if (mu > 0f) { s.Tail = Smooth(mu); s.Fade = 1f - Smooth(mu - 0.5f); }     // the spread's mark: 3 s
                }
                if (u > 0f && _coil != null)
                {
                    if (_roots.Count == 0) SpawnRoots();
                    foreach (var r in _roots) { r.Head = Smooth(u * 1.3f); r.Fade = 1f - Smooth((u - 0.6f) / 0.4f); r.Glow = 0.5f; }
                }
                if (_flashGo != null)
                {
                    float f = Mathf.Max(Flash(hs), _flashAge >= 0f ? Flash(a - _flashAge) : 0f);
                    if (bite == VineBite.Seed && _activateAge < 0f) f *= 0.5f;
                    _flashGo.SetActive(f > 0.01f);
                    if (f > 0.01f)
                    {
                        var cam = Camera.main;
                        _flashGo.transform.position = _flashAge >= 0f && a - _flashAge < hs ? _flashAt : _hitPos;
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
                }, shotRadius * 0.55f);          // r4 (Fable g r3 #3): 2-3 px read as dots - thick enough to see at 5 m
                root.Length = rootLength; _roots.Add(root);
            }
        }

        // ------------------------------------------------------------------ the verbs' bites

        void SpawnCoil(float turnScale)
        {
            // r5 (Fable 가 r4 #1): the loops turn round the axis that points at the caster (the neck), so each front arc crosses the chest in view -
            // not round the body's up axis, where the front arc hid behind the head
            // r6 (Fable 가 r5 #1): the lying body's jaw hid the front arc - higher (+0.25 m) and wider (x1.4) so it crosses the brow and leaves the outline
            float r = _coilR * 1.1f * 1.4f;
            float turns = coilTurns * turnScale;
            Vector3 ax = _hitNormal, sd = Vector3.Cross(Vector3.up, ax).normalized;
            float chestUp = ChestAbove() - _feetBelow + 0.25f;
            _coil = Add(Kind.Coil, t =>
            {
                float ang = Mathf.PI * 0.5f + t * turns * 2f * Mathf.PI;          // starts on top, comes down across the front
                float rr = r * (1f + 0.08f * Mathf.Sin(t * 19f));
                Vector3 c = _target + Vector3.up * chestUp + ax * Mathf.Lerp(0.25f, -0.45f, t);
                return c + sd * (Mathf.Cos(ang) * rr) + Vector3.up * (Mathf.Sin(ang) * rr * 0.8f);
            }, shotRadius * 1.25f);
            _coil.Length = turns * 2f * Mathf.PI * r;
            // two leaves on the front arcs, half a turn apart (the 각 leaf: round, bigger)
            // two big leaves where the loops cross the face: the bottom of the first loop and the side of the second (0.35 m)
            for (int i = 0; i < 2; i++) _leaves.Add(new Leaf { On = _coil, T = i == 0 ? 0.5f / turns : Mathf.Min(0.95f, 1.25f / turns), Side = (i & 1) == 0 ? 1f : -1f, Size = leafSize * 2.2f, Seed = Rand() });
        }

        /// <summary>각: branches rise from the ground round the body and arch over it into a cage; the tips curl on above the top ("위로 솟는 가지 완성형").</summary>
        void SpawnCage()
        {
            // r2 (my look: wide and low it read as spider legs from the camera): narrow and tall - a cage, not a dome
            // r2 (Fable 각 r1 #1): woven, not parallel hoops - 6 main bars + 6 thin ones, each swaying sideways 1.5 times up its length with neighbours in
            // opposite phase so they cross once or twice; height ~1.6 x the body, foot radius ~0.7 of its length; a small curled side shoot on each main bar
            int n = 12;
            float r = _targetRadius * 0.7f + 0.25f;
            float top = Mathf.Max(1.4f, _feetBelow * 1.6f * 2f);
            for (int i = 0; i < n; i++)
            {
                bool main = (i & 1) == 0;
                float a0 = i / (float)n * Mathf.PI * 2f + Rand() * 0.15f, seed = Rand();
                Vector3 dir = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), tng = Vector3.Cross(Vector3.up, dir);
                float sway = (main ? 0.2f : 0.15f) * ((i / 2) % 2 == 0 ? 1f : -1f);
                Vector3 p0 = _feet + dir * r, apex = _feet + Vector3.up * top + dir * 0.02f;
                Vector3 ctrl = _feet + dir * (r * 1.0f) + Vector3.up * top;
                float ph = a0 * 3f;
                System.Func<float, Vector3> bar = t =>
                {
                    // r3 (Fable 각 r2 #2): alive while it holds - a slow ±2° sway, more toward the top
                    Vector3 breathe = tng * (Mathf.Sin(_age * 2.5f + ph) * 0.09f * t);      // r6: ±5° (±2° did not read)
                    if (t < 0.84f)
                    {
                        float u = t / 0.84f;
                        Vector3 a = Vector3.Lerp(p0, ctrl, u), b = Vector3.Lerp(ctrl, apex, u);
                        return Vector3.Lerp(a, b, u) + tng * (Mathf.Sin(u * Mathf.PI * 3f) * sway * Mathf.Sin(u * Mathf.PI)) + breathe;
                    }
                    // r3 (Fable 각 r2 #4): the tips twist round one point - a knot - and only then curl on upward
                    float w = (t - 0.84f) / 0.16f, k = a0 + w * 4f;
                    return apex + new Vector3(Mathf.Cos(k), 0f, Mathf.Sin(k)) * (0.09f * (1f - 0.5f * w)) + Vector3.up * (0.28f * w) + breathe;
                };
                var s = Add(Kind.Cage, bar, shotRadius * (main ? 1.1f : 0.45f));
                s.Length = top * 1.6f; s.Seed = 0.3f + (i / 2) * 0.07f + (main ? 0f : 0.12f);       // after the first wind (the coil) - then one bar after another
                _cage.Add(s);
                if (!main) continue;
                _arrivals.Add(new Arrival { At = _age + s.Seed + 0.05f, Pos = p0 + Vector3.up * 0.1f, Dir = Vector3.up });     // r6 (Fable 각 r3 #1): ink kicks up at each bar's foot as it breaks ground
                for (int k = 0; k < 2; k++) _leaves.Add(new Leaf { On = s, T = 0.3f + 0.35f * k + 0.1f * Rand(), Side = (k & 1) == 0 ? 1f : -1f, Size = leafSize * (1.6f + 0.5f * Rand()), Seed = Rand() });
                float at = 0.45f + 0.1f * seed, side = seed < 0.5f ? 1f : -1f;
                var shoot = Add(Kind.Cage, t => { Vector3 b0 = bar(at); float ang = t * 4f; return b0 + tng * side * (Mathf.Sin(ang) * 0.14f + t * 0.1f) + Vector3.up * ((1f - Mathf.Cos(ang)) * 0.1f) + dir * (t * 0.08f); }, shotRadius * 0.4f);
                shoot.Length = 0.35f; shoot.Seed = s.Seed + 0.35f;
                _cage.Add(shoot);
            }
            // r6 (Fable 각 r3 #1): an ink pool under the whole cage (pressed flat, a jade rim) - two loose rings and a few short spokes
            Vector3 c0 = _feet + Vector3.up * 0.02f; float sp = Rand();
            for (int k = 0; k < 2; k++)
            {
                float rr0 = r * (1.15f + 0.25f * k), kk = k;
                var pool = Add(Kind.Root, t => { float ang = t * Mathf.PI * 2f + sp * 6f + kk; float wob = 1f + 0.12f * (Mathf.PerlinNoise(t * 7f, sp * 5f + kk) - 0.5f); return c0 + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * rr0 * wob; }, shotRadius * (1.2f - 0.5f * k));
                pool.Length = Mathf.PI * 2f * rr0; pool.Seed = 0.25f + 0.1f * k; pool.Flat = true;
                _cage.Add(pool);
            }
        }

        /// <summary>감: a seed lodged at the hit with a thin pillar of light-ink standing over it ("세로 기둥 · 점"); it waits.</summary>
        void SpawnSeed()
        {
            Vector3 at = _hitPos + Vector3.up * 0.35f + _hitNormal * 0.2f;      // r4 (Fable 감 r3 #1): above the head and toward the camera - at the chest it hid in the body
            // r2 (Fable 감 r1): a thin column (x0.4) with a seed node half way, and a seed mark at the hit - an ink knot with two glass leaves
            _pillar = Add(Kind.Pillar, t => at + Vector3.up * (t * 1.3f) + _side * (Mathf.Sin(t * 9f) * 0.1f * t) + _hitNormal * (Mathf.Cos(t * 9f) * 0.1f * t), shotRadius * 0.5f);     // r6 (Fable 감 r5 #1): 0.06 m, a loose spiral the motes ride
            _pillar.Length = 1.5f;
            var knot = Add(Kind.Curl, t => { float ang = t * 9f; float rr = 0.09f * (1f - 0.4f * t); return at + _hitNormal * 0.04f + _side * (Mathf.Cos(ang) * rr) + Vector3.up * (Mathf.Sin(ang) * rr); }, shotRadius * 0.8f);
            knot.Length = 0.6f; knot.Seed = -1f; _marks.Add(knot);
            var node = Add(Kind.Curl, t => { float ang = t * 9f; float rr = 0.05f; return at + Vector3.up * 0.8f + _side * (Mathf.Cos(ang) * rr) + _hitNormal * (Mathf.Sin(ang) * rr); }, shotRadius * 0.5f);
            node.Length = 0.35f; node.Seed = -1f; _marks.Add(node);
            for (int k = 0; k < 2; k++)
            {
                float dir = k == 0 ? 1f : -1f, seed = Rand();
                var c = Add(Kind.Curl, t => { float ang = t * 4f; return at + _hitNormal * 0.05f + _side * dir * (Mathf.Sin(ang) * 0.12f) + Vector3.up * ((1f - Mathf.Cos(ang)) * 0.09f + t * 0.1f); }, shotRadius * 0.5f);
                c.Length = 0.35f; c.Seed = -1f;
                _marks.Add(c);
                _leaves.Add(new Leaf { On = c, T = 0.85f, Side = dir, Size = leafSize * 2.4f, Seed = seed });
            }
            // r3 (Fable 감 r2 #2 / #3 / #4): small motes rising up the column (it is alive), a jade bud at its tip, an ink dot on the ground under it
            for (int k = 0; k < 4; k++) { var m = new Leaf { On = _pillar, T = k / 4f, Side = (k & 1) == 0 ? 1f : -1f, Size = leafSize * 0.7f, Seed = Rand() }; _leaves.Add(m); _motes.Add(m); }
            for (int k = 0; k < 2; k++) _leaves.Add(new Leaf { On = _pillar, T = 0.985f, Side = k == 0 ? 1f : -1f, Size = leafSize * 0.65f, Seed = Rand() });     // the bud: two small leaves
            Vector3 g = _feet + _hitNormal * (_targetRadius + 0.45f); g.y = _feet.y + 0.02f;      // in front of the chin, where the camera sees the ground
            var dot = Add(Kind.Root, t => { float ang = t * Mathf.PI * 4f; float rr = 0.09f * (1f - 0.7f * t); return g + new Vector3(Mathf.Cos(ang) * rr, 0f, Mathf.Sin(ang) * rr); }, shotRadius * 0.9f);
            dot.Length = 0.6f; dot.Seed = -1f; dot.Flat = true; _marks.Add(dot);
        }

        /// <summary>갓: a straight crack carved into the ground along the path, from the caster's feet through the body and on ("아래로 파인 균열선").</summary>
        /// <summary>r2 (Fable 갓 r1 #2 / #3): laid at the LAUNCH along the aim and grown under the flying head - the cut follows the vine; it starts in
        /// front of the caster's feet (not behind them, off screen) and runs on 1.6 m past the body.</summary>
        void SpawnCrack()
        {
            if (_crack != null) return;
            Vector3 fwd = _aimEnd - _origin; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : transform.forward;
            Vector3 a = _origin + fwd * 0.9f; a.y = GroundY(a + Vector3.up * 1.5f, null) + 0.015f;
            float len = maxReach;
            Vector3 side = Vector3.Cross(Vector3.up, fwd); float seed = Rand();
            _crackA = a; _crackDir = fwd;
            _crack = Add(Kind.Crack, t => a + fwd * (len * t) + side * ((Mathf.PerlinNoise(t * 40f, seed * 10f) - 0.5f) * 0.09f), shotRadius * 0.8f);
            _crack.Length = len; _crack.Flat = true;
            for (int k = 0; k < 3; k++) _leaves.Add(new Leaf { On = _crack, T = 0.08f + 0.06f * k, Side = (k & 1) == 0 ? 1f : -1f, Size = leafSize * 1.9f, Seed = Rand() });     // r4 (Fable 갓 r3 #4): glass leaves along the cut
        }
        Vector3 _crackA, _crackDir;

        /// <summary>강: the head turns at the body and comes back to the brush along the other side - one closed loop ("닫힌 곡선").</summary>
        void SpawnBack()
        {
            // r4 (Fable 강 r3 #1): home is the brush HAND now (the grip), not the launch tip - that tip sat off to the right, so the return grew sideways
            Vector3? grip = null;            // r5: the great brush's root sits far out along the brush (near the body) - home is the caster's right hand
            Vector3 fw = _target - _origin; fw.y = 0f; fw = fw.sqrMagnitude > 1e-4f ? fw.normalized : transform.forward;
            _home = grip ?? (_origin + Vector3.up * 1.1f + _side * 0.95f + fw * 0.2f);      // r7: out to the right and low - straight back along the view it did not move on screen      // r6: the right hand, out in front - visible past the caster's shoulder

            float sgn = 1f;                 // r6: always out on the right, where the caster's body does not hide it
            _back = Add(Kind.Back, t =>
            {
                // r2 (Fable 강 r1 #1: the high return read as a blade rising out of the head): a wide, low arc out to the other side and home -
                // the outgoing line and this one make one closed loop seen from behind (its home end fades 0.5 m before the brush)
                Vector3 p = Vector3.Lerp(_hitPos, _home, t);
                float bell = Mathf.Sin(Mathf.PI * t);
                return p + _side * (sgn * 0.8f * bell) + Vector3.up * (0.3f * bell);
            }, shotRadius);
            _back.Length = Vector3.Distance(_hitPos, _home) * 1.2f;
            for (int i = 0; i < 2; i++) _leaves.Add(new Leaf { On = _back, T = 0.3f + 0.25f * i, Side = (i & 1) == 0 ? 1f : -1f, Size = leafSize * 1.2f, Seed = Rand() });
            // r2 (Fable 강 r1 #2): the closed curve on the ground under the loop, 3 s
            Vector3 c0 = Vector3.Lerp(_origin, _feet, 0.55f); c0.y = _feet.y + 0.02f;
            Vector3 ax = _feet - _origin; ax.y = 0f; float half = ax.magnitude * 0.5f; ax.Normalize();
            Vector3 sx = Vector3.Cross(Vector3.up, ax); float s2 = Rand();
            var ring = Add(Kind.Root, t => { float ang = t * Mathf.PI * 2f; return c0 + ax * (Mathf.Cos(ang) * half) + sx * (Mathf.Sin(ang) * 0.7f * sgn) + ax * ((Mathf.PerlinNoise(t * 6f, s2 * 9f) - 0.5f) * 0.1f); }, shotRadius * 0.55f);
            ring.Length = Mathf.PI * (half + 0.7f); ring.Seed = _age - 0.8f;      // a 2 s mark (it fades inside the shot) ring.Flat = true;
            _marks.Add(ring);
        }

        /// <summary>간: a thinner vine hops from the first body to the next one (3 m), coils it, and leaves a short flat mark on the ground there (3 s).</summary>
        void SpawnJump(SpellFxHit hit)
        {
            // r2 (Fable 간 r1): the same bind rule as the first body (neck / shoulder, loops round the arrival axis), a shoulder-high hop, and the hit's
            // own reaction (ink + the coil's flash) when the hop ARRIVES
            float tR = Mathf.Clamp(hit.TargetRadius, 0.25f, 0.9f), r = (tR * 0.75f + 0.05f) * 1.1f * 1.4f;     // r4 (Fable 간 r3 #1): the same ring as the first body
            Transform tr = hit.Target; Vector3 off = tr != null ? hit.Position - tr.position : Vector3.zero;
            float fb = Mathf.Max(0.2f, hit.Position.y - GroundY(hit.Position, hit.Target)), up2 = Mathf.Max(0.4f, fb * 0.75f) - fb + 0.25f;
            System.Func<Vector3> now = () => (tr != null ? tr.position + off : hit.Position) + Vector3.up * up2;
            Vector3 from = _target + Vector3.up * (ChestAbove() - _feetBelow);
            var j = Add(Kind.Jump, t =>
            {
                Vector3 p = Vector3.Lerp(from, now(), t);
                return p + Vector3.up * (Mathf.Sin(Mathf.PI * t) * 0.35f);
            }, shotRadius * 0.75f);
            j.Length = Vector3.Distance(from, now()) + 0.3f; j.Seed = _age;
            _jumps.Add(j);
            // r5 (Fable 간 r4 #2): the same closed ring, the same way round as the first body - its axis points at the caster too, not along the hop
            Vector3 ax = _hitNormal;
            Vector3 sd = Vector3.Cross(Vector3.up, ax).normalized;
            _leaves.Add(new Leaf { On = j, T = 0.5f, Side = 1f, Size = leafSize * 1.4f, Seed = Rand() });          // r5 (#3): a glass leaf on each hop
            var c = Add(Kind.Coil, t =>
            {
                float ang = Mathf.PI * 0.5f + t * 1.5f * 2f * Mathf.PI;
                return now() + ax * Mathf.Lerp(0.2f, -0.4f, t) + sd * (Mathf.Cos(ang) * r) + Vector3.up * (Mathf.Sin(ang) * r * 0.8f);
            }, shotRadius);
            c.Length = 1.5f * 2f * Mathf.PI * r; c.Seed = _age;
            _jumps.Add(c);
            _arrivals.Add(new Arrival { At = _age + 0.14f, Pos = now(), Dir = _hitNormal });
            Vector3 feet = hit.Feet + _hitNormal * (tR + 0.3f); feet.y = GroundY(hit.Position, hit.Target) + 0.02f; float s0 = Rand();     // r3 (Fable 간 r2 #2): in front of the body, where the camera sees it
            var mark = Add(Kind.Root, t => { float ang = s0 * 6.28f + t * 5f; float rr = 0.25f + 0.45f * t; return feet + new Vector3(Mathf.Cos(ang) * rr, 0f, Mathf.Sin(ang) * rr); }, shotRadius * 0.55f);
            mark.Length = 2.2f; mark.Seed = _age; mark.Flat = true;
            _marks.Add(mark);
            for (int i = 0; i < 2; i++) _leaves.Add(new Leaf { On = c, T = 0.2f + 0.4f * i, Side = (i & 1) == 0 ? 1f : -1f, Size = leafSize * 1.3f, Seed = Rand() });
        }

        // ------------------------------------------------------------------ ㅓ attachment: the vine on the caster (2026-10-30)

        readonly List<Strand> _self = new List<Strand>();
        bool _burst;
        float _selfHold, _selfGrow;

        static float HoldOf(VineBite b)
        {
            switch (b) { case VineBite.Parry: return 0.5f; case VineBite.Heal: return 3f; case VineBite.SelfSeed: return 8f; default: return 6f; }
        }

        /// <summary>The attachment verbs: nothing is shot - at the launch the vine takes the caster's own body (round the torso / along the brush / round
        /// the caster), holds for the verb's time (the rules: parry 0.5 s, heal 3 s, cling / blade / orbit 6 s, seed 8 s) and lets go as the path's bind does.</summary>
        void SpawnSelf(SpellFxCast cast, Vector3 fwd)
        {
            _selfHold = HoldOf(bite);
            _selfGrow = bite == VineBite.Parry ? 0.09f : bite == VineBite.Heal ? 0.9f : 0.35f;
            Vector3 c = _origin; float bodyR = 0.36f;
            _hit = true; _hitAge = 0.04f; _hitNormal = -fwd;
            _target = c + Vector3.up * 1.0f; _feet = c; _feetBelow = 1.0f;
            System.Func<Vector3> hand = () => c + Vector3.up * 1.25f + _side * 0.32f + fwd * 0.25f;
            System.Func<Vector3> tip = () => hand() + (Vector3.up * 0.9f + fwd * 0.45f).normalized * 1.8f;      // r1: a raised blade from the hand (the rig's brush tip lay by the hand and hid it)
            _hitPos = c + Vector3.up * 1.2f - fwd * 0.3f + _side * 0.2f;          // the flash: on the caster's back-right, where the camera sees it
            switch (bite)
            {
                case VineBite.Parry:
                case VineBite.Heal:
                case VineBite.Cling:
                {
                    // round the torso, from the hips up (heal: from the ground, slowly), and along the brush arm to the brush
                    float y0 = bite == VineBite.Heal ? 0.05f : 0.55f, turns = bite == VineBite.Parry ? 2.2f : 2.6f;
                    var body = Add(Kind.Coil, t =>
                    {
                        float ang = t * turns * 2f * Mathf.PI;
                        float r = bodyR * (1f + 0.15f * Mathf.Sin(t * 17f)) * (bite == VineBite.Heal ? 1.15f : 1f);
                        return c + new Vector3(Mathf.Cos(ang) * r, Mathf.Lerp(y0, 1.55f, t), Mathf.Sin(ang) * r);
                    }, shotRadius * 0.9f);
                    body.Length = turns * 2f * Mathf.PI * bodyR; body.Seed = 0f; _self.Add(body);
                    var arm = Add(Kind.Coil, t =>
                    {
                        Vector3 a = c + Vector3.up * 1.45f + _side * 0.2f, b = hand() + fwd * 0.35f + Vector3.up * 0.25f;      // r3 (Fable 거 · 건 r2: "an antenna"): to the brush head only
                        Vector3 axis = (b - a); float len = axis.magnitude; axis = len > 1e-3f ? axis / len : fwd;
                        Vector3 n1 = Vector3.Cross(axis, Vector3.up).normalized, n2 = Vector3.Cross(axis, n1);
                        float ang = t * 2f * Mathf.PI * 2f;
                        return Vector3.Lerp(a, b, t * 0.85f) + (n1 * Mathf.Cos(ang) + n2 * Mathf.Sin(ang)) * 0.07f;
                    }, shotRadius * 0.6f);
                    arm.Length = 3f; arm.Seed = 0.08f; _self.Add(arm);
                    int nl = bite == VineBite.Heal ? 8 : 4;
                    for (int i = 0; i < nl; i++) _leaves.Add(new Leaf { On = body, T = 0.12f + 0.8f * i / nl, Side = (i & 1) == 0 ? 1f : -1f, Size = leafSize * 1.4f, Seed = Rand() });
                    _leaves.Add(new Leaf { On = arm, T = 0.5f, Side = 1f, Size = leafSize * 1.2f, Seed = Rand() });
                    body.Glow = bite == VineBite.Parry ? 0.9f : 0.25f; arm.Glow = body.Glow;
                    if (bite == VineBite.Heal)
                        for (int k = 0; k < 3; k++)
                        {
                            // invisible carriers for the rising motes (heal light), round the body
                            float a0 = k * 2.1f;
                            var carrier = Add(Kind.Pillar, t => c + new Vector3(Mathf.Cos(a0 + t * 2f) * 0.55f, 0.2f + t * 2.0f, Mathf.Sin(a0 + t * 2f) * 0.55f), 0.0002f);
                            carrier.Length = 2f; carrier.Seed = 0.3f; _self.Add(carrier);
                            for (int m = 0; m < 3; m++) { var mote = new Leaf { On = carrier, T = m / 3f, Side = 1f, Size = leafSize * 0.6f, Seed = Rand() }; _leaves.Add(mote); _motes.Add(mote); }
                        }
                    if (bite == VineBite.Cling)
                        for (int k = 0; k < 3; k++)
                        {
                            // feelers: short tendrils reaching out from the shoulders toward where the spread would go
                            float side = k - 1f, sd = Rand();
                            Vector3 from = c + Vector3.up * (1.35f - 0.15f * Mathf.Abs(side)) + _side * (0.3f * side);
                            Vector3 dir = (fwd * 0.8f + _side * (side * 0.9f + 0.35f) + Vector3.up * 0.15f).normalized;
                            float kk = k; var feel = Add(Kind.Curl, t => from + dir * (t * 1.0f) + Vector3.up * (Mathf.Sin(t * 6f + sd * 5f) * 0.08f + Mathf.Sin(_age * 4.4f + kk * 2f) * 0.2f * t) + Vector3.Cross(Vector3.up, dir) * (Mathf.Sin(t * 9f) * 0.05f), shotRadius * 1.1f);     // r3: the sway is vertical (seen from behind)     // r2 (Fable 건 r1): thicker, swaying ±15°
                            feel.Length = 1.0f; feel.Seed = 0.3f + 0.1f * k; feel.Glow = 0.4f; _self.Add(feel);
                            _leaves.Add(new Leaf { On = feel, T = 0.92f, Side = 1f, Size = leafSize * 1.8f, Seed = Rand() });
                        }
                    break;
                }
                case VineBite.SelfSeed:
                {
                    // the seed on the caster's back (the camera sees the back): an ink knot, two glass leaves, a column of light over the head with motes
                    Vector3 at = c + Vector3.up * 1.3f - fwd * 0.22f;
                    var knot = Add(Kind.Curl, t => { float ang = t * 9f; float rr = 0.09f * (1f - 0.4f * t); return at - fwd * 0.04f + _side * (Mathf.Cos(ang) * rr) + Vector3.up * (Mathf.Sin(ang) * rr); }, shotRadius * 0.8f);
                    knot.Length = 0.6f; knot.Seed = 0f; _self.Add(knot);
                    for (int k = 0; k < 2; k++) _leaves.Add(new Leaf { On = knot, T = 0.3f + 0.5f * k, Side = k == 0 ? 1f : -1f, Size = leafSize * 2.2f, Seed = Rand() });
                    var col = Add(Kind.Pillar, t => at + Vector3.up * (t * 1.0f) + _side * (Mathf.Sin(t * 9f) * 0.08f * t) - fwd * (Mathf.Cos(t * 9f) * 0.08f * t), shotRadius * 0.9f);     // r2 (Fable 검 r1 #1): 1 m, a loose spiral
                    col.Length = 1.2f; col.Seed = 0.1f; col.Glow = 0.8f; _self.Add(col);
                    for (int k = 0; k < 4; k++) { var m = new Leaf { On = col, T = k / 4f, Side = (k & 1) == 0 ? 1f : -1f, Size = leafSize * 1.1f, Seed = Rand() }; _leaves.Add(m); _motes.Add(m); }
                    for (int k = 0; k < 2; k++) _leaves.Add(new Leaf { On = col, T = 0.985f, Side = k == 0 ? 1f : -1f, Size = leafSize * 0.65f, Seed = Rand() });
                    // r2 (Fable 검 r1 #2): the 2 m it will bind when it goes off - a faint jade ring on the ground
                    var reach = Add(Kind.Root, t => { float ang = t * Mathf.PI * 2f; return c + Vector3.up * 0.02f + new Vector3(Mathf.Cos(ang) * 2f, 0f, Mathf.Sin(ang) * 2f); }, shotRadius * 0.4f);
                    reach.Length = 4f * Mathf.PI; reach.Seed = 0.2f; reach.Flat = true; reach.Glow = 0.6f; reach.Radius = shotRadius; _self.Add(reach);
                    break;
                }
                case VineBite.Blade:
                {
                    // the vine wound along the brush shaft (two helices) and a lit cutting edge beside it
                    for (int h = 0; h < 2; h++)
                    {
                        float ph = h * Mathf.PI;
                        var wrap = Add(Kind.Coil, t =>
                        {
                            Vector3 a = hand(), b = tip(), axis = b - a; float len = axis.magnitude; axis = len > 1e-3f ? axis / len : fwd;
                            Vector3 n1 = Vector3.Cross(axis, Vector3.up).normalized, n2 = Vector3.Cross(axis, n1);
                            float ang = ph + t * 3f * Mathf.PI * 2f;      // r2: 3 turns (10 read as a screw)
                            return Vector3.Lerp(a, b, t) + (n1 * Mathf.Cos(ang) + n2 * Mathf.Sin(ang)) * 0.06f;
                        }, shotRadius * 0.55f);
                        wrap.Length = 3f; wrap.Seed = 0.05f * h; _self.Add(wrap);
                    }
                    var edge = Add(Kind.Pillar, t =>      // drawn as light (the pillar look): the blade's cutting edge
                    
                    {
                        Vector3 a = hand(), b = tip(), axis = b - a; float len = axis.magnitude; axis = len > 1e-3f ? axis / len : fwd;
                        Vector3 n = Vector3.Cross(axis, Vector3.up).normalized;
                        return Vector3.Lerp(a, b, 0.15f + 0.95f * t) - n * 0.11f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.1f));      // r3: on the outer side (the coil hid it)
                    }, shotRadius * 0.4f);
                    edge.Length = 2.5f; edge.Seed = 0.15f; edge.Glow = 1f; _self.Add(edge);
                    // r2 (Fable 것 r1 #1: "a drill, no blade"): the blade itself - one long glass leaf laid along the edge (1.7 m, slim), the vine only binds it
                    _leaves.Add(new Leaf { On = edge, T = 0.02f, Side = 1f, Size = 1.75f, Width = 0.12f, Along = true, Seed = 0.5f });
                    for (int k = 0; k < 5; k++) _leaves.Add(new Leaf { On = _self[k & 1], T = 0.04f + 0.06f * k, Side = (k & 1) == 0 ? 1f : -1f, Size = leafSize * (1.2f + 0.2f * k), Seed = Rand() });
                    break;
                }
                case VineBite.Orbit:
                {
                    // a ring of vine circling the caster at the waist (it turns), leaves on it, its track on the ground
                    float r = 0.85f;      // r1: at 1.2 m the near side passed the camera as a black slab
                    var ring = Add(Kind.Coil, t =>
                    {
                        float ang = t * Mathf.PI * 2f + _age * 4f;      // r2 (Fable 겅 r1 #2): one closed ring
                        return c + new Vector3(Mathf.Cos(ang) * r, 0.85f + 0.08f * Mathf.Sin(ang * 3f), Mathf.Sin(ang) * r);
                    }, shotRadius * 1.0f);
                    ring.Length = r * Mathf.PI * 2f; ring.Seed = 0f; ring.Glow = 0.3f; _self.Add(ring);
                    for (int i = 0; i < 4; i++) _leaves.Add(new Leaf { On = ring, T = 0.15f + 0.22f * i, Side = (i & 1) == 0 ? 1f : -1f, Size = leafSize * 1.5f, Seed = Rand() });
                    var track = Add(Kind.Root, t => { float ang = t * Mathf.PI * 2f; return c + Vector3.up * 0.02f + new Vector3(Mathf.Cos(ang) * (r + 0.2f), 0f, Mathf.Sin(ang) * (r + 0.2f)); }, shotRadius * 1.2f);      // r3: outside the ring, wider - the ring hid it
                    track.Length = r * Mathf.PI * 2f; track.Seed = 0.15f; track.Flat = true; track.Glow = 0.5f; _self.Add(track);
                    break;
                }
            }
        }

        void AnimateSelf(float a)
        {
            float u = (a - _selfHold) / (bite == VineBite.Parry ? 0.25f : unravelSeconds);      // r2 (Fable 거 r1 #1): the parry bursts away fast
            if (bite == VineBite.Parry && u > 0f && !_burst)
            {
                _burst = true;
                for (int k = 0; k < 3; k++) SpellFxLabShot.InkBurst(_origin + Vector3.up * (0.7f + 0.35f * k) + _hitNormal * 0.35f, Vector3.up, SpellFxBurstBody.Water);
            }
            foreach (var s in _self)
            {
                s.Head = Smooth((a - s.Seed) / _selfGrow);
                if (bite == VineBite.Parry && s.Kind == Kind.Coil) s.Glow = 0.25f + 0.75f * Mathf.Clamp01(1f - (a - 0.1f) / 0.4f);
                if (u > 0f) { s.Tail = Smooth(u) * 0.4f; s.Fade = 1f - Smooth(u * 1.1f); }
            }
            for (int m = 0; m < _motes.Count; m++) _motes[m].T = Mathf.Repeat(m / (float)_motes.Count + a * 0.31f, 0.95f);
            if (_flashGo != null)
            {
                float f = Flash(a - _hitAge) * (bite == VineBite.Parry ? 1f : 0.5f);
                _flashGo.SetActive(f > 0.01f);
                if (f > 0.01f)
                {
                    _flashGo.transform.position = _hitPos;
                    if (_cam != null) _flashGo.transform.rotation = Quaternion.LookRotation(_hitPos - _cam.position, Vector3.up);
                    _flashGo.transform.localScale = Vector3.one * flashSize;
                    _flashMat.SetFloat("_Flash", f);
                }
            }
        }

        // ------------------------------------------------------------------ ㅗ 면: the vine over the area in front (2026-10-30)

        bool IsFan => bite >= VineBite.Sweep;
        readonly List<Strand> _fan = new List<Strand>();
        readonly List<Vector3> _runEnds = new List<Vector3>();
        float _fanDepth = 6f, _fanHalf = 0.75f, _fanHold;
        Vector3 _fanFwd;

        static float FanHoldOf(VineBite b) { switch (b) { case VineBite.Thicket: return 3.2f; case VineBite.FanSeed: case VineBite.RingSeed: return 8f; case VineBite.FanReturn: return 1.2f; case VineBite.RingReturn: return 1.4f; case VineBite.RingCut: return 10f; case VineBite.RingPillar: return 10f; default: return 2.2f; } }

        /// <summary>The fan verbs: runners race out along the ground over the wedge in front (the rule's fan: depth / half angle), then the verb's own
        /// shape: 고 binds what is inside · 곡 a thicket rises (and the wedge grows x1.5) · 곤 the rim reaches on to the bodies beside · 곰 seeds dotted on the
        /// ground · 곳 straight cuts fanned in the ground · 공 the runners sweep out and curl back. Bodies inside are coiled as the runner reaches them.</summary>
        void SpawnFan(SpellFxCast cast, Vector3 fwd)
        {
            _fanFwd = fwd;
            if (bite >= VineBite.RingPillar) { SpawnRing(cast, fwd); return; }
            if (cast.Spec != null && cast.Spec.FanDepth > 0.5f) { _fanDepth = Mathf.Min(9f, cast.Spec.FanDepth); _fanHalf = Mathf.Clamp(Mathf.Max(0.78f, cast.Spec.FanHalfAngleRad), 0.3f, 1.2f); }
            _fanHold = FanHoldOf(bite);
            _hit = true; _hitAge = 0.05f; _hitNormal = -fwd;
            _feet = _origin; _feetBelow = 1f; _target = _origin + fwd * 2f;
            _hitPos = _origin + fwd * 1.2f + Vector3.up * 0.3f;               // a soft flash at the ground in front as it bursts out
            Vector3 g0 = _origin + Vector3.up * 0.04f;
            // r2 (Fable 면 r1: the area did not read): the wedge's own outline, faint jade on the ground - two edges and the far arc
            for (int e = 0; e < 3; e++)
            {
                int ee = e;
                var edge = Add(Kind.Root, t =>
                {
                    if (ee < 2) { float a = (ee == 0 ? -1f : 1f) * _fanHalf; return g0 + Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * fwd * (1.3f + _fanDepth * t); }
                    float an = Mathf.Lerp(-_fanHalf, _fanHalf, t); return g0 + Quaternion.AngleAxis(an * Mathf.Rad2Deg, Vector3.up) * fwd * (1.3f + _fanDepth);
                }, shotRadius * 0.35f);
                edge.Length = e < 2 ? _fanDepth : _fanDepth * _fanHalf * 2f; edge.Seed = 0.05f + 0.1f * e; edge.Grow = 0.35f; edge.Flat = true; edge.Glow = 0.55f;
                _fan.Add(edge);
            }
            int n = bite == VineBite.FanSeed ? 0 : 9;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Lerp(-_fanHalf, _fanHalf, i / (float)(n - 1)) + (Rand() - 0.5f) * 0.08f, sd = Rand();
                Vector3 dir = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * fwd, side = Vector3.Cross(Vector3.up, dir);
                float depth = _fanDepth * (0.82f + 0.18f * Rand()) * (bite == VineBite.Thicket ? 1.5f : 1f);
                bool cut = bite == VineBite.FanCut;
                var run = Add(cut ? Kind.Crack : Kind.Root, t =>
                {
                    float tt = t;
                    // 공: the head itself goes out and comes back (AnimateFan) - the path is one way
                    Vector3 p = g0 + dir * (1.3f + depth * tt) + side * ((cut ? 0.02f : 0.18f) * Mathf.Sin(tt * 7f + sd * 6f) * tt);
                    
                    if (!cut) p.y += 0.03f + 0.05f * Mathf.Abs(Mathf.Sin(tt * 11f + sd * 4f));
                    return p;
                }, shotRadius * (cut ? 0.7f : 0.75f));
                run.Length = depth; run.Seed = 0.02f * i; run.Grow = bite == VineBite.FanReturn ? 0.4f : 0.32f; run.Flat = true;      // r2 (Fable 면 r1: "a reed fence"): every runner is a flat ink flow on the ground run.Glow = cut ? 0.7f : 0.3f;
                _fan.Add(run); _runEnds.Add(g0 + dir * (1.3f + depth)); run.Glow = cut ? 0.7f : 0.5f;     // r1: start 1.3 m out (under the caster it read as a bundle), jade edges on the red soil
                for (int q = 0; q < 2; q++) _leaves.Add(new Leaf { On = run, T = 0.3f + 0.35f * q + 0.1f * Rand(), Side = ((i + q) & 1) == 0 ? 1f : -1f, Size = leafSize * 1.8f, Seed = Rand(), Lying = true });
                if (bite == VineBite.Thicket)
                    for (int k = 0; k < 2; k++)
                    {
                        // stalks rising along the runner: a small forest
                        float at = 0.3f + 0.35f * k + 0.1f * Rand(), h = 0.7f + 0.8f * Rand(), ph = Rand() * 6f;
                        Vector3 b = g0 + dir * (1.3f + depth * at);
                        var stalk = Add(Kind.Cage, t => b + Vector3.up * (h * t) + side * (Mathf.Sin(t * 4f + ph) * 0.08f * t) + side * (Mathf.Sin(_age * 2.2f + ph) * 0.05f * t), shotRadius * 0.9f);
                        stalk.Length = h; stalk.Seed = 0.25f + at * 0.35f; stalk.Grow = 0.6f; _fan.Add(stalk);
                        _leaves.Add(new Leaf { On = stalk, T = 0.55f + 0.3f * Rand(), Side = (k & 1) == 0 ? 1f : -1f, Size = leafSize * 1.6f, Seed = Rand() });
                    }
                if (bite == VineBite.Sweep || bite == VineBite.FanSpread)
                {
                    // the runner's end curls up
                    Vector3 e = g0 + dir * (1.3f + depth); float ph = Rand() * 6f;
                    var tip = Add(Kind.Curl, t => { float ang = t * 4f + ph; return e + Vector3.up * (0.45f * t) + side * (Mathf.Sin(ang) * 0.12f) + dir * ((1f - Mathf.Cos(ang)) * 0.08f); }, shotRadius * 0.5f);
                    tip.Length = 0.5f; tip.Seed = 0.3f; tip.Grow = 0.25f; _fan.Add(tip);
                }
            }
            if (bite == VineBite.FanSeed)
                for (int i = 0; i < 14; i++)
                {
                    // seeds dotted over the wedge: a knot and a short column of light each (r2: 14, spread over the whole wedge)
                    float a = Mathf.Lerp(-_fanHalf, _fanHalf, Rand()), d = Mathf.Lerp(1.5f, _fanDepth, Rand());
                    Vector3 at = g0 + Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * fwd * d + Vector3.up * 0.06f; float ph = Rand() * 6f;
                    var knot = Add(Kind.Curl, t => { float ang = t * 9f + ph; float rr = 0.09f * (1f - 0.4f * t); return at + new Vector3(Mathf.Cos(ang) * rr, 0.02f + 0.04f * t, Mathf.Sin(ang) * rr); }, shotRadius * 0.7f);
                    knot.Length = 0.55f; knot.Seed = 0.05f * i; knot.Grow = 0.3f; _fan.Add(knot);
                    var col = Add(Kind.Pillar, t => at + Vector3.up * (t * 0.9f), shotRadius * 0.45f);
                    col.Length = 0.9f; col.Seed = 0.15f + 0.05f * i; col.Grow = 0.4f; col.Glow = 0.8f; _fan.Add(col);
                    _leaves.Add(new Leaf { On = col, T = 0.97f, Side = 1f, Size = leafSize * 0.8f, Seed = Rand() });
                }
        }

        /// <summary>ㅜ 둘레 with a final (국 군 굼 굿 궁 - the bare ㅜ is the hexagon ward): a vine ring on the ground round the cast point (the rule's ring
        /// radius), drawn round from the front; 국 pillars rise on it · 군 it reaches out to bodies crossing it · 굼 seeds sit on it · 굿 it is a cut line in the
        /// ground · 궁 it swells out to 4 m and comes back.</summary>
        void SpawnRing(SpellFxCast cast, Vector3 fwd)
        {
            float R = cast.Spec != null && cast.Spec.RingRadius > 0.5f ? cast.Spec.RingRadius : 2f;
            _fanDepth = R; _fanHold = FanHoldOf(bite);
            _hit = true; _hitAge = 0.05f; _hitNormal = -fwd;
            _feet = _origin; _feetBelow = 1f; _target = _origin;
            _hitPos = _origin + fwd * R + Vector3.up * 0.3f;
            Vector3 c = _origin + Vector3.up * 0.04f;
            float a0 = Mathf.Atan2(fwd.z, fwd.x), sd = Rand();
            bool cut = bite == VineBite.RingCut;
            System.Func<float, float> radius = t => bite == VineBite.RingReturn ? Mathf.Lerp(R, 4f, Mathf.Sin(Mathf.PI * Mathf.Clamp01((_age - 0.35f) / 1.1f))) : R;
            for (int k = 0; k < (cut ? 1 : 2); k++)
            {
                float kk = k, dir = k == 0 ? 1f : -1f;
                // two halves drawn from the front round to the back (so the camera sees it close at once)
                var half = Add(cut ? Kind.Crack : Kind.Root, t =>
                {
                    float ang = a0 + dir * t * (cut ? Mathf.PI * 2f : Mathf.PI);
                    float rr = radius(t) * (1f + 0.04f * Mathf.Sin(t * 13f + sd * 6f + kk));
                    Vector3 p = c + new Vector3(Mathf.Cos(ang) * rr, 0f, Mathf.Sin(ang) * rr);
                    if (!cut) p.y += 0.04f + 0.05f * Mathf.Abs(Mathf.Sin(t * 17f + kk * 3f));
                    return p;
                }, shotRadius * (cut ? 0.8f : 0.85f));
                half.Length = R * (cut ? Mathf.PI * 2f : Mathf.PI); half.Seed = 0.02f; half.Grow = 0.45f; half.Flat = cut; half.Glow = cut ? 0.75f : 0.5f;
                _fan.Add(half);
                for (int i = 0; i < 3; i++) _leaves.Add(new Leaf { On = half, T = 0.15f + 0.3f * i + 0.05f * Rand(), Side = (i & 1) == 0 ? 1f : -1f, Size = leafSize * 1.4f, Seed = Rand() });
            }
            for (int i = 0; i < 12; i++) { float ang = a0 + i / 12f * Mathf.PI * 2f; _runEnds.Add(c + new Vector3(Mathf.Cos(ang) * R, 0.3f, Mathf.Sin(ang) * R)); }
            if (bite == VineBite.RingPillar)
                for (int i = 0; i < 7; i++)
                {
                    // 국: pillars of root rising on the ring (a footing to stand on), leaves and a curled top
                    float ang = a0 + (i - 3) * 0.55f, h = 1.0f + 0.7f * Rand(), ph = Rand() * 6f;
                    Vector3 b = c + new Vector3(Mathf.Cos(ang) * R, 0f, Mathf.Sin(ang) * R);
                    var pil = Add(Kind.Cage, t => b + Vector3.up * (h * t) + new Vector3(Mathf.Sin(t * 5f + ph) * 0.06f, 0f, Mathf.Cos(t * 5f + ph) * 0.06f) * t, shotRadius * 1.6f);
                    pil.Length = h; pil.Seed = 0.35f + 0.06f * i; pil.Grow = 0.7f; _fan.Add(pil);
                    _leaves.Add(new Leaf { On = pil, T = 0.5f + 0.3f * Rand(), Side = (i & 1) == 0 ? 1f : -1f, Size = leafSize * 1.6f, Seed = Rand() });
                }
            if (bite == VineBite.RingSeed)
                for (int i = 0; i < 6; i++)
                {
                    float ang = a0 + (i - 2.5f) * 0.6f; float ph = Rand() * 6f;
                    Vector3 at = c + new Vector3(Mathf.Cos(ang) * R, 0.06f, Mathf.Sin(ang) * R);
                    var knot = Add(Kind.Curl, t => { float an = t * 9f + ph; float rr = 0.09f * (1f - 0.4f * t); return at + new Vector3(Mathf.Cos(an) * rr, 0.02f + 0.04f * t, Mathf.Sin(an) * rr); }, shotRadius * 0.7f);
                    knot.Length = 0.55f; knot.Seed = 0.3f + 0.05f * i; knot.Grow = 0.3f; _fan.Add(knot);
                    var col = Add(Kind.Pillar, t => at + Vector3.up * (t * 0.9f), shotRadius * 0.45f);
                    col.Length = 0.9f; col.Seed = 0.4f + 0.05f * i; col.Grow = 0.4f; col.Glow = 0.8f; _fan.Add(col);
                    _leaves.Add(new Leaf { On = col, T = 0.97f, Side = 1f, Size = leafSize * 0.9f, Seed = Rand() });
                }
        }

        readonly List<SpellFxHit> _fanHits = new List<SpellFxHit>();

        /// <summary>A body inside the fan: it is coiled when the runners reach its distance (the bind), or - 곤 - it is one of the bodies the rim reaches on to.</summary>
        void FanHit(SpellFxHit hit) { _fanHits.Add(hit); }

        void AnimateFan(float a)
        {
            float u = (a - _fanHold) / unravelSeconds;
            if ((bite == VineBite.Thicket || bite == VineBite.RingPillar) && _matLive) u = -1f;
            foreach (var s in _fan)
            {
                s.Head = Smooth((a - s.Seed) / s.Grow);
                if (bite == VineBite.FanReturn && s.Kind == Kind.Root && s.Flat) s.Head = Smooth((a - s.Seed) / s.Grow) * (1f - 0.88f * Smooth((a - s.Seed - s.Grow - 0.15f) / 0.45f));      // r2: out, then the head runs home
                if (s.Kind == Kind.Pillar) s.Glow = 0.65f + 0.3f * Mathf.Sin(a * 6.28f + s.Seed * 20f);
                if (u > 0f) { s.Tail = Mathf.Max(s.Tail, Smooth(u) * 0.5f); s.Fade = 1f - Smooth(u * 1.1f); }
            }
            // bodies reached: a coil each (the path's ring rule), timed to the runners' arrival at their distance; 곤's rim bodies get a hop from the nearest runner end
            for (int k = _fanHits.Count - 1; k >= 0; k--)
            {
                var h = _fanHits[k];
                float d = Vector3.Distance(new Vector3(h.Feet.x, _origin.y, h.Feet.z), _origin);
                float arrive = 0.05f + Mathf.Clamp01(d / Mathf.Max(1f, _fanDepth)) * 0.32f;
                if (a < arrive) continue;
                _fanHits.RemoveAt(k);
                if (bite == VineBite.FanSeed || bite == VineBite.RingSeed) continue;                    // seeds wait - nothing binds until something walks in (the lab has no such step)
                bool rim = (bite == VineBite.FanSpread && d > _fanDepth * 0.95f) || bite == VineBite.RingSpread;
                Vector3 from = _origin;
                if (rim) { float best = float.MaxValue; foreach (var e in _runEnds) { float dd = (e - h.Position).sqrMagnitude; if (dd < best) { best = dd; from = e; } } }
                FanCoil(h, rim ? from : (Vector3?)null);
                if (bite == VineBite.FanCut || bite == VineBite.RingCut) for (int q = 0; q < 2; q++) SpellFxLabShot.InkBurst(h.Position + _hitNormal * 0.3f + Vector3.up * (0.1f + 0.15f * q), Quaternion.AngleAxis(-30f + 60f * q, Vector3.up) * _hitNormal, SpellFxBurstBody.Armour);
            }
            for (int k = _arrivals.Count - 1; k >= 0; k--)
                if (a >= _arrivals[k].At) { SpellFxLabShot.InkBurst(_arrivals[k].Pos, _arrivals[k].Dir, SpellFxBurstBody.Water); Spark(_arrivals[k].Pos + _arrivals[k].Dir * 0.3f); _arrivals.RemoveAt(k); }
            for (int k = 0; k < _sparks.Count; k++)
            {
                var sp = _sparks[k]; float f = Flash(a - sp.At);
                sp.Go.SetActive(f > 0.01f);
                if (f <= 0.01f) continue;
                if (_cam != null) sp.Go.transform.rotation = Quaternion.LookRotation(sp.Go.transform.position - _cam.position, Vector3.up);
                sp.Go.transform.localScale = Vector3.one * flashSize * (0.6f + 0.6f * Mathf.Clamp01((a - sp.At) / flashSeconds));
                sp.Mat.SetFloat("_Flash", f);
            }
            if (_flashGo != null) _flashGo.SetActive(false);
        }

        /// <summary>A fan's bind on one body: the same closed ring round its neck the path uses, facing the caster; from = a runner end for 곤's hop.</summary>
        void FanCoil(SpellFxHit hit, Vector3? from)
        {
            float tR = Mathf.Clamp(hit.TargetRadius, 0.25f, 0.9f), r = (tR * 0.75f + 0.05f) * 1.1f * 1.4f;
            Transform tr = hit.Target; Vector3 off = tr != null ? hit.Position - tr.position : Vector3.zero;
            float fb = Mathf.Max(0.2f, hit.Position.y - GroundY(hit.Position, hit.Target)), up2 = Mathf.Max(0.4f, fb * 0.75f) - fb + 0.25f;
            System.Func<Vector3> now = () => (tr != null ? tr.position + off : hit.Position) + Vector3.up * up2;
            Vector3 ax = -_fanFwd, sd = Vector3.Cross(Vector3.up, ax).normalized;
            float delay = 0f;
            if (from.HasValue)
            {
                Vector3 f0 = from.Value;
                var hop = Add(Kind.Jump, t => Vector3.Lerp(f0, now(), t) + Vector3.up * (Mathf.Sin(Mathf.PI * t) * 0.35f), shotRadius * 0.6f);
                hop.Length = Vector3.Distance(f0, now()) + 0.3f; hop.Seed = _age; hop.Grow = 0.14f; hop.Glow = 0.2f; _fan.Add(hop);
                delay = 0.14f;
            }
            var c = Add(Kind.Coil, t =>
            {
                float ang = Mathf.PI * 0.5f + t * 1.5f * 2f * Mathf.PI;
                return now() + ax * Mathf.Lerp(0.2f, -0.4f, t) + sd * (Mathf.Cos(ang) * r) + Vector3.up * (Mathf.Sin(ang) * r * 0.8f);
            }, shotRadius);
            c.Length = 1.5f * 2f * Mathf.PI * r; c.Seed = _age + delay + HitStop; c.Grow = coilGrowSeconds; c.Glow = 0.3f; _fan.Add(c);
            for (int i = 0; i < 2; i++) _leaves.Add(new Leaf { On = c, T = 0.2f + 0.4f * i, Side = (i & 1) == 0 ? 1f : -1f, Size = leafSize * 1.4f, Seed = Rand() });
            _arrivals.Add(new Arrival { At = _age + delay, Pos = now(), Dir = -_fanFwd });
        }

        sealed class SparkCard { public GameObject Go; public Material Mat; public float At; }
        readonly List<SparkCard> _sparks = new List<SparkCard>();

        /// <summary>One more bite flash at <paramref name="at"/> (the spread's other bodies): a copy of the flash card with its own clock.</summary>
        void Spark(Vector3 at)
        {
            if (flashMaterial == null) return;
            var go = MakeChild("VineSpark", flashMaterial, out var mesh, out var mat, VfxAfterFog308.Layer);
            mesh.vertices = _flashMesh.vertices; mesh.uv = _flashMesh.uv; mesh.triangles = _flashMesh.triangles; mesh.RecalculateBounds();
            go.transform.position = at;
            _sparks.Add(new SparkCard { Go = go, Mat = mat, At = _age });
        }

        struct Arrival { public float At; public Vector3 Pos, Dir; }
        readonly List<Arrival> _arrivals = new List<Arrival>();

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
                // r4 (Fable g r1-r3, the "plank" by the hand three times): the shot never draws its first 0.5 m - next to the camera any tube reads as a board
                float t0 = s.Kind == Kind.Shot ? Mathf.Max(s.Tail, Mathf.Min(0.5f, 0.25f / Mathf.Max(0.5f, s.Length))) : s.Tail;
                float t1 = s.Head;      // r6 (Fable 강 r5 #1): the return reaches the hand itself (it tapers there)
                if (t1 - t0 < 0.002f || s.Fade <= 0.002f) continue;
                int segs = Mathf.Clamp(Mathf.CeilToInt(s.Length * (t1 - t0) * 16f), 4, 110);
                Vector3 prevT = Vector3.zero, normal = Vector3.zero;
                int baseV = _v.Count;
                for (int i = 0; i <= segs; i++)
                {
                    float u = Mathf.Lerp(t0, t1, i / (float)segs);
                    Vector3 p = s.At(u), q = s.At(Mathf.Min(1f, u + 0.01f)), pp = s.At(Mathf.Max(0f, u - 0.01f));
                    Vector3 tan = (q - pp); if (tan.sqrMagnitude < 1e-8f) tan = prevT; tan.Normalize();
                    if (i == 0) { normal = Vector3.Cross(tan, Vector3.up); if (normal.sqrMagnitude < 1e-4f) normal = Vector3.Cross(tan, Vector3.right); normal.Normalize(); }
                    else { normal = Vector3.ProjectOnPlane(normal, tan).normalized; }          // parallel transport (no twist)
                    Vector3 bin = Vector3.Cross(tan, normal);
                    prevT = tan;
                    // taper: the head comes to a point, the tail thins as it pulls in
                    float headTaper = Mathf.Clamp01((s.Head - u) / Mathf.Max(0.02f, 0.25f / Mathf.Max(0.5f, s.Length)));
                    float tailTaper = t0 > 0f ? Mathf.Clamp01((u - t0) / 0.08f) : 1f;
                    float startTaper = s.Kind == Kind.Shot ? Mathf.Lerp(0.15f, 1f, Smooth((u - t0) / 0.1f)) : s.Kind == Kind.Back ? Mathf.Lerp(1f, 0.15f, Smooth((u - (t1 - 0.1f)) / 0.1f)) : 1f;   // the shot grows out of nothing at 0.5 m (강: and fades there coming home)
                    float rootTaper = s.Kind == Kind.Curl || s.Kind == Kind.Root || s.Kind == Kind.Stem ? Mathf.Lerp(1f, 0.35f, u) : s.Seed == -2f ? Mathf.Lerp(1.8f, 0.4f, u)          // Seed -2: the ground line widens toward the camera
                        : s.Kind == Kind.Cage ? Mathf.Lerp(1.3f, 0.35f, u) * (1f + 0.25f * Mathf.Exp(-Mathf.Pow((u - 0.4f - 0.2f * (s.Radius * 37f % 1f)) * 12f, 2f))) : 1f;   // r3 (Fable 각 r2 #1): a brush stroke - thick foot, fine tip, one node
                    float rad = s.Radius * Mathf.Sqrt(headTaper) * Mathf.Lerp(0.4f, 1f, tailTaper) * rootTaper * startTaper;
                    for (int j = 0; j < Around; j++)
                    {
                        float ang = j / (float)Around * Mathf.PI * 2f;
                        Vector3 dir = normal * Mathf.Cos(ang) + bin * Mathf.Sin(ang);
                        Vector3 off = dir * rad;
                        if (s.Flat) { off.y *= 0.12f; off.x *= 2.2f; off.z *= 2.2f; }
                        _v.Add(toLocal.MultiplyPoint3x4(p + off));
                        _n.Add(toLocal.MultiplyVector(s.Flat ? Vector3.Lerp(dir, Vector3.up, 0.6f).normalized : dir));
                        _uv.Add(new Vector2(u * s.Length, j / (float)Around));
                        _c.Add(new Color(s.Glow, s.Flat ? 0.95f : (int)s.Kind / 10f, u, s.Fade));      // g 0.95 = a flat ground mark (the shader draws it as a decal)
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
            _cam = Camera.main != null ? Camera.main.transform : null;
            _v.Clear(); _n.Clear(); _uv.Clear(); _c.Clear(); _t.Clear();
            foreach (var l in _leaves)
            {
                var s = l.On;
                if (s.Head < l.T || s.Tail > l.T || s.Fade <= 0.01f) continue;
                float open = Smooth((s.Head - l.T) / 0.15f) * (s.Tail > 0f ? 1f - Smooth((s.Tail - l.T + 0.1f) / 0.1f) : 1f);
                if (open <= 0.01f) continue;
                Vector3 p = s.At(l.T), tan = (s.At(Mathf.Min(1f, l.T + 0.02f)) - s.At(Mathf.Max(0f, l.T - 0.02f))).normalized;
                Vector3 side = Vector3.Cross(tan, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.right; side.Normalize();
                Vector3 dir = l.Along ? tan : (side * l.Side * 0.8f + Vector3.up * 0.5f + tan * 0.3f).normalized;   // the leaf's long axis (Along: a blade laid along its strand)
                dir = Quaternion.AngleAxis((l.Seed - 0.5f) * 80f, tan) * dir;                       // r6 (Fable 각 r3 #5): ±40° each, no flat sticker row
                // r7 (감's leaves unseen three times): on a curve that faces the camera, cross(dir, tan) put the blade edge-on - turn each leaf about its
                // long axis toward the camera (a little of the old tilt kept so they do not read as stickers)
                Vector3 wid = Vector3.Cross(dir, tan).normalized;
                if (l.Lying)
                {
                    // a leaf lying on the ground beside its runner (the fan's flowing look)
                    dir = (tan * 0.55f + side * l.Side * 0.85f); dir.y = 0f; dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : side;
                    wid = Vector3.Cross(Vector3.up, dir).normalized; p += Vector3.up * 0.02f;
                }
                else if (_cam != null)
                {
                    Vector3 toCam = (_cam.position - p).normalized, face = Vector3.Cross(dir, toCam);
                    if (face.sqrMagnitude > 1e-4f) wid = Vector3.Slerp(wid, face.normalized, 0.75f).normalized;
                }
                float len = l.Size * open, w = l.Size * 0.45f * open * l.Width;
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
